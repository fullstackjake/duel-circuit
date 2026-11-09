using System;
using System.Collections.Generic;
using System.IO;

namespace Ygo.Engine
{
    public sealed class Deck
    {
        public string Name = "";
        public List<uint> Main = new List<uint>();
        public List<uint> Extra = new List<uint>();
        /// <summary>The copies this deck plays in a duel (a look per main- then extra-deck slot); null for a CPU's deck. Not saved.</summary>
        public CopyLook[]? Looks;
        public List<uint> Side = new List<uint>();
        /// <summary>Cards the player marked as combo starters (for opening-hand odds). Saved as a "#starters" comment.</summary>
        public List<uint> Starters = new List<uint>();
        /// <summary>A campaign deck: built from your collection. Saved as a "#campaign" line; other tools ignore it.</summary>
        public bool Campaign;
        /// <summary>
        /// Recipe notes, saved as "#key value" lines: era, tier, about (how it worked), why (why it mattered), source,
        /// banlist (the Forbidden/Limited list of its era, selected when the deck editor opens it).
        /// </summary>
        public Dictionary<string, string> Notes = new Dictionary<string, string>();
        // "passes": Ban Pass cards (campaign); "loaner": the recipe a custom loaner was made from; "originals": the deck
        // plays its era's original texts, and "current" the ids it keeps on current text on purpose (the `#originals`
        // lint, docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md S3).
        public static readonly string[] NoteKeys = { "era", "tier", "about", "why", "source", "banlist", "price", "title", "label", "passes", "loaner", "recipe", "partner", "rating", "originals", "current", "set" };

        public string Note(string key) => Notes.TryGetValue(key, out var v) ? v : "";

        public Deck Clone() => new Deck
        {
            Name = Name, Main = new List<uint>(Main), Extra = new List<uint>(Extra),
            Side = new List<uint>(Side), Starters = new List<uint>(Starters), Campaign = Campaign,
            Notes = new Dictionary<string, string>(Notes), Looks = Looks,
        };

        /// <summary>Parses the standard .ydk format used by EDOPro and most deck builders.</summary>
        public static Deck ParseYdk(string text, string name = "")
        {
            var deck = new Deck { Name = name };
            List<uint>? current = null;
            using var reader = new StringReader(text);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#starters"))
                {
                    foreach (var part in line.Substring(9).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
                        if (uint.TryParse(part, out var starter)) deck.Starters.Add(starter);
                    continue;
                }
                if (line.StartsWith("#campaign")) { deck.Campaign = true; continue; }
                var note = System.Array.Find(NoteKeys, k => line.StartsWith("#" + k + " "));
                if (note != null) { deck.Notes[note] = line.Substring(note.Length + 2).Trim(); continue; }
                // Historic recipes name themselves on their first line: "#created by Duel Circuit - Frog FTK (2010)".
                const string Created = "#created by Duel Circuit - ";
                if (line.StartsWith(Created) && !deck.Notes.ContainsKey("title"))
                {
                    var title = line.Substring(Created.Length);
                    var year = title.LastIndexOf(" (", StringComparison.Ordinal);
                    deck.Notes["title"] = (year > 0 ? title.Substring(0, year) : title).Trim();
                    continue;
                }
                if (line.StartsWith("#main")) { current = deck.Main; continue; }
                if (line.StartsWith("#extra")) { current = deck.Extra; continue; }
                if (line.StartsWith("!side")) { current = deck.Side; continue; }
                if (line[0] == '#' || line[0] == '!') continue;
                if (current != null && uint.TryParse(line, out var code))
                    current.Add(code);
            }
            return deck;
        }

        public static Deck LoadYdk(string path) =>
            ParseYdk(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
    }
}
