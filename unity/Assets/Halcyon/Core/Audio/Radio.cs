// Three generative radio stations; a port of the web version's
// src/audio/radio.ts. Each station composes endless songs from a seed (key,
// tempo, chord progression, arrangement, motifs) and renders them a bar at a
// time into stereo samples, with synthesized drums, bass, chords and leads,
// a reverb and a limiter.
//
//   Halcyon Pulse   synthwave: four on the floor, gated pads, arpeggios
//   Coastline FM    lo-fi: swung drums, electric piano sevenths, vinyl hiss
//   Redline Radio   rock: power chords through a driven amp, big drums
using System;
using System.Collections.Generic;

namespace Halcyon.Core.Audio
{
    public enum StationId { Pulse, Coast, Redline }

    public sealed class StationInfo
    {
        public StationId id;
        public string name, tagline;
    }

    public sealed class RadioBar
    {
        public float[] l, r;
        public StationId station;
        public string title, artist;
        /// <summary>True on the first bar of a song (and on the station ident).</summary>
        public bool newSong, ident;
    }

    public sealed class RadioStation
    {
        public static readonly StationInfo[] Stations =
        {
            new StationInfo { id = StationId.Pulse, name = "Halcyon Pulse", tagline = "synthwave all night" },
            new StationInfo { id = StationId.Coast, name = "Coastline FM", tagline = "low tides, lo-fi" },
            new StationInfo { id = StationId.Redline, name = "Redline Radio", tagline = "loud and fast" },
        };

        static double JsRound(double x) => Math.Floor(x + 0.5);
        static double MidiHz(double m) => 440 * Math.Pow(2, (m - 69) / 12);

        static readonly int[] Minor = { 0, 2, 3, 5, 7, 8, 10 };
        static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
        static readonly int[] Dorian = { 0, 2, 3, 5, 7, 9, 10 };
        static readonly int[] Mixolydian = { 0, 2, 4, 5, 7, 9, 10 };

        /// <summary>Scale degree (any integer, wraps by octaves) to a semitone offset.</summary>
        static int Degree(int[] scale, int d)
        {
            int n = scale.Length;
            int o = (int)Math.Floor((double)d / n);
            return scale[((d % n) + n) % n] + 12 * o;
        }

        static readonly string[] TitleA = { "Neon", "Coastal", "Midnight", "Golden", "Harbour", "Summit", "Velvet", "Static", "Salt", "Chrome", "Hollow", "Paper", "Ember", "Northern", "Long", "Quiet", "Open", "Silver", "Last", "Wild" };
        static readonly string[] TitleB = { "Drift", "Lights", "Horizon", "Overpass", "Tide", "Signal", "Run", "Heat", "Coast", "Machine", "Hours", "Weather", "Line", "Road", "Summer", "Sky", "Engine", "Shore", "Echo", "Miles" };
        static readonly string[][] Artists =
        {
            new[] { "Laser Harbour", "NightCity 86", "Vector Bloom", "Polar Grid", "Sunset Kinetic" },
            new[] { "tidepool", "slow ferry", "mono lagoon", "kettle & reed", "paper boats" },
            new[] { "The Torque Brothers", "Gravel Saints", "Red Mile", "Overboost", "Fourth Gear" },
        };

        // ------------------------------------------------------------------
        // Instruments: each note renders itself into a window of the output and
        // keeps its own state across bars.

        sealed class Fx
        {
            public float[] rev, dly;
        }

        abstract class Voice
        {
            public readonly long start;
            protected int t;
            protected readonly int sr, len;
            readonly double gl, gr, rev, dly;

            protected Voice(long start, int sr, int len, double pan, double rev, double dly = 0)
            {
                this.start = start;
                this.sr = sr;
                this.len = len;
                this.rev = rev;
                this.dly = dly;
                double a = (pan + 1) * 0.25 * Math.PI;
                gl = Math.Cos(a);
                gr = Math.Sin(a);
            }

            protected abstract double Sample(int t);

            public bool Render(float[] l, float[] r, long b, long from, long to, Fx fx)
            {
                long a = Math.Max(from, start + t);
                for (long i = a; i < to; i++)
                {
                    if (t >= len) return false;
                    double s = Sample(t++);
                    int k = (int)(i - b);
                    l[k] = (float)(l[k] + s * gl);
                    r[k] = (float)(r[k] + s * gr);
                    fx.rev[k] = (float)(fx.rev[k] + s * rev);
                    if (dly != 0) fx.dly[k] = (float)(fx.dly[k] + s * dly);
                }
                return t < len;
            }
        }

        sealed class Kick : Voice
        {
            double ph;
            readonly double vel, punch;
            public Kick(long start, int sr, double vel, double punch = 1) : base(start, sr, (int)JsRound(sr * 0.45), 0, 0.04)
            {
                this.vel = vel;
                this.punch = punch;
            }
            protected override double Sample(int t)
            {
                double s = (double)t / sr;
                double f = 44 + 120 * punch * Math.Exp(-s / 0.028);
                ph += f / sr;
                double body = Math.Sin(2 * Math.PI * ph) * Math.Exp(-s / 0.26);
                double click = s < 0.002 ? (1 - s / 0.002) * 0.5 : 0;
                return vel * 0.9 * Dsp.SoftClip((body + click) * 1.4);
            }
        }

        sealed class NoiseHit : Voice
        {
            readonly AudioRng rng;
            readonly Biquad f1, f2;
            readonly double vel, decay, tone;
            public NoiseHit(long start, int sr, double vel, double decay, double hp, double bp, double pan, double rev, double tone = 0, long seed = 1)
                : base(start, sr, (int)JsRound(sr * Math.Min(2.5, decay * 7)), pan, rev)
            {
                this.vel = vel;
                this.decay = decay;
                this.tone = tone;
                rng = new AudioRng(seed);
                f1 = Biquad.Highpass(sr, hp, 0.7);
                f2 = bp > 0 ? Biquad.Bandpass(sr, bp, 0.8) : null;
            }
            protected override double Sample(int t)
            {
                double s = (double)t / sr;
                double n = f1.Process(rng.Signed());
                if (f2 != null) n = f2.Process(n) * 1.6;
                double v = n * Math.Exp(-s / decay);
                if (tone != 0) v += Math.Sin(2 * Math.PI * tone * s) * Math.Exp(-s / 0.06) * 0.6;
                return v * vel;
            }
        }

        /// <summary>Band-limited saw (polyBLEP).</summary>
        static double BlepSaw(double ph, double dt)
        {
            double v = 2 * ph - 1;
            if (ph < dt)
            {
                double x = ph / dt;
                v -= x + x - x * x - 1;
            }
            else if (ph > 1 - dt)
            {
                double x = (ph - 1) / dt;
                v -= x * x + x + x + 1;
            }
            return v;
        }

        static double BlepSquare(double ph, double dt)
        {
            double v = ph < 0.5 ? 1 : -1;
            double p2 = (ph + 0.5) % 1;
            if (ph < dt)
            {
                double x = ph / dt;
                v += x + x - x * x - 1;
            }
            else if (ph > 1 - dt)
            {
                double x = (ph - 1) / dt;
                v += x * x + x + x + 1;
            }
            if (p2 < dt)
            {
                double x = p2 / dt;
                v -= x + x - x * x - 1;
            }
            else if (p2 > 1 - dt)
            {
                double x = (p2 - 1) / dt;
                v -= x * x + x + x + 1;
            }
            return v;
        }

        /// <summary>An envelope: attack, then hold for `hold` samples, then release.</summary>
        static double Adsr(int t, int sr, double a, int hold, double rel)
        {
            double s = (double)t / sr;
            if (s < a) return s / a;
            if (t < hold) return 1;
            return Math.Exp(-(t - hold) / (rel * sr));
        }

        enum Wave { Saw, Square, Sine, Tri }

        sealed class Synth : Voice
        {
            readonly double[] ph, freqs;
            readonly Biquad lp, hp;
            readonly int hold;
            readonly double vibStart, vel, attack, release, cutoff, cutoffEnv, cutoffDecay, detune, vibrato, drive;
            readonly Wave wave;

            public Synth(long start, int sr, double holdSec, double[] freqs, double vel, Wave wave, double attack, double release, double cutoff, double cutoffEnv, double cutoffDecay, double detune,
                double pan, double rev, double dly = 0, double vibrato = 0, double drive = 0)
                : base(start, sr, (int)JsRound(sr * (holdSec + release * 6)), pan, rev, dly)
            {
                this.freqs = freqs;
                this.vel = vel;
                this.wave = wave;
                this.attack = attack;
                this.release = release;
                this.cutoff = cutoff;
                this.cutoffEnv = cutoffEnv;
                this.cutoffDecay = cutoffDecay;
                this.detune = detune;
                this.vibrato = vibrato;
                this.drive = drive;
                ph = new double[freqs.Length];
                for (int i = 0; i < ph.Length; i++) ph[i] = (i * 0.37) % 1;
                lp = Biquad.Lowpass(sr, cutoff, 0.9);
                hp = Biquad.Highpass(sr, 60, 0.7);
                hold = (int)JsRound(sr * holdSec);
                vibStart = sr * 0.18;
            }

            protected override double Sample(int t)
            {
                double s = 0;
                double vib = vibrato != 0 && t > vibStart ? 1 + vibrato * Math.Sin((2 * Math.PI * 5.2 * t) / sr) * Math.Min(1, (t - vibStart) / (sr * 0.3)) : 1;
                for (int i = 0; i < freqs.Length; i++)
                {
                    double f = freqs[i] * vib * (1 + detune * (i % 3 == 0 ? 0 : i % 3 == 1 ? 1 : -1));
                    double dt = f / sr;
                    ph[i] = (ph[i] + dt) % 1;
                    double p = ph[i];
                    s += wave == Wave.Saw ? BlepSaw(p, dt) : wave == Wave.Square ? BlepSquare(p, dt) : wave == Wave.Tri ? 1 - 4 * Math.Abs(p - 0.5) : Math.Sin(2 * Math.PI * p);
                }
                s /= Math.Sqrt(freqs.Length);
                if (drive != 0) s = Dsp.SoftClip(s * drive) * 0.7;
                if (t % 32 == 0)
                {
                    double c = cutoff + cutoffEnv * Math.Exp(-t / (cutoffDecay * sr));
                    lp.SetLowpass(sr, Math.Min(sr * 0.45, c), 0.9);
                }
                s = lp.Process(s);
                s = hp.Process(s);
                return s * vel * Adsr(t, sr, attack, hold, release);
            }
        }

        sealed class EPiano : Voice
        {
            readonly int hold;
            readonly double f, vel;
            public EPiano(long start, int sr, double holdSec, double f, double vel, double pan, double rev) : base(start, sr, (int)JsRound(sr * (holdSec + 1.6)), pan, rev)
            {
                hold = (int)JsRound(sr * holdSec);
                this.f = f;
                this.vel = vel;
            }
            protected override double Sample(int t)
            {
                double s = (double)t / sr;
                double idx = 1.6 * Math.Exp(-s / 0.35) + 0.25;
                double mod = Math.Sin(2 * Math.PI * f * s) * idx;
                double car = Math.Sin(2 * Math.PI * f * s + mod);
                double bell = Math.Sin(2 * Math.PI * f * 4.01 * s) * 0.12 * Math.Exp(-s / 0.08);
                double env = Math.Exp(-s / 1.4) * (t < hold ? 1 : Math.Exp(-(t - hold) / (0.25 * sr)));
                double trem = 1 + 0.18 * Math.Sin(2 * Math.PI * 4.6 * s);
                return (car + bell) * env * trem * vel * Math.Min(1, s / 0.004);
            }
        }

        // ------------------------------------------------------------------
        // Effects with state across bars.

        sealed class Comb
        {
            readonly float[] buf;
            int i;
            double lp;
            readonly double fb, damp;
            public Comb(int len, double fb, double damp)
            {
                buf = new float[len];
                this.fb = fb;
                this.damp = damp;
            }
            public double Process(double x)
            {
                double y = buf[i];
                lp = y * (1 - damp) + lp * damp;
                buf[i] = (float)(x + lp * fb);
                i = (i + 1) % buf.Length;
                return y;
            }
        }

        sealed class Allpass
        {
            readonly float[] buf;
            int i;
            public Allpass(int len) => buf = new float[len];
            public double Process(double x)
            {
                double b = buf[i];
                double y = -x + b;
                buf[i] = (float)(x + b * 0.5);
                i = (i + 1) % buf.Length;
                return y;
            }
        }

        /// <summary>A small Freeverb-style stereo reverb.</summary>
        sealed class Reverb
        {
            readonly Comb[] cl, cr;
            readonly Allpass[] al, ar;
            public Reverb(int sr, double size, double damp)
            {
                double k = sr / 44100.0;
                int[] combs = { 1116, 1188, 1277, 1356 };
                cl = new Comb[4];
                cr = new Comb[4];
                for (int c = 0; c < 4; c++)
                {
                    cl[c] = new Comb((int)JsRound(combs[c] * k), size, damp);
                    cr[c] = new Comb((int)JsRound((combs[c] + 23) * k), size, damp);
                }
                al = new[] { new Allpass((int)JsRound(556 * k)), new Allpass((int)JsRound(441 * k)) };
                ar = new[] { new Allpass((int)JsRound(579 * k)), new Allpass((int)JsRound(464 * k)) };
            }
            public void Process(double x, out double ol, out double or)
            {
                double l = 0, r = 0;
                foreach (var c in cl) l += c.Process(x);
                foreach (var c in cr) r += c.Process(x);
                foreach (var a in al) l = a.Process(l);
                foreach (var a in ar) r = a.Process(r);
                ol = l * 0.25;
                or = r * 0.25;
            }
        }

        /// <summary>Ping-pong delay.</summary>
        sealed class Delay
        {
            readonly float[] bl, br;
            int i;
            readonly double fb;
            public Delay(int len, double fb)
            {
                bl = new float[len];
                br = new float[len];
                this.fb = fb;
            }
            public void Process(double x, out double ol, out double or)
            {
                double l = bl[i], r = br[i];
                bl[i] = (float)(x + r * fb);
                br[i] = (float)(l * fb);
                i = (i + 1) % bl.Length;
                ol = l;
                or = r;
            }
        }

        // ------------------------------------------------------------------
        // Composition.

        sealed class Section
        {
            public string name;
            public int bars, energy;
            public Section(string name, int bars, int energy)
            {
                this.name = name;
                this.bars = bars;
                this.energy = energy;
            }
        }

        sealed class Song
        {
            public string title, artist;
            public int bpm, root;
            public int[] scale, prog, bassPattern;
            public Section[] sections;
            /// <summary>A two-bar motif: (16th step, length in 16ths, scale degree offset).</summary>
            public List<(int step, int len, int deg)> motif;
            /// <summary>The chorus's own motif (the hook).</summary>
            public List<(int step, int len, int deg)> hook;
            public long seed;
        }

        public readonly StationInfo Info;
        public readonly StationId Id;
        public readonly int SampleRate;
        readonly AudioRng rng;
        Song song;
        int bar, sectionIndex, sectionBar, songCount;
        List<Voice> notes = new List<Voice>();
        long clock;
        readonly Reverb reverb;
        readonly Delay delay;
        bool identPending = true;
        readonly AudioRng crackle;
        readonly OnePole crackleLp;
        readonly Biquad hissHp;
        double lim = 1;
        readonly int sr;

        public RadioStation(StationId id, int sr, long seed)
        {
            Id = id;
            SampleRate = sr;
            this.sr = sr;
            Info = Stations[(int)id];
            rng = new AudioRng(seed ^ (id == StationId.Pulse ? 0x9a1 : id == StationId.Coast ? 0xc0a : 0x7ed));
            double size = id == StationId.Coast ? 0.8 : id == StationId.Pulse ? 0.84 : 0.72;
            reverb = new Reverb(sr, size, 0.35);
            delay = new Delay((int)JsRound(sr * 0.3), 0.38);
            crackle = new AudioRng(seed + 77);
            crackleLp = new OnePole(sr, 3000);
            hissHp = Biquad.Highpass(sr, 4000, 0.7);
            NewSong();
        }

        void NewSong()
        {
            var r = rng;
            int bpm = Id == StationId.Pulse ? 104 + r.Int(16) : Id == StationId.Coast ? 74 + r.Int(14) : 132 + r.Int(22);
            int[] scale = Id == StationId.Pulse ? Minor : Id == StationId.Coast ? r.Pick(new[] { Dorian, Major }) : r.Pick(new[] { Minor, Mixolydian });
            int[][] progs = Id == StationId.Pulse
                ? new[] { new[] { 0, 5, 2, 6 }, new[] { 0, 5, 3, 4 }, new[] { 0, 3, 5, 4 }, new[] { 5, 6, 0, 0 }, new[] { 0, 6, 5, 6 } }
                : Id == StationId.Coast
                    ? new[] { new[] { 1, 4, 0, 0 }, new[] { 3, 2, 1, 0 }, new[] { 0, 5, 1, 4 }, new[] { 5, 3, 4, 0 }, new[] { 1, 4, 2, 5 } }
                    : new[] { new[] { 0, 6, 5, 6 }, new[] { 0, 3, 4, 3 }, new[] { 0, 5, 6, 0 }, new[] { 0, 2, 3, 4 }, new[] { 0, 0, 3, 5 } };
            int root = (Id == StationId.Redline ? 40 : 45) + r.Int(7);
            Section[] sections = Id == StationId.Coast
                ? new[] { new Section("intro", 4, 1), new Section("a", 8, 2), new Section("b", 8, 3), new Section("a", 8, 2), new Section("b", 8, 3), new Section("outro", 4, 1) }
                : new[] { new Section("intro", 4, 1), new Section("verse", 8, 2), new Section("chorus", 8, 3), new Section("verse", 8, 2), new Section("chorus", 8, 3), new Section("break", 4, 1), new Section("chorus", 8, 3), new Section("outro", 4, 1) };
            var motif = MakeMotif(r);
            int[][] bassPatterns = Id == StationId.Pulse
                ? new[] { new[] { 0, 2, 4, 6, 8, 10, 12, 14 }, new[] { 0, 3, 6, 8, 11, 14 } }
                : Id == StationId.Coast
                    ? new[] { new[] { 0, 6, 10 }, new[] { 0, 7, 10, 14 } }
                    : new[] { new[] { 0, 2, 4, 6, 8, 10, 12, 14 }, new[] { 0, 4, 6, 8, 12, 14 } };
            string a = r.Pick(TitleA);
            string b = r.Pick(TitleB);
            string artist = r.Pick(Artists[(int)Id]);
            int[] prog = r.Pick(progs);
            int[] bass = r.Pick(bassPatterns);
            long seed = r.Int(1 << 30);
            song = new Song { title = a + " " + b, artist = artist, bpm = bpm, root = root, scale = scale, prog = prog, sections = sections, motif = motif, bassPattern = bass, seed = seed };
            // The chorus gets a hook of its own, so a song goes somewhere.
            song.hook = MakeMotif(r);
            sectionIndex = 0;
            sectionBar = 0;
            bar = 0;
            songCount++;
        }

        /// <summary>A motif of 4 to 7 notes over two bars, on chord-friendly degrees.</summary>
        static List<(int step, int len, int deg)> MakeMotif(AudioRng r)
        {
            var motif = new List<(int, int, int)>();
            int step = r.Int(3) * 2;
            int count = 4 + r.Int(4);
            int deg = r.Pick(new[] { 0, 2, 4 });
            for (int i = 0; i < count && step < 30; i++)
            {
                int len = r.Pick(new[] { 2, 2, 3, 4, 4, 6 });
                motif.Add((step, len, deg));
                step += len + (r.Next() < 0.3 ? 2 : 0);
                deg += r.Pick(new[] { -2, -1, 1, 1, 2, 3, -3 });
                deg = Math.Max(-2, Math.Min(7, deg));
            }
            return motif;
        }

        /// <summary>The song playing (for the HUD).</summary>
        public string NowPlaying => song.artist + " – " + song.title;

        /// <summary>Render the next bar: stereo samples and what it is.</summary>
        public RadioBar RenderBar()
        {
            bool ident = identPending;
            identPending = false;
            var s = song;
            double bpm = ident ? 120 : s.bpm;
            int barLen = (int)JsRound(sr * (60 / bpm) * 4);
            long b = clock;
            bool newSong = ident || (sectionIndex == 0 && sectionBar == 0);
            if (ident) ScheduleIdent(b, barLen);
            else ScheduleBar(b, barLen);
            var l = new float[barLen];
            var r = new float[barLen];
            var fx = new Fx { rev = new float[barLen], dly = new float[barLen] };
            long end = b + barLen;
            var keep = new List<Voice>(notes.Count);
            foreach (var n in notes)
                if (n.Render(l, r, b, b, end, fx)) keep.Add(n);
            notes = keep;
            // Lo-fi: vinyl crackle and hiss under everything.
            if (Id == StationId.Coast)
            {
                for (int i = 0; i < barLen; i++)
                {
                    double c = crackle.Next() < 0.0009 ? crackle.Signed() * 0.5 : 0;
                    double hiss = hissHp.Process(crackle.Signed()) * 0.012;
                    double v = crackleLp.Process(c) + hiss;
                    l[i] = (float)(l[i] + v);
                    r[i] = (float)(r[i] + v);
                }
            }
            double wet = Id == StationId.Redline ? 0.6 : 0.9;
            for (int i = 0; i < barLen; i++)
            {
                reverb.Process(fx.rev[i], out double rl, out double rr);
                double L = l[i] + rl * wet;
                double R = r[i] + rr * wet;
                delay.Process(fx.dly[i], out double dl, out double dr);
                L += dl * 0.5;
                R += dr * 0.5;
                // A slow limiter: pull the gain down fast on peaks, let it recover over half a second.
                double p = Math.Max(Math.Abs(L), Math.Abs(R)) * lim;
                if (p > 0.85) lim *= 0.85 / p;
                else lim += (1 - lim) * (1 / (0.5 * sr));
                l[i] = (float)(Dsp.SoftClip(L * lim * 1.1) * 0.82);
                r[i] = (float)(Dsp.SoftClip(R * lim * 1.1) * 0.82);
            }
            clock = end;
            var info = new RadioBar { l = l, r = r, station = Id, title = s.title, artist = s.artist, newSong = newSong, ident = ident };
            if (!ident) Advance();
            return info;
        }

        void Advance()
        {
            var s = song;
            bar++;
            sectionBar++;
            if (sectionBar >= s.sections[sectionIndex].bars)
            {
                sectionBar = 0;
                sectionIndex++;
                if (sectionIndex >= s.sections.Length)
                {
                    NewSong();
                    // Every second song, the station says its name.
                    if (songCount % 2 == 0) identPending = true;
                }
            }
        }

        /// <summary>The station's ident: a rising sweep and three chimes spelling its sound.</summary>
        void ScheduleIdent(long b, int barLen)
        {
            double beat = barLen / 4.0;
            int[] chord = Id == StationId.Pulse ? new[] { 69, 72, 76 } : Id == StationId.Coast ? new[] { 67, 71, 74 } : new[] { 64, 67, 71 };
            notes.Add(new NoiseHit(b, sr, 0.18, 0.9, 1500, 0, 0, 0.5, 0, 9));
            for (int i = 0; i < chord.Length; i++)
                notes.Add(new Synth(b + (long)JsRound(beat * (1 + i * 0.5)), sr, 0.25, new[] { MidiHz(chord[i] + 12) }, 0.22, Wave.Sine, 0.003, 0.5, 9000, 0, 0.1, 0, i - 1, 0.5, 0.3));
        }

        void ScheduleBar(long b, int barLen)
        {
            var s = song;
            var sec = s.sections[sectionIndex];
            int e = sec.energy;
            var r = new AudioRng(s.seed + bar * 7919L);
            int chordDeg = s.prog[bar % s.prog.Length];
            double sixteenth = barLen / 16.0;
            double swing = Id == StationId.Coast ? 0.16 : 0;
            long At(int st) => b + (long)JsRound(sixteenth * (st + (st % 2 == 1 ? swing * 2 : 0)));
            bool lastBarOfSection = sectionBar == sec.bars - 1;
            bool firstBarOfSection = sectionBar == 0;
            int ChordNote(int k, int octave) => s.root + 12 * octave + Degree(s.scale, chordDeg + k);
            double Hold(double steps) => (steps * sixteenth) / sr;

            // --- Drums -------------------------------------------------------
            if (Id == StationId.Pulse)
            {
                if (e >= 2 || (e == 1 && sectionBar >= 2))
                    for (int k = 0; k < 4; k++) notes.Add(new Kick(At(k * 4), sr, 0.9));
                if (e >= 2)
                {
                    foreach (int st in new[] { 4, 12 }) notes.Add(new NoiseHit(At(st), sr, 0.42, 0.14, 900, 2200, 0, 0.35, 185, r.Int(1000000000)));
                    for (int st = 2; st < 16; st += 4) notes.Add(new NoiseHit(At(st), sr, 0.16, 0.05, 7000, 0, 0.3, 0.05, 0, r.Int(1000000000)));
                }
                if (e >= 3)
                    for (int st = 0; st < 16; st += 2)
                        if (st % 4 != 2) notes.Add(new NoiseHit(At(st), sr, 0.07, 0.025, 8000, 0, -0.3, 0.02, 0, r.Int(1000000000)));
                // A snare roll into the next section, and a crash where the chorus lands.
                if (lastBarOfSection && e >= 2)
                    foreach (int st in new[] { 12, 13, 14, 15 }) notes.Add(new NoiseHit(At(st), sr, 0.22 + 0.06 * (st - 12), 0.1, 900, 2200, 0, 0.35, 185, r.Int(1000000000)));
                if (firstBarOfSection && e >= 3) notes.Add(new NoiseHit(At(0), sr, 0.22, 1.1, 5000, 0, 0.2, 0.45, 0, r.Int(1000000000)));
            }
            else if (Id == StationId.Coast)
            {
                if (e >= 2)
                {
                    int[] kicks = r.Next() < 0.5 ? new[] { 0, 7, 10 } : new[] { 0, 6, 10, 14 };
                    foreach (int st in kicks) notes.Add(new Kick(At(st), sr, 0.65, 0.6));
                    foreach (int st in new[] { 4, 12 }) notes.Add(new NoiseHit(At(st), sr, 0.3, 0.11, 700, 1800, 0.05, 0.4, 170, r.Int(1000000000)));
                }
                if (e >= 1)
                    for (int st = 0; st < 16; st += 2) notes.Add(new NoiseHit(At(st), sr, st % 4 == 0 ? 0.08 : 0.05, 0.03, 7500, 0, 0.25, 0.08, 0, r.Int(1000000000)));
                // A soft drag on the snare at the end of a section.
                if (lastBarOfSection && e >= 2)
                    foreach (int st in new[] { 14, 15 }) notes.Add(new NoiseHit(At(st), sr, 0.18, 0.08, 700, 1800, 0.05, 0.4, 170, r.Int(1000000000)));
            }
            else
            {
                if (e >= 2 || (e == 1 && sectionBar % 2 == 1))
                {
                    int[] kicks = e >= 3 ? new[] { 0, 3, 8, 10 } : new[] { 0, 8, 11 };
                    foreach (int st in kicks) notes.Add(new Kick(At(st), sr, 0.95, 1.2));
                    foreach (int st in new[] { 4, 12 }) notes.Add(new NoiseHit(At(st), sr, 0.6, 0.16, 600, 2500, 0, 0.45, 200, r.Int(1000000000)));
                    for (int st = 0; st < 16; st += 2) notes.Add(new NoiseHit(At(st), sr, 0.09, e >= 3 ? 0.09 : 0.04, 6500, 0, 0.3, 0.05, 0, r.Int(1000000000)));
                }
                if (firstBarOfSection && e >= 2) notes.Add(new NoiseHit(At(0), sr, 0.28, 1.0, 4500, 0, -0.2, 0.4, 0, r.Int(1000000000)));
                if (lastBarOfSection && e >= 2)
                    foreach (int st in new[] { 12, 13, 14, 15 }) notes.Add(new NoiseHit(At(st), sr, 0.45, 0.12, 600, 2400, (st - 13.5) * 0.3, 0.4, 210, r.Int(1000000000)));
            }

            // --- Bass --------------------------------------------------------
            if (e >= 2 || (Id != StationId.Redline && e >= 1 && sectionBar >= 2))
            {
                double f0 = MidiHz(ChordNote(0, -1));
                // In a chorus the last note of the bar walks to the next chord's root.
                double next = MidiHz(s.root - 12 + Degree(s.scale, s.prog[(bar + 1) % s.prog.Length]));
                foreach (int st in s.bassPattern)
                {
                    double f = e >= 3 && st == 14 ? next : f0;
                    int oct = Id == StationId.Pulse && st % 4 == 2 ? 2 : 1;
                    int len = Id == StationId.Coast ? 3 : 2;
                    if (Id == StationId.Coast) notes.Add(new Synth(At(st), sr, Hold(len), new[] { f }, 0.55, Wave.Sine, 0.005, 0.12, 900, 0, 0.1, 0, 0, 0.02));
                    else if (Id == StationId.Pulse) notes.Add(new Synth(At(st), sr, Hold(len) * 0.8, new[] { f * oct }, 0.32, Wave.Saw, 0.002, 0.06, 260, 1400, 0.09, 0.002, 0, 0.03));
                    else notes.Add(new Synth(At(st), sr, Hold(len) * 0.9, new[] { f }, 0.36, Wave.Saw, 0.002, 0.05, 500, 900, 0.07, 0, 0, 0.02, 0, 0, 1.5));
                }
            }

            // --- Chords ------------------------------------------------------
            if (Id == StationId.Pulse)
            {
                var fs = new[] { MidiHz(ChordNote(0, 1)), MidiHz(ChordNote(2, 1)), MidiHz(ChordNote(4, 1)) };
                if (e >= 3)
                {
                    // Gated pad: eighth-note chops.
                    for (int st = 0; st < 16; st += 2) notes.Add(new Synth(At(st), sr, Hold(1.6), fs, 0.11, Wave.Saw, 0.004, 0.05, 1600, 800, 0.1, 0.004, 0, 0.4));
                }
                else notes.Add(new Synth(b, sr, Hold(15), fs, 0.12, Wave.Saw, 0.35, 0.6, 1300, 0, 0.1, 0.005, 0, 0.6));
                // Arpeggio.
                if (e >= 2)
                {
                    int[] arp = { 0, 2, 4, 7, 4, 2 };
                    for (int st = 0; st < 16; st++)
                    {
                        int k = arp[st % arp.Length];
                        notes.Add(new Synth(At(st), sr, Hold(0.7), new[] { MidiHz(ChordNote(k, 2)) }, 0.07, Wave.Square, 0.001, 0.08, 1200, 3500, 0.06, 0, (st % 2) * 0.6 - 0.3, 0.2, 0.25));
                    }
                }
            }
            else if (Id == StationId.Coast)
            {
                // Seventh chords on the electric piano, sometimes pushed off the beat.
                int push = r.Next() < 0.4 ? 2 : 0;
                int[] voicing = { 0, 2, 4, 6 };
                for (int i = 0; i < voicing.Length; i++)
                    notes.Add(new EPiano(At(push) + i * (long)JsRound(sr * 0.012), sr, Hold(12), MidiHz(ChordNote(voicing[i], 1)), 0.14, -0.3 + i * 0.2, 0.45));
                if (e >= 3 && r.Next() < 0.5)
                    for (int i = 0; i < voicing.Length; i++)
                        notes.Add(new EPiano(At(10) + i * (long)JsRound(sr * 0.01), sr, Hold(5), MidiHz(ChordNote(voicing[i], 1)), 0.09, -0.3 + i * 0.2, 0.45));
            }
            else
            {
                // Power chords: root, fifth, octave through a driven amp.
                if (e >= 2)
                {
                    int rootM = ChordNote(0, 0);
                    var fs = new[] { MidiHz(rootM), MidiHz(rootM + 7), MidiHz(rootM + 12) };
                    int[,] pattern = e >= 3 ? new[,] { { 0, 3 }, { 3, 3 }, { 6, 2 }, { 8, 3 }, { 11, 3 }, { 14, 2 } } : new[,] { { 0, 6 }, { 8, 6 } };
                    for (int p = 0; p < pattern.GetLength(0); p++)
                    {
                        int st = pattern[p, 0], len = pattern[p, 1];
                        notes.Add(new Synth(At(st), sr, Hold(len) * 0.92, fs, 0.2, Wave.Saw, 0.002, 0.05, 2600, 0, 0.1, 0.004, -0.45, 0.12, 0, 0, 7));
                        notes.Add(new Synth(At(st), sr, Hold(len) * 0.92, fs, 0.2, Wave.Saw, 0.003, 0.05, 2500, 0, 0.1, 0.006, 0.45, 0.12, 0, 0, 7));
                    }
                }
                else if (sectionBar % 2 == 0)
                {
                    int rootM = ChordNote(0, 0);
                    notes.Add(new Synth(At(0), sr, Hold(14), new[] { MidiHz(rootM), MidiHz(rootM + 7) }, 0.16, Wave.Saw, 0.01, 0.8, 1800, 0, 0.1, 0.004, 0, 0.3, 0, 0, 4));
                }
            }

            // --- Lead: the motif, on the chord ------------------------------
            if (e >= 3 || (Id == StationId.Coast && e >= 2 && sectionBar % 4 >= 2))
            {
                int half = bar % 2; // which bar of the two-bar motif
                var tune = sec.name == "chorus" || sec.name == "b" ? s.hook : s.motif;
                foreach (var (st, len, d) in tune)
                {
                    int local = st - half * 16;
                    if (local < 0 || local >= 16) continue;
                    int m = s.root + 24 + Degree(s.scale, chordDeg + d);
                    double f = MidiHz(m);
                    if (Id == StationId.Pulse) notes.Add(new Synth(At(local), sr, Hold(len) * 0.9, new[] { f, f * 1.003 }, 0.11, Wave.Saw, 0.01, 0.15, 2400, 1500, 0.2, 0.002, 0.15, 0.3, 0.35, 0.006));
                    else if (Id == StationId.Coast) notes.Add(new Synth(At(local), sr, Hold(len) * 0.85, new[] { f * 2 }, 0.09, Wave.Tri, 0.02, 0.3, 3000, 0, 0.1, 0, 0.2, 0.5, 0.15, 0.008));
                    else notes.Add(new Synth(At(local), sr, Hold(len) * 0.95, new[] { f * 2 }, 0.12, Wave.Saw, 0.004, 0.12, 2800, 800, 0.15, 0, 0.1, 0.25, 0.15, 0.012, 5));
                }
            }
        }
    }
}
