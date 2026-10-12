// Races in Unity: the festival menu (T / d-pad up) with its events and its
// championships, the grid of AI cars with their Blender models, the
// countdown, the race HUD (position, lap, times, gaps; a drag race's trap
// speed; a time trial's gap to the ghost) and the results with the payout,
// a championship's standings and any accolades earned. The race itself is
// Halcyon.Core.RaceSession, the same code the headless AI tests run. Events
// have class limits and festival levels; rivals are matched to the player's
// PI (Halcyon.Core.Rivals), measured on worker threads while the grid lines
// up, and a time trial's ghost is the rival's own run, recorded there too.
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
        List<Championship> champs;
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
        /// <summary>The weather before an event set its own (restored when the race ends).</summary>
        bool weatherWasLocked;
        Sky weatherBefore;
        // The race under way: its championship (null for a one-off), and a time trial's ghost.
        Championship champ;
        Ghost ghost;
        Vehicle ghostCar;
        CarRig ghostRig;
        double ghostX, ghostY, ghostZ;
        /// <summary>A line for the results: the trap speed, the ghost, the championship.</summary>
        string resultNote;
        readonly List<Accolade> earned = new List<Accolade>();
        /// <summary>The championship's standings after this round (to show with the results).</summary>
        List<KeyValuePair<string, int>> standings;
        /// <summary>The player's car on this road (measured while the grid lines up) and its speeds along the racing line, for the line on the road.</summary>
        public CarAbility PlayerAbility { get; private set; }
        public SpeedProfile PlayerSpeeds { get; private set; }

        static readonly Color[] Liveries =
        {
            new Color(0.95f, 0.45f, 0.08f), new Color(0.05f, 0.66f, 0.64f), new Color(0.08f, 0.22f, 0.62f),
            new Color(0.93f, 0.78f, 0.12f), new Color(0.92f, 0.92f, 0.9f), new Color(0.78f, 0.12f, 0.45f),
            new Color(0.22f, 0.23f, 0.25f), new Color(0.08f, 0.32f, 0.18f), new Color(0.78f, 0.12f, 0.14f),
        };
        static readonly Color GhostTint = new Color(0.55f, 0.78f, 1f);

        public static RaceController Create(HalcyonGame game, Transform parent)
        {
            var go = new GameObject("Races");
            go.transform.SetParent(parent, false);
            var rc = go.AddComponent<RaceController>();
            rc.game = game;
            RacingLineView.Create(game, rc, go.transform);
            return rc;
        }

        public bool Racing => Session != null;
        Profile P => game.Career.Profile;
        int Rows => events.Count + champs.Count;

        /// <summary>The festival's events and championships (built the first time they are asked for).</summary>
        public List<RaceEvent> Events
        {
            get
            {
                if (events == null)
                {
                    events = EventCatalog.Festival(game.Sim.World, game.Sim.Ground);
                    champs = Championship.Festival(events);
                }
                return events;
            }
        }

        public List<Championship> Championships
        {
            get
            {
                _ = Events;
                return champs;
            }
        }

        public void OpenMenu()
        {
            _ = Events;
            MenuOpen = true;
            pick = Mathf.Clamp(pick, 0, Rows - 1);
        }

        /// <summary>Menu keys while the festival menu is open (called by the game after it polls input).</summary>
        public void TickMenu()
        {
            var m = game.Driver.Menu;
            if (m.up) pick = (pick + Rows - 1) % Rows;
            if (m.down) pick = (pick + 1) % Rows;
            if (m.left) difficulty = Mathf.Max(0, difficulty - 1);
            if (m.right) difficulty = Mathf.Min(3, difficulty + 1);
            if (m.back) MenuOpen = false;
            string why;
            if (pick >= events.Count)
            {
                var c = champs[pick - events.Count];
                var run = RunOf(c);
                // X on the championship under way abandons it.
                if (m.clear && run != null)
                {
                    P.Championship = null;
                    game.Career.MarkDirty();
                    game.Career.Say($"{c.Name} abandoned.");
                    return;
                }
                if (!m.select) return;
                if (P.Championship != null && run == null)
                {
                    game.Career.Say($"Finish the {ChampName(P.Championship.Id)} first, or abandon it (X on it).");
                    return;
                }
                if (run != null && run.CarId != game.Sim.Player.specId)
                {
                    game.Career.Say($"The {c.Name} is raced in the {CarVisual.LoadStyle(run.CarId).name}: drive it (G), or abandon the series (X).");
                    return;
                }
                if (!Eligible(c.Event(events, run?.Round ?? 0), c, out why))
                {
                    game.Career.Say(why);
                    return;
                }
                if (run == null)
                {
                    run = new ChampionshipRun { Id = c.Id, Seed = (uint)(seed++ * 7919 + 13), CarId = game.Sim.Player.specId, Rivals = (Difficulty)difficulty };
                    P.Championship = run;
                    game.Career.MarkDirty();
                }
                MenuOpen = false;
                StartCoroutine(Begin(c.Event(events, run.Round), c));
                return;
            }
            if (!m.select) return;
            var ev = events[pick];
            if (!Eligible(ev, null, out why))
            {
                game.Career.Say(why);
                return;
            }
            MenuOpen = false;
            StartCoroutine(Begin(ev, null));
        }

        ChampionshipRun RunOf(Championship c) => P.Championship != null && P.Championship.Id == c.Id ? P.Championship : null;

        string ChampName(string id)
        {
            foreach (var c in Championships) if (c.Id == id) return c.Name;
            return id;
        }

        int MyPi => P.CurrentCar.Pi;

        /// <summary>Whether the player may enter: the festival level, and their current car's PI within the class.</summary>
        bool Eligible(RaceEvent ev, Championship c, out string why)
        {
            why = null;
            int level = Economy.Level(P.Xp).level;
            int need = c?.MinLevel ?? ev.MinLevel;
            if (level < need)
            {
                why = $"{(c != null ? c.Name : ev.Name)} opens at festival level {need} (you are level {level}). Race, explore and do stunts for XP.";
                return false;
            }
            var limit = c != null ? c.ClassLimit : ev.ClassLimit;
            if (!limit.HasValue) return true;
            int pi = MyPi;
            if (pi <= 0)
            {
                why = "Your car is still being measured. Try again in a moment.";
                return false;
            }
            int max = Performance.ClassMax(limit.Value);
            if (pi <= max) return true;
            why = $"This is for class {limit.Value} and below (PI {max}). Your car is {Performance.ClassOf(pi)} {pi}: pick another car in the garage (G).";
            return false;
        }

        /// <summary>After an event with its own sky: back to the player's choice, or the island's own weather.</summary>
        void RestoreWeather(RaceEvent ev)
        {
            if (!ev.Weather.HasValue) return;
            if (weatherWasLocked) game.Sim.Weather.Force(weatherBefore);
            else game.Sim.Weather.Release();
        }

        static string ClassText(PiClass? limit) => limit.HasValue ? $"Class {limit.Value} (PI {Performance.ClassMax(limit.Value)} max)" : "Any car";

        /// <summary>What kind of race an event is, for the menu.</summary>
        public static string KindText(RaceEvent e) =>
            e.Ghost ? "Time trial" : e.Rival != null ? "Head to head" : e.Kind == RaceKind.Drag ? "Drag race" :
            e.Surface == Surface.Dirt ? "Rally" : e.Kind == RaceKind.Circuit ? "Circuit" : "Sprint";

        IEnumerator Begin(RaceEvent ev, Championship c)
        {
            Busy = true;
            yield return null; // let the "lining up" message draw
            champ = c;
            ghost = null;
            resultNote = null;
            standings = null;
            earned.Clear();
            if (!lines.TryGetValue(ev.Id, out var line)) lines[ev.Id] = line = RacingLine.Build(ev.Route);
            string mine = game.Sim.Player.specId;
            var me = P.CurrentCar;
            var pis = game.Career.Pis;
            var run = c != null ? RunOf(c) : null;
            // A championship keeps its rivals: the same seed and difficulty every round.
            var d = run != null ? run.Rivals : (Difficulty)difficulty;
            uint fieldSeed = run != null ? run.Seed : (uint)(seed * 7 + 1);
            int count = ev.Ghost ? 0 : ev.GridSize - 1;
            var limit = c != null ? c.ClassLimit : ev.ClassLimit;
            var spec = me.Spec;
            var tune = me.TuneOrDefault.Clone();
            var up = me.Upgrades;
            int known = me.Pi;
            var world = game.Sim.World;
            // The event's own sky and hour, if it has them (the island's are restored after).
            weatherWasLocked = game.Sim.Weather.Locked;
            weatherBefore = game.Sim.Weather.State;
            EventCatalog.SetConditions(game.Sim, ev);
            // The road as the race will find it (the AI plans for its wetness).
            double wet = RaceSession.WetnessFor(game.Sim);
            // Rivals matched to the player's PI, every rival setup measured, and a time trial's ghost driven, off the main thread.
            var prepare = Task.Run(() =>
            {
                int pi = known > 0 ? known : Performance.Measure(spec, tune, up).Pi;
                var field = Rivals.Field(pi, limit, count, d, fieldSeed, pis);
                // Head to head: the festival's rival, in the car the matching chose.
                if (ev.Rival != null && field.Count > 0) field[0].Driver = ev.Rival;
                Parallel.ForEach(field, r => abilities.Get(r.CarId, r.Tune, Rivals.PartsKey(r.Parts) == "stock" ? null : r.Upgrades, r.Key, wet, ev.Surface));
                Ghost g = null;
                if (ev.Ghost)
                {
                    var rival = Rivals.Field(pi, limit, 1, d, fieldSeed, pis)[0];
                    rival.Driver = ev.Rival ?? EventCatalog.TheRival();
                    abilities.Get(rival.CarId, rival.Tune, Rivals.PartsKey(rival.Parts) == "stock" ? null : rival.Upgrades, rival.Key, wet, ev.Surface);
                    g = Ghost.Record(world, ev, rival, abilities, wet, fieldSeed);
                }
                // The player's own car on this road, for the racing line's colours (a little more margin than the AI's).
                var myCar = CarAbility.Measure(spec, tune, up, wet, ev.Surface);
                var speeds = SpeedProfile.Build(line, myCar, 0.9, 0.8);
                return (pi, field, g, myCar, speeds);
            });
            while (!prepare.IsCompleted) yield return null;
            if (prepare.IsFaulted)
            {
                Debug.LogError("Halcyon: could not line up the grid: " + prepare.Exception);
                game.Career.Say("The race could not start: " + prepare.Exception?.GetBaseException().Message);
                RestoreWeather(ev);
                Busy = false;
                yield break;
            }
            var (playerPiNow, rivals, recorded, mineAbility, mineSpeeds) = prepare.Result;
            playerPi = playerPiNow;
            PlayerAbility = mineAbility;
            PlayerSpeeds = mineSpeeds;
            paid = false;
            paidCredits = paidXp = 0;
            // The player starts at the back of the grid's front half (alone in a time trial; in the right-hand lane of a drag race).
            Session = new RaceSession(game.Sim, ev, rivals, abilities, (uint)seed++, includePlayer: true, playerGrid: Mathf.Min(ev.GridSize / 2, rivals.Count), line: line);
            Session.PlayerEntrant.Pi = playerPi;
            int k = 0;
            foreach (var e in Session.Entrants)
            {
                if (e.Ai == null) continue;
                bool same = e.Car.specId == mine;
                var rig = CarRig.Create(e.Car.vehicle, e.Car.specId, same || k % 2 == 1 ? Liveries[k % Liveries.Length] : (Color?)null);
                rigs.Add(rig);
                k++;
            }
            if (recorded != null && recorded.Count > 0)
            {
                ghost = recorded;
                ghostCar = new Vehicle(VehicleParams.Build(Cars.ById(ghost.CarId)));
                for (int i = 0; i < 4; i++) ghostCar.wheels[i].length = ghostCar.Params.wheels[i].staticLength;
                ghost.Pose(0, ghostCar);
                ghostX = ghostCar.pos.x;
                ghostY = ghostCar.pos.y;
                ghostZ = ghostCar.pos.z;
                ghostRig = CarRig.Create(ghostCar, ghost.CarId, GhostTint);
                ghostRig.Silent = true;
                ghostRig.MakeGhost(GhostTint);
                ghostRig.Snap();
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
            if (ghostRig != null)
            {
                ghost.Pose(System.Math.Max(0, Session.Clock), ghostCar);
                // The ghost's wheels roll with the distance it covers.
                var p = ghostCar.pos;
                double dx = p.x - ghostX, dy = p.y - ghostY, dz = p.z - ghostZ;
                double moved = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
                ghostX = p.x;
                ghostY = p.y;
                ghostZ = p.z;
                for (int i = 0; i < 4; i++) ghostCar.wheels[i].spin += moved / ghostCar.Params.wheels[i].radius;
                ghostRig.Capture();
            }
            if (!paid && Session.PlayerEntrant != null && Session.PlayerEntrant.Finished) Pay();
        }

        /// <summary>Whether the player beat the ghost (a time trial).</summary>
        bool BeatGhost => ghost != null && Session.PlayerEntrant.Finished && (double.IsNaN(ghost.FinishTime) || Session.PlayerEntrant.FinishTime < ghost.FinishTime);

        /// <summary>The player's place for the payout: in a time trial, first if they beat the ghost.</summary>
        int PlayerPlace() => Session.Event.Ghost ? (BeatGhost ? 1 : 2) : Session.PlayerEntrant.Position;

        /// <summary>Credits and XP for the player's finish, once: the championship's standings and prize, accolades, saved at once.</summary>
        void Pay()
        {
            paid = true;
            var ev = Session.Event;
            var me = Session.PlayerEntrant;
            int pos = PlayerPlace();
            var cls = Performance.ClassOf(playerPi);
            paidCredits = Economy.Payout(ev.Distance, pos, cls, (Difficulty)difficulty);
            paidXp = Economy.RaceXp(ev.Distance, pos);
            P.AddRace(paidCredits, paidXp, pos == 1);
            Accolades.RecordRace(P, ev, pos, pos == 1);
            if (ev.Kind == RaceKind.Drag && !double.IsNaN(me.FinishSpeed))
                resultNote = $"Trap speed {Units.Speed(me.FinishSpeed)}";
            if (ev.Ghost && ghost != null)
            {
                double gap = me.FinishTime - ghost.FinishTime;
                resultNote = double.IsNaN(gap) ? $"{ghost.Name}'s ghost did not finish" :
                    gap < 0 ? $"You beat {ghost.Name}'s ghost by {-gap:0.00} s" : $"{ghost.Name}'s ghost was {gap:0.00} s faster";
            }
            var run = champ != null ? RunOf(champ) : null;
            if (run != null)
            {
                // Everyone's points from the finishing order (the player is "You" in it).
                var order = new List<string>();
                foreach (var e in Session.Order) order.Add(e.Name);
                run.Score(order);
                int place = run.PlayerPlace();
                standings = run.Table();
                if (run.Over(champ))
                {
                    int i = place - 1;
                    if (i >= 0 && i < Championship.Prize.Length)
                    {
                        int prize = (int)(Championship.Prize[i] * Economy.ClassFactor(cls));
                        P.Credits += prize;
                        P.Xp += Championship.PrizeXp[i];
                        paidCredits += prize;
                        paidXp += Championship.PrizeXp[i];
                    }
                    if (place == 1 && !P.ChampionshipsWon.Contains(champ.Id)) P.ChampionshipsWon.Add(champ.Id);
                    resultNote = place == 1 ? $"Champion of the {champ.Name}!" : $"{champ.Name}: {Ordinal(place)} overall";
                    P.Championship = null;
                }
                else resultNote = $"{champ.Name}: round {run.Round} of {champ.Rounds.Count} done, you are {Ordinal(place)} on {Points(run.Standings)} points";
            }
            earned.AddRange(Accolades.Check(P, game.Discover, game.Stunts));
            game.Career.SaveNow();
        }

        static int Points(Dictionary<string, int> s) => s.TryGetValue("You", out int v) ? v : 0;

        public static string Ordinal(int n) => n + (n % 100 >= 11 && n % 100 <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

        public void ResetPlayer() => Session?.ResetPlayer();

        public void Apply(float alpha)
        {
            foreach (var r in rigs) r.Apply(alpha);
            if (ghostRig != null) ghostRig.Apply(alpha);
        }

        /// <summary>Leave the race: the AI cars go, the player keeps driving from where they are.</summary>
        public void End()
        {
            if (Session == null) return;
            Session.End();
            RestoreWeather(Session.Event);
            foreach (var r in rigs) Destroy(r.gameObject);
            rigs.Clear();
            if (ghostRig != null) Destroy(ghostRig.gameObject);
            ghostRig = null;
            ghostCar = null;
            ghost = null;
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
            RaceSounds();
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

        // The race's sounds: the countdown beeps, GO, each lap and the finish.
        RaceSession soundSession;
        int lastBeep, lastLaps;
        bool finishSounded;

        void RaceSounds()
        {
            var s = Session;
            var snd = GameSound.Instance;
            if (snd == null) return;
            if (s != soundSession)
            {
                soundSession = s;
                lastBeep = 99;
                lastLaps = 0;
                finishSounded = false;
            }
            int beep = s.Clock < 0 ? Mathf.CeilToInt((float)-s.Clock) : 0;
            if (beep != lastBeep)
            {
                if (beep > 0 && beep <= 3) snd.Ui(Core.Audio.UiSound.Count);
                else if (beep == 0 && lastBeep != 99) snd.Ui(Core.Audio.UiSound.Go);
                lastBeep = beep;
            }
            var me = s.PlayerEntrant;
            if (me == null) return;
            if (me.LapsDone > lastLaps)
            {
                lastLaps = me.LapsDone;
                if (!me.Finished) snd.Ui(Core.Audio.UiSound.Checkpoint);
            }
            if (me.Finished && !finishSounded)
            {
                finishSounded = true;
                snd.Ui(me.Position == 1 ? Core.Audio.UiSound.Reward : Core.Audio.UiSound.Select);
            }
        }

        static string Time(double t) => double.IsNaN(t) ? "--:--.--" : $"{(int)(t / 60)}:{t % 60:00.00}";

        void OnGUI()
        {
            if (game == null || game.HideHud) return;
            Ui.Begin(hud: Session != null && !MenuOpen && !Busy);
            if (Busy)
            {
                var r = new Rect(Ui.W / 2 - 220, Ui.H / 2 - 40, 440, 80);
                Ui.Panel(r, 0.72f);
                GUI.Label(new Rect(r.x + 28, r.y + 24, r.width - 56, 32), "Lining up the grid…", Ui.Body);
            }
            else if (MenuOpen) DrawMenu();
            else if (Session != null) DrawRace();
            Ui.End();
        }

        void DrawMenu()
        {
            float w = Mathf.Min(1180, Ui.W - 80), h = Ui.H - 140;
            var r = new Rect(Ui.W / 2 - w / 2, 70, w, h);
            Ui.Panel(r, 0.8f);
            int level = Economy.Level(P.Xp).level;
            GUI.Label(new Rect(r.x + 30, r.y + 18, 400, 48), "Festival", Ui.Title);
            GUI.Label(new Rect(r.x + 30, r.y + 18, w - 60, 48), $"Level {level} · {P.Credits:N0} cr · {P.Wins} wins · {P.Accolades.Count} of {Accolades.All.Count} accolades", Ui.Value);
            // A window of rows around the one picked: the events, then the championships.
            const float RowH = 64;
            float top = r.y + 84;
            int rows = Mathf.Max(1, (int)((h - 180) / RowH));
            int first = Mathf.Clamp(pick - rows / 2, 0, Mathf.Max(0, Rows - rows));
            for (int i = first; i < Rows && i < first + rows; i++)
            {
                var row = new Rect(r.x + 16, top + (i - first) * RowH, w - 32, RowH - 6);
                if (i == pick) Ui.Fill(row, new Color(1f, 0.55f, 0.2f, 0.32f));
                if (i < events.Count) EventRow(row, events[i], level);
                else ChampRow(row, champs[i - events.Count], level);
            }
            if (first > 0) GUI.Label(new Rect(r.xMax - 56, top - 28, 40, 26), "▲", Ui.Small);
            if (first + rows < Rows) GUI.Label(new Rect(r.xMax - 56, top + rows * RowH - 4, 40, 26), "▼", Ui.Small);
            // The pick's prize, the car, the keys.
            int pi = MyPi;
            string car = $"Your car: {CarVisual.LoadStyle(game.Sim.Player.specId).name} {(pi > 0 ? $"{Performance.ClassOf(pi)} {pi}" : "(measuring)")}";
            string prize = "";
            if (pick < events.Count)
            {
                if (pi > 0) prize = $" · a win pays {Economy.Payout(events[pick].Distance, 1, Performance.ClassOf(pi), (Difficulty)difficulty):N0} cr and {Economy.RaceXp(events[pick].Distance, 1):N0} XP";
            }
            else prize = $" · the title pays {Championship.Prize[0]:N0} cr (more in faster classes) and {Championship.PrizeXp[0]:N0} XP, on top of each round";
            GUI.Label(new Rect(r.x + 30, r.yMax - 82, w - 60, 28), car + prize, Ui.Small);
            string x = pick >= events.Count && RunOf(champs[pick - events.Count]) != null ? "   X abandon" : "";
            GUI.Label(new Rect(r.x + 30, r.yMax - 50, w - 60, 28), $"↑/↓ choose   ←/→ rivals: {(Difficulty)difficulty}   Enter race{x}   Esc back", Ui.Small);
        }

        void EventRow(Rect row, RaceEvent e, int level)
        {
            bool ok = Eligible(e, null, out _);
            var old = GUI.color;
            GUI.color = ok ? Color.white : new Color(1, 1, 1, 0.5f);
            Ui.Fill(new Rect(row.x, row.y, 6, row.height), KindColour(e));
            GUI.Label(new Rect(row.x + 20, row.y + 4, row.width - 220, 30), e.Name, Ui.RowOn);
            string cond = e.ConditionsText != null ? e.ConditionsText + " · " : "";
            GUI.Label(new Rect(row.x + 20, row.y + 32, row.width - 40, 26), $"{KindText(e)} · {cond}{ClassText(e.ClassLimit)} · {e.Description}", Ui.Small);
            if (level < e.MinLevel) GUI.Label(new Rect(row.xMax - 200, row.y + 4, 186, 30), $"🔒 Level {e.MinLevel}", Ui.Value);
            GUI.color = old;
        }

        void ChampRow(Rect row, Championship c, int level)
        {
            var run = RunOf(c);
            bool won = P.ChampionshipsWon.Contains(c.Id);
            var old = GUI.color;
            GUI.color = level >= c.MinLevel ? Color.white : new Color(1, 1, 1, 0.5f);
            Ui.Fill(new Rect(row.x, row.y, 6, row.height), new Color(1f, 0.82f, 0.2f));
            GUI.Label(new Rect(row.x + 20, row.y + 4, row.width - 220, 30), $"Championship: {c.Name}", Ui.RowOn);
            var names = new List<string>();
            foreach (var id in c.Rounds) names.Add(events.Find(e => e.Id == id)?.Name ?? id);
            string state = run != null ? $"Round {run.Round + 1} of {c.Rounds.Count}, {Ordinal(Mathf.Max(1, run.PlayerPlace()))} on {Points(run.Standings)} pts · " : "";
            GUI.Label(new Rect(row.x + 20, row.y + 32, row.width - 40, 26), $"{state}{ClassText(c.ClassLimit)} · {string.Join(" → ", names)}", Ui.Small);
            if (level < c.MinLevel) GUI.Label(new Rect(row.xMax - 200, row.y + 4, 186, 30), $"🔒 Level {c.MinLevel}", Ui.Value);
            else if (won) GUI.Label(new Rect(row.xMax - 200, row.y + 4, 186, 30), "Won ✓", Ui.Value);
            else if (run != null) GUI.Label(new Rect(row.xMax - 200, row.y + 4, 186, 30), "Under way", Ui.Value);
            GUI.color = old;
        }

        /// <summary>A colour for each kind of race (the menu's row marks).</summary>
        static Color KindColour(RaceEvent e) =>
            e.Ghost ? GhostTint : e.Rival != null ? new Color(0.9f, 0.25f, 0.3f) : e.Kind == RaceKind.Drag ? new Color(0.25f, 0.85f, 0.5f) :
            e.Surface == Surface.Dirt ? new Color(0.75f, 0.5f, 0.28f) : e.Kind == RaceKind.Circuit ? new Color(0.3f, 0.6f, 1f) : new Color(1f, 0.55f, 0.2f);

        void DrawRace()
        {
            var s = Session;
            var me = s.PlayerEntrant;
            // Countdown and start.
            if (s.Clock < 0) GUI.Label(new Rect(0, Ui.H * 0.22f, Ui.W, 140), Mathf.CeilToInt((float)-s.Clock).ToString(), Ui.Big);
            else if (s.Clock < 1.2) GUI.Label(new Rect(0, Ui.H * 0.22f, Ui.W, 140), "GO", Ui.Big);
            // Position, lap and times.
            bool trial = s.Event.Ghost;
            Ui.Panel(new Rect(24, 86, 400, trial ? 176 : 156));
            GUI.Label(new Rect(42, 92, 380, 48), trial ? "Time trial" : $"P{me.Position} / {s.Entrants.Count}", Ui.Title);
            string togo = s.Event.Kind == RaceKind.Circuit ? $"Lap {s.Lap(me)} / {s.Event.Laps}" :
                s.Event.Kind == RaceKind.Drag ? $"{Units.Short(System.Math.Max(0, s.Event.Distance - me.Progress))} to go" :
                $"{Units.Long(System.Math.Max(0, s.Event.Distance - me.Progress))} to go";
            GUI.Label(new Rect(42, 142, 380, 30), togo, Ui.Body);
            double t = me.Finished ? me.FinishTime : System.Math.Max(0, s.Clock);
            GUI.Label(new Rect(42, 172, 380, 26), $"Time {Time(t)}", Ui.Small);
            if (s.Event.Kind == RaceKind.Circuit) GUI.Label(new Rect(42, 196, 380, 26), $"Best lap {Time(me.BestLap)}   Last {Time(me.LastLap)}", Ui.Small);
            if (trial && ghost != null)
            {
                // The gap to the ghost at this point of the route: green ahead of it, red behind.
                if (s.Clock > 0 && !me.Finished)
                {
                    double gap = ghost.Gap(s.Clock, me.Progress);
                    GUI.color = gap <= 0 ? Ui.Good : Ui.Bad;
                    GUI.Label(new Rect(42, 222, 380, 30), $"{(gap <= 0 ? "−" : "+")}{System.Math.Abs(gap):0.00} s  {ghost.Name}'s ghost", Ui.Body);
                    GUI.color = Color.white;
                }
                else if (s.Clock <= 0) GUI.Label(new Rect(42, 222, 380, 30), $"Beat {ghost.Name}: {Time(ghost.FinishTime)}", Ui.Body);
            }
            else if (!trial)
            {
                // The cars around the player.
                var order = s.Order;
                int idx = me.Position - 1;
                float y = 252;
                for (int i = Mathf.Max(0, idx - 2); i <= Mathf.Min(order.Count - 1, idx + 2); i++)
                {
                    var e = order[i];
                    string gap = e == me ? "" : $"{(e.Progress - me.Progress) / System.Math.Max(5, me.Car.vehicle.vLong):+0.0;-0.0} s";
                    GUI.color = e == me ? Ui.Accent : Color.white;
                    GUI.Label(new Rect(42, y, 60, 26), e.Position.ToString(), Ui.Small);
                    GUI.Label(new Rect(78, y, 220, 26), e.Name, Ui.Small);
                    GUI.Label(new Rect(300, y, 100, 26), gap, Ui.Small);
                    GUI.color = Color.white;
                    y += 26;
                }
            }
            if (leaveHold > 0.05f)
                GUI.Label(new Rect(0, Ui.H * 0.6f, Ui.W, 40), $"Leaving the race… {Mathf.CeilToInt((1 - leaveHold) * 10) / 10f:0.0}", Ui.Keys);
            else if (s.State == RaceState.Racing && s.Clock < 6)
                GUI.Label(new Rect(42, Ui.H - 52, 600, 28), "Hold T to leave the race", Ui.Small);
            if (me.WrongWay && s.State == RaceState.Racing)
                GUI.Label(new Rect(0, Ui.H * 0.33f, Ui.W, 140), "WRONG WAY", Ui.Big);
            if (s.State == RaceState.Finished) DrawResults();
        }

        void DrawResults()
        {
            var s = Session;
            bool table = standings != null;
            float left = 900, right = table ? 420 : 0, w = left + right;
            int lines = s.Entrants.Count + (resultNote != null ? 1 : 0) + earned.Count;
            if (table) lines = Mathf.Max(lines, standings.Count + 1);
            float h = 150 + lines * 34;
            var r = new Rect(Ui.W / 2 - w / 2, Mathf.Max(20, Ui.H / 2 - h / 2), w, h);
            Ui.Panel(r, 0.82f);
            bool trial = s.Event.Ghost;
            string head = trial ? (BeatGhost ? "Ghost beaten!" : "Ghost not beaten") : $"{s.Event.Name}: {Ordinal(s.PlayerEntrant.Position)}";
            GUI.Label(new Rect(r.x + 30, r.y + 18, left - 60, 48), head, Ui.Title);
            if (paid) GUI.Label(new Rect(r.x + 30, r.y + 18, left - 60, 48), $"+{paidCredits:N0} cr   +{paidXp:N0} XP", Ui.Value);
            float y = r.y + 80;
            var winner = s.Order[0];
            foreach (var e in s.Order)
            {
                string time = e.Finished ? (e == winner ? Time(e.FinishTime) : $"+{e.FinishTime - winner.FinishTime:0.00} s") : "running";
                string car = CarVisual.LoadStyle(e.Car.specId).name + (e.Pi > 0 ? $"  {Performance.ClassOf(e.Pi)} {e.Pi}" : "");
                string extra = s.Event.Kind == RaceKind.Drag ? (e.Finished ? Units.Speed(e.FinishSpeed) : "") : s.Event.Kind == RaceKind.Circuit ? $"best {Time(e.BestLap)}" : "";
                GUI.color = e.Player ? Ui.Accent : Color.white;
                GUI.Label(new Rect(r.x + 30, y, 50, 30), $"{e.Position}.", Ui.Mono);
                GUI.Label(new Rect(r.x + 74, y, 200, 30), e.Name, Ui.Mono);
                GUI.Label(new Rect(r.x + 280, y, 300, 30), car, Ui.Mono);
                GUI.Label(new Rect(r.x + 590, y, 140, 30), time, Ui.Mono);
                GUI.Label(new Rect(r.x + 730, y, 150, 30), extra, Ui.Mono);
                GUI.color = Color.white;
                y += 34;
            }
            if (trial && ghost != null)
            {
                GUI.color = GhostTint;
                GUI.Label(new Rect(r.x + 74, y, 200, 30), $"{ghost.Name} (ghost)", Ui.Mono);
                GUI.Label(new Rect(r.x + 280, y, 300, 30), CarVisual.LoadStyle(ghost.CarId).name, Ui.Mono);
                GUI.Label(new Rect(r.x + 590, y, 140, 30), Time(ghost.FinishTime), Ui.Mono);
                GUI.color = Color.white;
                y += 34;
            }
            if (resultNote != null)
            {
                GUI.Label(new Rect(r.x + 30, y + 4, left - 60, 30), resultNote, Ui.Sub);
                y += 38;
            }
            foreach (var a in earned)
            {
                GUI.color = Ui.Good;
                GUI.Label(new Rect(r.x + 30, y, left - 60, 30), $"Accolade: {a.Name}, {a.Description}  +{a.Xp:N0} XP", Ui.Body);
                GUI.color = Color.white;
                y += 34;
            }
            if (table)
            {
                // The championship's standings after this round.
                float x = r.x + left;
                Ui.Fill(new Rect(x - 1, r.y + 24, 2, r.height - 48), new Color(1, 1, 1, 0.15f));
                GUI.Label(new Rect(x + 24, r.y + 26, right - 48, 34), "Standings", Ui.Sub);
                float ty = r.y + 80;
                for (int i = 0; i < standings.Count; i++)
                {
                    var kv = standings[i];
                    GUI.color = kv.Key == "You" ? Ui.Accent : Color.white;
                    GUI.Label(new Rect(x + 24, ty, 40, 30), $"{i + 1}.", Ui.Mono);
                    GUI.Label(new Rect(x + 64, ty, 220, 30), kv.Key, Ui.Mono);
                    GUI.Label(new Rect(x + 24, ty, right - 60, 30), $"{kv.Value} pts", Ui.Value);
                    GUI.color = Color.white;
                    ty += 34;
                }
            }
            if (s.SinceFinish > 1.5) GUI.Label(new Rect(r.x + 30, r.yMax - 44, left - 60, 28), "Enter: back to the festival", Ui.Small);
        }
    }
}
