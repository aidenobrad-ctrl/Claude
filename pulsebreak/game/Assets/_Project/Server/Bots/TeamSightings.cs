using UnityEngine;

namespace Pulsebreak.Server
{
    /// <summary>
    /// Where each team last saw an enemy, and when. A bot that spots someone reports it; a bot
    /// with nothing to do heads for the most recent sighting near enough to matter.
    /// </summary>
    public sealed class TeamSightings
    {
        const int Kept = 6;

        struct Sighting
        {
            public Vector3 Where;
            public int Tick;
        }

        readonly Sighting[][] _teams = { new Sighting[Kept], new Sighting[Kept] };
        readonly int[] _next = new int[2];

        public TeamSightings()
        {
            for (int t = 0; t < 2; t++)
            {
                for (int i = 0; i < Kept; i++) _teams[t][i].Tick = int.MinValue;
            }
        }

        public void Report(int team, Vector3 where, int tick)
        {
            if (team != 0 && team != 1) return;
            _teams[team][_next[team]] = new Sighting { Where = where, Tick = tick };
            _next[team] = (_next[team] + 1) % Kept;
        }

        /// <summary>The freshest sighting no older than <paramref name="maxAgeTicks"/>, preferring nearer ones when they are about as fresh.</summary>
        public bool Latest(int team, int tick, int maxAgeTicks, Vector3 from, out Vector3 where)
        {
            where = default;
            if (team != 0 && team != 1) return false;
            float best = float.MaxValue;
            bool found = false;
            foreach (Sighting sighting in _teams[team])
            {
                int age = tick - sighting.Tick;
                if (sighting.Tick == int.MinValue || age < 0 || age > maxAgeTicks) continue;
                float score = age + Vector3.Distance(from, sighting.Where) * 2f;
                if (score >= best) continue;
                best = score;
                where = sighting.Where;
                found = true;
            }
            return found;
        }

        public void Clear()
        {
            for (int t = 0; t < 2; t++)
            {
                for (int i = 0; i < Kept; i++) _teams[t][i].Tick = int.MinValue;
            }
        }
    }
}
