// The island's clock and weather. Both advance on the simulation's fixed
// clock, so the road wetness the physics reads is deterministic for a seed.
//   - WorldClock: a 24-minute day by default, and the sun and moon of a
//     summer festival at 35 degrees north (sunrise about 5:15, sunset about
//     18:45).
//   - Weather: clear, cloudy, rain, storm and fog, chosen by a seeded Markov
//     chain (fog likes early mornings). Clouds roll in before the rain and
//     the rain stops before they clear; the roads soak in rain and dry in
//     sun and wind; storms throw lightning.
// Core coordinates: +X east, +Y up, -Z north. No UnityEngine.
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    public sealed class WorldClock
    {
        public const double Latitude = 35 * Math.PI / 180;
        /// <summary>The sun's declination: high summer, for long festival evenings.</summary>
        public const double Declination = 15 * Math.PI / 180;

        /// <summary>Time of day in hours, [0, 24).</summary>
        public double Hours = 10;
        public int Day;
        /// <summary>Real seconds in a full day: 24 minutes, one hour a minute.</summary>
        public double DayLength = 24 * 60;
        /// <summary>How fast time passes: 0 stops the clock.</summary>
        public double Rate = 1;

        public void Advance(double dt)
        {
            Hours += dt * 24 / DayLength * Rate;
            while (Hours >= 24)
            {
                Hours -= 24;
                Day++;
            }
            while (Hours < 0)
            {
                Hours += 24;
                Day--;
            }
        }

        /// <summary>Sets the time of day (wrapped into [0, 24)), keeping the day.</summary>
        public void Set(double hours)
        {
            Hours = hours % 24;
            if (Hours < 0) Hours += 24;
        }

        /// <summary>Elevation and azimuth (from north, clockwise) of a body on the celestial equator offset by declination, at an hour angle.</summary>
        static void Body(double hourAngle, double dec, out double elevation, out double azimuth)
        {
            double sinAlt = Math.Sin(Latitude) * Math.Sin(dec) + Math.Cos(Latitude) * Math.Cos(dec) * Math.Cos(hourAngle);
            elevation = Math.Asin(Math.Max(-1, Math.Min(1, sinAlt)));
            azimuth = Math.Atan2(-Math.Cos(dec) * Math.Sin(hourAngle), Math.Sin(dec) * Math.Cos(Latitude) - Math.Cos(dec) * Math.Sin(Latitude) * Math.Cos(hourAngle));
            if (azimuth < 0) azimuth += 2 * Math.PI;
        }

        static double HourAngle(double hours) => (hours - 12) / 12 * Math.PI;

        /// <summary>The sun's elevation above the horizon, radians (negative at night).</summary>
        public double SunElevation
        {
            get
            {
                Body(HourAngle(Hours), Declination, out double e, out _);
                return e;
            }
        }

        /// <summary>The sun's azimuth from north toward east, radians.</summary>
        public double SunAzimuth
        {
            get
            {
                Body(HourAngle(Hours), Declination, out _, out double a);
                return a;
            }
        }

        /// <summary>Unit vector toward the sun in Core axes.</summary>
        public void SunDirection(out double x, out double y, out double z) => Direction(SunElevation, SunAzimuth, out x, out y, out z);

        /// <summary>A full moon: opposite the sun in the sky.</summary>
        public void MoonDirection(out double x, out double y, out double z)
        {
            Body(HourAngle(Hours) + Math.PI, -Declination, out double e, out double a);
            Direction(e, a, out x, out y, out z);
        }

        static void Direction(double elevation, double azimuth, out double x, out double y, out double z)
        {
            double c = Math.Cos(elevation);
            x = Math.Sin(azimuth) * c;
            y = Math.Sin(elevation);
            z = -Math.Cos(azimuth) * c;
        }

        /// <summary>0 at night, 1 in daylight, easing through twilight (sun 6 degrees below to 8 above the horizon).</summary>
        public double Daylight => M.Smoothstep(-6 * Math.PI / 180, 8 * Math.PI / 180, SunElevation);

        /// <summary>True from the end of dusk to the start of dawn: headlights on.</summary>
        public bool IsNight => SunElevation < -2 * Math.PI / 180;

        /// <summary>The hour the sun's centre crosses the horizon in the morning (the evening one is 24 minus it).</summary>
        public static double SunriseHour => 12 - Math.Acos(-Math.Tan(Latitude) * Math.Tan(Declination)) * 12 / Math.PI;
        public static double SunsetHour => 24 - SunriseHour;

        /// <summary>"14:05".</summary>
        public string Label
        {
            get
            {
                int m = (int)Math.Floor(Hours * 60 + 1e-6) % 1440; // 1e-6: 0.5 h stays 00:30 after float sums
                return $"{m / 60:00}:{m % 60:00}";
            }
        }
    }

    public enum Sky { Clear, Cloudy, Rain, Storm, Fog }

    /// <summary>A lightning strike: where (Core axes), how bright (0 to 1), and the simulation time.</summary>
    public struct Strike
    {
        public double x, z, brightness, time;
    }

    public sealed class Weather
    {
        /// <summary>What each sky looks like once it has settled.</summary>
        public struct Look
        {
            public double Cloud, Rain, Fog, Wind;
            public double MinTime, MaxTime;
        }

        public static readonly Look[] Looks =
        {
            new Look { Cloud = 0.1, Rain = 0, Fog = 0.02, Wind = 2.5, MinTime = 300, MaxTime = 660 }, // Clear
            new Look { Cloud = 0.72, Rain = 0, Fog = 0.06, Wind = 4.5, MinTime = 150, MaxTime = 360 }, // Cloudy
            new Look { Cloud = 0.92, Rain = 0.55, Fog = 0.16, Wind = 5.5, MinTime = 150, MaxTime = 330 }, // Rain
            new Look { Cloud = 1, Rain = 1, Fog = 0.24, Wind = 12, MinTime = 100, MaxTime = 220 }, // Storm
            new Look { Cloud = 0.45, Rain = 0, Fog = 0.85, Wind = 0.8, MinTime = 150, MaxTime = 320 }, // Fog
        };

        /// <summary>Markov chain: the chance of each next sky from each sky (rows: from; columns: Clear, Cloudy, Rain, Storm, Fog).</summary>
        static readonly double[,] Next =
        {
            { 0, 0.72, 0, 0, 0.28 },
            { 0.45, 0, 0.33, 0.1, 0.12 },
            { 0.15, 0.5, 0, 0.25, 0.1 },
            { 0, 0.4, 0.6, 0, 0 },
            { 0.5, 0.4, 0.1, 0, 0 },
        };

        /// <summary>The sky the weather is heading for (its look eases in).</summary>
        public Sky State { get; private set; } = Sky.Clear;
        /// <summary>Real seconds until the next change of sky.</summary>
        public double TimeLeft { get; private set; }
        /// <summary>Cloud cover, rain and fog intensity, 0 to 1; wind speed, m/s.</summary>
        public double Cloud, Rain, Fog, Wind;
        /// <summary>Where the wind blows toward, radians from north toward east.</summary>
        public double WindDir;
        /// <summary>How wet paved roads are, 0 dry to 1 soaked: the physics reads this.</summary>
        public double Wetness;
        /// <summary>Standing water for the look of the roads (fills in heavy rain, lingers): presentation only.</summary>
        public double Puddles;
        /// <summary>Forced by a menu or an event: the sky never changes on its own.</summary>
        public bool Locked { get; private set; }
        /// <summary>Held during a race: no change of sky, and the road wetness stays as it is.</summary>
        public bool Hold;
        /// <summary>Lightning since the last drain (bounded).</summary>
        public readonly List<Strike> Strikes = new List<Strike>();

        readonly Rng rng;
        double nextStrike;
        double time;

        public Weather(uint seed = 0x5eed)
        {
            rng = new Rng(seed);
            TimeLeft = rng.Range(Looks[0].MinTime, Looks[0].MaxTime);
            Snap();
        }

        /// <summary>Settles the look at the current sky at once (no easing), and the roads to match.</summary>
        public void Snap()
        {
            Look l = Looks[(int)State];
            Cloud = l.Cloud;
            Rain = l.Rain;
            Fog = l.Fog;
            Wind = l.Wind;
            Wetness = l.Rain > 0 ? Math.Min(1, 0.55 + l.Rain * 0.45) : 0;
            Puddles = l.Rain > 0.5 ? l.Rain * 0.8 : 0;
        }

        /// <summary>Keeps this sky until Release (a menu choice, or an event's conditions).</summary>
        public void Force(Sky s, bool snap = false)
        {
            State = s;
            Locked = true;
            if (snap) Snap();
        }

        /// <summary>Lets the sky change on its own again.</summary>
        public void Release()
        {
            if (!Locked) return;
            Locked = false;
            Look l = Looks[(int)State];
            TimeLeft = rng.Range(l.MinTime, l.MaxTime);
        }

        /// <summary>Advances the weather by dt real seconds. lightX/lightZ: where strikes centre (the player).</summary>
        public void Step(double dt, WorldClock clock, double lightX = 0, double lightZ = 0)
        {
            time += dt;
            if (!Locked && !Hold)
            {
                TimeLeft -= dt;
                if (TimeLeft <= 0) Change(clock.Hours);
            }
            Look l = Looks[(int)State];
            Cloud += (l.Cloud - Cloud) * Ease(dt, 50);
            // Rain needs heavy cloud: it starts after the clouds roll in and stops before they clear.
            double fromCloud = M.Smoothstep(0.6, 0.88, Cloud);
            double rainTarget = l.Rain * fromCloud;
            Rain += (rainTarget - Rain) * Ease(dt, rainTarget > Rain ? 35 : 20);
            Rain = Math.Min(Rain, fromCloud); // never rain from a thinning sky
            Fog += (l.Fog - Fog) * Ease(dt, 60);
            Wind += (l.Wind - Wind) * Ease(dt, 40);
            WindDir += rng.Gauss(0, 0.02) * Math.Sqrt(dt);

            if (!Hold)
            {
                // Roads soak in rain (a storm wets them in about a minute) and dry in sun and wind
                // (about five minutes on a sunny afternoon, three times as long at night).
                double daylight = clock.Daylight;
                if (Rain > 0.02) Wetness += (1 - Wetness) * Ease(dt, 30 / Rain);
                double dry = (0.25 + 0.75 * daylight * (1 - 0.6 * Cloud)) * (1 + Wind / 8) / 300 * (1 - Math.Min(1, Rain * 4));
                Wetness = Math.Max(0, Math.Min(1, Wetness - dry * dt));
                double fill = M.Smoothstep(0.35, 0.9, Rain);
                if (fill > Puddles) Puddles += (fill - Puddles) * Ease(dt, 90);
                else Puddles = Math.Max(fill, Puddles - dry * 0.6 * dt);
            }

            // Lightning: a strike every 8 s or so in a full storm, 0.4 to 4 km from the player.
            if (State == Sky.Storm && Rain > 0.7)
            {
                nextStrike -= dt;
                if (nextStrike <= 0)
                {
                    double a = rng.Range(0, 2 * Math.PI), d = rng.Range(400, 4000);
                    if (Strikes.Count >= 16) Strikes.RemoveAt(0);
                    Strikes.Add(new Strike { x = lightX + Math.Sin(a) * d, z = lightZ - Math.Cos(a) * d, brightness = rng.Range(0.5, 1), time = time });
                    nextStrike = -Math.Log(1 - rng.Next()) * 8;
                }
            }
            else nextStrike = rng.Range(1, 6);
        }

        static double Ease(double dt, double tau) => 1 - Math.Exp(-dt / tau);

        void Change(double hour)
        {
            int from = (int)State;
            double[] w = new double[5];
            double sum = 0;
            for (int i = 0; i < 5; i++)
            {
                w[i] = Next[from, i];
                // Fog lies in the valleys at dawn and burns off by midday.
                if (i == (int)Sky.Fog) w[i] *= hour >= 3 && hour < 9 ? 3 : hour >= 11 && hour < 19 ? 0.3 : 1;
                sum += w[i];
            }
            double r = rng.Next() * sum;
            int to = from;
            for (int i = 0; i < 5; i++)
            {
                r -= w[i];
                if (w[i] > 0 && r < 0)
                {
                    to = i;
                    break;
                }
            }
            State = (Sky)to;
            Look l = Looks[to];
            TimeLeft = rng.Range(l.MinTime, l.MaxTime);
        }

        /// <summary>The words for the HUD.</summary>
        public static string Name(Sky s)
        {
            switch (s)
            {
                case Sky.Cloudy: return "Cloudy";
                case Sky.Rain: return "Rain";
                case Sky.Storm: return "Storm";
                case Sky.Fog: return "Fog";
                default: return "Clear";
            }
        }
    }
}
