using System;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Progression;

namespace Pulsebreak.Client.Online
{
    /// <summary>
    /// The signed-in player's progression as last heard from the backend: level, weapon levels,
    /// challenges and ranks. Screens read it from here; whoever talks to the backend fills it.
    /// Empty (null) when nobody is signed in.
    /// </summary>
    public static class ProgressionCache
    {
        public static ProgressionData Current { get; private set; }

        /// <summary>New progression arrived, or the player signed out.</summary>
        public static event Action Changed;

        public static void Set(ProgressionData data)
        {
            Current = data;
            Changed?.Invoke();
        }

        /// <summary>What the player has unlocked for matchmaking: their level and weapon levels, or the starter set when signed out.</summary>
        public static PlayerUnlocks Unlocks()
        {
            if (Current == null) return PlayerUnlocks.Starter;
            var unlocks = new PlayerUnlocks { Level = Math.Max(1, Current.level.level) };
            foreach (WeaponProgressData weapon in Current.weapons ?? new WeaponProgressData[0])
            {
                if (weapon != null) unlocks.SetWeaponLevel(weapon.weaponId, weapon.level);
            }
            return unlocks;
        }

        /// <summary>The player's rank in a ranked playlist, or a fresh one if they have not played it.</summary>
        public static RankData Rank(string playlist)
        {
            if (Current?.ranks != null)
            {
                foreach (RankData rank in Current.ranks)
                {
                    if (rank != null && rank.playlist == playlist) return rank;
                }
            }
            return new RankData { playlist = playlist, rating = Ranks.StartRating, name = Ranks.Name(Ranks.StartRating, 0), placementLeft = Ranks.PlacementMatches };
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Current = null;
            Changed = null;
        }
    }
}
