using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>One card of a Prize Pack.</summary>
    public sealed class PrizePull
    {
        /// <summary>1-3: from the field's decks; 4: from a tournament set; 5: the sealed slab.</summary>
        public int Slot;
        public uint Code;
        public Rarity Rarity;
        /// <summary>The slab's grade (9 or 10) for slot 5; 0 for a Raw copy.</summary>
        public int Slab;
        /// <summary>The printing's set (slot 4: the tournament set it came from).</summary>
        public string SetCode = "", SetName = "";
        /// <summary>Set by <see cref="PrizePacks.Give"/>: the copy (null when a 4th copy wasn't kept), whether it was new to you, its value.</summary>
        public CardCopy? Copy;
        public bool Kept, IsNew;
        public int Value;
        /// <summary>The rarity of a Raw copy this one replaced as a 4th copy (it goes for 0 DP: prizes never pay DP).</summary>
        public Rarity? Replaced;
    }

    /// <summary>A Prize Pack's odds (<see cref="PrizePacks.Odds"/>): each mix is (rarity, share) least rare first, summing to 1 (empty when the slot can't fill).</summary>
    public sealed class PrizeOdds
    {
        /// <summary>Slots 1-3: the rarity of a card from the field's decks, and how many cards they draw from.</summary>
        public List<(Rarity rarity, double share)> Field = new List<(Rarity, double)>();
        public int FieldCards;
        /// <summary>Slot 4: the tournament set (null when the game has none) and its rarity mix.</summary>
        public SetInfo? TournamentSet;
        public List<(Rarity rarity, double share)> Tournament = new List<(Rarity, double)>();
        /// <summary>Slot 5, the sealed slab: its rarity mix, and the per cent at Grade 10 (the rest Grade 9).</summary>
        public List<(Rarity rarity, double share)> Slab = new List<(Rarity, double)>();
        public int SlabTenPercent;
    }

    /// <summary>
    /// Prize Packs (BRACKETS-SPEC-2026-11-01): five cards, revealed least rare first. Slots 1-3 are three different cards from
    /// the field's 7 CPU decks (released by the cup's pool), at a printing of Rare or better from the pool's era (any
    /// printing, for a field with too few), rarity weighted by 10000/<see cref="Pricing.Unit"/> as the used market is;
    /// slot 4 a card from the real tournament set of the pool's time (the latest dated by its month, else the earliest);
    /// slot 5 a sealed slab, Grade 9 (75%) or 10 (25%), of a field card at Super Rare or better. Copies are
    /// <see cref="CopySource.Prize"/>, graded at the foil odds, paid 0: the Sell desk pays nothing for them, and a 4th copy
    /// pays no DP either. Every roll comes from the bracket's seed and the pack's number.
    /// </summary>
    public sealed class PrizePacks
    {
        public const int Size = 5, FieldSlots = 3;
        /// <summary>Per cent of slabs at Grade 10 (the rest Grade 9) [placeholder].</summary>
        public const int SlabTenPercent = 25;

        private static readonly Regex[] TournamentNames =
        {
            new Regex(@"^OTS Tournament Pack \d+$"),
            new Regex(@"^Tournament Pack \d+$"),
            new Regex(@"^Tournament Pack: \w+ Season$"),
            new Regex(@"^Turbo Pack: Booster \w+$"),
            new Regex(@"^Ultimate Tournament Pack \d+$"),
        };

        private readonly ICardDataProvider _cards;
        private readonly PrintingIndex _printings;
        private readonly Func<string, Deck?> _loadDeck;
        /// <summary>The tournament sets, oldest first, each with its printings (base code, rarity, set code).</summary>
        private readonly List<(SetInfo set, List<(uint code, Rarity rarity, string setCode)> cards)> _tournament =
            new List<(SetInfo, List<(uint, Rarity, string)>)>();
        private readonly Dictionary<string, List<uint>> _fieldCards = new Dictionary<string, List<uint>>();

        /// <param name="loadDeck">A deck by name (null when there's none): the CPUs' decks.</param>
        public PrizePacks(ICardDataProvider cards, PrintingIndex printings, Func<string, Deck?> loadDeck)
        {
            _cards = cards;
            _printings = printings;
            _loadDeck = loadDeck;
            var sets = printings.Sets.Values.Where(IsTournamentSet).ToDictionary(s => s.Code);
            var bySet = sets.Keys.ToDictionary(k => k, _ => new List<(uint, Rarity, string)>());
            foreach (var pair in printings.All)
            {
                if (cards.Get(pair.Key) == null) continue;
                uint baseCode = Banlist.BaseCode(pair.Key, cards);
                foreach (var p in pair.Value)
                    if (bySet.TryGetValue(PrintingIndex.SetPrefix(p.SetCode), out var list)) list.Add((baseCode, p.Rarity, p.SetCode));
            }
            foreach (var set in sets.Values.OrderBy(s => s.Date, StringComparer.Ordinal).ThenBy(s => s.Code, StringComparer.Ordinal))
            {
                var list = bySet[set.Code].Distinct().OrderBy(c => c.Item1).ThenBy(c => c.Item2).ThenBy(c => c.Item3, StringComparer.Ordinal).ToList();
                if (list.Count > 0) _tournament.Add((set, list));
            }
        }

        public static bool IsTournamentSet(SetInfo set) =>
            !set.Name.Contains("(POR)") && !set.Name.Contains("Speed Duel") && TournamentNames.Any(r => r.IsMatch(set.Name));

        /// <summary>The tournament sets the game has cards for, oldest first.</summary>
        public IEnumerable<SetInfo> TournamentSets => _tournament.Select(t => t.set);

        /// <summary>Slot 4's set for a pool month ("2005-09"): the latest tournament set dated by it, else the earliest.</summary>
        public SetInfo? TournamentSet(string pool) => TournamentEntry(pool)?.set;

        private (SetInfo set, List<(uint code, Rarity rarity, string setCode)> cards)? TournamentEntry(string pool)
        {
            if (_tournament.Count == 0) return null;
            if (pool.Length == 0) return _tournament[_tournament.Count - 1];
            var dated = _tournament.Where(t => t.set.Date.Length >= 7 && string.CompareOrdinal(t.set.Date.Substring(0, 7), pool) <= 0).ToList();
            return dated.Count > 0 ? dated[dated.Count - 1] : _tournament[0];
        }

        /// <summary>The field's cards: every base code in the 7 CPU decks released by the pool, in code order.</summary>
        public List<uint> FieldCards(Bracket b, string pool)
        {
            var key = b.Event + "|" + pool + "|" + string.Join(",", b.Slots.Where(s => s != null && !s.You).Select(s => s.Deck));
            if (_fieldCards.TryGetValue(key, out var cached)) return cached;
            var codes = new HashSet<uint>();
            foreach (var e in b.Slots.Where(s => s != null && !s.You))
            {
                var deck = _loadDeck(e.Deck);
                if (deck == null) continue;
                foreach (var c in deck.Main.Concat(deck.Extra).Concat(deck.Side))
                {
                    uint baseCode = Banlist.BaseCode(c, _cards);
                    if (_cards.Get(baseCode) != null && DeckRules.Released(baseCode, pool, _printings, _cards)) codes.Add(baseCode);
                }
            }
            return _fieldCards[key] = codes.OrderBy(c => c).ToList();
        }

        /// <summary>A card's printings at <paramref name="min"/> or rarer: those out by the pool's month, or (<paramref name="anyDate"/>) all.</summary>
        private List<Printing> Printings(uint code, Rarity min, string pool, bool anyDate) =>
            _printings.Of(code, _cards.Get(code)?.Alias ?? 0).Where(p => p.Rarity >= min && (anyDate || OutBy(p, pool))).ToList();

        /// <summary>
        /// Knob (card versions phase 2, an owner decision): date the pool's printings by set name (<see cref="PrintingIndex.DateOf"/>,
        /// spec S4) instead of by their code prefix's earliest set. True drops printings released after the pool's month that a
        /// prefix dates earlier (YR01 at TeleDAD, OTS Tournament Pack 11 at Link Dawn...), which changes 88 of 480 sampled
        /// Prize Packs (Link Dawn, Synchro Peak, TOSS, TeleDAD). Off, packs open as they always did; either way each pull is
        /// stamped by its set name's date, so its text is right.
        /// </summary>
        public static bool PoolByName = false;

        private bool OutBy(Printing p, string pool)
        {
            if (pool.Length == 0) return true;
            if (PoolByName)
            {
                var date = _printings.DateOf(p);
                return date.Length >= 7 && string.CompareOrdinal(date.Substring(0, 7), pool) <= 0;
            }
            return _printings.Sets.TryGetValue(PrintingIndex.SetPrefix(p.SetCode), out var set) && set.Date.Length >= 7
                && string.CompareOrdinal(set.Date.Substring(0, 7), pool) <= 0;
        }

        /// <summary>Field cards with a printing at <paramref name="min"/> or rarer (in the pool's era when at least <paramref name="need"/> have one).</summary>
        private (List<uint> codes, bool anyDate) Candidates(Bracket b, string pool, Rarity min, int need)
        {
            var field = FieldCards(b, pool);
            var dated = field.Where(c => Printings(c, min, pool, false).Count > 0).ToList();
            if (dated.Count >= need) return (dated, false);
            return (field.Where(c => Printings(c, min, pool, true).Count > 0).ToList(), true);
        }

        /// <summary>A rarity among a card's printings, weighted by 10000/Unit (commoner printings come up more), and the printing.</summary>
        private static Printing PickPrinting(List<Printing> printings, uint roll)
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

        /// <summary>A pack's five pulls (no copies made), in slot order: the same for the same bracket and pack number.</summary>
        public List<PrizePull> Roll(Bracket b, string pool, int pack)
        {
            uint k = 0;
            uint R() => Hash.Mix(b.Seed ^ 0x9A1C0000u, (uint)pack, k++);
            var pulls = new List<PrizePull>();
            // Slots 1-3: three different field cards at Rare or better.
            var (rares, anyDate) = Candidates(b, pool, Rarity.Rare, FieldSlots);
            var left = rares.ToList();
            for (int slot = 1; slot <= FieldSlots && left.Count > 0; slot++)
            {
                var code = left[(int)(R() % (uint)left.Count)];
                left.Remove(code);
                var p = PickPrinting(Printings(code, Rarity.Rare, pool, anyDate), R());
                pulls.Add(new PrizePull { Slot = slot, Code = code, Rarity = p.Rarity, SetCode = p.SetCode, SetName = p.SetName });
            }
            // Slot 4: the tournament set of the pool's time, a printing weighted like the rest.
            var t = TournamentEntry(pool);
            if (t != null)
            {
                var (set, cards) = t.Value;
                double total = cards.Sum(c => 10000.0 / Pricing.Unit(c.rarity)), pick = R() / 4294967296.0 * total;
                var chosen = cards[cards.Count - 1];
                foreach (var c in cards)
                {
                    pick -= 10000.0 / Pricing.Unit(c.rarity);
                    if (pick < 0) { chosen = c; break; }
                }
                pulls.Add(new PrizePull { Slot = 4, Code = chosen.code, Rarity = chosen.rarity, SetCode = chosen.setCode, SetName = set.Name });
            }
            // Slot 5: a sealed slab of a field card at Super Rare or better (one not already in the pack when it can).
            var (supers, superAny) = Candidates(b, pool, Rarity.SuperRare, 1);
            var fresh = supers.Where(c => pulls.All(p => p.Code != c)).ToList();
            if (fresh.Count == 0) fresh = supers;
            if (fresh.Count > 0)
            {
                var code = fresh[(int)(R() % (uint)fresh.Count)];
                var p = PickPrinting(Printings(code, Rarity.SuperRare, pool, superAny), R());
                int grade = R() % 100 < SlabTenPercent ? 10 : 9;
                pulls.Add(new PrizePull { Slot = 5, Code = code, Rarity = p.Rarity, Slab = grade, SetCode = p.SetCode, SetName = p.SetName });
            }
            return pulls;
        }

        /// <summary>
        /// The odds of a bracket's Prize Pack, shown before the entry is paid (SHOP-REWORK-SPEC section 5): slots 1-3's
        /// rarity mix over the field's candidate cards (each card's printings weighted by 10000/Unit as <see cref="Roll"/>
        /// picks them, averaged over the candidates: exact for slot 1, about right for 2-3, which draw without
        /// replacement), slot 4's tournament set and its mix, and slot 5's mix and grade split.
        /// </summary>
        public PrizeOdds Odds(Bracket b, string pool)
        {
            var odds = new PrizeOdds { SlabTenPercent = SlabTenPercent };
            List<(Rarity rarity, double share)> Mix(List<uint> codes, bool anyDate, Rarity min)
            {
                var sum = new Dictionary<Rarity, double>();
                foreach (var code in codes)
                {
                    var rarities = Printings(code, min, pool, anyDate).Select(p => p.Rarity).Distinct().ToList();
                    double total = rarities.Sum(r => 10000.0 / Pricing.Unit(r));
                    foreach (var r in rarities) sum[r] = sum.GetValueOrDefault(r) + 10000.0 / Pricing.Unit(r) / total / codes.Count;
                }
                return sum.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)).ToList();
            }
            var (rares, anyDate) = Candidates(b, pool, Rarity.Rare, FieldSlots);
            odds.FieldCards = rares.Count;
            odds.Field = Mix(rares, anyDate, Rarity.Rare);
            var t = TournamentEntry(pool);
            if (t != null)
            {
                var (set, cards) = t.Value;
                odds.TournamentSet = set;
                double total = cards.Sum(c => 10000.0 / Pricing.Unit(c.rarity));
                odds.Tournament = cards.GroupBy(c => c.rarity).OrderBy(g => g.Key).Select(g => (g.Key, g.Sum(c => 10000.0 / Pricing.Unit(c.rarity)) / total)).ToList();
            }
            var (supers, superAny) = Candidates(b, pool, Rarity.SuperRare, 1);
            odds.Slab = Mix(supers, superAny, Rarity.SuperRare);
            return odds;
        }

        /// <summary>Reveal order: least rare first (by printing value), the slab last.</summary>
        public static List<PrizePull> RevealOrder(IEnumerable<PrizePull> pulls) =>
            pulls.OrderBy(p => p.Slab > 0 ? 1 : 0).ThenBy(p => Pricing.Unit(p.Rarity)).ThenBy(p => p.Slot).ToList();

        /// <summary>
        /// Opens pack <paramref name="pack"/> of a bracket into your collection: each pull a <see cref="CopySource.Prize"/> copy,
        /// paid 0 (the slab sealed at its grade). A 4th copy keeps your best three as any pull does, but pays no DP. Returns the
        /// pulls in reveal order (least rare first).
        /// </summary>
        public List<PrizePull> Give(Profile profile, Bracket b, string pool, int pack)
        {
            var pulls = Roll(b, pool, pack);
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
            return RevealOrder(pulls);
        }
    }
}
