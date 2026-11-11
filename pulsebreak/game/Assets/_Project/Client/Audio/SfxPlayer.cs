using System;
using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.Audio
{
    public enum Sfx
    {
        Hit,
        Headshot,
        Kill,
        DryFire,
        ReloadStart,
        ReloadEnd,
        RoundIn,
        Swap,
        Jump,
        Land,
        Slide,
        Mantle,
        Explosion,
        Bounce,
        Throw,
    }

    /// <summary>
    /// Plays the game's placeholder sound effects. The clips are synthesised at startup
    /// (see <see cref="SfxSynth"/>), so the project needs no audio files until real sound
    /// design arrives; swapping in recorded clips later only touches this class.
    /// </summary>
    public sealed class SfxPlayer : MonoBehaviour
    {
        const int VoiceCount = 10;

        readonly Dictionary<string, AudioClip> _shotClips = new Dictionary<string, AudioClip>();
        AudioSource[] _voices;
        AudioClip[] _clips;
        int _nextVoice;

        void Awake()
        {
            _voices = new AudioSource[VoiceCount];
            for (int i = 0; i < VoiceCount; i++)
            {
                AudioSource voice = gameObject.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 0f;
                _voices[i] = voice;
            }

            _clips = new AudioClip[Enum.GetValues(typeof(Sfx)).Length];
            _clips[(int)Sfx.Hit] = SfxSynth.Hit();
            _clips[(int)Sfx.Headshot] = SfxSynth.Headshot();
            _clips[(int)Sfx.Kill] = SfxSynth.Kill();
            _clips[(int)Sfx.DryFire] = SfxSynth.Click("DryFire", 900f, 0.04f, 0.35f);
            _clips[(int)Sfx.ReloadStart] = SfxSynth.Click("ReloadStart", 320f, 0.12f, 0.5f);
            _clips[(int)Sfx.ReloadEnd] = SfxSynth.DoubleClick("ReloadEnd", 520f, 0.2f, 0.5f);
            _clips[(int)Sfx.RoundIn] = SfxSynth.Click("RoundIn", 640f, 0.06f, 0.45f);
            _clips[(int)Sfx.Swap] = SfxSynth.Whoosh("Swap", 0.16f, 0.16f);
            _clips[(int)Sfx.Jump] = SfxSynth.Thump("Jump", 0.1f, 45f, 0.25f, 0f);
            _clips[(int)Sfx.Land] = SfxSynth.Thump("Land", 0.16f, 28f, 0.6f, 0.5f);
            _clips[(int)Sfx.Slide] = SfxSynth.Whoosh("Slide", 0.45f, 0.3f);
            _clips[(int)Sfx.Mantle] = SfxSynth.Whoosh("Mantle", 0.2f, 0.2f);
            _clips[(int)Sfx.Explosion] = SfxSynth.Explosion();
            _clips[(int)Sfx.Bounce] = SfxSynth.Click("Bounce", 1200f, 0.05f, 0.3f);
            _clips[(int)Sfx.Throw] = SfxSynth.Whoosh("Throw", 0.22f, 0.3f);
        }

        /// <param name="pitchVariation">Random pitch change either way, as a fraction. Stops repeats sounding identical.</param>
        public void Play(Sfx sfx, float volume = 1f, float pitchVariation = 0f) =>
            PlayClip(_clips[(int)sfx], volume, pitchVariation);

        /// <summary>The firing sound of a weapon, built from its audio profile the first time it is needed.</summary>
        /// <param name="volumeScale">Below 1 for someone else's shot heard from a distance.</param>
        public void PlayShot(WeaponBuild build, float volumeScale = 1f)
        {
            bool suppressed = build.Suppressed;
            string key = build.Weapon.weaponId + (suppressed ? "|s" : "");
            if (!_shotClips.TryGetValue(key, out AudioClip clip))
            {
                clip = SfxSynth.Shot(build.Weapon.audio, suppressed, build.Weapon.weaponId);
                _shotClips[key] = clip;
            }
            PlayClip(clip, build.Weapon.audio.volume * (suppressed ? 0.55f : 1f) * volumeScale, 0.04f);
        }

        void PlayClip(AudioClip clip, float volume, float pitchVariation)
        {
            if (clip == null) return;

            AudioSource voice = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % VoiceCount;
            voice.pitch = 1f + UnityEngine.Random.Range(-pitchVariation, pitchVariation);
            voice.clip = clip;
            voice.volume = Mathf.Clamp01(volume);
            voice.Play();
        }
    }
}
