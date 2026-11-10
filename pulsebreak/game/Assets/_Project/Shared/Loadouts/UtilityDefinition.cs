using UnityEngine;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Shared.Loadouts
{
    /// <summary>
    /// A throwable carried in one of the two utility slots: a grenade, a charge, a disc.
    /// What it does once thrown is described by the same projectile stats weapons use, so a
    /// new throwable is an asset, not code.
    /// </summary>
    [CreateAssetMenu(menuName = "Pulsebreak/Utility Definition", fileName = "UTL_New")]
    public sealed class UtilityDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id used in saves, loadouts and the backend. Never change it once shipped.")]
        public string utilityId = "utility_id";
        public string displayName = "New Utility";
        [TextArea] public string description = "";
        [Tooltip("Player level needed to unlock it. Used from the progression phase on.")]
        [Min(1)] public int unlockLevel = 1;

        [Header("Use")]
        [Tooltip("How many are carried.")]
        [Min(1)] public int charges = 1;
        [Tooltip("Wind-up between pressing the key and letting go. The weapon cannot fire during it.")]
        [Min(0f)] public float throwTime = 0.25f;
        [Tooltip("Pause after a throw before the next.")]
        [Min(0f)] public float cooldown = 0.6f;
        [Tooltip("Seconds to get one back where recharging is on (the firing range). 0 never recharges.")]
        [Min(0f)] public float rechargeTime = 6f;
        [Tooltip("Thrown this many degrees above where you are looking.")]
        public float tossPitch = 6f;

        [Tooltip("The thrown object. Only the delivery, damage and projectile sections are used.")]
        public WeaponStats projectile = new WeaponStats { ballistics = BallisticsType.Projectile };

        [Header("Presentation")]
        public Color color = new Color(1f, 0.55f, 0.15f);
    }
}
