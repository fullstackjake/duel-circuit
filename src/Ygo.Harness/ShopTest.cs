using System.Text.RegularExpressions;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--campaign-test`'s card shop checks (SHOP-REWORK-SPEC-2026-11-01, chunk C, checks 1-12; check 13 is the box loop in
/// `--arbitrage`): the featured shelves, the shelf following the campaign, nothing deleted, New arrivals, house packs,
/// tags, pack odds against seeded packs, every card pullable, booster boxes, Prize Pack odds, the shop's lines and the
/// save. All seeded. To show the featured checks bite, YGO_SHOPTEST_FEATURED=&lt;file&gt; reads that file instead of
/// campaign/featured.txt (a test hook only; nothing sets it).
/// </summary>
public static class ShopTest
{
    public static int Run(ICardDataProvider cards, string root, PrintingIndex printings, PackCatalog catalog, List<Personality> roster,
        Circuits circuits, Library library, GuestList guests, Deck starter)
    {
        int failures = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        string Few(IEnumerable<string> items, int n = 6)
        {
            var list = items.ToList();
            return list.Count == 0 ? "" : string.Join("; ", list.Take(n)) + (list.Count > n ? $"; and {list.Count - n} more" : "");
        }
        Console.WriteLine("\nThe card shop (SHOP-REWORK-SPEC):");
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        var deckCache = new Dictionary<string, Deck?>();
        Deck? LoadDeck(string name)
        {
            if (deckCache.TryGetValue(name, out var known)) return known;
            Deck? d = null;
            if (deckFiles.TryGetValue(name, out var f)) { d = Deck.LoadYdk(f); d.Name = name; }
            return deckCache[name] = d;
        }
        string Name(uint c) => cards.Get(c)?.Name ?? c.ToString();
        int YearOf(PackSet s) => int.TryParse(s.Year, out var y) ? y : 0;
        Profile Fresh(uint salt = 20261101)
        {
            var p = Profile.New(starter, cards, printings);
            p.Collection.Salt = salt;
            return p;
        }
        int ExpFor(int level) => Enumerable.Range(1, Math.Max(0, level - 1)).Sum(Leveling.ExpToNext);
        // Every match of the circuits before this one won: it's the current circuit (all of them: the post-game).
        void Reach(Profile p, int circuit)
        {
            p.MatchWins.Clear();
            for (int i = 0; i < Math.Min(circuit, circuits.All.Count); i++)
                for (int m = 0; m < circuits.All[i].Matches.Count; m++) p.MatchWins[Profile.MatchKey(i, m)] = 1;
        }
        // The house packs on sale, as the shop builds them (SHOP-REWORK-SPEC section 3).
        List<PackSet> HousePacks(Profile p)
        {
            int year = circuits.ShopYear(p);
            var packs = new List<PackSet>();
            foreach (var (_, _, m) in circuits.HouseMatches(p))
            {
                var deck = LoadDeck(m.Deck);
                if (deck == null) continue;
                var set = SelectionPacks.Build(m.Deck, "SP-" + m.Deck, deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, cards)), p, year, printings, cards,
                    SelectionPacks.PriceFor(m.Deck, circuits));
                if (set.CardCount > 0) packs.Add(set);
            }
            return packs;
        }
        var imports = ImportShelf.Load(Path.Combine(root, "campaign", "ocg-imports.txt"), cards).Packs;
        ShopShelves Shelves(Profile p, FeaturedShelves f) =>
            ShopShelves.Build(p, circuits, catalog, f, HousePacks(p), ImportShelf.IsOpen(p, circuits) ? imports : Array.Empty<PackSet>());

        // ---- 1. The featured file. ----
        var featuredPath = Environment.GetEnvironmentVariable("YGO_SHOPTEST_FEATURED") is { Length: > 0 } alt ? alt : Path.Combine(root, "campaign", FeaturedShelves.FileName);
        if (featuredPath != Path.Combine(root, "campaign", FeaturedShelves.FileName)) Console.WriteLine($"  (featured shelves read from {featuredPath}, YGO_SHOPTEST_FEATURED)");
        var featured = FeaturedShelves.Load(featuredPath);
        var shelfNames = featured.Names.ToList();
        var wantShelves = circuits.All.Select(c => c.Name).Append(FeaturedShelves.PostGame).ToList();
        var missingShelves = wantShelves.Where(n => !shelfNames.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
        var featuredProblems = featured.Problems(circuits, catalog, LoadDeck, cards);
        int featuredLines = shelfNames.Sum(n => featured.For(n).Count), soonLines = shelfNames.Sum(n => featured.For(n).Count(x => x.Soon));
        Check($"featured.txt parses: {shelfNames.Count} shelves (every circuit and the post-game), {featuredLines - soonLines} set: and {soonLines} soon: lines, "
              + $"{featuredProblems.Count} problems (catalog, era, soon window, decks, every deck served, 3-4 sets, at most 2 soon)"
              + (missingShelves.Count + featuredProblems.Count > 0 ? $": {Few(missingShelves.Select(n => $"no shelf {n}").Concat(featuredProblems))}" : ""),
            File.Exists(featuredPath) && missingShelves.Count == 0 && featuredProblems.Count == 0 && shelfNames.Count == wantShelves.Count);
        // The check bites: the same file with a broken line, a set above its era, and a deck left unserved.
        {
            var text = File.Exists(featuredPath) ? File.ReadAllText(featuredPath) : "";
            var lines = text.Split('\n').ToList();
            int shelfAt = lines.FindIndex(l => l.Trim().StartsWith("shelf:") && l.Contains(circuits.All[0].Name));
            int setAt = shelfAt < 0 ? -1 : lines.FindIndex(shelfAt, l => l.Trim().StartsWith("set:"));
            List<string> ProblemsWith(string replacement)
            {
                if (setAt < 0) return new List<string>();
                var copy = lines.ToList();
                copy[setAt] = replacement;
                return FeaturedShelves.Parse(string.Join("\n", copy)).Problems(circuits, catalog, LoadDeck, cards);
            }
            var deck0 = circuits.All[0].Matches[0].Deck;
            var late = catalog.Sets.Where(s => YearOf(s) > circuits.All.Select(c => c.Era).Where(e => e > circuits.All[0].Era).DefaultIfEmpty(9999).Min())
                .Select(s => s.Info.Code).FirstOrDefault() ?? "";
            var broken = ProblemsWith($"set: NOSUCHSET | {deck0} | a broken line");
            var aboveEra = ProblemsWith($"set: {late} | {deck0} | a set above its era");
            var notADeck = ProblemsWith($"set: {lines.ElementAtOrDefault(setAt)?.Split(':', '|').ElementAtOrDefault(1)?.Trim()} | no-such-deck | a deck that isn't the circuit's");
            Check($"the featured check bites: a set not in the catalog ({broken.FirstOrDefault()}), a set above its era ({aboveEra.FirstOrDefault(x => x.Contains("after the era"))}), "
                  + $"a deck that isn't the circuit's ({notADeck.FirstOrDefault(x => x.Contains("isn't a match deck"))})",
                broken.Any(x => x.Contains("NOSUCHSET") && x.Contains("not in the shop's catalog")) && aboveEra.Any(x => x.Contains(late) && x.Contains("after the era"))
                && notADeck.Any(x => x.Contains("no-such-deck") && x.Contains("isn't a match deck")));
        }

        // ---- 2 and 3. The shelf follows the campaign; nothing deleted. ----
        var shelfIssues = new List<string>();
        var lostIssues = new List<string>();
        int statesChecked = 0;
        for (int ci = 0; ci <= circuits.All.Count; ci++)
        {
            bool post = ci == circuits.All.Count;
            string shelf = post ? FeaturedShelves.PostGame : circuits.All[ci].Name;
            var picks = featured.For(shelf);
            // Two states per circuit: just arrived (level 1, the shop at the band's era), and levelled up to the next era
            // with arrivals recorded on the way (New arrivals in use, every soon row open).
            foreach (bool levelled in new[] { false, true })
            {
                statesChecked++;
                var p = Fresh();
                Reach(p, ci);
                int era = circuits.BandEra(p), next = circuits.NextEra(p);
                circuits.NoteBand(p);
                Arrivals.Visit(p, circuits.ShopYear(p));
                if (levelled)
                {
                    p.Exp = ExpFor(p.BandStartLevel[era] + (next - era));
                    Arrivals.Visit(p, circuits.ShopYear(p));
                }
                int shopYear = circuits.ShopYear(p);
                var s = Shelves(p, featured);
                string where = $"{shelf}{(levelled ? $" at {shopYear}" : "")}";
                if (s.Shelf != shelf) shelfIssues.Add($"{where}: the shelf is {s.Shelf}");
                var rowCodes = s.Featured.Select(r => r.Set!.Info.Code).ToList();
                var pickCodes = picks.Select(x => x.Code).Distinct().ToList();
                if (rowCodes.Count != pickCodes.Count || !rowCodes.ToHashSet().SetEquals(pickCodes)) shelfIssues.Add($"{where}: rows {string.Join(",", rowCodes)}, shelf {string.Join(",", pickCodes)}");
                bool seenLocked = false;
                foreach (var r in s.Featured)
                {
                    var pick = picks.FirstOrDefault(x => x.Code == r.Set!.Info.Code);
                    int year = YearOf(r.Set!);
                    if (r.Locked) seenLocked = true;
                    else if (seenLocked) shelfIssues.Add($"{where}: live {r.Set!.Info.Code} after a locked row");
                    if (!r.Locked && (year > shopYear || !s.OnSale.Contains(r.Set!))) shelfIssues.Add($"{where}: live {r.Set!.Info.Code} ({year}) isn't on sale");
                    if (r.Locked && s.OnSale.Contains(r.Set!)) shelfIssues.Add($"{where}: locked {r.Set!.Info.Code} is on sale");
                    if (pick != null && !pick.Soon && r.Locked) shelfIssues.Add($"{where}: set: {r.Set!.Info.Code} ({year}) is locked with the shop at {shopYear}");
                    if (r.Locked && r.OpensAtLevel != p.BandStartLevel[era] + year - era) shelfIssues.Add($"{where}: {r.Set!.Info.Code} opens at level {r.OpensAtLevel}");
                    if (r.Locked && year <= shopYear) shelfIssues.Add($"{where}: {r.Set!.Info.Code} ({year}) locked though out by {shopYear}");
                }
                var live = s.Featured.Where(r => !r.Locked).Select(r => r.Set!).ToList();
                if (live.SkipWhile(x => !PackCatalog.IsRaritySet(x)).Any(x => !PackCatalog.IsRaritySet(x))) shelfIssues.Add($"{where}: a rarity set before a set with Commons");
                if (levelled && s.Featured.Any(r => r.Locked)) shelfIssues.Add($"{where}: soon rows still locked at the next era");
                var d = s.DefaultPick;
                var firstCommons = live.FirstOrDefault(x => !PackCatalog.IsRaritySet(x));
                if (d == null || PackCatalog.IsRaritySet(d) || SelectionPacks.IsHousePack(d) || ImportShelf.IsImport(d) || (firstCommons != null && d != firstCommons))
                    shelfIssues.Add($"{where}: default pick {d?.Info.Code ?? "none"} (first live set with Commons: {firstCommons?.Info.Code ?? "none"})");
                // 3. The real sets across Featured, New arrivals and the back shelves are the old rule's (year <= ShopYear), each once.
                var oldRule = catalog.Sets.Where(x => YearOf(x) <= shopYear).ToHashSet();
                var real = live.Concat(s.NewArrivals.Select(r => r.Set!)).Concat(s.Back.Select(r => r.Set!)).ToList();
                if (real.Count != real.Distinct().Count()) lostIssues.Add($"{where}: {real.Count - real.Distinct().Count()} sets listed twice");
                if (!real.ToHashSet().SetEquals(oldRule)) lostIssues.Add($"{where}: {oldRule.Except(real).Count()} missing ({Few(oldRule.Except(real).Select(x => x.Info.Code), 3)}), {real.Except(oldRule).Count()} extra");
                if (s.OnSale.Count != s.OnSale.Distinct().Count()) lostIssues.Add($"{where}: OnSale lists a set twice");
                var open = s.Rows(backOpen: true, "").Where(r => r.Kind == ShelfRowKind.Set && r.Set != null && !r.Locked).Select(r => r.Set!).ToList();
                if (open.Count != s.OnSale.Count || !open.ToHashSet().SetEquals(s.OnSale)) lostIssues.Add($"{where}: the open list has {open.Count} rows for {s.OnSale.Count} sets on sale");
                var shut = s.Rows(backOpen: false, "");
                if (s.Back.Count > 0 && (shut.Count(r => r.Kind == ShelfRowKind.BackToggle) != 1 || shut.Any(r => r.Section == ShelfSection.BackCatalog && r.Kind == ShelfRowKind.Set)))
                    lostIssues.Add($"{where}: the back shelves aren't one collapsed row");
                if (levelled && shopYear > era && s.NewArrivals.Count == 0 && catalog.Sets.Any(x => YearOf(x) > era && YearOf(x) <= shopYear && !pickCodes.Contains(x.Info.Code)))
                    lostIssues.Add($"{where}: no New arrivals after levelling");
                if (ci == 0 && !levelled)
                    Console.WriteLine($"         {s.FeaturedHeader}: {string.Join(", ", s.Featured.Select(r => r.Locked ? $"{r.Set!.Info.Code} (level {r.OpensAtLevel})" : r.Set!.Info.Code))}; "
                                      + $"default {d?.Info.Code}; {s.House.Count} house packs; {s.BackToggle(false)}");
            }
        }
        Check($"the shelf follows the campaign ({statesChecked} states: each circuit and the post-game, as arrived and levelled to the next era): the featured rows are its shelf, "
              + $"every set: row is on sale, soon rows are locked until their level, the default pick is the first live set with Commons{(shelfIssues.Count > 0 ? ": " + Few(shelfIssues) : "")}",
            shelfIssues.Count == 0);
        Check($"nothing deleted: in every state the real sets across Featured, New arrivals and the back shelves are the old rule's (year <= the shop's year), each once, "
              + $"and the open list shows each set on sale once{(lostIssues.Count > 0 ? ": " + Few(lostIssues) : "")}", lostIssues.Count == 0);

        // ---- 4. New arrivals. ----
        {
            var v = Fresh();
            circuits.NoteBand(v);
            var first = Arrivals.Visit(v, circuits.ShopYear(v));
            bool firstOk = first.Count == 0 && v.Arrivals.Count == 0 && v.ShopYearSeen == circuits.All[0].Era && v.ShopVisits == 1;
            v.Exp = ExpFor(2);
            int year = circuits.ShopYear(v);
            var arrived = Arrivals.Visit(v, year);
            var sets = catalog.Sets.Where(s => YearOf(s) == year).ToList();
            var newOn = new List<bool> { sets.All(s => Arrivals.IsNew(v, s)) };
            var shown = Shelves(v, featured);
            bool listed = sets.All(s => shown.NewArrivals.Any(r => r.Set == s) || shown.Featured.Any(r => r.Set == s && r.New) || shown.Back.Any(r => r.Set == s && r.New))
                          && shown.NewArrivals.Count > 0 && catalog.Sets.Where(s => YearOf(s) < year).All(s => !Arrivals.IsNew(v, s));
            for (int k = 0; k < 3; k++) { Arrivals.Visit(v, year); newOn.Add(sets.All(s => Arrivals.IsNew(v, s))); }
            bool gone = Shelves(v, featured).NewArrivals.Count == 0 && v.Arrivals.Count == 0;
            // An old save (never visited) that has levelled already: its first visit records nothing.
            var old = Fresh();
            circuits.NoteBand(old);
            old.Exp = ExpFor(5);
            var oldFirst = Arrivals.Visit(old, circuits.ShopYear(old));
            bool oldOk = oldFirst.Count == 0 && old.Arrivals.Count == 0 && old.ShopYearSeen == circuits.ShopYear(old) && catalog.Sets.All(s => !Arrivals.IsNew(old, s));
            // New Game+ right after the 2026 sets arrived: the shop drops back and nothing on sale is new.
            var champ = Fresh();
            Reach(champ, circuits.All.Count);
            champ.ShopYearSeen = 2019;
            champ.ShopVisits = 40;
            champ.Exp = ExpFor(60);
            foreach (var era in circuits.All.Select(c => c.Era).Distinct()) champ.BandStartLevel[era] = 1;
            var lastArrivals = Arrivals.Visit(champ, circuits.ShopYear(champ));
            champ.NewGamePlus();
            var ngYears = new List<int>();
            bool ngNothingNew = true;
            for (int k = 0; k < 3; k++)
            {
                ngYears.AddRange(Arrivals.Visit(champ, circuits.ShopYear(champ)));
                var s = Shelves(champ, featured);
                ngNothingNew &= s.NewArrivals.Count == 0 && s.Featured.Concat(s.Back).All(r => !r.New);
            }
            Check($"New arrivals: the first visit records nothing; a level-up records {string.Join(", ", arrived)} ({sets.Count} sets, listed as new); new on visits "
                  + $"{string.Join("/", newOn.Select(b => b ? "yes" : "no"))} then gone; an old save's first visit records nothing; after New Game+ "
                  + $"(the {string.Join(", ", lastArrivals.TakeLast(1))} sets just in) nothing on sale is new",
                firstOk && arrived.SequenceEqual(new[] { year }) && sets.Count > 0 && listed && newOn.SequenceEqual(new[] { true, true, true, false }) && gone && oldOk
                && lastArrivals.Count > 0 && ngYears.Count == 0 && ngNothingNew && champ.ShopYearSeen >= 2026);
        }

        // ---- 5. House packs. ----
        {
            var h = Fresh();
            var decks = circuits.HouseMatches(h).Select(m => m.entry.Deck).ToList();
            var expected = circuits.All[0].Matches.Concat(circuits.All[1].Matches).Select(m => m.Deck).Distinct().ToList();
            // A deck leaves on its first win: that unlocks its recipe, even if it's met again later in the two circuits.
            var wonDeck = circuits.All[0].Matches[1].Deck;
            var w = Fresh();
            w.MatchWins[Profile.MatchKey(0, 1)] = 1;
            bool keptWhileUnwon = circuits.HouseMatches(Fresh()).Any(m => m.entry.Deck == wonDeck) && circuits.HouseMatches(w).All(m => m.entry.Deck != wonDeck);
            for (int ci = 0; ci < 2; ci++)
                for (int mi = 0; mi < circuits.All[ci].Matches.Count; mi++)
                    if (circuits.All[ci].Matches[mi].Deck == wonDeck) w.MatchWins[Profile.MatchKey(ci, mi)] = 1;
            bool left = circuits.HouseMatches(w).All(m => m.entry.Deck != wonDeck);
            var all = Fresh();
            Reach(all, circuits.All.Count);
            bool noneAfter = circuits.HouseMatches(all).Count == 0 && circuits.SelectionPackCircuits(all).Count == 0;
            // In every state: the decks of the current and next circuit's matches not won and with no recipe unlocked, each once; named,
            // dated and priced as the spec says.
            var houseIssues = new List<string>();
            var both = new List<string>();
            for (int ci = 0; ci < circuits.All.Count; ci++)
            {
                var p = Fresh();
                Reach(p, ci);
                var matches = circuits.HouseMatches(p);
                var want = Enumerable.Range(ci, Math.Min(2, circuits.All.Count - ci)).SelectMany(c => circuits.All[c].Matches.Select((m, mi) => (c, mi, m)))
                    .Where(x => !p.HasWon(x.c, x.mi) && !RecipeBook.All(circuits).Any(r => r.Deck == x.m.Deck && RecipeBook.IsUnlocked(r, circuits, p)))
                    .Select(x => x.m.Deck).Distinct().ToList();
                if (!matches.Select(m => m.entry.Deck).SequenceEqual(want)) houseIssues.Add($"{circuits.All[ci].Name}: {string.Join(",", matches.Select(m => m.entry.Deck))}");
                foreach (var pack in HousePacks(p))
                {
                    var deck = pack.Info.Code.Substring(3);
                    if (!pack.Info.Name.StartsWith("House Pack:") || pack.Info.Date != "" || pack.Price != SelectionPacks.PriceFor(deck, circuits)
                        || pack.Sink != Ledger.SelectionPacks || !SelectionPacks.IsHousePack(pack))
                        houseIssues.Add($"{pack.Info.Code}: \"{pack.Info.Name}\" date \"{pack.Info.Date}\" {pack.Price} DP (want {SelectionPacks.PriceFor(deck, circuits)})");
                    if (RecipeBook.All(circuits).Any(r => r.Deck == deck && RecipeBook.IsUnlocked(r, circuits, p))) both.Add($"{deck} at {circuits.All[ci].Name}");
                }
            }
            var prices = HousePacks(h).Select(x => $"{x.Info.Name} {x.Price}").ToList();
            Check($"house packs: a fresh save gets {circuits.All[0].Name} and {circuits.All[1].Name}'s decks ({string.Join(", ", decks)}); {wonDeck} leaves on its first win; "
                  + $"all cleared gives none; in all {circuits.All.Count} circuits the decks, names, empty dates and SelectionPacks.PriceFor prices hold ({string.Join(", ", prices)})"
                  + (houseIssues.Count > 0 ? ": " + Few(houseIssues) : ""),
                decks.SequenceEqual(expected) && keptWhileUnwon && left && noneAfter && houseIssues.Count == 0);
            Check("house packs: never on sale while a recipe of their deck is unlocked (a beaten deck met again later)" + (both.Count > 0 ? ": " + Few(both, 5) : ""),
                both.Count == 0);

            // A house pack's rarities were printed by the shop's year (a 2010 shop has no 2013 Shatterfoil), for every deck
            // the circuits name, at every circuit's era.
            int YearOfSet(Printing pr) => printings.Sets.TryGetValue(PrintingIndex.SetPrefix(pr.SetCode), out var si) && si.Date.Length >= 4 && int.TryParse(si.Date.Substring(0, 4), out var y) ? y : 0;
            var lateRarities = new List<string>();
            int housesChecked = 0, dropped = 0;
            foreach (var era in circuits.All.Select(c => c.Era).Distinct())
                foreach (var dk in circuits.All.SelectMany(c => c.Matches).Select(m => m.Deck).Distinct())
                {
                    var deck = LoadDeck(dk);
                    if (deck == null) continue;
                    var codes = deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, cards)).ToList();
                    var pack = SelectionPacks.Build(dk, "SP-" + dk, codes, Fresh(), era, printings, cards);
                    var undated = SelectionPacks.Build(dk, "SP-" + dk, codes, null, era, printings, cards);
                    housesChecked++;
                    dropped += undated.Pool.Keys.Count(r => !pack.Pool.ContainsKey(r));
                    foreach (var (r, list) in pack.Pool)
                        foreach (var c in list)
                        {
                            var of = printings.Of(c, cards.Get(c)?.Alias ?? 0);
                            if (of.Any(pr => YearOfSet(pr) is > 0 and var y && y <= era) && !of.Any(pr => pr.Rarity == r && YearOfSet(pr) is > 0 and var y2 && y2 <= era))
                                lateRarities.Add($"{dk} at {era}: {Name(c)} as {Rarities.Name(r)}");
                        }
                }
            var classic2010 = LoadDeck("classic") is { } cl
                ? SelectionPacks.Build("classic", "SP-classic", cl.Main.Concat(cl.Extra).Select(c => Banlist.BaseCode(c, cards)), Fresh(), 2010, printings, cards) : null;
            Check($"house packs open at rarities printed by the shop's year: {housesChecked} packs (each deck at each circuit era) have no card at a rarity first printed later "
                  + $"({dropped} later rarities left out); House Pack: Classic in 2010 has {(classic2010 == null ? "no deck" : string.Join(", ", classic2010.Rarities.Select(Rarities.Name)))}"
                  + (lateRarities.Count > 0 ? ": " + Few(lateRarities) : ""),
                lateRarities.Count == 0 && classic2010 != null && !classic2010.Pool.ContainsKey(Rarity.ShatterfoilRare));
        }

        // ---- 6. Tags. ----
        var scouting = Scouting.Load(Path.Combine(root, "campaign", "scouting.txt"));
        var noPass = BanPasses.LoadNoPass(Path.Combine(root, "campaign", "no-pass.txt"), cards);
        var board = BountyBoard.Load(Path.Combine(root, "campaign", BountyBoard.FileName));
        var unlocks = new CardUnlocks(cards, printings, circuits, roster, noPass, LoadDeck, catalog, board, guests, library);
        var db1 = catalog.Find("DB1");
        {
            var t = Fresh();
            uint B(uint c) => Banlist.BaseCode(c, cards);
            // The opponents' decks, with a Side Deck card and a staple added to the first one (neither may count).
            var firstDeck = circuits.All[0].Matches[0].Deck;
            var houseCodes = circuits.HouseMatches(t).SelectMany(m => LoadDeck(m.entry.Deck)!.Main.Concat(LoadDeck(m.entry.Deck)!.Extra)).Select(B).ToHashSet();
            uint sideOnly = cards.All.Where(c => c.Alias == 0 && !houseCodes.Contains(c.Code) && !TagContext.Staples.Contains(c.Code)).Select(c => c.Code).OrderBy(c => c).First();
            uint staple = TagContext.Staples.First(c => cards.Get(c) != null);
            Deck? Opp(string name)
            {
                var d = LoadDeck(name);
                if (d == null || name != firstDeck) return d;
                var copy = d.Clone();
                copy.Name = name;
                copy.Side.Add(sideOnly);
                copy.Main.Add(staple);
                return copy;
            }
            // Your decks: the active one, a campaign deck (with a Side Deck card), and a loaner.
            var active = starter.Clone();
            active.Name = starter.Name;
            t.Deck = active.Name;
            var mine = Deck.LoadYdk(deckFiles["zombies"]);
            mine.Name = "my-zombies";
            var activeCodes = active.Main.Concat(active.Extra).Concat(active.Side).Select(B).ToHashSet();
            uint mySide = cards.All.Where(c => c.Alias == 0 && !activeCodes.Contains(c.Code) && !mine.Main.Concat(mine.Extra).Select(B).Contains(c.Code)).Select(c => c.Code).OrderByDescending(c => c).First();
            mine.Side.Add(mySide);
            var loaner = Deck.LoadYdk(deckFiles["goat-control"]);
            loaner.Name = Loaners.NameFor("goat-control");
            uint inActive = B(active.Main[0]);
            uint inMine = mine.Main.Select(B).First(c => !activeCodes.Contains(c));
            uint loanerOnly = loaner.Main.Select(B).First(c => !activeCodes.Contains(c) && !mine.Main.Concat(mine.Extra).Concat(mine.Side).Select(B).Contains(c));
            // The wishlist: one card wanted and missing, one wanted and owned already.
            uint wantedCard = db1!.Pool.Values.SelectMany(c => c).First(c => !t.Collection.Owns(c) && !houseCodes.Contains(c));
            uint ownedCard = t.Collection.All.Select(c => c.Code).First();
            t.Wishlist[wantedCard] = 2;
            t.Wishlist[ownedCard] = t.Collection.Count(ownedCard);
            t.WishlistFor = "warriors";
            string Title(string deck) => "the " + deck + " deck";
            var yours = new[] { active, mine, loaner };
            var ctx = TagContext.Build(t, circuits, yours, Opp, Title, cards, scouting);
            // Who plays a card, worked out here: the matches not won in the house circuits, in order, Main and Extra only, no staples.
            List<string> Players(uint code, Profile p) => circuits.SelectionPackCircuits(p).SelectMany(ci => circuits.All[ci].Matches.Select((m, mi) => (ci, mi, m)))
                .Where(x => !p.HasWon(x.ci, x.mi) && !TagContext.Staples.Contains(code) && Opp(x.m.Deck) is { } d && d.Main.Concat(d.Extra).Select(B).Contains(code))
                .Select(x => x.m.Character.Name).Distinct().ToList();
            var candidates = houseCodes.Append(sideOnly).Append(staple).OrderBy(c => c).ToList();
            var playIssues = candidates.Where(c => !PackTags.For(c, ctx).PlayedBy.SequenceEqual(Players(c, t))).Select(Name).ToList();
            // A won match's player drops out for the cards only that match's deck holds.
            var winner = Fresh();
            winner.MatchWins[Profile.MatchKey(0, 0)] = 1;
            var afterWin = TagContext.Build(winner, circuits, new[] { active }, Opp, Title, cards, scouting);
            playIssues.AddRange(candidates.Where(c => !PackTags.For(c, afterWin).PlayedBy.SequenceEqual(Players(c, winner))).Select(c => Name(c) + " after a win"));
            bool dropped = candidates.Any(c => Players(c, t).Count > Players(c, winner).Count);
            var shared = candidates.FirstOrDefault(c => Players(c, t).Count > 1 && c != wantedCard);
            var sharedTag = PackTags.For(shared, ctx);
            var wantedTag = PackTags.For(wantedCard, ctx);
            bool wantOk = wantedTag.Wanted && wantedTag.WantedFor == Title("warriors") && !PackTags.For(ownedCard, ctx).Wanted
                          && PackTags.Line(wantedTag) == $"Wanted for {Title("warriors")}";
            // The reveal line's priority: Wanted, then Plays this, then In your deck.
            var both = Fresh();
            both.Wishlist[shared] = 3;
            both.WishlistFor = "warriors";
            var bothCtx = TagContext.Build(both, circuits, yours, Opp, Title, cards, scouting);
            uint playedAndMine = candidates.FirstOrDefault(c => Players(c, t).Count == 1 && PackTags.For(c, ctx).InDeck.Length > 0 && !PackTags.For(c, ctx).Wanted);
            bool lineOk = shared != 0 && PackTags.Line(sharedTag) == $"{Players(shared, t)[0]} and {Players(shared, t).Count - 1} more play this"
                          && PackTags.Line(PackTags.For(shared, bothCtx)) == $"Wanted for {Title("warriors")}"
                          && (playedAndMine == 0 || PackTags.Line(PackTags.For(playedAndMine, ctx)) == $"{Players(playedAndMine, t)[0]} plays this")
                          && mine.Main.Select(B).Where(c => !activeCodes.Contains(c) && Players(c, t).Count == 0 && !t.Wanted().Contains(c))
                              .All(c => PackTags.Line(PackTags.For(c, ctx)) == $"In {Title(mine.Name)}");
            bool deckOk = PackTags.For(inActive, ctx).InDeck == Title(active.Name) && PackTags.For(inMine, ctx).InDeck == Title(mine.Name)
                          && PackTags.For(mySide, ctx).InDeck == Title(mine.Name) && PackTags.For(loanerOnly, ctx).InDeck == "";
            // Key cards: a house opponent's signature (or a scouting key) is a key card of that character; staples never are.
            var keyed = circuits.HouseMatches(t).Select(m => m.entry.Character).Where(c => c.Signature.Length > 0)
                .Select(c => (c, code: cards.All.Where(x => x.Name.Equals(c.Signature, StringComparison.OrdinalIgnoreCase)).Select(x => B(x.Code)).FirstOrDefault()))
                .FirstOrDefault(x => x.code != 0 && !TagContext.Staples.Contains(x.code));
            bool keyOk = !PackTags.For(staple, ctx).Key && (keyed.code == 0 || (PackTags.For(keyed.code, ctx).Key && PackTags.For(keyed.code, ctx).KeyOf.Length > 0));
            var (w, played, firstPlayer) = PackTags.Count(db1, ctx);
            var db1Codes = db1.Pool.Values.SelectMany(c => c).Distinct().ToList();
            bool countOk = w == db1Codes.Count(t.Wanted().Contains) && played == db1Codes.Count(c => Players(c, t).Count > 0)
                           && firstPlayer == (played == 0 ? "" : circuits.SelectionPackCircuits(t).SelectMany(ci => circuits.All[ci].Matches).Select(m => m.Character.Name)
                               .First(n => db1Codes.Any(c => Players(c, t).Contains(n))));
            Check($"tags: Wanted for the wishlist's deck (not a card owned already) and first on the reveal line; Plays this for {candidates.Count} cards as worked out here "
                  + $"(\"{PackTags.Line(sharedTag)}\" for {Name(shared)}; Side Decks, staples and won matches left out); In your deck for the active deck, a campaign deck and its Side, "
                  + $"not the loaner; {(keyed.code != 0 ? $"{keyed.c.Name}'s {Name(keyed.code)} is a key card; " : "")}DB1's row: {w} wanted, {firstPlayer} plays {played}"
                  + (playIssues.Count > 0 ? ": " + Few(playIssues) : ""),
                wantOk && playIssues.Count == 0 && dropped && lineOk && deckOk && keyOk && countOk);
            // Set progress is CardUnlocks.Progress, and CheckSets completes the set exactly when it reads n/n.
            var progressIssues = catalog.Sets.Where(s => unlocks.Progress(t, s.Info.Code) != (s.Pool.Values.SelectMany(c => c).Distinct().Count(t.Collection.Owns), s.CardCount))
                .Select(s => s.Info.Code).ToList();
            bool nonReal = unlocks.Progress(t, "SP-" + firstDeck) == (0, 0) && (imports.Count == 0 || unlocks.Progress(t, imports[0].Info.Code) == (0, 0));
            var small = catalog.Sets.Where(s => s.CardCount <= 60 && s.Pool.Values.SelectMany(c => c).All(c => !noPass.Contains(c))).OrderBy(s => s.CardCount).ThenBy(s => s.Info.Code).First();
            var c2 = Fresh();
            bool stepsOk = true;
            uint framedWant = unlocks.FramedCard(small.Info.Code);
            uint framedGot = 0;
            foreach (var code in small.Pool.Values.SelectMany(c => c).Distinct().OrderBy(c => c))
            {
                if (!c2.Collection.Owns(code)) c2.Collection.Add(code, Rarity.Common);
                var (owned, total) = unlocks.Progress(c2, small.Info.Code);
                var grant = unlocks.CheckSets(c2).FirstOrDefault(g => g.SetCode == small.Info.Code);
                stepsOk &= (grant != null) == (owned == total);
                if (grant != null) framedGot = grant.Code;
            }
            Check($"set progress is CardUnlocks.Progress for all {catalog.Sets.Count} sets ((0, 0) for a house pack or an import); {small.Info.Name} completes in CheckSets exactly at "
                  + $"{small.CardCount}/{small.CardCount}, framing {Name(framedGot)} (FramedCard said {Name(framedWant)}){(progressIssues.Count > 0 ? ": " + Few(progressIssues) : "")}",
                progressIssues.Count == 0 && nonReal && stepsOk && framedGot != 0 && framedGot == framedWant && unlocks.FramedCard(small.Info.Code, c2) == framedGot
                && unlocks.Progress(c2, small.Info.Code) == (small.CardCount, small.CardCount));
        }

        // ---- 7. Odds: exact against 20,000 seeded packs. ----
        {
            var house = SelectionPacks.Build("classic", "SP-classic", LoadDeck("classic")!.Main.Concat(LoadDeck("classic")!.Extra).Select(c => Banlist.BaseCode(c, cards)),
                Fresh(), 2005, printings, cards);
            var oddsSets = new[] { "DB1", "HA01", "WISU", "BPRO", "EEN", "RA04" }.Select(c => (code: c, set: catalog.Find(c))).Append((code: "SP-classic", set: (PackSet?)house)).ToList();
            const int packs = 20000;
            var oddsIssues = new List<string>();
            var oddsLines = new List<string>();
            double worst = 0;
            foreach (var ((code, set), index) in oddsSets.Select((x, i) => (x, i)))
            {
                if (set == null) { oddsIssues.Add($"{code}: not in the catalog"); continue; }
                var odds = PackOdds.For(set);
                var rng = new Random(20261101 + index);
                var atLeast = new Dictionary<Rarity, int>();
                var sum = new Dictionary<Rarity, double>();
                var sumSq = new Dictionary<Rarity, double>();
                for (int i = 0; i < packs; i++)
                {
                    var counts = PackCatalog.Open(set, rng).GroupBy(x => x.Rarity).ToDictionary(g => g.Key, g => g.Count());
                    foreach (var (r, n) in counts)
                    {
                        atLeast[r] = atLeast.GetValueOrDefault(r) + 1;
                        sum[r] = sum.GetValueOrDefault(r) + n;
                        sumSq[r] = sumSq.GetValueOrDefault(r) + n * n;
                    }
                }
                foreach (var r in sum.Keys.Where(r => odds.All(o => o.Rarity != r))) oddsIssues.Add($"{code}: pulled {Rarities.Name(r)}, which the odds don't list");
                foreach (var o in odds)
                {
                    double got = atLeast.GetValueOrDefault(o.Rarity) / (double)packs, mean = sum.GetValueOrDefault(o.Rarity) / packs;
                    double sd = Math.Sqrt(Math.Max(0, sumSq.GetValueOrDefault(o.Rarity) / packs - mean * mean));
                    // The spec's tolerances (0.5 points, 2%), widened to 4 standard errors where 20,000 packs can't resolve them.
                    double tolOne = Math.Max(0.005, 4 * Math.Sqrt(o.AtLeastOne * (1 - o.AtLeastOne) / packs)), tolPer = Math.Max(0.02 * o.PerPack, 4 * sd / Math.Sqrt(packs));
                    worst = Math.Max(worst, Math.Max(Math.Abs(got - o.AtLeastOne) / tolOne, Math.Abs(mean - o.PerPack) / tolPer));
                    if (Math.Abs(got - o.AtLeastOne) > tolOne || Math.Abs(mean - o.PerPack) > tolPer)
                        oddsIssues.Add($"{code} {Rarities.Name(o.Rarity)}: at least one {o.AtLeastOne:P2} vs {got:P2}, per pack {o.PerPack:0.0000} vs {mean:0.0000}");
                }
                oddsLines.Add($"{code}: {string.Join(" · ", odds.Where(o => o.Rarity > Rarity.Common).Select(o => $"{Rarities.Name(o.Rarity)} {PackOdds.Text(o)}"))}");
            }
            foreach (var l in oddsLines) Console.WriteLine($"         {l}");
            string T(double perPack, double one) => PackOdds.Text(new RarityOdds { PerPack = perPack, AtLeastOne = one });
            bool textOk = T(1.3, 0.9) == "about 1.3 a pack" && T(0.33, 1 / 3.0) == "1 in 3.0 packs" && T(0.08, 1 / 12.4) == "1 in 12 packs" && T(0, 0) == "never";
            var ha = catalog.Find("HA01");
            bool hitOk = db1 != null && ha != null && PackOdds.IsHit(db1, Rarity.SuperRare) && !PackOdds.IsHit(db1, Rarity.Rare)
                         && PackCatalog.IsRaritySet(ha) && !PackOdds.IsHit(ha, PackCatalog.BaseRarity(ha)) && ha.Pool.Keys.Where(r => r > PackCatalog.BaseRarity(ha)).All(r => PackOdds.IsHit(ha, r));
            Check($"odds: PackOdds.For matches {packs:N0} seeded packs of {string.Join(", ", oddsSets.Select(s => s.code))} (at least one within 0.5 points, per pack within 2%, "
                  + $"or 4 standard errors where wider; worst at {worst:P0} of its tolerance); the text reads \"1 in 3.0 packs\" / \"1 in 12 packs\" / \"about 1.3 a pack\"; "
                  + $"only pulls above a rarity set's base glow{(oddsIssues.Count > 0 ? ": " + Few(oddsIssues) : "")}",
                oddsIssues.Count == 0 && textOk && hitOk);
        }

        // ---- 8. Every card can be pulled (exact, from the odds). ----
        {
            var unreachable = new List<string>();
            var allHouse = circuits.All.SelectMany(c => c.Matches).Select(m => m.Deck).Distinct().Select(dk => LoadDeck(dk)).Where(dk => dk != null)
                .Select(dk => SelectionPacks.Build(dk!.Name, "SP-" + dk.Name, dk.Main.Concat(dk.Extra).Select(c => Banlist.BaseCode(c, cards)), null, 2026, printings, cards)).ToList();
            var thin = new List<string>();
            foreach (var set in catalog.Sets.Concat(allHouse).Concat(imports))
            {
                var odds = PackOdds.For(set).Where(o => o.AtLeastOne > 0 && o.PerPack > 0).Select(o => o.Rarity).ToHashSet();
                var lost = set.Pool.Where(x => !odds.Contains(x.Key)).SelectMany(x => x.Value).Distinct()
                    .Where(c => !set.Pool.Any(x => odds.Contains(x.Key) && x.Value.Contains(c))).ToList();
                if (lost.Count > 0)
                    unreachable.Add($"{set.Info.Code}: {lost.Count} cards never pulled ({string.Join(", ", set.Pool.Where(x => !odds.Contains(x.Key)).Select(x => Rarities.Name(x.Key)))})");
                // The 7 base slots draw from at least CommonFloor cards (F3: no pack of 7 copies of one stray Common).
                if (catalog.Sets.Contains(set) && PackCatalog.IsRaritySet(set) && set.Pool.TryGetValue(PackCatalog.BaseRarity(set), out var b) && b.Count < PackCatalog.CommonFloor)
                    thin.Add($"{set.Info.Code}: base {Rarities.Name(PackCatalog.BaseRarity(set))} has {b.Count} cards");
            }
            var wisu = catalog.Find("WISU");
            var wisuSuper = wisu == null ? null : PackOdds.For(wisu).FirstOrDefault(o => o.Rarity == Rarity.SuperRare);
            var strays = catalog.Sets.Where(s => s.Pool.TryGetValue(Rarity.Common, out var c) && c.Count is > 0 and < PackCatalog.CommonFloor).Select(s => s.Info.Code).ToList();
            Check($"every card can be pulled: every card of all {catalog.Sets.Count} sets, {allHouse.Count} house packs and {imports.Count} imports has a chance above 0 a pack "
                  + $"(Wild Survivors' Super Rares {(wisuSuper == null ? "missing" : PackOdds.Text(wisuSuper))}); the {strays.Count} sets with a stray Common ({string.Join(", ", strays)}) "
                  + $"open as rarity sets{(unreachable.Count + thin.Count > 0 ? ": " + Few(unreachable.Concat(thin)) : "")}",
                unreachable.Count == 0 && thin.Count == 0 && wisuSuper != null && wisuSuper.AtLeastOne > 0 && strays.All(c => PackCatalog.IsRaritySet(catalog.Find(c)!)));
        }

        // ---- 8b. Imports and house packs open as they always did (the rarity-set rule is for real sets only). ----
        {
            var allHouse = circuits.All.SelectMany(c => c.Matches).Select(m => m.Deck).Distinct().Select(dk => LoadDeck(dk)).Where(dk => dk != null)
                .SelectMany(dk => new[] { 2005, 2014, 2026 }.Select(era => SelectionPacks.Build(dk!.Name, "SP-" + dk.Name, dk.Main.Concat(dk.Extra).Select(c => Banlist.BaseCode(c, cards)), Fresh(), era, printings, cards)))
                .Where(x => x.CardCount > 0).ToList();
            var differ = new List<string>();
            int packsCompared = 0, fewCommons = 0;
            foreach (var (set, index) in imports.Concat(allHouse).Select((x, i) => (x, i)))
            {
                if (PackCatalog.IsRaritySet(set)) differ.Add($"{set.Info.Code}: counted as a rarity set");
                int commons = (set.Pool.TryGetValue(Rarity.Common, out var cm) ? cm : new List<uint>()).Concat(set.Pool.TryGetValue(Rarity.ShortPrint, out var sp) ? sp : new List<uint>()).Distinct().Count();
                if (commons < PackCatalog.CommonFloor) fewCommons++;
                var now = new Random(3000 + index);
                var before = new Random(3000 + index);
                for (int i = 0; i < 300; i++)
                {
                    packsCompared++;
                    var a = PackCatalog.Open(set, now);
                    var b = BaseOpen(set, before);
                    if (!a.Select(x => (x.Code, x.Rarity)).SequenceEqual(b.Select(x => (x.Code, x.Rarity)))) { differ.Add($"{set.Info.Code} pack {i}"); break; }
                }
            }
            Check($"imports and house packs open exactly as before the rework: {packsCompared:N0} seeded packs of {imports.Count} imports and {allHouse.Count} house packs "
                  + $"({fewCommons} with fewer than {PackCatalog.CommonFloor} commons) match the base layout pull for pull{(differ.Count > 0 ? ": " + Few(differ) : "")}",
                differ.Count == 0 && packsCompared > 0);
        }

        // ---- 9. Booster boxes. ----
        {
            var boxSets = shelfNames.SelectMany(n => featured.For(n)).Where(x => !x.Soon).Select(x => catalog.Find(x.Code)).Where(s => s != null).Distinct().ToList();
            var boxIssues = new List<string>();
            int boxes = 0, upgraded = 0, capped = 0, guaranteed = 0;
            var saved = ShopPrices.BoxGuarantee;
            for (int i = 0; i < 500 && boxSets.Count > 0; i++)
            {
                var set = boxSets[i % boxSets.Count]!;
                ShopPrices.BoxGuarantee = i % 5 == 4 ? ShopPrices.BoxGuaranteeKind.Top : ShopPrices.BoxGuaranteeKind.Classic;
                int price = ShopPrices.Box(set);
                var p = new Profile { Dp = price + 1000 };
                p.Collection.Salt = (uint)(7000 + i);
                p.Collection.CommonestOf = c => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
                // Every third box into a finished set: every pull a 4th copy, so the refund cap is what holds.
                if (i % 3 == 2)
                    foreach (var c in set.Pool.Values.SelectMany(x => x).Distinct()) p.Collection.Add(c, set.Pool.Keys.Max(), Collection.MaxCopies, CopySource.Starter, 0);
                int dp0 = p.Dp, total0 = p.Collection.Total, opened0 = p.PacksOpened;
                var unUpgraded = BoxRoll(set, i);
                var r = BoosterBox.Open(p, set, price, new Random(i));
                boxes++;
                string where = $"box {i} ({set.Info.Code}, {ShopPrices.BoxGuarantee})";
                var top = ShopPrices.BoxTop(set);
                if (!r.Opened || r.Pulls.Count != ShopPrices.BoxPacks * PackCatalog.PackSize) { boxIssues.Add($"{where}: {r.Pulls.Count} cards"); continue; }
                if (top is Rarity t)
                {
                    guaranteed++;
                    if (!r.Pulls.Any(x => x.pull.Rarity == t) || r.Topper.Rarity != t || !set.Pool[t].Contains(r.Topper.Code)) boxIssues.Add($"{where}: no {Rarities.Name(t)}");
                }
                if (r.Upgraded) upgraded++;
                if (BoxLoss(unUpgraded, r, top) is { Length: > 0 } loss) boxIssues.Add($"{where}: {loss}");
                int sold = r.Pulls.Sum(x => x.added.Dp), cap = (int)(price * ShopPrices.BoxRefundShare);
                if (r.Refund != Math.Min(sold, cap) || r.Refund > price / 2) boxIssues.Add($"{where}: refund {r.Refund} (sold {sold}, cap {cap})");
                if (sold > cap) capped++;
                if (p.Dp != dp0 - price + r.Refund || p.DpSpent.GetValueOrDefault(Ledger.Boxes) != price || p.DpSpent.Count != 1
                    || p.DpEarned.GetValueOrDefault(Ledger.PackRefund) != r.Refund || Ledger.Net(p) != p.Dp - dp0)
                    boxIssues.Add($"{where}: DP {dp0} -> {p.Dp} for {price}, refund {r.Refund}, spent {Few(p.DpSpent.Select(x => $"{x.Key} {x.Value}"))}");
                if (p.PacksOpened != opened0 + ShopPrices.BoxPacks) boxIssues.Add($"{where}: PacksOpened +{p.PacksOpened - opened0}");
                int added = r.Pulls.Count(x => x.added.Kept && x.added.Replaced == null);
                if (p.Collection.Total != total0 + added || r.Pulls.Any(x => !set.Pool.TryGetValue(x.pull.Rarity, out var pool) || !pool.Contains(x.pull.Code)))
                    boxIssues.Add($"{where}: the collection went {total0} -> {p.Collection.Total} for {added} kept");
                if (r.Pulls.Where(x => x.added.Kept).Sum(x => x.added.Copy?.Paid ?? 0) > price) boxIssues.Add($"{where}: the kept copies carry more than the price");
            }
            // The Rarity Collections under Classic (a Platinum Secret promised, Ghost, Starlight and Quarter Century above
            // it): no upgrade costs a card rarer than the promise.
            int rareBoxes = 0, rareUpgrades = 0;
            var rareLosses = new List<string>();
            ShopPrices.BoxGuarantee = ShopPrices.BoxGuaranteeKind.Classic;
            foreach (var set in new[] { "RA01", "RA02", "RA03", "RA04", "RA05" }.Select(catalog.Find).Where(x => x != null))
                for (int i = 0; i < 300; i++)
                {
                    var p = new Profile { Dp = ShopPrices.Box(set!) };
                    var before = BoxRoll(set!, 50000 + i);
                    var r = BoosterBox.Open(p, set!, ShopPrices.Box(set!), new Random(50000 + i));
                    rareBoxes++;
                    if (r.Upgraded) rareUpgrades++;
                    if (BoxLoss(before, r, ShopPrices.BoxTop(set!)) is { Length: > 0 } loss) rareLosses.Add($"{set!.Info.Code} box {i}: {loss}");
                }
            ShopPrices.BoxGuarantee = saved;
            Check($"boxes never lose a card rarer than their promise: against each box's roll before the upgrade, {boxes} featured boxes and {rareBoxes} Rarity Collection boxes "
                  + $"under Classic ({rareUpgrades} upgraded) give up at most one card, below the promise{(rareLosses.Count > 0 ? ": " + Few(rareLosses) : "")}",
                rareLosses.Count == 0 && rareBoxes > 0);
            // Short of DP: nothing happens.
            var poor = new Profile { Dp = 10 };
            var none = boxSets.Count > 0 ? BoosterBox.Open(poor, boxSets[0]!, ShopPrices.Box(boxSets[0]!), new Random(1)) : new BoxResult();
            bool poorOk = !none.Opened && poor.Dp == 10 && poor.DpSpent.Count == 0 && poor.PacksOpened == 0;
            Check($"boxes: {boxes} seeded boxes of {boxSets.Count} featured sets (Classic and Top guarantees), each {ShopPrices.BoxPacks * PackCatalog.PackSize} cards and its guaranteed rarity "
                  + $"({guaranteed} boxes; {upgraded} upgraded a pack), DP spent is the price at {Ledger.Boxes}, the refund at most half ({capped} capped), PacksOpened +{ShopPrices.BoxPacks}, "
                  + $"the collection reconciles; short of DP nothing happens{(boxIssues.Count > 0 ? ": " + Few(boxIssues) : "")}",
                boxIssues.Count == 0 && boxes == 500 && poorOk && Ledger.Boxes == "packs.box");
        }

        // ---- 10. Prize Pack odds. ----
        {
            var ratings = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName));
            var prizes = new PrizePacks(cards, printings, LoadDeck);
            var prizeIssues = new List<string>();
            int cups = 0;
            string sample = "";
            for (int c = 0; c < library.Cups.Count; c++)
            {
                if (library.Cups[c].Locals.Count != Brackets.LocalsPerCup) continue;
                cups++;
                var b = Brackets.Create(library, c, ratings, (uint)(9100 + c));
                var pool = library.Cups[c].Pool;
                var o = prizes.Odds(b, pool);
                bool Sums(List<(Rarity rarity, double share)> mix) => mix.Count == 0 || Math.Abs(mix.Sum(x => x.share) - 1) < 1e-9;
                if (!Sums(o.Field) || !Sums(o.Tournament) || !Sums(o.Slab) || o.Field.Count == 0 || o.FieldCards == 0 || o.SlabTenPercent != PrizePacks.SlabTenPercent)
                    prizeIssues.Add($"{library.Cups[c].Name}: field {o.Field.Sum(x => x.share):0.###} ({o.FieldCards} cards), tournament {o.Tournament.Sum(x => x.share):0.###}, slab {o.Slab.Sum(x => x.share):0.###}, {o.SlabTenPercent}%");
                if (cups == 1)
                {
                    // Slot 1 and the slab's grade against seeded packs.
                    const int n = 3000;
                    var slot1 = new Dictionary<Rarity, int>();
                    int tens = 0, slabs = 0;
                    for (int k = 0; k < n; k++)
                    {
                        var pulls = prizes.Roll(b, pool, k);
                        var first = pulls.FirstOrDefault(x => x.Slot == 1);
                        if (first != null) slot1[first.Rarity] = slot1.GetValueOrDefault(first.Rarity) + 1;
                        var slab = pulls.FirstOrDefault(x => x.Slot == 5);
                        if (slab != null) { slabs++; if (slab.Slab == 10) tens++; }
                    }
                    foreach (var (r, share) in o.Field)
                        if (Math.Abs(slot1.GetValueOrDefault(r) / (double)n - share) > 0.03) prizeIssues.Add($"{library.Cups[c].Name} slot 1 {Rarities.Name(r)}: {share:P1} vs {slot1.GetValueOrDefault(r) / (double)n:P1}");
                    if (slabs == 0 || Math.Abs(tens / (double)slabs - o.SlabTenPercent / 100.0) > 0.03) prizeIssues.Add($"{library.Cups[c].Name}: slab 10s {tens}/{slabs}");
                    sample = $"{library.Cups[c].Name}: {string.Join(" · ", o.Field.Select(x => $"{Rarities.Name(x.rarity)} {x.share:P0}"))}; {o.TournamentSet?.Name}; slab Grade 10 {o.SlabTenPercent}% "
                             + $"({tens * 100.0 / Math.Max(1, slabs):0}% of {slabs} seeded)";
                }
            }
            Check($"Prize Pack odds for {cups} Locals: each slot's mix sums to 1, the slab split is {PrizePacks.SlabTenPercent}% Grade 10, slot 1 and the slab match {3000:N0} seeded packs "
                  + $"within 3 points ({sample}){(prizeIssues.Count > 0 ? ": " + Few(prizeIssues) : "")}", prizeIssues.Count == 0 && cups > 0);
        }

        // ---- 11. The shop's lines. ----
        VoiceChecks(root, roster, circuits, db1!, Fresh, Reach, ExpFor, Check);

        // ---- 12. The save. ----
        {
            var s = Fresh();
            s.ShopVisits = 7;
            s.ShopYearSeen = 2011;
            s.Arrivals[2010] = 6;
            s.Arrivals[2011] = 7;
            s.VoiceCounts["footer"] = 3;
            s.VoiceCounts["shelf:Beginner Circuit"] = 2;
            s.VoiceCounts[ShopVoice.UltraSpacing] = 5;
            var path = Path.Combine(Path.GetTempPath(), $"ygo-shop-{Guid.NewGuid():N}.txt");
            s.Save(path);
            var back = Profile.Load(path)!;
            File.WriteAllLines(path, File.ReadAllLines(path).Where(l => !l.StartsWith("shopvisits ") && !l.StartsWith("shopseen ") && !l.StartsWith("arrival ") && !l.StartsWith("voice ")));
            var old = Profile.Load(path)!;
            File.Delete(path);
            bool Same(Profile a) => a.ShopVisits == 7 && a.ShopYearSeen == 2011 && a.Arrivals.Count == 2 && a.Arrivals[2010] == 6 && a.Arrivals[2011] == 7
                                    && a.VoiceCounts.Count == 3 && a.VoiceCounts["footer"] == 3 && a.VoiceCounts["shelf:Beginner Circuit"] == 2 && a.VoiceCounts[ShopVoice.UltraSpacing] == 5;
            bool roundTrip = Same(back);
            back.Seen.Add(ShopVoice.OpponentSeen + "tink:1");
            back.Seen.Add("shopline:footer#2");
            back.NewGamePlus();
            bool oppCleared = !back.Seen.Contains(ShopVoice.OpponentSeen + "tink:1") && back.Seen.Contains("shopline:footer#2");
            Check("the save: shop visits, the shop year seen, arrivals and voice counts (a hook with a space in it too) round-trip and survive New Game+ (heard lines stay heard, "
                  + "the once-per-opponent marks go, so those lines play again); an old save loads with none",
                roundTrip && Same(back) && oppCleared && old.ShopVisits == 0 && old.ShopYearSeen == 0 && old.Arrivals.Count == 0 && old.VoiceCounts.Count == 0);
        }
        return failures;
    }

    /// <summary>A box's 24 packs as they roll before any upgrade (the same rng calls <see cref="BoosterBox.Open"/> makes first).</summary>
    private static List<Pull> BoxRoll(PackSet set, int seed)
    {
        var rng = new Random(seed);
        return Enumerable.Range(0, ShopPrices.BoxPacks).SelectMany(_ => PackCatalog.Open(set, rng)).ToList();
    }

    /// <summary>
    /// What a box gave up against its roll before the upgrade: "" when it gave up nothing, or one card below the promise
    /// (the upgrade); otherwise what it lost.
    /// </summary>
    private static string BoxLoss(List<Pull> before, BoxResult box, Rarity? top)
    {
        var left = box.Pulls.Select(x => (x.pull.Code, x.pull.Rarity)).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var lost = new List<(uint code, Rarity rarity)>();
        foreach (var pull in before)
        {
            var key = (pull.Code, pull.Rarity);
            if (left.TryGetValue(key, out var n) && n > 0) left[key] = n - 1;
            else lost.Add(key);
        }
        if (lost.Count == 0) return "";
        if (lost.Count == 1 && box.Upgraded && top is Rarity t && lost[0].rarity < t) return "";
        return $"lost {string.Join(", ", lost.Select(l => $"{l.code} {Rarities.Name(l.rarity)}"))}{(box.Upgraded ? " to the upgrade" : "")}";
    }

    /// <summary>
    /// How packs opened before the rework (HEAD before SHOP-REWORK-SPEC's A1): 7 commons, a rare-tier card and a foil slot;
    /// with no commons, 7 of the lowest rarity and 2 foil slots over the rest. The reference house packs and imports must
    /// still match.
    /// </summary>
    private static List<Pull> BaseOpen(PackSet set, Random rng)
    {
        List<Pull> Tier(params Rarity[] rarities)
        {
            var list = new List<Pull>();
            foreach (var r in rarities)
                if (set.Pool.TryGetValue(r, out var cards))
                    foreach (var c in cards)
                        for (int n = r == Rarity.ShortPrint ? 1 : 3; n > 0; n--)
                            list.Add(new Pull(c, r));
            return list;
        }
        Pull Pick(List<Pull> tier) => tier[rng.Next(tier.Count)];
        Pull? Foil(Rarity? exclude = null)
        {
            var options = set.Pool.Keys.Where(r => r != exclude && PackCatalog.FoilWeight(r) is > 0).Select(r => (rarity: r, weight: PackCatalog.FoilWeight(r)!.Value)).ToList();
            var roll = rng.NextDouble() * (PackCatalog.FoilNothing + options.Sum(o => o.weight));
            foreach (var (rarity, weight) in options)
            {
                if (roll < weight)
                {
                    var cards = set.Pool[rarity];
                    return new Pull(cards[rng.Next(cards.Count)], rarity);
                }
                roll -= weight;
            }
            return null;
        }
        var pulls = new List<Pull>();
        if (set.Pool.Count == 0) return pulls;
        var commons = Tier(Rarity.Common, Rarity.ShortPrint);
        var rares = Tier(Rarity.Rare, Rarity.SuperRare, Rarity.RareParallelRare, Rarity.NormalParallelRare);
        if (commons.Count > 0)
        {
            for (int i = 0; i < 7; i++) pulls.Add(Pick(commons));
            pulls.Add(Pick(rares.Count > 0 ? rares : commons));
            pulls.Add(Foil() ?? Pick(rares.Count > 0 ? rares : commons));
        }
        else
        {
            var lowest = set.Pool.Keys.Min();
            var baseTier = Tier(lowest);
            for (int i = 0; i < 7; i++) pulls.Add(Pick(baseTier));
            for (int i = 0; i < 2; i++) pulls.Add(Foil(lowest) ?? Pick(baseTier));
        }
        return pulls.OrderBy(p => p.Rarity).ToList();
    }

    /// <summary>One line of campaign/shop.txt, read here on its own to tell which hook a picked line came from.</summary>
    private sealed class FileLine
    {
        public string Hook = "", Speaker = "", Mode = "", Text = "", Band = "";
        public bool Plus;
        public List<string> After = new List<string>(), Before = new List<string>();
        public Regex Pattern = null!;
    }

    private static List<FileLine> ReadShopFile(string path)
    {
        var lines = new List<FileLine>();
        string hook = "", band = "";
        bool plus = false;
        var after = new List<string>();
        var before = new List<string>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line.Substring(0, colon).Trim();
            var value = line.Substring(colon + 1).Trim();
            switch (key)
            {
                case "pool": hook = value; band = ""; plus = false; after = new List<string>(); before = new List<string>(); continue;
                case "after": after.Add(value); continue;
                case "before": before.Add(value); continue;
                case "band": plus = value.EndsWith("+"); band = value.TrimEnd('+').Trim(); continue;
            }
            var mode = "";
            var m = Regex.Match(key, @"^(.*?)\s*\[(era|open)\]$");
            if (m.Success) { key = m.Groups[1].Value; mode = m.Groups[2].Value; }
            var pattern = "^" + string.Join(".*?", Regex.Split(value, @"\{[^}]*\}").Select(Regex.Escape)) + "$";
            lines.Add(new FileLine { Hook = hook, Speaker = key, Mode = mode, Text = value, Band = band, Plus = plus, After = after, Before = before, Pattern = new Regex(pattern) });
        }
        return lines;
    }

    /// <summary>
    /// Check 11: shop.txt's problems, then a scripted run of visits and packs on one save: one footer per visit, an
    /// arrival beats the footer, unheard lines first, the pull priority and spacing, opponent lines once per opponent per
    /// circuit and never on a house pack, kit-hello first, and the same lines on a second run.
    /// </summary>
    private static void VoiceChecks(string root, List<Personality> roster, Circuits circuits, PackSet db1,
        Func<uint, Profile> fresh, Action<Profile, int> reach, Func<int, int> expFor, Action<string, bool> Check)
    {
        var path = Path.Combine(root, "campaign", ShopVoice.FileName);
        var voice = ShopVoice.Load(path);
        var story = Story.Load(Path.Combine(root, "campaign", "story.txt"));
        var problems = voice.Problems(roster, story.Scenes.Select(s => s.Id), circuits);
        var file = File.Exists(path) ? ReadShopFile(path) : new List<FileLine>();
        var noShelf = circuits.All.Where(c => !file.Any(l => l.Hook == "shelf:" + c.Name)).Select(c => c.Name).ToList();
        Check($"shop.txt parses: {file.Select(l => l.Hook).Distinct().Count()} hooks, {file.Count} lines, {problems.Count} problems (hooks, speakers, scenes, bands, shelf circuits, "
              + $"placeholders, lengths), every circuit has a shelf: pool{(problems.Count + noShelf.Count > 0 ? ": " + string.Join("; ", problems.Concat(noShelf.Select(n => "no shelf: pool for " + n)).Take(6)) : "")}",
            File.Exists(path) && problems.Count == 0 && noShelf.Count == 0);

        string HookOf(VoiceLine? line) =>
            line == null ? "-" : file.FirstOrDefault(l => l.Speaker == line.Speaker && l.Pattern.IsMatch(line.Text))?.Hook ?? "?";
        var bands = circuits.All.Select(c => c.Band).Where(b => b.Length > 0).Distinct().Append(ShopVoice.PostGameBand).ToList();
        int Eligible(string hook, Profile p) => file.Count(l => l.Hook == hook && l.Speaker != "-" && l.After.All(p.Seen.Contains) && !l.Before.Any(p.Seen.Contains)
            && (l.Mode.Length == 0 || (l.Mode == "era") == p.EraLimited)
            && (l.Band.Length == 0 || (l.Plus ? bands.IndexOf(ShopVoice.CurrentBand(p, circuits)) >= bands.IndexOf(l.Band) : ShopVoice.CurrentBand(p, circuits) == l.Band)));
        var kitHello = file.FirstOrDefault(l => l.Hook == ShopVoice.KitHello)?.Text ?? "";

        // The scripted run: every step's hook and line, plus what each rule needs.
        var ultraCode = db1.Pool.TryGetValue(Rarity.UltraRare, out var ultras) ? ultras[0] : db1.Pool.Values.First()[0];
        PackMoment Pack(Rarity best, string opponent = "", bool recipe = false, bool deck = false, bool house = false, bool previous = false) => new PackMoment
        {
            Set = db1, Best = best, BestCode = ultraCode, OpponentKey = opponent.Length > 0 ? ultraCode : 0, OpponentId = opponent,
            RecipeCard = recipe ? ultraCode : 0, DeckCard = deck ? ultraCode : 0, HousePack = house, PreviousHadLine = previous,
        };
        var opponents = circuits.All.Take(2).SelectMany(c => c.Matches).Select(m => m.Character.Id).Distinct().ToList();
        (List<string> log, Dictionary<string, object> facts) Script(uint salt)
        {
            var log = new List<string>();
            var facts = new Dictionary<string, object>();
            var p = fresh(salt);
            circuits.NoteBand(p);
            // Kit has said hello already on this save, so a pull pool's Kit line reads as its own hook (save B below tests the hello).
            p.Seen.Add("shop:" + ShopVoice.KitHello);
            string Note(string step, VoiceLine? line) { var h = HookOf(line); log.Add($"{step}: {h} {line?.Speaker}: {line?.Text}"); return h; }
            // Visits: the first plays the opening stock, then footers (unheard first, then the same order again).
            facts["visit1"] = Note("visit 1", voice.Footer(p, circuits, Arrivals.Visit(p, circuits.ShopYear(p))));
            int eligible = Eligible("footer", p);
            var footers = new List<VoiceLine?>();
            for (int v = 0; v < eligible + 2; v++) { var line = voice.Footer(p, circuits, Arrivals.Visit(p, circuits.ShopYear(p))); Note($"visit {v + 2}", line); footers.Add(line); }
            facts["eligible"] = eligible;
            facts["footers"] = footers;
            // A level-up: the arrival beats the footer.
            p.Exp = expFor(3);
            var arrived = Arrivals.Visit(p, circuits.ShopYear(p));
            var arrival = voice.Footer(p, circuits, arrived);
            facts["arrival"] = (Note("level-up visit", arrival), arrival?.Text ?? "", arrived.LastOrDefault());
            // Ultra on every third Ultra pack.
            var ultra = new List<bool>();
            for (int k = 0; k < 9; k++) ultra.Add(Note($"ultra {k + 1}", voice.Pull(Pack(Rarity.UltraRare), p, circuits)) == "pull:ultra");
            facts["ultra"] = ultra;
            // Secret, not right after a pack with a line.
            facts["secret"] = (Note("secret after a line", voice.Pull(Pack(Rarity.SecretRare, previous: true), p, circuits)),
                               Note("secret", voice.Pull(Pack(Rarity.SecretRare), p, circuits)));
            // The priority: each pack qualifies twice or more; the higher rule speaks.
            facts["priority"] = new List<(string want, string got)>
            {
                ("pull:starlight", Note("starlight + opponent + recipe", voice.Pull(Pack(Rarity.StarlightRare, opponents[0], recipe: true, deck: true), p, circuits))),
                ("pull:ghost", Note("ghost + opponent", voice.Pull(Pack(Rarity.GhostRare, opponents[0], recipe: true), p, circuits))),
                ("pull:quarter-century", Note("quarter century + opponent", voice.Pull(Pack(Rarity.QuarterCenturySecretRare, opponents[0], deck: true), p, circuits))),
                ("pull:opponent", Note("collector's + opponent + recipe", voice.Pull(Pack(Rarity.CollectorsRare, opponents[0], recipe: true, deck: true), p, circuits))),
                ("pull:recipe", Note("collector's + recipe + deck", voice.Pull(Pack(Rarity.CollectorsRare, recipe: true, deck: true), p, circuits))),
                ("pull:deck", Note("ultimate + deck", voice.Pull(Pack(Rarity.UltimateRare, deck: true), p, circuits))),
                ("pull:collectors", Note("collector's", voice.Pull(Pack(Rarity.CollectorsRare), p, circuits))),
                ("pull:ultimate", Note("ultimate", voice.Pull(Pack(Rarity.UltimateRare), p, circuits))),
                ("pull:deck", Note("secret + deck", voice.Pull(Pack(Rarity.SecretRare, deck: true), p, circuits))),
            };
            // Opponent lines: once per opponent per circuit, never on a house pack.
            facts["opponent"] = new List<string>
            {
                Note($"{opponents[0]} again", voice.Pull(Pack(Rarity.Rare, opponents[0]), p, circuits)),
                Note($"{opponents[1]}, house pack", voice.Pull(Pack(Rarity.Rare, opponents[1], house: true), p, circuits)),
                Note($"{opponents[1]}", voice.Pull(Pack(Rarity.Rare, opponents[1]), p, circuits)),
                Note($"{opponents[1]} again", voice.Pull(Pack(Rarity.Rare, opponents[1]), p, circuits)),
            };
            reach(p, 1);
            ((List<string>)facts["opponent"]).Add(Note($"{opponents[0]}, next circuit", voice.Pull(Pack(Rarity.Rare, opponents[0]), p, circuits)));
            // Save B, Tag Night: Kit's first line is the hello; the newly unlocked footer pool's lines come before heard ones.
            var labelSave = p;
            p = fresh(salt + 1);
            circuits.NoteBand(p);
            for (int v = 0; v < eligible + 1; v++) Note($"save B visit {v + 1}", voice.Footer(p, circuits, Arrivals.Visit(p, circuits.ShopYear(p))));
            bool kitBefore = log.Any(l => l.StartsWith("save B") && l.Contains(" kit: "));
            p.Seen.Add("tag-night");
            var unlocked = new List<VoiceLine?>();
            int newLines = Eligible("footer", p) - eligible;
            for (int v = 0; v < newLines + 1; v++) { var line = voice.Footer(p, circuits, Arrivals.Visit(p, circuits.ShopYear(p))); Note($"save B tag-night visit {v + 1}", line); unlocked.Add(line); }
            for (int k = 0; k < 6; k++) Note($"save B pull after tag-night {k + 1}", voice.Pull(Pack(Rarity.SecretRare), p, circuits));
            facts["kit"] = (kitBefore, newLines, unlocked);
            // The labels: never marked heard.
            p = labelSave;
            int seen = p.Seen.Count;
            var labels = new[] { voice.Pick("header", p, circuits, new Dictionary<string, string>()), voice.Pick("house-pack:name", p, circuits, new Dictionary<string, string> { ["deck"] = "Classic" }) };
            facts["labels"] = (labels[0]?.Text ?? "", labels[1]?.Text ?? "", p.Seen.Count == seen && labels.All(l => l?.IsLabel == true));
            return (log, facts);
        }

        var (run1, f) = Script(20261101);
        var (run2, _) = Script(20261101);
        var (other, _) = Script(424242);
        int el = (int)f["eligible"];
        var footers = (List<VoiceLine?>)f["footers"];
        bool footerOk = footers.All(l => HookOf(l) == "footer") && footers.Take(el).Select(l => l!.Text).Distinct().Count() == el
                        && footers[el]!.Text == footers[0]!.Text && footers[el + 1]!.Text == footers[1]!.Text;
        var (arrivalHook, arrivalText, arrivalYear) = ((string, string, int))f["arrival"];
        var ultra = (List<bool>)f["ultra"];
        var (secretAfter, secret) = ((string, string))f["secret"];
        var priority = (List<(string want, string got)>)f["priority"];
        var opp = (List<string>)f["opponent"];
        var (kitBefore, freshLines, unlocked) = ((bool, int, List<VoiceLine?>))f["kit"];
        var firstKit = run1.FirstOrDefault(l => l.StartsWith("save B") && l.Contains(" kit: ")) ?? "";
        var (header, houseName, labelsOk) = ((string, string, bool))f["labels"];
        Check($"the shop's lines on a scripted run: the first visit plays {f["visit1"]}; one footer a visit, all {el} eligible heard before one repeats; a level-up's {arrivalHook} "
              + $"(\"{arrivalText}\") beats the footer; the labels read \"{header}\" and \"{houseName}\" and aren't marked heard",
            (string)f["visit1"] == "arrival:" + circuits.All[0].Era && footerOk && arrivalHook.StartsWith("arrival") && arrivalText.Contains(arrivalYear.ToString())
            && header == "GUS'S CARDS" && houseName == "House Pack: Classic" && labelsOk);
        Check($"pull lines: Ultra on packs {string.Join(",", ultra.Select((b, i) => (b, i)).Where(x => x.b).Select(x => x.i + 1))} of 9; Secret {(secretAfter == "-" ? "silent" : secretAfter)} right after a line, "
              + $"{secret} otherwise; the priority {string.Join(" > ", priority.Select(x => x.got.Replace("pull:", "")))} (want {string.Join(" > ", priority.Select(x => x.want.Replace("pull:", "")))}); "
              + $"opponent lines {string.Join(", ", opp.Select(x => x.Replace("pull:", "")))} (repeat, house pack, new, repeat, next circuit)",
            ultra.SequenceEqual(new[] { true, false, false, true, false, false, true, false, false }) && secretAfter == "-" && secret == "pull:secret"
            && priority.All(x => x.got == x.want || x.got.StartsWith(x.want + ":")) && opp[0] == "-" && opp[1] == "-" && opp[2].StartsWith("pull:opponent") && opp[3] == "-" && opp[4].StartsWith("pull:opponent"));
        var unlockedHooks = unlocked.Select(l => l?.Text ?? "").ToList();
        var tagNightLines = file.Where(l => l.Hook == "footer" && l.After.SequenceEqual(new[] { "tag-night" })).Select(l => l.Text).ToList();
        Check($"Kit: no Kit footer before Tag Night, then kit-hello first (\"{firstKit.Split(": ").Last()}\") and once; Tag Night's {freshLines} new footer lines play before any heard one; "
              + $"the same save gives the same {run1.Count} lines on a second run (another save's order differs: {(run1.SequenceEqual(other) ? "no" : "yes")})",
            !kitBefore && firstKit.EndsWith(kitHello) && kitHello.Length > 0 && run1.Count(l => l.StartsWith("save B") && l.EndsWith(kitHello)) == 1
            && freshLines == tagNightLines.Count && unlockedHooks.Count(t => t == kitHello) == 1 && tagNightLines.All(unlockedHooks.Contains)
            && run1.SequenceEqual(run2) && !run1.SequenceEqual(other));
        // SHOP-REWORK-SPEC section 6: "Kit's lines need after: tag-night".
        var ungated = file.Where(l => l.Speaker == ShopVoice.KitId && l.Hook != ShopVoice.KitHello && !l.Hook.StartsWith("shelf:") && !l.After.Contains("tag-night")).Select(l => l.Hook).Distinct().ToList();
        Check("Kit: every Kit line outside the shelves waits for Tag Night" + (ungated.Count > 0 ? $": not {string.Join(", ", ungated)}" : ""), ungated.Count == 0);
    }
}
