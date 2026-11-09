using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--loaner-check`: every recipe built as a loaner (<see cref="Loaners.Build"/>) under every circuit's ban list it can be
/// borrowed for, checked with <see cref="DeckRules.Validate"/>. Prints the loaners that still break their circuit's list.
/// </summary>
public static class LoanerCheck
{
    public static int Run(ICardDataProvider cards, string root)
    {
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities"));
        var circuits = Circuits.Load(StudyFiles.CircuitsPath(root), roster);
        var lists = Banlist.LoadFolders(new[] { Path.Combine(root, "vendor", "LFLists"), Path.Combine(root, "lflists") });
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        int checkedCount = 0, bad = 0;
        foreach (var recipe in RecipeBook.All(circuits).Where(r => deckFiles.ContainsKey(r.Deck)))
            for (int c = recipe.Circuit; c < circuits.All.Count; c++)
            {
                var name = circuits.All[c].Banlist;
                var list = name.Length > 0 ? lists.FirstOrDefault(l => l.Name == name) : null;
                if (name.Length > 0 && list == null) { Console.WriteLine($"{circuits.All[c].Name}: no list named {name}"); bad++; continue; }
                var deck = Deck.LoadYdk(deckFiles[recipe.Deck]);
                deck.Name = recipe.Deck;
                var loaner = Loaners.Build(deck, list ?? Banlist.None, cards);
                var problems = DeckRules.Validate(loaner, list ?? Banlist.None, cards);
                checkedCount++;
                if (problems.Count == 0) continue;
                bad++;
                Console.WriteLine($"{recipe.Deck} in {circuits.All[c].Name} ({name}): {string.Join("; ", problems.Take(3).Select(p => p.Message))}");
            }
        Console.WriteLine($"{checkedCount} loaners checked, {bad} with problems");
        return bad == 0 ? 0 : 1;
    }

    /// <summary>`--show-loaner deck circuit`: a recipe as the game lends it at the first circuit whose name has that text.</summary>
    public static int Show(ICardDataProvider cards, string root, string deckName, string circuitText)
    {
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities"));
        var circuits = Circuits.Load(StudyFiles.CircuitsPath(root), roster);
        var lists = Banlist.LoadFolders(new[] { Path.Combine(root, "vendor", "LFLists"), Path.Combine(root, "lflists") });
        var file = Directory.GetFiles(Path.Combine(root, "decks"), deckName + ".ydk", SearchOption.AllDirectories).First();
        var circuit = circuits.All.First(c => c.Name.Contains(circuitText, StringComparison.OrdinalIgnoreCase));
        var list = circuit.Banlist.Length > 0 ? lists.First(l => l.Name == circuit.Banlist) : Banlist.None;
        var recipe = Deck.LoadYdk(file);
        recipe.Name = deckName;
        var result = Loaners.Adjust(recipe, list, cards);
        Console.WriteLine($"{deckName} at {circuit.Name} ({(circuit.Banlist.Length > 0 ? circuit.Banlist : "no list")}):");
        foreach (var change in result.Changes) Console.WriteLine($"  {change}");
        void Section(string title, List<uint> codes)
        {
            Console.WriteLine($"{title} ({codes.Count}):");
            foreach (var g in codes.GroupBy(c => c)) Console.WriteLine($"  {g.Count()} {cards.Get(g.Key)?.Name ?? g.Key.ToString()}");
        }
        Section("Main", result.Deck.Main);
        Section("Extra", result.Deck.Extra);
        return 0;
    }
}
