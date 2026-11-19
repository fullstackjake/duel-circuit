using System.Text.Json;
using Ygo.Engine.Data;

namespace Ygo.Harness;

/// <summary>
/// Converts YGOPRODeck's card info (https://db.ygoprodeck.com/api/v7/cardinfo.php, downloaded once into data/)
/// into the compact printings file the game loads. Every artwork id of a card gets the card's printings.
/// </summary>
public static class PrintingsImport
{
    public static int Run(string jsonPath, string outPath) => Run(jsonPath, outPath, out _);

    /// <summary>YGOPRODeck's placeholder rarity for a printing it hasn't confirmed yet.</summary>
    public const string PlaceholderRarity = "New";

    /// <summary>
    /// YGOPRODeck lists a "New" row next to a card's real printings in a set (e.g. MAMS-EN001 as Grand Master Rare,
    /// Ultra Rare, Starlight Rare and "New"). <see cref="Rarities.Parse"/> reads "New" as Common, which would give an
    /// all-foil set Commons (and the shop's price for a set with Commons). Such a row is dropped when the same card has
    /// a printing with a real rarity under the same set name; a "New" row that is the card's only one in its set stays
    /// (as Common, as before). Returns the rows kept, in their order.
    /// </summary>
    public static List<(string code, string name, string rarity)> DropPlaceholders(IReadOnlyList<(string code, string name, string rarity)> rows, out int dropped)
    {
        static bool IsPlaceholder(string rarity) => string.Equals(rarity.Trim(), PlaceholderRarity, StringComparison.OrdinalIgnoreCase);
        var realSets = new HashSet<string>(rows.Where(r => !IsPlaceholder(r.rarity)).Select(r => r.name), StringComparer.Ordinal);
        var kept = rows.Where(r => !(IsPlaceholder(r.rarity) && realSets.Contains(r.name))).ToList();
        dropped = rows.Count - kept.Count;
        return kept;
    }

    public static int Run(string jsonPath, string outPath, out int droppedPlaceholders)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(jsonPath));
        var index = new PrintingIndex();
        var rarities = new Dictionary<Rarity, int>();
        int cards = 0;
        droppedPlaceholders = 0;
        // How many printings of each set name use each code prefix: the set table can file a set under another code
        // than its cards carry (e.g. Dark Beginning 2 listed as "DB" while its cards are DB2-...).
        var prefixesByName = new Dictionary<string, Dictionary<string, int>>();
        foreach (var card in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            if (!card.TryGetProperty("card_sets", out var sets)) continue;
            var rows = sets.EnumerateArray().Select(set => (
                code: set.GetProperty("set_code").GetString() ?? "",
                name: set.GetProperty("set_name").GetString() ?? "",
                rarity: set.GetProperty("set_rarity").GetString() ?? "")).ToList();
            var keptRows = DropPlaceholders(rows, out var dropped);
            droppedPlaceholders += dropped;
            var printings = new List<Printing>();
            foreach (var (setCode, setName, rarityText) in keptRows)
            {
                var rarity = Rarities.Parse(rarityText);
                printings.Add(new Printing(setCode, setName, rarity));
                if (!prefixesByName.TryGetValue(setName, out var counts)) prefixesByName[setName] = counts = new Dictionary<string, int>();
                var prefix = PrintingIndex.SetPrefix(setCode);
                counts[prefix] = counts.GetValueOrDefault(prefix) + 1;
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
            var tableCodes = new HashSet<string>(setsDoc.RootElement.EnumerateArray()
                .Select(s => s.TryGetProperty("set_code", out var c) ? c.GetString() ?? "" : ""));
            var aliased = new List<string>();
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
                // The table files this set under another code than most of its printings carry, and no set in the table
                // has that code: file it under the printings' code too, so the set isn't lost.
                if (prefixesByName.TryGetValue(info.Name, out var counts) && counts.Count > 0)
                {
                    var main = counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).First().Key;
                    if (main != code && main.Length > 0 && !tableCodes.Contains(main) && !index.Sets.ContainsKey(main))
                    {
                        index.Sets[main] = new SetInfo { Code = main, Name = info.Name, Date = info.Date, CardCount = info.CardCount };
                        aliased.Add($"{info.Name} ({code} in the set table, {main} on its cards)");
                    }
                }
                // A printing is dated by its set's name (F3): every name's date, the earliest when a name repeats.
                if (info.Name.Length > 0 && info.Date.Length >= 7 && (!index.NameDates.TryGetValue(info.Name, out var had) || string.CompareOrdinal(info.Date, had) < 0))
                    index.NameDates[info.Name] = info.Date;
            }
            foreach (var line in aliased) Console.WriteLine($"  filed under its cards' code: {line}");
        }
        // Reprints the source files under an older set's name and code (campaign/printing-dates.txt; the json is in data/ygoprodeck/).
        var reprintsPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(jsonPath))))!, "campaign", ReprintsFile);
        if (File.Exists(reprintsPath)) foreach (var entry in ReadReprints(File.ReadAllText(reprintsPath))) index.ReprintDates.Add(entry);
        else Console.WriteLine($"  no {reprintsPath}: reprints filed under an older set's code date as that set");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using (var file = File.Create(outPath)) index.Write(file);
        Console.WriteLine($"{droppedPlaceholders} \"{PlaceholderRarity}\" placeholder printings dropped (the card has a real rarity in that set)");
        Console.WriteLine($"{index.Sets.Count} sets with release dates; {index.NameDates.Count} set names dated; {index.ReprintDates.Count} reprint dates by code");
        Console.WriteLine($"{cards} cards with printings ({index.Count} ids incl. alternate art) -> {outPath} ({new FileInfo(outPath).Length / 1024} KB)");
        foreach (var pair in rarities.OrderBy(p => p.Key))
            Console.WriteLine($"  {Rarities.Name(pair.Key),-28} {pair.Value,6}  ");
        return 0;
    }

    /// <summary>
    /// `--copies-test`'s importer step: a fixture cardinfo.json through <see cref="Run(string, string, out int)"/>
    /// and back through <see cref="PrintingIndex.Load"/>. A card printed "Ultra Rare" and "New" in one set keeps only
    /// the Ultra Rare row; a "New" row alone in its set, or beside a real printing in another set, stays a Common.
    /// Returns the number of failed checks.
    /// </summary>
    public static int SelfTest()
    {
        int failures = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        Console.WriteLine("\nPrintings import: YGOPRODeck's \"New\" placeholder rows");
        var folder = Path.Combine(Path.GetTempPath(), $"ygo-printings-test-{Guid.NewGuid():N}");
        var dataDir = Path.Combine(folder, "repo", "data", "ygoprodeck");
        Directory.CreateDirectory(dataDir);
        try
        {
            const string fixture = "{\"data\":[" +
                "{\"id\":9000001,\"name\":\"Fixture Foil Dragon\",\"card_sets\":[" +
                  "{\"set_name\":\"Fixture Foil Set\",\"set_code\":\"FXFS-EN001\",\"set_rarity\":\"Ultra Rare\"}," +
                  "{\"set_name\":\"Fixture Foil Set\",\"set_code\":\"FXFS-EN001\",\"set_rarity\":\"New\"}]}," +
                "{\"id\":9000002,\"name\":\"Fixture Fresh Card\",\"card_sets\":[" +
                  "{\"set_name\":\"Fixture Fresh Set\",\"set_code\":\"FXNW-EN001\",\"set_rarity\":\"New\"}]}," +
                "{\"id\":9000003,\"name\":\"Fixture Reprint\",\"card_sets\":[" +
                  "{\"set_name\":\"Fixture Old Set\",\"set_code\":\"FXOL-EN001\",\"set_rarity\":\"Super Rare\"}," +
                  "{\"set_name\":\"Fixture Fresh Set\",\"set_code\":\"FXNW-EN002\",\"set_rarity\":\"New\"}]}]}";
            var json = Path.Combine(dataDir, "cardinfo.json");
            File.WriteAllText(json, fixture);
            var bin = Path.Combine(folder, "printings.bin");
            Run(json, bin, out var dropped);
            var index = PrintingIndex.Load(bin);
            string Show(uint id) => string.Join(", ", index.Of(id).Select(p => $"{p.SetCode} {Rarities.Name(p.Rarity)}"));
            var foil = index.Of(9000001);
            Check($"a card printed Ultra Rare and \"New\" in one set keeps only the Ultra Rare row ({Show(9000001)})",
                foil.Length == 1 && foil[0].Rarity == Rarity.UltraRare && foil[0].SetCode == "FXFS-EN001");
            var fresh = index.Of(9000002);
            Check($"a \"New\" row alone in its set stays, as Common ({Show(9000002)})",
                fresh.Length == 1 && fresh[0].Rarity == Rarity.Common && fresh[0].SetCode == "FXNW-EN001");
            var reprint = index.Of(9000003);
            Check($"a \"New\" row beside a real printing in another set stays ({Show(9000003)})",
                reprint.Length == 2 && reprint.Any(p => p.SetCode == "FXOL-EN001" && p.Rarity == Rarity.SuperRare)
                && reprint.Any(p => p.SetCode == "FXNW-EN002" && p.Rarity == Rarity.Common));
            Check($"the import counts the rows it dropped ({dropped}, expected 1)", dropped == 1);
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
        return failures;
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
