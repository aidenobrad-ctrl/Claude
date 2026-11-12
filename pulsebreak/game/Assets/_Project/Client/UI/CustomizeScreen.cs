using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Pulsebreak.Client.Cosmetics;
using Pulsebreak.Client.Loadouts;
using Pulsebreak.Client.Visuals;
using Pulsebreak.Shared.Cosmetics;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.UI
{
    /// <summary>
    /// Where a player chooses how they look: suit and trim colours, a pattern, headgear, a
    /// visor, something on their back, and a skin for each weapon (or one for all of them).
    /// A live preview in the middle shows the result; drag it to turn the character round.
    ///
    /// Every item is free to pick. Choices are saved as they are made, and in a match everyone
    /// sees the new look straight away. Nothing here changes how anyone plays.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CustomizeScreen : MonoBehaviour
    {
        const int PreviewWidth = 460, PreviewHeight = 600;

        static readonly Color Accent = new Color32(38, 216, 255, 255);
        static readonly Color Text = new Color32(240, 244, 250, 255);
        static readonly Color Dim = new Color(0.94f, 0.96f, 0.98f, 0.62f);
        static readonly Color Card = new Color(0.07f, 0.09f, 0.125f, 1f);
        static readonly Color Tile = new Color(0.12f, 0.15f, 0.2f, 1f);

        static readonly (CosmeticSlot slot, string title)[] Categories =
        {
            (CosmeticSlot.SuitColor, "SUIT"),
            (CosmeticSlot.TrimColor, "TRIM"),
            (CosmeticSlot.Pattern, "PATTERN"),
            (CosmeticSlot.Headgear, "HEADGEAR"),
            (CosmeticSlot.Visor, "VISOR"),
            (CosmeticSlot.Back, "BACK"),
            (CosmeticSlot.WeaponSkin, "WEAPON SKINS"),
        };

        static CustomizeScreen _open;

        CosmeticsLibrary _cosmetics;
        LoadoutLibrary _loadouts;
        CharacterPreview _preview;
        VisualElement _items, _categoryList, _weaponRow;
        Label _heading, _caption;
        DropdownField _weaponField;
        readonly List<Button> _categoryButtons = new List<Button>();
        readonly List<WeaponDefinition> _weapons = new List<WeaponDefinition>();
        int _category;
        bool _dragging;
        float _dragX;

        /// <summary>The screen is showing. Other screens leave Esc to it meanwhile.</summary>
        public static bool IsOpen => _open != null;

        /// <summary>Raised once the screen has closed.</summary>
        public event Action Closed;

        /// <param name="loadouts">For the weapon to show in the character's hands, and the list of weapons to skin.</param>
        /// <param name="movement">For the character's proportions. Null for the defaults.</param>
        public static CustomizeScreen Open(CosmeticsLibrary cosmetics, LoadoutLibrary loadouts, MovementConfig movement = null)
        {
            if (_open != null) return _open;
            if (cosmetics == null)
            {
                Debug.LogWarning("Pulsebreak: customizing needs the game's services; there is no cosmetics library.");
                return null;
            }

            var go = new GameObject("Customize Screen");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = HudController.LoadPanel();
            document.sortingOrder = 40f;

            var screen = go.AddComponent<CustomizeScreen>();
            screen._cosmetics = cosmetics;
            screen._loadouts = loadouts;
            screen._preview = CharacterPreview.Create(movement, PreviewWidth, PreviewHeight);
            screen.Build(document.rootVisualElement);
            _open = screen;
            return screen;
        }

        public void Close()
        {
            if (this == null) return;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_cosmetics != null) _cosmetics.Changed -= Refresh;
            if (_preview != null) Destroy(_preview.gameObject);
            if (_open == this) _open = null;
            Closed?.Invoke();
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Close();
        }

        // ------------------------------------------------------------------ construction

        void Build(VisualElement root)
        {
            var styles = Resources.Load<StyleSheet>("UI/HudStyles");
            if (styles != null) root.styleSheets.Add(styles);
            root.AddToClassList("hud");
            Fill(root);
            root.style.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 0.88f);
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.color = Text;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;

            var card = new VisualElement();
            card.AddToClassList("pause-card");
            card.style.width = 1240;
            card.style.height = 780;
            card.style.backgroundColor = Card;
            card.style.paddingLeft = card.style.paddingRight = 28;
            card.style.paddingTop = card.style.paddingBottom = 22;
            root.Add(card);
            UiMotion.Enter(card);
            Audio.UiSounds.Attach(root);

            var header = Row(card);
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            var title = new Label("CUSTOMIZE");
            title.AddToClassList("pause-title");
            title.style.marginBottom = 0;
            header.Add(title);
            var free = new Label("Everything is free to pick. Looks never change how anyone plays.");
            free.style.color = Dim;
            free.style.fontSize = 13;
            header.Add(free);

            var body = Row(card);
            body.style.flexGrow = 1;
            body.style.marginTop = 16;

            // Categories down the left.
            _categoryList = new VisualElement();
            _categoryList.style.width = 200;
            _categoryList.style.marginRight = 18;
            body.Add(_categoryList);
            for (int i = 0; i < Categories.Length; i++)
            {
                int index = i;
                var button = new Button(() => SelectCategory(index)) { text = Categories[i].title };
                button.AddToClassList("armory-tab");
                button.style.height = 46;
                button.style.marginBottom = 6;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                button.style.paddingLeft = 14;
                _categoryList.Add(button);
                _categoryButtons.Add(button);
            }

            // The character in the middle.
            var stage = new VisualElement();
            stage.style.width = PreviewWidth;
            stage.style.height = PreviewHeight;
            stage.style.marginRight = 18;
            stage.style.backgroundImage = Background.FromRenderTexture(_preview.Texture);
            Round(stage, 8);
            stage.RegisterCallback<PointerDownEvent>(e =>
            {
                _dragging = true;
                _dragX = e.position.x;
                stage.CapturePointer(e.pointerId);
            });
            stage.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_dragging) return;
                _preview.Turn(-(e.position.x - _dragX) * 0.6f);
                _dragX = e.position.x;
            });
            stage.RegisterCallback<PointerUpEvent>(e =>
            {
                _dragging = false;
                stage.ReleasePointer(e.pointerId);
            });
            var hint = new Label("Drag to turn");
            hint.style.position = Position.Absolute;
            hint.style.bottom = 10;
            hint.style.left = 0;
            hint.style.right = 0;
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            hint.style.color = Dim;
            hint.style.fontSize = 12;
            hint.pickingMode = PickingMode.Ignore;
            stage.Add(hint);
            body.Add(stage);

            // The choices on the right.
            var right = new VisualElement();
            right.style.flexGrow = 1;
            body.Add(right);
            _heading = new Label();
            _heading.style.fontSize = 18;
            _heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            _heading.style.letterSpacing = 3;
            right.Add(_heading);
            _caption = new Label();
            _caption.style.color = Dim;
            _caption.style.fontSize = 13;
            _caption.style.whiteSpace = WhiteSpace.Normal;
            _caption.style.marginBottom = 8;
            right.Add(_caption);

            _weaponRow = Row(right);
            _weaponRow.style.alignItems = Align.Center;
            _weaponRow.style.marginBottom = 8;
            BuildWeaponChoice();

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            right.Add(scroll);
            _items = new VisualElement();
            _items.style.flexDirection = FlexDirection.Row;
            _items.style.flexWrap = Wrap.Wrap;
            scroll.Add(_items);

            var buttons = Row(card);
            buttons.AddToClassList("pause-buttons");
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 14;
            buttons.Add(Button("Randomize", false, Randomize));
            buttons.Add(Button("Reset to default", false, () => _cosmetics.Set(new CharacterLook())));
            buttons.Add(Button("Done", true, Close));

            _cosmetics.Changed += Refresh;
            SelectCategory(0);
        }

        /// <summary>The weapon a skin goes on: every weapon, or one in particular.</summary>
        void BuildWeaponChoice()
        {
            _weapons.Clear();
            var names = new List<string> { "Every weapon" };
            if (_loadouts != null && _loadouts.Catalog != null && _loadouts.Catalog.weapons != null)
            {
                var all = new List<WeaponDefinition>(_loadouts.Catalog.weapons.weapons);
                all.RemoveAll(w => w == null);
                // The weapons being carried first, then everything else by slot and name.
                LoadoutData equipped = _loadouts.Equipped;
                int Rank(WeaponDefinition w)
                {
                    if (equipped == null) return 3;
                    for (int slot = 0; slot < Loadout.WeaponSlots; slot++)
                    {
                        if (equipped.GetWeapon(slot).weaponId == w.weaponId) return slot - 10;
                    }
                    return (int)w.slot;
                }
                all.Sort((a, b) => Rank(a) != Rank(b) ? Rank(a).CompareTo(Rank(b)) : string.CompareOrdinal(a.displayName, b.displayName));
                foreach (WeaponDefinition weapon in all)
                {
                    _weapons.Add(weapon);
                    names.Add(weapon.displayName);
                }
            }

            var label = new Label("Skin for");
            label.style.marginRight = 10;
            label.style.fontSize = 15;
            _weaponRow.Add(label);
            _weaponField = new DropdownField(names, 0);
            _weaponField.style.width = 320;
            _weaponField.RegisterValueChangedCallback(_ => Refresh());
            _weaponRow.Add(_weaponField);
        }

        // ------------------------------------------------------------------ choosing

        void SelectCategory(int index)
        {
            _category = Mathf.Clamp(index, 0, Categories.Length - 1);
            for (int i = 0; i < _categoryButtons.Count; i++) _categoryButtons[i].EnableInClassList("armory-tab--active", i == _category);
            Refresh();
        }

        CosmeticSlot Slot => Categories[_category].slot;

        /// <summary>The weapon picked in the skin list, or null for every weapon.</summary>
        WeaponDefinition PickedWeapon =>
            _weaponField != null && _weaponField.index > 0 && _weaponField.index - 1 < _weapons.Count ? _weapons[_weaponField.index - 1] : null;

        void Choose(CosmeticItem item)
        {
            WeaponDefinition weapon = PickedWeapon;
            _cosmetics.Edit(look =>
            {
                if (item.Slot != CosmeticSlot.WeaponSkin) look.Set(item.Slot, item.Id);
                else if (weapon == null) look.SetSkinForAll(item.Id);
                else look.SetSkin(weapon.weaponId, item.Id);
            });
        }

        void Randomize()
        {
            CharacterLook random = LookRules.Random((uint)Environment.TickCount);
            // A new outfit, the same weapon skins.
            random.weaponSkin = _cosmetics.Look.weaponSkin;
            random.weaponSkins = _cosmetics.Look.Clone().weaponSkins;
            _cosmetics.Set(random);
        }

        /// <summary>Redraws everything from the current look.</summary>
        void Refresh()
        {
            CharacterLook look = _cosmetics.Look;
            CosmeticSlot slot = Slot;
            bool skins = slot == CosmeticSlot.WeaponSkin;
            WeaponDefinition picked = PickedWeapon;

            _heading.text = Categories[_category].title;
            _caption.text = Caption(slot, look, picked);
            _weaponRow.style.display = skins ? DisplayStyle.Flex : DisplayStyle.None;

            _preview.Show(look);
            _preview.FocusWeapon(skins);
            WeaponBuild held = PreviewWeapon(picked);
            _preview.Hold(held, held != null ? look.SkinFor(held.Weapon.weaponId) : null);

            string chosen = skins ? (picked != null ? look.SkinFor(picked.weaponId).Id : look.weaponSkin) : look.Get(slot);
            _items.Clear();
            foreach (CosmeticItem item in CosmeticCatalog.InSlot(slot)) _items.Add(ItemTile(item, item.Id == chosen, look));
        }

        string Caption(CosmeticSlot slot, CharacterLook look, WeaponDefinition picked)
        {
            switch (slot)
            {
                case CosmeticSlot.SuitColor: return "The main colour of your suit.";
                case CosmeticSlot.TrimColor: return "Your arms, and the colour of your headgear and back item.";
                case CosmeticSlot.Pattern: return "Printed on your suit, in a shade of its colour.";
                case CosmeticSlot.Headgear: return "Worn over the head. Purely for looks: your head is the same size to a bullet whatever you wear.";
                case CosmeticSlot.Visor: return "The glow across your face.";
                case CosmeticSlot.Back: return "Worn on your back.";
                default:
                    if (picked == null)
                        return "One skin for every weapon. Weapons given a skin of their own keep it." +
                               (look.weaponSkins.Length > 0 ? " " + look.weaponSkins.Length + " weapon(s) have their own." : "");
                    return "A skin for the " + picked.displayName + " alone. Choosing the skin every weapon has removes its own.";
            }
        }

        /// <summary>What the character holds: the weapon being skinned, or the equipped loadout's primary.</summary>
        WeaponBuild PreviewWeapon(WeaponDefinition picked)
        {
            if (picked != null) return new WeaponBuild(picked);
            if (_loadouts == null) return null;
            Loadout loadout = _loadouts.ResolveEquipped();
            return loadout != null ? loadout.Weapons[0] : null;
        }

        VisualElement ItemTile(CosmeticItem item, bool chosen, CharacterLook look)
        {
            var tile = new Button(() => Choose(item)) { text = "" };
            tile.style.width = 150;
            tile.style.height = 112;
            tile.style.marginRight = tile.style.marginBottom = 10;
            tile.style.paddingLeft = tile.style.paddingRight = tile.style.paddingTop = tile.style.paddingBottom = 8;
            tile.style.backgroundColor = Tile;
            tile.style.alignItems = Align.Center;
            tile.style.justifyContent = Justify.SpaceBetween;
            tile.style.borderTopWidth = tile.style.borderBottomWidth = tile.style.borderLeftWidth = tile.style.borderRightWidth = chosen ? 3 : 1;
            Color edge = chosen ? Accent : new Color(1f, 1f, 1f, 0.08f);
            tile.style.borderTopColor = tile.style.borderBottomColor = tile.style.borderLeftColor = tile.style.borderRightColor = edge;
            Round(tile, 8);

            var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
            swatch.style.width = 92;
            swatch.style.height = 56;
            Round(swatch, 6);
            Paint(swatch, item, look);
            tile.Add(swatch);

            var name = new Label(item.Name) { pickingMode = PickingMode.Ignore };
            name.style.fontSize = 14;
            name.style.color = chosen ? Accent : Text;
            name.style.unityFontStyleAndWeight = chosen ? FontStyle.Bold : FontStyle.Normal;
            tile.Add(name);
            return tile;
        }

        /// <summary>What a tile shows: the colour, the pattern, the glow, or the shape's name in the trim colour.</summary>
        static void Paint(VisualElement swatch, CosmeticItem item, CharacterLook look)
        {
            Color suit = look.Item(CosmeticSlot.SuitColor).Color, trim = look.Item(CosmeticSlot.TrimColor).Color;
            switch (item.Slot)
            {
                case CosmeticSlot.SuitColor:
                case CosmeticSlot.TrimColor:
                case CosmeticSlot.Visor:
                    swatch.style.backgroundColor = item.Color;
                    break;
                case CosmeticSlot.Pattern:
                    swatch.style.backgroundColor = suit;
                    if (item.Pattern != PatternKind.Plain)
                        swatch.style.backgroundImage = new StyleBackground(LookMaterials.Texture(item.Pattern, suit, Color.Lerp(suit, trim, 0.6f)));
                    break;
                case CosmeticSlot.WeaponSkin:
                    if (item.Factory)
                    {
                        swatch.style.backgroundColor = new Color(0.32f, 0.34f, 0.38f);
                        Glyph(swatch, "AS ISSUED");
                    }
                    else
                    {
                        swatch.style.backgroundColor = item.Color;
                        if (item.Pattern != PatternKind.Plain)
                            swatch.style.backgroundImage = new StyleBackground(LookMaterials.Texture(item.Pattern, item.Color, item.Second));
                    }
                    break;
                default:
                    // Headgear and back items: a plate in the trim colour, with a line marking "none".
                    bool none = item.Shape == 0;
                    swatch.style.backgroundColor = none ? new Color(0.2f, 0.22f, 0.27f) : trim;
                    Glyph(swatch, none ? "NONE" : "");
                    break;
            }
        }

        static void Glyph(VisualElement swatch, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = 11;
            label.style.letterSpacing = 2;
            label.style.color = new Color(1f, 1f, 1f, 0.75f);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.flexGrow = 1;
            swatch.Add(label);
        }

        // ------------------------------------------------------------------ helpers

        static Button Button(string text, bool primary, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("pause-button");
            if (primary) button.AddToClassList("primary");
            return button;
        }

        static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            parent.Add(row);
            return row;
        }

        static void Fill(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = element.style.top = element.style.right = element.style.bottom = 0;
        }

        static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
        }
    }
}
