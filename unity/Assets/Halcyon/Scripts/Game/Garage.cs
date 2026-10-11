// The garage, on a turntable on the festival stage (G / LB). Pages:
//   My cars   the cars you own: drive, upgrade, tune, test, paint, sell
//   Dealer    every car with its PI, class and price
//   Upgrades  parts by category, with the PI before and after, measured
//   Tuning    every slider the parts unlock, with live graphs, auto-tune
//             and share codes (Workshop.cs)
//   Test      the tuning test track (Workshop.cs)
//   Career    save location, export and import
// Keys: arrows / WASD / d-pad move, Enter / A picks, Esc / B goes back. The
// mouse works too. Every figure is measured by the same Core tests as
// docs/CARS.md, on worker threads.
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public sealed partial class Garage : MonoBehaviour
    {
        public bool IsOpen { get; private set; }

        enum Page { Cars, Dealer, Upgrades, Tuning, Test, Career }

        HalcyonGame game;
        Career career;
        Transform turntable;
        CarVisual display;
        string displayKey;
        Page page;
        int carIndex, dealerIndex, row, scroll;
        float spin, confirmUntil;
        string confirmWhat;
        Vector3 top, fw, rt;

        static readonly (string name, Color c)[] Paints =
        {
            ("Festival red", new Color(0.78f, 0.12f, 0.14f)),
            ("Sunrise orange", new Color(0.95f, 0.45f, 0.08f)),
            ("Lemon", new Color(0.93f, 0.78f, 0.12f)),
            ("Lagoon", new Color(0.05f, 0.66f, 0.64f)),
            ("Deep blue", new Color(0.08f, 0.22f, 0.62f)),
            ("Racing green", new Color(0.08f, 0.32f, 0.18f)),
            ("Pearl white", new Color(0.92f, 0.92f, 0.9f)),
            ("Graphite", new Color(0.22f, 0.23f, 0.25f)),
            ("Midnight", new Color(0.03f, 0.035f, 0.045f)),
            ("Magenta", new Color(0.78f, 0.12f, 0.45f)),
        };

        public static Garage Create(HalcyonGame game, WorldData data, Transform parent)
        {
            var go = new GameObject("Garage");
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<Garage>();
            g.game = game;
            g.career = game.Career;
            g.Build(data);
            return g;
        }

        void Build(WorldData data)
        {
            var s = data.Festival.stage;
            fw = Frame.Dir(s.fx, 0, s.fz);
            fw.y = 0;
            fw.Normalize();
            rt = Vector3.Cross(Vector3.up, fw);
            // On the stage platform (1.4 m high), a little forward of its centre.
            top = Frame.Pos(s.x, s.y, s.z) + Vector3.up * 1.4f + fw * 2.5f;
            var mb = new MeshBuilder(2);
            const int n = 64;
            const float R = 3.4f, H = 0.12f, Rin = R - 0.12f;
            var up = Vector3.up;
            int centre = mb.Add(up * H, up, new Vector2(0.5f, 0.5f));
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2 / n, a1 = (i + 1) * Mathf.PI * 2 / n;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                // Dark disc, a glowing rim on top, and the side.
                mb.Tri(0, centre, mb.Add(d1 * Rin + up * H, up, Vector2.zero), mb.Add(d0 * Rin + up * H, up, Vector2.zero));
                mb.Quad(1, mb.Add(d0 * Rin + up * H, up, Vector2.zero), mb.Add(d1 * Rin + up * H, up, Vector2.zero), mb.Add(d1 * R + up * H, up, Vector2.zero), mb.Add(d0 * R + up * H, up, Vector2.zero));
                mb.Quad(0, mb.Add(d0 * R + up * H, d0, Vector2.zero), mb.Add(d1 * R + up * H, d1, Vector2.zero), mb.Add(d1 * R, d1, Vector2.zero), mb.Add(d0 * R, d0, Vector2.zero));
            }
            var disc = Mats.Make("Turntable", new Color(0.05f, 0.05f, 0.055f), 0.85f, 0.4f);
            var rim = Mats.Make("TurntableRim", new Color(0.9f, 0.95f, 1f), 0.5f);
            Mats.SetEmission(rim, new Color(1f, 0.55f, 0.25f) * 2.5f);
            var go = mb.ToObject("Turntable", transform, new[] { disc, rim });
            turntable = go.transform;
            turntable.position = top;
            top += Vector3.up * H;
        }

        Profile P => career.Profile;
        OwnedCar Owned => P.Garage[Mathf.Clamp(carIndex, 0, P.Garage.Count - 1)];

        public void Open()
        {
            IsOpen = true;
            page = Page.Cars;
            carIndex = P.Current;
            row = 0;
            confirmWhat = null;
            ShowCar();
        }

        /// <summary>Leave the garage. The player drives the current car; if it was sold or changed, it is swapped in where it stands.</summary>
        public void Close()
        {
            SaveTune();
            IsOpen = false;
            if (display != null) Destroy(display.Root);
            display = null;
            displayKey = null;
            game.DriveCurrentCar();
        }

        // --- Measured stats, cached per car, parts and tune ------------------------------------
        readonly ConcurrentDictionary<string, Task<CarStats>> stats = new ConcurrentDictionary<string, Task<CarStats>>();

        Task<CarStats> StatsOf(string carId, IReadOnlyDictionary<PartCategory, int> parts, Tune tune)
        {
            var spec = Cars.ById(carId);
            string key = carId + "|" + Rivals.PartsKey(parts) + "|" + (tune == null ? "default" : TuneSpec.Encode(spec, tune));
            return stats.GetOrAdd(key, _ =>
            {
                var t = tune?.Clone();
                var up = Parts.Build(spec, parts);
                return Task.Run(() => Performance.Measure(spec, t, up));
            });
        }

        static string Label(Task<CarStats> t) => t.IsCompleted && !t.IsFaulted ? Performance.Label(t.Result) : "measuring…";

        // --- The car on the turntable ---------------------------------------------------------------
        void ShowCar()
        {
            string id;
            Color? paint = null;
            if (page == Page.Dealer) id = Cars.All[dealerIndex].id;
            else
            {
                id = Owned.Id;
                if (Owned.Paint != null) paint = new Color((float)Owned.Paint[0], (float)Owned.Paint[1], (float)Owned.Paint[2]);
            }
            string key = id + "|" + paint;
            if (key == displayKey) return;
            // A new colour on the same car: repaint it rather than load it again.
            if (display != null && displayKey != null && displayKey.StartsWith(id + "|"))
            {
                displayKey = key;
                display.SetPaint(paint ?? CarVisual.LoadStyle(id).paint);
                return;
            }
            displayKey = key;
            if (display != null) Destroy(display.Root);
            var spec = Cars.ById(id);
            display = CarVisual.Create(id, VehicleParams.Build(spec), paint);
            display.Root.transform.position = top;
            display.Root.transform.rotation = Quaternion.LookRotation(-fw) * Quaternion.Euler(0, spin, 0);
            display.SetLights(true, false);
        }

        /// <summary>Called by the game each frame while open, after it polls the input.</summary>
        public void Tick()
        {
            if (!IsOpen) return;
            var m = game.Driver.Menu;
            var held = game.Driver.MenuHeld;
            // Held left/right repeats (sliders, browsing).
            bool left = m.left || Repeat(held.left, ref leftHeld);
            bool right = m.right || Repeat(held.right, ref rightHeld);
            switch (page)
            {
                case Page.Cars: TickCars(m, left, right); break;
                case Page.Dealer: TickDealer(m, left, right); break;
                case Page.Upgrades: TickUpgrades(m, left, right); break;
                case Page.Tuning: TickTuning(m, left, right); break;
                case Page.Test: TickTest(m); break;
                case Page.Career: TickCareer(m); break;
            }
            if (!IsOpen) return;
            ShowCar();
            spin += Time.unscaledDeltaTime * 14f;
            turntable.rotation = Quaternion.Euler(0, spin, 0);
            if (display != null) display.Root.transform.rotation = Quaternion.LookRotation(-fw) * Quaternion.Euler(0, spin, 0);
        }

        float leftHeld, rightHeld;

        static bool Repeat(bool held, ref float t)
        {
            if (!held)
            {
                t = 0;
                return false;
            }
            float before = t;
            t += Time.unscaledDeltaTime;
            if (t < 0.4f) return false;
            // After 0.4 s, a step every 0.07 s.
            return Mathf.Floor((t - 0.4f) / 0.07f) > Mathf.Floor((before - 0.4f) / 0.07f) || before < 0.4f;
        }

        bool Confirm(string what)
        {
            if (confirmWhat == what && Time.unscaledTime < confirmUntil)
            {
                confirmWhat = null;
                return true;
            }
            confirmWhat = what;
            confirmUntil = Time.unscaledTime + 3f;
            return false;
        }

        void Go(Page p)
        {
            if (page == Page.Tuning) SaveTune();
            page = p;
            row = 0;
            scroll = 0;
            confirmWhat = null;
            if (p == Page.Tuning) OpenTuning();
            if (p == Page.Upgrades) OpenUpgrades();
        }

        // --- My cars -------------------------------------------------------------------------------
        static readonly string[] CarRows = { "Drive", "Upgrades", "Tuning", "Test track", "Paint", "Dealer", "Sell", "Career" };

        void TickCars(DriverInput.MenuKeys m, bool left, bool right)
        {
            int n = P.Garage.Count;
            if (m.up) row = (row + CarRows.Length - 1) % CarRows.Length;
            if (m.down) row = (row + 1) % CarRows.Length;
            if (CarRows[row] == "Paint")
            {
                if (left || right) CyclePaint(left ? -1 : 1);
            }
            else
            {
                if (left) carIndex = (carIndex + n - 1) % n;
                if (right) carIndex = (carIndex + 1) % n;
                if (left || right) confirmWhat = null;
            }
            if (m.back) { Close(); return; }
            if (m.select) ActivateCarRow(CarRows[row]);
        }

        void ActivateCarRow(string what)
        {
            switch (what)
            {
                case "Drive":
                    P.Current = carIndex;
                    career.MarkDirty();
                    Close();
                    break;
                case "Upgrades": Go(Page.Upgrades); break;
                case "Tuning": Go(Page.Tuning); break;
                case "Test track": Go(Page.Test); testReturn = Page.Cars; RunTest(); break;
                case "Paint": CyclePaint(1); break;
                case "Dealer":
                    dealerIndex = System.Array.FindIndex(Cars.All, c => c.id == Owned.Id);
                    Go(Page.Dealer);
                    break;
                case "Sell":
                    if (P.Garage.Count <= 1) { career.Say("You need at least one car."); break; }
                    int price = Economy.SellPrice(Mathf.Max(100, Owned.Pi));
                    if (!Confirm("sell")) { career.Say($"Sell the {CarVisual.LoadStyle(Owned.Id).name} for {Ui.Credits(price)}? Press again to confirm."); break; }
                    string name = CarVisual.LoadStyle(Owned.Id).name;
                    if (P.SellCar(carIndex, price, out string why))
                    {
                        carIndex = Mathf.Clamp(carIndex, 0, P.Garage.Count - 1);
                        career.MarkDirty();
                        career.Say($"Sold the {name} for {Ui.Credits(price)}.");
                    }
                    else career.Say("Can't sell: " + why);
                    break;
                case "Career": Go(Page.Career); break;
            }
        }

        void CyclePaint(int dir)
        {
            // Factory colour, then the presets.
            int cur = -1;
            if (Owned.Paint != null)
            {
                for (int i = 0; i < Paints.Length; i++)
                {
                    var c = Paints[i].c;
                    if (Mathf.Abs((float)Owned.Paint[0] - c.r) < 1e-3f && Mathf.Abs((float)Owned.Paint[1] - c.g) < 1e-3f && Mathf.Abs((float)Owned.Paint[2] - c.b) < 1e-3f) cur = i;
                }
            }
            int next = cur + dir;
            if (next >= Paints.Length) next = -1;
            if (next < -1) next = Paints.Length - 1;
            Owned.Paint = next < 0 ? null : new double[] { Paints[next].c.r, Paints[next].c.g, Paints[next].c.b };
            career.MarkDirty();
        }

        string PaintName()
        {
            if (Owned.Paint == null) return "Factory colour";
            foreach (var (name, c) in Paints)
            {
                if (Mathf.Abs((float)Owned.Paint[0] - c.r) < 1e-3f && Mathf.Abs((float)Owned.Paint[1] - c.g) < 1e-3f) return name;
            }
            return "Custom";
        }

        // --- Dealer --------------------------------------------------------------------------------
        void TickDealer(DriverInput.MenuKeys m, bool left, bool right)
        {
            int n = Cars.All.Length;
            if (left) { dealerIndex = (dealerIndex + n - 1) % n; confirmWhat = null; }
            if (right) { dealerIndex = (dealerIndex + 1) % n; confirmWhat = null; }
            if (m.back) { Go(Page.Cars); return; }
            if (m.select) Buy();
        }

        void Buy()
        {
            var spec = Cars.All[dealerIndex];
            var st = StatsOf(spec.id, null, null);
            if (!st.IsCompleted) { career.Say("Still measuring this car…"); return; }
            int price = Economy.CarPrice(st.Result.Pi);
            if (P.Credits < price) { career.Say($"You need {Ui.Credits(price - P.Credits)} more."); return; }
            string name = CarVisual.LoadStyle(spec.id).name;
            if (!Confirm("buy " + spec.id)) { career.Say($"Buy the {name} for {Ui.Credits(price)}? Press again to confirm."); return; }
            if (P.BuyCar(spec.id, price, out string why))
            {
                P.Garage[P.Garage.Count - 1].Pi = st.Result.Pi;
                carIndex = P.Garage.Count - 1;
                career.MarkDirty();
                career.Say($"The {name} is yours. It is now your current car.");
                Go(Page.Cars);
            }
            else career.Say("Can't buy: " + why);
        }

        // --- Career data ---------------------------------------------------------------------------
        static readonly string[] CareerRows = { "Copy career to clipboard", "Replace career from clipboard", "Back" };

        void TickCareer(DriverInput.MenuKeys m)
        {
            if (m.up) row = (row + CareerRows.Length - 1) % CareerRows.Length;
            if (m.down) row = (row + 1) % CareerRows.Length;
            if (m.back) { Go(Page.Cars); return; }
            if (!m.select) return;
            if (row == 0) career.ExportToClipboard();
            else if (row == 1)
            {
                if (!Confirm("import")) { career.Say("This replaces your whole career (the current save is kept as a backup). Press again to confirm."); return; }
                if (career.ImportFromClipboard(out string why))
                {
                    carIndex = P.Current;
                    displayKey = null;
                }
                else career.Say("Not imported: " + why);
            }
            else Go(Page.Cars);
        }

        // --- Camera ------------------------------------------------------------------------------------
        void LateUpdate()
        {
            if (!IsOpen) return;
            var cam = game.CameraRig.Cam;
            // The workshop pages frame the car to the right of the panels.
            bool side = page == Page.Upgrades || page == Page.Tuning || page == Page.Test;
            var at = top + Vector3.up * 0.65f + (side ? -rt * 1.6f : Vector3.zero);
            var eye = top + fw * 8.8f + rt * 2.6f + Vector3.up * 2.1f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye, Vector3.up));
            cam.fieldOfView = 30f;
        }

        // --- Drawing -----------------------------------------------------------------------------------
        void OnGUI()
        {
            if (!IsOpen) return;
            Ui.Begin();
            try
            {
                switch (page)
                {
                    case Page.Cars: DrawCars(); break;
                    case Page.Dealer: DrawDealer(); break;
                    case Page.Upgrades: DrawUpgrades(); break;
                    case Page.Tuning: DrawTuning(); break;
                    case Page.Test: DrawTest(); break;
                    case Page.Career: DrawCareer(); break;
                }
                DrawWallet();
                DrawNotice();
            }
            finally
            {
                Ui.End();
            }
        }

        void DrawWallet()
        {
            var (level, into, need) = Economy.Level(P.Xp);
            var r = new Rect(Ui.W - 430, 24, 400, 74);
            Ui.Panel(r);
            GUI.Label(new Rect(r.x + 16, r.y + 8, 370, 30), Ui.Credits(P.Credits), Ui.Sub);
            GUI.Label(new Rect(r.x + 16, r.y + 38, 370, 26), $"Level {level} · {into:N0} / {need:N0} XP · {P.Wins} wins in {P.Races} races", Ui.Small);
        }

        void DrawNotice()
        {
            if (career.Notice == null || Time.unscaledTime - career.NoticeAt > 5f) return;
            var r = new Rect(Ui.W / 2 - 420, Ui.H - 150, 840, 52);
            Ui.Panel(r, 0.75f);
            GUI.Label(new Rect(r.x + 16, r.y + 12, r.width - 32, 30), career.Notice, Ui.Body);
        }

        void Hint(string text) => GUI.Label(new Rect(0, Ui.H - 70, Ui.W, 40), text, Ui.Keys);

        void CarHeader(Rect r, CarSpec spec, string extra)
        {
            var style = CarVisual.LoadStyle(spec.id);
            GUI.Label(new Rect(r.x + 18, r.y + 12, r.width - 36, 48), style.name, Ui.Title);
            GUI.Label(new Rect(r.x + 18, r.y + 60, r.width - 36, 28), $"{spec.year} · {ClassName(spec.category)} · {extra}", Ui.Sub);
        }

        string Figures(CarSpec spec, Task<CarStats> st)
        {
            var p = VehicleParams.Build(spec);
            double kw = p.engine.peakPower / 1000.0;
            string engine = spec.engine.cylinders == 0 ? $"{spec.engine.layout} electric" : $"{spec.engine.displacement:0.0} L {spec.engine.layout} {Aspiration(spec.engine.aspiration)}";
            string lines =
                $"{engine}\n" +
                $"{kw:0} kW ({kw * 1.341:0} hp) · {p.engine.peakTorque:0} N·m · {spec.drivetrain}\n" +
                $"{spec.mass:0} kg · {spec.weightFront * 100:0}% front · {spec.tires.compound} tyres\n";
            if (st.IsCompleted && !st.IsFaulted)
            {
                var s = st.Result;
                lines += $"\n0–100 km/h  {s.ZeroTo100:0.00} s     Top speed  {s.TopSpeed:0} km/h\n100–0 km/h  {s.Braking:0.0} m     Skidpad  {s.Skidpad:0.00} g";
            }
            else lines += "\nMeasuring on the test track…";
            return lines;
        }

        void DrawCars()
        {
            var car = Owned;
            var spec = car.Spec;
            var st = StatsOf(car.Id, car.Parts, car.Tune);
            var r = new Rect(24, 24, 560, 560);
            Ui.Panel(r);
            CarHeader(r, spec, $"{Label(st)} · car {carIndex + 1} of {P.Garage.Count}{(carIndex == P.Current ? " · driving" : "")}");
            GUI.Label(new Rect(r.x + 18, r.y + 100, r.width - 36, 200), Figures(spec, st), Ui.Body);
            int fitted = 0;
            foreach (var kv in car.Parts) if (kv.Value > 0) fitted++;
            GUI.Label(new Rect(r.x + 18, r.y + 300, r.width - 36, 26), fitted == 0 ? "Stock parts" : $"{fitted} upgrade{(fitted == 1 ? "" : "s")} fitted" + (car.Tune != null ? " · tuned" : ""), Ui.Small);
            float y = r.y + 340;
            for (int i = 0; i < CarRows.Length; i++)
            {
                string value = CarRows[i] switch
                {
                    "Paint" => "◀ " + PaintName() + " ▶",
                    "Sell" => P.Garage.Count > 1 ? Ui.Credits(Economy.SellPrice(Mathf.Max(100, car.Pi))) : "your only car",
                    "Dealer" => "12 cars",
                    _ => null,
                };
                if (Ui.Item(new Rect(r.x + 8, y, r.width - 16, 26), CarRows[i], i == row, value))
                {
                    row = i;
                    ActivateCarRow(CarRows[i]);
                }
                y += 27;
            }
            Hint("←/→ car   ↑/↓ choose   Enter select   Esc leave the garage");
        }

        void DrawDealer()
        {
            var spec = Cars.All[dealerIndex];
            var st = StatsOf(spec.id, null, null);
            var r = new Rect(24, 24, 560, 470);
            Ui.Panel(r);
            CarHeader(r, spec, $"{Label(st)} · {dealerIndex + 1} of {Cars.All.Length}");
            GUI.Label(new Rect(r.x + 18, r.y + 100, r.width - 36, 200), Figures(spec, st), Ui.Body);
            GUI.Label(new Rect(r.x + 18, r.y + 290, r.width - 36, 80), spec.description, Ui.Small);
            string priceText = st.IsCompleted && !st.IsFaulted ? Ui.Credits(Economy.CarPrice(st.Result.Pi)) : "…";
            bool owned = P.Owns(spec.id);
            GUI.Label(new Rect(r.x + 18, r.y + 380, r.width - 36, 36), $"Price  {priceText}{(owned ? "   (you own one)" : "")}", Ui.Sub);
            if (Ui.Item(new Rect(r.x + 8, r.y + 420, r.width - 16, 30), "Buy", true)) Buy();
            Hint("←/→ car   Enter buy   Esc back");
        }

        void DrawCareer()
        {
            var r = new Rect(24, 24, 760, 300);
            Ui.Panel(r);
            GUI.Label(new Rect(r.x + 18, r.y + 12, r.width - 36, 48), "Career", Ui.Title);
            GUI.Label(new Rect(r.x + 18, r.y + 66, r.width - 36, 60), $"Saved automatically to\n{Career.DefaultPath}", Ui.Small);
            float y = r.y + 150;
            for (int i = 0; i < CareerRows.Length; i++)
            {
                if (Ui.Item(new Rect(r.x + 8, y, r.width - 16, 30), CareerRows[i], i == row))
                {
                    row = i;
                    TickCareer(new DriverInput.MenuKeys { select = true });
                }
                y += 34;
            }
            Hint("↑/↓ choose   Enter select   Esc back");
        }

        static string ClassName(Category c) => c switch
        {
            Category.Compact => "Compact",
            Category.HotHatch => "Hot hatch",
            Category.Classic => "Classic",
            Category.Muscle => "Muscle",
            Category.Sports => "Sports",
            Category.GT => "GT",
            Category.Super => "Super",
            Category.Hyper => "Hyper",
            Category.Rally => "Rally",
            Category.OffRoad => "Off-road",
            Category.Truck => "Truck",
            Category.SUV => "SUV",
            _ => c.ToString(),
        };

        static string Aspiration(Halcyon.Core.Aspiration a) => a switch
        {
            Halcyon.Core.Aspiration.TwinTurbo => "twin-turbo",
            Halcyon.Core.Aspiration.Turbo => "turbo",
            Halcyon.Core.Aspiration.Supercharged => "supercharged",
            _ => "",
        };
    }
}
