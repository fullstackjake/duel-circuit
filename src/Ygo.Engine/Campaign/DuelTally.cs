using System;
using System.Collections.Generic;
using Ygo.Engine.Presentation;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// What happened in one duel, for Gus's bounty board (campaign/bounties.txt, see <see cref="Bounty"/>): who went
    /// first, your lowest LP and the damage you took, your biggest hit, the most damage and summons in one of your turns,
    /// the traps you activated, and what took the opponent's last LP. It listens to the core's messages as the duel runs
    /// (subscribe <see cref="Observe"/> to the runner beside the board: the board's events are cleared every frame).
    /// Every fact is a public field, so a test can build a tally by hand.
    /// </summary>
    public sealed class DuelTally
    {
        /// <summary>How the opponent's last LP went.</summary>
        public enum Finish
        {
            /// <summary>The duel didn't end on the opponent's LP (deck-out, concession, Yata-lock, a loss or a draw).</summary>
            None,
            /// <summary>Battle damage from a Normal Monster.</summary>
            Normal,
            /// <summary>Battle damage from an Extra Deck monster (Fusion, Synchro, Xyz, Link).</summary>
            Extra,
            /// <summary>Battle damage from any other monster.</summary>
            Battle,
            /// <summary>Damage from an effect (burn), not battle.</summary>
            Effect,
        }

        private readonly ICardDataProvider? _cards;

        /// <summary>Your seat (your team's in a tag duel): 0 in the game.</summary>
        public readonly int Seat;
        /// <summary>The seat that took turn 1 (-1 before the first turn).</summary>
        public int FirstPlayer = -1;
        /// <summary>Turns begun so far (the turn the duel ended on, once it has).</summary>
        public int Turns;
        /// <summary>-1 while the duel runs; then the winning seat (2: a draw).</summary>
        public int Winner = -1;
        /// <summary>Your lowest LP in the duel, and all the damage you took (LP paid as a cost isn't damage).</summary>
        public int LowestLp = 8000, DamageTaken;
        /// <summary>Your biggest single hit on the opponent.</summary>
        public int BiggestHit;
        /// <summary>The most damage you dealt in one turn, and the most monsters you summoned in one turn.</summary>
        public int MostDamageInATurn, MostSummonsInATurn;
        /// <summary>Trap cards you activated (each activation counts).</summary>
        public int TrapsActivated;
        /// <summary>What took the opponent's last LP: the card (0 when unknown), and the kind.</summary>
        public uint FinisherCode;
        public Finish Finisher = Finish.None;

        public bool WentSecond => FirstPlayer >= 0 && FirstPlayer != Seat;
        public bool Won => Winner == Seat;

        private readonly int[] _lp = { 8000, 8000 };
        private int _damageThisTurn, _summonsThisTurn;
        private bool _inBattle;
        private uint _attacker;
        private readonly Dictionary<(int controller, uint sequence), uint> _monsters = new Dictionary<(int, uint), uint>();
        private readonly List<uint> _chain = new List<uint>();
        private uint _resolving;

        /// <param name="cards">Card data, to tell traps, Normal Monsters and Extra Deck monsters apart (null: those facts stay 0).</param>
        /// <param name="seat">Your seat (0 in the game; the campaign sim's player can sit at 1).</param>
        public DuelTally(ICardDataProvider? cards = null, int seat = 0)
        {
            _cards = cards;
            Seat = seat;
        }

        /// <summary>Reads one core message (subscribe it to <c>DuelRunner.MessageReceived</c>).</summary>
        public void Observe(byte[] message)
        {
            if (message.Length == 0) return;
            switch (message[0])
            {
                case Msg.Win:
                    if (message.Length > 1) Winner = message[1] > 1 ? 2 : message[1];
                    return;
                case Msg.LpUpdate:
                {
                    var r = new MessageReader(message, 1);
                    int p = r.U8();
                    if (p > 1) return;
                    _lp[p] = (int)r.U32();
                    if (p == Seat) LowestLp = Math.Min(LowestLp, _lp[p]);
                    return;
                }
                case Msg.ChainSolving:
                {
                    int link = message.Length > 1 ? message[1] : 0;
                    _resolving = link >= 1 && link <= _chain.Count ? _chain[link - 1] : 0;
                    return;
                }
                case Msg.ChainSolved:
                    _resolving = 0;
                    return;
            }
            var e = DuelEvent.Parse(message);
            if (e == null) return;
            switch (e.Kind)
            {
                case DuelEventKind.NewTurn:
                    Turns++;
                    if (FirstPlayer < 0) FirstPlayer = e.Player;
                    _damageThisTurn = _summonsThisTurn = 0;
                    _inBattle = false;
                    break;
                case DuelEventKind.Phase:
                    _inBattle = false;
                    break;
                case DuelEventKind.Move:
                    if ((e.From.Location & 0x7f) == Location.MZone && (e.From.Location & Location.Overlay) == 0) _monsters.Remove((e.From.Controller, e.From.Sequence));
                    if ((e.To.Location & 0x7f) == Location.MZone && (e.To.Location & Location.Overlay) == 0) _monsters[(e.To.Controller, e.To.Sequence)] = e.Code;
                    break;
                case DuelEventKind.NormalSummon:
                case DuelEventKind.SpecialSummon:
                case DuelEventKind.FlipSummon:
                    if (e.Code != 0 && (e.To.Location & 0x7f) == Location.MZone) _monsters[(e.To.Controller, e.To.Sequence)] = e.Code;
                    if (e.Player == Seat) MostSummonsInATurn = Math.Max(MostSummonsInATurn, ++_summonsThisTurn);
                    break;
                case DuelEventKind.Activate:
                    while (_chain.Count < e.ChainLink - 1) _chain.Add(0);
                    if (e.ChainLink >= 1 && e.ChainLink <= _chain.Count) _chain[e.ChainLink - 1] = e.Code; else _chain.Add(e.Code);
                    if (e.Player == Seat && _cards?.Get(e.Code) is CardInfo info && (info.Type & CardType.Trap) != 0) TrapsActivated++;
                    break;
                case DuelEventKind.ChainEnd:
                    _chain.Clear();
                    _resolving = 0;
                    break;
                case DuelEventKind.Attack:
                    _inBattle = true;
                    _attacker = _monsters.TryGetValue((e.From.Controller, e.From.Sequence), out var code) ? code : 0;
                    break;
                case DuelEventKind.AttackOver:
                    _inBattle = false;
                    break;
                case DuelEventKind.Damage:
                {
                    if (e.Player > 1) break;
                    _lp[e.Player] = Math.Max(0, _lp[e.Player] - e.Amount);
                    if (e.Player == Seat)
                    {
                        DamageTaken += e.Amount;
                        LowestLp = Math.Min(LowestLp, _lp[e.Player]);
                        break;
                    }
                    BiggestHit = Math.Max(BiggestHit, e.Amount);
                    MostDamageInATurn = Math.Max(MostDamageInATurn, _damageThisTurn += e.Amount);
                    if (_lp[e.Player] == 0 && Finisher == Finish.None)
                    {
                        FinisherCode = _inBattle ? _attacker : _resolving != 0 ? _resolving : _chain.Count > 0 ? _chain[_chain.Count - 1] : 0;
                        Finisher = !_inBattle ? Finish.Effect : Kind(FinisherCode);
                    }
                    break;
                }
                case DuelEventKind.Cost:
                    if (e.Player <= 1) _lp[e.Player] = Math.Max(0, _lp[e.Player] - e.Amount);
                    if (e.Player == Seat) LowestLp = Math.Min(LowestLp, _lp[e.Player]);
                    break;
                case DuelEventKind.Recover:
                    if (e.Player <= 1) _lp[e.Player] += e.Amount;
                    break;
            }
        }

        private Finish Kind(uint code)
        {
            var info = code == 0 ? null : _cards?.Get(code);
            if (info == null) return Finish.Battle;
            if (info.IsExtraDeckMonster) return Finish.Extra;
            return (info.Type & CardType.Normal) != 0 ? Finish.Normal : Finish.Battle;
        }
    }
}
