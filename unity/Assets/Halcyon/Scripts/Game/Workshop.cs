// The garage's workshop pages: upgrades, tuning and the test track.
//   Upgrades: ↑/↓ category, ←/→ level, Enter fits it. The panel shows the
//     part's price, what it does, the sliders it unlocks and the PI with it,
//     measured on a worker thread.
//   Tuning: ↑/↓ slider, ←/→ adjusts (held keys repeat). Locked sliders say
//     which part they need. Graphs: power and torque, gear and speed, and
//     the balance bars. 1-4 auto-tune for road, circuit, dirt or drag (by
//     measurement, a few seconds), C copies the share code, V pastes one,
//     R resets, Enter drives the test track.
//   Test track: 0-100, top speed, 100-0, lateral grip, understeer gradient
//     and PI, against the previous run.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public sealed partial class Garage
    {
        static readonly PartCategory[] Categories = (PartCategory[])Enum.GetValues(typeof(PartCategory));
        int[] pending;

        int CarPrice(OwnedCar car) => Economy.CarPrice(career.Pis.Get(car.Id, Rivals.Ladder[0]).Pi);

        // --- Upgrades ------------------------------------------------------------------------------
        void OpenUpgrades()
        {
            pending = new int[Categories.Length];
            for (int i = 0; i < Categories.Length; i++) pending[i] = Owned.Level(Categories[i]);
        }

        Dictionary<PartCategory, int> PendingParts(int i)
        {
            var parts = new Dictionary<PartCategory, int>(Owned.Parts);
            if (pending[i] <= 0) parts.Remove(Categories[i]);
            else parts[Categories[i]] = pending[i];
            return parts;
        }

        void TickUpgrades(DriverInput.MenuKeys m, bool left, bool right)
        {
            int n = Categories.Length;
            if (m.up) row = (row + n - 1) % n;
            if (m.down) row = (row + 1) % n;
            int levels = Parts.Catalog[Categories[row]].Length;
            if (left) pending[row] = (pending[row] + levels - 1) % levels;
            if (right) pending[row] = (pending[row] + 1) % levels;
            if (m.back) { Go(Page.Cars); return; }
            if (m.select) Fit(row);
        }

        void Fit(int i)
        {
            var car = Owned;
            var cat = Categories[i];
            int level = pending[i];
            if (car.Level(cat) == level) { career.Say("That is already fitted."); return; }
            var part = Parts.Get(cat, level);
            int price = Parts.Price(part, CarPrice(car));
            if (level > 0 && P.Credits < price) { career.Say($"You need {Ui.Credits(price - P.Credits)} more."); return; }
            if (!P.FitPart(car, cat, level, price, out string why)) { career.Say("Can't fit it: " + why); return; }
            // Sliders the parts no longer unlock go back to their defaults.
            if (car.Tune != null) car.Tune = TuneSpec.ForParts(car.Spec, car.Tune, Parts.Unlocked(car.Parts), out _);
            career.Remeasure(car);
            career.MarkDirty();
            career.Say(level == 0 ? $"Back to stock: {part.Name.ToLowerInvariant()}." : $"{part.Name} fitted for {Ui.Credits(price)}.");
        }

        static string Unlocks(TuneGroup g)
        {
            var names = new List<string>();
            if ((g & TuneGroup.Gearing) != 0) names.Add("final drive");
            if ((g & TuneGroup.GearRatios) != 0) names.Add("gear ratios");
            if ((g & TuneGroup.Springs) != 0) names.Add("springs and ride height");
            if ((g & TuneGroup.AntiRoll) != 0) names.Add("anti-roll bars");
            if ((g & TuneGroup.Damping) != 0) names.Add("dampers");
            if ((g & TuneGroup.Alignment) != 0) names.Add("camber, toe and caster");
            if ((g & TuneGroup.Aero) != 0) names.Add("downforce");
            if ((g & TuneGroup.Brakes) != 0) names.Add("brake balance and pressure");
            if ((g & TuneGroup.Differential) != 0) names.Add("differential");
            return names.Count == 0 ? "" : "Unlocks tuning: " + string.Join(", ", names) + ".";
        }

        void DrawUpgrades()
        {
            var car = Owned;
            var r = new Rect(24, 24, 620, 120 + Categories.Length * 34);
            Ui.Panel(r);
            CarHeader(r, car.Spec, "Upgrades");
            float y = r.y + 104;
            for (int i = 0; i < Categories.Length; i++)
            {
                var cat = Categories[i];
                var part = Parts.Get(cat, pending[i]);
                bool fitted = car.Level(cat) == pending[i];
                string value = $"◀ {part.Name} ▶" + (fitted ? "  ✓" : "");
                if (Ui.Item(new Rect(r.x + 8, y, r.width - 16, 30), cat.ToString(), i == row, value))
                {
                    if (row == i) Fit(i);
                    row = i;
                }
                y += 34;
            }
            // The selected part.
            var sel = Categories[row];
            var p = Parts.Get(sel, pending[row]);
            int price = Parts.Price(p, CarPrice(car));
            var d = new Rect(r.xMax + 16, 24, 560, 300);
            Ui.Panel(d);
            GUI.Label(new Rect(d.x + 18, d.y + 14, d.width - 36, 36), p.Name, Ui.Sub);
            string status = car.Level(sel) == pending[row] ? "Fitted" : pending[row] == 0 ? "Back to stock: free" : $"Price {Ui.Credits(price)}";
            GUI.Label(new Rect(d.x + 18, d.y + 52, d.width - 36, 28), $"{(string.IsNullOrEmpty(p.Effect) ? "Stock" : p.Effect)} · {status}", Ui.Body);
            GUI.Label(new Rect(d.x + 18, d.y + 84, d.width - 36, 50), Unlocks(p.Unlocks), Ui.Small);
            var now = StatsOf(car.Id, car.Parts, car.Tune);
            var with = StatsOf(car.Id, PendingParts(row), car.Tune);
            GUI.Label(new Rect(d.x + 18, d.y + 140, d.width - 36, 30), $"PI {Label(now)}  →  {Label(with)}", Ui.Sub);
            if (now.IsCompleted && with.IsCompleted && !now.IsFaulted && !with.IsFaulted)
            {
                var a = now.Result;
                var b = with.Result;
                GUI.Label(new Rect(d.x + 18, d.y + 176, d.width - 36, 110),
                    $"0–100   {a.ZeroTo100:0.00} → {b.ZeroTo100:0.00} s\n" +
                    $"Top     {a.TopSpeed:0} → {b.TopSpeed:0} km/h\n" +
                    $"Grip    {a.GripAtSpeed:0.00} → {b.GripAtSpeed:0.00} g at 180 km/h\n" +
                    $"100–0   {a.Braking:0.0} → {b.Braking:0.0} m", Ui.Mono);
            }
            GUI.Label(new Rect(d.x + 18, d.yMax + 8, d.width, 50), "Fitting a part buys it. A part you take off is not kept.", Ui.Small);
            Hint("↑/↓ category   ←/→ part   Enter fit   Esc back");
        }

        // --- Tuning --------------------------------------------------------------------------------
        Tune tune;
        bool tuneDirty;
        readonly List<(TuneField f, int gear)> rows = new List<(TuneField f, int gear)>();
        Task<Tune> autoTask;
        string autoFor;
        Plot powerPlot, gearPlot;
        string powerKey, gearKey;

        void OpenTuning()
        {
            var car = Owned;
            tune = TuneSpec.Snap(car.Tune ?? Tune.Default(car.Spec));
            tuneDirty = false;
            rows.Clear();
            foreach (var f in TuneSpec.Fields)
            {
                if (f.Key == "awdFront" && car.Spec.drivetrain != Drivetrain.AWD) continue;
                rows.Add((f, -1));
            }
            for (int g = 0; g < tune.gearScale.Length; g++) rows.Add((null, g));
        }

        /// <summary>Leaving the tuning page keeps the tune on the car (and remeasures its PI).</summary>
        void SaveTune()
        {
            if (page != Page.Tuning || !tuneDirty) return;
            var car = Owned;
            car.Tune = TuneSpec.Snap(tune);
            tuneDirty = false;
            career.Remeasure(car);
            career.MarkDirty();
        }

        TuneGroup Unlocked => Parts.Unlocked(Owned.Parts);

        bool Open((TuneField f, int gear) r) => r.f != null ? (Unlocked & r.f.Group) != 0 : (Unlocked & TuneGroup.GearRatios) != 0;

        static string Needs((TuneField f, int gear) r)
        {
            var g = r.f != null ? r.f.Group : TuneGroup.GearRatios;
            return g switch
            {
                TuneGroup.Gearing => "a sport transmission",
                TuneGroup.GearRatios => "a race transmission",
                TuneGroup.Springs or TuneGroup.AntiRoll => "sport suspension",
                TuneGroup.Damping or TuneGroup.Alignment => "race suspension",
                TuneGroup.Aero => "an aero kit",
                TuneGroup.Brakes => "race brakes",
                TuneGroup.Differential => "a sport differential",
                _ => "a part",
            };
        }

        void TickTuning(DriverInput.MenuKeys m, bool left, bool right)
        {
            if (autoTask != null)
            {
                if (!autoTask.IsCompleted) return; // measuring; the screen says so
                if (autoTask.IsFaulted) career.Say("The auto-tune failed: " + autoTask.Exception?.GetBaseException().Message);
                else
                {
                    tune = autoTask.Result;
                    tuneDirty = true;
                    career.Say($"Tuned for {autoFor} by measurement. Enter drives it on the test track.");
                }
                autoTask = null;
            }
            int n = rows.Count;
            if (m.up) row = (row + n - 1) % n;
            if (m.down) row = (row + 1) % n;
            var r = rows[row];
            if (left || right)
            {
                if (Open(r)) Adjust(r, left ? -1 : 1);
                else career.Say($"This needs {Needs(r)} (Upgrades).");
            }
            if (m.back) { Go(Page.Cars); return; }
            if (m.select)
            {
                Go(Page.Test);
                testReturn = Page.Tuning;
                RunTest();
            }
        }

        void Adjust((TuneField f, int gear) r, int dir)
        {
            if (r.f != null) r.f.Set(tune, 0, r.f.Snap(r.f.Get(tune) + dir * r.f.Step));
            else
            {
                double g = tune.gearScale[r.gear] + dir * TuneSpec.GearStep;
                tune.gearScale[r.gear] = Math.Max(TuneSpec.GearMin, Math.Min(TuneSpec.GearMax, Math.Round(g / TuneSpec.GearStep) * TuneSpec.GearStep));
            }
            tuneDirty = true;
        }

        void StartAuto(TuneFor kind)
        {
            if (autoTask != null) return;
            var car = Owned;
            var spec = car.Spec;
            var up = car.Upgrades;
            var unlocked = Unlocked;
            autoFor = kind.ToString().ToLowerInvariant();
            autoTask = Task.Run(() => TuneSpec.AutoTune(spec, up, unlocked, kind));
        }

        void CopyCode()
        {
            string code = TuneSpec.Encode(Owned.Spec, TuneSpec.Snap(tune));
            GUIUtility.systemCopyBuffer = code;
            career.Say("Tune code copied: " + code);
        }

        void PasteCode()
        {
            string code = (GUIUtility.systemCopyBuffer ?? "").Trim();
            if (!TuneSpec.Decode(code, out var spec, out var t, out string why)) { career.Say("Not a tune code: " + why + "."); return; }
            if (spec.id != Owned.Id) { career.Say($"That tune is for the {CarVisual.LoadStyle(spec.id).name}."); return; }
            tune = TuneSpec.ForParts(spec, t, Unlocked, out int locked);
            tuneDirty = true;
            career.Say(locked == 0 ? "Tune pasted." : $"Tune pasted. {locked} setting{(locked == 1 ? " needs parts" : "s need parts")} this car does not have, so they stay at default.");
        }

        void ResetTune()
        {
            tune = TuneSpec.Snap(Tune.Default(Owned.Spec));
            tuneDirty = true;
            career.Say("Tune reset to the car's defaults.");
        }

        void TuningKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || autoTask != null) return;
            switch (e.keyCode)
            {
                case KeyCode.Alpha1: StartAuto(TuneFor.Road); e.Use(); break;
                case KeyCode.Alpha2: StartAuto(TuneFor.Circuit); e.Use(); break;
                case KeyCode.Alpha3: StartAuto(TuneFor.Dirt); e.Use(); break;
                case KeyCode.Alpha4: StartAuto(TuneFor.Drag); e.Use(); break;
                case KeyCode.C: CopyCode(); e.Use(); break;
                case KeyCode.V: PasteCode(); e.Use(); break;
                case KeyCode.R: ResetTune(); e.Use(); break;
            }
        }

        string ValueText((TuneField f, int gear) r)
        {
            if (r.f == null) return $"x{tune.gearScale[r.gear]:0.00}";
            double v = r.f.Get(tune);
            string num = r.f.Step >= 0.1 ? v.ToString("0.0") : r.f.Step >= 0.01 ? v.ToString("0.00") : v.ToString("0.000");
            return r.f.Unit == "x" ? "x" + num : num + (string.IsNullOrEmpty(r.f.Unit) ? "" : " " + r.f.Unit);
        }

        void DrawTuning()
        {
            TuningKeys();
            var car = Owned;
            var r = new Rect(24, 24, 600, Ui.H - 120);
            Ui.Panel(r);
            CarHeader(r, car.Spec, "Tuning");
            // A scrolling list that keeps the selected slider in view.
            int visible = Mathf.FloorToInt((r.height - 120) / 29);
            scroll = Mathf.Clamp(scroll, row - visible + 1, row);
            scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, rows.Count - visible));
            float y = r.y + 104;
            for (int i = scroll; i < Mathf.Min(rows.Count, scroll + visible); i++)
            {
                var it = rows[i];
                string name = it.f != null ? it.f.Label : $"Gear {it.gear + 1} ratio";
                bool open = Open(it);
                string value = open ? $"◀ {ValueText(it)} ▶" : "locked";
                if (Ui.Item(new Rect(r.x + 8, y, r.width - 16, 27), name, i == row, value, open)) row = i;
                y += 29;
            }
            // Details of the selected slider.
            var sel = rows[row];
            var d = new Rect(r.xMax + 16, 24, 600, 130);
            Ui.Panel(d);
            if (sel.f != null)
            {
                GUI.Label(new Rect(d.x + 18, d.y + 12, d.width - 36, 30), $"{sel.f.Label}: {ValueText(sel)}", Ui.Sub);
                GUI.Label(new Rect(d.x + 18, d.y + 46, d.width - 36, 50), sel.f.Formula, Ui.Small);
                GUI.Label(new Rect(d.x + 18, d.y + 96, d.width - 36, 26), Open(sel) ? $"Range {sel.f.Min:0.###} to {sel.f.Max:0.###}, step {sel.f.Step:0.###}" : $"Needs {Needs(sel)}.", Ui.Small);
            }
            else
            {
                GUI.Label(new Rect(d.x + 18, d.y + 12, d.width - 36, 30), $"Gear {sel.gear + 1}: {ValueText(sel)}", Ui.Sub);
                GUI.Label(new Rect(d.x + 18, d.y + 46, d.width - 36, 50), "This gear's ratio x this; shift points are recomputed from the torque curve.", Ui.Small);
                GUI.Label(new Rect(d.x + 18, d.y + 96, d.width - 36, 26), Open(sel) ? $"Range x{TuneSpec.GearMin:0.00} to x{TuneSpec.GearMax:0.00}" : $"Needs {Needs(sel)}.", Ui.Small);
            }
            var p = VehicleParams.Build(car.Spec, tune, car.Upgrades);
            DrawPower(new Rect(d.x, d.yMax + 12, 600, 200), p);
            DrawGears(new Rect(d.x, d.yMax + 224, 600, 200), p);
            DrawBalance(new Rect(d.x, d.yMax + 436, 600, 150), p);
            // Buttons for the mouse; the keys are in the hint.
            float bx = d.x, by = d.yMax + 598;
            foreach (var (label, kind) in new[] { ("Road", TuneFor.Road), ("Circuit", TuneFor.Circuit), ("Dirt", TuneFor.Dirt), ("Drag", TuneFor.Drag) })
            {
                if (GUI.Button(new Rect(bx, by, 140, 34), "Auto: " + label)) StartAuto(kind);
                bx += 150;
            }
            if (autoTask != null)
            {
                Ui.Panel(new Rect(Ui.W / 2 - 300, Ui.H / 2 - 40, 600, 80), 0.85f);
                GUI.Label(new Rect(Ui.W / 2 - 280, Ui.H / 2 - 18, 560, 40), $"Tuning for {autoFor} by measurement…", Ui.Sub);
            }
            Hint("↑/↓ setting   ←/→ adjust   1-4 auto-tune road/circuit/dirt/drag   C copy code   V paste code   R reset   Enter test track   Esc back");
        }

        void DrawPower(Rect r, VehicleParams p)
        {
            var e = p.engine;
            string key = $"{Owned.Id}|{e.peakPower}|{e.limiter}";
            if (powerPlot == null) powerPlot = new Plot(560, 150);
            if (key != powerKey)
            {
                powerKey = key;
                powerPlot.Clear(5, 4);
                double maxT = 1, maxP = 1;
                for (double rpm = e.idle; rpm <= e.limiter; rpm += 50)
                {
                    double tq = EngineParams.CurveTorque(e, rpm);
                    maxT = Math.Max(maxT, tq);
                    maxP = Math.Max(maxP, tq * rpm * Math.PI / 30);
                }
                var dom = new Rect(0, 0, (float)e.limiter, 1.08f);
                double prevT = double.NaN, prevP = double.NaN, prevR = 0;
                for (double rpm = e.idle; rpm <= e.limiter; rpm += 50)
                {
                    double tq = EngineParams.CurveTorque(e, rpm) / maxT;
                    double pw = EngineParams.CurveTorque(e, rpm) * rpm * Math.PI / 30 / maxP;
                    if (!double.IsNaN(prevT))
                    {
                        powerPlot.Line(prevR, prevT, rpm, tq, dom, new Color32(110, 190, 255, 255));
                        powerPlot.Line(prevR, prevP, rpm, pw, dom, new Color32(255, 150, 60, 255));
                    }
                    prevT = tq;
                    prevP = pw;
                    prevR = rpm;
                }
                powerPlot.Apply();
            }
            Ui.Panel(r);
            powerPlot.Draw(new Rect(r.x + 20, r.y + 36, 560, 150));
            GUI.Label(new Rect(r.x + 20, r.y + 6, r.width - 40, 28),
                $"<color=#ff9a3c>Power {e.peakPower / 1000:0} kW</color> at {e.peakPowerRpm:0} rpm · <color=#6ebeff>torque {e.peakTorque:0} N·m</color> at {e.peakTorqueRpm:0} rpm · to {e.limiter:0} rpm", Ui.Small);
        }

        void DrawGears(Rect r, VehicleParams p)
        {
            var g = p.gearbox;
            var e = p.engine;
            double radius = p.wheels[2].radius;
            int n = g.ratios.Length - 1;
            double top = 0;
            for (int i = 1; i <= n; i++) top = Math.Max(top, e.limiter * Math.PI / 30 * radius / Math.Abs(g.ratios[i] * g.final) * 3.6);
            string key = $"{Owned.Id}|{g.final}|{string.Join(",", g.ratios)}";
            if (gearPlot == null) gearPlot = new Plot(560, 150);
            if (key != gearKey)
            {
                gearKey = key;
                gearPlot.Clear(6, 4);
                var dom = new Rect(0, 0, (float)(top * 1.05), (float)(e.limiter * 1.05));
                for (int i = 1; i <= n; i++)
                {
                    double vmax = e.limiter * Math.PI / 30 * radius / Math.Abs(g.ratios[i] * g.final) * 3.6;
                    byte shade = (byte)(140 + 110 * i / Math.Max(1, n));
                    gearPlot.Line(0, 0, vmax, e.limiter, dom, new Color32(shade, (byte)(255 - shade / 3), 120, 255));
                }
                gearPlot.Line(0, e.redline, top * 1.05, e.redline, dom, new Color32(255, 80, 70, 160));
                gearPlot.Apply();
            }
            Ui.Panel(r);
            gearPlot.Draw(new Rect(r.x + 20, r.y + 36, 560, 150));
            GUI.Label(new Rect(r.x + 20, r.y + 6, r.width - 40, 28), $"Gears: road speed (across, to {top:0} km/h) against engine speed (up, red line at {e.redline:0} rpm) · final drive {g.final:0.00}", Ui.Small);
        }

        void DrawBalance(Rect r, VehicleParams p)
        {
            Ui.Panel(r);
            var spec = Owned.Spec;
            float x = r.x + 210, w = r.width - 240;
            void BarRow(float y, string label, double front, string note)
            {
                GUI.Label(new Rect(r.x + 20, y - 6, 190, 26), label, Ui.Small);
                Ui.Bar(new Rect(x, y, w, 12), 0, 1, front, Ui.Accent, 0.5);
                GUI.Label(new Rect(x, y + 12, w, 22), note, Ui.Small);
            }
            BarRow(r.y + 18, "Weight", spec.weightFront, $"{spec.weightFront * 100:0}% front");
            if (p.aero.clA > 0.01) BarRow(r.y + 62, "Downforce", p.aero.balance, $"{p.aero.balance * 100:0}% front · ClA {p.aero.clA:0.00} m²");
            else GUI.Label(new Rect(r.x + 20, r.y + 56, 500, 26), "Downforce: none (an aero kit adds it)", Ui.Small);
            BarRow(r.y + 106, "Brakes", p.brakeBias, $"{p.brakeBias * 100:0}% front");
        }

        // --- Test track ------------------------------------------------------------------------------
        TestTrackResult testResult, testPrev;
        Task<TestTrackResult> testTask;
        string testCar;
        Page testReturn = Page.Tuning;

        void RunTest()
        {
            if (testTask != null && !testTask.IsCompleted) return;
            var car = Owned;
            if (testCar != car.Id)
            {
                testResult = testPrev = null;
                testCar = car.Id;
            }
            var spec = car.Spec;
            var t = car.TuneOrDefault.Clone();
            var up = car.Upgrades;
            testTask = Task.Run(() => TestTrack.Run(spec, t, up));
        }

        void TickTest(DriverInput.MenuKeys m)
        {
            if (testTask != null && testTask.IsCompleted)
            {
                if (testTask.IsFaulted) career.Say("The test failed: " + testTask.Exception?.GetBaseException().Message);
                else
                {
                    testPrev = testResult;
                    testResult = testTask.Result;
                }
                testTask = null;
            }
            if (m.select) RunTest();
            if (m.back) Go(testReturn);
        }

        void DrawTest()
        {
            var car = Owned;
            var r = new Rect(24, 24, 700, 470);
            Ui.Panel(r);
            CarHeader(r, car.Spec, "Test track");
            if (testResult == null)
            {
                GUI.Label(new Rect(r.x + 18, r.y + 110, r.width - 36, 30), "Driving the test track…", Ui.Body);
                Hint(testReturn == Page.Tuning ? "Esc back to tuning" : "Esc back");
                return;
            }
            var a = testResult;
            var b = testPrev;
            float y = r.y + 108;
            void Line(string label, string now, double delta, bool lowerIsBetter, string unit, string fmt)
            {
                GUI.Label(new Rect(r.x + 18, y, 240, 30), label, Ui.Body);
                GUI.Label(new Rect(r.x + 260, y, 200, 30), now, Ui.Sub);
                if (b != null && !double.IsNaN(delta) && Math.Abs(delta) > 1e-9)
                {
                    bool better = lowerIsBetter ? delta < 0 : delta > 0;
                    var old = GUI.color;
                    GUI.color = better ? Ui.Good : Ui.Bad;
                    GUI.Label(new Rect(r.x + 470, y, 220, 30), Ui.Signed(delta, fmt) + unit, Ui.Body);
                    GUI.color = old;
                }
                y += 40;
            }
            Line("0–100 km/h", $"{a.ZeroTo100:0.00} s", b == null ? double.NaN : a.ZeroTo100 - b.ZeroTo100, true, " s", "0.00");
            Line("Top speed", $"{a.TopSpeed:0} km/h", b == null ? double.NaN : a.TopSpeed - b.TopSpeed, false, " km/h", "0");
            Line("100–0 km/h", $"{a.Braking:0.0} m", b == null ? double.NaN : a.Braking - b.Braking, true, " m", "0.0");
            Line("Lateral grip", $"{a.LateralG:0.00} g", b == null ? double.NaN : a.LateralG - b.LateralG, false, " g", "0.00");
            string us = double.IsNaN(a.Understeer) ? "spins" : $"{a.Understeer:+0.00;-0.00} deg/g";
            Line("Understeer", us, b == null || double.IsNaN(a.Understeer) || double.IsNaN(b.Understeer) ? double.NaN : a.Understeer - b.Understeer, false, "", "+0.00;-0.00");
            Line("Performance index", Performance.Label(a.Stats), b == null ? double.NaN : a.Stats.Pi - b.Stats.Pi, false, "", "0");
            string balance = double.IsNaN(a.Understeer) ? "The car spins on a 60 m circle at 0.6 of its grip: too much oversteer to measure. Soften the rear or stiffen the front."
                : a.Understeer < 0 ? "Oversteer: the rear lets go first. Fast to rotate, hard to hold."
                : a.Understeer < 0.3 ? "Nearly neutral: quick, and it needs care at the limit."
                : a.Understeer < 1.0 ? "Mild understeer: stable and predictable."
                : "Strong understeer: the front washes wide.";
            GUI.Label(new Rect(r.x + 18, y + 4, r.width - 36, 60), balance, Ui.Small);
            if (testTask != null) GUI.Label(new Rect(r.x + 18, r.yMax - 34, r.width - 36, 28), "Running again…", Ui.Small);
            Hint(testReturn == Page.Tuning ? "Enter run again   Esc back to tuning" : "Enter run again   Esc back");
        }
    }
}
