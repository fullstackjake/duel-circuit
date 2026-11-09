using System.Diagnostics;
using System.Globalization;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.DeckBuilding;
using Ygo.Engine.Interactive;
using Ygo.Engine.Presentation;

namespace Ygo.Harness;

/// <summary>
/// `--card-search-test [--write-baseline]`: the deck editor's card search (docs/dev/CARD-SEARCH-SPEC-2026-11-07.md section 10):
/// <c>does:</c> genres and their fallback, <c>@archetypes</c>, <c>name:</c>/<c>$</c>/<c>text:</c>/<c>pend:</c> and the three
/// modes, OR, close names, the ban list terms the editor takes out, <see cref="SeenCards"/> from hand-built core messages,
/// <see cref="SeenLog"/>'s file, and the timing of 50 sample queries (fails over 30 ms).
/// <para>
/// Old queries: src/Ygo.Harness/fixtures/card-search-baseline.txt holds, for a fixed list of queries the editor sent before
/// this feature (<see cref="OldQueries"/>), the size and a hash of the result SET the old search returned (made once from
/// the pre-feature CardSearch.cs, commit 911b14d), and of the Related lists in order. Squashed names deliberately reorder
/// some results, so sets are compared, not order. The fixture records a fingerprint of the card database: when
/// vendor/BabelCDB moves, the comparison is skipped with a note; <c>--write-baseline</c> then rewrites the fixture from the
/// current search (after checking the differences are expected).
/// </para>
/// Writes only under build/card-search-test.
/// </summary>
public static class CardSearchTest
{
    /// <summary>Queries the editor sent before the card search feature: typed words, its chips and pickers, DeckBuildingTest's.</summary>
    public static IEnumerable<string> OldQueries()
    {
        var typed = new[]
        {
            // DeckBuildingTest's search checks
            "dark magician", "blue-eyes", "dragon lv8 atk>=3000", "handtrap", "synchro tuner", "trap \"destroy all\"", "link2 cyberse",
            "dragon -effect lv8", "attr:light", "type:spellcaster lv7", "negateactivation trap", "\"dark magician\"", "\"ash blossom\"",
            "\"stardust dragon\"",
            // Attribute and Type words (two-way), roles and text tags
            "dark", "light", "earth", "water", "fire", "wind", "divine", "dragon", "warrior", "spellcaster", "wingedbeast", "winged beast",
            "searcher", "search", "draw", "specialsummon", "revive", "destroymonster", "destroyst", "negate", "banish", "wipe", "boardwipe",
            "removal", "burn", "gainlp", "discard", "negateeffect", "negateactivation",
            // kinds and numbers
            "monster", "monster -extra", "extra", "spell", "trap", "normal", "effect", "tuner", "fusion", "ritual", "synchro", "xyz", "link",
            "pendulum", "flip", "quickplay", "quick-play", "continuous", "field", "equip", "counter", "spell quickplay", "continuous trap",
            "pendulum scale5", "rank4", "xyz rank>=8", "atk<500 tuner", "def:2000", "atk:0", "atk>=3000 def<=1000", "level>=10", "scale>=8",
            "link>=3", "lv4 warrior", "normal lv4", "attr:dark type:fiend lv4", "lv1 tuner handtrap", "light lv4 handtrap", "searcher spell",
            // names and text
            "elemental hero", "hero", "sky striker", "-monster destroy", "pot of", "raigeki", "monster reborn", "\"special summon\" -monster",
            "blue eyes", "exodia", "dark magician girl", "magician", "kuriboh", "cyber dragon", "harpie", "ojama", "\"draw 2\"", "mst", "solemn",
            "x-saber", "graveyard", "gy", "a", "the", "\"\"", "-", "destroy", "special summon", "mirror force", "ash blossom", "maxx",
            "-effect -normal monster", "lv4 dragon", "lv4 spellcaster", "\"destroy all\"", "neos", "utopia", "number",
        };
        foreach (var q in typed) yield return q;
        // The editor's chips and pickers, alone and with a word, as RunSearch joined them.
        foreach (var chip in new[] { "handtrap", "searcher", "draw", "specialsummon", "revive", "destroymonster", "destroyst", "negate", "banish", "wipe" })
        {
            yield return "dragon " + chip;
            yield return chip + " monster -extra";
        }
        foreach (var (_, name) in DuelText.Attributes) yield return "attr:" + name.ToLowerInvariant();
        foreach (var (_, name) in DuelText.Races) yield return "type:" + name.ToLowerInvariant().Replace(" ", "").Replace("-", "");
        for (int lv = 1; lv <= 12; lv++) yield return "lv" + lv;
        yield return "handtrap searcher monster attr:light lv1";
        yield return "spell searcher";
        yield return "trap negate";
        yield return "extra type:dragon lv8";
        yield return "dark magician monster type:spellcaster lv7";
        yield return "hero handtrap searcher draw specialsummon revive destroymonster destroyst negate banish wipe";
    }

    /// <summary>Cards whose Related list is in the baseline (in order).</summary>
    public static readonly string[] RelatedNames = { "Dark Magician", "Blue-Eyes White Dragon", "Elemental HERO Neos", "Dante, Traveler of the Burning Abyss" };

    public static string BaselinePath(string root) => Path.Combine(root, "src", "Ygo.Harness", "fixtures", "card-search-baseline.txt");

    /// <summary>FNV-1a over the codes in the order given.</summary>
    public static ulong Hash(IEnumerable<uint> codes)
    {
        ulong h = 14695981039346656037UL;
        foreach (var c in codes)
            for (int i = 0; i < 4; i++)
            {
                h ^= (c >> (8 * i)) & 0xff;
                h *= 1099511628211UL;
            }
        return h;
    }

    private static ulong SetHash(IEnumerable<uint> codes) => Hash(codes.Distinct().OrderBy(c => c));

    /// <summary>A fingerprint of everything the search reads from the card database.</summary>
    public static ulong Fingerprint(ICardDataProvider cards)
    {
        ulong h = 14695981039346656037UL;
        void Add(string s) { foreach (var ch in s) { h ^= ch; h *= 1099511628211UL; } h ^= 0xff; h *= 1099511628211UL; }
        foreach (var c in cards.All.OrderBy(c => c.Code))
            Add(string.Join(",", c.Code, c.Alias, c.Type, c.Level, c.Attribute, c.Race, c.Attack, c.Defense, c.LScale, c.Category,
                string.Join("/", c.Setcodes)) + "|" + c.Name + "|" + c.Text);
        return h;
    }

    /// <summary>Writes the baseline fixture from a search (the old one when it was made, the current one with --write-baseline).</summary>
    public static void WriteBaseline(string path, ICardDataProvider cards, Func<string, List<CardInfo>> find, Func<CardInfo, List<CardInfo>> related, string madeFrom)
    {
        var lines = new List<string>
        {
            "# --card-search-test: the result sets the editor's old queries returned (count and FNV-1a hash of the sorted codes),",
            "# and the Related lists in order. Made from " + madeFrom + ". Columns: kind, count, hash, query (or card name).",
            $"db\t{Fingerprint(cards):x16}",
        };
        foreach (var q in OldQueries().Distinct())
        {
            var r = find(q);
            lines.Add($"find\t{r.Count}\t{SetHash(r.Select(c => c.Code)):x16}\t{q}");
        }
        foreach (var name in RelatedNames)
        {
            var card = cards.All.First(c => c.Name == name && c.Alias == 0);
            var r = related(card);
            lines.Add($"related\t{r.Count}\t{Hash(r.Select(c => c.Code)):x16}\t{name}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
    }

    public static int Run(ICardDataProvider cards, string root, bool writeBaseline)
    {
        int fails = 0, oks = 0, skipped = 0;
        void Check(string what, bool ok)
        {
            if (ok) oks++; else fails++;
            Console.WriteLine($"card search test: {(ok ? "ok  " : "FAIL")} {what}");
        }
        void Note(string what) => Console.WriteLine($"card search test: note: {what}");

        var folder = Path.Combine(root, "build", "card-search-test");
        Directory.CreateDirectory(folder);
        var build = Stopwatch.StartNew();
        var search = new CardSearch(cards);
        var archetypes = Archetypes.Load(Path.Combine(root, "vendor", "CardScripts", "archetype_setcode_constants.lua"));
        search.SetArchetypes(archetypes);
        Note($"{search.CardCount} searchable cards, {archetypes.Count} archetype names; built and indexed in {build.ElapsedMilliseconds} ms");

        const int All = 100000;
        List<CardInfo> F(string q, CardSearch.SearchMode mode = CardSearch.SearchMode.NameAndText) => search.Find(q, mode, All);
        HashSet<uint> Set(string q, CardSearch.SearchMode mode = CardSearch.SearchMode.NameAndText) => new HashSet<uint>(F(q, mode).Select(c => c.Code));
        static bool Has(List<CardInfo> r, string name) => r.Any(c => c.Name == name);
        CardInfo Card(string name) => cards.All.First(c => c.Name == name && c.Alias == 0);
        string Top(List<CardInfo> r, int n = 3) => string.Join(", ", r.Take(n).Select(c => c.Name));
        var everything = F("");
        var searchable = new HashSet<uint>(everything.Select(c => c.Code));
        Check($"an empty query lists every searchable card ({everything.Count} of {search.CardCount})", everything.Count == search.CardCount);

        // ---- 1. Old queries: the same result sets as before the feature ----
        var baseline = BaselinePath(root);
        if (writeBaseline)
        {
            WriteBaseline(baseline, cards, q => search.Find(q, All), c => search.Related(c, 500), "the current CardSearch (--write-baseline)");
            Note($"wrote {baseline} from the current search");
        }
        if (!File.Exists(baseline))
        {
            Check($"the old-query baseline {baseline} is there", false);
        }
        else
        {
            var lines = File.ReadAllLines(baseline).Where(l => l.Length > 0 && !l.StartsWith("#")).Select(l => l.Split('\t')).ToList();
            var db = lines.FirstOrDefault(l => l[0] == "db")?[1];
            if (db != Fingerprint(cards).ToString("x16"))
            {
                skipped++;
                Note($"the card database changed since the baseline was made (fingerprint {db} vs {Fingerprint(cards):x16}), so the old-query " +
                     "comparison is skipped; check the differences, then rewrite it with --card-search-test --write-baseline");
            }
            else
            {
                int same = 0;
                var differ = new List<string>();
                foreach (var l in lines.Where(l => l[0] == "find" || l[0] == "related"))
                {
                    int count = int.Parse(l[1], CultureInfo.InvariantCulture);
                    string hash = l[2], q = l[3];
                    List<CardInfo> r;
                    string got;
                    if (l[0] == "find")
                    {
                        r = F(q);
                        got = SetHash(r.Select(c => c.Code)).ToString("x16");
                    }
                    else
                    {
                        r = search.Related(Card(q), 500);
                        got = Hash(r.Select(c => c.Code)).ToString("x16");
                    }
                    if (r.Count == count && got == hash) same++;
                    else differ.Add($"{l[0]} \"{q}\": {count} before, {r.Count} now");
                }
                foreach (var d in differ.Take(10)) Console.WriteLine($"card search test:      {d}");
                Check($"the old queries return the sets they did before ({same} of {same + differ.Count} the same; Related lists in the same order)",
                    differ.Count == 0 && same > 100);
            }
        }
        // The two deliberate changes: e-hero and d-hero found nothing; now they show close names.
        foreach (var (q, expect) in new[] { ("e-hero", "Elemental HERO"), ("d-hero", "Destiny HERO") })
        {
            var r = F(q);
            Check($"\"{q}\" (nothing before) now shows close names, {expect} among them ({r.Count}: {Top(r)})",
                search.LastUsedCloseNames && r.Any(c => c.Name.StartsWith(expect)));
        }
        {
            var dm = Card("Dark Magician");
            var r = F("dark magician");
            Check($"\"dark magician\" still ranks Dark Magician first ({Top(r)})", r.Count > 0 && r[0].Code == dm.Code);
        }

        // ---- 2. does: (section 1) ----
        Check($"{CardTags.Genres.Count} genres, each in one of the {CardTags.Groups.Count} groups once, with tokens GenreOf finds",
            CardTags.Genres.Count == 30 && CardTags.Groups.Count == 6
            && CardTags.Groups.SelectMany(CardTags.GenresIn).Select(g => g.Token).OrderBy(t => t).SequenceEqual(CardTags.Genres.Select(g => g.Token).OrderBy(t => t))
            && CardTags.Genres.All(g => CardTags.GenreOf(g.Token) == g && CardTags.GenreOf(g.Token.ToUpperInvariant()) == g && g.Meaning.Length > 0));
        Check("GenreOf reads labels with spaces and slashes (\"Destroy S/T\", \"Gain LP\") and nothing else",
            CardTags.GenreOf("Destroy S/T")?.Token == "destroyst" && CardTags.GenreOf("Gain LP")?.Token == "gainlp" && CardTags.GenreOf("nosuch") == null && CardTags.GenreOf("") == null);
        var empty = CardTags.Genres.Where(g => F("does:" + g.Token).Count == 0).Select(g => g.Token).ToList();
        Check($"every does: token finds cards{(empty.Count > 0 ? " (empty: " + string.Join(", ", empty) + ")" : "")}", empty.Count == 0);
        {
            var r = F("does:search");
            Check($"does:search finds Sangan and not Pot of Desires ({r.Count})", Has(r, "Sangan") && !Has(r, "Pot of Desires"));
            var pod = Card("Pot of Desires");
            Check("Pot of Desires is found by does:draw and does:banish",
                F("does:draw").Any(c => c.Code == pod.Code) && F("does:banish").Any(c => c.Code == pod.Code));
            r = F("does:banish lv4");
            Check($"does:banish lv4 only finds Level 4 monsters that banish ({r.Count})",
                r.Count > 0 && r.All(c => (c.Type & CardType.Monster) != 0 && c.Level == 4 && (c.Type & (CardType.Xyz | CardType.Link)) == 0)
                && new HashSet<uint>(r.Select(c => c.Code)).SetEquals(Set("does:banish").Intersect(Set("lv4"))));
            var searchGenre = CardTags.GenreOf("search")!;
            var fallback = everything.Where(c => c.Category == 0 && (c.Type & CardType.Effect) != 0 && (CardTraits.Of(c) & Trait.Search) != 0).ToList();
            var found = Set("does:search");
            Check($"Effect Monsters with no categories but the Search role are found by does:search through the fallback ({fallback.Count}, e.g. {Top(fallback, 2)})",
                fallback.Count > 0 && fallback.All(c => found.Contains(c.Code)));
            var precise = everything.Where(c => c.Category != 0 && (c.Category & searchGenre.Bits) == 0 && (CardTraits.Of(c) & Trait.Search) != 0).ToList();
            Check($"cards with categories but no Search category aren't found by their Search role ({precise.Count}, e.g. {Top(precise, 2)})",
                precise.All(c => !found.Contains(c.Code)));
            Check("GenresOf shows categories only: an uncategorized searcher has no Search chip",
                fallback.All(c => CardTags.GenresOf(c).All(t => t.Label != "Search")));
            var banishFallback = everything.Where(c => c.Category == 0 && c.Text.ToLowerInvariant().Contains("banish")).ToList();
            var banished = Set("does:banish");
            Check($"the text fallback: uncategorized cards that say \"banish\" are found by does:banish ({banishFallback.Count})",
                banishFallback.Count > 0 && banishFallback.All(c => banished.Contains(c.Code)));
            r = F("does:win");
            var exodia = Card("Exodia the Forbidden One");
            Check($"does:win finds Exodia the Forbidden One ({r.Count}: {Top(r)}) and its card detail shows Wins the Duel",
                r.Any(c => c.Code == exodia.Code) && CardTags.GenresOf(exodia).Any(t => t.Label == "Wins the Duel")
                && r.All(c => c.Text.ToLowerInvariant().Contains("win the duel")));
            Check("does:Search and does:\"search\" read as does:search", Set("does:Search").SetEquals(found) && Set("does:\"search\"").SetEquals(found));
            r = F("does:nosuch");
            Check($"does:nosuch finds nothing and is reported (LastUnknownGenres: {string.Join(", ", search.LastUnknownGenres)})",
                r.Count == 0 && search.LastUnknownGenres.SequenceEqual(new[] { "nosuch" }));
            F("does:search");
            Check("the next search clears LastUnknownGenres", search.LastUnknownGenres.Count == 0);
            r = F("-does:search monster");
            Check($"-does:search excludes every searcher ({r.Count})", r.Count > 0 && r.All(c => !found.Contains(c.Code) && (c.Type & CardType.Monster) != 0));
        }

        // ---- 3. Archetypes (section 3) ----
        {
            var ba = archetypes.All.First(a => a.name == "Burning Abyss").code;
            var a1 = Set("@\"burning abyss\"");
            var members = everything.Where(c => Archetypes.IsIn(c, ba)).Select(c => c.Code).ToHashSet();
            Check($"@\"burning abyss\", @burningabyss, @burning_abyss and @BurningAbyss return the same set: every member ({a1.Count})",
                a1.Count > 10 && a1.SetEquals(Set("@burningabyss")) && a1.SetEquals(Set("@burning_abyss")) && a1.SetEquals(Set("@BurningAbyss")) && a1.SetEquals(members));
            var r = F("@nosucharchetype");
            Check($"@nosucharchetype is empty and reported (LastUnknownArchetypes: {string.Join(", ", search.LastUnknownArchetypes)})",
                r.Count == 0 && search.LastUnknownArchetypes.SequenceEqual(new[] { "nosucharchetype" }));
            F("@burningabyss");
            Check("the next search clears LastUnknownArchetypes", search.LastUnknownArchetypes.Count == 0);
            Check("@burning (no archetype called that) falls back to the names that start with it", Set("@burning").IsSupersetOf(a1));
            var hero = archetypes.All.First(a => a.name == "Hero").code;
            var heroes = everything.Where(c => Archetypes.IsIn(c, hero)).Select(c => c.Code).ToHashSet();
            var h = Set("@hero");
            Check($"@hero is HERO exactly, sub-archetypes included (Elemental HERO...), not every name starting \"hero\" ({h.Count})",
                h.SetEquals(heroes) && F("@hero").Any(c => c.Name.StartsWith("Elemental HERO")) && F("@hero").Any(c => c.Name.StartsWith("Destiny HERO")));
            r = F("-@burningabyss lv3");
            Check($"-@burningabyss lv3 excludes the members ({r.Count})", r.Count > 0 && !r.Any(c => a1.Contains(c.Code)) && r.All(c => c.Level == 3));
            Check("CardSearch.ArchetypeTerm(\"Burning Abyss\") is @burningabyss, and it finds the members",
                CardSearch.ArchetypeTerm("Burning Abyss") == "@burningabyss" && Set(CardSearch.ArchetypeTerm("Burning Abyss")).SetEquals(a1));

            var neos = Card("Elemental HERO Neos");
            var of = search.ArchetypesOf(neos);
            var names = of.Select(a => a.name).ToList();
            Check($"ArchetypesOf(Elemental HERO Neos): {string.Join(", ", of.Select(a => $"{a.name} ({a.code:x})"))}",
                names.Contains("Hero") && names.Contains("Elemental Hero") && names.Contains("Neos") && of.All(a => Archetypes.IsIn(neos, a.code)));
            var dante = cards.All.First(c => c.Name.StartsWith("Dante, Traveler") && c.Alias == 0);
            Check($"ArchetypesOf(Dante): {string.Join(", ", search.ArchetypesOf(dante).Select(a => a.name))}", search.ArchetypesOf(dante).Any(a => a.name == "Burning Abyss"));

            var sugg = search.ArchetypeSuggestions("hero");
            Console.WriteLine($"card search test:      suggestions for \"hero\": {string.Join("; ", sugg.Select(s => $"{s.name} · {s.count}"))}");
            int firstContains = sugg.FindIndex(s => !CardSearch.SquashName(s.name).StartsWith("hero"));
            Check("ArchetypeSuggestions(\"hero\"): names that start with it first, then names that contain it, at most 8",
                sugg.Count == 8 && firstContains > 0 && sugg.Skip(firstContains).All(s => !CardSearch.SquashName(s.name).StartsWith("hero") && CardSearch.SquashName(s.name).Contains("hero"))
                && sugg.Take(firstContains).All(s => CardSearch.SquashName(s.name).StartsWith("hero")));
            var bur = search.ArchetypeSuggestions("Burning A", 3);
            Check($"ArchetypeSuggestions(\"Burning A\", 3) offers Burning Abyss with its member count ({string.Join("; ", bur.Select(s => $"{s.name} · {s.count}"))})",
                bur.Count is > 0 and <= 3 && bur[0].name == "Burning Abyss" && bur[0].count == a1.Count);
            Check("every suggestion's count is what its @term finds", search.ArchetypeSuggestions("dra", 8).Concat(sugg)
                .All(s => s.count == F(CardSearch.ArchetypeTerm(s.name)).Count || archetypes.All.Count(a => CardSearch.SquashName(a.name) == CardSearch.SquashName(s.name)) > 1));
            // Archetypes with fewer than 2 cards in the search set are never suggested.
            var single = archetypes.All.Where(a => archetypes.All.Count(b => CardSearch.SquashName(b.name) == CardSearch.SquashName(a.name)) == 1)
                .Select(a => (a.code, a.name, n: everything.Count(c => Archetypes.IsIn(c, a.code)))).Where(a => a.n < 2).ToList();
            var offered = single.Where(a => search.ArchetypeSuggestions(a.name, 50).Any(s => s.code == a.code)).Select(a => a.name).ToList();
            Check($"archetypes with fewer than 2 searchable members are never suggested ({single.Count} checked{(offered.Count > 0 ? "; offered: " + string.Join(", ", offered.Take(5)) : "")})",
                single.Count > 0 && offered.Count == 0);
            Check("ArchetypeSuggestions of \"\" or spaces is empty", search.ArchetypeSuggestions("").Count == 0 && search.ArchetypeSuggestions("  -").Count == 0);
            var fresh = new CardSearch(cards);
            Check("before SetArchetypes every @term names nothing", fresh.Find("@burningabyss").Count == 0 && fresh.LastUnknownArchetypes.Count == 1);
        }

        // ---- 4. Name only, text only, Pendulum Effects (sections 4 and 6.2) ----
        {
            var r = F("$dark");
            Check($"$dark finds no card that only has \"dark\" in its text ({r.Count})", r.Count > 0 && r.All(c => CardSearch.SquashName(c.Name).Contains("dark")));
            Check("name:dark is $dark, and $\"dark magician\" ranks Dark Magician first",
                Set("name:dark").SetEquals(Set("$dark")) && F("$\"dark magician\"")[0].Name == "Dark Magician");
            r = F("text:dragon");
            Check($"text:dragon finds no card that only has it in its name ({r.Count})", r.Count > 0 && r.All(c => c.Text.ToLowerInvariant().Contains("dragon")));
            Check("text:\"destroy all\" trap finds Mirror Force; -text:\"destroy all\" trap doesn't",
                Has(F("text:\"destroy all\" trap"), "Mirror Force") && !Has(F("-text:\"destroy all\" trap"), "Mirror Force") && F("-text:\"destroy all\" trap").Count > 0);
            r = F("-name:dragon dragon");
            Check($"-name:dragon dragon leaves the Dragons without it in their names ({r.Count})", r.Count > 0 && r.All(c => !c.Name.ToLowerInvariant().Contains("dragon")));
            Check("-$dark excludes the names with dark", F("-$dark lv4").All(c => !CardSearch.SquashName(c.Name).Contains("dark")) && F("-$dark lv4").Count > 0);

            r = F("pend:destroy");
            var oddEyes = Card("Odd-Eyes Pendulum Dragon");
            Check($"pend:destroy finds Odd-Eyes Pendulum Dragon and no non-Pendulum card ({r.Count})",
                r.Any(c => c.Code == oddEyes.Code) && r.All(c => (c.Type & CardType.Pendulum) != 0)
                && r.All(c => CardSearch.SplitPendulum(c.Text.ToLowerInvariant()).pendulum.Contains("destroy")));
            var monsterHalf = F("text:destroy pendulum").Where(c => !CardSearch.SplitPendulum(c.Text.ToLowerInvariant()).pendulum.Contains("destroy")).ToList();
            Check($"text: searches both of a Pendulum's effects, pend: only its Pendulum Effect ({monsterHalf.Count} say destroy only in the monster half, e.g. {Top(monsterHalf, 1)})",
                monsterHalf.Count > 0 && !monsterHalf.Any(c => Set("pend:destroy").Contains(c.Code)));
            var (pend, mon) = CardSearch.SplitPendulum(oddEyes.Text);
            Check("SplitPendulum splits Odd-Eyes Pendulum Dragon's text in two, without the headings or the dashes",
                pend.Length > 0 && mon.Length > 0 && !pend.Contains("Pendulum Effect ]") && !pend.Contains("----") && !mon.Contains("----")
                && !mon.StartsWith("[") && oddEyes.Text.Contains(pend) && oddEyes.Text.Contains(mon.Split('\n')[0]));
            Check("SplitPendulum leaves a card without that layout whole", CardSearch.SplitPendulum(Card("Sangan").Text) == ("", Card("Sangan").Text));

            // The three modes: plain words and phrases only; prefixed terms ignore them.
            const CardSearch.SearchMode Name = CardSearch.SearchMode.NameOnly, Text = CardSearch.SearchMode.TextOnly;
            var both = Set("dark");
            var nameOnly = Set("dark", Name);
            var textOnly = Set("dark", Text);
            Check($"the mode switch changes the results for \"dark\": Name + text {both.Count} (the Attribute or the name), Name {nameOnly.Count}, Text {textOnly.Count}",
                nameOnly.Count > 0 && textOnly.Count > 0 && both.IsSupersetOf(nameOnly) && !both.SetEquals(nameOnly) && !textOnly.SetEquals(nameOnly) && !textOnly.SetEquals(both));
            var magician = Set("magician");
            var nameOrText = Set("magician", Name);
            nameOrText.UnionWith(Set("magician", Text));
            Check($"for a plain word, Name + text is Name plus Text (\"magician\": {magician.Count} = {Set("magician", Name).Count} by name, {Set("magician", Text).Count} by text)",
                magician.SetEquals(nameOrText) && magician.Count > Set("magician", Name).Count && magician.Count > Set("magician", Text).Count);
            Check("Name mode: \"dark\" is $dark (the Attribute word loses its two-way meaning)", nameOnly.SetEquals(Set("$dark")));
            Check("Text mode: \"dragon\" is text:dragon", Set("dragon", Text).SetEquals(Set("text:dragon")));
            var darkOnly = everything.Where(c => (c.Attribute & 0x20) != 0 && (c.Type & CardType.Monster) != 0
                                                 && !CardSearch.SquashName(c.Name).Contains("dark") && !c.Text.ToLowerInvariant().Contains("dark")).Select(c => c.Code).ToList();
            Check($"in Name + text, \"dark\" still finds DARK monsters that don't say dark ({darkOnly.Count}); the other modes don't",
                darkOnly.Count > 0 && darkOnly.All(both.Contains) && !darkOnly.Any(nameOnly.Contains) && !darkOnly.Any(textOnly.Contains));
            Check("prefixed terms ignore the mode (name:, $, text:, pend:, @, does:, comparisons, kinds)",
                new[] { "name:dark", "$dark", "text:dragon", "pend:destroy", "@burningabyss", "does:search", "lv4 atk>=1800", "spell quickplay", "attr:dark", "type:dragon" }
                    .All(q => Set(q, Name).SetEquals(Set(q)) && Set(q, Text).SetEquals(Set(q))));
            Check("Name mode: a phrase looks in names only", F("\"dark magician\"", Name).All(c => c.Name.ToLowerInvariant().Contains("dark magician")) && F("\"dark magician\"", Name).Count > 0);
            Check("Text mode: a phrase looks in the text only", F("\"dark magician\"", Text).All(c => c.Text.ToLowerInvariant().Contains("dark magician")) && !Has(F("\"dark magician\"", Text), "Dark Magician"));
            Check("Find(query) is Find(query, NameAndText)", Set("dark magician girl").SetEquals(new HashSet<uint>(search.Find("dark magician girl", All).Select(c => c.Code))));
        }

        // ---- 5. OR (section 6.1) ----
        {
            var union = Set("lv4 dragon");
            union.UnionWith(Set("lv4 spellcaster"));
            Check($"lv4 dragon or spellcaster is the union of lv4 dragon and lv4 spellcaster ({union.Count})", Set("lv4 dragon or spellcaster").SetEquals(union));
            Check("OR, | and a|b without spaces read the same",
                Set("lv4 dragon OR spellcaster").SetEquals(union) && Set("lv4 dragon | spellcaster").SetEquals(union) && Set("lv4 dragon|spellcaster").SetEquals(union));
            var dragon = Set("dragon");
            Check("or dragon, dragon or, and | dragon are dragon", Set("or dragon").SetEquals(dragon) && Set("dragon or").SetEquals(dragon) && Set("| dragon").SetEquals(dragon) && Set("dragon |").SetEquals(dragon));
            Check("a doubled OR counts as one", Set("lv4 dragon or or spellcaster").SetEquals(union) && Set("lv4 dragon or | spellcaster").SetEquals(union) && Set("lv4 dragon||spellcaster").SetEquals(union));
            var three = Set("does:banish");
            three.UnionWith(Set("does:draw"));
            three.UnionWith(Set("@burningabyss"));
            Check("chains: does:banish or does:draw or @burningabyss", Set("does:banish or does:draw or @burningabyss").SetEquals(three));
            var neg = Set("-effect");
            neg.UnionWith(Set("atk>2500"));
            Check("an OR can hold -word and comparisons: -effect or atk>2500", Set("-effect or atk>2500").SetEquals(neg));
            var or = Set("\"or\"");
            var andOr = Set("dragon");
            andOr.IntersectWith(or);
            andOr.IntersectWith(Set("spellcaster"));
            Check($"\"or\" in quotes is a plain phrase: dragon \"or\" spellcaster is all three ANDed ({andOr.Count})",
                Set("dragon \"or\" spellcaster").SetEquals(andOr) && !andOr.SetEquals(Set("dragon or spellcaster")));
            var spell = Set("spell");
            spell.UnionWith(Set("trap"));
            Check("spell or trap is every Spell and Trap", Set("spell or trap").SetEquals(spell) && Set("spell|trap").SetEquals(spell));
        }

        // ---- 6. Close names and ranking (section 6.4) ----
        {
            var bewd = Card("Blue-Eyes White Dragon");
            foreach (var q in new[] { "blueeyes", "blue eyes", "bleu-eyes", "bleu eyes", "blueyes", "darkmagician" })
            {
                var r = F(q);
                var want = q == "darkmagician" ? Card("Dark Magician") : bewd;
                Check($"\"{q}\" finds {want.Name} ({r.Count}, close names: {search.LastUsedCloseNames}, at {r.FindIndex(c => c.Code == want.Code)}: {Top(r)})", r.Any(c => c.Code == want.Code));
            }
            F("blueeyes");
            Check("\"blueeyes\" matches names squashed, so it needs no close names", !search.LastUsedCloseNames);
            F("bleu-eyes");
            Check("\"bleu-eyes\" uses close names (no name has it)", search.LastUsedCloseNames);
            var top = F("blue eyes white");
            Check($"\"blue eyes white\" ranks Blue-Eyes White Dragon first ({Top(top)})", top.Count > 0 && top[0].Code == bewd.Code);
            Check("\"blue-eyes white dragon\" ranks it first too", F("blue-eyes white dragon")[0].Code == bewd.Code);
            F("dark magician");
            Check("no close names when names match", !search.LastUsedCloseNames);
            // Squashed, "Archer of the Rose" has "hero" (not where a word starts) and "Neo-Spacian" has "neos" (not where one ends):
            // neither is a name match (the old search didn't find them either).
            foreach (var word in new[] { "hero", "neos" })
            {
                var across = everything.Where(c => CardSearch.SquashName(c.Name).Contains(word) && !c.Name.ToLowerInvariant().Contains(word)
                                                   && !c.Text.ToLowerInvariant().Contains(word)).ToList();
                var found = Set(word);
                Check($"\"{word}\" doesn't match across name words ({across.Count} names have it squashed, e.g. {Top(across, 1)}; found {across.Count(c => found.Contains(c.Code))})",
                    across.Count > 0 && !across.Any(c => found.Contains(c.Code)));
            }
            // Close names are only for plain words.
            foreach (var (q, mode) in new[] { ("\"bleu eyes\"", CardSearch.SearchMode.NameAndText), ("name:bleu", CardSearch.SearchMode.NameAndText),
                         ("$bleueyes", CardSearch.SearchMode.NameAndText), ("text:bleu", CardSearch.SearchMode.NameAndText), ("@bleueyes", CardSearch.SearchMode.NameAndText),
                         ("-bleu lv4", CardSearch.SearchMode.NameAndText), ("bleu", CardSearch.SearchMode.TextOnly), ("bleu or eys", CardSearch.SearchMode.NameAndText) })
            {
                var r = F(q, mode);
                Check($"no close names for {q}{(mode != CardSearch.SearchMode.NameAndText ? " in " + mode : "")} ({r.Count})", !search.LastUsedCloseNames && !r.Any(c => c.Code == bewd.Code && !q.StartsWith("-")));
            }
            var mixed = F("bleu eyes lv8");
            Check($"close names still obey the other terms: \"bleu eyes lv8\" ({mixed.Count}: {Top(mixed)})",
                mixed.Any(c => c.Code == bewd.Code) && mixed.All(c => c.Level == 8 && (c.Type & CardType.Monster) != 0));
            var typo3 = F("dak magician");
            Check($"words of 3 letters or fewer must match exactly: \"dak magician\" has no Dark Magician ({typo3.Count})", !Has(typo3, "Dark Magician"));
            var swap = F("drgaon");
            Check($"swapping two neighbouring letters is one edit: \"drgaon\" (6 letters, 1 edit allowed) finds Blue-Eyes White Dragon ({swap.Count})",
                swap.Any(c => c.Code == bewd.Code) && search.LastUsedCloseNames);
            var two = F("dargn");
            Check($"two edits are too many for 4-6 letters: \"dargn\" doesn't find Blue-Eyes White Dragon ({two.Count})", !two.Any(c => c.Code == bewd.Code));
            var seven = F("draggoon white");
            Check($"words of 7+ letters allow 2 edits: \"draggoon white\" (two edits) finds it ({seven.Count})", seven.Any(c => c.Code == bewd.Code));
            // A query some card texts have but no name has: the text matches come first, close names after.
            bool Regular(CardInfo c, string[] words) => words.All(w => c.Name.ToLowerInvariant().Contains(w) || CardSearch.SquashName(c.Name).Contains(CardSearch.SquashName(w))
                                                                  || c.Text.ToLowerInvariant().Contains(w));
            string? ordered = null;
            foreach (var q in new[] { "summons", "destroys", "traps", "heroes" })
            {
                var r = F(q);
                if (!search.LastUsedCloseNames) continue;
                var words = q.Split(' ');
                int firstClose = r.FindIndex(c => !Regular(c, words));
                if (firstClose <= 0) continue;
                ordered = $"\"{q}\": {firstClose} matches, then {r.Count - firstClose} close names";
                Check($"close-name results come after the matches ({ordered})", r.Skip(firstClose).All(c => !Regular(c, words)));
                break;
            }
            if (ordered == null) Check("found a query with both text matches and close names to check their order", false);

            // Attribute and Type words filter: they never call up close names (review fix 1).
            var em = F("earth machine lv4");
            Check($"\"earth machine lv4\" adds no close names and finds only EARTH-or-named, Machine-or-named Level 4s ({em.Count}; Gil Garth {(Has(em, "Gil Garth") ? "in" : "out")})",
                !search.LastUsedCloseNames && search.LastCloseNameStart == -1 && !Has(em, "Gil Garth")
                && new HashSet<uint>(em.Select(c => c.Code)).SetEquals(Set("earth").Intersect(Set("machine")).Intersect(Set("lv4"))));
            var typo = F("dark magican");
            Check($"\"dark magican\" still finds Dark Magician through \"magican\", first: the close names with \"dark\" in the name lead ({Top(typo)}, close names {search.LastUsedCloseNames})",
                search.LastUsedCloseNames && typo.Count > 0 && typo[0].Name == "Dark Magician");
            Check("\"dragon -dragon\" adds no close names", F("dragon -dragon").Count == 0 && !search.LastUsedCloseNames);
            // A word some card's name has never calls up close names, whatever the other terms leave (review fix 2).
            foreach (var q in new[] { "raigeki monster", "destroy trap", "lv4 send", "destroy does:win", "pot spell lv4", "magician trap" })
            {
                var words = q.Split(' ').Where(w => !w.Contains(':') && !w.StartsWith("lv") && w != "monster" && w != "trap" && w != "spell").ToArray();
                bool aName = everything.Any(c => words.All(w => c.Name.ToLowerInvariant().Contains(w)));
                var r = F(q);
                var real = Set(q.Split(' ')[0]);
                foreach (var w in q.Split(' ').Skip(1)) real.IntersectWith(Set(w));
                Check($"\"{q}\": {r.Count} cards, close names {search.LastUsedCloseNames} (a name has {string.Join(" ", words)}: {aName})",
                    search.LastUsedCloseNames == !aName && (aName ? new HashSet<uint>(r.Select(c => c.Code)).SetEquals(real) : r.Take(search.LastCloseNameStart < 0 ? r.Count : search.LastCloseNameStart).All(c => real.Contains(c.Code))));
            }
            // Where the close names start (review fix 3).
            var bleu = F("bleu-eyes");
            Check($"\"bleu-eyes\" (no matches) starts its close names at 0 ({search.LastCloseNameStart} of {bleu.Count})", search.LastCloseNameStart == 0 && bleu.Count > 0);
            foreach (var q in new[] { "summons", "destroys", "traps", "heroes" })
            {
                var r = F(q);
                if (!search.LastUsedCloseNames) continue;
                int start = search.LastCloseNameStart;
                var matches = search.Find(q, CardSearch.SearchMode.NameAndText, "", All, closeNames: false).Select(c => c.Code).ToList();
                Check($"\"{q}\": LastCloseNameStart {start} of {r.Count} is the first close name (the matches before it, in order; none after)",
                    start > 0 && start < r.Count && r.Take(start).Select(c => c.Code).SequenceEqual(matches) && !r.Skip(start).Any(c => matches.Contains(c.Code)));
                break;
            }
            F("dark magician");
            Check("LastCloseNameStart is -1 when no close names were added", search.LastCloseNameStart == -1);
        }

        // ---- 7. Ban list terms (section 5): the editor takes them out ----
        {
            var rest = CardSearch.TakeBanFilters("dragon limit:1 \"is legal\" legal lv4", out var limit, out var legal);
            Check($"TakeBanFilters takes limit:1 and legal and keeps a quoted \"is legal\" (\"{rest}\", {limit}, {legal})",
                rest == "dragon \"is legal\" lv4" && limit == 1 && legal);
            rest = CardSearch.TakeBanFilters("limit:0 dragon LIMIT:2 \"legal\" text:\"limit:3\"", out limit, out legal);
            Check($"the last limit: wins, case doesn't matter, and quoted forms stay (\"{rest}\", {limit}, {legal})",
                rest == "dragon \"legal\" text:\"limit:3\"" && limit == 2 && !legal);
            rest = CardSearch.TakeBanFilters("dark magician", out limit, out legal);
            Check("no ban terms: the query comes back, limit null, legal false", rest == "dark magician" && limit == null && !legal);
            rest = CardSearch.TakeBanFilters("", out limit, out legal);
            Check("an empty query", rest == "" && limit == null && !legal);
            Check("Find ignores limit:N (the editor applies it)", Set("dragon limit:2").SetEquals(Set("dragon")) && Set("limit:0").Count == everything.Count);
            Check("Find reads \"legal\" as a word (the editor strips it with TakeBanFilters first)", Set("dragon legal").SetEquals(Set("dragon").Intersect(Set("legal"))));
            rest = CardSearch.TakeBanFilters("dragon -limit:0 -LEGAL limit:1 -limit:3 -limit:0 -\"legal\" -text:\"limit:2\"", out CardSearch.BanTerms terms);
            Check($"TakeBanFilters(out BanTerms) also takes -limit:N and -legal (\"{rest}\", limit {terms.Limit}, not {string.Join("/", terms.NotLimits)}, not legal {terms.NotLegal})",
                rest == "dragon -\"legal\" -text:\"limit:2\"" && terms.Limit == 1 && !terms.Legal && terms.NotLimits.SequenceEqual(new[] { 0, 3 }) && terms.NotLegal && terms.Any);
            rest = CardSearch.TakeBanFilters("dragon -limit:0 -legal", out limit, out legal);
            Check($"the old TakeBanFilters leaves the negations in the query as before (\"{rest}\")", rest == "dragon -limit:0 -legal" && limit == null && !legal);
            rest = CardSearch.TakeBanFilters("-limit:9 dark", out terms);
            Check("BanTerms.Any: -limit:9 names no limit, nothing to filter; none is none", rest == "dark" && !terms.Any
                && !(CardSearch.TakeBanFilters("dark", out terms) == "dark" && terms.Any) && CardSearch.TakeBanFilters("-legal", out terms) == "" && terms.Any);
        }

        // ---- 7b. The editor's Find(box, mode, filters) (review fixes, 2026-11-08) ----
        {
            const CardSearch.SearchMode Both = CardSearch.SearchMode.NameAndText, Name = CardSearch.SearchMode.NameOnly, Text = CardSearch.SearchMode.TextOnly;
            List<CardInfo> E(string box, CardSearch.SearchMode mode, string filters, bool closeNames = true) => search.Find(box, mode, filters, All, closeNames);
            HashSet<uint> ESet(string box, CardSearch.SearchMode mode, string filters) => new HashSet<uint>(E(box, mode, filters).Select(c => c.Code));

            // With no filters in Name + text, it is the one-box Find: the same cards in the same order, for every old query.
            var differ = OldQueries().Distinct().Where(q => !E(q, Both, "").Select(c => c.Code).SequenceEqual(F(q).Select(c => c.Code))).ToList();
            Check($"Find(q, NameAndText, \"\") is Find(q) for every old query, order included{(differ.Count > 0 ? ": " + string.Join(", ", differ.Take(5)) : "")}", differ.Count == 0);
            var split = OldQueries().Distinct().Where(q => q.Contains(' ') && !q.Contains('"') && !q.Contains(" or ")).Take(40)
                .Where(q => { var w = q.Split(' ', 2); return !ESet(w[0], Both, w[1]).SetEquals(Set(q)); }).ToList();
            Check($"a query split into box and filters finds the same set{(split.Count > 0 ? ": " + string.Join(", ", split.Take(5)) : "")}", split.Count == 0);

            // ORs never cross from the box to the filters.
            var dragonDark = Set("dragon attr:dark");
            Check($"box \"dragon or\" with the DARK picker is dragon AND DARK ({dragonDark.Count}), not dragon OR DARK",
                ESet("dragon or", Both, "attr:dark").SetEquals(dragonDark) && ESet("dragon |", Both, "attr:dark").SetEquals(dragonDark)
                && ESet("dragon", Both, "or attr:dark").SetEquals(dragonDark) && ESet("dragon", Both, "| attr:dark lv4").SetEquals(Set("dragon attr:dark lv4")));
            var union = Set("does:banish");
            union.UnionWith(Set("does:draw"));
            Check("an OR inside the filters still works (Match any: does:banish or does:draw)", ESet("", Both, "does:banish or does:draw").SetEquals(union)
                && ESet("monster", Both, "does:banish or does:draw").SetEquals(Set("monster").Intersect(union)));

            // Name only and Text only: bare keywords in the box are words.
            var gate = E("fusion gate", Name, "");
            Check($"Name only, box \"fusion gate\" finds Fusion Gate first ({Top(gate)}), every name with both words",
                gate.Count > 0 && gate[0].Name == "Fusion Gate" && gate.All(c => CardSearch.SquashName(c.Name).Contains("fusion") && CardSearch.SquashName(c.Name).Contains("gate")));
            Check("the one-box Find keeps reading fusion as a kind in Name only (unchanged)", Set("fusion gate", Name).SetEquals(Set("fusion").Intersect(Set("gate", Name))));
            foreach (var word in new[] { "trap", "effect", "draw", "banish", "dark", "dragon", "monster" })
            {
                var named = E(word, Name, "");
                bool close = search.LastUsedCloseNames;
                int start = search.LastCloseNameStart < 0 ? named.Count : search.LastCloseNameStart;
                var texts = E(word, Text, "");
                Check($"Name only \"{word}\": names with it ({start}{(close ? " + close names" : "")}); Text only: texts with it ({texts.Count})",
                    named.Take(start).All(c => CardSearch.SquashName(c.Name).Contains(word)) && texts.All(c => c.Text.ToLowerInvariant().Contains(word))
                    && (start > 0 || close) && texts.Count > 0);
            }
            var prefixed = new[] { "lv4", "atk>2000", "rank4", "attr:dark", "type:dragon", "does:search", "@burningabyss", "name:dark", "$dark", "text:dragon", "pend:destroy", "-lv4 atk>2000" }
                .Where(q => !(ESet(q, Name, "").SetEquals(Set(q)) && ESet(q, Text, "").SetEquals(Set(q)))).ToList();
            Check($"prefixed terms and comparisons in the box keep their meaning in every mode{(prefixed.Count > 0 ? ": " + string.Join(", ", prefixed) : "")}", prefixed.Count == 0);
            var filtered = ESet("dark", Name, "monster -extra handtrap fusion");
            Check($"the filters always read keywords as filters, in any mode (Name only \"dark\" + Main monster, Hand trap: {filtered.Count})",
                filtered.SetEquals(Set("dark", Name).Intersect(Set("monster -extra handtrap fusion"))) && ESet("dark", Text, "spell").SetEquals(Set("dark", Text).Intersect(Set("spell"))));

            // closeNames: false (the Card Trader).
            var noClose = E("bleu-eyes", Both, "", closeNames: false);
            Check($"closeNames: false adds no close names (\"bleu-eyes\": {noClose.Count})", noClose.Count == 0 && !search.LastUsedCloseNames && search.LastCloseNameStart == -1);
            Check("closeNames: false changes nothing when names match", E("dark magician", Both, "", closeNames: false).Select(c => c.Code).SequenceEqual(F("dark magician").Select(c => c.Code)));
        }

        // ---- 8. SeenCards from hand-built core messages (section 2) ----
        {
            static byte[] Message(byte id, params object[] parts)
            {
                var l = new List<byte> { id };
                foreach (var o in parts)
                    l.AddRange(o switch
                    {
                        byte b => new[] { b },
                        uint u => BitConverter.GetBytes(u),
                        ulong x => BitConverter.GetBytes(x),
                        _ => throw new ArgumentException(o.GetType().Name),
                    });
                return l.ToArray();
            }
            static object[] At(byte controller, uint location, uint sequence, uint position) => new object[] { controller, (byte)location, sequence, position };
            static byte[] Move(uint code, object[] from, object[] to) => Message(Msg.Move, new object[] { code }.Concat(from).Concat(to).Append(0u).ToArray());
            static byte[] Summon(byte kind, uint code, object[] at) => Message(kind, new object[] { code }.Concat(at).ToArray());
            static byte[] Activate(uint code, object[] at) =>
                Message(Msg.Chaining, new object[] { code }.Concat(at).Concat(new object[] { (byte)at[0], (byte)at[1], 0u, 0UL, 1u }).ToArray());
            static byte[] PosChange(uint code, byte controller, uint location, uint was, uint now) =>
                Message(Msg.PosChange, code, controller, (byte)location, (byte)0, (byte)was, (byte)now);
            const uint FaceUp = Position.FaceUpAttack, FaceDown = Position.FaceDownDefense, SetSpell = Position.FaceDownAttack;

            uint sangan = Card("Sangan").Code, mirror = Card("Mirror Force").Code, raigeki = Card("Raigeki").Code, ash = Card("Ash Blossom & Joyous Spring").Code;
            uint bewd = Card("Blue-Eyes White Dragon").Code, mst = Card("Mystical Space Typhoon").Code, jinzo = Card("Jinzo").Code, potg = Card("Pot of Greed").Code;
            uint token = cards.All.First(c => (c.Type & CardType.Token) != 0).Code;
            var alt = cards.All.FirstOrDefault(c => c.Alias == bewd);
            var dm = Card("Dark Magician");
            var dmAlt = cards.All.FirstOrDefault(c => c.Alias == dm.Code);
            var seen = new SeenCards(cards, 0);
            seen.Observe(Move(sangan, At(1, Location.Hand, 0, 0), At(1, Location.MZone, 0, FaceUp)));     // the opponent summons Sangan: the move...
            seen.Observe(Summon(Msg.Summoning, sangan, At(1, Location.MZone, 0, FaceUp)));               // ...then the summon (still one play)
            seen.Observe(Move(mirror, At(1, Location.Hand, 0, 0), At(1, Location.SZone, 1, SetSpell)));    // the opponent Sets Mirror Force: unknown
            seen.Observe(Move(jinzo, At(1, Location.Hand, 1, 0), At(1, Location.MZone, 1, FaceDown)));     // the opponent Sets Jinzo: unknown
            seen.Observe(Move(raigeki, At(0, Location.Hand, 0, 0), At(0, Location.SZone, 0, FaceUp)));    // our Raigeki...
            seen.Observe(Activate(raigeki, At(0, Location.SZone, 0, FaceUp)));                            // ...activated by us
            seen.Observe(Move(raigeki, At(0, Location.SZone, 0, FaceUp), At(0, Location.Grave, 0, 0)));   // ...to our GY
            seen.Observe(Summon(Msg.SpSummoning, token, At(1, Location.MZone, 2, FaceUp)));              // the opponent's token
            seen.Observe(Move(token, At(1, 0, 0, 0), At(1, Location.MZone, 2, FaceUp)));
            seen.Observe(Activate(ash, At(1, Location.Hand, 0, 0)));                                      // the opponent activates Ash from the hand
            seen.Observe(Move(ash, At(1, Location.Hand, 0, 0), At(1, Location.Grave, 0, 0)));
            if (alt != null) seen.Observe(Summon(Msg.SpSummoning, alt.Code, At(1, Location.MZone, 3, FaceUp))); // an alternate artwork
            seen.Observe(Move(sangan, At(1, Location.MZone, 0, FaceUp), At(1, Location.Grave, 0, 0)));      // Sangan to the GY: seen again, not played
            seen.Observe(Activate(sangan, At(1, Location.Grave, 0, 0)));                                  // Sangan's effect: a second play
            seen.Observe(Move(potg, At(1, Location.Deck, 0, 0), At(1, Location.Hand, 0, 0)));             // searched to the hand: unknown
            seen.Observe(Move(mst, At(1, Location.Deck, 0, 0), At(1, Location.Removed, 0, Position.FaceDownAttack))); // banished face-down: unknown
            seen.Observe(Move(dm.Code, At(1, Location.Grave, 0, 0), At(1, Location.Deck, 0, 0)));         // shuffled into the Deck: unknown
            seen.Observe(Move(0, At(1, Location.Deck, 0, 0), At(1, Location.Grave, 0, 0)));               // a code the core hid
            var expect = new List<uint> { sangan, ash };
            if (alt != null) expect.Add(bewd);
            Console.WriteLine($"card search test:      seen: {string.Join(", ", seen.Cards.Select(c => $"{cards.Get(c.code)?.Name} x{c.count}"))}");
            Check("SeenCards records the opponent's summons and activations, in first-seen order, alternate artworks as their base code",
                seen.Cards.Select(c => c.code).SequenceEqual(expect) && seen.Count == expect.Count);
            Check("it counts plays: Sangan summoned (its move and summon are one play) then activated is 2; Ash is 1",
                seen.Cards.First(c => c.code == sangan).count == 2 && seen.Cards.First(c => c.code == ash).count == 1);
            Check("our own cards, face-down Sets, tokens, cards going to the hand or Deck and face-down banishes aren't recorded",
                !seen.Cards.Any(c => c.code == raigeki || c.code == mirror || c.code == jinzo || c.code == token || c.code == potg || c.code == mst || c.code == dm.Code || c.code == 0));

            var seen2 = new SeenCards(cards, 0);
            seen2.Observe(Move(mirror, At(1, Location.SZone, 1, SetSpell), At(1, Location.Grave, 0, 0)));          // a Set card destroyed: now known
            seen2.Observe(PosChange(jinzo, 1, Location.MZone, FaceDown, Position.FaceUpDefense));                // flipped face-up where it is
            seen2.Observe(Move(mst, At(1, Location.Hand, 0, 0), At(1, Location.Removed, 0, Position.FaceUpAttack))); // banished face-up
            seen2.Observe(Move(raigeki, At(1, Location.Deck, 0, 0), At(1, Location.Overlay | Location.MZone, 0, 0))); // Xyz material
            if (dmAlt != null) seen2.Observe(Move(dmAlt.Code, At(1, Location.Deck, 0, 0), At(1, Location.Grave, 0, 0))); // milled alternate artwork
            seen2.Observe(Move(bewd, At(1, Location.MZone, 0, FaceUp), At(1, Location.Extra, 0, Position.FaceDownDefense))); // face-down into the Extra Deck: no
            seen2.Observe(PosChange(sangan, 1, Location.MZone, FaceUp, FaceDown));                               // turned face-down: no
            seen2.Observe(PosChange(potg, 0, Location.MZone, FaceDown, FaceUp));                                 // ours flipped: no
            seen2.Observe(Move(ash, At(0, Location.Hand, 0, 0), At(1, Location.MZone, 0, FaceUp)));              // ours taken to their side face-up: the opponent's now
            seen2.Observe(new byte[] { Msg.Move, 1, 2 });                                                        // garbled
            seen2.Observe(new byte[] { Msg.PosChange, 1 });
            seen2.Observe(Array.Empty<byte>());
            var expect2 = new List<uint> { mirror, jinzo, mst, raigeki };
            if (dmAlt != null) expect2.Add(dm.Code);
            expect2.Add(ash);
            Console.WriteLine($"card search test:      seen: {string.Join(", ", seen2.Cards.Select(c => $"{cards.Get(c.code)?.Name} x{c.count}"))}");
            Check("a Set card sent to the GY, a flip face-up, a face-up banish, Xyz material and a milled card are known; garbled messages are ignored",
                seen2.Cards.Select(c => c.code).SequenceEqual(expect2) && seen2.Cards.All(c => c.count == 1));

            var seat1 = new SeenCards(cards, 1);
            seat1.Observe(Summon(Msg.Summoning, sangan, At(0, Location.MZone, 0, FaceUp)));
            seat1.Observe(Summon(Msg.Summoning, jinzo, At(1, Location.MZone, 0, FaceUp)));
            Check("from seat 1, seat 0 is the opponent", seat1.Cards.Select(c => c.code).SequenceEqual(new[] { sangan }));
            var raw = new SeenCards(null, 0);
            if (alt != null) raw.Observe(Summon(Msg.SpSummoning, alt.Code, At(1, Location.MZone, 0, FaceUp)));
            Check("without card data, codes are kept as they come", alt == null || raw.Cards.Select(c => c.code).SequenceEqual(new[] { alt.Code }));
        }

        // ---- 9. SeenLog: seen.txt ----
        {
            var path = Path.Combine(folder, "decks", "seen.txt");
            if (Directory.Exists(Path.GetDirectoryName(path)!)) Directory.Delete(Path.GetDirectoryName(path)!, true);
            var log = SeenLog.Read(path);
            Check("a missing file reads empty", log.Duels.Count == 0 && log.Viewed.Count == 0 && log.Codes().Count == 0 && log.LastSeenIn(1) == null);
            var start = new DateTime(2026, 11, 7, 10, 0, 0);
            for (int i = 0; i < 25; i++)
                log.AddDuel(start.AddMinutes(i), $"Rival\t{i}\n", i % 2 == 0 ? "campaign" : "free\tplay", new[] { (89631139u, 2), (1000u + (uint)i, 1), (89631139u, 1), (0u, 4), (2000u, 0) });
            Check($"AddDuel keeps the newest 20 ({log.Duels.Count}), newest first, merging repeated codes (2 + 1 = 3) and dropping code 0",
                log.Duels.Count == SeenLog.MaxDuels && log.Duels[0].When == start.AddMinutes(24) && log.Duels[19].When == start.AddMinutes(5)
                && log.Duels[0].Cards.SequenceEqual(new[] { (89631139u, 3), (1024u, 1), (2000u, 1) }));
            Check($"tabs and line breaks in names come out (\"{log.Duels[0].Opponent}\", \"{log.Duels[1].Mode}\")",
                log.Duels[0].Opponent == "Rival 24" && log.Duels[1].Mode == "free play" && log.Duels.All(d => !d.Opponent.Contains('\t') && !d.Mode.Contains('\t')));
            for (uint c = 1; c <= 45; c++) log.View(c);
            log.View(10);
            log.View(0);
            Check($"View keeps the last 40, newest first, no repeats; viewing one again moves it to the front ({string.Join(" ", log.Viewed.Take(4))}...)",
                log.Viewed.Count == SeenLog.MaxViewed && log.Viewed[0] == 10 && log.Viewed[1] == 45 && log.Viewed[39] == 6 && log.Viewed.Distinct().Count() == 40 && !log.Viewed.Contains(0));
            var codes = log.Codes();
            Check($"Codes: newest duel first, in the order seen, each once ({codes.Count})",
                codes.Count == 22 && codes[0] == 89631139 && codes[1] == 1024 && codes[2] == 2000 && codes[3] == 1023 && codes.Distinct().Count() == codes.Count);
            Check("Codes(duel) is that duel's", log.Codes(log.Duels[3]).SequenceEqual(new[] { 89631139u, 1021u, 2000u }));
            Check("LastSeenIn is the newest duel a card was seen in", log.LastSeenIn(89631139) == log.Duels[0] && log.LastSeenIn(1010)?.Opponent == "Rival 10" && log.LastSeenIn(1001) == null);

            log.Write();
            var text = File.ReadAllText(path);
            Check("Write makes the folder, leaves no temporary file, and writes the spec's lines",
                File.Exists(path) && !File.Exists(path + ".tmp") && text.StartsWith("duel 2026-11-07 10:24\tRival 24\tcampaign\n89631139 3\n1024 1\n")
                && text.Split('\n').Count(l => l.StartsWith("duel ")) == 20 && text.Contains("\nviewed 10 45 44 "));
            var back = SeenLog.Read(path);
            bool same = back.Duels.Count == log.Duels.Count && back.Duels.Zip(log.Duels).All(p => p.First.When == p.Second.When && p.First.Opponent == p.Second.Opponent
                && p.First.Mode == p.Second.Mode && p.First.Cards.SequenceEqual(p.Second.Cards)) && back.Viewed.SequenceEqual(log.Viewed);
            Check("it reads its own file back the same (duels, cards, counts, viewed)", same);
            back.AddDuel(start.AddDays(1), "Professor Mica", "campaign", new[] { (14558127u, 1) });
            back.Write();
            var again = SeenLog.Read(path);
            Check("writing over an old file works and still caps at 20", again.Duels.Count == 20 && again.Duels[0].Opponent == "Professor Mica" && again.Duels[19].When == start.AddMinutes(6));
            Check("writing over an old file leaves no temporary or backup file", !File.Exists(path + ".tmp")
                && Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName).SequenceEqual(new[] { "seen.txt" }));
            // A crash before the first move (only seen.txt.tmp there): Read falls back to it; with both there, the main file wins.
            File.Move(path, path + ".tmp");
            var fromTemp = SeenLog.Read(path);
            Check($"with only seen.txt.tmp there, Read reads it ({fromTemp.Duels.Count} duels)", fromTemp.Duels.Count == 20 && fromTemp.Duels[0].Opponent == "Professor Mica");
            fromTemp.Write();
            Check("writing then moves it into place", File.Exists(path) && !File.Exists(path + ".tmp") && SeenLog.Read(path).Duels.Count == 20);
            File.WriteAllText(path + ".tmp", "duel 2026-11-08 09:00\tHalf written\tcampaign\n1 1\n");
            Check("with both there, Read reads seen.txt, not a half-written temporary file", SeenLog.Read(path).Duels[0].Opponent == "Professor Mica");
            again.Write();
            Check("a stale temporary file doesn't stop a write (and goes)", !File.Exists(path + ".tmp") && SeenLog.Read(path).Duels.Count == 20);

            // The spec's example, a file over the caps, and broken ones.
            File.WriteAllText(path, "duel 2026-11-07 21:14\tProfessor Mica\tcampaign\r\n89631139 2\r\n14558127 1\r\n");
            var example = SeenLog.Read(path);
            Check("the spec's example (with CRLF) reads", example.Duels.Count == 1 && example.Duels[0].Opponent == "Professor Mica" && example.Duels[0].When == new DateTime(2026, 11, 7, 21, 14, 0)
                && example.Duels[0].Cards.SequenceEqual(new[] { (89631139u, 2), (14558127u, 1) }));
            var big = new List<string>();
            for (int i = 0; i < 30; i++) { big.Add($"duel 2026-11-0{1 + i / 10} 1{i % 10}:00\tR{i}\tcampaign"); big.Add($"{100 + i} 1"); }
            big.Add("viewed " + string.Join(" ", Enumerable.Range(1, 50).Select(n => (n % 45 + 1).ToString())));
            File.WriteAllText(path, string.Join("\n", big));
            var capped = SeenLog.Read(path);
            Check($"a file over the caps reads 20 duels and 40 viewed, no repeats ({capped.Duels.Count}, {capped.Viewed.Count})",
                capped.Duels.Count == 20 && capped.Duels[0].Opponent == "R0" && capped.Viewed.Count == 40 && capped.Viewed.Distinct().Count() == 40 && capped.Viewed[0] == 2);
            File.WriteAllBytes(path, new byte[] { 0, 255, 13, 10, 65, 66, 9, 200, 0xEF, 0xBB });
            var garbled = SeenLog.Read(path);
            Check("a garbled (binary) file reads empty", garbled.Duels.Count == 0 && garbled.Viewed.Count == 0);
            File.WriteAllText(path, "12345 2\nduel bad\nxx\n89631139 2\nduel 2026-11-07 21:14\tOnly two fields\nduel 2026-13-40 99:99\tX\tY\n1 1\n" +
                                    "duel 2026-11-07 21:15\tGood\tcampaign\n89631139 0\n-5 1\n0 3\n14558127 x\n55144522 1 extra\n44095762 2\n44095762 5\nviewed a b 7 7 -1 0 8\n");
            var half = SeenLog.Read(path);
            Check($"a half-broken file reads what it can (duels {half.Duels.Count}: {string.Join(", ", half.Duels.Select(d => $"{d.Opponent} {d.Cards.Count}"))}; viewed {string.Join(" ", half.Viewed)})",
                half.Duels.Count == 1 && half.Duels[0].Opponent == "Good" && half.Duels[0].Cards.SequenceEqual(new[] { (44095762u, 2) }) && half.Viewed.SequenceEqual(new uint[] { 7, 8 }));
            File.WriteAllText(path, "");
            Check("an empty file reads empty", SeenLog.Read(path).Duels.Count == 0);
            var dirPath = Path.Combine(folder, "decks", "a-folder.txt");
            Directory.CreateDirectory(dirPath);
            Check("a folder where the file should be reads empty", SeenLog.Read(dirPath).Duels.Count == 0);
        }

        // ---- 10. Speed (section 8): the slowest of 50 sample queries, every filter on ----
        {
            var timing = new[]
            {
                "dragon", "dark magician", "blue eyes white", "bleu-eyes", "lv4 dragon or spellcaster", "does:search", "does:banish lv4", "@burningabyss",
                "@\"blue eyes\" -extra", "name:dark text:destroy", "pend:destroy", "handtrap", "atk>=2000 def<=1000 lv4", "\"destroy all\"", "negate trap",
                "e", "a", "the", "magican", "drgon", "xyzz", "sky strikr", "elemental hero -fusion", "-monster", "spell or trap",
                "does:draw or does:search or does:banish", "warrior lv4 atk>=1800", "searcher spell", "monster -extra handtrap attr:light", "text:\"special summon\"",
                "$cyber", "cyber dragon", "harpie lady", "zzzzzzz qqqq", "destroy", "banish discard", "synchro tuner", "link2 cyberse", "@sky striker",
                "pendulum scale>=8", "fusion -effect", "blackwing or lightsworn or zombie", "abc def ghi jkl", "elemntal hreo neso", "-@hero -does:search -name:dragon monster",
                "bleu eyes handtrap searcher draw specialsummon revive destroymonster destroyst negate banish wipe monster attr:light type:dragon lv8",
                "does:negate or does:destroymonster or does:destroyst or does:banish or does:tohand or does:todeck or does:control @hero",
                "magician or dragon or warrior or spellcaster or fiend or zombie lv4 -effect",
                "\"if this card is\" text:\"special summon\" pend:\"pendulum\" -$dragon",
                "a b c d e f g h i j",
            };
            Check($"{timing.Length} sample queries", timing.Length == 50);
            foreach (var q in timing) F(q); // warm up (close names' word lists are built on first use)
            var times = new List<(string q, double ms, int n, bool close)>();
            foreach (var q in timing)
            {
                var runs = new double[3];
                int n = 0;
                for (int k = 0; k < runs.Length; k++)
                {
                    var t = Stopwatch.StartNew();
                    n = F(q).Count;
                    runs[k] = t.Elapsed.TotalMilliseconds;
                }
                times.Add((q, runs.OrderBy(x => x).ElementAt(1), n, search.LastUsedCloseNames));
            }
            foreach (var t in times.OrderByDescending(t => t.ms).Take(5))
                Console.WriteLine($"card search test:      {t.ms,6:F1} ms  \"{t.q}\" ({t.n} cards{(t.close ? ", close names" : "")})");
            var slowest = times.OrderByDescending(t => t.ms).First();
            Check($"the slowest of {times.Count} sample queries takes {slowest.ms:F1} ms (median of 3 runs; \"{slowest.q}\"), under 30 ms", slowest.ms < 30);
        }

        Console.WriteLine($"card search test: {oks} ok, {fails} FAIL{(skipped > 0 ? $", {skipped} skipped" : "")}");
        return fails == 0 ? 0 : 1;
    }
}
