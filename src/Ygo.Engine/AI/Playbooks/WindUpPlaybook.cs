namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Wind-Up (2012): Wind-Up Factory searches a Wind-Up each time one's effect activates; Normal Summon Wind-Up Magician,
    /// then Wind-Up Shark joins from the hand (its own trigger), which makes Magician Special Summon Wind-Up Rat from the
    /// Deck; Rat revives another Wind-Up. The bodies become Rank 3 / Rank 4 Xyz (Carrier Zenmaity summons more Wind-Ups).
    /// Pot of Duality locks Special Summons for the turn, so it waits while a Wind-Up line is in hand.
    /// </summary>
    public sealed class WindUpPlaybook : Playbook
    {
        private const uint Rabbit = 42874792, Shark = 25484449, Rat = 57962537, Magician = 59297550, TourGuide = 10802915,
            Factory = 95714077, Duality = 98645731, Zenmaines = 78156759, Zenmaister = 68597372, Zenmaity = 81122844,
            Zenmaioh = 77334267, ShockMaster = 54719828, Leviathan = 69610924, GigaBrilliant = 47805931, AcidGolem = 81330115,
            Leviair = 95992081, Tiras = 31386180, Adreus = 94119480, Sangan = 26202165, SnowmanEater = 91133740;

        private static readonly uint[] WindUps = { Rabbit, Shark, Rat, Magician };

        public override string Deck => "wind-up";

        public WindUpPlaybook()
        {
            Activate("Wind-Up Factory", Factory, () => !V.InSpellTrapZone(Factory));
            NormalSummon("Wind-Up Magician starts the line", Magician);
            Activate("Wind-Up Shark joins from the hand", Shark, perTurn: 2);
            Activate("Wind-Up Magician summons from the Deck", Magician);
            Activate("Wind-Up Rat revives a Wind-Up", Rat, () => V.InGraveyardAny(WindUps) > 0);
            NormalSummon("Wind-Up Rat", Rat, () => V.InGraveyardAny(WindUps) > 0 || V.InHand(Shark) > 0);
            NormalSummon("Tour Guide brings a Level 3 Fiend", TourGuide, () => V.InDeckAny(Sangan, TourGuide) > 0);
            NormalSummon("Wind-Up Rabbit", Rabbit, () => V.InHand(Shark) > 0);
            Activate("Carrier Zenmaity summons a Wind-Up", Zenmaity);
            SpecialSummon("two Level 3s into Wind-Up Carrier Zenmaity", Zenmaity, () => V.InDeckAny(WindUps) > 0);
            SpecialSummon("two Level 5s into Tiras", Tiras, () => V.OpponentCardCount > 0);
            SpecialSummon("two Level 5s into Adreus", Adreus, () => V.OpponentCardCount > 0);
            SpecialSummon("two Level 4s into Number 16: Shock Master", ShockMaster, () => V.IsFirstTurn);
            SpecialSummon("two Level 4s into Wind-Up Zenmaister", Zenmaister);
            SpecialSummon("two Level 3s into Number 17: Leviathan Dragon", Leviathan, () => !V.IsFirstTurn);
            SpecialSummon("two Level 3s into Wind-Up Zenmaines", Zenmaines);

            HoldActivation(Duality, () => V.InHandAny(WindUps) || V.InSpellTrapZone(Factory));
            HoldSpecialSummon(AcidGolem);
            HoldNormalSummon(Shark, () => V.InHandAny(Magician, Rat, Rabbit));

            Prefer(Searches, Factory, Magician, Shark, Rat, Rabbit);
            Prefer(Searches, 0, Magician, Shark, Rat);
            Prefer(SummonPicks, Magician, Rat, Shark, Rabbit);
            Prefer(SummonPicks, Zenmaity, Magician, Shark, Rat, Rabbit);
            Prefer(SummonPicks, Rat, Magician, Shark, Rabbit);
            Prefer(SummonPicks, TourGuide, Sangan, TourGuide);
            Prefer(Materials, 0, Rat, Rabbit, TourGuide, Sangan, Shark, SnowmanEater);
            Avoid(Materials, 0, Magician);
        }
    }
}
