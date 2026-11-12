using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Pulsebreak.Client.Loadouts;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.UI
{
    /// <summary>
    /// The loadout screen in the pause menu. Pick a loadout from the list and it is equipped;
    /// change anything in it and it is saved. Presets cannot be changed, so editing one
    /// quietly makes a personal copy and edits that instead.
    /// </summary>
    public sealed class LoadoutPanel
    {
        static readonly string[] TabTitles = { "PRIMARY", "SECONDARY", "MELEE", "UTILITY", "PERKS" };
        static readonly string[] BarTitles = { "Damage", "Fire rate", "Range", "Accuracy", "Recoil control", "Mobility", "Handling" };
        const int FirstUtilityTab = Loadout.WeaponSlots;
        const int PerkTab = Loadout.WeaponSlots + Loadout.UtilitySlots;

        struct Bar
        {
            public VisualElement Base;
            public VisualElement Change;
        }

        readonly LoadoutLibrary _library;
        readonly LoadoutCatalog _catalog;

        readonly ScrollView _loadoutList, _itemList;
        readonly TextField _nameField;
        readonly Label _note, _limit, _name, _class, _description, _numbers;
        readonly Button _newButton, _deleteButton;
        readonly Button[] _tabs = new Button[TabTitles.Length];
        readonly VisualElement _attachments, _barList;
        readonly Bar[] _bars = new Bar[BarTitles.Length];

        int _tab;
        bool _batching;
        // Bars compare a weapon with the others that fit the same slot; one scale per slot.
        readonly WeaponRatingScale[] _scales = new WeaponRatingScale[Loadout.WeaponSlots];
        readonly VisualElement[] _barRows = new VisualElement[BarTitles.Length];
        readonly Button _deployButton;

        /// <summary>The player pressed Deploy: the selected loadout is equipped and they want to get back to playing.</summary>
        public event Action DeployRequested;

        public LoadoutPanel(VisualElement root, LoadoutLibrary library)
        {
            _library = library;
            _catalog = library.Catalog;

            _loadoutList = root.Q<ScrollView>("loadout-list");
            _itemList = root.Q<ScrollView>("loadout-items");
            _nameField = root.Q<TextField>("loadout-name");
            _note = root.Q<Label>("loadout-note");
            _limit = root.Q<Label>("loadout-limit");
            _name = root.Q<Label>("detail-name");
            _class = root.Q<Label>("detail-class");
            _description = root.Q<Label>("detail-description");
            _numbers = root.Q<Label>("detail-numbers");
            _attachments = root.Q<VisualElement>("detail-attachments");
            _barList = root.Q<VisualElement>("detail-bars");
            _newButton = root.Q<Button>("loadout-new");
            _deleteButton = root.Q<Button>("loadout-delete");

            _nameField.RegisterValueChangedCallback(e => _library.Rename(_library.EquippedId, e.newValue));
            _newButton.clicked += CreateCopy;
            _deleteButton.clicked += DeleteEquipped;
            _deployButton = root.Q<Button>("loadout-deploy");
            if (_deployButton != null) _deployButton.clicked += () => DeployRequested?.Invoke();

            VisualElement tabRow = root.Q<VisualElement>("loadout-tabs");
            for (int i = 0; i < TabTitles.Length; i++)
            {
                int tab = i;
                var button = new Button(() => SelectTab(tab)) { text = TabTitles[i] };
                button.AddToClassList("armory-tab");
                tabRow.Add(button);
                _tabs[i] = button;
            }

            for (int i = 0; i < BarTitles.Length; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("bar-row");

                var label = new Label(BarTitles[i]);
                label.AddToClassList("bar-label");
                row.Add(label);

                var track = new VisualElement();
                track.AddToClassList("bar-track");
                _bars[i].Base = new VisualElement();
                _bars[i].Base.AddToClassList("bar-base");
                _bars[i].Change = new VisualElement();
                _bars[i].Change.AddToClassList("bar-change");
                track.Add(_bars[i].Base);
                track.Add(_bars[i].Change);
                row.Add(track);

                _barList.Add(row);
                _barRows[i] = row;
            }

            _library.Changed += Refresh;
        }

        /// <summary>Stops listening to the library. Call when the screen this belongs to goes away.</summary>
        public void Dispose() => _library.Changed -= Refresh;

        /// <summary>Redraws everything from the library.</summary>
        public void Refresh()
        {
            if (_batching) return;

            // Only the player's own loadouts are listed, so one of them is always the one equipped.
            if (_library.EnsureOwnLoadout()) return;       // it said it changed, which redraws this

            LoadoutData equipped = _library.Equipped;
            if (equipped == null) return;
            bool preset = _library.IsPreset(equipped.id);

            RebuildLoadoutList(equipped);

            _nameField.SetValueWithoutNotify(equipped.name);
            _nameField.SetEnabled(!preset);
            _note.text = "Changes are saved as you make them. New makes a copy of this one to change.";

            _newButton.SetEnabled(_library.CanCreate);
            // There is always at least one loadout to carry.
            _deleteButton.SetEnabled(!preset && _library.Custom.Count > 1);
            _limit.text = _library.Custom.Count + " of " + LoadoutLibrary.MaxCustomLoadouts + " custom loadouts";

            for (int i = 0; i < _tabs.Length; i++) _tabs[i].EnableInClassList("armory-tab--active", i == _tab);

            Loadout loadout = LoadoutRules.Resolve(equipped, _catalog);
            RebuildItemList(loadout);
            RefreshDetail(loadout);
        }

        // ------------------------------------------------------------------ actions

        void SelectTab(int tab)
        {
            _tab = tab;
            Refresh();
        }

        /// <summary>Deletes the equipped loadout, equipping the one next to it first.</summary>
        void DeleteEquipped()
        {
            IReadOnlyList<LoadoutData> mine = _library.Custom;
            if (mine.Count <= 1) return;
            string doomed = _library.EquippedId;
            int index = 0;
            for (int i = 0; i < mine.Count; i++)
            {
                if (mine[i].id == doomed) index = i;
            }
            _library.Equip(mine[index > 0 ? index - 1 : 1].id);
            _library.Delete(doomed);
        }

        void CreateCopy()
        {
            LoadoutData copy = _library.CreateFrom(_library.EquippedId);
            if (copy != null) _library.Equip(copy.id);
        }

        /// <summary>
        /// Runs an edit against the equipped loadout and saves it. If the equipped loadout is
        /// a preset, a copy is made and equipped first, and the edit goes to the copy.
        /// </summary>
        void Edit(Action<LoadoutData> change)
        {
            _batching = true;
            try
            {
                LoadoutData current = _library.Equipped;
                if (_library.IsPreset(current.id))
                {
                    current = _library.CreateFrom(current.id);
                    if (current == null) return; // No room for a copy; the note already says so.
                    _library.Equip(current.id);
                }

                LoadoutData edited = current.Clone();
                change(edited);
                _library.Replace(edited);
            }
            finally
            {
                _batching = false;
                Refresh();
            }
        }

        void ChooseWeapon(WeaponDefinition weapon)
        {
            int slot = _tab;
            if (_library.Equipped.GetWeapon(slot).weaponId == weapon.weaponId) return;
            Edit(data =>
            {
                WeaponBuildData build = data.GetWeapon(slot);
                build.weaponId = weapon.weaponId;
                build.attachments = new string[0];
            });
        }

        void ChooseUtility(UtilityDefinition utility)
        {
            if (_library.Equipped.utility == utility.utilityId) return;
            Edit(data => data.utility = utility.utilityId);
        }

        void ChoosePerkSet(PerkSetDefinition perkSet)
        {
            if (_library.Equipped.perkSet == perkSet.perkSetId) return;
            Edit(data => data.perkSet = perkSet.perkSetId);
        }

        void CycleAttachment(AttachmentSlot attachmentSlot, int step)
        {
            int slot = _tab;
            Edit(data =>
            {
                WeaponBuild build = LoadoutRules.Resolve(data, _catalog).Weapons[slot];
                if (build == null) return;
                List<AttachmentDefinition> options = build.OptionsFor(attachmentSlot);

                // Positions run: none, first option, second option... and wrap around.
                int positions = options.Count + 1;
                int position = options.IndexOf(build.Get(attachmentSlot)) + 1;
                int next = ((position + step) % positions + positions) % positions;

                if (next == 0) build.Clear(attachmentSlot);
                else build.Fit(options[next - 1]);
                data.GetWeapon(slot).attachments = build.FittedIds();
            });
        }

        // ------------------------------------------------------------------ lists

        void RebuildLoadoutList(LoadoutData equipped)
        {
            _loadoutList.Clear();
            AddGroupHeader(_loadoutList, "YOUR LOADOUTS");
            foreach (LoadoutData custom in _library.Custom) AddLoadoutButton(custom, equipped);
        }

        void AddLoadoutButton(LoadoutData loadout, LoadoutData equipped)
        {
            string id = loadout.id;
            var button = new Button(() => _library.Equip(id)) { text = "" };
            button.AddToClassList("armory-weapon");
            button.AddToClassList("loadout-entry");
            button.EnableInClassList("armory-weapon--active", loadout.id == equipped.id);

            // The name, and underneath it what the loadout carries, so loadouts can be told apart without opening each.
            var name = new Label(loadout.name);
            name.AddToClassList("loadout-entry-name");
            var contents = new Label(Contents(loadout));
            contents.AddToClassList("loadout-entry-contents");
            button.Add(name);
            button.Add(contents);
            _loadoutList.Add(button);
        }

        /// <summary>"Assault Rifle · Handgun · Knife · Frag Grenade".</summary>
        string Contents(LoadoutData data)
        {
            Loadout loadout = LoadoutRules.Resolve(data, _catalog);
            var parts = new List<string>(4);
            foreach (WeaponBuild build in loadout.Weapons)
            {
                if (build != null) parts.Add(build.Weapon.displayName);
            }
            if (loadout.Utilities[0] != null) parts.Add(loadout.Utilities[0].displayName);
            return string.Join("  ·  ", parts);
        }

        static void AddGroupHeader(VisualElement list, string title)
        {
            var header = new Label(title);
            header.AddToClassList("armory-group");
            list.Add(header);
        }

        void RebuildItemList(Loadout loadout)
        {
            _itemList.Clear();

            if (_tab < Loadout.WeaponSlots)
            {
                WeaponDefinition equipped = loadout.Weapons[_tab] != null ? loadout.Weapons[_tab].Weapon : null;
                var slotType = (WeaponSlotType)_tab;
                bool first = true;
                WeaponClass group = default;

                foreach (WeaponDefinition weapon in _catalog.weapons.weapons)
                {
                    if (weapon == null || weapon.slot != slotType) continue;
                    if (first || weapon.weaponClass != group)
                    {
                        group = weapon.weaponClass;
                        first = false;
                        AddGroupHeader(_itemList, ClassTitle(group));
                    }
                    WeaponDefinition choice = weapon;
                    AddItemButton(weapon.displayName, weapon == equipped, () => ChooseWeapon(choice));
                }
            }
            else if (_tab < PerkTab)
            {
                UtilityDefinition equipped = loadout.Utilities[_tab - FirstUtilityTab];
                foreach (UtilityDefinition utility in _catalog.utilities)
                {
                    if (utility == null) continue;
                    UtilityDefinition choice = utility;
                    AddItemButton(utility.displayName, utility == equipped, () => ChooseUtility(choice));
                }
            }
            else
            {
                foreach (PerkSetDefinition perkSet in _catalog.perkSets)
                {
                    if (perkSet == null) continue;
                    PerkSetDefinition choice = perkSet;
                    AddItemButton(perkSet.displayName, perkSet == loadout.PerkSet, () => ChoosePerkSet(choice));
                }
            }
        }

        void AddItemButton(string title, bool active, Action onClick)
        {
            var button = new Button(onClick) { text = title };
            button.AddToClassList("armory-weapon");
            button.EnableInClassList("armory-weapon--active", active);
            _itemList.Add(button);
        }

        // ------------------------------------------------------------------ detail

        void RefreshDetail(Loadout loadout)
        {
            _attachments.Clear();

            if (_tab < Loadout.WeaponSlots) ShowWeapon(loadout, _tab);
            else if (_tab < PerkTab) ShowUtility(loadout, _tab - FirstUtilityTab);
            else ShowPerkSet(loadout.PerkSet);
        }

        void ShowWeapon(Loadout loadout, int slot)
        {
            WeaponBuild build = loadout.Weapons[slot];
            if (build == null)
            {
                ShowText("Empty", string.Empty, "Nothing is carried in this slot.", string.Empty);
                return;
            }

            WeaponDefinition weapon = build.Weapon;
            _name.text = weapon.displayName;
            _class.text = ClassTitle(weapon.weaponClass);
            _description.text = weapon.description;

            foreach (AttachmentSlot attachmentSlot in Enum.GetValues(typeof(AttachmentSlot)))
            {
                if (build.OptionsFor(attachmentSlot).Count == 0) continue;
                _attachments.Add(BuildAttachmentRow(build, attachmentSlot));
            }

            // The bars compare the bare weapon with what this loadout makes of it:
            // attachments and the perk set together.
            WeaponStats stats = loadout.ResolveStats(slot);
            WeaponRatings now = WeaponRatings.From(stats);
            WeaponRatings stock = WeaponRatings.From(weapon.stats);
            WeaponRatingScale scale = ScaleFor(slot);

            _barList.style.display = DisplayStyle.Flex;
            for (int i = 0; i < BarTitles.Length; i++)
            {
                bool meaningful = scale.Meaningful(i);
                _barRows[i].style.display = meaningful ? DisplayStyle.Flex : DisplayStyle.None;
                if (meaningful) SetBar(i, scale.Bar(stock, i), scale.Bar(now, i));
            }

            _numbers.text = Summarise(stats, now);
        }

        void ShowUtility(Loadout loadout, int slot)
        {
            UtilityDefinition utility = loadout.Utilities[slot];
            if (utility == null)
            {
                ShowText("Empty", string.Empty, "Nothing is carried in this slot.", string.Empty);
                return;
            }

            WeaponStats s = utility.projectile;
            var text = new StringBuilder();
            text.Append("Carried  ").Append(loadout.UtilityCharges(slot));
            text.Append("     Damage  ").Append(Number(s.damage));
            if (s.explosionRadius > 0f) text.Append("     Blast radius  ").Append(Number(s.explosionRadius)).Append(" m");
            text.Append('\n');
            text.Append(s.fuseTime > 0f ? "Fuse  " + s.fuseTime.ToString("0.0") + " s" : s.explosionRadius > 0f ? "Bursts on contact" : "No blast");
            if (s.bounces > 0) text.Append("     Bounces");
            if (utility.rechargeTime > 0f) text.Append("\nOn the range, one comes back every ").Append(Number(utility.rechargeTime)).Append(" s");

            ShowText(utility.displayName, "UTILITY", utility.description, text.ToString());
        }

        void ShowPerkSet(PerkSetDefinition perkSet)
        {
            if (perkSet == null)
            {
                ShowText("None", string.Empty, "No perk set.", string.Empty);
                return;
            }

            var text = new StringBuilder();
            void Line(string line)
            {
                if (text.Length > 0) text.Append('\n');
                text.Append(line);
            }

            if (perkSet.weaponModifiers != null)
            {
                foreach (StatModifier modifier in perkSet.weaponModifiers) Line(Describe(modifier) + " on every weapon");
            }
            if (Mathf.Abs(perkSet.moveSpeedMultiplier - 1f) > 0.0005f) Line(Percent(perkSet.moveSpeedMultiplier) + " movement speed");
            if (perkSet.bonusUtilityCharges != 0) Line("+" + perkSet.bonusUtilityCharges + " of each utility item");
            if (Mathf.Abs(perkSet.utilityRechargeMultiplier - 1f) > 0.0005f) Line(Percent(perkSet.utilityRechargeMultiplier) + " utility recharge speed");

            ShowText(perkSet.displayName, "PERK SET", perkSet.description, text.ToString());
        }

        void ShowText(string name, string kind, string description, string numbers)
        {
            _name.text = name;
            _class.text = kind;
            _description.text = description;
            _numbers.text = numbers;
            _barList.style.display = DisplayStyle.None;
        }

        VisualElement BuildAttachmentRow(WeaponBuild build, AttachmentSlot slot)
        {
            AttachmentDefinition fitted = build.Get(slot);

            var row = new VisualElement();
            row.AddToClassList("attachment-row");

            var title = new Label(slot.ToString().ToUpperInvariant());
            title.AddToClassList("attachment-slot");
            row.Add(title);

            var previous = new Button(() => CycleAttachment(slot, -1)) { text = "<" };
            previous.AddToClassList("attachment-step");
            row.Add(previous);

            var name = new Label(fitted != null ? fitted.displayName : "None");
            name.AddToClassList("attachment-name");
            row.Add(name);

            var next = new Button(() => CycleAttachment(slot, 1)) { text = ">" };
            next.AddToClassList("attachment-step");
            row.Add(next);

            var effect = new Label(Describe(fitted));
            effect.AddToClassList("attachment-effect");
            row.Add(effect);

            return row;
        }

        /// <summary>The scale for the bars of weapons that go in a slot, worked out the first time it is needed.</summary>
        WeaponRatingScale ScaleFor(int slot)
        {
            if (_scales[slot] != null) return _scales[slot];
            var stats = new List<WeaponStats>();
            foreach (WeaponDefinition weapon in _catalog.weapons.weapons)
            {
                if (weapon != null && (int)weapon.slot == slot) stats.Add(weapon.stats);
            }
            _scales[slot] = WeaponRatingScale.From(stats);
            return _scales[slot];
        }

        /// <summary>Shows the stock value in white and what the loadout added (green) or cost (red).</summary>
        void SetBar(int index, float stock, float now)
        {
            float low = Mathf.Min(stock, now), high = Mathf.Max(stock, now);
            _bars[index].Base.style.width = Width(low);

            VisualElement change = _bars[index].Change;
            change.style.left = Width(low);
            change.style.width = Width(high - low);
            change.EnableInClassList("bar-change--better", now > stock + 0.0005f);
            change.EnableInClassList("bar-change--worse", now < stock - 0.0005f);
        }

        static Length Width(float fraction) => new Length(Mathf.Clamp01(fraction) * 100f, LengthUnit.Percent);

        // ------------------------------------------------------------------ text

        static string Summarise(WeaponStats s, WeaponRatings r)
        {
            var text = new StringBuilder();

            if (s.ballistics == BallisticsType.Melee)
            {
                text.Append("Damage  ").Append(Number(s.damage));
                text.Append("     Reach  ").Append(Number(s.meleeRange)).Append(" m");
                text.Append("\nSwings per minute  ").Append(Number(s.roundsPerMinute));
            }
            else
            {
                text.Append("Damage  ").Append(Number(s.damage));
                if (s.pellets > 1) text.Append(" x ").Append(s.pellets);
                if (s.explosionRadius > 0f) text.Append("     Blast radius  ").Append(Number(s.explosionRadius)).Append(" m");
                else text.Append("     Head  ").Append(Number(s.damage * s.headMultiplier));

                text.Append("\nFire rate  ").Append(Mathf.RoundToInt(r.EffectiveRpm)).Append(" rpm");
                text.Append("     Magazine  ").Append(s.magazineSize);
                text.Append("     Reload  ").Append(s.reloadTime.ToString("0.00")).Append(" s");
                if (s.reloadStyle == ReloadStyle.PerRound) text.Append(" each");

                text.Append("\nAim time  ").Append(s.adsTime.ToString("0.00")).Append(" s");
                text.Append("     Zoom  ").Append(s.adsZoom.ToString("0.##")).Append("x");
                if (s.ballistics == BallisticsType.Projectile)
                    text.Append("     Velocity  ").Append(Mathf.RoundToInt(s.projectileSpeed)).Append(" m/s");
                else
                    text.Append("     Range  ").Append(Mathf.RoundToInt(s.falloffStart)).Append("-").Append(Mathf.RoundToInt(s.falloffEnd)).Append(" m");
            }

            text.Append("\nTime to kill  ");
            if (r.ShotsToKill <= 1) text.Append("one hit");
            else text.Append(r.TimeToKill.ToString("0.00")).Append(" s (").Append(r.ShotsToKill).Append(" body shots)");

            return text.ToString();
        }

        static string Number(float value) => value.ToString(value >= 10f ? "0" : "0.#");

        static string Percent(float multiplier)
        {
            int percent = Mathf.RoundToInt((multiplier - 1f) * 100f);
            return (percent >= 0 ? "+" : "") + percent + "%";
        }

        /// <summary>Spells out an attachment's effects, e.g. "+33% magazine, +10% reload time".</summary>
        static string Describe(AttachmentDefinition attachment)
        {
            if (attachment == null) return string.Empty;

            var text = new StringBuilder();
            if (attachment.sightZoom > 0f) text.Append(attachment.sightZoom.ToString("0.##")).Append("x zoom");

            if (attachment.modifiers != null)
            {
                foreach (StatModifier modifier in attachment.modifiers)
                {
                    if (text.Length > 0) text.Append(", ");
                    text.Append(Describe(modifier));
                }
            }
            return text.ToString();
        }

        static string Describe(StatModifier modifier)
        {
            string amount = modifier.op == ModifierOp.Multiply
                ? Percent(modifier.value)
                : (modifier.value >= 0f ? "+" : "") + modifier.value.ToString("0.##");
            return amount + " " + StatTitle(modifier.stat);
        }

        static string StatTitle(WeaponStat stat)
        {
            switch (stat)
            {
                case WeaponStat.Damage: return "damage";
                case WeaponStat.FireRate: return "fire rate";
                case WeaponStat.Range: return "range";
                case WeaponStat.MagazineSize: return "magazine";
                case WeaponStat.ReloadTime: return "reload time";
                case WeaponStat.AdsTime: return "aim time";
                case WeaponStat.MoveSpeed: return "move speed";
                case WeaponStat.AdsMoveSpeed: return "aiming move speed";
                case WeaponStat.SprintToFireTime: return "sprint-out time";
                case WeaponStat.SwapTime: return "swap time";
                case WeaponStat.HipSpread: return "hip spread";
                case WeaponStat.AdsSpread: return "aimed spread";
                case WeaponStat.Bloom: return "bloom";
                case WeaponStat.RecoilVertical: return "vertical recoil";
                case WeaponStat.RecoilHorizontal: return "sideways recoil";
                case WeaponStat.RecoilRecovery: return "recoil recovery";
                case WeaponStat.ProjectileSpeed: return "projectile speed";
                default: return stat.ToString();
            }
        }

        static string ClassTitle(WeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case WeaponClass.AssaultRifle: return "ASSAULT RIFLES";
                case WeaponClass.Smg: return "SUBMACHINE GUNS";
                case WeaponClass.Lmg: return "LIGHT MACHINE GUNS";
                case WeaponClass.Shotgun: return "SHOTGUNS";
                case WeaponClass.Sniper: return "SNIPER RIFLES";
                case WeaponClass.Marksman: return "MARKSMAN RIFLES";
                case WeaponClass.Pistol: return "PISTOLS";
                case WeaponClass.Launcher: return "LAUNCHERS";
                case WeaponClass.Special: return "SPECIAL";
                default: return "MELEE";
            }
        }
    }
}
