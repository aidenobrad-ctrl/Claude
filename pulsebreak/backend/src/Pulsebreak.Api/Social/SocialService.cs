using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pulsebreak.Api.Data;
using Pulsebreak.Api.Matchmaking;
using Pulsebreak.Api.Progression;
using Pulsebreak.Shared.Social;
using StackExchange.Redis;

namespace Pulsebreak.Api.Social;

/// <summary>Why a player can be reported.</summary>
public static class ReportReasons
{
    public const string Cheating = "cheating", AbusiveChat = "abusive_chat", OffensiveName = "offensive_name", Griefing = "griefing", Other = "other";
    public static readonly string[] All = [Cheating, AbusiveChat, OffensiveName, Griefing, Other];
}

/// <summary>Where a player is, as their friends see it.</summary>
public static class Presence
{
    public const string Offline = "offline", Menu = "menu", Match = "match", Range = "range";
    public static string Clean(string? status) => status is Menu or Match or Range ? status : Menu;
}

public sealed record PersonDto(string AccountId, string Username, string Status, bool InParty);
public sealed record PartyMemberDto(string AccountId, string Username, bool Leader, string Status);
/// <summary>The leader's search for the party: its ticket, playlist, "searching" or "matched", and when it began (seconds since 1970).</summary>
public sealed record PartySearchDto(string TicketId, string Playlist, string Status, long StartedAt);
public sealed record PartyDto(string Id, string LeaderId, PartyMemberDto[] Members, PartySearchDto? Search, int MaxSize);
public sealed record InviteDto(string PartyId, string FromId, string FromName, long SentAt);
public sealed record ChatLineDto(long Seq, string AccountId, string Username, string Text, long At);
public sealed record SocialDto(PersonDto[] Friends, PersonDto[] Incoming, PersonDto[] Outgoing, PersonDto[] Blocked, PartyDto? Party,
    InviteDto[] Invites, ChatLineDto[] Chat, long ChatCursor, SeasonDto Season);
public sealed record ReportRequest(string? AccountId, string? Reason, string? Details, string? MatchId, string[]? Chat);

/// <summary>A party as matchmaking needs it: who leads, and everyone in it.</summary>
public sealed record PartyInfo(string Id, string LeaderId, IReadOnlyList<(string Key, string Name)> Members, string SearchTicket);

public enum SocialResult
{
    Ok,
    NotFound,
    Self,
    Blocked,
    AlreadyFriends,
    TooManyFriends,
    NotFriends,
    PartyFull,
    NotLeader,
    NoInvite,
    NotInParty,
    RateLimited,
    Invalid,
}

/// <summary>
/// Friends, blocks, reports, who is online, parties and party chat.
///
/// Friendships, blocks and reports are kept in PostgreSQL. Everything that only matters
/// while people are playing (who is online, parties, invites, party chat) lives in Redis and
/// fades on its own: presence lasts a little longer than the game's check-in interval, and a
/// party member whose game stops checking in is dropped from the party by the next one that does.
///
/// A blocked player cannot send a friend request or an invite to the one who blocked them,
/// and their party chat is not shown to them. Blocking also ends any friendship.
/// </summary>
public sealed class SocialService(
    AppDbContext db, IConnectionMultiplexer redis, IOptions<SocialOptions> options, Seasons seasons, MatchmakingService matchmaking,
    TimeProvider clock)
{
    const string Prefix = "sc:";
    static readonly TimeSpan PartyLife = TimeSpan.FromHours(2);
    const int ChatKept = 50;

    readonly SocialOptions _options = options.Value;
    IDatabase Redis => redis.GetDatabase();
    long Now => clock.GetUtcNow().ToUnixTimeSeconds();

    static string PresenceKey(Guid id) => Prefix + "pr:" + id;
    static string PartyOfKey(Guid id) => Prefix + "po:" + id;
    static string PartyKey(string party) => Prefix + "p:" + party;
    static string MembersKey(string party) => Prefix + "p:" + party + ":m";
    static string NamesKey(string party) => Prefix + "p:" + party + ":n";
    static string ChatKey(string party) => Prefix + "p:" + party + ":c";
    static string ChatSeqKey(string party) => Prefix + "p:" + party + ":s";
    static string InvitesKey(Guid id) => Prefix + "inv:" + id;
    static string RateKey(Guid id) => Prefix + "rl:" + id + ":" + (DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60);

    // ------------------------------------------------------------------ the poll

    /// <summary>
    /// Everything the game shows about a player's social life, in one call, which also marks
    /// them online. <paramref name="chatAfter"/> is the last party chat line already seen.
    /// </summary>
    public async Task<SocialDto> PollAsync(Account me, string? status, long chatAfter)
    {
        IDatabase r = Redis;
        await r.StringSetAsync(PresenceKey(me.Id), Presence.Clean(status), TimeSpan.FromSeconds(Math.Max(10, _options.PresenceSeconds)));

        List<FriendshipRow> links = await db.Friendships.Where(f => f.RequesterId == me.Id || f.AddresseeId == me.Id).ToListAsync();
        List<Guid> blockedIds = await db.Blocks.Where(b => b.OwnerId == me.Id).Select(b => b.BlockedId).ToListAsync();
        var ids = links.Select(f => f.RequesterId == me.Id ? f.AddresseeId : f.RequesterId).Concat(blockedIds).Distinct().ToList();
        Dictionary<Guid, string> names = await db.Accounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Username);
        Dictionary<Guid, string> statuses = await StatusesAsync(links.Where(f => f.Accepted).Select(f => f.RequesterId == me.Id ? f.AddresseeId : f.RequesterId));

        PersonDto Person(Guid id, bool withStatus) =>
            new(id.ToString(), names.GetValueOrDefault(id, "?"), withStatus ? statuses.GetValueOrDefault(id, Presence.Offline) : Presence.Offline, false);

        var friends = new List<PersonDto>();
        var incoming = new List<PersonDto>();
        var outgoing = new List<PersonDto>();
        foreach (FriendshipRow link in links)
        {
            Guid other = link.RequesterId == me.Id ? link.AddresseeId : link.RequesterId;
            if (!names.ContainsKey(other)) continue;
            if (link.Accepted) friends.Add(Person(other, true));
            else if (link.AddresseeId == me.Id) incoming.Add(Person(other, false));
            else outgoing.Add(Person(other, false));
        }

        string? partyId = await PartyIdAsync(me.Id);
        if (partyId != null)
        {
            await TouchPartyAsync(partyId);
            partyId = await PartyIdAsync(me.Id);
        }
        PartyDto? party = partyId != null ? await PartyDtoAsync(partyId) : null;
        if (party != null)
        {
            var inParty = party.Members.Select(m => m.AccountId).ToHashSet();
            friends = friends.Select(f => f with { InParty = inParty.Contains(f.AccountId) }).ToList();
        }

        // Invites still in time, from people not since blocked.
        var invites = new List<InviteDto>();
        foreach (HashEntry entry in await r.HashGetAllAsync(InvitesKey(me.Id)))
        {
            InviteDto? invite = JsonSerializer.Deserialize<InviteDto>(entry.Value.ToString(), JsonSerializerOptions.Web);
            if (invite == null || Now - invite.SentAt > _options.InviteSeconds || entry.Name == partyId ||
                (Guid.TryParse(invite.FromId, out Guid from) && blockedIds.Contains(from)) || !await r.KeyExistsAsync(PartyKey(entry.Name!)))
            {
                await r.HashDeleteAsync(InvitesKey(me.Id), entry.Name);
                continue;
            }
            invites.Add(invite);
        }

        var chat = new List<ChatLineDto>();
        long cursor = chatAfter;
        if (partyId != null)
        {
            var blocked = blockedIds.Select(b => b.ToString()).ToHashSet();
            foreach (RedisValue line in await r.ListRangeAsync(ChatKey(partyId)))
            {
                ChatLineDto? said = JsonSerializer.Deserialize<ChatLineDto>(line.ToString(), JsonSerializerOptions.Web);
                if (said == null || said.Seq <= chatAfter) continue;
                cursor = Math.Max(cursor, said.Seq);
                if (!blocked.Contains(said.AccountId)) chat.Add(said);
            }
        }

        return new SocialDto(friends.OrderBy(f => f.Status == Presence.Offline).ThenBy(f => f.Username, StringComparer.OrdinalIgnoreCase).ToArray(),
            incoming.ToArray(), outgoing.ToArray(),
            blockedIds.Where(names.ContainsKey).Select(id => new PersonDto(id.ToString(), names[id], Presence.Offline, false)).ToArray(),
            party, invites.ToArray(), chat.ToArray(), cursor, seasons.Now());
    }

    // ------------------------------------------------------------------ friends

    /// <summary>Asks someone to be friends, by username. If they had already asked, this accepts.</summary>
    public async Task<(SocialResult Result, PersonDto? Person)> RequestAsync(Account me, string? username)
    {
        string normalized = (username ?? "").Trim().ToLowerInvariant();
        Account? other = await db.Accounts.FirstOrDefaultAsync(a => a.UsernameNormalized == normalized && !a.Disabled);
        if (other == null) return (SocialResult.NotFound, null);
        if (other.Id == me.Id) return (SocialResult.Self, null);
        if (await BlockedEitherWayAsync(me.Id, other.Id)) return (SocialResult.Blocked, null);

        FriendshipRow? existing = await LinkAsync(me.Id, other.Id);
        if (existing is { Accepted: true }) return (SocialResult.AlreadyFriends, null);
        if (existing != null && existing.AddresseeId == me.Id)
        {
            // They asked first: this is a yes.
            existing.Accepted = true;
            existing.AcceptedAt = clock.GetUtcNow();
            await db.SaveChangesAsync();
            return (SocialResult.Ok, new PersonDto(other.Id.ToString(), other.Username, Presence.Offline, false));
        }
        if (existing == null)
        {
            if (await FriendCountAsync(me.Id) >= _options.MaxFriends) return (SocialResult.TooManyFriends, null);
            db.Friendships.Add(new FriendshipRow { RequesterId = me.Id, AddresseeId = other.Id, CreatedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }
        return (SocialResult.Ok, new PersonDto(other.Id.ToString(), other.Username, Presence.Offline, false));
    }

    public async Task<SocialResult> AcceptAsync(Account me, Guid other)
    {
        FriendshipRow? link = await db.Friendships.FirstOrDefaultAsync(f => f.RequesterId == other && f.AddresseeId == me.Id && !f.Accepted);
        if (link == null) return SocialResult.NotFound;
        if (await FriendCountAsync(me.Id) >= _options.MaxFriends) return SocialResult.TooManyFriends;
        link.Accepted = true;
        link.AcceptedAt = clock.GetUtcNow();
        await db.SaveChangesAsync();
        return SocialResult.Ok;
    }

    /// <summary>Unfriends, declines a request, or takes back one sent: whatever there is between the two.</summary>
    public async Task<SocialResult> RemoveAsync(Account me, Guid other)
    {
        FriendshipRow? link = await LinkAsync(me.Id, other);
        if (link == null) return SocialResult.NotFound;
        db.Friendships.Remove(link);
        await db.SaveChangesAsync();
        return SocialResult.Ok;
    }

    // ------------------------------------------------------------------ blocks and reports

    public async Task<SocialResult> BlockAsync(Account me, Guid other)
    {
        if (other == me.Id) return SocialResult.Self;
        if (!await db.Accounts.AnyAsync(a => a.Id == other)) return SocialResult.NotFound;
        if (!await db.Blocks.AnyAsync(b => b.OwnerId == me.Id && b.BlockedId == other))
            db.Blocks.Add(new BlockRow { OwnerId = me.Id, BlockedId = other, CreatedAt = clock.GetUtcNow() });
        FriendshipRow? link = await LinkAsync(me.Id, other);
        if (link != null) db.Friendships.Remove(link);
        await db.SaveChangesAsync();

        // Any invite between the two goes too.
        string? theirParty = await PartyIdAsync(other);
        if (theirParty != null) await Redis.HashDeleteAsync(InvitesKey(me.Id), theirParty);
        string? myParty = await PartyIdAsync(me.Id);
        if (myParty != null) await Redis.HashDeleteAsync(InvitesKey(other), myParty);
        return SocialResult.Ok;
    }

    public async Task<SocialResult> UnblockAsync(Account me, Guid other)
    {
        BlockRow? block = await db.Blocks.FirstOrDefaultAsync(b => b.OwnerId == me.Id && b.BlockedId == other);
        if (block == null) return SocialResult.NotFound;
        db.Blocks.Remove(block);
        await db.SaveChangesAsync();
        return SocialResult.Ok;
    }

    /// <summary>
    /// Files a report for a person to review. A second report of the same player within the
    /// hour is taken as the same report.
    /// </summary>
    public async Task<SocialResult> ReportAsync(Account me, ReportRequest request)
    {
        if (!Guid.TryParse(request.AccountId, out Guid other) || !ReportReasons.All.Contains(request.Reason ?? "")) return SocialResult.Invalid;
        if (other == me.Id) return SocialResult.Self;
        if (!await db.Accounts.AnyAsync(a => a.Id == other)) return SocialResult.NotFound;

        DateTimeOffset now = clock.GetUtcNow();
        if (await db.Reports.CountAsync(r => r.ReporterId == me.Id && r.CreatedAt > now.AddDays(-1)) >= _options.ReportsPerDay) return SocialResult.RateLimited;
        if (await db.Reports.AnyAsync(r => r.ReporterId == me.Id && r.ReportedId == other && r.CreatedAt > now.AddHours(-1))) return SocialResult.Ok;

        string chat = string.Join("\n", (request.Chat ?? []).Take(10).Select(line => ChatText.Clean(line ?? "")).Where(line => line != null));
        db.Reports.Add(new ReportRow
        {
            Id = Guid.NewGuid(), ReporterId = me.Id, ReportedId = other, Reason = request.Reason!,
            Details = Truncate(ChatText.Clean(request.Details ?? "") ?? "", 500), MatchId = Truncate((request.MatchId ?? "").Trim(), 32),
            Chat = Truncate(chat, 2000), CreatedAt = now,
        });
        await db.SaveChangesAsync();
        return SocialResult.Ok;
    }

    // ------------------------------------------------------------------ parties

    /// <summary>Invites a friend to the party, starting one with this player as leader if there is none.</summary>
    public async Task<SocialResult> InviteAsync(Account me, Guid friend)
    {
        if (friend == me.Id) return SocialResult.Self;
        FriendshipRow? link = await LinkAsync(me.Id, friend);
        if (link is not { Accepted: true }) return SocialResult.NotFriends;
        if (await BlockedEitherWayAsync(me.Id, friend)) return SocialResult.Blocked;

        IDatabase r = Redis;
        await MarkOnlineAsync(me.Id);
        string partyId = await PartyIdAsync(me.Id) ?? await CreatePartyAsync(me);
        if (await r.ListLengthAsync(MembersKey(partyId)) >= _options.MaxPartySize) return SocialResult.PartyFull;
        if ((await r.ListRangeAsync(MembersKey(partyId))).Any(m => m == friend.ToString())) return SocialResult.Ok;

        var invite = new InviteDto(partyId, me.Id.ToString(), me.Username, Now);
        await r.HashSetAsync(InvitesKey(friend), partyId, JsonSerializer.Serialize(invite, JsonSerializerOptions.Web));
        await r.KeyExpireAsync(InvitesKey(friend), TimeSpan.FromSeconds(Math.Max(30, _options.InviteSeconds)));
        return SocialResult.Ok;
    }

    /// <summary>Joins a party one was invited to, leaving any other party first.</summary>
    public async Task<SocialResult> JoinAsync(Account me, string partyId)
    {
        IDatabase r = Redis;
        RedisValue raw = await r.HashGetAsync(InvitesKey(me.Id), partyId);
        if (!raw.HasValue) return SocialResult.NoInvite;
        await r.HashDeleteAsync(InvitesKey(me.Id), partyId);
        InviteDto? invite = JsonSerializer.Deserialize<InviteDto>(raw.ToString(), JsonSerializerOptions.Web);
        if (invite == null || Now - invite.SentAt > _options.InviteSeconds || !await r.KeyExistsAsync(PartyKey(partyId))) return SocialResult.NoInvite;
        if (await r.ListLengthAsync(MembersKey(partyId)) >= _options.MaxPartySize) return SocialResult.PartyFull;

        string? current = await PartyIdAsync(me.Id);
        if (current == partyId) return SocialResult.Ok;
        if (current != null) await LeaveAsync(me);
        await MarkOnlineAsync(me.Id);

        await CancelSearchAsync(partyId);
        await r.ListRightPushAsync(MembersKey(partyId), me.Id.ToString());
        await r.HashSetAsync(NamesKey(partyId), me.Id.ToString(), me.Username);
        await r.StringSetAsync(PartyOfKey(me.Id), partyId, PartyLife);
        await SayAsync(partyId, Guid.Empty, "", me.Username + " joined the party.");
        await TouchPartyAsync(partyId);
        return SocialResult.Ok;
    }

    public async Task<SocialResult> DeclineAsync(Account me, string partyId) =>
        await Redis.HashDeleteAsync(InvitesKey(me.Id), partyId) ? SocialResult.Ok : SocialResult.NoInvite;

    /// <summary>Leaves the party. If the leader goes, the next to have joined leads; a party left with one person ends.</summary>
    public async Task<SocialResult> LeaveAsync(Account me) => await RemoveFromPartyAsync(me.Id, me.Username, " left the party.");

    public async Task<SocialResult> KickAsync(Account me, Guid member)
    {
        string? partyId = await PartyIdAsync(me.Id);
        if (partyId == null) return SocialResult.NotInParty;
        if ((await Redis.HashGetAsync(PartyKey(partyId), "leader")).ToString() != me.Id.ToString()) return SocialResult.NotLeader;
        if (member == me.Id) return SocialResult.Self;
        if (await PartyIdAsync(member) != partyId) return SocialResult.NotFound;
        string name = (await Redis.HashGetAsync(NamesKey(partyId), member.ToString())).ToString();
        return await RemoveFromPartyAsync(member, name, " was removed from the party.");
    }

    /// <summary>Says something to the party.</summary>
    public async Task<SocialResult> ChatAsync(Account me, string? text)
    {
        string? clean = ChatText.Clean(text ?? "");
        if (clean == null) return SocialResult.Invalid;
        string? partyId = await PartyIdAsync(me.Id);
        if (partyId == null) return SocialResult.NotInParty;
        IDatabase r = Redis;
        string rate = RateKey(me.Id);
        long sent = await r.StringIncrementAsync(rate);
        if (sent == 1) await r.KeyExpireAsync(rate, TimeSpan.FromSeconds(70));
        if (sent > _options.ChatPerMinute) return SocialResult.RateLimited;
        await SayAsync(partyId, me.Id, me.Username, clean);
        return SocialResult.Ok;
    }

    /// <summary>The party a player is in, for matchmaking. Null if none.</summary>
    public async Task<PartyInfo?> PartyOfAsync(Guid accountId)
    {
        string? partyId = await PartyIdAsync(accountId);
        if (partyId == null) return null;
        IDatabase r = Redis;
        string leader = (await r.HashGetAsync(PartyKey(partyId), "leader")).ToString();
        Dictionary<string, string> names = (await r.HashGetAllAsync(NamesKey(partyId))).ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
        var members = (await r.ListRangeAsync(MembersKey(partyId))).Select(m => m.ToString()).Select(m => (m, names.GetValueOrDefault(m, "?"))).ToList();
        return new PartyInfo(partyId, leader, members, (await r.HashGetAsync(PartyKey(partyId), "search")).ToString());
    }

    /// <summary>Notes the search the leader started for the party, so its members can follow it. Empty clears it.</summary>
    public async Task SetSearchAsync(string partyId, string ticketId, string playlist) =>
        await Redis.HashSetAsync(PartyKey(partyId), [new("search", ticketId), new("searchPlaylist", playlist)]);

    // ------------------------------------------------------------------ helpers

    /// <summary>Someone acting on a party is plainly online, whether or not their game has checked in yet.</summary>
    Task MarkOnlineAsync(Guid id) =>
        Redis.StringSetAsync(PresenceKey(id), Presence.Menu, TimeSpan.FromSeconds(Math.Max(10, _options.PresenceSeconds)), When.NotExists);

    async Task<string> CreatePartyAsync(Account leader)
    {
        IDatabase r = Redis;
        string partyId = Guid.NewGuid().ToString("N")[..12];
        await r.HashSetAsync(PartyKey(partyId), [new("leader", leader.Id.ToString()), new("created", Now), new("search", ""), new("searchPlaylist", "")]);
        await r.ListRightPushAsync(MembersKey(partyId), leader.Id.ToString());
        await r.HashSetAsync(NamesKey(partyId), leader.Id.ToString(), leader.Username);
        await r.StringSetAsync(PartyOfKey(leader.Id), partyId, PartyLife);
        await TouchPartyAsync(partyId);
        return partyId;
    }

    async Task<SocialResult> RemoveFromPartyAsync(Guid member, string name, string saying)
    {
        string? partyId = await PartyIdAsync(member);
        if (partyId == null) return SocialResult.NotInParty;
        IDatabase r = Redis;
        await CancelSearchAsync(partyId);
        await r.ListRemoveAsync(MembersKey(partyId), member.ToString());
        await r.HashDeleteAsync(NamesKey(partyId), member.ToString());
        await r.KeyDeleteAsync(PartyOfKey(member));

        RedisValue[] left = await r.ListRangeAsync(MembersKey(partyId));
        if (left.Length <= 1)
        {
            // Nobody left to party with: the party ends.
            foreach (RedisValue other in left) await r.KeyDeleteAsync(PartyOfKey(Guid.Parse(other.ToString())));
            await r.KeyDeleteAsync([PartyKey(partyId), MembersKey(partyId), NamesKey(partyId), ChatKey(partyId), ChatSeqKey(partyId)]);
            return SocialResult.Ok;
        }
        if ((await r.HashGetAsync(PartyKey(partyId), "leader")).ToString() == member.ToString())
            await r.HashSetAsync(PartyKey(partyId), "leader", left[0]);
        await SayAsync(partyId, Guid.Empty, "", name + saying);
        return SocialResult.Ok;
    }

    /// <summary>The party's search ends whenever the party changes: it was for the people in it before.</summary>
    async Task CancelSearchAsync(string partyId)
    {
        IDatabase r = Redis;
        string ticket = (await r.HashGetAsync(PartyKey(partyId), "search")).ToString();
        string leader = (await r.HashGetAsync(PartyKey(partyId), "leader")).ToString();
        if (ticket.Length > 0) await matchmaking.CancelAsync(ticket, leader);
        await SetSearchAsync(partyId, "", "");
    }

    async Task SayAsync(string partyId, Guid from, string name, string text)
    {
        IDatabase r = Redis;
        long seq = await r.StringIncrementAsync(ChatSeqKey(partyId));
        var line = new ChatLineDto(seq, from == Guid.Empty ? "" : from.ToString(), name, text, Now);
        await r.ListRightPushAsync(ChatKey(partyId), JsonSerializer.Serialize(line, JsonSerializerOptions.Web));
        await r.ListTrimAsync(ChatKey(partyId), -ChatKept, -1);
    }

    /// <summary>Keeps a party alive while its members are, and drops members whose game has stopped checking in.</summary>
    async Task TouchPartyAsync(string partyId)
    {
        IDatabase r = Redis;
        foreach (RedisValue member in await r.ListRangeAsync(MembersKey(partyId)))
        {
            Guid id = Guid.Parse(member.ToString());
            if (await r.KeyExistsAsync(PresenceKey(id))) await r.KeyExpireAsync(PartyOfKey(id), PartyLife);
            else await RemoveFromPartyAsync(id, (await r.HashGetAsync(NamesKey(partyId), member)).ToString(), " went offline.");
        }
        foreach (string key in new[] { PartyKey(partyId), MembersKey(partyId), NamesKey(partyId), ChatKey(partyId), ChatSeqKey(partyId) })
            await r.KeyExpireAsync(key, PartyLife);
    }

    async Task<PartyDto?> PartyDtoAsync(string partyId)
    {
        IDatabase r = Redis;
        HashEntry[] fields = await r.HashGetAllAsync(PartyKey(partyId));
        if (fields.Length == 0) return null;
        Dictionary<string, string> party = fields.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
        Dictionary<string, string> names = (await r.HashGetAllAsync(NamesKey(partyId))).ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
        var ids = (await r.ListRangeAsync(MembersKey(partyId))).Select(m => m.ToString()).ToList();
        Dictionary<Guid, string> statuses = await StatusesAsync(ids.Select(Guid.Parse));
        string leader = party.GetValueOrDefault("leader", "");
        var members = ids.Select(id => new PartyMemberDto(id, names.GetValueOrDefault(id, "?"), id == leader,
            statuses.GetValueOrDefault(Guid.Parse(id), Presence.Offline))).ToArray();
        // The search, while it is running or has just found a match; once it is over it goes.
        PartySearchDto? searchDto = null;
        string search = party.GetValueOrDefault("search", "");
        if (search.Length > 0)
        {
            MatchTicket? ticket = await matchmaking.PeekAsync(search);
            if (ticket is { Status: MatchTicket.Searching or MatchTicket.Matched })
                searchDto = new PartySearchDto(search, ticket.Playlist, ticket.Status, ticket.CreatedMs / 1000);
            else
                await SetSearchAsync(partyId, "", "");
        }
        return new PartyDto(partyId, leader, members, searchDto, _options.MaxPartySize);
    }

    async Task<string?> PartyIdAsync(Guid accountId)
    {
        RedisValue id = await Redis.StringGetAsync(PartyOfKey(accountId));
        return id.HasValue ? id.ToString() : null;
    }

    async Task<Dictionary<Guid, string>> StatusesAsync(IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().ToList();
        var result = new Dictionary<Guid, string>();
        if (list.Count == 0) return result;
        RedisValue[] values = await Redis.StringGetAsync(list.Select(id => (RedisKey)PresenceKey(id)).ToArray());
        for (int i = 0; i < list.Count; i++) result[list[i]] = values[i].HasValue ? values[i].ToString() : Presence.Offline;
        return result;
    }

    Task<FriendshipRow?> LinkAsync(Guid a, Guid b) =>
        db.Friendships.FirstOrDefaultAsync(f => (f.RequesterId == a && f.AddresseeId == b) || (f.RequesterId == b && f.AddresseeId == a));

    Task<int> FriendCountAsync(Guid id) => db.Friendships.CountAsync(f => f.RequesterId == id || f.AddresseeId == id);

    Task<bool> BlockedEitherWayAsync(Guid a, Guid b) =>
        db.Blocks.AnyAsync(x => (x.OwnerId == a && x.BlockedId == b) || (x.OwnerId == b && x.BlockedId == a));

    static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
