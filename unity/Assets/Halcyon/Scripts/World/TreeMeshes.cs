// Procedural tree meshes per kind (broadleaf, conifer, palm, cypress, bush,
// dead), each with a detailed near mesh and a light far mesh. Submesh 0 is
// bark, 1 foliage. Sizes are for scale 1; instances scale them.
using UnityEngine;

namespace Halcyon
{
    public static class TreeMeshes
    {
        public const int Bark = 0, Leaves = 1;

        public static Mesh Make(int kind, bool far)
        {
            var mb = new MeshBuilder(2);
            var rng = new System.Random(1000 + kind);
            int sides = far ? 5 : 8;
            switch (kind)
            {
                case Core.TreeKind.Broadleaf:
                    Trunk(mb, Vector3.zero, 3.4f, 0.30f, 0.15f, sides);
                    Branch(mb, new Vector3(0, 2.6f, 0), new Vector3(0.9f, 1.6f, 0.2f), 0.12f, sides);
                    Branch(mb, new Vector3(0, 2.8f, 0), new Vector3(-0.7f, 1.5f, -0.6f), 0.11f, sides);
                    for (int i = 0; i < (far ? 3 : 6); i++)
                    {
                        float a = i * 2.3f;
                        var c = new Vector3(Mathf.Cos(a) * 1.1f, 5.0f + (i % 3) * 0.6f, Mathf.Sin(a) * 1.1f);
                        Blob(mb, c, new Vector3(1.9f, 1.55f, 1.9f) * (0.85f + 0.3f * (float)rng.NextDouble()), far ? 5 : 9, rng.Next());
                    }
                    Blob(mb, new Vector3(0, 6.3f, 0), new Vector3(1.6f, 1.3f, 1.6f), far ? 5 : 9, rng.Next());
                    break;
                case Core.TreeKind.Conifer:
                    Trunk(mb, Vector3.zero, 9.5f, 0.26f, 0.06f, sides);
                    int tiers = far ? 4 : 6;
                    for (int i = 0; i < tiers; i++)
                    {
                        float t = i / (float)(tiers - 1);
                        float y = 1.6f + t * 7.2f;
                        float r = Mathf.Lerp(2.5f, 0.55f, t);
                        Cone(mb, new Vector3(0, y, 0), 2.9f - t * 1.2f, r, far ? 6 : 11, rng.Next());
                    }
                    break;
                case Core.TreeKind.Palm:
                    {
                        // A gently curved trunk, then drooping fronds.
                        var top = Vector3.zero;
                        int segs = far ? 4 : 8;
                        Vector3 prev = Vector3.zero;
                        for (int i = 1; i <= segs; i++)
                        {
                            float t = i / (float)segs;
                            var p = new Vector3(0.9f * t * t, 7.4f * t, 0);
                            Tube(mb, prev, p, Mathf.Lerp(0.24f, 0.15f, (i - 1) / (float)segs), Mathf.Lerp(0.24f, 0.15f, t), sides);
                            prev = p;
                        }
                        top = prev;
                        int fronds = far ? 6 : 10;
                        for (int i = 0; i < fronds; i++)
                        {
                            float a = i * Mathf.PI * 2 / fronds + (float)rng.NextDouble() * 0.3f;
                            Frond(mb, top, new Vector3(Mathf.Cos(a), 0.35f, Mathf.Sin(a)), 3.4f, 0.55f, far ? 3 : 6);
                        }
                        Blob(mb, top + Vector3.down * 0.2f, Vector3.one * 0.45f, 4, 7);
                    }
                    break;
                case Core.TreeKind.Cypress:
                    Trunk(mb, Vector3.zero, 1.6f, 0.18f, 0.12f, sides);
                    Blob(mb, new Vector3(0, 4.6f, 0), new Vector3(1.0f, 3.9f, 1.0f), far ? 6 : 12, rng.Next());
                    Blob(mb, new Vector3(0.1f, 2.6f, 0.05f), new Vector3(1.05f, 1.8f, 1.05f), far ? 4 : 8, rng.Next());
                    break;
                case Core.TreeKind.Bush:
                    for (int i = 0; i < (far ? 2 : 4); i++)
                    {
                        float a = i * 1.9f;
                        Blob(mb, new Vector3(Mathf.Cos(a) * 0.55f, 0.65f + (i % 2) * 0.2f, Mathf.Sin(a) * 0.55f), new Vector3(1.0f, 0.75f, 1.0f) * (0.8f + 0.3f * (float)rng.NextDouble()), far ? 4 : 7, rng.Next());
                    }
                    break;
                default: // Dead tree.
                    Trunk(mb, Vector3.zero, 4.8f, 0.22f, 0.05f, sides);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * 1.7f;
                        Branch(mb, new Vector3(0, 2.0f + i * 0.6f, 0), new Vector3(Mathf.Cos(a) * 1.3f, 1.0f, Mathf.Sin(a) * 1.3f), 0.07f, sides);
                    }
                    break;
            }
            var m = mb.ToMesh("Tree" + kind + (far ? "Far" : ""));
            return m;
        }

        static void Trunk(MeshBuilder mb, Vector3 b, float h, float r0, float r1, int sides) => Tube(mb, b, b + Vector3.up * h, r0, r1, sides);

        static void Branch(MeshBuilder mb, Vector3 from, Vector3 dir, float r, int sides) => Tube(mb, from, from + dir, r, r * 0.4f, sides);

        /// <summary>A tapered tube from a to b (bark).</summary>
        static void Tube(MeshBuilder mb, Vector3 a, Vector3 b, float r0, float r1, int sides)
        {
            var axis = (b - a).normalized;
            var side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var side2 = Vector3.Cross(axis, side);
            int start = mb.Count;
            for (int i = 0; i <= sides; i++)
            {
                float t = i / (float)sides * Mathf.PI * 2;
                var n = side * Mathf.Cos(t) + side2 * Mathf.Sin(t);
                mb.Add(a + n * r0, n, new Vector2(i / (float)sides, 0));
                mb.Add(b + n * r1, n, new Vector2(i / (float)sides, (b - a).magnitude / 2f));
            }
            for (int i = 0; i < sides; i++)
            {
                int p = start + i * 2;
                // Around (side -> side2) then up the axis: faces outward.
                mb.Quad(Bark, p, p + 2, p + 3, p + 1);
            }
        }

        /// <summary>A lumpy ellipsoid of leaves with normals pointing out from its centre (soft foliage shading).</summary>
        static void Blob(MeshBuilder mb, Vector3 c, Vector3 r, int rings, int seed)
        {
            int segs = rings * 2;
            int start = mb.Count;
            var rnd = new System.Random(seed);
            float[] bumps = new float[(rings + 1) * (segs + 1)];
            for (int i = 0; i < bumps.Length; i++) bumps[i] = 0.85f + 0.3f * (float)rnd.NextDouble();
            for (int j = 0; j <= rings; j++)
            {
                float v = j / (float)rings;
                float phi = v * Mathf.PI;
                for (int i = 0; i <= segs; i++)
                {
                    float u = i / (float)segs;
                    float th = u * Mathf.PI * 2;
                    var n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    float k = (i == segs) ? bumps[j * (segs + 1)] : bumps[j * (segs + 1) + i];
                    if (j == 0 || j == rings) k = 1f;
                    var p = c + Vector3.Scale(n, r) * k;
                    mb.Add(p, (n + Vector3.up * 0.25f).normalized, new Vector2(u * 2, v * 2));
                }
            }
            for (int j = 0; j < rings; j++)
            {
                for (int i = 0; i < segs; i++)
                {
                    int a = start + j * (segs + 1) + i;
                    int b = a + segs + 1;
                    mb.Quad(Leaves, a, a + 1, b + 1, b);
                }
            }
        }

        /// <summary>A cone of needles (conifer tier) with a ragged hem.</summary>
        static void Cone(MeshBuilder mb, Vector3 baseC, float h, float r, int sides, int seed)
        {
            var rnd = new System.Random(seed);
            int apex = mb.Add(baseC + Vector3.up * h, Vector3.up, new Vector2(0.5f, 1));
            int start = mb.Count;
            for (int i = 0; i <= sides; i++)
            {
                float t = i / (float)sides * Mathf.PI * 2;
                float k = (i == sides) ? 1f : 0.85f + 0.3f * (float)rnd.NextDouble();
                var d = new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t));
                var p = baseC + d * r * k + Vector3.down * 0.25f * (float)rnd.NextDouble();
                var n = (d * h + Vector3.up * r).normalized;
                mb.Add(p, n, new Vector2(i / (float)sides, 0));
            }
            for (int i = 0; i < sides; i++) mb.Tri(Leaves, apex, start + i + 1, start + i);
            // Underside so the tier does not look hollow from below.
            int under = mb.Add(baseC + Vector3.up * 0.3f, Vector3.down, new Vector2(0.5f, 0.5f));
            for (int i = 0; i < sides; i++) mb.Tri(Leaves, under, start + i, start + i + 1);
        }

        /// <summary>A palm frond: a drooping strip, both sides.</summary>
        static void Frond(MeshBuilder mb, Vector3 root, Vector3 dir, float len, float width, int segs)
        {
            dir = dir.normalized;
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            int start = mb.Count;
            for (int i = 0; i <= segs; i++)
            {
                float t = i / (float)segs;
                var p = root + dir * len * t + Vector3.down * len * 0.45f * t * t;
                float w = width * Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.15f));
                var n = Vector3.Cross(side, (dir + Vector3.down * 0.9f * t).normalized).normalized;
                if (n.y < 0) n = -n;
                mb.Add(p - side * w, n, new Vector2(0, t));
                mb.Add(p + side * w, n, new Vector2(1, t));
            }
            for (int i = 0; i < segs; i++)
            {
                int a = start + i * 2;
                mb.Quad(Leaves, a, a + 2, a + 3, a + 1);
                mb.Quad(Leaves, a, a + 1, a + 3, a + 2);
            }
        }
    }
}
