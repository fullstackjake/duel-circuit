using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.AI
{
    /// <summary>
    /// Taxing negators: an opposing monster that negates the first activation of each chain and pays for it out of its own
    /// stats, until it can't. Light and Darkness Dragon negates every Spell/Trap Card activation and monster effect (its
    /// controller's too), losing 500 ATK/DEF each time, and stops once its ATK or DEF is under 500; Apollousa, Bow of the
    /// Goddess negates our monster effects, losing 800 ATK each time, and stops under 800. Both work once per chain, so
    /// whatever is chained after their negate resolves.
    /// <para>
    /// Facing one, the CPU treats each activation as a tax: free bait first (a monster's own effect on the field, with no
    /// cost: only that effect's use is lost), never a cost paid into a certain negate, and a card spent only when it buys
    /// something (the negator brought under an attacker's ATK this turn, or run dry so the rest of the hand works). How
    /// much it is willing to spend otherwise follows its aggression; a cautious CPU only baits for free and waits.
    /// </para>
    /// </summary>
    public sealed partial class SmartDuelist
    {
        public const uint LightAndDarknessDragon = 47297616, Apollousa = 4280258;

        /// <summary>YGO_TAX_SENSE=0 turns this off (for measuring what it changes).</summary>
        public static readonly bool TaxSense = Environment.GetEnvironmentVariable("YGO_TAX_SENSE") != "0";

        private sealed class Taxer
        {
            public FieldCard Card = null!;
            public bool MonsterEffectsOnly;
            /// <summary>Negates it has left before its stats run out.</summary>
            public int UsesLeft;
            /// <summary>What each negate takes from it.</summary>
            public int Drain;
            public string Name = "";
        }

        /// <summary>The opposing taxing negators that would negate an activation started now (none used yet this chain).</summary>
        private List<Taxer> LiveTaxers(int me)
        {
            var list = new List<Taxer>();
            int opp = 1 - me;
            foreach (var (_, card) in Monsters(opp))
            {
                if (!card.IsFaceUp || card.IsNegated) continue;
                if (_ctx.Chain.Any(l => l.Code == card.Code && l.Controller == opp)) continue; // already used this chain
                if (card.Code == LightAndDarknessDragon)
                {
                    int uses = Math.Min(card.Attack / 500, card.Defense / 500);
                    if (uses > 0) list.Add(new Taxer { Card = card, UsesLeft = uses, Drain = 500, Name = "Light and Darkness Dragon" });
                }
                else if (card.Code == Apollousa)
                {
                    int uses = card.Attack / 800;
                    if (uses > 0) list.Add(new Taxer { Card = card, UsesLeft = uses, Drain = 800, MonsterEffectsOnly = true, Name = "Apollousa" });
                }
            }
            return list;
        }

        /// <summary>The taxer that would negate this activation as the next chain link, if any.</summary>
        private Taxer? TaxedBy(CardRef card, CardInfo info, int me)
        {
            bool monsterEffect = (info.Type & CardType.Monster) != 0 && (card.Location & Location.SZone) == 0;
            // A face-up Spell/Trap on the field using its effect isn't "activating a card": Light and Darkness Dragon
            // lets it through (Kozmotown, Area Zero, a Continuous Trap's effect).
            bool cardActivation = !monsterEffect && !((card.Location & Location.SZone) != 0
                                                      && (card.Position & (Ygo.Engine.Position.FaceUpAttack | Ygo.Engine.Position.FaceUpDefense)) != 0);
            if (!monsterEffect && !cardActivation) return null;
            return LiveTaxers(me).FirstOrDefault(t => monsterEffect || !t.MonsterEffectsOnly);
        }

        /// <summary>
        /// A cost that spends another card (a Tribute, a discard) or real LP. Not "send this card and a Spell/Trap from
        /// your hand to the GY" (Eldlich): those come back from the GY, and paying them into the negate wears it down.
        /// </summary>
        private static readonly Regex ExtraCardCost = new Regex(
            @"(Tribute (?!this card)|[Dd]iscard (?!this card)|[Pp]ay (half|[1-9]\d{3}))[^.;:]*;",
            RegexOptions.Compiled);

        /// <summary>What a negated activation of this card loses us: nothing for a field monster's cost-free effect.</summary>
        private int TaxLoss(CardRef card, CardInfo info)
        {
            int cost = ExtraCardCost.IsMatch(info.Text) ? 1000 : 0;
            bool monster = (info.Type & CardType.Monster) != 0;
            if (monster && (card.Location & Location.MZone) != 0) return cost;          // the monster stays
            if (monster && (card.Location & (Location.Grave | Location.Removed)) != 0) return 300 + cost;
            return CardValue(info, null, inHand: card.Location == Location.Hand) + cost; // the card is gone
        }

        /// <summary>
        /// Wearing it down gets it under our best attacker this turn: this negate and at most one more (Light and
        /// Darkness Dragon, whose stats are what a battle uses; Apollousa's ATK drops too).
        /// </summary>
        private bool KillPlan(Taxer t, int me)
        {
            if (Board.TurnPlayer != me || Board.Phase != Phase.Main1 || Board.Turn <= 1) return false;
            int best = Monsters(me).Where(m => m.card.IsFaceUp && !m.card.IsDefense).Select(m => m.card.Attack).DefaultIfEmpty(0).Max();
            int wall = t.Card.IsDefense ? t.Card.Defense : t.Card.Attack;
            return best > 0 && wall >= best && wall - 2 * t.Drain < best;
        }

        /// <summary>
        /// Why an activation into a taxing negator would be a waste, or null. A negator with three or four negates is
        /// worth trading a card a negate for (holding everything only hands it the duel), so the waste is paying more:
        /// a cost that spends another card or real LP into a certain negate. Unless the negate is its last, or wearing
        /// it down puts it under our attacker this turn. A cautious CPU also keeps its better cards back while the
        /// negator could stop everything it has to play.
        /// </summary>
        private string? TaxBlunder(CardRef card, CardInfo info, int me, IEnumerable<CardRef> available)
        {
            if (!TaxSense) return null;
            if (TaxedBy(card, info, me) is not Taxer t) return null;
            if (t.UsesLeft == 1 || KillPlan(t, me)) return null;
            string why = $"{t.Name} would negate {info.Name} ({t.UsesLeft} negate{(t.UsesLeft == 1 ? "" : "s")} left)";
            if (ExtraCardCost.IsMatch(info.Text)) return why + ", and its cost is paid anyway";
            if (_style.Aggression < 0.35 && TaxLoss(card, info) > 1000)
            {
                int taxed = available.Count(c => _cards.Get(c.Code) is CardInfo i && TaxedBy(c, i, me) != null);
                if (taxed <= t.UsesLeft) return why;
            }
            return null;
        }

        /// <summary>
        /// Free bait for a taxing negator, as a Main Phase action, before anything that matters: a field monster's
        /// cost-free effect it would negate (only that effect's use is lost), when we hold other plays it would stop.
        /// Null when there's nothing free to feed it or no reason to.
        /// </summary>
        private byte[]? TaxBait(IdlePrompt p, int me, bool dryRun, out string what)
        {
            what = "";
            if (!UseRulesSense || !TaxSense) return null;
            var taxers = LiveTaxers(me);
            if (taxers.Count == 0) return null;
            int bait = -1, taxed = 0;
            for (int i = 0; i < p.Activatable.Count; i++)
            {
                var o = p.Activatable[i];
                if (_cards.Get(o.Card.Code) is not CardInfo info || TaxedBy(o.Card, info, me) is null) continue;
                taxed++;
                if (bait >= 0 || TaxLoss(o.Card, info) != 0 || CostBlunder(info, me) != null) continue;
                if (_tries.TryGetValue($"bait:{o.Card.Code}:{o.Description}", out var n) && n >= 1) continue;
                bait = i;
            }
            if (bait < 0 || taxed < 2 && !KillPlan(taxers[0], me)) return null;
            var chosen = p.Activatable[bait];
            if (!dryRun) _tries[$"bait:{chosen.Card.Code}:{chosen.Description}"] = 1;
            what = $"baits {taxers[0].Name} with {Name(chosen.Card.Code)}";
            return Responses.Idle(IdleAction.Activate, bait);
        }
    }
}
