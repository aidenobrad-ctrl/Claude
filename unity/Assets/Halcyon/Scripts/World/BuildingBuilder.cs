// The island's 639 buildings: walls with a window facade, and flat, gable or
// hip roofs, by kind (houses, shops, towers, warehouses, barns, silos,
// midrise blocks), merged into a few meshes per kilometre.
using System.Collections.Generic;
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public static class BuildingBuilder
    {
        // Building kinds from the exporter.
        const int House = 0, Shop = 1, Tower = 2, Warehouse = 3, Barn = 4, Silo = 5, Midrise = 6;
        const int RoofFlat = 0, RoofGable = 1, RoofHip = 2;

        // Submeshes: 0..4 wall tints, 5 glass tower, 6 metal, 7 roof tiles, 8 roof dark, 9 roof flat, 10 barn red.
        const int Walls = 0, Glass = 5, Metal = 6, RoofTiles = 7, RoofDark = 8, RoofFlatMat = 9, BarnRed = 10, Subs = 11;

        static readonly Color[] WallTints =
        {
            new Color(0.93f, 0.89f, 0.80f), new Color(0.88f, 0.70f, 0.55f), new Color(0.97f, 0.96f, 0.93f),
            new Color(0.76f, 0.80f, 0.84f), new Color(0.92f, 0.82f, 0.62f),
        };

        public static void Build(WorldData w, Transform parent)
        {
            var root = new GameObject("Buildings").transform;
            root.SetParent(parent, false);
            ProceduralTextures.Facade(out var fa, out var fn);
            var mats = new Material[Subs];
            for (int i = 0; i < 5; i++) mats[Walls + i] = Mats.Make("Wall" + i, WallTints[i], 0.12f, 0f, fa, fn);
            mats[Glass] = Mats.Make("TowerGlass", new Color(0.30f, 0.42f, 0.52f), 0.9f, 0.6f, fa, fn);
            // Windows light up at dusk (the emission map is the facade's lit panes).
            var lights = ProceduralTextures.FacadeLights();
            for (int i = 0; i <= Glass; i++)
            {
                mats[i].SetTexture("_EmissionMap", lights);
                SkyDirector.Glow(mats[i], Color.black, Color.white * (i == Glass ? 2.2f : 1.6f));
            }
            mats[Metal] = Mats.Make("Metal", new Color(0.55f, 0.58f, 0.62f), 0.45f, 0.6f);
            mats[RoofTiles] = Mats.Make("RoofTiles", new Color(0.56f, 0.22f, 0.15f), 0.2f);
            mats[RoofDark] = Mats.Make("RoofDark", new Color(0.20f, 0.21f, 0.23f), 0.25f);
            mats[RoofFlatMat] = Mats.Make("RoofFlat", new Color(0.36f, 0.36f, 0.35f), 0.1f);
            mats[BarnRed] = Mats.Make("BarnRed", new Color(0.50f, 0.12f, 0.09f), 0.15f);
            var groups = new Dictionary<long, MeshBuilder>();
            foreach (var b in w.Buildings)
            {
                long key = ((long)Mathf.FloorToInt((float)(b.x / 1024)) << 20) ^ (long)Mathf.FloorToInt((float)(b.z / 1024)) & 0xfffff;
                if (!groups.TryGetValue(key, out var mb)) groups[key] = mb = new MeshBuilder(Subs);
                Add(mb, b);
            }
            int n = 0;
            foreach (var kv in groups) kv.Value.ToObject("Buildings_" + n++, root, mats);
        }

        static void Add(MeshBuilder mb, BuildingInfo b)
        {
            // Local frame: front (fx, fz) in the simulation, right = (-fz, fx); then to Unity.
            Vector3 f = Frame.Dir(b.fx, 0, b.fz).normalized;
            Vector3 r = Frame.Dir(-b.fz, 0, b.fx).normalized;
            Vector3 c = Frame.Pos(b.x, 0, b.z);
            float y0 = (float)b.y0, y1 = (float)(b.floor + b.h);
            float hw = (float)b.w / 2, hd = (float)b.d / 2;
            int wall = Walls + (int)(b.seed * 5) % 5;
            if (b.kind == Tower) wall = Glass;
            else if (b.kind == Warehouse) wall = Metal;
            else if (b.kind == Barn) wall = BarnRed;
            if (b.kind == Silo)
            {
                Cylinder(mb, Metal, c + Vector3.up * y0, Mathf.Min(hw, hd), y1 - y0, 16);
                return;
            }
            Vector3 P(float x, float y, float z) => c + r * x + Vector3.up * y + f * z;
            // Walls: four faces, facade UVs in metres / 12 (windows every 3 m).
            var uv = new Vector2(1 / 12f, 1 / 12f);
            mb.FlatQuad(wall, P(-hw, y0, hd), P(hw, y0, hd), P(hw, y1, hd), P(-hw, y1, hd), uv);   // front (+f)
            mb.FlatQuad(wall, P(hw, y0, -hd), P(-hw, y0, -hd), P(-hw, y1, -hd), P(hw, y1, -hd), uv); // back
            mb.FlatQuad(wall, P(hw, y0, hd), P(hw, y0, -hd), P(hw, y1, -hd), P(hw, y1, hd), uv);   // right
            mb.FlatQuad(wall, P(-hw, y0, -hd), P(-hw, y0, hd), P(-hw, y1, hd), P(-hw, y1, -hd), uv); // left
            int roofMat = b.kind == Barn ? RoofDark : (b.seed * 7 % 1) < 0.6 ? RoofTiles : RoofDark;
            switch (b.roof)
            {
                case RoofGable:
                    {
                        // Ridge along the width, slopes to front and back, gables at the sides.
                        float rh = hd * 0.62f;
                        float o = 0.35f; // eave overhang
                        Vector3 ridgeL = P(-hw - o, y1 + rh, 0), ridgeR = P(hw + o, y1 + rh, 0);
                        mb.FlatQuad(roofMat, P(-hw - o, y1 - 0.15f, hd + o), P(hw + o, y1 - 0.15f, hd + o), ridgeR, ridgeL, Vector2.one * 0.25f);
                        mb.FlatQuad(roofMat, P(hw + o, y1 - 0.15f, -hd - o), P(-hw - o, y1 - 0.15f, -hd - o), ridgeL, ridgeR, Vector2.one * 0.25f);
                        Gable(mb, wall, P(hw, y1, hd), P(hw, y1, -hd), P(hw, y1 + rh, 0));
                        Gable(mb, wall, P(-hw, y1, -hd), P(-hw, y1, hd), P(-hw, y1 + rh, 0));
                        break;
                    }
                case RoofHip:
                    {
                        float rh = Mathf.Min(hw, hd) * 0.55f;
                        float inset = Mathf.Min(hw, hd) * 0.9f;
                        Vector3 a = P(-hw, y1, hd), bb = P(hw, y1, hd), cc = P(hw, y1, -hd), d = P(-hw, y1, -hd);
                        Vector3 rl = P(-hw + inset, y1 + rh, 0), rr = P(hw - inset, y1 + rh, 0);
                        mb.FlatQuad(roofMat, a, bb, rr, rl, Vector2.one * 0.25f);
                        mb.FlatQuad(roofMat, cc, d, rl, rr, Vector2.one * 0.25f);
                        Gable(mb, roofMat, bb, cc, rr);
                        Gable(mb, roofMat, d, a, rl);
                        break;
                    }
                default:
                    {
                        // Flat roof with a low parapet.
                        mb.FlatQuad(RoofFlatMat, P(-hw, y1, -hd), P(-hw, y1, hd), P(hw, y1, hd), P(hw, y1, -hd), Vector2.one * 0.25f);
                        float ph = 0.6f, pt = 0.25f;
                        var q = Quaternion.LookRotation(f, Vector3.up);
                        mb.Box(wall, P(0, y1 + ph / 2, hd - pt / 2), q, new Vector3(hw * 2, ph, pt), 1 / 12f);
                        mb.Box(wall, P(0, y1 + ph / 2, -hd + pt / 2), q, new Vector3(hw * 2, ph, pt), 1 / 12f);
                        mb.Box(wall, P(hw - pt / 2, y1 + ph / 2, 0), q, new Vector3(pt, ph, hd * 2), 1 / 12f);
                        mb.Box(wall, P(-hw + pt / 2, y1 + ph / 2, 0), q, new Vector3(pt, ph, hd * 2), 1 / 12f);
                        break;
                    }
            }
        }

        static void Gable(MeshBuilder mb, int sub, Vector3 a, Vector3 b, Vector3 top)
        {
            var n = Vector3.Cross(b - a, top - a).normalized;
            int ia = mb.Add(a, n, Vector2.zero), ib = mb.Add(b, n, new Vector2(1, 0)), it = mb.Add(top, n, new Vector2(0.5f, 0.5f));
            mb.Tri(sub, ia, ib, it);
        }

        static void Cylinder(MeshBuilder mb, int sub, Vector3 baseC, float r, float h, int sides)
        {
            int start = mb.Count;
            for (int i = 0; i <= sides; i++)
            {
                float t = i / (float)sides * Mathf.PI * 2;
                var n = new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t));
                mb.Add(baseC + n * r, n, new Vector2(i / (float)sides * 4, 0));
                mb.Add(baseC + n * r + Vector3.up * h, n, new Vector2(i / (float)sides * 4, h / 4));
            }
            for (int i = 0; i < sides; i++)
            {
                int p = start + i * 2;
                mb.Quad(sub, p, p + 1, p + 3, p + 2);
            }
            // Domed cap.
            int apex = mb.Add(baseC + Vector3.up * (h + r * 0.5f), Vector3.up, Vector2.zero);
            for (int i = 0; i < sides; i++)
            {
                int p = start + i * 2 + 1;
                mb.Tri(sub, apex, p + 2, p);
            }
        }
    }
}
