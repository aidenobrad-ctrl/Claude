// Equivalence tests for the C# vehicle port (unity/Assets/Halcyon/Core/Vehicles)
// against the TypeScript web version. The reference numbers come from
// tools/dev/vehicle-ref.ts (regenerate with
// `node tools/run-ts.mjs tools/dev/vehicle-ref.ts` after changing the
// TypeScript physics). Every number must match bit for bit.
//
//   cd dotnet/Halcyon.Core.Tests && dotnet run -c Release [-- --only <text>] [--ref <dir>]
//
// Exit code 0 when everything passes, 1 otherwise.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Halcyon.Core.Tests
{
    static class Program
    {
        static string FindRefDir(string explicitDir)
        {
            if (explicitDir != null) return Path.GetFullPath(explicitDir);
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (string d = start; d != null; d = Path.GetDirectoryName(d))
                {
                    string c1 = Path.Combine(d, "ref", "vehicle");
                    if (File.Exists(Path.Combine(c1, "index.json"))) return c1;
                    string c2 = Path.Combine(d, "dotnet", "ref", "vehicle");
                    if (File.Exists(Path.Combine(c2, "index.json"))) return c2;
                }
            }
            throw new DirectoryNotFoundException("dotnet/ref/vehicle not found; run `node tools/run-ts.mjs tools/dev/vehicle-ref.ts` from the repo root");
        }

        static Check Guard(string group, string name, Func<Check> f)
        {
            var sw = Stopwatch.StartNew();
            Check c;
            try
            {
                c = f();
            }
            catch (Exception e)
            {
                c = new Check(group, name);
                c.Fail($"exception: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            }
            c.Seconds += sw.Elapsed.TotalSeconds;
            return c;
        }

        static string Fmt(double x) => x == 0 ? "0" : x.ToString("0.###e+0", CultureInfo.InvariantCulture);

        static int Main(string[] args)
        {
            string only = null, refArg = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--only" && i + 1 < args.Length) only = args[++i];
                else if (args[i] == "--ref" && i + 1 < args.Length) refArg = args[++i];
            }
            var total = Stopwatch.StartNew();
            string refDir = FindRefDir(refArg);
            JsonElement carsJson = RefJson.Load(Path.Combine(refDir, "cars.json"));
            JsonElement staticJson = RefJson.Load(Path.Combine(refDir, "static.json"));
            JsonElement telJson = RefJson.Load(Path.Combine(refDir, "telemetry.json"));
            JsonElement index = RefJson.Load(Path.Combine(refDir, "index.json"));

            // The test-only variant cars come from the reference data.
            foreach (JsonElement v in carsJson.GetProperty("variants").EnumerateArray())
            {
                CarSpec s = RefJson.Build<CarSpec>(v);
                Scenarios.Variants[s.id] = s;
            }

            var checks = new List<Check>();
            bool Want(string name) => only == null || name.IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0;

            if (Want("data") || Want("cars")) checks.AddRange(StaticChecks.Cars(carsJson));
            if (Want("math") || Want("dmath")) checks.Add(Guard("math", "DMath", () => StaticChecks.DMathCheck(staticJson)));
            if (Want("math") || Want("js")) checks.Add(Guard("math", "JsMath", () => StaticChecks.JsSemantics(staticJson)));
            if (Want("tires") || Want("curve")) checks.Add(Guard("tires", "curves", () => StaticChecks.Curves(staticJson)));
            if (Want("tires") || Want("tireforce")) checks.Add(Guard("tires", "tireForce", () => StaticChecks.TireForce(staticJson)));
            if (Want("params")) checks.Add(Guard("params", "buildParams", () => StaticChecks.Params(staticJson)));
            if (Want("input") || Want("shap")) checks.Add(Guard("input", "shaping", () => StaticChecks.Shaping(staticJson)));
            if (Want("collision")) checks.Add(Guard("collision", "collideWorld fuzz", () => StaticChecks.CollisionWorld(staticJson)));
            if (Want("collision")) checks.Add(Guard("collision", "collideCars fuzz", () => StaticChecks.CollisionCars(staticJson)));

            foreach (JsonElement s in index.GetProperty("scenarios").EnumerateArray())
            {
                string name = RefJson.S(s, "name");
                if (!Want(name) && !Want("trace")) continue;
                string file = Path.Combine(refDir, RefJson.S(s, "file"));
                checks.Add(Guard("trace", name, () =>
                {
                    var c = new Check("trace", name);
                    Scenarios.Run(c, RefJson.Load(file));
                    return c;
                }));
            }

            if (Want("telemetry"))
            {
                try
                {
                    checks.AddRange(TelemetryChecks.All(telJson));
                }
                catch (Exception e)
                {
                    var c = new Check("telemetry", "telemetry");
                    c.Fail($"exception: {e}");
                    checks.Add(c);
                }
            }
            if (Want("alloc")) checks.Add(Guard("alloc", "allocations", AllocationChecks.Run));

            // --- Report ---------------------------------------------------------
            Console.WriteLine($"Halcyon.Core vehicle port vs TypeScript reference ({refDir})");
            Console.WriteLine($"Comparing every recorded number bit for bit (NaN equals NaN; -0 differs from +0).");
            Console.WriteLine();
            Console.WriteLine($"{"GROUP",-10} {"CHECK",-52} {"VALUES",9} {"EXACT",9} {"MAX ABS",10} {"MAX REL",10} {"TIME",7}  RESULT");
            long values = 0, exact = 0;
            int failed = 0;
            foreach (Check c in checks)
            {
                values += c.Values;
                exact += c.Exact;
                if (!c.Pass) failed++;
                string res = c.Pass ? "PASS" : "FAIL";
                Console.WriteLine($"{c.Group,-10} {Trunc(c.Name, 52),-52} {c.Values,9} {c.Exact,9} {Fmt(c.MaxAbs),10} {Fmt(c.MaxRel),10} {c.Seconds,6:F2}s  {res}");
                if (!c.Pass)
                {
                    foreach (string p in c.Problems) Console.WriteLine($"           ! {p}");
                    if (c.ProblemCount > c.Problems.Count) Console.WriteLine($"           ! ... {c.ProblemCount - c.Problems.Count} more");
                }
            }
            Console.WriteLine();
            if (Want("telemetry")) Console.Write("Measured with the C# port (identical to the TypeScript):\n" + TelemetryChecks.Summary(telJson) + "\n");
            Console.WriteLine($"SUMMARY: {checks.Count} checks, {values} numbers compared, {exact} bit-exact, {failed} failed, {total.Elapsed.TotalSeconds:F1} s");
            Console.WriteLine(failed == 0 && checks.Count > 0 ? "PASS" : "FAIL");
            return failed == 0 && checks.Count > 0 ? 0 : 1;
        }

        static string Trunc(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";
    }
}
