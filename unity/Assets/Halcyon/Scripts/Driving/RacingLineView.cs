// The racing line on the road in a race (Settings.Line): the line the AI
// drives on this route, a stretch ahead of the player, coloured by the
// player's speed against what their car can carry there. Good where they
// can keep accelerating, warning where they should lift, bad where they
// must brake now; the colours follow the colour-blind setting. "Braking
// only" draws only the lift and brake stretches. The targets come from the
// AI's speed profile for the player's own car, with a little more margin.
using System.Collections.Generic;
using Halcyon.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Halcyon
{
    public sealed class RacingLineView : MonoBehaviour
    {
        const double Reach = 170, Step = 2, HalfWidth = 0.42, Lift = 0.11;

        HalcyonGame game;
        RaceController races;
        Mesh mesh;
        MeshRenderer mr;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> tris = new List<int>();
        float flow;

        public static RacingLineView Create(HalcyonGame game, RaceController races, Transform parent)
        {
            var go = new GameObject("RacingLine");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<RacingLineView>();
            v.game = game;
            v.races = races;
            v.mesh = new Mesh { name = "RacingLine" };
            v.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = v.mesh;
            v.mr = go.AddComponent<MeshRenderer>();
            var m = Mats.Particle("RacingLine", ProceduralTextures.GpsChevrons(), new Color(1.6f, 1.6f, 1.6f, 1));
            m.SetFloat("_Cull", 0f);
            v.mr.sharedMaterial = m;
            v.mr.shadowCastingMode = ShadowCastingMode.Off;
            v.mr.receiveShadows = false;
            v.mr.enabled = false;
            return v;
        }

        static float Smooth(double a, double b, double x)
        {
            double t = System.Math.Max(0, System.Math.Min(1, (x - a) / (b - a)));
            return (float)(t * t * (3 - 2 * t));
        }

        void LateUpdate()
        {
            var s = races.Session;
            var mode = game.Settings != null ? game.Settings.Line : LineAssist.Off;
            bool on = s != null && races.PlayerSpeeds != null && races.PlayerAbility != null && mode != LineAssist.Off &&
                      !game.HideHud && s.State != RaceState.Finished && s.PlayerEntrant != null && !s.PlayerEntrant.Finished;
            mr.enabled = on;
            if (!on) return;
            if (!game.Paused) flow = (flow + Time.deltaTime * 4) % 6;
            Build(s, races.PlayerSpeeds, races.PlayerAbility, mode == LineAssist.Braking);
        }

        void Build(RaceSession session, SpeedProfile speeds, CarAbility car, bool brakingOnly)
        {
            verts.Clear();
            uvs.Clear();
            colors.Clear();
            tris.Clear();
            mesh.Clear();
            var line = speeds.Line;
            var r = line.Route;
            var e = session.PlayerEntrant;
            double v = System.Math.Max(0, e.Car.vehicle.vLong);
            double s0 = e.S;
            double brake = System.Math.Max(3, car.BrakeMax(v) * 0.9);
            Color good = Ui.Good, warn = Ui.Warn, bad = Ui.Bad;
            int k = 0;
            for (double d = 2; d <= Reach; d += Step)
            {
                double s = s0 + d;
                if (!r.Closed && s > r.Length) break;
                double target = speeds.At(s);
                // The braking this point asks for: from the speed now to its target in the distance to it.
                double need = (v * v - target * target) / (2 * d);
                Color c = need > 0.5 * brake ? bad : need > 0.12 * brake || v > target + 1 ? warn : good;
                float a = Smooth(2, 9, d) * (1 - Smooth(Reach * 0.7, Reach, d));
                if (brakingOnly && c == good) a = 0;
                c.a = a;
                r.At(s, out double cx, out double y, out double cz, out double tx, out double tz);
                double off = line.OffsetAt(s);
                double rx = -tz, rz = tx;
                double x = cx + rx * off, z = cz + rz * off;
                float uv = (float)((s - flow) / 6);
                verts.Add(Frame.Pos(x - rx * HalfWidth, y + Lift, z - rz * HalfWidth));
                verts.Add(Frame.Pos(x + rx * HalfWidth, y + Lift, z + rz * HalfWidth));
                uvs.Add(new Vector2(0, uv));
                uvs.Add(new Vector2(1, uv));
                colors.Add(c);
                colors.Add(c);
                if (k > 0)
                {
                    int i = 2 * (k - 1);
                    tris.Add(i);
                    tris.Add(i + 2);
                    tris.Add(i + 1);
                    tris.Add(i + 1);
                    tris.Add(i + 2);
                    tris.Add(i + 3);
                }
                k++;
            }
            if (k < 2) return;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}
