using System;

// This file is also compiled into the backend (backend/src/Pulsebreak.Api): the backend
// awards XP and rating with it, and the game shows levels and ranks with it, so the two
// always agree. Keep it free of UnityEngine.
#nullable disable

namespace Pulsebreak.Shared.Progression
{
    /// <summary>How a match went for one player.</summary>
    public enum MatchOutcome : byte
    {
        Lost,
        Draw,
        Won,
    }

    /// <summary>Player levels and weapon levels, and how much XP each takes.</summary>
    public static class Levels
    {
        public const int MaxLevel = 50;
        public const int MaxWeaponLevel = 20;

        /// <summary>XP to go from <paramref name="level"/> to the next. Grows by 50 a level from 200.</summary>
        public static int XpToNext(int level) => level >= MaxLevel ? 0 : 200 + 50 * (Math.Max(1, level) - 1);

        /// <summary>Total XP at which a level is reached. Level 1 is 0.</summary>
        public static long XpAtLevel(int level)
        {
            level = Math.Clamp(level, 1, MaxLevel);
            long total = 0;
            for (int l = 1; l < level; l++) total += XpToNext(l);
            return total;
        }

        /// <summary>The level for a total, and how far into it.</summary>
        public static int LevelFor(long xp, out long into, out long needed)
        {
            int level = 1;
            into = Math.Max(0, xp);
            while (level < MaxLevel && into >= XpToNext(level))
            {
                into -= XpToNext(level);
                level++;
            }
            needed = XpToNext(level);
            return level;
        }

        public static int LevelFor(long xp) => LevelFor(xp, out _, out _);

        /// <summary>Weapon XP to go from one weapon level to the next. Grows by 50 a level from 200.</summary>
        public static int WeaponXpToNext(int level) => level >= MaxWeaponLevel ? 0 : 200 + 50 * (Math.Max(1, level) - 1);

        public static int WeaponLevelFor(long xp, out long into, out long needed)
        {
            int level = 1;
            into = Math.Max(0, xp);
            while (level < MaxWeaponLevel && into >= WeaponXpToNext(level))
            {
                into -= WeaponXpToNext(level);
                level++;
            }
            needed = WeaponXpToNext(level);
            return level;
        }

        public static int WeaponLevelFor(long xp) => WeaponLevelFor(xp, out _, out _);
    }

    /// <summary>XP for a match. The backend works it out from what the game server reported.</summary>
    public static class MatchXp
    {
        public const int Finish = 100, PerKill = 25, PerHeadshot = 10, Win = 150, Draw = 75, PerRoundWon = 30;
        /// <summary>Objective points (zone captures and holds) are worth this fraction in XP.</summary>
        public const int ObjectiveDivisor = 4;
        public const int Cap = 2500;
        public const int WeaponPerKill = 100, WeaponPerHeadshot = 25;

        /// <summary>
        /// XP for one player's match. Staying to the end counts for a lot, and leaving early
        /// forfeits the finishing and winning parts. Ranked pays a quarter more.
        /// </summary>
        public static int For(PlayerReport player, bool ranked)
        {
            long xp = (long)player.kills * PerKill + (long)player.headshots * PerHeadshot +
                      Math.Max(0, player.objective) / ObjectiveDivisor + (long)player.roundsWon * PerRoundWon;
            if (player.completed)
            {
                xp += Finish;
                if (player.outcome == MatchOutcome.Won) xp += Win;
                else if (player.outcome == MatchOutcome.Draw) xp += Draw;
            }
            if (ranked) xp = xp * 5 / 4;
            return (int)Math.Clamp(xp, 0, Cap);
        }

        public static int ForWeapon(WeaponReport weapon) =>
            (int)Math.Clamp((long)weapon.kills * WeaponPerKill + (long)weapon.headshots * WeaponPerHeadshot, 0, Cap);
    }

    public enum RankTier : byte
    {
        Spark,
        Pulse,
        Surge,
        Arc,
        Flux,
        Nova,
        Zenith,
    }

    /// <summary>Ranked ratings, and the tiers they show as. Every tier name is original.</summary>
    public static class Ranks
    {
        public const int StartRating = 1000;
        public const int PlacementMatches = 5;
        /// <summary>Rating at which each tier starts. Each tier below the top has three divisions.</summary>
        static readonly int[] TierFloor = { 0, 900, 1100, 1300, 1500, 1700, 1900 };

        public static RankTier TierFor(int rating)
        {
            for (int i = TierFloor.Length - 1; i > 0; i--)
            {
                if (rating >= TierFloor[i]) return (RankTier)i;
            }
            return RankTier.Spark;
        }

        /// <summary>1, 2 or 3, from the bottom of the tier to the top. The top tier has no divisions (0).</summary>
        public static int DivisionFor(int rating)
        {
            int tier = (int)TierFor(rating);
            if (tier == TierFloor.Length - 1) return 0;
            int floor = tier == 0 ? 700 : TierFloor[tier];
            int span = TierFloor[tier + 1] - floor;
            return Math.Clamp(1 + (rating - floor) * 3 / span, 1, 3);
        }

        /// <summary>"Surge II", "Zenith", or how many placement matches are left.</summary>
        public static string Name(int rating, int matchesPlayed)
        {
            if (matchesPlayed < PlacementMatches) return "Placement " + matchesPlayed + "/" + PlacementMatches;
            int division = DivisionFor(rating);
            return TierFor(rating) + (division > 0 ? " " + new[] { "", "I", "II", "III" }[division] : "");
        }

        /// <summary>
        /// The change in a player's rating after a match, Elo-style, against the average
        /// rating of the other side. Placement matches move it faster.
        /// </summary>
        /// <param name="score">1 for a win, 0.5 a draw, 0 a loss.</param>
        public static int Change(int rating, int opponents, double score, int matchesPlayed)
        {
            double expected = 1.0 / (1.0 + Math.Pow(10.0, (opponents - rating) / 400.0));
            double k = matchesPlayed < PlacementMatches ? 48 : matchesPlayed < 30 ? 32 : 24;
            return (int)Math.Round(k * (score - expected));
        }

        public static double Score(MatchOutcome outcome) => outcome == MatchOutcome.Won ? 1.0 : outcome == MatchOutcome.Draw ? 0.5 : 0.0;
    }

    // ---------------------------------------------------------------------- what the game server reports

    /// <summary>Everything a game server tells the backend about a finished matchmade match.</summary>
    [Serializable]
    public sealed class MatchReport
    {
        public string matchId = "";
        public int durationSeconds;
        public PlayerReport[] players = new PlayerReport[0];
    }

    /// <summary>One player's match. Bots are left out of reports.</summary>
    [Serializable]
    public sealed class PlayerReport
    {
        /// <summary>From the player's join ticket: an account id, or "guest:..." for a guest.</summary>
        public string accountId = "";
        public string name = "";
        /// <summary>0 or 1, or 255 in a mode without teams.</summary>
        public int team;
        public MatchOutcome outcome;
        /// <summary>Still in the match when it ended. Leaving early is a loss in ranked.</summary>
        public bool completed;
        public int kills, deaths, headshots;
        /// <summary>Points from zones: taking them and holding them.</summary>
        public int objective;
        public int roundsWon;
        public int score;
        public WeaponReport[] weapons = new WeaponReport[0];
    }

    [Serializable]
    public sealed class WeaponReport
    {
        public string weaponId = "";
        /// <summary>The weapon's class, such as "Smg" or "Shotgun", for challenges about a kind of weapon.</summary>
        public string weaponClass = "";
        public int kills;
        public int headshots;
    }

    // ---------------------------------------------------------------------- what the backend hands a game server

    /// <summary>A match for a game server to start: its rules, and who is coming, on which side, with what unlocked.</summary>
    [Serializable]
    public sealed class MatchAssignment
    {
        public string matchId = "";
        public string playlist = "";
        public bool ranked;
        public AssignedPlayer[] players = new AssignedPlayer[0];
    }

    [Serializable]
    public sealed class AssignedPlayer
    {
        public string accountId = "";
        /// <summary>The side the matchmaker balanced them onto, or -1 to let the server choose.</summary>
        public int team = -1;
        public int level = 1;
        /// <summary>Weapon levels above 1. Anything not listed is level 1.</summary>
        public WeaponLevel[] weapons = new WeaponLevel[0];
    }

    [Serializable]
    public sealed class WeaponLevel
    {
        public string weaponId = "";
        public int level = 1;
    }
}
