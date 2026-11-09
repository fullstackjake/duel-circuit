using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// Which real printing a new copy is (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 4.2): every printing is dated by
    /// <see cref="PrintingIndex.DateOf"/> (its set name, or a reprint's own date), and a copy's text follows from that date
    /// (<see cref="Collection.Stamp(CardCopy, string, string)"/>). Nothing here touches a shared <see cref="Random"/>: a
    /// printing picked from a seed is <see cref="Hash.Mix"/> of that seed, so packs, listings and binders never reroll.
    /// No printing is ever invented: a card with none at the asked rarity gets none (null).
    /// </summary>
    public static class CopyPrints
    {
        /// <summary>The hash salt of <see cref="BySeed"/> (changing it rerolls printings, not listings).</summary>
        public const uint UsedPrintingSalt = 0x5E7;

        /// <summary>A date's year ("2002-06-26" -> 2002), 0 when it has none.</summary>
        public static int YearOf(string date) => date.Length >= 4 && int.TryParse(date.Substring(0, 4), out var y) ? y : 0;

        /// <summary>A card's printings at a rarity with their dates, oldest first (then by set code): those with a known date only.</summary>
        public static List<(Printing printing, string date)> Dated(uint code, Rarity r, PrintingIndex p, ICardDataProvider? c) =>
            p.Of(code, c?.Get(code)?.Alias ?? 0).Where(x => x.Rarity == r).Select(x => (x, date: p.DateOf(x))).Where(x => x.date.Length >= 7)
                .OrderBy(x => x.date, StringComparer.Ordinal).ThenBy(x => x.x.SetCode, StringComparer.Ordinal).ToList();

        /// <summary>
        /// The card's printing in a set (its code prefix, "MRD") at the pulled rarity: the earliest dated one, then the first
        /// by set code (MRD-069 before MRD-E069; a reprint filed under the set's code dates later). Null when the set has
        /// none at that rarity. A pack's pull is dated as the pack's set, whatever this printing's own date.
        /// </summary>
        public static Printing? InSet(uint code, Rarity r, string setPrefix, PrintingIndex p, ICardDataProvider? c)
        {
            foreach (var (printing, _) in Dated(code, r, p, c))
                if (PrintingIndex.SetPrefix(printing.SetCode) == setPrefix) return printing;
            // A printing of the set whose date is unknown: still the set's.
            return p.Of(code, c?.Get(code)?.Alias ?? 0).Where(x => x.Rarity == r && PrintingIndex.SetPrefix(x.SetCode) == setPrefix)
                .OrderBy(x => x.SetCode, StringComparer.Ordinal).Select(x => (Printing?)x).FirstOrDefault();
        }

        /// <summary>The newest printing at that rarity dated in or before <paramref name="maxYear"/> (the latest date, then the first set code), or null.</summary>
        public static Printing? Newest(uint code, Rarity r, int maxYear, PrintingIndex p, ICardDataProvider? c)
        {
            Printing? best = null;
            string bestDate = "";
            foreach (var (printing, date) in Dated(code, r, p, c))
                if (YearOf(date) <= maxYear && string.CompareOrdinal(date, bestDate) > 0) { best = printing; bestDate = date; }
            return best;
        }

        /// <summary>
        /// The newest printing at any rarity dated in or before <paramref name="maxYear"/> (else of any year), for a copy whose
        /// rarity is the game's, not a printing's: a structure deck's cards all come Common. Null when it has no dated printing.
        /// </summary>
        public static Printing? NewestOfAny(uint code, int maxYear, PrintingIndex p, ICardDataProvider? c)
        {
            Printing? best = null, any = null;
            string bestDate = "", anyDate = "";
            foreach (var x in p.Of(code, c?.Get(code)?.Alias ?? 0))
            {
                var date = p.DateOf(x);
                if (date.Length < 7) continue;
                bool later(string d) => string.CompareOrdinal(date, d) > 0 || date == d && best != null && string.CompareOrdinal(x.SetCode, best.Value.SetCode) < 0;
                if (YearOf(date) <= maxYear && (best == null || later(bestDate))) { best = x; bestDate = date; }
                if (any == null || string.CompareOrdinal(date, anyDate) > 0) { any = x; anyDate = date; }
            }
            return best ?? any;
        }

        /// <summary>
        /// One of the printings at that rarity dated in or before <paramref name="maxYear"/>, picked by
        /// Hash.Mix(<paramref name="seed"/>, <see cref="UsedPrintingSalt"/>) over them oldest first; null when there's none.
        /// </summary>
        public static Printing? BySeed(uint code, Rarity r, int maxYear, uint seed, PrintingIndex p, ICardDataProvider? c)
        {
            var list = Dated(code, r, p, c).Where(x => YearOf(x.date) <= maxYear).ToList();
            return list.Count == 0 ? (Printing?)null : list[(int)(Hash.Mix(seed, UsedPrintingSalt) % (uint)list.Count)].printing;
        }

        /// <summary>The earliest printing at that rarity (any rarity when <paramref name="r"/> is null) dated by <paramref name="maxYm"/> ("yyyy-mm"; "" for any), or null.</summary>
        public static Printing? Earliest(uint code, Rarity? r, string maxYm, PrintingIndex p, ICardDataProvider? c)
        {
            Printing? best = null;
            string bestDate = "";
            foreach (var x in p.Of(code, c?.Get(code)?.Alias ?? 0))
            {
                if (r is Rarity want && x.Rarity != want) continue;
                var date = p.DateOf(x);
                if (date.Length < 7 || maxYm.Length >= 7 && string.CompareOrdinal(date.Substring(0, 7), maxYm) > 0) continue;
                if (best == null || string.CompareOrdinal(date, bestDate) < 0 || date == bestDate && string.CompareOrdinal(x.SetCode, best.Value.SetCode) < 0)
                {
                    best = x;
                    bestDate = date;
                }
            }
            return best;
        }

        /// <summary>
        /// A copy bought new (the shop, the Card Trader, recipe packs, Make it yours, a structure deck without a `#set`): the
        /// newest printing at that rarity by the shop's year, else the newest of any year (a card the shop sells before its
        /// first printing's year, outside era-limited play, comes as printed now). Null when it has no dated printing.
        /// </summary>
        public static Printing? Shop(uint code, Rarity r, int shopYear, PrintingIndex p, ICardDataProvider? c) =>
            Newest(code, r, shopYear, p, c) ?? Newest(code, r, 9999, p, c);

        /// <summary>
        /// The newest printing at that rarity that carries the current text (special orders: OrdersGiveOriginals is off, D5),
        /// or null when every printing at that rarity carries an original (the copy is then current text, printing not recorded).
        /// </summary>
        public static Printing? Current(uint code, Rarity r, CardTexts texts, PrintingIndex p, ICardDataProvider? c)
        {
            Printing? best = null;
            string bestDate = "";
            foreach (var (printing, date) in Dated(code, r, p, c))
                if (texts.PrintedText(code, date) == 0 && string.CompareOrdinal(date, bestDate) > 0) { best = printing; bestDate = date; }
            return best;
        }

        /// <summary>Whether special orders may give original texts (D5: no, so originals are found, not bought).</summary>
        public const bool OrdersGiveOriginals = false;
    }
}
