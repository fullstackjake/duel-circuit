using System.Diagnostics;
using System.Text.RegularExpressions;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// Phase 0 for the Back Alley and brackets (POST-GAME-PLAN step 2, "Targets"):
/// <list type="bullet">
/// <item>`--alley-gen [--only text] [--seed N] [--decks K]`: each event's contraband pool and K splash decks (base, what
/// was splashed, what was cut), each checked against the Alley list.</item>
/// <item>`--alley-rate [--only text] [--games N] [--decks K]`: each splash deck against its own base (N bot duels, seats
/// alternating) as an Elo gain, and both goldfished (turn they win against a dummy). Targets: +60 to +150 Elo (over
/// +250 means too many cards); at most 1 turn faster than the base, never under turn 3 on average.</item>
/// <item>`--endless-test [--games N]`: splash decks and the alley's authored decks (the ready tables' authored seats: Orvel's
/// house deck, Nyx's Gravekeeper's Contraband and chaos-yata; the ready headliners') against each other under the Alley
/// list, with the turn cap (<see cref="TurnCap"/>): how many duels hit the prompt cap, are decided at turn 40, or draw,
/// overall and for each authored deck. Targets: under 0.5%, 2% and 1%.</item>
/// <item>`--alley-study [--only table] [--games N] [--option deck] [--load save]`: the QA stand-in (the roster's best
/// planner, knowing its deck) with your best deck and with the starter against each seat of each ready table, as the
/// game builds them (BackAlley's lineups: the regulars of 20 seeded nights, the closer, the ready headliners; an authored
/// table's seats as written, Orvel's one seat and Nyx's four among them), with the turn cap; win rates against section 6's
/// targets (Orvel's Table: 55-60%; Nyx's: 55%), duels decided at turn 40, first-turn losses. A table that isn't ready is
/// skipped. `--seat N` keeps an authored table's seat N only.</item>
/// <item>`--bracket-sim [--games N]`: Library Locals and the alley's brackets as the game makes them (the Alley Brawl's and the
/// Midnight Open's fields from <see cref="Brackets.CreateAlley"/>, settled through the engine): how often a deck at a given
/// rating takes the title. Targets: 30-45% at 8, 15-30% at 16 for a champion's best deck.</item>
/// <item>`--bracket-calibrate [--games N]`: the Elo odds against real duels: pairs of rated decks across the range play N
/// bot duels each; the measured gap in Elo against the rated gap. Target: within about 8 points on average.</item>
/// </list>
/// </summary>
public static partial class AlleyStudy
{
    private sealed class Setup
    {
        public List<Banlist> Lists = null!;
        public Banlist AlleyList = null!;
        public PrintingIndex Printings = null!;
        public Dictionary<string, string> DeckFiles = null!;
        public Dictionary<string, double> Elo = new Dictionary<string, double>();
        public ICardDataProvider Cards = null!;
        /// <summary>campaign/alley.txt's curated lists (empty: the whole pool by card value).</summary>
        public Dictionary<string, Curation> Curated = new Dictionary<string, Curation>();

        public Deck Load(string name)
        {
            var d = Deck.LoadYdk(DeckFiles[name]);
            d.Name = name;
            return d;
        }
    }

    private static Setup Load(ICardDataProvider cards, string root)
    {
        var s = new Setup
        {
            Cards = cards,
            Lists = Banlist.LoadFolders(new[] { Path.Combine(root, "vendor", "LFLists"), Path.Combine(root, "lflists") }).ToList(),
            Printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin")),
            DeckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
                .Where(f => !f.Contains("structure")).GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First()),
        };
        var curatedFile = Path.Combine(root, "campaign", "alley.txt");
        if (File.Exists(curatedFile) && Environment.GetEnvironmentVariable("ALLEY_UNCURATED") == null) s.Curated = Alley.LoadCurated(curatedFile, cards);
        var traditional = s.Lists.First(l => l.Name.Contains("Traditional"));
        s.AlleyList = Alley.List(traditional, cards);
        s.Elo = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName));
        return s;
    }

    /// <summary>An event's base decks: rated decks inside its Elo range.</summary>
    private static List<string> Bases(AlleyEvent e, Setup s) =>
        s.Elo.Where(p => p.Value >= e.MinElo && p.Value <= e.MaxElo && s.DeckFiles.ContainsKey(p.Key) && Alley.FromEra(s.Load(p.Key), e, s.Printings, s.Cards))
            .Where(p => !(s.Curated.TryGetValue(e.Name, out var c) && c.Skip.Contains(p.Key)))
            .OrderBy(p => p.Value).Select(p => p.Key).ToList();

    /// <summary>K splash decks for an event, base decks taken in turn, each from its own seed.</summary>
    private static List<SplashDeck> Generate(AlleyEvent e, Setup s, ICardDataProvider cards, int seed, int count)
    {
        var pool = Alley.Contraband(s.Lists, s.AlleyList, e.EraCap, s.Printings, cards);
        // Every base once (in a seeded order) before any repeats, so a run measures them all.
        var rng = new Random(seed);
        var bases = Bases(e, s).OrderBy(_ => rng.Next()).ToList();
        return Enumerable.Range(0, count).Select(k =>
        {
            var baseName = bases[k % bases.Count];
            return Alley.Splash(s.Load(baseName), e, pool, s.AlleyList, seed * 1000 + k, cards, s.Curated.TryGetValue(e.Name, out var c) ? c : null);
        }).ToList();
    }

    private static string Name(uint code, ICardDataProvider cards) => cards.Get(code)?.Name ?? code.ToString();

    public static int Gen(ICardDataProvider cards, string root, string only, int seed, int count)
    {
        var s = Load(cards, root);
        int illegal = 0;
        foreach (var e in Alley.Events.Where(e => only.Length == 0 || e.Name.Contains(only, StringComparison.OrdinalIgnoreCase)))
        {
            var pool = Alley.Contraband(s.Lists, s.AlleyList, e.EraCap, s.Printings, cards);
            var bases = Bases(e, s);
            Console.WriteLine($"\n{e.Name}: {bases.Count} base decks ({string.Join(", ", bases)}), {pool.Count} contraband cards out by {e.EraCap}, splash {e.Splash}");
            Console.WriteLine($"  pool: {string.Join(", ", (Environment.GetEnvironmentVariable("ALLEY_FULL_POOL") != null ? pool : pool.Take(30)).Select(c => Name(c, cards)))}{(pool.Count > 30 && Environment.GetEnvironmentVariable("ALLEY_FULL_POOL") == null ? $", …{pool.Count - 30} more" : "")}");
            foreach (var d in Generate(e, s, cards, seed, count))
            {
                var problems = DeckRules.Validate(d.Deck, s.AlleyList, cards);
                int want = s.Curated.TryGetValue(e.Name, out var cur) && cur.Sizes.TryGetValue(d.Base, out var own) ? own : e.Splash;
                if (problems.Count > 0 || d.Added.Count < want) illegal++;
                Console.WriteLine($"  {d.Base,-20} + {string.Join(", ", d.Added.Select(c => Name(c, cards)))}"
                    + $"   cut {string.Join(", ", d.Cut.Select(c => Name(c, cards)))}"
                    + (d.Added.Count < want ? $"   ONLY {d.Added.Count} OF {want}" : "")
                    + (problems.Count > 0 ? $"   ILLEGAL: {problems[0].Message}" : ""));
            }
        }
        Console.WriteLine($"\n{illegal} deck{(illegal == 1 ? "" : "s")} short or illegal");
        return illegal == 0 ? 0 : 1;
    }

    // ---- Duels ---------------------------------------------------------------------------------------

    /// <summary>
    /// One bot duel (the generic bot with each deck's playbook, no planning): the winner (0, 1, 2 draw, -1 stopped) and the
    /// turns. Alley duels carry the turn cap (<paramref name="turnCap"/>: turn 41 never starts, the higher LP wins).
    /// </summary>
    private static (int winner, int turns, int prompts) Duel(ICardDataProvider cards, IScriptProvider scripts, Deck a, Deck b, int seed, int first, string playbookA, string playbookB,
        int maxPrompts = 20000, bool turnCap = true) => DuelCapped(cards, scripts, a, b, seed, first, playbookA, playbookB, maxPrompts, turnCap) is var r ? (r.winner, r.turns, r.prompts) : default;

    private static (int winner, int turns, int prompts, bool capped) DuelCapped(ICardDataProvider cards, IScriptProvider scripts, Deck a, Deck b, int seed, int first, string playbookA,
        string playbookB, int maxPrompts = 20000, bool turnCap = true, bool trace = false)
    {
        var recipe = new DuelRecipe
        {
            Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)seed, FirstPlayer = first }, Deck0 = a.Clone(), Deck1 = b.Clone(),
        };
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        PromptTrace? ta = null, tb = null;
        DuelRunner? runner = null;
        IDuelist pa = new SmartDuelist("A", seed * 2, context, trace ? s => ta!.Note(s) : null, playbook: Playbooks.ForDeck(playbookA));
        IDuelist pb = new SmartDuelist("B", seed * 2 + 1, context, trace ? s => tb!.Note(s) : null, playbook: Playbooks.ForDeck(playbookB));
        if (trace)
        {
            pa = ta = new PromptTrace(pa, cards, "A");
            pb = tb = new PromptTrace(pb, cards, "B");
            ta.Where = tb.Where = () => $"[t{runner!.Result.Turns} p{runner.Result.Prompts:00000}]";
        }
        var answers = new ResponseLog(); // only recorded (for --keep-replays)
        var kept = ReplayKeeper.Current?.Begin("alley", $"s{seed}-{playbookA}-vs-{playbookB}", recipe, duel, answers, new[] { "A", "B" },
            new[] { SeatSpec.Of(0, new Personality { Name = "A" }, playbookA, seed * 2, false), SeatSpec.Of(1, new Personality { Name = "B" }, playbookB, seed * 2 + 1, false) },
            turnCap);
        runner = new DuelRunner(duel, ReplayKeeper.Wrap(kept, pa), ReplayKeeper.Wrap(kept, pb), answers);
        runner.MessageReceived += context.Observe;
        YataLock.Attach(runner, duel);
        var cap = turnCap ? TurnCap.Attach(runner, duel) : null;
        ReplayKeeper.Attach(kept, runner);
        var perTurn = new List<string>();
        if (trace)
            runner.MessageReceived += m =>
            {
                if (m[0] == Msg.NewTurn && cap != null) perTurn.Add($"t{runner.Result.Turns}:{runner.Result.Prompts}p {cap.Lp(0)}/{cap.Lp(1)}");
            };
        var result = runner.Run(maxPrompts);
        ReplayKeeper.Current?.Finish(kept, result);
        if (trace)
        {
            Console.WriteLine($"  seed {seed}: winner {result.Winner}, turn {result.Turns}, prompts {result.Prompts}, retries {result.Retries}{(cap != null ? $", LP {cap.Lp(0)}/{cap.Lp(1)}" : "")}");
            Console.WriteLine($"    at each new turn (prompts so far, LP A/B): {string.Join("  ", perTurn)}");
            Console.WriteLine($"    A prompts: {string.Join(", ", ta!.ByType.Select(kv => $"{kv.Key} {kv.Value}"))}");
            Console.WriteLine($"    B prompts: {string.Join(", ", tb!.ByType.Select(kv => $"{kv.Key} {kv.Value}"))}");
            foreach (var line in ta.Last.Concat(tb.Last).OrderBy(l => l, StringComparer.Ordinal).TakeLast(40)) Console.WriteLine("    " + line);
        }
        return (result.Winner, result.Turns, result.Prompts, result.Reason == DuelRunner.ReasonTurnCap);
    }

    /// <summary>The turn a deck wins on (going first) against a dummy that only sets a monster each turn; 0 if it doesn't by turn 20.</summary>
    private static int Goldfish(ICardDataProvider cards, IScriptProvider scripts, Deck deck, string playbook, Deck dummy, int seed)
    {
        var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)seed, FirstPlayer = 0 }, Deck0 = deck.Clone(), Deck1 = dummy.Clone() };
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var runner = new DuelRunner(duel, new SmartDuelist("A", seed, context, playbook: Playbooks.ForDeck(playbook)), new DummyDuelist(seed + 1, cards));
        runner.MessageReceived += context.Observe;
        var result = runner.Run(6000);
        return result.Winner == 0 && result.Turns <= 20 ? result.Turns : 0;
    }

    private static double EloGap(double p) => 400 * Math.Log10(Math.Clamp(p, 0.02, 0.98) / (1 - Math.Clamp(p, 0.02, 0.98)));

    public static int Rate(ICardDataProvider cards, IScriptProvider scripts, string root, string only, int seed, int count, int games)
    {
        var s = Load(cards, root);
        var dummy = Deck.LoadYdk(Path.Combine(root, "decks", "classic.ydk"));
        var timer = Stopwatch.StartNew();
        foreach (var e in Alley.Events.Where(e => only.Length == 0 || e.Name.Contains(only, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"\n{e.Name} (splash {e.Splash}): {count} decks, {games} duels each against their base, {games} goldfish games each");
            var gains = new List<double>();
            var speedups = new List<double>();
            foreach (var d in Generate(e, s, cards, seed, count))
            {
                var baseDeck = s.Load(d.Base);
                int wins = 0, played = 0;
                Parallel.For(0, games, g =>
                {
                    var (winner, _, _) = Duel(cards, scripts, d.Deck, baseDeck, seed * 7919 + g, g % 2, d.Base, d.Base);
                    if (winner == 0) Interlocked.Increment(ref wins);
                    if (winner == 0 || winner == 1) Interlocked.Increment(ref played);
                });
                double p = played == 0 ? 0.5 : (double)wins / played;
                var splashTurns = new int[games];
                var baseTurns = new int[games];
                Parallel.For(0, games, g =>
                {
                    splashTurns[g] = Goldfish(cards, scripts, d.Deck, d.Base, dummy, seed * 31 + g);
                    baseTurns[g] = Goldfish(cards, scripts, baseDeck, d.Base, dummy, seed * 31 + g);
                });
                double Avg(int[] t) => t.Where(x => x > 0).DefaultIfEmpty(0).Average();
                double gain = EloGap(p), speedup = Avg(baseTurns) - Avg(splashTurns);
                gains.Add(gain);
                speedups.Add(speedup);
                bool tooFast = speedup > 1 || (Avg(splashTurns) > 0 && Avg(splashTurns) < 3);
                Console.WriteLine($"  {d.Base,-20} + {string.Join(", ", d.Added.Select(c => Name(c, cards))),-70} won {p,4:P0} of {played}: {gain,5:+0;-0} Elo"
                    + $"   goldfish turn {Avg(splashTurns):0.0} (base {Avg(baseTurns):0.0}){(gain > 250 ? "  TOO STRONG" : gain < 60 ? "  weak" : "")}{(tooFast ? "  TOO FAST" : "")}");
            }
            Console.WriteLine($"  {e.Name}: average {gains.Average():+0;-0} Elo (target +60 to +150), {gains.Count(g => g > 250)} over +250; "
                + $"average {speedups.Average():0.0} turns faster (target at most 1)");
        }
        Console.WriteLine($"\n({timer.Elapsed.TotalMinutes:0.0} min)");
        return 0;
    }

    /// <summary>
    /// `--endless-test`: the alley's decks against each other under the Alley list, with the turn cap: 6 splash decks an
    /// event, plus the alley's authored decks (the authored seats' decks that are built, Orvel's house deck among them, and
    /// the ready headliners'), which play every third duel against a splash deck, so each meets dozens of regulars. Counts
    /// for all duels and for each authored deck: the prompt cap, duels decided at turn 40, draws.
    /// </summary>
    public static int Endless(ICardDataProvider cards, IScriptProvider scripts, string root, int seed, int games)
    {
        var s = Load(cards, root);
        var splash = Alley.Events.SelectMany(e => Generate(e, s, cards, seed, 6)).Select(d => (deck: d.Deck, playbook: d.Base, label: d.Deck.Name)).ToList();
        var data = AlleyData.Load(Path.Combine(root, "campaign", AlleyData.FileName), cards);
        var authored = new List<(Deck deck, string playbook, string label)>();
        void Add(string name, string label)
        {
            if (!s.DeckFiles.ContainsKey(name) || authored.Any(x => x.deck.Name == name)) return;
            var d = s.Load(name);
            authored.Add((d, Playbooks.RecipeOf(d), label));
        }
        foreach (var t in data.Tables.Where(t => t.Ready))
            foreach (var seat in t.Seats.Where(x => x.Kind == "authored")) Add(seat.Deck, $"{t.Name}: {seat.Deck}");
        foreach (var h in data.Headliners.Where(h => h.Ready)) Add(h.Deck, $"headliner {h.Handle}: {h.Deck}");
        // ENDLESS_DECKS=<deck>,<deck>...: more authored decks to test (played every third duel with the rest, like the ready headliners').
        foreach (var name in (Environment.GetEnvironmentVariable("ENDLESS_DECKS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            Add(name.Trim(), $"extra: {name.Trim()}");
        // No Limits (spec 4.7, 14.3): every build whose deck exists (ready or not: the gate decides ready) and every Brawl deck, in a group of their own.
        var nlDecks = new List<(Deck deck, string playbook, string label)>();
        foreach (var b in data.Builds.Where(b => b.Deck.Length > 0 && s.DeckFiles.ContainsKey(b.Deck)))
        {
            var d = s.Load(b.Deck);
            nlDecks.Add((d, Playbooks.RecipeOf(d), $"build {b.Handle}: {b.Deck}"));
        }
        foreach (var name in data.Brackets.SelectMany(b => b.Field.Select(AlleyBracketSpec.ParseField)).Where(f => f.after.Length > 0 && f.words.Length > 1).Select(f => f.words[1]).Distinct())
            if (s.DeckFiles.ContainsKey(name)) { var d = s.Load(name); nlDecks.Add((d, Playbooks.RecipeOf(d), $"Brawl deck: {name}")); }
        int capped = 0, long40 = 0, draws = 0, played = 0;
        var per = authored.ToDictionary(x => x.label, _ => new int[5]); // played, won, capped, at turn 40, draws
        var timer = Stopwatch.StartNew();
        var notes = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, games, g =>
        {
            // Every third duel puts an authored deck (in turn) against a splash deck; the rest are splash against splash.
            bool withAuthored = authored.Count > 0 && g % 3 == 2;
            var a = withAuthored ? authored[(g / 3) % authored.Count] : splash[(g * 7) % splash.Count];
            var b = splash[(g * 13 + 5) % splash.Count];
            var (winner, turns, prompts, atCap) = DuelCapped(cards, scripts, a.deck, b.deck, seed * 1000 + g, g % 2, a.playbook, b.playbook);
            Interlocked.Increment(ref played);
            if (winner < 0) { Interlocked.Increment(ref capped); notes.Add($"  stopped at the prompt cap: {a.label} vs {b.label} (seed {seed * 1000 + g}, turn {turns})"); }
            else if (winner == 2) { Interlocked.Increment(ref draws); notes.Add($"  draw: {a.label} vs {b.label} (turn {turns}{(atCap ? ", level LP at turn 40" : "")})"); }
            if (atCap) { Interlocked.Increment(ref long40); notes.Add($"  decided at turn 40: {a.label} vs {b.label} (seed {seed * 1000 + g}, {prompts} prompts)"); }
            if (withAuthored)
            {
                var c = per[a.label];
                lock (c)
                {
                    c[0]++;
                    if (winner == 0) c[1]++;
                    if (winner < 0) c[2]++;
                    if (atCap) c[3]++;
                    if (winner == 2) c[4]++;
                }
            }
        });
        // The No Limits group: each build and Brawl deck plays an equal share of a third of the duels, against splash decks and the other No Limits decks in turn.
        // ENDLESS_REPLAY=<seed>,<seed>...: replay only those No Limits duels (the seeds the notes print) with a prompt trace.
        if (Environment.GetEnvironmentVariable("ENDLESS_REPLAY") is { Length: > 0 } replay && nlDecks.Count > 0)
        {
            foreach (var word in replay.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                int gameSeed = int.Parse(word.Trim()), g = gameSeed - (seed * 1000 + 700000);
                var (a, b) = NoLimitsPairing(nlDecks, splash, g);
                Console.WriteLine($"replay {gameSeed} (g {g}): {a.label} vs {b.label}, first {g % 2}");
                DuelCapped(cards, scripts, a.deck, b.deck, gameSeed, g % 2, a.playbook, b.playbook, trace: true);
            }
            return 0;
        }
        var nlPer = nlDecks.ToDictionary(x => x.label, _ => new int[5]);
        int nlCapped = 0, nlLong40 = 0, nlDraws = 0, nlPlayed = 0;
        if (nlDecks.Count > 0)
        {
            int nlGames = Math.Max(20 * nlDecks.Count, games / 3);
            Parallel.For(0, nlGames, g =>
            {
                var (a, b) = NoLimitsPairing(nlDecks, splash, g);
                var (winner, turns, prompts, atCap) = DuelCapped(cards, scripts, a.deck, b.deck, seed * 1000 + 700000 + g, g % 2, a.playbook, b.playbook);
                Interlocked.Increment(ref nlPlayed);
                if (winner < 0) { Interlocked.Increment(ref nlCapped); notes.Add($"  stopped at the prompt cap: {a.label} vs {b.label} (seed {seed * 1000 + 700000 + g}, turn {turns})"); }
                else if (winner == 2) { Interlocked.Increment(ref nlDraws); notes.Add($"  draw: {a.label} vs {b.label} (turn {turns}{(atCap ? ", level LP at turn 40" : "")})"); }
                if (atCap) { Interlocked.Increment(ref nlLong40); notes.Add($"  decided at turn 40: {a.label} vs {b.label} (seed {seed * 1000 + 700000 + g}, {prompts} prompts)"); }
                var c = nlPer[a.label];
                lock (c)
                {
                    c[0]++;
                    if (winner == 0) c[1]++;
                    if (winner < 0) c[2]++;
                    if (atCap) c[3]++;
                    if (winner == 2) c[4]++;
                }
            });
        }
        // Every prompt-cap stop (each is a stalled duel to replay with ENDLESS_REPLAY), then up to 15 of the other notes.
        foreach (var n in notes.Where(n => n.Contains("prompt cap")).OrderBy(n => n, StringComparer.Ordinal)) Console.WriteLine(n);
        foreach (var n in notes.Where(n => !n.Contains("prompt cap")).OrderBy(n => n, StringComparer.Ordinal).Take(15)) Console.WriteLine(n);
        string Pct(int n, int of) => $"{100.0 * n / Math.Max(1, of):0.0}%";
        // The targets: prompt cap under 0.5%, decided at turn 40 under 2%, draws under 1%.
        bool Off(int cap, int at40, int draw, int of) => cap >= 0.005 * of || at40 >= 0.02 * of || draw >= 0.01 * of;
        int off = 0;
        foreach (var (label, c) in per)
        {
            bool bad = Off(c[2], c[3], c[4], c[0]);
            if (bad) off++;
            Console.WriteLine($"  {(bad ? "OFF " : "ok  ")} {label}: {c[0]} duels against splash decks, won {Pct(c[1], c[0])}; prompt cap {Pct(c[2], c[0])}, decided at turn 40 {Pct(c[3], c[0])}, draws {Pct(c[4], c[0])}");
        }
        int nlOff = 0;
        foreach (var (label, c) in nlPer)
        {
            bool bad = Off(c[2], c[3], c[4], c[0]);
            if (bad) nlOff++;
            Console.WriteLine($"  {(bad ? "OFF " : "ok  ")} {label}: {c[0]} duels against splash decks and the other No Limits decks, won {Pct(c[1], c[0])}; prompt cap {Pct(c[2], c[0])}, decided at turn 40 {Pct(c[3], c[0])}, draws {Pct(c[4], c[0])}");
        }
        bool nlBad = nlDecks.Count > 0 && Off(nlCapped, nlLong40, nlDraws, nlPlayed);
        if (nlDecks.Count > 0)
            Console.WriteLine($"{(nlBad ? "OFF " : "ok  ")} {nlPlayed} No Limits duels among {nlDecks.Count} builds and Brawl decks, splash decks {splash.Count}: prompt cap {Pct(nlCapped, nlPlayed)} (target under 0.5%), decided at turn 40 {Pct(nlLong40, nlPlayed)} (under 2%), draws {Pct(nlDraws, nlPlayed)} (under 1%); {nlOff} deck{(nlOff == 1 ? "" : "s")} off");
        capped += nlCapped; long40 += nlLong40; draws += nlDraws; played += nlPlayed;
        bool allBad = Off(capped, long40, draws, played);
        Console.WriteLine($"\n{(allBad ? "OFF " : "ok  ")} {played} alley duels among {splash.Count} splash decks and {authored.Count} authored decks: prompt cap {Pct(capped, played)} (target under 0.5%), "
            + $"decided at turn 40 {Pct(long40, played)} (under 2%), draws {Pct(draws, played)} (under 1%); {off} authored deck{(off == 1 ? "" : "s")} off   ({timer.Elapsed.TotalMinutes:0.0} min)");
        return nlBad || nlOff > 0 ? 1 : 0;
    }

    /// <summary>The endless test's No Limits duel <paramref name="g"/>: the deck (in turn) and its opponent (a splash deck or the other No Limits decks in turn).</summary>
    private static ((Deck deck, string playbook, string label) a, (Deck deck, string playbook, string label) b) NoLimitsPairing(
        List<(Deck deck, string playbook, string label)> nlDecks, List<(Deck deck, string playbook, string label)> splash, int g)
    {
        int di = g % nlDecks.Count, round = g / nlDecks.Count;
        var a = nlDecks[di];
        var b = round % 2 == 0 ? splash[(g * 13 + 5) % splash.Count] : nlDecks[(di + 1 + (round / 2) % (nlDecks.Count - 1 == 0 ? 1 : nlDecks.Count - 1)) % nlDecks.Count];
        return (a, b);
    }

    // ---- Brackets ------------------------------------------------------------------------------------

    private static double Odds(double a, double b) => 1 / (1 + Math.Pow(10, (b - a) / 400));

    /// <summary>
    /// `--bracket-sim`: Library Locals (<see cref="BracketStudy.Sim"/>: the real brackets, your matches at the measured
    /// rates), then the alley's two (<see cref="BracketStudy.AlleySim"/>: the fields as <see cref="Brackets.CreateAlley"/>
    /// draws them, settled through the engine).
    /// </summary>
    public static int BracketSim(ICardDataProvider cards, string root, int seed, int brackets)
    {
        BracketStudy.Sim(cards, root, seed, brackets);
        return BracketStudy.AlleySim(cards, root, seed, brackets);
    }

    public static int BracketCalibrate(ICardDataProvider cards, IScriptProvider scripts, string root, int seed, int games)
    {
        var s = Load(cards, root);
        // Pairs across the range: a deck against ones about 0, 50, 100, 200 and 300 points below it.
        var rated = s.Elo.Where(p => s.DeckFiles.ContainsKey(p.Key)).OrderByDescending(p => p.Value).ToList();
        var rng = new Random(seed);
        var pairs = new List<(string a, string b)>();
        foreach (var gap in new[] { 0, 50, 100, 200, 300 })
            for (int k = 0; k < 3; k++)
            {
                var a = rated[rng.Next(rated.Count / 2)];
                var b = rated.Where(p => p.Key != a.Key).OrderBy(p => Math.Abs(a.Value - p.Value - gap)).First();
                pairs.Add((a.Key, b.Key));
            }
        var timer = Stopwatch.StartNew();
        var errors = new List<double>();
        foreach (var (a, b) in pairs)
        {
            var da = s.Load(a);
            var db = s.Load(b);
            int wins = 0, played = 0;
            Parallel.For(0, games, g =>
            {
                var (winner, _, _) = Duel(cards, scripts, da, db, seed * 977 + g, g % 2, a, b, turnCap: false);
                if (winner == 0) Interlocked.Increment(ref wins);
                if (winner == 0 || winner == 1) Interlocked.Increment(ref played);
            });
            double p = played == 0 ? 0.5 : (double)wins / played;
            double rated_ = s.Elo[a] - s.Elo[b], measured = EloGap(p);
            errors.Add(Math.Abs(measured - rated_));
            Console.WriteLine($"  {a,-22} {s.Elo[a],5:0} vs {b,-22} {s.Elo[b],5:0}: expected {Odds(s.Elo[a], s.Elo[b]),4:P0}, won {p,4:P0} of {played}   "
                + $"gap rated {rated_,4:+0;-0}, measured {measured,4:+0;-0}");
        }
        Console.WriteLine($"\nMean gap error {errors.Average():0} Elo over {pairs.Count} pairs (target about 8; at {games} duels a pair, chance alone is about "
            + $"{400 / Math.Log(10) * 2 / Math.Sqrt(games):0})   ({timer.Elapsed.TotalMinutes:0.0} min)");
        return 0;
    }

    // ---- --alley-study -------------------------------------------------------------------------------

    /// <summary>Section 6's targets: your best option and the starter at each regular table; headliners against your best option.</summary>
    private static readonly Dictionary<string, (double best, double starter, double headliner)> Targets = new Dictionary<string, (double, double, double)>
    {
        ["dumpster-row"] = (0.75, 0.65, 0.70), ["basement"] = (0.65, 0.50, 0.60), ["black-market"] = (0.60, 0.40, 0.55),
        // Orvel's Table (decision O1): your best deck wins 55-60% against the House; the starter has no target there.
        ["orvels-table"] = (0.575, 0, 0),
        // Nyx's Table (section 6, decision O2): your best option wins 55% at each of its four seats; no starter target.
        ["nyx"] = (0.55, 0, 0),
    };

    /// <summary>Targets given as a range (the rest are a point, ± 10%): Orvel's Table, 55-60% for your best deck.</summary>
    private static readonly Dictionary<string, (double low, double high)> Ranges = new Dictionary<string, (double, double)> { ["orvels-table"] = (0.55, 0.60) };

    /// <summary>
    /// `--alley-study`: the QA stand-in (the roster's best planner that knows its deck, with the turn planner) against each
    /// seat of each ready regular table as the game builds it, with the turn cap: the regulars of 20 seeded nights (seats
    /// 1-3, <paramref name="games"/> duels each), the closer (a regular at seat 4 once the headliners are beaten) and each
    /// ready headliner (10 × <paramref name="games"/> duels each). Your best deck (<paramref name="option"/>, a deck name or a
    /// .ydk; default: the best-rated deck in the `--load` save, else goat-control) and the starter (classic), each made legal
    /// for the Alley list (as the versus screen would ask you to). Seats alternate who goes first.
    /// </summary>
    public static int Study(ICardDataProvider cards, IScriptProvider scripts, string root, string only, int seed, int games, string option, string? load, int seatOnly = 0)
    {
        var timer = Stopwatch.StartNew();
        games = Math.Max(1, games);
        var printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities")).ToList();
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var library = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), roster, circuits);
        var a = AlleyTest.Load(cards, root, printings, roster, library);
        var data = a.Data;
        var ctx = a.Ctx;
        var you = roster.Where(p => p.PlansAhead && p.KnowsDeck && data.FindBuild(p.Id) == null).OrderByDescending(p => p.Skill).First();

        // Your decks: the best one and the starter, made legal for the Alley list.
        Deck? FromSave(string name)
        {
            if (File.Exists(name)) { var d = Deck.LoadYdk(name); d.Name = Path.GetFileNameWithoutExtension(name); return d; }
            return ctx.LoadDeck(name);
        }
        Deck? best = option.Length > 0 ? FromSave(option) : null;
        if (best == null && load != null && Directory.Exists(Path.Combine(load, "decks")))
            best = Directory.GetFiles(Path.Combine(load, "decks"), "*.ydk").Select(f => { var d = Deck.LoadYdk(f); d.Name = Path.GetFileNameWithoutExtension(f); return d; })
                .OrderByDescending(d => ctx.Ratings.TryGetValue(Playbooks.RecipeOf(d), out var r) ? r : 0).FirstOrDefault();
        // The QA save's best deck (no_limits comparison row): --option, else the best deck in --load, else the QA save under build/qa-night5 (read only).
        var qaBest = best ?? BestFromSave(Path.Combine(root, "build", "qa-night5", "mods"), ctx);
        bool nlOnly = only.Length > 0 && data.Tables.Where(x => x.Name.Contains(only, StringComparison.OrdinalIgnoreCase)) is var named && named.Any() && named.All(x => x.Tier == AlleyTier.NoLimits);
        best ??= ctx.LoadDeck("goat-control")!;
        var starter = Deck.LoadYdk(Path.Combine(root, "decks", "classic.ydk"));
        starter.Name = "classic";
        var yours = new List<(string label, Deck deck)>();
        foreach (var (label, d) in nlOnly ? Array.Empty<(string, Deck)>() : new[] { ("best", best), ("starter", starter) })
        {
            var legal = Loaners.Adjust(d, a.List, cards).Deck;
            legal.Side.Clear();
            legal.Name = d.Name;
            if (d.Note("recipe").Length > 0) legal.Notes["recipe"] = d.Note("recipe");
            var problems = DeckRules.Validate(d, a.List, cards);
            Console.WriteLine($"Your {label} deck: {d.Name} (rated {(ctx.Ratings.TryGetValue(Playbooks.RecipeOf(d), out var r) ? r.ToString("0") : "?")})"
                + (problems.Count > 0 ? $", made legal for the Alley ({string.Join("; ", problems.Select(p => p.Message).Take(3))})" : ", legal in the Alley as built"));
            yours.Add((label, legal));
        }
        if (!nlOnly) Console.WriteLine($"Alley study: you play as {you.Name} (skill {you.Skill:0.00}, planner on), {games} duel(s) a regular over 20 nights, {10 * games} a closer or headliner, "
            + $"turn cap {AlleyEconomy.AlleyTurns}, seed {seed}");

        foreach (var ti in Enumerable.Range(0, data.Tables.Count))
        {
            var t = data.Tables[ti];
            if (only.Length > 0 && !t.Name.Contains(only, StringComparison.OrdinalIgnoreCase)) continue;
            if (t.Tier == AlleyTier.NoLimits)
            {
                // The No Limits tier is measured whether or not it is ready: the study is what decides ready: (AlleyStudy.NoLimits.cs).
                NoLimitsStudy(cards, scripts, root, seed, games, qaBest, qaBest?.Name ?? "", a, printings, roster, library, starter, you);
                continue;
            }
            if (!t.Ready)
            {
                Console.WriteLine($"\n{t.Name} ({t.Tier}): not ready (its authored decks come in a later phase); skipped");
                continue;
            }
            // The seats as the game draws them. A regular table: 20 nights of regulars, then the closer (headliners beaten), and
            // each ready headliner. An authored table (Orvel's, Nyx's): its seats as written, a regular seat over 20 nights and
            // every other seat (an authored deck, a headliner) at 10 × games.
            var p = AlleyTest.Opened(starter, cards, printings, library, (uint)seed * 2654435761u + 17);
            foreach (var other in data.Tables.Where(x => x != t)) p.AlleyTables[other.Id] = new AlleyTableState { Cleared = true };
            var seats = new List<(string group, AlleyOpponent o, int duels)>();
            bool authored = t.Seats.Count > 0;
            for (int night = 0; night < 20; night++)
            {
                p.AlleyTables[t.Id] = new AlleyTableState { Night = night };
                foreach (var o in BackAlley.Lineup(p, data, ti, ctx))
                {
                    if (authored && seatOnly > 0 && o.Seat != seatOnly) continue;
                    if (!authored && o.Kind == "regular" && o.Seat < BackAlley.Matches) seats.Add(("regulars", o, games));
                    else if (authored && o.Kind == "regular") seats.Add(($"seat {o.Seat}: regulars", o, games));
                    else if (authored && night == 0) seats.Add(($"seat {o.Seat}: {o.Handle} ({o.Kind})", o, 10 * games));
                }
            }
            if (!authored)
            {
                foreach (var h in t.Headliners.Select(data.FindHeadliner).OfType<Headliner>().Where(h => h.Ready)) p.Headliners[h.Id] = new HeadlinerState { Beaten = 1 };
                p.AlleyTables[t.Id] = new AlleyTableState { Night = 20 };
                if (BackAlley.Lineup(p, data, ti, ctx).FirstOrDefault(o => o.Seat == BackAlley.Matches) is AlleyOpponent closer)
                    seats.Add(($"closer ({closer.Base})", closer, 10 * games));
                foreach (var h in t.Headliners.Select(data.FindHeadliner).OfType<Headliner>().Where(h => h.Ready))
                    if (BackAlley.Encore(p, data, ti, h.Id, ctx) is AlleyOpponent enc) seats.Add(($"{h.Handle} (headliner)", enc, 10 * games));
            }
            if (seats.Count == 0)
            {
                Console.WriteLine($"\n{t.Name} ({t.Tier}): no seats drawn; skipped");
                continue;
            }
            var target = Targets.TryGetValue(t.Id, out var tg) ? tg : (0, 0, 0);
            var range = Ranges.TryGetValue(t.Id, out var rg) ? rg : ((double low, double high)?)null;
            string Want(double x) => range != null && x == target.best ? $"{range.Value.low:P0}-{range.Value.high:P0}" : $"{x:P0}";
            Console.WriteLine(authored
                ? $"\n{t.Name} ({t.Tier}, stake {Ante.StakeFor(t.Tier):N0}{(t.Tier == AlleyTier.Orvel ? " and a spare card" : "")}): {t.SeatCount} seat(s): "
                  + $"{string.Join(", ", seats.Select(x => x.group).Distinct())}. Target: best {Want(target.best)}"
                : $"\n{t.Name} ({t.Tier}, stake {Ante.StakeFor(t.Tier):N0}): {seats.Count(x => x.group == "regulars")} regulars, the closer, "
                  + $"{seats.Count(x => x.group.EndsWith("(headliner)"))} headliner(s). Targets: best {target.best:P0}, starter {target.starter:P0}, headliners {target.headliner:P0}");
            foreach (var (label, mine) in yours)
            {
                // An authored table with no target for the starter (Orvel's) isn't worth its duels.
                if (authored && label == "starter" && target.starter == 0)
                {
                    Console.WriteLine($"  {label,-8} no target at {t.Name}; skipped");
                    continue;
                }
                int allWon = 0, allPlayed = 0;
                foreach (var group in seats.Select(x => x.group).Distinct())
                {
                    var list = seats.Where(x => x.group == group).ToList();
                    var jobs = list.SelectMany((x, i) => Enumerable.Range(0, x.duels).Select(g => (x.o, n: i * 1000 + g))).ToList();
                    int won = 0, lost = 0, draws = 0, atCap = 0, firstTurn = 0, stopped = 0;
                    Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, job =>
                    {
                        int gameSeed = seed * 100000 + ti * 10000 + job.n;
                        int first = job.n % 2;
                        var theirs = BackAlley.DeckOf(job.o, ctx);
                        var them = BackAlley.PersonalityOf(job.o, ctx);
                        var (winner, turns, capped) = StudyDuel(cards, scripts, gameSeed, first, you, mine, them, theirs);
                        if (winner == 0) Interlocked.Increment(ref won);
                        else if (winner == 1) Interlocked.Increment(ref lost);
                        else if (winner == 2) Interlocked.Increment(ref draws);
                        else Interlocked.Increment(ref stopped);
                        if (capped) Interlocked.Increment(ref atCap);
                        if (winner == 1 && turns <= 1 && first == 1) Interlocked.Increment(ref firstTurn);
                    });
                    int n = jobs.Count;
                    allWon += won;
                    allPlayed += won + lost + draws;
                    double rate = won / (double)Math.Max(1, won + lost + draws);
                    double want = group.EndsWith("(headliner)") ? (label == "best" ? target.headliner : 0) : label == "best" ? target.best : target.starter;
                    string Pct(int x) => $"{100.0 * x / Math.Max(1, n):0.#}%";
                    // A range target (Orvel's) is OFF outside it; a point target, more than 10 points away.
                    bool off = range != null && want == target.best ? rate < range.Value.low || rate > range.Value.high : Math.Abs(rate - want) > 0.1;
                    double se = Math.Sqrt(rate * (1 - rate) / Math.Max(1, won + lost + draws));
                    Console.WriteLine($"  {label,-8} vs {group,-34} won {rate,6:P1} of {n,4} (± {se:P1}){(want > 0 ? $" (target {Want(want)}{(off ? ", OFF" : "")})" : "")}  "
                        + $"decided at turn 40 {Pct(atCap)}, draws {Pct(draws)}, first-turn losses {Pct(firstTurn)}{(stopped > 0 ? $", stopped {stopped}" : "")}");
                }
                // An authored table of several seats (Nyx's): every seat's duels together.
                if (authored && seats.Select(x => x.group).Distinct().Count() > 1)
                {
                    double all = allWon / (double)Math.Max(1, allPlayed);
                    Console.WriteLine($"  {label,-8} vs {"every seat",-34} won {all,6:P1} of {allPlayed,4} (± {Math.Sqrt(all * (1 - all) / Math.Max(1, allPlayed)):P1})"
                        + (label == "best" && target.best > 0 ? $" (target {Want(target.best)}{(Math.Abs(all - target.best) > 0.1 ? ", OFF" : "")})" : ""));
                }
            }
        }
        Console.WriteLine($"\n({timer.Elapsed.TotalMinutes:0.0} min)");
        return 0;
    }

    /// <summary>One alley duel as the study plays it: you (the stand-in, planning) against the CPU as the game makes it, with the turn cap.</summary>
    private static (int winner, int turns, bool capped) StudyDuel(ICardDataProvider cards, IScriptProvider scripts, int gameSeed, int first, Personality you, Deck mine,
        Personality them, Deck theirs)
    {
        var recipe = new DuelRecipe
        {
            Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)gameSeed, FirstPlayer = first }, Deck0 = mine.Clone(), Deck1 = theirs.Clone(),
        };
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var answers = new ResponseLog();
        var yours = new SmartDuelist(you.Name, gameSeed * 4, context, personality: you, playbook: Playbooks.ForDeck(mine),
            planner: new TurnPlanner(recipe, answers, you, () => Playbooks.ForDeck(mine)));
        IDuelist cpu = them.PlansAhead
            ? new SmartDuelist(them.Name, gameSeed * 4 + 1, context, personality: them, playbook: Playbooks.ForDeck(theirs),
                planner: new TurnPlanner(recipe, answers, them, () => Playbooks.ForDeck(theirs)))
            : new SmartDuelist(them.Name, gameSeed * 4 + 1, context, personality: them, playbook: them.KnowsDeck ? Playbooks.ForDeck(theirs) : null);
        var kept = ReplayKeeper.Current?.Begin("alley-study", $"s{gameSeed}-{mine.Name}-vs-{theirs.Name}", recipe, duel, answers, new[] { you.Name, them.Name },
            new[] { SeatSpec.Of(0, you, mine, gameSeed * 4, true), SeatSpec.Of(1, them, theirs, gameSeed * 4 + 1, them.PlansAhead) }, turnCap: true);
        var runner = new DuelRunner(duel, ReplayKeeper.Wrap(kept, yours), ReplayKeeper.Wrap(kept, cpu), answers);
        runner.MessageReceived += context.Observe;
        YataLock.Attach(runner, duel);
        TurnCap.Attach(runner, duel);
        ReplayKeeper.Attach(kept, runner);
        var result = runner.Run(20000);
        ReplayKeeper.Current?.Finish(kept, result);
        return (result.Winner, result.Turns, result.Reason == DuelRunner.ReasonTurnCap);
    }
}
