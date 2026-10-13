// The road an event is run on: a centreline resampled every few metres with
// the drivable half width on each side, closed for circuits and open for
// sprints. Progress along it (arc length s) is how races are scored and how
// the AI finds its place on the racing line. Pure C#, no UnityEngine.
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    public sealed class RaceRoute
    {
        /// <summary>Target spacing of the resampled centreline, m.</summary>
        public const double Spacing = 3.0;

        public string Name;
        public bool Closed;
        public int N;
        public double[] X, Y, Z;
        /// <summary>Unit tangent along the direction of travel.</summary>
        public double[] Tx, Tz;
        /// <summary>Arc length at each sample.</summary>
        public double[] S;
        /// <summary>Drivable half width left and right of the centreline (where a car's centre may go plus its half width).</summary>
        public double[] WLeft, WRight;
        public double Length;

        RaceRoute() { }

        /// <summary>
        /// From a polyline (x, y, z) with half widths per point. The polyline is
        /// resampled evenly; closed routes join the last point back to the first.
        /// </summary>
        public static RaceRoute FromPolyline(string name, IList<double> x, IList<double> y, IList<double> z, IList<double> wLeft, IList<double> wRight, bool closed)
        {
            int m = x.Count;
            if (m < 2) throw new ArgumentException("route needs at least two points");
            // Cumulative length of the input.
            int segs = closed ? m : m - 1;
            var cum = new double[segs + 1];
            for (int i = 0; i < segs; i++)
            {
                int j = (i + 1) % m;
                double dx = x[j] - x[i], dz = z[j] - z[i];
                cum[i + 1] = cum[i] + Math.Sqrt(dx * dx + dz * dz);
            }
            double L = cum[segs];
            int n = Math.Max(4, (int)Math.Round(L / Spacing));
            var r = new RaceRoute { Name = name, Closed = closed, Length = L };
            int count = closed ? n : n + 1;
            r.N = count;
            r.X = new double[count];
            r.Y = new double[count];
            r.Z = new double[count];
            r.Tx = new double[count];
            r.Tz = new double[count];
            r.S = new double[count];
            r.WLeft = new double[count];
            r.WRight = new double[count];
            int seg = 0;
            for (int k = 0; k < count; k++)
            {
                double s = L * k / n;
                while (seg < segs - 1 && cum[seg + 1] < s) seg++;
                int i = seg, j = (seg + 1) % m;
                double len = cum[seg + 1] - cum[seg];
                double t = len > 0 ? (s - cum[seg]) / len : 0;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                r.X[k] = x[i] + (x[j] - x[i]) * t;
                r.Y[k] = y[i] + (y[j] - y[i]) * t;
                r.Z[k] = z[i] + (z[j] - z[i]) * t;
                r.WLeft[k] = wLeft[i] + (wLeft[j] - wLeft[i]) * t;
                r.WRight[k] = wRight[i] + (wRight[j] - wRight[i]) * t;
                r.S[k] = s;
            }
            for (int k = 0; k < count; k++)
            {
                int a = closed ? (k - 1 + count) % count : Math.Max(0, k - 1);
                int b = closed ? (k + 1) % count : Math.Min(count - 1, k + 1);
                double dx = r.X[b] - r.X[a], dz = r.Z[b] - r.Z[a];
                double l = Math.Sqrt(dx * dx + dz * dz);
                if (l == 0) l = 1;
                r.Tx[k] = dx / l;
                r.Tz[k] = dz / l;
            }
            return r;
        }

        /// <summary>The proving-ground circuit, starting at its start line, run in its own direction.</summary>
        public static RaceRoute FromProving(ProvingCircuit pc, string name = "Proving Ground")
        {
            var road = pc.road;
            int n = road.n;
            // Start the samples at the start line so s = 0 there.
            int i0 = 0;
            while (i0 + 1 < n && road.s[i0 + 1] <= pc.startS) i0++;
            var xs = new List<double>();
            var ys = new List<double>();
            var zs = new List<double>();
            var wl = new List<double>();
            road.At(pc.startS, out double sx, out double sy, out double sz, out _, out _, out _);
            xs.Add(sx);
            ys.Add(sy);
            zs.Add(sz);
            double w = pc.halfWidth + pc.curbWidth * 0.6;
            wl.Add(w);
            for (int k = 1; k <= n; k++)
            {
                int i = (i0 + k) % n;
                xs.Add(road.x[i]);
                ys.Add(road.y[i]);
                zs.Add(road.z[i]);
                wl.Add(w);
            }
            // The last added point is sample i0 again (just before the start): drop duplicates closer than 0.5 m to the start.
            int last = xs.Count - 1;
            double ddx = xs[last] - sx, ddz = zs[last] - sz;
            if (ddx * ddx + ddz * ddz < 0.25)
            {
                xs.RemoveAt(last);
                ys.RemoveAt(last);
                zs.RemoveAt(last);
                wl.RemoveAt(last);
            }
            return FromPolyline(name, xs, ys, zs, wl, wl, true);
        }

        public double Wrap(double s)
        {
            if (!Closed) return s < 0 ? 0 : s > Length ? Length : s;
            s %= Length;
            return s < 0 ? s + Length : s;
        }

        /// <summary>Signed difference b - a along a closed route, in (-L/2, L/2].</summary>
        public double Delta(double a, double b)
        {
            double d = b - a;
            if (!Closed) return d;
            d %= Length;
            if (d > Length / 2) d -= Length;
            if (d <= -Length / 2) d += Length;
            return d;
        }

        int Index(double s, out double t)
        {
            double ss = Wrap(s);
            int n = Closed ? N : N - 1;
            double f = ss / Length * n;
            int i = (int)Math.Floor(f);
            if (i >= n) i = n - 1;
            if (i < 0) i = 0;
            t = f - i;
            return i;
        }

        int Next(int i) => Closed ? (i + 1) % N : Math.Min(N - 1, i + 1);

        /// <summary>Centreline point, height and unit tangent at arc length s.</summary>
        public void At(double s, out double x, out double y, out double z, out double tx, out double tz)
        {
            int i = Index(s, out double t);
            int j = Next(i);
            x = X[i] + (X[j] - X[i]) * t;
            y = Y[i] + (Y[j] - Y[i]) * t;
            z = Z[i] + (Z[j] - Z[i]) * t;
            tx = Tx[i] + (Tx[j] - Tx[i]) * t;
            tz = Tz[i] + (Tz[j] - Tz[i]) * t;
            double l = Math.Sqrt(tx * tx + tz * tz);
            if (l == 0) l = 1;
            tx /= l;
            tz /= l;
        }

        /// <summary>Linear interpolation of a per-sample array at arc length s.</summary>
        public double Sample(double[] a, double s)
        {
            int i = Index(s, out double t);
            return a[i] + (a[Next(i)] - a[i]) * t;
        }

        /// <summary>
        /// Nearest point of the centreline to (px, pz), searching only arc lengths
        /// within [hint - back, hint + ahead]. Gives s, the signed lateral offset
        /// (+ = right of the direction of travel) and the distance.
        /// </summary>
        public void Project(double px, double pz, double hint, double back, double ahead, out double s, out double lateral, out double dist)
        {
            int i0 = Index(hint - back, out _);
            int steps = (int)Math.Ceiling((back + ahead) / (Length / (Closed ? N : N - 1))) + 2;
            ProjectRange(px, pz, i0, steps, out s, out lateral, out dist);
        }

        /// <summary>Nearest point anywhere on the route.</summary>
        public void ProjectAny(double px, double pz, out double s, out double lateral, out double dist)
        {
            ProjectRange(px, pz, 0, Closed ? N : N - 1, out s, out lateral, out dist);
        }

        void ProjectRange(double px, double pz, int i0, int steps, out double s, out double lateral, out double dist)
        {
            int segs = Closed ? N : N - 1;
            double best = double.PositiveInfinity;
            s = 0;
            lateral = 0;
            for (int k = 0; k < steps && k < segs; k++)
            {
                int i = Closed ? (i0 + k) % N : i0 + k;
                if (i >= segs) break;
                int j = Next(i);
                double ax = X[i], az = Z[i];
                double dx = X[j] - ax, dz = Z[j] - az;
                double l2 = dx * dx + dz * dz;
                double t = l2 > 0 ? ((px - ax) * dx + (pz - az) * dz) / l2 : 0;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                double cx = ax + dx * t, cz = az + dz * t;
                double d2 = (px - cx) * (px - cx) + (pz - cz) * (pz - cz);
                if (d2 < best)
                {
                    best = d2;
                    double sl = Math.Sqrt(l2);
                    if (sl == 0) sl = 1;
                    s = S[i] + (j == 0 && Closed ? Length - S[i] : S[j] - S[i]) * t;
                    lateral = (px - cx) * (-dz / sl) + (pz - cz) * (dx / sl);
                }
            }
            dist = Math.Sqrt(best);
            s = Wrap(s);
        }
    }

    /// <summary>Shortest routes over the island's road network (A*), as RaceRoutes.</summary>
    public static class RouteFinder
    {
        /// <summary>
        /// The road route from (ax, az) to (bx, bz), each snapped to its nearest road; null if unreachable.
        /// When nodes is given it receives the junctions the route passes through, in order.
        /// </summary>
        public static RaceRoute Find(WorldData w, IslandGround ground, double ax, double az, double bx, double bz, string name, List<int> nodes = null)
        {
            nodes?.Clear();
            var a = Snap(w, ground, ax, az);
            var b = Snap(w, ground, bx, bz);
            if (a == null || b == null) return null;
            RoadEdge ea = w.Edges[a.edge], eb = w.Edges[b.edge];
            double sA = ea.s[a.i] + (ea.s[Math.Min(ea.n - 1, a.i + 1)] - ea.s[a.i]) * a.t;
            double sB = eb.s[b.i] + (eb.s[Math.Min(eb.n - 1, b.i + 1)] - eb.s[b.i]) * b.t;
            var xs = new List<double>();
            var ys = new List<double>();
            var zs = new List<double>();
            var ws = new List<double>();
            if (a.edge == b.edge)
            {
                Slice(w, a.edge, sA, sB, xs, ys, zs, ws);
                return xs.Count >= 2 ? RaceRoute.FromPolyline(name, xs, ys, zs, ws, ws, false) : null;
            }
            int n = w.Nodes.Length;
            var g = new double[n];
            var came = new int[n];
            var via = new int[n];
            var closed = new bool[n];
            for (int k = 0; k < n; k++)
            {
                g[k] = double.PositiveInfinity;
                came[k] = -1;
                via[k] = -1;
            }
            double H(int k)
            {
                double dx = w.Nodes[k].x - bx, dz = w.Nodes[k].z - bz;
                return Math.Sqrt(dx * dx + dz * dz);
            }
            var open = new List<int>();
            g[ea.a] = sA;
            g[ea.b] = ea.length - sA;
            open.Add(ea.a);
            open.Add(ea.b);
            double ToGoal(int k) => k == eb.a ? sB : k == eb.b ? eb.length - sB : double.PositiveInfinity;
            double best = double.PositiveInfinity;
            int bestEnd = -1;
            while (open.Count > 0)
            {
                int bi = 0;
                for (int k = 1; k < open.Count; k++) if (g[open[k]] + H(open[k]) < g[open[bi]] + H(open[bi])) bi = k;
                int u = open[bi];
                open.RemoveAt(bi);
                if (closed[u]) continue;
                closed[u] = true;
                if (g[u] >= best) break;
                double tg = ToGoal(u);
                if (g[u] + tg < best)
                {
                    best = g[u] + tg;
                    bestEnd = u;
                }
                foreach (int eid in w.Nodes[u].edges)
                {
                    var e = w.Edges[eid];
                    int v = e.a == u ? e.b : e.a;
                    double ng = g[u] + e.length;
                    if (ng < g[v])
                    {
                        g[v] = ng;
                        came[v] = u;
                        via[v] = eid;
                        open.Add(v);
                    }
                }
            }
            if (bestEnd < 0) return null;
            var chain = new List<int>();
            for (int k = bestEnd; k >= 0; k = came[k]) chain.Add(k);
            chain.Reverse();
            nodes?.AddRange(chain);
            int first = chain[0];
            Slice(w, a.edge, sA, first == ea.a ? 0 : ea.length, xs, ys, zs, ws);
            for (int k = 1; k < chain.Count; k++)
            {
                int eid = via[chain[k]];
                var e = w.Edges[eid];
                bool fromA = e.a == chain[k - 1];
                Slice(w, eid, fromA ? 0 : e.length, fromA ? e.length : 0, xs, ys, zs, ws);
            }
            int last = chain[chain.Count - 1];
            Slice(w, b.edge, last == eb.a ? 0 : eb.length, sB, xs, ys, zs, ws);
            // Drop repeated points where edges meet.
            for (int k = xs.Count - 1; k > 0; k--)
            {
                double dx = xs[k] - xs[k - 1], dz = zs[k] - zs[k - 1];
                if (dx * dx + dz * dz < 0.25)
                {
                    xs.RemoveAt(k);
                    ys.RemoveAt(k);
                    zs.RemoveAt(k);
                    ws.RemoveAt(k);
                }
            }
            return xs.Count >= 2 ? RaceRoute.FromPolyline(name, xs, ys, zs, ws, ws, false) : null;
        }

        static RoadHit Snap(WorldData w, IslandGround ground, double x, double z)
        {
            var hit = new RoadHit();
            foreach (double reach in new[] { 20.0, 60.0 })
            {
                if (ground.NearestRoad(x, z, reach, hit)) return hit;
            }
            double best = double.PositiveInfinity;
            RoadHit found = null;
            // Every sample, the last of each road too (a destination off the end of a road is nearest its end).
            foreach (var e in w.Edges)
            {
                for (int i = 0; i < e.n; i++)
                {
                    double d = (e.x[i] - x) * (e.x[i] - x) + (e.z[i] - z) * (e.z[i] - z);
                    if (d < best)
                    {
                        best = d;
                        found = new RoadHit { edge = e.id, i = i, t = 0, dist = Math.Sqrt(d), y = e.y[i], halfWidth = e.info.halfWidth };
                    }
                }
            }
            return found;
        }

        /// <summary>Samples of an edge between arc lengths s0 and s1 (either direction), with the drivable half width.</summary>
        static void Slice(WorldData w, int edge, double s0, double s1, List<double> xs, List<double> ys, List<double> zs, List<double> ws)
        {
            var e = w.Edges[edge];
            // The paved road only; narrower where a guardrail or parapet stands close.
            double hw = e.info.halfWidth;
            double lo = Math.Min(s0, s1), hi = Math.Max(s0, s1);
            int start = xs.Count;
            for (int i = 0; i < e.n; i++)
            {
                if (e.s[i] < lo || e.s[i] > hi) continue;
                xs.Add(e.x[i]);
                ys.Add(e.y[i]);
                zs.Add(e.z[i]);
                double h = hw;
                if (e.railOffsets != null)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        double r = e.railOffsets[i * 2 + k];
                        if (r > 0 && !double.IsNaN(r)) h = Math.Min(h, r - 0.5);
                    }
                }
                if (e.bridge[i] == 1) h = Math.Min(h, hw - 0.3);
                ws.Add(Math.Max(2.4, h));
            }
            if (s1 < s0)
            {
                xs.Reverse(start, xs.Count - start);
                ys.Reverse(start, ys.Count - start);
                zs.Reverse(start, zs.Count - start);
                ws.Reverse(start, ws.Count - start);
            }
        }
    }
}
