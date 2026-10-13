// Draws the island's 185k trees and bushes with GPU instancing: detailed
// meshes near the camera, light ones out to the horizon, rebuilt into
// batches of 1023 whenever the camera has moved far enough.
using System.Collections.Generic;
using Halcyon.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Halcyon
{
    public sealed class VegetationRenderer : MonoBehaviour
    {
        public float NearDistance = 420f;
        public float FarDistance = 2600f;
        public bool CastShadowsNear = true;

        const float Chunk = 256f;
        const int Tints = 2;
        WorldData w;
        int chunksPerSide;
        // [chunk][kind * Tints + tint] -> matrices
        List<Matrix4x4>[][] chunks;
        readonly List<Batch> near = new List<Batch>();
        readonly List<Batch> far = new List<Batch>();
        Mesh[] nearMesh, farMesh;
        Material bark;
        Material[] leaves;
        Vector3 builtAt = new Vector3(1e9f, 0, 0);
        Camera cam;

        sealed class Batch
        {
            public int kind, tint;
            public Matrix4x4[] m = new Matrix4x4[1023];
            public int count;
        }

        static readonly Color[,] LeafColors =
        {
            { new Color(0.15f, 0.29f, 0.09f), new Color(0.22f, 0.33f, 0.10f) }, // broadleaf
            { new Color(0.08f, 0.19f, 0.10f), new Color(0.11f, 0.23f, 0.11f) }, // conifer
            { new Color(0.22f, 0.35f, 0.11f), new Color(0.28f, 0.38f, 0.12f) }, // palm
            { new Color(0.09f, 0.21f, 0.08f), new Color(0.12f, 0.24f, 0.09f) }, // cypress
            { new Color(0.18f, 0.30f, 0.10f), new Color(0.25f, 0.32f, 0.11f) }, // bush
            { new Color(0.30f, 0.27f, 0.22f), new Color(0.34f, 0.30f, 0.25f) }, // dead (no leaves)
        };

        public void Init(WorldData data)
        {
            w = data;
            nearMesh = new Mesh[TreeKind.Count];
            farMesh = new Mesh[TreeKind.Count];
            for (int k = 0; k < TreeKind.Count; k++)
            {
                nearMesh[k] = TreeMeshes.Make(k, false);
                farMesh[k] = TreeMeshes.Make(k, true);
            }
            bark = Mats.Make("Bark", new Color(0.24f, 0.18f, 0.13f), 0.08f);
            leaves = new Material[TreeKind.Count * Tints];
            for (int k = 0; k < TreeKind.Count; k++)
                for (int t = 0; t < Tints; t++)
                    leaves[k * Tints + t] = Mats.Make($"Leaves{k}_{t}", LeafColors[k, t], 0.12f);
            chunksPerSide = Mathf.CeilToInt((float)(w.WorldHalf * 2 / Chunk));
            chunks = new List<Matrix4x4>[chunksPerSide * chunksPerSide][];
            int count = 0;
            foreach (var tile in w.TreeTiles)
            {
                for (int i = 0; i < tile.count; i++)
                {
                    int kind = tile.kind[i];
                    float s = tile.scale[i];
                    var p = Frame.Pos(tile.x[i], tile.y[i] - 0.15f, tile.z[i]);
                    int cx = Mathf.Clamp((int)((tile.x[i] + w.WorldHalf) / Chunk), 0, chunksPerSide - 1);
                    int cz = Mathf.Clamp((int)((tile.z[i] + w.WorldHalf) / Chunk), 0, chunksPerSide - 1);
                    int c = cz * chunksPerSide + cx;
                    if (chunks[c] == null) chunks[c] = new List<Matrix4x4>[TreeKind.Count * Tints];
                    int tint = (tile.rank[i] + i) & 1;
                    int slot = kind * Tints + tint;
                    var list = chunks[c][slot] ?? (chunks[c][slot] = new List<Matrix4x4>());
                    float sy = s * (0.9f + 0.2f * ((tile.rank[i] * 7 % 13) / 13f));
                    list.Add(Matrix4x4.TRS(p, Quaternion.Euler(0, tile.yaw[i] * Mathf.Rad2Deg, 0), new Vector3(s, sy, s)));
                    count++;
                }
            }
            Debug.Log($"Halcyon: {count} trees in {chunksPerSide * chunksPerSide} chunks");
        }

        void Rebuild(Vector3 c)
        {
            builtAt = c;
            near.Clear();
            far.Clear();
            float n2 = NearDistance * NearDistance, f2 = FarDistance * FarDistance;
            var openNear = new Batch[TreeKind.Count * Tints];
            var openFar = new Batch[TreeKind.Count * Tints];
            for (int cz = 0; cz < chunksPerSide; cz++)
            {
                for (int cx = 0; cx < chunksPerSide; cx++)
                {
                    var slots = chunks[cz * chunksPerSide + cx];
                    if (slots == null) continue;
                    // Chunk centre in Unity space (z negated).
                    float x = (float)(-w.WorldHalf + (cx + 0.5f) * Chunk), z = -(float)(-w.WorldHalf + (cz + 0.5f) * Chunk);
                    float dx = x - c.x, dz = z - c.z;
                    float d2 = dx * dx + dz * dz;
                    float reach = Chunk * 0.71f;
                    if (d2 > (FarDistance + reach) * (FarDistance + reach)) continue;
                    for (int slot = 0; slot < slots.Length; slot++)
                    {
                        var list = slots[slot];
                        if (list == null) continue;
                        foreach (var m in list)
                        {
                            float px = m.m03 - c.x, pz = m.m23 - c.z;
                            float t2 = px * px + pz * pz;
                            if (t2 > f2) continue;
                            bool isNear = t2 < n2;
                            var open = isNear ? openNear : openFar;
                            var batches = isNear ? near : far;
                            var b = open[slot];
                            if (b == null || b.count == 1023)
                            {
                                b = open[slot] = new Batch { kind = slot / Tints, tint = slot % Tints };
                                batches.Add(b);
                            }
                            b.m[b.count++] = m;
                        }
                    }
                }
            }
        }

        void LateUpdate()
        {
            if (w == null) return;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            var c = cam.transform.position;
            if ((c - builtAt).sqrMagnitude > 48f * 48f) Rebuild(c);
            var nearShadows = CastShadowsNear ? ShadowCastingMode.On : ShadowCastingMode.Off;
            foreach (var b in near) Draw(nearMesh[b.kind], b, nearShadows);
            foreach (var b in far) Draw(farMesh[b.kind], b, ShadowCastingMode.Off);
        }

        void Draw(Mesh mesh, Batch b, ShadowCastingMode shadows)
        {
            if (b.count == 0) return;
            if (mesh.GetIndexCount(TreeMeshes.Bark) > 0)
                Graphics.DrawMeshInstanced(mesh, TreeMeshes.Bark, bark, b.m, b.count, null, shadows, true);
            if (b.kind != TreeKind.Dead && mesh.subMeshCount > 1 && mesh.GetIndexCount(TreeMeshes.Leaves) > 0)
                Graphics.DrawMeshInstanced(mesh, TreeMeshes.Leaves, leaves[b.kind * Tints + b.tint], b.m, b.count, null, shadows, true);
        }
    }
}
