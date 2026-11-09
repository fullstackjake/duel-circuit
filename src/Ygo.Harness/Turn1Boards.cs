using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;

namespace Ygo.Harness;

/// <summary>
/// `--turn1 a,b,c [--games N] [--seed N]`: each deck's turn-1 end board as the late CPUs build it (skill 1.0, the turn
/// planner and the deck's playbook), going first against an opponent that only passes. Prints every board (monsters,
/// set and face-up Spells/Traps, cards left in hand) and how many actions the turn took, to compare with the boards real
/// players make with the deck.
/// </summary>
public static class Turn1Boards
{
    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, string deckList, int games, int seed)
    {
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        var style = new Personality { Id = "late-cpu", Name = "CPU", Skill = 1.0 };
        var passer = Deck.LoadYdk(deckFiles["classic"]);
        foreach (var name in deckList.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0))
        {
            var deck = Deck.LoadYdk(deckFiles[name]);
            deck.Name = name;
            Console.WriteLine($"\n{name}: turn-1 boards ({games} hands)");
            double monsters = 0, backrow = 0, actions = 0;
            // TURN1_WATCH=<deck>: this deck goes first, then that deck plays its turn into our board; prints what our CPU
            // does with the board during their turn (the interruptions it uses, and on what) and what's left after.
            if (Environment.GetEnvironmentVariable("TURN1_WATCH") is string watch && watch.Length > 0)
            {
                var theirs = Deck.LoadYdk(deckFiles[watch]);
                theirs.Name = watch;
                double used = 0, ourLeft = 0, theirBoard = 0, lpLost = 0;
                for (int g = 0; g < games; g++)
                {
                    int gameSeed = seed * 1000 + g;
                    var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)gameSeed }, Deck0 = deck.Clone(), Deck1 = theirs.Clone() };
                    using var duel = recipe.Create();
                    var context = new DuelContext(duel, cards);
                    var answers = new ResponseLog();
                    var log = new List<string>();
                    bool theirTurn = false;
                    SmartDuelist Cpu(Deck d, int seat, Action<string>? l) => new SmartDuelist(seat == 0 ? "WE" : "THEY", gameSeed * 2 + seat, context, l, style, Playbooks.ForDeck(d),
                        new TurnPlanner(recipe, answers, style, () => Playbooks.ForDeck(d)));
                    bool verboseAll = Environment.GetEnvironmentVariable("TURN1_VERBOSE") != null;
                    var runner = new DuelRunner(duel, Cpu(deck, 0, m => { if (theirTurn || verboseAll) log.Add((theirTurn ? "" : "[our turn] ") + m); }), Cpu(theirs, 1, m => { if (theirTurn) log.Add(m); }), answers);
                    runner.MessageReceived += context.Observe;
                    int ourChains = 0;
                    runner.MessageReceived += m =>
                    {
                        if (theirTurn && m[0] == Msg.Chaining)
                        {
                            var r = new Ygo.Engine.Protocol.MessageReader(m, 1);
                            var code = r.U32();
                            r.LocInfo(code);
                            if (r.U8() == 0) { ourChains++; log.Add($"  >> we chain {cards.Get(code)?.Name ?? code.ToString()}"); }
                        }
                    };
                    string Names(IEnumerable<FieldCard?> list) => string.Join(", ", list.Where(c => c != null).Select(c => c!.Code == 0 ? "(set)" : cards.Get(c!.Code)?.Name ?? c!.Code.ToString()));
                    string ourBoard = "";
                    while (runner.Result.Turns < 3 && runner.Step())
                        if (runner.Result.Turns == 2 && !theirTurn)
                        {
                            theirTurn = true;
                            var me0 = context.Board.Players[0];
                            ourBoard = $"{Names(me0.Monsters)} | {Names(me0.SpellTraps.Take(6))} | {me0.Hand.Count} in hand";
                        }
                    var us = context.Board.Players[0];
                    var them = context.Board.Players[1];
                    used += ourChains;
                    ourLeft += us.Monsters.Count(c => c != null);
                    theirBoard += them.Monsters.Count(c => c != null);
                    lpLost += 8000 - (runner.Result.Winner == 1 ? 0 : us.LifePoints);
                    Console.WriteLine($"  game {g + 1}: our board [{ourBoard}]");
                    bool all = Environment.GetEnvironmentVariable("TURN1_VERBOSE") != null;
                    foreach (var line in log.Where(l => all || l.StartsWith("  >>") || l.StartsWith("WE"))) Console.WriteLine($"      {line}");
                    Console.WriteLine($"    after their turn: ours [{Names(us.Monsters)}], theirs [{Names(them.Monsters)}], our LP {(runner.Result.Winner == 1 ? 0 : us.LifePoints)}");
                }
                Console.WriteLine($"  average: we chained {used / games:0.0} times on their turn; {ourLeft / games:0.0} of our monsters left, {theirBoard / games:0.0} of theirs; we lost {lpLost / games:0} LP");
                continue;
            }
            // TURN2_VS=<deck>: that deck goes first as a late CPU, and this deck's first turn is measured instead (going
            // second: what it breaks and how much damage it deals).
            if (Environment.GetEnvironmentVariable("TURN2_VS") is string versus && versus.Length > 0)
            {
                var theirs = Deck.LoadYdk(deckFiles[versus]);
                theirs.Name = versus;
                double cleared = 0, damage = 0, wins = 0;
                for (int g = 0; g < games; g++)
                {
                    int gameSeed = seed * 1000 + g;
                    var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)gameSeed, FirstPlayer = 1 }, Deck0 = deck.Clone(), Deck1 = theirs.Clone() };
                    using var duel = recipe.Create();
                    var context = new DuelContext(duel, cards);
                    var answers = new ResponseLog();
                    bool verbose = Environment.GetEnvironmentVariable("TURN1_VERBOSE") != null;
                    var decisions = new List<string>();
                    SmartDuelist Cpu(Deck d, int seat, Action<string>? log) => new SmartDuelist(style.Name, gameSeed * 2 + seat, context, log, style, Playbooks.ForDeck(d),
                        new TurnPlanner(recipe, answers, style, () => Playbooks.ForDeck(d)));
                    var runner = new DuelRunner(duel, Cpu(deck, 0, verbose ? decisions.Add : null), Cpu(theirs, 1, null), answers);
                    runner.MessageReceived += context.Observe;
                    int theirsBefore = 0;
                    string before = "";
                    string Names(IEnumerable<FieldCard?> list) => string.Join(", ", list.Where(c => c != null).Select(c => c!.Code == 0 ? "(set)" : cards.Get(c!.Code)?.Name ?? c!.Code.ToString()));
                    while (runner.Result.Turns < 3 && runner.Step())
                        if (runner.Result.Turns == 2 && before.Length == 0)
                        {
                            var them = context.Board.Players[1];
                            theirsBefore = them.Monsters.Count(c => c != null) + them.SpellTraps.Take(6).Count(c => c != null);
                            before = $"{Names(them.Monsters)} | {Names(them.SpellTraps.Take(6))}";
                        }
                    var after = context.Board.Players[1];
                    int theirsAfter = after.Monsters.Count(c => c != null) + after.SpellTraps.Take(6).Count(c => c != null);
                    int lp = runner.Result.Winner == 0 ? 0 : after.LifePoints;
                    cleared += theirsBefore - theirsAfter;
                    damage += 8000 - lp;
                    if (runner.Result.Winner == 0) wins++;
                    Console.WriteLine($"  game {g + 1}: they built [{before}] ({theirsBefore} cards); after our turn they have {theirsAfter}, LP {lp}{(runner.Result.Winner == 0 ? " (won)" : "")}; our board [{Names(context.Board.Players[0].Monsters)}]");
                    if (verbose) foreach (var d in decisions) Console.WriteLine($"      {d}");
                }
                Console.WriteLine($"  average: removed {cleared / games:0.0} of their cards, dealt {damage / games:0} damage, won on the turn {wins}/{games}");
                continue;
            }
            for (int g = 0; g < games; g++)
            {
                int gameSeed = seed * 1000 + g;
                var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)gameSeed }, Deck0 = deck.Clone(), Deck1 = passer.Clone() };
                using var duel = recipe.Create();
                var context = new DuelContext(duel, cards);
                var answers = new ResponseLog();
                bool verbose = Environment.GetEnvironmentVariable("TURN1_VERBOSE") != null;
                var decisions = new List<string>();
                var planner = new TurnPlanner(recipe, answers, style, () => Playbooks.ForDeck(name));
                // TURN1_SEARCH=ms: the planner's deeper combo search, with that budget per decision.
                if (int.TryParse(Environment.GetEnvironmentVariable("TURN1_SEARCH"), out var budget)) { planner.ComboSearch = true; planner.SearchBudgetMs = budget; }
                var cpu = new SmartDuelist(style.Name, gameSeed, context, verbose ? decisions.Add : null, style, Playbooks.ForDeck(name), planner);
                var runner = new DuelRunner(duel, cpu, new PassiveDuelist(cards), answers);
                runner.MessageReceived += context.Observe;
                int chains = 0, summons = 0;
                runner.MessageReceived += m =>
                {
                    if (runner.Result.Turns != 1) return;
                    if (m[0] == Msg.Chaining) chains++;
                    if (m[0] is Msg.Summoning or Msg.SpSummoning) summons++;
                };
                // Turn 1 only: stop when turn 2 starts. (TURN1_VERBOSE: the opening hand and every decision.)
                string? opening = null;
                while (runner.Result.Turns < 2 && runner.Step())
                    if (verbose && opening == null && runner.IsWaiting == false && context.Board.Players[0].Hand.Count >= 5)
                        opening = string.Join(", ", context.Board.Players[0].Hand.Where(c => c != null).Select(c => cards.Get(c!.Code)?.Name ?? c!.Code.ToString()));
                if (verbose)
                {
                    Console.WriteLine($"  hand {g + 1} opened with: {opening}");
                    foreach (var d in decisions) Console.WriteLine($"      {d}");
                }
                var me = context.Board.Players[0];
                string Names(IEnumerable<FieldCard?> list) => string.Join(", ", list.Where(c => c != null).Select(c => c!.Code == 0 ? "(set)" : cards.Get(c!.Code)?.Name ?? c!.Code.ToString()));
                var mons = me.Monsters.Where(c => c != null).ToList();
                var st = me.SpellTraps.Take(6).Where(c => c != null).ToList();
                monsters += mons.Count;
                backrow += st.Count;
                actions += chains + summons;
                Console.WriteLine($"  hand {g + 1}: {mons.Count} monsters [{Names(mons)}] | {st.Count} S/T [{Names(st)}] | {me.Hand.Count} in hand | {summons} summons, {chains} activations");
            }
            Console.WriteLine($"  average: {monsters / games:0.0} monsters, {backrow / games:0.0} Spells/Traps, {actions / games:0.0} actions");
        }
        return 0;
    }
}
