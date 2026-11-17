using UnityEngine;
using UnityEngine.Rendering;

namespace Pulsebreak.Client.Visuals
{
    /// <summary>
    /// A particle system set up once and then told where to put particles: sparks, smoke,
    /// fire. One system serves every effect of its kind at once, so a burst costs a few
    /// calls and no new objects.
    /// </summary>
    public sealed class FxParticles
    {
        public readonly ParticleSystem System;
        ParticleSystem.EmitParams _params;

        /// <param name="stretch">Draw each particle as a streak along its motion (sparks) rather than a facing square.</param>
        /// <param name="gravity">1 falls like a stone; negative rises.</param>
        /// <param name="drag">How quickly particles slow down.</param>
        /// <param name="fade">Colour and alpha over each particle's life, multiplied by its starting colour.</param>
        /// <param name="grow">Size over life, multiplied by its starting size.</param>
        public FxParticles(Transform parent, string name, Material material, bool stretch, float gravity, float drag,
            int maxParticles, Gradient fade, AnimationCurve grow)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            System = go.AddComponent<ParticleSystem>();
            System.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = System.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 10f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            main.startSize = 0.1f;
            main.gravityModifier = gravity;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            ParticleSystem.EmissionModule emission = System.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = System.shape;
            shape.enabled = false;

            ParticleSystem.ColorOverLifetimeModule color = System.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(fade);

            ParticleSystem.SizeOverLifetimeModule size = System.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, grow);

            if (drag > 0f)
            {
                ParticleSystem.LimitVelocityOverLifetimeModule limit = System.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.limit = new ParticleSystem.MinMaxCurve(1000f);
                limit.dampen = 0f;
                limit.drag = new ParticleSystem.MinMaxCurve(drag);
                limit.multiplyDragByParticleSize = false;
                limit.multiplyDragByParticleVelocity = false;
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            if (stretch)
            {
                renderer.velocityScale = 0.035f;
                renderer.lengthScale = 1.5f;
            }
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.maxParticleSize = 3f;
            renderer.sortMode = ParticleSystemSortMode.None;

            System.Play();
        }

        public void Emit(Vector3 position, Vector3 velocity, float size, float lifetime, Color color)
        {
            _params.position = position;
            _params.velocity = velocity;
            _params.startSize = size;
            _params.startLifetime = lifetime;
            _params.startColor = color;
            _params.rotation = Random.Range(0f, 360f);
            _params.applyShapeToPosition = false;
            System.Emit(_params, 1);
        }

        public void Clear() => System.Clear();

        /// <summary>Fades out to nothing over its life.</summary>
        public static Gradient Fade(Color start, Color end, float holdUntil = 0f)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, Mathf.Clamp01(holdUntil)), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }

        public static AnimationCurve Curve(params float[] valuesOverLife)
        {
            var keys = new Keyframe[valuesOverLife.Length];
            for (int i = 0; i < valuesOverLife.Length; i++)
                keys[i] = new Keyframe(valuesOverLife.Length == 1 ? 0f : i / (float)(valuesOverLife.Length - 1), valuesOverLife[i]);
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }
    }

    /// <summary>A handful of point lights lent out for flashes: muzzle fire, explosions.</summary>
    public sealed class FxLights
    {
        struct Slot
        {
            public Light Light;
            public float Intensity, Age, Duration;
        }

        readonly Slot[] _flashes;
        int _next;

        public FxLights(Transform parent, int count)
        {
            _flashes = new Slot[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Flash Light");
                go.transform.SetParent(parent, false);
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.enabled = false;
                _flashes[i].Light = light;
            }
        }

        public void Flash(Vector3 position, Color color, float intensity, float range, float duration)
        {
            ref Slot flash = ref _flashes[_next];
            _next = (_next + 1) % _flashes.Length;
            flash.Light.transform.position = position;
            flash.Light.color = color;
            flash.Light.range = range;
            flash.Light.intensity = intensity;
            flash.Light.enabled = true;
            flash.Intensity = intensity;
            flash.Age = 0f;
            flash.Duration = Mathf.Max(0.01f, duration);
        }

        public void Update(float deltaTime)
        {
            for (int i = 0; i < _flashes.Length; i++)
            {
                ref Slot flash = ref _flashes[i];
                if (!flash.Light.enabled) continue;
                flash.Age += deltaTime;
                float t = flash.Age / flash.Duration;
                if (t >= 1f)
                {
                    flash.Light.enabled = false;
                    continue;
                }
                flash.Light.intensity = flash.Intensity * (1f - t) * (1f - t);
            }
        }
    }

    /// <summary>
    /// Flat textured squares lent out and taken back in turn: bullet holes, scorch marks,
    /// shock rings. Each can fade and grow over its life; the oldest is reused when they run out.
    /// </summary>
    public sealed class FxQuads
    {
        struct Quad
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public Color Color;
            public float Age, Life, FadeFrom, StartSize, EndSize;
            public bool Active;
        }

        static Mesh _mesh;
        readonly Quad[] _quads;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        int _next;

        public FxQuads(Transform parent, string name, Material material, int count)
        {
            _quads = new Quad[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                go.SetActive(false);
                _quads[i].Transform = go.transform;
                _quads[i].Renderer = renderer;
            }
        }

        /// <summary>Lays one flat on a surface, facing out along <paramref name="normal"/>.</summary>
        /// <param name="life">Seconds before it is gone; 0 to stay until reused.</param>
        /// <param name="fadeFrom">The share of its life after which it starts to fade.</param>
        public void Place(Vector3 point, Vector3 normal, float size, Color color, float life = 0f, float fadeFrom = 0.7f, float endSize = -1f)
        {
            ref Quad quad = ref _quads[_next];
            _next = (_next + 1) % _quads.Length;
            Quaternion facing = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            quad.Transform.SetPositionAndRotation(point + normal * 0.006f, facing);
            quad.Transform.localScale = Vector3.one * size;
            quad.Color = color;
            quad.Age = 0f;
            quad.Life = life;
            quad.FadeFrom = fadeFrom;
            quad.StartSize = size;
            quad.EndSize = endSize < 0f ? size : endSize;
            quad.Active = true;
            SetColor(quad.Renderer, color);
            quad.Transform.gameObject.SetActive(true);
        }

        public void Update(float deltaTime)
        {
            for (int i = 0; i < _quads.Length; i++)
            {
                ref Quad quad = ref _quads[i];
                if (!quad.Active || quad.Life <= 0f) continue;
                quad.Age += deltaTime;
                float t = quad.Age / quad.Life;
                if (t >= 1f)
                {
                    quad.Active = false;
                    quad.Transform.gameObject.SetActive(false);
                    continue;
                }
                float grown = 1f - (1f - t) * (1f - t);
                quad.Transform.localScale = Vector3.one * Mathf.Lerp(quad.StartSize, quad.EndSize, grown);
                float fade = t <= quad.FadeFrom ? 1f : 1f - (t - quad.FadeFrom) / (1f - quad.FadeFrom);
                Color c = quad.Color;
                c.a *= fade;
                SetColor(quad.Renderer, c);
            }
        }

        public void HideAll()
        {
            for (int i = 0; i < _quads.Length; i++)
            {
                _quads[i].Active = false;
                _quads[i].Transform.gameObject.SetActive(false);
            }
        }

        void SetColor(MeshRenderer renderer, Color color)
        {
            _block.SetColor(FxResources.ColorProperty, color);
            renderer.SetPropertyBlock(_block);
        }

        /// <summary>A one-metre square in the XY plane, facing +Z.</summary>
        public static Mesh QuadMesh()
        {
            if (_mesh != null) return _mesh;
            _mesh = new Mesh { name = "Fx Quad" };
            _mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            _mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            _mesh.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            _mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            return _mesh;
        }
    }
}
