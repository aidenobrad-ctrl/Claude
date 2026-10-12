// VehicleParams: the numbers the physics step uses, derived from a CarSpec
// plus tuning. Every tuning slider changes the physics through a formula in
// this file; docs/DESIGN.md lists them. Ported from the web version's
// src/vehicles/params.ts:
//   buildParams        -> VehicleParams.Build
//   defaultTune        -> Tune.Default
//   noUpgrades         -> Upgrades.None
//   curveTorque        -> EngineParams.CurveTorque
//   computeShiftPoints -> GearboxParams.ComputeShiftPoints
//   GRAVITY, AIR_DENSITY, camber/pressure grip formulas -> VehicleParams
using System;

namespace Halcyon.Core
{
    public sealed class WheelParams
    {
        /// <summary>Suspension hardpoint (top of travel) in the body frame.</summary>
        public V3 hardpoint;
        public double radius;
        /// <summary>Wheel, tire, hub and brake inertia, kg·m².</summary>
        public double inertia;
        public bool front;
        public bool left;
        public bool steerable;
        /// <summary>Share of drive torque this wheel receives through open differentials.</summary>
        public double driveWeight;
        /// <summary>Spring rate at the wheel, N/m.</summary>
        public double spring;
        /// <summary>Spring free length, m (from the hardpoint, along the strut).</summary>
        public double freeLength;
        /// <summary>Static length at design load, m.</summary>
        public double staticLength;
        /// <summary>Full droop length, m.</summary>
        public double maxLength;
        /// <summary>Bump stop engages below this length, m.</summary>
        public double bumpLength;
        public double damperBump;
        public double damperRebound;
        public double maxBrake;
        public bool handbrake;
        /// <summary>Static toe angle, rad (+ = toe-in).</summary>
        public double toe;
        /// <summary>Static camber, rad (negative = top leaning in).</summary>
        public double camber;
        /// <summary>Height above the contact point where tire forces act, m.</summary>
        public double rollCenter;
        public TireSetup tire;
    }

    public sealed class AxleParams
    {
        /// <summary>Wheel indices.</summary>
        public int left;
        public int right;
        public double arb;
        public DiffSpec diff;
    }

    public sealed class TurboParams
    {
        public double lag;
        public double share;
        public double spoolRpm;
        public double fullRpm;
    }

    public sealed class EngineParams
    {
        public double[] rpm;
        public double[] torque;
        public double idle;
        public double redline;
        public double limiter;
        public double inertia;
        public double peakTorque;
        public double peakTorqueRpm;
        public double peakPower;
        public double peakPowerRpm;
        /// <summary>Closed-throttle engine braking at the redline, N·m.</summary>
        public double brakeTorque;
        /// <summary>Null for non-turbo engines.</summary>
        public TurboParams turbo;
        public bool electric;
        public int cylinders;

        /// <summary>Engine torque at full throttle (before boost scaling) at the given rpm (curveTorque).</summary>
        public static double CurveTorque(EngineParams e, double rpm)
        {
            double x = rpm / 100;
            if (x <= 0) return e.torque[0];
            if (double.IsNaN(x)) return double.NaN; // the TypeScript indexes with NaN and gets NaN
            double i = Math.Floor(x);
            if (i >= e.rpm.Length - 1) return e.torque[e.rpm.Length - 1];
            double f = x - i;
            int k = (int)i;
            return e.torque[k] + (e.torque[k + 1] - e.torque[k]) * f;
        }
    }

    public sealed class GearboxParams
    {
        /// <summary>ratios[0] is reverse (negative), ratios[1..n] are forward gears.</summary>
        public double[] ratios;
        public double final;
        public double shiftTime;
        public double[] upshiftRpm;
        public double[] downshiftRpm;
        public double clutchMaxTorque;
        /// <summary>Engine speed the auto clutch holds while launching, rpm.</summary>
        public double launchRpm;
        public double efficiency;

        /// <summary>
        /// Upshift where the next gear gives more wheel torque at the same road
        /// speed (or just before the limiter). Downshift low enough that the lower
        /// gear lands safely below its own upshift point.
        /// </summary>
        public static void ComputeShiftPoints(EngineParams e, GearboxParams g)
        {
            int n = g.ratios.Length - 1;
            g.upshiftRpm = new double[n + 1];
            g.downshiftRpm = new double[n + 1];
            for (int i = 0; i <= n; i++) g.upshiftRpm[i] = e.limiter - 150;
            for (int gear = 1; gear < n; gear++)
            {
                double r0 = g.ratios[gear];
                double r1 = g.ratios[gear + 1];
                double up = e.limiter - 150;
                for (double rpm = e.peakTorqueRpm; rpm < e.limiter - 150; rpm += 50)
                {
                    double tNow = EngineParams.CurveTorque(e, rpm) * r0;
                    double tNext = EngineParams.CurveTorque(e, (rpm * r1) / r0) * r1;
                    if (tNext >= tNow)
                    {
                        up = rpm;
                        break;
                    }
                }
                g.upshiftRpm[gear] = JsMath.Max(up, JsMath.Min(e.redline * 0.85, e.limiter - 150));
            }
            for (int gear = 2; gear <= n; gear++)
            {
                g.downshiftRpm[gear] = (g.upshiftRpm[gear - 1] * 0.72 * g.ratios[gear]) / g.ratios[gear - 1];
            }
        }
    }

    /// <summary>Tuning layer. Every field has a documented effect in VehicleParams.Build.</summary>
    public sealed class Tune
    {
        /// <summary>Tire pressure offset from nominal, bar (−0.6 .. +0.6).</summary>
        public double pressureF;
        public double pressureR;
        /// <summary>Final drive multiplier (0.8 .. 1.25).</summary>
        public double finalDrive;
        /// <summary>Per-gear ratio multipliers (index 0 = first gear). Missing entries count as 1.</summary>
        public double[] gearScale;
        /// <summary>Camber, degrees (−4 .. +1).</summary>
        public double camberF;
        public double camberR;
        /// <summary>Toe, degrees (+ = toe-in, −1 .. +1).</summary>
        public double toeF;
        public double toeR;
        /// <summary>Caster, degrees (3 .. 8).</summary>
        public double caster;
        /// <summary>Anti-roll bar multipliers (0.2 .. 2.5).</summary>
        public double arbF;
        public double arbR;
        /// <summary>Spring natural frequency multipliers (0.6 .. 1.8).</summary>
        public double springF;
        public double springR;
        /// <summary>Ride height offset, m (−0.05 .. +0.08).</summary>
        public double rideHeight;
        /// <summary>Damping ratio multipliers (0.5 .. 2), bump and rebound.</summary>
        public double bumpF;
        public double bumpR;
        public double reboundF;
        public double reboundR;
        /// <summary>Aero downforce multipliers (0 .. 2), front and rear.</summary>
        public double aeroF;
        public double aeroR;
        /// <summary>Brake balance, front share (0.45 .. 0.75) and pressure multiplier (0.7 .. 1.3).</summary>
        public double brakeBalance;
        public double brakePressure;
        /// <summary>Differential locking: accel and decel (0..1), and AWD front split (0.1 .. 0.9).</summary>
        public double diffAccel;
        public double diffDecel;
        public double awdFront;

        /// <summary>defaultTune(spec).</summary>
        public static Tune Default(CarSpec spec)
        {
            double brakeBias = spec.brakes.torqueF / (spec.brakes.torqueF + spec.brakes.torqueR);
            var gearScale = new double[spec.gears.Length];
            for (int i = 0; i < gearScale.Length; i++) gearScale[i] = 1;
            return new Tune
            {
                pressureF = 0,
                pressureR = 0,
                finalDrive = 1,
                gearScale = gearScale,
                camberF = -1.5,
                camberR = -1.0,
                toeF = 0,
                toeR = 0.1,
                caster = 5.5,
                arbF = 1,
                arbR = 1,
                springF = 1,
                springR = 1,
                rideHeight = 0,
                bumpF = 1,
                bumpR = 1,
                reboundF = 1,
                reboundR = 1,
                aeroF = 1,
                aeroR = 1,
                brakeBalance = brakeBias,
                brakePressure = 1,
                diffAccel = spec.diff.accel,
                diffDecel = spec.diff.decel,
                awdFront = spec.awdFront ?? 0.4,
            };
        }

        public Tune Clone()
        {
            var t = (Tune)MemberwiseClone();
            t.gearScale = gearScale == null ? null : (double[])gearScale.Clone();
            return t;
        }
    }

    /// <summary>Upgrade layer (M5 fills this in). Multipliers default to 1.</summary>
    public sealed class Upgrades
    {
        public double powerScale = 1;
        public double massScale = 1;
        /// <summary>Tire compound override; null keeps the spec's compound.</summary>
        public Compound? compound;
        public double tireWidthAdd;
        public double brakeScale = 1;
        public double aeroKit;
        /// <summary>Extra downforce area added by an aero kit, m².</summary>
        public double clAAdd;

        /// <summary>noUpgrades().</summary>
        public static Upgrades None() => new Upgrades();

        public Upgrades Clone() => (Upgrades)MemberwiseClone();
    }

    public sealed class VehicleParams
    {
        public const double GRAVITY = 9.81;
        public const double AIR_DENSITY = 1.225;

        public CarSpec spec;
        public double mass;
        public double invMass;
        /// <summary>Body-frame principal inertia (pitch about X, yaw about Y, roll about Z).</summary>
        public V3 inertia;
        public V3 invInertia;
        public double wheelbase;
        public double cgHeight;
        /// <summary>Distance from the COM to the front and rear axles.</summary>
        public double a;
        public double b;
        /// <summary>Front-left, front-right, rear-left, rear-right.</summary>
        public WheelParams[] wheels;
        /// <summary>Front axle, rear axle.</summary>
        public AxleParams[] axles;
        public Drivetrain drivetrain;
        /// <summary>Center coupling locking torque (AWD), N·m per rad/s of axle speed difference.</summary>
        public double centerLock;
        public EngineParams engine;
        public GearboxParams gearbox;
        public AeroSpec aero;
        public double maxSteer;
        public double steerRatio;
        public double ackermann;
        /// <summary>Body collision half extents (body frame) and center offset from the COM.</summary>
        public V3 half;
        public V3 boxCenter;
        /// <summary>Points on the body that collide with the ground, body frame.</summary>
        public V3[] bodyPoints;
        /// <summary>Brake bias toward the front (0..1), already folded into maxBrake.</summary>
        public double brakeBias;
        /// <summary>
        /// Caster relative to the stock 5.5 degrees. Steering then leans each
        /// steered wheel: camber changes by +-casterDelta * sin(steer) degrees, the
        /// outer wheel into the turn (more negative), the inner one away from it.
        /// </summary>
        public double casterDelta;

        static double InterpTorque((double rpm, double nm)[] curve, double rpm)
        {
            if (rpm <= curve[0].rpm) return curve[0].nm;
            for (int i = 1; i < curve.Length; i++)
            {
                if (rpm <= curve[i].rpm)
                {
                    double r0 = curve[i - 1].rpm, t0 = curve[i - 1].nm;
                    double r1 = curve[i].rpm, t1 = curve[i].nm;
                    return t0 + ((t1 - t0) * (rpm - r0)) / (r1 - r0);
                }
            }
            return curve[curve.Length - 1].nm;
        }

        /// <summary>Camber grip formula (documented in DESIGN.md): best lateral grip near −2°.</summary>
        public static double CamberLateralGrip(double camberDeg)
        {
            double d = camberDeg + 2.0;
            return 1.03 - 0.012 * d * d;
        }

        public static double CamberLongGrip(double camberDeg) => 1 - 0.008 * Math.Abs(camberDeg);

        /// <summary>Pressure grip: peak at nominal, ±1 bar costs about 7%. Higher pressure sharpens response.</summary>
        public static double PressureGrip(double bar) => 1 - 0.07 * bar * bar;

        static bool Driven(CarSpec spec, bool front) =>
            spec.drivetrain == Drivetrain.AWD || (spec.drivetrain == Drivetrain.FWD ? front : !front);

        /// <summary>
        /// buildParams(spec, tune, up). Null tune and upgrades mean Tune.Default(spec)
        /// and Upgrades.None().
        /// </summary>
        public static VehicleParams Build(CarSpec spec, Tune tune = null, Upgrades up = null)
        {
            if (tune == null) tune = Tune.Default(spec);
            if (up == null) up = Upgrades.None();
            double mass = spec.mass * up.massScale;
            double wf = spec.weightFront;
            double L = spec.wheelbase;
            double a = L * (1 - wf);
            double b = L * wf;
            double cgHeight = spec.cgHeight + tune.rideHeight * 0.9;
            double r = spec.tires.radius;
            double W = spec.width;
            double H = spec.height;
            double len = spec.length;
            double yawScale = spec.yawInertiaScale ?? 1;
            // Effective dimensions: mass sits lower and more central than a uniform box.
            double hEff = H * 0.75;
            var inertia = new V3((mass / 12) * (hEff * hEff + len * len) * 0.88, (mass / 12) * (W * W + len * len) * 0.92 * yawScale, (mass / 12) * (W * W + hEff * hEff) * 0.9);

            Compound compound = up.compound ?? spec.tires.compound;
            CompoundInfo info = Tires.Info(compound);
            Curve curve = Tires.MakeCurve(info.slide, info.E);
            Curve looseCurve = Tires.LooseCurveFor(info);
            double staticF = (mass * GRAVITY * wf) / 2;
            double staticR = (mass * GRAVITY * (1 - wf)) / 2;
            TireSetup MkTire(double width, double load, double pressure, double camberDeg)
            {
                // Wider tires relative to the load they carry grip a little more.
                double loadPerMm = load / (width + up.tireWidthAdd);
                double widthGrip = JsMath.Max(0.9, JsMath.Min(1.12, 1 + 0.18 * (1 - loadPerMm / 15)));
                double alphaPeak = info.alphaPeak * (1 - 0.06 * pressure);
                return new TireSetup
                {
                    compound = compound,
                    info = info,
                    curve = curve,
                    looseCurve = looseCurve,
                    widthGrip = widthGrip,
                    tuneGrip = PressureGrip(pressure) * CamberLongGrip(camberDeg),
                    tuneLateral = CamberLateralGrip(camberDeg) / CamberLongGrip(camberDeg),
                    alphaPeak = alphaPeak,
                    tanAlphaPeak = DMath.Tan(alphaPeak),
                    kappaPeak = info.kappaPeak,
                    nominalLoad = load,
                };
            }
            TireSetup tireF = MkTire(spec.tires.widthF, staticF, tune.pressureF, tune.camberF);
            TireSetup tireR = MkTire(spec.tires.widthR, staticR, tune.pressureR, tune.camberR);

            double travel = spec.suspension.travel;
            const double deg = Math.PI / 180;
            WheelParams Corner(bool front, bool left)
            {
                double load = front ? staticF : staticR;
                double cornerMass = load / GRAVITY;
                double freq = (front ? spec.suspension.freqF * tune.springF : spec.suspension.freqR * tune.springR);
                double omegaN = 2 * Math.PI * freq;
                double k = cornerMass * omegaN * omegaN;
                double crit = 2 * Math.Sqrt(k * cornerMass);
                double zeta = front ? spec.suspension.dampF : spec.suspension.dampR;
                double staticBase = travel * 0.45;
                double staticLength = JsMath.Max(travel * 0.12, staticBase + tune.rideHeight);
                double track = front ? spec.trackFront : spec.trackRear;
                double hpY = r - spec.cgHeight + staticBase; // fixed in the body: ride height moves the COM instead
                bool driven = Driven(spec, front);
                double awdF = tune.awdFront;
                double driveWeight = !driven ? 0 : spec.drivetrain == Drivetrain.AWD ? (front ? awdF : 1 - awdF) / 2 : 0.5;
                double brakeTotal = (spec.brakes.torqueF + spec.brakes.torqueR) * up.brakeScale * tune.brakePressure;
                double maxBrake = front ? brakeTotal * tune.brakeBalance : brakeTotal * (1 - tune.brakeBalance);
                return new WheelParams
                {
                    hardpoint = new V3((left ? -1 : 1) * track / 2, hpY - (spec.cgHeight - cgHeight), front ? -a : b),
                    radius = r,
                    inertia = ((0.55 * (front ? spec.tires.widthF : spec.tires.widthR)) / 255) * (r / 0.33) * (r / 0.33) + 0.6,
                    front = front,
                    left = left,
                    steerable = front,
                    driveWeight = driveWeight,
                    spring = k,
                    freeLength = staticLength + load / k,
                    staticLength = staticLength,
                    maxLength = travel,
                    bumpLength = travel * 0.04,
                    damperBump = crit * zeta * (front ? tune.bumpF : tune.bumpR),
                    damperRebound = crit * zeta * 1.6 * (front ? tune.reboundF : tune.reboundR),
                    maxBrake = maxBrake,
                    handbrake = !front,
                    toe = (front ? tune.toeF : tune.toeR) * deg,
                    camber = (front ? tune.camberF : tune.camberR) * deg,
                    // Forces act at the contact patch so total load transfer is m*a*h/track (or /wheelbase).
                    rollCenter = 0,
                    tire = front ? tireF : tireR,
                };
            }
            var wheels = new[] { Corner(true, true), Corner(true, false), Corner(false, true), Corner(false, false) };
            DiffSpec Diff(bool front)
            {
                bool driven = Driven(spec, front);
                if (!driven) return new DiffSpec { type = DiffType.Open, preload = 0, accel = 0, decel = 0 };
                // AWD cars carry the spec diff at the rear and a milder one at the front.
                if (spec.drivetrain == Drivetrain.AWD && front)
                    return new DiffSpec { type = spec.diff.type == DiffType.Spool ? DiffType.LSD : spec.diff.type, preload = spec.diff.preload * 0.5, accel = tune.diffAccel * 0.5, decel = tune.diffDecel * 0.5 };
                return new DiffSpec { type = spec.diff.type, preload = spec.diff.preload, accel = tune.diffAccel, decel = tune.diffDecel };
            }
            var axles = new[]
            {
                new AxleParams { left = 0, right = 1, arb = spec.suspension.arbF * tune.arbF, diff = Diff(true) },
                new AxleParams { left = 2, right = 3, arb = spec.suspension.arbR * tune.arbR, diff = Diff(false) },
            };

            // Engine: sample the authored curve every 100 rpm for fast lookup.
            EngineSpec e = spec.engine;
            int n = (int)Math.Ceiling(e.limiter / 100) + 2;
            var rpmArr = new double[n];
            var torqueArr = new double[n];
            double peakTorque = 0;
            double peakTorqueRpm = 0;
            double peakPower = 0;
            double peakPowerRpm = 0;
            for (int i = 0; i < n; i++)
            {
                double rpm = i * 100;
                double t = InterpTorque(e.torque, JsMath.Max(rpm, e.idle * 0.5)) * up.powerScale;
                rpmArr[i] = rpm;
                torqueArr[i] = t;
                if (rpm <= e.limiter)
                {
                    if (t > peakTorque)
                    {
                        peakTorque = t;
                        peakTorqueRpm = rpm;
                    }
                    double p = (t * rpm * Math.PI) / 30;
                    if (p > peakPower)
                    {
                        peakPower = p;
                        peakPowerRpm = rpm;
                    }
                }
            }
            bool electric = e.aspiration == Aspiration.Electric;
            TurboParams turbo = e.aspiration == Aspiration.Turbo || e.aspiration == Aspiration.TwinTurbo
                ? new TurboParams { lag = e.turboLag ?? 0.4, share = e.aspiration == Aspiration.TwinTurbo ? 0.4 : 0.45, spoolRpm = e.idle * 1.4, fullRpm = JsMath.Max(e.idle * 2.6, peakTorqueRpm) }
                : null;
            var engine = new EngineParams
            {
                rpm = rpmArr,
                torque = torqueArr,
                idle = e.idle,
                redline = e.redline,
                limiter = e.limiter,
                inertia = e.inertia,
                peakTorque = peakTorque,
                peakTorqueRpm = peakTorqueRpm,
                peakPower = peakPower,
                peakPowerRpm = peakPowerRpm,
                brakeTorque = electric ? peakTorque * 0.12 : peakTorque * 0.2,
                turbo = turbo,
                electric = electric,
                cylinders = e.cylinders,
            };

            var ratios = new double[spec.gears.Length + 1];
            ratios[0] = -Math.Abs(spec.reverse);
            for (int i = 0; i < spec.gears.Length; i++)
            {
                double scale = tune.gearScale != null && i < tune.gearScale.Length ? tune.gearScale[i] : 1; // tune.gearScale[i] ?? 1
                ratios[i + 1] = spec.gears[i] * scale;
            }
            double final = spec.final * tune.finalDrive;
            var gearbox = new GearboxParams
            {
                ratios = ratios,
                final = final,
                shiftTime = spec.shiftTime,
                upshiftRpm = new double[0],
                downshiftRpm = new double[0],
                clutchMaxTorque = peakTorque * 1.6,
                // Like launch control or a torque converter's stall speed: the clutch
                // slips with the engine held in its torque band until the wheels catch up.
                launchRpm = electric ? 0 : M.Clamp(e.redline * 0.45, e.idle * 2.5, e.redline * 0.6),
                efficiency = spec.drivetrain == Drivetrain.AWD ? 0.86 : 0.9,
            };
            GearboxParams.ComputeShiftPoints(engine, gearbox);

            var half = new V3(W / 2, (H - spec.groundClearance) / 2, len / 2);
            var boxCenter = new V3(0, spec.groundClearance + (H - spec.groundClearance) / 2 - cgHeight, (b - a) / 2 * 0.15);
            double yBottom = spec.groundClearance - cgHeight + 0.02;
            double yTop = H - cgHeight - 0.08;
            double bx = W / 2 - 0.12;
            double bzF = -len / 2 + 0.2;
            double bzR = len / 2 - 0.2;
            var bodyPoints = new[]
            {
                new V3(-bx, yBottom, bzF), new V3(bx, yBottom, bzF), new V3(-bx, yBottom, bzR), new V3(bx, yBottom, bzR),
                new V3(-bx, yBottom, 0), new V3(bx, yBottom, 0),
                new V3(-bx * 0.8, yTop, -len * 0.12), new V3(bx * 0.8, yTop, -len * 0.12), new V3(-bx * 0.8, yTop, len * 0.2), new V3(bx * 0.8, yTop, len * 0.2),
                new V3(-bx, yTop * 0.4, bzF), new V3(bx, yTop * 0.4, bzF), new V3(-bx, yTop * 0.4, bzR), new V3(bx, yTop * 0.4, bzR),
            };

            double cl = spec.aero.clA + up.clAAdd;
            double fShare = spec.aero.balance * tune.aeroF;
            double rShare = (1 - spec.aero.balance) * tune.aeroR;
            double balance = cl == 0 ? spec.aero.balance : fShare / JsMath.Max(1e-6, fShare + rShare);

            return new VehicleParams
            {
                spec = spec,
                mass = mass,
                invMass = 1 / mass,
                inertia = inertia,
                invInertia = new V3(1 / inertia.x, 1 / inertia.y, 1 / inertia.z),
                wheelbase = L,
                cgHeight = cgHeight,
                a = a,
                b = b,
                wheels = wheels,
                axles = axles,
                drivetrain = spec.drivetrain,
                centerLock = spec.drivetrain == Drivetrain.AWD ? 18 : 0,
                engine = engine,
                gearbox = gearbox,
                aero = new AeroSpec
                {
                    cdA = spec.aero.cdA * (1 + 0.04 * up.aeroKit),
                    clA = spec.aero.clA + up.clAAdd,
                    balance = balance,
                },
                maxSteer = spec.steering.maxAngle * deg,
                steerRatio = spec.steering.ratio,
                ackermann = 0.6,
                half = half,
                boxCenter = boxCenter,
                bodyPoints = bodyPoints,
                brakeBias = tune.brakeBalance,
                casterDelta = tune.caster - 5.5,
            };
        }
    }
}
