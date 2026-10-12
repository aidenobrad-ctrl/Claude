// The GPS: the road route from the car to a destination, kept up to date
// as the car drives. It follows the car's progress along the route, plans
// a new route when the car strays from it, finds the next junction where
// the route turns (for the HUD's arrow), notices when the car faces the
// wrong way along it, and says when the car has arrived. A destination off
// the road (a landmark in a field, a point picked on the map) is reached
// at the road nearest to it. No UnityEngine.
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    /// <summary>
    /// A junction where the route leaves the road it is on: where along the route, the heading change
    /// through it (radians, + = right), and which way to go. A turn is a real change of direction; a
    /// keep is a fork where the route takes a gentler branch than the straightest road on.
    /// </summary>
    public struct GpsTurn
    {
        public double S, Angle;
        public int Node;
        public bool Right, Keep;
    }

    public sealed class Gps
    {
        /// <summary>Further than this from the route, the car is off it (and a new route is planned).</summary>
        public const double OffRoute = 45;
        /// <summary>A junction counts as a turn when the route's heading changes by more than this through it.</summary>
        public const double TurnAngle = 35 * Math.PI / 180;
        /// <summary>The car has arrived within this distance of the route's end.</summary>
        public const double ArriveWithin = 30;

        readonly WorldData w;
        readonly IslandGround ground;
        readonly List<int> nodes = new List<int>();
        readonly List<GpsTurn> turns = new List<GpsTurn>();
        double replanIn, wrongFor;

        public Gps(WorldData world, IslandGround g)
        {
            w = world;
            ground = g;
        }

        /// <summary>The route from the car to the destination (null when none is set, or no road leads there).</summary>
        public RaceRoute Route { get; private set; }
        /// <summary>A destination is set (even while no route to it has been found).</summary>
        public bool Active { get; private set; }
        public double TargetX { get; private set; }
        public double TargetZ { get; private set; }
        public string TargetName { get; private set; }
        /// <summary>How far along the route the car is, m.</summary>
        public double Progress { get; private set; }
        /// <summary>The car's distance from the route, m.</summary>
        public double OffBy { get; private set; }
        /// <summary>The road distance left to the destination, m.</summary>
        public double Remaining => Route == null ? 0 : Math.Max(0, Route.Length - Progress);
        /// <summary>The car has been facing back along the route for a while: time to turn around.</summary>
        public bool WrongWay { get; private set; }
        /// <summary>Changes whenever the route does, so views know to redraw it.</summary>
        public int Version { get; private set; }
        /// <summary>New routes planned because the car left the old one.</summary>
        public int Replans { get; private set; }
        /// <summary>The junctions where the route turns, in order along it.</summary>
        public IReadOnlyList<GpsTurn> Turns => turns;

        /// <summary>Set the destination and plan the route there from the car at (x, z). False if no road leads there.</summary>
        public bool Set(double fromX, double fromZ, double x, double z, string name)
        {
            Active = true;
            TargetX = x;
            TargetZ = z;
            TargetName = name;
            Replans = 0;
            return Plan(fromX, fromZ);
        }

        public void Clear()
        {
            Active = false;
            Route = null;
            turns.Clear();
            WrongWay = false;
            Version++;
        }

        /// <summary>Plan again from (x, z) now (after the car is moved: fast travel, a reset).</summary>
        public bool Plan(double x, double z)
        {
            if (!Active) return false;
            Route = RouteFinder.Find(w, ground, x, z, TargetX, TargetZ, "GPS", nodes);
            turns.Clear();
            Progress = 0;
            OffBy = 0;
            WrongWay = false;
            wrongFor = 0;
            replanIn = 1.5;
            Version++;
            if (Route == null) return false;
            // The junctions where the route leaves the road. Plain bends are not announced: only
            // junctions with a choice of road (three or more ways), where the route turns, or forks
            // off along a branch when another road runs on straighter (or nearly as straight).
            double hint = 0;
            foreach (int k in nodes)
            {
                var node = w.Nodes[k];
                Route.Project(node.x, node.z, hint, 5, Route.Length, out double s, out _, out double d);
                if (d > 10) continue;
                hint = s;
                if (node.edges.Count < 3 || s < 1 || s > Route.Length - 1) continue;
                // Heading in, and the way the route leaves (both over 25 m, so a kink at the node does not count).
                Route.At(Math.Max(0, s - 25), out double ix, out _, out double iz, out _, out _);
                Route.At(Math.Min(Route.Length, s + 25), out double ox, out _, out double oz, out _, out _);
                double a = Angle(node.x - ix, node.z - iz, ox - node.x, oz - node.z);
                // The other roads on from the junction (not back the way the route came, nor the route's own).
                double best = double.PositiveInfinity, bestAngle = 0;
                foreach (int eid in node.edges)
                {
                    var e = w.Edges[eid];
                    for (int end = 0; end < 2; end++)
                    {
                        if ((end == 0 ? e.a : e.b) != k) continue;
                        Leaving(e, end == 0, 25, out double lx, out double lz);
                        double b = Angle(node.x - ix, node.z - iz, lx - node.x, lz - node.z);
                        if (Math.Abs(b) > 0.75 * Math.PI || Math.Abs(b - a) < 0.12) continue;
                        if (Math.Abs(b) < best)
                        {
                            best = Math.Abs(b);
                            bestAngle = b;
                        }
                    }
                }
                bool turn = Math.Abs(a) >= TurnAngle;
                bool keep = !turn && best < double.PositiveInfinity && Math.Abs(a) > 8 * Math.PI / 180 && (best < Math.Abs(a) || Math.Abs(bestAngle - a) < 25 * Math.PI / 180);
                if (turn || keep) turns.Add(new GpsTurn { S = s, Angle = a, Node = k, Keep = keep, Right = turn ? a > 0 : a > bestAngle });
            }
            return true;
        }

        /// <summary>The signed angle from direction a to direction b, radians in (-pi, pi], + = to the right.</summary>
        static double Angle(double ax, double az, double bx, double bz) =>
            // + = right: with x east and z south, turning right takes (tx, tz) towards (-tz, tx).
            Math.Atan2(ax * bz - az * bx, ax * bx + az * bz);

        /// <summary>The point an edge reaches a distance along from one of its ends.</summary>
        static void Leaving(RoadEdge e, bool fromA, double along, out double x, out double z)
        {
            double target = fromA ? Math.Min(along, e.length) : Math.Max(0, e.length - along);
            int i = 0;
            while (i < e.n - 1 && e.s[i] < target) i++;
            x = e.x[i];
            z = e.z[i];
        }

        /// <summary>
        /// The car is at (x, z) heading (fx, fz) (call every frame or few): follows it along the route,
        /// re-plans when it has left the route, and returns true once when it arrives (the GPS then clears).
        /// </summary>
        public bool Update(double x, double z, double fx, double fz, double dt)
        {
            if (!Active) return false;
            if (Route == null)
            {
                // No road led there from where the car was: try again now and then.
                replanIn -= dt;
                if (replanIn <= 0) Plan(x, z);
                replanIn = Math.Max(replanIn, 3);
                return false;
            }
            Route.Project(x, z, Progress, 40, 160, out double s, out _, out double dist);
            if (dist > OffRoute)
            {
                // A shortcut (or a jump along the route): anywhere on it will do.
                Route.ProjectAny(x, z, out double s2, out _, out double d2);
                if (d2 < dist)
                {
                    s = s2;
                    dist = d2;
                }
            }
            OffBy = dist;
            if (dist > OffRoute)
            {
                replanIn -= dt;
                if (replanIn <= 0)
                {
                    Replans++;
                    Plan(x, z);
                    // Far from any road, planning again every second and a half gains nothing.
                    if (Route != null)
                    {
                        Route.Project(x, z, 0, 0, 200, out _, out _, out double away);
                        if (away > OffRoute) replanIn = 5;
                    }
                }
                return false;
            }
            replanIn = 1.5;
            Progress = s;
            // Facing back along the route (and not just turning round at a junction).
            Route.At(s, out _, out _, out _, out double tx, out double tz);
            double fl = Math.Sqrt(fx * fx + fz * fz);
            bool against = fl > 0 && (fx * tx + fz * tz) / fl < -0.35;
            wrongFor = against ? wrongFor + dt : 0;
            WrongWay = wrongFor > 2;
            if (Remaining < ArriveWithin)
            {
                Clear();
                return true;
            }
            return false;
        }

        /// <summary>The next junction where the route turns, ahead of the car; false when the road runs on to the destination.</summary>
        public bool NextTurn(out GpsTurn turn, out double distance)
        {
            foreach (var t in turns)
            {
                if (t.S <= Progress + 2) continue;
                turn = t;
                distance = t.S - Progress;
                return true;
            }
            turn = default;
            distance = Remaining;
            return false;
        }

        /// <summary>A distance for the HUD: "350 m" (to the nearest 10 m) or "4.2 km".</summary>
        public static string Distance(double m) => m < 1000 ? $"{Math.Max(10, Math.Round(m / 10) * 10):0} m" : $"{m / 1000:0.0} km";
    }
}
