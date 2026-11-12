using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Pulsebreak.Api.Endpoints;
using Pulsebreak.Api.Progression;
using Pulsebreak.Shared.Net;
using Pulsebreak.Shared.Progression;

namespace Pulsebreak.Api.Tests;

/// <summary>
/// What playing earns, end to end through the real API: a matchmade match is set up, its game
/// server reports how it went, and XP, weapon levels, challenges, ratings and history follow.
/// </summary>
[TestFixture]
public class ProgressionTests
{
    const string FleetKey = "test-fleet-key-0123456789-abcdefghijklmnop";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { IncludeFields = true };
    static int _version = 19000;

    ApiFixture _api = null!;

    [OneTimeSetUp]
    public void Start() => _api = new ApiFixture(new Dictionary<string, string>
    {
        ["Matchmaking:FleetKey"] = FleetKey,
        ["Matchmaking:FillAfterSeconds"] = "1",
        ["Matchmaking:HeartbeatSeconds"] = "1",
        ["Matchmaking:ServerTimeoutSeconds"] = "30",
        ["Matchmaking:IntervalMilliseconds"] = "100",
    });

    [OneTimeTearDown] public void Stop() => _api.Dispose();

    static uint NewVersion() => (uint)Interlocked.Increment(ref _version);

    HttpClient Fleet()
    {
        HttpClient http = _api.CreateClient();
        http.DefaultRequestHeaders.Add(MatchmakingEndpoints.FleetKeyHeader, FleetKey);
        return http;
    }

    async Task<string> ServerAsync(HttpClient fleet, uint version)
    {
        JsonElement body = await (await fleet.PostAsync("/v1/fleet/servers", new { address = "203.0.113.9", port = 7790, version })).JsonAsync();
        string id = body.GetProperty("id").GetString()!;
        await fleet.PostAsync("/v1/fleet/servers/" + id + "/heartbeat", new { state = "idle", matchId = "", players = 0 });
        return id;
    }

    async Task<TestPlayer> VerifiedAsync()
    {
        TestPlayer player = await _api.RegisterAsync();
        string code = CapturedMail.VerifyCode(await _api.Mail.WaitForAsync(player.Email));
        Assert.That((await player.Http.PostAsync("/v1/auth/verify-email", new { code })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        return player;
    }

    /// <summary>Queues everyone for a playlist and waits until the matchmaker puts them in one match. Returns their tickets.</summary>
    static async Task<JsonElement[]> MatchAsync(string playlist, uint version, params TestPlayer[] players)
    {
        var ids = new List<string>();
        foreach (TestPlayer player in players)
        {
            HttpResponseMessage queued = await player.Http.PostAsync("/v1/matchmaking/tickets", new { playlist, version });
            Assert.That(queued.StatusCode, Is.EqualTo(HttpStatusCode.Created), await queued.Content.ReadAsStringAsync());
            ids.Add((await queued.JsonAsync()).GetProperty("id").GetString()!);
        }
        var tickets = new JsonElement[players.Length];
        for (int attempt = 0; attempt < 60; attempt++)
        {
            for (int i = 0; i < players.Length; i++) tickets[i] = await (await players[i].Http.GetAsync("/v1/matchmaking/tickets/" + ids[i])).JsonAsync();
            if (tickets.All(t => t.GetProperty("status").GetString() == "matched")) return tickets;
            await Task.Delay(100);
        }
        Assert.Fail("Not matched: " + string.Join(", ", tickets.Select(t => t.GetProperty("status").GetString())));
        return tickets;
    }

    static async Task<MatchAssignment> AssignmentAsync(HttpClient fleet, string server)
    {
        HttpResponseMessage beat = await fleet.PostAsync("/v1/fleet/servers/" + server + "/heartbeat", new { state = "idle", matchId = "", players = 0 });
        return (await beat.Content.ReadFromJsonAsync<MatchAssignment>(Json))!;
    }

    static Task<HttpResponseMessage> ReportAsync(HttpClient fleet, string server, MatchReport report) =>
        fleet.PostAsync("/v1/fleet/servers/" + server + "/results", JsonContent.Create(report, options: Json));

    static PlayerReport Player(TestPlayer player, int team, MatchOutcome outcome, int kills = 0, int headshots = 0, params WeaponReport[] weapons) => new()
    {
        accountId = player.Id, name = player.Username, team = team, outcome = outcome, completed = true, kills = kills, headshots = headshots,
        deaths = 3, weapons = weapons,
    };

    static async Task<JsonElement> ProgressionOf(TestPlayer player) => await (await player.Http.GetAsync("/v1/progression")).JsonAsync();

    // ------------------------------------------------------------------ tests

    [Test]
    public async Task AFinishedMatch_PaysXpAndWeaponXp_OnceOnly_AndShowsInHistory()
    {
        uint version = NewVersion();
        HttpClient fleet = Fleet();
        string server = await ServerAsync(fleet, version);
        TestPlayer ada = await _api.RegisterAsync(), bo = await _api.RegisterAsync();

        JsonElement[] tickets = await MatchAsync("duel", version, ada, bo);
        string matchId = tickets[0].GetProperty("matchId").GetString()!;
        MatchAssignment assignment = await AssignmentAsync(fleet, server);
        Assert.That(assignment.matchId, Is.EqualTo(matchId));
        Assert.That(assignment.ranked, Is.False);
        Assert.That(assignment.players.Select(p => p.accountId), Is.EquivalentTo(new[] { ada.Id, bo.Id }));
        Assert.That(assignment.players.All(p => p.level == 1 && p.team == -1), "casual: level 1, and the server picks sides");

        var rifle = new WeaponReport { weaponId = "ar_hadron", weaponClass = "AssaultRifle", kills = 9, headshots = 3 };
        var report = new MatchReport
        {
            matchId = matchId, durationSeconds = 240,
            players =
            [
                Player(ada, 0, MatchOutcome.Won, kills: 9, headshots: 3, rifle),
                Player(bo, 1, MatchOutcome.Lost, kills: 4),
                new PlayerReport { accountId = Guid.NewGuid().ToString(), kills = 99, completed = true },     // nobody sent here
            ],
        };
        HttpResponseMessage first = await ReportAsync(fleet, server, report);
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK), await first.Content.ReadAsStringAsync());
        Assert.That((await first.JsonAsync()).GetProperty("outcome").GetString(), Is.EqualTo("recorded"));

        JsonElement mine = await ProgressionOf(ada);
        int expected = MatchXp.For(report.players[0], false);
        int bonus = mine.GetProperty("lastMatch").GetProperty("xpGained").GetInt32() - expected;
        Assert.That(bonus, Is.GreaterThanOrEqualTo(0), "challenge rewards come on top");
        Assert.That(mine.GetProperty("level").GetProperty("xp").GetInt32(), Is.EqualTo(expected + bonus));
        JsonElement weapon = mine.GetProperty("weapons").EnumerateArray().Single();
        Assert.That(weapon.GetProperty("weaponId").GetString(), Is.EqualTo("ar_hadron"));
        Assert.That(weapon.GetProperty("xp").GetInt32(), Is.EqualTo(MatchXp.ForWeapon(rifle)));
        Assert.That(weapon.GetProperty("level").GetInt32(), Is.EqualTo(Levels.WeaponLevelFor(MatchXp.ForWeapon(rifle))));
        Assert.That(mine.GetProperty("lastMatch").GetProperty("matchId").GetString(), Is.EqualTo(matchId));
        Assert.That(mine.GetProperty("lastMatch").GetProperty("outcome").GetInt32(), Is.EqualTo((int)MatchOutcome.Won));

        // Reported again, nothing changes.
        HttpResponseMessage again = await ReportAsync(fleet, server, report);
        Assert.That((await again.JsonAsync()).GetProperty("outcome").GetString(), Is.EqualTo("already_recorded"));
        Assert.That((await ProgressionOf(ada)).GetProperty("level").GetProperty("xp").GetInt32(), Is.EqualTo(expected + bonus));

        JsonElement history = await (await ada.Http.GetAsync("/v1/matches")).JsonAsync();
        Assert.That(history.GetArrayLength(), Is.EqualTo(1));
        Assert.That(history[0].GetProperty("kills").GetInt32(), Is.EqualTo(9));
        Assert.That(history[0].GetProperty("playlist").GetString(), Is.EqualTo("duel"));

        // Only the server the match went to may report it, and only for matches matchmaking made.
        HttpClient otherFleet = Fleet();
        string other = await ServerAsync(otherFleet, version);
        Assert.That((await ReportAsync(otherFleet, other, report)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await ReportAsync(fleet, server, new MatchReport { matchId = "made-up" })).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await _api.CreateClient().PostAsync("/v1/fleet/servers/" + server + "/results", new { matchId })).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden), "no fleet key, no report");
    }

    [Test]
    public async Task Challenges_AreSixADayAndWeek_AndMoveWithPlay()
    {
        uint version = NewVersion();
        HttpClient fleet = Fleet();
        string server = await ServerAsync(fleet, version);
        TestPlayer ada = await _api.RegisterAsync(), bo = await _api.RegisterAsync();

        JsonElement before = await ProgressionOf(ada);
        JsonElement[] challenges = before.GetProperty("challenges").EnumerateArray().ToArray();
        Assert.That(challenges.Count(c => !c.GetProperty("weekly").GetBoolean()), Is.EqualTo(3));
        Assert.That(challenges.Count(c => c.GetProperty("weekly").GetBoolean()), Is.EqualTo(3));
        Assert.That(challenges.All(c => c.GetProperty("progress").GetInt32() == 0));
        Assert.That(challenges.All(c => c.GetProperty("endsAt").GetInt64() > DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        Assert.That((await ProgressionOf(ada)).GetProperty("challenges").EnumerateArray().Select(c => c.GetProperty("id").GetString()),
            Is.EqualTo(challenges.Select(c => c.GetProperty("id").GetString())), "the same challenges every time you look");

        JsonElement[] tickets = await MatchAsync("domination", version, ada, bo);
        var weapons = new[]
        {
            new WeaponReport { weaponId = "smg_flit", weaponClass = "Smg", kills = 6, headshots = 2 },
            new WeaponReport { weaponId = "sg_breaker", weaponClass = "Shotgun", kills = 5 },
            new WeaponReport { weaponId = "melee_field_knife", weaponClass = "Melee", kills = 2 },
        };
        PlayerReport played = Player(ada, 0, MatchOutcome.Won, kills: 13, headshots: 6, weapons);
        played.objective = 520;
        played.roundsWon = 0;
        await ReportAsync(fleet, server, new MatchReport { matchId = tickets[0].GetProperty("matchId").GetString()!, players = [played] });

        JsonElement after = await ProgressionOf(ada);
        foreach (JsonElement challenge in after.GetProperty("challenges").EnumerateArray())
        {
            Challenge rule = Challenges.Find(challenge.GetProperty("id").GetString()!)!;
            int expected = Math.Min(rule.Goal, rule.ProgressFrom(played, false));
            Assert.That(challenge.GetProperty("progress").GetInt32(), Is.EqualTo(expected), rule.Id);
            Assert.That(challenge.GetProperty("completed").GetBoolean(), Is.EqualTo(expected >= rule.Goal), rule.Id);
        }
    }

    [Test]
    public async Task Ranked_IsForVerifiedAccounts_MovesRatings_AndFillsTheLeaderboard()
    {
        uint version = NewVersion();
        HttpClient fleet = Fleet();
        string server = await ServerAsync(fleet, version);

        // Not for guests, not for unverified accounts.
        HttpClient guest = _api.CreateClient();
        string pass = (await (await guest.PostAsync("/v1/matchmaking/guest", new { name = "Gus" })).JsonAsync()).GetProperty("token").GetString()!;
        guest.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", pass);
        HttpResponseMessage asGuest = await guest.PostAsync("/v1/matchmaking/tickets", new { playlist = "duel_ranked", version });
        Assert.That(await asGuest.ErrorCodeAsync(), Is.EqualTo("sign_in_required"));
        TestPlayer unverified = await _api.RegisterAsync();
        HttpResponseMessage asUnverified = await unverified.Http.PostAsync("/v1/matchmaking/tickets", new { playlist = "duel_ranked", version });
        Assert.That(await asUnverified.ErrorCodeAsync(), Is.EqualTo("email_not_verified"));

        TestPlayer ada = await VerifiedAsync(), bo = await VerifiedAsync();
        for (int match = 1; match <= Ranks.PlacementMatches; match++)
        {
            JsonElement[] tickets = await MatchAsync("duel_ranked", version, ada, bo);
            byte[] key = Encoding.UTF8.GetBytes(ApiFixture.TicketKey);
            Assert.That(GameTicket.TryVerify(tickets[0].GetProperty("joinTicket").GetString(), key, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                out TicketClaims claims, out _), Is.True);
            Assert.That(claims.Mode, Is.EqualTo(GameTicket.Ranked));

            MatchAssignment assignment = await AssignmentAsync(fleet, server);
            Assert.That(assignment.ranked, Is.True);
            Assert.That(assignment.players.Select(p => p.team), Is.EquivalentTo(new[] { 0, 1 }), "the matchmaker picks the sides in ranked");
            int adaTeam = assignment.players.Single(p => p.accountId == ada.Id).team;

            await ReportAsync(fleet, server, new MatchReport
            {
                matchId = assignment.matchId,
                players = [Player(ada, adaTeam, MatchOutcome.Won, kills: 3), Player(bo, 1 - adaTeam, MatchOutcome.Lost, kills: 1)],
            });
            // The server takes the match on, plays it, and is free again.
            await fleet.PostAsync("/v1/fleet/servers/" + server + "/heartbeat", new { state = "idle", matchId = assignment.matchId, players = 0 });
        }

        JsonElement adaRank = (await ProgressionOf(ada)).GetProperty("ranks").EnumerateArray().Single(r => r.GetProperty("playlist").GetString() == "duel_ranked");
        JsonElement boRank = (await ProgressionOf(bo)).GetProperty("ranks").EnumerateArray().Single(r => r.GetProperty("playlist").GetString() == "duel_ranked");
        Assert.That(adaRank.GetProperty("rating").GetInt32(), Is.GreaterThan(Ranks.StartRating + 50));
        Assert.That(boRank.GetProperty("rating").GetInt32(), Is.LessThan(Ranks.StartRating - 50));
        Assert.That(adaRank.GetProperty("wins").GetInt32(), Is.EqualTo(Ranks.PlacementMatches));
        Assert.That(adaRank.GetProperty("placementLeft").GetInt32(), Is.EqualTo(0));
        Assert.That(adaRank.GetProperty("name").GetString(), Does.Not.StartWith("Placement"));

        JsonElement board = await (await _api.CreateClient().GetAsync("/v1/ranked/duel_ranked/leaderboard")).JsonAsync();
        Assert.That(board[0].GetProperty("name").GetString(), Is.EqualTo(ada.Username));
        Assert.That(board[0].GetProperty("place").GetInt32(), Is.EqualTo(1));
        Assert.That((await _api.CreateClient().GetAsync("/v1/ranked/tdm/leaderboard")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task ARankedNoShow_Loses_AndTheNextAssignmentCarriesWhatPlayersUnlocked()
    {
        uint version = NewVersion();
        HttpClient fleet = Fleet();
        string server = await ServerAsync(fleet, version);
        TestPlayer ada = await VerifiedAsync(), bo = await VerifiedAsync();

        JsonElement[] tickets = await MatchAsync("duel_ranked", version, ada, bo);
        MatchAssignment first = await AssignmentAsync(fleet, server);
        int adaTeam = first.players.Single(p => p.accountId == ada.Id).team;
        // Only Ada showed up; she played on her own and won with plenty of kills on the SMG.
        var smg = new WeaponReport { weaponId = "smg_flit", weaponClass = "Smg", kills = 40, headshots = 10 };
        await ReportAsync(fleet, server, new MatchReport { matchId = first.matchId, players = [Player(ada, adaTeam, MatchOutcome.Won, 40, 10, smg)] });
        await fleet.PostAsync("/v1/fleet/servers/" + server + "/heartbeat", new { state = "idle", matchId = first.matchId, players = 0 });

        JsonElement boRank = (await ProgressionOf(bo)).GetProperty("ranks").EnumerateArray().Single(r => r.GetProperty("playlist").GetString() == "duel_ranked");
        Assert.That(boRank.GetProperty("losses").GetInt32(), Is.EqualTo(1), "a no-show in ranked is a loss");

        await MatchAsync("duel", version, ada, bo);
        MatchAssignment next = await AssignmentAsync(fleet, server);
        AssignedPlayer adaNext = next.players.Single(p => p.accountId == ada.Id);
        Assert.That(adaNext.level, Is.GreaterThan(1));
        Assert.That(adaNext.weapons.Single(w => w.weaponId == "smg_flit").level, Is.EqualTo(Levels.WeaponLevelFor(MatchXp.ForWeapon(smg))));
        Assert.That(next.players.Single(p => p.accountId == bo.Id).weapons, Is.Empty);
    }
}
