// Tileable textures generated at startup, so the island has believable
// ground, asphalt and facades before hand-made or CC0 texture sets replace
// them: albedo with layered value noise, normal maps from the same heights.
using UnityEngine;

namespace Halcyon
{
    public static class ProceduralTextures
    {
        // --- Tileable value noise ------------------------------------------------------
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
            }
        }

        /// <summary>Value noise in [0,1], tileable with the given period (in lattice cells).</summary>
        public static float Noise(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            float u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy);
            int x0 = Mod(xi, period), x1 = Mod(xi + 1, period), y0 = Mod(yi, period), y1 = Mod(yi + 1, period);
            float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        static int Mod(int a, int m) => ((a % m) + m) % m;

        /// <summary>Tileable fBm over [0,1)^2 coordinates.</summary>
        public static float Fbm(float u, float v, int baseCells, int octaves, int seed, float gain = 0.5f)
        {
            float sum = 0, amp = 1, norm = 0;
            int cells = baseCells;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Noise(u * cells, v * cells, cells, seed + o * 31);
                norm += amp;
                amp *= gain;
                cells *= 2;
            }
            return sum / norm;
        }

        public delegate float HeightFn(float u, float v);
        public delegate Color ColorFn(float u, float v, float h);

        /// <summary>An albedo and a matching normal map from a height function.</summary>
        public static void Make(int size, HeightFn height, ColorFn color, float bumpiness, out Texture2D albedo, out Texture2D normal, string name)
        {
            var h = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    h[y * size + x] = height((float)x / size, (float)y / size);
            var cols = new Color32[size * size];
            var nrm = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float hv = h[y * size + x];
                    cols[y * size + x] = color((float)x / size, (float)y / size, hv);
                    float dx = h[y * size + (x + 1) % size] - h[y * size + (x - 1 + size) % size];
                    float dy = h[((y + 1) % size) * size + x] - h[((y - 1 + size) % size) * size + x];
                    var n = new Vector3(-dx * bumpiness, -dy * bumpiness, 1f).normalized;
                    nrm[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }
            albedo = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            albedo.SetPixels32(cols);
            albedo.Apply(true, false);
            normal = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = name + "_N", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            normal.SetPixels32(nrm);
            normal.Apply(true, false);
        }

        static Color Mix(Color a, Color b, float t) => Color.Lerp(a, b, Mathf.Clamp01(t));

        // --- Terrain layers -----------------------------------------------------------------
        public static void Grass(out Texture2D a, out Texture2D n) => Make(512,
            (u, v) => Fbm(u, v, 16, 5, 11) * 0.7f + Fbm(u, v, 64, 2, 12) * 0.3f,
            (u, v, h) =>
            {
                float patch = Fbm(u, v, 4, 3, 13);
                var lush = new Color(0.20f, 0.33f, 0.10f);
                var dry = new Color(0.40f, 0.42f, 0.18f);
                var c = Mix(lush, dry, patch * 1.4f - 0.3f);
                return c * (0.72f + 0.5f * h);
            }, 3f, out a, out n, "Grass");

        public static void Rock(out Texture2D a, out Texture2D n) => Make(512,
            (u, v) =>
            {
                float f = Fbm(u, v, 6, 6, 21, 0.55f);
                float ridges = 1 - Mathf.Abs(Fbm(u, v, 12, 3, 22) * 2 - 1);
                return f * 0.7f + ridges * 0.3f;
            },
            (u, v, h) =>
            {
                var light = new Color(0.52f, 0.49f, 0.45f);
                var dark = new Color(0.26f, 0.25f, 0.24f);
                return Mix(dark, light, h * 1.3f - 0.1f);
            }, 7f, out a, out n, "Rock");

        public static void Dirt(out Texture2D a, out Texture2D n) => Make(512,
            (u, v) => Fbm(u, v, 12, 5, 31) * 0.8f + (Noise(u * 160, v * 160, 160, 32) > 0.82f ? 0.25f : 0f),
            (u, v, h) =>
            {
                var c = Mix(new Color(0.33f, 0.24f, 0.16f), new Color(0.50f, 0.40f, 0.28f), h);
                return c;
            }, 4f, out a, out n, "Dirt");

        public static void Sand(out Texture2D a, out Texture2D n) => Make(512,
            (u, v) => Fbm(u, v, 8, 4, 41) * 0.6f + Mathf.Sin((u * 24 + Fbm(u, v, 4, 2, 42) * 3) * Mathf.PI * 2) * 0.08f,
            (u, v, h) => Mix(new Color(0.72f, 0.62f, 0.44f), new Color(0.86f, 0.78f, 0.60f), h * 1.2f - 0.1f), 2f, out a, out n, "Sand");

        public static void Snow(out Texture2D a, out Texture2D n) => Make(512,
            (u, v) => Fbm(u, v, 6, 5, 51) * 0.7f,
            (u, v, h) => Mix(new Color(0.80f, 0.84f, 0.90f), new Color(0.97f, 0.98f, 1f), h * 1.3f), 2f, out a, out n, "Snow");

        // --- Roads --------------------------------------------------------------------------------
        /// <summary>
        /// Asphalt with markings for one road class. u runs across the paved width
        /// (0 = left edge, 1 = right edge), v along the road with a 12 m period.
        /// </summary>
        public static Texture2D Road(Core.RoadClassInfo cls, bool dirt, out Texture2D normal)
        {
            const int W = 256, H = 1024;
            float widthM = (float)(cls.halfWidth * 2);
            var cols = new Color32[W * H];
            var hts = new float[W * H];
            float[] lanes = LaneCentres(cls, widthM);
            for (int y = 0; y < H; y++)
            {
                float vm = y / (float)H * 12f;
                for (int x = 0; x < W; x++)
                {
                    float um = x / (float)W * widthM;
                    float u = x / (float)W, v = y / (float)H;
                    float grain = Fbm(u * 4, v, 16, 3, 61);
                    float h = grain;
                    Color c;
                    if (dirt)
                    {
                        float rut = Mathf.Exp(-Mathf.Pow((Mathf.Abs(um - widthM / 2) - 0.85f) / 0.35f, 2));
                        c = Mix(new Color(0.42f, 0.33f, 0.23f), new Color(0.30f, 0.23f, 0.16f), rut * 0.7f + grain * 0.4f);
                    }
                    else
                    {
                        float tar = Fbm(u * 2, v * 0.5f, 6, 3, 62);
                        c = Mix(new Color(0.085f, 0.085f, 0.09f), new Color(0.16f, 0.16f, 0.165f), grain * 0.9f + tar * 0.3f);
                        // Wheel tracks are a touch darker and smoother.
                        foreach (float lane in lanes)
                        {
                            c *= 1f - 0.12f * Mathf.Exp(-Mathf.Pow((um - lane + 0.85f) / 0.35f, 2));
                            c *= 1f - 0.12f * Mathf.Exp(-Mathf.Pow((um - lane - 0.85f) / 0.35f, 2));
                        }
                        float paint = Markings(cls, um, vm, widthM, out bool yellow);
                        if (paint > 0)
                        {
                            var line = yellow ? new Color(0.85f, 0.66f, 0.12f) : new Color(0.86f, 0.86f, 0.84f);
                            c = Mix(c, line * (0.85f + 0.15f * grain), paint);
                            h += paint * 0.2f;
                        }
                    }
                    cols[y * W + x] = c;
                    hts[y * W + x] = h;
                }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true, false) { name = "Road_" + cls.id, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 16 };
            tex.wrapModeU = TextureWrapMode.Clamp;
            tex.SetPixels32(cols);
            tex.Apply(true, false);
            var nrm = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float dx = hts[y * W + Mathf.Min(W - 1, x + 1)] - hts[y * W + Mathf.Max(0, x - 1)];
                    float dy = hts[((y + 1) % H) * W + x] - hts[((y - 1 + H) % H) * W + x];
                    var n = new Vector3(-dx * 2f, -dy * 2f, 1f).normalized;
                    nrm[y * W + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }
            normal = new Texture2D(W, H, TextureFormat.RGBA32, true, true) { name = "Road_" + cls.id + "_N", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 16 };
            normal.SetPixels32(nrm);
            normal.Apply(true, false);
            return tex;
        }

        static float[] LaneCentres(Core.RoadClassInfo cls, float widthM)
        {
            int lanes = Mathf.Max(1, cls.lanes);
            var r = new float[lanes];
            for (int i = 0; i < lanes; i++) r[i] = (i + 0.5f) * widthM / lanes;
            return r;
        }

        static float Line(float um, float at, float halfW)
        {
            float d = Mathf.Abs(um - at) - halfW;
            return Mathf.Clamp01(1 - d / 0.02f);
        }

        static float Markings(Core.RoadClassInfo cls, float um, float vm, float widthM, out bool yellow)
        {
            yellow = false;
            float p = 0;
            if (cls.edgeLines)
            {
                p = Mathf.Max(p, Line(um, 0.35f, 0.075f));
                p = Mathf.Max(p, Line(um, widthM - 0.35f, 0.075f));
            }
            float mid = widthM / 2;
            bool dash = (vm % 12f) < 3f;
            switch (cls.center)
            {
                case Core.CenterLine.Dashed:
                    if (dash) p = Mathf.Max(p, Line(um, mid, 0.06f));
                    break;
                case Core.CenterLine.Solid:
                    p = Mathf.Max(p, Line(um, mid, 0.06f));
                    break;
                case Core.CenterLine.Double:
                    float d = Mathf.Max(Line(um, mid - 0.12f, 0.06f), Line(um, mid + 0.12f, 0.06f));
                    if (d > 0) yellow = true;
                    p = Mathf.Max(p, d);
                    break;
            }
            // Multi-lane roads: dashed lane separators in each direction.
            if (cls.lanes >= 4 && dash)
            {
                p = Mathf.Max(p, Line(um, widthM * 0.25f, 0.06f));
                p = Mathf.Max(p, Line(um, widthM * 0.75f, 0.06f));
            }
            return p;
        }

        public static void Gravel(out Texture2D a, out Texture2D n) => Make(256,
            (u, v) => Noise(u * 96, v * 96, 96, 71) * 0.6f + Fbm(u, v, 8, 3, 72) * 0.4f,
            (u, v, h) => Mix(new Color(0.30f, 0.27f, 0.23f), new Color(0.52f, 0.49f, 0.44f), h), 5f, out a, out n, "Gravel");

        public static void Concrete(out Texture2D a, out Texture2D n) => Make(256,
            (u, v) => Fbm(u, v, 16, 4, 81) * 0.5f + (Noise(u * 128, v * 128, 128, 82) > 0.9f ? 0.2f : 0),
            (u, v, h) => Mix(new Color(0.48f, 0.47f, 0.45f), new Color(0.64f, 0.63f, 0.60f), h), 1.5f, out a, out n, "Concrete");

        /// <summary>
        /// The facade's windows at night, as an emission map on the same UVs:
        /// about four in ten lit, in warm tungsten, cooler LED and the odd
        /// television blue, a little uneven across each pane.
        /// </summary>
        public static Texture2D FacadeLights()
        {
            const int size = 256;
            var cols = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    float fx = (u * 4) % 1f, fy = (v * 4) % 1f;
                    bool win = fx > 0.27f && fx < 0.73f && fy > 0.32f && fy < 0.78f;
                    Color c = Color.black;
                    if (win)
                    {
                        int wx = Mathf.FloorToInt(u * 4), wy = Mathf.FloorToInt(v * 4);
                        float h = Hash(wx, wy, 401), k = Hash(wx, wy, 402);
                        if (h < 0.42f)
                        {
                            c = k < 0.6f ? new Color(1f, 0.72f, 0.42f) : k < 0.9f ? new Color(0.95f, 0.9f, 0.8f) : new Color(0.55f, 0.68f, 1f);
                            // Brighter toward the top of the pane (a ceiling lamp), with a curtain's unevenness.
                            c *= (0.55f + 0.45f * fy) * (0.8f + 0.2f * Noise(u * 64, v * 64, 64, 403)) * (0.6f + 0.4f * Hash(wx, wy, 404));
                        }
                    }
                    cols[y * size + x] = c;
                }
            }
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = "FacadeLights", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.SetPixels32(cols);
            t.Apply(true, false);
            return t;
        }

        /// <summary>A raindrop streak: bright in the middle, fading to the ends and the sides.</summary>
        public static Texture2D RainStreak()
        {
            const int w = 8, h = 64;
            var cols = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sx = 1 - Mathf.Abs((x + 0.5f) / w * 2 - 1), sy = Mathf.Sin((y + 0.5f) / h * Mathf.PI);
                    cols[y * w + x] = new Color(1, 1, 1, Mathf.Pow(sx, 1.5f) * sy);
                }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, false) { name = "RainStreak", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels32(cols);
            t.Apply(false, false);
            return t;
        }

        /// <summary>
        /// The GPS ribbon: chevrons pointing up the texture (+v, the way to go) that repeat along
        /// it, over a faint band that fades out at the sides. White: the material tints it.
        /// </summary>
        public static Texture2D GpsChevrons()
        {
            const int w = 32, h = 64;
            var cols = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float across = Mathf.Abs((x + 0.5f) / w * 2 - 1), along = (y + 0.5f) / h;
                    // The tip leads at the centre: the sides trail behind it.
                    float f = Mathf.Repeat(along + across * 0.22f, 1);
                    float chev = Mathf.SmoothStep(0, 1, f / 0.08f) * (1 - Mathf.SmoothStep(0, 1, (f - 0.38f) / 0.12f));
                    float edge = 1 - Mathf.SmoothStep(0, 1, (across - 0.75f) / 0.25f);
                    cols[y * w + x] = new Color(1, 1, 1, (0.28f + 0.72f * chev) * edge);
                }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, false) { name = "GpsChevrons", wrapModeU = TextureWrapMode.Clamp, wrapModeV = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels32(cols);
            t.Apply(true, false);
            return t;
        }

        /// <summary>A soft round puff for spray and mist.</summary>
        public static Texture2D SoftDot()
        {
            const int size = 64;
            var cols = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2 - 1, dy = (y + 0.5f) / size * 2 - 1;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1 - r);
                    a = a * a * (3 - 2 * a) * (0.75f + 0.25f * Noise(x / 8f, y / 8f, 8, 411));
                    cols[y * size + x] = new Color(1, 1, 1, a);
                }
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = "SoftDot", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            t.SetPixels32(cols);
            t.Apply(true, false);
            return t;
        }

        /// <summary>
        /// Cloud cover as alpha (white RGB), tileable: fBm thresholded so the
        /// share of sky covered follows coverage (0 clear to 1 overcast), with
        /// soft edges. Pure math on plain arrays: safe on a worker thread.
        /// </summary>
        public static Color32[] CloudPixels(int size, float coverage, int seed)
        {
            var px = new Color32[size * size];
            float cut = Mathf.Lerp(0.78f, 0.18f, Mathf.Clamp01(coverage));
            float soft = 0.16f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    float n = Fbm(u, v, 4, 6, seed, 0.55f);
                    // Ridges and wisps: stretch a little along the wind.
                    n = n * 0.85f + 0.15f * Fbm(u * 2 % 1f, v, 8, 3, seed + 7);
                    float a = Mathf.Clamp01((n - cut) / soft);
                    a = a * a * (3 - 2 * a);
                    // Thick cloud is darker underneath: keep that in the green channel (shade).
                    byte shade = (byte)(255 * (1 - 0.45f * Mathf.Clamp01((n - cut - soft) / 0.3f)));
                    px[y * size + x] = new Color32(shade, shade, shade, (byte)(255 * a));
                }
            return px;
        }

        /// <summary>A facade: wall with a grid of windows every 3 m (u, v in metres / 3).</summary>
        public static void Facade(out Texture2D a, out Texture2D n) => Make(256,
            (u, v) =>
            {
                float fx = (u * 4) % 1f, fy = (v * 4) % 1f;
                bool win = fx > 0.25f && fx < 0.75f && fy > 0.3f && fy < 0.8f;
                return win ? 0.2f : 0.6f + Fbm(u, v, 16, 2, 91) * 0.1f;
            },
            (u, v, h) =>
            {
                float fx = (u * 4) % 1f, fy = (v * 4) % 1f;
                bool win = fx > 0.25f && fx < 0.75f && fy > 0.3f && fy < 0.8f;
                if (win)
                {
                    float sky = 0.35f + 0.4f * fy + 0.1f * Noise(u * 4, v * 4, 4, 92);
                    return new Color(0.16f * sky, 0.22f * sky, 0.30f * sky);
                }
                return Color.white * (0.85f + 0.15f * h);
            }, 6f, out a, out n, "Facade");
    }
}
