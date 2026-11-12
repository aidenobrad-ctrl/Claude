using System;
using UnityEngine;

namespace Pulsebreak.Shared.Simulation
{
    [Flags]
    public enum InputButtons : ushort
    {
        None = 0,
        Jump = 1 << 0,
        Sprint = 1 << 1,
        Crouch = 1 << 2,
        Fire = 1 << 3,
        Ads = 1 << 4,
        Reload = 1 << 5,
        LeanLeft = 1 << 6,
        LeanRight = 1 << 7,
        Utility1 = 1 << 8,
        /// <summary>No longer used: loadouts carry one utility item. Kept so the other bits keep their values.</summary>
        Utility2 = 1 << 9,
    }

    /// <summary>
    /// Everything a player asked for during one simulation tick. This is the only thing
    /// the simulation reads from the player, and (from Phase 2) the only thing a client
    /// sends to the server.
    /// </summary>
    public struct PlayerInputCmd
    {
        public int Tick;

        /// <summary>x = strafe, y = forward, each in [-1, 1].</summary>
        public Vector2 Move;

        /// <summary>Absolute view yaw in degrees.</summary>
        public float Yaw;

        /// <summary>Absolute view pitch in degrees. Positive looks down, as Unity's X rotation does.</summary>
        public float Pitch;

        /// <summary>Buttons held this tick. A tap shorter than a tick still shows as held for one tick.</summary>
        public InputButtons Buttons;

        /// <summary>Weapon slot to switch to, counting from 1. 0 means no change.</summary>
        public byte SelectSlot;

        /// <summary>
        /// Online only: the server tick the player was looking at when this was sampled.
        /// Other players are drawn slightly in the past, and the server uses this to put
        /// them back where the shooter saw them before working out what a shot hit.
        /// </summary>
        public float ViewTick;

        public bool Has(InputButtons button) => (Buttons & button) != 0;
    }

    public static class AimMath
    {
        /// <summary>Unit forward vector on the ground plane for a yaw in degrees.</summary>
        public static Vector3 YawForward(float yawDeg)
        {
            float r = yawDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        /// <summary>Unit right vector on the ground plane for a yaw in degrees.</summary>
        public static Vector3 YawRight(float yawDeg)
        {
            float r = yawDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(r), 0f, -Mathf.Sin(r));
        }

        /// <summary>
        /// View direction for yaw and pitch in degrees. Matches
        /// <c>Quaternion.Euler(pitch, yaw, 0) * Vector3.forward</c> without building a quaternion.
        /// </summary>
        public static Vector3 Direction(float yawDeg, float pitchDeg)
        {
            float y = yawDeg * Mathf.Deg2Rad;
            float p = pitchDeg * Mathf.Deg2Rad;
            float cp = Mathf.Cos(p);
            return new Vector3(Mathf.Sin(y) * cp, -Mathf.Sin(p), Mathf.Cos(y) * cp);
        }
    }
}
