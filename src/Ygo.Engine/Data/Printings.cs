using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Ygo.Engine.Data
{
    /// <summary>
    /// TCG card rarities, ordered roughly from plain to rarest so the "rarest printing" is simply the highest value.
    /// Everything up to <see cref="SecretRare"/> counts as a classic rarity (see PrintingIndex.RarestUpTo).
    /// </summary>
    public enum Rarity : byte
    {
        Common,
        ShortPrint,
        Rare,
        NormalParallelRare,
        RareParallelRare,
        SuperRare,
        SuperParallelRare,
        StarfoilRare,
        MosaicRare,
        ShatterfoilRare,
        UltraRare,
        UltraParallelRare,
        PharaohsRare,
        GoldRare,
        PremiumGoldRare,
        PlatinumRare,
        SecretRare,
        PrismaticSecretRare,
        GoldSecretRare,
        ExtraSecretRare,
        PlatinumSecretRare,
        UltimateRare,
        CollectorsRare,
        GhostGoldRare,
        GhostRare,
        QuarterCenturySecretRare,
        StarlightRare,
    }

    /// <summary>Foil on the art window.</summary>
    public enum ArtFoil : byte { None, Holo, Secret, Prismatic, Ghost, Embossed, Fingerprint, Hieroglyph }

    /// <summary>
    /// Foil on the name's letters. Speckled: silver with multicolour specks (Prismatic Secret); Champagne: pale gold with
    /// fine specks (Quarter Century Secret).
    /// </summary>
    public enum NameFoil : byte { None, Silver, Gold, Secret, Embossed, Speckled, Champagne }

    /// <summary>Foil over the whole card (the text box keeps little or none of it, as on the real cards).</summary>
    public enum WholeCardFoil : byte { None, StarlightGrid, Platinum, Secret, Starfoil, Mosaic, Shatterfoil, Parallel, PlatinumSecret, QuarterCentury }

    /// <summary>Foil on the card's border (and the frame round the art). Gold also edges the text box; Embossed is Ultimate's raised frame.</summary>
    public enum BorderFoil : byte { None, Gold, Fingerprint, Embossed }

    /// <summary>
    /// How a rarity looks, as layers the card renderer combines. From the real cards: e.g. a Super Rare foils only the
    /// art; an Ultra Rare the art, name, attribute and level stars; a Secret Rare has a 45° diagonal foil on the art; a
    /// Starlight Rare a grid foil over the entire card.
    /// </summary>
    public readonly struct Finish
    {
        public readonly ArtFoil Art;
        public readonly NameFoil Name;
        public readonly WholeCardFoil Card;
        public readonly BorderFoil Border;
        /// <summary>Attribute icon and level / rank stars are foiled.</summary>
        public readonly bool Stars;
        /// <summary>Gold outline around the main figure in the art (Premium Gold Rare).</summary>
        public readonly bool Outline;

        public Finish(ArtFoil art = ArtFoil.None, NameFoil name = NameFoil.None, WholeCardFoil card = WholeCardFoil.None,
            BorderFoil border = BorderFoil.None, bool stars = false, bool outline = false)
        {
            Art = art;
            Name = name;
            Card = card;
            Border = border;
            Stars = stars;
            Outline = outline;
        }

        public bool IsPlain => Art == ArtFoil.None && Name == NameFoil.None && Card == WholeCardFoil.None && Border == BorderFoil.None && !Outline;
    }

    /// <summary>One printing of a card: where it was released and at what rarity.</summary>
    public readonly struct Printing
    {
        public readonly string SetCode;   // e.g. "BETB-EN007"
        public readonly string SetName;   // e.g. "Battles of Legend: Terminal Revenge"
        public readonly Rarity Rarity;

        public Printing(string setCode, string setName, Rarity rarity)
        {
            SetCode = setCode;
            SetName = setName;
            Rarity = rarity;
        }

        public override string ToString() => $"{SetCode} {Rarities.Name(Rarity)}";
    }

    public static class Rarities
    {
        public static Finish FinishOf(Rarity r) => r switch
        {
            Rarity.Common or Rarity.ShortPrint => new Finish(),
            Rarity.Rare => new Finish(name: NameFoil.Silver),
            // Super Rare: holo art, and (since 2017) a foiled Level / Rank and Attribute.
            Rarity.SuperRare => new Finish(ArtFoil.Holo, stars: true),
            Rarity.UltraRare => new Finish(ArtFoil.Holo, NameFoil.Gold, stars: true),
            // Pharaoh's Rare: an Ultra Rare with hieroglyphs in the art's holo foil and golden text.
            Rarity.PharaohsRare => new Finish(ArtFoil.Hieroglyph, NameFoil.Gold, stars: true),
            Rarity.SecretRare => new Finish(ArtFoil.Secret, NameFoil.Secret, stars: true),
            // Prismatic Secret Rare: a grid foil on the art only, and a multicolour speckled name.
            Rarity.PrismaticSecretRare => new Finish(ArtFoil.Prismatic, NameFoil.Speckled, stars: true),
            // Extra Secret Rare: the secret foil covers the entire card.
            Rarity.ExtraSecretRare => new Finish(name: NameFoil.Secret, card: WholeCardFoil.Secret, stars: true),
            // Platinum Rare: metallic over the whole card. Platinum Secret: silver text, secret foil over the whole card.
            Rarity.PlatinumRare => new Finish(name: NameFoil.Silver, card: WholeCardFoil.Platinum, stars: true),
            Rarity.PlatinumSecretRare => new Finish(name: NameFoil.Silver, card: WholeCardFoil.PlatinumSecret, stars: true),
            // Ultimate Rare: embossed texture on the art (highlighting its center), the art frame and border, name, and stars.
            Rarity.UltimateRare => new Finish(ArtFoil.Embossed, NameFoil.Embossed, border: BorderFoil.Embossed, stars: true),
            // Ghost Rare: 3D-looking art muted to silver.
            Rarity.GhostRare => new Finish(ArtFoil.Ghost, NameFoil.Silver, stars: true),
            Rarity.GhostGoldRare => new Finish(ArtFoil.Ghost, NameFoil.Gold, border: BorderFoil.Gold, stars: true),
            // Collector's Rare: a textured "fingerprint" foil through the art and around the borders.
            Rarity.CollectorsRare => new Finish(ArtFoil.Fingerprint, NameFoil.Secret, border: BorderFoil.Fingerprint, stars: true),
            // Gold Rares: gold borders; Gold Rare has a super-rare foil art, Gold Secret a secret one, both golden text.
            Rarity.GoldRare => new Finish(ArtFoil.Holo, NameFoil.Gold, border: BorderFoil.Gold, stars: true),
            Rarity.GoldSecretRare => new Finish(ArtFoil.Secret, NameFoil.Gold, border: BorderFoil.Gold, stars: true),
            Rarity.PremiumGoldRare => new Finish(name: NameFoil.Gold, border: BorderFoil.Gold, stars: true, outline: true),
            // Starlight: the Prismatic horizontal pattern over the whole face, text box excluded, and a rainbow name.
            // Quarter Century: Secret foil over the whole face, a champagne-gold speckled name, and a translucent "25"
            // watermark in the text box (our own plain numeral, not the anniversary logo).
            Rarity.StarlightRare => new Finish(name: NameFoil.Secret, card: WholeCardFoil.StarlightGrid, stars: true),
            Rarity.QuarterCenturySecretRare => new Finish(name: NameFoil.Champagne, card: WholeCardFoil.QuarterCentury, stars: true),
            // Battle Pack gimmicks: a pattern over the whole card.
            Rarity.StarfoilRare => new Finish(card: WholeCardFoil.Starfoil),
            Rarity.MosaicRare => new Finish(card: WholeCardFoil.Mosaic),
            Rarity.ShatterfoilRare => new Finish(card: WholeCardFoil.Shatterfoil),
            // Parallel / Duel Terminal: bands and dots in a glossy film over the card, on top of the base rarity's foil.
            Rarity.NormalParallelRare => new Finish(card: WholeCardFoil.Parallel),
            Rarity.RareParallelRare => new Finish(name: NameFoil.Silver, card: WholeCardFoil.Parallel),
            Rarity.SuperParallelRare => new Finish(ArtFoil.Holo, card: WholeCardFoil.Parallel),
            Rarity.UltraParallelRare => new Finish(ArtFoil.Holo, NameFoil.Gold, WholeCardFoil.Parallel, stars: true),
            _ => new Finish(),
        };

        public static string Name(Rarity r) => r switch
        {
            Rarity.ShortPrint => "Short Print",
            Rarity.NormalParallelRare => "Normal Parallel Rare",
            Rarity.RareParallelRare => "Rare Parallel Rare",
            Rarity.SuperRare => "Super Rare",
            Rarity.SuperParallelRare => "Super Parallel Rare",
            Rarity.StarfoilRare => "Starfoil Rare",
            Rarity.MosaicRare => "Mosaic Rare",
            Rarity.ShatterfoilRare => "Shatterfoil Rare",
            Rarity.UltraRare => "Ultra Rare",
            Rarity.UltraParallelRare => "Ultra Parallel Rare",
            Rarity.PharaohsRare => "Pharaoh's Rare",
            Rarity.GoldRare => "Gold Rare",
            Rarity.PremiumGoldRare => "Premium Gold Rare",
            Rarity.PlatinumRare => "Platinum Rare",
            Rarity.SecretRare => "Secret Rare",
            Rarity.PrismaticSecretRare => "Prismatic Secret Rare",
            Rarity.GoldSecretRare => "Gold Secret Rare",
            Rarity.ExtraSecretRare => "Extra Secret Rare",
            Rarity.PlatinumSecretRare => "Platinum Secret Rare",
            Rarity.UltimateRare => "Ultimate Rare",
            Rarity.CollectorsRare => "Collector's Rare",
            Rarity.GhostGoldRare => "Ghost/Gold Rare",
            Rarity.GhostRare => "Ghost Rare",
            Rarity.QuarterCenturySecretRare => "Quarter Century Secret Rare",
            Rarity.StarlightRare => "Starlight Rare",
            _ => r.ToString(),
        };

        /// <summary>
        /// Reads a rarity name as YGOPRODeck writes it ("Ultra Rare", "Duel Terminal Super Parallel Rare",
        /// "PLatinum Secret Rare", "Ultra Rare (Pharaoh's Rare)"...). Unknown or junk values ("New", "2", "Reprint")
        /// count as Common.
        /// </summary>
        public static Rarity Parse(string text)
        {
            var t = text.Trim().ToLowerInvariant();
            if (t.Contains("starlight") || t.Contains("10000")) return Rarity.StarlightRare;
            if (t.Contains("quarter century")) return Rarity.QuarterCenturySecretRare;
            if (t.Contains("ghost") && t.Contains("gold")) return Rarity.GhostGoldRare;
            if (t.Contains("ghost")) return Rarity.GhostRare;
            if (t.Contains("collector")) return Rarity.CollectorsRare;
            if (t.Contains("ultimate")) return Rarity.UltimateRare;
            if (t.Contains("pharaoh")) return Rarity.PharaohsRare;
            if (t.Contains("platinum secret")) return Rarity.PlatinumSecretRare;
            if (t.Contains("platinum")) return Rarity.PlatinumRare;
            if (t.Contains("gold secret")) return Rarity.GoldSecretRare;
            if (t.Contains("prismatic")) return Rarity.PrismaticSecretRare;
            if (t.Contains("extra secret")) return Rarity.ExtraSecretRare;
            if (t.Contains("secret")) return Rarity.SecretRare;
            if (t.Contains("premium gold")) return Rarity.PremiumGoldRare;
            if (t.Contains("gold")) return Rarity.GoldRare;
            if (t.Contains("parallel"))
            {
                if (t.Contains("ultra")) return Rarity.UltraParallelRare;
                if (t.Contains("super")) return Rarity.SuperParallelRare;
                if (t.Contains("rare parallel")) return Rarity.RareParallelRare;
                return Rarity.NormalParallelRare;
            }
            if (t.Contains("starfoil")) return Rarity.StarfoilRare;
            if (t.Contains("mosaic")) return Rarity.MosaicRare;
            if (t.Contains("shatterfoil")) return Rarity.ShatterfoilRare;
            if (t.Contains("ultra") || t.Contains("grand master")) return Rarity.UltraRare;
            if (t.Contains("short print")) return Rarity.ShortPrint;
            if (t.Contains("super")) return Rarity.SuperRare;
            if (t == "rare" || t.EndsWith(" rare")) return Rarity.Rare;
            return Rarity.Common;
        }
    }

    /// <summary>A real-world set: its code prefix ("BETB"), name, TCG release date ("2021-02-25"), and official card count.</summary>
    public sealed class SetInfo
    {
        public string Code = "";
        public string Name = "";
        public string Date = "";
        public int CardCount;
    }

    /// <summary>
    /// Every card's real-world printings (set code, set name, rarity), from YGOPRODeck's card-set data.
    /// Kept apart from the card pack so the game still works without it: cards then simply have no printings.
    /// </summary>
    public sealed class PrintingIndex
    {
        private const uint Magic = 0x4E525059; // "YPRN"
        /// <summary>2: rarity values renumbered; 3: set list; 4: set dates by name and reprint dates by code (S4, F3).</summary>
        public const int Version = 4;

        private readonly Dictionary<uint, Printing[]> _byCode = new Dictionary<uint, Printing[]>();
        private static readonly Printing[] None = Array.Empty<Printing>();

        public int Count => _byCode.Count;

        /// <summary>Set details by code prefix (the part of a printing's set code before the dash).</summary>
        public Dictionary<string, SetInfo> Sets { get; } = new Dictionary<string, SetInfo>();

        /// <summary>
        /// Every set name's TCG release date ("yyyy-mm-dd", the earliest when a name repeats), from cardsets.json: a printing is
        /// dated by its set's name, not its code prefix (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md F3, S4). A prefix spans sets
        /// years apart (JUMP: 2006-2018), so its earliest date would make a 2018 printing read as 2006.
        /// </summary>
        public Dictionary<string, string> NameDates { get; } = new Dictionary<string, string>();

        /// <summary>
        /// Printings the source files under an older set's name and code although they were printed later (data/printing-dates.txt):
        /// a set code ("DPYG-EN030"), or a code prefix ending in "*" ("MRD-EN*"), and the date it was really printed. Checked
        /// before <see cref="NameDates"/>, in file order.
        /// </summary>
        public List<(string code, string date)> ReprintDates { get; } = new List<(string, string)>();

        /// <summary>Whether a set code matches a <see cref="ReprintDates"/> entry (exact, or a prefix ending in "*").</summary>
        public static bool CodeMatches(string setCode, string pattern) =>
            pattern.EndsWith("*") ? setCode.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.Ordinal) : setCode == pattern;

        /// <summary>
        /// When a printing was printed ("yyyy-mm-dd"; "" when unknown): its <see cref="ReprintDates"/> entry, else its set
        /// name's date (<see cref="NameDates"/>), else its code prefix's (<see cref="Sets"/>). A copy's text follows from this
        /// date (<see cref="DeckBuilding.CardTexts.PrintedText"/>).
        /// </summary>
        public string DateOf(Printing p)
        {
            foreach (var (code, date) in ReprintDates)
                if (CodeMatches(p.SetCode, code)) return date;
            if (p.SetName != null && NameDates.TryGetValue(p.SetName, out var byName)) return byName;
            return Sets.TryGetValue(SetPrefix(p.SetCode), out var set) ? set.Date : "";
        }

        /// <summary>"BETB-EN007" -> "BETB".</summary>
        public static string SetPrefix(string setCode)
        {
            var dash = setCode.IndexOf('-');
            return dash > 0 ? setCode.Substring(0, dash) : setCode;
        }
        public IEnumerable<KeyValuePair<uint, Printing[]>> All => _byCode;

        /// <summary>A card's printings, rarest first. Alternate artworks fall back to their base card (pass its alias).</summary>
        public Printing[] Of(uint code, uint alias = 0)
        {
            if (_byCode.TryGetValue(code, out var list)) return list;
            if (alias != 0 && _byCode.TryGetValue(alias, out list)) return list;
            return None;
        }

        /// <summary>The rarest printing's rarity, or Common for cards with no printings (OCG-only, anime...).</summary>
        /// <summary>The year of a card's first TCG printing (by its sets' release dates), or 0 when unknown.</summary>
        public int FirstYear(uint code, uint alias = 0)
        {
            int year = 0;
            foreach (var p in Of(code, alias))
                if (Sets.TryGetValue(SetPrefix(p.SetCode), out var set) && set.Date.Length >= 4 && int.TryParse(set.Date.Substring(0, 4), out var y)
                    && (year == 0 || y < year))
                    year = y;
            return year;
        }

        /// <summary>The release date ("yyyy-mm-dd") of a card's first TCG printing, or "" when unknown.</summary>
        public string FirstDate(uint code, uint alias = 0)
        {
            string first = "";
            foreach (var p in Of(code, alias))
                if (Sets.TryGetValue(SetPrefix(p.SetCode), out var set) && set.Date.Length >= 7 && (first.Length == 0 || string.CompareOrdinal(set.Date, first) < 0))
                    first = set.Date;
            return first;
        }

        /// <summary>A card's least rare printing (Common when it has none).</summary>
        public Rarity Commonest(uint code, uint alias = 0)
        {
            var list = Of(code, alias);
            return list.Length == 0 ? Rarity.Common : list.Min(p => p.Rarity);
        }

        public Rarity Rarest(uint code, uint alias = 0)
        {
            var list = Of(code, alias);
            return list.Length > 0 ? list[0].Rarity : Rarity.Common;
        }

        /// <summary>
        /// The rarest printing no rarer than <paramref name="cap"/>. Cards printed only above it get their least rare printing.
        /// (Staples are reprinted at Starlight / Quarter Century rarity so often that "rarest" makes most decks all rainbow.)
        /// </summary>
        public Rarity RarestUpTo(uint code, uint alias, Rarity cap)
        {
            var list = Of(code, alias);
            if (list.Length == 0) return Rarity.Common;
            foreach (var p in list)
                if (p.Rarity <= cap) return p.Rarity;
            return list[list.Length - 1].Rarity;
        }

        public void Add(uint code, IEnumerable<Printing> printings)
        {
            var list = new List<Printing>(printings);
            if (_byCode.TryGetValue(code, out var existing)) list.AddRange(existing);
            // Rarest first; same rarity: newest-looking set code last is fine, keep it stable by code.
            list.Sort((a, b) => b.Rarity != a.Rarity ? b.Rarity.CompareTo(a.Rarity) : string.CompareOrdinal(a.SetCode, b.SetCode));
            // Drop exact duplicates (YGOPRODeck sometimes lists a printing twice).
            var unique = new List<Printing>(list.Count);
            foreach (var p in list)
                if (unique.Count == 0 || unique[unique.Count - 1].SetCode != p.SetCode || unique[unique.Count - 1].Rarity != p.Rarity)
                    unique.Add(p);
            _byCode[code] = unique.ToArray();
        }

        public void Write(Stream stream)
        {
            using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            // Set names repeat thousands of times, so they're stored once in a table.
            var names = new List<string>();
            var nameIds = new Dictionary<string, int>();
            foreach (var entry in _byCode.Values)
                foreach (var p in entry)
                    if (!nameIds.ContainsKey(p.SetName))
                    {
                        nameIds[p.SetName] = names.Count;
                        names.Add(p.SetName);
                    }

            w.Write(Magic);
            w.Write(Version);
            w.Write(names.Count);
            foreach (var n in names) w.Write(n);
            w.Write(_byCode.Count);
            foreach (var pair in _byCode)
            {
                w.Write(pair.Key);
                w.Write((ushort)pair.Value.Length);
                foreach (var p in pair.Value)
                {
                    w.Write(p.SetCode);
                    w.Write((ushort)nameIds[p.SetName]);
                    w.Write((byte)p.Rarity);
                }
            }
            w.Write(Sets.Count);
            foreach (var set in Sets.Values)
            {
                w.Write(set.Code);
                w.Write(set.Name);
                w.Write(set.Date);
                w.Write(set.CardCount);
            }
            w.Write(NameDates.Count);
            foreach (var pair in NameDates.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                w.Write(pair.Key);
                w.Write(pair.Value);
            }
            w.Write(ReprintDates.Count);
            foreach (var (code, date) in ReprintDates)
            {
                w.Write(code);
                w.Write(date);
            }
        }

        public static PrintingIndex Read(Stream stream)
        {
            var index = new PrintingIndex();
            using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (r.ReadUInt32() != Magic) throw new InvalidDataException("Not a printings file");
            if (r.ReadInt32() != Version) throw new InvalidDataException($"Unsupported printings version (this build reads {Version}: regenerate printings.bin with --export-printings)");
            var setNames = new string[r.ReadInt32()];
            for (int i = 0; i < setNames.Length; i++) setNames[i] = r.ReadString();
            var count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var code = r.ReadUInt32();
                var list = new Printing[r.ReadUInt16()];
                for (int p = 0; p < list.Length; p++)
                {
                    var setCode = r.ReadString();
                    var name = setNames[r.ReadUInt16()];
                    list[p] = new Printing(setCode, name, (Rarity)r.ReadByte());
                }
                index._byCode[code] = list; // already sorted when written
            }
            var sets = r.ReadInt32();
            for (int i = 0; i < sets; i++)
            {
                var set = new SetInfo { Code = r.ReadString(), Name = r.ReadString(), Date = r.ReadString(), CardCount = r.ReadInt32() };
                index.Sets[set.Code] = set;
            }
            var names = r.ReadInt32();
            for (int i = 0; i < names; i++) index.NameDates[r.ReadString()] = r.ReadString();
            var reprints = r.ReadInt32();
            for (int i = 0; i < reprints; i++) index.ReprintDates.Add((r.ReadString(), r.ReadString()));
            return index;
        }

        public static PrintingIndex Load(string path)
        {
            using var file = File.OpenRead(path);
            return Read(file);
        }
    }
}
