using System;

namespace Ygo.Engine.Audio
{
    /// <summary>
    /// A lookahead peak limiter for the game's final mix (docs/dev/AUDIO-PASS-1-SPEC-2026-11-20.md section B). Plain C#:
    /// the game runs it in OnAudioFilterRead on the AudioListener's GameObject, and the harness's --audio-check tests the
    /// same code. Stereo linked: each frame's level is its loudest channel. The output is the input delayed by
    /// <see cref="LatencyFrames"/>, times a gain that drops ahead of every peak over the threshold and recovers slowly.
    /// No allocation after construction, no clock, no random numbers, no locks.
    /// </summary>
    public sealed class Limiter
    {
        /// <summary>Unity's largest speaker layout (7.1): the delay line always has room for it, so a change in the channel
        /// count never allocates.</summary>
        private const int MaxChannels = 8;
        /// <summary>The safety clamp: nothing leaves louder than this.</summary>
        private const float Ceiling = 0.98f;

        private readonly float _threshold;
        private readonly int _lookahead;
        private readonly double _attack, _release;
        private readonly int _width;

        // The delay line: _lookahead frames of _width samples, a ring.
        private readonly float[] _delay;
        private int _delayPos;
        private int _channels;

        // The running maximum of frame levels over the last _lookahead + 1 frames: a monotonic (decreasing) deque in a ring.
        private readonly long[] _maxIndex;
        private readonly float[] _maxValue;
        private int _maxHead, _maxCount;
        private long _frame;

        private double _gain = 1;

        public Limiter(int sampleRate, int channels, float threshold = 0.89f, float lookaheadMs = 2f, float releaseMs = 120f)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
            _threshold = threshold;
            _lookahead = Math.Max(1, (int)Math.Round(lookaheadMs / 1000.0 * sampleRate));
            // Eight time constants over the lookahead: when a peak leaves the delay line the gain has had L + 1 steps toward
            // it, leaving e^(-8(L+1)/L) (about 0.0003) of the drop, so even an input peak of 16 comes out under 0.895.
            _attack = 1 - Math.Exp(-8.0 / _lookahead);
            _release = Math.Exp(-1.0 / Math.Max(1e-9, releaseMs / 1000.0 * sampleRate));
            _width = Math.Max(MaxChannels, channels);
            _channels = channels;
            _delay = new float[_lookahead * _width];
            _maxIndex = new long[_lookahead + 1];
            _maxValue = new float[_lookahead + 1];
            ResetMeters();
        }

        /// <summary>The delay, in frames: round(lookahead × rate) (96 at 48 kHz, 88 at 44.1 kHz).</summary>
        public int LatencyFrames => _lookahead;

        /// <summary>The largest |sample| that came in since the last reset.</summary>
        public float InputPeak { get; private set; }
        /// <summary>The largest |sample| that went out since the last reset.</summary>
        public float OutputPeak { get; private set; }
        /// <summary>The lowest gain applied since the last reset (1 when nothing was limited).</summary>
        public float MinGain { get; private set; }
        /// <summary>Samples the safety clamp caught (over ±0.98) since the last reset: 0 when the limiter does its job.</summary>
        public int Clamped { get; private set; }
        /// <summary>Frames processed since the last reset.</summary>
        public long Frames { get; private set; }

        /// <summary>Starts the meters again. (On another thread than <see cref="Process(float[], int)"/>'s, set a flag there
        /// and call this from the audio thread.)</summary>
        public void ResetMeters()
        {
            InputPeak = 0;
            OutputPeak = 0;
            MinGain = 1;
            Clamped = 0;
            Frames = 0;
        }

        /// <summary>Limits interleaved samples in place. No allocation; safe on the audio thread.</summary>
        public void Process(float[] data, int channels) => Process(data, 0, data.Length, channels);

        /// <summary>Limits <paramref name="count"/> interleaved samples from <paramref name="offset"/> in place (whole
        /// frames only; a trailing partial frame is left untouched). No allocation; safe on the audio thread.</summary>
        public void Process(float[] data, int offset, int count, int channels)
        {
            if (channels <= 0 || channels > _width) return;
            if (channels != _channels)
            {
                // A new layout: what's in the delay line no longer lines up. Start it silent (no allocation).
                Array.Clear(_delay, 0, _delay.Length);
                _channels = channels;
            }
            float inPeak = InputPeak, outPeak = OutputPeak, minGain = MinGain;
            int clamped = Clamped;
            int frames = count / channels;
            double gain = _gain;
            for (int f = 0; f < frames; f++)
            {
                int at = offset + f * channels;
                float x = 0;
                for (int c = 0; c < channels; c++)
                {
                    float a = Math.Abs(data[at + c]);
                    if (a > x) x = a;
                }
                if (x > inPeak) inPeak = x;

                // The window's maximum: drop the frame that left it, then everything this frame outranks.
                long oldest = _frame - _lookahead;
                if (_maxCount > 0 && _maxIndex[_maxHead] < oldest) { _maxHead = (_maxHead + 1) % _maxIndex.Length; _maxCount--; }
                while (_maxCount > 0)
                {
                    int back = (_maxHead + _maxCount - 1) % _maxIndex.Length;
                    if (_maxValue[back] > x) break;
                    _maxCount--;
                }
                int slot = (_maxHead + _maxCount) % _maxIndex.Length;
                _maxIndex[slot] = _frame;
                _maxValue[slot] = x;
                _maxCount++;
                float peak = _maxValue[_maxHead];

                double target = peak > _threshold ? _threshold / peak : 1;
                if (target < gain) gain += (target - gain) * _attack;
                else gain = target + (gain - target) * _release;
                if (gain < minGain) minGain = (float)gain;

                // Out goes the frame from _lookahead frames ago; in goes this one.
                int d = _delayPos * _width;
                float g = (float)gain;
                for (int c = 0; c < channels; c++)
                {
                    float delayed = _delay[d + c];
                    _delay[d + c] = data[at + c];
                    float y = delayed * g;
                    if (y > Ceiling) { y = Ceiling; clamped++; }
                    else if (y < -Ceiling) { y = -Ceiling; clamped++; }
                    float a = Math.Abs(y);
                    if (a > outPeak) outPeak = a;
                    data[at + c] = y;
                }
                _delayPos = (_delayPos + 1) % _lookahead;
                _frame++;
            }
            _gain = gain;
            InputPeak = inPeak;
            OutputPeak = outPeak;
            MinGain = minGain;
            Clamped = clamped;
            Frames += frames;
        }
    }
}
