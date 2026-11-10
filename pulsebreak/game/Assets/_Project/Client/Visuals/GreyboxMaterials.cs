using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pulsebreak.Client.Visuals
{
    /// <summary>
    /// Materials for untextured prototype geometry. Everything in Phase 1 is drawn with these,
    /// so there are no art assets to import. One material is shared per colour.
    ///
    /// Works under URP (the project default) and falls back to the Built-in pipeline's
    /// shaders if no render pipeline asset is assigned, so nothing ever renders pink.
    /// </summary>
    public static class GreyboxMaterials
    {
        enum Kind { Grid, Flat, Unlit }

        const string UrpLit = "Universal Render Pipeline/Lit";
        const string UrpUnlit = "Universal Render Pipeline/Unlit";

        static readonly Dictionary<int, Material> Cache = new Dictionary<int, Material>();
        static Material _litBase, _unlitBase;
        static Texture2D _grid;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Cache.Clear();
            _litBase = null;
            _unlitBase = null;
            _grid = null;
        }

        /// <summary>Lit, with a one-metre grid so speed and distance are readable.</summary>
        public static Material Grid(Color color) => Get(color, Kind.Grid);

        /// <summary>Lit, plain colour.</summary>
        public static Material Flat(Color color) => Get(color, Kind.Flat);

        /// <summary>Unlit, plain colour. For tracers, flashes and anything that should glow.</summary>
        public static Material Unlit(Color color) => Get(color, Kind.Unlit);

        /// <summary>Sets the main colour whichever pipeline the material belongs to.</summary>
        public static void SetColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        static Material Get(Color color, Kind kind)
        {
            Color32 c = color;
            int key = ((int)kind << 24) | (c.r << 16) | (c.g << 8) | c.b;
            if (Cache.TryGetValue(key, out Material cached) && cached != null) return cached;

            var material = new Material(kind == Kind.Unlit ? UnlitBase() : LitBase())
            {
                name = "Greybox " + kind + " #" + ColorUtility.ToHtmlStringRGB(color),
            };
            SetColor(material, color);

            if (kind == Kind.Grid)
            {
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", GridTexture());
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", GridTexture());
            }
            if (kind != Kind.Unlit)
            {
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
                if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.12f);
            }

            Cache[key] = material;
            return material;
        }

        static Material LitBase()
        {
            if (_litBase == null) _litBase = LoadBase("Materials/GreyboxLit", UrpLit, "Standard");
            return _litBase;
        }

        static Material UnlitBase()
        {
            if (_unlitBase == null) _unlitBase = LoadBase("Materials/GreyboxUnlit", UrpUnlit, "Unlit/Color");
            return _unlitBase;
        }

        static Material LoadBase(string resourcePath, string urpShader, string builtInShader)
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null)
            {
                // The material asset is what keeps the shader in a built player.
                var asset = Resources.Load<Material>(resourcePath);
                if (asset != null && asset.shader != null && asset.shader.name == urpShader && asset.shader.isSupported)
                    return asset;

                Shader found = Shader.Find(urpShader);
                if (found != null) return new Material(found);
                if (pipeline.defaultMaterial != null) return pipeline.defaultMaterial;
            }

            Shader fallback = Shader.Find(builtInShader);
            if (fallback == null) fallback = Shader.Find("Sprites/Default");
            return new Material(fallback);
        }

        static Texture2D GridTexture()
        {
            if (_grid != null) return _grid;

            const int size = 128;
            var pixels = new Color32[size * size];
            var fill = new Color32(222, 222, 222, 255);
            var minor = new Color32(196, 196, 196, 255);
            var major = new Color32(150, 150, 150, 255);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool edge = x < 2 || y < 2 || x >= size - 2 || y >= size - 2;
                    bool half = x == size / 2 || y == size / 2;
                    pixels[y * size + x] = edge ? major : half ? minor : fill;
                }
            }

            _grid = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Greybox Grid",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            _grid.SetPixels32(pixels);
            _grid.Apply(true, true);
            return _grid;
        }
    }
}
