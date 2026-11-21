using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;

namespace Ygo.Harness;

/// <summary>
/// The climb's engine side (docs/dev/THE-CLIMB-SPEC-2026-11-20.md, Part E tests 1-12), run inside `--campaign-test`: repeat
/// pay for circuit, Tag Cup and guest rematches; "finalline:" and "was:"; the old-save migration and its former recipes;
/// deck strength stars; the style summary without its skill; OwnsAll. Steps that need the deck swaps run on the live
/// circuits.txt's own was: lines (whatever swaps the data holds) and on a fixture campaign of their own.
/// </summary>
public static class ClimbTest
{
    /// <summary>
    /// The first build's swaps (2026-11-20): a save holding <see cref="Circuits.ClimbMigrated"/> had them applied, so the engine
    /// counts their done marks as written. The data has moved on since (only Trap Masters Cup match 1 is still swapped).
    /// </summary>
    private static readonly string[] FirstBuildSwaps =
        { "wasdone:underdog-open.3:spellbook|spellbook", "wasdone:trap-masters-cup.1:geargia|geargia", "wasdone:small-engines-invitational.4:yummy|yummy" };

    /// <summary>A small campaign of its own: two swaps, one old pairing still met in its Tag Cup (Brask's gadgets), one met nowhere else (Vera's zombies).</summary>
    private const string FixtureCampaign =
        "circuit: Fix A\nband: Beginner\nmatch: rookie-ryo classic\nmatch: brask warriors\nmatch: tink gadgets\nwas: 2 brask gadgets\n"
        + "circuit: Fix B\nband: Novice\nmatch: old-gus classic\nmatch: vera warriors\nwas: 2 vera zombies\n"
        + "tagcup: Fix Tag\nafter: Fix A\ntag: brask gadgets + old-gus zombies\n";

    public static int Run(ICardDataProvider cards, string root, PrintingIndex printings, List<Personality> roster, Circuits circuits, GuestList guests, Deck starter)
    {
        int failures = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} climb: {what}");
            if (!ok) failures++;
        }
        void Warn(string what) => Console.WriteLine($"  warn climb: {what}");
        Console.WriteLine("\nThe climb (repeat pay, billed finals, former recipes, deck stars):");
        Profile Fresh() => Profile.New(starter, cards, printings);
        int Earned(Profile p, string kind) => p.DpEarned.TryGetValue(kind, out var n) ? n : 0;
        // Exp one short of the next level, so the next duel's EXP crosses it.
        void JustUnderLevel(Profile p) => p.Exp = Enumerable.Range(1, p.Level).Sum(Leveling.ExpToNext) - 1;
        // A circuit whose win pays more than a rematch, so a full-rate rematch can't pass for one.
        int dear = Enumerable.Range(1, circuits.All.Count - 1).First(i => circuits.WinDp(i) > Circuits.RepeatWinDp * 2);

        // 1. A circuit rematch: 150 DP, Repeat, booked under "circuit"; the first win paid WinDp + the first-win bonus.
        var p1 = Fresh();
        int c0 = Earned(p1, Ledger.Circuit);
        var first = circuits.Record(p1, 0, 0, 0);
        int c1 = Earned(p1, Ledger.Circuit);
        var second = circuits.Record(p1, 0, 0, 0);
        int c2 = Earned(p1, Ledger.Circuit);
        var dearFirst = circuits.Record(p1, dear, 0, 0);
        var dearAgain = circuits.Record(p1, dear, 0, 0);
        Check($"1. a second win in 0.0 pays {second.Dp} ({second.Summary}), Repeat {second.Repeat}, \"circuit\" +{c2 - c1}; the first paid {first.Dp} "
              + $"(WinDp {circuits.WinDp(0)} + {Circuits.FirstWinBonus}); at {circuits.All[dear].Name} {dearFirst.Dp} then {dearAgain.Dp} (WinDp {circuits.WinDp(dear)})",
            first.Dp == circuits.WinDp(0) + Circuits.FirstWinBonus && first.FirstWin && !first.Repeat && c1 - c0 == first.Dp
            && second.Dp == Circuits.RepeatWinDp && second.Repeat && !second.FirstWin && c2 - c1 == Circuits.RepeatWinDp && second.Summary.StartsWith("+150 DP (rematch)")
            && dearFirst.Dp == circuits.WinDp(dear) + Circuits.FirstWinBonus && dearAgain.Dp == Circuits.RepeatWinDp && dearAgain.Repeat
            && circuits.DuelDp(dear, false) == circuits.WinDp(dear) && circuits.DuelDp(dear, true) == Circuits.RepeatWinDp && Circuits.RepeatWinDp == Library.RepeatWinDp);

        // 2. A loaner rematch pays half (75); a Tag Cup rematch 150; a guest rematch 150; a guest's prize win isn't a repeat.
        var lent = circuits.Record(p1, dear, 0, 0, loaner: true);
        var tagger = Fresh();
        int cup = Enumerable.Range(0, circuits.TagCups.Count).First(t => circuits.WinDp(circuits.TagCups[t].After) > Circuits.RepeatWinDp * 2);
        var tagFirst = circuits.RecordTag(tagger, cup, 0, 0);
        int tagBefore = Earned(tagger, Ledger.Tag);
        var tagAgain = circuits.RecordTag(tagger, cup, 0, 0);
        int tagPaid = Earned(tagger, Ledger.Tag) - tagBefore;
        var tagLent = circuits.RecordTag(tagger, cup, 0, 0, loaner: true);
        var guest = guests.All.First(g => !g.PrizeOnce && g.Prize > 0);
        var visitor = Fresh();
        // On a circuit past the first, so a guest's full win pays more than a rematch.
        for (int ci = 0; ci < dear; ci++)
            for (int mi = 0; mi < circuits.All[ci].Matches.Count; mi++) visitor.MatchWins[Profile.MatchKey(ci, mi)] = 1;
        int guestDuel = circuits.WinDp(circuits.Current(visitor));
        var guestFirst = GuestList.Record(visitor, circuits, guest, false, 0);
        int guestBefore = Earned(visitor, Ledger.Guest);
        var guestAgain = GuestList.Record(visitor, circuits, guest, false, 0);
        int guestPaid = Earned(visitor, Ledger.Guest) - guestBefore;
        var guestLent = GuestList.Record(visitor, circuits, guest, false, 0, loaner: true);
        string tagGuest = "no tag guest";
        bool tagGuestOk = true;
        if (guests.All.FirstOrDefault(g => g.HasTag) is Guest tg)
        {
            var tgFirst = GuestList.Record(visitor, circuits, tg, true, 0);
            var tgAgain = GuestList.Record(visitor, circuits, tg, true, 0);
            tagGuest = $"{tg.Character.Name}'s tag duel {tgFirst.Dp} then {tgAgain.Dp}";
            tagGuestOk = tgFirst.FirstWin && !tgFirst.Repeat && tgAgain.Repeat && tgAgain.Dp == Circuits.RepeatWinDp;
        }
        Check($"2. a loaner rematch pays {lent.Dp} ({lent.Summary}); a Tag Cup rematch {tagAgain.Dp} (first {tagFirst.Dp}, loaner {tagLent.Dp}); "
              + $"{guest.Character.Name}: prize win {guestFirst.Dp} (Repeat {guestFirst.Repeat}), rematch {guestAgain.Dp} ({guestAgain.Summary}), loaner {guestLent.Dp}; {tagGuest}",
            lent.Dp == 75 && lent.Repeat && lent.Summary.Contains("(rematch)") && lent.Summary.Contains("loaner")
            && tagFirst.FirstWin && tagAgain.Repeat && tagAgain.Dp == Circuits.RepeatWinDp && tagPaid == Circuits.RepeatWinDp && tagLent.Dp == 75 && tagAgain.Summary.StartsWith("+150 DP (rematch)")
            && guestFirst.FirstWin && !guestFirst.Repeat && guestFirst.Dp == guestDuel + guest.Prize && guestDuel > Circuits.RepeatWinDp
            && guestAgain.Repeat && !guestAgain.FirstWin && guestAgain.Dp == Circuits.RepeatWinDp && guestPaid == Circuits.RepeatWinDp && guestAgain.Summary.StartsWith("+150 DP (rematch)")
            && guestLent.Dp == 75 && tagGuestOk);

        // 3. A rematch across a level pays it in tokens (at least one) and no DP; a first win across a level pays its DP.
        var p3 = Fresh();
        JustUnderLevel(p3);
        var firstUp = circuits.Record(p3, dear, 1, 0);
        JustUnderLevel(p3);
        int dpBefore = p3.Dp, levelDp = Earned(p3, Ledger.LevelUp), tokens = p3.Tokens;
        var againUp = circuits.Record(p3, dear, 1, 0);
        bool tokenOnly = againUp.LevelUps.Count > 0 && againUp.LevelUps.All(u => u.Dp == 0 && u.Tokens >= 1)
                         && p3.Dp - dpBefore == Circuits.RepeatWinDp && Earned(p3, Ledger.LevelUp) == levelDp && p3.Tokens - tokens == againUp.LevelUps.Sum(u => u.Tokens);
        JustUnderLevel(tagger);
        int tagLevelDp = Earned(tagger, Ledger.LevelUp);
        var tagUp = circuits.RecordTag(tagger, cup, 0, 0);
        JustUnderLevel(visitor);
        int guestLevelDp = Earned(visitor, Ledger.LevelUp);
        var guestUp = GuestList.Record(visitor, circuits, guest, false, 0);
        Check($"3. a rematch across a level pays {string.Join(", ", againUp.LevelUps.Select(Leveling.Describe))} ({againUp.Summary}); "
              + $"a first win across one pays {string.Join(", ", firstUp.LevelUps.Select(Leveling.Describe))}; tag and guest rematches: {string.Join(", ", tagUp.LevelUps.Concat(guestUp.LevelUps).Select(Leveling.Describe))}",
            tokenOnly && firstUp.LevelUps.Count > 0 && firstUp.LevelUps.All(u => u.Dp > 0) && !againUp.Summary.Contains("+0 DP")
            && tagUp.LevelUps.Count > 0 && tagUp.LevelUps.All(u => u.Dp == 0 && u.Tokens >= 1) && Earned(tagger, Ledger.LevelUp) == tagLevelDp
            && guestUp.LevelUps.Count > 0 && guestUp.LevelUps.All(u => u.Dp == 0 && u.Tokens >= 1) && Earned(visitor, Ledger.LevelUp) == guestLevelDp);

        // 4. A loss in a won match pays 50 and token-only level-ups; a loss in a match not yet won pays level DP as before.
        JustUnderLevel(p3);
        int lossDp = p3.Dp;
        var wonLoss = circuits.Record(p3, dear, 1, 1);
        int afterWonLoss = p3.Dp;
        JustUnderLevel(p3);
        var newLoss = circuits.Record(p3, dear, 2, 1);
        Check($"4. a loss in a won match pays {afterWonLoss - lossDp} DP and {string.Join(", ", wonLoss.LevelUps.Select(Leveling.Describe))}; "
              + $"in a match not yet won {string.Join(", ", newLoss.LevelUps.Select(Leveling.Describe))}",
            wonLoss.Dp == Circuits.LossDp && afterWonLoss - lossDp == Circuits.LossDp && !wonLoss.Repeat
            && wonLoss.LevelUps.Count > 0 && wonLoss.LevelUps.All(u => u.Dp == 0 && u.Tokens >= 1) && newLoss.LevelUps.Count > 0 && newLoss.LevelUps.All(u => u.Dp > 0));

        // 5-6. "finalline:" and "was:" on a fixture (a Tag Cup's are ignored; a bad number or an unknown character is skipped).
        var fixture = Circuits.Parse(string.Join("\n",
            "circuit: Climb One", "band: Beginner", "finalline: This one decides it. Ready?   # a comment",
            "match: rookie-ryo classic", "match: brask warriors", "match: rookie-ryo warriors",
            "was: 2 brask gadgets", "was: 9 brask classic", "was: 0 brask classic", "was: 3 nobody-at-all classic", "was: x brask classic",
            "circuit: Climb Two", "band: Novice", "match: brask gadgets", "was: 1 brask", "match: old-gus classic",
            "tagcup: Climb Tag", "after: Climb One", "finalline: not this", "was: 1 brask classic", "tag: brask warriors + old-gus classic"), roster);
        var one = fixture.All[0];
        var two = fixture.All[1];
        Check($"5. finalline parses (\"{one.FinalLine}\"); none is \"\" (\"{two.FinalLine}\"); a Tag Cup's is ignored; IsFinal only for the last match "
              + $"({string.Join("", Enumerable.Range(0, 4).Select(m => Circuits.IsFinal(one, m) ? "F" : "-"))}); the live final: {circuits.All[0].Name} match {circuits.All[0].Matches.Count}",
            one.FinalLine == "This one decides it. Ready?" && two.FinalLine == "" && fixture.TagCups.Count == 1
            && !Circuits.IsFinal(one, 0) && !Circuits.IsFinal(one, 1) && Circuits.IsFinal(one, 2) && !Circuits.IsFinal(one, 3)
            && Circuits.IsFinal(circuits.All[0], circuits.All[0].Matches.Count - 1) && one.Matches.Count == 3);
        Check($"6. was: parses to Former ({string.Join(", ", one.Former.Select(f => $"{f.Match}: {f.Was.Character.Id} {f.Was.Deck}"))}; "
              + $"{string.Join(", ", two.Former.Select(f => $"{f.Match}: {f.Was.Character.Id} {f.Was.Deck}"))}); bad numbers and unknown characters skipped, a Tag Cup's ignored",
            one.Former.Count == 1 && one.Former[0].Match == 1 && one.Former[0].Was.Character.Id == "brask" && one.Former[0].Was.Deck == "gadgets"
            && two.Former.Count == 1 && two.Former[0].Match == 0 && two.Former[0].Was.Deck == roster.First(r => r.Id == "brask").Decks[0]);

        // 7-8. Former recipes. Every expectation comes from a campaign's own "was:" lines: SwapsOf lists them (circuit, match,
        // old pairing, done mark); Stripped is the same text without them (what an old save's wins meant).
        string FormerOf(CircuitMatch was) => RecipeBook.FormerPrefix + RecipeBook.Key(was.Character.Id, was.Deck);
        List<(int ci, int mi, CircuitMatch was, string done)> SwapsOf(Circuits c) =>
            Enumerable.Range(0, c.All.Count).SelectMany(ci => c.All[ci].Former.Select(f => (ci, f.Match, f.Was, Circuits.FormerDoneKey(c.All[ci], f.Match, f.Was)))).ToList();
        Circuits Stripped(string text) => Circuits.Parse(string.Join("\n", text.Split('\n').Where(l => !l.TrimStart().StartsWith("was:"))), roster);
        List<string> Sorted(IEnumerable<string> keys) => keys.Distinct().OrderBy(u => u, StringComparer.Ordinal).ToList();
        string Keys(IEnumerable<string> keys) => string.Join(", ", keys);
        Recipe? RecipeOf(List<Recipe> all, string owner, string deck) => all.FirstOrDefault(r => r.Owner.Id == owner && r.Deck == deck);
        // What Migrate must add to a save, by its rules: each swap not yet done (by its mark, or by the first build's gate for that
        // build's swaps) adds its done mark, and its old pairing's former key when the save beat that pairing.
        List<string> Expected(Profile p, List<(int ci, int mi, CircuitMatch was, string done)> swaps)
        {
            bool gate = p.Unlocks.Contains(Circuits.ClimbMigrated);
            var open = swaps.Where(k => !p.Unlocks.Contains(k.done) && !(gate && FirstBuildSwaps.Contains(k.done))).ToList();
            bool Beat((int ci, int mi, CircuitMatch was, string done) k) => p.HasWon(k.ci, k.mi)
                || (p.Cycle >= 1 && (p.WinsAgainst(k.was.Character.Id) > 0 || p.Seen.Contains("recipe:" + RecipeBook.Key(k.was.Character.Id, k.was.Deck))));
            return Sorted(open.Where(Beat).Select(k => FormerOf(k.was)).Concat(open.Select(k => k.done)).Where(k => !p.Unlocks.Contains(k)));
        }
        // Migrates a save: it must gain exactly Expected and nothing else may move; a second Migrate and a reload change nothing.
        (bool ok, List<string> gained) Migrated(Profile p, Circuits c)
        {
            var expected = Expected(p, SwapsOf(c));
            var had = p.Unlocks.ToHashSet();
            var wins = p.MatchWins.OrderBy(w => w.Key).ToList();
            var seen = p.Seen.ToHashSet();
            int dp = p.Dp, exp = p.Exp, tokens = p.Tokens, cleared = c.ClearedCount(p);
            c.Migrate(p);
            var gained = Sorted(p.Unlocks.Except(had));
            var once = Sorted(p.Unlocks);
            c.Migrate(p);
            var file = Path.Combine(Path.GetTempPath(), $"ygo-climb-{Guid.NewGuid():N}.txt");
            p.Save(file);
            var back = Profile.Load(file)!;
            File.Delete(file);
            File.Delete(file + ".bak");
            c.Migrate(back);
            bool ok = gained.SequenceEqual(expected) && had.All(p.Unlocks.Contains) && Sorted(p.Unlocks).SequenceEqual(once) && Sorted(back.Unlocks).SequenceEqual(once)
                      && p.MatchWins.OrderBy(w => w.Key).SequenceEqual(wins) && back.MatchWins.OrderBy(w => w.Key).SequenceEqual(wins)
                      && p.Dp == dp && back.Dp == dp && p.Exp == exp && p.Tokens == tokens && p.Seen.SetEquals(seen) && c.ClearedCount(p) == cleared;
            return (ok, gained);
        }

        // 7. The fixture (no live data): a save won swapped A.2 but not B.2. The old pairing it beat is unlocked, the other isn't, the
        // current pairings by their wins; a save with no wins gets the done marks only; a campaign without was: lines writes nothing.
        var fixtureCampaign = Circuits.Parse(FixtureCampaign, roster);
        var fixtureSwaps = SwapsOf(fixtureCampaign);
        var fixtureOld = Fresh();
        foreach (var key in new[] { "0.0", "0.1", "0.2", "1.0" }) fixtureOld.MatchWins[key] = 1;
        Stripped(FixtureCampaign).Migrate(fixtureOld); // the layout only
        var (fixtureOk, fixtureGained) = Migrated(fixtureOld, fixtureCampaign);
        var fixtureRecipes = RecipeBook.All(fixtureCampaign);
        var braskOld = RecipeOf(fixtureRecipes, "brask", "gadgets");
        var veraOld = RecipeOf(fixtureRecipes, "vera", "zombies");
        var fixtureNone = Fresh();
        var (noneOk, noneGained) = Migrated(fixtureNone, fixtureCampaign);
        var plainSave = Fresh();
        int plainUnlocks = plainSave.Unlocks.Count;
        Stripped(FixtureCampaign).Migrate(plainSave);
        Check($"7. fixture swaps {Keys(fixtureSwaps.Select(k => $"{k.ci}.{k.mi} {k.was.Character.Id} {k.was.Deck}"))}: a save that won A.2 gains [{Keys(fixtureGained)}]; "
              + $"Brask's gadgets {(braskOld != null && RecipeBook.IsUnlocked(braskOld, fixtureCampaign, fixtureOld) ? "unlocked" : "LOCKED")} (former {braskOld?.Former}: met in the Tag Cup), "
              + $"Vera's zombies {(veraOld != null && RecipeBook.IsUnlocked(veraOld, fixtureCampaign, fixtureOld) ? "UNLOCKED" : "locked")} (former {veraOld?.Former}); no wins: +[{Keys(noneGained)}]; "
              + "nothing else moved, a second Migrate and a reload change nothing; no was: lines, nothing written",
            fixtureSwaps.Count == 2 && fixtureOk && noneOk
            && fixtureGained.SequenceEqual(Sorted(new[] { RecipeBook.FormerPrefix + "brask|gadgets", fixtureSwaps[0].done, fixtureSwaps[1].done }))
            && noneGained.SequenceEqual(Sorted(fixtureSwaps.Select(k => k.done)))
            && braskOld != null && !braskOld.Former && RecipeBook.IsUnlocked(braskOld, fixtureCampaign, fixtureOld)
            && veraOld != null && veraOld.Former && !RecipeBook.IsUnlocked(veraOld, fixtureCampaign, fixtureOld) && !RecipeBook.IsUnlocked(veraOld, fixtureCampaign, fixtureNone)
            && RecipeBook.IsUnlocked(RecipeOf(fixtureRecipes, "brask", "warriors")!, fixtureCampaign, fixtureOld)
            && !RecipeBook.IsUnlocked(RecipeOf(fixtureRecipes, "vera", "warriors")!, fixtureCampaign, fixtureOld)
            && plainSave.Unlocks.Count == plainUnlocks);

        // 7a. The live data, whatever swaps circuits.txt holds: a save that won every match up to the last swapped circuit except
        // one swapped match gains each won swap's former key and every done mark, and nothing else.
        var liveText = File.ReadAllText(Path.Combine(root, "campaign", "circuits.txt"));
        var liveSwaps = SwapsOf(circuits);
        var liveStripped = Stripped(liveText);
        if (liveSwaps.Count == 0) Console.WriteLine("  info climb: 7a. circuits.txt has no was: lines: the live old-save step didn't run");
        else
        {
            var skip = liveSwaps[liveSwaps.Count - 1];
            var liveOld = Fresh();
            for (int ci = 0; ci <= liveSwaps.Max(k => k.ci); ci++)
                for (int mi = 0; mi < circuits.All[ci].Matches.Count; mi++)
                    if (ci != skip.ci || mi != skip.mi) liveOld.MatchWins[Profile.MatchKey(ci, mi)] = 1 + (ci + mi) % 3;
            liveStripped.Migrate(liveOld);
            var (liveOk, liveGained) = Migrated(liveOld, circuits);
            var swapRecipes = RecipeBook.All(circuits);
            var wonSwaps = liveSwaps.Where(k => k.ci != skip.ci || k.mi != skip.mi).ToList();
            bool oldUnlocked = wonSwaps.All(k => RecipeOf(swapRecipes, k.was.Character.Id, k.was.Deck) is Recipe r && RecipeBook.IsUnlocked(r, circuits, liveOld));
            Check($"7a. live swaps {Keys(liveSwaps.Select(k => $"{k.ci}.{k.mi} {k.was.Character.Id} {k.was.Deck} -> {circuits.All[k.ci].Matches[k.mi].Deck}"))}; "
                  + $"a save that won all but {skip.ci}.{skip.mi} gains [{Keys(liveGained)}]; the old pairings it beat are unlocked ({oldUnlocked}); "
                  + "nothing else moved, a second Migrate and a reload change nothing",
                liveOk && oldUnlocked && liveGained.SequenceEqual(Sorted(wonSwaps.Select(k => FormerOf(k.was)).Concat(liveSwaps.Select(k => k.done)))));
        }

        // 7b. New Game+ clears match wins (and can start any time): a cycle-1 save that beat a swapped pairing's duelist, or opened
        // its recipe, keeps it (fixture: Vera's zombies, met nowhere else); one that never did, and a cycle-0 save without the win, don't.
        List<string> FormersGained(Profile p, Circuits c)
        {
            var had = p.Unlocks.ToHashSet();
            c.Migrate(p);
            return Sorted(p.Unlocks.Except(had).Where(u => u.StartsWith(RecipeBook.FormerPrefix)));
        }
        Profile Cycled() { var p = Fresh(); p.NewGamePlus(); return p; }
        var beatVera = Cycled();
        beatVera.Wins["vera"] = 1;
        var cycledBeaten = FormersGained(beatVera, fixtureCampaign);
        var cycledNever = FormersGained(Cycled(), fixtureCampaign);
        var seenOnly = Cycled();
        seenOnly.Seen.Add("recipe:vera|zombies");
        var seenGained = FormersGained(seenOnly, fixtureCampaign);
        var cycle0 = Fresh();
        cycle0.Wins["vera"] = 1;
        var cycle0Gained = FormersGained(cycle0, fixtureCampaign);
        Check($"7b. a cycle-1 save with empty MatchWins that beat Vera gains [{Keys(cycledBeaten)}]; one that never did +[{Keys(cycledNever)}]; "
              + $"one that opened her old recipe +[{Keys(seenGained)}]; a cycle-0 save without the match win +[{Keys(cycle0Gained)}]",
            cycledBeaten.SequenceEqual(new[] { RecipeBook.FormerPrefix + "vera|zombies" }) && cycledNever.Count == 0
            && seenGained.SequenceEqual(new[] { RecipeBook.FormerPrefix + "vera|zombies" }) && cycle0Gained.Count == 0);

        // 7c. Each swap applies once, whenever it arrives: a was: line added later (fixture) applies only itself, once. A save from
        // the first build (its climb:2026-11-20 gate and former keys for swaps the data may no longer have) keeps them and gets every
        // live swap the first build didn't apply.
        var later = Circuits.Parse(FixtureCampaign.Replace("was: 2 vera zombies\n", "was: 2 vera zombies\nwas: 1 old-gus zombies\n"), roster);
        var (laterOk, laterGained) = Migrated(fixtureOld, later);
        var fourth = SwapsOf(later).First(k => k.was.Character.Id == "old-gus");
        var firstBuild = Fresh();
        for (int ci = 0; ci <= liveSwaps.Select(k => k.ci).DefaultIfEmpty(0).Max(); ci++)
            for (int mi = 0; mi < circuits.All[ci].Matches.Count; mi++) firstBuild.MatchWins[Profile.MatchKey(ci, mi)] = 1;
        firstBuild.Unlocks.Add(Circuits.ClimbMigrated);
        foreach (var key in new[] { "spellbook|spellbook", "geargia|geargia", "yummy|yummy" }) firstBuild.Unlocks.Add(RecipeBook.FormerPrefix + key);
        var (gateOk, gateGained) = Migrated(firstBuild, circuits);
        var stillApplied = liveSwaps.Where(k => !FirstBuildSwaps.Contains(k.done)).ToList();
        Check($"7c. a fixture was: line added later applies only itself (+[{Keys(laterGained)}]), once; a first-build save (gate and three former keys) gains "
              + $"[{Keys(gateGained)}] from the live swaps, keeping what it had",
            laterOk && laterGained.SequenceEqual(Sorted(new[] { RecipeBook.FormerPrefix + "old-gus|zombies", fourth.done }))
            && gateOk && gateGained.SequenceEqual(Sorted(stillApplied.Select(k => FormerOf(k.was)).Concat(stillApplied.Select(k => k.done)))));

        // 7d. A recipe kept through a former key isn't "unlocked" again by the Tag Cup match that still plays it (fixture: Brask's gadgets).
        var tagHolder = Fresh();
        tagHolder.MatchWins["0.1"] = 1; // won A.2 before the swap
        fixtureCampaign.Migrate(tagHolder);
        fixtureCampaign.RecordTag(tagHolder, 0, 0, 0);
        var tagNews = RecipeBook.JustUnlockedTag(fixtureCampaign, tagHolder, 0, 0);
        var fresher = Fresh();
        fixtureCampaign.Migrate(fresher);
        fixtureCampaign.RecordTag(fresher, 0, 0, 0);
        var freshNews = RecipeBook.JustUnlockedTag(fixtureCampaign, fresher, 0, 0);
        Check($"7d. a save holding Brask's gadgets through its former key wins the Tag Cup match: newly unlocked [{Keys(tagNews.Select(r => $"{r.Owner.Name}'s {r.Deck}"))}]; "
              + $"a save without it [{Keys(freshNews.Select(r => $"{r.Owner.Name}'s {r.Deck}"))}]",
            tagHolder.Unlocks.Contains(RecipeBook.FormerPrefix + "brask|gadgets") && !tagNews.Any(r => r.Owner.Id == "brask" && r.Deck == "gadgets")
            && freshNews.Any(r => r.Owner.Id == "brask" && r.Deck == "gadgets"));

        // 8. Real saves (copies only; the step reports when none is there): migrated on the live campaign after the same text without
        // its was: lines (their meaning before), each gains exactly what its record says, and nothing else moves.
        var saves = new[] { Path.Combine(root, "build", "climb", "save-copy"), Path.Combine(root, "build", "climb-e", "saves") }
            .Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "profile*.txt", SearchOption.AllDirectories)).OrderBy(f => f).ToList();
        if (saves.Count == 0) Console.WriteLine("  info climb: 8. no save copies under build/climb/save-copy or build/climb-e/saves: the real-save step didn't run");
        foreach (var path in saves)
        {
            var name = Path.GetRelativePath(root, path);
            var save = Profile.Load(path);
            if (save == null) { Check($"8. {name} loads", false); continue; }
            liveStripped.Migrate(save);
            var won = liveSwaps.Where(k => save.HasWon(k.ci, k.mi)).Select(k => $"{k.ci}.{k.mi}").ToList();
            bool gate = save.Unlocks.Contains(Circuits.ClimbMigrated);
            int marks = liveSwaps.Count(k => !save.Unlocks.Contains(k.done) && !(gate && FirstBuildSwaps.Contains(k.done)));
            var (ok, gained) = Migrated(save, circuits);
            Check($"8. {name}: {circuits.ClearedCount(save)} circuits cleared, {save.MatchWins.Count} match wins (swapped ones won: {string.Join(" ", won)}), {save.Dp:N0} DP, cycle {save.Cycle}; "
                  + $"gains [{Keys(gained.Where(u => u.StartsWith(RecipeBook.FormerPrefix)))}] and {gained.Count(u => u.StartsWith("wasdone:"))} done marks; nothing else changes",
                ok && gained.Count(u => u.StartsWith("wasdone:")) == marks);
        }

        // 9. Deck strength: the fixed lines, unrated decks, mean and strongest, and "Best after".
        var ratings = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName), Path.Combine(root, "campaign", DeckRatings.PeakFileName));
        var lines = new (double elo, int stars)[] { (1349.9, 1), (1350, 2), (1499, 2), (1500, 3), (1649, 3), (1650, 4), (1799, 4), (1800, 5), (900, 1), (2400, 5) };
        var bands = circuits.All.Select(c => c.Band).Distinct().ToList();
        var guestBest = guests.All.Select(g => (g, elo: DeckStrength.Elo(ratings, g.Decks, strongest: true))).ToList();
        string fake = "no-such-deck-" + Guid.NewGuid().ToString("N");
        var pair = new[] { "warriors", "synchron", fake };
        Check($"9. stars: {string.Join(", ", lines.Select(l => $"{l.elo} -> {DeckStrength.Stars(l.elo)}"))}; unrated -> {DeckStrength.StarsFor(ratings, fake)}; "
              + $"warriors {DeckStrength.StarsFor(ratings, "warriors")}, warriors+synchron mean {DeckStrength.Elo(ratings, pair, false):0} -> {DeckStrength.StarsFor(ratings, pair)}; "
              + $"band means {string.Join(", ", bands.Select(b => $"{b} {DeckStrength.BandMean(circuits, ratings, b):0}"))}; best after: 1768 {DeckStrength.BestAfterBand(circuits, ratings, 1768)}, "
              + $"1401 {DeckStrength.BestAfterBand(circuits, ratings, 1401)}, 2100 {DeckStrength.BestAfterBand(circuits, ratings, 2100)}; guests "
              + string.Join(", ", guestBest.Select(x => $"{x.g.Character.Name} {x.elo:0} {DeckStrength.Stars(x.elo)}* -> {DeckStrength.BestAfterBand(circuits, ratings, x.elo)}")),
            lines.All(l => DeckStrength.Stars(l.elo) == l.stars) && DeckStrength.StarsFor(ratings, fake) == 0 && DeckStrength.StarsFor(ratings, new[] { fake }) == 0
            && DeckStrength.Elo(ratings, new[] { fake }, true) == 0 && DeckStrength.MaxStars == 5 && DeckStrength.Lines.SequenceEqual(new double[] { 1350, 1500, 1650, 1800 })
            && Math.Abs(DeckStrength.Elo(ratings, pair, false) - (ratings["warriors"] + ratings["synchron"]) / 2) < 1e-9
            && DeckStrength.Elo(ratings, pair, true) == Math.Max(ratings["warriors"], ratings["synchron"])
            && DeckStrength.BestAfterBand(circuits, ratings, 1768) == "Master" && DeckStrength.BestAfterBand(circuits, ratings, 1401) == "Beginner"
            && DeckStrength.BestAfterBand(circuits, ratings, 2100) == "Tier 0 Gauntlet");

        // 10. The style summary has no skill number, for every character.
        var skilled = roster.Where(p => p.StyleSummary.Contains("skill") || p.StyleSummary.Contains("/10")).Select(p => p.Id).ToList();
        Check($"10. no StyleSummary says \"skill\" ({roster.Count} characters; e.g. {roster.First(p => p.Id == "rookie-ryo").Name}: \"{roster.First(p => p.Id == "rookie-ryo").StyleSummary}\"; with it: {string.Join(", ", skilled)})",
            skilled.Count == 0 && roster.All(p => p.StyleSummary.Length > 0));

        // 11. OwnsAll: every card of a recipe owned; then one copy short.
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        var recipe = Deck.LoadYdk(deckFiles["warriors"]);
        var wants = AutoDeck.Wants(recipe, Ygo.Engine.DeckBuilding.Banlist.None, cards);
        var holder = new Profile();
        foreach (var (code, count) in wants) holder.Collection.Add(code, Rarity.Common, count);
        var shortOne = new Profile();
        var shortCard = wants.First(w => w.Value >= 1).Key;
        foreach (var (code, count) in wants) shortOne.Collection.Add(code, Rarity.Common, code == shortCard ? count - 1 : count);
        // Era-limited mode: a card never printed in the TCG still has to be owned (the shop's Missing leaves it out).
        var ocgOnly = cards.All.First(c => c.Alias == 0 && printings.FirstYear(c.Code, 0) == 0 && (c.Type & CardType.Monster) != 0).Code;
        var withOcg = new Deck { Name = "warriors+ocg", Main = recipe.Main.Append(ocgOnly).ToList(), Extra = recipe.Extra.ToList() };
        holder.EraLimited = true;
        bool eraOwns = RecipeBook.OwnsAll(withOcg, holder, printings, cards), eraWhole = RecipeBook.OwnsAll(recipe, holder, printings, cards);
        holder.EraLimited = false;
        Check($"11b. era-limited: the recipe plus {cards.Get(ocgOnly)?.Name} (never printed in the TCG, not owned) reads owned: {eraOwns}; the recipe alone {eraWhole}",
            !eraOwns && eraWhole);
        Check($"11. OwnsAll: the whole warriors recipe ({wants.Values.Sum()} copies) {RecipeBook.OwnsAll(recipe, holder, printings, cards)}, "
              + $"one {cards.Get(shortCard)?.Name} short {RecipeBook.OwnsAll(recipe, shortOne, printings, cards)}, a new save's starter (classic): {RecipeBook.OwnsAll(recipe, Fresh(), printings, cards)}",
            RecipeBook.OwnsAll(recipe, holder, printings, cards) && !RecipeBook.OwnsAll(recipe, shortOne, printings, cards) && !RecipeBook.OwnsAll(recipe, new Profile(), printings, cards));

        // 12. Data: final lines (warnings until the owner approves them) and "was:" decks whose recipe is reachable.
        var noLine = circuits.All.Where(c => c.FinalLine.Length == 0).Select(c => c.Name).ToList();
        var longLine = circuits.All.Where(c => c.FinalLine.Length > 100).Select(c => $"{c.Name} ({c.FinalLine.Length})").ToList();
        if (noLine.Count > 0) Warn($"12. {noLine.Count} of {circuits.All.Count} circuits have no finalline yet (the row and the versus fall back to the intro)");
        if (longLine.Count > 0) Warn($"12. final lines over 100 characters: {string.Join(", ", longLine)}");
        var all = RecipeBook.All(circuits);
        var formers = circuits.All.SelectMany(c => c.Former.Select(f => (c, f.Match, f.Was))).ToList();
        var unreachable = formers.Where(f => !deckFiles.ContainsKey(f.Was.Deck) || !all.Any(r => r.Owner.Id == f.Was.Character.Id && r.Deck == f.Was.Deck))
            .Select(f => $"{f.c.Name} match {f.Match + 1}: {f.Was.Character.Id} {f.Was.Deck}").ToList();
        if (formers.Count == 0) Console.WriteLine("  info climb: 12. circuits.txt has no was: lines yet (part D's swaps)");
        Check($"12. every was: line ({formers.Count}: {string.Join(", ", formers.Select(f => $"{f.c.Name} {f.Match + 1} {f.Was.Character.Id} {f.Was.Deck}"))}) names a deck with a reachable recipe "
              + $"({string.Join("; ", unreachable)}); {circuits.All.Count - noLine.Count} final lines, none over 100 characters ({longLine.Count})",
            unreachable.Count == 0);

        return failures;
    }
}
