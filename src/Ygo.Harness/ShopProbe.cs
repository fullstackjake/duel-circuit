using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--prices &lt;variant&gt;` (any mode) sets the shop's price knobs (<see cref="ShopPrices"/>) before the run:
/// base (today), sel (selection packs by band), novice (Novice recipe packs at 450), year (boosters by year), foil2 (sets
/// with no Commons at 2x), all (every one); comma-separated, and each can take its numbers: sel=200/200/400/650/1000/1600,
/// novice=450, year=150/200/300/450/600, foil=2.
/// <para>
/// `--shop-probe [--prices X] [--snapshots &lt;folder&gt;]`: what completing each band's decks costs at those prices, by
/// recipe packs, selection packs and the best booster, from the starter collection and (with --snapshots, written by
/// `--campaign-sim` under YGO_SIM_SNAPSHOTS) from the sim's collection as the band starts; then booster boxes and the
/// sets with no Commons (docs/dev/SHOP-PRICES-2026-11-01.md).
/// </para>
/// </summary>
public static class ShopProbe
{
    public static string Apply(string variant)
    {
        foreach (var part in variant.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2);
            int[] Nums() => kv[1].Split('/').Select(int.Parse).ToArray();
            switch (kv[0])
            {
                case "base": ShopPrices.UseToday(); break; // the prices from before the rework (the defaults are the approved ones)
                case "all":
                    ShopPrices.SelectionBandPricing = true; ShopPrices.NoviceRecipePack = 450; ShopPrices.BoosterYearPricing = true; ShopPrices.NoCommonsFactor = 2;
                    break;
                case "sel":
                    ShopPrices.SelectionBandPricing = true;
                    if (kv.Length > 1) ShopPrices.SelectionByBand = Nums();
                    break;
                case "novice": ShopPrices.NoviceRecipePack = kv.Length > 1 ? int.Parse(kv[1]) : 450; break;
                case "year":
                    ShopPrices.BoosterYearPricing = true;
                    if (kv.Length > 1)
                    {
                        var n = Nums();
                        ShopPrices.BoosterByYear = ShopPrices.BoosterByYear.Select((b, i) => (b.upTo, i < n.Length ? n[i] : b.price)).ToArray();
                    }
                    break;
                case "foil2": ShopPrices.NoCommonsFactor = 2; break;
                // By the set's base rarity: Rare 1.5x, Super 2.5x, Ultra or better 4x (foiltier=1.5/2.5/4).
                case "foiltier":
                {
                    var f = kv.Length > 1 ? kv[1].Split('/').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray() : new[] { 1.5, 2.5, 4.0 };
                    ShopPrices.NoCommonsByBase = new[] { (Rarity.UltraRare, f[2]), (Rarity.SuperRare, f[1]), (Rarity.Common, f[0]) };
                    break;
                }
                case "foil": ShopPrices.NoCommonsFactor = double.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture); break;
                default: throw new ArgumentException($"--prices: unknown part '{part}'");
            }
        }
        return Describe();
    }

    public static string Describe() =>
        $"recipe packs Novice {ShopPrices.NoviceRecipePack}; selection packs "
        + (ShopPrices.SelectionBandPricing ? string.Join("/", ShopPrices.SelectionByBand) + " by band" : $"{ShopPrices.SelectionFlat} flat")
        + "; boosters " + (ShopPrices.BoosterYearPricing ? string.Join("/", ShopPrices.BoosterByYear.Select(b => b.price)) + " by year" : $"{PackCatalog.DefaultPrice} flat")
        + (ShopPrices.NoCommonsByBase is { } byBase ? "; no-Commons sets by base rarity " + string.Join(", ", byBase.Select(b => $"{b.atLeast}+ x{b.factor:0.##}"))
            : $"; no-Commons sets x{ShopPrices.NoCommonsFactor:0.##}");

    private static readonly string[] Bands = { "Beginner", "Novice", "Intermediate", "Advanced", "Expert", "Master", "Gauntlet" };

    public static int Run(ICardDataProvider cards, string root, string? snapshots, int seed, bool a0 = false)
    {
        var printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        IReadOnlyList<PackSet> sets = a0 ? RoughA0(printings, cards) : new PackCatalog(printings, cards).Sets;
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities"));
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        Deck LoadDeck(string name) { var d = Deck.LoadYdk(deckFiles[name]); d.Name = name; return d; }
        Rarity Commonest(uint c) => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
        var starter = LoadDeck("classic");
        Console.WriteLine($"Shop probe: {Describe()}; catalog {(a0 ? "with the rough A0 fix" : "as built")}: {sets.Count} sets");
        foreach (var g in sets.GroupBy(x => int.TryParse(x.Year, out var y) ? Array.FindIndex(ShopPrices.BoosterByYear, b => y <= b.upTo) : -1).OrderBy(g => g.Key))
            Console.WriteLine($"  up to {ShopPrices.BoosterByYear[Math.Max(0, g.Key)].upTo}: {g.Count()} sets ({g.Count(x => !ShopPrices.NoCommons(x))} core, {g.Count(ShopPrices.NoCommons)} rarity), prices {string.Join("/", g.Select(x => x.Price).Distinct().OrderBy(x => x))}");

        // The decks first met in each band (their recipe's band prices their packs).
        var recipes = RecipeBook.All(circuits).Where(r => r.Circuit >= 0 && deckFiles.ContainsKey(r.Deck))
            .GroupBy(r => r.Deck).Select(g => g.First()).ToList();

        Profile Start(string band)
        {
            Profile? p = null;
            if (snapshots != null && File.Exists(Path.Combine(snapshots, band + ".txt"))) p = Profile.Load(Path.Combine(snapshots, band + ".txt"));
            p ??= Profile.New(starter, cards, printings);
            p.Collection.CommonestOf = Commonest;
            p.Dp = 50_000_000;
            return p;
        }
        int Net(Profile p, int spent0, int earned0) => p.DpSpent.Values.Sum() - spent0 - (p.DpEarned.Values.Sum() - earned0);

        Console.WriteLine();
        Console.WriteLine($"Completing a deck ({(snapshots != null ? "from the sim's collection as the band starts" : "from the starter collection")}; net of refunds; median of the band's decks, 6 tries each):");
        Console.WriteLine("  band          | first-win pay | missing copies | recipe pack: price, packs, net DP, DP/useful | selection: price, packs, net DP, DP/useful | best booster in the band's shop years: DP/useful (first pack) | outright | selection-then-recipe mix");
        foreach (var band in Bands)
        {
            var decks = recipes.Where(r => circuits.All[r.Circuit].Band == band).Select(r => r.Deck).ToList();
            if (decks.Count == 0) continue;
            var bandCircuits = Enumerable.Range(0, circuits.All.Count).Where(i => circuits.All[i].Band == band).ToList();
            double firstWin = bandCircuits.Average(i => circuits.WinDp(i) + Circuits.FirstWinBonus + circuits.All[i].Prize / (double)Math.Max(1, circuits.All[i].Matches.Count));
            var rows = new List<(int missing, int recipePacks, int recipeNet, int selPacks, int selNet, double boosterPer, int outright, int mixSel, int mixNet)>();
            int recipePrice = 0, selPrice = 0;
            foreach (var name in decks)
            {
                var deck = LoadDeck(name);
                var pool = deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, cards)).ToList();
                recipePrice = RecipeShop.PackPriceFor(name, circuits);
                selPrice = SelectionPacks.PriceFor(name, circuits);
                var probe = Start(band);
                int missing = RecipeShop.Missing(deck, probe, Banlist.None, 9999, printings, cards).Values.Sum();
                if (missing == 0) continue;
                int outright = RecipeShop.CompletePrice(deck, probe, Banlist.None, 9999, printings, cards, recipePrice);
                var wanted = AutoDeck.Missing(deck, probe.Collection, Banlist.None, cards);
                // The shop opens up to the next band's era (by levels) while you play this band.
                int bandEra = circuits.All[bandCircuits[0]].Era;
                int maxYear = circuits.All.Select(c => c.Era).Where(e => e > bandEra).DefaultIfEmpty(9999).Min();
                var best = sets.Where(s => int.TryParse(s.Year, out var y) && y <= maxYear)
                    .Select(s => (s, per: PackCatalog.Expected(s, wanted, 60))).Where(x => x.per > 0)
                    .Select(x => x.s.Price / x.per).DefaultIfEmpty(double.NaN).Min();
                for (int t = 0; t < 6; t++)
                {
                    var rng = new Random(seed * 1000 + t);
                    var a = Start(band);
                    int s0 = a.DpSpent.Values.Sum(), e0 = a.DpEarned.Values.Sum(), rp = 0;
                    while (RecipeShop.Missing(deck, a, Banlist.None, 9999, printings, cards).Count > 0 && rp < 400)
                    { RecipeShop.OpenPack(deck, a, Banlist.None, 9999, printings, cards, rng, recipePrice); rp++; }
                    int rNet = Net(a, s0, e0);
                    var b = Start(band);
                    s0 = b.DpSpent.Values.Sum(); e0 = b.DpEarned.Values.Sum();
                    var sel = SelectionPacks.Build(name, "SP-" + name, pool, b, 9999, printings, cards, selPrice);
                    int sp = 0;
                    while (RecipeShop.Missing(deck, b, Banlist.None, 9999, printings, cards).Count > 0 && sp < 400 && sel.CardCount > 0)
                    { b.Spend(sel.Sink, sel.Price); b.OpenPack(sel, rng, out _); sp++; }
                    int sNet = Net(b, s0, e0);
                    // The mix a sharp player uses: selection packs while they bring in more missing copies per DP than
                    // a recipe pack would (9 missing copies, or what's left), then recipe packs to finish.
                    var m = Start(band);
                    s0 = m.DpSpent.Values.Sum(); e0 = m.DpEarned.Values.Sum();
                    var msel = SelectionPacks.Build(name, "SP-" + name, pool, m, 9999, printings, cards, selPrice);
                    int mp = 0, left = RecipeShop.Missing(deck, m, Banlist.None, 9999, printings, cards).Values.Sum();
                    var recent = new Queue<int>();
                    while (left > 0 && msel.CardCount > 0 && mp < 400)
                    {
                        if (recent.Count == 3 && recent.Average() / selPrice < Math.Min(PackCatalog.PackSize, left) / (double)recipePrice) break;
                        m.Spend(msel.Sink, msel.Price); m.OpenPack(msel, rng, out _); mp++;
                        int now = RecipeShop.Missing(deck, m, Banlist.None, 9999, printings, cards).Values.Sum();
                        recent.Enqueue(left - now); if (recent.Count > 3) recent.Dequeue();
                        left = now;
                    }
                    int mr = 0;
                    while (RecipeShop.Missing(deck, m, Banlist.None, 9999, printings, cards).Count > 0 && mr < 400)
                    { RecipeShop.OpenPack(deck, m, Banlist.None, 9999, printings, cards, rng, recipePrice); mr++; }
                    rows.Add((missing, rp, rNet, sp, sNet, best, outright, mp, Net(m, s0, e0)));
                }
            }
            if (rows.Count == 0) { Console.WriteLine($"  {band,-13} | (every deck already owned)"); continue; }
            T Med<T>(Func<(int missing, int recipePacks, int recipeNet, int selPacks, int selNet, double boosterPer, int outright, int mixSel, int mixNet), T> f) => rows.Select(f).OrderBy(x => x).ElementAt(rows.Count / 2);
            int miss = Med(r => r.missing), rNetM = Med(r => r.recipeNet), sNetM = Med(r => r.selNet);
            Console.WriteLine($"  {band,-13} | {firstWin,13:N0} | {miss,14} | {recipePrice,5} x{Med(r => r.recipePacks),3} = {rNetM,7:N0} ({rNetM / (double)miss,4:N0}/card, {rNetM / firstWin:0.0} wins)"
                              + $" | {selPrice,5} x{Med(r => r.selPacks),3} = {sNetM,7:N0} ({sNetM / (double)miss,4:N0}/card, {sNetM / firstWin:0.0} wins)"
                              + $" | {Med(r => r.boosterPer),6:N0} | {Med(r => r.outright),7:N0}"
                              + $" | mix {Med(r => r.mixSel),3} sel then recipe = {Med(r => r.mixNet),7:N0} ({Med(r => r.mixNet) / (double)miss,4:N0}/card, {Med(r => r.mixNet) / firstWin:0.0} wins, {1 - Med(r => r.mixNet) / (double)Math.Max(1, rNetM):P0} under recipe-only)");
        }

        HouseAvailability(circuits, deckFiles.ContainsKey);
        Boxes(sets, printings, cards, seed);
        RaritySets(sets, printings, cards, seed);
        return 0;
    }

    // ---- The rework's catalog fix (A0, rough) and rarity-set opening (A1), harness-side, to measure prices on them. ----

    /// <summary>PackCatalog's "not a booster" words (copied; the rework matches them as whole words).</summary>
    private static readonly string[] NotBoosters =
    {
        "Structure Deck", "Starter Deck", "Starter Kit", "Speed Duel", "Tin", "Promo", "Prize", "Tournament", "Championship",
        "Duelist League", "Sneak Peek", "Special Edition", "Deck:", "Collector's Box", "Box Set", "Gift", "Mega Pack",
        "OTS", "Participation", "Rush", "Advent Calendar", "Egyptian God Deck", "Power-Up", "Value Box", "Mat",
        "Sample", "Demo", "Deluxe Edition", "Retro Pack", "Shonen Jump", "Forbidden Memories", "Bundle", "Promotional",
        "Hobby League", "Premium Pack", "Movie Pack", "Duel Terminal", "Jump", "Magazine", "Video Game", "Exclusive",
    };

    /// <summary>
    /// A rough A0 (SHOP-REWORK-SPEC "A0, the catalog fix"): a set's pool is its printings under the prefix's most common
    /// name, the words match whole words, and the 80% rule counts distinct card numbers. Priced by ShopPrices.
    /// </summary>
    public static List<PackSet> RoughA0(PrintingIndex printings, ICardDataProvider cards)
    {
        var byPrefix = new Dictionary<string, List<(uint code, Printing p)>>();
        foreach (var pair in printings.All)
        {
            var info = cards.Get(pair.Key);
            if (info == null || info.Alias != 0) continue;
            foreach (var p in pair.Value)
            {
                var prefix = PrintingIndex.SetPrefix(p.SetCode);
                if (!byPrefix.TryGetValue(prefix, out var l)) byPrefix[prefix] = l = new List<(uint, Printing)>();
                l.Add((pair.Key, p));
            }
        }
        bool Word(string name, string w) => System.Text.RegularExpressions.Regex.IsMatch(name,
            $"(?<![A-Za-z]){System.Text.RegularExpressions.Regex.Escape(w)}(?![A-Za-z])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var sets = new List<PackSet>();
        foreach (var (prefix, list) in byPrefix)
        {
            if (!printings.Sets.TryGetValue(prefix, out var info) || info.Date.Length == 0) continue;
            var name = list.GroupBy(x => x.p.SetName).OrderByDescending(g => g.Count()).First().Key;
            if (NotBoosters.Any(w => Word(name, w))) continue;
            var mine = list.Where(x => x.p.SetName == name).ToList();
            var set = new PackSet { Info = new SetInfo { Code = info.Code, Name = name, Date = info.Date, CardCount = info.CardCount } };
            foreach (var (code, p) in mine)
            {
                if (!set.Pool.TryGetValue(p.Rarity, out var l)) set.Pool[p.Rarity] = l = new List<uint>();
                if (!l.Contains(code)) l.Add(code);
            }
            int numbers = mine.Select(x => x.p.SetCode).Distinct().Count();
            if (set.CardCount < 30 || set.Pool.Keys.Count < 2 || set.CardCount < numbers * 0.8) continue;
            set.Price = ShopPrices.Booster(set);
            sets.Add(set);
        }
        return sets.OrderByDescending(s => s.Info.Date).ThenBy(s => s.Info.Name).ToList();
    }

    /// <summary>PackCatalog's foil weights (copied).</summary>
    private static readonly Dictionary<Rarity, double> FoilWeights = new Dictionary<Rarity, double>
    {
        [Rarity.SuperRare] = 0, [Rarity.UltraRare] = 18, [Rarity.UltraParallelRare] = 6, [Rarity.SuperParallelRare] = 6,
        [Rarity.StarfoilRare] = 20, [Rarity.MosaicRare] = 20, [Rarity.ShatterfoilRare] = 20, [Rarity.PharaohsRare] = 6,
        [Rarity.GoldRare] = 18, [Rarity.PremiumGoldRare] = 8, [Rarity.PlatinumRare] = 6, [Rarity.SecretRare] = 9,
        [Rarity.PrismaticSecretRare] = 7, [Rarity.GoldSecretRare] = 6, [Rarity.ExtraSecretRare] = 2, [Rarity.PlatinumSecretRare] = 5,
        [Rarity.UltimateRare] = 5, [Rarity.CollectorsRare] = 3, [Rarity.GhostGoldRare] = 1.5, [Rarity.GhostRare] = 1.5,
        [Rarity.QuarterCenturySecretRare] = 3, [Rarity.StarlightRare] = 1,
    };

    /// <summary>
    /// One pack with the rework's A1: a rarity set (ShopPrices.NoCommons) opens 7 cards of its base rarity (stray cards
    /// below it drawn with it) and 2 foil slots, and a foil roll of "nothing" gives the lowest rarity above the base with
    /// foil weight 0 (Super Rare over a Rare base). Any other set opens as the engine opens it.
    /// </summary>
    public static List<Pull> OpenA1(PackSet set, Random rng)
    {
        if (!ShopPrices.NoCommons(set)) return PackCatalog.Open(set, rng);
        var baseRarity = ShopPrices.BaseRarity(set);
        var baseTier = set.Pool.Where(p => p.Key <= baseRarity).SelectMany(p => p.Value.Select(c => new Pull(c, p.Key))).ToList();
        var fallback = set.Pool.Keys.Where(r => r > baseRarity && FoilWeights.TryGetValue(r, out var w) && w == 0).DefaultIfEmpty(baseRarity).Min();
        var pulls = new List<Pull>();
        for (int i = 0; i < 7; i++) pulls.Add(baseTier[rng.Next(baseTier.Count)]);
        for (int i = 0; i < 2; i++)
        {
            var options = set.Pool.Keys.Where(r => r > baseRarity && FoilWeights.TryGetValue(r, out var w) && w > 0).Select(r => (r, w: FoilWeights[r])).ToList();
            var roll = rng.NextDouble() * (36 + options.Sum(o => o.w));
            Pull? got = null;
            foreach (var (r, w) in options) { if (roll < w) { var l = set.Pool[r]; got = new Pull(l[rng.Next(l.Count)], r); break; } roll -= w; }
            if (got == null) { var l = set.Pool[fallback]; got = fallback == baseRarity ? baseTier[rng.Next(baseTier.Count)] : new Pull(l[rng.Next(l.Count)], fallback); }
            pulls.Add(got.Value);
        }
        return pulls.OrderBy(p => p.Rarity).ToList();
    }

    /// <summary>
    /// A box as the spec builds it: <see cref="ShopPrices.BoxPacks"/> packs (A1 opening), the guarantee as an upgrade of one
    /// pack's best card, every pull's share of the box price over all its pulls, 4th copies' DP capped at
    /// <see cref="ShopPrices.BoxRefundShare"/> of the price. Spends nothing (the caller does).
    /// </summary>
    public static (int worth, int tops, bool upgraded, int refund) OpenBox(Profile p, PackSet set, int price, Random rng)
    {
        var top = ShopPrices.BoxTop(set);
        var packs = Enumerable.Range(0, ShopPrices.BoxPacks).Select(_ => OpenA1(set, rng)).ToList();
        bool upgraded = false;
        if (top is { } t && !packs.Any(pk => pk.Any(x => x.Rarity == t)))
        {
            var pack = packs[rng.Next(packs.Count)];
            var l = set.Pool[t];
            pack[pack.Count - 1] = new Pull(l[rng.Next(l.Count)], t);
            upgraded = true;
        }
        var pulls = packs.SelectMany(x => x).ToList();
        Rarity C(uint code) => p.Collection.CommonestOf?.Invoke(code) ?? Rarity.Common;
        var shares = Pricing.Shares(price, pulls.Select(x => Pricing.Basis(x.Rarity, C(x.Code))).ToList());
        int sold = 0;
        for (int i = 0; i < pulls.Count; i++) sold += p.Collection.AddPulled(pulls[i].Code, pulls[i].Rarity, CopySource.Pack, shares[i]).Dp;
        int refund = Math.Min(sold, (int)(price * ShopPrices.BoxRefundShare));
        p.Earn(Ledger.PackRefund, refund);
        p.PacksOpened += ShopPrices.BoxPacks;
        return (pulls.Sum(x => Pricing.Basis(x.Rarity, C(x.Code))), top == null ? 0 : pulls.Count(x => x.Rarity == top), upgraded, refund);
    }

    private static void Boxes(IReadOnlyList<PackSet> sets, PrintingIndex printings, ICardDataProvider cards, int seed)
    {
        Rarity Commonest(uint c) => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
        Profile Fresh() { var p = new Profile { Dp = 10_000_000 }; p.Collection.CommonestOf = Commonest; return p; }
        // Core sets across the year brackets, plus the spec's watch list (RA04, RA05, BLGG) and a rarity set.
        var core = sets.Where(s => !ShopPrices.NoCommons(s)).ToList();
        var picks = ShopPrices.BoosterByYear.Select(b => b.upTo)
            .SelectMany(y => core.Where(s => int.TryParse(s.Year, out var sy) && sy <= y).Take(2)).ToList();
        picks.AddRange(sets.Where(s => s.Info.Code is "RA04" or "RA05" or "BLGG" or "BLMR" or "BLCR"));
        picks = picks.Distinct().ToList();
        Console.WriteLine();
        Console.WriteLine($"Booster boxes ({ShopPrices.BoxPacks} packs, refund cap {ShopPrices.BoxRefundShare:P0} of the price; 150 boxes per row; worth = the pulls' Raw NM basis):");
        Console.WriteLine("  set (year, pack price)              | guarantee: top rarity, boxes where it fires, worth it adds a box | worth/DP: 24 singles, box at 20% off, at 25% off | box (25% off) on a finished set: refund + Sell desk");
        foreach (var set in picks)
        {
            foreach (var kind in new[] { ShopPrices.BoxGuaranteeKind.Classic, ShopPrices.BoxGuaranteeKind.Top })
            {
                ShopPrices.BoxGuarantee = kind;
                var top = ShopPrices.BoxTop(set);
                double Run(double off, bool guarantee, out double fired)
                {
                    var keep = ShopPrices.BoxDiscount; ShopPrices.BoxDiscount = off;
                    int price = ShopPrices.Box(set);
                    ShopPrices.BoxDiscount = keep;
                    var rng = new Random(seed * 31 + 5);
                    double worth = 0; fired = 0;
                    var saved = ShopPrices.BoxGuarantee;
                    for (int b = 0; b < 150; b++)
                    {
                        if (!guarantee) { var singles = Enumerable.Range(0, ShopPrices.BoxPacks).SelectMany(_ => OpenA1(set, rng)); worth += singles.Sum(x => Pricing.Basis(x.Rarity, Commonest(x.Code))); continue; }
                        var r = OpenBox(Fresh(), set, price, rng);
                        worth += r.worth; if (r.upgraded) fired++;
                    }
                    fired /= 150;
                    return worth / 150 / price;
                }
                double singlesPerDp = Run(0, false, out _), box20 = Run(0.20, true, out var fired20), box25 = Run(0.25, true, out _);
                // What the guarantee adds: the box with it, against the same boxes without it (worth per box).
                double added = (box20 - singlesPerDp / 0.8) * Pricing.Round5(ShopPrices.BoxPacks * set.Price * 0.8);
                // Arbitrage: a finished collection (every pull is a 4th copy, or replaces a less rare one), then sell everything paid for.
                var full = Fresh();
                foreach (var c in set.Pool.Values.SelectMany(x => x).Distinct()) full.Collection.Add(c, Commonest(c), 3, CopySource.Starter, 0);
                ShopPrices.BoxDiscount = 0.25;
                int boxPrice = ShopPrices.Box(set);
                ShopPrices.BoxDiscount = 0.20;
                full.Spend(set.Sink, boxPrice);
                var box = OpenBox(full, set, boxPrice, new Random(seed));
                int sold = full.Collection.All.Where(c => c.Paid > 0).ToList().Sum(c => SellDesk.Sell(full, c, _ => 0));
                Console.WriteLine($"  {Trim(set.Info.Name, 28)} ({set.Year}, {set.Price,4}) | {kind,-7} {top?.ToString() ?? "-",-24} {fired20,6:P0} {added,7:N0} | {singlesPerDp,5:0.00} {box20,5:0.00} {box25,5:0.00} | {(box.refund + sold) / (double)boxPrice,6:P1}");
            }
        }
        ShopPrices.BoxGuarantee = ShopPrices.BoxGuaranteeKind.Classic;
    }

    private static string Trim(string s, int n) => s.Length <= n ? s.PadRight(n) : s.Substring(0, n - 1) + "…";

    private static void RaritySets(IReadOnlyList<PackSet> sets, PrintingIndex printings, ICardDataProvider cards, int seed)
    {
        Rarity Commonest(uint c) => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
        (double worth, double above, int reachable) Per(PackSet set)
        {
            var rng = new Random(seed * 17 + 3);
            double worth = 0, above = 0;
            var seen = new HashSet<uint>();
            var baseR = ShopPrices.NoCommons(set) ? ShopPrices.BaseRarity(set) : Rarity.Rare;
            for (int i = 0; i < 400; i++)
                foreach (var x in OpenA1(set, rng))
                {
                    worth += Pricing.Basis(x.Rarity, Commonest(x.Code));
                    if (x.Rarity > baseR) above++;
                    seen.Add(x.Code);
                }
            return (worth / 400, above / 400, seen.Count);
        }
        var core = sets.Where(s => !ShopPrices.NoCommons(s)).Select(s => (s, st: Per(s))).ToList();
        int Bracket(PackSet s) => int.TryParse(s.Year, out var y) ? Array.FindIndex(ShopPrices.BoosterByYear, b => y <= b.upTo) : -1;
        double CoreWorth(PackSet s) => Median(core.Where(c => Bracket(c.s) == Bracket(s)).Select(c => c.st.worth).DefaultIfEmpty(Median(core.Select(c => c.st.worth))));
        Console.WriteLine();
        Console.WriteLine($"Rarity sets (fewer than {ShopPrices.CommonFloor} Common/SP cards; opened with A1; 400 packs each) against core boosters of the same years:");
        foreach (var g in core.GroupBy(c => Bracket(c.s)).OrderBy(g => g.Key))
            Console.WriteLine($"  core boosters up to {ShopPrices.BoosterByYear[Math.Max(0, g.Key)].upTo}: {g.Count()} sets, worth a pack median {Median(g.Select(x => x.st.worth)):N0} at {g.First().s.Price} DP");
        var rows = sets.Where(ShopPrices.NoCommons).Select(s => (s, st: Per(s), b: ShopPrices.BaseRarity(s))).ToList();
        foreach (var grp in rows.GroupBy(r => r.b >= Rarity.UltraRare ? "Ultra or better" : r.b >= Rarity.SuperRare ? "Super" : "Rare").OrderBy(g => g.Key))
        {
            var ratios = grp.Select(r => r.st.worth / CoreWorth(r.s)).OrderBy(x => x).ToList();
            Console.WriteLine($"  base {grp.Key}: {grp.Count()} sets, worth a pack {ratios.First():0.0}-{ratios.Last():0.0}x a core pack of its years (median {Median(ratios):0.0}x); priced {string.Join("/", grp.Select(r => r.s.Price).Distinct().OrderBy(x => x))}");
            foreach (var r in grp.OrderByDescending(r => r.s.Info.Date))
                Console.WriteLine($"    {Trim(r.s.Info.Name, 36)} ({r.s.Year}, {r.s.Price,4} DP): worth {r.st.worth,5:N0} = {r.st.worth / CoreWorth(r.s):0.0}x core, {r.st.above:0.0} above base a pack, {r.st.reachable}/{r.s.CardCount} cards seen");
        }
    }

    /// <summary>Selection (house) packs on sale as each band starts: today's rule (every opened circuit and the next band) against the rework's (unbeaten decks of the current and next circuit).</summary>
    private static void HouseAvailability(Circuits circuits, Func<string, bool> hasDeck)
    {
        Console.WriteLine();
        Console.WriteLine("Selection (house) packs on sale at the start of each band: today (opened circuits + the next band, unbeaten decks) / the rework (unbeaten decks, current + next circuit), with their price range:");
        string last = "";
        for (int ci = 0; ci < circuits.All.Count; ci++)
        {
            var band = circuits.All[ci].Band;
            if (band == last) continue;
            last = band;
            var p = new Profile();
            for (int c = 0; c < ci; c++) for (int m = 0; m < circuits.All[c].Matches.Count; m++) circuits.Record(p, c, m, 0);
            var beaten = RecipeBook.Unlocked(circuits, p).Select(r => r.Deck).ToHashSet();
            List<string> Decks(IEnumerable<int> cs) => cs.SelectMany(i => circuits.All[i].Matches.Select(m => m.Deck)).Distinct().Where(d => hasDeck(d) && !beaten.Contains(d)).ToList();
            var today = Decks(circuits.SelectionPackCircuits(p));
            var house = Decks(new[] { ci, ci + 1 }.Where(i => i < circuits.All.Count));
            string Prices(List<string> d) => d.Count == 0 ? "-" : $"{d.Min(x => SelectionPacks.PriceFor(x, circuits))}-{d.Max(x => SelectionPacks.PriceFor(x, circuits))} DP";
            Console.WriteLine($"  {band,-13} {today.Count,3} decks ({Prices(today)}) / {house.Count,2} decks ({Prices(house)})");
        }
    }

    private static double Median(IEnumerable<double> xs) { var l = xs.OrderBy(x => x).ToList(); return l.Count == 0 ? 0 : l[l.Count / 2]; }
}
