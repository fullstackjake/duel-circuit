using System;
using System.Linq;

namespace Ygo.Engine.Audio
{
    /// <summary>
    /// The prized moment's chimes (a Gem Mint Secret Rare and up arriving face-up on your side): Secret is a bright
    /// glass arpeggio, Ghost an airy glass-harmonica swell, Starlight a shimmering cascade, plus a crown ping for Gem
    /// Mint and a soft glint for the glint-only rarities. All are built on E (the everyday theme's key) from scale
    /// degrees 1, 2 and 5 only, so they sit over major and minor music alike; Sounds plays them tuned to the music.
    /// </summary>
    public static partial class Sfx
    {
        /// <summary>The chimes' root: E6.</summary>
        public const double PrizeRoot = 1318.5;
        private const double Fifth = 1.4983, Ninth = 2.2449, Second = 1.1225;

        /// <summary>A glass-bar bell: inharmonic partials, the high ones dying first (none above 11 kHz).</summary>
        private static float[] GlassBell(double f, double decay, double volume)
        {
            var ratios = new[] { 1, 2.756, 5.404, 8.933 };
            var amps = new[] { 1.0, 0.45, 0.22, 0.10 };
            var decays = new[] { decay, 0.30, 0.14, 0.07 };
            return Mix(ratios.Select((r, i) => f * r > 11000 ? new float[1]
                : Tone(f * r, f * r, decays[i] + 0.002, Wave.Sine, 0.002, decays[i], volume * amps[i])).ToArray());
        }

        /// <summary>A two-operator FM bell: the modulation index falls to 0, a bright strike with a pure tail.</summary>
        private static float[] FmBell(double f, double ratio, double index, double indexFall, double attack, double decay, double volume)
        {
            int n = Samples(attack + decay);
            var s = new float[n];
            double carrier = 0, modulator = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                double k = index * Math.Max(0, 1 - t / indexFall);
                modulator += f * ratio / Rate;
                carrier += f / Rate;
                s[i] = (float)(Math.Sin(2 * Math.PI * carrier + k * Math.Sin(2 * Math.PI * modulator)) * Env(t, attack, attack + decay) * volume);
            }
            return s;
        }

        /// <summary>Secret: a rising 1-5-9 arpeggio of glass bells, each with a strike tick.</summary>
        private static float[] PrizeSecret()
        {
            var notes = new[] { 1, Fifth, Ninth }.Select((r, i) => Delay(Mix(
                GlassBell(PrizeRoot * r, i == 2 ? 0.75 : 0.6, 0.5),
                Noise(0.012, 9000, 6000, 0.001, 0.01, 0.12)), i * 0.07)).ToArray();
            return Normalize(Echo(Mix(notes), 0.07, 0.22), 0.6);
        }

        /// <summary>A glass-harmonica note: odd harmonics, three voices a few cents apart, gliding in from just sharp.</summary>
        private static float[] Harmonica(double f, double volume)
        {
            var voices = new[] { 0.0, 5, -5 }.Select(cents =>
            {
                double g = f * Math.Pow(2, cents / 1200);
                return Mix(new[] { 1, 3, 5 }.Select((h, i) =>
                    Tone(g * h * Math.Pow(2, 20 / 1200.0), g * h, 0.12, Wave.Sine, 0.07, 0.65, volume * new[] { 1, 0.16, 0.05 }[i] / 3)).ToArray());
            }).ToArray();
            return Mix(voices);
        }

        /// <summary>Ghost: a soft swell on the root and fifth, a faint ninth, breath, and a slow tremolo.</summary>
        private static float[] PrizeGhost()
        {
            var s = Mix(
                Harmonica(PrizeRoot, 1),
                Delay(Harmonica(PrizeRoot * Fifth, 0.55), 0.09),
                Delay(Tone(PrizeRoot * Ninth, PrizeRoot * Ninth, 0.4, Wave.Sine, 0.01, 0.4, 0.15), 0.22),
                Noise(0.5, 5500, 2500, 0.04, 0.45, 0.07));
            for (int i = 0; i < s.Length; i++) s[i] *= (float)(1 - 0.15 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 5.5 * i / Rate)));
            return Normalize(Echo(s, 0.12, 0.3), 0.5);
        }

        /// <summary>Starlight: six FM bells cascading down to the root, a landing pad, and tuned sparkle above.</summary>
        private static float[] PrizeStarlight()
        {
            var ratios = new[] { 2.9966, Ninth, 2, Fifth, Second, 1 };
            var cascade = ratios.Select((r, i) => Delay(FmBell(PrizeRoot * r, 3.5, 1.2, 0.15, 0.002, i == ratios.Length - 1 ? 0.7 : 0.4, 0.4), i * 0.05)).ToArray();
            var pad = Delay(Mix(new[] { 1, Fifth, Ninth }.Select(r => Tone(PrizeRoot * r, PrizeRoot * r, 0.8, Wave.Sine, 0.15, 0.65, 0.25)).ToArray()), 0.25);
            var rng = new Random(7);
            var degrees = new[] { 1, Second, Fifth };
            var pips = Enumerable.Range(0, 8).Select(_ =>
            {
                double f = PrizeRoot * degrees[rng.Next(3)] * (rng.Next(2) == 0 ? 2 : 4);
                return f > 11000 ? new float[1] : Delay(Tone(f, f, 0.123, Wave.Sine, 0.003, 0.12, 0.10), rng.NextDouble() * 0.6);
            }).ToArray();
            return Normalize(Echo(Mix(Mix(cascade), pad, Mix(pips)), 0.06, 0.25), 0.6);
        }

        /// <summary>Gem Mint's crown: a metallic "ting" two octaves over the root.</summary>
        private static float[] PrizePing() => Normalize(Echo(FmBell(PrizeRoot * 4, 1.0, 0.8, 0.06, 0.001, 0.6, 1), 0.09, 0.3), 0.4);

        /// <summary>The glint-only rarities: a soft high bell.</summary>
        private static float[] PrizeGlint() =>
            Normalize(Echo(Mix(Tone(PrizeRoot * 2, PrizeRoot * 2, 0.35, Wave.Sine, 0.002, 0.35, 1), Tone(PrizeRoot * 3, PrizeRoot * 3, 0.35, Wave.Sine, 0.002, 0.35, 0.15)), 0.06, 0.2), 0.3);
    }
}
