using System.Linq;

namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Frog FTK (2010), the Basement Game's headliner "Mass Driver Moss" (docs/dev/HEADLINERS-2026-11-01.md). Mass Driver
    /// Tributes a monster for 400 damage, with no limit. Substitoad Tributes a monster to Special Summon a Frog from the
    /// Deck, and Ronintoadin banishes a Frog from the GY (or field) to come back from the GY, neither with a limit. So with
    /// Substitoad on the field, Mass Driver up and Ronintoadin in the GY, every Frog is about 400 damage: the ones pulled
    /// from the Deck are shot, and the ones in the GY bring Ronintoadin back to be shot or Tributed again. One for One and
    /// Swap Frog (it sends Ronintoadin from the Deck) set it up; Moray of Greed and the Destructions dig for Mass Driver
    /// and fill the GY.
    /// </summary>
    public sealed class FrogFtkPlaybook : Playbook
    {
        private const uint Treeborn = 12538374, Ronintoadin = 1357146, Substitoad = 20663556, Moray = 22123627, OneForOne = 2295440,
            MassDriver = 34906152, Trunade = 42703248, Dupe = 46239604, Beelze = 49522489, Unifrog = 56052205, PoisonDraw = 56840658,
            CardDestruction = 72892473, HandDestruction = 74519184, FlipFlop = 81278754, DesFrog = 84451804, Swap = 9126351,
            Fishborg = 93369354, Salvage = 96947648;

        private static readonly uint[] Frogs = { Treeborn, Ronintoadin, Substitoad, Dupe, Beelze, Unifrog, PoisonDraw, FlipFlop, DesFrog, Swap };
        private static readonly uint[] FrogsButRonin = Frogs.Where(f => f != Ronintoadin).ToArray();
        /// <summary>Frogs worth least once the loop is running: banished first, discarded first.</summary>
        private static readonly uint[] Spare = { Unifrog, FlipFlop, Beelze, DesFrog, Treeborn, Dupe, PoisonDraw, Swap };
        private static readonly uint[] RoninThenSpare = new[] { Ronintoadin }.Concat(Spare).ToArray();
        /// <summary>What Mass Driver shoots first: the Frogs that pay when they leave the field (a draw, a search).</summary>
        private static readonly uint[] ShootOrder = { PoisonDraw, Dupe, Swap, Unifrog, FlipFlop, Beelze, DesFrog, Treeborn, Fishborg, Ronintoadin };

        public override string Deck => "frog-ftk";

        /// <summary>The loop takes about two actions per 400 damage: 40 or more for the kill.</summary>
        public override int ActionsPerPhase => 120;

        private bool DriverUp => V.InSpellTrapZone(MassDriver);
        private bool HaveDriver => DriverUp || V.InHand(MassDriver) > 0;
        private bool Engine => V.OnField(Substitoad) > 0 && DriverUp;
        private int RoninOnField => V.OnField(Ronintoadin);
        /// <summary>Frogs in the GY Ronintoadin can banish to come back.</summary>
        private int Fuel => V.InGraveyardAny(FrogsButRonin) + System.Math.Max(0, V.InGraveyard(Ronintoadin) - 1);
        private int DeckFrogs => V.InDeckAny(Frogs);
        /// <summary>Monsters Mass Driver can shoot without spending Substitoad or Ronintoadin.</summary>
        private int Shootable => V.MyMonsterCount - System.Math.Min(1, V.OnField(Substitoad)) - RoninOnField;
        /// <summary>Ronintoadin can keep coming back: on the field, or in the GY with a Frog to banish.</summary>
        private bool LoopReady => RoninOnField > 0 || V.InGraveyard(Ronintoadin) > 0 && Fuel > 0;
        /// <summary>A shot leaves Substitoad something to Tribute: another monster, or Ronintoadin able to come back.</summary>
        private bool KeepsPartner => V.OnField(Substitoad) == 0 || DeckFrogs == 0 || Shootable > 1 || LoopReady;
        /// <summary>Substitoad is up alone: it needs something else to Tribute (Tributing itself ends the loop).</summary>
        private bool SubstitoadAlone => V.OnField(Substitoad) > 0 && V.MyMonsterCount == V.OnField(Substitoad);

        public FrogFtkPlaybook()
        {
            var cost = new[] { DuelContext.HintDiscard, DuelContext.HintToGrave };
            // ---- The set-up, then the first shot with the engine assembled (the payoff: the loop is on).
            ComboRoute("Frog FTK", () => V.IsMyTurn && V.IsMyMainPhase && HaveDriver && (V.InHandAny(Substitoad, OneForOne) || V.OnField(Substitoad) > 0))
                .AnyDecision()
                .Waiting()
                .Counts("damage", () => -V.OpponentLifePoints)
                .Activate("One for One summons Substitoad", OneForOne, () => V.OnField(Substitoad) == 0 && V.InHand(Substitoad) == 0)
                    .Pick(cost, RoninThenSpare)
                    .Keep(cost, Substitoad, Swap)
                    .Pick(SummonPicks, Substitoad)
                .SpecialSummon("Swap Frog (a WATER discarded) sends Ronintoadin", Swap, () => V.InGraveyard(Ronintoadin) == 0)
                    .Pick(cost, RoninThenSpare)
                    .Keep(cost, Substitoad)
                    .Pick(ToGrave, Ronintoadin, Treeborn, Unifrog, FlipFlop)
                .NormalSummon("Substitoad", Substitoad, () => V.OnField(Substitoad) == 0)
                .Activate("Mass Driver", MassDriver, () => !DriverUp)
                .Activate("Mass Driver's first shot with the loop assembled", MassDriver, () => Engine && LoopReady && Shootable > 0, effect: 0)
                    .Pick(Tributes, ShootOrder)
                    .Keep(Tributes, Substitoad, Ronintoadin)
                    .Payoff();

            // ---- The loop. Rules are checked in order at each decision.
            const int loop = 60;
            // Shoot what isn't Substitoad or Ronintoadin, but not Substitoad's last partner before the loop can run.
            ActivateEffect("Mass Driver shoots a spare monster", MassDriver, 0, () => DriverUp && Shootable > 0 && KeepsPartner, loop);
            // Substitoad Tributes Ronintoadin for a Frog from the Deck (Ronintoadin comes back on the pulled Frog).
            Activate("Substitoad Tributes Ronintoadin for a Frog", Substitoad, () => DriverUp && RoninOnField > 0 && DeckFrogs > 0, loop);
            Activate("Ronintoadin banishes a Frog and comes back", Ronintoadin, () => V.InGraveyard(Ronintoadin) > 0 && Fuel > 0 && V.MyMonsterCount < 5, loop);
            // No pull on offer (the rules above come first): Ronintoadin is shot and comes back on the GY's Frogs.
            ActivateEffect("Mass Driver shoots Ronintoadin", MassDriver, 0, () => DriverUp && RoninOnField > 0
                                                                                 && (Fuel > 0 || DeckFrogs == 0 || V.OnField(Substitoad) == 0), loop);
            ActivateEffect("Mass Driver finishes it", MassDriver, 0, () => DriverUp && V.MyMonsterCount > 0
                                                                          && (V.OpponentLifePoints <= 400 * V.MyMonsterCount || (DeckFrogs == 0 && Fuel == 0)), loop);

            // Substitoad alone: the Normal Summon gives it a Tribute (Ronintoadin best: it comes back).
            foreach (var spare in new[] { Ronintoadin, Swap, PoisonDraw, Dupe, Treeborn, Unifrog, FlipFlop, Beelze })
                NormalSummon("a Frog for Substitoad to Tribute", spare, () => SubstitoadAlone);
            Activate("Substitoad Tributes a spare Frog", Substitoad, () => !SubstitoadAlone && DeckFrogs > 0 && (DriverUp || V.InHand(MassDriver) == 0), loop);

            // ---- Finding the pieces.
            Activate("One for One summons Substitoad", OneForOne, () => V.OnField(Substitoad) == 0);
            Activate("Moray of Greed digs for the engine", Moray, () => !HaveDriver || !V.InHandAny(Substitoad, OneForOne) && V.OnField(Substitoad) == 0);
            Activate("Hand Destruction digs and fills the GY", HandDestruction, () => !Engine && (!HaveDriver || V.OnField(Substitoad) == 0));
            Activate("Card Destruction digs and fills the GY", CardDestruction, () => !Engine && !HaveDriver && V.HandCount >= 3);
            SpecialSummon("Swap Frog sends Ronintoadin", Swap, () => V.InGraveyard(Ronintoadin) == 0 && V.InDeck(Ronintoadin) > 0);
            NormalSummon("Substitoad", Substitoad);
            NormalSummon("Swap Frog sends Ronintoadin", Swap, () => V.InHand(Substitoad) == 0);
            NormalSummon("Dupe Frog (it finds a Frog when it's shot)", Dupe, () => V.InHand(Substitoad) == 0 && HaveDriver);
            Activate("Mass Driver", MassDriver, () => !DriverUp);
            Activate("Salvage returns two Frogs", Salvage, () => V.InGraveyardAny(Substitoad, Swap, Ronintoadin) > 0);

            LeadWithRules();
            HoldActivation(Trunade, () => V.OpponentSpellTrapCount == 0);
            HoldActivation(Swap); // its return-to-hand effect; the summon's send is a trigger
            // Substitoad's pulls are the loop's fuel: none before Mass Driver is up, if it's coming this turn.
            HoldActivation(Substitoad, () => !DriverUp && V.InHand(MassDriver) > 0);
            // Substitoad Tributing itself ends the pulls; only when nothing else can feed it this turn.
            HoldActivation(Substitoad, () => SubstitoadAlone && V.InHandAny(Frogs));
            HoldActivation(HandDestruction, () => Engine);
            HoldActivation(CardDestruction, () => Engine);
            HoldActivation(Moray, () => Engine);
            // Shooting Substitoad ends the pulls: only "finishes it" does that.
            HoldActivation(Fishborg, () => Engine);

            // Costs are paid before the card is on the chain, so the bot can't tell which card asks: one order for
            // Mass Driver's and Substitoad's Tributes (by Substitoad's turn, the spares are already shot).
            Prefer(Tributes, 0, ShootOrder);
            Avoid(Tributes, 0, Substitoad);
            // Pulls: Swap Frog sends another Frog to the GY, Poison Draw Frog draws when shot, then Ronintoadin.
            Prefer(SummonPicks, Substitoad, Swap, PoisonDraw, Ronintoadin, Dupe, Treeborn, Unifrog, FlipFlop, Beelze, DesFrog);
            Prefer(Banish, 0, Spare);
            Avoid(Banish, 0, Substitoad, Ronintoadin);
            Prefer(ToGrave, Swap, Ronintoadin, Treeborn, Unifrog, FlipFlop);
            Prefer(Searches, Dupe, Substitoad, Ronintoadin, Swap, PoisonDraw);
            Prefer(cost, 0, RoninThenSpare);
            Avoid(cost, 0, MassDriver, Substitoad, OneForOne);
            Prefer(new[] { DuelContext.HintToDeck }, Moray, Unifrog, FlipFlop, Beelze, DesFrog, Treeborn, Dupe);
            Avoid(new[] { DuelContext.HintToDeck }, Moray, Substitoad, Swap);
            Prefer(new[] { DuelContext.HintAddToHand }, Salvage, Substitoad, Swap, Ronintoadin, PoisonDraw);
        }
    }
}
