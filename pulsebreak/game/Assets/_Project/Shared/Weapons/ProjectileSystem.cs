using System;
using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Shared.Weapons
{
    /// <summary>One projectile in flight.</summary>
    public struct Projectile
    {
        public int Id;
        public Vector3 Position;
        /// <summary>Where it was one tick ago, for blending its drawn position between ticks.</summary>
        public Vector3 PreviousPosition;
        public Vector3 Velocity;
        public float Age;
        public float Travelled;
        public int BouncesLeft;
        public float DamageScale;
        public WeaponStats Stats;
        public string WeaponId;
        /// <summary>Who launched it. It passes through their own body and never damages them. May be null.</summary>
        public IDamageable Owner;
        /// <summary>Visual-only systems: it has reached where it would end and is waiting to be told how.</summary>
        public bool Finished;
    }

    public enum ProjectileEventKind : byte
    {
        Bounce,
        /// <summary>Stopped on the level or on a target without exploding.</summary>
        Impact,
        Explosion,
        /// <summary>Ran out of lifetime in mid-air.</summary>
        Expired,
    }

    /// <summary>Something that happened to a projectile, for effects and sound.</summary>
    public struct ProjectileEvent
    {
        public ProjectileEventKind Kind;
        public int ProjectileId;
        public Vector3 Point;
        public Vector3 Normal;
        /// <summary>Explosion radius; 0 for everything else.</summary>
        public float Radius;
        public bool HitTarget;
        public string WeaponId;
    }

    /// <summary>
    /// Simulates every projectile in the world: rockets, grenades, bolts, energy shots, thrown
    /// knives. Like movement and weapons it advances in fixed ticks, so it can run on the
    /// server unchanged. Each tick a projectile is swept along its path, which means fast
    /// ones cannot skip through thin walls or targets.
    /// </summary>
    public sealed class ProjectileSystem
    {
        const int MaxSegmentsPerTick = 4;

        readonly List<Projectile> _live = new List<Projectile>(32);
        readonly List<HitReport> _reports = new List<HitReport>(8);
        int _nextId = 1;

        /// <summary>Presentation hook. Never drive gameplay from it.</summary>
        public event Action<ProjectileEvent> Happened;
        /// <summary>A projectile or its explosion damaged something.</summary>
        public event Action<HitReport> HitConfirmed;

        public IReadOnlyList<Projectile> Live => _live;

        /// <summary>
        /// A system that only draws. It flies projectiles against the level so they look
        /// right, but never touches a target, deals damage or decides how one ends: a client
        /// uses it to show projectiles the server is simulating, and removes each when the
        /// server says what became of it.
        /// </summary>
        public bool VisualOnly { get; set; }

        /// <param name="owner">Who launched it; may be null.</param>
        /// <param name="id">Leave at 0 to assign one. A visual-only system passes the server's id.</param>
        /// <returns>The new projectile's id.</returns>
        public int Spawn(Vector3 origin, Vector3 direction, WeaponStats stats, string weaponId, float damageScale,
            IDamageable owner = null, int id = 0)
        {
            if (id == 0) id = _nextId++;
            _live.Add(new Projectile
            {
                Id = id,
                Owner = owner,
                Position = origin,
                PreviousPosition = origin,
                Velocity = direction * stats.projectileSpeed,
                BouncesLeft = stats.bounces,
                DamageScale = damageScale,
                Stats = stats,
                WeaponId = weaponId,
            });
            return id;
        }

        public void Clear() => _live.Clear();

        /// <summary>Removes one projectile without any event. Returns where it was.</summary>
        public bool Remove(int id, out Vector3 lastPosition)
        {
            for (int i = 0; i < _live.Count; i++)
            {
                if (_live[i].Id != id) continue;
                lastPosition = _live[i].Position;
                _live.RemoveAt(i);
                return true;
            }
            lastPosition = default;
            return false;
        }

        /// <summary>
        /// Moves one projectile forward several ticks at once. For a visual-only system that
        /// heard about a projectile late and needs to draw it where it is by now.
        /// </summary>
        public void FastForward(int id, int ticks, float dt)
        {
            if (!VisualOnly) return;
            for (int i = 0; i < _live.Count; i++)
            {
                if (_live[i].Id != id) continue;
                Projectile projectile = _live[i];
                for (int t = 0; t < ticks; t++) Advance(ref projectile, 0, dt);
                projectile.PreviousPosition = projectile.Position;
                _live[i] = projectile;
                return;
            }
        }

        /// <summary>Advances every projectile by one tick.</summary>
        public void Step(int tick, float dt)
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Projectile projectile = _live[i];
                if (Advance(ref projectile, tick, dt)) _live[i] = projectile;
                else _live.RemoveAt(i);
            }
        }

        /// <returns>False once the projectile is finished.</returns>
        bool Advance(ref Projectile p, int tick, float dt)
        {
            WeaponStats stats = p.Stats;
            bool explosive = stats.explosionRadius > 0f;
            bool fused = stats.fuseTime > 0f;

            p.PreviousPosition = p.Position;
            p.Age += dt;
            if (p.Finished) return p.Age < stats.projectileLifetime + 2f;

            // Same integration as the character: average of start and end velocity.
            Vector3 startVelocity = p.Velocity;
            p.Velocity += Vector3.down * (stats.projectileGravity * dt);
            Vector3 displacement = (startVelocity + p.Velocity) * (0.5f * dt);

            for (int segment = 0; segment < MaxSegmentsPerTick; segment++)
            {
                float distance = displacement.magnitude;
                if (distance < 0.00001f) break;
                Vector3 direction = displacement / distance;

                if (!Sweep(p.Position, direction, distance, stats.projectileRadius, VisualOnly, out RaycastHit hit))
                {
                    p.Position += displacement;
                    p.Travelled += distance;
                    break;
                }

                p.Position += direction * hit.distance;
                p.Travelled += hit.distance;

                Hitbox hitbox = null;
                bool struckTarget = !VisualOnly && hit.collider.TryGetComponent(out hitbox);

                // It flies through whoever launched it.
                if (struckTarget && p.Owner != null && hitbox.Owner == p.Owner)
                {
                    p.Position += direction * 0.05f;
                    displacement = direction * Mathf.Max(0f, distance - hit.distance - 0.05f);
                    continue;
                }

                // A fused explosive can be set to bounce off targets and wait for its fuse.
                bool bouncesOffTargets = explosive && fused && !stats.detonateOnTarget;
                if (struckTarget && !bouncesOffTargets)
                {
                    if (explosive)
                    {
                        Detonate(ref p, hit.point + hit.normal * 0.05f, hit.normal, tick);
                    }
                    else
                    {
                        bool hitTarget = CombatResolver.ApplyHit(stats, p.WeaponId, hitbox, hit.point, direction,
                            p.Travelled, p.DamageScale, tick, out HitReport report, p.Owner);
                        if (hitTarget) HitConfirmed?.Invoke(report);
                        Raise(ProjectileEventKind.Impact, p, hit.point, hit.normal, 0f, hitTarget);
                    }
                    return false;
                }

                if (p.BouncesLeft > 0)
                {
                    p.BouncesLeft--;
                    p.DamageScale *= stats.bounceDamageMultiplier;
                    p.Velocity = Vector3.Reflect(p.Velocity, hit.normal) * stats.bounceSpeedKept;
                    displacement = Vector3.Reflect(direction, hit.normal) * ((distance - hit.distance) * stats.bounceSpeedKept);
                    // Lift off the surface so the next sweep does not start inside it.
                    p.Position += hit.normal * 0.01f;
                    Raise(ProjectileEventKind.Bounce, p, hit.point, hit.normal, 0f, false);
                    continue;
                }

                if (fused)
                {
                    // Out of bounces with the fuse still burning: lie where it landed.
                    p.Velocity = Vector3.zero;
                    break;
                }

                if (VisualOnly) return Finish(ref p);
                if (explosive) Detonate(ref p, hit.point + hit.normal * 0.05f, hit.normal, tick);
                else Raise(ProjectileEventKind.Impact, p, hit.point, hit.normal, 0f, false);
                return false;
            }

            if (VisualOnly) return (fused && p.Age >= stats.fuseTime) || p.Age >= stats.projectileLifetime ? Finish(ref p) : true;

            if (fused && p.Age >= stats.fuseTime)
            {
                Detonate(ref p, p.Position, Vector3.up, tick);
                return false;
            }

            if (p.Age >= stats.projectileLifetime)
            {
                if (explosive) Detonate(ref p, p.Position, Vector3.up, tick);
                else Raise(ProjectileEventKind.Expired, p, p.Position, Vector3.up, 0f, false);
                return false;
            }

            return true;
        }

        /// <summary>A visual-only projectile stops where it would end and waits to be removed.</summary>
        static bool Finish(ref Projectile p)
        {
            p.Finished = true;
            p.Velocity = Vector3.zero;
            return true;
        }

        static bool Sweep(Vector3 from, Vector3 direction, float distance, float radius, bool worldOnly, out RaycastHit hit)
        {
            int mask = worldOnly ? GameLayers.WorldMask : GameLayers.ShotMask;
            return radius > 0.001f
                ? Physics.SphereCast(from, radius, direction, out hit, distance, mask, QueryTriggerInteraction.Ignore)
                : Physics.Raycast(from, direction, out hit, distance, mask, QueryTriggerInteraction.Ignore);
        }

        void Detonate(ref Projectile p, Vector3 centre, Vector3 normal, int tick)
        {
            _reports.Clear();
            CombatResolver.Explode(centre, p.Stats, p.WeaponId, p.DamageScale, tick, _reports, p.Owner);
            Raise(ProjectileEventKind.Explosion, p, centre, normal, p.Stats.explosionRadius, _reports.Count > 0);
            for (int i = 0; i < _reports.Count; i++) HitConfirmed?.Invoke(_reports[i]);
        }

        void Raise(ProjectileEventKind kind, in Projectile p, Vector3 point, Vector3 normal, float radius, bool hitTarget)
        {
            Happened?.Invoke(new ProjectileEvent
            {
                Kind = kind,
                ProjectileId = p.Id,
                Point = point,
                Normal = normal,
                Radius = radius,
                HitTarget = hitTarget,
                WeaponId = p.WeaponId,
            });
        }
    }
}
