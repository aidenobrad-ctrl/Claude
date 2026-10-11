// Checks that do not step a vehicle: car data, tire compounds and curves, the
// tire force function, derived VehicleParams, pad shaping, the DMath port and
// the JavaScript semantics (Math.round, max/min, hypot) the port relies on.
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Halcyon.Core.Tests
{
    static class StaticChecks
    {
        static readonly Compound[] CompoundOrder =
        {
            Compound.Eco, Compound.Street, Compound.Sport, Compound.SemiSlick, Compound.Slick,
            Compound.Rally, Compound.OffRoad, Compound.Snow, Compound.Drag,
        };

        static Compound ParseCompound(string s) => (Compound)Enum.Parse(typeof(Compound), s, true);

        /// <summary>CARS against Cars.All, field by field, plus the compound table and default aids.</summary>
        public static IEnumerable<Check> Cars(JsonElement carsJson)
        {
            var spec = new Check("data", "cars.ts: every CarSpec field");
            JsonElement cars = carsJson.GetProperty("cars");
            if (cars.GetArrayLength() != Core.Cars.All.Length) spec.Fail($"CARS has {cars.GetArrayLength()} cars, Cars.All has {Core.Cars.All.Length}");
            int i = 0;
            foreach (JsonElement c in cars.EnumerateArray())
            {
                if (i >= Core.Cars.All.Length) break;
                RefJson.Tree(spec, $"CARS[{i}]", c, Core.Cars.All[i], strict: true);
                i++;
            }
            yield return spec;

            var comp = new Check("data", "tires.ts: COMPOUNDS");
            JsonElement compounds = carsJson.GetProperty("compounds");
            if (compounds.GetArrayLength() != Tires.Compounds.Length) comp.Fail("compound count differs");
            i = 0;
            foreach (JsonElement c in compounds.EnumerateArray())
            {
                string key = RefJson.S(c, "key");
                comp.Str(key, CompoundOrder[i].ToString().ToLowerInvariant(), $"COMPOUNDS order [{i}]");
                RefJson.Tree(comp, $"COMPOUNDS.{key}", c.GetProperty("info"), Tires.Info(ParseCompound(key)), strict: true);
                i++;
            }
            yield return comp;

            var aids = new Check("data", "vehicle.ts: defaultAids()");
            RefJson.Tree(aids, "defaultAids", carsJson.GetProperty("aidsDefault"), Aids.Default(), strict: true);
            var dt = new Check("data", "loop.ts: SIM_DT");
            dt.Num(RefJson.D(carsJson, "simDt"), Loop.SIM_DT, "SIM_DT");
            aids.Values += dt.Values;
            aids.Exact += dt.Exact;
            aids.ProblemCount += dt.ProblemCount;
            aids.Problems.AddRange(dt.Problems);
            yield return aids;
        }

        /// <summary>makeCurve, mf and mfSlope.</summary>
        public static Check Curves(JsonElement st)
        {
            var c = new Check("tires", "makeCurve / mf / mfSlope");
            var xs = new List<double>();
            foreach (JsonElement x in st.GetProperty("curveXs").EnumerateArray()) xs.Add(RefJson.D(x));
            foreach (JsonElement k in st.GetProperty("curves").EnumerateArray())
            {
                double slide = RefJson.D(k, "slide"), E = RefJson.D(k, "E");
                Curve curve = Tires.MakeCurve(slide, E);
                string at = $"curve(slide {slide}, E {E})";
                c.Num(RefJson.D(k, "B"), curve.B, at + ".B");
                c.Num(RefJson.D(k, "C"), curve.C, at + ".C");
                c.Num(RefJson.D(k, "curveE"), curve.E, at + ".E");
                int i = 0;
                foreach (JsonElement y in k.GetProperty("mf").EnumerateArray()) { c.Num(RefJson.D(y), Tires.Mf(curve, xs[i]), $"{at} mf({xs[i]})"); i++; }
                i = 0;
                foreach (JsonElement y in k.GetProperty("mfSlope").EnumerateArray()) { c.Num(RefJson.D(y), Tires.MfSlope(curve, xs[i]), $"{at} mfSlope({xs[i]})"); i++; }
            }
            return c;
        }

        /// <summary>tireForce on every compound's tire setup.</summary>
        public static Check TireForce(JsonElement st)
        {
            var c = new Check("tires", "tireForce (9 compounds x 48 inputs)");
            var res = new TireResult();
            foreach (JsonElement k in st.GetProperty("tire").EnumerateArray())
            {
                Compound compound = ParseCompound(RefJson.S(k, "compound"));
                TireSetup setup = VehicleParams.Build(Core.Cars.All[0], null, new Upgrades { compound = compound }).wheels[0].tire;
                var outputs = new List<JsonElement>();
                foreach (JsonElement o in k.GetProperty("outputs").EnumerateArray()) outputs.Add(o);
                int i = 0;
                foreach (JsonElement inp in k.GetProperty("inputs").EnumerateArray())
                {
                    Tires.TireForce(setup, RefJson.D(inp[0]), RefJson.D(inp[1]), RefJson.D(inp[2]), RefJson.D(inp[3]), RefJson.D(inp[4]), res);
                    JsonElement o = outputs[i];
                    string at = $"{compound} input {i}";
                    c.Num(RefJson.D(o[0]), res.fx, at + " fx");
                    c.Num(RefJson.D(o[1]), res.fy, at + " fy");
                    c.Num(RefJson.D(o[2]), res.dFxdV, at + " dFxdV");
                    c.Num(RefJson.D(o[3]), res.slip, at + " slip");
                    i++;
                }
            }
            return c;
        }

        /// <summary>buildParams for every car and the tuned/compound configurations.</summary>
        public static Check Params(JsonElement st)
        {
            var c = new Check("params", "buildParams (all derived numbers)");
            foreach (JsonElement cfg in st.GetProperty("params").EnumerateArray())
            {
                VehicleParams p = Scenarios.ParamsFor(cfg);
                string label = RefJson.S(cfg, "car") + (RefJson.TryGet(cfg, "tune", out _) ? "+tune" : "") + (RefJson.TryGet(cfg, "up", out JsonElement up) ? "+up" + (RefJson.TryGet(up, "compound", out JsonElement ck) ? ":" + ck.GetString() : "") : "");
                RefJson.Tree(c, label, cfg.GetProperty("params"), p);
            }
            return c;
        }

        /// <summary>shapeAxis / shapeTrigger.</summary>
        public static Check Shaping(JsonElement st)
        {
            var c = new Check("input", "shapeAxis / shapeTrigger");
            foreach (JsonElement k in st.GetProperty("shaping").EnumerateArray())
            {
                PadSettings pad = RefJson.Build<PadSettings>(k.GetProperty("pad"));
                var vs = new List<double>();
                foreach (JsonElement v in k.GetProperty("v").EnumerateArray()) vs.Add(RefJson.D(v));
                int i = 0;
                foreach (JsonElement y in k.GetProperty("axis").EnumerateArray()) { c.Num(RefJson.D(y), InputShaping.ShapeAxis(vs[i], pad), $"shapeAxis({Check.R(vs[i])})"); i++; }
                i = 0;
                foreach (JsonElement y in k.GetProperty("trigger").EnumerateArray()) { c.Num(RefJson.D(y), InputShaping.ShapeTrigger(vs[i], pad), $"shapeTrigger({Check.R(vs[i])})"); i++; }
            }
            return c;
        }

        /// <summary>The DMath port against dmath.ts.</summary>
        public static Check DMathCheck(JsonElement st)
        {
            var c = new Check("math", "DMath (Core/Math) vs dmath.ts");
            foreach (JsonElement k in st.GetProperty("dmath").EnumerateArray())
            {
                string f = RefJson.S(k, "f");
                var a = new List<double>();
                foreach (JsonElement x in k.GetProperty("a").EnumerateArray()) a.Add(RefJson.D(x));
                var b = new List<double>();
                if (k.TryGetProperty("b", out JsonElement bj)) foreach (JsonElement x in bj.EnumerateArray()) b.Add(RefJson.D(x));
                int i = 0;
                foreach (JsonElement y in k.GetProperty("out").EnumerateArray())
                {
                    double x = a[i];
                    double r;
                    switch (f)
                    {
                        case "sin": r = DMath.Sin(x); break;
                        case "cos": r = DMath.Cos(x); break;
                        case "tan": r = DMath.Tan(x); break;
                        case "atan": r = DMath.Atan(x); break;
                        case "asin": r = DMath.Asin(x); break;
                        case "acos": r = DMath.Acos(x); break;
                        case "exp": r = DMath.Exp(x); break;
                        case "log": r = DMath.Log(x); break;
                        case "tanh": r = DMath.Tanh(x); break;
                        case "atan2": r = DMath.Atan2(x, b[i]); break;
                        case "pow": r = DMath.Pow(x, b[i]); break;
                        default: c.Fail($"unknown function {f}"); return c;
                    }
                    c.Num(RefJson.D(y), r, $"{f}({Check.R(x)}{(b.Count > 0 ? ", " + Check.R(b[i]) : "")})");
                    i++;
                }
            }
            return c;
        }

        static double[] Arr(JsonElement obj, string name)
        {
            var r = new List<double>();
            foreach (JsonElement x in obj.GetProperty(name).EnumerateArray()) r.Add(RefJson.D(x));
            return r.ToArray();
        }

        /// <summary>The TypeScript fuzzVehicle: a car at (x, z, yaw) raised by dy, with velocity and angular velocity.</summary>
        static Vehicle FuzzVehicle(string car, double[] pose, double[] vel, double[] ang)
        {
            var v = new Vehicle(VehicleParams.Build(Scenarios.CarSpec(car)));
            v.Place(pose[0], 0, pose[1], pose[2]);
            v.pos.y += pose[3];
            v.vel.Set(vel[0], vel[1], vel[2]);
            v.angVel.Set(ang[0], ang[1], ang[2]);
            return v;
        }

        static void CompareState(Check c, string at, JsonElement expected, Vehicle v)
        {
            double[] got = { v.pos.x, v.pos.y, v.pos.z, v.vel.x, v.vel.y, v.vel.z, v.angVel.x, v.angVel.y, v.angVel.z, v.impact };
            string[] names = { "pos.x", "pos.y", "pos.z", "vel.x", "vel.y", "vel.z", "angVel.x", "angVel.y", "angVel.z", "impact" };
            int i = 0;
            foreach (JsonElement x in expected.EnumerateArray())
            {
                c.Num(RefJson.D(x), got[i], $"{at} {names[i]}");
                i++;
            }
        }

        /// <summary>collideWorld on random poses, velocities and obstacles (segments, circles, breakables, height filters).</summary>
        public static Check CollisionWorld(JsonElement st)
        {
            var c = new Check("collision", "collideWorld fuzz");
            int idx = 0;
            foreach (JsonElement k in st.GetProperty("collisionWorld").EnumerateArray())
            {
                string at = $"case {idx++}";
                Vehicle v = FuzzVehicle(RefJson.S(k, "car"), Arr(k, "pose"), Arr(k, "vel"), Arr(k, "ang"));
                if (k.GetProperty("vertical").GetBoolean()) v.fwd.Set(0, 1, 0);
                var w = new Colliders();
                foreach (JsonElement o in k.GetProperty("obstacles").EnumerateArray())
                {
                    if (o[0].GetString() == "s")
                    {
                        w.AddSegment(new Segment
                        {
                            ax = RefJson.D(o[1]), az = RefJson.D(o[2]), bx = RefJson.D(o[3]), bz = RefJson.D(o[4]),
                            top = RefJson.D(o[5]), bottom = RefJson.D(o[6]), bounce = RefJson.D(o[7]), friction = RefJson.D(o[8]),
                            kind = (SegmentKind)Enum.Parse(typeof(SegmentKind), o[9].GetString(), true),
                        });
                    }
                    else
                    {
                        w.AddCircle(new Circle
                        {
                            x = RefJson.D(o[1]), z = RefJson.D(o[2]), r = RefJson.D(o[3]), top = RefJson.D(o[4]), bottom = RefJson.D(o[5]),
                            kind = (CircleKind)Enum.Parse(typeof(CircleKind), o[6].GetString(), true), breakable = RefJson.D(o[7]) != 0,
                        });
                    }
                }
                var breaks = new List<double>();
                CollisionEvent? hit = CarCollision.CollideWorld(v, w, circle => breaks.Add(circle.id));
                JsonElement hj = k.GetProperty("hit");
                if (hj.ValueKind == JsonValueKind.Null)
                {
                    if (hit.HasValue) c.Fail($"{at}: C# reports a hit ({Check.R(hit.Value.impulse)} N·s), the TypeScript none");
                    else c.Str("null", "null", at + " hit");
                }
                else if (!hit.HasValue) c.Fail($"{at}: the TypeScript reports a hit, C# none");
                else
                {
                    c.Num(RefJson.D(hj[0]), hit.Value.impulse, at + " hit.impulse");
                    c.Num(RefJson.D(hj[1]), hit.Value.x, at + " hit.x");
                    c.Num(RefJson.D(hj[2]), hit.Value.z, at + " hit.z");
                    c.Str(hj[3].GetString(), hit.Value.kind.ToString().ToLowerInvariant(), at + " hit.kind");
                }
                double[] eb = Arr(k, "breaks");
                if (eb.Length != breaks.Count) c.Fail($"{at}: {eb.Length} breaks, got {breaks.Count}");
                else for (int i = 0; i < eb.Length; i++) c.Num(eb[i], breaks[i], $"{at} break {i}");
                CompareState(c, at, k.GetProperty("state"), v);
            }
            return c;
        }

        /// <summary>collideCars on random pairs of poses and velocities.</summary>
        public static Check CollisionCars(JsonElement st)
        {
            var c = new Check("collision", "collideCars fuzz");
            int idx = 0;
            foreach (JsonElement k in st.GetProperty("collisionCars").EnumerateArray())
            {
                string at = $"pair {idx++}";
                Vehicle a = FuzzVehicle(RefJson.S(k, "carA"), Arr(k, "poseA"), Arr(k, "velA"), Arr(k, "angA"));
                Vehicle b = FuzzVehicle(RefJson.S(k, "carB"), Arr(k, "poseB"), Arr(k, "velB"), Arr(k, "angB"));
                double j = CarCollision.CollideCars(a, b);
                c.Num(RefJson.D(k, "j"), j, at + " impulse");
                CompareState(c, at + " a", k.GetProperty("a"), a);
                CompareState(c, at + " b", k.GetProperty("b"), b);
            }
            return c;
        }

        /// <summary>Math.round, Math.max/min with signed zeros and NaN, and V8's Math.hypot.</summary>
        public static Check JsSemantics(JsonElement st)
        {
            var c = new Check("math", "M.JsRound, JsMath.Max/Min/Hypot vs V8");
            JsonElement js = st.GetProperty("jsmath");
            JsonElement round = js.GetProperty("round");
            var rx = new List<double>();
            foreach (JsonElement x in round.GetProperty("x").EnumerateArray()) rx.Add(RefJson.D(x));
            int i = 0;
            foreach (JsonElement y in round.GetProperty("r").EnumerateArray()) { c.Num(RefJson.D(y), M.JsRound(rx[i]), $"Math.round({Check.R(rx[i])})"); i++; }
            JsonElement mm = js.GetProperty("minmax");
            var ab = new List<(double, double)>();
            foreach (JsonElement p in mm.GetProperty("ab").EnumerateArray()) ab.Add((RefJson.D(p[0]), RefJson.D(p[1])));
            i = 0;
            foreach (JsonElement y in mm.GetProperty("max").EnumerateArray()) { c.Num(RefJson.D(y), JsMath.Max(ab[i].Item1, ab[i].Item2), $"Math.max({Check.R(ab[i].Item1)}, {Check.R(ab[i].Item2)})"); i++; }
            i = 0;
            foreach (JsonElement y in mm.GetProperty("min").EnumerateArray()) { c.Num(RefJson.D(y), JsMath.Min(ab[i].Item1, ab[i].Item2), $"Math.min({Check.R(ab[i].Item1)}, {Check.R(ab[i].Item2)})"); i++; }
            foreach (JsonElement h in js.GetProperty("hypot").EnumerateArray())
            {
                double x = RefJson.D(h[0]), y = RefJson.D(h[1]);
                c.Num(RefJson.D(h[2]), JsMath.Hypot(x, y), $"Math.hypot({Check.R(x)}, {Check.R(y)})");
            }
            return c;
        }
    }
}
