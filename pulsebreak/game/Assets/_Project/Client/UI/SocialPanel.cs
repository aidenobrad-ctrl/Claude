using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using Pulsebreak.Client.Audio;
using Pulsebreak.Client.Online;
using Pulsebreak.Client.Settings;
using Pulsebreak.Shared.Social;

namespace Pulsebreak.Client.UI
{
    /// <summary>
    /// The social side of the main menu: party invites, the party and its chat, friends (with
    /// where they are), friend requests, and adding a friend by name. Everything comes from
    /// <see cref="SocialClient"/>, which checks in with the backend every few seconds.
    ///
    /// The party leader finds matches for the whole party from the main menu; the others
    /// follow automatically. Ranked is played alone.
    /// </summary>
    public sealed class SocialPanel
    {
        static readonly Color Accent = new Color32(38, 216, 255, 255);
        static readonly Color Text = new Color32(240, 244, 250, 255);
        static readonly Color Dim = new Color(0.94f, 0.96f, 0.98f, 0.55f);
        static readonly Color Good = new Color32(96, 232, 140, 255);
        static readonly Color Bad = new Color32(255, 96, 84, 255);
        static readonly Color Card = new Color(0.07f, 0.09f, 0.125f, 1f);

        readonly SocialClient _social;
        readonly AccountClient _account;
        readonly VisualElement _card, _invites, _party, _friends, _requests, _chatLines;
        readonly TextField _chatField, _addField;
        readonly Label _status, _signedOut;
        readonly VisualElement _signedIn;
        readonly Dictionary<string, float> _confirming = new Dictionary<string, float>();

        public VisualElement Root => _card;

        public SocialPanel(VisualElement parent, SocialClient social, AccountClient account)
        {
            _social = social;
            _account = account;

            _card = new VisualElement();
            _card.AddToClassList("pause-card");
            _card.style.width = 420;
            _card.style.maxHeight = Length.Percent(88);
            _card.style.backgroundColor = Card;
            _card.style.paddingLeft = _card.style.paddingRight = 24;
            _card.style.paddingTop = _card.style.paddingBottom = 22;
            parent.Add(_card);

            var title = Label(_card, "SOCIAL", 22, Text, bold: true);
            title.style.letterSpacing = 4;
            title.style.marginBottom = 8;

            _signedOut = Label(_card, "Sign in to add friends, see who is online and play together in a party.", 14, Dim);
            _signedOut.style.whiteSpace = WhiteSpace.Normal;

            _signedIn = new VisualElement();
            _card.Add(_signedIn);
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.style.maxHeight = 560;
            _signedIn.Add(scroll);

            _invites = Block(scroll.contentContainer);
            _party = Block(scroll.contentContainer);
            _chatLines = new VisualElement();
            _chatLines.style.maxHeight = 150;
            _chatLines.style.overflow = Overflow.Hidden;
            _chatLines.style.justifyContent = Justify.FlexEnd;
            var chatRow = new VisualElement();
            chatRow.style.flexDirection = FlexDirection.Row;
            chatRow.style.marginTop = 4;
            _chatField = new TextField { maxLength = ChatText.MaxLength };
            _chatField.style.flexGrow = 1;
            _chatField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                SendChat();
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            chatRow.Add(_chatField);
            chatRow.Add(SmallButton("Say", SendChat));
            _party.parent.Insert(_party.parent.IndexOf(_party) + 1, _chatLines);
            _party.parent.Insert(_party.parent.IndexOf(_chatLines) + 1, chatRow);

            _friends = Block(scroll.contentContainer);
            _requests = Block(scroll.contentContainer);

            var add = Block(_signedIn);
            Header(add, "ADD A FRIEND");
            var addRow = new VisualElement();
            addRow.style.flexDirection = FlexDirection.Row;
            _addField = new TextField { maxLength = 20 };
            _addField.style.flexGrow = 1;
            _addField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                _ = AddFriendAsync();
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            addRow.Add(_addField);
            addRow.Add(SmallButton("Add", () => _ = AddFriendAsync()));
            add.Add(addRow);
            _status = Label(_signedIn, "", 13, Dim);
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.marginTop = 6;

            _social.Changed += Refresh;
            _social.PartyChat += OnPartyChat;
            _social.Invited += invite => UiSounds.Play(Sfx.Promote);
            Refresh();
        }

        public void Dispose()
        {
            _social.Changed -= Refresh;
            _social.PartyChat -= OnPartyChat;
        }

        // ------------------------------------------------------------------ drawing

        void Refresh()
        {
            bool signedIn = _account.SignedIn;
            _signedOut.style.display = signedIn ? DisplayStyle.None : DisplayStyle.Flex;
            _signedIn.style.display = signedIn ? DisplayStyle.Flex : DisplayStyle.None;
            SocialData data = _social.Current;
            if (!signedIn || data == null)
            {
                _chatLines.Clear();
                return;
            }

            DrawInvites(data);
            DrawParty();
            DrawFriends(data);
            DrawRequests(data);
        }

        void DrawInvites(SocialData data)
        {
            _invites.Clear();
            if (data.invites == null || data.invites.Length == 0) return;
            foreach (InviteData invite in data.invites)
            {
                InviteData target = invite;
                var row = Row(_invites);
                row.style.backgroundColor = new Color(0.15f, 0.36f, 0.45f, 0.5f);
                row.style.paddingLeft = 8;
                Label(row, invite.fromName + " invited you to their party", 14, Text).style.flexGrow = 1;
                row.Add(SmallButton("Join", () => _ = Do(_social.JoinPartyAsync(target.partyId), "You joined the party.")));
                row.Add(SmallButton("No", () => _ = Do(_social.DeclineInviteAsync(target.partyId), null)));
            }
        }

        void DrawParty()
        {
            _party.Clear();
            PartyData party = _social.Party;
            bool inParty = _social.InParty;
            _chatLines.style.display = inParty ? DisplayStyle.Flex : DisplayStyle.None;
            _chatField.parent.style.display = inParty ? DisplayStyle.Flex : DisplayStyle.None;
            if (!inParty)
            {
                Header(_party, "PARTY");
                Label(_party, "Invite friends who are online to play together. The party leader finds matches for everyone.", 13, Dim)
                    .style.whiteSpace = WhiteSpace.Normal;
                if (party == null) _chatLines.Clear();
                return;
            }

            var head = Row(_party);
            Header(head, "PARTY  " + party.members.Length + "/" + party.maxSize).style.flexGrow = 1;
            head.Add(SmallButton("Leave", () => _ = Do(_social.LeavePartyAsync(), "You left the party.")));
            bool leader = _social.IsLeader;
            foreach (PartyMemberData member in party.members)
            {
                PartyMemberData target = member;
                var row = Row(_party);
                Dot(row, member.status);
                bool me = member.accountId == _social.MyId;
                Label(row, (member.leader ? "★ " : "") + member.username + (me ? "  (you)" : ""), 15, member.leader ? Accent : Text, bold: me)
                    .style.flexGrow = 1;
                Label(row, Where(member.status), 12, Dim).style.marginRight = 6;
                if (leader && !me) row.Add(Confirm("Remove", "kick:" + member.accountId, () => _ = Do(_social.KickAsync(target.accountId), null)));
            }
            if (party.search != null && party.search.status == "searching")
                Label(_party, leader ? "Finding a match for the party..." : "Your party leader is finding a match. You will join it automatically.", 13, Accent)
                    .style.whiteSpace = WhiteSpace.Normal;
        }

        void DrawFriends(SocialData data)
        {
            _friends.Clear();
            int online = 0;
            foreach (PersonData friend in data.friends) if (friend.Online) online++;
            Header(_friends, "FRIENDS  " + online + " online");
            if (data.friends.Length == 0)
            {
                Label(_friends, "No friends yet. Add someone by their username below.", 13, Dim).style.whiteSpace = WhiteSpace.Normal;
                return;
            }
            PartyData party = _social.Party;
            bool room = party == null || party.members.Length < party.maxSize;
            foreach (PersonData friend in data.friends)
            {
                PersonData target = friend;
                var row = Row(_friends);
                Dot(row, friend.status);
                Label(row, friend.username, 15, friend.Online ? Text : Dim).style.flexGrow = 1;
                Label(row, friend.inParty ? "in your party" : Where(friend.status), 12, Dim).style.marginRight = 6;
                if (friend.Online && !friend.inParty && room)
                    row.Add(SmallButton("Invite", () => _ = Do(_social.InviteAsync(target.accountId), "Invited " + target.username + ".")));
                row.Add(Confirm("Remove", "remove:" + friend.accountId, () => _ = Do(_social.RemoveFriendAsync(target.accountId), null)));
                row.Add(Confirm("Block", "block:" + friend.accountId, () => _ = Do(_social.BlockAsync(target.accountId), target.username + " is blocked.")));
            }
        }

        void DrawRequests(SocialData data)
        {
            _requests.Clear();
            int count = data.incoming.Length + data.outgoing.Length + data.blocked.Length;
            if (count == 0) return;
            if (data.incoming.Length + data.outgoing.Length > 0) Header(_requests, "REQUESTS");
            foreach (PersonData person in data.incoming)
            {
                PersonData target = person;
                var row = Row(_requests);
                Label(row, person.username + " wants to be friends", 14, Text).style.flexGrow = 1;
                row.Add(SmallButton("Accept", () => _ = Do(_social.AcceptFriendAsync(target.accountId), "You and " + target.username + " are friends.")));
                row.Add(SmallButton("Decline", () => _ = Do(_social.RemoveFriendAsync(target.accountId), null)));
            }
            foreach (PersonData person in data.outgoing)
            {
                PersonData target = person;
                var row = Row(_requests);
                Label(row, "Asked " + person.username, 14, Dim).style.flexGrow = 1;
                row.Add(SmallButton("Cancel", () => _ = Do(_social.RemoveFriendAsync(target.accountId), null)));
            }
            if (data.blocked.Length > 0)
            {
                Header(_requests, "BLOCKED");
                foreach (PersonData person in data.blocked)
                {
                    PersonData target = person;
                    var row = Row(_requests);
                    Label(row, person.username, 14, Dim).style.flexGrow = 1;
                    row.Add(SmallButton("Unblock", () => _ = Do(_social.UnblockAsync(target.accountId), null)));
                }
            }
        }

        void OnPartyChat(ChatLineData line)
        {
            if (!GameSettings.ShowChat) return;
            bool system = string.IsNullOrEmpty(line.accountId);
            string text = GameSettings.FilterChat ? ChatText.Filter(line.text) : line.text;
            var label = new Label(system ? text : line.username + ":  " + text) { enableRichText = false };
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 13;
            label.style.color = system ? Dim : Text;
            _chatLines.Add(label);
            while (_chatLines.childCount > 30) _chatLines.RemoveAt(0);
            if (!system && line.accountId != _social.MyId) UiSounds.Play(Sfx.UiToggle, 0.35f);
        }

        void SendChat()
        {
            string text = _chatField.value;
            if (string.IsNullOrWhiteSpace(text)) return;
            _chatField.SetValueWithoutNotify("");
            _ = Do(_social.SayAsync(text), null);
        }

        async Task AddFriendAsync()
        {
            string name = (_addField.value ?? "").Trim();
            if (name.Length == 0) return;
            AccountResult result = await _social.AddFriendAsync(name);
            if (result.Ok)
            {
                _addField.SetValueWithoutNotify("");
                Say("Friend request sent to " + name + ".", Good);
            }
            else
            {
                Say(result.Message, Bad);
                UiSounds.Play(Sfx.UiError);
            }
        }

        async Task Do(Task<AccountResult> action, string success)
        {
            AccountResult result = await action;
            if (result.Ok)
            {
                if (success != null) Say(success, Good);
            }
            else
            {
                Say(result.Message, Bad);
                UiSounds.Play(Sfx.UiError);
            }
        }

        void Say(string text, Color color)
        {
            _status.text = text ?? "";
            _status.style.color = color;
        }

        // ------------------------------------------------------------------ pieces

        /// <summary>A button that asks once more before it acts.</summary>
        Button Confirm(string text, string key, Action act)
        {
            Button button = null;
            button = SmallButton(text, () =>
            {
                if (_confirming.TryGetValue(key, out float at) && Time.unscaledTime - at < 3f)
                {
                    _confirming.Remove(key);
                    act();
                    return;
                }
                _confirming[key] = Time.unscaledTime;
                button.text = "Sure?";
                button.schedule.Execute(() => button.text = text).StartingIn(3000);
            });
            return button;
        }

        static string Where(string status)
        {
            switch (status)
            {
                case "match": return "in a match";
                case "range": return "on the range";
                case "menu": return "in the menu";
                default: return "offline";
            }
        }

        static void Dot(VisualElement row, string status)
        {
            var dot = new VisualElement();
            dot.style.width = dot.style.height = 9;
            dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius = dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 5;
            dot.style.marginRight = 8;
            dot.style.backgroundColor = status == "offline" ? new Color(1f, 1f, 1f, 0.2f) : status == "match" ? (Color)new Color32(255, 196, 64, 255) : Good;
            row.Add(dot);
        }

        static VisualElement Block(VisualElement parent)
        {
            var block = new VisualElement();
            block.style.marginBottom = 10;
            parent.Add(block);
            return block;
        }

        static Label Header(VisualElement parent, string text)
        {
            Label label = Label(parent, text, 12, Dim, bold: true);
            label.style.letterSpacing = 2;
            label.style.marginTop = 6;
            label.style.marginBottom = 4;
            return label;
        }

        static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 30;
            parent.Add(row);
            return row;
        }

        static Label Label(VisualElement parent, string text, int size, Color color, bool bold = false)
        {
            var label = new Label(text) { enableRichText = false };
            label.style.fontSize = size;
            label.style.color = color;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            parent.Add(label);
            return label;
        }

        static Button SmallButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("pause-button");
            button.style.minWidth = 0;
            button.style.height = 28;
            button.style.fontSize = 13;
            button.style.marginLeft = 4;
            button.style.paddingLeft = button.style.paddingRight = 8;
            return button;
        }
    }
}
