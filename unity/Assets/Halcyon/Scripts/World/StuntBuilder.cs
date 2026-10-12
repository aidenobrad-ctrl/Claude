// The stunts by the road (Halcyon.Core.Stunts): a board for each, beside
// the road and facing the cars that come at it, with an icon in the stunt's
// colour (a camera for a speed trap, a speedometer for a speed zone, a
// swirl for a drift zone, the warning triangle for a danger sign) and its
// name; zones get a pair of poles at each end. The panels light up at night.
using System.Collections.Generic;
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public static class StuntBuilder
    {
        /// <summary>Each kind's colour (the boards, the map, the HUD).</summary>
        public static Color Colour(StuntKind k) => k switch
        {
            StuntKind.SpeedTrap => new Color(0.18f, 0.5f, 1f),
            StuntKind.SpeedZone => new Color(1f, 0.52f, 0.12f),
            StuntKind.DriftZone => new Color(0.66f, 0.3f, 1f),
            _ => new Color(1f, 0.8f, 0.1f),
        };

        static readonly Dictionary<StuntKind, Texture2D> icons = new Dictionary<StuntKind, Texture2D>();

        /// <summary>The kind's icon (also drawn on the map and the HUD).</summary>
        public static Texture2D Icon(StuntKind k)
        {
            if (!icons.TryGetValue(k, out var t)) icons[k] = t = MakeIcon(k);
            return t;
        }

        public static void Build(WorldData w, List<Stunt> stunts, Transform parent)
        {
            var root = new GameObject("Stunts").transform;
            root.SetParent(parent, false);
            var post = Mats.Make("StuntPost", new Color(0.32f, 0.33f, 0.35f), 0.4f, 0.6f);
            var back = Mats.Make("StuntBoard", new Color(0.08f, 0.09f, 0.1f), 0.3f);
            Font font = null;
            try
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (System.Exception)
            {
                // Older editors call it Arial.
            }
            if (font == null)
            {
                try
                {
                    font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                catch (System.Exception)
                {
                    font = null;
                }
            }
            var panels = new Dictionary<StuntKind, Material>();
            foreach (StuntKind k in System.Enum.GetValues(typeof(StuntKind)))
            {
                var m = Mats.Make("Stunt" + k, Color.white, 0.2f, 0f, Icon(k));
                // A little light of their own at night, like a road sign in the headlights.
                SkyDirector.Glow(m, Color.black, new Color(0.35f, 0.35f, 0.35f));
                panels[k] = m;
            }
            foreach (var st in stunts)
            {
                var e = w.Edges[st.Edge];
                var go = new GameObject(st.Name).transform;
                go.SetParent(root, false);
                // The way the cars come: along the edge, or against it for a jump taken that way.
                double tx = System.Math.Sin(-st.Yaw), tz = -System.Math.Cos(st.Yaw);
                double side = e.info.halfWidth + e.info.shoulder + 2.2;
                // On the right of the road for cars coming at it.
                double bx = st.X - tz * side, bz = st.Z + tx * side;
                go.position = Frame.Pos(bx, st.Y, bz);
                go.rotation = Quaternion.LookRotation(Frame.Dir(tx, 0, tz));
                Board(go, st, post, back, panels[st.Kind], font, st.Kind != StuntKind.DangerSign);
                if (st.Kind == StuntKind.SpeedZone || st.Kind == StuntKind.DriftZone)
                {
                    Gate(root, e, st.S0, Colour(st.Kind), post);
                    Gate(root, e, st.S1, Colour(st.Kind), post);
                }
            }
        }

        /// <summary>Two posts and a dark panel, the icon on the front (and the back for a stunt taken either way), the name under it.</summary>
        static void Board(Transform t, Stunt st, Material post, Material back, Material panel, Font font, bool twoSided)
        {
            var mb = new MeshBuilder(2);
            mb.Box(0, new Vector3(-1.5f, 1.5f, 0.06f), Quaternion.identity, new Vector3(0.12f, 3f, 0.12f));
            mb.Box(0, new Vector3(1.5f, 1.5f, 0.06f), Quaternion.identity, new Vector3(0.12f, 3f, 0.12f));
            mb.Box(1, new Vector3(0, 3.1f, 0.06f), Quaternion.identity, new Vector3(3.4f, 2.0f, 0.08f));
            mb.ToObject("Frame", t, new[] { post, back });
            Face(t, panel, new Vector3(0, 3.35f, -0.01f), Quaternion.identity);
            if (twoSided) Face(t, panel, new Vector3(0, 3.35f, 0.13f), Quaternion.Euler(0, 180, 0));
            if (font == null) return;
            Label(t, st.Name, font, new Vector3(0, 2.38f, -0.02f), Quaternion.identity);
            if (twoSided) Label(t, st.Name, font, new Vector3(0, 2.38f, 0.14f), Quaternion.Euler(0, 180, 0));
        }

        static void Face(Transform t, Material m, Vector3 at, Quaternion r)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Icon";
            Object.Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(t, false);
            q.transform.localPosition = at;
            q.transform.localRotation = r;
            q.transform.localScale = new Vector3(1.35f, 1.35f, 1);
            var mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void Label(Transform t, string text, Font font, Vector3 at, Quaternion r)
        {
            var go = new GameObject("Name");
            go.transform.SetParent(t, false);
            go.transform.localPosition = at;
            // Like a Quad, a TextMesh reads from -Z.
            go.transform.localRotation = r;
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.text = text.ToUpperInvariant();
            tm.fontSize = 48;
            tm.characterSize = 0.06f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = font.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // A line is about fontSize x characterSize / 10 units tall, a capital about 0.62 of that wide;
            // long names shrink to fit the 3.2 m panel.
            float width = text.Length * tm.fontSize * tm.characterSize * 0.1f * 0.62f;
            if (width > 3.1f) go.transform.localScale = Vector3.one * (3.1f / width);
        }

        /// <summary>A pole either side of the road at arc length s, banded in the stunt's colour.</summary>
        static void Gate(Transform root, RoadEdge e, double s, Color c, Material post)
        {
            int i = 0;
            while (i < e.n - 1 && e.s[i + 1] <= s) i++;
            var band = Mats.Make("StuntGate", c, 0.3f);
            SkyDirector.Glow(band, Color.black, c * 0.6f);
            for (int k = -1; k <= 1; k += 2)
            {
                double off = k * (e.info.halfWidth + e.info.shoulder + 0.8);
                var go = new GameObject("Gate").transform;
                go.SetParent(root, false);
                go.position = Frame.Pos(e.x[i] - e.tz[i] * off, e.y[i], e.z[i] + e.tx[i] * off);
                var mb = new MeshBuilder(2);
                mb.Box(0, new Vector3(0, 1.6f, 0), Quaternion.identity, new Vector3(0.18f, 3.2f, 0.18f));
                mb.Box(1, new Vector3(0, 2.9f, 0), Quaternion.identity, new Vector3(0.22f, 0.5f, 0.22f));
                mb.ToObject("Pole", go, new[] { post, band });
            }
        }

        // --- Icons (128 px, drawn in code) ---------------------------------------------------------

        static Texture2D MakeIcon(StuntKind k)
        {
            const int N = 128;
            var px = new Color32[N * N];
            var bg = (Color32)Colour(k);
            var white = new Color32(255, 255, 255, 255);
            var black = new Color32(20, 20, 22, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            // A white border.
            Rect(px, N, 0, 0, N, 5, white);
            Rect(px, N, 0, N - 5, N, 5, white);
            Rect(px, N, 0, 0, 5, N, white);
            Rect(px, N, N - 5, 0, 5, N, white);
            switch (k)
            {
                case StuntKind.SpeedTrap:
                    // A camera: a rounded body, the viewfinder bump, a lens ring.
                    Rect(px, N, 26, 38, 76, 50, white);
                    Rect(px, N, 44, 88, 26, 10, white);
                    Disc(px, N, 64, 63, 18, bg);
                    Disc(px, N, 64, 63, 11, white);
                    break;
                case StuntKind.SpeedZone:
                    // A speedometer: an arc and a needle up to the right.
                    for (int a = 0; a <= 180; a += 2)
                    {
                        float r = a * Mathf.Deg2Rad;
                        Disc(px, N, 64 + Mathf.RoundToInt(Mathf.Cos(r) * 40), 46 + Mathf.RoundToInt(Mathf.Sin(r) * 40), 5, white);
                    }
                    Line(px, N, 64, 46, 92, 80, 5, white);
                    Disc(px, N, 64, 46, 8, white);
                    break;
                case StuntKind.DriftZone:
                    // A swirl: two offset half-circles, an S of tyre tracks.
                    for (int a = 0; a <= 180; a += 2)
                    {
                        float r = a * Mathf.Deg2Rad;
                        Disc(px, N, 50 + Mathf.RoundToInt(Mathf.Cos(r) * 22), 74 + Mathf.RoundToInt(Mathf.Sin(r) * 22), 4, white);
                        Disc(px, N, 78 - Mathf.RoundToInt(Mathf.Cos(r) * 22), 52 - Mathf.RoundToInt(Mathf.Sin(r) * 22), 4, white);
                    }
                    break;
                default:
                    // The warning triangle with an exclamation mark.
                    Triangle(px, N, 64, 104, 22, 30, 106, 30, black, 7);
                    Rect(px, N, 60, 52, 8, 32, black);
                    Rect(px, N, 60, 38, 8, 8, black);
                    break;
            }
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "StuntIcon" + k, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        static void Put(Color32[] px, int n, int x, int y, Color32 c)
        {
            if (x >= 0 && y >= 0 && x < n && y < n) px[y * n + x] = c;
        }

        static void Rect(Color32[] px, int n, int x, int y, int w, int h, Color32 c)
        {
            for (int j = y; j < y + h; j++)
                for (int i = x; i < x + w; i++) Put(px, n, i, j, c);
        }

        static void Disc(Color32[] px, int n, int cx, int cy, int r, Color32 c)
        {
            for (int j = -r; j <= r; j++)
                for (int i = -r; i <= r; i++)
                    if (i * i + j * j <= r * r) Put(px, n, cx + i, cy + j, c);
        }

        static void Line(Color32[] px, int n, int x0, int y0, int x1, int y1, int r, Color32 c)
        {
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
            for (int s = 0; s <= steps; s++)
                Disc(px, n, x0 + (x1 - x0) * s / Mathf.Max(1, steps), y0 + (y1 - y0) * s / Mathf.Max(1, steps), r, c);
        }

        static void Triangle(Color32[] px, int n, int ax, int ay, int bx, int by, int cx, int cy, Color32 c, int r)
        {
            Line(px, n, ax, ay, bx, by, r, c);
            Line(px, n, bx, by, cx, cy, r, c);
            Line(px, n, cx, cy, ax, ay, r, c);
        }
    }
}
