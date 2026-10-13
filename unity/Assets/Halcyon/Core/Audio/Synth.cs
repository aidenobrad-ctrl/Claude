// Sample-level synthesis building blocks for the procedural audio, ported
// from the web version's src/audio/synth.ts: noise, filters, resonators and
// shapers that render into float arrays. Engine loops, tire and wind beds,
// impacts, UI sounds and the radio are all made from these. Audio is
// presentation, not simulation, so System.Math is fine here; every generator
// is seeded, so a sound renders the same each time (and close to the web's).
using System;

namespace Halcyon.Core.Audio
{
    /// <summary>A small, fast seeded generator (xorshift32), the same sequence as the TypeScript.</summary>
    public sealed class AudioRng
    {
        uint s;

        public AudioRng(long seed)
        {
            s = (uint)(seed & 0xffffffff);
            if (s == 0) s = 0x9e3779b9;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double Next()
        {
            uint x = s;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            s = x;
            return x / 4294967296.0;
        }

        /// <summary>Uniform in [-1, 1).</summary>
        public double Signed() => Next() * 2 - 1;

        /// <summary>Roughly normal, mean 0, deviation 1 (sum of four uniforms).</summary>
        public double Gauss() => (Next() + Next() + Next() + Next() - 2) * 1.7320508;

        /// <summary>An integer in [0, n).</summary>
        public int Int(int n) => (int)(Math.Floor(Next() * n) % n);

        public T Pick<T>(T[] a) => a[Int(a.Length)];
    }

    /// <summary>RBJ-cookbook biquad, transposed direct form II.</summary>
    public sealed class Biquad
    {
        public double b0 = 1, b1, b2, a1, a2;
        double z1, z2;

        public static Biquad Lowpass(double sr, double f, double q = 0.7071) => new Biquad().SetLowpass(sr, f, q);
        public static Biquad Highpass(double sr, double f, double q = 0.7071) => new Biquad().SetHighpass(sr, f, q);
        /// <summary>Band-pass with 0 dB peak gain.</summary>
        public static Biquad Bandpass(double sr, double f, double q) => new Biquad().SetBandpass(sr, f, q);
        public static Biquad Peaking(double sr, double f, double q, double gainDb) => new Biquad().SetPeaking(sr, f, q, gainDb);

        Biquad Set(double nb0, double nb1, double nb2, double na0, double na1, double na2)
        {
            b0 = nb0 / na0;
            b1 = nb1 / na0;
            b2 = nb2 / na0;
            a1 = na1 / na0;
            a2 = na2 / na0;
            return this;
        }

        public Biquad SetLowpass(double sr, double f, double q = 0.7071)
        {
            double w = (2 * Math.PI * Math.Min(f, sr * 0.45)) / sr;
            double c = Math.Cos(w), al = Math.Sin(w) / (2 * q);
            return Set((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + al, -2 * c, 1 - al);
        }

        public Biquad SetHighpass(double sr, double f, double q = 0.7071)
        {
            double w = (2 * Math.PI * Math.Min(f, sr * 0.45)) / sr;
            double c = Math.Cos(w), al = Math.Sin(w) / (2 * q);
            return Set((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + al, -2 * c, 1 - al);
        }

        public Biquad SetBandpass(double sr, double f, double q)
        {
            double w = (2 * Math.PI * Math.Min(f, sr * 0.45)) / sr;
            double c = Math.Cos(w), al = Math.Sin(w) / (2 * q);
            return Set(al, 0, -al, 1 + al, -2 * c, 1 - al);
        }

        public Biquad SetPeaking(double sr, double f, double q, double gainDb)
        {
            double A = Math.Pow(10, gainDb / 40);
            double w = (2 * Math.PI * Math.Min(f, sr * 0.45)) / sr;
            double c = Math.Cos(w), al = Math.Sin(w) / (2 * q);
            return Set(1 + al * A, -2 * c, 1 - al * A, 1 + al / A, -2 * c, 1 - al / A);
        }

        public double Process(double x)
        {
            double y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }

        public void Reset() => z1 = z2 = 0;
    }

    /// <summary>One-pole low-pass (6 dB/octave).</summary>
    public sealed class OnePole
    {
        double a, y;
        public OnePole(double sr, double f) => SetFreq(sr, f);
        public void SetFreq(double sr, double f) => a = 1 - Math.Exp((-2 * Math.PI * f) / sr);
        public double Process(double x)
        {
            y += a * (x - y);
            return y;
        }
    }

    /// <summary>Pink noise (Paul Kellet's economy filter), roughly unit level.</summary>
    public sealed class PinkNoise
    {
        double p0, p1, p2;
        readonly AudioRng rng;
        public PinkNoise(AudioRng rng) => this.rng = rng;
        public double Next()
        {
            double w = rng.Signed();
            p0 = 0.99765 * p0 + w * 0.099046;
            p1 = 0.963 * p1 + w * 0.2965164;
            p2 = 0.57 * p2 + w * 1.0526913;
            return (p0 + p1 + p2 + w * 0.1848) * 0.25;
        }
    }

    /// <summary>Brown (red) noise: integrated white noise with a leak, roughly unit level.</summary>
    public sealed class BrownNoise
    {
        double y;
        readonly AudioRng rng;
        public BrownNoise(AudioRng rng) => this.rng = rng;
        public double Next()
        {
            y = (y + 0.02 * rng.Signed()) * 0.998;
            return y * 3.5;
        }
    }

    public static class Dsp
    {
        /// <summary>Smooth saturation: tanh-like, unity slope at 0, approaches +-1.</summary>
        public static double SoftClip(double x)
        {
            if (x > 3) return 1;
            if (x < -3) return -1;
            double x2 = x * x;
            return (x * (27 + x2)) / (27 + 9 * x2);
        }

        public static double Rms(float[] buf)
        {
            double s = 0;
            for (int i = 0; i < buf.Length; i++) s += (double)buf[i] * buf[i];
            return Math.Sqrt(s / Math.Max(1, buf.Length));
        }

        public static double Peak(float[] buf)
        {
            double p = 0;
            for (int i = 0; i < buf.Length; i++) p = Math.Max(p, Math.Abs(buf[i]));
            return p;
        }

        /// <summary>Scale a buffer in place to the given RMS level (peaks held under `ceiling`).</summary>
        public static float[] Normalize(float[] buf, double targetRms, double ceiling = 0.98)
        {
            double r = Rms(buf);
            if (r <= 1e-9) return buf;
            double g = targetRms / r;
            double p = Peak(buf) * g;
            if (p > ceiling) g *= ceiling / p;
            for (int i = 0; i < buf.Length; i++) buf[i] = (float)(buf[i] * g);
            return buf;
        }

        /// <summary>Fade the ends of a one-shot so it starts and stops without clicks.</summary>
        public static float[] FadeEdges(float[] buf, double sr, double inMs = 2, double outMs = 8)
        {
            int ni = Math.Min(buf.Length, (int)Math.Round((inMs / 1000) * sr));
            int no = Math.Min(buf.Length, (int)Math.Round((outMs / 1000) * sr));
            for (int i = 0; i < ni; i++) buf[i] *= (float)((double)i / ni);
            for (int i = 0; i < no; i++) buf[buf.Length - 1 - i] *= (float)((double)i / no);
            return buf;
        }

        /// <summary>Equal-power crossfade of a noise bed's tail into its head, making it loop.</summary>
        public static float[] CrossfadeLoop(float[] buf, int fade)
        {
            int n = buf.Length - fade;
            var outb = new float[n];
            Array.Copy(buf, outb, n);
            for (int i = 0; i < fade; i++)
            {
                double t = (double)i / fade;
                outb[i] = (float)(buf[i] * Math.Sin(t * Math.PI * 0.5) + buf[n + i] * Math.Cos(t * Math.PI * 0.5));
            }
            return outb;
        }
    }
}
