using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Data;

namespace Ygo.Engine.Campaign
{
    /// <summary>A box opened: every pull in pack order (each pack least rare first), the guaranteed card, the DP refunded.</summary>
    public sealed class BoxResult
    {
        public List<(Pull pull, bool isNew, Added added)> Pulls = new List<(Pull, bool, Added)>();
        /// <summary>The box's promise (a pull at <see cref="ShopPrices.BoxTop"/>; the best pull when the set has nothing to guarantee).</summary>
        public Pull Topper;
        /// <summary>4th copies turned into DP, at most <see cref="ShopPrices.BoxRefundShare"/> of the box price.</summary>
        public int Refund;
        /// <summary>No pack held the guarantee, so one pack's best card was upgraded to it.</summary>
        public bool Upgraded;
        /// <summary>The box was paid for and opened (false: not enough DP, nothing happened).</summary>
        public bool Opened => Pulls.Count > 0;
    }

    /// <summary>
    /// Booster boxes (SHOP-REWORK-SPEC section 4): <see cref="ShopPrices.BoxPacks"/> packs of one set bought at once, at
    /// <see cref="ShopPrices.Box"/>, with at least one card at <see cref="ShopPrices.BoxTop"/>. No new odds: the packs open
    /// as packs do, and if none of their pulls is at the guaranteed rarity, one pack (picked by the box's rng among those
    /// whose best card is below it) has that card (the last foil slot) upgraded to a random card at it; no card rarer
    /// than the guarantee is ever replaced. Every pull takes its share of the box price, so the
    /// Sell desk's rule holds; 4th copies pay DP up to the refund cap for the whole box. The caller runs
    /// <see cref="CardUnlocks.CheckSets"/> once after, and saves.
    /// </summary>
    public static class BoosterBox
    {
        /// <summary>Spends <paramref name="price"/> at <see cref="Ledger.Boxes"/> and opens the box; an empty result if DP is short.</summary>
        public static BoxResult Open(Profile profile, PackSet set, int price, Random rng)
        {
            var result = new BoxResult();
            if (set.Pool.Count == 0 || !profile.Spend(Ledger.Boxes, price)) return result;
            var top = ShopPrices.BoxTop(set);
            var packs = Enumerable.Range(0, ShopPrices.BoxPacks).Select(_ => PackCatalog.Open(set, rng)).ToList();
            int topperPack = -1, topperSlot = -1;
            if (top is Rarity t)
            {
                for (int p = 0; p < packs.Count && topperPack < 0; p++)
                    for (int i = packs[p].Count - 1; i >= 0; i--)
                        if (packs[p][i].Rarity == t) { topperPack = p; topperSlot = i; break; }
                if (topperPack < 0 && set.Pool.TryGetValue(t, out var cards) && cards.Count > 0)
                {
                    // An upgrade never costs a rarer card: a pack whose best card is below the guarantee gives up that card
                    // (under Classic a pack can hold a Ghost or a Starlight above it); if every pack holds something rarer,
                    // one gives up its best card below the guarantee instead.
                    var below = Enumerable.Range(0, packs.Count).Where(p => packs[p].Count > 0 && packs[p][packs[p].Count - 1].Rarity < t).ToList();
                    if (below.Count == 0) below = Enumerable.Range(0, packs.Count).Where(p => packs[p].Any(x => x.Rarity < t)).ToList();
                    if (below.Count > 0)
                    {
                        topperPack = below[rng.Next(below.Count)];
                        var pack = packs[topperPack];
                        pack[pack.FindLastIndex(x => x.Rarity < t)] = new Pull(cards[rng.Next(cards.Count)], t);
                        packs[topperPack] = pack.OrderBy(x => x.Rarity).ToList();
                        topperSlot = packs[topperPack].FindLastIndex(x => x.Rarity == t);
                        result.Upgraded = true;
                    }
                }
            }
            var pulls = packs.SelectMany(x => x).ToList();
            result.Topper = topperPack >= 0 ? packs[topperPack][topperSlot] : pulls.OrderBy(x => x.Rarity).Last();
            // Each pull's share of the price (by what it's worth), over the whole box, as a pack shares its price over 9.
            var shares = Pricing.Shares(price, pulls.Select(x => Pricing.Basis(x.Rarity, profile.Collection.CommonestOf?.Invoke(x.Code) ?? x.Rarity)).ToList());
            int sold = 0;
            for (int i = 0; i < pulls.Count; i++)
            {
                bool isNew = !profile.Collection.Owns(pulls[i].Code);
                var (setCode, printed) = Profile.PackPrint(set, pulls[i], profile.Collection.Printings);
                var added = profile.Collection.AddPulled(pulls[i].Code, pulls[i].Rarity, CopySource.Pack, shares[i], setCode, printed);
                sold += added.Dp;
                result.Pulls.Add((pulls[i], isNew, added));
            }
            result.Refund = Math.Min(sold, (int)(price * ShopPrices.BoxRefundShare));
            profile.Earn(Ledger.PackRefund, result.Refund);
            profile.PacksOpened += ShopPrices.BoxPacks;
            return result;
        }
    }
}
