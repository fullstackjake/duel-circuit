using System;
using System.Collections.Generic;
using System.Linq;

namespace Ygo.Engine.DeckBuilding
{
    public enum DeckSection { Main, Extra, Side }

    public sealed class DeckProblem
    {
        public string Message = "";
        /// <summary>The card the problem is about, or 0 for deck-wide problems (e.g. size).</summary>
        public uint Code;
    }

    /// <summary>Deck legality and summary numbers.</summary>
    public static class DeckRules
    {
        public const int MainMin = 40, MainMax = 60, ExtraMax = 15, SideMax = 15;

        /// <summary>Which section a card goes to when added: Extra Deck monsters to the Extra Deck, others to the Main Deck.</summary>
        public static DeckSection HomeSection(CardInfo info) => info.IsExtraDeckMonster ? DeckSection.Extra : DeckSection.Main;

        public static List<DeckProblem> Validate(Deck deck, Banlist banlist, ICardDataProvider cards)
        {
            var problems = new List<DeckProblem>();
            if (deck.Main.Count < MainMin) problems.Add(new DeckProblem { Message = $"Main Deck needs at least {MainMin} cards ({deck.Main.Count} now)." });
            if (deck.Main.Count > MainMax) problems.Add(new DeckProblem { Message = $"Main Deck can have at most {MainMax} cards ({deck.Main.Count} now)." });
            if (deck.Extra.Count > ExtraMax) problems.Add(new DeckProblem { Message = $"Extra Deck can have at most {ExtraMax} cards ({deck.Extra.Count} now)." });
            if (deck.Side.Count > SideMax) problems.Add(new DeckProblem { Message = $"Side Deck can have at most {SideMax} cards ({deck.Side.Count} now)." });

            foreach (var code in deck.Main.Where(c => cards.Get(c)?.IsExtraDeckMonster == true).Distinct())
                problems.Add(new DeckProblem { Code = code, Message = $"{Name(code, cards)} belongs in the Extra Deck." });
            foreach (var code in deck.Extra.Where(c => cards.Get(c) is CardInfo i && !i.IsExtraDeckMonster).Distinct())
                problems.Add(new DeckProblem { Code = code, Message = $"{Name(code, cards)} can't go in the Extra Deck." });
            foreach (var code in deck.Main.Concat(deck.Extra).Concat(deck.Side).Where(c => cards.Get(c) == null).Distinct())
                problems.Add(new DeckProblem { Code = code, Message = $"Unknown card id {code}." });

            // Copies are counted across Main + Extra + Side, by base card (alternate artworks are the same card).
            var counts = deck.Main.Concat(deck.Extra).Concat(deck.Side)
                .GroupBy(c => Banlist.BaseCode(c, cards))
                .Select(g => (code: g.Key, count: g.Count()));
            foreach (var (code, count) in counts)
            {
                var allowed = Math.Min(3, banlist.Allowed(code, cards));
                if (count <= allowed) continue;
                var why = allowed == 0
                    ? banlist.Whitelist ? $"not legal in {banlist.Name}" : $"Forbidden in {banlist.Name}"
                    : allowed < 3 ? $"limited to {allowed} in {banlist.Name}" : "3 copies at most";
                problems.Add(new DeckProblem { Code = code, Message = $"{Name(code, cards)}: {count} cop{(count == 1 ? "y" : "ies")}, {why}." });
            }
            return problems;
        }

        /// <summary>
        /// When a ban list's format ended: the month the next TCG list took effect ("yyyy-mm"), or "" for the newest
        /// list (or an undated one). A format runs from its list until the next, so sets released in between belong to it.
        /// </summary>
        public static string FormatEnd(Banlist banlist, IEnumerable<Banlist> all)
        {
            var date = banlist.Date;
            if (date.Length == 0) return "";
            return all.Where(l => l.Name.Contains("TCG") && !l.Whitelist).Select(l => l.Date)
                .Where(d => d.Length > 0 && string.CompareOrdinal(d, date) > 0).OrderBy(d => d, StringComparer.Ordinal).FirstOrDefault() ?? "";
        }

        /// <summary>
        /// Cards that weren't out yet in a ban list's format: their first TCG printing came after <paramref name="formatEnd"/>
        /// (see <see cref="FormatEnd"/>). Cards with no TCG printing on record (OCG-only, anime) aren't judged. Nothing for
        /// the newest list.
        /// </summary>
        public static List<DeckProblem> Releases(Deck deck, Banlist banlist, string formatEnd, Data.PrintingIndex? printings, ICardDataProvider cards)
        {
            var problems = new List<DeckProblem>();
            if (printings == null || formatEnd.Length == 0) return problems;
            foreach (var code in deck.Main.Concat(deck.Extra).Concat(deck.Side).Select(c => Banlist.BaseCode(c, cards)).Distinct())
            {
                if (Released(code, formatEnd, printings, cards)) continue;
                var first = printings.FirstDate(code, cards.Get(code)?.Alias ?? 0);
                problems.Add(new DeckProblem { Code = code, Message = $"{Name(code, cards)}: not released yet in the {banlist.Name} format (first printed {MonthYear(first)})." });
            }
            return problems;
        }

        /// <summary>Whether a card was out by the end of a format ("yyyy-MM"; true when either date isn't known).</summary>
        public static bool Released(uint code, string formatEnd, Data.PrintingIndex? printings, ICardDataProvider cards)
        {
            if (printings == null || formatEnd.Length == 0) return true;
            var first = printings.FirstDate(code, cards.Get(code)?.Alias ?? 0);
            return first.Length < 7 || string.CompareOrdinal(first.Substring(0, 7), formatEnd) <= 0;
        }

        /// <summary>"May 2025" for "2025-05" (or a full date); the year alone when the month isn't known.</summary>
        public static string MonthYear(string date)
        {
            var months = new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
            if (date.Length < 7) return date;
            return int.TryParse(date.Substring(5, 2), out var m) && m >= 1 && m <= 12 ? $"{months[m - 1]} {date.Substring(0, 4)}" : date.Substring(0, 4);
        }

        /// <summary>"Monster 19 · Spell 13 · Trap 8" for a list of cards.</summary>
        public static string Breakdown(IEnumerable<uint> codes, ICardDataProvider cards)
        {
            int monsters = 0, spells = 0, traps = 0;
            foreach (var code in codes)
            {
                var type = cards.Get(code)?.Type ?? 0;
                if ((type & CardType.Monster) != 0) monsters++;
                else if ((type & CardType.Spell) != 0) spells++;
                else if ((type & CardType.Trap) != 0) traps++;
            }
            return $"Monster {monsters} · Spell {spells} · Trap {traps}";
        }

        /// <summary>Extra Deck breakdown: "Fusion 0 · Synchro 5 · Xyz 3 · Link 1".</summary>
        public static string ExtraBreakdown(IEnumerable<uint> codes, ICardDataProvider cards)
        {
            int fusion = 0, synchro = 0, xyz = 0, link = 0;
            foreach (var code in codes)
            {
                var type = cards.Get(code)?.Type ?? 0;
                if ((type & CardType.Fusion) != 0) fusion++;
                else if ((type & CardType.Synchro) != 0) synchro++;
                else if ((type & CardType.Xyz) != 0) xyz++;
                else if ((type & CardType.Link) != 0) link++;
            }
            return $"Fusion {fusion} · Synchro {synchro} · Xyz {xyz} · Link {link}";
        }

        /// <summary>Cards grouped by kind, then Level (high first), then name: the usual way decks are read.</summary>
        public static List<uint> Sorted(IEnumerable<uint> codes, ICardDataProvider cards) =>
            codes.OrderBy(c => CardSearch.KindOrder(cards.Get(c)?.Type ?? 0))
                .ThenByDescending(c => cards.Get(c)?.Level ?? 0)
                .ThenBy(c => cards.Get(c)?.Name ?? "")
                .ThenBy(c => c)
                .ToList();

        private static string Name(uint code, ICardDataProvider cards) => cards.Get(code)?.Name ?? $"#{code}";
    }

    /// <summary>Exact opening-hand probabilities (hypergeometric).</summary>
    public static class OpeningOdds
    {
        /// <summary>Chance a hand of <paramref name="handSize"/> from a <paramref name="deckSize"/>-card deck holds at least one of <paramref name="hits"/> cards.</summary>
        public static double AtLeastOne(int deckSize, int hits, int handSize)
        {
            if (deckSize <= 0 || hits <= 0) return 0;
            if (hits >= deckSize || handSize > deckSize - hits) return 1;
            // P(none) = C(deck - hits, hand) / C(deck, hand), computed as a running product to stay exact enough.
            double none = 1;
            for (int i = 0; i < handSize; i++) none *= (double)(deckSize - hits - i) / (deckSize - i);
            return 1 - none;
        }
    }
}
