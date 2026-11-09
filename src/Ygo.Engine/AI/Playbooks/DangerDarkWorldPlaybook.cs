namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Danger! Dark World (2018): every Danger! monster reveals itself so the opponent discards a random card from the hand;
    /// if it wasn't the revealed name, a copy is Special Summoned and you draw. The discards are the payoff: Jackalope
    /// summons a Danger! from the Deck, Nessie searches one, Bigfoot and Thunderbird destroy, Broww draws, Grapha destroys.
    /// Card Destruction, Allure and The Beginning of the End keep the hand full; the bodies become Knightmare Links.
    /// </summary>
    public sealed class DangerDarkWorldPlaybook : Playbook
    {
        private const uint Allure = 1475311, Broww = 79126789, CardDestruction = 72892473, Bigfoot = 43316238, Chupacabra = 16209941,
            Dogman = 26302107, Mothman = 52350806, Nessie = 70711847, Thunderbird = 90807199, Jackalope = 43694650,
            Tsuchinoko = 99745551, Grapha = 34230233, Snoww = 60228941, BeginningOfTheEnd = 73680966, CannonSoldier = 11384280,
            Unicorn = 38342335, Phoenix = 2857636, Cerberus = 75452921, Mermaid = 3679218, Gumblar = 22593417;

        public override string Deck => "danger-dark-world";

        public DangerDarkWorldPlaybook()
        {
            // Reveal first the Danger! monsters whose discard pays most, so the random discards keep the chain going.
            Activate("Danger!? Jackalope?", Jackalope);
            Activate("Danger! Nessie!", Nessie);
            Activate("Danger! Chupacabra!", Chupacabra);
            Activate("Danger!? Tsuchinoko?", Tsuchinoko);
            Activate("Danger! Bigfoot!", Bigfoot, () => V.OpponentCardCount > 0 || V.HandCount >= 3);
            Activate("Danger! Thunderbird!", Thunderbird);
            Activate("Danger! Mothman!", Mothman);
            Activate("Danger! Dogman!", Dogman);
            Activate("Card Destruction refills the hand", CardDestruction, () => V.HandCount >= 3);
            SpecialSummon("Grapha returns a Dark World monster", Grapha, () => V.OnFieldAny(Broww, Snoww) > 0);
            SpecialSummon("Links into Knightmare Unicorn", Unicorn, () => !V.IsFirstTurn && V.OpponentCardCount > 0 && V.MyMonsterCount >= 3);
            SpecialSummon("Links into Knightmare Phoenix", Phoenix, () => !V.IsFirstTurn && V.OpponentCardCount > V.OpponentMonsterCount && V.MyMonsterCount >= 3);
            SpecialSummon("Links into Knightmare Cerberus", Cerberus, () => !V.IsFirstTurn && V.OpponentMonsterCount > 0 && V.MyMonsterCount >= 3);

            HoldSpecialSummon(Gumblar);
            HoldSpecialSummon(Mermaid);
            HoldNormalSummon(Bigfoot);
            HoldNormalSummon(Thunderbird);

            Prefer(ToGrave, 0, Broww, Grapha, Jackalope, Nessie, Bigfoot, Thunderbird, Chupacabra, Tsuchinoko, Snoww, Dogman, Mothman);
            Prefer(SummonPicks, Jackalope, Bigfoot, Thunderbird, Dogman, Chupacabra, Nessie);
            Prefer(SummonPicks, Chupacabra, Bigfoot, Thunderbird, Jackalope, Dogman, Nessie);
            Prefer(Searches, Nessie, Jackalope, Chupacabra, Bigfoot, Tsuchinoko);
            Prefer(Searches, 0, Jackalope, Nessie, Grapha, Broww);
            Prefer(Banish, Allure, CannonSoldier, Mothman, Dogman, Tsuchinoko);
            Prefer(Banish, BeginningOfTheEnd, Mothman, Dogman, Tsuchinoko, Snoww, Broww);
            Prefer(Materials, 0, Tsuchinoko, Jackalope, Mothman, Chupacabra, Dogman, Nessie, CannonSoldier);
            Avoid(Materials, 0, Bigfoot, Thunderbird);
        }
    }
}
