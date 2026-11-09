using System.Collections.Generic;
using Ygo.Engine.Protocol;

namespace Ygo.Engine
{
    /// <summary>A card's live state as reported by the core's location queries.</summary>
    public sealed class FieldCard
    {
        public uint Code;
        /// <summary>The deck slot this card was added from (see <see cref="CopyLooks"/>); 0 for tokens and cards made mid-duel.</summary>
        public uint Cover;
        public uint Position;
        public uint Type;
        public uint Level;
        public uint Rank;
        public int Attack;
        public int Defense;
        public uint LinkRating;
        public int OverlayCount;
        public bool IsPublic;
        /// <summary>The core's STATUS_ bits for the card (see <see cref="IsNegated"/>, <see cref="SetThisTurn"/>).</summary>
        public uint Status;
        /// <summary>Counters on the card, all kinds together.</summary>
        public int Counters;
        /// <summary>The monster this card is equipped to (an Equip Spell, a Union), or null.</summary>
        public (byte controller, uint location, uint sequence)? EquippedTo;
        /// <summary>The cards this card's lasting effect targets (Nightmare Wheel's monster, Spellbinding Circle's...).</summary>
        public List<(byte controller, uint location, uint sequence)> Targets = new List<(byte, uint, uint)>();

        /// <summary>A face-up card whose effects are negated right now (Effect Veiler, Skill Drain...): STATUS_DISABLED.</summary>
        public bool IsNegated => IsFaceUp && (Status & StatusDisabled) != 0;
        /// <summary>Set this turn (STATUS_SET_TURN): a Trap can't be activated until the next turn.</summary>
        public bool SetThisTurn => (Status & StatusSetTurn) != 0;
        private const uint StatusDisabled = 0x1, StatusSetTurn = 0x10;

        public bool IsFaceUp => (Position & (Position_FaceUpAttack | Position_FaceUpDefense)) != 0;
        public bool IsDefense => (Position & (Position_FaceUpDefense | Position_FaceDownDefense)) != 0;

        private const uint Position_FaceUpAttack = 0x1, Position_FaceUpDefense = 0x4, Position_FaceDownDefense = 0x8;
    }

    internal static class QueryParser
    {
        public const uint Code = 0x1, Position = 0x2, Type = 0x8, Level = 0x10, Rank = 0x20,
            Attack = 0x100, Defense = 0x200, EquipCard = 0x4000, TargetCard = 0x8000, OverlayCard = 0x10000, Counters = 0x20000, Status = 0x80000, IsPublic = 0x100000, Link = 0x800000, Cover = 0x2000000,
            End = 0x80000000;

        public const uint BoardFlags = Code | Position | Type | Level | Rank | Attack | Defense | OverlayCard | Link | Status | Counters
                                       | EquipCard | TargetCard | Cover;

        /// <summary>
        /// Parses an OCG_DuelQueryLocation buffer: u32 total size, then per slot either u16 0 (empty)
        /// or a list of [u16 size][u32 flag][payload] fields ending with QUERY_END.
        /// </summary>
        public static List<FieldCard?> ParseLocation(byte[] buffer)
        {
            var result = new List<FieldCard?>();
            var r = new MessageReader(buffer);
            if (buffer.Length < 4) return result;
            r.U32();
            while (!r.AtEnd)
            {
                var size = r.U16();
                if (size == 0)
                {
                    result.Add(null);
                    continue;
                }
                var card = new FieldCard();
                while (true)
                {
                    var fieldStart = r.Offset;
                    var flag = r.U32();
                    if (flag == End) break;
                    switch (flag)
                    {
                        case Code: card.Code = r.U32(); break;
                        case Position: card.Position = r.U32(); break;
                        case Type: card.Type = r.U32(); break;
                        case Level: card.Level = r.U32(); break;
                        case Rank: card.Rank = r.U32(); break;
                        case Attack: card.Attack = r.I32(); break;
                        case Defense: card.Defense = r.I32(); break;
                        case OverlayCard: card.OverlayCount = (int)r.U32(); break;
                        case Link: card.LinkRating = r.U32(); r.U32(); break;
                        case IsPublic: card.IsPublic = r.U8() != 0; break;
                        case Status: card.Status = r.U32(); break;
                        case Cover: card.Cover = r.U32(); break;
                        case EquipCard:
                        {
                            byte controller = r.U8();
                            uint location = r.U8(), sequence = r.U32();
                            r.U32(); // position
                            if (location != 0) card.EquippedTo = (controller, location, sequence);
                            break;
                        }
                        case TargetCard:
                        {
                            var count = r.U32();
                            for (int k = 0; k < count; k++)
                            {
                                byte controller = r.U8();
                                uint location = r.U8(), sequence = r.U32();
                                r.U32(); // position
                                card.Targets.Add((controller, location, sequence));
                            }
                            break;
                        }
                        case Counters:
                        {
                            var kinds = r.U32();
                            for (int k = 0; k < kinds; k++) card.Counters += (int)(r.U32() >> 16);
                            break;
                        }
                    }
                    // Skip whatever we did not read so unknown fields never desync the cursor.
                    r.Seek(fieldStart + size);
                    size = r.U16();
                }
                result.Add(card);
            }
            return result;
        }
    }
}
