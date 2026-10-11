// Accumulates vertices and per-material triangle lists, then makes a Mesh.
using System.Collections.Generic;
using UnityEngine;

namespace Halcyon
{
    public sealed class MeshBuilder
    {
        public readonly List<Vector3> Verts = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<Color32> Colors = new List<Color32>();
        readonly List<List<int>> subs = new List<List<int>>();
        public bool UseColors;

        public MeshBuilder(int submeshes)
        {
            for (int i = 0; i < submeshes; i++) subs.Add(new List<int>());
        }

        public int Count => Verts.Count;
        public bool Empty
        {
            get
            {
                foreach (var s in subs) if (s.Count > 0) return false;
                return true;
            }
        }

        public int Add(Vector3 p, Vector3 n, Vector2 uv)
        {
            Verts.Add(p);
            Normals.Add(n);
            Uvs.Add(uv);
            if (UseColors) Colors.Add(new Color32(255, 255, 255, 255));
            return Verts.Count - 1;
        }

        public int Add(Vector3 p, Vector3 n, Vector2 uv, Color32 c)
        {
            Verts.Add(p);
            Normals.Add(n);
            Uvs.Add(uv);
            Colors.Add(c);
            return Verts.Count - 1;
        }

        /// <summary>Triangle facing Vector3.Cross(b - a, c - a) (Unity's front face).</summary>
        public void Tri(int sub, int a, int b, int c)
        {
            var l = subs[sub];
            l.Add(a);
            l.Add(b);
            l.Add(c);
        }

        /// <summary>Quad a-b-c-d, front facing Cross(b - a, d - a).</summary>
        public void Quad(int sub, int a, int b, int c, int d)
        {
            Tri(sub, a, b, d);
            Tri(sub, d, b, c);
        }

        /// <summary>A flat quad with its own vertices, normal from its corners.</summary>
        public void FlatQuad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uvScale)
        {
            var n = Vector3.Cross(b - a, d - a).normalized;
            float w = Vector3.Distance(a, b), h = Vector3.Distance(a, d);
            int ia = Add(a, n, Vector2.zero);
            int ib = Add(b, n, new Vector2(w * uvScale.x, 0));
            int ic = Add(c, n, new Vector2(w * uvScale.x, h * uvScale.y));
            int id = Add(d, n, new Vector2(0, h * uvScale.y));
            Quad(sub, ia, ib, ic, id);
        }

        /// <summary>An axis-aligned-in-its-own-frame box: centre, rotation, size.</summary>
        public void Box(int sub, Vector3 c, Quaternion r, Vector3 size, float uvScale = 1f)
        {
            Vector3 hx = r * new Vector3(size.x / 2, 0, 0), hy = r * new Vector3(0, size.y / 2, 0), hz = r * new Vector3(0, 0, size.z / 2);
            Vector3 P(int sx, int sy, int sz) => c + hx * sx + hy * sy + hz * sz;
            var uv = new Vector2(uvScale, uvScale);
            FlatQuad(sub, P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), uv);   // top
            FlatQuad(sub, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), uv); // bottom
            FlatQuad(sub, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), uv);   // +z
            FlatQuad(sub, P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), uv); // -z
            FlatQuad(sub, P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), uv);   // +x
            FlatQuad(sub, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), uv); // -x
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (Verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(Verts);
            m.SetNormals(Normals);
            m.SetUVs(0, Uvs);
            if (Colors.Count == Verts.Count && Colors.Count > 0) m.SetColors(Colors);
            m.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) m.SetTriangles(subs[i], i, false);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        public GameObject ToObject(string name, Transform parent, Material[] mats, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = ToMesh(name);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
    }
}
