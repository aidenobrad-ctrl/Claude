// The island's landmarks, found from the exported island the same way every
// time (so the tests, the map and the Unity builders agree):
//   - a radio mast on the highest summit;
//   - a lighthouse on the eastern cape;
//   - a wind farm on the hills above Westhaven;
//   - a stone arch in the Redmesa desert;
//   - an observatory on the high ground above the lake.
// Each stands clear of the roads but within reach of one, and adds solid
// colliders to the simulation. No UnityEngine.
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    public sealed class Landmark
    {
        public string Id, Name, Kind;
        /// <summary>Where it stands (ground height in Y); Yaw faces the nearest road.</summary>
        public double X, Y, Z, Yaw;
        /// <summary>Extra positions (the wind farm's turbines), with their ground heights.</summary>
        public readonly List<(double x, double y, double z)> Parts = new List<(double, double, double)>();
        /// <summary>How close counts as finding it, m (always reaching past the nearest road).</summary>
        public double Radius = 160;
        /// <summary>Distance to the nearest road, m.</summary>
        public double RoadDist;
    }

    public static class Landmarks
    {
        public const string Mast = "mast", Lighthouse = "lighthouse", WindFarm = "windfarm", Arch = "arch", Observatory = "observatory";

        /// <summary>The island's landmarks (cached per island).</summary>
        public static List<Landmark> Find(WorldData w, IslandGround g)
        {
            lock (cache)
            {
                if (cache.TryGetValue(w, out var list)) return list;
                list = Compute(w, g);
                cache[w] = list;
                return list;
            }
        }

        static readonly Dictionary<WorldData, List<Landmark>> cache = new Dictionary<WorldData, List<Landmark>>();

        static double X(WorldData w, int i) => w.Origin + i * w.Spacing;

        static bool Dry(WorldData w, int k) => w.Water[k] == short.MinValue || w.Height[k] > w.Water[k] / 100.0 + 0.5;

        /// <summary>
        /// Distance from (x, z) to the nearest road centre line anywhere on the network (up to max), m.
        /// IslandGround.NearestRoad only looks at the roads within ~60 m, which is right for driving
        /// but not for placing things hundreds of metres away.
        /// </summary>
        public static double RoadDistance(WorldData w, double x, double z, double max = 1e9)
        {
            double best = max * max;
            foreach (var e in w.Edges)
            {
                for (int i = 0; i + 1 < e.n; i++)
                {
                    double ax = e.x[i], az = e.z[i], dx = e.x[i + 1] - ax, dz = e.z[i + 1] - az;
                    // Cheap reject: the segment's nearer end is already further than the best.
                    double ex = Math.Min(Math.Abs(ax - x), Math.Abs(ax + dx - x)), ez = Math.Min(Math.Abs(az - z), Math.Abs(az + dz - z));
                    if (ex * ex + ez * ez > best + 64) continue;
                    double l2 = dx * dx + dz * dz;
                    double t = l2 > 0 ? Math.Max(0, Math.Min(1, ((x - ax) * dx + (z - az) * dz) / l2)) : 0;
                    double qx = ax + dx * t - x, qz = az + dz * t - z;
                    double d = qx * qx + qz * qz;
                    if (d < best) best = d;
                }
            }
            return Math.Sqrt(best);
        }

        static double RoadDistance(IslandGround g, double x, double z, double max) => RoadDistance(g.W, x, z, max);

        /// <summary>Steepest height difference to the 4 neighbours, over the grid spacing.</summary>
        static double Slope(WorldData w, int i, int j)
        {
            int n = w.N;
            if (i < 1 || j < 1 || i >= n - 1 || j >= n - 1) return 1;
            double h = w.Height[j * n + i], s = 0;
            s = Math.Max(s, Math.Abs(w.Height[j * n + i + 1] - h));
            s = Math.Max(s, Math.Abs(w.Height[j * n + i - 1] - h));
            s = Math.Max(s, Math.Abs(w.Height[(j + 1) * n + i] - h));
            s = Math.Max(s, Math.Abs(w.Height[(j - 1) * n + i] - h));
            return s / w.Spacing;
        }

        static Poi PoiNamed(WorldData w, string name)
        {
            foreach (var p in w.Pois) if (p.name == name) return p;
            return null;
        }

        static Landmark Make(WorldData w, IslandGround g, string id, string name, string kind, double x, double z)
        {
            var l = new Landmark { Id = "landmark:" + id, Name = name, Kind = kind, X = x, Z = z, Y = g.GroundHeight(x, z) };
            l.RoadDist = RoadDistance(w, x, z, 3000);
            // Found from the road that passes nearest.
            l.Radius = Math.Max(l.Radius, l.RoadDist + 120);
            {
                // Face the nearest road.
                double best = 1e18, bx = x, bz = z;
                foreach (var e in w.Edges)
                {
                    for (int i = 0; i < e.n; i++)
                    {
                        double d = (e.x[i] - x) * (e.x[i] - x) + (e.z[i] - z) * (e.z[i] - z);
                        if (d < best)
                        {
                            best = d;
                            bx = e.x[i];
                            bz = e.z[i];
                        }
                    }
                }
                l.Yaw = Simulation.YawOf(bx - x, bz - z);
            }
            return l;
        }

        static List<Landmark> Compute(WorldData w, IslandGround g)
        {
            var list = new List<Landmark>();
            int n = w.N, step = Math.Max(1, (int)Math.Round(16 / w.Spacing)); // scan every ~16 m

            // 1. The radio mast: the highest dry point within 500 m of a road (it stands over the
            // road below, and its red lights show for miles at night).
            int top = -1;
            var order = new List<int>();
            for (int j = 1; j < n - 1; j += step)
                for (int i = 1; i < n - 1; i += step)
                    if (Dry(w, j * n + i) && w.Height[j * n + i] > 150) order.Add(j * n + i);
            order.Sort((p, q) => w.Height[q].CompareTo(w.Height[p]));
            foreach (int k in order)
            {
                double rd = RoadDistance(g, X(w, k % n), X(w, k / n), 500);
                if (rd < 500 && rd > 40 && Slope(w, k % n, k / n) < 0.5)
                {
                    top = k;
                    break;
                }
            }
            if (top < 0) top = order.Count > 0 ? order[0] : n * n / 2;
            double mx = X(w, top % n), mz = X(w, top / n);
            list.Add(Make(w, g, "summit-mast", "Summit Mast", Mast, mx, mz));

            // 2. The lighthouse: the dry point furthest east with the sea (water at sea level, not the
            // lake) within 80 m and a road within 600 m, away from the harbour and the city.
            {
                double bestX = double.NegativeInfinity, lx = 0, lz = 0;
                int reach = Math.Max(1, (int)Math.Round(80 / w.Spacing));
                for (int j = reach; j < n - reach; j += step)
                    for (int i = reach; i < n - reach; i += step)
                    {
                        int k = j * n + i;
                        double h = w.Height[k];
                        if (!Dry(w, k) || h < 4 || h > 45) continue;
                        double x = X(w, i), z = X(w, j);
                        if (x <= bestX || w.Region[k] == 5 || w.Region[k] == 6) continue;
                        bool sea = false;
                        for (int a = 0; a < 8 && !sea; a++)
                        {
                            double ang = a * Math.PI / 4;
                            int ii = i + (int)Math.Round(Math.Cos(ang) * reach), jj = j + (int)Math.Round(Math.Sin(ang) * reach);
                            int kk = jj * n + ii;
                            // The sea is the ground below sea level (rivers and the lake carry their own water level).
                            if (w.Height[kk] < -0.5 && (w.Water[kk] == short.MinValue || w.Water[kk] < 100)) sea = true;
                        }
                        if (!sea) continue;
                        double rd = RoadDistance(g, x, z, 600);
                        if (rd >= 600 || rd < 30) continue;
                        bestX = x;
                        lx = x;
                        lz = z;
                    }
                if (!double.IsNegativeInfinity(bestX)) list.Add(Make(w, g, "cape-lighthouse", "Cape Lighthouse", Lighthouse, lx, lz));
            }

            // 3. The wind farm: the six highest hilltops within 2.5 km of Westhaven, 140 m apart, clear of the
            // roads but within 450 m of one.
            var west = PoiNamed(w, "Westhaven");
            if (west != null)
            {
                var cands = new List<(double h, double x, double z)>();
                for (int j = 1; j < n - 1; j += step)
                    for (int i = 1; i < n - 1; i += step)
                    {
                        int k = j * n + i;
                        double x = X(w, i), z = X(w, j);
                        if ((x - west.x) * (x - west.x) + (z - west.z) * (z - west.z) > 2500 * 2500) continue;
                        if (!Dry(w, k) || w.Height[k] < 30 || Slope(w, i, j) > 0.35) continue;
                        cands.Add((w.Height[k], x, z));
                    }
                cands.Sort((p, q) => q.h.CompareTo(p.h));
                var farm = new List<(double h, double x, double z)>();
                foreach (var c in cands)
                {
                    if (farm.Count >= 6) break;
                    bool apart = true;
                    foreach (var f in farm) if ((f.x - c.x) * (f.x - c.x) + (f.z - c.z) * (f.z - c.z) < 140 * 140) apart = false;
                    if (!apart) continue;
                    double rd = RoadDistance(g, c.x, c.z, 450);
                    if (rd < 40 || rd >= 450) continue;
                    // Keep the farm together: within 900 m of the first turbine.
                    if (farm.Count > 0 && (farm[0].x - c.x) * (farm[0].x - c.x) + (farm[0].z - c.z) * (farm[0].z - c.z) > 900 * 900) continue;
                    farm.Add(c);
                }
                if (farm.Count >= 3)
                {
                    double cx = 0, cz = 0;
                    foreach (var f in farm) { cx += f.x; cz += f.z; }
                    var l = Make(w, g, "westhaven-windfarm", "Westhaven Wind Farm", WindFarm, cx / farm.Count, cz / farm.Count);
                    l.Radius = Math.Max(450, l.Radius);
                    foreach (var f in farm) l.Parts.Add((f.x, g.GroundHeight(f.x, f.z), f.z));
                    list.Add(l);
                }
            }

            // 4. The stone arch: a flat spot in the desert near Redmesa, 60 to 400 m from a road.
            var mesa = PoiNamed(w, "Redmesa");
            if (mesa != null)
            {
                double best = double.NegativeInfinity, ax = 0, az = 0;
                for (int j = 1; j < n - 1; j += step)
                    for (int i = 1; i < n - 1; i += step)
                    {
                        int k = j * n + i;
                        double x = X(w, i), z = X(w, j);
                        double d = Math.Sqrt((x - mesa.x) * (x - mesa.x) + (z - mesa.z) * (z - mesa.z));
                        if (d < 350 || d > 1500 || !Dry(w, k) || w.Region[k] != 4) continue;
                        double score = w.Height[k] * 0.05 - Slope(w, i, j) * 40 - Math.Abs(d - 700) * 0.01;
                        if (score <= best) continue;
                        double rd = RoadDistance(g, x, z, 400);
                        if (rd < 60 || rd >= 400) continue;
                        best = score;
                        ax = x;
                        az = z;
                    }
                if (!double.IsNegativeInfinity(best)) list.Add(Make(w, g, "redmesa-arch", "Redmesa Arch", Arch, ax, az));
            }

            // 5. The observatory: the highest point within 2 km of the lake, 1.5 km or more from the mast.
            {
                double lx = 0, lz = 0;
                int lake = 0;
                for (int j = 1; j < n - 1; j += step)
                    for (int i = 1; i < n - 1; i += step)
                        if (w.Region[j * n + i] == 11) { lx += X(w, i); lz += X(w, j); lake++; }
                if (lake > 0)
                {
                    lx /= lake;
                    lz /= lake;
                    double best = double.NegativeInfinity, ox = 0, oz = 0;
                    for (int j = 1; j < n - 1; j += step)
                        for (int i = 1; i < n - 1; i += step)
                        {
                            int k = j * n + i;
                            double x = X(w, i), z = X(w, j);
                            if ((x - lx) * (x - lx) + (z - lz) * (z - lz) > 2000 * 2000) continue;
                            if ((x - mx) * (x - mx) + (z - mz) * (z - mz) < 1500 * 1500) continue;
                            if (!Dry(w, k) || Slope(w, i, j) > 0.3 || w.Height[k] <= best) continue;
                            double rd = RoadDistance(g, x, z, 500);
                            if (rd < 30 || rd >= 500) continue;
                            best = w.Height[k];
                            ox = x;
                            oz = z;
                        }
                    if (!double.IsNegativeInfinity(best)) list.Add(Make(w, g, "lakeview-observatory", "Lakeview Observatory", Observatory, ox, oz));
                }
            }
            return list;
        }

        /// <summary>Solid footprints for the physics: a car hits the structures instead of driving through them.</summary>
        public static void AddColliders(Colliders c, List<Landmark> list)
        {
            void Post(double x, double z, double r, double top) => c.AddCircle(new Circle { x = x, z = z, r = r, top = top, bottom = -5, kind = CircleKind.Post });
            foreach (var l in list)
            {
                switch (l.Kind)
                {
                    case Mast: Post(l.X, l.Z, 3.2, l.Y + 60); break;
                    case Lighthouse: Post(l.X, l.Z, 4.2, l.Y + 26); break;
                    case Observatory: Post(l.X, l.Z, 8.5, l.Y + 14); break;
                    case WindFarm:
                        foreach (var p in l.Parts) Post(p.x, p.z, 2.4, p.y + 60);
                        break;
                    case Arch:
                    {
                        // Two pillars 16 m apart across the yaw, the span drivable beneath.
                        double rx = Math.Cos(l.Yaw), rz = -Math.Sin(l.Yaw);
                        Post(l.X - rx * 8, l.Z - rz * 8, 2.6, l.Y + 18);
                        Post(l.X + rx * 8, l.Z + rz * 8, 2.6, l.Y + 18);
                        break;
                    }
                }
            }
        }
    }
}
