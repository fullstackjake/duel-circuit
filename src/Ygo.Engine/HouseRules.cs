using System.Linq;
using Ygo.Engine.Protocol;

namespace Ygo.Engine
{
    /// <summary>
    /// The Yata-lock: when Yata-Garasu attacks directly and deals battle damage to a player with no cards in hand and
    /// none on the field, that player skips every Draw Phase from then on (Yata returns to hand and is summoned again
    /// each turn) and can never play another card. The duel is decided, so it ends there as a loss for them instead of
    /// being played out to a deck-out. Applies to everyone, you included.
    /// </summary>
    public sealed class YataLock
    {
        public const uint YataGarasu = 3078576;

        private readonly Duel _duel;
        private readonly DuelRunner _runner;
        private int _yataAttacker = -1;

        private YataLock(Duel duel, DuelRunner runner)
        {
            _duel = duel;
            _runner = runner;
        }

        /// <summary>Watches a duel for the lock.</summary>
        public static void Attach(DuelRunner runner, Duel duel)
        {
            var rule = new YataLock(duel, runner);
            runner.MessageReceived += rule.Handle;
        }

        private void Handle(byte[] message)
        {
            var r = new MessageReader(message, 1);
            switch (message[0])
            {
                case Msg.Attack:
                {
                    var attacker = r.LocInfo(0);
                    var target = r.LocInfo(0);
                    var monsters = _duel.QueryLocation(attacker.Controller, Location.MZone);
                    var card = attacker.Sequence < monsters.Count ? monsters[(int)attacker.Sequence] : null;
                    _yataAttacker = target.Location == 0 && card != null && card.Code == YataGarasu ? attacker.Controller : -1;
                    break;
                }
                case Msg.NewPhase:
                case Msg.NewTurn:
                    _yataAttacker = -1;
                    break;
                case Msg.Damage when _yataAttacker >= 0:
                {
                    int player = r.U8();
                    if (player != 1 - _yataAttacker) break;
                    bool Empty(uint location) => _duel.QueryLocation((byte)player, location).All(c => c == null);
                    if (Empty(Location.Hand) && Empty(Location.MZone) && Empty(Location.SZone))
                        _runner.Concede(player, DuelRunner.ReasonYataLock);
                    _yataAttacker = -1;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The Back Alley's turn cap (docs/dev/ALLEY-SPEC-2026-11-01.md section 4): turn 40 is played in full, and when turn 41
    /// would start (its Msg.NewTurn, before any prompt of that turn) the player with more LP wins; equal LP is a draw.
    /// LP are tracked from the duel's starting LP through the four LP messages, as <see cref="BoardState"/> does. A core
    /// win in the same message batch counts first (the runner defers the ending), and it sits beside <see cref="YataLock"/>.
    /// Attached only to alley duels (tables, brackets, the exhibition) and the harness's alley studies.
    /// </summary>
    public sealed class TurnCap
    {
        /// <summary>The alley's last turn (Campaign.AlleyEconomy.AlleyTurns is the knob; it starts at this).</summary>
        public const int AlleyTurns = 40;

        private readonly DuelRunner _runner;
        private readonly int[] _lp;
        private int _turn;
        private bool _fired;

        /// <summary>The last turn played in full.</summary>
        public int LastTurn { get; }
        /// <summary>
        /// Whether the cap ended the duel: it asked to, and the duel ended with <see cref="DuelRunner.ReasonTurnCap"/> (a core
        /// win in the same message batch, a deck-out at turn 41's draw, counts first and isn't the cap's).
        /// </summary>
        public bool Decided => _fired && _runner.Result.Winner >= 0 && _runner.Result.Reason == DuelRunner.ReasonTurnCap;

        private TurnCap(DuelRunner runner, Duel duel, int lastTurn)
        {
            _runner = runner;
            LastTurn = lastTurn;
            _lp = new[] { (int)duel.StartingLP, (int)duel.StartingLP };
        }

        /// <summary>Watches a duel: when turn lastTurn + 1 would start, the higher LP wins; equal LP is a draw (0: the alley's knob).</summary>
        public static TurnCap Attach(DuelRunner runner, Duel duel, int lastTurn = 0)
        {
            var rule = new TurnCap(runner, duel, lastTurn > 0 ? lastTurn : Campaign.AlleyEconomy.AlleyTurns);
            runner.MessageReceived += rule.Handle;
            return rule;
        }

        /// <summary>Both players' LP as the rule tracks them (from Damage, PayLpCost, Recover and LpUpdate, as BoardState does).</summary>
        public int Lp(int player) => _lp[player & 1];

        private void Handle(byte[] message)
        {
            var r = new MessageReader(message, 1);
            switch (message[0])
            {
                case Msg.NewTurn:
                    if (++_turn <= LastTurn || _fired) break;
                    _fired = true;
                    if (_lp[0] == _lp[1]) _runner.EndInDraw(DuelRunner.ReasonTurnCap);
                    else _runner.Concede(_lp[0] < _lp[1] ? 0 : 1, DuelRunner.ReasonTurnCap);
                    break;
                case Msg.Damage:
                case Msg.PayLpCost:
                {
                    int p = r.U8() & 1;
                    _lp[p] = System.Math.Max(0, _lp[p] - (int)r.U32());
                    break;
                }
                case Msg.Recover:
                {
                    int p = r.U8() & 1;
                    _lp[p] += (int)r.U32();
                    break;
                }
                case Msg.LpUpdate:
                {
                    int p = r.U8() & 1;
                    _lp[p] = (int)r.U32();
                    break;
                }
            }
        }
    }
}
