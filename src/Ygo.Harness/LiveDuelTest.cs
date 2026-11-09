using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;

namespace Ygo.Harness;

/// <summary>
/// `--campaign-test`'s live-duel checks (Profile.LiveDuel, <see cref="LiveDuels"/>): a save closed mid-duel settles at load
/// as that kind's loss, with exactly what a real loss does to the save, once. Per kind (a circuit match, a Tag Cup match, a
/// guest one on one and in a tag duel, a Library cup match, the champion's table, an "Every Era" round, a Locals game with
/// a loaner), on one scripted save a level short of a level-up: the record written, the save reloaded as a relaunch reads
/// it, <see cref="LiveDuels.Settle"/>, against the same save after the real loss (the kind's Record call with winner 1, then
/// <see cref="CardUnlocks.AfterDuel"/>, as DuelController does): the two saves must be identical, line for line. Then the
/// save line itself: it round-trips, a finished duel leaves none, an old save without it loads (an old live Locals match
/// still replays), New Game+ clears it, and a circuit migration moves it.
/// </summary>
public static class LiveDuelTest
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
        Console.WriteLine("\nDuels left mid-way (the live-duel record):");
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        Deck? LoadDeck(string name) => deckFiles.TryGetValue(name, out var f) ? Deck.LoadYdk(f) : null;
        var unlocks = new CardUnlocks(cards, printings, circuits, roster, BanPasses.LoadNoPass(Path.Combine(root, "campaign", "no-pass.txt"), cards),
            LoadDeck, catalog, BountyBoard.Load(Path.Combine(root, "campaign", BountyBoard.FileName)), guests, library);
        var ratings = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName));
        string NameOf(string id) => roster.FirstOrDefault(p => p.Id == id)?.Name ?? id;

        // The scripted save: the first Library cup cleared (its Locals open), DP for the entry, one EXP short of a level.
        var basis = AlleyTest.Opened(starter, cards, printings, library, 0x11E0D001);
        basis.Earn(Ledger.Debug, 5000);
        var (into, needed) = Leveling.Progress(basis.Exp);
        Leveling.AddExp(basis, needed - into - 1);
        var basisText = AlleyTest.SaveText(basis);
        Profile Fresh() => AlleyTest.FromText(basisText, printings, cards);
        int tableCup = library.Cups.ToList().FindIndex(c => c.Matches.Any(m => m.Champion));
        int champion = tableCup < 0 ? -1 : library.Cups[tableCup].Matches.FindIndex(m => m.Champion);
        var tagGuest = guests.All.First();

        // Each kind: its record, how DuelController records a real loss of it, and who the notice names.
        var kinds = new List<(string name, LiveDuel live, Action<Profile> realLoss, string opponent)>
        {
            ("a circuit match", new LiveDuel { Kind = LiveDuels.Circuit, Event = 0, Match = 1 }, p =>
            {
                var r = circuits.Record(p, 0, 1, 1, loaner: false);
                library.Open(p, circuits);
                unlocks.AfterDuel(p, r, circuits.All[0].Band, null, false, tag: false);
            }, circuits.All[0].Matches[1].Character.Name),
            ("a Tag Cup match", new LiveDuel { Kind = LiveDuels.TagCup, Event = 0, Match = 2 }, p =>
                unlocks.AfterDuel(p, circuits.RecordTag(p, 0, 2, 1, loaner: false), "", null, false, tag: true),
                $"{circuits.TagCups[0].Matches[2].lead.Character.Name} & {circuits.TagCups[0].Matches[2].partner.Character.Name}"),
            ("a guest", new LiveDuel { Kind = LiveDuels.Guest, Guest = guests.All[1].Character.Id }, p =>
                unlocks.AfterDuel(p, GuestList.Record(p, circuits, guests.All[1], false, 1, loaner: false), "", null, false, tag: false),
                guests.All[1].Character.Name),
            ("a guest's tag duel", new LiveDuel { Kind = LiveDuels.Guest, Guest = tagGuest.Character.Id, Tag = true }, p =>
                unlocks.AfterDuel(p, GuestList.Record(p, circuits, tagGuest, true, 1, loaner: false), "", null, false, tag: true),
                $"{tagGuest.Character.Name} & {tagGuest.Partner.Name}"),
            ("a Library cup match", new LiveDuel { Kind = LiveDuels.Cup, Event = 1, Match = 0 }, p =>
                unlocks.AfterDuel(p, library.Record(p, 1, 0, 1, loaner: false, built: true, deck: "classic"), "", null, false, tag: false),
                library.Cups[1].Matches[0].Character.Name),
            ("the champion's table", new LiveDuel { Kind = LiveDuels.Cup, Event = tableCup, Match = champion }, p =>
                unlocks.AfterDuel(p, library.Record(p, tableCup, champion, 1, loaner: false, built: true, deck: "classic"), "", null, false, tag: false),
                library.Cups[Math.Max(0, tableCup)].Matches[Math.Max(0, champion)].Character.Name),
            ("an \"Every Era\" round", new LiveDuel { Kind = LiveDuels.Round, Event = 0 }, p =>
                unlocks.AfterDuel(p, library.RecordFinal(p, 0, 1, "classic"), "", null, false, tag: false),
                library.Rounds[0].Match.Character.Name),
        };
        Check($"the scripted save: {kinds.Count} kinds besides Locals, a champion's table ({library.Cups[Math.Max(0, tableCup)].Name}), one EXP short of level {basis.Level + 1}",
            champion >= 0 && guests.All.Count > 1 && library.Rounds.Count > 0 && Leveling.LevelFor(basis.Exp + 1) == basis.Level + 1);

        foreach (var (name, live, realLoss, opponent) in kinds)
        {
            // The record written at Duel! (saved), then the app gone: the save as a relaunch reads it.
            var quit = Fresh();
            quit.LiveDuel = live;
            var relaunched = AlleyTest.FromText(AlleyTest.SaveText(quit), printings, cards);
            bool saved = relaunched.LiveDuel is LiveDuel back && back.Kind == live.Kind && back.Event == live.Event && back.Match == live.Match
                         && back.Guest == live.Guest && back.Tag == live.Tag;
            int dp0 = relaunched.Dp, played0 = relaunched.DuelsPlayed;
            var settled = LiveDuels.Settle(relaunched, circuits, library, guests, unlocks, null, NameOf);
            var settledText = AlleyTest.SaveText(relaunched);
            var again = LiveDuels.Settle(relaunched, circuits, library, guests, unlocks, null, NameOf);
            var real = Fresh();
            realLoss(real);
            var realText = AlleyTest.SaveText(real);
            bool same = settledText == realText;
            string diff = same ? "" : string.Join(" | ", settledText.Split('\n').Except(realText.Split('\n')).Concat(realText.Split('\n').Except(settledText.Split('\n')).Select(l => "real: " + l)).Take(4));
            Check($"{name} left live settles at load as its loss (\"{settled?.Summary}\"): the save line for line as after a real loss{(same ? "" : $" ({diff})")}, "
                  + $"+{relaunched.Dp - dp0} DP, a level-up, once",
                saved && settled != null && same && !settledText.Contains("liveduel") && relaunched.LiveDuel == null && again == null
                && settled.Summary.StartsWith($"You left your match against {opponent} mid-duel. That counts as a loss. +{Circuits.LossDp} DP for the duel")
                && relaunched.Dp > dp0 && relaunched.DuelsPlayed == played0 + 1 && settled.Reward.LevelUps.Count == 1
                // Its scenes as its results screen plays them (a guest's plays no level scene there).
                && settled.Events.Contains("level:" + (basis.Level + 1)) == (live.Kind != LiveDuels.Guest) && Ledger.Net(relaunched) - Ledger.Net(Fresh()) == relaunched.Dp - dp0);
        }

        // A Locals game, played with a loaner: lost in the bracket (as a concession: out, the placement paid at half), not replayed.
        var locals = Fresh();
        var lb = Brackets.EnterLocals(locals, library, 0, ratings, "classic")!;
        Brackets.StartYours(lb);
        int m = lb.LiveMatch;
        locals.LiveDuel = new LiveDuel { Kind = LiveDuels.Locals, Match = m, Loaner = true };
        var localsText = AlleyTest.SaveText(locals);
        var them = NameOf(lb.Slots[lb.Opponent(m)].Character);
        var crashed = AlleyTest.FromText(localsText, printings, cards);
        var gameLeft = LiveDuels.Settle(crashed, circuits, library, guests, unlocks, null, NameOf);
        var crashedText = AlleyTest.SaveText(crashed);
        var realGame = AlleyTest.FromText(localsText, printings, cards);
        var realResult = Brackets.RecordYours(realGame, library, realGame.Bracket!, 1, loaner: true)!;
        unlocks.AfterDuel(realGame, realResult.Reward, "", null, true, tag: false);
        realGame.LiveDuel = null;
        var localsSame = crashedText == AlleyTest.SaveText(realGame);
        var cb = crashed.Bracket;
        Check($"a Locals game left live loses it in the bracket (\"{gameLeft?.Summary}\"): out, the placement paid at half for the loaner, never replayed; "
              + "the save as after a real loss, once",
            gameLeft != null && localsSame && gameLeft.Bracket?.Out == true && cb != null && cb.YouOut && cb.LiveMatch == -1 && cb.Loaned
            && gameLeft.Summary.StartsWith($"You left your match against {them} mid-duel. That counts as a loss.") && gameLeft.Summary.Contains("half: loaner deck")
            && crashed.LiveDuel == null && LiveDuels.Settle(crashed, circuits, library, guests, unlocks, null, NameOf) == null);

        // Finished: the game's result recorded, the record cleared with it. Settling a record whose game was recorded anyway
        // (a save written between the two) changes nothing: never twice.
        var finished = AlleyTest.FromText(localsText, printings, cards);
        Brackets.RecordYours(finished, library, finished.Bracket!, 0, loaner: true);
        var beforeStale = AlleyTest.SaveText(finished);
        var stale = LiveDuels.Settle(finished, circuits, library, guests, unlocks, null, NameOf);
        bool staleDropped = stale == null && finished.LiveDuel == null
                            && AlleyTest.SaveText(finished) == string.Join("\n", beforeStale.Split('\n').Where(l => !l.StartsWith("liveduel")));
        var won = Fresh();
        won.LiveDuel = new LiveDuel { Kind = LiveDuels.Circuit, Event = 0, Match = 0 };
        circuits.Record(won, 0, 0, 0);
        won.LiveDuel = null;
        var wonBack = AlleyTest.RoundTrip(won, printings, cards);
        Check("a finished duel leaves no record (its result's save has no liveduel line); a Locals record whose game was already recorded settles nothing",
            !AlleyTest.SaveText(won).Contains("liveduel") && wonBack.LiveDuel == null && staleDropped);

        // Old saves, New Game+, and a circuit list that moved.
        var old = AlleyTest.FromText(basisText, printings, cards);
        var oldLocals = AlleyTest.FromText(string.Join("\n", localsText.Split('\n').Where(l => !l.StartsWith("liveduel"))), printings, cards);
        var oldLeft = LiveDuels.Settle(oldLocals, circuits, library, guests, unlocks, null, NameOf);
        Check("an old save without the line loads with nothing live; an old save's live Locals match (no record) is left to replay",
            !basisText.Contains("liveduel") && old.LiveDuel == null && LiveDuels.Settle(old, circuits, library, guests, unlocks) == null
            && oldLeft == null && oldLocals.Bracket?.LiveMatch == m && oldLocals.Bracket.YouOut == false);
        var plus = Fresh();
        plus.LiveDuel = new LiveDuel { Kind = LiveDuels.Guest, Guest = tagGuest.Character.Id, Tag = true, Loaner = true };
        var plusTrip = AlleyTest.RoundTrip(plus, printings, cards);
        bool trips = plusTrip.LiveDuel is { Kind: LiveDuels.Guest, Tag: true, Loaner: true, Event: -1, Match: -1 } t && t.Guest == tagGuest.Character.Id;
        plus.NewGamePlus();
        // A save from before the October 2026 circuits: its live Gauntlet match moves to where the Gauntlet is now.
        int gauntlet = circuits.All.ToList().FindIndex(c => c.Name == Library.OpensAfter);
        var oldLayout = circuits.All.Select(c => c.Name).Where(n => n != "Expert Series: Fall Format" && n != "Master League: October 2026").ToList();
        var moved = Fresh();
        moved.CircuitLayout = oldLayout;
        moved.LiveDuel = new LiveDuel { Kind = LiveDuels.Circuit, Event = oldLayout.IndexOf(Library.OpensAfter), Match = 0 };
        circuits.Migrate(moved);
        Check($"the record round-trips the save (a guest's tag duel with a loaner); New Game+ clears it; a circuit migration moves it with its circuit "
              + $"({Library.OpensAfter}: {oldLayout.IndexOf(Library.OpensAfter)} -> {moved.LiveDuel?.Event})",
            trips && plus.LiveDuel == null && gauntlet >= 0 && moved.LiveDuel?.Event == gauntlet && oldLayout.IndexOf(Library.OpensAfter) != gauntlet);
        return failures;
    }
}
