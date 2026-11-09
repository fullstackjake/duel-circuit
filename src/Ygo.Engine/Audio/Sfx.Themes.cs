using System;
using System.Collections.Generic;
using System.Linq;

namespace Ygo.Engine.Audio
{
    /// <summary>
    /// A layered music theme: one chord per bar, a melody and a groove. It renders (<see cref="Sfx.Render"/>) as layers
    /// of exactly the same length that loop in sync, so the game can fade them in and out as a duel changes.
    /// </summary>
    public sealed class Theme
    {
        public string Name = "";
        public double Bpm = 120;
        /// <summary>The tonic's pitch class (C = 0) and the mode.</summary>
        public int Key;
        public bool Minor;
        /// <summary>One chord per bar, bars split by "|": "Am | F | C7 | Gsus". Qualities: m, 7, m7, maj7, dim, sus.</summary>
        public string Chords = "";
        /// <summary>
        /// Notes with their length in eighths ("E5:2", a rest "r:2"), bars split by "|", 8 eighths a bar. It repeats to
        /// fill the chords. Empty: the lead is an arpeggio of the chords instead (no tune, for the everyday duel music).
        /// </summary>
        public string Melody = "";
        public Sfx.Voice Lead = Sfx.Voice.Square;
        /// <summary>Semitones added to the melody (it's written an octave up, where it's easier to read).</summary>
        public int LeadShift = -12;
        /// <summary>A second voice a third below the melody, in the key.</summary>
        public bool Harmony;
        /// <summary>The melody doubled an octave below.</summary>
        public bool Octave;
        /// <summary>The drum pattern (see Sfx.Grooves) and the bass line: quarters, eighths, offbeat, gallop, walk, long.</summary>
        public string Groove = "pop";
        public string Bass = "quarters";
        /// <summary>How late the off-beat eighths land, in sixteenths (0.33 is a shuffle).</summary>
        public double Swing;
    }

    /// <summary>A rendered theme: its looping layers by name (<see cref="Sfx.LayerNames"/>), plus three one-shots.</summary>
    public sealed class ThemeMusic
    {
        public Theme Theme = new Theme();
        public int Bars;
        public double BarSeconds;
        public Dictionary<string, float[]> Layers = new Dictionary<string, float[]>();
        /// <summary>Before the duel (the versus screen), at a win, and at a loss.</summary>
        public float[] Sting = new float[0], Victory = new float[0], Defeat = new float[0];
    }

    public static partial class Sfx
    {
        /// <summary>
        /// The layers every theme renders, all the same length:
        /// bed (pad and bass, always on), drums, lead (the tune, when the boss's ace is out), drive (double-time hats and
        /// a pumping bass, when someone's LP is low), dread (a drone and a ticking clock: you're low), hope (a rising
        /// sparkle: they're low), lethal (a heartbeat alone, while an attack that would end the duel is pending).
        /// </summary>
        public static readonly string[] LayerNames = { "bed", "drums", "lead", "drive", "dread", "hope", "lethal" };

        /// <summary>
        /// The layered themes. "duel" is the everyday duel music (the old loop's chords, no tune); the others are bosses':
        /// one per band (each circuit boss in it a variation), one per special guest, and one for the Tag Cup finals.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, Theme> Themes = new[]
        {
            new Theme
            {
                Name = "duel", Bpm = 118, Key = 4, Minor = true, Groove = "rock", Bass = "quarters",
                Chords = "Em | Em | C | C | D | D | Bm | Bm | Em | Em | C | C | D | D | Bm | Bm",
            },
            // Beginner: the card shop. Bright, bouncy, C major.
            new Theme
            {
                Name = "beginner", Bpm = 140, Key = 0, Groove = "pop", Bass = "eighths", Lead = Voice.Square,
                Chords = "C | G | Am | F | C | G | F | G | Am | F | C | G | Dm | G | C | G",
                Melody = "E5:1 G5:1 C6:2 G5:1 E5:1 G5:2 | D5:1 G5:1 B5:2 A5:1 G5:1 D5:2 | C5:1 E5:1 A5:2 G5:1 E5:1 C5:1 E5:1 | F5:3 E5:1 D5:2 C5:2 | "
                       + "E5:1 G5:1 C6:2 D6:1 C6:1 B5:1 C6:1 | D6:2 B5:2 G5:2 r:2 | A5:1 C6:1 A5:1 F5:1 G5:2 A5:2 | G5:6 r:2 | "
                       + "A4:2 C5:2 E5:2 A5:2 | G5:1 F5:1 E5:2 C5:4 | E5:2 G5:2 C6:2 E6:2 | D6:1 C6:1 B5:2 G5:4 | "
                       + "F5:2 A5:2 D6:2 C6:1 A5:1 | B5:2 D6:2 G5:4 | C6:1 B5:1 C6:1 D6:1 E6:2 G5:2 | D6:3 B5:1 G5:2 r:2",
            },
            // Novice: underdogs and trap decks. Sly and syncopated, D minor.
            new Theme
            {
                Name = "novice", Bpm = 126, Key = 2, Minor = true, Groove = "sneak", Bass = "eighths", Lead = Voice.Saw,
                Chords = "Dm | Dm | Bb | C | Dm | Dm | Gm | A | Bb | C | Dm | Am | Bb | C | A | A",
                Melody = "D5:1 r:1 F5:1 D5:1 A5:2 G5:1 F5:1 | E5:1 F5:1 D5:2 r:4 | D5:1 r:1 F5:1 D5:1 Bb5:2 A5:1 G5:1 | A5:2 G5:1 E5:1 C5:4 | "
                       + "D5:1 r:1 F5:1 D5:1 A5:2 C6:1 A5:1 | D6:2 C6:1 A5:1 F5:2 r:2 | G5:2 Bb5:2 D6:2 C6:1 Bb5:1 | A5:4 C#5:2 E5:2 | "
                       + "F5:2 D5:2 Bb4:2 D5:2 | E5:2 G5:2 C6:3 r:1 | A5:1 G5:1 F5:1 E5:1 D5:2 F5:2 | E5:2 C5:2 A4:4 | "
                       + "D5:1 F5:1 Bb5:2 A5:1 G5:1 F5:2 | G5:1 A5:1 C6:2 E6:2 C6:2 | C#6:2 A5:2 E5:2 C#5:2 | E5:1 F5:1 G5:1 A5:1 r:4",
            },
            // Intermediate: Synchro, Xyz and Link. Electronic, four on the floor, A minor.
            new Theme
            {
                Name = "intermediate", Bpm = 144, Key = 9, Minor = true, Groove = "four", Bass = "offbeat", Lead = Voice.SuperSaw,
                Chords = "Am | F | C | G | Am | F | G | E | F | G | Em | Am | F | G | E | E",
                Melody = "A5:3 B5:1 C6:2 E6:2 | D6:2 C6:2 A5:4 | G5:3 A5:1 C6:2 G5:2 | B5:2 A5:2 G5:2 D5:2 | "
                       + "A5:3 B5:1 C6:2 E6:2 | F6:2 E6:2 C6:4 | D6:3 C6:1 B5:2 G5:2 | G#5:4 B5:2 E6:2 | "
                       + "C6:1 A5:1 F5:1 A5:1 C6:2 F6:2 | D6:1 B5:1 G5:1 B5:1 D6:2 G6:2 | E6:3 D6:1 B5:2 G5:2 | A5:6 r:2 | "
                       + "A5:1 C6:1 F6:2 E6:1 D6:1 C6:2 | B5:1 D6:1 G6:2 F6:1 E6:1 D6:2 | E6:2 B5:2 G#5:2 B5:2 | E5:2 G#5:2 B5:2 D6:2",
            },
            // Advanced: champions of their eras. A regal march for brass in thirds, G minor.
            new Theme
            {
                Name = "advanced", Bpm = 120, Key = 7, Minor = true, Groove = "march", Bass = "quarters", Lead = Voice.Brass, Harmony = true,
                Chords = "Gm | Eb | Bb | F | Gm | Eb | Cm | D | Eb | F | Bb | Gm | Cm | D | Gm | D",
                Melody = "G4:3 D5:1 D5:4 | Eb5:2 D5:1 C5:1 Bb4:4 | F5:3 Bb5:1 Bb5:4 | C6:2 Bb5:1 A5:1 F5:4 | "
                       + "G5:3 A5:1 Bb5:2 G5:2 | G5:2 F5:2 Eb5:4 | Eb5:2 G5:2 C6:2 Bb5:2 | A5:4 F#5:4 | "
                       + "G5:2 Bb5:2 Eb6:3 D6:1 | C6:2 A5:2 F5:4 | D6:3 C6:1 Bb5:2 F5:2 | G5:6 r:2 | "
                       + "C6:2 Eb6:2 G6:3 F6:1 | F#6:2 D6:2 A5:4 | Bb5:2 A5:2 G5:2 D5:2 | F#5:4 A5:4",
            },
            // Expert: shadows, storms, rituals, fire. Fast and dark, E minor with a Phrygian F.
            new Theme
            {
                Name = "expert", Bpm = 152, Key = 4, Minor = true, Groove = "drive", Bass = "gallop", Lead = Voice.Square,
                Chords = "Em | F | Em | D | Em | F | G | F | Am | G | F | E | Am | G | F | B7",
                Melody = "E5:1 E5:1 G5:1 E5:1 B5:2 A5:1 G5:1 | F5:2 A5:2 C6:3 B5:1 | B5:1 G5:1 E5:1 G5:1 B5:2 E6:2 | D6:2 C6:1 B5:1 A5:2 F#5:2 | "
                       + "E5:1 E5:1 G5:1 E5:1 B5:2 A5:1 G5:1 | F5:2 A5:2 C6:2 E6:2 | D6:2 B5:2 G5:2 D6:2 | C6:4 A5:2 F5:2 | "
                       + "E6:3 D6:1 C6:2 A5:2 | B5:2 D6:2 G6:4 | F6:2 E6:1 D6:1 C6:2 A5:2 | G#5:2 B5:2 E6:4 | "
                       + "A5:1 C6:1 E6:1 A6:1 G6:2 E6:2 | D6:1 G6:1 B6:2 A6:1 G6:1 D6:2 | F6:2 E6:2 C6:2 A5:2 | B5:2 D#6:2 F#6:2 A6:2",
            },
            // Master: the Master League. Heavy, half-time and wide, C minor.
            new Theme
            {
                Name = "master", Bpm = 100, Key = 0, Minor = true, Groove = "half", Bass = "long", Lead = Voice.SuperSaw, Octave = true,
                Chords = "Cm | Ab | Eb | Bb | Cm | Ab | Fm | G | Ab | Bb | Gm | Cm | Ab | Bb | G | G",
                Melody = "C5:4 Eb5:2 G5:2 | Ab5:3 G5:1 F5:2 Eb5:2 | G5:4 Bb5:2 Eb6:2 | D6:3 C6:1 Bb5:4 | "
                       + "C6:4 Bb5:2 G5:2 | Ab5:2 C6:2 Eb6:4 | F6:2 Eb6:2 C6:2 Ab5:2 | B5:4 D6:4 | "
                       + "Eb6:3 F6:1 Eb6:2 C6:2 | D6:3 Eb6:1 D6:2 Bb5:2 | G5:2 Bb5:2 D6:2 G6:2 | Eb6:6 D6:1 C6:1 | "
                       + "C6:2 Eb6:2 Ab6:4 | G6:2 F6:2 D6:4 | B5:2 D6:2 G6:2 F6:2 | D6:4 B5:4",
            },
            // The Tier 0 Gauntlet: the finale. The fastest theme, doubled at the octave, F# minor.
            new Theme
            {
                Name = "gauntlet", Bpm = 160, Key = 6, Minor = true, Groove = "drive", Bass = "gallop", Lead = Voice.SuperSaw, Octave = true,
                Chords = "F#m | D | E | C# | F#m | D | Bm | C# | D | E | C#m | F#m | Bm | D | E | C#",
                Melody = "F#5:1 A5:1 C#6:2 F#6:2 E6:1 C#6:1 | D6:2 A5:2 F#5:2 A5:2 | E6:1 G#5:1 B5:2 E6:2 D6:1 B5:1 | C#6:3 B5:1 G#5:2 F5:2 | "
                       + "F#5:1 A5:1 C#6:2 F#6:2 G#6:1 A6:1 | F#6:2 D6:2 A5:4 | B5:1 D6:1 F#6:2 E6:1 D6:1 B5:2 | C#6:4 G#5:2 F5:2 | "
                       + "A5:2 D6:2 F#6:3 E6:1 | G#6:2 E6:2 B5:4 | G#5:2 C#6:2 E6:2 G#6:2 | A6:4 F#6:4 | "
                       + "B6:2 A6:1 F#6:1 D6:2 B5:2 | A5:1 D6:1 F#6:2 A6:2 F#6:2 | G#6:2 B6:2 E6:2 G#6:2 | F6:2 G#6:2 C#6:2 r:2",
            },
            // Tag Cup finals: a team anthem, two voices in thirds, Bb major.
            new Theme
            {
                Name = "tagcup", Bpm = 136, Key = 10, Groove = "pop", Bass = "eighths", Lead = Voice.Square, Harmony = true,
                Chords = "Bb | F | Gm | Eb | Bb | F | Eb | F | Gm | Eb | Bb | F | Gm | Eb | Cm | F",
                Melody = "D5:2 F5:2 Bb5:3 A5:1 | A5:2 F5:2 C5:4 | D5:2 G5:2 Bb5:3 A5:1 | G5:2 Eb5:2 Bb4:4 | "
                       + "D5:2 F5:2 Bb5:2 D6:2 | C6:3 Bb5:1 A5:2 F5:2 | G5:2 Bb5:2 Eb6:2 D6:1 C6:1 | C6:6 r:2 | "
                       + "Bb5:2 D6:2 G6:3 F6:1 | Eb6:2 D6:1 C6:1 Bb5:4 | D6:2 F6:2 Bb6:4 | A6:3 G6:1 F6:2 C6:2 | "
                       + "D6:2 G6:2 Bb6:2 A6:1 G6:1 | G6:2 Eb6:2 Bb5:4 | C6:2 Eb6:2 G6:2 F6:1 Eb6:1 | D6:2 C6:2 A5:2 F5:2",
            },
            // Ines, Gus's old rival: old-school and swung, a walking bass and vibes, G major.
            new Theme
            {
                Name = "ines", Bpm = 112, Key = 7, Groove = "shuffle", Bass = "walk", Lead = Voice.Triangle, Swing = 0.33,
                Chords = "G | Em | Am7 | D7 | G | Em | C | D7 | Em | Bm | C | G | Am7 | D7 | G | D7",
                Melody = "B4:1 D5:1 G5:2 F#5:1 G5:1 A5:2 | G5:2 E5:2 B4:4 | C5:1 E5:1 A5:2 G5:1 A5:1 B5:2 | A5:2 F#5:2 D5:2 C5:2 | "
                       + "B4:1 D5:1 G5:2 F#5:1 G5:1 B5:2 | D6:2 B5:2 G5:2 E5:2 | E5:1 G5:1 C6:2 B5:1 A5:1 G5:2 | F#5:2 A5:2 D6:2 C6:2 | "
                       + "B5:3 A5:1 G5:2 E5:2 | F#5:3 G5:1 F#5:2 D5:2 | E5:2 G5:2 C6:2 E6:2 | D6:4 B5:2 r:2 | "
                       + "C6:1 B5:1 A5:1 G5:1 E5:2 C5:2 | D5:1 F#5:1 A5:1 C6:1 B5:2 A5:2 | G5:4 B5:2 D6:2 | C6:2 A5:2 F#5:2 D5:2",
            },
            // Nocturne, the masked challenger: sparse bells over a slow pulse, B minor with a Neapolitan C.
            new Theme
            {
                Name = "nocturne", Bpm = 92, Key = 11, Minor = true, Groove = "noir", Bass = "long", Lead = Voice.Bell,
                Chords = "Bm | G | Em | F# | Bm | G | C | F# | Em | Bm | G | D | Em | C | F# | F#",
                Melody = "F#5:2 B5:2 D6:4 | C#6:2 B5:2 G5:4 | E5:2 G5:2 B5:3 A5:1 | A#5:4 F#5:4 | "
                       + "F#5:2 B5:2 D6:2 F#6:2 | E6:2 D6:2 B5:4 | C6:2 E6:2 G6:3 F#6:1 | F#6:4 C#6:4 | "
                       + "G6:3 F#6:1 E6:2 B5:2 | D6:3 C#6:1 B5:4 | B5:2 D6:2 G6:4 | F#6:3 E6:1 D6:2 A5:2 | "
                       + "E6:2 G6:2 B6:4 | C7:2 B6:2 G6:4 | A#6:2 F#6:2 C#6:4 | r:8",
            },
            // Rhea, the tag champion: a fast, bright anthem in thirds, E major.
            new Theme
            {
                Name = "rhea", Bpm = 150, Key = 4, Groove = "four", Bass = "offbeat", Lead = Voice.SuperSaw, Harmony = true,
                Chords = "E | B | C#m | A | E | B | A | B | C#m | A | E | B | C#m | A | F# | B",
                Melody = "E5:1 G#5:1 B5:2 E6:2 D#6:1 B5:1 | D#6:2 F#6:2 B5:4 | C#6:1 E6:1 G#6:2 F#6:1 E6:1 C#6:2 | E6:3 C#6:1 A5:4 | "
                       + "E5:1 G#5:1 B5:2 E6:2 F#6:1 G#6:1 | F#6:2 D#6:2 B5:4 | A5:1 C#6:1 E6:2 A6:2 G#6:1 F#6:1 | F#6:6 r:2 | "
                       + "G#6:3 F#6:1 E6:2 C#6:2 | E6:3 F#6:1 E6:2 A5:2 | B5:2 E6:2 G#6:2 B6:2 | A6:3 G#6:1 F#6:4 | "
                       + "E6:2 G#6:2 C#7:4 | B6:2 A6:2 E6:4 | F#6:2 A#6:2 C#7:2 A#6:2 | B6:4 D#6:2 F#6:2",
            },
            // Seraph, the traveling pro: slow and grand, a choir doubled at the octave, Bb minor.
            new Theme
            {
                Name = "seraph", Bpm = 84, Key = 10, Minor = true, Groove = "epic", Bass = "long", Lead = Voice.Choir, Octave = true,
                Chords = "Bbm | Gb | Db | Ab | Bbm | Gb | Ebm | F | Gb | Ab | Db | Bbm | Gb | Ab | F | F",
                Melody = "F5:4 Db6:2 C6:2 | Bb5:6 Ab5:2 | F5:4 Ab5:2 Db6:2 | C6:6 Eb6:2 | "
                       + "F6:4 Eb6:2 Db6:2 | Db6:4 Bb5:4 | Gb5:2 Bb5:2 Eb6:2 Db6:2 | C6:4 A5:4 | "
                       + "Bb5:2 Db6:2 Gb6:4 | F6:2 Eb6:2 C6:4 | Db6:3 Eb6:1 F6:4 | F6:6 Eb6:1 Db6:1 | "
                       + "Db6:4 Gb6:4 | F6:3 Eb6:1 C6:4 | A5:2 C6:2 F6:2 Eb6:2 | C6:8",
            },
            // The Back Alley's everyday music (ALLEY-SPEC section 13): late-night noir under the string lights. A walking
            // bass, a lazy swung backbeat with a rim click, no tune (the chords climb in plucks, as "duel"), D minor.
            new Theme
            {
                Name = "alley", Bpm = 94, Key = 2, Minor = true, Groove = "alley", Bass = "walk", Swing = 0.25,
                Chords = "Dm7 | Dm7 | Gm7 | Gm7 | Bbmaj7 | A7 | Dm7 | A7 | Dm7 | Dm7 | Gm7 | C7 | Fmaj7 | Bbmaj7 | Em7 | A7",
            },
            // Patron Orvel, the House: a measured tango for brass in thirds, polite and unhurried, F minor.
            new Theme
            {
                Name = "orvel", Bpm = 108, Key = 5, Minor = true, Groove = "tango", Bass = "quarters", Lead = Voice.Brass, Harmony = true,
                Chords = "Fm | Fm | Db | C7 | Fm | Bbm | C7 | Fm | Ab | Eb | Db | C7 | Bbm | Db | C7 | C7",
                Melody = "C5:3 Db5:1 C5:2 F5:2 | Ab5:3 G5:1 F5:4 | F5:3 Ab5:1 Db6:2 C6:2 | Bb5:2 G5:2 E5:4 | "
                       + "C5:3 Db5:1 C5:2 F5:2 | Db6:3 C6:1 Bb5:2 F5:2 | E5:2 G5:2 Bb5:2 C6:2 | Ab5:6 r:2 | "
                       + "C6:3 Eb6:1 Ab6:2 G6:2 | G6:2 F6:2 Eb6:4 | F6:3 Eb6:1 Db6:2 Ab5:2 | C6:2 Bb5:2 G5:2 E5:2 | "
                       + "F5:3 Bb5:1 Db6:2 F6:2 | Eb6:2 Db6:2 Ab5:4 | G5:2 Bb5:2 E6:2 G6:2 | F6:4 E6:2 r:2",
            },
            // Nyx, keeper of the old words: a slow choir over a lantern-light pulse, A minor with a Phrygian Bb.
            new Theme
            {
                Name = "nyx", Bpm = 78, Key = 9, Minor = true, Groove = "noir", Bass = "long", Lead = Voice.Choir, Octave = true,
                Chords = "Am | Bb | Am | E | Am | F | Dm | E | F | G | Am | Bb | Dm | Bb | E | E",
                Melody = "E5:4 A5:2 C6:2 | D6:3 C6:1 Bb5:4 | A5:2 C6:2 E6:4 | D6:2 B5:2 G#5:4 | "
                       + "E5:4 A5:2 B5:2 | C6:3 A5:1 F5:4 | D5:2 F5:2 A5:3 G5:1 | G#5:6 r:2 | "
                       + "A5:2 C6:2 F6:4 | E6:2 D6:2 B5:4 | C6:3 B5:1 A5:2 E5:2 | F5:2 Bb5:2 D6:4 | "
                       + "F6:3 E6:1 D6:2 A5:2 | Bb5:4 D6:2 F6:2 | E6:2 D6:2 B5:2 G#5:2 | E5:6 r:2",
            },
            // No Limits (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 11): the end chair under one warm bulb in a cold strip-lit
            // room. A slow noir pulse, a long bass, a plucked tune doubled an octave down, C# minor; played at seat 4 (BossMusic.ForAlley's host-seat rule), seats 1-3 play "alley".
            new Theme
            {
                Name = "no-limits", Bpm = 96, Key = 1, Minor = true, Groove = "noir", Bass = "long", Lead = Voice.Pluck, Octave = true, Swing = 0.2,
                Chords = "C#m | C#m | A | B | C#m | C#m | F#m | G#7 | A | B | C#m | A | F#m | B | G#7 | G#7",
                Melody = "C#5:4 E5:2 G#5:2 | F#5:3 E5:1 C#5:4 | A5:2 G#5:2 E5:4 | F#5:2 D#5:2 B4:4 | "
                       + "C#5:4 E5:2 G#5:2 | B5:3 A5:1 G#5:4 | F#5:2 A5:2 C#6:4 | C6:2 G#5:2 D#5:4 | "
                       + "A5:4 C#6:2 E6:2 | D#6:3 C#6:1 B5:4 | C#6:2 G#5:2 E5:4 | A5:2 E5:2 C#5:4 | "
                       + "F#5:3 A5:1 C#6:4 | B5:2 D#6:2 F#6:4 | G#5:2 C6:2 D#6:2 G#6:2 | G#5:6 r:2",
            },
        }.ToDictionary(t => t.Name);

        // ---- Voices ----------------------------------------------------------------------------------

        /// <summary>Instruments for the themes: oscillators through a filter that opens with the envelope.</summary>
        public enum Voice { Square, Saw, SuperSaw, Triangle, Brass, Choir, Bell, Pluck, Pad, Bass }

        private sealed class VoiceSpec
        {
            public (Wave wave, double ratio, double gain)[] Oscs = new (Wave, double, double)[0];
            public double Attack, Decay, Sustain, Release, Cutoff, EnvAmount, Vibrato;
        }

        private static readonly Dictionary<Voice, VoiceSpec> Voices = new Dictionary<Voice, VoiceSpec>
        {
            [Voice.Square] = new VoiceSpec { Oscs = new[] { (Wave.Square, 1.0, 1.0) }, Attack = 0.005, Decay = 0.15, Sustain = 0.7, Release = 0.08, Cutoff = 2500, EnvAmount = 0.8, Vibrato = 0.004 },
            [Voice.Saw] = new VoiceSpec { Oscs = new[] { (Wave.Saw, 1.0, 1.0) }, Attack = 0.008, Decay = 0.2, Sustain = 0.7, Release = 0.1, Cutoff = 1800, EnvAmount = 1.2, Vibrato = 0.004 },
            [Voice.SuperSaw] = new VoiceSpec { Oscs = new[] { (Wave.Saw, 0.994, 0.5), (Wave.Saw, 1.0, 0.6), (Wave.Saw, 1.006, 0.5) }, Attack = 0.01, Decay = 0.2, Sustain = 0.75, Release = 0.15, Cutoff = 2400, EnvAmount = 0.8, Vibrato = 0.003 },
            [Voice.Triangle] = new VoiceSpec { Oscs = new[] { (Wave.Triangle, 1.0, 1.0), (Wave.Sine, 2.0, 0.25) }, Attack = 0.005, Decay = 0.3, Sustain = 0.5, Release = 0.15, Cutoff = 6000, Vibrato = 0.006 },
            [Voice.Brass] = new VoiceSpec { Oscs = new[] { (Wave.Saw, 1.0, 0.7), (Wave.Saw, 1.004, 0.6) }, Attack = 0.04, Decay = 0.25, Sustain = 0.8, Release = 0.12, Cutoff = 700, EnvAmount = 2.5, Vibrato = 0.004 },
            [Voice.Choir] = new VoiceSpec { Oscs = new[] { (Wave.Triangle, 0.996, 0.5), (Wave.Triangle, 1.0, 0.6), (Wave.Triangle, 1.004, 0.5), (Wave.Saw, 1.002, 0.15) }, Attack = 0.25, Decay = 0.3, Sustain = 0.9, Release = 0.5, Cutoff = 2000, EnvAmount = 0.3, Vibrato = 0.007 },
            [Voice.Pluck] = new VoiceSpec { Oscs = new[] { (Wave.Square, 1.0, 0.5), (Wave.Triangle, 1.0, 0.8) }, Attack = 0.002, Decay = 0.22, Sustain = 0, Release = 0.05, Cutoff = 3500, EnvAmount = 1.0 },
            [Voice.Pad] = new VoiceSpec { Oscs = new[] { (Wave.Saw, 0.995, 0.5), (Wave.Saw, 1.005, 0.5) }, Attack = 0.3, Decay = 0.5, Sustain = 0.85, Release = 0.8, Cutoff = 1000, EnvAmount = 0.3 },
            [Voice.Bass] = new VoiceSpec { Oscs = new[] { (Wave.Triangle, 1.0, 1.0), (Wave.Square, 1.0, 0.3) }, Attack = 0.005, Decay = 0.2, Sustain = 0.8, Release = 0.05, Cutoff = 600, EnvAmount = 0.8 },
        };

        private static double Hz(double midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);

        /// <summary>One note held for <paramref name="seconds"/>, then its release (the array runs on past it).</summary>
        public static float[] Note(Voice voice, double midi, double seconds, double volume)
        {
            double f = Hz(midi);
            if (voice == Voice.Bell)
            {
                // Struck metal: inharmonic partials, the high ones dying first.
                var partials = new[] { (1.0, 1.0, 1.6), (2.76, 0.5, 0.9), (5.4, 0.25, 0.5), (8.93, 0.12, 0.3) };
                return Mix(partials.Select(p => Tone(f * p.Item1, f * p.Item1, Math.Max(seconds, 0.3) + 1.2, Wave.Sine, 0.002, p.Item3, volume * p.Item2)).ToArray());
            }
            var v = Voices[voice];
            int n = (int)((seconds + v.Release) * Rate);
            var s = new float[n];
            var phases = new double[v.Oscs.Length];
            double y1 = 0, y2 = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                double env = t < v.Attack ? t / v.Attack : t < v.Attack + v.Decay ? 1 - (1 - v.Sustain) * (t - v.Attack) / v.Decay : v.Sustain;
                if (t > seconds) env *= Math.Max(0, 1 - (t - seconds) / v.Release);
                double vibrato = v.Vibrato > 0 && t > 0.2 ? 1 + v.Vibrato * Math.Sin(2 * Math.PI * 5.5 * t) * Math.Min(1, (t - 0.2) / 0.3) : 1;
                double x = 0;
                for (int k = 0; k < phases.Length; k++)
                {
                    phases[k] += f * v.Oscs[k].ratio * vibrato / Rate;
                    x += Osc(v.Oscs[k].wave, phases[k]) * v.Oscs[k].gain;
                }
                // Two one-pole low-passes in a row, the cutoff opening with the envelope (brighter at the attack).
                double a = 1 - Math.Exp(-2 * Math.PI * Math.Min(16000, v.Cutoff * (1 + v.EnvAmount * env)) / Rate);
                y1 += a * (x - y1);
                y2 += a * (y1 - y2);
                s[i] = (float)(y2 * env * volume);
            }
            return s;
        }

        private static float[] HighPass(float[] s, double cutoff)
        {
            var low = LowPass(s, cutoff);
            for (int i = 0; i < s.Length; i++) low[i] = s[i] - low[i];
            return low;
        }

        // ---- Grooves ---------------------------------------------------------------------------------

        /// <summary>Drum patterns, one bar in sixteenths: kick, snare ("g" a ghost note, "r" a rim click), closed hat, open hat.</summary>
        private static readonly Dictionary<string, string[]> Grooves = new Dictionary<string, string[]>
        {
            ["rock"] = new[] { "x...x...x...x...", "....x.......x...", "..x...x...x...x.", "................" },
            ["pop"] = new[] { "x.......x.x.....", "....x.......x...", "x.x.x.x.x.x.x.x.", "................" },
            ["sneak"] = new[] { "x.....x...x.....", "....x..g....x..g", "x.xx..x.x.xx..x.", "................" },
            ["four"] = new[] { "x...x...x...x...", "....x.......x...", "x.x.x.x.x.x.x.x.", "..x...x...x...x." },
            ["march"] = new[] { "x.......x.......", "....x..gx...x.gg", "x...x...x...x...", "................" },
            ["drive"] = new[] { "x..x..x.x..x..x.", "....x.......x...", "xxxxxxxxxxxxxxxx", "................" },
            ["half"] = new[] { "x.........x.....", "........x.......", "x.x.x.x.x.x.x.x.", "..............x." },
            ["shuffle"] = new[] { "x.......x.......", "....x.......x...", "x.x.x.x.x.x.x.x.", "................" },
            ["noir"] = new[] { "x.........x.....", "........r.......", "x...x...x...x...", "................" },
            ["epic"] = new[] { "x.......x.x.....", "........x.......", "x...x...x...x...", "................" },
            ["alley"] = new[] { "x.....x...x.....", "....r.......x..g", "x.x.x.x.x.x.x.x.", "..............x." },
            ["tango"] = new[] { "x..x..x.x.......", "........x..g....", "x...x...x...x...", "................" },
        };

        /// <summary>The snare in the last bar of every eight: a fill into the next phrase.</summary>
        private const string Fill = "....x...x.xgxxxx";

        // ---- Parsing ---------------------------------------------------------------------------------

        private static int Mod(int a, int m) => ((a % m) + m) % m;

        private static int PitchClass(string text, ref int i)
        {
            int pc = "C.D.EF.G.A.B".IndexOf(char.ToUpperInvariant(text[i++]));
            if (pc < 0) throw new FormatException($"Not a note: \"{text}\"");
            for (; i < text.Length && (text[i] == '#' || text[i] == 'b'); i++) pc += text[i] == '#' ? 1 : -1;
            return pc;
        }

        private static (int root, int[] intervals) ParseChord(string text, string theme)
        {
            int i = 0;
            int root = PitchClass(text, ref i);
            var intervals = text.Substring(i) switch
            {
                "" => new[] { 0, 4, 7 },
                "m" => new[] { 0, 3, 7 },
                "7" => new[] { 0, 4, 7, 10 },
                "m7" => new[] { 0, 3, 7, 10 },
                "maj7" => new[] { 0, 4, 7, 11 },
                "dim" => new[] { 0, 3, 6 },
                "sus" => new[] { 0, 5, 7 },
                _ => throw new FormatException($"Theme {theme}: unknown chord \"{text}\""),
            };
            return (Mod(root, 12), intervals);
        }

        /// <summary>The melody as (start, length) in eighths and a MIDI note (-1 for a rest), and its length in bars.</summary>
        private static (List<(double start, double length, int midi)> notes, int bars) ParseMelody(string melody, string theme)
        {
            var notes = new List<(double, double, int)>();
            if (melody.Trim().Length == 0) return (notes, 0);
            var bars = melody.Split('|');
            for (int b = 0; b < bars.Length; b++)
            {
                double at = b * 8;
                foreach (var token in bars[b].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = token.Split(':');
                    double length = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                    int midi = -1;
                    if (parts[0] != "r")
                    {
                        int i = 0;
                        int pc = PitchClass(parts[0], ref i);
                        midi = 12 * (int.Parse(parts[0].Substring(i)) + 1) + pc;
                    }
                    notes.Add((at, length, midi));
                    at += length;
                }
                if (Math.Abs(at - (b + 1) * 8) > 1e-6)
                    throw new FormatException($"Theme {theme}: melody bar {b + 1} is {at - b * 8} eighths long, not 8");
            }
            return (notes, bars.Length);
        }

        // ---- Rendering -------------------------------------------------------------------------------

        /// <summary>How loud the everyday mix (bed, drums, lead) is, as RMS: the old duel loop's loudness.</summary>
        private const double ThemeRms = 0.085;

        /// <summary>
        /// Renders a theme's layers and one-shots, <paramref name="transpose"/> semitones up (bosses sharing a band's
        /// theme each get their own key). Pure computation, safe to run off the main thread.
        /// </summary>
        public static ThemeMusic Render(Theme theme, int transpose = 0)
        {
            double beat = 60 / theme.Bpm, bar = beat * 4, step = beat / 4;
            var chords = theme.Chords.Split('|').Select(c => ParseChord(c.Trim(), theme.Name)).ToList();
            var (melody, melodyBars) = ParseMelody(theme.Melody, theme.Name);
            int bars = chords.Count;
            if (melodyBars > 0 && bars % melodyBars != 0) throw new FormatException($"Theme {theme.Name}: {melodyBars} melody bars don't fit {bars} chords");
            int key = Mod(theme.Key + transpose, 12);
            int length = (int)(bar * bars * Rate);
            var layers = LayerNames.ToDictionary(n => n, n => new float[length]);

            // Everything wraps around the end, so each layer loops without a seam.
            void Add(string layer, float[] part, double at, double gain = 1)
            {
                var s = layers[layer];
                int start = (int)(at * Rate);
                for (int i = 0; i < part.Length; i++) s[Mod(start + i, length)] += (float)(part[i] * gain);
            }
            // A sixteenth's time in a bar; with swing the off-beat eighths land late.
            double At(int b, double sixteenth) => b * bar + sixteenth * step + (Math.Abs(sixteenth % 4 - 2) < 1e-6 ? theme.Swing * step : 0);

            int Root(int b) => Mod(chords[b].root + transpose, 12);
            // Close voicings between G3 and F#4, so chord changes move smoothly.
            int[] Voiced(int b) => chords[b].intervals.Select(i => 55 + Mod(Root(b) + i - 7, 12)).OrderBy(m => m).ToArray();
            int BassNote(int b) => 36 + Root(b);

            var kick = Thud(0.3, 52, 1.0);
            var snare = Mix(Noise(0.18, 6000, 1800, 0.001, 0.16, 0.5), Tone(210, 170, 0.1, Wave.Triangle, 0.001, 0.09, 0.4));
            var rim = Mix(Tone(1700, 1700, 0.03, Wave.Square, 0.001, 0.025, 0.2, 4000), Noise(0.04, 4000, 3000, 0.001, 0.03, 0.2));
            var hat = HighPass(Noise(0.04, 12000, 9000, 0.001, 0.035, 0.5), 6000);
            var open = HighPass(Noise(0.25, 12000, 9000, 0.002, 0.22, 0.5), 6000);
            var crash = Crash();
            var groove = Grooves[theme.Groove];
            var scale = theme.Minor ? new[] { 0, 2, 3, 5, 7, 8, 10 } : new[] { 0, 2, 4, 5, 7, 9, 11 };

            for (int b = 0; b < bars; b++)
            {
                double at = b * bar;
                // Bed: the pad and the bass.
                foreach (var m in Voiced(b)) Add("bed", Note(Voice.Pad, m, bar, 0.04), at);
                int bass = BassNote(b), next = BassNote((b + 1) % bars);
                var line = theme.Bass switch
                {
                    "eighths" => Enumerable.Range(0, 8).Select(e => (e * 2.0, 1.8, bass + (e % 4 == 2 ? 12 : 0))).ToArray(),
                    "offbeat" => Enumerable.Range(0, 4).Select(q => (q * 4 + 2.0, 1.6, bass)).ToArray(),
                    "gallop" => Enumerable.Range(0, 4).SelectMany(q => new[] { (q * 4.0, 0.9, bass), (q * 4 + 2.0, 0.9, bass), (q * 4 + 3.0, 0.9, bass) }).ToArray(),
                    // A walking line: root, third, fifth, then a half step into the next chord's root.
                    "walk" => new[] { (0.0, 3.6, bass), (4.0, 3.6, bass + chords[b].intervals[1]), (8.0, 3.6, bass + 7), (12.0, 3.6, next + (next > bass + 7 ? -1 : 1)) },
                    "long" => new[] { (0.0, 9.5, bass), (10.0, 5.5, bass) },
                    _ => new[] { (0.0, 3.5, bass), (4.0, 3.5, bass), (8.0, 3.5, bass), (12.0, 3.5, bass + 7) },
                };
                foreach (var (pos, len, m) in line) Add("bed", Note(Voice.Bass, m, len * step, 0.3), At(b, pos));

                // Drums, with a fill into every eighth bar and a crash at the top of each phrase.
                bool fill = b % 8 == 7;
                for (int x = 0; x < 16; x++)
                {
                    double t = At(b, x);
                    if (groove[0][x] == 'x') Add("drums", kick, t, 0.55);
                    char sn = fill ? Fill[x] : groove[1][x];
                    if (sn == 'x') Add("drums", snare, t, fill ? 0.2 + 0.2 * x / 15 : 0.35);
                    else if (sn == 'g') Add("drums", snare, t, 0.12);
                    else if (sn == 'r') Add("drums", rim, t, 0.3);
                    if (groove[2][x] == 'x') Add("drums", hat, t, x % 4 == 0 ? 0.16 : 0.1);
                    if (groove[3][x] == 'x') Add("drums", open, t, 0.12);
                }
                if (b % 8 == 0) Add("drums", crash, at, 0.25);

                // Drive: sixteenth hats, a pumping octave bass, a snare roll into every fourth bar.
                for (int x = 0; x < 16; x++)
                {
                    Add("drive", hat, At(b, x), x % 4 == 0 ? 0.1 : 0.06);
                    if (x % 2 == 0) Add("drive", Note(Voice.Bass, bass + (x % 4 == 2 ? 12 : 0), step * 1.6, 0.18), At(b, x));
                    if (b % 4 == 3 && x >= 8) Add("drive", snare, At(b, x), 0.08 + 0.3 * (x - 8) / 7);
                }
                if (b % 4 == 0) Add("drive", crash, at, 0.15);

                // Dread: a low drone, a clashing swell every two bars, and a clock ticking on the beat.
                Add("dread", Note(Voice.Pad, 28 + Mod(key - 4, 12), bar, 0.09), at);
                if (b % 2 == 0)
                {
                    Add("dread", Note(Voice.Choir, 60 + key + 1, bar * 1.6, 0.06), at);
                    Add("dread", Note(Voice.Choir, 60 + key + 6, bar * 1.6, 0.05), at);
                }
                for (int q = 0; q < 4; q++)
                    Add("dread", Tone(q % 2 == 0 ? 2400 : 1800, q % 2 == 0 ? 2400 : 1800, 0.02, Wave.Square, 0.001, 0.018, 0.14, 5000), at + q * beat);

                // Hope: the chord climbing in sixteenths, louder up the bar.
                var tones = Voiced(b);
                for (int x = 0; x < 16; x++)
                {
                    int climb = x % (tones.Length * 2);
                    Add("hope", Note(Voice.Pluck, tones[climb % tones.Length] + 12 + 12 * (climb / tones.Length), step * 0.9, 0.06 + 0.08 * x / 15.0), At(b, x));
                }

                // Lethal: a heartbeat on beats 1 and 3, a low drone, and a trembling fifth up high.
                double dub = Math.Min(0.18, beat * 0.35);
                for (int q = 0; q < 4; q += 2)
                {
                    Add("lethal", Thud(0.3, 48, 0.8), at + q * beat);
                    Add("lethal", Thud(0.25, 42, 0.5), at + q * beat + dub);
                }
                Add("lethal", Note(Voice.Pad, 36 + key, bar, 0.07), at);
                var tremolo = Note(Voice.Saw, 72 + Mod(key + 7, 12), bar, 0.03);
                for (int i = 0; i < tremolo.Length; i++) tremolo[i] *= (float)(0.6 + 0.4 * Math.Sin(2 * Math.PI * i / (step * Rate)));
                Add("lethal", tremolo, at);
            }

            // Lead: the tune (repeated to fill the loop), or an arpeggio of the chords when there isn't one.
            var lead = melody.Where(n => n.midi >= 0).Select(n => (n.start, n.length, midi: n.midi + theme.LeadShift + transpose)).ToList();
            if (melodyBars > 0)
            {
                for (int r = 0; r < bars / melodyBars; r++)
                    foreach (var (start, len, midi) in lead)
                    {
                        double pos = r * melodyBars * 16 + start * 2;
                        int b = (int)(pos / 16);
                        double t = At(b, pos - b * 16), seconds = len * 2 * step * 0.92;
                        Add("lead", Note(theme.Lead, midi, seconds, theme.Lead == Voice.Bell ? 0.08 : 0.2), t);
                        if (theme.Harmony) Add("lead", Note(theme.Lead, ThirdBelow(midi, key, scale), seconds, 0.12), t);
                        if (theme.Octave) Add("lead", Note(theme.Lead, midi - 12, seconds, 0.1), t);
                    }
            }
            else
            {
                for (int b = 0; b < bars; b++)
                {
                    var v = Voiced(b);
                    for (int e = 0; e < 8; e++)
                        Add("lead", Note(Voice.Pluck, v[e % 3] + 12 + 12 * ((b * 8 + e) % 6 < 3 ? 1 : 0), step * 1.8, 0.09), At(b, e * 2));
                }
            }

            // One loudness for all the layers: the everyday mix at the old loop's loudness, but never clipping when
            // everything that can play together does.
            var everyday = Sum(layers["bed"], layers["drums"], layers["lead"]);
            double gain = ThemeRms / Math.Max(1e-6, Rms(everyday));
            double loudest = new[]
            {
                Sum(everyday, layers["drive"], layers["dread"]), Sum(everyday, layers["drive"], layers["hope"]), layers["lethal"],
            }.Max(s => s.Max(x => Math.Abs(x)));
            gain = Math.Min(gain, 0.95 / Math.Max(1e-6, loudest));
            foreach (var s in layers.Values)
                for (int i = 0; i < s.Length; i++) s[i] = (float)(s[i] * gain);

            var first = FirstBar(theme, transpose);
            return new ThemeMusic
            {
                Theme = theme, Bars = bars, BarSeconds = bar, Layers = layers,
                Sting = Sting(theme, key, first, crash),
                Victory = Victory(theme, key, first, bar, crash),
                Defeat = Defeat(theme, key, first, bar),
            };
        }

        /// <summary>Just the sting, for the versus screen to play at once (a whole theme takes about a second).</summary>
        public static float[] RenderSting(Theme theme, int transpose = 0) =>
            Sting(theme, Mod(theme.Key + transpose, 12), FirstBar(theme, transpose), Crash());

        private static float[] Crash() => HighPass(Noise(1.6, 12000, 6000, 0.002, 1.5, 0.6), 3000);

        /// <summary>The tune's first bar (without a tune, the first chord climbing): what the sting and the endings quote.</summary>
        private static List<(double start, double length, int midi)> FirstBar(Theme theme, int transpose)
        {
            var (melody, _) = ParseMelody(theme.Melody, theme.Name);
            if (melody.Count > 0)
                return melody.Where(n => n.midi >= 0 && n.start < 8).Select(n => (n.start, n.length, n.midi + theme.LeadShift + transpose)).ToList();
            var (root, intervals) = ParseChord(theme.Chords.Split('|')[0].Trim(), theme.Name);
            return intervals.Select(i => 55 + Mod(root + transpose + i - 7, 12)).OrderBy(m => m).Select((m, i) => (i * 1.0, 1.0, m + 12)).ToList();
        }

        /// <summary>A third below <paramref name="midi"/> in the key; a minor third for notes outside it.</summary>
        private static int ThirdBelow(int midi, int key, int[] scale)
        {
            int degree = Array.IndexOf(scale, Mod(midi - key, 12));
            return degree < 0 ? midi - 3 : midi - Mod(scale[degree] - scale[Mod(degree - 2, 7)], 12);
        }

        private static int[] Tonic(int key, bool minor) => new[] { 48 + key, 48 + key + (minor ? 3 : 4), 48 + key + 7, 60 + key };

        /// <summary>The tune's first notes, fast, then a hit on the tonic: the boss steps up.</summary>
        private static float[] Sting(Theme theme, int key, List<(double start, double length, int midi)> first, float[] crash)
        {
            const double Step = 0.09;
            var parts = first.Take(4).Select((n, i) => Delay(Note(theme.Lead, n.midi, Step * 0.9, 0.3), i * Step)).ToList();
            double hit = Math.Min(4, first.Count) * Step + 0.05;
            parts.AddRange(Tonic(key, theme.Minor).Select(m => Delay(Note(Voice.Brass, m, 1.2, 0.15), hit)));
            parts.Add(Delay(Thud(1.4, 40, 1.0), hit));
            parts.Add(Delay(crash, hit));
            return Normalize(Echo(Mix(parts.ToArray()), 0.18, 0.3), 0.85);
        }

        /// <summary>The tune's first bar, then the tonic chord in major, climbing: a win.</summary>
        private static float[] Victory(Theme theme, int key, List<(double start, double length, int midi)> first, double bar, float[] crash)
        {
            double eighth = bar / 8;
            var parts = first.Select(n => Delay(Note(theme.Lead, n.midi, n.length * eighth * 0.92, 0.25), n.start * eighth)).ToList();
            var major = Tonic(key, false);
            parts.AddRange(major.Select(m => Delay(Note(Voice.SuperSaw, m, 1.8, 0.1), bar)));
            parts.AddRange(Enumerable.Range(0, 8).Select(i => Delay(Note(Voice.Pluck, major[i % 3] + 12 + 12 * (i / 3), 0.12, 0.12), bar + i * 0.07)));
            parts.Add(Delay(Thud(1.0, 45, 0.8), bar));
            parts.Add(Delay(crash, bar));
            return Normalize(Echo(Mix(parts.ToArray()), 0.15, 0.3), 0.8);
        }

        /// <summary>The tune's first bar slowed and an octave down, sinking to the tonic chord in minor: a loss.</summary>
        private static float[] Defeat(Theme theme, int key, List<(double start, double length, int midi)> first, double bar)
        {
            double eighth = bar / 8 * Math.Min(2, 3.0 / bar);
            var parts = first.Select(n => Delay(Note(theme.Lead, n.midi - 12, n.length * eighth * 0.92, 0.2), n.start * eighth)).ToList();
            double end = 8 * eighth;
            parts.AddRange(Tonic(key, true).Select(m => Delay(Note(Voice.Pad, m, 2.5, 0.08), end)));
            parts.Add(Delay(Note(Voice.Bass, 36 + key, 2.5, 0.3), end));
            return Normalize(Echo(LowPass(Mix(parts.ToArray()), 1800), 0.2, 0.3), 0.65);
        }

        // ---- The Back Alley's rain bed ----------------------------------------------------------------

        /// <summary>The rain bed's loop length in seconds (both drones finish whole cycles in it, so it loops without a seam).</summary>
        public const double RainBedSeconds = 12;

        /// <summary>
        /// The Back Alley's rain bed (ALLEY-SPEC section 13), played under the music while the Back Alley tab or the van is
        /// shown, at MusicVolume × 0.35, fading with the 300 ms crossfade: rain (noise through a band, with drops on top), a
        /// 47 and 52 Hz sub-bass drone that swells twice a loop, and vinyl crackle. Every part wraps around its end, and the
        /// filters run twice so their state carries over the seam: it loops without a click. Mono, peak 0.6. Pure
        /// computation (a second or two), safe off the main thread.
        /// </summary>
        public static float[] RainBed()
        {
            int n = Samples(RainBedSeconds);
            var rng = new Random(0x2A1E);
            var white = new float[n];
            for (int i = 0; i < n; i++) white[i] = (float)(rng.NextDouble() * 2 - 1);
            // One-pole low-passes run around the loop twice, so the end's state is the start's.
            float[] Loop(float[] input, double cutoff)
            {
                double a = 1 - Math.Exp(-2 * Math.PI * cutoff / Rate), y = 0;
                var o = new float[input.Length];
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i < input.Length; i++) o[i] = (float)(y += a * (input[i] - y));
                return o;
            }
            var bed = new float[n];
            void Add(float[] part, int at)
            {
                for (int i = 0; i < part.Length; i++) bed[(at + i) % n] += part[i];
            }
            // The rain: a band of noise (a 5 kHz low-pass minus a 700 Hz one), breathing slowly.
            var high = Loop(white, 5000);
            var low = Loop(white, 700);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate, breathe = 0.8 + 0.2 * Math.Sin(2 * Math.PI * t / (RainBedSeconds / 3));
                bed[i] += (float)((high[i] - low[i]) * 0.5 * breathe);
            }
            // Drops: short bright ticks on the rain.
            for (int d = 0; d < (int)(RainBedSeconds * 26); d++)
                Add(Tone(2600 + rng.NextDouble() * 3400, 1800 + rng.NextDouble() * 1200, 0.012, Wave.Sine, 0.001, 0.011, 0.05 + rng.NextDouble() * 0.08), rng.Next(n));
            // The sub-bass drone: 47 and 52 Hz (whole cycles in the loop), swelling twice a loop.
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate, swell = 0.7 + 0.3 * Math.Sin(2 * Math.PI * t * 2 / RainBedSeconds);
                bed[i] += (float)((Math.Sin(2 * Math.PI * 47 * t) * 0.22 + Math.Sin(2 * Math.PI * 52 * t) * 0.12) * swell);
            }
            // Vinyl crackle: sparse clicks, a few loud pops, softened.
            var crackle = new float[n];
            for (int c = 0; c < (int)(RainBedSeconds * 9); c++)
            {
                int at = rng.Next(n);
                double amp = (rng.NextDouble() < 0.12 ? 0.5 : 0.18) * (rng.NextDouble() < 0.5 ? -1 : 1);
                for (int k = 0; k < 90; k++) crackle[(at + k) % n] += (float)(amp * Math.Exp(-k / 14.0) * (rng.NextDouble() * 2 - 1));
            }
            var soft = Loop(crackle, 6000);
            for (int i = 0; i < n; i++) bed[i] += soft[i];
            double peak = bed.Max(x => Math.Abs(x));
            if (peak > 0) for (int i = 0; i < n; i++) bed[i] = (float)(bed[i] * 0.6 / peak);
            return bed;
        }

        private static float[] Sum(params float[][] parts)
        {
            var s = new float[parts[0].Length];
            foreach (var p in parts)
                for (int i = 0; i < s.Length; i++) s[i] += p[i];
            return s;
        }

        private static double Rms(float[] s) => Math.Sqrt(s.Average(x => (double)x * x));
    }
}
