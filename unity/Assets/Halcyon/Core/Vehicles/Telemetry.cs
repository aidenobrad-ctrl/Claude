// Standard performance tests, run headless on flat ground. Used by the
// physics validation suite, the PI calculation and the in-game test track.
// Ported from the web version's src/vehicles/telemetry.ts; the measurements
// use V8's Math.hypot (JsMath.Hypot) like the TypeScript, so results match.
using System;

namespace Halcyon.Core
{
    public sealed class AccelResult
    {
        /// <summary>Seconds to 100 km/h, 200 km/h (NaN if not reached).</summary>
        public double t100 = double.NaN;
        public double t200 = double.NaN;
        /// <summary>Quarter-mile (402 m) time and trap speed (km/h).</summary>
        public double quarter = double.NaN;
        public double quarterKmh = double.NaN;
        /// <summary>Top speed reached, km/h, and when the car stopped gaining.</summary>
        public double topKmh;
        public double topTime;
        public int shifts;
        public double maxSlip;
    }

    public sealed class BrakeResult
    {
        public double distance;
        public double time;
        /// <summary>Largest yaw rate during the stop, rad/s (stability check).</summary>
        public double maxYaw;
    }

    public sealed class SkidpadResult
    {
        /// <summary>Maximum steady lateral acceleration, g.</summary>
        public double g;
        public double kmh;
        /// <summary>Mean radius error while steady, m.</summary>
        public double err;
    }

    public static class Telemetry
    {
        static IGround Flat(int surface) => new FlatGround((x, z) => surface);

        static void Settle(Vehicle v, IGround ground, double seconds = 1)
        {
            for (int i = 0; i < seconds / Loop.SIM_DT; i++) v.Step(Loop.SIM_DT, ground);
        }

        /// <summary>newTestVehicle(params, aids): a vehicle with defaultAids() overridden by <paramref name="aids"/>.</summary>
        public static Vehicle NewTestVehicle(VehicleParams p, PartialAids aids = null)
        {
            var v = new Vehicle(p);
            Aids a = Aids.Default();
            if (aids != null) aids.ApplyTo(a);
            v.aids = a;
            return v;
        }

        /// <summary>Standing start at full throttle until the car stops gaining speed.</summary>
        public static AccelResult AccelTest(VehicleParams p, double? maxTime = null, PartialAids aids = null, int surface = Surface.Asphalt, double wetness = 0)
        {
            IGround ground = Flat(surface);
            Vehicle v = NewTestVehicle(p, aids);
            v.wetness = wetness;
            Settle(v, ground);
            V3 start = v.pos.Clone();
            var r = new AccelResult();
            double tMax = maxTime ?? 120;
            double t = 0;
            int lastGear = v.gear;
            double best = 0;
            double bestT = 0;
            v.input.throttle = 1;
            while (t < tMax)
            {
                v.Step(Loop.SIM_DT, ground);
                t += Loop.SIM_DT;
                double kmh = v.vLong * 3.6;
                if (double.IsNaN(r.t100) && kmh >= 100) r.t100 = t;
                if (double.IsNaN(r.t200) && kmh >= 200) r.t200 = t;
                double dist = JsMath.Hypot(v.pos.x - start.x, v.pos.z - start.z);
                if (double.IsNaN(r.quarter) && dist >= 402.336)
                {
                    r.quarter = t;
                    r.quarterKmh = kmh;
                }
                if (v.gear != lastGear)
                {
                    r.shifts++;
                    lastGear = v.gear;
                }
                for (int i = 0; i < v.wheels.Length; i++) r.maxSlip = JsMath.Max(r.maxSlip, v.wheels[i].slipRatio);
                if (kmh > best + 0.05)
                {
                    best = kmh;
                    bestT = t;
                }
                // Stop once the car has gained less than 0.05 km/h in 4 s.
                if (t - bestT > 4 && t > 15) break;
            }
            r.topKmh = best;
            r.topTime = bestT;
            return r;
        }

        /// <summary>Full braking from a speed to a standstill, in a straight line.</summary>
        public static BrakeResult BrakeTest(VehicleParams p, double fromKmh = 100, PartialAids aids = null, int surface = Surface.Asphalt, double wetness = 0)
        {
            IGround ground = Flat(surface);
            Vehicle v = NewTestVehicle(p, aids);
            v.wetness = wetness;
            Settle(v, ground);
            v.SetSpeed(fromKmh / 3.6);
            // Let the suspension settle at speed with a whiff of throttle.
            v.input.throttle = 0.25;
            for (int i = 0; i < 60; i++) v.Step(Loop.SIM_DT, ground);
            v.SetSpeed(fromKmh / 3.6);
            v.input.throttle = 0;
            v.input.brake = 1;
            V3 start = v.pos.Clone();
            double t = 0;
            double maxYaw = 0;
            while (v.vLong > 0.05 && t < 20)
            {
                v.Step(Loop.SIM_DT, ground);
                t += Loop.SIM_DT;
                maxYaw = JsMath.Max(maxYaw, Math.Abs(v.yawRate));
            }
            return new BrakeResult { distance = JsMath.Hypot(v.pos.x - start.x, v.pos.z - start.z), time = t, maxYaw = maxYaw };
        }

        /// <summary>
        /// Constant-radius skidpad. The driver steers with a kinematic feedforward
        /// (L/R), a yaw-rate term and PI feedback on the radius error, so it holds
        /// the line until the tires truly give up, while the target speed creeps up.
        /// The result is the highest lateral acceleration held for a full second
        /// with the car within 0.6 m of the line. Aids default to steering off and
        /// ESC off; <paramref name="aids"/> overrides that.
        /// </summary>
        public static SkidpadResult SkidpadTest(VehicleParams p, double radius = 40, PartialAids aids = null, int surface = Surface.Asphalt, double? maxTime = null, double wetness = 0)
        {
            IGround ground = Flat(surface);
            var baseAids = new PartialAids { steering = SteeringAssist.Off, esc = false };
            Vehicle v = NewTestVehicle(p, baseAids.Merge(aids));
            v.wetness = wetness;
            // Counter-clockwise (left turn) around the origin, starting at (R, 0).
            // Heading at (R,0) tangent toward -Z (north) is yaw 0.
            v.Place(radius, 0, 0, 0);
            Settle(v, ground, 0.5);
            double L = p.wheelbase;
            double vTarget = 8;
            double errInt = 0;
            v.SetSpeed(vTarget);
            double best = 0;
            double bestKmh = 0;
            double steadyTime = 0;
            double accG = 0;
            int accN = 0;
            double errSum = 0;
            int errN = 0;
            double tMax = maxTime ?? 160;
            for (double t = 0; t < tMax; t += Loop.SIM_DT)
            {
                double px = v.pos.x;
                double pz = v.pos.z;
                double rNow = Math.Sqrt(px * px + pz * pz);
                double err = rNow - radius;
                // Unit tangent for travel around the circle (decreasing angle, a left turn).
                double hx = pz / JsMath.Max(1e-6, rNow);
                double hz = -px / JsMath.Max(1e-6, rNow);
                // Heading error, yaw sense: + when the car points left of the tangent.
                double headErr = DMath.Atan2(v.fwd.x * hz - v.fwd.z * hx, v.fwd.x * hx + v.fwd.z * hz);
                errInt = M.Clamp(errInt + err * Loop.SIM_DT, -4, 4);
                double yawTarget = v.vLong / radius;
                // Steer angle (+ = left): kinematic L/R, PI on running wide, heading and yaw-rate terms.
                double delta = L / radius + 0.04 * err + 0.02 * errInt - 0.6 * headErr + 0.15 * (yawTarget - v.yawRate);
                v.input.steer = M.Clamp(-delta / p.maxSteer, -1, 1);
                // Speed: creep the target up while the car holds the line.
                if (Math.Abs(err) < 0.6) vTarget += 0.12 * Loop.SIM_DT;
                else if (err > 1.5) vTarget -= 0.4 * Loop.SIM_DT;
                double dv = vTarget - v.vLong;
                v.input.throttle = M.Clamp(0.25 + dv * 0.6, 0, 1);
                v.input.brake = M.Clamp(-dv * 0.3 - 0.1, 0, 1);
                v.Step(Loop.SIM_DT, ground);
                double g = (v.speed * v.speed) / JsMath.Max(1, rNow) / 9.81;
                if (Math.Abs(err) < 0.6 && Math.Abs(v.input.steer) < 0.98)
                {
                    steadyTime += Loop.SIM_DT;
                    accG += g;
                    accN++;
                    errSum += Math.Abs(err);
                    errN++;
                    if (steadyTime >= 1)
                    {
                        double mean = accG / accN;
                        if (mean > best)
                        {
                            best = mean;
                            bestKmh = v.speed * 3.6;
                        }
                        steadyTime = 0;
                        accG = 0;
                        accN = 0;
                    }
                }
                else
                {
                    steadyTime = 0;
                    accG = 0;
                    accN = 0;
                }
                if (Math.Abs(err) > 8) break;
            }
            return new SkidpadResult { g = best, kmh = bestKmh, err = errN != 0 ? errSum / errN : double.NaN };
        }
    }
}
