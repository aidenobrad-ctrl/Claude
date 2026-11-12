using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Pulsebreak.Client.Audio;
using Pulsebreak.Client.Online;

namespace Pulsebreak.Client.UI
{
    /// <summary>
    /// Reporting a player: why, a few words, and (if they said anything) their recent chat,
    /// which goes with the report so a reviewer can see it. Sent to the backend for a person
    /// to look at; the player reported is not told.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ReportDialog : MonoBehaviour
    {
        static readonly (string Id, string Label)[] Reasons =
        {
            ("cheating", "Cheating"),
            ("abusive_chat", "Abusive chat"),
            ("offensive_name", "Offensive name"),
            ("griefing", "Ruining the match on purpose"),
            ("other", "Something else"),
        };

        static ReportDialog _open;
        public static bool IsOpen => _open != null;

        Func<ReportBody, Task<AccountResult>> _send;
        ReportBody _report;
        DropdownField _reason;
        TextField _details;
        Toggle _includeChat;
        Label _result;
        Button _sendButton;
        string[] _chat;

        /// <param name="chat">What they said that we saw, newest last. Empty if nothing.</param>
        public static ReportDialog Open(string name, string accountId, string matchId, IReadOnlyList<string> chat, Func<ReportBody, Task<AccountResult>> send)
        {
            if (_open != null) _open.Close();
            var go = new GameObject("Report Dialog");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = HudController.LoadPanel();
            document.sortingOrder = 60f;
            var dialog = go.AddComponent<ReportDialog>();
            dialog._send = send;
            dialog._chat = chat != null ? new List<string>(chat).ToArray() : new string[0];
            dialog._report = new ReportBody { accountId = accountId, matchId = matchId ?? "" };
            dialog.Build(document.rootVisualElement, name);
            _open = dialog;
            return dialog;
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
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Close();
        }

        void Build(VisualElement root, string name)
        {
            var styles = Resources.Load<StyleSheet>("UI/HudStyles");
            if (styles != null) root.styleSheets.Add(styles);
            root.AddToClassList("hud");
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            root.style.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 0.7f);
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            UiSounds.Attach(root);

            var card = new VisualElement();
            card.AddToClassList("pause-card");
            card.style.width = 560;
            root.Add(card);
            UiMotion.Enter(card);

            var title = new Label("REPORT " + (name ?? "").ToUpperInvariant());
            title.AddToClassList("pause-title");
            card.Add(title);

            var choices = new List<string>();
            foreach (var reason in Reasons) choices.Add(reason.Label);
            _reason = new DropdownField("Why", choices, 0);
            card.Add(_reason);

            _details = new TextField("What happened") { multiline = true, maxLength = 400 };
            _details.style.minHeight = 90;
            card.Add(_details);

            _includeChat = new Toggle("Include their chat (" + _chat.Length + " line" + (_chat.Length == 1 ? ")" : "s)")) { value = _chat.Length > 0 };
            _includeChat.SetEnabled(_chat.Length > 0);
            card.Add(_includeChat);

            var note = new Label("Reports go to a person to review. The player is not told who reported them.");
            note.AddToClassList("loadout-note");
            note.style.whiteSpace = WhiteSpace.Normal;
            card.Add(note);

            _result = new Label();
            _result.style.whiteSpace = WhiteSpace.Normal;
            _result.style.marginTop = 8;
            card.Add(_result);

            var buttons = new VisualElement();
            buttons.AddToClassList("pause-buttons");
            card.Add(buttons);
            var cancel = new Button(Close) { text = "Cancel" };
            cancel.AddToClassList("pause-button");
            buttons.Add(cancel);
            _sendButton = new Button(() => _ = SendAsync()) { text = "Send report" };
            _sendButton.AddToClassList("pause-button");
            _sendButton.AddToClassList("primary");
            buttons.Add(_sendButton);
        }

        async Task SendAsync()
        {
            _report.reason = Reasons[Mathf.Clamp(_reason.index, 0, Reasons.Length - 1)].Id;
            _report.details = _details.value ?? "";
            _report.chat = _includeChat.value ? _chat : new string[0];
            _sendButton.SetEnabled(false);
            _result.text = "Sending...";
            _result.style.color = new Color(1f, 1f, 1f, 0.7f);
            AccountResult result = await _send(_report);
            if (this == null) return;
            if (result.Ok)
            {
                _result.text = "Thank you. The report has been sent.";
                _result.style.color = (Color)new Color32(96, 232, 140, 255);
                schedule.Execute(Close).StartingIn(1400);
            }
            else
            {
                _result.text = result.Message;
                _result.style.color = (Color)new Color32(255, 96, 84, 255);
                _sendButton.SetEnabled(true);
                UiSounds.Play(Sfx.UiError);
            }
        }

        IVisualElementScheduler schedule => GetComponent<UIDocument>().rootVisualElement.schedule;
    }
}
