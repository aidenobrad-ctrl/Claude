using System.Collections.Generic;
using Pulsebreak.Client.Player;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Client.FiringRange
{
    /// <summary>Running totals for a firing range session.</summary>
    public sealed class RangeStats
    {
        readonly Dictionary<IDamageable, int> _firstHitTick = new Dictionary<IDamageable, int>();

        public int Shots { get; private set; }
        public int Hits { get; private set; }
        public int Headshots { get; private set; }
        public int Kills { get; private set; }
        public float DamageDealt { get; private set; }

        /// <summary>Seconds from the first hit on a target to the kill. Negative until there has been a kill.</summary>
        public float LastTimeToKill { get; private set; } = -1f;

        /// <summary>Goes up whenever anything changes, so the HUD knows when to redraw.</summary>
        public int Version { get; private set; }

        public float Accuracy => Shots > 0 ? (float)Hits / Shots : 0f;
        public float HeadshotRate => Hits > 0 ? (float)Headshots / Hits : 0f;

        public void RecordShot()
        {
            Shots++;
            Version++;
        }

        public void RecordHit(in HitFeedback hit)
        {
            Hits++;
            DamageDealt += hit.Damage;
            if (hit.Zone == HitZone.Head) Headshots++;

            if (hit.Target != null)
            {
                if (!_firstHitTick.TryGetValue(hit.Target, out int firstTick))
                {
                    firstTick = hit.Tick;
                    _firstHitTick[hit.Target] = firstTick;
                }

                if (hit.Killed)
                {
                    Kills++;
                    LastTimeToKill = (hit.Tick - firstTick) * SimConstants.TickDelta;
                    _firstHitTick.Remove(hit.Target);
                }
            }

            Version++;
        }

        public void Reset()
        {
            Shots = Hits = Headshots = Kills = 0;
            DamageDealt = 0f;
            LastTimeToKill = -1f;
            _firstHitTick.Clear();
            Version++;
        }
    }
}
