// Engine sound, synthesized per car from its engine spec; a port of the web
// version's src/audio/engine-synth.ts. Each firing of a cylinder is an
// exhaust pressure pulse; the pulses run through the exhaust (a few
// resonances per bank and a muffler) and are rendered into seamless loops at
// a ladder of engine speeds, on and off the throttle. At run time the player
// pitches the two loops either side of the current rpm and crossfades
// between them (and between the on- and off-load sets), the way racing games
// play recorded engines; here the recordings are synthesized.
//
// The firing pattern carries the character: an inline four's even buzz, a
// six's smooth wail, a cross-plane V8's uneven bank pattern (the burble), a
// flat-plane V8's scream. Electric motors are a stack of motor harmonics.
using System;
using System.Collections.Generic;

namespace Halcyon.Core.Audio
{
    public struct Firing
    {
        /// <summary>Fraction of the 720-degree cycle.</summary>
        public double at;
        /// <summary>Exhaust bank, 0 or 1.</summary>
        public int bank;
        /// <summary>Relative pulse strength.</summary>
        public double amp;
    }

    public sealed class EngineVoice
    {
        public bool electric;
        public Firing[] firing;
        /// <summary>Exhaust resonances, Hz, and how much each bank detunes them.</summary>
        public double boom, honk, rasp, bankDetune;
        /// <summary>Muffler low-pass cutoff, Hz.</summary>
        public double muffler;
        /// <summary>Rasp and combustion noise, 0..1 (sporty engines more).</summary>
        public double edge;
        public double idle, limiter;
        public bool turbo;
        /// <summary>Electric: motor pole pairs (sets the whine's pitch).</summary>
        public int poles;
        public uint seed;
    }

    public sealed class EngineLoop
    {
        /// <summary>Engine speed the loop was rendered at (after rounding to whole cycles).</summary>
        public double rpm;
        public bool onLoad;
        public float[] data;
    }

    public sealed class EngineSoundSet
    {
        public EngineVoice voice;
        public int sampleRate;
        /// <summary>Ascending rpm; the same ladder for the on- and off-load sets.</summary>
        public EngineLoop[] on, off;
    }

    public struct LoopMix
    {
        /// <summary>Index of the lower and upper loop, the blend toward the upper one (0..1), and both playback rates.</summary>
        public int lo, hi;
        public double t, rateLo, rateHi;
    }

    public static class EngineSynth
    {
        /// <summary>JavaScript's Math.round (half up), for identical loop lengths.</summary>
        static double JsRound(double x) => Math.Floor(x + 0.5);

        /// <summary>FNV-1a of the car id, as the TypeScript's hashId.</summary>
        public static uint HashId(string s)
        {
            uint h = 0x811c9dc5;
            foreach (char ch in s)
            {
                h ^= ch;
                h *= 0x01000193;
            }
            return h;
        }

        static double Sporty(Category c)
        {
            switch (c)
            {
                case Category.Super: return 1;
                case Category.Hyper: return 1;
                case Category.Sports: return 0.8;
                case Category.HotHatch: return 0.75;
                case Category.Rally: return 0.8;
                case Category.GT: return 0.6;
                case Category.Muscle: return 0.55;
                case Category.Classic: return 0.45;
                case Category.Compact: return 0.25;
                case Category.OffRoad: return 0.3;
                case Category.Truck: return 0.3;
                case Category.SUV: return 0.35;
                default: return 0.5;
            }
        }

        /// <summary>The engine's voice from its spec: firing pattern, exhaust and character.</summary>
        public static EngineVoice Voice(CarSpec spec)
        {
            var e = spec.engine;
            var rng = new AudioRng(HashId(spec.id) ^ 0x5eedu);
            int n = Math.Max(0, e.cylinders);
            string layout = e.layout.ToUpperInvariant();
            bool vee = layout.StartsWith("V", StringComparison.Ordinal) || layout.StartsWith("FLAT", StringComparison.Ordinal) || layout.StartsWith("W", StringComparison.Ordinal);
            // A V8 revving past 7500 is taken to be flat-plane (even banks); otherwise cross-plane.
            bool crossPlane = n == 8 && vee && e.redline < 7500;
            int[] crossBanks = { 0, 1, 1, 0, 1, 0, 0, 1 };
            var firing = new Firing[n];
            for (int k = 0; k < n; k++)
            {
                int bank = !vee ? 0 : crossPlane ? crossBanks[k] : k % 2;
                // Cylinders are never quite equal; cross-plane banks differ more.
                double amp = 1 + 0.06 * rng.Signed() + (crossPlane ? (bank != 0 ? 0.1 : -0.1) : 0);
                firing[k] = new Firing { at = (double)k / n, bank = bank, amp = amp };
            }
            double disp = Math.Max(0.6, e.displacement);
            double sporty = Sporty(spec.category);
            return new EngineVoice
            {
                electric = e.aspiration == Aspiration.Electric || n == 0,
                firing = firing,
                boom = Math.Max(55, Math.Min(150, 150 - 13 * (disp - 1))),
                honk = Math.Max(300, 660 - 38 * disp),
                rasp = 1500 + 1500 * sporty,
                bankDetune = vee ? 0.07 : 0,
                muffler = 900 + 3000 * sporty,
                edge = sporty,
                idle = e.idle,
                limiter = e.limiter,
                turbo = e.aspiration == Aspiration.Turbo || e.aspiration == Aspiration.TwinTurbo,
                poles = 4,
                seed = HashId(spec.id),
            };
        }

        /// <summary>The ladder of loop speeds: idle, then even steps to the limiter (about 900 rpm apart).</summary>
        public static double[] RpmLadder(EngineVoice v)
        {
            double lo = v.idle, hi = v.limiter * 1.02;
            int steps = (int)Math.Max(3, JsRound((hi - lo) / 900));
            var o = new double[steps + 1];
            for (int i = 0; i <= steps; i++) o[i] = lo + ((hi - lo) * i) / steps;
            return o;
        }

        struct Ev
        {
            public double t, amp;
            public int bank, noiseSeed;
        }

        /// <summary>One combustion engine loop at `rpm`, on or off the throttle.</summary>
        public static EngineLoop RenderLoop(EngineVoice v, double rpm, bool onLoad, int sr, double seconds = 0.7)
        {
            double cycleHz = rpm / 120; // four-stroke: one cycle per two revolutions
            int cycles = (int)Math.Max(3, JsRound(seconds * cycleHz));
            int period = (int)JsRound((cycles / cycleHz) * sr);
            double effRpm = ((double)cycles * sr / period) * 120;
            double cyc = (double)period / cycles;
            var rng = new AudioRng(v.seed ^ (uint)(long)JsRound(rpm) ^ (onLoad ? 0xa11u : 0x0ffu));
            double load = onLoad ? 1 : 0.3;
            double rev = Math.Min(1, rpm / v.limiter);
            // Pulse rise time: sharper on load and at speed.
            double rise = ((onLoad ? 0.32 : 0.75) / 1000) * sr * (1.15 - 0.35 * rev);
            double burstDecay = (2.2 / 1000) * sr;
            double burstAmp = (0.15 + 0.5 * v.edge) * (onLoad ? 1 : 0.35);
            var events = new List<Ev>();
            double idleWobble = 1 - rev;
            for (int c = 0; c < cycles; c++)
            {
                foreach (var f in v.firing)
                {
                    double jitter = rng.Gauss() * (0.004 + 0.01 * idleWobble) * (cyc / v.firing.Length);
                    double amp = f.amp * load * (1 + rng.Gauss() * (0.03 + 0.07 * idleWobble));
                    events.Add(new Ev { t = (c + f.at) * cyc + jitter, bank = f.bank, amp = amp, noiseSeed = rng.Int(1 << 30) });
                }
            }
            bool twoBanks = false;
            foreach (var f in v.firing) if (f.bank == 1) twoBanks = true;
            int banks = twoBanks ? 2 : 1;
            var exc = new[] { new float[period * 3], new float[period * 3] };
            int pulseLen = (int)Math.Ceiling(rise * 10);
            int burstLen = (int)Math.Ceiling(burstDecay * 6);
            int span = Math.Max(pulseLen, burstLen);
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var ev in events)
                {
                    var buf = exc[ev.bank];
                    double t0 = ev.t + pass * period;
                    int i0 = (int)Math.Floor(t0);
                    var nrng = new AudioRng(ev.noiseSeed);
                    for (int j = 0; j < span; j++)
                    {
                        int i = i0 + j;
                        if (i < 0 || i >= buf.Length) continue;
                        double tau = i - t0;
                        if (tau < 0) continue;
                        double s = 0;
                        if (j < pulseLen)
                        {
                            double x = tau / rise;
                            s += x * Math.Exp(1 - x);
                        }
                        if (j < burstLen) s += burstAmp * nrng.Signed() * Math.Exp(-tau / burstDecay);
                        buf[i] = (float)(buf[i] + ev.amp * s);
                    }
                }
            }
            var all = new float[period * 3];
            for (int b = 0; b < banks; b++)
            {
                double d = b == 0 ? 1 - v.bankDetune * 0.5 : 1 + v.bankDetune * 0.5;
                var boom = Biquad.Bandpass(sr, v.boom * d, 1.3);
                var honk = Biquad.Bandpass(sr, v.honk * d, 2.4);
                var rasp = Biquad.Bandpass(sr, v.rasp * d, 1.1);
                var muff = Biquad.Lowpass(sr, v.muffler * (onLoad ? 1 : 0.6), 0.8);
                double raspGain = (0.12 + 0.55 * v.edge) * (onLoad ? 1 : 0.4);
                var e = exc[b];
                for (int i = 0; i < all.Length; i++)
                {
                    double x = e[i];
                    all[i] = (float)(all[i] + (1.0 * boom.Process(x) + 0.55 * honk.Process(x) + raspGain * rasp.Process(x) + 0.35 * muff.Process(x)));
                }
            }
            // Remove DC and rumble below hearing, then saturate a little: loud engines are not clean.
            var dc = Biquad.Highpass(sr, 28, 0.7);
            double drive = onLoad ? 1.6 + 1.2 * v.edge : 1.1;
            double level = 0;
            for (int i = 0; i < all.Length; i++) level += (double)all[i] * all[i];
            level = Math.Sqrt(level / all.Length);
            if (level == 0 || double.IsNaN(level)) level = 1;
            for (int i = 0; i < all.Length; i++) all[i] = (float)(Dsp.SoftClip((dc.Process(all[i]) / level) * 0.35 * drive) / drive);
            var data = new float[period];
            Array.Copy(all, period, data, 0, period);
            Dsp.Normalize(data, 0.25);
            return new EngineLoop { rpm = effRpm, onLoad = onLoad, data = data };
        }

        /// <summary>An electric motor loop at `rpm`: motor harmonics, a gear whine and a little hiss.</summary>
        public static EngineLoop RenderElectricLoop(EngineVoice v, double rpm, int sr, double seconds = 0.5)
        {
            double f1 = (rpm / 60) * v.poles;
            int k = (int)Math.Max(1, JsRound(seconds * f1));
            int period = (int)JsRound((k * (double)sr) / f1);
            double fEff = (k * (double)sr) / period;
            double effRpm = (fEff * 60) / v.poles;
            var rng = new AudioRng(v.seed ^ 0xe1ecu);
            double[,] partials = { { 1, 1 }, { 2, 0.45 }, { 3, 0.22 }, { 6, 0.12 }, { 5, 0.18 } };
            var hiss = Biquad.Bandpass(sr, 5000, 0.8);
            var data = new float[period];
            for (int i = 0; i < period; i++)
            {
                double s = 0;
                for (int p = 0; p < partials.GetLength(0); p++) s += partials[p, 1] * Math.Sin((2 * Math.PI * partials[p, 0] * k * i) / period);
                data[i] = (float)(s + 0.05 * hiss.Process(rng.Signed()));
            }
            Dsp.Normalize(data, 0.2);
            return new EngineLoop { rpm = effRpm, onLoad = true, data = data };
        }

        /// <summary>Every loop a car needs: the rpm ladder on and off the throttle (or one motor loop).</summary>
        public static EngineSoundSet RenderSet(CarSpec spec, int sr)
        {
            var voice = Voice(spec);
            if (voice.electric)
            {
                // Rendered at 9000 rpm: played from a crawl to 22000 rpm, its rate stays inside what Unity can pitch (3x).
                var loop = RenderElectricLoop(voice, 9000, sr);
                return new EngineSoundSet { voice = voice, sampleRate = sr, on = new[] { loop }, off = new[] { loop } };
            }
            var ladder = RpmLadder(voice);
            var on = new EngineLoop[ladder.Length];
            var off = new EngineLoop[ladder.Length];
            for (int i = 0; i < ladder.Length; i++)
            {
                on[i] = RenderLoop(voice, ladder[i], true, sr);
                off[i] = RenderLoop(voice, ladder[i], false, sr);
            }
            return new EngineSoundSet { voice = voice, sampleRate = sr, on = on, off = off };
        }

        /// <summary>Which two loops play at `rpm`, how they blend and how fast each plays.</summary>
        public static LoopMix Mix(EngineLoop[] loops, double rpm)
        {
            var m = new LoopMix();
            int n = loops.Length;
            if (n == 1 || rpm <= loops[0].rpm)
            {
                m.lo = m.hi = 0;
                m.t = 0;
            }
            else if (rpm >= loops[n - 1].rpm)
            {
                m.lo = m.hi = n - 1;
                m.t = 0;
            }
            else
            {
                int i = 0;
                while (i < n - 2 && loops[i + 1].rpm <= rpm) i++;
                m.lo = i;
                m.hi = i + 1;
                m.t = (rpm - loops[i].rpm) / (loops[i + 1].rpm - loops[i].rpm);
            }
            m.rateLo = Math.Max(0.05, rpm / loops[m.lo].rpm);
            m.rateHi = Math.Max(0.05, rpm / loops[m.hi].rpm);
            return m;
        }
    }
}
