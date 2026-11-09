using System;
using System.Linq;
using Ygo.Engine.Data;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// The card shop's price knobs, in one place so the harness can measure other prices (`--prices`,
    /// docs/dev/SHOP-PRICES-2026-11-01.md). The defaults are the prices the owner approved on 2026-11-01: house packs by
    /// band at half a recipe pack, Novice recipe packs at 450, boosters by year, rarity sets by their base rarity, boxes
    /// of 24 at 20% off with the Classic guarantee. <see cref="UseToday"/> puts back the prices from before. Set them
    /// before a <see cref="PackCatalog"/> is built (it prices its sets once).
    /// </summary>
    public static class ShopPrices
    {
        /// <summary>A Novice recipe pack (Beginner stays <see cref="RecipeShop.BasePackPrice"/>).</summary>
        public static int NoviceRecipePack = 450;

        /// <summary>House (selection) packs priced by their deck's band (<see cref="SelectionByBand"/>), else one flat price.</summary>
        public static bool SelectionBandPricing = true;
        public static int SelectionFlat = 200;
        /// <summary>Beginner, Novice, Intermediate, Advanced, Expert, Master (the Gauntlet and the Library too): half a recipe pack.</summary>
        public static int[] SelectionByBand = { 200, 200, 300, 500, 750, 1250 };

        /// <summary>Real boosters priced by the set's year (<see cref="BoosterByYear"/>), else one flat price.</summary>
        public static bool BoosterYearPricing = true;
        /// <summary>(last year of the bracket, price), oldest first; later years take the last price.</summary>
        public static (int upTo, int price)[] BoosterByYear = { (2008, 150), (2013, 200), (2018, 300), (2022, 450), (9999, 600) };
        /// <summary>A rarity set (Hidden Arsenal, Legendary Decks: every slot a foil or Rare) costs this many times as much.</summary>
        public static double NoCommonsFactor = 1.0;
        /// <summary>
        /// When set, a rarity set costs by its base rarity instead: the first entry whose rarity it reaches, highest first
        /// (Ultra or better 3.5x, Super 2.5x, Rare 1.5x), to the nearest 50.
        /// </summary>
        public static (Rarity atLeast, double factor)[]? NoCommonsByBase = { (Rarity.UltraRare, 3.5), (Rarity.SuperRare, 2.5), (Rarity.Common, 1.5) };

        /// <summary>The prices from before the shop rework (flat house packs and boosters, Novice at the Beginner price, rarity sets at 1x): for the harness's baseline.</summary>
        public static void UseToday()
        {
            NoviceRecipePack = RecipeShop.BasePackPrice;
            SelectionBandPricing = false;
            BoosterYearPricing = false;
            NoCommonsFactor = 1.0;
            NoCommonsByBase = null;
        }

        private static readonly string[] BandOrder = { "Beginner", "Novice", "Intermediate", "Advanced", "Expert", "Master" };

        /// <summary>A selection pack's price for a deck first met in this band (the Gauntlet and the Library at the Master price).</summary>
        public static int Selection(string? band)
        {
            if (!SelectionBandPricing || band == null) return SelectionFlat;
            int i = Array.IndexOf(BandOrder, band);
            if (band is "Gauntlet" or "Library") i = BandOrder.Length - 1;
            return i < 0 ? SelectionFlat : SelectionByBand[Math.Min(i, SelectionByBand.Length - 1)];
        }

        /// <summary>A real booster's price: by its year, and for a rarity set by its base rarity (<see cref="NoCommonsByBase"/>, else <see cref="NoCommonsFactor"/>).</summary>
        public static int Booster(PackSet set)
        {
            int price = PackCatalog.DefaultPrice;
            if (BoosterYearPricing && int.TryParse(set.Year, out var year))
                price = BoosterByYear.First(b => year <= b.upTo).price;
            if (PackCatalog.IsRaritySet(set) && set.Pool.Count > 0)
            {
                var lowest = BaseRarity(set);
                double factor = NoCommonsByBase?.FirstOrDefault(f => lowest >= f.atLeast).factor ?? NoCommonsFactor;
                price = (int)Math.Round(price * (factor > 0 ? factor : 1) / 50.0) * 50;
            }
            return price;
        }

        /// <summary>A rarity set: the same as <see cref="PackCatalog.IsRaritySet"/> (kept for the harness's probe).</summary>
        public static bool NoCommons(PackSet set) => PackCatalog.IsRaritySet(set);
        public const int CommonFloor = PackCatalog.CommonFloor;

        /// <summary>A rarity set's base: <see cref="PackCatalog.BaseRarity"/> (kept for the harness's probe).</summary>
        public static Rarity BaseRarity(PackSet set) => PackCatalog.BaseRarity(set);

        // ---- Booster boxes (SHOP-REWORK-SPEC section 4, BoosterBox). ----

        public enum BoxGuaranteeKind { Top, Classic }
        public const int BoxPacks = 24;
        /// <summary>A box costs <see cref="BoxPacks"/> packs less this share.</summary>
        public static double BoxDiscount = 0.20;
        /// <summary>
        /// Top: the set's rarest rarity; Classic: its rarest up to the Secret Rares (Secret, Prismatic, Gold, Extra and
        /// Platinum Secret: no Ultimate, Collector's, Ghost, Quarter Century or Starlight per box).
        /// </summary>
        public static BoxGuaranteeKind BoxGuarantee = BoxGuaranteeKind.Classic;
        /// <summary>The whole box's 4th-copy refund cap, as a share of its price (as for packs).</summary>
        public static double BoxRefundShare = 0.5;

        /// <summary>A box's price: round5(packs x the set's price x (1 - discount)).</summary>
        public static int Box(PackSet set) => Pricing.Round5(BoxPacks * set.Price * (1 - BoxDiscount));

        /// <summary>The rarity a box guarantees at least one of (null when the set has nothing above Super Rare).</summary>
        public static Rarity? BoxTop(PackSet set)
        {
            if (BoxGuarantee == BoxGuaranteeKind.Top) return set.Pool.Count > 0 && PackCatalog.TopRarity(set) > Rarity.SuperRare ? PackCatalog.TopRarity(set) : (Rarity?)null;
            var keys = set.Pool.Keys.Where(r => r > Rarity.SuperRare && r <= Rarity.PlatinumSecretRare).ToList();
            return keys.Count == 0 ? (Rarity?)null : keys.Max();
        }
    }
}
