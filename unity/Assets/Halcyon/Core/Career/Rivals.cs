// Rivals matched to the player's performance index. Every car has the same
// upgrade ladder (stock, then tyres, engine, weight and so on, a step at a
// time). A rival's car is the ladder step whose measured PI comes closest to
// the player's from below, without leaving the event's class. PIs are
// measured, never estimated, so they are cached per car and step and
// measured in parallel.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Halcyon.Core
{
    /// <summary>An AI entrant: the car, its parts (none = stock), its PI and the driver.</summary>
    public sealed class Rival
    {
        public string CarId;
        public Personality Driver;
        public Dictionary<PartCategory, int> Parts = new Dictionary<PartCategory, int>();
        /// <summary>The car's tune (null: the default).</summary>
        public Tune Tune;
        public int Pi;

        public Upgrades Upgrades => Halcyon.Core.Parts.Build(Cars.ById(CarId), Parts);
        /// <summary>Names the car's setup, so rivals with the same setup share one measurement.</summary>
        public string Key => Rivals.PartsKey(Parts) + (Tune == null ? "" : "|" + TuneSpec.Encode(Cars.ById(CarId), Tune));
    }

    /// <summary>Measured stats per car and set of parts (default tune). Thread-safe.</summary>
    public sealed class PiTable
    {
        readonly ConcurrentDictionary<string, CarStats> map = new ConcurrentDictionary<string, CarStats>();

        public CarStats Get(string carId, IReadOnlyDictionary<PartCategory, int> parts)
        {
            string key = carId + "|" + Rivals.PartsKey(parts);
            if (map.TryGetValue(key, out var s)) return s;
            var spec = Cars.ById(carId);
            s = Performance.Measure(spec, null, Parts.Build(spec, parts));
            return map.GetOrAdd(key, s);
        }

        public int Count => map.Count;
    }

    public static class Rivals
    {
        static Dictionary<PartCategory, int> P(int tires, int engine, int weight, int brakes, int width, int aero) =>
            new Dictionary<PartCategory, int>
            {
                [PartCategory.Tires] = tires,
                [PartCategory.Engine] = engine,
                [PartCategory.Weight] = weight,
                [PartCategory.Brakes] = brakes,
                [PartCategory.TireWidth] = width,
                [PartCategory.Aero] = aero,
            };

        /// <summary>
        /// The upgrade steps, stock first. Each step keeps every part of the
        /// one before, at the same or a higher level, so the PI rises along the
        /// ladder (the career tests check this for every car).
        /// </summary>
        public static readonly Dictionary<PartCategory, int>[] Ladder =
        {
            P(0, 0, 0, 0, 0, 0),
            P(1, 0, 0, 0, 0, 0),
            P(1, 1, 0, 0, 0, 0),
            P(1, 1, 1, 1, 0, 0),
            P(2, 1, 1, 1, 1, 0),
            P(2, 2, 2, 1, 1, 1),
            P(2, 2, 2, 2, 2, 1),
            P(3, 3, 2, 2, 2, 2),
            P(3, 3, 3, 2, 2, 2),
        };

        /// <summary>A canonical name for a set of parts ("stock" when there are none).</summary>
        public static string PartsKey(IReadOnlyDictionary<PartCategory, int> parts)
        {
            if (parts == null) return "stock";
            var sb = new StringBuilder();
            foreach (PartCategory c in Enum.GetValues(typeof(PartCategory)))
            {
                if (parts.TryGetValue(c, out int level) && level > 0) sb.Append((int)c).Append(':').Append(level).Append(' ');
            }
            return sb.Length == 0 ? "stock" : sb.ToString(0, sb.Length - 1);
        }

        /// <summary>The highest ladder step whose PI is at most cap, or -1 when the car is above it even stock.</summary>
        public static int StepBelow(string carId, int cap, PiTable table)
        {
            if (table.Get(carId, Ladder[0]).Pi > cap) return -1;
            int lo = 0, hi = Ladder.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (table.Get(carId, Ladder[mid]).Pi <= cap) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        /// <summary>The PI rivals may reach for a player at playerPi: a little above it, never above the class limit.</summary>
        public static int Cap(int playerPi, PiClass? limit)
        {
            int cap = playerPi + 15;
            return limit.HasValue ? Math.Min(cap, Performance.ClassMax(limit.Value)) : cap;
        }

        /// <summary>
        /// count rivals for a player whose car measures playerPi, in an event
        /// limited to a class (null: open). Each car brings the ladder step that
        /// comes closest to the player's PI without passing Cap. Cars within 40
        /// PI of the player make the pool; when none do, the closest car that
        /// fits the class races alone (a one-make race is fairer than a
        /// mismatched one).
        /// </summary>
        public static List<Rival> Field(int playerPi, PiClass? limit, int count, Difficulty d, uint seed, PiTable table)
        {
            int cap = Cap(playerPi, limit);
            int classMax = limit.HasValue ? Performance.ClassMax(limit.Value) : int.MaxValue;
            var all = Cars.All;
            var step = new int[all.Length];
            var pi = new int[all.Length];
            Parallel.For(0, all.Length, i =>
            {
                step[i] = StepBelow(all[i].id, cap, table);
                pi[i] = table.Get(all[i].id, Ladder[Math.Max(0, step[i])]).Pi;
            });
            var pool = new List<int>();
            for (int i = 0; i < all.Length; i++) if (step[i] >= 0 && pi[i] >= playerPi - 40) pool.Add(i);
            if (pool.Count == 0)
            {
                int best = -1;
                for (int i = 0; i < all.Length; i++)
                {
                    if (pi[i] > classMax) continue;
                    if (best < 0 || Math.Abs(pi[i] - playerPi) < Math.Abs(pi[best] - playerPi)) best = i;
                }
                if (best < 0) best = 0;
                pool.Add(best);
            }
            pool.Sort((a, b) => Math.Abs(pi[a] - playerPi).CompareTo(Math.Abs(pi[b] - playerPi)));
            var rng = new Rng(seed);
            var drivers = Personalities.Field(d, count, rng);
            // A seeded shuffle, then each car in turn: a model repeats only when
            // the pool is smaller than the field.
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rng.Int(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            var field = new List<Rival>();
            for (int i = 0; i < count; i++)
            {
                int c = pool[i % pool.Count];
                field.Add(new Rival
                {
                    CarId = all[c].id,
                    Driver = drivers[i],
                    Parts = new Dictionary<PartCategory, int>(Ladder[Math.Max(0, step[c])]),
                    Pi = pi[c],
                });
            }
            return field;
        }

        /// <summary>Stock cars for a list of (car, driver) pairs.</summary>
        public static List<Rival> Stock(IList<(string carId, Personality p)> opponents)
        {
            var list = new List<Rival>();
            foreach (var (carId, p) in opponents) list.Add(new Rival { CarId = carId, Driver = p });
            return list;
        }
    }
}
