// The Halcyon Festival on its plateau: main stage, the big wheel, the
// entrance arch, pavilion tents and flag lines along the festival road.
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public sealed class FestivalBuilder : MonoBehaviour
    {
        Transform wheel;

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
            var colors = new Material[f.colors.Length];
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
