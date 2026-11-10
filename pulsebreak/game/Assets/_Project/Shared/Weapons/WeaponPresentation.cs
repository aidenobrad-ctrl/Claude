using System;
using UnityEngine;

namespace Pulsebreak.Shared.Weapons
{
    /// <summary>
    /// Describes a weapon's placeholder first-person model as a handful of proportions. The
    /// client builds the model from these until real art replaces it. Lengths are metres.
    /// </summary>
    [Serializable]
    public sealed class WeaponViewProfile
    {
        public ViewModelKind kind = ViewModelKind.Gun;
        [Tooltip("Body length, not counting barrel or stock.")]
        [Min(0.05f)] public float bodyLength = 0.3f;
        [Tooltip("Thickness multiplier. 1 is a rifle, 0.7 a pistol, 1.4 a machine gun.")]
        [Min(0.2f)] public float bulk = 1f;
        [Min(0f)] public float barrelLength = 0.3f;
        [Tooltip("0 for no stock.")]
        [Min(0f)] public float stockLength = 0.2f;
        [Tooltip("0 for no visible magazine.")]
        [Min(0f)] public float magazineLength = 0.14f;
        public Color bodyColor = new Color(0.16f, 0.17f, 0.2f);
        public Color accentColor = new Color(0.15f, 0.85f, 1f);
    }

    /// <summary>Parameters for the synthesised placeholder shot sound.</summary>
    [Serializable]
    public sealed class WeaponAudioProfile
    {
        public ShotSoundKind kind = ShotSoundKind.Ballistic;
        [Tooltip("1 is a rifle. Lower is deeper.")]
        [Range(0.4f, 2f)] public float pitch = 1f;
        [Tooltip("How much low-end thump the shot has.")]
        [Range(0f, 1.5f)] public float weight = 1f;
        [Tooltip("How long the shot rings out, in seconds.")]
        [Range(0.05f, 0.8f)] public float tail = 0.25f;
        [Range(0f, 1f)] public float volume = 0.8f;
    }
}
