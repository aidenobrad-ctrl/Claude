using System;
using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Server.Bots;
using Pulsebreak.Server.Modes;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Cosmetics;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Net;
using Pulsebreak.Shared.Progression;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Server
{
    [Serializable]
    public sealed class ServerConfig
    {
        /// <summary>The mode, map, team size and limits. Fixed for as long as anyone is connected.</summary>
        public MatchSettings match = MatchSettings.For(GameModeKind.Duel);
        [Tooltip("Send a snapshot every this many ticks. 1 = 60 a second.")]
        [Min(1)] public int snapshotInterval = 1;
        [Tooltip("The furthest back in time a shot may be judged. Caps the advantage a very laggy player gets.")]
        [Range(0f, 0.5f)] public float maxRewindSeconds = 0.3f;
        [Tooltip("Once there are enough players for a match, how long to wait for more before starting. A full match starts at once.")]
        [Min(0f)] public float lobbySeconds = 6f;

        /// <summary>
        /// The secret shared with the backend for checking join tickets. When set, only
        /// players who show a valid ticket get in, and they play under their account's name.
        /// When empty (a match hosted from the menu, or on a LAN) anyone can join under any name.
        ///
        /// A secret does not belong in a scene file, so this is not saved with one: a dedicated
        /// server is given it on the command line or in its environment.
        /// </summary>
        [NonSerialized] public string ticketKey = "";

        /// <summary>
        /// Admit only players whose ticket is good for ranked play. The backend gives those
        /// only to accounts with a verified email. Needs a ticket key.
        /// </summary>
        public bool rankedOnly;
    }

    /// <summary>How a player is placed in the world.</summary>
    public struct SpawnOptions
    {
        /// <summary>Reloads never run the reserve down.</summary>
        public bool UnlimitedAmmo;
        /// <summary>Utility items come back over time.</summary>
        public bool RechargeUtility;
        /// <summary>What of the loadout may be used (see <see cref="MatchLoadouts"/>). 0 means all of it.</summary>
        public byte SlotMask;
        /// <summary>The weapon slot in hand.</summary>
        public int ActiveSlot;
        /// <summary>Carry this rather than the player's own loadout (Gun Game). Null for their own.</summary>
        public LoadoutData Loadout;

        /// <summary>Nobody to fight yet: ammunition and utility never run out.</summary>
        public static SpawnOptions Warmup => new SpawnOptions { UnlimitedAmmo = true, RechargeUtility = true };
        public static SpawnOptions Normal => default;
    }

    /// <summary>
    /// The authority for one match. It owns the only copy of the game that counts: clients
    /// send what their player is pressing, and the server alone decides where everyone is,
    /// what every shot hit and who won.
    ///
    /// That is also the anti-cheat. A client cannot move faster, fire faster, shoot without
    /// ammunition or claim a hit, because none of those are things a client gets to say. It
    /// says "these buttons were down", and the same simulation the client predicted with is
    /// run here on the real state.
    ///
    /// The rules of the match (who is on which team, when people come back, what scores)
    /// belong to a <see cref="GameMode"/>. Empty places can be taken by bots, which are
    /// players without a connection: they send their commands from a <see cref="BotBrain"/>
    /// and are simulated exactly like everyone else.
    ///
    /// Nothing here touches the scene directly. It reaches the physical world through
    /// <see cref="IGameWorld"/>, and the network through <see cref="INetSocket"/>.
    /// </summary>
    public sealed class GameServer : IDisposable
    {
        public const int MaxPlayers = NetProtocol.MaxPlayers;
        const float KillPlaneY = -25f;

        static readonly string[] BotNames =
        {
            "Alder", "Birch", "Cedar", "Dune", "Ember", "Flint", "Garnet", "Hollow", "Indigo", "Juniper", "Kestrel", "Lumen",
        };

        readonly INetSocket _socket;
        readonly IGameWorld _world;
        readonly MovementConfig _movement;
        readonly LoadoutCatalog _catalog;
        readonly ServerConfig _config;
        readonly uint _secret;
        readonly byte[] _ticketKey;
        readonly Func<long> _unixTime;

        readonly ServerPlayer[] _players = new ServerPlayer[MaxPlayers];
        readonly Dictionary<NetAddress, ServerPlayer> _byAddress = new Dictionary<NetAddress, ServerPlayer>();
        readonly ProjectileSystem _projectiles = new ProjectileSystem();
        readonly LagCompensator _lag = new LagCompensator(MaxPlayers);
        readonly Placement[] _placements = new Placement[MaxPlayers];
        ArenaLayout _arena;
        GameMode _mode;
        NavGrid _nav;
        uint _botSeed;

        readonly byte[] _receiveBuffer = new byte[2048];
        readonly NetReader _reader = new NetReader();
        readonly NetWriter _handshake = new NetWriter(64);
        readonly NetWriter _out = new NetWriter(1400);
        readonly PlayerInputCmd[] _inputs = new PlayerInputCmd[NetMessages.MaxInputsPerMessage];
        readonly List<HitReport> _reports = new List<HitReport>(8);
        readonly bool[] _aliveAtTickStart = new bool[MaxPlayers];
        readonly Action<NetReader> _onMessage;
        ServerPlayer _receivingFrom;
        double _now;

        /// <summary>Where and with what a player was last placed, so somebody joining later can be told.</summary>
        struct Placement
        {
            public ArenaSpawn Spawn;
            public SpawnOptions Options;
            public LoadoutData Carried;
        }

        public int Tick { get; private set; }
        public PlayerList Players => new PlayerList(_players);
        public MatchSettings Settings { get; private set; }
        public GameMode Mode => _mode;
        public MatchState Match => _mode.State;
        public ArenaLayout Arena => _arena;
        public LoadoutCatalog Catalog => _catalog;
        /// <summary>Where bots can walk. Built the first time a bot needs it.</summary>
        public NavGrid Nav => _nav ?? (_nav = NavGrid.Build(_arena));
        public NetAddress Address => _socket.LocalAddress;
        /// <summary>The matchmade match this server is running, or empty. When set, only tickets for it are admitted.</summary>
        public string MatchId { get; private set; } = "";
        /// <summary>The matchmade match is ranked: only ranked tickets get in, and there are no bots.</summary>
        public bool Ranked { get; private set; }

        /// <summary>A matchmade match finished. Hand the report to the backend.</summary>
        public event Action<MatchReport> MatchReported;

        // Who matchmaking sent, by account: their side and what they have unlocked.
        readonly Dictionary<string, AssignedPlayer> _assigned = new Dictionary<string, AssignedPlayer>();
        // People who left a matchmade match before it ended, as they stood when they went.
        readonly List<PlayerReport> _leavers = new List<PlayerReport>();
        bool _reported;
        /// <summary>
        /// Once there are enough players, how long to wait for more before starting. A matchmade
        /// match gives the players it was sent longer to load in; it starts at once when they all have.
        /// </summary>
        public float LobbySeconds => MatchId.Length > 0 ? Mathf.Max(_config.lobbySeconds, 20f) : _config.lobbySeconds;

        /// <summary>Things worth a line in the server log.</summary>
        public event Action<string> Logged;

        /// <summary>True if this server only admits players who show a valid ticket from the backend.</summary>
        public bool RequiresTicket => _ticketKey != null;

        /// <summary>People connected, whether or not they have finished joining. Bots do not count.</summary>
        public int HumanCount
        {
            get
            {
                int count = 0;
                foreach (ServerPlayer player in _players)
                {
                    if (player != null && !player.IsBot) count++;
                }
                return count;
            }
        }

        /// <param name="secret">Any number nobody else knows. It keeps the connection handshake from being forged.</param>
        /// <param name="unixTime">Seconds since 1970 (UTC), for judging whether a ticket has expired. Replaceable for tests.</param>
        public GameServer(INetSocket socket, IGameWorld world, MovementConfig movement, LoadoutCatalog catalog,
            ServerConfig config, uint secret, Func<long> unixTime = null)
        {
            _socket = socket;
            _world = world;
            _movement = movement;
            _catalog = catalog;
            _config = config ?? new ServerConfig();
            _secret = secret;
            _botSeed = secret ^ 0xB07B07u;
            _unixTime = unixTime ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            _onMessage = OnMessage;

            if (!string.IsNullOrEmpty(_config.ticketKey))
            {
                _ticketKey = System.Text.Encoding.UTF8.GetBytes(_config.ticketKey);
                if (_ticketKey.Length < GameTicket.MinKeyBytes)
                    throw new ArgumentException("The ticket key must be at least " + GameTicket.MinKeyBytes + " characters, the same value the backend uses.");
            }
            else if (_config.rankedOnly)
            {
                throw new ArgumentException("A ranked server needs a ticket key: without one it cannot tell who has verified their email.");
            }

            _projectiles.Happened += OnProjectileEvent;
            _projectiles.HitConfirmed += OnProjectileHit;
            Configure(_config.match);
        }

        public void Dispose()
        {
            foreach (ServerPlayer player in _players)
            {
                if (player == null) continue;
                if (!player.IsBot) SendDisconnect(player);
                player.Body?.Destroy();
            }
            _socket.Dispose();
        }

        /// <summary>
        /// Makes the rules sensible: limits in range, and a map that has what the mode needs.
        /// The settings passed in are not changed.
        /// </summary>
        public static MatchSettings Prepare(MatchSettings requested)
        {
            MatchSettings settings = (requested ?? MatchSettings.For(GameModeKind.Duel)).Clone();
            if (!Enum.IsDefined(typeof(GameModeKind), settings.mode)) settings.mode = GameModeKind.Duel;
            settings.teamSize = Mathf.Clamp(settings.teamSize, 1, 5);
            settings.maxPlayers = Mathf.Clamp(settings.maxPlayers, 2, MaxPlayers);
            settings.botSkill = Mathf.Clamp(settings.botSkill, 0, 2);
            if (settings.mode == GameModeKind.Duel) settings.teamSize = 1;
            if (settings.mode == GameModeKind.GunGame) settings.scoreLimit = GunGameLadder.Length;

            if (!ArenaLayout.Find(settings.arenaId).Supports(settings) || Array.IndexOf(ArenaLayout.Ids, settings.arenaId) < 0)
            {
                // The requested map cannot hold this match: use the first one that can.
                foreach (string id in ArenaLayout.Ids)
                {
                    if (!ArenaLayout.Find(id).Supports(settings)) continue;
                    settings.arenaId = id;
                    break;
                }
            }
            return settings;
        }

        /// <summary>
        /// Starts over with new rules, possibly on another map. Only while no person is
        /// connected, since everyone in a match has been told its rules; a matchmaking server
        /// is given each match's rules this way. Returns false if someone is here.
        /// </summary>
        /// <param name="matchId">Matchmaking's id for the match, when it is one; only tickets for it get in. Empty for an open match.</param>
        /// <param name="assignment">Who matchmaking is sending, on which side, with what unlocked, and whether it is ranked.</param>
        public bool Configure(MatchSettings settings, string matchId = "", MatchAssignment assignment = null)
        {
            if (_mode != null && HumanCount > 0) return false;
            MatchId = matchId ?? "";
            Ranked = assignment != null && assignment.ranked;
            _assigned.Clear();
            _leavers.Clear();
            _reported = false;
            if (assignment?.players != null)
            {
                foreach (AssignedPlayer player in assignment.players)
                {
                    if (player != null && !string.IsNullOrEmpty(player.accountId)) _assigned[player.accountId] = player;
                }
            }

            for (int i = 0; i < MaxPlayers; i++)
            {
                if (_players[i] != null) DropBot(_players[i]);
            }
            _projectiles.Clear();

            Settings = Prepare(settings);
            if (_arena == null || _arena.Id != Settings.arenaId)
            {
                _arena = ArenaLayout.Find(Settings.arenaId);
                _nav = null;
                bool previous = GameLayers.ServerContext;
                GameLayers.ServerContext = true;
                try
                {
                    _world.BuildArena(_arena);
                }
                finally
                {
                    GameLayers.ServerContext = previous;
                }
            }

            _mode = GameMode.Create(this, _secret ^ (uint)Tick);
            Log("Ready for " + MatchSettings.Title(Settings.mode) + " on " + _arena.DisplayName +
                (Settings.TeamBased ? ", " + Settings.teamSize + "v" + Settings.teamSize : ", up to " + Settings.Capacity + " players") +
                (Settings.fillWithBots ? ", bots filling empty places" : "") + ".");
            return true;
        }

        // ------------------------------------------------------------------ the tick

        /// <summary>One server tick. Call it <see cref="SimConstants.TickRate"/> times a second.</summary>
        /// <param name="now">The current time in seconds, from any steady clock.</param>
        public void Step(double now)
        {
            bool previous = GameLayers.ServerContext;
            GameLayers.ServerContext = true;
            try
            {
                _now = now;
                Tick++;

                ReceivePackets();
                AnnounceLooks();
                BalanceBots();
                ThinkBots();

                // A player who dies this tick still gets the commands they had already sent
                // for it, so two shots that cross in the same tick both count.
                for (int i = 0; i < MaxPlayers; i++) _aliveAtTickStart[i] = _players[i] != null && _players[i].Alive;
                for (int i = 0; i < MaxPlayers; i++)
                {
                    if (_players[i] != null && _players[i].Ready) RunCommands(_players[i]);
                }

                _world.SyncPhysics();
                _projectiles.Step(Tick, SimConstants.TickDelta);

                _mode.Update();
                _lag.Record(Tick, _players);

                if (Tick % _config.snapshotInterval == 0) SendSnapshots();

                for (int i = 0; i < MaxPlayers; i++)
                {
                    ServerPlayer player = _players[i];
                    if (player == null || player.IsBot) continue;
                    player.Connection.Flush(_socket, now);

                    if (player.Connection.Broken) Remove(player, "stopped responding");
                    else if (now - player.Connection.LastReceiveTime > NetProtocol.TimeoutSeconds) Remove(player, "timed out");
                }
            }
            finally
            {
                GameLayers.ServerContext = previous;
            }
        }

        // ------------------------------------------------------------------ connections

        void ReceivePackets()
        {
            // Bounded, so a flood of junk cannot hold a tick up indefinitely.
            for (int packet = 0; packet < 512; packet++)
            {
                if (!_socket.Receive(_receiveBuffer, out int length, out NetAddress from)) break;
                if (length < 1) continue;

                switch ((PacketType)_receiveBuffer[0])
                {
                    case PacketType.ConnectRequest:
                        OnConnectRequest(from, length);
                        break;
                    case PacketType.ChallengeResponse:
                        OnChallengeResponse(from, length);
                        break;
                    case PacketType.Data:
                        if (_byAddress.TryGetValue(from, out ServerPlayer sender))
                        {
                            _receivingFrom = sender;
                            sender.Connection.Receive(_receiveBuffer, length, _now, _onMessage);
                            _receivingFrom = null;
                        }
                        break;
                    case PacketType.Disconnect:
                        _reader.Reset(_receiveBuffer, 1, length - 1);
                        if (_byAddress.TryGetValue(from, out ServerPlayer leaving) &&
                            _reader.ReadUInt() == leaving.Connection.Token && !_reader.Failed)
                            Remove(leaving, "left");
                        break;
                }
            }
        }

        /// <summary>
        /// The number sent back to someone asking to join. It is worked out from their address
        /// rather than stored, so a flood of requests from made-up addresses costs no memory.
        /// </summary>
        uint ChallengeFor(NetAddress from, uint clientSalt) =>
            DeterministicRandom.Combine(DeterministicRandom.Combine(_secret, from.Host),
                DeterministicRandom.Combine(from.Port, clientSalt));

        void OnConnectRequest(NetAddress from, int length)
        {
            // Padded on purpose: the reply is never larger than the request, so the server
            // cannot be used to flood a third party.
            if (length < NetProtocol.ConnectRequestSize) return;
            _reader.Reset(_receiveBuffer, 1, length - 1);
            uint version = _reader.ReadUInt();
            uint clientSalt = _reader.ReadUInt();
            if (_reader.Failed) return;

            _handshake.Reset();
            if (version != NetProtocol.Version)
            {
                _handshake.WriteByte((byte)PacketType.Denied);
                _handshake.WriteUInt(clientSalt);
                _handshake.WriteByte((byte)DenyReason.WrongVersion);
            }
            else
            {
                _handshake.WriteByte((byte)PacketType.Challenge);
                _handshake.WriteUInt(clientSalt);
                _handshake.WriteUInt(ChallengeFor(from, clientSalt));
            }
            _socket.Send(from, _handshake.Data, _handshake.Length);
        }

        void OnChallengeResponse(NetAddress from, int length)
        {
            _reader.Reset(_receiveBuffer, 1, length - 1);
            uint clientSalt = _reader.ReadUInt();
            uint token = _reader.ReadUInt();
            if (_reader.Failed || token != (clientSalt ^ ChallengeFor(from, clientSalt))) return;

            if (!_byAddress.TryGetValue(from, out ServerPlayer player) || player.Connection.Token != token)
            {
                // Same address, new token: they restarted. The old player goes first.
                if (player != null) Remove(player, "reconnected");

                // People take places from bots, never the other way round.
                int slot = HumanCount >= Settings.Capacity ? -1 : Array.IndexOf(_players, null);
                if (slot < 0 && HumanCount < Settings.Capacity)
                {
                    ServerPlayer bot = BotToDrop();
                    if (bot != null)
                    {
                        slot = bot.Id;
                        DropBot(bot);
                    }
                }
                if (slot < 0)
                {
                    _handshake.Reset();
                    _handshake.WriteByte((byte)PacketType.Denied);
                    _handshake.WriteUInt(clientSalt);
                    _handshake.WriteByte((byte)DenyReason.ServerFull);
                    _socket.Send(from, _handshake.Data, _handshake.Length);
                    return;
                }

                player = new ServerPlayer(slot, new NetConnection(from, token, _now));
                Admit(player);
                _byAddress[from] = player;
            }

            // Also the answer to a repeated response, in case the first acceptance was lost.
            _handshake.Reset();
            _handshake.WriteByte((byte)PacketType.Accepted);
            _handshake.WriteUInt(token);
            _handshake.WriteByte((byte)player.Id);
            _socket.Send(from, _handshake.Data, _handshake.Length);
        }

        void Admit(ServerPlayer player)
        {
            player.Vitals.Damaged += OnDamaged;
            player.Vitals.CanBeHurtBy = attacker => _mode.CanHurt(attacker, player);
            _players[player.Id] = player;
            _placements[player.Id] = default;
        }

        void SendDisconnect(ServerPlayer player, DisconnectReason why = DisconnectReason.None)
        {
            _handshake.Reset();
            _handshake.WriteByte((byte)PacketType.Disconnect);
            _handshake.WriteUInt(player.Connection.Token);
            _handshake.WriteByte((byte)why);
            for (int i = 0; i < 3; i++) _socket.Send(player.Connection.Remote, _handshake.Data, _handshake.Length);
        }

        void Remove(ServerPlayer player, string reason, DisconnectReason why = DisconnectReason.None)
        {
            if (_players[player.Id] != player) return;
            _players[player.Id] = null;
            if (!player.IsBot)
            {
                _byAddress.Remove(player.Connection.Remote);
                SendDisconnect(player, why);
            }

            // Leaving a matchmade match part way still counts, as a loss.
            if (!player.IsBot && player.Ready && MatchId.Length > 0 && _mode != null && _mode.Underway)
                _leavers.Add(ReportFor(player, completed: false, MatchOutcome.Lost));

            player.Spawned = false;
            player.Vitals.Clear();
            player.Body?.Destroy();

            if (!player.Ready) return;
            Log(player.Name + " " + reason + ".");
            _out.Reset();
            NetMessages.Write(_out, new PlayerInfoMessage { PlayerId = player.Id, Present = false, Name = player.Name, Team = player.Team, Bot = player.IsBot });
            Broadcast(_out, reliable: true);
            _mode.PlayerLeft(player);
        }

        // ------------------------------------------------------------------ matchmade matches

        /// <summary>
        /// What a player may carry. In a matchmade match, only what they have unlocked (guests:
        /// the starter set); in a private match, anything in the game.
        /// </summary>
        LoadoutData CheckLoadout(ServerPlayer player, LoadoutData loadout)
        {
            if (MatchId.Length == 0) return LoadoutRules.Sanitize(loadout, _catalog);
            _assigned.TryGetValue(player.AccountId ?? "", out AssignedPlayer assigned);
            return UnlockRules.Apply(loadout, _catalog, assigned != null ? PlayerUnlocks.From(assigned) : PlayerUnlocks.Starter);
        }

        /// <summary>The side matchmaking put a player on, or -1 to let the mode choose.</summary>
        public int PresetTeam(ServerPlayer player) =>
            !player.IsBot && _assigned.TryGetValue(player.AccountId ?? "", out AssignedPlayer assigned) ? assigned.team : -1;

        /// <summary>Everyone matchmaking sent has arrived, so a matchmade match need not wait any longer.</summary>
        public bool EveryoneAssignedIsHere
        {
            get
            {
                if (_assigned.Count == 0) return false;
                int here = 0;
                foreach (ServerPlayer player in _players)
                {
                    if (player != null && player.Ready && !player.IsBot && _assigned.ContainsKey(player.AccountId ?? "")) here++;
                }
                return here >= _assigned.Count;
            }
        }

        /// <summary>Called by the mode when the match is decided. In a matchmade match, reports it, once.</summary>
        public void MatchFinished()
        {
            if (MatchId.Length == 0 || _reported) return;
            _reported = true;

            var players = new List<PlayerReport>();
            var present = new HashSet<string>();
            foreach (ServerPlayer player in _players)
            {
                if (player == null || player.IsBot || !player.Ready) continue;
                players.Add(ReportFor(player, completed: true, _mode.OutcomeFor(player.Team, player.Id)));
                present.Add(player.AccountId);
            }
            foreach (PlayerReport leaver in _leavers)
            {
                // Someone who left and came back is reported as they finished.
                if (present.Add(leaver.accountId)) players.Add(leaver);
            }
            _leavers.Clear();

            var report = new MatchReport
            {
                matchId = MatchId,
                durationSeconds = Mathf.Max(0, (Tick - _mode.StartedTick) / SimConstants.TickRate),
                players = players.ToArray(),
            };
            Log("Reporting match " + MatchId + " for " + players.Count + " player(s).");
            MatchReported?.Invoke(report);
        }

        PlayerReport ReportFor(ServerPlayer player, bool completed, MatchOutcome outcome)
        {
            var weapons = new List<WeaponReport>();
            foreach (KeyValuePair<string, (int Kills, int Headshots)> tally in player.WeaponKills)
            {
                WeaponDefinition weapon = _catalog.weapons.FindWeapon(tally.Key);
                weapons.Add(new WeaponReport
                {
                    weaponId = tally.Key,
                    weaponClass = weapon != null ? weapon.weaponClass.ToString() : "",
                    kills = tally.Value.Kills,
                    headshots = tally.Value.Headshots,
                });
            }
            return new PlayerReport
            {
                accountId = player.AccountId ?? "",
                name = player.Name,
                team = player.Team,
                outcome = outcome,
                completed = completed,
                kills = player.Kills,
                deaths = player.Deaths,
                headshots = player.Headshots,
                objective = player.Objective,
                roundsWon = player.RoundsWon,
                score = player.Score,
                weapons = weapons.ToArray(),
            };
        }

        /// <summary>
        /// The end of a matchmade match: everyone is sent back to their menus and the server
        /// waits, empty, for matchmaking to hand it the next one.
        /// </summary>
        public void EndSession()
        {
            for (int i = 0; i < MaxPlayers; i++)
            {
                ServerPlayer player = _players[i];
                if (player != null && !player.IsBot) Remove(player, "went back to the menu", DisconnectReason.MatchOver);
            }
            for (int i = 0; i < MaxPlayers; i++)
            {
                if (_players[i] != null) DropBot(_players[i]);
            }
            _projectiles.Clear();
            _mode = GameMode.Create(this, _secret ^ (uint)Tick);
            Log("Match " + MatchId + " is over; waiting for the next.");
        }

        // ------------------------------------------------------------------ bots

        /// <summary>
        /// Keeps the match at its size with bots, when the rules ask for that: a bot for every
        /// empty place while at least one person is playing, one fewer for every person who
        /// joins, and none at all once the last person has gone.
        /// </summary>
        void BalanceBots()
        {
            int humans = 0, readyHumans = 0, bots = 0;
            foreach (ServerPlayer player in _players)
            {
                if (player == null) continue;
                if (player.IsBot) bots++;
                else
                {
                    humans++;
                    if (player.Ready) readyHumans++;
                }
            }

            int wanted = Settings.fillWithBots && readyHumans > 0 ? Mathf.Max(0, Settings.Capacity - humans) : 0;
            while (bots > wanted)
            {
                ServerPlayer bot = BotToDrop();
                if (bot == null) break;
                DropBot(bot);
                bots--;
            }
            while (bots < wanted && AddBot() != null) bots++;
        }

        ServerPlayer AddBot()
        {
            int slot = Array.IndexOf(_players, null);
            if (slot < 0) return null;

            _botSeed = DeterministicRandom.Combine(_botSeed, (uint)Tick + (uint)slot);
            var bot = new ServerPlayer(slot, null)
            {
                Name = NextBotName(),
                Ready = true,
                Brain = new BotBrain(Settings.botSkill, _botSeed),
                Look = LookRules.Random(_botSeed),
            };
            bot.PendingLoadout = BotLoadout(_botSeed);
            Admit(bot);
            _mode.AssignTeam(bot);
            BroadcastPlayerInfo(bot);
            Log(bot.Name + " joined.");
            _mode.PlayerJoined(bot);
            return bot;
        }

        /// <summary>A bot on the bigger team, so whoever takes its place keeps the teams even.</summary>
        ServerPlayer BotToDrop()
        {
            ServerPlayer best = null;
            int bestSize = -1;
            foreach (ServerPlayer player in _players)
            {
                if (player == null || !player.IsBot) continue;
                int size = player.Team == Teams.None ? 0 : TeamSize(player.Team);
                // Dead bots go first: nobody notices one leave.
                if (!player.Alive) size += 100;
                if (size <= bestSize) continue;
                best = player;
                bestSize = size;
            }
            return best;
        }

        void DropBot(ServerPlayer bot) => Remove(bot, "made room");

        string NextBotName()
        {
            foreach (string name in BotNames)
            {
                string candidate = "Bot " + name;
                bool taken = false;
                foreach (ServerPlayer player in _players)
                {
                    if (player != null && player.Name == candidate) taken = true;
                }
                if (!taken) return candidate;
            }
            return "Bot " + (Tick % 1000);
        }

        /// <summary>The presets bots carry: straightforward guns they handle well. Any that are missing are skipped.</summary>
        static readonly string[] BotPresets = { "assault", "rusher", "marksman", "breacher" };

        LoadoutData BotLoadout(uint seed)
        {
            var usable = new List<LoadoutPreset>(BotPresets.Length);
            foreach (string id in BotPresets)
            {
                LoadoutPreset preset = _catalog.FindPreset(id);
                if (preset != null) usable.Add(preset);
            }
            if (usable.Count == 0) usable.AddRange(_catalog.presets);
            if (usable.Count == 0) return new LoadoutData();
            return LoadoutRules.Sanitize(usable[(int)(seed % (uint)usable.Count)].ToData(), _catalog);
        }

        void ThinkBots()
        {
            for (int i = 0; i < MaxPlayers; i++)
            {
                ServerPlayer bot = _players[i];
                if (bot == null || bot.Brain == null || !bot.Alive) continue;
                PlayerInputCmd cmd = bot.Brain.Think(this, bot);
                cmd.Tick = Tick;
                cmd.ViewTick = Tick;
                MatchRules.Sanitize(ref cmd);
                bot.LastReceivedTick = Tick;
                if (bot.Commands.Count >= ServerPlayer.MaxQueuedCommands) bot.Commands.Dequeue();
                bot.Commands.Enqueue(cmd);
            }
        }

        // ------------------------------------------------------------------ messages in

        void OnMessage(NetReader reader)
        {
            ServerPlayer player = _receivingFrom;
            switch ((NetMessage)reader.ReadByte())
            {
                case NetMessage.Hello:
                    OnHello(player, reader);
                    break;
                case NetMessage.Loadout:
                {
                    LoadoutData loadout = NetMessages.ReadLoadout(reader);
                    if (!reader.Failed && player.Ready) player.PendingLoadout = CheckLoadout(player, loadout);
                    break;
                }
                case NetMessage.Input:
                    OnInput(player, reader);
                    break;
                case NetMessage.Look:
                {
                    CharacterLook look = NetMessages.ReadLook(reader);
                    if (reader.Failed || !player.Ready) break;
                    player.Look = look;
                    player.LookChanged = true;
                    break;
                }
            }
        }

        /// <summary>How often, at most, everyone is told about one player's new look.</summary>
        const int LookIntervalTicks = SimConstants.TickRate / 2;

        /// <summary>
        /// Passes new looks on to everyone. A player changing their look many times a second
        /// is only passed on twice a second: the last look always arrives, nothing in between.
        /// </summary>
        void AnnounceLooks()
        {
            foreach (ServerPlayer player in _players)
            {
                if (player == null || !player.LookChanged || Tick - player.LookSentTick < LookIntervalTicks) continue;
                player.LookChanged = false;
                player.LookSentTick = Tick;
                BroadcastPlayerInfo(player);
            }
        }

        void OnHello(ServerPlayer player, NetReader reader)
        {
            if (player.Ready) return;
            string name = reader.ReadString();
            string ticket = reader.ReadLongString(GameTicket.MaxLength);
            LoadoutData loadout = NetMessages.ReadLoadout(reader);
            CharacterLook look = NetMessages.ReadLook(reader);
            if (reader.Failed) return;

            if (_ticketKey != null)
            {
                // A server run for signed-in players: who they are comes from the backend's
                // signature, not from what the client says its name is.
                if (!GameTicket.TryVerify(ticket, _ticketKey, _unixTime(), out TicketClaims claims, out string problem))
                {
                    Log("Refused " + player.Connection.Remote + ": " + problem + ".");
                    Remove(player, "was refused", DisconnectReason.TicketRequired);
                    return;
                }
                if (MatchId.Length > 0 && claims.MatchId != MatchId)
                {
                    Log("Refused " + claims.Name + ": their ticket is for another match.");
                    Remove(player, "was refused", DisconnectReason.WrongMatch);
                    return;
                }
                if ((_config.rankedOnly || Ranked) && claims.Mode != GameTicket.Ranked)
                {
                    Log("Refused " + claims.Name + ": this server is ranked and their ticket is not.");
                    Remove(player, "was refused", DisconnectReason.RankedOnly);
                    return;
                }
                foreach (ServerPlayer other in _players)
                {
                    if (other == null || other == player || other.IsBot || other.AccountId != claims.AccountId) continue;
                    Log("Refused " + player.Connection.Remote + ": " + claims.Name + " is already in this match.");
                    Remove(player, "was refused", DisconnectReason.AlreadyHere);
                    return;
                }
                player.AccountId = claims.AccountId;
                player.EmailVerified = claims.EmailVerified;
                name = claims.Name;
            }

            // Whatever the client asked for is checked against the server's own content.
            // Anything it cannot have is replaced, exactly as it would be on loading a save.
            name = LoadoutRules.CleanName(name);
            player.Name = name == LoadoutRules.DefaultName ? "Player " + (player.Id + 1) : name;
            player.PendingLoadout = CheckLoadout(player, loadout);
            player.Look = look;
            player.LookSentTick = Tick;
            player.Ready = true;
            _mode.AssignTeam(player);

            _out.Reset();
            NetMessages.Write(_out, new WelcomeMessage { PlayerId = player.Id, ServerTick = Tick, Settings = Settings });
            player.Connection.Send(_out, true);

            foreach (ServerPlayer other in _players)
            {
                if (other == null || !other.Ready) continue;
                WritePlayerInfo(other);
                if (other == player) Broadcast(_out, reliable: true);
                else player.Connection.Send(_out, true);

                if (other == player || !other.Spawned) continue;
                WriteSpawn(other);
                player.Connection.Send(_out, true);
            }

            SendMatchState(player);
            Log(player.Name + " joined from " + player.Connection.Remote + ".");
            _mode.PlayerJoined(player);
        }

        void OnInput(ServerPlayer player, NetReader reader)
        {
            int count = NetMessages.ReadInput(reader, _inputs);
            for (int i = 0; i < count; i++)
            {
                // Each packet repeats the last few commands; only ones not seen before are new.
                if (_inputs[i].Tick <= player.LastReceivedTick) continue;
                player.LastReceivedTick = _inputs[i].Tick;
                if (player.Commands.Count >= ServerPlayer.MaxQueuedCommands) player.Commands.Dequeue();
                player.Commands.Enqueue(_inputs[i]);
            }
        }

        // ------------------------------------------------------------------ simulation

        void RunCommands(ServerPlayer player)
        {
            player.CommandCredit = Mathf.Min(player.CommandCredit + 1f, ServerPlayer.MaxCommandCredit);

            // Normally one command per tick. When a few have piled up after a hiccup, two, so
            // the player catches up without ever being more than a command or two behind.
            int budget = player.Commands.Count >= 3 ? 2 : 1;
            while (budget-- > 0 && player.Commands.Count > 0 && player.CommandCredit >= 1f)
            {
                PlayerInputCmd cmd = player.Commands.Dequeue();
                player.CommandCredit -= 1f;
                player.LastProcessedTick = cmd.Tick;
                if (!player.Spawned || !_aliveAtTickStart[player.Id]) continue;

                if (_mode.Frozen) MatchRules.Freeze(ref cmd);
                player.Sim.Step(cmd, SimConstants.TickDelta);
                player.Body.SetPose(BodyPose.From(player.Sim.Motor.State));
                ResolveShots(player, cmd);
            }

            if (!player.Spawned) return;
            player.Sim.Capture(player.State);
            if (player.Alive && player.State.Motor.Position.y < KillPlaneY) _mode.FellOut(player);
        }

        void ResolveShots(ServerPlayer player, in PlayerInputCmd cmd)
        {
            PlayerSimulation sim = player.Sim;
            WeaponSimulator weapon = sim.SteppedWeapon;
            Vector3 origin = sim.Motor.State.EyePosition;

            if (weapon.Shots.Count > 0)
            {
                WeaponBuild build = sim.SteppedBuild;
                WeaponStats stats = weapon.Stats;
                string weaponId = build.Weapon.weaponId;
                int slot = sim.SteppedSlot;

                // Instant shots are judged against the world the shooter saw. Projectiles are
                // not: they fly in server time, and everyone sees them do it. A bot sees the
                // world as it is, so it never needs to look back.
                bool rewound = false;
                for (int i = 0; i < weapon.Shots.Count && !rewound && !player.IsBot; i++) rewound = weapon.Shots[i].Kind != ShotKind.Projectile;
                if (rewound)
                {
                    float oldest = Tick - _config.maxRewindSeconds * SimConstants.TickRate;
                    _lag.Rewind(player, Mathf.Clamp(cmd.ViewTick, oldest, Tick), _players);
                    _world.SyncPhysics();
                }

                for (int i = 0; i < weapon.Shots.Count; i++)
                {
                    ShotEvent shot = weapon.Shots[i];
                    Vector3 direction = AimMath.Direction(cmd.Yaw + shot.AimOffset.x, cmd.Pitch - shot.AimOffset.y);

                    switch (shot.Kind)
                    {
                        case ShotKind.Bullet:
                        {
                            _reports.Clear();
                            HitscanHit end = CombatResolver.FireBullet(origin, direction, stats, weaponId, shot.DamageScale,
                                Tick, _reports, player.Vitals);
                            for (int r = 0; r < _reports.Count; r++) SendHit(player, _reports[r]);
                            SendShot(player, slot, shot, origin, end.Point, end.Normal, end.HasHit && end.Hitbox == null);
                            break;
                        }

                        case ShotKind.Melee:
                        {
                            if (CombatResolver.MeleeStrike(origin, direction, stats, weaponId, Tick, out HitReport report, player.Vitals))
                                SendHit(player, report);
                            SendShot(player, slot, shot, origin, origin + direction * stats.meleeRange, -direction, false);
                            break;
                        }

                        case ShotKind.Projectile:
                            Launch(player, origin, direction, stats, weaponId, shot.DamageScale, slot);
                            break;
                    }
                }

                if (rewound)
                {
                    _lag.Restore(_players);
                    _world.SyncPhysics();
                }
            }

            int thrown = sim.Belt.ReleasedSlot;
            if (thrown >= 0)
            {
                UtilityDefinition item = sim.Loadout.Utilities[thrown];
                Vector3 direction = AimMath.Direction(cmd.Yaw, cmd.Pitch - item.tossPitch);
                Launch(player, origin, direction, item.projectile, item.utilityId, 1f, ProjectileSpawnMessage.UtilityBase + thrown);
            }
        }

        void Launch(ServerPlayer player, Vector3 origin, Vector3 direction, WeaponStats stats, string weaponId, float damageScale, int source)
        {
            int id = _projectiles.Spawn(origin, direction, stats, weaponId, damageScale, player.Vitals);
            _out.Reset();
            NetMessages.Write(_out, new ProjectileSpawnMessage
            {
                Id = id,
                Owner = player.Id,
                Source = source,
                Origin = origin,
                Direction = direction,
                Tick = Tick,
            });
            Broadcast(_out, reliable: true);
        }

        void OnProjectileEvent(ProjectileEvent projectileEvent)
        {
            // Clients bounce their own drawing of the projectile; they only need to hear how it ended.
            if (projectileEvent.Kind == ProjectileEventKind.Bounce) return;
            _out.Reset();
            NetMessages.Write(_out, new ProjectileEndMessage
            {
                Id = projectileEvent.ProjectileId,
                Kind = projectileEvent.Kind,
                Point = projectileEvent.Point,
                Normal = projectileEvent.Normal,
                Radius = projectileEvent.Radius,
                HitTarget = projectileEvent.HitTarget,
            });
            Broadcast(_out, reliable: true);
        }

        void OnProjectileHit(HitReport report)
        {
            if (report.Attacker is PlayerVitals vitals && _players[vitals.PlayerId] != null)
                SendHit(_players[vitals.PlayerId], report);
        }

        void OnDamaged(PlayerVitals vitals, DamageInfo info, DamageResult result)
        {
            ServerPlayer victim = _players[vitals.PlayerId];
            if (victim == null) return;
            ServerPlayer attacker = info.Attacker is PlayerVitals from ? _players[from.PlayerId] : null;
            if (attacker != null && attacker != victim) victim.Brain?.HurtBy(attacker.Id);
            SendHurt(victim, attacker, info, result);
            if (!result.Killed) return;

            Kill(victim, attacker, info.WeaponId, info.Zone == HitZone.Head);
        }

        // ------------------------------------------------------------------ used by the mode

        /// <summary>
        /// Takes a player out: tells everyone, makes sure they are down, and lets the mode
        /// score it. <paramref name="killer"/> is null when nobody gets the credit.
        /// </summary>
        public void Kill(ServerPlayer victim, ServerPlayer killer, string weaponId, bool headshot)
        {
            victim.Vitals.Clear();
            victim.Body.SetHittable(false);

            _out.Reset();
            NetMessages.Write(_out, new KillMessage
            {
                Killer = killer != null ? killer.Id : KillMessage.NoKiller,
                Victim = victim.Id,
                WeaponId = weaponId ?? "",
                Headshot = headshot,
            });
            Broadcast(_out, reliable: true);
            _mode.OnKill(victim, killer, weaponId ?? "", headshot);
        }

        /// <summary>Places a player in the world with full health and whatever loadout they last asked for.</summary>
        public void Spawn(ServerPlayer player, in ArenaSpawn spawn, in SpawnOptions options)
        {
            if (player.PendingLoadout != null)
            {
                player.LoadoutData = player.PendingLoadout;
                player.PendingLoadout = null;
            }
            LoadoutData carried = options.Loadout ?? player.LoadoutData ?? new LoadoutData();
            byte mask = options.SlotMask == 0 ? MatchLoadouts.AllSlots : options.SlotMask;

            if (player.Sim == null)
            {
                player.Body = _world.CreateBody((player.IsBot ? "ServerBot " : "ServerPlayer ") + player.Id, _movement, withController: true);
                player.Body.SetOwner(player.Vitals);
                player.Sim = new PlayerSimulation(player.Body.Controller, _movement, PlayerSimulation.SeedFor(player.Id));
            }

            player.Sim.InfiniteReserve = options.UnlimitedAmmo;
            player.Sim.RechargeUtilities = options.RechargeUtility;
            player.Sim.SetLoadout(MatchLoadouts.Apply(LoadoutRules.Resolve(carried, _catalog), mask), options.ActiveSlot);
            player.Sim.Motor.Teleport(spawn.Position, spawn.Yaw, 0f);
            player.Sim.Capture(player.State);

            player.Vitals.Max = Settings.maxHealth;
            player.Vitals.Revive();
            player.Spawned = true;
            player.SpawnCount++;
            player.SlotMask = mask;
            player.RespawnTick = -1;
            player.Body.SetHittable(true);
            player.Body.SetPose(BodyPose.From(player.State.Motor));
            player.Brain?.Spawned(this, player);

            _placements[player.Id] = new Placement
            {
                Spawn = spawn,
                Options = new SpawnOptions
                {
                    UnlimitedAmmo = options.UnlimitedAmmo,
                    RechargeUtility = options.RechargeUtility,
                    SlotMask = mask,
                    ActiveSlot = options.ActiveSlot,
                },
                Carried = carried,
            };
            WriteSpawn(player);
            Broadcast(_out, reliable: true);
        }

        /// <summary>Swaps a living player's weapons where they stand, keeping their health and movement (Gun Game).</summary>
        public void Rearm(ServerPlayer player, LoadoutData loadout, byte slotMask, int activeSlot)
        {
            if (!player.Spawned) return;
            slotMask = slotMask == 0 ? MatchLoadouts.AllSlots : slotMask;
            player.Sim.SetLoadout(MatchLoadouts.Apply(LoadoutRules.Resolve(loadout, _catalog), slotMask), activeSlot);
            player.Sim.Capture(player.State);
            player.SpawnCount++;
            player.SlotMask = slotMask;

            ref Placement placement = ref _placements[player.Id];
            placement.Carried = loadout;
            placement.Options.SlotMask = slotMask;
            placement.Options.ActiveSlot = activeSlot;

            _out.Reset();
            NetMessages.Write(_out, new RearmMessage
            {
                PlayerId = player.Id,
                SpawnCount = player.SpawnCount,
                SlotMask = slotMask,
                ActiveSlot = (byte)activeSlot,
                Loadout = loadout,
            });
            Broadcast(_out, reliable: true);
        }

        /// <summary>Takes a player out of the world without a death, until they are placed again.</summary>
        public void Withdraw(ServerPlayer player)
        {
            if (!player.Spawned) return;
            player.Vitals.Clear();
            player.Body.SetHittable(false);
            player.Spawned = false;
        }

        public void ClearProjectiles() => _projectiles.Clear();

        public void BroadcastMatchState()
        {
            _out.Reset();
            NetMessages.Write(_out, _mode.State);
            Broadcast(_out, reliable: true);
        }

        /// <summary>Tells everyone a player's name and team again, after the team changed.</summary>
        public void BroadcastPlayerInfo(ServerPlayer player)
        {
            WritePlayerInfo(player);
            Broadcast(_out, reliable: true);
        }

        /// <summary>Players on a team, bots included.</summary>
        public int TeamSize(int team)
        {
            int count = 0;
            foreach (ServerPlayer player in _players)
            {
                if (player != null && player.Ready && player.Team == team) count++;
            }
            return count;
        }

        public void Log(string message) => Logged?.Invoke(message);

        // ------------------------------------------------------------------ messages out

        void WritePlayerInfo(ServerPlayer player)
        {
            _out.Reset();
            NetMessages.Write(_out, new PlayerInfoMessage
            {
                PlayerId = player.Id,
                Present = true,
                Name = player.Name,
                Team = player.Team,
                Bot = player.IsBot,
                Look = player.Look,
            });
        }

        void WriteSpawn(ServerPlayer player)
        {
            Placement placement = _placements[player.Id];
            _out.Reset();
            NetMessages.Write(_out, new SpawnMessage
            {
                PlayerId = player.Id,
                SpawnCount = player.SpawnCount,
                UnlimitedAmmo = placement.Options.UnlimitedAmmo,
                RechargeUtility = placement.Options.RechargeUtility,
                SlotMask = placement.Options.SlotMask,
                ActiveSlot = (byte)placement.Options.ActiveSlot,
                Loadout = placement.Carried ?? new LoadoutData(),
                Position = placement.Spawn.Position,
                Yaw = placement.Spawn.Yaw,
            });
        }

        void SendMatchState(ServerPlayer player)
        {
            _out.Reset();
            NetMessages.Write(_out, _mode.State);
            player.Connection.Send(_out, true);
        }

        void SendHit(ServerPlayer attacker, in HitReport report)
        {
            if (attacker.IsBot) return;
            _out.Reset();
            NetMessages.Write(_out, new HitMessage
            {
                Victim = report.Target is PlayerVitals target ? target.PlayerId : 255,
                Damage = report.Damage,
                Zone = report.Zone,
                Killed = report.Killed,
                Point = report.Point,
            });
            attacker.Connection.Send(_out, true);
        }

        /// <summary>Tells a person they were hurt and from where. A shot's source is back along its path; an explosion's is its centre.</summary>
        void SendHurt(ServerPlayer victim, ServerPlayer attacker, in DamageInfo info, in DamageResult result)
        {
            if (victim.IsBot || (result.Applied <= 0f && !result.Killed)) return;
            Vector3 source = info.Direction.sqrMagnitude > 0.0001f ? info.Point - info.Direction.normalized * info.Distance : info.Point;
            _out.Reset();
            NetMessages.Write(_out, new HurtMessage
            {
                Attacker = attacker != null ? attacker.Id : HurtMessage.NoAttacker,
                Damage = result.Applied,
                Zone = info.Zone,
                Killed = result.Killed,
                Source = source,
            });
            victim.Connection.Send(_out, true);
        }

        void SendShot(ServerPlayer shooter, int slot, in ShotEvent shot, Vector3 origin, Vector3 end, Vector3 normal, bool hitWorld)
        {
            _out.Reset();
            NetMessages.Write(_out, new ShotMessage
            {
                Shooter = shooter.Id,
                Slot = slot,
                Kind = shot.Kind,
                First = shot.Pellet == 0,
                Origin = origin,
                End = end,
                Normal = normal,
                HitWorld = hitWorld,
            });

            // The shooter drew their own shot the moment they fired.
            foreach (ServerPlayer other in _players)
            {
                if (other != null && other != shooter && other.Ready && !other.IsBot) other.Connection.Send(_out, false);
            }
        }

        void Broadcast(NetWriter message, bool reliable)
        {
            foreach (ServerPlayer player in _players)
            {
                if (player != null && player.Ready && !player.IsBot) player.Connection.Send(message, reliable);
            }
        }

        void SendSnapshots()
        {
            int count = 0;
            foreach (ServerPlayer player in _players)
            {
                if (player != null && player.Ready) count++;
            }

            foreach (ServerPlayer receiver in _players)
            {
                if (receiver == null || !receiver.Ready || receiver.IsBot) continue;

                _out.Reset();
                NetMessages.WriteSnapshotHeader(_out, Tick, count, _mode.Zones, _mode.ZoneCount);
                foreach (ServerPlayer player in _players)
                {
                    if (player == null || !player.Ready) continue;
                    // Everyone gets where everyone is. Only the player themselves gets the rest:
                    // nobody is told an opponent's ammunition or cooldowns.
                    bool own = player == receiver && player.Spawned;
                    NetMessages.WriteSnapshotPlayer(_out, player.Id, player.Spawned, player.Alive, player.SpawnCount,
                        player.Vitals.Current, player.State.Motor, player.State.ActiveSlot, own ? player.State : null,
                        player.LastProcessedTick, player.Stats);
                }
                receiver.Connection.Send(_out, false);
            }
        }
    }
}
