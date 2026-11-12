using UnityEngine;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Shared.Movement
{
    public enum Stance : byte
    {
        Standing,
        Crouching,
        Sliding,
    }

    /// <summary>
    /// Per-tick modifiers that come from outside the motor, such as the equipped weapon.
    /// </summary>
    public struct MoveModifiers
    {
        /// <summary>Scales run, sprint and crouch speed. 1 = unmodified.</summary>
        public float SpeedMultiplier;

        public static MoveModifiers None => new MoveModifiers { SpeedMultiplier = 1f };
    }

    /// <summary>
    /// The complete movement state of one character. It is a plain struct on purpose:
    /// client-side prediction needs to save a copy every tick and restore one when the
    /// server disagrees.
    /// </summary>
    public struct MotorState
    {
        /// <summary>Feet position.</summary>
        public Vector3 Position;
        public Vector3 Velocity;
        public float Yaw;
        public float Pitch;

        public Stance Stance;
        public bool Grounded;
        public bool Sprinting;
        public bool Mantling;
        public bool JustLanded;
        public Vector3 GroundNormal;

        /// <summary>Eye height above the feet, eased between stances.</summary>
        public float EyeHeight;

        public float CoyoteTimer;
        public float JumpBufferTimer;
        public float SlideTimer;
        public float SlideCooldown;
        public float TimeSinceSprint;

        public float MantleTimer;
        public float MantleDuration;
        public float MantleCooldown;
        public Vector3 MantleFrom;
        public Vector3 MantleTo;

        public InputButtons PrevButtons;

        public float HorizontalSpeed
        {
            get
            {
                float x = Velocity.x, z = Velocity.z;
                return Mathf.Sqrt(x * x + z * z);
            }
        }

        public Vector3 EyePosition => Position + Vector3.up * EyeHeight;
    }
}
