using System.Text.Json;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--texts-check [--texts &lt;file&gt;]`: campaign/texts.txt, the card text timeline (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md
/// 9.1, check table in section 12): every Pre-Errata entry has a row; every date reads as a month; a `set`'s printing is
/// dated `until`; every collectible text has a TCG printing before it. Printings are dated as copies are stamped, by
/// <see cref="PrintingIndex.DateOf"/> (set name, F3; reprints by code, campaign/printing-dates.txt), and no reprint the
/// source files under an older set can stamp an original (phase 2's reprint fix). Then the `#originals` lint (S3) over
/// every deck, and where each original plays.
/// </summary>
public static class TextsCheck
{
    /// <summary>
    /// The spec's well-sourced rows (9.1), for tests that must not wait on the real file: Sangan, Witch, CED, Catapult
    /// Turtle, Ring of Destruction and Crush Card Virus with their dups, Mind Master's two texts, Toon World's OCG text.
    /// </summary>
    public const string Fixture = @"# Test rows (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 9.1)
511000228 until 2014-06 set DL18     # Catapult Turtle
511000822 until 2015-03 set PGL2     # Crush Card Virus
511000823 dup 511000822              # Crush Card Virus
511000825 until 2015-03 set PGL2     # Ring of Destruction
511000824 dup 511000825              # Ring of Destruction
511000819 until 2018-11              # Chaos Emperor Dragon
511002631 until 2016-09 set DPRP     # Sangan
511003012 until 2017-07 set BLLR     # Witch of the Black Forest
511003019 cpu                        # Mind Master
96782896  until 2026-09              # Mind Master
511004007 never                      # Toon World
";

    public static int Run(ICardDataProvider cards, string root, string? path)
    {
        path ??= Path.Combine(root, "campaign", CardTexts.File);
        Console.WriteLine($"Card texts: {path}");
        if (!File.Exists(path))
        {
            Console.WriteLine("  FAIL no texts file (campaign/texts.txt is written by the research chunk, 1.0)");
            return 1;
        }
        var texts = CardTexts.Load(path, cards);
        var printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        var rows = texts.Rows.ToList();
        Console.WriteLine($"  {rows.Count} rows: {rows.Count(r => r.Collectible)} collectible, {rows.Count(r => r.CpuOnly)} cpu, "
                          + $"{rows.Count(r => r.Never)} never, {rows.Count(r => r.DupOf != 0)} dup; "
                          + $"{rows.Select(r => r.Base).Distinct().Count()} cards; {printings.NameDates.Count} set names dated, {printings.ReprintDates.Count} reprint dates");
        int failures = 0;
        var problems = texts.Problems(cards, printings, printings.DateOf);
        foreach (var problem in problems) Console.WriteLine($"  FAIL {problem}");
        failures += problems.Count;
        failures += Reprints(cards, root, texts, printings);

        // Where each original plays as printed (worked as in section 5.3): the Library cups by pool month.
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities"));
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var library = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), roster, circuits);
        var lists = Banlist.LoadFolders(new[] { Path.Combine(root, "vendor", "LFLists"), Path.Combine(root, "lflists") }).ToList();
        var cups = Enumerable.Range(0, library.Cups.Count).Select(c => (cup: library.Cups[c], rules: library.Rules(c, lists).Texts)).ToList();
        var newest = TextRules.ForList(lists.Where(l => l.Name.StartsWith("TCG ")).OrderBy(l => l.Date, StringComparer.Ordinal).Last(), lists);
        Console.WriteLine("\nWhere each original plays as printed (Library cups; the alley always; never under the newest list):");
        foreach (var row in rows.Where(r => r.Collectible).OrderBy(r => r.Until, StringComparer.Ordinal).ThenBy(r => r.Code))
        {
            var at = cups.Where(c => c.rules.Allows(row)).Select(c => c.cup.Name).ToList();
            Console.WriteLine($"  {row.Until}  {cards.Get(row.Code)?.Name ?? row.Code.ToString(),-56} {at.Count,2} of {cups.Count} cups{(at.Count > 0 ? $", through {at[^1]}" : "")}");
            if (newest.Allows(row)) { Console.WriteLine($"  FAIL {row.Code}: plays as printed under the newest list (AsOf {newest.AsOf})"); failures++; }
        }

        // The `#originals` lint (S3): a deck that says it plays its era's originals uses an original id for every card
        // whose original its list's texts allow, except the ids it names on `#current`.
        Console.WriteLine("\n#originals lint:");
        int linted = 0;
        foreach (var file in Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories).OrderBy(f => f))
        {
            var deck = Deck.LoadYdk(file);
            if (deck.Note("originals").Length == 0) continue;
            linted++;
            var name = Path.GetRelativePath(Path.Combine(root, "decks"), file);
            var list = lists.FirstOrDefault(l => l.Name == deck.Note("banlist"));
            if (list == null) { Console.WriteLine($"  FAIL {name}: #originals with no installed #banlist"); failures++; continue; }
            var rules = TextRules.ForList(list, lists);
            var kept = deck.Note("current").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => uint.TryParse(s, out var c) ? c : 0).ToHashSet();
            var hits = deck.Main.Concat(deck.Extra).Concat(deck.Side).Distinct()
                .Where(c => cards.Get(c) is CardInfo info && CardVersions.KindOf(info) == CardVersions.TextKind.Current)
                .Where(c => !kept.Contains(c) && !kept.Contains(Banlist.BaseCode(c, cards)))
                .Where(c => texts.Timeline(Banlist.BaseCode(c, cards)).Any(r => r.Collectible && rules.Allows(r)))
                .ToList();
            foreach (var hit in hits)
                Console.WriteLine($"  FAIL {name}: {cards.Get(hit)?.Name} ({hit}) on its current id; its original plays under {list.Name} ({rules.Describe()})");
            if (hits.Count == 0) Console.WriteLine($"  ok   {name} ({list.Name}, {rules.Describe()})");
            failures += hits.Count;
        }
        Console.WriteLine(linted == 0 ? "  (no deck says #originals)" : "");
        failures += GlyphCorpus(root);
        Console.WriteLine(failures == 0 ? "All texts checks passed." : $"{failures} texts check(s) FAILED.");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// The reprint fix (docs/dev/CV2-BUILD-2026-11-05.md): printings.bin carries campaign/printing-dates.txt as built; every entry
    /// dates the printings it matches and none of them stamps an original; the 25th Anniversary reprints of Sangan, Catapult
    /// Turtle, Witch of the Black Forest, Imperial Order, Inspection and Toon World, Stronghold the Moving Fortress's DPYG
    /// reprint and the other Duelist Pack reprints carry the current text; CED's JUMP-EN086 is 2018; and every set the data
    /// names "(25th Anniversary Edition)" has its prefix covered (a data refresh that adds one must be looked at).
    /// </summary>
    public static int Reprints(ICardDataProvider cards, string root, CardTexts texts, PrintingIndex printings)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        var file = Path.Combine(root, "campaign", PrintingsImport.ReprintsFile);
        var listed = File.Exists(file) ? PrintingsImport.ReadReprints(File.ReadAllText(file)) : new List<(string, string)>();
        Check(listed.Count > 0 && listed.SequenceEqual(printings.ReprintDates),
            $"printings.bin (version {PrintingIndex.Version}) carries campaign/{PrintingsImport.ReprintsFile} as written ({printings.ReprintDates.Count} of {listed.Count} entries; regenerate with --export-printings)");
        // Every printing an entry matches: dated by it, and carrying the current text (the reprints are current-text printings).
        var matched = new Dictionary<string, int>();
        var originals = new List<string>();
        foreach (var pair in printings.All)
        {
            var info = cards.Get(pair.Key);
            if (info == null) continue;
            var baseCode = Banlist.BaseCode(pair.Key, cards);
            foreach (var p in pair.Value)
            {
                var entry = printings.ReprintDates.FirstOrDefault(e => PrintingIndex.CodeMatches(p.SetCode, e.code));
                if (entry.code == null) continue;
                matched[entry.code] = matched.GetValueOrDefault(entry.code) + 1;
                var date = printings.DateOf(p);
                if (date != entry.date || texts.PrintedText(baseCode, date) != 0) originals.Add($"{p.SetCode} {info.Name} ({date})");
            }
        }
        var unused = printings.ReprintDates.Where(e => !matched.ContainsKey(e.code)).Select(e => e.code).ToList();
        Check(unused.Count == 0 && originals.Count == 0,
            $"{matched.Values.Sum()} printings dated by {printings.ReprintDates.Count} reprint entries, none carrying an original text"
            + (unused.Count > 0 ? $"; matching nothing: {string.Join(", ", unused)}" : "") + (originals.Count > 0 ? $"; originals: {string.Join(", ", originals.Take(6))}" : ""));
        // The named reprints (phase 1's research), each through the same calls a copy is stamped by.
        var named = new (uint code, string set, Rarity rarity)[]
        {
            (26202165, "MRD-EN069", Rarity.Rare), (95727991, "MRD-EN075", Rarity.SuperRare), (78010363, "MRD-EN116", Rarity.Rare),
            (61740673, "PSV-EN104", Rarity.SecretRare), (16227556, "PSV-EN038", Rarity.Common), (15259703, "SRL-EN076", Rarity.SuperRare),
            (13955608, "DPYG-EN030", Rarity.Common), (65240384, "DPYG-EN009", Rarity.Common), (95727991, "DPYG-EN006", Rarity.Common),
            (82301904, "IOC-EN000", Rarity.SecretRare), (40737112, "IOC-EN065", Rarity.UltraRare),
        };
        var wrong = new List<string>();
        foreach (var (code, set, rarity) in named)
        {
            var p = printings.Of(code).Where(x => x.SetCode == set && x.Rarity == rarity).Select(x => (Printing?)x).FirstOrDefault();
            if (p == null) { wrong.Add($"{set}: not in printings.bin"); continue; }
            var date = printings.DateOf(p.Value);
            var copy = new Collection { Texts = texts };
            var c = new CardCopy { Code = code };
            copy.Stamp(c, p, printings);
            if (c.Text != 0 || CopyPrints.YearOf(date) < 2013) wrong.Add($"{set} {cards.Get(code)?.Name} stamps text {c.Text} ({date})");
        }
        Check(wrong.Count == 0, $"the {named.Length} named reprints (25th Anniversary MRD/PSV/SRL/IOC, Duelist Pack: Yugi) stamp the current text{(wrong.Count > 0 ? ": " + string.Join("; ", wrong) : "")}");
        // The first printings under the same codes still read 2002 (a 2002 booster's pull is the 2002 printing).
        var sangan2002 = printings.Of(26202165).FirstOrDefault(x => x.SetCode == "MRD-069");
        var ced2004 = printings.Of(82301904).FirstOrDefault(x => x.SetCode == "IOC-000");
        Check(sangan2002.SetCode != null && printings.DateOf(sangan2002).StartsWith("2002-") && texts.PrintedText(26202165, printings.DateOf(sangan2002)) != 0
              && ced2004.SetCode != null && printings.DateOf(ced2004).StartsWith("2004-") && texts.PrintedText(82301904, printings.DateOf(ced2004)) != 0,
            $"MRD-069 Sangan ({printings.DateOf(sangan2002)}) and IOC-000 Chaos Emperor Dragon ({printings.DateOf(ced2004)}) carry the original text");
        // F3: a prefix spanning years dates each printing by its set name.
        var jump = printings.Of(82301904).FirstOrDefault(x => x.SetCode == "JUMP-EN086");
        Check(jump.SetCode != null && printings.DateOf(jump).StartsWith("2018-11") && texts.PrintedText(82301904, printings.DateOf(jump)) == 0,
            $"CED's JUMP-EN086 (the errata printing) dates {printings.DateOf(jump)}, not the JUMP prefix's {printings.Sets.GetValueOrDefault("JUMP")?.Date}, and carries the current text");
        // A data refresh that adds a 25th Anniversary Edition must be looked at: its prefix needs an entry, or a note why not.
        var anniversary = printings.NameDates.Keys.Where(n => n.EndsWith("(25th Anniversary Edition)")).ToList();
        var prefixes = anniversary.Select(n => printings.Sets.Values.FirstOrDefault(s => n.StartsWith(s.Name + " ("))?.Code ?? "?").ToList();
        bool Covered(string prefix) => printings.ReprintDates.Any(e => e.code == prefix + "-EN*");
        var uncovered = prefixes.Where(x => !Covered(x)).ToList();
        Check(anniversary.Count > 0 && uncovered.Count == 0,
            $"every \"(25th Anniversary Edition)\" set has its EN reprints dated ({string.Join(", ", prefixes)})"
            + (uncovered.Count > 0 ? $"; not covered: {string.Join(", ", uncovered)}" : ""));
        // The reprint pattern by codes, whatever the set names say: a booster's main set name holding region-less codes and EN
        // codes of the same numbers, with rarities only the EN rows have (a 2003 Short Print as a Common). Each such prefix is
        // dated (an EN* entry) or reviewed: LON and LOD were never reprinted (campaign/printing-dates.txt's sources), so their
        // EN rows are European printings of the time.
        var reviewed = new HashSet<string> { "LON", "LOD" };
        var byPrefix = new Dictionary<string, Dictionary<string, (HashSet<Rarity> bare, HashSet<Rarity> en)>>();
        var mainName = printings.All.SelectMany(pr => pr.Value).GroupBy(x => PrintingIndex.SetPrefix(x.SetCode))
            .ToDictionary(g => g.Key, g => g.GroupBy(x => x.SetName).OrderByDescending(n => n.Count()).ThenBy(n => n.Key, StringComparer.Ordinal).First().Key);
        foreach (var pr in printings.All)
            foreach (var x in pr.Value)
            {
                var prefix = PrintingIndex.SetPrefix(x.SetCode);
                if (x.SetName != mainName[prefix]) continue;
                var suffix = x.SetCode.Length > prefix.Length + 1 ? x.SetCode.Substring(prefix.Length + 1) : "";
                bool en = suffix.StartsWith("EN") && suffix.Length > 2 && suffix.Skip(2).All(char.IsDigit), bare = suffix.Length > 0 && suffix.All(char.IsDigit);
                if (!en && !bare) continue;
                if (!byPrefix.TryGetValue(prefix, out var numbers)) byPrefix[prefix] = numbers = new Dictionary<string, (HashSet<Rarity>, HashSet<Rarity>)>();
                var number = $"{pr.Key}:{(en ? suffix.Substring(2) : suffix)}";
                if (!numbers.TryGetValue(number, out var r)) numbers[number] = r = (new HashSet<Rarity>(), new HashSet<Rarity>());
                (en ? r.en : r.bare).Add(x.Rarity);
            }
        var pattern = byPrefix.Where(p => p.Value.Values.Count(r => r.bare.Count > 0) >= 30 && p.Value.Values.Count(r => r.en.Count > 0) >= 30
                                          && p.Value.Values.Any(r => r.bare.Count > 0 && r.en.Count > 0 && !r.en.SetEquals(r.bare)))
            .Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var unreviewed = pattern.Where(x => !Covered(x) && !reviewed.Contains(x)).ToList();
        Check(pattern.Count > 0 && unreviewed.Count == 0,
            $"by codes: {pattern.Count} sets hold region-less and EN printings of one number with EN-only rarities ({string.Join(", ", pattern)}): "
            + $"{pattern.Count(Covered)} dated as reprints, {pattern.Count(reviewed.Contains)} reviewed as European printings of the time"
            + (unreviewed.Count > 0 ? $"; not covered or reviewed: {string.Join(", ", unreviewed)}" : ""));
        return failures;
    }

    /// <summary>Set name -> its TCG release date ("yyyy-mm-dd"), from YGOPRODeck's cardsets.json (the earliest when a name repeats).</summary>
    public static Dictionary<string, string> SetDatesByName(string path)
    {
        var dates = new Dictionary<string, string>();
        if (!File.Exists(path)) return dates;
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var set in doc.RootElement.EnumerateArray())
        {
            var name = set.TryGetProperty("set_name", out var n) ? n.GetString() ?? "" : "";
            var date = set.TryGetProperty("tcg_date", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : "";
            if (name.Length == 0 || date.Length < 7) continue;
            if (!dates.TryGetValue(name, out var had) || string.CompareOrdinal(date, had) < 0) dates[name] = date;
        }
        return dates;
    }

    // ---- The glyph corpus's freshness (docs/dev/TYPE-AND-FONTS-SPEC-2026-11-21.md, section 7 step 3) ----

    /// <summary>Where tools/glyph_corpus.py writes the corpus the game's -typecheck reads.</summary>
    public static string GlyphCorpusPath(string root) => Path.Combine(root, "unity", "Assets", "Resources", "glyph-corpus.txt");

    /// <summary>
    /// unity/Assets/Resources/glyph-corpus.txt is current: its "chars-sha256" line is the hash of the characters the sources
    /// hold now (the same rule as tools/glyph_corpus.py: string and char literals in unity/Assets/Scripts/*.cs, every file in
    /// campaign/ and personalities/), and its lines list exactly those characters. First a fixture shows the literal rule.
    /// </summary>
    public static int GlyphCorpus(string root)
    {
        Console.WriteLine("\nGlyph corpus (tools/glyph_corpus.py):");
        int failures = 0;
        void Report(string what, bool ok) { Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}"); if (!ok) failures++; }

        // The rule on a fixture: comments don't count, literals do (verbatim and interpolated too), escapes are decoded.
        const string fixture = "// ★ in a comment\n/* ☆ in a block */ var a = \"→ x\"; var b = '\\u2190'; var c = @\"say \"\"◆\"\"\"; var d = $\"{n} ✦\"; var e = \"\\\" ⬢\"; var f = \"\\uD83D\\uDCCC\";\n";
        var got = new string(CsLiterals(fixture).Where(ch => ch > 0x7F).ToArray());
        Report($"the literal rule on a fixture: \"{got}\" (comments skipped, verbatim, interpolated and escaped literals read; an escaped surrogate pair is one character, as tools/glyph_corpus.py reads it)",
            got == "→←◆✦⬢\U0001F4CC" && CorpusOf(got).SequenceEqual(new[] { "→", "←", "◆", "✦", "⬢", "\U0001F4CC" }));

        var path = GlyphCorpusPath(root);
        if (!File.Exists(path)) { Report($"{path} exists (run python3 tools/glyph_corpus.py)", false); return failures; }
        var lines = File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');
        var recorded = lines.FirstOrDefault(l => l.StartsWith("# chars-sha256: ", StringComparison.Ordinal))?.Substring("# chars-sha256: ".Length).Trim() ?? "";
        var listed = lines.Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal)).Select(l => l.Split('\t')[0]).ToList();
        var now = CorpusCharacters(root);
        var hash = CorpusHash(now);
        Report($"the corpus is current: {now.Count} characters in the sources, {listed.Count} listed; hash {(hash.Length >= 12 ? hash.Substring(0, 12) : hash)} "
               + $"{(hash == recorded ? "matches" : $"!= recorded {(recorded.Length >= 12 ? recorded.Substring(0, 12) : recorded)}: run python3 tools/glyph_corpus.py")}",
            hash == recorded && listed.SequenceEqual(now));
        return failures;
    }

    /// <summary>The SHA-256 (hex) of the characters in order, as UTF-8 with nothing between them.</summary>
    public static string CorpusHash(IEnumerable<string> characters)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Concat(characters)));
        return string.Concat(bytes.Select(b => b.ToString("x2")));
    }

    /// <summary>The characters above U+007F in <paramref name="text"/>, in order, a surrogate pair as one (as the corpus counts them).</summary>
    public static List<string> CorpusOf(string text)
    {
        var list = new List<string>();
        for (int i = 0; i < text.Length; i++)
        {
            int cp = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? char.ConvertToUtf32(text[i], text[++i]) : text[i];
            if (cp > 0x7F) list.Add(char.ConvertFromUtf32(cp));
        }
        return list;
    }

    /// <summary>Every character above U+007F (the byte-order mark aside) the sources hold, by code point, each as a string.</summary>
    public static List<string> CorpusCharacters(string root)
    {
        var found = new HashSet<int>();
        void Add(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                int cp = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? char.ConvertToUtf32(text[i], text[++i]) : text[i];
                if (cp > 0x7F && cp != 0xFEFF) found.Add(cp);
            }
        }
        var scripts = Path.Combine(root, "unity", "Assets", "Scripts");
        if (Directory.Exists(scripts))
            foreach (var file in Directory.EnumerateFiles(scripts, "*.cs", SearchOption.AllDirectories)) Add(CsLiterals(File.ReadAllText(file)));
        foreach (var top in new[] { "campaign", "personalities" })
        {
            var folder = Path.Combine(root, top);
            if (Directory.Exists(folder))
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) Add(File.ReadAllText(file));
        }
        return found.OrderBy(cp => cp).Select(char.ConvertFromUtf32).ToList();
    }

    /// <summary>The text inside a C# file's string and char literals (comments skipped; \\u and \\U escapes decoded).</summary>
    public static string CsLiterals(string src)
    {
        var o = new System.Text.StringBuilder();
        int i = 0, n = src.Length;
        while (i < n)
        {
            char c = src[i];
            if (c == '/' && i + 1 < n && src[i + 1] == '/') { int j = src.IndexOf('\n', i); i = j < 0 ? n : j; continue; }
            if (c == '/' && i + 1 < n && src[i + 1] == '*') { int j = src.IndexOf("*/", i + 2, StringComparison.Ordinal); i = j < 0 ? n : j + 2; continue; }
            bool verbatim = false;
            if ((c == '@' || c == '$') && i + 1 < n && (src[i + 1] == '"' || ((src[i + 1] == '@' || src[i + 1] == '$') && i + 2 < n && src[i + 2] == '"')))
            {
                int length = src[i + 1] == '"' ? 2 : 3;
                verbatim = src.Substring(i, length).Contains('@');
                i += length - 1;
                c = '"';
            }
            if (c == '"' || c == '\'')
            {
                char quote = c;
                i++;
                while (i < n)
                {
                    char d = src[i];
                    if (verbatim)
                    {
                        if (d == '"')
                        {
                            if (i + 1 < n && src[i + 1] == '"') { i += 2; continue; }
                            i++;
                            break;
                        }
                        o.Append(d);
                        i++;
                        continue;
                    }
                    if (d == '\\' && i + 1 < n)
                    {
                        char e = src[i + 1];
                        if (e == 'u' && i + 6 <= n && int.TryParse(src.Substring(i + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out int u))
                        { o.Append((char)u); i += 6; continue; }
                        if (e == 'U' && i + 10 <= n && int.TryParse(src.Substring(i + 2, 8), System.Globalization.NumberStyles.HexNumber, null, out int big))
                        { o.Append(char.ConvertFromUtf32(big)); i += 10; continue; }
                        i += 2;
                        continue;
                    }
                    if (d == quote || d == '\n') { i++; break; }
                    o.Append(d);
                    i++;
                }
                continue;
            }
            i++;
        }
        return o.ToString();
    }
}
