using System;

namespace Pulsebreak.Client.Audio
{
    /// <summary>
    /// The menu music, made as it plays: a slow synth piece in D minor at 100 beats a minute,
    /// 32 bars long and then round again. A warm pad carries the chords (D minor, B flat,
    /// F, C, two bars each), a plucked arpeggio with an echo runs over it, and a bass line,
    /// hats and a soft kick come and go to give it sections:
    ///
    ///   bars  1-8   pad and a dark arpeggio
    ///   bars  9-16  bass and hats join
    ///   bars 17-24  the kick joins and the arpeggio brightens
    ///   bars 25-32  it thins out, with a rising sweep back to the top
    ///
    /// Plain arithmetic with no engine calls, so it can run on the audio thread, and be
    /// rendered and checked anywhere. It is entirely the game's own.
    /// </summary>
    public sealed class MusicSynth
    {
        public const float Bpm = 100f;
        public const int Bars = 32;
        const float Tau = 2f * MathF.PI;

        // Pad voicings (MIDI note numbers), the bass root under each, and the arpeggio's five notes.
        static readonly int[][] Pads = { new[] { 62, 65, 69 }, new[] { 58, 62, 65 }, new[] { 60, 65, 69 }, new[] { 60, 64, 67 } };
        static readonly int[] Roots = { 38, 34, 41, 36 };
        static readonly int[][] Arps = { new[] { 62, 65, 69, 74, 77 }, new[] { 58, 62, 65, 70, 74 }, new[] { 60, 65, 69, 72, 77 }, new[] { 60, 64, 67, 72, 76 } };
        static readonly int[] ArpPattern = { 0, 1, 2, 3, 4, 3, 2, 1, 0, 2, 1, 3, 2, 4, 3, 1 };

        readonly int _rate;
        readonly double _samplesPerBeat;
        long _position;

        readonly float[] _padPhase = new float[6];
        readonly float[] _padFrequency = new float[6];
        float _padLowL, _padLowR;

        float _arpPhase, _arpFrequency, _arpAge = 10f, _arpBright;
        readonly float[] _echoL, _echoR;
        int _echoIndex;

        float _bassPhase, _bassFrequency, _bassAge = 10f, _bassLow;
        float _kickPhase, _kickAge = 10f;
        float _hatAge = 10f, _hatLow, _riserLow;
        uint _noise = 0x9E3779B9u;
        long _lastStep = -1;

        public MusicSynth(int sampleRate)
        {
            _rate = Math.Max(8000, sampleRate);
            _samplesPerBeat = _rate * 60.0 / Bpm;
            int echo = (int)(_samplesPerBeat * 0.75);
            _echoL = new float[echo];
            _echoR = new float[echo];
            for (int i = 0; i < 6; i++) _padFrequency[i] = Frequency(Pads[0][i / 2]);
        }

        /// <summary>Seconds in one time round the piece.</summary>
        public static float LoopSeconds => Bars * 4f * 60f / Bpm;

        /// <summary>Fills an interleaved buffer with the next stretch of music: left, right, and silence in any other channels.</summary>
        public void Render(float[] data, int channels)
        {
            if (channels <= 0) return;
            int frames = data.Length / channels;
            for (int frame = 0; frame < frames; frame++)
            {
                Next(out float left, out float right);
                int at = frame * channels;
                data[at] = left;
                if (channels > 1) data[at + 1] = right;
                for (int c = 2; c < channels; c++) data[at + c] = 0f;
            }
        }

        void Next(out float left, out float right)
        {
            float dt = 1f / _rate;
            double beats = _position / _samplesPerBeat;
            long step = (long)(beats * 4.0);                 // sixteenth notes
            int bar = (int)((long)(beats / 4.0) % Bars);
            int chord = (bar / 2) % 4;
            float barProgress = (float)(beats / 4.0 - Math.Floor(beats / 4.0));
            bool bass = bar >= 8;
            bool hats = bar >= 8 && bar < 24;
            bool kick = bar >= 16 && bar < 24;
            bool sparse = bar >= 24;

            if (step != _lastStep)
            {
                _lastStep = step;
                int sixteenth = (int)(step % 16);
                if (!sparse || sixteenth % 2 == 0)
                {
                    _arpFrequency = Frequency(Arps[chord][ArpPattern[sixteenth]]);
                    _arpAge = 0f;
                }
                if (bass && sixteenth % 2 == 0)
                {
                    _bassFrequency = Frequency(Roots[chord] + (sixteenth == 14 ? 12 : 0));
                    _bassAge = 0f;
                }
                if (kick && sixteenth % 8 == 0) _kickAge = 0f;
                if (hats && sixteenth % 4 == 2) _hatAge = 0f;
            }

            // Pad: two slightly detuned saws per note, one each side, gliding between chords.
            float padL = 0f, padR = 0f;
            for (int i = 0; i < 6; i++)
            {
                float target = Frequency(Pads[chord][i / 2]) * (i % 2 == 0 ? 0.9988f : 1.0012f);
                _padFrequency[i] += (target - _padFrequency[i]) * 0.0015f;
                _padPhase[i] += _padFrequency[i] * dt;
                if (_padPhase[i] >= 1f) _padPhase[i] -= 1f;
                float saw = 2f * _padPhase[i] - 1f;
                if (i % 2 == 0) padL += saw; else padR += saw;
            }
            float sweep = 0.5f + 0.5f * MathF.Sin(Tau * (float)(beats / 32.0));
            float cutoff = (bar < 8 ? 450f + 600f * bar / 8f : 700f) + 1100f * sweep;
            float a = 1f - MathF.Exp(-Tau * cutoff * dt);
            _padLowL += (padL - _padLowL) * a;
            _padLowR += (padR - _padLowR) * a;

            // Arpeggio: a pluck, darker at first, through a ping-pong echo.
            _arpBright += ((kick ? 0.55f : 0.2f) - _arpBright) * 0.0001f;
            _arpAge += dt;
            _arpPhase += _arpFrequency * dt;
            if (_arpPhase >= 1f) _arpPhase -= 1f;
            float pluck = (MathF.Sin(Tau * _arpPhase) + _arpBright * MathF.Sin(2f * Tau * _arpPhase)) *
                          MathF.Exp(-_arpAge * 9f) * Math.Min(1f, _arpAge / 0.003f) * 0.15f;
            float echoL = _echoL[_echoIndex], echoR = _echoR[_echoIndex];
            _echoL[_echoIndex] = pluck * 0.5f + echoR * 0.42f;
            _echoR[_echoIndex] = echoL * 0.42f;
            _echoIndex = (_echoIndex + 1) % _echoL.Length;

            // Bass: a rounded tone on the eighths, ducking under the kick.
            float duck = kick ? 1f - 0.45f * MathF.Exp(-_kickAge * 10f) : 1f;
            float bassTone = 0f;
            if (bass)
            {
                _bassAge += dt;
                _bassPhase += _bassFrequency * dt;
                if (_bassPhase >= 1f) _bassPhase -= 1f;
                float raw = MathF.Sin(Tau * _bassPhase) + 0.3f * MathF.Sin(2f * Tau * _bassPhase);
                _bassLow += (raw - _bassLow) * 0.2f;
                bassTone = Soft(_bassLow * 1.4f) * MathF.Exp(-_bassAge * 3.2f) * Math.Min(1f, _bassAge / 0.004f) * 0.2f * duck;
            }

            float kickTone = 0f;
            if (kick)
            {
                _kickAge += dt;
                _kickPhase += (45f + 80f * MathF.Exp(-_kickAge * 30f)) * dt;
                if (_kickPhase >= 1f) _kickPhase -= 1f;
                kickTone = MathF.Sin(Tau * _kickPhase) * MathF.Exp(-_kickAge * 7f) * 0.42f;
            }

            float noise = Noise();
            float hat = 0f;
            if (hats)
            {
                _hatAge += dt;
                _hatLow += (noise - _hatLow) * 0.35f;
                hat = (noise - _hatLow) * MathF.Exp(-_hatAge * 45f) * 0.05f;
            }

            float riser = 0f;
            if (bar == Bars - 1)
            {
                _riserLow += (noise - _riserLow) * (0.02f + 0.3f * barProgress * barProgress);
                riser = _riserLow * barProgress * barProgress * 0.12f;
            }

            float padLevel = 0.055f * duck;
            left = Soft((_padLowL * padLevel + pluck * 0.6f + echoL * 0.35f + bassTone + kickTone + hat * 0.8f + riser) * 0.95f) * 1.5f;
            right = Soft((_padLowR * padLevel + pluck * 0.4f + echoR * 0.35f + bassTone + kickTone + hat * 1.2f + riser) * 0.95f) * 1.5f;
            _position++;
        }

        float Noise()
        {
            _noise ^= _noise << 13;
            _noise ^= _noise >> 17;
            _noise ^= _noise << 5;
            return _noise / (float)uint.MaxValue * 2f - 1f;
        }

        static float Frequency(int midi) => 440f * MathF.Pow(2f, (midi - 69) / 12f);

        static float Soft(float x) => x / (1f + MathF.Abs(x));
    }
}
