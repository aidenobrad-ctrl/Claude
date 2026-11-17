using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Pulsebreak.Client.Audio;
using Pulsebreak.Client.Settings;

namespace Pulsebreak.Client.UI
{
    /// <summary>
    /// Every setting in one place, in three tabs: gameplay (aim and feel), video (quality,
    /// window, frame rate) and audio (a slider for each kind of sound). Changes take effect
    /// as they are made; closing the screen saves them, and the gameplay and audio ones follow
    /// a signed-in account to other computers. Opened from the main menu and the pause menu.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsScreen : MonoBehaviour
    {
        static readonly string[] TabNames = { "GAMEPLAY", "VIDEO", "AUDIO" };
        static readonly List<string> QualityNames = new List<string> { "Low", "Medium", "High", "Ultra" };
        static readonly List<string> WindowNames = new List<string> { "Fullscreen", "Borderless window", "Windowed" };

        static SettingsScreen _open;

        readonly List<Button> _tabs = new List<Button>();
        VisualElement _body;
        Label _hint;
        int _tab;

        /// <summary>The screen is showing. Other screens leave Esc to it meanwhile.</summary>
        public static bool IsOpen => _open != null;

        public event Action Closed;

        public static SettingsScreen Open()
        {
            if (_open != null) return _open;
            var go = new GameObject("Settings Screen");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = HudController.LoadPanel();
            document.sortingOrder = 45f;
            var screen = go.AddComponent<SettingsScreen>();
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
            GameSettings.Save();
            if (_open == this) _open = null;
            Closed?.Invoke();
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                UiSounds.Play(Sfx.UiBack);
                Close();
            }
        }

        void Build(VisualElement root)
        {
            var styles = Resources.Load<StyleSheet>("UI/HudStyles");
            if (styles != null) root.styleSheets.Add(styles);
            root.AddToClassList("hud");
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            root.style.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 0.86f);
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            UiSounds.Attach(root);

            var card = new VisualElement();
            card.AddToClassList("pause-card");
            UiMotion.Enter(card);
            card.style.width = 720;
            card.style.minHeight = 560;
            root.Add(card);

            var title = new Label("SETTINGS");
            title.AddToClassList("pause-title");
            card.Add(title);

            var tabs = new VisualElement();
            tabs.AddToClassList("armory-tabs");
            card.Add(tabs);
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                var tab = new Button(() => SelectTab(index)) { text = TabNames[i] };
                tab.AddToClassList("armory-tab");
                tabs.Add(tab);
                _tabs.Add(tab);
            }

            _body = new VisualElement();
            _body.style.flexGrow = 1;
            card.Add(_body);

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

            SelectTab(0);
        }

        void SelectTab(int tab)
        {
            _tab = tab;
            for (int i = 0; i < _tabs.Count; i++) _tabs[i].EnableInClassList("armory-tab--active", i == _tab);
            _body.Clear();
            switch (_tab)
            {
                case 0: BuildGameplay(); break;
                case 1: BuildVideo(); break;
                default: BuildAudio(); break;
            }
        }

        // ------------------------------------------------------------------ tabs

        void BuildGameplay()
        {
            AddSlider("Sensitivity", GameSettings.MinSensitivity, GameSettings.MaxSensitivity, GameSettings.Sensitivity, v => GameSettings.Sensitivity = v);
            AddSlider("Aim sensitivity", GameSettings.MinAdsMultiplier, GameSettings.MaxAdsMultiplier, GameSettings.AdsSensitivityMultiplier, v => GameSettings.AdsSensitivityMultiplier = v);
            var fov = new SliderInt("Field of view", Mathf.RoundToInt(GameSettings.MinFieldOfView), Mathf.RoundToInt(GameSettings.MaxFieldOfView)) { showInputField = true };
            fov.SetValueWithoutNotify(Mathf.RoundToInt(GameSettings.FieldOfView));
            fov.RegisterValueChangedCallback(e => Change(() => GameSettings.FieldOfView = e.newValue));
            _body.Add(fov);
            AddToggle("Damage numbers", GameSettings.ShowDamageNumbers, v => GameSettings.ShowDamageNumbers = v);
            AddToggle("Toggle sprint", GameSettings.ToggleSprint, v => GameSettings.ToggleSprint = v);
            AddToggle("Camera shake", GameSettings.CameraShake, v => GameSettings.CameraShake = v);
            AddToggle("Show chat", GameSettings.ShowChat, v => GameSettings.ShowChat = v);
            AddToggle("Filter strong language", GameSettings.FilterChat, v => GameSettings.FilterChat = v);
            _hint.text = "Sensitivity, field of view, damage numbers and toggle sprint follow your account to other computers. " +
                         "In a match, Enter or T chats to everyone and Y to your team.";
        }

        void BuildVideo()
        {
            var quality = new DropdownField("Quality", QualityNames, (int)GameSettings.Quality);
            quality.RegisterValueChangedCallback(_ => Change(() => GameSettings.Quality = (GraphicsQuality)quality.index));
            _body.Add(quality);

            var window = new DropdownField("Display", WindowNames, (int)GameSettings.Window);
            window.RegisterValueChangedCallback(_ => Change(() => GameSettings.Window = (WindowMode)window.index));
            _body.Add(window);

            var caps = new List<string>();
            foreach (int cap in GameSettings.FrameCaps) caps.Add(cap == 0 ? "No limit" : cap + " fps");
            var frameCap = new DropdownField("Frame rate limit", caps, Mathf.Max(0, Array.IndexOf(GameSettings.FrameCaps, GameSettings.FrameCap)));
            frameCap.RegisterValueChangedCallback(_ => Change(() => GameSettings.FrameCap = GameSettings.FrameCaps[Mathf.Max(0, frameCap.index)]));
            _body.Add(frameCap);

            AddToggle("VSync", GameSettings.VSync, v => GameSettings.VSync = v);
            AddSlider("Render scale", GameSettings.MinRenderScale, GameSettings.MaxRenderScale, GameSettings.RenderScale, v => GameSettings.RenderScale = v);
            AddToggle("Show frame rate", GameSettings.ShowFps, v => GameSettings.ShowFps = v);
            _hint.text = "Low turns off bloom and soft shadows for the most frames. Render scale below 1 draws the world at a lower " +
                         "resolution, which is faster; the interface stays sharp. VSync ignores the frame rate limit. " +
                         "Video settings stay on this computer.";
        }

        void BuildAudio()
        {
            AddSlider("Master", 0f, 1f, GameSettings.MasterVolume, v => GameSettings.MasterVolume = v);
            AddSlider("Effects", 0f, 1f, GameSettings.EffectsVolume, v => GameSettings.EffectsVolume = v, Sfx.Hit);
            AddSlider("Music", 0f, 1f, GameSettings.MusicVolume, v => GameSettings.MusicVolume = v);
            AddSlider("Interface", 0f, 1f, GameSettings.InterfaceVolume, v => GameSettings.InterfaceVolume = v, Sfx.UiClick);
            _hint.text = "Effects are weapons, footsteps and explosions. Music plays in the menus and at the big moments of a match. " +
                         "Interface is clicks, countdowns and objective calls.";
        }

        // ------------------------------------------------------------------ helpers

        /// <param name="preview">A sound to play when the slider is let go, to hear the new level.</param>
        void AddSlider(string label, float low, float high, float value, Action<float> set, Sfx? preview = null)
        {
            var slider = new Slider(label, low, high) { showInputField = true };
            slider.SetValueWithoutNotify(value);
            slider.RegisterValueChangedCallback(e => Change(() => set(e.newValue)));
            if (preview.HasValue)
            {
                Sfx sound = preview.Value;
                slider.RegisterCallback<PointerCaptureOutEvent>(_ =>
                {
                    if (sound == Sfx.UiClick) UiSounds.Play(sound);
                    else if (SfxPlayer.Current != null) SfxPlayer.Current.Play(sound);
                    else UiSounds.Play(Sfx.UiClick);
                });
            }
            _body.Add(slider);
        }

        void AddToggle(string label, bool value, Action<bool> set)
        {
            var toggle = new Toggle(label);
            toggle.SetValueWithoutNotify(value);
            toggle.RegisterValueChangedCallback(e => Change(() => set(e.newValue)));
            _body.Add(toggle);
        }

        static void Change(Action change)
        {
            change();
            GameSettings.Apply();
        }
    }
}
