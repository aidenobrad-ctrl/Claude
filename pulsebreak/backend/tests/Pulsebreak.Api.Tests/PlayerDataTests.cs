using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Net;

namespace Pulsebreak.Api.Tests;

[TestFixture]
public class LoadoutSyncTests
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { IncludeFields = true };
    ApiFixture _api = null!;
    long _now;

    [OneTimeSetUp] public void Start() => _api = new ApiFixture();
    [OneTimeTearDown] public void Stop() => _api.Dispose();
    [SetUp] public void Clock() => _now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    static LoadoutData Loadout(string id, string name, long updatedAt, string primary = "ar_hadron") => new()
    {
        id = id, name = name, updatedAt = updatedAt,
        primary = new WeaponBuildData { weaponId = primary, attachments = ["sight_red_dot", "grip_vertical"] },
        secondary = new WeaponBuildData { weaponId = "pistol_notch" },
        melee = new WeaponBuildData { weaponId = "melee_field_knife" },
        utility = "frag_grenade", perkSet = "vanguard",
    };

    static async Task<LoadoutCollection> SyncAsync(TestPlayer device, LoadoutCollection mine)
    {
        HttpResponseMessage response = await device.Http.PostAsync("/v1/loadouts/sync", JsonContent.Create(mine, options: Json));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LoadoutCollection>(Json))!;
    }

    [Test]
    public async Task LoadoutsSavedOnOneDevice_ArriveOnAnother()
    {
        TestPlayer desktop = await _api.RegisterAsync();
        LoadoutCollection empty = await SyncAsync(desktop, new LoadoutCollection());
        Assert.That(empty.loadouts, Is.Empty);
        Assert.That(empty.version, Is.EqualTo(LoadoutCollection.CurrentVersion));

        LoadoutCollection saved = await SyncAsync(desktop, new LoadoutCollection
        {
            equippedId = "mine-1", equippedUpdatedAt = _now - 50,
            loadouts = [Loadout("mine-1", "Close quarters", _now - 100), Loadout("mine-2", "Long range", _now - 90, "sr_aphelion")],
        });
        Assert.That(saved.loadouts.Select(l => l.id), Is.EqualTo(new[] { "mine-1", "mine-2" }));

        // A second machine signs in with nothing saved and receives everything.
        TestPlayer laptop = await _api.LoginElsewhereAsync(desktop);
        LoadoutCollection received = await SyncAsync(laptop, new LoadoutCollection());
        Assert.That(received.loadouts.Select(l => l.name), Is.EqualTo(new[] { "Close quarters", "Long range" }));
        Assert.That(received.equippedId, Is.EqualTo("mine-1"));
        LoadoutData first = received.loadouts[0];
        Assert.That(first.primary.weaponId, Is.EqualTo("ar_hadron"));
        Assert.That(first.primary.attachments, Is.EqualTo(new[] { "sight_red_dot", "grip_vertical" }));
        Assert.That(first.secondary.weaponId, Is.EqualTo("pistol_notch"));
        Assert.That(first.utility, Is.EqualTo("frag_grenade"));
        Assert.That(first.perkSet, Is.EqualTo("vanguard"));
        Assert.That(first.updatedAt, Is.EqualTo(_now - 100));

        // Reading without sending anything gives the same.
        var read = await (await laptop.Http.GetAsync("/v1/loadouts")).Content.ReadFromJsonAsync<LoadoutCollection>(Json);
        Assert.That(read!.loadouts.Select(l => l.id), Is.EqualTo(new[] { "mine-1", "mine-2" }));

        // And syncing again changes nothing.
        LoadoutCollection again = await SyncAsync(laptop, received);
        Assert.That(JsonSerializer.Serialize(again, Json), Is.EqualTo(JsonSerializer.Serialize(received, Json)));
    }

    [Test]
    public async Task WhenTwoDevicesChangeTheSameLoadout_TheNewerChangeWins_OnBoth()
    {
        TestPlayer desktop = await _api.RegisterAsync();
        TestPlayer laptop = await _api.LoginElsewhereAsync(desktop);
        await SyncAsync(desktop, new LoadoutCollection { loadouts = [Loadout("shared", "Original", _now - 100)] });

        // Offline, each changes it. The laptop's change is the later one, but the desktop syncs last.
        await SyncAsync(laptop, new LoadoutCollection { loadouts = [Loadout("shared", "Laptop's version", _now - 20, "smg_flit")] });
        LoadoutCollection onDesktop = await SyncAsync(desktop, new LoadoutCollection { loadouts = [Loadout("shared", "Desktop's version", _now - 60, "lmg_bulwark")] });

        Assert.That(onDesktop.loadouts.Single().name, Is.EqualTo("Laptop's version"));
        Assert.That(onDesktop.loadouts.Single().primary.weaponId, Is.EqualTo("smg_flit"));
        Assert.That((await SyncAsync(laptop, new LoadoutCollection())).loadouts.Single().name, Is.EqualTo("Laptop's version"));
    }

    [Test]
    public async Task ADeletedLoadout_StaysDeleted_EvenIfAnotherDeviceStillHasIt()
    {
        TestPlayer desktop = await _api.RegisterAsync();
        TestPlayer laptop = await _api.LoginElsewhereAsync(desktop);
        LoadoutData doomed = Loadout("doomed", "Not for long", _now - 100);
        await SyncAsync(desktop, new LoadoutCollection { loadouts = [doomed, Loadout("keeper", "Keeper", _now - 100)] });
        await SyncAsync(laptop, new LoadoutCollection());

        LoadoutCollection afterDelete = await SyncAsync(desktop, new LoadoutCollection
        {
            loadouts = [Loadout("keeper", "Keeper", _now - 100)],
            deleted = [new DeletedLoadout { id = "doomed", deletedAt = _now - 30 }],
        });
        Assert.That(afterDelete.loadouts.Select(l => l.id), Is.EqualTo(new[] { "keeper" }));

        // The laptop still has its old copy and sends it up. The deletion is newer, so it loses.
        LoadoutCollection onLaptop = await SyncAsync(laptop, new LoadoutCollection { loadouts = [doomed, Loadout("keeper", "Keeper", _now - 100)] });
        Assert.That(onLaptop.loadouts.Select(l => l.id), Is.EqualTo(new[] { "keeper" }));
        Assert.That(onLaptop.deleted.Select(d => d.id), Does.Contain("doomed"));

        // Unless it was edited after the deletion: then the edit is the newest word and it comes back.
        LoadoutCollection revived = await SyncAsync(laptop, new LoadoutCollection { loadouts = [Loadout("doomed", "Back again", _now - 5)] });
        Assert.That(revived.loadouts.Select(l => l.name), Does.Contain("Back again"));
        Assert.That(revived.deleted.Select(d => d.id), Does.Not.Contain("doomed"));
    }

    [Test]
    public async Task TheEquippedLoadout_FollowsTheLatestChoice()
    {
        TestPlayer desktop = await _api.RegisterAsync();
        TestPlayer laptop = await _api.LoginElsewhereAsync(desktop);
        await SyncAsync(desktop, new LoadoutCollection { equippedId = "assault", equippedUpdatedAt = _now - 80 });
        Assert.That((await SyncAsync(laptop, new LoadoutCollection { equippedId = "marksman", equippedUpdatedAt = _now - 40 })).equippedId, Is.EqualTo("marksman"));
        Assert.That((await SyncAsync(desktop, new LoadoutCollection { equippedId = "assault", equippedUpdatedAt = _now - 80 })).equippedId, Is.EqualTo("marksman"));
    }

    [Test]
    public async Task OnePlayersLoadouts_AreNobodyElses()
    {
        TestPlayer ada = await _api.RegisterAsync();
        TestPlayer bo = await _api.RegisterAsync();
        await SyncAsync(ada, new LoadoutCollection { loadouts = [Loadout("same-id", "Ada's", _now - 10)] });
        await SyncAsync(bo, new LoadoutCollection { loadouts = [Loadout("same-id", "Bo's", _now - 5)] });

        Assert.That((await SyncAsync(ada, new LoadoutCollection())).loadouts.Single().name, Is.EqualTo("Ada's"));
        Assert.That((await SyncAsync(bo, new LoadoutCollection())).loadouts.Single().name, Is.EqualTo("Bo's"));
    }

    [Test]
    public async Task WhatIsStored_IsTidied_AndAClockSetInTheFutureDoesNotWinForever()
    {
        TestPlayer player = await _api.RegisterAsync();
        LoadoutData messy = Loadout("messy", "  A name that is far too long to keep\n\t ", _now + 10_000_000);
        LoadoutCollection stored = await SyncAsync(player, new LoadoutCollection { loadouts = [messy], equippedId = "messy", equippedUpdatedAt = long.MaxValue });

        LoadoutData tidy = stored.loadouts.Single();
        Assert.That(tidy.name, Is.EqualTo("A name that is far too l"));
        Assert.That(tidy.name, Has.Length.LessThanOrEqualTo(24));
        Assert.That(tidy.updatedAt, Is.InRange(_now - 5, _now + 5), "a timestamp from the far future is treated as now");
        Assert.That(stored.equippedUpdatedAt, Is.InRange(_now - 5, _now + 5));

        // So an honest edit a moment later still wins.
        await Task.Delay(1100);
        long later = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.That((await SyncAsync(player, new LoadoutCollection { loadouts = [Loadout("messy", "Fixed", later)] })).loadouts.Single().name, Is.EqualTo("Fixed"));

        // Unknown extra fields are dropped rather than stored.
        string withExtras = """{"loadouts":[{"id":"extra","name":"Extra","updatedAt":5,"cheat":true,"primary":{"weaponId":"ar_hadron","damage":9999}}]}""";
        await player.Http.PostAsync("/v1/loadouts/sync", new StringContent(withExtras, Encoding.UTF8, "application/json"));
        string raw = await _api.WithDbAsync(async db => (await db.Loadouts.FindAsync(Guid.Parse(player.Id), "extra"))!.Data);
        Assert.That(raw, Does.Not.Contain("cheat").And.Not.Contain("9999"));
        Assert.That(raw, Does.Contain("ar_hadron"));
    }

    [Test]
    public async Task MalformedLoadouts_AreRefused()
    {
        TestPlayer player = await _api.RegisterAsync();
        var cases = new (string why, LoadoutCollection body)[]
        {
            ("no id", new LoadoutCollection { loadouts = [Loadout("", "x", 1)] }),
            ("an id with a space", new LoadoutCollection { loadouts = [Loadout("has space", "x", 1)] }),
            ("an id that is a path", new LoadoutCollection { loadouts = [Loadout("../../etc", "x", 1)] }),
            ("a huge id", new LoadoutCollection { loadouts = [Loadout(new string('a', 65), "x", 1)] }),
            ("a weapon id with markup", new LoadoutCollection { loadouts = [Loadout("ok", "x", 1, "<script>")] }),
            ("too many attachments", new LoadoutCollection { loadouts = [new LoadoutData { id = "ok", primary = new WeaponBuildData { weaponId = "a", attachments = Enumerable.Repeat("x", 9).ToArray() } }] }),
            ("too many loadouts", new LoadoutCollection { loadouts = Enumerable.Range(0, 51).Select(i => Loadout("l" + i, "x", i)).ToArray() }),
            ("a deletion with no id", new LoadoutCollection { deleted = [new DeletedLoadout { id = "", deletedAt = 1 }] }),
            ("a bad equipped id", new LoadoutCollection { equippedId = "not valid!" }),
        };
        foreach ((string why, LoadoutCollection body) in cases)
        {
            HttpResponseMessage response = await player.Http.PostAsync("/v1/loadouts/sync", JsonContent.Create(body, options: Json));
            Assert.That((int)response.StatusCode, Is.EqualTo(422), why);
            Assert.That(await response.ErrorCodeAsync(), Is.EqualTo("invalid_request"), why);
        }
        Assert.That((await SyncAsync(player, new LoadoutCollection())).loadouts, Is.Empty, "nothing from the refused requests was stored");

        HttpResponseMessage nothing = await player.Http.PostAsync("/v1/loadouts/sync", new StringContent("null", Encoding.UTF8, "application/json"));
        Assert.That((int)nothing.StatusCode, Is.EqualTo(422));
        HttpResponseMessage huge = await player.Http.PostAsync("/v1/loadouts/sync", new StringContent("{\"equippedId\":\"" + new string('a', 100_000) + "\"}", Encoding.UTF8, "application/json"));
        Assert.That((int)huge.StatusCode, Is.EqualTo(413), "a body over the size cap is turned away unread");
    }

    [Test]
    public async Task MoreThanTenLoadouts_AreCutToTheTenMostRecentlyChanged()
    {
        TestPlayer player = await _api.RegisterAsync();
        LoadoutCollection stored = await SyncAsync(player, new LoadoutCollection
        {
            loadouts = Enumerable.Range(0, 14).Select(i => Loadout("l" + i, "Loadout " + i, _now - 1000 + i)).ToArray(),
        });
        Assert.That(stored.loadouts, Has.Length.EqualTo(10));
        Assert.That(stored.loadouts.Select(l => l.id), Is.EqualTo(Enumerable.Range(4, 10).Select(i => "l" + i)));
        Assert.That(stored.deleted.Select(d => d.id), Is.EquivalentTo(new[] { "l0", "l1", "l2", "l3" }));
    }

    [Test]
    public async Task TwoDevicesSyncingAtTheSameInstant_BothKeepTheirChanges()
    {
        TestPlayer desktop = await _api.RegisterAsync();
        TestPlayer laptop = await _api.LoginElsewhereAsync(desktop);
        for (int round = 0; round < 5; round++)
        {
            await Task.WhenAll(
                SyncAsync(desktop, new LoadoutCollection { loadouts = [Loadout("desktop-" + round, "D" + round, _now - 50 + round)] }),
                SyncAsync(laptop, new LoadoutCollection { loadouts = [Loadout("laptop-" + round, "L" + round, _now - 50 + round)] }));
        }
        LoadoutCollection all = await SyncAsync(desktop, new LoadoutCollection());
        Assert.That(all.loadouts, Has.Length.EqualTo(10));
        Assert.That(all.loadouts.Count(l => l.id.StartsWith("desktop-")), Is.EqualTo(5));
        Assert.That(all.loadouts.Count(l => l.id.StartsWith("laptop-")), Is.EqualTo(5));
    }
}

[TestFixture]
public class SettingsAndTicketTests
{
    ApiFixture _api = null!;

    [OneTimeSetUp] public void Start() => _api = new ApiFixture();
    [OneTimeTearDown] public void Stop() => _api.Dispose();

    [Test]
    public async Task Settings_FollowTheAccount_AndTheNewestSaveWins()
    {
        TestPlayer desktop = await _api.RegisterAsync();
        TestPlayer laptop = await _api.LoginElsewhereAsync(desktop);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        JsonElement none = await (await desktop.Http.GetAsync("/v1/settings")).JsonAsync();
        Assert.That(none.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(none.GetProperty("updatedAt").GetInt64(), Is.EqualTo(0));

        var mine = new { data = new { sensitivity = 3.5f, adsSensitivityMultiplier = 0.8f, fieldOfView = 110f, showDamageNumbers = false, masterVolume = 0.4f }, updatedAt = now - 50 };
        Assert.That((await desktop.Http.PutAsJsonAsync("/v1/settings", mine)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        JsonElement onLaptop = await (await laptop.Http.GetAsync("/v1/settings")).JsonAsync();
        Assert.That(onLaptop.GetProperty("data").GetProperty("sensitivity").GetSingle(), Is.EqualTo(3.5f));
        Assert.That(onLaptop.GetProperty("data").GetProperty("fieldOfView").GetSingle(), Is.EqualTo(110f));
        Assert.That(onLaptop.GetProperty("data").GetProperty("showDamageNumbers").GetBoolean(), Is.False);
        Assert.That(onLaptop.GetProperty("updatedAt").GetInt64(), Is.EqualTo(now - 50));

        // An older save from the laptop does not overwrite it; the reply tells the laptop what is current.
        var stale = new { data = new { sensitivity = 1f, adsSensitivityMultiplier = 1f, fieldOfView = 90f, showDamageNumbers = true, masterVolume = 1f }, updatedAt = now - 500 };
        JsonElement reply = await (await laptop.Http.PutAsJsonAsync("/v1/settings", stale)).JsonAsync();
        Assert.That(reply.GetProperty("data").GetProperty("sensitivity").GetSingle(), Is.EqualTo(3.5f));

        // Values outside what the game allows are pulled back in range.
        var wild = new { data = new { sensitivity = 9999f, adsSensitivityMultiplier = -4f, fieldOfView = 360f, showDamageNumbers = true, masterVolume = 50f }, updatedAt = now };
        JsonElement clamped = (await (await laptop.Http.PutAsJsonAsync("/v1/settings", wild)).JsonAsync()).GetProperty("data");
        Assert.That(clamped.GetProperty("sensitivity").GetSingle(), Is.EqualTo(8f));
        Assert.That(clamped.GetProperty("adsSensitivityMultiplier").GetSingle(), Is.EqualTo(0.3f));
        Assert.That(clamped.GetProperty("fieldOfView").GetSingle(), Is.EqualTo(120f));
        Assert.That(clamped.GetProperty("masterVolume").GetSingle(), Is.EqualTo(1f));

        Assert.That((int)(await laptop.Http.PutAsJsonAsync("/v1/settings", new { updatedAt = now })).StatusCode, Is.EqualTo(422));
    }

    [Test]
    public async Task ATicket_SaysWhoYouAre_AndAGameServerCanCheckItByItself()
    {
        TestPlayer player = await _api.RegisterAsync();
        HttpResponseMessage response = await player.Http.PostAsync("/v1/game/tickets", new { mode = "casual" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        JsonElement body = await response.JsonAsync();
        string ticket = body.GetProperty("ticket").GetString()!;
        Assert.That(body.GetProperty("expiresIn").GetInt32(), Is.EqualTo(60));

        // Exactly what a game server does: the shared key, the ticket, the time. No call back to the API.
        byte[] key = Encoding.UTF8.GetBytes(ApiFixture.TicketKey);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.That(GameTicket.TryVerify(ticket, key, now, out TicketClaims claims, out string problem), Is.True, problem);
        Assert.That(claims.AccountId, Is.EqualTo(player.Id));
        Assert.That(claims.Name, Is.EqualTo(player.Username));
        Assert.That(claims.EmailVerified, Is.False);
        Assert.That(claims.Mode, Is.EqualTo("casual"));
        Assert.That(claims.ExpiresAt, Is.InRange(now + 55, now + 65));

        Assert.That(GameTicket.TryVerify(ticket, key, now + 120, out _, out problem), Is.False);
        Assert.That(problem, Does.Contain("expired"));
        Assert.That(GameTicket.TryVerify(ticket, Encoding.UTF8.GetBytes("a-different-key-0123456789-abcdefghijkl"), now, out _, out problem), Is.False);
        Assert.That(problem, Does.Contain("signature"));

        // A ticket is not an access token, and an access token is not a ticket.
        var asToken = new HttpRequestMessage(HttpMethod.Get, "/v1/me");
        asToken.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ticket);
        Assert.That((await _api.CreateClient().SendAsync(asToken)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(GameTicket.TryVerify(player.AccessToken, Encoding.UTF8.GetBytes(ApiFixture.JwtKey), now, out _, out _), Is.False);

        Assert.That((int)(await player.Http.PostAsync("/v1/game/tickets", new { mode = "deathmatch" })).StatusCode, Is.EqualTo(422));
        Assert.That((await player.Http.PostAsync("/v1/game/tickets", new { })).StatusCode, Is.EqualTo(HttpStatusCode.OK), "casual when not said");
    }

    [Test]
    public async Task RankedPlay_NeedsAVerifiedEmail()
    {
        TestPlayer player = await _api.RegisterAsync();

        HttpResponseMessage refused = await player.Http.PostAsync("/v1/game/tickets", new { mode = "ranked" });
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(await refused.ErrorCodeAsync(), Is.EqualTo("email_not_verified"));
        Assert.That((await player.Http.PostAsync("/v1/game/tickets", new { mode = "casual" })).StatusCode, Is.EqualTo(HttpStatusCode.OK), "casual play is open to everyone");

        await _api.VerifyEmailAsync(player);

        // Straight away, with the access token from before verifying: the check is against the account, not the token.
        HttpResponseMessage allowed = await player.Http.PostAsync("/v1/game/tickets", new { mode = "RANKED" });
        Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        string ticket = (await allowed.JsonAsync()).GetProperty("ticket").GetString()!;
        Assert.That(GameTicket.TryVerify(ticket, Encoding.UTF8.GetBytes(ApiFixture.TicketKey), DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out TicketClaims claims, out _), Is.True);
        Assert.That(claims.Mode, Is.EqualTo("ranked"));
        Assert.That(claims.EmailVerified, Is.True);
    }

    [Test]
    public async Task ADisabledAccount_IsShutOut_EvenWithAValidToken()
    {
        TestPlayer player = await _api.RegisterAsync();
        await _api.WithDbAsync(async db =>
        {
            Data.Account account = (await db.Accounts.FindAsync(Guid.Parse(player.Id)))!;
            account.Disabled = true;
            return await db.SaveChangesAsync();
        });

        Assert.That((await player.Http.GetAsync("/v1/me")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await player.Http.PostAsync("/v1/game/tickets", new { mode = "casual" })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await _api.CreateClient().PostAsync("/v1/auth/refresh", new { refreshToken = player.RefreshToken })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        HttpResponseMessage login = await _api.CreateClient().PostAsync("/v1/auth/login", new { login = player.Email, password = player.Password });
        Assert.That(await login.ErrorCodeAsync(), Is.EqualTo("account_disabled"));
    }
}
