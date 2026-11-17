using System;
using System.Collections.Generic;

namespace Pulsebreak.Shared.Weapons
{
    /// <summary>
    /// A weapon plus the attachments fitted to it: one per slot at most. This is what a
    /// loadout stores, and <see cref="ResolveStats"/> is the single place attachment effects
    /// are turned into numbers, so client and server always agree on them.
    /// </summary>
    public sealed class WeaponBuild
    {
        public static readonly int SlotCount = Enum.GetValues(typeof(AttachmentSlot)).Length;

        readonly AttachmentDefinition[] _fitted = new AttachmentDefinition[SlotCount];

        public WeaponDefinition Weapon { get; }

        public WeaponBuild(WeaponDefinition weapon)
        {
            Weapon = weapon;
        }

        public WeaponBuild(WeaponDefinition weapon, IEnumerable<AttachmentDefinition> attachments) : this(weapon)
        {
            if (attachments == null) return;
            foreach (AttachmentDefinition attachment in attachments) Fit(attachment);
        }

        public AttachmentDefinition Get(AttachmentSlot slot) => _fitted[(int)slot];

        public bool Accepts(AttachmentDefinition attachment) =>
            attachment != null && Weapon.attachments != null && Array.IndexOf(Weapon.attachments, attachment) >= 0;

        /// <summary>Fits an attachment in its own slot, replacing what was there. Returns false if it does not fit this weapon.</summary>
        public bool Fit(AttachmentDefinition attachment)
        {
            if (!Accepts(attachment)) return false;
            _fitted[(int)attachment.slot] = attachment;
            return true;
        }

        public void Clear(AttachmentSlot slot) => _fitted[(int)slot] = null;

        /// <summary>The attachments this weapon accepts in one slot, in catalog order.</summary>
        public List<AttachmentDefinition> OptionsFor(AttachmentSlot slot)
        {
            var options = new List<AttachmentDefinition>();
            if (Weapon.attachments == null) return options;
            foreach (AttachmentDefinition attachment in Weapon.attachments)
            {
                if (attachment != null && attachment.slot == slot) options.Add(attachment);
            }
            return options;
        }

        /// <summary>The reticle drawn when aiming: the fitted sight's, or the weapon's own.</summary>
        public ReticleStyle Reticle
        {
            get
            {
                AttachmentDefinition sight = Get(AttachmentSlot.Sight);
                return sight != null && sight.reticle != ReticleStyle.None ? sight.reticle : Weapon.reticle;
            }
        }

        public bool Suppressed
        {
            get
            {
                foreach (AttachmentDefinition attachment in _fitted)
                {
                    if (attachment != null && attachment.suppressesSound) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// The weapon's base stats with every fitted attachment applied, plus any extra
        /// modifiers from outside the weapon (a perk set). Additions go first, then
        /// multipliers, so the order things were fitted in never matters.
        /// </summary>
        public WeaponStats ResolveStats(StatModifier[] extraModifiers = null)
        {
            WeaponStats stats = Weapon.stats.Clone();

            ApplyAll(stats, ModifierOp.Add, extraModifiers);
            ApplyAll(stats, ModifierOp.Multiply, extraModifiers);

            AttachmentDefinition sight = Get(AttachmentSlot.Sight);
            if (sight != null && sight.sightZoom > 0f) stats.adsZoom = sight.sightZoom;

            stats.Sanitize();
            return stats;
        }

        /// <summary>The ids of the fitted attachments, in slot order. This is what gets saved.</summary>
        public string[] FittedIds()
        {
            var ids = new List<string>();
            foreach (AttachmentDefinition attachment in _fitted)
            {
                if (attachment != null) ids.Add(attachment.attachmentId);
            }
            return ids.ToArray();
        }

        void ApplyAll(WeaponStats stats, ModifierOp op, StatModifier[] extraModifiers)
        {
            foreach (AttachmentDefinition attachment in _fitted)
            {
                if (attachment == null || attachment.modifiers == null) continue;
                foreach (StatModifier modifier in attachment.modifiers)
                {
                    if (modifier.op == op) stats.Apply(modifier);
                }
            }

            if (extraModifiers == null) return;
            foreach (StatModifier modifier in extraModifiers)
            {
                if (modifier.op == op) stats.Apply(modifier);
            }
        }
    }
}
