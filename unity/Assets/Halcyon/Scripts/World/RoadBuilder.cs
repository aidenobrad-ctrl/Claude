// Road meshes from the network: asphalt with painted markings per class,
// gravel shoulders, kerbed sidewalks in towns, bridge decks with parapets
// and piers, and guardrails on retaining walls along drop-offs.
using System.Collections.Generic;
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public static class RoadBuilder
    {
        // Submeshes per piece: 0 asphalt (class material), 1 shoulder, 2 concrete, 3 steel.
        const int Asphalt = 0, Shoulder = 1, Concrete = 2, Steel = 3;
        const int PieceSamples = 64;
        const float Lift = 0.03f;

        public static void Build(WorldData w, Transform parent)
        {
            var root = new GameObject("Roads").transform;
            root.SetParent(parent, false);
            ProceduralTextures.Gravel(out var ga, out var gn);
            ProceduralTextures.Concrete(out var ca, out var cn);
            var shoulder = Mats.Make("Shoulder", Color.white, 0.08f, 0f, ga, gn);
            shoulder.SetTextureScale("_BaseMap", Vector2.one);
            var concrete = Mats.Make("Concrete", Color.white, 0.15f, 0f, ca, cn);
            var steel = Mats.Make("Guardrail", new Color(0.72f, 0.74f, 0.76f), 0.62f, 0.85f);
            var classMats = new Material[w.Classes.Length];
            for (int c = 0; c < w.Classes.Length; c++)
            {
                var info = w.Classes[c];
                bool dirt = info.surface == Surface.Dirt;
                var tex = ProceduralTextures.Road(info, dirt, out var nrm);
                classMats[c] = Mats.Make("Road_" + info.id, Color.white, dirt ? 0.06f : 0.22f, 0f, tex, nrm);
            }
            // Rails by edge, for the walls and beams.
            var railsByEdge = new Dictionary<int, List<RailRun>>();
            foreach (var run in w.Rails)
            {
                if (!railsByEdge.TryGetValue(run.edge, out var l)) railsByEdge[run.edge] = l = new List<RailRun>();
                l.Add(run);
            }
            foreach (var e in w.Edges)
            {
                for (int i0 = 0; i0 < e.n - 1; i0 += PieceSamples)
                {
                    int i1 = Mathf.Min(e.n - 1, i0 + PieceSamples);
                    var mb = new MeshBuilder(4);
                    Ribbon(e, i0, i1, mb);
                    Bridges(e, i0, i1, mb, w);
                    if (railsByEdge.TryGetValue(e.id, out var runs))
                        foreach (var run in runs) Rail(w, e, run, i0, i1, mb);
                    if (mb.Empty) continue;
                    mb.ToObject($"Road_{e.id}_{i0}", root, new[] { classMats[e.cls], shoulder, concrete, steel });
                }
            }
        }

        static Vector3 At(RoadEdge e, int i, double lat, double dy) =>
            Frame.Pos(e.x[i] - e.tz[i] * lat, e.y[i] + dy, e.z[i] + e.tx[i] * lat);

        static Vector3 UpNormal(RoadEdge e, int i)
        {
            int a = Mathf.Max(0, i - 1), b = Mathf.Min(e.n - 1, i + 1);
            double ds = e.s[b] - e.s[a];
            if (ds == 0) ds = 1;
            double gy = (e.y[b] - e.y[a]) / ds;
            double l = System.Math.Sqrt(1 + gy * gy);
            return Frame.Dir(-e.tx[i] * gy / l, 1 / l, -e.tz[i] * gy / l);
        }

        /// <summary>The paved band plus shoulders or kerbed sidewalks.</summary>
        static void Ribbon(RoadEdge e, int i0, int i1, MeshBuilder mb)
        {
            var info = e.info;
            double hw = info.halfWidth;
            double W = hw + info.shoulder;
            float curb = 0.12f;
            int prevRow = -1;
            for (int i = i0; i <= i1; i++)
            {
                var n = UpNormal(e, i);
                float v = (float)(e.s[i] / 12.0);
                bool bridge = e.bridge[i] == 1;
                int row = mb.Count;
                // Asphalt: left edge, centre, right edge.
                mb.Add(At(e, i, -hw, Lift), n, new Vector2(0, v));
                mb.Add(At(e, i, 0, Lift), n, new Vector2(0.5f, v));
                mb.Add(At(e, i, hw, Lift), n, new Vector2(1, v));
                if (info.sidewalk)
                {
                    // Kerb faces and sidewalk tops on both sides (concrete).
                    var sideL = Frame.Dir(e.tz[i], 0, -e.tx[i]);
                    mb.Add(At(e, i, -hw, Lift), sideL * -1f, new Vector2(0, v));
                    mb.Add(At(e, i, -hw, Lift + curb), sideL * -1f, new Vector2(0.1f, v));
                    mb.Add(At(e, i, -W, Lift + curb), n, new Vector2((float)(W - hw) / 3f, v * 4));
                    mb.Add(At(e, i, hw, Lift), sideL, new Vector2(0, v));
                    mb.Add(At(e, i, hw, Lift + curb), sideL, new Vector2(0.1f, v));
                    mb.Add(At(e, i, W, Lift + curb), n, new Vector2((float)(W - hw) / 3f, v * 4));
                    mb.Add(At(e, i, -hw, Lift + curb), n, new Vector2(0, v * 4));
                }
                else
                {
                    // Shoulders: the outer edge dips to meet the cut terrain (not on bridges).
                    float dip = bridge ? 0f : -0.07f;
                    mb.Add(At(e, i, -W, Lift + dip), n, new Vector2(0, v * 3));
                    mb.Add(At(e, i, -hw, Lift), n, new Vector2((float)info.shoulder / 3f, v * 3));
                    mb.Add(At(e, i, hw, Lift), n, new Vector2(0, v * 3));
                    mb.Add(At(e, i, W, Lift + dip), n, new Vector2((float)info.shoulder / 3f, v * 3));
                }
                if (prevRow >= 0)
                {
                    int p = prevRow, c = row;
                    // Asphalt (left to right across, forward along): faces up.
                    mb.Quad(Asphalt, p + 0, c + 0, c + 1, p + 1);
                    mb.Quad(Asphalt, p + 1, c + 1, c + 2, p + 2);
                    if (info.sidewalk)
                    {
                        // Left kerb face looks right (into the road), right kerb face looks left.
                        mb.Quad(Concrete, p + 4, c + 4, c + 3, p + 3);
                        mb.Quad(Concrete, p + 6, c + 6, c + 7, p + 7);
                        // Sidewalk tops.
                        mb.Quad(Concrete, p + 5, c + 5, c + 9, p + 9);
                        mb.Quad(Concrete, p + 7, c + 7, c + 8, p + 8);
                    }
                    else
                    {
                        mb.Quad(Shoulder, p + 3, c + 3, c + 4, p + 4);
                        mb.Quad(Shoulder, p + 5, c + 5, c + 6, p + 6);
                    }
                }
                prevRow = row;
            }
        }

        /// <summary>Bridge decks: underside, side faces, parapets and piers.</summary>
        static void Bridges(RoadEdge e, int i0, int i1, MeshBuilder mb, WorldData w)
        {
            var info = e.info;
            double W = info.halfWidth + info.shoulder;
            const double depth = 1.1, parapetH = 0.95, parapetW = 0.32;
            double sincePier = 0;
            for (int i = i0; i < i1; i++)
            {
                if (e.bridge[i] == 0 || e.bridge[i + 1] == 0) continue;
                var uv = new Vector2(0.25f, 0.25f);
                // Underside.
                mb.FlatQuad(Concrete, At(e, i, -W, -depth), At(e, i, W, -depth), At(e, i + 1, W, -depth), At(e, i + 1, -W, -depth), uv);
                // Outer faces and parapets on both sides.
                foreach (int side in new[] { -1, 1 })
                {
                    double o = W * side, inner = (W - parapetW) * side;
                    // Quads ordered (i, i+1) face left, (i+1, i) face right.
                    if (side > 0)
                    {
                        mb.FlatQuad(Concrete, At(e, i + 1, o, -depth), At(e, i, o, -depth), At(e, i, o, parapetH), At(e, i + 1, o, parapetH), uv);
                        mb.FlatQuad(Concrete, At(e, i, inner, Lift), At(e, i + 1, inner, Lift), At(e, i + 1, inner, parapetH), At(e, i, inner, parapetH), uv);
                    }
                    else
                    {
                        mb.FlatQuad(Concrete, At(e, i, o, -depth), At(e, i + 1, o, -depth), At(e, i + 1, o, parapetH), At(e, i, o, parapetH), uv);
                        mb.FlatQuad(Concrete, At(e, i + 1, inner, Lift), At(e, i, inner, Lift), At(e, i, inner, parapetH), At(e, i + 1, inner, parapetH), uv);
                    }
                    double a = System.Math.Min(o, inner), b = System.Math.Max(o, inner);
                    mb.FlatQuad(Concrete, At(e, i, a, parapetH), At(e, i + 1, a, parapetH), At(e, i + 1, b, parapetH), At(e, i, b, parapetH), uv);
                }
                // Piers every ~28 m down to the ground or lake/river bed.
                sincePier += e.s[i + 1] - e.s[i];
                if (sincePier >= 28)
                {
                    sincePier = 0;
                    double bottom = System.Math.Min(e.baseY[i], w.TerrainHeight(e.x[i], e.z[i])) - 1.5;
                    double top = e.y[i] - depth;
                    if (top - bottom > 0.5)
                    {
                        var c = Frame.Pos(e.x[i], (top + bottom) / 2, e.z[i]);
                        var rot = Quaternion.LookRotation(Frame.Dir(e.tx[i], 0, e.tz[i]), Vector3.up);
                        mb.Box(Concrete, c, rot, new Vector3((float)(W * 1.2), (float)(top - bottom), 1.4f), 0.25f);
                    }
                }
            }
        }

        /// <summary>A guardrail run: W-beam on posts, with a retaining wall where the ground drops away.</summary>
        static void Rail(WorldData w, RoadEdge e, RailRun run, int i0, int i1, MeshBuilder mb)
        {
            int a = Mathf.Max(i0, run.i0), b = Mathf.Min(i1, run.i1);
            if (b <= a) return;
            double off = run.offset * run.side;
            double W = e.info.halfWidth + e.info.shoulder;
            double since = 0;
            var uv = new Vector2(0.25f, 0.25f);
            for (int i = a; i < b; i++)
            {
                // Beam: inner face towards the road, outer face away.
                Vector3 p0 = At(e, i, off, 0.5), p1 = At(e, i + 1, off, 0.5), q0 = At(e, i, off, 0.82), q1 = At(e, i + 1, off, 0.82);
                if (run.side > 0)
                {
                    mb.FlatQuad(Steel, p1, p0, q0, q1, uv);
                    mb.FlatQuad(Steel, p0, p1, q1, q0, uv);
                }
                else
                {
                    mb.FlatQuad(Steel, p0, p1, q1, q0, uv);
                    mb.FlatQuad(Steel, p1, p0, q0, q1, uv);
                }
                since += e.s[i + 1] - e.s[i];
                if (since >= 2)
                {
                    since = 0;
                    var post = At(e, i, off + run.side * 0.12, 0.42);
                    var rot = Quaternion.LookRotation(Frame.Dir(e.tx[i], 0, e.tz[i]), Vector3.up);
                    mb.Box(Steel, post, rot, new Vector3(0.12f, 0.85f, 0.16f));
                }
                // Retaining wall down to the ground where it falls away.
                double o2 = off + run.side * 0.2;
                double f0 = WallFoot(w, e, i, o2), f1 = WallFoot(w, e, i + 1, o2);
                if (f0 < e.y[i] - 0.3 || f1 < e.y[i + 1] - 0.3)
                {
                    double top0 = -0.07, top1 = -0.07;
                    Vector3 t0 = At(e, i, o2, top0), t1 = At(e, i + 1, o2, top1);
                    Vector3 b0 = Frame.Pos(e.x[i] - e.tz[i] * o2, System.Math.Min(f0, e.y[i] + top0), e.z[i] + e.tx[i] * o2);
                    Vector3 b1 = Frame.Pos(e.x[i + 1] - e.tz[i + 1] * o2, System.Math.Min(f1, e.y[i + 1] + top1), e.z[i + 1] + e.tx[i + 1] * o2);
                    // The wall faces away from the road.
                    if (run.side > 0) mb.FlatQuad(Concrete, b1, b0, t0, t1, uv);
                    else mb.FlatQuad(Concrete, b0, b1, t1, t0, uv);
                    // Deck strip from the shoulder edge out to the wall, if the rail stands beyond it.
                    if (System.Math.Abs(o2) > W)
                    {
                        double inner = W * run.side;
                        Vector3 s0 = At(e, i, inner, -0.07), s1 = At(e, i + 1, inner, -0.07);
                        if (run.side > 0) mb.FlatQuad(Concrete, s0, s1, t1, t0, uv);
                        else mb.FlatQuad(Concrete, t0, t1, s1, s0, uv);
                    }
                }
            }
        }

        static double WallFoot(WorldData w, RoadEdge e, int i, double lat) =>
            w.TerrainHeight(e.x[i] - e.tz[i] * lat, e.z[i] + e.tx[i] * lat) - 0.6;
    }
}
