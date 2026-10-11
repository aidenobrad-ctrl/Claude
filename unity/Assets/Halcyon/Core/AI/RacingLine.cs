// Racing lines and speed profiles.
//
// The line is the centreline moved sideways by an offset per sample, chosen to
// minimise the sum of squared curvature (second differences) within the track
// limits: Gauss-Seidel sweeps of the biharmonic update
//   p_i <- (-p_{i-2} + 4 p_{i-1} + 4 p_{i+1} - p_{i+2}) / 6
// projected onto each sample's normal and clamped to the drivable width, run
// coarse-to-fine so long corners converge quickly.
//
// A speed profile is one car's fastest speed along that line under its own
// measured limits (CarAbility): cornering grip with downforce, an
// acceleration curve, braking, all shared through a friction circle, with
// forward (acceleration) and backward (braking) passes.
using System;

namespace Halcyon.Core
{
    public sealed class RacingLine
    {
        public readonly RaceRoute Route;
        /// <summary>Lateral offset from the centreline per route sample (+ = right).</summary>
        public readonly double[] Offset;
        /// <summary>Line points per route sample.</summary>
        public readonly double[] X, Z;
        /// <summary>Signed curvature of the line, 1/m (+ = turning right).</summary>
        public readonly double[] Curv;
        /// <summary>Distance along the line from each sample to the next, m.</summary>
        public readonly double[] Ds;

        RacingLine(RaceRoute r)
        {
            Route = r;
            Offset = new double[r.N];
            X = new double[r.N];
            Z = new double[r.N];
            Curv = new double[r.N];
            Ds = new double[r.N];
        }

        /// <summary>The minimum-curvature line keeping the car's centre at least halfCar + margin inside the edges.</summary>
        public static RacingLine Build(RaceRoute r, double halfCar = 1.0, double margin = 0.7, int sweeps = 400, double minRadius = 7.5)
        {
            var line = new RacingLine(r);
            int n = r.N;
            var lo = new double[n];
            var hi = new double[n];
            for (int i = 0; i < n; i++)
            {
                lo[i] = -Math.Max(0, r.WLeft[i] - halfCar - margin);
                hi[i] = Math.Max(0, r.WRight[i] - halfCar - margin);
            }
            if (!r.Closed)
            {
                lo[0] = hi[0] = 0;
                lo[n - 1] = hi[n - 1] = 0;
            }
            // Keep the line's radius at least minRadius: in a hairpin tighter than
            // that the line must swing wide (the smoothing alone would bunch points
            // on the inside, where the samples' normals converge).
            var kc = CentreCurvature(r);
            for (int i = 0; i < n; i++)
            {
                double k = kc[i];
                if (Math.Abs(k) < 1e-4) continue;
                double insideMax = 1 / Math.Abs(k) - minRadius;
                if (k > 0)
                {
                    hi[i] = Math.Min(hi[i], insideMax);
                    if (hi[i] < lo[i]) hi[i] = lo[i];
                }
                else
                {
                    lo[i] = Math.Max(lo[i], -insideMax);
                    if (lo[i] > hi[i]) lo[i] = hi[i];
                }
            }
            var a = line.Offset;
            // Coarse to fine: optimise every 16th sample, then 8th, ... 1st.
            for (int step = 16; step >= 1; step /= 2)
            {
                int iters = step == 1 ? sweeps : sweeps / 2;
                for (int it = 0; it < iters; it++)
                {
                    bool forward = (it & 1) == 0;
                    for (int q = 0; q < n; q += step)
                    {
                        int i = forward ? q : (n - 1 - q) / step * step;
                        Relax(r, a, lo, hi, i, step);
                    }
                }
                if (step > 1)
                {
                    // Interpolate the samples in between from the optimised ones.
                    for (int i = 0; i < n; i++)
                    {
                        if (i % step == 0) continue;
                        int i0 = i / step * step;
                        int i1 = i0 + step;
                        double t = (double)(i - i0) / step;
                        double a1;
                        if (i1 < n) a1 = a[i1];
                        else a1 = r.Closed ? a[0] : a[i0];
                        a[i] = Math.Max(lo[i], Math.Min(hi[i], a[i0] + (a1 - a[i0]) * t));
                    }
                }
            }
            line.Finish();
            return line;
        }

        /// <summary>The centreline itself as a line (offset 0).</summary>
        public static RacingLine Centre(RaceRoute r)
        {
            var line = new RacingLine(r);
            line.Finish();
            return line;
        }

        /// <summary>
        /// A driver's own version of the line: the offsets scaled by accuracy
        /// (1 = this line, 0 = the centreline). A less accurate driver uses less
        /// of the road, so corners are tighter and their speed profile slower.
        /// </summary>
        public RacingLine Blend(double accuracy)
        {
            if (accuracy >= 0.999) return this;
            var line = new RacingLine(Route);
            for (int i = 0; i < Offset.Length; i++) line.Offset[i] = Offset[i] * accuracy;
            line.Finish();
            return line;
        }

        /// <summary>Signed curvature of the route's centreline (+ = right), the largest over a few samples either side.</summary>
        static double[] CentreCurvature(RaceRoute r)
        {
            int n = r.N;
            var k = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (!r.Closed && (i == 0 || i == n - 1)) continue;
                int ia = Idx(r, i - 1), ib = Idx(r, i + 1);
                double ax = r.X[i] - r.X[ia], az = r.Z[i] - r.Z[ia];
                double bx = r.X[ib] - r.X[i], bz = r.Z[ib] - r.Z[i];
                double cx = r.X[ib] - r.X[ia], cz = r.Z[ib] - r.Z[ia];
                double den = Math.Sqrt(ax * ax + az * az) * Math.Sqrt(bx * bx + bz * bz) * Math.Sqrt(cx * cx + cz * cz);
                k[i] = den > 1e-9 ? 2 * (ax * bz - az * bx) / den : 0;
            }
            var o = new double[n];
            for (int i = 0; i < n; i++)
            {
                double best = 0;
                for (int d = -3; d <= 3; d++)
                {
                    double v = k[Idx(r, i + d)];
                    if (Math.Abs(v) > Math.Abs(best)) best = v;
                }
                o[i] = best;
            }
            return o;
        }

        static int Idx(RaceRoute r, int i)
        {
            int n = r.N;
            if (r.Closed) return ((i % n) + n) % n;
            return i < 0 ? 0 : i >= n ? n - 1 : i;
        }

        static void Relax(RaceRoute r, double[] a, double[] lo, double[] hi, int i, int step)
        {
            if (!r.Closed && (i == 0 || i >= r.N - 1)) return;
            int im2 = Idx(r, i - 2 * step), im1 = Idx(r, i - step), ip1 = Idx(r, i + step), ip2 = Idx(r, i + 2 * step);
            double Px(int k) => r.X[k] - r.Tz[k] * a[k];
            double Pz(int k) => r.Z[k] + r.Tx[k] * a[k];
            double qx = (-Px(im2) + 4 * Px(im1) + 4 * Px(ip1) - Px(ip2)) / 6;
            double qz = (-Pz(im2) + 4 * Pz(im1) + 4 * Pz(ip1) - Pz(ip2)) / 6;
            // Offset along the normal (right = (-tz, tx)) that brings p_i closest to q.
            double off = (qx - r.X[i]) * -r.Tz[i] + (qz - r.Z[i]) * r.Tx[i];
            a[i] = Math.Max(lo[i], Math.Min(hi[i], off));
        }

        void Finish()
        {
            var r = Route;
            int n = r.N;
            for (int i = 0; i < n; i++)
            {
                X[i] = r.X[i] - r.Tz[i] * Offset[i];
                Z[i] = r.Z[i] + r.Tx[i] * Offset[i];
            }
            for (int i = 0; i < n; i++)
            {
                int j = r.Closed ? (i + 1) % n : Math.Min(n - 1, i + 1);
                double dx = X[j] - X[i], dz = Z[j] - Z[i];
                Ds[i] = Math.Sqrt(dx * dx + dz * dz);
            }
            var raw = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (!r.Closed && (i == 0 || i == n - 1))
                {
                    raw[i] = 0;
                    continue;
                }
                int ia = Idx(r, i - 1), ib = Idx(r, i + 1);
                double ax = X[i] - X[ia], az = Z[i] - Z[ia];
                double bx = X[ib] - X[i], bz = Z[ib] - Z[i];
                double cx = X[ib] - X[ia], cz = Z[ib] - Z[ia];
                double la = Math.Sqrt(ax * ax + az * az), lb = Math.Sqrt(bx * bx + bz * bz), lc = Math.Sqrt(cx * cx + cz * cz);
                double cross = ax * bz - az * bx;
                double den = la * lb * lc;
                raw[i] = den > 1e-9 ? 2 * cross / den : 0;
            }
            // Light smoothing against sampling noise.
            for (int i = 0; i < n; i++) Curv[i] = (raw[Idx(r, i - 1)] + 2 * raw[i] + raw[Idx(r, i + 1)]) / 4;
        }

        /// <summary>Offset of the line at arc length s (interpolated).</summary>
        public double OffsetAt(double s) => Route.Sample(Offset, s);

        public double CurvAt(double s) => Route.Sample(Curv, s);
    }

    /// <summary>
    /// What a car can do, measured by driving it on the test track: cornering
    /// grip (40 m skidpad), braking (from 100 km/h), an acceleration curve, and
    /// its aero. The AI plans with these, so every AI drives its own car to the
    /// same physical limits as the player.
    /// </summary>
    public sealed class CarAbility
    {
        public const double G = 9.81;
        public const double Bin = 2.0;

        public string Id;
        /// <summary>Lateral grip in g on the skidpad.</summary>
        public double Mu;
        /// <summary>Braking deceleration in g from 100 km/h with ABS.</summary>
        public double BrakeG;
        /// <summary>Downforce as a fraction of weight per (m/s)^2.</summary>
        public double AeroK;
        /// <summary>Drag deceleration per (m/s)^2, m/s^2.</summary>
        public double DragK;
        public double TopSpeed;
        /// <summary>Full-throttle acceleration (m/s^2) at v = k * Bin m/s.</summary>
        public double[] Accel;
        public double Wheelbase, HalfWidth, Length, MaxSteer;
        /// <summary>Rear axle: share of the brake force, share of the weight, CG height over wheelbase, tyre grip (longitudinal, lateral).</summary>
        public double RearBrake, RearWeight, HOverL, RearMuX, RearMuY;

        public double AccelAt(double v)
        {
            if (v <= 0) return Accel[0];
            double f = v / Bin;
            int i = (int)f;
            if (i >= Accel.Length - 1) return Accel[Accel.Length - 1];
            double t = f - i;
            return Accel[i] + (Accel[i + 1] - Accel[i]) * t;
        }

        /// <summary>Lateral acceleration (g) the car can use before it risks tipping over (RollLimit).</summary>
        public double RollG = double.PositiveInfinity;

        /// <summary>
        /// A tall car on sticky tyres tips before it slides. Half the track over
        /// the CG height (the static stability factor) is where the inside
        /// wheels lift in a steady turn; body roll, kerbs and turn-in overshoot
        /// take some of that, so 85% of it is the usable limit, in g.
        /// </summary>
        public static double RollLimit(VehicleParams p) => 0.85 * (p.spec.trackFront + p.spec.trackRear) / 4 / p.cgHeight;

        /// <summary>Lateral acceleration limit at speed v: grip with downforce, but never past the rollover limit.</summary>
        public double LatMax(double v) => Math.Min(Mu * G * (1 + AeroK * v * v), RollG * G);

        /// <summary>Braking deceleration limit at speed v (grip with downforce, plus drag).</summary>
        public double BrakeMax(double v) => BrakeG * G * (1 + AeroK * v * v) + DragK * v * v;

        /// <summary>
        /// Braking (g) that still leaves the rear tyres grip for a lateral
        /// acceleration aLat (g). Braking moves weight off the rear axle while
        /// it keeps its share of the brake force, so in a corner the rear
        /// saturates first and the car spins. In a straight line ABS holds the
        /// rear and the full braking is stable, so the limit fades in with
        /// lateral load.
        /// </summary>
        public double StableBrake(double aLat)
        {
            aLat = Math.Abs(aLat);
            double Excess(double b)
            {
                double load = RearWeight - b * HOverL;
                if (load <= 0.02) return 1e9;
                double x = RearBrake * b / (RearMuX * load), y = RearWeight * aLat / (RearMuY * load);
                return x * x + y * y - 0.81; // keep a tenth of the rear's grip in reserve
            }
            double stable;
            if (Excess(BrakeG) <= 0) stable = BrakeG;
            else if (Excess(0) > 0) stable = 0;
            else
            {
                double lo = 0, hi = BrakeG;
                for (int i = 0; i < 40; i++)
                {
                    double mid = (lo + hi) / 2;
                    if (Excess(mid) <= 0) lo = mid;
                    else hi = mid;
                }
                stable = lo;
            }
            double t = Math.Min(1, aLat / 0.12);
            return BrakeG + (stable - BrakeG) * t;
        }

        public static CarAbility Measure(CarSpec spec, Tune tune = null, Upgrades up = null)
        {
            var p = VehicleParams.Build(spec, tune, up);
            var c = new CarAbility
            {
                Id = spec.id,
                Wheelbase = spec.wheelbase,
                HalfWidth = spec.width / 2,
                Length = spec.length,
                MaxSteer = p.maxSteer,
                RearBrake = 1 - p.brakeBias,
                RearWeight = p.a / p.wheelbase,
                HOverL = p.cgHeight / p.wheelbase,
                RearMuX = p.wheels[2].tire.info.mu * p.wheels[2].tire.info.muLong,
                RearMuY = p.wheels[2].tire.info.mu,
            };
            c.Mu = Telemetry.SkidpadTest(p, 40).g;
            c.RollG = RollLimit(p);
            var br = Telemetry.BrakeTest(p, 100);
            double v0 = 100 / 3.6;
            c.BrakeG = v0 * v0 / (2 * Math.Max(1, br.distance)) / G;
            c.AeroK = 0.5 * VehicleParams.AIR_DENSITY * p.aero.clA / (p.mass * G);
            c.DragK = 0.5 * VehicleParams.AIR_DENSITY * p.aero.cdA / p.mass;
            // Acceleration curve from a full-throttle run, binned by speed.
            var ground = new FlatGround();
            var v = Telemetry.NewTestVehicle(p);
            for (int i = 0; i < 240; i++) v.Step(Loop.SIM_DT, ground);
            const int bins = 120;
            var sum = new double[bins];
            var cnt = new int[bins];
            double prev = 0, top = 0, stall = 0;
            v.input.throttle = 1;
            for (int step = 0; step < 240 * 90; step++)
            {
                v.Step(Loop.SIM_DT, ground);
                double sp = v.vLong;
                double a = (sp - prev) / Loop.SIM_DT;
                prev = sp;
                if (step % 24 == 0 && step > 0)
                {
                    int b = (int)(sp / Bin);
                    if (b >= 0 && b < bins)
                    {
                        sum[b] += a;
                        cnt[b]++;
                    }
                }
                if (sp > top + 0.01)
                {
                    top = sp;
                    stall = 0;
                }
                else stall += Loop.SIM_DT;
                if (stall > 3) break;
            }
            c.TopSpeed = top;
            int topBin = Math.Min(bins - 1, (int)(top / Bin) + 1);
            c.Accel = new double[topBin + 1];
            double last = 0;
            for (int b = 0; b <= topBin; b++)
            {
                double val = cnt[b] > 0 ? sum[b] / cnt[b] : last;
                c.Accel[b] = Math.Max(0, val);
                last = c.Accel[b];
            }
            // The first bins are launch-limited and noisy: use the best of the first few.
            double launch = 0;
            for (int b = 0; b < Math.Min(4, c.Accel.Length); b++) launch = Math.Max(launch, c.Accel[b]);
            for (int b = 0; b < Math.Min(2, c.Accel.Length); b++) c.Accel[b] = launch;
            c.Accel[topBin] = 0;
            return c;
        }
    }

    /// <summary>Target speed along a racing line for one car.</summary>
    public sealed class SpeedProfile
    {
        public readonly RacingLine Line;
        /// <summary>Target speed per route sample, m/s.</summary>
        public readonly double[] V;

        SpeedProfile(RacingLine line)
        {
            Line = line;
            V = new double[line.Route.N];
        }

        /// <summary>
        /// grip, brake and accel scale the car's measured limits (a driver's
        /// margin): 1 drives at the measured limit.
        /// </summary>
        public static SpeedProfile Build(RacingLine line, CarAbility car, double grip = 0.96, double brake = 0.94, double accel = 1.0)
        {
            var sp = new SpeedProfile(line);
            var r = line.Route;
            int n = r.N;
            double g = CarAbility.G;
            double vmax = car.TopSpeed * 0.995;
            var v = sp.V;
            double Lat(double vv) => car.LatMax(vv) * grip;
            // Cornering limit.
            for (int i = 0; i < n; i++)
            {
                double k = Math.Abs(line.Curv[i]);
                double mu = car.Mu * grip;
                double den = k - mu * car.AeroK * g;
                v[i] = den <= 1e-6 ? vmax : Math.Min(vmax, Math.Sqrt(mu * g / den));
                if (k > 1e-9) v[i] = Math.Min(v[i], Math.Sqrt(car.RollG * grip * g / k));
            }
            int laps = r.Closed ? 2 : 1;
            // Forward: acceleration-limited, sharing grip with cornering.
            for (int pass = 0; pass < laps; pass++)
            {
                for (int q = 0; q < n; q++)
                {
                    int i = q;
                    int j = r.Closed ? (i + 1) % n : i + 1;
                    if (j >= n) break;
                    double vi = v[i];
                    double lat = vi * vi * Math.Abs(line.Curv[i]);
                    double use = Math.Min(1, lat / Math.Max(1e-6, Lat(vi)));
                    double grade = g * (r.Y[j] - r.Y[i]) / Math.Max(0.5, line.Ds[i]);
                    double a = car.AccelAt(vi) * accel * Math.Sqrt(Math.Max(0, 1 - use * use)) - grade;
                    double vj = Math.Sqrt(Math.Max(0, vi * vi + 2 * Math.Max(0, a) * line.Ds[i]));
                    if (a <= 0) vj = Math.Max(vj, Math.Min(v[j], vi));
                    if (vj < v[j]) v[j] = vj;
                }
            }
            // Backward: braking-limited.
            for (int pass = 0; pass < laps; pass++)
            {
                for (int q = n - 1; q >= 0; q--)
                {
                    int i = q;
                    int j = r.Closed ? (i + 1) % n : i + 1;
                    if (j >= n) continue;
                    double vj = v[j];
                    double lat = vj * vj * Math.Abs(line.Curv[j]);
                    double use = Math.Min(1, lat / Math.Max(1e-6, Lat(vj)));
                    double grade = g * (r.Y[j] - r.Y[i]) / Math.Max(0.5, line.Ds[i]);
                    double b = car.BrakeMax(vj) * brake * Math.Sqrt(Math.Max(0, 1 - use * use));
                    // ...and no harder than the rear can take while cornering.
                    double bs = car.StableBrake(lat / g) * g * (1 + car.AeroK * vj * vj) * brake + car.DragK * vj * vj;
                    if (bs < b) b = bs;
                    b += grade;
                    double vi = Math.Sqrt(Math.Max(0, vj * vj + 2 * Math.Max(0.5, b) * line.Ds[i]));
                    if (vi < v[i]) v[i] = vi;
                }
            }
            return sp;
        }

        public double At(double s) => Line.Route.Sample(V, s);
    }
}
