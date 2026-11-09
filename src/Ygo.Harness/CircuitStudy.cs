using System.Diagnostics;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Campaign;
using Ygo.Engine.DeckBuilding;
using Ygo.Engine.Interactive;
using Ygo.Engine.Protocol;

namespace Ygo.Harness;

/// <summary>
/// `--circuit-study --only text --option deck [--games N] [--seed N] [--era-limited] [--you-chain auto] [--you-seat app]
/// [--seeds a-b --first them --match N]`: how a deck does at each match of the circuits whose
/// name has that text, played as the QA stand-in plays it (the roster's best planner that knows its deck, with the turn
/// planner) against the CPUs as the game makes them. Your deck is the recipe borrowed as a loaner: as built, or with
/// --era-limited made legal for the circuit's ban list (the starter if --option is empty). Even seeds have you first, odd seeds second, and every match plays
/// the same seeds, so two builds can be compared duel by duel: each match prints its results as a string of W and L.
/// `--write file` merges each match's result into a toss-advice file (campaign/toss-advice.txt, the WallAdvice format keyed
/// by <see cref="Profile.MatchKey"/>): the coin toss's tip for a player bringing that deck there.
/// `--seeds a-b` plays exactly those duel seeds (an app duel's seed is in its log: "duel 90: You vs ..."), `--first them`
/// or `you` sets who starts, and `--match N` plays only that match; each duel prints how it ended, to compare with the app.
/// </summary>
public static class CircuitStudy
{
    private const string Starter = "mekk-knight-invoked";

    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, int games, int seed, string only, string deckName, bool eraLimited = false, string write = "",
        bool autoChain = false, bool appSeat = false, int[]? seeds = null, int first = -1, int onlyMatch = 0)
    {
        var timer = Stopwatch.StartNew();
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities"));
        var circuits = Circuits.Load(StudyFiles.CircuitsPath(root), roster);
        var lists = Banlist.LoadFolders(new[] { Path.Combine(root, "vendor", "LFLists"), Path.Combine(root, "lflists") });
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        Deck Load(string name) { var d = Deck.LoadYdk(deckFiles[name]); d.Name = name; return d; }
        var you = roster.Where(p => p.PlansAhead && p.KnowsDeck).OrderByDescending(p => p.Skill).First();
        if (deckName.Length == 0) deckName = Starter;
        Console.WriteLine($"Circuit study: {deckName}, {games} duels per match, you play as {you.Name} (skill {you.Skill:0.00}), seed {seed}"
            + (autoChain ? ", chain prompts as Chain: Auto asks" : "") + (appSeat ? ", seated as the app's QA play" : ""));

        var advice = write.Length > 0 ? WallAdvice.Load(write) : WallAdvice.Empty;
        foreach (var circuit in circuits.All.Where(c => c.Name.Contains(only, StringComparison.OrdinalIgnoreCase)))
        {
            int circuitIndex = circuits.All.ToList().IndexOf(circuit);
            var list = eraLimited && circuit.Banlist.Length > 0 ? lists.FirstOrDefault(l => l.Name == circuit.Banlist) ?? Banlist.None : Banlist.None;
            var mine = deckName == Starter ? Load(deckName) : Loaners.Build(Load(deckName), list, cards);
            mine.Name = deckName;
            Console.WriteLine($"\n{circuit.Name} (list {(list != Banlist.None ? list.Name : "none")}; your deck {mine.Main.Count} + {mine.Extra.Count})");
            for (int m = 0; m < circuit.Matches.Count; m++)
            {
                var match = circuit.Matches[m];
                if (onlyMatch > 0 && m + 1 != onlyMatch) continue;
                if (seeds != null) games = seeds.Length;
                var results = new char[games];
                var endings = new string[games];
                Interlocked.Exchange(ref _rollouts, 0);
                Interlocked.Exchange(ref _diverged, 0);
                Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, g =>
                {
                    int gameSeed = seeds != null ? seeds[g] : seed * 1000 + g;
                    try
                    {
                        var (won, ending) = PlayOne(cards, scripts, gameSeed, first >= 0 ? first : g % 2, you, mine, match.Character, Load(match.Deck), autoChain, appSeat);
                        results[g] = won ? 'W' : 'L';
                        endings[g] = $"    seed {gameSeed}: {(won ? "won" : "lost")} {ending}";
                    }
                    catch (Exception ex) { results[g] = 'x'; Console.WriteLine($"    seed {gameSeed}: {ex.Message}"); }
                });
                // Who started each duel: alternating by seed, or --first for all of them.
                int StartedBy(int g) => first >= 0 ? first : g % 2;
                int Won(int parity) => Enumerable.Range(0, games).Count(g => StartedBy(g) == parity && results[g] == 'W');
                int Played(int parity) => Enumerable.Range(0, games).Count(g => StartedBy(g) == parity);
                string Pct(int a, int b) => b == 0 ? "-" : $"{a * 100 / b}%";
                int w = Won(0) + Won(1);
                Console.WriteLine($"  match {m + 1}: {$"{match.Character.Name}, {match.Deck}",-44} won {Pct(w, games),4}  first {Pct(Won(0), Played(0)),4}  second {Pct(Won(1), Played(1)),4}  {new string(results)}"
                    + $"  (your planner: {_diverged} of {_rollouts} rollouts diverged)");
                if (seeds != null) foreach (var line in endings) Console.WriteLine(line);
                if (write.Length > 0)
                    advice.Merge(Profile.MatchKey(circuitIndex, m), new[] { new MeasuredOption
                    {
                        Deck = deckName == Starter ? MeasuredOption.YourDeck : deckName, Percent = w * 100 / games,
                        YouFirst = Won(0) * 100 / Math.Max(1, Played(0)), YouSecond = Won(1) * 100 / Math.Max(1, Played(1)), Games = games,
                    } });
            }
        }
        if (write.Length > 0)
            advice.Save(write, "Measured by `--circuit-study` (src/Ygo.Harness/CircuitStudy.cs): a deck at a circuit match, wins in 100,\n"
                + "first= and second= by who took the first turn (the QA stand-in's play, the CPUs as the game makes them).\n"
                + "The coin toss's tip for a player bringing that deck there (not the Stuck? advice; the CPUs never read it).");
        Console.WriteLine($"\n({timer.Elapsed.TotalMinutes:0.0} min)");
        return 0;
    }

    private static long _rollouts, _diverged;

    private static IDuelist Bot(Personality style, string deckName, int salt, DuelContext context, DuelRecipe recipe, ResponseLog answers)
    {
        if (!style.PlansAhead)
            return new SmartDuelist(style.Name, salt, context, personality: style, playbook: style.KnowsDeck ? Playbooks.ForDeck(deckName) : null);
        return new SmartDuelist(style.Name, salt, context, personality: style, playbook: Playbooks.ForDeck(deckName),
            planner: new TurnPlanner(recipe, answers, style, () => Playbooks.ForDeck(deckName)));
    }

    private static (bool won, string ending) PlayOne(ICardDataProvider cards, IScriptProvider scripts, int gameSeed, int first, Personality you, Deck mine,
        Personality them, Deck theirs, bool autoChain, bool appSeat)
    {
        var recipe = new DuelRecipe
        {
            Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)gameSeed, FirstPlayer = first },
            Deck0 = mine.Clone(), Deck1 = theirs.Clone(),
        };
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var answers = new ResponseLog();
        // Your planner, for counting its rollouts (the stand-in always plans).
        var planner = new TurnPlanner(recipe, answers, you, () => Playbooks.ForDeck(mine));
        IDuelist yours = appSeat
            ? new AppSeat(context, gameSeed, new SmartDuelist("You", gameSeed * 4 + 5, context, personality: you, playbook: Playbooks.ForDeck(mine), planner: planner))
            : new SmartDuelist(you.Name, gameSeed * 4, context, personality: you, playbook: Playbooks.ForDeck(mine), planner: planner);
        if (autoChain && !appSeat) yours = new WallStudy.AskedOnly(yours, context);
        // --keep-replays: the duel as a replay, with how each seat's bot was built (the seats shown to its watcher, answers untouched).
        var kept = ReplayKeeper.Current?.Begin("circuit", $"s{gameSeed}-{mine.Name}-vs-{theirs.Name}", recipe, duel, answers, new[] { you.Name, them.Name },
            new[] { SeatSpec.Of(0, you, mine, appSeat ? gameSeed * 4 + 5 : gameSeed * 4, true, "", appSeat ? "app-seat" : autoChain ? "asked-only" : "", gameSeed),
                    SeatSpec.Of(1, them, theirs.Name, gameSeed * 4 + 1, them.PlansAhead) });
        var runner = new DuelRunner(duel, ReplayKeeper.Wrap(kept, yours),
            ReplayKeeper.Wrap(kept, Bot(them, theirs.Name, gameSeed * 4 + 1, context, recipe, answers)), answers);
        runner.MessageReceived += context.Observe;
        YataLock.Attach(runner, duel);
        ReplayKeeper.Attach(kept, runner);
        var result = runner.Run(20000);
        ReplayKeeper.Current?.Finish(kept, result);
        Interlocked.Add(ref _rollouts, planner.Rollouts);
        Interlocked.Add(ref _diverged, planner.DivergedRollouts);
        var lp = context.Board.Players;
        return (result.Winner == 0, $"on turn {result.Turns}, LP {Math.Max(0, lp[0].LifePoints)} / {Math.Max(0, lp[1].LifePoints)}");
    }

    /// <summary>
    /// Your seat as the app's QA play has it (`--you-seat app`, DuelController): a <see cref="HumanDuelist"/> with the
    /// game's Chain: Auto and zones to pick, whose shown prompts the stand-in answers; routine prompts answer themselves.
    /// </summary>
    internal sealed class AppSeat : IDuelist
    {
        private readonly HumanDuelist _human;
        private readonly IDuelist _advisor;

        public AppSeat(DuelContext context, int seed, IDuelist advisor)
        {
            _human = new HumanDuelist(context, seed);
            _human.Presenter.AskForZones = true;
            _human.Presenter.Mode = ResponseMode.Auto;
            _advisor = advisor;
        }

        public byte[]? Respond(Prompt prompt, int attempt)
        {
            var answer = _human.Respond(prompt, attempt);
            if (answer != null || _human.Current == null) return answer;
            _human.Submit(_advisor.Respond(prompt, attempt) ?? throw new InvalidOperationException("the stand-in gave no answer"));
            return _human.Respond(prompt, attempt);
        }

        public void OnPhaseChanged() => _advisor.OnPhaseChanged();
    }
}
