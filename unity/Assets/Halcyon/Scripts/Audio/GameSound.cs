// The game's sound in Unity, from the same synthesis as the web version
// (Core/Audio): the player's engine (loops pitched and crossfaded by rpm and
// load), tires on every surface, road and wind noise, turbo and intake,
// impacts and gear shifts; the engines of the nearest other cars (racers and
// traffic) in 3D; menu blips; and the radio. Engine loops and the beds are
// rendered on worker threads (Unity's audio clips are made on the main
// thread once they are ready), so a new car never stalls a frame. The radio
// renders bars on a worker thread into a queue that the audio thread plays.
//
// Unity mixes 32 real voices by default and a car alone has a dozen loops,
// so a loop that is silent is paused (it gives its voice back) and the sounds
// that must never be the one dropped come first (Unity's priority, 0 first):
// the radio, then the menus, effects and weather, the player's engine, the
// tires and wind, and last the other cars.
using System.Collections.Generic;
using System.Threading.Tasks;
using Halcyon.Core;
using Halcyon.Core.Audio;
using UnityEngine;

namespace Halcyon
{
    public sealed class GameSound : MonoBehaviour
    {
        public static GameSound Instance { get; private set; }
        const int ClipRate = 44100;
        const int RemoteVoices = 4;
        const float RemoteRange = 110;
        const int FxVoices = 4;
        const int UiPriority = 16, EnginePriority = 32, BedPriority = 48, RemotePriority = 96;
        /// <summary>The bonnet camera hears the car through the bulkhead (as the web version: 5.2 kHz inside).</summary>
        const float InsideCutoff = 5200, OpenCutoff = 22000;

        HalcyonGame game;
        readonly Dictionary<Bed, AudioClip> beds = new Dictionary<Bed, AudioClip>();
        readonly Dictionary<Shot, AudioClip[]> shots = new Dictionary<Shot, AudioClip[]>();
        readonly Dictionary<UiSound, AudioClip> uiClips = new Dictionary<UiSound, AudioClip>();
        Task<SharedSamples> sharedTask;

        /// <summary>A car model's engine, as clips (or still rendering).</summary>
        sealed class EngineClips
        {
            public double[] rpm;
            /// <summary>The rpm ladder as EngineSynth.Mix takes it.</summary>
            public EngineLoop[] ladder;
            public AudioClip[] on, off;
            public bool electric, turbo;
            public double edge, limiter, idle;
        }
        readonly Dictionary<string, EngineClips> engines = new Dictionary<string, EngineClips>();
        readonly Dictionary<string, Task<EngineSoundSet>> rendering = new Dictionary<string, Task<EngineSoundSet>>();
        /// <summary>Engines that failed to render (said once, not asked again every frame).</summary>
        readonly HashSet<string> failed = new HashSet<string>();

        sealed class SharedSamples
        {
            public readonly Dictionary<Bed, float[]> beds = new Dictionary<Bed, float[]>();
            public readonly Dictionary<Shot, float[][]> shots = new Dictionary<Shot, float[][]>();
            public readonly Dictionary<UiSound, float[]> ui = new Dictionary<UiSound, float[]>();
        }

        PlayerVoice player;
        readonly RemoteVoice[] remotes = new RemoteVoice[RemoteVoices];
        AudioSource uiSource;
        /// <summary>The effects (impacts, shifts, crackle, blow-off): a few sources, since a source's pitch is shared by everything it plays.</summary>
        readonly Layer[] fx = new Layer[FxVoices];
        readonly float[] fxUntil = new float[FxVoices];
        RadioPlayer radio;
        StationChoice playingStation = (StationChoice)(-1);
        float cutoff = OpenCutoff;
        bool wasInMenu, racingAtOpen;
        /// <summary>Unity reset its audio (a new output device): every source stopped.</summary>
        volatile bool audioReset;

        public static GameSound Create(HalcyonGame game, Transform parent)
        {
            var go = new GameObject("Sound");
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<GameSound>();
            s.game = game;
            Instance = s;
            s.uiSource = s.Source2D("UI");
            s.uiSource.priority = UiPriority;
            for (int i = 0; i < FxVoices; i++)
            {
                s.fx[i] = new Layer(go.transform, "Effects " + i, null, UiPriority, false, true);
                s.fx[i].src.loop = false;
            }
            s.player = new PlayerVoice(s, go.transform);
            for (int i = 0; i < RemoteVoices; i++) s.remotes[i] = new RemoteVoice(go.transform, i);
            s.radio = RadioPlayer.Create(go.transform);
            s.radio.OnNewSong = text => s.game?.Career?.Say(text);
            s.sharedTask = Task.Run(() =>
            {
                var r = new SharedSamples();
                foreach (Bed b in System.Enum.GetValues(typeof(Bed))) r.beds[b] = Beds.Render(b, ClipRate);
                foreach (Shot sh in System.Enum.GetValues(typeof(Shot)))
                {
                    int variants = sh == Core.Audio.Shot.Crackle || sh <= Core.Audio.Shot.ImpactHeavy ? 3 : 1;
                    var list = new float[variants][];
                    for (int v = 0; v < variants; v++) list[v] = Beds.Render(sh, ClipRate, v);
                    r.shots[sh] = list;
                }
                foreach (UiSound u in System.Enum.GetValues(typeof(UiSound))) r.ui[u] = Beds.Render(u, ClipRate);
                return r;
            });
            return s;
        }

        void OnEnable() => AudioSettings.OnAudioConfigurationChanged += OnAudioReset;

        void OnDisable() => AudioSettings.OnAudioConfigurationChanged -= OnAudioReset;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnAudioReset(bool deviceChanged) => audioReset = true;

        AudioSource Source2D(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.spatialBlend = 0;
            return a;
        }

        internal static AudioClip Clip(string name, float[] data, int channels = 1)
        {
            var c = AudioClip.Create(name, data.Length / channels, channels, ClipRate, false);
            c.SetData(data, 0);
            return c;
        }

        internal AudioClip BedClip(Bed b) => beds.TryGetValue(b, out var c) ? c : null;

        /// <summary>A car model's engine clips, or null while they render (the render starts on the first ask).</summary>
        EngineClips Engine(string specId)
        {
            if (specId == null) return null;
            if (engines.TryGetValue(specId, out var e)) return e;
            if (rendering.ContainsKey(specId) || failed.Contains(specId)) return null;
            CarSpec spec = null;
            foreach (var s in Cars.All) if (s.id == specId) spec = s;
            if (spec == null)
            {
                failed.Add(specId);
                Debug.LogWarning($"Halcyon: no car {specId} to make an engine sound for");
                return null;
            }
            rendering[specId] = Task.Run(() => EngineSynth.RenderSet(spec, ClipRate));
            return null;
        }

        /// <summary>Finished renders become clips (on the main thread, where Unity allows it).</summary>
        void Collect()
        {
            if (sharedTask != null && sharedTask.IsCompleted)
            {
                if (sharedTask.Status == TaskStatus.RanToCompletion)
                {
                    var r = sharedTask.Result;
                    foreach (var kv in r.beds) beds[kv.Key] = Clip("Bed " + kv.Key, kv.Value);
                    foreach (var kv in r.shots)
                    {
                        var list = new AudioClip[kv.Value.Length];
                        for (int i = 0; i < list.Length; i++) list[i] = Clip($"{kv.Key} {i}", kv.Value[i]);
                        shots[kv.Key] = list;
                    }
                    foreach (var kv in r.ui) uiClips[kv.Key] = Clip("UI " + kv.Key, kv.Value);
                    player.AttachBeds();
                }
                else Debug.LogWarning("Halcyon: the sound beds did not render: " + sharedTask.Exception?.GetBaseException().Message);
                sharedTask = null;
            }
            List<string> done = null;
            foreach (var kv in rendering)
            {
                if (!kv.Value.IsCompleted) continue;
                (done ??= new List<string>()).Add(kv.Key);
                if (kv.Value.Status != TaskStatus.RanToCompletion)
                {
                    failed.Add(kv.Key);
                    Debug.LogWarning($"Halcyon: the engine of {kv.Key} did not render: {kv.Value.Exception?.GetBaseException().Message}");
                    continue;
                }
                var set = kv.Value.Result;
                var e = new EngineClips
                {
                    rpm = new double[set.on.Length],
                    on = new AudioClip[set.on.Length],
                    off = new AudioClip[set.voice.electric ? 0 : set.off.Length],
                    electric = set.voice.electric,
                    turbo = set.voice.turbo,
                    edge = set.voice.edge,
                    limiter = set.voice.limiter,
                    idle = set.voice.idle,
                };
                for (int i = 0; i < set.on.Length; i++)
                {
                    e.rpm[i] = set.on[i].rpm;
                    e.on[i] = Clip($"{kv.Key} on {i}", set.on[i].data);
                }
                for (int i = 0; i < e.off.Length; i++) e.off[i] = Clip($"{kv.Key} off {i}", set.off[i].data);
                e.ladder = new EngineLoop[e.rpm.Length];
                for (int i = 0; i < e.rpm.Length; i++) e.ladder[i] = new EngineLoop { rpm = e.rpm[i] };
                engines[kv.Key] = e;
            }
            if (done != null) foreach (var k in done) rendering.Remove(k);
        }

        /// <summary>A menu or HUD sound.</summary>
        public void Ui(UiSound s)
        {
            if (uiSource != null && uiClips.TryGetValue(s, out var c)) uiSource.PlayOneShot(c, 0.6f);
        }

        void PlayShot(Shot s, float gain, float pitch = 1)
        {
            if (!shots.TryGetValue(s, out var list) || list.Length == 0) return;
            // A source that has finished (its pitch is free to change), else the one that ends first.
            float now = Time.unscaledTime;
            int pick = 0;
            for (int i = 1; i < FxVoices; i++)
                if (fxUntil[i] < fxUntil[pick]) pick = i;
            var clip = list[Random.Range(0, list.Length)];
            var a = fx[pick].src;
            a.pitch = Mathf.Clamp(pitch, 0.05f, 3f);
            a.PlayOneShot(clip, gain * Effects);
            fxUntil[pick] = Mathf.Max(fxUntil[pick], now + clip.length / a.pitch);
        }

        /// <summary>A collision: `value` is the impact impulse the simulation reports.</summary>
        public void Impact(double value)
        {
            float s = Mathf.Min(1, (float)(value / 12000));
            if (s < 0.03f) return;
            PlayShot(s > 0.6f ? Core.Audio.Shot.ImpactHeavy : s > 0.25f ? Core.Audio.Shot.ImpactMedium : Core.Audio.Shot.ImpactLight, 0.35f + 0.65f * s, 0.92f + Random.value * 0.16f);
        }

        float Effects => (float)(game.Settings?.Effects ?? 1);
        float EngineVolume => (float)(game.Settings?.Engine ?? 1);

        /// <summary>The station after this one (the radio key): off, then each station in turn.</summary>
        public StationChoice CycleStation()
        {
            var s = game.Settings;
            s.Station = (StationChoice)(((int)s.Station + 1) % 4);
            game.SaveSettings();
            return s.Station;
        }

        public static string StationName(StationChoice c) => c == StationChoice.Off ? "Radio off" : RadioStation.Stations[(int)c - 1].name;

        void Update()
        {
            if (game == null || !game.Ready) return;
            if (audioReset)
            {
                // Every source stopped: the loops start again when next heard, the radio re-tunes at the new rate.
                audioReset = false;
                player.Reset();
                foreach (var v in remotes) v.Reset();
                foreach (var f in fx) f.Reset();
                radio.Restart();
                playingStation = (StationChoice)(-1);
            }
            Collect();
            var st = game.Settings;
            // The radio follows the settings (the pause menu or the radio key).
            if (st.Station != playingStation)
            {
                playingStation = st.Station;
                radio.Play(st.Station == StationChoice.Off ? (StationId?)null : (StationId)((int)st.Station - 1), RadioPlayer.OutputRate);
            }
            radio.Volume = (float)(st.Music * st.Music) * 0.7f;
            bool paused = game.Paused || game.InMenu || game.HideHud;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            bool inside = game.CameraRig != null && game.CameraRig.Mode == CamMode.Bonnet;
            cutoff = Mathf.Lerp(cutoff, inside ? InsideCutoff : OpenCutoff, Mathf.Min(1, dt * 8));
            player.Update(game.PlayerRig, paused, inside, dt);
            player.Muffle(cutoff);
            foreach (var f in fx) f.Muffle(cutoff);
            UpdateRemotes(paused, dt);
        }

        /// <summary>Menu blips, once the menus have read this frame's keys.</summary>
        void LateUpdate()
        {
            if (game == null || !game.Ready) return;
            bool inMenu = game.InMenu;
            var m = game.Driver != null ? game.Driver.Menu : default;
            if (inMenu && !wasInMenu)
            {
                Ui(UiSound.Select);
                racingAtOpen = game.Racing;
            }
            else if (!inMenu && wasInMenu)
            {
                // Out of a menu: nothing when an event has just begun (its countdown speaks), else what closed it.
                if (!(game.Racing && !racingAtOpen)) Ui(m.select ? UiSound.Select : UiSound.Back);
            }
            else if (inMenu)
            {
                if (m.select) Ui(UiSound.Select);
                else if (m.back) Ui(UiSound.Back);
                else if (m.up || m.down || m.left || m.right) Ui(UiSound.Move);
            }
            wasInMenu = inMenu;
        }

        readonly List<(float d, CarRig rig)> near = new List<(float d, CarRig rig)>();

        int Rank(CarRig rig)
        {
            for (int i = 0; i < near.Count; i++)
                if (near[i].rig == rig) return i;
            return int.MaxValue;
        }

        /// <summary>
        /// The engines of the nearest other cars (racers, traffic), in 3D. A voice keeps its car while
        /// the car stays within 1.1 times the range and among the nearest six, so voices do not jump
        /// between cars at the edge; a car among the nearest four without a voice takes a free one, or
        /// the voice of the farthest car outside the four. Every change fades (a quarter second).
        /// </summary>
        void UpdateRemotes(bool paused, float dt)
        {
            var listener = game.CameraRig != null ? game.CameraRig.Cam.transform.position : Vector3.zero;
            float keep = RemoteRange * 1.1f;
            near.Clear();
            foreach (var rig in CarRig.All)
            {
                if (rig == null || rig == game.PlayerRig || rig.Silent || !rig.isActiveAndEnabled) continue;
                float d = (rig.transform.position - listener).sqrMagnitude;
                if (d < keep * keep) near.Add((d, rig));
            }
            near.Sort((a, b) => a.d.CompareTo(b.d));
            foreach (var v in remotes)
                if (v.Busy && !v.Releasing && Rank(v.Rig) >= 6) v.Releasing = true;
            for (int i = 0; i < near.Count && i < RemoteVoices; i++)
            {
                var (d, rig) = near[i];
                if (d > RemoteRange * RemoteRange) break;
                RemoteVoice has = null;
                foreach (var v in remotes)
                    if (v.Busy && v.Rig == rig) has = v;
                if (has != null)
                {
                    has.Releasing = false; // back among the nearest: fade in again
                    continue;
                }
                var clips = Engine(rig.SpecId);
                if (clips == null) continue;
                RemoteVoice free = null, farthest = null;
                int farthestRank = RemoteVoices - 1;
                foreach (var v in remotes)
                {
                    if (!v.Busy)
                    {
                        free = v;
                        break;
                    }
                    int r = Rank(v.Rig);
                    if (r > farthestRank)
                    {
                        farthestRank = r;
                        farthest = v;
                    }
                }
                if (free != null) free.Assign(rig, clips);
                else if (farthest != null) farthest.Releasing = true; // it fades out, then takes this car
            }
            foreach (var v in remotes) v.Update(paused, EngineVolume * Effects, dt);
        }

        static float Smoothstep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3 - 2 * t);
        }

        // ------------------------------------------------------------------------------------------
        /// <summary>
        /// One source: a loop that pauses while silent (it gives its voice back and costs nothing)
        /// and plays on when heard again, or a one-shot source. The player's sources carry their own
        /// low-pass filter for the bonnet camera (Unity filters only the source on their own object).
        /// </summary>
        sealed class Layer
        {
            public readonly AudioSource src;
            readonly AudioLowPassFilter muffle;
            bool started, paused;

            public Layer(Transform parent, string name, AudioClip clip, int priority, bool spatial, bool muffled)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                src = go.AddComponent<AudioSource>();
                src.clip = clip;
                src.loop = true;
                src.playOnAwake = false;
                src.volume = 0;
                src.priority = priority;
                src.spatialBlend = spatial ? 1 : 0;
                src.dopplerLevel = spatial ? 0.6f : 0;
                if (muffled)
                {
                    muffle = go.AddComponent<AudioLowPassFilter>();
                    muffle.cutoffFrequency = OpenCutoff;
                    muffle.enabled = false;
                }
            }

            /// <summary>This frame's volume and pitch (the pitch is left alone while silent).</summary>
            public void Set(float volume, float pitch)
            {
                if (volume <= 0.0005f)
                {
                    src.volume = 0;
                    if (started && !paused)
                    {
                        src.Pause();
                        paused = true;
                    }
                    return;
                }
                src.volume = volume;
                src.pitch = Mathf.Clamp(pitch, 0.05f, 3f);
                if (!started)
                {
                    // Each loop starts somewhere along its clip, so loops that start together are not in phase.
                    src.time = Random.value * src.clip.length * 0.9f;
                    src.Play();
                    started = true;
                    paused = false;
                }
                else if (paused)
                {
                    src.UnPause();
                    paused = false;
                }
            }

            /// <summary>The low-pass for the bonnet camera: off (no cost) when the car is heard from outside.</summary>
            public void Muffle(float hz)
            {
                if (muffle == null) return;
                bool use = hz < OpenCutoff * 0.9f;
                if (muffle.enabled != use) muffle.enabled = use;
                if (use) muffle.cutoffFrequency = hz;
            }

            /// <summary>After Unity's audio reset every source has stopped: start again when next heard.</summary>
            public void Reset()
            {
                started = false;
                paused = false;
            }

            public void Destroy() => Object.Destroy(src.gameObject);
        }

        // ------------------------------------------------------------------------------------------
        /// <summary>The player's car: every engine loop and the beds, in 2D.</summary>
        sealed class PlayerVoice
        {
            readonly GameSound owner;
            readonly Transform root;
            readonly List<Layer> on = new List<Layer>(), off = new List<Layer>();
            readonly Dictionary<Bed, Layer> bedLayers = new Dictionary<Bed, Layer>();
            EngineClips clips;
            string spec;
            float load, lastThrottle, crackleClock;
            int lastGear = 1;

            public PlayerVoice(GameSound owner, Transform parent)
            {
                this.owner = owner;
                root = new GameObject("Player car").transform;
                root.SetParent(parent, false);
            }

            public void AttachBeds()
            {
                foreach (Bed b in System.Enum.GetValues(typeof(Bed)))
                {
                    var c = owner.BedClip(b);
                    if (c != null && !bedLayers.ContainsKey(b)) bedLayers[b] = new Layer(root, "Bed " + b, c, BedPriority, false, true);
                }
            }

            void SetEngine(EngineClips e)
            {
                foreach (var l in on) l.Destroy();
                foreach (var l in off) l.Destroy();
                on.Clear();
                off.Clear();
                clips = e;
                for (int i = 0; i < e.on.Length; i++) on.Add(new Layer(root, "Engine on " + i, e.on[i], EnginePriority, false, true));
                for (int i = 0; i < e.off.Length; i++) off.Add(new Layer(root, "Engine off " + i, e.off[i], EnginePriority, false, true));
            }

            public void Muffle(float hz)
            {
                foreach (var l in on) l.Muffle(hz);
                foreach (var l in off) l.Muffle(hz);
                foreach (var l in bedLayers.Values) l.Muffle(hz);
            }

            public void Reset()
            {
                foreach (var l in on) l.Reset();
                foreach (var l in off) l.Reset();
                foreach (var l in bedLayers.Values) l.Reset();
            }

            void Bed(Bed b, float vol, float pitch)
            {
                if (bedLayers.TryGetValue(b, out var l)) l.Set(vol * owner.Effects, pitch);
            }

            public void Update(CarRig rig, bool paused, bool inside, float dt)
            {
                if (rig == null) return;
                if (rig.SpecId != spec)
                {
                    var e = owner.Engine(rig.SpecId);
                    if (e != null)
                    {
                        spec = rig.SpecId;
                        SetEngine(e);
                    }
                }
                var v = rig.Vehicle;
                float k = paused ? 0 : 1;
                float rpm = (float)System.Math.Max(0, v.Rpm);
                float thr = (float)v.throttleEff;
                load += ((thr > 0.05f ? Mathf.Min(1, 0.25f + thr) : 0) - load) * Mathf.Min(1, dt / 0.06f);
                if (clips != null && on.Count > 0)
                {
                    float limiter = (float)clips.limiter;
                    float level = k * owner.EngineVolume * owner.Effects * (clips.electric ? Mathf.Min(1, 0.15f + Mathf.Abs(thr) * 0.5f + (float)v.speed / 60) : 0.4f + 0.6f * Mathf.Pow(Mathf.Min(1.05f, rpm / limiter), 0.8f));
                    var mix = EngineSynth.Mix(clips.ladder, Mathf.Max(rpm, clips.electric ? 1 : (float)clips.idle * 0.6f));
                    float a = Mathf.Cos((float)mix.t * Mathf.PI * 0.5f), b = Mathf.Sin((float)mix.t * Mathf.PI * 0.5f);
                    void Both(List<Layer> layers, float g)
                    {
                        for (int i = 0; i < layers.Count; i++)
                        {
                            float w = i == mix.lo ? (mix.lo == mix.hi ? 1 : a) : i == mix.hi ? b : 0;
                            float rate = (float)(i == mix.lo ? mix.rateLo : i == mix.hi ? mix.rateHi : 1);
                            layers[i].Set(w * g * level, rate);
                        }
                    }
                    if (clips.electric) Both(on, 1);
                    else
                    {
                        Both(on, Mathf.Sqrt(load));
                        Both(off, Mathf.Sqrt(1 - load) * 0.8f);
                    }
                    // (Set every frame, to zero for a car without one: a whistle must not outlive a car change.)
                    Bed(Core.Audio.Bed.Turbo, clips.turbo ? k * 0.05f * (float)v.boost * (0.3f + 0.7f * thr) : 0, 0.5f + 0.7f * (float)v.boost);
                    if (clips.turbo)
                    {
                        if (lastThrottle > 0.6f && thr < 0.15f && v.boost > 0.5 && k > 0) owner.PlayShot(Core.Audio.Shot.Blowoff, 0.4f * (float)v.boost);
                    }
                    if (k > 0 && !clips.electric && thr < 0.05f && rpm > limiter * 0.55f && clips.edge > 0.5)
                    {
                        crackleClock -= dt;
                        if (crackleClock <= 0)
                        {
                            crackleClock = 0.25f + Random.value * 0.9f;
                            if (Random.value < 0.55f) owner.PlayShot(Core.Audio.Shot.Crackle, 0.22f + 0.25f * (float)clips.edge, 0.9f + Random.value * 0.2f);
                        }
                    }
                    Bed(Core.Audio.Bed.Intake, clips.electric ? 0 : k * 0.2f * thr * Mathf.Min(1, rpm / limiter), 0.6f + 1.1f * Mathf.Min(1, rpm / limiter));
                }
                else
                {
                    // Between cars (the next engine still rendering): no turbo or intake left hanging.
                    Bed(Core.Audio.Bed.Turbo, 0, 1);
                    Bed(Core.Audio.Bed.Intake, 0, 1);
                }
                lastThrottle = thr;
                if (k > 0 && v.gear != lastGear && v.gear > 0 && lastGear > 0) owner.PlayShot(Core.Audio.Shot.Shift, 0.3f);
                lastGear = v.gear;

                // Tires, road and wind.
                float speed = (float)v.speed;
                float squeal = 0, slideRate = 0, road = 0, gravel = 0, grass = 0, snow = 0, spray = 0;
                foreach (var w in v.wheels)
                {
                    if (!w.grounded) continue;
                    var s = Surfaces.All[w.surface];
                    float roll = Mathf.Min(1, speed / 30);
                    float slipK = Smoothstep(0.95f, 2.2f, (float)w.slip) * Smoothstep(2, 6, (float)w.slideSpeed);
                    if (w.surface == Surface.Water || w.water > 0.05) spray += 0.25f * Mathf.Min(1, speed / 12);
                    if (s.skid)
                    {
                        squeal += 0.25f * slipK;
                        slideRate += 0.25f * Mathf.Min(1, (float)w.slideSpeed / 18);
                        road += 0.25f * roll;
                    }
                    else if (w.surface == Surface.Grass) grass += 0.25f * Mathf.Min(1, roll + slipK);
                    else if (w.surface == Surface.Snow || w.surface == Surface.Ice) snow += 0.25f * Mathf.Min(1, roll + slipK);
                    else gravel += 0.25f * Mathf.Min(1, roll * 0.9f + slipK * 0.6f);
                    if (s.grip == GripClass.Wet || v.wetness > 0.2) spray += 0.25f * Mathf.Min(1, speed / 25) * (s.grip == GripClass.Wet ? 1 : (float)v.wetness);
                }
                Bed(Core.Audio.Bed.Squeal, k * 0.5f * Mathf.Min(1, squeal), 0.88f + 0.2f * slideRate);
                Bed(Core.Audio.Bed.Road, k * 0.32f * Mathf.Pow(road, 1.2f), 0.55f + Mathf.Min(1.2f, speed / 45));
                Bed(Core.Audio.Bed.Gravel, k * 0.42f * gravel, 0.7f + Mathf.Min(1, speed / 40));
                Bed(Core.Audio.Bed.Grass, k * 0.3f * grass, 0.8f + Mathf.Min(0.6f, speed / 50));
                Bed(Core.Audio.Bed.Snow, k * 0.35f * snow, 0.8f + Mathf.Min(0.6f, speed / 50));
                Bed(Core.Audio.Bed.Spray, k * 0.3f * Mathf.Min(1, spray), 0.8f + Mathf.Min(0.5f, speed / 60));
                float wind = Mathf.Min(1, Mathf.Pow(speed / 70, 2));
                Bed(Core.Audio.Bed.Wind, k * 0.45f * wind * (inside ? 0.6f : 1), 0.6f + Mathf.Min(1, speed / 80));
            }
        }

        // ------------------------------------------------------------------------------------------
        /// <summary>Another car's engine in 3D: its on-throttle loops, crossfaded by rpm, fading in and out.</summary>
        sealed class RemoteVoice
        {
            /// <summary>Full near the car, fading to nothing at the range (a voice let go there is already silent).</summary>
            static AnimationCurve rolloff;
            static AnimationCurve Rolloff => rolloff ??= new AnimationCurve(
                new Keyframe(0, 1), new Keyframe(0.06f, 1), new Keyframe(0.15f, 0.45f), new Keyframe(0.35f, 0.15f), new Keyframe(0.7f, 0.04f), new Keyframe(1, 0));

            readonly Transform root;
            readonly List<Layer> loops = new List<Layer>();
            EngineClips clips;
            CarRig rig;
            Vector3 lastPos;
            float lastRpm, lastThrottle, fade;
            /// <summary>On a car (or still fading out from one).</summary>
            public bool Busy { get; private set; }
            /// <summary>Letting go of its car: it fades out, then it is free.</summary>
            public bool Releasing;
            public CarRig Rig => rig;

            public RemoteVoice(Transform parent, int i)
            {
                root = new GameObject("Car voice " + i).transform;
                root.SetParent(parent, false);
            }

            public void Assign(CarRig r, EngineClips c)
            {
                rig = r;
                Busy = true;
                Releasing = false;
                fade = 0;
                lastPos = r.transform.position;
                root.position = lastPos;
                if (c == clips) return;
                foreach (var l in loops) l.Destroy();
                loops.Clear();
                clips = c;
                for (int i = 0; i < c.on.Length; i++)
                {
                    var l = new Layer(root, "Loop " + i, c.on[i], RemotePriority, true, false);
                    l.src.rolloffMode = AudioRolloffMode.Custom;
                    l.src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Rolloff);
                    l.src.maxDistance = RemoteRange;
                    loops.Add(l);
                }
            }

            public void Reset()
            {
                foreach (var l in loops) l.Reset();
            }

            public void Update(bool paused, float volume, float dt)
            {
                if (!Busy) return;
                // A car taken away (traffic despawned, a racer gone): fade out where it was.
                bool gone = rig == null;
                if (gone) Releasing = true;
                fade = Mathf.MoveTowards(fade, Releasing ? 0 : 1, dt * 4);
                if (Releasing && fade <= 0)
                {
                    foreach (var l in loops) l.Set(0, 1);
                    Busy = false;
                    Releasing = false;
                    rig = null;
                    return;
                }
                if (!gone)
                {
                    var v = rig.Vehicle;
                    lastPos = rig.transform.position;
                    lastRpm = (float)v.Rpm;
                    lastThrottle = Mathf.Clamp01((float)v.throttleEff);
                }
                root.position = lastPos;
                float rpm = Mathf.Max(lastRpm, clips.electric ? 1 : (float)clips.idle * 0.6f);
                var mix = EngineSynth.Mix(clips.ladder, rpm);
                float a0 = Mathf.Cos((float)mix.t * Mathf.PI * 0.5f), b0 = Mathf.Sin((float)mix.t * Mathf.PI * 0.5f);
                float level = paused ? 0 : fade * volume * (0.35f + 0.65f * lastThrottle) * (0.5f + 0.5f * Mathf.Min(1, rpm / (float)clips.limiter));
                for (int i = 0; i < loops.Count; i++)
                {
                    float w = i == mix.lo ? (mix.lo == mix.hi ? 1 : a0) : i == mix.hi ? b0 : 0;
                    loops[i].Set(w * level, (float)(i == mix.lo ? mix.rateLo : mix.rateHi));
                }
            }
        }
    }
}
