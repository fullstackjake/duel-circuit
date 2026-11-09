using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--arbitrage`'s No Limits loops (docs/dev/NO-LIMITS-ECONOMY-2026-11-02.md 6.1, loops 1-10): each starts from a save with No Limits open
/// and every pilot's binder made, and runs on the real code (Ante, BackAlley, MothsVan, the Grading Desk, the Sell desk, Orvel's Table
/// and Moth's rail, Brackets and the No Limits Pack). Then the separate exhaustive search the economy asks for: 12 actions over 1-4
/// steps (22,620 sequences), none DP-positive, every sequence with a lost stake or a quit below 0.
/// <para>
/// Loop 5 (the restored shelf slots) buys and sells every slab the real <see cref="MothsVan.Shelf"/> can show in a restored slot
/// (NL-2's engine: the ready builds' `stakes:` cards once the wall is open). Loops 6-8 are the Invitational's: its placements,
/// its packs graded, and all three alley brackets won in turn. The Invitational's field wants all eleven builds ready, so they
/// run on <see cref="AlleyTest.ReadyInvitational"/> (a copy of the data; wave 2's decks don't exist yet, so the No Limits Pack's
/// pool is wave 1's).
/// </para>
/// </summary>
public static partial class Arbitrage
{
    private static int NoLimitsLoops(ICardDataProvider cards, string root, PrintingIndex printings, List<Personality> roster, Library library, int seed)
    {
        Console.WriteLine("\nNo Limits: loops 1-10 of NO-LIMITS-ECONOMY 6.1 on the real code, then the 12-action search (22,620 sequences):");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var a = AlleyTest.Load(cards, root, printings, roster, library);
        var s = AlleyTest.ReadyNoLimits(a, cards);
        var data = s.Data;
        var ctx = s.Ctx;
        int nlt = data.IndexOf("no-limits"), orvel = data.IndexOf("orvels-table");
        var builds = data.Builds.Where(b => b.Ready && ctx.LoadDeck(b.Deck) != null).ToList();
        var packs = a.Packs;
        int none(uint _) => 0;
        string Name(uint c) => cards.Get(c)?.Name ?? c.ToString();
        var starter = Deck.LoadYdk(Path.Combine(root, "decks", "classic.ydk"));
        starter.Name = "Starter";
        const int Top = 50_000_000; // past level 99: no level-up DP
        const int Stake = 2500;

        // The save every loop starts from: No Limits open (the wall raised), tonight's lineup drawn and every pilot's binder made, Orvel's Table open.
        string TemplateText()
        {
            var p = AlleyTest.NoLimitsOpen(starter, cards, printings, library, (uint)seed * 2654435761u + 29, dp: 10_000_000);
            p.Exp = Top;
            BackAlley.Lineup(p, data, nlt, ctx);
            foreach (var b in builds) Ante.Binder(p, "nl:" + b.Id, ctx);
            foreach (var o in BackAlley.Lineup(p, data, orvel, ctx)) Ante.Binder(p, o.Binder, ctx);
            return AlleyTest.SaveText(p);
        }
        var template = TemplateText();
        Profile Fresh(string text = "") => AlleyTest.FromText(text.Length > 0 ? text : template, printings, cards);
        int Liquidate(Profile p)
        {
            if (p.AnteLive != null) Ante.SettleAbandoned(p, ctx);
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
            int dp0 = p.Dp + (p.AnteLive?.Held ?? 0), spent0 = p.DpSpent.Values.Sum();
            act(p);
            Liquidate(p);
            return (what, p.DpSpent.Values.Sum() - spent0, p.Dp - dp0);
        }
        int bad = 0;
        var ratios = new List<(string loop, string what, int spent, int net)>();
        // zero: every try exactly 0. negative: every try below 0 (a lost stake, a quit, a crack and regrade). Otherwise: none DP-positive.
        void Verdict(string loop, IEnumerable<(string what, int spent, int net)> tries, bool zero = false, bool negative = false)
        {
            var list = tries.ToList();
            if (list.Count == 0) { Console.WriteLine($"  {loop}: nothing to try"); return; }
            var best = list.OrderByDescending(t => t.net).First();
            bool fail = best.net > 0 || (zero && list.Any(t => t.net != 0)) || (negative && list.Any(t => t.net >= 0));
            if (fail) bad++;
            ratios.Add((loop, best.what, best.spent, best.net));
            Console.WriteLine($"  {(fail ? "FAIL" : "ok  ")} {loop}: best {best.what}: net {best.net:+#,0;-#,0;0} DP on {best.spent:N0} spent ({list.Count} tried"
                              + (zero ? $", {list.Count(t => t.net == 0)} at exactly 0" : negative ? $", {list.Count(t => t.net < 0)} below 0" : "") + ")");
        }
        // The seat the profile is at tonight (1-4) and its opponent.
        AlleyOpponent? Seat(Profile p) => BackAlley.Lineup(p, data, nlt, ctx).FirstOrDefault(x => x.Seat == BackAlley.NextSeat(p, data, nlt));
        void Hold(Profile p) { if (Seat(p) is AlleyOpponent o) Ante.Hold(p, data, nlt, o, null, none, ctx); }
        // Win seats 1..k-1, then one ending at seat k.
        void ToSeat(Profile p, int seat) { for (int k = 1; k < seat; k++) AlleyTest.Duel(p, s, nlt, 0); }

        // Loop 1: the ante at every seat (seat 4 too). Wins, draws and the guard exactly 0; a loss or a quit below 0; reload mid-duel.
        var zeros = new List<(string, int, int)>();
        var negatives = new List<(string, int, int)>();
        for (int seat = 1; seat <= BackAlley.Matches; seat++)
        {
            int k = seat;
            zeros.Add(Measure($"seat {k} win", p => { ToSeat(p, k); AlleyTest.Duel(p, s, nlt, 0); }));
            zeros.Add(Measure($"seat {k} draw", p => { ToSeat(p, k); AlleyTest.Duel(p, s, nlt, 2, AlleyEconomy.AlleyTurns + 1); }));
            zeros.Add(Measure($"seat {k} first-turn loss", p => { ToSeat(p, k); AlleyTest.Duel(p, s, nlt, 1, AlleyEconomy.NoLimitsGuardTurns, 1); }));
            zeros.Add(Measure($"seat {k} first-turn losses x10", p => { ToSeat(p, k); for (int r = 0; r < 10; r++) AlleyTest.Duel(p, s, nlt, 1, 1, 1); }));
            negatives.Add(Measure($"seat {k} loss", p => { ToSeat(p, k); AlleyTest.Duel(p, s, nlt, 1); }));
            negatives.Add(Measure($"seat {k} second-turn loss going second", p => { ToSeat(p, k); AlleyTest.Duel(p, s, nlt, 1, AlleyEconomy.NoLimitsGuardTurns + 1, 1); }));
            negatives.Add(Measure($"seat {k} quit", p => { ToSeat(p, k); Hold(p); Ante.SettleAbandoned(p, ctx); }));
            // The app closed mid-duel: the save holds the stake, and the load settles it as a loss.
            var held = Fresh(); ToSeat(held, k); Hold(held);
            negatives.Add(Measure($"seat {k} left at load", p => Ante.SettleAbandoned(p, ctx), AlleyTest.SaveText(held)));
        }
        Verdict("1. No Limits ante, every seat: win, draw, first-turn loss (and 10 in a row), sell everything: exactly 0", zeros, zero: true);
        Verdict("1. No Limits ante, every seat: loss, second-turn loss, quit, left at load: every one below 0", negatives, negative: true);

        // Loop 2: the restock. Win every seat for 10 nights (the leads rotate), sell everything: exactly 0.
        Verdict("2. the restock: win every seat for 10 nights (leads rotate), sell everything: exactly 0", new[] { Measure("10 nights, 40 wins", p =>
        {
            for (int d = 0; d < 10 * BackAlley.Matches; d++) AlleyTest.Duel(p, s, nlt, 0);
        }) }, zero: true);

        // Loop 3: a won lead slab cracked, regraded (to 10s), sold: below 0 (at most half the fee back).
        var graded = new List<(string, int, int)>();
        for (int night = 0; night < 8; night++)
        {
            int n = night;
            graded.Add(Measure($"night {n}'s four leads", p =>
            {
                p.AlleyTables[data.Tables[nlt].Id] = new AlleyTableState { Night = n, Cleared = true };
                int before = p.Collection.NextId;
                for (int d = 0; d < BackAlley.Matches; d++) AlleyTest.Duel(p, s, nlt, 0);
                GradeAll(p, p.Collection.All.Where(c => c.Source == CopySource.Ante && c.Id >= before));
            }));
        }
        Verdict("3. a won lead slab cracked, regraded (to 10s), sold: below 0", graded, negative: true);

        // Loop 4: a won No Limits lead staked at Orvel's, lost, bought back off the rail (1-5 rounds), graded, sold.
        var rails = new List<(string, int, int)>();
        for (int night = 0; night < 6; night++)
            for (int rounds = 1; rounds <= 5; rounds += 2)
            {
                int n = night, r = rounds;
                rails.Add(Measure($"night {n}, {r} round{(r == 1 ? "" : "s")}", p =>
                {
                    p.AlleyTables[data.Tables[nlt].Id] = new AlleyTableState { Night = n, Cleared = true };
                    var won = AlleyTest.Duel(p, s, nlt, 0)?.Ante?.Won;
                    if (won == null || !p.Collection.All.Contains(won)) return;
                    for (int round = 0; round < r; round++)
                        if (AlleyTest.OrvelDuel(p, s, won, 1)?.Ante?.Railed is RailEntry e) Ante.BuyBack(p, e);
                    GradeAll(p, new[] { won });
                }));
            }
        var played = rails.Where(t => t.Item2 > 0).ToList();
        Console.WriteLine($"      ({played.Count} of {rails.Count} tries found an Orvel match for the won lead; the rest stake nothing and spend nothing)");
        Verdict("4. a won No Limits lead staked at Orvel's, lost, bought back off the rail (1-5 rounds), graded, sold: below 0", played, negative: true);

        // Loop 5: the restored shelf slots on the real MothsVan.Shelf (wall open, wave 1 ready): every slab a restored slot can show is bought through
        // MothsVan.Buy and sold at Moth's Sell tab; and cracked, regraded, sold. At most a sixth back; below 0.
        var restoredItems = MothsVan.RestoredItems(data, ctx);
        var seenKeys = new HashSet<string>();
        var restoredAt = new List<(int epoch, int slot, string name)>();
        {
            var scan = Fresh();
            for (int epoch = 0; epoch < 2000 && seenKeys.Count < restoredItems.Count; epoch++)
            {
                scan.AlleyDuels = epoch * AlleyEconomy.ShelfEvery;
                foreach (var l in MothsVan.Shelf(scan, packs, ctx).Where(l => l.Restored))
                    if (seenKeys.Add(MothsVan.ShelfKey(l.Copy.Code, l.Copy.Rarity, l.Copy.Slab)))
                        restoredAt.Add((epoch, l.Slot, $"{Name(l.Copy.Code)} {l.Copy.Rarity} slab {l.Copy.Slab} at {l.Price:N0} (restock {epoch}, slot {l.Slot})"));
            }
        }
        CardCopy? BuyRestored(Profile p, int epoch, int slot)
        {
            p.AlleyDuels = epoch * AlleyEconomy.ShelfEvery;
            var l = MothsVan.Shelf(p, packs, ctx).FirstOrDefault(x => x.Slot == slot && x.Restored);
            if (l == null || MothsVan.Buy(p, l, shelf: true) < 0) return null;
            return p.Collection.All.FirstOrDefault(c => c.Seed == l.Copy.Seed && c.Code == l.Copy.Code && c.Paid == l.Price);
        }
        var toSell = restoredAt.Select(x => Measure(x.name, p => { if (BuyRestored(p, x.epoch, x.slot) is CardCopy c) MothsVan.Sell(p, c, none); })).ToList();
        var toGrade = restoredAt.Select(x => Measure(x.name, p => { if (BuyRestored(p, x.epoch, x.slot) is CardCopy c) GradeAll(p, new[] { c }); })).ToList();
        Console.WriteLine($"      ({restoredAt.Count} of the {restoredItems.Count} slabs the restored slots can show came up in {(restoredAt.Count > 0 ? restoredAt.Max(x => x.epoch) + 1 : 0)} restocks; the rest of each shelf is the regular draw, loop 'the shelf' of the alley loops)");
        bool coverOk = restoredItems.Count > 0 && restoredAt.Count == restoredItems.Count;
        if (!coverOk) bad++;
        Console.WriteLine($"  {(coverOk ? "ok  " : "FAIL")} 5. every possible restored slab came up on the real shelf and was tried ({restoredAt.Count} of {restoredItems.Count})");
        Verdict($"5. the restored shelf slots ({restoredAt.Count} slabs from the ready builds' leads) -> Moth's Sell tab: below 0", toSell, negative: true);
        Verdict("5. the restored shelf slots -> crack, regrade (to 10s), sell: below 0", toGrade, negative: true);
        var backs = toSell.Where(t => t.spent > 0).Select(t => (t.what, back: (t.spent + t.net) / (double)t.spent)).OrderByDescending(x => x.back).ToList();
        double shelfBack = backs.Count == 0 ? 0 : backs[0].back;
        bool sixthOk = backs.Count > 0 && shelfBack <= 1.0 / 6 + 1e-9;
        if (!sixthOk) bad++;
        Console.WriteLine($"  {(sixthOk ? "ok  " : "FAIL")} 5. at most a sixth of a restored slot's price comes back through Moth's Sell tab: best {shelfBack:P2} (limit {1.0 / 6:P2}); "
                          + $"the three highest: {string.Join("; ", backs.Take(3).Select(x => $"{x.what}: {x.back:P2}"))}; the cheapest slab costs {restoredItems.Min(x => AlleyEconomy.ShelfPrice(x.value)):N0} "
                          + $"(ShelfPrice rounds up to 500 since NL-2: it was 18% back at 2,500)");

        // The Invitational (loops 6-8): all eleven builds ready in a copy of the data, No Limits cleared, the No Limits Pack over wave 1's decks.
        var inv = AlleyTest.ReadyInvitational(a, cards);
        var invPacks = ContrabandPacks.NoLimits(inv.Data, inv.Ctx);
        var contrabandPool = ctx.Pool(9999);
        var tradeable = contrabandPool.OrderByDescending(c => CardTrader.Cost(c, printings, cards)).ThenBy(c => c).First();
        void TradeTokens(Profile p) { while (p.Tokens >= CardTrader.Cost(tradeable, printings, cards) && CardTrader.Trade(tradeable, p, 2026, printings, cards)) { } }
        void PlayAlley(Profile p, string kind, int loseIn)
        {
            AlleyTest.ClearNoLimits(p);
            if (kind == Brackets.InvitationalKind) { if (Brackets.EnterBlocked(p, inv.Data, kind) == null) AlleyTest.PlayInvitational(p, inv, library, packs, invPacks, loseIn); return; }
            var b = Brackets.EnterAlley(p, inv.Data, kind, inv.Ctx);
            if (b == null) return;
            while (b.YourMatch() >= 0) { Brackets.StartYours(b); Brackets.RecordYours(p, library, b, b.RoundOf(b.YourMatch()) == loseIn ? 1 : 0, loaner: false); }
            Brackets.SkipToEnd(b);
            Brackets.Finish(p, library, b, null, packs);
        }
        var placements = new List<(string name, int loseIn)> { ("Champion", -1), ("Finalist (the Bo3 final lost 0-2)", 3), ("Out in the semi-finals", 2), ("Out in the quarter-finals", 1), ("Out in the round of 16", 0) };

        // Loop 6: the Invitational at every placement: its No Limits Packs opened, the title's tokens traded, everything sold. Every placement under 100%; a title at most 50%.
        var invTries = placements.Select(x => Measure(x.name, p => { PlayAlley(p, Brackets.InvitationalKind, x.loseIn); TradeTokens(p); })).ToList();
        Verdict("6. the Invitational at every placement: packs opened, tokens traded, sell everything: every placement under 100%", invTries, negative: true);
        var title = invTries[0];
        double titleBack = (title.spent + title.net) / (double)title.spent;
        bool titleOk = title.spent == AlleyEconomy.InvitationalEntry && titleBack <= 0.5 + 1e-9 && invTries.All(t => t.spent > 0 && t.net < 0);
        if (!titleOk) bad++;
        Console.WriteLine($"  {(titleOk ? "ok  " : "FAIL")} 6. a title gets at most 50% of the {AlleyEconomy.InvitationalEntry:N0} entry back: {titleBack:P1} ({title.net:+#,0;-#,0} DP; the packs and tokens sell for 0); "
                          + $"placements: {string.Join(", ", invTries.Select(t => $"{t.what} {(t.spent + t.net) / (double)t.spent:P1}"))}");

        // Loop 7: the Invitational's packs graded (to 10s, crack, resubmit) and sold: below 0.
        Verdict("7. the Invitational at every placement, its packs graded (to 10s, crack and resubmit), sell: below 0", placements.Select(x => Measure(x.name, p =>
        {
            int before = p.Collection.NextId;
            PlayAlley(p, Brackets.InvitationalKind, x.loseIn);
            GradeAll(p, p.Collection.All.Where(c => c.Source == CopySource.Prize && c.Id >= before));
        })), negative: true);

        // Loop 8: all three alley brackets won in turn, their prizes sold: under 100%.
        var all3 = Measure("the Alley Brawl, the Midnight Open and the Invitational, all won", p =>
        {
            PlayAlley(p, Brackets.BrawlKind, -1);
            PlayAlley(p, Brackets.MidnightKind, -1);
            PlayAlley(p, Brackets.InvitationalKind, -1);
            TradeTokens(p);
        });
        double all3Back = all3.spent == 0 ? 1 : (all3.spent + all3.net) / (double)all3.spent;
        bool all3Ok = all3.spent == AlleyEconomy.BrawlEntry + AlleyEconomy.OpenEntry + AlleyEconomy.InvitationalEntry && all3.net < 0;
        if (!all3Ok) bad++;
        ratios.Add(("8. all three alley brackets won in turn, prizes sold", all3.what, all3.spent, all3.net));
        Console.WriteLine($"  {(all3Ok ? "ok  " : "FAIL")} 8. all three alley brackets won in turn, prizes sold: {all3Back:P1} of the {all3.spent:N0} in entries back (net {all3.net:+#,0;-#,0;0} DP; under 100%)");

        // Loop 9: New Game+ after a No Limits clear: the clear token doesn't re-pay; binders and nights carry over. (The title tokens and the posters are the Invitational's.)
        int tokensAgain = -1, bindersKept = -1, nightsKept = -1, duplicateStakes = 0;
        Verdict("9. a No Limits clear -> New Game+ -> clear again -> sell everything: exactly 0, the clear token pays once", new[] { Measure("two clears", p =>
        {
            for (int d = 0; d < BackAlley.Matches; d++) AlleyTest.Duel(p, s, nlt, 0);
            int clearedNight = p.AlleyTables[data.Tables[nlt].Id].Night;
            var binders = p.Binders.Keys.Where(k => k.StartsWith("nl:")).OrderBy(k => k).ToList();
            p.NewGamePlus();
            bindersKept = binders.Count(k => p.Binders.ContainsKey(k));
            nightsKept = p.AlleyTables[data.Tables[nlt].Id].Night == clearedNight && p.AlleyTables[data.Tables[nlt].Id].Cleared ? 1 : 0;
            int tokens = p.Tokens;
            for (int d = 0; d < BackAlley.Matches; d++) AlleyTest.Duel(p, s, nlt, 0);
            tokensAgain = p.Tokens - tokens;
            duplicateStakes = p.Binders.Where(b => b.Key.StartsWith("nl:")).Sum(b => b.Value.Cards.GroupBy(c => c.Code).Count(g => g.Count() > 1));
        }) }, zero: true);
        bool ngOk = tokensAgain == 0 && bindersKept == builds.Count && nightsKept == 1 && duplicateStakes == 0;
        if (!ngOk) bad++;
        Console.WriteLine($"  {(ngOk ? "ok  " : "FAIL")} 9. after New Game+ the second clear pays {tokensAgain} tokens, the {bindersKept} of {builds.Count} nl: binders and the cleared night carry over ({nightsKept}), no binder holds a card twice ({duplicateStakes})");

        // Loop 10: a won lead that is a 4th copy displacing a bought Raw copy pays 0 DP and no refund.* line (the ante path drops Added.Dp).
        var fourth = new List<(string, int, int)>();
        int refundLines = 0;
        for (int seat = 1; seat <= BackAlley.Matches; seat++)
            foreach (var grade in new[] { 1, 9 })
            {
                int k = seat;
                void Setup(Profile p, bool win)
                {
                    ToSeat(p, k);
                    var st = Seat(p) is AlleyOpponent o ? Ante.Next(p, o.Binder, ctx) : null;
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
                    if (win) AlleyTest.Duel(p, s, nlt, 0);
                }
                int refunds = 0;
                var with = Measure("", p => { Setup(p, true); refunds += p.DpEarned.Where(e => e.Key.StartsWith("refund.")).Sum(e => e.Value); });
                var without = Measure("", p => Setup(p, false));
                refundLines += refunds;
                var probe = Fresh();
                ToSeat(probe, k);
                var stake = Seat(probe) is AlleyOpponent so ? Ante.Next(probe, so.Binder, ctx) : null;
                fourth.Add(($"seat {k}: {(stake != null ? Name(stake.Code) : "-")} over 3 grade-{grade} copies", with.spent, with.net - without.net));
            }
        Verdict("10. a won lead that is a 4th copy displacing a bought Raw copy, sell everything (net of the same 3 copies sold without it, which is the displaced copy's sale): never DP-positive", fourth);
        Console.WriteLine($"  {(refundLines == 0 ? "ok  " : "FAIL")} 10. no refund.* line: {refundLines} DP of refund.* earned across the {fourth.Count} tries");
        if (refundLines != 0) bad++;

        // The separate exhaustive search: 12 actions over 1-4 steps (22,620 sequences).
        var wonSlab = new Func<Profile, CardCopy?>(p => p.Collection.All.Where(c => c.Source == CopySource.Ante && c.IsSlab && !c.AtDesk && p.Collection.Value(c) >= AlleyEconomy.OrvelMinCard)
            .OrderByDescending(c => p.Collection.Value(c)).ThenBy(c => c.Id).FirstOrDefault(c => BackAlley.Lineup(p, data, orvel, ctx).Any(o => Ante.Next(p, o.Binder, ctx, c) != null)));
        var actions = new List<(string name, Action<Profile> act)>
        {
            ("nl win", p => AlleyTest.Duel(p, s, nlt, 0)),
            ("nl loss", p => AlleyTest.Duel(p, s, nlt, 1)),
            ("nl draw", p => AlleyTest.Duel(p, s, nlt, 2, AlleyEconomy.AlleyTurns + 1)),
            ("nl first-turn loss", p => AlleyTest.Duel(p, s, nlt, 1, AlleyEconomy.NoLimitsGuardTurns, 1)),
            ("nl quit", p => { Hold(p); Ante.SettleAbandoned(p, ctx); }),
            ("next night", p =>
            {
                var st = p.AlleyTables[data.Tables[nlt].Id];
                st.Night++;
                st.WonMask = 0;
                BackAlley.Lineup(p, data, nlt, ctx);
            }),
            ("restored shelf buy", p =>
            {
                if (MothsVan.Shelf(p, packs, ctx).FirstOrDefault(l => l.Restored && !l.Sold && MothsVan.Blocked(p, l) == null) is Listing l) MothsVan.Buy(p, l, shelf: true);
            }),
            ("orvel stake of a nl slab (loss)", p => { if (wonSlab(p) is CardCopy c) AlleyTest.OrvelDuel(p, s, c, 1); }),
            ("buy back", p => { if (p.Rail.FirstOrDefault() is RailEntry e) Ante.BuyBack(p, e); }),
            ("grade best", p => { var c = p.Collection.All.Where(x => !x.AtDesk).OrderByDescending(x => p.Collection.Value(x)).FirstOrDefault(); if (c != null) GradingDesk.Submit(p, c, true, none); }),
            ("crack+regrade", p => { var c = p.Collection.All.FirstOrDefault(x => x.IsSlab); if (c != null) { GradingDesk.Crack(p, c); GradingDesk.Submit(p, c, true, none); } }),
            ("sell best", p => { var c = p.Collection.All.Where(x => x.Paid > 0 && !x.AtDesk).OrderByDescending(x => p.Collection.SellPrice(x)).FirstOrDefault(); if (c != null) SellDesk.Sell(p, c, none); }),
        };
        var seqs = new List<(string, int, int)>();
        foreach (var len in new[] { 1, 2, 3, 4 })
            foreach (var seq in Sequences(actions.Count, len))
            {
                seqs.Add(Measure(string.Join(" > ", seq.Select(i => actions[i].name)), p => { foreach (var i in seq) actions[i].act(p); }));
            }
        Verdict($"exhaustive: every sequence of 1-4 of {actions.Count} No Limits actions, then sell all ({seqs.Count:N0} sequences, {seqs.Count(x => x.Item3 > 0)} DP-positive)", seqs);
        bool countOk = seqs.Count == 22620;
        if (!countOk) bad++;
        Console.WriteLine($"  {(countOk ? "ok  " : "FAIL")} the search is {seqs.Count:N0} sequences (12 + 144 + 1,728 + 20,736 = 22,620)");
        var losing = new HashSet<string> { "nl loss", "nl quit" };
        var withLoss = seqs.Where(x => x.Item1.Split(" > ").Any(losing.Contains)).ToList();
        bool lossesCost = withLoss.All(x => x.Item3 < 0);
        if (!lossesCost) bad++;
        Console.WriteLine($"  {(lossesCost ? "ok  " : "FAIL")} the {withLoss.Count:N0} sequences with a lost stake or a quit all lose DP; {seqs.Count(x => x.Item3 == 0):N0} break even (wins, draws, the guard, trades, nights)"
                          + (lossesCost ? "" : $"; not: {string.Join("; ", withLoss.Where(x => x.Item3 >= 0).Take(5).Select(x => $"{x.Item1} ({x.Item3:+#,0;-#,0;0})"))}"));
        var nearest = seqs.Where(x => x.Item3 < 0).OrderByDescending(x => x.Item3).First();
        Console.WriteLine($"  the closest losing sequence: {nearest.Item1}: net {nearest.Item3:+#,0;-#,0} on {nearest.Item2:N0} spent");
        Console.WriteLine("  Best ratio per loop (DP back / DP in; ECONOMY: the same shapes at Orvel's lose -8,100 on 9,000; the shelf at most a sixth back):");
        foreach (var (loop, what, spent, net) in ratios)
            Console.WriteLine($"    {(spent > 0 ? (spent + net) / (double)spent : 0),6:P1}  {loop}  ({what}, net {net:+#,0;-#,0;0} on {spent:N0})");
        Console.WriteLine($"  (No Limits loops: {timer.Elapsed.TotalSeconds:0.0} s)");
        return bad;
    }
}
