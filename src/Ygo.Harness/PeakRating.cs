using System.Diagnostics;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Campaign;

namespace Ygo.Harness;

/// <summary>
/// `--rate-anchored &lt;folder or .ydk&gt;[,...] [--rounds N] [--skilled] [--write]` (docs/dev/NO-LIMITS-SPEC-2026-11-02.md P3):
/// rates new decks against the frozen pool. Each deck plays N duels (default <see cref="DefaultRounds"/>) against pool decks (the ones
/// `--rate-decks` rates, at their campaign/deck-ratings.txt Elo) drawn within ±<see cref="Window"/> of its running
/// estimate, with `--rate-decks`' bot (playbook on; `--skilled`: the planner at skill 1.0), who goes first alternating.
/// Only the new decks' Elo moves: each is the maximum-likelihood rating of its results against the pool's fixed ratings,
/// re-estimated after every batch (the estimate picks the next opponents). Prints `--rate-decks`' table lines, so
/// <see cref="DeckRatings.Parse"/> reads them; `--write` merges them into campaign/peak-ratings.txt (a deck already there
/// is replaced, the rest kept).
/// <para>
/// Every run also checks itself: <see cref="CalibrationDecks"/> pool decks spread over the new decks' range are rated
/// the same way as if new (never meeting themselves) and must land within ±<see cref="CalibrationTolerance"/> of their
/// pool rating. A miss fails the run and nothing is written.
/// </para>
/// </summary>
public static class PeakRating
{
    /// <summary>
    /// Duels a deck. The spec's 300 left the calibration at the edge of its tolerance (one pool deck of five at +42 on the
    /// first run: the anchored estimate's own spread, about ±20 at 300, adds to the pool rating's), so 600: about ±14.
    /// </summary>
    public const int DefaultRounds = 600;
    /// <summary>How far from a deck's running estimate its pool opponents may be.</summary>
    public const double Window = 250;
    public const int CalibrationDecks = 5;
    public const double CalibrationTolerance = 40;
    /// <summary>Duels a deck plays between estimates (all decks' batches run in parallel).</summary>
    private const int Batch = 10;
    /// <summary>The fewest pool decks a draw may choose from; the window widens until it has them.</summary>
    private const int MinOpponents = 6;

    private sealed class Entry
    {
        public string Name = "";
        public Deck Deck = new Deck();
        public double Elo;
        public double Start;
        /// <summary>Pool rating, for a calibration deck (0 for a new one).</summary>
        public double Pool;
        public Random Rng = new Random(0);
        public readonly List<(double opponent, double score)> Results = new List<(double, double)>();
        public int Wins, Errors, Turns;
        public double MinOpponent = double.MaxValue, MaxOpponent;
    }

    private sealed class PoolDeck
    {
        public string Name = "";
        public Deck Deck = new Deck();
        public double Elo;
    }

    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, string targets, int rounds, int seed, bool skilled, bool write)
    {
        var ratings = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName));
        var pool = DeckRating.DeckFiles(root)
            .Select(f => (name: Path.GetFileNameWithoutExtension(f), file: f))
            .Where(f => ratings.ContainsKey(f.name))
            .Select(f => new PoolDeck { Name = f.name, Deck = Named(Deck.LoadYdk(f.file), f.name), Elo = ratings[f.name] })
            .Where(p => p.Deck.Main.Count >= 40)
            .ToList();
        var files = new List<string>();
        foreach (var target in targets.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.IsPathRooted(target) ? target : Path.Combine(root, target);
            if (Directory.Exists(path)) files.AddRange(Directory.GetFiles(path, "*.ydk", SearchOption.AllDirectories).OrderBy(f => f));
            else if (File.Exists(path) && path.EndsWith(".ydk", StringComparison.OrdinalIgnoreCase)) files.Add(path);
            else { Console.Error.WriteLine($"--rate-anchored: no folder or .ydk \"{target}\""); return 1; }
        }
        var poolNames = pool.Select(p => p.Name).ToHashSet();
        var fresh = files.Select(f => Named(Deck.LoadYdk(f), Path.GetFileNameWithoutExtension(f)))
            .Where(d => d.Main.Count >= 40 && !poolNames.Contains(d.Name))
            .Select((d, i) =>
            {
                // A deck starts at its recipe's rating (a build's base), else the pool's median.
                double start = ratings.TryGetValue(d.Note("recipe"), out var b) ? b : Median(pool.Select(p => p.Elo));
                return new Entry { Name = d.Name, Deck = d, Elo = start, Start = start, Rng = new Random(seed * 7919 + i) };
            })
            .ToList();
        if (fresh.Count == 0) { Console.Error.WriteLine("--rate-anchored: no unrated decks to rate"); return 1; }

        // The calibration decks: the pool decks nearest evenly spaced points over the new decks' starting range.
        double lo = fresh.Min(e => e.Start) - 50, hi = fresh.Max(e => e.Start) + 150;
        var calibration = new List<Entry>();
        for (int k = 0; k < CalibrationDecks; k++)
        {
            double at = lo + (hi - lo) * k / (CalibrationDecks - 1);
            var p = pool.Where(x => calibration.All(c => c.Name != x.Name)).OrderBy(x => Math.Abs(x.Elo - at)).ThenBy(x => x.Name, StringComparer.Ordinal).First();
            double start = Median(pool.Select(x => x.Elo));
            calibration.Add(new Entry { Name = p.Name, Deck = p.Deck, Elo = start, Start = start, Pool = p.Elo, Rng = new Random(seed * 104729 + k) });
        }

        var all = fresh.Concat(calibration).ToList();
        Console.WriteLine($"Rating {fresh.Count} decks anchored to {pool.Count} pool decks ({rounds} duels each, ±{Window:F0} window{(skilled ? ", skilled" : "")}), "
                          + $"with {calibration.Count} pool decks re-rated as a calibration check...");
        var timer = Stopwatch.StartNew();
        for (int played = 0; played < rounds; played += Batch)
        {
            int n = Math.Min(Batch, rounds - played);
            // Opponents for the batch, drawn from each deck's own stream around its current estimate.
            var games = new List<(Entry e, PoolDeck o, int seed, bool first)>();
            foreach (var e in all)
            {
                var near = Window;
                List<PoolDeck> choices;
                do
                {
                    choices = pool.Where(p => p.Name != e.Name && Math.Abs(p.Elo - e.Elo) <= near).ToList();
                    near += 50;
                }
                while (choices.Count < MinOpponents && near < 2000);
                for (int g = 0; g < n; g++)
                {
                    int game = played + g;
                    games.Add((e, choices[e.Rng.Next(choices.Count)], seed + all.IndexOf(e) * 100003 + game, game % 2 == 0));
                }
            }
            var results = new (int winner, int turns, bool error)[games.Count];
            Parallel.For(0, games.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                i => results[i] = Play(cards, scripts, games[i].e.Deck, games[i].o.Deck, games[i].seed, games[i].first, skilled));
            for (int i = 0; i < games.Count; i++)
            {
                var (e, o, _, _) = games[i];
                var (winner, turns, error) = results[i];
                e.Turns += turns;
                if (error) { e.Errors++; continue; }
                if (winner == 0) e.Wins++;
                e.Results.Add((o.Elo, winner == 0 ? 1 : winner == 1 ? 0 : 0.5));
                e.MinOpponent = Math.Min(e.MinOpponent, o.Elo);
                e.MaxOpponent = Math.Max(e.MaxOpponent, o.Elo);
            }
            foreach (var e in all) e.Elo = Estimate(e.Results, e.Elo);
            if ((played / Batch + 1) % 5 == 0) Console.WriteLine($"  {played + n}/{rounds} duels a deck  ({timer.Elapsed:mm\\:ss})");
        }

        Console.WriteLine();
        Console.WriteLine("Calibration (pool decks re-rated as if new; anchored minus pool):");
        bool calibrated = true;
        foreach (var c in calibration.OrderByDescending(c => c.Pool))
        {
            double diff = c.Elo - c.Pool;
            bool ok = Math.Abs(diff) <= CalibrationTolerance;
            calibrated &= ok;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {c.Name,-26} pool {c.Pool,5:F0}  anchored {c.Elo,5:F0}  {diff,+5:F0}   won {Rate(c.Wins, c.Results.Count + c.Errors),3:F0}% of {c.Results.Count + c.Errors}{(c.Errors > 0 ? $", {c.Errors} errors" : "")}");
        }
        Console.WriteLine();
        var lines = new List<string>();
        int rank = 1;
        foreach (var e in fresh.OrderByDescending(e => e.Elo))
        {
            int games = e.Results.Count + e.Errors;
            var line = $"{rank++,3}. {e.Name,-26} Elo {e.Elo,5:F0}   won {Rate(e.Wins, games),3:F0}% of {games}   avg {e.Turns / (double)Math.Max(1, games),4:F1} turns"
                       + $"   start {e.Start:F0}, {e.Elo - e.Start,+4:F0}   pool opponents {e.MinOpponent:F0}-{e.MaxOpponent:F0}{(e.Errors > 0 ? $"   {e.Errors} errors" : "")}";
            Console.WriteLine(line);
            lines.Add(line);
        }
        Console.WriteLine();
        Console.WriteLine(calibrated ? $"Calibration within ±{CalibrationTolerance:F0}." : $"Calibration FAILED: a pool deck re-rated more than {CalibrationTolerance:F0} from its pool rating.");
        if (write && calibrated)
        {
            var path = Path.Combine(root, "campaign", DeckRatings.PeakFileName);
            var merged = DeckRatings.Load(path);
            foreach (var e in fresh) merged[e.Name] = Math.Round(e.Elo);
            var kept = File.Exists(path) ? File.ReadAllText(path).Split('\n').Where(l => DeckRatings.Parse(l).Count == 1).ToList() : new List<string>();
            var rows = fresh.Select(e => (e.Name, line: lines.First(l => DeckRatings.Parse(l).ContainsKey(e.Name))))
                .Concat(kept.Select(l => (Name: DeckRatings.Parse(l).Keys.First(), line: l)).Where(k => fresh.All(e => e.Name != k.Name)))
                .OrderByDescending(r => merged[r.Name]).ToList();
            var text = new List<string>
            {
                "Anchored deck ratings (docs/dev/NO-LIMITS-SPEC-2026-11-02.md P3): decks rated against the frozen pool of deck-ratings.txt,",
                "which they never move. Written by `--rate-anchored <folders> --write`; read after deck-ratings.txt (DeckRatings.Load).",
                $"Last run: {targets}, {rounds} duels a deck{(skilled ? ", skilled" : "")}, seed {seed}; calibration within ±{CalibrationTolerance:F0}.",
                "",
            };
            int r = 1;
            foreach (var (_, line) in rows) text.Add($"{r++,3}." + line.Substring(line.IndexOf('.') + 1));
            File.WriteAllText(path, string.Join("\n", text) + "\n");
            Console.WriteLine($"Wrote {rows.Count} ratings to {path}");
        }
        return calibrated ? 0 : 3;

        static double Rate(int wins, int games) => 100.0 * wins / Math.Max(1, games);
    }

    private static Deck Named(Deck d, string name) { d.Name = name; return d; }

    private static double Median(IEnumerable<double> values)
    {
        var v = values.OrderBy(x => x).ToList();
        return v.Count == 0 ? 1500 : v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }

    /// <summary>
    /// The maximum-likelihood rating against fixed opponents: the R where the expected score equals the score. A perfect
    /// or empty record stays within 400 of the opponents met.
    /// </summary>
    private static double Estimate(List<(double opponent, double score)> results, double current)
    {
        if (results.Count == 0) return current;
        double score = results.Sum(r => r.score);
        double lo = results.Min(r => r.opponent) - 400, hi = results.Max(r => r.opponent) + 400;
        double Expected(double r) => results.Sum(x => 1 / (1 + Math.Pow(10, (x.opponent - r) / 400)));
        if (score <= Expected(lo)) return lo;
        if (score >= Expected(hi)) return hi;
        for (int i = 0; i < 60; i++)
        {
            double mid = (lo + hi) / 2;
            if (Expected(mid) < score) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }

    /// <summary>One duel, `--rate-decks`' bot on both sides: 0 the new deck won, 1 the pool deck, 2 a draw or unfinished.</summary>
    private static (int winner, int turns, bool error) Play(ICardDataProvider cards, IScriptProvider scripts, Deck mine, Deck theirs, int gameSeed, bool mineFirst, bool skilled)
    {
        try
        {
            var first = mineFirst ? mine : theirs;
            var second = mineFirst ? theirs : mine;
            var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)gameSeed }, Deck0 = first, Deck1 = second };
            using var duel = recipe.Create();
            var context = new DuelContext(duel, cards);
            var answers = new ResponseLog();
            IDuelist Bot(int seat, Deck d)
            {
                if (!skilled) return new SmartDuelist($"p{seat}", gameSeed * 2 + seat, context, playbook: Playbooks.ForDeck(d));
                var style = new Personality { Name = $"p{seat}", Skill = 1.0 };
                var planner = new TurnPlanner(recipe, answers, style, () => Playbooks.ForDeck(d));
                return new SmartDuelist(style.Name, gameSeed * 2 + seat, context, null, style, Playbooks.ForDeck(d), planner);
            }
            var runner = new DuelRunner(duel, Bot(0, first), Bot(1, second), answers);
            runner.MessageReceived += context.Observe;
            var result = runner.Run(6000);
            int winner = result.Winner is 0 or 1 ? ((result.Winner == 0) == mineFirst ? 0 : 1) : 2;
            return (winner, result.Turns, false);
        }
        catch (Exception ex)
        {
            if (Environment.GetEnvironmentVariable("RATE_ERRORS") == "1") Console.WriteLine($"  error {mine.Name} vs {theirs.Name} (seed {gameSeed}): {ex.GetType().Name}: {ex.Message}");
            return (2, 0, true);
        }
    }
}
