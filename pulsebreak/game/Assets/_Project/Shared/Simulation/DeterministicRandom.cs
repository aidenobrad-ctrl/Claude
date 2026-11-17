namespace Pulsebreak.Shared.Simulation
{
    /// <summary>
    /// Small seedable generator (xorshift32). Bullet spread uses it instead of
    /// <c>UnityEngine.Random</c> so that the client and the server, given the same seed,
    /// produce the same shot. That is what lets the server check a client's hits.
    /// </summary>
    public struct DeterministicRandom
    {
        uint _state;

        public DeterministicRandom(uint seed)
        {
            _state = Scramble(seed);
            if (_state == 0) _state = 0x9E3779B9u;
        }

        /// <summary>Mixes two values into one seed, for example a weapon seed and a shot number.</summary>
        public static uint Combine(uint a, uint b)
        {
            unchecked
            {
                return Scramble(a ^ (b + 0x9E3779B9u + (a << 6) + (a >> 2)));
            }
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>Uniform float in [-1, 1).</summary>
        public float NextSigned() => NextFloat() * 2f - 1f;

        static uint Scramble(uint x)
        {
            unchecked
            {
                x ^= x >> 16;
                x *= 0x7FEB352Du;
                x ^= x >> 15;
                x *= 0x846CA68Bu;
                x ^= x >> 16;
                return x;
            }
        }
    }
}
