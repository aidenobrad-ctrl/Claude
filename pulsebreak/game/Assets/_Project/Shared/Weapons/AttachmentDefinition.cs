using UnityEngine;

namespace Pulsebreak.Shared.Weapons
{
    /// <summary>
    /// One attachment, as an asset. An attachment is a list of stat changes, so the same
    /// asset (say, a suppressor) can fit many weapons: each weapon lists the attachments it accepts.
    ///
    /// To add one: Assets ▸ Create ▸ Pulsebreak ▸ Attachment Definition, then add it to the
    /// attachments list of every weapon it should fit.
    /// </summary>
    [CreateAssetMenu(menuName = "Pulsebreak/Attachment Definition", fileName = "ATT_New")]
    public sealed class AttachmentDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id used in saves, loadouts and the backend. Never change it once shipped.")]
        public string attachmentId = "attachment_id";
        public string displayName = "New Attachment";
        [TextArea] public string description = "";
        public AttachmentSlot slot = AttachmentSlot.Sight;
        [Tooltip("Weapon level needed to unlock it. Used from the progression phase on.")]
        [Min(1)] public int unlockLevel = 1;

        [Header("Effect")]
        [Tooltip("Additions are applied first, then multipliers.")]
        public StatModifier[] modifiers = new StatModifier[0];

        [Header("Sights only")]
        [Tooltip("Magnification when aiming. 0 keeps the weapon's own.")]
        [Min(0f)] public float sightZoom = 0f;
        public ReticleStyle reticle = ReticleStyle.None;

        [Header("Presentation")]
        public AttachmentVisual visual = AttachmentVisual.None;
        [Tooltip("Muffles the shot sound and hides the muzzle flash.")]
        public bool suppressesSound = false;
    }
}
