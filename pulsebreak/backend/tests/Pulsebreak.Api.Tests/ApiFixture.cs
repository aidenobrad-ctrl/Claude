using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pulsebreak.Api.Data;
using Pulsebreak.Api.Email;
using StackExchange.Redis;

namespace Pulsebreak.Api.Tests;

/// <summary>Catches every email the API would have sent, so a test can read the code out of it.</summary>
public sealed class CapturedMail : IEmailSender
{
    readonly ConcurrentQueue<EmailMessage> _sent = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancel = default)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> To(string address) => _sent.Where(m => m.To.Equals(address, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Emails are sent in the background; this waits for the nth one to an address.</summary>
    public async Task<EmailMessage> WaitForAsync(string address, int count = 1)
    {
        for (int i = 0; i < 200; i++)
        {
            IReadOnlyList<EmailMessage> mail = To(address);
            if (mail.Count >= count) return mail[count - 1];
            await Task.Delay(10);
        }
        throw new TimeoutException($"Email number {count} to {address} never arrived.");
    }

    public static string VerifyCode(EmailMessage mail) => Regex.Match(mail.Body, @"^\s+(\d{6})\s*$", RegexOptions.Multiline).Groups[1].Value;
    public static string VerifyLink(EmailMessage mail) => Regex.Match(mail.Body, @"(/v1/auth/verify-email\?token=\S+)").Groups[1].Value;
    public static string ResetCode(EmailMessage mail) => Regex.Match(mail.Body, @"^\s+([A-Z0-9]{4}-[A-Z0-9]{4})\s*$", RegexOptions.Multiline).Groups[1].Value;
}

/// <summary>
/// The whole API, running in memory against a real PostgreSQL and a real Redis. Each fixture
/// starts from an empty database built by the migrations, so those are tested too.
///
/// Where the services are comes from PULSEBREAK_TEST_POSTGRES and PULSEBREAK_TEST_REDIS,
/// defaulting to a local pair.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>
{
    public const string JwtKey = "test-signing-key-0123456789-abcdefghijklmnop";
    public const string TicketKey = "test-ticket-key-0123456789-abcdefghijklmnopq";

    static int _redisDatabase = 1;

    public CapturedMail Mail { get; } = new();

    /// <param name="settings">Configuration to change for this fixture, as "Section:Key" to value.</param>
    public ApiFixture(Dictionary<string, string>? settings = null)
    {
        string database = "pulsebreak_test_" + Guid.NewGuid().ToString("N")[..8];
        string postgres = Environment.GetEnvironmentVariable("PULSEBREAK_TEST_POSTGRES")
                          ?? "Host=127.0.0.1;Port=5432;Username=pulsebreak;Password=pulsebreak_dev";
        string redis = Environment.GetEnvironmentVariable("PULSEBREAK_TEST_REDIS") ?? "127.0.0.1:6390";

        var all = new Dictionary<string, string>
        {
            ["ConnectionStrings:Postgres"] = postgres + ";Database=" + database,
            ["ConnectionStrings:Redis"] = redis + ",defaultDatabase=" + Interlocked.Increment(ref _redisDatabase) % 15 + ",allowAdmin=true",
            ["Auth:JwtSigningKey"] = JwtKey,
            ["Game:TicketSigningKey"] = TicketKey,
            // Real Argon2id, turned right down so hundreds of sign-ins take seconds, not minutes.
            ["Auth:Argon2MemoryKb"] = "256",
            ["Auth:Argon2Iterations"] = "1",
            ["Auth:MaxFailedLogins"] = "5",
            ["Auth:LockoutSeconds"] = "2",
            ["Auth:MaxLockoutSeconds"] = "4",
            ["Auth:MaxCodeAttempts"] = "5",
            ["Auth:EmailCooldownSeconds"] = "1",
            ["RateLimits:AuthPerMinute"] = "100000",
            ["RateLimits:GeneralPerMinute"] = "100000",
            ["Database:MigrateOnStartup"] = "true",
            ["Email:Mode"] = "Log",
            ["Logging:LogLevel:Default"] = "Warning",
        };
        foreach (KeyValuePair<string, string> setting in settings ?? []) all[setting.Key] = setting.Value;

        // The application reads some settings while it is still being assembled, before a
        // test host gets a say. Environment variables are the one source it sees that early.
        foreach (KeyValuePair<string, string> setting in all)
            Environment.SetEnvironmentVariable(setting.Key.Replace(":", "__"), setting.Value);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");

        // Building the host happens on first use; do it now, while the variables above are ours.
        using HttpClient warmup = CreateClient();
        using (IServiceScope scope = Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>().GetServers()[0]
                .FlushDatabase(scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>().GetDatabase().Database);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Mail);
        });
    }

    bool _dropped;

    protected override void Dispose(bool disposing)
    {
        // The base class comes through here more than once; the database is dropped the first time only.
        if (disposing && !_dropped)
        {
            _dropped = true;
            using IServiceScope scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeleted();
        }
        base.Dispose(disposing);
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> work)
    {
        using IServiceScope scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public IDatabase Redis => Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
}

/// <summary>A player as a test sees one: who they are, their tokens, and a client that sends them.</summary>
public sealed class TestPlayer(HttpClient http, string email, string username, string password)
{
    public HttpClient Http { get; } = http;
    public string Email { get; } = email;
    public string Username { get; } = username;
    public string Password { get; set; } = password;
    public string AccessToken { get; private set; } = "";
    public string RefreshToken { get; private set; } = "";
    public string Id { get; private set; } = "";

    public void Adopt(JsonElement session)
    {
        AccessToken = session.GetProperty("accessToken").GetString()!;
        RefreshToken = session.GetProperty("refreshToken").GetString()!;
        Id = session.GetProperty("account").GetProperty("id").GetString()!;
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
    }
}

public static class TestHttp
{
    static int _counter;

    public static string Unique(string stem) => stem + Interlocked.Increment(ref _counter) + Guid.NewGuid().ToString("N")[..5];

    public static Task<HttpResponseMessage> PostAsync(this HttpClient http, string path, object body) => http.PostAsJsonAsync(path, body);

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync();
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    public static async Task<string> ErrorCodeAsync(this HttpResponseMessage response) =>
        (await response.JsonAsync()).GetProperty("error").GetProperty("code").GetString()!;

    public static async Task<string[]> ErrorFieldsAsync(this HttpResponseMessage response) =>
        (await response.JsonAsync()).GetProperty("error").GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("field").GetString()!).ToArray();

    /// <summary>Signs up a new player with a name nobody else has, and signs them in.</summary>
    public static async Task<TestPlayer> RegisterAsync(this ApiFixture api, string password = "correct horse battery")
    {
        string username = Unique("Player");
        var player = new TestPlayer(api.CreateClient(), username.ToLowerInvariant() + "@example.com", username, password);
        HttpResponseMessage response = await player.Http.PostAsync("/v1/auth/register", new { email = player.Email, username = player.Username, password });
        if ((int)response.StatusCode != 201) throw new InvalidOperationException("Sign-up failed: " + await response.Content.ReadAsStringAsync());
        player.Adopt(await response.JsonAsync());
        return player;
    }

    /// <summary>The same account signing in from another device.</summary>
    public static async Task<TestPlayer> LoginElsewhereAsync(this ApiFixture api, TestPlayer player)
    {
        var device = new TestPlayer(api.CreateClient(), player.Email, player.Username, player.Password);
        HttpResponseMessage response = await device.Http.PostAsync("/v1/auth/login", new { login = player.Email, password = player.Password });
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Sign-in failed: " + await response.Content.ReadAsStringAsync());
        device.Adopt(await response.JsonAsync());
        return device;
    }

    public static async Task VerifyEmailAsync(this ApiFixture api, TestPlayer player)
    {
        string code = CapturedMail.VerifyCode(await api.Mail.WaitForAsync(player.Email));
        HttpResponseMessage response = await player.Http.PostAsync("/v1/auth/verify-email", new { code });
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Verifying failed: " + await response.Content.ReadAsStringAsync());
    }
}
