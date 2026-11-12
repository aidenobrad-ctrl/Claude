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

/// <summary>How far a player has come with one weapon.</summary>
public sealed class WeaponProgressRow
{
    public Guid AccountId { get; set; }
    public string WeaponId { get; set; } = "";
    public int Xp { get; set; }
    public int Kills { get; set; }
}

/// <summary>A player's rating in one ranked playlist.</summary>
public sealed class RatingRow
{
    public Guid AccountId { get; set; }
    public string Playlist { get; set; } = "";
    public int Rating { get; set; } = 1000;
    public int Matches { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>The ranked season these numbers belong to. A row from an earlier season is soft-reset when next used.</summary>
    public int Season { get; set; } = 1;
    /// <summary>The highest rating reached this season.</summary>
    public int BestRating { get; set; } = 1000;
}

/// <summary>Where a player finished a ranked season in one playlist. Written when their rating rolls over to the next season.</summary>
public sealed class SeasonResultRow
{
    public Guid AccountId { get; set; }
    public string Playlist { get; set; } = "";
    public int Season { get; set; }
    public int FinalRating { get; set; }
    public int BestRating { get; set; }
    public int Matches { get; set; }
    public int Wins { get; set; }
}

/// <summary>
/// A friendship, or a request for one. One row per pair: the requester asked, the addressee
/// has not yet answered unless <see cref="Accepted"/>.
/// </summary>
public sealed class FriendshipRow
{
    public Guid RequesterId { get; set; }
    public Guid AddresseeId { get; set; }
    public bool Accepted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}

/// <summary>Someone a player never wants to hear from: no friend requests, no invites, and their chat hidden.</summary>
public sealed class BlockRow
{
    public Guid OwnerId { get; set; }
    public Guid BlockedId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A report of a player, for a person to review.</summary>
public sealed class ReportRow
{
    public Guid Id { get; set; }
    public Guid ReporterId { get; set; }
    public Guid ReportedId { get; set; }
    /// <summary>One of <see cref="Social.ReportReasons.All"/>.</summary>
    public string Reason { get; set; } = "";
    public string Details { get; set; } = "";
    /// <summary>The match it happened in, if it was one.</summary>
    public string MatchId { get; set; } = "";
    /// <summary>What the reported player said that the reporter saw, one line each.</summary>
    public string Chat { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>"open", then whatever a reviewer decides.</summary>
    public string Status { get; set; } = "open";
}

/// <summary>Progress on one challenge in one day or week.</summary>
public sealed class ChallengeRow
{
    public Guid AccountId { get; set; }
    /// <summary>"d2026-11-12" for a day, "w2026-W46" for a week.</summary>
    public string Period { get; set; } = "";
    public string ChallengeId { get; set; } = "";
    public int Progress { get; set; }
    public bool Completed { get; set; }
}

/// <summary>A finished matchmade match. Recorded once: a second report of the same match changes nothing.</summary>
public sealed class MatchRow
{
    public string MatchId { get; set; } = "";
    public string Playlist { get; set; } = "";
    public bool Ranked { get; set; }
    public string ServerId { get; set; } = "";
    public DateTimeOffset EndedAt { get; set; }
    public int DurationSeconds { get; set; }
}

/// <summary>One signed-in player's part in a match, and what it earned them.</summary>
public sealed class MatchPlayerRow
{
    public string MatchId { get; set; } = "";
    public Guid AccountId { get; set; }
    public int Team { get; set; }
    /// <summary>0 lost, 1 draw, 2 won.</summary>
    public int Outcome { get; set; }
    public bool Completed { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Headshots { get; set; }
    public int Objective { get; set; }
    public int RoundsWon { get; set; }
    public int Score { get; set; }
    public int XpGained { get; set; }
    public int LevelBefore { get; set; }
    public int LevelAfter { get; set; }
    public int? RatingBefore { get; set; }
    public int? RatingAfter { get; set; }
    /// <summary>The challenges this match finished, by id, comma separated.</summary>
    public string ChallengesCompleted { get; set; } = "";
    public DateTimeOffset EndedAt { get; set; }
}
