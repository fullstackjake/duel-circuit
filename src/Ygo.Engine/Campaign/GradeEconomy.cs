using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Data;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// One price for every screen (the Economy Designer's, POST-GAME-PLAN "One price for every screen"): a copy is worth
    /// its basis (its rarity against the card's commonest printing) times its grade's multiplier. A Raw Near Mint copy is
    /// worth exactly <see cref="RecipeShop.CopyPrice"/>, so shop prices and "Make it yours" quotes don't move; a Starlight
    /// Gem Mint slab is worth 15,000.
    /// </summary>
    public static class Pricing
    {
        /// <summary>Per mille of the basis: a slab at grades 1-10 (index = grade).</summary>
        private static readonly int[] SlabPm = { 0, 200, 300, 400, 500, 600, 800, 1000, 1200, 2000, 6000 };

        /// <summary>Per mille for a Raw copy, by its word (Raw 9 and 10 alike: you don't know which it is until it's graded).</summary>
        private static int RawPm(int grade) => grade >= 9 ? 1300 : grade >= 7 ? 1000 : grade >= 5 ? 700 : grade >= 3 ? 450 : 250;

        /// <summary>What a printing of a rarity is worth: the shop's prices up to Secret Rare, then the premium tiers.</summary>
        public static int Unit(Rarity r) => r <= Rarity.SecretRare ? RecipeShop.CopyPrice(r) : r switch
        {
            Rarity.QuarterCenturySecretRare => 900,
            Rarity.GhostRare or Rarity.GhostGoldRare => 1500,
            Rarity.StarlightRare => 2500,
            _ => 600, // Prismatic, Gold, Extra and Platinum Secret, Ultimate, Collector's
        };

        /// <summary>
        /// A copy's Raw Near Mint worth: the card's price at its commonest printing, or the copy's own rarity when that's
        /// worth more (a Secret Rare copy of a card printed as a Common is worth a Secret Rare).
        /// </summary>
        public static int Basis(Rarity rarity, Rarity commonest)
        {
            int floor = RecipeShop.CopyPrice(commonest);
            return rarity <= commonest ? floor : Math.Max(floor, Unit(rarity));
        }

        /// <summary>The one price: basis times the grade's multiplier (the slab's grade, or the Raw word's), to the nearest 5.</summary>
        public static int V(CardCopy copy, Rarity commonest) =>
            Round5(Basis(copy.Rarity, commonest) * (copy.IsSlab ? SlabPm[Math.Min(10, copy.Slab)] : RawPm(copy.Grade)) / 1000.0);

        public static int Round5(double x) => Math.Max(5, (int)Math.Round(x / 5, MidpointRounding.AwayFromZero) * 5);

        /// <summary>
        /// Splits what you paid across what you got, by basis (the best one takes the rounding), so each copy knows its
        /// share: a pack's price across its 9 pulls, a structure deck's across its cards.
        /// </summary>
        public static int[] Shares(int price, IReadOnlyList<int> bases)
        {
            var shares = new int[bases.Count];
            long total = bases.Sum(b => (long)Math.Max(1, b));
            if (bases.Count == 0 || total == 0) return shares;
            int given = 0, best = 0;
            for (int i = 0; i < bases.Count; i++)
            {
                shares[i] = (int)(price * (long)Math.Max(1, bases[i]) / total);
                given += shares[i];
                if (bases[i] >= bases[best]) best = i;
            }
            shares[best] += price - given;
            return shares;
        }
    }

    /// <summary>
    /// The Grading Desk (Gauge &amp; Gilt; Odile Varga, "the Loupe"): a copy goes in Raw (or cracked out of its slab) and
    /// comes back sealed with an exact grade: its true grade 75% of the time, one higher 10%, one lower 15%, never below
    /// a slab it was cracked out of. Each attempt's result is fixed by the copy's seed and how many times it's been
    /// graded. Standard takes 3 duels (the copy can't be played or sold meanwhile); express is twice the fee and back now.
    /// </summary>
    public static class GradingDesk
    {
        public const int Turnaround = 3;

        /// <summary>The fee by the copy's basis (never its grade, so the fee gives nothing away).</summary>
        public static int Fee(int basis) => basis < 300 ? 100 : basis < 900 ? 300 : basis < 2000 ? 1000 : 2500;

        public static int FeeFor(Collection collection, CardCopy copy, bool express) => Fee(collection.Basis(copy)) * (express ? 2 : 1);

        /// <summary>The odds shown: one lower, its true grade, one higher (per cent), and the floor from a cracked slab.</summary>
        public const int OddsLower = 15, OddsSame = 75, OddsHigher = 10;

        /// <summary>Why a copy can't go in now (null: it can). A standard submission may not leave a saved deck short.</summary>
        public static string? Blocked(Profile profile, CardCopy copy, bool express, Func<uint, int> neededByDecks)
        {
            if (copy.AtDesk) return "It's already at the desk.";
            int fee = FeeFor(profile.Collection, copy, express);
            if (profile.Dp < fee) return $"Grading it costs {fee:N0} DP; you have {profile.Dp:N0}.";
            if (!express && profile.Collection.Count(copy.Code) - 1 < neededByDecks(copy.Code))
                return "A saved deck needs it while it's away. Send it express, or free a copy first.";
            return null;
        }

        /// <summary>Sends a copy (a slab is cracked first); express grades it now. Returns the slab's grade now, or 0 if it's away (or refused).</summary>
        public static int Submit(Profile profile, CardCopy copy, bool express, Func<uint, int> neededByDecks)
        {
            if (Blocked(profile, copy, express, neededByDecks) != null) return 0;
            int fee = FeeFor(profile.Collection, copy, express);
            if (!profile.Spend(Ledger.Grading, fee)) return 0;
            copy.Paid += fee;
            if (copy.IsSlab) Crack(profile, copy);
            if (express) return Grade(profile, copy);
            copy.ReadyAt = profile.DuelsPlayed + Turnaround;
            profile.Collection.Resort(copy.Code);
            return 0;
        }

        private static int Grade(Profile profile, CardCopy copy)
        {
            uint roll = Hash.Mix(copy.Seed, 0x9E3779B9u, (uint)copy.Submissions) % 100;
            copy.Submissions++;
            int grade = Math.Max(1, Math.Min(10, copy.Grade + (roll < OddsHigher ? 1 : roll < OddsHigher + OddsLower ? -1 : 0)));
            copy.Slab = Math.Max(grade, copy.Floor);
            copy.ReadyAt = 0;
            profile.Collection.Resort(copy.Code);
            return copy.Slab;
        }

        public static bool IsReady(Profile profile, CardCopy copy) => copy.AtDesk && profile.DuelsPlayed >= copy.ReadyAt;

        /// <summary>Duels until a copy at the desk is back (0: ready).</summary>
        public static int DuelsLeft(Profile profile, CardCopy copy) => copy.AtDesk ? Math.Max(0, copy.ReadyAt - profile.DuelsPlayed) : 0;

        /// <summary>Collects a ready copy, sealed: its slab's grade (0 if it isn't ready).</summary>
        public static int Collect(Profile profile, CardCopy copy) => IsReady(profile, copy) ? Grade(profile, copy) : 0;

        /// <summary>Cracks a slab (free): the copy is Raw again, and a resubmission can never come back lower than this slab.</summary>
        public static void Crack(Profile profile, CardCopy copy)
        {
            if (!copy.IsSlab) return;
            copy.Floor = Math.Max(copy.Floor, copy.Slab);
            copy.Slab = 0;
            profile.Collection.Resort(copy.Code);
        }

        public static IEnumerable<CardCopy> AtDesk(Profile profile) => profile.Collection.All.Where(c => c.AtDesk);

        /// <summary>Copies waiting to be collected.</summary>
        public static int Ready(Profile profile) => profile.Collection.All.Count(c => IsReady(profile, c));
    }

    /// <summary>
    /// The Sell desk: a copy pays a quarter of its value, never more than half of what you paid for it (so buying,
    /// grading and selling always loses). Gifts (starters, the trader, rewards) were free and pay nothing, and so do cards
    /// won at ante (Moth's van sells at this desk's price, and says so). A copy a saved deck needs, or one at the Grading
    /// Desk, can't be sold.
    /// </summary>
    public static class SellDesk
    {
        /// <summary>The refusal for a card won at ante that nothing has been paid into (grading it adds the fee to Paid).</summary>
        public const string AnteRefusal = "Won at ante: Moth doesn't buy back his own.";

        public static string? Blocked(Profile profile, CardCopy copy, Func<uint, int> neededByDecks)
        {
            if (copy.AtDesk) return "It's at the Grading Desk.";
            if (copy.Paid <= 0) return copy.Source == CopySource.Ante ? AnteRefusal : "A gift: not for sale.";
            if (profile.Collection.Count(copy.Code) - 1 < neededByDecks(copy.Code)) return "A saved deck needs it.";
            return null;
        }

        /// <summary>Sells a copy; returns the DP paid (0 if it can't be sold).</summary>
        public static int Sell(Profile profile, CardCopy copy, Func<uint, int> neededByDecks)
        {
            if (Blocked(profile, copy, neededByDecks) != null) return 0;
            int price = profile.Collection.SellPrice(copy);
            if (!profile.Collection.Remove(copy)) return 0;
            profile.Earn(Ledger.Sell, price);
            return price;
        }
    }

    /// <summary>One copy on the used market.</summary>
    public sealed class Listing
    {
        public int Slot;
        public CardCopy Copy = null!;
        public int Price;
        /// <summary>A card on your wishlist.</summary>
        public bool Wanted;
        public bool Sold;
        /// <summary>The period it was listed in (Moth's van: <see cref="MothsVan.Buy"/> refuses a listing from before a restock).</summary>
        public int Epoch;
        /// <summary>Moth's shelf: one of the restored slots (a ready build's `stakes:` card, once the wall is open: <see cref="MothsVan.RestoredItems"/>).</summary>
        public bool Restored;
        /// <summary>Moth's shelf: the shelf as it would be drawn now for a save (set by <see cref="MothsVan.Shelf(Profile, ContrabandPacks, AlleyContext)"/>; <see cref="MothsVan.Buy"/> checks the listing is still on it).</summary>
        internal Func<Profile, List<Listing>>? Current;
    }

    /// <summary>
    /// The used market: 24 copies of real printings, 17 Raw (at the used odds: more of them worn) and 7 in slabs, priced
    /// at exactly their value, new every 5 duels (the listings come from the save's salt and the period, so reloading
    /// never rerolls them). Up to 4 are wishlist cards, always at a rarer printing or in a slab, so the market is never a
    /// cheap way to finish a recipe. Special orders: any printing but the commonest of a card the shop sells, Raw Near
    /// Mint, at its basis plus half.
    /// </summary>
    public static class UsedMarket
    {
        public const int Size = 24, RawSlots = 17, Every = 5, WishSlots = 4;
        /// <summary>Per mille of slab grades 10 down to 1.</summary>
        private static readonly int[] SlabOdds = { 50, 250, 300, 200, 100, 50, 30, 20, 0, 0 };

        public static int Epoch(Profile profile) => profile.DuelsPlayed / Every;

        public static int DuelsToRefresh(Profile profile) => Every - profile.DuelsPlayed % Every;

        /// <summary>This period's listings (sold ones marked). <paramref name="pool"/>: cards the market deals in (base codes).</summary>
        public static List<Listing> Listings(Profile profile, IReadOnlyList<uint> pool, int shopYear, PrintingIndex printings, ICardDataProvider cards)
        {
            int epoch = Epoch(profile);
            if (profile.MarketEpoch != epoch) { profile.MarketEpoch = epoch; profile.MarketSold.Clear(); }
            var rng = new Random(unchecked((int)Hash.Mix(profile.Collection.Salt, 0x55AE, (uint)epoch)));
            var inEra = pool.Where(c => RecipeShop.InEra(c, profile, shopYear, printings, cards) && printings.Of(c, cards.Get(c)?.Alias ?? 0).Length > 0)
                .Distinct().OrderBy(c => c).ToList();
            var wanted = profile.Wanted().Where(c => inEra.Contains(c)).OrderBy(c => c).ToList();
            var listings = new List<Listing>();
            if (inEra.Count == 0) return listings;
            for (int slot = 0; slot < Size; slot++)
            {
                bool slab = slot >= RawSlots;
                bool wish = slot < WishSlots && slot < wanted.Count;
                uint code = wish ? wanted[slot] : inEra[rng.Next(inEra.Count)];
                var alias = cards.Get(code)?.Alias ?? 0;
                var commonest = printings.Commonest(code, alias);
                var choices = printings.Of(code, alias).Select(p => p.Rarity).Distinct().ToList();
                // A wishlist card comes rarer than its commonest printing, or in a slab.
                if (wish && !slab && choices.Any(r => r != commonest)) choices.RemoveAll(r => r == commonest);
                if (wish && choices.All(r => r == commonest)) slab = true;
                double total = choices.Sum(r => 10000.0 / Pricing.Unit(r)), pick = rng.NextDouble() * total;
                var rarity = choices[choices.Count - 1];
                foreach (var r in choices) { pick -= 10000.0 / Pricing.Unit(r); if (pick <= 0) { rarity = r; break; } }
                uint seed = Hash.Mix(profile.Collection.Salt ^ 0x5EED, (uint)epoch, (uint)slot);
                int grade = slab ? SlabGrade(seed) : Grades.Roll(CopySource.Used, seed);
                var copy = new CardCopy { Code = code, Rarity = rarity, Grade = grade, Seed = seed, Source = CopySource.Used, Slab = slab ? grade : 0, Floor = slab ? grade : 0 };
                // Its printing (spec 4.2): one of its in-era printings at that rarity, by the listing's seed (no Random drawn).
                int era = profile.EraLimited ? shopYear : 9999;
                profile.Collection.Stamp(copy, CopyPrints.BySeed(code, rarity, era, seed, printings, cards) ?? CopyPrints.BySeed(code, rarity, 9999, seed, printings, cards), printings);
                listings.Add(new Listing { Slot = slot, Copy = copy, Price = Pricing.V(copy, commonest), Wanted = wish, Sold = profile.MarketSold.Contains(slot) });
            }
            return listings;
        }

        private static int SlabGrade(uint seed)
        {
            int roll = (int)(Hash.Mix(seed, 0xA11) % 1000), sum = 0;
            for (int i = 0; i < SlabOdds.Length; i++)
                if (roll < (sum += SlabOdds[i])) return 10 - i;
            return 8;
        }

        /// <summary>Why a listing can't be bought (null: it can).</summary>
        public static string? Blocked(Profile profile, Listing listing)
        {
            if (listing.Sold) return "Sold.";
            if (profile.Dp < listing.Price) return $"It costs {listing.Price:N0} DP; you have {profile.Dp:N0}.";
            if (!profile.Collection.WouldKeep(listing.Copy)) return "You have 3 copies worth more already.";
            return null;
        }

        /// <summary>Buys a listing: the copy joins your collection (a 4th copy replaces your least valuable Raw one). Returns what that sold for, or -1.</summary>
        public static int Buy(Profile profile, Listing listing)
        {
            if (Blocked(profile, listing) != null) return -1;
            if (!profile.Spend(Ledger.Used, listing.Price)) return -1;
            var bought = listing.Copy;
            var copy = new CardCopy
            {
                Code = bought.Code, Rarity = bought.Rarity, Grade = bought.Grade, Seed = bought.Seed, Source = CopySource.Used,
                Slab = bought.Slab, Floor = bought.Floor, Id = profile.Collection.NextId++, Paid = listing.Price,
                Text = bought.Text, Set = bought.Set, Printed = bought.Printed,
            };
            var added = profile.Collection.Take(copy);
            profile.Earn(Ledger.UsedRefund, added.Dp);
            profile.MarketSold.Add(listing.Slot);
            listing.Sold = true;
            return added.Dp;
        }

        /// <summary>A special order's price: the printing's basis plus half.</summary>
        public static int OrderPrice(uint code, Rarity rarity, PrintingIndex printings, ICardDataProvider cards) =>
            Pricing.Round5(Pricing.Basis(rarity, printings.Commonest(code, cards.Get(code)?.Alias ?? 0)) * 1.5);

        /// <summary>The printings a card can be ordered at: every one but its commonest (none for a card with one printing).</summary>
        public static List<Rarity> Orderable(uint code, PrintingIndex printings, ICardDataProvider cards)
        {
            var alias = cards.Get(code)?.Alias ?? 0;
            var commonest = printings.Commonest(code, alias);
            return printings.Of(code, alias).Select(p => p.Rarity).Where(r => r != commonest).Distinct().OrderBy(r => r).ToList();
        }

        /// <summary>Orders a printing: it arrives now, Raw Near Mint. Returns the copy, or null if it can't be ordered or afforded.</summary>
        public static CardCopy? Order(Profile profile, uint code, Rarity rarity, int shopYear, PrintingIndex printings, ICardDataProvider cards)
        {
            if (!Orderable(code, printings, cards).Contains(rarity) || !RecipeShop.InEra(code, profile, shopYear, printings, cards)) return null;
            int price = OrderPrice(code, rarity, printings, cards);
            var copy = profile.Collection.NewCopy(code, rarity, CopySource.Shop);
            copy.Paid = price;
            // Current text only (OrdersGiveOriginals is off, D5): the newest printing at that rarity with the current text, else unstamped.
            profile.Collection.Stamp(copy, CopyPrints.OrdersGiveOriginals ? CopyPrints.Newest(code, rarity, 9999, printings, cards)
                : CopyPrints.Current(code, rarity, profile.Collection.Texts, printings, cards), printings);
            if (profile.Dp < price || !profile.Collection.WouldKeep(copy)) return null;
            if (!profile.Spend(Ledger.Order, price)) return null;
            var added = profile.Collection.Take(copy);
            profile.Earn(Ledger.OrderRefund, added.Dp);
            return copy;
        }
    }
}
