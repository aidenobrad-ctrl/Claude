// Water: a sea plane reaching past the horizon at y = 0, and the lake and
// river surfaces from the exported per-vertex water levels.
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public static class WaterBuilder
    {
        public static Material WaterMaterial()
        {
            var m = Mats.Make("Water", new Color(0.05f, 0.22f, 0.27f, 0.82f), 0.94f, 0f);
            Mats.MakeTransparent(m);
            return m;
        }

        public static void Build(WorldData w, Transform parent)
        {
            var root = new GameObject("Water").transform;
            root.SetParent(parent, false);
            var mat = WaterMaterial();
            // The sea: a large grid so vertex fog and precision stay good far out.
            var sea = new MeshBuilder(1);
            const int cells = 24;
            const float size = 48000f;
            for (int j = 0; j <= cells; j++)
                for (int i = 0; i <= cells; i++)
                    sea.Add(new Vector3(-size / 2 + size * i / cells, 0, -size / 2 + size * j / cells), Vector3.up, new Vector2(i, j));
            for (int j = 0; j < cells; j++)
                for (int i = 0; i < cells; i++)
                {
                    int a = j * (cells + 1) + i;
                    sea.Quad(0, a, a + cells + 1, a + cells + 2, a + 1);
                }
            sea.ToObject("Sea", root, new[] { mat }, false);
            // Lakes and rivers: runs of 4 m cells at (nearly) the same level, merged per row.
            int n = w.N;
            const int chunk = 64;
            for (int cz = 0; cz < n - 1; cz += chunk)
            {
                for (int cx = 0; cx < n - 1; cx += chunk)
                {
                    var mb = new MeshBuilder(1);
                    for (int j = cz; j < Mathf.Min(n - 1, cz + chunk); j++)
                    {
                        int runStart = -1;
                        float runLevel = 0;
                        for (int i = cx; i <= Mathf.Min(n - 1, cx + chunk); i++)
                        {
                            float level = i < Mathf.Min(n - 1, cx + chunk) ? CellLevel(w, i, j) : float.NaN;
                            bool same = runStart >= 0 && !float.IsNaN(level) && Mathf.Abs(level - runLevel) < 0.03f;
                            if (same) continue;
                            if (runStart >= 0) Strip(mb, w, runStart, i, j, runLevel);
                            runStart = float.IsNaN(level) ? -1 : i;
                            runLevel = level;
                        }
                    }
                    if (!mb.Empty) mb.ToObject($"InlandWater_{cx}_{cz}", root, new[] { mat }, false);
                }
            }
        }

        /// <summary>Water level over a cell, NaN if it is dry or part of the sea.</summary>
        static float CellLevel(WorldData w, int i, int j)
        {
            int n = w.N;
            float sum = 0;
            int count = 0;
            bool wetAboveGround = false;
            for (int k = 0; k < 4; k++)
            {
                int vi = i + (k & 1), vj = j + (k >> 1);
                short wl = w.Water[vj * n + vi];
                if (wl == short.MinValue) continue;
                float lv = wl / 100f;
                sum += lv;
                count++;
                if (lv > w.Height[vj * n + vi]) wetAboveGround = true;
            }
            if (count == 0 || !wetAboveGround) return float.NaN;
            float level = sum / count;
            return level < 0.6f ? float.NaN : level; // the sea plane covers the coast
        }

        static void Strip(MeshBuilder mb, WorldData w, int i0, int i1, int j, float level)
        {
            double x0 = w.Origin + i0 * w.Spacing, x1 = w.Origin + i1 * w.Spacing;
            double z0 = w.Origin + j * w.Spacing, z1 = z0 + w.Spacing;
            // A little over each edge so neighbouring strips meet without cracks.
            float y = level + 0.02f;
            int a = mb.Add(Frame.Pos(x0, y, z0), Vector3.up, new Vector2((float)x0 / 8f, (float)z0 / 8f));
            int b = mb.Add(Frame.Pos(x1, y, z0), Vector3.up, new Vector2((float)x1 / 8f, (float)z0 / 8f));
            int c = mb.Add(Frame.Pos(x1, y, z1), Vector3.up, new Vector2((float)x1 / 8f, (float)z1 / 8f));
            int d = mb.Add(Frame.Pos(x0, y, z1), Vector3.up, new Vector2((float)x0 / 8f, (float)z1 / 8f));
            // Unity z = -z: (x0,z0) -> (x0,z1) runs towards Unity -z. Face up.
            mb.Quad(0, a, d, c, b);
        }
    }
}
