using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;

namespace Ygo.Harness;

/// <summary>
/// `--campaign-test`'s checks for the 2026-11-05 bug batch (docs/dev/BUGS-BUILD-2026-11-05.md, agent E), run from
/// <see cref="LedgerCheck.Run"/>: a Library cup's Format Master stamp counts only a deck of yours (no loaner, no
/// partner's edit: the final's rule, one predicate), and Gus's bounty board neither shows nor pays before the Beginner
/// Circuit is cleared (its gus-lessons scene puts it up), while saves past the clear keep it.
/// </summary>
public static class CampaignGates
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
        Profile Fresh()
        {
            var p = Profile.New(starter, cards, printings);
            p.Collection.Salt = 20261105;
            p.Collection.CommonestOf = c => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
            return p;
        }

        // ---- The Format Master stamp and partner edits. ----
        Console.WriteLine("\nThe Format Master stamp: a deck of yours, the final's rule:");
        var yours = Deck.ParseYdk("#campaign\n#title My Warriors\n#main\n" + string.Join("\n", starter.Main) + "\n#extra\n!side\n", "my-warriors");
        var edit = Deck.ParseYdk("#campaign\n#title Vera's Warriors (your edit)\n#recipe vera-warriors\n#partner vera\n#main\n" + string.Join("\n", starter.Main) + "\n#extra\n!side\n",
            "Vera's Warriors (your edit)");
        var custom = yours.Clone();
        custom.Name = "My Warriors (custom loaner)";
        custom.Notes["loaner"] = "vera-warriors";
        var loaner = yours.Clone();
        loaner.Name = Loaners.NameFor("vera-warriors");
        Check($"a partner's edit reads its note (\"{Loaners.PartnerEditOf(edit)}\") and isn't yours; a loaner and a custom loaner aren't; your own deck is",
            Loaners.PartnerEditOf(edit) == "vera" && Loaners.PartnerEditOf(yours) == "" && !Loaners.IsYours(edit) && !Loaners.IsYours(custom)
            && !Loaners.IsYours(loaner) && Loaners.IsYours(yours));
        Check("the stamp counts your own deck with no Ban Passes, never a partner's edit, a loaner, a custom loaner, Ban Passes or an unsaved deck",
            Library.CountsForStamp(yours, 0) && !Library.CountsForStamp(yours, 1) && !Library.CountsForStamp(edit, 0) && !Library.CountsForStamp(custom, 0)
            && !Library.CountsForStamp(loaner, 0) && !Library.CountsForStamp(null, 0));
        // A cup won match by match with a partner's edit: cleared, but no stamp; then every match again with your own deck: stamped.
        var cupper = Fresh();
        cupper.LibraryOpen = true;
        int cup = 0, matches = library.Cups[cup].Matches.Count;
        var editRewards = Enumerable.Range(0, matches)
            .Select(m => library.Record(cupper, cup, m, 0, loaner: Loaners.IsLoanerDeck(edit), built: Library.CountsForStamp(edit, 0), deck: edit.Name)).ToList();
        bool editCleared = library.IsCleared(cup, cupper), editStamped = library.HasStamp(cup, cupper) || editRewards.Any(r => r.StampedCup != null);
        var ownRewards = Enumerable.Range(0, matches)
            .Select(m => library.Record(cupper, cup, m, 0, loaner: false, built: Library.CountsForStamp(yours, 0), deck: yours.Name)).ToList();
        Check($"{library.Cups[cup].Name} won with a partner's edit: cleared, no Format Master stamp; won again with your own deck: stamped once (+{Library.StampTokens} token)",
            editCleared && !editStamped && library.HasStamp(cup, cupper) && ownRewards.Count(r => r.StampedCup != null) == 1);

        // ---- Gus's bounty board. ----
        Console.WriteLine("\nGus's bounty board opens at the Beginner Circuit's clear:");
        var story = Story.Load(Path.Combine(root, "campaign", "story.txt"));
        var intro = story.Scenes.FirstOrDefault(s => s.Id == BountyBoard.IntroScene);
        int gate = BountyBoard.GateCircuit(circuits);
        Check($"story.txt's {BountyBoard.IntroScene} scene introduces the board on \"{intro?.Trigger}\", the clear of circuit {gate} ({(gate >= 0 ? circuits.All[gate].Name : "none")})",
            intro != null && intro.Trigger == "clear:" + BountyBoard.OpensAfter && intro.Lines.Any(l => l.Text.Contains("bounty board"))
            && gate == 0 && circuits.All[gate].Name == BountyBoard.OpensAfter);

        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        Deck? LoadDeck(string name) => deckFiles.TryGetValue(name, out var f) ? Deck.LoadYdk(f) : null;
        var noPass = BanPasses.LoadNoPass(Path.Combine(root, "campaign", "no-pass.txt"), cards);
        var board = BountyBoard.Load(Path.Combine(root, "campaign", BountyBoard.FileName));
        var unlocks = new CardUnlocks(cards, printings, circuits, roster, noPass, LoadDeck, catalog, board, guests, library);
        // A duel that meets every Beginner bounty: you go first, take no damage, a Normal Monster finishes on turn 5.
        DuelTally Perfect() => new DuelTally(cards) { FirstPlayer = 0, Turns = 5, Winner = 0, Finisher = DuelTally.Finish.Normal };
        List<Grant> Win(Profile p, int match)
        {
            var reward = circuits.Record(p, gate, match, 0);
            return unlocks.AfterDuel(p, reward, circuits.All[gate].Band, Perfect(), false, tag: false)
                .Where(g => g.Kind is GrantKind.Bounty or GrantKind.BountyToken).ToList();
        }
        int beginnerBounties = board.For(circuits.All[gate].Band).Count;
        var hunter = Fresh();
        var firstDuel = Win(hunter, 0);
        bool closedBefore = !BountyBoard.IsOpen(hunter, circuits) && !unlocks.BountiesOpen(hunter) && unlocks.Board(hunter).Count == 0 && unlocks.OpenBounties(hunter) == 0;
        Check($"a fresh save's first duel (a perfect win at {circuits.All[gate].Name}) pays no bounty ({firstDuel.Count}), and the board shows none ({unlocks.Board(hunter).Count} rows, "
              + $"{unlocks.OpenBounties(hunter)} open, open: {BountyBoard.IsOpen(hunter, circuits)})",
            firstDuel.Count == 0 && closedBefore && !hunter.Unlocks.Any(u => u.StartsWith("bounty")));
        var clearing = new List<Grant>();
        for (int m = 1; m < circuits.All[gate].Matches.Count; m++) clearing.AddRange(Win(hunter, m));
        bool openAfterClear = circuits.IsCleared(gate, hunter) && BountyBoard.IsOpen(hunter, circuits) && unlocks.Board(hunter).Count == beginnerBounties
                              && unlocks.OpenBounties(hunter) == beginnerBounties;
        var afterClear = Win(hunter, 0);
        Check($"the duels up to the clear pay none ({clearing.Count}: the clearing duel comes before the scene); then the board is up ({unlocks.Board(hunter).Count} rows) "
              + $"and the next perfect win pays its {afterClear.Count} bounties ({string.Join(" / ", afterClear.Select(g => g.Text))})",
            clearing.Count == 0 && openAfterClear && afterClear.Count == beginnerBounties && unlocks.OpenBounties(hunter) == 0);

        // The clearing duel recorded by another Circuits instance (a reload, a tool): still no bounty, the gate goes by name.
        var otherCircuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var elsewhere = Fresh();
        var elsewhereGrants = new List<Grant>();
        DuelReward? clearedElsewhere = null;
        for (int m = 0; m < otherCircuits.All[gate].Matches.Count; m++)
        {
            var reward = otherCircuits.Record(elsewhere, gate, m, 0);
            if (reward.ClearedCircuit != null) clearedElsewhere = reward;
            elsewhereGrants.AddRange(unlocks.AfterDuel(elsewhere, reward, otherCircuits.All[gate].Band, Perfect(), false, tag: false)
                .Where(g => g.Kind is GrantKind.Bounty or GrantKind.BountyToken));
        }
        Check($"a clearing duel recorded by a second Circuits instance (not the board's) still pays no bounty ({elsewhereGrants.Count}); the gate compares the cleared circuit by name",
            !ReferenceEquals(otherCircuits.All[gate], circuits.All[gate]) && clearedElsewhere?.ClearedCircuit == otherCircuits.All[gate] && elsewhereGrants.Count == 0
            && !BountyBoard.IsOpen(elsewhere, circuits, clearedElsewhere) && !BountyBoard.IsOpen(elsewhere, otherCircuits, clearedElsewhere)
            && BountyBoard.IsOpen(elsewhere, circuits) && BountyBoard.IsOpen(elsewhere, otherCircuits));

        // Saves past the clear keep the board: the clear itself, the scene seen (an old save whose circuits moved), New Game+.
        var past = Fresh();
        for (int m = 0; m < circuits.All[gate].Matches.Count; m++) past.MatchWins[Profile.MatchKey(gate, m)] = 1;
        var path = Path.Combine(Path.GetTempPath(), $"ygo-bounties-{Guid.NewGuid():N}.txt");
        past.Save(path);
        var loaded = Profile.Load(path)!;
        File.Delete(path);
        var seenOnly = Fresh();
        seenOnly.Seen.Add(BountyBoard.IntroScene);
        var cycled = Fresh();
        for (int m = 0; m < circuits.All[gate].Matches.Count; m++) cycled.MatchWins[Profile.MatchKey(gate, m)] = 1;
        cycled.Seen.Add(BountyBoard.IntroScene);
        cycled.NewGamePlus();
        var cycledNoScene = Fresh();
        cycledNoScene.Cycle = 1;
        Check("a save past the clear keeps its board (loaded from disk), and so do a save that saw the scene and New Game+ (the circuits start over)",
            BountyBoard.IsOpen(loaded, circuits) && unlocks.Board(loaded).Count == beginnerBounties && BountyBoard.IsOpen(seenOnly, circuits)
            && BountyBoard.IsOpen(cycled, circuits) && !circuits.IsCleared(gate, cycled) && BountyBoard.IsOpen(cycledNoScene, circuits) && !BountyBoard.IsOpen(Fresh(), circuits));
        return failures;
    }
}
