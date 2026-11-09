using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Data;

namespace Ygo.Engine.Campaign
{
    /// <summary>One rarity's odds in a pack: the set's cards at it, the copies a pack gives on average, the chance of at least one.</summary>
    public sealed class RarityOdds
    {
        public Rarity Rarity;
        public int Cards;
        public double PerPack, AtLeastOne;
    }

    /// <summary>
    /// The odds a pack shows before you pay (SHOP-REWORK-SPEC section 5), computed exactly from the pack's layout
    /// (<see cref="PackCatalog.Open"/> reads the same one), not sampled. Every slot rolls on its own, so for a rarity
    /// with chance p_s in slot s a pack gives sum(p_s) copies on average and at least one with 1 - prod(1 - p_s).
    /// </summary>
    public static class PackOdds
    {
        /// <summary>Per rarity in the pool, least rare first (rarest last).</summary>
        public static List<RarityOdds> For(PackSet set)
        {
            var odds = new List<RarityOdds>();
            if (set.Pool.Count == 0) return odds;
            var layout = PackCatalog.LayoutOf(set);
            if (layout.Base.Count == 0) return odds;
            double foilTotal = PackCatalog.FoilNothing + layout.Foils.Sum(f => f.weight), nothing = PackCatalog.FoilNothing / foilTotal;
            double Share(List<Pull> tier, Rarity r) => tier.Count == 0 ? 0 : tier.Count(p => p.Rarity == r) / (double)tier.Count;
            foreach (var r in set.Pool.Keys.OrderBy(r => r))
            {
                double inBase = Share(layout.Base, r), inRare = layout.Rare != null ? Share(layout.Rare, r) : 0;
                double inFoil = layout.Foils.Where(f => f.rarity == r).Sum(f => f.weight) / foilTotal + nothing * Share(layout.Fallback, r);
                var slots = Enumerable.Repeat(inBase, 7).Concat(layout.Rare != null ? new[] { inRare } : Array.Empty<double>())
                    .Concat(Enumerable.Repeat(inFoil, layout.FoilSlots)).ToList();
                odds.Add(new RarityOdds
                {
                    Rarity = r,
                    Cards = set.Pool[r].Distinct().Count(),
                    PerPack = slots.Sum(),
                    AtLeastOne = 1 - slots.Aggregate(1.0, (miss, p) => miss * (1 - p)),
                });
            }
            return odds;
        }

        /// <summary>
        /// Whether a pull glows, bursts and plays the rare sounds on reveal: Super Rare or rarer, and in a rarity set above
        /// its base too (a Hidden Arsenal pack of Super Rares glows only on its Secret Rares).
        /// </summary>
        public static bool IsHit(PackSet set, Rarity rarity) =>
            rarity >= Rarity.SuperRare && (!PackCatalog.IsRaritySet(set) || rarity > PackCatalog.BaseRarity(set));

        /// <summary>
        /// "1 in 3.0 packs" (one decimal under 10, whole numbers from 10), or "about 1.3 a pack" when a pack gives one or
        /// more on average; "never" for a rarity no pack can give.
        /// </summary>
        public static string Text(RarityOdds odds)
        {
            if (odds.PerPack >= 1) return $"about {odds.PerPack.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} a pack";
            if (odds.AtLeastOne <= 0) return "never";
            double n = 1 / odds.AtLeastOne;
            return n < 10 ? $"1 in {n.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} packs" : $"1 in {Math.Round(n):0} packs";
        }

        /// <summary>The copies of each rarity a box of <paramref name="packs"/> packs gives on average (the guarantee aside).</summary>
        public static double PerBox(RarityOdds odds, int packs = ShopPrices.BoxPacks) => odds.PerPack * packs;
    }
}
