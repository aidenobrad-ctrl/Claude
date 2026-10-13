// The C# side of the scenarios in tools/dev/vehicle-ref.ts. The setup (cars,
// tuning, aids, ground, driver arguments) comes from the reference JSON; the
// drivers below mirror the TypeScript ones statement for statement, so keep
// the two files in sync.
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Halcyon.Core.Tests
{
    /// <summary>The TypeScript Ctx: steps vehicles, records a row every `every` steps, and logs events.</summary>
    sealed class Ctx
    {
        public int n;
        public readonly List<double[]> Rows = new List<double[]>();
        public readonly List<object[]> Events = new List<object[]>();
        public readonly Vehicle[] V;
        public readonly IGround Ground;
        public readonly int Every;
        readonly List<double> row = new List<double>();

        public Ctx(Vehicle[] v, IGround ground, int every)
        {
            V = v;
            Ground = ground;
            Every = every;
        }

        public void Rec()
        {
            row.Clear();
            row.Add(n);
            foreach (Vehicle v in V) Sample(v, row);
            Rows.Add(row.ToArray());
        }

        public void Event(params object[] xs)
        {
            var e = new object[xs.Length + 1];
            e[0] = (double)n;
            Array.Copy(xs, 0, e, 1, xs.Length);
            Events.Add(e);
        }

        void After()
        {
            n++;
            if (n % Every == 0) Rec();
        }

        static int Steps(double seconds) => (int)M.JsRound(seconds / Loop.SIM_DT);

        /// <summary>Step one vehicle for `seconds`, calling each(t, i) before and post() after every step.</summary>
        public void Run(Vehicle v, double seconds, Action<double, int> each = null, Action post = null)
        {
            int count = Steps(seconds);
            for (int i = 0; i < count; i++)
            {
                each?.Invoke(i * Loop.SIM_DT, i);
                v.Step(Loop.SIM_DT, Ground);
                post?.Invoke();
                After();
            }
        }

        /// <summary>Step two vehicles and resolve their collision every step.</summary>
        public void RunPair(Vehicle a, Vehicle b, double seconds, Action<double, int> each = null)
        {
            int count = Steps(seconds);
            for (int i = 0; i < count; i++)
            {
                each?.Invoke(i * Loop.SIM_DT, i);
                a.Step(Loop.SIM_DT, Ground);
                b.Step(Loop.SIM_DT, Ground);
                double j = CarCollision.CollideCars(a, b);
                if (j != 0) Event("cars", j);
                After();
            }
        }

        public static readonly string[] VCols =
        {
            "pos.x", "pos.y", "pos.z", "quat.x", "quat.y", "quat.z", "quat.w", "vel.x", "vel.y", "vel.z", "angVel.x", "angVel.y", "angVel.z",
            "engineOmega", "rpm", "gear", "clutchEngage", "boost", "throttleEff", "tcsCut", "escCut", "steerAngle", "shiftTimer",
            "speed", "odometer", "accelLong", "accelLat", "impact", "input.steer", "input.throttle", "input.brake", "input.handbrake",
        };
        public static readonly string[] WCols = { "omega", "length", "load", "slipRatio", "slipAngle", "fx", "fy", "steer", "spin", "absScale", "brakeTorque", "contact.x", "contact.y", "contact.z" };

        public static List<string> Columns(int nv)
        {
            var cols = new List<string> { "step" };
            for (int k = 0; k < nv; k++)
            {
                string p = nv > 1 ? $"v{k}." : "";
                foreach (string c in VCols) cols.Add(p + c);
                for (int i = 0; i < 4; i++) foreach (string c in WCols) cols.Add($"{p}w{i}.{c}");
            }
            return cols;
        }

        static void Sample(Vehicle v, List<double> r)
        {
            r.Add(v.pos.x); r.Add(v.pos.y); r.Add(v.pos.z);
            r.Add(v.quat.x); r.Add(v.quat.y); r.Add(v.quat.z); r.Add(v.quat.w);
            r.Add(v.vel.x); r.Add(v.vel.y); r.Add(v.vel.z);
            r.Add(v.angVel.x); r.Add(v.angVel.y); r.Add(v.angVel.z);
            r.Add(v.engineOmega); r.Add(v.Rpm); r.Add(v.gear); r.Add(v.clutchEngage); r.Add(v.boost); r.Add(v.throttleEff);
            r.Add(v.tcsCut); r.Add(v.escCut); r.Add(v.steerAngle); r.Add(v.shiftTimer);
            r.Add(v.speed); r.Add(v.odometer); r.Add(v.accelLong); r.Add(v.accelLat); r.Add(v.impact);
            r.Add(v.input.steer); r.Add(v.input.throttle); r.Add(v.input.brake); r.Add(v.input.handbrake);
            foreach (WheelState w in v.wheels)
            {
                r.Add(w.omega); r.Add(w.length); r.Add(w.load); r.Add(w.slipRatio); r.Add(w.slipAngle); r.Add(w.fx); r.Add(w.fy);
                r.Add(w.steer); r.Add(w.spin); r.Add(w.absScale); r.Add(w.brakeTorque);
                r.Add(w.contact.x); r.Add(w.contact.y); r.Add(w.contact.z);
            }
        }
    }

    /// <summary>Driver arguments from the JSON.</summary>
    sealed class Args
    {
        readonly JsonElement e;
        public Args(JsonElement e) { this.e = e; }
        public double D(string name) => RefJson.D(e, name);
        public string S(string name) => RefJson.S(e, name);
    }

    static class Scenarios
    {
        const double RAD2DEG = 180 / Math.PI;

        // --- Cars ------------------------------------------------------------

        /// <summary>The test-only variant specs from cars.json (the real cars come from Cars.All).</summary>
        public static readonly Dictionary<string, CarSpec> Variants = new Dictionary<string, CarSpec>();

        public static CarSpec CarSpec(string id) => Variants.TryGetValue(id, out CarSpec s) ? s : Cars.ById(id);

        /// <summary>paramsFor({ car, tune?, up? }).</summary>
        public static VehicleParams ParamsFor(JsonElement setup)
        {
            CarSpec spec = CarSpec(RefJson.S(setup, "car"));
            Tune tune = RefJson.TryGet(setup, "tune", out JsonElement t) ? RefJson.Build<Tune>(t) : null;
            Upgrades up = RefJson.TryGet(setup, "up", out JsonElement u) ? RefJson.Build<Upgrades>(u) : null;
            return VehicleParams.Build(spec, tune, up);
        }

        static VehicleParams ParamsFor(string car) => VehicleParams.Build(CarSpec(car));

        // --- Grounds and worlds ----------------------------------------------

        static readonly int[] Stripes =
        {
            Surface.Asphalt, Surface.Grass, Surface.Dirt, Surface.Sand, Surface.Snow, Surface.Gravel, Surface.Mud,
            Surface.Ice, Surface.Concrete, Surface.Curb, Surface.Rock, Surface.AsphaltWet, Surface.Water,
        };

        static int StripeSurface(double x, double z)
        {
            double n = Stripes.Length;
            double k = Math.Floor(z / 7) + (x > 0.4 ? 3 : 0);
            return Stripes[(int)(((k % n) + n) % n)];
        }

        sealed class PuddleGround : IGround
        {
            public bool Sample(double x, double z, double yRef, GroundHit o)
            {
                bool inPuddle = x > -3 && x < 3 && z < -20 && z > -45;
                o.y = 0;
                o.nx = 0;
                o.ny = 1;
                o.nz = 0;
                o.surface = inPuddle ? Surface.AsphaltWet : Surface.Asphalt;
                o.water = inPuddle ? 0.03 + (0.1 * (-20 - z)) / 25 : 0;
                Bumps.Apply(x, z, o);
                return true;
            }
        }

        /// <summary>Flat concrete that ends at x = 20: beyond it Sample finds no ground at all (the world's edge).</summary>
        sealed class EdgeGround : IGround
        {
            public bool Sample(double x, double z, double yRef, GroundHit o)
            {
                if (x > 20) return false;
                o.y = 0;
                o.nx = 0;
                o.ny = 1;
                o.nz = 0;
                o.surface = Surface.Concrete;
                o.water = 0;
                Bumps.Apply(x, z, o);
                return true;
            }
        }

        public static IGround MakeGround(JsonElement g)
        {
            switch (RefJson.S(g, "type"))
            {
                case "flat":
                    int surface = (int)RefJson.D(g, "surface");
                    return new FlatGround((x, z) => surface);
                case "stripes":
                    return new FlatGround(StripeSurface);
                case "puddle":
                    return new PuddleGround();
                case "edge":
                    return new EdgeGround();
                case "slope":
                    return new SlopeGround(RefJson.D(g, "grade"), (int)RefJson.D(g, "surface"));
            }
            throw new ArgumentException($"unknown ground {g}");
        }

        static Colliders MakeWorld(string layout)
        {
            var w = new Colliders();
            switch (layout)
            {
                case "wall":
                    w.AddSegment(new Segment { ax = -30, az = -40, bx = 30, bz = -40, top = 2, bottom = -1, bounce = 0.3, friction = 0.5, kind = SegmentKind.Wall });
                    break;
                case "tree":
                    w.AddCircle(new Circle { x = -0.3, z = -10, r = 0.2, top = 0.6, bottom = 0, kind = CircleKind.Post, breakable = true });
                    w.AddSegment(new Segment { ax = -5, az = -14, bx = 5, bz = -14, top = 0.05, bottom = -1, bounce = 0.3, friction = 0.5, kind = SegmentKind.Barrier });
                    var tree = new Circle { x = 0.7, z = -24, r = 0.4, top = 10, bottom = 0, kind = CircleKind.Tree, id = -1 }; // no id in the TypeScript (?? -1)
                    w.Extra = (x, z, r, cb) => cb(tree);
                    break;
                case "post":
                    w.AddCircle(new Circle { x = 1.0, z = -25, r = 0.25, top = 1.5, bottom = 0, kind = CircleKind.Post });
                    break;
                case "rail":
                    w.AddSegment(new Segment { ax = -3, az = -200, bx = -3, bz = 10, top = 0.8, bottom = -1, bounce = 0.2, friction = 0.6, kind = SegmentKind.Rail });
                    break;
                case "end":
                    w.AddSegment(new Segment { ax = 0.5, az = -6, bx = 0.5, bz = -6, top = 1, bottom = -1, bounce = 0.2, friction = 0.5, kind = SegmentKind.Tires }); // zero length
                    w.AddSegment(new Segment { ax = 2.5, az = -12, bx = 3, bz = -12, top = 1, bottom = -1, bounce = 0.2, friction = 0.5, kind = SegmentKind.Barrier }); // beside the path
                    w.AddSegment(new Segment { ax = -0.05, az = -20, bx = 0.25, bz = -20, top = 1.2, bottom = -1, bounce = 0.25, friction = 0.4, kind = SegmentKind.Fence });
                    break;
                default:
                    throw new ArgumentException($"unknown layout {layout}");
            }
            return w;
        }

        // --- Drivers ---------------------------------------------------------

        static readonly int[] Ups = { (int)M.JsRound(2.0 / Loop.SIM_DT), (int)M.JsRound(2.8 / Loop.SIM_DT), (int)M.JsRound(3.6 / Loop.SIM_DT) };
        static readonly int[] Downs = { (int)M.JsRound(5.2 / Loop.SIM_DT), (int)M.JsRound(5.6 / Loop.SIM_DT), (int)M.JsRound(6.0 / Loop.SIM_DT), (int)M.JsRound(6.4 / Loop.SIM_DT) };

        public static readonly Dictionary<string, Action<Ctx, Args>> Drivers = new Dictionary<string, Action<Ctx, Args>>
        {
            ["launch"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                c.Run(v, 0.5);
                c.Run(v, a.D("seconds"), (t, i) => { v.input.throttle = 1; });
            },
            ["brake"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                c.Run(v, 0.5);
                v.SetSpeed(a.D("kmh") / 3.6);
                c.Rec();
                c.Run(v, a.D("seconds"), (t, i) => { v.input.brake = 1; });
            },
            ["corner"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                double speed = a.D("speed"), steer = a.D("steer");
                v.Place(a.D("radius"), 0, 0, 0);
                c.Run(v, 0.5);
                v.SetSpeed(speed);
                c.Rec();
                c.Run(v, a.D("seconds"), (t, i) =>
                {
                    v.input.steer = steer;
                    v.input.throttle = 0.25 + (speed - v.vLong) * 0.5;
                });
            },
            ["weave"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                double steer = a.D("steer"), amp = a.D("amp"), freq = a.D("freq"), throttle = a.D("throttle");
                c.Run(v, 0.3);
                v.SetSpeed(a.D("speed"));
                c.Rec();
                c.Run(v, a.D("seconds"), (t, i) =>
                {
                    v.input.steer = steer + amp * DMath.Sin(t * freq);
                    v.input.throttle = throttle;
                });
            },
            ["lanechange"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                c.Run(v, 0.3);
                v.SetSpeed(a.D("speed"));
                c.Rec();
                c.Run(v, a.D("seconds"), (t, i) =>
                {
                    v.input.steer = t < 0.6 ? -0.35 : t < 1.4 ? 0.35 : 0;
                    v.input.throttle = t < 1.6 ? 0 : 0.3;
                });
            },
            ["handbrake"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                c.Run(v, 0.3);
                v.SetSpeed(a.D("speed"));
                c.Rec();
                c.Run(v, 1.2, (t, i) =>
                {
                    v.input.steer = -0.5;
                    v.input.handbrake = t < 0.5 ? 1 : 0;
                    v.input.throttle = t > 0.5 ? 0.7 : 0;
                });
                double maxDeg = v.Params.spec.steering.maxAngle;
                c.Run(v, a.D("seconds"), (t, i) =>
                {
                    double beta = DMath.Atan2(v.vLat, JsMath.Max(1, v.vLong)) * RAD2DEG;
                    v.input.handbrake = 0;
                    v.input.steer = JsMath.Max(-1, JsMath.Min(1, (beta + 0.6 * (beta - 25)) / maxDeg));
                    v.input.throttle = JsMath.Max(0, JsMath.Min(1, 0.55 + 0.03 * (25 - beta)));
                });
            },
            ["surfaces"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                double brakeAt = a.D("brakeAt");
                c.Run(v, 0.5);
                v.SetSpeed(a.D("speed"));
                c.Rec();
                c.Run(v, a.D("seconds"), (t, i) =>
                {
                    v.input.steer = 0.25 * DMath.Sin(t * 1.3);
                    v.input.throttle = t < brakeAt ? 0.8 : 0;
                    v.input.brake = t < brakeAt ? 0 : 1;
                });
            },
            ["puddle"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                c.Run(v, 0.3);
                v.SetSpeed(a.D("speed"));
                c.Rec();
                c.Run(v, a.D("seconds"), (t, i) =>
                {
                    v.input.throttle = 0.35;
                    v.input.steer = 0.15 * DMath.Sin(t * 2.1);
                });
            },
            ["hillstart"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                v.Place(0, 0, 0, -Math.PI / 2);
                c.Rec();
                c.Run(v, 1, (t, i) => { v.input.handbrake = 1; });
                c.Run(v, 0.4, (t, i) =>
                {
                    v.input.handbrake = 1;
                    v.input.throttle = 0.5;
                });
                c.Run(v, a.D("seconds"), (t, i) =>
                {
                    v.input.handbrake = 0;
                    v.input.throttle = 0.7;
                });
            },
            ["rollback"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                v.Place(0, 0, 0, -Math.PI / 2);
                c.Rec();
                c.Run(v, 1, (t, i) => { v.input.handbrake = 1; });
                c.Run(v, a.D("seconds"), (t, i) => { v.input.handbrake = 0; });
            },
            ["parked"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                v.Place(0, 0, 0, 0);
                c.Rec();
                c.Run(v, a.D("seconds"), (t, i) => { v.input.handbrake = 1; });
            },
            ["manual"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                c.Run(v, 0.3);
                c.Run(v, 7, (t, i) =>
                {
                    v.input.shiftUp = Array.IndexOf(Ups, i) >= 0;
                    v.input.shiftDown = Array.IndexOf(Downs, i) >= 0;
                    v.input.throttle = t < 4.4 ? 1 : t < 5 ? 0.5 : 0;
                    v.input.clutch = t >= 4.4 && t < 5 ? 1 : 0;
                    v.input.brake = t >= 5 ? 0.6 : 0;
                });
            },
            ["reverse"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                c.Run(v, 0.5);
                c.Run(v, 3, (t, i) => { v.input.brake = 1; });
                c.Run(v, 2.5, (t, i) =>
                {
                    v.input.brake = 0;
                    v.input.throttle = 1;
                });
            },
            ["crash"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                Colliders world = MakeWorld(a.S("layout"));
                double throttle = a.D("throttle");
                Action<Circle> onBreak = circle => c.Event("break", (double)circle.id, circle.x, circle.z);
                Action collide = () =>
                {
                    CollisionEvent? hit = CarCollision.CollideWorld(v, world, onBreak);
                    if (hit.HasValue) c.Event("hit", hit.Value.impulse, hit.Value.x, hit.Value.z, hit.Value.kind.ToString().ToLowerInvariant());
                };
                v.Place(a.D("x"), 0, a.D("z"), a.D("yaw"));
                c.Run(v, 0.3, null, collide);
                v.SetSpeed(a.D("speed"));
                c.Rec();
                c.Run(v, a.D("seconds"), (t, i) => { v.input.throttle = throttle; }, collide);
            },
            ["twocars"] = (c, a) =>
            {
                Vehicle A = c.V[0], B = c.V[1];
                double athr = a.D("athr"), bthr = a.D("bthr");
                A.Place(a.D("ax"), 0, a.D("az"), a.D("ayaw"));
                B.Place(a.D("bx"), 0, a.D("bz"), a.D("byaw"));
                c.Run(A, 0.3);
                c.Run(B, 0.3);
                A.SetSpeed(a.D("aspeed"));
                double bspeed = a.D("bspeed");
                if (bspeed != 0 && !double.IsNaN(bspeed)) B.SetSpeed(bspeed); // `if (a.bspeed)`
                c.Rec();
                c.RunPair(A, B, a.D("seconds"), (t, i) =>
                {
                    A.input.throttle = athr;
                    B.input.throttle = bthr;
                });
            },
            ["filterKeys"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                var f = new PlayerControlFilter();
                f.settings.steerSensitivity = 1.2;
                Controls ctl = Controls.Neutral();
                ctl.analogSteer = false;
                ctl.source = InputSource.Keyboard;
                c.Run(v, 0.3);
                c.Run(v, 6, (t, i) =>
                {
                    ctl.steer = t < 1 ? 0 : t < 2 ? 1 : t < 2.5 ? 0 : t < 3.5 ? -1 : t < 4 ? 1 : 0;
                    ctl.throttle = t < 4 ? 1 : 0;
                    ctl.brake = t < 4 ? 0 : 1;
                    ctl.handbrake = t >= 4.5 && t < 4.8 ? 1 : 0;
                    ctl.clutch = t >= 1.5 && t < 1.6 ? 1 : 0;
                    ctl.pressed[ControlAction.ShiftUp] = i == 240 || i == 480;
                    ctl.pressed[ControlAction.ShiftDown] = i == 1100;
                    if (i == 700) f.Reset();
                    v.input = f.Update(ctl, v, Loop.SIM_DT);
                });
            },
            ["filterPad"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                var f = new PlayerControlFilter();
                var pad = new PadSettings { deadzone = 0.1, outerDeadzone = 0.03, triggerDeadzone = 0.04, steerLinearity = 1.25 };
                Controls ctl = Controls.Neutral();
                c.Run(v, 0.3);
                c.Run(v, 6, (t, i) =>
                {
                    f.settings.autoThrottle = t >= 2 && t < 3;
                    ctl.steer = InputShaping.ShapeAxis(0.9 * DMath.Sin(t * 1.7), pad);
                    ctl.throttle = InputShaping.ShapeTrigger(0.5 + 0.5 * DMath.Sin(t * 0.7), pad);
                    ctl.brake = InputShaping.ShapeTrigger(t > 4.5 ? 0.8 : 0.02, pad);
                    v.input = f.Update(ctl, v, Loop.SIM_DT);
                });
            },
            ["drop"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                v.Place(0, a.D("height"), 0, 0);
                c.Rec();
                c.Run(v, a.D("seconds"));
            },
            ["rollover"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                v.Place(0, 1.5, 0, 0);
                v.quat.Set(0, 0, DMath.Sin((Math.PI / 4) * 0.98), DMath.Cos((Math.PI / 4) * 0.98));
                v.angVel.Set(0.5, 1.5, 3);
                c.Rec();
                c.Run(v, a.D("seconds"));
            },
            ["setparams"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                v.Place(0, 1, 0, 0);
                c.Rec();
                c.Run(v, 0.1);
                v.SetParams(ParamsFor(a.S("other")));
                c.Rec();
                c.Run(v, a.D("seconds"), (t, i) => { v.input.throttle = 0.6; });
            },
            ["tuned"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                v.draftFactor = 0.7;
                c.Run(v, 0.5);
                c.Run(v, 3, (t, i) => { v.input.throttle = 1; });
                c.Run(v, 3, (t, i) =>
                {
                    v.input.throttle = 0.6;
                    v.input.steer = -0.3 * DMath.Sin(t * 1.5);
                });
            },
            ["overrev"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                c.Run(v, 0.3);
                v.SetSpeed(a.D("speed"));
                v.gear = 1;
                c.Rec();
                c.Run(v, 1, (t, i) => { v.input.throttle = 0; });
                v.gear = 0;
                c.Rec();
                c.Run(v, 1, (t, i) => { v.input.throttle = 0.5; });
            },
            ["cruise"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                double throttle = a.D("throttle");
                c.Run(v, 0.3);
                v.SetSpeed(a.D("speed"));
                c.Rec();
                c.Run(v, a.D("seconds"), (t, i) => { v.input.throttle = throttle; });
            },
            ["edge"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                v.Place(0, 0, 0, -Math.PI / 2);
                c.Run(v, 0.3);
                v.SetSpeed(a.D("speed"));
                c.Rec();
                c.Run(v, a.D("seconds"), (t, i) => { v.input.throttle = 0.3; });
            },
            ["limiter"] = (c, a) =>
            {
                Vehicle v = c.V[0];
                c.Run(v, 0.3);
                v.SetSpeed(a.D("speed"));
                c.Rec();
                c.Run(v, a.D("seconds"), (t, i) => { v.input.throttle = 1; });
            },
        };

        /// <summary>Replay one trace file's scenario and compare every recorded number and event.</summary>
        public static void Run(Check check, JsonElement trace)
        {
            var cars = new List<JsonElement>();
            foreach (JsonElement s in trace.GetProperty("cars").EnumerateArray()) cars.Add(s);
            JsonElement aidsJson = trace.GetProperty("aids");
            var vs = new Vehicle[cars.Count];
            for (int k = 0; k < cars.Count; k++)
            {
                vs[k] = new Vehicle(ParamsFor(cars[k]));
                vs[k].aids = RefJson.Build<Aids>(aidsJson[k]);
                if (RefJson.TryGet(cars[k], "wetness", out JsonElement wet)) vs[k].wetness = RefJson.D(wet);
            }
            var c = new Ctx(vs, MakeGround(trace.GetProperty("ground")), (int)RefJson.D(trace, "every"));
            string driver = RefJson.S(trace, "driver");
            if (!Drivers.TryGetValue(driver, out Action<Ctx, Args> drive))
            {
                check.Fail($"no C# driver '{driver}'");
                return;
            }
            drive(c, new Args(trace.GetProperty("args")));

            // Columns, step count, rows, events.
            var cols = new List<string>();
            foreach (JsonElement e in trace.GetProperty("columns").EnumerateArray()) cols.Add(e.GetString());
            List<string> mine = Ctx.Columns(vs.Length);
            if (string.Join(",", cols) != string.Join(",", mine)) check.Fail("column lists differ");
            check.Num(RefJson.D(trace, "steps"), c.n, "steps");
            JsonElement rows = trace.GetProperty("rows");
            if (rows.GetArrayLength() != c.Rows.Count) check.Fail($"expected {rows.GetArrayLength()} rows, got {c.Rows.Count}");
            int ri = 0;
            foreach (JsonElement row in rows.EnumerateArray())
            {
                if (ri >= c.Rows.Count) break;
                double[] got = c.Rows[ri++];
                int ci = 0;
                foreach (JsonElement x in row.EnumerateArray())
                {
                    if (ci >= got.Length) break;
                    int col = ci;
                    double step = got[0];
                    check.Num(RefJson.D(x), got[ci++], () => $"step {step} {(col < cols.Count ? cols[col] : "#" + col)}");
                }
                if (ci != row.GetArrayLength() || ci != got.Length) check.Fail($"row {ri}: {row.GetArrayLength()} values, got {got.Length}");
            }
            JsonElement events = trace.GetProperty("events");
            if (events.GetArrayLength() != c.Events.Count) check.Fail($"expected {events.GetArrayLength()} events, got {c.Events.Count}");
            int ei = 0;
            foreach (JsonElement ev in events.EnumerateArray())
            {
                if (ei >= c.Events.Count) break;
                object[] got = c.Events[ei++];
                if (got.Length != ev.GetArrayLength())
                {
                    check.Fail($"event {ei}: {ev.GetArrayLength()} items, got {got.Length}");
                    continue;
                }
                int k = 0;
                foreach (JsonElement x in ev.EnumerateArray())
                {
                    string where = $"event {ei - 1} item {k}";
                    if (got[k] is string s) check.Str(x.GetString(), s, where);
                    else check.Num(RefJson.D(x), (double)got[k], where);
                    k++;
                }
            }
        }
    }
}
