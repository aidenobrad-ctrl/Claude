using UnityEngine;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.Player
{
    /// <summary>Where one bullet or pellet went, for tracers and impact marks.</summary>
    public struct ShotPresentation
    {
        public Vector3 Origin;
        public Vector3 End;
        public Vector3 Normal;
        /// <summary>The bullet stopped on level geometry.</summary>
        public bool HitWorld;
        /// <summary>The bullet damaged something.</summary>
        public bool HitTarget;
        public WeaponBuild Build;
    }
}
