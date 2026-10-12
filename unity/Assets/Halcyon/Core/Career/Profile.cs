// The player's career: credits, XP, and the cars they own with their parts,
// tune and paint. Saved as JSON (written here, read back with MiniJson);
// Unity keeps it in Application.persistentDataPath.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Halcyon.Core
{
    public sealed class OwnedCar
    {
        public string Id;
        /// <summary>Part level per category (missing = stock).</summary>
        public Dictionary<PartCategory, int> Parts = new Dictionary<PartCategory, int>();
        /// <summary>The car's tune, or null for the default.</summary>
        public Tune Tune;
        /// <summary>Paint colour (linear 0..1), or null for the car's own.</summary>
        public double[] Paint;
        /// <summary>The PI with these parts and this tune (refreshed when they change).</summary>
        public int Pi;

        public CarSpec Spec => Cars.ById(Id);
        public Upgrades Upgrades => Halcyon.Core.Parts.Build(Spec, Parts);
        public Tune TuneOrDefault => Tune ?? Tune.Default(Spec);
        public int Level(PartCategory c) => Parts.TryGetValue(c, out int l) ? l : 0;
    }

    public sealed class Profile
    {
        public const int Version = 1;
        public int Credits = Economy.StartingCredits;
        public int Xp;
        public int Current;
        public int Races, Wins;
        public readonly List<OwnedCar> Garage = new List<OwnedCar>();
        /// <summary>The island (Discoveries): places and landmarks found, roads driven and the map uncovered (base64 bitsets), road milestones paid.</summary>
        public readonly HashSet<string> Found = new HashSet<string>();
        public string RoadsDriven = "", Fog = "";
        public int RoadMilestones;
        /// <summary>The best score at each stunt, by its id.</summary>
        public readonly Dictionary<string, double> StuntBest = new Dictionary<string, double>();
        /// <summary>Accolades earned (by id), and the counters they read (Accolades.Podiums, ...).</summary>
        public readonly HashSet<string> Accolades = new HashSet<string>();
        public readonly Dictionary<string, int> Stats = new Dictionary<string, int>();
        /// <summary>Championships won (by id), and the one under way (null when none).</summary>
        public readonly HashSet<string> ChampionshipsWon = new HashSet<string>();
        public ChampionshipRun Championship;

        public OwnedCar CurrentCar => Garage.Count == 0 ? null : Garage[Math.Max(0, Math.Min(Garage.Count - 1, Current))];

        static bool Known(string id) => Array.Exists(Cars.All, c => c.id == id);

        /// <summary>A new career with one car.</summary>
        public static Profile New(string starter = "halden-aster-gt")
        {
            var p = new Profile();
            p.Garage.Add(new OwnedCar { Id = starter });
            return p;
        }

        public bool Owns(string id)
        {
            foreach (var c in Garage) if (c.Id == id) return true;
            return false;
        }

        /// <summary>Buys a car (it becomes the current car). False with a reason if not possible.</summary>
        public bool BuyCar(string id, int price, out string why)
        {
            why = null;
            if (!Known(id)) { why = "no such car"; return false; }
            if (Credits < price) { why = $"needs {price - Credits:N0} more credits"; return false; }
            Credits -= price;
            Garage.Add(new OwnedCar { Id = id });
            Current = Garage.Count - 1;
            return true;
        }

        /// <summary>Sells a car (not the last one).</summary>
        public bool SellCar(int index, int price, out string why)
        {
            why = null;
            if (Garage.Count <= 1) { why = "you need at least one car"; return false; }
            if (index < 0 || index >= Garage.Count) { why = "no such car"; return false; }
            Garage.RemoveAt(index);
            Credits += price;
            // Keep pointing at the same car when one before it goes.
            if (index < Current) Current--;
            if (Current >= Garage.Count) Current = Garage.Count - 1;
            return true;
        }

        /// <summary>Fits a part (level 0 returns the category to stock for free; parts already bought are not refunded).</summary>
        public bool FitPart(OwnedCar car, PartCategory c, int level, int price, out string why)
        {
            why = null;
            if (car.Level(c) == level) return true;
            if (level > 0 && Credits < price) { why = $"needs {price - Credits:N0} more credits"; return false; }
            if (level > 0) Credits -= price;
            if (level <= 0) car.Parts.Remove(c);
            else car.Parts[c] = level;
            return true;
        }

        /// <summary>Records a finished race: credits, XP and wins.</summary>
        public void AddRace(int credits, int xp, bool won)
        {
            Credits += credits;
            Xp += xp;
            Races++;
            if (won) Wins++;
        }

        // --- JSON --------------------------------------------------------------------------------
        public string ToJson()
        {
            var cars = new List<object>();
            foreach (var c in Garage)
            {
                var parts = new Dictionary<string, object>();
                foreach (var kv in c.Parts) parts[kv.Key.ToString()] = kv.Value;
                var o = new Dictionary<string, object> { ["id"] = c.Id, ["parts"] = parts, ["pi"] = c.Pi };
                if (c.Tune != null) o["tune"] = TuneToDict(c.Tune);
                if (c.Paint != null) o["paint"] = new List<object> { c.Paint[0], c.Paint[1], c.Paint[2] };
                cars.Add(o);
            }
            var root = new Dictionary<string, object>
            {
                ["version"] = Version,
                ["credits"] = Credits,
                ["xp"] = Xp,
                ["current"] = Current,
                ["races"] = Races,
                ["wins"] = Wins,
                ["garage"] = cars,
                ["found"] = new List<object>(new SortedSet<string>(Found)),
                ["roads"] = RoadsDriven,
                ["fog"] = Fog,
                ["roadMilestones"] = RoadMilestones,
                ["stunts"] = StuntsJson(),
                ["accolades"] = new List<object>(new SortedSet<string>(Accolades, StringComparer.Ordinal)),
                ["stats"] = SortedInts(Stats),
                ["champsWon"] = new List<object>(new SortedSet<string>(ChampionshipsWon, StringComparer.Ordinal)),
            };
            if (Championship != null) root["championship"] = Championship.ToDict();
            return JsonWriter.Write(root);
        }

        static Dictionary<string, object> SortedInts(Dictionary<string, int> d)
        {
            var o = new Dictionary<string, object>();
            foreach (var k in new SortedSet<string>(d.Keys, StringComparer.Ordinal)) o[k] = d[k];
            return o;
        }

        Dictionary<string, object> StuntsJson()
        {
            // Sorted, so the same profile always writes the same text.
            var d = new Dictionary<string, object>();
            foreach (var k in new SortedSet<string>(StuntBest.Keys, StringComparer.Ordinal)) d[k] = StuntBest[k];
            return d;
        }

        public static Profile FromJson(string json)
        {
            var root = Json.Obj(MiniJson.Parse(json));
            var p = new Profile
            {
                Credits = root.Int("credits"),
                Xp = root.Int("xp"),
                Current = root.Int("current"),
                Races = (int)root.Num("races", 0),
                Wins = (int)root.Num("wins", 0),
            };
            foreach (var co in root.Arr("garage"))
            {
                var o = Json.Obj(co);
                var id = (string)o["id"];
                if (!Known(id)) continue; // a car this version no longer has
                var car = new OwnedCar { Id = id, Pi = (int)o.Num("pi", 0) };
                if (o.TryGetValue("parts", out var po) && po is Dictionary<string, object> parts)
                {
                    foreach (var kv in parts)
                    {
                        if (Enum.TryParse(kv.Key, out PartCategory c)) car.Parts[c] = Convert.ToInt32(kv.Value, CultureInfo.InvariantCulture);
                    }
                }
                if (o.TryGetValue("tune", out var to) && to is Dictionary<string, object> td) car.Tune = TuneFromDict(td, car.Spec);
                if (o.TryGetValue("paint", out var pa) && pa is List<object> pl && pl.Count == 3)
                {
                    car.Paint = new[] { Convert.ToDouble(pl[0], CultureInfo.InvariantCulture), Convert.ToDouble(pl[1], CultureInfo.InvariantCulture), Convert.ToDouble(pl[2], CultureInfo.InvariantCulture) };
                }
                p.Garage.Add(car);
            }
            // The island: saves from before M6 have none of this.
            if (root.TryGetValue("found", out var fo) && fo is List<object> fl)
                foreach (var f in fl) if (f is string fs) p.Found.Add(fs);
            if (root.TryGetValue("roads", out var ro) && ro is string rs) p.RoadsDriven = rs;
            if (root.TryGetValue("fog", out var fg) && fg is string fgs) p.Fog = fgs;
            p.RoadMilestones = (int)root.Num("roadMilestones", 0);
            if (root.TryGetValue("stunts", out var so) && so is Dictionary<string, object> sd)
                foreach (var kv in sd)
                    if (kv.Value is double dv && !double.IsNaN(dv) && dv > 0) p.StuntBest[kv.Key] = dv;
            // Progression (M7): saves from before it have none of this.
            if (root.TryGetValue("accolades", out var ao) && ao is List<object> al)
                foreach (var a in al) if (a is string a_) p.Accolades.Add(a_);
            if (root.TryGetValue("stats", out var sto) && sto is Dictionary<string, object> std)
                foreach (var kv in std) if (kv.Value is double v) p.Stats[kv.Key] = (int)v;
            if (root.TryGetValue("champsWon", out var cwo) && cwo is List<object> cwl)
                foreach (var c in cwl) if (c is string cs) p.ChampionshipsWon.Add(cs);
            if (root.TryGetValue("championship", out var cho) && cho is Dictionary<string, object> chd) p.Championship = ChampionshipRun.FromDict(chd);
            if (p.Garage.Count == 0) p.Garage.Add(new OwnedCar { Id = "halden-aster-gt" });
            p.Current = Math.Max(0, Math.Min(p.Garage.Count - 1, p.Current));
            return p;
        }

        static Dictionary<string, object> TuneToDict(Tune t)
        {
            var d = new Dictionary<string, object>();
            foreach (var f in TuneSpec.Fields) d[f.Key] = f.Get(t, 0);
            var gears = new List<object>();
            if (t.gearScale != null) foreach (var g in t.gearScale) gears.Add(g);
            d["gearScale"] = gears;
            return d;
        }

        static Tune TuneFromDict(Dictionary<string, object> d, CarSpec spec)
        {
            var t = Tune.Default(spec);
            foreach (var f in TuneSpec.Fields)
            {
                if (d.TryGetValue(f.Key, out var v) && v != null) f.Set(t, 0, Convert.ToDouble(v, CultureInfo.InvariantCulture));
            }
            if (d.TryGetValue("gearScale", out var g) && g is List<object> gl)
            {
                for (int i = 0; i < Math.Min(gl.Count, t.gearScale.Length); i++) t.gearScale[i] = Convert.ToDouble(gl[i], CultureInfo.InvariantCulture);
            }
            return t;
        }
    }

    /// <summary>Writes dictionaries, lists, numbers, strings and booleans as JSON.</summary>
    public static class JsonWriter
    {
        public static string Write(object o)
        {
            var sb = new StringBuilder();
            Value(sb, o);
            return sb.ToString();
        }

        static void Value(StringBuilder sb, object o)
        {
            switch (o)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: Str(sb, s); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case double d: sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture)); break;
                case Dictionary<string, object> obj:
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in obj)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Str(sb, kv.Key);
                        sb.Append(':');
                        Value(sb, kv.Value);
                    }
                    sb.Append('}');
                    break;
                case List<object> arr:
                    sb.Append('[');
                    for (int k = 0; k < arr.Count; k++)
                    {
                        if (k > 0) sb.Append(',');
                        Value(sb, arr[k]);
                    }
                    sb.Append(']');
                    break;
                default: throw new ArgumentException($"JsonWriter: unsupported {o.GetType().Name}");
            }
        }

        static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
