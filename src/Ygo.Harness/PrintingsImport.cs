using System.Text.Json;
using Ygo.Engine.Data;

namespace Ygo.Harness;

/// <summary>
/// Converts YGOPRODeck's card info (https://db.ygoprodeck.com/api/v7/cardinfo.php, downloaded once into data/)
/// into the compact printings file the game loads. Every artwork id of a card gets the card's printings.
/// </summary>
public static class PrintingsImport
{
    public static int Run(string jsonPath, string outPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(jsonPath));
        var index = new PrintingIndex();
        var rarities = new Dictionary<Rarity, int>();
        int cards = 0;
        foreach (var card in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            if (!card.TryGetProperty("card_sets", out var sets)) continue;
            var printings = new List<Printing>();
            foreach (var set in sets.EnumerateArray())
            {
                var rarity = Rarities.Parse(set.GetProperty("set_rarity").GetString() ?? "");
                printings.Add(new Printing(set.GetProperty("set_code").GetString() ?? "",
                    set.GetProperty("set_name").GetString() ?? "", rarity));
                rarities[rarity] = rarities.GetValueOrDefault(rarity) + 1;
            }
            if (printings.Count == 0) continue;
            var ids = new HashSet<uint> { card.GetProperty("id").GetUInt32() };
            if (card.TryGetProperty("card_images", out var images))
                foreach (var image in images.EnumerateArray())
                    ids.Add(image.GetProperty("id").GetUInt32());
            foreach (var id in ids) index.Add(id, printings);
            cards++;
        }
        // Set details (release dates, sizes) from cardsets.php, saved next to cardinfo.json.
        var setsPath = Path.Combine(Path.GetDirectoryName(jsonPath)!, "cardsets.json");
        if (File.Exists(setsPath))
        {
            using var setsDoc = JsonDocument.Parse(File.ReadAllBytes(setsPath));
            foreach (var set in setsDoc.RootElement.EnumerateArray())
            {
                var code = set.TryGetProperty("set_code", out var c) ? c.GetString() ?? "" : "";
                if (code.Length == 0) continue;
                var info = new SetInfo
                {
                    Code = code,
                    Name = set.GetProperty("set_name").GetString() ?? "",
                    Date = set.TryGetProperty("tcg_date", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : "",
                    CardCount = set.TryGetProperty("num_of_cards", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0,
                };
                // Several sets can share a prefix (e.g. reprint editions); keep the earliest dated one's details.
                if (!index.Sets.TryGetValue(code, out var existing) || string.CompareOrdinal(info.Date, existing.Date) < 0 && info.Date.Length > 0)
                    index.Sets[code] = info;
                // A printing is dated by its set's name (F3): every name's date, the earliest when a name repeats.
                if (info.Name.Length > 0 && info.Date.Length >= 7 && (!index.NameDates.TryGetValue(info.Name, out var had) || string.CompareOrdinal(info.Date, had) < 0))
                    index.NameDates[info.Name] = info.Date;
            }
        }
        // Reprints the source files under an older set's name and code (campaign/printing-dates.txt; the json is in data/ygoprodeck/).
        var reprintsPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(jsonPath))))!, "campaign", ReprintsFile);
        if (File.Exists(reprintsPath)) foreach (var entry in ReadReprints(File.ReadAllText(reprintsPath))) index.ReprintDates.Add(entry);
        else Console.WriteLine($"  no {reprintsPath}: reprints filed under an older set's code date as that set");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using (var file = File.Create(outPath)) index.Write(file);
        Console.WriteLine($"{index.Sets.Count} sets with release dates; {index.NameDates.Count} set names dated; {index.ReprintDates.Count} reprint dates by code");
        Console.WriteLine($"{cards} cards with printings ({index.Count} ids incl. alternate art) -> {outPath} ({new FileInfo(outPath).Length / 1024} KB)");
        foreach (var pair in rarities.OrderBy(p => p.Key))
            Console.WriteLine($"  {Rarities.Name(pair.Key),-28} {pair.Value,6}  ");
        return 0;
    }

    /// <summary>campaign/printing-dates.txt: reprints dated apart from the set name they're filed under.</summary>
    public const string ReprintsFile = "printing-dates.txt";

    /// <summary>"&lt;set code or prefix*&gt; &lt;yyyy-mm-dd&gt;" lines ("#" starts a comment); a line that doesn't read throws.</summary>
    public static List<(string code, string date)> ReadReprints(string text)
    {
        var list = new List<(string, string)>();
        int lineNo = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            var hash = raw.IndexOf('#');
            var line = (hash >= 0 ? raw.Substring(0, hash) : raw).Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !System.Text.RegularExpressions.Regex.IsMatch(parts[1], @"^\d{4}-\d{2}-\d{2}$"))
                throw new InvalidDataException($"{ReprintsFile} line {lineNo}: \"{line}\" isn't \"<set code> <yyyy-mm-dd>\"");
            list.Add((parts[0], parts[1]));
        }
        return list;
    }
}
