using UnityEngine;

namespace Pulsebreak.Shared.Simulation
{
    /// <summary>
    /// Physics layers by index. Code uses the indices, so it keeps working even if the
    /// names in Project Settings ▸ Tags and Layers are edited.
    /// </summary>
    public static class GameLayers
    {
        public const int Default = 0;

        /// <summary>Movement capsules of characters. Blocks other characters, ignored by bullets.</summary>
        public const int Character = 8;

        /// <summary>Per-body-part damage colliders. Hit by bullets, ignored by movement.</summary>
        public const int Hitbox = 9;

        /// <summary>Static level geometry: what movement collides with and probes against.</summary>
        public const int WorldMask = 1 << Default;

        /// <summary>What a bullet can hit: level geometry and hitboxes.</summary>
        public const int ShotMask = (1 << Default) | (1 << Hitbox);

        /// <summary>Call once at startup. Kept in code so it cannot drift from the constants above.</summary>
        public static void ConfigureCollisionMatrix()
        {
            Physics.IgnoreLayerCollision(Character, Hitbox, true);
            Physics.IgnoreLayerCollision(Hitbox, Hitbox, true);
        }
    }
}
