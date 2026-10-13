// The GPS line on the road: glowing chevrons in the right-hand lane along
// the route, flowing on towards the destination, fading in just ahead of
// the car and out a few hundred metres on. Rebuilt each frame from the
// stretch of route ahead (a few hundred vertices); hidden during races.
using System.Collections.Generic;
using Halcyon.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Halcyon
{
    public sealed class GpsRibbon : MonoBehaviour
    {
        const double Reach = 380, Step = 3, Flow = 9, Repeat = 7;
        const double HalfWidth = 1.0, Lift = 0.14;
        /// <summary>The GPS colour (the ribbon, the route on the maps).</summary>
        public static readonly Color Tint = new Color(0.68f, 0.46f, 1f);

        Gps gps;
        Mesh mesh;
        MeshRenderer mr;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> tris = new List<int>();
        double flow;
        /// <summary>Off while racing or in a menu.</summary>
        public bool Show = true;

        public static GpsRibbon Create(Transform parent, Gps gps)
        {
            var go = new GameObject("GPS");
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<GpsRibbon>();
            r.gps = gps;
            r.mesh = new Mesh { name = "GpsRibbon" };
            r.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = r.mesh;
            r.mr = go.AddComponent<MeshRenderer>();
            // Brighter than white so the bloom catches it; seen from either side (Frame flips z).
            var m = Mats.Particle("GPS", ProceduralTextures.GpsChevrons(), new Color(Tint.r * 2.2f, Tint.g * 2.2f, Tint.b * 2.2f, 1));
            m.SetFloat("_Cull", 0f);
            r.mr.sharedMaterial = m;
            r.mr.shadowCastingMode = ShadowCastingMode.Off;
            r.mr.receiveShadows = false;
            r.mr.enabled = false;
            return r;
        }

        void LateUpdate()
        {
            var route = gps.Route;
            bool on = Show && gps.Active && route != null;
            mr.enabled = on;
            if (!on) return;
            flow = (flow + Time.deltaTime * Flow) % Repeat;
            Build(route);
        }

        static float Smooth(double a, double b, double x)
        {
            double t = System.Math.Max(0, System.Math.Min(1, (x - a) / (b - a)));
            return (float)(t * t * (3 - 2 * t));
        }

        void Build(RaceRoute r)
        {
            verts.Clear();
            uvs.Clear();
            colors.Clear();
            tris.Clear();
            double s0 = gps.Progress + 2, s1 = System.Math.Min(r.Length, gps.Progress + Reach);
            mesh.Clear();
            if (s1 - s0 < 4) return;
            int k = 0;
            for (double s = s0; ; s += Step)
            {
                bool last = s >= s1;
                if (last) s = s1;
                r.At(s, out double x, out double y, out double z, out double tx, out double tz);
                // The right-hand lane: half way out to the edge of the road on the right.
                double lane = r.Sample(r.WRight, s) * 0.5;
                double rx = -tz, rz = tx;
                double cx = x + rx * lane, cz = z + rz * lane;
                double ahead = s - gps.Progress;
                var c = new Color(1, 1, 1, Smooth(4, 14, ahead) * (1 - Smooth(Reach * 0.7, Reach, ahead)));
                float v = (float)((s - flow) / Repeat);
                verts.Add(Frame.Pos(cx - rx * HalfWidth, y + Lift, cz - rz * HalfWidth));
                verts.Add(Frame.Pos(cx + rx * HalfWidth, y + Lift, cz + rz * HalfWidth));
                uvs.Add(new Vector2(0, v));
                uvs.Add(new Vector2(1, v));
                colors.Add(c);
                colors.Add(c);
                if (k > 0)
                {
                    int a = 2 * (k - 1);
                    tris.Add(a);
                    tris.Add(a + 2);
                    tris.Add(a + 1);
                    tris.Add(a + 1);
                    tris.Add(a + 2);
                    tris.Add(a + 3);
                }
                k++;
                if (last) break;
            }
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}
