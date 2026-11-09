using System.Text.RegularExpressions;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// Phase 0 for the Format Library (POST-GAME-PLAN step 2; the cups are campaign/library.txt):
/// <list type="bullet">
/// <item>`--library-check`: every cup deck as a player's loaner under its cup's list and pool: what the list takes, and
/// any card not yet released in the format (a deck built after its pool date needs a pool-filtered build). CPU decks
/// over the list are "visitors" and keep their cards.</item>
/// <item>`--library-plan`: per cup, the loaners to measure: the three highest-rated decks (campaign/deck-ratings.txt) whose
/// every card was out by the format's end, plus the boss's own deck (the mirror).</item>
/// <item>`--library-study [--games N] [--only text]`: runs `--circuit-study` for each of those at its cup (era-limited:
/// the cup's list), merging every match's result into campaign/library-advice.txt. Long: run it overnight.</item>
/// <item>`--library-summary`: the best measured option per match against the targets (65% at openers, 60% in the
/// middle, 55% at the boss), and the walls that need `--wall-study` or a Champion's table.</item>
/// </list>
/// </summary>
public static class LibraryStudy
{
    public const string CupsFile = "campaign/library.txt";
    public const string AdviceFile = "campaign/library-advice.txt";

    private sealed class Setup
    {
        public Circuits Cups = null!;
        public List<Banlist> Lists = null!;
        public Dictionary<string, string> DeckFiles = null!;
        public Dictionary<string, double> Elo = null!;
        public PrintingIndex? Printings;

        public Deck Load(string name)
        {
            var d = Deck.LoadYdk(DeckFiles[name]);
            d.Name = name;
            return d;
        }

        public Banlist ListOf(Circuit cup) => Lists.FirstOrDefault(l => l.Name == cup.Banlist) ?? Banlist.None;

        /// <summary>The cup's pool: cards first printed by its `pool:` month (the proposal's dates), else the list's format end.</summary>
        public Dictionary<string, string> Pools = new Dictionary<string, string>();

        public string FormatEnd(Circuit cup) => Pools.TryGetValue(cup.Name, out var pool) ? pool : DeckRules.FormatEnd(ListOf(cup), Lists);
    }

    private static Setup Load(string root)
    {
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities"));
        var setup = new Setup
        {
            Cups = Circuits.Load(Path.Combine(root, CupsFile), roster),
            Lists = Banlist.LoadFolders(new[] { Path.Combine(root, "vendor", "LFLists"), Path.Combine(root, "lflists") }).ToList(),
            DeckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
                .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First()),
            Elo = new Dictionary<string, double>(),
        };
        string? current = null;
        foreach (var line in File.ReadLines(Path.Combine(root, CupsFile)))
        {
            if (line.StartsWith("circuit:")) current = line.Substring(8).Trim();
            else if (line.StartsWith("pool:") && current != null) setup.Pools[current] = line.Substring(5).Trim();
        }
        var printingsFile = Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin");
        if (File.Exists(printingsFile)) setup.Printings = PrintingIndex.Load(printingsFile);
        setup.Elo = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName));
        return setup;
    }

    /// <summary>Share of a deck's cards (copies) released by the format's end.</summary>
    private static double Fit(Deck deck, string formatEnd, Setup s, ICardDataProvider cards)
    {
        var all = deck.Main.Concat(deck.Extra).ToList();
        return all.Count == 0 ? 0 : all.Count(c => DeckRules.Released(Banlist.BaseCode(c, cards), formatEnd, s.Printings, cards)) / (double)all.Count;
    }

    public static int Check(ICardDataProvider cards, string root)
    {
        var s = Load(root);
        int problems = 0;
        foreach (var cup in s.Cups.All)
        {
            var list = s.ListOf(cup);
            var end = s.FormatEnd(cup);
            Console.WriteLine($"\n{cup.Name} ({cup.Banlist}; cards out by {(end.Length > 0 ? end : "now")})");
            if (list == Banlist.None) { Console.WriteLine($"  no list named {cup.Banlist}"); problems++; continue; }
            foreach (var match in cup.Matches)
            {
                if (!s.DeckFiles.ContainsKey(match.Deck)) { Console.WriteLine($"  {match.Deck}: no deck file"); problems++; continue; }
                var deck = s.Load(match.Deck);
                var over = DeckRules.Validate(deck, list, cards).Count;
                var adjusted = Loaners.Adjust(deck, list, cards);
                var late = DeckRules.Releases(adjusted.Deck, list, end, s.Printings, cards);
                var listProblems = DeckRules.Validate(adjusted.Deck, list, cards);
                string Note() => (over > 0 ? $"a visitor as a CPU ({over} over the list); " : "")
                    + (adjusted.Changes.Count > 0 ? $"its loaner changes {adjusted.Changes.Count}; " : "")
                    + (late.Count > 0 ? $"{late.Count} card{(late.Count == 1 ? "" : "s")} not out yet: {string.Join(", ", late.Take(4).Select(p => p.Message.Split(':')[0]))}{(late.Count > 4 ? "…" : "")}; " : "");
                Console.WriteLine($"  {match.Character.Name,-22} {match.Deck,-22} Elo {(s.Elo.TryGetValue(match.Deck, out var e) ? e.ToString("0") : "?"),5}  {Note()}"
                    + (listProblems.Count > 0 ? $"LOANER STILL ILLEGAL: {listProblems[0].Message}" : "loaner legal"));
                if (listProblems.Count > 0 || late.Count > 0) problems++;
            }
        }
        Console.WriteLine($"\n{problems} cup deck{(problems == 1 ? "" : "s")} need attention (a pool-filtered build, or a fix to the list)");
        return 0;
    }

    /// <summary>The decks to measure at a cup: the three best-rated decks entirely out by its format's end, and the boss's.</summary>
    private static List<string> Candidates(Circuit cup, Setup s, ICardDataProvider cards, int count = 3)
    {
        var end = s.FormatEnd(cup);
        var boss = cup.Matches[cup.Matches.Count - 1].Deck;
        var fitting = s.Elo.Keys.Where(d => s.DeckFiles.ContainsKey(d) && d != boss && !s.DeckFiles[d].Contains("structure"))
            .Where(d => Fit(s.Load(d), end, s, cards) >= 1.0)
            .OrderByDescending(d => s.Elo[d]).Take(count).ToList();
        fitting.Add(boss);
        return fitting;
    }

    public static int Plan(ICardDataProvider cards, string root)
    {
        var s = Load(root);
        foreach (var cup in s.Cups.All)
        {
            var candidates = Candidates(cup, s, cards, Environment.GetEnvironmentVariable("LIBRARY_PLAN_ALL") == "1" ? int.MaxValue : 3);
            Console.WriteLine($"{cup.Name,-28} {string.Join(", ", candidates.Select(d => $"{d} {(s.Elo.TryGetValue(d, out var e) ? e.ToString("0") : "?")}"))}");
        }
        return 0;
    }

    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, int games, int seed, string only)
    {
        var s = Load(root);
        var advice = Path.Combine(root, AdviceFile);
        foreach (var cup in s.Cups.All.Where(c => only.Length == 0 || c.Name.Contains(only, StringComparison.OrdinalIgnoreCase)))
            foreach (var deck in Candidates(cup, s, cards))
            {
                StudyFiles.Circuits = Path.Combine(root, CupsFile);
                CircuitStudy.Run(cards, scripts, root, games, seed, cup.Name, deck, eraLimited: true, write: advice);
            }
        return Summary(root);
    }

    /// <summary>The best option per match (by measured win rate) against the targets.</summary>
    public static int Summary(string root)
    {
        var s = Load(root);
        var advice = WallAdvice.Load(Path.Combine(root, AdviceFile));
        int walls = 0, measured = 0;
        for (int c = 0; c < s.Cups.All.Count; c++)
        {
            var cup = s.Cups.All[c];
            Console.WriteLine($"\n{cup.Name}");
            for (int m = 0; m < cup.Matches.Count; m++)
            {
                var options = advice.For(Profile.MatchKey(c, m)).OrderByDescending(o => o.Percent).ToList();
                // The boss is the last match before a Champion's table; the table is held to the boss's mark.
                int target = m == 0 ? 65 : m >= cup.Matches.FindLastIndex(x => !x.Champion) ? 55 : 60;
                if (options.Count == 0) { Console.WriteLine($"  match {m + 1}: {cup.Matches[m].Deck,-22} not measured yet"); continue; }
                measured++;
                var best = options[0];
                // A Champion's table is optional: under target it's the hard table it's meant to be, not a wall.
                bool table = cup.Matches[m].Champion, wall = best.Percent < target && !table;
                if (wall) walls++;
                var toss = best.YouFirst >= 0 && best.YouSecond >= 0 ? $" (first {best.YouFirst}%, second {best.YouSecond}%)" : "";
                Console.WriteLine($"  match {m + 1}: {cup.Matches[m].Deck,-22} best {best.Deck,-22} {best.Percent,3}%{toss}  target {target}%{(wall ? "  WALL" : table && best.Percent < target ? "  (Champion's table)" : "")}"
                    + $"   others: {string.Join(", ", options.Skip(1).Take(3).Select(o => $"{o.Deck} {o.Percent}%"))}");
            }
        }
        Console.WriteLine($"\n{measured} matches measured, {walls} under target");
        return 0;
    }
}

/// <summary>Which campaign file the studies read (`--circuits file`): the circuits, or the Library's cups.</summary>
public static class StudyFiles
{
    public static string? Circuits;

    public static string CircuitsPath(string root) => Circuits ?? Path.Combine(root, "campaign", "circuits.txt");
}
