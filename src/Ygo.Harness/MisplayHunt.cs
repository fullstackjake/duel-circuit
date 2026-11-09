using System.Collections.Concurrent;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;

namespace Ygo.Harness;

/// <summary>
/// `--hunt-audit [--games N] [--seed N] [--only a,b,...] --keep-replays dir`: the `--audit` blunder watchers over every
/// character with every deck it plays (not just its first: Ilse's four, Rue's Brawl deck), for the misplay hunt
/// (docs/dev/MISPLAY-HUNT-2026-11-06.md). Each game draws two random (character, deck) entries; `--only` limits one seat to
/// the named decks (the post-game decks), the other seat drawing from everyone. The kept replays feed `--analyze-replay`.
/// </summary>
public static class MisplayHunt
{
    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, int games, int seed, string? only)
    {
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        var everyone = Directory.GetFiles(Path.Combine(root, "personalities"), "*.profile").Select(Personality.Load).OrderBy(p => p.Id).ToList();
        var entries = everyone.SelectMany(p => p.Decks.Where(deckFiles.ContainsKey).Select(d => (style: p, deck: d))).ToList();
        var focus = only == null ? entries
            : entries.Where(e => only.Split(',').Select(n => n.Trim()).Contains(e.deck)).ToList();
        Console.WriteLine($"{entries.Count} (character, deck) entries from {everyone.Count} characters; {focus.Count} in the first seat's pool");
        Deck LoadDeck(string name) { var d = Deck.LoadYdk(deckFiles[name]); d.Name = name; return d; }
        var keeper = ReplayKeeper.Current;
        var findings = new ConcurrentBag<BlunderAudit.Finding>();
        int played = 0, errors = 0;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, g =>
        {
            var rng = new Random(seed * 1000 + g);
            var (a, deckA) = focus[rng.Next(focus.Count)];
            var (b, deckB) = entries[rng.Next(entries.Count)];
            // The focus seat goes first in half the games.
            if (g % 2 == 1) ((a, deckA), (b, deckB)) = ((b, deckB), (a, deckA));
            var styles = new[] { a, b };
            var decks = new[] { LoadDeck(deckA), LoadDeck(deckB) };
            var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)(seed * 1000 + g) }, Deck0 = decks[0], Deck1 = decks[1] };
            try
            {
                using var duel = recipe.Create();
                var context = new DuelContext(duel, cards);
                var answers = new ResponseLog();
                IDuelist Bot(int seat)
                {
                    var style = styles[seat];
                    var playbook = style.KnowsDeck ? Playbooks.ForDeck(decks[seat]) : null;
                    var planner = style.PlansAhead ? new TurnPlanner(recipe, answers, style, () => style.KnowsDeck ? Playbooks.ForDeck(decks[seat]) : null) : null;
                    return new SmartDuelist(style.Name, g * 2 + seat, context, null, style, playbook, planner);
                }
                var names = new[] { $"{a.Name} ({decks[0].Name}, skill {a.Skill})", $"{b.Name} ({decks[1].Name}, skill {b.Skill})" };
                var watcher = new BlunderAudit.Watcher(duel, cards, names, g);
                var runner = new DuelRunner(duel, new BlunderAudit.Spy(Bot(0), watcher.Decided), new BlunderAudit.Spy(Bot(1), watcher.Decided), answers);
                runner.MessageReceived += context.Observe;
                YataLock.Attach(runner, duel);
                runner.MessageReceived += watcher.Handle;
                var result = runner.Run(8000);
                Interlocked.Increment(ref played);
                foreach (var f in watcher.Findings) findings.Add(f);
                bool flagged = watcher.Findings.Any(f => ReplayAnalyzer.Weight(ReplayAnalyzer.Severity(f.Kind, f.Text)) >= 1);
                if (keeper != null && (keeper.Mode == "all" || (keeper.Mode == "stalls" ? result.Winner < 0 : flagged)))
                    keeper.Save("hunt", $"game{g}", recipe, answers, new[] { a.Name, b.Name },
                        new[] { SeatSpec.Of(0, a, decks[0], g * 2, a.PlansAhead), SeatSpec.Of(1, b, decks[1], g * 2 + 1, b.PlansAhead) },
                        result, fileName: $"s{seed}-game{g}-{deckA}-vs-{deckB}");
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref errors);
                Console.Error.WriteLine($"game {g}: {ex.Message}");
            }
        });
        var all = findings.ToList();
        Console.WriteLine($"{played} games ({errors} errors) in {timer.Elapsed:hh\\:mm\\:ss}: {all.Count} findings, {all.Select(f => f.Game).Distinct().Count()} games with at least one");
        foreach (var kind in all.GroupBy(f => f.Kind).OrderByDescending(k => k.Count()))
        {
            Console.WriteLine($"== {kind.Key}: {kind.Count()} ({kind.Select(f => f.Game).Distinct().Count()} games)");
            foreach (var card in kind.GroupBy(f => f.Card).OrderByDescending(c => c.Count()).Take(8))
                Console.WriteLine($"   {card.Count(),4}  {(card.Key.Length > 0 ? card.Key : "-")}   e.g. game {card.First().Game} turn {card.First().Turn}: {card.First().Who}: {card.First().Text}");
        }
        return 0;
    }
}
