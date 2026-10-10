// Static world colliders: walls and barriers as line segments, and posts,
// trees and rocks as circles, stored in a spatial hash. Ported from the web
// version's src/world/colliders.ts (same query order, so same results).
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    public enum SegmentKind { Wall, Barrier, Tires, Rail, Fence }

    public enum CircleKind { Post, Tree, Rock, Pier, Building }

    public sealed class Segment
    {
        public double ax, az, bx, bz;
        /// <summary>Top of the barrier, m (cars higher than this pass over).</summary>
        public double top;
        /// <summary>Bottom, m.</summary>
        public double bottom;
        /// <summary>Restitution (bounciness) and friction for impacts.</summary>
        public double bounce, friction;
        public SegmentKind kind;
    }

    public sealed class Circle
    {
        public double x, z, r, top, bottom;
        public CircleKind kind;
        /// <summary>Breakable props (cones, signs, fences, bushes) shatter instead of stopping the car.</summary>
        public bool breakable;
        public bool broken;
        public int id;
    }

    public sealed class Colliders
    {
        public readonly List<Segment> Segments = new List<Segment>();
        public readonly List<Circle> Circles = new List<Circle>();
        readonly Dictionary<long, List<int>> segGrid = new Dictionary<long, List<int>>();
        readonly Dictionary<long, List<int>> circGrid = new Dictionary<long, List<int>>();
        readonly HashSet<int> seen = new HashSet<int>();
        public readonly double Cell;

        public Colliders(double cell = 24) { Cell = cell; }

        static long Key(long gx, long gz) => gx * 100003 + gz;

        public void AddSegment(Segment s)
        {
            Segments.Add(s);
            int idx = Segments.Count - 1;
            long x0 = (long)Math.Floor(Math.Min(s.ax, s.bx) / Cell);
            long x1 = (long)Math.Floor(Math.Max(s.ax, s.bx) / Cell);
            long z0 = (long)Math.Floor(Math.Min(s.az, s.bz) / Cell);
            long z1 = (long)Math.Floor(Math.Max(s.az, s.bz) / Cell);
            for (long gx = x0; gx <= x1; gx++)
            {
                for (long gz = z0; gz <= z1; gz++)
                {
                    long k = Key(gx, gz);
                    if (!segGrid.TryGetValue(k, out var l)) segGrid[k] = l = new List<int>();
                    l.Add(idx);
                }
            }
        }

        public void AddCircle(Circle c)
        {
            Circles.Add(c);
            int idx = Circles.Count - 1;
            c.id = idx;
            double g = Cell;
            for (long gx = (long)Math.Floor((c.x - c.r) / g); gx <= (long)Math.Floor((c.x + c.r) / g); gx++)
            {
                for (long gz = (long)Math.Floor((c.z - c.r) / g); gz <= (long)Math.Floor((c.z + c.r) / g); gz++)
                {
                    long k = Key(gx, gz);
                    if (!circGrid.TryGetValue(k, out var l)) circGrid[k] = l = new List<int>();
                    l.Add(idx);
                }
            }
        }

        /// <summary>Extra circles generated on demand (the island's trees), visited after the grid.</summary>
        public Action<double, double, double, Action<Circle>> Extra;

        /// <summary>Visit colliders whose cells overlap a circle of radius r around (x, z).</summary>
        public void Query(double x, double z, double r, Action<Segment> onSeg, Action<Circle> onCircle)
        {
            QueryGrid(x, z, r, onSeg, onCircle);
            Extra?.Invoke(x, z, r, onCircle);
        }

        void QueryGrid(double x, double z, double r, Action<Segment> onSeg, Action<Circle> onCircle)
        {
            double g = Cell;
            seen.Clear();
            long gx0 = (long)Math.Floor((x - r) / g);
            long gx1 = (long)Math.Floor((x + r) / g);
            long gz0 = (long)Math.Floor((z - r) / g);
            long gz1 = (long)Math.Floor((z + r) / g);
            for (long gx = gx0; gx <= gx1; gx++)
            {
                for (long gz = gz0; gz <= gz1; gz++)
                {
                    long k = Key(gx, gz);
                    if (segGrid.TryGetValue(k, out var sl))
                    {
                        foreach (int i in sl)
                        {
                            if (!seen.Add(i)) continue;
                            onSeg(Segments[i]);
                        }
                    }
                    if (circGrid.TryGetValue(k, out var cl))
                    {
                        foreach (int i in cl)
                        {
                            if (!seen.Add(-1 - i)) continue;
                            Circle c = Circles[i];
                            if (!c.broken) onCircle(c);
                        }
                    }
                }
            }
        }
    }
}
