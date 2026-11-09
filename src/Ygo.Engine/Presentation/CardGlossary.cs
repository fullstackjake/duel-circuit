using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Ygo.Engine.Presentation
{
    /// <summary>
    /// Card-text terms and what they mean, for the card detail: Master Duel's most repeated criticism is that it leaves
    /// card text unexplained ("Quick Effect", "once per turn", "negate the activation" vs "negate the effect"). The detail
    /// colors each term (its first use in a card) and explains it on hover. Plain C# so the harness can check it against
    /// every card.
    /// </summary>
    public static class CardGlossary
    {
        public sealed class Term
        {
            public string Id = "", Title = "", Meaning = "";
            public Regex Pattern = null!;
        }

        /// <summary>A term found in a card's text: where, and which.</summary>
        public readonly struct Found
        {
            public readonly int Start, Length;
            public readonly Term Term;
            public Found(int start, int length, Term term) { Start = start; Length = length; Term = term; }
        }

        /// <summary>How card text is punctuated, for the detail's "?" help.</summary>
        public const string ReadingHelp =
            "Card text has a grammar. Before a colon (:) is when the effect can be used (its condition). Before a semicolon (;) " +
            "is what you do to activate it (its cost, or choosing a target): that happens even if the effect is negated. After " +
            "the semicolon is what happens when it resolves. Terms in blue explain themselves when you point at them.";

        private static Term T(string id, string title, string pattern, string meaning) =>
            new Term { Id = id, Title = title, Meaning = meaning, Pattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled) };

        /// <summary>Longer, more specific terms come first: a match claims its characters, and later terms skip them.</summary>
        public static readonly IReadOnlyList<Term> Terms = new[]
        {
            // Timing and limits
            T("hard-opt", "Only once per turn (by name)",
                @"\b(you )?can only (use|activate)( each| 1)?[^.]*?once per (turn|duel)",
                "A hard limit: it counts across every copy of the card with this name. Use one copy's effect and the others can't use it that turn (or that Duel)."),
            T("soft-opt", "Once per turn",
                @"\bonce per turn\b",
                "A soft limit: each copy of the card can use it once per turn. Two copies on the field can use it twice. (If the card says \"you can only\", the limit counts across all copies.)"),
            T("once-chain", "Once per Chain",
                @"\bonce per chain\b",
                "It can be used again in the same turn, but not twice in the same Chain."),
            T("quick", "Quick Effect",
                @"\bquick effect\b",
                "Can be activated in either player's turn, and in response to other cards (like a Quick-Play Spell or a Trap)."),
            T("when", "When ... you can (missing the timing)",
                @"\bwhen\b(?![^.:]*quick effect)(?=[^.:]*:\s*you can\b)",
                "An optional \"When\" effect only activates if its trigger was the last thing that happened. If something else happened after it (later in the same effect), it misses the timing. \"If ... you can\" effects never miss the timing."),
            T("in-response", "In response",
                @"\bin response\b",
                "Chained to an activation: it resolves first, before the card it answers."),
            T("chain", "Chain",
                @"\bchain(s|ed)?\b(?! link)|\bchain link\b",
                "Cards and effects activated in response to each other form a Chain. The Chain resolves backwards: the last link first."),
            T("damage-step", "Damage Step",
                @"\bdamage step\b",
                "The part of a battle where damage is calculated. Only some cards can be activated in it (those that change ATK/DEF, Counter Traps, and cards that say they can)."),
            T("damage-calc", "Damage calculation",
                @"\bdamage calculation\b",
                "The moment in the Damage Step when battle damage is worked out from ATK and DEF."),
            T("standby", "Standby Phase",
                @"\bstandby phase\b",
                "The phase after the Draw Phase, before the Main Phase. Upkeep costs and \"during your next Standby Phase\" effects happen here."),
            T("end-phase", "End Phase",
                @"\bend phase\b",
                "The last phase of a turn. \"Until the End Phase\" effects stop here, and hand size is checked."),
            T("battle-phase", "Battle Phase",
                @"\bbattle phase\b",
                "The phase where monsters attack. It comes after Main Phase 1; there's a Main Phase 2 after it."),
            T("main-phase", "Main Phase",
                @"\bmain phase( [12])?\b",
                "Where you summon, set and activate most cards. Main Phase 1 comes before the Battle Phase, Main Phase 2 after it."),

            // Negation
            T("negate-activation", "Negate the activation",
                @"\bnegate (the|its|that|their) activation\b|\bnegate (the )?activation\b",
                "The card counts as never activated: its effect doesn't happen, and a negated Spell or Trap goes to the GY. Costs already paid stay paid. A hard once-per-turn limit isn't used up if the activation was negated."),
            T("negate-effect", "Negate the effect",
                @"\bnegate (the|that|its) effect\b|\bnegate that\b(?! card)",
                "The card was activated, but its effect does nothing. It still counts as having used its once-per-turn limit."),
            T("negate-effects", "Negate its effects",
                @"\bnegate (the effects of|its effects|their effects)\b|\b(its|their) effects are negated\b|\bhas its effects negated\b",
                "A monster (or card) on the field loses its effects for a time: it can't use them, and its ongoing effects stop."),
            T("negate-summon", "Negate the Summon",
                @"\bnegate (the|that) (summon|special summon|normal summon)\b",
                "The monster is never summoned: it goes to the GY instead. Only cards that say they negate a Summon can do this, and only as it happens."),

            // Targeting and protection
            T("target", "Target",
                @"\btarget(s|ed|ing)?\b",
                "Chosen when the effect is activated, and shown to both players. If the target leaves or changes before the effect resolves, the effect may do nothing. Cards that \"can't be targeted\" can't be chosen."),
            T("unaffected", "Unaffected",
                @"\bunaffected by\b",
                "Effects of that kind don't apply to this card at all (it isn't destroyed, banished, or changed by them). Costs and Summon rules still work."),
            T("cannot-target", "Cannot be targeted",
                @"\bcannot (be )?target\b|\bcannot be targeted\b",
                "Effects that choose a target can't choose this card. Effects that don't target (\"destroy all\") still work."),

            // Summons
            T("special-summon", "Special Summon",
                @"\bspecial summon(s|ed|ing)?\b",
                "Any summon that isn't a Normal or Flip Summon: from the Extra Deck, by a card's effect, or by a summoning rule. You can Special Summon as many times as you're able each turn."),
            T("normal-summon", "Normal Summon",
                @"\bnormal summon(s|ed)?\b(?!/set)|\bnormal summon/set\b",
                "Your one free summon each turn, from the hand. Level 5 and 6 monsters need 1 Tribute, Level 7 and up need 2. Setting a monster face-down uses it too."),
            T("tribute", "Tribute",
                @"\btribut(e|es|ed|ing)\b",
                "Sending your own monster from the field (or hand, if the card says so) to the GY as a cost or for a Tribute Summon. It isn't destroyed."),
            T("flip", "FLIP",
                @"\bflip(ped)?( summon(s|ed)?| face-up)?\b|\bflip:",
                "A FLIP effect happens when the monster is turned face-up: by a Flip Summon, by being attacked while face-down, or by a card's effect."),
            T("fusion", "Fusion Summon",
                @"\bfusion summon(s|ed)?\b",
                "Summoning a Fusion Monster from the Extra Deck by sending the listed materials to the GY, usually with a card like Polymerization."),
            T("synchro", "Synchro Summon",
                @"\bsynchro summon(s|ed)?\b",
                "Summoning a Synchro Monster from the Extra Deck by sending 1 Tuner and non-Tuners whose Levels add up exactly to its Level."),
            T("tuner", "Tuner",
                @"\b(non-)?tuners?\b",
                "A monster type needed for Synchro Summons: a Synchro needs exactly 1 Tuner plus non-Tuner monsters."),
            T("xyz", "Xyz Summon",
                @"\bxyz summon(s|ed)?\b",
                "Summoning an Xyz Monster from the Extra Deck by stacking monsters of the same Level under it. They become its materials."),
            T("detach", "Detach (Xyz Material)",
                @"\bdetach\b|\bxyz materials?\b",
                "An Xyz Monster's materials sit under it. Many Xyz effects \"detach\" one (send it to the GY) as their cost."),
            T("link", "Link Summon",
                @"\blink summon(s|ed)?\b",
                "Summoning a Link Monster from the Extra Deck by sending monsters equal to its Link Rating (a Link Monster used as material can count as its Link Rating)."),
            T("points-to", "Points to / co-linked",
                @"\bpoints? to\b|\bco-linked\b",
                "A Link Monster's arrows point at zones. A monster in a zone it points to is \"linked\"; two Link Monsters pointing at each other are \"co-linked\"."),
            T("ritual", "Ritual Summon",
                @"\britual summon(s|ed)?\b",
                "Summoning a Ritual Monster from the hand with its Ritual Spell, by Tributing monsters whose Levels add up to its Level (or more, if the Spell says so)."),
            T("pendulum", "Pendulum",
                @"\bpendulum (zone|scale|summon)s?\b",
                "A Pendulum Monster can be placed in a Pendulum Zone as a Spell. With two Scales placed, you can Pendulum Summon monsters whose Levels are between them."),
            T("summon-cond", "Cannot be Normal Summoned",
                @"\bcannot be normal summoned(/set)?\b",
                "This monster has a summoning rule: it can only be Special Summoned the way the card describes."),

            // Places and movement
            T("gy", "GY (Graveyard)",
                @"\bgy\b|\bgraveyards?\b",
                "Where used, destroyed and discarded cards go. Many cards can use cards in the GY."),
            T("banish", "Banish",
                @"\bbanish(es|ed|ing|ment)?\b",
                "Remove a card from play: it goes face-up (or face-down) to the banishment zone, out of the GY. Fewer cards can get it back."),
            T("destroy-send", "Destroy",
                @"\bdestroy(s|ed)?\b",
                "Destroyed cards go to the GY. \"Send to the GY\" isn't destruction, so cards that protect against destruction don't stop it."),
            T("send", "Send to the GY",
                @"\bsend(s)?\b(?=[^.;]*? to the (gy|graveyard)\b)",
                "Moving a card to the GY without destroying it. It gets around protection from destruction."),
            T("extra-deck", "Extra Deck",
                @"\bextra deck\b",
                "Your separate deck of Fusion, Synchro, Xyz and Link Monsters (and face-up Pendulum Monsters). They're summoned from there, not drawn."),
            T("emz", "Extra Monster Zone",
                @"\bextra monster zone\b",
                "The zones between both fields. Each player can use one; monsters from the Extra Deck often need it (Link Monsters can point to more zones)."),
            T("column", "Column",
                @"\bcolumns?\b",
                "A zone's column is the line through both players' Monster and Spell & Trap Zones in front of it."),
            T("search", "Add from the Deck to the hand",
                @"\bfrom your deck to your hand\b",
                "A \"search\": take the card out of your Deck and put it in your hand (both players can see it). Cards like Ash Blossom answer it."),
            T("excavate", "Excavate",
                @"\bexcavat(e|es|ed)\b",
                "Reveal cards from the top of your Deck. What isn't used goes where the card says (often back to the Deck)."),
            T("token", "Token",
                @"\btokens?\b",
                "A monster made by an effect, not a card. It can't go to the hand, GY or Deck: when it leaves the field it's gone."),

            // Card kinds and states
            T("continuous", "Continuous",
                @"\bcontinuous (spell|trap)s?\b",
                "Stays on the field after it's activated and keeps working while it's face-up."),
            T("quick-play", "Quick-Play Spell",
                @"\bquick-play\b",
                "A Spell you can activate in either player's turn (from a Set position on the opponent's turn), and in response to other cards."),
            T("counter", "Counter Trap",
                @"\bcounter trap\b",
                "A Trap that can only be answered by other Counter Traps. Most negate a Summon or an activation."),
            T("face-down", "Set / face-down",
                @"\bset\b(?! (1|2|a|an|the) \d)|\bface-down\b",
                "Placed face-down. A Set Trap can be activated from the next turn on; a face-down monster is in Defense Position."),
            T("pierce", "Piercing damage",
                @"\bpiercing\b",
                "When this attacks a Defense Position monster with less DEF than its ATK, the difference is dealt as damage anyway."),
            T("original", "Original ATK",
                @"\boriginal (atk|def|attack|defense|name|type|attribute|level)\b",
                "The value printed on the card, ignoring anything currently changing it."),
            T("control", "Take control",
                @"\btake control\b|\bchange control\b|\bcontrol of\b",
                "The monster moves to the other player's field. Its owner gets it back when the effect ends (or when it leaves the field)."),
        };

        private static readonly Regex Quoted = new Regex("\"[^\"]*\"", RegexOptions.Compiled);

        /// <summary>
        /// The terms in a card's text, each at its first use, not overlapping, and never starting inside a quoted card
        /// name ("Quick Launch" isn't a Quick Effect). A longer term may contain a quoted name ("You can only use this
        /// effect of "Ash Blossom" once per turn").
        /// </summary>
        public static List<Found> Find(string text)
        {
            var found = new List<Found>();
            if (string.IsNullOrEmpty(text)) return found;
            var inQuote = new bool[text.Length];
            var taken = new bool[text.Length];
            foreach (System.Text.RegularExpressions.Match q in Quoted.Matches(text))
                for (int i = q.Index; i < q.Index + q.Length; i++) inQuote[i] = true;
            foreach (var term in Terms)
            {
                foreach (System.Text.RegularExpressions.Match m in term.Pattern.Matches(text))
                {
                    if (m.Length == 0 || inQuote[m.Index]) continue;
                    bool free = true;
                    for (int i = m.Index; i < m.Index + m.Length && free; i++) free = !taken[i];
                    if (!free) continue;
                    for (int i = m.Index; i < m.Index + m.Length; i++) taken[i] = true;
                    found.Add(new Found(m.Index, m.Length, term));
                    break; // its first use in the card is enough
                }
            }
            found.Sort((a, b) => a.Start.CompareTo(b.Start));
            return found;
        }

        /// <summary>
        /// The text with each found term wrapped (for rich text: <c>wrap(term, piece)</c> returns the marked-up piece),
        /// plus the matches. Characters outside terms are copied as they are.
        /// </summary>
        public static string Markup(string text, IReadOnlyList<Found> matches, Func<Term, string, string> wrap)
        {
            var sb = new StringBuilder(text.Length + matches.Count * 24);
            int at = 0;
            foreach (var m in matches)
            {
                sb.Append(text, at, m.Start - at);
                sb.Append(wrap(m.Term, text.Substring(m.Start, m.Length)));
                at = m.Start + m.Length;
            }
            sb.Append(text, at, text.Length - at);
            return sb.ToString();
        }
    }
}
