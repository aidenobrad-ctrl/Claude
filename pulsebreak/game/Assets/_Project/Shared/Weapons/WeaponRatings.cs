using System.Collections.Generic;
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

        public const int BarCount = 7;
        /// <summary>
        /// The same seven qualities as the bars, before squeezing into 0..1: bigger is better
        /// for every one. <see cref="WeaponRatingScale"/> turns them into bars relative to the
        /// other weapons a player could pick instead.
        /// </summary>
        public float[] Raw;

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
            float handlingCost = s.adsTime * 1.5f + s.sprintToFireTime * 1.5f + s.equipTime * 0.5f + reload * 0.12f;
            r.Handling = 1f - Mathf.Clamp01(handlingCost / 2.2f);

            // Damage and fire rate span more than tenfold across the roster, so they are compared
            // on a log scale: going from 20 to 40 damage looks as big a step as 60 to 120.
            r.Raw = new[]
            {
                Mathf.Log(Mathf.Max(1f, r.DamagePerShot)),
                Mathf.Log(Mathf.Max(1f, r.EffectiveRpm)),
                reach,
                -(s.hipSpread + 4f * s.adsSpread + 0.3f * s.maxBloom),
                -kick,
                s.hipMoveMultiplier * 0.6f + s.adsMoveMultiplier * 0.4f,
                -handlingCost,
            };
            return r;
        }
    }

    /// <summary>
    /// Turns ratings into bars that compare a weapon with the others in the same group, for
    /// example every primary weapon. The weakest in the group sits near the left, the strongest
    /// near the right, so the differences between weapons are as wide as the bar allows. A
    /// little room is left at the right for attachments that push a weapon past the best.
    /// </summary>
    public sealed class WeaponRatingScale
    {
        const float Floor = 0.06f, Top = 0.88f;

        readonly float[] _min = new float[WeaponRatings.BarCount];
        readonly float[] _max = new float[WeaponRatings.BarCount];

        public static WeaponRatingScale From(IEnumerable<WeaponStats> weapons)
        {
            var scale = new WeaponRatingScale();
            for (int i = 0; i < WeaponRatings.BarCount; i++)
            {
                scale._min[i] = float.MaxValue;
                scale._max[i] = float.MinValue;
            }
            foreach (WeaponStats stats in weapons)
            {
                if (stats == null) continue;
                float[] raw = WeaponRatings.From(stats).Raw;
                for (int i = 0; i < WeaponRatings.BarCount; i++)
                {
                    scale._min[i] = Mathf.Min(scale._min[i], raw[i]);
                    scale._max[i] = Mathf.Max(scale._max[i], raw[i]);
                }
            }
            return scale;
        }

        /// <summary>False when every weapon in the group is the same on this bar (accuracy among knives, say), so it says nothing.</summary>
        public bool Meaningful(int bar) => _max[bar] - _min[bar] > 0.0001f;

        /// <summary>0..1 for a bar, relative to the group.</summary>
        public float Bar(in WeaponRatings ratings, int bar)
        {
            if (!Meaningful(bar)) return 0.5f;
            float t = (ratings.Raw[bar] - _min[bar]) / (_max[bar] - _min[bar]);
            return Mathf.Clamp(Floor + (Top - Floor) * t, 0.02f, 1f);
        }
    }
}
