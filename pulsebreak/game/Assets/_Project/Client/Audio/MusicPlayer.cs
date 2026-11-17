using System;
using UnityEngine;
using Pulsebreak.Client.Settings;

namespace Pulsebreak.Client.Audio
{
    /// <summary>A short musical phrase for a moment in a match.</summary>
    public enum Stinger
    {
        MatchStart,
        RoundWon,
        RoundLost,
        Victory,
        Defeat,
        LevelUp,
    }

    /// <summary>
    /// The game's music: the menu piece (see <see cref="MusicSynth"/>), which fades out when a
    /// match starts so footsteps can be heard and back in on return to the menu, and the
    /// stingers that mark a match's big moments. Both follow the music volume. One object,
    /// kept across scenes, made the first time anything asks for music.
    /// </summary>
    public sealed class MusicPlayer : MonoBehaviour
    {
        /// <summary>How loud the menu piece sits at full music volume, leaving room for the interface.</summary>
        const float MenuLevel = 0.6f;
        const float FadeSeconds = 1.6f;

        static MusicPlayer _instance;

        MusicSynth _synth;
        AudioSource _carrier, _stingers;
        AudioClip[] _stingerClips;
        int _rate;
        bool _menu;
        // Written on the main thread, read on the audio thread.
        volatile float _target;
        float _gain;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        /// <summary>Plays the menu music, or fades it out.</summary>
        public static void SetMenu(bool playing)
        {
            if (Application.isBatchMode) return;
            Instance._menu = playing;
        }

        public static void Play(Stinger stinger)
        {
            if (Application.isBatchMode) return;
            MusicPlayer player = Instance;
            int index = (int)stinger;
            if (player._stingerClips[index] == null) player._stingerClips[index] = SfxSynth.Stinger(stinger);
            player._stingers.volume = Mathf.Clamp01(GameSettings.MusicVolume);
            player._stingers.PlayOneShot(player._stingerClips[index]);
        }

        static MusicPlayer Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("Music");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<MusicPlayer>();
                }
                return _instance;
            }
        }

        void Awake()
        {
            _rate = AudioSettings.outputSampleRate;
            _synth = new MusicSynth(_rate);
            _stingerClips = new AudioClip[Enum.GetValues(typeof(Stinger)).Length];

            // The music is written straight into this source's audio by OnAudioFilterRead.
            // A silent looping clip keeps the source playing for it to write into.
            _carrier = gameObject.AddComponent<AudioSource>();
            _carrier.clip = AudioClip.Create("Silence", _rate, 2, _rate, false);
            _carrier.loop = true;
            _carrier.spatialBlend = 0f;
            _carrier.priority = 0;
            _carrier.ignoreListenerPause = true;
            _carrier.Play();

            // Stingers on an object of their own, so the music filter does not touch them.
            var stingers = new GameObject("Stingers");
            stingers.transform.SetParent(transform, false);
            _stingers = stingers.AddComponent<AudioSource>();
            _stingers.playOnAwake = false;
            _stingers.spatialBlend = 0f;
            _stingers.priority = 1;
            _stingers.ignoreListenerPause = true;
        }

        void Update()
        {
            float level = _menu ? GameSettings.MusicVolume * MenuLevel : 0f;
            // Make room for a stinger.
            if (_stingers.isPlaying) level *= 0.35f;
            _target = level;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float target = _target;
            float gain = _gain;
            if (target <= 0f && gain <= 0.0001f)
            {
                Array.Clear(data, 0, data.Length);
                _gain = 0f;
                return;
            }

            _synth.Render(data, channels);
            float step = 1f / (_rate * FadeSeconds);
            int frames = data.Length / channels;
            for (int frame = 0; frame < frames; frame++)
            {
                gain = gain < target ? Math.Min(target, gain + step) : Math.Max(target, gain - step);
                for (int c = 0; c < channels; c++) data[frame * channels + c] *= gain;
            }
            _gain = gain;
        }
    }
}
