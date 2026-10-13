// A recorded run played back: where a car was at each moment of a race
// (sampled 60 times a second) and how far along the route it had come. A
// time trial races the AI's own run as a ghost: no collisions, only the car
// to beat, the time to beat, and the gap to it at any point of the route.
// The run is recorded headless, in a simulation of its own, so it can be
// made on a worker thread while the player lines up. No UnityEngine.
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    public sealed class Ghost
    {
        /// <summary>Samples a second.</summary>
        public const int Rate = 60;
        public string Name, CarId;
        /// <summary>The time the run took to the finish (NaN if it never got there).</summary>
        public double FinishTime = double.NaN;
        readonly List<double> t = new List<double>(), x = new List<double>(), y = new List<double>(), z = new List<double>();
        readonly List<double> qx = new List<double>(), qy = new List<double>(), qz = new List<double>(), qw = new List<double>();
        readonly List<double> progress = new List<double>(), steer = new List<double>();

        public int Count => t.Count;
        public double Duration => t.Count > 0 ? t[t.Count - 1] : 0;

        /// <summary>Record the car as it is at race time clock, progress metres from the start.</summary>
        public void Add(double clock, Vehicle v, double along)
        {
            t.Add(clock);
            x.Add(v.pos.x);
            y.Add(v.pos.y);
            z.Add(v.pos.z);
            qx.Add(v.quat.x);
            qy.Add(v.quat.y);
            qz.Add(v.quat.z);
            qw.Add(v.quat.w);
            // Progress (negative on the grid) never goes backwards in the record, so the gap can be looked up by it.
            progress.Add(progress.Count > 0 ? Math.Max(progress[progress.Count - 1], along) : along);
            steer.Add(v.wheels[0].steer);
        }

        int Index(double clock, out double f)
        {
            if (t.Count == 0)
            {
                f = 0;
                return -1;
            }
            int lo = 0, hi = t.Count - 1;
            if (clock <= t[0])
            {
                f = 0;
                return 0;
            }
            if (clock >= t[hi])
            {
                f = 0;
                return hi;
            }
            while (hi - lo > 1)
            {
                int m = (lo + hi) / 2;
                if (t[m] <= clock) lo = m; else hi = m;
            }
            f = (clock - t[lo]) / (t[hi] - t[lo]);
            return lo;
        }

        /// <summary>Puts a car where the ghost was at race time clock (position and orientation, interpolated).</summary>
        public void Pose(double clock, Vehicle into)
        {
            int i = Index(clock, out double f);
            if (i < 0) return;
            int j = Math.Min(t.Count - 1, i + 1);
            into.pos.Set(x[i] + (x[j] - x[i]) * f, y[i] + (y[j] - y[i]) * f, z[i] + (z[j] - z[i]) * f);
            // Normalised lerp of the orientation (60 samples a second are close enough for it).
            double ax = qx[i], ay = qy[i], az = qz[i], aw = qw[i], bx = qx[j], by = qy[j], bz = qz[j], bw = qw[j];
            if (ax * bx + ay * by + az * bz + aw * bw < 0)
            {
                bx = -bx;
                by = -by;
                bz = -bz;
                bw = -bw;
            }
            double rx = ax + (bx - ax) * f, ry = ay + (by - ay) * f, rz = az + (bz - az) * f, rw = aw + (bw - aw) * f;
            double l = Math.Sqrt(rx * rx + ry * ry + rz * rz + rw * rw);
            into.quat.x = rx / l;
            into.quat.y = ry / l;
            into.quat.z = rz / l;
            into.quat.w = rw / l;
            double st = steer[i] + (steer[j] - steer[i]) * f;
            into.wheels[0].steer = st;
            into.wheels[1].steer = st;
        }

        /// <summary>How far along the route the ghost was at race time clock, m.</summary>
        public double ProgressAt(double clock)
        {
            int i = Index(clock, out double f);
            if (i < 0) return 0;
            int j = Math.Min(t.Count - 1, i + 1);
            return progress[i] + (progress[j] - progress[i]) * f;
        }

        /// <summary>When the ghost first got this far along the route (NaN if it never did).</summary>
        public double TimeAt(double along)
        {
            if (t.Count == 0 || along > progress[progress.Count - 1]) return double.NaN;
            if (along <= progress[0]) return t[0];
            int lo = 0, hi = progress.Count - 1;
            while (hi - lo > 1)
            {
                int m = (lo + hi) / 2;
                if (progress[m] < along) lo = m; else hi = m;
            }
            double d = progress[hi] - progress[lo];
            return d > 0 ? t[lo] + (t[hi] - t[lo]) * (along - progress[lo]) / d : t[hi];
        }

        /// <summary>The gap to the ghost for a car this far along at race time clock, s: + behind it, - ahead.</summary>
        public double Gap(double clock, double along)
        {
            double tg = TimeAt(along);
            return double.IsNaN(tg) ? clock - Duration : clock - tg;
        }

        /// <summary>
        /// Drive the event's route alone with the AI (a driver of this skill in this car) in a simulation of its own,
        /// recording the run. The road as wet as given (the player's race holds the weather too).
        /// </summary>
        public static Ghost Record(WorldData w, RaceEvent ev, Rival rival, RaceSession.AbilityCache abilities, double wetness, uint seed, double maxTime = 900)
        {
            var sim = new Simulation(w, SpawnAt.Island) { WorldTime = false };
            sim.Weather.Wetness = wetness;
            var race = new RaceSession(sim, ev, new List<Rival> { rival }, abilities, seed, includePlayer: false);
            var e = race.Entrants[0];
            var g = new Ghost { Name = rival.Driver.Name, CarId = rival.CarId };
            var hold = Controls.Neutral();
            int tick = 0;
            while (race.Clock < maxTime && !e.Finished)
            {
                race.Step(hold);
                if (race.Clock >= 0 && tick++ % (240 / Rate) == 0) g.Add(race.Clock, e.Car.vehicle, e.Progress);
            }
            // Past the line too, so the ghost does not vanish on it.
            for (int k = 0; k < 2 * 240 && e.Finished; k++)
            {
                race.Step(hold);
                if (tick++ % (240 / Rate) == 0) g.Add(race.Clock, e.Car.vehicle, e.Progress);
            }
            g.FinishTime = e.Finished ? e.FinishTime : double.NaN;
            race.End();
            return g;
        }
    }
}
