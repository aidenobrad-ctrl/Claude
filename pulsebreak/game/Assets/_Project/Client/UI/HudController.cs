using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Pulsebreak.Client.FiringRange;
using Pulsebreak.Client.Loadouts;
using Pulsebreak.Client.Player;
using Pulsebreak.Client.Settings;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;
using Cursor = UnityEngine.Cursor;

namespace Pulsebreak.Client.UI
{
    /// <summary>
    /// The in-game HUD and pause menu, built with UI Toolkit. The layout lives in
    /// Resources/UI/HudLayout.uxml and the look in HudStyles.uss; this class only moves
    /// data into it: crosshair spread, optics, ammo, weapon slots, utility, hit markers,
    /// damage numbers, movement readout, session stats, the loadout screen and a few settings.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudController : MonoBehaviour
    {
        const int DamageNumberCount = 32;
        const float DamageNumberLife = 0.8f;
        const float HitMarkerTime = 0.18f;
        const float CrosshairThickness = 4f;
        const float CrosshairLength = 10f;
        const float SlowRefreshInterval = 0.1f;

        static readonly Color HitColor = Color.white;
        static readonly Color HeadshotColor = new Color(1f, 0.82f, 0.25f);
        static readonly Color KillColor = new Color(1f, 0.33f, 0.27f);

        struct DamageNumber
        {
            public Label Label;
            public Vector3 World;
            public float Age;
            public float Drift;
            public bool Active;
        }

        LocalPlayerController _player;
        Camera _camera;
        RangeStats _stats;
        LoadoutLibrary _library;
        LoadoutPanel _loadoutPanel;

        VisualElement _root;
        bool _ready;

        VisualElement _lineTop, _lineBottom, _lineLeft, _lineRight, _centreDot;
        VisualElement _reticle, _reticleDot, _reticleCross, _reticleChevron;
        VisualElement _scope, _scopeLens;
        VisualElement _hitMarker;
        VisualElement[] _hitTicks;
        VisualElement _damageLayer, _hints, _pause, _slots, _utilities, _quick, _quickList;
        VisualElement _reloadBar, _reloadFill, _chargeBar, _chargeFill;
        Label _rangefinder, _ammoMag, _ammoReserve, _weaponStatus, _weaponName, _moveState, _moveSpeed;
        Label _statAccuracy, _statHeadshots, _statShots, _statKills, _statTtk;
        Label _loadoutTitle;
        Label[] _slotLabels = new Label[0];
        VisualElement[] _utilityRows = new VisualElement[0];
        Label[] _utilityCounts = new Label[0];
        int[] _shownUtilityCharges = new int[0];
        static readonly string[] UtilityKeys = { "G", "F" };

        readonly DamageNumber[] _numbers = new DamageNumber[DamageNumberCount];
        int _nextNumber;

        float _gap = 12f;
        float _hitTimer, _hitDuration = HitMarkerTime, _hitScale = 1f;
        float _slowTimer;
        int _shownAmmo = int.MinValue, _shownStatsVersion = -1;
        string _shownStatus;
        bool _shownReloadBar, _shownChargeBar, _shownScope;
        ReticleStyle _shownReticle = (ReticleStyle)255;

        public bool IsPaused { get; private set; }

        /// <summary>The quick loadout picker is open.</summary>
        public bool IsQuickSwapOpen { get; private set; }
        bool _resumeInputNextFrame;

        public event Action ResetStatsRequested;

        /// <summary>Resources path of the panel settings asset the Project Doctor generates.</summary>
        public const string PanelResource = "UI/HudPanel";
        public const string ThemeResource = "UI/PulsebreakTheme";

        /// <summary>Creates the HUD object, its panel and its document.</summary>
        public static HudController Create()
        {
            var go = new GameObject("HUD");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = LoadPanel();
            document.visualTreeAsset = Resources.Load<VisualTreeAsset>("UI/HudLayout");

            var hud = go.AddComponent<HudController>();
            hud.BuildView(document);
            return hud;
        }

        /// <summary>The panel every screen of the game draws on.</summary>
        public static PanelSettings LoadPanel()
        {
            // Prefer the saved asset: it is what makes a built player include UI Toolkit's shaders.
            var asset = Resources.Load<PanelSettings>(PanelResource);
            if (asset != null) return asset;

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "HUD Panel (runtime)";
            ConfigurePanel(settings, Resources.Load<ThemeStyleSheet>(ThemeResource));
            return settings;
        }

        /// <summary>The HUD scales from a 1920x1080 reference by screen height. Shared with the Editor tool that saves the asset.</summary>
        public static void ConfigurePanel(PanelSettings settings, ThemeStyleSheet theme)
        {
            settings.themeStyleSheet = theme;
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f;
        }

        void BuildView(UIDocument document)
        {
            _root = document.rootVisualElement;
            if (_root == null || _root.Q<VisualElement>("hud") == null)
            {
                Debug.LogError("Pulsebreak HUD: could not load Resources/UI/HudLayout.uxml. The game runs, but without a HUD.");
                return;
            }

            _root.pickingMode = PickingMode.Ignore;
            _root.style.position = Position.Absolute;
            _root.style.left = 0f;
            _root.style.top = 0f;
            _root.style.right = 0f;
            _root.style.bottom = 0f;

            var styles = Resources.Load<StyleSheet>("UI/HudStyles");
            if (styles != null) _root.styleSheets.Add(styles);
            else Debug.LogWarning("Pulsebreak HUD: Resources/UI/HudStyles.uss not found; the HUD will be unstyled.");

            _lineTop = _root.Q<VisualElement>("ch-top");
            _lineBottom = _root.Q<VisualElement>("ch-bottom");
            _lineLeft = _root.Q<VisualElement>("ch-left");
            _lineRight = _root.Q<VisualElement>("ch-right");
            _centreDot = _root.Q<VisualElement>("ch-dot");
            _reticle = _root.Q<VisualElement>("reticle");
            _reticleDot = _root.Q<VisualElement>("ret-dot");
            _reticleCross = _root.Q<VisualElement>("ret-cross");
            _reticleChevron = _root.Q<VisualElement>("ret-chevron");
            _scope = _root.Q<VisualElement>("scope");
            _scopeLens = _root.Q<VisualElement>("scope-lens");
            _scopeLens.style.backgroundImage = new StyleBackground(BuildScopeTexture());

            _hitMarker = _root.Q<VisualElement>("hitmarker");
            _hitTicks = new VisualElement[_hitMarker.childCount];
            for (int i = 0; i < _hitTicks.Length; i++) _hitTicks[i] = _hitMarker[i];

            _damageLayer = _root.Q<VisualElement>("damage-layer");
            _hints = _root.Q<VisualElement>("hints");
            _pause = _root.Q<VisualElement>("pause");
            _slots = _root.Q<VisualElement>("slots");
            _utilities = _root.Q<VisualElement>("utilities");
            _quick = _root.Q<VisualElement>("quick");
            _quickList = _root.Q<VisualElement>("quick-list");
            _loadoutTitle = _root.Q<Label>("loadout-title");
            _reloadBar = _root.Q<VisualElement>("reload-bar");
            _reloadFill = _root.Q<VisualElement>("reload-fill");
            _chargeBar = _root.Q<VisualElement>("charge-bar");
            _chargeFill = _root.Q<VisualElement>("charge-fill");

            _rangefinder = _root.Q<Label>("rangefinder");
            _ammoMag = _root.Q<Label>("ammo-mag");
            _ammoReserve = _root.Q<Label>("ammo-reserve");
            _weaponStatus = _root.Q<Label>("weapon-status");
            _weaponName = _root.Q<Label>("weapon-name");
            _moveState = _root.Q<Label>("move-state");
            _moveSpeed = _root.Q<Label>("move-speed");
            _statAccuracy = _root.Q<Label>("stat-accuracy");
            _statHeadshots = _root.Q<Label>("stat-headshots");
            _statShots = _root.Q<Label>("stat-shots");
            _statKills = _root.Q<Label>("stat-kills");
            _statTtk = _root.Q<Label>("stat-ttk");

            for (int i = 0; i < DamageNumberCount; i++)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList("damage-number");
                _damageLayer.Add(label);
                _numbers[i].Label = label;
            }

            BindSettings();
            _ready = true;
        }

        void BindSettings()
        {
            var sensitivity = _root.Q<Slider>("set-sensitivity");
            sensitivity.lowValue = GameSettings.MinSensitivity;
            sensitivity.highValue = GameSettings.MaxSensitivity;
            sensitivity.SetValueWithoutNotify(GameSettings.Sensitivity);
            sensitivity.RegisterValueChangedCallback(e =>
            {
                GameSettings.Sensitivity = e.newValue;
                GameSettings.Apply();
            });

            var ads = _root.Q<Slider>("set-ads");
            ads.lowValue = GameSettings.MinAdsMultiplier;
            ads.highValue = GameSettings.MaxAdsMultiplier;
            ads.SetValueWithoutNotify(GameSettings.AdsSensitivityMultiplier);
            ads.RegisterValueChangedCallback(e =>
            {
                GameSettings.AdsSensitivityMultiplier = e.newValue;
                GameSettings.Apply();
            });

            var fov = _root.Q<SliderInt>("set-fov");
            fov.lowValue = Mathf.RoundToInt(GameSettings.MinFieldOfView);
            fov.highValue = Mathf.RoundToInt(GameSettings.MaxFieldOfView);
            fov.SetValueWithoutNotify(Mathf.RoundToInt(GameSettings.FieldOfView));
            fov.RegisterValueChangedCallback(e =>
            {
                GameSettings.FieldOfView = e.newValue;
                GameSettings.Apply();
            });

            var volume = _root.Q<Slider>("set-volume");
            volume.SetValueWithoutNotify(GameSettings.MasterVolume);
            volume.RegisterValueChangedCallback(e =>
            {
                GameSettings.MasterVolume = e.newValue;
                GameSettings.Apply();
            });

            var damageNumbers = _root.Q<Toggle>("set-damage-numbers");
            damageNumbers.SetValueWithoutNotify(GameSettings.ShowDamageNumbers);
            damageNumbers.RegisterValueChangedCallback(e =>
            {
                GameSettings.ShowDamageNumbers = e.newValue;
                GameSettings.Apply();
            });

            _root.Q<Button>("btn-resume").clicked += () => SetPaused(false);
            _root.Q<Button>("btn-reset").clicked += () => ResetStatsRequested?.Invoke();
            _root.Q<Button>("btn-quit").clicked += Quit;
        }

        public void Bind(LocalPlayerController player, Camera playerCamera, RangeStats stats, LoadoutLibrary library)
        {
            _player = player;
            _camera = playerCamera;
            _stats = stats;
            _library = library;
            _player.HitConfirmed += OnHitConfirmed;
            _player.LoadoutChanged += OnLoadoutChanged;
            _player.ActiveWeaponChanged += OnActiveWeaponChanged;
            _library.Changed += OnLibraryChanged;

            if (!_ready) return;
            _loadoutPanel = new LoadoutPanel(_root.Q<VisualElement>("loadouts"), library);
            OnLoadoutChanged();
            OnActiveWeaponChanged(player.ActiveBuild);
            OnLibraryChanged();
        }

        void OnDestroy()
        {
            if (_library != null) _library.Changed -= OnLibraryChanged;
            if (_loadoutPanel != null) _loadoutPanel.Dispose();
            if (_player == null) return;
            _player.HitConfirmed -= OnHitConfirmed;
            _player.LoadoutChanged -= OnLoadoutChanged;
            _player.ActiveWeaponChanged -= OnActiveWeaponChanged;
        }

        public void SetPaused(bool paused)
        {
            bool wasPaused = IsPaused;
            IsPaused = paused;

            if (_player != null) _player.InputEnabled = !paused;
            Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = paused;
            if (_ready) _pause.style.display = paused ? DisplayStyle.Flex : DisplayStyle.None;

            if (paused)
            {
                SetQuickSwapOpen(false);
                if (_loadoutPanel != null) _loadoutPanel.Refresh();
            }
            if (wasPaused && !paused) GameSettings.Save();
        }

        /// <summary>
        /// The quick picker: every loadout on one small list, chosen by number key or click.
        /// It stands in for choosing a loadout between rounds until there are rounds.
        /// </summary>
        public void SetQuickSwapOpen(bool open)
        {
            if (!_ready || open == IsQuickSwapOpen || (open && IsPaused)) return;
            IsQuickSwapOpen = open;
            _quick.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

            if (open)
            {
                _player.InputEnabled = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                RebuildQuickSwapList();
            }
            else if (!IsPaused)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                // Wait a frame before handing input back, so the number key that picked a
                // loadout is not also read as "switch to weapon slot".
                _resumeInputNextFrame = true;
            }
        }

        void RebuildQuickSwapList()
        {
            _quickList.Clear();
            var all = _library.All();
            for (int i = 0; i < all.Count; i++)
            {
                string id = all[i].id;
                string number = i < 9 ? (i + 1) + "   " : "     ";
                var button = new Button(() => QuickEquip(id)) { text = number + all[i].name };
                button.AddToClassList("quick-item");
                button.EnableInClassList("quick-item--active", id == _library.EquippedId);
                _quickList.Add(button);
            }
        }

        void QuickEquip(string id)
        {
            _library.Equip(id);
            SetQuickSwapOpen(false);
        }

        void OnApplicationFocus(bool hasFocus)
        {
            // Coming back to the window: take the mouse again unless a menu is open.
            if (hasFocus && !IsPaused)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        /// <summary>If anything listens, the pause menu's last button asks it instead of closing the game.</summary>
        public event Action QuitRequested;

        /// <summary>
        /// Switches the HUD between its firing range and match forms: in a match the range's
        /// key hints, stats panel and "Reset stats" button are hidden, and the last button
        /// of the pause menu reads as given.
        /// </summary>
        public void SetMatchMode(string quitLabel)
        {
            if (!_ready) return;
            _hints.style.display = DisplayStyle.None;
            _root.Q<VisualElement>("range-stats").style.display = DisplayStyle.None;
            _root.Q<Button>("btn-reset").style.display = DisplayStyle.None;
            SetQuitLabel(quitLabel);
        }

        public void SetQuitLabel(string label)
        {
            if (_ready) _root.Q<Button>("btn-quit").text = label;
        }

        void Quit()
        {
            if (QuitRequested != null)
            {
                QuitRequested();
                return;
            }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ loadout

        void OnLibraryChanged()
        {
            if (!_ready) return;
            LoadoutData equipped = _library.Equipped;
            _loadoutTitle.text = equipped != null ? equipped.name.ToUpperInvariant() : string.Empty;
        }

        void OnLoadoutChanged()
        {
            if (!_ready) return;

            _slots.Clear();
            _slotLabels = new Label[_player.SlotCount];
            for (int i = 0; i < _slotLabels.Length; i++)
            {
                WeaponBuild build = _player.GetBuild(i);
                var label = new Label((i + 1) + "  " + (build != null ? build.Weapon.displayName.ToUpperInvariant() : "-"))
                {
                    pickingMode = PickingMode.Ignore,
                };
                label.AddToClassList("slot");
                _slots.Add(label);
                _slotLabels[i] = label;
            }

            _utilities.Clear();
            UtilityBelt belt = _player.Utilities;
            _utilityRows = new VisualElement[belt.SlotCount];
            _utilityCounts = new Label[belt.SlotCount];
            _shownUtilityCharges = new int[belt.SlotCount];
            for (int i = 0; i < belt.SlotCount; i++)
            {
                UtilityDefinition item = belt.GetItem(i);
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("utility");

                var key = new Label(i < UtilityKeys.Length ? UtilityKeys[i] : string.Empty) { pickingMode = PickingMode.Ignore };
                key.AddToClassList("utility-key");
                var name = new Label(item != null ? item.displayName.ToUpperInvariant() : "-") { pickingMode = PickingMode.Ignore };
                name.AddToClassList("utility-name");
                var count = new Label { pickingMode = PickingMode.Ignore };
                count.AddToClassList("utility-count");

                row.Add(key);
                row.Add(name);
                row.Add(count);
                _utilities.Add(row);
                _utilityRows[i] = row;
                _utilityCounts[i] = count;
                _shownUtilityCharges[i] = -1;
            }
        }

        void OnActiveWeaponChanged(WeaponBuild build)
        {
            if (!_ready) return;

            _weaponName.text = build.Weapon.displayName.ToUpperInvariant();
            _shownAmmo = int.MinValue;

            int active = _player.Inventory.ActiveIndex;
            for (int i = 0; i < _slotLabels.Length; i++) _slotLabels[i].EnableInClassList("slot--active", i == active);
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            if (_resumeInputNextFrame)
            {
                _resumeInputNextFrame = false;
                if (_player != null && !IsPaused && !IsQuickSwapOpen) _player.InputEnabled = true;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    if (IsQuickSwapOpen) SetQuickSwapOpen(false);
                    else SetPaused(!IsPaused);
                }
                // The rest are ignored while the pause menu is up, where the same keys are typed into the name box.
                else if (!IsPaused)
                {
                    if (keyboard.f5Key.wasPressedThisFrame) ResetStatsRequested?.Invoke();
                    if (keyboard.f1Key.wasPressedThisFrame && _ready)
                        _hints.style.display = _hints.resolvedStyle.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
                    if (keyboard.bKey.wasPressedThisFrame && _library != null) SetQuickSwapOpen(!IsQuickSwapOpen);
                    else if (IsQuickSwapOpen) HandleQuickSwapKeys(keyboard);
                }
            }

            if (!_ready || _player == null || _player.Inventory == null) return;

            float dt = Time.deltaTime;
            MotorState motor = _player.MotorState;
            WeaponSimulator inHand = _player.ActiveWeapon;
            WeaponState weapon = inHand.State;

            UpdateSights(inHand, weapon, dt);
            UpdateHitMarker(dt);
            UpdateDamageNumbers(dt);
            UpdateWeaponPanel(inHand, weapon);
            UpdateUtilities();

            _slowTimer -= dt;
            if (_slowTimer <= 0f)
            {
                _slowTimer = SlowRefreshInterval;
                UpdateMovementPanel(motor);
                UpdateRangefinder();
            }

            if (_stats != null && _stats.Version != _shownStatsVersion) UpdateStatsPanel();
        }

        void HandleQuickSwapKeys(Keyboard keyboard)
        {
            var all = _library.All();
            for (int i = 0; i < all.Count && i < 9; i++)
            {
                if (!keyboard[Key.Digit1 + i].wasPressedThisFrame) continue;
                QuickEquip(all[i].id);
                return;
            }
        }

        void UpdateUtilities()
        {
            UtilityBelt belt = _player.Utilities;
            for (int i = 0; i < _utilityCounts.Length && i < belt.SlotCount; i++)
            {
                int charges = belt.GetState(i).Charges;
                if (charges == _shownUtilityCharges[i]) continue;
                _shownUtilityCharges[i] = charges;
                _utilityCounts[i].text = "x" + charges;
                _utilityRows[i].EnableInClassList("utility--empty", charges <= 0);
            }
        }

        /// <summary>Hip-fire crosshair, optic reticle and scope view, blended by how far the weapon is aimed.</summary>
        void UpdateSights(WeaponSimulator inHand, in WeaponState weapon, float dt)
        {
            WeaponStats stats = inHand.Stats;
            bool melee = stats.ballistics == BallisticsType.Melee;
            ReticleStyle reticle = _player.ActiveBuild.Reticle;

            // Convert the spread cone into pixels at the current field of view, so the gap
            // between the lines is where shots can actually land.
            float panelHeight = _root.resolvedStyle.height;
            if (float.IsNaN(panelHeight) || panelHeight <= 0f) panelHeight = 1080f;

            float spread = inHand.CurrentSpread * Mathf.Deg2Rad;
            float halfFov = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float target = Mathf.Tan(spread) / Mathf.Tan(halfFov) * (panelHeight * 0.5f) + 3f;
            _gap = Mathf.Lerp(_gap, target, 1f - Mathf.Exp(-30f * dt));

            const float t = CrosshairThickness, len = CrosshairLength;
            Place(_lineTop, -t * 0.5f, -(_gap + len), t, len);
            Place(_lineBottom, -t * 0.5f, _gap, t, len);
            Place(_lineLeft, -(_gap + len), -t * 0.5f, len, t);
            Place(_lineRight, _gap, -t * 0.5f, len, t);

            // The lines fade out as the sights come up. A blade has no spread to show.
            float aimed = Mathf.SmoothStep(0f, 1f, weapon.AdsAmount);
            float lines = melee ? 0f : 1f - aimed;
            _lineTop.style.opacity = lines;
            _lineBottom.style.opacity = lines;
            _lineLeft.style.opacity = lines;
            _lineRight.style.opacity = lines;

            bool optic = reticle == ReticleStyle.Dot || reticle == ReticleStyle.Cross || reticle == ReticleStyle.Chevron;
            _reticle.style.opacity = optic ? aimed : 0f;
            _centreDot.style.opacity = optic || reticle == ReticleStyle.Scope ? 1f - aimed : 1f;

            if (reticle != _shownReticle)
            {
                _shownReticle = reticle;
                _reticleDot.style.display = reticle == ReticleStyle.Dot ? DisplayStyle.Flex : DisplayStyle.None;
                _reticleCross.style.display = reticle == ReticleStyle.Cross ? DisplayStyle.Flex : DisplayStyle.None;
                _reticleChevron.style.display = reticle == ReticleStyle.Chevron ? DisplayStyle.Flex : DisplayStyle.None;
            }

            bool swapping = weapon.Phase == WeaponPhase.Holstering || weapon.Phase == WeaponPhase.Equipping;
            bool scoped = reticle == ReticleStyle.Scope && weapon.AdsAmount > 0.9f && !swapping;
            if (scoped != _shownScope)
            {
                _shownScope = scoped;
                _scope.style.display = scoped ? DisplayStyle.Flex : DisplayStyle.None;
                // The lens is square: as wide as the screen is tall.
                if (scoped) _scopeLens.style.width = panelHeight;
            }
        }

        static void Place(VisualElement element, float left, float top, float width, float height)
        {
            element.style.left = left;
            element.style.top = top;
            element.style.width = width;
            element.style.height = height;
        }

        void UpdateWeaponPanel(WeaponSimulator inHand, in WeaponState weapon)
        {
            WeaponStats stats = inHand.Stats;

            if (weapon.AmmoInMagazine != _shownAmmo)
            {
                _shownAmmo = weapon.AmmoInMagazine;
                if (stats.UsesAmmo)
                {
                    _ammoMag.text = _shownAmmo.ToString();
                    _ammoMag.EnableInClassList("ammo-mag--low", _shownAmmo * 4 <= stats.magazineSize);
                    _ammoReserve.text = inHand.InfiniteReserve ? "/ unlimited" : "/ " + weapon.AmmoReserve;
                }
                else
                {
                    _ammoMag.text = "-";
                    _ammoMag.EnableInClassList("ammo-mag--low", false);
                    _ammoReserve.text = string.Empty;
                }
            }

            bool reloading = weapon.Phase == WeaponPhase.Reloading;
            bool charging = weapon.ChargeAmount > 0f;
            string status = reloading ? "RELOADING"
                : weapon.Phase == WeaponPhase.Holstering || weapon.Phase == WeaponPhase.Equipping ? "SWAPPING"
                : charging ? "CHARGING"
                : string.Empty;
            if (status != _shownStatus)
            {
                _shownStatus = status;
                _weaponStatus.text = status;
            }

            if (reloading != _shownReloadBar)
            {
                _shownReloadBar = reloading;
                _reloadBar.style.display = reloading ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (reloading) _reloadFill.style.width = new Length(weapon.ReloadProgress * 100f, LengthUnit.Percent);

            if (charging != _shownChargeBar)
            {
                _shownChargeBar = charging;
                _chargeBar.style.display = charging ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (charging) _chargeFill.style.width = new Length(Mathf.Clamp01(weapon.ChargeAmount) * 100f, LengthUnit.Percent);
        }

        void UpdateMovementPanel(in MotorState motor)
        {
            string state;
            if (motor.Mantling) state = "MANTLE";
            else if (!motor.Grounded) state = "AIR";
            else if (motor.Stance == Stance.Sliding) state = "SLIDE";
            else if (motor.Stance == Stance.Crouching) state = "CROUCH";
            else if (motor.Sprinting) state = "SPRINT";
            else state = motor.HorizontalSpeed > 0.2f ? "RUN" : "IDLE";

            _moveState.text = state;
            _moveSpeed.text = motor.HorizontalSpeed.ToString("0.0") + " m/s";
        }

        void UpdateRangefinder()
        {
            Transform view = _camera.transform;
            bool hit = Physics.Raycast(view.position, view.forward, out RaycastHit info, 500f,
                GameLayers.ShotMask, QueryTriggerInteraction.Ignore);
            _rangefinder.text = hit ? Mathf.RoundToInt(info.distance) + " m" : string.Empty;
        }

        void UpdateStatsPanel()
        {
            _shownStatsVersion = _stats.Version;
            _statAccuracy.text = "Accuracy  " + (_stats.Shots > 0 ? Mathf.RoundToInt(_stats.Accuracy * 100f) + "%" : "-");
            _statHeadshots.text = "Headshots  " + (_stats.Hits > 0 ? Mathf.RoundToInt(_stats.HeadshotRate * 100f) + "%" : "-");
            _statShots.text = "Shots  " + _stats.Shots + "   Landed  " + _stats.ShotsLanded;
            _statKills.text = "Kills  " + _stats.Kills + "   Damage  " + Mathf.RoundToInt(_stats.DamageDealt);
            _statTtk.text = "Last time to kill  " + (_stats.LastTimeToKill >= 0f ? _stats.LastTimeToKill.ToString("0.00") + " s" : "-");
        }

        // ------------------------------------------------------------------ hit feedback

        void OnHitConfirmed(HitReport hit)
        {
            if (!_ready) return;

            Color color = hit.Killed ? KillColor : hit.Zone == HitZone.Head ? HeadshotColor : HitColor;
            for (int i = 0; i < _hitTicks.Length; i++) _hitTicks[i].style.backgroundColor = color;

            _hitDuration = HitMarkerTime * (hit.Killed ? 1.8f : 1f);
            _hitTimer = _hitDuration;
            _hitScale = hit.Killed ? 1.45f : hit.Zone == HitZone.Head ? 1.2f : 1f;

            if (GameSettings.ShowDamageNumbers) SpawnDamageNumber(hit, color);
        }

        void UpdateHitMarker(float dt)
        {
            if (_hitTimer <= 0f) return;

            _hitTimer -= dt;
            float t = Mathf.Clamp01(_hitTimer / _hitDuration);
            float scale = _hitScale * (1f + 0.3f * t);
            _hitMarker.style.opacity = t;
            _hitMarker.style.scale = new Scale(new Vector3(scale, scale, 1f));
        }

        void SpawnDamageNumber(in HitReport hit, Color color)
        {
            ref DamageNumber number = ref _numbers[_nextNumber];
            _nextNumber = (_nextNumber + 1) % DamageNumberCount;

            number.World = hit.Point;
            number.Age = 0f;
            number.Drift = UnityEngine.Random.Range(-28f, 28f);
            number.Active = true;
            number.Label.text = Mathf.Max(1, Mathf.RoundToInt(hit.Damage)).ToString();
            number.Label.style.color = color;
        }

        void UpdateDamageNumbers(float dt)
        {
            IPanel panel = _damageLayer.panel;
            Transform view = _camera.transform;

            for (int i = 0; i < DamageNumberCount; i++)
            {
                ref DamageNumber number = ref _numbers[i];
                if (!number.Active) continue;

                number.Age += dt;
                bool expired = number.Age >= DamageNumberLife;
                bool inFront = Vector3.Dot(view.forward, number.World - view.position) > 0.1f;
                if (expired || !inFront || panel == null)
                {
                    number.Label.style.display = DisplayStyle.None;
                    if (expired) number.Active = false;
                    continue;
                }

                // Pinned to where the shot landed, rising and fading out.
                float t = number.Age / DamageNumberLife;
                Vector2 position = RuntimePanelUtils.CameraTransformWorldToPanel(panel, number.World, _camera);
                number.Label.style.left = position.x + number.Drift * t;
                number.Label.style.top = position.y - 28f - 70f * t;
                number.Label.style.opacity = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                number.Label.style.display = DisplayStyle.Flex;
            }
        }

        /// <summary>The scope view: a clear circle with a crosshair, black outside it.</summary>
        static Texture2D BuildScopeTexture()
        {
            const int size = 512;
            const float half = size * 0.5f;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half, dy = y + 0.5f - half;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy) / half;

                    // Clear in the middle, darkening into a black rim.
                    float alpha = Mathf.InverseLerp(0.86f, 0.95f, distance);
                    bool hair = Mathf.Abs(dx) < 1.2f || Mathf.Abs(dy) < 1.2f;
                    bool post = (Mathf.Abs(dy) < 4f && Mathf.Abs(dx) > half * 0.45f) || (Mathf.Abs(dx) < 4f && dy < -half * 0.45f);
                    if (hair || post) alpha = Mathf.Max(alpha, 0.9f);

                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Scope Lens",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
