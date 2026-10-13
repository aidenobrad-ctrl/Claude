// The Halcyon Festival on its plateau: main stage, the big wheel, the
// entrance arch, pavilion tents and flag lines along the festival road; the
// cars on show on lit plinths, the car park, food trucks, the stage's light
// towers, crowd barriers and the crowds (from the shared layout, as on the web).
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public sealed class FestivalBuilder : MonoBehaviour
    {
        Transform wheel;
        Material[] colors;

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        public static FestivalBuilder Build(WorldData w, Transform parent)
        {
            var go = new GameObject("Festival");
            go.transform.SetParent(parent, false);
            var fb = go.AddComponent<FestivalBuilder>();
            fb.Make(w.Festival);
            return fb;
        }

        void Make(FestivalLayout f)
        {
            var steel = Mats.Make("FestivalSteel", new Color(0.82f, 0.84f, 0.86f), 0.55f, 0.8f);
            var dark = Mats.Make("FestivalDark", new Color(0.08f, 0.08f, 0.09f), 0.3f);
            colors = new Material[f.colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = Mats.Make("Festival" + i, Hex(f.colors[i]), 0.35f);
                Mats.SetEmission(colors[i], Hex(f.colors[i]) * 0.15f);
                // The festival lights up at night.
                SkyDirector.Glow(colors[i], Hex(f.colors[i]) * 0.15f, Hex(f.colors[i]) * 1.4f);
            }
            Material ColorOf(int rgb)
            {
                for (int i = 0; i < f.colors.Length; i++) if (f.colors[i] == rgb) return colors[i];
                return colors[0];
            }

            // Stage: platform, back wall, roof on four towers, a glowing banner.
            {
                var p = f.stage;
                var mb = new MeshBuilder(3);
                Frame3(p, out var c, out var rot, out var fw, out var rt);
                float W = (float)p.w, D = (float)p.d, H = (float)p.h;
                mb.Box(1, c + Vector3.up * 0.7f, rot, new Vector3(W, 1.4f, D));
                mb.Box(1, c + Vector3.up * (H * 0.5f) - fw * (D / 2 - 0.4f), rot, new Vector3(W, H, 0.8f));
                foreach (int sx in new[] { -1, 1 })
                    foreach (int sz in new[] { -1, 1 })
                        mb.Box(0, c + rt * (sx * (W / 2 - 0.5f)) + fw * (sz * (D / 2 - 0.5f)) + Vector3.up * (H / 2), rot, new Vector3(0.8f, H, 0.8f));
                mb.Box(0, c + Vector3.up * (H + 0.6f), rot, new Vector3(W + 1.5f, 1.2f, D + 1.5f));
                mb.Box(2, c + Vector3.up * (H - 2.0f) + fw * (D / 2 - 0.3f), rot, new Vector3(W * 0.8f, 2.4f, 0.3f));
                mb.ToObject("Stage", transform, new[] { steel, dark, ColorOf(p.color) });
            }

            // The big wheel: rim, spokes, gondolas; spins slowly.
            {
                var p = f.wheel;
                Frame3(p, out var c, out var rot, out var fw, out var rt);
                float R = (float)p.h / 2;
                var hub = c + Vector3.up * (R + 2f);
                var legs = new MeshBuilder(1);
                foreach (int sx in new[] { -1, 1 })
                    foreach (int sz in new[] { -1, 1 })
                    {
                        var foot = c + rt * (sx * R * 0.45f) + fw * (sz * 2.2f);
                        Strut(legs, 0, foot, hub, 0.35f);
                    }
                legs.ToObject("WheelLegs", transform, new[] { steel });
                var wheelGo = new GameObject("BigWheel");
                wheelGo.transform.SetParent(transform, false);
                wheelGo.transform.position = hub;
                wheelGo.transform.rotation = rot;
                wheel = wheelGo.transform;
                var mb = new MeshBuilder(2 + colors.Length);
                const int n = 32;
                for (int i = 0; i < n; i++)
                {
                    float a0 = i * Mathf.PI * 2 / n, a1 = (i + 1) * Mathf.PI * 2 / n;
                    foreach (float z in new[] { -0.9f, 0.9f })
                    {
                        var p0 = new Vector3(Mathf.Cos(a0) * R, Mathf.Sin(a0) * R, z);
                        var p1 = new Vector3(Mathf.Cos(a1) * R, Mathf.Sin(a1) * R, z);
                        Strut(mb, 0, p0, p1, 0.22f);
                    }
                    if (i % 2 == 0)
                    {
                        Strut(mb, 0, new Vector3(0, 0, -0.9f), new Vector3(Mathf.Cos(a0) * R, Mathf.Sin(a0) * R, -0.9f), 0.08f);
                        Strut(mb, 0, new Vector3(0, 0, 0.9f), new Vector3(Mathf.Cos(a0) * R, Mathf.Sin(a0) * R, 0.9f), 0.08f);
                    }
                    if (i % 2 == 0)
                    {
                        var g = new Vector3(Mathf.Cos(a0) * R, Mathf.Sin(a0) * R - 1.6f, 0);
                        mb.Box(2 + (i / 2) % colors.Length, g, Quaternion.identity, new Vector3(1.8f, 1.6f, 1.6f));
                    }
                }
                mb.Box(1, Vector3.zero, Quaternion.identity, new Vector3(1.2f, 1.2f, 2.6f));
                var mats = new Material[2 + colors.Length];
                mats[0] = steel;
                mats[1] = dark;
                for (int i = 0; i < colors.Length; i++) mats[2 + i] = colors[i];
                var obj = mb.ToObject("WheelRim", wheel, mats);
                obj.transform.localPosition = Vector3.zero;
                obj.transform.localRotation = Quaternion.identity;
            }

            // Entrance arch: two pillars and a banner beam over the road.
            {
                var p = f.arch;
                Frame3(p, out var c, out var rot, out var fw, out var rt);
                float W = (float)p.w, H = (float)p.h;
                var mb = new MeshBuilder(2);
                mb.Box(0, c + rt * (-W / 2) + Vector3.up * (H / 2), rot, new Vector3(1.6f, H, 1.6f));
                mb.Box(0, c + rt * (W / 2) + Vector3.up * (H / 2), rot, new Vector3(1.6f, H, 1.6f));
                mb.Box(1, c + Vector3.up * (H - 1.4f), rot, new Vector3(W + 1.6f, 2.8f, 1.2f));
                mb.ToObject("Arch", transform, new[] { steel, ColorOf(p.color) });
            }

            // Tents: square pavilions with a peaked roof.
            {
                var mb = new MeshBuilder(1 + colors.Length);
                foreach (var t in f.tents)
                {
                    Frame3(t, out var c, out var rot, out var fw, out var rt);
                    float W = (float)t.w, D = (float)t.d, H = (float)t.h;
                    int m = 1 + System.Array.IndexOf(f.colors, t.color);
                    if (m < 1) m = 1;
                    float wallH = H * 0.45f;
                    foreach (int sx in new[] { -1, 1 })
                        foreach (int sz in new[] { -1, 1 })
                            mb.Box(0, c + rt * (sx * W / 2) + fw * (sz * D / 2) + Vector3.up * (wallH / 2), rot, new Vector3(0.15f, wallH, 0.15f));
                    Vector3 a = c + rt * (-W / 2) + fw * (D / 2) + Vector3.up * wallH, b = c + rt * (W / 2) + fw * (D / 2) + Vector3.up * wallH;
                    Vector3 cc = c + rt * (W / 2) + fw * (-D / 2) + Vector3.up * wallH, d = c + rt * (-W / 2) + fw * (-D / 2) + Vector3.up * wallH;
                    Vector3 top = c + Vector3.up * H;
                    Tri(mb, m, a, b, top);
                    Tri(mb, m, b, cc, top);
                    Tri(mb, m, cc, d, top);
                    Tri(mb, m, d, a, top);
                }
                var mats = new Material[1 + colors.Length];
                mats[0] = steel;
                for (int i = 0; i < colors.Length; i++) mats[1 + i] = colors[i];
                mb.ToObject("Tents", transform, mats);
            }

            // Flag poles with pennants.
            {
                var mb = new MeshBuilder(1 + colors.Length);
                foreach (var pole in f.poles)
                {
                    var c = Frame.Pos(pole.x, pole.y, pole.z);
                    float H = (float)(pole.h > 0 ? pole.h : 9);
                    mb.Box(0, c + Vector3.up * (H / 2), Quaternion.identity, new Vector3(0.14f, H, 0.14f));
                    int m = 1 + System.Array.IndexOf(f.colors, pole.color);
                    if (m < 1) m = 1;
                    var fw = Frame.Dir(pole.fx, 0, pole.fz);
                    if (fw.sqrMagnitude < 1e-6f) fw = Vector3.forward;
                    var side = Vector3.Cross(Vector3.up, fw.normalized);
                    Vector3 a = c + Vector3.up * H, b = a + side * 1.6f + Vector3.down * 0.5f, d = c + Vector3.up * (H - 1.2f);
                    Tri(mb, m, a, b, d);
                    Tri(mb, m, a, d, b);
                }
                var mats = new Material[1 + colors.Length];
                mats[0] = steel;
                for (int i = 0; i < colors.Length; i++) mats[1 + i] = colors[i];
                mb.ToObject("FlagPoles", transform, mats);
            }
            Dressing(f, steel, dark);
        }

        /// <summary>The cars on show and parked, the trucks, towers, barriers and crowds.</summary>
        void Dressing(FestivalLayout f, Material steel, Material dark)
        {
            // Cars: the Blender models, standing (plinths are 3.3 m round and 0.35 m high).
            var plinth = Mats.Make("FestivalPlinth", new Color(0.08f, 0.09f, 0.1f), 0.85f, 0.2f);
            var ringA = Mats.Make("FestivalRingA", new Color(0.08f, 0.72f, 0.65f), 0.5f);
            var ringB = Mats.Make("FestivalRingB", new Color(0.88f, 0.2f, 0.48f), 0.5f);
            Mats.SetEmission(ringA, new Color(0.08f, 0.72f, 0.65f) * 2f);
            Mats.SetEmission(ringB, new Color(0.88f, 0.2f, 0.48f) * 2f);
            for (int i = 0; i < f.displays.Length; i++)
            {
                var d = f.displays[i];
                var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                p.name = "Plinth " + d.specId;
                Destroy(p.GetComponent<Collider>());
                p.transform.SetParent(transform, false);
                p.transform.position = Frame.Pos(d.x, d.y + 0.175 - 0.2, d.z);
                p.transform.localScale = new Vector3(6.6f, 0.375f, 6.6f);
                p.GetComponent<Renderer>().sharedMaterial = plinth;
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(ring.GetComponent<Collider>());
                ring.transform.SetParent(p.transform.parent, false);
                ring.transform.position = Frame.Pos(d.x, d.y + 0.05, d.z);
                ring.transform.localScale = new Vector3(6.85f, 0.05f, 6.85f);
                ring.GetComponent<Renderer>().sharedMaterial = i % 2 == 1 ? ringA : ringB;
                Stand(d, 0.35f);
            }
            foreach (var c in f.parked) Stand(c, 0);
            if (f.carPark != null)
            {
                var p = f.carPark;
                var mb = new MeshBuilder(1);
                var fw = Frame.Dir(p[3], 0, p[4]).normalized;
                mb.Box(0, Frame.Pos(p[0], p[2] + 0.03, p[1]), Quaternion.LookRotation(fw, Vector3.up), new Vector3((float)p[5], 0.06f, (float)p[6]));
                mb.ToObject("CarPark", transform, new[] { Mats.Make("FestivalGravel", new Color(0.49f, 0.47f, 0.42f), 0.08f) }, false);
            }

            // Food trucks: body, cab, hatch and awning, a lit sign.
            {
                var mb = new MeshBuilder(4 + colors.Length);
                var sign = Mats.Make("FestivalTruckSign", new Color(1f, 0.95f, 0.84f), 0.4f);
                Mats.SetEmission(sign, new Color(1f, 0.95f, 0.84f) * 1.5f);
                foreach (var t in f.trucks)
                {
                    Frame3(t, out var c, out var rot, out var fw, out var rt);
                    float W = (float)t.w, D = (float)t.d, H = (float)t.h;
                    int m = 4 + System.Math.Max(0, System.Array.IndexOf(f.colors, t.color));
                    mb.Box(m, c + rt * -0.6f + Vector3.up * (0.55f + (H - 0.55f) / 2), rot, new Vector3(W - 1.6f, H - 0.55f, D));
                    mb.Box(0, c + rt * (W / 2 - 0.7f) + Vector3.up * 1.5f, rot, new Vector3(1.4f, 1.9f, D * 0.96f));
                    mb.Box(1, c + rt * -0.8f + Vector3.up * 1.95f + fw * (D / 2 + 0.02f), rot, new Vector3(W - 3.2f, 1.1f, 0.05f));
                    mb.Box(m, c + rt * -0.8f + Vector3.up * 2.62f + fw * (D / 2 + 0.65f), rot * Quaternion.Euler(-11f, 0, 0), new Vector3(W - 3.2f, 0.04f, 1.3f));
                    mb.Box(2, c + rt * -0.6f + Vector3.up * (H + 0.35f), rot, new Vector3(W - 2.4f, 0.6f, 0.16f));
                    foreach (float a in new[] { -W / 2 + 1.2f, W / 2 - 1.1f })
                        foreach (int sd in new[] { -1, 1 })
                            mb.Box(3, c + rt * a + fw * (sd * (D / 2 - 0.1f)) + Vector3.up * 0.42f, rot, new Vector3(0.84f, 0.84f, 0.28f));
                }
                var mats = new Material[4 + colors.Length];
                mats[0] = Mats.Make("FestivalTruckCab", new Color(0.94f, 0.93f, 0.9f), 0.5f);
                mats[1] = dark;
                mats[2] = sign;
                mats[3] = Mats.Make("FestivalTyre", new Color(0.07f, 0.07f, 0.075f), 0.1f);
                for (int i = 0; i < colors.Length; i++) mats[4 + i] = colors[i];
                if (f.trucks.Length > 0) mb.ToObject("FoodTrucks", transform, mats);
            }

            // Light towers: truss, lamp bar and a speaker stack, facing the stage.
            {
                var mb = new MeshBuilder(3);
                var lamp = Mats.Make("FestivalLamp", new Color(1f, 0.96f, 0.88f), 0.6f);
                Mats.SetEmission(lamp, new Color(1f, 0.96f, 0.88f) * 3f);
                foreach (var t in f.towers)
                {
                    Frame3(t, out var c, out var rot, out var fw, out var rt);
                    float H = (float)t.h;
                    foreach (int sx in new[] { -1, 1 })
                        foreach (int sz in new[] { -1, 1 })
                            mb.Box(0, c + rt * (sx * 0.45f) + fw * (sz * 0.45f) + Vector3.up * (H / 2), rot, new Vector3(0.08f, H, 0.08f));
                    for (int k = 1; k < H / 1.5f; k++) mb.Box(0, c + Vector3.up * (k * 1.5f), rot, new Vector3(0.96f, 0.06f, 0.96f));
                    mb.Box(1, c + fw * 0.75f + Vector3.up * (H - 3.2f), rot, new Vector3(1.1f, 2.2f, 0.8f));
                    for (int i = -2; i < 2; i++)
                    {
                        var lc = c + rt * ((i + 0.5f) * 0.6f) + fw * 0.8f + Vector3.up * (H - 0.7f);
                        mb.Box(1, lc, rot, new Vector3(0.4f, 0.4f, 0.5f));
                        mb.Box(2, lc + fw * 0.26f, rot, new Vector3(0.3f, 0.3f, 0.02f));
                    }
                }
                if (f.towers.Length > 0) mb.ToObject("LightTowers", transform, new[] { steel, dark, lamp });
            }

            // Barriers: a panel every 2.3 m, rails and bars.
            {
                var mb = new MeshBuilder(1);
                foreach (var b in f.barriers)
                {
                    for (int i = 0; i + 1 < b.Length / 3; i++)
                    {
                        var p = Frame.Pos(b[3 * i], b[3 * i + 2], b[3 * i + 1]);
                        var q = Frame.Pos(b[3 * i + 3], b[3 * i + 5], b[3 * i + 4]);
                        var along = q - p;
                        along.y = 0;
                        float len = along.magnitude;
                        if (len < 0.1f) continue;
                        var rot = Quaternion.LookRotation(along / len, Vector3.up);
                        int n = Mathf.Max(1, Mathf.RoundToInt(len / 2.3f));
                        for (int k = 0; k < n; k++)
                        {
                            var c = Vector3.Lerp(p, q, (k + 0.5f) / n);
                            float pw = len / n - 0.08f;
                            mb.Box(0, c + Vector3.up * 1.07f, rot, new Vector3(0.05f, 0.05f, pw));
                            mb.Box(0, c + Vector3.up * 0.25f, rot, new Vector3(0.05f, 0.05f, pw));
                            for (int j = 1; j < 8; j++) mb.Box(0, c + (along / len) * ((j / 8f - 0.5f) * pw) + Vector3.up * 0.67f, rot, new Vector3(0.025f, 0.85f, 0.025f));
                        }
                    }
                }
                if (!mb.Empty) mb.ToObject("Barriers", transform, new[] { steel });
            }

            // Crowds: people as three boxes (legs, shirt, head), in a few shirt colours.
            {
                var rng = new System.Random(7);
                var mb = new MeshBuilder(2 + colors.Length);
                foreach (var cr in f.crowds)
                {
                    var fw = Frame.Dir(cr.fx, 0, cr.fz).normalized;
                    var rt = Vector3.Cross(Vector3.up, fw);
                    var c0 = Frame.Pos(cr.x, cr.y, cr.z);
                    for (int i = 0, tries = 0; i < cr.count && tries < cr.count * 8; tries++)
                    {
                        float a = (float)(rng.NextDouble() - 0.5) * (float)cr.w;
                        float b = (float)(rng.NextDouble() - 0.5) * (float)cr.d;
                        if (rng.NextDouble() > 0.45 + 0.55 * (b / cr.d + 0.5)) continue;
                        var pos = c0 + rt * a + fw * b;
                        var rot = Quaternion.LookRotation(Quaternion.Euler(0, (float)(rng.NextDouble() - 0.5) * 57f, 0) * fw, Vector3.up);
                        float s = 0.9f + (float)rng.NextDouble() * 0.18f;
                        mb.Box(0, pos + Vector3.up * (0.43f * s), rot, new Vector3(0.34f, 0.86f, 0.2f) * s);
                        mb.Box(2 + rng.Next(colors.Length), pos + Vector3.up * (1.15f * s), rot, new Vector3(0.62f, 0.6f, 0.24f) * s);
                        mb.Box(1, pos + Vector3.up * (1.62f * s), rot, new Vector3(0.22f, 0.24f, 0.22f) * s);
                        i++;
                    }
                }
                var mats = new Material[2 + colors.Length];
                mats[0] = Mats.Make("CrowdLegs", new Color(0.12f, 0.14f, 0.2f), 0.15f);
                mats[1] = Mats.Make("CrowdSkin", new Color(0.78f, 0.6f, 0.47f), 0.2f);
                for (int i = 0; i < colors.Length; i++) mats[2 + i] = colors[i];
                if (!mb.Empty) mb.ToObject("Crowds", transform, mats);
            }
        }

        /// <summary>A car standing still: its Blender model on the ground (or a plinth), in its own or a given paint.</summary>
        void Stand(FestivalCar c, float lift)
        {
            var spec = Cars.ById(c.specId);
            if (spec == null) return;
            var v = CarVisual.Create(c.specId, VehicleParams.Build(spec), c.paint >= 0 ? Hex(c.paint) : (Color?)null);
            v.Root.transform.SetParent(transform, false);
            v.Root.transform.position = Frame.Pos(c.x, c.y + lift, c.z);
            v.Root.transform.rotation = Quaternion.LookRotation(Frame.Dir(c.fx, 0, c.fz), Vector3.up);
        }

        void Update()
        {
            if (wheel != null) wheel.Rotate(0, 0, 4f * Time.deltaTime, Space.Self);
        }

        static void Frame3(FestivalPiece p, out Vector3 c, out Quaternion rot, out Vector3 fw, out Vector3 rt)
        {
            c = Frame.Pos(p.x, p.y, p.z);
            fw = Frame.Dir(p.fx, 0, p.fz);
            if (fw.sqrMagnitude < 1e-6f) fw = Vector3.forward;
            fw.Normalize();
            rot = Quaternion.LookRotation(fw, Vector3.up);
            rt = Vector3.Cross(Vector3.up, fw);
        }

        static void Tri(MeshBuilder mb, int sub, Vector3 a, Vector3 b, Vector3 c)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            mb.Tri(sub, mb.Add(a, n, Vector2.zero), mb.Add(b, n, new Vector2(1, 0)), mb.Add(c, n, new Vector2(0.5f, 1)));
        }

        static void Strut(MeshBuilder mb, int sub, Vector3 a, Vector3 b, float thick)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            var rot = Quaternion.LookRotation(d.normalized, Mathf.Abs(d.normalized.y) > 0.95f ? Vector3.forward : Vector3.up);
            mb.Box(sub, (a + b) / 2, rot, new Vector3(thick, thick, d.magnitude));
        }
    }
}
