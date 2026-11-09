using System.Collections.Generic;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.AI
{
    public sealed class ChainLink
    {
        public uint Code;
        public byte Controller;
        public uint Location;
        public ulong Description;
        /// <summary>Where the activated card is (its controller, zone and sequence): a Spell/Trap's own zone while it's on the chain.</summary>
        public CardRef Card;
        /// <summary>The cards it targets (where they were when targeted), from MSG_BECOME_TARGET while it was activated.</summary>
        public List<CardRef> Targets = new List<CardRef>();
    }

    public enum DuelEventKind { None, Summon, SpecialSummon, FlipSummon, Activation, Attack }

    /// <summary>
    /// Shared memory for bots: watches the message stream and remembers what a player at the
    /// table would know (the chain, the last thing each player did, what a selection is for).
    /// The board comes from live core queries; bots must only read opponent cards that are face-up.
    /// </summary>
    public sealed class DuelContext
    {
        // Selection hint ids from CardScripts constant.lua (HINTMSG_*).
        public const ulong HintRelease = 500, HintDiscard = 501, HintDestroy = 502, HintRemove = 503,
            HintToGrave = 504, HintReturnToHand = 505, HintAddToHand = 506, HintToDeck = 507, HintSpSummon = 509,
            HintSet = 510, HintFusionMaterial = 511, HintSynchroMaterial = 512, HintXyzMaterial = 513,
            HintFaceDown = 515, HintEquip = 518, HintDetachMaterial = 519, HintControl = 520, HintToField = 527,
            HintTribute = 531, HintDeattachFrom = 532, HintLinkMaterial = 533, HintAttackTarget = 549,
            HintTarget = 551, HintPosition = 561, HintNegate = 575, HintConfirm = 526;
        public const ulong DescDirectAttack = 31;

        private const byte HintSelectMsg = 3;

        private readonly Duel _duel;
        private readonly BoardState _board = new BoardState();
        private bool _boardDirty = true;
        private readonly ulong[] _pendingHint = new ulong[2];

        public ICardDataProvider Cards { get; }

        /// <summary>Unresolved chain links, oldest first.</summary>
        public List<ChainLink> Chain { get; } = new List<ChainLink>();
        public bool ChainResolving { get; private set; }
        /// <summary>The card whose chain link is resolving right now (so a selection can be tied to it), or 0.</summary>
        public uint ResolvingCode { get; private set; }
        /// <summary>The card being activated right now (its costs and targets are being chosen), or 0.</summary>
        public uint ActivatingCode { get; private set; }

        public DuelEventKind LastEvent { get; private set; }
        /// <summary>The battle in progress (from the attack declaration to the end of the Damage Step): the attacking
        /// monster, and the attacked one (null for a direct attack). Null outside a battle.</summary>
        public (CardRef attacker, CardRef? target)? Battle { get; private set; }
        public int LastEventPlayer { get; private set; } = -1;
        /// <summary>The card behind the last event (summoned, activated, or attacking); 0 if hidden.</summary>
        public uint LastEventCode { get; private set; }
        /// <summary>Increments with every summon / activation / attack, so a bot can respond once per event.</summary>
        public int EventSerial { get; private set; }

        /// <summary>What the current prompt for each player is selecting for (a Hint* value, 0 if unknown).</summary>
        public ulong[] PromptHint { get; } = new ulong[2];

        /// <summary>
        /// Per player: the last Battle Phase menu they got offered neither Main Phase 2 nor the End Phase, so an attack is
        /// forced (EFFECT_MUST_ATTACK: Nightmare Pain, Tenpai Dragons). Cancelling that attack's target selection only
        /// brings the same menu back: the core lets it, but nothing else can happen, so a bot that keeps cancelling loops.
        /// </summary>
        public bool[] AttackForced { get; } = new bool[2];

        public int[] SpecialSummonsThisTurn { get; } = new int[2];
        /// <summary>Normal Summons (and monster Sets) each player has made this turn.</summary>
        public int[] NormalSummonsThisTurn { get; } = new int[2];

        /// <summary>Special Summons each player has made this duel (combo meters: Headliners, phase 3).</summary>
        public int[] SpecialSummonsTotal { get; } = new int[2];
        /// <summary>Cards banished this duel, by the controller they were banished from (combo meters).</summary>
        public int[] BanishedTotal { get; } = new int[2];
        /// <summary>Chain links each player has activated this duel (ComboWatch: did the opponent act while a route played?).</summary>
        public int[] ChainLinksTotal { get; } = new int[2];

        /// <summary>
        /// Times each player has interrupted on the other player's turn with a hand trap or a negation
        /// (public information: everyone saw it), so bots can judge how likely more interruptions are.
        /// </summary>
        public int[] InterruptionsShown { get; } = new int[2];

        /// <summary>
        /// The last face-up identity of the card in each monster zone, kept while it stays on the field.
        /// A card that flips face-down (e.g. Stealth Bird) is still known to everyone at the table.
        /// </summary>
        public uint[,] RememberedMonster { get; } = new uint[2, 7];

        /// <summary>
        /// Which card is in each monster zone, as a number that stays with the card while it moves between monster zones
        /// (a zone shift, a change of control) and is new for every card that arrives from anywhere else (a summon; a
        /// Reborn Tengu replacing itself in the same zone). 0: not numbered yet (a card placed before the duel started).
        /// </summary>
        private readonly int[,] _monsterIds = new int[2, 7];
        private int _nextMonsterId;

        /// <summary>
        /// The identity of the card in this monster zone (see <see cref="_monsterIds"/>): the bot's battle memory for the
        /// turn tells cards apart by it, so a new card in a zone already used this turn isn't mistaken for the old one.
        /// Ask only for an occupied zone (a card there before the duel gets its number now).
        /// </summary>
        public int MonsterIdentity(int controller, uint sequence)
        {
            if (sequence >= 7) return 0;
            ref int id = ref _monsterIds[controller & 1, sequence];
            if (id == 0) id = ++_nextMonsterId;
            return id;
        }

        private readonly HashSet<int> _monstersActivatedThisTurn = new HashSet<int>();

        /// <summary>
        /// Whether the monster in this zone activated an effect this turn (by card, see MonsterIdentity: a second copy
        /// still has its once-per-turn effect).
        /// </summary>
        public bool MonsterActivatedThisTurn(int controller, uint sequence) => _monstersActivatedThisTurn.Contains(MonsterIdentity(controller, sequence));

        /// <summary>MSG_SWAP (ocgapi_constants.h): two cards trade places, written as code + location for each.</summary>
        private const byte MsgSwap = 55;

        public DuelContext(Duel duel, ICardDataProvider cards)
        {
            _duel = duel;
            Cards = cards;
            foreach (var p in _board.Players) p.LifePoints = (int)duel.StartingLP;
        }

        public BoardState Board
        {
            get
            {
                if (_boardDirty)
                {
                    _board.Refresh(_duel);
                    _boardDirty = false;
                }
                return _board;
            }
        }

        /// <summary>
        /// Raised after each message is taken in (this context's counts already updated): a playbook confirms its route's
        /// payoff as it resolves, and <see cref="Playbooks.ComboWatch"/> closes a route's window at the turn's end.
        /// </summary>
        public event System.Action<byte[]>? Observed;

        public void Observe(byte[] message)
        {
            ObserveMessage(message);
            Observed?.Invoke(message);
        }

        private void ObserveMessage(byte[] message)
        {
            _board.Observe(message);
            _boardDirty = true;
            var r = new MessageReader(message, 1);
            switch (message[0])
            {
                case Msg.NewTurn:
                    SpecialSummonsThisTurn[0] = SpecialSummonsThisTurn[1] = 0;
                    NormalSummonsThisTurn[0] = NormalSummonsThisTurn[1] = 0;
                    AttackForced[0] = AttackForced[1] = false;
                    Battle = null;
                    _monstersActivatedThisTurn.Clear();
                    break;
                case Msg.Hint:
                {
                    var type = r.U8();
                    var player = r.U8();
                    var data = r.U64();
                    if (type == HintSelectMsg && player < 2) _pendingHint[player] = data;
                    break;
                }
                case Msg.Move:
                {
                    var code = r.U32();
                    var from = r.LocInfo(code);
                    var to = r.LocInfo(code);
                    if (from.Location == Location.MZone && from.Sequence < 7) RememberedMonster[from.Controller & 1, from.Sequence] = 0;
                    int carried = 0;
                    if (from.Location == Location.MZone && from.Sequence < 7)
                    {
                        carried = _monsterIds[from.Controller & 1, from.Sequence];
                        _monsterIds[from.Controller & 1, from.Sequence] = 0;
                    }
                    if (to.Location == Location.MZone && to.Sequence < 7)
                        _monsterIds[to.Controller & 1, to.Sequence] = carried != 0 ? carried : ++_nextMonsterId;
                    if ((to.Location & Location.Removed) != 0 && (from.Location & Location.Removed) == 0 && from.Location != 0) BanishedTotal[from.Controller & 1]++;
                    Remember(to);
                    break;
                }
                case MsgSwap: // two cards trade places (Creature Swap): each keeps its identity
                {
                    var a = r.LocInfo(r.U32());
                    var b = r.LocInfo(r.U32());
                    if (a.Location == Location.MZone && b.Location == Location.MZone && a.Sequence < 7 && b.Sequence < 7)
                        (_monsterIds[a.Controller & 1, a.Sequence], _monsterIds[b.Controller & 1, b.Sequence])
                            = (_monsterIds[b.Controller & 1, b.Sequence], _monsterIds[a.Controller & 1, a.Sequence]);
                    break;
                }
                case Msg.PosChange:
                {
                    var code = r.U32();
                    var controller = r.U8();
                    var location = r.U8();
                    var sequence = r.U8();
                    r.U8(); // previous position
                    Remember(new CardRef(code, controller, location, sequence, r.U8()));
                    break;
                }
                case Msg.Set:
                {
                    var card = r.LocInfo(r.U32());
                    if ((card.Location & 0x7f) == Location.MZone) NormalSummonsThisTurn[card.Controller & 1]++;
                    break;
                }
                case Msg.Summoning:
                {
                    var card = r.LocInfo(r.U32());
                    Remember(card);
                    NormalSummonsThisTurn[card.Controller & 1]++;
                    Event(DuelEventKind.Summon, card.Controller, card.Code);
                    break;
                }
                case Msg.SpSummoning:
                {
                    var card = r.LocInfo(r.U32());
                    Remember(card);
                    SpecialSummonsThisTurn[card.Controller & 1]++;
                    SpecialSummonsTotal[card.Controller & 1]++;
                    Event(DuelEventKind.SpecialSummon, card.Controller, card.Code);
                    break;
                }
                case Msg.FlipSummoning:
                {
                    var card = r.LocInfo(r.U32());
                    Remember(card);
                    Event(DuelEventKind.FlipSummon, card.Controller, card.Code);
                    break;
                }
                case Msg.Attack:
                {
                    var attacker = r.LocInfo(0);
                    var target = r.LocInfo(0);
                    Battle = (attacker, (target.Location & Location.MZone) != 0 ? target : (CardRef?)null);
                    Event(DuelEventKind.Attack, attacker.Controller, 0);
                    break;
                }
                case Msg.AttackDisabled:
                case Msg.DamageStepEnd:
                case Msg.NewPhase:
                    Battle = null;
                    break;
                case Msg.Chaining:
                {
                    var code = r.U32();
                    var card = r.LocInfo(code);
                    var controller = r.U8();
                    var location = r.U8();
                    r.U32(); // triggering sequence
                    var description = r.U64();
                    if (ChainResolving) { Chain.Clear(); ChainResolving = false; }
                    Chain.Add(new ChainLink { Code = card.Code, Controller = controller, Location = location, Description = description, Card = card });
                    if (card.Location == Location.MZone && card.Sequence < 7) _monstersActivatedThisTurn.Add(MonsterIdentity(card.Controller, card.Sequence));
                    ChainLinksTotal[controller & 1]++;
                    if (controller != _board.TurnPlayer)
                    {
                        var traits = CardTraits.Of(Cards.Get(card.Code));
                        if (traits.Has(Trait.HandTrap) || traits.Has(Trait.Negate)) InterruptionsShown[controller & 1]++;
                    }
                    Event(DuelEventKind.Activation, controller, card.Code);
                    ActivatingCode = card.Code;
                    break;
                }
                case 83: // MSG_BECOME_TARGET: targets of the link being activated
                {
                    if (ActivatingCode == 0 || Chain.Count == 0) break;
                    var count = r.U32();
                    for (int i = 0; i < count; i++) Chain[Chain.Count - 1].Targets.Add(r.LocInfo(0));
                    break;
                }
                case 71: // MSG_CHAINED: the newest link's costs and targets are done
                    ActivatingCode = 0;
                    break;
                case Msg.ChainSolving:
                {
                    ChainResolving = true;
                    var link = r.U8();
                    ResolvingCode = link >= 1 && link <= Chain.Count ? Chain[link - 1].Code : 0;
                    break;
                }
                case Msg.ChainSolved:
                    ResolvingCode = 0;
                    break;
                case Msg.ChainEnd:
                    ActivatingCode = 0;
                    Chain.Clear();
                    ChainResolving = false;
                    ResolvingCode = 0;
                    break;
                default:
                    if (PromptParser.IsPrompt(message[0]) && message.Length > 1)
                    {
                        var player = message[1] & 1;
                        PromptHint[player] = _pendingHint[player];
                        _pendingHint[player] = 0;
                        if (message[0] == Msg.SelectBattleCmd && PromptParser.Parse(message) is BattlePrompt battle)
                            AttackForced[player] = !battle.CanMain2 && !battle.CanEndPhase && battle.Attackers.Count > 0;
                    }
                    break;
            }
        }

        private void Remember(CardRef card)
        {
            const uint FaceUp = 0x1 | 0x4;
            if (card.Location == Location.MZone && card.Sequence < 7 && card.Code != 0 && (card.Position & FaceUp) != 0)
                RememberedMonster[card.Controller & 1, card.Sequence] = card.Code;
        }

        private void Event(DuelEventKind kind, int player, uint code)
        {
            LastEvent = kind;
            LastEventPlayer = player;
            LastEventCode = code;
            EventSerial++;
        }
    }
}
