using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--arbitrage [--load &lt;mods&gt;] [--seed N]` (plan step 11, "Loops: never DP-positive"): tries every buy -> sell or refund
/// loop the economy allows, greedy (the best candidate of each loop: every booster set, structure deck, recipe, market
/// listing...) and an exhaustive search over short sequences of actions, all through the engine. Every loop starts at
/// the top level, so level-ups don't hide in the numbers; the duel-paid loops (a cleared cup's repeat wins, the final's
/// repeats, a Locals run) are measured again at a realistic champion level, level-up DP included (the per-duel cap,
/// C3/E7: at most 300 DP a duel). Fails if a loop returns more DP than it took.
/// </summary>
public static partial class Arbitrage
{
    private const int FarmDuels = 200;

    public static int Run(ICardDataProvider cards, string root, string? load, int seed, string only = "")
    {
        // Card versions phase 2: every collection here carries the text timeline and the printings, as the app's does, so packs and
        // listings stamp their copies and the 4th-copy rule (Collection.KeepLoneText) tells texts apart (VersionSetup).
        if (Environment.GetEnvironmentVariable("YGO_CV2_OFF") != "1") VersionSetup.Use(VersionSetup.Load(cards, root)); // YGO_CV2_OFF=1: collections as the harness built them before
        if (Environment.GetEnvironmentVariable("YGO_KEEPLONE") == "0") Collection.KeepLoneText = false; // the rule as it was, for a side-by-side run
        Console.WriteLine($"Texts loaded: {VersionSetup.Texts.Rows.Count()} rows; KeepLoneText {Collection.KeepLoneText}.");
        // `--arbitrage --only "No Limits"`: only the No Limits loops and their search (Arbitrage.NoLimits.cs), for quick iterations.
        if (only.Length > 0 && "No Limits".Contains(only, StringComparison.OrdinalIgnoreCase))
        {
            var p0 = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
            var r0 = Personality.LoadFolder(Path.Combine(root, "personalities")).ToList();
            var c0 = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), r0);
            var l0 = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), r0, c0);
            int bad0 = NoLimitsLoops(cards, root, p0, r0, l0, seed);
            Console.WriteLine(bad0 == 0 ? "\nNo No Limits loop is DP-positive." : $"\n{bad0} No Limits loops fail.");
            return bad0 == 0 ? 0 : 1;
        }
        var printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        var catalog = new PackCatalog(printings, cards);
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities")).ToList();
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var library = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), roster, circuits);
        var ratings = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName));
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        Deck? LoadDeck(string name) { if (!deckFiles.TryGetValue(name, out var f)) return null; var d = Deck.LoadYdk(f); d.Name = name; return d; }
        var prizes = new PrizePacks(cards, printings, LoadDeck);
        var structures = Directory.GetFiles(Path.Combine(root, "decks", "structure"), "*.ydk").OrderBy(f => f)
            .Select(f => { var d = Deck.LoadYdk(f); d.Name = Path.GetFileNameWithoutExtension(f); return d; }).ToList();
        var recipes = Directory.GetFiles(Path.Combine(root, "decks", "history"), "*.ydk").OrderBy(f => f)
            .Select(f => { var d = Deck.LoadYdk(f); d.Name = Path.GetFileNameWithoutExtension(f); return d; }).ToList();
        var marketPool = cards.All.Where(c => c.Alias == 0 && printings.Of(c.Code, 0).Length > 1).Select(c => c.Code).OrderBy(c => c).ToList();
        Rarity Commonest(uint c) => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
        int none(uint _) => 0;

        const int Top = 50_000_000; // past level 99: no level-up DP
        Profile Fresh(int exp = Top)
        {
            var p = new Profile { Dp = 50_000_000, Exp = exp };
            p.Collection.Salt = (uint)seed * 2654435761u + 7;
            p.Collection.CommonestOf = Commonest;
            p.Collection.Texts = VersionSetup.Texts;
            p.Collection.Printings = printings;
            p.LibraryOpen = true;
            return p;
        }
        // A collection with 3 gift copies of every card in these codes (refund loops need a full collection).
        void Fill(Profile p, IEnumerable<uint> codes)
        {
            foreach (var c in codes.Distinct()) if (p.Collection.Owned(c) < Collection.MaxCopies) p.Collection.Add(c, Commonest(c), Collection.MaxCopies - p.Collection.Owned(c), CopySource.Starter, 0);
        }
        int Liquidate(Profile p)
        {
            foreach (var c in GradingDesk.AtDesk(p).ToList()) { for (int d = 0; d < GradingDesk.Turnaround; d++) p.NoteResult("arb", 0); GradingDesk.Collect(p, c); }
            int got = 0;
            foreach (var c in p.Collection.All.Where(c => c.Paid > 0 && !c.AtDesk).ToList()) got += SellDesk.Sell(p, c, none);
            return got;
        }
        void GradeAll(Profile p, IEnumerable<CardCopy> copies, bool resubmit)
        {
            foreach (var c in copies.ToList())
            {
                GradingDesk.Submit(p, c, express: true, none);
                // Crack and resubmit while it isn't a 10 (never comes back lower): the "grade for 10s" loop.
                for (int k = 0; resubmit && k < 3 && c.Slab < 10; k++) { GradingDesk.Crack(p, c); GradingDesk.Submit(p, c, express: true, none); }
            }
        }

        var verdicts = new List<(string loop, string best, double ratio, int net)>();
        void Verdict(string loop, IEnumerable<(string what, int spent, int back)> tries)
        {
            var list = tries.Where(t => t.spent > 0).ToList();
            if (list.Count == 0) { Console.WriteLine($"  {loop}: nothing to try"); return; }
            var best = list.OrderByDescending(t => t.back / (double)t.spent).First();
            verdicts.Add((loop, best.what, best.back / (double)best.spent, best.back - best.spent));
            Console.WriteLine($"  {(best.back > best.spent ? "FAIL" : "ok  ")} {loop}: best {best.what}: {best.back:N0} back for {best.spent:N0} = {best.back / (double)best.spent:P1}"
                              + $" ({list.Count} tried, mean {list.Average(t => t.back / (double)t.spent):P1})");
        }
        (string, int, int) Measure(string what, Action<Profile>? setup, Action<Profile> buy, Func<Profile, int>? after = null)
        {
            var p = Fresh();
            setup?.Invoke(p);
            int dp0 = p.Dp;
            var spent0 = p.DpSpent.Values.Sum();
            buy(p);
            after?.Invoke(p);
            Liquidate(p);
            int spent = p.DpSpent.Values.Sum() - spent0;
            return (what, spent, spent - (dp0 - p.Dp));
        }

        Console.WriteLine("Arbitrage: every buy -> sell / refund loop, greedy over candidates then exhaustive over short sequences.");
        var sets = catalog.Sets.Where(s => s.Price > 0).ToList();
        var rng = new Random(seed);

        // 1. Boosters -> 4th-copy refunds (a finished collection: every pull is a 4th copy).
        Verdict("boosters -> 4th-copy refunds (set finished)", sets.Select(s => Measure(s.Info.Code, p => Fill(p, s.Pool.Values.SelectMany(x => x)),
            p => { for (int i = 0; i < 8; i++) if (p.Spend(s.Sink, s.Price)) p.OpenPack(s, new Random(seed + i), out _); })));
        // 1b. The same, with text-aware 4th copies (card versions phase 2, spec 7 loop 3): the set finished with copies that were paid for
        // in full (as an old save's: Paid = twice the sell value), either as current text bought elsewhere (so an original arriving is
        // the lone version of its text and displaces a current copy) or as this set's own packs stamp them (originals owned already).
        void FillPaid(Profile p, PackSet s, bool ownPacks)
        {
            foreach (var code in s.Pool.Values.SelectMany(x => x).Distinct().OrderBy(c => c))
            {
                var rarity = s.Pool.Where(r => r.Value.Contains(code)).Select(r => r.Key).OrderBy(r => r).First();
                var (setCode, printed) = ownPacks ? Profile.PackPrint(s, new Pull(code, rarity), printings) : ("", "");
                while (p.Collection.Owned(code) < Collection.MaxCopies) p.Collection.AddPulled(code, rarity, CopySource.Pack, -1, setCode, printed);
            }
        }
        // (The pre-filled copies are paid for, so what selling them would have returned anyway is taken off: the loop's own return.)
        (string, int, int) MeasureOver(string what, Action<Profile> setup, Action<Profile> buy)
        {
            var p0 = Fresh();
            setup(p0);
            int d0 = p0.Dp;
            Liquidate(p0);
            int baseline = p0.Dp - d0;
            var (w, spent, back) = Measure(what, setup, buy);
            return (w, spent, back - baseline);
        }
        void EightPacks(Profile p, PackSet s) { for (int i = 0; i < 8; i++) if (p.Spend(s.Sink, s.Price)) p.OpenPack(s, new Random(seed + i), out _); }
        Verdict("boosters -> 4th-copy refunds (set finished with paid current-text copies; originals arrive)", sets.Select(s => MeasureOver(s.Info.Code, p => FillPaid(p, s, false), p => EightPacks(p, s))));
        Verdict("boosters -> 4th-copy refunds (set finished by its own packs, paid; originals owned)", sets.Select(s => MeasureOver(s.Info.Code, p => FillPaid(p, s, true), p => EightPacks(p, s))));
        // 2. Boosters -> Sell desk (an empty collection), and -> grade (crack and resubmit to 10) -> sell.
        Verdict("boosters -> Sell desk", sets.Select(s => Measure(s.Info.Code, null,
            p => { for (int i = 0; i < 8; i++) if (p.Spend(s.Sink, s.Price)) p.OpenPack(s, new Random(seed + i), out _); })));
        Verdict("boosters -> grade (to 10s) -> sell", sets.Take(60).Select(s => Measure(s.Info.Code, null,
            p => { for (int i = 0; i < 4; i++) if (p.Spend(s.Sink, s.Price)) p.OpenPack(s, new Random(seed + i), out _); },
            p => { GradeAll(p, p.Collection.All.OrderByDescending(c => p.Collection.Value(c)).Take(4), resubmit: true); return 0; })));
        // 3. Selection packs -> refunds and Sell desk.
        Verdict("selection packs -> refunds + Sell desk", recipes.Select(r => Measure(r.Name, p => Fill(p, r.Main.Take(10).Select(c => Banlist.BaseCode(c, cards))),
            p =>
            {
                var sel = SelectionPacks.Build(r.Name, "SP", r.Main.Concat(r.Extra).Select(c => Banlist.BaseCode(c, cards)), p, 2026, printings, cards,
                    SelectionPacks.PriceFor(r.Name, circuits));
                for (int i = 0; i < 6 && sel.CardCount > 0; i++) if (p.Spend(sel.Sink, sel.Price)) p.OpenPack(sel, new Random(seed + i), out _);
            })));
        // 3b. Booster boxes (SHOP-REWORK-SPEC section 4, check 13): a box of each featured set (every shelf's set: lines and the
        // post-game's), then sell everything; into a finished set (every pull a 4th copy: the refund cap); and grade the best first.
        var featured = FeaturedShelves.Load(Path.Combine(root, "campaign", FeaturedShelves.FileName));
        var boxSets = featured.Names.SelectMany(featured.For).Where(x => !x.Soon).Select(x => catalog.Find(x.Code)).OfType<PackSet>().Distinct().ToList();
        void Box(Profile p, PackSet s) => BoosterBox.Open(p, s, ShopPrices.Box(s), new Random(seed + p.DpSpent.Values.Sum()));
        var boxTries = boxSets.Select(s => Measure($"{s.Info.Code} box", null, p => Box(p, s))).ToList();
        Verdict($"booster box of each featured set ({boxSets.Count}) -> Sell desk", boxTries);
        Verdict("booster box (set finished) -> 4th-copy refunds", boxSets.Select(s => Measure($"{s.Info.Code} box", p => Fill(p, s.Pool.Values.SelectMany(x => x)), p => Box(p, s))));
        Verdict("booster box -> grade (to 10s) -> sell", boxSets.Select(s => Measure($"{s.Info.Code} box", null, p => Box(p, s),
            p => { GradeAll(p, p.Collection.All.OrderByDescending(c => p.Collection.Value(c)).Take(4), resubmit: true); return 0; })));
        // The Top guarantee (the set's rarest rarity: a Starlight in RA05 or BLGG), in case the knob moves.
        var guarantee = ShopPrices.BoxGuarantee;
        ShopPrices.BoxGuarantee = ShopPrices.BoxGuaranteeKind.Top;
        Verdict("booster box, Top guarantee -> Sell desk", boxSets.Select(s => Measure($"{s.Info.Code} box", null, p => Box(p, s))));
        ShopPrices.BoxGuarantee = guarantee;
        var boxBest = boxSets.Count == 0 ? null : boxSets[boxTries.Select((t, i) => (ratio: t.Item3 / (double)Math.Max(1, t.Item2), i)).OrderByDescending(x => x.ratio).First().i];
        // 4. Recipe packs -> refunds + sell; recipe outright -> sell (also Make it yours: the same Complete).
        Verdict("recipe packs -> refunds + Sell desk", recipes.Select(r => Measure(r.Name, null,
            p => { int price = RecipeShop.PackPriceFor(r.Name, circuits); for (int i = 0; i < 8; i++) RecipeShop.OpenPack(r, p, Banlist.None, 2026, printings, cards, new Random(seed + i), price); })));
        Verdict("recipe outright / Make it yours -> Sell desk", recipes.Select(r => Measure(r.Name, null,
            p => RecipeShop.Complete(r, p, Banlist.None, 2026, printings, cards, RecipeShop.PackPriceFor(r.Name, circuits)))));
        Verdict("recipe outright -> grade (to 10s) -> sell", recipes.Take(20).Select(r => Measure(r.Name, null,
            p => RecipeShop.Complete(r, p, Banlist.None, 2026, printings, cards, RecipeShop.PackPriceFor(r.Name, circuits)),
            p => { GradeAll(p, p.Collection.All.OrderByDescending(c => p.Collection.Value(c)).Take(3), resubmit: true); return 0; })));
        // 5. Structure decks -> refunds + sell, bought once into an empty collection and again into a full one.
        foreach (var full in new[] { false, true })
            Verdict($"structure deck -> refunds + Sell desk ({(full ? "cards owned already" : "new collection")})", structures.SelectMany(s => new[] { 400, 1500 }.Select(price =>
                Measure($"{s.Name} at {price}", full ? p => Fill(p, s.Main.Concat(s.Extra).Select(c => Banlist.BaseCode(c, cards))) : null,
                    p => p.BuyStructureDeck(s, price, cards)))));
        // 6. Card Tokens bought with DP -> the trader's most valuable card -> sell.
        var tradeable = marketPool.Where(c => CardTrader.Cost(c, printings, cards) >= 1).OrderByDescending(c => Pricing.Basis(Commonest(c), Commonest(c))).Take(30).ToList();
        Verdict("tokens (DP) -> Card Trader -> Sell desk", tradeable.Select(c => Measure(cards.Get(c)?.Name ?? c.ToString(), null, p =>
        {
            for (int k = 0; k < CardTrader.Cost(c, printings, cards); k++) DpServices.BuyToken(p);
            CardTrader.Trade(c, p, 2026, printings, cards);
        })));
        // 7. The used market -> sell, and -> grade -> sell (every listing over 20 periods).
        foreach (var grade in new[] { false, true })
        {
            var tries = new List<(string, int, int)>();
            for (int epoch = 0; epoch < 20; epoch++)
            {
                var probe = Fresh();
                for (int d = 0; d < epoch * UsedMarket.Every; d++) probe.NoteResult("arb", 0);
                foreach (var l in UsedMarket.Listings(probe, marketPool, 2026, printings, cards))
                    tries.Add(Measure($"{cards.Get(l.Copy.Code)?.Name} {l.Copy.Rarity} {(l.Copy.IsSlab ? "slab " + l.Copy.Slab : "raw " + l.Copy.Grade)}",
                        p => { for (int d = 0; d < epoch * UsedMarket.Every; d++) p.NoteResult("arb", 0); },
                        p => UsedMarket.Buy(p, UsedMarket.Listings(p, marketPool, 2026, printings, cards)[l.Slot]),
                        grade ? p => { GradeAll(p, p.Collection.All, resubmit: true); return 0; } : null));
            }
            Verdict(grade ? "used market -> grade (to 10s) -> sell" : "used market -> Sell desk", tries);
        }
        // 8. Special orders -> sell, and -> grade -> sell.
        var orderable = marketPool.Where(c => UsedMarket.Orderable(c, printings, cards).Count > 0).Where((c, i) => i % 40 == 0).Take(150).ToList();
        foreach (var grade in new[] { false, true })
            Verdict(grade ? "special orders -> grade (to 10s) -> sell" : "special orders -> Sell desk", orderable.SelectMany(c => UsedMarket.Orderable(c, printings, cards).Select(r =>
                Measure($"{cards.Get(c)?.Name} {r}", null, p => UsedMarket.Order(p, c, r, 2026, printings, cards),
                    grade ? p => { GradeAll(p, p.Collection.All, resubmit: true); return 0; } : null))));

        // 9. Locals -> Prize Packs -> sell, at the top level (no level DP): the entry against the best a run can pay.
        Profile Champion(int exp)
        {
            var p = Fresh(exp);
            for (int cup = 0; cup < library.Cups.Count; cup++)
                for (int m = 0; m < library.Cups[cup].Matches.Count; m++) library.Record(p, cup, m, 0, loaner: false, built: true, deck: "mine");
            return p;
        }
        (int spent, int back, int levelDp, int duels) LocalsRun(Profile p, int cup, bool title)
        {
            int dp0 = p.Dp, spent0 = p.DpSpent.Values.Sum(), lv0 = p.DpEarned.GetValueOrDefault(Ledger.LevelUp), duels = 0;
            var b = Brackets.EnterLocals(p, library, cup, ratings, library.Cups[cup].Matches.Last().Deck);
            if (b == null) return (0, 0, 0, 0);
            while (!b.Done && b.YourMatch() >= 0) { Brackets.StartYours(b); Brackets.RecordYours(p, library, b, title ? 0 : 1, loaner: false); duels++; }
            Brackets.SkipToEnd(b);
            Brackets.Finish(p, library, b, prizes);
            Liquidate(p);
            int spent = p.DpSpent.Values.Sum() - spent0;
            return (spent, spent - (dp0 - p.Dp), p.DpEarned.GetValueOrDefault(Ledger.LevelUp) - lv0, duels);
        }
        {
            var p = Champion(Top);
            var runs = Enumerable.Range(0, library.Cups.Count).Where(c => library.Cups[c].Locals.Count == Brackets.LocalsPerCup)
                .Select(c => { var r = LocalsRun(p, c, title: true); return ($"{library.Cups[c].Name} title", r.spent, r.back); }).ToList();
            Verdict("Locals title -> Prize Packs -> Sell desk (no level-ups)", runs);
        }

        // 10. The exhaustive search: every sequence of up to 3 actions from an empty collection, then sell everything.
        var best = sets.OrderByDescending(s => s.Pool.Values.Sum(x => x.Count)).First();
        var structure = structures[0];
        var recipe = recipes.First(r => r.Name == "mitsurugi");
        var orderCard = orderable.First();
        var actions = new List<(string name, Action<Profile> act)>
        {
            ("booster", p => { if (p.Spend(best.Sink, best.Price)) p.OpenPack(best, new Random(p.DpSpent.Values.Sum()), out _); }),
            ($"box ({boxBest?.Info.Code})", p => { if (boxBest != null) Box(p, boxBest); }),
            ("recipe pack", p => RecipeShop.OpenPack(recipe, p, Banlist.None, 2026, printings, cards, new Random(p.DpSpent.Values.Sum()), RecipeShop.PackPriceFor(recipe.Name, circuits))),
            ("outright", p => RecipeShop.Complete(recipe, p, Banlist.None, 2026, printings, cards, RecipeShop.PackPriceFor(recipe.Name, circuits))),
            ("structure", p => p.BuyStructureDeck(structure, 1500, cards)),
            ("token+trade", p => { DpServices.BuyToken(p); CardTrader.Trade(tradeable[0], p, 2026, printings, cards); }),
            ("used", p => { var l = UsedMarket.Listings(p, marketPool, 2026, printings, cards).FirstOrDefault(x => UsedMarket.Blocked(p, x) == null); if (l != null) UsedMarket.Buy(p, l); }),
            ("order", p => UsedMarket.Order(p, orderCard, UsedMarket.Orderable(orderCard, printings, cards).Last(), 2026, printings, cards)),
            ("grade best", p => { var c = p.Collection.All.Where(x => !x.AtDesk).OrderByDescending(x => p.Collection.Value(x)).FirstOrDefault(); if (c != null) GradingDesk.Submit(p, c, true, none); }),
            ("grade std", p => { var c = p.Collection.All.Where(x => !x.AtDesk && !x.IsSlab).OrderByDescending(x => p.Collection.Value(x)).FirstOrDefault(); if (c != null) GradingDesk.Submit(p, c, false, none); }),
            ("crack+regrade", p => { var c = p.Collection.All.FirstOrDefault(x => x.IsSlab); if (c != null) { GradingDesk.Crack(p, c); GradingDesk.Submit(p, c, true, none); } }),
            ("sell best", p => { var c = p.Collection.All.Where(x => x.Paid > 0 && !x.AtDesk).OrderByDescending(x => p.Collection.SellPrice(x)).FirstOrDefault(); if (c != null) SellDesk.Sell(p, c, none); }),
            ("wait 5 duels", p => { for (int d = 0; d < 5; d++) p.NoteResult("arb", 0); }),
        };
        var seqs = new List<(string, int, int)>();
        int positives = 0;
        foreach (var len in new[] { 1, 2, 3 })
            foreach (var seq in Sequences(actions.Count, len))
            {
                var name = string.Join(" > ", seq.Select(i => actions[i].name));
                var r = Measure(name, null, p => { foreach (var i in seq) actions[i].act(p); });
                seqs.Add(r);
                if (r.Item3 > r.Item2) positives++;
            }
        Verdict($"exhaustive: every sequence of 1-3 of {actions.Count} actions, then sell all ({seqs.Count} sequences, {positives} DP-positive)", seqs);

        // 11. Duel-paid DP at a realistic champion level: farming a cleared cup, the final's repeats, a Locals run.
        int champExp = 0;
        load ??= new[] { Path.Combine(root, "build", "proof-economy", "champion"), Path.Combine(root, "build", "qa-night5", "mods") }
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "save", "profile.txt")));
        if (load != null && Profile.Load(load.EndsWith(".txt") ? load : Path.Combine(load, "save", "profile.txt")) is { } saved) champExp = saved.Exp;
        var levels = new List<(string, int)> { ("level 30", ExpFor(30)), ("level 40", ExpFor(40)) };
        if (champExp > 0) levels.Insert(0, ($"the champion save (level {Leveling.LevelFor(champExp)})", champExp));
        Console.WriteLine($"\nDuel-paid DP at a realistic level (a cleared cup's repeat wins, the final's repeats, Locals; level-ups included; cap {300} DP a duel):");
        int overCap = 0, positiveLocals = 0;
        foreach (var (label, exp) in levels)
        {
            var p = Champion(exp);
            int lv0 = p.DpEarned.GetValueOrDefault(Ledger.LevelUp), cup0 = p.DpEarned.GetValueOrDefault(Ledger.Cup), level0 = p.Level;
            for (int d = 0; d < FarmDuels; d++) library.Record(p, d % library.Cups.Count, 0, 0, loaner: false, built: true, deck: "mine");
            int lv = p.DpEarned.GetValueOrDefault(Ledger.LevelUp) - lv0, cup = p.DpEarned.GetValueOrDefault(Ledger.Cup) - cup0;
            double perDuel = (lv + cup) / (double)FarmDuels;
            if (perDuel > 300) overCap++;
            Console.WriteLine($"  {(perDuel > 300 ? "OVER" : "ok  ")} {label}, then the 12 cups' first clears: {FarmDuels} repeat cup wins pay {cup / (double)FarmDuels:0} + {lv / (double)FarmDuels:0} level DP = {perDuel:0} DP a duel "
                              + $"(level {level0} -> {p.Level}, {Leveling.DuelExp(library.ExpCircuit, 0, false)} EXP a win, ~{(100 + 25 * level0) / (double)Leveling.ExpToNext(level0):0.00} DP an EXP at level {level0})");
            int final0 = p.DpEarned.GetValueOrDefault(Ledger.Final);
            lv0 = p.DpEarned.GetValueOrDefault(Ledger.LevelUp);
            for (int r = 0; r < library.Rounds.Count; r++) library.RecordFinal(p, r, 0, "mine");
            for (int d = 0; d < 60; d++) library.RecordFinal(p, d % library.Rounds.Count, 0, "mine");
            int fin = p.DpEarned.GetValueOrDefault(Ledger.Final) - final0, flv = p.DpEarned.GetValueOrDefault(Ledger.LevelUp) - lv0;
            Console.WriteLine($"       the Every Era final, its 6 rounds then 60 repeats: {(fin + flv) / 66.0:0} DP a duel ({fin / 66.0:0} + {flv / 66.0:0} level; first wins and the 10,000 prize included)");
            // Locals runs, averaged over 36 (3 a cup): level-ups come in lumps, so one run says little.
            var q = Champion(exp);
            var titles = Enumerable.Range(0, 36).Select(k => LocalsRun(q, k % library.Cups.Count, title: true)).Where(r => r.spent > 0).ToList();
            var outs = Enumerable.Range(0, 36).Select(k => LocalsRun(q, k % library.Cups.Count, title: false)).Where(r => r.spent > 0).ToList();
            double tNet = titles.Average(r => r.back - r.spent), tLv = titles.Average(r => r.levelDp), oNet = outs.Average(r => r.back - r.spent), oLv = outs.Average(r => r.levelDp);
            if (tNet > 0) positiveLocals++;
            Console.WriteLine($"  {(tNet > 0 ? "PLUS" : "ok  ")}      a Locals title (mean of {titles.Count}): {tNet:+#,0;-#,0} DP a run against the {Brackets.LocalsEntry:N0} entry ({Brackets.LocalsPay[0]} placement + {tLv:0} level DP over 3 duels; prize copies sell for 0); "
                              + $"out in the quarter-finals: {oNet:+#,0;-#,0} DP ({oLv:0} level)");
            Console.WriteLine($"            break-even title odds: {(Brackets.LocalsEntry - (Brackets.LocalsPay[3] + oLv)) / (tNet + Brackets.LocalsEntry - (Brackets.LocalsPay[3] + oLv)):P0} (title vs a first-round exit; the target title odds are 30-45%)");
        }

        // 12. The Back Alley (ALLEY-SPEC section 16, A3): the probe's loops on the real code.
        int alleyBad = AlleyLoops(cards, root, printings, roster, library, seed);

        // 13. No Limits (NO-LIMITS-ECONOMY 6.1, loops 1-10, and the 12-action search): Arbitrage.NoLimits.cs.
        alleyBad += NoLimitsLoops(cards, root, printings, roster, library, seed);
        // 14. The Champion's Ladder (CHAMPIONS-LADDER-SPEC section 9): Arbitrage.Ladder.cs.
        alleyBad += LadderLoops(root, roster, circuits, library, Ladder.Load(Path.Combine(root, "campaign", Ladder.FileName), roster), load);

        Console.WriteLine("\nBest ratio per loop (DP back / DP in):");
        foreach (var v in verdicts) Console.WriteLine($"  {v.ratio,7:P1}  {v.loop}  ({v.best}, net {v.net:+#,0;-#,0})");
        int bad = verdicts.Count(v => v.net > 0);
        Console.WriteLine(bad == 0 ? "\nNo buy -> sell loop is DP-positive." : $"\n{bad} loops are DP-positive.");
        if (overCap > 0 || positiveLocals > 0)
            Console.WriteLine($"Level-up DP: {overCap} of {levels.Count} levels farm a cleared cup over 300 DP a duel; {positiveLocals} of {levels.Count} make a Locals title DP-positive "
                              + "(proposed fix, not applied: post-game duels earn EXP but their level-ups pay no DP, or level DP only from first wins).");
        // The duel-paid targets fail the run too (C3/E7 and "never DP-positive"), apart from the buy -> sell verdict above.
        if (alleyBad > 0) Console.WriteLine($"{alleyBad} alley loops are DP-positive.");
        return bad == 0 && overCap == 0 && positiveLocals == 0 && alleyBad == 0 ? 0 : 1;
    }

    /// <summary>
    /// The Back Alley's loops (docs/dev/ALLEY-ECONOMY-2026-11-01.md: "the probe's loops moved onto the real code"): AlleyProbe's
    /// action list and its exhaustive search (every sequence of 1-4 actions, then sell everything), each action bound to
    /// the engine's own calls: Ante.Hold and BackAlley.Record (or Ante.SettleAbandoned) at Dumpster Row and The Basement
    /// Game, Orvel's Table with your card (AlleyTest.OrvelDuel), Moth's rail (Ante.BuyBack), shelf and used tab
    /// (MothsVan.Buy), the Card Trader, the Grading Desk and the Sell desk (Moth's Sell tab is the Sell desk). Named loops
    /// beside it: every stake at a table won (Nyx's Table too), a stake won as a 4th copy, a first-turn loss repeated, a
    /// first clear across New Game+, every shelf and used listing of ten restocks bought and sold (or graded and sold),
    /// Orvel's lose-and-buy-back rounds, a won slab staked, lost, bought back and graded; and (Phase C) the Alley Brawl and
    /// the Midnight Open entered and finished at every placement, their Contraband Packs sold or graded and sold, the
    /// Open's token traded, with an exhaustive search over short sequences of bracket actions (the Invitational's three
    /// actions among them, on a copy of the data with its builds ready: "invitational title", "invitational out first",
    /// "invitational final lost"). Every run starts at the top
    /// level (no level-ups) from the same save. Returns the DP-positive loops.
    /// </summary>
    private static int AlleyLoops(ICardDataProvider cards, string root, PrintingIndex printings, List<Personality> roster, Library library, int seed)
    {
        Console.WriteLine("\nThe Back Alley: every loop through ante (Nyx's Table too), Orvel's Table and Moth's rail, the van, the brackets, the Sell desk, the Grading Desk, the Card Trader and New Game+, on the real code:");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var s = AlleyTest.Load(cards, root, printings, roster, library);
        var data = s.Data;
        int dumpster = data.IndexOf("dumpster-row"), basement = data.IndexOf("basement"), blackMarket = data.IndexOf("black-market"), orvel = data.IndexOf("orvels-table");
        int nyx = data.IndexOf("nyx");
        var tables = new[] { dumpster, basement, blackMarket };
        // The tables whose stakes are DP and a CPU's card (Nyx's Table among them: four seats at Den stakes, open with The Black Market's clear).
        var stakeTables = new[] { dumpster, basement, blackMarket, nyx };
        var packs = s.Packs;
        int none(uint _) => 0;
        string Name(uint c) => cards.Get(c)?.Name ?? c.ToString();
        var starter = Deck.LoadYdk(Path.Combine(root, "decks", "classic.ydk"));
        starter.Name = "Starter";
        const int Top = 50_000_000;
        // The save every loop starts from: the alley open with its three tables, tonight's lineups drawn and every seat's binder made.
        string TemplateText(bool cleared)
        {
            var p = Profile.New(starter, cards, printings, VersionSetup.Texts);
            p.Dp = 10_000_000;
            p.Exp = Top;
            p.Collection.Salt = (uint)seed * 2654435761u + 13;
            p.LibraryOpen = p.AlleyOpen = true;
            if (cleared) foreach (var id in new[] { "dumpster-row", "basement", "black-market" }) p.AlleyTables[id] = new AlleyTableState { Cleared = true };
            foreach (var t in tables)
                foreach (var o in BackAlley.Lineup(p, data, t, s.Ctx)) Ante.Binder(p, o.Binder, s.Ctx);
            // Orvel's Table (open once The Black Market is cleared): his binder, and three spare slabs of yours to stake (Prize
            // copies, Paid 0, so selling everything never sells them: worth his cheapest, middle and dearest slab).
            if (cleared)
            {
                foreach (var o in BackAlley.Lineup(p, data, orvel, s.Ctx).Concat(BackAlley.Lineup(p, data, nyx, s.Ctx))) Ante.Binder(p, o.Binder, s.Ctx);
                foreach (var rank in new[] { 0.0, 0.5, 1.0 }) AlleyTest.SpareFor(p, s, rank);
            }
            return AlleyTest.SaveText(p);
        }
        var template = TemplateText(cleared: true);
        var unopened = TemplateText(cleared: false);
        Profile Fresh(string text = "") => AlleyTest.FromText(text.Length > 0 ? text : template, printings, cards);
        int Liquidate(Profile p)
        {
            if (p.AnteLive != null) Ante.SettleAbandoned(p, s.Ctx);
            foreach (var c in GradingDesk.AtDesk(p).ToList()) { for (int d = 0; d < GradingDesk.Turnaround; d++) p.NoteResult("arb", 0); GradingDesk.Collect(p, c); }
            int got = 0;
            foreach (var c in p.Collection.All.Where(c => c.Paid > 0 && !c.AtDesk).ToList()) got += SellDesk.Sell(p, c, none);
            return got;
        }
        void GradeAll(Profile p, IEnumerable<CardCopy> copies)
        {
            foreach (var c in copies.ToList())
            {
                GradingDesk.Submit(p, c, express: true, none);
                for (int k = 0; k < 3 && c.Slab < 10; k++) { GradingDesk.Crack(p, c); GradingDesk.Submit(p, c, express: true, none); }
            }
        }
        (string what, int spent, int net) Measure(string what, Action<Profile> act, string text = "")
        {
            var p = Fresh(text);
            // DP held in the save (a duel left mid-way) is still yours until it settles.
            int dp0 = p.Dp + (p.AnteLive?.Held ?? 0), spent0 = p.DpSpent.Values.Sum();
            act(p);
            Liquidate(p);
            return (what, p.DpSpent.Values.Sum() - spent0, p.Dp - dp0);
        }
        var results = new List<(string loop, string what, int spent, int net)>();
        int bad = 0;
        void Verdict(string loop, IEnumerable<(string what, int spent, int net)> tries, bool zero = false)
        {
            var list = tries.ToList();
            var best = list.OrderByDescending(t => t.net).First();
            bool fail = best.net > 0 || (zero && list.Any(t => t.net != 0));
            if (fail) bad++;
            results.Add((loop, best.what, best.spent, best.net));
            Console.WriteLine($"  {(fail ? "FAIL" : "ok  ")} {loop}: best {best.what}: net {best.net:+#,0;-#,0;0} DP on {best.spent:N0} spent ({list.Count} tried"
                              + (zero ? $", {list.Count(t => t.net == 0)} at exactly 0" : "") + ")");
        }
        void Hold(Profile p, int table)
        {
            var o = BackAlley.Lineup(p, data, table, s.Ctx).First(x => x.Seat == BackAlley.NextSeat(p, data, table));
            Ante.Hold(p, data, table, o, null, none, s.Ctx);
        }
        Stake? NextStake(Profile p, int table) =>
            BackAlley.Lineup(p, data, table, s.Ctx).FirstOrDefault(x => x.Seat == BackAlley.NextSeat(p, data, table)) is AlleyOpponent o ? Ante.Next(p, o.Binder, s.Ctx) : null;
        // A save written mid-duel at a table (the stake held), as the app leaves it when it closes.
        Profile Reloaded(int t) { var p = Fresh(); Hold(p, t); return p; }
        var contraband = s.Ctx.Pool(9999);
        var tradeable = contraband.OrderByDescending(c => CardTrader.Cost(c, printings, cards)).ThenBy(c => c).First();

        // Every stake at a table won (three nights), then everything sold: the stakes come back and won copies sell for 0.
        Verdict("win every stake at a table (3 nights, 100%), sell everything: exactly 0", stakeTables.Select(t => Measure(data.Tables[t].Name, p =>
        {
            for (int d = 0; d < 3 * BackAlley.Matches; d++) AlleyTest.Duel(p, s, t, 0);
        })), zero: true);
        Verdict("win every stake at a table (3 nights), grade the won cards (to 10s, crack and resubmit), sell", stakeTables.Select(t => Measure(data.Tables[t].Name, p =>
        {
            int before = p.Collection.NextId;
            for (int d = 0; d < 3 * BackAlley.Matches; d++) AlleyTest.Duel(p, s, t, 0);
            GradeAll(p, p.Collection.All.Where(c => c.Source == CopySource.Ante && c.Id >= before));
        })));
        // A stake won as a 4th copy: three bought copies of the card the seat puts up, then the win, against the same three
        // bought and sold without it (the won copy and the copy it displaces, net). Bought worn (the win displaces one) or Near Mint.
        var fourth = new List<(string, int, int)>();
        foreach (var t in stakeTables)
            for (int seat = 1; seat <= data.Tables[t].SeatCount; seat++)
                foreach (var grade in new[] { 1, 9 })
                {
                    int seatNo = seat;
                    void Setup(Profile p, bool win)
                    {
                        for (int k = 1; k < seatNo; k++) AlleyTest.Duel(p, s, t, 0);
                        var st = NextStake(p, t);
                        if (st == null) return;
                        while (p.Collection.Owned(st.Code) < Collection.MaxCopies)
                        {
                            var r = printings.Commonest(st.Code, cards.Get(st.Code)?.Alias ?? 0);
                            var c = p.Collection.NewCopy(st.Code, r, CopySource.Shop);
                            c.Grade = grade;
                            c.Paid = RecipeShop.CopyPrice(r);
                            p.Spend(Ledger.Order, c.Paid);
                            p.Collection.Insert(c);
                        }
                        if (win) AlleyTest.Duel(p, s, t, 0);
                    }
                    var with = Measure("", p => Setup(p, true));
                    var without = Measure("", p => Setup(p, false));
                    var probe = Fresh();
                    for (int k = 1; k < seatNo; k++) AlleyTest.Duel(probe, s, t, 0);
                    var stake = NextStake(probe, t);
                    fourth.Add(($"{data.Tables[t].Name} seat {seatNo}: {(stake != null ? Name(stake.Code) : "-")} over 3 grade-{grade} copies", with.spent, with.net - without.net));
                }
        Verdict("win a stake as a 4th copy, sell everything (net of the same 3 copies sold without it)", fourth);
        // The first-turn guard, a draw and a quit, over and over.
        Verdict("lose on the first turn, repeat (20x, Street and Den): exactly 0", new[] { dumpster, basement }.Select(t => Measure(data.Tables[t].Name, p =>
        {
            for (int d = 0; d < 20; d++) AlleyTest.Duel(p, s, t, 1, 1, 1);
        })), zero: true);
        Verdict("draw at turn 40, repeat (20x): exactly 0", new[] { dumpster, basement }.Select(t => Measure(data.Tables[t].Name, p =>
        {
            for (int d = 0; d < 20; d++) AlleyTest.Duel(p, s, t, 2, AlleyEconomy.AlleyTurns + 1);
        })), zero: true);
        Verdict("stake lost, quit or left at load (Street, Den, Nyx's)", new[] { dumpster, basement, nyx }.SelectMany(t => new[]
        {
            Measure($"{data.Tables[t].Name} loss", p => AlleyTest.Duel(p, s, t, 1)),
            Measure($"{data.Tables[t].Name} quit", p => { Hold(p, t); Ante.SettleAbandoned(p, s.Ctx); }),
            // The app closed mid-duel: the save holds the stake, and the load settles it as a loss.
            Measure($"{data.Tables[t].Name} left at load", p => Ante.SettleAbandoned(p, s.Ctx), AlleyTest.SaveText(Reloaded(t))),
        }));
        // New Game+: a first clear's token, then New Game+ and the table cleared again (it pays once), the tokens traded and sold.
        int tokensAgain = -1;
        Verdict("first clear -> New Game+ -> clear again -> tokens to the Card Trader -> sell", new[] { Measure("Dumpster Row", p =>
        {
            for (int d = 0; d < BackAlley.Matches; d++) AlleyTest.Duel(p, s, dumpster, 0);
            p.NewGamePlus();
            int tokens = p.Tokens;
            for (int d = 0; d < BackAlley.Matches; d++) AlleyTest.Duel(p, s, dumpster, 0);
            tokensAgain = p.Tokens - tokens;
            while (p.Tokens >= CardTrader.Cost(tradeable, printings, cards) && CardTrader.Trade(tradeable, p, 2026, printings, cards)) { }
        }, unopened) });
        if (tokensAgain != 0) { bad++; Console.WriteLine($"  FAIL a first clear paid again after New Game+ ({tokensAgain} tokens)"); }

        // ---- Phase B: Moth's van (the shelf, the used tab, his Sell tab) and Orvel's Table with the rail ----
        var vanRatios = new List<(string loop, string what, int spent, int net)>();
        void VanVerdict(string loop, List<(string what, int spent, int net)> tries)
        {
            Verdict(loop, tries);
            var best = tries.OrderByDescending(t => t.spent > 0 ? (t.spent + t.net) / (double)t.spent : 0).First();
            vanRatios.Add((loop, best.what, best.spent, best.net));
        }
        // Every listing of ten restocks: the shelf's at 10 alley duels apart, the used tab's at 5 duels apart.
        const int Restocks = 10;
        IEnumerable<(int epoch, int slot)> ShelfSlots() => Enumerable.Range(0, Restocks).SelectMany(e => Enumerable.Range(0, AlleyEconomy.ShelfSize).Select(i => (e, i)));
        IEnumerable<(int epoch, int slot)> UsedSlots() => Enumerable.Range(0, Restocks).SelectMany(e => Enumerable.Range(0, AlleyEconomy.VanUsedSize).Select(i => (e, i)));
        CardCopy? BuyShelf(Profile p, int epoch, int slot)
        {
            p.AlleyDuels = epoch * AlleyEconomy.ShelfEvery;
            var l = MothsVan.Shelf(p, packs, s.Ctx).FirstOrDefault(x => x.Slot == slot);
            if (l == null || MothsVan.Buy(p, l, shelf: true) < 0) return null;
            return p.Collection.All.FirstOrDefault(c => c.Seed == l.Copy.Seed && c.Code == l.Copy.Code && c.Paid == l.Price);
        }
        CardCopy? BuyUsed(Profile p, int epoch, int slot)
        {
            p.DuelsPlayed = epoch * AlleyEconomy.VanUsedEvery;
            var l = MothsVan.Used(p, packs, s.Ctx).FirstOrDefault(x => x.Slot == slot);
            if (l == null || MothsVan.Buy(p, l, shelf: false) < 0) return null;
            return p.Collection.All.FirstOrDefault(c => c.Seed == l.Copy.Seed && c.Code == l.Copy.Code && c.Paid == l.Price);
        }
        string ShelfName(int e, int i) { var p = Fresh(); p.AlleyDuels = e * AlleyEconomy.ShelfEvery; var l = MothsVan.Shelf(p, packs, s.Ctx)[i]; return $"{Name(l.Copy.Code)} {l.Copy.Rarity} slab {l.Copy.Slab} at {l.Price:N0}"; }
        string UsedName(int e, int i) { var p = Fresh(); p.DuelsPlayed = e * AlleyEconomy.VanUsedEvery; var l = MothsVan.Used(p, packs, s.Ctx)[i]; return $"{Name(l.Copy.Code)} {l.Copy.Rarity} {(l.Copy.IsSlab ? $"slab {l.Copy.Slab}" : $"raw {l.Copy.Grade}")} at {l.Price:N0}"; }
        VanVerdict($"the shelf -> Moth's Sell tab (every slab of {Restocks} restocks)", ShelfSlots().Select(x => Measure(ShelfName(x.epoch, x.slot), p =>
        {
            if (BuyShelf(p, x.epoch, x.slot) is CardCopy c) MothsVan.Sell(p, c, none);
        })).ToList());
        VanVerdict("the shelf -> crack, regrade (express, to 10s) -> sell", ShelfSlots().Select(x => Measure(ShelfName(x.epoch, x.slot), p =>
        {
            if (BuyShelf(p, x.epoch, x.slot) is CardCopy c) GradeAll(p, new[] { c });
        })).ToList());
        VanVerdict($"Moth's used tab -> Moth's Sell tab (every listing of {Restocks} restocks)", UsedSlots().Select(x => Measure(UsedName(x.epoch, x.slot), p =>
        {
            if (BuyUsed(p, x.epoch, x.slot) is CardCopy c) MothsVan.Sell(p, c, none);
        })).ToList());
        VanVerdict("Moth's used tab -> grade (to 10s) -> sell", UsedSlots().Select(x => Measure(UsedName(x.epoch, x.slot), p =>
        {
            if (BuyUsed(p, x.epoch, x.slot) is CardCopy c) GradeAll(p, new[] { c });
        })).ToList());
        // Orvel's Table and the rail: each of your three spares (his cheapest, middle and dearest slab's worth) staked and lost,
        // then bought back off the rail, 1-5 rounds, then everything sold.
        List<CardCopy> Spares(Profile p) => p.Collection.All.Where(c => c.Source == CopySource.Prize && c.IsSlab).OrderBy(c => p.Collection.Value(c)).ThenBy(c => c.Id).ToList();
        var spareNames = Spares(Fresh()).Select(c => $"{Name(c.Code)} ({Fresh().Collection.Value(c):N0})").ToList();
        var railTries = new List<(string, int, int)>();
        for (int k = 0; k < spareNames.Count; k++)
            for (int rounds = 1; rounds <= 5; rounds++)
            {
                int kk = k, n = rounds;
                railTries.Add(Measure($"{spareNames[k]}, {rounds} round{(rounds == 1 ? "" : "s")}", p =>
                {
                    var c = Spares(p)[kk];
                    for (int r = 0; r < n; r++)
                        if (AlleyTest.OrvelDuel(p, s, c, 1)?.Ante?.Railed is RailEntry e) Ante.BuyBack(p, e);
                }));
            }
        VanVerdict("Orvel's Table: lose your card, buy it back off the rail (1-5 rounds), sell everything", railTries);
        // A card won at Orvel's (Paid 0) staked, lost, bought back (its Paid becomes the tag), graded, sold.
        var wonTries = new List<(string, int, int)>();
        for (int k = 0; k < spareNames.Count; k++)
        {
            int kk = k;
            wonTries.Add(Measure($"the slab won with {spareNames[k]}", p =>
            {
                var won = AlleyTest.OrvelDuel(p, s, Spares(p)[kk], 0)?.Ante?.Won;
                if (won == null || !p.Collection.All.Contains(won)) return;
                if (AlleyTest.OrvelDuel(p, s, won, 1)?.Ante?.Railed is RailEntry e && Ante.BuyBack(p, e)) GradeAll(p, new[] { won });
            }));
        }
        VanVerdict("Orvel's Table: win his slab, stake it, lose it, buy it back, grade, sell", wonTries);
        Verdict("Orvel's Table: win every sitting (10x, each spare), sell everything: exactly 0", Enumerable.Range(0, spareNames.Count).Select(k => Measure(spareNames[k], p =>
        {
            for (int d = 0; d < 10; d++) AlleyTest.OrvelDuel(p, s, Spares(p)[k], 0);
        })).ToList(), zero: true);
        Verdict("Orvel's Table: draw, or lose on the first turn, repeat (10x each): exactly 0", Enumerable.Range(0, spareNames.Count).Select(k => Measure(spareNames[k], p =>
        {
            for (int d = 0; d < 10; d++) { AlleyTest.OrvelDuel(p, s, Spares(p)[k], 2, AlleyEconomy.AlleyTurns + 1); AlleyTest.OrvelDuel(p, s, Spares(p)[k], 1, 1, 1); }
        })).ToList(), zero: true);

        // ---- Phase C: the Alley Brawl and the Midnight Open, their Contraband Packs as prizes ----
        // One bracket entered and played through the engine: you win every duel until the round you lose in (-1: a title; the
        // Midnight Open's final lost 0-2), then Finish opens its Contraband Packs (Prize copies, Paid 0).
        // The Invitational (NL-2) is "Coming soon" in the real data (no build is ready): its actions run on a copy with all eleven forced ready,
        // No Limits cleared, and the No Limits Pack over the decks that exist (wave 1's).
        var inv = AlleyTest.ReadyInvitational(s, cards);
        var invPacks = ContrabandPacks.NoLimits(inv.Data, inv.Ctx);
        void PlayBracket(Profile p, string kind, int loseIn)
        {
            if (kind == Brackets.InvitationalKind)
            {
                AlleyTest.ClearNoLimits(p);
                if (Brackets.EnterBlocked(p, inv.Data, kind) == null) AlleyTest.PlayInvitational(p, inv, library, packs, invPacks, loseIn);
                return;
            }
            var b = Brackets.EnterAlley(p, data, kind, s.Ctx);
            if (b == null) return;
            while (b.YourMatch() >= 0)
            {
                Brackets.StartYours(b);
                Brackets.RecordYours(p, library, b, b.RoundOf(b.YourMatch()) == loseIn ? 1 : 0, loaner: false);
            }
            Brackets.SkipToEnd(b);
            Brackets.Finish(p, library, b, null, packs);
        }
        void TradeTokens(Profile p) { while (p.Tokens >= CardTrader.Cost(tradeable, printings, cards) && CardTrader.Trade(tradeable, p, 2026, printings, cards)) { } }
        var bracketRuns = new List<(string name, string kind, int loseIn)>();
        foreach (var (kind, rounds) in new[] { (Brackets.BrawlKind, 3), (Brackets.MidnightKind, 4) })
            for (int loseIn = -1; loseIn < rounds; loseIn++)
                bracketRuns.Add(($"{(kind == Brackets.BrawlKind ? "Alley Brawl" : "Midnight Open")} {Brackets.PlacementText(loseIn < 0 ? 0 : rounds - loseIn).ToLowerInvariant()}", kind, loseIn));
        var entered = bracketRuns.Select(x => Measure(x.name, p => { PlayBracket(p, x.kind, x.loseIn); TradeTokens(p); })).ToList();
        Verdict("enter an alley bracket, finish at each placement, open the Contraband Packs, trade the token, sell everything", entered);
        var bestBack = entered.Where(x => x.spent > 0).OrderByDescending(x => (x.spent + x.net) / (double)x.spent).First();
        var both = Measure("the Alley Brawl and the Midnight Open, both won", p =>
        {
            PlayBracket(p, Brackets.BrawlKind, -1);
            PlayBracket(p, Brackets.MidnightKind, -1);
            TradeTokens(p);
        });
        bool under = entered.All(x => x.spent > 0 && x.net < 0) && both.net < 0;
        if (!under) bad++;
        Console.WriteLine($"  {(under ? "ok  " : "FAIL")} enter every alley bracket, sell the prizes: at best {(bestBack.spent + bestBack.net) / (double)bestBack.spent:P1} of the entry back "
                          + $"({bestBack.what}: net {bestBack.net:+#,0;-#,0;0} on {bestBack.spent:N0}); both brackets won in turn {(both.spent + both.net) / (double)both.spent:P1} "
                          + $"(net {both.net:+#,0;-#,0;0} on {both.spent:N0}); every placement under 100%");
        Verdict("enter an alley bracket, finish at each placement, grade the Contraband pulls (to 10s, crack and resubmit), sell", bracketRuns.Select(x => Measure(x.name, p =>
        {
            int before = p.Collection.NextId;
            PlayBracket(p, x.kind, x.loseIn);
            GradeAll(p, p.Collection.All.Where(c => c.Source == CopySource.Prize && c.Id >= before));
        })));
        var bracketActions = new List<(string name, Action<Profile> act)>
        {
            ("brawl title", p => PlayBracket(p, Brackets.BrawlKind, -1)),
            ("brawl out first", p => PlayBracket(p, Brackets.BrawlKind, 0)),
            ("open title", p => PlayBracket(p, Brackets.MidnightKind, -1)),
            ("open final lost", p => PlayBracket(p, Brackets.MidnightKind, 3)),
            ("invitational title", p => PlayBracket(p, Brackets.InvitationalKind, -1)),
            ("invitational out first", p => PlayBracket(p, Brackets.InvitationalKind, 0)),
            ("invitational final lost", p => PlayBracket(p, Brackets.InvitationalKind, 3)),
            ("token trade", TradeTokens),
            ("grade best", p => { var c = p.Collection.All.Where(x => !x.AtDesk).OrderByDescending(x => p.Collection.Value(x)).FirstOrDefault(); if (c != null) GradingDesk.Submit(p, c, true, none); }),
            ("crack+regrade", p => { var c = p.Collection.All.FirstOrDefault(x => x.IsSlab && x.Source == CopySource.Prize); if (c != null) { GradingDesk.Crack(p, c); GradingDesk.Submit(p, c, true, none); } }),
            ("sell best", p => { var c = p.Collection.All.Where(x => x.Paid > 0 && !x.AtDesk).OrderByDescending(x => p.Collection.SellPrice(x)).FirstOrDefault(); if (c != null) SellDesk.Sell(p, c, none); }),
        };
        var bracketSeqs = new List<(string, int, int)>();
        foreach (var len in new[] { 1, 2, 3 })
            foreach (var seq in Sequences(bracketActions.Count, len))
                bracketSeqs.Add(Measure(string.Join(" > ", seq.Select(i => bracketActions[i].name)), p => { foreach (var i in seq) bracketActions[i].act(p); }));
        Verdict($"exhaustive: every sequence of 1-3 of {bracketActions.Count} bracket actions, then sell all ({bracketSeqs.Count} sequences, {bracketSeqs.Count(x => x.Item3 > 0)} DP-positive)", bracketSeqs);
        // NO-LIMITS-ECONOMY 6.1: 11 actions over 1-3 steps are 1,463 sequences (11 + 121 + 1,331).
        bool bracketCountOk = bracketSeqs.Count == 1463 && bracketActions.Count == 11;
        if (!bracketCountOk) bad++;
        Console.WriteLine($"  {(bracketCountOk ? "ok  " : "FAIL")} the bracket search is {bracketSeqs.Count:N0} sequences (11 + 121 + 1,331 = 1,463)");

        // The exhaustive search: every sequence of 1-4 alley actions, then sell everything.
        var actions = new List<(string name, Action<Profile> act)>
        {
            ("orvel win", p => AlleyTest.OrvelDuel(p, s, null, 0)),
            ("orvel loss", p => AlleyTest.OrvelDuel(p, s, null, 1)),
            ("orvel first-turn loss", p => AlleyTest.OrvelDuel(p, s, null, 1, 1, 1)),
            ("buy back", p => { if (p.Rail.FirstOrDefault() is RailEntry e) Ante.BuyBack(p, e); }),
            ("shelf buy", p => { if (MothsVan.Shelf(p, packs, s.Ctx).FirstOrDefault(l => MothsVan.Blocked(p, l) == null) is Listing l) MothsVan.Buy(p, l, shelf: true); }),
            ("used buy", p => { if (MothsVan.Used(p, packs, s.Ctx).Where(l => MothsVan.Blocked(p, l) == null).OrderByDescending(l => l.Price).FirstOrDefault() is Listing l) MothsVan.Buy(p, l, shelf: false); }),
            ("street win", p => AlleyTest.Duel(p, s, dumpster, 0)),
            ("den win", p => AlleyTest.Duel(p, s, basement, 0)),
            ("street loss", p => AlleyTest.Duel(p, s, dumpster, 1)),
            ("den loss", p => AlleyTest.Duel(p, s, basement, 1)),
            ("den draw", p => AlleyTest.Duel(p, s, basement, 2, AlleyEconomy.AlleyTurns + 1)),
            ("den first-turn loss", p => AlleyTest.Duel(p, s, basement, 1, 1, 1)),
            ("den quit", p => { Hold(p, basement); Ante.SettleAbandoned(p, s.Ctx); }),
            ("token trade", p => { p.Tokens += 3; CardTrader.Trade(tradeable, p, 2026, printings, cards); }),
            ("grade best", p => { var c = p.Collection.All.Where(x => !x.AtDesk).OrderByDescending(x => p.Collection.Value(x)).FirstOrDefault(); if (c != null) GradingDesk.Submit(p, c, true, none); }),
            ("crack+regrade", p => { var c = p.Collection.All.FirstOrDefault(x => x.IsSlab); if (c != null) { GradingDesk.Crack(p, c); GradingDesk.Submit(p, c, true, none); } }),
            ("sell best", p => { var c = p.Collection.All.Where(x => x.Paid > 0 && !x.AtDesk).OrderByDescending(x => p.Collection.SellPrice(x)).FirstOrDefault(); if (c != null) SellDesk.Sell(p, c, none); }),
        };
        var seqs = new List<(string, int, int)>();
        foreach (var len in new[] { 1, 2, 3, 4 })
            foreach (var seq in Sequences(actions.Count, len))
                seqs.Add(Measure(string.Join(" > ", seq.Select(i => actions[i].name)), p => { foreach (var i in seq) actions[i].act(p); }));
        Verdict($"exhaustive: every sequence of 1-4 of {actions.Count} alley actions, then sell all ({seqs.Count} sequences, {seqs.Count(x => x.Item3 > 0)} DP-positive)", seqs);
        var losing = new HashSet<string> { "street loss", "den loss", "den quit", "orvel loss" };
        var withLoss = seqs.Where(x => x.Item1.Split(" > ").Any(losing.Contains)).ToList();
        bool lossesCost = withLoss.All(x => x.Item3 < 0);
        if (!lossesCost) bad++;
        Console.WriteLine($"  {(lossesCost ? "ok  " : "FAIL")} the {withLoss.Count} sequences with a lost stake or a quit all lose DP; {seqs.Count(x => x.Item3 == 0)} break even (wins, draws, the guard, trades)"
                          + (lossesCost ? "" : $"; not: {string.Join("; ", withLoss.Where(x => x.Item3 >= 0).Take(5).Select(x => $"{x.Item1} ({x.Item3:+#,0;-#,0;0})"))}"));
        var nearest = seqs.Where(x => x.Item3 < 0).OrderByDescending(x => x.Item3).First();
        Console.WriteLine($"  the closest losing sequence: {nearest.Item1}: net {nearest.Item3:+#,0;-#,0} on {nearest.Item2:N0} spent");
        Console.WriteLine("  Best ratio per van and rail loop (DP back / DP in; ALLEY-ECONOMY: the shelf at most a sixth back, -4,600 on 5,500; used 20%; "
                          + "1-5 rounds of lose, buy back, sell -11,100 on 12,000):");
        foreach (var (loop, what, spent, net) in vanRatios)
            Console.WriteLine($"    {(spent > 0 ? (spent + net) / (double)spent : 0),6:P1}  {loop}  ({what}, net {net:+#,0;-#,0;0} on {spent:N0})");
        Console.WriteLine($"  (alley loops: {timer.Elapsed.TotalSeconds:0.0} s)");
        return bad;
    }

    private static int ExpFor(int level) { int exp = 0; for (int l = 1; l < level; l++) exp += Leveling.ExpToNext(l); return exp; }

    private static IEnumerable<int[]> Sequences(int n, int len)
    {
        if (len == 0) { yield return Array.Empty<int>(); yield break; }
        foreach (var head in Sequences(n, len - 1))
            for (int i = 0; i < n; i++) yield return head.Append(i).ToArray();
    }
}
