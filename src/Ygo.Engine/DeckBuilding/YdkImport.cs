using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Ygo.Engine.DeckBuilding
{
    /// <summary>
    /// A .ydk file read for import (docs/dev/YDK-IMPORT-2026-11-07.md): its Main, Extra and Side Decks and nothing else. The
    /// file's comments ("#created by ...", which may hold a person's name) and any of the game's own note lines
    /// ("#campaign", "#passes", "#title"...) are dropped, so an outside file can't set campaign state. Card codes the game's
    /// card database doesn't have are left out and listed. Copies, sizes and ban lists aren't checked here: the deck is
    /// imported as it is and the editor's legality line says what's wrong.
    /// </summary>
    public sealed class YdkImport
    {
        /// <summary>Files bigger than this aren't deck lists (a .ydk of 90 cards is about 1 KB).</summary>
        public const int MaxBytes = 256 * 1024;

        /// <summary>The deck read (Main, Extra, Side only), or null when nothing could be imported (see <see cref="Error"/>).</summary>
        public Deck? Deck;
        /// <summary>Why nothing was imported ("" when <see cref="Deck"/> is set).</summary>
        public string Error = "";
        /// <summary>Card codes not in the card database, each once, in file order, with the copies left out.</summary>
        public readonly List<(uint code, int copies)> Unknown = new List<(uint, int)>();
        /// <summary>Lines in a deck section that weren't a card code (skipped).</summary>
        public int BadLines;

        /// <summary>Copies left out because their code isn't in the card database.</summary>
        public int Skipped => Unknown.Sum(u => u.copies);

        public bool Ok => Deck != null;

        /// <summary>
        /// One line for the status bar: "Imported 41 / 15 / 15 cards from X.ydk." plus what was skipped, or the error.
        /// <paramref name="source"/> is where it came from ("ydk-decklist.ydk").
        /// </summary>
        public string Summary(string source)
        {
            if (Deck == null) return Error;
            var line = $"Imported {Deck.Main.Count} / {Deck.Extra.Count} / {Deck.Side.Count} cards from {source}.";
            if (Unknown.Count > 0)
            {
                var codes = string.Join(", ", Unknown.Take(4).Select(u => u.copies > 1 ? $"{u.code} x{u.copies}" : u.code.ToString()));
                line += $" Skipped {Skipped} cop{(Skipped == 1 ? "y" : "ies")} of {Unknown.Count} card{(Unknown.Count == 1 ? "" : "s")} the game doesn't have: {codes}"
                        + (Unknown.Count > 4 ? $" (+{Unknown.Count - 4} more)." : ".");
            }
            return line;
        }

        /// <summary>A deck's name from a file's name: "ydk-decklist.ydk" is "ydk-decklist" (safe as a file name, at most 60 characters).</summary>
        public static string NameFromFile(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path ?? "");
            return CleanName(name);
        }

        /// <summary>A name the player gave (or a file's): no control characters or path separators, no leading dots, at most 60 characters.</summary>
        public static string CleanName(string name)
        {
            // One encoding of each accented letter (FormC): APFS treats "Café" spelled either way as one file name.
            string normalized;
            try { normalized = (name ?? "").Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { normalized = name ?? ""; } // an unpaired surrogate can't be normalized
            var sb = new StringBuilder();
            foreach (var c in normalized)
                // No control characters or path separators; no < or > (names are shown in rich-text labels).
                sb.Append(char.IsControl(c) || c == '/' || c == '\\' || c == ':' || c == '<' || c == '>' ? ' ' : c);
            var clean = string.Join(" ", sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).TrimStart('.', ' ').Trim();
            if (clean.Length > 60)
            {
                int cut = char.IsHighSurrogate(clean[59]) ? 59 : 60; // never half a surrogate pair
                clean = clean.Substring(0, cut).TrimEnd();
            }
            return clean.Length == 0 ? "imported deck" : clean;
        }

        /// <summary>Reads a .ydk file from disk. Never throws: a missing, unreadable, huge or binary file comes back with an <see cref="Error"/>.</summary>
        public static YdkImport ReadFile(string path, ICardDataProvider cards)
        {
            byte[] bytes;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return Fail($"Couldn't find {Path.GetFileName(path)}.");
                if (info.Length > MaxBytes)
                    return Fail($"{Path.GetFileName(path)} is too big to be a deck list ({info.Length / 1024:N0} KB; the limit is {MaxBytes / 1024:N0} KB).");
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                return Fail($"Couldn't read {Path.GetFileName(path)}: {ex.Message}");
            }
            // UTF-16 with a byte-order mark (some Windows editors save that way) reads too; anything else with a NUL byte isn't text.
            string text;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            else if (Array.IndexOf(bytes, (byte)0) >= 0)
                return Fail($"{Path.GetFileName(path)} isn't a text file the game can read (if it's a deck list, save it as UTF-8).");
            else text = Encoding.UTF8.GetString(bytes);
            return Read(text, NameFromFile(path), cards, Path.GetFileName(path));
        }

        /// <summary>
        /// Reads .ydk text: sections "#main", "#extra" and "!side" (any case), one card code per line. A byte-order mark,
        /// CRLF or CR line ends, blank lines, spaces and tabs around a code, and anything after the code on its line are all
        /// fine. Codes before the first section are skipped.
        /// </summary>
        public static YdkImport Read(string text, string name, ICardDataProvider cards, string source = "the file")
        {
            text = (text ?? "").Replace("﻿", "");
            if (text.Trim().Length == 0) return Fail($"{source} is empty.");
            var result = new YdkImport();
            var deck = new Deck { Name = name };
            List<uint>? current = null;
            bool sawSection = false;
            int codes = 0;
            var unknown = new Dictionary<uint, int>();
            var order = new List<uint>();
            foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var lower = line.ToLowerInvariant();
                if (lower.StartsWith("#main")) { current = deck.Main; sawSection = true; continue; }
                if (lower.StartsWith("#extra")) { current = deck.Extra; sawSection = true; continue; }
                if (lower.StartsWith("!side")) { current = deck.Side; sawSection = true; continue; }
                if (line[0] == '#' || line[0] == '!') continue; // comments, "#created by", the game's own note lines
                if (current == null) continue;
                // The code is the line's first word ("12345", "  12345  ", "12345 # Ash Blossom").
                var word = line.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)[0];
                if (!uint.TryParse(word, out var code) || code == 0) { result.BadLines++; continue; }
                codes++;
                if (cards.Get(code) == null)
                {
                    if (!unknown.ContainsKey(code)) { unknown[code] = 0; order.Add(code); }
                    unknown[code]++;
                    continue;
                }
                current.Add(code);
            }
            foreach (var code in order) result.Unknown.Add((code, unknown[code]));
            if (!sawSection)
                return Fail($"{source} isn't a .ydk deck list: it has no #main, #extra or !side section.");
            if (codes == 0)
                return Fail($"{source} has no card codes in its #main, #extra or !side sections.", result);
            if (deck.Main.Count + deck.Extra.Count + deck.Side.Count == 0)
                return Fail($"None of the {codes} card codes in {source} are in the game's card database ({string.Join(", ", order.Take(4))}{(order.Count > 4 ? "..." : "")}).", result);
            if (deck.Main.Count + deck.Extra.Count == 0)
                return Fail($"{source} has no Main or Extra Deck cards the game has (only a Side Deck), so there's nothing to build.", result);
            result.Deck = deck;
            return result;
        }

        private static YdkImport Fail(string error, YdkImport? partial = null)
        {
            var result = partial ?? new YdkImport();
            result.Deck = null;
            result.Error = error;
            return result;
        }
    }
}
