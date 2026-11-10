using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Pulsebreak.Client.FiringRange;
using Pulsebreak.Client.Player;
using Pulsebreak.Client.Settings;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;
using Cursor = UnityEngine.Cursor;

namespace Pulsebreak.Client.UI
{
    /// <summary>
    /// The in-game HUD and pause menu, built with UI Toolkit. The layout lives in
    /// Resources/UI/HudLayout.uxml and the look in HudStyles.uss; this class only moves
    /// data into it: crosshair spread, ammo, hit markers, damage numbers, movement readout,
    /// session stats and the handful of settings needed to tune feel.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudController : MonoBehaviour
    {
        const int DamageNumberCount = 24;
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

        VisualElement _root;
        bool _ready;

        VisualElement _lineTop, _lineBottom, _lineLeft, _lineRight;
        VisualElement _hitMarker;
        VisualElement[] _hitTicks;
        VisualElement _damageLayer, _hints, _pause, _reloadBar, _reloadFill;
        Label _rangefinder, _ammoMag, _ammoReserve, _weaponStatus, _weaponName, _moveState, _moveSpeed;
        Label _statAccuracy, _statHeadshots, _statShots, _statKills, _statTtk;

        readonly DamageNumber[] _numbers = new DamageNumber[DamageNumberCount];
        int _nextNumber;

        float _gap = 12f;
        float _hitTimer, _hitDuration = HitMarkerTime, _hitScale = 1f;
        float _slowTimer;
        int _shownAmmo = -1, _shownStatsVersion = -1;
        WeaponPhase _shownPhase = (WeaponPhase)255;

        public bool IsPaused { get; private set; }

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

        static PanelSettings LoadPanel()
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
            _hitMarker = _root.Q<VisualElement>("hitmarker");
            _hitTicks = new VisualElement[_hitMarker.childCount];
            for (int i = 0; i < _hitTicks.Length; i++) _hitTicks[i] = _hitMarker[i];

            _damageLayer = _root.Q<VisualElement>("damage-layer");
            _hints = _root.Q<VisualElement>("hints");
            _pause = _root.Q<VisualElement>("pause");
            _reloadBar = _root.Q<VisualElement>("reload-bar");
            _reloadFill = _root.Q<VisualElement>("reload-fill");

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

        public void Bind(LocalPlayerController player, Camera playerCamera, RangeStats stats)
        {
            _player = player;
            _camera = playerCamera;
            _stats = stats;
            _player.HitConfirmed += OnHitConfirmed;

            if (_ready) _weaponName.text = player.Weapon.Definition.displayName.ToUpperInvariant();
        }

        void OnDestroy()
        {
            if (_player != null) _player.HitConfirmed -= OnHitConfirmed;
        }

        public void SetPaused(bool paused)
        {
            bool wasPaused = IsPaused;
            IsPaused = paused;

            if (_player != null) _player.InputEnabled = !paused;
            Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = paused;
            if (_ready) _pause.style.display = paused ? DisplayStyle.Flex : DisplayStyle.None;

            if (wasPaused && !paused) GameSettings.Save();
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

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) SetPaused(!IsPaused);
                if (keyboard.f5Key.wasPressedThisFrame) ResetStatsRequested?.Invoke();
                if (keyboard.f1Key.wasPressedThisFrame && _ready)
                    _hints.style.display = _hints.resolvedStyle.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (!_ready || _player == null || _player.Weapon == null) return;

            float dt = Time.deltaTime;
            MotorState motor = _player.MotorState;
            WeaponState weapon = _player.Weapon.State;

            UpdateCrosshair(weapon, dt);
            UpdateHitMarker(dt);
            UpdateDamageNumbers(dt);
            UpdateWeaponPanel(weapon);

            _slowTimer -= dt;
            if (_slowTimer <= 0f)
            {
                _slowTimer = SlowRefreshInterval;
                UpdateMovementPanel(motor);
                UpdateRangefinder();
            }

            if (_stats != null && _stats.Version != _shownStatsVersion) UpdateStatsPanel();
        }

        void UpdateCrosshair(in WeaponState weapon, float dt)
        {
            // Convert the spread cone into pixels at the current field of view, so the gap
            // between the lines is where bullets can actually land.
            float panelHeight = _root.resolvedStyle.height;
            if (float.IsNaN(panelHeight) || panelHeight <= 0f) panelHeight = 1080f;

            float spread = _player.Weapon.CurrentSpread * Mathf.Deg2Rad;
            float halfFov = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float target = Mathf.Tan(spread) / Mathf.Tan(halfFov) * (panelHeight * 0.5f) + 3f;
            _gap = Mathf.Lerp(_gap, target, 1f - Mathf.Exp(-30f * dt));

            const float t = CrosshairThickness, len = CrosshairLength;
            Place(_lineTop, -t * 0.5f, -(_gap + len), t, len);
            Place(_lineBottom, -t * 0.5f, _gap, t, len);
            Place(_lineLeft, -(_gap + len), -t * 0.5f, len, t);
            Place(_lineRight, _gap, -t * 0.5f, len, t);

            // The lines fade out as the sights come up; the centre dot stays.
            float opacity = 1f - Mathf.SmoothStep(0f, 1f, weapon.AdsAmount);
            _lineTop.style.opacity = opacity;
            _lineBottom.style.opacity = opacity;
            _lineLeft.style.opacity = opacity;
            _lineRight.style.opacity = opacity;
        }

        static void Place(VisualElement element, float left, float top, float width, float height)
        {
            element.style.left = left;
            element.style.top = top;
            element.style.width = width;
            element.style.height = height;
        }

        void UpdateWeaponPanel(in WeaponState weapon)
        {
            if (weapon.AmmoInMagazine != _shownAmmo)
            {
                _shownAmmo = weapon.AmmoInMagazine;
                _ammoMag.text = _shownAmmo.ToString();

                WeaponSimulator simulator = _player.Weapon;
                _ammoMag.EnableInClassList("ammo-mag--low", _shownAmmo * 4 <= simulator.Definition.magazineSize);
                _ammoReserve.text = simulator.InfiniteReserve ? "/ unlimited" : "/ " + weapon.AmmoReserve;
            }

            if (weapon.Phase != _shownPhase)
            {
                _shownPhase = weapon.Phase;
                bool reloading = _shownPhase == WeaponPhase.Reloading;
                _weaponStatus.text = reloading ? "RELOADING" : string.Empty;
                _reloadBar.style.display = reloading ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_shownPhase == WeaponPhase.Reloading)
                _reloadFill.style.width = new Length(Mathf.Clamp01(weapon.ReloadProgress) * 100f, LengthUnit.Percent);
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
            _statShots.text = "Shots  " + _stats.Shots + "   Hits  " + _stats.Hits;
            _statKills.text = "Kills  " + _stats.Kills;
            _statTtk.text = "Last time to kill  " + (_stats.LastTimeToKill >= 0f ? _stats.LastTimeToKill.ToString("0.00") + " s" : "-");
        }

        // ------------------------------------------------------------------ hit feedback

        void OnHitConfirmed(HitFeedback hit)
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

        void SpawnDamageNumber(in HitFeedback hit, Color color)
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

                // Pinned to where the bullet landed, rising and fading out.
                float t = number.Age / DamageNumberLife;
                Vector2 position = RuntimePanelUtils.CameraTransformWorldToPanel(panel, number.World, _camera);
                number.Label.style.left = position.x + number.Drift * t;
                number.Label.style.top = position.y - 28f - 70f * t;
                number.Label.style.opacity = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                number.Label.style.display = DisplayStyle.Flex;
            }
        }
    }
}
