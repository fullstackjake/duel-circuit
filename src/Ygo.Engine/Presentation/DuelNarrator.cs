using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.Presentation
{
    /// <summary>Turns core messages into readable duel commentary, one line at a time.</summary>
    public sealed class DuelNarrator
    {
        private readonly ICardDataProvider _cards;
        private readonly string[] _names;
        private readonly int[] _lp = { 8000, 8000 };
        private int _turn;

        public event Action<string>? Line;

        public bool ShowPhases { get; set; }
        /// <summary>Seat whose private information (drawn cards) must not be named, or -1.</summary>
        public int HiddenPlayer { get; set; } = -1;
        public int Activations { get; private set; }

        /// <summary>The chain as the table sees it (for the chain panel), fed by the same messages.</summary>
        public ChainTracker Chain { get; } = new ChainTracker();

        /// <summary>Names targeted places from what's visible on the board (see <see cref="BoardState.PublicCodeAt"/>).</summary>
        public Func<CardRef, uint>? CodeAt { get => Chain.CodeAt; set => Chain.CodeAt = value; }
        public int Summons { get; private set; }

        public DuelNarrator(ICardDataProvider cards, string player0, string player1)
        {
            _cards = cards;
            _names = new[] { player0, player1 };
            Chain.Negates = code => (_cards.Get(code)?.Text ?? "").IndexOf("negate", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Renames a player for the lines that follow (tag duels: whoever is in play for the team).</summary>
        public void SetName(int player, string name)
        {
            if (player is 0 or 1) _names[player] = name;
        }

        public void Write(string line) => Line?.Invoke(line);

        /// <summary>"Mika draws" but "You draw".</summary>
        private string Says(int player, string verb) =>
            _names[player] == "You" ? $"You {verb.Substring(0, verb.Length - 1)}" : $"{_names[player]} {verb}";

        /// <summary>Cards just added to a hand from the Deck, GY or banishment, said once the effect has shown them.</summary>
        private readonly List<(int Player, uint Code, uint From, bool Shown)> _added = new List<(int, uint, uint, bool)>();

        /// <summary>
        /// "Ryo adds Invocation from the Deck to the hand". A searched card is shown to the opponent (the effect
        /// confirms it), so it's named; one the hidden player adds without showing stays "a card".
        /// </summary>
        private void SayAdded()
        {
            if (_added.Count == 0) return;
            foreach (var group in _added.GroupBy(a => (a.Player, a.From)))
            {
                var names = group.Select(a => a.Shown || a.Player != HiddenPlayer ? Name(a.Code) : "a card").ToList();
                var from = group.Key.From == Location.Deck ? "the Deck" : group.Key.From == Location.Grave ? "the GY" : "banishment";
                Write($"  {Says(group.Key.Player, "adds")} {string.Join(", ", names)} from {from} to the hand");
            }
            _added.Clear();
        }

        public void Handle(byte[] message)
        {
            Chain.Handle(message);
            // The confirm (and shuffles) that follow a search belong to it; anything else says what was added.
            if (message[0] != Msg.Move && message[0] != Msg.ConfirmCards && message[0] != Msg.ShuffleDeck
                && message[0] != Msg.ShuffleHand && message[0] != Msg.Hint && message[0] != Msg.CardHint)
                SayAdded();
            var r = new MessageReader(message, 1);
            switch (message[0])
            {
                case Msg.Move:
                {
                    var code = r.U32();
                    var from = r.LocInfo(code);
                    var to = r.LocInfo(code);
                    uint fromLoc = from.Location & 0x7f, toLoc = to.Location & 0x7f;
                    if (toLoc == Location.Hand && (fromLoc == Location.Deck || fromLoc == Location.Grave || fromLoc == Location.Removed) && code != 0)
                        _added.Add((to.Controller, code, fromLoc, fromLoc != Location.Deck));
                    break;
                }
                case Msg.ConfirmCards:
                {
                    r.U8();
                    var count = r.U32();
                    for (int i = 0; i < count; i++)
                    {
                        var code = r.U32();
                        r.U8(); r.U8(); r.U32();
                        var at = _added.FindIndex(a => a.Code == code && !a.Shown);
                        if (at >= 0) _added[at] = (_added[at].Player, code, _added[at].From, true);
                    }
                    break;
                }
                case ChainTracker.MsgBecomeTarget:
                {
                    var link = Chain.Links.LastOrDefault(l => l.State == LinkState.Activating);
                    var count = r.U32();
                    var names = new List<string>();
                    for (int i = 0; i < count; i++)
                    {
                        var t = r.LocInfo(0);
                        var code = CodeAt?.Invoke(t) ?? 0;
                        names.Add(code != 0 ? Name(code) : (t.Location & 0x7f) == Location.Hand ? "a card in hand" : "a set card");
                    }
                    if (names.Count > 0) Write($"      targeting {string.Join(", ", names)}");
                    break;
                }
                case Msg.NewTurn:
                    _turn++;
                    Write($"\n=== Turn {_turn}: {_names[r.U8()]}  (LP {_lp[0]} / {_lp[1]}) ===");
                    break;
                case Msg.NewPhase:
                    if (ShowPhases) Write($"  -- {Phase.Name(r.U16())}");
                    break;
                case Msg.Draw:
                {
                    var player = r.U8();
                    var count = r.U32();
                    var drawn = new List<string>();
                    for (int i = 0; i < count; i++) { drawn.Add(Name(r.U32())); r.U32(); }
                    Write(player == HiddenPlayer
                        ? $"  {Says(player, "draws")} {count} card{(count == 1 ? "" : "s")}"
                        : $"  {Says(player, "draws")} {string.Join(", ", drawn)}");
                    break;
                }
                case Msg.Summoning:
                {
                    var card = r.LocInfo(r.U32());
                    Summons++;
                    Write($"  {Says(card.Controller, "Normal Summons")} {Name(card.Code)}");
                    break;
                }
                case Msg.SpSummoning:
                {
                    var card = r.LocInfo(r.U32());
                    Summons++;
                    Write($"  {Says(card.Controller, "Special Summons")} {(card.Code == 0 ? "a face-down monster" : Name(card.Code))}");
                    break;
                }
                case Msg.FlipSummoning:
                {
                    var card = r.LocInfo(r.U32());
                    Summons++;
                    Write($"  {Says(card.Controller, "Flip Summons")} {Name(card.Code)}");
                    break;
                }
                case Msg.Chaining:
                {
                    var card = r.LocInfo(r.U32());
                    r.U8(); r.U8(); r.U32(); r.U64();
                    var link = (int)r.U32();
                    Activations++;
                    Write($"  [Chain Link {link}] {Says(card.Controller, "activates")} {Name(card.Code)} (from {Location.Name(card.Location)})");
                    break;
                }
                case Msg.ChainNegated:
                case Msg.ChainDisabled:
                {
                    // Say what did it: "Solemn Judgment (Chain Link 2) negated the activation of Pot of Greed (Chain Link 1)".
                    var number = r.U8();
                    var link = Chain.Links.FirstOrDefault(l => l.Number == number);
                    var what = message[0] == Msg.ChainNegated ? "the activation of" : "the effect of";
                    var victim = link == null ? $"Chain Link {number}" : $"{Name(link.Code)} (Chain Link {number})";
                    var by = link?.NegatedBy > 0 ? Chain.Links.FirstOrDefault(l => l.Number == link.NegatedBy) : null;
                    Write(by == null ? $"  {char.ToUpperInvariant(what[0])}{what.Substring(1)} {victim} is negated"
                        : link!.NegatedByTarget ? $"  {char.ToUpperInvariant(what[0])}{what.Substring(1)} {victim} is negated: {Name(by.Code)} (Chain Link {by.Number}) targeted it"
                        : $"  {Name(by.Code)} (Chain Link {by.Number}) negated {what} {victim}");
                    break;
                }
                case Msg.ChainEnd:
                {
                    // A chain of two or more links gets a one-line summary, last link (first to resolve) first.
                    var links = Chain.LastChain;
                    if (links.Count < 2) break;
                    Write("  Chain resolved: " + string.Join(", ", links.OrderByDescending(l => l.Number).Select(l =>
                        $"{l.Number}. {Name(l.Code)} " + (l.State == LinkState.Negated ? "(negated)" : l.State == LinkState.EffectNegated ? "(effect negated)" : "(resolved)"))));
                    break;
                }
                case Msg.Attack:
                {
                    var attacker = r.LocInfo(0);
                    var target = r.LocInfo(0);
                    var who = Says(attacker.Controller, "attacks");
                    Write(target.Location == 0
                        ? $"  {who} directly (zone {attacker.Sequence + 1})"
                        : $"  {who} from zone {attacker.Sequence + 1} into zone {target.Sequence + 1}");
                    break;
                }
                case Msg.Damage:
                {
                    var player = r.U8();
                    var amount = (int)r.U32();
                    _lp[player] = Math.Max(0, _lp[player] - amount);
                    Write($"  {Says(player, "takes")} {amount} damage -> {_lp[player]} LP");
                    break;
                }
                case Msg.PayLpCost:
                {
                    var player = r.U8();
                    var amount = (int)r.U32();
                    _lp[player] = Math.Max(0, _lp[player] - amount);
                    Write($"  {Says(player, "pays")} {amount} LP -> {_lp[player]} LP");
                    break;
                }
                case Msg.Recover:
                {
                    var player = r.U8();
                    var amount = (int)r.U32();
                    _lp[player] += amount;
                    Write($"  {Says(player, "gains")} {amount} LP -> {_lp[player]} LP");
                    break;
                }
                case Msg.LpUpdate:
                {
                    var player = r.U8();
                    _lp[player] = (int)r.U32();
                    Write($"  {_names[player]}'s LP becomes {_lp[player]}");
                    break;
                }
                case Msg.Win:
                {
                    var winner = r.U8();
                    var reason = r.U8();
                    Write(reason == DuelRunner.ReasonTurnCap ? $"\n*** {TurnCapLine(winner)} ***"
                        : winner > 1 ? "\n*** The duel is a draw ***"
                        : reason == DuelRunner.ReasonYataLock ? $"\n*** {Says(winner, "wins")}! {(_names[1 - winner] == "You" ? "You are" : $"{_names[1 - winner]} is")} Yata-locked: no cards to play and no draws to come ***"
                        : reason == DuelRunner.ReasonConceded ? $"\n*** {Says(winner, "wins")}! {(_names[1 - winner] == "You" ? "You concede" : $"{_names[1 - winner]} concedes")} ***"
                        : $"\n*** {Says(winner, "wins")}! (reason {reason}) ***");
                    break;
                }
            }
        }

        private string Name(uint code) => _cards.Get(code)?.Name ?? $"#{code}";

        /// <summary>The alley's turn-cap result in a log (the turn just started is the one that never happens).</summary>
        private string TurnCapLine(int winner) => winner > 1
            ? TurnCapText(Math.Max(1, _turn - 1), null, 0, 0)
            : TurnCapText(Math.Max(1, _turn - 1), _names[winner], _lp[winner], _lp[1 - winner]);

        /// <summary>
        /// The turn-cap result line (<see cref="TurnCap"/>), for logs, replays and the results screen: "Turn 40: Three-Pot
        /// Teddy wins on life points, 3,200 to 1,900." ("You win" for "You"), or with no winner "Turn 40: level on life
        /// points. A draw."
        /// </summary>
        public static string TurnCapText(int lastTurn, string? winner, int winnerLp, int loserLp)
        {
            if (winner == null) return $"Turn {lastTurn}: level on life points. A draw.";
            string N(int lp) => lp.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            return $"Turn {lastTurn}: {(winner == "You" ? "You win" : winner + " wins")} on life points, {N(winnerLp)} to {N(loserLp)}.";
        }
    }
}
