using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Shared.Loadouts
{
    /// <summary>
    /// A loadout ready to play: real weapon builds, utility items and a perk set, resolved
    /// from a <see cref="LoadoutData"/> against the catalog.
    /// </summary>
    public sealed class Loadout
    {
        public const int WeaponSlots = 3;
        public const int UtilitySlots = 2;

        public string Id;
        public string Name;
        /// <summary>One build per weapon slot: primary, secondary, melee. An entry is null only if the catalog has nothing for that slot.</summary>
        public WeaponBuild[] Weapons = new WeaponBuild[WeaponSlots];
        public UtilityDefinition[] Utilities = new UtilityDefinition[UtilitySlots];
        public PerkSetDefinition PerkSet;

        /// <summary>A weapon's final numbers: its attachments, then the perk set's weapon bonuses.</summary>
        public WeaponStats ResolveStats(int slot)
        {
            WeaponBuild build = Weapons[slot];
            if (build == null) return null;
            return build.ResolveStats(PerkSet != null ? PerkSet.weaponModifiers : null);
        }

        public float MoveSpeedMultiplier => PerkSet != null ? PerkSet.moveSpeedMultiplier : 1f;
        public float UtilityRechargeMultiplier => PerkSet != null ? PerkSet.utilityRechargeMultiplier : 1f;

        /// <summary>How many of a utility item this loadout carries, perk bonus included.</summary>
        public int UtilityCharges(int slot)
        {
            UtilityDefinition utility = Utilities[slot];
            if (utility == null) return 0;
            return utility.charges + (PerkSet != null ? PerkSet.bonusUtilityCharges : 0);
        }
    }
}
