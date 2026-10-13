// References for the web build's port of the AI (src/ai/): the routes, racing
// lines and speed profiles of the festival's events, and measured car
// abilities, written by the C# (the original) for tests/ai.test.ts.
//   cd dotnet/Halcyon.AI.Tests && dotnet run -c Release -- airef
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Halcyon.Core;

static class AiRef
{
    /// <summary>The countdown (720 steps) and two seconds of racing: compared to the last bit.</summary>
    const int TraceSteps = 720 + 480;

    static string N(double v) => double.IsNaN(v) ? "null" : double.IsPositiveInfinity(v) ? "1e308" : v.ToString("R", CultureInfo.InvariantCulture);

    static string Arr(double[] a, int step = 1)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < a.Length; i += step)
        {
            if (i > 0) sb.Append(',');
            sb.Append(N(a[i]));
        }
        return sb.Append(']').ToString();
    }

    /// <summary>The reference race step by step (every `every` steps: each car's position, speed and inputs), to find where a port drifts.</summary>
    public static void Trace(WorldData w, int every, int until)
    {
        var sim = new Simulation(w, SpawnAt.Island);
        sim.Weather.Wetness = 0;
        var gp = EventCatalog.ProvingGP(w, 1);
        var field = RaceSession.Field(sim.Player.specId, 5, Difficulty.Pro, 7);
        var race = new RaceSession(sim, gp, field, new RaceSession.AbilityCache(), 7, includePlayer: false);
        var c = Controls.Neutral();
        for (int step = 0; step <= until; step++)
        {
            if (step % every == 0)
            {
                var sb = new StringBuilder($"{step}");
                foreach (var e in race.Entrants)
                {
                    var v = e.Car.vehicle;
                    sb.Append($" | {N(v.pos.x)} {N(v.pos.z)} {N(v.vel.x)} {N(v.input.steer)} {N(v.input.throttle)} {N(v.input.brake)} {N(e.S)} {N(v.draftFactor)} {N(v.pos.y)} {N(v.vel.y)} {N(v.vel.z)}");
                }
                Console.WriteLine(sb.ToString());
                if (step >= until - 3)
                {
                    var v2 = race.Entrants[2].Car.vehicle;
                    foreach (var wh in v2.wheels) Console.WriteLine($"wheel {step} {(wh.grounded ? 1 : 0)} {wh.surface} {N(wh.contact.x)} {N(wh.contact.y)} {N(wh.contact.z)} {N(wh.normal.x)} {N(wh.normal.y)} {N(wh.normal.z)} {N(wh.length)} {N(wh.load)}");
                }
            }
            long tick = sim.Tick;
            race.Step(c);
            if (step % every == 0)
                foreach (var ev in sim.Events)
                    if (ev.tick == tick) Console.WriteLine($"event {step} {ev.kind} car {ev.carId} {N(ev.x)} {N(ev.z)} {N(ev.value)}");
        }
    }

    public static void Write(string root, WorldData w, Simulation sim)
    {
        var sb = new StringBuilder("{\"events\":[");
        var events = EventCatalog.Festival(w, sim.Ground);
        bool first = true;
        var cache = new RaceSession.AbilityCache();
        foreach (var e in events)
        {
            // The web build has these (no conditions of their own, no ghost).
            if (e.Hour.HasValue || e.Weather.HasValue || e.Ghost) continue;
            var r = e.Route;
            var line = RacingLine.Build(r);
            var car = cache.Get("halden-aster-gt", null, null, "stock", 0, e.Surface);
            var p = new Personality { Skill = 0.8, Risk = 0.5 };
            var prof = SpeedProfile.Build(line.Blend(p.LineAccuracy), car, p.GripScale, p.BrakeScale, p.AccelScale);
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"{{\"id\":\"{e.Id}\",\"kind\":\"{e.Kind}\",\"n\":{r.N},\"length\":{N(r.Length)},\"startS\":{N(e.StartS)},\"distance\":{N(e.Distance)},\"closed\":{(r.Closed ? "true" : "false")}");
            sb.Append($",\"x\":{Arr(r.X)},\"z\":{Arr(r.Z)},\"y\":{Arr(r.Y)},\"wl\":{Arr(r.WLeft)},\"wr\":{Arr(r.WRight)}");
            sb.Append($",\"offset\":{Arr(line.Offset)},\"curv\":{Arr(line.Curv)},\"v\":{Arr(prof.V)}}}");
        }
        sb.Append("],\"abilities\":[");
        first = true;
        foreach (var (id, wet, surf) in new[] { ("halden-aster-gt", 0.0, Surface.Asphalt), ("arcline-vanta", 0.0, Surface.Asphalt), ("aurelle-pico", 0.4, Surface.Asphalt), ("hoshida-kiri-rs", 0.0, Surface.Dirt) })
        {
            var a = CarAbility.Measure(Cars.ById(id), null, null, wet, surf);
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"{{\"id\":\"{id}\",\"wetness\":{N(wet)},\"surface\":{surf},\"mu\":{N(a.Mu)},\"brakeG\":{N(a.BrakeG)},\"aeroK\":{N(a.AeroK)},\"dragK\":{N(a.DragK)},\"topSpeed\":{N(a.TopSpeed)},\"rollG\":{N(a.RollG)},\"accel\":{Arr(a.Accel)}");
            sb.Append($",\"stable\":[{N(a.StableBrake(0))},{N(a.StableBrake(0.5))},{N(a.StableBrake(1.0))}]}}");
        }
        sb.Append(']');
        // A whole race for the web's port to replay: five pro AI over a lap of the proving ground. The
        // countdown and the first two seconds of racing (the start, the grid hold, the first passes) are
        // the same arithmetic on both sides and must agree to the last bit; after that the island's ground
        // (exported to Unity in 32-bit floats) lets a wheel on a paint edge land differently, and the
        // finishing times need only agree closely.
        {
            var race0 = new Simulation(w, SpawnAt.Island);
            race0.Weather.Wetness = 0;
            var gp = EventCatalog.ProvingGP(w, 1);
            var field = RaceSession.Field(race0.Player.specId, 5, Difficulty.Pro, 7);
            var race = new RaceSession(race0, gp, field, cache, 7, includePlayer: false);
            var c = Controls.Neutral();
            long steps = 0;
            var trace = new StringBuilder();
            while (!race.AllFinished && race.Clock < 600)
            {
                if (steps <= TraceSteps && steps % 12 == 0)
                {
                    if (trace.Length > 0) trace.Append(',');
                    trace.Append('[').Append(steps);
                    foreach (var e in race.Entrants)
                    {
                        var v = e.Car.vehicle;
                        trace.Append(',').Append(N(v.pos.x)).Append(',').Append(N(v.pos.z)).Append(',').Append(N(v.vel.x)).Append(',').Append(N(v.input.steer)).Append(',').Append(N(v.input.throttle)).Append(',').Append(N(v.input.brake));
                    }
                    trace.Append(']');
                }
                race.Step(c);
                steps++;
            }
            sb.Append($",\"race\":{{\"event\":\"{gp.Id}\",\"laps\":1,\"count\":5,\"seed\":7,\"steps\":{steps},\"contacts\":{race.Contacts},\"wallHits\":{race.WallHits}");
            sb.Append($",\"cars\":[{string.Join(",", field.Select(f => $"\"{f.carId}\""))}]");
            sb.Append($",\"times\":[{string.Join(",", race.Entrants.Select(e => N(e.FinishTime)))}]");
            sb.Append($",\"trace\":[{trace}]}}");
            Console.WriteLine($"race: {steps} steps, contacts {race.Contacts}, times {string.Join(" ", race.Entrants.Select(e => e.FinishTime.ToString("0.000")))}");
        }
        sb.Append('}');
        string dir = Path.Combine(root, "dotnet/ref/ai");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ai-ref.json"), sb.ToString());
        Console.WriteLine($"wrote {Path.Combine(dir, "ai-ref.json")} ({sb.Length / 1024} KB)");
    }
}
