using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Pulsebreak.Client.Audio;
using Pulsebreak.Client.Online;
using Pulsebreak.Client.UI;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Progression;
using Cursor = UnityEngine.Cursor;

namespace Pulsebreak.Client.Duel
{
    /// <summary>One line of the scoreboard.</summary>
    public struct ScoreRow
    {
        public string Name;
        public int Team;
        public int Kills, Deaths, Score;
        public bool Local, Bot, Alive;
    }

    /// <summary>Whose good news a kill feed line is.</summary>
    public enum FeedTone
    {
        Neutral,
        Ours,
        Theirs,
    }

    /// <summary>
    /// Everything on screen that belongs to online play rather than to the weapon in your
    /// hands: the menu (find a match, host a private one, join by address), and during a
    /// match the scores, the clock, the zones, your health, the kill feed, the scoreboard and
    /// the big announcements.
    ///
    /// It sits on top of the ordinary HUD and only shows what it is told. The match's logic
    /// lives in <see cref="DuelBootstrap"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DuelUi : MonoBehaviour
    {
        const string NameKey = "pb.playerName", AddressKey = "pb.lastAddress", ModeKey = "pb.mode";
        const string MapKey = "pb.host.map", SizeKey = "pb.host.size", BotsKey = "pb.host.bots", LimitKey = "pb.host.limit";
        const float KillFeedSeconds = 6f;
        const int KillFeedLines = 5;

        public static readonly Color Accent = new Color32(38, 216, 255, 255);
        static readonly Color Text = new Color32(240, 244, 250, 255);
        static readonly Color Dim = new Color(0.94f, 0.96f, 0.98f, 0.62f);
        static readonly Color Good = new Color32(96, 232, 140, 255);
        public static readonly Color Bad = new Color32(255, 96, 84, 255);
        static readonly Color Panel = new Color(0.04f, 0.06f, 0.08f, 0.72f);

        /// <summary>The order modes are offered in.</summary>
        public static readonly GameModeKind[] Modes =
        {
            GameModeKind.Duel, GameModeKind.TeamDeathmatch, GameModeKind.FreeForAll, GameModeKind.Elimination,
            GameModeKind.Domination, GameModeKind.Hardpoint, GameModeKind.GunGame,
        };

        static readonly string[] BotChoices = { "No bots", "Easy bots", "Normal bots", "Hard bots" };

        VisualElement _menu, _match, _menuCard;
        Label _medal, _medalLine, _deathTitle, _deathLine;
        VisualElement _death;
        float _medalTimer;
        TextField _nameField, _addressField;
        DropdownField _modeField, _mapField, _sizeField, _botsField, _limitField;
        Label _modeHint, _menuStatus, _rankedHint, _levelText, _xpText;
        Toggle _rankedToggle;
        VisualElement _profile, _xpFill;
        Button _progressButton;
        Button _findButton, _hostToggle, _startButton, _joinButton, _rangeButton, _customizeButton, _quitButton, _cancelButton;
        VisualElement _hostOptions;

        Label _scoreLeft, _scoreRight, _nameLeft, _nameRight, _round, _clock, _status;
        Label _bannerTitle, _bannerSub, _notice, _netStats, _healthText;
        VisualElement _healthFill, _zoneStrip, _scoreboard, _scoreboardBody;
        Label _scoreboardTitle;
        readonly List<Zone> _zoneBadges = new List<Zone>();
        readonly List<Label> _feed = new List<Label>();
        readonly List<float> _feedAge = new List<float>();
        VisualElement _feedBox;

        struct Zone
        {
            public VisualElement Box, Fill;
            public Label Letter;
        }

        /// <summary>Argument: the mode chosen in the menu.</summary>
        /// <summary>Arguments: the mode, and whether to play it ranked.</summary>
        public event Action<GameModeKind, bool> FindRequested;
        public event Action ProgressRequested;
        /// <summary>Argument: the private match's rules, as set in the menu.</summary>
        public event Action<MatchSettings> HostRequested;
        /// <summary>Argument: the address typed in.</summary>
        public event Action<string> JoinRequested;
        public event Action RangeRequested;
        public event Action CustomizeRequested;
        public event Action QuitRequested;
        /// <summary>Pressed while searching for a match, or connecting to one.</summary>
        public event Action CancelRequested;

        public string PlayerName => _nameField != null ? _nameField.value.Trim() : "";
        public GameModeKind SelectedMode => Modes[Mathf.Clamp(_modeField != null ? _modeField.index : 0, 0, Modes.Length - 1)];

        AccountPanel _account;
        GameServices _services;

        /// <param name="services">The player's account and loadouts. The menu shows who is signed in and offers the sign-in forms.</param>
        public static DuelUi Create(GameServices services)
        {
            var go = new GameObject("Online UI");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = HudController.LoadPanel();
            document.sortingOrder = 10f;

            var ui = go.AddComponent<DuelUi>();
            ui._services = services;
            ui.Build(document.rootVisualElement);
            return ui;
        }

        /// <summary>One sentence on what a mode is, for the menu.</summary>
        public static string Describe(GameModeKind mode)
        {
            switch (mode)
            {
                case GameModeKind.Duel: return "One on one, no respawns. First to win three rounds.";
                case GameModeKind.TeamDeathmatch: return "Two teams, respawns on. First team to the kill limit.";
                case GameModeKind.FreeForAll: return "Everyone for themselves, respawns on. First to the kill limit.";
                case GameModeKind.Elimination: return "Two teams, no respawns within a round. Wipe the other team out.";
                case GameModeKind.Domination: return "Take and hold three zones. Every zone held scores a point a second.";
                case GameModeKind.Hardpoint: return "One zone at a time, moving every minute. Hold it alone to score.";
                case GameModeKind.GunGame: return "Every kill moves you to the next weapon. A knife kill with the last one wins.";
                default: return "";
            }
        }

        // ------------------------------------------------------------------ construction

        void Build(VisualElement root)
        {
            if (root == null)
            {
                Debug.LogError("Pulsebreak: the online UI could not be created (no UI root).");
                return;
            }

            // The look comes from the HUD's stylesheet. The layout that matters is also set
            // here in code, so a missing stylesheet costs polish, not a usable menu.
            var styles = Resources.Load<StyleSheet>("UI/HudStyles");
            if (styles != null) root.styleSheets.Add(styles);
            else Debug.LogWarning("Pulsebreak: Resources/UI/HudStyles.uss did not load; the online menu will look plain.");
            root.AddToClassList("hud");
            root.pickingMode = PickingMode.Ignore;
            Fill(root);
            root.style.color = Text;
            root.style.fontSize = 16;

            BuildMatch(root);
            BuildMenu(root);
            UiSounds.Attach(root);
            ShowMatch(false);
            ShowMenu("");
        }

        void BuildMenu(VisualElement root)
        {
            _menu = new VisualElement();
            _menu.AddToClassList("pause");
            Fill(_menu);
            _menu.style.alignItems = Align.Center;
            _menu.style.justifyContent = Justify.Center;
            _menu.style.backgroundColor = new Color(0.024f, 0.035f, 0.055f, 0.78f);
            root.Add(_menu);

            var card = new VisualElement();
            card.AddToClassList("pause-card");
            card.style.width = 560;
            card.style.paddingLeft = card.style.paddingRight = 32;
            card.style.paddingTop = card.style.paddingBottom = 26;
            card.style.backgroundColor = new Color(0.07f, 0.09f, 0.125f, 1f);
            _menu.Add(card);
            _menuCard = card;

            card.Add(Text_("PULSEBREAK", 30, Text, bold: true, spacing: 6, className: "pause-title"));
            Label subtitle = Text_("PLAY ONLINE", 13, Accent, bold: true, spacing: 3);
            subtitle.style.marginTop = -12;
            subtitle.style.marginBottom = 14;
            card.Add(subtitle);

            if (_services != null && GameServices.AccountsEnabled)
            {
                var accountHost = new VisualElement();
                card.Add(accountHost);
                _account = new AccountPanel(_services, card, accountHost, _menu);
                // Signed in, a player goes by their account's name, so there is nothing to type.
                _account.Changed += () =>
                {
                    if (_nameField != null) _nameField.style.display = _services.Account.SignedIn ? DisplayStyle.None : DisplayStyle.Flex;
                };
            }

            BuildProfile(card);

            _nameField = new TextField("Name") { value = PlayerPrefs.GetString(NameKey, "Player"), maxLength = 24 };
            _nameField.AddToClassList("loadout-name");
            _nameField.style.fontSize = 16;
            card.Add(_nameField);
            _account?.Refresh();

            var modeNames = new List<string>();
            foreach (GameModeKind mode in Modes) modeNames.Add(MatchSettings.Title(mode));
            _modeField = Dropdown(card, "Mode", modeNames, Mathf.Clamp(PlayerPrefs.GetInt(ModeKey, 0), 0, Modes.Length - 1));
            _modeHint = Text_("", 12, Dim);
            _modeHint.style.whiteSpace = WhiteSpace.Normal;
            _modeHint.style.marginBottom = 10;
            card.Add(_modeHint);
            _modeField.RegisterValueChangedCallback(_ => OnModeChanged());

            // Ranked, for the modes that have it.
            var rankedRow = new VisualElement();
            rankedRow.style.flexDirection = FlexDirection.Row;
            rankedRow.style.alignItems = Align.Center;
            rankedRow.style.marginBottom = 8;
            _rankedToggle = new Toggle("Ranked") { value = false };
            _rankedToggle.style.fontSize = 15;
            _rankedToggle.RegisterValueChangedCallback(_ => RefreshRanked());
            rankedRow.Add(_rankedToggle);
            _rankedHint = Text_("", 12, Dim);
            _rankedHint.style.marginLeft = 12;
            _rankedHint.style.whiteSpace = WhiteSpace.Normal;
            _rankedHint.style.flexShrink = 1;
            rankedRow.Add(_rankedHint);
            card.Add(rankedRow);

            _findButton = MenuButton("Find match", true, () =>
            {
                Remember();
                FindRequested?.Invoke(SelectedMode, RankedChosen);
            });
            card.Add(_findButton);

            // A private match: the host picks every rule.
            _hostToggle = MenuButton("Host a private match  ▾", false, () => SetHostOptions(_hostOptions.style.display == DisplayStyle.None));
            _hostToggle.style.marginTop = 10;
            _hostToggle.style.marginLeft = 0;
            card.Add(_hostToggle);

            _hostOptions = new VisualElement();
            _hostOptions.style.marginTop = 4;
            _hostOptions.style.paddingLeft = 12;
            _hostOptions.style.borderLeftWidth = 2;
            _hostOptions.style.borderLeftColor = new Color(1f, 1f, 1f, 0.12f);
            card.Add(_hostOptions);
            _mapField = Dropdown(_hostOptions, "Map", new List<string>(), 0);
            _sizeField = Dropdown(_hostOptions, "Players", new List<string>(), 0);
            _limitField = Dropdown(_hostOptions, "Win at", new List<string>(), 0);
            _botsField = Dropdown(_hostOptions, "Empty places", new List<string>(BotChoices), Mathf.Clamp(PlayerPrefs.GetInt(BotsKey, 2), 0, 3));
            Label hostHint = Text_("Bots fill any place nobody has taken, and step aside when someone joins. Others join with your address.", 12, Dim);
            hostHint.style.whiteSpace = WhiteSpace.Normal;
            _hostOptions.Add(hostHint);
            _startButton = MenuButton("Start private match", true, () =>
            {
                Remember();
                HostRequested?.Invoke(BuildSettings());
            });
            _startButton.style.marginTop = 8;
            _hostOptions.Add(_startButton);
            SetHostOptions(false);

            var joinRow = new VisualElement();
            joinRow.style.flexDirection = FlexDirection.Row;
            joinRow.style.alignItems = Align.Center;
            joinRow.style.marginTop = 14;
            _addressField = new TextField { value = PlayerPrefs.GetString(AddressKey, "127.0.0.1"), maxLength = 64 };
            _addressField.AddToClassList("loadout-name");
            _addressField.style.fontSize = 16;
            _addressField.style.flexGrow = 1;
            joinRow.Add(_addressField);
            _joinButton = MenuButton("Join by address", false, RequestJoin);
            _joinButton.style.marginLeft = 10;
            joinRow.Add(_joinButton);
            card.Add(joinRow);
            // Enter in the address box joins. Which of these two events carries the key depends on the Unity version.
            _addressField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) RequestJoin();
            }, TrickleDown.TrickleDown);
            _addressField.RegisterCallback<NavigationSubmitEvent>(e => RequestJoin(), TrickleDown.TrickleDown);

            _menuStatus = Text_("", 14, Bad);
            _menuStatus.style.whiteSpace = WhiteSpace.Normal;
            _menuStatus.style.marginTop = 14;
            _menuStatus.style.minHeight = 20;
            card.Add(_menuStatus);

            var bottom = new VisualElement();
            bottom.AddToClassList("pause-buttons");
            bottom.style.flexDirection = FlexDirection.Row;
            bottom.style.justifyContent = Justify.FlexEnd;
            bottom.style.marginTop = 18;
            _cancelButton = MenuButton("Cancel", false, () => CancelRequested?.Invoke());
            _customizeButton = MenuButton("Customize", false, () => CustomizeRequested?.Invoke());
            Button settings = MenuButton("Settings", false, () => SettingsScreen.Open());
            _rangeButton = MenuButton("Firing range", false, () => RangeRequested?.Invoke());
            _quitButton = MenuButton("Quit", false, () => QuitRequested?.Invoke());
            foreach (Button button in new[] { _cancelButton, _customizeButton, settings, _rangeButton, _quitButton })
            {
                // Five to a row: share it out evenly.
                button.style.minWidth = 0;
                button.style.flexGrow = 1;
                button.style.marginLeft = 6;
                bottom.Add(button);
            }
            _cancelButton.style.marginLeft = 0;
            card.Add(bottom);

            OnModeChanged();
        }

        static DropdownField Dropdown(VisualElement parent, string label, List<string> choices, int index)
        {
            var field = new DropdownField(label, choices, choices.Count > 0 ? Mathf.Clamp(index, 0, choices.Count - 1) : -1);
            field.style.fontSize = 15;
            parent.Add(field);
            return field;
        }

        void SetHostOptions(bool open)
        {
            _hostOptions.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            _hostToggle.text = open ? "Host a private match  ▴" : "Host a private match  ▾";
        }

        // ------------------------------------------------------------------ the private match's rules

        GameModeKind _optionsFor = (GameModeKind)255;
        readonly List<string> _mapIds = new List<string>();
        readonly List<int> _sizes = new List<int>();
        readonly List<int> _limits = new List<int>();

        /// <summary>Refills the host options to suit the chosen mode.</summary>
        void OnModeChanged()
        {
            GameModeKind mode = SelectedMode;
            _modeHint.text = Describe(mode);
            RefreshRanked();
            if (mode == _optionsFor) return;
            _optionsFor = mode;
            MatchSettings defaults = MatchSettings.For(mode);

            _mapIds.Clear();
            var mapNames = new List<string>();
            foreach (string id in ArenaLayout.Ids)
            {
                var probe = defaults.Clone();
                probe.arenaId = id;
                probe.teamSize = 1;
                probe.maxPlayers = 2;
                if (!ArenaLayout.Find(id).Supports(probe)) continue;
                _mapIds.Add(id);
                mapNames.Add(ArenaLayout.Title(id));
            }
            SetChoices(_mapField, mapNames, Mathf.Max(0, _mapIds.IndexOf(PlayerPrefs.GetString(MapKey + mode, defaults.arenaId))));

            _sizes.Clear();
            var sizeNames = new List<string>();
            if (mode == GameModeKind.Duel)
            {
                _sizes.Add(1);
                sizeNames.Add("1 v 1");
            }
            else if (defaults.TeamBased)
            {
                for (int n = 1; n <= 5; n++)
                {
                    _sizes.Add(n);
                    sizeNames.Add(n + " v " + n);
                }
            }
            else
            {
                for (int n = 2; n <= 12; n += 2)
                {
                    _sizes.Add(n);
                    sizeNames.Add("Up to " + n);
                }
            }
            int size = PlayerPrefs.GetInt(SizeKey + mode, defaults.TeamBased ? defaults.teamSize : defaults.maxPlayers);
            SetChoices(_sizeField, sizeNames, Mathf.Max(0, _sizes.IndexOf(size)));
            _sizeField.label = defaults.TeamBased ? "Teams" : "Players";

            _limits.Clear();
            var limitNames = new List<string>();
            if (mode == GameModeKind.GunGame)
            {
                _limits.Add(GunGameLadder.Length);
                limitNames.Add("The last weapon (" + GunGameLadder.Length + " kills)");
            }
            else if (defaults.RoundBased)
            {
                foreach (int rounds in new[] { 2, 3, 4, 5, 6 })
                {
                    _limits.Add(rounds);
                    limitNames.Add(rounds + " rounds (best of " + (rounds * 2 - 1) + ")");
                }
            }
            else
            {
                int[] options = mode == GameModeKind.Domination ? new[] { 100, 150, 200, 300 }
                    : mode == GameModeKind.Hardpoint ? new[] { 100, 150, 200, 250 }
                    : mode == GameModeKind.FreeForAll ? new[] { 10, 15, 20, 25, 30 }
                    : new[] { 20, 30, 50, 75, 100 };
                string unit = defaults.UsesZones ? " points" : " kills";
                foreach (int limit in options)
                {
                    _limits.Add(limit);
                    limitNames.Add(limit + unit);
                }
            }
            int wanted = PlayerPrefs.GetInt(LimitKey + mode, defaults.RoundBased ? defaults.roundsToWin : defaults.scoreLimit);
            SetChoices(_limitField, limitNames, Mathf.Max(0, _limits.IndexOf(wanted)));
        }

        static void SetChoices(DropdownField field, List<string> choices, int index)
        {
            field.choices = choices;
            field.index = choices.Count > 0 ? Mathf.Clamp(index, 0, choices.Count - 1) : -1;
            field.SetEnabled(choices.Count > 1);
        }

        MatchSettings BuildSettings()
        {
            GameModeKind mode = SelectedMode;
            MatchSettings settings = MatchSettings.For(mode);
            if (_mapField.index >= 0 && _mapField.index < _mapIds.Count) settings.arenaId = _mapIds[_mapField.index];
            if (_sizeField.index >= 0 && _sizeField.index < _sizes.Count)
            {
                if (settings.TeamBased) settings.teamSize = _sizes[_sizeField.index];
                else settings.maxPlayers = _sizes[_sizeField.index];
            }
            if (_limitField.index >= 0 && _limitField.index < _limits.Count)
            {
                if (settings.RoundBased) settings.roundsToWin = _limits[_limitField.index];
                else settings.scoreLimit = _limits[_limitField.index];
            }
            int bots = Mathf.Clamp(_botsField.index, 0, 3);
            settings.fillWithBots = bots > 0;
            settings.botSkill = Mathf.Max(0, bots - 1);
            return settings;
        }

        void Remember()
        {
            GameModeKind mode = SelectedMode;
            PlayerPrefs.SetInt(ModeKey, Array.IndexOf(Modes, mode));
            PlayerPrefs.SetInt(BotsKey, _botsField.index);
            if (_mapField.index >= 0 && _mapField.index < _mapIds.Count) PlayerPrefs.SetString(MapKey + mode, _mapIds[_mapField.index]);
            if (_sizeField.index >= 0 && _sizeField.index < _sizes.Count) PlayerPrefs.SetInt(SizeKey + mode, _sizes[_sizeField.index]);
            if (_limitField.index >= 0 && _limitField.index < _limits.Count) PlayerPrefs.SetInt(LimitKey + mode, _limits[_limitField.index]);
            PlayerPrefs.SetString(NameKey, PlayerName);
        }

        void RequestJoin()
        {
            string address = _addressField.value.Trim();
            if (address.Length == 0) return;
            PlayerPrefs.SetString(AddressKey, address);
            Remember();
            JoinRequested?.Invoke(address);
        }

        // ------------------------------------------------------------------ the match overlay

        void BuildMatch(VisualElement root)
        {
            _match = new VisualElement();
            _match.AddToClassList("layer");
            _match.pickingMode = PickingMode.Ignore;
            Fill(_match);
            root.Add(_match);

            // Scores, round and clock across the top; the zones under them.
            var top = new VisualElement { pickingMode = PickingMode.Ignore };
            top.style.position = Position.Absolute;
            top.style.top = 18;
            top.style.left = 0;
            top.style.right = 0;
            top.style.alignItems = Align.Center;
            _match.Add(top);

            var scores = Row(top);
            scores.style.justifyContent = Justify.Center;
            scores.style.alignItems = Align.Center;

            _nameLeft = Text_("", 18, Text, bold: true);
            _nameLeft.style.width = 260;
            _nameLeft.style.unityTextAlign = TextAnchor.MiddleRight;
            _scoreLeft = Text_("0", 40, Accent, bold: true);
            _scoreLeft.style.minWidth = 84;
            _scoreLeft.style.unityTextAlign = TextAnchor.MiddleCenter;

            var middle = new VisualElement();
            middle.style.width = 150;
            middle.style.alignItems = Align.Center;
            middle.pickingMode = PickingMode.Ignore;
            _clock = Text_("", 24, Text, bold: true);
            _round = Text_("", 12, Dim, bold: true, spacing: 3);
            middle.Add(_clock);
            middle.Add(_round);

            _scoreRight = Text_("0", 40, Bad, bold: true);
            _scoreRight.style.minWidth = 84;
            _scoreRight.style.unityTextAlign = TextAnchor.MiddleCenter;
            _nameRight = Text_("", 18, Text, bold: true);
            _nameRight.style.width = 260;

            scores.Add(_nameLeft);
            scores.Add(_scoreLeft);
            scores.Add(middle);
            scores.Add(_scoreRight);
            scores.Add(_nameRight);

            _zoneStrip = Row(top);
            _zoneStrip.style.marginTop = 6;
            _zoneStrip.style.justifyContent = Justify.Center;

            // Announcements, a quarter of the way down.
            var banner = new VisualElement();
            banner.pickingMode = PickingMode.Ignore;
            banner.style.position = Position.Absolute;
            banner.style.top = Length.Percent(24);
            banner.style.left = 0;
            banner.style.right = 0;
            banner.style.alignItems = Align.Center;
            _bannerTitle = Text_("", 64, Text, bold: true, spacing: 8);
            _bannerTitle.AddToClassList("banner-pop");
            _bannerSub = Text_("", 20, Dim);
            _notice = Text_("", 15, Accent);
            _notice.style.marginTop = 10;
            banner.Add(_bannerTitle);
            banner.Add(_bannerSub);
            banner.Add(_notice);
            _match.Add(banner);

            // Medals, under the crosshair.
            var medal = new VisualElement { pickingMode = PickingMode.Ignore };
            medal.AddToClassList("medal");
            medal.AddToClassList("medal--hidden");
            _medal = Text_("", 26, new Color32(255, 214, 92, 255), bold: true, spacing: 4);
            _medalLine = Text_("", 15, Text, spacing: 2);
            _medal.style.unityTextAlign = TextAnchor.MiddleCenter;
            _medalLine.style.unityTextAlign = TextAnchor.MiddleCenter;
            medal.Add(_medal);
            medal.Add(_medalLine);
            _match.Add(medal);

            // Who took us out, low in the middle while we wait to come back.
            _death = new VisualElement { pickingMode = PickingMode.Ignore };
            _death.style.position = Position.Absolute;
            _death.style.bottom = 170;
            _death.style.left = 0;
            _death.style.right = 0;
            _death.style.alignItems = Align.Center;
            _deathTitle = Text_("", 24, Bad, bold: true, spacing: 3);
            _deathLine = Text_("", 15, Text);
            _death.Add(_deathTitle);
            _death.Add(_deathLine);
            _death.style.display = DisplayStyle.None;
            _match.Add(_death);

            // A line for respawns and Gun Game, above the weapons along the bottom centre.
            _status = Text_("", 15, Accent, bold: true, spacing: 1);
            _status.style.position = Position.Absolute;
            _status.style.bottom = 118;
            _status.style.left = 0;
            _status.style.right = 0;
            _status.style.unityTextAlign = TextAnchor.MiddleCenter;
            _match.Add(_status);

            // Health, bottom left: the weapons have the bottom centre.
            var health = new VisualElement();
            health.pickingMode = PickingMode.Ignore;
            health.style.position = Position.Absolute;
            health.style.bottom = 28;
            health.style.left = 28;
            health.style.alignItems = Align.FlexStart;
            health.style.paddingLeft = health.style.paddingRight = 16;
            health.style.paddingTop = 10;
            health.style.paddingBottom = 14;
            health.style.backgroundColor = new Color(0.04f, 0.055f, 0.08f, 0.55f);
            health.style.borderLeftWidth = 3;
            health.style.borderLeftColor = Good;
            Round(health, 6);
            Label healthTitle = Text_("HEALTH", 12, Dim, bold: true, spacing: 3);
            _healthText = Text_("100", 30, Text, bold: true);
            var bar = new VisualElement();
            bar.pickingMode = PickingMode.Ignore;
            bar.style.width = 240;
            bar.style.height = 8;
            bar.style.marginTop = 2;
            bar.style.backgroundColor = new Color(0.04f, 0.06f, 0.08f, 0.7f);
            Round(bar, 4);
            _healthFill = new VisualElement();
            _healthFill.pickingMode = PickingMode.Ignore;
            _healthFill.style.height = Length.Percent(100);
            _healthFill.style.width = Length.Percent(100);
            _healthFill.style.backgroundColor = Good;
            Round(_healthFill, 4);
            bar.Add(_healthFill);
            health.Add(healthTitle);
            health.Add(_healthText);
            health.Add(bar);
            _match.Add(health);

            // Kill feed, top right.
            _feedBox = new VisualElement();
            _feedBox.pickingMode = PickingMode.Ignore;
            _feedBox.style.position = Position.Absolute;
            _feedBox.style.top = 28;
            _feedBox.style.right = 28;
            _feedBox.style.alignItems = Align.FlexEnd;
            _match.Add(_feedBox);

            // Connection readout, top left.
            _netStats = Text_("", 13, Dim);
            _netStats.style.position = Position.Absolute;
            _netStats.style.top = 28;
            _netStats.style.left = 28;
            _netStats.style.whiteSpace = WhiteSpace.Normal;
            _match.Add(_netStats);

            BuildScoreboard();
        }

        void BuildScoreboard()
        {
            _scoreboard = new VisualElement { pickingMode = PickingMode.Ignore };
            Fill(_scoreboard);
            _scoreboard.style.alignItems = Align.Center;
            _scoreboard.style.justifyContent = Justify.Center;
            _scoreboard.style.display = DisplayStyle.None;
            _match.Add(_scoreboard);

            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.style.minWidth = 620;
            card.style.paddingLeft = card.style.paddingRight = 22;
            card.style.paddingTop = card.style.paddingBottom = 18;
            card.style.backgroundColor = new Color(0.05f, 0.07f, 0.1f, 0.92f);
            card.style.borderTopWidth = 3;
            card.style.borderTopColor = Accent;
            Round(card, 8);
            _scoreboard.Add(card);

            _scoreboardTitle = Text_("", 15, Dim, bold: true, spacing: 3);
            _scoreboardTitle.style.marginBottom = 10;
            card.Add(_scoreboardTitle);
            _scoreboardBody = new VisualElement { pickingMode = PickingMode.Ignore };
            card.Add(_scoreboardBody);
        }

        // ------------------------------------------------------------------ profile and ranked

        /// <summary>Ranked is chosen, and allowed: the mode has it, and the player is signed in with a verified email.</summary>
        bool RankedChosen => _rankedToggle != null && _rankedToggle.value && RankedAllowed(out _);

        bool RankedAllowed(out string why)
        {
            why = "";
            if (!Playlists.HasRanked(SelectedMode))
            {
                why = "Ranked is Duel and Elimination.";
                return false;
            }
            if (_services == null || !_services.Account.SignedIn)
            {
                why = "Sign in to play ranked.";
                return false;
            }
            if (!_services.Account.Account.emailVerified)
            {
                why = "Verify your email address to play ranked.";
                return false;
            }
            return true;
        }

        void RefreshRanked()
        {
            if (_rankedToggle == null) return;
            bool allowed = RankedAllowed(out string why);
            _rankedToggle.SetEnabled(allowed);
            if (!allowed)
            {
                _rankedToggle.SetValueWithoutNotify(false);
                _rankedHint.text = why;
                return;
            }
            Playlist playlist = Playlists.For(SelectedMode, true);
            RankData rank = ProgressionCache.Rank(playlist.Id);
            _rankedHint.text = _rankedToggle.value
                ? playlist.Title + ": " + rank.name + (rank.placementLeft > 0 ? "" : "  (" + rank.rating + ")") + ". Full matches of people, matched by rating."
                : "Your " + playlist.Title + " rank: " + rank.name;
        }

        /// <summary>Level and XP across the top of the menu, for a signed-in player; an invitation to sign in otherwise.</summary>
        void BuildProfile(VisualElement card)
        {
            _profile = new VisualElement();
            _profile.style.flexDirection = FlexDirection.Row;
            _profile.style.alignItems = Align.Center;
            _profile.style.marginBottom = 12;
            _profile.style.paddingLeft = _profile.style.paddingRight = 12;
            _profile.style.paddingTop = _profile.style.paddingBottom = 8;
            _profile.style.backgroundColor = new Color(0.1f, 0.13f, 0.18f, 1f);
            Round(_profile, 6);
            card.Add(_profile);

            _levelText = Text_("", 18, Accent, bold: true, spacing: 1);
            _levelText.style.minWidth = 92;
            _profile.Add(_levelText);

            var bars = new VisualElement();
            bars.style.flexGrow = 1;
            bars.style.marginRight = 12;
            _profile.Add(bars);
            var track = new VisualElement();
            track.style.height = 8;
            track.style.backgroundColor = new Color(0.04f, 0.06f, 0.08f, 1f);
            Round(track, 4);
            _xpFill = new VisualElement();
            _xpFill.style.height = Length.Percent(100);
            _xpFill.style.backgroundColor = Accent;
            Round(_xpFill, 4);
            track.Add(_xpFill);
            bars.Add(track);
            _xpText = Text_("", 12, Dim);
            _xpText.style.marginTop = 3;
            _xpText.style.whiteSpace = WhiteSpace.Normal;
            bars.Add(_xpText);

            _progressButton = MenuButton("Progress", false, () => ProgressRequested?.Invoke());
            _progressButton.style.marginLeft = 0;
            _progressButton.style.minWidth = 110;
            _profile.Add(_progressButton);

            ProgressionCache.Changed += RefreshProfile;
            if (_services != null) _services.Account.Changed += RefreshProfile;
            RefreshProfile();
        }

        void RefreshProfile()
        {
            if (_profile == null) return;
            ProgressionData progression = ProgressionCache.Current;
            bool signedIn = _services != null && _services.Account.SignedIn;
            _xpFill.parent.style.display = signedIn ? DisplayStyle.Flex : DisplayStyle.None;
            _progressButton.style.display = signedIn ? DisplayStyle.Flex : DisplayStyle.None;
            if (!signedIn)
            {
                _levelText.text = "GUEST";
                _xpText.text = "Sign in to earn XP, unlock weapons, take on challenges and play ranked.";
            }
            else if (progression == null)
            {
                _levelText.text = "LEVEL -";
                _xpText.text = "Fetching your progress...";
                _xpFill.style.width = Length.Percent(0);
            }
            else
            {
                LevelData level = progression.level;
                bool max = level.level >= level.maxLevel;
                _levelText.text = "LEVEL " + level.level;
                float fraction = max || level.xpForNextLevel <= 0 ? 1f : Mathf.Clamp01(level.xpIntoLevel / (float)level.xpForNextLevel);
                _xpFill.style.width = Length.Percent(fraction * 100f);
                int open = 0;
                foreach (ChallengeData challenge in progression.challenges) if (!challenge.completed) open++;
                _xpText.text = (max ? "Top level" : level.xpIntoLevel + " / " + level.xpForNextLevel + " XP to level " + (level.level + 1)) +
                               "   ·   " + open + " challenge(s) to do";
            }
            RefreshRanked();
        }

        // ------------------------------------------------------------------ menu

        /// <summary>Shows the menu, ready for input. <paramref name="status"/> is a line of bad news, or empty.</summary>
        public void ShowMenu(string status)
        {
            if (_menu == null) return;
            if (_menu.style.display == DisplayStyle.None) UiMotion.Enter(_menuCard);
            _menu.style.display = DisplayStyle.Flex;
            _menuStatus.text = status ?? "";
            _menuStatus.style.color = Bad;
            SetBusy(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>Shows the menu with a line of good news, such as how the last match went.</summary>
        public void ShowMenuResult(string text)
        {
            ShowMenu("");
            _menuStatus.text = text ?? "";
            _menuStatus.style.color = Good;
        }

        /// <summary>Shows the menu with its buttons off while a match is being found or joined.</summary>
        public void ShowConnecting(string status)
        {
            if (_menu == null) return;
            _menu.style.display = DisplayStyle.Flex;
            _menuStatus.text = status ?? "";
            _menuStatus.style.color = Text;
            SetBusy(true);
        }

        public void HideMenu()
        {
            if (_menu == null) return;
            _menu.style.display = DisplayStyle.None;
            PlayerPrefs.SetString(NameKey, PlayerName);
        }

        public bool MenuVisible => _menu != null && _menu.resolvedStyle.display != DisplayStyle.None;

        void SetBusy(bool busy)
        {
            _findButton.SetEnabled(!busy);
            _hostToggle.SetEnabled(!busy);
            _startButton.SetEnabled(!busy);
            _joinButton.SetEnabled(!busy);
            _rangeButton.SetEnabled(!busy);
            _customizeButton.SetEnabled(!busy);
            _nameField.SetEnabled(!busy);
            _addressField.SetEnabled(!busy);
            _modeField.SetEnabled(!busy);
            _rankedToggle.SetEnabled(!busy && RankedAllowed(out _));
            _progressButton.SetEnabled(!busy);
            _cancelButton.style.display = busy ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ------------------------------------------------------------------ match

        /// <summary>Takes the match overlay off screen without forgetting what it shows, while a menu is up.</summary>
        public void SetMatchHidden(bool hidden)
        {
            if (_match != null) _match.style.visibility = hidden ? Visibility.Hidden : Visibility.Visible;
        }

        public void ShowMatch(bool visible)
        {
            if (_match == null) return;
            _match.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible) return;
            ShowDeath(null, null);
            foreach (Label line in _feed) line.RemoveFromHierarchy();
            _feed.Clear();
            _feedAge.Clear();
            ShowScoreboard(false);
            SetZones(null, 0, -1, Teams.None, false);
        }

        /// <summary>Our side on the left, the other on the right.</summary>
        public void SetScore(string ourName, string ourScore, string theirName, string theirScore, string round, string clock)
        {
            if (_match == null) return;
            _nameLeft.text = ourName;
            _scoreLeft.text = ourScore;
            _nameRight.text = theirName;
            _scoreRight.text = theirScore;
            _round.text = round;
            _clock.text = clock;
        }

        /// <summary>
        /// The zones under the score. Domination shows every zone; Hardpoint only the one in
        /// play. A zone's colour is its holder's; the fill shows a capture under way, or for a
        /// hardpoint the time it has left.
        /// </summary>
        public void SetZones(ZoneState[] zones, int count, int activeZone, int ourTeam, bool hardpoint, string[] names = null)
        {
            if (_zoneStrip == null) return;
            int shown = hardpoint ? (activeZone >= 0 && activeZone < count ? 1 : 0) : count;
            while (_zoneBadges.Count < shown) _zoneBadges.Add(MakeZoneBadge());
            for (int i = 0; i < _zoneBadges.Count; i++)
                _zoneBadges[i].Box.style.display = i < shown ? DisplayStyle.Flex : DisplayStyle.None;

            bool blink = Mathf.Repeat(Time.unscaledTime, 0.5f) < 0.25f;
            for (int i = 0; i < shown; i++)
            {
                int index = hardpoint ? activeZone : i;
                ZoneState zone = zones[index];
                Zone badge = _zoneBadges[i];
                string name = names != null && index < names.Length ? names[index] : ((char)('A' + index)).ToString();
                badge.Letter.text = hardpoint ? name.ToUpperInvariant() : name;
                badge.Box.style.minWidth = hardpoint ? 150 : 38;

                badge.Box.style.backgroundColor = TeamColor(zone.Owner, ourTeam, 0.85f, new Color(0.1f, 0.12f, 0.16f, 0.8f));
                Color edge = zone.Contested && blink ? Color.white : new Color(1f, 1f, 1f, 0.25f);
                badge.Box.style.borderTopColor = badge.Box.style.borderBottomColor =
                    badge.Box.style.borderLeftColor = badge.Box.style.borderRightColor = edge;

                if (hardpoint)
                {
                    badge.Fill.style.height = 3;
                    badge.Fill.style.width = Length.Percent(Mathf.Clamp01(zone.Progress) * 100f);
                    badge.Fill.style.backgroundColor = Color.white;
                }
                else
                {
                    badge.Fill.style.width = Length.Percent(100);
                    badge.Fill.style.height = Length.Percent(Mathf.Clamp01(zone.Progress) * 100f);
                    badge.Fill.style.backgroundColor = TeamColor(zone.Capturer, ourTeam, 0.7f, Color.clear);
                }
            }
        }

        Zone MakeZoneBadge()
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.style.height = 38;
            box.style.marginLeft = box.style.marginRight = 4;
            box.style.justifyContent = Justify.Center;
            box.style.alignItems = Align.Center;
            box.style.borderTopWidth = box.style.borderBottomWidth = box.style.borderLeftWidth = box.style.borderRightWidth = 2;
            box.style.overflow = Overflow.Hidden;
            Round(box, 6);

            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.position = Position.Absolute;
            fill.style.left = 0;
            fill.style.bottom = 0;
            box.Add(fill);

            Label letter = Text_("", 18, Text, bold: true);
            letter.style.paddingLeft = letter.style.paddingRight = 8;
            box.Add(letter);
            _zoneStrip.Add(box);
            return new Zone { Box = box, Fill = fill, Letter = letter };
        }

        /// <summary>Our colour, theirs, or a fallback for nobody.</summary>
        public static Color TeamColor(int team, int ourTeam, float alpha, Color nobody)
        {
            if (team == Teams.None || team < 0) return nobody;
            Color color = team == ourTeam ? Accent : Bad;
            color.a = alpha;
            return color;
        }

        public void SetHealth(float current, float max)
        {
            if (_match == null) return;
            float fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            _healthText.text = Mathf.CeilToInt(Mathf.Max(current, 0f)).ToString();
            _healthFill.style.width = Length.Percent(fraction * 100f);
            _healthFill.style.backgroundColor = Color.Lerp(Bad, Good, Mathf.InverseLerp(0.2f, 0.6f, fraction));
        }

        /// <summary>The line over the health bar: respawn timer, Gun Game progress. Empty clears it.</summary>
        public void SetStatus(string text)
        {
            if (_match != null) _status.text = text ?? "";
        }

        /// <summary>The big announcement in the middle of the screen. Empty strings clear it.</summary>
        public void SetBanner(string title, string subtitle, bool good = true)
        {
            if (_match == null) return;
            if (!string.IsNullOrEmpty(title) && title != _bannerTitle.text) UiMotion.Pop(_bannerTitle, "banner-pop--start");
            _bannerTitle.text = title ?? "";
            _bannerTitle.style.color = good ? Text : Bad;
            _bannerSub.text = subtitle ?? "";
        }

        /// <summary>A small line under the banner, for things worth knowing but not shouting.</summary>
        public void SetNotice(string text)
        {
            if (_match != null) _notice.text = text ?? "";
        }

        public void SetNetStats(string text)
        {
            if (_match != null) _netStats.text = text ?? "";
        }

        public void AddKill(string text, FeedTone tone)
        {
            if (_match == null) return;
            Label line = Text_(text, 15, tone == FeedTone.Ours ? Good : tone == FeedTone.Theirs ? Bad : Text, bold: true);
            line.style.paddingLeft = 10;
            line.style.paddingRight = 10;
            line.style.paddingTop = 4;
            line.style.paddingBottom = 4;
            line.style.marginBottom = 4;
            line.style.backgroundColor = Panel;
            Round(line, 4);
            _feedBox.Add(line);
            UiMotion.Enter(line);
            _feed.Add(line);
            _feedAge.Add(0f);

            if (_feed.Count <= KillFeedLines) return;
            _feed[0].RemoveFromHierarchy();
            _feed.RemoveAt(0);
            _feedAge.RemoveAt(0);
        }

        // ------------------------------------------------------------------ scoreboard

        public bool ScoreboardVisible => _scoreboard != null && _scoreboard.style.display == DisplayStyle.Flex;

        public void ShowScoreboard(bool visible)
        {
            if (_scoreboard != null) _scoreboard.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Fills the scoreboard: by team (ours first) in team modes, one list otherwise, each
        /// sorted best first. <paramref name="scoreLabel"/> heads the last column.
        /// </summary>
        public void SetScoreboard(string title, List<ScoreRow> rows, bool teams, int ourTeam, string scoreLabel, int score0 = 0, int score1 = 0)
        {
            if (_scoreboardBody == null) return;
            _scoreboardTitle.text = title;
            _scoreboardBody.Clear();
            rows.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : b.Kills != a.Kills ? b.Kills.CompareTo(a.Kills) : a.Deaths.CompareTo(b.Deaths));

            if (!teams)
            {
                AddHeader("PLAYERS", Text, scoreLabel);
                for (int i = 0; i < rows.Count; i++) AddRow(rows[i], (i + 1) + ".", rows[i].Local ? Accent : Text);
                return;
            }

            for (int pass = 0; pass < 2; pass++)
            {
                int team = pass == 0 ? ourTeam : 1 - ourTeam;
                if (team < 0 || team > 1) team = pass;
                Color color = team == ourTeam ? Accent : Bad;
                AddHeader((team == ourTeam ? "YOUR TEAM" : "ENEMY TEAM") + "   " + (team == 0 ? score0 : score1), color, scoreLabel);
                foreach (ScoreRow row in rows)
                {
                    if (row.Team == team) AddRow(row, "", row.Local ? Accent : Text);
                }
                if (pass == 0) _scoreboardBody.Add(new VisualElement { style = { height = 12 } });
            }
        }

        void AddHeader(string title, Color color, string scoreLabel)
        {
            VisualElement line = Row(_scoreboardBody);
            line.style.borderBottomWidth = 1;
            line.style.borderBottomColor = new Color(1f, 1f, 1f, 0.15f);
            line.style.paddingBottom = 3;
            line.style.marginBottom = 3;
            Cell(line, title, 300, color, bold: true, left: true);
            Cell(line, "K", 60, Dim, bold: true);
            Cell(line, "D", 60, Dim, bold: true);
            Cell(line, scoreLabel, 110, Dim, bold: true);
        }

        void AddRow(in ScoreRow row, string prefix, Color color)
        {
            VisualElement line = Row(_scoreboardBody);
            line.style.paddingTop = line.style.paddingBottom = 2;
            if (row.Local) line.style.backgroundColor = new Color(Accent.r, Accent.g, Accent.b, 0.12f);
            string name = (prefix.Length > 0 ? prefix + " " : "") + row.Name + (row.Bot ? "  (bot)" : "");
            Cell(line, name, 300, row.Alive ? color : new Color(color.r, color.g, color.b, 0.5f), bold: row.Local, left: true);
            Cell(line, row.Kills.ToString(), 60, Text);
            Cell(line, row.Deaths.ToString(), 60, Text);
            Cell(line, row.Score.ToString(), 110, Text, bold: true);
        }

        static void Cell(VisualElement line, string text, float width, Color color, bool bold = false, bool left = false)
        {
            Label label = Text_(text, 15, color, bold);
            label.style.width = width;
            label.style.unityTextAlign = left ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            line.Add(label);
        }

        void OnDestroy()
        {
            _account?.Dispose();
            ProgressionCache.Changed -= RefreshProfile;
            if (_services != null) _services.Account.Changed -= RefreshProfile;
        }

        /// <summary>A medal under the crosshair: a headshot, a multi-kill, a streak. It shows for a moment and goes.</summary>
        public void ShowMedal(string title, string line = "")
        {
            if (_match == null) return;
            _medal.text = title ?? "";
            _medalLine.text = line ?? "";
            VisualElement box = _medal.parent;
            box.RemoveFromClassList("medal--hidden");
            UiMotion.Pop(box, "medal--hidden");
            _medalTimer = 1.8f;
        }

        /// <summary>Who took us out, with what, and how much health they had left. Null hides it.</summary>
        public void ShowDeath(string title, string line)
        {
            if (_match == null) return;
            if (title == null)
            {
                _death.style.display = DisplayStyle.None;
                return;
            }
            _deathTitle.text = title;
            _deathLine.text = line ?? "";
            _death.style.display = DisplayStyle.Flex;
            UiMotion.Enter(_death);
        }

        void Update()
        {
            if (_medalTimer > 0f)
            {
                _medalTimer -= Time.unscaledDeltaTime;
                if (_medalTimer <= 0f && _medal != null) _medal.parent.AddToClassList("medal--hidden");
            }

            // Kill feed lines fade out and go.
            for (int i = _feed.Count - 1; i >= 0; i--)
            {
                _feedAge[i] += Time.unscaledDeltaTime;
                _feed[i].style.opacity = Mathf.Clamp01(KillFeedSeconds - _feedAge[i]);
                if (_feedAge[i] < KillFeedSeconds) continue;
                _feed[i].RemoveFromHierarchy();
                _feed.RemoveAt(i);
                _feedAge.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ helpers

        static Label Text_(string text, int size, Color color, bool bold = false, int spacing = 0, string className = null)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (spacing > 0) label.style.letterSpacing = spacing;
            if (className != null) label.AddToClassList(className);
            return label;
        }

        static Button MenuButton(string text, bool primary, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("pause-button");
            if (primary)
            {
                button.AddToClassList("primary");
                button.style.marginLeft = 0;
                button.style.height = 46;
            }
            return button;
        }

        static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            parent.Add(row);
            return row;
        }

        /// <summary>Stretches an element over the whole of its parent.</summary>
        static void Fill(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0;
            element.style.top = 0;
            element.style.right = 0;
            element.style.bottom = 0;
        }

        static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
