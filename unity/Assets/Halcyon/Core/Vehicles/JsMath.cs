// JavaScript Math semantics that System.Math does not guarantee on every
// runtime Unity uses. The vehicle port calls these wherever the TypeScript
// calls Math.max, Math.min or Math.hypot, so results match bit for bit.
//
// - Math.max / Math.min: NaN if either argument is NaN, and +0 is larger
//   than -0. .NET Core 3.0+ agrees, but Mono's and the .NET Framework's
//   System.Math.Max(+0, -0) return -0 and Min(-0, +0) returns +0.
// - Math.hypot: V8 scales by the largest argument and Kahan-sums the squares,
//   which differs from sqrt(x*x + y*y) in the last bit for about a third of
//   inputs. Only the telemetry measurements use it (as the TypeScript does).
using System;

namespace Halcyon.Core
{
    public static class JsMath
    {
        /// <summary>JavaScript's Math.max(a, b).</summary>
        public static double Max(double a, double b)
        {
            if (a > b) return a;
            if (b > a) return b;
            if (double.IsNaN(a)) return a;
            if (double.IsNaN(b)) return b;
            // Equal: only the sign of zero can differ, and +0 wins.
            return a == 0 && BitConverter.DoubleToInt64Bits(a) < 0 ? b : a;
        }

        /// <summary>JavaScript's Math.min(a, b).</summary>
        public static double Min(double a, double b)
        {
            if (a < b) return a;
            if (b < a) return b;
            if (double.IsNaN(a)) return a;
            if (double.IsNaN(b)) return b;
            // Equal: only the sign of zero can differ, and -0 wins.
            return a == 0 && BitConverter.DoubleToInt64Bits(a) >= 0 ? b : a;
        }

        /// <summary>
        /// V8's Math.hypot(x, y), bit for bit: the absolute values are scaled by
        /// the largest one and their squares summed with Kahan compensation.
        /// </summary>
        public static double Hypot(double x, double y)
        {
            bool nan = false;
            double ax = 0, ay = 0, max = 0;
            if (double.IsNaN(x)) nan = true;
            else
            {
                ax = Math.Abs(x);
                if (ax > max) max = ax;
            }
            if (double.IsNaN(y)) nan = true;
            else
            {
                ay = Math.Abs(y);
                if (ay > max) max = ay;
            }
            if (double.IsPositiveInfinity(max)) return double.PositiveInfinity;
            if (nan) return double.NaN;
            if (max == 0) return 0;
            double sum = 0;
            double compensation = 0;
            double n = ax / max;
            double summand = n * n - compensation;
            double preliminary = sum + summand;
            compensation = (preliminary - sum) - summand;
            sum = preliminary;
            n = ay / max;
            summand = n * n - compensation;
            preliminary = sum + summand;
            sum = preliminary;
            return Math.Sqrt(sum) * max;
        }
    }
}
