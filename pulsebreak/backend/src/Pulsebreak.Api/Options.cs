namespace Pulsebreak.Api;

/// <summary>Sign-in settings. Bound from the "Auth" section of configuration.</summary>
public sealed class AuthOptions
{
    /// <summary>
    /// The secret that signs access tokens. At least 32 characters. There is no default:
    /// outside development the API refuses to start without one.
    /// </summary>
    public string JwtSigningKey { get; set; } = "";
    public string Issuer { get; set; } = "pulsebreak";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;

    // Argon2id cost. The defaults are OWASP's recommended minimum: 19 MiB, 2 passes, 1 lane.
    public int Argon2MemoryKb { get; set; } = 19456;
    public int Argon2Iterations { get; set; } = 2;
    public int Argon2Parallelism { get; set; } = 1;

    /// <summary>Wrong passwords in a row before an account is locked for a while.</summary>
    public int MaxFailedLogins { get; set; } = 5;
    /// <summary>Length of the first lock. Each further failure doubles it, up to the maximum.</summary>
    public int LockoutSeconds { get; set; } = 60;
    public int MaxLockoutSeconds { get; set; } = 900;

    public int EmailCodeMinutes { get; set; } = 60 * 24;
    public int ResetCodeMinutes { get; set; } = 30;
    /// <summary>Wrong guesses allowed at one emailed code before it stops working.</summary>
    public int MaxCodeAttempts { get; set; } = 5;
    /// <summary>Shortest gap between two emails of the same kind to the same account.</summary>
    public int EmailCooldownSeconds { get; set; } = 60;
}

/// <summary>Settings for handing players over to game servers. Bound from "Game".</summary>
public sealed class GameOptions
{
    /// <summary>
    /// The secret that signs join tickets. Dedicated game servers are started with the same
    /// value so they can check a ticket without calling back here. At least 32 characters.
    /// </summary>
    public string TicketSigningKey { get; set; } = "";
    public int TicketSeconds { get; set; } = 60;
}

/// <summary>How email leaves the building. Bound from "Email".</summary>
public sealed class EmailOptions
{
    /// <summary>"Log" writes emails to the application log, "File" to a folder, "Smtp" sends them.</summary>
    public string Mode { get; set; } = "Log";
    public string From { get; set; } = "Pulsebreak <no-reply@pulsebreak.localhost>";
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = "";
    public string SmtpPassword { get; set; } = "";
    /// <summary>Upgrade the connection to TLS before sending anything. Turn off only for a local test inbox.</summary>
    public bool SmtpStartTls { get; set; } = true;
    public string FileDirectory { get; set; } = "emails";
    /// <summary>Where this API is reachable from a player's browser. Used for the link in the verification email.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5080";
}

/// <summary>Request limits per client address. Bound from "RateLimits".</summary>
public sealed class RateLimitOptions
{
    /// <summary>Sign-in, sign-up and password requests per minute from one address.</summary>
    public int AuthPerMinute { get; set; } = 20;
    /// <summary>All requests per minute from one address.</summary>
    public int GeneralPerMinute { get; set; } = 300;
    /// <summary>
    /// Set when the API sits behind a reverse proxy that sets X-Forwarded-For, so limits
    /// apply to the player's address rather than the proxy's. Leave off otherwise: a client
    /// could set that header itself.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }
}

/// <summary>Bound from "Database".</summary>
public sealed class DatabaseOptions
{
    /// <summary>Bring the schema up to date when the API starts. Convenient for development and single-server setups.</summary>
    public bool MigrateOnStartup { get; set; }
}
