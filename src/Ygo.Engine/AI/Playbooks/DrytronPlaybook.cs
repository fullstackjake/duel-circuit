namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Drytron (2021): Cyber Emergency and Drytron Nova find a Drytron; each Drytron Tributes another Drytron or a Ritual
    /// Monster to Special Summon itself (Zeta Aldhibah adds Meteonis Drytron, Alpha Thuban a Ritual Monster, Gamma Eltanin
    /// revives a Drytron). Meteonis Drytron Ritual Summons by ATK, so one Drytron makes Herald of Ultimateness and two make
    /// Meteonis Draconids. Diviner of the Herald sends Eva, which searches Fairy hand traps; Mu Beta Fafnir holds a negate.
    /// </summary>
    public sealed class DrytronPlaybook : Playbook
    {
        private const uint Diviner = 92919429, OrangeLight = 17266660, Delta = 22420202, Eltanin = 60037599, Zeta = 96026108,
            Alpha = 97148796, Eva = 40177746, Draconids = 69815951, Ultimateness = 48546368, Benten = 77235086, Idaten = 3629090,
            Natasha = 99427357, CyberEmergency = 60600126, Nova = 94187078, Meteonis = 22398665, Fafnir = 58793369,
            MuBetaFafnir = 1174075, ArcLight = 79606837, Nightingale = 48608796, Linkuriboh = 41999284, Apollousa = 4280258;

        private static readonly uint[] Drytrons = { Delta, Eltanin, Zeta, Alpha };
        private static readonly uint[] Rituals = { Draconids, Ultimateness, Benten, Idaten, Natasha };

        public override string Deck => "drytron";

        public DrytronPlaybook()
        {
            Activate("Drytron Fafnir searches", Fafnir, () => !V.InSpellTrapZone(Fafnir));
            Activate("Cyber Emergency finds a Drytron", CyberEmergency);
            Activate("Drytron Nova summons a Drytron", Nova);
            Activate("Zeta Aldhibah adds Meteonis Drytron", Zeta);
            Activate("Alpha Thuban adds a Ritual Monster", Alpha);
            Activate("Gamma Eltanin revives a Drytron", Eltanin);
            Activate("Delta Altais draws", Delta);
            NormalSummon("Diviner of the Herald sends a Fairy", Diviner);
            Activate("Meteonis Drytron Ritual Summons", Meteonis, () => V.InHandAny(Rituals) || V.InGraveyardAny(Rituals) > 0);
            SpecialSummon("two Drytrons into Mu Beta Fafnir", MuBetaFafnir, () => V.OnFieldAny(Drytrons) >= 2);

            HoldSpecialSummon(Nightingale);
            HoldSpecialSummon(Linkuriboh);
            HoldNormalSummon(OrangeLight);

            Prefer(Searches, CyberEmergency, Zeta, Alpha, Eltanin, Delta);
            Prefer(Searches, Fafnir, Meteonis, Nova);
            Prefer(Searches, Zeta, Meteonis);
            Prefer(Searches, Alpha, Ultimateness, Draconids, Benten);
            Prefer(Searches, Eva, Diviner, OrangeLight);
            Prefer(Searches, 0, Zeta, Meteonis, Ultimateness, Alpha);
            Prefer(SummonPicks, Nova, Zeta, Alpha, Eltanin, Delta);
            Prefer(SummonPicks, Eltanin, Zeta, Alpha, Delta);
            Prefer(SummonPicks, Meteonis, Ultimateness, Draconids, Benten, Idaten);
            Prefer(SummonPicks, Diviner, Eva, OrangeLight);
            Prefer(ToGrave, Diviner, Eva, ArcLight, Benten);
            Prefer(ToGrave, MuBetaFafnir, Eltanin, Zeta, Meteonis);
            Prefer(Tributes, 0, Benten, Idaten, Natasha, Delta, Eltanin, Alpha, Zeta, Diviner);
            Avoid(Tributes, 0, Ultimateness, Draconids, MuBetaFafnir);
            Prefer(Materials, MuBetaFafnir, Delta, Eltanin, Alpha, Zeta);
        }
    }
}
