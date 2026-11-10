using System;
using UnityEngine;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.Audio
{
    /// <summary>
    /// Generates short placeholder sound effects from noise and sine waves. They are stand-ins:
    /// good enough to feel a shot land or a reload finish, and trivially replaced by real clips.
    /// </summary>
    public static class SfxSynth
    {
        const int SampleRate = 44100;
        const float Tau = 2f * Mathf.PI;

        /// <summary>A weapon's firing sound, shaped by its audio profile.</summary>
        public static AudioClip Shot(WeaponAudioProfile profile, bool suppressed, string name)
        {
            switch (profile.kind)
            {
                case ShotSoundKind.Energy: return EnergyShot(profile, name);
                case ShotSoundKind.Thump: return LauncherShot(profile, name);
                case ShotSoundKind.Swing: return Whoosh(name, Mathf.Max(profile.tail, 0.12f), 0.5f);
                case ShotSoundKind.Twang: return Twang(profile, name);
                default: return BallisticShot(profile, suppressed, name);
            }
        }

        static AudioClip BallisticShot(WeaponAudioProfile profile, bool suppressed, string name)
        {
            var random = new System.Random(7);
            float lowpassed = 0f, phase = 0f;
            float tail = Mathf.Max(profile.tail, 0.06f);
            // Rates are tuned for a quarter-second rifle shot and stretch with the tail.
            float stretch = 0.25f / tail;
            float filter = suppressed ? 0.08f : 0.35f;

            return Build(name, tail, t =>
            {
                float noise = (float)random.NextDouble() * 2f - 1f;
                lowpassed += (noise - lowpassed) * filter;

                float crack = suppressed ? 0f : noise * Mathf.Exp(-t * 90f * stretch) * 0.55f;
                float body = lowpassed * Mathf.Exp(-t * 26f * stretch) * (suppressed ? 1.6f : 0.9f);

                // A thump that drops in pitch, which is what gives the shot weight.
                float frequency = Mathf.Max(45f, (150f - 260f * t * stretch) * profile.pitch);
                phase += Tau * frequency / SampleRate;
                float thump = Mathf.Sin(phase) * Mathf.Exp(-t * 20f * stretch) * 0.9f * profile.weight;

                return SoftClip((crack + body + thump) * 0.9f);
            });
        }

        /// <summary>A falling "pew".</summary>
        static AudioClip EnergyShot(WeaponAudioProfile profile, string name)
        {
            var random = new System.Random(23);
            float phase = 0f;
            float tail = Mathf.Max(profile.tail, 0.08f);

            return Build(name, tail, t =>
            {
                float progress = t / tail;
                float frequency = Mathf.Lerp(1900f, 260f, Mathf.Sqrt(progress)) * profile.pitch;
                phase += Tau * frequency / SampleRate;

                float tone = Mathf.Sin(phase) + 0.35f * Mathf.Sin(phase * 2f);
                float hiss = ((float)random.NextDouble() * 2f - 1f) * 0.12f;
                float sub = Mathf.Sin(Tau * 80f * t) * profile.weight * 0.5f;
                return SoftClip((tone * 0.6f + hiss + sub) * Mathf.Exp(-progress * 4f));
            });
        }

        /// <summary>A hollow "thoomp".</summary>
        static AudioClip LauncherShot(WeaponAudioProfile profile, string name)
        {
            var random = new System.Random(29);
            float lowpassed = 0f, phase = 0f;
            float tail = Mathf.Max(profile.tail, 0.15f);

            return Build(name, tail, t =>
            {
                float noise = (float)random.NextDouble() * 2f - 1f;
                lowpassed += (noise - lowpassed) * 0.12f;

                float frequency = Mathf.Max(40f, (110f - 200f * t) * profile.pitch);
                phase += Tau * frequency / SampleRate;
                float thump = Mathf.Sin(phase) * Mathf.Exp(-t * 14f) * (0.8f + 0.4f * profile.weight);
                float puff = lowpassed * 2.2f * Mathf.Exp(-t * 18f);
                return SoftClip(thump + puff);
            });
        }

        /// <summary>A plucked string with a click, for crossbows and thrown blades.</summary>
        static AudioClip Twang(WeaponAudioProfile profile, string name)
        {
            var random = new System.Random(31);
            float tail = Mathf.Max(profile.tail, 0.08f);
            float frequency = 210f * profile.pitch;

            return Build(name, tail, t =>
            {
                float pluck = (Mathf.Sin(Tau * frequency * t) + 0.5f * Mathf.Sin(Tau * frequency * 2.01f * t)) * Mathf.Exp(-t * 22f);
                float click = ((float)random.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 260f) * 0.6f;
                return SoftClip(pluck * 0.6f + click);
            });
        }

        public static AudioClip Explosion()
        {
            var random = new System.Random(37);
            float lowpassed = 0f, rumble = 0f, phase = 0f;

            return Build("Explosion", 0.9f, t =>
            {
                float noise = (float)random.NextDouble() * 2f - 1f;
                lowpassed += (noise - lowpassed) * 0.25f;
                rumble += (noise - rumble) * 0.03f;

                float crack = lowpassed * Mathf.Exp(-t * 18f) * 1.2f;
                float roar = rumble * 6f * Mathf.Exp(-t * 5f);
                float frequency = Mathf.Max(32f, 90f - 110f * t);
                phase += Tau * frequency / SampleRate;
                float boom = Mathf.Sin(phase) * Mathf.Exp(-t * 6f) * 1.1f;
                return SoftClip(crack + roar + boom);
            });
        }

        public static AudioClip Hit() =>
            Build("Hit", 0.06f, t => Mathf.Sin(Tau * 1700f * t) * Mathf.Exp(-t * 70f) * 0.5f);

        public static AudioClip Headshot() =>
            Build("Headshot", 0.14f, t =>
                (Mathf.Sin(Tau * 2400f * t) + 0.6f * Mathf.Sin(Tau * 3600f * t)) * Mathf.Exp(-t * 30f) * 0.35f);

        public static AudioClip Kill() =>
            Build("Kill", 0.24f, t =>
            {
                const float second = 0.08f;
                float first = Mathf.Sin(Tau * 900f * t) * Mathf.Exp(-t * 35f);
                float next = t < second ? 0f : Mathf.Sin(Tau * 1350f * (t - second)) * Mathf.Exp(-(t - second) * 22f);
                return (first + next) * 0.4f;
            });

        public static AudioClip Click(string name, float frequency, float seconds, float volume)
        {
            var random = new System.Random(11);
            return Build(name, seconds, t => ClickSample(random, frequency, t) * volume);
        }

        public static AudioClip DoubleClick(string name, float frequency, float seconds, float volume)
        {
            var random = new System.Random(13);
            const float second = 0.09f;
            return Build(name, seconds, t =>
            {
                float sample = ClickSample(random, frequency, t);
                if (t >= second) sample += ClickSample(random, frequency * 1.3f, t - second);
                return sample * volume;
            });
        }

        /// <summary>A soft low knock: filtered noise with an optional sub tone.</summary>
        public static AudioClip Thump(string name, float seconds, float decay, float volume, float subAmount)
        {
            var random = new System.Random(17);
            float lowpassed = 0f;
            return Build(name, seconds, t =>
            {
                float noise = (float)random.NextDouble() * 2f - 1f;
                lowpassed += (noise - lowpassed) * 0.08f;
                float sub = Mathf.Sin(Tau * 70f * t) * subAmount;
                return SoftClip((lowpassed * 3f + sub) * Mathf.Exp(-t * decay) * volume);
            });
        }

        /// <summary>Filtered noise that swells and fades.</summary>
        public static AudioClip Whoosh(string name, float seconds, float volume)
        {
            var random = new System.Random(19);
            float lowpassed = 0f;
            return Build(name, seconds, t =>
            {
                float noise = (float)random.NextDouble() * 2f - 1f;
                lowpassed += (noise - lowpassed) * 0.15f;
                float envelope = Mathf.Sqrt(Mathf.Max(0f, Mathf.Sin(Mathf.Clamp01(t / seconds) * Mathf.PI)));
                return lowpassed * 2f * envelope * volume;
            });
        }

        static float ClickSample(System.Random random, float frequency, float t)
        {
            float noise = (float)random.NextDouble() * 2f - 1f;
            return noise * Mathf.Exp(-t * 300f) * 0.6f + Mathf.Sin(Tau * frequency * t) * Mathf.Exp(-t * 80f) * 0.5f;
        }

        static float SoftClip(float x) => x / (1f + Mathf.Abs(x));

        static AudioClip Build(string name, float seconds, Func<float, float> sampleAt)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            var samples = new float[count];
            int fade = Mathf.Min(64, count);

            for (int i = 0; i < count; i++)
            {
                float sample = sampleAt(i / (float)SampleRate);
                // A few milliseconds of fade-out so clips never end on a click.
                int fromEnd = count - 1 - i;
                if (fromEnd < fade) sample *= fromEnd / (float)fade;
                samples[i] = Mathf.Clamp(sample, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
