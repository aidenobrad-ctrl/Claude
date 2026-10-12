// The game's sound in Unity, from the same synthesis as the web version
// (Core/Audio): the player's engine (loops pitched and crossfaded by rpm and
// load), tires on every surface, road and wind noise, turbo and intake,
// impacts and gear shifts; the engines of the nearest other cars (racers and
// traffic) in 3D; menu blips; and the radio. Engine loops and the beds are
// rendered on worker threads (Unity's audio clips are made on the main
// thread once they are ready), so a new car never stalls a frame. The radio
// renders bars on a worker thread into a queue that the audio thread plays.
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

        sealed class SharedSamples
        {
            public readonly Dictionary<Bed, float[]> beds = new Dictionary<Bed, float[]>();
            public readonly Dictionary<Shot, float[][]> shots = new Dictionary<Shot, float[][]>();
            public readonly Dictionary<UiSound, float[]> ui = new Dictionary<UiSound, float[]>();
        }

        PlayerVoice player;
        readonly RemoteVoice[] remotes = new RemoteVoice[RemoteVoices];
        AudioSource uiSource, fxSource;
        RadioPlayer radio;
        StationChoice playingStation = (StationChoice)(-1);

        public static GameSound Create(HalcyonGame game, Transform parent)
        {
            var go = new GameObject("Sound");
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<GameSound>();
            s.game = game;
            Instance = s;
            s.uiSource = s.Source2D("UI");
            s.fxSource = s.Source2D("Effects");
            s.player = new PlayerVoice(s, go.transform);
            for (int i = 0; i < RemoteVoices; i++) s.remotes[i] = new RemoteVoice(s, go.transform, i);
            s.radio = RadioPlayer.Create(go.transform);
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
            if (engines.TryGetValue(specId, out var e)) return e;
            if (!rendering.ContainsKey(specId))
            {
                CarSpec spec = null;
                foreach (var s in Cars.All) if (s.id == specId) spec = s;
                if (spec == null) return null;
                rendering[specId] = Task.Run(() => EngineSynth.RenderSet(spec, ClipRate));
            }
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
            if (uiClips.TryGetValue(s, out var c)) uiSource.PlayOneShot(c, 0.6f);
        }

        void PlayShot(Shot s, float gain, float pitch = 1)
        {
            if (!shots.TryGetValue(s, out var list) || list.Length == 0) return;
            fxSource.pitch = pitch;
            fxSource.PlayOneShot(list[Random.Range(0, list.Length)], gain * Effects);
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
            Collect();
            var st = game.Settings;
            // The radio follows the settings (the pause menu or the radio key).
            if (st.Station != playingStation)
            {
                playingStation = st.Station;
                radio.Play(st.Station == StationChoice.Off ? (StationId?)null : (StationId)((int)st.Station - 1), AudioSettings.outputSampleRate);
            }
            radio.Volume = (float)(st.Music * st.Music) * 0.7f;
            radio.OnNewSong = text => game.Career.Say(text);
            // Menu blips for every menu, from the menu keys.
            var m = game.Driver?.Menu ?? default;
            if (game.InMenu)
            {
                if (m.up || m.down || m.left || m.right) Ui(UiSound.Move);
                if (m.select) Ui(UiSound.Select);
                if (m.back) Ui(UiSound.Back);
            }
            bool paused = game.Paused || game.InMenu || game.HideHud;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            player.Update(game.PlayerRig, paused, game.CameraRig != null && game.CameraRig.Mode == CamMode.Bonnet, dt);
            UpdateRemotes(paused);
        }

        readonly List<(float d, CarRig rig)> near = new List<(float d, CarRig rig)>();
        readonly HashSet<CarRig> wanted = new HashSet<CarRig>();

        /// <summary>The engines of the nearest other cars (racers, traffic), in 3D.</summary>
        void UpdateRemotes(bool paused)
        {
            var listener = game.CameraRig?.Cam.transform.position ?? Vector3.zero;
            near.Clear();
            foreach (var rig in CarRig.All)
            {
                if (rig == null || rig == game.PlayerRig || rig.Silent || !rig.isActiveAndEnabled) continue;
                float d = (rig.transform.position - listener).sqrMagnitude;
                if (d < RemoteRange * RemoteRange) near.Add((d, rig));
            }
            near.Sort((a, b) => a.d.CompareTo(b.d));
            // Keep voices on cars that are still among the nearest, so they do not jump between cars.
            wanted.Clear();
            for (int i = 0; i < near.Count && i < RemoteVoices; i++) wanted.Add(near[i].rig);
            foreach (var v in remotes) if (v.Rig != null && !wanted.Contains(v.Rig)) v.Assign(null, null);
            foreach (var rig in wanted)
            {
                bool has = false;
                foreach (var v in remotes) if (v.Rig == rig) has = true;
                if (has) continue;
                var clips = Engine(rig.SpecId);
                if (clips == null) continue;
                foreach (var v in remotes)
                    if (v.Rig == null)
                    {
                        v.Assign(rig, clips);
                        break;
                    }
            }
            foreach (var v in remotes) v.Update(paused, EngineVolume * Effects);
        }

        static float Smoothstep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3 - 2 * t);
        }

        // ------------------------------------------------------------------------------------------
        /// <summary>The player's car: every engine loop, the beds and the one-shots, in 2D.</summary>
        sealed class PlayerVoice
        {
            readonly GameSound owner;
            readonly GameObject root;
            readonly List<AudioSource> on = new List<AudioSource>(), off = new List<AudioSource>();
            readonly Dictionary<Bed, AudioSource> bedSources = new Dictionary<Bed, AudioSource>();
            readonly AudioLowPassFilter muffle;
            EngineClips clips;
            string spec;
            float load, lastThrottle, crackleClock;
            int lastGear = 1;

            public PlayerVoice(GameSound owner, Transform parent)
            {
                this.owner = owner;
                root = new GameObject("Player car");
                root.transform.SetParent(parent, false);
                // The bonnet camera hears the car through the bulkhead.
                root.AddComponent<AudioSource>().playOnAwake = false;
                muffle = root.AddComponent<AudioLowPassFilter>();
                muffle.cutoffFrequency = 22000;
            }

            AudioSource Loop(AudioClip c, string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                var a = go.AddComponent<AudioSource>();
                a.clip = c;
                a.loop = true;
                a.spatialBlend = 0;
                a.volume = 0;
                a.playOnAwake = false;
                a.dopplerLevel = 0;
                a.priority = 32;
                a.time = Random.value * c.length * 0.9f;
                a.Play();
                return a;
            }

            public void AttachBeds()
            {
                foreach (Bed b in System.Enum.GetValues(typeof(Bed)))
                {
                    var c = owner.BedClip(b);
                    if (c != null && !bedSources.ContainsKey(b)) bedSources[b] = Loop(c, "Bed " + b);
                }
            }

            void SetEngine(EngineClips e)
            {
                foreach (var a in on) Object.Destroy(a.gameObject);
                foreach (var a in off) Object.Destroy(a.gameObject);
                on.Clear();
                off.Clear();
                clips = e;
                for (int i = 0; i < e.on.Length; i++) on.Add(Loop(e.on[i], "Engine on " + i));
                for (int i = 0; i < e.off.Length; i++) off.Add(Loop(e.off[i], "Engine off " + i));
            }

            static void Set(AudioSource a, float vol, float pitch)
            {
                if (a == null) return;
                a.volume = vol;
                if (vol > 0) a.pitch = Mathf.Clamp(pitch, 0.05f, 3f);
            }

            void Bed(Bed b, float vol, float pitch) => Set(bedSources.TryGetValue(b, out var a) ? a : null, vol * owner.Effects, pitch);

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
                muffle.cutoffFrequency = Mathf.Lerp(muffle.cutoffFrequency, inside ? 5200 : 22000, Mathf.Min(1, dt * 8));
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
                    void Both(List<AudioSource> layers, float g)
                    {
                        for (int i = 0; i < layers.Count; i++)
                        {
                            float w = i == mix.lo ? (mix.lo == mix.hi ? 1 : a) : i == mix.hi ? b : 0;
                            float rate = (float)(i == mix.lo ? mix.rateLo : i == mix.hi ? mix.rateHi : 1);
                            Set(layers[i], w * g * level, rate);
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
        /// <summary>Another car's engine in 3D: its on-throttle loops, crossfaded by rpm.</summary>
        sealed class RemoteVoice
        {
            readonly GameObject root;
            readonly List<AudioSource> loops = new List<AudioSource>();
            EngineClips clips;
            public CarRig Rig { get; private set; }

            public RemoteVoice(GameSound owner, Transform parent, int i)
            {
                root = new GameObject("Car voice " + i);
                root.transform.SetParent(parent, false);
            }

            public void Assign(CarRig rig, EngineClips c)
            {
                Rig = rig;
                if (c == clips) return;
                foreach (var a in loops) Object.Destroy(a.gameObject);
                loops.Clear();
                clips = c;
                if (c == null) return;
                for (int i = 0; i < c.on.Length; i++)
                {
                    var go = new GameObject("Loop " + i);
                    go.transform.SetParent(root.transform, false);
                    var a = go.AddComponent<AudioSource>();
                    a.clip = c.on[i];
                    a.loop = true;
                    a.spatialBlend = 1;
                    a.rolloffMode = AudioRolloffMode.Logarithmic;
                    a.minDistance = 7;
                    a.maxDistance = RemoteRange;
                    a.dopplerLevel = 0.6f;
                    a.volume = 0;
                    a.priority = 96;
                    a.time = Random.value * a.clip.length * 0.9f;
                    a.Play();
                    loops.Add(a);
                }
            }

            public void Update(bool paused, float volume)
            {
                if (Rig == null || clips == null)
                {
                    foreach (var a in loops) a.volume = 0;
                    return;
                }
                root.transform.position = Rig.transform.position;
                var v = Rig.Vehicle;
                float rpm = Mathf.Max((float)v.Rpm, clips.electric ? 1 : (float)clips.idle * 0.6f);
                var mix = EngineSynth.Mix(clips.ladder, rpm);
                float a0 = Mathf.Cos((float)mix.t * Mathf.PI * 0.5f), b0 = Mathf.Sin((float)mix.t * Mathf.PI * 0.5f);
                float level = paused ? 0 : volume * (0.35f + 0.65f * Mathf.Clamp01((float)v.throttleEff)) * (0.5f + 0.5f * Mathf.Min(1, rpm / (float)clips.limiter));
                for (int i = 0; i < loops.Count; i++)
                {
                    float w = i == mix.lo ? (mix.lo == mix.hi ? 1 : a0) : i == mix.hi ? b0 : 0;
                    loops[i].volume = w * level;
                    if (w > 0) loops[i].pitch = Mathf.Clamp((float)(i == mix.lo ? mix.rateLo : mix.rateHi), 0.05f, 3f);
                }
            }
        }
    }
}
