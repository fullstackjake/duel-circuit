using System.Diagnostics;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;

namespace Ygo.Harness;

/// <summary>
/// `--round-robin a,b,c [--games N] [--seed N]`: the named decks against each other, every pair N times with the first
/// turn alternating, both sides played as the late campaign's CPUs (skill 1.0: the turn planner and the deck's playbook).
/// Prints each pairing, and each deck's wins going first and going second against this field, to say which decks want
/// which turn against the strongest opponents rather than against the whole campaign.
/// </summary>
public static class RoundRobin
{
    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, string deckList, int games, int seed)
    {
        var timer = Stopwatch.StartNew();
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        var names = deckList.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToArray();
        Deck Load(string name) { var d = Deck.LoadYdk(deckFiles[name]); d.Name = name; return d; }
        var style = new Personality { Id = "late-cpu", Name = "CPU", Skill = 1.0 };
        int n = names.Length;
        // wins[a, b, f]: a's wins against b, f = 0 when a went first, 1 when second; plays likewise.
        var wins = new int[n, n, 2];
        var plays = new int[n, n, 2];
        var pairs = new List<(int a, int b, int g)>();
        for (int a = 0; a < n; a++)
            for (int b = a + 1; b < n; b++)
                for (int g = 0; g < games; g++) pairs.Add((a, b, g));
        Console.WriteLine($"Round robin: {n} decks, {games} duels per pair (first turn alternating), skill {style.Skill:0.0} with planners, seed {seed}");
        Parallel.ForEach(pairs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, p =>
        {
            bool aFirst = p.g % 2 == 0;
            int first = aFirst ? p.a : p.b, second = aFirst ? p.b : p.a;
            int gameSeed = seed * 10000 + p.a * 1000 + p.b * 50 + p.g;
            int winner;
            try { winner = Play(cards, scripts, gameSeed, style, Load(names[first]), Load(names[second])); }
            catch (Exception ex) { Console.WriteLine($"    {names[first]} vs {names[second]} seed {gameSeed}: {ex.Message}"); return; }
            lock (wins)
            {
                plays[first, second, 0]++;
                plays[second, first, 1]++;
                if (winner == 0) wins[first, second, 0]++;
                else if (winner == 1) wins[second, first, 1]++;
            }
        });
        string Pct(int w, int t) => t == 0 ? "  -" : $"{w * 100 / t,3}%";
        Console.WriteLine($"\nRow deck's wins against column deck (going first / going second)");
        Console.Write($"{"",-22}");
        foreach (var name in names) Console.Write($"{Short(name),14}");
        Console.WriteLine();
        for (int a = 0; a < n; a++)
        {
            Console.Write($"{names[a],-22}");
            for (int b = 0; b < n; b++)
                Console.Write(a == b ? $"{"",14}" : $"{Pct(wins[a, b, 0], plays[a, b, 0])} / {Pct(wins[a, b, 1], plays[a, b, 1])}".PadLeft(14));
            Console.WriteLine();
        }
        Console.WriteLine($"\n{"deck",-22} {"overall",8} {"first",8} {"second",8}   wants");
        foreach (var a in Enumerable.Range(0, n).OrderByDescending(a => Total(a, 0) + Total(a, 1)))
        {
            int wf = Total(a, 0), ws = Total(a, 1), pf = Plays(a, 0), ps = Plays(a, 1);
            int diff = (pf == 0 ? 0 : wf * 100 / pf) - (ps == 0 ? 0 : ws * 100 / ps);
            var wants = Math.Abs(diff) < 10 ? "either" : diff > 0 ? "first" : "second";
            Console.WriteLine($"{names[a],-22} {Pct(wf + ws, pf + ps),8} {Pct(wf, pf),8} {Pct(ws, ps),8}   {wants} ({diff:+0;-0} points first)");
        }
        Console.WriteLine($"\nDone in {timer.Elapsed.TotalMinutes:0.0} min");
        return 0;

        int Total(int a, int f) => Enumerable.Range(0, n).Sum(b => wins[a, b, f]);
        int Plays(int a, int f) => Enumerable.Range(0, n).Sum(b => plays[a, b, f]);
        static string Short(string name) => name.Length <= 13 ? name : name.Substring(0, 13);
    }

    /// <summary>
    /// `--routes-ab deck opponent,opponent,... [--games N]`: what a deck's combo routes are worth. The deck plays every
    /// opponent N times (first turn alternating) with its routes off, on as written, and on with the planner free to override them, on the same seeds, both sides played as the
    /// late CPUs. Prints its wins each way, overall and going first / second.
    /// </summary>
    public static int RoutesAb(ICardDataProvider cards, IScriptProvider scripts, string root, string deckName, string opponents, int games, int seed)
    {
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        Deck Load(string name) { var d = Deck.LoadYdk(deckFiles[name]); d.Name = name; return d; }
        var style = new Personality { Id = "late-cpu", Name = "CPU", Skill = 1.0 };
        var mine = Load(deckName);
        var theirs = opponents.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).Select(Load).ToList();
        var jobs = theirs.SelectMany((t, ti) => Enumerable.Range(0, games).Select(g => (t, ti, g))).ToList();
        Console.WriteLine($"Routes A/B: {deckName} against {theirs.Count} decks, {games} duels each (first turn alternating), seed {seed}");
        // ROUTES_AB_MODES=on: only the routes-on mode (to compare other settings, run once per setting).
        var modes = Environment.GetEnvironmentVariable("ROUTES_AB_MODES") == "on"
            ? new[] { (true, false) } : new[] { (false, false), (true, false), (true, true) };
        foreach (var (on, overridable) in modes)
        {
            Playbook.RoutesEnabled = on;
            Playbook.PlannerOverridesRoutes = overridable;
            int[] wins = new int[2], plays = new int[2];
            Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, j =>
            {
                bool meFirst = j.g % 2 == 0;
                int gameSeed = seed * 10000 + j.ti * 100 + j.g;
                int winner;
                try { winner = Play(cards, scripts, gameSeed, style, meFirst ? mine : j.t, meFirst ? j.t : mine); }
                catch (Exception ex) { Console.WriteLine($"    {j.t.Name} seed {gameSeed}: {ex.Message}"); return; }
                lock (wins)
                {
                    plays[meFirst ? 0 : 1]++;
                    if (winner == (meFirst ? 0 : 1)) wins[meFirst ? 0 : 1]++;
                }
            });
            string Pct(int w, int t) => t == 0 ? "-" : $"{w * 100 / t}%";
            Console.WriteLine($"  routes {(!on ? "off" : overridable ? "on, planner may override" : "on, as written")}: won {Pct(wins[0] + wins[1], plays[0] + plays[1])} ({wins[0] + wins[1]}/{plays[0] + plays[1]}); first {Pct(wins[0], plays[0])}, second {Pct(wins[1], plays[1])}");
        }
        Playbook.RoutesEnabled = true;
        Playbook.PlannerOverridesRoutes = false;
        return 0;
    }

    /// <summary>
    /// `--playbook-ab deck opponent,opponent,... [--games N]`: what a deck's playbook is worth. The deck plays every
    /// opponent N times (first turn alternating) without its playbook and with it, on the same seeds, both sides played
    /// as the late CPUs. Prints its wins each way, overall and going first / second.
    /// </summary>
    public static int PlaybookAb(ICardDataProvider cards, IScriptProvider scripts, string root, string deckName, string opponents, int games, int seed)
    {
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        Deck Load(string name) { var d = Deck.LoadYdk(deckFiles[name]); d.Name = name; return d; }
        var style = new Personality { Id = "late-cpu", Name = "CPU", Skill = 1.0 };
        var mine = Load(deckName);
        var theirs = opponents.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).Select(Load).ToList();
        var jobs = theirs.SelectMany((t, ti) => Enumerable.Range(0, games).Select(g => (t, ti, g))).ToList();
        Console.WriteLine($"Playbook A/B: {deckName} against {theirs.Count} decks, {games} duels each (first turn alternating), seed {seed}");
        foreach (var on in new[] { false, true })
        {
            if (on) Playbooks.Off.Remove(deckName); else Playbooks.Off.Add(deckName);
            int[] wins = new int[2], plays = new int[2];
            var byOpponent = new int[theirs.Count];
            Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, j =>
            {
                bool meFirst = j.g % 2 == 0;
                int gameSeed = seed * 10000 + j.ti * 100 + j.g;
                int winner;
                try { winner = Play(cards, scripts, gameSeed, style, meFirst ? mine : j.t, meFirst ? j.t : mine); }
                catch (Exception ex) { Console.WriteLine($"    {j.t.Name} seed {gameSeed}: {ex.Message}"); return; }
                lock (wins)
                {
                    plays[meFirst ? 0 : 1]++;
                    if (winner == (meFirst ? 0 : 1)) { wins[meFirst ? 0 : 1]++; byOpponent[j.ti]++; }
                }
            });
            string Pct(int w, int t) => t == 0 ? "-" : $"{w * 100 / t}%";
            Console.WriteLine($"  playbook {(on ? "on " : "off")}: won {Pct(wins[0] + wins[1], plays[0] + plays[1])} ({wins[0] + wins[1]}/{plays[0] + plays[1]}); first {Pct(wins[0], plays[0])}, second {Pct(wins[1], plays[1])}");
            Console.WriteLine("    " + string.Join(", ", theirs.Select((t, i) => $"{t.Name} {byOpponent[i]}/{games}")));
        }
        Playbooks.Off.Remove(deckName);
        return 0;
    }

    /// <summary>One duel: the first deck takes turn 1. Returns the winner (0 first, 1 second, 2 draw).</summary>
    private static int Play(ICardDataProvider cards, IScriptProvider scripts, int gameSeed, Personality style, Deck first, Deck second)
    {
        var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)gameSeed }, Deck0 = first.Clone(), Deck1 = second.Clone() };
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var answers = new ResponseLog();
        IDuelist Bot(Deck deck, int seat) => new SmartDuelist(style.Name, gameSeed * 2 + seat, context, null, style, Playbooks.ForDeck(deck),
            new TurnPlanner(recipe, answers, style, () => Playbooks.ForDeck(deck)));
        var kept = ReplayKeeper.Current?.Begin("round-robin", $"s{gameSeed}-{first.Name}-vs-{second.Name}", recipe, duel, answers, new[] { style.Name, style.Name },
            new[] { SeatSpec.Of(0, style, first, gameSeed * 2, true), SeatSpec.Of(1, style, second, gameSeed * 2 + 1, true) });
        var runner = new DuelRunner(duel, ReplayKeeper.Wrap(kept, Bot(first, 0)), ReplayKeeper.Wrap(kept, Bot(second, 1)), answers);
        runner.MessageReceived += context.Observe;
        YataLock.Attach(runner, duel);
        ReplayKeeper.Attach(kept, runner);
        var result = runner.Run(20000);
        ReplayKeeper.Current?.Finish(kept, result);
        return result.Winner is 0 or 1 ? result.Winner : 2;
    }
}
