// A road centreline sampled every few metres, with fast nearest-point
// queries. Ported from the web version's src/world/road.ts (Road).
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    public sealed class NearestResult
    {
        /// <summary>Segment index (sample i to i+1) and fraction along it.</summary>
        public int i;
        public double t;
        /// <summary>Arc length along the road, m.</summary>
        public double s;
        /// <summary>Signed lateral offset, + = right of the direction of travel.</summary>
        public double lateral;
        /// <summary>Unsigned distance from the centreline, m.</summary>
        public double dist;
        /// <summary>Road surface height at the projection.</summary>
        public double y;
    }

    public sealed class RoadLine
    {
        public int n;
        public bool closed;
        public double[] x, y, z;
        /// <summary>Unit tangent.</summary>
        public double[] tx, tz;
        /// <summary>Arc length at each sample.</summary>
        public double[] s;
        /// <summary>Signed curvature, 1/m (+ = turning right).</summary>
        public double[] curvature;
        public double halfWidth;
        public double length;
        readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        const double Cell = 32;

        RoadLine() { }

        /// <summary>From points: arc lengths, tangents and curvature computed like the TS Road.</summary>
        public static RoadLine FromPoints(double[] px, double[] py, double[] pz, bool closed, double halfWidth)
        {
            int n = px.Length;
            var r = new RoadLine
            {
                n = n, closed = closed, halfWidth = halfWidth,
                x = (double[])px.Clone(), y = (double[])py.Clone(), z = (double[])pz.Clone(),
                tx = new double[n], tz = new double[n], s = new double[n], curvature = new double[n],
            };
            double len = 0;
            for (int i = 0; i < n; i++)
            {
                r.s[i] = len;
                int j = r.Next(i);
                if (j < 0) break;
                len += r.SegLength(i, j);
            }
            r.length = len;
            for (int i = 0; i < n; i++)
            {
                int ia = closed ? (i - 1 + n) % n : Math.Max(0, i - 1);
                int ib = closed ? (i + 1) % n : Math.Min(n - 1, i + 1);
                double dx = r.x[ib] - r.x[ia];
                double dz = r.z[ib] - r.z[ia];
                double l = Math.Sqrt(dx * dx + dz * dz);
                if (l == 0) l = 1;
                r.tx[i] = dx / l;
                r.tz[i] = dz / l;
            }
            r.ComputeCurvature();
            r.BuildGrid();
            return r;
        }

        /// <summary>From exported samples (already with tangents and arc lengths).</summary>
        public static RoadLine FromSamples(double[] x, double[] y, double[] z, double[] tx, double[] tz, double[] s, double length, bool closed, double halfWidth = 0)
        {
            var r = new RoadLine
            {
                n = x.Length, closed = closed, halfWidth = halfWidth, x = x, y = y, z = z, tx = tx, tz = tz, s = s,
                length = length, curvature = new double[x.Length],
            };
            r.ComputeCurvature();
            r.BuildGrid();
            return r;
        }

        void ComputeCurvature()
        {
            for (int i = 0; i < n; i++)
            {
                int ia = closed ? (i - 2 + n) % n : Math.Max(0, i - 2);
                int ib = closed ? (i + 2) % n : Math.Min(n - 1, i + 2);
                double a1 = DMath.Atan2(tz[ia], tx[ia]);
                double a2 = DMath.Atan2(tz[ib], tx[ib]);
                double d = a2 - a1;
                while (d > Math.PI) d -= Math.PI * 2;
                while (d < -Math.PI) d += Math.PI * 2;
                double ds = closed ? 4 * (length / n) : Math.Max(1e-3, s[ib] - s[ia]);
                // Heading atan2(tz, tx) increases clockwise seen from above (x east, z south): a right turn.
                curvature[i] = d / ds;
            }
        }

        public double SegLength(int i, int j)
        {
            double dx = x[j] - x[i];
            double dz = z[j] - z[i];
            return Math.Sqrt(dx * dx + dz * dz);
        }

        public int Next(int i)
        {
            if (i + 1 < n) return i + 1;
            return closed ? 0 : -1;
        }

        static long Key(long gx, long gz) => gx * 100003 + gz;

        void BuildGrid()
        {
            grid.Clear();
            double pad = halfWidth + 30;
            for (int i = 0; i < n; i++)
            {
                int j = Next(i);
                if (j < 0) break;
                long x0 = (long)Math.Floor((Math.Min(x[i], x[j]) - pad) / Cell);
                long x1 = (long)Math.Floor((Math.Max(x[i], x[j]) + pad) / Cell);
                long z0 = (long)Math.Floor((Math.Min(z[i], z[j]) - pad) / Cell);
                long z1 = (long)Math.Floor((Math.Max(z[i], z[j]) + pad) / Cell);
                for (long gx = x0; gx <= x1; gx++)
                {
                    for (long gz = z0; gz <= z1; gz++)
                    {
                        long key = Key(gx, gz);
                        if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                        list.Add(i);
                    }
                }
            }
        }

        /// <summary>Nearest point on the centreline within about (halfWidth + 30) m; false when farther.</summary>
        public bool Nearest(double px, double pz, NearestResult o)
        {
            if (!grid.TryGetValue(Key((long)Math.Floor(px / Cell), (long)Math.Floor(pz / Cell)), out var list)) return false;
            double best = double.PositiveInfinity;
            for (int k = 0; k < list.Count; k++)
            {
                int i = list[k];
                int j = Next(i);
                double ax = x[i], az = z[i];
                double dx = x[j] - ax, dz = z[j] - az;
                double l2 = dx * dx + dz * dz;
                double t = l2 > 0 ? ((px - ax) * dx + (pz - az) * dz) / l2 : 0;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                double cx = ax + dx * t, cz = az + dz * t;
                double d2 = (px - cx) * (px - cx) + (pz - cz) * (pz - cz);
                if (d2 < best)
                {
                    best = d2;
                    o.i = i;
                    o.t = t;
                }
            }
            if (double.IsPositiveInfinity(best)) return false;
            Finish(px, pz, best, o);
            return true;
        }

        void Finish(double px, double pz, double best, NearestResult o)
        {
            int i = o.i;
            int j = Next(i) < 0 ? i : Next(i);
            double t = o.t;
            double segLen = SegLength(i, j);
            double sl = segLen == 0 ? 1 : segLen;
            o.s = s[i] + segLen * t;
            double cx = x[i] + (x[j] - x[i]) * t;
            double cz = z[i] + (z[j] - z[i]) * t;
            // Lateral sign from the segment direction: right = (-tz, tx).
            double sx = (x[j] - x[i]) / sl;
            double sz = (z[j] - z[i]) / sl;
            o.lateral = (px - cx) * -sz + (pz - cz) * sx;
            o.dist = Math.Sqrt(best);
            o.y = y[i] + (y[j] - y[i]) * t;
        }

        /// <summary>Nearest point on the road from anywhere: the grid first, then a full scan.</summary>
        public void NearestAny(double px, double pz, NearestResult o)
        {
            if (Nearest(px, pz, o)) return;
            double best = double.PositiveInfinity;
            int bi = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = x[i] - px, dz = z[i] - pz;
                double d2 = dx * dx + dz * dz;
                if (d2 < best)
                {
                    best = d2;
                    bi = i;
                }
            }
            // Refine on the two segments around the closest sample.
            int s0 = bi;
            int s1 = closed ? (bi - 1 + n) % n : Math.Max(0, bi - 1);
            best = double.PositiveInfinity;
            foreach (int i in new[] { s0, s1 })
            {
                int j = Next(i);
                if (j < 0) continue;
                double ax = x[i], az = z[i];
                double dx = x[j] - ax, dz = z[j] - az;
                double l2 = dx * dx + dz * dz;
                double t = l2 > 0 ? ((px - ax) * dx + (pz - az) * dz) / l2 : 0;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                double cx = ax + dx * t, cz = az + dz * t;
                double d2 = (px - cx) * (px - cx) + (pz - cz) * (pz - cz);
                if (d2 < best)
                {
                    best = d2;
                    o.i = i;
                    o.t = t;
                }
            }
            Finish(px, pz, best, o);
        }

        /// <summary>Position and unit tangent at arc length s (wraps on closed roads).</summary>
        public void At(double sAt, out double ox, out double oy, out double oz, out double otx, out double otz, out int oi)
        {
            double ss = sAt;
            if (closed)
            {
                ss %= length;
                if (ss < 0) ss += length;
            }
            else ss = Math.Max(0, Math.Min(length, ss));
            int lo = 0, hi = n - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (s[mid] <= ss) lo = mid;
                else hi = mid - 1;
            }
            int i = lo;
            int j = Next(i) < 0 ? i : Next(i);
            double segLen = j == i ? 1 : (j == 0 ? length : s[j]) - s[i];
            double t = j == i ? 0 : Math.Max(0, Math.Min(1, (ss - s[i]) / segLen));
            ox = x[i] + (x[j] - x[i]) * t;
            oy = y[i] + (y[j] - y[i]) * t;
            oz = z[i] + (z[j] - z[i]) * t;
            otx = tx[i] + (tx[j] - tx[i]) * t;
            otz = tz[i] + (tz[j] - tz[i]) * t;
            double l = Math.Sqrt(otx * otx + otz * otz);
            if (l == 0) l = 1;
            otx /= l;
            otz /= l;
            oi = i;
        }
    }
}
