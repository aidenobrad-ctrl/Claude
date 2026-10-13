// Championships: three or four of the festival's events raced as a series in
// one car against the same rivals, with points for every finish (25, 18, 15,
// 12, 10, 8, 6, 4) and a prize for the top three of the standings. The
// series in progress is kept in the profile, so a championship can be
// carried on after quitting. No UnityEngine.
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    public sealed class Championship
    {
        public string Id, Name, Description;
        /// <summary>The highest class allowed (as an event's); the series is raced in one car that fits it.</summary>
        public PiClass? ClassLimit;
        /// <summary>The rounds, as festival event ids, in order.</summary>
        public readonly List<string> Rounds = new List<string>();
        /// <summary>The festival level needed to enter.</summary>
        public int MinLevel = 1;

        /// <summary>Points for first to eighth.</summary>
        public static readonly int[] Points = { 25, 18, 15, 12, 10, 8, 6, 4 };
        public static int PointsFor(int position) => position >= 1 && position <= Points.Length ? Points[position - 1] : 0;

        /// <summary>The prize for the final standings (first, second, third), credits before the class factor.</summary>
        public static readonly int[] Prize = { 40000, 20000, 10000 };
        /// <summary>XP for the final standings.</summary>
        public static readonly int[] PrizeXp = { 3000, 1800, 1000 };

        /// <summary>The festival's championships, from the events it has (a series whose events are missing is left out).</summary>
        public static List<Championship> Festival(List<RaceEvent> events)
        {
            var list = new List<Championship>();
            void Add(string id, string name, string description, PiClass? limit, int level, params string[] rounds)
            {
                var c = new Championship { Id = id, Name = name, Description = description, ClassLimit = limit, MinLevel = level };
                foreach (var r in rounds)
                {
                    if (events.Find(e => e.Id == r) == null) return;
                    c.Rounds.Add(r);
                }
                list.Add(c);
            }
            Add("champ-grassroots", "Grassroots Trophy", "Three rounds for class D cars: the coast, the farms and the proving ground.", PiClass.D, 1,
                "sprint-saltmarsh", "sprint-ashby", "proving-gp");
            Add("champ-coastal", "Coastal Cup", "The city, the harbour and three laps of the proving ground, in class A.", PiClass.A, 3,
                "sprint-port-calder", "sprint-harbor", "proving-gp");
            Add("champ-mountain", "Mountain Masters", "The hill climb, the rally, the storm run and the night race: the island at its hardest, class A.", PiClass.A, 6,
                "sprint-highcrest", "rally-redmesa", "sprint-storm", "proving-gp-night");
            return list;
        }

        public RaceEvent Event(List<RaceEvent> events, int round) => round >= 0 && round < Rounds.Count ? events.Find(e => e.Id == Rounds[round]) : null;
    }

    /// <summary>A championship under way: its round, the car, the rivals' seed (the same field each round) and the standings.</summary>
    public sealed class ChampionshipRun
    {
        public string Id;
        /// <summary>The next round to race (0-based); Rounds.Count when the series is over.</summary>
        public int Round;
        /// <summary>The field's seed: the same rivals each round.</summary>
        public uint Seed;
        /// <summary>The car the series is raced in (the player's garage index is not kept: cars can be sold).</summary>
        public string CarId;
        /// <summary>The rivals' difficulty, set when the series starts.</summary>
        public Difficulty Rivals = Difficulty.Pro;
        /// <summary>Points by driver name ("You" for the player).</summary>
        public readonly Dictionary<string, int> Standings = new Dictionary<string, int>();
        /// <summary>The player's finishing position each round raced.</summary>
        public readonly List<int> Places = new List<int>();

        public bool Over(Championship c) => Round >= c.Rounds.Count;

        /// <summary>Adds a round's result: every driver's points from its finishing order (names, first to last).</summary>
        public void Score(IList<string> order)
        {
            for (int i = 0; i < order.Count; i++)
            {
                Standings.TryGetValue(order[i], out int pts);
                Standings[order[i]] = pts + Championship.PointsFor(i + 1);
                if (order[i] == "You") Places.Add(i + 1);
            }
            Round++;
        }

        /// <summary>The standings, best first (ties by name, so the order is stable).</summary>
        public List<KeyValuePair<string, int>> Table()
        {
            var t = new List<KeyValuePair<string, int>>(Standings);
            t.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
            return t;
        }

        /// <summary>The player's place in the standings (1-based), 0 if they have no points yet.</summary>
        public int PlayerPlace()
        {
            var t = Table();
            for (int i = 0; i < t.Count; i++) if (t[i].Key == "You") return i + 1;
            return 0;
        }

        public Dictionary<string, object> ToDict()
        {
            var pts = new Dictionary<string, object>();
            foreach (var kv in Table()) pts[kv.Key] = kv.Value;
            var places = new List<object>();
            foreach (var p in Places) places.Add(p);
            return new Dictionary<string, object> { ["id"] = Id, ["round"] = Round, ["seed"] = (double)Seed, ["car"] = CarId, ["rivals"] = (double)(int)Rivals, ["points"] = pts, ["places"] = places };
        }

        public static ChampionshipRun FromDict(Dictionary<string, object> d)
        {
            var r = new ChampionshipRun
            {
                Id = d.TryGetValue("id", out var id) ? id as string : null,
                Round = d.TryGetValue("round", out var ro) && ro is double rd ? (int)rd : 0,
                Seed = d.TryGetValue("seed", out var se) && se is double sd ? (uint)sd : 1,
                CarId = d.TryGetValue("car", out var ca) ? ca as string : null,
                Rivals = d.TryGetValue("rivals", out var rv) && rv is double rvd && rvd >= 0 && rvd <= 3 ? (Difficulty)(int)rvd : Difficulty.Pro,
            };
            if (d.TryGetValue("points", out var po) && po is Dictionary<string, object> pd)
                foreach (var kv in pd) if (kv.Value is double v) r.Standings[kv.Key] = (int)v;
            if (d.TryGetValue("places", out var pl) && pl is List<object> pll)
                foreach (var p in pll) if (p is double pv) r.Places.Add((int)pv);
            return r.Id == null ? null : r;
        }
    }
}
