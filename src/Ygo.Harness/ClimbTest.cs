using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;

namespace Ygo.Harness;

/// <summary>
/// The climb's engine side (docs/dev/THE-CLIMB-SPEC-2026-11-20.md, Part E tests 1-12), run inside `--campaign-test`: repeat
/// pay for circuit, Tag Cup and guest rematches; "finalline:" and "was:"; the old-save migration and its former recipes;
/// deck strength stars; the style summary without its skill; OwnsAll. Steps that need the deck swaps run on the live
/// circuits.txt with the spec's three swaps applied in memory (a no-op once they're in the file), so they don't wait on the data.
/// </summary>
public static class ClimbTest
{
    /// <summary>The three swaps of the spec's section 1: the line as it was, the new deck, and the "was:" line it leaves.</summary>
    private static readonly (string owner, string oldDeck, string newDeck, int number)[] Swaps =
    {
        ("spellbook", "spellbook", "spellcasters-command", 3),
        ("geargia", "geargia", "machine-re-volt", 1),
        ("yummy", "yummy", "synchron", 4),
    };

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

        // 7. The old save, through Migrate on the swapped campaign.
        var circuitsPath = Path.Combine(root, "campaign", "circuits.txt");
        var swappedText = Swapped(File.ReadAllText(circuitsPath));
        var swapped = Circuits.Parse(swappedText, roster);
        // The campaign as an old save knew it: the same file without its "was:" lines (nothing to migrate).
        var stripped = Circuits.Parse(string.Join("\n", swappedText.Split('\n').Where(l => !l.TrimStart().StartsWith("was:"))), roster);
        var swapKeys = new List<(string owner, string oldDeck, string newDeck, int ci, int mi, string done)>();
        foreach (var (owner, oldDeck, newDeck, number) in Swaps)
            for (int ci = 0; ci < swapped.All.Count; ci++)
                foreach (var (mi, was) in swapped.All[ci].Former)
                    if (was.Character.Id == owner && was.Deck == oldDeck && mi == number - 1 && swapped.All[ci].Matches[mi].Deck == newDeck)
                        swapKeys.Add((owner, oldDeck, newDeck, ci, mi, Circuits.FormerDoneKey(swapped.All[ci], mi, was)));
        string FormerOf(string owner, string deck) => RecipeBook.FormerPrefix + RecipeBook.Key(owner, deck);
        var doneKeys = swapKeys.Select(k => k.done).ToList();
        var oldSave = Fresh();
        for (int ci = 0; ci <= swapKeys.Select(k => k.ci).DefaultIfEmpty(0).Max(); ci++)
            for (int mi = 0; mi < swapped.All[ci].Matches.Count; mi++) oldSave.MatchWins[Profile.MatchKey(ci, mi)] = 1 + (ci + mi) % 3;
        stripped.Migrate(oldSave); // the layout move only
        var winsBefore = oldSave.MatchWins.OrderBy(w => w.Key).ToList();
        var clearedBefore = Enumerable.Range(0, swapped.All.Count).Where(i => swapped.IsCleared(i, oldSave)).ToList();
        var unlocksBefore = oldSave.Unlocks.ToHashSet();
        int dpOld = oldSave.Dp;
        swapped.Migrate(oldSave);
        var gained = oldSave.Unlocks.Except(unlocksBefore).OrderBy(u => u).ToList();
        var expected = Swaps.Select(s => FormerOf(s.owner, s.oldDeck)).Concat(doneKeys).OrderBy(u => u).ToList();
        var unlocked = RecipeBook.Unlocked(swapped, oldSave);
        bool Has(string owner, string deck) => unlocked.Any(r => r.Owner.Id == owner && r.Deck == deck);
        var oldRecipes = Swaps.Select(s => unlocked.FirstOrDefault(r => r.Owner.Id == s.owner && r.Deck == s.oldDeck)).ToList();
        var afterOnce = oldSave.Unlocks.OrderBy(u => u).ToList();
        swapped.Migrate(oldSave);
        bool idempotent = oldSave.Unlocks.OrderBy(u => u).SequenceEqual(afterOnce);
        var roundTrip = Path.Combine(Path.GetTempPath(), $"ygo-climb-{Guid.NewGuid():N}.txt");
        oldSave.Save(roundTrip);
        var reloaded = Profile.Load(roundTrip)!;
        File.Delete(roundTrip);
        File.Delete(roundTrip + ".bak");
        swapped.Migrate(reloaded);
        bool reloadKeeps = reloaded.Unlocks.OrderBy(u => u).SequenceEqual(afterOnce) && RecipeBook.Unlocked(swapped, reloaded).Count == unlocked.Count;
        reloaded.NewGamePlus();
        var yummyRecipe = RecipeBook.All(swapped).FirstOrDefault(r => r.Owner.Id == Swaps[2].owner && r.Deck == Swaps[2].oldDeck);
        bool ngKeeps = expected.All(reloaded.Unlocks.Contains) && yummyRecipe != null && RecipeBook.IsUnlocked(yummyRecipe, swapped, reloaded) && reloaded.MatchWins.Count == 0;
        var noWins = Fresh();
        var noWinsBefore = noWins.Unlocks.ToHashSet();
        swapped.Migrate(noWins);
        var noWinsGained = noWins.Unlocks.Except(noWinsBefore).OrderBy(u => u).ToList();
        // A campaign with no "was:" line writes nothing (so data that arrives later still finds the save unmigrated).
        var plainSave = Fresh();
        int plainUnlocks = plainSave.Unlocks.Count;
        stripped.Migrate(plainSave);
        Check($"7. old save: swaps at {string.Join(", ", swapKeys.Select(k => $"{k.ci}.{k.mi} {k.owner} {k.oldDeck} -> {k.newDeck}"))}; Migrate adds [{string.Join(", ", gained)}]; "
              + $"recipes {string.Join(", ", oldRecipes.Select(r => r == null ? "MISSING" : $"{r.Owner.Name}'s {r.Deck}{(r.Former ? " (former)" : "")}"))} and the new "
              + $"{string.Join(", ", Swaps.Select(s => $"{s.newDeck} {(Has(s.owner, s.newDeck) ? "unlocked" : "LOCKED")}"))}; cleared {string.Join(",", clearedBefore)} kept; "
              + $"a second Migrate adds nothing ({idempotent}); survives saving ({reloadKeeps}) and New Game+ ({ngKeeps}); no wins there: +[{string.Join(", ", noWinsGained)}]; no was: lines, nothing written",
            swapKeys.Select(k => $"{k.ci}.{k.mi}").SequenceEqual(new[] { "4.2", "5.0", "6.3" })
            && gained.SequenceEqual(expected) && oldRecipes.All(r => r != null) && oldRecipes.Last()!.Former && oldRecipes.Last()!.Where.EndsWith(", in an earlier version")
            && Swaps.All(s => Has(s.owner, s.newDeck))
            && oldSave.MatchWins.OrderBy(w => w.Key).SequenceEqual(winsBefore) && oldSave.Dp == dpOld
            && Enumerable.Range(0, swapped.All.Count).Where(i => swapped.IsCleared(i, oldSave)).SequenceEqual(clearedBefore) && clearedBefore.Contains(4) && clearedBefore.Contains(6)
            && idempotent && reloadKeeps && ngKeeps && noWinsGained.SequenceEqual(doneKeys.OrderBy(u => u))
            && !RecipeBook.Unlocked(swapped, noWins).Any(r => r.Former) && plainSave.Unlocks.Count == plainUnlocks);

        // 7b. New Game+ clears match wins (and can start any time): a cycle-1 save that beat those duelists keeps their old recipes.
        Profile Cycled(bool beaten)
        {
            var p = Fresh();
            p.NewGamePlus();
            if (beaten) foreach (var s in Swaps) p.Wins[s.owner] = 1;
            return p;
        }
        List<string> FormersGained(Profile p)
        {
            var had = p.Unlocks.ToHashSet();
            swapped.Migrate(p);
            return p.Unlocks.Except(had).Where(u => u.StartsWith(RecipeBook.FormerPrefix)).OrderBy(u => u).ToList();
        }
        var cycledBeaten = FormersGained(Cycled(true));
        var cycledNever = FormersGained(Cycled(false));
        var seenOnly = Cycled(false);
        seenOnly.Seen.Add("recipe:" + RecipeBook.Key(Swaps[2].owner, Swaps[2].oldDeck));
        var seenGained = FormersGained(seenOnly);
        var freshCycle0 = Fresh();
        freshCycle0.Wins[Swaps[2].owner] = 1; // beaten elsewhere, not at the match: cycle 0 still has its match wins, so no
        var cycle0Gained = FormersGained(freshCycle0);
        Check($"7b. a cycle-1 save with empty MatchWins that beat them gains [{string.Join(", ", cycledBeaten)}]; one that never did +[{string.Join(", ", cycledNever)}]; "
              + $"one that opened Yummy's recipe +[{string.Join(", ", seenGained)}]; a cycle-0 save without the match win +[{string.Join(", ", cycle0Gained)}]",
            cycledBeaten.SequenceEqual(Swaps.Select(s => FormerOf(s.owner, s.oldDeck)).OrderBy(u => u)) && cycledNever.Count == 0
            && seenGained.SequenceEqual(new[] { FormerOf(Swaps[2].owner, Swaps[2].oldDeck) }) && cycle0Gained.Count == 0);

        // 7c. Each swap is applied once, whenever it arrives: a save with the first build's gate counts today's three as done; a
        // fourth "was:" line added later is applied on the next load, and only it.
        var gated = Fresh();
        for (int ci = 0; ci <= 6; ci++)
            for (int mi = 0; mi < swapped.All[ci].Matches.Count; mi++) gated.MatchWins[Profile.MatchKey(ci, mi)] = 1;
        gated.Unlocks.Add(Circuits.ClimbMigrated);
        gated.Unlocks.Add(FormerOf("spellbook", "spellbook")); // what the first build granted it
        var gatedBefore = gated.Unlocks.ToHashSet();
        swapped.Migrate(gated);
        var gatedFormers = gated.Unlocks.Except(gatedBefore).Where(u => u.StartsWith(RecipeBook.FormerPrefix)).ToList();
        var fourthLine = "was: 1 frog-ftk classic";
        var later = Circuits.Parse(swappedText.Replace("was: 3 spellbook spellbook", "was: 3 spellbook spellbook\n" + fourthLine), roster);
        var laterBefore = oldSave.Unlocks.ToHashSet();
        later.Migrate(oldSave);
        var laterGained = oldSave.Unlocks.Except(laterBefore).OrderBy(u => u).ToList();
        var laterOnce = oldSave.Unlocks.OrderBy(u => u).ToList();
        later.Migrate(oldSave);
        var fourthDone = Circuits.FormerDoneKey(later.All[4], 0, later.All[4].Former.First(f => f.Was.Character.Id == "frog-ftk").Was);
        Check($"7c. a save with the old gate {Circuits.ClimbMigrated} gets no former recipe again (+[{string.Join(", ", gatedFormers)}], still {string.Join(", ", gated.Unlocks.Where(u => u.StartsWith(RecipeBook.FormerPrefix)))}); "
              + $"a fourth was: line later applies only itself (+[{string.Join(", ", laterGained)}]), once",
            gatedFormers.Count == 0 && gated.Unlocks.Contains(FormerOf("spellbook", "spellbook"))
            && laterGained.SequenceEqual(new[] { FormerOf("frog-ftk", "classic"), fourthDone }.OrderBy(u => u)) && oldSave.Unlocks.OrderBy(u => u).SequenceEqual(laterOnce));

        // 7d. A recipe kept through a former key isn't "unlocked" again by the Tag Cup match that still plays it.
        var tagHolder = Fresh();
        tagHolder.MatchWins[Profile.MatchKey(4, 2)] = 1; // won old 4.2 (Sage's Spellbook) before the swap
        swapped.Migrate(tagHolder);
        int sageCup = -1, sageMatch = -1;
        for (int t = 0; t < swapped.TagCups.Count && sageCup < 0; t++)
            for (int m = 0; m < swapped.TagCups[t].Matches.Count; m++)
                if (new[] { swapped.TagCups[t].Matches[m].lead, swapped.TagCups[t].Matches[m].partner }.Any(x => x.Character.Id == "spellbook" && x.Deck == "spellbook")) { sageCup = t; sageMatch = m; break; }
        var tagNews = new List<Recipe>();
        if (sageCup >= 0)
        {
            swapped.RecordTag(tagHolder, sageCup, sageMatch, 0);
            tagNews = RecipeBook.JustUnlockedTag(swapped, tagHolder, sageCup, sageMatch);
        }
        var fresher = Fresh();
        if (sageCup >= 0) swapped.RecordTag(fresher, sageCup, sageMatch, 0);
        var freshNews = sageCup >= 0 ? RecipeBook.JustUnlockedTag(swapped, fresher, sageCup, sageMatch) : new List<Recipe>();
        Check($"7d. a save holding Sage's Spellbook through {FormerOf("spellbook", "spellbook")} wins {(sageCup >= 0 ? swapped.TagCups[sageCup].Name : "?")} match {sageMatch + 1}: "
              + $"newly unlocked [{string.Join(", ", tagNews.Select(r => $"{r.Owner.Name}'s {r.Deck}"))}]; a save without it [{string.Join(", ", freshNews.Select(r => $"{r.Owner.Name}'s {r.Deck}"))}]",
            sageCup >= 0 && tagHolder.Unlocks.Contains(FormerOf("spellbook", "spellbook")) && !tagNews.Any(r => r.Owner.Id == "spellbook" && r.Deck == "spellbook")
            && freshNews.Any(r => r.Owner.Id == "spellbook" && r.Deck == "spellbook"));

        // 8. Real saves (copies only; the step reports when none is there): each loads, and migrating on the campaign with its
        // "was:" lines (against the same file without them) adds exactly the former recipes of the swapped matches it won (or,
        // in New Game+, whose duelists it beat) and the swaps' done marks; nothing else changes; a second Migrate and a reload neither.
        var saves = new[] { Path.Combine(root, "build", "climb", "save-copy"), Path.Combine(root, "build", "climb-e", "saves") }
            .Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "profile*.txt", SearchOption.AllDirectories)).OrderBy(f => f).ToList();
        if (saves.Count == 0) Console.WriteLine("  info climb: 8. no save copies under build/climb/save-copy or build/climb-e/saves: the real-save step didn't run");
        foreach (var path in saves)
        {
            var name = Path.GetRelativePath(root, path);
            var before = Profile.Load(path);
            var after = Profile.Load(path);
            if (before == null || after == null) { Check($"8. {name} loads", false); continue; }
            stripped.Migrate(before);
            swapped.Migrate(after);
            var added = after.Unlocks.Except(before.Unlocks).OrderBy(u => u).ToList();
            bool legacy = before.Unlocks.Contains(Circuits.ClimbMigrated);
            var expectedAdded = swapKeys.Where(k => !legacy && !before.Unlocks.Contains(k.done)
                    && (after.HasWon(k.ci, k.mi) || (after.Cycle >= 1 && (after.WinsAgainst(k.owner) > 0 || after.Seen.Contains("recipe:" + RecipeBook.Key(k.owner, k.oldDeck))))))
                .Select(k => FormerOf(k.owner, k.oldDeck))
                .Concat(swapKeys.Where(k => !legacy && !before.Unlocks.Contains(k.done)).Select(k => k.done))
                .Where(k => !before.Unlocks.Contains(k)).Distinct().OrderBy(u => u).ToList();
            var wonSwaps = swapKeys.Where(k => after.HasWon(k.ci, k.mi)).Select(k => $"{k.ci}.{k.mi}").ToList();
            int cleared = swapped.ClearedCount(after);
            var once = after.Unlocks.OrderBy(u => u).ToList();
            swapped.Migrate(after);
            var copy = Path.Combine(Path.GetTempPath(), $"ygo-climb-save-{Guid.NewGuid():N}.txt");
            after.Save(copy);
            var back = Profile.Load(copy)!;
            File.Delete(copy);
            File.Delete(copy + ".bak");
            swapped.Migrate(back);
            Check($"8. {name}: {cleared} circuits cleared, {after.MatchWins.Count} match wins (swapped ones won: {string.Join(" ", wonSwaps)}), {after.Dp:N0} DP, cycle {after.Cycle}; "
                  + $"gains [{string.Join(", ", added.Where(u => u.StartsWith(RecipeBook.FormerPrefix)))}] and {added.Count(u => u.StartsWith("wasdone:"))} done marks; nothing else changes",
                cleared == stripped.ClearedCount(before) && after.MatchWins.OrderBy(w => w.Key).SequenceEqual(before.MatchWins.OrderBy(w => w.Key)) && after.Dp == before.Dp
                && added.SequenceEqual(expectedAdded) && after.Unlocks.OrderBy(u => u).SequenceEqual(once)
                && back.Unlocks.OrderBy(u => u).SequenceEqual(once) && back.MatchWins.OrderBy(w => w.Key).SequenceEqual(after.MatchWins.OrderBy(w => w.Key)) && back.Dp == after.Dp
                && before.Unlocks.All(after.Unlocks.Contains) && after.Seen.SetEquals(before.Seen) && after.Exp == before.Exp && after.Tokens == before.Tokens);
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

    /// <summary>
    /// circuits.txt with the spec's three swaps applied (each "match: X old" becomes "match: X new" plus its "was:" line); a
    /// swap already in the text is left as it is, and a changed match without its "was:" line gets it.
    /// </summary>
    public static string Swapped(string text)
    {
        var lines = text.Split('\n').ToList();
        foreach (var (owner, oldDeck, newDeck, number) in Swaps)
        {
            var was = $"was: {number} {owner} {oldDeck}";
            if (lines.Any(l => l.Trim() == was)) continue;
            int at = lines.FindIndex(l => l.Trim() == $"match: {owner} {oldDeck}");
            if (at >= 0) lines[at] = $"match: {owner} {newDeck}";
            else at = lines.FindIndex(l => l.Trim() == $"match: {owner} {newDeck}");
            if (at >= 0) lines.Insert(at + 1, was);
        }
        return string.Join("\n", lines);
    }
}
