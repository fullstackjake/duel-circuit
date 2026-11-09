using System.Collections.Generic;
using System.Linq;

namespace Ygo.Engine.DeckBuilding
{
    /// <summary>
    /// The versions of each card the database has, grouped under its base card (the one other entries alias):
    /// <list type="bullet">
    /// <item>artworks: entries with the same name, which play identically and only change the picture;</item>
    /// <item>texts: the current text, plus original versions as played in their day ("Sangan (GOAT)",
    /// "Chaos Emperor Dragon - Envoy of the End (Pre-Errata)"), which have their own scripts.</item>
    /// </list>
    /// All versions count as one card for deck limits and the campaign collection.
    /// </summary>
    public sealed class CardVersions
    {
        public enum TextKind { Current, Goat, PreErrata, Other }

        private readonly ICardDataProvider _cards;
        private readonly Dictionary<uint, List<uint>> _byBase = new Dictionary<uint, List<uint>>();

        /// <summary>The text timeline (campaign/texts.txt): row labels, dups to hide, rows players can't own.</summary>
        public CardTexts TextRows { get; }

        public CardVersions(ICardDataProvider cards, CardTexts? texts = null)
        {
            _cards = cards;
            TextRows = texts ?? CardTexts.Empty;
            foreach (var info in cards.All.Where(c => c.Code != 0).OrderBy(c => c.Code))
            {
                var key = Banlist.BaseCode(info.Code, cards);
                if (!_byBase.TryGetValue(key, out var list)) _byBase[key] = list = new List<uint>();
                list.Add(info.Code);
            }
            // The base card first, then the rest in id order.
            foreach (var pair in _byBase)
                if (pair.Value.Remove(pair.Key)) pair.Value.Insert(0, pair.Key);
        }

        public static TextKind KindOf(CardInfo info) =>
            info.Name.EndsWith("(GOAT)") ? TextKind.Goat
            : info.Name.EndsWith("(Pre-Errata)") ? TextKind.PreErrata
            : info.Alias == 0 || !info.Name.EndsWith(")") ? TextKind.Current : TextKind.Other;

        public static string Label(TextKind kind) => kind switch
        {
            TextKind.Goat => "Goat format (2005)",
            TextKind.PreErrata => "Original (pre-errata)",
            TextKind.Current => "Current text",
            _ => "Alternate text",
        };

        private List<uint> All(uint code) =>
            _byBase.TryGetValue(Banlist.BaseCode(code, _cards), out var list) ? list : new List<uint> { code };

        private string NameOf(uint code) => _cards.Get(code)?.Name ?? "";

        /// <summary>
        /// Artworks of this card's text: the current text's same-named entries, the base picture first. An original text
        /// (a GOAT or Pre-Errata entry) has exactly one, itself.
        /// </summary>
        public List<uint> Arts(uint code)
        {
            if (_cards.Get(code) is CardInfo info && KindOf(info) != TextKind.Current) return new List<uint> { code };
            var name = NameOf(code);
            return All(code).Where(c => NameOf(c) == name && (_cards.Get(c) is not CardInfo i || KindOf(i) == TextKind.Current)).ToList();
        }

        /// <summary>
        /// One entry per text, current text first (its first artwork), then each original by entry, not name (spec S1:
        /// Mind Master's two originals are two texts). A row marked `dup` is hidden: it shows as its canonical text (S2).
        /// </summary>
        public List<uint> Texts(uint code)
        {
            var seen = new HashSet<string>();
            return All(code).Where(c => _cards.Get(c) is CardInfo i && KindOf(i) != TextKind.Current
                    ? !TextRows.IsDup(c)
                    : seen.Add(NameOf(c)))
                .ToList();
        }

        /// <summary>
        /// The texts the player's CARD TEXT switch offers: <see cref="Texts"/> without a row marked `cpu` or `never` (spec
        /// S1, 9.1: Mind Master's OCG-first text is for CPU decks only; no TCG printing carries a `never` text, and a
        /// player's slot plays it as current). A slot already holding that entry still shows it.
        /// </summary>
        public List<uint> PlayerTexts(uint code)
        {
            var shown = Canonical(code);
            return Texts(code).Where(c => c == shown || TextRows.Row(c) is not TextRow row || !row.CpuOnly && !row.Never).ToList();
        }

        /// <summary>The text a deck slot shows: a dup's canonical entry, else the slot.</summary>
        public uint Canonical(uint code) => TextRows.IsDup(code) && TextRows.Row(code) is TextRow row ? row.Code : code;

        /// <summary>A text's label: its row's ("Original text (until Sep 2016)"), else its kind's.</summary>
        public string TextLabel(uint code)
        {
            var label = TextRows.Label(code);
            return label.Length > 0 ? label : _cards.Get(code) is CardInfo info ? Label(KindOf(info)) : "";
        }

        /// <summary>
        /// The original version of a card if it has one, else null. Goat entries are preferred for 2005 lists. The editor's
        /// default under old lists; where an original plays is <see cref="TextRules"/>'s call.
        /// </summary>
        public uint? Original(uint code, bool preferGoat)
        {
            // Rows no TCG printing carries (`never`, `cpu`) aren't a default.
            var texts = Texts(code).Where(c => TextRows.Row(c) is not TextRow row || !row.Never && !row.CpuOnly)
                .Select(c => (code: c, kind: _cards.Get(c) is CardInfo i ? KindOf(i) : TextKind.Current)).ToList();
            var first = preferGoat ? TextKind.Goat : TextKind.PreErrata;
            var second = preferGoat ? TextKind.PreErrata : TextKind.Goat;
            foreach (var kind in new[] { first, second })
                foreach (var t in texts)
                    if (t.kind == kind) return t.code;
            return null;
        }

        /// <summary>Whether a ban list is from the era of the original card versions (through 2005, or Goat format).</summary>
        public static bool IsOriginalEra(string banlist, out bool preferGoat)
        {
            preferGoat = banlist.Contains("GOAT") || banlist.Contains("2005");
            var digits = System.Text.RegularExpressions.Regex.Match(banlist, @"(?<!\d)(20\d\d)(?!\d)");
            return preferGoat || (digits.Success && int.Parse(digits.Value) <= 2005);
        }
    }
}
