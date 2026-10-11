// Tire model: a Pacejka "magic formula" curve on normalized combined slip,
// with a friction circle (ellipse), load sensitivity and per-surface grip.
// Ported from the web version's src/vehicles/tires.ts; results match it bit
// for bit (see dotnet/Halcyon.Core.Tests).
//
// Normalized slips: sx = kappa / kappaPeak, sy = tan(alpha) / tan(alphaPeak).
// Combined s = |(sx, sy)|. Total force F = mu * Fz * MF(s), split along
// (sx, sy) so the force always opposes the contact patch's slip velocity.
// MF peaks at s = 1 and falls toward `slide` (sliding / peak grip) as s grows.
using System;

namespace Halcyon.Core
{
    /// <summary>Tire compound ('eco' | 'street' | 'sport' | 'semislick' | 'slick' | 'rally' | 'offroad' | 'snow' | 'drag').</summary>
    public enum Compound { Eco, Street, Sport, SemiSlick, Slick, Rally, OffRoad, Snow, Drag }

    /// <summary>Grip multiplier per surface class (the TypeScript's Record&lt;GripClass, number&gt;).</summary>
    public sealed class GripTable
    {
        public double asphalt, wet, dirt, gravel, sand, grass, snow, ice, mud;

        public double this[GripClass g]
        {
            get
            {
                switch (g)
                {
                    case GripClass.Asphalt: return asphalt;
                    case GripClass.Wet: return wet;
                    case GripClass.Dirt: return dirt;
                    case GripClass.Gravel: return gravel;
                    case GripClass.Sand: return sand;
                    case GripClass.Grass: return grass;
                    case GripClass.Snow: return snow;
                    case GripClass.Ice: return ice;
                    case GripClass.Mud: return mud;
                    default: return double.NaN; // a missing key reads as undefined in the TypeScript
                }
            }
        }
    }

    public sealed class CompoundInfo
    {
        public string name;
        /// <summary>Peak lateral friction coefficient on dry asphalt at nominal load.</summary>
        public double mu;
        /// <summary>Longitudinal peak relative to lateral.</summary>
        public double muLong;
        /// <summary>Peak slip angle, radians.</summary>
        public double alphaPeak;
        /// <summary>Peak slip ratio.</summary>
        public double kappaPeak;
        /// <summary>Sliding grip as a fraction of peak grip.</summary>
        public double slide;
        /// <summary>Magic formula curvature E (higher = flatter after the peak).</summary>
        public double E;
        /// <summary>Load sensitivity: grip coefficient drops by this per +100% load.</summary>
        public double loadSens;
        /// <summary>Grip multiplier per surface class.</summary>
        public GripTable surface;
    }

    /// <summary>Magic formula coefficients normalized so the peak is at x = 1.</summary>
    public sealed class Curve
    {
        public double B, C, E;
    }

    /// <summary>Per-axle tire setup derived from compound, width and tuning.</summary>
    public sealed class TireSetup
    {
        public Compound compound;
        public CompoundInfo info;
        public Curve curve;
        /// <summary>Grip multiplier from tire width relative to load (wider = more grip).</summary>
        public double widthGrip;
        /// <summary>Grip multiplier from camber, pressure and other tuning.</summary>
        public double tuneGrip;
        /// <summary>Lateral-only multiplier from camber.</summary>
        public double tuneLateral;
        public double alphaPeak;
        /// <summary>tan(alphaPeak), precomputed.</summary>
        public double tanAlphaPeak;
        public double kappaPeak;
        /// <summary>Nominal (static) load used for load sensitivity, N.</summary>
        public double nominalLoad;
    }

    public sealed class TireResult
    {
        public double fx;
        public double fy;
        /// <summary>d(Fx)/d(slip velocity), for the implicit wheel-speed update. N per (m/s).</summary>
        public double dFxdV;
        /// <summary>Normalized combined slip (1 = peak).</summary>
        public double slip;
    }

    /// <summary>The functions and data of tires.ts (COMPOUNDS, makeCurve, mf, mfSlope, tireForce).</summary>
    public static class Tires
    {
        const double DEG = Math.PI / 180;

        static CompoundInfo C(string name, double mu, double muLong, double alphaPeak, double kappaPeak, double slide, double E, double loadSens,
            double asphalt, double wet, double dirt, double gravel, double sand, double grass, double snow, double ice, double mud)
        {
            return new CompoundInfo
            {
                name = name, mu = mu, muLong = muLong, alphaPeak = alphaPeak, kappaPeak = kappaPeak, slide = slide, E = E, loadSens = loadSens,
                surface = new GripTable { asphalt = asphalt, wet = wet, dirt = dirt, gravel = gravel, sand = sand, grass = grass, snow = snow, ice = ice, mud = mud },
            };
        }

        /// <summary>COMPOUNDS, indexed by (int)Compound. Use <see cref="Info"/>.</summary>
        public static readonly CompoundInfo[] Compounds =
        {
            //  name           mu    muLong alphaPeak  kappa slide E     loadSens   asphalt wet  dirt  gravel sand  grass snow  ice   mud
            C("Eco",          0.9,  1.12, 9 * DEG,   0.13, 0.8,  0.2,  0.14,      1, 0.74, 0.6,  0.56, 0.42, 0.5,  0.33, 0.12, 0.34),
            C("Street",       1.0,  1.16, 8 * DEG,   0.12, 0.8,  0.2,  0.13,      1, 0.74, 0.6,  0.56, 0.43, 0.5,  0.32, 0.11, 0.33),
            C("Sport",        1.1,  1.22, 7 * DEG,   0.11, 0.78, 0.15, 0.12,      1, 0.7,  0.56, 0.53, 0.4,  0.47, 0.29, 0.1,  0.31),
            C("Semi-slick",   1.22, 1.18, 6.5 * DEG, 0.1,  0.76, 0.1,  0.11,      1, 0.6,  0.5,  0.47, 0.36, 0.43, 0.24, 0.09, 0.27),
            C("Slick",        1.38, 1.14, 6 * DEG,   0.09, 0.74, 0.05, 0.1,       1, 0.45, 0.42, 0.4,  0.32, 0.38, 0.2,  0.07, 0.24),
            C("Rally",        0.98, 1.12, 9 * DEG,   0.16, 0.84, 0.35, 0.13,      1, 0.8,  0.86, 0.85, 0.62, 0.66, 0.5,  0.18, 0.52),
            C("Off-road",     0.9,  1.1,  10 * DEG,  0.18, 0.86, 0.4,  0.14,      1, 0.8,  0.9,  0.9,  0.78, 0.78, 0.56, 0.2,  0.74),
            C("Snow",         0.88, 1.12, 9.5 * DEG, 0.15, 0.85, 0.35, 0.13,      1, 0.82, 0.72, 0.7,  0.52, 0.62, 0.8,  0.34, 0.48),
            C("Drag radial",  0.95, 1.5,  8 * DEG,   0.12, 0.75, 0.1,  0.1,       1, 0.5,  0.45, 0.42, 0.34, 0.4,  0.2,  0.08, 0.25),
        };

        /// <summary>COMPOUNDS[compound].</summary>
        public static CompoundInfo Info(Compound c) => Compounds[(int)c];

        /// <summary>
        /// Build a normalized curve with the given sliding ratio and curvature.
        /// C follows from the asymptote sin(C*pi/2) = slide; B is solved so that
        /// the peak, where C*atan(phi(x)) = pi/2, lands at x = 1.
        /// </summary>
        public static Curve MakeCurve(double slide, double E)
        {
            double C = 2 - (2 / Math.PI) * DMath.Asin(JsMath.Min(0.999, JsMath.Max(0.3, slide)));
            double target = DMath.Tan(Math.PI / (2 * C));
            // Solve B(1 - E) + E*atan(B) = target with Newton's method.
            double B = target;
            for (int i = 0; i < 30; i++)
            {
                double f = B * (1 - E) + E * DMath.Atan(B) - target;
                double df = 1 - E + E / (1 + B * B);
                double nb = B - f / df;
                if (Math.Abs(nb - B) < 1e-12)
                {
                    B = nb;
                    break;
                }
                B = nb;
            }
            return new Curve { B = B, C = C, E = E };
        }

        public static double Mf(Curve c, double x)
        {
            double bx = c.B * x;
            double phi = bx - c.E * (bx - DMath.Atan(bx));
            return DMath.Sin(c.C * DMath.Atan(phi));
        }

        /// <summary>d MF / dx.</summary>
        public static double MfSlope(Curve c, double x)
        {
            double bx = c.B * x;
            double phi = bx - c.E * (bx - DMath.Atan(bx));
            double dphi = c.B * (1 - c.E + c.E / (1 + bx * bx));
            return DMath.Cos(c.C * DMath.Atan(phi)) * c.C * (dphi / (1 + phi * phi));
        }

        /// <summary>
        /// Tire force for one contact.
        /// vLong, vLat: contact patch velocity along the wheel's forward and right axes.
        /// wheelSpeed: omega * radius (m/s). Fz: load, N. surfaceGrip: multiplier.
        /// Writes into and returns <paramref name="result"/>; allocation-free.
        /// </summary>
        public static TireResult TireForce(TireSetup t, double Fz, double vLong, double vLat, double wheelSpeed, double surfaceGrip, TireResult result, double lateral = double.NaN)
        {
            if (Fz <= 0)
            {
                result.fx = 0;
                result.fy = 0;
                result.dFxdV = 0;
                result.slip = 0;
                return result;
            }
            // Below about 3 m/s slips use a floor speed. That keeps forces stable at
            // a standstill, where they behave like a stiff viscous friction.
            double vDen = JsMath.Max(Math.Abs(vLong), 3);
            double slipVx = wheelSpeed - vLong; // + when driving (wheel faster than ground)
            double kappa = slipVx / vDen;
            double tanA = vLat / vDen;
            double sx = kappa / t.kappaPeak;
            double sy = tanA / t.tanAlphaPeak;
            double s = Math.Sqrt(sx * sx + sy * sy);
            // Load sensitivity: the friction coefficient falls as load rises.
            double loadRatio = Fz / t.nominalLoad;
            double sens = JsMath.Max(0.6, JsMath.Min(1.25, 1 - t.info.loadSens * (loadRatio - 1)));
            double mu = t.info.mu * sens * surfaceGrip * t.widthGrip * t.tuneGrip;
            double muX = mu * t.info.muLong;
            double muY = mu * (double.IsNaN(lateral) ? t.tuneLateral : lateral);
            if (s < 1e-9)
            {
                double slope0 = MfSlope(t.curve, 0);
                result.fx = 0;
                result.fy = 0;
                result.dFxdV = (muX * Fz * slope0) / (t.kappaPeak * vDen);
                result.slip = 0;
                return result;
            }
            double m = Mf(t.curve, s);
            double ms = JsMath.Max(0, MfSlope(t.curve, s));
            double nx = sx / s;
            double ny = sy / s;
            result.fx = muX * Fz * m * nx;
            result.fy = -muY * Fz * m * ny;
            // dFx/dsx = muX*Fz*[ (m/s)*ny^2 + m'(s)*nx^2 ], and dsx/dslipVx = 1/(kappaPeak*vDen).
            result.dFxdV = (muX * Fz * ((m / s) * ny * ny + ms * nx * nx)) / (t.kappaPeak * vDen);
            result.slip = s;
            return result;
        }
    }
}
