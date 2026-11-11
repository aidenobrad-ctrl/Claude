using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Pulsebreak.Api.Tests;

[TestFixture]
public class AuthTests
{
    ApiFixture _api = null!;

    [OneTimeSetUp] public void Start() => _api = new ApiFixture();
    [OneTimeTearDown] public void Stop() => _api.Dispose();

    // ------------------------------------------------------------------ signing up

    [Test]
    public async Task SigningUp_CreatesAnAccount_SignsYouIn_AndEmailsACode()
    {
        HttpClient http = _api.CreateClient();
        string name = TestHttp.Unique("Ada");
        HttpResponseMessage response = await http.PostAsync("/v1/auth/register", new { email = $"  {name}@Example.com ", username = name, password = "a long enough passphrase" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        JsonElement session = await response.JsonAsync();
        Assert.That(session.GetProperty("accessToken").GetString(), Is.Not.Empty);
        Assert.That(session.GetProperty("refreshToken").GetString(), Has.Length.GreaterThan(30));
        Assert.That(session.GetProperty("expiresIn").GetInt32(), Is.EqualTo(900));
        JsonElement account = session.GetProperty("account");
        Assert.That(account.GetProperty("username").GetString(), Is.EqualTo(name));
        Assert.That(account.GetProperty("email").GetString(), Is.EqualTo($"{name}@Example.com"), "kept as typed, minus the spaces");
        Assert.That(account.GetProperty("emailVerified").GetBoolean(), Is.False);
        Assert.That(account.GetProperty("profile").GetProperty("level").GetInt32(), Is.EqualTo(1));
        Assert.That(account.GetProperty("profile").GetProperty("rating").GetInt32(), Is.EqualTo(1000));

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        JsonElement me = await (await http.GetAsync("/v1/me")).JsonAsync();
        Assert.That(me.GetProperty("id").GetString(), Is.EqualTo(account.GetProperty("id").GetString()));

        Email.EmailMessage mail = await _api.Mail.WaitForAsync($"{name}@Example.com");
        Assert.That(CapturedMail.VerifyCode(mail), Has.Length.EqualTo(6));
        Assert.That(CapturedMail.VerifyLink(mail), Does.StartWith("/v1/auth/verify-email?token="));
    }

    [Test]
    public async Task ThePassword_IsStoredOnlyAsAnArgon2idHash()
    {
        const string password = "a very particular passphrase";
        TestPlayer player = await _api.RegisterAsync(password);
        string stored = await _api.WithDbAsync(db => db.Accounts.Where(a => a.Username == player.Username).Select(a => a.PasswordHash).SingleAsync());

        Assert.That(stored, Does.StartWith("$argon2id$v=19$m="));
        Assert.That(stored, Does.Not.Contain(password));
        Assert.That(stored.Split('$'), Has.Length.EqualTo(6));

        // Two accounts with the same password do not get the same hash.
        TestPlayer other = await _api.RegisterAsync(password);
        string otherStored = await _api.WithDbAsync(db => db.Accounts.Where(a => a.Username == other.Username).Select(a => a.PasswordHash).SingleAsync());
        Assert.That(otherStored, Is.Not.EqualTo(stored));
    }

    [Test]
    public async Task SigningUp_WithBadDetails_SaysWhichFieldsAreWrong()
    {
        HttpClient http = _api.CreateClient();

        HttpResponseMessage everything = await http.PostAsync("/v1/auth/register", new { email = "not-an-email", username = "a!", password = "short" });
        Assert.That((int)everything.StatusCode, Is.EqualTo(422));
        Assert.That(await everything.ErrorCodeAsync(), Is.EqualTo("invalid_request"));
        Assert.That(await everything.ErrorFieldsAsync(), Is.EquivalentTo(new[] { "email", "username", "password" }));

        HttpResponseMessage empty = await http.PostAsync("/v1/auth/register", new { });
        Assert.That(await empty.ErrorFieldsAsync(), Is.EquivalentTo(new[] { "email", "username", "password" }));

        string name = TestHttp.Unique("Bo");
        foreach ((object body, string field) in new (object, string)[]
                 {
                     (new { email = $"{name}@example.com", username = name, password = "1234567890" }, "password"),                 // on everyone's list
                     (new { email = $"{name}@example.com", username = name, password = "aaaaaaaaaaaaaa" }, "password"),             // one character
                     (new { email = $"{name}@example.com", username = name, password = $"x{name}123456" }, "password"),            // contains the username
                     (new { email = $"{name}@example.com", username = name, password = new string('p', 129) + "Q1!" }, "password"), // absurdly long
                     (new { email = $"{name}@example.com", username = "admin", password = "a fine passphrase" }, "username"),       // reserved
                     (new { email = $"{name}@example.com", username = "has space", password = "a fine passphrase" }, "username"),
                     (new { email = $"{name}@example", username = name, password = "a fine passphrase" }, "email"),
                     (new { email = $"{name}@@example.com", username = name, password = "a fine passphrase" }, "email"),
                 })
        {
            HttpResponseMessage response = await http.PostAsync("/v1/auth/register", body);
            Assert.That((int)response.StatusCode, Is.EqualTo(422), JsonSerializer.Serialize(body));
            Assert.That(await response.ErrorFieldsAsync(), Is.EqualTo(new[] { field }), JsonSerializer.Serialize(body));
        }
    }

    [Test]
    public async Task ATakenEmailOrUsername_IsRefused_WhateverTheCapitals()
    {
        TestPlayer first = await _api.RegisterAsync();
        HttpClient http = _api.CreateClient();

        HttpResponseMessage sameEmail = await http.PostAsync("/v1/auth/register",
            new { email = first.Email.ToUpperInvariant(), username = TestHttp.Unique("New"), password = "another passphrase" });
        Assert.That(sameEmail.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await sameEmail.ErrorFieldsAsync(), Is.EqualTo(new[] { "email" }));

        HttpResponseMessage sameName = await http.PostAsync("/v1/auth/register",
            new { email = TestHttp.Unique("new") + "@example.com", username = first.Username.ToUpperInvariant(), password = "another passphrase" });
        Assert.That(sameName.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await sameName.ErrorFieldsAsync(), Is.EqualTo(new[] { "username" }));
    }

    [Test]
    public async Task TwoSignUpsRacingForOneName_CannotBothWin()
    {
        string name = TestHttp.Unique("Race");
        Task<HttpResponseMessage>[] attempts = Enumerable.Range(0, 8).Select(i => _api.CreateClient().PostAsync("/v1/auth/register",
            new { email = $"{name}{i}@example.com", username = name, password = "a fine passphrase" })).ToArray();
        HttpResponseMessage[] responses = await Task.WhenAll(attempts);

        Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.Created), Is.EqualTo(1));
        Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.Conflict), Is.EqualTo(7));
        Assert.That(await _api.WithDbAsync(db => db.Accounts.CountAsync(a => a.UsernameNormalized == name.ToLowerInvariant())), Is.EqualTo(1));
    }

    // ------------------------------------------------------------------ signing in

    [Test]
    public async Task SigningIn_WorksWithEmailOrUsername_InAnyCapitals()
    {
        TestPlayer player = await _api.RegisterAsync();
        HttpClient http = _api.CreateClient();

        foreach (string login in new[] { player.Email, player.Email.ToUpperInvariant(), player.Username, player.Username.ToLowerInvariant(), "  " + player.Username + " " })
        {
            HttpResponseMessage response = await http.PostAsync("/v1/auth/login", new { login, password = player.Password });
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), login);
            Assert.That((await response.JsonAsync()).GetProperty("account").GetProperty("id").GetString(), Is.EqualTo(player.Id));
        }
    }

    [Test]
    public async Task AWrongPassword_AndAnAccountThatDoesNotExist_GetTheSameAnswer()
    {
        TestPlayer player = await _api.RegisterAsync();
        HttpClient http = _api.CreateClient();

        HttpResponseMessage wrong = await http.PostAsync("/v1/auth/login", new { login = player.Email, password = "not the password" });
        HttpResponseMessage nobody = await http.PostAsync("/v1/auth/login", new { login = TestHttp.Unique("ghost") + "@example.com", password = "not the password" });

        Assert.That(wrong.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(nobody.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await nobody.Content.ReadAsStringAsync(), Is.EqualTo(await wrong.Content.ReadAsStringAsync()));

        foreach (object body in new object[] { new { }, new { login = player.Email }, new { password = "x" }, new { login = new string('a', 400), password = "x" } })
            Assert.That((await http.PostAsync("/v1/auth/login", body)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task RepeatedWrongPasswords_LockTheAccount_EvenAgainstTheRightOne_ForAWhile()
    {
        TestPlayer player = await _api.RegisterAsync();
        HttpClient http = _api.CreateClient();

        for (int i = 0; i < 5; i++)
            Assert.That((await http.PostAsync("/v1/auth/login", new { login = player.Email, password = "guess number " + i })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        // Locked now. The right password does not get in either, and by username no more than by email.
        HttpResponseMessage locked = await http.PostAsync("/v1/auth/login", new { login = player.Username, password = player.Password });
        Assert.That((int)locked.StatusCode, Is.EqualTo(429));
        Assert.That(await locked.ErrorCodeAsync(), Is.EqualTo("too_many_attempts"));
        Assert.That(int.Parse(locked.Headers.GetValues("Retry-After").Single()), Is.InRange(1, 2));

        await Task.Delay(2200);
        HttpResponseMessage after = await http.PostAsync("/v1/auth/login", new { login = player.Email, password = player.Password });
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the lock lifts by itself");

        // A correct sign-in wipes the slate: it takes five more to lock again, not one.
        Assert.That((await http.PostAsync("/v1/auth/login", new { login = player.Email, password = "wrong again" })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await http.PostAsync("/v1/auth/login", new { login = player.Email, password = player.Password })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GuessingAtANameWithNoAccount_IsLockedOutJustTheSame()
    {
        HttpClient http = _api.CreateClient();
        string ghost = TestHttp.Unique("Ghost");
        for (int i = 0; i < 5; i++)
            Assert.That((await http.PostAsync("/v1/auth/login", new { login = ghost, password = "guess " + i })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((int)(await http.PostAsync("/v1/auth/login", new { login = ghost, password = "guess 6" })).StatusCode, Is.EqualTo(429),
            "so a lock does not reveal that an account exists");
    }

    [Test]
    public async Task EachFurtherFailure_DoublesTheWait_UpToACap()
    {
        TestPlayer player = await _api.RegisterAsync();
        HttpClient http = _api.CreateClient();
        for (int i = 0; i < 5; i++) await http.PostAsync("/v1/auth/login", new { login = player.Email, password = "guess " + i });

        await Task.Delay(2100);
        await http.PostAsync("/v1/auth/login", new { login = player.Email, password = "sixth guess" });
        HttpResponseMessage locked = await http.PostAsync("/v1/auth/login", new { login = player.Email, password = player.Password });
        Assert.That((int)locked.StatusCode, Is.EqualTo(429));
        Assert.That(int.Parse(locked.Headers.GetValues("Retry-After").Single()), Is.InRange(3, 4), "twice the first lock of 2 s");
    }

    // ------------------------------------------------------------------ sessions

    [Test]
    public async Task ARefreshToken_WorksOnce_AndUsingItTwiceEndsTheSession()
    {
        TestPlayer player = await _api.RegisterAsync();
        HttpClient http = _api.CreateClient();
        string first = player.RefreshToken;

        HttpResponseMessage rotated = await http.PostAsync("/v1/auth/refresh", new { refreshToken = first });
        Assert.That(rotated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        JsonElement session = await rotated.JsonAsync();
        string second = session.GetProperty("refreshToken").GetString()!;
        Assert.That(second, Is.Not.EqualTo(first));
        Assert.That(session.GetProperty("accessToken").GetString(), Is.Not.EqualTo(player.AccessToken));
        Assert.That(session.GetProperty("account").GetProperty("id").GetString(), Is.EqualTo(player.Id));

        // The first token turns up again: someone has a copy. Everything in that session dies, the newest token included.
        HttpResponseMessage replay = await http.PostAsync("/v1/auth/refresh", new { refreshToken = first });
        Assert.That(replay.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await replay.ErrorCodeAsync(), Is.EqualTo("session_revoked"));
        HttpResponseMessage afterwards = await http.PostAsync("/v1/auth/refresh", new { refreshToken = second });
        Assert.That(afterwards.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        foreach (object body in new object[] { new { }, new { refreshToken = "" }, new { refreshToken = "made-up" }, new { refreshToken = new string('x', 5000) } })
            Assert.That(await (await http.PostAsync("/v1/auth/refresh", body)).ErrorCodeAsync(), Is.EqualTo("invalid_refresh_token"));
    }

    [Test]
    public async Task TwoRequestsRacingWithOneRefreshToken_CannotBothGetANewOne()
    {
        TestPlayer player = await _api.RegisterAsync();
        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            _api.CreateClient().PostAsync("/v1/auth/refresh", new { refreshToken = player.RefreshToken })));
        Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.OK), Is.LessThanOrEqualTo(1));
    }

    [Test]
    public async Task RefreshTokens_AreNotStoredAnywhereInAUsableForm()
    {
        TestPlayer player = await _api.RegisterAsync();
        var server = _api.Services.GetService(typeof(StackExchange.Redis.IConnectionMultiplexer)) as StackExchange.Redis.IConnectionMultiplexer;
        foreach (StackExchange.Redis.RedisKey key in server!.GetServers()[0].Keys(_api.Redis.Database, "pb:*"))
        {
            Assert.That(key.ToString(), Does.Not.Contain(player.RefreshToken));
            if (_api.Redis.KeyType(key) != StackExchange.Redis.RedisType.Hash) continue;
            foreach (StackExchange.Redis.HashEntry entry in _api.Redis.HashGetAll(key))
                Assert.That(entry.Value.ToString(), Does.Not.Contain(player.RefreshToken));
        }
    }

    [Test]
    public async Task SigningOut_EndsThatDevice_AndSigningOutEverywhereEndsThemAll_AtOnce()
    {
        TestPlayer phone = await _api.RegisterAsync();
        TestPlayer desktop = await _api.LoginElsewhereAsync(phone);
        HttpClient http = _api.CreateClient();

        Assert.That((await http.PostAsync("/v1/auth/logout", new { refreshToken = phone.RefreshToken })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await http.PostAsync("/v1/auth/refresh", new { refreshToken = phone.RefreshToken })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await http.PostAsync("/v1/auth/refresh", new { refreshToken = desktop.RefreshToken })).StatusCode, Is.EqualTo(HttpStatusCode.OK), "the other device is untouched");

        TestPlayer laptop = await _api.LoginElsewhereAsync(phone);
        await Task.Delay(1100);     // tokens are stamped to the second; step past the one they were made in
        Assert.That((await laptop.Http.PostAsync("/v1/auth/logout-all", new { })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await http.PostAsync("/v1/auth/refresh", new { refreshToken = laptop.RefreshToken })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await laptop.Http.GetAsync("/v1/me")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "access tokens stop working straight away, not when they expire");
        Assert.That((await desktop.Http.GetAsync("/v1/me")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task WithoutAValidToken_YouGetNothing()
    {
        TestPlayer player = await _api.RegisterAsync();
        HttpClient http = _api.CreateClient();

        HttpResponseMessage none = await http.GetAsync("/v1/me");
        Assert.That(none.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await none.ErrorCodeAsync(), Is.EqualTo("unauthorized"));

        string[] parts = player.AccessToken.Split('.');
        string tampered = parts[0] + "." + parts[1] + "." + new string(parts[2].Reverse().ToArray());
        string unsigned = "eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0." + parts[1] + ".";
        foreach (string token in new[] { "garbage", tampered, unsigned, player.RefreshToken })
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/v1/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.That((await http.SendAsync(request)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), token);
        }

        foreach (string path in new[] { "/v1/loadouts", "/v1/settings" })
            Assert.That((await http.GetAsync(path)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), path);
        Assert.That((await http.PostAsync("/v1/game/tickets", new { mode = "casual" })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // ------------------------------------------------------------------ email verification

    [Test]
    public async Task TheEmailedCode_VerifiesTheAddress()
    {
        TestPlayer player = await _api.RegisterAsync();
        string code = CapturedMail.VerifyCode(await _api.Mail.WaitForAsync(player.Email));

        HttpResponseMessage wrong = await player.Http.PostAsync("/v1/auth/verify-email", new { code = code == "000000" ? "000001" : "000000" });
        Assert.That(wrong.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await wrong.ErrorCodeAsync(), Is.EqualTo("invalid_code"));

        // Typed with the spaces people add.
        Assert.That((await player.Http.PostAsync("/v1/auth/verify-email", new { code = code[..3] + " " + code[3..] })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await (await player.Http.GetAsync("/v1/me")).JsonAsync()).GetProperty("emailVerified").GetBoolean(), Is.True);

        // Once verified, a fresh token says so too, and asking again is harmless.
        JsonElement refreshed = await (await player.Http.PostAsync("/v1/auth/refresh", new { refreshToken = player.RefreshToken })).JsonAsync();
        Assert.That(refreshed.GetProperty("account").GetProperty("emailVerified").GetBoolean(), Is.True);
        Assert.That((await player.Http.PostAsync("/v1/auth/verify-email", new { code = "whatever" })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task ACode_StopsWorkingAfterAFewWrongGuesses()
    {
        TestPlayer player = await _api.RegisterAsync();
        string code = CapturedMail.VerifyCode(await _api.Mail.WaitForAsync(player.Email));

        for (int i = 0; i < 5; i++)
            Assert.That((await player.Http.PostAsync("/v1/auth/verify-email", new { code = "9" + ((int.Parse(code) + 1 + i) % 100000).ToString("00000") == code ? "111111" : "abcdef" })).StatusCode,
                Is.EqualTo(HttpStatusCode.BadRequest));

        Assert.That((await player.Http.PostAsync("/v1/auth/verify-email", new { code })).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "five wrong guesses and even the right code is dead: all million cannot be tried");

        // A new code is needed, and it works.
        await Task.Delay(1100);
        Assert.That((await player.Http.PostAsync("/v1/auth/resend-verification", new { })).StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        string fresh = CapturedMail.VerifyCode(await _api.Mail.WaitForAsync(player.Email, 2));
        Assert.That((await player.Http.PostAsync("/v1/auth/verify-email", new { code = fresh })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task AskingForAnotherEmailTooSoon_IsRefused_AndANewCodeReplacesTheOld()
    {
        TestPlayer player = await _api.RegisterAsync();
        string first = CapturedMail.VerifyCode(await _api.Mail.WaitForAsync(player.Email));

        HttpResponseMessage tooSoon = await player.Http.PostAsync("/v1/auth/resend-verification", new { });
        Assert.That((int)tooSoon.StatusCode, Is.EqualTo(429));
        Assert.That(await tooSoon.ErrorCodeAsync(), Is.EqualTo("too_soon"));

        await Task.Delay(1100);
        Assert.That((await player.Http.PostAsync("/v1/auth/resend-verification", new { })).StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        string second = CapturedMail.VerifyCode(await _api.Mail.WaitForAsync(player.Email, 2));

        if (first != second)
            Assert.That((await player.Http.PostAsync("/v1/auth/verify-email", new { code = first })).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "only the newest code counts");
        Assert.That((await player.Http.PostAsync("/v1/auth/verify-email", new { code = second })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task TheLinkInTheEmail_VerifiesToo_Once()
    {
        TestPlayer player = await _api.RegisterAsync();
        string link = CapturedMail.VerifyLink(await _api.Mail.WaitForAsync(player.Email));
        HttpClient browser = _api.CreateClient();

        HttpResponseMessage page = await browser.GetAsync(link);
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(page.Content.Headers.ContentType!.MediaType, Is.EqualTo("text/html"));
        Assert.That(await page.Content.ReadAsStringAsync(), Does.Contain("Email verified"));
        Assert.That((await (await player.Http.GetAsync("/v1/me")).JsonAsync()).GetProperty("emailVerified").GetBoolean(), Is.True);

        Assert.That((await browser.GetAsync(link)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "a link works once");
        Assert.That((await browser.GetAsync("/v1/auth/verify-email?token=made-up")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await browser.GetAsync("/v1/auth/verify-email")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // ------------------------------------------------------------------ passwords

    [Test]
    public async Task ForgotPassword_SaysTheSameThing_WhetherOrNotTheAddressHasAnAccount()
    {
        TestPlayer player = await _api.RegisterAsync();
        HttpClient http = _api.CreateClient();
        string nobody = TestHttp.Unique("nobody") + "@example.com";

        HttpResponseMessage known = await http.PostAsync("/v1/auth/forgot-password", new { email = player.Email });
        HttpResponseMessage unknown = await http.PostAsync("/v1/auth/forgot-password", new { email = nobody });
        HttpResponseMessage nonsense = await http.PostAsync("/v1/auth/forgot-password", new { email = "not an address" });
        foreach (HttpResponseMessage response in new[] { known, unknown, nonsense })
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.Empty);
        }

        Assert.That(CapturedMail.ResetCode(await _api.Mail.WaitForAsync(player.Email, 2)), Has.Length.EqualTo(9));
        await Task.Delay(150);
        Assert.That(_api.Mail.To(nobody), Is.Empty);
    }

    [Test]
    public async Task ResettingAPassword_NeedsTheEmailedCode_AndSignsEveryDeviceOut()
    {
        TestPlayer player = await _api.RegisterAsync("the original passphrase");
        TestPlayer otherDevice = await _api.LoginElsewhereAsync(player);
        HttpClient http = _api.CreateClient();

        await http.PostAsync("/v1/auth/forgot-password", new { email = player.Email.ToUpperInvariant() });
        string code = CapturedMail.ResetCode(await _api.Mail.WaitForAsync(player.Email, 2));

        // A wrong code, a code for a different address, and a weak new password all fail; the last without spending the code.
        Assert.That(await (await http.PostAsync("/v1/auth/reset-password", new { email = player.Email, code = "AAAA-AAAA", newPassword = "a brand new passphrase" })).ErrorCodeAsync(), Is.EqualTo("invalid_code"));
        Assert.That(await (await http.PostAsync("/v1/auth/reset-password", new { email = "someone@else.example", code, newPassword = "a brand new passphrase" })).ErrorCodeAsync(), Is.EqualTo("invalid_code"));
        HttpResponseMessage weak = await http.PostAsync("/v1/auth/reset-password", new { email = player.Email, code, newPassword = "short" });
        Assert.That((int)weak.StatusCode, Is.EqualTo(422));
        Assert.That(await weak.ErrorFieldsAsync(), Is.EqualTo(new[] { "newPassword" }));

        await Task.Delay(1100);
        HttpResponseMessage reset = await http.PostAsync("/v1/auth/reset-password", new { email = player.Email, code = code.ToLowerInvariant(), newPassword = "a brand new passphrase" });
        Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        Assert.That((await http.PostAsync("/v1/auth/login", new { login = player.Email, password = "the original passphrase" })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await http.PostAsync("/v1/auth/login", new { login = player.Email, password = "a brand new passphrase" })).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        Assert.That((await http.PostAsync("/v1/auth/refresh", new { refreshToken = player.RefreshToken })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await http.PostAsync("/v1/auth/refresh", new { refreshToken = otherDevice.RefreshToken })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await otherDevice.Http.GetAsync("/v1/me")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        Assert.That(await (await http.PostAsync("/v1/auth/reset-password", new { email = player.Email, code, newPassword = "yet another passphrase" })).ErrorCodeAsync(),
            Is.EqualTo("invalid_code"), "a code works once");
        Assert.That((await _api.Mail.WaitForAsync(player.Email, 3)).Subject, Does.Contain("password was changed"));
    }

    [Test]
    public async Task AResetCode_CannotBeGuessed_ByTryingManyTimes()
    {
        TestPlayer player = await _api.RegisterAsync();
        HttpClient http = _api.CreateClient();
        await http.PostAsync("/v1/auth/forgot-password", new { email = player.Email });
        string code = CapturedMail.ResetCode(await _api.Mail.WaitForAsync(player.Email, 2));

        for (int i = 0; i < 5; i++)
            await http.PostAsync("/v1/auth/reset-password", new { email = player.Email, code = "ZZZZ-ZZZ" + i, newPassword = "a brand new passphrase" });
        Assert.That(await (await http.PostAsync("/v1/auth/reset-password", new { email = player.Email, code, newPassword = "a brand new passphrase" })).ErrorCodeAsync(), Is.EqualTo("invalid_code"));
        Assert.That((await http.PostAsync("/v1/auth/login", new { login = player.Email, password = player.Password })).StatusCode, Is.EqualTo(HttpStatusCode.OK), "the password is unchanged");
    }

    [Test]
    public async Task ChangingAPassword_NeedsTheCurrentOne_AndSignsOtherDevicesOut()
    {
        TestPlayer player = await _api.RegisterAsync("the original passphrase");
        TestPlayer otherDevice = await _api.LoginElsewhereAsync(player);

        HttpResponseMessage wrong = await player.Http.PostAsync("/v1/auth/change-password", new { currentPassword = "not it", newPassword = "a brand new passphrase" });
        Assert.That(await wrong.ErrorFieldsAsync(), Is.EqualTo(new[] { "currentPassword" }));
        HttpResponseMessage weak = await player.Http.PostAsync("/v1/auth/change-password", new { currentPassword = "the original passphrase", newPassword = "tiny" });
        Assert.That(await weak.ErrorFieldsAsync(), Is.EqualTo(new[] { "newPassword" }));

        await Task.Delay(1100);
        HttpResponseMessage changed = await player.Http.PostAsync("/v1/auth/change-password", new { currentPassword = "the original passphrase", newPassword = "a brand new passphrase" });
        Assert.That(changed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        player.Adopt(await changed.JsonAsync());

        Assert.That((await player.Http.GetAsync("/v1/me")).StatusCode, Is.EqualTo(HttpStatusCode.OK), "this device carries on with its new session");
        Assert.That((await otherDevice.Http.GetAsync("/v1/me")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await _api.CreateClient().PostAsync("/v1/auth/refresh", new { refreshToken = otherDevice.RefreshToken })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await _api.CreateClient().PostAsync("/v1/auth/login", new { login = player.Email, password = "a brand new passphrase" })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    // ------------------------------------------------------------------ the edges

    [Test]
    public async Task ABodyThatIsNotJson_GetsAProperError_NotACrash()
    {
        HttpClient http = _api.CreateClient();
        foreach (string body in new[] { "{not json", "[1,2,3]", "\"just a string\"", "{\"email\": 5}" })
        {
            HttpResponseMessage response = await http.PostAsync("/v1/auth/register", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), body);
            Assert.That(await response.ErrorCodeAsync(), Is.EqualTo("bad_request"), body);
        }

        Assert.That((await http.GetAsync("/health")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        JsonElement ready = await (await http.GetAsync("/health/ready")).JsonAsync();
        Assert.That(ready.GetProperty("database").GetBoolean() && ready.GetProperty("redis").GetBoolean());
        Assert.That((await http.GetAsync("/v1/nothing-here")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}

[TestFixture]
public class RateLimitTests
{
    [Test]
    public async Task SignInRequests_FromOneAddress_AreCapped()
    {
        using var api = new ApiFixture(new Dictionary<string, string> { ["RateLimits:AuthPerMinute"] = "6" });
        HttpClient http = api.CreateClient();

        var statuses = new List<int>();
        for (int i = 0; i < 9; i++)
            statuses.Add((int)(await http.PostAsync("/v1/auth/login", new { login = TestHttp.Unique("nobody"), password = "guess" })).StatusCode);

        Assert.That(statuses.Take(6), Is.All.EqualTo(401));
        Assert.That(statuses.Skip(6), Is.All.EqualTo(429));

        HttpResponseMessage limited = await http.PostAsync("/v1/auth/register", new { email = "a@example.com", username = "Someone", password = "a fine passphrase" });
        Assert.That((int)limited.StatusCode, Is.EqualTo(429), "sign-up shares the same allowance");
        Assert.That(await limited.ErrorCodeAsync(), Is.EqualTo("rate_limited"));
        Assert.That((await http.GetAsync("/health")).StatusCode, Is.EqualTo(HttpStatusCode.OK), "other requests are not affected");
    }
}
