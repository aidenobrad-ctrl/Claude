// Stunts on the open road, as at a festival:
//   - speed traps: the speed through a camera at the end of a long straight;
//   - speed zones: the average speed along a stretch of fast sweepers;
//   - drift zones: a drift score along the twistiest stretches;
//   - danger signs: the length of a jump off a sharp crest.
// They are placed from the island's roads the same way every time, and each
// is scored to one, two or three stars against targets worked out from its
// road (the speed a straight allows, the jump a crest gives). The tracker
// follows the player's car every step and reports results; the best score
// of each stunt is kept in the profile. No UnityEngine.
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    public enum StuntKind { SpeedTrap, SpeedZone, DriftZone, DangerSign }

    public sealed class Stunt
    {
        public string Id, Name;
        public StuntKind Kind;
        public int Edge;
        /// <summary>Along the edge: the line of a trap or the crest of a jump (S0 = S1), or a zone from S0 to S1.</summary>
        public double S0, S1;
        /// <summary>A danger sign is jumped along the edge's direction (+1) or against it (-1); the others go either way (0).</summary>
        public int Dir;
        /// <summary>Where its board stands, and the way it faces: the start of a zone, a trap's line, a jump's run-up.</summary>
        public double X, Y, Z, Yaw;
        /// <summary>The scores for one, two and three stars.</summary>
        public readonly double[] Stars = new double[3];
        /// <summary>The speed the targets were worked out for, m/s: the most a trap's straight or a zone allows, a jump's run-up speed.</summary>
        public double RefSpeed;

        public string Unit => Kind == StuntKind.DriftZone ? "points" : Kind == StuntKind.DangerSign ? "m" : "km/h";
        public string KindName => Kind switch
        {
            StuntKind.SpeedTrap => "Speed Trap",
            StuntKind.SpeedZone => "Speed Zone",
            StuntKind.DriftZone => "Drift Zone",
            _ => "Danger Sign",
        };
        public int StarsFor(double score) => score >= Stars[2] ? 3 : score >= Stars[1] ? 2 : score >= Stars[0] ? 1 : 0;
        public string Format(double score) => Kind == StuntKind.DriftZone ? $"{score:N0} points" : Kind == StuntKind.DangerSign ? $"{score:0.0} m" : $"{score:0} km/h";
    }

    /// <summary>A stunt done: the score, its stars, and what it paid.</summary>
    public sealed class StuntResult
    {
        public Stunt Stunt;
        public double Score;
        public int Stars;
        public bool Best;
        public int Xp, Credits;
        public override string ToString() =>
            $"{Stunt.Name}: {Stunt.Format(Score)} {new string('*', Stars)}{(Best ? " (best)" : "")}{(Xp > 0 ? $" +{Xp} XP" : "")}{(Credits > 0 ? $" +{Credits:N0} cr" : "")}";
    }

    public static class Stunts
    {
        const double G = 9.81;
        /// <summary>Stunts stand at least this far apart, m.</summary>
        const double Apart = 450;

        static readonly Dictionary<WorldData, List<Stunt>> cache = new Dictionary<WorldData, List<Stunt>>();

        /// <summary>The island's stunts (cached per island). The jumps' targets are measured by driving over them, on g.</summary>
        public static List<Stunt> Find(WorldData w, IslandGround g = null)
        {
            lock (cache)
            {
                if (cache.TryGetValue(w, out var list)) return list;
                list = Place(w, g ?? new IslandGround(w));
                cache[w] = list;
                return list;
            }
        }

        sealed class Candidate
        {
            public StuntKind Kind;
            public int Edge, Dir;
            public double S0, S1, Score, RefSpeed;
            public double[] Stars;
        }

        /// <summary>Horizontal curvature (1/m) at each sample, from the tangents three samples either side.</summary>
        internal static double[] Curvature(RoadEdge e)
        {
            var k = new double[e.n];
            for (int i = 0; i < e.n; i++)
            {
                int a = Math.Max(0, i - 3), b = Math.Min(e.n - 1, i + 3);
                double da = Math.Atan2(e.tz[b], e.tx[b]) - Math.Atan2(e.tz[a], e.tx[a]);
                while (da > Math.PI) da -= 2 * Math.PI;
                while (da < -Math.PI) da += 2 * Math.PI;
                double ds = e.s[b] - e.s[a];
                k[i] = ds > 0 ? Math.Abs(da) / ds : 0;
            }
            return k;
        }

        /// <summary>Vertical curvature (1/m, + = a crest) at each sample, from the heights over about 12 m either side.</summary>
        static double[] Crest(RoadEdge e)
        {
            var c = new double[e.n];
            for (int i = 3; i < e.n - 3; i++)
            {
                double s0 = e.s[i] - e.s[i - 3], s1 = e.s[i + 3] - e.s[i];
                c[i] = -2 * ((e.y[i + 3] - e.y[i]) / s1 - (e.y[i] - e.y[i - 3]) / s0) / (s0 + s1);
            }
            return c;
        }

        static int IndexAt(RoadEdge e, double s)
        {
            int i = 0;
            while (i < e.n - 1 && e.s[i + 1] <= s) i++;
            return i;
        }

        static double Round(double v, double step) => Math.Round(v / step) * step;

        /// <summary>The speed a car can carry through a bend of curvature k, m/s (capped at vmax).</summary>
        static double BendSpeed(double k, double mu, double vmax) => k > 1e-6 ? Math.Min(vmax, Math.Sqrt(mu * G / k)) : vmax;

        /// <summary>Every candidate spot before choosing (kind, edge, from, to, direction, score, stars), for tests and tuning.</summary>
        public static List<(StuntKind kind, int edge, double s0, double s1, int dir, double score, double[] stars)> Candidates(WorldData w, IslandGround g = null)
        {
            var list = new List<(StuntKind, int, double, double, int, double, double[])>();
            foreach (var c in Gather(w, g ?? new IslandGround(w))) list.Add((c.Kind, c.Edge, c.S0, c.S1, c.Dir, c.Score, c.Stars));
            return list;
        }

        static List<Candidate> Gather(WorldData w, IslandGround g)
        {
            var found = new List<Candidate>();
            foreach (var e in w.Edges)
            {
                // The city's grid streets are too short and too square for any of them.
                if (e.info.id == "street" || e.n < 20) continue;
                var k = Curvature(e);
                var c = Crest(e);
                Traps(e, k, found);
                Zones(e, k, found);
                Drifts(e, k, found);
                Signs(e, k, c, g, found);
            }
            return found;
        }

        static List<Stunt> Place(WorldData w, IslandGround g)
        {
            var found = Gather(w, g);
            // Best first within each kind, then far enough from every stunt already placed.
            var picked = new List<(Candidate c, double x, double z)>();
            var quota = new Dictionary<StuntKind, int> { [StuntKind.SpeedTrap] = 4, [StuntKind.SpeedZone] = 3, [StuntKind.DriftZone] = 4, [StuntKind.DangerSign] = 4 };
            // Rarer kinds choose first, so a common one does not take their only good spot.
            foreach (var kind in new[] { StuntKind.DangerSign, StuntKind.DriftZone, StuntKind.SpeedZone, StuntKind.SpeedTrap })
            {
                var pool = found.FindAll(f => f.Kind == kind);
                pool.Sort((a, b) => b.Score.CompareTo(a.Score) != 0 ? b.Score.CompareTo(a.Score) : (a.Edge * 100000 + a.S0).CompareTo(b.Edge * 100000 + b.S0));
                int n = 0;
                foreach (var cand in pool)
                {
                    if (n >= quota[kind]) break;
                    var e = w.Edges[cand.Edge];
                    int i = IndexAt(e, (cand.S0 + cand.S1) / 2);
                    double x = e.x[i], z = e.z[i];
                    bool clear = true;
                    foreach (var p in picked)
                    {
                        var pe = w.Edges[p.c.Edge];
                        // Apart from every other stunt, measured between their nearest ends.
                        for (int q = 0; q < 2 && clear; q++)
                        {
                            int a = IndexAt(e, q == 0 ? cand.S0 : cand.S1);
                            for (int r = 0; r < 2 && clear; r++)
                            {
                                int b = IndexAt(pe, r == 0 ? p.c.S0 : p.c.S1);
                                double dx = e.x[a] - pe.x[b], dz = e.z[a] - pe.z[b];
                                if (dx * dx + dz * dz < Apart * Apart) clear = false;
                            }
                        }
                    }
                    if (!clear) continue;
                    picked.Add((cand, x, z));
                    n++;
                }
            }
            // Names from the nearest place, in a fixed order so ids never change.
            picked.Sort((a, b) => a.c.Kind != b.c.Kind ? a.c.Kind.CompareTo(b.c.Kind) : (a.c.Edge * 100000 + a.c.S0).CompareTo(b.c.Edge * 100000 + b.c.S0));
            var list = new List<Stunt>();
            var names = new HashSet<string>();
            foreach (var (cand, x, z) in picked)
            {
                var e = w.Edges[cand.Edge];
                var st = new Stunt { Kind = cand.Kind, Edge = cand.Edge, S0 = cand.S0, S1 = cand.S1, Dir = cand.Dir, RefSpeed = cand.RefSpeed };
                Array.Copy(cand.Stars, st.Stars, 3);
                // The board: a trap's line, a zone's first end, a jump's run-up 60 m before the crest.
                double sb = cand.Kind == StuntKind.DangerSign ? cand.S0 - cand.Dir * 60 : cand.S0;
                int bi = IndexAt(e, Math.Max(0, Math.Min(e.length, sb)));
                st.X = e.x[bi];
                st.Y = e.y[bi];
                st.Z = e.z[bi];
                st.Yaw = Simulation.YawOf(e.tx[bi] * (cand.Dir < 0 ? -1 : 1), e.tz[bi] * (cand.Dir < 0 ? -1 : 1));
                string place = NearestPlace(w, x, z);
                string name = $"{place} {st.KindName}";
                for (int extra = 2; names.Contains(name); extra++) name = $"{place} {st.KindName} {extra}";
                names.Add(name);
                st.Name = name;
                st.Id = $"{cand.Kind.ToString().ToLowerInvariant()}:{cand.Edge}:{cand.S0:0}";
                list.Add(st);
            }
            return list;
        }

        static string NearestPlace(WorldData w, double x, double z)
        {
            string best = "Halcyon";
            double bd = double.PositiveInfinity;
            foreach (var p in w.Pois)
            {
                double d = (p.x - x) * (p.x - x) + (p.z - z) * (p.z - z);
                if (d < bd)
                {
                    bd = d;
                    best = p.name;
                }
            }
            return best;
        }

        /// <summary>Speed traps: 80% of the way along a straight (radius over 600 m) of 350 m or more, either way.</summary>
        static void Traps(RoadEdge e, double[] k, List<Candidate> found)
        {
            int start = -1;
            for (int i = 0; i <= e.n; i++)
            {
                bool straight = i < e.n && k[i] < 1.0 / 600 && e.bridge[i] == 0;
                if (straight && start < 0) start = i;
                if (straight || start < 0) continue;
                int end = i - 1;
                double len = e.s[end] - e.s[start];
                if (len >= 350)
                {
                    // The line half way along, so it is the same run-up either way; the targets come from the slower way
                    // in (the tighter bend at either end; an edge's end counts as a junction taken at 90 km/h).
                    double line = e.s[start] + 0.5 * len;
                    double vIn = Math.Min(BendSpeed(start > 0 ? k[Math.Max(0, start - 4)] : 0, 1.0, 25), BendSpeed(end < e.n - 1 ? k[Math.Min(e.n - 1, end + 4)] : 0, 1.0, 25));
                    double v = Math.Min(300 / 3.6, Math.Sqrt(vIn * vIn + 2 * 4.0 * 0.5 * len)) * 3.6;
                    found.Add(new Candidate { Kind = StuntKind.SpeedTrap, Edge = e.id, S0 = line, S1 = line, Score = len, RefSpeed = v / 3.6, Stars = new[] { Round(v * 0.7, 5), Round(v * 0.85, 5), Round(v, 5) } });
                }
                start = -1;
            }
        }

        /// <summary>Zones start and end at least this far from their edge's ends, so a car always drives in through one end.</summary>
        const double EndRoom = 80;

        /// <summary>Speed zones: 500-800 m of fast sweepers (no bend under 100 m) that turn 15 degrees or more.</summary>
        static void Zones(RoadEdge e, double[] k, List<Candidate> found)
        {
            for (int i = 0; i < e.n; i += 10)
            {
                if (e.s[i] < EndRoom) continue;
                double turn = 0;
                bool ok = true;
                int j = i;
                for (; j + 1 < e.n && e.s[j] - e.s[i] < 800; j++)
                {
                    if (k[j] > 1.0 / 100 || e.bridge[j] != 0)
                    {
                        ok = false;
                        break;
                    }
                    turn += k[j] * (e.s[j + 1] - e.s[j]);
                }
                double len = e.s[j] - e.s[i];
                if (!ok || len < 500 || turn < 15 * Math.PI / 180 || e.s[j] > e.length - EndRoom) continue;
                double v = ZoneSpeed(e, k, i, j) * 3.6;
                found.Add(new Candidate { Kind = StuntKind.SpeedZone, Edge = e.id, S0 = e.s[i], S1 = e.s[j], Score = turn, RefSpeed = v / 3.6, Stars = new[] { Round(v * 0.65, 5), Round(v * 0.8, 5), Round(v * 0.95, 5) } });
            }
        }

        /// <summary>
        /// The average speed over samples i..j of a quick car driven well: through each bend at 1.05 g, between them
        /// accelerating at up to 4.5 m/s² (less towards 250 km/h) and braking at 8, arriving as fast as the road allows.
        /// </summary>
        static double ZoneSpeed(RoadEdge e, double[] k, int i, int j)
        {
            const double vMax = 250 / 3.6, brake = 8;
            int n = j - i + 1;
            var v = new double[n];
            for (int q = 0; q < n; q++) v[q] = BendSpeed(k[i + q], 1.05, vMax);
            for (int q = 1; q < n; q++)
            {
                double ds = e.s[i + q] - e.s[i + q - 1];
                double a = 4.5 * Math.Max(0.05, 1 - (v[q - 1] / vMax) * (v[q - 1] / vMax));
                v[q] = Math.Min(v[q], Math.Sqrt(v[q - 1] * v[q - 1] + 2 * a * ds));
            }
            for (int q = n - 2; q >= 0; q--)
            {
                double ds = e.s[i + q + 1] - e.s[i + q];
                v[q] = Math.Min(v[q], Math.Sqrt(v[q + 1] * v[q + 1] + 2 * brake * ds));
            }
            double time = 0;
            for (int q = 0; q + 1 < n; q++) time += (e.s[i + q + 1] - e.s[i + q]) / Math.Max(1, (v[q] + v[q + 1]) / 2);
            return (e.s[j] - e.s[i]) / Math.Max(1e-6, time);
        }

        /// <summary>
        /// Drift zones: 350-450 m of a main road or highway that turn 120 degrees or more in bends of 30-150 m, and none
        /// tighter. Not the mountain pass: its hairpins, rails and drops are no place to slide.
        /// </summary>
        static void Drifts(RoadEdge e, double[] k, List<Candidate> found)
        {
            if (e.info.id != "main" && e.info.id != "highway") return;
            for (int i = 0; i < e.n; i += 8)
            {
                if (e.s[i] < EndRoom) continue;
                double turn = 0;
                bool tight = false;
                int j = i;
                for (; j + 1 < e.n && e.s[j] - e.s[i] < 450; j++)
                {
                    if (k[j] > 1.0 / 30 || e.bridge[j] != 0) tight = true;
                    if (k[j] > 1.0 / 150) turn += k[j] * (e.s[j + 1] - e.s[j]);
                }
                double len = e.s[j] - e.s[i];
                if (tight || len < 350 || turn < 120 * Math.PI / 180 || e.s[j] > e.length - EndRoom) continue;
                // Targets per metre of zone: a drift bot holding about 20 degrees scores around 2-4 points a metre.
                found.Add(new Candidate { Kind = StuntKind.DriftZone, Edge = e.id, S0 = e.s[i], S1 = e.s[j], Score = turn, Stars = new[] { Round(len * 1.5, 100), Round(len * 4, 100), Round(len * 8, 100) } });
            }
        }

        /// <summary>
        /// Danger signs: a crest sharper than 320 m with a run-up of 120 m and 50 m on with no bend under 250 m. The targets are
        /// the jumps a reference car makes off it at 80%, 90% and 100% of the speed the run-up allows, measured by driving
        /// it (a point mass is no guide: the springs, loaded on the way up, throw a car further, and a falling road
        /// carries it on).
        /// </summary>
        static void Signs(RoadEdge e, double[] k, double[] c, IslandGround g, List<Candidate> found)
        {
            for (int i = 3; i < e.n - 3; i++)
            {
                if (c[i] < 1.0 / 320 || c[i] < c[i - 1] || c[i] < c[i + 1] || e.bridge[i] != 0) continue;
                for (int dir = 1; dir >= -1; dir -= 2)
                {
                    if (!Straight(e, k, e.s[i], -dir * 120) || !Straight(e, k, e.s[i], dir * 50)) continue;
                    // The speed the run-up allows: in from the bend before it, 4 m/s² for 150 m, 250 km/h at most.
                    int before = IndexAt(e, Math.Max(0, Math.Min(e.length, e.s[i] - dir * 170)));
                    double vIn = BendSpeed(k[before], 1.0, 30);
                    double vRef = Math.Min(250 / 3.6, Math.Sqrt(vIn * vIn + 2 * 4.0 * 150));
                    // Cheap first: a point mass that barely leaves the road will not make a jump worth a sign.
                    if (Jump(e, i, dir, vRef) < 5) continue;
                    double d3 = DriveJump(e, i, dir, vRef, g), d2 = DriveJump(e, i, dir, vRef * 0.9, g), d1 = DriveJump(e, i, dir, vRef * 0.8, g);
                    if (d3 < 10 || d1 < 4 || !(d1 < d2 && d2 < d3)) continue;
                    // 3% short of the measured jumps, rounded down to half metres: a jump at the speed a target was
                    // measured at earns it, even off a line a little different from the reference car's.
                    found.Add(new Candidate { Kind = StuntKind.DangerSign, Edge = e.id, S0 = e.s[i], S1 = e.s[i], Dir = dir, Score = c[i], RefSpeed = vRef, Stars = new[] { Math.Floor(d1 * 0.97 * 2) / 2, Math.Floor(d2 * 0.97 * 2) / 2, Math.Floor(d3 * 0.97 * 2) / 2 } });
                }
            }
        }

        /// <summary>No bend tighter than 250 m (a jump's run-up may sweep a little) and no bridge over a span from s.</summary>
        /// <summary>
        /// The jump a reference car (a stock Halden Aster GT with a player's aids) makes off the crest at sample i, going dir
        /// along the edge and held at speed v from 150 m before it: from the moment all four wheels leave the road to the
        /// moment two are back on it, metres over the ground. 0 if it never really leaves the road or lands on its roof.
        /// </summary>
        public static double DriveJump(RoadEdge e, int i, int dir, double v, IslandGround g)
        {
            var car = new Vehicle(VehicleParams.Build(Cars.ById("halden-aster-gt"))) { aids = Aids.Default() };
            int j = IndexAt(e, Math.Max(0, Math.Min(e.length, e.s[i] - dir * 150)));
            car.Place(e.x[j], g.GroundHeight(e.x[j], e.z[j]), e.z[j], Simulation.YawOf(e.tx[j] * dir, e.tz[j] * dir));
            car.SetSpeed(v);
            bool flying = false;
            double flown = 0, takeX = 0, takeZ = 0, early = 0;
            for (int step = 0; step < 8 * 240; step++)
            {
                // Pure pursuit on the centre line, holding the speed.
                double best = double.PositiveInfinity;
                int near = j;
                for (int q = Math.Max(0, j - 6); q <= Math.Min(e.n - 1, j + 12); q++)
                {
                    double d = (e.x[q] - car.pos.x) * (e.x[q] - car.pos.x) + (e.z[q] - car.pos.z) * (e.z[q] - car.pos.z);
                    if (d < best)
                    {
                        best = d;
                        near = q;
                    }
                }
                j = near;
                double sAhead = e.s[near] + dir * (12 + car.speed * 0.5);
                if (sAhead < 0 || sAhead > e.length) return 0;
                int t = IndexAt(e, sAhead);
                double dx = e.x[t] - car.pos.x, dz = e.z[t] - car.pos.z;
                double side = dx * car.right.x + dz * car.right.z, along = dx * car.fwd.x + dz * car.fwd.z;
                double err = v - car.vLong;
                car.input.steer = Math.Max(-1, Math.Min(1, Math.Atan2(side, along) * 1.6));
                car.input.throttle = Math.Max(0, Math.Min(1, err * 0.5));
                car.input.brake = Math.Max(0, Math.Min(1, -err * 0.2));
                car.input.handbrake = 0;
                car.Step(Loop.SIM_DT, g);
                if (!flying)
                {
                    // Only a takeoff from this crest counts (from 40 m before it), not a hop off a bump on the run-up.
                    double toCrest = (e.s[near] - e.s[i]) * dir;
                    if (car.groundedCount == 0 && toCrest > -40)
                    {
                        flying = true;
                        flown = 0;
                        takeX = car.pos.x;
                        takeZ = car.pos.z;
                    }
                    else if (car.groundedCount == 0)
                    {
                        // A real flight before the run-up's last 40 m (not a hop off a bump): a crest behind
                        // a crest, whose jump would depend on how the car came down from the first. Not a sign.
                        early += Loop.SIM_DT;
                        if (early > 0.3) return 0;
                    }
                    else
                    {
                        early = 0;
                        if (toCrest > 60) return 0;
                    }
                    continue;
                }
                if (car.groundedCount == 0) flown += Loop.SIM_DT;
                if (car.groundedCount >= 2 && car.airTime == 0)
                {
                    double d = Math.Sqrt((car.pos.x - takeX) * (car.pos.x - takeX) + (car.pos.z - takeZ) * (car.pos.z - takeZ));
                    if (flown < 0.25 || d < 2)
                    {
                        flying = false;
                        continue;
                    }
                    return car.up.y > 0.7 ? d : 0;
                }
            }
            return 0;
        }

        /// <summary>Why the crest nearest s on an edge is, or is not, a danger sign: each test of the finder (for tuning).</summary>
        public static string ExplainSign(WorldData w, int edge, double s, IslandGround g = null)
        {
            g = g ?? new IslandGround(w);
            var e = w.Edges[edge];
            var k = Curvature(e);
            var c = Crest(e);
            int i0 = IndexAt(e, s), i = i0;
            for (int q = Math.Max(3, i0 - 6); q <= Math.Min(e.n - 4, i0 + 6); q++) if (c[q] > c[i]) i = q;
            var sb = new System.Text.StringBuilder($"edge {edge} crest at {e.s[i]:0}: curvature {c[i]:0.0000} (a sign needs {1.0 / 320:0.0000})");
            for (int dir = 1; dir >= -1; dir -= 2)
            {
                bool runUp = Straight(e, k, e.s[i], -dir * 120), runOut = Straight(e, k, e.s[i], dir * 50);
                int bi = IndexAt(e, Math.Max(0, Math.Min(e.length, e.s[i] - dir * 170)));
                double vIn = BendSpeed(k[bi], 1.0, 30);
                double vRef = Math.Min(250 / 3.6, Math.Sqrt(vIn * vIn + 2 * 4.0 * 150));
                double d3 = DriveJump(e, i, dir, vRef, g), d2 = DriveJump(e, i, dir, vRef * 0.9, g), d1 = DriveJump(e, i, dir, vRef * 0.8, g);
                sb.Append($"\n    going {(dir > 0 ? "along" : "against")}: run-up straight {runUp}, run-out straight {runOut}, {vRef * 3.6:0} km/h, point mass {Jump(e, i, dir, vRef):0.0} m, driven {d1:0.0} / {d2:0.0} / {d3:0.0} m");
            }
            return sb.ToString();
        }

        static bool Straight(RoadEdge e, double[] k, double s, double span)
        {
            double a = Math.Min(s, s + span), b = Math.Max(s, s + span);
            if (a < 0 || b > e.length) return false;
            for (int i = IndexAt(e, a); i < e.n && e.s[i] <= b; i++)
                if (k[i] > 1.0 / 250 || e.bridge[i] != 0) return false;
            return true;
        }

        /// <summary>
        /// How far a point mass flies off the crest at sample i, going dir along the edge at speed v: launched along the road's
        /// slope there, landing where it falls back to the road's height. 0 if it does not leave the road.
        /// </summary>
        public static double Jump(RoadEdge e, int i, int dir, double v)
        {
            // Where it leaves the road: the first point on the way up to the crest where the road curves down faster
            // than gravity can pull the car round (v² c > g cos θ), else the crest itself.
            var c = Crest(e);
            int take = i;
            for (int back = 12; back >= 0; back--)
            {
                int j = i - dir * back;
                if (j < 3 || j > e.n - 4) continue;
                if (v * v * c[j] > G * 0.98)
                {
                    take = j;
                    break;
                }
            }
            int a = Math.Max(0, Math.Min(e.n - 1, take - dir * 2)), b = Math.Max(0, Math.Min(e.n - 1, take + dir * 2));
            double run = Math.Abs(e.s[b] - e.s[a]);
            // a is behind the takeoff and b ahead of it, in the direction of travel.
            double slope = run > 0 ? (e.y[b] - e.y[a]) / run : 0;
            double cosT = 1 / Math.Sqrt(1 + slope * slope);
            double vh = v * cosT, vy = v * slope * cosT;
            double y0 = e.y[take], s0 = e.s[take];
            double dt = 0.005;
            for (double t = dt; t < 6; t += dt)
            {
                double along = vh * t;
                double s = s0 + dir * along;
                if (s < 0 || s > e.length) return along;
                int j = IndexAt(e, s);
                int jn = Math.Min(e.n - 1, j + 1);
                double f = e.s[jn] > e.s[j] ? (s - e.s[j]) / (e.s[jn] - e.s[j]) : 0;
                double road = e.y[j] + (e.y[jn] - e.y[j]) * f;
                double y = y0 + vy * t - 0.5 * G * t * t;
                if (y <= road) return along < 1 ? 0 : along;
            }
            return 0;
        }
    }

    /// <summary>
    /// Follows the player's car through the stunts every simulation step and reports what it did. Drift points build while
    /// the car slides between 10 and 90 degrees above 30 km/h, faster and at a bigger angle for more, with a chain bonus up
    /// to three times for a long unbroken drift; a hit loses the chain.
    /// </summary>
    public sealed class StuntTracker
    {
        public readonly WorldData W;
        public readonly List<Stunt> All;
        readonly Profile p;
        readonly Dictionary<Stunt, double> bestBefore = new Dictionary<Stunt, double>();

        // The stunt under way: a zone (entered at one end), or a jump (armed on the run-up, then in the air).
        Stunt active;
        int enteredAt; // the zone's end the car came in at: 0 = S0, 1 = S1
        double zoneTime, banked, chain, chainTime, sinceSlide;
        // Where the car was along each stunt's road last step (NaN when it was not near it).
        readonly double[] lastS;
        bool airborne;
        double armedFor, takeX, takeZ, flown;

        /// <summary>The stunt under way (a zone being driven, or a jump), null when none.</summary>
        public Stunt Active => active;
        /// <summary>The score so far in the stunt under way (a zone's average speed, drift points, a jump's length in the air).</summary>
        public double Live { get; private set; }
        /// <summary>The drift chain's bonus, 1 to 3.</summary>
        public double Chain => 1 + Math.Min(2, chainTime / 4);

        public StuntTracker(WorldData w, Profile profile, IslandGround g = null)
        {
            W = w;
            All = Stunts.Find(w, g);
            p = profile;
            lastS = new double[All.Count];
            for (int i = 0; i < lastS.Length; i++) lastS[i] = double.NaN;
        }

        public double Best(Stunt s) => p.StuntBest.TryGetValue(s.Id, out double b) ? b : 0;

        /// <summary>Forget where the car was (after it is moved: fast travel, a reset, a race), so nothing counts the jump.</summary>
        public void Reset()
        {
            active = null;
            Live = 0;
            for (int i = 0; i < lastS.Length; i++) lastS[i] = double.NaN;
        }

        /// <summary>One simulation step of the player's car; returns a result when a stunt is done, else null.</summary>
        public StuntResult Step(Vehicle v, double dt)
        {
            double x = v.pos.x, z = v.pos.z;
            if (active != null) return Continue(v, x, z, dt);
            // Each stunt within reach: crossing a trap's line, driving into a zone at either end, a jump's run-up.
            for (int n = 0; n < All.Count; n++)
            {
                var st = All[n];
                double ps = lastS[n];
                if (!Near(st, x, z, out double s, out double lat))
                {
                    lastS[n] = double.NaN;
                    continue;
                }
                var e = W.Edges[st.Edge];
                bool onRoad = Math.Abs(lat) <= e.info.halfWidth + 4;
                lastS[n] = onRoad ? s : double.NaN;
                if (!onRoad || double.IsNaN(ps)) continue;
                switch (st.Kind)
                {
                    case StuntKind.SpeedTrap:
                        if ((ps - st.S0) * (s - st.S0) <= 0 && ps != s) return Finish(st, v.speed * 3.6);
                        break;
                    case StuntKind.SpeedZone:
                    case StuntKind.DriftZone:
                        if (ps < st.S0 && s >= st.S0) Begin(st, 0);
                        else if (ps > st.S1 && s <= st.S1) Begin(st, 1);
                        break;
                    case StuntKind.DangerSign:
                        // Armed on the last 80 m of the run-up, heading for the crest.
                        double toCrest = (st.S0 - s) * st.Dir;
                        if (toCrest > 0 && toCrest < 80 && (s - ps) * st.Dir > 0) Begin(st, 0);
                        break;
                }
                if (active != null) return null;
            }
            return null;
        }

        bool Near(Stunt st, double x, double z, out double s, out double lat)
        {
            s = 0;
            lat = double.PositiveInfinity;
            var e = W.Edges[st.Edge];
            double lo = Math.Min(st.S0, st.S1) - 120, hi = Math.Max(st.S0, st.S1) + 120;
            // A quick bound first: the stunt's middle within its half length plus 200 m.
            int mid = IndexOf(e, (st.S0 + st.S1) / 2);
            double r = (hi - lo) / 2 + 200;
            double dx = e.x[mid] - x, dz = e.z[mid] - z;
            if (dx * dx + dz * dz > r * r) return false;
            double best = double.PositiveInfinity;
            for (int i = IndexOf(e, Math.Max(0, lo)); i + 1 < e.n && e.s[i] <= hi; i++)
            {
                double ax = e.x[i], az = e.z[i], bx = e.x[i + 1] - ax, bz = e.z[i + 1] - az;
                double l2 = bx * bx + bz * bz;
                double t = l2 > 0 ? Math.Max(0, Math.Min(1, ((x - ax) * bx + (z - az) * bz) / l2)) : 0;
                double cx = ax + bx * t - x, cz = az + bz * t - z, d2 = cx * cx + cz * cz;
                if (d2 >= best) continue;
                best = d2;
                s = e.s[i] + (e.s[i + 1] - e.s[i]) * t;
                double l = Math.Sqrt(l2);
                lat = l > 0 ? ((x - ax) * (-bz / l) + (z - az) * (bx / l)) : 0;
            }
            return best < double.PositiveInfinity;
        }

        static int IndexOf(RoadEdge e, double s)
        {
            int lo = 0, hi = e.n - 1;
            while (lo < hi)
            {
                int m = (lo + hi + 1) / 2;
                if (e.s[m] <= s) lo = m; else hi = m - 1;
            }
            return lo;
        }

        void Begin(Stunt st, int end)
        {
            active = st;
            enteredAt = end;
            zoneTime = 0;
            banked = 0;
            chain = 0;
            chainTime = 0;
            sinceSlide = 0;
            Live = 0;
            airborne = false;
            armedFor = 0;
            flown = 0;
        }

        StuntResult Continue(Vehicle v, double x, double z, double dt)
        {
            var st = active;
            Near(st, x, z, out double s, out double lat);
            var e = W.Edges[st.Edge];
            bool offRoad = Math.Abs(lat) > e.info.halfWidth + 12;
            if (st.Kind == StuntKind.DangerSign)
            {
                armedFor += dt;
                if (!airborne)
                {
                    // A takeoff from the crest's last 40 m counts (as the targets were measured), not a hop on the run-up.
                    if (v.groundedCount == 0 && (s - st.S0) * st.Dir > -40)
                    {
                        airborne = true;
                        takeX = x;
                        takeZ = z;
                    }
                    else if (armedFor > 4 || offRoad || (s - st.S0) * st.Dir > 40) return Cancel();
                    return null;
                }
                Live = Math.Sqrt((x - takeX) * (x - takeX) + (z - takeZ) * (z - takeZ));
                if (v.groundedCount == 0) flown += dt;
                if (v.groundedCount >= 2 && v.airTime == 0)
                {
                    // Landed: a jump if it flew for a quarter of a second and two metres, on its wheels.
                    if (flown < 0.25 || Live < 2)
                    {
                        // Just a hop: still on the run-up, wait for the real takeoff.
                        airborne = false;
                        flown = 0;
                        Live = 0;
                        return null;
                    }
                    return v.up.y > 0.7 ? Finish(st, Live) : Cancel();
                }
                return armedFor > 12 ? Cancel() : null;
            }
            zoneTime += dt;
            // Left the zone: out of the far end scores, back out of the near end or off the road does not.
            double from = enteredAt == 0 ? st.S0 : st.S1, to = enteredAt == 0 ? st.S1 : st.S0;
            double len = Math.Abs(st.S1 - st.S0);
            bool pastEnd = enteredAt == 0 ? s >= st.S1 : s <= st.S0;
            bool backOut = enteredAt == 0 ? s < st.S0 - 5 : s > st.S1 + 5;
            if (offRoad || backOut || zoneTime > 240) return Cancel();
            if (st.Kind == StuntKind.SpeedZone)
            {
                double done = Math.Abs(s - from);
                Live = zoneTime > 0.5 ? done / zoneTime * 3.6 : v.speed * 3.6;
                if (pastEnd) return Finish(st, len / zoneTime * 3.6);
                return null;
            }
            // Drift points: speed times angle (full credit from 40 degrees), times the chain bonus.
            double angle = Math.Abs(Math.Atan2(v.vLat, Math.Abs(v.vLong) + 1e-9)) * 180 / Math.PI;
            bool sliding = v.speed > 30 / 3.6 && v.vLong > 0 && angle >= 10 && angle <= 90 && v.groundedCount >= 2;
            if (v.impact > 4000)
            {
                // A hit loses the chain under way.
                chain = 0;
                chainTime = 0;
            }
            if (sliding)
            {
                sinceSlide = 0;
                chainTime += dt;
                chain += v.speed * Math.Min(1, (angle - 10) / 30) * 10 * dt * Chain;
            }
            else
            {
                sinceSlide += dt;
                if (sinceSlide > 1.2 && chain > 0)
                {
                    banked += chain;
                    chain = 0;
                    chainTime = 0;
                }
            }
            Live = banked + chain;
            if (pastEnd) return Finish(st, banked + chain);
            _ = to;
            return null;
        }

        StuntResult Cancel()
        {
            Forget(active);
            active = null;
            Live = 0;
            return null;
        }

        void Forget(Stunt st)
        {
            int n = All.IndexOf(st);
            if (n >= 0) lastS[n] = double.NaN;
        }

        StuntResult Finish(Stunt st, double score)
        {
            Forget(st);
            active = null;
            Live = 0;
            double before = Best(st);
            int starsBefore = before > 0 ? st.StarsFor(before) : 0;
            var r = new StuntResult { Stunt = st, Score = score, Stars = st.StarsFor(score), Best = score > before };
            if (r.Best) p.StuntBest[st.Id] = score;
            // Each star pays once, the first time it is reached.
            for (int k = starsBefore + 1; k <= r.Stars; k++)
            {
                r.Xp += 150 * k;
                r.Credits += 1000 * k;
            }
            p.Xp += r.Xp;
            p.Credits += r.Credits;
            return r;
        }
    }
}
