using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Campaign;

namespace Ygo.Engine.DeckBuilding
{
    /// <summary>
    /// A table's texts date (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md section 5.1): an original text plays as printed where
    /// its errata came after <see cref="AsOf"/>, the same "released by" rule the card pool uses. <see cref="Printed"/>
    /// tables (the Back Alley, free duel under "No limits") play every original as printed; a <see cref="Goat"/> table
    /// rules an original copy as its 2005 GOAT entry.
    /// </summary>
    public readonly struct TextRules
    {
        /// <summary>A table whose texts date falls in this window is a Goat table (the Goat Cup, its Locals, Every Era round 1).</summary>
        public const string GoatFrom = "2005-04", GoatUntil = "2005-09";
        /// <summary>The texts date of the newest list: every errata is in force.</summary>
        public const string Latest = "9999-12";

        /// <summary>"yyyy-mm": the last month whose texts the table plays.</summary>
        public readonly string AsOf;
        public readonly bool Printed, Goat;

        public TextRules(string asOf, bool printed, bool goat)
        {
            AsOf = asOf;
            Printed = printed;
            Goat = goat;
        }

        /// <summary>Today's texts: every original plays its current text.</summary>
        public static TextRules Today => new TextRules(Latest, false, false);

        /// <summary>Every original plays as printed (the Back Alley).</summary>
        public static TextRules PrintedText => new TextRules(Latest, true, false);

        /// <summary>
        /// A table's texts: a Library cup's <paramref name="pool"/> month; else its list's format end
        /// (<see cref="DeckRules.FormatEnd"/>, today for the newest list); else the end of its <paramref name="eraYear"/>
        /// (a circuit with no list). "No limits" plays as printed. It is a Goat table when the pool month, or else the
        /// list's own month, is in [<see cref="GoatFrom"/>, <see cref="GoatUntil"/>] (so TCG April 2004, whose format
        /// ends April 2005, isn't one; the 2005.4 GOAT list and the Goat Cup are). A Goat table by its list's month plays
        /// texts no later than <see cref="GoatUntil"/>: the list history has a gap after TCG April 2005 (the next TCG list
        /// on record is September 2008), so its format end alone would rule 2005 decks by 2008 texts.
        /// </summary>
        public static TextRules ForList(Banlist? list, IEnumerable<Banlist> all, string pool = "", int eraYear = 0)
        {
            bool InGoat(string ym) => ym.Length >= 7 && string.CompareOrdinal(ym.Substring(0, 7), GoatFrom) >= 0 && string.CompareOrdinal(ym.Substring(0, 7), GoatUntil) <= 0;
            if (pool.Length >= 7) return new TextRules(pool.Substring(0, 7), false, InGoat(pool));
            if (list != null && list.IsUnrestricted && list.Date.Length == 0) return PrintedText;
            if (list != null && list.Date.Length > 0)
            {
                // The GOAT list is the 2005 format itself; its "format end" would be the next TCG list on record.
                if (list.Name.Contains("GOAT")) return new TextRules(GoatUntil, false, true);
                var end = DeckRules.FormatEnd(list, all);
                var asOf = end.Length > 0 ? end : Latest;
                bool goat = InGoat(list.Date);
                if (goat && string.CompareOrdinal(asOf, GoatUntil) > 0) asOf = GoatUntil;
                return new TextRules(asOf, false, goat);
            }
            if (eraYear > 0) return new TextRules($"{eraYear}-12", false, false);
            return Today;
        }

        /// <summary>Whether an original text plays as printed here (inclusive: an errata printed in the AsOf month is in force).</summary>
        public bool Allows(TextRow row) => Printed || string.CompareOrdinal(row.Until, AsOf ?? Latest) > 0;

        /// <summary>"texts of September 2016" / "as printed" / "Goat rulings" / "today's texts".</summary>
        public string Describe() =>
            Printed ? "as printed"
            : Goat ? "Goat rulings"
            : AsOf == null || AsOf == Latest ? "today's texts"
            : $"texts of {DeckRules.MonthYear(AsOf)}";
    }

    /// <summary>
    /// What the player's deck plays at a table (section 5.2). CPU decks never pass through here: they play as authored
    /// (5.6). Phase 1 has no copy texts yet, so a null copy plays its slot as written where the table allows that text.
    /// </summary>
    public static class PlayTexts
    {
        /// <summary>
        /// The code a deck slot plays, given the copy assigned to it:
        /// <list type="bullet">
        /// <item>a base card or alternate art plays as written;</item>
        /// <item>a Pre-Errata slot plays the copy's text (the slot's own with a null copy) where the table allows it, the
        /// card's GOAT entry at a Goat table, else the current card; a copy with the current text plays current;</item>
        /// <item>a GOAT slot of a card with a timeline needs its earliest original: the GOAT entry at a Goat table, the
        /// Pre-Errata entry where that is allowed, else current; a ruling-only GOAT slot plays only at a Goat table;</item>
        /// <item>a `never` or `cpu` row, and a Pre-Errata entry with no row, are never the player's (current, unless printed
        /// texts play: then a row-less entry plays as written).</item>
        /// </list>
        /// A dup (or any slot whose text is the one played) keeps its id: replays and decks are never rewritten.
        /// </summary>
        public static uint PlayCode(uint slot, CardCopy? copy, TextRules rules, CardTexts texts, ICardDataProvider cards)
        {
            var info = cards.Get(slot);
            if (info == null) return slot;
            var baseCode = Banlist.BaseCode(slot, cards);
            if (baseCode == slot) return slot;
            var kind = CardVersions.KindOf(info);
            if (kind == CardVersions.TextKind.Goat)
            {
                var earliest = texts.Earliest(baseCode);
                if (earliest == null) return rules.Goat ? slot : baseCode;
                var goatText = CopyText(copy, earliest, baseCode, texts);
                if (goatText == null || !rules.Allows(goatText)) return baseCode;
                return rules.Goat ? slot : goatText.Code;
            }
            if (kind != CardVersions.TextKind.PreErrata) return slot;
            var row = texts.Row(slot);
            if (row == null) return rules.Printed ? slot : baseCode;
            if (row.Never || row.CpuOnly) return baseCode;
            var text = CopyText(copy, row, baseCode, texts);
            if (text == null || !rules.Allows(text)) return baseCode;
            if (rules.Goat && texts.Goat(baseCode) is var goat && goat != 0) return goat;
            return text.Code == row.Code ? slot : text.Code;
        }

        /// <summary>A slot with no copy where copies are counted: it plays as a current-text copy.</summary>
        private static readonly CardCopy NoCopy = new CardCopy();

        /// <summary>The original text a copy carries: <paramref name="asked"/> for a null copy (no collection), null for a current copy.</summary>
        private static TextRow? CopyText(CardCopy? copy, TextRow asked, uint baseCode, CardTexts texts)
        {
            if (copy == null) return asked;
            if (copy.Text == 0) return null;
            var row = texts.Row(copy.Text);
            return row != null && row.Base == baseCode && row.Collectible ? row : null;
        }

        /// <summary>
        /// The deck as played at a table: every slot through <see cref="PlayCode"/>, <paramref name="copies"/> lined up
        /// with Main then Extra as <see cref="Collection.Assign"/> returns them (null: no copies, e.g. free duel or a CPU's
        /// deck, and every slot plays as written where the table allows it; the Side Deck plays with no copy). With copies, a
        /// slot Assign left without one (a borrowed card in a loaner) plays as a current-text copy would (spec 5.2, phase 2).
        /// <paramref name="downgraded"/> lists the cards (base codes, once each, deck order) a slot asked for an original of
        /// and that play their current text here: the versus line's names.
        /// </summary>
        public static Deck ForTable(Deck deck, IReadOnlyList<CardCopy?>? copies, TextRules rules, CardTexts texts,
                                    ICardDataProvider cards, out List<uint> downgraded)
        {
            var mapped = deck.Clone();
            var down = new List<uint>();
            int at = 0;
            void Map(List<uint> list, bool withCopies)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var copy = withCopies && copies != null ? (at < copies.Count ? copies[at] : null) ?? NoCopy : null;
                    if (withCopies) at++;
                    var slot = list[i];
                    var played = PlayCode(slot, copy, rules, texts, cards);
                    var baseCode = Banlist.BaseCode(slot, cards);
                    if (played != slot && played == baseCode && !down.Contains(baseCode)) down.Add(baseCode);
                    list[i] = played;
                }
            }
            Map(mapped.Main, true);
            Map(mapped.Extra, true);
            Map(mapped.Side, false);
            downgraded = down;
            return mapped;
        }
    }
}
