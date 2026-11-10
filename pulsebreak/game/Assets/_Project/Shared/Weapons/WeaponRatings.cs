using UnityEngine;

namespace Pulsebreak.Shared.Weapons
{
    /// <summary>
    /// A weapon's stats boiled down to what a player compares: a few 0..1 bars and the
    /// headline numbers. Used by the armory to show what an attachment changes. These are
    /// summaries for display only; nothing in the simulation reads them.
    /// </summary>
    public struct WeaponRatings
    {
        const float TargetHealth = 100f;

        public float Damage;
        public float FireRate;
        public float Range;
        public float Accuracy;
        public float Control;
        public float Mobility;
        public float Handling;

        /// <summary>Damage of one trigger pull landing fully on the body at close range.</summary>
        public float DamagePerShot;
        /// <summary>Shots per minute actually achievable, counting burst pauses and charge time.</summary>
        public float EffectiveRpm;
        public float DamagePerSecond;
        /// <summary>Body shots to drop a full-health target at close range.</summary>
        public int ShotsToKill;
        /// <summary>Seconds from the first shot to the kill, every shot landing on the body.</summary>
        public float TimeToKill;

        public static WeaponRatings From(WeaponStats s)
        {
            var r = new WeaponRatings();
            float interval = s.SecondsPerShot;

            r.DamagePerShot = s.damage * Mathf.Max(1, s.pellets);
            r.ShotsToKill = r.DamagePerShot > 0f ? Mathf.CeilToInt(TargetHealth / r.DamagePerShot - 0.0001f) : 0;

            float leadIn = 0f;
            switch (s.fireMode)
            {
                case FireMode.Burst:
                {
                    int burst = Mathf.Max(1, s.burstCount);
                    r.EffectiveRpm = 60f * burst / (burst * interval + s.burstCooldown);
                    break;
                }
                case FireMode.Charge:
                    r.EffectiveRpm = 60f / (s.chargeTime + interval);
                    leadIn = s.chargeTime;
                    break;
                default:
                    r.EffectiveRpm = s.roundsPerMinute;
                    break;
            }
            if (s.ballistics == BallisticsType.Melee) leadIn = s.meleeHitDelay;

            r.DamagePerSecond = r.DamagePerShot * r.EffectiveRpm / 60f;

            if (r.ShotsToKill > 0)
            {
                int gaps = r.ShotsToKill - 1;
                float time = leadIn;
                if (s.fireMode == FireMode.Burst)
                    time += gaps * interval + (gaps / Mathf.Max(1, s.burstCount)) * s.burstCooldown;
                else if (s.fireMode == FireMode.Charge)
                    time += gaps * (interval + s.chargeTime);
                else
                    time += gaps * interval;
                r.TimeToKill = time;
            }

            // Square root, so the gap between a rifle and an SMG is as visible as the gap
            // between a rifle and a shotgun.
            r.Damage = Mathf.Sqrt(Mathf.Clamp01(r.DamagePerShot / 120f));
            r.FireRate = Mathf.Clamp01(r.EffectiveRpm / 1100f);

            float reach;
            switch (s.ballistics)
            {
                case BallisticsType.Melee:
                    reach = s.meleeRange;
                    break;
                case BallisticsType.Projectile:
                    // No damage falloff to go by: what limits a projectile is how slow it is
                    // and whether it drops.
                    reach = s.projectileSpeed * (s.projectileGravity > 0f ? 0.27f : 0.45f);
                    break;
                default:
                    reach = (s.falloffStart + s.falloffEnd) * 0.5f;
                    break;
            }
            r.Range = Mathf.Clamp01(reach / 90f);

            r.Accuracy = 1f - Mathf.Clamp01((s.hipSpread + 4f * s.adsSpread + 0.3f * s.maxBloom) / 6f);

            // Average kick over the opening shots, weighted by how often they arrive.
            float kick = 0f;
            const int sample = 10;
            for (int i = 0; i < sample; i++) kick += s.GetRecoilKick(i).magnitude;
            kick = kick / sample * s.adsRecoilMultiplier * Mathf.Clamp(r.EffectiveRpm / 600f, 0.25f, 2f);
            r.Control = 1f - Mathf.Clamp01(kick / 1.4f);

            float hip = Mathf.InverseLerp(0.8f, 1.1f, s.hipMoveMultiplier);
            float aimed = Mathf.InverseLerp(0.4f, 1f, s.adsMoveMultiplier);
            r.Mobility = Mathf.Clamp01(hip * 0.6f + aimed * 0.4f);

            float reload = s.UsesAmmo ? s.reloadTime + s.reloadIntroTime : 0f;
            r.Handling = 1f - Mathf.Clamp01(
                (s.adsTime * 1.5f + s.sprintToFireTime * 1.5f + s.equipTime * 0.5f + reload * 0.12f) / 2.2f);

            return r;
        }
    }
}
