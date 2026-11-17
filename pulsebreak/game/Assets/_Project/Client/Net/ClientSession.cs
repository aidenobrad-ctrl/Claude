using System;
using UnityEngine;
using Pulsebreak.Shared.Cosmetics;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Net;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Client.Net
{
    public enum ClientStatus
    {
        Idle,
        Connecting,
        Connected,
        /// <summary>Never got in. <see cref="ClientSession.StatusText"/> says why.</summary>
        Failed,
        /// <summary>Was in, and is not any more.</summary>
        Disconnected,
    }

    /// <summary>Where another player is drawn this frame.</summary>
    public struct RemoteView
    {
        public BodyPose Pose;
        public float Pitch;
        public Vector3 Velocity;
        public Stance Stance;
        public int ActiveSlot;
    }

    /// <summary>What a client knows about one player in the match, itself included.</summary>
    public sealed class ClientPlayer
    {
        internal const int SampleCount = 32;

        internal struct Sample
        {
            public int Tick;
            public bool Visible;
            public byte SpawnCount;
            public RemoteView View;
        }

        public readonly int Id;
        public string Name = "";
        /// <summary>In the match at all.</summary>
        public bool Present;
        public bool Spawned;
        public bool Alive;
        public float Health;
        public byte SpawnCount;
        /// <summary>0 or 1, or <see cref="Teams.None"/> in a mode without teams.</summary>
        public int Team = Teams.None;
        public bool Bot;
        /// <summary>Their account, if the server knows it: for blocking, muting across matches and reporting.</summary>
        public string AccountId = "";
        /// <summary>How they look. A new object each time it changes, so a view can tell by comparing references.</summary>
        public CharacterLook Look = new CharacterLook();
        /// <summary>Kills, deaths and score, as of the newest snapshot.</summary>
        public PlayerStats Stats;
        public LoadoutData LoadoutData;
        /// <summary>What they carry, with anything the mode does not let them use already taken out.</summary>
        public Loadout Loadout;
        /// <summary>What of <see cref="LoadoutData"/> they may use: see <see cref="MatchLoadouts"/>.</summary>
        public byte SlotMask = MatchLoadouts.AllSlots;

        internal readonly Sample[] Samples = new Sample[SampleCount];
        internal int NewestTick = int.MinValue;

        internal ClientPlayer(int id)
        {
            Id = id;
            ClearSamples();
        }

        internal void ClearSamples()
        {
            for (int i = 0; i < SampleCount; i++) Samples[i].Tick = int.MinValue;
            NewestTick = int.MinValue;
        }
    }

    /// <summary>
    /// A client's side of an online match, with no visuals attached. It does four things:
    ///
    ///  - Connects, and keeps the connection alive.
    ///  - Prediction. The local player is simulated straight away from their own input, with
    ///    no wait for the server. Every command is also sent to the server, which runs the
    ///    same simulation and reports back the state it reached. When that differs from what
    ///    was predicted, the local player is put into the server's state and the commands
    ///    sent since are replayed on top.
    ///  - Interpolation. Other players are drawn a few hundredths of a second in the past,
    ///    blended between the two most recent reports of where they were.
    ///  - Turns everything else the server says into events for the HUD and effects.
    ///
    /// Whoever owns this steps the local simulation themselves, between <see cref="Prepare"/>
    /// and <see cref="Commit"/>, so they can draw the shots that step produced.
    /// </summary>
    public sealed class ClientSession : IDisposable
    {
        const int HistorySize = 256;
        const float Dt = SimConstants.TickDelta;

        readonly INetSocket _socket;
        readonly LoadoutCatalog _catalog;
        readonly string _name;
        readonly string _ticket;
        readonly uint _clientSalt;
        readonly ClientPlayer[] _players = new ClientPlayer[NetProtocol.MaxPlayers];
        readonly byte[] _receiveBuffer = new byte[2048];
        readonly NetReader _reader = new NetReader();
        readonly NetWriter _out = new NetWriter(512);
        readonly SnapshotPlayer _scratch = new SnapshotPlayer();
        readonly ZoneState[] _zoneScratch = new ZoneState[NetMessages.MaxZones];
        readonly Action<NetReader> _onMessage;

        LoadoutData _loadout;
        CharacterLook _look;
        PlayerSimulation _sim;
        NetConnection _connection;
        NetAddress _server;
        uint _token;
        bool _challenged;
        double _connectStarted, _lastHandshake = double.MinValue, _lastFlush = double.MinValue, _now;

        // Prediction.
        readonly PlayerInputCmd[] _cmds = new PlayerInputCmd[HistorySize];
        readonly PlayerSimState[] _states = new PlayerSimState[HistorySize];
        readonly int[] _stateTicks = new int[HistorySize];
        int _cmdTick, _firstCmdTick = 1, _ackedTick;
        bool _hasSpawn, _forceRestore, _unsentInput;
        byte _appliedSpawnCount;

        // The server's clock, as heard from here.
        bool _clockValid;
        double _clockOffset;
        float _jitter, _snapshotSpacing = 1f;
        int _newestSnapshotTick = int.MinValue;

        public ClientStatus Status { get; private set; }
        /// <summary>A sentence for the player about why they are not connected.</summary>
        public string StatusText { get; private set; } = "";
        public int PlayerId { get; private set; } = -1;
        /// <summary>The server has said hello: the rules and the arena are known.</summary>
        public bool Welcomed { get; private set; }
        /// <summary>The match's rules, as the server told them on joining.</summary>
        public MatchSettings Settings { get; private set; } = MatchSettings.For(GameModeKind.Duel);
        public string ArenaId => Settings.arenaId;
        public MatchState Match { get; private set; } = MatchState.Empty;
        /// <summary>The capture zones, as of the newest snapshot. Only the first <see cref="ZoneCount"/> mean anything.</summary>
        public ZoneState[] Zones { get; } = new ZoneState[NetMessages.MaxZones];
        public int ZoneCount { get; private set; }

        /// <summary>The local player is in the world and alive: simulate them.</summary>
        public bool LocalAlive { get; private set; }
        public ClientPlayer Local => PlayerId >= 0 ? _players[PlayerId] : null;
        public ClientPlayer[] Players => _players;

        public float RttMs => _connection != null ? _connection.Rtt * 1000f : 0f;
        public float LossPercent => _connection != null ? _connection.Loss * 100f : 0f;
        /// <summary>How many times the server has disagreed with the prediction.</summary>
        public int Corrections { get; private set; }
        /// <summary>How far the last correction moved the player, in metres.</summary>
        public float LastCorrection { get; private set; }
        /// <summary>What the server disagreed about last time, for the network readout.</summary>
        public string LastCorrectionCause { get; private set; } = "";

        // Events. All raised from inside Update.
        public event Action Connected;
        public event Action<ClientPlayer> PlayerChanged;
        /// <summary>A player was placed in the world (start of a round, or a respawn).</summary>
        public event Action<ClientPlayer, SpawnMessage> PlayerSpawned;
        /// <summary>A player's weapons were swapped where they stand (Gun Game).</summary>
        public event Action<ClientPlayer, RearmMessage> PlayerRearmed;
        /// <summary>The previous state, then the new one.</summary>
        public event Action<MatchState, MatchState> MatchChanged;
        public event Action<KillMessage> Killed;
        /// <summary>A shot of ours did damage.</summary>
        public event Action<HitMessage> HitConfirmed;
        /// <summary>We took damage.</summary>
        public event Action<HurtMessage> Hurt;
        /// <summary>Someone said something we can hear.</summary>
        public event Action<ChatLineMessage> ChatReceived;
        public event Action<ShotMessage> RemoteShot;
        public event Action<ProjectileSpawnMessage> ProjectileSpawned;
        public event Action<ProjectileEndMessage> ProjectileEnded;
        /// <summary>The prediction was corrected. Argument: where the player was drawn minus where they now are.</summary>
        public event Action<Vector3> Corrected;

        /// <param name="salt">Any number that is different each time. 0 picks one.</param>
        /// <param name="ticket">A join ticket from the backend, for servers that only admit signed-in players. Empty otherwise.</param>
        /// <param name="look">How this player looks to everyone else. Null for the default look.</param>
        public ClientSession(INetSocket socket, LoadoutCatalog catalog, string playerName, LoadoutData loadout, uint salt = 0, string ticket = "",
            CharacterLook look = null)
        {
            _look = LookRules.Sanitize(look);
            _socket = socket;
            _catalog = catalog;
            _name = playerName ?? "";
            _ticket = ticket ?? "";
            _loadout = loadout;
            _clientSalt = salt != 0 ? salt : (uint)Guid.NewGuid().GetHashCode() | 1u;
            _onMessage = OnMessage;

            for (int i = 0; i < _players.Length; i++) _players[i] = new ClientPlayer(i);
            for (int i = 0; i < HistorySize; i++)
            {
                _states[i] = new PlayerSimState();
                _stateTicks[i] = int.MinValue;
            }
        }

        /// <summary>Hands over the local player's simulation. Do this before connecting.</summary>
        public void Attach(PlayerSimulation simulation) => _sim = simulation;

        public void Connect(NetAddress server, double now)
        {
            _server = server;
            _connectStarted = now;
            _lastHandshake = double.MinValue;
            _challenged = false;
            Status = ClientStatus.Connecting;
            StatusText = "Connecting to " + server + "...";
        }

        /// <summary>Asks the server for a different loadout. It takes effect the next time we are placed.</summary>
        public void SetLoadout(LoadoutData loadout)
        {
            _loadout = loadout;
            if (Status != ClientStatus.Connected) return;
            _out.Reset();
            NetMessages.WriteLoadoutChange(_out, loadout);
            _connection.Send(_out, true);
        }

        /// <summary>Changes how this player looks to everyone else. Takes effect at once, wherever they are.</summary>
        public void SetLook(CharacterLook look)
        {
            _look = LookRules.Sanitize(look);
            if (Status != ClientStatus.Connected) return;
            _out.Reset();
            NetMessages.WriteLookChange(_out, _look);
            _connection.Send(_out, true);
        }

        /// <summary>Says something in the match: to everyone, or only to our team. False if there was nothing to send.</summary>
        public bool SendChat(string text, bool teamOnly)
        {
            string clean = Shared.Social.ChatText.Clean(text);
            if (clean == null || Status != ClientStatus.Connected) return false;
            _out.Reset();
            NetMessages.WriteChat(_out, clean, teamOnly);
            _connection.Send(_out, true);
            return true;
        }

        public void Disconnect()
        {
            if (_connection != null)
            {
                _out.Reset();
                _out.WriteByte((byte)PacketType.Disconnect);
                _out.WriteUInt(_token);
                for (int i = 0; i < 3; i++) _socket.Send(_server, _out.Data, _out.Length);
            }
            _connection = null;
            LocalAlive = false;
            Status = ClientStatus.Idle;
            StatusText = "";
        }

        public void Dispose()
        {
            Disconnect();
            _socket.Dispose();
        }

        // ------------------------------------------------------------------ every frame

        /// <summary>
        /// Takes in everything the server has sent. Call at the start of every frame, before
        /// simulating: this is where the local player is corrected and events are raised.
        /// </summary>
        public void Update(double now)
        {
            _now = now;

            for (int packet = 0; packet < 256; packet++)
            {
                if (!_socket.Receive(_receiveBuffer, out int length, out NetAddress from)) break;
                if (length < 1 || from != _server) continue;
                OnPacket(length, now);
            }

            if (Status == ClientStatus.Connecting)
            {
                if (now - _connectStarted > NetProtocol.ConnectTimeoutSeconds) Stop(ClientStatus.Failed, "No answer from the server.");
                else if (now - _lastHandshake >= NetProtocol.ConnectRetrySeconds) SendHandshake(now);
            }
            else if (Status == ClientStatus.Connected && now - _connection.LastReceiveTime > NetProtocol.TimeoutSeconds)
            {
                Stop(ClientStatus.Disconnected, "Lost the connection to the server.");
            }
        }

        /// <summary>
        /// Sends the newest commands. Call once per frame, after simulating. With nothing new
        /// to send it still speaks up now and then, so the server knows we are here.
        /// </summary>
        public void Flush(double now)
        {
            if (Status != ClientStatus.Connected) return;
            if (!_unsentInput && now - _lastFlush < 1.0 / 30.0) return;

            if (_unsentInput)
            {
                int count = Mathf.Min(NetMessages.InputRedundancy, _cmdTick - _firstCmdTick + 1);
                _out.Reset();
                NetMessages.WriteInput(_out, _cmds, _cmdTick, count);
                _connection.Send(_out, false);
                _unsentInput = false;
            }

            _connection.Flush(_socket, now);
            _lastFlush = now;
        }

        // ------------------------------------------------------------------ every tick

        /// <summary>
        /// Turns this tick's raw input into the command that will be both simulated here and
        /// sent to the server: numbered, rounded to what survives the trip, and stamped with
        /// what the player was looking at.
        /// </summary>
        public PlayerInputCmd Prepare(PlayerInputCmd cmd)
        {
            cmd.Tick = ++_cmdTick;
            cmd.ViewTick = (float)RenderTick;
            NetMessages.Quantize(ref cmd);

            // The server holds everyone still until the countdown ends. This command will not
            // reach it for a while, so what matters is the time it gets there, not the time now:
            // that way a round starts at the same moment for both players whatever their ping.
            if (Match.Phase == MatchPhase.Countdown && CommandArrivalTick <= Match.PhaseEndTick)
                MatchRules.Freeze(ref cmd);
            return cmd;
        }

        /// <summary>Records the command and the state it led to, ready to be sent and later checked.</summary>
        public void Commit(in PlayerInputCmd cmd)
        {
            int slot = cmd.Tick % HistorySize;
            _cmds[slot] = cmd;
            _sim.Capture(_states[slot]);
            _stateTicks[slot] = cmd.Tick;
            _unsentInput = true;
        }

        // ------------------------------------------------------------------ time

        /// <summary>The server tick whose snapshot is arriving about now: the server's clock, minus the trip here.</summary>
        public double ArrivalTick => _clockValid ? (_now + _clockOffset) / Dt : 0.0;

        /// <summary>
        /// The server tick at which a command sent now will be run: what has arrived, plus a
        /// full round trip. "When does the round start for me" is measured against this.
        /// </summary>
        public double CommandArrivalTick => ArrivalTick + (_connection != null ? _connection.Rtt : 0f) / Dt;

        /// <summary>
        /// The server tick other players are drawn at. It trails what has arrived by enough
        /// that there is nearly always a later snapshot to blend towards, and by more when
        /// snapshots are arriving unevenly.
        /// </summary>
        public double RenderTick => ArrivalTick - InterpolationDelay / Dt;

        public float InterpolationDelay => Mathf.Clamp(_snapshotSpacing * Dt * 2f + _jitter * 3f, 0.035f, 0.25f);

        void HearClock(int tick)
        {
            double sample = tick * (double)Dt - _now;
            if (!_clockValid || Math.Abs(sample - _clockOffset) > 0.5)
            {
                _clockOffset = sample;
                _clockValid = true;
                _jitter = 0f;
                return;
            }

            // Move a little towards each new reading, so one late packet does not jolt everything.
            double error = sample - _clockOffset;
            _jitter += ((float)Math.Abs(error) - _jitter) * 0.05f;
            _clockOffset += error * 0.05;
        }

        // ------------------------------------------------------------------ other players

        /// <summary>Where to draw a player this frame. False if there is nothing to draw.</summary>
        public bool TryGetView(int playerId, out RemoteView view)
        {
            view = default;
            ClientPlayer player = _players[playerId];
            if (!player.Present || !player.Spawned || !player.Alive || player.NewestTick == int.MinValue) return false;

            double renderTick = RenderTick;
            int floor = (int)Math.Floor(renderTick);

            bool hasEarlier = false, hasLater = false;
            ClientPlayer.Sample earlier = default, later = default;
            for (int tick = floor; tick > floor - ClientPlayer.SampleCount && !hasEarlier; tick--)
                hasEarlier = Find(player, tick, out earlier);
            for (int tick = floor + 1; tick <= player.NewestTick && !hasLater; tick++)
                hasLater = Find(player, tick, out later);

            if (!hasEarlier && !hasLater) return false;
            if (!hasEarlier) earlier = later;

            // Placed somewhere new since: they are not there yet, as far as the drawing goes.
            if (earlier.SpawnCount != player.SpawnCount)
            {
                if (!hasLater || later.SpawnCount != player.SpawnCount) return false;
                earlier = later;
                hasLater = false;
            }
            if (!earlier.Visible) return false;

            if (hasLater && later.Visible && later.SpawnCount == earlier.SpawnCount && later.Tick > earlier.Tick)
            {
                float t = (float)((renderTick - earlier.Tick) / (later.Tick - earlier.Tick));
                view = later.View;
                view.Pose = BodyPose.Lerp(earlier.View.Pose, later.View.Pose, t);
                view.Pitch = Mathf.Lerp(earlier.View.Pitch, later.View.Pitch, t);
                return true;
            }

            // Nothing newer has arrived. Carry on in a straight line for a moment rather than freeze.
            view = earlier.View;
            float ahead = Mathf.Clamp((float)(renderTick - earlier.Tick) * Dt, 0f, 0.1f);
            view.Pose.Position += view.Velocity * ahead;
            return true;
        }

        static bool Find(ClientPlayer player, int tick, out ClientPlayer.Sample sample)
        {
            sample = player.Samples[tick & (ClientPlayer.SampleCount - 1)];
            return sample.Tick == tick;
        }

        // ------------------------------------------------------------------ connecting

        void SendHandshake(double now)
        {
            _lastHandshake = now;
            _out.Reset();
            if (!_challenged)
            {
                _out.WriteByte((byte)PacketType.ConnectRequest);
                _out.WriteUInt(NetProtocol.Version);
                _out.WriteUInt(_clientSalt);
                while (_out.Length < NetProtocol.ConnectRequestSize) _out.WriteByte(0);
            }
            else
            {
                _out.WriteByte((byte)PacketType.ChallengeResponse);
                _out.WriteUInt(_clientSalt);
                _out.WriteUInt(_token);
            }
            _socket.Send(_server, _out.Data, _out.Length);
        }

        void OnPacket(int length, double now)
        {
            var type = (PacketType)_receiveBuffer[0];
            if (type == PacketType.Data)
            {
                if (_connection != null) _connection.Receive(_receiveBuffer, length, now, _onMessage);
                return;
            }

            _reader.Reset(_receiveBuffer, 1, length - 1);
            switch (type)
            {
                case PacketType.Challenge:
                {
                    uint salt = _reader.ReadUInt();
                    uint serverSalt = _reader.ReadUInt();
                    if (Status != ClientStatus.Connecting || _challenged || _reader.Failed || salt != _clientSalt) break;
                    _challenged = true;
                    _token = _clientSalt ^ serverSalt;
                    SendHandshake(now);
                    break;
                }

                case PacketType.Accepted:
                {
                    uint token = _reader.ReadUInt();
                    int id = _reader.ReadByte();
                    if (Status != ClientStatus.Connecting || !_challenged || _reader.Failed || token != _token || id >= _players.Length) break;

                    PlayerId = id;
                    _connection = new NetConnection(_server, _token, now);
                    Status = ClientStatus.Connected;
                    StatusText = "";

                    _out.Reset();
                    NetMessages.WriteHello(_out, _name, _loadout, _ticket, _look);
                    _connection.Send(_out, true);
                    Connected?.Invoke();
                    break;
                }

                case PacketType.Denied:
                {
                    uint salt = _reader.ReadUInt();
                    var reason = (DenyReason)_reader.ReadByte();
                    if (Status != ClientStatus.Connecting || _reader.Failed || salt != _clientSalt) break;
                    Stop(ClientStatus.Failed, reason == DenyReason.ServerFull
                        ? "That match is full."
                        : "This copy of the game is a different version from the server's.");
                    break;
                }

                case PacketType.Disconnect:
                {
                    if (_connection == null || _reader.ReadUInt() != _token || _reader.Failed) break;
                    var why = _reader.Remaining > 0 ? (DisconnectReason)_reader.ReadByte() : DisconnectReason.None;
                    Stop(ClientStatus.Disconnected,
                        why == DisconnectReason.TicketRequired ? "That server only admits signed-in players. Sign in from the menu and join again."
                        : why == DisconnectReason.AlreadyHere ? "Your account is already in that match."
                        : why == DisconnectReason.RankedOnly ? "That match is ranked. Verify your email address to play ranked."
                        : why == DisconnectReason.WrongMatch ? "That server is running a different match now. Find a match again."
                        : why == DisconnectReason.MatchOver ? MatchOverText
                        : "The server closed the connection.");
                    break;
                }
            }
        }

        /// <summary>What <see cref="StatusText"/> says when a matchmade match has finished and the server sent everyone home.</summary>
        public const string MatchOverText = "The match is over.";

        /// <summary>The server ended the session because its one match was over, rather than because something went wrong.</summary>
        public bool EndedByMatchOver => Status == ClientStatus.Disconnected && StatusText == MatchOverText;

        void Stop(ClientStatus status, string text)
        {
            _connection = null;
            LocalAlive = false;
            Status = status;
            StatusText = text;
        }

        // ------------------------------------------------------------------ messages

        void OnMessage(NetReader reader)
        {
            switch ((NetMessage)reader.ReadByte())
            {
                case NetMessage.Welcome:
                {
                    WelcomeMessage message = NetMessages.ReadWelcome(reader);
                    if (reader.Failed) break;
                    Settings = message.Settings;
                    Welcomed = true;
                    break;
                }

                case NetMessage.PlayerInfo:
                {
                    PlayerInfoMessage message = NetMessages.ReadPlayerInfo(reader);
                    if (reader.Failed || message.PlayerId >= _players.Length) break;
                    ClientPlayer player = _players[message.PlayerId];
                    player.Present = message.Present;
                    player.Name = message.Name;
                    player.Team = message.Team;
                    player.Bot = message.Bot;
                    player.Look = message.Look;
                    player.AccountId = message.AccountId ?? "";
                    if (!message.Present)
                    {
                        player.Spawned = player.Alive = false;
                        player.Stats = default;
                        player.ClearSamples();
                    }
                    PlayerChanged?.Invoke(player);
                    break;
                }

                case NetMessage.Spawn:
                {
                    SpawnMessage message = NetMessages.ReadSpawn(reader);
                    if (!reader.Failed && message.PlayerId < _players.Length) OnSpawn(message);
                    break;
                }

                case NetMessage.Rearm:
                {
                    RearmMessage message = NetMessages.ReadRearm(reader);
                    if (!reader.Failed && message.PlayerId < _players.Length) OnRearm(message);
                    break;
                }

                case NetMessage.Snapshot:
                    OnSnapshot(reader);
                    break;

                case NetMessage.Match:
                {
                    MatchState state = NetMessages.ReadMatch(reader);
                    if (reader.Failed) break;
                    MatchState previous = Match;
                    Match = state;
                    MatchChanged?.Invoke(previous, state);
                    break;
                }

                case NetMessage.Kill:
                {
                    KillMessage message = NetMessages.ReadKill(reader);
                    if (reader.Failed || message.Victim >= _players.Length) break;
                    _players[message.Victim].Alive = false;
                    _players[message.Victim].Health = 0f;
                    if (message.Victim == PlayerId) LocalAlive = false;
                    Killed?.Invoke(message);
                    break;
                }

                case NetMessage.Hit:
                {
                    HitMessage message = NetMessages.ReadHit(reader);
                    if (!reader.Failed) HitConfirmed?.Invoke(message);
                    break;
                }

                case NetMessage.ChatLine:
                {
                    ChatLineMessage message = NetMessages.ReadChatLine(reader);
                    if (!reader.Failed && message.PlayerId < _players.Length) ChatReceived?.Invoke(message);
                    break;
                }

                case NetMessage.Hurt:
                {
                    HurtMessage message = NetMessages.ReadHurt(reader);
                    if (!reader.Failed) Hurt?.Invoke(message);
                    break;
                }

                case NetMessage.Shot:
                {
                    ShotMessage message = NetMessages.ReadShot(reader);
                    if (!reader.Failed && message.Shooter < _players.Length) RemoteShot?.Invoke(message);
                    break;
                }

                case NetMessage.ProjectileSpawn:
                {
                    ProjectileSpawnMessage message = NetMessages.ReadProjectileSpawn(reader);
                    if (!reader.Failed && message.Owner < _players.Length) ProjectileSpawned?.Invoke(message);
                    break;
                }

                case NetMessage.ProjectileEnd:
                {
                    ProjectileEndMessage message = NetMessages.ReadProjectileEnd(reader);
                    if (!reader.Failed) ProjectileEnded?.Invoke(message);
                    break;
                }
            }
        }

        void OnSpawn(in SpawnMessage message)
        {
            ClientPlayer player = _players[message.PlayerId];
            player.LoadoutData = message.Loadout;
            player.SlotMask = message.SlotMask;
            player.Loadout = MatchLoadouts.Apply(LoadoutRules.Resolve(message.Loadout, _catalog), message.SlotMask);
            player.SpawnCount = message.SpawnCount;
            player.Spawned = true;
            player.Alive = true;
            player.Health = Settings.maxHealth;

            if (message.PlayerId == PlayerId && _sim != null)
            {
                // Rebuilt exactly as the server rebuilt its copy, so the two start level.
                _sim.Seed = PlayerSimulation.SeedFor(PlayerId);
                _sim.InfiniteReserve = message.UnlimitedAmmo;
                _sim.RechargeUtilities = message.RechargeUtility;
                _sim.SetLoadout(player.Loadout, message.ActiveSlot);
                _sim.Motor.Teleport(message.Position, message.Yaw, 0f);

                // Nothing predicted before this moment applies any more.
                _appliedSpawnCount = message.SpawnCount;
                _hasSpawn = true;
                _forceRestore = true;
                _firstCmdTick = _cmdTick + 1;
                for (int i = 0; i < HistorySize; i++) _stateTicks[i] = int.MinValue;
                LocalAlive = true;
            }

            PlayerSpawned?.Invoke(player, message);
        }

        void OnRearm(in RearmMessage message)
        {
            ClientPlayer player = _players[message.PlayerId];
            byte previousCount = player.SpawnCount;
            player.LoadoutData = message.Loadout;
            player.SlotMask = message.SlotMask;
            player.Loadout = MatchLoadouts.Apply(LoadoutRules.Resolve(message.Loadout, _catalog), message.SlotMask);
            player.SpawnCount = message.SpawnCount;

            // They have not moved, so where they have been is still where they have been.
            for (int i = 0; i < ClientPlayer.SampleCount; i++)
            {
                if (player.Samples[i].SpawnCount == previousCount) player.Samples[i].SpawnCount = message.SpawnCount;
            }

            if (message.PlayerId == PlayerId && _sim != null && _hasSpawn)
            {
                // Same body, same place, new weapons. The server's next word on our state is
                // about the new weapons, and replaces whatever was predicted with the old ones.
                _sim.SetLoadout(player.Loadout, message.ActiveSlot);
                _appliedSpawnCount = message.SpawnCount;
                _forceRestore = true;
                _sim.AnnounceActiveWeapon();
            }

            PlayerRearmed?.Invoke(player, message);
        }

        void OnSnapshot(NetReader reader)
        {
            NetMessages.ReadSnapshotHeader(reader, out int tick, _zoneScratch, out int zoneCount, out int count);
            if (reader.Failed || count > _players.Length) return;

            // Packets can overtake each other. An old snapshot still fills in where another
            // player was, but says nothing new about anyone's current state.
            bool newest = tick > _newestSnapshotTick;

            for (int i = 0; i < count; i++)
            {
                NetMessages.ReadSnapshotPlayer(reader, _scratch);
                if (reader.Failed || _scratch.Id >= _players.Length) return;
                ClientPlayer player = _players[_scratch.Id];
                if (newest) player.Stats = _scratch.Stats;

                if (_scratch.Id == PlayerId)
                {
                    if (newest) HearOwn(_scratch);
                    continue;
                }

                if (player.NewestTick == int.MinValue || tick > player.NewestTick - ClientPlayer.SampleCount)
                {
                    ref ClientPlayer.Sample sample = ref player.Samples[tick & (ClientPlayer.SampleCount - 1)];
                    sample.Tick = tick;
                    sample.Visible = _scratch.Spawned && _scratch.Alive;
                    sample.SpawnCount = _scratch.SpawnCount;
                    sample.View = new RemoteView
                    {
                        Pose = new BodyPose
                        {
                            Position = _scratch.Position,
                            Yaw = _scratch.Yaw,
                            EyeHeight = _scratch.EyeHeight,
                        },
                        Pitch = _scratch.Pitch,
                        Velocity = _scratch.Velocity,
                        Stance = _scratch.Stance,
                        ActiveSlot = _scratch.ActiveSlot,
                    };
                    if (tick > player.NewestTick) player.NewestTick = tick;
                }

                // A snapshot from before a respawn we already know about says nothing current.
                if (newest && _scratch.SpawnCount == player.SpawnCount)
                {
                    player.Spawned = _scratch.Spawned;
                    player.Alive = _scratch.Alive;
                    player.Health = _scratch.Health;
                }
            }

            if (!newest) return;
            for (int i = 0; i < zoneCount; i++) Zones[i] = _zoneScratch[i];
            ZoneCount = zoneCount;
            if (_newestSnapshotTick != int.MinValue)
                _snapshotSpacing = Mathf.Lerp(_snapshotSpacing, Mathf.Clamp(tick - _newestSnapshotTick, 1, 10), 0.1f);
            _newestSnapshotTick = tick;
            HearClock(tick);
        }

        // ------------------------------------------------------------------ prediction

        void HearOwn(SnapshotPlayer own)
        {
            // From before our last respawn, or after one we have not been told about yet.
            if (!_hasSpawn || own.SpawnCount != _appliedSpawnCount) return;

            ClientPlayer player = _players[PlayerId];
            player.Health = own.Health;
            player.Spawned = own.Spawned;
            player.Alive = own.Alive;
            LocalAlive = own.Spawned && own.Alive;

            if (own.HasOwn && _sim != null && LocalAlive) Reconcile(own);
        }

        /// <summary>
        /// Checks what we predicted against what the server actually got. The server's state
        /// is for some command a little way back; if we were right about that one, everything
        /// since still stands. If not, start again from the server's state and replay every
        /// command sent since, silently, to arrive at a corrected present.
        /// </summary>
        void Reconcile(SnapshotPlayer own)
        {
            int tick = own.LastCmdTick;
            string cause = "placed by the server";
            if (!_forceRestore)
            {
                if (tick <= _ackedTick) return;      // nothing new has been processed
                int slot = tick % HistorySize;
                bool predicted = tick > 0 && _stateTicks[slot] == tick;
                cause = predicted ? _states[slot].Difference(own.Own) : "a command from before we were placed";
                if (cause == null)
                {
                    _ackedTick = tick;
                    return;
                }
            }
            LastCorrectionCause = cause;
            _ackedTick = tick;
            _forceRestore = false;

            Vector3 before = _sim.Motor.State.Position;
            int activeBefore = _sim.Inventory.ActiveIndex;

            _sim.Muted = true;
            _sim.Restore(own.Own);
            for (int replay = Mathf.Max(tick + 1, _firstCmdTick); replay <= _cmdTick; replay++)
            {
                int slot = replay % HistorySize;
                if (_stateTicks[slot] != replay) continue;
                _sim.Step(_cmds[slot], Dt);
                _sim.Capture(_states[slot]);
            }
            _sim.Muted = false;

            Vector3 moved = before - _sim.Motor.State.Position;
            Corrections++;
            LastCorrection = moved.magnitude;
            Corrected?.Invoke(moved);
            if (_sim.Inventory.ActiveIndex != activeBefore) _sim.AnnounceActiveWeapon();
        }
    }
}
