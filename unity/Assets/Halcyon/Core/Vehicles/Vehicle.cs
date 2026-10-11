// Four-wheel vehicle model, stepped at 240 Hz (Loop.SIM_DT).
//
// Rigid body (6 DOF) on four raycast struts: springs, separate bump and
// rebound damping, bump stops and anti-roll bars. Per-wheel load, slip ratio
// and slip angle feed a combined-slip magic-formula tire (Tires.cs). The
// engine, clutch, gearbox and differentials are solved together implicitly,
// which keeps the stiff wheel-spin dynamics stable at any speed. ABS, TCS,
// stability control and a steering limiter are optional driver aids.
//
// Ported from the web version's src/vehicles/vehicle.ts. Every operation is
// in the same order with the same branches, so a C# run matches the
// TypeScript bit for bit (dotnet/Halcyon.Core.Tests proves it).
//
// Frame: the TypeScript (and three.js) frame, x east, y up, z south; the
// car's forward axis is -z in the body frame. Nothing here converts to
// Unity's frame; do that when rendering.
//
// Step() allocates nothing. Scratch vectors are per vehicle (the TypeScript
// shares them per module), so different vehicles may be stepped on different
// threads, but one vehicle must not be used from two threads at once.
using System;

namespace Halcyon.Core
{
    /// <summary>What the driver does with the controls, after filtering (PlayerControlFilter) or from an AI.</summary>
    public sealed class DriverInput
    {
        /// <summary>-1 full left .. +1 full right.</summary>
        public double steer;
        /// <summary>0..1. While in reverse, the throttle pedal brakes.</summary>
        public double throttle;
        /// <summary>0..1. Held at a standstill it selects reverse; in reverse it is the accelerator.</summary>
        public double brake;
        public double handbrake;
        /// <summary>0..1, 1 = pedal down (clutch disengaged).</summary>
        public double clutch;
        /// <summary>Manual gearbox shift requests; the vehicle acts on them every step they are true.</summary>
        public bool shiftUp;
        public bool shiftDown;

        /// <summary>neutralInput().</summary>
        public static DriverInput Neutral() => new DriverInput();

        public void CopyFrom(DriverInput o)
        {
            steer = o.steer; throttle = o.throttle; brake = o.brake; handbrake = o.handbrake; clutch = o.clutch;
            shiftUp = o.shiftUp; shiftDown = o.shiftDown;
        }
    }

    /// <summary>'off' | 'standard' | 'assisted'.</summary>
    public enum SteeringAssist { Off, Standard, Assisted }

    /// <summary>'auto' | 'manual'.</summary>
    public enum GearboxMode { Auto, Manual }

    public sealed class Aids
    {
        public bool abs;
        public bool tcs;
        public bool esc;
        public SteeringAssist steering;
        public GearboxMode gearbox;

        /// <summary>defaultAids(): everything on, standard steering, automatic gearbox.</summary>
        public static Aids Default() => new Aids { abs = true, tcs = true, esc = true, steering = SteeringAssist.Standard, gearbox = GearboxMode.Auto };

        public Aids Clone() => (Aids)MemberwiseClone();
    }

    /// <summary>Partial&lt;Aids&gt;: null fields leave the value they are applied over unchanged.</summary>
    public sealed class PartialAids
    {
        public bool? abs;
        public bool? tcs;
        public bool? esc;
        public SteeringAssist? steering;
        public GearboxMode? gearbox;

        /// <summary>Overwrite the fields that are set here ({ ...target, ...this }).</summary>
        public Aids ApplyTo(Aids target)
        {
            if (abs.HasValue) target.abs = abs.Value;
            if (tcs.HasValue) target.tcs = tcs.Value;
            if (esc.HasValue) target.esc = esc.Value;
            if (steering.HasValue) target.steering = steering.Value;
            if (gearbox.HasValue) target.gearbox = gearbox.Value;
            return target;
        }

        /// <summary>{ ...this, ...over }.</summary>
        public PartialAids Merge(PartialAids over)
        {
            var r = (PartialAids)MemberwiseClone();
            if (over == null) return r;
            if (over.abs.HasValue) r.abs = over.abs;
            if (over.tcs.HasValue) r.tcs = over.tcs;
            if (over.esc.HasValue) r.esc = over.esc;
            if (over.steering.HasValue) r.steering = over.steering;
            if (over.gearbox.HasValue) r.gearbox = over.gearbox;
            return r;
        }
    }

    /// <summary>
    /// Per-wheel simulation state. For rendering, a wheel's pivot sits at
    /// (hardpoint.x, hardpoint.y - length, hardpoint.z) in the body frame
    /// (see Vehicle.WheelLocalPosition), turned by <see cref="steer"/> about the
    /// body's up axis and rolled by <see cref="spin"/> about its axle.
    /// </summary>
    public sealed class WheelState
    {
        /// <summary>Road-wheel steering angle, rad, yaw sense (+ = left).</summary>
        public double steer;
        /// <summary>Strut length below the hardpoint, m (0 = fully compressed).</summary>
        public double length;
        public double prevLength;
        public bool grounded;
        /// <summary>Strut force before clamping, N.</summary>
        public double strutForce;
        /// <summary>Tire load, N.</summary>
        public double load;
        /// <summary>Wheel angular speed, rad/s (+ = rolling forward).</summary>
        public double omega;
        /// <summary>Accumulated roll angle, rad (wrapped into ±2π once it passes ±1e4).</summary>
        public double spin;
        /// <summary>Tire forces along the wheel's forward and side directions, N.</summary>
        public double fx;
        public double fy;
        public double slipRatio;
        /// <summary>Slip angle, rad.</summary>
        public double slipAngle;
        /// <summary>Contact-patch sliding speed, m/s (for smoke, marks, audio).</summary>
        public double slideSpeed;
        /// <summary>Normalized combined slip, 1 = peak grip.</summary>
        public double slip;
        /// <summary>Surface id under the tire (Surface.*).</summary>
        public int surface = Surface.Asphalt;
        public double water;
        /// <summary>Contact point (bottom of the tire), world.</summary>
        public readonly V3 contact = new V3();
        /// <summary>Ground normal at the contact, world.</summary>
        public readonly V3 normal = new V3(0, 1, 0);
        public readonly V3 fwdDir = new V3(0, 0, -1);
        public readonly V3 sideDir = new V3(1, 0, 0);
        /// <summary>Implicit tire stiffness d(Fx r)/d omega, N·m per rad/s.</summary>
        public double k;
        /// <summary>Tire reaction torque Fx * r, N·m.</summary>
        public double tireTorque;
        public double brakeTorque;
        public double absScale = 1;
        public double escBrake;
        public double vLong;
        /// <summary>Static-friction anchor for a braked tire at rest.</summary>
        public bool anchored;
        public double anchorX;
        public double anchorZ;
    }

    /// <summary>
    /// One car's simulation (the TypeScript Vehicle). Typical use:
    /// <code>
    /// var car = new Vehicle(VehicleParams.Build(Cars.ById("halden-aster-gt")));
    /// car.aids = Aids.Default();          // or set abs/tcs/esc/steering/gearbox
    /// car.Place(x, groundY, z, yaw);      // reset / teleport
    /// // every simulation step (Loop.SIM_DT):
    /// car.input = filter.Update(controls, car, Loop.SIM_DT); // or fill car.input directly (AI)
    /// car.Step(Loop.SIM_DT, ground);
    /// CarCollision.CollideWorld(car, colliders);              // then CollideCars for each pair
    /// // rendering: pos, quat, and per wheel length, steer, spin, contact, grounded
    /// </code>
    /// All state is in the web version's frame (x east, y up, z south; the body's
    /// forward axis is -z) and in SI units.
    /// </summary>
    public sealed class Vehicle
    {
        /// <summary>Center of mass, world.</summary>
        public readonly V3 pos = new V3();
        /// <summary>Body orientation.</summary>
        public readonly Quat quat = new Quat();
        public readonly V3 vel = new V3();
        /// <summary>Angular velocity, world, rad/s.</summary>
        public readonly V3 angVel = new V3();
        /// <summary>Body axes in world space, updated at the start of each step.</summary>
        public readonly V3 right = new V3(1, 0, 0);
        public readonly V3 up = new V3(0, 1, 0);
        public readonly V3 fwd = new V3(0, 0, -1);
        /// <summary>Front-left, front-right, rear-left, rear-right (same order as VehicleParams.wheels).</summary>
        public readonly WheelState[] wheels;
        /// <summary>The controls this step reads. May be replaced (e.g. by PlayerControlFilter.Out).</summary>
        public DriverInput input = DriverInput.Neutral();
        public Aids aids = Aids.Default();

        /// <summary>Engine speed, rad/s (see <see cref="Rpm"/>).</summary>
        public double engineOmega;
        /// <summary>-1 reverse, 0 neutral, 1..n forward.</summary>
        public int gear = 1;
        int nextGear = 1;
        /// <summary>Time left in the current gear change, s (0 when not shifting).</summary>
        public double shiftTimer;
        double shiftTotal;
        /// <summary>Clutch engagement, 0 (open) .. 1 (closed).</summary>
        public double clutchEngage = 1;
        /// <summary>True when the clutch did not slip this step.</summary>
        public bool clutchLocked;
        /// <summary>Turbo boost, 0 .. 1 (turbo engines only).</summary>
        public double boost;
        /// <summary>True while the rev limiter cuts the throttle.</summary>
        public bool limiterCut;
        /// <summary>Throttle the engine actually receives after aids, launch governor and shift cuts, 0 .. 1.</summary>
        public double throttleEff;
        /// <summary>Throttle cut by traction control and by stability control, 0 .. 1.</summary>
        public double tcsCut;
        public double escCut;
        /// <summary>Whether ABS, TCS and ESC intervened this step (HUD lights).</summary>
        public bool absActive;
        public bool tcsActive;
        public bool escActive;
        /// <summary>Steering angle of the road wheels before Ackermann and toe, rad (+ = left).</summary>
        public double steerAngle;
        double reverseTimer;
        /// <summary>Seconds since the last gear change started.</summary>
        public double timeSinceShift = 10;
        /// <summary>True while the clutch is slipping to launch the car.</summary>
        public bool launching;
        /// <summary>Launch control's trim on the traction cap, steered by the driven wheels' slip.</summary>
        public double launchTrim = 1;
        /// <summary>Gear being shifted into, for torque cut and rev matching.</summary>
        bool shiftUp;

        // Derived each step, for HUD, AI, audio and telemetry.
        /// <summary>|vel|, m/s.</summary>
        public double speed;
        /// <summary>Velocity along the body's forward and right axes, m/s (from the start of the step).</summary>
        public double vLong;
        public double vLat;
        /// <summary>Angular velocity about the body's up axis, rad/s (+ = turning left).</summary>
        public double yawRate;
        /// <summary>Filtered body-frame accelerations, m/s² (forward, right).</summary>
        public double accelLong;
        public double accelLat;
        /// <summary>Seconds with no wheel on the ground.</summary>
        public double airTime;
        /// <summary>Wheels touching the ground this step.</summary>
        public int groundedCount = 4;
        /// <summary>Largest collision impulse this step, N·s (for sound and haptics).</summary>
        public double impact;
        /// <summary>Extra drag multiplier from drafting (set by the race/sim layer).</summary>
        public double draftFactor = 1;
        /// <summary>How wet paved roads are, 0 dry to 1 soaked (set by the sim layer from the weather).</summary>
        public double wetness;
        /// <summary>Distance travelled, m.</summary>
        public double odometer;
        readonly V3 prevVel = new V3();
        readonly V3 frontAxle = new V3();

        // Scratch (module-level in the TypeScript).
        readonly V3 tmpA = new V3();
        readonly V3 tmpB = new V3();
        readonly V3 tmpC = new V3();
        readonly V3 hp = new V3();
        readonly V3 force = new V3();
        readonly V3 torque = new V3();
        readonly V3 bodyF = new V3();
        readonly TireResult tireRes = new TireResult();
        readonly GroundHit hit = new GroundHit();
        readonly double[] aCoef = new double[4];
        readonly double[] xTorque = new double[4];
        readonly double[] brakeSigns = new double[4];

        /// <summary>The parameters this vehicle simulates (the TypeScript's `params`). Change with SetParams.</summary>
        public VehicleParams Params { get; private set; }

        public Vehicle(VehicleParams p)
        {
            Params = p;
            wheels = new WheelState[p.wheels.Length];
            for (int i = 0; i < wheels.Length; i++) wheels[i] = new WheelState();
            Place(0, 0, 0, 0);
        }

        /// <summary>Engine speed in rpm.</summary>
        public double Rpm => (engineOmega * 30) / Math.PI;

        /// <summary>Number of forward gears.</summary>
        public int GearCount => Params.gearbox.ratios.Length - 1;

        /// <summary>Swap parameters (tuning, upgrades) keeping the current motion.</summary>
        public void SetParams(VehicleParams p)
        {
            Params = p;
            for (int i = 0; i < wheels.Length; i++) wheels[i].length = JsMath.Min(wheels[i].length, p.wheels[i].maxLength);
        }

        /// <summary>
        /// Put the car at rest on flat ground at (x, groundY, z) facing yaw
        /// (rotation about +Y; yaw 0 faces -Z). This is the reset/teleport.
        /// </summary>
        public void Place(double x, double groundY, double z, double yaw)
        {
            VehicleParams p = Params;
            quat.SetYaw(yaw);
            pos.Set(x, groundY + p.cgHeight + 0.01, z);
            vel.Set(0, 0, 0);
            angVel.Set(0, 0, 0);
            for (int i = 0; i < wheels.Length; i++)
            {
                WheelState w = wheels[i];
                w.omega = 0;
                w.length = w.prevLength = p.wheels[i].staticLength;
                w.absScale = 1;
                w.steer = 0;
            }
            gear = 1;
            nextGear = 1;
            shiftTimer = 0;
            engineOmega = (p.engine.idle * Math.PI) / 30;
            boost = 0;
            tcsCut = escCut = 0;
            launchTrim = 1;
            steerAngle = 0;
            airTime = 0;
            UpdateAxes();
            prevVel.Set(0, 0, 0);
        }

        /// <summary>Set a forward speed with wheels rolling and a sensible gear (tests, spawns).</summary>
        public void SetSpeed(double v)
        {
            VehicleParams p = Params;
            UpdateAxes();
            vel.Copy(fwd).Scale(v);
            for (int i = 0; i < wheels.Length; i++) wheels[i].omega = v / p.wheels[i].radius;
            GearboxParams g = p.gearbox;
            int gr = 1;
            double wheelOmega = v / p.wheels[0].radius;
            for (int n = 1; n < g.ratios.Length; n++)
            {
                double rpm = (wheelOmega * g.ratios[n] * g.final * 30) / Math.PI;
                gr = n;
                if (rpm < g.upshiftRpm[n] * 0.92) break;
            }
            gear = nextGear = gr;
            engineOmega = JsMath.Max((p.engine.idle * Math.PI) / 30, wheelOmega * g.ratios[gr] * g.final);
            prevVel.Copy(vel);
        }

        public void UpdateAxes()
        {
            right.Set(1, 0, 0).ApplyQuat(quat);
            up.Set(0, 1, 0).ApplyQuat(quat);
            fwd.Set(0, 0, -1).ApplyQuat(quat);
        }

        /// <summary>World position of a body-frame point.</summary>
        public V3 ToWorld(V3 local, V3 result) => result.Copy(local).ApplyQuat(quat).Add(pos);

        /// <summary>Velocity of a world-space point attached to the body.</summary>
        public V3 PointVelocity(V3 p, V3 result)
        {
            tmpC.SubVectors(p, pos);
            return result.CrossVectors(angVel, tmpC).Add(vel);
        }

        /// <summary>Apply an impulse (N·s) at a world point.</summary>
        public void ApplyImpulse(V3 point, V3 impulse)
        {
            VehicleParams p = Params;
            vel.AddScaled(impulse, p.invMass);
            tmpC.SubVectors(point, pos);
            tmpA.CrossVectors(tmpC, impulse);
            ApplyInvInertia(tmpA, tmpA);
            angVel.Add(tmpA);
        }

        /// <summary>result = I_world^-1 * v (result may alias v).</summary>
        public V3 ApplyInvInertia(V3 v, V3 result)
        {
            V3 inv = Params.invInertia;
            result.Copy(v).ApplyQuatInv(quat);
            result.Set(result.x * inv.x, result.y * inv.y, result.z * inv.z);
            return result.ApplyQuat(quat);
        }

        /// <summary>
        /// Body-frame position of wheel i's pivot (hub center) for rendering:
        /// (hardpoint.x, hardpoint.y - length, hardpoint.z). Not part of the TypeScript Vehicle.
        /// </summary>
        public V3 WheelLocalPosition(int i, V3 result)
        {
            V3 h = Params.wheels[i].hardpoint;
            return result.Set(h.x, h.y - wheels[i].length, h.z);
        }

        /// <summary>Advance the simulation by dt seconds (always Loop.SIM_DT in the game). Allocation-free.</summary>
        public void Step(double dt, IGround ground)
        {
            VehicleParams p = Params;
            UpdateAxes();
            vLong = vel.Dot(fwd);
            vLat = vel.Dot(right);
            yawRate = angVel.Dot(up);
            impact = 0;

            UpdateTransmission(dt);
            bool reversing = gear == -1;
            double throttleIn = M.Clamp(reversing ? input.brake : input.throttle, 0, 1);
            double brakeIn = M.Clamp(reversing ? input.throttle : input.brake, 0, 1);
            UpdateSteering(dt);

            // --- Suspension -----------------------------------------------------
            int grounded = 0;
            for (int i = 0; i < 4; i++)
            {
                if (Suspension(i, dt, ground)) grounded++;
            }
            groundedCount = grounded;
            AxleParams[] axles = p.axles;
            for (int ai = 0; ai < axles.Length; ai++)
            {
                AxleParams ax = axles[ai];
                WheelState L = wheels[ax.left];
                WheelState R = wheels[ax.right];
                double d = (p.wheels[ax.left].staticLength - L.length) - (p.wheels[ax.right].staticLength - R.length);
                double f = ax.arb * d;
                if (L.grounded) L.strutForce += f;
                if (R.grounded) R.strutForce -= f;
            }

            // --- Tires ----------------------------------------------------------
            for (int i = 0; i < 4; i++) Tire(i);

            // --- Driver aids ----------------------------------------------------
            UpdateAids(dt, throttleIn, brakeIn);

            // --- Drivetrain and wheel speeds -----------------------------------
            UpdateDrivetrain(dt, brakeIn);

            // --- Forces on the body ---------------------------------------------
            force.Set(0, -VehicleParams.GRAVITY * p.mass, 0);
            torque.Set(0, 0, 0);
            for (int i = 0; i < 4; i++)
            {
                WheelState w = wheels[i];
                if (!w.grounded) continue;
                // Strut force along the body's up axis, tire forces in the ground plane.
                tmpA.Copy(up).Scale(w.load).AddScaled(w.fwdDir, w.fx).AddScaled(w.sideDir, w.fy);
                // Loose and soft surfaces add drag proportional to speed and load.
                SurfaceInfo surf = Surfaces.All[w.surface];
                double drag = surf.drag + (w.water > 0.05 ? JsMath.Min(w.water, 0.8) * 25 : 0) + (wetness > 0 && surf.grip == GripClass.Asphalt ? wetness * Surfaces.All[Surface.AsphaltWet].drag : 0);
                if (drag > 0)
                {
                    PointVelocity(w.contact, tmpB);
                    tmpB.AddScaled(w.normal, -tmpB.Dot(w.normal));
                    tmpA.AddScaled(tmpB, (-drag * w.load) / 1000);
                }
                tmpB.Copy(w.contact).AddScaled(w.normal, p.wheels[i].rollCenter).Sub(pos);
                force.Add(tmpA);
                torque.Add(tmpC.CrossVectors(tmpB, tmpA));
            }
            // Aerodynamics: drag at the COM, downforce at each axle.
            double v2 = vel.LenSq();
            if (v2 > 1e-4)
            {
                double sp = Math.Sqrt(v2);
                double dragK = 0.5 * VehicleParams.AIR_DENSITY * p.aero.cdA * draftFactor * sp;
                force.AddScaled(vel, -dragK);
                if (p.aero.clA != 0 && vLong > 0)
                {
                    double down = 0.5 * VehicleParams.AIR_DENSITY * p.aero.clA * vLong * vLong;
                    ApplyBodyForce(0, -p.cgHeight * 0.3, -p.a, -down * p.aero.balance);
                    ApplyBodyForce(0, -p.cgHeight * 0.3, p.b, -down * (1 - p.aero.balance));
                }
            }
            BodyContacts(ground);

            // --- Integrate (semi-implicit Euler) -------------------------------
            prevVel.Copy(vel);
            vel.AddScaled(force, p.invMass * dt);
            // Angular: work in the body frame where inertia is diagonal.
            V3 inv = p.invInertia;
            V3 I = p.inertia;
            tmpA.Copy(torque).ApplyQuatInv(quat);
            tmpB.Copy(angVel).ApplyQuatInv(quat);
            double lx = I.x * tmpB.x;
            double ly = I.y * tmpB.y;
            double lz = I.z * tmpB.z;
            double gx = tmpB.y * lz - tmpB.z * ly;
            double gy = tmpB.z * lx - tmpB.x * lz;
            double gz = tmpB.x * ly - tmpB.y * lx;
            tmpB.x += (tmpA.x - gx) * inv.x * dt;
            tmpB.y += (tmpA.y - gy) * inv.y * dt;
            tmpB.z += (tmpA.z - gz) * inv.z * dt;
            angVel.Copy(tmpB.ApplyQuat(quat));
            // Gentle angular damping (air, chassis friction) keeps airborne spins sane.
            angVel.Scale(1 - 0.08 * dt);
            pos.AddScaled(vel, dt);
            quat.Integrate(angVel, dt);

            for (int i = 0; i < wheels.Length; i++)
            {
                WheelState w = wheels[i];
                w.spin += w.omega * dt;
                if (w.spin > 1e4 || w.spin < -1e4) w.spin %= Math.PI * 2;
            }
            speed = vel.Len();
            odometer += speed * dt;
            // Accelerations in the body frame, lightly filtered for the HUD and AI.
            tmpA.SubVectors(vel, prevVel).Scale(1 / dt);
            accelLong += (tmpA.Dot(fwd) - accelLong) * 0.1;
            accelLat += (tmpA.Dot(right) - accelLat) * 0.1;
            airTime = grounded == 0 ? airTime + dt : 0;
            timeSinceShift += dt;
        }

        /// <summary>Force along body Y at a body-frame point (used for downforce).</summary>
        void ApplyBodyForce(double lx, double ly, double lz, double fy)
        {
            tmpA.Set(lx, ly, lz).ApplyQuat(quat);
            tmpB.Copy(up).Scale(fy);
            force.Add(tmpB);
            torque.Add(tmpC.CrossVectors(tmpA, tmpB));
        }

        bool Suspension(int i, double dt, IGround ground)
        {
            VehicleParams p = Params;
            WheelParams wp = p.wheels[i];
            WheelState w = wheels[i];
            ToWorld(wp.hardpoint, hp);
            V3 u = up;
            w.prevLength = w.length;
            double length = wp.maxLength;
            bool isGrounded = false;
            if (ground.Sample(hp.x, hp.z, hp.y, hit))
            {
                double upN = u.x * hit.nx + u.y * hit.ny + u.z * hit.nz;
                if (upN > 0.25)
                {
                    // Ray from the hardpoint along -up to the ground plane, refined once
                    // at the contact point so slopes and curbs are hit where they are.
                    double t = ((hp.y - hit.y) * hit.ny) / upN;
                    double cx = hp.x - u.x * t;
                    double cz = hp.z - u.z * t;
                    if (ground.Sample(cx, cz, hp.y, hit))
                    {
                        upN = u.x * hit.nx + u.y * hit.ny + u.z * hit.nz;
                        if (upN > 0.25)
                        {
                            t = ((hp.x - cx) * hit.nx + (hp.y - hit.y) * hit.ny + (hp.z - cz) * hit.nz) / upN;
                            double l = t - wp.radius;
                            if (l < wp.maxLength)
                            {
                                isGrounded = true;
                                length = JsMath.Max(0, l);
                                w.normal.Set(hit.nx, hit.ny, hit.nz);
                                w.surface = hit.surface;
                                w.water = hit.water;
                            }
                        }
                    }
                }
            }
            w.length = length;
            w.grounded = isGrounded;
            if (!isGrounded)
            {
                w.strutForce = 0;
                w.load = 0;
                return false;
            }
            double compVel = (length - w.prevLength) / dt;
            double f = wp.spring * (wp.freeLength - length);
            f -= (compVel < 0 ? wp.damperBump : wp.damperRebound) * compVel;
            // Progressive bump stop.
            double bs = wp.bumpLength + 0.025;
            if (length < bs)
            {
                double d = bs - length;
                f += wp.spring * 12 * d + 3e6 * d * d - (compVel < 0 ? wp.damperBump * 2 * compVel : 0);
            }
            w.strutForce = f;
            // Contact point at the bottom of the wheel.
            w.contact.Copy(hp).AddScaled(u, -length).AddScaled(w.normal, -wp.radius);
            return true;
        }

        void Tire(int i)
        {
            VehicleParams p = Params;
            WheelParams wp = p.wheels[i];
            WheelState w = wheels[i];
            w.load = w.grounded ? JsMath.Max(0, w.strutForce) : 0;
            if (!w.grounded || w.load <= 0)
            {
                w.fx = w.fy = 0;
                w.k = 0;
                w.tireTorque = 0;
                w.slideSpeed = 0;
                w.slip = 0;
                w.slipRatio = 0;
                w.slipAngle = 0;
                return;
            }
            // Wheel heading: body forward rotated by the steer angle, projected onto the ground.
            double cs = DMath.Cos(w.steer);
            double sn = DMath.Sin(w.steer);
            V3 n = w.normal;
            w.fwdDir.Copy(fwd).Scale(cs).AddScaled(right, -sn);
            w.fwdDir.AddScaled(n, -w.fwdDir.Dot(n)).Normalize();
            w.sideDir.CrossVectors(w.fwdDir, n);
            PointVelocity(w.contact, tmpA);
            double vL = tmpA.Dot(w.fwdDir);
            double vS = tmpA.Dot(w.sideDir);
            w.vLong = vL;
            double wheelSpeed = w.omega * wp.radius;
            SurfaceInfo surf = Surfaces.All[w.surface];
            double grip = SurfaceGrip(wp.tire, surf);
            if (w.water > 0.02) grip *= JsMath.Max(0.35, 1 - w.water * 1.5);
            // Caster: a steered wheel leans as it turns, the outer one into the corner.
            double lateral = double.NaN;
            if (p.casterDelta != 0 && wp.steerable)
            {
                double deg = Math.PI / 180;
                double camber = wp.camber / deg;
                double lean = (wp.left ? 1 : -1) * p.casterDelta * DMath.Sin(w.steer);
                lateral = VehicleParams.CamberLateralGrip(camber + lean) / VehicleParams.CamberLongGrip(camber);
            }
            Tires.TireForce(wp.tire, w.load, vL, vS, wheelSpeed, grip, tireRes, lateral);
            w.fx = tireRes.fx;
            w.fy = tireRes.fy;
            // Static friction at a standstill. A braked tire whose patch has all but
            // stopped is anchored to the ground like a stiff tread spring (with
            // damping), so parked cars do not creep down slopes. If the needed force
            // exceeds the grip limit the anchor slides along with the patch.
            double patchSpeed = Math.Sqrt(vL * vL + vS * vS);
            if (patchSpeed < 0.3 && Math.Abs(wheelSpeed) < 0.05 && w.brakeTorque > 0 && speed < 0.6)
            {
                if (!w.anchored)
                {
                    w.anchored = true;
                    w.anchorX = w.contact.x;
                    w.anchorZ = w.contact.z;
                }
                double dx = w.contact.x - w.anchorX;
                double dz = w.contact.z - w.anchorZ;
                double dL = dx * w.fwdDir.x + dz * w.fwdDir.z;
                double dS = dx * w.sideDir.x + dz * w.sideDir.z;
                const double kS = 1.2e5;
                const double cS = 5000;
                double sxa = -kS * dL - cS * vL;
                double sya = -kS * dS - cS * vS;
                double limit = wp.tire.info.mu * grip * w.load;
                double sl = Math.Sqrt(sxa * sxa + sya * sya);
                if (sl > limit)
                {
                    sxa *= limit / sl;
                    sya *= limit / sl;
                    // Slide the anchor so the spring force equals the limit.
                    w.anchorX = w.contact.x + (w.fwdDir.x * sxa + w.sideDir.x * sya) / kS;
                    w.anchorZ = w.contact.z + (w.fwdDir.z * sxa + w.sideDir.z * sya) / kS;
                }
                w.fx = sxa;
                w.fy = sya;
                w.k = 0;
                w.tireTorque = 0;
            }
            else w.anchored = false;
            w.slip = tireRes.slip;
            w.k = wp.radius * wp.radius * tireRes.dFxdV;
            w.tireTorque = w.fx * wp.radius;
            double vDen = JsMath.Max(Math.Abs(vL), 3);
            w.slipRatio = (wheelSpeed - vL) / vDen;
            w.slipAngle = DMath.Atan2(vS, Math.Abs(vL) + 0.5);
            double sx = wheelSpeed - vL;
            w.slideSpeed = Math.Sqrt(sx * sx + vS * vS);
        }

        void UpdateAids(double dt, double throttleIn, double brakeIn)
        {
            VehicleParams p = Params;
            Aids a = aids;
            // Traction control: cut throttle when driven wheels slip past the peak.
            double kMax = 0;
            double kPeak = 0.12;
            for (int i = 0; i < 4; i++)
            {
                if (p.wheels[i].driveWeight <= 0 || !wheels[i].grounded) continue;
                kMax = JsMath.Max(kMax, gear == -1 ? -wheels[i].slipRatio : wheels[i].slipRatio);
                kPeak = p.wheels[i].tire.kappaPeak;
            }
            double tcsTarget = a.tcs && input.handbrake < 0.5 ? M.Clamp((kMax - kPeak) / (kPeak * 0.7), 0, 0.92) : 0;
            if (launching)
            {
                // While the clutch slips, the clutch cap does traction control's job.
                tcsCut += (0 - tcsCut) * JsMath.Min(1, dt * 20);
            }
            else
            {
                tcsCut += (tcsTarget - tcsCut) * JsMath.Min(1, dt * (tcsTarget > tcsCut ? 35 : 7));
            }
            tcsActive = tcsCut > 0.04 && throttleIn > 0.1;

            // Stability control.
            escCut = 0;
            for (int i = 0; i < wheels.Length; i++) wheels[i].escBrake = 0;
            escActive = false;
            if (a.esc && vLong > 6 && input.handbrake < 0.1)
            {
                double L = p.wheelbase;
                double v = vLong;
                double rt = (v * steerAngle) / (L * (1 + 0.0022 * v * v));
                double rMax = (0.85 * p.wheels[0].tire.info.mu * VehicleParams.GRAVITY) / v;
                rt = M.Clamp(rt, -rMax, rMax);
                double r = yawRate;
                double beta = DMath.Atan2(vLat, JsMath.Max(1, v));
                double excess = Math.Abs(r) - Math.Abs(rt) - 0.07;
                if (excess > 0 || (M.Sign(r) != M.Sign(rt) && Math.Abs(r) > 0.12))
                {
                    double e = JsMath.Max(excess, Math.Abs(r) * 0.5);
                    int outerFront = r > 0 ? 1 : 0;
                    wheels[outerFront].escBrake = M.Clamp(e * 9000, 0, p.wheels[outerFront].maxBrake * 0.6);
                    escCut = M.Clamp(e * 2.2, 0, 0.75);
                }
                double absBeta = Math.Abs(beta);
                if (absBeta > 0.1) escCut = JsMath.Max(escCut, M.Clamp((absBeta - 0.1) * 4, 0, 0.85));
                escActive = escCut > 0.05;
            }

            // ABS: release brake pressure on wheels that lock past the peak slip.
            absActive = false;
            for (int i = 0; i < 4; i++)
            {
                WheelState w = wheels[i];
                double target = 1;
                if (a.abs && brakeIn > 0.05 && w.grounded && Math.Abs(w.vLong) > 1.5)
                {
                    double kp = p.wheels[i].tire.kappaPeak;
                    double lockSlip = w.vLong > 0 ? -w.slipRatio : w.slipRatio;
                    target = M.Clamp(1 - (lockSlip - kp * 1.05) / (kp * 0.9), 0.05, 1);
                }
                w.absScale += (target - w.absScale) * JsMath.Min(1, dt * (target < w.absScale ? 50 : 25));
                if (w.absScale < 0.93) absActive = true;
            }

            double cut = JsMath.Max(tcsCut, escCut);
            throttleEff = throttleIn * (1 - cut);
            // Launch governor: hold the engine near the launch rpm while the clutch slips.
            if (launching) throttleEff *= M.Clamp(1 - (Rpm - p.gearbox.launchRpm) / 350, 0, 1);
            if (shiftTimer > 0)
            {
                if (shiftUp)
                {
                    // Ignition cut on upshifts so the engine drops to the new gear's speed.
                    throttleEff = 0;
                }
                else if (nextGear >= 1)
                {
                    // Blip on downshifts to match the lower gear's revs.
                    double target = WheelRpm(nextGear);
                    throttleEff = M.Clamp((target - Rpm) / 600, 0, 1);
                }
            }
            // Brake torque per wheel.
            for (int i = 0; i < 4; i++)
            {
                WheelParams wp = p.wheels[i];
                WheelState w = wheels[i];
                double tb = brakeIn * wp.maxBrake * w.absScale + w.escBrake;
                if (wp.handbrake && input.handbrake > 0) tb = JsMath.Max(tb, input.handbrake * wp.maxBrake * 2.2);
                w.brakeTorque = tb;
            }
        }

        double EngineTorque(double rpm, double throttle, double dt)
        {
            EngineParams e = Params.engine;
            if (rpm >= e.limiter) limiterCut = true;
            else if (rpm < e.limiter - (e.electric ? 20 : 140)) limiterCut = false;
            double thr = limiterCut ? 0 : throttle;
            if (e.electric && rpm > e.limiter - 300) thr *= M.Clamp((e.limiter - rpm) / 300, 0, 1);
            double mult = 1;
            if (e.turbo != null)
            {
                double spool = M.Smoothstep(e.turbo.spoolRpm, e.turbo.fullRpm, rpm);
                double target = thr * spool;
                double tau = target > boost ? e.turbo.lag : 0.3;
                boost += (target - boost) * JsMath.Min(1, dt / tau);
                mult = 1 - e.turbo.share + e.turbo.share * boost;
            }
            double full = EngineParams.CurveTorque(e, rpm) * mult;
            double brakeT = e.brakeTorque * M.Clamp(rpm / e.redline, 0.12, 1.2);
            double T = thr * full - (1 - thr) * brakeT;
            if (!e.electric && rpm < e.idle) T += M.Clamp((e.idle - rpm) * 0.9, 0, e.peakTorque * 0.4);
            if (rpm < 0) T = JsMath.Max(T, 0);
            return T;
        }

        /// <summary>The TypeScript's drivetrain(): engine, clutch, gearbox, differentials and brakes, solved implicitly.</summary>
        void UpdateDrivetrain(double dt, double brakeIn)
        {
            VehicleParams p = Params;
            GearboxParams g = p.gearbox;
            EngineParams e = p.engine;
            WheelState[] W = wheels;
            double rpm = Rpm;
            double Te = EngineTorque(rpm, throttleEff, dt);
            // G is the kinematic ratio (engine speed per wheel speed); Gt = G * efficiency
            // is the torque ratio, so gearbox losses apply to whatever the clutch passes.
            double G = gear == 0 ? 0 : g.ratios[gear == -1 ? 0 : gear] * g.final;
            double Gt = G * g.efficiency;
            bool engaged = G != 0 && clutchEngage > 0.001;

            // Per-wheel torques opposing rotation: tire reaction, rolling
            // resistance and brakes. Brakes and rolling resistance act as friction.
            double A = 0;
            double B = 0;
            double wSum = 0;
            double[] brakeSign = brakeSigns;
            for (int i = 0; i < 4; i++)
            {
                WheelState w = W[i];
                WheelParams wp = p.wheels[i];
                double a = 1 / (wp.inertia + dt * w.k);
                aCoef[i] = a;
                double rr = Surfaces.All[w.surface].rolling * w.load * wp.radius;
                double fr = rr + w.brakeTorque;
                double x = w.tireTorque;
                if (Math.Abs(w.omega) > 0.05)
                {
                    brakeSign[i] = M.Sign(w.omega);
                    x += brakeSign[i] * fr;
                }
                else
                {
                    brakeSign[i] = 0;
                }
                xTorque[i] = x;
                double dw = wp.driveWeight;
                if (dw > 0)
                {
                    A += dw * dw * a;
                    B += dw * a * x;
                    wSum += dw * w.omega;
                }
            }
            double Tc = 0;
            clutchLocked = false;
            double Ie = e.inertia;
            if (engaged)
            {
                double wIn = G * wSum;
                double Tlock = (engineOmega - wIn + dt * (Te / Ie + G * B)) / (dt * (1 / Ie + G * Gt * A));
                double cap = clutchEngage * g.clutchMaxTorque;
                if (Math.Abs(Tlock) <= cap)
                {
                    Tc = Tlock;
                    clutchLocked = true;
                }
                else
                {
                    Tc = M.Sign(Tlock) * cap;
                }
            }
            engineOmega += (dt * (Te - Tc)) / Ie;
            for (int i = 0; i < 4; i++)
            {
                WheelState w = W[i];
                double drive = Tc * Gt * p.wheels[i].driveWeight;
                w.omega += dt * aCoef[i] * (drive - xTorque[i]);
            }
            // Limited-slip differentials: transfer torque from the faster wheel to
            // the slower one, up to the locking torque.
            AxleParams[] axles = p.axles;
            for (int ai = 0; ai < axles.Length; ai++)
            {
                AxleParams ax = axles[ai];
                DiffSpec d = ax.diff;
                if (d.type == DiffType.Open || p.wheels[ax.left].driveWeight <= 0) continue;
                int iL = ax.left;
                int iR = ax.right;
                double delta = W[iL].omega - W[iR].omega;
                double tIn = Tc * Gt * (p.wheels[iL].driveWeight + p.wheels[iR].driveWeight);
                double lockMax = d.type == DiffType.Spool ? 1e9 : d.preload + (tIn >= 0 ? d.accel : d.decel) * Math.Abs(tIn);
                double tEq = delta / (dt * (aCoef[iL] + aCoef[iR]));
                double t = M.Clamp(tEq, -lockMax, lockMax);
                W[iL].omega -= dt * aCoef[iL] * t;
                W[iR].omega += dt * aCoef[iR] * t;
            }
            // AWD center coupling: viscous, limited so it never overshoots.
            if (p.centerLock > 0 && engaged)
            {
                double front = (W[0].omega + W[1].omega) / 2;
                double rear = (W[2].omega + W[3].omega) / 2;
                double delta = front - rear;
                double aF = (aCoef[0] + aCoef[1]) / 2;
                double aR = (aCoef[2] + aCoef[3]) / 2;
                double tMax = Math.Abs(delta) / (dt * (aF + aR));
                double t = M.Clamp(p.centerLock * delta * 40, -tMax, tMax);
                for (int i = 0; i < 2; i++) W[i].omega -= dt * aCoef[i] * t * 0.5;
                for (int i = 2; i < 4; i++) W[i].omega += dt * aCoef[i] * t * 0.5;
            }
            // Brakes are friction: they stop a wheel but never spin it backwards.
            for (int i = 0; i < 4; i++)
            {
                WheelState w = W[i];
                if (brakeSign[i] != 0)
                {
                    if (M.Sign(w.omega) != brakeSign[i]) w.omega = 0;
                }
                else if (w.brakeTorque > 0)
                {
                    double dw = dt * aCoef[i] * w.brakeTorque;
                    if (Math.Abs(w.omega) <= dw) w.omega = 0;
                    else w.omega -= M.Sign(w.omega) * dw;
                }
            }
            if (clutchLocked)
            {
                double s = 0;
                for (int i = 0; i < 4; i++) s += p.wheels[i].driveWeight * W[i].omega;
                engineOmega = G * s;
            }
            double minOmega = e.electric ? -1e9 : (e.idle * 0.45 * Math.PI) / 30;
            double maxOmega = (e.limiter * 1.06 * Math.PI) / 30;
            if (!e.electric) engineOmega = M.Clamp(engineOmega, minOmega, maxOmega);
            else engineOmega = M.Clamp(engineOmega, -maxOmega, maxOmega);
        }

        void ShiftTo(int g)
        {
            if (g == gear && shiftTimer <= 0) return;
            shiftUp = g > gear && gear >= 1;
            nextGear = g;
            shiftTotal = Params.gearbox.shiftTime;
            shiftTimer = shiftTotal;
            timeSinceShift = 0;
        }

        /// <summary>
        /// Clutch torque the driven tires can put down at peak longitudinal grip,
        /// from their current loads and surfaces (used by launch control).
        /// </summary>
        public double TractionTorque()
        {
            VehicleParams p = Params;
            GearboxParams g = p.gearbox;
            double G = Math.Abs(g.ratios[gear == -1 ? 0 : Math.Max(1, gear)] * g.final);
            double wheelTorque = 0;
            for (int i = 0; i < 4; i++)
            {
                WheelParams wp = p.wheels[i];
                if (wp.driveWeight <= 0) continue;
                WheelState w = wheels[i];
                TireSetup t = wp.tire;
                SurfaceInfo surf = Surfaces.All[w.surface];
                // The tire's own peak, as in TireForce: load sensitivity (a squatting
                // car's rear tires grip less per newton) and standing water.
                double sens = JsMath.Max(0.6, JsMath.Min(1.25, 1 - t.info.loadSens * (w.load / t.nominalLoad - 1)));
                double grip = SurfaceGrip(t, surf);
                if (w.water > 0.02) grip *= JsMath.Max(0.35, 1 - w.water * 1.5);
                double mu = t.info.mu * sens * t.info.muLong * t.widthGrip * t.tuneGrip * grip;
                // Limited by the driven wheel with the least grip relative to its torque share.
                double perWheel = (mu * w.load * wp.radius) / wp.driveWeight;
                wheelTorque = wheelTorque == 0 ? perWheel : JsMath.Min(wheelTorque, perWheel);
            }
            return wheelTorque / JsMath.Max(1e-6, G * g.efficiency);
        }

        /// <summary>A tire's grip factor on a surface; paved surfaces grip like wet asphalt in proportion to the road's wetness.</summary>
        double SurfaceGrip(TireSetup t, SurfaceInfo surf)
        {
            double grip = t.info.surface[surf.grip] * surf.gripScale;
            if (wetness > 0 && surf.grip == GripClass.Asphalt) grip += (t.info.surface.wet - t.info.surface.asphalt) * surf.gripScale * wetness;
            return grip;
        }

        /// <summary>Engine rpm the driven wheels imply in the given gear.</summary>
        public double WheelRpm(int g)
        {
            VehicleParams p = Params;
            double s = 0;
            for (int i = 0; i < 4; i++) s += p.wheels[i].driveWeight * wheels[i].omega;
            double ratio = p.gearbox.ratios[g == -1 ? 0 : g] * p.gearbox.final;
            return (Math.Abs(s * ratio) * 30) / Math.PI;
        }

        void UpdateTransmission(double dt)
        {
            VehicleParams p = Params;
            GearboxParams g = p.gearbox;
            EngineParams e = p.engine;
            DriverInput inp = input;
            int n = GearCount;

            // Reverse: hold the brake at a standstill; throttle returns to first.
            if (gear >= 1 && shiftTimer <= 0)
            {
                if (inp.brake > 0.4 && inp.throttle < 0.05 && vLong < 0.5)
                {
                    reverseTimer += dt;
                    if (reverseTimer > 0.35)
                    {
                        ShiftTo(-1);
                        reverseTimer = 0;
                    }
                }
                else reverseTimer = 0;
            }
            else if (gear == -1 && shiftTimer <= 0)
            {
                if (inp.throttle > 0.2 && inp.brake < 0.1 && vLong > -0.6) ShiftTo(1);
            }

            if (shiftTimer > 0)
            {
                shiftTimer -= dt;
                if (shiftTimer <= shiftTotal * 0.5 && gear != nextGear) gear = nextGear;
                if (shiftTimer <= 0)
                {
                    shiftTimer = 0;
                    gear = nextGear;
                }
            }
            else if (gear >= 1)
            {
                if (aids.gearbox == GearboxMode.Auto)
                {
                    double wheelRpm = WheelRpm(gear);
                    bool slipping = tcsCut > 0.3;
                    for (int i = 0; i < wheels.Length && !slipping; i++)
                    {
                        if (p.wheels[i].driveWeight > 0 && wheels[i].slipRatio > 0.35) slipping = true;
                    }
                    if (timeSinceShift > 0.4 && gear < n && wheelRpm > g.upshiftRpm[gear] && inp.throttle > 0.05 && !slipping)
                    {
                        ShiftTo(gear + 1);
                    }
                    else if (timeSinceShift > 0.6 && gear > 1)
                    {
                        double thr = inp.throttle;
                        double downAt = g.downshiftRpm[gear] * (thr > 0.6 ? 1.1 : thr > 0.15 ? 0.85 : inp.brake > 0.3 ? 1 : 0.7);
                        if (wheelRpm < downAt) ShiftTo(gear - 1);
                    }
                }
                else
                {
                    if (inp.shiftUp && gear < n) ShiftTo(gear + 1);
                    if (inp.shiftDown && gear > 1)
                    {
                        // Refuse a downshift that would over-rev the engine.
                        if (WheelRpm(gear - 1) < e.limiter * 0.98) ShiftTo(gear - 1);
                    }
                }
            }

            // Clutch engagement.
            double engage = 1;
            if (shiftTimer > 0)
            {
                double t = 1 - shiftTimer / shiftTotal;
                engage = t < 0.5 ? 0 : (t - 0.5) * 2;
            }
            launching = false;
            if (!e.electric && gear != 0 && shiftTimer <= 0)
            {
                double rpm = Rpm;
                double pedal = gear == -1 ? inp.brake : inp.throttle;
                if (pedal > 0.05)
                {
                    if (clutchLocked && rpm > e.idle + 150)
                    {
                        engage = 1;
                    }
                    else
                    {
                        // Launch, like launch control: a throttle governor holds the
                        // launch rpm (see UpdateAids) and the slipping clutch passes the
                        // engine's torque at that rpm. With traction control on, the
                        // clutch is also capped at what the driven tires can put down.
                        launching = true;
                        double boostMult = e.turbo != null ? 1 - e.turbo.share + e.turbo.share * boost : 1;
                        double cap = EngineParams.CurveTorque(e, g.launchRpm) * boostMult * pedal + 0.5 * (rpm - g.launchRpm);
                        if (aids.tcs)
                        {
                            // The trim holds the driven wheels' slip at 80% of the tires'
                            // peak: it corrects the cap for what the cap cannot see (wheel and
                            // driveline inertia, load swings). Slip under the target raises it
                            // slowly; slip past it lowers it fast, far enough that sliding
                            // tires grip again. It only moves while the tires, not the
                            // engine, are the limit.
                            double traction = TractionTorque() * 0.98;
                            if (traction * launchTrim < cap)
                            {
                                double err = LaunchSlipError();
                                launchTrim = M.Clamp(launchTrim + dt * (err > 0 ? 1 : 8) * err, 0.5, 1.06);
                            }
                            cap = JsMath.Min(cap, traction * launchTrim);
                        }
                        engage = M.Clamp(cap / g.clutchMaxTorque, 0, 1);
                    }
                }
                else
                {
                    engage = JsMath.Min(engage, M.Clamp((rpm - (e.idle + 40)) / 450, 0, 1));
                }
            }
            if (!launching) launchTrim = 1;
            if (inp.handbrake > 0.5 && Math.Abs(vLong) < 7) engage = 0;
            engage *= 1 - M.Clamp(inp.clutch, 0, 1);
            clutchEngage = engage;
        }

        /// <summary>
        /// (80% of peak slip - the driven axles' highest slip) / peak slip: + below
        /// the launch target, - above it. An axle's slip is that of its less-slipping
        /// wheel: in a turn the loaded outer wheel does the work while a locked or
        /// limited-slip differential spins the light inner one. The cap itself is
        /// still set by the weaker wheel, so this cannot overdrive a straight launch.
        /// </summary>
        double LaunchSlipError()
        {
            VehicleParams p = Params;
            double kMax = 0;
            double kPeak = 0.12;
            for (int a = 0; a < 4; a += 2)
            {
                double kAxle = double.PositiveInfinity;
                for (int i = a; i < a + 2; i++)
                {
                    if (p.wheels[i].driveWeight <= 0 || !wheels[i].grounded) continue;
                    kAxle = JsMath.Min(kAxle, gear == -1 ? -wheels[i].slipRatio : wheels[i].slipRatio);
                    kPeak = p.wheels[i].tire.kappaPeak;
                }
                if (!double.IsPositiveInfinity(kAxle)) kMax = JsMath.Max(kMax, kAxle);
            }
            return (0.8 * kPeak - kMax) / kPeak;
        }

        void UpdateSteering(double dt)
        {
            VehicleParams p = Params;
            double s = M.Clamp(input.steer, -1, 1);
            double maxS = p.maxSteer;
            double raw = -s * maxS;
            double target = raw;
            if (aids.steering != SteeringAssist.Off && vLong > 2)
            {
                // Direction the front axle is actually moving, relative to the body.
                frontAxle.Set(0, 0, -p.a);
                ToWorld(frontAxle, tmpA);
                PointVelocity(tmpA, tmpB);
                double vfl = tmpB.Dot(fwd);
                double vfs = tmpB.Dot(right);
                double theta = DMath.Atan2(-vfs, JsMath.Max(vfl, 0.5));
                double aLim = p.wheels[0].tire.alphaPeak * 1.2;
                double leftLim = JsMath.Min(maxS, theta + aLim);
                double rightLim = JsMath.Max(-maxS, theta - aLim);
                double center = aids.steering == SteeringAssist.Assisted ? theta * 0.8 : 0;
                center = M.Clamp(center, rightLim, leftLim);
                double assisted = s > 0 ? center + (rightLim - center) * s : center + (leftLim - center) * -s;
                target = M.Lerp(raw, assisted, M.Smoothstep(2, 8, vLong));
            }
            // Steering rack speed limit.
            steerAngle = M.MoveTowards(steerAngle, target, 5.5 * dt);
            double d = steerAngle;
            // Partial Ackermann: the inner wheel turns more.
            double L = p.wheelbase;
            double tr = p.spec.trackFront;
            double ack = p.ackermann;
            double dl = d;
            double dr = d;
            if (Math.Abs(d) > 1e-4)
            {
                double R = L / DMath.Tan(Math.Abs(d));
                double inner = DMath.Atan(L / JsMath.Max(0.5, R - tr / 2));
                double outer = DMath.Atan(L / (R + tr / 2));
                double sgn = M.Sign(d);
                if (d > 0)
                {
                    dl = sgn * M.Lerp(Math.Abs(d), inner, ack);
                    dr = sgn * M.Lerp(Math.Abs(d), outer, ack);
                }
                else
                {
                    dr = sgn * M.Lerp(Math.Abs(d), inner, ack);
                    dl = sgn * M.Lerp(Math.Abs(d), outer, ack);
                }
            }
            WheelParams[] wf = p.wheels;
            // Toe-in points both front wheels slightly toward the centerline.
            wheels[0].steer = dl - wf[0].toe;
            wheels[1].steer = dr + wf[1].toe;
            wheels[2].steer = -wf[2].toe;
            wheels[3].steer = wf[3].toe;
        }

        /// <summary>Body corners pushing out of the ground: rollovers, hard landings, bottoming.</summary>
        void BodyContacts(IGround ground)
        {
            VehicleParams p = Params;
            V3[] pts = p.bodyPoints;
            for (int i = 0; i < pts.Length; i++)
            {
                V3 lp = pts[i];
                ToWorld(lp, tmpA);
                if (!ground.Sample(tmpA.x, tmpA.z, tmpA.y + 0.5, hit)) continue;
                double depth = (hit.y - tmpA.y) * hit.ny;
                if (depth <= 0) continue;
                tmpB.Set(hit.nx, hit.ny, hit.nz);
                PointVelocity(tmpA, tmpC);
                double vn = tmpC.Dot(tmpB);
                double fn = 2.4e5 * depth + 1e5 * depth * depth * 100 - 9000 * vn;
                if (fn <= 0) continue;
                fn = JsMath.Min(fn, p.mass * 60);
                // Friction against sliding along the ground.
                double vtx = tmpC.x - tmpB.x * vn;
                double vty = tmpC.y - tmpB.y * vn;
                double vtz = tmpC.z - tmpB.z * vn;
                double vt = Math.Sqrt(vtx * vtx + vty * vty + vtz * vtz);
                const double mu = 0.45;
                double ff = vt > 0.3 ? (mu * fn) / vt : (mu * fn) / 0.3;
                bodyF.Set(tmpB.x * fn - vtx * ff, tmpB.y * fn - vty * ff, tmpB.z * fn - vtz * ff);
                force.Add(bodyF);
                tmpB.SubVectors(tmpA, pos);
                torque.Add(tmpC.CrossVectors(tmpB, bodyF));
            }
        }
    }
}
