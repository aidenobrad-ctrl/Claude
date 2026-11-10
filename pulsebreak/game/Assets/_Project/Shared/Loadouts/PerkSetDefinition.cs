using UnityEngine;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Shared.Loadouts
{
    /// <summary>
    /// A perk set: a named bundle of passive bonuses, one per loadout. Weapon bonuses use
    /// the same stat modifiers as attachments and apply to every weapon carried.
    /// </summary>
    [CreateAssetMenu(menuName = "Pulsebreak/Perk Set Definition", fileName = "PRK_New")]
    public sealed class PerkSetDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id used in saves, loadouts and the backend. Never change it once shipped.")]
        public string perkSetId = "perk_set_id";
        public string displayName = "New Perk Set";
        [TextArea] public string description = "";
        [Tooltip("Player level needed to unlock it. Used from the progression phase on.")]
        [Min(1)] public int unlockLevel = 1;

        [Header("Effects")]
        [Tooltip("Applied to every weapon in the loadout, after its attachments.")]
        public StatModifier[] weaponModifiers = new StatModifier[0];
        [Tooltip("Multiplies movement speed. 1.05 is five percent faster.")]
        [Range(0.8f, 1.2f)] public float moveSpeedMultiplier = 1f;
        [Tooltip("Extra charges for each utility item.")]
        [Range(0, 3)] public int bonusUtilityCharges = 0;
        [Tooltip("Multiplies how fast utility recharges where recharging is on. 1.5 is half as fast again.")]
        [Range(0.5f, 3f)] public float utilityRechargeMultiplier = 1f;
    }
}
