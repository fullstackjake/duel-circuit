using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>A card a CPU puts up: a printing, a grade (Raw, or a slab's), a wear seed, and a flag for special cards ("" for none).</summary>
    public sealed class Stake
    {
        public uint Code;
        public Rarity Rarity;
        public int Grade, Slab;
        public uint Seed;
        public string Flag = "";
        /// <summary>
        /// Its printing: the set code and month ("yyyy-mm"), "" when unknown (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 4.2, 9.2).
        /// The text is derived when it's won (<see cref="Ante.ToCopy"/>).
        /// </summary>
        public string Set = "", Printed = "";
        /// <summary>In memory only (never saved): its printing was looked for and none is dated, so a binder read doesn't look again.</summary>
        internal bool PrintTried;
    }

    /// <summary>
    /// A CPU's binder (<see cref="Ante.Binder"/>): up to <see cref="AlleyEconomy.BinderSize"/> different cards, the next
    /// stake first; <see cref="Serial"/> counts the stakes drawn (each from its own seed) and <see cref="RefilledAt"/> the
    /// alley duel count its refills are counted from.
    /// </summary>
    public sealed class StakeBinder
    {
        public string Owner = "";
        public List<Stake> Cards = new List<Stake>();
        public int Serial, RefilledAt;
    }

    /// <summary>
    /// A duel's stakes, held in the save from its start: your DP (out of <see cref="Profile.Dp"/>, not spent yet), their
    /// card, and at Orvel's Table your card. A save loaded with one settles it as a loss (<see cref="Ante.SettleAbandoned"/>).
    /// </summary>
    public sealed class AnteLive
    {
        /// <summary>The table's id and the seat (0: an encore).</summary>
        public string Table = "";
        public int Seat, Held, StartedAt;
        public Stake? Theirs;
        public CardCopy? Yours;
        /// <summary>The binder their card came from (it goes back on top unless you win it).</summary>
        public string Binder = "";
    }

    /// <summary>A card of yours on Moth's rail (lost at Orvel's Table): claimed back at the tag until the duel count reaches <see cref="ExpiresAt"/>.</summary>
    public sealed class RailEntry
    {
        public CardCopy Copy = null!;
        public int Price, ExpiresAt;
    }

    /// <summary>How a held duel's stakes settled.</summary>
    public sealed class AnteSettlement
    {
        /// <summary>win | loss | draw | firstturn | quit | none (nothing was held).</summary>
        public string Outcome = "";
        /// <summary>Your DP back, your DP lost (spent to <see cref="Ledger.AlleyStake"/>).</summary>
        public int Returned, Lost;
        /// <summary>Their card, yours now (Source Ante, Paid 0); <see cref="Added"/> says whether a 4th copy kept it (for 0 DP).</summary>
        public CardCopy? Won;
        public Added? Added;
        /// <summary>Your card on Moth's rail (Orvel's Table).</summary>
        public RailEntry? Railed;
        /// <summary>Story events: antewin, antelose, rail, firstturn.</summary>
        public List<string> Events = new List<string>();
        /// <summary>A quit's scene placeholders (<see cref="Ante.SettleAbandoned"/> sets them; a result's are <see cref="AlleyResult.Fill"/>).</summary>
        public Dictionary<string, string> Fill = new Dictionary<string, string>();
        public string Summary = "";
    }

    /// <summary>
    /// Ante (docs/dev/ALLEY-SPEC-2026-11-01.md section 9; docs/dev/ALLEY-ECONOMY-2026-11-01.md's model): your DP stake is held in
    /// the save from the duel's start, back on a win, a draw or a first-turn loss, lost on a loss or a quit; the CPU puts up
    /// the next card of its binder, worth at least your DP stake (<see cref="AlleyEconomy.StakeFloor"/>). Won cards are
    /// Paid 0, so they sell for nothing. Only at Orvel's Table do you stake a card too: a spare one, never one a saved deck
    /// needs; lost, it hangs on Moth's rail. Every DP change goes through the ledger; held DP leaves Dp without a line and
    /// either comes back or is spent as alley.stake.
    /// </summary>
    public static class Ante
    {
        /// <summary>
        /// Penny's stake's flag: her one Common (a Kuriboh), worth less than the Street stake, the one exception to
        /// <see cref="AlleyEconomy.StakeFloor"/>. Nothing of yours is held against it ("Penny plays for the Common.").
        /// </summary>
        public const string PennyFlag = "penny";
        /// <summary>Penny's card: Kuriboh.</summary>
        public const uint PennyCard = 40640057;

        public static int StakeFor(AlleyTier tier) => tier switch
        {
            AlleyTier.Street => AlleyEconomy.StreetStake,
            AlleyTier.Orvel => AlleyEconomy.OrvelStake,
            AlleyTier.NoLimits => AlleyEconomy.NoLimitsStake,
            _ => AlleyEconomy.DenStake,
        };

        /// <summary>The CPU's card's value range at a tier (Orvel's: no cap, and at least what the cheapest card you may stake matches, so every slab can be staked).</summary>
        private static (double floor, int cap) Range(AlleyTier tier) => tier switch
        {
            AlleyTier.Street => (AlleyEconomy.StakeFloor * AlleyEconomy.StreetStake, AlleyEconomy.StreetCap),
            AlleyTier.Orvel => (AlleyEconomy.OrvelMatchMin * AlleyEconomy.OrvelMinCard, int.MaxValue),
            AlleyTier.NoLimits => (AlleyEconomy.StakeFloor * AlleyEconomy.NoLimitsStake, AlleyEconomy.NoLimitsCap),
            _ => (AlleyEconomy.StakeFloor * AlleyEconomy.DenStake, AlleyEconomy.DenCap),
        };

        private static (int grade, bool slab, double weight)[] Conditions(AlleyTier tier) => tier switch
        {
            AlleyTier.Street => AlleyEconomy.StreetGrades,
            AlleyTier.Orvel => AlleyEconomy.OrvelGrades,
            AlleyTier.NoLimits => AlleyEconomy.NoLimitsGrades,
            _ => AlleyEconomy.DenGrades,
        };

        /// <summary>A No Limits build's binder owner key ("nl:&lt;pilot&gt;").</summary>
        public const string NoLimitsOwner = "nl:";

        /// <summary>A binder owner's tier: its table's ("regular:&lt;table&gt;...", "hl:&lt;id&gt;" by the headliner's table), Orvel's, Nyx's (Den stakes), No Limits' ("nl:&lt;pilot&gt;").</summary>
        private static AlleyTier TierOf(string owner, AlleyContext ctx)
        {
            if (owner == "orvel") return AlleyTier.Orvel;
            if (owner.StartsWith("nyx:")) return AlleyTier.Nyx;
            if (owner.StartsWith(NoLimitsOwner)) return AlleyTier.NoLimits;
            if (owner == BackAlley.PennyId) return AlleyTier.Street;
            var data = ctx.Data;
            if (data == null) return AlleyTier.Den;
            string table = owner.StartsWith("hl:") ? data.FindHeadliner(owner.Substring(3))?.Table ?? ""
                : owner.StartsWith("regular:") ? owner.Substring(8).Split('.')[0] : "";
            int i = data.IndexOf(table);
            return i >= 0 ? data.Tables[i].Tier : AlleyTier.Den;
        }

        /// <summary>The deck a binder is drawn from: tonight's opponent with that binder, a headliner's deck, an authored seat's.</summary>
        private static Deck? DeckFor(Profile profile, string owner, AlleyContext ctx, out HashSet<uint> contraband, out List<uint> lead)
        {
            contraband = new HashSet<uint>();
            lead = new List<uint>();
            var o = profile.AlleyOpponents.FirstOrDefault(x => x.Binder == owner);
            if (owner.StartsWith("hl:") || owner.StartsWith(NoLimitsOwner))
            {
                // A headliner's, or a No Limits build's (the build's deck from its build: block, its stakes: as leads).
                var h = ctx.Data?.FindHeadliner(owner.Substring(3));
                if (h != null) lead = h.Stakes.ToList();
                var deck = h != null ? ctx.LoadDeck(h.Deck) : o != null ? BackAlley.DeckOf(o, ctx) : null;
                // Its contraband: the cards in it that some TCG list has Forbidden (a build's: its deck ∩ the era-9999 pool).
                if (deck != null) contraband = new HashSet<uint>(ctx.Pool(9999));
                return deck;
            }
            if (o == null) return null;
            contraband = new HashSet<uint>(o.Added.Select(c => Banlist.BaseCode(c, ctx.Cards)));
            return BackAlley.DeckOf(o, ctx);
        }

        /// <summary>
        /// A CPU's binder, made the first time it's asked for (from <paramref name="deck"/>, else the deck its owner plays)
        /// and refilled lazily: one stake per <see cref="AlleyEconomy.RefillEvery"/> alley duels since, up to
        /// <see cref="AlleyEconomy.BinderSize"/>. A headliner's starts with its `stakes:` in order.
        /// </summary>
        public static StakeBinder Binder(Profile profile, string owner, AlleyContext ctx, Deck? deck = null)
        {
            bool made = profile.Binders.TryGetValue(owner, out var binder);
            if (owner == BackAlley.PennyId) return PennyBinder(profile, made ? binder! : profile.Binders[owner] = new StakeBinder { Owner = owner, RefilledAt = -1 }, ctx);
            if (!made) profile.Binders[owner] = binder = new StakeBinder { Owner = owner, RefilledAt = profile.AlleyDuels };
            int refills = AlleyEconomy.RefillEvery > 0 ? (profile.AlleyDuels - binder!.RefilledAt) / AlleyEconomy.RefillEvery : 0;
            if (refills > 0) binder.RefilledAt += refills * AlleyEconomy.RefillEvery;
            int want = made ? Math.Min(refills, AlleyEconomy.BinderSize - binder.Cards.Count) : AlleyEconomy.BinderSize;
            if (want <= 0) { Print(profile, owner, binder, ctx, null, deck); return binder; }
            var tier = TierOf(owner, ctx);
            HashSet<uint> contraband;
            List<uint> lead, codes;
            int era = 0;
            if (tier == AlleyTier.Orvel)
            {
                // Orvel stakes slabs of the ever-Forbidden pool (minus the exclusions), not of his deck.
                contraband = new HashSet<uint>(ctx.Pool(9999));
                lead = new List<uint>();
                codes = contraband.ToList();
            }
            else
            {
                var played = DeckFor(profile, owner, ctx, out contraband, out lead) is Deck d && deck == null ? d : deck;
                codes = played == null ? new List<uint>() : played.Main.Concat(played.Extra).Select(c => Banlist.BaseCode(c, ctx.Cards)).Distinct().ToList();
                era = EraOf(played);
            }
            var candidates = Candidates(codes, tier, contraband, ctx);
            // A new headliner binder leads with its stakes:, each at a printing that fits.
            if (!made)
                foreach (var code in lead)
                    if (binder.Cards.Count < AlleyEconomy.BinderSize && binder.Cards.All(x => x.Code != code)
                        && Draw(profile, binder, candidates.Where(c => c.stake.Code == code).ToList()) is Stake s)
                        binder.Cards.Add(s);
            for (int added = made ? 0 : binder.Cards.Count; added < want && binder.Cards.Count < AlleyEconomy.BinderSize; added++)
            {
                var stake = Draw(profile, binder, candidates.Where(c => binder.Cards.All(x => x.Code != c.stake.Code)).ToList());
                if (stake == null) break;
                binder.Cards.Add(stake);
            }
            Print(profile, owner, binder, ctx, era, deck);
            return binder;
        }

        /// <summary>The year a binder's stakes are printed by: its deck's `#era` (0: none, any date).</summary>
        private static int EraOf(Deck? deck) => deck != null && int.TryParse(deck.Note("era"), out var y) ? y : 0;

        /// <summary>
        /// Gives every stake in a binder that has none its printing (spec 4.2): among the card's printings at its rarity,
        /// those dated by the CPU deck's `#era` when there are any, else any, by Hash.Mix of the stake's seed
        /// (<see cref="CopyPrints.BySeed"/>); Penny's Kuriboh is its earliest printing at that rarity. Stakes from an older
        /// save get theirs the first time the binder is read (the same printing a new draw would). The draw itself is untouched.
        /// <paramref name="era"/>: the deck's era when the caller knows it (null: read it from the owner's deck).
        /// </summary>
        private static void Print(Profile profile, string owner, StakeBinder binder, AlleyContext ctx, int? era, Deck? deck)
        {
            if (ctx.Printings == null || binder.Cards.All(x => x == null || x.Set.Length > 0 || x.PrintTried)) return;
            if (era == null && deck == null && owner != "orvel" && owner != BackAlley.PennyId)
            {
                // The deck's era comes from its deck: a context that can't load decks (a check's) leaves the stakes for later.
                if (ctx.LoadDeck == null) return;
                deck = DeckFor(profile, owner, ctx, out _, out _);
            }
            era ??= EraOf(deck);
            foreach (var stake in binder.Cards)
                if (stake != null && stake.Set.Length == 0 && !stake.PrintTried)
                {
                    Print(stake, era.Value, ctx);
                    if (stake.Set.Length == 0) stake.PrintTried = true;
                }
        }

        private static void Print(Stake stake, int era, AlleyContext ctx)
        {
            if (stake.Set.Length > 0) return;
            var p = stake.Flag == PennyFlag
                ? CopyPrints.Earliest(stake.Code, stake.Rarity, "", ctx.Printings, ctx.Cards)
                : (era > 0 ? CopyPrints.BySeed(stake.Code, stake.Rarity, era, stake.Seed, ctx.Printings, ctx.Cards) : null)
                  ?? CopyPrints.BySeed(stake.Code, stake.Rarity, 9999, stake.Seed, ctx.Printings, ctx.Cards);
            if (p is not Printing printing) return;
            var date = ctx.Printings.DateOf(printing);
            if (date.Length < 7) return;
            stake.Set = printing.SetCode;
            stake.Printed = date.Substring(0, 7);
        }

        /// <summary>
        /// The No Limits restock (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 4.5, <see cref="AlleyEconomy.NoLimitsRestock"/>):
        /// when a night is drawn, each seated build's lead cards (its `stakes:`) missing from its binder go back in, one copy of
        /// each, behind the leads still there, each a new draw from the binder's seeded stream (a new serial, printing and
        /// seed). Called by <see cref="BackAlley"/> for every build it seats, so once a night; an encore isn't a night. The
        /// binder stays at <see cref="AlleyEconomy.BinderSize"/>: a restock into a full binder drops its last non-lead card.
        /// Any other owner, or the knob off: the binder as it is.
        /// </summary>
        public static StakeBinder Restock(Profile profile, string owner, AlleyContext ctx)
        {
            var binder = Binder(profile, owner, ctx);
            if (!AlleyEconomy.NoLimitsRestock || !owner.StartsWith(NoLimitsOwner)) return binder;
            var h = ctx.Data?.FindHeadliner(owner.Substring(NoLimitsOwner.Length));
            if (h == null || h.Stakes.Count == 0) return binder;
            var deck = ctx.LoadDeck(h.Deck);
            if (deck == null) return binder;
            var codes = deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, ctx.Cards)).Distinct().ToList();
            var candidates = Candidates(codes, AlleyTier.NoLimits, new HashSet<uint>(ctx.Pool(9999)), ctx);
            foreach (var code in h.Stakes)
            {
                if (binder.Cards.Any(x => x.Code == code)) continue;
                var stake = Draw(profile, binder, candidates.Where(c => c.stake.Code == code).ToList());
                if (stake == null) continue;
                int at = binder.Cards.FindLastIndex(x => h.Stakes.Contains(x.Code)) + 1;
                binder.Cards.Insert(at, stake);
                int drop = binder.Cards.FindLastIndex(x => !h.Stakes.Contains(x.Code));
                if (binder.Cards.Count > AlleyEconomy.BinderSize && drop >= 0) binder.Cards.RemoveAt(drop);
            }
            Print(profile, owner, binder, ctx, EraOf(deck), deck);
            return binder;
        }

        /// <summary>Whether a card (base code) fits some printing and condition in a tier's range, so it can be staked there (a build's `stakes:` lead must).</summary>
        public static bool CanStake(uint code, AlleyTier tier, AlleyContext ctx) => Candidates(new[] { code }, tier, new HashSet<uint>(), ctx).Count > 0;

        /// <summary>
        /// Penny's binder: one Kuriboh, the same card every night until you win it (her first, worn from eleven years of
        /// Fridays, is Raw 4); once it's gone, a fresh one the next night she sits ("Night one of a new streak."). Its
        /// <see cref="StakeBinder.RefilledAt"/> is the Street table's night it was last filled, not a duel count.
        /// </summary>
        private static StakeBinder PennyBinder(Profile profile, StakeBinder binder, AlleyContext ctx)
        {
            var street = ctx.Data?.Tables.FirstOrDefault(t => t.Tier == AlleyTier.Street)?.Id ?? "dumpster-row";
            int night = profile.AlleyTables.TryGetValue(street, out var s) ? s.Night : 0;
            if (binder.Cards.Count > 0 || night <= binder.RefilledAt) { Print(profile, binder.Owner, binder, ctx, 0, null); return binder; }
            uint seed = Hash.Mix(profile.Collection.Salt, CardUnlocks.StableHash(binder.Owner), (uint)binder.Serial);
            var alias = ctx.Cards.Get(PennyCard)?.Alias ?? 0;
            var rarity = ctx.Printings.Of(PennyCard, alias).Any(p => p.Rarity == Rarity.Common) ? Rarity.Common : ctx.Commonest(PennyCard);
            binder.Cards.Add(new Stake
            {
                Code = PennyCard, Rarity = rarity, Grade = binder.Serial == 0 ? 4 : Grades.Roll(CopySource.Used, seed), Seed = Hash.Mix(seed, PennyCard), Flag = PennyFlag,
            });
            binder.Serial++;
            binder.RefilledAt = night;
            Print(profile, binder.Owner, binder, ctx, 0, null);
            return binder;
        }

        /// <summary>Every (card, printing, condition) a tier allows, with its weight: the condition's, 10000 / Unit for the printing, 3× for contraband.</summary>
        private static List<(Stake stake, double weight)> Candidates(IEnumerable<uint> codes, AlleyTier tier, HashSet<uint> contraband, AlleyContext ctx)
        {
            var (floor, cap) = Range(tier);
            var list = new List<(Stake, double)>();
            foreach (var code in codes.OrderBy(c => c))
            {
                var alias = ctx.Cards.Get(code)?.Alias ?? 0;
                var commonest = ctx.Printings.Commonest(code, alias);
                foreach (var r in ctx.Printings.Of(code, alias).Select(p => p.Rarity).Distinct().OrderBy(r => r))
                {
                    if (tier == AlleyTier.Orvel && r < AlleyEconomy.OrvelMinRarity) continue;
                    foreach (var (grade, slab, w) in Conditions(tier))
                    {
                        int v = Pricing.V(new CardCopy { Code = code, Rarity = r, Grade = grade, Slab = slab ? grade : 0 }, commonest);
                        if (v < floor || v > cap) continue;
                        list.Add((new Stake { Code = code, Rarity = r, Grade = grade, Slab = slab ? grade : 0 },
                            w * 10000.0 / Pricing.Unit(r) * (contraband.Contains(code) ? AlleyEconomy.ContrabandWeight : 1)));
                    }
                }
            }
            return list;
        }

        /// <summary>One stake, from Hash.Mix(salt, hash(owner), serial++): a weighted pick, with its own wear seed.</summary>
        private static Stake? Draw(Profile profile, StakeBinder binder, List<(Stake stake, double weight)> candidates)
        {
            if (candidates.Count == 0) return null;
            uint seed = Hash.Mix(profile.Collection.Salt, CardUnlocks.StableHash(binder.Owner), (uint)binder.Serial++);
            double pick = (Hash.Mix(seed, 0x57A4u) / (double)uint.MaxValue) * candidates.Sum(c => c.weight);
            int i = 0;
            for (; i < candidates.Count - 1; i++) if ((pick -= candidates[i].weight) < 0) break;
            var chosen = candidates[i].stake;
            return new Stake { Code = chosen.Code, Rarity = chosen.Rarity, Grade = chosen.Grade, Slab = chosen.Slab, Seed = Hash.Mix(seed, chosen.Code) };
        }

        /// <summary>
        /// The stake for a duel: the binder's next card, fixed until that duel is played. At Orvel's Table with your card
        /// (<paramref name="yours"/>), the next in order worth 1-2× it (<see cref="AlleyEconomy.OrvelMatchMin"/>, Max). Null: an empty
        /// binder (or nothing matches yours).
        /// </summary>
        public static Stake? Next(Profile profile, string owner, AlleyContext ctx, CardCopy? yours = null)
        {
            var binder = Binder(profile, owner, ctx);
            if (yours == null) return binder.Cards.FirstOrDefault();
            int mine = profile.Collection.Value(yours);
            return binder.Cards.FirstOrDefault(s => Value(s, ctx) is var v && v >= AlleyEconomy.OrvelMatchMin * mine && v <= AlleyEconomy.OrvelMatchMax * mine);
        }

        /// <summary>A stake's value: Pricing.V at the card's commonest printing.</summary>
        public static int Value(Stake stake, AlleyContext ctx) =>
            Pricing.V(new CardCopy { Code = stake.Code, Rarity = stake.Rarity, Grade = stake.Grade, Slab = stake.Slab }, ctx.Commonest(stake.Code));

        /// <summary>
        /// A won stake as your copy: Source Ante, Paid 0 (it sells for nothing), its grade and seed kept, a new id, stamped with
        /// the stake's printing (its text derived now, from <see cref="Collection.Texts"/>; a stake without one: unstamped).
        /// </summary>
        public static CardCopy ToCopy(Stake stake, Profile profile)
        {
            var copy = new CardCopy
            {
                Code = stake.Code, Rarity = stake.Rarity, Grade = stake.Grade, Seed = stake.Seed, Slab = stake.Slab, Floor = stake.Slab,
                Source = CopySource.Ante, Paid = 0, Id = profile.Collection.NextId++,
            };
            profile.Collection.Stamp(copy, stake.Set, stake.Printed);
            return copy;
        }

        /// <summary>Copies you could stake at Orvel's Table, most valuable first: spares (no saved deck needs them), not at the Grading Desk, worth at least <paramref name="minValue"/>.</summary>
        public static List<CardCopy> Stakeable(Profile profile, Func<uint, int> neededByDecks, int minValue) =>
            profile.Collection.All.Where(c => !c.AtDesk && profile.Collection.Value(c) >= minValue && profile.Collection.Count(c.Code) - 1 >= neededByDecks(c.Code))
                .OrderByDescending(c => profile.Collection.Value(c)).ThenBy(c => c.Id).ToList();

        /// <summary>Why a duel can't start with these stakes (null: it can). The caller checks the deck's legality (the Alley's rules).</summary>
        public static string? Blocked(Profile profile, AlleyTier tier, Stake? theirs, CardCopy? yours, Func<uint, int> neededByDecks, AlleyContext ctx)
        {
            if (profile.AnteLive != null) return "A duel's stakes are already on the table.";
            if (tier == AlleyTier.Orvel)
            {
                if (yours == null) return Stakeable(profile, neededByDecks, AlleyEconomy.OrvelMinCard).Count == 0
                    ? "Orvel looks through your binder. \"Nothing I'd take.\"" : "Pick a spare card to stake.";
                if (yours.AtDesk) return "It's at the Grading Desk.";
                if (profile.Collection.Value(yours) < AlleyEconomy.OrvelMinCard) return $"Orvel takes cards worth {AlleyEconomy.OrvelMinCard:N0} DP or more.";
                if (profile.Collection.Count(yours.Code) - 1 < neededByDecks(yours.Code)) return "A saved deck needs it. Spares only.";
                if (theirs == null) return "\"Nothing in my binder matches that.\"";
            }
            int stake = StakeFor(tier);
            if (theirs != null && theirs.Flag != PennyFlag && profile.Dp < stake) return $"The stake is {stake:N0} DP; you have {profile.Dp:N0}.";
            return null;
        }

        /// <summary>
        /// Starts a duel's stakes: your DP stake leaves Dp into <see cref="AnteLive.Held"/> (no ledger line: it isn't spent
        /// yet), their stake leaves the binder, and at Orvel's Table your copy leaves the collection. An empty binder holds
        /// nothing of yours (the duel still counts as held, so leaving it is a loss). Null when <see cref="Blocked"/>. Save
        /// right after, then toss the coin.
        /// </summary>
        public static AnteLive? Hold(Profile profile, AlleyData data, int table, AlleyOpponent o, CardCopy? yours, Func<uint, int> neededByDecks, AlleyContext ctx)
        {
            ctx.Data ??= data;
            if (table < 0 || table >= data.Tables.Count) return null;
            var tier = BackAlley.TierOf(data, table);
            // Orvel's stake is a copy you own now (not one already sold, staked or on the rail): nothing moves otherwise.
            if (tier == AlleyTier.Orvel && yours != null && !profile.Collection.CopiesOf(yours.Code).Contains(yours)) return null;
            var theirs = Next(profile, o.Binder, ctx, tier == AlleyTier.Orvel ? yours : null);
            if (Blocked(profile, tier, theirs, tier == AlleyTier.Orvel ? yours : null, neededByDecks, ctx) != null) return null;
            // Nothing of yours is held against an empty binder, or against Penny's Common.
            int held = theirs != null && theirs.Flag != PennyFlag ? StakeFor(tier) : 0;
            var live = new AnteLive { Table = data.Tables[table].Id, Seat = o.Seat, Held = held, StartedAt = profile.DuelsPlayed, Theirs = theirs, Binder = o.Binder };
            if (!profile.HoldStake(live)) return null; // Blocked already refuses a second duel's stakes: nothing has moved yet
            if (theirs != null) Binder(profile, o.Binder, ctx).Cards.Remove(theirs);
            if (tier == AlleyTier.Orvel && yours != null && profile.Collection.Remove(yours)) live.Yours = yours;
            return live;
        }

        private static string Describe(Stake s, AlleyContext ctx) =>
            $"{ctx.Cards.Get(s.Code)?.Name ?? s.Code.ToString()} ({Rarities.Name(s.Rarity)}, {(s.Slab > 0 ? $"Grade {s.Slab} slab" : Grades.RawLabel(s.Grade))})";

        /// <summary>
        /// Settles the held stakes (called by <see cref="BackAlley.Record"/>): a win returns your DP and makes their card
        /// yours (a 4th copy keeps your best three, for 0 DP); a draw or a first-turn loss returns everything; a loss spends
        /// your DP to alley.stake, puts their card back on top of its binder and your card (Orvel's) on Moth's rail.
        /// </summary>
        public static AnteSettlement Settle(Profile profile, int winner, bool firstTurnLoss, AlleyContext ctx) =>
            SettleAs(profile, winner == 0 ? "win" : winner == 2 ? "draw" : firstTurnLoss ? "firstturn" : "loss", ctx);

        /// <summary>
        /// A held duel you left (conceding is a result: <see cref="BackAlley.Record"/>): settled as a loss, and counted as an
        /// alley duel lost at its seat. Also for a save loaded with <see cref="Profile.AnteLive"/> (the app closed mid-duel),
        /// with the notice in the summary. Null when nothing is held.
        /// </summary>
        public static AnteSettlement? SettleAbandoned(Profile profile, AlleyContext ctx)
        {
            var live = profile.AnteLive;
            if (live == null) return null;
            var settled = SettleAs(profile, "quit", ctx);
            var opp = profile.AlleyOpponents.FirstOrDefault(o => o.Table == live.Table && o.Seat == live.Seat);
            if (opp != null) profile.Losses[opp.Character] = profile.Losses.GetValueOrDefault(opp.Character) + 1;
            profile.AlleyDuels++;
            profile.NoteResult($"alley:{live.Table}.{live.Seat}", 1);
            var name = ctx.Data != null && ctx.Data.IndexOf(live.Table) is var ti && ti >= 0 ? ctx.Data.Tables[ti].Name : live.Table;
            if (ctx.Data != null)
                settled.Fill = BackAlley.FillFor(profile, ctx.Data, opp, settled.Railed is RailEntry railed ? BackAlley.StakeOf(railed.Copy) : live.Theirs, ctx.Cards);
            settled.Summary = $"You left {name} mid-duel. That counts as a loss." + (settled.Summary.Length > 0 ? " " + settled.Summary : "");
            return settled;
        }

        private static AnteSettlement SettleAs(Profile profile, string outcome, AlleyContext ctx)
        {
            var live = profile.AnteLive;
            var result = new AnteSettlement { Outcome = live == null ? "none" : outcome };
            if (live == null) return result;
            // Held DP comes back into Dp first (and the stakes leave the table); a loss then spends it, so the ledger says
            // exactly what was lost.
            profile.ReturnStake(live);
            bool staked = live.Theirs != null;
            var parts = new List<string>();
            bool lost = outcome is "loss" or "quit";
            if (lost)
            {
                // A No Limits stake lost writes its own kind (NO-LIMITS-ECONOMY 6.2); every other table alley.stake.
                int ti = ctx.Data?.IndexOf(live.Table) ?? -1;
                bool noLimits = ti >= 0 && ctx.Data!.Tables[ti].Tier == AlleyTier.NoLimits;
                profile.Spend(noLimits ? Ledger.NoLimitsStake : Ledger.AlleyStake, live.Held);
                result.Lost = live.Held;
                if (live.Held > 0) parts.Add($"-{live.Held:N0} DP stake");
            }
            else
            {
                result.Returned = live.Held;
                if (live.Held > 0) parts.Add($"{live.Held:N0} DP stake back");
            }
            // Your copy (Orvel's) comes home before their card goes in, so the 4th-copy rule sees every copy: the same copy
            // (id, grade, seed, slab) as it left. A loss puts it on Moth's rail instead (below).
            if (live.Yours != null && !lost) profile.Collection.Insert(live.Yours);
            if (live.Theirs != null)
            {
                if (outcome == "win")
                {
                    var copy = ToCopy(live.Theirs, profile);
                    var added = profile.Collection.Take(copy);
                    result.Won = copy;
                    result.Added = added;
                    parts.Add(added.Kept ? $"{Describe(live.Theirs, ctx)} is yours" : $"{Describe(live.Theirs, ctx)} won (a 4th copy: you keep your best three)");
                }
                else
                {
                    // A save from before AnteLive kept its binder: the seat's opponent says whose it was.
                    var owner = live.Binder.Length > 0 ? live.Binder
                        : profile.AlleyOpponents.FirstOrDefault(o => o.Table == live.Table && o.Seat == live.Seat)?.Binder ?? "";
                    if (owner.Length > 0) Binder(profile, owner, ctx).Cards.Insert(0, live.Theirs);
                }
            }
            if (live.Yours != null && lost)
            {
                // The rail's clock starts after this duel, which is counted (NoteResult) right after the stakes settle: the
                // card hangs for RailDuels more duels.
                var entry = new RailEntry
                {
                    Copy = live.Yours, Price = AlleyEconomy.RailPrice(profile.Collection.Value(live.Yours)), ExpiresAt = profile.DuelsPlayed + 1 + AlleyEconomy.RailDuels,
                };
                profile.Rail.Add(entry);
                result.Railed = entry;
                result.Events.Add("rail");
                parts.Add($"your {ctx.Cards.Get(live.Yours.Code)?.Name ?? "card"} goes on Moth's rail ({entry.Price:N0} DP to claim)");
            }
            // Penny's Common holds nothing of yours: losing to her loses no stake (no antelose, no first-turn refund).
            bool yoursHeld = live.Held > 0 || live.Yours != null;
            if (staked && outcome == "win") { profile.AnteWins++; result.Events.Add("antewin"); }
            if (staked && lost && yoursHeld) { profile.AnteLosses++; result.Events.Add("antelose"); }
            if (staked && yoursHeld && outcome == "firstturn") { result.Events.Add("firstturn"); parts.Add("lost before your first turn: the House doesn't charge for that"); }
            result.Summary = string.Join(" · ", parts);
            return result;
        }

        /// <summary>Moth sells the rail's expired cards (any duel counts): their names (codes without <paramref name="nameOf"/>). Call after any duel and at load.</summary>
        public static List<string> TickRail(Profile profile, Func<uint, string>? nameOf = null)
        {
            var gone = profile.Rail.Where(r => profile.DuelsPlayed >= r.ExpiresAt).ToList();
            foreach (var r in gone) profile.Rail.Remove(r);
            return gone.Select(r => nameOf?.Invoke(r.Copy.Code) ?? r.Copy.Code.ToString()).ToList();
        }

        /// <summary>Duels left before a rail entry is sold.</summary>
        public static int DuelsLeft(Profile profile, RailEntry entry) => Math.Max(0, entry.ExpiresAt - profile.DuelsPlayed);

        public static string? BuyBackBlocked(Profile profile, RailEntry entry)
        {
            if (!profile.Rail.Contains(entry) || profile.DuelsPlayed >= entry.ExpiresAt) return "Moth sold it.";
            if (profile.Dp < entry.Price) return $"The tag's {entry.Price:N0} DP; you have {profile.Dp:N0}.";
            if (!profile.Collection.WouldKeep(entry.Copy)) return "You have 3 copies worth more already.";
            return null;
        }

        /// <summary>Claims a card back at its tag: the same copy (id, grade, seed, slab), the price added to its Paid. A copy a 4th displaces pays refund.alley.</summary>
        public static bool BuyBack(Profile profile, RailEntry entry)
        {
            if (BuyBackBlocked(profile, entry) != null || !profile.Spend(Ledger.AlleyRail, entry.Price)) return false;
            profile.Rail.Remove(entry);
            entry.Copy.Paid += entry.Price;
            var added = profile.Collection.Take(entry.Copy);
            profile.Earn(Ledger.AlleyRefund, added.Dp);
            return true;
        }
    }
}
