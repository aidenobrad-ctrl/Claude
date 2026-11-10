using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Shared.Match;

namespace Pulsebreak.Server
{
    /// <summary>
    /// Remembers where every player's body was over the last second, so a shot can be judged
    /// against what the shooter was looking at when they fired.
    ///
    /// A client always sees other players a little in the past: the snapshot had to travel,
    /// and it is drawn slightly late on purpose so there are always two to blend between.
    /// Without this, anyone with a ping would have to aim ahead of a moving target. With it,
    /// the server puts the targets back where they were on the shooter's screen, traces the
    /// shot, and puts them forward again.
    /// </summary>
    public sealed class LagCompensator
    {
        const int History = 64;

        struct Sample
        {
            public int Tick;
            public BodyPose Pose;
            public bool Hittable;
            public byte SpawnCount;
        }

        readonly Sample[][] _records;

        public LagCompensator(int maxPlayers)
        {
            _records = new Sample[maxPlayers][];
            for (int i = 0; i < maxPlayers; i++)
            {
                _records[i] = new Sample[History];
                for (int j = 0; j < History; j++) _records[i][j].Tick = int.MinValue;
            }
        }

        /// <summary>Call at the end of every server tick.</summary>
        public void Record(int tick, IReadOnlyList<ServerPlayer> players)
        {
            for (int i = 0; i < players.Count; i++)
            {
                ServerPlayer player = players[i];
                ref Sample record = ref _records[i][tick & (History - 1)];
                record.Tick = tick;
                record.Hittable = player != null && player.Alive;
                if (!record.Hittable) continue;
                record.Pose = BodyPose.From(player.Sim.Motor.State);
                record.SpawnCount = player.SpawnCount;
            }
        }

        /// <summary>
        /// Poses everyone except the shooter as they were at <paramref name="viewTick"/>,
        /// which may fall between two ticks. Follow with <see cref="Restore"/>.
        /// </summary>
        public void Rewind(ServerPlayer shooter, float viewTick, IReadOnlyList<ServerPlayer> players)
        {
            int earlier = Mathf.FloorToInt(viewTick);
            float blend = viewTick - earlier;

            for (int i = 0; i < players.Count; i++)
            {
                ServerPlayer player = players[i];
                if (player == null || player == shooter || !player.Spawned) continue;

                bool hasA = Find(i, earlier, out Sample a);
                bool hasB = Find(i, earlier + 1, out Sample b);
                if (!hasA && !hasB) continue;            // nothing that old: leave them where they are
                if (!hasA) a = b;
                if (!hasB) b = a;

                // Dead then, or placed somewhere new in between: use whichever side is real, and never blend across.
                if (!a.Hittable && !b.Hittable)
                {
                    player.Body.SetHittable(false);
                    continue;
                }
                if (!a.Hittable) a = b;
                if (!b.Hittable || a.SpawnCount != b.SpawnCount) b = a;

                player.Body.SetHittable(player.Alive);
                player.Body.SetPose(BodyPose.Lerp(a.Pose, b.Pose, blend));
            }
        }

        /// <summary>Puts everyone back where they are now.</summary>
        public void Restore(IReadOnlyList<ServerPlayer> players)
        {
            for (int i = 0; i < players.Count; i++)
            {
                ServerPlayer player = players[i];
                if (player == null || !player.Spawned) continue;
                player.Body.SetHittable(player.Alive);
                player.Body.SetPose(BodyPose.From(player.Sim.Motor.State));
            }
        }

        bool Find(int player, int tick, out Sample record)
        {
            record = _records[player][tick & (History - 1)];
            return record.Tick == tick;
        }
    }
}
