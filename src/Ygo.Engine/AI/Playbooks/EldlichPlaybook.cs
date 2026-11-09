namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Eldlich (2020): a Trap deck that grinds. Cursed Eldland pays 800 LP to search Eldlich or a Golden Land Trap;
    /// Eldlixir of Black Awakening summons Eldlich from the hand or Deck; Eldlich sends itself and a Spell/Trap from the
    /// hand to send any card on the field to the GY, then comes back from the GY. Conquistador and Huaquero become Level 5
    /// LIGHT bodies (Constellar Pleiades); two Level 10 Eldlich make a Rail Cannon.
    /// </summary>
    public sealed class EldlichPlaybook : Playbook
    {
        private const uint Eldlich = 95440946, Boarder = 15397015, Extravagance = 49238328, BlackAwakening = 68829754,
            WhiteDestiny = 94224458, CursedEldland = 31434645, ScarletSanguine = 20612097, Conquistador = 20590515, GozenMatch = 53334471,
            SkillDrain = 82732705, Huaquero = 93191801, Solemn = 41420027, Forever = 56984514, Impermanence = 10045474,
            GustavMax = 56910167, SuperDora = 49032236, Liebe = 26096328, Pleiades = 73964868, SharkFortress = 50449881;

        public override string Deck => "eldlich";

        public EldlichPlaybook()
        {
            Activate("Pot of Extravagance", Extravagance);
            Activate("Cursed Eldland", CursedEldland, () => !V.InSpellTrapZone(CursedEldland));
            Activate("Cursed Eldland searches", CursedEldland, perTurn: 2);
            Activate("Eldlixir of Black Awakening summons Eldlich", BlackAwakening, () => V.OnField(Eldlich) == 0);
            Activate("Eldlich sends a card to the GY", Eldlich, () => V.InHand(Eldlich) > 0 && V.OpponentCardCount > 0 && V.HandCount >= 3);
            Activate("Eldlich returns from the GY", Eldlich, () => V.InGraveyard(Eldlich) > 0 && V.SpellTrapCount > 0);
            NormalSummon("Inspector Boarder on an empty field", Boarder, () => V.MyMonsterCount == 0);
            SpecialSummon("two Level 10s into Gustav Max", GustavMax, () => !V.IsFirstTurn);
            SpecialSummon("two Level 5 LIGHTs into Constellar Pleiades", Pleiades, () => V.OpponentCardCount > 0);

            HoldSpecialSummon(SharkFortress);
            HoldNormalSummon(Eldlich);

            Prefer(Searches, CursedEldland, Eldlich, Conquistador, Huaquero, ScarletSanguine);
            Prefer(Searches, 0, Eldlich, Conquistador, BlackAwakening);
            Prefer(SummonPicks, BlackAwakening, Eldlich);
            Prefer(SummonPicks, 0, Eldlich, Conquistador, Huaquero);
            // Eldlich's cost from the hand: the Eldlixirs, which set a Golden Land card from the GY.
            Prefer(ToGrave, Eldlich, BlackAwakening, ScarletSanguine, WhiteDestiny, CursedEldland, GozenMatch, SkillDrain);
            Avoid(ToGrave, Eldlich, Forever, Solemn, Impermanence);
            Prefer(ToGrave, 0, CursedEldland, Conquistador, Huaquero, BlackAwakening);
            Prefer(Tributes, Forever, Conquistador, Huaquero, Boarder);
            Avoid(Tributes, Forever, Eldlich);
            Prefer(Materials, 0, Conquistador, Huaquero, Eldlich);
        }
    }
}
