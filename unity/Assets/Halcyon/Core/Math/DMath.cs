// Deterministic transcendental functions for the simulation, ported from the
// web version's src/engine/dmath.ts so both produce identical bits.
//
// System.Math.Sin/Cos/Exp/Pow may differ between runtimes and CPUs. These use
// only +, -, *, /, sqrt and exact bit manipulation, all of which IEEE 754
// defines exactly. Coefficients come from fdlibm (Sun Microsystems, freely
// distributable). Simulation code uses these instead of System.Math's.
using System;

namespace Halcyon.Core
{
    public static class DMath
    {
        public const double PI = 3.141592653589793;
        public const double HALF_PI = 1.5707963267948966;
        public const double TAU = 6.283185307179586;

        /// <summary>2^k for integer k in the normal range, built from bits.</summary>
        static double Pow2i(int k)
        {
            if (k > 1023) return double.PositiveInfinity;
            if (k < -1022) return 0;
            return BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
        }

        // --- sin / cos ---------------------------------------------------------
        const double S1 = -1.66666666666666324348e-1;
        const double S2 = 8.33333333332248946124e-3;
        const double S3 = -1.98412698298579493134e-4;
        const double S4 = 2.75573137070700676789e-6;
        const double S5 = -2.50507602534068634195e-8;
        const double S6 = 1.58969099521155010221e-10;
        const double C1 = 4.16666666666666019037e-2;
        const double C2 = -1.38888888888741095749e-3;
        const double C3 = 2.48015872894767294178e-5;
        const double C4 = -2.75573143513906633035e-7;
        const double C5 = 2.08757232129817482790e-9;
        const double C6 = -1.13596475577881948265e-11;
        const double INV_PIO2 = 6.36619772367581382433e-1;
        const double PIO2_1 = 1.57079632673412561417;
        const double PIO2_2 = 6.07710050630396597660e-11;
        const double PIO2_3 = 2.02226624871116645580e-21;

        static double KSin(double x)
        {
            double z = x * x;
            return x + x * z * (S1 + z * (S2 + z * (S3 + z * (S4 + z * (S5 + z * S6)))));
        }

        static double KCos(double x)
        {
            double z = x * x;
            return 1 - 0.5 * z + z * z * (C1 + z * (C2 + z * (C3 + z * (C4 + z * (C5 + z * C6)))));
        }

        /// <summary>Cody-Waite reduction to [-pi/4, pi/4]; returns the quadrant.</summary>
        static double Reduce(double x, out int quadrant)
        {
            double n = M.JsRound(x * INV_PIO2);
            quadrant = (int)((long)n & 3);
            return x - n * PIO2_1 - n * PIO2_2 - n * PIO2_3;
        }

        public static double Sin(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return double.NaN;
            if (x > -0.7853981633974483 && x < 0.7853981633974483) return KSin(x);
            double r = Reduce(x, out int q);
            switch (q)
            {
                case 0: return KSin(r);
                case 1: return KCos(r);
                case 2: return -KSin(r);
                default: return -KCos(r);
            }
        }

        public static double Cos(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return double.NaN;
            if (x > -0.7853981633974483 && x < 0.7853981633974483) return KCos(x);
            double r = Reduce(x, out int q);
            switch (q)
            {
                case 0: return KCos(r);
                case 1: return -KSin(r);
                case 2: return -KCos(r);
                default: return KSin(r);
            }
        }

        public static double Tan(double x) => Sin(x) / Cos(x);

        // --- atan / atan2 ------------------------------------------------------
        static readonly double[] ATANHI = { 4.63647609000806093515e-1, 7.85398163397448278999e-1, 9.82793723247329054082e-1, 1.57079632679489655800 };
        static readonly double[] ATANLO = { 2.26987774529616870924e-17, 3.06161699786838301793e-17, 1.39033110312309984516e-17, 6.12323399573676603587e-17 };
        const double AT0 = 3.33333333333329318027e-1;
        const double AT1 = -1.99999999998764832476e-1;
        const double AT2 = 1.42857142725034663711e-1;
        const double AT3 = -1.11111104054623557880e-1;
        const double AT4 = 9.09088713343650656196e-2;
        const double AT5 = -7.69187620504482999495e-2;
        const double AT6 = 6.66107313738753120669e-2;
        const double AT7 = -5.83357013379057348645e-2;
        const double AT8 = 4.97687799461593236017e-2;
        const double AT9 = -3.65315727442169155270e-2;
        const double AT10 = 1.62858201153657823623e-2;

        public static double Atan(double x)
        {
            if (double.IsNaN(x)) return double.NaN;
            bool neg = x < 0;
            double ax = neg ? -x : x;
            if (ax > 1e17) return neg ? -HALF_PI : HALF_PI;
            int id = -1;
            if (ax >= 0.4375)
            {
                if (ax < 1.1875)
                {
                    if (ax < 0.6875)
                    {
                        id = 0;
                        ax = (2 * ax - 1) / (2 + ax);
                    }
                    else
                    {
                        id = 1;
                        ax = (ax - 1) / (ax + 1);
                    }
                }
                else if (ax < 2.4375)
                {
                    id = 2;
                    ax = (ax - 1.5) / (1 + 1.5 * ax);
                }
                else
                {
                    id = 3;
                    ax = -1 / ax;
                }
            }
            double z = ax * ax;
            double w = z * z;
            double s1 = z * (AT0 + w * (AT2 + w * (AT4 + w * (AT6 + w * (AT8 + w * AT10)))));
            double s2 = w * (AT1 + w * (AT3 + w * (AT5 + w * (AT7 + w * AT9))));
            double r;
            if (id < 0) r = ax - ax * (s1 + s2);
            else r = ATANHI[id] - (ax * (s1 + s2) - ATANLO[id] - ax);
            return neg ? -r : r;
        }

        public static double Atan2(double y, double x)
        {
            if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
            // Signed zeros follow Math.atan2: atan2(-0, -1) = -PI, atan2(0, -0) = PI.
            bool yPos = y > 0 || (y == 0 && 1 / y > 0);
            if (x == 0)
            {
                if (y != 0) return yPos ? HALF_PI : -HALF_PI;
                return 1 / x > 0 ? y : yPos ? PI : -PI;
            }
            double a = Atan(y / x);
            if (x > 0) return a;
            return yPos ? a + PI : a - PI;
        }

        public static double Asin(double x)
        {
            if (x >= 1) return HALF_PI;
            if (x <= -1) return -HALF_PI;
            return Atan(x / Math.Sqrt(1 - x * x));
        }

        public static double Acos(double x) => HALF_PI - Asin(x);

        // --- exp / log / pow ---------------------------------------------------
        const double LN2_HI = 6.93147180369123816490e-1;
        const double LN2_LO = 1.90821492927058770002e-10;
        const double INV_LN2 = 1.44269504088896338700;
        const double P1 = 1.66666666666666019037e-1;
        const double P2 = -2.77777777770155933842e-3;
        const double P3 = 6.61375632143793436117e-5;
        const double P4 = -1.65339022054652515390e-6;
        const double P5 = 4.13813679705723846039e-8;

        public static double Exp(double x)
        {
            if (double.IsNaN(x)) return double.NaN;
            if (x > 709.7) return double.PositiveInfinity;
            if (x < -745) return 0;
            if (x > -3.7e-9 && x < 3.7e-9) return 1 + x;
            double k = M.JsRound(x * INV_LN2);
            double hi = x - k * LN2_HI;
            double lo = k * LN2_LO;
            double r = hi - lo;
            double t = r * r;
            double c = r - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
            double y = 1 - (lo - (r * c) / (2 - c) - hi);
            int ki = (int)k;
            if (ki < -1021) return y * Pow2i(ki + 1000) * Pow2i(-1000);
            return y * Pow2i(ki);
        }

        const double LG1 = 6.666666666666735130e-1;
        const double LG2 = 3.999999999940941908e-1;
        const double LG3 = 2.857142874366239149e-1;
        const double LG4 = 2.222219843214978396e-1;
        const double LG5 = 1.818357216161805012e-1;
        const double LG6 = 1.531383769920937332e-1;
        const double LG7 = 1.479819860511658591e-1;

        public static double Log(double x)
        {
            if (double.IsNaN(x) || x < 0) return double.NaN;
            if (x == 0) return double.NegativeInfinity;
            if (double.IsPositiveInfinity(x)) return double.PositiveInfinity;
            long bits = BitConverter.DoubleToInt64Bits(x);
            uint hx = (uint)(bits >> 32);
            uint lx = (uint)bits;
            int k = 0;
            if (hx < 0x00100000)
            {
                // Subnormal: scale up by 2^54.
                bits = BitConverter.DoubleToInt64Bits(x * 18014398509481984.0);
                hx = (uint)(bits >> 32);
                lx = (uint)bits;
                k = -54;
            }
            k += (int)(hx >> 20) - 1023;
            hx &= 0x000fffff;
            // Normalize the mantissa into [sqrt(2)/2, sqrt(2)).
            uint i = (hx + 0x95f64) & 0x100000;
            uint hi = hx | (i ^ 0x3ff00000);
            k += (int)(i >> 20);
            double f = BitConverter.Int64BitsToDouble(((long)hi << 32) | lx) - 1;
            double s = f / (2 + f);
            double z = s * s;
            double w = z * z;
            double t1 = w * (LG2 + w * (LG4 + w * LG6));
            double t2 = z * (LG1 + w * (LG3 + w * (LG5 + w * LG7)));
            double R = t2 + t1;
            double hfsq = 0.5 * f * f;
            return k * LN2_HI - (hfsq - (s * (hfsq + R) + k * LN2_LO) - f);
        }

        /// <summary>x^y. Exact for small integer y; otherwise exp(y log x), ~1e-15 relative.</summary>
        public static double Pow(double x, double y)
        {
            if (y == 0) return 1;
            if (y == 1) return x;
            if (y == 2) return x * x;
            if (y == 0.5) return Math.Sqrt(x);
            if (y == Math.Floor(y) && Math.Abs(y) <= 32)
            {
                double r = 1;
                double b = x;
                int e = (int)Math.Abs(y);
                while (e > 0)
                {
                    if ((e & 1) != 0) r *= b;
                    b *= b;
                    e >>= 1;
                }
                return y < 0 ? 1 / r : r;
            }
            if (x == 0) return y > 0 ? 0 : double.PositiveInfinity;
            if (x < 0) return double.NaN;
            return Exp(y * Log(x));
        }

        public static double Tanh(double x)
        {
            if (x > 20) return 1;
            if (x < -20) return -1;
            if (x > -1e-5 && x < 1e-5) return x;
            double e = Exp(2 * x);
            return (e - 1) / (e + 1);
        }

        public static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);
    }
}
