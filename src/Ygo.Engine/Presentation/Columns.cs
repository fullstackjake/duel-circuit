using System.Text.RegularExpressions;

namespace Ygo.Engine.Presentation
{
    /// <summary>
    /// Columns (Mekk-Knights, World Legacy, Infinite Impermanence...): the five lines across the field, each a Main
    /// Monster Zone and a Spell &amp; Trap Zone of each player, facing each other, plus the Extra Monster Zone between them
    /// in the 2nd and 4th. Numbered 0-4 left to right as player 0 sees the field; player 1's zones run the other way.
    /// </summary>
    public static class Columns
    {
        public const int Count = 5;

        /// <summary>The column of a zone (0-4, from player 0's side), or null for zones in no column (Field Zone, piles).</summary>
        public static int? Of(int controller, uint location, int sequence)
        {
            location &= 0x7f;
            int column;
            if (location == Location.MZone && sequence is 5 or 6) column = sequence == 5 ? 1 : 3; // the Extra Monster Zones
            else if ((location == Location.MZone || location == Location.SZone) && sequence is >= 0 and <= 4) column = sequence;
            else if (location == Location.SZone && sequence is 6 or 7) column = sequence == 6 ? 0 : 4; // Pendulum Zones
            else return null;
            return controller == 0 ? column : 4 - column;
        }

        /// <summary>How many cards (face-up or face-down, either player's) are in each column.</summary>
        public static int[] CardsIn(BoardState board)
        {
            var counts = new int[Count];
            for (int p = 0; p < 2; p++)
            {
                var pb = board.Players[p];
                for (int s = 0; s < pb.Monsters.Count && s < 7; s++)
                    if (pb.Monsters[s] != null && Of(p, Location.MZone, s) is { } c) counts[c]++;
                for (int s = 0; s < pb.SpellTraps.Count && s < 8; s++)
                {
                    if (pb.SpellTraps[s] == null || s == 5) continue;
                    // A Pendulum Zone is the end Spell & Trap Zone: counted once if both report the card.
                    if ((s == 6 && pb.SpellTraps[0] != null) || (s == 7 && pb.SpellTraps[4] != null)) continue;
                    if (Of(p, Location.SZone, s) is { } c) counts[c]++;
                }
            }
            return counts;
        }

        private static readonly Regex MentionsColumn = new Regex(@"\bcolumns?\b", RegexOptions.IgnoreCase);
        private static readonly Regex NeedsCards = new Regex(
            @"\b(\d+|two|three|four) or more (other )?cards (are )?in (the same|that|this card's|its|their|a) column", RegexOptions.IgnoreCase);
        private static readonly Regex SummonsThere = new Regex(@"Special Summon this card \(from your hand\) in that column", RegexOptions.IgnoreCase);

        /// <summary>Whether a card's text is about columns.</summary>
        public static bool Mentions(CardInfo? info) => info != null && MentionsColumn.IsMatch(info.Text);

        /// <summary>"If 2 or more cards are in the same column": how many cards a column needs (0 when it doesn't say).</summary>
        public static int CardsNeeded(CardInfo info)
        {
            var m = NeedsCards.Match(info.Text);
            if (!m.Success) return 0;
            return m.Groups[1].Value.ToLowerInvariant() switch { "two" => 2, "three" => 3, "four" => 4, var n => int.Parse(n) };
        }

        /// <summary>Whether the card Special Summons itself from the hand into such a column (the Mekk-Knights).</summary>
        public static bool SummonsIntoColumn(CardInfo info) => SummonsThere.IsMatch(info.Text);

        /// <summary>Whether a player's Main Monster Zone in a column is free.</summary>
        public static bool MonsterZoneFree(BoardState board, int player, int column)
        {
            int seq = player == 0 ? column : 4 - column;
            var monsters = board.Players[player].Monsters;
            return seq >= monsters.Count || monsters[seq] == null;
        }
    }
}
