using UnityEngine;
using UnityEngine.UIElements;

namespace Pulsebreak.Client.Audio
{
    /// <summary>
    /// Interface sounds: clicks and hovers in the menus, countdown beeps, objective chimes.
    /// They play flat from an object of their own that outlives scenes, so they work in the
    /// menu, in a match and in the firing range alike, at the interface volume.
    /// </summary>
    public static class UiSounds
    {
        const int VoiceCount = 4;
        static AudioSource[] _voices;
        static int _next;
        static float _lastHover;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _voices = null;
            _next = 0;
            _lastHover = 0f;
        }

        public static void Play(Sfx sfx, float volume = 1f)
        {
            if (Application.isBatchMode) return;
            if (_voices == null || _voices[0] == null) Create();
            float level = volume * SoundBank.Volume(SoundBank.BusOf(sfx));
            AudioClip clip = SoundBank.Get(sfx);
            if (clip == null || level <= 0.001f) return;
            AudioSource voice = _voices[_next];
            _next = (_next + 1) % VoiceCount;
            voice.clip = clip;
            voice.volume = Mathf.Clamp01(level);
            voice.Play();
        }

        /// <summary>
        /// Gives every button, toggle and dropdown under <paramref name="root"/> a hover tick
        /// and a click, including ones added later.
        /// </summary>
        public static void Attach(VisualElement root)
        {
            if (root == null) return;
            root.RegisterCallback<PointerEnterEvent>(OnPointerEnter, TrickleDown.TrickleDown);
            root.RegisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
        }

        static void OnPointerEnter(PointerEnterEvent e)
        {
            if (!(e.target is VisualElement element) || !Interactive(element) || !element.enabledInHierarchy) return;
            // Sweeping across a row of buttons should not rattle.
            if (Time.unscaledTime - _lastHover < 0.05f) return;
            _lastHover = Time.unscaledTime;
            Play(Sfx.UiHover);
        }

        static void OnClick(ClickEvent e)
        {
            if (!(e.target is VisualElement element)) return;
            for (VisualElement at = element; at != null; at = at.parent)
            {
                if (at is Button button)
                {
                    if (!button.enabledInHierarchy) return;
                    bool back = button.ClassListContains("back") || button.text == "Back" || button.text == "Cancel" || button.text == "Close";
                    Play(back ? Sfx.UiBack : Sfx.UiClick);
                    return;
                }
                if (at is Toggle || at is DropdownField)
                {
                    Play(Sfx.UiToggle);
                    return;
                }
            }
        }

        static bool Interactive(VisualElement element) => element is Button || element is Toggle || element is DropdownField;

        static void Create()
        {
            var go = new GameObject("Interface Sounds");
            Object.DontDestroyOnLoad(go);
            _voices = new AudioSource[VoiceCount];
            for (int i = 0; i < VoiceCount; i++)
            {
                AudioSource voice = go.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 0f;
                voice.ignoreListenerPause = true;
                _voices[i] = voice;
            }
        }
    }
}
