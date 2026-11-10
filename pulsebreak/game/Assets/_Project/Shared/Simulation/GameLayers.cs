using UnityEngine;

namespace Pulsebreak.Shared.Simulation
{
    /// <summary>
    /// Physics layers by index. Code uses the indices, so it keeps working even if the
    /// names in Project Settings ▸ Tags and Layers are edited.
    ///
    /// There are two sets of character layers. The server simulates its own copy of every
    /// player, and when a server and a client share one process (hosting a match from the
    /// game) the two copies must not touch. Level geometry is shared by both.
    /// </summary>
    public static class GameLayers
    {
        public const int Default = 0;

        /// <summary>Movement capsules of characters. Blocks other characters, ignored by bullets.</summary>
        public const int Character = 8;
        /// <summary>Per-body-part damage colliders. Hit by bullets, ignored by movement.</summary>
        public const int Hitbox = 9;
        /// <summary>The server's movement capsules.</summary>
        public const int ServerCharacter = 10;
        /// <summary>The server's damage colliders.</summary>
        public const int ServerHitbox = 11;

        /// <summary>Static level geometry: what movement collides with and probes against.</summary>
        public const int WorldMask = 1 << Default;

        /// <summary>
        /// True while server code is running. It switches the masks below to the server's
        /// layers. The server sets it around each of its ticks; nothing else should.
        /// </summary>
        public static bool ServerContext;

        public static int CharacterLayer => ServerContext ? ServerCharacter : Character;
        public static int HitboxLayer => ServerContext ? ServerHitbox : Hitbox;

        /// <summary>Damageable body parts only. Used by explosions and melee.</summary>
        public static int HitboxMask => 1 << HitboxLayer;

        /// <summary>What a bullet can hit: level geometry and hitboxes.</summary>
        public static int ShotMask => WorldMask | HitboxMask;

        /// <summary>Call once at startup. Kept in code so it cannot drift from the constants above.</summary>
        public static void ConfigureCollisionMatrix()
        {
            int[] characters = { Character, ServerCharacter };
            int[] hitboxes = { Hitbox, ServerHitbox };

            // Hitboxes are only ever found by queries; they collide with nothing.
            foreach (int hitbox in hitboxes)
            {
                foreach (int character in characters) Physics.IgnoreLayerCollision(character, hitbox, true);
                foreach (int other in hitboxes) Physics.IgnoreLayerCollision(hitbox, other, true);
            }
            // The client's characters and the server's are in different worlds.
            Physics.IgnoreLayerCollision(Character, ServerCharacter, true);
            // In a match players pass through each other. It keeps a client's prediction of
            // its own movement independent of where it thinks everyone else is.
            Physics.IgnoreLayerCollision(ServerCharacter, ServerCharacter, true);
        }
    }
}
