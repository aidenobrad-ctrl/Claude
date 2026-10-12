// Ambient traffic on the island's roads, driving on the right, in two
// levels of detail around the player:
//   - ghosts (beyond about 220 m) glide along their lane with no physics,
//     keeping their distance from each other: cheap enough for dozens;
//   - near the player they become full physics cars in the simulation,
//     driven by a lane-keeping controller (pure pursuit on the lane line,
//     speed from the road's limit and its bends, slowing for junctions, and
//     a following gap to whatever is ahead, the player included), so the
//     player can pass, nudge or crash into them;
//   - cars appear out of sight (500 to 1100 m away) and vanish beyond
//     1400 m; a crashed car is put back on its lane once the player has
//     gone (or towed away if it cannot be).
// Races close the roads: RaceSession suspends the traffic and resumes it.
// Deterministic for a seed, like the rest of the simulation.
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    public sealed class TrafficCar
    {
        public int Id;
        public string SpecId;
        /// <summary>The car's vehicle: stepped by the physics when near, posed along its lane when a ghost.</summary>
        public Vehicle Vehicle;
        /// <summary>Its place in the simulation while it is a physics car (null for a ghost).</summary>
        public SimCar Car;
        /// <summary>Road edge, direction along it (+1 with the samples, -1 against), lane (0 nearest the centre line).</summary>
        public int Edge, Dir, Lane;
        /// <summary>Distance along the edge in the direction of travel, m.</summary>
        public double S;
        /// <summary>Ghost speed, m/s.</summary>
        public double Speed;
        /// <summary>The driver's pace on the limit (0.85 to 1.1).</summary>
        public double Cruise;
        /// <summary>The edge and direction it takes at the end of this one.</summary>
        public int NextEdge = -1, NextDir;
        /// <summary>Seconds out of its lane or stopped with nothing ahead (crashed).</summary>
        public double Trouble;
        /// <summary>Seconds stopped behind something that does not move (a parked or crashed car).</summary>
        public double Blocked;
        /// <summary>Going round it: metres still to go past it, seconds before giving up, and the sideways shift (m, negative = left).</summary>
        public double SwerveLeft, SwerveT, Swerve;
        /// <summary>Seconds waiting at a junction (after a while it goes anyway).</summary>
        public double Yielding;
        /// <summary>Seconds before it may change lane again.</summary>
        public double LaneHold;
        /// <summary>Seconds left backing up (too close behind something to steer round it).</summary>
        public double BackUp;
        /// <summary>For the presentation: a stable 0 to 1 number (paint).</summary>
        public double Look;
        public bool Physics => Car != null;
    }

    public sealed class Traffic
    {
        public readonly List<TrafficCar> Cars = new List<TrafficCar>();
        /// <summary>How many cars to keep within SpawnMax of the player.</summary>
        public int Target = 26;
        public double SpawnMin = 500, SpawnMax = 1100, Despawn = 1400;
        /// <summary>Ghosts become physics cars inside Near and go back beyond Far.</summary>
        public double Near = 220, Far = 280;
        public bool Suspended { get; private set; }
        /// <summary>Promotions, demotions, spawns, despawns and lane resets so far (for tests and the HUD).</summary>
        public int Promoted, Demoted, Spawned, Removed, Rescued, LaneChanges;
        /// <summary>Debug notes (null: off).</summary>
        public Action<string> Log;

        // The everyday cars on the island's roads, with their share of the traffic.
        static readonly (string id, double weight)[] Mix =
        {
            ("aurelle-pico", 3), ("stallworth-prairie", 3), ("ironbark-ridgeback", 2), ("aurelle-vif-r", 2),
            ("halden-280-sprint", 1), ("hoshida-kiri-rs", 1), ("nordvik-kyst-sport", 1), ("halden-aster-gt", 1),
            ("nordvik-varg", 1), ("stallworth-ironside-454", 1), ("orsini-saetta", 0.15),
        };

        const double LatAccel = 3.0;   // comfortable cornering, m/s^2
        const double Decel = 2.6;      // comfortable braking, m/s^2

        readonly Simulation sim;
        readonly WorldData w;
        readonly Rng rng;
        readonly List<int> eligible = new List<int>();
        readonly double[][] curv;
        readonly Dictionary<string, VehicleParams> paramsCache = new Dictionary<string, VehicleParams>();
        int nextId = 1;
        long tick;

        public Traffic(Simulation sim, uint seed = 0x7aff1c)
        {
            this.sim = sim;
            w = sim.World;
            rng = new Rng(seed);
            curv = new double[w.Edges.Length][];
            for (int i = 0; i < w.Edges.Length; i++)
            {
                var e = w.Edges[i];
                if (e.info.traffic > 0 && e.info.surface != Surface.Dirt && e.length > 30) eligible.Add(i);
                // Curvature per sample from the turn of the tangent.
                var k = new double[e.n];
                for (int j = 0; j < e.n; j++)
                {
                    int a = Math.Max(0, j - 1), b = Math.Min(e.n - 1, j + 1);
                    double ds = e.s[b] - e.s[a];
                    if (ds < 1e-6) continue;
                    double cross = e.tx[a] * e.tz[b] - e.tz[a] * e.tx[b];
                    double dot = e.tx[a] * e.tx[b] + e.tz[a] * e.tz[b];
                    k[j] = Math.Abs(Math.Atan2(cross, dot)) / ds;
                }
                curv[i] = k;
            }
        }

        // --- Lanes and geometry ---------------------------------------------------------------

        /// <summary>Lanes each way on a road class, and the offset of a lane's centre from the centre line.</summary>
        static int LanesEachWay(RoadClassInfo c) => c.lanes >= 2 ? c.lanes / 2 : 1;

        static double LaneOffset(RoadClassInfo c, int lane)
        {
            int each = LanesEachWay(c);
            double width = c.halfWidth / each;
            return c.lanes >= 2 ? width * (lane + 0.5) : 0;
        }

        /// <summary>Index of the sample at or before arc length s on an edge.</summary>
        static int SampleAt(RoadEdge e, double s)
        {
            int lo = 0, hi = e.n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (e.s[mid] <= s) lo = mid;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>The lane's centre at distance S along an edge in a direction: position and unit tangent of travel.</summary>
        void LanePoint(int edge, int dir, int lane, double S, out double x, out double y, out double z, out double tx, out double tz)
        {
            var e = w.Edges[edge];
            double s = dir > 0 ? S : e.length - S;
            s = Math.Max(0, Math.Min(e.length, s));
            int i = SampleAt(e, s);
            int j = Math.Min(e.n - 1, i + 1);
            double f = e.s[j] > e.s[i] ? (s - e.s[i]) / (e.s[j] - e.s[i]) : 0;
            x = e.x[i] + (e.x[j] - e.x[i]) * f;
            y = e.y[i] + (e.y[j] - e.y[i]) * f;
            z = e.z[i] + (e.z[j] - e.z[i]) * f;
            tx = (e.tx[i] + (e.tx[j] - e.tx[i]) * f) * dir;
            tz = (e.tz[i] + (e.tz[j] - e.tz[i]) * f) * dir;
            double n = Math.Sqrt(tx * tx + tz * tz);
            if (n > 1e-9) { tx /= n; tz /= n; }
            // Right of the direction of travel is (-tz, tx).
            double off = LaneOffset(e.info, lane);
            x -= tz * off;
            z += tx * off;
        }

        /// <summary>A point d metres ahead of the car on its lane (shifted sideways by extra, m), on the next edge if need be.</summary>
        void Ahead(TrafficCar c, double d, out double x, out double y, out double z, out double tx, out double tz, double extra = 0)
        {
            var e = w.Edges[c.Edge];
            double s = c.S + d;
            if (s <= e.length || c.NextEdge < 0) LanePoint(c.Edge, c.Dir, c.Lane, Math.Min(s, e.length), out x, out y, out z, out tx, out tz);
            else
            {
                var ne = w.Edges[c.NextEdge];
                int lane = Math.Min(c.Lane, LanesEachWay(ne.info) - 1);
                LanePoint(c.NextEdge, c.NextDir, lane, Math.Min(s - e.length, ne.length), out x, out y, out z, out tx, out tz);
            }
            x -= tz * extra;
            z += tx * extra;
        }

        /// <summary>No car within the stretch from 10 m behind to 70 m ahead in a corridor shifted sideways by offset (m, negative = left).</summary>
        bool Clear(TrafficCar c, double offset)
        {
            var v = c.Vehicle;
            double fx = v.fwd.x, fz = v.fwd.z;
            foreach (var car in sim.Cars)
            {
                if (car.vehicle == v) continue;
                double dx = car.vehicle.pos.x - v.pos.x, dz = car.vehicle.pos.z - v.pos.z;
                double lon = dx * fx + dz * fz, lat = dx * -fz + dz * fx;
                if (lon > -10 && lon < 70 && Math.Abs(lat - offset) < 2.2) return false;
            }
            foreach (var o in Cars)
            {
                if (o.Physics || o == c) continue;
                double dx = o.Vehicle.pos.x - v.pos.x, dz = o.Vehicle.pos.z - v.pos.z;
                double lon = dx * fx + dz * fz, lat = dx * -fz + dz * fx;
                if (lon > -10 && lon < 70 && Math.Abs(lat - offset) < 2.2) return false;
            }
            return true;
        }

        /// <summary>The road's own speed for the car over the next stretch: its limit, its bends and its junctions.</summary>
        double RoadSpeed(TrafficCar c, double v)
        {
            var e = w.Edges[c.Edge];
            double limit = e.info.traffic * c.Cruise;
            double reach = v * v / (2 * Decel) + 25;
            double best = limit;
            for (double d = 0; d <= reach; d += 6)
            {
                double s = c.S + d;
                int edge = c.Edge, dir = c.Dir;
                if (s > e.length)
                {
                    if (c.NextEdge < 0) break;
                    // A junction (three or more roads) at the end of this edge: take it at walking pace plus.
                    int node = c.Dir > 0 ? e.b : e.a;
                    if (w.Nodes[node].edges.Count >= 3) best = Math.Min(best, Math.Sqrt(9 * 9 + 2 * Decel * Math.Max(0, e.length - c.S)));
                    s -= e.length;
                    edge = c.NextEdge;
                    dir = c.NextDir;
                    if (s > w.Edges[edge].length) break;
                }
                var ee = w.Edges[edge];
                double sOn = dir > 0 ? s : ee.length - s;
                double k = curv[edge][SampleAt(ee, Math.Max(0, Math.Min(ee.length, sOn)))];
                double vk = k > 1e-5 ? Math.Sqrt(LatAccel / k) : 1e9;
                // Brake in time for it.
                best = Math.Min(best, Math.Sqrt(vk * vk + 2 * Decel * d));
            }
            return best;
        }

        /// <summary>Picks where the car goes at the end of its edge: any other road at the node, or back the way it came at a dead end.</summary>
        void ChooseNext(TrafficCar c)
        {
            var e = w.Edges[c.Edge];
            int node = c.Dir > 0 ? e.b : e.a;
            var options = new List<int>();
            foreach (int id in w.Nodes[node].edges)
            {
                if (id == c.Edge) continue;
                var o = w.Edges[id];
                if (o.info.traffic <= 0 || o.info.surface == Surface.Dirt) continue;
                options.Add(id);
            }
            if (options.Count == 0)
            {
                c.NextEdge = c.Edge;
                c.NextDir = -c.Dir;
                return;
            }
            int pick = options[rng.Int(0, options.Count)];
            c.NextEdge = pick;
            c.NextDir = w.Edges[pick].a == node ? 1 : -1;
        }

        void Advance(TrafficCar c)
        {
            var e = w.Edges[c.Edge];
            while (c.S > e.length)
            {
                c.S -= e.length;
                c.Edge = c.NextEdge;
                c.Dir = c.NextDir;
                e = w.Edges[c.Edge];
                c.Lane = Math.Min(c.Lane, LanesEachWay(e.info) - 1);
                ChooseNext(c);
            }
        }

        // --- The step ------------------------------------------------------------------------------

        /// <summary>Takes the traffic off the roads (a race): every car goes.</summary>
        public void Suspend()
        {
            foreach (var c in Cars) if (c.Car != null) sim.Cars.Remove(c.Car);
            Removed += Cars.Count;
            Cars.Clear();
            Suspended = true;
        }

        public void Resume() => Suspended = false;

        /// <summary>Forgets every car (the simulation was reset and its car list cleared).</summary>
        public void Clear()
        {
            Removed += Cars.Count;
            Cars.Clear();
        }

        /// <summary>One simulation tick (before the vehicles step): controls for the physics cars, the ghosts, and the population.</summary>
        public void Step(double dt)
        {
            if (Suspended) return;
            tick++;
            var pv = sim.Player.vehicle;
            double px = pv.pos.x, pz = pv.pos.z;
            // Controls at 120 Hz, held between.
            if (tick % 2 == 0)
                foreach (var c in Cars)
                    if (c.Physics) Drive(c, 2 * dt, px, pz);
            // Ghosts at 60 Hz.
            if (tick % 4 == 1)
                foreach (var c in Cars)
                    if (!c.Physics) Glide(c, 4 * dt);
            // Levels of detail and the population at 4 Hz.
            if (tick % 60 == 3) Manage(px, pz);
        }

        /// <summary>
        /// Whether to wait for another car at the junction ahead: one already in it
        /// (within 9 m of its centre), or arriving within 4 s and nearer than this
        /// car; the player is never made to wait, so traffic always yields to them.
        /// </summary>
        bool MustYield(TrafficCar c, int node, double toNode)
        {
            var n = w.Nodes[node];
            foreach (var car in sim.Cars)
            {
                var v = car.vehicle;
                if (v == c.Vehicle) continue;
                double dx = v.pos.x - n.x, dz = v.pos.z - n.z;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d > 40) continue;
                // Following the same way through? Then the following gap handles it.
                double same = v.fwd.x * c.Vehicle.fwd.x + v.fwd.z * c.Vehicle.fwd.z;
                double closing = -(dx * v.vel.x + dz * v.vel.z) / Math.Max(d, 1);
                bool inside = d < 9 && same < 0.9;
                bool coming = closing > 1 && d / closing < 4;
                if (!inside && !coming) continue;
                if (car.player || inside || d < toNode) return true;
            }
            return false;
        }

        /// <summary>The speed to hold behind something gap metres ahead going vAhead (stopping 11 m short of anything standing still, room to go round it).</summary>
        static double FollowSpeed(double gap, double vAhead, double v)
        {
            double want = (vAhead < 0.5 ? 11 : 6) + 1.6 * v; // two-second-ish rule
            if (gap >= want + 30) return 1e9;
            return Math.Max(0, vAhead + (gap - want) * 0.5);
        }

        readonly double[] px = new double[24], pz = new double[24], ptx = new double[24], ptz = new double[24];

        /// <summary>
        /// Gap to the nearest car (or the player) on the path the car is taking:
        /// its lane (shifted sideways by shift, m) sampled every 4 m for 80 m, so
        /// a bend does not hide a stopped car. Also that car's speed along the
        /// path and its place across it (+ right of the path).
        /// </summary>
        double GapOnPath(TrafficCar c, double shift, out double vAhead, out double latAhead)
        {
            const int n = 21;
            for (int k = 0; k < n; k++) Ahead(c, k * 4, out px[k], out _, out pz[k], out ptx[k], out ptz[k], shift);
            double best = 1e9, va = 0, la = 0;
            var self = c.Vehicle;
            void Consider(Vehicle o, double ov, bool physics)
            {
                double ox = o.pos.x, oz = o.pos.z;
                double dx0 = ox - self.pos.x, dz0 = oz - self.pos.z;
                if (dx0 * dx0 + dz0 * dz0 > 90 * 90) return;
                for (int k = 0; k < n; k++)
                {
                    double dx = ox - px[k], dz = oz - pz[k];
                    double lon = dx * ptx[k] + dz * ptz[k];
                    if (lon < -2 || lon > 2) continue;
                    double lat = dx * -ptz[k] + dz * ptx[k];
                    if (Math.Abs(lat) > 2.6) continue;
                    double gap = k * 4 + lon - 4.6;
                    if (gap < -1.5) break; // beside or behind: not ahead
                    if (gap < best)
                    {
                        best = gap;
                        va = physics ? o.vel.x * ptx[k] + o.vel.z * ptz[k] : ov;
                        la = lat;
                    }
                    break;
                }
            }
            foreach (var o in Cars)
                if (o != c) Consider(o.Vehicle, o.Speed, o.Physics);
            foreach (var car in sim.Cars)
                if (car.player) Consider(car.vehicle, 0, true);
            vAhead = va;
            latAhead = la;
            return best;
        }

        /// <summary>
        /// Two lanes each way: pull out into the passing lane (lane 0, by the centre line) to pass
        /// something slower within 50 m, and move back to the right once the passing lane ahead is
        /// clear. The other lane must be clear from 10 m behind to 70 m ahead; 8 s between changes.
        /// </summary>
        void ChooseLane(TrafficCar c, double cruise, double gap, double vAhead, double dt)
        {
            var e = w.Edges[c.Edge];
            c.LaneHold -= dt;
            if (LanesEachWay(e.info) < 2 || c.LaneHold > 0 || c.SwerveT > 0) return;
            int other = -1;
            if (c.Lane > 0 && gap < 50 && vAhead < cruise - 3) other = c.Lane - 1;
            else if (c.Lane == 0 && (gap > 90 || vAhead > cruise + 1)) other = 1;
            if (other < 0) return;
            if (!LaneClear(c, LaneOffset(e.info, other) - LaneOffset(e.info, c.Lane))) return;
            c.Lane = other;
            c.LaneHold = 8;
            LaneChanges++;
        }

        /// <summary>Nothing within 9 m behind the car (room to back up).</summary>
        bool BehindClear(TrafficCar c)
        {
            var v = c.Vehicle;
            double fx = v.fwd.x, fz = v.fwd.z;
            foreach (var car in sim.Cars)
            {
                if (car.vehicle == v) continue;
                double dx = car.vehicle.pos.x - v.pos.x, dz = car.vehicle.pos.z - v.pos.z;
                double lon = dx * fx + dz * fz, lat = dx * -fz + dz * fx;
                if (lon < 0 && lon > -9.5 && Math.Abs(lat) < 2.4) return false;
            }
            return true;
        }

        /// <summary>
        /// Room to move into the lane offset metres to the side: nobody in it within a gap that grows
        /// with the closing speed (behind: 8 m plus 3 s of a faster car's gain; ahead: 12 m plus 3 s of ours).
        /// </summary>
        bool LaneClear(TrafficCar c, double offset)
        {
            var v = c.Vehicle;
            double fx = v.fwd.x, fz = v.fwd.z;
            double mine = c.Physics ? v.vLong : c.Speed;
            bool Blocks(Vehicle o, double speed)
            {
                double dx = o.pos.x - v.pos.x, dz = o.pos.z - v.pos.z;
                double lon = dx * fx + dz * fz, lat = dx * -fz + dz * fx;
                if (Math.Abs(lat - offset) > 2.4 || lon < -60 || lon > 90) return false;
                if (lon < 0) return -lon < 8 + Math.Max(0, speed - mine) * 3;
                return lon < 12 + Math.Max(0, mine - speed) * 3;
            }
            foreach (var car in sim.Cars)
            {
                if (car.vehicle == v) continue;
                var o = car.vehicle;
                if (Blocks(o, o.vel.x * fx + o.vel.z * fz)) return false;
            }
            foreach (var o in Cars)
            {
                if (o.Physics || o == c) continue;
                if (Blocks(o.Vehicle, o.Speed)) return false;
            }
            return true;
        }

        void Glide(TrafficCar c, double dt)
        {
            var v = c.Vehicle;
            LanePoint(c.Edge, c.Dir, c.Lane, c.S, out double x, out _, out double z, out double tx, out double tz);
            double gap0 = GapOnPath(c, 0, out double va, out _);
            // Far from the player a ghost may change lane on the spot (nobody sees the jump).
            var pv = sim.Player.vehicle;
            if ((pv.pos.x - x) * (pv.pos.x - x) + (pv.pos.z - z) * (pv.pos.z - z) > 400 * 400)
                ChooseLane(c, w.Edges[c.Edge].info.traffic * c.Cruise, gap0, va, dt);
            double target = Math.Min(RoadSpeed(c, c.Speed), FollowSpeed(gap0, va, c.Speed));
            c.Speed += Math.Max(-Decel * 2 * dt, Math.Min(1.5 * dt, target - c.Speed));
            c.Speed = Math.Max(0, c.Speed);
            c.S += c.Speed * dt;
            Advance(c);
            Pose(c);
        }

        void Pose(TrafficCar c)
        {
            LanePoint(c.Edge, c.Dir, c.Lane, c.S, out double x, out double y, out double z, out double tx, out double tz);
            c.Vehicle.Place(x, y, z, Simulation.YawOf(tx, tz));
        }

        /// <summary>Starts going round something gap metres ahead: shifted until well past it.</summary>
        static void Begin(TrafficCar c, double shift, double gap)
        {
            c.Swerve = shift;
            c.SwerveLeft = gap + 4.6 + 9;
            c.SwerveT = 25;
            c.Blocked = 0;
        }

        void Drive(TrafficCar c, double dt, double px, double pz)
        {
            void Begin(double shift, double gap) => Traffic.Begin(c, shift, gap);
            var v = c.Vehicle;
            var p = v.Params;
            var e = w.Edges[c.Edge];
            // Where it is along its lane: project onto the edge near its last place.
            double sOn = c.Dir > 0 ? c.S : e.length - c.S;
            int i0 = SampleAt(e, Math.Max(0, Math.Min(e.length, sOn)));
            double bestD = 1e18, bestS = sOn;
            for (int i = Math.Max(0, i0 - 3); i < Math.Min(e.n - 1, i0 + 4); i++)
            {
                double ax = e.x[i], az = e.z[i], bx = e.x[i + 1], bz = e.z[i + 1];
                double vx = bx - ax, vz = bz - az, l2 = vx * vx + vz * vz;
                double t = l2 > 1e-9 ? Math.Max(0, Math.Min(1, ((v.pos.x - ax) * vx + (v.pos.z - az) * vz) / l2)) : 0;
                double qx = ax + vx * t - v.pos.x, qz = az + vz * t - v.pos.z;
                double d = qx * qx + qz * qz;
                if (d < bestD)
                {
                    bestD = d;
                    bestS = e.s[i] + (e.s[i + 1] - e.s[i]) * t;
                }
            }
            c.S = c.Dir > 0 ? bestS : e.length - bestS;
            if (c.S > e.length - 0.3)
            {
                c.S += 0.3;
                Advance(c);
            }
            double speed = Math.Max(0, v.vLong);
            LanePoint(c.Edge, c.Dir, c.Lane, c.S, out double lx, out _, out double lz, out double ltx, out double ltz);
            double offLane = Math.Sqrt((v.pos.x - lx) * (v.pos.x - lx) + (v.pos.z - lz) * (v.pos.z - lz));
            double heading = v.fwd.x * ltx + v.fwd.z * ltz;

            // Steering: pure pursuit from the rear axle on the lane ahead (shifted while going round
            // something stopped), with a little yaw damping.
            if (c.SwerveT > 0)
            {
                c.SwerveT -= dt;
                c.SwerveLeft -= Math.Max(0, v.vLong) * dt;
                if (c.SwerveLeft <= 0) c.SwerveT = 0;
            }
            double shift = c.SwerveT > 0 ? c.Swerve : 0;
            double ld = Math.Max(7, Math.Min(40, 6 + 0.5 * speed));
            if (offLane > 2 && c.SwerveT <= 0) ld = Math.Max(ld, 2.5 * offLane);
            Ahead(c, ld, out double ax2, out _, out double az2, out _, out _, shift);
            double rx = v.pos.x - v.fwd.x * p.b, rz = v.pos.z - v.fwd.z * p.b;
            double dx = ax2 - rx, dz = az2 - rz;
            double right = dx * v.right.x + dz * v.right.z;
            double kappa = -2 * right / Math.Max(1, dx * dx + dz * dz);
            double delta = Math.Atan(kappa * p.wheelbase) + 0.15 * (kappa * speed - v.yawRate);
            var inp = v.input;
            inp.steer = Math.Max(-1, Math.Min(1, -delta / p.maxSteer));

            // Speed: the road's, behind whatever is ahead on the path it is taking.
            double latNow = (v.pos.x - lx) * -ltz + (v.pos.z - lz) * ltx; // + right of the lane centre
            double target = RoadSpeed(c, speed);
            double gap = GapOnPath(c, shift, out double va, out double la);
            ChooseLane(c, e.info.traffic * c.Cruise, gap, va, dt);
            target = Math.Min(target, FollowSpeed(gap, va, speed));
            if (heading < 0.5 || (offLane > 3 && c.SwerveT <= 0)) target = Math.Min(target, 4); // pointing the wrong way or off the road: creep back
            // Stopped behind something that is not moving: after a few seconds, go round it on
            // whichever side needs the smaller move and stays on the road, when that path is clear.
            bool stuckBehind = gap < 19 && Math.Abs(va) < 0.5 && speed < 1;
            c.Blocked = stuckBehind ? c.Blocked + dt : 0;
            if (c.Blocked > 3 && c.SwerveT <= 0)
            {
                double mh = p.spec.width / 2, myOff = LaneOffset(e.info, c.Lane);
                double obstacle = la; // relative to the lane centre (the path is the lane)
                double right2 = obstacle + 1.1 + mh + 0.9, left2 = obstacle - 1.1 - mh - 0.9;
                bool rightOk = myOff + right2 + mh <= e.info.halfWidth + 0.8 * e.info.shoulder;
                bool leftOk = myOff + left2 - mh >= -e.info.halfWidth;
                double first = rightOk && (!leftOk || right2 < -left2) ? right2 : left2;
                double second = first == right2 ? left2 : right2;
                bool secondOk = first == right2 ? leftOk : rightOk;
                // Only a move the car can make before it gets there: sideways at most 0.7 of the room ahead.
                double room = Math.Max(0, gap - 3) * 0.7;
                bool firstClear = (rightOk || leftOk) && Math.Abs(first - latNow) <= room && Clear(c, first - latNow);
                bool secondClear = !firstClear && secondOk && Math.Abs(second - latNow) <= room && Clear(c, second - latNow);
                if (!firstClear && !secondClear && gap < 9 && BehindClear(c))
                {
                    // Too close to steer round it: back up a couple of metres first.
                    c.BackUp = 2.5;
                    c.Blocked = 0;
                }
                Log?.Invoke($"car {c.Id} blocked at ({v.pos.x:0.0},{v.pos.z:0.0}) lane {c.Lane} off {myOff:0.0} latNow {latNow:0.0} gap {gap:0.0} la {la:0.0} -> right {right2:0.0} ok {rightOk} left {left2:0.0} ok {leftOk}; first {first:0.0} clear {firstClear}, second {second:0.0} clear {secondClear}");
                if (firstClear) Begin(first, gap);
                else if (secondClear) Begin(second, gap);
            }
            if (c.BackUp > 0)
            {
                // Reverse on the brake pedal (the automatic box selects reverse at a standstill), straight back.
                c.BackUp -= dt;
                inp.steer = 0;
                inp.throttle = 0;
                inp.handbrake = 0;
                inp.brake = c.BackUp > 0 && BehindClear(c) ? 0.35 : 0;
                if (c.BackUp <= 0) inp.handbrake = 1;
                return;
            }
            // Round it slowly: a crawl until it is half way across, then walking pace.
            if (c.SwerveT > 0) target = Math.Min(target, Math.Abs(latNow - shift) > Math.Abs(shift) / 2 ? 2.5 : 6);
            // Junctions: give way to anything in it or arriving sooner (the player always has priority).
            int endNode = c.Dir > 0 ? e.b : e.a;
            double toNode = e.length - c.S;
            double look = Math.Max(35, speed * speed / (2 * Decel) + 15);
            if (toNode < look && w.Nodes[endNode].edges.Count >= 3 && c.Yielding < 8 && MustYield(c, endNode, toNode))
            {
                // Stop well short of the junction, clear of cars turning across it.
                target = Math.Min(target, Math.Sqrt(2 * Decel * Math.Max(0, toNode - 11)));
                c.Yielding += dt;
            }
            else if (toNode > look) c.Yielding = 0;
            double err = target - speed;
            inp.throttle = err > 0 ? Math.Min(1, err * 0.35 + 0.15) : 0;
            inp.brake = err < -0.6 ? Math.Min(1, -err * 0.25) : 0;
            inp.handbrake = 0;
            // Hold still on the handbrake: the brake pedal held at a standstill selects reverse.
            if (target < 1 && speed < 1)
            {
                inp.throttle = 0;
                inp.brake = 0;
                inp.handbrake = 1;
            }

            // Trouble: off its lane or stuck with nothing in the way. Put it back once the player is not looking on.
            bool blocked = va < 1 && target < 0.5;
            bool trouble = offLane > 3 || heading < 0.3 || (speed < 0.5 && !blocked && target > 3);
            c.Trouble = trouble ? c.Trouble + dt : 0;
            double toPlayer = Math.Sqrt((v.pos.x - px) * (v.pos.x - px) + (v.pos.z - pz) * (v.pos.z - pz));
            if (c.Trouble > 5 && toPlayer > 60)
            {
                c.Trouble = 0;
                Pose(c);
                v.SetSpeed(Math.Min(8, RoadSpeed(c, 8)));
                Rescued++;
            }
        }

        /// <summary>How far a car is from the centre of its lane, m (tests and debugging).</summary>
        public double LaneError(TrafficCar c)
        {
            LanePoint(c.Edge, c.Dir, c.Lane, c.S, out double x, out _, out double z, out _, out _);
            var v = c.Vehicle;
            return Math.Sqrt((v.pos.x - x) * (v.pos.x - x) + (v.pos.z - z) * (v.pos.z - z));
        }

        // --- Levels of detail and the population ----------------------------------------------------

        void Manage(double px, double pz)
        {
            for (int i = Cars.Count - 1; i >= 0; i--)
            {
                var c = Cars[i];
                var v = c.Vehicle;
                double d = Math.Sqrt((v.pos.x - px) * (v.pos.x - px) + (v.pos.z - pz) * (v.pos.z - pz));
                if (d > Despawn)
                {
                    if (c.Car != null) sim.Cars.Remove(c.Car);
                    Cars.RemoveAt(i);
                    Removed++;
                    continue;
                }
                if (!c.Physics && d < Near && Clear(v.pos.x, v.pos.z, c))
                {
                    // Into the physics: placed on its lane at its speed.
                    Pose(c);
                    v.SetSpeed(c.Speed);
                    v.input.throttle = 0;
                    v.input.brake = 0;
                    c.Car = sim.AddVehicle(v, c.SpecId, "Traffic");
                    c.Trouble = 0;
                    Promoted++;
                }
                else if (c.Physics && d > Far)
                {
                    // Back to a ghost at its current speed (on its lane: Drive keeps S).
                    sim.Cars.Remove(c.Car);
                    c.Car = null;
                    c.Speed = Math.Max(0, v.vLong);
                    Pose(c);
                    Demoted++;
                }
            }
            // Top up out of sight.
            int near = 0;
            foreach (var c in Cars)
            {
                var v = c.Vehicle;
                if ((v.pos.x - px) * (v.pos.x - px) + (v.pos.z - pz) * (v.pos.z - pz) < SpawnMax * SpawnMax) near++;
            }
            for (int k = 0; k < 3 && near < Target; k++)
                if (Spawn(px, pz)) near++;
        }

        /// <summary>No other car within 9 m: room to appear or to become a physics car.</summary>
        bool Clear(double x, double z, TrafficCar self)
        {
            foreach (var car in sim.Cars)
            {
                var v = car.vehicle;
                if (self != null && v == self.Vehicle) continue;
                if ((v.pos.x - x) * (v.pos.x - x) + (v.pos.z - z) * (v.pos.z - z) < 81) return false;
            }
            foreach (var o in Cars)
            {
                if (o == self) continue;
                var v = o.Vehicle;
                if ((v.pos.x - x) * (v.pos.x - x) + (v.pos.z - z) * (v.pos.z - z) < 81) return false;
            }
            return true;
        }

        /// <summary>A new ghost somewhere on a road between SpawnMin and SpawnMax from the player.</summary>
        bool Spawn(double px, double pz)
        {
            if (eligible.Count == 0) return false;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                int edge = eligible[rng.Int(0, eligible.Count)];
                var e = w.Edges[edge];
                double s = rng.Range(0, e.length);
                int i = SampleAt(e, s);
                double d = Math.Sqrt((e.x[i] - px) * (e.x[i] - px) + (e.z[i] - pz) * (e.z[i] - pz));
                if (d < SpawnMin || d > SpawnMax) continue;
                int dir = rng.Chance(0.5) ? 1 : -1;
                int lane = rng.Int(0, LanesEachWay(e.info));
                var c = new TrafficCar
                {
                    Id = nextId++,
                    SpecId = PickCar(),
                    Edge = edge,
                    Dir = dir,
                    Lane = lane,
                    S = dir > 0 ? s : e.length - s,
                    Cruise = rng.Range(0.85, 1.1),
                    Look = rng.Next(),
                };
                LanePoint(edge, dir, lane, c.S, out double x, out _, out double z, out _, out _);
                if (!Clear(x, z, null)) continue;
                c.Vehicle = new Vehicle(ParamsOf(c.SpecId)) { aids = Aids.Default() };
                // The controller steers the wheels directly (like the racing AI): the player's steering assist would blunt it.
                c.Vehicle.aids.steering = SteeringAssist.Off;
                c.Speed = e.info.traffic * c.Cruise * 0.8;
                ChooseNext(c);
                Pose(c);
                Cars.Add(c);
                Spawned++;
                return true;
            }
            return false;
        }

        string PickCar()
        {
            double sum = 0;
            foreach (var m in Mix) sum += m.weight;
            double r = rng.Next() * sum;
            foreach (var m in Mix)
            {
                r -= m.weight;
                if (r < 0) return m.id;
            }
            return Mix[0].id;
        }

        VehicleParams ParamsOf(string id)
        {
            if (!paramsCache.TryGetValue(id, out var p)) paramsCache[id] = p = VehicleParams.Build(Halcyon.Core.Cars.ById(id));
            return p;
        }
    }
}
