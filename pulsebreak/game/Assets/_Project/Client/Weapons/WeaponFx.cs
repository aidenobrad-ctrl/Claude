using System;
using UnityEngine;
using UnityEngine.Rendering;
using Pulsebreak.Client.Player;
using Pulsebreak.Client.Visuals;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;
using Random = UnityEngine.Random;

namespace Pulsebreak.Client.Weapons
{
    /// <summary>
    /// Everything a fight looks like apart from the bodies: muzzle flashes (ours and other
    /// players'), tracers, bullet holes with sparks and dust, bursts of energy where a shot
    /// lands on a player, projectiles and their trails, explosions with fire, smoke, a shock
    /// ring and a scorch mark, and the flashes when a player is taken out or arrives.
    ///
    /// Everything is made up front and reused, so firing allocates nothing. Particles come
    /// from a few shared systems; flashes, holes and rings from small pools.
    /// </summary>
    public sealed class WeaponFx : MonoBehaviour
    {
        const int TracerCount = 40;
        const int ProjectileCount = 32;
        const int WorldFlashCount = 10;
        const float TracerSpeed = 340f;
        const float TracerLength = 6f;
        const float MinTracerDistance = 2.5f;
        const float FlashTime = 0.04f;
        /// <summary>How long a projectile takes to drift from the muzzle onto its true path.</summary>
        const float MuzzleBlendTime = 0.15f;

        // Colours stay at or below 1: the additive material lifts them into glowing (FxResources.Glow).
        static readonly Color BulletTracer = new Color(1f, 0.82f, 0.5f);
        static readonly Color FlashColor = new Color(1f, 0.72f, 0.35f);
        static readonly Color SparkColor = new Color(1f, 0.72f, 0.38f);
        static readonly Color DustColor = new Color(0.62f, 0.6f, 0.56f, 0.55f);

        /// <summary>An explosion went off: where, and how big. The camera shakes if we are near.</summary>
        public event Action<Vector3, float> Exploded;

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
            public TrailRenderer Trail;
            public int Id;
            public bool Active, Seen;
            public Vector3 MuzzleOffset;
            public float Age;
        }

        struct WorldFlash
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public float Timer;
        }

        readonly Tracer[] _tracers = new Tracer[TracerCount];
        readonly ProjectileVisual[] _projectiles = new ProjectileVisual[ProjectileCount];
        readonly WorldFlash[] _worldFlashes = new WorldFlash[WorldFlashCount];
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        int _nextTracer, _nextWorldFlash;

        WeaponViewModel _viewModel;
        LocalPlayerController _player;
        GameObject _pool;
        FxParticles _sparks, _dust, _smoke, _fire, _energy;
        FxQuads _holes, _scorches, _rings;
        FxLights _lights;
        Material _tracerMaterial, _energyTracerMaterial;
        GameObject _flash;
        MeshRenderer _flashRenderer;
        Light _flashLight;
        float _flashTimer;
        static Mesh _flashMesh;

        public void Initialize(WeaponViewModel viewModel, LocalPlayerController player)
        {
            _viewModel = viewModel;
            _player = player;

            Transform pool = new GameObject("Effects").transform;
            _pool = pool.gameObject;

            Material additiveSpark = FxResources.Additive(FxResources.Spark);
            Material additiveGlow = FxResources.Additive(FxResources.Soft);
            Material alphaSmoke = FxResources.Alpha(FxResources.Smoke);

            _sparks = new FxParticles(pool, "Sparks", additiveSpark, stretch: true, gravity: 1.2f, drag: 0.6f, maxParticles: 800,
                FxParticles.Fade(Color.white, new Color(1f, 0.45f, 0.15f), 0.2f), FxParticles.Curve(1f, 0.6f));
            _dust = new FxParticles(pool, "Dust", alphaSmoke, stretch: false, gravity: -0.03f, drag: 2.5f, maxParticles: 300,
                SmokeFade(), FxParticles.Curve(0.6f, 1.5f));
            _smoke = new FxParticles(pool, "Smoke", alphaSmoke, stretch: false, gravity: -0.06f, drag: 1.2f, maxParticles: 300,
                SmokeFade(), FxParticles.Curve(0.5f, 1.4f, 2f));
            _fire = new FxParticles(pool, "Fire", additiveGlow, stretch: false, gravity: -0.15f, drag: 3.5f, maxParticles: 300,
                FireFade(), FxParticles.Curve(0.6f, 1.2f, 0.9f));
            _energy = new FxParticles(pool, "Energy", additiveGlow, stretch: false, gravity: 0f, drag: 4f, maxParticles: 800,
                FxParticles.Fade(Color.white, Color.white, 0.3f), FxParticles.Curve(1f, 0.3f));

            _holes = new FxQuads(pool, "Bullet Hole", FxResources.Alpha(FxResources.Hole), 96);
            _scorches = new FxQuads(pool, "Scorch", FxResources.Alpha(FxResources.Scorch), 12);
            _rings = new FxQuads(pool, "Ring", FxResources.Additive(FxResources.Ring), 8);
            _lights = new FxLights(pool, 6);

            _tracerMaterial = FxResources.Additive(FxResources.Spark);
            _energyTracerMaterial = FxResources.Additive(FxResources.Soft);
            for (int i = 0; i < TracerCount; i++)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(pool, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.startWidth = 0.014f;
                line.endWidth = 0.032f;
                line.textureMode = LineTextureMode.Stretch;
                line.sharedMaterial = _tracerMaterial;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.enabled = false;
                _tracers[i].Line = line;
            }

            for (int i = 0; i < ProjectileCount; i++)
            {
                GameObject body = GreyboxKit.Visual(pool, "Projectile", PrimitiveType.Sphere,
                    Vector3.zero, Vector3.one * 0.1f, GreyboxMaterials.Unlit(BulletTracer), castShadows: false);
                var trail = body.AddComponent<TrailRenderer>();
                trail.emitting = false;
                trail.textureMode = LineTextureMode.Stretch;
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.minVertexDistance = 0.15f;
                trail.autodestruct = false;
                body.SetActive(false);
                _projectiles[i].Transform = body.transform;
                _projectiles[i].Renderer = body.GetComponent<Renderer>();
                _projectiles[i].Trail = trail;
            }

            Material[] flashMaterials = { additiveSpark, FxResources.Additive(FxResources.Star) };
            for (int i = 0; i < WorldFlashCount; i++)
            {
                var go = new GameObject("Muzzle Flash");
                go.transform.SetParent(pool, false);
                go.AddComponent<MeshFilter>().sharedMesh = FlashMesh();
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = flashMaterials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                go.SetActive(false);
                _worldFlashes[i].Transform = go.transform;
                _worldFlashes[i].Renderer = renderer;
            }

            // Our own flash rides on the weapon in our hands.
            _flash = new GameObject("Muzzle Flash");
            _flash.transform.SetParent(viewModel.Muzzle, false);
            _flash.AddComponent<MeshFilter>().sharedMesh = FlashMesh();
            _flashRenderer = _flash.AddComponent<MeshRenderer>();
            _flashRenderer.sharedMaterials = flashMaterials;
            _flashRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _flashRenderer.receiveShadows = false;
            _flash.SetActive(false);

            var lightObject = new GameObject("Muzzle Light");
            lightObject.transform.SetParent(viewModel.Muzzle, false);
            _flashLight = lightObject.AddComponent<Light>();
            _flashLight.type = LightType.Point;
            _flashLight.color = new Color(1f, 0.72f, 0.4f);
            _flashLight.range = 7f;
            _flashLight.intensity = 3.5f;
            _flashLight.shadows = LightShadows.None;
            _flashLight.enabled = false;
        }

        void OnDestroy()
        {
            // The pool is not parented to anything, so it has to be taken down by hand.
            if (_pool != null) Destroy(_pool);
        }

        // ------------------------------------------------------------------ shots

        /// <summary>Muzzle flash, once per shot. Only for weapons that go bang: not suppressed ones, blades, bows or thrown knives.</summary>
        public void PlayMuzzle(WeaponBuild build)
        {
            if (!Flashes(build)) return;
            _flashTimer = FlashTime;
            _flash.SetActive(true);
            float size = Random.Range(0.8f, 1.25f);
            _flash.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            _flash.transform.localScale = new Vector3(size, size, size * Random.Range(0.8f, 1.3f)) * Scale(build);
            Tint(_flashRenderer, FlashTint(build));
            _flashLight.color = LightColor(build);
            _flashLight.enabled = true;

            Vector3 at = _viewModel.Muzzle.position;
            Vector3 forward = _viewModel.Muzzle.forward;
            for (int i = 0; i < 3; i++)
                _sparks.Emit(at, (forward + Random.insideUnitSphere * 0.35f).normalized * Random.Range(4f, 9f), Random.Range(0.006f, 0.012f), Random.Range(0.05f, 0.12f), SparkColor);
            if (Random.value < 0.35f)
                _smoke.Emit(at + forward * 0.05f, forward * 0.4f + Vector3.up * 0.2f, Random.Range(0.06f, 0.1f), Random.Range(0.4f, 0.7f), new Color(0.8f, 0.8f, 0.8f, 0.18f));
        }

        /// <summary>Another player's muzzle flash, out in the world.</summary>
        public void PlayRemoteMuzzle(Vector3 muzzle, Vector3 direction, WeaponBuild build)
        {
            if (!Flashes(build) || direction.sqrMagnitude < 0.0001f) return;
            ref WorldFlash flash = ref _worldFlashes[_nextWorldFlash];
            _nextWorldFlash = (_nextWorldFlash + 1) % WorldFlashCount;
            flash.Transform.SetPositionAndRotation(muzzle, Quaternion.LookRotation(direction) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
            flash.Transform.localScale = Vector3.one * Random.Range(1.1f, 1.5f) * Scale(build);
            Tint(flash.Renderer, FlashTint(build));
            flash.Timer = FlashTime * 1.4f;
            flash.Transform.gameObject.SetActive(true);
            _lights.Flash(muzzle, LightColor(build), 2.5f, 6f, 0.06f);
        }

        /// <summary>A tracer and impact for one bullet or pellet.</summary>
        public void PlayTracer(in ShotPresentation shot) => PlayTracer(shot, _viewModel.Muzzle.position);

        /// <summary>A tracer drawn from somewhere other than our own muzzle: another player's shot.</summary>
        public void PlayTracer(in ShotPresentation shot, Vector3 from)
        {
            // The tracer leaves the visible muzzle but ends where the bullet actually went.
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
                bool energy = shot.Build != null && shot.Build.Weapon.audio.kind == ShotSoundKind.Energy;
                tracer.Line.sharedMaterial = energy ? _energyTracerMaterial : _tracerMaterial;
                Color color = energy ? Lighten(shot.Build.Weapon.view.accentColor, 0.3f) : BulletTracer;
                tracer.Line.startColor = new Color(color.r, color.g, color.b, 0.15f);
                tracer.Line.endColor = new Color(color.r, color.g, color.b, 1f);
                tracer.Line.enabled = true;
                UpdateTracer(ref tracer, 0f);
            }

            if (shot.HitWorld) PlayImpact(shot.End, shot.Normal, shot.End - shot.Origin);
        }

        /// <summary>A bullet hitting level geometry: a hole, a few sparks, a puff of dust.</summary>
        public void PlayImpact(Vector3 point, Vector3 normal, Vector3 travel)
        {
            if (normal.sqrMagnitude < 0.0001f) return;
            _holes.Place(point, normal, Random.Range(0.06f, 0.09f), Color.white);
            Vector3 bounce = Vector3.Reflect(travel.normalized, normal);
            int sparks = Random.Range(4, 8);
            for (int i = 0; i < sparks; i++)
                _sparks.Emit(point + normal * 0.01f, (bounce * 0.6f + normal * 0.5f + Random.insideUnitSphere * 0.6f).normalized * Random.Range(2f, 7f),
                    Random.Range(0.008f, 0.014f), Random.Range(0.12f, 0.3f), SparkColor);
            for (int i = 0; i < 2; i++)
                _dust.Emit(point + normal * 0.03f, normal * Random.Range(0.4f, 1f) + Random.insideUnitSphere * 0.3f,
                    Random.Range(0.12f, 0.22f), Random.Range(0.5f, 0.9f), DustColor);
        }

        /// <summary>A shot landing on a player: a burst of energy in their colour. No blood.</summary>
        public void PlayBodyHit(Vector3 point, Vector3 shotDirection, Color color, bool head)
        {
            Vector3 along = shotDirection.sqrMagnitude > 0.0001f ? shotDirection.normalized : Vector3.up;
            Color bright = Lighten(color, 0.35f);
            int count = head ? 22 : 12;
            for (int i = 0; i < count; i++)
            {
                Vector3 velocity = (along * 0.5f + Random.onUnitSphere).normalized * Random.Range(2f, head ? 7f : 5f);
                _energy.Emit(point, velocity, Random.Range(0.03f, 0.06f), Random.Range(0.18f, 0.38f), bright);
            }
            _energy.Emit(point, Vector3.zero, head ? 0.5f : 0.3f, 0.09f, bright);
        }

        // ------------------------------------------------------------------ players

        /// <summary>A player taken out: their body comes apart into light.</summary>
        public void PlayDerez(Vector3 feet, float height, Color suit, Color trim)
        {
            Color a = Lighten(suit, 0.25f);
            Color b = Lighten(trim, 0.35f);
            for (int i = 0; i < 56; i++)
            {
                Vector2 around = Random.insideUnitCircle * 0.28f;
                Vector3 at = feet + new Vector3(around.x, Random.Range(0.1f, height), around.y);
                Vector3 velocity = new Vector3(around.x * 2.5f, Random.Range(0.6f, 2.4f), around.y * 2.5f);
                _energy.Emit(at, velocity, Random.Range(0.05f, 0.11f), Random.Range(0.5f, 1.1f), i % 3 == 0 ? b : a);
            }
            _energy.Emit(feet + Vector3.up * height * 0.6f, Vector3.zero, 1.1f, 0.15f, b);
            _lights.Flash(feet + Vector3.up * height * 0.6f, trim, 3f, 5f, 0.3f);
            _rings.Place(feet + Vector3.up * 0.02f, Vector3.up, 0.4f, Hdr(b, 0.8f), 0.45f, 0.3f, 2.2f);
        }

        /// <summary>A player arriving: light rising from a ring at their feet.</summary>
        public void PlaySpawn(Vector3 feet, Color color)
        {
            Color bright = Lighten(color, 0.3f);
            for (int i = 0; i < 32; i++)
            {
                float angle = i / 32f * Mathf.PI * 2f;
                Vector3 at = feet + new Vector3(Mathf.Cos(angle) * 0.55f, 0.05f, Mathf.Sin(angle) * 0.55f);
                _energy.Emit(at, Vector3.up * Random.Range(1.5f, 3.2f), Random.Range(0.04f, 0.07f), Random.Range(0.35f, 0.6f), bright);
            }
            _rings.Place(feet + Vector3.up * 0.02f, Vector3.up, 1.6f, Hdr(bright, 0.9f), 0.6f, 0.2f, 0.4f);
            _lights.Flash(feet + Vector3.up, color, 2f, 4f, 0.4f);
        }

        // ------------------------------------------------------------------ projectiles

        /// <summary>Starts drawing a projectile the simulation has just launched.</summary>
        public void TrackProjectile(int id, WeaponStats stats, Color bodyColor, Color accentColor) =>
            TrackProjectile(id, stats, bodyColor, accentColor, fromOwnMuzzle: true);

        /// <param name="fromOwnMuzzle">False for a projectile someone else launched: it is drawn on its real path from the start.</param>
        public void TrackProjectile(int id, WeaponStats stats, Color bodyColor, Color accentColor, bool fromOwnMuzzle)
        {
            for (int i = 0; i < ProjectileCount; i++)
            {
                ref ProjectileVisual visual = ref _projectiles[i];
                if (visual.Active) continue;

                Vector3 size;
                Material material;
                TrailRenderer trail = visual.Trail;
                trail.emitting = false;
                if (stats.explosionRadius > 0f && stats.fuseTime > 0f)
                {
                    // A grenade: round, with a faint streak behind it.
                    size = Vector3.one * 0.13f;
                    material = GreyboxMaterials.Unlit(accentColor);
                    SetTrail(trail, FxResources.Additive(FxResources.Soft), accentColor, 0.04f, 0f, 0.25f);
                }
                else if (stats.explosionRadius > 0f)
                {
                    // A rocket: long, trailing smoke.
                    size = new Vector3(0.09f, 0.09f, 0.36f);
                    material = GreyboxMaterials.Unlit(accentColor);
                    SetTrail(trail, FxResources.Alpha(FxResources.Smoke), new Color(0.75f, 0.74f, 0.72f, 0.55f), 0.12f, 0.5f, 0.9f);
                }
                else if (stats.projectileGravity > 0f)
                {
                    // Bolts and blades.
                    size = new Vector3(0.016f, 0.034f, 0.3f);
                    material = GreyboxMaterials.Flat(bodyColor);
                }
                else
                {
                    // Energy shots: a glowing streak.
                    float width = Mathf.Max(0.05f, stats.projectileRadius * 2f);
                    size = new Vector3(width, width, 0.42f);
                    material = GreyboxMaterials.Unlit(accentColor);
                    SetTrail(trail, FxResources.Additive(FxResources.Soft), Lighten(accentColor, 0.2f), width * 1.2f, 0f, 0.12f);
                }

                visual.Id = id;
                visual.Active = true;
                visual.Seen = true;
                visual.Age = 0f;
                visual.Renderer.sharedMaterial = material;
                visual.Transform.localScale = size;
                // Starts at the muzzle and is updated onto its real path in LateUpdate.
                visual.MuzzleOffset = fromOwnMuzzle ? _viewModel.Muzzle.position - _player.MotorState.EyePosition : Vector3.zero;
                visual.Transform.position = fromOwnMuzzle ? _viewModel.Muzzle.position : new Vector3(0f, -1000f, 0f);
                visual.Transform.gameObject.SetActive(true);
                trail.Clear();
                return;
            }
        }

        public void OnProjectileEvent(ProjectileEvent projectileEvent)
        {
            switch (projectileEvent.Kind)
            {
                case ProjectileEventKind.Explosion:
                    PlayExplosion(projectileEvent.Point, projectileEvent.Normal, projectileEvent.Radius);
                    break;
                case ProjectileEventKind.Impact:
                    if (projectileEvent.HitTarget) PlayBodyHit(projectileEvent.Point, Vector3.up, Color.white, false);
                    else PlayImpact(projectileEvent.Point, projectileEvent.Normal, -projectileEvent.Normal);
                    break;
            }
        }

        /// <summary>Fire, smoke, sparks, a shock ring, a scorch mark and a flash of light.</summary>
        public void PlayExplosion(Vector3 point, Vector3 normal, float radius)
        {
            radius = Mathf.Max(radius, 1f);
            _lights.Flash(point + Vector3.up * 0.3f, new Color(1f, 0.65f, 0.3f), 9f, radius * 3.5f, 0.35f);

            for (int i = 0; i < 18; i++)
                _fire.Emit(point + Random.insideUnitSphere * radius * 0.2f, Random.onUnitSphere * Random.Range(1.5f, 5f) + Vector3.up * 1.2f,
                    Random.Range(0.5f, 0.9f) * radius * 0.6f, Random.Range(0.3f, 0.6f), Color.white);
            for (int i = 0; i < 12; i++)
                _smoke.Emit(point + Random.insideUnitSphere * radius * 0.3f, Random.onUnitSphere * 1.2f + Vector3.up * Random.Range(0.6f, 1.4f),
                    Random.Range(0.9f, 1.4f) * radius * 0.5f, Random.Range(1.6f, 2.8f), new Color(0.32f, 0.31f, 0.3f, 0.6f));
            for (int i = 0; i < 28; i++)
                _sparks.Emit(point, (Random.onUnitSphere + Vector3.up * 0.4f).normalized * Random.Range(7f, 17f),
                    Random.Range(0.012f, 0.025f), Random.Range(0.3f, 0.8f), SparkColor);

            // Lie the ring and the scorch on whatever it went off against, or the floor below.
            Vector3 surface = normal;
            Vector3 at = point;
            if (surface.sqrMagnitude < 0.0001f && Physics.Raycast(point + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 1.6f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore))
            {
                surface = hit.normal;
                at = hit.point;
            }
            if (surface.sqrMagnitude > 0.0001f)
            {
                _scorches.Place(at, surface, radius * 0.9f, Color.white, 30f, 0.6f);
                _rings.Place(at + surface * 0.05f, surface, 0.3f, Hdr(new Color(1f, 0.75f, 0.45f), 0.9f), 0.32f, 0.15f, radius * 2.4f);
            }
            Exploded?.Invoke(point, radius);
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

            for (int i = 0; i < WorldFlashCount; i++)
            {
                ref WorldFlash flash = ref _worldFlashes[i];
                if (flash.Timer <= 0f) continue;
                flash.Timer -= dt;
                if (flash.Timer <= 0f) flash.Transform.gameObject.SetActive(false);
            }

            for (int i = 0; i < TracerCount; i++)
            {
                if (_tracers[i].Active) UpdateTracer(ref _tracers[i], dt);
            }

            _holes.Update(dt);
            _scorches.Update(dt);
            _rings.Update(dt);
            _lights.Update(dt);
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
                    // Start the trail once the projectile is on its real path, not from where it was parked.
                    if (!visual.Trail.emitting && visual.Trail.sharedMaterial != null && visual.Transform.position.y > -500f && visual.Age > 0f)
                    {
                        visual.Trail.Clear();
                        visual.Trail.emitting = visual.Trail.time > 0f;
                    }
                    break;
                }
            }

            // Anything the simulation no longer has is gone.
            for (int i = 0; i < ProjectileCount; i++)
            {
                ref ProjectileVisual visual = ref _projectiles[i];
                if (!visual.Active || visual.Seen) continue;
                visual.Active = false;
                visual.Trail.emitting = false;
                visual.Trail.Clear();
                visual.Transform.gameObject.SetActive(false);
            }
        }

        /// <summary>Takes down everything still showing, for a new round.</summary>
        public void ClearWorld()
        {
            _holes.HideAll();
            _scorches.HideAll();
            _rings.HideAll();
            _sparks.Clear();
            _dust.Clear();
            _smoke.Clear();
            _fire.Clear();
            _energy.Clear();
        }

        // ------------------------------------------------------------------ helpers

        static bool Flashes(WeaponBuild build)
        {
            ShotSoundKind kind = build.Weapon.audio.kind;
            bool bang = kind == ShotSoundKind.Ballistic || kind == ShotSoundKind.Energy || kind == ShotSoundKind.Thump;
            return bang && !build.Suppressed && build.Weapon.stats.ballistics != BallisticsType.Melee;
        }

        static float Scale(WeaponBuild build) => Mathf.Lerp(0.8f, 1.5f, Mathf.Clamp01(build.Weapon.audio.weight));

        static Color FlashTint(WeaponBuild build)
        {
            Color color = build.Weapon.audio.kind == ShotSoundKind.Energy ? Lighten(build.Weapon.view.accentColor, 0.2f) : FlashColor;
            return Hdr(color, 1f) * 1.2f;
        }

        static Color LightColor(WeaponBuild build) =>
            build.Weapon.audio.kind == ShotSoundKind.Energy ? build.Weapon.view.accentColor : FlashColor;

        /// <summary>A colour moved towards white, for things that glow.</summary>
        static Color Lighten(Color color, float amount)
        {
            Color c = Color.Lerp(color, Color.white, amount);
            c.a = 1f;
            return c;
        }

        /// <summary>A tint for a renderer's own colour, which replaces the material's glow, so it carries the glow itself.</summary>
        static Color Hdr(Color color, float alpha) =>
            new Color(color.r * FxResources.Glow, color.g * FxResources.Glow, color.b * FxResources.Glow, alpha);

        void Tint(Renderer renderer, Color color)
        {
            _block.SetColor(FxResources.ColorProperty, color);
            renderer.SetPropertyBlock(_block);
        }

        static void SetTrail(TrailRenderer trail, Material material, Color color, float startWidth, float endWidth, float time)
        {
            trail.sharedMaterial = material;
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
            trail.startWidth = startWidth;
            trail.endWidth = endWidth;
            trail.time = time;
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

        static Gradient SmokeFade()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }

        static Gradient FireFade()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.95f, 0.8f), 0f),
                    new GradientColorKey(new Color(1f, 0.6f, 0.2f), 0.25f),
                    new GradientColorKey(new Color(0.8f, 0.22f, 0.06f), 0.6f),
                    new GradientColorKey(new Color(0.15f, 0.05f, 0.02f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.5f), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }

        /// <summary>
        /// A muzzle flash: two crossed flames along the barrel (+Z) and a star facing forward.
        /// Two parts, for the flame and star materials.
        /// </summary>
        static Mesh FlashMesh()
        {
            if (_flashMesh != null) return _flashMesh;
            const float length = 0.26f, width = 0.07f, star = 0.09f;
            var mesh = new Mesh { name = "Muzzle Flash" };
            mesh.vertices = new[]
            {
                new Vector3(0f, -width, 0f), new Vector3(0f, width, 0f), new Vector3(0f, width, length), new Vector3(0f, -width, length),
                new Vector3(-width, 0f, 0f), new Vector3(width, 0f, 0f), new Vector3(width, 0f, length), new Vector3(-width, 0f, length),
                new Vector3(-star, -star, 0.01f), new Vector3(star, -star, 0.01f), new Vector3(star, star, 0.01f), new Vector3(-star, star, 0.01f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            };
            var white = new Color32(255, 255, 255, 255);
            mesh.colors32 = new[] { white, white, white, white, white, white, white, white, white, white, white, white };
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 }, 0);
            mesh.SetTriangles(new[] { 8, 9, 10, 8, 10, 11 }, 1);
            mesh.RecalculateBounds();
            _flashMesh = mesh;
            return mesh;
        }
    }
}
