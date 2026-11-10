using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Pulsebreak.Client.Player;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.UI
{
    /// <summary>
    /// The armory in the pause menu: pick the weapon for each slot, fit attachments, and see
    /// what they do to the numbers. Every change is applied to the player straight away, so
    /// closing the menu is all it takes to try a build.
    /// </summary>
    public sealed class ArmoryPanel
    {
        static readonly string[] SlotTitles = { "PRIMARY", "SECONDARY", "MELEE" };
        static readonly string[] BarTitles = { "Damage", "Fire rate", "Range", "Accuracy", "Recoil control", "Mobility", "Handling" };

        struct Bar
        {
            public VisualElement Base;
            public VisualElement Change;
        }

        readonly WeaponCatalog _catalog;
        readonly LocalPlayerController _player;

        readonly Button[] _tabs;
        readonly ScrollView _weaponList;
        readonly Label _name, _class, _description, _numbers;
        readonly VisualElement _attachments;
        readonly Bar[] _bars = new Bar[BarTitles.Length];

        int _slot;

        public ArmoryPanel(VisualElement root, WeaponCatalog catalog, LocalPlayerController player)
        {
            _catalog = catalog;
            _player = player;

            _weaponList = root.Q<ScrollView>("armory-weapons");
            _name = root.Q<Label>("armory-name");
            _class = root.Q<Label>("armory-class");
            _description = root.Q<Label>("armory-description");
            _numbers = root.Q<Label>("armory-numbers");
            _attachments = root.Q<VisualElement>("armory-attachments");

            VisualElement tabRow = root.Q<VisualElement>("armory-tabs");
            int slotCount = Mathf.Min(SlotTitles.Length, player.SlotCount);
            _tabs = new Button[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                int slot = i;
                var tab = new Button(() => SelectSlot(slot)) { text = SlotTitles[i] };
                tab.AddToClassList("armory-tab");
                tabRow.Add(tab);
                _tabs[i] = tab;
            }

            VisualElement barList = root.Q<VisualElement>("armory-bars");
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

                barList.Add(row);
            }
        }

        /// <summary>Redraws everything from the player's current loadout.</summary>
        public void Refresh()
        {
            _slot = Mathf.Clamp(_slot, 0, _tabs.Length - 1);
            for (int i = 0; i < _tabs.Length; i++) _tabs[i].EnableInClassList("armory-tab--active", i == _slot);

            RebuildWeaponList();
            RefreshDetail();
        }

        void SelectSlot(int slot)
        {
            _slot = slot;
            Refresh();
        }

        void SelectWeapon(WeaponDefinition weapon)
        {
            WeaponBuild current = _player.GetBuild(_slot);
            if (current != null && current.Weapon == weapon) return;
            _player.SetBuild(_slot, new WeaponBuild(weapon));
            Refresh();
        }

        void CycleAttachment(AttachmentSlot slot, int step)
        {
            WeaponBuild build = _player.GetBuild(_slot);
            if (build == null) return;
            List<AttachmentDefinition> options = build.OptionsFor(slot);

            // Positions run: none, first option, second option... and wrap around.
            int positions = options.Count + 1;
            int current = options.IndexOf(build.Get(slot)) + 1;
            int next = ((current + step) % positions + positions) % positions;

            if (next == 0) build.Clear(slot);
            else build.Fit(options[next - 1]);

            _player.SetBuild(_slot, build);
            RefreshDetail();
        }

        void RebuildWeaponList()
        {
            _weaponList.Clear();
            WeaponBuild current = _player.GetBuild(_slot);
            WeaponDefinition equipped = current != null ? current.Weapon : null;
            var slotType = (WeaponSlotType)_slot;
            bool first = true;
            WeaponClass group = default;

            foreach (WeaponDefinition weapon in _catalog.weapons)
            {
                if (weapon == null || weapon.slot != slotType) continue;

                if (first || weapon.weaponClass != group)
                {
                    group = weapon.weaponClass;
                    first = false;
                    var header = new Label(ClassTitle(group));
                    header.AddToClassList("armory-group");
                    _weaponList.Add(header);
                }

                WeaponDefinition choice = weapon;
                var button = new Button(() => SelectWeapon(choice)) { text = weapon.displayName };
                button.AddToClassList("armory-weapon");
                button.EnableInClassList("armory-weapon--active", weapon == equipped);
                _weaponList.Add(button);
            }
        }

        void RefreshDetail()
        {
            WeaponBuild build = _player.GetBuild(_slot);
            _attachments.Clear();
            if (build == null)
            {
                _name.text = "Empty";
                _class.text = string.Empty;
                _description.text = "Nothing is carried in this slot.";
                _numbers.text = string.Empty;
                for (int i = 0; i < _bars.Length; i++) SetBar(i, 0f, 0f);
                return;
            }

            WeaponDefinition weapon = build.Weapon;
            _name.text = weapon.displayName;
            _class.text = ClassTitle(weapon.weaponClass);
            _description.text = weapon.description;

            foreach (AttachmentSlot slot in Enum.GetValues(typeof(AttachmentSlot)))
            {
                if (build.OptionsFor(slot).Count == 0) continue;
                _attachments.Add(BuildAttachmentRow(build, slot));
            }

            WeaponStats stats = build.ResolveStats();
            WeaponRatings now = WeaponRatings.From(stats);
            WeaponRatings stock = WeaponRatings.From(weapon.stats);

            SetBar(0, stock.Damage, now.Damage);
            SetBar(1, stock.FireRate, now.FireRate);
            SetBar(2, stock.Range, now.Range);
            SetBar(3, stock.Accuracy, now.Accuracy);
            SetBar(4, stock.Control, now.Control);
            SetBar(5, stock.Mobility, now.Mobility);
            SetBar(6, stock.Handling, now.Handling);

            _numbers.text = Summarise(stats, now);
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

        /// <summary>Shows the stock value in white and what attachments added (green) or cost (red).</summary>
        void SetBar(int index, float stock, float now)
        {
            float low = Mathf.Min(stock, now), high = Mathf.Max(stock, now);
            _bars[index].Base.style.width = Percent(low);

            VisualElement change = _bars[index].Change;
            change.style.left = Percent(low);
            change.style.width = Percent(high - low);
            change.EnableInClassList("bar-change--better", now > stock + 0.0005f);
            change.EnableInClassList("bar-change--worse", now < stock - 0.0005f);
        }

        static Length Percent(float fraction) => new Length(Mathf.Clamp01(fraction) * 100f, LengthUnit.Percent);

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
                    if (modifier.op == ModifierOp.Multiply)
                    {
                        int percent = Mathf.RoundToInt((modifier.value - 1f) * 100f);
                        text.Append(percent >= 0 ? "+" : "").Append(percent).Append("% ");
                    }
                    else
                    {
                        text.Append(modifier.value >= 0f ? "+" : "").Append(modifier.value.ToString("0.##")).Append(' ');
                    }
                    text.Append(StatTitle(modifier.stat));
                }
            }
            return text.ToString();
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
