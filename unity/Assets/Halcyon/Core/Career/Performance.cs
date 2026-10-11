// The performance index (PI): one number from 100 to 999 for how fast a car
// is, computed from what the car measurably does on the test track, never
// typed in. Four measurements, the same ones docs/CARS.md lists:
//
//   0-100 km/h (s)        scored on a log scale, 11 s -> 0, 1.9 s -> 1
//   top speed (km/h)      150 -> 0, 430 -> 1
//   grip at 180 km/h (g)  skidpad grip plus downforce at 50 m/s, 0.62 -> 0, 1.30 -> 1
//   100-0 km/h (m)        48 m -> 0, 25 m -> 1
//
// S = 0.42 accel + 0.20 top speed + 0.26 grip + 0.12 braking, clamped to
// 0..1, and PI = 100 + 899 S^0.62. The weights were fitted so the stock
// roster lands in sensible classes (Pico D, Aster GT A, Vanta S2); the
// exponent spreads the crowded middle of the range.
using System;

namespace Halcyon.Core
{
    public enum PiClass { D, C, B, A, S1, S2, X }

    /// <summary>A car's measured performance and its PI.</summary>
    public sealed class CarStats
    {
        /// <summary>0-100 km/h, s.</summary>
        public double ZeroTo100;
        /// <summary>Top speed, km/h.</summary>
        public double TopSpeed;
        /// <summary>Steady lateral grip on the 40 m skidpad, g.</summary>
        public double Skidpad;
        /// <summary>Braking distance from 100 km/h, m.</summary>
        public double Braking;
        /// <summary>Downforce as a fraction of the car's weight per (m/s)^2.</summary>
        public double AeroK;
        public int Pi;
        public PiClass Class;

        /// <summary>Lateral grip at 180 km/h, g: the skidpad figure plus downforce.</summary>
        public double GripAtSpeed => Skidpad * (1 + AeroK * 50 * 50);
    }

    public static class Performance
    {
        /// <summary>Measures a car (with its tune and upgrades) on the test track and computes its PI.</summary>
        public static CarStats Measure(CarSpec spec, Tune tune = null, Upgrades up = null)
        {
            var p = VehicleParams.Build(spec, tune, up);
            var accel = Telemetry.AccelTest(p);
            var brake = Telemetry.BrakeTest(p, 100);
            var pad = Telemetry.SkidpadTest(p, 40);
            var s = new CarStats
            {
                ZeroTo100 = double.IsNaN(accel.t100) ? 30 : accel.t100,
                TopSpeed = accel.topKmh,
                Skidpad = pad.g,
                Braking = brake.distance,
                AeroK = 0.5 * VehicleParams.AIR_DENSITY * p.aero.clA / (p.mass * VehicleParams.GRAVITY),
            };
            s.Pi = Index(s);
            s.Class = ClassOf(s.Pi);
            return s;
        }

        /// <summary>The 0..1 sub-scores: acceleration, top speed, grip, braking.</summary>
        public static (double accel, double top, double grip, double brake) Scores(CarStats s)
        {
            double accel = (Math.Log(11.0) - Math.Log(Math.Max(0.5, s.ZeroTo100))) / (Math.Log(11.0) - Math.Log(1.9));
            double top = (s.TopSpeed - 150) / (430 - 150);
            double grip = (s.GripAtSpeed - 0.62) / (1.30 - 0.62);
            double brake = (48 - s.Braking) / (48 - 25);
            return (Clamp01(accel), Clamp01(top), Clamp01(grip), Clamp01(brake));
        }

        public static int Index(CarStats s)
        {
            var (a, t, g, b) = Scores(s);
            double score = Clamp01(0.42 * a + 0.20 * t + 0.26 * g + 0.12 * b);
            return (int)Math.Round(100 + 899 * Math.Pow(score, 0.62));
        }

        public static PiClass ClassOf(int pi) =>
            pi <= 500 ? PiClass.D : pi <= 600 ? PiClass.C : pi <= 700 ? PiClass.B : pi <= 800 ? PiClass.A
            : pi <= 900 ? PiClass.S1 : pi <= 998 ? PiClass.S2 : PiClass.X;

        /// <summary>The highest PI in a class (the limit for class-restricted events).</summary>
        public static int ClassMax(PiClass c) =>
            c == PiClass.D ? 500 : c == PiClass.C ? 600 : c == PiClass.B ? 700 : c == PiClass.A ? 800
            : c == PiClass.S1 ? 900 : c == PiClass.S2 ? 998 : 999;

        /// <summary>"A 703"-style label.</summary>
        public static string Label(CarStats s) => $"{s.Class} {s.Pi}";

        static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;
    }
}
