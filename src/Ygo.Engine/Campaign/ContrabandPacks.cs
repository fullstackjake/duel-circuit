using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// The Contraband Pack (docs/dev/ALLEY-SPEC-2026-11-01.md section 10; ALLEY-ECONOMY Evidence 8): an alley bracket's prize,
    /// never sold. Five different cards from the ever-Forbidden pool (every card some TCG list has Forbidden, minus the
    /// Alley's 33 exclusions, with a printing): slots 1-4 Raw, uniform from the pool, at a printing weighted 10000 /
    /// <see cref="Pricing.Unit"/> as the used market picks, graded at the foil odds; slot 5 a sealed slab of a card printed
    /// at Super Rare or better (<see cref="AlleyEconomy.PackSlabMinRarity"/>), Grade 9 at <see cref="AlleyEconomy.PackSlabNine"/>,
    /// else 10. Every copy is <see cref="CopySource.Prize"/>, Paid 0 (it sells for nothing), and a 4th copy pays no DP, as
    /// a Prize Pack's. Every roll comes from its seed. The No Limits Pack is the same pack over another pool (the explicit-pool
    /// constructor, <see cref="NoLimits"/>).
    /// </summary>
    public sealed class ContrabandPacks
    {
        public const int Size = 5;

        private readonly ICardDataProvider _cards;
        private readonly PrintingIndex _printings;
        /// <summary>The pool's cards that can fill slot 5 (a printing at the slab's minimum rarity or rarer).</summary>
        private readonly List<uint> _slabPool;

        public ContrabandPacks(ICardDataProvider cards, PrintingIndex printings, IEnumerable<Banlist> lists, Banlist alley)
        {
            _cards = cards;
            _printings = printings;
            Pool = Alley.Contraband(lists, alley, 9999, printings, cards).Where(c => Printings(c, Rarity.Common).Count > 0).ToList();
            _slabPool = Pool.Where(c => Printings(c, AlleyEconomy.PackSlabMinRarity).Count > 0).ToList();
        }

        /// <summary>
        /// A pack over an explicit pool (base codes; cards with no printing dropped, the order kept), in the Contraband Pack's
        /// format: the No Limits Pack (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 9.3) over <see cref="BackAlley.NoLimitsPackPool"/>.
        /// An empty pool gives empty packs.
        /// </summary>
        public ContrabandPacks(ICardDataProvider cards, PrintingIndex printings, IReadOnlyList<uint> pool)
        {
            _cards = cards;
            _printings = printings;
            Pool = pool.Distinct().Where(c => Printings(c, Rarity.Common).Count > 0).ToList();
            _slabPool = Pool.Where(c => Printings(c, AlleyEconomy.PackSlabMinRarity).Count > 0).ToList();
        }

        /// <summary>The No Limits Pack: the Invitational's prize, over the ready builds' ever-Forbidden cards (<see cref="BackAlley.NoLimitsPackPool"/>).</summary>
        public static ContrabandPacks NoLimits(AlleyData data, AlleyContext ctx) =>
            new ContrabandPacks(ctx.Cards, ctx.Printings, BackAlley.NoLimitsPackPool(data, ctx));

        /// <summary>
        /// Whether every pack is whole: <see cref="AlleyEconomy.PackSize"/> different cards, the last a slab, whatever the roll
        /// (enough cards, and enough slab cards that the four Raw pulls can't use them all up).
        /// </summary>
        public bool CanFill => Pool.Count >= AlleyEconomy.PackSize && _slabPool.Count >= Math.Max(1, AlleyEconomy.PackSize);

        /// <summary>The ever-Forbidden pool, in code order (base codes).</summary>
        public IReadOnlyList<uint> Pool { get; }

        /// <summary>A card's printings at <paramref name="min"/> or rarer (any date: the alley has no era).</summary>
        private List<Printing> Printings(uint code, Rarity min) =>
            _printings.Of(code, _cards.Get(code)?.Alias ?? 0).Where(p => p.Rarity >= min).ToList();

        /// <summary>A rarity among a card's printings, weighted by 10000/Unit (commoner printings come up more), and the printing of it.</summary>
        private static Printing Pick(List<Printing> printings, uint roll)
        {
            var rarities = printings.Select(p => p.Rarity).Distinct().OrderBy(r => r).ToList();
            double total = rarities.Sum(r => 10000.0 / Pricing.Unit(r)), pick = roll / 4294967296.0 * total;
            var rarity = rarities[rarities.Count - 1];
            foreach (var r in rarities)
            {
                pick -= 10000.0 / Pricing.Unit(r);
                if (pick < 0) { rarity = r; break; }
            }
            return printings.Where(p => p.Rarity == rarity).OrderBy(p => p.SetCode, StringComparer.Ordinal).First();
        }

        /// <summary>
        /// A pack's five pulls (no copies made), in slot order: four Raw cards, then the sealed slab (9 or 10, Super Rare
        /// and up), all different. The same for the same seed.
        /// </summary>
        public List<PrizePull> Roll(uint seed)
        {
            uint k = 0;
            uint R() => Hash.Mix(seed ^ 0xC0B70000u, 0xC7A8u, k++);
            var pulls = new List<PrizePull>();
            var left = Pool.ToList();
            int raw = Math.Max(0, AlleyEconomy.PackSize - 1);
            for (int slot = 1; slot <= raw && left.Count > 0; slot++)
            {
                var code = left[(int)(R() % (uint)left.Count)];
                left.Remove(code);
                var p = Pick(Printings(code, Rarity.Common), R());
                pulls.Add(new PrizePull { Slot = slot, Code = code, Rarity = p.Rarity, SetCode = p.SetCode, SetName = p.SetName });
            }
            // The sealed slab: a card not already in the pack.
            var slabs = _slabPool.Where(c => pulls.All(x => x.Code != c)).ToList();
            if (slabs.Count > 0)
            {
                var code = slabs[(int)(R() % (uint)slabs.Count)];
                var p = Pick(Printings(code, AlleyEconomy.PackSlabMinRarity), R());
                int grade = R() % 10000 < AlleyEconomy.PackSlabNine * 10000 ? 9 : 10;
                pulls.Add(new PrizePull { Slot = raw + 1, Code = code, Rarity = p.Rarity, Slab = grade, SetCode = p.SetCode, SetName = p.SetName });
            }
            return pulls;
        }

        /// <summary>
        /// Opens a pack into your collection: each pull a <see cref="CopySource.Prize"/> copy (Raw at the foil odds, or the
        /// slab sealed at its grade), Paid 0. A 4th copy keeps your best three as any pull does, and pays no DP. Returns the
        /// pulls in reveal order (least rare first, the slab last), as <see cref="PrizePacks.Give"/> does.
        /// </summary>
        public List<PrizePull> Give(Profile profile, uint seed)
        {
            var pulls = Roll(seed);
            foreach (var pull in pulls)
            {
                pull.IsNew = !profile.Collection.Owns(pull.Code);
                var copy = profile.Collection.NewCopy(pull.Code, pull.Rarity, CopySource.Prize);
                copy.Paid = 0;
                // The pull's printing, dated by its set name (spec 4.2, S4).
                if (pull.SetCode.Length > 0) profile.Collection.Stamp(copy, new Printing(pull.SetCode, pull.SetName, pull.Rarity), _printings);
                if (pull.Slab > 0) copy.Grade = copy.Slab = copy.Floor = pull.Slab;
                pull.Value = profile.Collection.Value(copy);
                var added = profile.Collection.Take(copy);
                pull.Kept = added.Kept;
                pull.Copy = added.Copy;
                pull.Replaced = added.Replaced;
            }
            return PrizePacks.RevealOrder(pulls);
        }

        /// <summary>
        /// The pack's odds, shown as a Prize Pack's are (E5): slots 1-4's rarity mix (<see cref="PrizeOdds.Field"/>: each
        /// card's printings weighted as <see cref="Roll"/> picks them, averaged over the pool; every slot's card is uniform
        /// over the pool, so this is exact for each), how many cards they draw from, and slot 5's mix over the slab pool and
        /// its grade split. No tournament slot.
        /// </summary>
        public PrizeOdds Odds()
        {
            List<(Rarity rarity, double share)> Mix(List<uint> codes, Rarity min)
            {
                var sum = new Dictionary<Rarity, double>();
                foreach (var code in codes)
                {
                    var rarities = Printings(code, min).Select(p => p.Rarity).Distinct().ToList();
                    double total = rarities.Sum(r => 10000.0 / Pricing.Unit(r));
                    foreach (var r in rarities) sum[r] = sum.GetValueOrDefault(r) + 10000.0 / Pricing.Unit(r) / total / codes.Count;
                }
                return sum.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)).ToList();
            }
            return new PrizeOdds
            {
                Field = Mix(Pool.ToList(), Rarity.Common),
                FieldCards = Pool.Count,
                Slab = Mix(_slabPool, AlleyEconomy.PackSlabMinRarity),
                SlabTenPercent = (int)Math.Round((1 - AlleyEconomy.PackSlabNine) * 100),
            };
        }
    }
}
