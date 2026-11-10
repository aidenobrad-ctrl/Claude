using UnityEngine;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Shared.Weapons
{
    public enum WeaponPhase : byte
    {
        Ready,
        Equipping,
        Reloading,
    }

    /// <summary>The complete state of one held weapon. A plain struct, for the same reason as MotorState.</summary>
    public struct WeaponState
    {
        public WeaponPhase Phase;
        /// <summary>Time left in the current phase.</summary>
        public float PhaseTimer;
        public float PhaseDuration;

        public int AmmoInMagazine;
        public int AmmoReserve;

        /// <summary>Time until the next shot may leave. Dips just below zero to carry the remainder between shots.</summary>
        public float FireCooldown;
        /// <summary>A recent trigger pull that has not produced a shot yet (semi-auto and burst).</summary>
        public float TriggerBuffer;
        public int BurstShotsRemaining;

        /// <summary>0 = hip, 1 = fully aimed.</summary>
        public float AdsAmount;
        /// <summary>Extra spread from recent shots, in degrees.</summary>
        public float Bloom;

        /// <summary>Position in the recoil pattern.</summary>
        public int SprayIndex;
        public float TimeSinceLastShot;
        /// <summary>Where recoil is pushing the view. x = right, y = up, degrees.</summary>
        public Vector2 RecoilTarget;
        /// <summary>Where the view actually is, chasing the target.</summary>
        public Vector2 RecoilCurrent;

        /// <summary>Shots fired since the weapon was created. Seeds the spread of each shot.</summary>
        public uint ShotCounter;
        public InputButtons PrevButtons;

        public float ReloadProgress =>
            Phase == WeaponPhase.Reloading && PhaseDuration > 0f ? 1f - PhaseTimer / PhaseDuration : 0f;
    }

    /// <summary>What the weapon needs to know about its carrier this tick.</summary>
    public struct WeaponContext
    {
        /// <summary>False while the hands are busy, for example during a mantle.</summary>
        public bool CanUseWeapon;
        public bool Sprinting;
        public float TimeSinceSprint;
        public float HorizontalSpeed;
        public bool Grounded;
        public bool Crouched;
    }

    /// <summary>One bullet leaving the weapon.</summary>
    public struct ShotEvent
    {
        public int Tick;
        public uint ShotNumber;
        /// <summary>Recoil plus spread to add to the view angles. x = right, y = up, degrees.</summary>
        public Vector2 AimOffset;
        /// <summary>The spread cone half-angle this shot was drawn from, in degrees.</summary>
        public float Spread;
    }
}
