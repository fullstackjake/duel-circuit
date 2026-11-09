namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Scientist-Turtle burn (2004, the 2004 Catapult Turtle, Sangan and Witch texts), Nyx's Table's third seat "Lab Coat
    /// Lyle" (docs/dev/HEADLINERS-2026-11-01.md). Magical Scientist pays 1000 LP to call a Level 6 or lower Fusion from the Extra
    /// Deck; Catapult Turtle, with no limit in 2004, Tributes it for half its ATK as damage. Seven calls at 2100-2200 ATK is
    /// most of a duel's Life Points without an attack. The two need two Normal Summons: one comes back from the GY (Premature
    /// Burial, Monster Reborn, Call of the Haunted), or waits a turn; Scapegoat, Sangan and Witch are the Turtle's Tribute.
    /// The first shot of a called Fusion is the payoff; the rules play the loop.
    /// </summary>
    public sealed class ScientistTurtlePlaybook : Playbook
    {
        private const uint Scientist = 34206604, Turtle = 511000228, Sangan = 511002631, Witch = 511003012, Grizzly = 57839750,
            Tomato = 83011277, Scapegoat = 73915051, SheepToken = 73915052, Premature = 70828912, Reborn = 83764718, Call = 97077563,
            PotOfGreed = 55144522, GracefulCharity = 79571449, CardDestruction = 72892473, PainfulChoice = 74191942, MagicianOfFaith = 31560081,
            DarkFlareKnight = 13722870, DarkBlade = 86805855, EmpressJudge = 15237615, PunishedEagle = 74703140, OceanSnake = 19066538,
            Sanwitch = 53539634, RabidHorseman = 94905343;

        /// <summary>The Fusions Scientist calls, best first (the most damage per 1000 LP).</summary>
        private static readonly uint[] Fusions = { DarkFlareKnight, DarkBlade, EmpressJudge, PunishedEagle, OceanSnake, Sanwitch, RabidHorseman };
        private static readonly uint[] SheepTokens = { SheepToken, SheepToken + 1, SheepToken + 2, SheepToken + 3 };
        /// <summary>The Turtle's Tributes, first to last: the called Fusions, then the Sheep, the searchers (they search), the scientist last.</summary>
        private static readonly uint[] Shots = { DarkFlareKnight, DarkBlade, EmpressJudge, PunishedEagle, OceanSnake, Sanwitch, RabidHorseman,
            SheepToken, SheepToken + 1, SheepToken + 2, SheepToken + 3, Sangan, Witch, Tomato, Grizzly, MagicianOfFaith };
        private static readonly uint[] Revivers = { Premature, Reborn, Call };

        public override string Deck => "scientist-turtle";

        /// <summary>The loop: about two actions a shot.</summary>
        public override int ActionsPerPhase => 60;

        private bool Lab => V.OnField(Scientist) > 0 && V.OnField(Turtle) > 0;
        private bool CanPay => V.MyLifePoints > 1000;
        private bool Revive(uint code) => V.InGraveyard(code) > 0 && V.InHandAny(Revivers);
        /// <summary>A called Fusion is on the field to fire.</summary>
        private bool Loaded => V.OnFieldAny(Fusions) > 0;
        /// <summary>The Turtle has a Tribute for its Tribute Summon (no Sheep: they can't be Tributed for one).</summary>
        private bool TurtleFodder => V.MyMonsterCount - V.OnFieldAny(SheepTokens) > 0;

        public ScientistTurtlePlaybook()
        {
            ComboRoute("Scientist and Turtle", () => V.IsMyMainPhase && V.InExtraDeckAny(Fusions)
                                                     && (V.OnField(Scientist) > 0 || V.InHand(Scientist) > 0 && !V.NormalSummonUsed || Revive(Scientist))
                                                     && (V.OnField(Turtle) > 0 || V.InHand(Turtle) > 0 && !V.NormalSummonUsed && TurtleFodder || Revive(Turtle))
                                                     && !(V.InHand(Scientist) > 0 && V.InHand(Turtle) > 0 && V.OnField(Scientist) + V.OnField(Turtle) == 0 && !Revive(Scientist) && !Revive(Turtle)))
                .AnyDecision()
                .Waiting()
                .Counts("damage", () => -V.OpponentLifePoints)
                .Activate("Premature Burial brings back the scientist", Premature, () => V.OnField(Scientist) == 0 && V.InGraveyard(Scientist) > 0)
                    .Pick(SummonPicks, Scientist)
                .Activate("Monster Reborn brings back the scientist", Reborn, () => V.OnField(Scientist) == 0 && V.InGraveyard(Scientist) > 0)
                    .Pick(SummonPicks, Scientist)
                .Activate("Call of the Haunted brings back the scientist", Call, () => V.OnField(Scientist) == 0 && V.InGraveyard(Scientist) > 0)
                    .Pick(SummonPicks, Scientist)
                .NormalSummon("Catapult Turtle", Turtle, () => V.OnField(Turtle) == 0)
                    .Pick(Tributes, Sangan, Witch, Tomato, Grizzly, MagicianOfFaith)
                .Activate("Premature Burial brings back the turtle", Premature, () => V.OnField(Turtle) == 0 && V.InGraveyard(Turtle) > 0)
                    .Pick(SummonPicks, Turtle)
                .Activate("Monster Reborn brings back the turtle", Reborn, () => V.OnField(Turtle) == 0 && V.InGraveyard(Turtle) > 0)
                    .Pick(SummonPicks, Turtle)
                .NormalSummon("Magical Scientist", Scientist, () => V.OnField(Scientist) == 0)
                .Activate("Magical Scientist calls a Fusion", Scientist, () => Lab && CanPay && !Loaded)
                    .Pick(SummonPicks, Fusions)
                .Activate("Catapult Turtle fires the Fusion", Turtle, () => Lab && Loaded)
                    .Pick(Tributes, Shots)
                    .Keep(Tributes, Turtle, Scientist)
                    .Payoff();

            // ---- The loop: call, fire, again, while the LP lasts (the last 1000 stays).
            Activate("Catapult Turtle fires a Fusion", Turtle, () => V.OnField(Turtle) > 0 && Loaded, perTurn: 20);
            Activate("Magical Scientist calls a Fusion", Scientist, () => Lab && CanPay && !Loaded, perTurn: 20);
            // Out of LP for another call: the scientist goes last, then a Sheep for the last points.
            Activate("Catapult Turtle fires the rest", Turtle, () => Lab && !CanPay && !Loaded && V.OpponentLifePoints <= 150 * V.MyMonsterCount + 600, perTurn: 10);

            // ---- Setting it up: the searchers on the field (the Turtle's Tribute), the draws, the revivals.
            Activate("Pot of Greed", PotOfGreed);
            Activate("Graceful Charity", GracefulCharity);
            Activate("Painful Choice", PainfulChoice);
            Activate("Card Destruction", CardDestruction, () => V.InHandAny(Scientist, Turtle) == false);
            NormalSummon("Sangan waits for the turtle", Sangan, () => V.InHand(Turtle) > 0 && V.OnField(Turtle) == 0 && !TurtleFodder);
            NormalSummon("Witch waits for the turtle", Witch, () => V.InHand(Turtle) > 0 && V.OnField(Turtle) == 0 && !TurtleFodder);
            NormalSummon("Magical Scientist waits for the turtle", Scientist, () => V.OnField(Scientist) == 0 && V.InHand(Turtle) > 0 && V.OnField(Turtle) == 0);
            Activate("Scapegoat", Scapegoat, () => V.OnField(Turtle) > 0 || V.InHand(Turtle) > 0);

            // The scientist never calls without the Turtle up (the Fusion would only wait for the End Phase).
            HoldActivation(Scientist, () => V.OnField(Turtle) == 0 || !CanPay);
            // The revivals are for the lab's pieces.
            foreach (var r in Revivers) HoldActivation(r, () => !Revive(Scientist) && !Revive(Turtle));
            // The Turtle's Normal Summon and the scientist's: kept for the lab while it can come together.
            HoldNormalSummons(() => V.InHand(Turtle) > 0 && V.OnField(Turtle) == 0 && (V.OnField(Scientist) > 0 || Revive(Scientist)) && TurtleFodder);
            Boss(Turtle);

            Prefer(SummonPicks, Scientist, Fusions);
            Prefer(SummonPicks, 0, Scientist, Turtle);
            Prefer(Tributes, 0, Shots);
            Avoid(Tributes, 0, Turtle, Scientist);
            Prefer(Searches, 0, Scientist, Turtle, Sangan, Witch);
            Prefer(new[] { DuelContext.HintDiscard, DuelContext.HintToGrave }, 0, Scientist, Tomato, Grizzly);
            Avoid(new[] { DuelContext.HintDiscard, DuelContext.HintToGrave }, 0, Turtle, Premature, Reborn, Call);
        }
    }
}
