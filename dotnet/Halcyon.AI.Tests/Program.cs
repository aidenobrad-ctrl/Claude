// The AI on the island, headless: racing lines, every car lapping alone,
// sprints, and tournaments of AI against AI.
//   cd dotnet/Halcyon.AI.Tests && dotnet run -c Release [-- quick|solo|tournament|...]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Halcyon.Core;

static class Program
{
    static int failures;
    static bool Verbose = Environment.GetEnvironmentVariable("AI_VERBOSE") == "1";
    static WorldData W;
    static readonly RaceSession.AbilityCache Abilities = new RaceSession.AbilityCache();

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

    static int Main(string[] args)
    {
        string only = args.Length > 0 ? args[0] : "";
        var sw = Stopwatch.StartNew();
        W = WorldData.LoadFromDirectory(Path.Combine(Root(), "unity/Assets/Halcyon/Data/Resources/HalcyonIsland"));
        var sim = new Simulation(W, SpawnAt.Island);
        Console.WriteLine($"island loaded in {sw.ElapsedMilliseconds} ms");
        var events = EventCatalog.All(W, sim.Ground);
        foreach (var e in events) Console.WriteLine($"  event {e.Id}: {e.Name}, route {e.Route.Length:0} m ({e.Route.N} samples), race {e.Distance:0} m");
        if (only == "" || only == "line") Lines(events);
        if (only == "debugline") DebugLine(events[1]);
        if (only == "colliders") CollidersNear(args.Length > 3 ? events.First(e => e.Id == args[3]) : events[0], double.Parse(args[1]), double.Parse(args[2]));
        if (only == "impact") ImpactTrace(args[1], double.Parse(args[2]), double.Parse(args[3]));
        if (only == "survey") Survey(sim);
        if (only == "routeinfo") RouteInfo(events.First(e => e.Id == args[1]), double.Parse(args[2]), double.Parse(args[3]));
        if (only == "trace2") TraceEvent(events.First(e => e.Id == args[1]), args[2], double.Parse(args[3]), double.Parse(args[4]));
        if (only == "trace") Trace(events[0], args.Length > 1 ? args[1] : "arcline-vanta", args.Length > 2 ? double.Parse(args[2]) : 900, args.Length > 3 ? double.Parse(args[3]) : 1300);
        if (only == "" || only == "ability") AbilityTable();
        if (only == "" || only == "solo") SoloLaps(events[0]);
        if (only == "" || only == "sprint") Sprints(events);
        if (only == "" || only == "tournament") Tournament(events);
        if (only == "" || only == "grids") Grids(events);
        if (only == "" || only == "field") MixedField(events);
        if (only == "skillsolo") SkillSolo(events);
        if (only == "duel") DuelTrace(events.First(e => e.Id == args[1]), double.Parse(args[2]), double.Parse(args[3]), uint.Parse(args[4]), args.Length > 5 ? double.Parse(args[5]) : 1.0);
        if (only == "compare") Compare(events.First(e => e.Id == args[1]), double.Parse(args[2]), double.Parse(args[3]), args.Length > 4 ? uint.Parse(args[4]) : 1);
        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "AI: PASS" : $"AI: {failures} FAILED");
        return failures == 0 ? 0 : 1;
    }

    static void Lines(List<RaceEvent> events)
    {
        Console.WriteLine("\nRacing lines");
        foreach (var e in events)
        {
            var sw = Stopwatch.StartNew();
            var line = RacingLine.Build(e.Route);
            double ms = sw.Elapsed.TotalMilliseconds;
            var centre = RacingLine.Centre(e.Route);
            double kLine = 0, kCentre = 0, maxK = 0, sumK2 = 0, sumC2 = 0;
            bool inside = true;
            for (int i = 0; i < e.Route.N; i++)
            {
                kLine = Math.Max(kLine, Math.Abs(line.Curv[i]));
                kCentre = Math.Max(kCentre, Math.Abs(centre.Curv[i]));
                sumK2 += line.Curv[i] * line.Curv[i];
                sumC2 += centre.Curv[i] * centre.Curv[i];
                if (line.Offset[i] < -e.Route.WLeft[i] + 0.9 || line.Offset[i] > e.Route.WRight[i] - 0.9) inside = false;
            }
            Console.WriteLine($"  {e.Id}: built in {ms:0} ms; max curvature {kLine:0.0000} vs centreline {kCentre:0.0000}; sum k^2 {sumK2:0.000} vs {sumC2:0.000}");
            Check(inside, $"{e.Id}: the line stays inside the track");
            Check(sumK2 < sumC2 * 0.85, $"{e.Id}: the line is smoother than the centreline");
        }
    }

    static void Survey(Simulation sim0)
    {
        var sp = W.Spawn;
        foreach (var p in W.Pois)
        {
            var ev = EventCatalog.Sprint(W, sim0.Ground, "x", p.name, sp.x, sp.z, p.x, p.z);
            if (ev == null) { Console.WriteLine($"  {p.name}: no route"); continue; }
            double maxK = 0;
            var cl = RacingLine.Centre(ev.Route);
            foreach (var k in cl.Curv) maxK = Math.Max(maxK, Math.Abs(k));
            var run = RunRace(ev, new List<(string, Personality)> { ("halden-aster-gt", Ace()) }, 3, 900);
            var e = run.Session.Entrants[0];
            Console.WriteLine($"  {p.name,-16} {ev.Distance,6:0} m  min radius {1 / maxK,5:0.0} m  time {Fmt(e.FinishTime)}  resets {e.Ai.Resets}  max stuck {e.Ai.MaxStuck:0.0}");
        }
    }

    static void RouteInfo(RaceEvent ev, double s0, double s1)
    {
        var r = ev.Route;
        var line = RacingLine.Build(r);
        var prof = SpeedProfile.Build(line, Abilities.Get("halden-aster-gt"), 0.92, 0.92);
        var sim = new Simulation(W, SpawnAt.Island);
        for (double s = s0; s <= s1; s += 6)
        {
            r.At(s, out double x, out double y, out double z, out _, out _);
            double y1 = r.Sample(r.Y, s + 6), y0 = r.Sample(r.Y, s - 6);
            double grade = (y1 - y0) / 12;
            double kv = (y1 - 2 * y + y0) / 36;
            double gy = sim.Ground.GroundHeight(x, z);
            int rails = 0;
            sim.Colliders.Query(x, z, 8, seg => rails++, c => { });
            Console.WriteLine($"  s {s,6:0} ({x,7:0.0},{z,7:0.0}) y {y,6:0.0} ground {gy,6:0.0} grade {grade * 100,5:0.0}% kv {kv * 1000,6:0.0}e-3 k {line.CurvAt(s),7:0.000} w {r.Sample(r.WLeft, s),4:0.0} line {line.OffsetAt(s),5:0.0} v {prof.At(s) * 3.6,4:0} km/h  colliders {rails}");
        }
    }

    static void TraceEvent(RaceEvent ev, string car, double s0, double s1)
    {
        var sim = new Simulation(W, SpawnAt.Island);
        var who = Ace();
        if (Environment.GetEnvironmentVariable("AI_SKILL") is string sk) who.Skill = double.Parse(sk);
        if (Environment.GetEnvironmentVariable("AI_CONS") is string cs) who.Consistency = double.Parse(cs);
        uint seed = Environment.GetEnvironmentVariable("AI_SEED") is string sd ? uint.Parse(sd) : 1;
        if (ev.Kind == RaceKind.Circuit) ev = EventCatalog.ProvingGP(W, 2);
        var race = new RaceSession(sim, ev, new List<(string, Personality)> { (car, who) }, Abilities, seed, includePlayer: false);
        var e = race.Entrants[0];
        var ai = e.Ai;
        ai.OnEvent = m => Console.WriteLine("    " + m);
        var c = Controls.Neutral();
        int k = 0;
        while (!race.AllFinished && race.Clock < 600)
        {
            race.Step(c);
            k++;
            var v = e.Car.vehicle;
            bool lap = Environment.GetEnvironmentVariable("AI_LAP") is string lp ? e.LapsDone == int.Parse(lp) : true;
            if (lap && k % (s1 - s0 > 3000 ? 1200 : 12) == 0 && ai.S >= s0 && ai.S <= s1)
            {
                double beta = Math.Atan2(v.vLat, Math.Max(2, Math.Abs(v.vLong))) * 57.3;
                string w = string.Join("", v.wheels.Select(q => q.grounded ? q.surface.ToString().Substring(0, 1) : "-"));
                Console.WriteLine($"  t {race.Clock,6:0.00} s {ai.S,6:0.0} v {v.vLong,5:0.0} vT {ai.TargetSpeed,5:0.0} k {race.Line.CurvAt(ai.S),7:0.0000} lat {ai.Lateral,6:0.0} line {ai.Line.OffsetAt(ai.S),5:0.0} yaw {v.yawRate,5:0.00} st {v.input.steer,5:0.00} th {v.input.throttle:0.00} br {v.input.brake:0.00} beta {beta,5:0.0} y {v.pos.y:0.0} upY {v.up.y:0.00} wheels {w} imp {v.impact:0} aReq {ai.DebugA:0.0} aFF {ai.DebugFF:0.0}");
            }
            double lapProgress = ev.Kind == RaceKind.Circuit ? e.Progress - e.LapsDone * ev.Route.Length : e.Progress;
            if (lap && lapProgress > s1 + 50) break;
        }
        Console.WriteLine($"  trace ended at t {race.Clock:0.0}, laps {e.LapsDone}, progress {e.Progress:0}, finished {e.Finished}");
    }

    static void ImpactTrace(string car, double t0, double t1)
    {
        var ev = EventCatalog.ProvingGP(W, 1);
        var sim = new Simulation(W, SpawnAt.Island);
        var race = new RaceSession(sim, ev, new List<(string, Personality)> { (car, Ace()) }, Abilities, 1, includePlayer: false);
        var e = race.Entrants[0];
        var c = Controls.Neutral();
        while (race.Clock < t1)
        {
            race.Step(c);
            var v = e.Car.vehicle;
            foreach (var se in sim.Events) if (se.kind == SimEventKind.Impact) Console.WriteLine($"    IMPACT car {se.carId} at ({se.x:0.0},{se.z:0.0}) impulse {se.value:0}");
            if (race.Clock >= t0)
            {
                string w = string.Join(" ", v.wheels.Select(q => $"{(q.grounded ? "G" : "-")}{q.contact.y:0.00}/{q.load:0}/{q.surface}"));
                Console.WriteLine($"  t {race.Clock:0.000} pos ({v.pos.x:0.00},{v.pos.y:0.00},{v.pos.z:0.00}) v {v.vLong:0.0} yaw {v.yawRate:0.00} up {v.up.y:0.000} impact {v.impact:0} wheels {w}");
            }
        }
    }

    static void CollidersNear(RaceEvent ev, double s0, double s1)
    {
        var sim = new Simulation(W, SpawnAt.Island);
        var r = ev.Route;
        var line = RacingLine.Build(r);
        var seen = new HashSet<object>();
        for (double s = s0; s <= s1; s += 5)
        {
            r.At(s, out double x, out _, out double z, out double tx, out double tz);
            sim.Colliders.Query(x, z, 15, seg =>
            {
                if (!seen.Add(seg)) return;
                r.Project((seg.ax + seg.bx) / 2, (seg.az + seg.bz) / 2, s, 30, 30, out double ss, out double lat, out _);
                Console.WriteLine($"  segment {seg.kind} ({seg.ax:0.0},{seg.az:0.0})-({seg.bx:0.0},{seg.bz:0.0}) top {seg.top:0.0} bottom {seg.bottom:0.0} at s {ss:0} lateral {lat:0.0} (line {line.OffsetAt(ss):0.0})");
            }, c =>
            {
                if (!seen.Add(c)) return;
                r.Project(c.x, c.z, s, 30, 30, out double ss, out double lat, out _);
                Console.WriteLine($"  circle {c.kind} r {c.r:0.00} at ({c.x:0.0},{c.z:0.0}) top {c.top:0.0} bottom {c.bottom:0.0} at s {ss:0} lateral {lat:0.0} (line {line.OffsetAt(ss):0.0})");
            });
        }
    }

    static void DebugLine(RaceEvent e)
    {
        var line = RacingLine.Build(e.Route);
        var r = e.Route;
        var idx = Enumerable.Range(0, r.N).OrderByDescending(i => Math.Abs(line.Curv[i])).Take(12).OrderBy(i => i).ToList();
        foreach (int i in idx)
        {
            Console.WriteLine($"  i {i} s {r.S[i]:0} k {line.Curv[i]:0.000} off {line.Offset[i]:0.00} w {r.WLeft[i]:0.0}/{r.WRight[i]:0.0} ds {line.Ds[i]:0.00} pos ({r.X[i]:0},{r.Z[i]:0})");
            for (int d = -3; d <= 3; d++)
            {
                int j = Math.Max(0, Math.Min(r.N - 1, i + d));
                Console.WriteLine($"      {j}: c ({r.X[j]:0.0},{r.Z[j]:0.0}) t ({r.Tx[j]:0.00},{r.Tz[j]:0.00}) off {line.Offset[j]:0.00} p ({line.X[j]:0.0},{line.Z[j]:0.0})");
            }
        }
    }

    static void Trace(RaceEvent gp, string car, double s0, double s1)
    {
        var ev = EventCatalog.ProvingGP(W, 1);
        var sim = new Simulation(W, SpawnAt.Island);
        var race = new RaceSession(sim, ev, new List<(string, Personality)> { (car, Ace()) }, Abilities, 1, includePlayer: false);
        var e = race.Entrants[0];
        var ai = e.Ai;
        var c = Controls.Neutral();
        int k = 0;
        while (!race.AllFinished && race.Clock < 300)
        {
            race.Step(c);
            k++;
            var v = e.Car.vehicle;
            if (k % 24 == 0 && ai.S >= s0 && ai.S <= s1)
            {
                double beta = Math.Atan2(v.vLat, Math.Max(2, Math.Abs(v.vLong))) * 57.3;
                Console.WriteLine($"  t {race.Clock,6:0.00} s {ai.S,6:0.0} v {v.vLong,5:0.0} vT {ai.TargetSpeed,5:0.0} k {race.Line.CurvAt(ai.S),7:0.0000} lat {ai.Lateral,6:0.0} line {race.Line.OffsetAt(ai.S),5:0.0} lane {ai.Lane,5:0.0} st {v.input.steer,5:0.00} th {v.input.throttle:0.00} br {v.input.brake:0.00} beta {beta,5:0.0} yaw {v.yawRate:0.00} esc {v.escActive} g {v.gear}");
            }
        }
    }

    static void AbilityTable()
    {
        Console.WriteLine("\nCar abilities (measured on the test track)");
        foreach (var c in Cars.All)
        {
            var a = Abilities.Get(c.id);
            Console.WriteLine($"  {c.id,-26} grip {a.Mu:0.000} g  brake {a.BrakeG:0.000} g  aero {a.AeroK * 1e4:0.00}e-4/v2  top {a.TopSpeed * 3.6:0} km/h  accel@0 {a.AccelAt(0):0.0} @20 {a.AccelAt(20):0.0} @50 {a.AccelAt(50):0.0}  stable braking at 0.1/0.2/0.4/0.6 g lateral {a.StableBrake(0.1):0.00}/{a.StableBrake(0.2):0.00}/{a.StableBrake(0.4):0.00}/{a.StableBrake(0.6):0.00} g (rear brake {a.RearBrake:0.00}, rear weight {a.RearWeight:0.00})");
        }
    }

    sealed class Run
    {
        public RaceSession Session;
        public double Seconds;
        public double ms;
    }

    static Run RunRace(RaceEvent ev, List<(string carId, Personality p)> field, uint seed, double timeout, RacingLine line = null)
    {
        var sim = new Simulation(W, SpawnAt.Island);
        var sw = Stopwatch.StartNew();
        var race = new RaceSession(sim, ev, field, Abilities, seed, includePlayer: false, line: line);
        if (Environment.GetEnvironmentVariable("AI_ESC") == "0") foreach (var e in race.Entrants) e.Car.vehicle.aids.esc = false;
        if (Verbose) race.ContactAt = (a, b, imp) => Console.WriteLine($"      contact t {race.Clock:0.0} s {a.S:0}: {a.Name} lat {a.Lateral:0.0} v {a.Car.vehicle.vLong:0.0} pass {a.Ai.DebugPassSide} / {b.Name} lat {b.Lateral:0.0} v {b.Car.vehicle.vLong:0.0} pass {b.Ai.DebugPassSide}, impulse {imp:0}");
        if (Verbose) race.WallHitAt = (e, imp) => Console.WriteLine($"      {e.Name}: wall hit at s {e.S:0}, lateral {e.Lateral:0.0}, impulse {imp:0}, speed {e.Car.vehicle.vLong:0.0}, passing {e.Ai.DebugPassSide}");
        foreach (var e in race.Entrants) if (e.Ai != null) { var name = e.Name; var car = e.Car.specId; e.Ai.OnEvent = m => { if (Verbose) Console.WriteLine($"      {name} ({car}): {m}"); }; }
        var c = Controls.Neutral();
        int steps = 0;
        string traceSkill = Environment.GetEnvironmentVariable("AI_TRACE");
        double t0 = double.TryParse(Environment.GetEnvironmentVariable("AI_T0"), out double tt0) ? tt0 : 0;
        double t1 = double.TryParse(Environment.GetEnvironmentVariable("AI_T1"), out double tt1) ? tt1 : 1e9;
        var traced = traceSkill == null ? null : race.Entrants.FirstOrDefault(e => e.Ai.P.Skill.ToString("0.00") == traceSkill);
        while (!race.AllFinished && race.Clock < timeout)
        {
            race.Step(c);
            steps++;
            if (traced != null && steps % 30 == 0 && race.Clock >= t0 && race.Clock <= t1)
            {
                var v = traced.Car.vehicle;
                var near = race.Entrants.Where(e => e != traced).OrderBy(e => Math.Abs(ev.Route.Delta(traced.S, e.S))).First();
                Console.WriteLine($"    t {race.Clock,6:0.0} s {traced.S,6:0.0} lat {traced.Lateral,5:0.0} v {v.vLong,5:0.0} th {v.input.throttle:0.00} br {v.input.brake:0.00} st {v.input.steer,5:0.00} g {v.gear} upY {v.up.y:0.00} wheels {string.Join("", v.wheels.Select(q => q.grounded ? q.surface.ToString() : "-"))} pos ({v.pos.x:0.0},{v.pos.y:0.0},{v.pos.z:0.0}) | nearest {near.Name} ds {ev.Route.Delta(traced.S, near.S):0.0} lat {near.Lateral:0.0} v {near.Car.vehicle.vLong:0.0}");
            }
        }
        return new Run { Session = race, Seconds = race.Clock, ms = sw.Elapsed.TotalMilliseconds };
    }

    static Personality Ace(string name = "Ace") => new Personality { Name = name, Skill = 1, Aggression = 0.5, Consistency = 1, Risk = 0.5 };

    static void SoloLaps(RaceEvent gp)
    {
        Console.WriteLine("\nEvery car alone, 2 laps of the proving circuit (best AI driver)");
        var line = RacingLine.Build(gp.Route);
        var ev = EventCatalog.ProvingGP(W, 2);
        var times = new Dictionary<string, double>();
        foreach (var c in Cars.All)
        {
            var run = RunRace(ev, new List<(string, Personality)> { (c.id, Ace()) }, 1, 600, line);
            var e = run.Session.Entrants[0];
            var ai = e.Ai;
            times[c.id] = e.BestLap;
            Console.WriteLine($"  {c.id,-26} lap {Fmt(e.LastLap)}  best {Fmt(e.BestLap)}  finished {e.Finished}  resets {ai.Resets}  max stuck {ai.MaxStuck:0.0} s  reversals {ai.Reversals}  max off {ai.MaxOff:0.0} m  ({run.ms:0} ms)");
            Check(e.Finished && ai.Resets == 0 && ai.MaxStuck < 5, $"{c.id}: two clean laps");
        }
        // Faster cars lap faster.
        Check(times["arcline-vanta"] < times["halden-aster-gt"] && times["halden-aster-gt"] < times["aurelle-pico"], "lap times follow performance (hyper < sports < compact)");
    }

    static string Fmt(double t) => double.IsNaN(t) ? "  --  " : $"{(int)(t / 60)}:{t % 60:00.00}";

    static void Sprints(List<RaceEvent> events)
    {
        Console.WriteLine("\nSprints, one car each");
        foreach (var ev in events.Where(e => e.Kind == RaceKind.Sprint))
        {
            var run = RunRace(ev, new List<(string, Personality)> { ("halden-aster-gt", Ace()) }, 3, 900);
            var e = run.Session.Entrants[0];
            Console.WriteLine($"  {ev.Id}: {ev.Distance:0} m in {Fmt(e.FinishTime)}  (avg {ev.Distance / e.FinishTime * 3.6:0} km/h)  resets {e.Ai.Resets}  max stuck {e.Ai.MaxStuck:0.0} s  ({run.ms:0} ms)");
            Check(e.Finished && e.Ai.MaxStuck < 5, $"{ev.Id}: finishes without getting stuck");
        }
    }

    static readonly double[] Skills = { 0.30, 0.44, 0.58, 0.72, 0.86, 1.00 };

    static Personality Driver(double skill) => new Personality { Name = $"S{skill:0.00}", Skill = skill, Aggression = 0.5, Consistency = 0.9, Risk = 0.5 };

    /// <summary>
    /// The tournament from the spec: every AI against every other AI, head to
    /// head, on every route, over several seeds, in identical cars. The
    /// stronger driver starts from pole in half the runs and from behind in the
    /// other half, so passing is tested too. Drivers ranked by skill must finish
    /// in that order in at least 80% of runs, and no AI may be stuck for 5 s.
    /// </summary>
    static void Tournament(List<RaceEvent> events)
    {
        int seeds = int.TryParse(Environment.GetEnvironmentVariable("AI_SEEDS"), out int n) ? n : 4;
        Console.WriteLine($"\nTournament: every driver against every other, head to head, on every route, {seeds} seeds (identical cars)");
        var jobs = new List<(RaceEvent ev, RacingLine line, int a, int b, int seed)>();
        string onlyEvent = Environment.GetEnvironmentVariable("AI_EVENT");
        foreach (var ev0 in events)
        {
            if (onlyEvent != null && ev0.Id != onlyEvent) continue;
            var ev = ev0.Kind == RaceKind.Circuit ? EventCatalog.ProvingGP(W, 2) : ev0;
            var line = RacingLine.Build(ev.Route);
            for (int a = 0; a < Skills.Length; a++)
                for (int b = a + 1; b < Skills.Length; b++)
                    for (int seed = 1; seed <= seeds; seed++) jobs.Add((ev, line, a, b, seed));
        }
        Abilities.Get("halden-aster-gt");
        var res = new (bool ok, int contacts, int walls, double gap, double stuck, int resets)[jobs.Count];
        var sw = Stopwatch.StartNew();
        System.Threading.Tasks.Parallel.For(0, jobs.Count, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, k =>
        {
            var (ev, line, a, b, seed) = jobs[k];
            // The weaker driver (a) starts on pole in odd seeds, the stronger (b) in even ones.
            var field = seed % 2 == 1
                ? new List<(string, Personality)> { ("halden-aster-gt", Driver(Skills[a])), ("halden-aster-gt", Driver(Skills[b])) }
                : new List<(string, Personality)> { ("halden-aster-gt", Driver(Skills[b])), ("halden-aster-gt", Driver(Skills[a])) };
            var run = RunRace(ev, field, (uint)(seed * 101 + a * 7 + b), ev.Kind == RaceKind.Circuit ? 600 : 1200, line);
            var order = run.Session.Order;
            bool ok = order[0].Ai.P.Skill > order[1].Ai.P.Skill && order[0].Finished;
            double gap = order[1].Finished && order[0].Finished ? order[1].FinishTime - order[0].FinishTime : double.NaN;
            res[k] = (ok, run.Session.Contacts, run.Session.WallHits, gap, run.Session.Entrants.Max(e => e.Ai.MaxStuck), run.Session.Entrants.Sum(e => e.Ai.Resets));
        });
        Console.WriteLine($"  {jobs.Count} races in {sw.Elapsed.TotalSeconds:0} s");
        // By route and by pair.
        foreach (var g in jobs.Select((j, k) => (j, k)).GroupBy(x => x.j.ev.Id))
        {
            var r = g.Select(x => res[x.k]).ToList();
            Console.WriteLine($"  {g.Key,-20} stronger driver wins {r.Count(x => x.ok)}/{r.Count}  contacts {r.Sum(x => x.contacts)}  wall hits {r.Sum(x => x.walls)}  resets {r.Sum(x => x.resets)}  max stuck {r.Max(x => x.stuck):0.0} s");
        }
        Console.WriteLine("  by skill gap (stronger wins / runs):");
        foreach (var g in jobs.Select((j, k) => (j, k)).GroupBy(x => x.j.b - x.j.a).OrderBy(g => g.Key))
        {
            var r = g.Select(x => res[x.k]).ToList();
            var fromBehind = g.Where(x => x.j.seed % 2 == 1).Select(x => res[x.k]).ToList();
            Console.WriteLine($"    {g.Key * 0.14:0.00}: {r.Count(x => x.ok)}/{r.Count}  (from behind {fromBehind.Count(x => x.ok)}/{fromBehind.Count})");
        }
        int wins = res.Count(x => x.ok);
        int contacts = res.Sum(x => x.contacts);
        var gaps = res.Where(x => !double.IsNaN(x.gap)).Select(x => x.gap).OrderBy(x => x).ToList();
        Console.WriteLine($"  overall: skill order held in {wins}/{res.Length} runs ({(double)wins / res.Length:P0}); {contacts} contacts in {res.Length} races ({(double)contacts / res.Length:0.00} per race); median winning gap {gaps[gaps.Count / 2]:0.0} s");
        Check((double)wins / res.Length >= 0.8, "drivers ranked by skill finish in that order in at least 80% of runs");
        Check(res.Max(x => x.stuck) < 5, "no AI is stuck for more than 5 s");
    }

    /// <summary>Full grids of the six skills on every route: how a crowd races (order, contacts, spread).</summary>
    static void Grids(List<RaceEvent> events)
    {
        Console.WriteLine("\nFull grids: the six skills together in identical cars, random grid order");
        int runs = 0, pairs = 0, agree = 0, contacts = 0, walls = 0;
        double maxStuck = 0;
        var spread = new List<double>();
        string onlyEvent = Environment.GetEnvironmentVariable("AI_EVENT");
        foreach (var ev0 in events)
        {
            if (onlyEvent != null && ev0.Id != onlyEvent) continue;
            var ev = ev0.Kind == RaceKind.Circuit ? EventCatalog.ProvingGP(W, 2) : ev0;
            var line = RacingLine.Build(ev.Route);
            for (int seed = 1; seed <= 2; seed++)
            {
                var rng = new Rng((uint)(seed * 31 + ev.Id.Length));
                var order = Enumerable.Range(0, Skills.Length).OrderBy(_ => rng.Next()).ToList();
                var field = order.Select(k => ("halden-aster-gt", Driver(Skills[k]))).ToList();
                var run = RunRace(ev, field, (uint)seed, ev.Kind == RaceKind.Circuit ? 600 : 1200, line);
                var res = run.Session.Order.Select(e => e.Ai.P.Skill).ToList();
                runs++;
                for (int i = 0; i < res.Count; i++)
                    for (int j = i + 1; j < res.Count; j++)
                    {
                        pairs++;
                        if (res[i] > res[j]) agree++;
                    }
                contacts += run.Session.Contacts;
                walls += run.Session.WallHits;
                foreach (var e in run.Session.Entrants) maxStuck = Math.Max(maxStuck, e.Ai.MaxStuck);
                var fin = run.Session.Entrants.Where(e => e.Finished).Select(e => e.FinishTime).ToList();
                if (fin.Count > 1) spread.Add((fin.Max() - fin.Min()) / fin.Min());
                Console.WriteLine($"  {ev.Id,-20} seed {seed}: finishing order by skill {string.Join(" ", res.Select(x => x.ToString("0.00")))}  contacts {run.Session.Contacts}  wall hits {run.Session.WallHits}  ({run.ms:0} ms)");
            }
        }
        Console.WriteLine($"  pairwise skill order {(double)agree / pairs:P0}; {(double)contacts / runs:0.0} contacts and {(double)walls / runs:0.0} wall hits per race; finish spread {spread.Average():P1}; max stuck {maxStuck:0.0} s");
        Check(maxStuck < 5, "full grids: no AI is stuck for more than 5 s");
    }

    /// <summary>Two drivers, the first on pole: gaps, lanes and pass state every interval seconds.</summary>
    static void DuelTrace(RaceEvent ev0, double skPole, double skBehind, uint seed, double every)
    {
        var ev = ev0.Kind == RaceKind.Circuit ? EventCatalog.ProvingGP(W, 2) : ev0;
        var sim = new Simulation(W, SpawnAt.Island);
        var field = new List<(string, Personality)> { ("halden-aster-gt", Driver(skPole)), ("halden-aster-gt", Driver(skBehind)) };
        var race = new RaceSession(sim, ev, field, Abilities, seed, includePlayer: false);
        var a = race.Entrants[0];
        var b = race.Entrants[1];
        a.Ai.OnEvent = m => Console.WriteLine($"    pole: {m}");
        b.Ai.OnEvent = m => Console.WriteLine($"    behind: {m}");
        var c = Controls.Neutral();
        double next = 0;
        int contacts = 0;
        while (!race.AllFinished && race.Clock < 1200)
        {
            race.Step(c);
            if (race.Contacts != contacts)
            {
                contacts = race.Contacts;
                Console.WriteLine($"    contact at t {race.Clock:0.00} s {a.S:0}: pole lat {a.Lateral:0.0} v {a.Car.vehicle.vLong:0.0}, behind lat {b.Lateral:0.0} v {b.Car.vehicle.vLong:0.0}");
            }
            if (race.Clock < next) continue;
            next += every;
            Console.WriteLine($"  t {race.Clock,6:0.0} gap {b.Progress - a.Progress,6:0.0} m  pole s {a.S,6:0} lat {a.Lateral,5:0.0} v {a.Car.vehicle.vLong * 3.6,4:0}  behind lat {b.Lateral,5:0.0} v {b.Car.vehicle.vLong * 3.6,4:0} vT {b.Ai.TargetSpeed * 3.6,4:0} lane {b.Ai.Lane,5:0.0} tgt {b.Ai.DebugLaneTarget,5:0.0} pass {b.Ai.DebugPassSide,2} {b.Ai.DebugPassT:0.0}  w {ev.Route.Sample(ev.Route.WLeft, a.S):0.0}/{ev.Route.Sample(ev.Route.WRight, a.S):0.0}  | pole th {a.Car.vehicle.input.throttle:0.00} br {a.Car.vehicle.input.brake:0.00} g {a.Car.vehicle.gear} tcs {a.Car.vehicle.tcsActive}  behind th {b.Car.vehicle.input.throttle:0.00} br {b.Car.vehicle.input.brake:0.00} g {b.Car.vehicle.gear} tcs {b.Car.vehicle.tcsActive} draft {b.Car.vehicle.draftFactor:0.00}");
        }
        Console.WriteLine($"  finish: pole {Fmt(a.FinishTime)}  behind {Fmt(b.FinishTime)}  contacts {race.Contacts}");
    }

    /// <summary>Pace by skill with no other cars: how much each step of skill is worth on each route.</summary>
    static void SkillSolo(List<RaceEvent> events)
    {
        Console.WriteLine("\nSkill alone: one Aster GT per run, finish times by skill and seed");
        double[] skills = { 0.30, 0.44, 0.58, 0.72, 0.86, 1.00 };
        foreach (var ev0 in events)
        {
            var ev = ev0.Kind == RaceKind.Circuit ? EventCatalog.ProvingGP(W, 2) : ev0;
            var line = RacingLine.Build(ev.Route);
            foreach (double sk in skills)
            {
                var times = new List<string>();
                for (uint seed = 1; seed <= 3; seed++)
                {
                    var p = new Personality { Name = "S", Skill = sk, Aggression = 0.5, Consistency = 0.9, Risk = 0.5 };
                    var run = RunRace(ev, new List<(string, Personality)> { ("halden-aster-gt", p) }, seed, 1200, line);
                    var e = run.Session.Entrants[0];
                    times.Add($"{Fmt(e.FinishTime)}{(e.Ai.Resets > 0 ? $" ({e.Ai.Resets}R)" : "")}");
                }
                Console.WriteLine($"  {ev.Id,-20} skill {sk:0.00}: {string.Join("  ", times)}");
            }
        }
    }

    /// <summary>Two skills alone on one route: split times every 250 m, with slide and ESC time per split.</summary>
    static void Compare(RaceEvent ev0, double skA, double skB, uint seed)
    {
        var ev = ev0.Kind == RaceKind.Circuit ? EventCatalog.ProvingGP(W, 2) : ev0;
        var line = RacingLine.Build(ev.Route);
        List<(double t, double vmin, double slide, double esc, double s)> Splits(double sk)
        {
            var sim = new Simulation(W, SpawnAt.Island);
            var p = new Personality { Name = "S", Skill = sk, Aggression = 0.5, Consistency = 0.9, Risk = 0.5 };
            var race = new RaceSession(sim, ev, new List<(string, Personality)> { ("halden-aster-gt", p) }, Abilities, seed, includePlayer: false, line: line);
            var e = race.Entrants[0];
            if (Environment.GetEnvironmentVariable("AI_ESC") == "0") e.Car.vehicle.aids.esc = false;
            var c = Controls.Neutral();
            var o = new List<(double, double, double, double, double)>();
            double next = 250, vmin = 1e9, slide = 0, esc = 0;
            e.Ai.OnEvent = m => Console.WriteLine($"    skill {sk}: {m}");
            while (!race.AllFinished && race.Clock < 1200)
            {
                race.Step(c);
                if (race.Clock < 0) continue;
                var v = e.Car.vehicle;
                vmin = Math.Min(vmin, v.vLong);
                double beta = Math.Atan2(v.vLat, Math.Max(2, Math.Abs(v.vLong)));
                if (Math.Abs(beta) > 0.10) slide += Loop.SIM_DT;
                if (v.escActive) esc += Loop.SIM_DT;
                if (e.Progress >= next)
                {
                    o.Add((race.Clock, vmin, slide, esc, e.S));
                    next += 250; vmin = 1e9; slide = 0; esc = 0;
                }
            }
            return o;
        }
        var a = Splits(skA);
        var b = Splits(skB);
        double pa = 0, pb = 0;
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
        {
            double da = a[i].t - pa, db = b[i].t - pb;
            pa = a[i].t; pb = b[i].t;
            Console.WriteLine($"  {(i + 1) * 250,5} m (s {a[i].s,5:0})  {skA:0.00}: {da,6:0.00} s vmin {a[i].vmin * 3.6,4:0} slide {a[i].slide:0.0} esc {a[i].esc:0.0}   {skB:0.00}: {db,6:0.00} s vmin {b[i].vmin * 3.6,4:0} slide {b[i].slide:0.0} esc {b[i].esc:0.0}   diff {db - da,6:+0.00;-0.00}");
        }
    }

    static void MixedField(List<RaceEvent> events)
    {
        Console.WriteLine("\nMixed field: eight cars and drivers around Pro difficulty");
        foreach (var ev0 in events.Take(2))
        {
            var ev = ev0.Kind == RaceKind.Circuit ? EventCatalog.ProvingGP(W, 2) : ev0;
            var field = RaceSession.Field("halden-aster-gt", 8, Difficulty.Pro, 7);
            var run = RunRace(ev, field, 7, 1200);
            var s = run.Session;
            foreach (var e in s.Order) Console.WriteLine($"    P{e.Position} {e.Name,-14} {e.Car.specId,-24} skill {e.Ai.P.Skill:0.00}  {Fmt(e.FinishTime)}  resets {e.Ai.Resets}");
            double maxStuck = s.Entrants.Max(e => e.Ai.MaxStuck);
            Console.WriteLine($"  {ev.Id}: contacts {s.Contacts}, max stuck {maxStuck:0.0} s, {run.ms:0} ms for {run.Seconds:0} s of racing");
            Check(s.AllFinished && maxStuck < 5, $"{ev.Id}: every car finishes, none stuck over 5 s");
        }
    }
}
