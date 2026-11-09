using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Ygo.Engine.Data;

namespace Ygo.Engine.Campaign
{
    /// <summary>A booster you can buy: a real set's cards, grouped by the rarity they were printed at.</summary>
    public sealed class PackSet
    {
        public SetInfo Info = new SetInfo();
        /// <summary>Card ids in the set by rarity (a card printed at two rarities appears in both).</summary>
        public Dictionary<Rarity, List<uint>> Pool = new Dictionary<Rarity, List<uint>>();
        public int Price;
        /// <summary>The ledger sink buying it counts at (<see cref="Ledger.Packs"/>; selection packs <see cref="Ledger.SelectionPacks"/>).</summary>
        public string Sink = Ledger.Packs;
        /// <summary>A house pack's shop year (<see cref="SelectionPacks.Build"/>): its pulls are shop copies by it (<see cref="Profile.PackPrint"/>).</summary>
        public int StampYear = 9999;

        public int CardCount => Pool.Values.SelectMany(c => c).Distinct().Count();
        public IEnumerable<Rarity> Rarities => Pool.Keys.OrderBy(r => r);
        public string Year => Info.Date.Length >= 4 ? Info.Date.Substring(0, 4) : "";
    }

    /// <summary>One card pulled from a pack.</summary>
    public readonly struct Pull
    {
        public readonly uint Code;
        public readonly Rarity Rarity;
        public Pull(uint code, Rarity rarity) { Code = code; Rarity = rarity; }
    }

    /// <summary>
    /// The card shop's stock: real booster sets (from the printings data) that the game has cards for, newest first,
    /// and how their packs open. A pack is 9 cards: 7 commons, 1 card from the set's rare tier (Rare or Super), and a
    /// foil slot that is usually Super / Rare but can be anything rarer the set has, at odds a little kinder than real
    /// boosters. Rarity sets (fewer than <see cref="CommonFloor"/> commons: Hidden Arsenal, Battles of Legend...) give 7
    /// cards of their base rarity and 2 foil slots (SHOP-REWORK-SPEC section 4).
    /// </summary>
    public sealed class PackCatalog
    {
        public const int PackSize = 9;
        public const int DefaultPrice = 150;
        /// <summary>A set with fewer distinct cards than this at Common or Short Print is a rarity set [placeholder].</summary>
        public const int CommonFloor = 5;
        /// <summary>The foil slot's weight for "nothing special" (see <see cref="FoilSlot"/>).</summary>
        public const double FoilNothing = 36;

        /// <summary>How often each rarity wins the foil slot when the set has it (relative weights).</summary>
        private static readonly Dictionary<Rarity, double> FoilWeights = new Dictionary<Rarity, double>
        {
            [Rarity.SuperRare] = 0, // the base of the slot; see FoilSlot()
            [Rarity.UltraRare] = 18,
            [Rarity.UltraParallelRare] = 6,
            [Rarity.SuperParallelRare] = 6,
            [Rarity.StarfoilRare] = 20,
            [Rarity.MosaicRare] = 20,
            [Rarity.ShatterfoilRare] = 20,
            [Rarity.PharaohsRare] = 6,
            [Rarity.GoldRare] = 18,
            [Rarity.PremiumGoldRare] = 8,
            [Rarity.PlatinumRare] = 6,
            [Rarity.SecretRare] = 9,
            [Rarity.PrismaticSecretRare] = 7,
            [Rarity.GoldSecretRare] = 6,
            [Rarity.ExtraSecretRare] = 2,
            [Rarity.PlatinumSecretRare] = 5,
            [Rarity.UltimateRare] = 5,
            [Rarity.CollectorsRare] = 3,
            [Rarity.GhostGoldRare] = 1.5,
            [Rarity.GhostRare] = 1.5,
            [Rarity.QuarterCenturySecretRare] = 3,
            [Rarity.StarlightRare] = 1,
        };

        /// <summary>
        /// Set names that aren't boosters (decks, starter sets, tins, promos, prizes, Speed Duel...), matched as whole words (a plural
        /// "s" allowed), so "Tin" never matches "Destiny".
        /// </summary>
        private static readonly string[] NotBoosters =
        {
            "Structure Deck", "Starter", "Kaiba Set", "Speed Duel", "Tin", "Promo", "Prize", "Tournament", "Championship",
            "Duelist League", "Sneak Peek", "Special Edition", "Deck:", "Collector's Box", "Box Set", "Gift", "Mega Pack",
            "OTS", "Participation", "Rush", "Advent Calendar", "Egyptian God Deck", "Power-Up", "Value Box", "Mat",
            "Sample", "Demo", "Deluxe Edition", "Retro Pack", "Shonen Jump", "Forbidden Memories", "Bundle", "Promotional",
            "Hobby League", "Premium Pack", "Movie Pack", "Duel Terminal", "Jump", "Magazine", "Video Game", "Exclusive",
        };
        private static readonly Regex NotBooster = new Regex(
            @"(?<![A-Za-z])(" + string.Join("|", NotBoosters.Select(Regex.Escape)) + @")s?(?![A-Za-z])", RegexOptions.IgnoreCase);

        public IReadOnlyList<PackSet> Sets { get; }

        /// <summary>
        /// Builds the stock (A0, SHOP-REWORK-SPEC "F1"): a set is a printing prefix ("ORCS"), named by the name most of its
        /// printings carry ("Order of Chaos", not the set table's "Order of Chaos Sneak Peek Participation Card"), and its
        /// pool is the printings under that name only (no Special Edition or Sneak Peek cards). It's a booster when the
        /// name isn't a deck, tin, promo... and the game has 80% of its card numbers (each number once, whatever its
        /// edition or region: LOB-001, LOB-E001 and LOB-EN001 are one card).
        /// </summary>
        public PackCatalog(PrintingIndex printings, ICardDataProvider cards)
        {
            var byPrefix = new Dictionary<string, List<(uint code, Printing printing)>>();
            foreach (var pair in printings.All)
                foreach (var p in pair.Value)
                {
                    var prefix = PrintingIndex.SetPrefix(p.SetCode);
                    if (!byPrefix.TryGetValue(prefix, out var list)) byPrefix[prefix] = list = new List<(uint, Printing)>();
                    list.Add((pair.Key, p));
                }
            var sets = new List<PackSet>();
            foreach (var pair in byPrefix)
            {
                if (!printings.Sets.TryGetValue(pair.Key, out var table) || table.Date.Length == 0) continue;
                var name = pair.Value.GroupBy(x => x.printing.SetName).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
                var mine = pair.Value.Where(x => x.printing.SetName == name).ToList();
                var pack = new PackSet { Info = new SetInfo { Code = table.Code, Name = name, Date = table.Date, CardCount = mine.Select(x => Number(x.printing.SetCode)).Distinct().Count() } };
                foreach (var (code, p) in mine)
                {
                    var info = cards.Get(code);
                    if (info == null || info.Alias != 0) continue; // only cards the game knows; no alternate artworks
                    if (!pack.Pool.TryGetValue(p.Rarity, out var list)) pack.Pool[p.Rarity] = list = new List<uint>();
                    if (!list.Contains(code)) list.Add(code);
                }
                if (IsBooster(pack)) sets.Add(pack);
            }
            Sets = sets.OrderByDescending(s => s.Info.Date).ThenBy(s => s.Info.Name).ToList();
            // Priced once the pool is known (a rarity set costs more; see ShopPrices).
            foreach (var s in Sets) s.Price = ShopPrices.Booster(s);
        }

        /// <summary>
        /// A printing's card number without its region ("LOB-001", "LOB-E001" and "LOB-EN001" -> "001"; a Legendary Decks
        /// box's "LDK2-ENK01" -> "K01").
        /// </summary>
        private static string Number(string setCode)
        {
            var dash = setCode.IndexOf('-');
            var suffix = dash >= 0 ? setCode.Substring(dash + 1) : setCode;
            if (suffix.StartsWith("EN", StringComparison.Ordinal)) return suffix.Substring(2);
            foreach (var region in new[] { "E", "PT" })
                if (suffix.Length > region.Length && suffix.StartsWith(region, StringComparison.Ordinal) && char.IsDigit(suffix[region.Length]))
                    return suffix.Substring(region.Length);
            return suffix;
        }

        private static bool IsBooster(PackSet set)
        {
            if (NotBooster.IsMatch(set.Info.Name)) return false;
            if (set.CardCount < 30) return false;
            // Enough of the set exists in the game to be worth opening (the rest are OCG / anime differences).
            if (set.Info.CardCount > 0 && set.CardCount < set.Info.CardCount * 0.8) return false;
            return set.Pool.Keys.Count >= 2;
        }

        public PackSet? Find(string code) => Sets.FirstOrDefault(s => s.Info.Code == code);

        /// <summary>
        /// A rarity set (SHOP-REWORK-SPEC "Rarity sets"): a real set (not a house pack or an OCG import) with fewer than
        /// <see cref="CommonFloor"/> distinct cards at Common or Short Print. The one predicate for its opening, its price,
        /// its badge and the shelves' order. House packs and imports keep the layout they always had (<see cref="LayoutOf"/>).
        /// </summary>
        public static bool IsRaritySet(PackSet set) =>
            !SelectionPacks.IsHousePack(set) && !ImportShelf.IsImport(set)
            && (set.Pool.TryGetValue(Rarity.Common, out var c) ? c : Enumerable.Empty<uint>())
               .Concat(set.Pool.TryGetValue(Rarity.ShortPrint, out var sp) ? sp : Enumerable.Empty<uint>()).Distinct().Count() < CommonFloor;

        /// <summary>A rarity set's base: its lowest rarity with at least <see cref="CommonFloor"/> cards (else its lowest). Common for an empty set.</summary>
        public static Rarity BaseRarity(PackSet set) =>
            set.Pool.Count == 0 ? Rarity.Common
                : set.Pool.Where(p => p.Value.Count >= CommonFloor).Select(p => p.Key).DefaultIfEmpty(set.Pool.Keys.Min()).Min();

        /// <summary>The rarest rarity in the set's pool (Starlight in a modern core set, Ultra in Dark Beginning 1). Common for an empty set.</summary>
        public static Rarity TopRarity(PackSet set) => set.Pool.Count == 0 ? Rarity.Common : set.Pool.Keys.Max();

        /// <summary>A rarity's foil-slot weight (0 for Super Rare, the slot's base; null for a rarity that never takes the slot).</summary>
        public static double? FoilWeight(Rarity rarity) => FoilWeights.TryGetValue(rarity, out var w) ? w : (double?)null;

        /// <summary>
        /// How a set's packs are laid out: <see cref="Base"/> fills 7 slots, <see cref="Rare"/> one more (sets with Commons;
        /// null in a rarity set), and <see cref="FoilSlots"/> foil slots roll <see cref="Foils"/> against
        /// <see cref="FoilNothing"/>, "nothing" giving a card of <see cref="Fallback"/>. Tiers are lists of pulls, one entry
        /// per chance (a Short Print once, every other card three times), so a pick is uniform over the list.
        /// <see cref="PackOdds"/> reads the same layout, so the odds shown are the odds opened.
        /// </summary>
        internal sealed class Layout
        {
            public List<Pull> Base = new List<Pull>();
            public List<Pull>? Rare;
            public int FoilSlots = 1;
            public List<(Rarity rarity, double weight)> Foils = new List<(Rarity, double)>();
            public List<Pull> Fallback = new List<Pull>();
        }

        internal static Layout LayoutOf(PackSet set)
        {
            if (!IsRaritySet(set))
            {
                var commons = Tier(set, Rarity.Common, Rarity.ShortPrint);
                if (commons.Count == 0 && set.Pool.Count > 0)
                {
                    // A house pack or an import with no commons, as such packs always opened: 7 cards of its lowest rarity
                    // and 2 foil slots over the rest, "nothing" giving a lowest-rarity card.
                    var lowest = set.Pool.Keys.Min();
                    var lowTier = Tier(set, lowest);
                    return new Layout
                    {
                        Base = lowTier, Rare = null, FoilSlots = 2, Fallback = lowTier,
                        Foils = set.Pool.Keys.Where(r => r != lowest && FoilWeights.TryGetValue(r, out var w) && w > 0).Select(r => (r, FoilWeights[r])).ToList(),
                    };
                }
                var rares = Tier(set, Rarity.Rare, Rarity.SuperRare, Rarity.RareParallelRare, Rarity.NormalParallelRare);
                var rare = rares.Count > 0 ? rares : commons;
                return new Layout
                {
                    Base = commons, Rare = rare, FoilSlots = 1, Fallback = rare,
                    Foils = set.Pool.Keys.Where(r => FoilWeights.TryGetValue(r, out var w) && w > 0).Select(r => (r, FoilWeights[r])).ToList(),
                };
            }
            // A rarity set (A1): 7 cards of its base rarity (cards at a stray rarity below it, or at one above it that never
            // takes the foil slot, drawn with it) and 2 foil slots over the rarities above the base. A foil roll of "nothing"
            // gives the lowest rarity above the base whose foil weight is 0 (Super Rare over a Rare base), so no rarity is
            // left unreachable; with none, a base card.
            var baseRarity = BaseRarity(set);
            var foils = set.Pool.Keys.Where(r => r > baseRarity && FoilWeights.TryGetValue(r, out var w) && w > 0).Select(r => (r, FoilWeights[r])).ToList();
            var fallback = set.Pool.Keys.Where(r => r > baseRarity && FoilWeights.TryGetValue(r, out var w) && w == 0).Select(r => (Rarity?)r).DefaultIfEmpty(null).Min();
            var baseTier = Tier(set, set.Pool.Keys.Where(r => r <= baseRarity || (r != fallback && foils.All(f => f.Item1 != r))).OrderBy(r => r).ToArray());
            return new Layout
            {
                Base = baseTier, Rare = null, FoilSlots = 2, Foils = foils,
                Fallback = fallback is Rarity f ? Tier(set, f) : baseTier,
            };
        }

        /// <summary>Opens one pack: least rare first, the best pull last (the way you'd flip through a real pack).</summary>
        public static List<Pull> Open(PackSet set, Random rng)
        {
            var layout = LayoutOf(set);
            var pulls = new List<Pull>();
            if (layout.Base.Count == 0) return pulls;
            for (int i = 0; i < 7; i++) pulls.Add(Pick(layout.Base, rng));
            if (layout.Rare != null) pulls.Add(Pick(layout.Rare, rng));
            for (int i = 0; i < layout.FoilSlots; i++) pulls.Add(FoilSlot(set, layout, rng) ?? Pick(layout.Fallback, rng));
            return pulls.OrderBy(p => p.Rarity).ToList();
        }

        /// <summary>
        /// The foil slot: a weighted pick among the layout's foil rarities (each kind competing with a base weight of
        /// <see cref="FoilNothing"/> for "nothing special"), so e.g. a set with Ultra + Secret gives Ultra 1 in 3.5 packs,
        /// Secret 1 in 6. Null for "nothing".
        /// </summary>
        private static Pull? FoilSlot(PackSet set, Layout layout, Random rng)
        {
            var roll = rng.NextDouble() * (FoilNothing + layout.Foils.Sum(o => o.weight));
            foreach (var (rarity, weight) in layout.Foils)
            {
                if (roll < weight)
                {
                    var cards = set.Pool[rarity];
                    return new Pull(cards[rng.Next(cards.Count)], rarity);
                }
                roll -= weight;
            }
            return null;
        }

        private static List<Pull> Tier(PackSet set, params Rarity[] rarities)
        {
            var list = new List<Pull>();
            foreach (var r in rarities)
                if (set.Pool.TryGetValue(r, out var cards))
                    // Short prints show up a third as often as the other commons.
                    foreach (var c in cards)
                        for (int n = r == Rarity.ShortPrint ? 1 : 3; n > 0; n--)
                            list.Add(new Pull(c, r));
            return list;
        }

        private static Pull Pick(List<Pull> tier, Random rng) => tier[rng.Next(tier.Count)];

        /// <summary>
        /// About how many of the <paramref name="wanted"/> cards (base codes) one pack of this set gives, estimated by
        /// opening <paramref name="samples"/> packs with a fixed seed. 0 when the set has none of them.
        /// </summary>
        public static double Expected(PackSet set, ICollection<uint> wanted, int samples = 200)
        {
            if (wanted.Count == 0 || !set.Pool.Values.Any(cards => cards.Any(wanted.Contains))) return 0;
            var rng = new Random(12345);
            int hits = 0;
            for (int i = 0; i < samples; i++)
                foreach (var pull in Open(set, rng))
                    if (wanted.Contains(pull.Code)) hits++;
            return (double)hits / samples;
        }
    }
}
