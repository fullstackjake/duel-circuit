using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.Presentation
{
    public enum HistoryKind { Turn, Summon, Activate, Set, Attack, Destroyed, Banished, Negated, Damage, Recover, Win }

    /// <summary>One thing that happened, short enough for a tile or a row.</summary>
    public sealed class HistoryEntry
    {
        public int Turn;
        /// <summary>Who did it (or, for damage and destroyed cards, who it happened to).</summary>
        public int Player;
        public HistoryKind Kind;
        /// <summary>The card (0: not public, such as an opponent's set card).</summary>
        public uint Code;
        public string Text = "";
        /// <summary>Damage or LP gained.</summary>
        public int Amount;
    }

    /// <summary>
    /// The duel as a list of moments for the history strip and drawer: turns, summons, activations (with their
    /// targets), sets, attacks, cards destroyed or banished from the field, negations and LP changes. Only public
    /// information: a hidden player's set cards stay unnamed. It also calls out the plays a viewer could miss
    /// (<see cref="Notable"/>): the opponent's activations, the viewer's cards negated, destroyed or banished.
    /// </summary>
    public sealed class DuelHistory
    {
        public const int MaxEntries = 600;

        private readonly ICardDataProvider _cards;
        private readonly string[] _names;
        private readonly ChainTracker _chain = new ChainTracker();
        private readonly List<HistoryEntry> _entries = new List<HistoryEntry>();
        private readonly Dictionary<int, HistoryEntry> _linkEntries = new Dictionary<int, HistoryEntry>();
        private int _turn;

        /// <summary>The seat whose hand and set cards are hidden (-1: none).</summary>
        public int HiddenPlayer { get; set; } = -1;
        /// <summary>The seat watching from (the player): notable plays are told from their side. -1: no one.</summary>
        public int Viewer { get; set; } = -1;
        /// <summary>Names a place on the field (the core sends only where): its card if public, else 0.</summary>
        public Func<CardRef, uint>? CodeAt
        {
            get => _chain.CodeAt;
            set => _chain.CodeAt = value;
        }

        /// <summary>Raised for each new entry.</summary>
        public event Action<HistoryEntry>? Added;
        /// <summary>A play the viewer could miss, as a short sentence ("Rival activates Ash Blossom in response to your search").</summary>
        public event Action<HistoryKind, uint, string>? Notable;

        public IReadOnlyList<HistoryEntry> Entries => _entries;
        /// <summary>Counts every entry ever added (the list drops the oldest past MaxEntries), for "anything new?".</summary>
        public int Version { get; private set; }

        public DuelHistory(ICardDataProvider cards, string name0, string name1)
        {
            _cards = cards;
            _names = new[] { name0, name1 };
        }

        public void SetName(int player, string name)
        {
            if (player >= 0 && player < 2) _names[player] = name;
        }

        public string Name(int player) => player >= 0 && player < 2 ? _names[player] : "";
        /// <summary>"Gus activates" but "You activate".</summary>
        private string Does(int player, string verb) => Name(player) == "You" && verb.EndsWith("s") ? $"You {verb.Substring(0, verb.Length - 1)}" : $"{Name(player)} {verb}";

        /// <summary>Whether the viewer's card leaving the field is worth a message: not when their own effect did it.</summary>
        private bool ByOpponent() => _chain.ResolvingLink is not { } link || link.Controller != Viewer;

        private string Card(uint code) => code == 0 ? "a set card" : _cards.Get(code)?.Name ?? "a card";
        private string Your(int player) => Viewer >= 0 ? (player == Viewer ? "your" : $"{Name(player)}'s") : $"{Name(player)}'s";

        private HistoryEntry Add(HistoryKind kind, int player, uint code, string text, int amount = 0)
        {
            var e = new HistoryEntry { Turn = _turn, Player = player, Kind = kind, Code = code, Text = text, Amount = amount };
            _entries.Add(e);
            if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            Version++;
            Added?.Invoke(e);
            return e;
        }

        public void Handle(byte[] message)
        {
            _chain.Handle(message);
            var r = new MessageReader(message, 1);
            switch (message[0])
            {
                case Msg.NewTurn:
                {
                    _turn++;
                    int player = r.U8() & 1;
                    Add(HistoryKind.Turn, player, 0, $"Turn {_turn}: {Name(player)}");
                    break;
                }
                case Msg.Summoning:
                case Msg.SpSummoning:
                case Msg.FlipSummoning:
                {
                    var card = r.LocInfo(r.U32());
                    var verb = message[0] == Msg.Summoning ? "summons" : message[0] == Msg.SpSummoning ? "Special Summons" : "Flip Summons";
                    Add(HistoryKind.Summon, card.Controller, card.Code, $"{Does(card.Controller, verb)} {Card(card.Code)}");
                    break;
                }
                case Msg.Set:
                {
                    var code = r.U32();
                    var card = r.LocInfo(code);
                    bool hidden = card.Controller == HiddenPlayer;
                    bool monster = (card.Location & 0x7f) == Location.MZone;
                    Add(HistoryKind.Set, card.Controller, hidden ? 0 : card.Code,
                        hidden ? $"{Does(card.Controller, "sets")} {(monster ? "a monster" : "a card")}" : $"{Does(card.Controller, "sets")} {Card(card.Code)}");
                    break;
                }
                case Msg.Chaining:
                {
                    var link = _chain.Links.LastOrDefault();
                    if (link == null) break;
                    var e = Add(HistoryKind.Activate, link.Controller, link.Code, $"{Does(link.Controller, "activates")} {Card(link.Code)}");
                    _linkEntries[link.Number] = e;
                    break;
                }
                case ChainTracker.MsgChained:
                {
                    // The link's costs and targets are known now: name the targets, and tell the viewer about the
                    // opponent's plays.
                    var link = _chain.Links.LastOrDefault();
                    if (link == null || !_linkEntries.TryGetValue(link.Number, out var e)) break;
                    if (link.Targets.Count > 0)
                        e.Text += " → " + string.Join(", ", link.Targets.Select(t => t.Code != 0 ? Card(t.Code) : "a set card"));
                    if (Viewer >= 0 && link.Controller != Viewer)
                    {
                        var text = $"{Name(link.Controller)} activates {Card(link.Code)}";
                        var answered = _chain.Links.LastOrDefault(l => l.Number < link.Number && l.Controller == Viewer);
                        if (answered != null) text += $" in response to your {Card(answered.Code)}";
                        var mine = link.Targets.Where(t => t.Controller == Viewer).ToList();
                        if (mine.Count > 0) text += $", targeting your {string.Join(", ", mine.Select(t => t.Code != 0 ? Card(t.Code) : "set card"))}";
                        Notable?.Invoke(HistoryKind.Activate, link.Code, text);
                    }
                    break;
                }
                case Msg.ChainNegated:
                case Msg.ChainDisabled:
                {
                    var link = _chain.Links.FirstOrDefault(l => l.Number == message[1]);
                    if (link == null) break;
                    var by = link.NegatedBy > 0 ? _chain.Links.FirstOrDefault(l => l.Number == link.NegatedBy) : null;
                    var what = message[0] == Msg.ChainNegated ? "is negated" : "has its effect negated";
                    Add(HistoryKind.Negated, link.Controller, link.Code, $"{Card(link.Code)} {what}{(by != null ? $" by {Card(by.Code)}" : "")}");
                    if (Viewer >= 0 && link.Controller == Viewer)
                        Notable?.Invoke(HistoryKind.Negated, link.Code, $"Your {Card(link.Code)} {what}{(by != null ? $" by {Card(by.Code)}" : "")}");
                    break;
                }
                case Msg.Attack:
                {
                    var attacker = r.LocInfo(0);
                    var target = r.LocInfo(0);
                    var a = CodeAt?.Invoke(attacker) ?? 0;
                    var t = target.Location == 0 ? 0 : CodeAt?.Invoke(target) ?? 0;
                    Add(HistoryKind.Attack, attacker.Controller, a, target.Location == 0
                        ? $"{Card(a)} attacks directly"
                        : $"{Card(a)} attacks {(t != 0 ? Card(t) : "a set monster")}");
                    break;
                }
                case Msg.Move:
                {
                    var code = r.U32();
                    var from = r.LocInfo(code);
                    var to = r.LocInfo(code);
                    uint fromLoc = from.Location & 0x7f, toLoc = to.Location & 0x7f;
                    if (fromLoc != Location.MZone && fromLoc != Location.SZone) break;
                    // A card sent from the field: destroyed (or sent to the GY), or banished. Overlay materials and
                    // a Spell/Trap going to the GY after it resolved are routine, and left out.
                    if ((from.Location & 0x80) != 0) break;
                    if (toLoc == Location.Removed)
                    {
                        Add(HistoryKind.Banished, from.Controller, code, $"{Card(code)} is banished");
                        if (Viewer >= 0 && from.Controller == Viewer && ByOpponent()) Notable?.Invoke(HistoryKind.Banished, code, $"Your {Card(code)} is banished");
                    }
                    else if (toLoc == Location.Grave && (r.AtEnd ? 0 : r.U32() & ReasonDestroy) != 0)
                    {
                        Add(HistoryKind.Destroyed, from.Controller, code, $"{Card(code)} is destroyed");
                        if (Viewer >= 0 && from.Controller == Viewer && ByOpponent()) Notable?.Invoke(HistoryKind.Destroyed, code, $"Your {Card(code)} is destroyed");
                    }
                    break;
                }
                case Msg.Damage:
                {
                    int player = r.U8() & 1;
                    int amount = (int)r.U32();
                    Add(HistoryKind.Damage, player, 0, $"{Does(player, "takes")} {amount} damage", amount);
                    break;
                }
                case Msg.Recover:
                {
                    int player = r.U8() & 1;
                    int amount = (int)r.U32();
                    Add(HistoryKind.Recover, player, 0, $"{Does(player, "gains")} {amount} LP", amount);
                    break;
                }
                case Msg.Win:
                {
                    int winner = r.U8();
                    Add(HistoryKind.Win, winner, 0, winner > 1 ? "The duel ends in a draw" : $"{Does(winner, "wins")}");
                    break;
                }
            }
        }

        private const uint ReasonDestroy = 0x1;
    }
}
