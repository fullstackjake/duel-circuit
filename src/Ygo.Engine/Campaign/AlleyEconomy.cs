using System;
using Ygo.Engine.Data;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// The Back Alley's numbers, in one place (docs/dev/ALLEY-SPEC-2026-11-01.md section 15; the Economy Designer's
    /// docs/dev/ALLEY-ECONOMY-2026-11-01.md measured them). Every phase's knobs are here already, so later phases add none, and
    /// the harness can swap values before it measures. The owner accepted these defaults on 2026-11-02. Hard rules no knob
    /// may break: DP back for a copy is at most half of what went into it; Moth buys at the Sell desk's price; stakes are
    /// held in the save from the duel's start; alley duels pay at most 0 DP all-in; no loop is DP-positive.
    /// </summary>
    public static class AlleyEconomy
    {
        // ---- Stakes ----
        /// <summary>Your DP stake, held from the duel's start: back on a win, a draw or a first-turn loss; lost on a loss or a quit.</summary>
        public static int StreetStake = 300, DenStake = 1000, OrvelStake = 3000;
        /// <summary>At Orvel's Table you also stake one spare card worth at least this.</summary>
        public static int OrvelMinCard = 1000;
        /// <summary>The most a CPU's card is worth at Street (Dumpster Row) and Den (The Basement Game, The Black Market, Nyx's Table) tables.</summary>
        public static int StreetCap = 1000, DenCap = 5000;
        /// <summary>The CPU's card is worth at least this times your DP stake, so a bet looks fair.</summary>
        public static double StakeFloor = 1.0;
        /// <summary>Orvel's slab is worth between these times your card.</summary>
        public static double OrvelMatchMin = 1.0, OrvelMatchMax = 2.0;
        /// <summary>A binder draws the CPU's splashed contraband this many times as often as its other cards.</summary>
        public static double ContrabandWeight = 3;

        /// <summary>How a Street binder's copies come: Raw only, often worn (grade, slab, weight).</summary>
        public static (int grade, bool slab, double weight)[] StreetGrades = { (4, false, 0.15), (6, false, 0.25), (8, false, 0.40), (9, false, 0.20) };
        /// <summary>A Den binder's: Raw 8-9 and slabs 8-10 (the probe's mix).</summary>
        public static (int grade, bool slab, double weight)[] DenGrades = { (8, false, 0.25), (9, false, 0.25), (8, true, 0.20), (9, true, 0.20), (10, true, 0.10) };
        /// <summary>Orvel's: slabs 9-10, at <see cref="OrvelMinRarity"/> and up.</summary>
        public static (int grade, bool slab, double weight)[] OrvelGrades = { (9, true, 0.5), (10, true, 0.5) };
        public static Rarity OrvelMinRarity = Rarity.SuperRare;

        // ---- Binders ----
        /// <summary>Different cards in a CPU's binder, and one stake back per this many alley duels (brackets excluded).</summary>
        public static int BinderSize = 12, RefillEvery = 3;

        // ---- Moth's rail (Orvel's Table) ----
        /// <summary>A lost card hangs this many duels (any duel counts), at this times its value (added to its Paid on a buy-back).</summary>
        public static int RailDuels = 5;
        public static double RailMarkup = 1.5;

        // ---- Duels ----
        /// <summary>DP an alley duel pays (win, loss or draw): none. The CPU's card is the prize; EXP at the Library's rate, level-ups in tokens.</summary>
        public static int DuelDp = 0;
        /// <summary>Card Tokens for a table's first clear (night 0's fourth win).</summary>
        public static int ClearTokens = 1;
        /// <summary>The headliner who isn't the night's closer takes one of seats 1-3 with this chance; Penny takes seat 1 at Dumpster Row with this one.</summary>
        public static double WildcardChance = 1.0 / 3, PennyChance = 0.2;
        /// <summary>The alley's turn cap (turn 41 never starts: the higher LP wins), and the turn the board's counter starts reading "Turn N / 40".</summary>
        public static int AlleyTurns = TurnCap.AlleyTurns, TurnCounterFrom = 30;

        // ---- Moth's van ----
        /// <summary>The contraband shelf: this many slabs (9-10, <see cref="ShelfMinRarity"/> and up, worth at least <see cref="ShelfMinValue"/>), new every <see cref="ShelfEvery"/> alley duels.</summary>
        public static int ShelfSize = 6, ShelfEvery = 10, ShelfMinValue = 3500;
        public static Rarity ShelfMinRarity = Rarity.SecretRare;
        /// <summary>Shelf price: this times the value, rounded up to 500 (<see cref="ShelfPrice"/>).</summary>
        public static double ShelfMarkup = 1.5;
        /// <summary>Moth's used contraband: listings, slabs up to this grade, new every this many duels (the used market's period).</summary>
        public static int VanUsedSize = 18, VanUsedSlabMax = 8, VanUsedEvery = 5;

        // ---- The Contraband Pack (bracket prizes only) ----
        /// <summary>Cards a pack: four Raw and a sealed slab; the slab is a 9 with this chance (else a 10), at <see cref="PackSlabMinRarity"/> and up.</summary>
        public static int PackSize = 5;
        public static double PackSlabNine = 0.75;
        public static Rarity PackSlabMinRarity = Rarity.SuperRare;

        // ---- Alley brackets ----
        /// <summary>Alley Brawl: entry, placement pay and Contraband Packs (winner, finalist, semi-finals, quarter-finals).</summary>
        public static int BrawlEntry = 1000;
        public static int[] BrawlPay = { 900, 650, 400, 150 }, BrawlPacks = { 2, 1, 0, 0 };
        /// <summary>The Midnight Open: entry, pay and packs (winner ... round of 16), and Card Tokens on the first title only.</summary>
        public static int OpenEntry = 2500, OpenTokens = 1;
        public static int[] OpenPay = { 1500, 1000, 600, 300, 100 }, OpenPacks = { 4, 2, 1, 0, 0 };

        // ---- No Limits (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 13; NO-LIMITS-ECONOMY section 7: all of NL-1's and NL-2's knobs, so NL-2 adds none) ----
        /// <summary>Your DP stake at No Limits, held (back on a win, a draw or the guard; lost on a loss or a quit). No card of yours.</summary>
        public static int NoLimitsStake = 2500;
        /// <summary>The most a pilot's card is worth; the floor is <see cref="StakeFloor"/> × the stake.</summary>
        public static int NoLimitsCap = 10000;
        /// <summary>A pilot's binder: Raw 8-9 and slabs 9-10 (the out-of-towners keep their cards sleeved and slabbed).</summary>
        public static (int grade, bool slab, double weight)[] NoLimitsGrades = { (8, false, 0.15), (9, false, 0.15), (9, true, 0.35), (10, true, 0.35) };
        /// <summary>When a night is drawn, each seated pilot's missing lead cards go back into its binder (<see cref="Ante.Restock"/>).</summary>
        public static bool NoLimitsRestock = true;
        /// <summary>The first-turn guard at No Limits: a loss with this many turns or fewer while the pilot went first gives the stake back (2 only if wins by turn 2 exceed 10%).</summary>
        public static int NoLimitsGuardTurns = 1;
        /// <summary>The No Limits Invitational (NL-2): entry, pay and No Limits Packs (winner ... round of 16), and Card Tokens on the first title only.</summary>
        public static int InvitationalEntry = 3000, InvitationalTokens = 2;
        public static int[] InvitationalPay = { 1500, 1000, 600, 300, 100 }, InvitationalPacks = { 4, 3, 2, 1, 0 };
        /// <summary>Moth's shelf once the wall is open (NL-2): this many of its <see cref="ShelfSize"/> slots are drawn from the ready builds' `stakes:` cards, at this rarity and value or more.</summary>
        public static int ShelfRestoredSlots = 2, ShelfRestoredMinValue = 1500;
        public static Rarity ShelfRestoredMinRarity = Rarity.UltraRare;

        /// <summary>
        /// A shelf slab's price: <see cref="ShelfMarkup"/> times its value, rounded up to 500 (NL-2: never under 1.5 V, so a slab
        /// sells back at most V/4 of it, a sixth). Rounding to the nearest 500 let a few slabs under it: a Quarter Century slab 10
        /// (5,400: 8,100 to 8,000, 16.9% back) and the restored slots' cheap slabs (a slab 9 at 1,800: 2,700 to 2,500, 18%).
        /// </summary>
        public static int ShelfPrice(int value) => Math.Max(500, (int)Math.Ceiling(Math.Round(value * ShelfMarkup, 6) / 500.0) * 500);

        /// <summary>A rail tag: <see cref="RailMarkup"/> times the copy's value, to the nearest 5.</summary>
        public static int RailPrice(int value) => Pricing.Round5(value * RailMarkup);
    }
}
