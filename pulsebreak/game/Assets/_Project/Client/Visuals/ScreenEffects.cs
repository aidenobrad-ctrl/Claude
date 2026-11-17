using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Pulsebreak.Client.Settings;

namespace Pulsebreak.Client.Visuals
{
    /// <summary>
    /// The finishing pass on the picture, as one global post-processing volume made in code:
    /// gentle bloom so muzzle flashes, energy and fire glow, a neutral tone curve, a touch of
    /// contrast and a soft vignette. On top of that it shows how the player is doing: a red
    /// edge and a jolt of colour fringing when hit, a lasting red edge and drained colour on
    /// low health, and grey when out.
    /// </summary>
    public sealed class ScreenEffects : MonoBehaviour
    {
        static ScreenEffects _instance;

        Volume _volume;
        VolumeProfile _profile;
        Bloom _bloom;
        ColorAdjustments _color;
        Vignette _vignette;
        ChromaticAberration _fringe;
        float _hurt, _lowHealth, _lowHealthTarget, _out, _outTarget;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        /// <summary>Makes the volume if there is none yet. It lasts for the whole session.</summary>
        public static ScreenEffects Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("Screen Effects");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ScreenEffects>();
            return _instance;
        }

        /// <summary>A hit landed on us. <paramref name="amount"/> is roughly the share of health it took.</summary>
        public static void Hurt(float amount)
        {
            if (_instance == null) return;
            _instance._hurt = Mathf.Clamp01(_instance._hurt + 0.35f + amount * 1.5f);
        }

        /// <summary>Our health as a share of full, and whether we are in play at all.</summary>
        public static void SetHealth(float fraction, bool alive)
        {
            if (_instance == null) return;
            _instance._lowHealthTarget = alive ? Mathf.Clamp01((0.4f - fraction) / 0.3f) : 0f;
            _instance._outTarget = alive ? 0f : 1f;
        }

        /// <summary>Back to a calm picture: in the menu, or on a new spawn.</summary>
        public static void Calm()
        {
            if (_instance == null) return;
            _instance._hurt = 0f;
            _instance._lowHealthTarget = _instance._lowHealth = 0f;
            _instance._outTarget = _instance._out = 0f;
        }

        void Awake()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "Pulsebreak Screen";

            _bloom = _profile.Add<Bloom>(true);
            _bloom.threshold.Override(1f);
            _bloom.intensity.Override(0.6f);
            _bloom.scatter.Override(0.65f);

            Tonemapping tone = _profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);

            _color = _profile.Add<ColorAdjustments>(true);
            _color.contrast.Override(10f);
            _color.saturation.Override(6f);

            _vignette = _profile.Add<Vignette>(true);
            _vignette.intensity.Override(0.2f);
            _vignette.smoothness.Override(0.45f);
            _vignette.color.Override(Color.black);

            _fringe = _profile.Add<ChromaticAberration>(true);
            _fringe.intensity.Override(0f);

            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 10f;
            _volume.sharedProfile = _profile;
            ApplyQuality(GameSettings.Quality);
        }

        void OnDestroy()
        {
            if (_profile != null) Destroy(_profile);
        }

        public void ApplyQuality(GraphicsQuality quality)
        {
            _bloom.active = quality >= GraphicsQuality.Medium;
            _bloom.highQualityFiltering.Override(quality >= GraphicsQuality.Ultra);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _hurt = Mathf.MoveTowards(_hurt, 0f, dt * 1.8f);
            _lowHealth = Mathf.MoveTowards(_lowHealth, _lowHealthTarget, dt * 2f);
            _out = Mathf.MoveTowards(_out, _outTarget, dt * (_outTarget > _out ? 1.5f : 4f));

            // On low health the red edge beats in time with the heartbeat.
            float beat = _lowHealth > 0f ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.Lerp(6f, 10f, _lowHealth)) : 0f;
            float red = Mathf.Clamp01(_hurt + _lowHealth * (0.55f + 0.25f * beat));
            _vignette.color.Override(Color.Lerp(Color.black, new Color(0.55f, 0f, 0.02f), red));
            _vignette.intensity.Override(0.2f + 0.22f * red + 0.1f * _out);
            _fringe.intensity.Override(Mathf.Clamp01(_hurt * 0.6f));
            _color.saturation.Override(6f - 40f * _lowHealth - 70f * _out);
        }
    }
}
