using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pulsebreak.Client.Visuals
{
    /// <summary>
    /// The textures and materials effects are drawn with. The textures are painted in code
    /// the first time they are needed (a soft glow, a spark streak, a puff of smoke, a star,
    /// a ring, a bullet hole, a scorch mark), so the project carries no image files for them.
    /// The materials use the game's own effect shader (Resources/Shaders/PulsebreakEffect).
    /// </summary>
    public static class FxResources
    {
        public const string ShaderResource = "Shaders/PulsebreakEffect";

        /// <summary>
        /// How much brighter than white additive effects are drawn. Particle and line colours
        /// are stored in eight bits, so they cannot go above 1 themselves; the material lifts
        /// them, so fire and sparks catch the bloom.
        /// </summary>
        public const float Glow = 2.5f;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");

        static Shader _shader;
        static bool _shaderChecked;
        static readonly Dictionary<long, Material> Materials = new Dictionary<long, Material>();
        static Texture2D _glow, _spark, _smoke, _star, _ring, _hole, _scorch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _shader = null;
            _shaderChecked = false;
            Materials.Clear();
            _glow = _spark = _smoke = _star = _ring = _hole = _scorch = null;
        }

        public static Texture2D Soft => _glow ? _glow : _glow = Paint("Fx Glow", 64, 64, (u, v) =>
        {
            float r = Radius(u, v);
            float a = Mathf.Clamp01(1f - r);
            return new Color(1f, 1f, 1f, a * a * a);
        });

        public static Texture2D Spark => _spark ? _spark : _spark = Paint("Fx Spark", 64, 16, (u, v) =>
        {
            float x = Mathf.Abs(u * 2f - 1f), y = Mathf.Abs(v * 2f - 1f);
            float a = Mathf.Clamp01(1f - x * x) * Mathf.Exp(-y * y * 9f);
            return new Color(1f, 1f, 1f, a);
        });

        public static Texture2D Smoke => _smoke ? _smoke : _smoke = Paint("Fx Smoke", 64, 64, (u, v) =>
        {
            float r = Radius(u, v);
            float noise = 0.65f + 0.35f * Mathf.PerlinNoise(u * 5.3f + 11f, v * 5.3f + 7f) + 0.15f * (Mathf.PerlinNoise(u * 13f, v * 13f) - 0.5f);
            float a = Mathf.Clamp01((1f - r) * 1.4f) * noise;
            return new Color(1f, 1f, 1f, Mathf.Clamp01(a * a));
        });

        public static Texture2D Star => _star ? _star : _star = Paint("Fx Star", 64, 64, (u, v) =>
        {
            float x = Mathf.Abs(u * 2f - 1f), y = Mathf.Abs(v * 2f - 1f);
            float r = Mathf.Sqrt(x * x + y * y);
            float core = Mathf.Clamp01(1f - r * 1.8f);
            float rays = Mathf.Exp(-x * 14f) * (1f - y) + Mathf.Exp(-y * 14f) * (1f - x);
            float a = Mathf.Clamp01(core * core * 1.5f + rays * 0.9f) * Mathf.Clamp01(1f - r);
            return new Color(1f, 1f, 1f, a);
        });

        public static Texture2D Ring => _ring ? _ring : _ring = Paint("Fx Ring", 128, 128, (u, v) =>
        {
            float r = Radius(u, v);
            float a = Mathf.Exp(-Mathf.Pow((r - 0.85f) * 9f, 2f));
            return new Color(1f, 1f, 1f, a);
        });

        public static Texture2D Hole => _hole ? _hole : _hole = Paint("Fx Hole", 64, 64, (u, v) =>
        {
            float r = Radius(u, v);
            float rough = 0.12f * (Mathf.PerlinNoise(u * 9f + 3f, v * 9f + 5f) - 0.5f);
            float hole = r < 0.22f + rough ? 1f : 0f;
            float rim = Mathf.Clamp01(1f - (r - 0.22f) / 0.55f) * 0.6f;
            float a = Mathf.Max(hole, rim * rim);
            float shade = hole > 0f ? 0.04f : 0.18f;
            return new Color(shade, shade, shade * 1.05f, a);
        });

        public static Texture2D Scorch => _scorch ? _scorch : _scorch = Paint("Fx Scorch", 128, 128, (u, v) =>
        {
            float r = Radius(u, v) + 0.25f * (Mathf.PerlinNoise(u * 6f + 1f, v * 6f + 2f) - 0.5f);
            float a = Mathf.Clamp01(1f - r) * 0.85f;
            return new Color(0.05f, 0.045f, 0.04f, a * a);
        });

        /// <summary>A shared material that adds light: fire, sparks, flashes, tracers.</summary>
        public static Material Additive(Texture2D texture) => Get(texture, additive: true);

        /// <summary>A shared material that is see-through: smoke, decals.</summary>
        public static Material Alpha(Texture2D texture) => Get(texture, additive: false);

        /// <summary>Sets a material's tint, whichever shader it ended up with.</summary>
        public static void Tint(Material material, Color color)
        {
            if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);
            GreyboxMaterials.SetColor(material, color);
        }

        public static int ColorProperty => ColorId;

        static Material Get(Texture2D texture, bool additive)
        {
            long key = ((long)texture.GetInstanceID() << 1) | (additive ? 1L : 0L);
            if (Materials.TryGetValue(key, out Material cached) && cached != null) return cached;

            Shader shader = EffectShader();
            Material material;
            if (shader != null)
            {
                material = new Material(shader) { name = "Fx " + texture.name + (additive ? " (add)" : " (alpha)") };
                material.mainTexture = texture;
                material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
                material.SetFloat(DstBlendId, (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                material.renderQueue = (int)RenderQueue.Transparent + (additive ? 10 : 0);
                if (additive) material.SetColor(ColorId, new Color(Glow, Glow, Glow, 1f));
            }
            else
            {
                // No effect shader: plain unlit colour rather than nothing, or pink.
                material = new Material(GreyboxMaterials.Unlit(Color.white));
            }
            Materials[key] = material;
            return material;
        }

        static Shader EffectShader()
        {
            if (_shaderChecked) return _shader;
            _shaderChecked = true;
            Shader shader = Resources.Load<Shader>(ShaderResource);
            if (shader == null) shader = Shader.Find("Pulsebreak/Effect");
            if (shader != null && !shader.isSupported) shader = null;
            if (shader == null) Debug.LogWarning("Pulsebreak: the effects shader (" + ShaderResource + ") is missing or unsupported; effects will be drawn plainly.");
            _shader = shader;
            return shader;
        }

        static float Radius(float u, float v)
        {
            float x = u * 2f - 1f, y = v * 2f - 1f;
            return Mathf.Sqrt(x * x + y * y);
        }

        static Texture2D Paint(string name, int width, int height, System.Func<float, float, Color> pixel)
        {
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = pixel((x + 0.5f) / width, (y + 0.5f) / height);
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }
    }
}
