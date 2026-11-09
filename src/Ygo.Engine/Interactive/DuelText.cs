using System.Collections.Generic;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.Interactive
{
    /// <summary>Turns the core's description ids, hints, zones, and bit flags into readable text.</summary>
    public static class DuelText
    {
        private static readonly Dictionary<ulong, string> System = new Dictionary<ulong, string>
        {
            // Option ids the core itself offers (ocgcore operations.cpp / libduel.cpp).
            [1] = "Summon it the usual way",
            [10] = "Remove the counters normally",
            [11] = "Pay the LP normally",
            [12] = "Detach the materials normally",
            [60] = "Heads",
            [61] = "Tails",
            [70] = "Monster Card",
            [71] = "Spell Card",
            [72] = "Trap Card",
            [80] = "Go to the Battle Phase",
            [81] = "Go to the End Phase",
            [30] = "Continue attacking?",
            [31] = "Attack directly?",
            [500] = "Select the card(s) to Tribute",
            [501] = "Select the card(s) to discard",
            [502] = "Select the card(s) to destroy",
            [503] = "Select the card(s) to banish",
            [504] = "Select the card(s) to send to the GY",
            [505] = "Select the card(s) to return to the hand",
            [506] = "Select the card(s) to add to your hand",
            [507] = "Select the card(s) to return to the Deck",
            [509] = "Select the monster(s) to Special Summon",
            [510] = "Select the card(s) to Set",
            [511] = "Select Fusion Materials",
            [512] = "Select Synchro Materials",
            [513] = "Select Xyz Materials",
            [514] = "Select a face-up card",
            [515] = "Select a face-down card",
            [516] = "Select a monster in Attack Position",
            [517] = "Select a monster in Defense Position",
            [518] = "Select the card to equip",
            [519] = "Select the material(s) to detach",
            [520] = "Select the monster to take control of",
            [526] = "Select the card(s) to reveal",
            [527] = "Select the card(s) to place on the field",
            [529] = "Select your own card",
            [530] = "Select your opponent's card",
            [531] = "Select the monster(s) to Tribute",
            [532] = "Select the monster to detach material from",
            [533] = "Select Link Materials",
            [549] = "Select the attack target",
            [550] = "Select the effect to activate",
            [551] = "Select the target(s)",
            [556] = "Select the effect to resolve",
            [561] = "Select a battle position",
            [569] = "Select a zone",
            [570] = "Select a zone to disable",
            [571] = "Select the zone to move to",
            [572] = "Select where to place counters",
            [575] = "Select the card(s) to negate",
        };

        /// <summary>
        /// A description id is either a card string (code &lt;&lt; 20 | index, see aux.Stringid) or a system string.
        /// Returns "" when nothing readable is known.
        /// </summary>
        public static string Describe(ulong description, ICardDataProvider cards)
        {
            if (description == 0) return "";
            if (description >= (1UL << 20))
            {
                var info = cards.Get((uint)(description >> 20));
                var index = (int)(description & 0xFFFFF);
                if (info != null && index < info.Strings.Length) return info.Strings[index];
                return "";
            }
            return System.TryGetValue(description, out var text) ? text : "";
        }

        public static string Zone(CardRef card, int viewer)
        {
            var whose = card.Controller == viewer ? "your" : "opponent's";
            switch (card.Location & 0x7f)
            {
                case Location.Hand: return $"{whose} hand";
                case Location.MZone: return card.Sequence >= 5 ? $"{whose} Extra Monster Zone" : $"{whose} Monster Zone {card.Sequence + 1}";
                case Location.SZone:
                    return card.Sequence == 5 ? $"{whose} Field Zone" : $"{whose} Spell & Trap Zone {card.Sequence + 1}";
                case Location.Grave: return $"{whose} GY";
                case Location.Removed: return $"{whose} banished cards";
                case Location.Extra: return $"{whose} Extra Deck";
                case Location.Deck: return $"{whose} Deck";
                default: return "";
            }
        }

        public static string Position(uint position)
        {
            switch (position)
            {
                case Ygo.Engine.Position.FaceUpAttack: return "Face-up Attack Position";
                case Ygo.Engine.Position.FaceDownAttack: return "Face-down Attack Position";
                case Ygo.Engine.Position.FaceUpDefense: return "Face-up Defense Position";
                case Ygo.Engine.Position.FaceDownDefense: return "Face-down Defense Position";
                default: return $"position {position}";
            }
        }

        public static readonly (ulong bit, string name)[] Races =
        {
            (0x1, "Warrior"), (0x2, "Spellcaster"), (0x4, "Fairy"), (0x8, "Fiend"), (0x10, "Zombie"), (0x20, "Machine"),
            (0x40, "Aqua"), (0x80, "Pyro"), (0x100, "Rock"), (0x200, "Winged Beast"), (0x400, "Plant"), (0x800, "Insect"),
            (0x1000, "Thunder"), (0x2000, "Dragon"), (0x4000, "Beast"), (0x8000, "Beast-Warrior"), (0x10000, "Dinosaur"),
            (0x20000, "Fish"), (0x40000, "Sea Serpent"), (0x80000, "Reptile"), (0x100000, "Psychic"),
            (0x200000, "Divine-Beast"), (0x400000, "Creator God"), (0x800000, "Wyrm"), (0x1000000, "Cyberse"),
            (0x2000000, "Illusion"),
        };

        public static readonly (uint bit, string name)[] Attributes =
        {
            (0x1, "EARTH"), (0x2, "WATER"), (0x4, "FIRE"), (0x8, "WIND"), (0x10, "LIGHT"), (0x20, "DARK"), (0x40, "DIVINE"),
        };
    }
}
