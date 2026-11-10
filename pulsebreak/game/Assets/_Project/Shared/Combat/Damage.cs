using UnityEngine;

namespace Pulsebreak.Shared.Combat
{
    public enum HitZone : byte
    {
        Body,
        Head,
        Limb,
    }

    /// <summary>One instance of damage, as handed to whatever was hit.</summary>
    public struct DamageInfo
    {
        public float Amount;
        public HitZone Zone;
        public Vector3 Point;
        public Vector3 Direction;
        public float Distance;
        public string WeaponId;
    }

    /// <summary>What actually happened when damage was applied.</summary>
    public struct DamageResult
    {
        /// <summary>Health removed. Less than the requested amount on an overkill, 0 if already dead.</summary>
        public float Applied;
        public bool Killed;
    }

    /// <summary>A hit that dealt damage, for hit markers, damage numbers and stats.</summary>
    public struct HitReport
    {
        /// <summary>Simulation tick the damage was applied on.</summary>
        public int Tick;
        public Vector3 Point;
        public float Damage;
        public HitZone Zone;
        public bool Killed;
        /// <summary>How far the shot travelled, or for an explosion, how far from its centre.</summary>
        public float Distance;
        public IDamageable Target;
        public string WeaponId;
    }

    public interface IDamageable
    {
        bool IsAlive { get; }
        DamageResult ApplyDamage(in DamageInfo info);
    }
}
