using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--brawl-gen &lt;base&gt;[,&lt;base&gt;...] [--seed N]` (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 8): the out-of-towners' Brawl
/// decks, as the splash builder makes them: the base (decks/history) made legal for the Alley list with
/// <see cref="Loaners.Adjust"/>, plus 3 Main Deck cards from alley.txt's Basement Game list picked by <see cref="Alley.Splash"/>
/// (the Basement event with its splash at 3, the same seed every time). Prints each deck as recipe lines ("N name") with
/// what was added and cut, so it can be frozen into decks/recipes/no-limits.txt. Nothing is written.
/// </summary>
public static class BrawlGen
{
    public const int BrawlSplash = 3;

    public static int Run(ICardDataProvider cards, string root, string bases, int seed)
    {
        var lists = Banlist.LoadFolders(new[] { Path.Combine(root, "vendor", "LFLists"), Path.Combine(root, "lflists") }).ToList();
        var traditional = lists.First(l => l.Name.Contains("Traditional"));
        var alley = Alley.List(traditional, cards);
        var printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        var curated = Alley.LoadCurated(Path.Combine(root, "campaign", "alley.txt"), cards);
        var basement = Alley.Events.First(e => e.Name == "The Basement Game");
        var e = new AlleyEvent { Name = basement.Name, MinElo = basement.MinElo, MaxElo = basement.MaxElo, EraCap = basement.EraCap, Splash = BrawlSplash, MaxExtra = 0, Skill = basement.Skill };
        var pool = Alley.Contraband(lists, alley, e.EraCap, printings, cards);
        var curation = curated.TryGetValue(e.Name, out var c) ? c : null;
        string Name(uint code) => cards.Get(code)?.Name ?? code.ToString();
        foreach (var baseName in bases.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(b => b.Trim()))
        {
            var file = Path.Combine(root, "decks", "history", baseName + ".ydk");
            if (!File.Exists(file)) { Console.Error.WriteLine($"--brawl-gen: no decks/history/{baseName}.ydk"); return 1; }
            var baseDeck = Deck.LoadYdk(file);
            baseDeck.Name = baseName;
            var legal = Loaners.Adjust(baseDeck, alley, cards);
            var splash = Alley.SplashChecked(baseDeck, e, pool, alley, seed, cards, curation, out int sub);
            var problems = DeckRules.Validate(splash.Deck, alley, cards);
            Console.WriteLine($"\n== {baseName} (seed {seed}, sub-seed {sub}): {splash.Deck.Main.Count} + {splash.Deck.Extra.Count}, {problems.Count} problems under {alley.Name}");
            Console.WriteLine($"  Alley list: {string.Join("; ", legal.Changes)}");
            Console.WriteLine($"  added: {string.Join(", ", splash.Added.Select(Name))}");
            Console.WriteLine($"  cut: {string.Join(", ", splash.Cut.Select(Name))}");
            foreach (var p in problems) Console.WriteLine($"  problem: {p.Message}");
            Console.WriteLine("main:");
            foreach (var g in splash.Deck.Main.GroupBy(x => x)) Console.WriteLine($"{g.Count()} {Name(g.Key)}");
            Console.WriteLine("extra:");
            foreach (var g in splash.Deck.Extra.GroupBy(x => x)) Console.WriteLine($"{g.Count()} {Name(g.Key)}");
        }
        return 0;
    }
}
