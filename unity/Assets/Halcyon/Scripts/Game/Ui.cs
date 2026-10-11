// Shared IMGUI pieces for the garage, the workshop and the race menus.
// Screens lay out on a virtual canvas 1080 units tall (as wide as the
// screen's aspect makes it), so text and panels scale with the resolution.
// Plots draw lines into small textures and are redrawn only when their data
// changes.
using System.Collections.Generic;
using UnityEngine;

namespace Halcyon
{
    public static class Ui
    {
        public static GUIStyle Title, Sub, Body, Small, Keys, Mono, Big, Row, RowOn, Value;
        public static readonly Color Accent = new Color(1f, 0.6f, 0.25f);
        public static readonly Color Good = new Color(0.45f, 0.9f, 0.5f);
        public static readonly Color Bad = new Color(1f, 0.42f, 0.36f);
        public static readonly Color Dim = new Color(1, 1, 1, 0.55f);

        /// <summary>Virtual canvas width and height for this frame (height is always 1080).</summary>
        public static float W { get; private set; } = 1920;
        public const float H = 1080;
        static Matrix4x4 saved;

        /// <summary>Call at the top of OnGUI: builds the styles once and scales the canvas.</summary>
        public static void Begin()
        {
            if (Title == null) Build();
            float s = Mathf.Max(0.4f, Screen.height / H);
            W = Screen.width / s;
            saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
        }

        public static void End() => GUI.matrix = saved;

        static GUIStyle Label(int size, Color c, FontStyle f = FontStyle.Normal, TextAnchor a = TextAnchor.UpperLeft)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = f, alignment = a, wordWrap = false, richText = true };
            st.normal.textColor = c;
            return st;
        }

        static void Build()
        {
            Title = Label(38, Color.white, FontStyle.Bold);
            Sub = Label(20, Accent);
            Body = Label(19, new Color(1, 1, 1, 0.92f));
            Body.wordWrap = true;
            Small = Label(16, new Color(1, 1, 1, 0.75f));
            Small.wordWrap = true;
            Keys = Label(17, new Color(1, 1, 1, 0.7f), FontStyle.Normal, TextAnchor.MiddleCenter);
            Mono = Label(18, new Color(1, 1, 1, 0.9f));
            Big = Label(96, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            Row = Label(20, new Color(1, 1, 1, 0.85f), FontStyle.Normal, TextAnchor.MiddleLeft);
            RowOn = Label(20, Color.white, FontStyle.Bold, TextAnchor.MiddleLeft);
            Value = Label(20, new Color(1, 1, 1, 0.9f), FontStyle.Normal, TextAnchor.MiddleRight);
        }

        public static void Panel(Rect r, float alpha = 0.6f)
        {
            var c = GUI.color;
            GUI.color = new Color(0.02f, 0.025f, 0.035f, alpha);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = c;
        }

        public static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>A menu row; returns true when clicked. The selected row is highlighted.</summary>
        public static bool Item(Rect r, string label, bool selected, string value = null, bool enabled = true)
        {
            if (selected) Fill(r, new Color(1f, 0.55f, 0.2f, 0.32f));
            var old = GUI.color;
            if (!enabled) GUI.color = new Color(1, 1, 1, 0.45f);
            GUI.Label(new Rect(r.x + 14, r.y, r.width - 28, r.height), label, selected ? RowOn : Row);
            if (value != null) GUI.Label(new Rect(r.x + 14, r.y, r.width - 28, r.height), value, Value);
            GUI.color = old;
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>A horizontal bar from lo to hi with a marker at v (and an optional reference marker).</summary>
        public static void Bar(Rect r, double lo, double hi, double v, Color c, double reference = double.NaN)
        {
            Fill(r, new Color(1, 1, 1, 0.12f));
            float t = Mathf.Clamp01((float)((v - lo) / (hi - lo)));
            Fill(new Rect(r.x, r.y, r.width * t, r.height), c);
            if (!double.IsNaN(reference))
            {
                float u = Mathf.Clamp01((float)((reference - lo) / (hi - lo)));
                Fill(new Rect(r.x + r.width * u - 1, r.y - 3, 2, r.height + 6), new Color(1, 1, 1, 0.8f));
            }
        }

        public static string Credits(int c) => $"{c:N0} cr";

        public static string Signed(double d, string format) => (d > 0 ? "+" : "") + d.ToString(format);
    }

    /// <summary>A small line chart drawn into a texture.</summary>
    public sealed class Plot
    {
        public readonly Texture2D Tex;
        readonly Color32[] px;
        readonly int w, h;
        static readonly Color32 Back = new Color32(10, 12, 16, 200);
        static readonly Color32 Grid = new Color32(255, 255, 255, 28);

        public Plot(int width, int height)
        {
            w = width;
            h = height;
            px = new Color32[w * h];
            Tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        }

        /// <summary>Clears to the background with a light grid of nx by ny cells.</summary>
        public void Clear(int nx = 4, int ny = 4)
        {
            for (int i = 0; i < px.Length; i++) px[i] = Back;
            for (int k = 1; k < nx; k++) VLine(k * (w - 1) / nx, Grid);
            for (int k = 1; k < ny; k++) HLine(k * (h - 1) / ny, Grid);
        }

        void VLine(int x, Color32 c)
        {
            for (int y = 0; y < h; y++) px[y * w + x] = c;
        }

        void HLine(int y, Color32 c)
        {
            for (int x = 0; x < w; x++) px[y * w + x] = c;
        }

        void Dot(int x, int y, Color32 c)
        {
            for (int dy = 0; dy <= 1; dy++)
            {
                for (int dx = 0; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx >= 0 && xx < w && yy >= 0 && yy < h) px[yy * w + xx] = c;
                }
            }
        }

        /// <summary>A line in data coordinates (x0..x1 by y0..y1 fills the plot).</summary>
        public void Line(double ax, double ay, double bx, double by, Rect domain, Color32 c)
        {
            float x0 = (float)((ax - domain.xMin) / domain.width * (w - 2));
            float y0 = (float)((ay - domain.yMin) / domain.height * (h - 2));
            float x1 = (float)((bx - domain.xMin) / domain.width * (w - 2));
            float y1 = (float)((by - domain.yMin) / domain.height * (h - 2));
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0))));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Dot(Mathf.RoundToInt(x0 + (x1 - x0) * t), Mathf.RoundToInt(y0 + (y1 - y0) * t), c);
            }
        }

        public void Series(IList<Vector2> pts, Rect domain, Color32 c)
        {
            for (int i = 1; i < pts.Count; i++) Line(pts[i - 1].x, pts[i - 1].y, pts[i].x, pts[i].y, domain, c);
        }

        public void Apply()
        {
            Tex.SetPixels32(px);
            Tex.Apply(false);
        }

        public void Draw(Rect r) => GUI.DrawTexture(r, Tex);
    }
}
