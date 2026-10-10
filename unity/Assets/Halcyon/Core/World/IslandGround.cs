// The island's drivable ground for the vehicle physics, from WorldData: the
// terrain mesh, road decks (bridges, kerbs, the deck behind guardrails), the
// proving ground's paved areas, plus tree trunks and static colliders.
// A port of the web version's WorldGround (src/world/world.ts), the proving
// overlay in src/sim.ts and TestTrackGround (src/world/testtrack.ts).
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    public sealed class RoadHit
    {
        public int edge;
        public int i;
        public double t;
        /// <summary>Signed lateral offset (+ right of the edge direction).</summary>
        public double lateral;
        public double dist;
        public double y;
        public double halfWidth;
        public bool bridge;
    }

    public sealed class IslandGround : IGround
    {
        public readonly WorldData W;
        readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        const double Cell = 64;
        const double Reach = 60;
        readonly RoadHit hit = new RoadHit();
        readonly GroundHit terr = new GroundHit();
        readonly NearestResult near = new NearestResult();
        readonly Circle treeCircle = new Circle { kind = CircleKind.Tree };

        /// <summary>Bushes cars have driven through (by tree id), part of the simulation state.</summary>
        public readonly HashSet<long> BrokenTrees = new HashSet<long>();

        public IslandGround(WorldData w)
        {
            W = w;
            foreach (var e in w.Edges)
                for (int i = 0; i + 1 < e.n; i++) InsertSeg(e, i);
        }

        static long Key(long gx, long gz) => gx * 100003 + gz;

        void InsertSeg(RoadEdge e, int i)
        {
            double pad = e.info.halfWidth + Reach;
            long x0 = (long)Math.Floor((Math.Min(e.x[i], e.x[i + 1]) - pad) / Cell);
            long x1 = (long)Math.Floor((Math.Max(e.x[i], e.x[i + 1]) + pad) / Cell);
            long z0 = (long)Math.Floor((Math.Min(e.z[i], e.z[i + 1]) - pad) / Cell);
            long z1 = (long)Math.Floor((Math.Max(e.z[i], e.z[i + 1]) + pad) / Cell);
            for (long gx = x0; gx <= x1; gx++)
            {
                for (long gz = z0; gz <= z1; gz++)
                {
                    long key = Key(gx, gz);
                    if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<int>();
                    l.Add(e.id * 65536 + i);
                }
            }
        }

        /// <summary>
        /// Nearest road surface point whose distance beyond its paved edge is at
        /// most maxDist (searching the roads within ~60 m). False if none.
        /// </summary>
        public bool NearestRoad(double x, double z, double maxDist, RoadHit o)
        {
            if (!grid.TryGetValue(Key((long)Math.Floor(x / Cell), (long)Math.Floor(z / Cell)), out var list)) return false;
            double best = double.PositiveInfinity;
            var edges = W.Edges;
            for (int k = 0; k < list.Count; k++)
            {
                int packed = list[k];
                int e = packed / 65536;
                int i = packed - e * 65536;
                RoadEdge r = edges[e];
                double ax = r.x[i], az = r.z[i];
                double dx = r.x[i + 1] - ax, dz = r.z[i + 1] - az;
                double l2 = dx * dx + dz * dz;
                double t = l2 > 0 ? ((x - ax) * dx + (z - az) * dz) / l2 : 0;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                double cx = ax + dx * t, cz = az + dz * t;
                double d = Math.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
                // Compare by distance beyond each road's own edge.
                double score = d - r.info.halfWidth;
                if (score < best)
                {
                    best = score;
                    double len = Math.Sqrt(l2);
                    if (len == 0) len = 1;
                    o.edge = e;
                    o.i = i;
                    o.t = t;
                    o.dist = d;
                    o.lateral = (x - cx) * (-dz / len) + (z - cz) * (dx / len);
                    o.y = r.y[i] + (r.y[i + 1] - r.y[i]) * t;
                    o.halfWidth = r.info.halfWidth;
                    o.bridge = r.bridge[i] == 1 || r.bridge[i + 1] == 1;
                }
            }
            return best <= maxDist;
        }

        /// <summary>Terrain only: height and normal of the mesh triangle, surface and water of the nearest vertex.</summary>
        public void GroundAt(double x, double z, GroundHit o)
        {
            var w = W;
            int n = w.N;
            double lx = (x - w.Origin) / w.Spacing;
            double lz = (z - w.Origin) / w.Spacing;
            int i = (int)Math.Floor(lx);
            int j = (int)Math.Floor(lz);
            if (i < 0) i = 0;
            if (j < 0) j = 0;
            if (i > n - 2) i = n - 2;
            if (j > n - 2) j = n - 2;
            double u = lx - i;
            double v = lz - j;
            int k = j * n + i;
            double h00 = w.Height[k], h10 = w.Height[k + 1], h01 = w.Height[k + n], h11 = w.Height[k + n + 1];
            double sp = w.Spacing;
            double y, dhdx, dhdz;
            // Two triangles split along (0,0)-(1,1), matching the mesh index order.
            if (u >= v)
            {
                y = h00 + (h10 - h00) * u + (h11 - h10) * v;
                dhdx = (h10 - h00) / sp;
                dhdz = (h11 - h10) / sp;
            }
            else
            {
                y = h00 + (h01 - h00) * v + (h11 - h01) * u;
                dhdx = (h11 - h01) / sp;
                dhdz = (h01 - h00) / sp;
            }
            double l = Math.Sqrt(dhdx * dhdx + 1 + dhdz * dhdz);
            o.y = y;
            o.nx = -dhdx / l;
            o.ny = 1 / l;
            o.nz = -dhdz / l;
            int ni = u < 0.5 ? i : i + 1;
            int nj = v < 0.5 ? j : j + 1;
            int nk = nj * n + ni;
            o.surface = w.SurfaceGrid[nk];
            short wl = w.Water[nk];
            double level = wl == short.MinValue ? -1e9 : wl / 100.0;
            o.water = level > y ? level - y : 0;
        }

        public bool Sample(double x, double z, double yRef, GroundHit o)
        {
            var w = W;
            if (Math.Abs(x) > w.WorldHalf || Math.Abs(z) > w.WorldHalf) return false;
            if (ProvingOverlay(x, z, o)) return true;
            GroundHit t = terr;
            GroundAt(x, z, t);
            RoadHit h = hit;
            // Bridge decks run out to the parapets and city streets have raised
            // sidewalks; elsewhere the shoulder is terrain.
            if (NearestRoad(x, z, w.ShoulderMax, h) && h.y <= yRef + 1.6)
            {
                RoadEdge e = w.Edges[h.edge];
                RoadClassInfo info = e.info;
                bool paved = h.dist <= h.halfWidth;
                // Behind a guardrail the shoulder is a retaining-wall deck out to the rail.
                double ro = paved ? 0 : e.railOffsets[h.i * 2 + (h.lateral > 0 ? 1 : 0)];
                bool railed = ro > 0 && h.dist <= ro;
                bool onDeck = paved || railed || ((h.bridge || info.sidewalk) && h.dist <= h.halfWidth + info.shoulder);
                // The paved surface always wins under the wheels (there are no tunnels:
                // terrain above a road is an interpolation artifact); sidewalks and
                // bridge shoulders only where they are not below the ground.
                if (onDeck && (paved || h.bridge || h.y >= t.y - 0.3))
                {
                    bool kerb = info.sidewalk && h.dist > h.halfWidth;
                    o.y = h.y + (kerb ? w.CurbHeight : 0);
                    // Road normal from the profile slope along the road.
                    int i = h.i;
                    double ds = e.s[i + 1] - e.s[i];
                    if (ds == 0) ds = 1;
                    double gy = (e.y[i + 1] - e.y[i]) / ds;
                    double l = Math.Sqrt(1 + gy * gy);
                    o.nx = (-e.tx[i] * gy) / l;
                    o.ny = 1 / l;
                    o.nz = (-e.tz[i] * gy) / l;
                    o.surface = kerb ? Surface.Concrete : info.surface;
                    o.water = 0;
                    Bumps.Apply(x, z, o);
                    return true;
                }
            }
            o.CopyFrom(t);
            Bumps.Apply(x, z, o);
            return true;
        }

        /// <summary>Height of the drivable surface at (x, z) from above (for placing cars).</summary>
        public double GroundHeight(double x, double z)
        {
            var g = new GroundHit();
            return Sample(x, z, 1e5, g) ? g.y : 0;
        }

        // --- Proving ground ---------------------------------------------------------
        bool ProvingOverlay(double x, double z, GroundHit o)
        {
            var p = W.Proving;
            if (x < p.originX - 800 || x > p.originX + 720 || z < p.originZ - 130 || z > p.originZ + 540) return false;
            int surf = ProvingPavedAt(x, z);
            if (surf < 0) return false;
            o.y = p.height;
            o.nx = 0;
            o.ny = 1;
            o.nz = 0;
            o.surface = surf;
            o.water = 0;
            Bumps.Apply(x, z, o);
            return true;
        }

        /// <summary>The proving ground's surface at (x, z), or -1 off its paved areas.</summary>
        public int ProvingPavedAt(double x, double z)
        {
            int s = ProvingSurfaceAt(x, z);
            return s == Surface.Grass ? -1 : s;
        }

        public int ProvingSurfaceAt(double x, double z)
        {
            var p = W.Proving;
            if (x >= p.paddockX0 && x <= p.paddockX1 && z >= p.paddockZ0 && z <= p.paddockZ1) return Surface.Asphalt;
            double dr = Math.Sqrt((x - p.skidX) * (x - p.skidX) + (z - p.skidZ) * (z - p.skidZ));
            if (Math.Abs(dr - p.skidR) <= p.skidWidth / 2) return Surface.Asphalt;
            if (dr < p.skidR - p.skidWidth / 2 && dr > p.skidR - p.skidWidth / 2 - 3) return Surface.Concrete;
            if (!p.road.Nearest(x, z, near)) return Surface.Grass;
            double lat = near.lateral;
            double a = Math.Abs(lat);
            if (a <= p.halfWidth) return Surface.Asphalt;
            int i = near.i;
            int kind = p.cornerKind[i];
            if (kind > 0 && a <= p.halfWidth + p.curbWidth) return Surface.Curb;
            if (kind == 2 && M.Sign(lat) == p.outside[i] && a <= p.halfWidth + p.curbWidth + p.gravelWidth) return Surface.Gravel;
            // Paved shoulder on the straight.
            double lx = x - p.originX;
            double lz = z - p.originZ;
            if (kind == 0 && a <= p.halfWidth + 2 && Math.Abs(lz) < 20 && lx > -720 && lx < 520) return Surface.Concrete;
            return Surface.Grass;
        }

        // --- Trees and colliders ----------------------------------------------------------
        /// <summary>Visit tree trunks within r of (x, z) as collision circles (one reused object).</summary>
        public void QueryTrees(double x, double z, double r, Action<Circle> cb)
        {
            var w = W;
            double tile = w.TreeTile;
            int half = w.TreeTilesX / 2;
            int tx0 = (int)Math.Floor((x - r) / tile);
            int tx1 = (int)Math.Floor((x + r) / tile);
            int tz0 = (int)Math.Floor((z - r) / tile);
            int tz1 = (int)Math.Floor((z + r) / tile);
            Circle c = treeCircle;
            double rr = r + 1;
            for (int tx = tx0; tx <= tx1; tx++)
            {
                for (int tz = tz0; tz <= tz1; tz++)
                {
                    int ti = tx + half;
                    int tj = tz + w.TreeTilesZ / 2;
                    if (ti < 0 || tj < 0 || ti >= w.TreeTilesX || tj >= w.TreeTilesZ) continue;
                    TreeTile tr = w.TreeTiles[tj * w.TreeTilesX + ti];
                    for (int k = 0; k < tr.count; k++)
                    {
                        int kind = tr.kind[k];
                        double dx = tr.x[k] - x;
                        double dz = tr.z[k] - z;
                        if (dx * dx + dz * dz > rr * rr) continue;
                        c.x = tr.x[k];
                        c.z = tr.z[k];
                        c.r = TreeKind.Trunk[kind] * tr.scale[k];
                        c.bottom = tr.y[k] - 1;
                        c.top = tr.y[k] + 6;
                        c.breakable = kind == TreeKind.Bush;
                        // Flattened bushes stay flattened until the session resets.
                        long id = ((long)tx * 4096 + tz) * 64 + k;
                        if (c.breakable && BrokenTrees.Contains(id)) continue;
                        c.broken = false;
                        cb(c);
                        if (c.broken) BrokenTrees.Add(id);
                    }
                }
            }
        }

        /// <summary>All static colliders, with tree trunks supplied on demand.</summary>
        public Colliders BuildColliders()
        {
            var c = new Colliders(24);
            foreach (var s in W.Segments)
            {
                c.AddSegment(new Segment
                {
                    ax = s.ax, az = s.az, bx = s.bx, bz = s.bz, top = s.top, bottom = s.bottom,
                    bounce = s.bounce, friction = s.friction, kind = s.kind,
                });
            }
            foreach (var s in W.Circles)
                c.AddCircle(new Circle { x = s.x, z = s.z, r = s.r, top = s.top, bottom = s.bottom, kind = s.kind, breakable = s.breakable });
            c.Extra = QueryTrees;
            return c;
        }
    }
}
