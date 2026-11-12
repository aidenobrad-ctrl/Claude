using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Pulsebreak.Client.Audio;

namespace Pulsebreak.Client.UI
{
    /// <summary>One row of the players screen.</summary>
    public struct PlayerRow
    {
        public int Id;
        public string Name;
        /// <summary>Empty for bots, guests and players a server did not check: they can be muted but not friended, blocked or reported.</summary>
        public string AccountId;
        public string Side;
        public Color SideColor;
        public bool Bot, Self, Muted, Friend, Asked, Blocked;
    }

    /// <summary>What the players screen can ask for. Each returns a line to show, or null for nothing to say.</summary>
    public sealed class PlayerActions
    {
        public Func<List<PlayerRow>> List;
        public Action<PlayerRow> ToggleMute;
        public Action<PlayerRow> AddFriend;
        public Action<PlayerRow> Block;
        public Action<PlayerRow> Report;
        /// <summary>Signed in, so the account actions are possible.</summary>
        public Func<bool> SignedIn;
    }

    /// <summary>
    /// Everyone in the match, with what can be done about each: mute their chat for this
    /// match, ask to be friends, block, or report. Opened from the pause menu in a match.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayersScreen : MonoBehaviour
    {
        static PlayersScreen _open;
        public static bool IsOpen => _open != null;

        PlayerActions _actions;
        VisualElement _rows;
        Label _hint;
        float _refresh;

        public static PlayersScreen Open(PlayerActions actions)
        {
            if (_open != null) return _open;
            var go = new GameObject("Players Screen");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = HudController.LoadPanel();
            document.sortingOrder = 45f;
            var screen = go.AddComponent<PlayersScreen>();
            screen._actions = actions;
            screen.Build(document.rootVisualElement);
            _open = screen;
            return screen;
        }

        public void Close()
        {
            if (this != null) Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_open == this) _open = null;
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !ReportDialog.IsOpen) Close();
            // People come and go, and requests are answered: keep the list current.
            _refresh -= Time.unscaledDeltaTime;
            if (_refresh <= 0f) Refresh();
        }

        void Build(VisualElement root)
        {
            var styles = Resources.Load<StyleSheet>("UI/HudStyles");
            if (styles != null) root.styleSheets.Add(styles);
            root.AddToClassList("hud");
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            root.style.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 0.82f);
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            UiSounds.Attach(root);

            var card = new VisualElement();
            card.AddToClassList("pause-card");
            card.style.width = 860;
            card.style.maxHeight = Length.Percent(86);
            root.Add(card);
            UiMotion.Enter(card);

            var title = new Label("PLAYERS");
            title.AddToClassList("pause-title");
            card.Add(title);

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            card.Add(scroll);
            _rows = scroll.contentContainer;

            _hint = new Label();
            _hint.AddToClassList("loadout-note");
            _hint.style.whiteSpace = WhiteSpace.Normal;
            _hint.style.marginTop = 10;
            card.Add(_hint);

            var buttons = new VisualElement();
            buttons.AddToClassList("pause-buttons");
            card.Add(buttons);
            var done = new Button(Close) { text = "Done" };
            done.AddToClassList("pause-button");
            done.AddToClassList("primary");
            buttons.Add(done);
            Refresh();
        }

        void Refresh()
        {
            _refresh = 1f;
            _rows.Clear();
            bool signedIn = _actions.SignedIn != null && _actions.SignedIn();
            foreach (PlayerRow row in _actions.List())
            {
                var line = new VisualElement();
                line.style.flexDirection = FlexDirection.Row;
                line.style.alignItems = Align.Center;
                line.style.paddingTop = line.style.paddingBottom = 6;
                line.style.borderBottomWidth = 1;
                line.style.borderBottomColor = new Color(1f, 1f, 1f, 0.06f);
                _rows.Add(line);

                var side = new VisualElement();
                side.style.width = 6;
                side.style.height = 26;
                side.style.marginRight = 10;
                side.style.backgroundColor = row.SideColor;
                line.Add(side);

                var name = new Label(row.Name + (row.Self ? "  (you)" : row.Bot ? "  (bot)" : ""));
                name.style.width = 300;
                name.style.fontSize = 16;
                name.style.unityFontStyleAndWeight = row.Self ? FontStyle.Bold : FontStyle.Normal;
                line.Add(name);
                var team = new Label(row.Side);
                team.AddToClassList("loadout-note");
                team.style.width = 110;
                line.Add(team);

                if (row.Self || row.Bot) continue;
                PlayerRow target = row;
                bool account = signedIn && !string.IsNullOrEmpty(row.AccountId);
                AddButton(line, row.Muted ? "Unmute" : "Mute", true, () => { _actions.ToggleMute(target); Refresh(); });
                AddButton(line, row.Friend ? "Friends" : row.Asked ? "Asked" : "Add friend", account && !row.Friend && !row.Asked && !row.Blocked,
                    () => { _actions.AddFriend(target); Refresh(); });
                AddButton(line, row.Blocked ? "Blocked" : "Block", account && !row.Blocked, () => { _actions.Block(target); Refresh(); });
                AddButton(line, "Report", account, () => _actions.Report(target));
            }
            _hint.text = signedIn
                ? "Mute hides a player's chat for this match. Block hides it for good, ends any friendship, and stops their requests and invites."
                : "Sign in to add friends, block or report. You can still mute anyone's chat.";
        }

        static void AddButton(VisualElement line, string text, bool enabled, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("pause-button");
            button.style.minWidth = 0;
            button.style.width = 104;
            button.style.height = 32;
            button.style.fontSize = 14;
            button.SetEnabled(enabled);
            line.Add(button);
        }
    }
}
