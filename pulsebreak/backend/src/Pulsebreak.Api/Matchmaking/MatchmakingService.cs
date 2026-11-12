using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pulsebreak.Api.Progression;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Net;
using Pulsebreak.Shared.Progression;
using StackExchange.Redis;

namespace Pulsebreak.Api.Matchmaking;

/// <summary>
/// A place in a queue, and once matched, where to go. One player, or a party: the leader is
/// <see cref="Player"/>, and the others are in <see cref="Members"/> ("id|name;id|name").
/// A party is always matched together, on one side.
/// </summary>
public sealed record MatchTicket(
    string Id, string Player, string Name, string Playlist, uint Version, long CreatedMs, long SeenMs,
    string Status, string Address, string JoinTicket, string MatchId, int Rating = Ranks.StartRating, string Members = "")
{
    public const string Searching = "searching", Matched = "matched", Cancelled = "cancelled", Expired = "expired";

    /// <summary>Everyone besides the leader.</summary>
    public IReadOnlyList<(string Key, string Name)> MemberList =>
        Members.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(m => m.Split('|')).Where(p => p.Length == 2).Select(p => (p[0], p[1])).ToList();

    /// <summary>How many people this ticket brings.</summary>
    public int Size => 1 + MemberList.Count;

    public bool Includes(string player) => Player == player || MemberList.Any(m => m.Key == player);

    public static string Encode(IEnumerable<(string Key, string Name)> members) => string.Join(";", members.Select(m => m.Key + "|" + m.Name));
}

/// <summary>A match the matchmaker set up: which server it went to, and what that server was told.</summary>
public sealed record StoredMatch(string ServerId, MatchAssignment Assignment);

/// <summary>A game server offering itself for matches, as it last described itself.</summary>
public sealed record FleetServer(
    string Id, string Address, int Port, uint Version, string State, string ReportedMatch, int Players,
    string AssignedMatch, string AssignedPlaylist)
{
    /// <summary>Free for a new match: nobody on it, and whatever it was last given is finished (or never arrived).</summary>
    public bool Free => State == "idle" && (AssignedMatch.Length == 0 || ReportedMatch == AssignedMatch);
}

/// <summary>
/// Matchmaking, kept in Redis so any number of API processes can share it.
///
/// A player joins a playlist's queue and is handed a ticket id; they ask about it every
/// second or so. Game servers register with a fleet key and check in every few seconds,
/// saying whether anyone is playing on them. Every second the matchmaker (one process at a
/// time, under a lock) takes the oldest players in each queue and, when there are enough of
/// them or the oldest has waited long enough, gives them a free server: the server is told
/// the match's id and playlist on its next check-in, and each player's ticket gets the
/// server's address and a join ticket good for that match only.
///
/// Players whose game version differs from a server's are never sent to it.
///
/// Ranked playlists are for signed-in players with a verified email. They only ever start
/// full, with players close in rating (the range widens the longer the oldest has waited),
/// split into teams as even as their ratings allow. Every match's assignment carries what each
/// player has unlocked, so the server holds loadouts to it.
/// </summary>
public sealed class MatchmakingService(
    IConnectionMultiplexer redis, IOptions<MatchmakingOptions> options, IOptions<GameOptions> game, TimeProvider clock,
    IServiceScopeFactory scopes, ILogger<MatchmakingService> log)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { IncludeFields = true };
    static readonly TimeSpan MatchLife = TimeSpan.FromHours(6);

    const string Prefix = "mm:";
    const string ServersKey = Prefix + "servers";
    const string LockKey = Prefix + "lock";
    static readonly TimeSpan TicketLife = TimeSpan.FromMinutes(10);

    readonly MatchmakingOptions _options = options.Value;
    readonly GameOptions _game = game.Value;
    IDatabase Db => redis.GetDatabase();

    static string TicketKey(string id) => Prefix + "t:" + id;
    static string QueueKey(string playlist) => Prefix + "q:" + playlist;
    static string PlayerKey(string player) => Prefix + "p:" + player;
    static string ServerKey(string id) => Prefix + "s:" + id;
    static string MatchKey(string id) => Prefix + "m:" + id;

    long NowMs => clock.GetUtcNow().ToUnixTimeMilliseconds();
    TimeSpan ServerLife => TimeSpan.FromSeconds(Math.Max(_options.ServerTimeoutSeconds, _options.HeartbeatSeconds * 2));

    public bool FleetEnabled => Encoding.UTF8.GetByteCount(_options.FleetKey) >= 32;

    // ------------------------------------------------------------------ players

    /// <summary>The most people a party can bring to a playlist: a team's worth, or the whole match without teams.</summary>
    public static int MaxGroup(Playlist playlist) => playlist.TeamSize > 0 ? playlist.TeamSize : playlist.Players;

    /// <summary>
    /// Puts a player, or a party led by them, in a playlist's queue. Any place any of them
    /// already had, in any queue, is given up.
    /// </summary>
    /// <param name="rating">The player's rating in this playlist, for ranked; ignored otherwise.</param>
    /// <param name="members">The rest of the party, if this is one.</param>
    public async Task<MatchTicket> EnqueueAsync(Caller caller, Playlist playlist, uint version, int rating = Ranks.StartRating,
        IReadOnlyList<(string Key, string Name)>? members = null)
    {
        IDatabase db = Db;
        members ??= [];
        foreach (string key in members.Select(m => m.Key).Prepend(caller.Key))
        {
            RedisValue previous = await db.StringGetAsync(PlayerKey(key));
            if (previous.HasValue) await CancelAsync(previous.ToString(), key);
        }

        long now = NowMs;
        var ticket = new MatchTicket(Guid.NewGuid().ToString("N"), caller.Key, caller.Name, playlist.Id, version, now, now,
            MatchTicket.Searching, "", "", "", rating, MatchTicket.Encode(members));
        ITransaction tran = db.CreateTransaction();
        _ = tran.HashSetAsync(TicketKey(ticket.Id), Fields(ticket));
        _ = tran.KeyExpireAsync(TicketKey(ticket.Id), TicketLife);
        _ = tran.StringSetAsync(PlayerKey(caller.Key), ticket.Id, TicketLife);
        foreach ((string key, _) in members) _ = tran.StringSetAsync(PlayerKey(key), ticket.Id, TicketLife);
        _ = tran.SortedSetAddAsync(QueueKey(playlist.Id), ticket.Id, now);
        await tran.ExecuteAsync();
        return ticket;
    }

    /// <summary>
    /// A ticket, if it exists and this player is on it: its leader or one of its party. Each
    /// is given their own join ticket. Asking about it keeps it alive.
    /// </summary>
    public async Task<MatchTicket?> CheckAsync(string id, string player)
    {
        MatchTicket? ticket = await ReadTicketAsync(id);
        if (ticket == null || !ticket.Includes(player)) return null;
        if (ticket.Status == MatchTicket.Searching)
        {
            await Db.HashSetAsync(TicketKey(id), "seen", NowMs);
            await Db.KeyExpireAsync(TicketKey(id), TicketLife);
        }
        if (ticket.Player != player && ticket.Status == MatchTicket.Matched)
            ticket = ticket with { JoinTicket = (await Db.HashGetAsync(TicketKey(id), "join:" + player)).ToString() };
        return ticket;
    }

    /// <summary>Gives up a place in a queue. False if there was no such place, or it was not this player's to give up (only a party's leader can).</summary>
    public async Task<bool> CancelAsync(string id, string player)
    {
        MatchTicket? ticket = await ReadTicketAsync(id);
        if (ticket == null || ticket.Player != player) return false;

        ITransaction tran = Db.CreateTransaction();
        tran.AddCondition(Condition.HashEqual(TicketKey(id), "status", MatchTicket.Searching));
        _ = tran.HashSetAsync(TicketKey(id), "status", MatchTicket.Cancelled);
        _ = tran.SortedSetRemoveAsync(QueueKey(ticket.Playlist), id);
        foreach (string key in Everyone(ticket)) _ = tran.KeyDeleteAsync(PlayerKey(key));
        await tran.ExecuteAsync();
        return true;
    }

    /// <summary>A ticket as it stands, whoever asks, without keeping it alive. For showing a party its leader's search.</summary>
    public Task<MatchTicket?> PeekAsync(string id) => ReadTicketAsync(id);

    static IEnumerable<string> Everyone(MatchTicket ticket) => ticket.MemberList.Select(m => m.Key).Prepend(ticket.Player);

    public async Task<int> SearchingAsync(string playlist) => (int)await Db.SortedSetLengthAsync(QueueKey(playlist));

    // ------------------------------------------------------------------ game servers

    public async Task<FleetServer> RegisterAsync(string address, int port, uint version)
    {
        string id = Guid.NewGuid().ToString("N")[..12];
        IDatabase db = Db;
        await db.HashSetAsync(ServerKey(id),
        [
            new("address", address), new("port", port), new("version", (long)version), new("state", "idle"), new("match", ""),
            new("players", 0), new("assigned", ""), new("assignedPlaylist", ""),
        ]);
        await db.KeyExpireAsync(ServerKey(id), ServerLife);
        await db.SetAddAsync(ServersKey, id);
        log.LogInformation("Game server {Id} at {Address}:{Port} joined the fleet.", id, address, port);
        return (await ReadServerAsync(id))!;
    }

    /// <summary>
    /// A server checking in. Returns what it is now: if its assigned match differs from the
    /// one it reports, that is a match for it to start. Null if the server is not known (it
    /// timed out, or this backend restarted), in which case it should register again.
    /// </summary>
    public async Task<FleetServer?> HeartbeatAsync(string id, string state, string reportedMatch, int players)
    {
        IDatabase db = Db;
        if (!await db.KeyExistsAsync(ServerKey(id))) return null;
        await db.HashSetAsync(ServerKey(id),
        [
            new("state", state == "idle" ? "idle" : "busy"), new("match", reportedMatch ?? ""), new("players", Math.Max(0, players)),
        ]);
        await db.KeyExpireAsync(ServerKey(id), ServerLife);
        return await ReadServerAsync(id);
    }

    /// <summary>A match the matchmaker set up, or null if it never did (or it was long ago).</summary>
    public async Task<StoredMatch?> GetMatchAsync(string matchId)
    {
        if (string.IsNullOrEmpty(matchId) || matchId.Length > 32) return null;
        RedisValue stored = await Db.StringGetAsync(MatchKey(matchId));
        return stored.HasValue ? JsonSerializer.Deserialize<StoredMatch>(stored.ToString(), Json) : null;
    }

    public async Task RemoveServerAsync(string id)
    {
        await Db.KeyDeleteAsync(ServerKey(id));
        await Db.SetRemoveAsync(ServersKey, id);
    }

    public async Task<List<FleetServer>> ServersAsync()
    {
        var servers = new List<FleetServer>();
        foreach (RedisValue id in await Db.SetMembersAsync(ServersKey))
        {
            FleetServer? server = await ReadServerAsync(id.ToString());
            if (server != null) servers.Add(server);
            else await Db.SetRemoveAsync(ServersKey, id);       // timed out
        }
        return servers;
    }

    // ------------------------------------------------------------------ the matchmaker

    /// <summary>One pass over every queue. Several API processes may call this; only one at a time does anything.</summary>
    public async Task MatchAsync()
    {
        IDatabase db = Db;
        string owner = Guid.NewGuid().ToString("N");
        if (!await db.StringSetAsync(LockKey, owner, TimeSpan.FromSeconds(10), When.NotExists)) return;
        try
        {
            List<FleetServer> servers = await ServersAsync();
            foreach (Playlist playlist in Playlists.All) await MatchPlaylistAsync(playlist, servers);
        }
        finally
        {
            ITransaction release = db.CreateTransaction();
            release.AddCondition(Condition.StringEqual(LockKey, owner));
            _ = release.KeyDeleteAsync(LockKey);
            await release.ExecuteAsync();
        }
    }

    async Task MatchPlaylistAsync(Playlist playlist, List<FleetServer> servers)
    {
        IDatabase db = Db;
        long now = NowMs;
        long idleCutoff = now - _options.TicketIdleSeconds * 1000L;
        var waiting = new List<MatchTicket>();
        foreach (RedisValue id in await db.SortedSetRangeByRankAsync(QueueKey(playlist.Id)))
        {
            MatchTicket? ticket = await ReadTicketAsync(id.ToString());
            if (ticket == null || ticket.Status != MatchTicket.Searching)
            {
                await db.SortedSetRemoveAsync(QueueKey(playlist.Id), id);
                continue;
            }
            if (ticket.SeenMs < idleCutoff)
            {
                // Stopped asking: they closed the game or lost their connection.
                await ExpireAsync(ticket);
                continue;
            }
            waiting.Add(ticket);
        }

        // Only players on the same version as a server can play on it, so each version is its own queue.
        foreach (IGrouping<uint, MatchTicket> sameVersion in waiting.GroupBy(t => t.Version))
        {
            List<MatchTicket> line = sameVersion.OrderBy(t => t.CreatedMs).ToList();
            if (playlist.Ranked)
            {
                await MatchRankedAsync(playlist, line, servers, sameVersion.Key, now);
                continue;
            }
            while (line.Count > 0)
            {
                (List<MatchTicket> picked, Dictionary<string, int>? sides, int people) = Pick(playlist, line);
                bool full = people >= playlist.Players;
                bool waitedEnough = people >= playlist.MinPlayers && now - line[0].CreatedMs >= _options.FillAfterSeconds * 1000L;
                if (!full && !waitedEnough) break;

                FleetServer? server = servers.FirstOrDefault(s => s.Free && s.Version == sameVersion.Key);
                if (server == null) break;       // everyone waits for a server to come free

                foreach (MatchTicket taken in picked) line.Remove(taken);
                if (await StartMatchAsync(server, playlist, picked, sides)) servers.Remove(server);
            }
        }
    }

    /// <summary>
    /// Ranked: for each player in turn, oldest first, the closest-rated others within a range
    /// that starts at 150 and widens by 10 for every second the oldest has waited. A match only
    /// ever starts full.
    /// </summary>
    async Task MatchRankedAsync(Playlist playlist, List<MatchTicket> line, List<FleetServer> servers, uint version, long now)
    {
        int anchor = 0;
        while (anchor < line.Count && line.Count >= playlist.Players)
        {
            MatchTicket first = line[anchor];
            int window = 150 + (int)Math.Min(1500, (now - first.CreatedMs) / 100);
            List<MatchTicket> group = line.Where(t => t != first && Math.Abs(t.Rating - first.Rating) <= window)
                .OrderBy(t => Math.Abs(t.Rating - first.Rating)).ThenBy(t => t.CreatedMs).Take(playlist.Players - 1).Prepend(first).ToList();
            if (group.Count < playlist.Players)
            {
                anchor++;
                continue;
            }

            FleetServer? server = servers.FirstOrDefault(s => s.Free && s.Version == version);
            if (server == null) return;
            foreach (MatchTicket taken in group) line.Remove(taken);
            if (await StartMatchAsync(server, playlist, group)) servers.Remove(server);
        }
    }

    /// <summary>
    /// Casual: the oldest tickets that fit, in order. With teams, each party goes whole onto
    /// the side with most room; a ticket that fits nowhere waits for the next match. Returns
    /// the tickets, each ticket's side (null without teams), and how many people they are.
    /// </summary>
    static (List<MatchTicket> Picked, Dictionary<string, int>? Sides, int People) Pick(Playlist playlist, List<MatchTicket> line)
    {
        var picked = new List<MatchTicket>();
        Dictionary<string, int>? sides = playlist.TeamSize > 0 ? new Dictionary<string, int>() : null;
        int[] room = { playlist.TeamSize, playlist.TeamSize };
        int people = 0;
        foreach (MatchTicket ticket in line)
        {
            if (people + ticket.Size > playlist.Players) continue;
            if (sides != null)
            {
                int side = room[0] >= room[1] ? 0 : 1;
                if (room[side] < ticket.Size) continue;
                room[side] -= ticket.Size;
                sides[ticket.Id] = side;
            }
            picked.Add(ticket);
            people += ticket.Size;
            if (people == playlist.Players) break;
        }
        return (picked, sides, people);
    }

    /// <summary>Splits players into two sides as even as their ratings allow: best, then a snake down the list.</summary>
    static Dictionary<string, int> BalanceTeams(List<MatchTicket> players)
    {
        var teams = new Dictionary<string, int>();
        var ordered = players.OrderByDescending(p => p.Rating).ToList();
        for (int i = 0; i < ordered.Count; i++) teams[ordered[i].Player] = (i % 4 == 0 || i % 4 == 3) ? 0 : 1;
        return teams;
    }

    /// <param name="sides">Casual with teams: each ticket's side, so a party stays together. Ranked balances by rating instead.</param>
    async Task<bool> StartMatchAsync(FleetServer server, Playlist playlist, List<MatchTicket> players, Dictionary<string, int>? sides = null)
    {
        IDatabase db = Db;
        string matchId = Guid.NewGuid().ToString("N")[..16];

        // What the server will be told: the rules, who is coming, their sides and their unlocks.
        Dictionary<string, int>? teams = playlist.Ranked && playlist.TeamSize > 0 ? BalanceTeams(players) : null;
        // With a party in the match, everyone's side is fixed here, so nobody who arrives
        // first can take a place on the side the party needs. Otherwise the server picks.
        if (teams == null && sides != null && players.Any(p => p.Size > 1))
        {
            teams = new Dictionary<string, int>();
            foreach (MatchTicket ticket in players)
            {
                foreach (string key in Everyone(ticket)) teams[key] = sides[ticket.Id];
            }
        }
        AssignedPlayer[] assigned;
        using (IServiceScope scope = scopes.CreateScope())
        {
            assigned = await scope.ServiceProvider.GetRequiredService<ProgressionService>()
                .UnlocksAsync(players.SelectMany(Everyone).Select(key => (key, teams != null && teams.TryGetValue(key, out int team) ? team : -1)));
        }
        var assignment = new MatchAssignment { matchId = matchId, playlist = playlist.Id, ranked = playlist.Ranked, players = assigned };
        await db.StringSetAsync(MatchKey(matchId), JsonSerializer.Serialize(new StoredMatch(server.Id, assignment), Json), MatchLife);

        // The server first, so nobody is matched to a server someone else just took.
        ITransaction claim = db.CreateTransaction();
        claim.AddCondition(Condition.KeyExists(ServerKey(server.Id)));
        claim.AddCondition(Condition.HashEqual(ServerKey(server.Id), "assigned", server.AssignedMatch));
        _ = claim.HashSetAsync(ServerKey(server.Id), [new("assigned", matchId), new("assignedPlaylist", playlist.Id)]);
        if (!await claim.ExecuteAsync()) return false;

        string address = server.Address + ":" + server.Port.ToString(CultureInfo.InvariantCulture);
        int matched = 0;
        foreach (MatchTicket ticket in players)
        {
            ITransaction take = db.CreateTransaction();
            take.AddCondition(Condition.HashEqual(TicketKey(ticket.Id), "status", MatchTicket.Searching));
            var fields = new List<HashEntry>
            {
                new("status", MatchTicket.Matched), new("address", address), new("match", matchId),
                new("join", JoinTicket(ticket.Player, ticket.Name, matchId, playlist.Ranked)),
            };
            // Each of a party gets a join ticket of their own.
            foreach ((string key, string name) in ticket.MemberList) fields.Add(new("join:" + key, JoinTicket(key, name, matchId, playlist.Ranked)));
            _ = take.HashSetAsync(TicketKey(ticket.Id), fields.ToArray());
            _ = take.SortedSetRemoveAsync(QueueKey(playlist.Id), ticket.Id);
            foreach (string key in Everyone(ticket)) _ = take.KeyDeleteAsync(PlayerKey(key));
            if (await take.ExecuteAsync()) matched += ticket.Size;
        }

        log.LogInformation("Match {Match} ({Playlist}, {Players} players) goes to server {Server}.", matchId, playlist.Id, matched, server.Id);
        // If everyone gave up at the last moment, the server sees nobody arrive and frees itself.
        return true;
    }

    /// <summary>The ticket a player shows the game server: who they are, and that they belong in this match.</summary>
    string JoinTicket(string player, string name, string matchId, bool ranked)
    {
        byte[] key = Encoding.UTF8.GetBytes(_game.TicketSigningKey);
        if (key.Length < GameTicket.MinKeyBytes) return "";      // servers without a key let anyone in
        long now = clock.GetUtcNow().ToUnixTimeSeconds();
        return GameTicket.Create(new TicketClaims
        {
            AccountId = player,
            Name = name,
            Mode = ranked ? GameTicket.Ranked : GameTicket.Casual,
            EmailVerified = ranked,
            ExpiresAt = now + Math.Max(_game.TicketSeconds, 60),
            MatchId = matchId,
        }, key, now);
    }

    async Task ExpireAsync(MatchTicket ticket)
    {
        ITransaction tran = Db.CreateTransaction();
        tran.AddCondition(Condition.HashEqual(TicketKey(ticket.Id), "status", MatchTicket.Searching));
        _ = tran.HashSetAsync(TicketKey(ticket.Id), "status", MatchTicket.Expired);
        _ = tran.SortedSetRemoveAsync(QueueKey(ticket.Playlist), ticket.Id);
        foreach (string key in Everyone(ticket)) _ = tran.KeyDeleteAsync(PlayerKey(key));
        await tran.ExecuteAsync();
    }

    // ------------------------------------------------------------------ storage

    static HashEntry[] Fields(MatchTicket t) =>
    [
        new("player", t.Player), new("name", t.Name), new("playlist", t.Playlist), new("version", (long)t.Version),
        new("created", t.CreatedMs), new("seen", t.SeenMs), new("status", t.Status), new("address", t.Address),
        new("join", t.JoinTicket), new("match", t.MatchId), new("rating", t.Rating), new("members", t.Members),
    ];

    async Task<MatchTicket?> ReadTicketAsync(string id)
    {
        if (id.Length is 0 or > 64) return null;
        HashEntry[] entries = await Db.HashGetAllAsync(TicketKey(id));
        if (entries.Length == 0) return null;
        Dictionary<string, RedisValue> f = entries.ToDictionary(e => e.Name.ToString(), e => e.Value);
        return new MatchTicket(id, Text(f, "player"), Text(f, "name"), Text(f, "playlist"), (uint)Number(f, "version"),
            Number(f, "created"), Number(f, "seen"), Text(f, "status"), Text(f, "address"), Text(f, "join"), Text(f, "match"),
            (int)Number(f, "rating"), Text(f, "members"));
    }

    async Task<FleetServer?> ReadServerAsync(string id)
    {
        HashEntry[] entries = await Db.HashGetAllAsync(ServerKey(id));
        if (entries.Length == 0) return null;
        Dictionary<string, RedisValue> f = entries.ToDictionary(e => e.Name.ToString(), e => e.Value);
        return new FleetServer(id, Text(f, "address"), (int)Number(f, "port"), (uint)Number(f, "version"), Text(f, "state"),
            Text(f, "match"), (int)Number(f, "players"), Text(f, "assigned"), Text(f, "assignedPlaylist"));
    }

    static string Text(Dictionary<string, RedisValue> fields, string name) => fields.TryGetValue(name, out RedisValue v) ? v.ToString() : "";

    static long Number(Dictionary<string, RedisValue> fields, string name) =>
        fields.TryGetValue(name, out RedisValue v) && v.TryParse(out long n) ? n : 0;
}

/// <summary>Runs the matchmaker every second or so.</summary>
public sealed class MatchmakerWorker(MatchmakingService matchmaking, IOptions<MatchmakingOptions> options, ILogger<MatchmakerWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Clamp(options.Value.IntervalMilliseconds, 50, 10000));
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await matchmaking.MatchAsync();
            }
            catch (Exception error) when (error is RedisException or TimeoutException)
            {
                log.LogWarning("Matchmaking could not reach Redis: {Message}", error.Message);
            }
            try
            {
                await Task.Delay(interval, stopping);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
