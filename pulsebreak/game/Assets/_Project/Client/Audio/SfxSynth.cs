using System;
using UnityEngine;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.Audio
{
    /// <summary>
    /// Generates the game's sound effects from noise and sine waves when they are first needed,
    /// so the project carries no audio files and every sound is our own. Recorded clips could
    /// replace any of them later without touching the code that plays them.
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

        // ------------------------------------------------------------------ bodies and movement

        /// <summary>A shoe on a hard floor: a heel knock and a short scrape. Variants differ in pitch and grit.</summary>
        public static AudioClip Footstep(int variant)
        {
            var random = new System.Random(41 + variant * 7);
            float lowpassed = 0f, bandpassed = 0f, phase = 0f;
            float pitch = 1f + (variant - 1.5f) * 0.07f;
            return Build("Footstep" + variant, 0.13f, t =>
            {
                float noise = (float)random.NextDouble() * 2f - 1f;
                lowpassed += (noise - lowpassed) * 0.22f;
                bandpassed += (lowpassed - bandpassed) * 0.5f;
                float knock = (lowpassed - bandpassed) * 3.2f * Mathf.Exp(-t * 70f);
                phase += Tau * Mathf.Max(60f, 140f * pitch - 500f * t) / SampleRate;
                float body = Mathf.Sin(phase) * Mathf.Exp(-t * 45f) * 0.55f;
                float scrape = t > 0.025f ? noise * 0.12f * Mathf.Exp(-(t - 0.025f) * 55f) : 0f;
                return SoftClip((knock + body + scrape) * 0.9f);
            });
        }

        /// <summary>Taking a hit: a dull thud with a short ringing edge.</summary>
        public static AudioClip Hurt(bool head)
        {
            var random = new System.Random(head ? 43 : 47);
            float lowpassed = 0f, phase = 0f;
            return Build(head ? "HurtHead" : "Hurt", head ? 0.32f : 0.24f, t =>
            {
                float noise = (float)random.NextDouble() * 2f - 1f;
                lowpassed += (noise - lowpassed) * 0.1f;
                phase += Tau * Mathf.Max(50f, 120f - 200f * t) / SampleRate;
                float thud = (Mathf.Sin(phase) * 0.9f + lowpassed * 2.5f) * Mathf.Exp(-t * 22f);
                float ring = head ? Mathf.Sin(Tau * 2900f * t) * 0.25f * Mathf.Exp(-t * 14f) : 0f;
                return SoftClip((thud + ring) * 1.1f);
            });
        }

        /// <summary>Going down: a heavy hit, then the world dropping away.</summary>
        public static AudioClip Death()
        {
            var random = new System.Random(53);
            float lowpassed = 0f, phase = 0f;
            return Build("Death", 1.1f, t =>
            {
                float noise = (float)random.NextDouble() * 2f - 1f;
                lowpassed += (noise - lowpassed) * Mathf.Lerp(0.3f, 0.02f, t);
                phase += Tau * Mathf.Lerp(220f, 40f, Mathf.Sqrt(t)) / SampleRate;
                float fall = Mathf.Sin(phase) * Mathf.Exp(-t * 2.5f) * 0.6f;
                float hit = lowpassed * 3f * Mathf.Exp(-t * 6f);
                return SoftClip(fall + hit);
            });
        }

        /// <summary>A heartbeat: lub-dub.</summary>
        public static AudioClip Heartbeat() =>
            Build("Heartbeat", 0.5f, t =>
            {
                float lub = Mathf.Sin(Tau * 55f * t) * Mathf.Exp(-t * 28f);
                float dt = t - 0.16f;
                float dub = dt > 0f ? Mathf.Sin(Tau * 48f * dt) * Mathf.Exp(-dt * 34f) * 0.75f : 0f;
                return SoftClip((lub + dub) * 1.6f);
            });

        /// <summary>A bullet passing close by: a short hiss that drops in pitch as it goes.</summary>
        public static AudioClip Whiz()
        {
            var random = new System.Random(59);
            float a = 0f, b = 0f;
            return Build("Whiz", 0.22f, t =>
            {
                float noise = (float)random.NextDouble() * 2f - 1f;
                float cut = Mathf.Lerp(0.55f, 0.12f, t / 0.22f);
                a += (noise - a) * cut;
                b += (a - b) * cut;
                float envelope = Mathf.Sin(Mathf.Clamp01(t / 0.22f) * Mathf.PI);
                return (a - b) * 3f * envelope * envelope;
            });
        }

        /// <summary>Arriving: a rising shimmer.</summary>
        public static AudioClip Spawn() =>
            Build("Spawn", 0.6f, t =>
            {
                float progress = t / 0.6f;
                float frequency = Mathf.Lerp(380f, 1150f, progress * progress);
                float shimmer = Mathf.Sin(Tau * frequency * t) * (0.6f + 0.4f * Mathf.Sin(Tau * 18f * t));
                float envelope = Mathf.Sin(progress * Mathf.PI) * 0.35f;
                return shimmer * envelope + Mathf.Sin(Tau * frequency * 1.5f * t) * envelope * 0.3f;
            });

        // ------------------------------------------------------------------ signals

        /// <summary>A short beep, for countdowns.</summary>
        public static AudioClip Beep(string name, float frequency, float seconds, float volume) =>
            Build(name, seconds, t =>
            {
                float attack = Mathf.Clamp01(t / 0.004f);
                return (Mathf.Sin(Tau * frequency * t) + 0.25f * Mathf.Sin(Tau * frequency * 2f * t)) * attack * Mathf.Exp(-t * 6f / seconds) * volume;
            });

        /// <summary>Two or three notes one after another, as a chime.</summary>
        public static AudioClip Chime(string name, float step, float ring, float volume, params float[] notes)
        {
            float seconds = step * (notes.Length - 1) + ring;
            return Build(name, seconds, t =>
            {
                float sum = 0f;
                for (int i = 0; i < notes.Length; i++)
                {
                    float local = t - i * step;
                    if (local < 0f) break;
                    float f = notes[i];
                    sum += (Mathf.Sin(Tau * f * local) + 0.3f * Mathf.Sin(Tau * f * 2f * local) + 0.12f * Mathf.Sin(Tau * f * 3.01f * local))
                           * Mathf.Exp(-local * 4.5f / ring) * Mathf.Clamp01(local / 0.003f);
                }
                return SoftClip(sum * volume);
            });
        }

        /// <summary>A soft interface tick.</summary>
        public static AudioClip Tick(string name, float frequency, float seconds, float volume) =>
            Build(name, seconds, t => Mathf.Sin(Tau * frequency * t) * Mathf.Exp(-t * 5f / seconds) * Mathf.Clamp01(t / 0.002f) * volume);

        /// <summary>A buzz for something that cannot be done.</summary>
        public static AudioClip Buzz(string name) =>
            Build(name, 0.18f, t =>
            {
                float square = Mathf.Sign(Mathf.Sin(Tau * 140f * t)) * 0.5f + Mathf.Sin(Tau * 280f * t) * 0.3f;
                return square * Mathf.Exp(-t * 12f) * 0.25f;
            });

        // ------------------------------------------------------------------ music stingers

        /// <summary>A short musical phrase for a moment in a match, on the music volume.</summary>
        public static AudioClip Stinger(Stinger kind)
        {
            switch (kind)
            {
                case Audio.Stinger.MatchStart:
                    return Phrase("StingerMatchStart", 1.5f, 1f, (0f, -7, 1.3f), (0f, 0, 1.3f), (0f, 5, 1.3f), (0f, 8, 1.3f), (0.02f, 17, 0.5f));
                case Audio.Stinger.RoundWon:
                    return Phrase("StingerRoundWon", 1.1f, 0f, (0f, 0, 0.6f), (0.12f, 5, 0.6f), (0.24f, 12, 0.8f));
                case Audio.Stinger.RoundLost:
                    return Phrase("StingerRoundLost", 1.2f, 0f, (0f, 0, 0.6f), (0.15f, -4, 0.6f), (0.3f, -7, 0.9f));
                case Audio.Stinger.Victory:
                    return Phrase("StingerVictory", 2.6f, 0.8f, (0f, -7, 0.5f), (0.1f, -3, 0.5f), (0.2f, 0, 0.5f), (0.3f, 5, 0.6f),
                        (0.45f, 5, 2f), (0.45f, 9, 2f), (0.45f, 12, 2f), (0.45f, 17, 1.6f));
                case Audio.Stinger.Defeat:
                    return Phrase("StingerDefeat", 2.4f, 0.5f, (0f, -7, 0.7f), (0.25f, -9, 0.7f), (0.5f, -11, 0.8f), (0.75f, -12, 1.6f), (0.75f, -24, 1.8f));
                case Audio.Stinger.LevelUp:
                    return Phrase("StingerLevelUp", 1.6f, 0f, (0f, 3, 0.5f), (0.05f, 7, 0.5f), (0.1f, 10, 0.5f), (0.15f, 15, 0.6f),
                        (0.2f, 19, 0.7f), (0.25f, 22, 1.1f), (0.25f, 27, 1.1f));
                default:
                    return null;
            }
        }

        /// <summary>Notes (start in seconds, semitones from A4, how long they ring), with an optional low boom under the first.</summary>
        static AudioClip Phrase(string name, float seconds, float boom, params (float Start, int Semitones, float Ring)[] notes)
        {
            float boomPhase = 0f;
            return Build(name, seconds, t =>
            {
                float sum = 0f;
                for (int i = 0; i < notes.Length; i++)
                {
                    float local = t - notes[i].Start;
                    if (local < 0f) continue;
                    float f = Note(notes[i].Semitones);
                    float vibrato = 1f + 0.003f * Mathf.Sin(Tau * 5f * local) * Mathf.Clamp01(local * 2f);
                    float tone = Mathf.Sin(Tau * f * vibrato * local) + 0.35f * Mathf.Sin(Tau * f * 2f * local) + 0.12f * Mathf.Sin(Tau * f * 3f * local);
                    sum += tone * Mathf.Clamp01(local / 0.006f) * Mathf.Exp(-local * 3f / notes[i].Ring);
                }
                float low = 0f;
                if (boom > 0f)
                {
                    boomPhase += Tau * Mathf.Max(38f, 90f - 140f * t) / SampleRate;
                    low = Mathf.Sin(boomPhase) * Mathf.Exp(-t * 4f) * boom;
                }
                return SoftClip(sum * 0.22f + low * 0.7f);
            });
        }

        // ------------------------------------------------------------------ helpers

        public static float Note(int semitonesFromA4) => 440f * Mathf.Pow(2f, semitonesFromA4 / 12f);

        static float ClickSample(System.Random random, float frequency, float t)
        {
            float noise = (float)random.NextDouble() * 2f - 1f;
            return noise * Mathf.Exp(-t * 300f) * 0.6f + Mathf.Sin(Tau * frequency * t) * Mathf.Exp(-t * 80f) * 0.5f;
        }

        public static float SoftClip(float x) => x / (1f + Mathf.Abs(x));

        /// <summary>Renders a mono clip from a function of time in seconds.</summary>
        public static AudioClip Build(string name, float seconds, Func<float, float> sampleAt)
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
