// Weather sounds made at startup until recorded ones replace them: the hiss
// of rain (filtered noise with droplet ticks, looping without a seam) and
// thunder (a crack for near strikes, then a rumble of brown noise that
// rolls and fades over several seconds).
using UnityEngine;

namespace Halcyon
{
    public static class ProceduralAudio
    {
        const int Rate = 22050;

        /// <summary>Three seconds of rain that loop seamlessly.</summary>
        public static AudioClip RainLoop()
        {
            int n = Rate * 3, fade = Rate / 4;
            var data = new float[n + fade];
            var rng = new System.Random(71);
            float lp = 0, hp = 0, prev = 0;
            for (int i = 0; i < data.Length; i++)
            {
                float white = (float)(rng.NextDouble() * 2 - 1);
                // Hiss: white noise, high-passed, with a little of it low-passed for body.
                lp += (white - lp) * 0.08f;
                hp = 0.9f * (hp + white - prev);
                prev = white;
                float s = hp * 0.22f + lp * 0.35f;
                // Droplets: short decaying ticks.
                if (rng.NextDouble() < 0.0016)
                {
                    float amp = (float)(0.15 + rng.NextDouble() * 0.25);
                    int len = 60 + rng.Next(120);
                    for (int k = 0; k < len && i + k < data.Length; k++) data[i + k] += amp * Mathf.Exp(-k / 18f) * Mathf.Sin(k * 1.9f);
                }
                data[i] += s;
            }
            // Seamless loop: crossfade the tail into the head.
            for (int i = 0; i < fade; i++)
            {
                float t = (float)i / fade;
                data[i] = data[i] * t + data[n + i] * (1 - t);
            }
            var clip = AudioClip.Create("Rain", n, 1, Rate, false);
            var outData = new float[n];
            System.Array.Copy(data, outData, n);
            clip.SetData(outData, 0);
            return clip;
        }

        /// <summary>A thunderclap: near ones crack before they rumble.</summary>
        public static AudioClip Thunder(int seed, bool near)
        {
            int n = Rate * 7;
            var data = new float[n];
            var rng = new System.Random(seed);
            float brown = 0, roll = 0, lp = 0;
            float peak = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float white = (float)(rng.NextDouble() * 2 - 1);
                brown = Mathf.Clamp(brown + white * 0.05f, -1, 1) * 0.998f;
                // The rumble rolls: slow random swells.
                if (i % 2205 == 0) roll = (float)(0.4 + rng.NextDouble() * 0.6);
                lp += (roll - lp) * 0.0008f;
                float env = Mathf.Clamp01(t / (near ? 0.05f : 0.4f)) * Mathf.Exp(-t / (near ? 1.6f : 2.2f));
                float s = brown * 2.2f * env * lp;
                if (near && t < 0.35f) s += white * Mathf.Exp(-t / 0.06f) * 0.9f;
                data[i] = s;
                peak = Mathf.Max(peak, Mathf.Abs(s));
            }
            if (peak > 0)
                for (int i = 0; i < n; i++) data[i] *= 0.9f / peak;
            var clip = AudioClip.Create(near ? "ThunderNear" : "ThunderFar", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
