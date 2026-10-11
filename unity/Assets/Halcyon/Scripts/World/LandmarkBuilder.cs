// The island's landmarks (Halcyon.Core.Landmarks) as models made in code:
// a lattice radio mast with red warning lights, a banded lighthouse whose
// beam sweeps the sea at night, wind turbines turning with the wind, a
// sandstone arch in the desert and a domed observatory. A LandmarkAnimator
// turns the blades and the beam and blinks the lights from the sky's state.
using System.Collections.Generic;
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public static class LandmarkBuilder
    {
        const int White = 0, Red = 1, Concrete = 2, Steel = 3, Stone = 4, Glass = 5, Lamp = 6, Subs = 7;

        public static LandmarkAnimator Build(List<Landmark> list, Transform parent)
        {
            var root = new GameObject("Landmarks").transform;
            root.SetParent(parent, false);
            var mats = new Material[Subs];
            mats[White] = Mats.Make("LandmarkWhite", new Color(0.92f, 0.92f, 0.9f), 0.45f);
            mats[Red] = Mats.Make("LandmarkRed", new Color(0.72f, 0.1f, 0.08f), 0.4f);
            mats[Concrete] = Mats.Make("LandmarkConcrete", new Color(0.6f, 0.6f, 0.58f), 0.15f);
            mats[Steel] = Mats.Make("LandmarkSteel", new Color(0.55f, 0.57f, 0.6f), 0.55f, 0.8f);
            mats[Stone] = Mats.Make("LandmarkStone", new Color(0.66f, 0.36f, 0.22f), 0.08f);
            mats[Glass] = Mats.Make("LandmarkGlass", new Color(0.2f, 0.28f, 0.32f), 0.95f, 0.3f);
            mats[Lamp] = Mats.Make("LandmarkLamp", new Color(1f, 0.95f, 0.8f), 0.6f);
            var anim = root.gameObject.AddComponent<LandmarkAnimator>();
            anim.LampMat = mats[Lamp];
            foreach (var l in list)
            {
                var go = new GameObject(l.Name);
                go.transform.SetParent(root, false);
                go.transform.position = Frame.Pos(l.X, l.Y, l.Z);
                // Facing the road (Core yaw 0 faces -Z: north).
                go.transform.rotation = Quaternion.LookRotation(Frame.Dir(-System.Math.Sin(l.Yaw), 0, -System.Math.Cos(l.Yaw)));
                switch (l.Kind)
                {
                    case Landmarks.Mast: Mast(go.transform, mats, anim); break;
                    case Landmarks.Lighthouse: Lighthouse(go.transform, mats, anim); break;
                    case Landmarks.Observatory: Observatory(go.transform, mats); break;
                    case Landmarks.Arch: Arch(go.transform, mats); break;
                    case Landmarks.WindFarm:
                        int k = 0;
                        foreach (var p in l.Parts)
                        {
                            var t = new GameObject("Turbine" + k).transform;
                            t.SetParent(root, false);
                            t.position = Frame.Pos(p.x, p.y, p.z);
                            // Turbines face the prevailing westerly.
                            t.rotation = Quaternion.Euler(0, -90 + k * 3, 0);
                            Turbine(t, mats, anim, k++);
                        }
                        break;
                }
            }
            return anim;
        }

        // --- Shapes ------------------------------------------------------------------------------

        /// <summary>A tapered cylinder (frustum) between two heights, with caps.</summary>
        static void Cylinder(MeshBuilder mb, int sub, Vector3 c, float y0, float y1, float r0, float r1, int segs = 24, bool caps = true)
        {
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2 / segs, a1 = (i + 1) * Mathf.PI * 2 / segs;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                var n0 = (d0 * (y1 - y0) + Vector3.up * (r0 - r1)).normalized;
                var n1 = (d1 * (y1 - y0) + Vector3.up * (r0 - r1)).normalized;
                int a = mb.Add(c + d0 * r0 + Vector3.up * y0, n0, new Vector2(i / (float)segs, 0));
                int b = mb.Add(c + d1 * r0 + Vector3.up * y0, n1, new Vector2((i + 1) / (float)segs, 0));
                int t1 = mb.Add(c + d1 * r1 + Vector3.up * y1, n1, new Vector2((i + 1) / (float)segs, 1));
                int t0 = mb.Add(c + d0 * r1 + Vector3.up * y1, n0, new Vector2(i / (float)segs, 1));
                mb.Quad(sub, a, t0, t1, b);
                if (caps)
                {
                    int ct = mb.Add(c + Vector3.up * y1, Vector3.up, new Vector2(0.5f, 0.5f));
                    mb.Tri(sub, ct, mb.Add(c + d1 * r1 + Vector3.up * y1, Vector3.up, Vector2.zero), mb.Add(c + d0 * r1 + Vector3.up * y1, Vector3.up, Vector2.zero));
                }
            }
        }

        /// <summary>A dome (upper hemisphere) of radius r sitting at height y.</summary>
        static void Dome(MeshBuilder mb, int sub, Vector3 c, float y, float r, int segs = 24, int rings = 8)
        {
            for (int j = 0; j < rings; j++)
            {
                float p0 = j * Mathf.PI / 2 / rings, p1 = (j + 1) * Mathf.PI / 2 / rings;
                for (int i = 0; i < segs; i++)
                {
                    float a0 = i * Mathf.PI * 2 / segs, a1 = (i + 1) * Mathf.PI * 2 / segs;
                    Vector3 V(float a, float p) => new Vector3(Mathf.Cos(a) * Mathf.Cos(p), Mathf.Sin(p), Mathf.Sin(a) * Mathf.Cos(p));
                    Vector3 q00 = V(a0, p0), q10 = V(a1, p0), q11 = V(a1, p1), q01 = V(a0, p1);
                    int ia = mb.Add(c + Vector3.up * y + q00 * r, q00, Vector2.zero);
                    int ib = mb.Add(c + Vector3.up * y + q10 * r, q10, Vector2.right);
                    int ic = mb.Add(c + Vector3.up * y + q11 * r, q11, Vector2.one);
                    int id = mb.Add(c + Vector3.up * y + q01 * r, q01, Vector2.up);
                    mb.Quad(sub, ia, id, ic, ib);
                }
            }
        }

        static GameObject Emitter(Transform parent, string name, Vector3 local, float size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = Vector3.one * size;
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // --- The landmarks -------------------------------------------------------------------------

        static void Mast(Transform t, Material[] mats, LandmarkAnimator anim)
        {
            // A 60 m triangular lattice in red and white bands on a concrete pad.
            var mb = new MeshBuilder(Subs);
            mb.Box(Concrete, new Vector3(0, 0.3f, 0), Quaternion.identity, new Vector3(9, 0.8f, 9));
            const float H = 60, w0 = 4.2f, w1 = 1.1f;
            Vector3 Leg(int k, float y)
            {
                float w = Mathf.Lerp(w0, w1, y / H);
                float a = k * Mathf.PI * 2 / 3;
                return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * w + Vector3.up * y;
            }
            for (int seg = 0; seg < 12; seg++)
            {
                float y0 = seg * H / 12, y1 = (seg + 1) * H / 12;
                int band = seg % 2 == 0 ? Red : White;
                for (int k = 0; k < 3; k++)
                {
                    Strut(mb, band, Leg(k, y0), Leg(k, y1), 0.35f);
                    Strut(mb, Steel, Leg(k, y0), Leg((k + 1) % 3, y1), 0.12f); // bracing
                    Strut(mb, Steel, Leg(k, y1), Leg((k + 1) % 3, y1), 0.12f);
                }
            }
            Cylinder(mb, White, new Vector3(0, 0, 0), H, H + 6, 0.25f, 0.08f, 8);
            mb.ToObject("Mast", t, mats);
            var red = Mats.Make("MastLight", new Color(1f, 0.1f, 0.05f), 0.5f);
            anim.Blinkers.Add((Emitter(t, "TopLight", new Vector3(0, H + 6.2f, 0), 0.9f, red).GetComponent<Renderer>(), red, 0f));
            anim.Blinkers.Add((Emitter(t, "MidLight", new Vector3(0, H * 0.5f, w0 * 0.6f), 0.7f, red).GetComponent<Renderer>(), red, 0.5f));
        }

        static void Strut(MeshBuilder mb, int sub, Vector3 a, Vector3 b, float thick)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            mb.Box(sub, (a + b) / 2, Quaternion.LookRotation(d), new Vector3(thick, thick, d.magnitude));
        }

        static void Lighthouse(Transform t, Material[] mats, LandmarkAnimator anim)
        {
            var mb = new MeshBuilder(Subs);
            Cylinder(mb, Concrete, Vector3.zero, -1, 1.2f, 5.2f, 5.2f, 28);
            // Tower: white with two red bands, tapering from 3.6 to 2.4 m radius over 22 m.
            float[] ys = { 1.2f, 7, 10, 15, 18, 23 };
            for (int i = 0; i + 1 < ys.Length; i++)
            {
                float r0 = Mathf.Lerp(3.6f, 2.4f, (ys[i] - 1.2f) / 22), r1 = Mathf.Lerp(3.6f, 2.4f, (ys[i + 1] - 1.2f) / 22);
                Cylinder(mb, i % 2 == 1 ? Red : White, Vector3.zero, ys[i], ys[i + 1], r0, r1, 28, false);
            }
            Cylinder(mb, Steel, Vector3.zero, 23, 23.4f, 3.2f, 3.2f, 28); // gallery
            Cylinder(mb, Glass, Vector3.zero, 23.4f, 26.2f, 1.9f, 1.9f, 16, false); // lantern room
            Dome(mb, Red, Vector3.zero, 26.2f, 2.1f, 16, 5);
            mb.ToObject("Lighthouse", t, mats);
            Emitter(t, "Lamp", new Vector3(0, 24.8f, 0), 1.6f, anim.LampMat);
            // The beam: a spotlight and a soft cone, turning once every 10 s.
            var beam = new GameObject("Beam").transform;
            beam.SetParent(t, false);
            beam.localPosition = new Vector3(0, 24.8f, 0);
            var light = beam.gameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.range = 900;
            light.spotAngle = 9;
            light.innerSpotAngle = 3;
            light.intensity = 0;
            light.color = new Color(1f, 0.95f, 0.82f);
            light.shadows = LightShadows.None;
            var cone = new MeshBuilder(1);
            const float len = 320, rad = 18;
            int tip = cone.Add(Vector3.zero, Vector3.up, Vector2.zero);
            for (int i = 0; i < 12; i++)
            {
                float a0 = i * Mathf.PI * 2 / 12, a1 = (i + 1) * Mathf.PI * 2 / 12;
                int p = cone.Add(new Vector3(Mathf.Cos(a0) * rad, Mathf.Sin(a0) * rad, len), Vector3.up, new Vector2(0.5f, 1));
                int q = cone.Add(new Vector3(Mathf.Cos(a1) * rad, Mathf.Sin(a1) * rad, len), Vector3.up, new Vector2(0.5f, 1));
                cone.Tri(0, tip, p, q);
                cone.Tri(0, tip, q, p);
            }
            var beamMat = Mats.UnlitTransparent("LighthouseBeam", null, new Color(1f, 0.95f, 0.8f, 0));
            cone.ToObject("Cone", beam, new[] { beamMat }, false);
            anim.Beams.Add((beam, light, beamMat));
        }

        static void Observatory(Transform t, Material[] mats)
        {
            var mb = new MeshBuilder(Subs);
            Cylinder(mb, Concrete, Vector3.zero, -1, 0.6f, 11, 11, 32); // terrace
            Cylinder(mb, White, Vector3.zero, 0.6f, 8, 7.5f, 7.5f, 32, false); // drum
            Dome(mb, Steel, Vector3.zero, 8, 7.6f, 32, 10);
            // The shutter slit: a dark band over the dome.
            mb.Box(Glass, new Vector3(0, 12.5f, 2.2f), Quaternion.Euler(-30, 0, 0), new Vector3(1.6f, 0.2f, 8));
            // A low wing with windows toward the road.
            mb.Box(White, new Vector3(0, 2.2f, -11), Quaternion.identity, new Vector3(10, 3.8f, 6));
            mb.Box(Glass, new Vector3(0, 2.4f, -14.05f), Quaternion.identity, new Vector3(7, 1.4f, 0.1f));
            mb.ToObject("Observatory", t, mats);
        }

        static void Arch(Transform t, Material[] mats)
        {
            // A natural sandstone arch: two weathered pillars 16 m apart and a span 18 m up, built
            // from boxes along a semicircle, a little uneven.
            var mb = new MeshBuilder(Subs);
            const int n = 18;
            const float span = 8, rise = 16;
            var rng = new System.Random(17);
            for (int i = 0; i < n; i++)
            {
                float a0 = Mathf.PI * i / n, a1 = Mathf.PI * (i + 1) / n;
                var p0 = new Vector3(Mathf.Cos(a0) * span, Mathf.Sin(a0) * rise + 2, 0);
                var p1 = new Vector3(Mathf.Cos(a1) * span, Mathf.Sin(a1) * rise + 2, 0);
                float thick = 3.4f + (float)rng.NextDouble() * 0.8f - Mathf.Sin(a0) * 1.2f;
                mb.Box(Stone, (p0 + p1) / 2, Quaternion.LookRotation(Vector3.forward, (p1 - p0).normalized) * Quaternion.Euler(0, 0, 0), new Vector3(thick, (p1 - p0).magnitude + 0.6f, 4.2f - Mathf.Sin(a0) * 1.0f));
            }
            // Pillars down to the ground, wider at the foot.
            mb.Box(Stone, new Vector3(span, 0.5f, 0), Quaternion.identity, new Vector3(5, 5, 6));
            mb.Box(Stone, new Vector3(-span, 0.5f, 0), Quaternion.identity, new Vector3(5.4f, 5, 6.4f));
            mb.ToObject("Arch", t, mats);
        }

        static void Turbine(Transform t, Material[] mats, LandmarkAnimator anim, int k)
        {
            var mb = new MeshBuilder(Subs);
            Cylinder(mb, Concrete, Vector3.zero, -1, 0.5f, 3, 3, 16);
            Cylinder(mb, White, Vector3.zero, 0.5f, 58, 1.9f, 1.1f, 20, false);
            mb.Box(White, new Vector3(0, 59.2f, -1.5f), Quaternion.identity, new Vector3(2.4f, 2.6f, 7));
            mb.ToObject("Tower", t, mats);
            var hub = new GameObject("Rotor").transform;
            hub.SetParent(t, false);
            hub.localPosition = new Vector3(0, 59.2f, 2.3f);
            var rb = new MeshBuilder(Subs);
            Dome(rb, White, Vector3.zero, -0.2f, 1.2f, 12, 4);
            for (int b = 0; b < 3; b++)
            {
                var r = Quaternion.Euler(0, 0, b * 120);
                // A blade: 26 m, wide at the root, thin at the tip.
                var root = r * new Vector3(0, 1, 0);
                for (int s = 0; s < 4; s++)
                {
                    float y0 = 1 + s * 6.5f, y1 = 1 + (s + 1) * 6.5f;
                    float w = Mathf.Lerp(1.8f, 0.5f, (s + 0.5f) / 4);
                    rb.Box(White, r * new Vector3(0, (y0 + y1) / 2, 0), r * Quaternion.Euler(0, 12, 0), new Vector3(w, y1 - y0, 0.25f));
                }
            }
            rb.ToObject("Blades", hub, mats);
            hub.localRotation = Quaternion.Euler(0, 0, k * 37);
            anim.Rotors.Add(hub);
        }
    }

    /// <summary>Turns the turbines with the wind, sweeps the lighthouse beam and blinks the mast's lights at night.</summary>
    public sealed class LandmarkAnimator : MonoBehaviour
    {
        public readonly List<Transform> Rotors = new List<Transform>();
        public readonly List<(Transform t, Light light, Material cone)> Beams = new List<(Transform, Light, Material)>();
        public readonly List<(Renderer r, Material m, float phase)> Blinkers = new List<(Renderer, Material, float)>();
        public Material LampMat;
        public Simulation Sim;

        void Update()
        {
            float dt = Time.deltaTime;
            float night = SkyDirector.Instance != null ? SkyDirector.Instance.Night : 0;
            float wind = Sim != null ? (float)Sim.Weather.Wind : 4;
            // Blades: about 2 turns a minute in a light wind, 12 in a gale.
            float spin = Mathf.Clamp(wind * 4.5f, 6, 70) * dt;
            foreach (var r in Rotors) r.Rotate(0, 0, -spin, Space.Self);
            float lit = Mathf.Clamp01(night * 1.4f - 0.2f);
            foreach (var b in Beams)
            {
                b.t.Rotate(0, 36 * dt, 0, Space.World);
                b.light.intensity = 40 * lit;
                b.light.enabled = lit > 0.01f;
                b.cone.SetColor("_BaseColor", new Color(1f, 0.95f, 0.8f, 0.07f * lit));
                b.cone.SetColor("_Color", new Color(1f, 0.95f, 0.8f, 0.07f * lit));
            }
            if (LampMat != null) Mats.SetEmission(LampMat, new Color(1f, 0.92f, 0.7f) * (0.2f + 6f * lit));
            // Warning lights: a 1 s red flash every 2 s, brighter at night.
            float t = Time.time;
            foreach (var bl in Blinkers)
            {
                bool on = Mathf.Repeat(t * 0.5f + bl.phase, 1f) < 0.5f;
                Mats.SetEmission(bl.m, new Color(1f, 0.08f, 0.04f) * (on ? (1 + 7 * night) : 0.1f));
            }
        }
    }
}
