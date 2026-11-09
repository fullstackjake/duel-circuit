using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;

namespace Ygo.Harness;

/// <summary>
/// `--deck-variants &lt;playbook deck&gt; &lt;a.ydk,b.ydk,...&gt; [--games N]`: versions of one deck's list, played the same way.
/// Every version plays N duels against every deck in decks/ in turn (the same opponents and seeds for each version, half
/// going first and half second), piloted by the quick bot with the named deck's playbook. Prints each version's win rate,
/// overall and by who went first, and its difference from the first version.
/// </summary>
public static class DeckVariants
{
    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, string playbookDeck, string variantList, int games, int seed)
    {
        var variants = variantList.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => (path: p, deck: Deck.LoadYdk(p))).ToList();
        foreach (var v in variants) v.deck.Name = playbookDeck; // so the playbook matches every version
        var field = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .Where(f => Path.GetFileNameWithoutExtension(f) != playbookDeck)
            .Select(f => { var d = Deck.LoadYdk(f); d.Name = Path.GetFileNameWithoutExtension(f); return d; })
            .Where(d => d.Main.Count >= 40)
            .OrderBy(d => d.Name).ToList();
        Console.WriteLine($"{variants.Count} versions of {playbookDeck}, {games} duels each against {field.Count} decks in turn");
        var baseline = 0.0;
        for (int v = 0; v < variants.Count; v++)
        {
            int[] wins = new int[2], played = new int[2];
            int errors = 0;
            Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, g =>
            {
                var theirs = field[g % field.Count];
                bool meFirst = g / field.Count % 2 == 0;
                int gameSeed = seed * 100000 + g;
                var mine = variants[v].deck;
                try
                {
                    var recipe = new DuelRecipe
                    {
                        Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)gameSeed, FirstPlayer = meFirst ? 0 : 1 },
                        Deck0 = mine.Clone(), Deck1 = theirs.Clone(),
                    };
                    using var duel = recipe.Create();
                    var context = new DuelContext(duel, cards);
                    var runner = new DuelRunner(duel,
                        new SmartDuelist("me", gameSeed * 2, context, playbook: Playbooks.ForDeck(mine)),
                        new SmartDuelist(theirs.Name, gameSeed * 2 + 1, context, playbook: Playbooks.ForDeck(theirs)));
                    runner.MessageReceived += context.Observe;
                    var result = runner.Run(6000);
                    int order = meFirst ? 0 : 1;
                    Interlocked.Increment(ref played[order]);
                    if (result.Winner == 0) Interlocked.Increment(ref wins[order]);
                }
                catch { Interlocked.Increment(ref errors); }
            });
            double rate = (wins[0] + wins[1]) / (double)Math.Max(1, played[0] + played[1]);
            if (v == 0) baseline = rate;
            Console.WriteLine($"  {Path.GetFileNameWithoutExtension(variants[v].path),-28} won {rate,6:P1}   first {wins[0] / (double)Math.Max(1, played[0]),6:P1} / second {wins[1] / (double)Math.Max(1, played[1]),6:P1}"
                              + (v > 0 ? $"   {(rate - baseline) * 100,+5:F1} points" : "   (baseline)") + (errors > 0 ? $"   {errors} errors" : ""));
        }
        return 0;
    }
}
