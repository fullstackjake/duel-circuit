namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Adamancipator (2020): the Rock Tuners (Seeker, Researcher, Analyzer) Special Summon themselves from the hand and
    /// excavate the top 5 for a Level 4 or lower Rock. Unexpected Dai and Tenyi Adhara start from an empty field; Block
    /// Dragon comes from the hand or GY by banishing 3 EARTH and, sent to the GY as Synchro material, adds Rocks totalling
    /// Level 8. The Tuners become Raptite (excavates again), Borreload Savage Dragon or Herald of the Arc Light.
    /// </summary>
    public sealed class AdamancipatorPlaybook : Playbook
    {
        private const uint BlockDragon = 94689206, Gigantes = 47606319, KoakiGuardian = 45041488, Tackle = 12467005, Analyzer = 11302671,
            Supplier = 80839052, Seeker = 48519867, Researcher = 85914562, DokiDoki = 42143067, Adhara = 98159737, Dai = 911883,
            Beta = 39256679, Trumpeter = 87979586, Gamma = 38814750, Nibiru = 27204311,
            Borreload = 27548199, Raptite = 73079836, ArcLight = 79606837, Marcher = 81846453, GallantGranite = 32530043,
            Accesscode = 86066372, Apollousa = 4280258, Halqifibrax = 50588353, LinkSpider = 98978921, UnionCarrier = 83152482;

        private static readonly uint[] Tuners = { Seeker, Researcher, Analyzer };

        public override string Deck => "adamancipator";

        public AdamancipatorPlaybook()
        {
            Activate("Unexpected Dai calls a Normal Monster", Dai, () => V.MyMonsterCount == 0);
            Activate("Tenyi Adhara joins", Adhara, () => V.MyMonsterCount <= 1);
            NormalSummon("Adamancipator Seeker", Seeker, () => V.InHandAny(Researcher, Analyzer) || V.MyMonsterCount > 0);
            NormalSummon("Adamancipator Researcher", Researcher);
            NormalSummon("Adamancipator Analyzer", Analyzer);
            SpecialSummon("Adamancipator Analyzer summons itself", Analyzer);
            SpecialSummon("Adamancipator Seeker summons itself", Seeker);
            SpecialSummon("Adamancipator Researcher summons itself", Researcher);
            Activate("Analyzer excavates", Analyzer, perTurn: 2);
            Activate("Seeker excavates", Seeker, perTurn: 2);
            Activate("Researcher excavates", Researcher, perTurn: 2);
            Activate("Doki Doki summons a Rock", DokiDoki);
            SpecialSummon("Block Dragon from the hand or GY", BlockDragon);
            SpecialSummon("Gigantes", Gigantes, () => V.MyMonsterCount < 4);
            Activate("Raptite excavates", Raptite);
            SpecialSummon("Synchro into Adamancipator Risen - Raptite", Raptite);
            SpecialSummon("Synchro into Borreload Savage Dragon", Borreload);
            SpecialSummon("Tuner + body into Crystron Halqifibrax", Halqifibrax, () => V.MyMonsterCount >= 3);
            SpecialSummon("Synchro into Herald of the Arc Light", ArcLight, () => V.MyMonsterCount >= 3);
            SpecialSummon("Links into Accesscode Talker to finish", Accesscode, () => !V.IsFirstTurn && V.MyMonsterCount >= 3);
            SpecialSummon("Links into Apollousa (going first)", Apollousa, () => V.IsFirstTurn && V.MyMonsterCount >= 3);

            HoldSpecialSummon(UnionCarrier);
            HoldSpecialSummon(LinkSpider);
            HoldNormalSummon(KoakiGuardian, () => V.InHandAny(Tuners));
            HoldNormalSummon(Tackle, () => V.InHandAny(Tuners));

            Prefer(SummonPicks, 0, KoakiGuardian, Supplier, Tackle, Beta, DokiDoki, Gigantes, Trumpeter);
            Prefer(SummonPicks, Halqifibrax, Seeker, Researcher, Adhara);
            Prefer(SummonPicks, Dai, Beta, Trumpeter);
            Prefer(Searches, BlockDragon, Analyzer, Seeker, Researcher, KoakiGuardian, DokiDoki);
            Prefer(Searches, 0, Seeker, Researcher, Analyzer, BlockDragon);
            Prefer(Materials, 0, BlockDragon, Tackle, Seeker, Researcher, Analyzer, Adhara, DokiDoki, Beta, Supplier);
            Avoid(Materials, 0, KoakiGuardian, Borreload);
            Prefer(Banish, BlockDragon, Tackle, DokiDoki, Beta, Supplier, Seeker, Researcher);
            Avoid(Banish, BlockDragon, Analyzer);
        }
    }
}
