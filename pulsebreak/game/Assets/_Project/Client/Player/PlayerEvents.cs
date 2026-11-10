using UnityEngine;
using Pulsebreak.Shared.Combat;

namespace Pulsebreak.Client.Player
{
    /// <summary>Where a bullet went, for tracers and impact marks.</summary>
    public struct ShotPresentation
    {
        public Vector3 Origin;
        public Vector3 End;
        public Vector3 Normal;
        /// <summary>The bullet stopped on level geometry.</summary>
        public bool HitWorld;
        /// <summary>The bullet damaged something.</summary>
        public bool HitTarget;
    }

    /// <summary>A confirmed hit, for hit markers, damage numbers and stats.</summary>
    public struct HitFeedback
    {
        /// <summary>Simulation tick the bullet was fired on.</summary>
        public int Tick;
        public Vector3 Point;
        public float Damage;
        public HitZone Zone;
        public bool Killed;
        public float Distance;
        public IDamageable Target;
    }
}
