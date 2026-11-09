using Ygo.Engine;
using Ygo.Engine.Presentation;

namespace Ygo.Harness;

/// <summary>
/// `--glossary-test`: the card-term glossary against every card: how many cards each term is found in, how many terms a
/// card gets, the longest matches (a sign a pattern runs on too far), and a few cards marked up the way the card detail
/// shows them ([term] in brackets). Fails if a match starts inside a quoted card name or two matches overlap.
/// </summary>
public static class GlossaryTest
{
    public static int Run(ICardDataProvider cards)
    {
        var all = cards.All.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        var counts = CardGlossary.Terms.ToDictionary(t => t.Id, _ => 0);
        var longest = new List<(int length, string card, string piece)>();
        int bad = 0, total = 0, none = 0;
        foreach (var c in all)
        {
            var found = CardGlossary.Find(c.Text);
            total += found.Count;
            if (found.Count == 0) none++;
            for (int i = 0; i < found.Count; i++)
            {
                var f = found[i];
                counts[f.Term.Id]++;
                longest.Add((f.Length, c.Name, c.Text.Substring(f.Start, f.Length)));
                if (i > 0 && found[i - 1].Start + found[i - 1].Length > f.Start) { bad++; Console.WriteLine($"FAIL overlap in {c.Name}"); }
                if (System.Text.RegularExpressions.Regex.Matches(c.Text, "\"[^\"]*\"").Any(q => f.Start > q.Index && f.Start < q.Index + q.Length - 1))
                { bad++; Console.WriteLine($"FAIL inside a quoted name in {c.Name}: {c.Text.Substring(f.Start, f.Length)}"); }
            }
        }
        Console.WriteLine($"{all.Count} cards with text: {total} terms marked ({total / (double)all.Count:0.0} a card), {none} cards with none");
        foreach (var t in CardGlossary.Terms.OrderByDescending(t => counts[t.Id]))
            Console.WriteLine($"  {counts[t.Id],6}  {t.Title}");
        Console.WriteLine("Longest matches:");
        foreach (var (length, card, piece) in longest.OrderByDescending(l => l.length).Take(6))
            Console.WriteLine($"  {length,4}  {card}: {piece}");
        foreach (var name in new[] { "Ash Blossom & Joyous Spring", "Triple Tactics Thrust", "Snake-Eye Ash", "Mirror Force", "Solemn Judgment",
                     "Kozmo Dark Destroyer", "Crystron Halqifibrax", "Blue-Eyes White Dragon" })
        {
            var c = all.FirstOrDefault(x => x.Name == name);
            if (c == null) continue;
            Console.WriteLine($"\n{c.Name}:\n  {CardGlossary.Markup(c.Text, CardGlossary.Find(c.Text), (t, piece) => $"[{piece}]")}");
        }
        Console.WriteLine(bad == 0 ? "\nglossary test: all passed" : $"\nglossary test: {bad} failed");
        return bad == 0 ? 0 : 1;
    }
}
