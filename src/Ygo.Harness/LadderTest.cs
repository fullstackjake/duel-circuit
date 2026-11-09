using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--ladder-test`: the Champion's Ladder (docs/dev/CHAMPIONS-LADDER-SPEC-2026-11-06.md section 9, checks 1-11), then its
/// arbitrage loops (<see cref="Arbitrage.LadderLoops"/>). Checks 1-10 test <see cref="Ladder"/> and <see cref="LadderState"/>
/// directly (chunk 1); check 11, the story's scenes, fails until the writer's scenes land (W2), as intended.
/// </summary>
public static class LadderTest
{
    /// <summary>From Program's flag switch: loads the cards itself.</summary>
    public static int Run(string root) => Run(SqliteCardDatabase.LoadFolder(Path.Combine(root, "vendor", "BabelCDB")), root);

    public static int Run(ICardDataProvider cards, string root)
    {
        int failures = 0, passed = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (ok) passed++; else failures++;
        }

        var printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities")).ToList();
        var skillsBefore = roster.ToDictionary(p => p.Id, p => (p.Skill, p.Aggression, p.Caution, p.Patience, p.Spending));
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var library = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), roster, circuits);
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        var ladderPath = Path.Combine(root, "campaign", Ladder.FileName);
        var ladder = Ladder.Load(ladderPath, roster, deckFiles.ContainsKey);
        var ratings = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName));
        const int Top = 50_000_000; // past level 99: no level-up DP in the pay checks

        Profile Veteran(bool ilseWin = true, int exp = Top)
        {
            var p = new Profile { Exp = exp };
            for (int r = 0; r < library.Rounds.Count; r++) p.LibraryWins[library.FinalKey(r)] = 1;
            if (ilseWin && ladder.Opener != null) p.Wins[ladder.Opener.Id] = 1;
            return p;
        }
        // Climbs from the bottom to <rungs> rungs held (Ilse's sitting won in full), with no visitors.
        void ClimbTo(Profile p, LadderState s, int rungs)
        {
            for (int guard = 0; guard < 100 && ladder.Climbed(s) < rungs && ladder.Next(p, s, library) is LadderSeat next; guard++)
                ladder.Record(p, s, library, next, 0);
        }
        LadderSeat Rung(int n) => ladder.Rungs[n - 1];

        // ---- 1. The data ----
        Console.WriteLine($"\nThe Champion's Ladder ({ladderPath}):");
        foreach (var problem in ladder.Problems) Console.WriteLine($"  problem: {problem}");
        var topFile = Path.Combine(root, "personalities", "ladder-top.profile");
        bool topProfile = File.Exists(topFile);
        if (!topProfile) Console.WriteLine($"  MISSING {topFile} (the Narrative Designer's W1): rung 10 can't load until it lands");
        Check($"ladder.txt parses with no problems ({ladder.Problems.Count})", ladder.Problems.Count == 0);
        Check($"10 rungs and 4 visitors ({ladder.Rungs.Count} and {ladder.Visitors.Count})", ladder.Rungs.Count == 10 && ladder.Visitors.Count == 4);
        var allSeats = ladder.Rungs.Concat(ladder.Visitors).ToList();
        var missingDecks = allSeats.SelectMany(s => s.Decks).Where(d => !deckFiles.ContainsKey(d)).Distinct().ToList();
        Check($"every seat's deck file exists{(missingDecks.Count > 0 ? $" (missing: {string.Join(", ", missingDecks)})" : "")}", missingDecks.Count == 0);
        var known = roster.Select(p => p.Id).ToHashSet();
        Check($"every character is known, ladder-top included{(topProfile ? "" : " (personalities/ladder-top.profile is missing)")}",
            allSeats.All(s => known.Contains(s.Character.Id)) && ladder.Rungs.Count == 10 && ladder.Rungs[^1].Character.Id == "ladder-top");
        Check($"it opens with {ladder.Opener?.Name ?? "nobody"} (opens: {ladder.OpenerId}), the top pays {ladder.Prize:N0} DP, the visitors wait on the {ladder.VisitorsKind} title",
            ladder.Opener?.Id == "ilse" && ladder.Prize == 5000 && ladder.VisitorsKind == Brackets.InvitationalKind);
        var ilseRung = ladder.Rungs.FirstOrDefault(s => s.Character.Id == "ilse");
        Check($"Duchess Ilse holds rung 9 with 4 decks in her guest order ({(ilseRung == null ? "absent" : $"rung {ilseRung.Rung}: {string.Join(", ", ilseRung.Decks)}")})",
            ilseRung != null && ilseRung.Rung == 9 && ilseRung.Decks.SequenceEqual(new[] { "synchron", "zombies", "gadgets", "modern" })
            && ladder.Rungs.Count(r => r.IsSitting) == 1);
        Check($"the visitors are the Brawl's out-of-towners on their 2024 base decks ({string.Join(", ", ladder.Visitors.Select(v => $"{v.Character.Id} {v.Deck}"))})",
            ladder.Visitors.Select(v => (v.Character.Id, v.Deck)).SequenceEqual(new[] { ("rue", "yubel"), ("sasha", "tenpai-dragon"), ("augie", "k9-vanquish-soul"), ("junia", "snake-eye") }));
        var lists = Banlist.LoadFolders(new[] { Path.Combine(root, "vendor", "LFLists"), Path.Combine(root, "lflists") });
        var rulesName = Ladder.RulesBanlist(circuits);
        var rulesList = lists.FirstOrDefault(l => l.Name == rulesName);
        var topSeat = ladder.Rungs.Count > 0 ? ladder.Rungs[^1] : null;
        if (topSeat != null && rulesList != null && deckFiles.TryGetValue(topSeat.Deck, out var topDeckFile))
        {
            var deck = Deck.LoadYdk(topDeckFile);
            var problems = DeckRules.Validate(deck, rulesList, cards);
            var late = DeckRules.Releases(deck, rulesList, DeckRules.FormatEnd(rulesList, lists), printings, cards);
            foreach (var pr in problems.Concat(late).Take(5)) Console.WriteLine($"  {topSeat.Deck}: {pr.Message}");
            Check($"rung 10's {topSeat.Deck} has no problems under {circuits.All[Ladder.RulesCircuit(circuits)].Name}'s list, {rulesName} ({problems.Count} problems, {late.Count} cards not out yet)",
                problems.Count == 0 && late.Count == 0);
        }
        else Check($"rung 10's deck can be checked under the last circuit's list ({rulesName}: {(rulesList == null ? "not installed" : "installed")})", false);
        var top = roster.FirstOrDefault(p => p.Id == "ladder-top");
        var ritual = roster.FirstOrDefault(p => p.Id == "chaos-ritual");
        if (top == null) Check($"ladder-top's profile: MISSING ({topFile}; the Narrative Designer's W1)", false);
        else
        {
            bool knobs = ritual != null && top.Aggression == ritual.Aggression && top.Caution == ritual.Caution && top.Patience == ritual.Patience && top.Spending == ritual.Spending;
            Check($"ladder-top ({top.DisplayName}): skill {top.Skill}, ladder {top.Ladder}, decks {string.Join(", ", top.Decks)}, signature \"{top.Signature}\", "
                  + $"knobs {top.Aggression}/{top.Caution}/{top.Patience}/{top.Spending} {(knobs ? "equal" : "differ from")} chaos-ritual's",
                top.Skill == 1.0 && top.Ladder == 0 && top.Signature.Length == 0 && top.Decks.SequenceEqual(new[] { "chaos-ritual" }) && knobs
                && top.Name.Length > 0 && top.Name != "ladder-top" && top.Intro.Length > 0 && top.WinLine.Length > 0 && top.LoseLine.Length > 0 && top.Look.Length > 0);
        }
        Console.WriteLine("  Ratings (information only; tuning is parked):");
        foreach (var s in ladder.Rungs)
            Console.WriteLine($"    rung {s.Rung,2}  {s.Character.Name,-24} {string.Join(", ", s.Decks.Select(d => $"{d} ({DeckRatings.Of(ratings, d):0})"))}");
        foreach (var s in ladder.Visitors)
            Console.WriteLine($"    visitor   {s.Character.Name,-24} {s.Deck} ({DeckRatings.Of(ratings, s.Deck):0})");
        if (ladder.Rungs.Count < 10 || ilseRung == null || ladder.Opener == null || library.Rounds.Count == 0)
        {
            Console.WriteLine($"\nThe rest needs the full ladder, Ilse's rung and the Every Era final: stopping. {passed} passed, {failures} failed.");
            return 1;
        }

        // ---- 2. Opening ----
        Console.WriteLine("\nOpening:");
        var vet = Veteran(ilseWin: false);
        Check($"a veteran save with the final won and no win over Ilse: shown, locked (\"Locked: {ladder.LockReason(vet, library)}\")",
            ladder.IsShown(vet, library) && !ladder.IsOpen(vet, library) && ladder.Next(vet, new LadderState(), library) == null
            && ladder.LockReason(vet, library) == "Sit across from Duchess Ilse first (Campaign > Guests)");
        var fresh = new Profile();
        fresh.Wins["ilse"] = 1;
        Check("before the final it isn't shown, even with a win over Ilse", !ladder.IsShown(fresh, library) && !ladder.IsOpen(fresh, library));
        vet.Wins["ilse"] = 1;
        bool opened = ladder.IsOpen(vet, library) && ladder.LockReason(vet, library) == "";
        vet.NewGamePlus();
        Check("one win over Ilse opens it, and it stays open after New Game+", opened && ladder.IsOpen(vet, library));
        Check("with no Library loaded it stays closed", !ladder.IsOpen(vet, null) && ladder.Next(vet, new LadderState(), null) == null);

        // ---- 3. Climbing ----
        Console.WriteLine("\nClimbing:");
        var cp = Veteran();
        var cs = new LadderState();
        var n0 = ladder.Next(cp, cs, library);
        Check($"from fresh, the next opponent is rung 1 ({n0?.Character.Name}, {n0?.Deck})", n0 == Rung(1) && ladder.Climbed(cs) == 0);
        ladder.Record(cp, cs, library, Rung(1), 0);
        Check($"a win takes rung 1; next is rung 2 ({ladder.Next(cp, cs, library)?.Character.Name})", ladder.Climbed(cs) == 1 && ladder.Next(cp, cs, library) == Rung(2));
        ladder.Record(cp, cs, library, Rung(2), 1);
        ladder.Record(cp, cs, library, Rung(2), 2);
        Check("a loss and a draw keep the rung", ladder.Climbed(cs) == 1 && ladder.Next(cp, cs, library) == Rung(2));
        int dpSkip = cp.Dp;
        Check("a win against a rung above the next one, or a rematch before the top, records nothing",
            ladder.Record(cp, cs, library, Rung(3), 0) == null && ladder.Record(cp, cs, library, Rung(1), 0) == null && cp.Dp == dpSkip && ladder.Climbed(cs) == 1);

        // ---- 4. Ilse's sitting ----
        Console.WriteLine("\nIlse's sitting:");
        var sp = Veteran();
        var ss = new LadderState();
        ClimbTo(sp, ss, 8);
        var ilse = ladder.Next(sp, ss, library);
        var decksSeen = new List<string>();
        for (int k = 0; k < 3; k++) { decksSeen.Add(ladder.DeckFor(ilse!, ss)); ladder.Record(sp, ss, library, ilse!, 0); }
        decksSeen.Add(ladder.DeckFor(ilse!, ss));
        var pips = ladder.SittingProgress(ilse!, ss);
        var lost = ladder.Record(sp, ss, library, ilse!, 1);
        Check($"at 8 climbed Ilse is next; three wins bring {string.Join(" -> ", decksSeen)} ({pips.won} of {pips.of}); a loss resets it to {ladder.DeckFor(ilse!, ss)}",
            ilse == ilseRung && decksSeen.SequenceEqual(ilseRung.Decks) && pips == (3, 4) && lost!.SittingReset && ss.Sitting == 0
            && ladder.DeckFor(ilse!, ss) == "synchron" && ladder.Climbed(ss) == 8);
        ladder.Record(sp, ss, library, ilse!, 0);
        ladder.Record(sp, ss, library, ilse!, 2);
        Check("a draw ends the sitting too", ss.Sitting == 0 && ladder.Climbed(ss) == 8);
        LadderResult? fourth = null;
        for (int k = 0; k < 4; k++) fourth = ladder.Record(sp, ss, library, ilse!, 0);
        var afterIlse = ladder.Next(sp, ss, library);
        Check($"four wins take rung 9, the sitting is back at 0, and next is rung 10 ({afterIlse?.Character.Name} on {afterIlse?.Deck})",
            fourth!.RungTaken == 9 && ss.Sitting == 0 && ladder.Climbed(ss) == 9 && afterIlse == Rung(10) && afterIlse!.Deck == "chaos-ritual"
            && ladder.SittingProgress(ilse!, ss) == (4, 4));

        // ---- 5. Full strength ----
        Console.WriteLine("\nFull strength:");
        var opponents = allSeats.Select(Ladder.Opponent).ToList();
        Check($"every seat's opponent plays at skill 1.0 ({string.Join(", ", allSeats.Select(s => $"{s.Character.Id} {skillsBefore[s.Character.Id].Skill}"))} in their profiles)",
            opponents.All(o => o.Skill == 1.0));
        Check("the roster's personalities are unchanged",
            roster.All(p => skillsBefore[p.Id] == (p.Skill, p.Aggression, p.Caution, p.Patience, p.Spending)) && roster.First(p => p.Id == "rhea").Skill < 1.0);

        // ---- 6. Pay ----
        Console.WriteLine("\nPay:");
        var pp = Veteran();
        var ps = new LadderState();
        int Earned(string source) => pp.DpEarned.GetValueOrDefault(source);
        bool Balanced() => Ledger.Net(pp) == pp.Dp - Profile.StartingDp;
        string L = Ladder.LedgerKind, LLoss = Ledger.Loss(Ladder.LedgerKind), LPrize = Ledger.Prize(Ladder.LedgerKind);
        int dp0 = pp.Dp;
        var first = ladder.Record(pp, ps, library, Rung(1), 0)!;
        Check($"a first climb pays {first.Reward.Dp} DP under \"{L}\" ({first.Reward.Summary})", first.Reward.Dp == 800 && Earned(L) == 800 && pp.Dp - dp0 == 800 && first.Reward.Summary.StartsWith("Rung 1 +800"));
        ladder.Record(pp, ps, library, Rung(2), 1);
        ladder.Record(pp, ps, library, Rung(2), 2);
        Check($"a loss pays 50 and a draw 100 under \"{LLoss}\" ({Earned(LLoss)})", Earned(LLoss) == 150);
        ClimbTo(pp, ps, 8);
        int sit0 = Earned(L);
        ladder.Record(pp, ps, library, ilseRung, 0);
        Check($"a win inside the sitting pays 150 ({Earned(L) - sit0})", Earned(L) - sit0 == 150);
        ClimbTo(pp, ps, 9);
        bool posterBefore = Ladder.MakePoster(_ => ps).Earned(circuits, pp);
        int tokens0 = pp.Tokens, prize0 = Earned(LPrize);
        var topWin = ladder.Record(pp, ps, library, Rung(10), 0)!;
        bool posterAfter = Ladder.MakePoster(_ => ps).Earned(circuits, pp);
        Check($"the top pays 800 + the 5,000 prize under \"{LPrize}\" and 3 Card Tokens ({topWin.Reward.Summary})",
            topWin.TopReached && topWin.Reward.Dp == 5800 && Earned(LPrize) - prize0 == 5000 && pp.Tokens - tokens0 == 3 && topWin.Reward.Tokens == 3);
        Check($"the poster \"{Ladder.PosterId}\" (card {Ladder.PosterCard}, {cards.Get(Ladder.PosterCard)?.Name}) is earned at the top, not before; null state reads as unearned",
            !posterBefore && posterAfter && !Ladder.MakePoster(_ => null).Earned(circuits, pp) && cards.Get(Ladder.PosterCard) != null);
        int re0 = Earned(L);
        var rematch = ladder.Record(pp, ps, library, Rung(3), 0)!;
        var rematchIlse = ladder.Record(pp, ps, library, ilseRung, 0)!;
        Check($"after the top a rematch pays 150 ({rematch.Reward.Summary}); Ilse's rung plays one duel, going round her decks", rematch.Rematch && Earned(L) - re0 == 300
            && rematchIlse.Rematch && ps.Sitting == 0 && ladder.Climbed(ps) == 10);
        int prizeBefore = Earned(LPrize), tokensBefore = pp.Tokens, firstsAfter = 0;
        pp.NewGamePlus();
        foreach (var seat in ladder.Rungs) { var r = ladder.Record(pp, ps, library, seat, 0); if (r == null || r.Reward.FirstWin || r.RungTaken > 0 || r.TopReached) firstsAfter++; }
        Check($"a replay after New Game+ pays no first climbs ({firstsAfter}), no prize, no tokens", firstsAfter == 0 && Earned(LPrize) == prizeBefore && pp.Tokens == tokensBefore);
        Check($"the ledger lines add up to the DP change (net {Ledger.Net(pp):N0} = {pp.Dp - Profile.StartingDp:N0}; ladder lines {Earned(L) + Earned(LLoss) + Earned(LPrize):N0}, nothing spent)",
            Balanced() && pp.DpSpent.Count == 0 && Earned(L) + Earned(LLoss) + Earned(LPrize) == pp.Dp - Profile.StartingDp);
        // Level-ups: a first climb's pay DP, a rematch's and a sitting win's only tokens.
        var lp = Veteran(exp: 0);
        var ls = new LadderState();
        ladder.Record(lp, ls, library, Rung(1), 0);
        int lvFirst = lp.DpEarned.GetValueOrDefault(Ledger.LevelUp);
        ClimbTo(lp, ls, 10);
        int lv0 = lp.DpEarned.GetValueOrDefault(Ledger.LevelUp), levelBefore = lp.Level;
        for (int d = 0; d < 60; d++) ladder.Record(lp, ls, library, ladder.Rungs[d % 10], 0);
        Check($"level-ups pay DP on a first climb ({lvFirst} DP from rung 1) but not on rematches (level {levelBefore} -> {lp.Level}, {lp.DpEarned.GetValueOrDefault(Ledger.LevelUp) - lv0} level DP)",
            lvFirst > 0 && lp.Level > levelBefore && lp.DpEarned.GetValueOrDefault(Ledger.LevelUp) == lv0);
        Check("wins count against the holder (bonds and beat: gifts work)", pp.WinsAgainst("rhea") >= 2 && pp.WinsAgainst("ladder-top") >= 2);

        // ---- 7. Visitors ----
        Console.WriteLine("\nVisitors:");
        var vp = Veteran();
        var vs = new LadderState();
        ClimbTo(vp, vs, 3);
        int bracketsBefore = vp.AlleyBrackets.Count;
        Check("without the Invitational title they don't come (and asking adds no bracket record)",
            ladder.ActiveVisitor(vp, vs) == null && ladder.Next(vp, vs, library) == Rung(4) && vp.AlleyBrackets.Count == bracketsBefore);
        vp.AlleyBrackets[Brackets.InvitationalKind] = new LocalsRecord { Entries = 1, Titles = 1, Best = 0 };
        var rue = ladder.Next(vp, vs, library);
        Check($"with the title, next is Rue even mid-climb ({rue?.Character.Name} on {rue?.Deck}, above rung {ladder.Climbed(vs)})", rue?.Character.Id == "rue" && rue.IsVisitor);
        var beatRue = ladder.Record(vp, vs, library, rue!, 0)!;
        Check($"beating Rue pays 800, keeps your rung ({ladder.Climbed(vs)}) and brings Sasha ({ladder.Next(vp, vs, library)?.Character.Name})",
            beatRue.VisitorBeaten && beatRue.Reward.Dp == 800 && ladder.Climbed(vs) == 3 && ladder.Next(vp, vs, library)?.Character.Id == "sasha");
        var wp = Veteran();
        var ws = new LadderState();
        ClimbTo(wp, ws, 8);
        ladder.Record(wp, ws, library, ilseRung, 0);
        wp.AlleyBrackets[Brackets.InvitationalKind] = new LocalsRecord { Entries = 1, Titles = 1, Best = 0 };
        Check("no visitor arrives during a sitting", ladder.Next(wp, ws, library) == ilseRung && ws.Sitting == 1);
        ladder.Record(wp, ws, library, ilseRung, 1);
        Check($"once the sitting ends, the visitor arrives ({ladder.Next(wp, ws, library)?.Character.Name})", ladder.Next(wp, ws, library)?.Character.Id == "rue");
        var tp = Veteran();
        var ts = new LadderState();
        ClimbTo(tp, ts, 10);
        tp.AlleyBrackets[Brackets.InvitationalKind] = new LocalsRecord { Entries = 1, Titles = 1, Best = 0 };
        var atTop = ladder.Next(tp, ts, library);
        bool rematchWhileVisiting = ladder.CanChallenge(tp, ts, library, Rung(4));
        foreach (var v in ladder.Visitors) ladder.Record(tp, ts, library, v, 0);
        Check($"at the top a visitor still shows up ({atTop?.Character.Name}), rematches stay open; with all four beaten next is nobody",
            atTop?.Character.Id == "rue" && rematchWhileVisiting && ladder.Next(tp, ts, library) == null && ladder.HoldsTop(ts));
        var fp = Veteran();
        ladder.VisitorsForced = true;
        bool forced = ladder.Next(fp, new LadderState(), library)?.Character.Id == "rue";
        ladder.VisitorsForced = false;
        Check("the check-only override brings them without the title", forced && ladder.Next(fp, new LadderState(), library) == Rung(1));

        // ---- 8. Events ----
        Console.WriteLine("\nEvents:");
        var ep = Veteran();
        var es = new LadderState();
        bool due1 = ladder.OpenDue(ep, library);
        ep.Seen.Add(Ladder.OpensScene);
        Check($"\"{Ladder.OpenEvent}\" comes once (due, then not after {Ladder.OpensScene} is seen); never while closed",
            due1 && !ladder.OpenDue(ep, library) && !ladder.OpenDue(Veteran(ilseWin: false), library));
        var seat1 = Ladder.SeatEvents(es, Rung(1));
        var e1 = ladder.Record(ep, es, library, Rung(1), 0)!.Events;
        ClimbTo(ep, es, 9);
        var seatTop = Ladder.SeatEvents(es, Rung(10));
        var eTop = ladder.Record(ep, es, library, Rung(10), 0)!.Events;
        var eRematch = ladder.Record(ep, es, library, Rung(1), 0)!.Events;
        var eTopAgain = ladder.Record(ep, es, library, Rung(10), 0)!.Events;
        Check($"a first climb raises {string.Join(", ", e1.Where(e => !e.StartsWith("level:")))}; a rematch raises none of them ({string.Join(", ", eRematch)})",
            e1.Contains("ladderrung:1") && e1.Contains("ladderbeat:rhea") && !eRematch.Any(e => e.StartsWith("ladder")));
        Check($"the top raises {string.Join(", ", eTop.Where(e => !e.StartsWith("level:")))} in that order, once",
            eTop.Where(e => e.StartsWith("ladder")).SequenceEqual(new[] { "ladderrung:10", "ladderbeat:ladder-top", Ladder.TopEvent }) && !eTopAgain.Contains(Ladder.TopEvent));
        var ip = Veteran();
        var iss = new LadderState();
        ClimbTo(ip, iss, 8);
        var sittingEvents = new List<string>();
        for (int k = 0; k < 3; k++) sittingEvents.AddRange(ladder.Record(ip, iss, library, ilseRung, 0)!.Events);
        var ilseFalls = ladder.Record(ip, iss, library, ilseRung, 0)!.Events;
        Check($"Ilse's sitting wins raise nothing until her rung falls ({string.Join(", ", ilseFalls.Where(e => e.StartsWith("ladder")))})",
            !sittingEvents.Any(e => e.StartsWith("ladder")) && ilseFalls.Contains("ladderbeat:ilse") && ilseFalls.Contains("ladderrung:9"));
        // ladderseat: raised before a seat's first duel; the story's Seen keeps its scene once, and a taken seat never raises it.
        var stub = Story.Parse("scene: holder\nwhen: ladderseat:ladder-top\n-: X.\n");
        var sp1 = new Profile();
        var firstPress = stub.Pending(sp1, seatTop);
        foreach (var sc in firstPress) Story.MarkSeen(sp1, sc);
        Check($"\"ladderseat:\" is pending before the first duel against a holder ({string.Join(", ", seat1)}; {string.Join(", ", seatTop)}), once, and never once the seat is taken",
            seat1.SequenceEqual(new[] { "ladderseat:rhea" }) && firstPress.Count == 1 && stub.Pending(sp1, seatTop).Count == 0
            && Ladder.SeatEvents(es, Rung(10)).Count == 0 && Ladder.SeatEvents(es, Rung(1)).Count == 0);
        Check("the five triggers are the ladder's (and nothing else is)",
            new[] { "ladder", "laddertop", "ladderseat:ilse", "ladderseat:junia", "ladderbeat:ladder-top", "ladderrung:5", "ladderrung:10" }.All(ladder.IsTrigger)
            && !new[] { "ladderseat:nobody", "ladderbeat:vera", "ladderrung:0", "ladderrung:11", "ladders", "guest:ilse" }.Any(ladder.IsTrigger));

        // ---- 9. Saves ----
        Console.WriteLine("\nSaves:");
        var saveState = new LadderState();
        saveState.Wins["rung.rhea"] = 3;
        saveState.Wins["visitor.rue"] = 1;
        saveState.Wins[Ladder.TopKey] = 1;
        saveState.Sitting = 2;
        var sb = new System.Text.StringBuilder();
        saveState.Write(sb);
        var back = LadderState.Read(sb.ToString().Split('\n'));
        Check($"ladwin/ladsit round-trip ({sb.ToString().Trim().Replace(Environment.NewLine, " | ")})",
            back.Sitting == 2 && back.Wins.Count == 3 && back.WinsAt("rung.rhea") == 3 && back.WinsAt("visitor.rue") == 1 && back.WinsAt(Ladder.TopKey) == 1);
        var dir = Path.Combine(Path.GetTempPath(), "ladder-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var savePath = Path.Combine(dir, "profile.txt");
            var saver = Veteran();
            saver.Save(savePath, backup: false);
            var oldSave = LadderState.Read(File.ReadAllLines(savePath));
            var oldProfile = Profile.Load(savePath)!;
            Check("an old save with no ladder lines loads, unclimbed: next is rung 1",
                oldSave.IsEmpty && ladder.Climbed(oldSave) == 0 && ladder.Next(oldProfile, oldSave, library) == Rung(1));
            File.AppendAllText(savePath, sb.ToString());
            var withLines = Profile.Load(savePath);
            var readBack = LadderState.Read(File.ReadAllLines(savePath));
            Check("a save with ladder lines loads, and Profile.Load reads them into Profile.Ladder",
                withLines != null && withLines.Dp == saver.Dp && readBack.Sitting == 2 && readBack.WinsAt("rung.rhea") == 3
                && withLines.Ladder.Sitting == 2 && withLines.Ladder.WinsAt("rung.rhea") == 3 && withLines.Ladder.WinsAt(Ladder.TopKey) == 1);
            withLines!.NewGamePlus();
            withLines.Save(savePath, backup: false);
            var again = Profile.Load(savePath)!;
            Check($"Profile.Save writes them back, and New Game+ leaves them alone ({again.Ladder.Wins.Count} wins, sitting {again.Ladder.Sitting})",
                again.Ladder.Wins.Count == 3 && again.Ladder.Sitting == 2 && again.Ladder.WinsAt("visitor.rue") == 1);
            var bad = new LadderState();
            bool skipped = bad.ReadLine("ladwin", new[] { "rung.eldlich" }) && bad.ReadLine("ladsit", new[] { "x" }) && !bad.ReadLine("libwin", new[] { "a", "1" });
            Check("a malformed ladder line is skipped, and other lines aren't the ladder's", skipped && bad.IsEmpty);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }

        // Keys go by holder: a reordered ladder keeps a save's progress with the person beaten.
        var reordered = Ladder.Parse(File.ReadAllText(ladderPath).Replace("rung: rhea thunder-dragon", "").Replace("rung: eldlich eldlich", "rung: eldlich eldlich\nrung: rhea thunder-dragon"), roster, deckFiles.ContainsKey);
        var keyed = new LadderState();
        keyed.Wins[Rung(1).Key] = 1;
        Check($"seat keys go by holder ({Rung(1).Key}, {ilseRung.Key}, {ladder.Visitors[0].Key}): with Rhea moved to rung 2 the save's win is still hers ({reordered.Rungs[1].Character.Id} is rung 2, beaten {Ladder.Beaten(keyed, reordered.Rungs[1])})",
            Rung(1).Key == "rung.rhea" && ilseRung.Key == "rung.ilse" && reordered.Rungs[1].Character.Id == "rhea" && Ladder.Beaten(keyed, reordered.Rungs[1])
            && !Ladder.Beaten(keyed, reordered.Rungs[0]) && reordered.Problems.Count == 0);
        var dup = Ladder.Parse("opens: ilse\nrung: rhea thunder-dragon\nrung: rhea thunder-dragon\n", roster);
        Check($"a holder listed twice is skipped and named ({string.Join("; ", dup.Problems)})", dup.Rungs.Count == 1 && dup.Problems.Any(p => p.Contains("already")));

        // ---- 10. A live duel left mid-way ----
        Console.WriteLine("\nLive duels:");
        var lv = Veteran();
        var lvs = new LadderState();
        ClimbTo(lv, lvs, 8);
        ladder.Record(lv, lvs, library, ilseRung, 0);
        ladder.Record(lv, lvs, library, ilseRung, 0);
        int lossBefore = lv.DpEarned.GetValueOrDefault(Ledger.Loss(Ladder.LedgerKind));
        var settled = ladder.SettleLoss(lv, lvs, library, ilseRung.Key);
        Check($"a live ladder duel settles as a loss: rung kept ({ladder.Climbed(lvs)}), sitting reset ({lvs.Sitting}), +{lv.DpEarned.GetValueOrDefault(Ledger.Loss(Ladder.LedgerKind)) - lossBefore} as \"{Ledger.Loss(Ladder.LedgerKind)}\"",
            settled != null && settled.SittingReset && ladder.Climbed(lvs) == 8 && lvs.Sitting == 0 && lv.DpEarned.GetValueOrDefault(Ledger.Loss(Ladder.LedgerKind)) - lossBefore == 50);
        var live = Veteran();
        ClimbTo(live, live.Ladder, 8);
        ladder.Record(live, live.Ladder, library, ilseRung, 0);
        live.LiveDuel = new LiveDuel { Kind = LiveDuels.Ladder, Guest = ilseRung.Key };
        var guests = GuestList.Load(Path.Combine(root, "campaign", "guests.txt"), roster, library);
        var viaSettle = LiveDuels.Settle(live, circuits, library, guests, ladder: ladder);
        Check($"LiveDuels.Settle takes the ladder: {viaSettle?.Summary}",
            viaSettle != null && viaSettle.Opponent == "Duchess Ilse" && live.LiveDuel == null && live.Ladder.Sitting == 0 && ladder.Climbed(live.Ladder) == 8
            && viaSettle.Reward.Dp == Circuits.LossDp);
        Check("a live duel naming a seat the data no longer has: SettleLoss finds nothing", ladder.SettleLoss(lv, lvs, library, "rung.nobody") == null && ladder.SeatByKey("visitor.junia") != null);
        // Settle with no ladder passed (or a seat it no longer has): a plain loss, paid and counted, the sitting ended, never dropped.
        var plain = Veteran();
        ClimbTo(plain, plain.Ladder, 8);
        ladder.Record(plain, plain.Ladder, library, ilseRung, 0);
        int plainLoss0 = plain.DpEarned.GetValueOrDefault(Ledger.Loss(Ladder.LedgerKind)), ilseLosses0 = plain.Losses.GetValueOrDefault("ilse");
        plain.LiveDuel = new LiveDuel { Kind = LiveDuels.Ladder, Guest = ilseRung.Key };
        var plainSettled = LiveDuels.Settle(plain, circuits, library, guests, nameOf: id => roster.FirstOrDefault(r => r.Id == id)?.Name ?? id);
        plain.LiveDuel = new LiveDuel { Kind = LiveDuels.Ladder, Guest = "rung.nobody" };
        var goneSettled = LiveDuels.Settle(plain, circuits, library, guests, ladder: ladder);
        Check($"with no ladder passed, a live ladder duel settles as a plain loss ({plainSettled?.Summary}); a seat the data lost too ({goneSettled?.Summary})",
            plainSettled != null && plainSettled.Opponent == "Duchess Ilse" && goneSettled != null && plain.LiveDuel == null && plain.Ladder.Sitting == 0 && ladder.Climbed(plain.Ladder) == 8
            && plain.DpEarned.GetValueOrDefault(Ledger.Loss(Ladder.LedgerKind)) - plainLoss0 == 2 * Circuits.LossDp && plain.Losses.GetValueOrDefault("ilse") == ilseLosses0 + 1
            && Ledger.Net(plain) == plain.Dp - Profile.StartingDp);

        // ---- 11. The story's scenes (W2) ----
        Console.WriteLine("\nStory scenes:");
        var story = Story.Load(Path.Combine(root, "campaign", "story.txt"));
        var missingScenes = Ladder.RequiredScenes.Where(r => !story.Scenes.Any(s => s.Id == r.scene && s.Trigger == r.trigger)).Select(r => $"{r.scene} ({r.trigger})").ToList();
        Check($"the required scenes exist with their triggers{(missingScenes.Count > 0 ? $" (missing until W2's scenes land: {string.Join(", ", missingScenes)})" : "")}", missingScenes.Count == 0);
        var badLadderTriggers = story.Scenes.Where(s => s.Trigger.StartsWith("ladder") && !ladder.IsTrigger(s.Trigger)).Select(s => $"{s.Id} ({s.Trigger})").ToList();
        Check($"every ladder scene's trigger is one of the ladder's{(badLadderTriggers.Count > 0 ? $" (bad: {string.Join(", ", badLadderTriggers)})" : "")}", badLadderTriggers.Count == 0);

        // ---- Arbitrage ----
        int arb = Arbitrage.LadderLoops(root, roster, circuits, library, ladder);
        Check("the ladder's arbitrage loops (above)", arb == 0);

        Console.WriteLine(failures == 0 ? $"\nLadder test: all {passed} checks passed." : $"\nLadder test: {passed} passed, {failures} failed.");
        return failures == 0 ? 0 : 1;
    }
}
