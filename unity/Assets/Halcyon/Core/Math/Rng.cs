// Seeded, deterministic random numbers and integer hashes, ported from the
// web version's src/engine/rng.ts (identical outputs for identical seeds).
using System;

namespace Halcyon.Core
{
    public static class Hash
    {
        /// <summary>FNV-1a 32-bit hash of a string (UTF-16 code units, like JavaScript).</summary>
        public static uint HashString(string s)
        {
            uint h = 0x811c9dc5;
            unchecked
            {
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 0x01000193;
                }
            }
            return h;
        }

        /// <summary>Murmur3 finalizer: a good 32-bit integer mixer.</summary>
        public static uint Mix32(uint x)
        {
            unchecked
            {
                x ^= x >> 16;
                x *= 0x85ebca6b;
                x ^= x >> 13;
                x *= 0xc2b2ae35;
                x ^= x >> 16;
                return x;
            }
        }

        /// <summary>JavaScript's x | 0 for an integral double (wraps modulo 2^32).</summary>
        public static int ToInt32(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            double t = Math.Truncate(v);
            double m = t % 4294967296.0;
            if (m < 0) m += 4294967296.0;
            return unchecked((int)(uint)m);
        }

        /// <summary>Hash of two integers and a seed, as an unsigned 32-bit integer.</summary>
        public static uint Hash2i(int x, int y, uint seed)
        {
            unchecked
            {
                return Mix32((uint)(x * 0x27d4eb2d) ^ (uint)(y * 0x165667b1) ^ Mix32(seed));
            }
        }

        /// <summary>Hash of two integers and a seed, mapped to [0, 1).</summary>
        public static double Hash2f(int x, int y, uint seed) => Hash2i(x, y, seed) / 4294967296.0;

        /// <summary>Hash of one integer and a seed, mapped to [0, 1).</summary>
        public static double Hash1f(int x, uint seed)
        {
            unchecked
            {
                return Mix32((uint)(x * (int)0x9e3779b1) ^ Mix32(seed)) / 4294967296.0;
            }
        }
    }

    /// <summary>sfc32 generator (fast, small state, passes PractRand to large sizes).</summary>
    public sealed class Rng
    {
        uint a, b, c, d;

        public Rng(uint seed = 1) { Seed(seed); }
        public Rng(string seed) { Seed(Hash.HashString(seed)); }

        public void Seed(uint seed)
        {
            uint s = seed;
            uint Splitmix()
            {
                unchecked
                {
                    s += 0x9e3779b9;
                    uint z = s;
                    z = (z ^ (z >> 16)) * 0x85ebca6b;
                    z = (z ^ (z >> 13)) * 0xc2b2ae35;
                    return z ^ (z >> 16);
                }
            }
            a = Splitmix();
            b = Splitmix();
            c = Splitmix();
            d = Splitmix();
            for (int i = 0; i < 12; i++) U32();
        }

        public uint U32()
        {
            unchecked
            {
                uint t = a + b + d;
                d += 1;
                a = b ^ (b >> 9);
                b = c + (c << 3);
                c = (c << 21) | (c >> 11);
                c += t;
                return t;
            }
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double Next() => U32() / 4294967296.0;

        public double Range(double min, double max) => min + (max - min) * Next();

        /// <summary>Integer in [min, maxExclusive).</summary>
        public int Int(int min, int maxExclusive) => min + (int)Math.Floor(Next() * (maxExclusive - min));

        public bool Chance(double p) => Next() < p;

        public T Pick<T>(T[] arr) => arr[(int)Math.Floor(Next() * arr.Length)];

        /// <summary>Normal distribution (Box-Muller).</summary>
        public double Gauss(double mean = 0, double sd = 1)
        {
            double u = 1 - Next();
            double v = Next();
            return mean + sd * Math.Sqrt(-2 * DMath.Log(u)) * DMath.Cos(2 * Math.PI * v);
        }

        /// <summary>An independent generator derived from this one's state and a label.</summary>
        public Rng Fork(uint label) => new Rng(Hash.Mix32(a ^ Hash.Mix32(b ^ label)) ^ c);
        public Rng Fork(string label) => Fork(Hash.HashString(label));

        public uint[] GetState() => new[] { a, b, c, d };

        public void SetState(uint[] s)
        {
            a = s[0]; b = s[1]; c = s[2]; d = s[3];
        }
    }
}
