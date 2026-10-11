// Races in Unity: the event menu (T / Y), the grid of AI cars with their
// Blender models, the countdown, the race HUD (position, lap, times, gaps)
// and the results with the payout. The race itself is
// Halcyon.Core.RaceSession, the same code the headless AI tournaments run.
// Events have class limits; rivals are matched to the player's PI with parts
// (Halcyon.Core.Rivals), measured on worker threads while the grid lines up.
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public sealed class RaceController : MonoBehaviour
    {
        HalcyonGame game;
        List<RaceEvent> events;
        readonly RaceSession.AbilityCache abilities = new RaceSession.AbilityCache();
        readonly Dictionary<string, RacingLine> lines = new Dictionary<string, RacingLine>();
        readonly List<CarRig> rigs = new List<CarRig>();
        public RaceSession Session { get; private set; }
        public bool MenuOpen { get; private set; }
        public bool Busy { get; private set; }
        int pick, difficulty = (int)Difficulty.Pro;
        int seed = 1;
        int playerPi;
        bool paid;
        int paidCredits, paidXp;
        GUIStyle title, item, small, big, row;

        static readonly Color[] Liveries =
        {
            new Color(0.95f, 0.45f, 0.08f), new Color(0.05f, 0.66f, 0.64f), new Color(0.08f, 0.22f, 0.62f),
            new Color(0.93f, 0.78f, 0.12f), new Color(0.92f, 0.92f, 0.9f), new Color(0.78f, 0.12f, 0.45f),
            new Color(0.22f, 0.23f, 0.25f), new Color(0.08f, 0.32f, 0.18f), new Color(0.78f, 0.12f, 0.14f),
        };

        public static RaceController Create(HalcyonGame game, Transform parent)
        {
            var go = new GameObject("Races");
            go.transform.SetParent(parent, false);
            var rc = go.AddComponent<RaceController>();
            rc.game = game;
            return rc;
        }

        public bool Racing => Session != null;

        public void OpenMenu()
        {
            if (events == null) events = EventCatalog.All(game.Sim.World, game.Sim.Ground);
            MenuOpen = true;
            pick = Mathf.Clamp(pick, 0, events.Count - 1);
        }

        /// <summary>Menu keys while the event list is open (called by the game after it polls input).</summary>
        public void TickMenu()
        {
            var m = game.Driver.Menu;
            if (m.up) pick = (pick + events.Count - 1) % events.Count;
            if (m.down) pick = (pick + 1) % events.Count;
            if (m.left) difficulty = Mathf.Max(0, difficulty - 1);
            if (m.right) difficulty = Mathf.Min(3, difficulty + 1);
            if (m.back) MenuOpen = false;
            if (m.select)
            {
                var ev = events[pick];
                if (!Eligible(ev, out string why))
                {
                    game.Career.Say(why);
                    return;
                }
                MenuOpen = false;
                StartCoroutine(Begin(ev));
            }
        }

        int MyPi => game.Career.Profile.CurrentCar.Pi;

        /// <summary>Whether the player's current car may enter (its PI within the event's class).</summary>
        bool Eligible(RaceEvent ev, out string why)
        {
            why = null;
            if (!ev.ClassLimit.HasValue) return true;
            int pi = MyPi;
            if (pi <= 0) { why = "Your car is still being measured. Try again in a moment."; return false; }
            int max = Performance.ClassMax(ev.ClassLimit.Value);
            if (pi <= max) return true;
            why = $"This event is for class {ev.ClassLimit.Value} and below (PI {max}). Your car is {Performance.ClassOf(pi)} {pi}: pick another car in the garage (G).";
            return false;
        }

        static string ClassText(RaceEvent ev) => ev.ClassLimit.HasValue ? $"Class {ev.ClassLimit.Value} (PI {Performance.ClassMax(ev.ClassLimit.Value)} max)" : "Open to every car";

        IEnumerator Begin(RaceEvent ev)
        {
            Busy = true;
            yield return null; // let the "preparing" message draw
            if (!lines.TryGetValue(ev.Id, out var line)) lines[ev.Id] = line = RacingLine.Build(ev.Route);
            string mine = game.Sim.Player.specId;
            var me = game.Career.Profile.CurrentCar;
            var pis = game.Career.Pis;
            var d = (Difficulty)difficulty;
            uint fieldSeed = (uint)(seed * 7 + 1);
            int count = ev.GridSize - 1;
            var spec = me.Spec;
            var tune = me.TuneOrDefault.Clone();
            var up = me.Upgrades;
            int known = me.Pi;
            // Rivals matched to the player's PI, and every rival setup measured, off the main thread.
            var prepare = Task.Run(() =>
            {
                int pi = known > 0 ? known : Performance.Measure(spec, tune, up).Pi;
                var field = Rivals.Field(pi, ev.ClassLimit, count, d, fieldSeed, pis);
                Parallel.ForEach(field, r => abilities.Get(r.CarId, r.Tune, Rivals.PartsKey(r.Parts) == "stock" ? null : r.Upgrades, r.Key));
                return (pi, field);
            });
            while (!prepare.IsCompleted) yield return null;
            if (prepare.IsFaulted)
            {
                Debug.LogError("Halcyon: could not line up the grid: " + prepare.Exception);
                game.Career.Say("The race could not start: " + prepare.Exception?.GetBaseException().Message);
                Busy = false;
                yield break;
            }
            var (playerPiNow, rivals) = prepare.Result;
            playerPi = playerPiNow;
            paid = false;
            paidCredits = paidXp = 0;
            // The player starts at the back of the grid's front half.
            Session = new RaceSession(game.Sim, ev, rivals, abilities, (uint)seed++, includePlayer: true, playerGrid: ev.GridSize / 2, line: line);
            Session.PlayerEntrant.Pi = playerPi;
            int k = 0;
            foreach (var e in Session.Entrants)
            {
                if (e.Ai == null) continue;
                bool same = e.Car.specId == mine;
                var rig = CarRig.Create(e.Car.vehicle, e.Car.specId, same || k % 2 == 1 ? Liveries[k % Liveries.Length] : (Color?)null);
                rig.Headlights = true;
                rigs.Add(rig);
                k++;
            }
            game.PlayerRig.Snap();
            game.CameraRig.Snap();
            Busy = false;
        }

        /// <summary>One fixed step of the race (instead of the free-roam Simulation.Step).</summary>
        public void Step(Controls c)
        {
            Session.Step(c);
            foreach (var r in rigs) r.Capture();
            if (!paid && Session.PlayerEntrant != null && Session.PlayerEntrant.Finished) Pay();
        }

        /// <summary>Credits and XP for the player's finish, once, into the career (saved at once).</summary>
        void Pay()
        {
            paid = true;
            var ev = Session.Event;
            int pos = Session.PlayerEntrant.Position;
            var cls = Performance.ClassOf(playerPi);
            paidCredits = Economy.Payout(ev.Distance, pos, cls, (Difficulty)difficulty);
            paidXp = Economy.RaceXp(ev.Distance, pos);
            game.Career.Profile.AddRace(paidCredits, paidXp, pos == 1);
            game.Career.SaveNow();
        }

        public void ResetPlayer() => Session?.ResetPlayer();

        public void Apply(float alpha)
        {
            foreach (var r in rigs) r.Apply(alpha);
        }

        /// <summary>Leave the race: the AI cars go, the player keeps driving from where they are.</summary>
        public void End()
        {
            if (Session == null) return;
            Session.End();
            foreach (var r in rigs) Destroy(r.gameObject);
            rigs.Clear();
            Session = null;
        }

        /// <summary>
        /// Keys during a race: Enter on the results screen returns to free roam.
        /// Holding the events key (T, d-pad up) for a second abandons the race, so
        /// a stray tap does not.
        /// </summary>
        public void TickRace()
        {
            if (Session == null) return;
            var m = game.Driver.Menu;
            bool done = Session.State == RaceState.Finished && Session.SinceFinish > 1.5;
            if (done && m.select)
            {
                End();
                return;
            }
            leaveHold = game.Driver.MenuHeld.events ? leaveHold + UnityEngine.Time.unscaledDeltaTime : 0;
            if (leaveHold > 1f)
            {
                leaveHold = 0;
                End();
            }
        }

        float leaveHold;

        void Styles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold };
            title.normal.textColor = Color.white;
            item = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            item.normal.textColor = new Color(1, 1, 1, 0.85f);
            small = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            small.normal.textColor = new Color(1, 1, 1, 0.75f);
            big = new GUIStyle(GUI.skin.label) { fontSize = 96, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            big.normal.textColor = Color.white;
            row = new GUIStyle(GUI.skin.label) { fontSize = 17 };
            row.normal.textColor = Color.white;
        }

        static string Time(double t) => double.IsNaN(t) ? "--:--.--" : $"{(int)(t / 60)}:{t % 60:00.00}";

        void Panel(Rect r, float a = 0.55f)
        {
            GUI.color = new Color(0, 0, 0, a);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void OnGUI()
        {
            Styles();
            if (Busy)
            {
                Panel(new Rect(Screen.width / 2 - 160, Screen.height / 2 - 30, 320, 60));
                GUI.Label(new Rect(Screen.width / 2 - 140, Screen.height / 2 - 14, 300, 30), "Lining up the grid…", item);
                return;
            }
            if (MenuOpen) DrawMenu();
            else if (Session != null) DrawRace();
        }

        void DrawMenu()
        {
            float w = 760, h = 150 + events.Count * 58;
            var r = new Rect(Screen.width / 2 - w / 2, Screen.height / 2 - h / 2, w, h);
            Panel(r, 0.7f);
            GUI.Label(new Rect(r.x + 24, r.y + 16, w, 40), "Festival events", title);
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                var y = r.y + 70 + i * 58;
                if (i == pick)
                {
                    GUI.color = new Color(1f, 0.55f, 0.2f, 0.35f);
                    GUI.DrawTexture(new Rect(r.x + 12, y - 4, w - 24, 54), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
                bool ok = Eligible(e, out _);
                GUI.color = ok ? Color.white : new Color(1, 1, 1, 0.5f);
                GUI.Label(new Rect(r.x + 24, y, w - 48, 26), e.Name, item);
                GUI.Label(new Rect(r.x + 24, y + 24, w - 48, 22), $"{(e.Kind == RaceKind.Circuit ? "Circuit" : "Sprint")} · {ClassText(e)} · {e.Description}", small);
                GUI.color = Color.white;
            }
            var sel = events[pick];
            int pi = MyPi;
            string car = $"Your car: {CarVisual.LoadStyle(game.Sim.Player.specId).name} {(pi > 0 ? $"{Performance.ClassOf(pi)} {pi}" : "(measuring)")}";
            string prize = pi > 0 ? $" · a win pays {Economy.Payout(sel.Distance, 1, Performance.ClassOf(pi), (Difficulty)difficulty):N0} cr and {Economy.RaceXp(sel.Distance, 1):N0} XP" : "";
            GUI.Label(new Rect(r.x + 24, r.yMax - 66, w - 48, 24), car + prize, small);
            GUI.Label(new Rect(r.x + 24, r.yMax - 40, w - 48, 24), $"↑/↓ event   ←/→ rivals: {(Difficulty)difficulty}   Enter race   Esc back", small);
        }

        void DrawRace()
        {
            var s = Session;
            var me = s.PlayerEntrant;
            // Countdown and start.
            if (s.Clock < 0)
            {
                int n = Mathf.CeilToInt((float)-s.Clock);
                GUI.Label(new Rect(0, Screen.height * 0.25f, Screen.width, 120), n.ToString(), big);
            }
            else if (s.Clock < 1.2)
            {
                GUI.Label(new Rect(0, Screen.height * 0.25f, Screen.width, 120), "GO", big);
            }
            // Position, lap and times.
            Panel(new Rect(14, 50, 300, 128));
            GUI.Label(new Rect(28, 56, 280, 44), $"P{me.Position} / {s.Entrants.Count}", title);
            string lap = s.Event.Kind == RaceKind.Circuit ? $"Lap {s.Lap(me)} / {s.Event.Laps}" : $"{Mathf.Max(0, (float)((s.Event.Distance - me.Progress) / 1000)):0.0} km to go";
            GUI.Label(new Rect(28, 98, 280, 24), lap, item);
            double t = me.Finished ? me.FinishTime : System.Math.Max(0, s.Clock);
            GUI.Label(new Rect(28, 122, 280, 22), $"Time {Time(t)}", small);
            if (s.Event.Kind == RaceKind.Circuit) GUI.Label(new Rect(28, 142, 280, 22), $"Best lap {Time(me.BestLap)}   Last {Time(me.LastLap)}", small);
            // The cars around the player.
            var order = s.Order;
            int idx = me.Position - 1;
            float y = 190;
            for (int i = Mathf.Max(0, idx - 2); i <= Mathf.Min(order.Count - 1, idx + 2); i++)
            {
                var e = order[i];
                string gap = e == me ? "" : $"{(e.Progress - me.Progress) / System.Math.Max(5, me.Car.vehicle.vLong):+0.0;-0.0} s";
                GUI.color = e == me ? new Color(1f, 0.6f, 0.25f) : Color.white;
                GUI.Label(new Rect(28, y, 300, 22), $"{e.Position,2}  {e.Name,-14} {gap}", small);
                GUI.color = Color.white;
                y += 20;
            }
            if (leaveHold > 0.05f)
            {
                GUI.Label(new Rect(0, Screen.height * 0.6f, Screen.width, 40), $"Leaving the race… {Mathf.CeilToInt((1 - leaveHold) * 10) / 10f:0.0}", small);
            }
            else if (s.State == RaceState.Racing && s.Clock < 6)
            {
                GUI.Label(new Rect(28, Screen.height - 40, 400, 24), "Hold T to leave the race", small);
            }
            if (me.WrongWay && s.State == RaceState.Racing)
            {
                GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, 120), "WRONG WAY", big);
            }
            if (s.State == RaceState.Finished) DrawResults();
        }

        void DrawResults()
        {
            var s = Session;
            float w = 760, h = 110 + s.Entrants.Count * 28;
            var r = new Rect(Screen.width / 2 - w / 2, Screen.height / 2 - h / 2, w, h);
            Panel(r, 0.75f);
            GUI.Label(new Rect(r.x + 24, r.y + 14, w, 40), $"{s.Event.Name}: P{s.PlayerEntrant.Position}", title);
            if (paid) GUI.Label(new Rect(r.x + 340, r.y + 22, w - 360, 30), $"+{paidCredits:N0} cr   +{paidXp:N0} XP", item);
            float y = r.y + 64;
            var winner = s.Order[0];
            foreach (var e in s.Order)
            {
                string time = e.Finished ? (e == winner ? Time(e.FinishTime) : $"+{e.FinishTime - winner.FinishTime:0.00} s") : "running";
                string car = CarVisual.LoadStyle(e.Car.specId).name + (e.Pi > 0 ? $" {Performance.ClassOf(e.Pi)} {e.Pi}" : "");
                GUI.color = e.Player ? new Color(1f, 0.6f, 0.25f) : Color.white;
                GUI.Label(new Rect(r.x + 24, y, w - 48, 26), $"{e.Position,2}.  {e.Name,-14}  {car,-26}  {time,-12}  best {Time(e.BestLap)}", row);
                GUI.color = Color.white;
                y += 28;
            }
            if (s.SinceFinish > 1.5) GUI.Label(new Rect(r.x + 24, r.yMax - 34, w - 48, 24), "Enter: back to the festival", small);
        }
    }
}
