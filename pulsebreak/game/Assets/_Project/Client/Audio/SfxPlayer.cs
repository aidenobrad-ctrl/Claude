using UnityEngine;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.Audio
{
    /// <summary>
    /// Plays sound effects for the camera it sits on. Sounds of our own (our gun, our
    /// footsteps, a hit marker) play flat, in both ears. Sounds out in the world (someone
    /// else's shot, their footsteps, an explosion) play from where they happened: quieter and
    /// duller with distance, and muffled when a wall is in the way, so you can tell where a
    /// fight is and whether it is in the open.
    ///
    /// Each sound's volume is its own times its slider (effects or interface); the master
    /// slider applies to everything through the listener.
    /// </summary>
    public sealed class SfxPlayer : MonoBehaviour
    {
        const int FlatVoiceCount = 10;
        const int WorldVoiceCount = 20;
        /// <summary>Beyond this, anything in the world is too far to hear.</summary>
        const float HearingRange = 160f;

        /// <summary>The player for the camera in use, for code that has no rig to hand.</summary>
        public static SfxPlayer Current { get; private set; }

        struct WorldVoice
        {
            public AudioSource Source;
            public AudioLowPassFilter Filter;
            public float StartedAt;
        }

        AudioSource[] _flat;
        WorldVoice[] _world;
        int _nextFlat;

        void Awake()
        {
            _flat = new AudioSource[FlatVoiceCount];
            for (int i = 0; i < FlatVoiceCount; i++)
            {
                AudioSource voice = gameObject.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 0f;
                _flat[i] = voice;
            }

            // The world voices live outside the camera, each moved to where its sound is.
            var holder = new GameObject("World Sounds").transform;
            holder.SetParent(transform.parent, false);
            _world = new WorldVoice[WorldVoiceCount];
            for (int i = 0; i < WorldVoiceCount; i++)
            {
                var go = new GameObject("Voice");
                go.transform.SetParent(holder, false);
                AudioSource source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.dopplerLevel = 0f;
                source.spread = 0f;
                source.maxDistance = HearingRange;
                _world[i].Source = source;
                _world[i].Filter = go.AddComponent<AudioLowPassFilter>();
                _world[i].Filter.cutoffFrequency = 22000f;
            }
            SoundBank.Prewarm();
        }

        void OnEnable() => Current = this;

        void OnDisable()
        {
            if (Current == this) Current = null;
        }

        void OnDestroy()
        {
            if (_world != null && _world.Length > 0 && _world[0].Source != null) Destroy(_world[0].Source.transform.parent.gameObject);
        }

        // ------------------------------------------------------------------ flat

        /// <param name="pitchVariation">Random pitch change either way, as a fraction. Stops repeats sounding identical.</param>
        public void Play(Sfx sfx, float volume = 1f, float pitchVariation = 0f) =>
            PlayFlat(SoundBank.Get(sfx), volume * SoundBank.Volume(SoundBank.BusOf(sfx)), pitchVariation);

        /// <summary>Our own weapon firing.</summary>
        public void PlayShot(WeaponBuild build, float volumeScale = 1f) =>
            PlayFlat(SoundBank.Shot(build), build.Weapon.audio.volume * (build.Suppressed ? 0.55f : 1f) * volumeScale * SoundBank.Volume(AudioBus.Effects), 0.04f);

        void PlayFlat(AudioClip clip, float volume, float pitchVariation)
        {
            if (clip == null || volume <= 0.001f) return;
            AudioSource voice = _flat[_nextFlat];
            _nextFlat = (_nextFlat + 1) % FlatVoiceCount;
            voice.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            voice.clip = clip;
            voice.volume = Mathf.Clamp01(volume);
            voice.Play();
        }

        // ------------------------------------------------------------------ in the world

        /// <summary>A sound that happened somewhere in the world.</summary>
        /// <param name="near">Within this distance it is at full volume.</param>
        /// <param name="far">Footsteps and the like: gone completely by this distance. 0 for the long, natural falloff of loud sounds.</param>
        public void PlayAt(Sfx sfx, Vector3 position, float volume = 1f, float pitchVariation = 0f, float near = 2f, float far = 0f) =>
            PlayWorld(SoundBank.Get(sfx), position, volume * SoundBank.Volume(SoundBank.BusOf(sfx)), pitchVariation, near, far);

        /// <summary>Someone else's weapon firing, heard from where they stand.</summary>
        public void PlayShotAt(WeaponBuild build, Vector3 position)
        {
            float volume = build.Weapon.audio.volume * (build.Suppressed ? 0.5f : 1f) * SoundBank.Volume(AudioBus.Effects);
            // A suppressed weapon carries much less far.
            PlayWorld(SoundBank.Shot(build), position, volume, 0.04f, build.Suppressed ? 2f : 5f, build.Suppressed ? 45f : 0f);
        }

        public void PlayWorld(AudioClip clip, Vector3 position, float volume, float pitchVariation, float near, float far)
        {
            if (clip == null || volume <= 0.001f) return;
            Vector3 listener = transform.position;
            float distance = Vector3.Distance(listener, position);
            if (distance > (far > 0f ? far : HearingRange)) return;

            ref WorldVoice voice = ref _world[PickVoice()];
            AudioSource source = voice.Source;
            source.transform.position = position;
            source.clip = clip;
            source.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            source.minDistance = near;
            if (far > 0f)
            {
                source.rolloffMode = AudioRolloffMode.Linear;
                source.maxDistance = far;
            }
            else
            {
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.maxDistance = HearingRange;
            }

            // Distance takes the top off a sound; a wall takes most of it.
            float cutoff = Mathf.Lerp(22000f, 4500f, Mathf.Clamp01(distance / 120f));
            if (distance > 1.5f && Physics.Linecast(listener, position, GameLayers.WorldMask, QueryTriggerInteraction.Ignore))
            {
                cutoff = Mathf.Min(cutoff, 1100f);
                volume *= 0.6f;
            }
            voice.Filter.cutoffFrequency = cutoff;
            source.volume = Mathf.Clamp01(volume);
            voice.StartedAt = Time.unscaledTime;
            source.Play();
        }

        /// <summary>A voice that is free, or else the one that has been playing longest.</summary>
        int PickVoice()
        {
            int oldest = 0;
            for (int i = 0; i < _world.Length; i++)
            {
                if (!_world[i].Source.isPlaying) return i;
                if (_world[i].StartedAt < _world[oldest].StartedAt) oldest = i;
            }
            return oldest;
        }
    }
}
