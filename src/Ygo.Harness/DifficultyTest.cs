using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--difficulty [--games N] [--seed N]`: how the CPU's rules sense changed campaign difficulty. Every campaign match
/// is played N times by the campaign sim's casual player (skill 0.6, the old CPU), with decks of the strength they'd
/// have by then (the previous circuit's opponents' decks, in turn), against the match's character and deck: once with
/// the old opponent CPU (no rules sense, playbooks from skill 0.7), once with the new (rules sense, playbooks from
/// 0.5), on the same game seeds. Prints the player's win rate per circuit and the matches that changed most.
/// </summary>
public static class DifficultyTest
{
    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, int games, int seed)
    {
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities"));
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        Deck? Load(string name)
        {
            if (!deckFiles.TryGetValue(name, out var f)) return null;
            var d = Deck.LoadYdk(f);
            d.Name = name;
            return d;
        }
        var player = new Personality { Name = "Player", Skill = 0.6 };
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var rows = new List<(int circuit, string match, double before, double after)>();
        Console.WriteLine($"{circuits.All.Count} circuits, {games} games per match and side");
        for (int ci = 0; ci < circuits.All.Count; ci++)
        {
            var circuit = circuits.All[ci];
            // The player's decks: what the previous circuit's opponents played (for the first circuit, its own).
            var pool = circuits.All[Math.Max(0, ci - 1)].Matches.Select(m => Load(m.Deck)).Where(d => d != null).Select(d => d!).ToList();
            double totalBefore = 0, totalAfter = 0;
            int counted = 0;
            for (int mi = 0; mi < circuit.Matches.Count; mi++)
            {
                var match = circuit.Matches[mi];
                var theirs = Load(match.Deck);
                if (theirs == null || pool.Count == 0) continue;
                var wins = new int[2];
                Parallel.For(0, games * 2, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, k =>
                {
                    int g = k / 2;
                    bool sense = k % 2 == 1;
                    int gameSeed = seed * 100000 + ci * 1000 + mi * 50 + g;
                    var mine = pool[g % pool.Count];
                    if (PlayerWins(cards, scripts, mine, theirs, player, match.Character, sense, gameSeed)) Interlocked.Increment(ref wins[sense ? 1 : 0]);
                });
                double before = wins[0] / (double)games, after = wins[1] / (double)games;
                rows.Add((ci, $"{circuit.Name} match {mi + 1} ({match.Character.Name}, {match.Deck}, skill {match.Character.Skill})", before, after));
                totalBefore += before;
                totalAfter += after;
                counted++;
            }
            if (counted > 0)
                Console.WriteLine($"{ci + 1,2}. {circuit.Name,-40} player wins {totalBefore / counted,6:P0} -> {totalAfter / counted,6:P0}   ({timer.Elapsed:mm\\:ss})");
        }
        Console.WriteLine();
        Console.WriteLine("Matches that changed most (player win rate, old opponent CPU -> new):");
        foreach (var r in rows.OrderBy(r => r.after - r.before).Take(12))
            Console.WriteLine($"  {r.before,5:P0} -> {r.after,5:P0}  {r.match}");
        foreach (var r in rows.OrderByDescending(r => r.after - r.before).Take(4).Where(r => r.after > r.before))
            Console.WriteLine($"  {r.before,5:P0} -> {r.after,5:P0}  {r.match}  (easier)");
        Console.WriteLine($"Overall: player wins {rows.Average(r => r.before):P0} of matches' games before, {rows.Average(r => r.after):P0} after.");
        return 0;
    }

    /// <summary>
    /// `--late-decks [--games N] [--from C]`: is the late campaign a deck wall or a skill wall? From circuit C (default 14)
    /// on, every match is played with the player counter-picking: each of six candidate decks gets N games, and the
    /// best one counts. Three set-ups: the top-rated decks unlocked by then (every earlier opponent's deck, the Recipe
    /// Book) with the casual player, the same with a skilled player (plans ahead, knows its deck), and the top-rated
    /// decks in the whole game with the skilled player. Decks are made legal for the circuit's ban list first.
    /// </summary>
    public static int LateDecks(ICardDataProvider cards, IScriptProvider scripts, string root, int games, int seed, int from)
    {
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities"));
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var lists = Banlist.LoadFolders(new[] { Path.Combine(root, "vendor", "LFLists"), Path.Combine(root, "lflists") });
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        var elo = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName));
        Deck Load(string name) { var d = Deck.LoadYdk(deckFiles[name]); d.Name = name; return d; }
        // Owning every card of every deck (three copies), so only the ban list changes a deck.
        var owned = new Collection();
        foreach (var name in deckFiles.Keys)
            foreach (var code in Load(name).Main.Concat(Load(name).Extra).Distinct())
                owned.Add(Banlist.BaseCode(code, cards), Rarity.Common, 3);
        var casual = new Personality { Name = "Player", Skill = 0.6 };
        var skilled = new Personality { Name = "Player", Skill = 0.9 };
        var topAll = elo.OrderByDescending(e => e.Value).Select(e => e.Key).Where(deckFiles.ContainsKey).Take(6).ToList();
        var setups = new[] { "unlocked decks, casual player", "unlocked decks, skilled player", "top-6 decks of the game, skilled player" };
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"Top-6 decks overall: {string.Join(", ", topAll)}");
        var circuitAverages = new List<string>();
        for (int ci = Math.Max(0, from - 1); ci < circuits.All.Count; ci++)
        {
            var circuit = circuits.All[ci];
            var list = circuit.Banlist.Length > 0 ? lists.FirstOrDefault(l => l.Name == circuit.Banlist) ?? Banlist.None : Banlist.None;
            var unlocked = circuits.All.Take(ci).SelectMany(c => c.Matches.Select(m => m.Deck)).Distinct().Where(deckFiles.ContainsKey)
                .OrderByDescending(d => elo.TryGetValue(d, out var e) ? e : 0).Take(6).ToList();
            Deck Legal(string name) { var r = AutoDeck.Fix(Load(name), owned, list, cards).Deck; r.Name = name; return r; }
            var pools = new[] { unlocked, unlocked, topAll };
            Console.WriteLine();
            Console.WriteLine($"== {ci + 1}. {circuit.Name} ({circuit.Banlist}); unlocked: {string.Join(", ", unlocked.Select(d => $"{d} {elo.GetValueOrDefault(d):0}"))}");
            var sums = new double[3];
            foreach (var (match, mi) in circuit.Matches.Select((m, i) => (m, i)))
            {
                if (!deckFiles.ContainsKey(match.Deck)) continue;
                var theirs = Load(match.Deck);
                var line = $"   {match.Character.Name} ({match.Deck} {elo.GetValueOrDefault(match.Deck):0}, skill {match.Character.Skill}):";
                for (int s = 0; s < 3; s++)
                {
                    var pilot = s == 0 ? casual : skilled;
                    var pool = pools[s].Select(Legal).ToList();
                    var wins = new int[pool.Count];
                    Parallel.For(0, pool.Count * games, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, k =>
                    {
                        int di = k / games, g = k % games;
                        int gameSeed = seed * 100000 + ci * 1000 + mi * 50 + g;
                        if (PlayerWins(cards, scripts, pool[di], theirs, pilot, match.Character, true, gameSeed, playerKnowsDeck: s > 0))
                            Interlocked.Increment(ref wins[di]);
                    });
                    int best = Enumerable.Range(0, pool.Count).OrderByDescending(i => wins[i]).First();
                    double rate = wins[best] / (double)games;
                    sums[s] += rate;
                    line += $"  [{(s == 0 ? "A" : s == 1 ? "B" : "C")}] {rate:P0} with {pool[best].Name}";
                }
                Console.WriteLine(line);
            }
            int n = circuit.Matches.Count(m => deckFiles.ContainsKey(m.Deck));
            var avg = $"{ci + 1,2}. {circuit.Name,-38} A {sums[0] / n,5:P0}   B {sums[1] / n,5:P0}   C {sums[2] / n,5:P0}   ({timer.Elapsed:hh\\:mm\\:ss})";
            circuitAverages.Add(avg);
            Console.WriteLine(avg);
        }
        Console.WriteLine();
        Console.WriteLine($"Player win rate with the best deck for each match (A: {setups[0]}; B: {setups[1]}; C: {setups[2]}):");
        foreach (var a in circuitAverages) Console.WriteLine(a);
        return 0;
    }

    /// <summary>
    /// `--playbook-test [--games N] [--from C] [--pilot casual]`: what the late bosses' deck playbooks are worth. From
    /// circuit C (default 14) on, every match is played N times by the skilled player (plans ahead, knows its deck; with the
    /// top decks it has unlocked by then, in turn) against the match's character and deck: once with the opponent's
    /// playbook taken away, once with it, on the same seeds.
    /// Prints the opponent's win rate per match, without and with.
    /// </summary>
    public static int PlaybookValue(ICardDataProvider cards, IScriptProvider scripts, string root, int games, int seed, int from, string pilot)
    {
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities"));
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        Deck Load(string name) { var d = Deck.LoadYdk(deckFiles[name]); d.Name = name; return d; }
        var elo = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName));
        bool skilled = pilot != "casual";
        var player = new Personality { Name = "Player", Skill = skilled ? 0.9 : 0.6 };
        var timer = System.Diagnostics.Stopwatch.StartNew();
        double sumOff = 0, sumOn = 0;
        int counted = 0;
        Console.WriteLine($"Late bosses with and without their playbooks, {games} games each against the {(skilled ? "skilled" : "casual")} player");
        for (int ci = Math.Max(0, from - 1); ci < circuits.All.Count; ci++)
        {
            var circuit = circuits.All[ci];
            var pool = circuits.All.Take(ci).SelectMany(c => c.Matches.Select(m => m.Deck)).Distinct().Where(deckFiles.ContainsKey)
                .OrderByDescending(d => elo.TryGetValue(d, out var e) ? e : 0).Take(6).Select(Load).ToList();
            Console.WriteLine($"== {ci + 1}. {circuit.Name}");
            foreach (var (match, mi) in circuit.Matches.Select((m, i) => (m, i)))
            {
                if (!deckFiles.ContainsKey(match.Deck) || Playbooks.ForDeck(match.Deck) == null) continue;
                var theirs = Load(match.Deck);
                var wins = new int[2];
                Parallel.For(0, games * 2, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, k =>
                {
                    int g = k / 2;
                    bool on = k % 2 == 1;
                    int gameSeed = seed * 100000 + ci * 1000 + mi * 50 + g;
                    if (!PlayerWins(cards, scripts, pool[g % pool.Count], theirs, player, match.Character, true, gameSeed,
                            playerKnowsDeck: skilled, opponentPlaybook: on))
                        Interlocked.Increment(ref wins[on ? 1 : 0]);
                });
                double off = wins[0] / (double)games, with = wins[1] / (double)games;
                sumOff += off; sumOn += with; counted++;
                Console.WriteLine($"   {match.Deck,-22} {match.Character.Name,-22} boss wins {off,5:P0} -> {with,5:P0}  {(with - off) * 100,4:+0;-0;0}   ({timer.Elapsed:hh\\:mm\\:ss})");
            }
        }
        Console.WriteLine($"Overall: the late bosses win {sumOff / counted:P0} without playbooks, {sumOn / counted:P0} with.");
        return 0;
    }

    /// <summary>
    /// `--deck-ab &lt;deck&gt; &lt;opponent deck&gt; [--games N]`: what a deck's playbook is worth. The skilled player (plans ahead,
    /// rules sense) pilots the deck N times with its playbook and N times without, on the same seeds, against a skilled
    /// opponent who knows its own deck. Prints both win rates.
    /// </summary>
    public static int DeckAb(ICardDataProvider cards, IScriptProvider scripts, string root, string deckName, string opponentName, int games, int seed)
    {
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        Deck Load(string name) { var d = Deck.LoadYdk(deckFiles[name]); d.Name = name; return d; }
        var mine = Load(deckName);
        var theirs = Load(opponentName);
        var player = new Personality { Name = "Player", Skill = 0.9 };
        var opponent = new Personality { Name = "Opponent", Skill = 0.9 };
        var wins = new int[2];
        Parallel.For(0, games * 2, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, k =>
        {
            bool on = k % 2 == 1;
            int gameSeed = seed * 100000 + k / 2;
            // Without the playbook: the same deck under another name, so no playbook matches it.
            var deck = mine.Clone();
            if (!on) deck.Name = deckName + "-no-playbook";
            if (PlayerWins(cards, scripts, deck, theirs, player, opponent, true, gameSeed, playerKnowsDeck: true, opponentPlaybook: true))
                Interlocked.Increment(ref wins[on ? 1 : 0]);
        });
        Console.WriteLine($"{deckName} vs {opponentName}, {games} games each: {wins[0] / (double)games:P0} without its playbook, {wins[1] / (double)games:P0} with");
        return 0;
    }

    private static bool PlayerWins(ICardDataProvider cards, IScriptProvider scripts, Deck mine, Deck theirs, Personality pilot, Personality opponent,
        bool sense, int gameSeed, bool playerKnowsDeck = false, bool? opponentPlaybook = null)
    {
        bool meFirst = (gameSeed & 1) == 0;
        var decks = meFirst ? new[] { mine, theirs } : new[] { theirs, mine };
        var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)gameSeed }, Deck0 = decks[0].Clone(), Deck1 = decks[1].Clone() };
        try
        {
            using var duel = recipe.Create();
            var context = new DuelContext(duel, cards);
            var answers = new ResponseLog();
            int mySeat = meFirst ? 0 : 1;
            var seats = new IDuelist[2];
            // The player: the old CPU by default (for the before/after comparison); a skilled player plans ahead and
            // knows its deck, with today's rules sense.
            var myDeck = mine.Name;
            var myPlanner = playerKnowsDeck && pilot.PlansAhead ? new TurnPlanner(recipe, answers, pilot, () => Playbooks.ForDeck(myDeck)) : null;
            seats[mySeat] = new SmartDuelist(pilot.Name, gameSeed * 2 + mySeat, context, null, pilot, playerKnowsDeck ? Playbooks.ForDeck(myDeck) : null, myPlanner)
                { UseRulesSense = playerKnowsDeck };
            // The opponent as the campaign plays them: their playbook from the skill bar of the CPU being measured.
            bool knows = opponentPlaybook ?? opponent.Skill >= (sense ? 0.5 : 0.7);
            var deckName = theirs.Name;
            var planner = opponent.PlansAhead ? new TurnPlanner(recipe, answers, opponent, () => knows ? Playbooks.ForDeck(deckName) : null) : null;
            seats[1 - mySeat] = new SmartDuelist(opponent.Name, gameSeed * 2 + 1 - mySeat, context, null, opponent, knows ? Playbooks.ForDeck(deckName) : null, planner)
                { UseRulesSense = sense };
            var runner = new DuelRunner(duel, seats[0], seats[1], answers);
            runner.MessageReceived += context.Observe;
            YataLock.Attach(runner, duel);
            return runner.Run(8000).Winner == mySeat;
        }
        catch
        {
            return false;
        }
    }
}
