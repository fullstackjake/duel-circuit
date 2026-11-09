using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--campaign-test`'s DP ledger checks (plan step 11): a scripted mix of every way DP moves, then
/// sum(DpEarned) - sum(DpSpent) == Dp - StartingDp, each source and sink named as <see cref="Ledger"/> says, and the
/// "earned"/"spent" lines round-trip a save (with a bought Ban Pass refunded at load).
/// </summary>
public static class LedgerCheck
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
        Console.WriteLine("\nThe DP ledger:");
        var p = Profile.New(starter, cards, printings);
        p.Collection.Salt = 20261101;
        p.Collection.CommonestOf = c => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
        int none(uint _) => 0;
        var rng = new Random(11);
        bool Balanced() => Ledger.Net(p) == p.Dp - Profile.StartingDp;

        // Duels of every kind: a loss, a circuit cleared (prize, level-ups), a Tag Cup match, a guest's first win.
        circuits.Record(p, 0, 0, 1);
        for (int m = 0; m < circuits.All[0].Matches.Count; m++) circuits.Record(p, 0, m, 0);
        circuits.Record(p, 0, 0, 2);
        if (circuits.TagCups.Count > 0) { circuits.RecordTag(p, 0, 0, 1); circuits.RecordTag(p, 0, 0, 0, loaner: true); }
        if (guests.All.Count > 0) { GuestList.Record(p, circuits, guests.All[0], false, 0); GuestList.Record(p, circuits, guests.All[0], false, 1); }
        bool duelsOk = Balanced();
        // Money for the shopping (a check's grant, in the ledger as "debug").
        p.Earn(Ledger.Debug, 200000);

        // Shopping: boosters, a selection pack, a booster box, a token, recipe packs, a recipe outright, Make it yours, a structure deck.
        var set = catalog.Sets.First(s => s.Price > 0);
        for (int i = 0; i < 4; i++) if (p.Spend(set.Sink, set.Price)) p.OpenPack(set, rng, out _);
        var recipe = Deck.LoadYdk(Path.Combine(root, "decks", "history", "mitsurugi.ydk"));
        recipe.Name = "mitsurugi";
        var selection = SelectionPacks.Build("mitsurugi", "SP-mitsurugi", recipe.Main.Select(c => Banlist.BaseCode(c, cards)), p, 2026, printings, cards);
        if (p.Spend(selection.Sink, selection.Price)) p.OpenPack(selection, rng, out _);
        // A booster box (SHOP-REWORK-SPEC section 4): its own sink, its refunds with the packs'.
        BoosterBox.Open(p, set, ShopPrices.Box(set), rng);
        DpServices.BuyToken(p);
        for (int i = 0; i < 2; i++) RecipeShop.OpenPack(recipe, p, Banlist.None, 2026, printings, cards, rng, 450);
        RecipeShop.Complete(recipe, p, Banlist.None, 2026, printings, cards, 450);
        var other = Deck.LoadYdk(Path.Combine(root, "decks", "history", "goat-control.ydk"));
        RecipeShop.Complete(other, p, Banlist.None, 9999, printings, cards, 300, Ledger.MakeItYours);
        var structureFile = Directory.GetFiles(Path.Combine(root, "decks", "structure"), "*.ydk").OrderBy(f => f).First();
        p.BuyStructureDeck(Deck.LoadYdk(structureFile), 1500, cards);
        p.BuyStructureDeck(Deck.LoadYdk(structureFile), 1500, cards); // the second one's copies are 4th copies: a refund
        bool shopOk = Balanced();

        // The grade economy: grade, sell, the used market, a special order.
        var paidCopy = p.Collection.All.Where(c => c.Paid > 0 && !c.AtDesk).OrderByDescending(c => p.Collection.Value(c)).First();
        GradingDesk.Submit(p, paidCopy, express: true, none);
        SellDesk.Sell(p, paidCopy, none);
        foreach (var c in p.Collection.All.Where(c => c.Paid > 0 && !c.AtDesk).Take(5).ToList()) SellDesk.Sell(p, c, none);
        var poolCodes = cards.All.Where(c => c.Alias == 0 && printings.Of(c.Code, 0).Length > 1).Take(300).Select(c => c.Code).ToList();
        foreach (var l in UsedMarket.Listings(p, poolCodes, 2026, printings, cards).Where(l => UsedMarket.Blocked(p, l) == null).Take(3).ToList()) UsedMarket.Buy(p, l);
        var orderCard = poolCodes.First(c => UsedMarket.Orderable(c, printings, cards).Count > 0);
        UsedMarket.Order(p, orderCard, UsedMarket.Orderable(orderCard, printings, cards).Last(), 2026, printings, cards);
        bool gradeOk = Balanced();

        // The Library: a cup cleared, a final round, a mat, a plate, a Locals bracket played to its end.
        p.LibraryOpen = true;
        for (int m = 0; m < library.Cups[0].Matches.Count; m++) library.Record(p, 0, m, 0, loaner: false, built: true, deck: "my-goat");
        library.Record(p, 0, 0, 1, loaner: false, built: true);
        library.Record(p, 0, 0, 0, loaner: false, built: true, deck: "my-goat");
        if (library.Rounds.Count > 0) { library.RecordFinal(p, 0, 1, "my-goat"); library.RecordFinal(p, 0, 0, "my-goat"); }
        Cosmetics.BuyMat(p, library, 0);
        Cosmetics.Engrave(p, library, 0, "my-goat", "My Goat");
        var ratings = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName));
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        var prizes = new PrizePacks(cards, printings, name => deckFiles.TryGetValue(name, out var f) ? Deck.LoadYdk(f) : null);
        var b = Brackets.EnterLocals(p, library, 0, ratings, "goat-control");
        if (b != null)
        {
            // Win the first match, lose the next: a placement paid at the loss.
            for (int r = 0; r < 2 && !b.Done && b.YourMatch() >= 0; r++) { Brackets.StartYours(b); Brackets.RecordYours(p, library, b, r == 0 ? 0 : 1, loaner: false); }
            Brackets.SkipToEnd(b);
            Brackets.Finish(p, library, b, prizes);
        }
        bool libraryOk = Balanced();

        // The Champion's Ladder (docs/dev/CHAMPIONS-LADDER-SPEC-2026-11-06.md section 5), on a save of its own past the final (so the
        // mix above stays where it is): a loss, a draw, the climb to the top, a rematch. Its lines are "ladder", "ladder.loss" and
        // "ladder.prize", and nothing else moves.
        var ladder = Ladder.Load(Path.Combine(root, "campaign", Ladder.FileName), roster, deckFiles.ContainsKey);
        var ladderSources = new[] { Ledger.Ladder, Ledger.Loss(Ledger.Ladder), Ledger.Prize(Ledger.Ladder) };
        var lp = new Profile { Exp = p.Exp };
        foreach (var r in Enumerable.Range(0, library.Rounds.Count)) lp.LibraryWins[library.FinalKey(r)] = 1;
        if (ladder.Opener != null) lp.Wins[ladder.Opener.Id] = 1;
        if (ladder.Rungs.Count > 0) { ladder.Record(lp, lp.Ladder, library, ladder.Rungs[0], 1); ladder.Record(lp, lp.Ladder, library, ladder.Rungs[0], 2); }
        for (int guard = 0; guard < 100 && ladder.Next(lp, lp.Ladder, library) is LadderSeat next; guard++) ladder.Record(lp, lp.Ladder, library, next, 0);
        if (ladder.Rungs.Count > 0) ladder.Record(lp, lp.Ladder, library, ladder.Rungs[0], 0);
        var ladderOther = lp.DpEarned.Keys.Where(k => !ladderSources.Contains(k) && k != Ledger.LevelUp).Concat(lp.DpSpent.Keys).ToList();
        Check($"the Champion's Ladder pays under {string.Join(", ", ladderSources.Select(k => $"{k} {lp.DpEarned.GetValueOrDefault(k):N0}"))} (level-ups "
              + $"{lp.DpEarned.GetValueOrDefault(Ledger.LevelUp):N0}), spends nothing, and balances" + (ladderOther.Count > 0 ? $"; other lines: {string.Join(", ", ladderOther)}" : ""),
            ladder.HoldsTop(lp.Ladder) && ladderSources.All(lp.DpEarned.ContainsKey) && ladderOther.Count == 0 && Ledger.Net(lp) == lp.Dp - Profile.StartingDp);

        // The Back Alley (ALLEY-SPEC check 12): the cup cleared above opens it. Stakes held at a duel's start leave Dp with no
        // line, so mid-duel the ledger reconciles against Dp + held; settled, against Dp alone. One of each ending.
        var alley = AlleyTest.Load(cards, root, printings, roster, library);
        int dumpster = alley.Data.IndexOf("dumpster-row");
        bool opened = BackAlley.Open(p, library);
        bool heldOk = true;
        foreach (var (winner, turns, first) in new[] { (0, 12, 0), (1, 12, 0), (2, 41, 1), (1, 1, 1), (0, 9, 1) })
        {
            AlleyTest.Duel(p, alley, dumpster, winner, turns, first);
            heldOk &= Balanced();
        }
        var quitSeat = BackAlley.Lineup(p, alley.Data, dumpster, alley.Ctx).First(o => o.Seat == BackAlley.NextSeat(p, alley.Data, dumpster));
        var held = Ante.Hold(p, alley.Data, dumpster, quitSeat, null, none, alley.Ctx);
        heldOk &= held != null && held.Held > 0 && !Balanced() && Ledger.Net(p) == p.Dp + held.Held - Profile.StartingDp;
        Ante.SettleAbandoned(p, alley.Ctx);
        bool alleyOk = opened && heldOk && Balanced();

        // Phase B: Orvel's Table (a card staked and lost, then claimed off Moth's rail), the van's shelf and used tab, and a
        // 4th copy bought at the van (the copy it displaces pays refund.alley). The ledger balances after each.
        foreach (var id in new[] { "dumpster-row", "basement", "black-market" })
        {
            if (!p.AlleyTables.TryGetValue(id, out var st)) p.AlleyTables[id] = st = new AlleyTableState();
            st.Cleared = true;
        }
        var spare = AlleyTest.SpareFor(p, alley, 0.5);
        var lostAtOrvel = AlleyTest.OrvelDuel(p, alley, spare, 1);
        bool vanOk = lostAtOrvel?.Ante?.Railed is RailEntry railed && Balanced() && Ante.BuyBack(p, railed) && Balanced();
        var packs = alley.Packs;
        var shelfBuy = MothsVan.Shelf(p, packs, alley.Ctx).First(l => MothsVan.Blocked(p, l) == null);
        vanOk &= MothsVan.Buy(p, shelfBuy, shelf: true) == 0 && Balanced();
        var fourth = MothsVan.Used(p, packs, alley.Ctx).Where(l => !l.Sold && !l.Copy.IsSlab && l.Copy.Grade >= 5 && p.Collection.Owned(l.Copy.Code) == 0).OrderByDescending(l => l.Price).First();
        for (int i = 0; i < Collection.MaxCopies; i++)
        {
            var worn = p.Collection.NewCopy(fourth.Copy.Code, fourth.Copy.Rarity, CopySource.Shop);
            worn.Grade = 1;
            worn.Paid = 400;
            p.Spend(Ledger.Order, 400);
            p.Collection.Insert(worn);
        }
        vanOk &= MothsVan.Buy(p, fourth, shelf: false) > 0 && Balanced();
        alleyOk &= vanOk;

        // Phase C: an Alley Brawl lost in the semi-finals (its placement paid at the loss) and a Midnight Open won 2-1 in the
        // final (paid at the end, its Contraband Packs opened, its first-title token): entries to alley.brackets, pay to alley.
        int bracketsBefore = p.DpSpent.GetValueOrDefault(Ledger.AlleyBrackets), alleyPayBefore = p.DpEarned.GetValueOrDefault(Ledger.Alley);
        bool bracketOk = p.Bracket == null;
        foreach (var (kind, results) in new[] { (Brackets.BrawlKind, new[] { 0, 1 }), (Brackets.MidnightKind, new[] { 0, 0, 0, 0, 1, 0 }) })
        {
            var ab = Brackets.EnterAlley(p, alley.Data, kind, alley.Ctx);
            bracketOk &= ab != null && Balanced();
            if (ab == null) continue;
            foreach (var r in results)
            {
                if (ab.YourMatch() < 0) break;
                Brackets.StartYours(ab);
                Brackets.RecordYours(p, library, ab, r, loaner: false);
                bracketOk &= Balanced();
            }
            Brackets.SkipToEnd(ab);
            bracketOk &= Brackets.Finish(p, library, ab, null, packs) != null && Balanced();
        }
        int bracketEntries = p.DpSpent.GetValueOrDefault(Ledger.AlleyBrackets) - bracketsBefore, bracketPay = p.DpEarned.GetValueOrDefault(Ledger.Alley) - alleyPayBefore;
        bracketOk &= bracketEntries == AlleyEconomy.BrawlEntry + AlleyEconomy.OpenEntry && bracketPay == AlleyEconomy.BrawlPay[2] + AlleyEconomy.OpenPay[0];
        alleyOk &= bracketOk;

        // No Limits (NO-LIMITS-ECONOMY 6.2): the wall open (Nyx's Table cleared, alley-closing seen, nolimits raised); a stake lost at the table and a
        // quit write nolimits.stake, never alley.stake; a win, a draw and the first-turn guard write no line. The ledger balances at every step.
        // The No Limits Invitational (NL-2) follows, below: entries to nolimits.entry, placement pay to nolimits.
        var nlSetup = AlleyTest.ReadyNoLimits(alley, cards);
        int noLimits = nlSetup.Data.IndexOf("no-limits");
        p.AlleyTables["nyx"] = new AlleyTableState { Cleared = true };
        p.Seen.Add(BackAlley.ClosingScene);
        p.Seen.Add(BackAlley.WallEvent);
        p.Earn(Ledger.Debug, 20000);
        int nlStake0 = p.DpSpent.GetValueOrDefault(Ledger.NoLimitsStake), alleyStake0 = p.DpSpent.GetValueOrDefault(Ledger.AlleyStake), nlDp0 = p.Dp;
        bool nlOk = true;
        foreach (var (winner, turns, first) in new[] { (0, 12, 0), (1, 12, 0), (2, 41, 0), (1, 1, 1), (0, 9, 1) })
        {
            nlOk &= AlleyTest.Duel(p, nlSetup, noLimits, winner, turns, first) != null && Balanced();
        }
        int nlLost = p.DpSpent.GetValueOrDefault(Ledger.NoLimitsStake) - nlStake0;
        var nlSeat = BackAlley.Lineup(p, nlSetup.Data, noLimits, nlSetup.Ctx).First(o => o.Seat == BackAlley.NextSeat(p, nlSetup.Data, noLimits));
        var nlHeld = Ante.Hold(p, nlSetup.Data, noLimits, nlSeat, null, none, nlSetup.Ctx);
        nlOk &= nlHeld != null && nlHeld.Held == AlleyEconomy.NoLimitsStake && Ledger.Net(p) == p.Dp + nlHeld.Held - Profile.StartingDp;
        Ante.SettleAbandoned(p, nlSetup.Ctx);
        bool noLimitsOk = nlOk && Balanced() && nlLost == AlleyEconomy.NoLimitsStake && p.DpSpent.GetValueOrDefault(Ledger.NoLimitsStake) == nlStake0 + 2 * AlleyEconomy.NoLimitsStake
                          && p.DpSpent.GetValueOrDefault(Ledger.AlleyStake) == alleyStake0 && p.Dp == nlDp0 - 2 * AlleyEconomy.NoLimitsStake;

        // The No Limits Invitational (every build forced ready in the test's own data; No Limits cleared): a semi-final lost (its 600 paid at the loss) and a title
        // won 3-2 in the final (1,500 paid at the end, 4 No Limits Packs opened, 2 tokens). Entries are nolimits.entry, pay is nolimits, never alley.brackets or alley.
        var invSetup = AlleyTest.ReadyInvitational(alley, cards);
        AlleyTest.ClearNoLimits(p);
        p.Earn(Ledger.Debug, 20000);
        int invEntry0 = p.DpSpent.GetValueOrDefault(Ledger.NoLimitsEntry), invPay0 = p.DpEarned.GetValueOrDefault(Ledger.NoLimits);
        int bracketsNow = p.DpSpent.GetValueOrDefault(Ledger.AlleyBrackets), alleyPayNow = p.DpEarned.GetValueOrDefault(Ledger.Alley);
        var noLimitsPacks = ContrabandPacks.NoLimits(invSetup.Data, invSetup.Ctx);
        bool invOk = p.Bracket == null;
        foreach (var results in new[] { new[] { 0, 0, 1 }, new[] { 0, 0, 0, 0, 1, 0 } })
        {
            var ib = Brackets.EnterAlley(p, invSetup.Data, Brackets.InvitationalKind, invSetup.Ctx);
            invOk &= ib != null && Balanced();
            if (ib == null) continue;
            foreach (var r in results)
            {
                if (ib.YourMatch() < 0) break;
                Brackets.StartYours(ib);
                Brackets.RecordYours(p, library, ib, r, loaner: false);
                invOk &= Balanced();
            }
            Brackets.SkipToEnd(ib);
            invOk &= Brackets.Finish(p, library, ib, null, alley.Packs, noLimitsPacks) != null && Balanced();
        }
        int invEntries = p.DpSpent.GetValueOrDefault(Ledger.NoLimitsEntry) - invEntry0, invPay = p.DpEarned.GetValueOrDefault(Ledger.NoLimits) - invPay0;
        invOk &= invEntries == 2 * AlleyEconomy.InvitationalEntry && invPay == AlleyEconomy.InvitationalPay[2] + AlleyEconomy.InvitationalPay[0]
                 && p.DpSpent.GetValueOrDefault(Ledger.AlleyBrackets) == bracketsNow && p.DpEarned.GetValueOrDefault(Ledger.Alley) == alleyPayNow;
        alleyOk &= noLimitsOk && invOk;
        Check($"No Limits: a loss and a quit spend the {AlleyEconomy.NoLimitsStake:N0} DP stake to {Ledger.NoLimitsStake} ({p.DpSpent.GetValueOrDefault(Ledger.NoLimitsStake):N0} DP), never {Ledger.AlleyStake}; "
              + "a win, a draw and the first-turn guard write no line; held mid-duel it reconciles as Dp + held, settled as Dp",
            noLimitsOk);
        Check($"the Invitational: two entries spend {Ledger.NoLimitsEntry} ({invEntries:N0}) and pay {Ledger.NoLimits} ({invPay:N0}: a semi-final's {AlleyEconomy.InvitationalPay[2]} at the loss, the title's {AlleyEconomy.InvitationalPay[0]:N0} at the end), "
              + $"nothing into {Ledger.AlleyBrackets} or {Ledger.Alley}; the ledger balances at every step",
            invOk);

        string Show(Dictionary<string, int> d) => string.Join(", ", d.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key} {x.Value:N0}"));
        Console.WriteLine($"    earned: {Show(p.DpEarned)}");
        Console.WriteLine($"    spent:  {Show(p.DpSpent)}");
        Check($"after a scripted mix (duels, shop, grades, Library): sum(earned) {p.DpEarned.Values.Sum():N0} - sum(spent) {p.DpSpent.Values.Sum():N0} "
              + $"== Dp {p.Dp:N0} - {Profile.StartingDp:N0} at every step",
            duelsOk && shopOk && gradeOk && libraryOk && alleyOk && Balanced());
        Check($"the alley's held stakes: a duel held mid-way reconciles as Dp + held ({AlleyEconomy.StreetStake} held), every settled one as Dp; lost stakes are {Ledger.AlleyStake} "
              + $"({p.DpSpent.GetValueOrDefault(Ledger.AlleyStake):N0} DP: two at Dumpster Row, one at Orvel's Table), stakes that come back write no line; Orvel's rail, the shelf, "
              + $"the used tab and a 4th copy at the van balance ({Ledger.AlleyRail} {p.DpSpent.GetValueOrDefault(Ledger.AlleyRail):N0}, {Ledger.AlleyShelf} "
              + $"{p.DpSpent.GetValueOrDefault(Ledger.AlleyShelf):N0}, {Ledger.AlleyUsed} {p.DpSpent.GetValueOrDefault(Ledger.AlleyUsed):N0}, {Ledger.AlleyRefund} "
              + $"{p.DpEarned.GetValueOrDefault(Ledger.AlleyRefund):N0}); an Alley Brawl and a Midnight Open spend {Ledger.AlleyBrackets} ({bracketEntries:N0}) and pay {Ledger.Alley} "
              + $"({bracketPay:N0}: a semi-final's {AlleyEconomy.BrawlPay[2]} at the loss, the Open's {AlleyEconomy.OpenPay[0]} title at the end), nothing into Locals' lines",
            alleyOk && bracketOk && p.DpSpent.GetValueOrDefault(Ledger.AlleyStake) == 2 * AlleyEconomy.StreetStake + AlleyEconomy.OrvelStake);
        var wantSources = new[]
        {
            Ledger.Circuit, Ledger.Prize(Ledger.Circuit), Ledger.Loss(Ledger.Circuit), Ledger.Tag, Ledger.Loss(Ledger.Tag), Ledger.Guest,
            Ledger.Prize(Ledger.Guest), Ledger.Loss(Ledger.Guest), Ledger.Cup, Ledger.Prize(Ledger.Cup), Ledger.Loss(Ledger.Cup), Ledger.Final,
            Ledger.Loss(Ledger.Final), Ledger.Locals, Ledger.LevelUp, Ledger.Sell, Ledger.StructureRefund, Ledger.Debug, Ledger.AlleyRefund, Ledger.Alley,
            Ledger.NoLimits,
        };
        var wantSinks = new[]
        {
            Ledger.Packs, Ledger.SelectionPacks, Ledger.Boxes, Ledger.Tokens, Ledger.RecipePack, Ledger.RecipeOutright, Ledger.MakeItYours, Ledger.Structure,
            Ledger.Grading, Ledger.Used, Ledger.Order, Ledger.Mats, Ledger.Engrave, Ledger.Brackets, Ledger.AlleyStake, Ledger.AlleyRail, Ledger.AlleyShelf,
            Ledger.AlleyUsed, Ledger.AlleyBrackets, Ledger.NoLimitsStake, Ledger.NoLimitsEntry,
        };
        var missing = wantSources.Where(s => !p.DpEarned.ContainsKey(s)).Concat(wantSinks.Where(s => !p.DpSpent.ContainsKey(s))).ToList();
        Check($"every source and sink the mix touched is counted under its name ({wantSources.Length} sources, {wantSinks.Length} sinks)"
              + (missing.Count > 0 ? $"; missing: {string.Join(", ", missing)}" : ""), missing.Count == 0);
        // Every ledger key is listed here: the mix's and the refunds the mix may not hit.
        // (No Limits, NO-LIMITS-ECONOMY 6.2: the mix has nolimits.stake and, from the Invitational, nolimits.entry and nolimits.)
        var notInMix = new[] { Ledger.PackRefund, Ledger.RecipeRefund, Ledger.UsedRefund, Ledger.OrderRefund, Ledger.PassRefund };
        var listed = wantSources.Concat(wantSinks).Concat(notInMix).Concat(ladderSources).ToHashSet();
        var keys = typeof(Ledger).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!).ToList();
        var unlisted = keys.Where(k => !listed.Contains(k)).ToList();
        Check($"every Ledger key ({keys.Count}) is listed in this check, the alley's 7 and No Limits' 3 included, all in the mix ({Ledger.AlleyStake}, {Ledger.AlleyRail}, {Ledger.AlleyShelf}, "
              + $"{Ledger.AlleyUsed}, {Ledger.AlleyRefund}, {Ledger.AlleyBrackets}, {Ledger.Alley}, {Ledger.NoLimitsStake}, {Ledger.NoLimitsEntry}, {Ledger.NoLimits}){(unlisted.Count > 0 ? "; unlisted: " + string.Join(", ", unlisted) : "")}",
            unlisted.Count == 0 && keys.Count == keys.Distinct().Count() && new[] { "alley.stake", "alley.rail", "alley.shelf", "alley.used", "alley.brackets", "alley", "refund.alley", "nolimits.stake", "nolimits.entry", "nolimits" }.All(keys.Contains));

        // A 30-duel alley session from level 1 (so level-ups happen): every result kind at Dumpster Row, then The Basement
        // Game once it opens. The ledger reconciles at every step, held or not, and alley duels pay no DP at all (E7: at
        // most 0 all-in): level-ups pay tokens only, and lost stakes are the only lines.
        var session = AlleyTest.Opened(starter, cards, printings, library, 0x30D0E15);
        session.Earn(Ledger.Debug, 40000);
        var srng = new Random(30);
        int earned0 = session.DpEarned.Values.Sum(), dp0 = session.Dp, tokens0 = session.Tokens, level0 = session.Level, steps = 0, balancedSteps = 0, lostStakes = 0;
        var levelUps = new List<LevelUp>();
        for (int d = 0; d < 30; d++)
        {
            int table = BackAlley.IsUnlocked(session, alley.Data, alley.Data.IndexOf("basement")) && d % 2 == 1 ? alley.Data.IndexOf("basement") : dumpster;
            var seat = BackAlley.Lineup(session, alley.Data, table, alley.Ctx).First(o => o.Seat == BackAlley.NextSeat(session, alley.Data, table));
            var live = Ante.Hold(session, alley.Data, table, seat, null, none, alley.Ctx);
            steps += 2;
            if (live != null && Ledger.Net(session) == session.Dp + live.Held - Profile.StartingDp) balancedSteps++;
            int roll = srng.Next(10);
            if (roll == 9) { lostStakes += Ante.SettleAbandoned(session, alley.Ctx)?.Lost ?? 0; }
            else
            {
                var (winner, turns, first) = roll switch { < 6 => (0, 10, 0), 6 => (1, 10, 1), 7 => (2, 41, 0), _ => (1, 1, 1) };
                var r = BackAlley.Record(session, alley.Data, table, winner, turns, first, alley.Ctx);
                lostStakes += r.Ante?.Lost ?? 0;
                levelUps.AddRange(r.Reward.LevelUps);
            }
            if (Ledger.Net(session) == session.Dp - Profile.StartingDp) balancedSteps++;
        }
        int clearTokens = session.AlleyTables.Values.Count(t => t.Cleared) * AlleyEconomy.ClearTokens;
        Check($"a 30-duel alley session reconciles at all {steps} steps ({balancedSteps} balanced, held stakes counted); its duels earned {session.DpEarned.Values.Sum() - earned0} DP "
              + $"(E7: at most 0 all-in; {session.Dp - dp0:+#,0;-#,0;0} DP, all of it {lostStakes:N0} in lost stakes); {levelUps.Count} level-ups (level {level0} -> {session.Level}) "
              + $"paid {session.Tokens - tokens0 - clearTokens} tokens and no DP",
            balancedSteps == steps && session.DpEarned.Values.Sum() == earned0 && session.Dp - dp0 == -lostStakes && session.DpSpent.GetValueOrDefault(Ledger.AlleyStake) == lostStakes
            && levelUps.Count > 0 && levelUps.All(u => u.Dp == 0) && session.Tokens - tokens0 == levelUps.Sum(u => u.Tokens) + clearTokens && session.AlleyDuels == 30);

        // The save: "earned" and "spent" lines round-trip; a bought Ban Pass comes back as "passrefund" at load and still balances.
        p.PassesBought = 1;
        var path = Path.Combine(Path.GetTempPath(), $"ygo-ledger-{Guid.NewGuid():N}.txt");
        p.Save(path);
        var back = Profile.Load(path)!;
        File.Delete(path);
        bool same = p.DpEarned.All(e => back.DpEarned.GetValueOrDefault(e.Key) == e.Value) && p.DpSpent.All(s => back.DpSpent.GetValueOrDefault(s.Key) == s.Value);
        Check($"the ledger round-trips a save; a bought Ban Pass is refunded at load as {Ledger.PassRefund} ({back.DpEarned.GetValueOrDefault(Ledger.PassRefund):N0} DP) and it still balances",
            same && back.DpEarned.GetValueOrDefault(Ledger.PassRefund) == DpServices.BoughtPassRefund(1) && Ledger.Net(back) == back.Dp - Profile.StartingDp);
        back.NewGamePlus();
        Check("New Game+ keeps the ledger", back.DpEarned.Count == p.DpEarned.Count + 1 && back.DpSpent.Count == p.DpSpent.Count);

        // The 3-copy cap counts copies at the Grading Desk: own 3, send one away, and neither the trader nor a recipe takes a 4th.
        const uint ash = 14558127;
        var capped = Profile.New(starter, cards, printings);
        capped.Dp = 100000;
        capped.Tokens = 10;
        for (int i = 0; i < Collection.MaxCopies; i++) capped.Collection.Add(ash, printings.Commonest(ash, 0), 1, CopySource.Shop, 300);
        GradingDesk.Submit(capped, capped.Collection.CopiesOf(ash).First(), express: false, none);
        var ashRecipe = new Deck { Name = "ash-test" };
        for (int i = 0; i < Collection.MaxCopies; i++) ashRecipe.Main.Add(ash);
        var traderSays = CardTrader.Blocked(ash, capped, 2026, printings, cards);
        int completed = RecipeShop.Complete(ashRecipe, capped, Banlist.None, 2026, printings, cards, 300, Ledger.MakeItYours);
        Check($"own 3, send 1 to grading: the trader refuses a 4th (\"{traderSays}\"), the recipe misses nothing and Make it yours buys nothing",
            capped.Collection.Count(ash) == 2 && traderSays == $"you already have {Collection.MaxCopies}" && !CardTrader.Trade(ash, capped, 2026, printings, cards)
            && RecipeShop.Missing(ashRecipe, capped, Banlist.None, 2026, printings, cards).Count == 0 && completed == 0 && capped.Collection.Owned(ash) == 3);

        // A structure deck's refund for extra copies is at most half its price (a deck bought three times: the 2nd and 3rd are all extras).
        var builder = Profile.New(starter, cards, printings);
        builder.Earn(Ledger.Debug, 100000);
        var structureDeck = Deck.LoadYdk(structureFile);
        var refunds = new List<int>();
        foreach (var price in new[] { 1500, 1500, 1500, 400 })
        {
            int before = builder.DpEarned.GetValueOrDefault(Ledger.StructureRefund);
            builder.BuyStructureDeck(structureDeck, price, cards);
            refunds.Add(builder.DpEarned.GetValueOrDefault(Ledger.StructureRefund) - before);
        }
        var prices = new[] { 1500, 1500, 1500, 400 };
        Check($"a structure deck's refund is at most half its price (refunds {string.Join(", ", refunds.Select((r, i) => $"{r}/{prices[i]}"))})",
            refunds.Select((r, i) => r <= prices[i] / 2).All(x => x) && refunds[1] > 0);

        failures += AcrossSaves(Check, cards, root, printings, catalog, starter);
        failures += SourceGuard(Check, root);
        failures += CampaignGates.Run(cards, root, printings, catalog, roster, circuits, library, guests, starter);
        return failures;
    }

    /// <summary>
    /// Every shop sink, one at a time, each followed by a save and a load (the next step plays on the loaded save): packs
    /// (a booster, a house pack, a box), a token, a recipe pack, a recipe outright, Make it yours, a structure deck, grading,
    /// a sale, a used-market buy, a special order. After each, the loaded save's Dp - StartingDp == Ledger.Net, the DP that
    /// moved is what the ledger moved, and the step's own sink or source moved by exactly the price the shop quoted before
    /// (no DP amount changed). Failures are counted by the caller's Check.
    /// </summary>
    private static int AcrossSaves(Action<string, bool> Check, ICardDataProvider cards, string root, PrintingIndex printings, PackCatalog catalog, Deck starter)
    {
        Profile Prep(Profile x)
        {
            x.Collection.CommonestOf = c => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
            return x;
        }
        var s = Prep(Profile.New(starter, cards, printings));
        s.Collection.Salt = 20261105;
        s.Earn(Ledger.Debug, 400000);
        var rng = new Random(1105);
        int none(uint _) => 0;
        var rows = new List<string>();
        var bad = new List<string>();
        int saves = 0;
        // act: does the step on the save and returns the DP the shop quoted for it (before acting), or -1 when it failed.
        void Step(string kind, bool sink, Func<Profile, int> act)
        {
            int dp0 = s.Dp, net0 = Ledger.Net(s), k0 = (sink ? s.DpSpent : s.DpEarned).GetValueOrDefault(kind);
            int quoted = act(s);
            var path = Path.Combine(Path.GetTempPath(), $"ygo-ledger-saves-{Guid.NewGuid():N}.txt");
            s.Save(path);
            s = Prep(Profile.Load(path)!);
            File.Delete(path);
            saves++;
            int moved = (sink ? s.DpSpent : s.DpEarned).GetValueOrDefault(kind) - k0;
            bool ok = quoted > 0 && moved == quoted && s.Dp - dp0 == Ledger.Net(s) - net0 && Ledger.Net(s) == s.Dp - Profile.StartingDp;
            rows.Add($"{kind} {(sink ? "-" : "+")}{moved:N0}");
            if (!ok) bad.Add($"{kind} (quoted {quoted}, ledger {moved}, Dp {s.Dp - dp0:+#,0;-#,0;0}, net {Ledger.Net(s) - net0:+#,0;-#,0;0})");
        }

        var set = catalog.Sets.First(x => x.Price > 0);
        for (int i = 0; i < 3; i++)
            Step(set.Sink, true, p => { if (!p.Spend(set.Sink, set.Price)) return -1; p.OpenPack(set, rng, out _); return set.Price; });
        var recipe = Deck.LoadYdk(Path.Combine(root, "decks", "history", "mitsurugi.ydk"));
        recipe.Name = "mitsurugi";
        Step(Ledger.SelectionPacks, true, p =>
        {
            var house = SelectionPacks.Build("mitsurugi", "SP-mitsurugi", recipe.Main.Select(c => Banlist.BaseCode(c, cards)), p, 2026, printings, cards);
            if (!p.Spend(house.Sink, house.Price)) return -1;
            p.OpenPack(house, rng, out _);
            return house.Price;
        });
        Step(Ledger.Boxes, true, p => { int price = ShopPrices.Box(set); BoosterBox.Open(p, set, price, rng); return price; });
        for (int i = 0; i < 2; i++)
            Step(Ledger.Tokens, true, p => { int price = DpServices.TokenPrice(p); return DpServices.BuyToken(p) ? price : -1; });
        Step(Ledger.RecipePack, true, p => RecipeShop.OpenPack(recipe, p, Banlist.None, 2026, printings, cards, rng, 450).Count > 0 ? 450 : -1);
        Step(Ledger.RecipeOutright, true, p =>
        {
            int price = RecipeShop.CompletePrice(recipe, p, Banlist.None, 2026, printings, cards, 450);
            return RecipeShop.Complete(recipe, p, Banlist.None, 2026, printings, cards, 450) > 0 ? price : -1;
        });
        var other = Deck.LoadYdk(Path.Combine(root, "decks", "history", "goat-control.ydk"));
        Step(Ledger.MakeItYours, true, p =>
        {
            int price = RecipeShop.CompletePrice(other, p, Banlist.None, 9999, printings, cards, 300);
            return RecipeShop.Complete(other, p, Banlist.None, 9999, printings, cards, 300, Ledger.MakeItYours) > 0 ? price : -1;
        });
        var structureFile = Directory.GetFiles(Path.Combine(root, "decks", "structure"), "*.ydk").OrderBy(f => f).First();
        Step(Ledger.Structure, true, p => p.BuyStructureDeck(Deck.LoadYdk(structureFile), 1500, cards) > 0 ? 1500 : -1);
        Step(Ledger.Grading, true, p =>
        {
            var copy = p.Collection.All.Where(c => c.Paid > 0 && !c.AtDesk && !c.IsSlab).OrderByDescending(c => p.Collection.Value(c)).First();
            int fee = GradingDesk.FeeFor(p.Collection, copy, express: true);
            return GradingDesk.Submit(p, copy, express: true, none) > 0 ? fee : -1;
        });
        for (int i = 0; i < 2; i++)
            Step(Ledger.Sell, false, p =>
            {
                var copy = p.Collection.All.Where(c => c.Paid > 0 && !c.AtDesk && p.Collection.SellPrice(c) > 0).OrderByDescending(c => p.Collection.SellPrice(c)).First();
                int price = p.Collection.SellPrice(copy);
                return SellDesk.Sell(p, copy, none) == price ? price : -1;
            });
        var poolCodes = cards.All.Where(c => c.Alias == 0 && printings.Of(c.Code, 0).Length > 1).Take(300).Select(c => c.Code).ToList();
        for (int i = 0; i < 2; i++)
            Step(Ledger.Used, true, p =>
            {
                var listing = UsedMarket.Listings(p, poolCodes, 2026, printings, cards).FirstOrDefault(l => UsedMarket.Blocked(p, l) == null);
                return listing != null && UsedMarket.Buy(p, listing) >= 0 ? listing.Price : -1;
            });
        Step(Ledger.Order, true, p =>
        {
            var code = poolCodes.First(c => UsedMarket.Orderable(c, printings, cards).Count > 0 && p.Collection.Owned(c) == 0);
            var rarity = UsedMarket.Orderable(code, printings, cards).Last();
            int price = UsedMarket.OrderPrice(code, rarity, printings, cards);
            return UsedMarket.Order(p, code, rarity, 2026, printings, cards) != null ? price : -1;
        });
        Check($"across {saves} saves (each sink, then save and load): the loaded save's Dp - {Profile.StartingDp:N0} == Ledger.Net every time "
              + $"(now {s.Dp - Profile.StartingDp:N0} == {Ledger.Net(s):N0}), and each step moved its own line by the price quoted ({string.Join(", ", rows)})"
              + (bad.Count > 0 ? "; WRONG: " + string.Join("; ", bad) : ""),
            bad.Count == 0 && rows.Count == saves && Ledger.Net(s) == s.Dp - Profile.StartingDp);
        return 0;
    }

    /// <summary>
    /// The engine moves Profile.Dp only in Spend and Earn, and an alley stake's HoldStake/ReturnStake (the one move with no
    /// line), which only Ante.Hold and Ante's settlement call. A scan of src/Ygo.Engine (comments and string literals
    /// blanked) for every way to change a Dp field: an assignment or compound assignment, ++/--, and out/ref. Each hit is
    /// allowed by the method it sits in (not its line text), and each allowed method by how many hits it may have: Spend,
    /// Earn, HoldStake, ReturnStake, the save's "dp" line (out, in Profile.Load), Profile's field, and the Added and LevelUp
    /// constructors (their own Dp). A duel reward's Dp (receiver "reward") is a DuelReward's. HoldStake and ReturnStake
    /// are called nowhere else, in the engine or in unity/Assets/Scripts. Skipped when the sources aren't there.
    /// </summary>
    private static int SourceGuard(Action<string, bool> Check, string root)
    {
        var engine = Path.Combine(root, "src", "Ygo.Engine");
        if (!Directory.Exists(engine)) { Console.WriteLine("  (the DP source scan is skipped: no src/Ygo.Engine here)"); return 0; }
        var stakeCall = new System.Text.RegularExpressions.Regex(@"\b(HoldStake|ReturnStake)\s*\(");
        // file|method (or "(field)" outside any method) -> hits allowed there.
        var allowed = new Dictionary<string, int>
        {
            ["Profile.cs|Spend"] = 1, ["Profile.cs|Earn"] = 1, ["Profile.cs|HoldStake"] = 1, ["Profile.cs|ReturnStake"] = 1,
            ["Profile.cs|Load"] = 1, ["Profile.cs|(field)"] = 1, ["Profile.cs|Added"] = 1, ["Leveling.cs|LevelUp"] = 1,
        };
        var hits = new Dictionary<string, List<string>>();
        var stray = new List<string>();
        int scanned = 0;
        foreach (var file in Directory.GetFiles(engine, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            scanned++;
            var code = CodeOnly(File.ReadAllText(file));
            var methodAt = MethodsOf(code);
            string Where(int at) => $"{Path.GetRelativePath(root, file)}:{code.Take(at).Count(c => c == '\n') + 1}";
            foreach (var (key, at, what) in DpWrites(Path.GetFileName(file), code))
            {
                if (!hits.TryGetValue(key, out var list)) hits[key] = list = new List<string>();
                list.Add($"{Where(at)}: {what}");
            }
            foreach (System.Text.RegularExpressions.Match m in stakeCall.Matches(code))
            {
                var method = methodAt(m.Index);
                var name = Path.GetFileName(file);
                bool ok = name == "Ante.cs" && (method == "Hold" && m.Groups[1].Value == "HoldStake" || method == "SettleAs" && m.Groups[1].Value == "ReturnStake")
                          || name == "Profile.cs" && method == null; // the declarations
                if (!ok) stray.Add($"{Where(m.Index)}: a {m.Groups[1].Value} call in {method ?? "(no method)"}");
            }
        }
        foreach (var (key, list) in hits.OrderBy(h => h.Key, StringComparer.Ordinal))
            if (!allowed.TryGetValue(key, out var max) || list.Count > max) stray.AddRange(list.Select(l => $"{l} (in {key})"));
        int unityFiles = 0;
        var unity = Path.Combine(root, "unity", "Assets", "Scripts");
        if (Directory.Exists(unity))
            foreach (var file in Directory.GetFiles(unity, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                unityFiles++;
                var code = CodeOnly(File.ReadAllText(file));
                foreach (System.Text.RegularExpressions.Match m in stakeCall.Matches(code))
                    stray.Add($"{Path.GetRelativePath(root, file)}:{code.Take(m.Index).Count(c => c == '\n') + 1}: a {m.Groups[1].Value} call");
            }
        // The scan itself, on a made-up file: every form is caught, in the method it's in; comments, strings, interpolation
        // holes and a reward's Dp are not.
        const string probe = "class Profile {\n public int Dp = 1000;\n public bool Spend(string s, int dp) { Dp -= dp; return true; }\n"
            + " void Sneak(Profile profile, Profile p, Profile x, DuelReward reward) {\n  Dp++; profile.Dp = 5; Bump(ref p.Dp); --x.Dp; x.Dp *= 2; Dp = dp;\n"
            + "  var a = \"Dp = 1\"; var b = $\"{(a.Length > 0 ? \"}\" : \"{\")} Dp = 2\"; var c = '{'; // Dp = 3\n  /* Dp = 4 */ reward.Dp = 6; reward.Dp += 1;\n }\n}\n";
        var probed = DpWrites("Profile.cs", CodeOnly(probe)).GroupBy(h => h.key).ToDictionary(g => g.Key, g => g.Count());
        bool probeOk = probed.Count == 3 && probed.GetValueOrDefault("Profile.cs|(field)") == 1 && probed.GetValueOrDefault("Profile.cs|Spend") == 1
                       && probed.GetValueOrDefault("Profile.cs|Sneak") == 6;
        if (!probeOk) stray.Add("the scan's own probe: " + string.Join(", ", probed.Select(p => $"{p.Key} {p.Value}")));
        var found = allowed.Keys.Where(hits.ContainsKey).Count();
        Check($"the engine's {scanned} source files change a Dp field (=, op=, ++/--, out/ref) only in Spend, Earn, HoldStake, ReturnStake and the known "
              + $"others ({found} of {allowed.Count} allowed methods hit, each within its count), and HoldStake/ReturnStake are called only by Ante.Hold and its "
              + $"settlement ({unityFiles} unity scripts scanned too)" + (stray.Count > 0 ? "; outside them: " + string.Join(" | ", stray) : ""),
            stray.Count == 0 && scanned > 0 && found == allowed.Count);
        return 0;
    }

    private static readonly System.Text.RegularExpressions.Regex DpWrite = new System.Text.RegularExpressions.Regex(
        @"(?<recv>[\w.]*?)\bDp\s*(?:\+=|-=|\*=|/=|%=|\?\?=|<<=|>>=|&=|\|=|\^=|=(?![=>])|\+\+|--)|(?:\+\+|--)\s*(?<recv>[\w.]*?)\bDp\b|\b(?:out|ref)\s+(?<recv>[\w.]*?)\bDp\b");

    /// <summary>
    /// Every write to a Dp field in <paramref name="code"/> (from <see cref="CodeOnly"/>): "file|method" (or "file|(field)"),
    /// its position and text. A receiver named reward is a DuelReward's Dp and is left out.
    /// </summary>
    internal static List<(string key, int at, string what)> DpWrites(string fileName, string code)
    {
        var methodAt = MethodsOf(code);
        var found = new List<(string, int, string)>();
        foreach (System.Text.RegularExpressions.Match m in DpWrite.Matches(code))
        {
            var recv = m.Groups["recv"].Value.TrimEnd('.');
            if (recv == "reward" || recv.EndsWith(".reward")) continue;
            found.Add((fileName + "|" + (methodAt(m.Index) ?? "(field)"), m.Index, m.Value.Trim()));
        }
        return found;
    }

    /// <summary>
    /// <paramref name="text"/> with comments and string and char literals (interpolation holes included) blanked to spaces,
    /// newlines kept, so positions and line numbers still match and braces and "Dp =" inside them don't count.
    /// </summary>
    internal static string CodeOnly(string text)
    {
        var o = text.ToCharArray();
        int n = text.Length;
        void Blank(int from, int to) { for (int k = from; k < to && k < n; k++) if (o[k] != '\n') o[k] = ' '; }
        // Returns the index just past a literal starting at i (a ", @", $", $@" or @$ string, or a char literal).
        int Literal(int i)
        {
            int j = i;
            bool verbatim = false, interp = false;
            while (j < n && (text[j] == '@' || text[j] == '$')) { verbatim |= text[j] == '@'; interp |= text[j] == '$'; j++; }
            if (j < n && text[j] == '\'')
            {
                j++;
                if (j < n && text[j] == '\\') j += 2; else j++;
                while (j < n && text[j] != '\'' && text[j] != '\n') j++;
                return Math.Min(n, j + 1);
            }
            if (j + 2 < n && text[j] == '"' && text[j + 1] == '"' && text[j + 2] == '"')
            {
                int end = text.IndexOf("\"\"\"", j + 3, StringComparison.Ordinal);
                return end < 0 ? n : end + 3;
            }
            j++; // the opening quote
            while (j < n)
            {
                char c = text[j];
                if (verbatim) { if (c == '"') { if (j + 1 < n && text[j + 1] == '"') { j += 2; continue; } return j + 1; } }
                else
                {
                    if (c == '\\') { j += 2; continue; }
                    if (c == '"') return j + 1;
                    if (c == '\n') return j;
                }
                if (interp && c == '{')
                {
                    if (j + 1 < n && text[j + 1] == '{') { j += 2; continue; }
                    j++;
                    for (int depth = 1; j < n && depth > 0;)
                    {
                        char h = text[j];
                        if (h == '"' || h == '\'' || (h == '@' || h == '$') && j + 1 < n && (text[j + 1] == '"' || text[j + 1] == '@' || text[j + 1] == '$')) { j = Literal(j); continue; }
                        if (h == '{') depth++;
                        else if (h == '}') depth--;
                        j++;
                    }
                    continue;
                }
                j++;
            }
            return n;
        }
        for (int i = 0; i < n;)
        {
            char c = text[i];
            if (c == '/' && i + 1 < n && text[i + 1] == '/') { int e = text.IndexOf('\n', i); e = e < 0 ? n : e; Blank(i, e); i = e; continue; }
            if (c == '/' && i + 1 < n && text[i + 1] == '*') { int e = text.IndexOf("*/", i + 2, StringComparison.Ordinal); e = e < 0 ? n : e + 2; Blank(i, e); i = e; continue; }
            if (c == '"' || c == '\'' || (c == '@' || c == '$') && i + 1 < n && (text[i + 1] == '"' || text[i + 1] == '@' || text[i + 1] == '$'))
            {
                int e = Literal(i);
                Blank(i, e);
                i = e;
                continue;
            }
            i++;
        }
        return new string(o);
    }

    /// <summary>
    /// For code from <see cref="CodeOnly"/>: a lookup from a position to the innermost method (or constructor, operator,
    /// accessor-bearing member) whose braced body holds it, null outside any (fields, declarations, types).
    /// </summary>
    /// <summary>A member header's end: its name, type parameters, parameter list, and a where clause or base/this call.</summary>
    private static readonly System.Text.RegularExpressions.Regex MemberHeader = new System.Text.RegularExpressions.Regex(
        @"(?<id>~?\b\w+)\s*(<[^(){};]*>)?\s*\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\)\s*(where\b.*|:\s*(base|this)\s*\(.*\))?$",
        System.Text.RegularExpressions.RegexOptions.Singleline);

    internal static Func<int, string?> MethodsOf(string code)
    {
        var spans = new List<(int open, int close, string name)>();
        var stack = new Stack<(int open, string? name)>();
        var notMembers = new HashSet<string> { "if", "for", "foreach", "while", "switch", "catch", "using", "lock", "fixed", "return", "nameof", "typeof", "sizeof", "default", "checked", "unchecked", "new", "when" };
        int headerStart = 0;
        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            if (c == ';') { headerStart = i + 1; continue; }
            if (c == '{')
            {
                var header = code.Substring(headerStart, i - headerStart).Trim();
                string? name = null;
                if (!header.EndsWith("=>"))
                {
                    var sig = MemberHeader.Match(header);
                    var id = sig.Success ? sig.Groups["id"].Value : "";
                    var prefix = sig.Success ? header.Substring(0, sig.Index).TrimEnd() : "";
                    if (id.Length > 0 && !notMembers.Contains(id) && !prefix.Contains('=') && !prefix.EndsWith("new") && !prefix.EndsWith("."))
                        name = id;
                }
                stack.Push((i, name));
                headerStart = i + 1;
                continue;
            }
            if (c == '}')
            {
                if (stack.Count > 0)
                {
                    var (open, name) = stack.Pop();
                    if (name != null) spans.Add((open, i, name));
                }
                headerStart = i + 1;
            }
        }
        return at => spans.Where(s => s.open < at && at < s.close).OrderByDescending(s => s.open).Select(s => s.name).FirstOrDefault();
    }
}
