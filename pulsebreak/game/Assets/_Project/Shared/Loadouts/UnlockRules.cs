using System.Collections.Generic;
using Pulsebreak.Shared.Progression;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Shared.Loadouts
{
    /// <summary>What a player has unlocked: their level, and the level of each weapon they have used.</summary>
    public sealed class PlayerUnlocks
    {
        public int Level = 1;
        readonly Dictionary<string, int> _weaponLevels = new Dictionary<string, int>();

        /// <summary>A new player: level 1, every weapon at level 1. Guests always play with this.</summary>
        public static PlayerUnlocks Starter => new PlayerUnlocks();

        /// <summary>Everything unlocked: private matches and the firing range.</summary>
        public static PlayerUnlocks Everything => new PlayerUnlocks { Level = int.MaxValue, _all = true };

        bool _all;

        public bool HasEverything => _all;

        public int WeaponLevel(string weaponId) =>
            _all ? int.MaxValue : weaponId != null && _weaponLevels.TryGetValue(weaponId, out int level) ? level : 1;

        public void SetWeaponLevel(string weaponId, int level)
        {
            if (!string.IsNullOrEmpty(weaponId)) _weaponLevels[weaponId] = level;
        }

        public static PlayerUnlocks From(AssignedPlayer assigned)
        {
            var unlocks = new PlayerUnlocks { Level = assigned != null ? assigned.level : 1 };
            if (assigned?.weapons == null) return unlocks;
            foreach (WeaponLevel weapon in assigned.weapons)
            {
                if (weapon != null) unlocks.SetWeaponLevel(weapon.weaponId, weapon.level);
            }
            return unlocks;
        }
    }

    /// <summary>
    /// Which weapons, attachments, utility items and perk sets a player may carry in matchmade
    /// games. Weapons, utility items and perk sets open up with the player's level; attachments
    /// with the level of the weapon they fit. Private matches and the firing range have
    /// everything open, so none of this applies there.
    /// </summary>
    public static class UnlockRules
    {
        public static bool IsUnlocked(WeaponDefinition weapon, PlayerUnlocks unlocks) => weapon == null || weapon.unlockLevel <= unlocks.Level;

        public static bool IsUnlocked(AttachmentDefinition attachment, string weaponId, PlayerUnlocks unlocks) =>
            attachment == null || attachment.unlockLevel <= unlocks.WeaponLevel(weaponId);

        public static bool IsUnlocked(UtilityDefinition utility, PlayerUnlocks unlocks) => utility == null || utility.unlockLevel <= unlocks.Level;

        public static bool IsUnlocked(PerkSetDefinition perkSet, PlayerUnlocks unlocks) => perkSet == null || perkSet.unlockLevel <= unlocks.Level;

        /// <summary>
        /// A copy of a loadout with everything the player has not unlocked swapped out: a
        /// locked weapon for the first open one in its slot, locked attachments taken off, a
        /// locked utility item or perk set for the first open one.
        /// </summary>
        /// <param name="notes">If given, receives a line for everything swapped.</param>
        public static LoadoutData Apply(LoadoutData data, LoadoutCatalog catalog, PlayerUnlocks unlocks, List<string> notes = null)
        {
            LoadoutData clean = LoadoutRules.Sanitize(data, catalog);
            if (unlocks == null || unlocks.HasEverything) return clean;

            for (int slot = 0; slot < Loadout.WeaponSlots; slot++)
            {
                WeaponBuildData build = clean.GetWeapon(slot);
                WeaponDefinition weapon = catalog.weapons.FindWeapon(build.weaponId);
                if (!IsUnlocked(weapon, unlocks))
                {
                    WeaponDefinition replacement = FirstOpenWeapon(catalog, weapon.slot, unlocks);
                    notes?.Add(weapon.displayName + " unlocks at level " + weapon.unlockLevel + "; using " + replacement.displayName + ".");
                    build.weaponId = replacement.weaponId;
                    build.attachments = new string[0];
                    continue;
                }

                var kept = new List<string>();
                foreach (string id in build.attachments)
                {
                    AttachmentDefinition attachment = catalog.weapons.FindAttachment(id);
                    if (IsUnlocked(attachment, build.weaponId, unlocks)) kept.Add(id);
                    else notes?.Add(attachment.displayName + " unlocks at " + weapon.displayName + " level " + attachment.unlockLevel + ".");
                }
                build.attachments = kept.ToArray();
            }

            UtilityDefinition utility = catalog.FindUtility(clean.utility);
            if (!IsUnlocked(utility, unlocks))
            {
                UtilityDefinition replacement = FirstOpen(catalog.utilities, u => IsUnlocked(u, unlocks));
                notes?.Add(utility.displayName + " unlocks at level " + utility.unlockLevel + ".");
                clean.utility = replacement != null ? replacement.utilityId : clean.utility;
            }

            PerkSetDefinition perkSet = catalog.FindPerkSet(clean.perkSet);
            if (!IsUnlocked(perkSet, unlocks))
            {
                PerkSetDefinition replacement = FirstOpen(catalog.perkSets, p => IsUnlocked(p, unlocks));
                notes?.Add(perkSet.displayName + " unlocks at level " + perkSet.unlockLevel + ".");
                clean.perkSet = replacement != null ? replacement.perkSetId : clean.perkSet;
            }
            return clean;
        }

        /// <summary>The open weapon for a slot with the lowest unlock level: the starter.</summary>
        static WeaponDefinition FirstOpenWeapon(LoadoutCatalog catalog, WeaponSlotType slot, PlayerUnlocks unlocks)
        {
            WeaponDefinition best = null;
            foreach (WeaponDefinition weapon in catalog.weapons.weapons)
            {
                if (weapon == null || weapon.slot != slot || !IsUnlocked(weapon, unlocks)) continue;
                if (best == null || weapon.unlockLevel < best.unlockLevel) best = weapon;
            }
            return best ?? catalog.weapons.FirstForSlot(slot);
        }

        static T FirstOpen<T>(T[] items, System.Func<T, bool> open) where T : class
        {
            foreach (T item in items)
            {
                if (item != null && open(item)) return item;
            }
            return items.Length > 0 ? items[0] : null;
        }
    }
}
