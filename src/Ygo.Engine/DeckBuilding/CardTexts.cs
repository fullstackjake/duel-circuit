using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ygo.Engine.Data;

namespace Ygo.Engine.DeckBuilding
{
    /// <summary>
    /// One original printed TCG text of a card (a line of campaign/texts.txt): its "(Pre-Errata)" version entry, and the
    /// month the TCG text changed. A printing dated before <see cref="Until"/> carries this text, and a table whose texts
    /// date is before it plays it (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md, sections 5 and 9.1).
    /// </summary>
    public sealed class TextRow
    {
        public uint Code, Base;
        /// <summary>"yyyy-mm": the first month of the new text. "" for a `never` row (and a `cpu` row may leave it out).</summary>
        public string Until = "";
        /// <summary>The set prefix whose printing brought the new text ("DPRP"), or "" when the change was a database one.</summary>
        public string Set = "";
        /// <summary>Never: no TCG printing carries it (an OCG or anime text). CpuOnly: playable by CPUs, not collectible.</summary>
        public bool Never, CpuOnly;
        /// <summary>An identical entry's canonical id: this one only loads (decks and replays keep it), it is never shown.</summary>
        public uint DupOf;

        /// <summary>A text a copy can carry: a dated row that is neither never, CPU-only nor a duplicate.</summary>
        public bool Collectible => !Never && !CpuOnly && DupOf == 0 && Until.Length > 0;
    }

    /// <summary>
    /// The card text timeline (campaign/texts.txt): which original texts each card was printed with, and until when.
    /// Read by <see cref="PlayTexts"/> (what a slot plays at a table), <see cref="CardVersions"/> (texts by entry, not
    /// name) and the `--texts-check`.
    /// </summary>
    public sealed class CardTexts
    {
        public const string File = "texts.txt";

        public static readonly CardTexts Empty = new CardTexts();

        private readonly Dictionary<uint, TextRow> _rows = new Dictionary<uint, TextRow>();
        private readonly Dictionary<uint, List<TextRow>> _timeline = new Dictionary<uint, List<TextRow>>();
        private readonly Dictionary<uint, uint> _goat = new Dictionary<uint, uint>();
        private readonly List<string> _parseProblems = new List<string>();

        private static readonly Regex Month = new Regex(@"^\d{4}-(0[1-9]|1[0-2])$");

        public int Count => _rows.Count;
        public IEnumerable<TextRow> Rows => _rows.Values;

        /// <summary>
        /// Reads texts.txt: "&lt;version id&gt; until &lt;yyyy-mm&gt; [set &lt;prefix&gt;] [cpu] [dup &lt;id&gt;]", or
        /// "&lt;id&gt; never", "# comment" anywhere. Lines it can't read are kept for <see cref="Problems"/>.
        /// </summary>
        public static CardTexts Parse(string text, ICardDataProvider cards)
        {
            var texts = new CardTexts();
            foreach (var info in cards.All)
                if (info.Alias != 0 && CardVersions.KindOf(info) == CardVersions.TextKind.Goat && !texts._goat.ContainsKey(Banlist.BaseCode(info.Code, cards)))
                    texts._goat[Banlist.BaseCode(info.Code, cards)] = info.Code;
            int lineNo = 0;
            foreach (var raw in text.Split('\n'))
            {
                lineNo++;
                var hash = raw.IndexOf('#');
                var line = (hash >= 0 ? raw.Substring(0, hash) : raw).Trim();
                if (line.Length == 0) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (!uint.TryParse(parts[0], out var code)) { texts._parseProblems.Add($"line {lineNo}: no version id: \"{line}\""); continue; }
                var row = new TextRow { Code = code, Base = Banlist.BaseCode(code, cards) };
                for (int i = 1; i < parts.Length; i++)
                {
                    string Value() => i + 1 < parts.Length ? parts[++i] : "";
                    switch (parts[i])
                    {
                        case "until": row.Until = Value(); break;
                        case "set": row.Set = Value(); break;
                        case "cpu": row.CpuOnly = true; break;
                        case "never": row.Never = true; break;
                        case "dup":
                            if (!uint.TryParse(Value(), out row.DupOf) || row.DupOf == 0) texts._parseProblems.Add($"line {lineNo}: {code}: dup without an id");
                            break;
                        case "/": break; // "until ????-?? / cpu" reads as "until ????-?? cpu"
                        default: texts._parseProblems.Add($"line {lineNo}: {code}: unknown word \"{parts[i]}\""); break;
                    }
                }
                if (texts._rows.ContainsKey(code)) { texts._parseProblems.Add($"line {lineNo}: {code} has a second row"); continue; }
                // Only a Pre-Errata entry has a row (a GOAT entry is the table's 2005 ruling, section 3); others are left out.
                var info = cards.Get(code);
                if (info == null) { texts._parseProblems.Add($"line {lineNo}: {code} is not a card in the database"); continue; }
                if (row.Base == code || CardVersions.KindOf(info) != CardVersions.TextKind.PreErrata)
                {
                    texts._parseProblems.Add($"line {lineNo}: {code} {info.Name} is not a Pre-Errata entry"
                                             + (CardVersions.KindOf(info) == CardVersions.TextKind.Goat ? " (a GOAT entry needs no row: it is the 2005 ruling)" : ""));
                    continue;
                }
                texts._rows[code] = row;
                if (row.DupOf != 0) continue;
                if (!texts._timeline.TryGetValue(row.Base, out var list)) texts._timeline[row.Base] = list = new List<TextRow>();
                list.Add(row);
            }
            return texts;
        }

        /// <summary>Reads campaign/texts.txt; a missing file reads as no rows (every original then plays current text, but where printed texts play).</summary>
        public static CardTexts Load(string path, ICardDataProvider cards) =>
            Parse(System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : "", cards);

        /// <summary>A card's rows, oldest first as the file lists them (collectible, cpu and never rows; no dups).</summary>
        public IReadOnlyList<TextRow> Timeline(uint baseCode) =>
            _timeline.TryGetValue(baseCode, out var list) ? list : (IReadOnlyList<TextRow>)Array.Empty<TextRow>();

        /// <summary>A version entry's row, or null when it has none; a dup resolves to its canonical row.</summary>
        public TextRow? Row(uint versionCode)
        {
            if (!_rows.TryGetValue(versionCode, out var row)) return null;
            if (row.DupOf == 0) return row;
            return _rows.TryGetValue(row.DupOf, out var canonical) && canonical.DupOf == 0 ? canonical : null;
        }

        /// <summary>Whether this id is a row marked `dup` (only loads; shown as its canonical text).</summary>
        public bool IsDup(uint versionCode) => _rows.TryGetValue(versionCode, out var row) && row.DupOf != 0;

        /// <summary>A card's GOAT entry (its 2005 ruling), or 0.</summary>
        public uint Goat(uint baseCode) => _goat.TryGetValue(baseCode, out var code) ? code : 0;

        /// <summary>A card's earliest collectible text (what its GOAT entry needs a copy of), or null for a ruling-only card.</summary>
        public TextRow? Earliest(uint baseCode) => Timeline(baseCode).FirstOrDefault(r => r.Collectible);

        /// <summary>The text a printing dated <paramref name="printedYm"/> ("yyyy-mm") carries: the row in force then, 0 = current.</summary>
        public uint PrintedText(uint baseCode, string printedYm)
        {
            if (printedYm.Length < 7) return 0;
            var month = printedYm.Substring(0, 7);
            foreach (var row in Timeline(baseCode))
                if (row.Collectible && string.CompareOrdinal(month, row.Until) < 0) return row.Code;
            return 0;
        }

        /// <summary>"Original text (until Sep 2016)"; "" for an entry with no row.</summary>
        public string Label(uint versionCode)
        {
            var row = Row(versionCode);
            if (row == null) return "";
            if (row.Never) return "Original text (no TCG printing)"; // OCG texts, ruling-only versions and one-off misprints alike
            var until = Month.IsMatch(row.Until) ? $"until {ShortMonth(row.Until)}" : "";
            if (row.CpuOnly) return until.Length > 0 ? $"Original text ({until}, CPUs only)" : "Original text (CPUs only)";
            return until.Length > 0 ? $"Original text ({until})" : "Original text";
        }

        /// <summary>"Sep 2016" for "2016-09".</summary>
        public static string ShortMonth(string ym)
        {
            var full = DeckRules.MonthYear(ym);
            var space = full.IndexOf(' ');
            return space > 3 ? full.Substring(0, 3) + full.Substring(space) : full;
        }

        /// <summary>
        /// What the `--texts-check` refuses (section 12): a Pre-Errata entry with no row; a row for an unknown id, a GOAT
        /// entry or a current card (left out when read); an `until` that doesn't read as "yyyy-mm" (a `?` included); rows of one card out of
        /// order; a dup that doesn't point at a canonical row of the same card; a `set` whose printing month isn't
        /// `until`; a collectible row whose card has no TCG printing dated before `until`. Printings are dated by
        /// <paramref name="dateOf"/> ("yyyy-mm-dd"; by set name, F3), else by their prefix's date.
        /// </summary>
        public List<string> Problems(ICardDataProvider cards, PrintingIndex printings, Func<Printing, string>? dateOf = null)
        {
            var problems = new List<string>(_parseProblems);
            dateOf ??= p => printings.Sets.TryGetValue(PrintingIndex.SetPrefix(p.SetCode), out var set) ? set.Date : "";
            string Name(uint code) => cards.Get(code)?.Name ?? $"#{code}";
            foreach (var info in cards.All.Where(c => c.Alias != 0 && CardVersions.KindOf(c) == CardVersions.TextKind.PreErrata).OrderBy(c => c.Code))
                if (!_rows.ContainsKey(info.Code)) problems.Add($"{info.Code} {info.Name}: no row (a date, `never`, `cpu` or `dup`)");
            foreach (var row in _rows.Values.OrderBy(r => r.Code))
            {
                var what = $"{row.Code} {Name(row.Code)}";
                if (row.DupOf != 0)
                {
                    var canonical = _rows.TryGetValue(row.DupOf, out var c) ? c : null;
                    if (canonical == null || canonical.DupOf != 0 || canonical.Base != row.Base)
                        problems.Add($"{what}: dup {row.DupOf} isn't a canonical row of the same card");
                    if (row.Until.Length > 0 || row.Set.Length > 0 || row.Never || row.CpuOnly) problems.Add($"{what}: a dup carries nothing but its canonical id");
                    continue;
                }
                if (row.Never)
                {
                    if (row.Until.Length > 0 || row.Set.Length > 0 || row.CpuOnly) problems.Add($"{what}: a `never` row has no date, set or `cpu`");
                    continue;
                }
                if (row.Until.Length == 0 && !row.CpuOnly) { problems.Add($"{what}: no `until`"); continue; }
                if (row.Until.Length > 0 && !Month.IsMatch(row.Until)) { problems.Add($"{what}: until \"{row.Until}\" isn't a month (yyyy-mm)"); continue; }
                if (row.CpuOnly) continue;
                var dated = printings.Of(row.Base).Select(p => (p, date: dateOf(p))).Where(x => x.date.Length >= 7).ToList();
                if (row.Set.Length > 0)
                {
                    var inSet = dated.Where(x => PrintingIndex.SetPrefix(x.p.SetCode) == row.Set).Select(x => x.date.Substring(0, 7)).OrderBy(d => d, StringComparer.Ordinal).ToList();
                    if (inSet.Count == 0) problems.Add($"{what}: no printing in set {row.Set}");
                    else if (inSet[0] != row.Until) problems.Add($"{what}: set {row.Set} is dated {inSet[0]}, not {row.Until}");
                }
                if (!dated.Any(x => string.CompareOrdinal(x.date.Substring(0, 7), row.Until) < 0))
                    problems.Add($"{what}: no TCG printing dated before {row.Until}, so no copy can carry it (mark it `never` or `cpu`?)");
            }
            foreach (var pair in _timeline)
            {
                var dates = pair.Value.Where(r => r.Collectible).Select(r => r.Until).ToList();
                for (int i = 1; i < dates.Count; i++)
                    if (string.CompareOrdinal(dates[i - 1], dates[i]) >= 0)
                        problems.Add($"{Name(pair.Key)}: rows out of order ({dates[i - 1]} then {dates[i]}; oldest first)");
            }
            return problems;
        }
    }
}
