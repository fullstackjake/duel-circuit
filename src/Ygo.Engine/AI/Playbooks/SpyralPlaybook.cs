namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// SPYRAL (2017): Terraforming finds SPYRAL Resort, which searches a SPYRAL monster. SPYRAL Super Agent Special
    /// Summons itself from the hand by guessing the type of the opponent's top card; Tough and the GEARs follow. A SPYRAL
    /// and another monster make SPYRAL Double Helix, which brings Super Agent back from anywhere, and the bodies climb into
    /// bigger Links.
    /// The Black Market's headliner "Master Plan Marlo" (docs/dev/HEADLINERS-2026-11-01.md; "#recipe master-plan-marlo") adds
    /// a route: Double Helix brings SPYRAL Master Plan from the Deck, and Master Plan's MISSION search is the payoff (the
    /// plan is on the field); Linked off, it searches Resort and an agent for the rest of the board. The spyral deck itself
    /// plays without the route.
    /// </summary>
    public sealed class SpyralPlaybook : Playbook
    {
        private const uint SuperAgent = 41091257, Tough = 20584712, Sleeper = 35699, Resort = 54631665, DoubleHelix = 1322368,
            Terraforming = 73628505, Drone = 4474060, BigRed = 30979619, LastResort = 37433748, MasterPlan = 91258852,
            QuikFix = 78080961, Rescue = 9212051, Assault = 27642961, FoolishGoods = 35726888,
            LinkSpider = 98978921, Firewall = 5043010, Borreload = 31833038, Decode = 1861629, Proxy = 22862454;

        private static readonly uint[] Spyral = { SuperAgent, Tough, Sleeper, Drone, BigRed, QuikFix };

        public override string Deck => "spyral";

        public SpyralPlaybook() : this(false) { }

        /// <summary>With <paramref name="masterPlan"/>, the headliner's route (Master Plan Marlo); without, the playbook as it always was.</summary>
        public SpyralPlaybook(bool masterPlan)
        {
            if (masterPlan) MasterPlanRoute();
            Activate("Terraforming finds SPYRAL Resort", Terraforming, () => !V.InSpellTrapZone(Resort));
            Activate("SPYRAL Resort", Resort, perTurn: 2);
            Activate("Super Agent summons itself", SuperAgent, perTurn: 2);
            NormalSummon("Tough", Tough, () => V.OnField(SuperAgent) > 0 || V.InHand(SuperAgent) == 0);
            NormalSummon("Super Agent", SuperAgent);
            Activate("SPYRAL GEAR - Drone", Drone);
            Activate("SPYRAL Quik-Fix summons a GEAR", QuikFix);
            SpecialSummon("SPYRAL + another into Double Helix", DoubleHelix, () => V.MyMonsterCount >= 2 && V.OnFieldAny(Spyral) > 0);
            Activate("Double Helix brings back Super Agent", DoubleHelix);
            SpecialSummon("climb into Decode Talker", Decode, () => V.OnField(DoubleHelix) > 0 && V.MyMonsterCount >= 2);
            SpecialSummon("climb into Firewall Dragon", Firewall, () => V.OnField(DoubleHelix) > 0 && V.MyMonsterCount >= 3);

            Prefer(Searches, Terraforming, Resort);
            Prefer(Searches, Resort, SuperAgent, Tough, Sleeper);
            Prefer(Searches, 0, SuperAgent, Resort, Tough);
            Prefer(SummonPicks, DoubleHelix, SuperAgent, Tough);
            Prefer(SummonPicks, 0, SuperAgent, Tough, BigRed, Drone);
            Prefer(Materials, DoubleHelix, SuperAgent, Drone, BigRed, Tough);
            Prefer(Materials, 0, Drone, BigRed, SuperAgent, Tough, DoubleHelix);
            HoldNormalSummon(Sleeper);
        }

        /// <summary>Two SPYRAL monsters on the field this turn, or on their way (Resort's search, Super Agent from the hand, the Normal Summon).</summary>
        private int SpyralsToCome =>
            V.OnFieldAny(Spyral) + V.OnField(DoubleHelix) * 2
            + (V.InHand(SuperAgent) > 0 || V.InSpellTrapZone(Resort) || V.InHandAny(Resort, Terraforming) ? 1 : 0)
            + (!V.NormalSummonUsed && (V.InHandAny(Tough, QuikFix, SuperAgent) || V.InSpellTrapZone(Resort) || V.InHandAny(Resort, Terraforming)) ? 1 : 0);

        private void MasterPlanRoute()
        {
            ComboRoute("Master Plan", () => V.IsMyMainPhase && V.InDeck(MasterPlan) + V.InGraveyard(MasterPlan) > 0 && V.OnField(MasterPlan) == 0
                                             && (V.OnField(DoubleHelix) > 0 || V.InExtraDeck(DoubleHelix) > 0) && SpyralsToCome >= 2)
                .AnyDecision()
                .Waiting()
                .Counts("summons", () => V.SpecialSummonsThisDuel)
                .Activate("Terraforming finds SPYRAL Resort", Terraforming, () => !V.InSpellTrapZone(Resort) && V.InHand(Resort) == 0)
                    .Pick(Searches, Resort)
                .Activate("SPYRAL Resort", Resort, () => !V.InSpellTrapZone(Resort))
                .Activate("SPYRAL Resort finds an agent", Resort, () => V.InSpellTrapZone(Resort))
                    .Pick(Searches, SuperAgent, Tough, QuikFix)
                .Activate("Super Agent summons itself", SuperAgent, () => V.OnField(DoubleHelix) == 0)
                .NormalSummon("SPYRAL Tough", Tough, () => V.OnField(DoubleHelix) == 0)
                .NormalSummon("SPYRAL Quik-Fix", QuikFix, () => V.OnField(DoubleHelix) == 0)
                .NormalSummon("Super Agent", SuperAgent, () => V.OnField(DoubleHelix) == 0)
                .SpecialSummon("SPYRAL Double Helix", DoubleHelix, () => V.OnFieldAny(Spyral) >= 2)
                    .Pick(Materials, Drone, Tough, QuikFix, SuperAgent, BigRed)
                .Activate("Double Helix calls SPYRAL Master Plan", DoubleHelix)
                    .Pick(SummonPicks, MasterPlan)
                .Activate("SPYRAL Master Plan: the plan is in motion", MasterPlan, effect: 0)
                    .Pick(Searches, Rescue, Assault)
                    .Payoff();
            // Double Helix: Special Summon what it takes (to the zone it points to), not add it to the hand.
            ChooseOption(DoubleHelix, 1);
            // Linked off, Master Plan searches Resort and an agent.
            SpecialSummon("Master Plan into Decode Talker", Decode, () => V.OnField(MasterPlan) > 0 && V.MyMonsterCount >= 2);
            Prefer(Materials, Decode, MasterPlan, DoubleHelix);
        }
    }
}
