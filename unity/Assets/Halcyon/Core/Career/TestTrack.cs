// The tuning test track: the numbers a tuner looks at after each change.
// 0-100 km/h, top speed, 100-0 km/h, steady lateral grip, and the understeer
// gradient: how much more steering a car needs per g of cornering on a
// constant radius (deg/g; positive understeers, negative oversteers).
using System;

namespace Halcyon.Core
{
    public sealed class TestTrackResult
    {
        public double ZeroTo100, TopSpeed, Braking, LateralG;
        /// <summary>deg of steering per g of lateral acceleration on a 60 m circle (+ = understeer).</summary>
        public double Understeer;
        public CarStats Stats;
    }

    public static class TestTrack
    {
        /// <summary>
        /// Drives a 60 m circle at two steady speeds (about 0.3 g and 0.6 g of the
        /// car's grip) and compares the mean steer angle: on a constant radius
        /// the kinematic angle cancels, so the difference per g is the car's
        /// understeer gradient.
        /// </summary>
        public static double UndersteerGradient(VehicleParams p, double grip, int surface = Surface.Asphalt, double radius = 60)
        {
            var (d1, a1) = SteadySteer(p, radius, Math.Sqrt(0.3 * grip * 9.81 * radius), surface);
            var (d2, a2) = SteadySteer(p, radius, Math.Sqrt(0.6 * grip * 9.81 * radius), surface);
            if (double.IsNaN(d1) || double.IsNaN(d2) || a2 - a1 < 0.05) return double.NaN;
            return (d2 - d1) * 180 / Math.PI / (a2 - a1);
        }

        /// <summary>Mean steer angle (rad, + = left) and lateral g holding a circle at a speed.</summary>
        static (double steer, double g) SteadySteer(VehicleParams p, double radius, double speed, int surface)
        {
            var ground = new FlatGround((x, z) => surface);
            var v = Telemetry.NewTestVehicle(p, new PartialAids { steering = SteeringAssist.Off, esc = false });
            v.Place(radius, 0, 0, 0);
            for (int i = 0; i < 120; i++) v.Step(Loop.SIM_DT, ground);
            v.SetSpeed(speed);
            double L = p.wheelbase, errInt = 0, sumSteer = 0, sumG = 0;
            int n = 0;
            for (double t = 0; t < 8; t += Loop.SIM_DT)
            {
                double px = v.pos.x, pz = v.pos.z;
                double rNow = Math.Sqrt(px * px + pz * pz);
                double err = rNow - radius;
                double hx = pz / Math.Max(1e-6, rNow), hz = -px / Math.Max(1e-6, rNow);
                double headErr = DMath.Atan2(v.fwd.x * hz - v.fwd.z * hx, v.fwd.x * hx + v.fwd.z * hz);
                errInt = Math.Max(-4, Math.Min(4, errInt + err * Loop.SIM_DT));
                double delta = L / radius + 0.04 * err + 0.02 * errInt - 0.6 * headErr + 0.15 * (v.vLong / radius - v.yawRate);
                v.input.steer = Math.Max(-1, Math.Min(1, -delta / p.maxSteer));
                double dv = speed - v.vLong;
                v.input.throttle = Math.Max(0, Math.Min(1, 0.25 + dv * 0.6));
                v.input.brake = Math.Max(0, Math.Min(1, -dv * 0.3 - 0.1));
                v.Step(Loop.SIM_DT, ground);
                if (Math.Abs(err) > 6) return (double.NaN, double.NaN);
                if (t > 5)
                {
                    sumSteer += v.steerAngle;
                    sumG += v.speed * v.speed / Math.Max(1, rNow) / 9.81;
                    n++;
                }
            }
            return (sumSteer / n, sumG / n);
        }

        /// <summary>Everything the tuning screen shows, for a car with a tune and upgrades.</summary>
        public static TestTrackResult Run(CarSpec spec, Tune tune, Upgrades up)
        {
            var stats = Performance.Measure(spec, tune, up);
            var p = VehicleParams.Build(spec, tune, up);
            return new TestTrackResult
            {
                ZeroTo100 = stats.ZeroTo100,
                TopSpeed = stats.TopSpeed,
                Braking = stats.Braking,
                LateralG = stats.Skidpad,
                Understeer = UndersteerGradient(p, stats.Skidpad),
                Stats = stats,
            };
        }
    }
}
