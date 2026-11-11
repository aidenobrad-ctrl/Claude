namespace Pulsebreak.Api.Data;

/// <summary>Who someone is and how they prove it. Nothing about the game lives here.</summary>
public sealed class Account
{
    public Guid Id { get; set; }

    /// <summary>As the player typed it, for sending mail and showing back to them.</summary>
    public string Email { get; set; } = "";
    /// <summary>Trimmed and lower-cased. Unique.</summary>
    public string EmailNormalized { get; set; } = "";

    /// <summary>As the player typed it.</summary>
    public string Username { get; set; } = "";
    /// <summary>Lower-cased. Unique, so "Ada" and "ada" cannot both exist.</summary>
    public string UsernameNormalized { get; set; } = "";

    /// <summary>An Argon2id hash with its salt and cost, never the password.</summary>
    public string PasswordHash { get; set; } = "";

    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public bool Disabled { get; set; }

    public Profile Profile { get; set; } = null!;
}

/// <summary>A player's standing in the game. One per account.</summary>
public sealed class Profile
{
    public Guid AccountId { get; set; }
    public int Level { get; set; } = 1;
    public int Xp { get; set; }
    public int Currency { get; set; }
    public int Rating { get; set; } = 1000;
}

/// <summary>One custom loadout, or the record that one was deleted.</summary>
public sealed class LoadoutRow
{
    public Guid AccountId { get; set; }
    /// <summary>The id the game gave it. Unique per account.</summary>
    public string LoadoutId { get; set; } = "";
    /// <summary>The loadout in the game's own stored format, as JSON. Empty once deleted.</summary>
    public string Data { get; set; } = "{}";
    /// <summary>When the player last changed (or deleted) it, by their clock. Seconds since 1970.</summary>
    public long UpdatedAt { get; set; }
    public bool Deleted { get; set; }
    /// <summary>When the server first saw it. Keeps the list in a steady order.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Which loadout a player has equipped.</summary>
public sealed class LoadoutStateRow
{
    public Guid AccountId { get; set; }
    public string EquippedId { get; set; } = "";
    public long EquippedUpdatedAt { get; set; }
}

/// <summary>A player's settings, so they follow the account to another machine.</summary>
public sealed class SettingsRow
{
    public Guid AccountId { get; set; }
    public string Data { get; set; } = "{}";
    public long UpdatedAt { get; set; }
}
