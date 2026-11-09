using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Ygo.Engine.AI;

namespace Ygo.Engine.Presentation
{
    /// <summary>
    /// What a card is at a glance (Link Evolution's "Card Genre" and its Spell Speed line): the card's Spell Speed,
    /// worked out from its type and wording, and genre tags (Search, Negate, Banish...) from the card database's
    /// search categories. The genres are also the deck editor's <c>does:</c> search filters (see <see cref="Genres"/>).
    /// </summary>
    public static class CardTags
    {
        /// <summary>A tag: its short label and what it means.</summary>
        public readonly struct Tag
        {
            public readonly string Label, Meaning;
            public Tag(string label, string meaning) { Label = label; Meaning = meaning; }
            public override string ToString() => Label;
        }

        private static readonly Regex QuickWording = new Regex(
            @"\(Quick Effect\)|during either player's turn|during your opponent's turn|during each player's turn", RegexOptions.IgnoreCase);
        private static readonly Regex ActivatedWording = new Regex(@"[:;]|\bYou can\b|\bFLIP\b");

        /// <summary>
        /// The card's Spell Speed (1, 2 or 3) and why, or null when it has nothing to activate (a Normal Monster, or
        /// a monster whose effects only apply). For a monster with a Quick Effect it's 2 (its other effects are 1).
        /// </summary>
        public static (int speed, string why)? SpellSpeed(CardInfo info)
        {
            uint type = info.Type;
            if ((type & CardType.Trap) != 0)
                return (type & CardType.Counter) != 0
                    ? (3, "Counter Traps are Spell Speed 3: only another Counter Trap can respond to them.")
                    : (2, "Traps are Spell Speed 2: they can respond to Spell Speed 1 and 2, once they've been Set for a turn.");
            if ((type & CardType.Spell) != 0)
                return (type & CardType.QuickPlay) != 0
                    ? (2, "Quick-Play Spells are Spell Speed 2: from your hand on your turn, or Set first to use on your opponent's turn.")
                    : (1, "This Spell is Spell Speed 1: it starts a chain on your own turn and can't respond to anything.");
            if ((type & CardType.Monster) == 0 || (type & CardType.Effect) == 0) return null;
            if (QuickWording.IsMatch(info.Text))
                return (2, "Its Quick Effect is Spell Speed 2: it can respond to other cards, even on your opponent's turn. Its other effects are Spell Speed 1.");
            if (!ActivatedWording.IsMatch(info.Text)) return null;
            return (1, "Monster effects are Spell Speed 1: they can start a chain, but can't respond to anything.");
        }

        /// <summary>
        /// A genre: what a card does, as a search filter (<c>does:token</c>) and a card detail chip. Most come from the card
        /// database's search category bits; "Wins the Duel" is read from the text.
        /// </summary>
        public sealed class Genre
        {
            /// <summary>The search token, what follows <c>does:</c> ("search", "destroymonster", "win").</summary>
            public readonly string Token;
            /// <summary>The chip's label ("Search") and what it means.</summary>
            public readonly string Label, Meaning;
            /// <summary>The More filters panel's group: one of <see cref="Groups"/>.</summary>
            public readonly string Group;
            /// <summary>The category bits it reads (0: text-derived).</summary>
            public readonly ulong Bits;
            private readonly Func<CardInfo, string, bool>? _fallback;

            internal Genre(string token, ulong bits, string label, string meaning, string group, Func<CardInfo, string, bool>? fallback)
            {
                Token = token; Bits = bits; Label = label; Meaning = meaning; Group = group; _fallback = fallback;
            }

            /// <summary>The chip (label and meaning).</summary>
            public Tag Tag => new Tag(Label, Meaning);

            /// <summary>
            /// Whether the card does this. A card with categories matches on its categories only; one without falls back to
            /// the bots' roles and the text tags where there's one for this genre, and otherwise doesn't match.
            /// </summary>
            public bool Matches(CardInfo info) => Matches(info, info.Text.ToLowerInvariant());

            /// <summary><see cref="Matches(CardInfo)"/> with the card's text already lowercased (the search caches it).</summary>
            public bool Matches(CardInfo info, string lowerText)
            {
                if (Bits == 0) return _fallback != null && _fallback(info, lowerText);
                if (info.Category != 0) return (info.Category & Bits) != 0;
                return _fallback != null && _fallback(info, lowerText);
            }

            public override string ToString() => Label;
        }

        /// <summary>The More filters panel's genre groups, in order.</summary>
        public static readonly IReadOnlyList<string> Groups = new[] { "Removal", "Cards and resources", "Summoning", "Battle", "Life Points", "Other" };

        // Effect tags read straight from the text (the search's banish, discard, burn and gainlp words use these too).
        internal static bool TextBanishes(string t) => t.Contains("banish");
        internal static bool TextDiscards(string t) => t.Contains("discard");
        internal static bool TextBurns(string t) => t.Contains("inflict") && t.Contains("damage");
        internal static bool TextGainsLp(string t) => t.Contains("gain") && (t.Contains(" lp") || t.Contains("life points"));
        private static bool TextWins(string t) => t.Contains("win the duel");

        private static Func<CardInfo, string, bool> Role(Trait role) => (c, _) => (CardTraits.Of(c) & role) != 0;

        private const string Removal = "Removal", Resources = "Cards and resources", Summoning = "Summoning", Battle = "Battle", Life = "Life Points", Other = "Other";

        /// <summary>Every genre, most telling first (the card detail's chip order); the database's category bits, then "Wins the Duel".</summary>
        public static readonly IReadOnlyList<Genre> Genres = new[]
        {
            new Genre("negate", 0x30000, "Negate", "Negates a card, an effect, an activation or a Summon.", Other, Role(Trait.Negate)),
            new Genre("search", 0x200, "Search", "Adds a card from your Deck to your hand.", Resources, Role(Trait.Search)),
            new Genre("specialsummon", 0x100000, "Special Summon", "Special Summons a monster.", Summoning, Role(Trait.SpecialSummon | Trait.Revive)),
            new Genre("draw", 0x100, "Draw", "Draws cards.", Resources, Role(Trait.Draw)),
            new Genre("destroymonster", 0x1, "Destroy monsters", "Destroys monsters.", Removal, Role(Trait.MonsterRemoval)),
            new Genre("destroyst", 0x2, "Destroy S/T", "Destroys Spells or Traps.", Removal, Role(Trait.SpellTrapRemoval)),
            new Genre("banish", 0x80, "Banish", "Banishes cards (removes them from play, face-up unless it says face-down).", Removal, (_, t) => TextBanishes(t)),
            new Genre("tohand", 0x20, "To hand", "Returns cards to the hand, or adds them from the GY.", Removal, null),
            new Genre("todeck", 0x40, "To Deck", "Shuffles or puts cards back into the Deck.", Removal, null),
            new Genre("mill", 0x4, "Mill", "Sends cards from the Deck to the GY.", Resources, null),
            new Genre("discard", 0x8, "Discard", "Discards, or sends cards from the hand to the GY.", Resources, (_, t) => TextDiscards(t)),
            new Genre("control", 0x40000000, "Take control", "Takes control of your opponent's monster.", Removal, null),
            new Genre("damage", 0x40000, "Damage", "Inflicts damage to Life Points.", Life, (_, t) => TextBurns(t)),
            new Genre("gainlp", 0x80000, "Gain LP", "Gains Life Points.", Life, (_, t) => TextGainsLp(t)),
            new Genre("atkdef", 0x400, "ATK/DEF", "Changes ATK or DEF.", Battle, null),
            new Genre("level", 0x800, "Level", "Changes Levels.", Battle, null),
            new Genre("position", 0x1000, "Position", "Changes battle positions (attack, defense, face-down).", Removal, null),
            new Genre("piercing", 0x2000, "Piercing", "Inflicts piercing battle damage (damage past a Defense Position monster's DEF).", Battle, null),
            new Genre("directattack", 0x4000, "Direct attack", "Lets a monster attack directly.", Battle, null),
            new Genre("extraattacks", 0x8000, "Extra attacks", "Lets a monster attack more than once.", Battle, null),
            new Genre("token", 0x400000, "Token", "Makes Tokens.", Summoning, null),
            new Genre("columns", 0x80000000, "Columns", "Cares about columns or moves cards between zones.", Other, null),
            new Genre("fusion", 0x800000, "Fusion", "Fusion-related.", Summoning, null),
            new Genre("ritual", 0x1000000, "Ritual", "Ritual-related.", Summoning, null),
            new Genre("synchro", 0x2000000, "Synchro", "Synchro-related.", Summoning, null),
            new Genre("xyz", 0x4000000, "Xyz", "Xyz-related.", Summoning, null),
            new Genre("link", 0x8000000, "Link", "Link-related.", Summoning, null),
            new Genre("counters", 0x10000000, "Counters", "Places or uses counters.", Other, null),
            new Genre("coin", 0x20000000, "Coin / dice", "Tosses coins or rolls dice.", Other, null),
            new Genre("win", 0, "Wins the Duel", "Wins the Duel outright when its condition is met.", Other, (_, t) => TextWins(t)),
        };

        // The More filters panel's order within each group (section 1 of the card search spec).
        private static readonly string[] PanelOrder =
        {
            "destroymonster", "destroyst", "banish", "tohand", "todeck", "control", "position",
            "search", "draw", "mill", "discard",
            "specialsummon", "token", "fusion", "ritual", "synchro", "xyz", "link",
            "atkdef", "level", "piercing", "directattack", "extraattacks",
            "damage", "gainlp",
            "negate", "counters", "coin", "columns", "win",
        };

        private static readonly Dictionary<string, Genre> ByToken = Genres.ToDictionary(g => g.Token);

        /// <summary>The genre with this token ("search"; case and spaces ignored), or null.</summary>
        public static Genre? GenreOf(string token)
        {
            var key = new string((token ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            return ByToken.TryGetValue(key, out var g) ? g : null;
        }

        /// <summary>One group's genres in the More filters panel's order (<paramref name="group"/> is one of <see cref="Groups"/>).</summary>
        public static List<Genre> GenresIn(string group) =>
            PanelOrder.Select(t => ByToken[t]).Where(g => g.Group == group).ToList();

        /// <summary>
        /// The card's genre tags, most telling first: its database categories (none when it has no categories), plus
        /// "Wins the Duel" when its text says so.
        /// </summary>
        public static List<Tag> GenresOf(CardInfo info)
        {
            var tags = new List<Tag>();
            string? lower = null;
            foreach (var genre in Genres)
            {
                if (genre.Bits == 0)
                {
                    if (genre.Matches(info, lower ??= info.Text.ToLowerInvariant())) tags.Add(genre.Tag);
                    continue;
                }
                if ((info.Category & genre.Bits) == 0) continue;
                // "To hand" says nothing more once it's a search.
                if (genre.Bits == 0x20 && (info.Category & 0x200) != 0) continue;
                tags.Add(genre.Tag);
            }
            return tags;
        }
    }
}
