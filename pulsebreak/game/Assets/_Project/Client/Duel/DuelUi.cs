using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Pulsebreak.Client.Online;
using Pulsebreak.Client.UI;
using Cursor = UnityEngine.Cursor;

namespace Pulsebreak.Client.Duel
{
    /// <summary>
    /// Everything on screen that belongs to an online duel rather than to the weapon in your
    /// hands: the menu to host or join, and during a match the score, the round clock, your
    /// health, the kill feed and the big announcements.
    ///
    /// It sits on top of the ordinary HUD and only shows what it is told. The duel's logic
    /// lives in <see cref="DuelBootstrap"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DuelUi : MonoBehaviour
    {
        const string NameKey = "pb.playerName", AddressKey = "pb.lastAddress";
        const float KillFeedSeconds = 6f;
        const int KillFeedLines = 4;

        static readonly Color Accent = new Color32(38, 216, 255, 255);
        static readonly Color Text = new Color32(240, 244, 250, 255);
        static readonly Color Dim = new Color(0.94f, 0.96f, 0.98f, 0.62f);
        static readonly Color Good = new Color32(96, 232, 140, 255);
        static readonly Color Bad = new Color32(255, 96, 84, 255);

        VisualElement _menu, _match;
        TextField _nameField, _addressField;
        Label _menuStatus, _menuHint;
        Button _hostButton, _joinButton, _rangeButton, _quitButton, _cancelButton;

        Label _scoreLeft, _scoreRight, _nameLeft, _nameRight, _round, _clock;
        Label _bannerTitle, _bannerSub, _notice, _netStats, _healthText;
        VisualElement _healthFill;
        readonly List<Label> _feed = new List<Label>();
        readonly List<float> _feedAge = new List<float>();
        VisualElement _feedBox;

        public event Action HostRequested;
        /// <summary>Argument: the address typed in.</summary>
        public event Action<string> JoinRequested;
        public event Action RangeRequested;
        public event Action QuitRequested;
        /// <summary>Pressed while an attempt to connect is under way.</summary>
        public event Action CancelRequested;

        public string PlayerName => _nameField != null ? _nameField.value.Trim() : "";

        AccountPanel _account;
        VisualElement _mainCard;
        GameServices _services;

        /// <param name="services">The player's account and loadouts. The menu shows who is signed in and offers the sign-in forms.</param>
        public static DuelUi Create(GameServices services)
        {
            var go = new GameObject("Duel UI");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = HudController.LoadPanel();
            document.sortingOrder = 10f;

            var ui = go.AddComponent<DuelUi>();
            ui._services = services;
            ui.Build(document.rootVisualElement);
            return ui;
        }

        // ------------------------------------------------------------------ construction

        void Build(VisualElement root)
        {
            if (root == null)
            {
                Debug.LogError("Pulsebreak: the duel UI could not be created (no UI root).");
                return;
            }

            // The look comes from the HUD's stylesheet. The layout that matters is also set
            // here in code, so a missing stylesheet costs polish, not a usable menu.
            var styles = Resources.Load<StyleSheet>("UI/HudStyles");
            if (styles != null) root.styleSheets.Add(styles);
            else Debug.LogWarning("Pulsebreak: Resources/UI/HudStyles.uss did not load; the duel menu will look plain.");
            root.AddToClassList("hud");
            root.pickingMode = PickingMode.Ignore;
            Fill(root);
            root.style.color = Text;
            root.style.fontSize = 16;

            BuildMatch(root);
            BuildMenu(root);
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
            card.style.width = 520;
            card.style.paddingLeft = card.style.paddingRight = 32;
            card.style.paddingTop = card.style.paddingBottom = 28;
            card.style.backgroundColor = new Color(0.07f, 0.09f, 0.125f, 1f);
            _menu.Add(card);

            card.Add(Text_("PULSEBREAK", 30, Text, bold: true, spacing: 6, className: "pause-title"));
            Label subtitle = Text_("1v1 DUEL  ·  BEST OF FIVE", 13, Accent, bold: true, spacing: 3);
            subtitle.style.marginTop = -12;
            subtitle.style.marginBottom = 18;
            card.Add(subtitle);
            _mainCard = card;

            if (_services != null)
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

            _nameField = new TextField("Name") { value = PlayerPrefs.GetString(NameKey, "Player"), maxLength = 24 };
            _nameField.AddToClassList("loadout-name");
            _nameField.style.fontSize = 16;
            card.Add(_nameField);
            _account?.Refresh();

            _hostButton = MenuButton("Host a match", true, () => HostRequested?.Invoke());
            _hostButton.style.marginTop = 14;
            card.Add(_hostButton);
            _menuHint = Text_("Starts a match on this computer and puts you in it. Someone else joins with your address.", 12, Dim);
            _menuHint.style.whiteSpace = WhiteSpace.Normal;
            _menuHint.style.marginTop = 6;
            _menuHint.style.marginBottom = 16;
            card.Add(_menuHint);

            var joinRow = new VisualElement();
            joinRow.style.flexDirection = FlexDirection.Row;
            joinRow.style.alignItems = Align.Center;
            _addressField = new TextField { value = PlayerPrefs.GetString(AddressKey, "127.0.0.1"), maxLength = 64 };
            _addressField.AddToClassList("loadout-name");
            _addressField.style.fontSize = 16;
            _addressField.style.flexGrow = 1;
            joinRow.Add(_addressField);
            _joinButton = MenuButton("Join", false, RequestJoin);
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
            bottom.style.marginTop = 22;
            _cancelButton = MenuButton("Cancel", false, () => CancelRequested?.Invoke());
            _rangeButton = MenuButton("Firing range", false, () => RangeRequested?.Invoke());
            _quitButton = MenuButton("Quit", false, () => QuitRequested?.Invoke());
            bottom.Add(_cancelButton);
            bottom.Add(_rangeButton);
            bottom.Add(_quitButton);
            card.Add(bottom);
        }

        void RequestJoin()
        {
            string address = _addressField.value.Trim();
            if (address.Length == 0) return;
            PlayerPrefs.SetString(AddressKey, address);
            JoinRequested?.Invoke(address);
        }

        void BuildMatch(VisualElement root)
        {
            _match = new VisualElement();
            _match.AddToClassList("layer");
            _match.pickingMode = PickingMode.Ignore;
            Fill(_match);
            root.Add(_match);

            // Score, round and clock across the top.
            var top = Row(_match);
            top.style.position = Position.Absolute;
            top.style.top = 22;
            top.style.left = 0;
            top.style.right = 0;
            top.style.justifyContent = Justify.Center;
            top.style.alignItems = Align.Center;

            _nameLeft = Text_("", 18, Text, bold: true);
            _nameLeft.style.width = 260;
            _nameLeft.style.unityTextAlign = TextAnchor.MiddleRight;
            _scoreLeft = Text_("0", 40, Good, bold: true);
            _scoreLeft.style.width = 64;
            _scoreLeft.style.unityTextAlign = TextAnchor.MiddleCenter;

            var middle = new VisualElement();
            middle.style.width = 130;
            middle.style.alignItems = Align.Center;
            middle.pickingMode = PickingMode.Ignore;
            _clock = Text_("", 24, Text, bold: true);
            _round = Text_("", 12, Dim, bold: true, spacing: 3);
            middle.Add(_clock);
            middle.Add(_round);

            _scoreRight = Text_("0", 40, Bad, bold: true);
            _scoreRight.style.width = 64;
            _scoreRight.style.unityTextAlign = TextAnchor.MiddleCenter;
            _nameRight = Text_("", 18, Text, bold: true);
            _nameRight.style.width = 260;

            top.Add(_nameLeft);
            top.Add(_scoreLeft);
            top.Add(middle);
            top.Add(_scoreRight);
            top.Add(_nameRight);

            // Announcements, a third of the way down.
            var banner = new VisualElement();
            banner.pickingMode = PickingMode.Ignore;
            banner.style.position = Position.Absolute;
            banner.style.top = Length.Percent(24);
            banner.style.left = 0;
            banner.style.right = 0;
            banner.style.alignItems = Align.Center;
            _bannerTitle = Text_("", 64, Text, bold: true, spacing: 8);
            _bannerSub = Text_("", 20, Dim);
            _notice = Text_("", 15, Accent);
            _notice.style.marginTop = 10;
            banner.Add(_bannerTitle);
            banner.Add(_bannerSub);
            banner.Add(_notice);
            _match.Add(banner);

            // Health, bottom centre.
            var health = new VisualElement();
            health.pickingMode = PickingMode.Ignore;
            health.style.position = Position.Absolute;
            health.style.bottom = 34;
            health.style.left = 0;
            health.style.right = 0;
            health.style.alignItems = Align.Center;
            _healthText = Text_("100", 26, Text, bold: true);
            var bar = new VisualElement();
            bar.pickingMode = PickingMode.Ignore;
            bar.style.width = 300;
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
        }

        // ------------------------------------------------------------------ menu

        /// <summary>Shows the menu, ready for input. <paramref name="status"/> is a line of bad news, or empty.</summary>
        public void ShowMenu(string status)
        {
            if (_menu == null) return;
            _menu.style.display = DisplayStyle.Flex;
            _menuStatus.text = status ?? "";
            _menuStatus.style.color = Bad;
            SetBusy(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>Shows the menu with its buttons off while a connection is being made.</summary>
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
            _hostButton.SetEnabled(!busy);
            _joinButton.SetEnabled(!busy);
            _rangeButton.SetEnabled(!busy);
            _nameField.SetEnabled(!busy);
            _addressField.SetEnabled(!busy);
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
            foreach (Label line in _feed) line.RemoveFromHierarchy();
            _feed.Clear();
            _feedAge.Clear();
        }

        /// <summary>Our name and score on the left, theirs on the right.</summary>
        public void SetScore(string ourName, int ourScore, string theirName, int theirScore, string round, string clock)
        {
            if (_match == null) return;
            _nameLeft.text = ourName;
            _scoreLeft.text = ourScore.ToString();
            _nameRight.text = theirName;
            _scoreRight.text = theirScore.ToString();
            _round.text = round;
            _clock.text = clock;
        }

        public void SetHealth(float current, float max)
        {
            if (_match == null) return;
            float fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            _healthText.text = Mathf.CeilToInt(Mathf.Max(current, 0f)).ToString();
            _healthFill.style.width = Length.Percent(fraction * 100f);
            _healthFill.style.backgroundColor = Color.Lerp(Bad, Good, Mathf.InverseLerp(0.2f, 0.6f, fraction));
        }

        /// <summary>The big announcement in the middle of the screen. Empty strings clear it.</summary>
        public void SetBanner(string title, string subtitle, bool good = true)
        {
            if (_match == null) return;
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

        public void AddKill(string text, bool ours)
        {
            if (_match == null) return;
            Label line = Text_(text, 15, ours ? Good : Text, bold: true);
            line.style.paddingLeft = 10;
            line.style.paddingRight = 10;
            line.style.paddingTop = 4;
            line.style.paddingBottom = 4;
            line.style.marginBottom = 4;
            line.style.backgroundColor = new Color(0.04f, 0.06f, 0.08f, 0.6f);
            Round(line, 4);
            _feedBox.Add(line);
            _feed.Add(line);
            _feedAge.Add(0f);

            if (_feed.Count <= KillFeedLines) return;
            _feed[0].RemoveFromHierarchy();
            _feed.RemoveAt(0);
            _feedAge.RemoveAt(0);
        }

        void OnDestroy() => _account?.Dispose();

        void Update()
        {
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
