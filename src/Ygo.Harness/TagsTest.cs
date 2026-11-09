using Ygo.Engine;
using Ygo.Engine.Presentation;

namespace Ygo.Harness;

/// <summary>
/// `--tags-test`: Spell Speed and genre tags (the card detail's tag row) for well-known cards, checked against what
/// they should be, and how many cards get a Spell Speed and tags at all.
/// </summary>
public static class TagsTest
{
    public static int Run(ICardDataProvider cards)
    {
        int bad = 0;
        var byName = new Dictionary<string, CardInfo>();
        foreach (var c in cards.All) byName.TryAdd(c.Name, c);
        void Expect(string name, int? speed, params string[] tags)
        {
            if (!byName.TryGetValue(name, out var c)) { bad++; Console.WriteLine($"FAIL missing {name}"); return; }
            var s = CardTags.SpellSpeed(c)?.speed;
            var got = CardTags.GenresOf(c).Select(t => t.Label).ToList();
            bool ok = s == speed && tags.All(got.Contains);
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: speed {s?.ToString() ?? "-"} (want {speed?.ToString() ?? "-"}), tags {string.Join(", ", got)}{(ok ? "" : $" (want {string.Join(", ", tags)})")}");
        }
        Expect("Mekk-Knight Purple Nightfall", 2, "Banish", "Search", "Special Summon", "Columns");
        Expect("Ash Blossom & Joyous Spring", 2, "Negate");
        Expect("Mystical Space Typhoon", 2, "Destroy S/T");
        Expect("Pot of Greed", 1, "Draw");
        Expect("Solemn Judgment", 3, "Negate");
        Expect("Dimensional Prison", 2, "Banish");
        Expect("Blue-Eyes White Dragon", null);
        Expect("Infinite Impermanence", 2, "Negate");
        Expect("Aleister the Invoker", 2, "Search"); // its ATK boost works during either Main Phase
        Expect("Invoked Mechaba", 2, "Negate", "Banish");
        Expect("Polymerization", 1, "Fusion", "Special Summon");
        Expect("Change of Heart", 1, "Take control");
        Expect("Effect Veiler", 2, "Negate");
        Expect("Jinzo", null);
        // Columns: zones line up across the field (your zone 0 faces their zone 4; the Extra Monster Zones are columns 1 and 3).
        var board = new BoardState();
        foreach (var pb in board.Players)
        {
            pb.Monsters = Enumerable.Repeat<FieldCard?>(null, 7).ToList();
            pb.SpellTraps = Enumerable.Repeat<FieldCard?>(null, 8).ToList();
        }
        board.Players[0].Monsters[0] = new FieldCard { Code = 1 };   // column 0
        board.Players[1].SpellTraps[4] = new FieldCard { Code = 2 }; // their 4 = column 0
        board.Players[1].Monsters[5] = new FieldCard { Code = 3 };   // their left EMZ = column 3
        board.Players[0].SpellTraps[3] = new FieldCard { Code = 4 }; // column 3
        board.Players[0].SpellTraps[5] = new FieldCard { Code = 5 }; // Field Zone: no column
        var counts = Columns.CardsIn(board);
        bool columnsOk = counts.SequenceEqual(new[] { 2, 0, 0, 2, 0 }) && !Columns.MonsterZoneFree(board, 0, 0) && Columns.MonsterZoneFree(board, 1, 0);
        if (!columnsOk) bad++;
        Console.WriteLine($"{(columnsOk ? "ok  " : "FAIL")} column counts {string.Join(" ", counts)} (want 2 0 0 2 0)");
        void Column(string name, int needed, bool summons)
        {
            if (!byName.TryGetValue(name, out var c)) { bad++; Console.WriteLine($"FAIL missing {name}"); return; }
            bool ok = Columns.Mentions(c) && Columns.CardsNeeded(c) == needed && Columns.SummonsIntoColumn(c) == summons;
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: needs {Columns.CardsNeeded(c)} cards in a column (want {needed}), summons into it {Columns.SummonsIntoColumn(c)}");
        }
        Column("Mekk-Knight Purple Nightfall", 2, true);
        Column("Mekk-Knight Blue Sky", 2, true);
        Column("Infinite Impermanence", 0, false);
        Console.WriteLine($"{cards.All.Count(Columns.Mentions)} cards mention columns, {cards.All.Count(c => Columns.CardsNeeded(c) > 0)} need cards in a column, " +
            $"{cards.All.Count(Columns.SummonsIntoColumn)} Special Summon themselves into one");

        var all = cards.All.Where(c => (c.Type & CardType.Token) == 0).ToList();
        int speeds = all.Count(c => CardTags.SpellSpeed(c) != null), tagged = all.Count(c => CardTags.GenresOf(c).Count > 0);
        Console.WriteLine($"{all.Count} cards: {speeds} with a Spell Speed, {tagged} with genre tags; most tags on one card: {all.Max(c => CardTags.GenresOf(c).Count)}");
        Console.WriteLine(bad == 0 ? "tags test: all passed" : $"tags test: {bad} failed");
        return bad == 0 ? 0 : 1;
    }
}
