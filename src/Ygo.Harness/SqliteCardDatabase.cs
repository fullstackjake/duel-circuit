using Microsoft.Data.Sqlite;
using Ygo.Engine;

namespace Ygo.Harness;

/// <summary>Reads EDOPro-format .cdb files (BabelCDB) into memory.</summary>
public sealed class SqliteCardDatabase : ICardDataProvider
{
    private readonly Dictionary<uint, CardInfo> _cards = new();

    public IEnumerable<CardInfo> All => _cards.Values;
    public int Count => _cards.Count;

    public CardInfo? Get(uint code) => _cards.TryGetValue(code, out var info) ? info : null;

    /// <summary>Loads the official database first, then release/pre-release files for newer cards.</summary>
    public static SqliteCardDatabase LoadFolder(string folder)
    {
        var db = new SqliteCardDatabase();
        var files = Directory.GetFiles(folder, "*.cdb")
            // Rush Duel, Skill, anime/unofficial, and GOAT duplicate entries aren't used by real decks.
            .Where(f => !new[] { "rush", "skill", "unofficial", "goat" }.Any(Path.GetFileName(f).Contains))
            .OrderBy(f => Path.GetFileName(f) == "cards.cdb" ? 0 : 1)
            .ThenBy(f => f);
        foreach (var file in files)
            db.LoadFile(file);
        // Original versions for retro decks: Goat-format entries ("Sangan (GOAT)") and the pre-errata printings
        // ("Chaos Emperor Dragon - Envoy of the End (Pre-Errata)"). Each points at its modern card through its alias, so
        // search hides it and ban lists count it as the same card.
        var goat = Path.Combine(folder, "goat-entries.cdb");
        if (File.Exists(goat)) db.LoadFile(goat);
        var unofficial = Path.Combine(folder, "cards-unofficial.cdb");
        if (File.Exists(unofficial)) db.LoadFile(unofficial, "t.name LIKE '%(Pre-Errata)'");
        return db;
    }

    public void LoadFile(string path, string? where = null)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT d.id, d.alias, d.setcode, d.type, d.atk, d.def, d.level, d.race, d.attribute, t.name, t.desc, " +
            "t.str1, t.str2, t.str3, t.str4, t.str5, t.str6, t.str7, t.str8, " +
            "t.str9, t.str10, t.str11, t.str12, t.str13, t.str14, t.str15, t.str16, d.category " +
            "FROM datas d JOIN texts t ON d.id = t.id" + (where != null ? " WHERE " + where : "");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var code = (uint)reader.GetInt64(0);
            if (_cards.ContainsKey(code)) continue;

            var type = (uint)reader.GetInt64(3);
            var level = (uint)reader.GetInt64(6);
            var defense = reader.GetInt32(5);
            var info = new CardInfo
            {
                Code = code,
                Alias = (uint)reader.GetInt64(1),
                Setcodes = UnpackSetcodes((ulong)reader.GetInt64(2)),
                Type = type,
                Attack = reader.GetInt32(4),
                Defense = defense,
                // level column packs: level in the low byte, pendulum scales in the high bytes.
                Level = level & 0xff,
                LScale = (level >> 24) & 0xff,
                RScale = (level >> 16) & 0xff,
                Race = (ulong)reader.GetInt64(7),
                Attribute = (uint)reader.GetInt64(8),
                Name = reader.IsDBNull(9) ? "" : reader.GetString(9),
                Text = reader.IsDBNull(10) ? "" : reader.GetString(10),
                Strings = Enumerable.Range(11, 16).Select(i => reader.IsDBNull(i) ? "" : reader.GetString(i)).ToArray(),
                Category = reader.IsDBNull(27) ? 0 : (ulong)reader.GetInt64(27),
            };
            // Link monsters store their arrows in the DEF column.
            if ((type & CardType.Link) != 0)
            {
                info.LinkMarker = (uint)defense;
                info.Defense = 0;
            }
            _cards[code] = info;
        }
    }

    private static ushort[] UnpackSetcodes(ulong packed)
    {
        var list = new List<ushort>();
        for (int i = 0; i < 4; i++)
        {
            var sc = (ushort)((packed >> (i * 16)) & 0xffff);
            if (sc != 0) list.Add(sc);
        }
        return list.ToArray();
    }
}
