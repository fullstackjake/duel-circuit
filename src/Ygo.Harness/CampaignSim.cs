using System.Diagnostics;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--campaign-sim [--starter classic] [--pilot casual|skilled] [--seed N] [--save <mods folder>]`: a bot plays the whole campaign the way a
/// player would. It starts with a starter deck, plays each circuit's matches in order (retrying losses), spends its DP
/// on packs from the sets the shop offers, keeps a new deck built from its collection when that deck beats its current
/// one, and fixes its deck when a circuit's ban list hits it. Prints every duel and a summary per circuit, so walls
/// (matches that take many tries) and the DP economy show up. Each band ends with its DP ledger (earned by source,
/// spent by sink, the share spent; plan step 11), and `--save &lt;mods folder&gt;` writes the champion's save at the
/// Gauntlet clear for `--postgame-sim --load` and the app's -mods.
/// </summary>
public static class CampaignSim
{
    private const int MaxAttempts = 15, CompareGames = 12;
    /// <summary>Shop from the wishlist (YGO_SIM_WISHLIST=0 turns it off, for comparison).</summary>
    private static readonly bool UseWishlist = Environment.GetEnvironmentVariable("YGO_SIM_WISHLIST") != "0";
    /// <summary>Buy recipe packs, missing cards and selection packs (YGO_SIM_RECIPES=0 turns it off, for comparison).</summary>
    private static readonly bool UseRecipePacks = Environment.GetEnvironmentVariable("YGO_SIM_RECIPES") != "0";
    /// <summary>Spend Card Tokens on the target deck's missing cards (YGO_SIM_TOKENS=0 turns it off).</summary>
    private static readonly bool UseTokens = Environment.GetEnvironmentVariable("YGO_SIM_TOKENS") != "0";
    /// <summary>When a match is a wall: look wider every 3 losses, then the skilled pilot (YGO_SIM_WALLS=0 turns it off: 15 tries, then stop).</summary>
    private static readonly bool WallFallback = Environment.GetEnvironmentVariable("YGO_SIM_WALLS") != "0";
    private const int WallAttempts = 30;
    /// <summary>House packs (the shop rework): selection packs for the current and next circuit only (YGO_SIM_HOUSE=1).</summary>
    private static readonly bool HousePacks = Environment.GetEnvironmentVariable("YGO_SIM_HOUSE") == "1";

    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, string starterName, string pilot, int seed, string? savePath = null)
    {
        var timer = Stopwatch.StartNew();
        var rng = new Random(seed);
        var printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        var catalog = new PackCatalog(printings, cards);
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities"));
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var lists = Banlist.LoadFolders(new[] { Path.Combine(root, "vendor", "LFLists"), Path.Combine(root, "lflists") });
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());

        // Recipes a player could copy: the historic tournament decks.
        var recipes = Directory.GetFiles(Path.Combine(root, "decks", "history"), "*.ydk")
            .Select(f => { var d = Deck.LoadYdk(f); d.Name = Path.GetFileNameWithoutExtension(f); return d; }).ToList();
        var ratings = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName));
        var starter = Deck.LoadYdk(deckFiles[starterName]);
        starter.Name = starterName;
        var profile = Profile.New(starter, cards, printings);
        // A fixed salt, so a run with the same seed gets the same card unlocks.
        profile.Collection.Salt = (uint)seed * 2654435761u + 1;
        var deck = starter.Clone();
        // Card unlocks (rival gifts, drops, bonds, bounties, set completion), counted per band against everything the
        // collection gained there (the 15% budget, CARD-UNLOCKS-SPEC-2026-11-01).
        var noPass = BanPasses.LoadNoPass(Path.Combine(root, "campaign", "no-pass.txt"), cards);
        var unlocks = new CardUnlocks(cards, printings, circuits, roster, noPass,
            name => deckFiles.TryGetValue(name, out var f) ? Deck.LoadYdk(f) : null, catalog,
            BountyBoard.Load(Path.Combine(root, "campaign", BountyBoard.FileName)));
        var unlockCards = new Dictionary<string, int>();
        var acquired = new Dictionary<string, int>();
        var bandOrder = new List<string>();
        int CardsIn(IEnumerable<Grant> grants) => grants.Count(g => g.Copy != null || g.Kind == GrantKind.BountyToken);
        var style = pilot == "skilled" ? new Personality { Name = "Player", Skill = 0.9 } : new Personality { Name = "Player", Skill = 0.6 };
        Console.WriteLine($"Campaign playthrough: starter {starterName}, {pilot} pilot (skill {style.Skill}), seed {seed}");

        int totalDuels = 0, packsSinceBuild = 0, rebuilds = 0;
        // The decks the player owns outright (a recipe finished from its collection), and the recipes already in it.
        var stable = new List<Deck>();
        var stableRecipes = new HashSet<string>();
        Deck LoadDeck(string name) { var d = Deck.LoadYdk(deckFiles[name]); d.Name = name; return d; }
        double Elo(string name) => ratings.TryGetValue(name, out var e) ? e : 1400;
        var walls = new List<string>();
        // Cost per useful card (docs/dev/SHOP-PRICES-2026-11-01.md): per band and sink, DP spent and copies of the target's
        // missing cards it brought in; and the decks finished for the stable, with the duels it took.
        var useful = new Dictionary<(string band, string sink), (int dp, int copies)>();
        var builtLog = new List<(string band, string deck, int duel)>();
        void Useful(string band, string sink, int dp, int copies)
        {
            if (dp <= 0 && copies <= 0) return;
            var u = useful.GetValueOrDefault((band, sink));
            useful[(band, sink)] = (u.dp + dp, u.copies + copies);
        }
        // The DP ledger per band (plan step 11, decision 21): earned by source and spent by sink while the band was played.
        string? ledgerBand = null;
        var bandEarned = new Dictionary<string, int>(profile.DpEarned);
        var bandSpent = new Dictionary<string, int>(profile.DpSpent);
        int bandDpStart = profile.Dp;
        void BandLedger()
        {
            if (ledgerBand == null) return;
            PrintLedger($"{ledgerBand} ledger", Diff(profile.DpEarned, bandEarned), Diff(profile.DpSpent, bandSpent), bandDpStart, profile.Dp);
            var rows = useful.Where(u => u.Key.band == ledgerBand).OrderByDescending(u => u.Value.dp).ToList();
            Console.WriteLine("    useful copies by sink: " + (rows.Count == 0 ? "(none)" : string.Join(", ", rows.Select(u =>
                $"{u.Key.sink} {u.Value.copies} for {u.Value.dp:N0} DP ({(u.Value.copies > 0 ? $"{u.Value.dp / (double)u.Value.copies:N0} DP each" : "none useful")})"))));
            var built = builtLog.Where(b => b.band == ledgerBand).ToList();
            Console.WriteLine($"    decks finished: {(built.Count == 0 ? "(none)" : string.Join(", ", built.Select(b => $"{b.deck} at duel {b.duel}")))}");
            bandEarned = new Dictionary<string, int>(profile.DpEarned);
            bandSpent = new Dictionary<string, int>(profile.DpSpent);
            bandDpStart = profile.Dp;
        }
        for (int ci = 0; ci < circuits.All.Count; ci++)
        {
            var circuit = circuits.All[ci];
            if (circuit.Band != ledgerBand)
            {
                BandLedger();
                ledgerBand = circuit.Band;
                // YGO_SIM_SNAPSHOTS=<folder>: the profile as each band starts, for `--shop-probe --snapshots`.
                if (Environment.GetEnvironmentVariable("YGO_SIM_SNAPSHOTS") is { Length: > 0 } snaps)
                {
                    Directory.CreateDirectory(snaps);
                    profile.Save(Path.Combine(snaps, circuit.Band + ".txt"));
                }
            }
            var list = circuit.Banlist.Length > 0 ? lists.FirstOrDefault(l => l.Name == circuit.Banlist) ?? Banlist.None : Banlist.None;
            int duels = 0, wins = 0, dpStart = profile.Dp, packsStart = profile.PacksOpened, ownedStart = profile.Collection.Total;
            var simBand = unlocks.Bounties.BoardOf(circuit.Band) is { Length: > 0 } boardBand ? boardBand : circuit.Band;
            if (!bandOrder.Contains(simBand)) bandOrder.Add(simBand);

            // A new ban list (or a deck that somehow broke the rules): fix it.
            var problems = DeckRules.Validate(deck, list, cards).Concat(CollectionRules.Validate(deck, profile.Collection, cards)).ToList();
            if (problems.Count > 0)
            {
                var fixedDeck = AutoDeck.Fix(deck, profile.Collection, list, cards);
                deck = fixedDeck.Deck;
                Console.WriteLine($"  [{circuit.Name}] deck fixed for {(list.Name.Length > 0 ? list.Name : "the campaign")}: {string.Join("; ", fixedDeck.Changes)}");
                var left = DeckRules.Validate(deck, list, cards);
                if (left.Count > 0) Console.WriteLine($"    still illegal: {left[0].Message}");
            }

            // How close the collection is to the strongest recipes (could the player build several top decks by now?).
            var topRecipes = recipes.Where(r => ratings.ContainsKey(r.Name)).OrderByDescending(r => ratings[r.Name]).Take(10)
                .Select(r => $"{r.Name} {AutoDeck.Owned(r, profile.Collection, list, cards):P0}");
            Console.WriteLine($"  [{circuit.Name}] owns of the top-10 recipes: {string.Join(", ", topRecipes)}");
            Console.WriteLine($"  [{circuit.Name}] deck stable: {(stable.Count == 0 ? "(only the main deck)" : string.Join(", ", stable.Select(d => $"{d.Name} {Elo(d.Name):0}")))}");

            for (int mi = 0; mi < circuit.Matches.Count; mi++)
            {
                var match = circuit.Matches[mi];
                if (!deckFiles.TryGetValue(match.Deck, out var theirFile)) continue;
                var theirs = Deck.LoadYdk(theirFile);
                theirs.Name = match.Deck;
                int attempts = 0;
                // Counter-pick: the main deck, the stable, and the two strongest loaners, four test games each.
                var options = new List<(Deck deck, bool loaner)> { (deck, false) };
                options.AddRange(stable.Where(d => DeckRules.Validate(d, list, cards).Count == 0).Select(d => (d, false)));
                options.AddRange(RecipeBook.Unlocked(circuits, profile).Select(r => r.Deck).Distinct().Where(deckFiles.ContainsKey)
                    .Where(d => !stableRecipes.Contains(d)).OrderByDescending(Elo).Take(2)
                    .Select(d => (Loaners.Build(LoadDeck(d), list, cards), true)));
                var pick = options[0];
                if (options.Count > 1)
                {
                    int bestWins = -1;
                    foreach (var option in options)
                    {
                        int w = 0;
                        for (int t = 0; t < 4; t++) if (Play(cards, scripts, option.deck, theirs, style, match.Character, seed * 104729 + totalDuels * 7 + t) == 0) w++;
                        if (w > bestWins) { bestWins = w; pick = option; }
                    }
                    if (pick.deck != deck) Console.WriteLine($"    plays {pick.deck.Name}{(pick.loaner ? " (loaner)" : "")} against {match.Character.Name}");
                }
                var pilotNow = style;
                while (!profile.HasWon(ci, mi) && attempts < (WallFallback ? WallAttempts : MaxAttempts))
                {
                    // Stuck (docs/dev/SHOP-PRICES-2026-11-01.md): every 3 losses the player looks wider, at every loaner the
                    // Recipe Book lends and every deck they own (the Stuck? advice), 6 test games each; past MaxAttempts
                    // they've read the Scout report and learned the matchup (the skilled pilot) for this match only.
                    if (WallFallback && attempts > 0 && attempts % 3 == 0)
                    {
                        var wide = new List<(Deck deck, bool loaner)> { (deck, false) };
                        wide.AddRange(stable.Where(d => DeckRules.Validate(d, list, cards).Count == 0).Select(d => (d, false)));
                        wide.AddRange(RecipeBook.Unlocked(circuits, profile).Select(r => r.Deck).Distinct().Where(deckFiles.ContainsKey)
                            .Where(d => !stableRecipes.Contains(d)).OrderByDescending(Elo).Take(8)
                            .Select(d => (Loaners.Build(LoadDeck(d), list, cards), true)));
                        int bestWide = -1;
                        foreach (var option in wide)
                        {
                            int w = 0;
                            for (int t = 0; t < 6; t++) if (Play(cards, scripts, option.deck, theirs, pilotNow, match.Character, seed * 104729 + totalDuels * 11 + attempts * 3 + t) == 0) w++;
                            if (w > bestWide) { bestWide = w; pick = option; }
                        }
                        Console.WriteLine($"    stuck after {attempts}: plays {pick.deck.Name}{(pick.loaner ? " (loaner)" : "")} ({bestWide}/6 in tests)");
                    }
                    if (WallFallback && attempts == MaxAttempts) { pilotNow = new Personality { Name = "Player", Skill = 0.9 }; Console.WriteLine("    stuck: the skilled pilot takes over for this match"); }
                    attempts++;
                    duels++;
                    totalDuels++;
                    var tallies = new List<DuelTally>();
                    int winner = Play(cards, scripts, pick.deck, theirs, pilotNow, match.Character, seed * 7919 + totalDuels, tallies);
                    var reward = circuits.Record(profile, ci, mi, winner, loaner: pick.loaner);
                    var granted = unlocks.AfterDuel(profile, reward, circuit.Band, tallies.FirstOrDefault(), pick.loaner, tag: false);
                    unlockCards[simBand] = unlockCards.GetValueOrDefault(simBand) + CardsIn(granted);
                    foreach (var g in granted) Console.WriteLine($"    unlock: {g.Text}");
                    if (winner == 0) wins++;
                    Console.WriteLine($"  [{circuit.Name}] match {mi + 1} vs {match.Character.Name} ({match.Deck}): "
                                      + $"{(winner == 0 ? "won" : winner == 1 ? "lost" : "draw")} (try {attempts}) · {reward.Summary} · {profile.Dp} DP");

                    // Spend DP on packs, then see whether a deck rebuilt from the collection is better.
                    // Shop for a target: the strongest known deck you already own a good part of.
                    // The target: an unlocked recipe (recipe packs can finish it) or a known deck you own a good part of,
                    // never one already in the stable.
                    var unlocked = RecipeBook.Unlocked(circuits, profile).Select(r => r.Deck).Distinct().Where(deckFiles.ContainsKey)
                        .Where(d => !stableRecipes.Contains(d)).Select(LoadDeck).ToList();
                    bool late = circuits.IsLateBand(ci) && UseRecipePacks;
                    // In the late bands the shop also sells a selection pack for each deck of the circuits you've opened.
                    // YGO_SIM_HOUSE=1: the rework's house packs (SHOP-REWORK-SPEC section 3), the current and next circuit only,
                    // until Circuits.SelectionPackCircuits itself narrows (then this is the same list).
                    var houseCircuits = HousePacks
                        ? new[] { circuits.Current(profile), circuits.Current(profile) + 1 }.Where(i => i < circuits.All.Count && !circuits.IsCleared(i, profile)).ToList()
                        : circuits.SelectionPackCircuits(profile);
                    var selectionDecks = UseRecipePacks
                        ? houseCircuits.SelectMany(i => circuits.All[i].Matches.Select(m => m.Deck)).Distinct()
                            .Where(d => deckFiles.ContainsKey(d) && !stableRecipes.Contains(d) && unlocked.All(u => u.Name != d)).Select(LoadDeck).ToList()
                        : new List<Deck>();
                    var target = AutoDeck.BestRecipes(recipes.Where(r => !stableRecipes.Contains(r.Name)), profile.Collection, list, cards, 0.35)
                        .Select(r => (r.recipe, owned: r.owned, buyable: false))
                        .Concat(unlocked.Concat(selectionDecks).Select(r => (recipe: r, owned: AutoDeck.Owned(r, profile.Collection, list, cards), buyable: true)))
                        // Late on, a player works toward the strongest deck they can buy into, not the one they own most of.
                        .OrderByDescending(r => Elo(r.recipe.Name) + (late ? 120 : 300) * r.owned + (r.buyable ? (late ? 250 : 150) : 0))
                        .Select(r => r.recipe).FirstOrDefault();
                    var wanted = target == null ? new HashSet<uint>() : AutoDeck.Missing(target, profile.Collection, list, cards);
                    int shopYear = circuits.ShopYear(profile);
                    int bought = 0;
                    int MissingNow() => target == null ? 0 : RecipeShop.Missing(target, profile, list, shopYear, printings, cards).Values.Sum();
                    int spentNow() => profile.DpSpent.Values.Sum();
                    int miss0 = MissingNow(), spent0 = spentNow();
                    string sink = "recipe";
                    if (target != null && unlocked.Any(u => u.Name == target.Name) && UseRecipePacks)
                    {
                        // Finish it outright when that's affordable, else recipe packs.
                        int packPrice = RecipeShop.PackPriceFor(target.Name, circuits);
                        if (RecipeShop.CompletePrice(target, profile, list, shopYear, printings, cards, packPrice) is int price && price > 0 && price <= profile.Dp)
                            bought += RecipeShop.Complete(target, profile, list, shopYear, printings, cards, packPrice) > 0 ? 1 : 0;
                        while (profile.Dp >= packPrice && RecipeShop.Missing(target, profile, list, shopYear, printings, cards).Count > 0)
                        {
                            RecipeShop.OpenPack(target, profile, list, shopYear, printings, cards, rng, packPrice);
                            bought++;
                        }
                    }
                    else if (target != null && UseRecipePacks && selectionDecks.Any(d => d.Name == target.Name))
                    {
                        // Not unlocked yet: its selection pack, if the shop has one.
                        sink = "selection";
                        var selection = SelectionPacks.Build(target.Name, "SP-" + target.Name,
                            target.Main.Concat(target.Extra).Select(c => Banlist.BaseCode(c, cards)), profile, shopYear, printings, cards,
                            SelectionPacks.PriceFor(target.Name, circuits));
                        // Until the target's missing cards are in (CardCount is the pack's pool, which never shrinks).
                        while (selection.CardCount > 0 && profile.Dp >= selection.Price
                               && RecipeShop.Missing(target, profile, list, shopYear, printings, cards).Count > 0)
                        {
                            profile.Spend(selection.Sink, selection.Price);
                            profile.OpenPack(selection, rng, out _);
                            bought++;
                        }
                    }
                    // Late on, the DP is saved for the target's packs; random boosters only when there's nothing to aim for.
                    // Late on, the DP is saved for the target's own packs; boosters when the target can't be bought that way.
                    Useful(simBand, sink, spentNow() - spent0, miss0 - MissingNow());
                    miss0 = MissingNow(); spent0 = spentNow();
                    if (!late || target == null || bought == 0) bought += Shop(catalog, shopYear, profile, rng, wanted);
                    Useful(simBand, "boosters", spentNow() - spent0, miss0 - MissingNow());
                    miss0 = MissingNow();
                    // The wishlist the bounties pay from: the target's missing cards (the game's Recipe).
                    profile.Wishlist.Clear();
                    if (target != null) foreach (var w in AutoDeck.Wants(target, list, cards)) profile.Wishlist[w.Key] = w.Value;
                    var setGrants = unlocks.CheckSets(profile).Concat(unlocks.PayOwed(profile)).ToList();
                    unlockCards[simBand] = unlockCards.GetValueOrDefault(simBand) + CardsIn(setGrants);
                    foreach (var g in setGrants) Console.WriteLine($"    unlock: {g.Text}");
                    // A recipe now (almost) fully owned joins the stable.
                    foreach (var r in unlocked.Concat(recipes).Where(r => !stableRecipes.Contains(r.Name)).ToList())
                        if (AutoDeck.Owned(r, profile.Collection, list, cards) >= 0.95)
                        {
                            var built = AutoDeck.FromRecipe(r, profile.Collection, list, cards).Deck;
                            built.Name = r.Name;
                            if (built.Main.Count >= 40 && DeckRules.Validate(built, list, cards).Count == 0 && stableRecipes.Add(r.Name))
                            {
                                stable.Add(built);
                                builtLog.Add((ledgerBand ?? "", r.Name, totalDuels));
                                Console.WriteLine($"    built {r.Name} ({Elo(r.Name):0}) for the stable at duel {totalDuels}");
                            }
                        }
                    // Card Tokens (from levelling up) go on the wanted cards that are hardest to pull.
                    if (UseTokens)
                        foreach (var code in wanted.OrderByDescending(c => printings.Commonest(c, cards.Get(c)?.Alias ?? 0)).ToList())
                        {
                            if (profile.Tokens == 0) break;
                            while (profile.Tokens > 0 && profile.Collection.Count(code) < Collection.MaxCopies
                                   && profile.Collection.Count(code) < (target != null ? AutoDeck.Wants(target, list, cards).GetValueOrDefault(code, 1) : 1)
                                   && CardTrader.Trade(code, profile, circuits.ShopYear(profile), printings, cards))
                                bought++;
                        }
                    Useful(simBand, "tokens", 0, miss0 - MissingNow());
                    packsSinceBuild += bought;
                    if (packsSinceBuild >= 4)
                    {
                        packsSinceBuild = 0;
                        // Candidates: a deck built from the best cards, and the recipes (historic decks) you own most of.
                        var candidates = new List<(Deck deck, string what)>
                        {
                            (AutoDeck.Build(profile.Collection, list, cards, name: "rebuilt").Deck, "the best cards"),
                        };
                        foreach (var (recipe, owned) in AutoDeck.BestRecipes(recipes, profile.Collection, list, cards, 0.6).Take(3))
                            candidates.Add((AutoDeck.FromRecipe(recipe, profile.Collection, list, cards).Deck, $"the {recipe.Name} recipe ({owned:P0} owned)"));
                        (Deck deck, string what, int score)? best = null;
                        foreach (var (candidate, what) in candidates)
                        {
                            if (candidate.Main.Count < 40 || DeckRules.Validate(candidate, list, cards).Count > 0) continue;
                            int score = Compare(cards, scripts, candidate, deck, seed + totalDuels);
                            if (score > CompareGames / 2 && (best == null || score > best.Value.score)) best = (candidate, what, score);
                        }
                        if (best is { } b)
                        {
                            rebuilds++;
                            Console.WriteLine($"    rebuilt the deck from {b.what} (won {b.score}/{CompareGames} against the old one)");
                            deck = b.deck;
                        }
                    }
                }
                if (!profile.HasWon(ci, mi))
                {
                    walls.Add($"{circuit.Name} match {mi + 1} ({match.Character.Name}, {match.Deck})");
                    Console.WriteLine($"  WALL: {circuit.Name} match {mi + 1} not won in {MaxAttempts} tries.");
                }
            }
            acquired[simBand] = acquired.GetValueOrDefault(simBand) + Math.Max(0, profile.Collection.Total - ownedStart);
            Console.WriteLine($"== {ci + 1,2}. {circuit.Name,-38} {(circuits.IsCleared(ci, profile) ? "cleared" : "NOT cleared")} · level {profile.Level}, {profile.TokensSpent} tokens traded · "
                              + $"{wins}/{duels} duels won · DP {dpStart} -> {profile.Dp} · {profile.PacksOpened - packsStart} packs · "
                              + $"collection {profile.Collection.Distinct} different · {timer.Elapsed:hh\\:mm\\:ss}");
            if (!circuits.IsCleared(ci, profile)) break;
        }

        BandLedger();
        PrintLedger("Whole campaign ledger", profile.DpEarned, profile.DpSpent, Profile.StartingDp, profile.Dp);
        if (Ledger.Net(profile) != profile.Dp - Profile.StartingDp)
            Console.WriteLine($"LEDGER MISMATCH: net {Ledger.Net(profile)} vs Dp change {profile.Dp - Profile.StartingDp}");
        if (savePath != null)
        {
            if (circuits.IsCleared(circuits.All.Count - 1, profile))
                SaveChampion(savePath, profile, deck, stable, circuits, root, roster);
            else Console.WriteLine($"Not saved to {savePath}: the Gauntlet wasn't cleared.");
        }
        Console.WriteLine();
        Console.WriteLine($"Summary: {circuits.ClearedCount(profile)}/{circuits.All.Count} circuits cleared in {totalDuels} duels, "
                          + $"{profile.PacksOpened} packs, {rebuilds} deck rebuilds, {profile.Collection.Total} cards owned, {profile.Dp} DP left, {timer.Elapsed:hh\\:mm\\:ss}");
        Console.WriteLine(walls.Count == 0 ? "No walls." : "Walls: " + string.Join("; ", walls));
        Console.WriteLine("Card unlocks per band (cards and tokens paid / the collection's growth there): "
                          + string.Join(", ", bandOrder.Select(b => $"{b} {unlockCards.GetValueOrDefault(b)}/{acquired.GetValueOrDefault(b)}"
                              + (acquired.GetValueOrDefault(b) > 0 ? $" ({unlockCards.GetValueOrDefault(b) / (double)acquired[b]:P1})" : ""))));
        return 0;
    }

    internal static Dictionary<string, int> Diff(Dictionary<string, int> now, Dictionary<string, int> before) =>
        now.Select(p => (p.Key, v: p.Value - before.GetValueOrDefault(p.Key))).Where(p => p.v != 0).ToDictionary(p => p.Key, p => p.v);

    /// <summary>Prints a ledger: DP earned by source and spent by sink, and the share of the DP on hand that was spent.</summary>
    internal static void PrintLedger(string title, IReadOnlyDictionary<string, int> earned, IReadOnlyDictionary<string, int> spent, int dpStart, int dpEnd)
    {
        int e = earned.Values.Sum(), sp = spent.Values.Sum();
        string Rows(IReadOnlyDictionary<string, int> d, int total) => d.Count == 0 ? "(none)" :
            string.Join(", ", d.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value:N0} ({x.Value / (double)Math.Max(1, total):P0})"));
        Console.WriteLine($"  {title}: DP {dpStart:N0} -> {dpEnd:N0} · earned {e:N0} · spent {sp:N0} = {sp / (double)Math.Max(1, e):P1} of the earnings, "
                          + $"{sp / (double)Math.Max(1, dpStart + e):P1} of the DP on hand (start + earned)");
        Console.WriteLine($"    earned by source: {Rows(earned, e)}");
        Console.WriteLine($"    spent by sink:    {Rows(spent, sp)}");
    }

    /// <summary>
    /// `--save &lt;path&gt;`: the champion's save as the app keeps it under -mods: &lt;mods&gt;/save/profile.txt and the decks it
    /// plays as campaign decks in &lt;mods&gt;/decks (the deck in use first). A path ending in .txt is the profile itself
    /// (its decks go to ../decks beside its folder). The Library opens on load (Library.Open after the Gauntlet).
    /// </summary>
    internal static void SaveChampion(string savePath, Profile profile, Deck main, IEnumerable<Deck> stable, Circuits circuits, string root, IEnumerable<Personality> roster)
    {
        // The main campaign's scenes count as seen (the sim doesn't show them); the post-game's wait for their events.
        var library = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), roster, circuits);
        var postGame = new HashSet<string>(library.Cups.Select(c => c.Name).Append(library.Final.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var scene in Story.Load(Path.Combine(root, "campaign", "story.txt")).Scenes)
        {
            var trigger = scene.Trigger;
            var colon = trigger.IndexOf(':');
            var head = colon < 0 ? trigger : trigger.Substring(0, colon);
            var arg = colon < 0 ? "" : trigger.Substring(colon + 1);
            bool later = new[] { "library", "final", "round", "locals", "bracketwin", "stamp", "table", "ngplus", "setcomplete", "partner" }.Contains(head)
                         || (head is "open" or "cupclear" && postGame.Contains(arg));
            if (!later) Story.MarkSeen(profile, scene);
        }
        string profilePath, decksDir;
        if (savePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            profilePath = Path.GetFullPath(savePath);
            decksDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(profilePath)!)!, "decks");
        }
        else
        {
            profilePath = Path.Combine(Path.GetFullPath(savePath), "save", "profile.txt");
            decksDir = Path.Combine(Path.GetFullPath(savePath), "decks");
        }
        Directory.CreateDirectory(decksDir);
        var saved = new List<string>();
        foreach (var d in new[] { main }.Concat(stable))
        {
            var copy = d.Clone();
            copy.Name = DeckLibrary.SafeName(d.Name == "rebuilt" || d.Name.Length == 0 ? "Champion deck" : d.Name);
            if (saved.Contains(copy.Name)) continue;
            copy.Campaign = true;
            File.WriteAllText(Path.Combine(decksDir, copy.Name + ".ydk"), DeckCodes.ToYdk(copy));
            saved.Add(copy.Name);
        }
        profile.Deck = saved[0];
        // The app's own layout bookkeeping, so it doesn't migrate the match keys.
        profile.CircuitLayout = circuits.All.Select(c => c.Name).ToList();
        profile.TagCupLayout = circuits.TagCups.Select(c => c.Name).ToList();
        profile.Save(profilePath);
        Console.WriteLine($"Saved the champion to {profilePath} ({profile.Dp:N0} DP, {profile.Collection.Total} copies; decks {string.Join(", ", saved)} in {decksDir}). "
                          + $"Run the app with -mods {Path.GetDirectoryName(Path.GetDirectoryName(profilePath))}");
    }

    /// <summary>One campaign duel: 0 = the player won, 1 = the opponent, 2 = draw. Who goes first alternates by seed.</summary>
    private static int Play(ICardDataProvider cards, IScriptProvider scripts, Deck mine, Deck theirs, Personality pilot, Personality opponent, int gameSeed,
        List<DuelTally>? tallies = null)
    {
        bool meFirst = (gameSeed & 1) == 0;
        var decks = meFirst ? new[] { mine, theirs } : new[] { theirs, mine };
        var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)gameSeed }, Deck0 = decks[0], Deck1 = decks[1] };
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var answers = new ResponseLog();
        IDuelist Bot(Personality style, Deck deck, int seat, bool knowsDeck)
        {
            var playbook = knowsDeck ? Playbooks.ForDeck(deck) : null;
            var planner = style.PlansAhead ? new TurnPlanner(recipe, answers, style, () => knowsDeck ? Playbooks.ForDeck(deck) : null) : null;
            return new SmartDuelist(style.Name, gameSeed * 2 + seat, context, null, style, playbook, planner);
        }
        int mySeat = meFirst ? 0 : 1;
        var seats = new IDuelist[2];
        seats[mySeat] = Bot(pilot, mine, mySeat, knowsDeck: false);
        seats[1 - mySeat] = Bot(opponent, theirs, 1 - mySeat, opponent.KnowsDeck);
        var runner = new DuelRunner(duel, seats[0], seats[1], answers);
        runner.MessageReceived += context.Observe;
        if (tallies != null)
        {
            var tally = new DuelTally(cards, mySeat);
            runner.MessageReceived += tally.Observe;
            tallies.Add(tally);
        }
        YataLock.Attach(runner, duel);
        int winner;
        try { winner = runner.Run(8000).Winner; }
        catch { winner = 2; }
        return winner is 0 or 1 ? (winner == mySeat ? 0 : 1) : 2;
    }

    /// <summary>Games won by deck <paramref name="a"/> against deck <paramref name="b"/>, both played by the same quick bot.</summary>
    private static int Compare(ICardDataProvider cards, IScriptProvider scripts, Deck a, Deck b, int seed)
    {
        var style = Personality.Balanced;
        int wins = 0;
        Parallel.For(0, CompareGames, g =>
        {
            int gameSeed = seed * 31 + g;
            bool aFirst = g % 2 == 0;
            try
            {
                using var duel = new Duel(cards, scripts, new DuelSettings { Seed = (ulong)gameSeed });
                duel.AddDeck(0, aFirst ? a : b);
                duel.AddDeck(1, aFirst ? b : a);
                var context = new DuelContext(duel, cards);
                var runner = new DuelRunner(duel, new SmartDuelist("p0", gameSeed * 2, context, null, style),
                    new SmartDuelist("p1", gameSeed * 2 + 1, context, null, style));
                runner.MessageReceived += context.Observe;
                var w = runner.Run(6000).Winner;
                if (w is 0 or 1 && (w == 0) == aFirst) Interlocked.Increment(ref wins);
            }
            catch { }
        });
        return wins;
    }

    /// <summary>
    /// Spends DP on packs. With a wishlist, mostly from the set that gives the most wanted cards per pack (the shop's
    /// wishlist view); otherwise the newest sets the shop has, with some variety. Returns packs bought.
    /// </summary>
    private static int Shop(PackCatalog catalog, int year, Profile profile, Random rng, HashSet<uint> wanted)
    {
        var available = catalog.Sets.Where(s => s.Info.Date.Length >= 4 && int.Parse(s.Info.Date.Substring(0, 4)) <= year).ToList();
        if (available.Count == 0) return 0;
        int newest = available.Max(s => int.Parse(s.Info.Date.Substring(0, 4)));
        var recent = available.Where(s => int.Parse(s.Info.Date.Substring(0, 4)) >= newest - 3).ToList();
        int bought = 0;
        PackSet? best = null;
        if (wanted.Count > 0 && UseWishlist)
            // Wanted cards per DP (with one price everywhere, the same as per pack).
            best = available.Select(s => (set: s, e: PackCatalog.Expected(s, wanted) / s.Price)).Where(x => x.e > 0)
                .OrderByDescending(x => x.e).Select(x => x.set).FirstOrDefault();
        int cheapest = available.Min(s => s.Price);
        while (profile.Dp >= cheapest)
        {
            var set = best != null && rng.NextDouble() < 0.8 ? best
                : rng.NextDouble() < 0.75 ? recent[rng.Next(recent.Count)] : available[rng.Next(available.Count)];
            // A set it can't afford now: the cheapest of the recent ones, else stop.
            if (set.Price > profile.Dp) set = recent.Where(s => s.Price <= profile.Dp).OrderBy(s => s.Price).FirstOrDefault() ?? set;
            if (!profile.Spend(set.Sink, set.Price)) break;
            profile.OpenPack(set, rng, out _);
            bought++;
        }
        return bought;
    }
}
