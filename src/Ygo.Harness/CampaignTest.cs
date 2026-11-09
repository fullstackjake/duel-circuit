using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;

namespace Ygo.Harness;

/// <summary>`--campaign-test`: the shop's stock, pack odds, saving and loading a profile, circuit rewards, the 3-copy cap, collection rules.</summary>
public static class CampaignTest
{
    public static int Run(ICardDataProvider cards, string root)
    {
        int failures = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }

        var printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        var catalog = new PackCatalog(printings, cards);
        Console.WriteLine($"Shop: {catalog.Sets.Count} booster sets. Newest:");
        foreach (var set in catalog.Sets.Take(8))
            Console.WriteLine($"  {set.Info.Code,-6} {set.Info.Date}  {set.Info.Name}  ({set.CardCount} cards: {string.Join(", ", set.Rarities.Select(Rarities.Name))})");
        Console.WriteLine("  ...oldest:");
        foreach (var set in catalog.Sets.Skip(catalog.Sets.Count - 3))
            Console.WriteLine($"  {set.Info.Code,-6} {set.Info.Date}  {set.Info.Name}  ({set.CardCount} cards)");
        Check("the shop has at least 100 booster sets", catalog.Sets.Count >= 100);
        Check("no structure decks or tins in the shop", !catalog.Sets.Any(s => s.Info.Name.Contains("Structure Deck") || s.Info.Name.Contains("Tin")));

        // Pack odds over many openings, for a few kinds of set.
        var rng = new Random(7);
        foreach (var code in new[] { catalog.Sets[0].Info.Code, "LOB", "BP02", "RA01" })
        {
            var set = catalog.Find(code);
            if (set == null) { Console.WriteLine($"  ({code} not in the shop)"); continue; }
            const int packs = 3000;
            var counts = new Dictionary<Rarity, int>();
            bool sizes = true, members = true;
            for (int i = 0; i < packs; i++)
            {
                var pack = PackCatalog.Open(set, rng);
                sizes &= pack.Count == PackCatalog.PackSize;
                foreach (var pull in pack)
                {
                    counts[pull.Rarity] = counts.GetValueOrDefault(pull.Rarity) + 1;
                    members &= set.Pool.TryGetValue(pull.Rarity, out var pool) && pool.Contains(pull.Code);
                }
            }
            Console.WriteLine($"\n{set.Info.Name} ({code}), {packs} packs:");
            foreach (var pair in counts.OrderBy(p => p.Key))
                Console.WriteLine($"  {Rarities.Name(pair.Key),-28} {pair.Value,6}   1 in {packs / (double)pair.Value:F1} packs");
            Check($"{code}: every pack has {PackCatalog.PackSize} cards", sizes);
            Check($"{code}: every pull is in the set at that rarity", members);
        }

        // A new profile, a few campaign results, and a save / load round trip.
        Console.WriteLine();
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities"));
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        foreach (var (c, i) in circuits.All.Select((c, i) => (c, i)))
            Console.WriteLine($"  {i + 1,2}. [{c.Band}] {c.Name,-36} {string.Join(", ", c.Matches.Select(m => $"{m.Character.Name} ({m.Deck})"))}  · win {circuits.WinDp(i)} DP · prize {c.Prize} · shop to {c.Era}");
        Check($"{circuits.All.Count} circuits in difficulty bands, each with 3 or 4 matches",
            circuits.All.Count >= 20 && circuits.All.All(c => c.Matches.Count is 3 or 4 && c.Band.Length > 0));
        Check("every circuit's duelists exist (no match skipped)", circuits.All.Sum(c => c.Matches.Count) == 4 * 3 + 71);
        var shopYears = circuits.All.Select(c => c.Era).ToList();
        Check($"card shop eras never go backwards ({string.Join(", ", shopYears.Distinct())})", shopYears.Zip(shopYears.Skip(1), (a, b) => b >= a).All(x => x));
        var deckNames = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories).Select(Path.GetFileNameWithoutExtension).ToHashSet();
        Check("every match's deck exists", circuits.All.All(c => c.Matches.All(m => deckNames.Contains(m.Deck))));
        Check($"{circuits.TagCups.Count} Tag Cups, each with 3 tag matches, opening after a circuit, decks that exist",
            circuits.TagCups.Count == 7 && circuits.TagCups.All(t => t.Matches.Count == 3 && t.After >= 0
                && t.Matches.All(m => deckNames.Contains(m.lead.Deck) && deckNames.Contains(m.partner.Deck))));
        var eraLists = Ygo.Engine.DeckBuilding.Banlist.LoadFolders(new[] { Path.Combine(root, "lflists") });
        Check($"every circuit after Beginner names an installed era ban list ({string.Join(", ", circuits.All.Select(c => c.Banlist).Where(b => b.Length > 0).Distinct())})",
            circuits.All.All(c => c.Band == "Beginner" ? c.Banlist.Length == 0 : eraLists.Any(l => l.Name == c.Banlist)));
        // The starter decks meet the first ban list as you leave the Beginner band; report what each needs to change.
        var firstList = eraLists.First(l => l.Name == circuits.All.First(c => c.Banlist.Length > 0).Banlist);
        foreach (var name in new[] { "classic", "warriors", "zombies", "gadgets", "stall-burn" })
        {
            var starterPath = Path.Combine(root, "decks", name + ".ydk");
            if (!File.Exists(starterPath)) continue;
            var issues = Ygo.Engine.DeckBuilding.DeckRules.Validate(Deck.LoadYdk(starterPath), firstList, cards);
            Console.WriteLine($"         starter {name} under {firstList.Name}: {(issues.Count == 0 ? "legal" : string.Join("; ", issues.Select(i => i.Message)))}");
        }

        var starter = Deck.LoadYdk(Path.Combine(root, "decks", "classic.ydk"));
        starter.Name = "Starter";
        var profile = Profile.New(starter, cards, printings);
        Check($"starter collection holds the starter deck ({profile.Collection.Total} cards)",
            CollectionRules.Validate(starter, profile.Collection, cards).Count == 0);
        var modern = Deck.LoadYdk(Path.Combine(root, "decks", "modern.ydk"));
        var missing = CollectionRules.Validate(modern, profile.Collection, cards);
        Check($"a deck of cards you don't own is flagged ({missing.Count} problems, e.g. \"{missing.FirstOrDefault()?.Message}\")", missing.Count > 0);

        Check("only the first circuit is open at the start", circuits.IsUnlocked(0, profile) && !circuits.IsUnlocked(1, profile));

        // EXP, levels, Card Tokens, the Card Trader, and New Game+ (on a scratch profile).
        var player = Profile.New(starter, cards, printings);
        Check($"levels: 0 EXP is level 1, {Leveling.ExpToNext(1)} EXP is level 2", Leveling.LevelFor(0) == 1 && Leveling.LevelFor(Leveling.ExpToNext(1)) == 2);
        var ups = Leveling.AddExp(player, Enumerable.Range(1, 9).Sum(Leveling.ExpToNext));
        Check($"reaching level 10 pays {ups.Sum(u => u.Dp)} DP and {player.Tokens} tokens over {ups.Count} level-ups",
            player.Level == 10 && ups.Count == 9 && player.Tokens == ups.Sum(u => u.Tokens) && player.Tokens == 7);
        var sangan = cards.All.First(c => c.Name == "Sangan").Code;
        var ash = cards.All.First(c => c.Name == "Ash Blossom & Joyous Spring").Code;
        player.EraLimited = true;
        Check("era-limited trader: a 2017 card is refused in a 2005 campaign",
            CardTrader.Blocked(ash, player, 2005, printings, cards)?.Contains("first printed") == true);
        int had = player.Collection.Count(sangan);
        Check("a token trades for a card from the era", CardTrader.Trade(sangan, player, 2005, printings, cards) && player.Collection.Count(sangan) == had + 1 && player.Tokens == 6);
        int sanganCost = CardTrader.Cost(sangan, printings, cards), ashCost = CardTrader.Cost(ash, printings, cards);
        CardInfo? AtBest(Rarity r) => printings == null ? null
            : cards.All.FirstOrDefault(c => c.Alias == 0 && printings.FirstYear(c.Code, 0) > 0 && printings.Commonest(c.Code, 0) == r);
        var ultra = AtBest(Rarity.UltraRare);
        var secret = AtBest(Rarity.SecretRare);
        Check($"tokens by rarity: Sangan (Common) {sanganCost}, {ultra?.Name} (Ultra Rare at best) {(ultra == null ? 0 : CardTrader.Cost(ultra.Code, printings, cards))}, "
              + $"{secret?.Name} (Secret Rare only) {(secret == null ? 0 : CardTrader.Cost(secret.Code, printings, cards))}",
            sanganCost == 1 && ultra != null && CardTrader.Cost(ultra.Code, printings, cards) == 2 && secret != null && CardTrader.Cost(secret.Code, printings, cards) == 3);
        player.EraLimited = false;
        player.Tokens = 6;
        Check($"without the era limit any card can be traded for, at its price ({player.Tokens} tokens left)",
            CardTrader.Trade(ash, player, 2005, printings, cards) && player.Tokens == 6 - ashCost);
        // The Library Trader: a cup's pool month in place of the shop's era, whatever the mode.
        player.Tokens = 6;
        var poolAsh = CardTrader.Blocked(ash, player, 2005, printings, cards, "2005-10");
        player.EraLimited = true;
        var lateAsh = CardTrader.Blocked(ash, player, 2005, printings, cards, "2025-05");
        player.EraLimited = false;
        Check($"the Library Trader takes only cards out by the cup's pool ({poolAsh}), and a later pool allows them even in a 2005 campaign",
            poolAsh?.Contains("this cup's cards end October 2005") == true && CardTrader.Blocked(sangan, player, 2005, printings, cards, "2005-10") == null
            && lateAsh == null && !CardTrader.Trade(ash, player, 2005, printings, cards, "2005-10") && player.Tokens == 6);
        // DP buys tokens, each dearer than the last; Ban Passes are earned only (3, then the Advanced and Expert clears: 5).
        player.Dp = 30000;
        int t0 = player.Tokens;
        bool bought = DpServices.BuyToken(player) && DpServices.BuyToken(player);
        Check($"two tokens for 1,000 + 1,500 DP (next {DpServices.TokenPrice(player)})", bought && player.Tokens == t0 + 2 && player.Dp == 30000 - 2500 && DpServices.TokenPrice(player) == 2000);
        player.EraLimited = true;
        var allCleared = new Profile();
        foreach (var i in Enumerable.Range(0, circuits.All.Count)) foreach (var m in Enumerable.Range(0, circuits.All[i].Matches.Count)) allCleared.MatchWins[Profile.MatchKey(i, m)] = 1;
        Check($"Ban Passes: {BanPasses.Starting} at the start, {BanPasses.Allowance(circuits, allCleared)} with every band cleared (at most {BanPasses.Max})",
            BanPasses.Allowance(circuits, player) == BanPasses.Starting && BanPasses.Allowance(circuits, allCleared) == BanPasses.Max);
        var tmpSave = Path.Combine(Path.GetTempPath(), $"ygo-profile-{Guid.NewGuid():N}.txt");
        player.MatchWins["0.0"] = 1;
        player.Save(tmpSave);
        var reloaded = Profile.Load(tmpSave)!;
        File.Delete(tmpSave);
        Check("EXP, tokens, bought tokens and the era option survive saving", reloaded.Exp == player.Exp && reloaded.Tokens == player.Tokens
            && reloaded.TokensBought == 2 && reloaded.PassRefund == 0
            && reloaded.TokensSpent == 1 + ashCost && reloaded.EraLimited == player.EraLimited);
        // A save from when Ban Pass slots were sold (3 bought: 4,000 + 8,000 + 12,000 DP) gets the DP back, once.
        var oldSave = Path.Combine(Path.GetTempPath(), $"ygo-profile-{Guid.NewGuid():N}.txt");
        player.Save(oldSave);
        File.WriteAllLines(oldSave, File.ReadAllLines(oldSave).Select(l => l.StartsWith("bought ") ? "bought 2 3" : l).ToList());
        var refunded = Profile.Load(oldSave)!;
        refunded.Save(oldSave);
        var reread = Profile.Load(oldSave)!;
        File.Delete(oldSave);
        Check($"an old save's 3 bought Ban Pass slots are refunded once ({refunded.PassRefund:N0} DP)",
            refunded.PassRefund == 24000 && refunded.Dp == player.Dp + 24000 && refunded.PassesBought == 0 && reread.PassRefund == 0 && reread.Dp == refunded.Dp);
        // Posters: the first is yours from the start, each band (and the Novice Tag Cup) pins one more, the pick is saved,
        // and a pick you haven't earned shows the first poster.
        var everything = new Profile();
        foreach (var k in allCleared.MatchWins.Keys) everything.MatchWins[k] = 1;
        foreach (var t in Enumerable.Range(0, circuits.TagCups.Count)) foreach (var m in Enumerable.Range(0, circuits.TagCups[t].Matches.Count)) everything.MatchWins[Circuits.TagKey(t, m)] = 1;
        // (and the Library's "Every Era" final won: its rounds' keys, as the Library names them; the poster asks the Library
        // Library.Load sets, so a renamed final or another round count still earns it)
        var posterLibrary = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), roster, circuits);
        Posters.Library = posterLibrary;
        foreach (var r in Enumerable.Range(0, posterLibrary.Rounds.Count)) everything.LibraryWins[posterLibrary.FinalKey(r)] = 1;
        // (and the Back Alley's: found, a card lost to the rail, Orvel's Table, the held card home, both alley titles)
        foreach (var scene in new[] { "alley-kit", "orvel-first-loss", BackAlley.HeldHomeScene }) everything.Seen.Add(scene);
        everything.AlleyTables["orvels-table"] = new AlleyTableState { Cleared = true };
        everything.AlleyTables["no-limits"] = new AlleyTableState { Cleared = true }; // the-wall (NO-LIMITS-SPEC section 11)
        foreach (var kind in new[] { "brawl", "midnight", "invitational" }) everything.AlleyBrackets[kind] = new LocalsRecord { Entries = 1, Titles = 1 };
        everything.Ladder.Wins[Ladder.TopKey] = 1; // the Champion's Ladder's top
        // (and every headliner's Combo Breaker poster)
        foreach (var p in Posters.All.Where(p => p.Id.StartsWith(Posters.BreakerPrefix))) everything.Headliners[p.Id.Substring(Posters.BreakerPrefix.Length)] = new HeadlinerState { Breaker = 1 };
        Check($"posters: {Posters.Unlocked(circuits, player).Count} at the start ({Posters.Current(circuits, player)?.Title}), "
              + $"{Posters.Unlocked(circuits, everything).Count} of {Posters.All.Count} with every band, the Novice Tag Cup, the Every Era final and the alley's cleared, every one framing a real card",
            Posters.Unlocked(circuits, player).Count == 1 && Posters.Current(circuits, player)?.Id == "shop-bell" && Posters.New(circuits, player).Count == 0
            && Posters.Unlocked(circuits, everything).Count == Posters.All.Count && Posters.New(circuits, everything).Count == Posters.All.Count - 1
            && Posters.All.All(p => cards.Get(p.Card) != null));
        // The Every Era poster asks the Library: a final renamed with one round fewer is earned by its own keys (and not by
        // the old ones), one round short isn't, and no Library loaded yet leaves it unearned.
        var everyEra = Posters.Find("every-era")!;
        var renamedFile = Path.Combine(Path.GetTempPath(), $"ygo-library-{Guid.NewGuid():N}.txt");
        var libraryLines = File.ReadAllLines(Path.Combine(root, "campaign", Library.CupsFile)).ToList();
        int lastRound = libraryLines.FindLastIndex(l => l.TrimStart().StartsWith("round:"));
        libraryLines.RemoveAt(lastRound);
        File.WriteAllLines(renamedFile, libraryLines.Select(l => l.TrimStart().StartsWith("final:") ? "final: Every Era Redux" : l));
        var renamed = Library.Load(renamedFile, roster, circuits);
        File.Delete(renamedFile);
        var reduxWon = new Profile();
        foreach (var r in Enumerable.Range(0, renamed.Rounds.Count)) reduxWon.LibraryWins[renamed.FinalKey(r)] = 1;
        var reduxShort = new Profile();
        foreach (var r in Enumerable.Range(0, renamed.Rounds.Count - 1)) reduxShort.LibraryWins[renamed.FinalKey(r)] = 1;
        Posters.Library = renamed;
        bool reduxEarned = everyEra.Earned(circuits, reduxWon), reduxShortEarned = everyEra.Earned(circuits, reduxShort), oldKeysEarned = everyEra.Earned(circuits, everything);
        Posters.Library = null;
        bool noLibraryEarned = everyEra.Earned(circuits, everything);
        Posters.Library = posterLibrary;
        Check($"the Every Era poster asks the Library: earned with the final's {posterLibrary.Rounds.Count} rounds ({posterLibrary.FinalKey(0)}...); a final renamed "
              + $"\"{renamed.Final.Name}\" with {renamed.Rounds.Count} rounds is earned by its keys ({renamed.FinalKey(0)}...), not one round short or by the old keys; "
              + "no Library loaded leaves it unearned",
            posterLibrary.Rounds.Count > 0 && everyEra.Earned(circuits, everything) && renamed.Rounds.Count == posterLibrary.Rounds.Count - 1
            && renamed.FinalKey(0) != posterLibrary.FinalKey(0) && reduxEarned && !reduxShortEarned && !oldKeysEarned && !noLibraryEarned);
        var hanger = Profile.New(starter, cards, printings);
        var posterSave = Path.Combine(Path.GetTempPath(), $"ygo-profile-{Guid.NewGuid():N}.txt");
        hanger.Poster = "golden-age";
        hanger.Save(posterSave);
        var hung = Profile.Load(posterSave)!;
        hanger.Poster = Posters.Emblem;
        hanger.Save(posterSave);
        var emblem = Profile.Load(posterSave)!;
        File.Delete(posterSave);
        Check("the poster pick is saved; one not earned yet shows Shop Bell, and the emblem shows no poster",
            hung.Poster == "golden-age" && Posters.Current(circuits, hung)?.Id == "shop-bell" && Posters.Current(circuits, everything) != null
            && emblem.Poster == Posters.Emblem && Posters.Current(circuits, emblem) == null);

        // Recipe packs by band: Novice decks keep today's 300 DP, a Master deck's packs cost 2,500, and buying a late deck's
        // missing cards outright costs more than the packs that would get them.
        var shopper = Profile.New(starter, cards, printings);
        var mitsurugi = Deck.LoadYdk(Path.Combine(root, "decks", "history", "mitsurugi.ydk"));
        mitsurugi.Name = "mitsurugi";
        int masterPack = RecipeShop.PackPriceFor("mitsurugi", circuits), novicePack = RecipeShop.PackPriceFor(circuits.All.First(c => c.Band == "Novice").Matches[0].Deck, circuits);
        int missingCopies = RecipeShop.Missing(mitsurugi, shopper, Ygo.Engine.DeckBuilding.Banlist.None, 2026, printings, cards).Values.Sum();
        int cardsOnly = RecipeShop.CompletePrice(mitsurugi, shopper, Ygo.Engine.DeckBuilding.Banlist.None, 2026, printings, cards);
        int outright = RecipeShop.CompletePrice(mitsurugi, shopper, Ygo.Engine.DeckBuilding.Banlist.None, 2026, printings, cards, masterPack);
        int viaPacks = (missingCopies + PackCatalog.PackSize - 1) / PackCatalog.PackSize * masterPack;
        Check($"recipe packs by band: Novice {novicePack} DP, Master {masterPack} DP; Mitsurugi's {missingCopies} missing cards cost {outright:N0} DP outright "
              + $"({cardsOnly:N0} for the cards), {viaPacks:N0} in packs",
            novicePack == ShopPrices.NoviceRecipePack && masterPack == 2500 && outright > viaPacks && outright == cardsOnly + RecipeShop.ServiceFee(missingCopies, masterPack)
            && RecipeShop.ServiceFee(missingCopies, RecipeShop.BasePackPrice) == 0);
        int cardsBefore = reloaded.Collection.Total, dpBefore = reloaded.Dp;
        reloaded.NewGamePlus();

        // The Format Library: 12 cups under their own lists and pools; wins of its own (New Game+ keeps them), a clear
        // that skips the Champion's table, and a Format Master stamp only for a deck of yours.
        var library = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), roster, circuits);
        Check($"the Library has {library.Cups.Count} cups, each with a pool, an installed list and decks that exist",
            library.Cups.Count == 12 && library.Cups.All(c => System.Text.RegularExpressions.Regex.IsMatch(c.Pool, @"^\d{4}-\d{2}$")
                && eraLists.Any(l => l.Name == c.Banlist) && c.Matches.Count == 4 && c.Matches.All(m => deckNames.Contains(m.Deck))));
        var champion = Profile.New(starter, cards, printings);
        champion.Exp = 50_000_000; // past the top level, so no level-up pays DP or tokens in between
        int libGauntlet = circuits.All.ToList().FindIndex(c => c.Name == Library.OpensAfter);
        bool shut = !library.Open(champion, circuits);
        foreach (var m in Enumerable.Range(0, circuits.All[libGauntlet].Matches.Count)) champion.MatchWins[Profile.MatchKey(libGauntlet, m)] = 1;
        bool opened = library.Open(champion, circuits) && champion.LibraryOpen;
        int dp0 = champion.Dp, tokens0 = champion.Tokens;
        var libFirst = library.Record(champion, 0, 0, 0, loaner: false, built: true);
        var libAgain = library.Record(champion, 0, 0, 0, loaner: false, built: true);
        var libLent = library.Record(champion, 0, 1, 0, loaner: true, built: false);
        Check($"Library wins pay {libFirst.Dp} the first time, {libAgain.Dp} after, {libLent.Dp} with a loaner; it opens after the Gauntlet",
            shut && opened && libFirst.Dp == Library.WinDp + Circuits.FirstWinBonus && libAgain.Dp == Library.RepeatWinDp
            && libLent.Dp == (int)Math.Round((Library.WinDp + Circuits.FirstWinBonus) * Loaners.DpShare) && champion.Dp == dp0 + libFirst.Dp + libAgain.Dp + libLent.Dp);
        library.Record(champion, 0, 2, 0, loaner: false, built: true);
        var libClear = library.Record(champion, 0, 3, 0, loaner: false, built: true);
        var libRestamp = library.Record(champion, 0, 1, 0, loaner: false, built: true);
        Check($"a cup's first clear pays its prize and {Library.ClearTokens} tokens; the stamp waits for a built win at every match, then pays {Library.StampTokens}",
            libClear.ClearedCircuit == library.Cups[0] && libClear.StampedCup == null && libRestamp.StampedCup == library.Cups[0] && library.HasStamp(0, champion)
            && champion.Tokens == tokens0 + Library.ClearTokens + Library.StampTokens
            && Library.Events(libClear).Contains("cupclear:" + library.Cups[0].Name) && Library.Events(libRestamp).Contains("stamp:" + library.Cups[0].Name) && Library.Events(libRestamp).Contains("stamp"));
        var withTable = new Library(new List<Circuit> { new Circuit { Name = "Test Cup", Matches = { new CircuitMatch { Character = roster[0] }, new CircuitMatch { Character = roster[1], Champion = true } } } }, 0);
        var tabler = new Profile();
        var tableClear = withTable.Record(tabler, 0, 0, 0, loaner: false, built: true);
        Check("a Champion's table match isn't needed to clear its cup, but is for the stamp; its first win raises \"table:\"",
            tableClear.ClearedCircuit != null && tableClear.TableCup == null && !withTable.HasStamp(0, tabler)
            && withTable.Record(tabler, 0, 1, 0, false, true) is var tableWin && tableWin.StampedCup != null && Library.Events(tableWin).Contains("table:Test Cup")
            && withTable.Record(tabler, 0, 1, 0, false, true).TableCup == null);
        var libSave = Path.Combine(Path.GetTempPath(), $"ygo-library-{Guid.NewGuid():N}.txt");
        champion.Save(libSave);
        var libLoaded = Profile.Load(libSave)!;
        File.Delete(libSave);
        libLoaded.NewGamePlus();
        Check("the Library's wins and stamps survive saving and New Game+",
            libLoaded.LibraryOpen && libLoaded.LibraryWins.Count == champion.LibraryWins.Count && library.IsCleared(0, libLoaded) && library.HasStamp(0, libLoaded)
            && library.Record(libLoaded, 0, 0, 0, false, true).Dp == Library.RepeatWinDp);
        // The Library's cosmetics: a cleared cup's era mat, and an engraving with a deck of yours that won there; for DP,
        // counted as spent, kept through saving and New Game+.
        var collector = new Profile { Dp = Cosmetics.MatPrice + 2 * Cosmetics.EngravePrice + 100 };
        string goatMat = Cosmetics.MatId(library.Cups[0]);
        bool lockedFirst = Cosmetics.MatBlocked(collector, library, 0)?.Contains("clear the") == true && !Cosmetics.BuyMat(collector, library, 0);
        for (int m = 0; m < library.Cups[0].Matches.Count; m++) library.Record(collector, 0, m, 0, loaner: m == 0, built: m > 0, deck: m == 0 ? "loaner-goat" : "my-goat");
        collector.Dp = Cosmetics.MatPrice + 2 * Cosmetics.EngravePrice + 100;
        bool matBought = Cosmetics.BuyMat(collector, library, 0) && collector.Mat == goatMat && collector.Dp == 2 * Cosmetics.EngravePrice + 100
            && Cosmetics.MatBlocked(collector, library, 0) == "you own it" && Cosmetics.CupOfMat(library, goatMat) == 0
            && Cosmetics.MatBlocked(collector, library, 1)?.Contains("clear the") == true;
        bool noLoanerPlate = Cosmetics.EngraveBlocked(collector, library, 0, "loaner-goat", "Loaner Goat")?.Contains("win a match") == true;
        bool engraved = Cosmetics.Engrave(collector, library, 0, "my-goat", "My Goat") && Cosmetics.Engraving(collector, library, 0) == "My Goat"
            && !Cosmetics.Engrave(collector, library, 0, "my-goat", "My Goat") && collector.Dp == Cosmetics.EngravePrice + 100;
        library.Record(collector, 0, 1, 0, false, true, "My Chaos Deck");
        bool reEngraved = Cosmetics.Engrave(collector, library, 0, "My Chaos Deck", "My Chaos") && Cosmetics.Engraving(collector, library, 0) == "My Chaos";
        bool picks = Cosmetics.PickMat(collector, "") && collector.Mat == "" && !Cosmetics.PickMat(collector, Cosmetics.MatId(library.Cups[1]))
            && Cosmetics.PickMat(collector, goatMat);
        bool counted = collector.DpSpent[Cosmetics.MatSink] == Cosmetics.MatPrice && collector.DpSpent[Cosmetics.EngraveSink] == 2 * Cosmetics.EngravePrice;
        var cosSave = Path.Combine(Path.GetTempPath(), $"ygo-cosmetics-{Guid.NewGuid():N}.txt");
        collector.Save(cosSave);
        var cosLoaded = Profile.Load(cosSave)!;
        File.Delete(cosSave);
        cosLoaded.NewGamePlus();
        Check($"an era mat ({Cosmetics.MatPrice:N0} DP) and an engraving ({Cosmetics.EngravePrice:N0} DP, a deck of yours that won there, again at full price) wait for the cup's clear, count as DP spent, and survive saving and New Game+",
            lockedFirst && matBought && noLoanerPlate && engraved && reEngraved && picks && counted && cosLoaded.Mat == goatMat && cosLoaded.Mats.SetEquals(collector.Mats)
            && Cosmetics.Engraving(cosLoaded, library, 0) == "My Chaos" && Cosmetics.EngraveDecks(cosLoaded, library, 0).Contains("My Chaos Deck") && Cosmetics.EngraveDecks(cosLoaded, library, 0).Count == 2
            && cosLoaded.DpSpent[Cosmetics.EngraveSink] == 2 * Cosmetics.EngravePrice);
        // The "Every Era" final: six rounds (each a cup's boss under that cup's rules, no Ban Passes), open once all 12
        // cups are cleared, played in order, a lost round played again; the clear pays its prize and 3 tokens.
        var finalist = new Profile();
        var finalRounds = string.Join(", ", library.Rounds.Select(r => $"{library.Cups[r.Cup].Name}: {r.Match.Deck}"));
        bool finalShut = !library.FinalOpen(finalist) && !library.CanPlayRound(finalist, 0);
        DuelReward? lastCupClear = null;
        for (int c = 0; c < library.Cups.Count; c++)
            for (int m = 0; m < library.Cups[c].Matches.Count; m++)
                if (!library.Cups[c].Matches[m].Champion) lastCupClear = library.Record(finalist, c, m, 0, false, true, "mine");
        var lists = Ygo.Engine.DeckBuilding.Banlist.LoadFolders(new[] { Path.Combine(root, "vendor", "LFLists"), Path.Combine(root, "lflists") });
        bool roundRules = Enumerable.Range(0, library.Rounds.Count).All(r => library.RoundRules(r, lists).Passes == 0
            && library.RoundRules(r, lists).Pool == library.Cups[library.Rounds[r].Cup].Pool && library.RoundRules(r, lists).Forced);
        Check($"the {library.Final.Name} final has {library.Rounds.Count} rounds ({finalRounds}), each under its cup's list and pool with no Ban Passes",
            library.Final.Name == "Every Era" && library.Final.Prize == 10000 && library.Rounds.Count == 6 && roundRules
            && library.Rounds.Select(r => r.Match.Deck).SequenceEqual(new[] { "goat-control", "six-samurai", "kozmo", "thunder-dragon", "eldlich", "ryzeal" }));
        bool opens = finalShut && library.FinalOpen(finalist) && lastCupClear?.UnlockedCircuit == library.Final && Library.Events(lastCupClear).Contains("final");
        bool inOrder = library.CanPlayRound(finalist, 0) && !library.CanPlayRound(finalist, 1);
        var lost = library.RecordFinal(finalist, 0, 1, "mine");
        bool waits = library.FinalNext(finalist) == 0 && !library.FinalWon(finalist, 0);
        var roundOne = library.RecordFinal(finalist, 0, 0, "mine");
        for (int r = 1; r < library.Rounds.Count - 1; r++) library.RecordFinal(finalist, r, 0, "mine");
        int finalTokens = finalist.Tokens;
        var won = library.RecordFinal(finalist, library.Rounds.Count - 1, 0, "mine");
        var replayed = library.RecordFinal(finalist, 0, 0, "mine");
        Check($"it opens with the last cup's clear, rounds go in order, a lost round waits to be played again; the clear pays {library.Final.Prize:N0} DP and {Library.FinalTokens} tokens once",
            opens && inOrder && waits && lost.Dp == Circuits.LossDp
            && roundOne.FirstWin && roundOne.RoundOpened == 2 && Library.Events(roundOne).Contains("round:2")
            && won.ClearedCircuit == library.Final && Library.Events(won).Contains("cupclear:" + library.Final.Name)
            && finalist.Tokens == finalTokens + Library.FinalTokens + won.LevelUps.Sum(u => u.Tokens)
            && library.FinalCleared(finalist) && library.FinalNext(finalist) == -1 && replayed.ClearedCircuit == null && replayed.Dp == Library.RepeatWinDp
            && Cosmetics.EngraveDecks(finalist, library, library.Rounds[0].Cup).Contains("mine"));
        // Level-ups after the first win (PROOF-2026-11-01): a Library match you'd already won, an Every Era round you'd
        // already won, and every Locals duel pay their levels in tokens, never DP; first wins and retries still pay DP.
        {
            var farmer = new Profile { Exp = Leveling.ExpToNext(1) * 0 };
            for (int lv = 1; lv < 40; lv++) farmer.Exp += Leveling.ExpToNext(lv);
            for (int m = 0; m < library.Cups[0].Matches.Count; m++) farmer.LibraryWins[library.Key(0, m)] = 1;
            int farmDp0 = farmer.Dp, farmTokens0 = farmer.Tokens, repeatDp = 0;
            var repeats = new List<DuelReward>();
            for (int i = 0; i < 12; i++) { int before = farmer.Dp; repeats.Add(library.Record(farmer, 0, 0, 0, false, true, "mine")); repeatDp = Math.Max(repeatDp, farmer.Dp - before); }
            bool levelled = repeats.Any(r => r.LevelUps.Count > 0);
            var freshLv = new Profile();
            for (int lv = 1; lv < 40; lv++) freshLv.Exp += Leveling.ExpToNext(lv);
            int freshBefore = freshLv.Dp;
            var firstLv = library.Record(freshLv, 1, 0, 0, false, true, "mine");
            Check($"repeat Library wins pay their level-ups in tokens, never DP (at most {repeatDp} DP a repeat duel, cap 300; {farmer.Tokens - farmTokens0} tokens over 12), and a first win still pays its level DP",
                levelled && repeatDp <= 300 && repeats.All(r => r.LevelUps.All(u => u.Dp == 0 && u.Tokens >= 1))
                && firstLv.LevelUps.All(u => u.Dp > 0 || u.Level % 2 == 1) && (firstLv.LevelUps.Count == 0 || firstLv.LevelUps.Any(u => u.Dp > 0)));
        }
        // The story: every scene's trigger names a real circuit or cup, every speaker is a character, scenes show once.
        var story = Story.Load(Path.Combine(root, "campaign", "story.txt"));
        var ids = roster.Select(p => p.Id).ToHashSet();
        var names = circuits.All.Select(c => c.Name).Concat(circuits.TagCups.Select(t => t.Name)).Concat(library.Cups.Select(c => c.Name)).Append(library.Final.Name).ToHashSet();
        // The Back Alley's triggers (alley, alleyvisit, open:/alleyclear:<table>, headliner:<id>...) are its data's.
        var alleyData = AlleyData.Load(Path.Combine(root, "campaign", AlleyData.FileName), cards);
        var guestIds = GuestList.Load(Path.Combine(root, "campaign", "guests.txt"), roster, library).All.Select(g => g.Character.Id).ToHashSet();
        // The Champion's Ladder's triggers (ladder, ladderseat:, ladderbeat:, ladderrung:, laddertop): Ladder.IsTrigger.
        var ladderData = Ladder.Load(Path.Combine(root, "campaign", Ladder.FileName), roster);
        bool TriggerOk(string t) => ladderData.IsTrigger(t) || BackAlley.IsStoryTrigger(t, alleyData) || t == "start" || t == "partner" || t == "ngplus" || t == "library" || t == "final" || t == "stamp" || t == "locals" || t == "bracketwin" ||
            t == Story.FirstLossEvent || t == Story.FinaleEvent || (t.StartsWith("guest:") && guestIds.Contains(t.Substring(6))) ||
            (t.StartsWith("guestbeat:") && guestIds.Contains(t.Substring(10))) ||
            (t.StartsWith("bracketwin:") && library.Cups.Any(c => t.Substring(11) == c.Name + " Locals")) || t.StartsWith("level:")
            || (t.StartsWith("round:") && int.TryParse(t.Substring(6), out var rn) && rn >= 2 && rn <= library.Rounds.Count)
            || ((t.StartsWith("stamp:") || t.StartsWith("table:")) && library.IndexOf(t.Substring(6)) >= 0)
            || (t.StartsWith("partner:") && ids.Contains(t.Substring(8)))
            || ((t.StartsWith("clear:") || t.StartsWith("open:")) && names.Contains(t.Substring(t.IndexOf(':') + 1)))
            || (t.StartsWith("cupclear:") && names.Contains(t.Substring(9)))
            // Card unlocks: a first win, a bond reached, a set completed.
            || (t.StartsWith("beat:") && ids.Contains(t.Substring(5)))
            || (t.StartsWith("bond:") && t.Split(':') is { Length: 3 } bond && ids.Contains(bond[1]) && bond[2] is "3" or "5")
            || t == "setcomplete" || (t.StartsWith("setcomplete:") && catalog.Find(t.Substring(12)) != null);
        var badTriggers = story.Scenes.Where(sc => !TriggerOk(sc.Trigger)).Select(sc => $"{sc.Id} ({sc.Trigger})").ToList();
        var badSpeakers = story.Scenes.SelectMany(sc => sc.Lines).Select(l => l.Speaker).Where(sp => sp.Length > 0 && sp != "partner" && sp != "opponent" && !ids.Contains(sp)).Distinct().ToList();
        Check($"{story.Scenes.Count} story scenes; triggers and speakers all exist{(badTriggers.Count + badSpeakers.Count > 0 ? $" (bad: {string.Join(", ", badTriggers.Concat(badSpeakers))})" : "")}",
            story.Scenes.Count >= 20 && badTriggers.Count == 0 && badSpeakers.Count == 0);
        var reader = Profile.New(starter, cards, printings);
        var opening = story.Pending(reader, new[] { "start" });
        foreach (var sc in opening) Story.MarkSeen(reader, sc);
        Check($"the opening scene plays once ({opening.Count} then {story.Pending(reader, new[] { "start" }).Count})",
            opening.Count == 1 && story.Pending(reader, new[] { "start" }).Count == 0);
        var vera = roster.First(p => p.Id == "vera");
        var withVera = story.Pending(reader, new[] { "partner", "partner:vera" }, "vera");
        var withRyo = story.Pending(reader, new[] { "partner", "partner:rookie-ryo" }, "rookie-ryo");
        Check($"a partner with a scene of their own gets it ({withVera.FirstOrDefault()?.Id}); others get the general one ({withRyo.FirstOrDefault()?.Id})",
            withVera.Count == 1 && withVera[0].Id == "partner-vera" && withRyo.Count == 1 && withRyo[0].Id == "first-partner");
        Check("{partner} fills in", Story.Fill("Hi {partner}", vera, reader) == "Hi Vera");
        var storyHeader = File.ReadLines(Path.Combine(root, "campaign", "story.txt")).TakeWhile(l => l.StartsWith("#")).ToList();
        foreach (var t in new[] { "beat:vera", "bond:ines:3", "bond:vera:5", "setcomplete" }) if (!TriggerOk(t)) Console.WriteLine($"  (trigger refused: {t})");
        Check("card unlock triggers pass (beat:, bond:<id>:3|5, setcomplete, setcomplete:<set>; not bond:<id>:4 or beat:<nobody>) and the story header lists them",
            TriggerOk("beat:vera") && TriggerOk("bond:ines:3") && TriggerOk("bond:vera:5") && TriggerOk("setcomplete") && TriggerOk("setcomplete:" + catalog.Sets[0].Info.Code)
            && !TriggerOk("bond:vera:4") && !TriggerOk("beat:nobody") && !TriggerOk("setcomplete:NOPE")
            && new[] { "beat:", "bond:", "setcomplete", "setcomplete:" }.All(k => storyHeader.Any(l => l.Contains(k))));
        Check("{card} and {set} fill in", Story.Fill("A {card} from {set}", null, reader, card: "Sangan", set: "Legend of Blue Eyes White Dragon") == "A Sangan from Legend of Blue Eyes White Dragon");
        GateChecks(Check, story, starter, cards, printings, storyHeader);

        // Tag Cups: closed until their circuit is cleared; partners are duelists you've beaten; a win pays and counts.
        var tagger = Profile.New(starter, cards, printings);
        Check("a Tag Cup is closed until its circuit is cleared", !circuits.IsTagCupOpen(0, tagger));
        // A cup moved later (the Master Tag Cup, 2026-10-31) stays open for a save that already won a match there.
        var early = Profile.New(starter, cards, printings);
        early.MatchWins[Circuits.TagKey(0, 0)] = 1;
        Check("a Tag Cup you've already won a match at stays open before its circuit is cleared", circuits.IsTagCupOpen(0, early));
        for (int m = 0; m < circuits.All[circuits.TagCups[0].After].Matches.Count; m++)
            for (int ci = 0; ci <= circuits.TagCups[0].After; ci++)
                for (int mi = 0; mi < circuits.All[ci].Matches.Count; mi++) circuits.Record(tagger, ci, mi, 0);
        var partners = circuits.Partners(tagger);
        Check($"it opens after its circuit, with {partners.Count} partners to choose from (duelists you've beaten)",
            circuits.IsTagCupOpen(0, tagger) && partners.Count > 0 && partners.All(p => tagger.HasBeaten(p.Id)));
        int dpBeforeTag = tagger.Dp;
        for (int m = 0; m < 3; m++) circuits.RecordTag(tagger, 0, m, 0);
        Check($"winning its 3 tag matches clears it and pays {tagger.Dp - dpBeforeTag:N0} DP (prize included)",
            circuits.IsTagCupCleared(0, tagger) && tagger.Dp - dpBeforeTag >= circuits.TagCups[0].Prize);
        // Loaners in Tag Cups and guest duels: a win pays half the duel's DP (prizes in full).
        var full = circuits.RecordTag(tagger, 0, 0, 0);
        var half = circuits.RecordTag(tagger, 0, 0, 0, loaner: true);
        Check($"a tag win with a loaner pays half ({half.Summary} against {full.Summary})",
            half.Dp == (int)Math.Round(full.Dp * Loaners.DpShare) && half.Summary.Contains("loaner"));
        // Partner bonds: more wins against someone, more decks to bring and sharper play.
        var brask = roster.First(p => p.Id == "brask");
        var fresh = Profile.New(starter, cards, printings);
        fresh.Wins["brask"] = 1;
        var bonded = TagPartners.DeckChoices(brask, circuits, tagger, Array.Empty<string>());
        Check($"a partner beaten once brings their first deck ({string.Join(", ", TagPartners.DeckChoices(brask, circuits, fresh, Array.Empty<string>()))}); "
              + $"beaten {tagger.WinsAgainst("brask")}x, any deck you beat them with ({string.Join(", ", bonded)}) and plays sharper",
            TagPartners.DeckChoices(brask, circuits, fresh, Array.Empty<string>()).SequenceEqual(new[] { brask.Decks[0] })
            && bonded.Count >= 2 && TagPartners.Playing(brask, tagger).Skill >= TagPartners.SharpSkill && TagPartners.Playing(brask, fresh) == brask);
        var kit = roster.FirstOrDefault(p => p.Id == TagPartners.ShopRegularId);
        var kitLent = kit == null ? new List<string>() : TagPartners.DeckChoices(kit, circuits, fresh, new[] { fresh.Deck, "my other deck" });
        var kitOwn = kit == null ? new List<string>() : TagPartners.DeckChoices(kit, circuits, fresh, new[] { fresh.Deck });
        Check($"Kit, the shop regular, plays your other decks ({string.Join(", ", kitLent)}), or their own while you have one ({string.Join(", ", kitOwn)})",
            kit != null && kitLent.SequenceEqual(new[] { "my other deck" }) && kitOwn.SequenceEqual(new[] { kit.Decks[0] }));
        // Losses in a row at a match: three before the versus screen offers its "Stuck?" advice; a win ends the run.
        circuits.Record(fresh, 0, 1, 1); circuits.Record(fresh, 0, 1, 1); circuits.Record(fresh, 0, 1, 2); circuits.Record(fresh, 0, 1, 1);
        int run = fresh.LossesInARow(Profile.MatchKey(0, 1));
        circuits.RecordTag(fresh, 0, 0, 1);
                fresh.TagPartner = "brask";
        fresh.PartnerDecks["brask"] = "gadgets";
        var partnerSave = Path.Combine(Path.GetTempPath(), $"ygo-profile-{Guid.NewGuid():N}.txt");
        fresh.Save(partnerSave);
        var freshAgain = Profile.Load(partnerSave)!;
        File.Delete(partnerSave);
        Check("the partner you picked and their deck survive saving", freshAgain.TagPartner == "brask" && freshAgain.PartnerDecks.GetValueOrDefault("brask") == "gadgets");
        Check($"losses in a row count per match, a draw keeps the run, and it survives saving ({run} at match 2, 1 at the Tag Cup)",
            run == 3 && freshAgain.LossesInARow(Profile.MatchKey(0, 1)) == 3 && freshAgain.LossesInARow(Circuits.TagKey(0, 0)) == 1);
        circuits.Record(fresh, 0, 1, 0);
        Check("a win ends the run of losses", fresh.LossesInARow(Profile.MatchKey(0, 1)) == 0);
        // Special guests: every one names real characters and decks, unlocks by its milestone, and pays and counts a win.
        var guests = GuestList.Load(Path.Combine(root, "campaign", "guests.txt"), roster, library);
        bool GuestDeckExists(string d) => File.Exists(Path.Combine(root, "decks", "history", d + ".ydk")) || File.Exists(Path.Combine(root, "decks", d + ".ydk"));
        Check($"{guests.All.Count} special guests, each with real decks and (when they bring one) a real partner ({string.Join(", ", guests.All.Select(g => $"{g.Character.Name}: {string.Join("/", g.Decks)}{(g.HasTag ? $" + {g.Partner.Name}" : "")}"))})",
            guests.All.Count >= 3 && guests.All.All(g => g.Decks.Count > 0 && g.Decks.All(GuestDeckExists))
            && guests.All.Where(g => g.HasTag).All(g => GuestDeckExists(g.PartnerDeck)));
        // Scouting: every written report is for a real deck, and every card it says to watch is a card in that deck.
        var scouting = Scouting.Load(Path.Combine(root, "campaign", "scouting.txt"));
        var scoutFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        var scoutProblems = new List<string>();
        var cardNames = cards.All.Select(c => c.Name).ToHashSet();
        foreach (var report in scouting.Written)
        {
            if (!scoutFiles.TryGetValue(report.Deck, out var file)) { scoutProblems.Add($"{report.Deck}: no such deck"); continue; }
            var scouted = Deck.LoadYdk(file);
            var inDeck = scouted.Main.Concat(scouted.Extra).Select(c => cards.Get(c)?.Name).Where(n => n != null).ToHashSet();
            scoutProblems.AddRange(report.Keys.Where(k => !inDeck.Contains(k)).Select(k => $"{report.Deck}: {k} isn't in the deck"));
            if (report.Plan.Length == 0 || report.Counters.Length == 0 || report.Keys.Count < 3 || report.Build.Count < 3) scoutProblems.Add($"{report.Deck}: incomplete");
            scoutProblems.AddRange(report.Build.Where(b => !cardNames.Contains(b)).Select(b => $"{report.Deck}: no card named {b}"));
            if (report.Turn is not ("" or "first" or "second")) scoutProblems.Add($"{report.Deck}: turn '{report.Turn}'");
        }
        var wallDecks = circuits.TagCups.Where(c => c.Name.StartsWith("Expert") || c.Name.StartsWith("Master"))
            .SelectMany(c => c.Matches.SelectMany(m => new[] { m.lead.Deck, m.partner.Deck }))
            .Concat(guests.All.SelectMany(g => g.HasTag ? g.Decks.Append(g.PartnerDeck) : g.Decks)).Distinct()
            .Where(d => scouting.Written.All(r => r.Deck != d) && d != "goat-control" && d != "stall-burn" && d != "hat" && d != "altergeist").ToList();
        Check($"{scouting.Written.Count} scouting reports, each card to watch is in its deck and each card to build with exists ({string.Join("; ", scoutProblems.Take(6))})", scoutProblems.Count == 0);
        Check($"every deck at the late walls has a written report (missing: {string.Join(", ", wallDecks)})", wallDecks.Count == 0);
        // A written report is late-era advice: none reaches a deck an early (Beginner or Novice) match plays unless it was
        // written for that character ("for:"), so Ilse's zombies and gadgets reports stay off Old Gus's, Vera's and Brask's tables.
        var earlyBands = new[] { "Beginner", "Novice" };
        var earlyCircuits = Enumerable.Range(0, circuits.All.Count).Where(i => earlyBands.Contains(circuits.All[i].Band)).ToHashSet();
        var earlyMatches = earlyCircuits.SelectMany(i => circuits.All[i].Matches.Select(m => (who: m.Character.Id, deck: m.Deck)))
            .Concat(circuits.TagCups.Where(t => earlyCircuits.Contains(t.After)).SelectMany(t => t.Matches.SelectMany(m => new[] { (who: m.lead.Character.Id, deck: m.lead.Deck), (who: m.partner.Character.Id, deck: m.partner.Deck) })))
            .Distinct().ToList();
        var earlyAdvice = earlyMatches.Where(m => scoutFiles.ContainsKey(m.deck))
            .Select(m => (m, report: scouting.For(m.deck, Deck.LoadYdk(scoutFiles[m.deck]), cards, m.who)))
            .Where(x => x.report.Written && x.report.For != x.m.who).Select(x => $"{x.m.who} {x.m.deck}").ToList();
        var ilseZombies = scouting.For("zombies", Deck.LoadYdk(scoutFiles["zombies"]), cards, "ilse");
        Check($"no written report reaches a deck an early match plays ({earlyMatches.Count} Beginner and Novice seats) unless it's that character's ({string.Join(", ", earlyAdvice)}); Ilse still gets hers ({ilseZombies.For})",
            earlyAdvice.Count == 0 && ilseZombies.Written && ilseZombies.For == "ilse" && !scouting.For("zombies", Deck.LoadYdk(scoutFiles["zombies"]), cards, "old-gus").Written);
        var fallback = scouting.For("classic", Deck.LoadYdk(scoutFiles["classic"]), cards);
        Check($"an unwritten deck still gets cards to watch ({string.Join(", ", fallback.Keys)})", !fallback.Written && fallback.Keys.Count >= 3);
        // The wall study's advice: every wall is a real match, every deck a real deck, every partner a real character who
        // isn't playing against you there; and the file reads back as written.
        var advice = WallAdvice.Load(Path.Combine(root, "campaign", "wall-advice.txt"));
        var adviceProblems = new List<string>();
        var rosterIds = roster.Select(p => p.Id).ToHashSet();
        foreach (var key in advice.Keys)
        {
            string[] opponents;
            var tagMatch = System.Text.RegularExpressions.Regex.Match(key, @"^tag(\d+)\.(\d+)$");
            if (tagMatch.Success && int.Parse(tagMatch.Groups[1].Value) is int t && t < circuits.TagCups.Count
                && int.Parse(tagMatch.Groups[2].Value) is int mi && mi < circuits.TagCups[t].Matches.Count)
                opponents = new[] { circuits.TagCups[t].Matches[mi].lead.Character.Id, circuits.TagCups[t].Matches[mi].partner.Character.Id };
            else if (guests.All.FirstOrDefault(g => GuestList.Key(g, false) == key || GuestList.Key(g, true) == key) is { } guest)
                opponents = guest.HasTag ? new[] { guest.Character.Id, guest.Partner.Id } : new[] { guest.Character.Id };
            else { adviceProblems.Add($"{key}: no such match"); continue; }
            foreach (var o in advice.For(key))
            {
                if (o.Borrows && !scoutFiles.ContainsKey(o.Deck)) adviceProblems.Add($"{key}: no deck {o.Deck}");
                if (o.Partner.Length > 0 && (!rosterIds.Contains(o.Partner) || !scoutFiles.ContainsKey(o.PartnerDeck))) adviceProblems.Add($"{key}: partner {o.Partner} with {o.PartnerDeck}");
                if (opponents.Contains(o.Partner)) adviceProblems.Add($"{key}: partner {o.Partner} is an opponent there");
            }
        }
        var adviceCopy = Path.Combine(Path.GetTempPath(), $"ygo-advice-{Guid.NewGuid():N}.txt");
        advice.Save(adviceCopy, "check");
        var adviceBack = WallAdvice.Load(adviceCopy);
        File.Delete(adviceCopy);
        bool sameBack = advice.Keys.All(k => adviceBack.For(k).Select(o => (o.Percent, o.Deck, o.Partner, o.PartnerDeck, o.YouFirst, o.YouSecond))
            .SequenceEqual(advice.For(k).Select(o => (o.Percent, o.Deck, o.Partner, o.PartnerDeck, o.YouFirst, o.YouSecond))));
        Check($"wall advice for {advice.Keys.Count} matches ({advice.Keys.Sum(k => advice.For(k).Count)} measured options), all real and reading back as written ({string.Join("; ", adviceProblems.Take(4))})",
            advice.Keys.Count >= 8 && adviceProblems.Count == 0 && sameBack);
        var lessonGuest = guests.All.First(g => g.Unlock == "lessons");
        var student = Profile.New(starter, cards, printings);
        bool lockedBefore = !GuestList.IsUnlocked(lessonGuest, circuits, student);
        foreach (var l in ChainLessons.All) student.Seen.Add(l.SeenKey);
        Check($"{lessonGuest.Character.Name} unlocks with Gus's lessons ({GuestList.UnlockText(lessonGuest, student)})", lockedBefore && GuestList.IsUnlocked(lessonGuest, circuits, student));
        // ...but her tag duel (her and Gus) waits for the first Tag Cup, where tag duels are taught and you pick a partner.
        Check($"{lessonGuest.Character.Name}'s tag duel stays locked after the lessons ({GuestList.TagUnlockText(lessonGuest, circuits, student)})",
            !GuestList.IsTagUnlocked(lessonGuest, circuits, student) && GuestList.TagUnlockText(lessonGuest, circuits, student).Contains(circuits.TagCups[0].Name));
        // Every guest's tag rule names a real Tag Cup, and no guest's tag duel opens before the first Tag Cup is cleared:
        // with every circuit cleared, the lessons passed and a dozen tag wins elsewhere, each guest is open one on one but
        // not in tag; clearing the first Tag Cup opens every tag duel.
        var badTagRules = guests.All.Where(g => g.TagUnlock.StartsWith("cup:") && !circuits.TagCups.Any(t => t.Name == g.TagUnlock.Substring(4).Trim()))
            .Select(g => $"{g.Character.Name}: {g.TagUnlock}").ToList();
        var veteran = Profile.New(starter, cards, printings);
        foreach (var l in ChainLessons.All) veteran.Seen.Add(l.SeenKey);
        for (int ci = 0; ci < circuits.All.Count; ci++)
            for (int mi = 0; mi < circuits.All[ci].Matches.Count; mi++) veteran.MatchWins[Profile.MatchKey(ci, mi)] = 1;
        if (circuits.TagCups.Count > 1) veteran.MatchWins[Circuits.TagKey(1, 0)] = 12;
        // Ilse waits for the Every Era final: the veteran (every circuit, the lessons, 12 tag wins) still has her locked.
        var finalGuest = guests.All.FirstOrDefault(g => g.Unlock == "final");
        bool finalLockedForVeteran = finalGuest != null && !GuestList.IsUnlocked(finalGuest, circuits, veteran);
        veteran.LibraryOpen = true;
        for (int r = 0; r < library.Rounds.Count; r++) veteran.LibraryWins[library.FinalKey(r)] = 1;
        bool openAlone = guests.All.All(g => GuestList.IsUnlocked(g, circuits, veteran));
        var tagTooEarly = guests.All.Where(g => GuestList.IsTagUnlocked(g, circuits, veteran)).Select(g => g.Character.Name).ToList();
        for (int mi = 0; mi < circuits.TagCups[0].Matches.Count; mi++) veteran.MatchWins[Circuits.TagKey(0, mi)] = 1;
        var tagStillShut = guests.All.Where(g => g.HasTag && !GuestList.IsTagUnlocked(g, circuits, veteran)).Select(g => g.Character.Name).ToList();
        Check($"no guest's tag duel opens before {circuits.TagCups[0].Name} is cleared, and clearing it opens them all (too early: {string.Join(", ", tagTooEarly)}; still shut: {string.Join(", ", tagStillShut)}; bad rules: {string.Join(", ", badTagRules)})",
            openAlone && tagTooEarly.Count == 0 && tagStillShut.Count == 0 && badTagRules.Count == 0);
        // A guest tag duel already won stays open (older saves from before the gate).
        var oldTagWinner = Profile.New(starter, cards, printings);
        foreach (var l in ChainLessons.All) oldTagWinner.Seen.Add(l.SeenKey);
        oldTagWinner.MatchWins[GuestList.Key(lessonGuest, true)] = 1;
        Check($"a guest tag duel you've already won stays open", GuestList.IsTagUnlocked(lessonGuest, circuits, oldTagWinner));
        var tagGuest = guests.All.First(g => g.Unlock.StartsWith("tagwins"));
        Check($"{tagGuest.Character.Name} is locked until enough tag wins ({GuestList.UnlockText(tagGuest, student)})", !GuestList.IsUnlocked(tagGuest, circuits, student));
        int guestDp = student.Dp;
        var guestWin = GuestList.Record(student, circuits, lessonGuest, false, 0);
        var guestAgain = GuestList.Record(student, circuits, lessonGuest, false, 0);
        Check($"a guest win pays the prize once ({guestWin.Summary} / {guestAgain.Summary}) and counts against them",
            guestWin.FirstWin && !guestAgain.FirstWin && guestWin.Dp == guestAgain.Dp + lessonGuest.Prize && student.WinsAgainst(lessonGuest.Character.Id) == 2 && student.Dp > guestDp);
        var otherGuest = guests.All.First(g => g != lessonGuest);
        var guestLoaned = GuestList.Record(student, circuits, otherGuest, false, 0, loaner: true);
        Check($"a guest win with a loaner pays half the duel's DP and the whole prize ({guestLoaned.Summary})",
            guestLoaned.Dp == (int)Math.Round(guestAgain.Dp * Loaners.DpShare) + otherGuest.Prize);
        IlseChecks(Check, root, cards, printings, roster, circuits, library, guests, starter, finalLockedForVeteran, veteran, story, alleyData);
        VeraChecks(Check, root, cards, printings, roster, circuits, library, guests, starter, story, scouting, scoutFiles, TriggerOk);
        Check("New Game+ resets the circuits and keeps cards, DP, level and tokens", reloaded.MatchWins.Count == 0 && reloaded.Cycle == 1
            && reloaded.Collection.Total == cardsBefore && reloaded.Dp == dpBefore && reloaded.Level == 10);
        int dp = profile.Dp;
        var loss = circuits.Record(profile, 0, 0, 1);
        Check($"a loss pays {loss.Dp} DP", profile.Dp == dp + Circuits.LossDp);
        var w0 = circuits.Record(profile, 0, 0, 0);
        var w1 = circuits.Record(profile, 0, 1, 0);
        Check($"two of three matches won: circuit not cleared yet ({w1.Summary})", !circuits.IsCleared(0, profile) && !circuits.IsUnlocked(1, profile));
        int tokensBefore = profile.Tokens;
        var w2 = circuits.Record(profile, 0, 2, 0);
        Check($"third win clears it and opens the next ({w2.Summary})",
            circuits.IsCleared(0, profile) && circuits.IsUnlocked(1, profile) && w2.ClearedCircuit != null && w2.Dp == circuits.WinDp(0) + Circuits.FirstWinBonus + circuits.All[0].Prize);
        Check($"clearing a circuit pays a Card Token ({w2.Tokens})",
            w2.Tokens == Circuits.ClearTokens && profile.Tokens == tokensBefore + w2.Tokens + w2.LevelUps.Sum(u => u.Tokens));
        var again = circuits.Record(profile, 0, 0, 0);
        Check($"replaying a cleared match pays without bonus or prize ({again.Summary})", again.Dp == circuits.WinDp(0) && again.ClearedCircuit == null);

        // The 3-copy cap: a 4th copy becomes DP, or replaces a less rare copy.
        var c3 = new Collection();
        uint card = 89631139;
        for (int i = 0; i < 3; i++) c3.AddPulled(card, Rarity.Common);
        var extra = c3.AddPulled(card, Rarity.Common);
        Check($"a 4th common copy is sold for {extra.Dp} DP", !extra.Kept && extra.Dp == Collection.SellValue(Rarity.Common) && c3.Count(card) == 3);
        // A pack of nothing but extra copies must never pay back its price (or packs of a finished set print DP).
        var payback = catalog.Sets.Select(set =>
        {
            var packRng = new Random(7);
            double dp = 0;
            // A collection that already has 3 of everything in the set.
            var full = new Profile { Dp = 0 };
            foreach (var code in set.Pool.Values.SelectMany(c => c).Distinct()) full.Collection.Add(code, Rarity.StarlightRare, 3);
            for (int i = 0; i < 100; i++) { full.OpenPack(set, packRng, out var refund); dp += refund; }
            return (set, average: dp / 100 / set.Price);
        }).OrderByDescending(x => x.average).First();
        Check($"a pack of extra copies pays back at most {payback.average:P0} of its price ({payback.set.Info.Name})", payback.average <= 0.5);
        var upgrade = c3.AddPulled(card, Rarity.SecretRare);
        Check($"a rarer 4th copy replaces a common (sold for {upgrade.Dp} DP)",
            upgrade.Kept && upgrade.Replaced == Rarity.Common && c3.Count(card) == 3 && c3.Best(card) == Rarity.SecretRare);
        var cheaper = c3.AddPulled(card, Rarity.Common);
        Check("a 4th copy no rarer than the rest is sold", !cheaper.Kept && c3.Count(card) == 3);

        foreach (var pull in PackCatalog.Open(catalog.Sets[0], rng)) profile.Collection.AddPulled(pull.Code, pull.Rarity);
        profile.PacksOpened = 1;
        var path = Path.Combine(Path.GetTempPath(), $"ygo-profile-{Guid.NewGuid():N}.txt");
        profile.Save(path);
        profile.Save(path); // twice: the second save replaces the first (and keeps a .bak)
        var loaded = Profile.Load(path)!;
        Check("profile round-trips (DP, wins, losses, deck, packs, every card at its rarity)",
            loaded.Dp == profile.Dp && loaded.Deck == profile.Deck && loaded.PacksOpened == 1
            && loaded.MatchWins.Count == 3 && loaded.HasWon(0, 2) && loaded.MatchWins[Profile.MatchKey(0, 0)] == 2
            && loaded.Collection.Total == profile.Collection.Total
            && profile.Collection.Codes.All(c => loaded.Collection.Copies(c).SequenceEqual(profile.Collection.Copies(c))));
        File.Delete(path);
        File.Delete(path + ".bak");

        // Saves from before 2026-10-28's circuits (Expert Series: Fall Format, Master League: October 2026, the Fall
        // Format Tag Cup): their position-based match keys move to the same circuits, by name.
        int Index(string name) => circuits.All.ToList().FindIndex(c => c.Name == name);
        int Cup(string name) => circuits.TagCups.ToList().FindIndex(c => c.Name == name);
        int houston = Index("Master League: Houston 2026"), gauntlet = Index("Tier 0 Gauntlet"), master = Cup("Master Tag Cup");
        Check($"the October 2026 circuits are in place (Fall Format before Link Wars, October 2026 before the Gauntlet, the Fall Format Tag Cup after its circuit)",
            Index("Expert Series: Fall Format") == Index("Master League: Link Wars") - 1 && Index("Master League: October 2026") == gauntlet - 1
            && circuits.TagCups[Cup("Fall Format Tag Cup")].After == Index("Expert Series: Fall Format"));
        var old = Profile.New(starter, cards, printings);
        old.MatchWins[Profile.MatchKey(houston - 1, 2)] = 1;      // Houston was one place earlier (before Fall Format)
        old.MatchWins[Profile.MatchKey(gauntlet - 2, 0)] = 3;     // the Gauntlet two places earlier
        old.MatchWins[Profile.MatchKey(0, 1)] = 1;                // the Beginner Circuit, unmoved
        old.MatchWins[Circuits.TagKey(master - 1, 0)] = 1;        // the Master Tag Cup one place earlier
        old.LossStreaks[Circuits.TagKey(master - 1, 2)] = 4;
        old.LossStreaks["guest:seraph"] = 2;
        circuits.Migrate(old);
        Check("an older save's wins move to the same circuits and Tag Cups (Houston, the Gauntlet, the Master Tag Cup; the first circuit and guests unmoved)",
            old.HasWon(houston, 2) && old.MatchWins[Profile.MatchKey(gauntlet, 0)] == 3 && old.HasWon(0, 1) && old.MatchWins.Count == 4
            && old.MatchWins.ContainsKey(Circuits.TagKey(master, 0)) && old.LossStreaks[Circuits.TagKey(master, 2)] == 4 && old.LossStreaks["guest:seraph"] == 2);
        var layoutPath = Path.Combine(Path.GetTempPath(), $"ygo-layout-{Guid.NewGuid():N}.txt");
        old.Save(layoutPath);
        var migrated = Profile.Load(layoutPath)!;
        circuits.Migrate(migrated);
        Check("a migrated save records its layout and isn't moved again",
            migrated.CircuitLayout!.Count == circuits.All.Count && migrated.HasWon(houston, 2) && migrated.MatchWins[Profile.MatchKey(gauntlet, 0)] == 3);
        File.Delete(layoutPath);
        File.Delete(layoutPath + ".bak");

        // Copies, not counts: every copy has its own rarity, grade, seed and id.
        var newSave = Profile.New(starter, cards, printings);
        var starterCopies = newSave.Collection.All.ToList();
        Check($"a new save's {starterCopies.Count} starter copies are Raw Near Mint (8) with their own ids and seeds",
            starterCopies.All(c => c.Grade == Grades.Fresh && c.Source == CopySource.Starter)
            && starterCopies.Select(c => c.Id).Distinct().Count() == starterCopies.Count && starterCopies.Select(c => c.Seed).Distinct().Count() == starterCopies.Count);

        // Grades follow the plan's odds and never reroll: the same source and seed always give the same grade.
        int[] Spread(CopySource source) => Enumerable.Range(0, 20000).Select(i => Grades.Roll(source, Hash.Mix(12345, (uint)i))).GroupBy(g => g)
            .Aggregate(new int[11], (acc, g) => { acc[g.Key] = g.Count(); return acc; });
        var packSpread = Spread(CopySource.Pack);
        var usedSpread = Spread(CopySource.Used);
        double Share(int[] spread, int lo, int hi) => Enumerable.Range(lo, hi - lo + 1).Sum(g => spread[g]) / 20000.0;
        Check($"pack grades: {Share(packSpread, 10, 10):P0} Gem Mint, {Share(packSpread, 9, 9):P0} Mint, {Share(packSpread, 7, 8):P0} Near Mint, "
              + $"{Share(packSpread, 1, 2):P0} Damaged; used copies {Share(usedSpread, 1, 4):P0} Played or worse; rolls repeat",
            Math.Abs(Share(packSpread, 10, 10) - 0.10) < 0.01 && Math.Abs(Share(packSpread, 9, 9) - 0.35) < 0.015 && Math.Abs(Share(packSpread, 7, 8) - 0.45) < 0.015
            && packSpread[1] + packSpread[2] == 0 && Math.Abs(Share(usedSpread, 1, 4) - 0.26) < 0.015
            && Enumerable.Range(0, 200).All(i => Grades.Roll(CopySource.Pack, (uint)i * 7919) == Grades.Roll(CopySource.Pack, (uint)i * 7919)));
        Check("grade labels: 10 Gem Mint, 9 Mint, 8 Near Mint, 6 Excellent, 3 Played, 1 Damaged; a Raw copy shows only its word",
            Grades.Label(10) == "10 Gem Mint" && Grades.Label(9) == "9 Mint" && Grades.Label(8) == "8 Near Mint" && Grades.Label(6) == "6 Excellent"
            && Grades.Label(3) == "3 Played" && Grades.Label(1) == "1 Damaged" && Grades.RawLabel(7) == "Raw · Near Mint");

        // An old save's counts become copies graded 7-10, the same ones every time it loads; the new save keeps them exactly.
        var oldPath = Path.Combine(Path.GetTempPath(), $"ygo-profile-{Guid.NewGuid():N}.txt");
        newSave.Save(oldPath);
        var oldLines = File.ReadAllLines(oldPath).Where(l => !l.StartsWith("copy ") && !l.StartsWith("copies ")).ToList();
        oldLines.AddRange(newSave.Collection.Codes.SelectMany(code => newSave.Collection.Copies(code).Select(e => $"card {code} {e.rarity} {e.copies}")));
        File.WriteAllLines(oldPath, oldLines);
        var firstLoad = Profile.Load(oldPath)!;
        var secondLoad = Profile.Load(oldPath)!;
        string Fingerprint(Collection c) => string.Join(";", c.All.OrderBy(x => x.Id).Select(x => $"{x.Code}/{x.Rarity}/{x.Grade}/{x.Seed}/{x.Id}/{x.Source}"));
        firstLoad.Save(oldPath);
        var resaved = Profile.Load(oldPath)!;
        File.Delete(oldPath);
        File.Delete(oldPath + ".bak");
        var oldCopies = firstLoad.Collection.All.ToList();
        Check($"an old save's {oldCopies.Count} cards become copies graded 7-10 ({string.Join(", ", oldCopies.GroupBy(c => c.Grade).OrderByDescending(g => g.Key).Select(g => $"{g.Count()} at {g.Key}"))}), "
              + "the same on every load, and saved copy by copy after",
            oldCopies.Count == newSave.Collection.Total && oldCopies.All(c => c.Grade >= 7 && c.Grade <= 10 && c.Source == CopySource.Old)
            && string.Join(";", firstLoad.Collection.All.OrderBy(x => x.Id).Select(x => $"{x.Code}/{x.Grade}/{x.Seed}"))
               == string.Join(";", secondLoad.Collection.All.OrderBy(x => x.Id).Select(x => $"{x.Code}/{x.Grade}/{x.Seed}"))
            && Fingerprint(resaved.Collection) == Fingerprint(firstLoad.Collection) && resaved.Collection.NextId == firstLoad.Collection.NextId
            && Codes(firstLoad.Collection).All(code => firstLoad.Collection.Copies(code).SequenceEqual(newSave.Collection.Copies(code))));
        IEnumerable<uint> Codes(Collection c) => c.Codes;

        // A deck's slots take your copies best first, the same every time; slots past what you own have none.
        var shelf = new Collection { Salt = 99 };
        shelf.Add(card, Rarity.Common, 2, CopySource.Pack);
        shelf.Add(card, Rarity.SecretRare, 1, CopySource.Pack);
        var trio = new Deck { Name = "trio" };
        trio.Main.AddRange(new[] { card, card, card, card });
        var slots = shelf.Assign(trio, cards);
        var again2 = shelf.Assign(trio, cards);
        Check("a deck's 4 slots of a card take the Secret Rare, then the two Commons (better grade first), then nothing; the same every time",
            slots.Count == 4 && slots[0]!.Rarity == Rarity.SecretRare && slots[1]!.Rarity == Rarity.Common && slots[2]!.Rarity == Rarity.Common
            && slots[1]!.Grade >= slots[2]!.Grade && slots[3] == null && slots.Select(c => c?.Id).SequenceEqual(again2.Select(c => c?.Id)));

        // Replays keep the looks of your copies (format 3).
        var looked = new Ygo.Engine.Replays.Replay { Decks = new[] { trio.Clone(), new Deck() } };
        looked.Looks[0] = slots.Select(c => c?.Look ?? default).ToArray();
        var reread3 = Ygo.Engine.Replays.Replay.FromCode(looked.ToCode())!;
        Check("a replay keeps each slot's rarity, grade and wear seed (and an empty slot stays empty)",
            reread3.Looks[0] != null && reread3.Looks[1] == null
            && reread3.Looks[0]!.Select(l => (l.Rarity, l.Grade, l.Seed)).SequenceEqual(looked.Looks[0]!.Select(l => (l.Rarity, l.Grade, l.Seed)))
            && !reread3.Looks[0]![3].IsCopy);

        // ---- The grade economy -------------------------------------------------------------------------------
        CardCopy Copy(uint code, Rarity rarity, int grade, int slab = 0, int paid = 0, int id = 1) =>
            new CardCopy { Code = code, Rarity = rarity, Grade = grade, Slab = slab, Paid = paid, Id = id, Seed = (uint)(id * 7919) };
        uint potOfGreed = 55144522;
        var greedCommonest = printings.Commonest(potOfGreed, 0);
        Check($"one price: a Raw Near Mint copy is the shop's price ({Pricing.V(Copy(potOfGreed, greedCommonest, 8), greedCommonest)} = {RecipeShop.CopyPrice(greedCommonest)}), "
              + $"a Starlight Gem Mint slab {Pricing.V(Copy(1, Rarity.StarlightRare, 10, 10), Rarity.Common):N0}, a Secret copy of a Common card "
              + $"{Pricing.V(Copy(1, Rarity.SecretRare, 8), Rarity.Common)} Raw and {Pricing.V(Copy(1, Rarity.SecretRare, 10, 10), Rarity.Common):N0} as a 10",
            Pricing.V(Copy(potOfGreed, greedCommonest, 8), greedCommonest) == RecipeShop.CopyPrice(greedCommonest)
            && Pricing.V(Copy(1, Rarity.StarlightRare, 10, 10), Rarity.Common) == 15000
            && Pricing.V(Copy(1, Rarity.SecretRare, 8), Rarity.Common) == 400 && Pricing.V(Copy(1, Rarity.SecretRare, 10, 10), Rarity.Common) == 2400
            && Pricing.V(Copy(1, Rarity.Common, 10), Rarity.Common) == Pricing.V(Copy(1, Rarity.Common, 9), Rarity.Common));
        Check("Raw 9 and 10 both read Mint (and price alike); a slab shows its number",
            Copy(1, Rarity.Common, 10).Shown == 9 && Grades.Describe(Copy(1, Rarity.Common, 10)) == "Raw · Mint: sharp corners"
            && Grades.Describe(Copy(1, Rarity.Common, 10, 10)) == "Grade 10 · Gem Mint: flawless");

        // The Grading Desk.
        var grader = Profile.New(starter, cards, printings);
        grader.Dp = 100000;
        var mine = grader.Collection.All.First(c => grader.Collection.Count(c.Code) == 1);
        int none(uint _) => 0;
        int fee = GradingDesk.FeeFor(grader.Collection, mine, express: false);
        int sent = GradingDesk.Submit(grader, mine, express: false, none);
        bool away = mine.AtDesk && grader.Collection.Count(mine.Code) == 0 && grader.Collection.Owned(mine.Code) == 1 && grader.Dp == 100000 - fee && mine.Paid == fee;
        var starterDeck = new Deck { Name = "probe" };
        starterDeck.Main.Add(mine.Code);
        bool skipped = grader.Collection.Assign(starterDeck, cards)[0] == null;
        for (int d = 0; d < 2; d++) grader.NoteResult("guest:probe", 0);
        bool notYet = !GradingDesk.IsReady(grader, mine) && GradingDesk.DuelsLeft(grader, mine) == 1;
        grader.NoteResult("guest:probe", 0);
        int slab = GradingDesk.Collect(grader, mine);
        Check($"a standard submission ({fee} DP) leaves for 3 duels (not playable meanwhile) and comes back a {slab} (true {mine.Grade})",
            sent == 0 && away && skipped && notYet && slab >= Math.Max(1, mine.Grade - 1) && slab <= Math.Min(10, mine.Grade + 1) && mine.IsSlab && !mine.AtDesk);
        GradingDesk.Crack(grader, mine);
        int floor = mine.Floor;
        int regraded = GradingDesk.Submit(grader, mine, express: true, none);
        Check($"crack (free) and resubmit express (twice the fee): back now at {regraded}, never below the cracked {floor}",
            regraded >= floor && mine.Slab == regraded && !mine.AtDesk);
        // The odds: over many copies, the true grade 75%, one higher 10%, one lower 15%.
        int same = 0, higher = 0, lower = 0;
        var odds = new Profile { Dp = 10000000 };
        for (int i = 0; i < 4000; i++)
        {
            var c = Copy(1, Rarity.Common, 5, paid: 10, id: i + 1);
            c.Seed = Hash.Mix(99, (uint)i);
            odds.Collection.Insert(c);
            int got = GradingDesk.Submit(odds, c, express: true, none);
            if (got == 5) same++; else if (got == 6) higher++; else if (got == 4) lower++;
        }
        Check($"grading odds over 4,000 copies: {same / 40.0:0}% true grade, {higher / 40.0:0}% one higher, {lower / 40.0:0}% one lower",
            Math.Abs(same / 4000.0 - 0.75) < 0.03 && Math.Abs(higher / 4000.0 - 0.10) < 0.02 && Math.Abs(lower / 4000.0 - 0.15) < 0.02);
        Check("the fee goes by value, never grade: 100 / 300 / 1,000 / 2,500",
            GradingDesk.Fee(40) == 100 && GradingDesk.Fee(400) == 300 && GradingDesk.Fee(1500) == 1000 && GradingDesk.Fee(2500) == 2500);
        var deckNeeds = Profile.New(starter, cards, printings);
        deckNeeds.Dp = 100000;
        var needed = deckNeeds.Collection.All.First();
        Check("a standard submission that would leave a saved deck short is refused (express goes)",
            GradingDesk.Blocked(deckNeeds, needed, false, c => deckNeeds.Collection.Count(c)) != null
            && GradingDesk.Blocked(deckNeeds, needed, true, c => deckNeeds.Collection.Count(c)) == null);

        // Selling: a quarter of the value, at most half of what you paid; gifts pay nothing.
        var seller = Profile.New(starter, cards, printings);
        var gift = seller.Collection.All.First();
        var boughtUltra = Copy(1, Rarity.UltraRare, 8, paid: 400, id: 90001);
        seller.Collection.Insert(boughtUltra);
        int dpBeforeSale = seller.Dp, paidOut = SellDesk.Sell(seller, boughtUltra, none);
        Check($"the Sell desk pays min(25% of value, half of what you paid): an Ultra worth {Pricing.V(boughtUltra, Rarity.UltraRare)} bought for 400 sells for {paidOut}; a starter copy is a gift",
            paidOut == Math.Min(Pricing.V(boughtUltra, Rarity.UltraRare) / 4, 200) && seller.Dp == dpBeforeSale + paidOut
            && SellDesk.Blocked(seller, gift, none) == "A gift: not for sale." && SellDesk.Sell(seller, gift, none) == 0);

        // No loop makes DP: buy used at its value, grade it, sell it.
        var trader = Profile.New(starter, cards, printings);
        trader.Dp = 1000000;
        int startDp = trader.Dp, worstReturn = 0;
        var poolCodes = cards.All.Where(c => c.Alias == 0 && printings.Of(c.Code, 0).Length > 1).Take(300).Select(c => c.Code).ToList();
        for (int round = 0; round < 6; round++)
        {
            foreach (var listing in UsedMarket.Listings(trader, poolCodes, 2026, printings, cards).Where(l => !l.Sold).Take(6))
            {
                if (UsedMarket.Buy(trader, listing) < 0) continue;
                var got = trader.Collection.CopiesOf(listing.Copy.Code).First(c => c.Source == CopySource.Used && c.Paid >= listing.Price);
                GradingDesk.Submit(trader, got, express: true, none);
                SellDesk.Sell(trader, got, none);
            }
            for (int d = 0; d < UsedMarket.Every; d++) trader.NoteResult("guest:probe", 0);
        }
        Check($"buy used, grade, sell, over 6 market periods: {trader.Dp - startDp:N0} DP (it never pays)", trader.Dp < startDp);

        // Packs: each pull carries its share of the price; extras pay back at most half.
        var packer = Profile.New(starter, cards, printings);
        packer.Dp = 100000;
        int idBefore = packer.Collection.NextId;
        packer.OpenPack(catalog.Sets[0], new Random(3), out _);
        var pulled = packer.Collection.All.Where(c => c.Id >= idBefore).ToList();
        Check($"a pack's pulls carry its price between them ({pulled.Sum(c => c.Paid)} of {catalog.Sets[0].Price} DP on the {pulled.Count} kept)",
            pulled.Sum(c => c.Paid) <= catalog.Sets[0].Price && pulled.All(c => c.Paid > 0));

        // The used market.
        var shopper2 = Profile.New(starter, cards, printings);
        shopper2.Dp = 1000000;
        var first = UsedMarket.Listings(shopper2, poolCodes, 2026, printings, cards);
        var firstAgain = UsedMarket.Listings(shopper2, poolCodes, 2026, printings, cards);
        for (int d = 0; d < UsedMarket.Every; d++) shopper2.NoteResult("guest:probe", 0);
        var next = UsedMarket.Listings(shopper2, poolCodes, 2026, printings, cards);
        string Key(List<Listing> l) => string.Join(",", l.Select(x => $"{x.Copy.Code}/{x.Copy.Rarity}/{x.Copy.Grade}/{x.Copy.Slab}"));
        Check($"the used market: {first.Count} listings ({first.Count(l => !l.Copy.IsSlab)} Raw, {first.Count(l => l.Copy.IsSlab)} slabs) at their value; "
              + "the same until 5 duels pass, then new ones",
            first.Count == UsedMarket.Size && first.Count(l => l.Copy.IsSlab) == UsedMarket.Size - UsedMarket.RawSlots
            && first.All(l => l.Price == Pricing.V(l.Copy, printings.Commonest(l.Copy.Code, 0))) && Key(first) == Key(firstAgain) && Key(first) != Key(next));
        var listingToBuy = next.First(l => UsedMarket.Blocked(shopper2, l) == null);
        UsedMarket.Buy(shopper2, listingToBuy);
        var marketSave = Path.Combine(Path.GetTempPath(), $"ygo-profile-{Guid.NewGuid():N}.txt");
        shopper2.Save(marketSave);
        var shopperBack = Profile.Load(marketSave)!;
        File.Delete(marketSave);
        Check("a bought listing stays sold after saving and loading; the copy keeps its grade, slab and what you paid",
            UsedMarket.Listings(shopperBack, poolCodes, 2026, printings, cards)[listingToBuy.Slot].Sold
            && shopperBack.Collection.All.Any(c => c.Source == CopySource.Used && c.Grade == listingToBuy.Copy.Grade && c.Slab == listingToBuy.Copy.Slab && c.Paid == listingToBuy.Price));
        var orderCard = poolCodes.First(c => UsedMarket.Orderable(c, printings, cards).Count > 0);
        var orderRarity = UsedMarket.Orderable(orderCard, printings, cards).Last();
        int orderPrice = UsedMarket.OrderPrice(orderCard, orderRarity, printings, cards);
        var ordered = UsedMarket.Order(shopper2, orderCard, orderRarity, 2026, printings, cards);
        Check($"a special order: any printing but the commonest, Raw Near Mint, at its basis plus half ({orderPrice:N0} DP for {orderRarity})",
            ordered != null && ordered.Grade == 8 && !ordered.IsSlab && ordered.Paid == orderPrice
            && !UsedMarket.Orderable(orderCard, printings, cards).Contains(printings.Commonest(orderCard, 0)));

        // Card versions phase 1 (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md F1, 5.1-5.3): a table's texts date decides where an
        // original plays as printed. 3 pre-errata Chaos Emperor Dragons are legal in a Master League deck (CED is unlisted)
        // but play as 3 current CEDs: the codes the duel (and its replay) gets. The Goat Cup rules an original Sangan as
        // its GOAT entry; the alley plays it as printed.
        Console.WriteLine("\nCard texts by table:");
        var textsFile = Path.Combine(root, "campaign", Ygo.Engine.DeckBuilding.CardTexts.File);
        var cardTexts = File.Exists(textsFile) ? Ygo.Engine.DeckBuilding.CardTexts.Load(textsFile, cards) : Ygo.Engine.DeckBuilding.CardTexts.Parse(TextsCheck.Fixture, cards);
        if (!File.Exists(textsFile)) Console.WriteLine("         (no campaign/texts.txt yet: the spec's rows)");
        var allLists = lists.ToList();
        var masterLeague = circuits.All.Last(c => c.Banlist == "TCG October 2026");
        var masterList = allLists.First(l => l.Name == masterLeague.Banlist);
        const uint CedPre = 511000819, CedBase = 82301904, SanganPre = 511002631, SanganGoat = 504700178, SanganBase = 26202165;
        var exploit = Deck.LoadYdk(Path.Combine(root, "decks", "modern.ydk"));
        exploit.Main.RemoveRange(0, 3);
        exploit.Main.AddRange(new[] { CedPre, CedPre, CedPre });
        var masterTexts = Ygo.Engine.DeckBuilding.TextRules.ForList(masterList, allLists);
        var played = Ygo.Engine.DeckBuilding.PlayTexts.ForTable(exploit, null, masterTexts, cardTexts, cards, out var downgraded);
        // As the duel records it (DuelController: ForTable before Assign, then the replay keeps the decks played).
        var cedReplay = Ygo.Engine.Replays.Replay.FromCode(new Ygo.Engine.Replays.Replay { Decks = new[] { played.Clone(), exploit.Clone() } }.ToCode());
        Check($"{masterLeague.Name} ({masterList.Name}, {masterTexts.Describe()}): a deck with 3 pre-errata Chaos Emperor Dragons is legal and plays 3 current ones",
            Ygo.Engine.DeckBuilding.DeckRules.Validate(exploit, masterList, cards).All(p => Ygo.Engine.DeckBuilding.Banlist.BaseCode(p.Code, cards) != CedBase)
            && played.Main.Count(c => c == CedBase) == 3 && !played.Main.Contains(CedPre) && downgraded.SequenceEqual(new[] { CedBase })
            && exploit.Main.Count(c => c == CedPre) == 3 && cedReplay?.Decks[0].Main.Count(c => c == CedBase) == 3 && !cedReplay.Decks[0].Main.Contains(CedPre));
        int goatCup = library.Cups.ToList().FindIndex(c => c.Pool == "2005-09");
        var goatRules = library.Rules(goatCup, allLists).Texts;
        var original = new CardCopy { Code = SanganBase, Text = SanganPre };
        var current = new CardCopy { Code = SanganBase };
        Check($"the {library.Cups[goatCup].Name} ({goatRules.Describe()}) plays an original Sangan as its GOAT entry, a current copy as current; so does Every Era round 1",
            goatRules.Goat && goatRules.AsOf == "2005-09" && library.RoundRules(0, allLists).Texts.Goat
            && Ygo.Engine.DeckBuilding.PlayTexts.PlayCode(SanganPre, original, goatRules, cardTexts, cards) == SanganGoat
            && Ygo.Engine.DeckBuilding.PlayTexts.PlayCode(SanganPre, current, goatRules, cardTexts, cards) == SanganBase
            && Ygo.Engine.DeckBuilding.PlayTexts.PlayCode(SanganPre, null, goatRules, cardTexts, cards) == SanganGoat
            && Enumerable.Range(0, library.Cups.Count).Count(c => library.Rules(c, allLists).Texts.Goat) == 1);
        var cupTexts = Enumerable.Range(0, library.Cups.Count).Select(c => (cup: library.Cups[c], rules: library.Rules(c, allLists).Texts)).ToList();
        var sanganCups = cupTexts.Where(c => Ygo.Engine.DeckBuilding.PlayTexts.PlayCode(SanganPre, original, c.rules, cardTexts, cards) == SanganPre).Select(c => c.cup.Pool).ToList();
        var alleyTexts = Alley.Rules(Ygo.Engine.DeckBuilding.Banlist.None).Texts;
        Check($"an original Sangan plays as printed in the cups through 2016-07 but Goat's ({string.Join(", ", sanganCups)}), in the alley, never under {masterList.Name}",
            sanganCups.Count > 0 && sanganCups.All(p => string.CompareOrdinal(p, "2005-09") > 0 && string.CompareOrdinal(p, "2016-07") <= 0)
            && cupTexts.Count(c => string.CompareOrdinal(c.cup.Pool, "2005-09") > 0 && string.CompareOrdinal(c.cup.Pool, "2016-07") <= 0) == sanganCups.Count
            && alleyTexts.Printed && Ygo.Engine.DeckBuilding.PlayTexts.PlayCode(SanganPre, original, alleyTexts, cardTexts, cards) == SanganPre
            && Ygo.Engine.DeckBuilding.PlayTexts.PlayCode(CedPre, null, alleyTexts, cardTexts, cards) == CedPre
            && Ygo.Engine.DeckBuilding.PlayTexts.PlayCode(SanganPre, original, masterTexts, cardTexts, cards) == SanganBase);

        failures += VersionChecks(cards, root, printings, catalog, cardTexts, allLists, masterTexts, alleyTexts);
        failures += UnlockChecks(cards, root, printings, catalog, roster, circuits, library, guests, eraLists, starter);
        failures += BracketChecks.Run(cards, root, printings, roster, library, starter);
        failures += LedgerCheck.Run(cards, root, printings, catalog, roster, circuits, library, guests, starter);
        failures += ShopTest.Run(cards, root, printings, catalog, roster, circuits, library, guests, starter);
        failures += AlleyTest.Run(cards, root, printings, roster, circuits, library, starter);
        failures += HeadlinerTest.Run(cards, root, printings, roster, circuits, library, starter);
        failures += LiveDuelTest.Run(cards, root, printings, catalog, roster, circuits, library, guests, starter);
        failures += StoryPlacesTest.Run(cards, root, library);
        // The Champion's Ladder (docs/dev/CHAMPIONS-LADDER-SPEC-2026-11-06.md section 9; also alone as --ladder-test).
        if (LadderTest.Run(cards, root) != 0) failures++;

        Console.WriteLine($"\n{(failures == 0 ? "All campaign checks passed." : $"{failures} campaign checks failed.")}");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Card versions phase 2 (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 4.2-4.4, 12): a deck whose original-text slots are not all
    /// backed by original copies is legal, and the slots without one play current text (a note, not a refusal); a pack's pull
    /// is stamped with the printing its set gave it, so an old set gives the original and a reprint shares nothing with it.
    /// </summary>
    private static int VersionChecks(ICardDataProvider cards, string root, PrintingIndex printings, PackCatalog catalog, Ygo.Engine.DeckBuilding.CardTexts texts,
        List<Ygo.Engine.DeckBuilding.Banlist> allLists, Ygo.Engine.DeckBuilding.TextRules masterTexts, Ygo.Engine.DeckBuilding.TextRules alleyTexts)
    {
        int failures = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        Console.WriteLine("\nCard versions on copies:");
        const uint SanganBase = 26202165, SanganPre = 511002631;
        Collection Owning(params (Rarity rarity, bool original)[] copies)
        {
            var col = new Profile().Collection;
            col.Texts = texts;
            col.Printings = printings;
            col.CommonestOf = c => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
            foreach (var (rarity, original) in copies)
                col.Add(SanganBase, rarity, 1, CopySource.Shop, 10, original ? "MRD-069" : "MRD-EN069", original ? "2002-06-26" : "2023-04-20");
            return col;
        }
        string Plays(Deck deck, Collection col, Ygo.Engine.DeckBuilding.TextRules rules, out List<uint> downgraded)
        {
            var copies = col.Assign(deck, cards);
            var played = Ygo.Engine.DeckBuilding.PlayTexts.ForTable(deck, copies, rules, texts, cards, out downgraded);
            return string.Join(",", played.Main.Concat(played.Extra).Select(c => c == SanganPre ? "orig" : c == SanganBase ? "cur" : c.ToString()));
        }

        // 2 original Sangan slots and 1 original copy (3 copies in all): legal; one slot plays current text.
        var deck2 = new Deck { Main = { SanganPre, SanganPre } };
        var one = Owning((Rarity.Common, true), (Rarity.Rare, false), (Rarity.Rare, false));
        var note = Ygo.Engine.Campaign.CollectionRules.TextShortfalls(deck2, one, texts, cards);
        var played2 = Plays(deck2, one, alleyTexts, out var down2);
        Check($"2 original Sangan slots with 1 original copy: the deck is legal ({Ygo.Engine.Campaign.CollectionRules.Validate(deck2, one, cards).Count} problems), the note says \"{note.FirstOrDefault()?.Message}\", "
              + $"and the table plays [{played2}] (1 original, 1 current)",
            Ygo.Engine.Campaign.CollectionRules.Validate(deck2, one, cards).Count == 0 && note.Count == 1 && note[0].Code == SanganBase
            && note[0].Message.Contains("Sangan") && note[0].Message.Contains("2 slots") && note[0].Message.Contains("1 original copy") && note[0].Message.Contains("1 plays its current text")
            && played2 == "orig,cur");
        var deck3 = new Deck { Main = { SanganPre, SanganPre, SanganPre } };
        var played3 = Plays(deck3, one, alleyTexts, out _);
        var note3 = Ygo.Engine.Campaign.CollectionRules.TextShortfalls(deck3, one, texts, cards);
        Check($"3 original slots with 2 current copies and 1 original: legal, one note (\"{note3.FirstOrDefault()?.Message}\"), Assign gives texts "
              + $"{string.Join(",", one.Assign(deck3, cards).Select(c => c!.Text == 0 ? "0" : c.Text.ToString()))}, plays [{played3}]",
            Ygo.Engine.Campaign.CollectionRules.Validate(deck3, one, cards).Count == 0 && note3.Count == 1 && note3[0].Message.Contains("3 slots") && note3[0].Message.Contains("2 play their current text")
            && one.Assign(deck3, cards).Select(c => c!.Text).SequenceEqual(new[] { SanganPre, 0u, 0u }) && played3 == "orig,cur,cur");
        var two = Owning((Rarity.Common, true), (Rarity.Rare, true), (Rarity.Rare, false));
        Check("2 original slots with 2 original copies: no note, both play the original",
            Ygo.Engine.Campaign.CollectionRules.TextShortfalls(deck2, two, texts, cards).Count == 0 && Plays(deck2, two, alleyTexts, out _) == "orig,orig");
        var none = Owning((Rarity.Rare, false), (Rarity.Rare, false), (Rarity.Rare, false));
        var noteNone = Ygo.Engine.Campaign.CollectionRules.TextShortfalls(deck2, none, texts, cards);
        Check($"2 original slots with no original copy: legal, one note (\"{noteNone.FirstOrDefault()?.Message}\"), both play current",
            Ygo.Engine.Campaign.CollectionRules.Validate(deck2, none, cards).Count == 0 && noteNone.Count == 1 && noteNone[0].Message.Contains("no original cop") && Plays(deck2, none, alleyTexts, out _) == "cur,cur");
        var short2 = Owning((Rarity.Common, true), (Rarity.Rare, false));
        Check("a deck short of copies altogether is Validate's problem, not a text note (3 Sangan, 2 owned)",
            Ygo.Engine.Campaign.CollectionRules.Validate(deck3, short2, cards).Count == 1 && Ygo.Engine.Campaign.CollectionRules.TextShortfalls(deck3, short2, texts, cards).Count == 0);
        var masterPlays = Plays(deck2, two, masterTexts, out var downMaster);
        Check($"where the table doesn't allow the original (TCG October 2026) the same 2 original copies play current ([{masterPlays}], downgraded {downMaster.Count})",
            masterPlays == "cur,cur" && downMaster.Contains(SanganBase));

        // Pack pulls (4.2): the pack's own set dates the printing; a reprint filed under the set's code doesn't make it current.
        string NameCode(string name) => cards.All.First(c => c.Name == name && c.Alias == 0).Code.ToString();
        uint Code(string name) => uint.Parse(NameCode(name));
        var mrd = catalog.Find("MRD");
        var mrdRarity = mrd?.Pool.FirstOrDefault(p => p.Value.Contains(SanganBase)).Key ?? Rarity.Common;
        var (mrdSet, mrdDate) = mrd == null ? ("", "") : Profile.PackPrint(mrd, new Pull(SanganBase, mrdRarity), printings);
        var sanganPrints = printings.Of(SanganBase, 0);
        Console.WriteLine($"         Sangan printings: {string.Join(", ", sanganPrints.OrderBy(p => p.SetCode, StringComparer.Ordinal).Select(p => $"{p.SetCode} {Rarities.Name(p.Rarity)} {printings.DateOf(p)}"))}");
        var col0 = Owning();
        var oldCopy = col0.NewCopy(SanganBase, mrdRarity, CopySource.Pack);
        col0.Stamp(oldCopy, mrdSet, mrdDate);
        Check($"a Metal Raiders pull of Sangan is the 2002 printing and carries the original text ({mrdSet} {mrdDate}, text {oldCopy.Text})",
            mrd != null && mrdSet.StartsWith("MRD-") && !mrdSet.StartsWith("MRD-EN") && mrdDate.StartsWith("2002-06") && oldCopy.Text == SanganPre && oldCopy.Printed == "2002-06");
        var reprints = sanganPrints.Where(p => p.SetCode.StartsWith("MRD-EN")).ToList();
        var reprint = col0.NewCopy(SanganBase, Rarity.Common, CopySource.Pack);
        if (reprints.Count > 0) col0.Stamp(reprint, reprints[0], printings);
        Check($"the 25th Anniversary reprint ({string.Join(", ", reprints.Select(p => $"{p.SetCode} dated {printings.DateOf(p)}"))}) stamps current: text {reprint.Text}, printed {reprint.Printed}",
            reprints.Count > 0 && reprints.All(p => printings.DateOf(p).StartsWith("2023-04")) && reprint.Text == 0 && reprint.Printed == "2023-04");
        var newest = CopyPrints.Newest(SanganBase, Rarity.Common, 9999, printings, cards);
        var newestCopy = col0.NewCopy(SanganBase, Rarity.Common, CopySource.Shop);
        col0.Stamp(newestCopy, newest, printings);
        Check($"a copy bought new (the newest printing, {newest?.SetCode} {(newest is Printing np ? printings.DateOf(np) : "")}) is current text", newest != null && newestCopy.Text == 0);
        // Real packs: every Sangan, Witch and Catapult Turtle that comes out of Metal Raiders is the 2002 printing with the original text.
        int seen = 0, original = 0, reprintSets = 0;
        var cardsOfMrd = new[] { SanganBase, Code("Witch of the Black Forest"), Code("Catapult Turtle") };
        if (mrd != null)
            for (int k = 0; k < 400; k++)
            {
                var p = new Profile { Dp = 10_000_000 };
                p.Collection.Texts = texts; p.Collection.Printings = printings;
                p.Collection.CommonestOf = c => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
                foreach (var o in p.OpenPack(mrd, new Random(k), out _))
                    if (cardsOfMrd.Contains(o.pull.Code) && o.added.Copy is CardCopy c)
                    {
                        seen++;
                        if (c.Text != 0 && c.Printed.StartsWith("2002")) original++;
                        if (c.Set.StartsWith("MRD-EN")) reprintSets++;
                    }
            }
        Check($"400 Metal Raiders packs: {seen} pulls of Sangan, Witch of the Black Forest or Catapult Turtle, {original} with the original text, {reprintSets} filed under a reprint code",
            seen > 0 && original == seen && reprintSets == 0);

        // Duelist Pack boosters: the 2009/2010 printing, while the 2013/2017 reprint rows (same code) date as the reprint.
        var dpyg = catalog.Find("DPYG");
        var dpkb = catalog.Find("DPKB");
        var cases = new List<(PackSet? set, string name, uint code, string reprintSet, string reprintDate)>
        {
            (dpyg, "Stronghold the Moving Fortress", Code("Stronghold the Moving Fortress"), "DPYG-EN030", "2013-10"),
            (dpyg, "Big Shield Gardna", Code("Big Shield Gardna"), "DPYG-EN009", "2013-10"),
            (dpyg, "Catapult Turtle", Code("Catapult Turtle"), "DPYG-EN006", "2017-09"),
            (dpyg, "Brain Control", Code("Brain Control"), "DPYG-EN024", "2017-09"),
            (dpkb, "Ring of Destruction", Code("Ring of Destruction"), "DPKB-EN036", "2017-09"),
            (dpkb, "Crush Card Virus", Code("Crush Card Virus"), "DPKB-EN039", "2017-09"),
        };
        foreach (var (set, name, code, reprintSet, reprintDate) in cases)
        {
            var baseCode = Ygo.Engine.DeckBuilding.Banlist.BaseCode(code, cards);
            var rarity = set?.Pool.FirstOrDefault(p => p.Value.Contains(baseCode)).Key ?? Rarity.Common;
            var (setCode, date) = set == null ? ("", "") : Profile.PackPrint(set, new Pull(baseCode, rarity), printings);
            var boosterCopy = col0.NewCopy(baseCode, rarity, CopySource.Pack);
            col0.Stamp(boosterCopy, setCode, date);
            var row = printings.Of(baseCode, 0).Cast<Printing?>().FirstOrDefault(p => p!.Value.SetCode == reprintSet);
            var reprintCopy = col0.NewCopy(baseCode, row?.Rarity ?? rarity, CopySource.Shop);
            if (row is Printing rp) col0.Stamp(reprintCopy, rp, printings);
            Check($"{name}: a {set?.Info.Code} booster pull is {setCode} dated {date} (text {boosterCopy.Text}, original), the {reprintSet} row dates as {(row is Printing r2 ? printings.DateOf(r2) : "?")} (text {reprintCopy.Text}, current)",
                set != null && setCode == reprintSet && date.StartsWith(set.Info.Date.Substring(0, 4)) && date.Length >= 7 && string.CompareOrdinal(date, "2011") < 0
                && boosterCopy.Text != 0 && row != null && printings.DateOf(row.Value).StartsWith(reprintDate) && reprintCopy.Text == 0);
        }
        // Invasion of Chaos: a booster's Chaos Emperor Dragon is the 2004 printing (IOC-000, the original text), not the 2023 IOC-EN000 reprint.
        var ioc = catalog.Find("IOC");
        const uint CedBase = 82301904;
        var iocRarity = ioc?.Pool.FirstOrDefault(p => p.Value.Contains(CedBase)).Key ?? Rarity.SecretRare;
        var (iocSet, iocDate) = ioc == null ? ("", "") : Profile.PackPrint(ioc, new Pull(CedBase, iocRarity), printings);
        var iocCopy = col0.NewCopy(CedBase, iocRarity, CopySource.Pack);
        col0.Stamp(iocCopy, iocSet, iocDate);
        Check($"an Invasion of Chaos pull of Chaos Emperor Dragon is {iocSet} dated {iocDate}: the original text (text {iocCopy.Text})", ioc != null && !iocSet.StartsWith("IOC-EN") && iocDate.StartsWith("2004") && iocCopy.Text == 511000819);
        int dpSeen = 0, dpOriginal = 0;
        if (dpyg != null)
            for (int k = 0; k < 400; k++)
            {
                var p = new Profile { Dp = 10_000_000 };
                p.Collection.Texts = texts; p.Collection.Printings = printings;
                p.Collection.CommonestOf = c => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
                foreach (var o in p.OpenPack(dpyg, new Random(k), out _))
                    if (texts.Timeline(o.pull.Code).Any(t => t.Collectible) && o.added.Copy is CardCopy c)
                    {
                        dpSeen++;
                        if (c.Text != 0 && c.Printed.StartsWith("2009")) dpOriginal++;
                    }
            }
        Check($"400 Duelist Pack: Yugi packs: {dpSeen} pulls of a card with an original text, {dpOriginal} stamped 2009 with the original", dpSeen > 0 && dpOriginal == dpSeen);
        return failures;
    }

    /// <summary>
    /// Card unlocks (CARD-UNLOCKS-SPEC-2026-11-01, checks 2-7): the signatures, rival gifts, bonds, bounties, set
    /// completion, the 15% budget, and that every unlock pays cards (never DP), once, the same every run.
    /// </summary>
    private static int UnlockChecks(ICardDataProvider cards, string root, PrintingIndex printings, PackCatalog catalog, List<Personality> roster,
        Circuits circuits, Library library, GuestList guests, List<Ygo.Engine.DeckBuilding.Banlist> eraLists, Deck starter)
    {
        int failures = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        Console.WriteLine("\nCard unlocks:");
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        Deck? LoadDeck(string name) => deckFiles.TryGetValue(name, out var f) ? Deck.LoadYdk(f) : null;
        var noPass = BanPasses.LoadNoPass(Path.Combine(root, "campaign", "no-pass.txt"), cards);
        var board = BountyBoard.Load(Path.Combine(root, "campaign", BountyBoard.FileName));
        var unlocks = new CardUnlocks(cards, printings, circuits, roster, noPass, LoadDeck, catalog, board, guests, library);
        string Name(uint c) => cards.Get(c)?.Name ?? c.ToString();
        Profile Fresh(uint salt = 20261101)
        {
            var p = Profile.New(starter, cards, printings);
            p.Collection.Salt = salt;
            p.Collection.CommonestOf = c => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
            return p;
        }
        (int ci, int mi) MatchOf(string id)
        {
            for (int ci = 0; ci < circuits.All.Count; ci++)
                for (int mi = 0; mi < circuits.All[ci].Matches.Count; mi++)
                    if (circuits.All[ci].Matches[mi].Character.Id == id) return (ci, mi);
            return (-1, -1);
        }
        // A circuit win against them, with its unlocks.
        (DuelReward reward, List<Grant> grants) Beat(Profile p, string id, DuelTally? tally = null, bool loaner = false)
        {
            var (ci, mi) = MatchOf(id);
            var reward = circuits.Record(p, ci, mi, 0, loaner);
            int dp = p.Dp;
            var grants = unlocks.AfterDuel(p, reward, circuits.All[ci].Band, tally, loaner, tag: false);
            if (p.Dp != dp) throw new InvalidOperationException("an unlock paid DP");
            return (reward, grants);
        }

        // 2. Signatures.
        var problems = unlocks.SignatureProblems(eraLists);
        Check($"{unlocks.Signatures.Count} signatures, each in the character's decks, released by the era where you meet them, not no-pass, not Forbidden there, none shared "
              + $"({string.Join(", ", unlocks.Signatures.OrderBy(s => s.Key).Select(s => $"{s.Key}: {Name(s.Value)}"))}){(problems.Count > 0 ? " PROBLEMS: " + string.Join("; ", problems) : "")}",
            unlocks.Signatures.Count == 23 && problems.Count == 0 && unlocks.Signatures.Values.All(c => c != 0)
            && unlocks.SignatureOf("ines") != 0 && Name(unlocks.SignatureOf("ines")) == "Thousand-Eyes Restrict" && unlocks.SignatureOf("kit") == 0);

        // 3. Rival gifts.
        var gifted = Fresh();
        var (firstReward, firstGrants) = Beat(gifted, "vera");
        var gift = firstGrants.FirstOrDefault(g => g.Kind == GrantKind.RivalGift);
        var veraSig = unlocks.SignatureOf("vera");
        Check($"the first win against Vera pays her signature: \"{gift?.Text}\" (a grade 8 reward copy at its commonest printing that sells for 0)",
            gift != null && gift.Code == veraSig && gift.Copy is { Grade: Grades.Fresh, Source: CopySource.Reward, Paid: 0 } && gifted.Collection.SellPrice(gift.Copy) == 0
            && gift.Rarity == printings.Commonest(veraSig, 0) && gift.Event == "beat:vera" && firstReward.WinsAdded.SequenceEqual(new[] { ("vera", 1) })
            && firstReward.Grants.Contains(gift) && CardUnlocks.Events(firstGrants).Contains("beat:vera") && gifted.Unlocks.Contains("gift:vera")
            && CardUnlocks.For("beat:vera", firstGrants) == gift && gift.Text.StartsWith("Vera's gift: Gearfried the Swordmaster · Raw · Near Mint"));
        // The story queue in the save (TODO 2026-11-06): a result's batch (its events, fill, partner and grants) written as optional
        // "storyq" lines and read back the same; an old save loads with none, and an old build skips the lines (the rest reads
        // the same). The grants are display only: a save holding the gift's batch loads with the gift owned once, the queued
        // grant has no copy, and its scene still finds the card's name.
        {
            var q = Fresh(20261106);
            var (qReward, qGrants) = Beat(q, "vera");
            var qGift = qGrants.First(g => g.Kind == GrantKind.RivalGift);
            q.StoryQueue.Add(new StoryBatch
            {
                Events = CardUnlocks.Events(qGrants).Concat(new[] { "clear:Regional Qualifier", "bracketwin:Alley Brawl", "-" }).ToList(),
                Fill = new Dictionary<string, string> { ["handle"] = "Mr. Nobody  ·  50% off", ["sold"] = "Pot of Greed", ["nick"] = "" },
                Grants = qGrants.Select(StoryBatch.Shown).ToList(),
            });
            q.StoryQueue.Add(new StoryBatch { Events = { "partner", "partner:kit" }, Partner = "kit" });
            int copies = q.Collection.All.Count(), owned = q.Collection.Owned(qGift.Code), dp = q.Dp;
            var text = AlleyTest.SaveText(q);
            var qBack = AlleyTest.FromText(text, printings, cards);
            string Batches(Profile p) => string.Join(" / ", p.StoryQueue.Select(b =>
                $"{b.Partner}|{string.Join(",", b.Events)}|{string.Join(",", b.Fill.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Key + "=" + f.Value))}|"
                + string.Join(",", b.Grants.Select(g => $"{g.Kind}:{g.Code}:{g.Rarity}:{g.Owed}:{g.Character}:{g.Event}:{g.SetCode}:{g.CardName}:{g.SetName}:{g.Text}"))));
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            var storyLines = lines.Where(l => l.StartsWith("storyq")).ToList();
            var oldSave = string.Join("\n", lines.Where(l => !l.StartsWith("storyq")));
            var qOld = AlleyTest.FromText(oldSave, printings, cards);
            var backGift = qBack.StoryQueue.FirstOrDefault()?.Grants.FirstOrDefault(g => g.Kind == GrantKind.RivalGift);
            Check($"the story queue's batches survive a save ({storyLines.Count} storyq lines: {Batches(qBack)}), stable on a second save; an old save (no storyq lines) loads with none, "
                  + "and a save without them reads the same otherwise (what an older build sees)",
                qBack.StoryQueue.Count == 2 && Batches(qBack) == Batches(q) && AlleyTest.SaveText(qBack) == text && storyLines.Count == 2 + CardUnlocks.Events(qGrants).Count + 5 + 3 + qGrants.Count
                && qOld.StoryQueue.Count == 0 && AlleyTest.SaveText(qOld).Replace("\r", "") == oldSave.Replace("\r", "") && qBack.StoryQueue[1].Partner == "kit" && qBack.StoryQueue[0].Events.Contains("-")
                && qBack.StoryQueue[0].Fill["nick"] == "" && qBack.StoryQueue[0].Fill["handle"] == "Mr. Nobody  ·  50% off");
            Check($"a queued gift's batch replays without paying it again: after the save and load, Vera's signature is owned {qBack.Collection.Owned(qGift.Code)} time(s) (was {owned}), "
                  + $"{qBack.Collection.All.Count()} copies (was {copies}), {qBack.Dp} DP (was {dp}); the queued grant has no copy and its scene still reads \"{CardUnlocks.For("beat:vera", qBack.StoryQueue[0].Grants)?.CardName}\"",
                qGift.Copy != null && backGift != null && backGift.Copy == null && StoryBatch.Shown(qGift).Copy == null && q.StoryQueue[0].Grants.All(g => g.Copy == null)
                && qBack.Collection.Owned(qGift.Code) == owned && owned == 1 && qBack.Collection.All.Count() == copies && qBack.Dp == dp
                && CardUnlocks.For("beat:vera", qBack.StoryQueue[0].Grants)?.CardName == qGift.CardName && qGift.CardName.Length > 0 && qReward.Grants.Contains(qGift));
        }
        // A progress-only save (the story queue, a scene seen) keeps profile.txt.bak the last real save's; a null or a lone surrogate
        // in a queued batch is written empty instead of breaking the save.
        {
            var q = Fresh(20261107);
            var dir = Path.Combine(Path.GetTempPath(), $"ygo-progress-{Guid.NewGuid():N}");
            var path = Path.Combine(dir, "profile.txt");
            q.Save(path);
            q.Save(path);
            var bak = File.ReadAllText(path + ".bak");
            q.Dp += 1;
            q.Save(path, backup: false);
            bool kept = File.ReadAllText(path + ".bak") == bak && Profile.Load(path)!.Dp == q.Dp;
            q.StoryQueue.Add(new StoryBatch { Events = { "beat:vera", "\uD800" }, Fill = { ["card"] = null!, ["sold"] = "a\uDC00b" }, Grants = { new Grant { Text = null!, CardName = "\uD800" } } });
            string? error = null;
            try { q.Save(path, backup: false); } catch (Exception ex) { error = ex.Message; }
            var odd = error == null ? Profile.Load(path) : null;
            Directory.Delete(dir, true);
            Check($"a progress-only save keeps profile.txt.bak the last real save's ({kept}); a queued batch holding a null or a lone surrogate still saves ({error ?? "no error"}) and loads "
                  + $"({odd?.StoryQueue.FirstOrDefault()?.Events.Count} events, the bad text empty or U+FFFD)",
                kept && error == null && odd != null && odd.StoryQueue.Count == 1 && odd.StoryQueue[0].Events.FirstOrDefault() == "beat:vera" && odd.StoryQueue[0].Events.Skip(1).All(Bad)
                && odd.StoryQueue[0].Fill["card"] == "" && Bad(odd.StoryQueue[0].Fill["sold"].Replace("a", "").Replace("b", "")) && odd.StoryQueue[0].Grants.Count == 1
                && Bad(odd.StoryQueue[0].Grants[0].CardName) && odd.StoryQueue[0].Grants[0].Text == "");
            // (the escaper drops a lone surrogate's text, or, where it repairs one, writes U+FFFD in its place)
            static bool Bad(string t) => t.Length == 0 || t == "\uFFFD";
        }
        var (_, secondGrants) = Beat(gifted, "vera");
        Check($"a second win doesn't pay it again ({(secondGrants.Count == 0 ? "nothing" : string.Join("; ", secondGrants))})",
            secondGrants.All(g => g.Kind == GrantKind.Drop));
        var cycled = Fresh();
        Beat(cycled, "tink");
        cycled.NewGamePlus();
        var cycledGrants = Enumerable.Range(0, 1).SelectMany(_ => Beat(cycled, "tink").grants).ToList();
        Check($"after New Game+ a win pays nothing (no gift, no drop: {cycledGrants.Count})", cycledGrants.Count == 0 && cycled.Unlocks.Contains("gift:tink"));
        var historic = Fresh();
        historic.EraLimited = true;
        var early = new DuelReward();
        historic.AddWin("snake-eye", early);
        var held = unlocks.AfterDuel(historic, early, "", null, false, false);
        int copiesBefore = historic.Collection.Owned(unlocks.SignatureOf("snake-eye"));
        foreach (var ci in Enumerable.Range(0, circuits.All.Count)) foreach (var mi in Enumerable.Range(0, circuits.All[ci].Matches.Count)) historic.MatchWins[Profile.MatchKey(ci, mi)] = 1;
        var paidLater = unlocks.PayOwed(historic);
        Check($"in era-limited play a gift not in the shop's era is held (\"{held.FirstOrDefault()?.Text}\"), then paid with its beat: event once it is (shop to {circuits.ShopYear(historic)}: \"{paidLater.FirstOrDefault()?.Text}\")",
            held.Count == 1 && held[0].Owed && held[0].Event.Length == 0 && copiesBefore == 0 && paidLater.Count == 1 && paidLater[0].Kind == GrantKind.RivalGift && paidLater[0].Event == "beat:snake-eye"
            && historic.Unlocks.Contains("gift:snake-eye") && !historic.Unlocks.Contains("giftowed:snake-eye") && unlocks.PayOwed(historic).Count == 0
            && historic.Collection.Owned(unlocks.SignatureOf("snake-eye")) == 1);

        // Drops: about 15% of later wins, in the first playthrough, and the same for the same save and duel count.
        int drops = 0, trials = 2000;
        var dropCodes = new List<uint>();
        for (int i = 0; i < trials; i++)
        {
            var p = new Profile();
            p.Collection.Salt = (uint)i * 2654435761u;
            p.DuelsPlayed = i % 7;
            var r1 = new DuelReward(); p.AddWin("tink", r1); unlocks.AfterDuel(p, r1, "", null, false, false);
            var r2 = new DuelReward(); p.AddWin("tink", r2);
            var g = unlocks.AfterDuel(p, r2, "", null, false, false).FirstOrDefault(x => x.Kind == GrantKind.Drop);
            if (g != null) { drops++; if (dropCodes.Count < 5) dropCodes.Add(g.Code); }
        }
        var tinkCards = unlocks.DeckCards("tink");
        Check($"a later win drops a spare {drops * 100.0 / trials:F1}% of the time (15% planned), a Common or Rare from their decks ({string.Join(", ", dropCodes.Select(Name))})",
            Math.Abs(drops / (double)trials - 0.15) < 0.025 && dropCodes.All(c => tinkCards.Contains(c) && printings.Commonest(c, 0) <= Rarity.NormalParallelRare));
        List<string> Run(uint salt)
        {
            var p = Fresh(salt);
            var log = new List<string>();
            for (int i = 0; i < 12; i++) log.AddRange(Beat(p, "tink").grants.Select(g => g.Text));
            return log;
        }
        var runA = Run(77);
        Check($"the same save and duels get the same unlocks every run ({runA.Count} grants over 12 wins)", runA.SequenceEqual(Run(77)) && runA.Count >= 3);

        // 4. Bonds.
        var bonded = Fresh();
        var bondEvents = new List<string>();
        for (int i = 0; i < 3; i++) bondEvents.AddRange(CardUnlocks.Events(Beat(bonded, "vera").grants));
        var offers = unlocks.TradeOffers(bonded, "vera");
        Check($"the 3rd win against Vera raises bond:vera:3 once and offers a trade ({offers.Count} cards: {string.Join(", ", offers.Take(4).Select(Name))}...)",
            bondEvents.Count(e => e == "bond:vera:3") == 1 && bonded.Unlocks.Contains("bondtrade:vera") && offers.Count > 0 && !offers.Contains(veraSig));
        var take = offers.Last();
        var gives = unlocks.TradeGives(bonded, take, _ => 0);
        var give = gives.FirstOrDefault();
        string? refused = null, refusedLow = null;
        var needed = give == null ? null : unlocks.Trade(bonded, "vera", take, give, c => c == give.Code ? bonded.Collection.Count(c) : 0, out refused);
        var cheap = bonded.Collection.All.FirstOrDefault(c => CardTrader.Cost(c.Code, printings, cards) < CardTrader.Cost(offers[0], printings, cards));
        if (cheap != null) unlocks.Trade(bonded, "vera", offers[0], cheap, _ => 0, out refusedLow);
        int takeBefore = bonded.Collection.Owned(take), dpBeforeTrade = bonded.Dp;
        var traded = give == null ? null : unlocks.Trade(bonded, "vera", take, give, _ => 0, out _);
        Check($"a trade of a copy a saved deck needs is refused (\"{refused}\"{(refusedLow != null ? $", and a cheaper one: \"{refusedLow}\"" : "")}); a spare goes: \"{traded?.Text}\", once",
            needed == null && refused?.Contains("saved deck needs") == true && (cheap == null || refusedLow != null) && traded != null
            && bonded.Collection.Owned(take) == takeBefore + 1 && !bonded.Collection.All.Contains(give!) && bonded.Dp == dpBeforeTrade
            && bonded.Unlocks.Contains("bondtraded:vera") && unlocks.TradeOffers(bonded, "vera").Count == 0);
        var fifth = Beat(bonded, "vera").grants.Concat(Beat(bonded, "vera").grants).ToList();
        var bondGift = fifth.FirstOrDefault(g => g.Kind == GrantKind.BondGift);
        Check($"the 5th win pays the rarest printing: \"{bondGift?.Text}\"",
            bondGift != null && bondGift.Code == veraSig && bondGift.Rarity == printings.Rarest(veraSig, 0) && bondGift.Copy?.Paid == 0
            && CardUnlocks.Events(fifth).Contains("bond:vera:5") && bonded.Unlocks.Contains("bondgift:vera")
            && TagPartners.Bond(roster.First(p => p.Id == "vera"), Fresh()).Contains("bond 3: one trade · bond 5: their Gearfried the Swordmaster"));
        var kit = new Profile();
        var kitReward = new DuelReward();
        for (int i = 0; i < 5; i++) kit.AddWin("kit", kitReward);
        Check("Kit, the shop regular, has no bond rewards", unlocks.AfterDuel(kit, kitReward, "", null, false, true).All(g => g.Kind == GrantKind.Drop));

        // 5. Bounties.
        var known = board.All.SelectMany(b => b.Checks).Where(c => !Bounty.Known(c)).ToList();
        var bands = new[] { "Beginner", "Novice", "Intermediate", "Advanced", "Expert", "Master" };
        Check($"bounties.txt: 3 bounties for each of {string.Join(", ", board.Bands)}, every check known{(known.Count > 0 ? " (unknown: " + string.Join(", ", known) + ")" : "")}, ids unique, the Gauntlet on the Master board",
            board.Bands.SequenceEqual(bands) && bands.All(b => board.For(b).Count == 3) && known.Count == 0 && board.All.Select(b => b.Id).Distinct().Count() == board.All.Count
            && board.BoardOf("Gauntlet") == "Master" && circuits.All.All(c => board.BoardOf(c.Band).Length > 0));
        // A tally read from core messages: you go first, summon a Normal Monster, and on turn 3 it attacks for all 8000.
        byte[] Msg(byte id, params object[] parts)
        {
            var bytes = new List<byte> { id };
            foreach (var part in parts)
                bytes.AddRange(part switch { byte b => new[] { b }, uint u => BitConverter.GetBytes(u), _ => throw new ArgumentException() });
            return bytes.ToArray();
        }
        uint luster = cards.All.First(c => c.Name == "Luster Dragon").Code;
        var tally = new DuelTally(cards);
        foreach (var m in new[]
        {
            Msg(Ygo.Engine.Msg.NewTurn, (byte)0),
            Msg(Ygo.Engine.Msg.Summoning, luster, (byte)0, (byte)Location.MZone, 2u, 1u),
            Msg(Ygo.Engine.Msg.NewTurn, (byte)1),
            Msg(Ygo.Engine.Msg.Damage, (byte)0, 300u),
            Msg(Ygo.Engine.Msg.NewTurn, (byte)0),
            Msg(Ygo.Engine.Msg.Attack, (byte)0, (byte)Location.MZone, 2u, 1u, (byte)0, (byte)0, 0u, 0u),
            Msg(Ygo.Engine.Msg.Damage, (byte)1, 8000u),
            Msg(Ygo.Engine.Msg.Win, (byte)0, (byte)1),
        }) tally.Observe(m);
        Check($"a tally reads the duel: first {tally.FirstPlayer}, {tally.Turns} turns, finisher {tally.Finisher} ({Name(tally.FinisherCode)}), hit {tally.BiggestHit}, LP low {tally.LowestLp}, winner {tally.Winner}",
            tally.FirstPlayer == 0 && !tally.WentSecond && tally.Turns == 3 && tally.Finisher == DuelTally.Finish.Normal && tally.FinisherCode == luster
            && tally.BiggestHit == 8000 && tally.MostDamageInATurn == 8000 && tally.MostSummonsInATurn == 1 && tally.DamageTaken == 300 && tally.LowestLp == 7700 && tally.Won);
        var built = new DuelTally(cards) { FirstPlayer = 1, Turns = 9, Winner = 0, LowestLp = 900, DamageTaken = 7100, BiggestHit = 3000, MostDamageInATurn = 8000,
            MostSummonsInATurn = 5, TrapsActivated = 2, Finisher = DuelTally.Finish.Extra };
        string Met(string band, DuelTally t, bool loaner = false) => string.Join(",", board.For(band).Where(b => b.Met(t, loaner)).Select(b => b.Id));
        Check($"a built tally meets and misses as expected (Beginner: {Met("Beginner", tally)}; Novice: {Met("Novice", built)}, with a loaner: {Met("Novice", built, true)}; Intermediate: {Met("Intermediate", built)})",
            Met("Beginner", tally) == "beginner-vanilla,beginner-quick" && Met("Novice", built) == "novice-comeback,novice-bighit,novice-traps"
            && Met("Novice", built, true) == "novice-comeback,novice-bighit" && Met("Intermediate", built) == "intermediate-extra,intermediate-swarm,intermediate-second"
            && Met("Advanced", built) == "advanced-otk" && Met("Gauntlet", built) == "master-otk" && Met("Expert", built) == "expert-comeback");
        var hunter = Fresh();
        hunter.Seen.Add(BountyBoard.IntroScene); // Gus's board is up (it opens at the Beginner Circuit's clear)
        var perfect = new DuelTally(cards) { FirstPlayer = 0, Turns = 5, Winner = 0, Finisher = DuelTally.Finish.Normal };
        int tokensBefore = hunter.Tokens;
        var (claimReward, claimed) = Beat(hunter, circuits.All[0].Matches[0].Character.Id, perfect);
        int tokensPaid = hunter.Tokens - tokensBefore - claimReward.Tokens - claimReward.LevelUps.Sum(u => u.Tokens);
        var again = Beat(hunter, circuits.All[0].Matches[0].Character.Id, perfect).grants.Where(g => g.Kind is GrantKind.Bounty or GrantKind.BountyToken).ToList();
        uint wanted = cards.All.First(c => c.Name == "Ash Blossom & Joyous Spring").Code;
        hunter.Wishlist[wanted] = 3;
        var owedPaid = unlocks.PayOwed(hunter);
        var claimedBounties = claimed.Where(g => g.Kind is GrantKind.Bounty or GrantKind.BountyToken).ToList();
        Check($"with an empty wishlist: 1 token, then owed ({string.Join(" / ", claimedBounties.Select(g => g.Text))}); a Recipe pays what's owed ({string.Join(" / ", owedPaid.Select(g => g.Text))}), once",
            claimedBounties.Count == 3 && claimedBounties.Count(g => g.Kind == GrantKind.BountyToken) == 1 && tokensPaid == 1
            && again.Count == 0 && owedPaid.Count == 2 && owedPaid.All(g => g.Code == wanted && g.Copy?.Paid == 0) && hunter.BountyOwed == 0 && unlocks.PayOwed(hunter).Count == 0
            && board.For("Beginner").All(b => hunter.Unlocks.Contains("bounty:" + b.Id)) && unlocks.OpenBounties(hunter) == 0);
        var lent = Fresh();
        lent.Wishlist[wanted] = 1;
        var tagged = new DuelReward();
        lent.AddWin("vera", tagged);
        Check($"bounties count only one on one in a circuit (a tag or guest win pays none), and pay from the Recipe ({unlocks.BountyPays(lent)})",
            unlocks.AfterDuel(lent, tagged, circuits.All[0].Band, perfect, false, tag: true).All(g => g.Kind != GrantKind.Bounty)
            && unlocks.AfterDuel(lent, tagged, "", perfect, false, false).All(g => g.Kind != GrantKind.Bounty) && unlocks.BountyPays(lent).Contains("Ash Blossom"));

        // 6. Set completion.
        var small = catalog.Sets.Where(s => s.CardCount <= 60 && s.Pool.Values.SelectMany(c => c).All(c => !noPass.Contains(c))).OrderBy(s => s.CardCount).ThenBy(s => s.Info.Code).First();
        var collector = Fresh();
        var setCards = small.Pool.Values.SelectMany(c => c).Distinct().OrderBy(c => c).ToList();
        foreach (var c in setCards.Skip(1)) if (!collector.Collection.Owns(c)) collector.Collection.Add(c, Rarity.Common);
        var notYet = unlocks.CheckSets(collector).Where(g => g.SetCode == small.Info.Code).ToList();
        if (!collector.Collection.Owns(setCards[0])) collector.Collection.Add(setCards[0], Rarity.Common);
        int dpSet = collector.Dp;
        var done = unlocks.CheckSets(collector).Where(g => g.SetCode == small.Info.Code).ToList();
        var rarest = small.Pool.Keys.Max();
        var poster = Posters.Find(Posters.SetPrefix + small.Info.Code, collector);
        Check($"completing {small.Info.Name} ({small.CardCount} cards) pays once: \"{done.FirstOrDefault()?.Text}\", and earns the poster \"{poster?.Title}\"",
            notYet.Count == 0 && done.Count == 1 && done[0].Rarity == rarest && small.Pool[rarest].Contains(done[0].Code) && done[0].Copy?.Paid == 0 && collector.Dp == dpSet
            && collector.SetsDone[small.Info.Code] == done[0].Code && unlocks.CheckSets(collector).All(g => g.SetCode != small.Info.Code)
            && CardUnlocks.Events(done).SequenceEqual(new[] { "setcomplete:" + small.Info.Code, "setcomplete" }) && CardUnlocks.For("setcomplete", done) == done[0]
            && poster != null && poster.Title == small.Info.Name && poster.Card == done[0].Code && Posters.Unlocked(circuits, collector).Contains(poster, new PosterIds())
            && !Posters.Unlocked(circuits, Fresh()).Any(p => p.Id.StartsWith(Posters.SetPrefix)));
        collector.Poster = poster!.Id;
        Check("a set poster can hang on your plate", Posters.Current(circuits, collector)?.Id == poster.Id);

        // Saving: the unlock keys, sets done, owed bounties and the catch-up flag; New Game+ keeps them; old saves catch up once.
        collector.Unlocks.Add("giftowed:vera");
        collector.BountyOwed = 2;
        var tmp = Path.Combine(Path.GetTempPath(), $"ygo-unlocks-{Guid.NewGuid():N}.txt");
        collector.Save(tmp);
        var back = Profile.Load(tmp)!;
        var oldLines = File.ReadAllLines(tmp).Where(l => !l.StartsWith("unlock") && !l.StartsWith("setdone") && !l.StartsWith("bountyowed")).ToList();
        File.WriteAllLines(tmp, oldLines);
        var old = Profile.Load(tmp)!;
        File.Delete(tmp);
        back.NewGamePlus();
        Check("unlocks, sets done, owed bounties and the catch-up flag survive saving and New Game+",
            back.Unlocks.SetEquals(collector.Unlocks) && back.SetsDone.Count == collector.SetsDone.Count && back.SetsDone[small.Info.Code] == done[0].Code
            && back.BountyOwed == 2 && back.UnlocksCaughtUp && collector.UnlocksCaughtUp && !new Profile().UnlocksCaughtUp);
        old.Wins["vera"] = 5;
        old.Wins["brask"] = 1;
        old.Wins["kit"] = 4;
        old.Collection.CommonestOf = c => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
        var caught = unlocks.CatchUp(old);
        Check($"an old save gets the gifts for duelists already beaten, once (\"Gus kept these for you\": {string.Join(" / ", caught.Select(g => g.Text))})",
            old.Unlocks.Contains("gift:vera") && caught.Count(g => g.Kind == GrantKind.RivalGift) == 2 && caught.Any(g => g.Kind == GrantKind.BondGift && g.Character == "vera")
            && caught.Any(g => g.Kind == GrantKind.BondTrade && g.Character == "vera") && caught.All(g => g.Kind != GrantKind.Drop && g.Character != "kit")
            && old.UnlocksCaughtUp && unlocks.CatchUp(old).Count == 0);

        // 7. The budget: the most the unlocks can pay per band (signatures met there, its 3 bounties, 15% of its later
        // wins as drops, 2 bond events) against what the QA stand-in's collection grew by in that band.
        var acquired = new Dictionary<string, int> { ["Beginner"] = 125, ["Novice"] = 88, ["Intermediate"] = 85, ["Advanced"] = 85, ["Expert"] = 148, ["Master"] = 123 };
        var budget = new List<string>();
        bool within = true;
        var met = new HashSet<string>();
        foreach (var band in bands)
        {
            var inBand = Enumerable.Range(0, circuits.All.Count).Where(i => board.BoardOf(circuits.All[i].Band) == band).ToList();
            int sigs = unlocks.Signatures.Keys.Count(id => unlocks.MeetCircuit(id) is int m && inBand.Contains(m));
            int later = 0;
            foreach (var m in inBand.SelectMany(i => circuits.All[i].Matches)) if (!met.Add(m.Character.Id)) later++;
            int dropsMax = (int)Math.Ceiling(CardUnlocks.DropPerMille / 1000.0 * Math.Max(1, later));
            int most = sigs + board.For(band).Count + dropsMax + 2;
            double share = most / (double)acquired[band];
            within &= share <= 0.15;
            budget.Add($"{band} {most}/{acquired[band]} ({share:P1})");
        }
        Check($"per band the most the unlocks pay stays at or below 15% of acquisitions: {string.Join(", ", budget)}; set completion adds at most 1 card per 30 owned", within);
        return failures;
    }

    /// <summary>
    /// Duchess Ilse as a guest after the Every Era final, and the story hooks that came with her (docs/dev/ILSE-GUEST-SPEC-2026-11-06.md
    /// section 4, checks 1-7): her entry and unlock, the deck rotation, the prize and gift, firstloss, finale, {partnerline}, the scenes.
    /// </summary>
    private static void IlseChecks(Action<string, bool> Check, string root, ICardDataProvider cards, PrintingIndex printings, List<Personality> roster,
        Circuits circuits, Library library, GuestList guests, Deck starter, bool lockedForVeteran, Profile veteran, Story story, AlleyData alleyData)
    {
        Console.WriteLine("\nDuchess Ilse and the story hooks:");
        var ilse = guests.All.FirstOrDefault(g => g.Character.Id == "ilse");
        if (ilse == null) { Check("Ilse is a special guest", false); return; }
        bool DeckExists(string d) => File.Exists(Path.Combine(root, "decks", "history", d + ".ydk")) || File.Exists(Path.Combine(root, "decks", d + ".ydk"));
        // 1. Her entry, the unlock, New Game+, no tag duel.
        var ngPlus = Profile.New(starter, cards, printings);
        ngPlus.LibraryOpen = true;
        for (int r = 0; r < library.Rounds.Count; r++) ngPlus.LibraryWins[library.FinalKey(r)] = 1;
        bool beforeNg = GuestList.IsUnlocked(ilse, circuits, ngPlus);
        ngPlus.NewGamePlus();
        var noLibrary = GuestList.Load(Path.Combine(root, "campaign", "guests.txt"), roster).All.First(g => g.Character.Id == "ilse");
        foreach (var mi in Enumerable.Range(0, circuits.TagCups.Count > 0 ? circuits.TagCups[0].Matches.Count : 0)) veteran.MatchWins[Circuits.TagKey(0, mi)] = 1;
        veteran.MatchWins[GuestList.Key(ilse, true)] = 1; // even a stray tag key in a save opens nothing
        Check($"Ilse parses with {ilse.Decks.Count} decks ({string.Join(", ", ilse.Decks)}, each a file), no partner, unlock \"{ilse.Unlock}\"; the veteran save (every circuit, the lessons, 12 tag wins) "
              + $"has her locked ({lockedForVeteran}), the final's keys unlock her ({GuestList.IsUnlocked(ilse, circuits, veteran)}), still after New Game+ ({GuestList.IsUnlocked(ilse, circuits, ngPlus)}, cycle {ngPlus.Cycle}); "
              + $"with no Library she stays locked; her tag duel never opens",
            ilse.Decks.Count == 4 && ilse.Decks.All(DeckExists) && !ilse.HasTag && ilse.Unlock == "final" && lockedForVeteran
            && GuestList.IsUnlocked(ilse, circuits, veteran) && beforeNg && GuestList.IsUnlocked(ilse, circuits, ngPlus) && ngPlus.Cycle == 1
            && !GuestList.IsUnlocked(noLibrary, circuits, veteran) && !GuestList.IsTagUnlocked(ilse, circuits, veteran));
        veteran.MatchWins.Remove(GuestList.Key(ilse, true));
        // Her lock line: the Library closed, cups left, then rounds.
        var locked = Profile.New(starter, cards, printings);
        string closed = GuestList.UnlockText(ilse, locked);
        locked.LibraryOpen = true;
        for (int c = 0; c < Math.Min(7, library.Cups.Count); c++)
            for (int m = 0; m < library.Cups[c].Matches.Count; m++) locked.LibraryWins[library.Key(c, m)] = 1;
        string cupsLeft = GuestList.UnlockText(ilse, locked);
        for (int c = 0; c < library.Cups.Count; c++)
            for (int m = 0; m < library.Cups[c].Matches.Count; m++) locked.LibraryWins[library.Key(c, m)] = 1;
        for (int r = 0; r < 2; r++) locked.LibraryWins[library.FinalKey(r)] = 1;
        string roundsLeft = GuestList.UnlockText(ilse, locked);
        Check($"her lock line: \"{closed}\" / \"{cupsLeft}\" / \"{roundsLeft}\"",
            closed == $"Win the {library.Final.Name} final" && cupsLeft == $"Win the {library.Final.Name} final (Format Library: 7 of {library.Cups.Count} cups cleared)"
            && roundsLeft == $"Win the {library.Final.Name} final (2 of {library.Rounds.Count} rounds won)" && !GuestList.IsUnlocked(ilse, circuits, locked));

        // 2. The rotation: one deck a duel, moved on by wins only.
        var rot = Profile.New(starter, cards, printings);
        var seen = new List<string> { GuestList.DeckFor(ilse, rot) };
        GuestList.Record(rot, circuits, ilse, false, 1); seen.Add(GuestList.DeckFor(ilse, rot));
        GuestList.Record(rot, circuits, ilse, false, 2); seen.Add(GuestList.DeckFor(ilse, rot));
        for (int w = 0; w < 4; w++) { GuestList.Record(rot, circuits, ilse, false, 0); seen.Add(GuestList.DeckFor(ilse, rot)); }
        Check($"her rotation: {string.Join(" > ", seen)} (start, a loss, a draw, then four wins)",
            seen.SequenceEqual(new[] { "synchron", "synchron", "synchron", "zombies", "gadgets", "modern", "synchron" })
            && GuestList.DeckIndex(ilse, rot) == 0 && guests.All.Where(g => g != ilse).All(g => GuestList.DeckFor(g, rot) == g.Deck));

        // 3. The prize once (then WinDp), Mirror Force with beat:ilse, met at the last circuit, no signature problems.
        var noPass = BanPasses.LoadNoPass(Path.Combine(root, "campaign", "no-pass.txt"), cards);
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        var unlocks = new CardUnlocks(cards, printings, circuits, roster, noPass, n => deckFiles.TryGetValue(n, out var f) ? Deck.LoadYdk(f) : null, null, null, guests, library);
        var winner = Profile.New(starter, cards, printings);
        int dp0 = winner.Dp;
        var first = GuestList.Record(winner, circuits, ilse, false, 0);
        int dp1 = winner.Dp;
        var grants = unlocks.AfterDuel(winner, first, "", null, false, tag: false);
        var again = GuestList.Record(winner, circuits, ilse, false, 0);
        int duelDp = circuits.WinDp(circuits.Current(winner));
        var gift = grants.FirstOrDefault(g => g.Kind == GrantKind.RivalGift);
        var mirror = cards.All.FirstOrDefault(c => c.Name == "Mirror Force")?.Code ?? 0;
        var eraLists = Ygo.Engine.DeckBuilding.Banlist.LoadFolders(new[] { Path.Combine(root, "lflists") });
        var sigProblems = unlocks.SignatureProblems(eraLists);
        Check($"her first win pays {first.Dp:N0} DP ({first.Summary}), the next {again.Dp:N0} (WinDp {duelDp}); the gift is {gift?.Text} ({gift?.Event}); met at circuit {unlocks.MeetCircuit("ilse")} of {circuits.All.Count}; signature problems: {string.Join("; ", sigProblems)}",
            first.FirstWin && first.Dp == duelDp + 10000 && dp1 - dp0 == first.Dp && !again.FirstWin && again.Dp == duelDp && ilse.Prize == 10000
            && gift != null && gift.Code == unlocks.SignatureOf("ilse") && unlocks.SignatureOf("ilse") != 0 && cards.Get(unlocks.SignatureOf("ilse"))?.Name == "Mirror Force"
            && gift.Event == "beat:ilse" && CardUnlocks.Events(grants).Contains("beat:ilse")
            && unlocks.MeetCircuit("ilse") == circuits.All.Count - 1 && sigProblems.Count == 0 && mirror != 0);

        // 4. firstloss: a new save's first circuit loss, not the second; a guest loss on a new save; not a draw; not a save that lost before.
        var loser = Profile.New(starter, cards, printings);
        circuits.Record(loser, 0, 0, 1);
        var firstLoss = Story.LossEvents(loser, 1);
        circuits.Record(loser, 0, 0, 1);
        var secondLoss = Story.LossEvents(loser, 1);
        var guestLoser = Profile.New(starter, cards, printings);
        GuestList.Record(guestLoser, circuits, guests.All[0], false, 1);
        var guestLoss = Story.LossEvents(guestLoser, 1);
        var drawer = Profile.New(starter, cards, printings);
        circuits.Record(drawer, 0, 0, 2);
        var draw = Story.LossEvents(drawer, 2);
        var oldLoser = Profile.New(starter, cards, printings);
        oldLoser.Losses["vera"] = 2;
        circuits.Record(oldLoser, 0, 0, 1);
        var oldLoss = Story.LossEvents(oldLoser, 1);
        Check($"firstloss: a new save's first circuit loss raises it ({string.Join(",", firstLoss)}), the second doesn't ({secondLoss.Count}); a guest loss on a new save does ({string.Join(",", guestLoss)}); a draw doesn't ({draw.Count}); a save that lost before doesn't ({oldLoss.Count})",
            firstLoss.SequenceEqual(new[] { Story.FirstLossEvent }) && secondLoss.Count == 0 && guestLoss.SequenceEqual(new[] { Story.FirstLossEvent }) && draw.Count == 0 && oldLoss.Count == 0);

        // 5. finale: both strands (Library, alley) while No Limits isn't ready; all three once it is; once.
        var nlTables = alleyData.Tables.Where(t => t.Tier == AlleyTier.NoLimits).ToList();
        var readyBefore = nlTables.Select(t => t.Ready).ToList();
        foreach (var t in nlTables) t.Ready = false;
        var fin = new Profile();
        bool none = Story.FinaleDue(fin, alleyData);
        fin.Seen.Add(Story.LibraryStrandScene);
        bool libraryOnly = Story.FinaleDue(fin, alleyData);
        fin.Seen.Remove(Story.LibraryStrandScene);
        fin.Seen.Add(BackAlley.ClosingScene);
        bool alleyOnly = Story.FinaleDue(fin, alleyData);
        fin.Seen.Add(Story.LibraryStrandScene);
        bool both = Story.FinaleDue(fin, alleyData);
        foreach (var t in nlTables) t.Ready = true;
        bool bothReady = Story.FinaleDue(fin, alleyData);
        fin.Seen.Add(Story.NoLimitsStrandScene);
        bool allThree = Story.FinaleDue(fin, alleyData);
        fin.Seen.Add(Story.FinaleScene);
        bool afterSeen = Story.FinaleDue(fin, alleyData);
        for (int i = 0; i < nlTables.Count; i++) nlTables[i].Ready = readyBefore[i];
        Check($"finale: not due with no strand ({none}), the Library's alone ({libraryOnly}) or the alley's alone ({alleyOnly}); due with both while No Limits isn't ready ({both}); "
              + $"with No Limits ready ({nlTables.Count} table) it needs the Invitational too ({bothReady} then {allThree}); not once seen ({afterSeen})",
            !none && !libraryOnly && !alleyOnly && both && nlTables.Count > 0 && !bothReady && allThree && !afterSeen && !Story.FinaleDue(fin, null));

        // 6. {partnerline}: filled first (it may use {partnerdeck}); a partner without one has that line skipped; partner: parses.
        var lined = Personality.Parse("name: Test Partner\npartner: Ready when you are. {partnerdeck} it is.", "test-partner");
        var plain = Personality.Parse("name: Plain", "plain");
        var reader = Profile.New(starter, cards, printings);
        var line = new StoryLine("partner", "{partnerline}");
        string filled = Story.Fill(line.Text, lined, reader, "Gadgets");
        Check($"{{partnerline}} fills in (\"{filled}\"), is a placeholder, and a partner without a line has it skipped (with: {Story.Shows(line, lined)}, without: {Story.Shows(line, plain)}, none: {Story.Shows(line, null)}); other lines show",
            lined.PartnerLine == "Ready when you are. {partnerdeck} it is." && filled == "Ready when you are. Gadgets it is." && Story.Placeholders.Contains("partnerline")
            && Story.Shows(line, lined) && !Story.Shows(line, plain) && !Story.Shows(line, null) && Story.Shows(new StoryLine("", "Hi {partner}"), plain));
        var canPartner = circuits.All.SelectMany(c => c.Matches).Select(m => m.Character)
            .Concat(guests.All.Select(g => g.Character)).Concat(roster.Where(TagPartners.IsShopRegular))
            .GroupBy(p => p.Id).Select(g => g.First()).ToList();
        var noLine = canPartner.Where(p => p.PartnerLine.Length == 0).Select(p => p.Id).OrderBy(i => i).ToList();
        Console.WriteLine($"  info {canPartner.Count} characters can partner you; {noLine.Count} have no partner: line{(noLine.Count > 0 ? ": " + string.Join(", ", noLine) : "")}");

        // 7. The scenes: Ilse's meeting and gift, the first loss, the finale (fails until the writers' scenes land).
        var expected = new (string id, string trigger, string place)[]
        {
            ("ilse-table", Story.GuestEvent("ilse"), ""), ("gift-ilse", "beat:ilse", StoryPlaces.CorkboardTop),
            ("first-loss", Story.FirstLossEvent, ""), ("finale", Story.FinaleEvent, ""),
        };
        var missing = expected.Where(e => !story.Scenes.Any(sc => sc.Id == e.id && sc.Trigger == e.trigger && (e.place.Length == 0 || sc.Place == e.place)))
            .Select(e => $"{e.id} ({e.trigger}{(e.place.Length > 0 ? ", place: " + e.place : "")})").ToList();
        Check($"the scenes exist: ilse-table (guest:ilse), gift-ilse (beat:ilse, place: corkboard-top), first-loss (firstloss), finale (finale){(missing.Count > 0 ? "; missing: " + string.Join(", ", missing) : "")}",
            missing.Count == 0);
    }

    /// <summary>
    /// Story gates (after:/before:, see Story's doc comment; docs/dev/story-review/STRUCTURE-2026-11-06.md D6): the file's gates name
    /// scenes and the checker catches each bad kind; two variants on one trigger play exactly one, the other left unseen; a scene
    /// due ahead in the same batch counts as seen; the trigger firing again plays nothing more; an old save that saw the original
    /// never gets its twin; story.txt's own variant pairs play one of each pair for every combination of what's been seen; and
    /// the ungated scenes play exactly as they did before gates (Seen alone, no save field).
    /// </summary>
    private static void GateChecks(Action<string, bool> Check, Story story, Deck starter, ICardDataProvider cards, PrintingIndex printings, List<string> header)
    {
        Console.WriteLine("\nStory gates:");
        // 1. story.txt's gates, and the checker on a file with every mistake.
        var problems = story.GateProblems();
        var gated = story.Scenes.Where(sc => sc.Gated).ToList();
        Check($"story.txt: {gated.Count} gated scene{(gated.Count == 1 ? "" : "s")}, every gate naming a scene in the file{(problems.Count > 0 ? ": " + string.Join("; ", problems.Take(6)) : "")}; the header documents after: and before:",
            problems.Count == 0 && header.Any(l => l.Contains("after: <scene id>")) && header.Any(l => l.Contains("before: <scene id>")));
        var bad = Story.Parse("scene: a\nwhen: start\nafter: nowhere\nafter: first-partner\nbefore: a\n-: A.\n"
            + "scene: b\nwhen: start\nafter: c\nbefore: c\n-: B.\n"
            + "scene: c\nwhen: start\n-: C.\n"
            + "scene: first-partner\nwhen: partner\n-: P.\n"
            + "scene: d1\nwhen: go\nbefore: c\n-: D1.\n"
            + "scene: d2\nwhen: go\nafter: c\n-: D2.\n");
        var badProblems = bad.GateProblems();
        Check($"the gate checker catches a gate naming no scene, the generic partner scene, the scene itself, after: and before: the same scene, and a variant pair missing its before: ({string.Join("; ", badProblems)})",
            badProblems.Count == 5 && badProblems.Contains("a: gate names no scene nowhere") && badProblems.Any(x => x.StartsWith("a: gate names first-partner, the generic partner scene"))
            && badProblems.Contains("a: gates on itself") && badProblems.Contains("b: both after: and before: c (it can never play)")
            && badProblems.Any(x => x.StartsWith("d2: a variant of d1")));

        // 2. Two variants on one trigger: exactly one plays, the other stays unseen; firing again plays nothing more.
        var twins = Story.Parse("scene: x\nwhen: other\n-: X.\n"
            + "scene: v-before\nwhen: go\nbefore: x\n-: Before X.\n"
            + "scene: v-after\nwhen: go\nafter: x\nbefore: v-before\n-: After X.\n"
            + "scene: plain\nwhen: go\n-: Always.\n");
        string Play(Story st, Profile p, params string[] events)
        {
            var due = st.Pending(p, events);
            foreach (var sc in due) Story.MarkSeen(p, sc);
            return string.Join(",", due.Select(sc => sc.Id));
        }
        var early = new Profile();
        string early1 = Play(twins, early, "go");
        bool earlyAfterUnseen = !early.Seen.Contains("v-after");
        Play(twins, early, "other");
        string early2 = Play(twins, early, "go");
        var late = new Profile();
        late.Seen.Add("x");
        string late1 = Play(twins, late, "go");
        bool lateBeforeUnseen = !late.Seen.Contains("v-before");
        string late2 = Play(twins, late, "go");
        var batch = new Profile();
        string sameBatch = Play(twins, batch, "go", "other");
        Check($"two variants on one trigger play exactly one: before X \"{early1}\" (v-after left unseen: {earlyAfterUnseen}), after X \"{late1}\" (v-before unseen: {lateBeforeUnseen}); "
              + $"the trigger again plays \"{early2}\" / \"{late2}\"; X due ahead in the same batch counts: \"{sameBatch}\"",
            early1 == "v-before,plain" && earlyAfterUnseen && late1 == "v-after,plain" && lateBeforeUnseen && early2 == "" && late2 == "" && sameBatch == "x,v-after,plain");

        // 3. An old save: it saw the original before the twin existed; once X is seen and the trigger fires again, nothing plays.
        var original = Story.Parse("scene: x\nwhen: other\n-: X.\nscene: v-before\nwhen: go\n-: The original.\n");
        var oldSave = new Profile();
        string oldFirst = Play(original, oldSave, "go");
        oldSave.Seen.Add("x");
        string oldAgain = Play(twins, oldSave, "go");
        Check($"an old save that saw the original (\"{oldFirst}\") never gets its twin when the trigger fires again after X (\"{oldAgain}\": only the scene it hadn't seen)",
            oldFirst == "v-before" && oldAgain == "plain" && !oldSave.Seen.Contains("v-after"));

        // 4. story.txt's own variant pairs (same trigger, one before: X, one after: X): one of each pair for every combination.
        var pairs = story.Scenes.SelectMany(a => story.Scenes.Where(b => b != a && b.Trigger == a.Trigger && a.Before.Intersect(b.After).Any()).Select(b => (a, b))).ToList();
        var pairFailures = new List<string>();
        foreach (var trigger in pairs.Select(p => p.a.Trigger).Distinct())
        {
            var group = story.Scenes.Where(sc => sc.Trigger == trigger).ToList();
            var keys = group.SelectMany(sc => sc.After.Concat(sc.Before)).Distinct().Where(k => group.All(sc => sc.Id != k)).Take(10).ToList();
            for (int mask = 0; mask < 1 << keys.Count; mask++)
            {
                var p = new Profile();
                for (int k = 0; k < keys.Count; k++) if ((mask & 1 << k) != 0) p.Seen.Add(keys[k]);
                var due = story.Pending(p, new[] { trigger }).Select(sc => sc.Id).ToHashSet();
                foreach (var (a, b) in pairs.Where(x => x.a.Trigger == trigger))
                    if (due.Contains(a.Id) == due.Contains(b.Id)) pairFailures.Add($"{trigger} with [{string.Join(",", p.Seen)}]: {a.Id} {due.Contains(a.Id)}, {b.Id} {due.Contains(b.Id)}");
            }
        }
        Check($"story.txt's {pairs.Count} variant pair{(pairs.Count == 1 ? "" : "s")} play exactly one of each for every combination of the scenes they gate on{(pairFailures.Count > 0 ? ": " + string.Join("; ", pairFailures.Take(4)) : "")}",
            pairFailures.Count == 0);

        // 5. The ungated scenes play as before gates: the old rule (trigger, Seen, a partner's own scene first) on a new save and
        // on one that has seen every other scene, for every trigger in the file and a partner with and without a scene.
        var drift = new List<string>();
        var seenHalf = Profile.New(starter, cards, printings);
        for (int i = 0; i < story.Scenes.Count; i += 2) seenHalf.Seen.Add(story.Scenes[i].Id);
        foreach (var p in new[] { Profile.New(starter, cards, printings), seenHalf })
            foreach (var partner in new[] { "vera", "rookie-ryo" })
                foreach (var trigger in story.Scenes.Select(sc => sc.Trigger).Distinct())
                {
                    var now = story.Pending(p, new[] { trigger }, partner);
                    var before = story.Scenes.Where(sc => sc.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase)
                            && !p.Seen.Contains(sc.PerPartner ? $"{sc.Id}:{partner}" : sc.Id)
                            && !(sc.PerPartner && story.Scenes.Any(o => o.Trigger.Equals("partner:" + partner, StringComparison.OrdinalIgnoreCase))))
                        .ToList();
                    if (!now.Where(sc => !sc.Gated).Select(sc => sc.Id).SequenceEqual(before.Where(sc => !sc.Gated).Select(sc => sc.Id))
                        || now.Any(sc => !before.Contains(sc))) drift.Add(trigger);
                }
        Check($"the ungated scenes play exactly as before gates, on a new save and on one that has seen half the scenes ({story.Scenes.Select(sc => sc.Trigger).Distinct().Count()} triggers{(drift.Count > 0 ? "; differ: " + string.Join(", ", drift.Distinct().Take(6)) : "")})",
            drift.Count == 0);
    }

    /// <summary>
    /// Vera's rematch, a post-final guest (docs/dev/NEXT-BUILD-2026-11-06b.md chunk G): her entry (warriors, no partner, unlock: final,
    /// Ilse's prize), the unlock and New Game+, the prize once a cycle with no second signature gift and guestbeat:vera rather than
    /// beat:vera, her scouting report kept to her games, and the triggers the writers' scenes take.
    /// </summary>
    private static void VeraChecks(Action<string, bool> Check, string root, ICardDataProvider cards, PrintingIndex printings, List<Personality> roster,
        Circuits circuits, Library library, GuestList guests, Deck starter, Story story, Scouting scouting, Dictionary<string, string> scoutFiles, Func<string, bool> triggerOk)
    {
        Console.WriteLine("\nVera's rematch (a post-final guest):");
        var vera = guests.All.FirstOrDefault(g => g.Character.Id == "vera");
        var ilse = guests.All.FirstOrDefault(g => g.Character.Id == "ilse");
        if (vera == null || ilse == null) { Check("Vera (and Ilse) are special guests", false); return; }
        // 1. Her entry: her circuit deck, one on one, after the final, the same prize as Ilse (the same milestone), the sixth row.
        var circuitDecks = circuits.All.SelectMany(c => c.Matches).Where(m => m.Character.Id == "vera").Select(m => m.Deck).Distinct().ToList();
        Check($"Vera parses: plays {string.Join(", ", vera.Decks)} (her circuit decks: {string.Join(", ", circuitDecks)}), {(vera.HasTag ? "a partner" : "no partner")}, unlock \"{vera.Unlock}\", "
              + $"prize {vera.Prize:N0} (Ilse's {ilse.Prize:N0}); guest {guests.All.ToList().IndexOf(vera) + 1} of {guests.All.Count}",
            vera.Decks.SequenceEqual(new[] { "warriors" }) && circuitDecks.Contains("warriors") && !vera.HasTag && vera.Unlock == "final" && vera.Prize == ilse.Prize
            && guests.All.Count == 6 && guests.All.Last() == vera);

        // 2. The unlock: every circuit and Tag Cup cleared isn't enough; the final's keys open her, still after New Game+; no tag duel.
        var vet = Profile.New(starter, cards, printings);
        foreach (var l in ChainLessons.All) vet.Seen.Add(l.SeenKey);
        for (int ci = 0; ci < circuits.All.Count; ci++)
            for (int mi = 0; mi < circuits.All[ci].Matches.Count; mi++) vet.MatchWins[Profile.MatchKey(ci, mi)] = 1;
        for (int t = 0; t < circuits.TagCups.Count; t++)
            for (int mi = 0; mi < circuits.TagCups[t].Matches.Count; mi++) vet.MatchWins[Circuits.TagKey(t, mi)] = 1;
        bool lockedForVet = !GuestList.IsUnlocked(vera, circuits, vet);
        string lockLine = GuestList.UnlockText(vera, vet);
        vet.LibraryOpen = true;
        for (int r = 0; r < library.Rounds.Count; r++) vet.LibraryWins[library.FinalKey(r)] = 1;
        bool open = GuestList.IsUnlocked(vera, circuits, vet), tagShut = !GuestList.IsTagUnlocked(vera, circuits, vet);
        vet.NewGamePlus();
        Check($"she's locked with every circuit and Tag Cup cleared (\"{lockLine}\"), open once the final is won ({open}), still after New Game+ ({GuestList.IsUnlocked(vera, circuits, vet)}); her tag duel never opens ({tagShut})",
            lockedForVet && open && GuestList.IsUnlocked(vera, circuits, vet) && tagShut && lockLine.StartsWith($"Win the {library.Final.Name} final"));

        // 3. The prize and the gift: a save that beat her in the circuits (her signature paid then, beat:vera) beats her as a guest:
        // the prize once, no second gift, guestbeat:vera; a rematch and a loss raise nothing; New Game+ doesn't pay it again (prize: once).
        var noPass = BanPasses.LoadNoPass(Path.Combine(root, "campaign", "no-pass.txt"), cards);
        var unlocks = new CardUnlocks(cards, printings, circuits, roster, noPass, n => scoutFiles.TryGetValue(n, out var f) ? Deck.LoadYdk(f) : null, null, null, guests, library);
        int veraCircuit = -1, veraMatch = -1;
        for (int ci = 0; ci < circuits.All.Count && veraCircuit < 0; ci++)
            for (int mi = 0; mi < circuits.All[ci].Matches.Count; mi++)
                if (circuits.All[ci].Matches[mi].Character.Id == "vera") { veraCircuit = ci; veraMatch = mi; break; }
        var player = Profile.New(starter, cards, printings);
        var circuitWin = circuits.Record(player, veraCircuit, veraMatch, 0);
        var circuitGrants = unlocks.AfterDuel(player, circuitWin, circuits.All[veraCircuit].Band, null, false, false);
        bool giftedInCircuit = CardUnlocks.Events(circuitGrants).Contains("beat:vera");
        int copies = player.Collection.All.Count(), dp0 = player.Dp, duelDp = circuits.WinDp(circuits.Current(player));
        var first = GuestList.Record(player, circuits, vera, false, 0);
        int paid = player.Dp - dp0;
        var firstGrants = unlocks.AfterDuel(player, first, "", null, false, false);
        var firstEvents = Story.GuestEvents(vera, false, 0, first);
        var again = GuestList.Record(player, circuits, vera, false, 0);
        unlocks.AfterDuel(player, again, "", null, false, false);
        var againEvents = Story.GuestEvents(vera, false, 0, again);
        var loss = GuestList.Record(player, circuits, vera, false, 1);
        var lossEvents = Story.GuestEvents(vera, false, 1, loss);
        player.NewGamePlus();
        var nextCycle = GuestList.Record(player, circuits, vera, false, 0);
        var nextGrants = unlocks.AfterDuel(player, nextCycle, "", null, false, false);
        var eraLists = Ygo.Engine.DeckBuilding.Banlist.LoadFolders(new[] { Path.Combine(root, "lflists") });
        var sigProblems = unlocks.SignatureProblems(eraLists);
        Check($"beaten in the circuits first (her gift then: {giftedInCircuit}), her first guest win pays {first.Dp:N0} DP ({first.Summary}) with no second gift "
              + $"({string.Join(", ", firstGrants.Select(g => g.Kind))}) and raises [{string.Join(", ", firstEvents)}], not beat:vera; a rematch pays {again.Dp:N0} (WinDp {duelDp}) and raises "
              + $"[{string.Join(", ", againEvents)}], a loss [{string.Join(", ", lossEvents)}]; New Game+'s first win pays {nextCycle.Dp:N0}, no prize again ({nextCycle.FirstWin}) and still no gift; "
              + $"met at circuit {unlocks.MeetCircuit("vera")} (her match's); signature problems: {string.Join("; ", sigProblems)}",
            giftedInCircuit && first.FirstWin && first.Dp == duelDp + vera.Prize && paid == first.Dp + first.LevelUps.Sum(u => u.Dp)
            && !firstGrants.Any(g => g.Kind is GrantKind.RivalGift or GrantKind.BondGift && g.Character == "vera") && !CardUnlocks.Events(firstGrants).Contains("beat:vera")
            && firstEvents.SequenceEqual(new[] { Story.GuestBeatEvent("vera") }) && !again.FirstWin && again.Dp == duelDp && againEvents.Count == 0 && lossEvents.Count == 0
            && !nextCycle.FirstWin && nextCycle.Dp == circuits.WinDp(circuits.Current(player)) && !nextGrants.Any(g => g.Kind == GrantKind.RivalGift)
            && unlocks.MeetCircuit("vera") == veraCircuit && sigProblems.Count == 0);

        // 3b. "prize: <DP> once" (the owner, 2026-11-06): the post-final guests' prizes pay once ever, the others' each cycle.
        // New Game+ > win > New Game+ > win pays a once guest's prize once; an old save that won it this cycle (its guest:<id>
        // win, no guestprize mark) keeps it through its next New Game+; a guest without the flag pays again.
        var onceIds = guests.All.Where(g => g.PrizeOnce).Select(g => g.Character.Id).OrderBy(i => i).ToList();
        var onceProblems = new List<string>();
        foreach (var g in guests.All)
        {
            var cycler = Profile.New(starter, cards, printings);
            var prizes = new List<bool>();
            for (int c = 0; c < 3; c++)
            {
                cycler.NewGamePlus();
                int before = cycler.Dp;
                var won = GuestList.Record(cycler, circuits, g, false, 0);
                bool prizePaid = won.FirstWin && won.Dp == circuits.WinDp(circuits.Current(cycler)) + g.Prize;
                if (!prizePaid && won.Dp != circuits.WinDp(circuits.Current(cycler))) onceProblems.Add($"{g.Character.Id}: cycle {c + 1} paid {won.Dp}");
                prizes.Add(prizePaid);
                GuestList.Record(cycler, circuits, g, false, 0);
            }
            var expected = g.PrizeOnce ? new[] { true, false, false } : new[] { true, true, true };
            if (!prizes.SequenceEqual(expected)) onceProblems.Add($"{g.Character.Id}: prizes over three cycles {string.Join("/", prizes)}");
            if (GuestList.PrizeDue(g, cycler) != false) onceProblems.Add($"{g.Character.Id}: still due after a win");
            cycler.NewGamePlus();
            if (GuestList.PrizeDue(g, cycler) == g.PrizeOnce) onceProblems.Add($"{g.Character.Id}: due after New Game+ is {GuestList.PrizeDue(g, cycler)}");
        }
        var oldWinner = Profile.New(starter, cards, printings);
        oldWinner.MatchWins[GuestList.Key(ilse, false)] = 1; // won before the mark existed
        bool oldDueBefore = GuestList.PrizeDue(ilse, oldWinner);
        oldWinner.NewGamePlus();
        var oldAfter = GuestList.Record(oldWinner, circuits, ilse, false, 0);
        var onceParse = GuestList.Parse("guest: vera warriors\nprize: 700 once\nguest: ilse modern\nprize: 900\n", roster);
        Check($"\"prize: once\" guests ({string.Join(", ", onceIds)}) pay their prize once over New Game+ > win > New Game+ > win, the others each cycle; "
              + $"an old save that won Ilse this cycle (due {oldDueBefore}) isn't paid after its New Game+ ({oldAfter.Summary}); the flag parses (700 once, 900 not)"
              + (onceProblems.Count > 0 ? ": " + string.Join("; ", onceProblems.Take(6)) : ""),
            onceIds.SequenceEqual(new[] { "ilse", "vera" }) && onceProblems.Count == 0 && !oldDueBefore && !oldAfter.FirstWin
            && onceParse.All.Count == 2 && onceParse.All[0].Prize == 700 && onceParse.All[0].PrizeOnce && onceParse.All[1].Prize == 900 && !onceParse.All[1].PrizeOnce);

        // 4. Her scouting report is hers: Brask's and Ryo's warriors (the same deck file) don't get it.
        var warriorsDeck = Deck.LoadYdk(scoutFiles["warriors"]);
        var hers = scouting.For("warriors", warriorsDeck, cards, "vera");
        var others = new[] { "brask", "rookie-ryo" }.Where(id => scouting.For("warriors", warriorsDeck, cards, id).Written).ToList();
        Check($"her warriors report is written for her ({hers.Written}, for: {hers.For}) and reaches no one else's warriors ({string.Join(", ", others)})",
            hers.Written && hers.For == "vera" && others.Count == 0);

        // 5. The writers' triggers: guest:vera (Duel pressed, before the versus) and guestbeat:vera (the win's results).
        var triggers = new[] { Story.GuestEvent("vera"), Story.GuestBeatEvent("vera") };
        Check($"story.txt can take her scenes: {string.Join(" and ", triggers)} are triggers (guestbeat:<nobody> isn't)",
            triggers.All(triggerOk) && !triggerOk("guestbeat:nobody") && !triggerOk(Story.GuestBeatEvent("brask")));
        var written = triggers.Select(t => $"{t}: {string.Join(", ", story.Scenes.Where(sc => sc.Trigger == t).Select(sc => sc.Id).DefaultIfEmpty("none yet"))}");
        Console.WriteLine($"  info her scenes (the Narrative Designer's): {string.Join("; ", written)}");
    }

    private sealed class PosterIds : IEqualityComparer<Poster>
    {
        public bool Equals(Poster? a, Poster? b) => a?.Id == b?.Id;
        public int GetHashCode(Poster p) => p.Id.GetHashCode();
    }

}
