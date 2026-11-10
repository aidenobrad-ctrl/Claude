using UnityEngine;
using Pulsebreak.Shared.Combat;

namespace Pulsebreak.Shared.Weapons
{
    /// <summary>The one place damage is worked out, so client feedback and server authority cannot disagree.</summary>
    public static class DamageCalculator
    {
        public static float Compute(WeaponDefinition weapon, float distance, HitZone zone) =>
            weapon.damage * FalloffMultiplier(weapon, distance) * ZoneMultiplier(weapon, zone);

        /// <summary>1 inside <c>falloffStart</c>, dropping linearly to <c>minDamageMultiplier</c> at <c>falloffEnd</c>.</summary>
        public static float FalloffMultiplier(WeaponDefinition weapon, float distance)
        {
            if (distance <= weapon.falloffStart || weapon.falloffEnd <= weapon.falloffStart) return 1f;
            if (distance >= weapon.falloffEnd) return weapon.minDamageMultiplier;

            float t = (distance - weapon.falloffStart) / (weapon.falloffEnd - weapon.falloffStart);
            return Mathf.Lerp(1f, weapon.minDamageMultiplier, t);
        }

        public static float ZoneMultiplier(WeaponDefinition weapon, HitZone zone)
        {
            switch (zone)
            {
                case HitZone.Head: return weapon.headMultiplier;
                case HitZone.Limb: return weapon.limbMultiplier;
                default: return 1f;
            }
        }
    }
}
