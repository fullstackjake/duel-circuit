using System;
using System.Collections.Generic;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>A level reached: what it paid.</summary>
    public readonly struct LevelUp
    {
        public readonly int Level, Dp, Tokens;
        public LevelUp(int level, int dp, int tokens) { Level = level; Dp = dp; Tokens = tokens; }
    }

    /// <summary>
    /// Duelist level: every campaign duel earns EXP (more for wins, harder circuits, and first wins; a little for
    /// losses), and each level pays DP and, every second level, a Card Token, with an extra token every fifth level.
    /// A token trades for one copy of any card at the Card Trader.
    /// </summary>
    public static class Leveling
    {
        public const int MaxLevel = 99, CircuitClearExp = 150;

        /// <summary>EXP from level <paramref name="level"/> to the next.</summary>
        public static int ExpToNext(int level) => 150 + 50 * (level - 1);

        public static int LevelFor(int exp)
        {
            int level = 1;
            while (level < MaxLevel && exp >= ExpToNext(level)) exp -= ExpToNext(level++);
            return level;
        }

        /// <summary>EXP into the current level, and the EXP that level needs.</summary>
        public static (int into, int needed) Progress(int exp)
        {
            int level = 1;
            while (level < MaxLevel && exp >= ExpToNext(level)) exp -= ExpToNext(level++);
            return (exp, ExpToNext(level));
        }

        /// <summary>EXP for one campaign duel. <paramref name="winner"/>: 0 you, 1 them, 2 draw.</summary>
        public static int DuelExp(int circuit, int winner, bool firstWin) =>
            winner == 0 ? 60 + 15 * circuit + (firstWin ? 40 : 0) : 20 + 5 * circuit;

        /// <summary>A level-up's line: "Level 12! +400 DP, +1 Card Token" (no DP part when it paid none).</summary>
        public static string Describe(LevelUp up) =>
            $"Level {up.Level}!" + (up.Dp > 0 ? $" +{up.Dp} DP" : "") + (up.Tokens > 0 ? $"{(up.Dp > 0 ? "," : "")} +{up.Tokens} Card Token{(up.Tokens > 1 ? "s" : "")}" : "");

        public static LevelUp RewardFor(int level) =>
            new LevelUp(level, 100 + 25 * level, (level % 2 == 0 ? 1 : 0) + (level % 5 == 0 ? 1 : 0));

        /// <summary>
        /// Adds EXP to a profile and pays out every level it reaches. <paramref name="payDp"/> false (post-game duels you've
        /// already won, and Locals: PROOF-2026-11-01): the levels pay Card Tokens only, at least one each, never DP, so
        /// farming them can't beat the 300 DP a duel cap or make a bracket pay for itself.
        /// </summary>
        public static List<LevelUp> AddExp(Profile profile, int exp, bool payDp = true)
        {
            var ups = new List<LevelUp>();
            int before = LevelFor(profile.Exp);
            profile.Exp += exp;
            for (int level = before + 1; level <= LevelFor(profile.Exp); level++)
            {
                var up = RewardFor(level);
                if (!payDp) up = new LevelUp(level, 0, Math.Max(1, up.Tokens));
                profile.Earn(Ledger.LevelUp, up.Dp);
                profile.Tokens += up.Tokens;
                ups.Add(up);
            }
            return ups;
        }
    }

    /// <summary>
    /// The Card Trader: Card Tokens for one copy of any card of any era (within the shop's era in the historic
    /// challenge; within a cup's pool at the Library's trader, whatever the mode). A card costs by its least rare printing:
    /// 1 token if it was ever a Common or Rare, 2 if a Super or Ultra Rare at best, 3 if it was only ever a Secret Rare or
    /// rarer (2 if never printed in the TCG).
    /// </summary>
    public static class CardTrader
    {
        /// <summary>The Card Tokens one copy costs.</summary>
        public static int Cost(uint baseCode, PrintingIndex? printings, ICardDataProvider cards)
        {
            var info = cards.Get(baseCode);
            if (printings == null || info == null) return 1;
            if (printings.FirstYear(baseCode, info.Alias) == 0) return 2;
            var rarity = printings.Commonest(baseCode, info.Alias);
            return rarity <= Rarity.NormalParallelRare ? 1 : rarity <= Rarity.PlatinumRare ? 2 : 3;
        }

        /// <summary>
        /// Why a card can't be traded for right now, or null when it can. A <paramref name="pool"/> ("yyyy-MM", a Library
        /// cup's) takes only cards first printed in the TCG by then, in place of the historic challenge's shop year.
        /// </summary>
        public static string? Blocked(uint baseCode, Profile profile, int shopYear, PrintingIndex? printings, ICardDataProvider cards, string pool = "")
        {
            var info = cards.Get(baseCode);
            if (info == null) return "unknown card";
            if (profile.Tokens <= 0) return "you have no Card Tokens";
            if (profile.Collection.Owned(baseCode) >= Collection.MaxCopies) return $"you already have {Collection.MaxCopies}";
            int cost = Cost(baseCode, printings, cards);
            if (profile.Tokens < cost) return $"it costs {cost} Card Tokens and you have {profile.Tokens}";
            if (pool.Length > 0 && printings != null)
            {
                if (!InPool(baseCode, pool, printings, cards))
                    return printings.FirstYear(baseCode, info.Alias) == 0 ? "never printed in the TCG"
                        : $"first printed {DeckRules.MonthYear(printings.FirstDate(baseCode, info.Alias))}; this cup's cards end {DeckRules.MonthYear(pool)}";
            }
            else if (profile.EraLimited && printings != null)
            {
                int year = printings.FirstYear(baseCode, info.Alias);
                if (year == 0) return "never printed in the TCG";
                if (year > shopYear) return $"first printed in {year}; the shop's era is up to {shopYear}";
            }
            return null;
        }

        /// <summary>Whether a Library trader with this pool offers the card: first printed in the TCG by the pool's month.</summary>
        public static bool InPool(uint baseCode, string pool, PrintingIndex printings, ICardDataProvider cards) =>
            printings.FirstYear(baseCode, cards.Get(baseCode)?.Alias ?? 0) > 0 && DeckRules.Released(baseCode, pool, printings, cards);

        /// <summary>Spends its cost in tokens on one copy of the card, at its least rare printing. False if it can't be traded.</summary>
        public static bool Trade(uint baseCode, Profile profile, int shopYear, PrintingIndex? printings, ICardDataProvider cards, string pool = "")
        {
            if (Blocked(baseCode, profile, shopYear, printings, cards, pool) != null) return false;
            var info = cards.Get(baseCode)!;
            int cost = Cost(baseCode, printings, cards);
            profile.Tokens -= cost;
            profile.TokensSpent += cost;
            var rarity = printings?.Commonest(baseCode, info.Alias) ?? Rarity.Common;
            var print = printings != null ? CopyPrints.Shop(baseCode, rarity, shopYear, printings, cards) : null;
            profile.Collection.Add(baseCode, rarity, 1, CopySource.Shop, 0, print?.SetCode ?? "", print is Printing p ? printings!.DateOf(p) : "");
            return true;
        }
    }

    /// <summary>
    /// What DP buys besides packs: a Card Token (1,000 DP, then 500 more each time). Ban Passes are earned, not sold (see
    /// <see cref="BanPasses"/>); saves from when they were sold get their DP back (<see cref="BoughtPassRefund"/>).
    /// </summary>
    public static class DpServices
    {
        public static int TokenPrice(Profile profile) => 1000 + 500 * profile.TokensBought;

        /// <summary>What <paramref name="bought"/> Ban Pass slots cost when they were sold (4,000 DP, then 8,000, 12,000).</summary>
        public static int BoughtPassRefund(int bought) => 4000 * bought * (bought + 1) / 2;

        public static bool BuyToken(Profile profile)
        {
            int price = TokenPrice(profile);
            if (!profile.Spend(Ledger.Tokens, price)) return false;
            profile.Tokens++;
            profile.TokensBought++;
            return true;
        }
    }
}
