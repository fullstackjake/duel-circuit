using Ygo.Engine.Protocol;

namespace Ygo.Engine.Presentation
{
    public enum DuelEventKind
    {
        /// <summary>A card changed place (From to To).</summary>
        Move,
        /// <summary><see cref="DuelEvent.Amount"/> cards drawn by <see cref="DuelEvent.Player"/>.</summary>
        Draw,
        NormalSummon,
        SpecialSummon,
        FlipSummon,
        /// <summary>A card or effect activated (a new chain link), at From.</summary>
        Activate,
        /// <summary>From attacks To (To.Location is 0 for a direct attack).</summary>
        Attack,
        /// <summary>Battle or effect damage: <see cref="DuelEvent.Amount"/> lost.</summary>
        Damage,
        /// <summary>LP paid as a cost.</summary>
        Cost,
        Recover,
        /// <summary>A chain link (<see cref="DuelEvent.Amount"/>) or its effect was negated.</summary>
        Negate,
        /// <summary>A new phase (<see cref="DuelEvent.Amount"/> is the phase) of <see cref="DuelEvent.Player"/>'s turn.</summary>
        Phase,
        /// <summary>A new turn for <see cref="DuelEvent.Player"/>.</summary>
        NewTurn,
        /// <summary>The chain finished resolving.</summary>
        ChainEnd,
        /// <summary>The current attack is over: its damage step ended, or it was negated.</summary>
        AttackOver,
        /// <summary><see cref="DuelEvent.Player"/>'s Deck was shuffled (the board riffles the pile).</summary>
        ShuffleDeck,
    }

    /// <summary>Something the board should animate, read from one core message.</summary>
    public sealed class DuelEvent
    {
        public DuelEventKind Kind;
        public uint Code;
        public CardRef From;
        public CardRef To;
        public int Player;
        public int Amount;
        /// <summary>Activations: the effect's description id (see DuelText.Describe), and its chain link number.</summary>
        public ulong Description;
        public int ChainLink;
        /// <summary>Moves: why the card moved (the core's REASON_ bits: 0x1 destroyed, 0x8 material, 0x20 battle...).</summary>
        public uint Reason;

        /// <summary>The event in a core message, or null for messages with nothing to animate.</summary>
        public static DuelEvent? Parse(byte[] message)
        {
            var r = new MessageReader(message, 1);
            switch (message[0])
            {
                case Msg.Move:
                {
                    var code = r.U32();
                    var from = r.LocInfo(code);
                    var to = r.LocInfo(code);
                    var reason = r.AtEnd ? 0u : r.U32();
                    return new DuelEvent { Kind = DuelEventKind.Move, Code = code, From = from, To = to, Player = to.Controller, Reason = reason };
                }
                case Msg.Draw:
                    return new DuelEvent { Kind = DuelEventKind.Draw, Player = r.U8(), Amount = (int)r.U32() };
                case Msg.Summoning:
                case Msg.SpSummoning:
                case Msg.FlipSummoning:
                {
                    var card = r.LocInfo(r.U32());
                    var kind = message[0] == Msg.Summoning ? DuelEventKind.NormalSummon
                        : message[0] == Msg.SpSummoning ? DuelEventKind.SpecialSummon : DuelEventKind.FlipSummon;
                    return new DuelEvent { Kind = kind, Code = card.Code, To = card, Player = card.Controller };
                }
                case Msg.Chaining:
                {
                    var card = r.LocInfo(r.U32());
                    r.U8(); r.U8(); r.U32(); // chain controller, location, sequence
                    var description = r.U64();
                    var link = (int)r.U32();
                    return new DuelEvent { Kind = DuelEventKind.Activate, Code = card.Code, From = card, Player = card.Controller, Description = description, ChainLink = link };
                }
                case Msg.NewPhase:
                    return new DuelEvent { Kind = DuelEventKind.Phase, Amount = r.U16() };
                case Msg.NewTurn:
                    return new DuelEvent { Kind = DuelEventKind.NewTurn, Player = r.U8() };
                case Msg.ShuffleDeck:
                    return new DuelEvent { Kind = DuelEventKind.ShuffleDeck, Player = r.U8() };
                case Msg.ChainEnd:
                    return new DuelEvent { Kind = DuelEventKind.ChainEnd };
                case Msg.Attack:
                {
                    var attacker = r.LocInfo(0);
                    var target = r.LocInfo(0);
                    return new DuelEvent { Kind = DuelEventKind.Attack, From = attacker, To = target, Player = attacker.Controller };
                }
                case Msg.DamageStepEnd:
                case Msg.AttackDisabled:
                    return new DuelEvent { Kind = DuelEventKind.AttackOver };
                case Msg.Damage:
                    return new DuelEvent { Kind = DuelEventKind.Damage, Player = r.U8(), Amount = (int)r.U32() };
                case Msg.PayLpCost:
                    return new DuelEvent { Kind = DuelEventKind.Cost, Player = r.U8(), Amount = (int)r.U32() };
                case Msg.Recover:
                    return new DuelEvent { Kind = DuelEventKind.Recover, Player = r.U8(), Amount = (int)r.U32() };
                case Msg.ChainNegated:
                case Msg.ChainDisabled:
                    // Reason 1: only the effect was negated (the activation still happened).
                    return new DuelEvent { Kind = DuelEventKind.Negate, Amount = r.U8(), Reason = message[0] == Msg.ChainDisabled ? 1u : 0u };
                default:
                    return null;
            }
        }

        public override string ToString() => $"{Kind} code={Code} from={From.Controller}/{From.Location:x}/{From.Sequence} to={To.Controller}/{To.Location:x}/{To.Sequence} p={Player} n={Amount}";
    }
}
