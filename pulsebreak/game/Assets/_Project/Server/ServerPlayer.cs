using System.Collections.Generic;
using Pulsebreak.Server.Bots;
using Pulsebreak.Shared.Cosmetics;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Net;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Server
{
    /// <summary>Everything the server keeps about one player, connected or a bot.</summary>
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
        /// <summary>Null for a bot.</summary>
        public readonly NetConnection Connection;
        public readonly PlayerVitals Vitals;
        public readonly Queue<PlayerInputCmd> Commands = new Queue<PlayerInputCmd>(MaxQueuedCommands);
        public readonly PlayerSimState State = new PlayerSimState();

        public string Name = "";
        /// <summary>The backend account this player proved they own, or empty on a server that does not ask.</summary>
        public string AccountId = "";
        /// <summary>Lines of chat this player may still send before slowing down, and when the allowance last grew.</summary>
        public int ChatAllowance = 5;
        public int ChatRefillTick;
        public bool EmailVerified;
        /// <summary>Has said hello: has a name and a loadout, and takes part in the match. A bot is ready from the start.</summary>
        public bool Ready;
        /// <summary>How they look. Only ever drawn; it changes nothing about play.</summary>
        public CharacterLook Look = new CharacterLook();
        /// <summary>A new look arrived that everyone has not been told about yet.</summary>
        public bool LookChanged;
        /// <summary>The tick everyone was last told about their look, to keep a client from flooding everyone with changes.</summary>
        public int LookSentTick = int.MinValue;

        public PlayerSimulation Sim;
        public ICharacterBody Body;

        /// <summary>What they are carrying now.</summary>
        public LoadoutData LoadoutData;
        /// <summary>What they asked to carry from the next time they are placed.</summary>
        public LoadoutData PendingLoadout;
        /// <summary>What of <see cref="LoadoutData"/> they may use: see <see cref="MatchLoadouts"/>.</summary>
        public byte SlotMask = MatchLoadouts.AllSlots;

        /// <summary>In the world. A ready player is not spawned until the match places them.</summary>
        public bool Spawned;
        /// <summary>Goes up each time they are placed or rearmed. Lets a client tell snapshots from before and after apart.</summary>
        public byte SpawnCount;

        // The match's view of them.
        /// <summary>0 or 1, or <see cref="Teams.None"/> in a mode without teams.</summary>
        public int Team = Teams.None;
        public int Kills, Deaths, Score;
        /// <summary>Kills that were headshots.</summary>
        public int Headshots;
        /// <summary>Points from zones: taking them and holding them.</summary>
        public int Objective;
        /// <summary>Rounds their side won while they were in the match.</summary>
        public int RoundsWon;
        /// <summary>Kills (and how many were headshots) with each weapon, for weapon XP.</summary>
        public readonly Dictionary<string, (int Kills, int Headshots)> WeaponKills = new Dictionary<string, (int, int)>();
        /// <summary>Gun Game: how far up the ladder they are.</summary>
        public int GunLevel;
        /// <summary>Respawning modes: the tick they come back at, or -1 if they are not waiting to.</summary>
        public int RespawnTick = -1;

        /// <summary>Drives a bot. Null for a person.</summary>
        public BotBrain Brain;

        public int LastReceivedTick = int.MinValue;
        public int LastProcessedTick;
        public float CommandCredit;

        public bool IsBot => Connection == null;
        public bool Alive => Spawned && Vitals.IsAlive;
        public PlayerStats Stats => new PlayerStats { Team = Team, Kills = Kills, Deaths = Deaths, Score = Score };

        public ServerPlayer(int id, NetConnection connection)
        {
            Id = id;
            Connection = connection;
            Vitals = new PlayerVitals(id);
        }

        /// <summary>A fresh match: everything the scoreboard shows goes back to zero.</summary>
        public void ResetStats()
        {
            Kills = Deaths = Score = Headshots = Objective = RoundsWon = 0;
            WeaponKills.Clear();
            GunLevel = 0;
            RespawnTick = -1;
        }
    }
}
