using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Pulsebreak.Client.Online;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Progression;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.UI
{
    /// <summary>
    /// A signed-in player's progress: their level, today's and this week's challenges, what
    /// is unlocked and what comes next, their weapon levels, their ranks with each ranked
    /// playlist's leaderboard, and their recent matches. Everything comes from the backend;
    /// nothing here can be changed, only looked at.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProgressScreen : MonoBehaviour
    {
        static readonly Color Accent = new Color32(38, 216, 255, 255);
        static readonly Color Text = new Color32(240, 244, 250, 255);
        static readonly Color Dim = new Color(0.94f, 0.96f, 0.98f, 0.62f);
        static readonly Color Good = new Color32(96, 232, 140, 255);
        static readonly Color Bad = new Color32(255, 96, 84, 255);
        static readonly Color Card = new Color(0.07f, 0.09f, 0.125f, 1f);
        static readonly Color Panel = new Color(0.11f, 0.14f, 0.19f, 1f);
        static readonly string[] TabNames = { "OVERVIEW", "WEAPONS", "RANKED", "HISTORY" };

        static ProgressScreen _open;

        GameServices _services;
        LoadoutCatalog _catalog;
        VisualElement _body;
        Label _status;
        readonly List<Button> _tabs = new List<Button>();
        int _tab;
        HistoryEntryData[] _history;
        readonly Dictionary<string, LeaderboardEntryData[]> _boards = new Dictionary<string, LeaderboardEntryData[]>();

        public static bool IsOpen => _open != null;

        public static ProgressScreen Open(GameServices services, LoadoutCatalog catalog)
        {
            if (_open != null) return _open;
            if (services == null || !services.Account.SignedIn) return null;

            var go = new GameObject("Progress Screen");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = HudController.LoadPanel();
            document.sortingOrder = 40f;
            var screen = go.AddComponent<ProgressScreen>();
            screen._services = services;
            screen._catalog = catalog;
            screen.Build(document.rootVisualElement);
            _open = screen;
            screen.Load();
            return screen;
        }

        public void Close() => Destroy(gameObject);

        void OnDestroy()
        {
            ProgressionCache.Changed -= Redraw;
            if (_open == this) _open = null;
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Close();
        }

        async void Load()
        {
            _status.text = "Fetching your progress...";
            await _services.RefreshProgressionAsync();
            if (this == null) return;
            AccountResult<HistoryList> history = await _services.Account.GetHistoryAsync(10);
            if (this == null) return;
            if (history.Ok && history.Value != null) _history = history.Value.items;
            foreach (Playlist playlist in Playlists.All)
            {
                if (!playlist.Ranked) continue;
                AccountResult<LeaderboardList> board = await _services.Account.GetLeaderboardAsync(playlist.Id, 15);
                if (this == null) return;
                if (board.Ok && board.Value != null) _boards[playlist.Id] = board.Value.items;
            }
            _status.text = ProgressionCache.Current == null ? "Could not reach the account server." : "";
            Redraw();
        }

        // ------------------------------------------------------------------ construction

        void Build(VisualElement root)
        {
            var styles = Resources.Load<StyleSheet>("UI/HudStyles");
            if (styles != null) root.styleSheets.Add(styles);
            root.AddToClassList("hud");
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            root.style.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 0.88f);
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.color = Text;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;

            var card = new VisualElement();
            card.AddToClassList("pause-card");
            card.style.width = 1100;
            card.style.height = 760;
            card.style.backgroundColor = Card;
            card.style.paddingLeft = card.style.paddingRight = 28;
            card.style.paddingTop = card.style.paddingBottom = 22;
            root.Add(card);

            var header = Row(card);
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            var title = new Label("PROGRESS");
            title.AddToClassList("pause-title");
            title.style.marginBottom = 0;
            header.Add(title);
            _status = Label("", 13, Dim);
            header.Add(_status);

            var tabs = Row(card);
            tabs.AddToClassList("armory-tabs");
            tabs.style.marginTop = 12;
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                var tab = new Button(() => SelectTab(index)) { text = TabNames[i] };
                tab.AddToClassList("armory-tab");
                tabs.Add(tab);
                _tabs.Add(tab);
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            card.Add(scroll);
            _body = scroll.contentContainer;

            var buttons = Row(card);
            buttons.AddToClassList("pause-buttons");
            buttons.style.justifyContent = Justify.FlexEnd;
            var done = new Button(Close) { text = "Done" };
            done.AddToClassList("pause-button");
            done.AddToClassList("primary");
            buttons.Add(done);

            ProgressionCache.Changed += Redraw;
            SelectTab(0);
        }

        void SelectTab(int tab)
        {
            _tab = tab;
            for (int i = 0; i < _tabs.Count; i++) _tabs[i].EnableInClassList("armory-tab--active", i == _tab);
            Redraw();
        }

        void Redraw()
        {
            if (_body == null) return;
            _body.Clear();
            ProgressionData data = ProgressionCache.Current;
            if (data == null)
            {
                _body.Add(Label("Nothing to show yet.", 15, Dim));
                return;
            }
            switch (_tab)
            {
                case 0: Overview(data); break;
                case 1: Weapons(data); break;
                case 2: Ranked(data); break;
                default: History(); break;
            }
        }

        // ------------------------------------------------------------------ tabs

        void Overview(ProgressionData data)
        {
            LevelData level = data.level;
            var top = Section("LEVEL " + level.level);
            bool max = level.level >= level.maxLevel;
            Bar(top, max ? 1f : level.xpIntoLevel / (float)Mathf.Max(1, level.xpForNextLevel), Accent,
                max ? "Top level, " + level.xp + " XP in all" : level.xpIntoLevel + " / " + level.xpForNextLevel + " XP to level " + (level.level + 1));
            Label(top, "Matches pay XP for finishing, winning, kills, headshots, rounds and objectives; ranked pays a quarter more. " +
                       "Only matches found through Find match count.", 12, Dim, wrap: true);

            var next = NextUnlocks(level.level);
            if (next.Count > 0)
            {
                Label(top, "NEXT TO UNLOCK", 12, Dim, bold: true).style.marginTop = 10;
                foreach (string line in next) Label(top, line, 14, Text);
            }

            foreach (bool weekly in new[] { false, true })
            {
                var section = Section(weekly ? "THIS WEEK" : "TODAY");
                long ends = 0;
                foreach (ChallengeData challenge in data.challenges)
                {
                    if (challenge.weekly != weekly) continue;
                    ends = challenge.endsAt;
                    var row = Row(section);
                    row.style.alignItems = Align.Center;
                    row.style.marginBottom = 6;
                    var left = new VisualElement();
                    left.style.flexGrow = 1;
                    row.Add(left);
                    Label(left, (challenge.completed ? "✓  " : "") + challenge.text, 15, challenge.completed ? Good : Text);
                    Bar(left, challenge.progress / (float)Mathf.Max(1, challenge.goal), challenge.completed ? Good : Accent,
                        challenge.progress + " / " + challenge.goal);
                    Label rewardLabel = Label(row, "+" + challenge.rewardXp + " XP", 14, challenge.completed ? Dim : Accent, bold: true);
                    rewardLabel.style.width = 90;
                    rewardLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                }
                if (ends > 0) Label(section, "New ones in " + Until(ends) + ".", 12, Dim);
            }
        }

        /// <summary>The next few things the player's level will open up.</summary>
        List<string> NextUnlocks(int level)
        {
            var items = new List<(int Level, string Text)>();
            if (_catalog != null)
            {
                foreach (WeaponDefinition weapon in _catalog.weapons.weapons)
                    if (weapon != null && weapon.unlockLevel > level) items.Add((weapon.unlockLevel, weapon.displayName + " (" + weapon.slot + ")"));
                foreach (UtilityDefinition utility in _catalog.utilities)
                    if (utility != null && utility.unlockLevel > level) items.Add((utility.unlockLevel, utility.displayName + " (utility)"));
                foreach (PerkSetDefinition perkSet in _catalog.perkSets)
                    if (perkSet != null && perkSet.unlockLevel > level) items.Add((perkSet.unlockLevel, perkSet.displayName + " (perk set)"));
            }
            items.Sort((a, b) => a.Level.CompareTo(b.Level));
            var lines = new List<string>();
            for (int i = 0; i < items.Count && i < 4; i++) lines.Add("Level " + items[i].Level + ":  " + items[i].Text);
            return lines;
        }

        void Weapons(ProgressionData data)
        {
            var section = Section("WEAPON LEVELS");
            Label(section, "Kills with a weapon level it up, and levels unlock its attachments for matchmaking.", 12, Dim, wrap: true);
            var levels = new Dictionary<string, WeaponProgressData>();
            foreach (WeaponProgressData weapon in data.weapons) levels[weapon.weaponId] = weapon;

            if (_catalog == null) return;
            var weapons = new List<WeaponDefinition>(_catalog.weapons.weapons);
            weapons.RemoveAll(w => w == null);
            weapons.Sort((a, b) =>
            {
                levels.TryGetValue(a.weaponId, out WeaponProgressData pa);
                levels.TryGetValue(b.weaponId, out WeaponProgressData pb);
                int xa = pa != null ? pa.xp : -1, xb = pb != null ? pb.xp : -1;
                return xa != xb ? xb.CompareTo(xa) : a.unlockLevel.CompareTo(b.unlockLevel);
            });

            foreach (WeaponDefinition weapon in weapons)
            {
                levels.TryGetValue(weapon.weaponId, out WeaponProgressData progress);
                bool locked = weapon.unlockLevel > data.level.level;
                var row = Row(section);
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 4;
                Label name = Label(row, weapon.displayName, 15, locked ? Dim : Text);
                name.style.width = 220;
                Label lv = Label(row, locked ? "Unlocks at level " + weapon.unlockLevel : "Lv " + (progress != null ? progress.level : 1), 14,
                    locked ? Dim : Accent, bold: !locked);
                lv.style.width = 170;
                var bars = new VisualElement();
                bars.style.flexGrow = 1;
                row.Add(bars);
                if (!locked)
                {
                    float fraction = progress == null ? 0f : progress.level >= Levels.MaxWeaponLevel ? 1f
                        : progress.xpIntoLevel / (float)Mathf.Max(1, progress.xpForNextLevel);
                    Bar(bars, fraction, Accent, (progress != null ? progress.kills : 0) + " kills" + NextAttachment(weapon, progress != null ? progress.level : 1));
                }
            }
        }

        string NextAttachment(WeaponDefinition weapon, int level)
        {
            AttachmentDefinition next = null;
            foreach (AttachmentDefinition attachment in weapon.attachments ?? new AttachmentDefinition[0])
            {
                if (attachment == null || attachment.unlockLevel <= level) continue;
                if (next == null || attachment.unlockLevel < next.unlockLevel) next = attachment;
            }
            return next != null ? "   ·   next: " + next.displayName + " at Lv " + next.unlockLevel : "";
        }

        void Ranked(ProgressionData data)
        {
            foreach (Playlist playlist in Playlists.All)
            {
                if (!playlist.Ranked) continue;
                RankData rank = ProgressionCache.Rank(playlist.Id);
                var section = Section(playlist.Title.ToUpperInvariant());
                var row = Row(section);
                row.style.alignItems = Align.Center;
                Label tier = Label(row, rank.name, 26, Accent, bold: true);
                tier.style.width = 260;
                Label(row, rank.placementLeft > 0
                    ? rank.placementLeft + " placement match(es) left before your rank shows."
                    : "Rating " + rank.rating + "   ·   " + rank.wins + " won, " + rank.losses + " lost" + (rank.draws > 0 ? ", " + rank.draws + " drawn" : ""), 14, Dim);

                Label(section, "LEADERBOARD", 12, Dim, bold: true).style.marginTop = 8;
                if (!_boards.TryGetValue(playlist.Id, out LeaderboardEntryData[] board) || board.Length == 0)
                {
                    Label(section, "Nobody has finished their placement matches yet.", 13, Dim);
                    continue;
                }
                string me = _services.Account.Account?.username;
                foreach (LeaderboardEntryData entry in board)
                {
                    var line = Row(section);
                    Color colour = entry.name == me ? Accent : Text;
                    Label(line, entry.place + ".", 14, Dim).style.width = 40;
                    Label(line, entry.name, 14, colour, bold: entry.name == me).style.width = 260;
                    Label(line, entry.rankName, 14, colour).style.width = 160;
                    Label(line, entry.rating.ToString(), 14, colour).style.width = 80;
                    Label(line, entry.wins + " / " + entry.matches + " won", 14, Dim);
                }
            }
            Label(_body, "Ranked: sign in with a verified email, pick Duel or Elimination and tick Ranked. Matches are full, of people only, " +
                         "with players near your rating; leaving or not turning up counts as a loss.", 12, Dim, wrap: true);
        }

        void History()
        {
            var section = Section("RECENT MATCHES");
            if (_history == null || _history.Length == 0)
            {
                Label(section, "No matchmade matches yet. Find a match from the menu.", 14, Dim);
                return;
            }
            foreach (HistoryEntryData match in _history)
            {
                var row = Row(section);
                row.style.marginBottom = 4;
                Playlist playlist = Playlists.Find(match.playlist);
                Color colour = match.outcome == 2 ? Good : match.outcome == 0 ? Bad : Text;
                Label(row, match.outcome == 2 ? "WIN" : match.outcome == 1 ? "DRAW" : "LOSS", 14, colour, bold: true).style.width = 70;
                Label(row, playlist != null ? playlist.Title : match.playlist, 14, Text).style.width = 220;
                Label(row, match.kills + " / " + match.deaths, 14, Text).style.width = 90;
                Label(row, "+" + match.xpGained + " XP", 14, Accent).style.width = 110;
                Label(row, match.ranked ? (match.ratingChange >= 0 ? "+" : "") + match.ratingChange + " rating" : "", 14, Dim).style.width = 120;
                Label(row, (match.completed ? "" : "left early   ") + Ago(match.endedAt), 13, Dim);
            }
        }

        // ------------------------------------------------------------------ helpers

        VisualElement Section(string title)
        {
            var section = new VisualElement();
            section.style.backgroundColor = Panel;
            section.style.marginTop = 12;
            section.style.paddingLeft = section.style.paddingRight = 16;
            section.style.paddingTop = section.style.paddingBottom = 12;
            section.style.borderTopLeftRadius = section.style.borderTopRightRadius = 8;
            section.style.borderBottomLeftRadius = section.style.borderBottomRightRadius = 8;
            _body.Add(section);
            Label(section, title, 14, Dim, bold: true).style.marginBottom = 8;
            return section;
        }

        static void Bar(VisualElement parent, float fraction, Color colour, string caption)
        {
            var track = new VisualElement();
            track.style.height = 8;
            track.style.marginTop = 3;
            track.style.backgroundColor = new Color(0.04f, 0.06f, 0.08f, 1f);
            track.style.borderTopLeftRadius = track.style.borderTopRightRadius = track.style.borderBottomLeftRadius = track.style.borderBottomRightRadius = 4;
            var fill = new VisualElement();
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
            fill.style.backgroundColor = colour;
            fill.style.borderTopLeftRadius = fill.style.borderTopRightRadius = fill.style.borderBottomLeftRadius = fill.style.borderBottomRightRadius = 4;
            track.Add(fill);
            parent.Add(track);
            Label(parent, caption, 12, Dim).style.marginTop = 2;
        }

        static Label Label(string text, int size, Color colour)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = colour;
            return label;
        }

        static Label Label(VisualElement parent, string text, int size, Color colour, bool bold = false, bool wrap = false)
        {
            Label label = Label(text, size, colour);
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (wrap) label.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(label);
            return label;
        }

        static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            parent.Add(row);
            return row;
        }

        static string Until(long unixSeconds)
        {
            TimeSpan left = DateTimeOffset.FromUnixTimeSeconds(unixSeconds) - DateTimeOffset.UtcNow;
            if (left.TotalHours >= 24) return (int)left.TotalDays + "d " + left.Hours + "h";
            return Math.Max(0, (int)left.TotalHours) + "h " + Math.Max(0, left.Minutes) + "m";
        }

        static string Ago(long unixSeconds)
        {
            TimeSpan ago = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            if (ago.TotalMinutes < 1) return "just now";
            if (ago.TotalHours < 1) return (int)ago.TotalMinutes + " min ago";
            if (ago.TotalDays < 1) return (int)ago.TotalHours + " h ago";
            return (int)ago.TotalDays + " d ago";
        }
    }
}
