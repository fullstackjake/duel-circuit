using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Data;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// Moth's van, Under the Counter (docs/dev/ALLEY-SPEC-2026-11-01.md section 10; ALLEY-ECONOMY Evidence 7): one price
    /// everywhere, so nothing Moth sells comes back at more than a sixth (the shelf) or a quarter (used) of its price.
    /// <list type="bullet">
    /// <item><b>The shelf:</b> <see cref="AlleyEconomy.ShelfSize"/> slabs, grade 9 or 10, of contraband printed at Secret
    /// Rare and up and worth at least <see cref="AlleyEconomy.ShelfMinValue"/>, at <see cref="AlleyEconomy.ShelfPrice"/>
    /// (1.5× value, rounded up to 500), no card twice; new every <see cref="AlleyEconomy.ShelfEvery"/> alley duels. Once the
    /// wall is open, <see cref="AlleyEconomy.ShelfRestoredSlots"/> of them are restored slots (<see cref="RestoredItems"/>).</item>
    /// <item><b>Used &amp; graded:</b> the used market's rules over the Contraband Pack's pool: Raw at the used odds and
    /// slabs up to <see cref="AlleyEconomy.VanUsedSlabMax"/>, priced at exactly their value, <see cref="AlleyEconomy.VanUsedSize"/>
    /// listings, new every <see cref="AlleyEconomy.VanUsedEvery"/> duels (any duel, as the used market counts). No wishlist
    /// slots and no special orders. Moth's rail (<see cref="Profile.Rail"/>, <see cref="Ante.BuyBack"/>) tops this tab, and the
    /// HELD row (<see cref="HeldShown"/>) ends it.</item>
    /// <item><b>Sell:</b> the Sell desk exactly (<see cref="SellDesk.Sell"/>, ledger "sell").</item>
    /// </list>
    /// Listings come from the save's salt and the period (their own salts), so reloading never rerolls them. A purchase is
    /// Paid its price; a copy it displaces under the 4th-copy rule pays its Sell desk price to refund.alley. No Contraband
    /// Packs are sold here: they're bracket prizes (<see cref="ContrabandPacks"/>).
    /// </summary>
    public static class MothsVan
    {
        /// <summary>The used tab's, the shelf's and the restored slots' salts.</summary>
        private const uint UsedSalt = 0x0A11, ShelfSalt = 0x5E1F, RestoredSalt = 0x7E57;

        /// <summary>Per mille of the used tab's slab grades 8 down to 3 (the used market's odds at 8 and under).</summary>
        private static readonly int[] SlabOdds = { 300, 200, 100, 50, 30, 20 };

        /// <summary>Ines's 2005 Scapegoat (docs/dev/ALLEY-STORY-2026-11-01.md section 3): Raw 6, Excellent, held at the end of the rail, never for sale.</summary>
        public const uint HeldCode = 73915051;
        public const int HeldGrade = 6;
        public const string HeldLabel = "HELD · not for sale";

        public static int UsedEpoch(Profile profile) => profile.DuelsPlayed / Math.Max(1, AlleyEconomy.VanUsedEvery);

        public static int ShelfEpoch(Profile profile) => profile.AlleyDuels / Math.Max(1, AlleyEconomy.ShelfEvery);

        /// <summary>Duels until a tab restocks: any duel for the used tab, alley duels for the shelf.</summary>
        public static int DuelsToRefresh(Profile profile, bool shelf) => shelf
            ? AlleyEconomy.ShelfEvery - profile.AlleyDuels % Math.Max(1, AlleyEconomy.ShelfEvery)
            : AlleyEconomy.VanUsedEvery - profile.DuelsPlayed % Math.Max(1, AlleyEconomy.VanUsedEvery);

        /// <summary>A weighted rarity among a card's printings (10000/Unit, as the used market picks), from a roll.</summary>
        private static Rarity PickRarity(IReadOnlyList<Rarity> rarities, uint roll)
        {
            double total = rarities.Sum(r => 10000.0 / Pricing.Unit(r)), pick = roll / 4294967296.0 * total;
            foreach (var r in rarities)
                if ((pick -= 10000.0 / Pricing.Unit(r)) < 0) return r;
            return rarities[rarities.Count - 1];
        }

        /// <summary>
        /// This period's used contraband (sold ones marked): <see cref="AlleyEconomy.VanUsedSize"/> listings over the pack's
        /// pool, the first ones Raw at the used odds, the rest slabs up to <see cref="AlleyEconomy.VanUsedSlabMax"/>, each at
        /// exactly its value.
        /// </summary>
        public static List<Listing> Used(Profile profile, ContrabandPacks packs, AlleyContext ctx)
        {
            int epoch = UsedEpoch(profile);
            if (profile.VanUsedEpoch != epoch) { profile.VanUsedEpoch = epoch; profile.VanUsedSold.Clear(); }
            var listings = new List<Listing>();
            var pool = packs.Pool;
            if (pool.Count == 0) return listings;
            // The used market's mix: 17 Raw in 24, the rest slabs.
            int raw = (int)Math.Round(AlleyEconomy.VanUsedSize * (double)UsedMarket.RawSlots / UsedMarket.Size);
            var slabGrades = Enumerable.Range(0, SlabOdds.Length).Select(i => 8 - i).Where(g => g <= AlleyEconomy.VanUsedSlabMax).ToList();
            for (int slot = 0; slot < AlleyEconomy.VanUsedSize; slot++)
            {
                uint Roll(uint k) => Hash.Mix(profile.Collection.Salt ^ UsedSalt, (uint)epoch, (uint)slot * 8 + k);
                uint code = pool[(int)(Roll(0) % (uint)pool.Count)];
                var alias = ctx.Cards.Get(code)?.Alias ?? 0;
                var rarity = PickRarity(ctx.Printings.Of(code, alias).Select(p => p.Rarity).Distinct().OrderBy(r => r).ToList(), Roll(1));
                uint seed = Roll(2);
                bool slab = slot >= raw && slabGrades.Count > 0;
                int grade = slab ? SlabGrade(slabGrades, Roll(3)) : Grades.Roll(CopySource.Used, seed);
                var copy = new CardCopy { Code = code, Rarity = rarity, Grade = grade, Seed = seed, Source = CopySource.Used, Slab = slab ? grade : 0, Floor = slab ? grade : 0 };
                // Its printing: one of the card's printings at that rarity (any date: the alley has no era), by the listing's seed.
                profile.Collection.Stamp(copy, CopyPrints.BySeed(code, rarity, 9999, seed, ctx.Printings, ctx.Cards), ctx.Printings);
                listings.Add(new Listing { Slot = slot, Copy = copy, Price = Pricing.V(copy, ctx.Commonest(code)), Sold = profile.VanUsedSold.Contains(slot), Epoch = epoch });
            }
            return listings;
        }

        private static int SlabGrade(List<int> grades, uint roll)
        {
            int total = grades.Sum(g => SlabOdds[8 - g]), pick = (int)(roll % (uint)total);
            foreach (var g in grades)
                if ((pick -= SlabOdds[8 - g]) < 0) return g;
            return grades[grades.Count - 1];
        }

        /// <summary>Every item the shelf can show: (card, printing, grade 9 or 10) worth at least the shelf's minimum, in a fixed order.</summary>
        public static List<(uint code, Rarity rarity, int grade, int value)> ShelfItems(ContrabandPacks packs, AlleyContext ctx)
        {
            var items = new List<(uint, Rarity, int, int)>();
            foreach (var code in packs.Pool)
            {
                var alias = ctx.Cards.Get(code)?.Alias ?? 0;
                var commonest = ctx.Printings.Commonest(code, alias);
                foreach (var r in ctx.Printings.Of(code, alias).Select(p => p.Rarity).Where(r => r >= AlleyEconomy.ShelfMinRarity).Distinct().OrderBy(r => r))
                    foreach (var g in new[] { 9, 10 })
                    {
                        int v = Pricing.V(new CardCopy { Code = code, Rarity = r, Grade = g, Slab = g }, commonest);
                        if (v >= AlleyEconomy.ShelfMinValue) items.Add((code, r, g, v));
                    }
            }
            return items;
        }

        /// <summary>
        /// Whether the shelf gives slots to restored cards (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 13; NO-LIMITS-ECONOMY section
        /// 4): once the wall is open (<see cref="BackAlley.WallEvent"/> in <see cref="Profile.Seen"/>). Until then the shelf is
        /// exactly as it was.
        /// </summary>
        public static bool RestoredOpen(Profile profile) => profile.Seen.Contains(BackAlley.WallEvent);

        /// <summary>
        /// Every item the restored slots can show: the ready builds' `stakes:` cards (base codes, in build then stakes order, each
        /// once), each printing at <see cref="AlleyEconomy.ShelfRestoredMinRarity"/> or rarer, slabs 9 and 10, worth at least
        /// <see cref="AlleyEconomy.ShelfRestoredMinValue"/>. Empty while no build is ready.
        /// </summary>
        public static List<(uint code, Rarity rarity, int grade, int value)> RestoredItems(AlleyData data, AlleyContext ctx)
        {
            var items = new List<(uint, Rarity, int, int)>();
            var codes = data.Builds.Where(b => b.Ready).SelectMany(b => b.Stakes).Distinct().ToList();
            foreach (var code in codes)
            {
                var alias = ctx.Cards.Get(code)?.Alias ?? 0;
                var commonest = ctx.Printings.Commonest(code, alias);
                foreach (var r in ctx.Printings.Of(code, alias).Select(p => p.Rarity).Where(r => r >= AlleyEconomy.ShelfRestoredMinRarity).Distinct().OrderBy(r => r))
                    foreach (var g in new[] { 9, 10 })
                    {
                        int v = Pricing.V(new CardCopy { Code = code, Rarity = r, Grade = g, Slab = g }, commonest);
                        if (v >= AlleyEconomy.ShelfRestoredMinValue) items.Add((code, r, g, v));
                    }
            }
            return items;
        }

        /// <summary>A shelf item's identity (card, printing, grade: "code.rarity.grade"): what its place in the order and a sold mark go by.</summary>
        public static string ShelfKey(uint code, Rarity rarity, int grade) => $"{code}.{(int)rarity}.{grade}";

        private static string ShelfKey(CardCopy copy) => ShelfKey(copy.Code, copy.Rarity, copy.Slab);

        /// <summary>
        /// This period's contraband shelf (sold ones marked): <see cref="AlleyEconomy.ShelfSize"/> slabs of different cards,
        /// drawn evenly from <see cref="ShelfItems"/>, each at <see cref="AlleyEconomy.ShelfPrice"/> of its value. Once the wall
        /// is open (<see cref="RestoredOpen"/>), <see cref="AlleyEconomy.ShelfRestoredSlots"/> of them are drawn from
        /// <see cref="RestoredItems"/> (<see cref="AlleyContext.Data"/>'s ready builds; <see cref="Listing.Restored"/>), last.
        /// </summary>
        public static List<Listing> Shelf(Profile profile, ContrabandPacks packs, AlleyContext ctx)
        {
            // The items, once for this call and the listings' re-draw at Buy (the wall and the period are read from the save each time).
            var items = ShelfItems(packs, ctx);
            var data = ctx.Data;
            List<(uint code, Rarity rarity, int grade, int value)>? restored = null;
            List<Listing> Draw(Profile p) => ShelfOver(p, items, ctx, RestoredOpen(p) && data != null ? restored ??= RestoredItems(data, ctx) : null, Draw);
            return Draw(profile);
        }

        /// <summary>
        /// The shelf over these items. Each item's place in the period's order hangs on what it is (card, printing, grade),
        /// not where it sits in the list, and a sold mark is the item's own (<see cref="Profile.VanShelfSoldCards"/>): when
        /// the pool changes mid-period (an exclusion, a list's history), the cards still possible keep their order, and one
        /// you bought stays sold. A save from before marked sold slots: each is matched to the slab it bought (its copy's
        /// seed) and marked by that card; a slot whose copy has left the collection keeps marking its slot.
        /// <para>
        /// <paramref name="restored"/> (the restored slots' items; null or empty: none): the regular draw takes
        /// <see cref="AlleyEconomy.ShelfSize"/> − <see cref="AlleyEconomy.ShelfRestoredSlots"/> slots as before (the same first
        /// cards of the same order), then up to <see cref="AlleyEconomy.ShelfRestoredSlots"/> restored slabs follow, in their own
        /// order (a salt of their own) and of cards not already on the shelf, and the regular draw fills any slot they leave.
        /// The shelf's rules otherwise: <see cref="AlleyEconomy.ShelfPrice"/>, no card twice, the period, the sold marks.
        /// </para>
        /// </summary>
        public static List<Listing> Shelf(Profile profile, IReadOnlyList<(uint code, Rarity rarity, int grade, int value)> items, AlleyContext? ctx = null,
            IReadOnlyList<(uint code, Rarity rarity, int grade, int value)>? restored = null) =>
            ShelfOver(profile, items, ctx, restored, p => Shelf(p, items, ctx, restored));

        /// <summary>
        /// The shelf over these items (see above); each listing keeps <paramref name="current"/>, the shelf as it would be drawn
        /// now, so <see cref="Buy"/> sells it only while it's still there (the wall opening, or a build turning ready, mid-period
        /// moves slots 4-5 to restored slabs).
        /// </summary>
        private static List<Listing> ShelfOver(Profile profile, IReadOnlyList<(uint code, Rarity rarity, int grade, int value)> items, AlleyContext? ctx,
            IReadOnlyList<(uint code, Rarity rarity, int grade, int value)>? restored, Func<Profile, List<Listing>> current)
        {
            int epoch = ShelfEpoch(profile);
            if (profile.VanShelfEpoch != epoch) { profile.VanShelfEpoch = epoch; profile.VanShelfSold.Clear(); profile.VanShelfSoldCards.Clear(); }
            MarkLegacySold(profile, epoch);
            uint salt = profile.Collection.Salt ^ ShelfSalt;
            List<((uint code, Rarity rarity, int grade, int value) x, string key)> Order(IReadOnlyList<(uint code, Rarity rarity, int grade, int value)> from, uint orderSalt) =>
                from.Select(x => (x, key: ShelfKey(x.code, x.rarity, x.grade)))
                    .Select(x => (x.x, x.key, k: Hash.Mix(orderSalt, (uint)epoch, CardUnlocks.StableHash(x.key))))
                    .OrderBy(x => x.k).ThenBy(x => x.key, StringComparer.Ordinal).Select(x => (x.x, x.key)).ToList();
            var listings = new List<Listing>();
            void Fill(List<((uint code, Rarity rarity, int grade, int value) x, string key)> order, int upTo, bool isRestored)
            {
                foreach (var ((code, rarity, grade, value), key) in order)
                {
                    if (listings.Count >= upTo) break;
                    if (listings.Any(l => l.Copy.Code == code)) continue;
                    int slot = listings.Count;
                    uint seed = Hash.Mix(salt ^ 0x5EED, (uint)epoch, CardUnlocks.StableHash(key));
                    var copy = new CardCopy { Code = code, Rarity = rarity, Grade = grade, Seed = seed, Source = CopySource.Used, Slab = grade, Floor = grade };
                    // Its printing, by the slab's seed (ShelfKey, and so the order and sold marks, doesn't include it).
                    if (ctx != null) profile.Collection.Stamp(copy, CopyPrints.BySeed(code, rarity, 9999, seed, ctx.Printings, ctx.Cards), ctx.Printings);
                    // An older save's slot marks were written for regular slots: they never mark a restored slab.
                    bool sold = profile.VanShelfSoldCards.Contains(key) || (!isRestored && profile.VanShelfSold.Contains(slot));
                    listings.Add(new Listing { Slot = slot, Copy = copy, Price = AlleyEconomy.ShelfPrice(value), Sold = sold, Epoch = epoch, Restored = isRestored, Current = current });
                }
            }
            var regular = Order(items, salt);
            int restoredSlots = restored == null || restored.Count == 0 ? 0 : Math.Max(0, Math.Min(AlleyEconomy.ShelfRestoredSlots, AlleyEconomy.ShelfSize));
            Fill(regular, AlleyEconomy.ShelfSize - restoredSlots, false);
            if (restoredSlots > 0) Fill(Order(restored!, salt ^ RestoredSalt), listings.Count + restoredSlots, true);
            // Restored items too few (or all their cards already on the shelf): the regular draw fills the rest, as before.
            Fill(regular, AlleyEconomy.ShelfSize, false);
            return listings;
        }

        /// <summary>
        /// An older save's sold slots this period (the shelf then was ordered by the items' places, and a bought slab's seed
        /// was its slot's): each slot whose copy is still in the collection is marked by that copy's card instead.
        /// </summary>
        private static void MarkLegacySold(Profile profile, int epoch)
        {
            if (profile.VanShelfSold.Count == 0) return;
            foreach (var slot in profile.VanShelfSold.ToList())
            {
                uint legacySeed = Hash.Mix(profile.Collection.Salt ^ ShelfSalt ^ 0x5EED, (uint)epoch, (uint)slot);
                var bought = profile.Collection.All.FirstOrDefault(c => c.Seed == legacySeed && c.Source == CopySource.Used && c.IsSlab);
                if (bought == null) continue;
                profile.VanShelfSoldCards.Add(ShelfKey(bought));
                profile.VanShelfSold.Remove(slot);
            }
        }

        /// <summary>Why a listing can't be bought (null: it can).</summary>
        public static string? Blocked(Profile profile, Listing listing)
        {
            if (listing.Sold) return "Sold.";
            if (profile.Dp < listing.Price) return $"It costs {listing.Price:N0} DP; you have {profile.Dp:N0}.";
            if (!profile.Collection.WouldKeep(listing.Copy)) return "You have 3 copies worth more already.";
            return null;
        }

        /// <summary>
        /// Buys a shelf or used listing: alley.shelf or alley.used, the copy Paid its price (a new id; the 4th-copy rule
        /// through <see cref="Collection.Take"/>), its slot marked sold. Returns the DP a displaced copy paid (refund.alley),
        /// or -1 if refused (blocked, the tab restocked since the listing was shown, its slot sold since, or, for a listing the
        /// shelf drew, the slab no longer at its slot on the shelf as drawn now: the wall opened or a build turned ready
        /// mid-period). A listing built by hand (a check's) isn't the shelf's, so only the period and the sold marks apply to it.
        /// </summary>
        public static int Buy(Profile profile, Listing listing, bool shelf)
        {
            if (Blocked(profile, listing) != null) return -1;
            // The listing's own period against now (the tab may have restocked, and been shown again, since it was listed).
            if (listing.Epoch != (shelf ? ShelfEpoch(profile) : UsedEpoch(profile))) return -1;
            if ((shelf ? profile.VanShelfEpoch : profile.VanUsedEpoch) != listing.Epoch) return -1;
            if (shelf ? profile.VanShelfSoldCards.Contains(ShelfKey(listing.Copy)) || (!listing.Restored && profile.VanShelfSold.Contains(listing.Slot))
                      : profile.VanUsedSold.Contains(listing.Slot)) return -1;
            // Still on the shelf: the same slab (its key), restored or not, at the same slot and price.
            if (shelf && listing.Current != null
                && !listing.Current(profile).Any(l => l.Slot == listing.Slot && ShelfKey(l.Copy) == ShelfKey(listing.Copy) && l.Restored == listing.Restored && l.Price == listing.Price && !l.Sold))
                return -1;
            if (!profile.Spend(shelf ? Ledger.AlleyShelf : Ledger.AlleyUsed, listing.Price)) return -1;
            var bought = listing.Copy;
            var copy = new CardCopy
            {
                Code = bought.Code, Rarity = bought.Rarity, Grade = bought.Grade, Seed = bought.Seed, Source = CopySource.Used,
                Slab = bought.Slab, Floor = bought.Floor, Id = profile.Collection.NextId++, Paid = listing.Price,
                Text = bought.Text, Set = bought.Set, Printed = bought.Printed,
            };
            var added = profile.Collection.Take(copy);
            profile.Earn(Ledger.AlleyRefund, added.Dp);
            if (shelf) profile.VanShelfSoldCards.Add(ShelfKey(listing.Copy));
            else profile.VanUsedSold.Add(listing.Slot);
            listing.Sold = true;
            return added.Dp;
        }

        /// <summary>Moth's Sell tab: the Sell desk exactly (its price, its refusals, ledger "sell").</summary>
        public static int Sell(Profile profile, CardCopy copy, Func<uint, int> neededByDecks) => SellDesk.Sell(profile, copy, neededByDecks);

        /// <summary>
        /// Whether the HELD row (Ines's Scapegoat, face down, not selectable, no price) hangs at the end of the rail: until the
        /// held-card-home scene is seen (no save field: <see cref="Profile.Seen"/> holds it). See <see cref="BackAlley.HeldCardDue"/>.
        /// </summary>
        public static bool HeldShown(Profile profile) => !profile.Seen.Contains(BackAlley.HeldHomeScene);

        /// <summary>The HELD row's printing, for its label only (spec 4.2): Ines's Scapegoat is its earliest printing by 2005-04 (null without one).</summary>
        public static Printing? HeldPrinting(PrintingIndex printings, ICardDataProvider cards) => CopyPrints.Earliest(HeldCode, null, "2005-04", printings, cards);

        /// <summary>A rail tag's placeholders for van:rail lines and the row: {card}, {grade}, {price} and {left} (duels left).</summary>
        public static Dictionary<string, string> RailFill(Profile profile, RailEntry entry, ICardDataProvider cards) => new Dictionary<string, string>
        {
            ["card"] = cards.Get(entry.Copy.Code)?.Name ?? "that card",
            ["grade"] = entry.Copy.IsSlab ? $"Grade {entry.Copy.Slab}" : Grades.RawLabel(entry.Copy.Grade),
            ["price"] = $"{entry.Price:N0} DP",
            ["left"] = Ante.DuelsLeft(profile, entry).ToString(),
        };
    }
}
