// The tuning sliders: range, step, the parts that unlock them, and what each
// does to the physics (the formulas live in VehicleParams.Build; DESIGN.md
// lists them). Also tune share codes and the auto-tune helper, which tunes by
// measurement rather than by rules of thumb.
//
// Share code: "HT1." + base64url of [car index, one byte per slider (its
// step index), one byte per gear ratio, checksum]. Every slider has at most
// 255 steps, so a snapped tune survives the round trip exactly.
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    public sealed class TuneField
    {
        public string Key, Label, Unit, Formula;
        public TuneGroup Group;
        public double Min, Max, Step;
        internal Func<Tune, double> getter;
        internal Action<Tune, double> setter;

        public double Get(Tune t, int unused = 0) => getter(t);
        public void Set(Tune t, int unused, double v) => setter(t, Math.Max(Min, Math.Min(Max, v)));
        public int Steps => (int)Math.Round((Max - Min) / Step);
        public double Snap(double v) => Min + Math.Round((Math.Max(Min, Math.Min(Max, v)) - Min) / Step) * Step;
    }

    public enum TuneFor { Road, Circuit, Dirt, Drag }

    public static class TuneSpec
    {
        static TuneField F(string key, string label, TuneGroup g, double min, double max, double step, string unit, string formula, Func<Tune, double> get, Action<Tune, double> set) =>
            new TuneField { Key = key, Label = label, Group = g, Min = min, Max = max, Step = step, Unit = unit, Formula = formula, getter = get, setter = set };

        /// <summary>Every slider except the per-gear ratios.</summary>
        public static readonly TuneField[] Fields =
        {
            F("pressureF", "Tyre pressure front", TuneGroup.Pressure, -0.6, 0.6, 0.05, "bar", "grip x (1 - 0.07 bar^2); peak slip angle x (1 - 0.06 bar): higher is sharper, never grippier", t => t.pressureF, (t, v) => t.pressureF = v),
            F("pressureR", "Tyre pressure rear", TuneGroup.Pressure, -0.6, 0.6, 0.05, "bar", "as front", t => t.pressureR, (t, v) => t.pressureR = v),
            F("finalDrive", "Final drive", TuneGroup.Gearing, 0.8, 1.25, 0.01, "x", "final drive ratio x this", t => t.finalDrive, (t, v) => t.finalDrive = v),
            F("camberF", "Camber front", TuneGroup.Alignment, -4, 1, 0.1, "deg", "lateral grip x (1.03 - 0.012 (camber + 2)^2), best at -2; braking grip x (1 - 0.008 |camber|)", t => t.camberF, (t, v) => t.camberF = v),
            F("camberR", "Camber rear", TuneGroup.Alignment, -4, 1, 0.1, "deg", "as front", t => t.camberR, (t, v) => t.camberR = v),
            F("toeF", "Toe front", TuneGroup.Alignment, -1, 1, 0.05, "deg", "static steer angle of each wheel: + toe-in, - toe-out", t => t.toeF, (t, v) => t.toeF = v),
            F("toeR", "Toe rear", TuneGroup.Alignment, -1, 1, 0.05, "deg", "as front, for the rear wheels", t => t.toeR, (t, v) => t.toeR = v),
            F("caster", "Caster", TuneGroup.Alignment, 3, 8, 0.1, "deg", "steered wheels lean +-(caster - 5.5) x sin(steer) degrees, the outer one into the turn", t => t.caster, (t, v) => t.caster = v),
            F("arbF", "Anti-roll bar front", TuneGroup.AntiRoll, 0.2, 2.5, 0.05, "x", "front roll stiffness x this: stiffer front = more understeer", t => t.arbF, (t, v) => t.arbF = v),
            F("arbR", "Anti-roll bar rear", TuneGroup.AntiRoll, 0.2, 2.5, 0.05, "x", "stiffer rear = more oversteer", t => t.arbR, (t, v) => t.arbR = v),
            F("springF", "Springs front", TuneGroup.Springs, 0.6, 1.8, 0.02, "x", "spring natural frequency x this", t => t.springF, (t, v) => t.springF = v),
            F("springR", "Springs rear", TuneGroup.Springs, 0.6, 1.8, 0.02, "x", "as front", t => t.springR, (t, v) => t.springR = v),
            F("rideHeight", "Ride height", TuneGroup.Springs, -0.05, 0.08, 0.005, "m", "lowers the centre of gravity by 0.9 x this", t => t.rideHeight, (t, v) => t.rideHeight = v),
            F("bumpF", "Bump front", TuneGroup.Damping, 0.5, 2, 0.05, "x", "damping ratio in compression x this", t => t.bumpF, (t, v) => t.bumpF = v),
            F("bumpR", "Bump rear", TuneGroup.Damping, 0.5, 2, 0.05, "x", "as front", t => t.bumpR, (t, v) => t.bumpR = v),
            F("reboundF", "Rebound front", TuneGroup.Damping, 0.5, 2, 0.05, "x", "damping ratio in extension x this (1.6 x bump at 1)", t => t.reboundF, (t, v) => t.reboundF = v),
            F("reboundR", "Rebound rear", TuneGroup.Damping, 0.5, 2, 0.05, "x", "as front", t => t.reboundR, (t, v) => t.reboundR = v),
            F("aeroF", "Downforce front", TuneGroup.Aero, 0, 2, 0.05, "x", "front share of the downforce x this", t => t.aeroF, (t, v) => t.aeroF = v),
            F("aeroR", "Downforce rear", TuneGroup.Aero, 0, 2, 0.05, "x", "rear share of the downforce x this", t => t.aeroR, (t, v) => t.aeroR = v),
            F("brakeBalance", "Brake balance", TuneGroup.Brakes, 0.45, 0.75, 0.01, "front", "front share of the brake torque", t => t.brakeBalance, (t, v) => t.brakeBalance = v),
            F("brakePressure", "Brake pressure", TuneGroup.Brakes, 0.7, 1.3, 0.02, "x", "total brake torque x this", t => t.brakePressure, (t, v) => t.brakePressure = v),
            F("diffAccel", "Diff locking on power", TuneGroup.Differential, 0, 1, 0.05, "", "limited-slip locking under throttle", t => t.diffAccel, (t, v) => t.diffAccel = v),
            F("diffDecel", "Diff locking off power", TuneGroup.Differential, 0, 1, 0.05, "", "locking when lifting or braking", t => t.diffDecel, (t, v) => t.diffDecel = v),
            F("awdFront", "AWD front share", TuneGroup.Differential, 0.1, 0.9, 0.05, "", "share of drive torque sent to the front (AWD cars)", t => t.awdFront, (t, v) => t.awdFront = v),
        };

        /// <summary>Per-gear ratio multipliers (race transmission).</summary>
        public const double GearMin = 0.8, GearMax = 1.25, GearStep = 0.01;

        public static TuneField Field(string key)
        {
            foreach (var f in Fields) if (f.Key == key) return f;
            return null;
        }

        /// <summary>A copy of the tune with every value on its slider's grid and inside its range.</summary>
        public static Tune Snap(Tune t)
        {
            var o = t.Clone();
            foreach (var f in Fields) f.setter(o, f.Snap(f.getter(o)));
            if (o.gearScale != null)
            {
                for (int i = 0; i < o.gearScale.Length; i++)
                {
                    o.gearScale[i] = GearMin + Math.Round((Math.Max(GearMin, Math.Min(GearMax, o.gearScale[i])) - GearMin) / GearStep) * GearStep;
                }
            }
            return o;
        }

        /// <summary>
        /// The tune a car with these parts can run: every slider its parts lock
        /// goes back to the car's default (and the gear ratios, without a race
        /// transmission). Used after a part is removed and when a code is pasted.
        /// </summary>
        public static Tune ForParts(CarSpec spec, Tune t, TuneGroup unlocked, out int locked)
        {
            locked = 0;
            var o = t.Clone();
            var d = Tune.Default(spec);
            foreach (var f in Fields)
            {
                if ((unlocked & f.Group) != 0) continue;
                if (f.getter(o) != f.getter(d)) locked++;
                f.setter(o, f.getter(d));
            }
            if ((unlocked & TuneGroup.GearRatios) == 0 && o.gearScale != null)
            {
                for (int i = 0; i < o.gearScale.Length; i++)
                {
                    if (o.gearScale[i] != 1) locked++;
                    o.gearScale[i] = 1;
                }
            }
            return o;
        }

        // --- Share codes ----------------------------------------------------------------------------
        public static string Encode(CarSpec spec, Tune tune)
        {
            var t = Snap(tune);
            int carIndex = Array.IndexOf(Cars.All, spec);
            var bytes = new List<byte> { (byte)carIndex };
            foreach (var f in Fields) bytes.Add((byte)Math.Round((f.getter(t) - f.Min) / f.Step));
            foreach (var g in t.gearScale) bytes.Add((byte)Math.Round((g - GearMin) / GearStep));
            int sum = 17;
            foreach (var b in bytes) sum = (sum * 31 + b) & 0xFF;
            bytes.Add((byte)sum);
            return "HT1." + Convert.ToBase64String(bytes.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>Reads a share code. False with a reason if it is not a valid code.</summary>
        public static bool Decode(string code, out CarSpec spec, out Tune tune, out string why)
        {
            spec = null;
            tune = null;
            why = null;
            if (code == null || !code.StartsWith("HT1.")) { why = "not a Halcyon tune code"; return false; }
            byte[] bytes;
            try
            {
                string b64 = code.Substring(4).Trim().Replace('-', '+').Replace('_', '/');
                bytes = Convert.FromBase64String(b64 + new string('=', (4 - b64.Length % 4) % 4));
            }
            catch (FormatException)
            {
                why = "the code is damaged";
                return false;
            }
            if (bytes.Length < 2 + Fields.Length) { why = "the code is too short"; return false; }
            int sum = 17;
            for (int i = 0; i < bytes.Length - 1; i++) sum = (sum * 31 + bytes[i]) & 0xFF;
            if (sum != bytes[bytes.Length - 1]) { why = "the code is damaged"; return false; }
            if (bytes[0] >= Cars.All.Length) { why = "the code is for a car this version does not have"; return false; }
            spec = Cars.All[bytes[0]];
            int gears = bytes.Length - 2 - Fields.Length;
            if (gears != spec.gears.Length) { why = "the code does not match the car's gearbox"; return false; }
            var t = Tune.Default(spec);
            for (int i = 0; i < Fields.Length; i++)
            {
                var f = Fields[i];
                if (bytes[1 + i] > f.Steps) { why = "the code is damaged"; return false; }
                f.setter(t, f.Min + bytes[1 + i] * f.Step);
            }
            for (int i = 0; i < gears; i++) t.gearScale[i] = GearMin + bytes[1 + Fields.Length + i] * GearStep;
            tune = t;
            return true;
        }

        // --- Auto-tune ------------------------------------------------------------------------------
        /// <summary>The surface an event is driven on.</summary>
        public static int SurfaceFor(TuneFor kind) => kind == TuneFor.Dirt ? Surface.Gravel : Surface.Asphalt;

        /// <summary>
        /// How good a tune is for a kind of event, measured on its surface: the
        /// PI's acceleration, grip and braking terms (same scales), weighted for
        /// the event (a drag run only cares about acceleration).
        /// </summary>
        public static double Score(CarSpec spec, Tune tune, Upgrades up, TuneFor kind)
        {
            var p = VehicleParams.Build(spec, tune, up);
            int surface = SurfaceFor(kind);
            var acc = Telemetry.AccelTest(p, 14, null, surface);
            double t100 = double.IsNaN(acc.t100) ? 30 : acc.t100;
            double accel = (Math.Log(11.0) - Math.Log(Math.Max(0.5, t100))) / (Math.Log(11.0) - Math.Log(1.9));
            if (kind == TuneFor.Drag)
            {
                double quarter = double.IsNaN(acc.quarter) ? 25 : acc.quarter;
                return accel + (25 - quarter) / 20;
            }
            double grip = (Telemetry.SkidpadTest(p, 40, null, surface).g - 0.4) / (1.3 - 0.4);
            double brake = (60 - Telemetry.BrakeTest(p, 100, null, surface).distance) / (60 - 25);
            return kind == TuneFor.Circuit ? 0.25 * accel + 0.5 * grip + 0.25 * brake
                : kind == TuneFor.Dirt ? 0.3 * accel + 0.45 * grip + 0.25 * brake
                : 0.4 * accel + 0.35 * grip + 0.25 * brake;
        }

        /// <summary>
        /// Builds a tune for a kind of event by measurement: a coordinate search
        /// over the sliders the car's parts unlock, on the event's surface,
        /// keeping each change that improves Score. Steady-state tests reward
        /// rotation, so a change is only kept if the car still understeers at
        /// least min(stock - 0.15, 0.6) deg/g (never below 0.25), and rear
        /// toe-out is never used: the tune stays drivable. Damping is left alone
        /// (the test-track runs are steady and cannot judge it). Deterministic;
        /// a few seconds for a car with every part on a desktop core.
        /// </summary>
        public static Tune AutoTune(CarSpec spec, Upgrades up, TuneGroup unlocked, TuneFor kind, int budget = 48)
        {
            var best = Snap(Tune.Default(spec));
            double bestScore = Score(spec, best, up, kind);
            int used = 1;
            int surface = SurfaceFor(kind);
            var stockParams = VehicleParams.Build(spec, best, up);
            double grip = Telemetry.SkidpadTest(stockParams, 40, null, surface).g;
            double kStock = TestTrack.UndersteerGradient(stockParams, grip, surface);
            double kMin = double.IsNaN(kStock) ? 0.25 : Math.Max(0.25, Math.Min(kStock - 0.15, 0.6));
            bool Stable(Tune t)
            {
                double k = TestTrack.UndersteerGradient(VehicleParams.Build(spec, t, up), grip, surface);
                return !double.IsNaN(k) && k >= kMin;
            }
            var fields = new List<TuneField>();
            foreach (var f in Fields)
            {
                if ((unlocked & f.Group) == 0 || f.Group == TuneGroup.Damping) continue;
                if (f.Key == "awdFront" && spec.drivetrain != Drivetrain.AWD) continue;
                if ((f.Key == "brakeBalance" || f.Key == "brakePressure") && kind == TuneFor.Drag) continue;
                fields.Add(f);
            }
            // Coarse steps first, then finer ones.
            foreach (int steps in new[] { 6, 3, 1 })
            {
                foreach (var f in fields)
                {
                    foreach (int dir in new[] { 1, -1 })
                    {
                        if (used >= budget) return best;
                        var trial = best.Clone();
                        double v = f.Snap(f.Get(best) + dir * steps * f.Step);
                        if (v == f.Get(best)) continue;
                        if (f.Key == "toeR" && v < 0) continue;
                        f.Set(trial, 0, v);
                        double sc = Score(spec, trial, up, kind);
                        used++;
                        if (sc > bestScore + 1e-4 && (kind == TuneFor.Drag || Stable(trial)))
                        {
                            best = trial;
                            bestScore = sc;
                            break; // keep going from the better tune with the next slider
                        }
                    }
                }
            }
            return best;
        }
    }
}
