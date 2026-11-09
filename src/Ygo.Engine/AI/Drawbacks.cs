using System;
using System.Text.RegularExpressions;

namespace Ygo.Engine.AI
{
    /// <summary>
    /// What a card costs its own controller, read from its text: damage it deals you every Standby Phase (Number 30:
    /// Acid Golem of Destruction, Dark Contracts), an LP payment or self-damage to use it (Solemn Strike, Krebons,
    /// Performage Trick Clown), a lock on your own Special Summons (Acid Golem again), and the archetype rule that
    /// destroys it when you control an outsider (Burning Abyss).
    /// </summary>
    public static class Drawbacks
    {
        private static readonly Regex Upkeep = new Regex(@"your Standby Phase[^.]*?\btake (\d+) damage", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PayLp = new Regex(@"(?<!opponent )\bpay (\d+) (?:LP|Life Points)|\byou take (\d+) damage", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Outsider = new Regex("If you control a monster that is not an? \"([^\"]+)\" monster, destroy this card", RegexOptions.Compiled);

        /// <summary>
        /// Damage this card deals its own controller each of their Standby Phases (0: none). Cards made to be given to the
        /// opponent (Lava Golem: "to your opponent's field") don't count: there the upkeep is the point.
        /// </summary>
        public static int UpkeepDamage(CardInfo? info)
        {
            if (info == null) return 0;
            if (info.Text.Contains("opponent's field") && info.Text.Contains("Special Summon")) return 0;
            var m = Upkeep.Match(info.Text);
            return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
        }

        private static readonly Regex Materials = new Regex(@"^(\d+)\+? Level", RegexOptions.Compiled);

        /// <summary>
        /// Standby Phases a card's upkeep can be dodged (an Xyz Monster that can "Detach 1 ... or take" damage: one
        /// per material it's summoned with); 0 otherwise.
        /// </summary>
        public static int FreeUpkeepTurns(CardInfo? info)
        {
            if (info == null || !Regex.IsMatch(info.Text, @"Detach 1[^.]*or take", RegexOptions.IgnoreCase)) return 0;
            var m = Materials.Match(info.Text);
            return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 2;
        }

        /// <summary>LP this card makes you pay to use it (the largest fixed amount in its text; 0: none).</summary>
        public static int LifePointCost(CardInfo? info)
        {
            if (info == null) return 0;
            int most = 0;
            foreach (Match m in PayLp.Matches(info.Text))
                if (int.TryParse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, out var n)) most = Math.Max(most, n);
            return most;
        }

        private static readonly Regex SelfDamageText = new Regex(@"(?:\bif you do|\bthen|\band),? take (\d+) damage", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Damage this card's effect deals its own controller, said without "you" ("Special Summon this card, and if you do,
        /// take 400 damage": Blackwing - Zephyros the Elite, Assault Synchron, Performage Plushfire); 0: none. The most it names.
        /// </summary>
        public static int SelfDamage(CardInfo? info)
        {
            if (info == null) return 0;
            int most = 0;
            foreach (Match m in SelfDamageText.Matches(info.Text))
                if (int.TryParse(m.Groups[1].Value, out var n)) most = Math.Max(most, n);
            return most;
        }

        private static readonly Regex Tax = new Regex(@"each time your opponent activates a card or effect, inflict (\d+) damage to your opponent", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Damage this card deals its controller's opponent each time they activate something (Red-Eyes Flare Metal
        /// Dragon: 500 per activation); 0: none.
        /// </summary>
        public static int ActivationTax(CardInfo? info)
        {
            if (info == null) return 0;
            var m = Tax.Match(info.Text);
            return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
        }

        private static readonly Regex DeckSpend = new Regex(
            @"banish (\d+) cards from the top of your Deck|send the top (\d+) cards of your Deck to the (?:GY|Graveyard)|\bdraw (\d+) cards",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Cards this card takes off the top of your own Deck to use (Gizmek Orochi banishes 8, Tearlaments send 3,
        /// Pot of Greed draws 2): the most it names; 0: none.
        /// </summary>
        public static int DeckCost(CardInfo? info)
        {
            if (info == null) return 0;
            int most = 0;
            foreach (Match m in DeckSpend.Matches(info.Text))
                for (int g = 1; g <= 3; g++)
                    if (m.Groups[g].Success && int.TryParse(m.Groups[g].Value, out var n)) most = Math.Max(most, n);
            return most;
        }

        /// <summary>A card that damages both players (Ring of Destruction: both take the destroyed monster's ATK).</summary>
        public static bool HurtsBothPlayers(CardInfo? info) =>
            info != null && (info.Text.Contains("damage to both players") || info.Text.Contains("both players take damage")
                             || info.Text.Contains("take damage equal to its"));

        /// <summary>A monster that stops its controller from Special Summoning while it's on the field (Acid Golem).</summary>
        public static bool LocksSpecialSummons(CardInfo? info) => info != null && info.Text.Contains("You cannot Special Summon any monsters");

        /// <summary>The archetype this card insists on (it destroys itself when you control any other monster), or null.</summary>
        public static string? OnlyWith(CardInfo? info)
        {
            if (info == null) return null;
            var m = Outsider.Match(info.Text);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Whether a card counts as a member of an archetype named in text ("Burning Abyss").</summary>
        public static bool IsMember(CardInfo info, string archetype) => info.Name.Contains(archetype);
    }
}
