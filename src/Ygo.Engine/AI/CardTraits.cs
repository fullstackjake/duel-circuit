using System;
using System.Collections.Generic;

namespace Ygo.Engine.AI
{
    [Flags]
    public enum Trait
    {
        None = 0,
        Draw = 1 << 0,
        Search = 1 << 1,
        SpecialSummon = 1 << 2,
        Revive = 1 << 3,
        MonsterRemoval = 1 << 4,
        OneSided = 1 << 5,
        BoardWipe = 1 << 6,
        SpellTrapRemoval = 1 << 7,
        Negate = 1 << 8,
        HandTrap = 1 << 9,
        OnSummonTrigger = 1 << 10,
        QuickPlay = 1 << 11,
        /// <summary>
        /// A Spell or Trap whose first effect returns cards from the GY to the hand (The Warrior Returning Alive, Salvage,
        /// Fusion Recovery). Card advantage for the bot (SmartDuelist.UseRecoverySense), but not a Deck search: nothing that
        /// counts searches, Deck thinning or deck-out reads it, and deck building doesn't either.
        /// </summary>
        Recover = 1 << 12,

        Advantage = Draw | Search,
        Disruption = Negate | HandTrap,
    }

    /// <summary>
    /// Rough card roles read from card text. Good enough for generic play; deck-specific bots
    /// can override individual cards with <see cref="Override"/>.
    /// </summary>
    public static class CardTraits
    {
        private const uint TypeEffect = 0x20, TypeQuickPlay = 0x10000;

        private static readonly Dictionary<uint, Trait> Cache = new Dictionary<uint, Trait>();
        private static readonly Dictionary<uint, Trait> Overrides = new Dictionary<uint, Trait>();

        public static void Override(uint code, Trait traits)
        {
            lock (Cache)
            {
                Overrides[code] = traits;
                Cache.Remove(code);
            }
        }

        public static Trait Of(CardInfo? info)
        {
            if (info == null) return Trait.None;
            lock (Cache)
            {
                if (Cache.TryGetValue(info.Code, out var cached)) return cached;
                var traits = Overrides.TryGetValue(info.Code, out var o) ? o : Classify(info);
                Cache[info.Code] = traits;
                return traits;
            }
        }

        public static bool Has(this Trait traits, Trait flag) => (traits & flag) != 0;

        /// <summary>The first sentence returns cards from the GY to the hand ("Target 1 Warrior monster in your GY; add it to your hand").</summary>
        private static readonly System.Text.RegularExpressions.Regex RecoversFromGraveyard = new System.Text.RegularExpressions.Regex(
            @"^[^.]*in your (?:gy|graveyard)[^.;]*;[^.]*\badd (?:it|that target|that card|them|those targets|both) to your hand|^[^.]*from your (?:gy|graveyard) to your hand",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>The first sentence pays with cards from the hand ("Discard 2 cards, then", "Send 2 Spells from your hand").</summary>
        private static readonly System.Text.RegularExpressions.Regex HandCost = new System.Text.RegularExpressions.Regex(
            @"^[^.]*?\b(?:discard|from your hand)\b[^.]*?\badd\b", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static Trait Classify(CardInfo info)
        {
            var t = info.Text.ToLowerInvariant();
            var traits = Trait.None;
            bool isMonster = (info.Type & CardType.Monster) != 0;

            if (Contains(t, "draw 1 card", "draw 2 cards", "draw 3 cards", "draw cards", "draw 1 more card"))
                traits |= Trait.Draw;
            if (t.Contains("add") && Contains(t, "from your deck to your hand", "from your deck or gy to your hand",
                    "from your gy to your hand", "to your hand from your deck"))
                traits |= Trait.Search;
            if (t.Contains("special summon"))
            {
                traits |= Trait.SpecialSummon;
                if (Contains(t, "in your gy", "in either gy", "from your gy", "from either gy", "in the gy"))
                    traits |= Trait.Revive;
            }

            bool targetsMonsters = Contains(t, "monster your opponent controls", "monsters your opponent controls",
                "monster on the field", "monsters on the field", "face-up monster", "opponent's monster");
            if (targetsMonsters && Contains(t, "destroy", "banish", "return", "shuffle", "face-down defense", "change"))
                traits |= Trait.MonsterRemoval;
            if (Contains(t, "destroy all monsters on the field", "destroy as many monsters on the field"))
                traits |= Trait.BoardWipe | Trait.MonsterRemoval;
            if (Contains(t, "your opponent controls", "your opponent's field"))
                traits |= Trait.OneSided;
            if (Contains(t, "spell/trap", "spells/traps", "spell card", "trap card") && Contains(t, "destroy", "banish", "return"))
                traits |= Trait.SpellTrapRemoval;
            if (t.Contains("negate")) traits |= Trait.Negate;
            // Not one that costs cards from the hand first (Magical Stone Excavation discards 2, Spell Reproduction sends 2
            // Spells): a conversion or a loss, not advantage.
            if (!isMonster && (traits & Trait.Search) == 0 && RecoversFromGraveyard.IsMatch(t) && !HandCost.IsMatch(t)) traits |= Trait.Recover;

            bool onSummon = isMonster && (info.Type & TypeEffect) != 0
                && Contains(t, "if this card is normal summoned", "when this card is normal summoned",
                    "if this card is summoned", "when this card is summoned");
            if (onSummon) traits |= Trait.OnSummonTrigger;
            // Hand traps: a Quick Effect that discards / sends the card itself from the hand (Ash Blossom, Maxx "C",
            // Effect Veiler, Ghost Ogre, Kuriboh...). Not a monster that does its work when Summoned (Aleister the
            // Invoker fetches Invocation; its hand effect is a bonus): kept in hand, it would never be played.
            if (isMonster && !onSummon && t.Contains("quick effect") && Contains(t, "discard this card", "send this card from your hand"))
                traits |= Trait.HandTrap;
            if ((info.Type & CardType.Spell) != 0 && (info.Type & TypeQuickPlay) != 0)
                traits |= Trait.QuickPlay;

            return traits;
        }

        private static readonly System.Text.RegularExpressions.Regex StaysOnField = new System.Text.RegularExpressions.Regex(
            @"remains? on the field|[Ee]quip(?:ped)? (?:it |that monster |them )?with this card|[Ee]quip this card|this card as an Equip|becomes? an Equip|[Ee]quip Card",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// A Spell/Trap that leaves the field once it has resolved: a Normal, Quick-Play or Ritual Spell, or a Normal or
        /// Counter Trap. Not a Continuous, Field or Equip card, nor a Normal one that stays (Swords of Revealing Light
        /// "remains on the field"; Kunai with Chain, Altergeist Manifestation and Metalmorph equip themselves).
        /// </summary>
        public static bool LeavesAfterResolving(CardInfo info) =>
            (info.Type & CardType.Monster) == 0 && (info.Type & (0x20000 | 0x40000 | 0x80000)) == 0 && !StaysOnField.IsMatch(info.Text);

        /// <summary>
        /// A Spell/Trap on the chain that resolves just the same off the field: it leaves after resolving, and its text
        /// never refers to "this card" (Pot of Greed, Dark Hole, Monster Reborn, Reckless Greed). Destroying or banishing
        /// it in response gains nothing. A card that summons, Sets, attaches or equips itself, or locks "this card's
        /// column", needs to stay (its script checks it's still there), so removal on the chain does stop it (The Phantom
        /// Knights of Shade Brigandine, Scrap-Iron Scarecrow, Xyz Reborn, Infinite Impermanence): not this.
        /// </summary>
        public static bool ResolvesOffField(CardInfo info) =>
            LeavesAfterResolving(info) && info.Text.IndexOf("this card", StringComparison.OrdinalIgnoreCase) < 0;

        private static readonly System.Text.RegularExpressions.Regex PureRemoval = new System.Text.RegularExpressions.Regex(
            @"^(?:Pay \d+ LP(?:, then|;) |Discard 1 card(?:, then|;) )?[Tt]arget (?:1|up to 2|2) (?:face-up |Set )?(?:Spells?/Traps?|Spell/Trap Cards?) (?:on the field|your opponent controls); (?:destroy|banish) (?:that target|it|them|those targets)(?:, then you can Set 1 Spell/Trap from your hand)?\.(?: If [^.]*you can activate this card from your hand\.)?$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// A Spell/Trap whose whole text is Spell/Trap removal, nothing else: Mystical Space Typhoon, Cosmic Cyclone,
        /// Twister, Typhoon, Twin Twisters, Dust Tornado. (A card with any other effect, like Artifact Ignition's Set or
        /// a monster's, may be played for that: it's never judged by its removal.)
        /// </summary>
        public static bool PureSpellTrapRemoval(CardInfo info) =>
            (info.Type & CardType.Monster) == 0 && PureRemoval.IsMatch(info.Text.Trim());

        /// <summary>Destroys every Spell/Trap on a side without targeting (Heavy Storm, Harpie's Feather Duster).</summary>
        public static bool WipesSpellTraps(CardInfo info) =>
            info.Text.Contains("all Spell") || info.Text.Contains("all other Spell") || info.Text.Contains("all cards on the field")
            || info.Text.Contains("all cards your opponent controls") || info.Text.Contains("all cards you control");

        private static bool Contains(string text, params string[] any)
        {
            foreach (var s in any)
                if (text.Contains(s)) return true;
            return false;
        }
    }
}
