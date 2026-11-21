using System;
using System.Collections.Generic;

namespace Ygo.Engine.Audio
{
    /// <summary>
    /// Tuning the tonal effects to the music (docs/dev/AUDIO-PASS-1-SPEC-2026-11-20.md sections F and I): the pitch class
    /// each tuned effect is built on, the chain's steps, the menu loop's key and the nearest-way key ratio. The game
    /// plays a tuned effect at the call's pitch × KeyRatio(root, the music's key), with no random jitter.
    /// </summary>
    public static partial class Sfx
    {
        /// <summary>The pitch class each tuned effect is built on (section I). Summons, thuds, swishes and noise aren't tuned.</summary>
        public static readonly IReadOnlyDictionary<string, int> TunedRoots = new Dictionary<string, int>
        {
            // The prize chimes are built on E (see Sfx.Prize.cs).
            ["prize_secret"] = 4, ["prize_ghost"] = 4, ["prize_starlight"] = 4, ["prize_ping"] = 4, ["prize_glint"] = 4,
            // Built on C.
            ["win"] = 0, ["lose"] = 0, ["recover"] = 0, ["level_up"] = 0, ["activate"] = 0, ["phase"] = 0, ["chain_resolve"] = 0,
            // The turn chime is a G and a D: built on G.
            ["turn"] = 7,
            // The draw-game chord is A, D and E over A.
            ["draw_game"] = 9,
        };

        /// <summary>Semitones per chain link, links 1-4 (later links use the last): root, 2nd, 5th, octave.</summary>
        public static readonly IReadOnlyList<int> ChainSteps = new[] { 0, 2, 7, 12 };

        /// <summary>The menu loop's key (A minor: its chords are Am F C G).</summary>
        public const int MenuKey = 9;

        /// <summary>The pitch ratio moving a sound built on <paramref name="from"/> to key <paramref name="to"/>, the
        /// nearest way (-6..+5 semitones; a tritone goes down).</summary>
        public static double KeyRatio(int from, int to)
        {
            int s = (((to - from + 6) % 12) + 12) % 12 - 6;
            return Math.Pow(2, s / 12.0);
        }
    }
}
