// The radio in Unity: a worker thread renders the station's bars (Core/Audio's
// RadioStation, the same music as the web version) a few seconds ahead into
// a queue, and the audio thread plays the queue through OnAudioFilterRead on
// a source looping silence. Switching stations clears the queue and plays a
// moment of tuning static; a new song's name reaches the HUD when it starts.
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Halcyon.Core.Audio;
using UnityEngine;

namespace Halcyon
{
    public sealed class RadioPlayer : MonoBehaviour
    {
        sealed class Chunk
        {
            public float[] stereo;
            public string song;
        }

        readonly object gate = new object();
        readonly Queue<Chunk> queue = new Queue<Chunk>();
        int queuedFrames;
        /// <summary>The station asked for (null: off) and a counter that changes with each ask.</summary>
        RadioStation wanted;
        int epoch;
        Chunk current;
        int pos;
        volatile float volume;
        volatile bool running = true;
        int staticLeft;
        readonly ConcurrentQueue<string> songs = new ConcurrentQueue<string>();
        readonly System.Random noise = new System.Random(7);
        Task worker;
        int rate = 48000;

        public float Volume { set => volume = value; }
        public System.Action<string> OnNewSong;

        public static RadioPlayer Create(Transform parent)
        {
            var go = new GameObject("Radio");
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0;
            src.loop = true;
            src.playOnAwake = false;
            int sr = AudioSettings.outputSampleRate;
            // A second of silence to loop: the filter below writes the music over it.
            src.clip = AudioClip.Create("Radio carrier", sr, 2, sr, false);
            src.clip.SetData(new float[sr * 2], 0);
            var r = go.AddComponent<RadioPlayer>();
            r.rate = sr;
            src.Play();
            return r;
        }

        /// <summary>Tune to a station (null: off).</summary>
        public void Play(StationId? station, int sampleRate)
        {
            rate = sampleRate;
            lock (gate)
            {
                queue.Clear();
                queuedFrames = 0;
                current = null;
                wanted = station.HasValue ? new RadioStation(station.Value, sampleRate, System.Environment.TickCount) : null;
                epoch++;
                staticLeft = station.HasValue ? (int)(sampleRate * 0.28) : 0;
            }
            if (worker == null) worker = Task.Run(Render);
        }

        /// <summary>The worker: keep about three seconds of the station rendered ahead.</summary>
        void Render()
        {
            RadioStation station = null;
            int seen = -1;
            while (running)
            {
                bool need;
                int mine;
                lock (gate)
                {
                    if (seen != epoch)
                    {
                        station = wanted;
                        seen = epoch;
                    }
                    mine = epoch;
                    need = station != null && queuedFrames < rate * 3;
                }
                if (!need)
                {
                    Thread.Sleep(40);
                    continue;
                }
                var bar = station.RenderBar();
                var stereo = new float[bar.l.Length * 2];
                for (int i = 0; i < bar.l.Length; i++)
                {
                    stereo[2 * i] = bar.l[i];
                    stereo[2 * i + 1] = bar.r[i];
                }
                var chunk = new Chunk { stereo = stereo, song = bar.newSong && !bar.ident ? $"{station.Info.name} · {bar.artist} – {bar.title}" : null };
                lock (gate)
                {
                    // The station changed while this bar rendered: drop it.
                    if (mine != epoch) continue;
                    queue.Enqueue(chunk);
                    queuedFrames += bar.l.Length;
                }
            }
        }

        void Update()
        {
            while (songs.TryDequeue(out var s)) OnNewSong?.Invoke(s);
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float vol = volume;
            int frames = data.Length / channels;
            lock (gate)
            {
                for (int f = 0; f < frames; f++)
                {
                    float l = 0, r = 0;
                    if (staticLeft > 0)
                    {
                        float n = (float)(noise.NextDouble() * 2 - 1) * 0.12f;
                        l += n;
                        r += n;
                        staticLeft--;
                    }
                    if (current == null || pos >= current.stereo.Length)
                    {
                        current = queue.Count > 0 ? queue.Dequeue() : null;
                        pos = 0;
                        if (current != null)
                        {
                            queuedFrames -= current.stereo.Length / 2;
                            if (current.song != null) songs.Enqueue(current.song);
                        }
                    }
                    if (current != null)
                    {
                        l += current.stereo[pos];
                        r += current.stereo[pos + 1];
                        pos += 2;
                    }
                    if (channels == 1) data[f] += (l + r) * 0.5f * vol;
                    else
                    {
                        data[f * channels] += l * vol;
                        data[f * channels + 1] += r * vol;
                    }
                }
            }
        }

        void OnDestroy() => running = false;
    }
}
