// telemetry.ts's accelTest, brakeTest and skidpadTest for every car,
// compared field by field with the TypeScript results.
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Halcyon.Core.Tests
{
    static class TelemetryChecks
    {
        static object Run(string test, VehicleParams p)
        {
            switch (test)
            {
                case "accel": return Telemetry.AccelTest(p);
                case "accel-notcs-6s": return Telemetry.AccelTest(p, maxTime: 6, aids: new PartialAids { tcs = false });
                case "brake-abs-100": return Telemetry.BrakeTest(p, 100, aids: new PartialAids { abs = true });
                case "brake-noabs-100": return Telemetry.BrakeTest(p, 100, aids: new PartialAids { abs = false });
                case "skidpad-40": return Telemetry.SkidpadTest(p, 40);
                case "skidpad-40-grass-90s": return Telemetry.SkidpadTest(p, 40, surface: Surface.Grass, maxTime: 90);
                case "accel-sand-30s": return Telemetry.AccelTest(p, maxTime: 30, surface: Surface.Sand);
                case "brake-snow-60": return Telemetry.BrakeTest(p, 60, surface: Surface.Snow);
                case "skidpad-3-too-tight": return Telemetry.SkidpadTest(p, 3, maxTime: 10);
                case "skidpad-40-ice-20s": return Telemetry.SkidpadTest(p, 40, surface: Surface.Ice, maxTime: 20);
                case "skidpad-25-assisted-esc": return Telemetry.SkidpadTest(p, 25, aids: new PartialAids { steering = SteeringAssist.Assisted, esc = true }, maxTime: 40);
                case "accel-wet": return Telemetry.AccelTest(p, maxTime: 30, wetness: 1);
                case "brake-wet-100": return Telemetry.BrakeTest(p, 100, wetness: 1);
                case "skidpad-40-damp": return Telemetry.SkidpadTest(p, 40, wetness: 0.5);
            }
            return null;
        }

        /// <summary>One check per car, covering all of that car's tests.</summary>
        public static IEnumerable<Check> All(JsonElement tel)
        {
            var byCar = new Dictionary<string, Check>();
            var order = new List<Check>();
            foreach (JsonElement r in tel.GetProperty("results").EnumerateArray())
            {
                string car = RefJson.S(r, "car");
                string test = RefJson.S(r, "test");
                if (!byCar.TryGetValue(car, out Check c))
                {
                    c = new Check("telemetry", car);
                    byCar[car] = c;
                    order.Add(c);
                }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                object result = Run(test, VehicleParams.Build(Scenarios.CarSpec(car)));
                c.Seconds += sw.Elapsed.TotalSeconds;
                if (result == null) c.Fail($"{test}: no C# equivalent");
                else RefJson.Tree(c, test, r.GetProperty("r"), result, strict: true);
            }
            return order;
        }

        /// <summary>The 0-100, braking and skidpad figures, for the report.</summary>
        public static string Summary(JsonElement tel)
        {
            var sb = new System.Text.StringBuilder();
            foreach (JsonElement r in tel.GetProperty("results").EnumerateArray())
            {
                string test = RefJson.S(r, "test");
                JsonElement x = r.GetProperty("r");
                string v = test switch
                {
                    "accel" => $"0-100 {RefJson.D(x, "t100"):F2} s, top {RefJson.D(x, "topKmh"):F1} km/h",
                    "brake-abs-100" => $"100-0 {RefJson.D(x, "distance"):F2} m (ABS)",
                    "skidpad-40" => $"skidpad {RefJson.D(x, "g"):F3} g",
                    _ => null,
                };
                if (v != null) sb.Append($"  {RefJson.S(r, "car"),-20} {v}\n");
            }
            return sb.ToString();
        }
    }
}
