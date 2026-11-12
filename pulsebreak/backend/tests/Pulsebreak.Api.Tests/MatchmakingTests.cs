using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Pulsebreak.Api.Endpoints;
using Pulsebreak.Shared.Net;

namespace Pulsebreak.Api.Tests;

/// <summary>
/// Matchmaking end to end, against the real API, PostgreSQL and Redis: guests and accounts
/// queue, game servers join the fleet and check in, and the matchmaker puts the two together.
/// Each test plays on its own game version, so its players and servers never meet another
/// test's.
/// </summary>
[TestFixture]
public class MatchmakingTests
{
    const string FleetKey = "test-fleet-key-0123456789-abcdefghijklmnop";
    static int _version = 9000;

    ApiFixture _api = null!;

    [OneTimeSetUp]
    public void Start() => _api = new ApiFixture(new Dictionary<string, string>
    {
        ["Matchmaking:FleetKey"] = FleetKey,
        ["Matchmaking:FillAfterSeconds"] = "2",
        ["Matchmaking:TicketIdleSeconds"] = "5",
        ["Matchmaking:HeartbeatSeconds"] = "1",
        ["Matchmaking:ServerTimeoutSeconds"] = "2",
        ["Matchmaking:IntervalMilliseconds"] = "100",
    });

    [OneTimeTearDown] public void Stop() => _api.Dispose();

    static uint NewVersion() => (uint)Interlocked.Increment(ref _version);

    // ------------------------------------------------------------------ helpers

    sealed class Guest(HttpClient http, string id, string name)
    {
        public HttpClient Http { get; } = http;
        public string Id { get; } = id;
        public string Name { get; } = name;
        public string Ticket = "";
    }

    async Task<Guest> GuestAsync(string name)
    {
        HttpClient http = _api.CreateClient();
        HttpResponseMessage response = await http.PostAsync("/v1/matchmaking/guest", new { name });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        JsonElement body = await response.JsonAsync();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return new Guest(http, body.GetProperty("guestId").GetString()!, body.GetProperty("name").GetString()!);
    }

    static async Task<JsonElement> QueueAsync(Guest guest, string playlist, uint version)
    {
        HttpResponseMessage response = await guest.Http.PostAsync("/v1/matchmaking/tickets", new { playlist, version });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        JsonElement ticket = await response.JsonAsync();
        guest.Ticket = ticket.GetProperty("id").GetString()!;
        return ticket;
    }

    static async Task<JsonElement> CheckAsync(Guest guest) =>
        await (await guest.Http.GetAsync("/v1/matchmaking/tickets/" + guest.Ticket)).JsonAsync();

    /// <summary>Asks about a ticket every so often, the way the game does, until it is no longer searching.</summary>
    static async Task<JsonElement> WaitAsync(Guest guest, double seconds)
    {
        JsonElement ticket = default;
        for (double waited = 0; waited < seconds; waited += 0.1)
        {
            ticket = await CheckAsync(guest);
            if (ticket.GetProperty("status").GetString() != "searching") return ticket;
            await Task.Delay(100);
        }
        return ticket;
    }

    HttpClient Fleet(string key = FleetKey)
    {
        HttpClient http = _api.CreateClient();
        http.DefaultRequestHeaders.Add(MatchmakingEndpoints.FleetKeyHeader, key);
        return http;
    }

    async Task<string> RegisterServerAsync(HttpClient fleet, uint version, int port = 7777)
    {
        HttpResponseMessage response = await fleet.PostAsync("/v1/fleet/servers", new { address = "203.0.113.5", port, version });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        JsonElement body = await response.JsonAsync();
        Assert.That(body.GetProperty("heartbeatSeconds").GetSingle(), Is.EqualTo(1f));
        return body.GetProperty("id").GetString()!;
    }

    static async Task<JsonElement> BeatAsync(HttpClient fleet, string server, string state, string matchId = "", int players = 0)
    {
        HttpResponseMessage response = await fleet.PostAsync("/v1/fleet/servers/" + server + "/heartbeat", new { state, matchId, players });
        return response.StatusCode == HttpStatusCode.OK ? await response.JsonAsync() : default;
    }

    /// <summary>Checks a server in every half second, as a real one does, until the test is over.</summary>
    static CancellationTokenSource KeepBeating(HttpClient fleet, string server, string state = "idle")
    {
        var stop = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await BeatAsync(fleet, server, state);
                try { await Task.Delay(500, stop.Token); } catch (OperationCanceledException) { }
            }
        });
        return stop;
    }

    // ------------------------------------------------------------------ tests

    [Test]
    public async Task AGuestPass_IsGoodForMatchmaking_AndNothingElse()
    {
        Guest guest = await GuestAsync("   Ada  the   Great!!<script> ");
        Assert.That(guest.Name, Is.EqualTo("Ada the Greatscript"), "names are tidied before anyone else sees them");
        Assert.That(guest.Id, Does.StartWith("guest:"));
        Assert.That((await GuestAsync("")).Name, Is.EqualTo("Player"));

        Assert.That((await guest.Http.GetAsync("/v1/me")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "a guest pass is not an account");
        Assert.That((await guest.Http.PostAsync("/v1/game/tickets", new { mode = "casual" })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        JsonElement ticket = await QueueAsync(guest, "tdm", NewVersion());
        Assert.That(ticket.GetProperty("status").GetString(), Is.EqualTo("searching"));
        Assert.That(ticket.GetProperty("playlist").GetString(), Is.EqualTo("tdm"));
        Assert.That(ticket.GetProperty("searching").GetInt32(), Is.EqualTo(1));

        HttpClient anonymous = _api.CreateClient();
        Assert.That((await anonymous.PostAsync("/v1/matchmaking/tickets", new { playlist = "tdm", version = 1 })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-pass");
        Assert.That((await anonymous.PostAsync("/v1/matchmaking/tickets", new { playlist = "tdm", version = 1 })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        Assert.That((int)(await guest.Http.PostAsync("/v1/matchmaking/tickets", new { playlist = "capture_the_flag", version = 1 })).StatusCode, Is.EqualTo(422));
        Assert.That((int)(await guest.Http.PostAsync("/v1/matchmaking/tickets", new { playlist = "tdm", version = 0 })).StatusCode, Is.EqualTo(422));

        JsonElement playlists = await (await anonymous.GetAsync("/v1/matchmaking/playlists")).JsonAsync();
        Assert.That(playlists.EnumerateArray().Select(p => p.GetProperty("id").GetString()),
            Is.EquivalentTo(new[] { "duel", "tdm", "ffa", "elimination", "domination", "hardpoint", "gungame", "duel_ranked", "elimination_ranked" }));
    }

    [Test]
    public async Task AFullQueue_GoesToAFreeServer_WithTicketsForThatMatchOnly()
    {
        uint version = NewVersion();
        HttpClient fleet = Fleet();
        string server = await RegisterServerAsync(fleet, version, 7801);
        JsonElement quiet = await BeatAsync(fleet, server, "idle");
        Assert.That(quiet.GetProperty("matchId").GetString(), Is.Empty, "nothing to play yet");

        Guest ada = await GuestAsync("Ada"), bo = await GuestAsync("Bo");
        await QueueAsync(ada, "duel", version);
        await QueueAsync(bo, "duel", version);
        JsonElement mine = await WaitAsync(ada, 3), theirs = await WaitAsync(bo, 3);

        Assert.That(mine.GetProperty("status").GetString(), Is.EqualTo("matched"));
        Assert.That(theirs.GetProperty("status").GetString(), Is.EqualTo("matched"));
        Assert.That(mine.GetProperty("address").GetString(), Is.EqualTo("203.0.113.5:7801"));
        string matchId = mine.GetProperty("matchId").GetString()!;
        Assert.That(theirs.GetProperty("matchId").GetString(), Is.EqualTo(matchId), "the two are in the same match");

        // Each join ticket names its player and the match, and checks out with the key game servers have.
        byte[] key = Encoding.UTF8.GetBytes(ApiFixture.TicketKey);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.That(GameTicket.TryVerify(mine.GetProperty("joinTicket").GetString(), key, now, out TicketClaims claims, out string problem), Is.True, problem);
        Assert.That(claims.AccountId, Is.EqualTo(ada.Id));
        Assert.That(claims.Name, Is.EqualTo("Ada"));
        Assert.That(claims.MatchId, Is.EqualTo(matchId));
        Assert.That(claims.Mode, Is.EqualTo(GameTicket.Casual));

        // The server hears about the match on its next check-in, until it says it has taken it on.
        JsonElement handed = await BeatAsync(fleet, server, "idle");
        Assert.That(handed.GetProperty("matchId").GetString(), Is.EqualTo(matchId));
        Assert.That(handed.GetProperty("playlist").GetString(), Is.EqualTo("duel"));
        Assert.That((await BeatAsync(fleet, server, "busy", matchId, 2)).GetProperty("matchId").GetString(), Is.Empty);

        // Once it is empty again it is free for the next match.
        Assert.That((await BeatAsync(fleet, server, "idle", matchId)).GetProperty("matchId").GetString(), Is.Empty);
        Guest cy = await GuestAsync("Cy"), di = await GuestAsync("Di");
        await QueueAsync(cy, "duel", version);
        await QueueAsync(di, "duel", version);
        JsonElement next = await WaitAsync(cy, 3);
        Assert.That(next.GetProperty("status").GetString(), Is.EqualTo("matched"));
        Assert.That(next.GetProperty("matchId").GetString(), Is.Not.EqualTo(matchId));
        Assert.That((await BeatAsync(fleet, server, "idle", matchId)).GetProperty("matchId").GetString(), Is.EqualTo(next.GetProperty("matchId").GetString()));
    }

    [Test]
    public async Task AQueueThatDoesNotFill_StartsAfterAWait_WhenBotsCanFillIn()
    {
        uint version = NewVersion();
        HttpClient fleet = Fleet();
        string server = await RegisterServerAsync(fleet, version);
        using CancellationTokenSource beating = KeepBeating(fleet, server);

        // Team Deathmatch is for ten, but bots take the empty places: one person is enough, after the wait.
        Guest ada = await GuestAsync("Ada");
        await QueueAsync(ada, "tdm", version);
        await Task.Delay(800);
        Assert.That((await CheckAsync(ada)).GetProperty("status").GetString(), Is.EqualTo("searching"), "a moment's wait for others first");
        Assert.That((await WaitAsync(ada, 4)).GetProperty("status").GetString(), Is.EqualTo("matched"));

        // A duel needs two people however long anyone waits.
        string second = await RegisterServerAsync(fleet, version, 7802);
        using CancellationTokenSource alsoBeating = KeepBeating(fleet, second);
        Guest bo = await GuestAsync("Bo");
        await QueueAsync(bo, "duel", version);
        Assert.That((await WaitAsync(bo, 3.5)).GetProperty("status").GetString(), Is.EqualTo("searching"));
        beating.Cancel();
        alsoBeating.Cancel();
    }

    [Test]
    public async Task PlayersWait_ForAFreeServer_OfTheirOwnVersion()
    {
        uint version = NewVersion();
        HttpClient fleet = Fleet();
        Guest ada = await GuestAsync("Ada"), bo = await GuestAsync("Bo");
        await QueueAsync(ada, "duel", version);
        await QueueAsync(bo, "duel", version);

        // A server on another version, and one that is busy, are no use.
        string other = await RegisterServerAsync(fleet, version + 1000);
        await BeatAsync(fleet, other, "idle");
        string busy = await RegisterServerAsync(fleet, version, 7803);
        await BeatAsync(fleet, busy, "busy", "", 6);
        Assert.That((await WaitAsync(ada, 1.5)).GetProperty("status").GetString(), Is.EqualTo("searching"));
        Assert.That((await CheckAsync(ada)).GetProperty("searching").GetInt32(), Is.EqualTo(2));

        await BeatAsync(fleet, busy, "idle");
        JsonElement matched = await WaitAsync(ada, 3);
        Assert.That(matched.GetProperty("status").GetString(), Is.EqualTo("matched"));
        Assert.That(matched.GetProperty("address").GetString(), Does.EndWith(":7803"));
    }

    [Test]
    public async Task APlaceCanBeGivenUp_OnlyByItsOwner_AndJoiningAgainReplacesIt()
    {
        uint version = NewVersion();
        Guest ada = await GuestAsync("Ada"), eve = await GuestAsync("Eve");
        await QueueAsync(ada, "ffa", version);
        string first = ada.Ticket;

        HttpResponseMessage stolen = await eve.Http.DeleteAsync("/v1/matchmaking/tickets/" + first);
        Assert.That(stolen.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await eve.Http.GetAsync("/v1/matchmaking/tickets/" + first)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "nor can anyone else look at it");

        await QueueAsync(ada, "gungame", version);
        Assert.That((await ada.Http.GetAsync("/v1/matchmaking/tickets/" + first)).JsonAsync().Result.GetProperty("status").GetString(),
            Is.EqualTo("cancelled"), "one search at a time");

        Assert.That((await ada.Http.DeleteAsync("/v1/matchmaking/tickets/" + ada.Ticket)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await CheckAsync(ada)).GetProperty("status").GetString(), Is.EqualTo("cancelled"));
    }

    [Test]
    public async Task APlayerWhoStopsAsking_IsTakenOutOfTheQueue()
    {
        uint version = NewVersion();
        Guest ada = await GuestAsync("Ada");
        await QueueAsync(ada, "elimination", version);
        await Task.Delay(TimeSpan.FromSeconds(6.5));
        Assert.That((await CheckAsync(ada)).GetProperty("status").GetString(), Is.EqualTo("expired"));
    }

    [Test]
    public async Task OnlyGameServersWithTheFleetKey_JoinTheFleet()
    {
        uint version = NewVersion();
        HttpResponseMessage wrong = await Fleet("someone-elses-key-0123456789-abcdefghijklm").PostAsync("/v1/fleet/servers",
            new { address = "198.51.100.7", port = 7777, version });
        Assert.That(wrong.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await _api.CreateClient().PostAsync("/v1/fleet/servers", new { address = "198.51.100.7", port = 7777, version })).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));

        HttpClient fleet = Fleet();
        Assert.That((int)(await fleet.PostAsync("/v1/fleet/servers", new { address = "", port = 7777, version })).StatusCode, Is.EqualTo(422));
        Assert.That((int)(await fleet.PostAsync("/v1/fleet/servers", new { address = "198.51.100.7", port = 70000, version })).StatusCode, Is.EqualTo(422));

        // A server that stops checking in is forgotten, and told so when it comes back.
        string server = await RegisterServerAsync(fleet, version);
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        HttpResponseMessage late = await fleet.PostAsync("/v1/fleet/servers/" + server + "/heartbeat", new { state = "idle", matchId = "", players = 0 });
        Assert.That(late.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await late.ErrorCodeAsync(), Is.EqualTo("unknown_server"));
    }

    [Test]
    public async Task ASignedInPlayer_QueuesAsTheirAccount()
    {
        uint version = NewVersion();
        HttpClient fleet = Fleet();
        string server = await RegisterServerAsync(fleet, version);
        using CancellationTokenSource beating = KeepBeating(fleet, server);

        TestPlayer player = await _api.RegisterAsync();
        HttpResponseMessage queued = await player.Http.PostAsync("/v1/matchmaking/tickets", new { playlist = "hardpoint", version });
        Assert.That(queued.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        string id = (await queued.JsonAsync()).GetProperty("id").GetString()!;

        JsonElement ticket = default;
        for (int i = 0; i < 40; i++)
        {
            ticket = await (await player.Http.GetAsync("/v1/matchmaking/tickets/" + id)).JsonAsync();
            if (ticket.GetProperty("status").GetString() == "matched") break;
            await Task.Delay(100);
        }
        Assert.That(ticket.GetProperty("status").GetString(), Is.EqualTo("matched"));
        Assert.That(GameTicket.TryVerify(ticket.GetProperty("joinTicket").GetString(), Encoding.UTF8.GetBytes(ApiFixture.TicketKey),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out TicketClaims claims, out _), Is.True);
        Assert.That(claims.AccountId, Is.EqualTo(player.Id));
        Assert.That(claims.Name, Is.EqualTo(player.Username));
        beating.Cancel();
    }
}

[TestFixture]
public class FleetSwitchedOffTests
{
    [Test]
    public async Task WithoutAFleetKey_NoServerCanJoin()
    {
        using var api = new ApiFixture(new Dictionary<string, string> { ["Matchmaking:FleetKey"] = "" });
        HttpClient http = api.CreateClient();
        http.DefaultRequestHeaders.Add(MatchmakingEndpoints.FleetKeyHeader, "");
        HttpResponseMessage response = await http.PostAsync("/v1/fleet/servers", new { address = "198.51.100.7", port = 7777, version = 1 });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(await response.ErrorCodeAsync(), Is.EqualTo("fleet_disabled"));
    }
}
