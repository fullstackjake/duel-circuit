using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--copies-test`: each card a deck adds is stamped with its deck slot (the core's cover field), so a duel can show
/// which of your copies it is. Checks, for one-on-one and tag duels with either side starting: every stamp names a slot
/// holding that card before the duel starts, and still does for every card of yours wherever it is after a whole bot
/// duel; the deal is the same as without stamps (the shuffle is unchanged); and the looks come back for your deck only.
/// Given a folder of replays recorded before stamps, each plays back to the same winner on the same turn.
/// </summary>
public static partial class CopiesTest
{
    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, int games, int firstSeed, string? replays = null)
    {
        int failures = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk").OrderBy(f => f).ToArray();
        uint[] Locations = { Location.Deck, Location.Hand, Location.MZone, Location.SZone, Location.Grave, Location.Removed, Location.Extra };
        const uint Flags = 0x1 | 0x8 | 0x2000000; // code, type, cover

        for (int g = 0; g < Math.Max(1, games); g++)
        {
            int seed = firstSeed + g;
            bool tag = g % 2 == 1;
            int first = g / 2 % 2;
            var mine = Deck.LoadYdk(deckFiles[g % deckFiles.Length]);
            var theirs = Deck.LoadYdk(deckFiles[(g * 3 + 1) % deckFiles.Length]);
            // Made-up looks: slot k is grade 1 + k % 10 (0 for every 7th slot: a borrowed card with no copy behind it).
            mine.Looks = Enumerable.Range(0, mine.Main.Count + mine.Extra.Count)
                .Select(k => k % 7 == 6 ? default : new CopyLook(Rarity.Common, 1 + k % 10, (uint)k)).ToArray();
            var codes = mine.Main.Concat(mine.Extra).ToList();
            var settings = new DuelSettings { Seed = (ulong)seed, FirstPlayer = first };
            var recipe = tag
                ? TagDuel.Recipe(cards, scripts, settings, mine, Deck.LoadYdk(deckFiles[(g + 5) % deckFiles.Length]), theirs, Deck.LoadYdk(deckFiles[(g + 7) % deckFiles.Length]))
                : new DuelRecipe { Cards = cards, Scripts = scripts, Settings = settings, Deck0 = mine, Deck1 = theirs };
            // Where "mine" went: Deck0 (team 0, duelist 0) or Partner0 (team 0, duelist 1, swapped in later).
            int myDeckId = !tag || first == 0 ? 0 : 2;
            string label = $"seed {seed} {(tag ? "tag" : "1v1")}, {(first == 0 ? "you" : "they")} first, {mine.Name}";

            // The same deal as without stamps: the core's deck order matches a plain shuffle with the same numbers.
            var plain = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = settings, Deck0 = recipe.Deck0.Clone(), Deck1 = recipe.Deck1.Clone() };
            // (A tag partner's deck waits off the board until their first turn, so it's checked after the duel only.)
            if (myDeckId == 0)
            using (var duel = recipe.Create())
            {
                var looks = duel.Looks!;
                var deck = duel.QueryLocation(0, Location.Deck, Flags).Concat(duel.QueryLocation(0, Location.Extra, Flags)).OfType<FieldCard>().ToList();
                var mineNow = deck.Where(c => (c.Cover >> 16 & 0xFF) == myDeckId && (c.Cover & 0xFF000000) == CopyLooks.Tag).ToList();
                bool slotsRight = mineNow.Count == codes.Count && mineNow.All(c => codes[(int)(c.Cover & 0xFFFF) - 1] == c.Code)
                                  && mineNow.Select(c => c.Cover).Distinct().Count() == codes.Count;
                bool looksRight = mineNow.All(c => looks.Of(c) is CopyLook l
                    ? mine.Looks[(int)(c.Cover & 0xFFFF) - 1].Grade == l.Grade
                    : !mine.Looks[(int)(c.Cover & 0xFFFF) - 1].IsCopy);
                bool othersPlain = deck.Where(c => (c.Cover >> 16 & 0xFF) != myDeckId).All(c => looks.Of(c) == null);
                Check($"{label}: before the duel, all {codes.Count} of your cards carry their own slot and look (the other decks none)",
                    slotsRight && looksRight && othersPlain);
            }
            if (!tag)
            {
                // A deck order is what the core will draw from: compare it with and without looks (stamping touches no RNG).
                using var a = recipe.Create();
                plain.Deck0.Looks = null;
                using var b = plain.Create();
                var orderA = a.QueryLocation(0, Location.Deck, 0x1).OfType<FieldCard>().Select(c => c.Code);
                var orderB = b.QueryLocation(0, Location.Deck, 0x1).OfType<FieldCard>().Select(c => c.Code);
                Check($"{label}: the deal is the same with or without looks", orderA.SequenceEqual(orderB));
            }

            // A whole bot duel: every card of yours, wherever it ended up, still names a slot holding its card.
            using (var duel = recipe.Create())
            {
                var context = new DuelContext(duel, cards);
                IDuelist Bot(int seat) => new SmartDuelist($"bot{seat}", seed * 2 + seat, context);
                var result = tag
                    ? new DuelRunner(duel, new TagSeat(0, Bot(0), Bot(2), leadFirst: first == 0), new TagSeat(1, Bot(1), Bot(3), leadFirst: first != 0)).Run()
                    : new DuelRunner(duel, Bot(0), Bot(1)).Run();
                int stamped = 0, wrong = 0, unstamped = 0;
                foreach (var team in new byte[] { 0, 1 })
                    foreach (var location in Locations)
                        foreach (var card in duel.QueryLocation(team, location, Flags).OfType<FieldCard>())
                        {
                            if ((card.Cover & 0xFF000000) != CopyLooks.Tag) { if ((card.Type & CardType.Token) == 0) unstamped++; continue; }
                            if ((card.Cover >> 16 & 0xFF) != myDeckId) continue;
                            stamped++;
                            uint want = codes[(int)(card.Cover & 0xFFFF) - 1];
                            if (card.Code != want && Banlist.BaseCode(card.Code, cards) != Banlist.BaseCode(want, cards)) wrong++;
                        }
                Check($"{label}: after the duel (turn {result.Turns}), {stamped} cards of yours in view still name their slot "
                      + $"({wrong} show another card: code-changing effects; {unstamped} unstamped non-tokens)",
                    stamped > 0 && wrong <= 2 && unstamped <= 2);
            }
        }
        if (replays != null)
        {
            var files = Directory.GetFiles(replays, "*.replay", SearchOption.AllDirectories);
            int same = 0, checkedCount = 0;
            var different = new List<string>();
            foreach (var file in files)
            {
                var replay = Ygo.Engine.Replays.Replay.Load(file);
                if (replay == null || replay.Winner < 0) continue;
                checkedCount++;
                using var duel = replay.Recipe(cards, scripts).Create();
                var feed = new Ygo.Engine.Replays.ReplayFeed(replay.Responses);
                var runner = new DuelRunner(duel, new Ygo.Engine.Replays.ReplayDuelist(feed), new Ygo.Engine.Replays.ReplayDuelist(feed));
                YataLock.Attach(runner, duel);
                try { while (runner.Step() && !(runner.IsWaiting && feed.Finished)) { } }
                catch (Exception ex) { different.Add($"{Path.GetFileName(file)} ({ex.Message})"); continue; }
                if (runner.Result.Winner == replay.Winner && runner.Result.Turns == replay.Turns) same++;
                else different.Add($"{Path.GetFileName(file)} (winner {runner.Result.Winner} turn {runner.Result.Turns}, recorded {replay.Winner} turn {replay.Turns})");
            }
            Check($"{same} of {checkedCount} finished replays recorded before stamps end the same way{(different.Count > 0 ? ": " + string.Join(", ", different.Take(5)) : "")}",
                checkedCount > 0 && same == checkedCount);
        }
        failures += Versions(cards, root);
        Console.WriteLine($"\n{(failures == 0 ? "All copy checks passed." : $"{failures} copy checks failed.")}");
        return failures == 0 ? 0 : 1;
    }
}
