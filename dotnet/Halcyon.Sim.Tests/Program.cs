// The living island, headless: the clock and the sun, the weather and the
// road wetness it gives the physics, and the AI racing in the wet.
//   cd dotnet/Halcyon.Sim.Tests && dotnet run -c Release [-- clock|weather|sim|wetrace]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Halcyon.Core;

static class Program
{
    static int failures;
    static WorldData W;

    static void Check(bool ok, string what)
    {
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {what}");
        if (!ok) failures++;
    }

    static string Root()
    {
        string d = AppContext.BaseDirectory;
        while (d != null && !Directory.Exists(Path.Combine(d, "unity"))) d = Path.GetDirectoryName(d);
        return d ?? throw new Exception("repo root not found");
    }

    static WorldData World()
    {
        if (W == null) W = WorldData.LoadFromDirectory(Path.Combine(Root(), "unity/Assets/Halcyon/Data/Resources/HalcyonIsland"));
        return W;
    }

    static int Main(string[] args)
    {
        string only = args.Length > 0 ? args[0] : "";
        var sw = Stopwatch.StartNew();
        if (only == "" || only == "clock") Clock();
        if (only == "" || only == "weather") WeatherStats();
        if (only == "" || only == "sim") SimWiring();
        if (only == "" || only == "wetrace") WetRaces();
        if (only == "" || only == "festival") FestivalEvents();
        if (only == "" || only == "traffic") TrafficTests();
        if (only == "trafficdebug") TrafficDebug();
        if (only == "" || only == "highcrest") Highcrest();
        if (only == "" || only == "discover") DiscoveryTests();
        if (only == "coast") CoastDebug();
        if (only == "" || only == "gps") GpsTests();
        if (only == "stuntsurvey") StuntSurvey();
        Console.WriteLine($"\n{(failures == 0 ? "PASS" : $"FAIL ({failures})")} in {sw.Elapsed.TotalSeconds:0.0} s");
        return failures == 0 ? 0 : 1;
    }

    static double Deg(double r) => r * 180 / Math.PI;

    // --- The clock and the sun --------------------------------------------------------------

    static void Clock()
    {
        Console.WriteLine("\nThe clock and the sun");
        var c = new WorldClock();
        double rise = WorldClock.SunriseHour, set = WorldClock.SunsetHour;
        Console.WriteLine($"  sunrise {rise:0.00} h, sunset {set:0.00} h; day length {(set - rise):0.0} h");
        Check(rise > 5 && rise < 5.5 && Math.Abs(rise + set - 24) < 1e-9, "a summer festival day: sunrise between 5:00 and 5:30, symmetric about noon");
        c.Set(rise);
        Check(Math.Abs(c.SunElevation) < 1e-9, $"the sun is on the horizon at sunrise ({Deg(c.SunElevation):0.000000} degrees)");
        double riseAz = Deg(c.SunAzimuth);
        Check(riseAz > 60 && riseAz < 90, $"it rises north of east in summer ({riseAz:0.0} degrees from north)");
        c.Set(12);
        double noonEl = Deg(c.SunElevation), noonAz = Deg(c.SunAzimuth);
        Console.WriteLine($"  noon: elevation {noonEl:0.0} degrees, azimuth {noonAz:0.0}");
        Check(Math.Abs(noonEl - 70) < 1e-6 && Math.Abs(noonAz - 180) < 1e-6, "at noon the sun is due south, 70 degrees up (90 - 35 latitude + 15 declination)");
        c.SunDirection(out double sx, out double sy, out double sz);
        Check(Math.Abs(sx) < 1e-9 && sz > 0 && Math.Abs(sx * sx + sy * sy + sz * sz - 1) < 1e-12, "the noon sun direction points south (+Z in Core axes) and is a unit vector");
        c.Set(18);
        double eveAz = Deg(c.SunAzimuth);
        Check(eveAz > 240 && eveAz < 300 && c.SunElevation > 0, $"at 18:00 the sun is in the west and still up ({eveAz:0.0} degrees, {Deg(c.SunElevation):0.0} up)");
        c.Set(0);
        double midEl = Deg(c.SunElevation);
        c.MoonDirection(out _, out double my, out _);
        Check(midEl < -30 && my > 0.5 && c.Daylight == 0 && c.IsNight, $"at midnight the sun is far below ({midEl:0.0} degrees), the full moon high, no daylight, night");
        c.Set(12);
        Check(c.Daylight == 1 && !c.IsNight, "noon is full daylight");
        // Twilight eases: daylight climbs monotonically through the morning.
        double prev = -1;
        bool mono = true;
        for (double h = 3; h <= 9; h += 0.05)
        {
            c.Set(h);
            if (c.Daylight < prev - 1e-12) mono = false;
            prev = c.Daylight;
        }
        Check(mono, "daylight rises smoothly through dawn");
        // A 24-minute day; the day counter rolls over.
        c = new WorldClock();
        c.Set(23.5);
        for (int i = 0; i < 600; i++) c.Advance(0.1); // 60 real seconds: one hour
        Check(c.Day == 1 && Math.Abs(c.Hours - 0.5) < 1e-9, $"one real minute is one hour, and midnight starts day 1 ({c.Label}, day {c.Day})");
        c.Rate = 0;
        c.Advance(100);
        Check(Math.Abs(c.Hours - 0.5) < 1e-9, "rate 0 stops the clock");
        c.Set(-1);
        Check(c.Label == "23:00", $"times wrap ({c.Label})");
        c.Set(14.0833333333);
        Check(c.Label == "14:05", $"label {c.Label}");
    }

    // --- The weather ------------------------------------------------------------------------

    sealed class Series
    {
        public double[] TimeIn = new double[5];
        public int[] Entered = new int[5];
        public int DawnFog, DawnChanges, DayFog, DayChanges;
        public int Strikes, StrikesOutsideStorm;
        public double StormTime;
        public double RainWithoutCloud;
        public double MaxWet;
        public List<double> SoakTimes = new List<double>(), DryTimes = new List<double>();
        public double WetTime;
        public double Hash;
    }

    /// <summary>Runs the weather for hours of real time with a clock, gathering statistics.</summary>
    static Series RunWeather(uint seed, double hours, double dt = 0.1)
    {
        var w = new Weather(seed);
        var c = new WorldClock();
        var s = new Series();
        Sky last = w.State;
        double soakFrom = -1, dryFrom = -1;
        int steps = (int)(hours * 3600 / dt);
        for (int i = 0; i < steps; i++)
        {
            double t = i * dt;
            c.Advance(dt);
            w.Step(dt, c);
            s.TimeIn[(int)w.State] += dt;
            if (w.State != last)
            {
                s.Entered[(int)w.State]++;
                bool dawn = c.Hours >= 3 && c.Hours < 9, day = c.Hours >= 11 && c.Hours < 19;
                if (dawn) { s.DawnChanges++; if (w.State == Sky.Fog) s.DawnFog++; }
                if (day) { s.DayChanges++; if (w.State == Sky.Fog) s.DayFog++; }
                last = w.State;
            }
            if (w.Rain > 0.1 && w.Cloud < 0.6) s.RainWithoutCloud += dt;
            if (w.State == Sky.Storm) s.StormTime += dt;
            foreach (var k in w.Strikes)
            {
                s.Strikes++;
                if (w.State != Sky.Storm) s.StrikesOutsideStorm++;
            }
            w.Strikes.Clear();
            s.MaxWet = Math.Max(s.MaxWet, w.Wetness);
            if (w.Wetness > 0.1) s.WetTime += dt;
            // Soak: from the first drops on a dry road to the road 90% wet (a shower that stops first does not count).
            if (soakFrom < 0 && w.Rain > 0.05 && w.Wetness < 0.05) soakFrom = t;
            if (soakFrom >= 0 && w.Wetness > 0.9) { s.SoakTimes.Add(t - soakFrom); soakFrom = -1; }
            if (soakFrom >= 0 && w.Rain < 0.02) soakFrom = -1;
            // Dry: from the rain stopping on a soaked road to the road under 10% wet.
            if (dryFrom < 0 && w.Rain < 0.02 && w.Wetness > 0.85) dryFrom = t;
            if (dryFrom >= 0 && w.Wetness < 0.1) { s.DryTimes.Add(t - dryFrom); dryFrom = -1; }
            if (dryFrom >= 0 && w.Rain > 0.1) dryFrom = -1;
            s.Hash = s.Hash * 1.0000001 + w.Wetness + w.Cloud * 3 + w.Fog * 7;
        }
        return s;
    }

    static void WeatherStats()
    {
        Console.WriteLine("\nThe weather: 8 seeds, 24 real hours each (60 game days)");
        var runs = new Series[8];
        Parallel.For(0, runs.Length, i => runs[i] = RunWeather((uint)(1000 + i * 7919), 24));
        var all = new Series();
        foreach (var s in runs)
        {
            for (int k = 0; k < 5; k++) { all.TimeIn[k] += s.TimeIn[k]; all.Entered[k] += s.Entered[k]; }
            all.DawnFog += s.DawnFog; all.DawnChanges += s.DawnChanges; all.DayFog += s.DayFog; all.DayChanges += s.DayChanges;
            all.Strikes += s.Strikes; all.StrikesOutsideStorm += s.StrikesOutsideStorm; all.StormTime += s.StormTime;
            all.RainWithoutCloud += s.RainWithoutCloud;
            all.MaxWet = Math.Max(all.MaxWet, s.MaxWet);
            all.SoakTimes.AddRange(s.SoakTimes);
            all.DryTimes.AddRange(s.DryTimes);
            all.WetTime += s.WetTime;
        }
        double total = all.TimeIn.Sum();
        for (int k = 0; k < 5; k++)
            Console.WriteLine($"  {Weather.Name((Sky)k),-7} {all.TimeIn[k] / total * 100,5:0.0}% of the time, {all.Entered[k],4} spells, mean {(all.Entered[k] > 0 ? all.TimeIn[k] / all.Entered[k] / 60 : 0),4:0.0} min");
        double clear = all.TimeIn[0] / total, rainy = (all.TimeIn[2] + all.TimeIn[3]) / total;
        Check(clear > 0.3 && clear < 0.55, $"clear skies most of the time ({clear * 100:0}%)");
        Check(rainy > 0.12 && rainy < 0.3, $"rain or storm some of the time ({rainy * 100:0}%)");
        Check(all.Entered.All(n => n > 20), "every sky comes round many times");
        double dawnFog = (double)all.DawnFog / Math.Max(1, all.DawnChanges), dayFog = (double)all.DayFog / Math.Max(1, all.DayChanges);
        Console.WriteLine($"  fog: {dawnFog * 100:0}% of changes at dawn (3-9 h), {dayFog * 100:0}% by day (11-19 h)");
        Check(dawnFog > 2 * dayFog, "fog forms at dawn far more than by day");
        Check(all.RainWithoutCloud == 0, $"it never rains from a light sky ({all.RainWithoutCloud:0.0} s of rain under cloud cover below 0.6)");
        double rate = all.StormTime > 0 ? all.Strikes / all.StormTime : 0;
        Console.WriteLine($"  lightning: {all.Strikes} strikes in {all.StormTime / 60:0} storm minutes ({(rate > 0 ? 1 / rate : 0):0.0} s apart)");
        Check(all.StrikesOutsideStorm == 0 && rate > 1 / 14.0 && rate < 1 / 7.0, "lightning only in storms, every 7 to 14 s on average (the rain has to build first)");
        double soak = all.SoakTimes.Count > 0 ? all.SoakTimes.Average() : double.NaN, dry = all.DryTimes.Count > 0 ? all.DryTimes.Average() : double.NaN;
        Console.WriteLine($"  roads: soaked (90%) {soak:0} s after the rain sets in ({all.SoakTimes.Count} times), dry (10%) {dry / 60:0.0} min after it stops ({all.DryTimes.Count} times, {all.DryTimes.DefaultIfEmpty(0).Min() / 60:0.0} to {all.DryTimes.DefaultIfEmpty(0).Max() / 60:0.0} min); wet {all.WetTime / total * 100:0}% of the time");
        Check(all.MaxWet > 0.97 && all.SoakTimes.Count > 10 && soak > 45 && soak < 240, "rain soaks the roads in under four minutes from the first drops");
        Check(all.DryTimes.Count > 10 && dry > 180 && dry < 1200, "they dry in three to 20 minutes (fast in sun and wind, slow at night)");

        // Determinism: the same seed gives the same weather; another seed does not.
        var a = RunWeather(42, 2);
        var b = RunWeather(42, 2);
        var d = RunWeather(43, 2);
        Check(a.Hash == b.Hash && a.Hash != d.Hash, "the same seed gives the same weather, bit for bit; another seed differs");

        // Forced skies, holding, snapping.
        var w = new Weather(7);
        var c = new WorldClock();
        w.Force(Sky.Storm);
        for (int i = 0; i < 36000; i++) w.Step(0.1, c);
        Check(w.State == Sky.Storm && w.Locked && w.Wetness > 0.99 && w.Rain > 0.95, "a forced storm stays a storm for an hour and soaks the roads");
        w.Hold = true;
        w.Force(Sky.Clear);
        double held = w.Wetness;
        for (int i = 0; i < 6000; i++) w.Step(0.1, c);
        Check(w.Wetness == held && w.Rain < 0.01, "held (a race), the road stays as wet as it was while the sky clears");
        w.Hold = false;
        w.Release();
        for (int i = 0; i < 6000; i++) w.Step(0.1, c);
        Check(w.Wetness < held && !w.Locked, "released, the road dries and the sky changes on its own again");
        w.Force(Sky.Rain, snap: true);
        Check(w.Rain == Weather.Looks[(int)Sky.Rain].Rain && w.Wetness > 0.7, "a snapped rain starts at once on a wet road");
    }

    // --- The simulation carries the weather to every car -----------------------------------------

    static void SimWiring()
    {
        Console.WriteLine("\nThe simulation: weather to the physics, lightning to the presentation");
        var sim = new Simulation(World(), SpawnAt.Island);
        var ctl = Controls.Neutral();
        sim.Clock.Set(21.5);
        sim.Weather.Force(Sky.Storm, snap: true);
        sim.AddCar(Cars.All[1].id).vehicle.Place(sim.Player.vehicle.pos.x + 8, sim.Player.vehicle.pos.y, sim.Player.vehicle.pos.z, 0);
        int lightning = 0;
        double tStart = sim.Clock.Hours;
        for (int i = 0; i < 240 * 90; i++)
        {
            sim.Step(ctl);
            foreach (var e in sim.Events) if (e.kind == SimEventKind.Lightning) lightning++;
            sim.Events.Clear();
        }
        Check(sim.Cars.All(c => c.vehicle.wetness == sim.Weather.Wetness) && sim.Wetness > 0.9, $"every car drives on the weather's road wetness ({sim.Wetness:0.000})");
        Check(lightning >= 4 && lightning <= 25, $"a storm's lightning reaches the presentation as events ({lightning} in 90 s)");
        Check(Math.Abs(sim.Clock.Hours - tStart - 1.5) < 1e-6, $"90 s of driving is an hour and a half on the clock ({sim.Clock.Label})");
        sim.Reset();
        Check(sim.Weather.State == Sky.Storm && sim.Clock.Hours > 22.9, "the weather and the clock carry on across a reset");
        sim.WetnessOverride = 0;
        sim.Step(ctl);
        Check(sim.Player.vehicle.wetness == 0, "a wetness override (the test track) replaces the weather's");
        sim.WetnessOverride = -1;
        sim.WorldTime = false;
        double h = sim.Clock.Hours;
        for (int i = 0; i < 2400; i++) sim.Step(ctl);
        Check(sim.Clock.Hours == h, "with world time off the clock stands still");
    }

    // --- The AI in the wet ----------------------------------------------------------------------

    sealed class RaceResult
    {
        public double Time;
        public int Resets, Contacts, Walls;
        public bool Finished;
        public double MaxStuck;
    }

    /// <summary>A field of six on an event at a wetness. dryPlan: the AI plans with dry abilities (the control).</summary>
    static RaceResult Race(RaceEvent ev, double wetness, bool dryPlan, uint seed, RaceSession.AbilityCache abilities)
    {
        var sim = new Simulation(World(), SpawnAt.Island) { WorldTime = false };
        sim.Weather.Wetness = dryPlan ? 0 : wetness;
        string[] cars = { "halden-aster-gt", "aurelle-vif-r", "nordvik-kyst-sport", "hoshida-kiri-rs", "stallworth-ironside-454", "halden-280-sprint" };
        var field = new List<(string, Personality)>();
        for (int i = 0; i < cars.Length; i++) field.Add((cars[i], new Personality { Name = $"D{i}", Skill = 0.75 + 0.05 * i, Aggression = 0.5, Consistency = 0.9, Risk = 0.5 }));
        var race = new RaceSession(sim, ev, field, abilities, seed, includePlayer: false);
        if (dryPlan) sim.WetnessOverride = wetness;
        var c = Controls.Neutral();
        while (!race.AllFinished && race.Clock < 900) race.Step(c);
        var r = new RaceResult
        {
            Finished = race.AllFinished,
            Time = race.Entrants.Where(e => e.Finished).Select(e => e.FinishTime).DefaultIfEmpty(double.NaN).Min(),
            Resets = race.Entrants.Sum(e => e.Ai.Resets),
            Contacts = race.Contacts,
            Walls = race.WallHits,
            MaxStuck = race.Entrants.Max(e => e.Ai.MaxStuck),
        };
        race.End();
        return r;
    }

    static void WetRaces()
    {
        Console.WriteLine("\nThe AI in the wet: six cars per race, dry against soaked, and soaked with the AI planning for dry roads (the control)");
        var sim0 = new Simulation(World(), SpawnAt.Island);
        var events = EventCatalog.All(World(), sim0.Ground);
        var abilities = new RaceSession.AbilityCache();
        var jobs = new List<(RaceEvent ev, double wet, bool dryPlan, uint seed)>();
        foreach (var ev in events)
            foreach (uint seed in new uint[] { 1, 2 })
            {
                jobs.Add((ev, 0, false, seed));
                jobs.Add((ev, 1, false, seed));
                jobs.Add((ev, 1, true, seed));
            }
        var results = new RaceResult[jobs.Count];
        var sw = Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, i => results[i] = Race(jobs[i].ev, jobs[i].wet, jobs[i].dryPlan, jobs[i].seed, abilities));
        Console.WriteLine($"  {jobs.Count} races in {sw.Elapsed.TotalSeconds:0} s");
        int wetResets = 0, ctrlResets = 0, dryResets = 0, wetWalls = 0, ctrlWalls = 0, dryWalls = 0;
        bool allFinished = true, slower = true;
        for (int i = 0; i < jobs.Count; i += 3)
        {
            var (ev, _, _, seed) = jobs[i];
            RaceResult dry = results[i], wet = results[i + 1], ctrl = results[i + 2];
            double slow = wet.Time / dry.Time - 1;
            Console.WriteLine($"  {ev.Id,-20} seed {seed}: dry {Fmt(dry.Time)} ({dry.Resets} resets, {dry.Walls} wall hits)  wet {Fmt(wet.Time)} {slow * 100:+0;-0}% ({wet.Resets} resets, {wet.Walls} wall hits, {wet.Contacts} contacts)  wet, planned dry: {Fmt(ctrl.Time)} ({ctrl.Resets} resets, {ctrl.Walls} wall hits)");
            allFinished &= dry.Finished && wet.Finished && wet.MaxStuck < 5;
            slower &= slow > 0.04 && slow < 0.35;
            wetResets += wet.Resets; ctrlResets += ctrl.Resets; dryResets += dry.Resets;
            wetWalls += wet.Walls; ctrlWalls += ctrl.Walls; dryWalls += dry.Walls;
        }
        Console.WriteLine($"  totals: dry {dryResets} resets / {dryWalls} wall hits; wet {wetResets} / {wetWalls}; wet planned dry {ctrlResets} / {ctrlWalls}");
        Check(allFinished, "every dry and wet race finishes, nobody stuck over 5 s");
        Check(slower, "the wet winner is 4% to 35% slower than the dry one on every event");
        Check(wetResets <= dryResets + 2, $"planning for the wet, the AI resets about as rarely as in the dry ({wetResets} against {dryResets})");
        Check(ctrlResets + ctrlWalls > wetResets + wetWalls, $"the control proves it matters: planning for dry roads in the wet goes worse ({ctrlResets} resets, {ctrlWalls} wall hits against {wetResets}, {wetWalls})");
    }

    // --- Events with their own conditions --------------------------------------------------------

    static void FestivalEvents()
    {
        Console.WriteLine("\nEvents with their own conditions: a night race and a storm");
        var sim = new Simulation(World(), SpawnAt.Island);
        var all = EventCatalog.All(World(), sim.Ground);
        var fest = EventCatalog.Festival(World(), sim.Ground);
        var night = fest.FirstOrDefault(e => e.Id == "proving-gp-night");
        var storm = fest.FirstOrDefault(e => e.Id == "sprint-storm");
        Check(fest.Count == all.Count + 3 && night != null && storm != null && fest.Any(e => e.Id == "sprint-highcrest"), $"the festival lists {fest.Count} events: the {all.Count} plus the Highcrest climb, a night race and a storm");
        Check(all.All(e => e.ConditionsText == null) && night.ConditionsText == "Night · Clear" && storm.ConditionsText == "Day · Storm", $"conditions read \"{night.ConditionsText}\" and \"{storm.ConditionsText}\"");
        foreach (var ev in new[] { night, storm })
        {
            var s = new Simulation(World(), SpawnAt.Island);
            EventCatalog.SetConditions(s, ev);
            double wet = RaceSession.WetnessFor(s);
            var field = new List<(string, Personality)>();
            string[] cars = { "halden-aster-gt", "aurelle-vif-r", "hoshida-kiri-rs", "nordvik-kyst-sport" };
            for (int i = 0; i < cars.Length; i++) field.Add((cars[i], new Personality { Name = $"D{i}", Skill = 0.8 + 0.05 * i, Aggression = 0.5, Consistency = 0.9, Risk = 0.5 }));
            var abilities = new RaceSession.AbilityCache();
            Parallel.ForEach(field, f => abilities.Get(f.Item1, wet));
            var race = new RaceSession(s, ev, field, abilities, 5, includePlayer: false);
            bool lightsOn = s.Clock.IsNight;
            var c = Controls.Neutral();
            int strikes = 0;
            while (!race.AllFinished && race.Clock < 900)
            {
                race.Step(c);
                foreach (var e in s.Events) if (e.kind == SimEventKind.Lightning) strikes++;
                s.Events.Clear();
            }
            int resets = race.Entrants.Sum(e => e.Ai.Resets);
            bool stillNight = s.Clock.IsNight;
            bool heldWet = race.Entrants.All(e => e.Car.vehicle.wetness == wet);
            Console.WriteLine($"  {ev.Name}: wetness {wet:0.0}, {s.Clock.Label} at the end, {Fmt(race.Order[0].FinishTime)} for the winner, {resets} resets, {race.Contacts} contacts, {strikes} lightning strikes");
            race.End();
            if (ev == night) Check(race.AllFinished && wet == 0 && lightsOn && stillNight && resets == 0 && s.Clock.Rate == 1, "the night race is at night from start to finish (time runs at a fifth while racing), on a dry road, and every car finishes cleanly");
            else Check(race.AllFinished && wet == 1 && heldWet && strikes > 3 && resets <= 1 && !s.Weather.Hold, "the storm soaks the road for the whole race (the AI planned for it), lightning flashes, every car finishes, and the weather is released after");
        }
    }

    // --- Discoveries, the map and fast travel --------------------------------------------------------

    static void DiscoveryTests()
    {
        Console.WriteLine("\nDiscoveries: landmarks, places found by driving, the fog of war, saves and fast travel");
        var sim = new Simulation(World(), SpawnAt.Island) { WorldTime = false };
        var lms = Landmarks.Find(World(), sim.Ground);
        var hit = new RoadHit();
        bool placed = true;
        foreach (var l in lms)
        {
            double rd = Landmarks.RoadDistance(World(), l.X, l.Z);
            Console.WriteLine($"  {l.Name,-22} {l.Kind,-12} at ({l.X:0}, {l.Z:0}), {l.Y:0} m up, {rd:0} m from a road, found within {l.Radius:0} m{(l.Parts.Count > 0 ? $", {l.Parts.Count} turbines" : "")}");
            placed &= rd >= 25 && rd < 650 && l.Y > 1 && l.Radius > rd + 50;
        }
        bool apart = true;
        for (int a = 0; a < lms.Count; a++)
            for (int b = a + 1; b < lms.Count; b++)
                apart &= Math.Sqrt((lms[a].X - lms[b].X) * (lms[a].X - lms[b].X) + (lms[a].Z - lms[b].Z) * (lms[a].Z - lms[b].Z)) > 800;
        Check(lms.Count == 5 && lms.Select(l => l.Kind).Distinct().Count() == 5, "five landmarks: mast, lighthouse, wind farm, arch, observatory");
        Check(placed && apart, "each on dry land, clear of the roads but found from the nearest one, and far apart");
        var farm = lms.FirstOrDefault(l => l.Kind == Landmarks.WindFarm);
        Check(farm != null && farm.Parts.Count >= 3, "the wind farm has its turbines");
        int solid = sim.Colliders.Circles.Count(c => lms.Any(l => Math.Abs(c.x - l.X) < 1e-9 && Math.Abs(c.z - l.Z) < 1e-9) || (farm != null && farm.Parts.Any(q => Math.Abs(c.x - q.x) < 1e-9 && Math.Abs(c.z - q.z) < 1e-9)));
        Check(solid >= lms.Count - 1 + (farm?.Parts.Count ?? 0) - 1, $"the structures are solid to the cars ({solid} colliders)");

        // Drive the Harbour Dash route with the AI and let the player's discoveries follow.
        var profile = Profile.New();
        var disc = new Discoveries(World(), sim.Ground, profile);
        Check(disc.RoadShare == 0 && disc.MapShare == 0 && profile.Found.Count == 0, "a new career has found nothing");
        var ev = EventCatalog.All(World(), sim.Ground).First(e => e.Id == "sprint-harbor");
        var race = new RaceSession(sim, ev, new List<(string, Personality)> { ("halden-aster-gt", new Personality { Name = "Bot", Skill = 0.8, Aggression = 0.5, Consistency = 1, Risk = 0.5 }) }, new RaceSession.AbilityCache(), 1, includePlayer: false);
        var car = race.Entrants[0].Car.vehicle;
        var c = Controls.Neutral();
        var notes = new List<string>();
        int xp0 = profile.Xp;
        while (!race.AllFinished && race.Clock < 600)
        {
            race.Step(c);
            if (sim.Tick % 60 == 0) notes.AddRange(disc.Update(car.pos.x, car.pos.z));
        }
        race.End();
        foreach (var n in notes) Console.WriteLine("    " + n);
        Console.WriteLine($"  after the Harbour Dash: {profile.Found.Count} places found, {disc.RoadShare * 100:0.0}% of the roads driven, {disc.MapShare * 100:0.0}% of the map uncovered, +{profile.Xp - xp0} XP");
        Check(profile.Found.Contains("place:Halcyon Festival") && profile.Found.Contains("place:Harbor"), "driving finds the festival and the harbour");
        Check(disc.RoadShare > 0.08 && disc.RoadShare < 0.2, "the roads driven are about the route's share of the network (7.6 of 63 km)");
        r_At(ev.Route, ev.Route.Length * 0.5, out double mx, out double mz);
        Check(disc.Uncovered(mx, mz) && !disc.Uncovered(-3500, -3500), "the fog clears along the route and nowhere else");
        Check(profile.Xp - xp0 >= 600, "finding places pays XP");

        // Saves: the career carries its discoveries; an old save (before M6) loads with none.
        disc.Save();
        var json = profile.ToJson();
        var back = Profile.FromJson(json);
        var disc2 = new Discoveries(World(), sim.Ground, back);
        Check(disc2.DrivenSamples == disc.DrivenSamples && disc2.FogCleared == disc.FogCleared && back.Found.SetEquals(profile.Found), "discoveries survive a save and load bit for bit");
        var old = Profile.FromJson("{\"version\":1,\"credits\":1000,\"xp\":0,\"current\":0,\"garage\":[{\"id\":\"halden-aster-gt\",\"parts\":{},\"pi\":0}]}");
        Check(old.Found.Count == 0 && new Discoveries(World(), sim.Ground, old).RoadShare == 0, "a save from before M6 loads with an undiscovered island");

        // Fast travel to every place found: onto a road, upright, settled.
        var targets = disc.FastTravelTargets();
        bool allGood = targets.Count >= 2;
        foreach (var t in targets)
        {
            var s2 = new Simulation(World(), SpawnAt.Island) { WorldTime = false };
            if (!disc.FastTravel(t, out double x, out double z, out double yaw)) { allGood = false; continue; }
            s2.Teleport(x, z, yaw);
            var hold = Controls.Neutral();
            hold.handbrake = 1;
            for (int i = 0; i < 480; i++) s2.Step(hold);
            var v = s2.Player.vehicle;
            bool onRoad = s2.OnRoad();
            double moved = Math.Sqrt((v.pos.x - x) * (v.pos.x - x) + (v.pos.z - z) * (v.pos.z - z));
            Console.WriteLine($"  fast travel to {t.Name}: ({x:0}, {z:0}), on the road {onRoad}, upright {v.up.y:0.00}, moved {moved:0.00} m, {Math.Sqrt((x - t.X) * (x - t.X) + (z - t.Z) * (z - t.Z)):0} m from the place");
            allGood &= (onRoad || t.Kind == "track") && v.up.y > 0.95 && moved < 1 && v.speed < 0.2; // the proving ground: its paddock
        }
        Check(allGood, $"fast travel to each of the {targets.Count} places found lands on a road, upright and still");
    }

    // --- The GPS -----------------------------------------------------------------------------------

    sealed class NoField : AiDriver.IField
    {
        public int Count => 0;
        public AiDriver.AiDriverView Get(int i) => default;
    }

    static double Dist(double ax, double az, double bx, double bz) => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

    static void GpsTests()
    {
        Console.WriteLine("\nGPS: a route to every place and landmark, a bot following one, straying from it, the wrong way");
        var w = World();
        var sim = new Simulation(w, SpawnAt.Island) { WorldTime = false };
        var gps = new Gps(w, sim.Ground);
        var places = new Discoveries(w, sim.Ground, Profile.New()).Places;
        var v = sim.Player.vehicle;
        int routed = 0, badStart = 0, badEnd = 0, badTurns = 0;
        Place far = null;
        double farLength = 0;
        foreach (var p in places)
        {
            if (!gps.Set(v.pos.x, v.pos.z, p.X, p.Z, p.Name))
            {
                Console.WriteLine($"  no route to {p.Name}");
                continue;
            }
            routed++;
            var r = gps.Route;
            r_At(r, 0, out double x0, out double z0);
            r_At(r, r.Length, out double x1, out double z1);
            // The route ends at the road nearest the destination.
            double nearest = double.PositiveInfinity;
            foreach (var e in w.Edges)
                for (int i = 0; i < e.n; i++) nearest = Math.Min(nearest, Dist(e.x[i], e.z[i], p.X, p.Z));
            if (Dist(x0, z0, v.pos.x, v.pos.z) > 30) badStart++;
            if (Dist(x1, z1, p.X, p.Z) > nearest + 10) badEnd++;
            double lastS = -1;
            foreach (var t in gps.Turns)
            {
                if (t.S <= lastS || (!t.Keep && Math.Abs(t.Angle) < Gps.TurnAngle) || (t.Keep && Math.Abs(t.Angle) >= Gps.TurnAngle) || w.Nodes[t.Node].edges.Count < 3) badTurns++;
                lastS = t.S;
            }
            if (p.Kind != "track" && r.Length > farLength)
            {
                farLength = r.Length;
                far = p;
            }
            Console.WriteLine($"  to {p.Name,-22} {Gps.Distance(r.Length),8} by road, {gps.Turns.Count(t => !t.Keep),2} turns and {gps.Turns.Count(t => t.Keep)} forks ({gps.Turns.Count(t => t.Right)} right), ends {Dist(x1, z1, p.X, p.Z):0} m from it (nearest road {nearest:0} m)");
        }
        Check(routed == places.Count, $"a road route from the festival to every place and landmark ({routed} of {places.Count})");
        Check(badStart == 0 && badEnd == 0, "each route starts at the car and ends at the road nearest the destination");
        Check(badTurns == 0, "turns and forks are called only at junctions, in order along the route");

        // 2. A bot follows the GPS to the farthest place, the GPS following it.
        gps.Set(v.pos.x, v.pos.z, far.X, far.Z, far.Name);
        var route = gps.Route;
        int turnsToCall = gps.Turns.Count;
        r_At(route, 0, out double sx, out double sz);
        route.At(0, out _, out _, out _, out double stx, out double stz);
        sim.Player.vehicle.Place(sx - stz * 2, sim.Ground.GroundHeight(sx, sz), sz + stx * 2, Simulation.YawOf(stx, stz));
        var ability = new RaceSession.AbilityCache().Get(sim.Player.specId);
        var bot = new AiDriver(sim.Player, new Personality { Name = "Bot", Skill = 0.6, Aggression = 0.3, Consistency = 1, Risk = 0.3 }, ability, 3);
        bot.SetRoute(route, RacingLine.KeepRight(route));
        var field = new NoField();
        double t0 = 0, worstBack = 0, lastProgress = 0;
        bool arrived = false, wrongWay = false;
        int called = 0, lastNode = -1, version = gps.Version;
        double callDistance = 0;
        var callAt = new List<double>();
        while (t0 < 900 && !arrived)
        {
            bot.Update(Loop.SIM_DT, field);
            var car = sim.Player.vehicle;
            var c = new Controls { throttle = car.input.throttle, brake = car.input.brake, steer = car.input.steer, handbrake = car.input.handbrake, analogSteer = true, source = InputSource.Script };
            sim.Step(c);
            sim.Events.Clear();
            t0 += Loop.SIM_DT;
            if (sim.Tick % 8 != 0) continue; // the game follows the car every frame: 30 Hz is plenty here
            arrived = gps.Update(car.pos.x, car.pos.z, car.fwd.x, car.fwd.z, 8 * Loop.SIM_DT);
            if (arrived) break;
            wrongWay |= gps.WrongWay;
            worstBack = Math.Max(worstBack, lastProgress - gps.Progress);
            lastProgress = gps.Progress;
            // Each turn is called out before the car gets to it, counting down.
            if (gps.NextTurn(out var turn, out double dist))
            {
                if (turn.Node != lastNode)
                {
                    if (lastNode >= 0) callAt.Add(callDistance);
                    lastNode = turn.Node;
                    called++;
                    callDistance = dist;
                }
                else callDistance = Math.Min(callDistance, dist);
            }
        }
        if (lastNode >= 0) callAt.Add(callDistance);
        Console.WriteLine($"  the bot drove the GPS route to {far.Name} ({Gps.Distance(route.Length)}) in {t0 / 60:0}:{t0 % 60:00.0}: arrived {arrived}, {gps.Replans} new routes, {called} of {turnsToCall} turns called, the car went back along the route at most {worstBack:0.0} m");
        Check(arrived && !gps.Active, "the GPS says it has arrived when the car gets there, and clears");
        Check(gps.Replans == 0 && version + 1 == gps.Version, "a car following the route is never sent another way");
        Check(called == turnsToCall && callAt.All(d => d < 40), "every turn is called out in order and counted down to the junction");
        Check(!wrongWay && worstBack < 10, "a car driving the route is never told to turn around");

        // 3. Straying: the car leaves the route for another road; the GPS plans again from there within two seconds.
        var sim2 = new Simulation(w, SpawnAt.Island) { WorldTime = false };
        var v2 = sim2.Player.vehicle;
        gps.Set(v2.pos.x, v2.pos.z, far.X, far.Z, far.Name);
        var first = gps.Route;
        // A road sample 400 m or more from every point of the route.
        double bx = 0, bz = 0, byaw = 0;
        bool found = false;
        foreach (var e in w.Edges)
        {
            for (int i = 0; i < e.n && !found; i += 5)
            {
                first.ProjectAny(e.x[i], e.z[i], out _, out _, out double away);
                if (away < 400 || Dist(e.x[i], e.z[i], v2.pos.x, v2.pos.z) > 2500 || e.bridge[i] != 0) continue;
                bx = e.x[i] - e.tz[i] * e.info.halfWidth * 0.5;
                bz = e.z[i] + e.tx[i] * e.info.halfWidth * 0.5;
                byaw = Simulation.YawOf(e.tx[i], e.tz[i]);
                found = true;
            }
            if (found) break;
        }
        sim2.Teleport(bx, bz, byaw);
        double waited = 0;
        while (waited < 2.5 && gps.Replans == 0)
        {
            gps.Update(v2.pos.x, v2.pos.z, v2.fwd.x, v2.fwd.z, 1.0 / 30);
            waited += 1.0 / 30;
        }
        r_At(gps.Route, 0, out double nx, out double nz);
        Console.WriteLine($"  the car moved to another road 400+ m from the route: a new route after {waited:0.0} s, starting {Dist(nx, nz, v2.pos.x, v2.pos.z):0} m from the car, {Gps.Distance(gps.Route.Length)} to go");
        Check(found && gps.Replans == 1 && waited <= 2 && Dist(nx, nz, v2.pos.x, v2.pos.z) < 30, "a car that leaves the route gets a new one from where it is within two seconds");

        // 4. The wrong way: on the route but facing back along it.
        gps.Set(v2.pos.x, v2.pos.z, far.X, far.Z, far.Name);
        gps.Route.At(200, out double wx, out _, out double wz, out double wtx, out double wtz);
        bool early = false;
        for (double t = 0; t < 2.5; t += 1.0 / 30)
        {
            gps.Update(wx, wz, -wtx, -wtz, 1.0 / 30);
            if (t < 1.5) early |= gps.WrongWay;
        }
        bool told = gps.WrongWay;
        gps.Update(wx, wz, wtx, wtz, 1.0 / 30);
        Check(!early && told && !gps.WrongWay, "facing back along the route for two seconds says turn around; facing along it again clears it");
    }

    /// <summary>Where the stunts could go: long straights, fast sweepers, twisty stretches, crests (exploration).</summary>
    static void StuntSurvey()
    {
        var w = World();
        Console.WriteLine($"{w.Edges.Length} edges, {w.Edges.Sum(e => e.length) / 1000:0.0} km");
        foreach (var e in w.Edges)
        {
            // Horizontal curvature from the tangents 6 samples apart; vertical curvature from heights over +-12 m.
            int n = e.n;
            var k = new double[n];
            var c = new double[n];
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - 3), b = Math.Min(n - 1, i + 3);
                double da = Math.Atan2(e.tz[b], e.tx[b]) - Math.Atan2(e.tz[a], e.tx[a]);
                while (da > Math.PI) da -= 2 * Math.PI;
                while (da < -Math.PI) da += 2 * Math.PI;
                double ds = e.s[b] - e.s[a];
                k[i] = ds > 0 ? Math.Abs(da) / ds : 0;
                int a2 = Math.Max(0, i - 3), b2 = Math.Min(n - 1, i + 3);
                double h0 = e.y[a2], h1 = e.y[i], h2 = e.y[b2];
                double s0 = e.s[i] - e.s[a2], s1 = e.s[b2] - e.s[i];
                c[i] = (s0 > 0 && s1 > 0) ? -2 * ((h2 - h1) / s1 - (h1 - h0) / s0) / (s0 + s1) : 0; // + = crest
            }
            // Longest straight (radius > 600 m).
            double best = 0, bestAt = 0, run = 0;
            for (int i = 1; i < n; i++)
            {
                if (k[i] < 1.0 / 600 && e.bridge[i] == 0) run += e.s[i] - e.s[i - 1]; else run = 0;
                if (run > best) { best = run; bestAt = e.s[i]; }
            }
            // Twisty: 400 m windows with the most turning in bends of radius 25-120 m.
            double twist = 0, twistAt = 0;
            for (int i = 0; i < n; i++)
            {
                double sum = 0;
                int j = i;
                for (; j < n && e.s[j] - e.s[i] < 400; j++) if (k[j] > 1.0 / 120 && k[j] < 1.0 / 25) sum += k[j] * (j + 1 < n ? e.s[j + 1] - e.s[j] : 0);
                if (j < n && sum > twist) { twist = sum; twistAt = e.s[i]; }
            }
            // Crests: the sharpest convex vertical curvature with a straight run-up of 150 m.
            double crest = 0, crestAt = 0;
            for (int i = 0; i < n; i++)
            {
                if (c[i] <= crest || e.bridge[i] != 0) continue;
                bool straight = true;
                for (int j = i; j >= 0 && e.s[i] - e.s[j] < 150; j--) if (k[j] > 1.0 / 400) straight = false;
                if (!straight) continue;
                crest = c[i];
                crestAt = e.s[i];
            }
            Console.WriteLine($"  edge {e.id,3} {e.info.id,-8} {w.Nodes[e.a].id,-14}->{w.Nodes[e.b].id,-14} {e.length,6:0} m | straight {best,5:0} m ending {bestAt,5:0} | twist {Deg(twist),4:0} deg in 400 m at {twistAt,5:0} | crest r {(crest > 0 ? 1 / crest : 0),6:0} m (takeoff {(crest > 0 ? Math.Sqrt(9.81 / crest) * 3.6 : 0),4:0} km/h) at {crestAt,5:0}");
        }
    }

    static void CoastDebug()
    {
        var w = World();
        var g = new IslandGround(w);
        int n = w.N, step = Math.Max(1, (int)Math.Round(16 / w.Spacing));
        int reach = Math.Max(1, (int)Math.Round(80 / w.Spacing));
        int dry = 0, height = 0, region = 0, sea = 0, road = 0;
        double minH = 1e9;
        var hit = new RoadHit();
        Console.WriteLine($"grid {n} spacing {w.Spacing} origin {w.Origin} reach {reach}");
        for (int j = reach; j < n - reach; j += step)
            for (int i = reach; i < n - reach; i += step)
            {
                int k = j * n + i;
                minH = Math.Min(minH, w.Height[k]);
                bool isDry = w.Water[k] == short.MinValue || w.Height[k] > w.Water[k] / 100.0 + 0.5;
                if (!isDry) continue;
                dry++;
                double h = w.Height[k];
                if (h < 4 || h > 45) continue;
                height++;
                if (w.Region[k] == 5 || w.Region[k] == 6) continue;
                region++;
                bool s2 = false;
                for (int a2 = 0; a2 < 8 && !s2; a2++)
                {
                    double ang = a2 * Math.PI / 4;
                    int ii = i + (int)Math.Round(Math.Cos(ang) * reach), jj = j + (int)Math.Round(Math.Sin(ang) * reach);
                    int kk = jj * n + ii;
                    if (w.Height[kk] < -0.5 && (w.Water[kk] == short.MinValue || w.Water[kk] < 100)) s2 = true;
                }
                if (!s2) continue;
                sea++;
                double x = w.Origin + i * w.Spacing, z = w.Origin + j * w.Spacing;
                double rd = g.NearestRoad(x, z, 600, hit) ? hit.dist : 600;
                if (rd < 30 || rd >= 600) continue;
                road++;
            }
        Console.WriteLine($"min height {minH:0.0}; dry {dry}, height ok {height}, region ok {region}, sea near {sea}, road ok {road}");
        // What is the water grid like at sea?
        int k0 = 5 * n + 5;
        Console.WriteLine($"corner cell: height {w.Height[k0]:0.0} water {w.Water[k0]}");
    }

    static void r_At(RaceRoute r, double s, out double x, out double z) => r.At(s, out x, out _, out z, out _, out _);

    // --- The Highcrest pass ----------------------------------------------------------------------

    static void Highcrest()
    {
        Console.WriteLine("\nThe Highcrest hill climb: the rebuilt pass (hairpins of 19 m and wider) raced by the AI");
        var sim0 = new Simulation(World(), SpawnAt.Island);
        var ev = EventCatalog.Festival(World(), sim0.Ground).FirstOrDefault(e => e.Id == "sprint-highcrest");
        Check(ev != null, "the festival has a Highcrest hill climb");
        if (ev == null) return;
        // The tightest bend of the race route.
        var r = ev.Route;
        double tight = 1e9;
        for (int i = 3; i < r.N - 3; i++)
        {
            double a0 = Math.Atan2(r.Tz[i - 3], r.Tx[i - 3]), a1 = Math.Atan2(r.Tz[i + 3], r.Tx[i + 3]);
            double d = a1 - a0;
            while (d > Math.PI) d -= 2 * Math.PI;
            while (d < -Math.PI) d += 2 * Math.PI;
            double ds = r.S[i + 3] - r.S[i - 3];
            if (Math.Abs(d) > 1e-6) tight = Math.Min(tight, ds / Math.Abs(d));
        }
        Console.WriteLine($"  {ev.Name}: {ev.Distance / 1000:0.0} km, tightest bend on the route {tight:0.0} m");
        double RadiusAt(double s)
        {
            int i = Math.Max(3, Math.Min(r.N - 4, (int)(s / (r.Length / r.N))));
            double d = Math.Atan2(r.Tz[i + 3], r.Tx[i + 3]) - Math.Atan2(r.Tz[i - 3], r.Tx[i - 3]);
            while (d > Math.PI) d -= 2 * Math.PI;
            while (d < -Math.PI) d += 2 * Math.PI;
            return (r.S[i + 3] - r.S[i - 3]) / Math.Max(1e-6, Math.Abs(d));
        }
        if (Environment.GetEnvironmentVariable("HC_PROFILE") != null)
        {
            var parts = Environment.GetEnvironmentVariable("HC_PROFILE").Split(',');
            double s0 = double.Parse(parts[0]), s1 = double.Parse(parts[1]);
            var gh = new GroundHit();
            for (double s = s0; s <= s1; s += 6)
            {
                r.At(s, out double x, out double y, out double z, out double tx, out double tz);
                r.At(s + 6, out _, out double y2, out _, out _, out _);
                // Cross-slope: ground height 3 m either side of the centre line.
                sim0.Ground.Sample(x - tz * 3, z + tx * 3, y + 5, gh);
                double yr = gh.y;
                sim0.Ground.Sample(x + tz * 3, z - tx * 3, y + 5, gh);
                double yl = gh.y;
                sim0.Ground.Sample(x, z, y + 5, gh);
                Console.WriteLine($"    s {s,6:0} ({x,6:0},{z,6:0}) y {y,6:0.0} ground {gh.y,6:0.0} grade {(y2 - y) / 6 * 100,5:0.0}% cross {(yr - yl) / 6 * 100,5:0.0}% radius {RadiusAt(s),6:0} surf {gh.surface}");
            }
        }
        if (Environment.GetEnvironmentVariable("HC_BENDS") == "1")
            for (double s = 0; s < r.Length; s += 8)
                if (RadiusAt(s) < 15) { r.At(s, out double bx, out _, out double bz, out _, out _); Console.WriteLine($"    tight {RadiusAt(s):0.0} m at s {s:0} ({bx:0}, {bz:0})"); }
        if (Environment.GetEnvironmentVariable("HC_TRACE") != null)
        {
            var parts = Environment.GetEnvironmentVariable("HC_TRACE").Split(',');
            string car = parts[0];
            double t0s = double.Parse(parts[1]), t1s = double.Parse(parts[2]);
            var simT = new Simulation(World(), SpawnAt.Island) { WorldTime = false };
            var raceT = new RaceSession(simT, ev, new List<(string, Personality)> { (car, new Personality { Name = "T", Skill = 0.9, Aggression = 0.5, Consistency = 0.9, Risk = 0.5 }) }, new RaceSession.AbilityCache(), 1, includePlayer: false);
            var en = raceT.Entrants[0];
            en.Ai.OnEvent = m => Console.WriteLine($"    EVENT t {raceT.Clock:0.0}: {m}");
            var cc = Controls.Neutral();
            int n = 0;
            while (!raceT.AllFinished && raceT.Clock < 600)
            {
                raceT.Step(cc);
                if (++n % 30 != 0 || en.S < t0s || en.S > t1s) continue;
                var v = en.Car.vehicle;
                Console.WriteLine($"    t {raceT.Clock,6:0.00} s {en.S,6:0.0} lat {en.Lateral,5:0.0} v {v.vLong,5:0.0} vT {en.Ai.TargetSpeed,5:0.0} th {v.input.throttle:0.00} br {v.input.brake:0.00} st {v.input.steer,5:0.00} gear {v.gear} up ({v.up.x:0.00},{v.up.y:0.00},{v.up.z:0.00}) y {v.pos.y:0.0} wheels {string.Join("", v.wheels.Select(q => q.grounded ? q.surface.ToString() : "-"))} radius {RadiusAt(en.S):0}");
            }
            return;
        }
        string[][] fields =
        {
            new[] { "halden-aster-gt", "aurelle-vif-r", "nordvik-kyst-sport", "hoshida-kiri-rs", "halden-280-sprint", "aurelle-pico" },
            new[] { "arcline-vanta", "orsini-saetta", "stallworth-prairie", "ironbark-ridgeback", "stallworth-ironside-454", "nordvik-varg" },
        };
        var abilities = new RaceSession.AbilityCache();
        var results = new (bool ok, string line)[fields.Length * 2];
        Parallel.For(0, results.Length, k =>
        {
            var cars = fields[k % fields.Length];
            uint seed = (uint)(k / fields.Length + 1);
            var sim = new Simulation(World(), SpawnAt.Island) { WorldTime = false };
            var field = new List<(string, Personality)>();
            for (int i = 0; i < cars.Length; i++) field.Add((cars[i], new Personality { Name = $"D{i}", Skill = 0.75 + 0.05 * i, Aggression = 0.5, Consistency = 0.9, Risk = 0.5 }));
            var race = new RaceSession(sim, ev, field, abilities, seed, includePlayer: false);
            if (Environment.GetEnvironmentVariable("HC_RESETS") == "1")
                foreach (var e in race.Entrants)
                {
                    var en = e;
                    e.Ai.OnEvent = m => { if (m.Contains("reset")) Console.WriteLine($"    {en.Car.specId} at s {en.S:0} (radius {RadiusAt(en.S):0} m, lateral {en.Lateral:0.0}): {m}"); };
                }
            var c = Controls.Neutral();
            while (!race.AllFinished && race.Clock < 900) race.Step(c);
            int resets = race.Entrants.Sum(e => e.Ai.Resets);
            double stuck = race.Entrants.Max(e => e.Ai.MaxStuck);
            var times = string.Join(" ", race.Order.Select(e => $"{e.Car.specId.Split('-').Last()} {Fmt(e.FinishTime)}{(e.Ai.Resets > 0 ? $" ({e.Ai.Resets}R)" : "")}"));
            results[k] = (race.AllFinished && stuck < 5 && resets <= 1, $"  field {k % fields.Length} seed {seed}: {resets} resets, {race.Contacts} contacts, {race.WallHits} wall hits, max stuck {stuck:0.0} s | {times}");
        });
        foreach (var x in results) Console.WriteLine(x.line);
        Check(tight >= 15, "the race route has no bend tighter than 15 m");
        Check(results.All(x => x.ok), "every car, from the Pico to the Vanta and the pickup, climbs the pass: all finish, at most one reset a race, nobody stuck");
    }

    // --- Traffic -------------------------------------------------------------------------------

    sealed class TrafficField : AiDriver.IField
    {
        readonly List<AiDriver.AiDriverView> views = new List<AiDriver.AiDriverView>();
        public int Count => views.Count;
        public AiDriver.AiDriverView Get(int i) => views[i];

        /// <summary>The traffic near the bot as race cars on its route: oncoming cars move backwards.</summary>
        public void Refresh(Simulation sim, RaceRoute route, double botS)
        {
            views.Clear();
            var me = sim.Player.vehicle;
            foreach (var c in sim.Traffic.Cars)
            {
                if (!c.Physics) continue;
                var v = c.Vehicle;
                double dx = v.pos.x - me.pos.x, dz = v.pos.z - me.pos.z;
                if (dx * dx + dz * dz > 120 * 120) continue;
                route.Project(v.pos.x, v.pos.z, botS, 140, 140, out double s, out double lat, out double dist);
                if (Math.Abs(lat) > 14) continue;
                route.At(s, out _, out _, out _, out double tx, out double tz);
                views.Add(new AiDriver.AiDriverView { id = c.Car.id, s = s, lateral = lat, speed = v.vel.x * tx + v.vel.z * tz, halfWidth = v.Params.spec.width / 2, length = v.Params.spec.length });
            }
        }
    }

    static void TrafficTests()
    {
        Console.WriteLine("\nTraffic: parked at the festival for ten minutes, then a bot driving a sprint through it");
        // 1. Parked: population, levels of detail, lanes, determinism, cost.
        double Hash(Simulation s)
        {
            double h = 0;
            foreach (var c in s.Traffic.Cars) h = h * 1.000001 + c.Vehicle.pos.x * 3 + c.Vehicle.pos.z * 7 + c.Id;
            return h;
        }
        Simulation Parked(out double hash, out List<double> laneErr, out int nearMin, out int physicsMax, out int ttImpacts, out double ms, out bool finite)
        {
            var s = new Simulation(World(), SpawnAt.Island) { WorldTime = false };
            var t = s.EnableTraffic(99);
            ParkOffRoad(s);
            var ctl = Controls.Neutral();
            ctl.handbrake = 1;
            laneErr = new List<double>();
            nearMin = int.MaxValue;
            physicsMax = 0;
            ttImpacts = 0;
            finite = true;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 240 * 600; i++)
            {
                s.Step(ctl);
                foreach (var e in s.Events)
                    if (e.kind == SimEventKind.Impact && e.carId != s.Player.id) ttImpacts++;
                s.Events.Clear();
                if (i % 240 == 0 && i > 240 * 60)
                {
                    int near = 0, phys = 0;
                    foreach (var c in t.Cars)
                    {
                        var v = c.Vehicle;
                        if (!(M.IsFinite(v.pos.x) && M.IsFinite(v.pos.y) && M.IsFinite(v.pos.z))) finite = false;
                        double dx = v.pos.x - s.Player.vehicle.pos.x, dz = v.pos.z - s.Player.vehicle.pos.z;
                        if (dx * dx + dz * dz < t.SpawnMax * t.SpawnMax) near++;
                        if (c.Physics)
                        {
                            phys++;
                            if (c.Trouble == 0) laneErr.Add(t.LaneError(c));
                            double gy = s.Ground.GroundHeight(v.pos.x, v.pos.z);
                            if (v.pos.y < gy - 0.5) finite = false;
                        }
                    }
                    nearMin = Math.Min(nearMin, near);
                    physicsMax = Math.Max(physicsMax, phys);
                }
            }
            ms = sw.Elapsed.TotalMilliseconds;
            hash = Hash(s);
            return s;
        }
        var a = Parked(out double ha, out var lane, out int nearMin, out int physMax, out int tt, out double msA, out bool finite);
        var t0 = a.Traffic;
        lane.Sort();
        double p50 = lane.Count > 0 ? lane[lane.Count / 2] : double.NaN, p95 = lane.Count > 0 ? lane[(int)(lane.Count * 0.95)] : double.NaN;
        Console.WriteLine($"  10 min parked: {t0.Spawned} spawned, {t0.Removed} removed, {t0.Promoted} became physics cars, {t0.Demoted} went back, {t0.Rescued} put back on their lane; at least {nearMin} cars within {t0.SpawnMax:0} m, up to {physMax} physics cars at once");
        Console.WriteLine($"  lane keeping: median {p50:0.00} m, 95th percentile {p95:0.00} m off the lane centre ({lane.Count} samples); {tt} impacts between traffic cars; {t0.LaneChanges} lane changes; {msA / 600:0.0} ms per simulated second");
        Check(finite, "no NaN, no traffic car under the ground");
        Check(nearMin >= t0.Target * 0.75, $"the island stays populated: at least {nearMin} of {t0.Target} cars around the player after the first minute");
        Check(t0.Promoted >= 5 && t0.Demoted >= 3, "cars pass the player as physics cars and go back to ghosts beyond");
        Check(p95 < 1.2, "physics traffic keeps to its lane (95% within 1.2 m of the centre)");
        Check(tt <= 6, $"traffic cars rarely touch each other ({tt} impacts in ten minutes)");
        Check(t0.LaneChanges >= 5, $"on the dual carriageways traffic pulls out to pass and moves back ({t0.LaneChanges} lane changes)");
        var b = Parked(out double hb, out _, out _, out _, out _, out _, out _);
        Check(ha == hb, "the same seed gives the same traffic, bit for bit");

        // 2. A bot drives the Harbour Dash route through the traffic.
        var sim = new Simulation(World(), SpawnAt.Island) { WorldTime = false };
        var traffic = sim.EnableTraffic(7);
        var ev = EventCatalog.All(World(), sim.Ground).First(e => e.Id == "sprint-harbor");
        // A road driver: the right-hand lane at a brisk pace, passing when it can.
        var line = RacingLine.KeepRight(ev.Route);
        var ability = new RaceSession.AbilityCache().Get(sim.Player.specId);
        var bot = new AiDriver(sim.Player, new Personality { Name = "Bot", Skill = 0.55, Aggression = 0.3, Consistency = 1, Risk = 0.3 }, ability, 3);
        ev.Route.At(ev.StartS, out double sx, out _, out double sz, out double stx, out double stz);
        sim.Player.vehicle.Place(sx + -stz * 2.2, sim.Ground.GroundHeight(sx, sz), sz + stx * 2.2, Simulation.YawOf(stx, stz));
        bot.SetRoute(ev.Route, line);
        // Let the traffic fill in before setting off.
        var hold = Controls.Neutral();
        hold.handbrake = 1;
        for (int i = 0; i < 240 * 30; i++) sim.Step(hold);
        var field = new TrafficField();
        int contacts = 0;
        long lastContact = -1000;
        double t = 0, progress0 = bot.S;
        bool finished = false;
        int passedPhysics = 0;
        var seen = new HashSet<int>();
        while (t < 600)
        {
            if (sim.Tick % 4 == 0) field.Refresh(sim, ev.Route, bot.S);
            bot.Update(Loop.SIM_DT, field);
            var v = sim.Player.vehicle;
            var c = new Controls { throttle = v.input.throttle, brake = v.input.brake, steer = v.input.steer, handbrake = v.input.handbrake, analogSteer = true, source = InputSource.Script };
            sim.Step(c);
            foreach (var e in sim.Events)
                if (e.kind == SimEventKind.Impact && e.carId == sim.Player.id && sim.Tick - lastContact > 120)
                {
                    contacts++;
                    lastContact = sim.Tick;
                    if (Environment.GetEnvironmentVariable("TR_CONTACTS") == "1")
                    {
                        var me = sim.Player.vehicle;
                        TrafficCar other = null;
                        double bd = 1e9;
                        foreach (var tc in traffic.Cars)
                        {
                            double dx = tc.Vehicle.pos.x - me.pos.x, dz = tc.Vehicle.pos.z - me.pos.z, d = dx * dx + dz * dz;
                            if (d < bd) { bd = d; other = tc; }
                        }
                        if (other != null)
                        {
                            var ov = other.Vehicle;
                            double dx = ov.pos.x - me.pos.x, dz = ov.pos.z - me.pos.z;
                            double lon = dx * me.fwd.x + dz * me.fwd.z, lat = dx * me.right.x + dz * me.right.z;
                            double rel = me.fwd.x * ov.fwd.x + me.fwd.z * ov.fwd.z;
                            Console.WriteLine($"    contact t {t:0.0} s {bot.S:0} botLat {bot.Lateral:0.0} me v {me.vLong:0.0} | traffic {other.SpecId} v {ov.vLong:0.0} at lon {lon:0.0} lat {lat:0.0}, heading dot {rel:0.00}, lane err {traffic.LaneError(other):0.0}, swerve {other.SwerveT:0.0}, trouble {other.Trouble:0.0}; impulse {e.value:0}");
                        }
                    }
                }
            sim.Events.Clear();
            foreach (var tc in traffic.Cars) if (tc.Physics && seen.Add(tc.Id)) passedPhysics++;
            t += Loop.SIM_DT;
            if (bot.S > ev.Route.Length - 200) { finished = true; break; }
        }
        Console.WriteLine($"  the bot drove {ev.Route.Length / 1000:0.0} km in {Fmt(t)} past {passedPhysics} physics traffic cars: {contacts} contacts, {bot.Resets} resets; traffic: {traffic.Spawned} spawned, {traffic.Promoted} promoted, {traffic.Rescued} put back");
        Check(finished && passedPhysics >= 8, "the bot gets through the traffic to the harbour, passing real (physics) cars on the way");
        // The bot is the racing AI on a keep-right line: it overtakes like a racer (into oncoming
        // cars now and then) and never gives way at junctions, so some contacts are its own.
        Check(contacts <= 6, $"contacts stay rare for a racer in two-way traffic ({contacts} in {ev.Route.Length / 1000:0.0} km past {passedPhysics} cars)");

        // 3. The player stopped in a lane on the festival highway: traffic stops behind and goes round, never into them.
        var sp3 = new Simulation(World(), SpawnAt.Island) { WorldTime = false };
        var tr3 = sp3.EnableTraffic(11);
        if (Environment.GetEnvironmentVariable("TR_LOG") == "1") tr3.Log = m => Console.WriteLine($"    [{sp3.Time:0.0}] {m} | player ({sp3.Player.vehicle.pos.x:0.0},{sp3.Player.vehicle.pos.z:0.0})");
        var parked = Controls.Neutral();
        parked.handbrake = 1;
        int hits = 0, passed = 0;
        var behind = new Dictionary<int, double>();
        var me3 = sp3.Player.vehicle;
        for (int i = 0; i < 240 * 480; i++)
        {
            sp3.Step(parked);
            foreach (var e in sp3.Events)
                if (e.kind == SimEventKind.Impact && Math.Abs(e.x - me3.pos.x) < 4 && Math.Abs(e.z - me3.pos.z) < 4)
                {
                    hits++;
                    if (Environment.GetEnvironmentVariable("TR_CONTACTS") == "1")
                        foreach (var c in tr3.Cars)
                        {
                            if (!c.Physics) continue;
                            var v = c.Vehicle;
                            double dx = v.pos.x - me3.pos.x, dz = v.pos.z - me3.pos.z;
                            if (dx * dx + dz * dz > 49) continue;
                            double lon = dx * me3.fwd.x + dz * me3.fwd.z, lat = dx * me3.right.x + dz * me3.right.z;
                            Console.WriteLine($"    hit t {i / 240.0:0.0}: {c.SpecId} v {v.vLong:0.0} gear {v.gear} at lon {lon:0.0} lat {lat:0.0} (player frame), heading dot {me3.fwd.x * v.fwd.x + me3.fwd.z * v.fwd.z:0.00}, lane {c.Lane} err {tr3.LaneError(c):0.0}, blocked {c.Blocked:0.0} swerve {c.SwerveT:0.0} yielding {c.Yielding:0.0} input t{v.input.throttle:0.0} b{v.input.brake:0.0} h{v.input.handbrake:0.0}");
                        }
                }
            sp3.Events.Clear();
            if (i % 24 != 0) continue;
            foreach (var c in tr3.Cars)
            {
                if (!c.Physics) continue;
                var v = c.Vehicle;
                double dx = me3.pos.x - v.pos.x, dz = me3.pos.z - v.pos.z;
                double lon = dx * v.fwd.x + dz * v.fwd.z;
                // 1: has had the player ahead of it (close by); 2: has since gone past and is counted.
                bool close = Math.Abs(dx * -v.fwd.z + dz * v.fwd.x) < 12;
                behind.TryGetValue(c.Id, out double state);
                if (state == 0 && close && lon > 0 && lon < 40) behind[c.Id] = 1;
                else if (state == 1 && close && lon < -6)
                {
                    behind[c.Id] = 2;
                    passed++;
                }
            }
        }
        Console.WriteLine($"  the player stopped in a lane for 8 minutes: {passed} cars went past them, {hits} impacts on the player, {tr3.Rescued} cars put back");
        Check(hits == 0 && passed >= 5, "traffic stops for a player stopped in its lane and goes round, never into them");
    }

    /// <summary>The player's car parked beside the festival road, clear of the lanes.</summary>
    static void ParkOffRoad(Simulation s)
    {
        var sp = s.World.Spawn;
        double fx = -Math.Sin(sp.yaw), fz = -Math.Cos(sp.yaw); // yaw 0 faces -z
        double x = sp.x - fz * 16, z = sp.z + fx * 16;       // 16 m to the right
        s.Teleport(x, z, sp.yaw);
    }

    static void TrafficDebug()
    {
        var s = new Simulation(World(), SpawnAt.Island) { WorldTime = false };
        var t = s.EnableTraffic(99);
        ParkOffRoad(s);
        var ctl = Controls.Neutral();
        ctl.handbrake = 1;
        int world = 0, cars = 0;
        for (int i = 0; i < 240 * 240; i++)
        {
            s.Step(ctl);
            foreach (var e in s.Events)
            {
                if (e.kind != SimEventKind.Impact) continue;
                bool nearCar = false;
                int n = 0;
                foreach (var c in s.Cars)
                {
                    double dx = c.vehicle.pos.x - e.x, dz = c.vehicle.pos.z - e.z;
                    if (dx * dx + dz * dz < 9) n++;
                }
                nearCar = n >= 2;
                if (nearCar) cars++; else world++;
                if (i > 240 * 60 && (cars + world) % 10 == 1) Console.WriteLine($"  impact t {i / 240.0:0.0} at ({e.x:0},{e.z:0}) {(nearCar ? "car-car" : "world")} impulse {e.value:0}");
            }
            s.Events.Clear();
            if (Environment.GetEnvironmentVariable("TR_ONE") == "1" && i > 240 * 60 && i % 60 == 0)
            {
                foreach (var c in t.Cars)
                {
                    if (!c.Physics) continue;
                    var v = c.Vehicle;
                    Console.WriteLine($"  t {i / 240.0:0.00} car {c.Id} S {c.S:0.0} v {v.vLong:0.0} laneErr {t.LaneError(c):0.00} steer {v.input.steer:0.000} yawRate {v.yawRate:0.000} fwd ({v.fwd.x:0.000},{v.fwd.z:0.000}) pos ({v.pos.x:0.0},{v.pos.z:0.0})");
                    break;
                }
            }
            if (i % (240 * 20) == 0 && i > 240 * 60)
            {
                foreach (var c in t.Cars)
                {
                    if (!c.Physics) continue;
                    var v = c.Vehicle;
                    var e = s.World.Edges[c.Edge];
                    Console.WriteLine($"  t {i / 240.0:0} car {c.Id} {c.SpecId,-22} edge {c.Edge} ({e.info.id}, {e.info.lanes} lanes, half {e.info.halfWidth}) dir {c.Dir} lane {c.Lane} S {c.S:0.0}/{e.length:0} v {v.vLong:0.0} laneErr {t.LaneError(c):0.00} steer {v.input.steer:0.00} thr {v.input.throttle:0.00} brk {v.input.brake:0.00} trouble {c.Trouble:0.0}");
                }
            }
        }
        Console.WriteLine($"impacts: {cars} car-car, {world} world");
    }

    static string Fmt(double t) => double.IsNaN(t) ? "  --  " : $"{(int)(t / 60)}:{t % 60:00.00}";
}
