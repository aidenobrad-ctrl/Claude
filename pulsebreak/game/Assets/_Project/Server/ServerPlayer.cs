using System.Collections.Generic;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Net;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Server
{
    /// <summary>Everything the server keeps about one connected player.</summary>
    public sealed class ServerPlayer
    {
        /// <summary>How many commands can wait before the oldest is thrown away.</summary>
        public const int MaxQueuedCommands = 32;
        /// <summary>
        /// A player earns one command per server tick and can save up this many. Saving up
        /// lets a burst after a network hiccup through; the cap stops a client that sends
        /// commands faster than real time from moving faster than everyone else.
        /// </summary>
        public const float MaxCommandCredit = 8f;

        public readonly int Id;
        public readonly NetConnection Connection;
        public readonly PlayerVitals Vitals;
        public readonly Queue<PlayerInputCmd> Commands = new Queue<PlayerInputCmd>(MaxQueuedCommands);
        public readonly PlayerSimState State = new PlayerSimState();

        public string Name = "";
        /// <summary>Has said hello: has a name and a loadout, and takes part in the match.</summary>
        public bool Ready;

        public PlayerSimulation Sim;
        public ICharacterBody Body;

        /// <summary>What they are carrying now.</summary>
        public LoadoutData LoadoutData;
        /// <summary>What they asked to carry from the next time they are placed.</summary>
        public LoadoutData PendingLoadout;

        /// <summary>In the world. A ready player is not spawned until the match places them.</summary>
        public bool Spawned;
        /// <summary>Goes up each time they are placed. Lets a client tell snapshots from before and after apart.</summary>
        public byte SpawnCount;

        public int LastReceivedTick = int.MinValue;
        public int LastProcessedTick;
        public float CommandCredit;

        public bool Alive => Spawned && Vitals.IsAlive;

        public ServerPlayer(int id, NetConnection connection)
        {
            Id = id;
            Connection = connection;
            Vitals = new PlayerVitals(id);
        }
    }
}
