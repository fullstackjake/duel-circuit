namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Elfnote / Power Patron (2026): the Elfnote monsters Special Summon themselves to the center Main Monster Zone (Lucina
    /// searches another Elfnote, Tinia and Fortuna place their Continuous Spell/Trap); Regina joins by sending another
    /// Elfnote and, in the center, calls one from the Deck. Medius the Pure finds Elfnote Power Patron, which raises the
    /// center monster's Level by 3 and Synchro Summons on the spot (June Pride, Seraphim Strelitzia); sent as material it
    /// searches again.
    /// </summary>
    public sealed class ElfnotePlaybook : Playbook
    {
        private const uint Lucina = 13597785, Regina = 56651978, Tinia = 59581480, Fortuna = 85976588, PowerPatron = 12375297,
            Medius = 97556336, TheoMedius = 90875418, WelcomeHome = 64491754, Theorealize = 48739627, PastLull = 36709484,
            Citrinitas = 37279096, Terminus = 25661743, Rhapsodia = 24092792, Junoldo = 10266279, Vidolium = 70488851,
            JunePride = 5559570, Strelitzia = 42302563, CrystalWing = 50954680, EndPunisher = 60465049, Archethys = 87188910,
            Purification = 31822037, Mediclius = 4063756, LinkSpider = 98978921, Fuwalos = 42141493;

        private static readonly uint[] Elfnotes = { Lucina, Regina, Tinia, Fortuna };

        public override string Deck => "elfnote";

        public ElfnotePlaybook()
        {
            SpecialSummon("Elfnote Lucina takes the center", Lucina, () => V.MyMonsterCount == 0);
            Activate("Elfnote Lucina searches an Elfnote", Lucina);
            NormalSummon("Medius the Pure finds Elfnote Power Patron", Medius);
            Activate("Terminus finds Medius", Terminus, () => V.InHand(Medius) == 0);
            Activate("Theorealize Past Lull calls Medius", PastLull);
            Activate("Elfnote Regina joins", Regina, () => V.InHandAny(Tinia, Fortuna, Lucina) || V.OnFieldAny(Tinia, Fortuna) > 0);
            Activate("Elfnote Tinia places Welcome Home", Tinia);
            Activate("Elfnote Fortuna places Rhapsodia", Fortuna);
            SpecialSummon("Elfnote Tinia takes the center", Tinia, () => V.MyMonsterCount == 0);
            SpecialSummon("Elfnote Fortuna takes the center", Fortuna, () => V.MyMonsterCount == 0);
            Activate("Elfnotes: Welcome Home calls an Elfnote", WelcomeHome, () => V.HandCount >= 2 || V.MyMonsterCount >= 2);
            Activate("Ars Magna Citrinitas searches", Citrinitas);
            Activate("Elfnote Power Patron raises the center and Synchro Summons", PowerPatron, () => V.IsMyTurn);
            Activate("Medius comes back", Medius, () => V.MyMonsterCount < 3);
            SpecialSummon("Synchro into Elfnote June Pride", JunePride);
            SpecialSummon("Synchro into Elfnote Seraphim Strelitzia", Strelitzia);
            SpecialSummon("Synchro into Crystal Wing Synchro Dragon", CrystalWing);
            SpecialSummon("Synchro into Ravenous Crocodragon Archethys", Archethys, () => V.HandCount <= 2);

            HoldSpecialSummon(LinkSpider);
            HoldActivation(Theorealize, () => V.InDeck(Medius) == 0);

            Prefer(Searches, Lucina, Regina, PowerPatron, Tinia, Fortuna);
            Prefer(Searches, Medius, PowerPatron);
            Prefer(Searches, PowerPatron, WelcomeHome, Regina, Lucina, Rhapsodia);
            Prefer(Searches, Terminus, Medius, TheoMedius);
            Prefer(Searches, Citrinitas, Medius);
            Prefer(Searches, 0, Lucina, Regina, Medius, PowerPatron);
            Prefer(SummonPicks, Medius, PowerPatron);
            Prefer(SummonPicks, Regina, Tinia, Fortuna, Lucina);
            Prefer(SummonPicks, WelcomeHome, Tinia, Fortuna, Regina, Lucina);
            Prefer(SummonPicks, PastLull, Medius);
            Prefer(SummonPicks, 0, JunePride, Strelitzia, CrystalWing, Regina, Lucina);
            Prefer(ToGrave, Regina, Fortuna, Tinia, PowerPatron, Fuwalos);
            Prefer(ToGrave, Terminus, PowerPatron);
            Prefer(ToGrave, 0, Fuwalos, Medius, Fortuna, Tinia);
            Avoid(ToGrave, 0, Lucina, JunePride, Strelitzia);
            Prefer(Materials, 0, PowerPatron, Regina, Medius, TheoMedius, Fortuna, Tinia);
            Avoid(Materials, 0, Lucina, JunePride);
        }
    }
}
