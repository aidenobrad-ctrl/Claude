// Equivalence of the C# island (WorldData + IslandGround) with the web
// version: ground samples, nearest-road queries, tree trunks and colliders
// against references from tools/dev/world-ref.ts.
//   cd dotnet/Halcyon.World.Tests && dotnet run -c Release
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Halcyon.Core;

static class Program
{
    static int failures;

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

    static int Main()
    {
        string root = Root();
        var sw = Stopwatch.StartNew();
        var w = WorldData.LoadFromDirectory(Path.Combine(root, "unity/Assets/Halcyon/Data/Resources/HalcyonIsland"));
        Console.WriteLine($"loaded island in {sw.ElapsedMilliseconds} ms: {w.N}x{w.N} grid, {w.Edges.Length} roads, {w.Rails.Length} rails, {w.TreeCount} trees, {w.Segments.Length} segments, {w.Buildings.Length} buildings");
        sw.Restart();
        var ground = new IslandGround(w);
        var colliders = ground.BuildColliders();
        Console.WriteLine($"ground + colliders built in {sw.ElapsedMilliseconds} ms");
        var refj = Json.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(root, "dotnet/ref/world/world-ref.json"))));

        // --- Ground samples -------------------------------------------------------------
        var g = new GroundHit();
        int n = 0, surfMiss = 0, okMiss = 0;
        double maxDy = 0, maxDn = 0, maxDw = 0;
        var dyList = new List<double>();
        string worst = "";
        foreach (var o in refj.Arr("samples"))
        {
            var a = Json.Arr(o);
            double x = Json.Num(a[0]), z = Json.Num(a[1]), yRef = Json.Num(a[2]);
            bool ok = ground.Sample(x, z, yRef, g);
            n++;
            if (ok != (Json.Num(a[3]) == 1)) { okMiss++; continue; }
            if (!ok) continue;
            double dy = Math.Abs(g.y - Json.Num(a[4]));
            dyList.Add(dy);
            if (dy > maxDy) { maxDy = dy; worst = $"({x:F2}, {z:F2}) yRef {yRef:F1}: {g.y:F3} vs {Json.Num(a[4]):F3}, surf {g.surface} vs {Json.Num(a[8])}"; }
            maxDn = Math.Max(maxDn, Math.Abs(g.nx - Json.Num(a[5])) + Math.Abs(g.ny - Json.Num(a[6])) + Math.Abs(g.nz - Json.Num(a[7])));
            if (g.surface != (int)Json.Num(a[8])) surfMiss++;
            maxDw = Math.Max(maxDw, Math.Abs(g.water - Json.Num(a[9])));
        }
        dyList.Sort();
        double p99 = dyList[(int)(dyList.Count * 0.99)];
        double p999 = dyList[(int)(dyList.Count * 0.999)];
        Console.WriteLine($"ground: {n} samples; |dy| p99 {p99 * 100:F2} cm, p99.9 {p999 * 100:F2} cm, max {maxDy * 100:F2} cm; max |dn| {maxDn:F4}; max |dwater| {maxDw * 100:F2} cm; surface mismatches {surfMiss}; hit/miss mismatches {okMiss}");
        Console.WriteLine($"  worst: {worst}");
        Check(okMiss == 0, "same points have ground");
        Check(p99 < 0.012, "heights within 1.2 cm for 99% of samples (16-bit terrain)");
        Check(p999 < 0.05, "heights within 5 cm for 99.9% of samples");
        Check(surfMiss <= n / 1000, "surfaces agree (at most 0.1% differ, at quantization boundaries)");
        Check(maxDw < 0.05, "water depth within 5 cm");

        // --- Nearest road ---------------------------------------------------------------------
        var rh = new RoadHit();
        int rn = 0, rMiss = 0, ties = 0;
        double rMaxDy = 0, rMaxDd = 0;
        foreach (var o in refj.Arr("roads"))
        {
            var a = Json.Arr(o);
            double x = Json.Num(a[0]), z = Json.Num(a[1]);
            bool ok = ground.NearestRoad(x, z, 4.2, rh);
            rn++;
            bool rok = Json.Num(a[2]) == 1;
            if (ok != rok) { rMiss++; continue; }
            if (!ok) continue;
            // A different segment is only a disagreement if it is farther: at a shared
            // vertex, a junction or the centre of a hairpin several road points can
            // be equally near, and float32 export may flip which one wins.
            bool sameSeg = rh.edge == (int)Json.Num(a[3]) && rh.i == (int)Json.Num(a[4]);
            if (!sameSeg)
            {
                if (Math.Abs(rh.dist - Json.Num(a[7])) < 0.001) { ties++; continue; }
                rMiss++;
                if (rMiss <= 8) Console.WriteLine($"    ({x:F2}, {z:F2}): C# e{rh.edge} i{rh.i} y {rh.y:F3} d {rh.dist:F3} | TS e{Json.Num(a[3])} i{Json.Num(a[4])} y {Json.Num(a[8]):F3} d {Json.Num(a[7]):F3}");
                continue;
            }
            if (rh.bridge != (Json.Num(a[10]) == 1)) { rMiss++; continue; }
            rMaxDy = Math.Max(rMaxDy, Math.Abs(rh.y - Json.Num(a[8])));
            rMaxDd = Math.Max(rMaxDd, Math.Abs(rh.dist - Json.Num(a[7])) + Math.Abs(rh.lateral - Json.Num(a[6])));
        }
        Console.WriteLine($"roads: {rn} queries, {ties} pick a different segment at a tie, {rMiss} disagree; max |dy| {rMaxDy * 1000:F2} mm, max |d dist|+|d lateral| {rMaxDd * 1000:F2} mm");
        Check(rMiss == 0, "nearest road agrees (equal distance, same bridge flag)");
        Check(rMaxDy < 0.002 && rMaxDd < 0.004, "road heights and offsets within millimetres");

        // --- Trees -----------------------------------------------------------------------------
        int tq = 0, tMiss = 0;
        double tMax = 0;
        foreach (var o in refj.Arr("trees"))
        {
            var a = Json.Arr(o);
            double x = Json.Num(a[0]), z = Json.Num(a[1]);
            var got = new List<double>();
            ground.QueryTrees(x, z, 6, c => { got.Add(c.x); got.Add(c.z); got.Add(c.r); got.Add(c.bottom); got.Add(c.top); got.Add(c.breakable ? 1 : 0); });
            tq++;
            if (got.Count != a.Count - 2) { tMiss++; continue; }
            for (int k = 0; k < got.Count; k++) tMax = Math.Max(tMax, Math.Abs(got[k] - Json.Num(a[k + 2])));
        }
        Console.WriteLine($"trees: {tq} queries, {tMiss} with different trunk counts; max field difference {tMax * 100:F2} cm");
        Check(tMiss == 0, "same tree trunks found");
        Check(tMax < 0.03, "trunk positions, radii and heights within 3 cm");

        // --- Colliders ---------------------------------------------------------------------------
        int cq = 0, cMiss = 0;
        foreach (var o in refj.Arr("cols"))
        {
            var a = Json.Arr(o);
            double x = Json.Num(a[0]), z = Json.Num(a[1]);
            int segs = 0, circ = 0;
            colliders.Query(x, z, 12, s => segs++, c => { if (c.kind != CircleKind.Tree) circ++; });
            cq++;
            if (segs != (int)Json.Num(a[2]) || circ != (int)Json.Num(a[3])) cMiss++;
        }
        Console.WriteLine($"colliders: {cq} queries, {cMiss} differ");
        Check(cMiss == 0, "same static colliders around road points");

        // --- Unity helpers ---------------------------------------------------------------------
        // Web vertex (i, j) = (2*512 + 100, 3*512 + 37) is in Unity tile (2, 1):
        // Unity rows run along -z, so tileZ*512 + row = 2560 - j = 987.
        var tile = TerrainTiles.UnityHeights(w, 2, 1, 512);
        double hx = w.Origin + (2 * 512 + 100) * w.Spacing;
        double hz = w.Origin + (3 * 512 + 37) * w.Spacing;
        double back = tile.YMin + tile.Heights[475, 100] * (tile.YMax - tile.YMin);
        Check(Math.Abs(back - w.TerrainHeight(hx, hz)) < 0.001, $"Unity terrain tile flips rows correctly ({back:F3} vs {w.TerrainHeight(hx, hz):F3})");
        var alpha = TerrainTiles.UnityAlphamaps(w, 2, 1, 512);
        double sumMax = 0;
        for (int r = 0; r < 512; r += 7)
            for (int c = 0; c < 512; c += 7)
                sumMax = Math.Max(sumMax, Math.Abs(alpha[r, c, 0] + alpha[r, c, 1] + alpha[r, c, 2] + alpha[r, c, 3] + alpha[r, c, 4] - 1));
        Check(sumMax < 1e-5, "alphamap weights sum to 1");

        // --- The whole simulation: a scripted drive from the festival spawn ----------------------
        var sref = Json.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(root, "dotnet/ref/world/sim-ref.json"))));
        var script = sref.Arr("script");
        var sim = new Simulation(w);
        var ctl = new Controls();
        var samples = sref.Arr("samples");
        int si = 0;
        double maxDev = 0, devAt = 0, devRoad = 0, devDirt = 0;
        var devs = new List<string>();
        int total = (int)Math.Round(Json.Num(Json.Arr(script[script.Count - 1])[0]) * 240);
        sw.Restart();
        for (int k = 0; k < total; k++)
        {
            double tsec = k / 240.0;
            List<object> seg = null;
            foreach (var row in script)
            {
                var r = Json.Arr(row);
                if (tsec < Json.Num(r[0])) { seg = r; break; }
            }
            seg = seg ?? Json.Arr(script[script.Count - 1]);
            ctl.throttle = Json.Num(seg[1]);
            ctl.brake = Json.Num(seg[2]);
            ctl.steer = Json.Num(seg[3]);
            ctl.analogSteer = true;
            ctl.source = InputSource.Gamepad;
            sim.Step(ctl);
            if ((k + 1) % 60 == 0 && si < samples.Count)
            {
                var a = Json.Arr(samples[si++]);
                var v = sim.Player.vehicle;
                double d = Math.Sqrt(Math.Pow(v.pos.x - Json.Num(a[1]), 2) + Math.Pow(v.pos.y - Json.Num(a[2]), 2) + Math.Pow(v.pos.z - Json.Num(a[3]), 2));
                if (d > maxDev) { maxDev = d; devAt = Json.Num(a[0]); }
                double ts = Json.Num(a[0]);
                if (ts <= 5.01) devRoad = Math.Max(devRoad, d);
                else if (ts <= 6.51) devDirt = Math.Max(devDirt, d);
                if (si % 8 == 0) devs.Add($"{Json.Num(a[0]):F0}s:{d * 100:F1}cm");
                if (Environment.GetEnvironmentVariable("SIM_DEBUG") == "1" && si >= 16 && si <= 40)
                {
                    var g0 = new GroundHit();
                    sim.Ground.Sample(v.pos.x, v.pos.z, v.pos.y, g0);
                    Console.WriteLine($"  t={Json.Num(a[0]):F2} C# ({v.pos.x:F3},{v.pos.y:F3},{v.pos.z:F3}) TS ({Json.Num(a[1]):F3},{Json.Num(a[2]):F3},{Json.Num(a[3]):F3}) d={d * 100:F2}cm v={v.speed * 3.6:F1} surf={g0.surface} onRoad={sim.OnRoad()} events={sim.Events.Count}");
                }
            }
        }
        var pvEnd = sim.Player.vehicle;
        Console.WriteLine($"island drive: {total / 240.0:F0} s simulated in {sw.ElapsedMilliseconds} ms; deviation from the TypeScript {string.Join(" ", devs)}; max {maxDev * 100:F1} cm at {devAt:F1} s; ends at {pvEnd.speed * 3.6:F1} km/h");
        // On the asphalt the two agree exactly; on the dirt shoulder the 16-bit
        // terrain (1.3 cm steps) shows; after the crash at 6.75 s chaos takes over.
        Check(devRoad < 0.001, $"on the road the C# drive matches the TypeScript ({devRoad * 1000:F2} mm over 5 s at up to 105 km/h)");
        Check(devDirt < 0.05, $"on the dirt shoulder it stays within 5 cm ({devDirt * 100:F2} cm, 16-bit terrain)");

        Console.WriteLine(failures == 0 ? "\nWORLD EQUIVALENCE: PASS" : $"\nWORLD EQUIVALENCE: FAIL ({failures})");
        return failures == 0 ? 0 : 1;
    }
}
