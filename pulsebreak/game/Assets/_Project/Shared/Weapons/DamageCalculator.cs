using UnityEngine;
using Pulsebreak.Shared.Combat;

namespace Pulsebreak.Shared.Weapons
{
    /// <summary>The one place damage is worked out, so client feedback and server authority cannot disagree.</summary>
    public static class DamageCalculator
    {
        /// <summary>Damage of one bullet or pellet at a distance, on a body part.</summary>
        public static float Compute(WeaponStats stats, float distance, HitZone zone) =>
            stats.damage * FalloffMultiplier(stats, distance) * ZoneMultiplier(stats, zone);

        /// <summary>1 inside <c>falloffStart</c>, dropping linearly to <c>minDamageMultiplier</c> at <c>falloffEnd</c>.</summary>
        public static float FalloffMultiplier(WeaponStats stats, float distance)
        {
            if (distance <= stats.falloffStart || stats.falloffEnd <= stats.falloffStart) return 1f;
            if (distance >= stats.falloffEnd) return stats.minDamageMultiplier;

            float t = (distance - stats.falloffStart) / (stats.falloffEnd - stats.falloffStart);
            return Mathf.Lerp(1f, stats.minDamageMultiplier, t);
        }

        public static float ZoneMultiplier(WeaponStats stats, HitZone zone)
        {
            switch (zone)
            {
                case HitZone.Head: return stats.headMultiplier;
                case HitZone.Limb: return stats.limbMultiplier;
                default: return 1f;
            }
        }

        /// <summary>
        /// Damage of an explosion at a distance from its centre: full at the centre, falling
        /// to <c>explosionEdgeDamage</c> at the edge, nothing beyond it.
        /// </summary>
        public static float ExplosionDamage(WeaponStats stats, float distanceFromCentre)
        {
            if (stats.explosionRadius <= 0f || distanceFromCentre > stats.explosionRadius) return 0f;
            float t = Mathf.Clamp01(distanceFromCentre / stats.explosionRadius);
            return stats.damage * Mathf.Lerp(1f, stats.explosionEdgeDamage, t);
        }
    }
}
