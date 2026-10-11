// Materials made in code that work in URP (Lit / Complex Lit / Terrain Lit)
// and fall back to the built-in pipeline's Standard shaders. Property names
// for both are set; a shader ignores the ones it does not have.
using UnityEngine;

namespace Halcyon
{
    public static class Mats
    {
        static Shader lit, complexLit, terrainLit, unlit;

        public static bool Urp => UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;

        static Shader Find(params string[] names)
        {
            foreach (var n in names)
            {
                var s = Shader.Find(n);
                if (s != null) return s;
            }
            return null;
        }

        public static Shader Lit => lit != null ? lit : lit = Find("Universal Render Pipeline/Lit", "Standard");
        public static Shader ComplexLit => complexLit != null ? complexLit : complexLit = Find("Universal Render Pipeline/Complex Lit", "Universal Render Pipeline/Lit", "Standard");
        public static Shader TerrainLit => terrainLit != null ? terrainLit : terrainLit = Find("Universal Render Pipeline/Terrain/Lit", "Nature/Terrain/Standard");
        public static Shader Unlit => unlit != null ? unlit : unlit = Find("Universal Render Pipeline/Unlit", "Unlit/Color");

        public static Material Make(string name, Color color, float smoothness = 0.3f, float metallic = 0f, Texture2D map = null, Texture2D normal = null, Shader shader = null)
        {
            var m = new Material(shader != null ? shader : Lit) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetColor("_Color", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Glossiness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (map != null)
            {
                m.SetTexture("_BaseMap", map);
                m.SetTexture("_MainTex", map);
            }
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.EnableKeyword("_NORMALMAP");
            }
            m.enableInstancing = true;
            return m;
        }

        public static void SetEmission(Material m, Color c)
        {
            m.SetColor("_EmissionColor", c);
            if (c.maxColorComponent > 0)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else m.DisableKeyword("_EMISSION");
        }

        /// <summary>Car paint: metallic base under a clear coat where the shader supports it.</summary>
        public static Material Paint(Color color, float metallic = 0.55f)
        {
            var m = Make("Paint", color, 0.62f, metallic, shader: ComplexLit);
            m.SetFloat("_ClearCoatMask", 1f);
            m.SetFloat("_ClearCoatSmoothness", 0.95f);
            m.EnableKeyword("_CLEARCOAT");
            return m;
        }

        /// <summary>Transparent surface (water, glass) in either pipeline.</summary>
        public static void MakeTransparent(Material m)
        {
            // URP Lit.
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            // Built-in Standard.
            m.SetFloat("_Mode", 3f);
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }
}
