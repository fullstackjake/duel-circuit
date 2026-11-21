using System;
using UnityEngine;
using Ygo.Engine.Audio;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The end of the mix, on the AudioListener's GameObject (after the listener, so it sees the final mix): a lookahead
    /// limiter that holds every peak under -1 dBFS (<see cref="Limiter"/>, the engine's, tested by the harness), then the
    /// background fade (0.25 s, section D of docs/dev/AUDIO-PASS-1-SPEC-2026-11-20.md), then `-mute` zeroing the output
    /// (<see cref="Sounds.OutputMuted"/>). The meters are taken before the mute, so muted checks read real levels.
    /// <see cref="OnAudioFilterRead"/> runs on the audio thread: no Unity API, no allocation, no locks there; the main
    /// thread sets plain fields it reads (a torn read is harmless).
    /// </summary>
    public sealed class OutputLimiter : MonoBehaviour
    {
        private Limiter? _limiter;
        private int _rate, _channels;
        /// <summary>The device's rate the limiter was built for, as Unity reports it (0 with no device): compared as is.</summary>
        private int _deviceRate = -1;
        /// <summary>The audio thread saw a channel count the limiter wasn't built for: rebuilt on the main thread.</summary>
        private volatile int _seenChannels;

        // Written on the main thread, read on the audio thread.
        private static volatile float _targetGain = 1;
        private static volatile bool _snapGain;
        /// <summary>Bumped by each <see cref="ResetMeters"/>; the audio thread resets the limiter's meters when it sees a new one.</summary>
        private static int _generation;
        /// <summary>The audio thread's: the generation it last reset for (only it touches this).</summary>
        private int _appliedGeneration;
        private static volatile float _fadeStep = 1f / (0.25f * 48000);

        // Written on the audio thread, read on the main thread.
        private static volatile float _gain = 1, _inputPeak, _outputPeak, _minGain = 1;
        private static volatile int _clamped;
        private static long _frames;
        /// <summary>The generation the published meters belong to: meters from before the last reset never read back.</summary>
        private static volatile int _publishedGeneration;

        private static bool Current => _publishedGeneration == System.Threading.Volatile.Read(ref _generation);

        /// <summary>The limiter's meters since the last <see cref="ResetMeters"/>: the loudest sample in and out, the lowest gain, samples clamped.</summary>
        public static (float inputPeak, float outputPeak, float minGain, int clamped) Meters
        {
            get
            {
                var m = (_inputPeak, _outputPeak, _minGain, _clamped);
                return Current ? m : (0f, 0f, 1f, 0);
            }
        }

        /// <summary>Frames the limiter has processed since the last <see cref="ResetMeters"/> (a check's proof the filter sees the mix).</summary>
        public static long Frames
        {
            get
            {
                long frames = System.Threading.Interlocked.Read(ref _frames);
                return Current ? frames : 0;
            }
        }

        /// <summary>The background fade's gain now (1 = full, 0 = silent).</summary>
        public static float OutputGain => _gain;

        /// <summary>Starts the meters again (from the audio thread's next buffer; they read empty until it publishes).</summary>
        public static void ResetMeters() => System.Threading.Interlocked.Increment(ref _generation);

        /// <summary>Where the background fade heads (0 or 1), over 0.25 s; <paramref name="snap"/>: at once.</summary>
        internal static void SetTargetGain(float gain, bool snap)
        {
            _targetGain = gain;
            if (snap) _snapGain = true;
        }

        private void Awake() => Rebuild(ChannelsOf(AudioSettings.speakerMode));

        private void Update()
        {
            // A new output device (another rate or channel count): a limiter built for it.
            int seen = _seenChannels;
            if (NeedsRebuild(AudioSettings.outputSampleRate, _deviceRate, seen, _channels)) Rebuild(seen > 0 ? seen : ChannelsOf(AudioSettings.speakerMode));
        }

        /// <summary>
        /// Whether the limiter must be built again: the device's rate (as reported, 0 with no device) isn't the one it was built
        /// for, or the audio thread saw another channel count. Same rate, same channels: never (no allocation a frame).
        /// </summary>
        internal static bool NeedsRebuild(int deviceRate, int builtForRate, int seenChannels, int builtChannels) =>
            deviceRate != builtForRate || (seenChannels > 0 && seenChannels != builtChannels);

        private void Rebuild(int channels)
        {
            _deviceRate = AudioSettings.outputSampleRate;
            _rate = Math.Max(8000, _deviceRate);
            _channels = Math.Max(1, channels);
            _fadeStep = 1f / (0.25f * _rate);
            _limiter = new Limiter(_rate, _channels);
            _seenChannels = 0;
        }

        private static int ChannelsOf(AudioSpeakerMode mode) => mode switch
        {
            AudioSpeakerMode.Mono => 1,
            AudioSpeakerMode.Quad => 4,
            AudioSpeakerMode.Surround => 5,
            AudioSpeakerMode.Mode5point1 => 6,
            AudioSpeakerMode.Mode7point1 => 8,
            _ => 2,
        };

        private void OnAudioFilterRead(float[] data, int channels)
        {
            var limiter = _limiter;
            if (limiter != null && channels == _channels)
            {
                int generation = System.Threading.Volatile.Read(ref _generation);
                if (generation != _appliedGeneration) { _appliedGeneration = generation; limiter.ResetMeters(); }
                limiter.Process(data, channels);
                // Published with their generation: a reset while this buffer ran makes them read as empty (Meters).
                _inputPeak = limiter.InputPeak;
                _outputPeak = limiter.OutputPeak;
                _minGain = limiter.MinGain;
                _clamped = limiter.Clamped;
                System.Threading.Interlocked.Exchange(ref _frames, limiter.Frames);
                _publishedGeneration = generation;
            }
            else _seenChannels = channels; // not limited this buffer; the main thread builds one that fits

            // The background fade, per frame.
            float gain = _gain, target = _targetGain, step = _fadeStep;
            if (_snapGain) { _snapGain = false; gain = target; }
            if (gain != target || gain != 1f)
            {
                for (int i = 0; i + channels <= data.Length; i += channels)
                {
                    if (gain < target) gain = Math.Min(target, gain + step);
                    else if (gain > target) gain = Math.Max(target, gain - step);
                    for (int c = 0; c < channels; c++) data[i + c] *= gain;
                }
            }
            _gain = gain;

            if (Sounds.OutputMuted) Array.Clear(data, 0, data.Length);
        }
    }
}
