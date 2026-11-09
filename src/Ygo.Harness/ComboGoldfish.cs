using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Campaign;
using Ygo.Engine.Protocol;
using Ygo.Engine.Replays;

namespace Ygo.Harness;

/// <summary>
/// `--combo-goldfish deck [--games N] [--save-replay]` (docs/dev/HEADLINERS-2026-11-01.md): a headliner's combo against a
/// dummy that only sets a monster each turn, half the games going first. How often its route starts by its second and third
/// turns, finishes (reaches its payoff), on which of its turns, and how often it wins that turn. One game prints the bot's
/// decisions and the route's steps. Targets: starts by the third turn 50%+, finishes 55-75% (40% is the go line).
/// With --save-replay, the best finished duel (won on the payoff turn, soonest) is saved as the headliner's Watch the combo
/// clip (<see cref="HeadlinerReplays"/>: campaign/replays/&lt;id&gt;.txt), after a check that it plays back the same.
/// </summary>
public static class ComboGoldfish
{
    /// <summary>One duel's route story: our own turn numbers (1 = our first turn; 0 = never).</summary>
    public sealed class Game
    {
        public bool First;
        public int Seed;
        public string Route = "";
        public int Started, PaidOff, WonOn, Turns, Winner = -1;
        public bool Finished, Fizzled;
        /// <summary>Payoffs that landed with the meter under the route's minimum (<see cref="MinAtPayoff"/>): not a finish.</summary>
        public int ShortPayoffs;
        /// <summary>How the first route the headliner started went: 0 never started, 1 paid off, 2 fizzled (the show's fizzle).</summary>
        public int FirstTry;
        public int OpponentLp = 8000;
        /// <summary>The route's meter at its last signal, and what it counts.</summary>
        public int Count;
        public string Counter = "";
        /// <summary>Every answer given, for a replay.</summary>
        public List<byte[]> Responses = new List<byte[]>();
    }

    /// <summary>
    /// The least the route's meter must read when its payoff lands for it to count as finished (0: any). Return Rook's payoff
    /// is two or more monsters home (<see cref="ReturnRookPlaybook.Need"/>): a Return that brings back fewer isn't the show.
    /// </summary>
    public static int MinAtPayoff(Playbook playbook) => playbook is ReturnRookPlaybook ? ReturnRookPlaybook.Need : 0;

    /// <summary>The headliner's deck file, loaded with its name (decks/**/&lt;name&gt;.ydk), or null.</summary>
    public static Deck? LoadDeck(string root, string deckName)
    {
        var file = Directory.GetFiles(Path.Combine(root, "decks"), deckName + ".ydk", SearchOption.AllDirectories).FirstOrDefault();
        if (file == null) return null;
        var deck = Deck.LoadYdk(file);
        deck.Name = deckName;
        return deck;
    }

    /// <summary>The goldfish dummy's deck (decks/classic.ydk).</summary>
    public static Deck Dummy(string root) => Deck.LoadYdk(Path.Combine(root, "decks", "classic.ydk"));

    /// <summary>
    /// One duel: the headliner (seat 0, the playbook its deck plays by) against <paramref name="opponent"/> (default the
    /// dummy) with the opponent's deck and any cards placed for it first; game <paramref name="g"/> of seed set
    /// <paramref name="seed"/> (seed·7919+g; even games the headliner goes first). <paramref name="watch"/> sees the headliner's
    /// playbook and the duel's context before it starts (a ComboWatch, a signal hook).
    /// </summary>
    public static Game Play(ICardDataProvider cards, IScriptProvider scripts, Deck deck, Deck opponentDeck, int g, int seed,
        Func<int, DuelContext, IDuelist>? opponent = null, IEnumerable<PlacedCard>? setup = null, Action<string>? log = null,
        Action<DuelRunner, DuelContext>? attach = null, Action<Playbook, DuelContext>? watch = null)
    {
        var game = new Game { First = g % 2 == 0, Seed = seed * 7919 + g };
        int s = game.Seed;
        var recipe = new DuelRecipe
        {
            Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)s, FirstPlayer = game.First ? 0 : 1 },
            Deck0 = deck.Clone(), Deck1 = opponentDeck.Clone(),
        };
        if (setup != null) recipe.Setup.AddRange(setup);
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var playbook = Playbooks.ForDeck(deck)!;
        int min = MinAtPayoff(playbook);
        int Own(int turn) => game.First ? (turn + 1) / 2 : turn / 2;
        playbook.RouteSignaled += signal =>
        {
            log?.Invoke($"   [{signal.Kind}] {signal.Route}: step {signal.Step + 1} of {signal.Steps} {signal.StepName}"
                        + (signal.Counter.Length > 0 ? $" ({signal.Counter} {signal.Count})" : "")
                        + (signal.Kind == Playbook.RouteEvent.Ended ? (signal.Finished ? " (finished)" : " (stuck)") : ""));
            if (signal.Counter.Length > 0) { game.Counter = signal.Counter; game.Count = signal.Count; }
            switch (signal.Kind)
            {
                case Playbook.RouteEvent.Started:
                    if (game.Started == 0) { game.Started = Own(signal.Turn); game.Route = signal.Route; }
                    break;
                case Playbook.RouteEvent.Payoff:
                    if (signal.Count < min) { game.ShortPayoffs++; break; }
                    if (game.PaidOff == 0) game.PaidOff = Own(signal.Turn);
                    if (game.FirstTry == 0) game.FirstTry = 1;
                    break;
                case Playbook.RouteEvent.Ended:
                {
                    bool landed = signal.Finished && signal.Count >= min;
                    if (landed && game.PaidOff == 0) game.PaidOff = game.Started;
                    if (!landed) game.Fizzled = true;
                    if (game.FirstTry == 0) game.FirstTry = landed ? 1 : 2;
                    break;
                }
            }
        };
        watch?.Invoke(playbook, context);
        var answers = new ResponseLog();
        var bot = new SmartDuelist("Headliner", s, context, log: log, playbook: playbook);
        var them = opponent?.Invoke(s + 1, context) ?? new DummyDuelist(s + 1, cards);
        var runner = new DuelRunner(duel, bot, them, answers);
        runner.MessageReceived += context.Observe;
        YataLock.Attach(runner, duel);
        attach?.Invoke(runner, context);
        var result = runner.Run(40000);
        game.Finished = game.PaidOff > 0;
        game.OpponentLp = context.Board.Players[1].LifePoints;
        game.Winner = result.Winner;
        game.Turns = result.Turns;
        if (result.Winner == 0) game.WonOn = Own(result.Turns);
        game.Responses = answers.Responses.ToList();
        return game;
    }

    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, string deckName, int games, int seed, bool saveReplay = false)
    {
        var deck = LoadDeck(root, deckName);
        if (deck == null) { Console.WriteLine($"no deck {deckName}"); return 1; }
        var dummy = Dummy(root);
        if (Playbooks.ForDeck(deck) == null) { Console.WriteLine($"{deckName} has no playbook"); return 1; }
        bool verbose = games == 1;
        var results = new List<Game>();
        for (int g = 0; g < games; g++)
        {
            var game = Play(cards, scripts, deck, dummy, g, seed, log: verbose ? Console.WriteLine : null);
            if (!saveReplay) game.Responses.Clear();
            results.Add(game);
            if (verbose)
                Console.WriteLine($"\n{(game.First ? "first" : "second")}: route {(game.Started > 0 ? $"{game.Route} on its turn {game.Started}" : "never started")}, "
                                  + $"{(game.Finished ? $"payoff on its turn {game.PaidOff}" : "no payoff")}, {(game.Winner == 0 ? $"won on its turn {game.WonOn}" : $"winner {game.Winner}")} "
                                  + $"(duel turn {game.Turns}, opponent at {game.OpponentLp} LP){(game.Counter.Length > 0 ? $"; meter: {game.Count} {game.Counter}" : "")}");
        }
        if (!verbose)
        {
            Console.WriteLine($"{deckName}: {games} goldfish games, half going first");
            Summarize(results);
        }
        if (saveReplay) return SaveBest(cards, scripts, root, deckName, deck, dummy, results);
        return 0;
    }

    /// <summary>The goldfish table: starts, finishes, the payoff turn and wins, for all games and by who went first; then each route.</summary>
    public static void Summarize(List<Game> results)
    {
        foreach (var (label, set) in new[] { ("all", results), ("first", results.Where(r => r.First).ToList()), ("second", results.Where(r => !r.First).ToList()) })
        {
            if (set.Count == 0) continue;
            double Pct(Func<Game, bool> f) => 100.0 * set.Count(f) / set.Count;
            var paid = set.Where(r => r.Finished).ToList();
            Console.WriteLine($"  {label,-7} starts by its turn 2 {Pct(r => r.Started is > 0 and <= 2),3:0}% (turn 3 {Pct(r => r.Started is > 0 and <= 3),3:0}%)"
                              + $"   finishes {Pct(r => r.Finished),3:0}% (by its turn 3 {Pct(r => r.PaidOff is > 0 and <= 3),3:0}%)"
                              + $"   payoff on its turn {(paid.Count > 0 ? paid.Average(r => r.PaidOff) : 0):0.0}"
                              + $"   wins on the payoff turn {Pct(r => r.Finished && r.WonOn == r.PaidOff),3:0}%"
                              + $"   wins by its turn 3 {Pct(r => r.WonOn is > 0 and <= 3),3:0}%   wins {Pct(r => r.WonOn > 0),3:0}%");
        }
        foreach (var route in results.Where(r => r.Started > 0).GroupBy(r => r.Route).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  route {route.Key}: started {route.Count()}, finished {route.Count(r => r.Finished)}");
        var stuck = results.Where(r => r.Started > 0 && !r.Finished).ToList();
        if (stuck.Count > 0) Console.WriteLine($"  stuck routes left the opponent at {stuck.Average(r => r.OpponentLp):0} LP on average");
        int shortPaid = results.Count(r => r.ShortPayoffs > 0);
        if (shortPaid > 0) Console.WriteLine($"  payoffs under the meter's minimum (not finishes): {results.Sum(r => r.ShortPayoffs)} in {shortPaid} duels");
        int past40 = results.Count(r => r.Turns > TurnCap.AlleyTurns);
        Console.WriteLine($"  duels past turn {TurnCap.AlleyTurns}: {past40} of {results.Count} ({100.0 * past40 / Math.Max(1, results.Count):0.0}%); "
                          + $"not won: {results.Count(r => r.Winner != 0)}");
    }

    /// <summary>
    /// The Watch the combo clip: the finished duel won on its payoff turn, soonest (then the shortest), saved as a replay
    /// once it plays back to the same end. Returns 1 when no duel qualifies or the replay doesn't hold.
    /// </summary>
    private static int SaveBest(ICardDataProvider cards, IScriptProvider scripts, string root, string deckName, Deck deck, Deck dummy, List<Game> results)
    {
        var best = results.Where(r => r.Finished && r.Winner == 0 && r.WonOn == r.PaidOff)
            .OrderBy(r => r.PaidOff).ThenBy(r => r.Turns).ThenByDescending(r => r.Count).FirstOrDefault()
            ?? results.Where(r => r.Finished && r.Winner == 0).OrderBy(r => r.PaidOff).ThenBy(r => r.Turns).FirstOrDefault();
        if (best == null) { Console.WriteLine("  no finished duel to save"); return 1; }
        var data = AlleyData.Load(Path.Combine(root, "campaign", AlleyData.FileName), cards);
        var handle = data.FindHeadliner(deckName)?.Handle ?? deckName;
        var replay = new Replay
        {
            Settings = new DuelSettings { Seed = (ulong)best.Seed, FirstPlayer = best.First ? 0 : 1 },
            Decks = new[] { deck.Clone(), dummy.Clone() },
            Names = new[] { handle, "The stand-in" },
            Characters = new[] { deckName, "alley-stand-in" },
            DeckNames = new[] { deckName, "classic" },
            Responses = best.Responses, Winner = best.Winner, Turns = best.Turns, CardCount = cards.All.Count(),
        };
        // It must play back the same: the core is deterministic, so the answers alone reproduce the duel.
        var loaded = Replay.FromCode(replay.ToCode())!;
        using (var duel = loaded.Recipe(cards, scripts).Create())
        {
            var feed = new ReplayFeed(loaded.Responses);
            var runner = new DuelRunner(duel, new ReplayDuelist(feed), new ReplayDuelist(feed));
            YataLock.Attach(runner, duel);
            var played = runner.Run(40000);
            if (played.Winner != best.Winner || played.Turns != best.Turns)
            {
                Console.WriteLine($"  the clip didn't play back the same (winner {played.Winner} vs {best.Winner}, turns {played.Turns} vs {best.Turns}): not saved");
                return 1;
            }
        }
        var path = HeadlinerReplays.PathFor(Path.Combine(root, "campaign"), deckName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        replay.Save(path);
        Console.WriteLine($"  saved the Watch the combo clip: {Path.GetRelativePath(root, path)} (seed {best.Seed}, {(best.First ? "first" : "second")}, "
                          + $"{best.Route}, payoff on its turn {best.PaidOff}, won on duel turn {best.Turns}{(best.Counter.Length > 0 ? $", {best.Count} {best.Counter}" : "")})");
        return 0;
    }
}
