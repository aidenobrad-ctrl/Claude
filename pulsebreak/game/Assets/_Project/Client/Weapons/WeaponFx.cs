using UnityEngine;
using UnityEngine.Rendering;
using Pulsebreak.Client.Player;
using Pulsebreak.Client.Visuals;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.Weapons
{
    /// <summary>
    /// Shot visuals: muzzle flash, tracers, impact marks, projectiles in flight and explosions.
    /// Everything is pooled up front, so firing allocates nothing.
    /// </summary>
    public sealed class WeaponFx : MonoBehaviour
    {
        const int TracerCount = 32;
        const int ImpactCount = 64;
        const int ProjectileCount = 32;
        const int ExplosionCount = 6;
        const float TracerSpeed = 320f;
        const float TracerLength = 7f;
        const float MinTracerDistance = 2.5f;
        const float FlashTime = 0.035f;
        const float ExplosionTime = 0.28f;
        /// <summary>How long a projectile takes to drift from the muzzle onto its true path.</summary>
        const float MuzzleBlendTime = 0.15f;

        static readonly Color BulletTracer = new Color(1f, 0.86f, 0.45f);

        struct Tracer
        {
            public LineRenderer Line;
            public Vector3 From, Direction;
            public float Length, Travelled;
            public bool Active;
        }

        struct ProjectileVisual
        {
            public Transform Transform;
            public Renderer Renderer;
            public int Id;
            public bool Active, Seen;
            public Vector3 MuzzleOffset;
            public float Age;
        }

        struct Explosion
        {
            public Transform Transform;
            public float Radius, Age;
            public bool Active;
        }

        readonly Tracer[] _tracers = new Tracer[TracerCount];
        readonly Transform[] _impacts = new Transform[ImpactCount];
        readonly ProjectileVisual[] _projectiles = new ProjectileVisual[ProjectileCount];
        readonly Explosion[] _explosions = new Explosion[ExplosionCount];
        int _nextTracer, _nextImpact, _nextExplosion;

        WeaponViewModel _viewModel;
        LocalPlayerController _player;
        GameObject _flash;
        Light _flashLight;
        float _flashTimer;

        public void Initialize(WeaponViewModel viewModel, LocalPlayerController player)
        {
            _viewModel = viewModel;
            _player = player;

            var pool = new GameObject("WeaponFx Pool").transform;
            Material impactMaterial = GreyboxMaterials.Flat(new Color(0.07f, 0.07f, 0.08f));

            for (int i = 0; i < TracerCount; i++)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(pool, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.startWidth = 0.012f;
                line.endWidth = 0.03f;
                line.sharedMaterial = GreyboxMaterials.Unlit(BulletTracer);
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.enabled = false;
                _tracers[i].Line = line;
            }

            for (int i = 0; i < ImpactCount; i++)
            {
                GameObject mark = GreyboxKit.Visual(pool, "Impact", PrimitiveType.Sphere,
                    Vector3.zero, new Vector3(0.07f, 0.07f, 0.012f), impactMaterial, castShadows: false);
                mark.SetActive(false);
                _impacts[i] = mark.transform;
            }

            for (int i = 0; i < ProjectileCount; i++)
            {
                GameObject body = GreyboxKit.Visual(pool, "Projectile", PrimitiveType.Sphere,
                    Vector3.zero, Vector3.one * 0.1f, GreyboxMaterials.Unlit(BulletTracer), castShadows: false);
                body.SetActive(false);
                _projectiles[i].Transform = body.transform;
                _projectiles[i].Renderer = body.GetComponent<Renderer>();
            }

            Material blast = GreyboxMaterials.Unlit(new Color(1f, 0.62f, 0.2f));
            for (int i = 0; i < ExplosionCount; i++)
            {
                GameObject ball = GreyboxKit.Visual(pool, "Explosion", PrimitiveType.Sphere,
                    Vector3.zero, Vector3.one, blast, castShadows: false);
                ball.SetActive(false);
                _explosions[i].Transform = ball.transform;
            }

            _flash = GreyboxKit.Visual(viewModel.Muzzle, "MuzzleFlash", PrimitiveType.Sphere,
                new Vector3(0f, 0f, 0.04f), new Vector3(0.05f, 0.05f, 0.11f),
                GreyboxMaterials.Unlit(new Color(1f, 0.8f, 0.35f)), castShadows: false);
            _flash.SetActive(false);

            var lightObject = new GameObject("MuzzleLight");
            lightObject.transform.SetParent(viewModel.Muzzle, false);
            _flashLight = lightObject.AddComponent<Light>();
            _flashLight.type = LightType.Point;
            _flashLight.color = new Color(1f, 0.75f, 0.4f);
            _flashLight.range = 6f;
            _flashLight.intensity = 3f;
            _flashLight.shadows = LightShadows.None;
            _flashLight.enabled = false;
        }

        // ------------------------------------------------------------------ triggers

        /// <summary>Muzzle flash, once per shot. Only for weapons that go bang: not suppressed ones, blades, bows or thrown knives.</summary>
        public void PlayMuzzle(WeaponBuild build)
        {
            ShotSoundKind kind = build.Weapon.audio.kind;
            bool flashes = kind == ShotSoundKind.Ballistic || kind == ShotSoundKind.Energy || kind == ShotSoundKind.Thump;
            if (!flashes || build.Suppressed || build.Weapon.stats.ballistics == BallisticsType.Melee) return;

            _flashTimer = FlashTime;
            _flash.SetActive(true);
            _flash.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float size = Random.Range(0.8f, 1.25f);
            _flash.transform.localScale = new Vector3(0.05f * size, 0.05f * size, 0.11f * size);
            _flashLight.enabled = true;
        }

        /// <summary>A tracer and impact mark for one bullet or pellet.</summary>
        public void PlayTracer(in ShotPresentation shot)
        {
            // The tracer leaves the visible muzzle but ends where the bullet actually went.
            Vector3 from = _viewModel.Muzzle.position;
            Vector3 toEnd = shot.End - from;
            float distance = toEnd.magnitude;
            if (distance > MinTracerDistance)
            {
                ref Tracer tracer = ref _tracers[_nextTracer];
                _nextTracer = (_nextTracer + 1) % TracerCount;
                tracer.From = from;
                tracer.Direction = toEnd / distance;
                tracer.Length = distance;
                tracer.Travelled = 0f;
                tracer.Active = true;
                tracer.Line.sharedMaterial = GreyboxMaterials.Unlit(TracerColor(shot.Build));
                tracer.Line.enabled = true;
                UpdateTracer(ref tracer, 0f);
            }

            if (shot.HitWorld) PlaceImpact(shot.End, shot.Normal);
        }

        /// <summary>Starts drawing a projectile the simulation has just launched.</summary>
        public void TrackProjectile(int id, WeaponBuild build)
        {
            for (int i = 0; i < ProjectileCount; i++)
            {
                ref ProjectileVisual visual = ref _projectiles[i];
                if (visual.Active) continue;

                WeaponStats stats = build.Weapon.stats;
                WeaponViewProfile view = build.Weapon.view;
                Vector3 size;
                Material material;
                if (stats.explosionRadius > 0f)
                {
                    // Grenades are round, rockets long.
                    size = stats.fuseTime > 0f ? Vector3.one * 0.13f : new Vector3(0.09f, 0.09f, 0.36f);
                    material = GreyboxMaterials.Unlit(view.accentColor);
                }
                else if (stats.projectileGravity > 0f)
                {
                    // Bolts and blades.
                    size = new Vector3(0.016f, 0.034f, 0.3f);
                    material = GreyboxMaterials.Flat(view.bodyColor);
                }
                else
                {
                    // Energy shots.
                    float width = Mathf.Max(0.05f, stats.projectileRadius * 2f);
                    size = new Vector3(width, width, 0.42f);
                    material = GreyboxMaterials.Unlit(view.accentColor);
                }

                visual.Id = id;
                visual.Active = true;
                visual.Seen = true;
                visual.Age = 0f;
                visual.Renderer.sharedMaterial = material;
                visual.Transform.localScale = size;
                // Starts at the muzzle and is updated onto its real path in LateUpdate.
                visual.MuzzleOffset = _viewModel.Muzzle.position - _player.MotorState.EyePosition;
                visual.Transform.position = _viewModel.Muzzle.position;
                visual.Transform.gameObject.SetActive(true);
                return;
            }
        }

        public void OnProjectileEvent(ProjectileEvent projectileEvent)
        {
            switch (projectileEvent.Kind)
            {
                case ProjectileEventKind.Explosion:
                {
                    ref Explosion explosion = ref _explosions[_nextExplosion];
                    _nextExplosion = (_nextExplosion + 1) % ExplosionCount;
                    explosion.Radius = projectileEvent.Radius;
                    explosion.Age = 0f;
                    explosion.Active = true;
                    explosion.Transform.position = projectileEvent.Point;
                    explosion.Transform.localScale = Vector3.one * 0.3f;
                    explosion.Transform.gameObject.SetActive(true);
                    break;
                }
                case ProjectileEventKind.Impact:
                    if (!projectileEvent.HitTarget) PlaceImpact(projectileEvent.Point, projectileEvent.Normal);
                    break;
            }
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            float dt = Time.deltaTime;

            if (_flashTimer > 0f)
            {
                _flashTimer -= dt;
                if (_flashTimer <= 0f)
                {
                    _flash.SetActive(false);
                    _flashLight.enabled = false;
                }
            }

            for (int i = 0; i < TracerCount; i++)
            {
                if (_tracers[i].Active) UpdateTracer(ref _tracers[i], dt);
            }

            for (int i = 0; i < ExplosionCount; i++)
            {
                ref Explosion explosion = ref _explosions[i];
                if (!explosion.Active) continue;

                explosion.Age += dt;
                float t = explosion.Age / ExplosionTime;
                if (t >= 1f)
                {
                    explosion.Active = false;
                    explosion.Transform.gameObject.SetActive(false);
                    continue;
                }
                // Bursts out fast, then eases to full size.
                float grown = 1f - (1f - t) * (1f - t);
                explosion.Transform.localScale = Vector3.one * Mathf.Lerp(0.3f, explosion.Radius * 2f, grown);
            }
        }

        /// <summary>Runs after the player's update, so projectile positions use this frame's tick blend.</summary>
        void LateUpdate()
        {
            if (_player == null) return;

            for (int i = 0; i < ProjectileCount; i++) _projectiles[i].Seen = false;

            float alpha = _player.TickAlpha;
            var live = _player.Projectiles.Live;
            for (int n = 0; n < live.Count; n++)
            {
                Projectile projectile = live[n];
                for (int i = 0; i < ProjectileCount; i++)
                {
                    ref ProjectileVisual visual = ref _projectiles[i];
                    if (!visual.Active || visual.Id != projectile.Id) continue;

                    visual.Seen = true;
                    visual.Age += Time.deltaTime;
                    float settle = 1f - Mathf.Clamp01(visual.Age / MuzzleBlendTime);
                    Vector3 position = Vector3.Lerp(projectile.PreviousPosition, projectile.Position, alpha);
                    visual.Transform.position = position + visual.MuzzleOffset * settle;
                    if (projectile.Velocity.sqrMagnitude > 0.01f)
                        visual.Transform.rotation = Quaternion.LookRotation(projectile.Velocity);
                    break;
                }
            }

            // Anything the simulation no longer has is gone.
            for (int i = 0; i < ProjectileCount; i++)
            {
                ref ProjectileVisual visual = ref _projectiles[i];
                if (!visual.Active || visual.Seen) continue;
                visual.Active = false;
                visual.Transform.gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ helpers

        static Color TracerColor(WeaponBuild build) =>
            build != null && build.Weapon.audio.kind == ShotSoundKind.Energy ? build.Weapon.view.accentColor : BulletTracer;

        void PlaceImpact(Vector3 point, Vector3 normal)
        {
            if (normal.sqrMagnitude < 0.0001f) return;
            Transform mark = _impacts[_nextImpact];
            _nextImpact = (_nextImpact + 1) % ImpactCount;
            mark.SetPositionAndRotation(point + normal * 0.004f, Quaternion.LookRotation(normal));
            mark.gameObject.SetActive(true);
        }

        static void UpdateTracer(ref Tracer tracer, float dt)
        {
            tracer.Travelled += TracerSpeed * dt;
            float tail = Mathf.Max(0f, tracer.Travelled - TracerLength);
            if (tail >= tracer.Length)
            {
                tracer.Active = false;
                tracer.Line.enabled = false;
                return;
            }

            float head = Mathf.Min(tracer.Travelled, tracer.Length);
            tracer.Line.SetPosition(0, tracer.From + tracer.Direction * tail);
            tracer.Line.SetPosition(1, tracer.From + tracer.Direction * head);
        }
    }
}
