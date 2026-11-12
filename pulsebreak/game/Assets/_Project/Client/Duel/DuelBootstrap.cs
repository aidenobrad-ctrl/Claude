using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Pulsebreak.Client.Audio;
using Pulsebreak.Client.FiringRange;
using Pulsebreak.Client.Loadouts;
using Pulsebreak.Client.Net;
using Pulsebreak.Client.Online;
using Pulsebreak.Client.Player;
using Pulsebreak.Client.Settings;
using Pulsebreak.Client.UI;
using Pulsebreak.Client.Visuals;
using Pulsebreak.Server;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Net;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.Duel
{
    /// <summary>
    /// Entry point of the online scene, and the game's main menu. Put this on one object in
    /// an otherwise empty scene. (It is called the duel scene for history's sake: it plays
    /// every mode.)
    ///
    /// The same scene runs four ways:
    ///
    ///   Find match  asks the backend's matchmaking for a match in the chosen mode, then
    ///               joins the server it found.
    ///   Host        starts a server in this process with the rules picked in the menu (bots
    ///               included), then joins it like anyone else would.
    ///   Join        connects to a server at an address.
    ///   Dedicated   started with -server (or in batch mode): runs only the server, no
    ///               window contents, no player. With -fleet it offers itself to matchmaking.
    ///
    /// A hosting player gets no shortcuts: their client talks to their own server over the
    /// network socket and is predicted, checked and corrected exactly as a remote one is.
    ///
    /// Command line: -server, -port N, -mode ID, -map ID, -bots, -fleet URL, -public-address
    /// HOST, -host, -connect ADDRESS, -name NAME, and for testing -lag MS, -jitter MS,
    /// -loss PERCENT to make the connection worse on purpose.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class DuelBootstrap : MonoBehaviour
    {
        public const string RangeScene = "FiringRange";

        static readonly Color FloorColor = new Color(0.62f, 0.64f, 0.68f);
        static readonly Color WallColor = new Color(0.4f, 0.44f, 0.5f);
        static readonly Color CoverColor = new Color(0.36f, 0.5f, 0.7f);
        static readonly Color AccentColor = new Color(0.9f, 0.6f, 0.28f);

        [SerializeField] MovementConfig movement;
        [SerializeField] LoadoutCatalog catalog;
        [Tooltip("Turn off if the scene already has its own directional light.")]
        [SerializeField] bool createSunLight = true;

        [Header("Accounts")]
        [Tooltip("Where the accounts backend is. -backend URL on the command line overrides it.")]
        [SerializeField] string backendUrl = GameServices.DefaultBackendUrl;

        [Header("Hosting")]
        [SerializeField] int port = NetProtocol.DefaultPort;
        [SerializeField] ServerConfig server = new ServerConfig();

        [Header("Testing")]
        [Tooltip("Makes this client's connection worse on purpose: delay, uneven delay and lost packets, each way. Leave at zero for normal play.")]
        [SerializeField] NetConditions simulatedConnection = new NetConditions();

        LoadoutLibrary _library;
        GameServices _services;
        DuelUi _ui;
        ServerHost _host;
        Camera _menuCamera;
        Transform _arenaRoot;
        string _shownArenaId;
        string _joinHint = "";
        string _nameOverride;

        // Only while connecting or in a match.
        ClientSession _session;
        PlayerRig _rig;
        HudController _hud;
        ProjectileSystem _projectiles;
        readonly RemotePlayerView[] _remotes = new RemotePlayerView[NetProtocol.MaxPlayers];
        readonly List<ScoreRow> _rows = new List<ScoreRow>(NetProtocol.MaxPlayers);
        ZoneMarkers _zoneMarkers;
        bool _showNetStats = true;
        float _deadSince = -1f;
        // Goes up with every attempt to connect and every return to the menu, so an attempt
        // still waiting for its ticket can tell it has been called off.
        int _attempt;
        float _noticeTimer, _fightTimer, _textTimer;

        void Awake()
        {
            if (movement == null)
            {
                Debug.LogWarning("DuelBootstrap: no Movement Config assigned, using built-in defaults.", this);
                movement = ScriptableObject.CreateInstance<MovementConfig>();
            }
            if (catalog == null || catalog.weapons == null || catalog.weapons.weapons.Length == 0 || catalog.presets.Length == 0)
            {
                Debug.LogWarning("DuelBootstrap: no usable Loadout Catalog assigned, using built-in stand-in equipment.", this);
                catalog = FallbackContent.BuildCatalog();
            }

            Application.runInBackground = true;
            GameLayers.ConfigureCollisionMatrix();

            string[] args = Environment.GetCommandLineArgs();
            port = (int)ArgNumber(args, "-port", port);
            simulatedConnection.latencyMs = ArgNumber(args, "-lag", simulatedConnection.latencyMs);
            simulatedConnection.jitterMs = ArgNumber(args, "-jitter", simulatedConnection.jitterMs);
            simulatedConnection.lossPercent = ArgNumber(args, "-loss", simulatedConnection.lossPercent);
            _nameOverride = ArgText(args, "-name");

            if (Application.isBatchMode || HasArg(args, "-server"))
            {
                // A dedicated server: nothing to draw and nobody to draw it for.
                Application.targetFrameRate = 120;

                // Given the backend's ticket key, it admits only signed-in players. The
                // environment is the better place for a secret: the command line can be read
                // by anyone on the machine who can list processes.
                server.ticketKey = Environment.GetEnvironmentVariable("PULSEBREAK_TICKET_KEY") ?? ArgText(args, "-ticket-key") ?? "";
                server.rankedOnly |= HasArg(args, "-ranked");
                ApplyServerArgs(args);
                if (!StartServer(out string problem))
                {
                    Debug.LogError("[Server] " + problem);
                    return;
                }

                // Offered to matchmaking: the backend hands this server a match whenever it is free.
                string fleet = ArgText(args, "-fleet");
                if (fleet != null)
                {
                    var agent = gameObject.AddComponent<FleetAgent>();
                    agent.Begin(_host, fleet, Environment.GetEnvironmentVariable("PULSEBREAK_FLEET_KEY") ?? ArgText(args, "-fleet-key") ?? "",
                        ArgText(args, "-public-address") ?? LocalAddress(), port);
                }
                return;
            }

            GameSettings.Load();
            RangeEnvironment.Apply(createSunLight);
            _services = GameServices.Get(catalog, backendUrl);
            _library = _services.Library;
            _library.EquippedChanged += OnEquippedChanged;

            ShowArena(server.match.arenaId);
            CreateMenuCamera();

            _ui = DuelUi.Create(_services);
            _ui.FindRequested += Find;
            _ui.HostRequested += Host;
            _ui.JoinRequested += Join;
            _ui.CancelRequested += () => Leave("");
            _ui.RangeRequested += () => SceneManager.LoadScene(RangeScene);
            _ui.QuitRequested += Quit;

            string connect = ArgText(args, "-connect");
            if (connect != null)
            {
                Join(connect);
            }
            else if (HasArg(args, "-host"))
            {
                ApplyServerArgs(args);
                Host(server.match);
            }
        }

        /// <summary>-mode, -map and -bots set the rules a server started from the command line plays by.</summary>
        void ApplyServerArgs(string[] args)
        {
            string modeId = ArgText(args, "-mode");
            if (modeId != null)
            {
                Playlist playlist = Playlists.Find(modeId);
                if (playlist != null) server.match = MatchSettings.For(playlist.Mode);
                else Debug.LogWarning("Pulsebreak: unknown -mode \"" + modeId + "\". Modes: duel, tdm, ffa, elimination, domination, hardpoint, gungame.");
            }
            string map = ArgText(args, "-map");
            if (map != null) server.match.arenaId = map;
            if (HasArg(args, "-bots")) server.match.fillWithBots = true;
        }

        void OnDestroy()
        {
            if (_library != null) _library.EquippedChanged -= OnEquippedChanged;
            TearDownMatch();
            StopServer();
        }

        // ------------------------------------------------------------------ hosting and joining

        bool StartServer(out string problem)
        {
            problem = "";
            if (_host != null) return true;

            _host = gameObject.AddComponent<ServerHost>();
            try
            {
                _host.Begin(port, movement, catalog, server);
                if (_host.Server.RequiresTicket) Debug.Log("[Server] Only signed-in players are admitted" + (server.rankedOnly ? ", and only with a verified email (ranked)." : "."));
                return true;
            }
            catch (SocketException)
            {
                problem = "Could not open port " + port + ". Is a match already being hosted on this computer?";
            }
            catch (Exception e)
            {
                problem = "Could not start the server: " + e.Message;
            }
            Debug.LogError("[Server] " + problem);

            Destroy(_host);
            _host = null;
            return false;
        }

        void StopServer()
        {
            if (_host == null) return;
            _host.End();
            Destroy(_host);
            _host = null;
        }

        void Host(MatchSettings rules)
        {
            if (_session != null) return;
            server.match = rules ?? server.match;
            if (!StartServer(out string problem))
            {
                _ui.ShowMenu(problem);
                return;
            }
            _joinHint = "Others join with  " + LocalAddress() + (port != NetProtocol.DefaultPort ? ":" + port : "");
            Connect(NetAddress.Loopback(port));
        }

        void Join(string address)
        {
            if (_session != null) return;
            if (!NetAddress.TryParse(address, NetProtocol.DefaultPort, out NetAddress target))
            {
                _ui.ShowMenu("\"" + address + "\" is not an address that can be reached. Try something like 192.168.1.20 or 192.168.1.20:7777.");
                return;
            }
            _joinHint = "";
            Connect(target);
        }

        /// <summary>Queues for a match through the backend, then joins the server it finds.</summary>
        async void Find(GameModeKind mode)
        {
            if (_session != null) return;
            int attempt = ++_attempt;
            MatchmakingClient matchmaking = _services.Matchmaking;
            string name = PlayerName();
            _ui.ShowConnecting("Joining the " + MatchSettings.Title(mode) + " queue...");

            AccountResult<MatchTicketData> queued = await matchmaking.JoinQueueAsync(mode, name);
            if (this == null) return;
            if (attempt != _attempt)
            {
                if (queued.Ok) _ = matchmaking.LeaveQueueAsync(queued.Value.id, name);
                return;
            }
            if (!queued.Ok)
            {
                _ui.ShowMenu(queued.Offline
                    ? "Could not reach matchmaking. Is the backend running? You can still host a match or join one by address."
                    : queued.Message);
                return;
            }

            MatchTicketData ticket = queued.Value;
            while (ticket.Searching)
            {
                _ui.ShowConnecting(MatchmakingClient.Describe(ticket, mode));
                await Task.Delay(1000);
                if (this == null) return;
                if (attempt != _attempt)
                {
                    // Cancelled while searching: give the place up.
                    _ = matchmaking.LeaveQueueAsync(ticket.id, name);
                    return;
                }

                AccountResult<MatchTicketData> check = await matchmaking.CheckAsync(ticket.id, name);
                if (this == null || attempt != _attempt) return;
                if (!check.Ok)
                {
                    if (check.Offline) continue;        // a blip: keep the place and ask again
                    _ui.ShowMenu(check.Message);
                    return;
                }
                ticket = check.Value;
            }

            if (!ticket.Matched || !NetAddress.TryParse(ticket.address, NetProtocol.DefaultPort, out NetAddress target))
            {
                _ui.ShowMenu(ticket.status == "expired" ? "The search ran out of time. Try again." : "The search was called off.");
                return;
            }
            _joinHint = "";
            ConnectWithTicket(target, ticket.joinTicket);
        }

        string PlayerName() => _services.Account.SignedIn ? _services.Account.Account.username
            : string.IsNullOrEmpty(_nameOverride) ? _ui.PlayerName : _nameOverride;

        /// <summary>Signed in: get a ticket from the backend first, so servers that ask know who this is.</summary>
        async void Connect(NetAddress target)
        {
            int attempt = ++_attempt;
            string ticket = "";
            if (_services.Account.SignedIn)
            {
                _ui.ShowConnecting("Getting a ticket from the account server...");
                AccountResult<string> result = await _services.Account.GetTicketAsync(ranked: _services.Account.Account.emailVerified);
                // Left the scene, or pressed Cancel, while waiting.
                if (this == null || attempt != _attempt) return;
                if (result.Ok) ticket = result.Value;
                else Debug.LogWarning("Pulsebreak: could not get a join ticket (" + result.Message + "). Joining without one; servers that need one will say so.");
            }
            ConnectWithTicket(target, ticket);
        }

        void ConnectWithTicket(NetAddress target, string ticket)
        {
            if (_session != null) return;
            INetSocket socket;
            try
            {
                socket = new UdpSocket();
            }
            catch (Exception e)
            {
                StopServer();
                _ui.ShowMenu("Could not open a network connection: " + e.Message);
                return;
            }
            if (simulatedConnection.Any)
                socket = new ConditionedSocket(socket, simulatedConnection, () => Time.realtimeSinceStartupAsDouble, (uint)Environment.TickCount | 1u);

            // Signed in, a player goes by their account's name everywhere. (On a server that checks
            // tickets, the name comes from the ticket whatever is sent here.)
            _session = new ClientSession(socket, catalog, PlayerName(), _library.Equipped, ticket: ticket);
            _session.Connected += EnterMatch;
            _session.PlayerChanged += OnPlayerChanged;
            _session.PlayerSpawned += OnPlayerSpawned;
            _session.PlayerRearmed += OnPlayerRearmed;
            _session.MatchChanged += OnMatchChanged;
            _session.Killed += OnKilled;
            _session.HitConfirmed += OnHitConfirmed;
            _session.RemoteShot += OnRemoteShot;
            _session.ProjectileSpawned += OnProjectileSpawned;
            _session.ProjectileEnded += OnProjectileEnded;

            _session.Connect(target, Time.realtimeSinceStartupAsDouble);
            _ui.ShowConnecting(_session.StatusText);
        }

        /// <summary>The server let us in: build the player and the HUD. The server places us a moment later.</summary>
        void EnterMatch()
        {
            ArenaLayout arena = ShowArena(_session.ArenaId);
            ArenaSpawn start = arena.Sides[_session.PlayerId % arena.Sides.Length][0];

            _projectiles = new ProjectileSystem();
            _rig = PlayerRigFactory.Create(start.Position, start.Yaw, movement, _library.ResolveEquipped(), _projectiles, _session);
            RangeEnvironment.ConfigureCamera(_rig.Camera.Camera);
            _menuCamera.gameObject.SetActive(false);

            _hud = HudController.Create();
            _hud.Bind(_rig.Player, _rig.Camera.Camera, null, _library);
            _hud.SetMatchMode("Leave match");
            _hud.QuitRequested += () => Leave("");
            _hud.SetPaused(false);

            _ui.HideMenu();
            _ui.ShowMatch(true);
            _ui.SetBanner("", "");
            _ui.SetNotice("");
            _ui.SetStatus("");
            _deadSince = -1f;
        }

        /// <summary>
        /// The welcome arrived: the rules, and so the map, are known. A server can play a
        /// different map from the one behind the menu, so the scene follows it.
        /// </summary>
        void OnWelcomed()
        {
            ArenaLayout arena = ShowArena(_session.ArenaId);
            if (_zoneMarkers != null) Destroy(_zoneMarkers.gameObject);
            _zoneMarkers = _session.Settings.UsesZones ? ZoneMarkers.Create(arena, _session.Settings.mode == GameModeKind.Hardpoint) : null;
        }

        /// <summary>Back to the menu, whether we chose to go or were dropped.</summary>
        void Leave(string reason)
        {
            _attempt++;
            TearDownMatch();
            StopServer();
            _ui.ShowMatch(false);
            _ui.ShowMenu(reason);
        }

        void TearDownMatch()
        {
            if (_session != null)
            {
                _session.Dispose();
                _session = null;
            }

            for (int i = 0; i < _remotes.Length; i++)
            {
                if (_remotes[i] != null) Destroy(_remotes[i].gameObject);
                _remotes[i] = null;
            }

            if (_hud != null) Destroy(_hud.gameObject);
            _hud = null;
            if (_zoneMarkers != null) Destroy(_zoneMarkers.gameObject);
            _zoneMarkers = null;
            _welcomed = false;

            if (_rig != null)
            {
                if (_rig.Player != null) Destroy(_rig.Player.gameObject);
                if (_rig.Camera != null) Destroy(_rig.Camera.gameObject);
                _rig = null;
            }
            _projectiles = null;
            if (_menuCamera != null) _menuCamera.gameObject.SetActive(true);
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ every frame

        bool _welcomed;

        void Update()
        {
            if (_session == null) return;
            double now = Time.realtimeSinceStartupAsDouble;

            // Until there is a player to do it, keep the connection attempt moving ourselves.
            if (_rig == null)
            {
                _session.Update(now);
                if (_session == null) return;
                _session.Flush(now);
            }

            if (_session.Status == ClientStatus.Failed || _session.Status == ClientStatus.Disconnected)
            {
                Leave(_session.StatusText);
                return;
            }
            if (_session.Welcomed && !_welcomed)
            {
                _welcomed = true;
                OnWelcomed();
            }
            if (_rig == null) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame) _showNetStats = !_showNetStats;

            // The pause menu and the loadout swap belong to the HUD underneath; get out of their way.
            _ui.SetMatchHidden(_hud != null && (_hud.IsPaused || _hud.IsQuickSwapOpen));
            _ui.SetHealth(_session.Local != null ? _session.Local.Health : 0f, _session.Settings.maxHealth);
            UpdateBanner();

            // Hold Tab for the scoreboard. It also comes up by itself when the match ends.
            bool board = (keyboard != null && keyboard.tabKey.isPressed) || _session.Match.Phase == MatchPhase.MatchEnd;
            if (board != _ui.ScoreboardVisible)
            {
                _ui.ShowScoreboard(board);
                _textTimer = 0f;
            }

            if (_zoneMarkers != null && _session.Local != null)
                _zoneMarkers.Show(_session.Zones, _session.ZoneCount, _session.Match.ActiveZone, _session.Local.Team);

            if (_noticeTimer > 0f)
            {
                _noticeTimer -= Time.unscaledDeltaTime;
                if (_noticeTimer <= 0f) _ui.SetNotice("");
            }

            // Text that changes at most a few times a second is not rebuilt every frame.
            _textTimer -= Time.unscaledDeltaTime;
            if (_textTimer > 0f) return;
            _textTimer = 0.1f;
            UpdateScores();
            UpdateStatus();
        }

        void UpdateScores()
        {
            MatchState match = _session.Match;
            MatchSettings rules = _session.Settings;
            ClientPlayer me = _session.Local;
            int ourTeam = me.Team;

            string clock = "";
            if (match.Phase == MatchPhase.Live || (match.Phase == MatchPhase.Waiting && match.PhaseEndTick > 0))
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt(TicksLeft(match) * SimConstants.TickDelta));
                clock = (seconds / 60) + ":" + (seconds % 60).ToString("00");
            }
            string round = match.Phase == MatchPhase.Waiting ? "WARM-UP"
                : rules.RoundBased ? "ROUND " + match.Round
                : MatchSettings.Title(rules.mode).ToUpperInvariant();

            if (rules.TeamBased)
            {
                string ours = "YOUR TEAM", theirs = "ENEMY TEAM";
                if (rules.teamSize == 1)
                {
                    // One on one: names say more than "team".
                    ClientPlayer opponent = FirstOnTeam(1 - ourTeam);
                    ours = me.Name;
                    theirs = opponent != null ? opponent.Name : "-";
                }
                _ui.SetScore(ours, match.Score(ourTeam).ToString(), theirs, match.Score(1 - ourTeam).ToString(), round, clock);
            }
            else
            {
                // Everyone for themselves: you against whoever is doing best.
                ClientPlayer leader = BestOther();
                _ui.SetScore("YOU", ScoreText(me), leader != null ? leader.Name : "-", leader != null ? ScoreText(leader) : "0", round, clock);
            }

            bool hardpoint = rules.mode == GameModeKind.Hardpoint;
            ArenaLayout arena = _zoneMarkers != null ? _zoneMarkers.Arena : null;
            string[] names = null;
            if (arena != null)
            {
                ArenaZone[] areas = hardpoint ? arena.Hardpoints : arena.Zones;
                names = new string[areas.Length];
                for (int i = 0; i < areas.Length; i++) names[i] = areas[i].Name;
            }
            _ui.SetZones(_session.Zones, _session.ZoneCount, match.ActiveZone, ourTeam, hardpoint, names);

            if (_ui.ScoreboardVisible) FillScoreboard(match, rules, ourTeam);

            _ui.SetNetStats(!_showNetStats ? "" :
                "Ping " + Mathf.RoundToInt(_session.RttMs) + " ms    Loss " + Mathf.RoundToInt(_session.LossPercent) + "%    Corrections " + _session.Corrections +
                (simulatedConnection.Any ? "\nSimulated: +" + simulatedConnection.latencyMs + " ms each way, " + simulatedConnection.lossPercent + "% loss" : "") +
                "\nF3 hides this   ·   Tab scores");
        }

        /// <summary>A player's number on the score line: Gun Game shows which weapon they are on.</summary>
        string ScoreText(ClientPlayer player) => _session.Settings.mode == GameModeKind.GunGame
            ? Mathf.Min(player.Stats.Score + 1, GunGameLadder.Length) + "/" + GunGameLadder.Length
            : player.Stats.Score.ToString();

        void FillScoreboard(in MatchState match, MatchSettings rules, int ourTeam)
        {
            _rows.Clear();
            foreach (ClientPlayer player in _session.Players)
            {
                if (!player.Present) continue;
                _rows.Add(new ScoreRow
                {
                    Name = player.Name,
                    Team = player.Team,
                    Kills = player.Stats.Kills,
                    Deaths = player.Stats.Deaths,
                    Score = player.Stats.Score,
                    Local = player.Id == _session.PlayerId,
                    Bot = player.Bot,
                    Alive = player.Alive,
                });
            }
            string title = MatchSettings.Title(rules.mode).ToUpperInvariant() + "   ·   " + ArenaLayout.Title(rules.arenaId).ToUpperInvariant() +
                           "   ·   " + Objective(rules).ToUpperInvariant();
            string scoreLabel = rules.mode == GameModeKind.GunGame ? "WEAPON" : rules.mode == GameModeKind.FreeForAll ? "KILLS" : "SCORE";
            _ui.SetScoreboard(title, _rows, rules.TeamBased, ourTeam, scoreLabel, match.Score0, match.Score1);
        }

        /// <summary>The line above the health bar: a respawn countdown, or Gun Game's progress.</summary>
        void UpdateStatus()
        {
            MatchSettings rules = _session.Settings;
            MatchState match = _session.Match;
            ClientPlayer me = _session.Local;
            bool playing = match.Phase == MatchPhase.Live || match.Phase == MatchPhase.Waiting;

            if (playing && (!me.Spawned || !me.Alive))
            {
                if (rules.RoundBased && match.Phase == MatchPhase.Live)
                {
                    _ui.SetStatus(me.Spawned ? "OUT UNTIL THE NEXT ROUND" : "YOU JOIN AT THE NEXT ROUND");
                }
                else if (_deadSince >= 0f)
                {
                    float wait = match.Phase == MatchPhase.Waiting ? 1.5f : rules.respawnSeconds;
                    int left = Mathf.Max(1, Mathf.CeilToInt(wait - (Time.unscaledTime - _deadSince)));
                    _ui.SetStatus("BACK IN " + left);
                }
                else
                {
                    _ui.SetStatus("");
                }
                return;
            }

            if (rules.mode == GameModeKind.GunGame && me.Alive)
            {
                int level = Mathf.Clamp(me.Stats.Score, 0, GunGameLadder.Length - 1);
                string next = level + 1 < GunGameLadder.Length ? DisplayName(GunGameLadder.Weapons[level + 1]) : "the win";
                _ui.SetStatus("WEAPON " + (level + 1) + " OF " + GunGameLadder.Length + "   ·   NEXT: " + next.ToUpperInvariant());
                return;
            }
            _ui.SetStatus("");
        }

        ClientPlayer FirstOnTeam(int team)
        {
            foreach (ClientPlayer player in _session.Players)
            {
                if (player.Present && player.Team == team && player.Id != _session.PlayerId) return player;
            }
            return null;
        }

        /// <summary>The best-placed player other than us.</summary>
        ClientPlayer BestOther()
        {
            ClientPlayer best = null;
            foreach (ClientPlayer player in _session.Players)
            {
                if (!player.Present || player.Id == _session.PlayerId) continue;
                if (best == null || player.Stats.Score > best.Stats.Score) best = player;
            }
            return best;
        }

        /// <summary>Where we finished in a mode without teams: 1 for first.</summary>
        int Placing()
        {
            int place = 1;
            int ours = _session.Local.Stats.Score;
            foreach (ClientPlayer player in _session.Players)
            {
                if (player.Present && player.Id != _session.PlayerId && player.Stats.Score > ours) place++;
            }
            return place;
        }

        static string Ordinal(int n) => n + (n % 100 >= 11 && n % 100 <= 13 ? "th" : n % 10 == 1 ? "st" : n % 10 == 2 ? "nd" : n % 10 == 3 ? "rd" : "th");

        /// <summary>What wins the match, in a few words.</summary>
        static string Objective(MatchSettings rules)
        {
            switch (rules.mode)
            {
                case GameModeKind.Duel:
                case GameModeKind.Elimination: return "first to " + rules.roundsToWin + " rounds";
                case GameModeKind.TeamDeathmatch: return "first team to " + rules.scoreLimit + " kills";
                case GameModeKind.FreeForAll: return "first to " + rules.scoreLimit + " kills";
                case GameModeKind.Domination:
                case GameModeKind.Hardpoint: return "first team to " + rules.scoreLimit + " points";
                case GameModeKind.GunGame: return "a kill with every weapon";
                default: return "";
            }
        }

        float TicksLeft(in MatchState match) => (float)(match.PhaseEndTick - _session.ArrivalTick);

        void UpdateBanner()
        {
            MatchState match = _session.Match;
            MatchSettings rules = _session.Settings;
            int ourTeam = _session.Local != null ? _session.Local.Team : Teams.None;
            switch (match.Phase)
            {
                case MatchPhase.Waiting:
                    if (match.PhaseEndTick > 0)
                    {
                        int left = Mathf.Max(1, Mathf.CeilToInt(TicksLeft(match) * SimConstants.TickDelta));
                        _ui.SetBanner("STARTING IN " + left, "Waiting a moment for anyone else on their way" + (_joinHint.Length > 0 ? "\n" + _joinHint : ""));
                    }
                    else
                    {
                        _ui.SetBanner("WAITING FOR PLAYERS", (rules.TeamBased ? "A match needs someone on each team." : "A match needs at least two players.") +
                                                              (_joinHint.Length > 0 ? "\n" + _joinHint : ""));
                    }
                    break;

                case MatchPhase.Countdown:
                {
                    // Counted against when our commands reach the server, the same clock that
                    // decides when we can move: the number hits zero as our controls come alive.
                    float left = (float)(match.PhaseEndTick - _session.CommandArrivalTick) * SimConstants.TickDelta;
                    string what = MatchSettings.Title(rules.mode) + "   ·   " + (rules.RoundBased ? "Round " + match.Round + "   ·   " : "") + Objective(rules);
                    if (left <= 0f) _ui.SetBanner("FIGHT", "");
                    else _ui.SetBanner(Mathf.Clamp(Mathf.CeilToInt(left), 1, 9).ToString(), what);
                    break;
                }

                case MatchPhase.Live:
                    if (_fightTimer > 0f)
                    {
                        _fightTimer -= Time.unscaledDeltaTime;
                        _ui.SetBanner("FIGHT", "");
                    }
                    else
                    {
                        _ui.SetBanner("", "");
                    }
                    break;

                case MatchPhase.RoundEnd:
                    if (match.RoundWinner == MatchState.Nobody) _ui.SetBanner("DRAW", "Nobody takes the round");
                    else if (match.RoundWinner == ourTeam) _ui.SetBanner("ROUND WON", "B  changes loadout for the next round");
                    else _ui.SetBanner("ROUND LOST", "B  changes loadout for the next round", good: false);
                    break;

                case MatchPhase.MatchEnd:
                {
                    if (rules.TeamBased)
                    {
                        string score = match.Score(ourTeam) + "  -  " + match.Score(1 - ourTeam);
                        bool forfeit = FirstOnTeam(1 - ourTeam) == null;
                        if (match.MatchWinner == MatchState.Nobody) _ui.SetBanner("DRAW", score);
                        else if (match.MatchWinner == ourTeam) _ui.SetBanner("VICTORY", forfeit ? "The other side left the match" : score);
                        else _ui.SetBanner("DEFEAT", score, good: false);
                    }
                    else
                    {
                        int winner = match.MatchWinner;
                        string name = winner >= 0 && winner < _session.Players.Length ? _session.Players[winner].Name : "";
                        if (winner == _session.PlayerId) _ui.SetBanner("VICTORY", "You came first");
                        else if (winner == MatchState.Nobody) _ui.SetBanner("DRAW", "Time ran out with the lead shared");
                        else _ui.SetBanner("DEFEAT", name + " wins   ·   you came " + Ordinal(Placing()), good: false);
                    }
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ what the server tells us

        void OnMatchChanged(MatchState previous, MatchState state)
        {
            if (state.Phase == MatchPhase.Live && previous.Phase != MatchPhase.Live) _fightTimer = 1f;

            // A new round starts with a clean sky.
            if (state.Phase == MatchPhase.Countdown || state.Phase == MatchPhase.Waiting) _projectiles?.Clear();
        }

        void OnPlayerChanged(ClientPlayer player)
        {
            if (player.Present || _remotes[player.Id] == null) return;
            Destroy(_remotes[player.Id].gameObject);
            _remotes[player.Id] = null;
        }

        void OnPlayerSpawned(ClientPlayer player, SpawnMessage spawn)
        {
            if (player.Id == _session.PlayerId)
            {
                // Whatever loadout we asked for is now the one in our hands.
                _deadSince = -1f;
                _noticeTimer = 0f;
                _ui.SetNotice("");
                return;
            }
            if (_remotes[player.Id] == null) _remotes[player.Id] = RemotePlayerView.Create(_session, player.Id, movement);
        }

        void OnPlayerRearmed(ClientPlayer player, RearmMessage rearm)
        {
            if (player.Id != _session.PlayerId || _rig == null) return;
            // Gun Game: a kill moved us up a weapon.
            WeaponBuild build = BuildOf(player.Id, rearm.ActiveSlot);
            _ui.SetNotice(build != null ? "NEW WEAPON: " + build.Weapon.displayName.ToUpperInvariant() : "");
            _noticeTimer = 2f;
            _rig.Sfx.Play(Sfx.Swap, 1f);
        }

        void OnEquippedChanged()
        {
            if (_session == null || _session.Status != ClientStatus.Connected) return;
            _session.SetLoadout(_library.Equipped);
            MatchSettings rules = _session.Settings;
            _ui.SetNotice(rules.mode == GameModeKind.GunGame
                ? "Gun Game hands out its own weapons; your loadout is for other modes"
                : "Loadout \"" + _library.Equipped.name + "\" will be used from your " + (rules.RoundBased ? "next round" : "next spawn"));
            _noticeTimer = 5f;
        }

        void OnKilled(KillMessage kill)
        {
            ClientPlayer victimPlayer = _session.Players[kill.Victim];
            string victim = victimPlayer.Name;
            if (kill.Victim == _session.PlayerId)
            {
                _deadSince = Time.unscaledTime;
                if (_rig != null) _rig.Sfx.Play(Sfx.Land, 0.9f);
            }

            if (kill.Killer == KillMessage.NoKiller || kill.Killer >= _session.Players.Length)
            {
                _ui.AddKill(victim + " fell out of the arena", Tone(kill.Victim, victimPlayer.Team, victimIsSubject: true));
                return;
            }
            ClientPlayer killer = _session.Players[kill.Killer];
            string text = kill.Killer == kill.Victim
                ? victim + "   [" + DisplayName(kill.WeaponId) + "]   themselves"
                : killer.Name + "   [" + DisplayName(kill.WeaponId) + (kill.Headshot ? ", headshot" : "") + "]   " + victim;
            _ui.AddKill(text, kill.Killer == kill.Victim ? Tone(kill.Victim, victimPlayer.Team, true) : Tone(kill.Killer, killer.Team, false));
        }

        /// <summary>
        /// Green when it went our way, red when it did not: a kill by us or our team, or a death
        /// of theirs. <paramref name="victimIsSubject"/> means the player named is the one who died.
        /// </summary>
        FeedTone Tone(int playerId, int team, bool victimIsSubject)
        {
            bool friendly = playerId == _session.PlayerId ||
                            (_session.Settings.TeamBased && _session.Local != null && team == _session.Local.Team);
            if (!friendly && !_session.Settings.TeamBased && playerId != _session.PlayerId) return FeedTone.Neutral;
            return friendly != victimIsSubject ? FeedTone.Ours : FeedTone.Theirs;
        }

        string DisplayName(string id)
        {
            WeaponDefinition weapon = catalog.weapons != null ? catalog.weapons.FindWeapon(id) : null;
            if (weapon != null) return weapon.displayName;
            UtilityDefinition utility = catalog.FindUtility(id);
            return utility != null ? utility.displayName : id;
        }

        void OnHitConfirmed(HitMessage hit)
        {
            if (hit.Victim < _remotes.Length && _remotes[hit.Victim] != null) _remotes[hit.Victim].Flash();
        }

        /// <summary>The other player fired: draw the tracer from their weapon and play the shot, quieter with distance.</summary>
        void OnRemoteShot(ShotMessage shot)
        {
            if (_rig == null) return;
            WeaponBuild build = BuildOf(shot.Shooter, shot.Slot);
            if (build == null) return;

            if (shot.Kind == ShotKind.Bullet)
            {
                RemotePlayerView view = _remotes[shot.Shooter];
                Vector3 from = view != null && view.Visible ? view.Muzzle : shot.Origin;
                _rig.Fx.PlayTracer(new ShotPresentation
                {
                    Origin = shot.Origin,
                    End = shot.End,
                    Normal = shot.Normal,
                    HitWorld = shot.HitWorld,
                    Build = build,
                }, from);
            }
            if (shot.First) _rig.Sfx.PlayShot(build, Loudness(shot.Origin));
        }

        /// <summary>The server launched a projectile: start drawing our own picture of it.</summary>
        void OnProjectileSpawned(ProjectileSpawnMessage spawn)
        {
            if (_rig == null) return;
            ClientPlayer owner = _session.Players[spawn.Owner];
            if (owner.Loadout == null) return;
            bool ours = spawn.Owner == _session.PlayerId;

            WeaponStats stats;
            string id;
            Color body, accent;
            if (spawn.Source >= ProjectileSpawnMessage.UtilityBase)
            {
                int slot = spawn.Source - ProjectileSpawnMessage.UtilityBase;
                UtilityDefinition item = slot < owner.Loadout.Utilities.Length ? owner.Loadout.Utilities[slot] : null;
                if (item == null) return;
                stats = item.projectile;
                id = item.utilityId;
                body = accent = item.color;
                if (!ours) _rig.Sfx.Play(Sfx.Throw, 0.6f * Loudness(spawn.Origin), 0.06f);
            }
            else
            {
                WeaponBuild build = BuildOf(spawn.Owner, spawn.Source);
                if (build == null) return;
                stats = owner.Loadout.ResolveStats(spawn.Source);
                id = build.Weapon.weaponId;
                body = build.Weapon.view.bodyColor;
                accent = build.Weapon.view.accentColor;
                if (!ours) _rig.Sfx.PlayShot(build, Loudness(spawn.Origin));
            }

            _projectiles.Spawn(spawn.Origin, spawn.Direction, stats, id, 1f, null, spawn.Id);
            _rig.Fx.TrackProjectile(spawn.Id, stats, body, accent, fromOwnMuzzle: ours);

            // If the news arrived late, put the picture where the real thing is by now.
            int late = Mathf.Clamp((int)(_session.ArrivalTick - spawn.Tick), 0, 30);
            if (late > 0) _projectiles.FastForward(spawn.Id, late, SimConstants.TickDelta);
        }

        void OnProjectileEnded(ProjectileEndMessage end)
        {
            if (_rig == null) return;
            _projectiles.Remove(end.Id, out _);
            _rig.Fx.OnProjectileEvent(new ProjectileEvent
            {
                Kind = end.Kind,
                ProjectileId = end.Id,
                Point = end.Point,
                Normal = end.Normal,
                Radius = end.Radius,
                HitTarget = end.HitTarget,
            });
            if (end.Kind == ProjectileEventKind.Explosion) _rig.Sfx.Play(Sfx.Explosion, Loudness(end.Point), 0.06f);
        }

        WeaponBuild BuildOf(int playerId, int slot)
        {
            Loadout loadout = _session.Players[playerId].Loadout;
            return loadout != null && slot >= 0 && slot < loadout.Weapons.Length ? loadout.Weapons[slot] : null;
        }

        /// <summary>Sound has no position yet, so distance just turns it down.</summary>
        float Loudness(Vector3 source)
        {
            float distance = Vector3.Distance(_rig.Camera.transform.position, source);
            return Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(distance / 45f));
        }

        // ------------------------------------------------------------------ the scene

        /// <summary>
        /// The arena as the client sees it: the shared collision, plus something to look at.
        /// Asking for the one already shown does nothing; another one replaces it.
        /// </summary>
        ArenaLayout ShowArena(string arenaId)
        {
            ArenaLayout layout = ArenaLayout.Find(arenaId);
            new UnityGameWorld(server: false).BuildArena(layout);
            if (_shownArenaId == layout.Id && _arenaRoot != null) return layout;

            if (_arenaRoot != null) Destroy(_arenaRoot.gameObject);
            _shownArenaId = layout.Id;
            var root = new GameObject("Arena (" + layout.DisplayName + ")").transform;
            root.SetParent(transform, false);
            _arenaRoot = root;
            if (_menuCamera != null) AimMenuCamera(layout);
            foreach (ArenaBox box in layout.Boxes)
            {
                Color color = box.Paint == ArenaPaint.Floor ? FloorColor
                    : box.Paint == ArenaPaint.Wall ? WallColor
                    : box.Paint == ArenaPaint.Cover ? CoverColor
                    : AccentColor;
                GreyboxKit.Box(root, "Block", box.Center, box.Size, color, solid: false);
            }
            return layout;
        }

        /// <summary>Looks down on the arena behind the menu.</summary>
        void CreateMenuCamera()
        {
            var go = new GameObject("Menu Camera");
            _menuCamera = go.AddComponent<Camera>();
            _menuCamera.fieldOfView = 60f;
            _menuCamera.farClipPlane = 600f;
            go.AddComponent<AudioListener>();
            RangeEnvironment.ConfigureCamera(_menuCamera);
            AimMenuCamera(ArenaLayout.Find(_shownArenaId));
        }

        /// <summary>Far enough back to take in the whole map.</summary>
        void AimMenuCamera(ArenaLayout layout)
        {
            float size = 0f;
            foreach (ArenaBox box in layout.Boxes) size = Mathf.Max(size, Mathf.Max(box.Size.x, box.Size.z));
            float scale = Mathf.Max(1f, size / 40f);
            _menuCamera.transform.SetPositionAndRotation(new Vector3(-6f, 19f, -27f) * scale, Quaternion.Euler(36f, 12f, 0f));
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>This computer's address on the local network, for telling a friend what to type.</summary>
        static string LocalAddress()
        {
            try
            {
                foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address)) return address.ToString();
                }
            }
            catch (Exception)
            {
                // No network name: fall through to the generic wording.
            }
            return "this computer's address";
        }

        static bool HasArg(string[] args, string flag) => Array.IndexOf(args, flag) >= 0;

        static string ArgText(string[] args, string flag)
        {
            int index = Array.IndexOf(args, flag);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        static float ArgNumber(string[] args, string flag, float fallback)
        {
            string text = ArgText(args, flag);
            return text != null && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
        }
    }
}
