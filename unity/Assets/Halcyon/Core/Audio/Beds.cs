// The rest of the car's sound, synthesized once; a port of the web version's
// src/audio/beds.ts. Looping beds (tire squeal, road roar, gravel crunch,
// grass swish, snow crunch, spray, wind, intake, turbo) that the player
// pitches and fades with speed and slip, and one-shots (impacts, blow-off,
// overrun crackle, gear shift, scrape, UI blips).
using System;

namespace Halcyon.Core.Audio
{
    public enum Bed { Squeal, Road, Gravel, Grass, Snow, Spray, Wind, Intake, Turbo }
    public enum Shot { ImpactLight, ImpactMedium, ImpactHeavy, Blowoff, Crackle, Shift, Scrape }
    public enum UiSound { Move, Select, Back, Error, Count, Go, Reward, Photo, Checkpoint }

    public static class Beds
    {
        const double Loop = 2.0, Fade = 0.2;

        static double JsRound(double x) => Math.Floor(x + 0.5);

        static float[] BedBuffer(int sr, int seed, Action<AudioRng, float[]> gen)
        {
            var rng = new AudioRng(seed);
            var raw = new float[(int)JsRound((Loop + Fade) * sr)];
            gen(rng, raw);
            return Dsp.CrossfadeLoop(raw, (int)JsRound(Fade * sr));
        }

        /// <summary>The TypeScript's ShotId string length, which seeds the one-shots there.</summary>
        static int ShotNameLength(Shot s)
        {
            switch (s)
            {
                case Shot.ImpactLight: return "impactLight".Length;
                case Shot.ImpactMedium: return "impactMedium".Length;
                case Shot.ImpactHeavy: return "impactHeavy".Length;
                case Shot.Blowoff: return "blowoff".Length;
                case Shot.Crackle: return "crackle".Length;
                case Shot.Shift: return "shift".Length;
                default: return "scrape".Length;
            }
        }

        /// <summary>A looping bed: unit-ish level, seamless.</summary>
        public static float[] Render(Bed id, int sr)
        {
            switch (id)
            {
                case Bed.Squeal:
                    // Tire squeal: a few narrow, wandering bands around a kilohertz over a little hiss.
                    return Dsp.Normalize(BedBuffer(sr, 11, (rng, o) =>
                    {
                        double[,] spec = { { 760, 30, 1 }, { 1130, 34, 0.7 }, { 1520, 40, 0.45 }, { 2290, 30, 0.2 } };
                        int nb = spec.GetLength(0);
                        var f = new double[nb];
                        var g = new double[nb];
                        var bq = new Biquad[nb];
                        var lfo = new double[nb];
                        for (int k = 0; k < nb; k++)
                        {
                            f[k] = spec[k, 0];
                            g[k] = spec[k, 2];
                            bq[k] = Biquad.Bandpass(sr, spec[k, 0], spec[k, 1]);
                            lfo[k] = rng.Next() * 6;
                        }
                        var hiss = Biquad.Bandpass(sr, 3000, 0.7);
                        for (int i = 0; i < o.Length; i++)
                        {
                            double t = (double)i / sr;
                            if (i % 64 == 0)
                                for (int k = 0; k < nb; k++) bq[k].SetBandpass(sr, f[k] * (1 + 0.025 * Math.Sin(t * 7.3 + lfo[k]) + 0.012 * Math.Sin(t * 23 + lfo[k] * 2)), 32);
                            double w = rng.Signed();
                            double s = 0;
                            for (int k = 0; k < nb; k++) s += g[k] * bq[k].Process(w);
                            o[i] = (float)(s * 6 + 0.08 * hiss.Process(rng.Signed()) * (0.8 + 0.2 * Math.Sin(t * 13)));
                        }
                    }), 0.25);
                case Bed.Road:
                    // Tire roar on asphalt: brown noise with a hump around 120 Hz and some tread whine.
                    return Dsp.Normalize(BedBuffer(sr, 12, (rng, o) =>
                    {
                        var br = new BrownNoise(rng);
                        var hump = Biquad.Peaking(sr, 120, 1, 9);
                        var lp = Biquad.Lowpass(sr, 900, 0.7);
                        var tread = Biquad.Bandpass(sr, 520, 6);
                        for (int i = 0; i < o.Length; i++) o[i] = (float)(lp.Process(hump.Process(br.Next())) + 0.15 * tread.Process(rng.Signed()));
                    }), 0.25);
                case Bed.Gravel:
                    // Stones popping under the tread: dense random grains through a crunchy band.
                    return Dsp.Normalize(BedBuffer(sr, 13, (rng, o) =>
                    {
                        var bp = Biquad.Bandpass(sr, 1800, 0.6);
                        var lo = Biquad.Lowpass(sr, 420, 0.7);
                        double env = 0;
                        for (int i = 0; i < o.Length; i++)
                        {
                            if (rng.Next() < 0.004) env = 0.4 + rng.Next() * 0.9;
                            env *= 0.993;
                            double n = rng.Signed();
                            o[i] = (float)(bp.Process(n * env) * 2.2 + lo.Process(n) * 0.25);
                        }
                    }), 0.25);
                case Bed.Grass:
                    return Dsp.Normalize(BedBuffer(sr, 14, (rng, o) =>
                    {
                        var pink = new PinkNoise(rng);
                        var bp = Biquad.Bandpass(sr, 1500, 0.5);
                        for (int i = 0; i < o.Length; i++)
                        {
                            double t = (double)i / sr;
                            o[i] = (float)(bp.Process(pink.Next()) * (0.7 + 0.3 * Math.Sin(t * 9 + Math.Sin(t * 2.3) * 2)));
                        }
                    }), 0.25);
                case Bed.Snow:
                    return Dsp.Normalize(BedBuffer(sr, 15, (rng, o) =>
                    {
                        var lp = Biquad.Lowpass(sr, 1200, 0.9);
                        double env = 0;
                        for (int i = 0; i < o.Length; i++)
                        {
                            if (rng.Next() < 0.002) env = 0.6 + rng.Next() * 0.6;
                            env *= 0.997;
                            o[i] = (float)lp.Process(rng.Signed() * (0.3 + env));
                        }
                    }), 0.25);
                case Bed.Spray:
                    return Dsp.Normalize(BedBuffer(sr, 16, (rng, o) =>
                    {
                        var hp = Biquad.Highpass(sr, 2500, 0.7);
                        var pink = new PinkNoise(rng);
                        for (int i = 0; i < o.Length; i++) o[i] = (float)(hp.Process(pink.Next()) * (0.85 + 0.15 * Math.Sin(((double)i / sr) * 17)));
                    }), 0.25);
                case Bed.Wind:
                    // Wind over the body: pink noise, low-passed, slowly gusting.
                    return Dsp.Normalize(BedBuffer(sr, 17, (rng, o) =>
                    {
                        var pink = new PinkNoise(rng);
                        var lp = new OnePole(sr, 700);
                        var bp = Biquad.Bandpass(sr, 380, 0.9);
                        for (int i = 0; i < o.Length; i++)
                        {
                            double t = (double)i / sr;
                            double gust = 0.75 + 0.25 * Math.Sin(t * 1.7) * Math.Sin(t * 0.9 + 1);
                            double n = pink.Next();
                            o[i] = (float)((lp.Process(n) * 0.8 + bp.Process(n) * 0.6) * gust);
                        }
                    }), 0.25);
                case Bed.Intake:
                    // Air rushing into the engine: band-limited noise the player tunes with rpm.
                    return Dsp.Normalize(BedBuffer(sr, 18, (rng, o) =>
                    {
                        var bp = Biquad.Bandpass(sr, 700, 0.9);
                        var pink = new PinkNoise(rng);
                        for (int i = 0; i < o.Length; i++) o[i] = (float)bp.Process(pink.Next());
                    }), 0.25);
                default:
                {
                    // A turbo's whistle: a sine near 3.2 kHz with a little noise; pitched with boost.
                    double f = 3200;
                    int cycles = (int)JsRound(Loop * f);
                    int n = (int)JsRound((cycles * (double)sr) / f);
                    var o = new float[n];
                    var rng = new AudioRng(19);
                    var bp = Biquad.Bandpass(sr, 4200, 2);
                    for (int i = 0; i < n; i++)
                        o[i] = (float)(Math.Sin((2 * Math.PI * cycles * i) / n) * 0.6 + 0.4 * Math.Sin((4 * Math.PI * cycles * i) / n) * 0.25 + 0.3 * bp.Process(rng.Signed()));
                    return Dsp.Normalize(o, 0.25);
                }
            }
        }

        /// <summary>A one-shot effect.</summary>
        public static float[] Render(Shot id, int sr, int variant = 0)
        {
            var rng = new AudioRng(100 + variant * 31 + ShotNameLength(id) * 7);
            float[] Len(double s) => new float[(int)JsRound(s * sr)];
            switch (id)
            {
                case Shot.ImpactLight:
                case Shot.ImpactMedium:
                case Shot.ImpactHeavy:
                {
                    double heavy = id == Shot.ImpactHeavy ? 1 : id == Shot.ImpactMedium ? 0.55 : 0.2;
                    var o = Len(0.35 + 0.9 * heavy);
                    double thump = 55 + 35 * (1 - heavy) + variant * 6;
                    var metal = new[] { Biquad.Bandpass(sr, 900 + variant * 140, 6), Biquad.Bandpass(sr, 1730 + variant * 90, 9), Biquad.Bandpass(sr, 2950, 12) };
                    var crunch = Biquad.Bandpass(sr, 2400, 0.6);
                    double grit = 0;
                    for (int i = 0; i < o.Length; i++)
                    {
                        double t = (double)i / sr;
                        double body = Math.Sin(2 * Math.PI * thump * t * (1 + 0.6 * Math.Exp(-t / 0.02))) * Math.Exp(-t / (0.06 + 0.08 * heavy));
                        if (rng.Next() < 0.02 * heavy) grit = 1;
                        grit *= 0.996;
                        double n = rng.Signed();
                        double ring = (metal[0].Process(n) + metal[1].Process(n) * 0.7 + metal[2].Process(n) * 0.4) * Math.Exp(-t / (0.12 + 0.35 * heavy)) * 3;
                        double cr = crunch.Process(n) * (Math.Exp(-t / (0.05 + 0.15 * heavy)) + grit * 0.5 * heavy);
                        o[i] = (float)Dsp.SoftClip(body * 1.4 + ring * (0.4 + heavy) + cr * (0.5 + heavy));
                    }
                    return Dsp.Normalize(Dsp.FadeEdges(o, sr, 0.5, 30), 0.22, 0.95);
                }
                case Shot.Blowoff:
                {
                    // The blow-off valve on a lift: a falling chirp then a hiss.
                    var o = Len(0.45);
                    var hp = Biquad.Highpass(sr, 1800, 0.7);
                    var bp = Biquad.Bandpass(sr, 3500, 1.5);
                    for (int i = 0; i < o.Length; i++)
                    {
                        double t = (double)i / sr;
                        bp.SetBandpass(sr, 3800 - 1800 * Math.Min(1, t / 0.3), 1.5);
                        double n = rng.Signed();
                        o[i] = (float)((bp.Process(n) * 1.5 + hp.Process(n) * 0.3) * Math.Min(1, t / 0.01) * Math.Exp(-t / 0.12));
                    }
                    return Dsp.Normalize(Dsp.FadeEdges(o, sr), 0.18);
                }
                case Shot.Crackle:
                {
                    // Overrun: two or three sharp pops in the exhaust.
                    var o = Len(0.3);
                    var bp = Biquad.Bandpass(sr, 600 + variant * 120, 0.9);
                    int pops = 2 + rng.Int(2);
                    for (int k = 0; k < pops; k++)
                    {
                        int at = (int)JsRound((0.02 + rng.Next() * 0.2) * sr);
                        for (int j = 0; j < sr * 0.03 && at + j < o.Length; j++)
                            o[at + j] = (float)(o[at + j] + rng.Signed() * Math.Exp(-j / (sr * 0.006)) * (0.6 + rng.Next() * 0.4));
                    }
                    for (int i = 0; i < o.Length; i++) o[i] = (float)Dsp.SoftClip(bp.Process(o[i]) * 3 + o[i] * 0.2);
                    return Dsp.Normalize(Dsp.FadeEdges(o, sr), 0.2);
                }
                case Shot.Shift:
                {
                    var o = Len(0.12);
                    var bp = Biquad.Bandpass(sr, 420, 3);
                    for (int i = 0; i < o.Length; i++)
                    {
                        double t = (double)i / sr;
                        o[i] = (float)(bp.Process(rng.Signed()) * Math.Exp(-t / 0.025) + Math.Sin(2 * Math.PI * 140 * t) * Math.Exp(-t / 0.02) * 0.4);
                    }
                    return Dsp.Normalize(Dsp.FadeEdges(o, sr), 0.15);
                }
                default:
                {
                    // Bodywork dragging along a wall or rail: a gritty band, looped by the player.
                    var o = Len(1.2);
                    var bp = Biquad.Bandpass(sr, 1400, 1.2);
                    var bp2 = Biquad.Bandpass(sr, 3100, 4);
                    double env = 0.5;
                    for (int i = 0; i < o.Length; i++)
                    {
                        if (rng.Next() < 0.003) env = 0.4 + rng.Next() * 0.6;
                        double n = rng.Signed();
                        o[i] = (float)((bp.Process(n) + bp2.Process(n) * 0.6) * env);
                    }
                    return Dsp.Normalize(Dsp.CrossfadeLoop(o, (int)JsRound(0.15 * sr)), 0.2);
                }
            }
        }

        /// <summary>Menu and HUD sounds: short tuned blips.</summary>
        public static float[] Render(UiSound id, int sr)
        {
            var o = new float[(int)JsRound(0.7 * sr)];
            void Tone(double at, double f, double dur, double gain, double bright = 0.3)
            {
                int a = (int)JsRound(at * sr);
                int n = (int)JsRound(dur * sr);
                for (int i = 0; i < n && a + i < o.Length; i++)
                {
                    double t = (double)i / sr;
                    double env = Math.Min(1, t / 0.003) * Math.Exp(-t / (dur * 0.35));
                    o[a + i] = (float)(o[a + i] + gain * env * (Math.Sin(2 * Math.PI * f * t) + bright * Math.Sin(4 * Math.PI * f * t) * Math.Exp(-t / 0.03)));
                }
            }
            switch (id)
            {
                case UiSound.Move: Tone(0, 1760, 0.05, 0.5, 0.2); break;
                case UiSound.Select: Tone(0, 1320, 0.08, 0.5); Tone(0.06, 1980, 0.12, 0.5); break;
                case UiSound.Back: Tone(0, 1320, 0.08, 0.5); Tone(0.06, 990, 0.12, 0.45); break;
                case UiSound.Error: Tone(0, 330, 0.12, 0.6, 0.6); Tone(0.1, 311, 0.18, 0.6, 0.6); break;
                case UiSound.Count: Tone(0, 880, 0.25, 0.6, 0.1); break;
                case UiSound.Go: Tone(0, 1760, 0.5, 0.6, 0.1); break;
                case UiSound.Checkpoint: Tone(0, 1568, 0.12, 0.45); Tone(0.07, 2093, 0.2, 0.45); break;
                case UiSound.Reward:
                {
                    double[] ats = { 0, 0.08, 0.16, 0.24 };
                    double[] fs = { 1047, 1319, 1568, 2093 };
                    for (int i = 0; i < 4; i++) Tone(ats[i], fs[i], 0.35, 0.4);
                    break;
                }
                case UiSound.Photo:
                {
                    var rng = new AudioRng(5);
                    var hp = Biquad.Highpass(sr, 2000, 0.7);
                    for (int i = 0; i < 0.06 * sr; i++) o[i] = (float)(o[i] + hp.Process(rng.Signed()) * Math.Exp(-i / (0.008 * sr)) * 0.8);
                    int a = (int)JsRound(0.07 * sr);
                    for (int i = 0; i < 0.05 * sr; i++) o[a + i] = (float)(o[a + i] + hp.Process(rng.Signed()) * Math.Exp(-i / (0.006 * sr)) * 0.6);
                    break;
                }
            }
            return Dsp.Normalize(Dsp.FadeEdges(o, sr, 0.5, 20), 0.1, 0.8);
        }
    }
}
