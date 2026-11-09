namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Branded Despia (2026): Branded Fusion fuses Fallen of Albaz with a LIGHT monster from the Deck into Albion the Branded
    /// Dragon, which fuses again (Mirrorjade banishes a monster each turn). Fallen of the White Dragon sends an Albaz Fusion
    /// to summon itself and calls an Ecclesia; Albaz, Normal Summoned, fuses with an opposing monster; Cartesia fuses on the
    /// opponent's turn. The Albaz Fusions sent to the GY pay off at the End Phase, so they are the costs. A Chaos Ritual
    /// side (Ragged Records, Light and Darkness Ritual, Black Chaos) adds more threats.
    /// </summary>
    public sealed class BrandedDespiaPlaybook : Playbook
    {
        private const uint SoldierOfLD = 70405001, MagicianOfDarkChaos = 44001993, BlackChaos = 98684220, SkullArchfiend = 24088928,
            Griffoh = 97462632, Aluber = 62962630, WhiteDragon = 73819701, Albaz = 68468459, Quem = 45883110, Cartesia = 95515789,
            IncredibleEcclesia = 55273560, DogmatikaEcclesia = 60303688, GoldenSwordsoul = 82489470, Kitt = 19304410,
            Mercourier = 19096726, LDRitual = 33599853, BrandedFusion = 44362883, HighSpirits = 29948294, NadirServant = 1984618,
            PrePreparation = 13048472, RaggedRecords = 24461358, FallenAndVirtuous = 30271097, MagicalSpring = 43455065,
            SuperPoly = 48130397, Sarcophagus = 75500286, MindShuffle = 24749710,
            ThreeChampions = 74405783, EcclesiaDarkDragon = 78397661, Albion = 87746184, Mirrorjade = 44146295, Lubellion = 70534340,
            Granguignol = 24915933, Titaniklad = 41373230, Rindbrumm = 51409648, Dogma = 76666602, Quaeritis = 72272462,
            Ash = 14558127, Nibiru = 27204311, Droll = 94145021, Ghost = 59438930,
            ShiningFlare = 87758525, Mixousia = 80843006, Norden = 17412721, Garura = 11765832,
            // No Limits (Imogen's nl-branded, docs/dev/NO-LIMITS-SPEC-2026-11-02.md 7.2): cards the as-built list doesn't run.
            Expulsion = 1041278, EclipseWyvern = 51858306, ChaosSorcerer = 9596126, ShroudedAlbion = 25451383;

        private const uint Light = 0x10, Dark = 0x20;

        private static readonly uint[] AlbazFusions = { Titaniklad, Albion, Lubellion, Rindbrumm, Mirrorjade, Dogma };

        public override string Deck => "branded-despia";

        public BrandedDespiaPlaybook()
        {
            // ---- Combo routes (turn 1, going first): the lines a player plays from each starter.
            var fusionMaterial = new[] { DuelContext.HintFusionMaterial };
            var cost = new[] { DuelContext.HintDiscard, DuelContext.HintToGrave };
            // Branded Fusion (or Aluber to search it): Albaz and a LIGHT from the Deck make Albion; Albion makes
            // Lubellion with a DARK (Aluber), Lubellion shuffles Albion and Albaz back for Mirrorjade. Then Fallen of the
            // White Dragon and Cartesia add bodies, and a Fusion sent to the GY brings Titaniklad's search at the End Phase.
            ComboRoute("Branded Fusion into Mirrorjade", () => V.IsMyTurn && V.IsFirstTurn && V.InHandAny(BrandedFusion, Aluber))
                .NormalSummon("Aluber searches Branded Fusion", Aluber, () => V.InHand(BrandedFusion) == 0)
                    .Pick(Searches, BrandedFusion, HighSpirits, FallenAndVirtuous)
                .Activate("Branded Fusion: Albaz and a LIGHT from the Deck make Albion", BrandedFusion)
                    // One list for the three Fusions in a row: Albion first, Albion makes Lubellion, Lubellion makes Mirrorjade.
                    .Pick(SummonPicks, Albion, Lubellion, Mirrorjade, Titaniklad)
                    .Pick(fusionMaterial, Albaz, IncredibleEcclesia, Cartesia, DogmatikaEcclesia, Quem, Aluber, WhiteDragon)
                    .Pick(Banish, Albaz, Aluber, Quem)
                    .Pick(new[] { DuelContext.HintToDeck }, Albion, Albaz, Aluber)
                    .Keep(cost, SuperPoly, BrandedFusion, Ash, Nibiru, Droll, Ghost)
                    .Pick(cost, MagicalSpring, LDRitual, Kitt, Mercourier, Quem, Cartesia, IncredibleEcclesia)
                .Activate("Fallen of the White Dragon summons itself", WhiteDragon, () => V.InHand(WhiteDragon) > 0)
                    .Pick(ToGrave, Titaniklad, Rindbrumm, Granguignol)
                    .Pick(SummonPicks, IncredibleEcclesia, DogmatikaEcclesia)
                .Activate("Blazing Cartesia joins", Cartesia, () => V.InHand(Cartesia) > 0)
                .Activate("The Fallen & The Virtuous revives", FallenAndVirtuous, () => V.OpponentCardCount == 0 && V.MyMonsterCount < 4)
                    .Pick(SummonPicks, Albion, Titaniklad, Albaz, IncredibleEcclesia)
                    .Pick(ToGrave, Titaniklad, Rindbrumm)
                .Set("Super Polymerization waits for their turn", SuperPoly);
            // No Branded Fusion: Fallen of the White Dragon summons itself and calls Incredible Ecclesia, which brings
            // Albaz; Blazing Cartesia (or Branded in High Spirits for it) fuses them into Albion on the field.
            ComboRoute("Fallen of the White Dragon into Albion", () => V.IsMyTurn && V.IsFirstTurn && V.InHandAny(WhiteDragon, HighSpirits) && V.InHandAny(Cartesia, WhiteDragon))
                .Activate("Branded in High Spirits finds Fallen of the White Dragon", HighSpirits, () => V.InHand(WhiteDragon) == 0 || V.InHand(Cartesia) == 0)
                    .Pick(Searches, WhiteDragon, Cartesia, Quem)
                    .Keep(new[] { DuelContext.HintConfirm }, WhiteDragon, Cartesia, Albaz, Aluber)
                    .Pick(ToGrave, Titaniklad, Granguignol, Quaeritis)
                .Activate("Fallen of the White Dragon summons itself", WhiteDragon, () => V.InHand(WhiteDragon) > 0)
                    .Pick(ToGrave, Titaniklad, Rindbrumm, Granguignol)
                    .Pick(SummonPicks, IncredibleEcclesia, DogmatikaEcclesia)
                .Activate("Incredible Ecclesia calls Albaz", IncredibleEcclesia, () => V.InDeckAny(Albaz) > 0)
                    .Pick(SummonPicks, Albaz)
                .Activate("Blazing Cartesia joins", Cartesia, () => V.InHand(Cartesia) > 0)
                .Activate("Blazing Cartesia fuses Albion", Cartesia, () => V.OnFieldAny(Albaz, WhiteDragon) > 0)
                    .Pick(SummonPicks, Albion, Lubellion, Granguignol)
                    .Pick(fusionMaterial, Albaz, WhiteDragon, IncredibleEcclesia, Quem)
                    .Pick(Banish, Albaz, WhiteDragon, Quem)
                    .Pick(new[] { DuelContext.HintToDeck }, Albion, Albaz, WhiteDragon)
                .Activate("The Fallen & The Virtuous revives", FallenAndVirtuous, () => V.OpponentCardCount == 0 && V.MyMonsterCount < 4)
                    .Pick(SummonPicks, Albion, Titaniklad, Albaz)
                .Set("Super Polymerization waits for their turn", SuperPoly);

            Activate("Branded Fusion makes Albion", BrandedFusion);
            Activate("Pre-Preparation of Rites finds a Ritual", PrePreparation);
            Activate("Ragged Records of Rites finds a monster", RaggedRecords);
            Activate("Branded in High Spirits finds Albaz", HighSpirits, () => V.IsMyTurn);
            Activate("Fallen of the White Dragon summons itself", WhiteDragon, () => V.InHand(WhiteDragon) > 0);
            NormalSummon("Fallen of Albaz fuses with an opposing monster", Albaz, () => V.OpponentMonsterCount > 0);
            NormalSummon("Aluber searches a Branded card", Aluber);
            NormalSummon("Guiding Quem sends an Albaz card", Quem);
            Activate("Blazing Cartesia joins", Cartesia, () => V.InHand(Cartesia) > 0);
            SpecialSummon("Incredible Ecclesia joins", IncredibleEcclesia);
            Activate("Incredible Ecclesia calls Albaz", IncredibleEcclesia, () => V.IsMyTurn && V.InDeckAny(Albaz) > 0);
            Activate("Dogmatika Ecclesia joins", DogmatikaEcclesia, () => V.InHand(DogmatikaEcclesia) > 0);
            Activate("Nadir Servant adds a Dogmatika", NadirServant, () => !V.InHandAny(BrandedFusion, WhiteDragon, Albaz));
            Activate("Mirrorjade banishes a monster", Mirrorjade, () => V.OpponentMonsterCount > 0);
            Activate("The Fallen & The Virtuous destroys", FallenAndVirtuous, () => V.OpponentCardCount > 0);
            Activate("Light and Darkness Ritual", LDRitual, () => V.InHandAny(SoldierOfLD, MagicianOfDarkChaos));
            SpecialSummon("Black Chaos", BlackChaos);
            Activate("Black Chaos banishes two", BlackChaos, () => V.OnField(BlackChaos) > 0 && V.OpponentCardCount > 0);
            Activate("Magician of Dark Chaos banishes", MagicianOfDarkChaos, () => V.OpponentCardCount > 0);

            HoldSpecialSummon(Norden);
            HoldSpecialSummon(Garura);
            HoldNormalSummon(BlackChaos);
            HoldNormalSummon(Griffoh);
            HoldActivation(MagicalSpring, () => V.IsMyTurn);
            // Branded in High Spirits is part of the turn's combo: not in the Draw or Standby Phase.
            HoldActivation(HighSpirits, () => !V.IsMyMainPhase);

            Prefer(SummonPicks, BrandedFusion, Albion, Lubellion, Titaniklad, Rindbrumm);
            Prefer(SummonPicks, Albion, Mirrorjade, Rindbrumm, Lubellion, Quaeritis);
            Prefer(SummonPicks, Lubellion, Mirrorjade, Rindbrumm, Titaniklad);
            Prefer(SummonPicks, Albaz, Mirrorjade, Dogma, Albion, Titaniklad);
            Prefer(SummonPicks, Cartesia, Mirrorjade, Granguignol, Albion);
            Prefer(SummonPicks, WhiteDragon, IncredibleEcclesia, DogmatikaEcclesia);
            Prefer(SummonPicks, IncredibleEcclesia, Albaz, WhiteDragon);
            Prefer(SummonPicks, LDRitual, MagicianOfDarkChaos, SoldierOfLD);
            Prefer(new[] { DuelContext.HintFusionMaterial }, 0, Albaz, WhiteDragon, Quem, IncredibleEcclesia, Cartesia, Kitt, DogmatikaEcclesia);
            // Costs that send an Albaz Fusion from the Extra Deck: the ones that pay off in the GY.
            Prefer(ToGrave, WhiteDragon, Titaniklad, Albion, Lubellion, Rindbrumm);
            Prefer(ToGrave, HighSpirits, Titaniklad, Albion, Granguignol, Quaeritis);
            Prefer(ToGrave, FallenAndVirtuous, Titaniklad, Albion, Lubellion, Rindbrumm);
            Prefer(ToGrave, NadirServant, Titaniklad, Mirrorjade, Dogma);
            Prefer(ToGrave, Mirrorjade, Titaniklad, Albion, Lubellion, Rindbrumm);
            Prefer(ToGrave, Quem, Albaz, Kitt, HighSpirits);
            Avoid(ToGrave, 0, Mirrorjade, Albion);
            Prefer(Searches, Aluber, BrandedFusion, HighSpirits, FallenAndVirtuous);
            Prefer(Searches, HighSpirits, Albaz, WhiteDragon, Quem, Aluber);
            Prefer(Searches, NadirServant, DogmatikaEcclesia, Albaz);
            Prefer(Searches, PrePreparation, LDRitual, MagicianOfDarkChaos, SoldierOfLD);
            Prefer(Searches, RaggedRecords, SkullArchfiend, Griffoh);
            Prefer(Searches, 0, BrandedFusion, Albaz, WhiteDragon, Aluber);
            Prefer(Materials, 0, Quem, Cartesia, IncredibleEcclesia, Kitt, Aluber, WhiteDragon, Albaz);
            Avoid(Materials, 0, Mirrorjade, BlackChaos, MagicianOfDarkChaos);

            // ---- No Limits (Imogen's nl-branded, 7.2): Expulsion and the Chaos picks. Additive only: every card below is one
            // the as-built branded-despia list doesn't run, so the base plays as before (7.1).
            // Branded Expulsion: set it; on their turn, Tribute an Albaz Fusion in response to their play or at their End Phase
            // to revive two non-Fusions (Mirrorjade sent to the GY wipes their monsters at the end of the turn).
            SetSpellTrap("Branded Expulsion waits for their turn", Expulsion);
            ChainWhen("Branded Expulsion Tributes an Albaz Fusion to revive two", Expulsion,
                () => !V.IsMyTurn && V.OnFieldAny(AlbazFusions) > 0 && V.RecoverableNonFusionMonsters >= 2 && V.FreeMonsterZones >= 2
                      && (V.ChainTopIsTheirs || V.IsOpponentEndPhase));
            Prefer(Tributes, Expulsion, Mirrorjade, Albion, Titaniklad, Rindbrumm, Lubellion, Dogma);
            Prefer(SummonPicks, Expulsion, Albaz, WhiteDragon, Cartesia, IncredibleEcclesia, Quem, Aluber, DogmatikaEcclesia, EclipseWyvern, ChaosSorcerer);
            // Eclipse Wyvern: the LIGHT Branded Fusion sends from the Deck; it banishes Albion the Shrouded Dragon, which comes
            // to the hand when Chaos Sorcerer banishes Wyvern from the GY.
            Prefer(new[] { DuelContext.HintFusionMaterial }, BrandedFusion, EclipseWyvern);
            Prefer(ToGrave, 0, EclipseWyvern);
            Prefer(Banish, EclipseWyvern, ShroudedAlbion);
            // Chaos Sorcerer: banish a LIGHT (Wyvern first) and a DARK from the GY to summon it; it banishes a face-up monster.
            SpecialSummon("Chaos Sorcerer banishes a LIGHT and a DARK", ChaosSorcerer,
                () => V.IsMyMainPhase && V.InHand(ChaosSorcerer) > 0 && V.FreeMonsterZones > 0
                      && V.GraveyardMonstersOfAttribute(Light) >= 1 && V.GraveyardMonstersOfAttribute(Dark) >= 1
                      && (V.OpponentMonsterCount > 0 || V.InGraveyard(EclipseWyvern) > 0));
            Prefer(Banish, ChaosSorcerer, EclipseWyvern, Aluber, Quem, DogmatikaEcclesia, Kitt);
            Avoid(Banish, ChaosSorcerer, Albaz, WhiteDragon, Cartesia, IncredibleEcclesia, ShroudedAlbion, Albion, Mirrorjade, Titaniklad);
            ActivateEffect("Chaos Sorcerer banishes a monster", ChaosSorcerer, 1, () => V.OpponentMonsterCount > 0 && !V.IsFirstTurn);
            Avoid(Materials, 0, ChaosSorcerer);
        }
    }
}
