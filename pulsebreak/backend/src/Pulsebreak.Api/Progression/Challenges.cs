using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Pulsebreak.Shared.Progression;

namespace Pulsebreak.Api.Progression;

/// <summary>What a challenge counts.</summary>
public enum ChallengeKind
{
    /// <summary>Kills, optionally only with some classes of weapon.</summary>
    Kills,
    Headshots,
    Wins,
    /// <summary>Matches played to the end.</summary>
    Matches,
    RankedMatches,
    ObjectivePoints,
    RoundsWon,
}

/// <param name="Classes">For kills: the weapon classes that count, as the game names them (such as "Smg"). Empty for any.</param>
public sealed record Challenge(string Id, string Text, ChallengeKind Kind, int Goal, int RewardXp, bool Weekly, params string[] Classes)
{
    /// <summary>How far one match takes this challenge.</summary>
    public int ProgressFrom(PlayerReport player, bool ranked)
    {
        switch (Kind)
        {
            case ChallengeKind.Kills:
                if (Classes.Length == 0) return player.kills;
                int kills = 0;
                foreach (WeaponReport weapon in player.weapons ?? [])
                {
                    if (weapon != null && Classes.Contains(weapon.weaponClass)) kills += weapon.kills;
                }
                return kills;
            case ChallengeKind.Headshots: return player.headshots;
            case ChallengeKind.Wins: return player.completed && player.outcome == MatchOutcome.Won ? 1 : 0;
            case ChallengeKind.Matches: return player.completed ? 1 : 0;
            case ChallengeKind.RankedMatches: return player.completed && ranked ? 1 : 0;
            case ChallengeKind.ObjectivePoints: return Math.Max(0, player.objective);
            case ChallengeKind.RoundsWon: return player.roundsWon;
            default: return 0;
        }
    }
}

/// <summary>
/// The challenges: three a day and three a week for each player, picked from a pool by the
/// player and the date, so they are the same wherever the player looks and different from
/// their friends'. Days start at midnight UTC; weeks on Monday.
/// </summary>
public static class Challenges
{
    public const int PerPeriod = 3;

    public static readonly Challenge[] Daily =
    [
        new("d_kills", "Get 15 kills", ChallengeKind.Kills, 15, 400, false),
        new("d_headshots", "Land 5 headshot kills", ChallengeKind.Headshots, 5, 400, false),
        new("d_wins", "Win 2 matches", ChallengeKind.Wins, 2, 500, false),
        new("d_matches", "Play 3 matches to the end", ChallengeKind.Matches, 3, 300, false),
        new("d_smg", "Get 10 kills with SMGs", ChallengeKind.Kills, 10, 450, false, "Smg"),
        new("d_shotgun", "Get 8 kills with shotguns", ChallengeKind.Kills, 8, 450, false, "Shotgun"),
        new("d_rifle", "Get 12 kills with assault rifles", ChallengeKind.Kills, 12, 450, false, "AssaultRifle"),
        new("d_pistol", "Get 6 kills with pistols", ChallengeKind.Kills, 6, 450, false, "Pistol"),
        new("d_melee", "Get 3 melee kills", ChallengeKind.Kills, 3, 500, false, "Melee"),
        new("d_objective", "Score 400 objective points in Domination or Hardpoint", ChallengeKind.ObjectivePoints, 400, 450, false),
    ];

    public static readonly Challenge[] Weekly =
    [
        new("w_kills", "Get 150 kills", ChallengeKind.Kills, 150, 2000, true),
        new("w_wins", "Win 10 matches", ChallengeKind.Wins, 10, 2500, true),
        new("w_headshots", "Land 40 headshot kills", ChallengeKind.Headshots, 40, 2000, true),
        new("w_rounds", "Win 15 rounds in Duel or Elimination", ChallengeKind.RoundsWon, 15, 2000, true),
        new("w_objective", "Score 3000 objective points", ChallengeKind.ObjectivePoints, 3000, 2200, true),
        new("w_long", "Get 30 kills with sniper and marksman rifles", ChallengeKind.Kills, 30, 2200, true, "Sniper", "Marksman"),
        new("w_heavy", "Get 40 kills with light machine guns", ChallengeKind.Kills, 40, 2200, true, "Lmg"),
        new("w_ranked", "Play 5 ranked matches to the end", ChallengeKind.RankedMatches, 5, 2500, true),
    ];

    public static string DayKey(DateTimeOffset now) => "d" + now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string WeekKey(DateTimeOffset now)
    {
        DateTime day = now.UtcDateTime;
        return "w" + ISOWeek.GetYear(day).ToString(CultureInfo.InvariantCulture) + "-W" + ISOWeek.GetWeekOfYear(day).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>When the current day or week's challenges end.</summary>
    public static DateTimeOffset DayEnds(DateTimeOffset now) => new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);

    public static DateTimeOffset WeekEnds(DateTimeOffset now)
    {
        DateTime day = now.UtcDateTime.Date;
        int sinceMonday = ((int)day.DayOfWeek + 6) % 7;
        return new DateTimeOffset(day.AddDays(7 - sinceMonday), TimeSpan.Zero);
    }

    /// <summary>A player's challenges for a period: the same three every time for the same player and period.</summary>
    public static IReadOnlyList<Challenge> For(Guid accountId, string period, bool weekly)
    {
        Challenge[] pool = weekly ? Weekly : Daily;
        byte[] seed = SHA256.HashData(Encoding.UTF8.GetBytes(accountId.ToString("N") + "|" + period));
        var order = Enumerable.Range(0, pool.Length).OrderBy(i => seed[i % seed.Length] ^ (i * 37 & 0xff)).ThenBy(i => i).ToList();
        return order.Take(PerPeriod).Select(i => pool[i]).ToList();
    }

    public static Challenge? Find(string id) => Daily.Concat(Weekly).FirstOrDefault(c => c.Id == id);
}
