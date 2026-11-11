using System;
using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Net;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Server
{
    [Serializable]
    public sealed class ServerConfig
    {
        public string arenaId = ArenaLayout.DefaultId;
        public DuelConfig duel = new DuelConfig();
        [Tooltip("Send a snapshot every this many ticks. 1 = 60 a second.")]
        [Min(1)] public int snapshotInterval = 1;
        [Tooltip("The furthest back in time a shot may be judged. Caps the advantage a very laggy player gets.")]
        [Range(0f, 0.5f)] public float maxRewindSeconds = 0.3f;

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
    /// Nothing here touches the scene directly. It reaches the physical world through
    /// <see cref="IGameWorld"/>, and the network through <see cref="INetSocket"/>.
    /// </summary>
    public sealed class GameServer : IDisposable
    {
        public const int MaxPlayers = NetProtocol.MaxPlayers;
        const float KillPlaneY = -25f;

        readonly INetSocket _socket;
        readonly IGameWorld _world;
        readonly MovementConfig _movement;
        readonly LoadoutCatalog _catalog;
        readonly ServerConfig _config;
        readonly ArenaLayout _arena;
        readonly uint _secret;
        readonly byte[] _ticketKey;
        readonly Func<long> _unixTime;

        readonly ServerPlayer[] _players = new ServerPlayer[MaxPlayers];
        readonly Dictionary<NetAddress, ServerPlayer> _byAddress = new Dictionary<NetAddress, ServerPlayer>();
        readonly ProjectileSystem _projectiles = new ProjectileSystem();
        readonly LagCompensator _lag = new LagCompensator(MaxPlayers);
        readonly DuelMatch _match;

        readonly byte[] _receiveBuffer = new byte[2048];
        readonly NetReader _reader = new NetReader();
        readonly NetWriter _handshake = new NetWriter(64);
        readonly NetWriter _out = new NetWriter(1024);
        readonly PlayerInputCmd[] _inputs = new PlayerInputCmd[NetMessages.MaxInputsPerMessage];
        readonly List<HitReport> _reports = new List<HitReport>(8);
        readonly bool[] _aliveAtTickStart = new bool[MaxPlayers];
        readonly Action<NetReader> _onMessage;
        ServerPlayer _receivingFrom;
        double _now;

        public int Tick { get; private set; }
        public IReadOnlyList<ServerPlayer> Players => _players;
        public DuelMatch Match => _match;
        public ArenaLayout Arena => _arena;
        public DuelConfig Rules => _config.duel;
        public NetAddress Address => _socket.LocalAddress;

        /// <summary>Things worth a line in the server log.</summary>
        public event Action<string> Logged;

        /// <summary>True if this server only admits players who show a valid ticket from the backend.</summary>
        public bool RequiresTicket => _ticketKey != null;

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

            _arena = ArenaLayout.Find(_config.arenaId);
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

            _projectiles.Happened += OnProjectileEvent;
            _projectiles.HitConfirmed += OnProjectileHit;
            _match = new DuelMatch(this, secret);
        }

        public void Dispose()
        {
            foreach (ServerPlayer player in _players)
            {
                if (player == null) continue;
                SendDisconnect(player);
                player.Body?.Destroy();
            }
            _socket.Dispose();
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

                // A player who dies this tick still gets the commands they had already sent
                // for it, so two shots that cross in the same tick both count.
                for (int i = 0; i < MaxPlayers; i++) _aliveAtTickStart[i] = _players[i] != null && _players[i].Alive;
                for (int i = 0; i < MaxPlayers; i++)
                {
                    if (_players[i] != null && _players[i].Ready) RunCommands(_players[i]);
                }

                _world.SyncPhysics();
                _projectiles.Step(Tick, SimConstants.TickDelta);

                _match.Update();
                _lag.Record(Tick, _players);

                if (Tick % _config.snapshotInterval == 0) SendSnapshots();

                for (int i = 0; i < MaxPlayers; i++)
                {
                    ServerPlayer player = _players[i];
                    if (player == null) continue;
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

                int slot = Array.IndexOf(_players, null);
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
                player.Vitals.Damaged += OnDamaged;
                _players[slot] = player;
                _byAddress[from] = player;
            }

            // Also the answer to a repeated response, in case the first acceptance was lost.
            _handshake.Reset();
            _handshake.WriteByte((byte)PacketType.Accepted);
            _handshake.WriteUInt(token);
            _handshake.WriteByte((byte)player.Id);
            _socket.Send(from, _handshake.Data, _handshake.Length);
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
            _byAddress.Remove(player.Connection.Remote);
            SendDisconnect(player, why);

            player.Spawned = false;
            player.Vitals.Clear();
            player.Body?.Destroy();

            if (!player.Ready) return;
            Log(player.Name + " " + reason + ".");
            _out.Reset();
            NetMessages.Write(_out, new PlayerInfoMessage { PlayerId = player.Id, Present = false, Name = player.Name });
            Broadcast(_out, reliable: true);
            _match.PlayerLeft(player);
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
                    if (!reader.Failed && player.Ready) player.PendingLoadout = LoadoutRules.Sanitize(loadout, _catalog);
                    break;
                }
                case NetMessage.Input:
                    OnInput(player, reader);
                    break;
            }
        }

        void OnHello(ServerPlayer player, NetReader reader)
        {
            if (player.Ready) return;
            string name = reader.ReadString();
            string ticket = reader.ReadLongString(GameTicket.MaxLength);
            LoadoutData loadout = NetMessages.ReadLoadout(reader);
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
                if (_config.rankedOnly && claims.Mode != GameTicket.Ranked)
                {
                    Log("Refused " + claims.Name + ": this server is ranked and their ticket is not.");
                    Remove(player, "was refused", DisconnectReason.RankedOnly);
                    return;
                }
                foreach (ServerPlayer other in _players)
                {
                    if (other == null || other == player || other.AccountId != claims.AccountId) continue;
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
            player.PendingLoadout = LoadoutRules.Sanitize(loadout, _catalog);
            player.Ready = true;

            _out.Reset();
            NetMessages.Write(_out, new WelcomeMessage { PlayerId = player.Id, ArenaId = _arena.Id, ServerTick = Tick, Config = Rules });
            player.Connection.Send(_out, true);

            foreach (ServerPlayer other in _players)
            {
                if (other == null || !other.Ready) continue;
                _out.Reset();
                NetMessages.Write(_out, new PlayerInfoMessage { PlayerId = other.Id, Present = true, Name = other.Name });
                if (other == player) Broadcast(_out, reliable: true);
                else player.Connection.Send(_out, true);

                if (other == player || !other.Spawned) continue;
                WriteSpawn(other);
                player.Connection.Send(_out, true);
            }

            SendMatchState(player);
            Log(player.Name + " joined from " + player.Connection.Remote + ".");
            _match.PlayerJoined(player);
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

                if (_match.Frozen) MatchRules.Freeze(ref cmd);
                player.Sim.Step(cmd, SimConstants.TickDelta);
                player.Body.SetPose(BodyPose.From(player.Sim.Motor.State));
                ResolveShots(player, cmd);
            }

            if (!player.Spawned) return;
            player.Sim.Capture(player.State);
            if (player.Alive && player.State.Motor.Position.y < KillPlaneY) _match.FellOut(player);
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
                // not: they fly in server time, and everyone sees them do it.
                bool rewound = false;
                for (int i = 0; i < weapon.Shots.Count && !rewound; i++) rewound = weapon.Shots[i].Kind != ShotKind.Projectile;
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
            if (!result.Killed) return;
            ServerPlayer victim = _players[vitals.PlayerId];
            if (victim == null) return;

            var killer = info.Attacker as PlayerVitals;
            AnnounceDeath(victim, killer != null ? killer.PlayerId : KillMessage.NoKiller, info.WeaponId, info.Zone == HitZone.Head);
        }

        // ------------------------------------------------------------------ used by the match

        /// <summary>Places a player in the world with full health and whatever loadout they last asked for.</summary>
        /// <param name="warmup">Nobody to fight yet: ammunition and utility never run out.</param>
        public void Spawn(ServerPlayer player, in ArenaSpawn spawn, bool warmup)
        {
            if (player.PendingLoadout != null)
            {
                player.LoadoutData = player.PendingLoadout;
                player.PendingLoadout = null;
            }

            if (player.Sim == null)
            {
                player.Body = _world.CreateBody("ServerPlayer " + player.Id, _movement, withController: true);
                player.Body.SetOwner(player.Vitals);
                player.Sim = new PlayerSimulation(player.Body.Controller, _movement, PlayerSimulation.SeedFor(player.Id));
            }

            player.Sim.InfiniteReserve = warmup;
            player.Sim.RechargeUtilities = warmup;
            player.Sim.SetLoadout(LoadoutRules.Resolve(player.LoadoutData, _catalog), 0);
            player.Sim.Motor.Teleport(spawn.Position, spawn.Yaw, 0f);
            player.Sim.Capture(player.State);

            player.Vitals.Max = Rules.maxHealth;
            player.Vitals.Revive();
            player.Spawned = true;
            player.SpawnCount++;
            player.Body.SetHittable(true);
            player.Body.SetPose(BodyPose.From(player.State.Motor));

            _spawnWarmup[player.Id] = warmup;
            _spawnPoint[player.Id] = spawn;
            WriteSpawn(player);
            Broadcast(_out, reliable: true);
        }

        readonly bool[] _spawnWarmup = new bool[MaxPlayers];
        readonly ArenaSpawn[] _spawnPoint = new ArenaSpawn[MaxPlayers];

        void WriteSpawn(ServerPlayer player)
        {
            _out.Reset();
            NetMessages.Write(_out, new SpawnMessage
            {
                PlayerId = player.Id,
                SpawnCount = player.SpawnCount,
                Warmup = _spawnWarmup[player.Id],
                Loadout = player.LoadoutData,
                Position = _spawnPoint[player.Id].Position,
                Yaw = _spawnPoint[player.Id].Yaw,
            });
        }

        /// <summary>Tells everyone a player is out, and makes sure they are.</summary>
        public void AnnounceDeath(ServerPlayer victim, int killerId, string weaponId, bool headshot)
        {
            victim.Vitals.Clear();
            victim.Body.SetHittable(false);

            _out.Reset();
            NetMessages.Write(_out, new KillMessage
            {
                Killer = killerId,
                Victim = victim.Id,
                WeaponId = weaponId ?? "",
                Headshot = headshot,
            });
            Broadcast(_out, reliable: true);
        }

        public void ClearProjectiles() => _projectiles.Clear();

        public void BroadcastMatchState()
        {
            _out.Reset();
            NetMessages.Write(_out, _match.State);
            Broadcast(_out, reliable: true);
        }

        public void Log(string message) => Logged?.Invoke(message);

        // ------------------------------------------------------------------ messages out

        void SendMatchState(ServerPlayer player)
        {
            _out.Reset();
            NetMessages.Write(_out, _match.State);
            player.Connection.Send(_out, true);
        }

        void SendHit(ServerPlayer attacker, in HitReport report)
        {
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
                if (other != null && other != shooter && other.Ready) other.Connection.Send(_out, false);
            }
        }

        void Broadcast(NetWriter message, bool reliable)
        {
            foreach (ServerPlayer player in _players)
            {
                if (player != null && player.Ready) player.Connection.Send(message, reliable);
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
                if (receiver == null || !receiver.Ready) continue;

                _out.Reset();
                NetMessages.WriteSnapshotHeader(_out, Tick, count);
                foreach (ServerPlayer player in _players)
                {
                    if (player == null || !player.Ready) continue;
                    // Everyone gets where everyone is. Only the player themselves gets the rest:
                    // nobody is told an opponent's ammunition or cooldowns.
                    bool own = player == receiver && player.Spawned;
                    NetMessages.WriteSnapshotPlayer(_out, player.Id, player.Spawned, player.Alive, player.SpawnCount,
                        player.Vitals.Current, player.State.Motor, player.State.ActiveSlot, own ? player.State : null,
                        player.LastProcessedTick);
                }
                receiver.Connection.Send(_out, false);
            }
        }
    }
}
