using Microsoft.Data.Sqlite;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--ocg-imports`: writes campaign/ocg-imports.txt, the card shop's OCG import packs (<see cref="ImportShelf"/>): every
/// card the OCG printed and the TCG never did, that the game can play, in themed packs. A card is OCG-only when its
/// database entry says OCG and not TCG (the official database and the OCG pre-release sets; Rush, Skill and anime cards
/// aren't used) and it has no TCG printing on record. An archetype with at least <see cref="ThemeSize"/> such cards gets
/// its own pack; smaller archetypes share an Archetype Support pack, and cards of no archetype go into Monsters (Extra
/// Deck monsters too, unless there are enough for a pack of their own) and Spells and Traps packs. Rarities: Secret for cards 3 or more of the game's recipes play, Ultra for ones 1 or 2 play
/// and for Extra Deck monsters, Super for Level 7+ monsters, Rare for Level 5-6 monsters and Quick-Play Spells and Counter
/// Traps, Common for the rest.
/// </summary>
public static class OcgImports
{
    private const int ThemeSize = 8;

    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root)
    {
        var folder = Path.Combine(root, "vendor", "BabelCDB");
        var printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        var archetypes = Archetypes.Load(Path.Combine(root, "vendor", "CardScripts", "archetype_setcode_constants.lua"));

        // The region bits of a database entry's "ot": 1 OCG, 2 TCG (0x100 marks a pre-release).
        var ocg = new HashSet<uint>();
        foreach (var file in Directory.GetFiles(folder, "*.cdb"))
        {
            var name = Path.GetFileName(file);
            if (name != "cards.cdb" && !(name.StartsWith("prerelease-") && !name.Contains("rush"))) continue;
            using var connection = new SqliteConnection($"Data Source={file};Mode=ReadOnly");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM datas WHERE (ot & 3) = 1";
            using var reader = command.ExecuteReader();
            while (reader.Read()) ocg.Add((uint)reader.GetInt64(0));
        }

        // How many of the game's recipes play each card.
        var usage = new Dictionary<uint, int>();
        foreach (var path in Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories))
        {
            var deck = Deck.LoadYdk(path);
            foreach (var code in deck.Main.Concat(deck.Extra).Concat(deck.Side).Select(c => Banlist.BaseCode(c, cards)).Distinct())
                usage[code] = usage.GetValueOrDefault(code) + 1;
        }

        bool Playable(CardInfo info) =>
            scripts.Load($"c{info.Code}.lua") != null || ((info.Type & CardType.Normal) != 0 && (info.Type & CardType.Effect) == 0);
        var pool = ocg.Select(cards.Get).OfType<CardInfo>()
            .Where(i => i.Alias == 0 && (i.Type & CardType.Token) == 0 && printings.FirstYear(i.Code, 0) == 0 && Playable(i))
            .ToList();

        Rarity RarityOf(CardInfo i)
        {
            int used = usage.GetValueOrDefault(i.Code);
            if (used >= 3) return Rarity.SecretRare;
            if (used >= 1 || i.IsExtraDeckMonster) return Rarity.UltraRare;
            if ((i.Type & CardType.Monster) != 0 && i.Level >= 7) return Rarity.SuperRare;
            if ((i.Type & CardType.Monster) != 0 ? i.Level >= 5 : (i.Type & (CardType.QuickPlay | CardType.Counter)) != 0) return Rarity.Rare;
            return Rarity.Common;
        }

        // Each card's theme: its first archetype by name.
        string Theme(CardInfo i) => i.Setcodes.Where(s => s != 0).Select(s => archetypes.Name((ushort)(s & 0xfff))).FirstOrDefault(n => !n.StartsWith("0x")) ?? "";
        var byTheme = pool.GroupBy(Theme).Where(g => g.Key.Length > 0 && g.Count() >= ThemeSize).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
        var themed = byTheme.SelectMany(g => g).Select(i => i.Code).ToHashSet();
        var packs = byTheme.Select(g => (name: g.Key, cards: g.ToList())).ToList();
        // Smaller archetypes share one pack; cards of no archetype are sorted by kind.
        var rest = pool.Where(i => !themed.Contains(i.Code)).ToList();
        packs.Add(("Archetype Support", rest.Where(i => Theme(i).Length > 0).ToList()));
        rest = rest.Where(i => Theme(i).Length == 0).ToList();
        // (Extra Deck monsters get their own pack when there are enough of them.)
        bool extraPack = rest.Count(i => i.IsExtraDeckMonster) >= ThemeSize;
        if (extraPack) packs.Add(("Extra Deck", rest.Where(i => i.IsExtraDeckMonster).ToList()));
        packs.Add(("Monsters", rest.Where(i => (i.Type & CardType.Monster) != 0 && !(extraPack && i.IsExtraDeckMonster)).ToList()));
        packs.Add(("Spells and Traps", rest.Where(i => (i.Type & CardType.Monster) == 0).ToList()));

        var lines = new List<string>
        {
            "# Written by `--ocg-imports` (src/Ygo.Harness/OcgImports.cs): the card shop's OCG import packs.",
            "# Cards the OCG printed and the TCG never did, in themed packs (see OcgImports.cs for how rarities are given).",
            "# pack: <code> | <name>, then its cards as \"card: <id> <rarity>\".",
        };
        foreach (var (name, list) in packs.Where(p => p.cards.Count > 0))
        {
            var code = ImportShelf.Prefix + new string(name.ToUpperInvariant().Where(char.IsLetterOrDigit).Take(6).ToArray());
            lines.Add("");
            lines.Add($"pack: {code} | OCG Import: {name}");
            foreach (var i in list.OrderByDescending(RarityOf).ThenBy(i => i.Name))
                lines.Add($"card: {i.Code} {RarityOf(i)}  # {i.Name}");
        }
        var output = Path.Combine(root, "campaign", "ocg-imports.txt");
        File.WriteAllLines(output, lines);
        Console.WriteLine($"{pool.Count} OCG-only cards ({ocg.Count} marked OCG-only in the databases) in {packs.Count(p => p.cards.Count > 0)} packs:");
        foreach (var (name, list) in packs)
            Console.WriteLine($"  {name,-24} {list.Count,4} cards: {string.Join(", ", Enum.GetValues<Rarity>().Select(r => (r, n: list.Count(i => RarityOf(i) == r))).Where(x => x.n > 0).Select(x => $"{x.n} {x.r}"))}");
        Console.WriteLine($"Wrote {output}");
        foreach (var g in pool.GroupBy(Theme).Where(g => g.Key.Length > 0 && g.Count() >= 4 && g.Count() < ThemeSize).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  (under {ThemeSize}: {g.Key} {g.Count()})");
        return 0;
    }
}
