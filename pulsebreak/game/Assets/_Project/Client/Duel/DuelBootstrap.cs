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

        // What has already been announced, so each sound and medal plays once.
        readonly ZoneState[] _heardZones = new ZoneState[NetMessages.MaxZones];
        int _heardZoneCount = -1, _heardActiveZone = -1, _heardCountdown = -1;
        bool _heardGo, _heardMatchStart;
        float _heartbeatTimer, _lastKillTime = -10f;
        int _streak, _multiKill;
        Vector3 _menuFocus, _menuOffset;

        // Chat in this match: who we have muted, and what each player said (for reports).
        readonly HashSet<int> _muted = new HashSet<int>();
        readonly List<(int Player, string Text)> _chatLog = new List<(int, string)>();
        bool _wasTyping;

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
            GraphicsSetup.Initialize();
            SoundBank.Prewarm();
            MusicPlayer.SetMenu(true);
            _services = GameServices.Get(catalog, backendUrl);
            _library = _services.Library;
            _library.EquippedChanged += OnEquippedChanged;

            ShowArena(server.match.arenaId);
            CreateMenuCamera();

            _ui = DuelUi.Create(_services);
            _ui.FindRequested += Find;
            _ui.ProgressRequested += () => ProgressScreen.Open(_services, catalog);
            _ = _services.RefreshProgressionAsync();
            _ui.HostRequested += Host;
            _ui.JoinRequested += Join;
            _ui.CancelRequested += () =>
            {
                // A party member stops following the leader's search by leaving the party.
                if (_followingParty && _services.Social != null) _ = _services.Social.LeavePartyAsync();
                _followingParty = false;
                Leave("");
            };
            if (_services.Social != null)
            {
                _services.Social.Status = SocialClient.Menu;
                _services.Social.PartySearchStarted += OnPartySearch;
            }
            _ui.RangeRequested += () => SceneManager.LoadScene(RangeScene);
            _ui.CustomizeRequested += () => CustomizeScreen.Open(_services.Cosmetics, _library, movement);
            _ui.ChatSubmitted += (text, team) => _session?.SendChat(text, team);
            _services.Cosmetics.Changed += OnLookChanged;
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
            if (_services != null && _services.Social != null) _services.Social.PartySearchStarted -= OnPartySearch;
            if (_library != null) _library.EquippedChanged -= OnEquippedChanged;
            if (_services != null && _services.Cosmetics != null) _services.Cosmetics.Changed -= OnLookChanged;
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
        // The matchmade match being played, to find its results afterwards.
        string _matchmadeId = "";

        async void Find(GameModeKind mode, bool ranked)
        {
            if (_session != null) return;
            int attempt = ++_attempt;
            MatchmakingClient matchmaking = _services.Matchmaking;
            string name = PlayerName();
            _ui.ShowConnecting("Joining the " + MatchSettings.Title(mode) + " queue...");

            AccountResult<MatchTicketData> queued = await matchmaking.JoinQueueAsync(mode, name, ranked);
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
            _matchmadeId = ticket.matchId;
            UiSounds.Play(Sfx.MatchFound);
            ConnectWithTicket(target, ticket.joinTicket);
        }

        bool _followingParty;

        /// <summary>
        /// The party leader started a search: follow it, and join the match it finds with a
        /// ticket of our own. Cancel leaves the party.
        /// </summary>
        void OnPartySearch(PartySearchData search) => FollowParty(search);

        async void FollowParty(PartySearchData search)
        {
            if (_session != null || _ui == null) return;
            int attempt = ++_attempt;
            _followingParty = true;
            Playlist playlist = Playlists.Find(search.playlist);
            string what = playlist != null ? "a " + playlist.Title + " match" : "a match";
            string name = PlayerName();
            UiSounds.Play(Sfx.UiClick);

            MatchTicketData ticket = null;
            while (true)
            {
                _ui.ShowConnecting("Your party leader is finding " + what + ". You will join it automatically.\nCancel leaves the party.");
                AccountResult<MatchTicketData> check = await _services.Matchmaking.CheckAsync(search.ticketId, name);
                if (this == null || attempt != _attempt) return;
                if (check.Ok && check.Value != null)
                {
                    ticket = check.Value;
                    if (!ticket.Searching) break;
                }
                else if (!check.Offline)
                {
                    _followingParty = false;
                    _ui.ShowMenu("The party's search has ended.");
                    return;
                }
                await Task.Delay(1000);
                if (this == null || attempt != _attempt) return;
            }

            _followingParty = false;
            if (!ticket.Matched || !NetAddress.TryParse(ticket.address, NetProtocol.DefaultPort, out NetAddress target))
            {
                _ui.ShowMenu(ticket.status == "expired" ? "The party's search ran out of time." : "The party leader stopped the search.");
                return;
            }
            _joinHint = "";
            _matchmadeId = ticket.matchId;
            UiSounds.Play(Sfx.MatchFound);
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
            _session = new ClientSession(socket, catalog, PlayerName(), _library.Equipped, ticket: ticket, look: _services.Cosmetics.Look);
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
            _session.Hurt += OnHurt;
            _session.ChatReceived += OnChat;

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

            MusicPlayer.SetMenu(false);
            ScreenEffects.Calm();
            _hud.AddPauseButton("Players", OpenPlayers);
            _muted.Clear();
            _chatLog.Clear();
            if (_services.Social != null) _services.Social.Status = SocialClient.Match;
            _heardZoneCount = _heardActiveZone = _heardCountdown = -1;
            _heardGo = _heardMatchStart = false;
            _streak = _multiKill = 0;
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
            if (_session.Settings.weapons != WeaponRule.Any)
            {
                _ui.SetNotice(WeaponRules.Title(_session.Settings.weapons).ToUpperInvariant() + " in this match: anything else in your loadout is swapped");
                _noticeTimer = 8f;
            }
        }

        /// <summary>
        /// A matchmade match is over and the server has sent everyone home. Back to the menu,
        /// and as soon as the backend has counted the match, a line saying what it earned.
        /// </summary>
        async void AfterMatch()
        {
            string matchId = _matchmadeId;
            Leave("");
            _ui.ShowMenuResult("Match over." + (_services.Account.SignedIn ? " Counting your XP..." : " Sign in to earn XP from matches."));
            if (!_services.Account.SignedIn || string.IsNullOrEmpty(matchId)) return;

            int attempt = _attempt;
            for (int tries = 0; tries < 10; tries++)
            {
                ProgressionData progression = await _services.RefreshProgressionAsync();
                if (this == null || attempt != _attempt) return;
                if (progression != null && progression.lastMatch.matchId == matchId)
                {
                    _ui.ShowMenuResult(Summary(progression));
                    if (progression.lastMatch.levelAfter > progression.lastMatch.levelBefore) MusicPlayer.Play(Stinger.LevelUp);
                    else if (progression.lastMatch.challengesCompleted.Length > 0) UiSounds.Play(Sfx.ChallengeDone);
                    return;
                }
                await System.Threading.Tasks.Task.Delay(1000);
                if (this == null || attempt != _attempt) return;
            }
            _ui.ShowMenuResult("Match over. Its results are still being counted; see Progress shortly.");
        }

        static string Summary(ProgressionData progression)
        {
            LastMatchData last = progression.lastMatch;
            string outcome = last.outcome == 2 ? "Victory" : last.outcome == 1 ? "Draw" : "Defeat";
            string text = outcome + ".  +" + last.xpGained + " XP";
            if (last.levelAfter > last.levelBefore) text += ", level up to " + last.levelAfter + "!";
            if (last.ranked)
            {
                int change = last.ratingAfter - last.ratingBefore;
                text += "   ·   Rating " + last.ratingAfter + " (" + (change >= 0 ? "+" : "") + change + ")";
            }
            if (last.challengesCompleted.Length > 0) text += "\nChallenge done: " + string.Join(", ", last.challengesCompleted);
            return text;
        }

        /// <summary>Back to the menu, whether we chose to go or were dropped.</summary>
        void Leave(string reason)
        {
            _attempt++;
            TearDownMatch();
            StopServer();
            _ui.ShowMatch(false);
            _ui.ShowMenu(reason);
            MusicPlayer.SetMenu(true);
            ScreenEffects.Calm();
            TextEntry.Active = false;
            if (_services.Social != null) _services.Social.Status = SocialClient.Menu;
            if (!string.IsNullOrEmpty(reason)) UiSounds.Play(Sfx.UiError);
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
            DriftMenuCamera();
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
                if (_session.EndedByMatchOver) AfterMatch();
                else Leave(_session.StatusText);
                return;
            }
            if (_session.Welcomed && !_welcomed)
            {
                _welcomed = true;
                OnWelcomed();
            }
            if (_rig == null) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame && !TextEntry.Active) _showNetStats = !_showNetStats;
            UpdateChatKeys(keyboard);

            // The pause menu and the loadout swap belong to the HUD underneath; get out of their way.
            _ui.SetMatchHidden(_hud != null && (_hud.IsPaused || _hud.IsQuickSwapOpen));
            _ui.SetHealth(_session.Local != null ? _session.Local.Health : 0f, _session.Settings.maxHealth);
            UpdateHealthEffects();
            UpdateBanner();
            UpdateObjectiveSounds();

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
                    if (left <= 0f)
                    {
                        _ui.SetBanner("FIGHT", "");
                        PlayGo();
                    }
                    else
                    {
                        int count = Mathf.Clamp(Mathf.CeilToInt(left), 1, 9);
                        _ui.SetBanner(count.ToString(), what);
                        if (count != _heardCountdown && count <= 3) UiSounds.Play(Sfx.CountdownTick);
                        _heardCountdown = count;
                        _heardGo = false;
                    }
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
            if (state.Phase == MatchPhase.Live && previous.Phase != MatchPhase.Live)
            {
                _fightTimer = 1f;
                PlayGo();
            }

            // A new round starts with a clean sky, and clean walls.
            if (state.Phase == MatchPhase.Countdown || state.Phase == MatchPhase.Waiting)
            {
                _projectiles?.Clear();
                if (_rig != null) _rig.Fx.ClearWorld();
                _heardCountdown = -1;
            }
            if (state.Phase == MatchPhase.Waiting) _heardMatchStart = false;

            int ourTeam = _session.Local != null ? _session.Local.Team : Teams.None;
            if (state.Phase == MatchPhase.RoundEnd && previous.Phase != MatchPhase.RoundEnd && state.RoundWinner != MatchState.Nobody)
                MusicPlayer.Play(state.RoundWinner == ourTeam ? Stinger.RoundWon : Stinger.RoundLost);
            if (state.Phase == MatchPhase.MatchEnd && previous.Phase != MatchPhase.MatchEnd && state.MatchWinner != MatchState.Nobody)
            {
                bool won = _session.Settings.TeamBased ? state.MatchWinner == ourTeam : state.MatchWinner == _session.PlayerId;
                MusicPlayer.Play(won ? Stinger.Victory : Stinger.Defeat);
            }
        }

        /// <summary>The countdown reached zero: a bright tone, and at the start of a match a musical sting.</summary>
        void PlayGo()
        {
            if (_heardGo) return;
            _heardGo = true;
            if (!_heardMatchStart)
            {
                _heardMatchStart = true;
                MusicPlayer.Play(Stinger.MatchStart);
            }
            UiSounds.Play(Sfx.CountdownGo);
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
                _ui.ShowDeath(null, null);
                ScreenEffects.Calm();
                if (_rig != null) _rig.Sfx.Play(Sfx.Spawn, 0.6f);
                return;
            }
            if (_remotes[player.Id] == null) _remotes[player.Id] = RemotePlayerView.Create(_session, player.Id, movement);
            // Someone arriving mid-fight shimmers in; everyone appearing for a new round does not.
            if (_rig != null && _session.Match.Phase == MatchPhase.Live)
            {
                _rig.Fx.PlaySpawn(spawn.Position, TrimOf(player.Id));
                _rig.Sfx.PlayAt(Sfx.Spawn, spawn.Position, 0.7f, 0.05f, 2f, 40f);
            }
        }

        void OnPlayerRearmed(ClientPlayer player, RearmMessage rearm)
        {
            if (player.Id != _session.PlayerId || _rig == null) return;
            // Gun Game: a kill moved us up a weapon.
            WeaponBuild build = BuildOf(player.Id, rearm.ActiveSlot);
            _ui.SetNotice(build != null ? "NEW WEAPON: " + build.Weapon.displayName.ToUpperInvariant() : "");
            _noticeTimer = 2f;
            _rig.Sfx.Play(Sfx.Swap, 1f);
            UiSounds.Play(Sfx.Promote);
        }

        /// <summary>A new look chosen mid-match: everyone else sees it straight away.</summary>
        void OnLookChanged()
        {
            if (_session != null && _session.Status == ClientStatus.Connected) _session.SetLook(_services.Cosmetics.Look);
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
            bool byUs = kill.Killer == _session.PlayerId && kill.Victim != _session.PlayerId;
            if (kill.Victim == _session.PlayerId)
            {
                _deadSince = Time.unscaledTime;
                _streak = 0;
                if (_rig != null) _rig.Sfx.Play(Sfx.Death, 0.9f);
                ShowWhoGotUs(kill);
            }
            else
            {
                // Their body comes apart where it stood.
                RemotePlayerView view = _remotes[kill.Victim];
                if (_rig != null && view != null && view.Visible)
                    _rig.Fx.PlayDerez(view.Feet, 1.8f, SuitOf(kill.Victim), TrimOf(kill.Victim));
            }
            if (byUs) AwardMedal(kill, victim);

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
            if (_rig != null && hit.Victim < _session.Players.Length)
                _rig.Fx.PlayBodyHit(hit.Point, hit.Point - _rig.Camera.transform.position, TrimOf(hit.Victim), hit.Zone == Shared.Combat.HitZone.Head);
        }

        /// <summary>We were hurt: a sound, an arc pointing at the source, a red edge and a flinch.</summary>
        void OnHurt(HurtMessage hurt)
        {
            if (_rig == null) return;
            float share = Mathf.Clamp01(hurt.Damage / Mathf.Max(1f, _session.Settings.maxHealth));
            bool head = hurt.Zone == Shared.Combat.HitZone.Head;
            _rig.Sfx.Play(head ? Sfx.HurtHead : Sfx.Hurt, Mathf.Lerp(0.55f, 1f, share * 3f), 0.05f);
            ScreenEffects.Hurt(share);
            Transform eye = _rig.Camera.transform;
            Vector3 toward = hurt.Source - eye.position;
            bool fromAway = toward.sqrMagnitude > 0.25f;
            if (fromAway && _hud != null) _hud.ShowHurt(hurt.Source, share * 3f);
            if (fromAway) _rig.Camera.AddFlinch(Mathf.Clamp01(share * 2.5f), Vector3.Dot(eye.right, toward.normalized));
        }

        // ------------------------------------------------------------------ chat and players

        /// <summary>Enter or T to chat to everyone, Y to the team; nothing moves while typing.</summary>
        void UpdateChatKeys(Keyboard keyboard)
        {
            bool typing = TextEntry.Active;
            if (typing) _rig.Player.InputEnabled = false;
            else if (_wasTyping && _hud != null && !_hud.IsPaused && !_hud.IsQuickSwapOpen) _rig.Player.InputEnabled = true;
            _wasTyping = typing;

            if (keyboard == null || typing || !GameSettings.ShowChat || _hud == null || _hud.IsPaused || _hud.IsQuickSwapOpen) return;
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.tKey.wasPressedThisFrame) _ui.OpenChat(false);
            else if (keyboard.yKey.wasPressedThisFrame) _ui.OpenChat(_session.Settings.TeamBased);
        }

        void OnChat(ChatLineMessage line)
        {
            ClientPlayer speaker = _session.Players[line.PlayerId];
            _chatLog.Add((line.PlayerId, line.Text));
            if (_chatLog.Count > 200) _chatLog.RemoveAt(0);
            if (!GameSettings.ShowChat || _muted.Contains(line.PlayerId)) return;
            if (_services.Social != null && _services.Social.IsBlocked(speaker.AccountId)) return;

            string text = GameSettings.FilterChat ? Shared.Social.ChatText.Filter(line.Text) : line.Text;
            _ui.AddChat(speaker.Name, text, SideColor(speaker), line.TeamOnly);
            UiSounds.Play(Sfx.UiToggle, 0.35f);
        }

        /// <summary>White for us, blue for a teammate, red for an opponent; grey without teams.</summary>
        Color SideColor(ClientPlayer player)
        {
            if (player.Id == _session.PlayerId) return Color.white;
            if (!_session.Settings.TeamBased || _session.Local == null) return new Color(0.75f, 0.78f, 0.82f);
            return player.Team == _session.Local.Team ? new Color32(96, 190, 255, 255) : DuelUi.Bad;
        }

        void OpenPlayers()
        {
            SocialClient social = _services.Social;
            PlayersScreen.Open(new PlayerActions
            {
                List = PlayerRows,
                SignedIn = () => _services.Account.SignedIn && social != null,
                ToggleMute = row =>
                {
                    bool muted = _muted.Add(row.Id);
                    if (!muted) _muted.Remove(row.Id);
                    _ui.AddChatNotice(row.Name + (muted ? " is muted for this match." : " is no longer muted."));
                },
                AddFriend = row => _ = social?.AddFriendAsync(row.Name),
                Block = row =>
                {
                    _ = social?.BlockAsync(row.AccountId);
                    _ui.AddChatNotice(row.Name + " is blocked.");
                },
                Report = row => ReportDialog.Open(row.Name, row.AccountId, _matchmadeId ?? "", ChatOf(row.Id), report => social.ReportAsync(report)),
            });
        }

        List<PlayerRow> PlayerRows()
        {
            var rows = new List<PlayerRow>();
            if (_session == null) return rows;
            SocialClient social = _services.Social;
            foreach (ClientPlayer player in _session.Players)
            {
                if (!player.Present) continue;
                bool teams = _session.Settings.TeamBased && _session.Local != null;
                rows.Add(new PlayerRow
                {
                    Id = player.Id,
                    Name = player.Name,
                    AccountId = player.AccountId,
                    Side = !teams ? "" : player.Team == _session.Local.Team ? "Your team" : "Enemy",
                    SideColor = SideColor(player),
                    Bot = player.Bot,
                    Self = player.Id == _session.PlayerId,
                    Muted = _muted.Contains(player.Id),
                    Friend = social != null && social.IsFriend(player.AccountId),
                    Asked = social != null && social.HasAsked(player.AccountId),
                    Blocked = social != null && social.IsBlocked(player.AccountId),
                });
            }
            rows.Sort((a, b) => string.CompareOrdinal(a.Side, b.Side));
            return rows;
        }

        /// <summary>What a player said in this match, newest last, for a report.</summary>
        List<string> ChatOf(int playerId)
        {
            var lines = new List<string>();
            foreach ((int player, string text) in _chatLog)
            {
                if (player == playerId) lines.Add(text);
            }
            if (lines.Count > 10) lines.RemoveRange(0, lines.Count - 10);
            return lines;
        }

        /// <summary>Who took us out, with what, and what they had left.</summary>
        void ShowWhoGotUs(KillMessage kill)
        {
            if (kill.Killer == KillMessage.NoKiller || kill.Killer >= _session.Players.Length)
            {
                _ui.ShowDeath("OUT OF THE ARENA", "");
                return;
            }
            if (kill.Killer == kill.Victim)
            {
                _ui.ShowDeath("YOUR OWN " + DisplayName(kill.WeaponId).ToUpperInvariant(), "");
                return;
            }
            ClientPlayer killer = _session.Players[kill.Killer];
            string line = DisplayName(kill.WeaponId) + (kill.Headshot ? "   ·   headshot" : "");
            if (killer.Alive) line += "   ·   they had " + Mathf.CeilToInt(Mathf.Max(0f, killer.Health)) + " health left";
            _ui.ShowDeath("ELIMINATED BY  " + killer.Name.ToUpperInvariant(), line);
        }

        /// <summary>A medal for a kill of ours: several in quick succession, a streak, or a headshot.</summary>
        void AwardMedal(KillMessage kill, string victim)
        {
            float now = Time.unscaledTime;
            _multiKill = now - _lastKillTime < 4f ? _multiKill + 1 : 1;
            _lastKillTime = now;
            _streak++;

            string title = _multiKill >= 4 ? "MULTI KILL" : _multiKill == 3 ? "TRIPLE KILL" : _multiKill == 2 ? "DOUBLE KILL"
                : _streak == 15 ? "LEGENDARY" : _streak == 10 ? "UNSTOPPABLE" : _streak == 5 ? "ON A ROLL"
                : kill.Headshot ? "HEADSHOT" : "ELIMINATED";
            string line = victim.ToUpperInvariant() + (_streak >= 3 ? "   ·   " + _streak + " IN A ROW" : "");
            _ui.ShowMedal(title, line);
        }

        /// <summary>Red edges and a heartbeat on low health; grey while out.</summary>
        void UpdateHealthEffects()
        {
            ClientPlayer me = _session.Local;
            if (me == null) return;
            bool inPlay = me.Spawned && _session.LocalAlive;
            float max = Mathf.Max(1f, _session.Settings.maxHealth);
            float fraction = inPlay ? Mathf.Clamp01(me.Health / max) : 1f;
            ScreenEffects.SetHealth(fraction, inPlay || _deadSince < 0f);

            if (!inPlay || fraction >= 0.3f)
            {
                _heartbeatTimer = 0f;
                return;
            }
            _heartbeatTimer -= Time.unscaledDeltaTime;
            if (_heartbeatTimer > 0f) return;
            _heartbeatTimer = Mathf.Lerp(0.55f, 0.95f, fraction / 0.3f);
            _rig?.Sfx.Play(Sfx.Heartbeat, 0.75f);
        }

        /// <summary>Chimes when a zone changes hands, and when the hardpoint moves.</summary>
        void UpdateObjectiveSounds()
        {
            int count = _session.ZoneCount;
            int active = _session.Match.ActiveZone;
            int ourTeam = _session.Local != null ? _session.Local.Team : Teams.None;
            bool fresh = count != _heardZoneCount;
            for (int i = 0; i < count; i++)
            {
                int owner = _session.Zones[i].Owner;
                if (!fresh && owner != _heardZones[i].Owner && owner != Teams.None && ourTeam != Teams.None)
                    UiSounds.Play(owner == ourTeam ? Sfx.ZoneCaptured : Sfx.ZoneLost);
                _heardZones[i] = _session.Zones[i];
            }
            if (!fresh && _session.Settings.mode == GameModeKind.Hardpoint && active != _heardActiveZone && active >= 0 && _heardActiveZone >= 0)
                UiSounds.Play(Sfx.ZoneMoved);
            _heardZoneCount = count;
            _heardActiveZone = active;
        }

        Color SuitOf(int playerId)
        {
            ClientPlayer player = _session.Players[playerId];
            return player.Look != null ? player.Look.Item(Shared.Cosmetics.CosmeticSlot.SuitColor).Color : Color.gray;
        }

        Color TrimOf(int playerId)
        {
            ClientPlayer player = _session.Players[playerId];
            return player.Look != null ? player.Look.Item(Shared.Cosmetics.CosmeticSlot.TrimColor).Color : new Color(0.55f, 0.85f, 1f);
        }

        /// <summary>The other player fired: draw the tracer from their weapon and play the shot, quieter with distance.</summary>
        void OnRemoteShot(ShotMessage shot)
        {
            if (_rig == null) return;
            WeaponBuild build = BuildOf(shot.Shooter, shot.Slot);
            if (build == null) return;

            RemotePlayerView view = _remotes[shot.Shooter];
            bool seen = view != null && view.Visible;
            Vector3 from = seen ? view.Muzzle : shot.Origin;
            if (shot.Kind == ShotKind.Bullet)
            {
                _rig.Fx.PlayTracer(new ShotPresentation
                {
                    Origin = shot.Origin,
                    End = shot.End,
                    Normal = shot.Normal,
                    HitWorld = shot.HitWorld,
                    Build = build,
                }, from);
                if (!shot.HitWorld) BurstOnWhoeverWasHit(shot);
                Whiz(shot);
            }
            if (shot.First)
            {
                _rig.Sfx.PlayShotAt(build, shot.Origin);
                if (seen && shot.Kind == ShotKind.Bullet) _rig.Fx.PlayRemoteMuzzle(from, shot.End - shot.Origin, build);
            }
        }

        /// <summary>Someone else's bullet stopped on a third player: draw the burst there.</summary>
        void BurstOnWhoeverWasHit(in ShotMessage shot)
        {
            for (int i = 0; i < _remotes.Length; i++)
            {
                RemotePlayerView view = _remotes[i];
                if (view == null || i == shot.Shooter || !view.Covers(shot.End)) continue;
                _rig.Fx.PlayBodyHit(shot.End, shot.End - shot.Origin, TrimOf(i), false);
                return;
            }
        }

        /// <summary>A bullet that passed close to our head, without hitting us, cracks past.</summary>
        void Whiz(in ShotMessage shot)
        {
            Vector3 ear = _rig.Camera.transform.position;
            Vector3 path = shot.End - shot.Origin;
            float length = path.magnitude;
            if (length < 0.01f) return;
            Vector3 along = path / length;
            float t = Vector3.Dot(ear - shot.Origin, along);
            if (t < 3f || t > length + 0.5f) return;       // not past us yet, or stopped short of us
            Vector3 closest = shot.Origin + along * t;
            float miss = Vector3.Distance(closest, ear);
            if (miss < 0.35f || miss > 2.2f) return;        // a hit, or nowhere near
            _rig.Sfx.PlayAt(Sfx.Whiz, closest, Mathf.Lerp(1f, 0.35f, miss / 2.2f), 0.12f, 1f, 10f);
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
                if (!ours) _rig.Sfx.PlayAt(Sfx.Throw, spawn.Origin, 0.7f, 0.06f, 1.5f, 30f);
            }
            else
            {
                WeaponBuild build = BuildOf(spawn.Owner, spawn.Source);
                if (build == null) return;
                stats = owner.Loadout.ResolveStats(spawn.Source);
                id = build.Weapon.weaponId;
                body = build.Weapon.view.bodyColor;
                accent = build.Weapon.view.accentColor;
                if (!ours) _rig.Sfx.PlayShotAt(build, spawn.Origin);
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
            if (end.Kind == ProjectileEventKind.Explosion) _rig.Sfx.PlayAt(Sfx.Explosion, end.Point, 1f, 0.06f, 6f);
        }

        WeaponBuild BuildOf(int playerId, int slot)
        {
            Loadout loadout = _session.Players[playerId].Loadout;
            return loadout != null && slot >= 0 && slot < loadout.Weapons.Length ? loadout.Weapons[slot] : null;
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
            // The arena never moves: merge its blocks into a few big meshes, so it costs a
            // handful of draw calls instead of one per block.
            StaticBatchingUtility.Combine(root.gameObject);
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

            // Where that view meets the ground: the camera sways slowly round it.
            Vector3 forward = _menuCamera.transform.forward;
            float down = Mathf.Max(0.1f, -forward.y);
            _menuFocus = _menuCamera.transform.position + forward * (_menuCamera.transform.position.y / down);
            _menuOffset = _menuCamera.transform.position - _menuFocus;
        }

        /// <summary>Behind the menu, the camera drifts gently back and forth over the arena.</summary>
        void DriftMenuCamera()
        {
            if (_menuCamera == null || !_menuCamera.gameObject.activeSelf || _menuOffset == Vector3.zero) return;
            float angle = Mathf.Sin(Time.unscaledTime * 0.06f) * 14f;
            Vector3 position = _menuFocus + Quaternion.Euler(0f, angle, 0f) * _menuOffset;
            _menuCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(_menuFocus - position));
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
