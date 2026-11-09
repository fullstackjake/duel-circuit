using System;
using System.Collections.Generic;
using System.IO;

namespace Ygo.Engine.Protocol
{
    public enum IdleAction
    {
        Summon = 0,
        SpecialSummon = 1,
        ChangePosition = 2,
        SetMonster = 3,
        SetSpellTrap = 4,
        Activate = 5,
        ToBattlePhase = 6,
        ToEndPhase = 7,
        Shuffle = 8,
    }

    public enum BattleAction
    {
        Activate = 0,
        Attack = 1,
        ToMain2 = 2,
        ToEndPhase = 3,
    }

    /// <summary>Builds the byte responses the core expects for each prompt (see playerop.cpp).</summary>
    public static class Responses
    {
        public static byte[] Int32(int value) => BitConverter.GetBytes(value);

        public static byte[] Idle(IdleAction action, int index = 0) => Int32((index << 16) | (int)action);

        public static byte[] Battle(BattleAction action, int index = 0) => Int32((index << 16) | (int)action);

        /// <summary>
        /// Out of the Battle Phase: the End Phase, else Main Phase 2. When neither is allowed (a card says monsters must
        /// attack, as Tenpai Dragon's do), the first attacker attacks, or the first effect activates.
        /// </summary>
        public static byte[] LeaveBattle(BattlePrompt p, bool endFirst = true) =>
            p.CanEndPhase && (endFirst || !p.CanMain2) ? Battle(BattleAction.ToEndPhase)
            : p.CanMain2 ? Battle(BattleAction.ToMain2)
            : p.Attackers.Count > 0 ? Battle(BattleAction.Attack, 0)
            : Battle(BattleAction.Activate, 0);

        public static byte[] YesNo(bool yes) => Int32(yes ? 1 : 0);

        /// <summary>For SelectCard, SelectTribute and SelectSum: indices into the prompt's card list.</summary>
        public static byte[] Cards(IReadOnlyList<int> indices)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(0);
            w.Write((uint)indices.Count);
            foreach (var i in indices) w.Write((uint)i);
            return ms.ToArray();
        }

        public static byte[] CancelSelection() => Int32(-1);

        /// <summary>For SelectUnselectCard. Index counts selectable cards first, then unselectable ones.</summary>
        public static byte[] PickOne(int index)
        {
            var bytes = new byte[8];
            BitConverter.GetBytes(1).CopyTo(bytes, 0);
            BitConverter.GetBytes(index).CopyTo(bytes, 4);
            return bytes;
        }

        public static byte[] NoChain() => Int32(-1);

        /// <summary>Each zone is (absolute player, location MZone/SZone, sequence).</summary>
        public static byte[] Places(IEnumerable<(byte player, uint location, byte sequence)> zones)
        {
            var bytes = new List<byte>();
            foreach (var (player, location, sequence) in zones)
            {
                bytes.Add(player);
                bytes.Add((byte)location);
                bytes.Add(sequence);
            }
            return bytes.ToArray();
        }

        public static byte[] Counters(IReadOnlyList<ushort> perCard)
        {
            var bytes = new byte[perCard.Count * 2];
            for (int i = 0; i < perCard.Count; i++)
                BitConverter.GetBytes(perCard[i]).CopyTo(bytes, i * 2);
            return bytes;
        }

        /// <summary>Accept the default order for SortCard / SortChain.</summary>
        public static byte[] DefaultSort() => new byte[] { 0xFF };

        public static byte[] Race(ulong races) => BitConverter.GetBytes(races);

        public static byte[] Attribute(uint attributes) => BitConverter.GetBytes(attributes);
    }
}
