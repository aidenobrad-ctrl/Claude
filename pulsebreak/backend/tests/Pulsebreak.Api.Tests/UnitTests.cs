using System.Text;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Pulsebreak.Api.Auth;
using Pulsebreak.Shared.Net;

namespace Pulsebreak.Api.Tests;

[TestFixture]
public class PasswordHasherTests
{
    static PasswordHasher Hasher(int memoryKb = 256, int iterations = 1) =>
        new(Options.Create(new AuthOptions { Argon2MemoryKb = memoryKb, Argon2Iterations = iterations, Argon2Parallelism = 1 }));

    [Test]
    public async Task AHash_ChecksOut_OnlyForThePasswordItWasMadeFrom()
    {
        PasswordHasher hasher = Hasher();
        string stored = await hasher.HashAsync("correct horse battery staple");

        Assert.That(stored, Does.StartWith("$argon2id$v=19$m=256,t=1,p=1$"));
        Assert.That(await hasher.VerifyAsync("correct horse battery staple", stored), Is.EqualTo(PasswordCheck.Correct));
        Assert.That(await hasher.VerifyAsync("correct horse battery stapl", stored), Is.EqualTo(PasswordCheck.Wrong));
        Assert.That(await hasher.VerifyAsync("", stored), Is.EqualTo(PasswordCheck.Wrong));
        Assert.That(await hasher.HashAsync("correct horse battery staple"), Is.Not.EqualTo(stored), "a fresh salt every time");

        string unicode = await hasher.HashAsync("pässwörd-密码-🔑");
        Assert.That(await hasher.VerifyAsync("pässwörd-密码-🔑", unicode), Is.EqualTo(PasswordCheck.Correct));
    }

    [Test]
    public async Task RaisingTheCost_KeepsOldHashesWorking_AndFlagsThemForUpgrade()
    {
        string old = await Hasher(memoryKb: 128).HashAsync("a password from last year");
        PasswordHasher today = Hasher(memoryKb: 512, iterations: 2);

        Assert.That(await today.VerifyAsync("a password from last year", old), Is.EqualTo(PasswordCheck.CorrectNeedsRehash));
        Assert.That(await today.VerifyAsync("not it", old), Is.EqualTo(PasswordCheck.Wrong));
        string upgraded = await today.HashAsync("a password from last year");
        Assert.That(upgraded, Does.Contain("m=512,t=2"));
        Assert.That(await today.VerifyAsync("a password from last year", upgraded), Is.EqualTo(PasswordCheck.Correct));
    }

    [Test]
    public async Task AStoredValueThatIsNotOneOfOurs_NeverMatches_AndNeverThrows()
    {
        PasswordHasher hasher = Hasher();
        string good = await hasher.HashAsync("x");
        string[] parts = good.Split('$');
        foreach (string stored in new[]
                 {
                     "", "plaintext", "$2a$10$abcdefghijklmnopqrstuv", "$argon2id$v=19$m=256,t=1,p=1$$",
                     "$argon2i$v=19$m=256,t=1,p=1$" + parts[4] + "$" + parts[5],
                     "$argon2id$v=19$m=999999999,t=1,p=1$" + parts[4] + "$" + parts[5],     // would take gigabytes
                     "$argon2id$v=19$m=256,t=9999,p=1$" + parts[4] + "$" + parts[5],        // would take minutes
                     "$argon2id$v=19$m=256,t=1,p=1$!!!$" + parts[5],
                     "$argon2id$v=19$m=256,t=1$" + parts[4] + "$" + parts[5],
                 })
            Assert.That(await hasher.VerifyAsync("x", stored), Is.EqualTo(PasswordCheck.Wrong), stored);
    }
}

[TestFixture]
public class RulesTests
{
    [TestCase("ada@example.com")]
    [TestCase("first.last+tag@sub.example.co.uk")]
    [TestCase("  padded@example.com  ")]
    public void RealAddresses_Pass(string email) => Assert.That(Rules.CheckEmail(email), Is.Null);

    [TestCase(null)]
    [TestCase("")]
    [TestCase("no-at-sign")]
    [TestCase("@example.com")]
    [TestCase("ada@")]
    [TestCase("ada@localhost")]
    [TestCase("ada@example.")]
    [TestCase("ada lovelace@example.com")]
    [TestCase("ada@exa mple.com")]
    [TestCase("a@b@example.com")]
    [TestCase("Ada <ada@example.com>")]
    public void ThingsThatAreNotAddresses_Fail(string? email) => Assert.That(Rules.CheckEmail(email), Is.Not.Null);

    [Test]
    public void Usernames_AreShortPlainAndNotReserved()
    {
        foreach (string good in new[] { "Ada", "ada_lovelace", "Player_1", "x_X_x", new string('a', 20) })
            Assert.That(Rules.CheckUsername(good), Is.Null, good);
        foreach (string? bad in new[] { null, "", "ab", new string('a', 21), "has space", "emoji🙂", "dash-ed", "dot.ted", "Admin", "MODERATOR", "pulsebreak", "ada@home" })
            Assert.That(Rules.CheckUsername(bad), Is.Not.Null, bad);
    }

    [Test]
    public void Passwords_NeedLength_NotSymbols()
    {
        Assert.That(Rules.CheckPassword("correct horse battery"), Is.Null, "a plain passphrase is fine");
        Assert.That(Rules.CheckPassword("tr0ub4dor&3x"), Is.Null);
        Assert.That(Rules.CheckPassword(new string('é', 10) + "abc"), Is.Null);

        Assert.That(Rules.CheckPassword(null), Is.Not.Null);
        Assert.That(Rules.CheckPassword("nine char"), Is.Not.Null);
        Assert.That(Rules.CheckPassword(new string('a', 129)), Is.Not.Null);
        Assert.That(Rules.CheckPassword("password123"), Is.Not.Null);
        Assert.That(Rules.CheckPassword("PASSWORD123"), Is.Not.Null);
        Assert.That(Rules.CheckPassword("abababababab"), Is.Not.Null, "two characters repeated");
        Assert.That(Rules.CheckPassword("my name is Ada99", username: "ada99"), Is.Not.Null);
        Assert.That(Rules.CheckPassword("lovelace-is-my-email", email: "Lovelace@example.com"), Is.Not.Null);
        Assert.That(Rules.CheckPassword("a fine passphrase", username: "Ada", email: "al@example.com"), Is.Null, "a three letter email name is too short to count");
    }
}

[TestFixture]
public class GameTicketTests
{
    static readonly byte[] Key = Encoding.UTF8.GetBytes("unit-test-ticket-key-0123456789-abcdef");
    static TicketClaims Claims(long expires = 2000) => new() { AccountId = "0f8fad5b-d9cb-469f-a165-70867728950e", Name = "Ada_99", EmailVerified = true, Mode = GameTicket.Ranked, ExpiresAt = expires };

    [Test]
    public void ATicket_RoundTrips()
    {
        string ticket = GameTicket.Create(Claims(), Key, issuedAt: 1940);
        Assert.That(ticket.Split('.'), Has.Length.EqualTo(3));
        Assert.That(ticket, Has.Length.LessThan(GameTicket.MaxLength));

        Assert.That(GameTicket.TryVerify(ticket, Key, 1999, out TicketClaims claims, out string problem), Is.True, problem);
        Assert.That(claims.AccountId, Is.EqualTo("0f8fad5b-d9cb-469f-a165-70867728950e"));
        Assert.That(claims.Name, Is.EqualTo("Ada_99"));
        Assert.That(claims.EmailVerified, Is.True);
        Assert.That(claims.Mode, Is.EqualTo("ranked"));
        Assert.That(claims.ExpiresAt, Is.EqualTo(2000));

        Assert.That(GameTicket.TryVerify(ticket, Key, 2000, out _, out problem), Is.False);
        Assert.That(problem, Is.EqualTo("the ticket has expired"));
    }

    [Test]
    public void ItIsAStandardSignedToken_ThatOtherToolsCanRead()
    {
        string ticket = GameTicket.Create(Claims(), Key, issuedAt: 1940);
        var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();
        Microsoft.IdentityModel.Tokens.TokenValidationResult result = handler.ValidateTokenAsync(ticket, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Key),
            ValidAudience = GameTicket.Audience,
            ValidateIssuer = false,
            ValidateLifetime = false,
        }).Result;
        Assert.That(result.IsValid, Is.True, result.Exception?.Message);
        Assert.That(result.Claims["name"], Is.EqualTo("Ada_99"));
        Assert.That(result.Claims["sub"], Is.EqualTo("0f8fad5b-d9cb-469f-a165-70867728950e"));
    }

    [Test]
    public void AnythingTamperedWith_IsRefused()
    {
        string ticket = GameTicket.Create(Claims(), Key, 1940);
        string[] parts = ticket.Split('.');
        string Payload(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var forgeries = new Dictionary<string, string>
        {
            ["a changed name, old signature"] = parts[0] + "." + Payload("{\"aud\":\"pulsebreak-game\",\"sub\":\"x\",\"name\":\"Admin\",\"ev\":true,\"mode\":\"ranked\",\"iat\":1,\"exp\":9999999999}") + "." + parts[2],
            ["no signature"] = parts[0] + "." + parts[1] + ".",
            ["an empty signature part"] = parts[0] + "." + parts[1],
            ["the 'none' algorithm"] = "eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0." + parts[1] + ".",
            ["a header saying a different algorithm"] = "eyJhbGciOiJIUzUxMiIsInR5cCI6IkpXVCJ9." + parts[1] + "." + parts[2],
            ["one flipped character"] = ticket[..^1] + (ticket[^1] == 'A' ? 'B' : 'A'),
            ["characters that are not base64"] = parts[0] + "." + parts[1] + ".***",
            ["nothing"] = "",
            ["dots"] = "..",
            ["far too long"] = ticket + new string('A', 1000),
        };
        foreach (KeyValuePair<string, string> forgery in forgeries)
        {
            Assert.That(GameTicket.TryVerify(forgery.Value, Key, 1950, out TicketClaims claims, out string problem), Is.False, forgery.Key);
            Assert.That(claims, Is.Null, forgery.Key);
            Assert.That(problem, Is.Not.Empty, forgery.Key);
        }

        Assert.That(GameTicket.TryVerify(ticket, Encoding.UTF8.GetBytes("another-key-0123456789-abcdefghijklmnop"), 1950, out _, out _), Is.False);
        Assert.That(GameTicket.TryVerify(ticket, new byte[8], 1950, out _, out string shortKey), Is.False);
        Assert.That(shortKey, Does.Contain("no ticket key"));
        Assert.Throws<ArgumentException>(() => GameTicket.Create(Claims(), new byte[8], 1));
    }

    [Test]
    public void ATicketForSomethingElse_OrWithNobodyInIt_IsRefused()
    {
        // Properly signed with the right key, but not a join ticket.
        string Sign(string json)
        {
            string body = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            using var hmac = new System.Security.Cryptography.HMACSHA256(Key);
            return body + "." + Convert.ToBase64String(hmac.ComputeHash(Encoding.ASCII.GetBytes(body))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        Assert.That(GameTicket.TryVerify(Sign("{\"aud\":\"pulsebreak-game\",\"sub\":\"a\",\"name\":\"Ada\",\"ev\":false,\"mode\":\"casual\",\"exp\":2000}"), Key, 1, out TicketClaims ok, out _), Is.True);
        Assert.That(ok.EmailVerified, Is.False);

        foreach (string json in new[]
                 {
                     "{\"aud\":\"pulsebreak-api\",\"sub\":\"a\",\"name\":\"Ada\",\"exp\":2000}",                    // another audience
                     "{\"aud\":\"pulsebreak-game\",\"sub\":\"a\",\"name\":\"Ada\"}",                                // never expires
                     "{\"aud\":\"pulsebreak-game\",\"sub\":\"a\",\"name\":\"Ada\",\"exp\":\"2000\"}",               // expiry as text
                     "{\"aud\":\"pulsebreak-game\",\"sub\":\"\",\"name\":\"Ada\",\"exp\":2000}",                    // nobody
                     "{\"aud\":\"pulsebreak-game\",\"sub\":\"a\",\"name\":\"Ada\",\"name\":\"Admin\",\"exp\":2000}", // a field given twice
                     "{\"aud\":\"pulsebreak-game\",\"sub\":\"a\",\"name\":{\"x\":1},\"exp\":2000}",                 // nested
                     "[1,2,3]", "not json", "{\"aud\":\"pulsebreak-game\",\"sub\":\"a\",\"name\":\"Ada\",\"exp\":2000} trailing",
                 })
            Assert.That(GameTicket.TryVerify(Sign(json), Key, 1, out _, out _), Is.False, json);
    }
}
