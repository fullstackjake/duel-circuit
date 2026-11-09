namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Thunder Dragon (2018): the Thunder Dragons discard themselves for value (Thunder Dragon adds two more, Dragondark
    /// adds a copy, Dragonhawk revives one, Dragonroar returns one) and float when banished or sent from the field.
    /// Batteryman Solar sends a Thunder to the GY to start. Once a Thunder has used its effect in the hand, Thunder Dragon
    /// Colossus comes out by Tributing a Thunder; Instant Fusion makes Kaminari Attack, which becomes Thunder Dragon Titan.
    /// </summary>
    public sealed class ThunderDragonPlaybook : Playbook
    {
        private const uint Dragonroar = 29596581, InstantFusion = 1845204, Sarcophagus = 75500286, ThunderDragon = 31786629,
            ThunderDragonFusion = 95238394, Dragondark = 56713174, Diabolos = 50383626, Dragonhawk = 83107873, Allure = 1475311,
            Dragonmatrix = 20318029, Envoy = 72989439, Solar = 44586426, ChaosEmperor = 4538826,
            KaminariAttack = 9653271, Titan = 41685633, Colossus = 15291624, SummerSummoner = 38406364, Bomber = 5821478,
            Linkuriboh = 41999284, Sorceress = 61665245, Borrelsword = 85289965;

        private static readonly uint[] ThunderDragons = { ThunderDragon, Dragonroar, Dragondark, Dragonhawk, Dragonmatrix };

        public override string Deck => "thunder-dragon";

        public ThunderDragonPlaybook()
        {
            Activate("Thunder Dragon adds two more", ThunderDragon, () => V.InDeck(ThunderDragon) > 0);
            Activate("Thunder Dragondark adds a copy", Dragondark, () => V.InDeck(Dragondark) > 0);
            NormalSummon("Batteryman Solar sends a Thunder", Solar);
            Activate("Thunder Dragonhawk revives a Thunder Dragon", Dragonhawk, () => V.InGraveyardAny(ThunderDragons) > 0);
            Activate("Thunder Dragonroar returns a Thunder Dragon", Dragonroar, () => V.InGraveyardAny(ThunderDragons) > 0);
            Activate("Instant Fusion makes Kaminari Attack", InstantFusion, () => V.InHandAny(ThunderDragons));
            SpecialSummon("a Thunder Fusion into Thunder Dragon Titan", Titan);
            SpecialSummon("Tribute a Thunder for Thunder Dragon Colossus", Colossus, () => V.OnField(Colossus) == 0 || V.MyMonsterCount >= 2);
            Activate("Thunder Dragon Fusion", ThunderDragonFusion, () => V.OnField(Titan) == 0);
            Activate("Thunder Dragon Fusion adds a Thunder", ThunderDragonFusion, perTurn: 2);

            HoldActivation(ChaosEmperor, () => V.OpponentCardCount < 3);
            HoldSpecialSummon(Linkuriboh);
            HoldSpecialSummon(Sorceress);
            HoldSpecialSummon(Bomber);
            HoldNormalSummon(ThunderDragon);
            HoldNormalSummon(Dragonhawk);

            Prefer(Searches, ThunderDragon, ThunderDragon);
            Prefer(Searches, Dragondark, Dragondark, ThunderDragon, Dragonhawk, Dragonroar);
            Prefer(Searches, Dragonroar, ThunderDragon, Dragonhawk, Dragondark);
            Prefer(Searches, ThunderDragonFusion, Dragonhawk, Dragondark, Solar);
            Prefer(Searches, 0, ThunderDragon, Dragondark, Dragonhawk);
            Prefer(ToGrave, Solar, Dragonroar, Dragondark, Dragonhawk, ThunderDragon);
            Prefer(SummonPicks, InstantFusion, KaminariAttack);
            Prefer(SummonPicks, ThunderDragonFusion, Titan, Colossus);
            Prefer(SummonPicks, Dragonhawk, Dragondark, Dragonroar, ThunderDragon);
            Prefer(SummonPicks, Dragonroar, Dragondark, Dragonhawk, ThunderDragon);
            // Colossus's Tribute and Titan's banish: the Thunder Dragons that float when they leave.
            Prefer(Tributes, Colossus, Dragondark, Dragonroar, Dragonhawk, Solar, Dragonmatrix);
            Prefer(Banish, Titan, Dragonroar, Dragondark, Dragonhawk, Dragonmatrix, ThunderDragon);
            Prefer(ToGrave, 0, ThunderDragon, Dragondark, Dragonhawk, Dragonroar, Dragonmatrix);
            Prefer(Materials, 0, KaminariAttack, Solar, Dragonmatrix, Dragondark, Dragonroar);
            Avoid(Materials, 0, Titan, Colossus);
        }
    }
}
