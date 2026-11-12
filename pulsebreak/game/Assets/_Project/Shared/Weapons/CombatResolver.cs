using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Shared.Weapons
{
    /// <summary>
    /// Turns shots into damage: bullets (with piercing), melee strikes and explosions. This is
    /// the authority step. Offline the local player calls it directly; online only the server
    /// will, while clients show predicted effects.
    /// </summary>
    public static class CombatResolver
    {
        const int MaxBulletSteps = 12;

        // Scratch buffers. The simulation is single-threaded, so sharing them is safe.
        static readonly Collider[] Overlaps = new Collider[64];
        static readonly List<IDamageable> Targets = new List<IDamageable>(8);
        static readonly List<Vector3> TargetPoints = new List<Vector3>(8);
        static readonly List<float> TargetDistances = new List<float>(8);

        /// <summary>
        /// Fires one bullet. It stops at level geometry, and passes through as many extra
        /// targets as the weapon's piercing allows, losing damage each time.
        /// </summary>
        /// <returns>Where the bullet ended up, for tracers and impact marks.</returns>
        /// <param name="attacker">Who fired. Their own body is never hit. May be null.</param>
        public static HitscanHit FireBullet(Vector3 origin, Vector3 direction, WeaponStats stats, string weaponId,
            float damageScale, int tick, List<HitReport> reports, IDamageable attacker = null)
        {
            Targets.Clear();
            if (attacker != null) Targets.Add(attacker);
            float travelled = 0f;
            int pierced = 0;
            HitscanHit last = default;

            for (int step = 0; step < MaxBulletSteps; step++)
            {
                float remaining = stats.maxRange - travelled;
                if (remaining <= 0f) break;

                HitscanHit hit = Hitscan.Trace(origin + direction * travelled, direction, remaining);
                hit.Distance += travelled;
                last = hit;
                if (!hit.HasHit || hit.Hitbox == null) break;

                // Several body parts belong to one target; only the first one struck counts.
                IDamageable target = hit.Hitbox.Owner;
                if (target != null && target.IsAlive && !Targets.Contains(target))
                {
                    Targets.Add(target);
                    if (ApplyHit(stats, weaponId, hit.Hitbox, hit.Point, direction, hit.Distance, damageScale, tick, out HitReport report, attacker))
                        reports.Add(report);

                    if (pierced >= stats.pierceTargets) break;
                    pierced++;
                    damageScale *= stats.pierceDamageKept;
                }

                travelled = hit.Distance + 0.02f;
            }

            return last;
        }

        /// <summary>Applies a direct hit on a body part, with range falloff and the body part's multiplier.</summary>
        public static bool ApplyHit(WeaponStats stats, string weaponId, Hitbox hitbox, Vector3 point, Vector3 direction,
            float distance, float damageScale, int tick, out HitReport report, IDamageable attacker = null)
        {
            report = default;
            IDamageable target = hitbox.Owner;
            if (target == null || !target.IsAlive || target == attacker) return false;

            float amount = DamageCalculator.Compute(stats, distance, hitbox.zone) * damageScale;
            report = Deliver(target, amount, hitbox.zone, point, direction, distance, weaponId, tick, attacker);
            return report.Damage > 0f || report.Killed;
        }

        /// <summary>
        /// A melee swing: strikes the nearest living target within reach in front of the
        /// attacker, unless a wall is in the way. Melee damage is flat.
        /// </summary>
        public static bool MeleeStrike(Vector3 origin, Vector3 direction, WeaponStats stats, string weaponId,
            int tick, out HitReport report, IDamageable attacker = null)
        {
            report = default;
            Vector3 end = origin + direction * stats.meleeRange;
            int count = Physics.OverlapCapsuleNonAlloc(origin, end, stats.meleeRadius, Overlaps,
                GameLayers.HitboxMask, QueryTriggerInteraction.Ignore);

            IDamageable best = null;
            Vector3 bestPoint = default;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (!Overlaps[i].TryGetComponent(out Hitbox hitbox)) continue;
                IDamageable target = hitbox.Owner;
                if (target == null || !target.IsAlive || target == attacker) continue;

                Vector3 point = Overlaps[i].ClosestPoint(origin);
                float distance = Vector3.Distance(origin, point);
                if (distance >= bestDistance) continue;
                if (Physics.Linecast(origin, point, GameLayers.WorldMask, QueryTriggerInteraction.Ignore)) continue;

                best = target;
                bestPoint = point;
                bestDistance = distance;
            }

            if (best == null) return false;
            report = Deliver(best, stats.damage, HitZone.Body, bestPoint, direction, bestDistance, weaponId, tick, attacker);
            return report.Damage > 0f || report.Killed;
        }

        /// <summary>
        /// An explosion: damages every living target within the radius that is not behind
        /// cover, once each, by how close its nearest body part is to the centre.
        /// </summary>
        /// <param name="attacker">Who caused it. They take no damage from their own blast. May be null.</param>
        public static void Explode(Vector3 centre, WeaponStats stats, string weaponId, float damageScale, int tick,
            List<HitReport> reports, IDamageable attacker = null)
        {
            if (stats.explosionRadius <= 0f) return;

            int count = Physics.OverlapSphereNonAlloc(centre, stats.explosionRadius, Overlaps,
                GameLayers.HitboxMask, QueryTriggerInteraction.Ignore);

            Targets.Clear();
            TargetPoints.Clear();
            TargetDistances.Clear();
            for (int i = 0; i < count; i++)
            {
                if (!Overlaps[i].TryGetComponent(out Hitbox hitbox)) continue;
                IDamageable target = hitbox.Owner;
                if (target == null || !target.IsAlive || target == attacker) continue;

                Vector3 point = Overlaps[i].ClosestPoint(centre);
                float distance = Vector3.Distance(centre, point);

                int known = Targets.IndexOf(target);
                if (known < 0)
                {
                    Targets.Add(target);
                    TargetPoints.Add(point);
                    TargetDistances.Add(distance);
                }
                else if (distance < TargetDistances[known])
                {
                    TargetPoints[known] = point;
                    TargetDistances[known] = distance;
                }
            }

            for (int i = 0; i < Targets.Count; i++)
            {
                Vector3 point = TargetPoints[i];
                if (Physics.Linecast(centre, point, GameLayers.WorldMask, QueryTriggerInteraction.Ignore)) continue;

                float amount = DamageCalculator.ExplosionDamage(stats, TargetDistances[i]) * damageScale;
                if (amount <= 0f) continue;

                Vector3 away = point - centre;
                Vector3 direction = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.up;
                HitReport report = Deliver(Targets[i], amount, HitZone.Body, point, direction, TargetDistances[i], weaponId, tick, attacker);
                if (report.Damage > 0f || report.Killed) reports.Add(report);
            }
        }

        static HitReport Deliver(IDamageable target, float amount, HitZone zone, Vector3 point, Vector3 direction,
            float distance, string weaponId, int tick, IDamageable attacker)
        {
            DamageResult result = target.ApplyDamage(new DamageInfo
            {
                Amount = DamageCalculator.Standardize(amount),
                Zone = zone,
                Point = point,
                Direction = direction,
                Distance = distance,
                WeaponId = weaponId,
                Attacker = attacker,
            });

            return new HitReport
            {
                Tick = tick,
                Point = point,
                Damage = result.Applied,
                Zone = zone,
                Killed = result.Killed,
                Distance = distance,
                Target = target,
                WeaponId = weaponId,
                Attacker = attacker,
            };
        }
    }
}
