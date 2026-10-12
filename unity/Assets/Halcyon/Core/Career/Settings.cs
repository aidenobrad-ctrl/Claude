// The player's settings: graphics, units, the driving assists, the racing
// line, the HUD and accessibility (its scale, colour-blind palettes, camera
// shake, motion blur, lightning flashes, key hints), the camera's field of
// view and the sound levels. Saved as JSON beside the career, not in it (a
// shared career code should not carry someone's settings). Values read
// back from a file are clamped to their ranges. No UnityEngine.
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    public enum GraphicsPreset { Low, Medium, High, Ultra }

    /// <summary>The racing line on the road in races: none, only where to brake, or all of it.</summary>
    public enum LineAssist { Off, Braking, Full }

    /// <summary>The radio: off or one of the three stations (Core/Audio's RadioStation).</summary>
    public enum StationChoice { Off, Pulse, Coast, Redline }

    /// <summary>The HUD's palette: the usual greens and reds, or one that reads with a colour-vision deficiency.</summary>
    public enum ColourVision { Normal, Deuteranopia, Protanopia, Tritanopia }

    public sealed class Settings
    {
        public const int Version = 1;

        public GraphicsPreset Graphics = GraphicsPreset.High;
        /// <summary>Miles, mph and feet instead of kilometres, km/h and metres.</summary>
        public bool Imperial;

        // Driving assists (the player's car).
        public bool Abs = true, Tcs = true, Esc = true;
        public SteeringAssist Steering = SteeringAssist.Standard;
        public GearboxMode Gearbox = GearboxMode.Auto;
        public LineAssist Line = LineAssist.Braking;

        // HUD and accessibility.
        /// <summary>The driving HUD's size, 0.75 to 1.5.</summary>
        public double HudScale = 1;
        public ColourVision Colours = ColourVision.Normal;
        /// <summary>Camera shake from bumps, speed and impacts, 0 (none) to 1.</summary>
        public double Shake = 1;
        public bool MotionBlur = true;
        /// <summary>Lightning lights the sky a quarter as brightly.</summary>
        public bool ReduceFlashes;
        public bool Minimap = true;
        public bool KeyHints = true;
        /// <summary>The chase camera's field of view at rest, degrees (50 to 80).</summary>
        public double Fov = 60;

        // Sound, 0 to 1, and the radio.
        public double Master = 0.8, Engine = 1, Effects = 1, Ambience = 1, Music = 0.55;
        public StationChoice Station = StationChoice.Pulse;

        /// <summary>The assists as the physics takes them.</summary>
        public Aids Aids() => new Aids { abs = Abs, tcs = Tcs, esc = Esc, steering = Steering, gearbox = Gearbox };

        public Settings Clone() => (Settings)MemberwiseClone();

        static double Clamp(double v, double lo, double hi) => double.IsNaN(v) ? lo : Math.Max(lo, Math.Min(hi, v));

        static T Enum<T>(Dictionary<string, object> d, string key, T fallback) where T : struct
        {
            if (!d.TryGetValue(key, out var v) || !(v is string s)) return fallback;
            return System.Enum.TryParse(s, out T r) && System.Enum.IsDefined(typeof(T), r) ? r : fallback;
        }

        static bool Bool(Dictionary<string, object> d, string key, bool fallback) => d.TryGetValue(key, out var v) && v is bool b ? b : fallback;

        static double Num(Dictionary<string, object> d, string key, double fallback, double lo, double hi) =>
            d.TryGetValue(key, out var v) && v is double x ? Clamp(x, lo, hi) : fallback;

        public Dictionary<string, object> ToDict() => new Dictionary<string, object>
        {
            ["version"] = Version,
            ["graphics"] = Graphics.ToString(),
            ["imperial"] = Imperial,
            ["abs"] = Abs,
            ["tcs"] = Tcs,
            ["esc"] = Esc,
            ["steering"] = Steering.ToString(),
            ["gearbox"] = Gearbox.ToString(),
            ["line"] = Line.ToString(),
            ["hudScale"] = HudScale,
            ["colours"] = Colours.ToString(),
            ["shake"] = Shake,
            ["motionBlur"] = MotionBlur,
            ["reduceFlashes"] = ReduceFlashes,
            ["minimap"] = Minimap,
            ["keyHints"] = KeyHints,
            ["fov"] = Fov,
            ["master"] = Master,
            ["engine"] = Engine,
            ["effects"] = Effects,
            ["ambience"] = Ambience,
            ["music"] = Music,
            ["station"] = Station.ToString(),
        };

        public string ToJson() => JsonWriter.Write(ToDict());

        /// <summary>Settings from JSON: anything missing or out of range takes its default (or is clamped).</summary>
        public static Settings FromJson(string json)
        {
            var s = new Settings();
            Dictionary<string, object> d;
            try
            {
                d = MiniJson.Parse(json) as Dictionary<string, object>;
            }
            catch (Exception)
            {
                return s;
            }
            if (d == null) return s;
            s.Graphics = Enum(d, "graphics", s.Graphics);
            s.Imperial = Bool(d, "imperial", s.Imperial);
            s.Abs = Bool(d, "abs", s.Abs);
            s.Tcs = Bool(d, "tcs", s.Tcs);
            s.Esc = Bool(d, "esc", s.Esc);
            s.Steering = Enum(d, "steering", s.Steering);
            s.Gearbox = Enum(d, "gearbox", s.Gearbox);
            s.Line = Enum(d, "line", s.Line);
            s.HudScale = Num(d, "hudScale", s.HudScale, 0.75, 1.5);
            s.Colours = Enum(d, "colours", s.Colours);
            s.Shake = Num(d, "shake", s.Shake, 0, 1);
            s.MotionBlur = Bool(d, "motionBlur", s.MotionBlur);
            s.ReduceFlashes = Bool(d, "reduceFlashes", s.ReduceFlashes);
            s.Minimap = Bool(d, "minimap", s.Minimap);
            s.KeyHints = Bool(d, "keyHints", s.KeyHints);
            s.Fov = Num(d, "fov", s.Fov, 50, 80);
            s.Master = Num(d, "master", s.Master, 0, 1);
            s.Engine = Num(d, "engine", s.Engine, 0, 1);
            s.Effects = Num(d, "effects", s.Effects, 0, 1);
            s.Ambience = Num(d, "ambience", s.Ambience, 0, 1);
            s.Music = Num(d, "music", s.Music, 0, 1);
            s.Station = Enum(d, "station", s.Station);
            return s;
        }
    }

    /// <summary>Speeds and distances for the screen, metric or imperial (Settings.Imperial).</summary>
    public static class Units
    {
        public static bool Imperial;
        const double Mile = 1609.344, Foot = 0.3048;

        /// <summary>A speed in m/s as "123 km/h" or "76 mph".</summary>
        public static string Speed(double mps) => double.IsNaN(mps) ? "--" : Imperial ? $"{mps * 3600 / Mile:0} mph" : $"{mps * 3.6:0} km/h";

        /// <summary>The number alone, for big speedometer digits.</summary>
        public static int SpeedValue(double mps) => (int)Math.Round(Math.Abs(mps) * (Imperial ? 3600 / Mile : 3.6));

        public static string SpeedUnit => Imperial ? "mph" : "km/h";

        /// <summary>A short distance: "350 m" or "1150 ft".</summary>
        public static string Short(double m) => Imperial ? $"{m / Foot:0} ft" : $"{m:0} m";

        /// <summary>A distance on the road: "350 m" / "4.2 km", or "0.2 mi" / "2.6 mi".</summary>
        public static string Long(double m)
        {
            if (Imperial) return m < 0.1 * Mile ? $"{m / Foot:0} ft" : $"{m / Mile:0.0} mi";
            return m < 1000 ? $"{m:0} m" : $"{m / 1000:0.0} km";
        }
    }
}
