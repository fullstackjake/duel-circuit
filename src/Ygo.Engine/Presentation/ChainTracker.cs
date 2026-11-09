using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.Presentation
{
    public enum LinkState { Activating, Waiting, Resolving, Resolved, Negated, EffectNegated }

    /// <summary>One link of the chain, as the table saw it.</summary>
    public sealed class TrackedLink
    {
        public int Number;
        public uint Code;
        public int Controller;
        public uint Location;
        public uint Sequence;
        public ulong Description;
        /// <summary>Cards it targets (code 0: a face-down card).</summary>
        public List<CardRef> Targets = new List<CardRef>();
        public LinkState State = LinkState.Activating;
        /// <summary>The link whose resolution negated this one (0: none, or not known).</summary>
        public int NegatedBy;
        /// <summary>
        /// NegatedBy was worked out from targeting: the effect was negated as it resolved, and an earlier-resolved link
        /// of this chain that negates had targeted this card (Effect Veiler, Infinite Impermanence).
        /// </summary>
        public bool NegatedByTarget;
    }

    /// <summary>
    /// Follows the chain from core messages: links as they're activated (MSG_CHAINING, then costs and targets until
    /// MSG_CHAINED), their targets (MSG_BECOME_TARGET), resolution in reverse (MSG_CHAIN_SOLVING / SOLVED), and
    /// negations, tied to the link that was resolving when they happened. Only public information: what the core
    /// announces to both players.
    /// </summary>
    public sealed class ChainTracker
    {
        public const byte MsgChained = 71, MsgBecomeTarget = 83;

        private readonly List<TrackedLink> _links = new List<TrackedLink>();

        /// <summary>Names a targeted place (the core sends only where a target is): its card if public, else 0.</summary>
        public System.Func<CardRef, uint>? CodeAt { get; set; }

        /// <summary>Whether a card's text negates effects (to credit a negation at resolution); null: never credit one.</summary>
        public System.Func<uint, bool>? Negates { get; set; }
        private int _resolving;

        /// <summary>The chain being built or resolved (empty between chains).</summary>
        public IReadOnlyList<TrackedLink> Links => _links;
        /// <summary>The chain that just finished (for its summary), until the next one starts.</summary>
        public IReadOnlyList<TrackedLink> LastChain { get; private set; } = new List<TrackedLink>();

        /// <summary>Raised when a chain finishes resolving, with its links.</summary>
        public event System.Action<IReadOnlyList<TrackedLink>>? Finished;

        public void Handle(byte[] message)
        {
            var r = new MessageReader(message, 1);
            switch (message[0])
            {
                case Msg.Chaining:
                {
                    var card = r.LocInfo(r.U32());
                    r.U8(); r.U8(); r.U32(); // chain controller, location, sequence
                    var description = r.U64();
                    var number = (int)r.U32();
                    if (_links.Count == 0) LastChain = new List<TrackedLink>();
                    _links.Add(new TrackedLink { Number = number, Code = card.Code, Controller = card.Controller, Location = card.Location, Sequence = card.Sequence, Description = description });
                    break;
                }
                case MsgChained:
                    foreach (var l in _links.Where(l => l.State == LinkState.Activating)) l.State = LinkState.Waiting;
                    break;
                case MsgBecomeTarget:
                {
                    // Targets chosen while a link is being activated (or, rarely, by a resolving effect) belong to it.
                    var link = _links.LastOrDefault(l => l.State == LinkState.Activating) ?? _links.FirstOrDefault(l => l.Number == _resolving);
                    var count = r.U32();
                    for (int i = 0; i < count; i++)
                    {
                        var target = r.LocInfo(0);
                        var code = CodeAt?.Invoke(target) ?? 0;
                        link?.Targets.Add(new CardRef(code, target.Controller, target.Location, target.Sequence, target.Position));
                    }
                    break;
                }
                case Msg.ChainSolving:
                {
                    _resolving = r.U8();
                    var link = Find(_resolving);
                    if (link != null && link.State != LinkState.Negated && link.State != LinkState.EffectNegated) link.State = LinkState.Resolving;
                    break;
                }
                case Msg.ChainSolved:
                {
                    var link = Find(r.U8());
                    if (link != null && link.State == LinkState.Resolving) link.State = LinkState.Resolved;
                    break;
                }
                case Msg.ChainNegated:
                case Msg.ChainDisabled:
                {
                    var link = Find(r.U8());
                    if (link == null) break;
                    link.State = message[0] == Msg.ChainNegated ? LinkState.Negated : LinkState.EffectNegated;
                    if (_resolving != 0 && _resolving != link.Number) link.NegatedBy = _resolving;
                    else if (_resolving == link.Number) CreditByTarget(link);
                    break;
                }
                case Msg.ChainEnd:
                    if (_links.Count > 0)
                    {
                        LastChain = _links.ToList();
                        Finished?.Invoke(LastChain);
                    }
                    _links.Clear();
                    _resolving = 0;
                    break;
            }
        }

        private TrackedLink? Find(int number) => _links.FirstOrDefault(l => l.Number == number);

        /// <summary>The link resolving right now, or null.</summary>
        public TrackedLink? ResolvingLink => _resolving == 0 ? null : Find(_resolving);

        /// <summary>
        /// A link negated as it resolves (no other link resolving at that moment) is credited to the card that did it
        /// only on firm evidence: exactly one later link of this chain resolved, negates effects, and targeted this
        /// link's card where it was activated. Otherwise it stays uncredited.
        /// </summary>
        private void CreditByTarget(TrackedLink victim)
        {
            if (Negates == null || (victim.Location & 0x7f) is not (Ygo.Engine.Location.MZone or Ygo.Engine.Location.SZone)) return;
            var culprits = _links.Where(l => l.Number > victim.Number && l.State == LinkState.Resolved && Negates(l.Code)
                && l.Targets.Any(t => t.Controller == victim.Controller && (t.Location & 0x7f) == (victim.Location & 0x7f) && t.Sequence == victim.Sequence)).ToList();
            if (culprits.Count != 1) return;
            victim.NegatedBy = culprits[0].Number;
            victim.NegatedByTarget = true;
        }
    }
}
