using System.Collections.Concurrent;
using System.Diagnostics;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;

namespace Ygo.Harness;

/// <summary>
/// `--rate-decks [--rounds N]`: an Elo rating for every deck in decks/ (historic ones included; decks/alley's authored ones and decks/no-limits' peak builds aren't). Each round pairs the decks
/// at random and plays one duel per pair with the same balanced bot on both sides (who goes first alternates), so the
/// rating measures the deck, not the pilot. Prints decks strongest first, plus turn-1 board size and any engine errors.
/// Each deck's record is also split by going first and going second, and the decks that win clearly more going second
/// are listed (candidates for a playbook's GoesSecond). `--skilled`: both bots at skill 1.0 with the turn planner (how
/// the late campaign's opponents play; much slower).
/// </summary>
public static class DeckRating
{
    private sealed class Entry
    {
        public string Name = "";
        public Deck Deck = new Deck();
        public double Elo = 1500;
        public int Games, Wins, Errors;
        public int FirstGames, FirstWins, SecondGames, SecondWins;
        public long Turns;
    }

    /// <summary>
    /// The deck files rated: every decks/**/*.ydk but decks/alley's, the alley's authored decks (Orvel's, Nyx's), and
    /// decks/no-limits', the peak builds. Both are rated anchored (`--rate-anchored`, PeakRating) into peak-ratings.txt, so
    /// a full re-rate doesn't shift the pool every Elo in the game is measured against (docs/dev/NO-LIMITS-SPEC-2026-11-02.md N4).
    /// </summary>
    public static IEnumerable<string> DeckFiles(string root)
    {
        var skipped = new[] { "alley", "no-limits" }.Select(d => Path.Combine(root, "decks", d) + Path.DirectorySeparatorChar).ToList();
        return Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .Where(f => !skipped.Any(d => f.StartsWith(d, StringComparison.Ordinal)))
            .OrderBy(f => f);
    }

    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, int rounds, int seed, bool skilled = false)
    {
        var entries = DeckFiles(root)
            .Select(f => new Entry { Name = Path.GetFileNameWithoutExtension(f), Deck = Deck.LoadYdk(f) })
            .Where(e => e.Deck.Main.Count >= 40)
            .ToList();
        Console.WriteLine($"Rating {entries.Count} decks over {rounds} rounds ({entries.Count / 2 * rounds} duels)...");
        var rng = new Random(seed);
        var timer = Stopwatch.StartNew();
        for (int round = 0; round < rounds; round++)
        {
            var order = entries.OrderBy(_ => rng.Next()).ToList();
            var pairs = Enumerable.Range(0, order.Count / 2).Select(i => (order[2 * i], order[2 * i + 1], seed + round * 1000 + i)).ToList();
            var results = new ConcurrentBag<(Entry a, Entry b, int winner, int turns, bool error, bool aFirst)>();
            Parallel.ForEach(pairs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, pair =>
            {
                var (a, b, gameSeed) = pair;
                bool aFirst = (gameSeed & 1) == 0;
                int winner = -1, turns = 0;
                bool error = false;
                try
                {
                    var first = aFirst ? a : b;
                    var second = aFirst ? b : a;
                    var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)gameSeed }, Deck0 = first.Deck, Deck1 = second.Deck };
                    using var duel = recipe.Create();
                    var context = new DuelContext(duel, cards);
                    var answers = new ResponseLog();
                    // Each deck as a bot that knows it plays it: its playbook if it has one (and, skilled, the planner).
                    IDuelist Bot(int seat, Entry e)
                    {
                        if (!skilled) return new SmartDuelist($"p{seat}", gameSeed * 2 + seat, context, playbook: Playbooks.ForDeck(e.Deck));
                        var style = new Personality { Name = $"p{seat}", Skill = 1.0 };
                        var planner = new TurnPlanner(recipe, answers, style, () => Playbooks.ForDeck(e.Deck));
                        return new SmartDuelist(style.Name, gameSeed * 2 + seat, context, null, style, Playbooks.ForDeck(e.Deck), planner);
                    }
                    var runner = new DuelRunner(duel, Bot(0, first), Bot(1, second), answers);
                    runner.MessageReceived += context.Observe;
                    var result = runner.Run(6000);
                    turns = result.Turns;
                    // Seat back to deck: 0 = a won, 1 = b won, 2 = draw / unfinished.
                    winner = result.Winner is 0 or 1 ? ((result.Winner == 0) == aFirst ? 0 : 1) : 2;
                }
                catch (Exception ex)
                {
                    if (Environment.GetEnvironmentVariable("RATE_ERRORS") == "1") Console.WriteLine($"  error {a.Name} vs {b.Name} (seed {gameSeed}): {ex.GetType().Name}: {ex.Message}");
                    error = true;
                    winner = 2;
                }
                results.Add((a, b, winner, turns, error, aFirst));
            });
            foreach (var (a, b, winner, turns, error, aFirst) in results)
            {
                a.Games++; b.Games++;
                var (onFirst, onSecond) = aFirst ? (a, b) : (b, a);
                if (!error)
                {
                    onFirst.FirstGames++; onSecond.SecondGames++;
                    if (winner == (aFirst ? 0 : 1)) onFirst.FirstWins++;
                    if (winner == (aFirst ? 1 : 0)) onSecond.SecondWins++;
                }
                a.Turns += turns; b.Turns += turns;
                if (error) { a.Errors++; b.Errors++; }
                double score = winner == 0 ? 1 : winner == 1 ? 0 : 0.5;
                if (winner == 0) a.Wins++;
                if (winner == 1) b.Wins++;
                double expected = 1 / (1 + Math.Pow(10, (b.Elo - a.Elo) / 400));
                // Larger steps early, smaller later, so the order settles.
                double k = round < rounds / 3 ? 32 : 16;
                a.Elo += k * (score - expected);
                b.Elo += k * (expected - score);
            }
            if ((round + 1) % 10 == 0) Console.WriteLine($"  round {round + 1}/{rounds}  ({timer.Elapsed:mm\\:ss})");
        }
        Console.WriteLine();
        int rank = 1;
        foreach (var e in entries.OrderByDescending(e => e.Elo))
            Console.WriteLine($"{rank++,3}. {e.Name,-26} Elo {e.Elo,5:F0}   won {100.0 * e.Wins / Math.Max(1, e.Games),3:F0}% of {e.Games}   avg {e.Turns / (double)Math.Max(1, e.Games),4:F1} turns"
                              + $"   first {Rate(e.FirstWins, e.FirstGames),3:F0}% / second {Rate(e.SecondWins, e.SecondGames),3:F0}%{(e.Errors > 0 ? $"   {e.Errors} errors" : "")}");
        // Going second clearly better: by more than two standard errors of the difference.
        Console.WriteLine();
        Console.WriteLine("Better going second (second minus first, in points; * = beyond two standard errors):");
        foreach (var e in entries.OrderByDescending(e => Rate(e.SecondWins, e.SecondGames) - Rate(e.FirstWins, e.FirstGames)))
        {
            double p1 = Rate(e.FirstWins, e.FirstGames) / 100, p2 = Rate(e.SecondWins, e.SecondGames) / 100;
            double se = Math.Sqrt(p1 * (1 - p1) / Math.Max(1, e.FirstGames) + p2 * (1 - p2) / Math.Max(1, e.SecondGames));
            if (p2 <= p1) break;
            Console.WriteLine($"  {e.Name,-26} {100 * (p2 - p1),+5:F0}  (first {100 * p1:F0}% of {e.FirstGames}, second {100 * p2:F0}% of {e.SecondGames}){(p2 - p1 > 2 * se ? " *" : "")}");
        }
        return 0;

        static double Rate(int wins, int games) => 100.0 * wins / Math.Max(1, games);
    }
}
