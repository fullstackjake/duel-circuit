using System.Collections.Generic;
using System.Linq;

namespace Ygo.Engine.Presentation
{
    public enum ChangeKind { Arrived, Changed, Left }

    /// <summary>A copy of what a viewer sees of the board at one moment: each zone's card, the LP and the pile sizes.</summary>
    public sealed class BoardSnapshot
    {
        public int Turn;
        public readonly int[] LifePoints = new int[2];
        /// <summary>(player, location, sequence) -> (code, position) for each card on the field.</summary>
        public readonly Dictionary<(int, uint, int), (uint code, uint position)> Field = new Dictionary<(int, uint, int), (uint, uint)>();
        /// <summary>(player, location) -> count, for the hand, Deck, Extra Deck, GY and banished cards.</summary>
        public readonly Dictionary<(int, uint), int> Piles = new Dictionary<(int, uint), int>();

        public static BoardSnapshot Of(BoardState board)
        {
            var s = new BoardSnapshot { Turn = board.Turn };
            for (int p = 0; p < 2; p++)
            {
                var pb = board.Players[p];
                s.LifePoints[p] = pb.LifePoints;
                void Zones(List<FieldCard?> cards, uint location)
                {
                    for (int i = 0; i < cards.Count; i++)
                        if (cards[i] is FieldCard c) s.Field[(p, location, i)] = (c.Code, c.Position);
                }
                Zones(pb.Monsters, Location.MZone);
                Zones(pb.SpellTraps, Location.SZone);
                s.Piles[(p, Location.Hand)] = pb.Hand.Count;
                s.Piles[(p, Location.Deck)] = pb.DeckCount;
                s.Piles[(p, Location.Extra)] = pb.ExtraDeck.Count;
                s.Piles[(p, Location.Grave)] = pb.Graveyard.Count;
                s.Piles[(p, Location.Removed)] = pb.Banished.Count;
            }
            return s;
        }
    }

    /// <summary>
    /// What changed between two moments (a replay jump): cards that arrived in, changed (flipped or turned), or left a
    /// zone, LP before and after, and how each pile grew or shrank.
    /// </summary>
    public sealed class BoardChanges
    {
        public int TurnBefore, TurnAfter;
        public readonly int[] LifeBefore = new int[2], LifeAfter = new int[2];
        public readonly List<(int player, uint location, int sequence, ChangeKind kind, uint code)> Field =
            new List<(int, uint, int, ChangeKind, uint)>();
        /// <summary>(player, location) -> change in size, for piles that changed.</summary>
        public readonly Dictionary<(int player, uint location), int> Piles = new Dictionary<(int, uint), int>();

        public bool Any => Field.Count > 0 || Piles.Count > 0 || LifeBefore[0] != LifeAfter[0] || LifeBefore[1] != LifeAfter[1];

        public static BoardChanges Between(BoardSnapshot before, BoardState after) => Between(before, BoardSnapshot.Of(after));

        public static BoardChanges Between(BoardSnapshot before, BoardSnapshot now)
        {
            var c = new BoardChanges { TurnBefore = before.Turn, TurnAfter = now.Turn };
            for (int p = 0; p < 2; p++) { c.LifeBefore[p] = before.LifePoints[p]; c.LifeAfter[p] = now.LifePoints[p]; }
            foreach (var (place, card) in now.Field)
            {
                if (!before.Field.TryGetValue(place, out var was) || was.code != card.code)
                    c.Field.Add((place.Item1, place.Item2, place.Item3, ChangeKind.Arrived, card.code));
                else if (was.position != card.position)
                    c.Field.Add((place.Item1, place.Item2, place.Item3, ChangeKind.Changed, card.code));
            }
            foreach (var (place, card) in before.Field)
                if (!now.Field.ContainsKey(place))
                    c.Field.Add((place.Item1, place.Item2, place.Item3, ChangeKind.Left, card.code));
            foreach (var (pile, count) in now.Piles)
            {
                var delta = count - (before.Piles.TryGetValue(pile, out var was) ? was : 0);
                if (delta != 0) c.Piles[pile] = delta;
            }
            return c;
        }

        /// <summary>One line for the log: "Since turn 3: Ryo 8000 → 5200 LP · 3 cards arrived, 2 left the field".</summary>
        public string Summary(IReadOnlyList<string> names)
        {
            var parts = new List<string>();
            for (int p = 0; p < 2; p++)
                if (LifeBefore[p] != LifeAfter[p]) parts.Add($"{names[p]} {LifeBefore[p]} → {LifeAfter[p]} LP");
            int arrived = Field.Count(f => f.kind == ChangeKind.Arrived), changed = Field.Count(f => f.kind == ChangeKind.Changed),
                left = Field.Count(f => f.kind == ChangeKind.Left);
            var field = new List<string>();
            if (arrived > 0) field.Add($"{arrived} card{(arrived == 1 ? "" : "s")} arrived");
            if (changed > 0) field.Add($"{changed} changed position");
            if (left > 0) field.Add($"{left} left the field");
            if (field.Count > 0) parts.Add(string.Join(", ", field));
            var when = TurnBefore == TurnAfter ? $"Within turn {TurnAfter}" : TurnBefore > TurnAfter ? $"Back from turn {TurnBefore}" : $"Since turn {TurnBefore}";
            return parts.Count == 0 ? $"{when}: nothing on the field or LP changed." : $"{when}: {string.Join("  ·  ", parts)}.";
        }
    }
}
