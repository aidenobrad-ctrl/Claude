// The pause menu (Esc / Start): resume, settings, accolades, photo mode,
// the controls and quitting. Settings change as they are picked (left and
// right) and are saved as they change: graphics, units, the assists, the
// racing line, and the accessibility options (HUD size, colour-blind
// palettes, camera shake, motion blur, field of view, lightning flashes,
// the minimap and key hints) and the sound. The accolades page lists every
// accolade with how far along it is, and the career's numbers.
using System.Collections.Generic;
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public sealed class PauseMenu : MonoBehaviour
    {
        HalcyonGame game;
        public bool Open { get; private set; }
        enum Page { Main, Settings, Accolades, Controls }
        Page page;
        int pick;
        bool pausedBefore;

        static readonly string[] MainItems = { "Resume", "Settings", "Accolades", "Photo mode", "Controls", "Quit" };

        /// <summary>A settings row: its name, its value as shown, and a step left (-1) or right (+1). A row without Step is a heading.</summary>
        sealed class Row
        {
            public string Name;
            public System.Func<string> Value;
            public System.Action<int> Step;
            public string Help;
        }

        List<Row> rows;

        public static PauseMenu Create(HalcyonGame game, Transform parent)
        {
            var go = new GameObject("PauseMenu");
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<PauseMenu>();
            p.game = game;
            return p;
        }

        public void Show()
        {
            if (Open) return;
            Open = true;
            pausedBefore = game.Paused;
            game.Paused = true;
            page = Page.Main;
            pick = 0;
        }

        public void Hide()
        {
            if (!Open) return;
            Open = false;
            game.Paused = pausedBefore;
        }

        static string OnOff(bool b) => b ? "On" : "Off";
        static string Pct(double v) => $"{v * 100:0}%";
        static double Step(double v, int dir, double step, double lo, double hi) => System.Math.Round(System.Math.Max(lo, System.Math.Min(hi, v + dir * step)) / step) * step;
        static T Cycle<T>(T v, int dir) where T : System.Enum
        {
            var all = (T[])System.Enum.GetValues(typeof(T));
            int i = System.Array.IndexOf(all, v);
            return all[((i + dir) % all.Length + all.Length) % all.Length];
        }

        List<Row> Rows()
        {
            if (rows != null) return rows;
            Settings S() => game.Settings;
            rows = new List<Row>
            {
                new Row { Name = "Graphics" },
                new Row { Name = "Quality", Value = () => S().Graphics.ToString(), Step = d => S().Graphics = Cycle(S().Graphics, d), Help = "Shadows, detail, tree distance and render scale. Low for older machines, Ultra for the best." },
                new Row { Name = "Motion blur", Value = () => OnOff(S().MotionBlur), Step = d => S().MotionBlur = !S().MotionBlur, Help = "Blur at speed. Off if it makes you feel unwell." },
                new Row { Name = "Driving" },
                new Row { Name = "Units", Value = () => S().Imperial ? "mph, miles" : "km/h, km", Step = d => S().Imperial = !S().Imperial },
                new Row { Name = "Steering assist", Value = () => S().Steering == SteeringAssist.Off ? "Off (simulation)" : S().Steering.ToString(), Step = d => S().Steering = Cycle(S().Steering, d), Help = "Standard keeps keyboard steering smooth; Assisted also stops the front washing wide." },
                new Row { Name = "Gearbox", Value = () => S().Gearbox == GearboxMode.Auto ? "Automatic" : "Manual (E/Q)", Step = d => S().Gearbox = Cycle(S().Gearbox, d) },
                new Row { Name = "Anti-lock brakes", Value = () => OnOff(S().Abs), Step = d => S().Abs = !S().Abs },
                new Row { Name = "Traction control", Value = () => OnOff(S().Tcs), Step = d => S().Tcs = !S().Tcs },
                new Row { Name = "Stability control", Value = () => OnOff(S().Esc), Step = d => S().Esc = !S().Esc, Help = "Catches slides. Off for drifting." },
                new Row { Name = "Racing line", Value = () => S().Line == LineAssist.Braking ? "Braking only" : S().Line.ToString(), Step = d => S().Line = Cycle(S().Line, d), Help = "The line on the road in races, coloured where to brake for your car." },
                new Row { Name = "Display and accessibility" },
                new Row { Name = "HUD size", Value = () => Pct(S().HudScale), Step = d => S().HudScale = Step(S().HudScale, d, 0.05, 0.75, 1.5) },
                new Row { Name = "Colours", Value = () => S().Colours == ColourVision.Normal ? "Standard" : S().Colours == ColourVision.Tritanopia ? "Tritanopia (blue-yellow)" : $"{S().Colours} (red-green)", Step = d => S().Colours = Cycle(S().Colours, d), Help = "Ahead/behind and the racing line in colours that stay apart for colour-blind players." },
                new Row { Name = "Camera shake", Value = () => Pct(S().Shake), Step = d => S().Shake = Step(S().Shake, d, 0.1, 0, 1) },
                new Row { Name = "Field of view", Value = () => $"{S().Fov:0}°", Step = d => S().Fov = Step(S().Fov, d, 2, 50, 80) },
                new Row { Name = "Reduce flashes", Value = () => OnOff(S().ReduceFlashes), Step = d => S().ReduceFlashes = !S().ReduceFlashes, Help = "Lightning lights the sky a quarter as brightly." },
                new Row { Name = "Minimap", Value = () => OnOff(S().Minimap), Step = d => S().Minimap = !S().Minimap },
                new Row { Name = "Key hints", Value = () => OnOff(S().KeyHints), Step = d => S().KeyHints = !S().KeyHints },
                new Row { Name = "Sound" },
                new Row { Name = "Master volume", Value = () => Pct(S().Master), Step = d => S().Master = Step(S().Master, d, 0.1, 0, 1) },
                new Row { Name = "Engines", Value = () => Pct(S().Engine), Step = d => S().Engine = Step(S().Engine, d, 0.1, 0, 1) },
                new Row { Name = "Effects", Value = () => Pct(S().Effects), Step = d => S().Effects = Step(S().Effects, d, 0.1, 0, 1), Help = "Tires, wind, impacts and the engines." },
                new Row { Name = "Weather sounds", Value = () => Pct(S().Ambience), Step = d => S().Ambience = Step(S().Ambience, d, 0.1, 0, 1) },
                new Row { Name = "Radio", Value = () => GameSound.StationName(S().Station), Step = d => S().Station = Cycle(S().Station, d), Help = "Three stations that write their own music. N (or the d-pad right) changes station while driving." },
                new Row { Name = "Music volume", Value = () => Pct(S().Music), Step = d => S().Music = Step(S().Music, d, 0.1, 0, 1) },
            };
            return rows;
        }

        /// <summary>The menu keys while the pause menu is open (called by the game after it polls input).</summary>
        public void Tick()
        {
            var m = game.Driver.Menu;
            switch (page)
            {
                case Page.Main:
                    if (m.back)
                    {
                        Hide();
                        return;
                    }
                    if (m.up) pick = (pick + MainItems.Length - 1) % MainItems.Length;
                    if (m.down) pick = (pick + 1) % MainItems.Length;
                    if (m.select) Choose(pick);
                    break;
                case Page.Settings:
                    var list = Rows();
                    if (m.back)
                    {
                        Back(1);
                        return;
                    }
                    if (m.up) pick = NextRow(list, pick, -1);
                    if (m.down) pick = NextRow(list, pick, 1);
                    if ((m.left || m.right || m.select) && list[pick].Step != null)
                    {
                        list[pick].Step(m.left ? -1 : 1);
                        game.ApplySettings();
                        game.SaveSettings();
                    }
                    break;
                case Page.Accolades:
                    if (m.back) Back(2);
                    if (m.up) pick = Mathf.Max(0, pick - 1);
                    if (m.down) pick = Mathf.Min(Accolades.All.Count - 1, pick + 1);
                    break;
                default:
                    if (m.back || m.select) Back(4);
                    break;
            }
        }

        void Back(int item)
        {
            page = Page.Main;
            pick = item;
        }

        static int NextRow(List<Row> list, int from, int dir)
        {
            int i = from;
            for (int k = 0; k < list.Count; k++)
            {
                i = (i + dir + list.Count) % list.Count;
                if (list[i].Step != null) return i;
            }
            return from;
        }

        void Choose(int item)
        {
            switch (item)
            {
                case 0: Hide(); break;
                case 1:
                    page = Page.Settings;
                    pick = NextRow(Rows(), -1 + Rows().Count, 1);
                    break;
                case 2:
                    page = Page.Accolades;
                    pick = 0;
                    break;
                case 3:
                    Hide();
                    game.StartPhotoMode();
                    break;
                case 4:
                    page = Page.Controls;
                    break;
                default:
                    game.Career.SaveNow();
                    game.SaveSettings();
                    Application.Quit();
                    break;
            }
        }

        void OnGUI()
        {
            if (!Open) return;
            Ui.Begin();
            Ui.Fill(new Rect(0, 0, Ui.W, Ui.H), new Color(0, 0, 0, 0.35f));
            switch (page)
            {
                case Page.Main: DrawMain(); break;
                case Page.Settings: DrawSettings(); break;
                case Page.Accolades: DrawAccolades(); break;
                default: DrawControls(); break;
            }
            Ui.End();
        }

        void DrawMain()
        {
            float w = 520, h = 140 + MainItems.Length * 56;
            var r = new Rect(Ui.W / 2 - w / 2, Ui.H / 2 - h / 2, w, h);
            Ui.Panel(r, 0.82f);
            GUI.Label(new Rect(r.x + 30, r.y + 20, w - 60, 48), "Paused", Ui.Title);
            var p = game.Career.Profile;
            var (level, into, need) = Economy.Level(p.Xp);
            GUI.Label(new Rect(r.x + 30, r.y + 20, w - 60, 48), $"Level {level}", Ui.Value);
            Ui.Bar(new Rect(r.x + 30, r.y + 74, w - 60, 6), 0, need, into, Ui.Accent);
            for (int i = 0; i < MainItems.Length; i++)
                if (Ui.Item(new Rect(r.x + 16, r.y + 96 + i * 56, w - 32, 50), MainItems[i], i == pick))
                {
                    pick = i;
                    Choose(i);
                }
            GUI.Label(new Rect(r.x + 30, r.yMax - 40, w - 60, 28), "↑/↓ choose · Enter pick · Esc resume", Ui.Small);
        }

        void DrawSettings()
        {
            var list = Rows();
            float w = Mathf.Min(1000, Ui.W - 80), h = Ui.H - 120;
            var r = new Rect(Ui.W / 2 - w / 2, 60, w, h);
            Ui.Panel(r, 0.85f);
            GUI.Label(new Rect(r.x + 30, r.y + 16, w - 60, 48), "Settings", Ui.Title);
            const float RowH = 40;
            float top = r.y + 76;
            int fit = Mathf.Max(1, (int)((h - 170) / RowH));
            int first = Mathf.Clamp(pick - fit / 2, 0, Mathf.Max(0, list.Count - fit));
            for (int i = first; i < list.Count && i < first + fit; i++)
            {
                var row = list[i];
                var rr = new Rect(r.x + 16, top + (i - first) * RowH, w - 32, RowH - 4);
                if (row.Step == null)
                {
                    GUI.Label(new Rect(rr.x + 8, rr.y + 6, rr.width, rr.height), row.Name, Ui.Sub);
                    continue;
                }
                if (Ui.Item(rr, row.Name, i == pick, $"‹  {row.Value()}  ›"))
                {
                    pick = i;
                    row.Step(1);
                    game.ApplySettings();
                    game.SaveSettings();
                }
            }
            string help = list[pick].Help ?? "";
            GUI.Label(new Rect(r.x + 30, r.yMax - 88, w - 60, 48), help, Ui.Small);
            GUI.Label(new Rect(r.x + 30, r.yMax - 40, w - 60, 28), "↑/↓ choose · ←/→ change · Esc back", Ui.Small);
        }

        void DrawAccolades()
        {
            var p = game.Career.Profile;
            var all = Accolades.All;
            float w = Mathf.Min(1100, Ui.W - 80), h = Ui.H - 120;
            var r = new Rect(Ui.W / 2 - w / 2, 60, w, h);
            Ui.Panel(r, 0.85f);
            GUI.Label(new Rect(r.x + 30, r.y + 16, w - 60, 48), $"Accolades  {p.Accolades.Count} / {all.Count}", Ui.Title);
            // The career in numbers.
            p.Stats.TryGetValue(Accolades.Podiums, out int podiums);
            int stars = 0;
            if (game.Stunts != null)
                foreach (var st in game.Stunts.All)
                {
                    double b = game.Stunts.Best(st);
                    if (b > 0) stars += st.StarsFor(b);
                }
            string roads = game.Discover != null ? $" · {game.Discover.RoadShare * 100:0}% of roads driven" : "";
            GUI.Label(new Rect(r.x + 30, r.y + 66, w - 60, 28), $"{p.Races} races · {p.Wins} wins · {podiums} podiums · {p.ChampionshipsWon.Count} championships · {stars} stunt stars{roads}", Ui.Small);
            const float RowH = 58;
            float top = r.y + 104;
            int fit = Mathf.Max(1, (int)((h - 160) / RowH));
            int first = Mathf.Clamp(pick - fit / 2, 0, Mathf.Max(0, all.Count - fit));
            for (int i = first; i < all.Count && i < first + fit; i++)
            {
                var a = all[i];
                var rr = new Rect(r.x + 16, top + (i - first) * RowH, w - 32, RowH - 6);
                bool got = p.Accolades.Contains(a.Id);
                if (i == pick) Ui.Fill(rr, new Color(1f, 0.55f, 0.2f, 0.25f));
                var old = GUI.color;
                GUI.color = got ? Color.white : new Color(1, 1, 1, 0.7f);
                GUI.Label(new Rect(rr.x + 14, rr.y + 2, rr.width - 260, 28), (got ? "✓ " : "") + a.Name, Ui.RowOn);
                GUI.Label(new Rect(rr.x + 14, rr.y + 28, rr.width - 260, 24), a.Description, Ui.Small);
                GUI.Label(new Rect(rr.x, rr.y + 2, rr.width - 14, 28), $"{a.Xp:N0} XP", Ui.Value);
                double prog = Accolades.ProgressOf(a, p, game.Discover, game.Stunts);
                Ui.Bar(new Rect(rr.xMax - 230, rr.y + 36, 216, 6), 0, 1, prog, got ? Ui.Good : Ui.Accent);
                GUI.color = old;
            }
            GUI.Label(new Rect(r.x + 30, r.yMax - 40, w - 60, 28), "↑/↓ scroll · Esc back", Ui.Small);
        }

        static readonly string[,] Keys =
        {
            { "Throttle / brake", "W / S or ↑ / ↓", "RT / LT" },
            { "Steer", "A / D or ← / →", "Left stick" },
            { "Handbrake", "Space", "A" },
            { "Shift up / down (manual)", "E / Q", "B / X" },
            { "Reset to the road", "R", "Back" },
            { "Camera / look back", "C / B", "Y / RB" },
            { "Headlights", "L", "" },
            { "Garage", "G", "LB" },
            { "Festival events", "T (hold in a race to leave)", "D-pad up" },
            { "Map (GPS, fast travel)", "M", "D-pad down" },
            { "Photo mode", "P", "Right stick click" },
            { "Hour back / forward, weather", "[ ] and \\", "" },
            { "Pause", "Esc", "Start" },
        };

        void DrawControls()
        {
            int n = Keys.GetLength(0);
            float w = Mathf.Min(1000, Ui.W - 80), h = 150 + n * 40;
            var r = new Rect(Ui.W / 2 - w / 2, Ui.H / 2 - h / 2, w, h);
            Ui.Panel(r, 0.85f);
            GUI.Label(new Rect(r.x + 30, r.y + 16, w - 60, 48), "Controls", Ui.Title);
            GUI.Label(new Rect(r.x + 420, r.y + 70, 300, 28), "Keyboard", Ui.Sub);
            GUI.Label(new Rect(r.x + 730, r.y + 70, 260, 28), "Gamepad", Ui.Sub);
            for (int i = 0; i < n; i++)
            {
                float y = r.y + 104 + i * 40;
                GUI.Label(new Rect(r.x + 30, y, 380, 34), Keys[i, 0], Ui.Row);
                GUI.Label(new Rect(r.x + 420, y, 300, 34), Keys[i, 1], Ui.Row);
                GUI.Label(new Rect(r.x + 730, y, 260, 34), Keys[i, 2], Ui.Row);
            }
            GUI.Label(new Rect(r.x + 30, r.yMax - 40, w - 60, 28), "Esc back", Ui.Small);
        }
    }
}
