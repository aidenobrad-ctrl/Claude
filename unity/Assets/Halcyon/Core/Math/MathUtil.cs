// Scalar helpers, vectors and quaternions for the simulation, ported from
// the web version's src/engine/math.ts. Doubles throughout, like JavaScript.
using System;

namespace Halcyon.Core
{
    public static class M
    {
        public const double KMH = 3.6;
        public const double MPH = 2.2369363;
        public const double G = 9.81;
        public const double DEG = Math.PI / 180;

        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
        public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        public static double Lerp(double a, double b, double t) => a + (b - a) * t;
        public static double InvLerp(double a, double b, double v) => b == a ? 0 : (v - a) / (b - a);
        public static double Remap(double v, double a0, double a1, double b0, double b1) => Lerp(b0, b1, Clamp01(InvLerp(a0, a1, v)));

        public static double Smoothstep(double e0, double e1, double x)
        {
            double t = Clamp01((x - e0) / (e1 - e0));
            return t * t * (3 - 2 * t);
        }

        /// <summary>JavaScript's sign: -1, 0 or 1 (NaN for NaN), as a double.</summary>
        public static double Sign(double v) => v > 0 ? 1 : v < 0 ? -1 : v;

        /// <summary>Wrap an angle to (-PI, PI].</summary>
        public static double WrapPi(double a)
        {
            a %= Math.PI * 2;
            if (a > Math.PI) a -= Math.PI * 2;
            else if (a <= -Math.PI) a += Math.PI * 2;
            return a;
        }

        public static double MoveTowards(double v, double target, double maxDelta) =>
            Math.Abs(target - v) <= maxDelta ? target : v + Sign(target - v) * maxDelta;

        /// <summary>Frame-rate independent exponential smoothing.</summary>
        public static double Damp(double current, double target, double lambda, double dt) =>
            Lerp(current, target, 1 - DMath.Exp(-lambda * dt));

        /// <summary>JavaScript's Math.round: halves round towards +infinity.</summary>
        public static double JsRound(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return x;
            double f = Math.Floor(x);
            return x - f >= 0.5 ? f + 1 : f;
        }

        /// <summary>JavaScript's Math.imul.</summary>
        public static int Imul(int a, int b) => unchecked(a * b);

        public static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }

    /// <summary>Mutable 3-vector (allocation-free simulation math).</summary>
    public sealed class V3
    {
        public double x, y, z;

        public V3() { }
        public V3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }

        public V3 Set(double x, double y, double z) { this.x = x; this.y = y; this.z = z; return this; }
        public V3 Copy(V3 v) { x = v.x; y = v.y; z = v.z; return this; }
        public V3 Clone() => new V3(x, y, z);
        public V3 Add(V3 v) { x += v.x; y += v.y; z += v.z; return this; }
        public V3 Sub(V3 v) { x -= v.x; y -= v.y; z -= v.z; return this; }
        public V3 SubVectors(V3 a, V3 b) { x = a.x - b.x; y = a.y - b.y; z = a.z - b.z; return this; }
        public V3 Scale(double s) { x *= s; y *= s; z *= s; return this; }
        public V3 AddScaled(V3 v, double s) { x += v.x * s; y += v.y * s; z += v.z * s; return this; }
        public double Dot(V3 v) => x * v.x + y * v.y + z * v.z;

        /// <summary>this = a x b (safe when this aliases a or b).</summary>
        public V3 CrossVectors(V3 a, V3 b)
        {
            double cx = a.y * b.z - a.z * b.y;
            double cy = a.z * b.x - a.x * b.z;
            double cz = a.x * b.y - a.y * b.x;
            x = cx; y = cy; z = cz;
            return this;
        }

        public double Len() => Math.Sqrt(x * x + y * y + z * z);
        public double LenSq() => x * x + y * y + z * z;

        public double DistTo(V3 v)
        {
            double dx = x - v.x, dy = y - v.y, dz = z - v.z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public V3 Normalize()
        {
            double l = Len();
            if (l > 1e-12) Scale(1 / l);
            return this;
        }

        public V3 Negate() { x = -x; y = -y; z = -z; return this; }
        public V3 Lerp(V3 v, double t) { x += (v.x - x) * t; y += (v.y - y) * t; z += (v.z - z) * t; return this; }

        /// <summary>Rotate by quaternion q.</summary>
        public V3 ApplyQuat(Quat q)
        {
            double vx = x, vy = y, vz = z;
            double qx = q.x, qy = q.y, qz = q.z, qw = q.w;
            double tx = 2 * (qy * vz - qz * vy);
            double ty = 2 * (qz * vx - qx * vz);
            double tz = 2 * (qx * vy - qy * vx);
            x = vx + qw * tx + qy * tz - qz * ty;
            y = vy + qw * ty + qz * tx - qx * tz;
            z = vz + qw * tz + qx * ty - qy * tx;
            return this;
        }

        /// <summary>Rotate by the inverse (conjugate) of unit quaternion q.</summary>
        public V3 ApplyQuatInv(Quat q)
        {
            double vx = x, vy = y, vz = z;
            double qx = -q.x, qy = -q.y, qz = -q.z, qw = q.w;
            double tx = 2 * (qy * vz - qz * vy);
            double ty = 2 * (qz * vx - qx * vz);
            double tz = 2 * (qx * vy - qy * vx);
            x = vx + qw * tx + qy * tz - qz * ty;
            y = vy + qw * ty + qz * tx - qx * tz;
            z = vz + qw * tz + qx * ty - qy * tx;
            return this;
        }

        public bool IsFinite() => M.IsFinite(x) && M.IsFinite(y) && M.IsFinite(z);
        public override string ToString() => $"({x}, {y}, {z})";
    }

    /// <summary>Mutable unit quaternion.</summary>
    public sealed class Quat
    {
        public double x, y, z, w = 1;

        public Quat() { }
        public Quat(double x, double y, double z, double w) { this.x = x; this.y = y; this.z = z; this.w = w; }

        public Quat Set(double x, double y, double z, double w) { this.x = x; this.y = y; this.z = z; this.w = w; return this; }
        public Quat Copy(Quat q) { x = q.x; y = q.y; z = q.z; w = q.w; return this; }
        public Quat Clone() => new Quat(x, y, z, w);
        public Quat Identity() => Set(0, 0, 0, 1);

        public Quat SetAxisAngle(V3 ax, double angle)
        {
            double h = angle / 2;
            double s = DMath.Sin(h);
            return Set(ax.x * s, ax.y * s, ax.z * s, DMath.Cos(h));
        }

        /// <summary>Rotation about +Y (heading). Heading 0 faces +Z.</summary>
        public Quat SetYaw(double yaw) => Set(0, DMath.Sin(yaw / 2), 0, DMath.Cos(yaw / 2));

        /// <summary>this = this * q</summary>
        public Quat Multiply(Quat q) => MultiplyQuats(this, q);

        /// <summary>this = q * this</summary>
        public Quat Premultiply(Quat q) => MultiplyQuats(q, this);

        public Quat MultiplyQuats(Quat a, Quat b)
        {
            double ax = a.x, ay = a.y, az = a.z, aw = a.w;
            double bx = b.x, by = b.y, bz = b.z, bw = b.w;
            x = ax * bw + aw * bx + ay * bz - az * by;
            y = ay * bw + aw * by + az * bx - ax * bz;
            z = az * bw + aw * bz + ax * by - ay * bx;
            w = aw * bw - ax * bx - ay * by - az * bz;
            return this;
        }

        public Quat Normalize()
        {
            double l = Math.Sqrt(x * x + y * y + z * z + w * w);
            if (l < 1e-12) return Identity();
            l = 1 / l;
            x *= l; y *= l; z *= l; w *= l;
            return this;
        }

        public Quat Conjugate() { x = -x; y = -y; z = -z; return this; }

        /// <summary>Integrate a world-space angular velocity over dt.</summary>
        public Quat Integrate(V3 av, double dt)
        {
            double hx = av.x * dt * 0.5;
            double hy = av.y * dt * 0.5;
            double hz = av.z * dt * 0.5;
            double qx = x, qy = y, qz = z, qw = w;
            x += hx * qw + hy * qz - hz * qy;
            y += hy * qw + hz * qx - hx * qz;
            z += hz * qw + hx * qy - hy * qx;
            w += -hx * qx - hy * qy - hz * qz;
            return Normalize();
        }

        public bool IsFinite() => M.IsFinite(x) && M.IsFinite(y) && M.IsFinite(z) && M.IsFinite(w);
    }
}
