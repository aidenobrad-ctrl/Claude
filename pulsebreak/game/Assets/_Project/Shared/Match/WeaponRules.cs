using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Shared.Match
{
    /// <summary>What a private match lets people carry.</summary>
    public enum WeaponRule : byte
    {
        Any,
        /// <summary>No primary weapon: pistols (and the like), melee and utility.</summary>
        SidearmsOnly,
        /// <summary>Every primary is a sniper or marksman rifle.</summary>
        SnipersOnly,
        /// <summary>Every primary is a shotgun.</summary>
        ShotgunsOnly,
        /// <summary>No launchers and no explosive utility.</summary>
        NoExplosives,
        /// <summary>Melee weapons only.</summary>
        MeleeOnly,
    }

    /// <summary>
    /// Holds loadouts to a private match's weapon rule. Whatever a player chose that the
    /// rule does not allow is swapped for the first weapon in the catalogue that it does, with
    /// no attachments, so everyone always spawns with something to fight with.
    /// </summary>
    public static class WeaponRules
    {
        public static readonly WeaponRule[] All =
        {
            WeaponRule.Any, WeaponRule.SidearmsOnly, WeaponRule.SnipersOnly, WeaponRule.ShotgunsOnly, WeaponRule.NoExplosives, WeaponRule.MeleeOnly,
        };

        public static string Title(WeaponRule rule)
        {
            switch (rule)
            {
                case WeaponRule.SidearmsOnly: return "Sidearms only";
                case WeaponRule.SnipersOnly: return "Snipers only";
                case WeaponRule.ShotgunsOnly: return "Shotguns only";
                case WeaponRule.NoExplosives: return "No explosives";
                case WeaponRule.MeleeOnly: return "Melee only";
                default: return "Any weapons";
            }
        }

        /// <summary>The slots the rule allows, as a slot mask (see <see cref="MatchLoadouts"/>).</summary>
        public static byte Mask(WeaponRule rule)
        {
            switch (rule)
            {
                case WeaponRule.SidearmsOnly: return (byte)((1 << (int)WeaponSlotType.Secondary) | (1 << (int)WeaponSlotType.Melee) | MatchLoadouts.UtilityBit);
                case WeaponRule.MeleeOnly: return (byte)(1 << (int)WeaponSlotType.Melee);
                default: return MatchLoadouts.AllSlots;
            }
        }

        /// <summary>The slot to start holding: the first the rule allows.</summary>
        public static int FirstSlot(WeaponRule rule) =>
            rule == WeaponRule.MeleeOnly ? (int)WeaponSlotType.Melee : rule == WeaponRule.SidearmsOnly ? (int)WeaponSlotType.Secondary : 0;

        /// <summary>A copy of the loadout that keeps to the rule.</summary>
        public static LoadoutData Apply(LoadoutData data, LoadoutCatalog catalog, WeaponRule rule)
        {
            if (rule == WeaponRule.Any || data == null || catalog == null || catalog.weapons == null) return data;
            LoadoutData kept = data.Clone();
            WeaponCatalog weapons = catalog.weapons;
            switch (rule)
            {
                case WeaponRule.SnipersOnly:
                    Require(kept.primary, weapons, WeaponSlotType.Primary, c => c == WeaponClass.Sniper || c == WeaponClass.Marksman);
                    break;
                case WeaponRule.ShotgunsOnly:
                    Require(kept.primary, weapons, WeaponSlotType.Primary, c => c == WeaponClass.Shotgun);
                    break;
                case WeaponRule.NoExplosives:
                    Require(kept.primary, weapons, WeaponSlotType.Primary, c => c != WeaponClass.Launcher);
                    Require(kept.secondary, weapons, WeaponSlotType.Secondary, c => c != WeaponClass.Launcher);
                    UtilityDefinition utility = catalog.FindUtility(kept.utility);
                    if (utility != null && Explosive(utility))
                    {
                        kept.utility = "";
                        foreach (UtilityDefinition other in catalog.utilities)
                        {
                            if (other == null || Explosive(other)) continue;
                            kept.utility = other.utilityId;
                            break;
                        }
                    }
                    break;
            }
            return kept;
        }

        static bool Explosive(UtilityDefinition utility) => utility.projectile != null && utility.projectile.explosionRadius > 0f;

        /// <summary>Swaps the weapon in a slot for the first allowed one, if it is not allowed itself.</summary>
        static void Require(WeaponBuildData build, WeaponCatalog weapons, WeaponSlotType slot, System.Func<WeaponClass, bool> allowed)
        {
            WeaponDefinition current = weapons.FindWeapon(build.weaponId);
            if (current != null && current.slot == slot && allowed(current.weaponClass)) return;
            foreach (WeaponDefinition weapon in weapons.weapons)
            {
                if (weapon == null || weapon.slot != slot || !allowed(weapon.weaponClass)) continue;
                build.weaponId = weapon.weaponId;
                build.attachments = new string[0];
                return;
            }
        }
    }
}
