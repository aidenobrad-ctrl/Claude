using UnityEngine;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Shared.Weapons
{
    public struct HitscanHit
    {
        public bool HasHit;
        /// <summary>Impact point, or the end of the ray if nothing was hit.</summary>
        public Vector3 Point;
        public Vector3 Normal;
        public float Distance;
        public Collider Collider;
        /// <summary>Set when the ray ended on a damageable body part.</summary>
        public Hitbox Hitbox;
    }

    /// <summary>
    /// Traces an instant bullet. It only reports what the ray met; applying damage is a
    /// separate step, because online only the server is allowed to do that.
    /// </summary>
    public static class Hitscan
    {
        /// <param name="layerMask">0 uses whatever bullets can currently hit.</param>
        public static HitscanHit Trace(Vector3 origin, Vector3 direction, float maxRange, int layerMask = 0)
        {
            if (layerMask == 0) layerMask = GameLayers.ShotMask;
            var result = new HitscanHit
            {
                Point = origin + direction * maxRange,
                Distance = maxRange,
            };

            if (Physics.Raycast(origin, direction, out RaycastHit hit, maxRange, layerMask, QueryTriggerInteraction.Ignore))
            {
                result.HasHit = true;
                result.Point = hit.point;
                result.Normal = hit.normal;
                result.Distance = hit.distance;
                result.Collider = hit.collider;
                if (hit.collider.TryGetComponent(out Hitbox hitbox)) result.Hitbox = hitbox;
            }

            return result;
        }
    }
}
