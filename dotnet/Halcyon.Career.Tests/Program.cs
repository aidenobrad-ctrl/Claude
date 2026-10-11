// The career layer, headless: performance index, upgrades, economy, saves,
// tune share codes and auto-tune.
//   cd dotnet/Halcyon.Career.Tests && dotnet run -c Release [-- pi|...]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Halcyon.Core;

static class Program
{
    static int failures;

    static void Check(bool ok, string what)
    {
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {what}");
        if (!ok) failures++;
    }

    static int Main(string[] args)
    {
        string only = args.Length > 0 ? args[0] : "";
        if (only == "" || only == "pi") Pi();
        if (only == "brakes") BrakeProbe();
        if (only == "us") UndersteerProbe();
        if (only == "" || only == "parts") PartsAndPrices();
        if (only == "" || only == "save") Saves();
        if (only == "" || only == "tune") Tuning();
        if (only == "" || only == "ladder") Ladder();
        if (only == "" || only == "rivals") RivalFields();
        if (only == "" || only == "extremes") Extremes();
        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "CAREER: PASS" : $"CAREER: {failures} FAILED");
        return failures == 0 ? 0 : 1;
    }

    static readonly Dictionary<string, PiClass> StockClass = new Dictionary<string, PiClass>
    {
        ["ironbark-ridgeback"] = PiClass.D, ["halden-280-sprint"] = PiClass.D, ["aurelle-pico"] = PiClass.D,
        ["stallworth-prairie"] = PiClass.D, ["stallworth-ironside-454"] = PiClass.D,
        ["hoshida-kiri-rs"] = PiClass.B, ["aurelle-vif-r"] = PiClass.B,
        ["halden-aster-gt"] = PiClass.A, ["nordvik-kyst-sport"] = PiClass.A, ["nordvik-varg"] = PiClass.A,
        ["orsini-saetta"] = PiClass.S1, ["arcline-vanta"] = PiClass.S2,
    };

    static void PartsAndPrices()
    {
        Console.WriteLine("\nParts and prices");
        var spec = Cars.ById("halden-aster-gt");
        int carPrice = Economy.CarPrice(703);
        foreach (var kv in Parts.Catalog)
        {
            var prices = new List<int>();
            foreach (var part in kv.Value) prices.Add(Parts.Price(part, carPrice));
            Console.WriteLine($"  {kv.Key,-13} " + string.Join("  ", kv.Value.Select(pt => $"{pt.Name} {Parts.Price(pt, carPrice):N0}")));
        }
        bool rising = true;
        foreach (var kv in Parts.Catalog)
        {
            if (kv.Key == PartCategory.Tires) continue; // rally and off-road tyres are alternatives, not a ladder
            for (int i = 1; i < kv.Value.Length; i++) if (Parts.Price(kv.Value[i], carPrice) <= Parts.Price(kv.Value[i - 1], carPrice)) rising = false;
        }
        Check(rising, "each part ladder costs more at every level");
        var up = Parts.Build(spec, new Dictionary<PartCategory, int> { [PartCategory.Engine] = 3, [PartCategory.Weight] = 2, [PartCategory.Tires] = 2, [PartCategory.Brakes] = 2, [PartCategory.Aero] = 2 });
        Check(Math.Abs(up.powerScale - 1.22) < 1e-9 && Math.Abs(up.massScale - 0.94) < 1e-9 && up.compound == Compound.Slick || up.compound == Compound.SemiSlick, $"parts fold into the upgrades layer (power x{up.powerScale:0.00}, mass x{up.massScale:0.00}, {up.compound})");
        var unl = Parts.Unlocked(new Dictionary<PartCategory, int> { [PartCategory.Suspension] = 1, [PartCategory.Brakes] = 2 });
        Check((unl & TuneGroup.Springs) != 0 && (unl & TuneGroup.Brakes) != 0 && (unl & TuneGroup.Damping) == 0 && (unl & TuneGroup.Pressure) != 0, "sport suspension and race brakes unlock springs and brake tuning, not damping");

        Console.WriteLine("\nCar prices from PI");
        int prev = 0;
        bool up2 = true;
        foreach (var c in Cars.All.Select(c => (c, s: Performance.Measure(c))).OrderBy(x => x.s.Pi))
        {
            int price = Economy.CarPrice(c.s.Pi);
            Console.WriteLine($"  {c.c.id,-26} {Performance.Label(c.s),-7} {price,12:N0} credits");
            if (price < prev) up2 = false;
            prev = price;
        }
        Check(up2, "price rises with PI");
        Check(Economy.CarPrice(457) > 15000 && Economy.CarPrice(457) < 40000 && Economy.CarPrice(928) > 1500000, "a compact costs tens of thousands, a hypercar millions");
        int p1 = Economy.Payout(11165, 1, PiClass.A, Difficulty.Pro), p2 = Economy.Payout(11165, 2, PiClass.A, Difficulty.Pro), p8 = Economy.Payout(11165, 8, PiClass.A, Difficulty.Pro);
        Console.WriteLine($"  Proving GP, class A, Pro: P1 {p1:N0}, P2 {p2:N0}, P8 {p8:N0} credits, {Economy.RaceXp(11165, 1)} XP for a win");
        Check(p1 > p2 && p2 > p8 && p8 > 0, "payouts fall down the order");
        Check(Economy.Payout(5000, 1, PiClass.S2, Difficulty.Legend) > Economy.Payout(5000, 1, PiClass.D, Difficulty.Novice) * 3, "higher classes and harder rivals pay more");
        var (lvl, into, need) = Economy.Level(5000);
        Check(lvl == 3 && into >= 0 && into < need, $"5000 XP is level {lvl} ({into}/{need})");
    }

    static void Saves()
    {
        Console.WriteLine("\nCareer saves");
        var p = Profile.New();
        Check(p.Credits == Economy.StartingCredits && p.CurrentCar.Id == "halden-aster-gt", "a new career starts with the Aster GT and the starting credits");
        int pico = Economy.CarPrice(457);
        Check(p.BuyCar("aurelle-pico", pico, out _), "buy a Pico");
        Check(!p.BuyCar("arcline-vanta", Economy.CarPrice(928), out string why) && why.Contains("more credits"), $"a Vanta is out of reach ({why})");
        var car = p.CurrentCar;
        Check(car.Id == "aurelle-pico", "the new car becomes the current car");
        int before = p.Credits;
        Check(p.FitPart(car, PartCategory.Engine, 2, 4000, out _) && p.Credits == before - 4000, "fitting a part costs its price");
        p.FitPart(car, PartCategory.Suspension, 2, 6000, out _);
        car.Tune = TuneSpec.AutoTune(car.Spec, car.Upgrades, Parts.Unlocked(car.Parts), TuneFor.Circuit, 12);
        car.Paint = new[] { 0.9, 0.2, 0.1 };
        car.Pi = 512;
        p.AddRace(9000, 1300, true);
        string json = p.ToJson();
        var q = Profile.FromJson(json);
        Check(q.ToJson() == json, $"a save reads back identically ({json.Length} bytes)");
        Check(q.CurrentCar.Level(PartCategory.Engine) == 2 && Math.Abs(q.CurrentCar.Tune.camberF - car.Tune.camberF) < 1e-12 && q.Wins == 1, "parts, tune and stats survive the round trip");
        Check(!p.SellCar(0, 1, out _) || p.Garage.Count == 1, "selling works and keeps at least one car");
        var four = Profile.New();
        four.BuyCar("aurelle-pico", 0, out _);
        four.BuyCar("halden-280-sprint", 0, out _);
        four.BuyCar("hoshida-kiri-rs", 0, out _);
        four.Current = 2;
        four.SellCar(0, 0, out _);
        Check(four.CurrentCar.Id == "halden-280-sprint", "selling a car listed before the current one keeps the current car");
        var damaged = json.Replace("aurelle-pico", "no-such-car");
        var r = Profile.FromJson(damaged);
        Check(r.Garage.Count >= 1, "a save with an unknown car still loads");
    }

    static void Tuning()
    {
        Console.WriteLine("\nTuning: every slider changes the physics");
        var spec = Cars.ById("nordvik-kyst-sport"); // AWD with an LSD, so every slider has something to act on
        string Run(Tune t)
        {
            var p = VehicleParams.Build(spec, t, new Upgrades { aeroKit = 1, clAAdd = 0.6 });
            var v = Telemetry.NewTestVehicle(p);
            var g = new FlatGround();
            for (int i = 0; i < 240; i++) v.Step(Loop.SIM_DT, g);
            v.SetSpeed(25);
            for (int i = 0; i < 960; i++)
            {
                double tt = i * Loop.SIM_DT;
                v.input.throttle = tt < 1.5 ? 0.7 : 0;
                v.input.steer = tt > 0.5 && tt < 2.5 ? 0.35 : 0;
                v.input.brake = tt > 2.5 ? 0.6 : 0;
                v.Step(Loop.SIM_DT, g);
            }
            return $"{v.pos.x:R},{v.pos.z:R},{v.vLong:R},{v.yawRate:R}";
        }
        var baseTune = Tune.Default(spec);
        int changed = 0;
        foreach (var f in TuneSpec.Fields)
        {
            var lo = baseTune.Clone();
            var hi = baseTune.Clone();
            f.Set(lo, 0, f.Min);
            f.Set(hi, 0, f.Max);
            bool differs = Run(lo) != Run(hi);
            if (differs) changed++;
            else Console.WriteLine($"  {f.Key} made no difference");
        }
        var g1 = baseTune.Clone();
        var g2 = baseTune.Clone();
        g2.gearScale[1] = TuneSpec.GearMax;
        bool gears = Run(g1) != Run(g2);
        Check(changed == TuneSpec.Fields.Length && gears, $"{changed}/{TuneSpec.Fields.Length} sliders and the gear ratios change the car's path");

        var custom = Tune.Default(spec);
        custom.camberF = -3;
        custom.springF = 1.4;
        custom.pressureF = 0.2;
        var allowed = TuneSpec.ForParts(spec, custom, TuneGroup.Pressure | TuneGroup.Springs, out int lockedN);
        Check(allowed.camberF == Tune.Default(spec).camberF && allowed.springF == 1.4 && allowed.pressureF == 0.2 && lockedN == 1,
            "settings a car's parts no longer unlock go back to default; unlocked ones keep their values");

        Console.WriteLine("\nShare codes");
        var rng = new Rng(7);
        int ok = 0, total = 0;
        foreach (var c in Cars.All)
        {
            var t = Tune.Default(c);
            foreach (var f in TuneSpec.Fields) f.Set(t, 0, f.Min + rng.Next() * (f.Max - f.Min));
            for (int i = 0; i < t.gearScale.Length; i++) t.gearScale[i] = TuneSpec.GearMin + rng.Next() * (TuneSpec.GearMax - TuneSpec.GearMin);
            t = TuneSpec.Snap(t);
            string code = TuneSpec.Encode(c, t);
            total++;
            if (TuneSpec.Decode(code, out var c2, out var t2, out _) && c2 == c && TuneSpec.Encode(c2, t2) == code) ok++;
            if (c.id == "halden-aster-gt") Console.WriteLine($"  Aster GT: {code}");
        }
        Check(ok == total, $"codes survive the round trip for every car ({ok}/{total})");
        string good = TuneSpec.Encode(spec, baseTune);
        char[] bad = good.ToCharArray();
        bad[10] = bad[10] == 'A' ? 'B' : 'A';
        Check(!TuneSpec.Decode(new string(bad), out _, out _, out string w1), $"a damaged code is refused ({w1})");
        Check(!TuneSpec.Decode("hello", out _, out _, out string w2), $"a non-code is refused ({w2})");

        Console.WriteLine("\nAuto-tune (by measurement)");
        var locked = TuneSpec.AutoTune(spec, Upgrades.None(), TuneGroup.Pressure, TuneFor.Circuit, 12);
        Check(locked.springF == 1 && locked.camberF == TuneSpec.Snap(Tune.Default(spec)).camberF, "without parts, auto-tune only touches tyre pressures");
        var parts = new Dictionary<PartCategory, int> { [PartCategory.Suspension] = 2, [PartCategory.Differential] = 1, [PartCategory.Brakes] = 2, [PartCategory.Transmission] = 1 };
        var up = Parts.Build(spec, parts);
        var unlocked = Parts.Unlocked(parts);
        var sw = Stopwatch.StartNew();
        var circuit = TuneSpec.AutoTune(spec, up, unlocked, TuneFor.Circuit);
        long msCircuit = sw.ElapsedMilliseconds;
        var dirt = TuneSpec.AutoTune(spec, up, unlocked, TuneFor.Dirt);
        var stock = TuneSpec.Snap(Tune.Default(spec));
        double sStockC = TuneSpec.Score(spec, stock, up, TuneFor.Circuit), sCircuitC = TuneSpec.Score(spec, circuit, up, TuneFor.Circuit);
        double sStockD = TuneSpec.Score(spec, stock, up, TuneFor.Dirt), sDirtD = TuneSpec.Score(spec, dirt, up, TuneFor.Dirt), sCircuitD = TuneSpec.Score(spec, circuit, up, TuneFor.Dirt);
        Console.WriteLine($"  circuit score: stock {sStockC:0.0000}, circuit tune {sCircuitC:0.0000} ({msCircuit} ms)");
        Console.WriteLine($"  dirt score:    stock {sStockD:0.0000}, dirt tune {sDirtD:0.0000}, circuit tune {sCircuitD:0.0000}");
        foreach (var f in TuneSpec.Fields)
        {
            double c0 = f.Get(stock), c1 = f.Get(circuit), d1 = f.Get(dirt);
            if (c0 != c1 || c0 != d1) Console.WriteLine($"    {f.Key,-14} stock {c0,6:0.###}  circuit {c1,6:0.###}  dirt {d1,6:0.###}");
        }
        double kStock = TestTrack.UndersteerGradient(VehicleParams.Build(spec, stock, up), 0.9);
        double kCircuit = TestTrack.UndersteerGradient(VehicleParams.Build(spec, circuit, up), 0.9);
        Console.WriteLine($"  understeer: stock {kStock:+0.00} deg/g, circuit tune {kCircuit:+0.00} deg/g");
        Check(kCircuit >= Math.Max(0.25, Math.Min(kStock - 0.15, 0.6)) - 0.02 && circuit.toeR >= 0 && dirt.toeR >= 0, "auto-tunes keep the car's understeer margin and never toe the rear out");
        Check(sCircuitC > sStockC, "the circuit auto-tune measures better than stock on asphalt");
        Check(sDirtD > sStockD, "the dirt auto-tune measures better than stock on gravel");
        Check(sDirtD >= sCircuitD, "on gravel the dirt tune is at least as good as the circuit tune");
    }

    static readonly PiTable Table = new PiTable();

    /// <summary>The rivals' upgrade ladder: PI must never fall from one step to the next (rivals binary-search it).</summary>
    static void Ladder()
    {
        Console.WriteLine("\nUpgrade ladder: PI at every step, stock first");
        var sw = Stopwatch.StartNew();
        var all = Cars.All;
        int n = Rivals.Ladder.Length;
        var pis = new int[all.Length, n];
        Parallel.For(0, all.Length * n, k => pis[k / n, k % n] = Table.Get(all[k / n].id, Rivals.Ladder[k % n]).Pi);
        bool rising = true;
        for (int i = 0; i < all.Length; i++)
        {
            var row = new List<string>();
            for (int j = 0; j < n; j++)
            {
                row.Add($"{pis[i, j],4}");
                if (j > 0 && pis[i, j] < pis[i, j - 1]) rising = false;
            }
            Console.WriteLine($"  {all[i].id,-26} {string.Join(" ", row)}");
        }
        Console.WriteLine($"  ({all.Length * n} measurements in {sw.ElapsedMilliseconds} ms on {Environment.ProcessorCount} cores)");
        Check(rising, "the PI never falls from one ladder step to the next, for any car");
        int top = 0;
        for (int i = 0; i < all.Length; i++) top = Math.Max(top, pis[i, n - 1]);
        Check(top <= 999, $"no car passes PI 999 with every part (highest {top})");
    }

    static void RivalFields()
    {
        Console.WriteLine("\nRivals matched to the player's PI (7 rivals, Pro)");
        int Stock(string id) => Table.Get(id, Rivals.Ladder[0]).Pi;
        int Full(string id) => Table.Get(id, Rivals.Ladder[Rivals.Ladder.Length - 1]).Pi;
        var cases = new (string what, int pi, PiClass? limit)[]
        {
            ("Aster GT stock, open", Stock("halden-aster-gt"), null),
            ("Aster GT stock, A class", Stock("halden-aster-gt"), PiClass.A),
            ("Pico stock, D class", Stock("aurelle-pico"), PiClass.D),
            ("Ridgeback stock, D class", Stock("ironbark-ridgeback"), PiClass.D),
            ("Pico with every part, open", Full("aurelle-pico"), null),
            ("Saetta stock, S1 class", Stock("orsini-saetta"), PiClass.S1),
            ("Vanta stock, open", Stock("arcline-vanta"), null),
            ("Vanta with every part, open", Full("arcline-vanta"), null),
        };
        bool inClass = true, close = true, capped = true;
        foreach (var (what, pi, limit) in cases)
        {
            var sw = Stopwatch.StartNew();
            var field = Rivals.Field(pi, limit, 7, Difficulty.Pro, 11, Table);
            long ms = sw.ElapsedMilliseconds;
            int cap = Rivals.Cap(pi, limit);
            var groups = field.GroupBy(r => (r.CarId, r.Pi)).Select(g => $"{g.Key.CarId} {g.Key.Pi}{(g.Count() > 1 ? " x" + g.Count() : "")}");
            Console.WriteLine($"  {what,-30} PI {pi,3} cap {cap,4}: {string.Join(", ", groups)}  ({ms} ms)");
            if (limit.HasValue && field.Any(r => r.Pi > Performance.ClassMax(limit.Value))) inClass = false;
            if (field.Any(r => Math.Abs(r.Pi - pi) > 40)) close = false;
            if (field.Any(r => r.Pi > cap) && field.Select(r => r.CarId).Distinct().Count() > 1) capped = false;
        }
        Check(inClass, "no rival is above the event's class");
        Check(close, "every rival is within 40 PI of the player");
        Check(capped, "rivals stay at or below the player's PI + 15, unless one car is all there is");
        var a = Rivals.Field(703, PiClass.A, 7, Difficulty.Pro, 5, Table);
        var b = Rivals.Field(703, PiClass.A, 7, Difficulty.Pro, 5, Table);
        Check(a.Select(r => r.CarId + r.Key + r.Driver.Name).SequenceEqual(b.Select(r => r.CarId + r.Key + r.Driver.Name)), "the same seed gives the same field");
    }

    /// <summary>
    /// Every slider at its minimum and at its maximum, alone, on every car,
    /// plus random combinations of extremes with every part: the test-track
    /// numbers must stay finite and physical.
    /// </summary>
    static void Extremes()
    {
        Console.WriteLine("\nExtreme tunes on the test track");
        var sw = Stopwatch.StartNew();
        var cases = new List<(CarSpec spec, string what, Tune tune, Upgrades up)>();
        foreach (var c in Cars.All)
        {
            foreach (var f in TuneSpec.Fields)
            {
                foreach (double v in new[] { f.Min, f.Max })
                {
                    var t = Tune.Default(c);
                    f.Set(t, 0, v);
                    cases.Add((c, $"{f.Key}={v}", t, null));
                }
            }
            foreach (double g in new[] { TuneSpec.GearMin, TuneSpec.GearMax })
            {
                var t = Tune.Default(c);
                for (int i = 0; i < t.gearScale.Length; i++) t.gearScale[i] = g;
                cases.Add((c, $"gears={g}", t, null));
            }
            var rng = new Rng(Hash.HashString(c.id));
            for (int k = 0; k < 6; k++)
            {
                var t = Tune.Default(c);
                foreach (var f in TuneSpec.Fields)
                {
                    double r = rng.Next();
                    if (r < 0.35) f.Set(t, 0, f.Min);
                    else if (r < 0.7) f.Set(t, 0, f.Max);
                }
                for (int i = 0; i < t.gearScale.Length; i++) t.gearScale[i] = rng.Next() < 0.5 ? TuneSpec.GearMin : TuneSpec.GearMax;
                cases.Add((c, $"random extremes {k} with every part", t, Parts.Build(c, Rivals.Ladder[Rivals.Ladder.Length - 1])));
            }
        }
        var bad = new System.Collections.Concurrent.ConcurrentBag<string>();
        var spins = new System.Collections.Concurrent.ConcurrentBag<string>();
        var worst = new System.Collections.Concurrent.ConcurrentBag<(double g, double brake, double t100, string what)>();
        Parallel.ForEach(cases, cs =>
        {
            var p = VehicleParams.Build(cs.spec, cs.tune, cs.up);
            var acc = Telemetry.AccelTest(p);
            var br = Telemetry.BrakeTest(p, 100);
            var pad = Telemetry.SkidpadTest(p, 40);
            double k = TestTrack.UndersteerGradient(p, pad.g);
            string id = $"{cs.spec.id} {cs.what}";
            bool finite = !double.IsNaN(acc.topKmh) && !double.IsInfinity(acc.topKmh) && !double.IsNaN(br.distance) && !double.IsNaN(pad.g);
            if (!finite) bad.Add($"{id}: non-finite (top {acc.topKmh}, brake {br.distance}, grip {pad.g})");
            else
            {
                if (pad.g > 2.2 || pad.g < 0.3) bad.Add($"{id}: skidpad {pad.g:0.000} g");
                if (br.distance < 15 || br.distance > 90) bad.Add($"{id}: 100-0 in {br.distance:0.0} m");
                if (double.IsNaN(acc.t100) || acc.t100 > 25) bad.Add($"{id}: 0-100 {acc.t100:0.00} s");
                if (acc.topKmh < 120 || acc.topKmh > 560) bad.Add($"{id}: top speed {acc.topKmh:0} km/h");
                // A combination of extremes may make a car that spins at 0.6 of
                // its grip (the test track then shows "spins"); one slider alone
                // must not.
                if (double.IsNaN(k))
                {
                    if (cs.what.StartsWith("random")) spins.Add(id);
                    else bad.Add($"{id}: spins on the 60 m circle with one slider at its limit");
                }
            }
            worst.Add((pad.g, br.distance, acc.t100, id));
        });
        foreach (var b in bad.OrderBy(x => x).Take(40)) Console.WriteLine($"    {b}");
        Console.WriteLine($"  {cases.Count} tunes in {sw.ElapsedMilliseconds} ms; most grip {worst.Max(w => w.g):0.000} g, shortest stop {worst.Min(w => w.brake):0.0} m, slowest 0-100 {worst.Max(w => double.IsNaN(w.t100) ? 99 : w.t100):0.00} s");
        Console.WriteLine($"  {spins.Count} of 72 random combinations of extremes spin on the 60 m circle: {string.Join(", ", spins.OrderBy(x => x))}");
        Check(bad.IsEmpty, $"every extreme tune stays finite and physical ({bad.Count} problems)");
    }

    static void UndersteerProbe()
    {
        foreach (var c in Cars.All)
        {
            var sw = Stopwatch.StartNew();
            var p = VehicleParams.Build(c);
            double g = Telemetry.SkidpadTest(p).g;
            double k = TestTrack.UndersteerGradient(p, g);
            Console.WriteLine($"  {c.id,-26} skidpad {g:0.000} g  understeer {k:+0.00;-0.00} deg/g  ({sw.ElapsedMilliseconds} ms)");
        }
        var spec = Cars.ById("nordvik-kyst-sport");
        var t = TuneSpec.Snap(Tune.Default(spec));
        void Try(string what, Action<Tune> f)
        {
            var x = t.Clone();
            f(x);
            var p = VehicleParams.Build(spec, x);
            Console.WriteLine($"  Kyst {what,-34} understeer {TestTrack.UndersteerGradient(p, 0.9):+0.00;-0.00} deg/g");
        }
        Try("stock", x => { });
        Try("rear toe-out -0.35", x => x.toeR = -0.35);
        Try("arb F 0.55 R 1.45", x => { x.arbF = 0.55; x.arbR = 1.45; });
        Try("springs F 0.88 R 1.12", x => { x.springF = 0.88; x.springR = 1.12; });
        Try("previous circuit auto-tune", x => { x.toeF = -0.45; x.toeR = -0.35; x.arbF = 0.55; x.arbR = 1.45; x.springF = 0.88; x.springR = 1.12; x.camberF = -1.8; x.camberR = -0.4; x.caster = 6.1; x.rideHeight = 0.03; });
        Try("arb F 1.5 R 0.6", x => { x.arbF = 1.5; x.arbR = 0.6; });
    }

    static void BrakeProbe()
    {
        foreach (var c in Cars.All)
        {
            var row = new List<string>();
            foreach (var comp in new Compound?[] { null, Compound.SemiSlick, Compound.Slick })
            {
                foreach (double scale in new[] { 1.0, 1.25, 1.5 })
                {
                    var p = VehicleParams.Build(c, null, new Upgrades { compound = comp, brakeScale = scale });
                    var b = Telemetry.BrakeTest(p, 100);
                    var b2 = Telemetry.BrakeTest(p, 200);
                    row.Add($"{(comp?.ToString() ?? "stock"),-9}x{scale:0.00} {b.distance:0.0}/{b2.distance:0.0}");
                }
            }
            Console.WriteLine($"  {c.id,-24} " + string.Join("  ", row));
        }
    }

    static void Pi()
    {
        Console.WriteLine("\nPerformance index of the stock roster");
        var stats = new Dictionary<string, CarStats>();
        foreach (var c in Cars.All)
        {
            var sw = Stopwatch.StartNew();
            var s = Performance.Measure(c);
            stats[c.id] = s;
            var (a, t, g, b) = Performance.Scores(s);
            Console.WriteLine($"  {c.id,-26} {Performance.Label(s),-7} 0-100 {s.ZeroTo100:0.00} s  top {s.TopSpeed:0.0} km/h  grip {s.Skidpad:0.000} g ({s.GripAtSpeed:0.000} at 180)  100-0 {s.Braking:0.0} m   scores {a:0.00}/{t:0.00}/{g:0.00}/{b:0.00}  ({sw.ElapsedMilliseconds} ms)");
        }
        foreach (var kv in StockClass) Check(stats[kv.Key].Class == kv.Value, $"{kv.Key} is class {kv.Value} (measured {Performance.Label(stats[kv.Key])})");
        Check(Math.Abs(stats["halden-aster-gt"].ZeroTo100 - 4.74) < 0.01 && Math.Abs(stats["halden-aster-gt"].TopSpeed - 297.6) < 0.1, "the Aster GT measures as docs/CARS.md (4.74 s, 297.6 km/h)");
        var order = stats.OrderByDescending(kv => kv.Value.Pi).Select(kv => kv.Key).ToList();
        Check(order[0] == "arcline-vanta" && order[1] == "orsini-saetta", "the hypercar and the supercar have the two highest PIs");
        var again = Performance.Measure(Cars.ById("halden-aster-gt"));
        Check(again.Pi == stats["halden-aster-gt"].Pi && again.Braking == stats["halden-aster-gt"].Braking, "measuring twice gives identical results");

        Console.WriteLine("\nEvery kind of upgrade raises the PI (Aster GT)");
        var spec = Cars.ById("halden-aster-gt");
        int basePi = stats[spec.id].Pi;
        var cases = new (string what, Upgrades up)[]
        {
            ("+20% power", new Upgrades { powerScale = 1.2 }),
            ("-10% mass", new Upgrades { massScale = 0.9 }),
            ("semi-slick tyres", new Upgrades { compound = Compound.SemiSlick }),
            ("race aero kit", new Upgrades { aeroKit = 1, clAAdd = 0.6 }),
        };
        foreach (var (what, up) in cases)
        {
            var s = Performance.Measure(spec, null, up);
            Console.WriteLine($"  {what,-18} {Performance.Label(s),-7} 0-100 {s.ZeroTo100:0.00}  top {s.TopSpeed:0.0}  grip {s.GripAtSpeed:0.000}  100-0 {s.Braking:0.0}");
            Check(s.Pi > basePi, $"{what} raises the PI ({basePi} -> {s.Pi})");
        }
        // Brakes only help a car whose brakes, not tyres, limit the stop: the
        // Ironside's small brakes do, the Aster GT's do not (and costs no PI).
        var muscle = Cars.ById("stallworth-ironside-454");
        var m0 = stats[muscle.id];
        var m1 = Performance.Measure(muscle, null, new Upgrades { brakeScale = 1.25 });
        Console.WriteLine($"  Ironside +25% brakes: 100-0 {m0.Braking:0.0} -> {m1.Braking:0.0} m, PI {m0.Pi} -> {m1.Pi}");
        Check(m1.Braking < m0.Braking - 3 && m1.Pi > m0.Pi, "bigger brakes shorten the Ironside's stop and raise its PI");
        var a1 = Performance.Measure(spec, null, new Upgrades { brakeScale = 1.25 });
        Check(Math.Abs(a1.Pi - basePi) <= 1, $"the Aster GT's stop is tyre-limited, so bigger brakes leave its PI alone ({basePi} -> {a1.Pi})");
    }
}
