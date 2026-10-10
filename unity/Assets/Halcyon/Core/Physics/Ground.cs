// The ground interface the vehicle physics queries, with flat and sloped
// grounds for tests. Ported from the web version's src/world/ground.ts.
using System;

namespace Halcyon.Core
{
    public sealed class GroundHit
    {
        /// <summary>Surface height at the query point, m.</summary>
        public double y;
        /// <summary>Unit surface normal.</summary>
        public double nx, ny = 1, nz;
        public int surface = Halcyon.Core.Surface.Asphalt;
        /// <summary>Depth of standing water above the surface, m (0 when dry).</summary>
        public double water;

        public void CopyFrom(GroundHit o)
        {
            y = o.y; nx = o.nx; ny = o.ny; nz = o.nz; surface = o.surface; water = o.water;
        }
    }

    public interface IGround
    {
        /// <summary>
        /// The highest surface at (x, z) that lies no more than about 1.5 m above
        /// yRef, so a car under a bridge does not snap up onto the deck.
        /// Returns false where there is no ground at all (outside the world).
        /// </summary>
        bool Sample(double x, double z, double yRef, GroundHit hit);
    }

    public static class Bumps
    {
        /// <summary>Smooth deterministic value noise in [-1, 1] with unit wavelength.</summary>
        static double ValueNoise(double x, double z, uint seed)
        {
            double xf = Math.Floor(x);
            double zf = Math.Floor(z);
            int xi = Hash.ToInt32(xf);
            int zi = Hash.ToInt32(zf);
            double fx = x - xf;
            double fz = z - zf;
            double u = fx * fx * (3 - 2 * fx);
            double v = fz * fz * (3 - 2 * fz);
            double a = Hash.Hash2f(xi, zi, seed);
            double b = Hash.Hash2f(xi + 1, zi, seed);
            double c = Hash.Hash2f(xi, zi + 1, seed);
            double d = Hash.Hash2f(xi + 1, zi + 1, seed);
            return (a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v) * 2 - 1;
        }

        /// <summary>
        /// Add a surface's small-scale bumps to a hit, tilting the normal to match.
        /// The bumps are too small to model in the render mesh; they exist so loose
        /// surfaces feel rough through the suspension.
        /// </summary>
        public static void Apply(double x, double z, GroundHit hit)
        {
            SurfaceInfo s = Surfaces.All[hit.surface];
            if (s.bump <= 0) return;
            double k = 1 / s.bumpScale;
            uint seed = (uint)(0x51f3 + hit.surface * 977);
            const double e = 0.2;
            double h0 = ValueNoise(x * k, z * k, seed);
            double hx = ValueNoise((x + e) * k, z * k, seed);
            double hz = ValueNoise(x * k, (z + e) * k, seed);
            hit.y += h0 * s.bump;
            // Tilt the normal by the bump gradient.
            double gx = ((hx - h0) / e) * s.bump;
            double gz = ((hz - h0) / e) * s.bump;
            double nx = hit.nx - gx * hit.ny;
            double ny = hit.ny;
            double nz = hit.nz - gz * hit.ny;
            double l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            nx /= l;
            ny /= l;
            nz /= l;
            hit.nx = nx;
            hit.ny = ny;
            hit.nz = nz;
        }
    }

    /// <summary>Infinite flat ground at a fixed height, with a surface chosen per point.</summary>
    public sealed class FlatGround : IGround
    {
        public Func<double, double, int> SurfaceAt;
        public double Height;

        public FlatGround(Func<double, double, int> surfaceAt = null, double height = 0)
        {
            SurfaceAt = surfaceAt ?? ((x, z) => Halcyon.Core.Surface.Asphalt);
            Height = height;
        }

        public bool Sample(double x, double z, double yRef, GroundHit hit)
        {
            hit.y = Height;
            hit.nx = 0;
            hit.ny = 1;
            hit.nz = 0;
            hit.surface = SurfaceAt(x, z);
            hit.water = 0;
            Bumps.Apply(x, z, hit);
            return true;
        }
    }

    /// <summary>A tilted plane, for slope tests: rises along +X with the given grade.</summary>
    public sealed class SlopeGround : IGround
    {
        public double Grade;
        public int Surface;

        public SlopeGround(double grade, int surface = Halcyon.Core.Surface.Asphalt)
        {
            Grade = grade;
            Surface = surface;
        }

        public bool Sample(double x, double z, double yRef, GroundHit hit)
        {
            hit.y = x * Grade;
            double l = Math.Sqrt(1 + Grade * Grade);
            hit.nx = -Grade / l;
            hit.ny = 1 / l;
            hit.nz = 0;
            hit.surface = Surface;
            hit.water = 0;
            return true;
        }
    }
}
