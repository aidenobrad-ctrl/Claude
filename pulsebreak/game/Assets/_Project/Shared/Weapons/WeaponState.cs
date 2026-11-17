using UnityEngine;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Shared.Weapons
{
    public enum WeaponPhase : byte
    {
        Ready,
        Equipping,
        Reloading,
        Holstering,
    }

    /// <summary>The complete state of one held weapon. A plain struct, for the same reason as MotorState.</summary>
    public struct WeaponState
    {
        public WeaponPhase Phase;
        /// <summary>Time left in the current phase (for a per-round reload, until the next round goes in).</summary>
        public float PhaseTimer;
        public float PhaseDuration;
        public float ReloadElapsed;
        public float ReloadTotal;

        public int AmmoInMagazine;
        public int AmmoReserve;

        /// <summary>Time until the next shot may leave. Dips just below zero to carry the remainder between shots.</summary>
        public float FireCooldown;
        /// <summary>A recent trigger pull that has not produced a shot yet (semi-auto and burst).</summary>
        public float TriggerBuffer;
        public int BurstShotsRemaining;
        /// <summary>Charge weapons: 0 = no charge, 1 = full.</summary>
        public float ChargeAmount;

        /// <summary>A melee swing is under way and has not connected yet.</summary>
        public bool MeleePending;
        public float MeleeTimer;

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
            Phase == WeaponPhase.Reloading && ReloadTotal > 0f ? Mathf.Clamp01(ReloadElapsed / ReloadTotal) : 0f;
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

    public enum ShotKind : byte
    {
        Bullet,
        Projectile,
        Melee,
    }

    /// <summary>
    /// One thing leaving the weapon: a bullet, a projectile or a melee strike. A shotgun
    /// blast produces one of these per pellet, all with the same shot number.
    /// </summary>
    public struct ShotEvent
    {
        public int Tick;
        public uint ShotNumber;
        public ShotKind Kind;
        public int Pellet;
        /// <summary>Recoil plus spread to add to the view angles. x = right, y = up, degrees.</summary>
        public Vector2 AimOffset;
        /// <summary>The spread cone half-angle this shot was drawn from, in degrees.</summary>
        public float Spread;
        /// <summary>Damage multiplier from charging. 1 for everything that does not charge.</summary>
        public float DamageScale;
    }
}
