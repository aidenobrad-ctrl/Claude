// The radio in Unity: a worker thread renders the station's bars (Core/Audio's
// RadioStation, the same music as the web version) a few seconds ahead into
// a queue, and the audio thread plays the queue through OnAudioFilterRead on
// a source looping silence. Switching stations clears the queue and plays a
// moment of tuning static; a new song's name reaches the HUD when it starts.
// Played bars go back to the worker to be filled again.
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Halcyon.Core.Audio;
using UnityEngine;

namespace Halcyon
{
    public sealed class RadioPlayer : MonoBehaviour
    {
        sealed class Chunk
        {
            public float[] stereo;
            public int frames;
            public string song;
        }

        readonly object gate = new object();
        readonly Queue<Chunk> queue = new Queue<Chunk>();
        readonly Stack<Chunk> spare = new Stack<Chunk>();
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
        Thread worker;
        int rate = 48000;
        AudioSource carrier;

        public float Volume { set => volume = value; }
        /// <summary>A new song's "station · artist – title", on the main thread.</summary>
        public System.Action<string> OnNewSong;

        /// <summary>The output's sample rate (48 kHz if Unity reports none, as with audio disabled).</summary>
        public static int OutputRate
        {
            get
            {
                int r = AudioSettings.outputSampleRate;
                return r > 0 ? r : 48000;
            }
        }

        public static RadioPlayer Create(Transform parent)
        {
            var go = new GameObject("Radio");
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0;
            src.loop = true;
            src.playOnAwake = false;
            // The radio is the one sound that must never be dropped for another voice.
            src.priority = 0;
            // A second of silence to loop: the filter below writes the music over it (at the output's rate, whatever the clip's).
            src.clip = AudioClip.Create("Radio carrier", 48000, 2, 48000, false);
            src.clip.SetData(new float[48000 * 2], 0);
            var r = go.AddComponent<RadioPlayer>();
            r.carrier = src;
            src.Play();
            return r;
        }

        /// <summary>After Unity's audio reset (a new output device) the carrier has stopped: play it again (then tune again at the new rate).</summary>
        public void Restart()
        {
            if (carrier != null && !carrier.isPlaying) carrier.Play();
        }

        /// <summary>Tune to a station (null: off).</summary>
        public void Play(StationId? station, int sampleRate)
        {
            if (sampleRate <= 0) sampleRate = 48000;
            lock (gate)
            {
                rate = sampleRate;
                while (queue.Count > 0) spare.Push(queue.Dequeue());
                if (current != null) spare.Push(current);
                queuedFrames = 0;
                current = null;
                wanted = station.HasValue ? new RadioStation(station.Value, sampleRate, System.Environment.TickCount) : null;
                epoch++;
                staticLeft = station.HasValue ? (int)(sampleRate * 0.28) : 0;
            }
            if (worker == null)
            {
                worker = new Thread(Render) { IsBackground = true, Name = "Halcyon radio" };
                worker.Start();
            }
        }

        /// <summary>The worker: keep about three seconds of the station rendered ahead.</summary>
        void Render()
        {
            RadioStation station = null;
            int seen = -1;
            bool broken = false;
            while (running)
            {
                bool need;
                int mine;
                Chunk chunk = null;
                lock (gate)
                {
                    if (seen != epoch)
                    {
                        station = wanted;
                        seen = epoch;
                        broken = false;
                    }
                    mine = epoch;
                    need = station != null && !broken && queuedFrames < rate * 3;
                    if (need && spare.Count > 0) chunk = spare.Pop();
                }
                if (!need)
                {
                    Thread.Sleep(40);
                    continue;
                }
                RadioBar bar;
                try
                {
                    bar = station.RenderBar();
                }
                catch (System.Exception e)
                {
                    // A station that fails goes quiet (said once); the next tune starts afresh.
                    broken = true;
                    Debug.LogWarning("Halcyon: the radio stopped: " + e.Message);
                    if (chunk != null) lock (gate) spare.Push(chunk);
                    continue;
                }
                int frames = bar.l.Length;
                chunk ??= new Chunk();
                if (chunk.stereo == null || chunk.stereo.Length < frames * 2) chunk.stereo = new float[frames * 2];
                var stereo = chunk.stereo;
                for (int i = 0; i < frames; i++)
                {
                    stereo[2 * i] = bar.l[i];
                    stereo[2 * i + 1] = bar.r[i];
                }
                chunk.frames = frames;
                chunk.song = bar.newSong && !bar.ident ? $"{station.Info.name} · {bar.artist} – {bar.title}" : null;
                lock (gate)
                {
                    // The station changed while this bar rendered: drop it.
                    if (mine != epoch)
                    {
                        spare.Push(chunk);
                        continue;
                    }
                    queue.Enqueue(chunk);
                    queuedFrames += frames;
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
                    if (current == null || pos >= current.frames * 2)
                    {
                        if (current != null) spare.Push(current);
                        current = queue.Count > 0 ? queue.Dequeue() : null;
                        pos = 0;
                        if (current != null)
                        {
                            queuedFrames -= current.frames;
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
