// Car specification: the authored data for one car. Everything the physics
// uses is derived from this (see VehicleParams.Build); performance numbers
// and PI are measured from it, never typed in. Ported from the web version's
// src/vehicles/spec.ts. Field names match the TypeScript.
using System.Collections.Generic;

namespace Halcyon.Core
{
    /// <summary>'compact' | 'hothatch' | 'classic' | 'muscle' | 'sports' | 'gt' | 'super' | 'hyper' | 'rally' | 'offroad' | 'truck' | 'suv'.</summary>
    public enum Category { Compact, HotHatch, Classic, Muscle, Sports, GT, Super, Hyper, Rally, OffRoad, Truck, SUV }

    /// <summary>'hatch' | 'coupe' | 'sedan' | 'wagon' | 'pickup' | 'suv' | 'midengine' | 'roadster'.</summary>
    public enum BodyType { Hatch, Coupe, Sedan, Wagon, Pickup, SUV, MidEngine, Roadster }

    /// <summary>'FWD' | 'RWD' | 'AWD'.</summary>
    public enum Drivetrain { FWD, RWD, AWD }

    /// <summary>'NA' | 'turbo' | 'twinturbo' | 'supercharged' | 'electric'.</summary>
    public enum Aspiration { NA, Turbo, TwinTurbo, Supercharged, Electric }

    /// <summary>'open' | 'lsd' | 'spool'.</summary>
    public enum DiffType { Open, LSD, Spool }

    public sealed class EngineSpec
    {
        /// <summary>Display layout, e.g. "I4", "V8", "flat-6", "dual motor".</summary>
        public string layout;
        /// <summary>Cylinder count; 0 for electric motors. Drives the engine sound.</summary>
        public int cylinders;
        /// <summary>Displacement in liters (0 for electric).</summary>
        public double displacement;
        public Aspiration aspiration;
        /// <summary>Full-throttle crank torque curve as (rpm, N·m) pairs, ascending rpm.</summary>
        public (double rpm, double nm)[] torque;
        public double idle;
        public double redline;
        /// <summary>Rev limiter cut-in.</summary>
        public double limiter;
        /// <summary>Engine plus flywheel inertia, kg·m².</summary>
        public double inertia;
        /// <summary>Turbo spool time constant, s (turbo cars only; null = default).</summary>
        public double? turboLag;
    }

    public sealed class DiffSpec
    {
        public DiffType type;
        /// <summary>Locking torque with no load, N·m.</summary>
        public double preload;
        /// <summary>Extra locking torque per N·m of drive torque (0..1).</summary>
        public double accel;
        /// <summary>Extra locking torque per N·m of engine braking (0..1).</summary>
        public double decel;
    }

    public sealed class CarVisual
    {
        /// <summary>Default paint, hex RGB.</summary>
        public int paint;
        /// <summary>Body style for the generator.</summary>
        public string style;
        /// <summary>Free-form generator parameters (the TypeScript's optional `params`).</summary>
        public Dictionary<string, double> Params;
    }

    public sealed class TireSpec
    {
        public Compound compound;
        /// <summary>Section widths, mm.</summary>
        public double widthF;
        public double widthR;
        /// <summary>Rolling radius, m.</summary>
        public double radius;
    }

    public sealed class BrakeSpec
    {
        /// <summary>Maximum brake torque per wheel, N·m.</summary>
        public double torqueF;
        public double torqueR;
    }

    public sealed class SuspensionSpec
    {
        /// <summary>Natural frequency, Hz.</summary>
        public double freqF;
        public double freqR;
        /// <summary>Damping ratio (fraction of critical) in bump; rebound is 1.6x.</summary>
        public double dampF;
        public double dampR;
        /// <summary>Anti-roll bar rate at the wheel, N/m.</summary>
        public double arbF;
        public double arbR;
        /// <summary>Total wheel travel, m.</summary>
        public double travel;
    }

    /// <summary>Drag area Cd*A and lift area Cl*A (m²); positive Cl*A is downforce.</summary>
    public sealed class AeroSpec
    {
        public double cdA;
        public double clA;
        /// <summary>Front share of the downforce (0..1).</summary>
        public double balance;
    }

    /// <summary>Maximum road-wheel steering angle (degrees) and steering ratio.</summary>
    public sealed class SteeringSpec
    {
        public double maxAngle;
        public double ratio;
    }

    public sealed class CarSpec
    {
        public string id;
        public string make;
        public string model;
        public int year;
        public Category category;
        public BodyType body;
        public string description;

        /// <summary>Curb mass plus driver, kg.</summary>
        public double mass;
        /// <summary>Fraction of static weight on the front axle.</summary>
        public double weightFront;
        public double wheelbase;
        public double trackFront;
        public double trackRear;
        /// <summary>Center of gravity height above the ground, m.</summary>
        public double cgHeight;
        public double length;
        public double width;
        public double height;
        public double groundClearance;
        /// <summary>Yaw inertia relative to a uniform box (mid-engine cars are lower). Null = 1.</summary>
        public double? yawInertiaScale;

        public EngineSpec engine;
        public Drivetrain drivetrain;
        /// <summary>AWD front torque share (0..1). Null = 0.4.</summary>
        public double? awdFront;
        public DiffSpec diff;
        /// <summary>Forward gear ratios, first to top.</summary>
        public double[] gears;
        public double final;
        public double reverse;
        /// <summary>Time to complete a shift, s.</summary>
        public double shiftTime;

        public TireSpec tires;
        /// <summary>Maximum brake torque per wheel, N·m.</summary>
        public BrakeSpec brakes;
        public SuspensionSpec suspension;
        public AeroSpec aero;
        public SteeringSpec steering;
        public CarVisual visual;
    }
}
