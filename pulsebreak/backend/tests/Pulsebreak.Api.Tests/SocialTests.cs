using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Pulsebreak.Api.Data;
using Pulsebreak.Api.Endpoints;
using Pulsebreak.Shared.Net;
using Pulsebreak.Shared.Progression;
using Pulsebreak.Shared.Social;

namespace Pulsebreak.Api.Tests;

/// <summary>Friends, blocks, reports, parties, party chat, party matchmaking and ranked seasons, against the real API, PostgreSQL and Redis.</summary>
[TestFixture]
public class SocialTests
{
    const string FleetKey = "test-fleet-key-0123456789-abcdefghijklmnop";
    const string AdminKey = "test-admin-key-0123456789-abcdefghijklmnop";
    static int _version = 29000;

    ApiFixture _api = null!;

    [OneTimeSetUp]
    public void Start() => _api = new ApiFixture(new Dictionary<string, string>
    {
        ["Matchmaking:FleetKey"] = FleetKey,
        ["Matchmaking:FillAfterSeconds"] = "1",
        ["Matchmaking:HeartbeatSeconds"] = "1",
        ["Matchmaking:ServerTimeoutSeconds"] = "30",
        ["Matchmaking:IntervalMilliseconds"] = "100",
        ["Social:AdminKey"] = AdminKey,
        // Seasons began long enough ago that there is a season before this one.
        ["Seasons:FirstStart"] = "2026-01-05T00:00:00Z",
    });

    [OneTimeTearDown] public void Stop() => _api.Dispose();

    // ------------------------------------------------------------------ helpers

    static async Task<JsonElement> PollAsync(TestPlayer player, long chatAfter = 0, string status = "menu")
    {
        HttpResponseMessage response = await player.Http.PostAsync("/v1/social/poll", new { status, chatAfter });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    async Task<(TestPlayer A, TestPlayer B)> FriendsAsync()
    {
        TestPlayer a = await _api.RegisterAsync(), b = await _api.RegisterAsync();
        Assert.That((await a.Http.PostAsync("/v1/friends", new { username = b.Username })).StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That((await b.Http.PostAsync("/v1/friends/" + a.Id + "/accept", new { })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        return (a, b);
    }

    async Task<string> PartyAsync(TestPlayer leader, params TestPlayer[] others)
    {
        foreach (TestPlayer other in others)
        {
            Assert.That((await leader.Http.PostAsync("/v1/party/invites", new { accountId = other.Id })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            JsonElement invites = (await PollAsync(other)).GetProperty("invites");
            Assert.That(invites.GetArrayLength(), Is.EqualTo(1));
            string partyId = invites[0].GetProperty("partyId").GetString()!;
            HttpResponseMessage joined = await other.Http.PostAsync("/v1/party/invites/" + partyId + "/accept", new { });
            Assert.That(joined.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await joined.Content.ReadAsStringAsync());
        }
        return (await PollAsync(leader)).GetProperty("party").GetProperty("id").GetString()!;
    }

    static string[] Names(JsonElement list) => list.EnumerateArray().Select(p => p.GetProperty("username").GetString()!).ToArray();

    // ------------------------------------------------------------------ friends

    [Test]
    public async Task Friends_AskAcceptSeeEachOtherOnline_AndUnfriend()
    {
        TestPlayer a = await _api.RegisterAsync(), b = await _api.RegisterAsync();

        Assert.That(await (await a.Http.PostAsync("/v1/friends", new { username = "nobody_by_that_name" })).ErrorCodeAsync(), Is.EqualTo("not_found"));
        Assert.That(await (await a.Http.PostAsync("/v1/friends", new { username = a.Username })).ErrorCodeAsync(), Is.EqualTo("self"));

        HttpResponseMessage asked = await a.Http.PostAsync("/v1/friends", new { username = b.Username.ToUpperInvariant() });
        Assert.That(asked.StatusCode, Is.EqualTo(HttpStatusCode.Created), "usernames match whatever the case");
        Assert.That(Names((await PollAsync(a)).GetProperty("outgoing")), Is.EqualTo(new[] { b.Username }));
        JsonElement bSees = await PollAsync(b, status: "range");
        Assert.That(Names(bSees.GetProperty("incoming")), Is.EqualTo(new[] { a.Username }));
        Assert.That(bSees.GetProperty("friends").GetArrayLength(), Is.EqualTo(0));

        // Asking back is a yes.
        Assert.That((await b.Http.PostAsync("/v1/friends", new { username = a.Username })).StatusCode, Is.EqualTo(HttpStatusCode.Created));
        JsonElement aSees = await PollAsync(a);
        Assert.That(Names(aSees.GetProperty("friends")), Is.EqualTo(new[] { b.Username }));
        Assert.That(aSees.GetProperty("friends")[0].GetProperty("status").GetString(), Is.EqualTo("range"), "b's game said it was on the range");
        Assert.That(aSees.GetProperty("season").GetProperty("number").GetInt32(), Is.GreaterThanOrEqualTo(1));

        Assert.That(await (await a.Http.PostAsync("/v1/friends", new { username = b.Username })).ErrorCodeAsync(), Is.EqualTo("already_friends"));
        Assert.That((await a.Http.DeleteAsync("/v1/friends/" + b.Id)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await PollAsync(b)).GetProperty("friends").GetArrayLength(), Is.EqualTo(0), "unfriending ends it for both");
    }

    [Test]
    public async Task Blocking_EndsTheFriendship_AndStopsRequestsAndInvites()
    {
        (TestPlayer a, TestPlayer b) = await FriendsAsync();
        Assert.That((await a.Http.PostAsync("/v1/blocks/" + b.Id, new { })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        JsonElement aSees = await PollAsync(a);
        Assert.That(aSees.GetProperty("friends").GetArrayLength(), Is.EqualTo(0));
        Assert.That(Names(aSees.GetProperty("blocked")), Is.EqualTo(new[] { b.Username }));

        Assert.That(await (await b.Http.PostAsync("/v1/friends", new { username = a.Username })).ErrorCodeAsync(), Is.EqualTo("blocked"));
        Assert.That(await (await b.Http.PostAsync("/v1/party/invites", new { accountId = a.Id })).ErrorCodeAsync(), Is.EqualTo("not_friends"));

        Assert.That((await a.Http.DeleteAsync("/v1/blocks/" + b.Id)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await b.Http.PostAsync("/v1/friends", new { username = a.Username })).StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    // ------------------------------------------------------------------ reports

    [Test]
    public async Task Reports_AreFiledOnce_AndAModeratorCanListThem()
    {
        TestPlayer a = await _api.RegisterAsync(), b = await _api.RegisterAsync();
        var report = new { accountId = b.Id, reason = "abusive_chat", details = "  kept   at it  ", matchId = "m123", chat = new[] { "first line", "second​ line" } };
        Assert.That((await a.Http.PostAsync("/v1/reports", report)).StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        Assert.That((await a.Http.PostAsync("/v1/reports", report)).StatusCode, Is.EqualTo(HttpStatusCode.Accepted), "a repeat is quietly the same report");
        Assert.That((int)(await a.Http.PostAsync("/v1/reports", new { accountId = b.Id, reason = "being_bad" })).StatusCode, Is.EqualTo(422));
        Assert.That(await (await a.Http.PostAsync("/v1/reports", new { accountId = a.Id, reason = "other" })).ErrorCodeAsync(), Is.EqualTo("self"));

        HttpClient moderator = _api.CreateClient();
        Assert.That((await moderator.GetAsync("/v1/admin/reports")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "needs the key");
        moderator.DefaultRequestHeaders.Add(SocialEndpoints.AdminKeyHeader, AdminKey);
        JsonElement list = await (await moderator.GetAsync("/v1/admin/reports")).JsonAsync();
        JsonElement[] mine = list.EnumerateArray().Where(r => r.GetProperty("reportedId").GetString() == b.Id).ToArray();
        Assert.That(mine.Length, Is.EqualTo(1));
        Assert.That(mine[0].GetProperty("reporter").GetString(), Is.EqualTo(a.Username));
        Assert.That(mine[0].GetProperty("details").GetString(), Is.EqualTo("kept at it"));
        Assert.That(mine[0].GetProperty("chat").GetString(), Is.EqualTo("first line\nsecond line"));

        string id = mine[0].GetProperty("id").GetString()!;
        Assert.That((await moderator.PostAsync("/v1/admin/reports/" + id + "/status", new { status = "actioned" })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        JsonElement open = await (await moderator.GetAsync("/v1/admin/reports")).JsonAsync();
        Assert.That(open.EnumerateArray().Any(r => r.GetProperty("id").GetString() == id), Is.False);
    }

    // ------------------------------------------------------------------ parties

    [Test]
    public async Task Parties_InviteJoinChatLeave()
    {
        (TestPlayer a, TestPlayer b) = await FriendsAsync();
        TestPlayer stranger = await _api.RegisterAsync();
        Assert.That(await (await a.Http.PostAsync("/v1/party/invites", new { accountId = stranger.Id })).ErrorCodeAsync(), Is.EqualTo("not_friends"));

        string partyId = await PartyAsync(a, b);
        JsonElement party = (await PollAsync(b)).GetProperty("party");
        Assert.That(party.GetProperty("id").GetString(), Is.EqualTo(partyId));
        Assert.That(party.GetProperty("leaderId").GetString(), Is.EqualTo(a.Id));
        Assert.That(Names(party.GetProperty("members")), Is.EqualTo(new[] { a.Username, b.Username }));

        Assert.That((await b.Http.PostAsync("/v1/party/chat", new { text = "  ready   when you are " })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        JsonElement aSees = await PollAsync(a);
        JsonElement[] lines = aSees.GetProperty("chat").EnumerateArray().ToArray();
        Assert.That(lines.Last().GetProperty("text").GetString(), Is.EqualTo("ready when you are"));
        Assert.That(lines.Last().GetProperty("username").GetString(), Is.EqualTo(b.Username));
        long cursor = aSees.GetProperty("chatCursor").GetInt64();
        Assert.That((await PollAsync(a, cursor)).GetProperty("chat").GetArrayLength(), Is.EqualTo(0), "nothing new since the cursor");

        Assert.That(await (await b.Http.PostAsync("/v1/party/members/" + a.Id + "/kick", new { })).ErrorCodeAsync(), Is.EqualTo("not_leader"));
        Assert.That((await a.Http.PostAsync("/v1/party/leave", new { })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await PollAsync(b)).GetProperty("party").ValueKind, Is.EqualTo(JsonValueKind.Null), "a party of one ends");
    }

    [Test]
    public async Task Parties_PassTheLead_AndHideBlockedPlayersChat()
    {
        (TestPlayer a, TestPlayer b) = await FriendsAsync();
        TestPlayer c = await _api.RegisterAsync();
        Assert.That((await a.Http.PostAsync("/v1/friends", new { username = c.Username })).StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That((await c.Http.PostAsync("/v1/friends/" + a.Id + "/accept", new { })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        await PartyAsync(a, b, c);

        Assert.That((await c.Http.PostAsync("/v1/blocks/" + b.Id, new { })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await b.Http.PostAsync("/v1/party/chat", new { text = "hello from b" })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await a.Http.PostAsync("/v1/party/chat", new { text = "hello from a" })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        string[] cHears = (await PollAsync(c)).GetProperty("chat").EnumerateArray().Select(l => l.GetProperty("text").GetString()!).ToArray();
        Assert.That(cHears, Does.Contain("hello from a"));
        Assert.That(cHears, Does.Not.Contain("hello from b"), "a blocked player's chat is not shown");

        Assert.That((await a.Http.PostAsync("/v1/party/leave", new { })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        JsonElement party = (await PollAsync(b)).GetProperty("party");
        Assert.That(party.GetProperty("leaderId").GetString(), Is.EqualTo(b.Id), "the next to have joined leads");
        Assert.That(Names(party.GetProperty("members")), Is.EqualTo(new[] { b.Username, c.Username }));
        Assert.That((await b.Http.PostAsync("/v1/party/members/" + c.Id + "/kick", new { })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await PollAsync(c)).GetProperty("party").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task APartyIsMatchedTogether_OnOneSide_EachWithTheirOwnTicket()
    {
        uint version = (uint)Interlocked.Increment(ref _version);
        (TestPlayer a, TestPlayer b) = await FriendsAsync();
        await PartyAsync(a, b);

        Assert.That(await (await b.Http.PostAsync("/v1/matchmaking/tickets", new { playlist = "elimination", version })).ErrorCodeAsync(),
            Is.EqualTo("party_member"), "the leader searches for the party");
        Assert.That(await (await a.Http.PostAsync("/v1/matchmaking/tickets", new { playlist = "duel", version })).ErrorCodeAsync(),
            Is.EqualTo("party_too_big"));
        Assert.That(await (await a.Http.PostAsync("/v1/matchmaking/tickets", new { playlist = "elimination_ranked", version })).ErrorCodeAsync(),
            Is.Not.EqualTo(""), "ranked refuses a party (or an unverified account) either way");

        HttpClient fleet = _api.CreateClient();
        fleet.DefaultRequestHeaders.Add(MatchmakingEndpoints.FleetKeyHeader, FleetKey);
        string server = (await (await fleet.PostAsync("/v1/fleet/servers", new { address = "203.0.113.20", port = 7801, version })).JsonAsync())
            .GetProperty("id").GetString()!;
        await fleet.PostAsync("/v1/fleet/servers/" + server + "/heartbeat", new { state = "idle", matchId = "", players = 0 });

        HttpResponseMessage queued = await a.Http.PostAsync("/v1/matchmaking/tickets", new { playlist = "elimination", version });
        Assert.That(queued.StatusCode, Is.EqualTo(HttpStatusCode.Created), await queued.Content.ReadAsStringAsync());
        string ticketId = (await queued.JsonAsync()).GetProperty("id").GetString()!;
        JsonElement search = (await PollAsync(b)).GetProperty("party").GetProperty("search");
        Assert.That(search.GetProperty("ticketId").GetString(), Is.EqualTo(ticketId), "the member can follow the leader's search");

        JsonElement aTicket = default, bTicket = default;
        for (int i = 0; i < 60; i++)
        {
            aTicket = await (await a.Http.GetAsync("/v1/matchmaking/tickets/" + ticketId)).JsonAsync();
            bTicket = await (await b.Http.GetAsync("/v1/matchmaking/tickets/" + ticketId)).JsonAsync();
            if (aTicket.GetProperty("status").GetString() == "matched") break;
            await Task.Delay(100);
        }
        Assert.That(bTicket.GetProperty("status").GetString(), Is.EqualTo("matched"));
        Assert.That(bTicket.GetProperty("matchId").GetString(), Is.EqualTo(aTicket.GetProperty("matchId").GetString()));

        byte[] key = Encoding.UTF8.GetBytes(ApiFixture.TicketKey);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.That(GameTicket.TryVerify(aTicket.GetProperty("joinTicket").GetString(), key, now, out TicketClaims aClaims, out string problem), problem);
        Assert.That(GameTicket.TryVerify(bTicket.GetProperty("joinTicket").GetString(), key, now, out TicketClaims bClaims, out problem), problem);
        Assert.That(aClaims.AccountId, Is.EqualTo(a.Id));
        Assert.That(bClaims.AccountId, Is.EqualTo(b.Id), "each member has a ticket in their own name");
        Assert.That(bClaims.Name, Is.EqualTo(b.Username));

        JsonElement assignment = await (await fleet.PostAsync("/v1/fleet/servers/" + server + "/heartbeat", new { state = "idle", matchId = "", players = 0 })).JsonAsync();
        JsonElement[] players = assignment.GetProperty("players").EnumerateArray().ToArray();
        Assert.That(players.Length, Is.EqualTo(2));
        Assert.That(players.Select(p => p.GetProperty("team").GetInt32()).Distinct().Count(), Is.EqualTo(1), "the party plays on one side");

        // Joining or leaving the party ends a search it had running.
        HttpResponseMessage again = await a.Http.PostAsync("/v1/matchmaking/tickets", new { playlist = "tdm", version = version + 1000 });
        string second = (await again.JsonAsync()).GetProperty("id").GetString()!;
        Assert.That((await b.Http.PostAsync("/v1/party/leave", new { })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await (await a.Http.GetAsync("/v1/matchmaking/tickets/" + second)).JsonAsync()).GetProperty("status").GetString(), Is.EqualTo("cancelled"));
    }

    // ------------------------------------------------------------------ seasons

    [Test]
    public async Task ANewSeason_SoftResetsRatings_ReplaysPlacements_AndKeepsTheOldResult()
    {
        TestPlayer player = await _api.RegisterAsync();
        Guid id = Guid.Parse(player.Id);
        int current = (await PollAsync(player)).GetProperty("season").GetProperty("number").GetInt32();
        Assert.That(current, Is.GreaterThan(1));

        // A rating from the season before: 1400 after 12 matches.
        await _api.WithDbAsync(async db =>
        {
            db.Ratings.Add(new RatingRow
            {
                AccountId = id, Playlist = "duel_ranked", Rating = 1400, BestRating = 1450, Matches = 12, Wins = 9, Losses = 3,
                Season = current - 1, UpdatedAt = DateTimeOffset.UtcNow,
            });
            return await db.SaveChangesAsync();
        });
        JsonElement board = await (await player.Http.GetAsync("/v1/ranked/duel_ranked/leaderboard")).JsonAsync();
        Assert.That(board.EnumerateArray().Any(e => e.GetProperty("name").GetString() == player.Username), Is.False, "last season's rating is not on this season's board");

        JsonElement progression = await (await player.Http.GetAsync("/v1/progression")).JsonAsync();
        JsonElement duel = progression.GetProperty("ranks").EnumerateArray().First(r => r.GetProperty("playlist").GetString() == "duel_ranked");
        Assert.That(duel.GetProperty("rating").GetInt32(), Is.EqualTo(1200), "halfway back to 1000");
        Assert.That(duel.GetProperty("matches").GetInt32(), Is.EqualTo(0));
        Assert.That(duel.GetProperty("placementLeft").GetInt32(), Is.EqualTo(Ranks.PlacementMatches), "placements are played again");

        JsonElement past = progression.GetProperty("pastSeasons");
        Assert.That(past.GetArrayLength(), Is.EqualTo(1));
        Assert.That(past[0].GetProperty("season").GetInt32(), Is.EqualTo(current - 1));
        Assert.That(past[0].GetProperty("finalRating").GetInt32(), Is.EqualTo(1400));
        Assert.That(past[0].GetProperty("bestRating").GetInt32(), Is.EqualTo(1450));
        Assert.That(progression.GetProperty("season").GetProperty("endsAt").GetInt64(), Is.GreaterThan(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

        int stored = await _api.WithDbAsync(db => db.Ratings.Where(r => r.AccountId == id).Select(r => r.Season).FirstAsync());
        Assert.That(stored, Is.EqualTo(current), "rolled over once, and saved");
    }

    // ------------------------------------------------------------------ chat text

    [Test]
    public void ChatText_IsCleanedAndFiltered()
    {
        Assert.That(ChatText.Clean("  hi ​  there\n\tall  "), Is.EqualTo("hi there all"));
        Assert.That(ChatText.Clean("   "), Is.Null);
        Assert.That(ChatText.Clean(new string('x', 500))!.Length, Is.EqualTo(ChatText.MaxLength));
        string rude = ChatText.Filter("what the FUUUCK, sh1t happens. Good night, classy pass");
        Assert.That(rude, Is.EqualTo("what the ******, **** happens. Good night, classy pass"), rude);
        Assert.That(ChatText.HasStrongLanguage("nice shot"), Is.False);
    }
}
