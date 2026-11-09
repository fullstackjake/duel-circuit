using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ygo.Engine.Audio
{
    /// <summary>
    /// The game's sound effects and music, synthesized from code (no audio files ship with the game): tones with pitch
    /// sweeps, filtered noise, envelopes and a little echo. Every sound is mono, 44.1 kHz, peak-normalised; the game
    /// turns them into clips at startup, and the harness can write them out as WAV files (--export-sounds).
    /// </summary>
    public static partial class Sfx
    {
        public const int Rate = 44100;

        /// <summary>Every effect by name. Music loops are in <see cref="Music"/>.</summary>
        public static readonly IReadOnlyDictionary<string, Func<float[]>> Effects = new Dictionary<string, Func<float[]>>
        {
            // Cards moving.
            ["draw"] = () => Normalize(Swish(0.16, 2500, 6000, 0.5), 0.55),
            // A riffle: seven short, quiet ticks of bright noise over 0.3 s, each a little lower than the last.
            ["shuffle"] = () => Normalize(Mix(Enumerable.Range(0, 7).Select(i =>
                Delay(Noise(0.035, 6000 - i * 300, 2600 + i * 120, 0.003, 0.032, i % 2 == 0 ? 1.0 : 0.75), i * 0.042)).ToArray()), 0.35),
            ["move"] = () => Normalize(Swish(0.2, 1200, 4000, 0.6), 0.5),
            ["place"] = () => Normalize(Mix(Thud(0.12, 160, 0.9), Swish(0.06, 3000, 1500, 0.3)), 0.6),
            ["flip"] = () => Normalize(Mix(Swish(0.09, 4000, 7000, 0.6), Tone(1800, 2400, 0.05, Wave.Sine, 0.002, 0.04, 0.25)), 0.5),
            // Summons: a rising sweep and a chord; special summons shimmer; big ones add a low boom.
            ["summon"] = () => Normalize(Mix(Tone(220, 660, 0.35, Wave.Saw, 0.01, 0.3, 0.35, 1800), Chord(new[] { 523.3, 659.3, 784.0 }, 0.45, 0.12, 0.4, 0.15)), 0.8),
            ["special"] = () => Normalize(Mix(Tone(330, 990, 0.3, Wave.Square, 0.01, 0.25, 0.25, 2500), Shimmer(0.7, 0.15),
                Chord(new[] { 587.3, 740.0, 880.0 }, 0.5, 0.15, 0.45, 0.12)), 0.8),
            ["summon_big"] = () => Normalize(Echo(Mix(Thud(0.8, 55, 1.0), Tone(110, 440, 0.6, Wave.Saw, 0.02, 0.5, 0.35, 1200),
                Chord(new[] { 261.6, 392.0, 523.3, 659.3 }, 0.9, 0.25, 0.8, 0.18), Shimmer(1.0, 0.3)), 0.12, 0.3), 0.9),
            // Effects: a bright activation chime; a chain link adds a higher ping; negation is a falling buzz.
            ["activate"] = () => Normalize(Echo(Mix(Tone(880, 1320, 0.08, Wave.Sine, 0.003, 0.07, 0.5), Chord(new[] { 1046.5, 1318.5, 1568.0 }, 0.5, 0.06, 0.45, 0.2)), 0.09, 0.35), 0.7),
            // The effect (not the activation) negated: hollower and lower, so the two can be told apart by ear.
            ["negate_effect"] = () => Normalize(Mix(Tone(420, 140, 0.4, Wave.Triangle, 0.005, 0.36, 0.5, 900), Noise(0.25, 600, 200, 0.01, 0.22, 0.2)), 0.65),
            ["negate"] = () => Normalize(Mix(Tone(600, 90, 0.45, Wave.Square, 0.005, 0.4, 0.5, 1400), Noise(0.3, 900, 300, 0.005, 0.28, 0.3)), 0.75),
            // Battle.
            ["attack"] = () => Normalize(Mix(Swish(0.3, 800, 5000, 0.8), Tone(150, 300, 0.25, Wave.Saw, 0.05, 0.2, 0.2, 900)), 0.75),
            ["impact"] = () => Normalize(Mix(Thud(0.35, 70, 1.0), Noise(0.25, 5000, 400, 0.001, 0.22, 0.8)), 0.95),
            ["destroy"] = () => Normalize(Mix(Noise(0.6, 3000, 200, 0.002, 0.55, 0.8), Tone(400, 60, 0.5, Wave.Saw, 0.005, 0.45, 0.3, 700)), 0.8),
            // Life points.
            ["damage"] = () => Normalize(Mix(Tone(330, 110, 0.4, Wave.Square, 0.005, 0.38, 0.45, 1500), Thud(0.3, 80, 0.7)), 0.8),
            ["damage_big"] = () => Normalize(Echo(Mix(Tone(260, 55, 0.7, Wave.Saw, 0.005, 0.65, 0.5, 1200), Thud(0.6, 50, 1.0), Noise(0.5, 2500, 150, 0.002, 0.45, 0.5)), 0.1, 0.3), 0.95),
            ["recover"] = () => Normalize(Arpeggio(new[] { 523.3, 659.3, 784.0, 1046.5 }, 0.07, 0.3, Wave.Sine), 0.6),
            ["cost"] = () => Normalize(Tone(500, 350, 0.18, Wave.Triangle, 0.005, 0.16, 0.6), 0.5),
            ["lp_tick"] = () => Normalize(Tone(1500, 1500, 0.025, Wave.Square, 0.001, 0.02, 0.4, 3000), 0.25),
            // Turn flow and results.
            ["turn"] = () => Normalize(Mix(Tone(392, 392, 0.12, Wave.Triangle, 0.005, 0.1, 0.5), Tone(587.3, 587.3, 0.2, Wave.Triangle, 0.005, 0.18, 0.5), 0.1), 0.5),
            ["win"] = () => Normalize(Echo(Arpeggio(new[] { 523.3, 659.3, 784.0, 1046.5, 1318.5, 1568.0, 2093.0 }, 0.09, 0.9, Wave.Square, 3000), 0.15, 0.3), 0.75),
            ["lose"] = () => Normalize(Echo(Arpeggio(new[] { 392.0, 349.2, 311.1, 261.6, 196.0 }, 0.18, 0.9, Wave.Triangle), 0.2, 0.3), 0.65),
            // Menus and the shop.
            ["click"] = () => Normalize(Tone(1200, 900, 0.035, Wave.Square, 0.001, 0.03, 0.4, 4000), 0.3),
            ["hover"] = () => Normalize(Tone(2200, 2200, 0.018, Wave.Sine, 0.001, 0.016, 0.4), 0.12),
            ["error"] = () => Normalize(Mix(Tone(220, 220, 0.12, Wave.Square, 0.003, 0.1, 0.4, 1200), Tone(180, 180, 0.15, Wave.Square, 0.003, 0.13, 0.4, 1200), 0.12), 0.45),
            ["pack_tear"] = () => Normalize(Noise(0.45, 6000, 1500, 0.01, 0.4, 0.9, crackle: true), 0.7),
            ["rare"] = () => Normalize(Echo(Mix(Shimmer(0.8, 0.2), Chord(new[] { 783.99, 987.8, 1174.7 }, 0.7, 0.05, 0.6, 0.18)), 0.1, 0.35), 0.7),
            ["burst"] = () => Normalize(Echo(Mix(Swish(0.4, 1500, 9000, 0.8), Shimmer(1.2, 0.35), Chord(new[] { 523.3, 784.0, 1046.5, 1568.0 }, 1.2, 0.08, 1.1, 0.16), Thud(0.4, 90, 0.6)), 0.14, 0.35), 0.9),
            ["dp"] = () => Normalize(Mix(Tone(1568, 1568, 0.08, Wave.Square, 0.002, 0.07, 0.3, 4000), Tone(2093, 2093, 0.25, Wave.Square, 0.002, 0.23, 0.3, 4000), 0.07), 0.4),
            ["level_up"] = () => Normalize(Echo(Mix(Arpeggio(new[] { 392.0, 523.3, 659.3, 784.0 }, 0.08, 0.4, Wave.Square, 3500),
                Chord(new[] { 523.3, 659.3, 784.0, 1046.5 }, 0.9, 0.32, 0.85, 0.2), Shimmer(1.2, 0.35)), 0.15, 0.3), 0.8),
            // The prized moment (see Sfx.Prize.cs): played tuned to the duel music.
            ["prize_secret"] = PrizeSecret,
            ["prize_ghost"] = PrizeGhost,
            ["prize_starlight"] = PrizeStarlight,
            ["prize_ping"] = PrizePing,
            ["prize_glint"] = PrizeGlint,
            ["token"] = () => Normalize(Echo(Mix(Tone(1046.5, 2093, 0.25, Wave.Sine, 0.003, 0.22, 0.5), Shimmer(0.7, 0.05)), 0.1, 0.4), 0.65),
        };

        /// <summary>Music loops by name: "menu" (calm). It loops cleanly. Duels play layered themes (<see cref="Themes"/>).</summary>
        public static readonly IReadOnlyDictionary<string, Func<float[]>> Music = new Dictionary<string, Func<float[]>>
        {
            ["menu"] = () => Normalize(Song(84, new[] { new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 48, 52, 55 }, new[] { 55, 59, 62 } }, drums: false), 0.6),
        };

        // ---- Building blocks -------------------------------------------------------------------------

        public enum Wave { Sine, Square, Saw, Triangle }

        private static int Samples(double seconds) => Math.Max(1, (int)(seconds * Rate));

        private static double Osc(Wave wave, double phase)
        {
            phase -= Math.Floor(phase);
            return wave switch
            {
                Wave.Sine => Math.Sin(2 * Math.PI * phase),
                Wave.Square => phase < 0.5 ? 1 : -1,
                Wave.Saw => 2 * phase - 1,
                _ => 1 - 4 * Math.Abs(phase - 0.5),
            };
        }

        /// <summary>Attack, then an exponential-ish decay to silence at the end.</summary>
        private static double Env(double t, double attack, double length) =>
            t < attack ? t / attack : Math.Max(0, Math.Pow(1 - (t - attack) / Math.Max(1e-6, length - attack), 2));

        /// <summary>A tone gliding from <paramref name="f0"/> to <paramref name="f1"/>, optionally low-passed (cutoff in Hz).</summary>
        public static float[] Tone(double f0, double f1, double seconds, Wave wave, double attack, double decay, double volume, double cutoff = 0)
        {
            int n = Samples(Math.Max(seconds, attack + decay));
            var s = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate, k = Math.Min(1, t / seconds);
                phase += (f0 * Math.Pow(f1 / f0, k)) / Rate;
                s[i] = (float)(Osc(wave, phase) * Env(t, attack, attack + decay) * volume);
            }
            return cutoff > 0 ? LowPass(s, cutoff) : s;
        }

        /// <summary>Notes together, each a soft sine with a slower decay (a "ding").</summary>
        private static float[] Chord(double[] freqs, double seconds, double delay, double decay, double volume)
        {
            var parts = freqs.Select(f => Mix(Tone(f, f, seconds, Wave.Sine, 0.005, decay, volume), Tone(f * 2, f * 2, seconds, Wave.Sine, 0.005, decay * 0.6, volume * 0.3))).ToArray();
            return Delay(Mix(parts), delay);
        }

        /// <summary>Notes one after another.</summary>
        private static float[] Arpeggio(double[] freqs, double step, double tail, Wave wave, double cutoff = 0)
        {
            var notes = freqs.Select((f, i) => Delay(Tone(f, f, step + tail, wave, 0.004, step + tail * (i == freqs.Length - 1 ? 1 : 0.3), 0.4, cutoff), step * i)).ToArray();
            return Mix(notes);
        }

        /// <summary>Filtered noise: the cutoff glides from <paramref name="c0"/> to <paramref name="c1"/>.</summary>
        private static float[] Noise(double seconds, double c0, double c1, double attack, double decay, double volume, bool crackle = false)
        {
            var rng = new Random(1234);
            int n = Samples(seconds);
            var s = new float[n];
            double y = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate, k = t / seconds;
                double cutoff = c0 * Math.Pow(c1 / c0, k);
                double a = 1 - Math.Exp(-2 * Math.PI * cutoff / Rate);
                double x = rng.NextDouble() * 2 - 1;
                if (crackle && rng.NextDouble() < 0.02) x *= 4;
                y += a * (x - y);
                s[i] = (float)(y * Env(t, attack, attack + decay) * volume * 2);
            }
            return s;
        }

        /// <summary>A whoosh: band of noise sweeping up in pitch.</summary>
        private static float[] Swish(double seconds, double c0, double c1, double volume) =>
            Noise(seconds, c0, c1, seconds * 0.4, seconds * 0.6, volume);

        /// <summary>A low drum hit: a fast downward sine sweep.</summary>
        private static float[] Thud(double seconds, double freq, double volume) =>
            Tone(freq * 2.5, freq, seconds, Wave.Sine, 0.002, seconds, volume);

        /// <summary>High sparkly partials fading in and out at random: foil and magic.</summary>
        private static float[] Shimmer(double seconds, double delay)
        {
            var rng = new Random(99);
            var parts = Enumerable.Range(0, 14).Select(i =>
            {
                double f = 2000 + rng.NextDouble() * 4500;
                return Delay(Tone(f, f * 1.02, 0.25, Wave.Sine, 0.01, 0.22, 0.12), rng.NextDouble() * seconds * 0.7);
            }).ToArray();
            return Delay(Mix(parts), delay);
        }

        public static float[] Mix(params float[][] parts)
        {
            var s = new float[parts.Max(p => p.Length)];
            foreach (var p in parts)
                for (int i = 0; i < p.Length; i++) s[i] += p[i];
            return s;
        }

        /// <summary>Two sounds, the second starting <paramref name="offset"/> seconds in.</summary>
        private static float[] Mix(float[] a, float[] b, double offset) => Mix(a, Delay(b, offset));

        private static float[] Delay(float[] s, double seconds)
        {
            int d = Samples(seconds);
            if (seconds <= 0) return s;
            var o = new float[s.Length + d];
            Array.Copy(s, 0, o, d, s.Length);
            return o;
        }

        private static float[] Echo(float[] s, double seconds, double feedback)
        {
            int d = Samples(seconds);
            var o = new float[s.Length + d * 4];
            Array.Copy(s, o, s.Length);
            for (int i = d; i < o.Length; i++) o[i] += (float)(o[i - d] * feedback);
            return o;
        }

        private static float[] LowPass(float[] s, double cutoff)
        {
            double a = 1 - Math.Exp(-2 * Math.PI * cutoff / Rate), y = 0;
            var o = new float[s.Length];
            for (int i = 0; i < s.Length; i++) o[i] = (float)(y += a * (s[i] - y));
            return o;
        }

        /// <summary>Scales to a peak of <paramref name="peak"/>, with a 5 ms fade at each end (no clicks).</summary>
        public static float[] Normalize(float[] s, double peak)
        {
            float max = s.Max(x => Math.Abs(x));
            if (max <= 0) return s;
            double scale = peak / max;
            int fade = Math.Min(s.Length / 2, Samples(0.005));
            for (int i = 0; i < s.Length; i++)
            {
                double edge = Math.Min(1, Math.Min(i, s.Length - 1 - i) / (double)Math.Max(1, fade));
                s[i] = (float)(s[i] * scale * (i < fade || i >= s.Length - fade ? edge : 1));
            }
            return s;
        }

        // ---- Music -----------------------------------------------------------------------------------

        private static double Midi(int note) => 440 * Math.Pow(2, (note - 69) / 12.0);

        /// <summary>
        /// A short loop: four chords (MIDI note triads), two bars each, as a soft pad, a bass line, an arpeggio on top,
        /// and optional drums. The tail wraps around to the start so it loops without a seam.
        /// </summary>
        private static float[] Song(double bpm, int[][] chords, bool drums)
        {
            double beat = 60 / bpm, bar = beat * 4;
            int length = Samples(bar * 2 * chords.Length);
            var song = new float[length];
            void Add(float[] part, double at)
            {
                int start = Samples(at);
                for (int i = 0; i < part.Length; i++) song[(start + i) % length] += part[i];
            }
            for (int c = 0; c < chords.Length; c++)
            {
                double at = c * bar * 2;
                var chord = chords[c];
                foreach (var note in chord)
                    Add(Tone(Midi(note), Midi(note), bar * 2, Wave.Saw, bar * 0.4, bar * 1.6, 0.07, 900), at);
                for (int b = 0; b < 8; b++)
                    Add(Tone(Midi(chord[0] - 12), Midi(chord[0] - 12), beat * 0.9, Wave.Triangle, 0.01, beat * 0.85, drums ? 0.32 : 0.22, 600), at + b * beat);
                for (int s = 0; s < 16; s++)
                {
                    int note = chord[s % 3] + 12 * (s % 6 < 3 ? 1 : 0);
                    Add(Tone(Midi(note), Midi(note), beat * 0.45, Wave.Square, 0.004, beat * 0.4, drums ? 0.06 : 0.045, 2200), at + s * beat / 2);
                }
                if (!drums) continue;
                for (int b = 0; b < 8; b++)
                {
                    Add(Thud(0.25, 55, 0.5), at + b * beat);
                    if (b % 2 == 1) Add(Noise(0.15, 4000, 1500, 0.002, 0.14, 0.25), at + b * beat);
                    Add(Noise(0.04, 9000, 7000, 0.001, 0.035, 0.08), at + b * beat + beat / 2);
                }
            }
            return song;
        }

        // ---- Files -----------------------------------------------------------------------------------

        /// <summary>Writes 16-bit mono PCM WAV.</summary>
        public static void WriteWav(string path, float[] samples)
        {
            using var w = new BinaryWriter(File.Create(path));
            int bytes = samples.Length * 2;
            w.Write("RIFF".ToCharArray()); w.Write(36 + bytes); w.Write("WAVE".ToCharArray());
            w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data".ToCharArray()); w.Write(bytes);
            foreach (var x in samples) w.Write((short)Math.Round(Math.Max(-1, Math.Min(1, x)) * 32767));
        }
    }
}
