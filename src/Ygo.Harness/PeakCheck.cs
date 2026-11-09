using System.Diagnostics;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;
using Ygo.Engine.Protocol;

namespace Ygo.Harness;

/// <summary>
/// `--peak-check &lt;pilot|all&gt; [--games N] [--seed N]` (docs/dev/NO-LIMITS-SPEC-2026-11-02.md 5.1 rule 9 and 14.3 chunk 3): per No
/// Limits build (the build: block of alley-events.txt, whether or not it is ready), the playbook's gates:
/// <list type="bullet">
/// <item><b>Use rate:</b> for each restored card and each pick, the share of the games it was drawn (it was in the build's hand)
/// in which the build activated it, summoned it, set it, or spent it as a cost or material. Extra Deck cards are never drawn:
/// their rate is over all games. At least <see cref="UseTarget"/> (60%, [P]) is the gate.</item>
/// <item><b>Gain over base:</b> the build against its base deck, both at full strength (planner and playbook), N duels with the first
/// turn alternating: the win rate as an Elo gap, beside the ratings' (+100 to +250 is the window).</item>
/// <item><b>Speed against the base:</b> in those duels, the build's average winning turn against the base's, and the share of
/// duels each wins by turn 2; and both decks goldfished (the turn they win going first against a dummy that only sets).</item>
/// </list>
/// Each build plays N duels against its base, N against the reference (the highest-rated deck legal under the No Limits list) and N
/// against the next build in the file: the use rate is over all of them.
/// </summary>
public static class PeakCheck
{
    /// <summary>[P] The use gate: a restored card or pick is used in at least this share of the games it is drawn.</summary>
    public const double UseTarget = 0.60;

    private const uint ReasonMaterial = 0x8, ReasonCost = 0x80;

    /// <summary>
    /// A duel that hasn't ended after this many prompts is stopped and reported (a stalled prompt, not a result). A duel
    /// played to the turn cap takes about 1,500-3,000 (3,056 the most of Lacrima's 180 at --games 60); the planner
    /// replays the duel for each decision, so a loop gets slower as it goes,
    /// and the endless test's 20,000 would take hours here (the hung Lacrima duel of 2026-11-06).
    /// </summary>
    public const int PromptCap = 8000;

    private sealed class Game
    {
        public int Winner = -1, Turns, Prompts, Seed;
        /// <summary>Stopped at <see cref="PromptCap"/>; decided by the turn cap (turn 41 never starts: the higher LP wins).</summary>
        public bool Stopped, AtTurnCap;
        public bool BuildFirst;
        public HashSet<uint> Drawn = new HashSet<uint>(), Used = new HashSet<uint>();
    }

    private static Game Play(ICardDataProvider cards, IScriptProvider scripts, int gameSeed, bool buildFirst, Deck mine, Deck theirs, Personality styleMine, Personality styleTheirs,
        bool trace = false)
    {
        var game = new Game { BuildFirst = buildFirst, Seed = gameSeed };
        var recipe = new DuelRecipe
        {
            Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)gameSeed, FirstPlayer = buildFirst ? 0 : 1 }, Deck0 = mine.Clone(), Deck1 = theirs.Clone(),
        };
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var answers = new ResponseLog();
        var bookMine = Playbooks.ForDeck(mine);
        var bookTheirs = Playbooks.ForDeck(theirs);
        PromptTrace? t0 = null, t1 = null;
        IDuelist p0 = new SmartDuelist(styleMine.Name, gameSeed * 2, context, trace ? s => t0!.Note(s) : null, styleMine, bookMine, new TurnPlanner(recipe, answers, styleMine, () => Playbooks.ForDeck(mine)));
        IDuelist p1 = new SmartDuelist(styleTheirs.Name, gameSeed * 2 + 1, context, trace ? s => t1!.Note(s) : null, styleTheirs, bookTheirs, new TurnPlanner(recipe, answers, styleTheirs, () => Playbooks.ForDeck(theirs)));
        if (trace) { p0 = t0 = new PromptTrace(p0, cards, "build"); p1 = t1 = new PromptTrace(p1, cards, "opponent"); }
        // --keep-replays: the duel as a replay, with how each seat's bot was built (the seats shown to its watcher, answers untouched).
        var kept = ReplayKeeper.Current?.Begin("peak", $"s{gameSeed}-{mine.Name}-vs-{theirs.Name}", recipe, duel, answers, new[] { styleMine.Name, styleTheirs.Name },
            new[] { SeatSpec.Of(0, styleMine, mine, gameSeed * 2, true), SeatSpec.Of(1, styleTheirs, theirs, gameSeed * 2 + 1, true) }, turnCap: true);
        var runner = new DuelRunner(duel, ReplayKeeper.Wrap(kept, p0), ReplayKeeper.Wrap(kept, p1), answers);
        if (trace) t0!.Where = t1!.Where = () => $"[t{runner.Result.Turns} p{runner.Result.Prompts:00000}]";
        uint Base(uint code) => Banlist.BaseCode(code, cards);
        runner.MessageReceived += message =>
        {
            context.Observe(message);
            var r = new MessageReader(message, 1);
            switch (message[0])
            {
                case Msg.Draw:
                {
                    var player = r.U8();
                    var count = r.U32();
                    for (int i = 0; i < count; i++) { var code = r.U32(); r.U32(); if (player == 0 && code != 0) game.Drawn.Add(Base(code)); }
                    break;
                }
                case Msg.Move:
                {
                    var code = r.U32();
                    var from = r.LocInfo(code);
                    var to = r.LocInfo(code);
                    var reason = r.AtEnd ? 0u : r.U32();
                    if (code == 0) break;
                    if (to.Controller == 0 && (to.Location & 0x7f) == Location.Hand) game.Drawn.Add(Base(code));
                    if (from.Controller == 0 && ((from.Location & 0x7f) is Location.Hand or Location.MZone or Location.SZone) && (reason & (ReasonCost | ReasonMaterial)) != 0) game.Used.Add(Base(code));
                    break;
                }
                case Msg.Summoning:
                case Msg.SpSummoning:
                case Msg.FlipSummoning:
                {
                    var card = r.LocInfo(r.U32());
                    if (card.Controller == 0 && card.Code != 0) game.Used.Add(Base(card.Code));
                    break;
                }
                case Msg.Set:
                {
                    var card = r.LocInfo(r.U32());
                    if (card.Controller == 0 && card.Code != 0) game.Used.Add(Base(card.Code));
                    break;
                }
                case Msg.Chaining:
                {
                    var code = r.U32();
                    var card = r.LocInfo(code);
                    var controller = r.U8();
                    if (controller == 0 && code != 0) game.Used.Add(Base(code));
                    break;
                }
            }
        };
        YataLock.Attach(runner, duel);
        TurnCap.Attach(runner, duel);
        ReplayKeeper.Attach(kept, runner);
        var result = runner.Run(PromptCap);
        ReplayKeeper.Current?.Finish(kept, result);
        game.Winner = result.Winner;
        game.Turns = result.Turns;
        game.Prompts = result.Prompts;
        game.Stopped = result.Winner < 0;
        game.AtTurnCap = result.Reason == DuelRunner.ReasonTurnCap;
        if (trace)
        {
            Console.WriteLine($"  seed {gameSeed}: winner {result.Winner}, turn {result.Turns}, prompts {result.Prompts}, retries {result.Retries}");
            foreach (var line in t0!.Last.Concat(t1!.Last).OrderBy(l => l, StringComparer.Ordinal).TakeLast(40)) Console.WriteLine("    " + line);
        }
        return game;
    }

    /// <summary>The turn a deck wins on (going first or second) against a dummy that only sets a monster each turn; 0 if it doesn't by turn 20.</summary>
    private static int Goldfish(ICardDataProvider cards, IScriptProvider scripts, Deck deck, Deck dummy, int seed, bool first)
    {
        var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)seed, FirstPlayer = first ? 0 : 1 }, Deck0 = deck.Clone(), Deck1 = dummy.Clone() };
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var runner = new DuelRunner(duel, new SmartDuelist("A", seed, context, playbook: Playbooks.ForDeck(deck)), new DummyDuelist(seed + 1, cards));
        runner.MessageReceived += context.Observe;
        var result = runner.Run(6000);
        return result.Winner == 0 && result.Turns <= 20 ? result.Turns : 0;
    }

    private static double EloGap(double p) => 400 * Math.Log10(Math.Clamp(p, 0.02, 0.98) / (1 - Math.Clamp(p, 0.02, 0.98)));

    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, string who, int games, int seed)
    {
        var timer = Stopwatch.StartNew();
        games = Math.Max(2, games);
        var printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities")).ToList();
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var library = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), roster, circuits);
        var a = AlleyTest.Load(cards, root, printings, roster, library);
        var data = a.Data;
        var ctx = a.Ctx;
        var list = Alley.NoLimitsList(cards);
        var builds = data.Builds.Where(b => b.Deck.Length > 0 && ctx.LoadDeck(b.Deck) != null && (who == "all" || b.Id == who || b.Deck == who)).ToList();
        if (builds.Count == 0)
        {
            Console.WriteLine($"no No Limits build matches \"{who}\" (builds with a deck: {string.Join(", ", data.Builds.Where(b => ctx.LoadDeck(b.Deck) != null).Select(b => b.Id))})");
            return 1;
        }
        // The reference: the highest-rated deck of deck-ratings.txt legal under the No Limits list as built (the study's).
        var roster0 = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName));
        var reference = roster0.OrderByDescending(r => r.Value).ThenBy(r => r.Key, StringComparer.Ordinal).Select(r => (name: r.Key, elo: r.Value, deck: ctx.LoadDeck(r.Key)))
            .First(x => x.deck != null && DeckRules.Validate(NoSide(x.deck!), list, cards).Count == 0);
        var dummy = Deck.LoadYdk(Path.Combine(root, "decks", "classic.ydk"));
        var late = new Personality { Id = "late-cpu", Name = "CPU", Skill = 1.0 };
        var all = data.Builds.Where(b => b.Deck.Length > 0 && ctx.LoadDeck(b.Deck) != null).ToList();
        Console.WriteLine($"Peak check: {builds.Count} build(s), {games} duels against each of three opponents a build (its base, the reference {reference.name} {reference.elo:0}, the next build), the first turn alternating, "
            + $"both sides at full strength (planner and playbook); use gate {UseTarget:P0} of the games a card is drawn; seed {seed}\n");
        int failures = 0;
        var summary = new List<string>();
        foreach (var build in builds)
        {
            var deck = ctx.LoadDeck(build.Deck)!;
            var baseDeck = ctx.LoadDeck(build.Base);
            var pilot = roster.FirstOrDefault(p => p.Id == build.Id)?.Copy() ?? new Personality { Id = build.Id, Name = build.Handle, Skill = 1.0 };
            pilot.Skill = 1.0;
            var next = all[(all.IndexOf(build) + 1) % all.Count];
            var opponents = new List<(string label, Deck deck)>();
            if (baseDeck != null) opponents.Add(($"its base {build.Base}", baseDeck));
            opponents.Add(($"the reference {reference.name}", reference.deck!));
            if (next != build) opponents.Add(($"{next.Handle}'s {next.Deck}", ctx.LoadDeck(next.Deck)!));
            // PEAK_REPLAY=<opponent>:<duel>[,...]: replay those duels of this build alone with a prompt trace (opponent 0 is the base, 1 the reference, 2 the next build).
            if (Environment.GetEnvironmentVariable("PEAK_REPLAY") is { Length: > 0 } replay)
            {
                foreach (var word in replay.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = word.Split(':');
                    int ro = int.Parse(parts[0]), rg = int.Parse(parts[1]);
                    Console.WriteLine($"replay {build.Id} against {opponents[ro].label}, duel {rg}:");
                    Play(cards, scripts, seed * 100003 + builds.IndexOf(build) * 10007 + ro * 1009 + rg, rg % 2 == 0, deck, opponents[ro].deck, pilot, late, trace: true);
                }
                continue;
            }
            var jobs = opponents.SelectMany((o, oi) => Enumerable.Range(0, games).Select(g => (oi, g))).ToList();
            var results = new Game[jobs.Count];
            Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, k =>
            {
                var (oi, g) = jobs[k];
                results[k] = Play(cards, scripts, seed * 100003 + builds.IndexOf(build) * 10007 + oi * 1009 + g, g % 2 == 0, deck, opponents[oi].deck, pilot, late);
            });
            var rated = ctx.Ratings.TryGetValue(build.Deck, out var er) ? er : 0;
            var baseRated = baseDeck != null && roster0.TryGetValue(build.Base, out var br) ? br : ctx.Ratings.TryGetValue(build.Base, out var br2) ? br2 : 0;
            Console.WriteLine($"{build.Deck} ({build.Handle}, {build.Id}; base {build.Base}; list {build.List}; wave {build.Wave}; {(build.Ready ? "ready" : "ready: no")}; rated {rated:0}, base {baseRated:0}, +{rated - baseRated:0} by the ratings):");
            // Duels that never ended (the prompt cap) are stalls: each one fails the check, with its seed to replay (PEAK_REPLAY).
            int stopped = results.Count(g => g.Stopped), atCap = results.Count(g => g.AtTurnCap);
            for (int k = 0; k < results.Length; k++)
                if (results[k].Stopped)
                    Console.WriteLine($"  STOP against {opponents[jobs[k].oi].label}, duel {jobs[k].g} (PEAK_REPLAY={jobs[k].oi}:{jobs[k].g}, seed {results[k].Seed}): no end after {PromptCap} prompts, turn {results[k].Turns}");
            Console.WriteLine($"  {(stopped == 0 ? "ok  " : "OFF ")} {results.Length} duels: {stopped} stopped at the prompt cap ({PromptCap}), {atCap} decided at turn {TurnCap.AlleyTurns}, "
                + $"most prompts in a finished duel {results.Where(g => !g.Stopped).Select(g => g.Prompts).DefaultIfEmpty(0).Max()}");
            failures += stopped;

            // The use rates, over every game.
            var deckCodes = deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, cards)).ToList();
            var extra = new HashSet<uint>(deck.Extra.Select(c => Banlist.BaseCode(c, cards)));
            var tracked = build.Restored.Select(c => (code: c, kind: "restored")).Concat(build.Picks.Select(c => (code: c, kind: "pick"))).Distinct().ToList();
            int low = 0;
            foreach (var (code, kind) in tracked)
            {
                string name = cards.Get(code)?.Name ?? code.ToString();
                int copies = deckCodes.Count(c => c == code);
                if (copies == 0)
                {
                    Console.WriteLine($"  LOW  {kind,-8} {name} x0: not in the deck ({(kind == "pick" ? "an over: pick the deck doesn't run" : "a restored card the deck doesn't run")})");
                    low++;
                    continue;
                }
                bool isExtra = extra.Contains(code);
                int drawn = results.Count(g => isExtra || g.Drawn.Contains(code));
                int used = results.Count(g => g.Used.Contains(code) && (isExtra || g.Drawn.Contains(code)));
                double rate = used / (double)Math.Max(1, drawn);
                bool ok = drawn > 0 && rate >= UseTarget;
                if (!ok) low++;
                Console.WriteLine($"  {(ok ? "ok  " : "LOW ")} {kind,-8} {name} x{copies}: {(isExtra ? "Extra Deck, over all" : "drawn in")} {drawn} of {results.Length} games, used in {used}: {rate:P0} (gate {UseTarget:P0})");
            }
            failures += low;

            // Gain over base and speed against it (the build against its base).
            if (baseDeck != null)
            {
                var vsBase = results.Where((g, k) => jobs[k].oi == 0).ToList();
                int won = vsBase.Count(g => g.Winner == 0), lost = vsBase.Count(g => g.Winner == 1);
                double p = won / (double)Math.Max(1, won + lost);
                double se = Math.Sqrt(p * (1 - p) / Math.Max(1, won + lost));
                var buildWins = vsBase.Where(g => g.Winner == 0).ToList();
                var baseWins = vsBase.Where(g => g.Winner == 1).ToList();
                double avgBuild = buildWins.Count == 0 ? 0 : buildWins.Average(g => g.Turns), avgBase = baseWins.Count == 0 ? 0 : baseWins.Average(g => g.Turns);
                double by2Build = vsBase.Count(g => g.Winner == 0 && g.Turns <= 2) / (double)Math.Max(1, vsBase.Count), by2Base = vsBase.Count(g => g.Winner == 1 && g.Turns <= 2) / (double)Math.Max(1, vsBase.Count);
                double gain = EloGap(p);
                var gfa = new (int, int, int, int)[games];
                Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, g =>
                    gfa[g] = (Goldfish(cards, scripts, deck, dummy, seed * 31 + g, true), Goldfish(cards, scripts, baseDeck, dummy, seed * 31 + g, true),
                              Goldfish(cards, scripts, deck, dummy, seed * 37 + g, false), Goldfish(cards, scripts, baseDeck, dummy, seed * 37 + g, false)));
                var gf = gfa.ToList();
                double Avg(IEnumerable<int> t) => t.Where(x => x > 0).DefaultIfEmpty(0).Average();
                bool gainOk = gain >= 100 && gain <= 250, speedOk = avgBuild == 0 || (avgBuild >= 3 && (avgBase == 0 || avgBase - avgBuild <= 1)), early = by2Build <= Math.Max(0.10, by2Base + 0.05);
                Console.WriteLine($"  gain over base: won {p:P0} of {won + lost} against {build.Base} (± {se:P0}): {gain:+0;-0} Elo measured (window +100 to +250: {(gainOk ? "inside" : "OUTSIDE")}; rated +{rated - baseRated:0})");
                Console.WriteLine($"  speed against the base: average winning turn {avgBuild:0.0} for the build, {avgBase:0.0} for the base ({avgBuild - avgBase:+0.0;-0.0}); wins by turn 2 {by2Build:P1} against the base's {by2Base:P1} "
                    + $"(at most max(10%, base + 5 points): {(early ? "ok" : "OVER")}); average winning turn at least 3 and at most 1 faster than the base: {(speedOk ? "ok" : "OFF")}");
                Console.WriteLine($"  goldfish (the turn each wins against a dummy that only sets, {games} games each): going first build {Avg(gf.Select(x => x.Item1)):0.0} / base {Avg(gf.Select(x => x.Item2)):0.0}; "
                    + $"going second build {Avg(gf.Select(x => x.Item3)):0.0} / base {Avg(gf.Select(x => x.Item4)):0.0}; wins by turn 2 going second build {gf.Count(x => x.Item3 is > 0 and <= 2) * 100.0 / games:0}% / base {gf.Count(x => x.Item4 is > 0 and <= 2) * 100.0 / games:0}%");
                if (!speedOk || !early) failures++;
                summary.Add($"{build.Id,-8} uses {tracked.Count - low}/{tracked.Count} at {UseTarget:P0}+; gain {gain:+0;-0} Elo (window +100..+250); avg win turn {avgBuild:0.0} vs {avgBase:0.0}; by turn 2 {by2Build:P0} vs {by2Base:P0}");
            }
            // Against the others (for the use rates' sake): the win rates.
            for (int oi = 1; oi < opponents.Count; oi++)
            {
                var vs = results.Where((g, k) => jobs[k].oi == oi).ToList();
                int w = vs.Count(g => g.Winner == 0), l = vs.Count(g => g.Winner == 1);
                Console.WriteLine($"  against {opponents[oi].label}: won {w / (double)Math.Max(1, w + l):P0} of {w + l}");
            }
            Console.WriteLine();
        }
        Console.WriteLine("Summary:");
        foreach (var line in summary) Console.WriteLine("  " + line);
        Console.WriteLine($"\n{(failures == 0 ? "ok  " : "OFF ")} {failures} use rate(s) under {UseTarget:P0}, cards missing from their deck, speed gates missed, or duels stopped at the prompt cap   ({timer.Elapsed.TotalMinutes:0.0} min)");
        return failures == 0 ? 0 : 1;
    }

    private static Deck NoSide(Deck d)
    {
        var c = d.Clone();
        c.Side.Clear();
        return c;
    }
}
