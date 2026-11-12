using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pulsebreak.Api;
using Pulsebreak.Api.Auth;
using Pulsebreak.Api.Data;
using Pulsebreak.Api.Email;
using Pulsebreak.Api.Endpoints;
using Pulsebreak.Api.Loadouts;
using Pulsebreak.Api.Matchmaking;
using Pulsebreak.Api.Progression;
using Pulsebreak.Shared.Net;
using StackExchange.Redis;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
IConfiguration config = builder.Configuration;

// Nothing a game client sends is large. A small cap stops oversized bodies at the door.
const long MaxRequestBytes = 64 * 1024;
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = MaxRequestBytes);

// ---------------------------------------------------------------------- settings

builder.Services.AddOptions<AuthOptions>().Bind(config.GetSection("Auth"))
    .Validate(o => Encoding.UTF8.GetByteCount(o.JwtSigningKey) >= TokenService.MinKeyLength,
        $"Auth:JwtSigningKey must be set to a secret of at least {TokenService.MinKeyLength} characters.")
    .Validate(o => o.AccessTokenMinutes is > 0 and <= 60 && o.RefreshTokenDays is > 0 and <= 365, "Auth token lifetimes are out of range.")
    .Validate(o => o.Argon2MemoryKb >= 8 && o.Argon2Iterations >= 1 && o.Argon2Parallelism >= 1, "Auth Argon2 settings are out of range.")
    .ValidateOnStart();
builder.Services.AddOptions<GameOptions>().Bind(config.GetSection("Game"))
    .Validate(o => o.TicketSigningKey.Length == 0 || Encoding.UTF8.GetByteCount(o.TicketSigningKey) >= GameTicket.MinKeyBytes,
        $"Game:TicketSigningKey must be empty (no tickets) or a secret of at least {GameTicket.MinKeyBytes} characters.")
    .Validate(o => o.TicketSeconds is >= 10 and <= 600, "Game:TicketSeconds must be between 10 and 600.")
    .ValidateOnStart();
builder.Services.AddOptions<EmailOptions>().Bind(config.GetSection("Email"))
    .Validate(o => o.Mode is "Log" or "File" or "Smtp", "Email:Mode must be Log, File or Smtp.")
    .Validate(o => o.Mode != "Smtp" || o.SmtpHost.Length > 0, "Email:SmtpHost is required when Email:Mode is Smtp.")
    .ValidateOnStart();
builder.Services.AddOptions<MatchmakingOptions>().Bind(config.GetSection("Matchmaking"))
    .Validate(o => o.FleetKey.Length == 0 || Encoding.UTF8.GetByteCount(o.FleetKey) >= 32,
        "Matchmaking:FleetKey must be empty (no game servers) or a secret of at least 32 characters.")
    .Validate(o => o.FillAfterSeconds is >= 0 and <= 600 && o.TicketIdleSeconds is >= 5 and <= 600 &&
                   o.HeartbeatSeconds is >= 1 and <= 60 && o.ServerTimeoutSeconds >= o.HeartbeatSeconds && o.GuestPassHours is >= 1 and <= 720,
        "Matchmaking timings are out of range.")
    .ValidateOnStart();
builder.Services.AddOptions<RateLimitOptions>().Bind(config.GetSection("RateLimits"));
builder.Services.AddOptions<DatabaseOptions>().Bind(config.GetSection("Database"));

// ---------------------------------------------------------------------- storage

builder.Services.AddDbContext<AppDbContext>(db => db.UseNpgsql(config.GetConnectionString("Postgres")));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var redis = ConfigurationOptions.Parse(config.GetConnectionString("Redis") ?? "localhost:6379");
    redis.AbortOnConnectFail = false;       // start even if Redis is a moment behind, and reconnect by itself
    return ConnectionMultiplexer.Connect(redis);
});

// ---------------------------------------------------------------------- services

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<TokenRevocation>();
builder.Services.AddSingleton<LoginGuard>();
builder.Services.AddSingleton<EmailCodes>();
builder.Services.AddSingleton<EmailQueue>();
builder.Services.AddSingleton<SessionIssuer>();
builder.Services.AddSingleton<AccountMailer>();
builder.Services.AddScoped<LoadoutService>();
builder.Services.AddScoped<ProgressionService>();
builder.Services.AddHostedService<EmailWorker>();
builder.Services.AddSingleton<GuestPasses>();
builder.Services.AddSingleton<MatchmakingService>();
builder.Services.AddHostedService<MatchmakerWorker>();
builder.Services.AddSingleton<IEmailSender>(services => services.GetRequiredService<IOptions<EmailOptions>>().Value.Mode switch
{
    "Smtp" => ActivatorUtilities.CreateInstance<SmtpEmailSender>(services),
    "File" => ActivatorUtilities.CreateInstance<FileEmailSender>(services),
    _ => ActivatorUtilities.CreateInstance<LogEmailSender>(services),
});

// The game's stored formats use public fields, and JSON for them must include those.
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.IncludeFields = true);
// A body that cannot be read becomes an exception, which is turned into a proper error reply below.
builder.Services.Configure<RouteHandlerOptions>(routes => routes.ThrowOnBadRequest = true);

// ---------------------------------------------------------------------- who is calling

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<IOptions<AuthOptions>>((jwt, auth) =>
{
    jwt.MapInboundClaims = false;
    jwt.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = auth.Value.Issuer,
        ValidAudience = TokenService.AccessAudience,
        IssuerSigningKey = TokenService.SigningKey(auth.Value),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "name",
    };
    jwt.Events = new JwtBearerEvents
    {
        // A valid signature is not the last word: tokens issued before a password reset or
        // "sign out everywhere" are refused even though they have not expired.
        OnTokenValidated = async context =>
        {
            var revocation = context.HttpContext.RequestServices.GetRequiredService<TokenRevocation>();
            if (context.Principal == null || !context.Principal.TryGetAccountId(out Guid accountId) ||
                await revocation.IsRevokedAsync(accountId, context.SecurityToken.ValidFrom))
                context.Fail("This token is no longer valid.");
        },
        OnChallenge = async context =>
        {
            context.HandleResponse();
            await Api.Unauthorized().ExecuteAsync(context.HttpContext);
        },
        OnForbidden = context => Api.Error(StatusCodes.Status403Forbidden, "forbidden", "You are not allowed to do that.").ExecuteAsync(context.HttpContext),
    };
});
builder.Services.AddAuthorization();

// ---------------------------------------------------------------------- limits

var limits = config.GetSection("RateLimits").Get<RateLimitOptions>() ?? new RateLimitOptions();
builder.Services.AddRateLimiter(limiter =>
{
    static string Address(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // Everything, per address.
    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http => RateLimitPartition.GetFixedWindowLimiter(
        Address(http), _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.GeneralPerMinute, Window = TimeSpan.FromMinutes(1) }));

    // Signing in and everything near it, per address, much tighter.
    limiter.AddPolicy(AuthEndpoints.RateLimitPolicy, http => RateLimitPartition.GetSlidingWindowLimiter(
        Address(http), _ => new SlidingWindowRateLimiterOptions { PermitLimit = limits.AuthPerMinute, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6 }));

    limiter.OnRejected = async (context, cancel) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        await Api.Error(StatusCodes.Status429TooManyRequests, "rate_limited", "Too many requests. Wait a moment and try again.")
            .ExecuteAsync(context.HttpContext);
    };
});

WebApplication app = builder.Build();

// ---------------------------------------------------------------------- pipeline

if (limits.TrustForwardedHeaders)
{
    // Only safe when the API can be reached through the proxy and no other way.
    var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
    app.UseForwardedHeaders(forwarded);
}

// Any failure nobody handled: say so in the same shape as every other error, and say no more.
app.UseExceptionHandler(errors => errors.Run(async http =>
{
    Exception? error = http.Features.Get<IExceptionHandlerFeature>()?.Error;
    IResult reply = error is BadHttpRequestException bad
        ? Api.Error(bad.StatusCode, "bad_request", "That request could not be read. Check that the body is valid JSON.")
        : Api.Error(StatusCodes.Status500InternalServerError, "server_error", "Something went wrong on our side.");
    await reply.ExecuteAsync(http);
}));

// A request that announces a body over the cap is refused before any of it is read. (The
// web server enforces the same cap on bodies that do not announce their size.)
app.Use(async (http, next) =>
{
    if (http.Request.ContentLength > MaxRequestBytes)
    {
        await Api.Error(StatusCodes.Status413PayloadTooLarge, "too_large", "That request is too large.").ExecuteAsync(http);
        return;
    }
    await next();
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Text("ok"));
app.MapGet("/health/ready", async (AppDbContext db, IConnectionMultiplexer redis) =>
{
    bool database = await db.Database.CanConnectAsync();
    bool cache = redis.IsConnected;
    return Results.Json(new { database, redis = cache }, statusCode: database && cache ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});
AuthEndpoints.Map(app);
PlayerEndpoints.Map(app);
MatchmakingEndpoints.Map(app);

if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStartup)
{
    using IServiceScope scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.Run();

/// <summary>Named so tests can start the whole application in memory.</summary>
public partial class Program;
