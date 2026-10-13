// Accolades: the festival's achievements. Each pays XP once, the first time
// its condition holds, judged from the profile (wins, podiums, the kinds of
// event won, championships, the garage) and, when given, the island's
// discoveries and the stunt scores. No UnityEngine.
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    public sealed class Accolade
    {
        public string Id, Name, Description;
        public int Xp;
        internal Func<Profile, Discoveries, StuntTracker, bool> Test;
        /// <summary>How far along it is, 0 to 1 (for the list), when it counts something.</summary>
        internal Func<Profile, Discoveries, StuntTracker, double> Progress;
    }

    public static class Accolades
    {
        /// <summary>Counters kept in Profile.Stats.</summary>
        public const string Podiums = "podiums", DragWins = "drag-wins", RallyWins = "rally-wins", DuelWins = "duel-wins",
            GhostWins = "ghost-wins", NightWins = "night-wins", StormWins = "storm-wins", ClimbWins = "climb-wins";

        static int Stat(Profile p, string k) => p.Stats.TryGetValue(k, out int v) ? v : 0;

        static int StuntStars(StuntTracker s, StuntKind? kind, out int count)
        {
            int stars = 0;
            count = 0;
            if (s == null) return 0;
            foreach (var st in s.All)
            {
                if (kind.HasValue && st.Kind != kind.Value) continue;
                count++;
                double b = s.Best(st);
                if (b > 0) stars += st.StarsFor(b);
            }
            return stars;
        }

        static double Share(int have, int need) => need <= 0 ? 0 : Math.Min(1, (double)have / need);

        static Accolade Count(string id, string name, string description, int xp, Func<Profile, int> value, int need) => new Accolade
        {
            Id = id, Name = name, Description = description, Xp = xp,
            Test = (p, d, s) => value(p) >= need,
            Progress = (p, d, s) => Share(value(p), need),
        };

        static int Places(Discoveries d, bool landmark, out int all)
        {
            all = 0;
            int found = 0;
            if (d == null) return 0;
            foreach (var pl in d.Places)
            {
                if (pl.Landmark != landmark) continue;
                all++;
                if (d.Found(pl)) found++;
            }
            return found;
        }

        public static readonly List<Accolade> All = new List<Accolade>
        {
            Count("first-win", "First Win", "Win a race.", 500, p => p.Wins, 1),
            Count("five-wins", "On a Roll", "Win five races.", 1000, p => p.Wins, 5),
            Count("twenty-five-wins", "Festival Star", "Win twenty-five races.", 3000, p => p.Wins, 25),
            Count("podiums", "Podium Regular", "Finish in the top three ten times.", 1000, p => Stat(p, Podiums), 10),
            Count("champion", "Champion", "Win a championship.", 2000, p => p.ChampionshipsWon.Count, 1),
            Count("triple-crown", "Triple Crown", "Win all three championships.", 5000, p => p.ChampionshipsWon.Count, 3),
            Count("drag", "Quarter Mile", "Win a drag race.", 500, p => Stat(p, DragWins), 1),
            Count("rally", "Mud and Gravel", "Win the Redmesa Rally.", 800, p => Stat(p, RallyWins), 1),
            Count("duel", "Rivalry", "Beat Mika Varga head to head.", 1000, p => Stat(p, DuelWins), 1),
            Count("ghost", "Ghostbuster", "Beat the ghost in the time trial.", 1000, p => Stat(p, GhostWins), 1),
            Count("night", "Night Owl", "Win the race by night.", 800, p => Stat(p, NightWins), 1),
            Count("storm", "Storm Chaser", "Win the storm run.", 800, p => Stat(p, StormWins), 1),
            Count("climb", "King of the Hill", "Win the Highcrest Hill Climb.", 800, p => Stat(p, ClimbWins), 1),
            Count("five-cars", "Collector", "Own five cars.", 1000, p => p.Garage.Count, 5),
            Count("ten-cars", "The Full Garage", "Own ten cars.", 2500, p => p.Garage.Count, 10),
            Count("hypercar", "Hypercar", "Own a car of class S2 or above (PI over 900).", 2000, p =>
            {
                foreach (var c in p.Garage) if (c.Pi > 900) return 1;
                return 0;
            }, 1),
            Count("tuner", "Tuner", "Fit ten levels of parts across your cars.", 800, p =>
            {
                int n = 0;
                foreach (var c in p.Garage) foreach (var kv in c.Parts) n += kv.Value;
                return n;
            }, 10),
            new Accolade
            {
                Id = "explorer", Name = "Explorer", Description = "Find every place on the island.", Xp = 1500,
                Test = (p, d, s) => d != null && Places(d, false, out int all) == all && all > 0,
                Progress = (p, d, s) => Share(Places(d, false, out int all), all),
            },
            new Accolade
            {
                Id = "sightseer", Name = "Sightseer", Description = "Find all five landmarks.", Xp = 1500,
                Test = (p, d, s) => d != null && Places(d, true, out int all) == all && all > 0,
                Progress = (p, d, s) => Share(Places(d, true, out int all), all),
            },
            new Accolade
            {
                Id = "road-trip", Name = "Road Trip", Description = "Drive half the island's roads.", Xp = 1500,
                Test = (p, d, s) => d != null && d.RoadShare >= 0.5,
                Progress = (p, d, s) => d == null ? 0 : Math.Min(1, d.RoadShare / 0.5),
            },
            new Accolade
            {
                Id = "every-road", Name = "Every Road", Description = "Drive nine tenths of the island's roads.", Xp = 4000,
                Test = (p, d, s) => d != null && d.RoadShare >= 0.9,
                Progress = (p, d, s) => d == null ? 0 : Math.Min(1, d.RoadShare / 0.9),
            },
            new Accolade
            {
                Id = "cartographer", Name = "Cartographer", Description = "Uncover three quarters of the map.", Xp = 2000,
                Test = (p, d, s) => d != null && d.MapShare >= 0.75,
                Progress = (p, d, s) => d == null ? 0 : Math.Min(1, d.MapShare / 0.75),
            },
            new Accolade
            {
                Id = "speed-traps", Name = "Speed Demon", Description = "Three stars at every speed trap.", Xp = 1500,
                Test = (p, d, s) => StuntStars(s, StuntKind.SpeedTrap, out int n) == 3 * n && n > 0,
                Progress = (p, d, s) => Share(StuntStars(s, StuntKind.SpeedTrap, out int n), 3 * n),
            },
            new Accolade
            {
                Id = "speed-zones", Name = "Smooth Operator", Description = "Three stars at every speed zone.", Xp = 1500,
                Test = (p, d, s) => StuntStars(s, StuntKind.SpeedZone, out int n) == 3 * n && n > 0,
                Progress = (p, d, s) => Share(StuntStars(s, StuntKind.SpeedZone, out int n), 3 * n),
            },
            new Accolade
            {
                Id = "sideways", Name = "Sideways", Description = "Three stars at a drift zone.", Xp = 1000,
                Test = (p, d, s) =>
                {
                    if (s == null) return false;
                    foreach (var st in s.All) if (st.Kind == StuntKind.DriftZone && st.StarsFor(s.Best(st)) == 3) return true;
                    return false;
                },
            },
            new Accolade
            {
                Id = "airborne", Name = "Airborne", Description = "Jump 20 m at a danger sign.", Xp = 800,
                Test = (p, d, s) =>
                {
                    if (s == null) return false;
                    foreach (var st in s.All) if (st.Kind == StuntKind.DangerSign && s.Best(st) >= 20) return true;
                    return false;
                },
            },
            new Accolade
            {
                Id = "stars", Name = "Star Collector", Description = "Earn twenty stunt stars.", Xp = 2000,
                Test = (p, d, s) => StuntStars(s, null, out _) >= 20,
                Progress = (p, d, s) => Math.Min(1, StuntStars(s, null, out _) / 20.0),
            },
        };

        /// <summary>
        /// The accolades earned since the last check: each is recorded in the profile and pays its XP once.
        /// Discoveries and stunts may be null (their accolades then wait).
        /// </summary>
        public static List<Accolade> Check(Profile p, Discoveries d = null, StuntTracker s = null)
        {
            var got = new List<Accolade>();
            foreach (var a in All)
            {
                if (p.Accolades.Contains(a.Id) || !a.Test(p, d, s)) continue;
                p.Accolades.Add(a.Id);
                p.Xp += a.Xp;
                got.Add(a);
            }
            return got;
        }

        /// <summary>How far along an accolade is, 0 to 1 (1 once earned).</summary>
        public static double ProgressOf(Accolade a, Profile p, Discoveries d = null, StuntTracker s = null) =>
            p.Accolades.Contains(a.Id) ? 1 : a.Progress != null ? a.Progress(p, d, s) : 0;

        /// <summary>
        /// Records a finished race in the counters the accolades read: podiums, and wins by the kind of event
        /// (a drag race, the rally, a head to head, a time trial, the night race, the storm, the hill climb).
        /// </summary>
        public static void RecordRace(Profile p, RaceEvent ev, int position, bool won)
        {
            void Add(string k)
            {
                p.Stats.TryGetValue(k, out int v);
                p.Stats[k] = v + 1;
            }
            if (position >= 1 && position <= 3 && !ev.Ghost) Add(Podiums);
            if (!won) return;
            if (ev.Kind == RaceKind.Drag) Add(DragWins);
            if (ev.Surface == Surface.Dirt) Add(RallyWins);
            if (ev.Ghost) Add(GhostWins);
            else if (ev.Rival != null) Add(DuelWins);
            if (ev.Hour.HasValue && (ev.Hour.Value >= 21 || ev.Hour.Value < 5)) Add(NightWins);
            if (ev.Weather == Sky.Storm) Add(StormWins);
            if (ev.Id == "sprint-highcrest") Add(ClimbWins);
        }
    }
}
