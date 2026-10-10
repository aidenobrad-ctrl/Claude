// Driving surfaces, ported from the web version's src/world/surfaces.ts.
// Grip comes from the tire compound's table (Tires.cs); this file holds the
// properties that belong to the ground: rolling resistance, extra drag,
// small-scale bumps and effects.
namespace Halcyon.Core
{
    public static class Surface
    {
        public const int Asphalt = 0;
        public const int AsphaltWet = 1;
        public const int Concrete = 2;
        public const int Dirt = 3;
        public const int Gravel = 4;
        public const int Sand = 5;
        public const int Grass = 6;
        public const int Snow = 7;
        public const int Ice = 8;
        public const int Mud = 9;
        public const int Curb = 10;
        public const int Water = 11;
        public const int Rock = 12;
        public const int Count = 13;
    }

    /// <summary>Which column of a tire compound's grip table applies.</summary>
    public enum GripClass { Asphalt, Wet, Dirt, Gravel, Sand, Grass, Snow, Ice, Mud }

    public enum Particles { None, Dust, Gravel, Spray, Snow, Mud }

    public sealed class SurfaceInfo
    {
        public string name;
        public GripClass grip;
        /// <summary>Extra multiplier on the compound's grip for this surface (e.g. curbs).</summary>
        public double gripScale;
        /// <summary>Rolling resistance coefficient Crr (force = Crr * load).</summary>
        public double rolling;
        /// <summary>Viscous drag per wheel, N per (m/s) per kN of load (sand, mud, water).</summary>
        public double drag;
        /// <summary>Amplitude of deterministic small-scale bumps, m.</summary>
        public double bump;
        /// <summary>Wavelength of the bumps, m.</summary>
        public double bumpScale;
        public Particles particles;
        /// <summary>Whether sliding tires leave dark skid marks (vs. ruts or nothing).</summary>
        public bool skid;
        /// <summary>True for loose surfaces: grip on them recovers more gently past the peak.</summary>
        public bool loose;

        public SurfaceInfo(string name, GripClass grip, double gripScale, double rolling, double drag, double bump, double bumpScale, Particles particles, bool skid, bool loose)
        {
            this.name = name;
            this.grip = grip;
            this.gripScale = gripScale;
            this.rolling = rolling;
            this.drag = drag;
            this.bump = bump;
            this.bumpScale = bumpScale;
            this.particles = particles;
            this.skid = skid;
            this.loose = loose;
        }
    }

    public static class Surfaces
    {
        public static readonly SurfaceInfo[] All =
        {
            new SurfaceInfo("asphalt", GripClass.Asphalt, 1, 0.012, 0, 0, 1, Particles.None, true, false),
            new SurfaceInfo("wet asphalt", GripClass.Wet, 1, 0.014, 0.15, 0, 1, Particles.Spray, false, false),
            new SurfaceInfo("concrete", GripClass.Asphalt, 0.97, 0.011, 0, 0.002, 6, Particles.None, true, false),
            new SurfaceInfo("dirt", GripClass.Dirt, 1, 0.03, 0.2, 0.018, 1.6, Particles.Dust, false, true),
            new SurfaceInfo("gravel", GripClass.Gravel, 1, 0.035, 0.35, 0.022, 1.1, Particles.Gravel, false, true),
            new SurfaceInfo("sand", GripClass.Sand, 1, 0.09, 2.2, 0.012, 2.5, Particles.Dust, false, true),
            new SurfaceInfo("grass", GripClass.Grass, 1, 0.05, 0.6, 0.02, 1.8, Particles.Dust, false, true),
            new SurfaceInfo("snow", GripClass.Snow, 1, 0.045, 0.9, 0.012, 2, Particles.Snow, false, true),
            new SurfaceInfo("ice", GripClass.Ice, 1, 0.01, 0, 0, 1, Particles.None, false, false),
            new SurfaceInfo("mud", GripClass.Mud, 1, 0.11, 2.8, 0.025, 1.4, Particles.Mud, false, true),
            new SurfaceInfo("curb", GripClass.Asphalt, 0.9, 0.014, 0, 0.012, 0.45, Particles.None, true, false),
            new SurfaceInfo("water", GripClass.Wet, 0.55, 0.06, 9, 0.01, 3, Particles.Spray, false, false),
            new SurfaceInfo("rock", GripClass.Dirt, 1.05, 0.025, 0, 0.03, 0.9, Particles.Dust, false, false),
        };
    }
}
