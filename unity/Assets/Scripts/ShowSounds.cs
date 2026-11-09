using System;
using Ygo.Engine.Audio;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The headliners' show's sounds, synthesized like <see cref="Sfx"/> and added to <see cref="Sounds"/> by name: the
    /// "going for it" sting (a rising brass stab and a hit), the crowd's roar at a payoff and its groan at a fizzle (a
    /// room of voices: detuned low saws under filtered noise, gliding up for the roar, sagging down for the groan).
    /// </summary>
    public static class ShowSounds
    {
        public const string Sting = "show_sting", Roar = "show_roar", Groan = "show_groan";
        /// <summary>How many effects <see cref="Register"/> adds (the menu check counts every clip built).</summary>
        public const int Count = 3;

        /// <summary>Adds the three to <see cref="Sounds"/> (once; later calls do nothing).</summary>
        public static void Register()
        {
            Sounds.AddEffect(Sting, BuildSting);
            Sounds.AddEffect(Roar, () => Crowd(up: true));
            Sounds.AddEffect(Groan, () => Crowd(up: false));
        }

        private static float[] BuildSting()
        {
            // Two stabs, the second a fourth higher, over a low hit: "here it comes".
            var low = Sfx.Tone(55, 45, 0.5, Sfx.Wave.Sine, 0.003, 0.45, 0.9);
            var stab1 = Stab(new[] { 220.0, 277.2, 329.6 }, 0.16, 0);
            var stab2 = Stab(new[] { 293.7, 370.0, 440.0, 587.3 }, 0.6, 0.18);
            return Sfx.Normalize(Sfx.Mix(low, stab1, stab2), 0.8);
        }

        /// <summary>A brass-ish chord: saws rising a little into pitch, low-passed, starting <paramref name="at"/> seconds in.</summary>
        private static float[] Stab(double[] freqs, double seconds, double at)
        {
            var parts = new float[freqs.Length][];
            for (int i = 0; i < freqs.Length; i++) parts[i] = Delay(Sfx.Tone(freqs[i] * 0.97, freqs[i], seconds, Sfx.Wave.Saw, 0.015, seconds * 0.9, 0.3, 2200), at);
            return Sfx.Mix(parts);
        }

        /// <summary>A crowd: a dozen voices on a vowel, gliding together, and the room's breath.</summary>
        private static float[] Crowd(bool up)
        {
            var rng = new Random(up ? 0x50A2 : 0x6A0A);
            double seconds = up ? 1.6 : 1.4;
            var parts = new float[13][];
            for (int i = 0; i < 12; i++)
            {
                double f = 140 + rng.NextDouble() * 120;
                double to = up ? f * (1.15 + rng.NextDouble() * 0.1) : f * (0.62 + rng.NextDouble() * 0.08);
                parts[i] = Delay(Sfx.Tone(f, to, seconds, Sfx.Wave.Saw, 0.08 + rng.NextDouble() * 0.1, seconds * 0.85, 0.12, up ? 1500 : 800), rng.NextDouble() * 0.08);
            }
            parts[12] = Noise(seconds, up ? 2600 : 900, 0.35, rng);
            return Sfx.Normalize(Sfx.Mix(parts), up ? 0.7 : 0.6);
        }

        /// <summary>Low-passed noise with a soft swell and fade.</summary>
        private static float[] Noise(double seconds, double cutoff, double volume, Random rng)
        {
            int n = (int)(seconds * Sfx.Rate);
            var s = new float[n];
            double a = 1 - Math.Exp(-2 * Math.PI * cutoff / Sfx.Rate), y = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n;
                double env = Math.Min(1, t * 6) * Math.Min(1, (1 - t) * 2.5);
                y += a * (rng.NextDouble() * 2 - 1 - y);
                s[i] = (float)(y * env * volume);
            }
            return s;
        }

        private static float[] Delay(float[] s, double seconds)
        {
            int d = (int)(seconds * Sfx.Rate);
            if (d <= 0) return s;
            var o = new float[s.Length + d];
            Array.Copy(s, 0, o, d, s.Length);
            return o;
        }
    }
}
