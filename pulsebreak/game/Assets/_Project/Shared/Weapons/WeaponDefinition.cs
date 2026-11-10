using UnityEngine;

namespace Pulsebreak.Shared.Weapons
{
    /// <summary>
    /// One weapon, as an asset. To add a weapon: Assets ▸ Create ▸ Pulsebreak ▸ Weapon Definition,
    /// fill it in, and add it to the weapon catalog. No code changes are needed unless it
    /// behaves in a way no existing weapon does.
    /// </summary>
    [CreateAssetMenu(menuName = "Pulsebreak/Weapon Definition", fileName = "WPN_New")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id used in saves, loadouts and the backend. Never change it once shipped.")]
        public string weaponId = "weapon_id";
        public string displayName = "New Weapon";
        [TextArea] public string description = "";
        public WeaponClass weaponClass = WeaponClass.AssaultRifle;
        public WeaponSlotType slot = WeaponSlotType.Primary;
        [Tooltip("Player level needed to unlock it. Used from the progression phase on.")]
        [Min(1)] public int unlockLevel = 1;

        [Tooltip("Base numbers, before attachments.")]
        public WeaponStats stats = new WeaponStats();

        [Header("Sights")]
        [Tooltip("What is drawn when aiming with no sight attached. None means iron sights.")]
        public ReticleStyle reticle = ReticleStyle.None;

        [Header("Attachments")]
        [Tooltip("Every attachment that fits this weapon. Each one says which slot it takes.")]
        public AttachmentDefinition[] attachments = new AttachmentDefinition[0];

        [Header("Presentation")]
        public WeaponViewProfile view = new WeaponViewProfile();
        public WeaponAudioProfile audio = new WeaponAudioProfile();
    }
}
