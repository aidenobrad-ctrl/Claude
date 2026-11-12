using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Shared.Cosmetics;

namespace Pulsebreak.Client.Visuals
{
    /// <summary>
    /// Materials for cosmetics: two-colour patterns drawn into small textures in code, so
    /// suits and weapon skins need no art assets. One texture and one material are made per
    /// pattern and colour pair, and shared.
    /// </summary>
    public static class LookMaterials
    {
        const int Size = 64;

        static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Materials.Clear();
            Textures.Clear();
        }

        /// <summary>A lit material printed with a pattern in two colours. A plain pattern is just the first colour.</summary>
        /// <param name="shine">0 matte to 1 mirror-like.</param>
        public static Material Patterned(PatternKind pattern, Color a, Color b, float shine = 0.12f)
        {
            string key = (int)pattern + ":" + ColorUtility.ToHtmlStringRGB(a) + ":" + ColorUtility.ToHtmlStringRGB(b) + ":" + Mathf.RoundToInt(shine * 20f);
            if (Materials.TryGetValue(key, out Material cached) && cached != null) return cached;

            Material material = new Material(GreyboxMaterials.Flat(Color.white)) { name = "Look " + pattern + " " + key };
            if (pattern == PatternKind.Plain)
            {
                GreyboxMaterials.SetColor(material, a);
            }
            else
            {
                GreyboxMaterials.SetColor(material, Color.white);
                Texture2D texture = Texture(pattern, a, b);
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            }
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", Mathf.Lerp(0.1f, 0.92f, shine));
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", Mathf.Lerp(0.1f, 0.92f, shine));
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", shine * 0.85f);
            Materials[key] = material;
            return material;
        }

        /// <summary>The material for a weapon skin's main surfaces, or null for the factory finish.</summary>
        public static Material SkinBody(CosmeticItem skin) =>
            skin == null || skin.Factory ? null : Patterned(skin.Pattern, skin.Color, skin.Second, skin.Shine);

        /// <summary>The material for a weapon skin's darker parts (grips, handguards), or null for the factory finish.</summary>
        public static Material SkinDark(CosmeticItem skin) =>
            skin == null || skin.Factory ? null : Patterned(PatternKind.Plain, Color.Lerp(skin.Color, Color.black, 0.45f), Color.black, skin.Shine * 0.6f);

        /// <summary>The glowing accent of a weapon skin, or null for the factory finish.</summary>
        public static Material SkinAccent(CosmeticItem skin) =>
            skin == null || skin.Factory ? null : GreyboxMaterials.Unlit(skin.Pattern == PatternKind.Plain ? Color.Lerp(skin.Color, Color.white, 0.4f) : skin.Second);

        /// <summary>A square swatch of a pattern, for buttons in the menus.</summary>
        public static Texture2D Texture(PatternKind pattern, Color a, Color b)
        {
            string key = (int)pattern + ":" + ColorUtility.ToHtmlStringRGB(a) + ":" + ColorUtility.ToHtmlStringRGB(b);
            if (Textures.TryGetValue(key, out Texture2D cached) && cached != null) return cached;

            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++) pixels[y * Size + x] = Color.Lerp(a, b, Mix(pattern, x, y));
            }

            bool blocky = pattern == PatternKind.Pixel || pattern == PatternKind.Circuit;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = "Pattern " + pattern,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = blocky ? FilterMode.Point : FilterMode.Trilinear,
                anisoLevel = 4,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            Textures[key] = texture;
            return texture;
        }

        /// <summary>How much of the second colour a pixel of a pattern is, 0 to 1. Every pattern tiles seamlessly.</summary>
        public static float Mix(PatternKind pattern, int x, int y)
        {
            switch (pattern)
            {
                case PatternKind.Stripes:
                    return ((x + y) & 15) < 5 ? 1f : 0f;

                case PatternKind.Chevrons:
                    return ((y + Mathf.Abs((x & 31) - 16)) & 15) < 5 ? 1f : 0f;

                case PatternKind.Honeycomb:
                {
                    // The edges between cells of an offset grid.
                    Nearest(x, y, HexCentres, out float first, out float second, out _, euclidean: true);
                    return second - first < 1.6f ? 1f : 0f;
                }

                case PatternKind.Splinter:
                {
                    // Cells measured by the larger of the two distances come out angular.
                    Nearest(x, y, SplinterPoints, out _, out _, out int cell, euclidean: false);
                    return cell % 3 == 0 ? 1f : cell % 3 == 1 ? 0f : 0.5f;
                }

                case PatternKind.Pixel:
                {
                    int value = Hash((x >> 3) & 7, (y >> 3) & 7) % 5;
                    return value < 2 ? 0f : value < 4 ? 0.55f : 1f;
                }

                case PatternKind.Dots:
                {
                    float dx = (x & 15) - 7.5f, dy = (y & 15) - 7.5f;
                    return dx * dx + dy * dy < 12f ? 1f : 0f;
                }

                case PatternKind.Weave:
                {
                    // Little tiles, alternately running across and down, each shaded along its length.
                    bool across = (((x >> 2) + (y >> 2)) & 1) == 0;
                    float along = across ? (x & 3) / 3f : (y & 3) / 3f;
                    return (across ? 0.05f : 0.35f) + 0.6f * Mathf.Sin(along * Mathf.PI);
                }

                case PatternKind.Tiger:
                {
                    float wobble = 5f * Mathf.Sin(y * Mathf.PI * 2f / 32f) + 2f * Mathf.Sin(y * Mathf.PI * 2f / 16f + 1f);
                    float band = Mathf.Repeat(x + wobble, 16f);
                    float width = 4f + 2f * Mathf.Sin(y * Mathf.PI * 2f / 64f);
                    return band < width ? 1f : 0f;
                }

                case PatternKind.Circuit:
                {
                    // Each 16-pixel cell has one trace across or down it, with a pad at its centre.
                    int cx = x >> 4, cy = y >> 4, lx = x & 15, ly = y & 15;
                    bool down = Hash(cx, cy) % 2 == 0;
                    bool trace = down ? lx == 8 : ly == 8;
                    bool pad = lx >= 6 && lx <= 10 && ly >= 6 && ly <= 10 && Hash(cx + 5, cy + 3) % 3 == 0;
                    return trace || pad ? 1f : 0f;
                }

                default:
                    return 0f;
            }
        }

        static readonly Vector2[] HexCentres = BuildHexCentres();
        static readonly Vector2[] SplinterPoints = BuildSplinterPoints();

        static Vector2[] BuildHexCentres()
        {
            var centres = new List<Vector2>();
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++) centres.Add(new Vector2(column * 16f + (row % 2) * 8f, row * 16f));
            }
            return centres.ToArray();
        }

        static Vector2[] BuildSplinterPoints()
        {
            var points = new Vector2[11];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector2(Hash(i, 17) % Size, Hash(i, 91) % Size);
            return points;
        }

        /// <summary>The nearest and second-nearest point to a pixel, measured round the edges so the result tiles.</summary>
        static void Nearest(int x, int y, Vector2[] points, out float first, out float second, out int index, bool euclidean)
        {
            first = second = float.MaxValue;
            index = 0;
            for (int i = 0; i < points.Length; i++)
            {
                float dx = Mathf.Abs(x - points[i].x), dy = Mathf.Abs(y - points[i].y);
                dx = Mathf.Min(dx, Size - dx);
                dy = Mathf.Min(dy, Size - dy);
                float distance = euclidean ? Mathf.Sqrt(dx * dx + dy * dy) : Mathf.Max(dx * 1.4f, dy) + Mathf.Min(dx, dy) * 0.3f;
                if (distance < first)
                {
                    second = first;
                    first = distance;
                    index = i;
                }
                else if (distance < second)
                {
                    second = distance;
                }
            }
        }

        /// <summary>A small, fixed scramble of two numbers: the same pattern every time the game runs.</summary>
        static int Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)(a * 374761393 + b * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (int)((h ^ (h >> 16)) & 0x7fffffff);
            }
        }
    }
}
