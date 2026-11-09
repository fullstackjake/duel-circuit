namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Bystial Dragon Link (2022): the Bystials Special Summon themselves by banishing a LIGHT or DARK monster from either
    /// GY; Lubellion, sent from the hand, searches a Bystial and comes back by Tributing one to place a Branded card.
    /// Chaos Space and Starliege Seyfert find the right Dragon. The Rokket side: Rokket Tracer with Absorouter Dragon
    /// (destroyed, it adds another Rokket) and Striker Dragon (adds Boot Sector Launch); the Dragons link into Borrelend.
    /// </summary>
    public sealed class BystialDragonLinkPlaybook : Playbook
    {
        private const uint Druiswurm = 6637331, Magnamhut = 33854624, Lubellion = 32731036, Saronir = 60242223, Tracer = 68464358,
            Absorouter = 67748760, Recharger = 5969957, RokketSynchron = 48355999, Collapserpent = 61901281, Levianeer = 55878038,
            ChaosEmperor = 4538826, Wyverburster = 99234526, Seyfert = 15381421, Noctovision = 70333910, BrandedRegained = 34090915,
            BootSector = 36668118, QuickLaunch = 31443476, ChaosSpace = 99266988, DragonRavine = 62265044, BrandedBeast = 32756828,
            Baronne = 84815190, Borreload = 27548199, Scarlight = 80666118, Borrelend = 98630720, TripleBurst = 49725936,
            Accesscode = 86066372, Dillingerous = 23732205, Romulus = 11969228, Pisty = 13143275, Quadborrel = 29296344,
            Striker = 73539069, Masquerena = 65741786, Hieratic = 24361622;

        private static readonly uint[] Bystials = { Druiswurm, Magnamhut, Saronir };

        public override string Deck => "bystial-dragon-link";

        public BystialDragonLinkPlaybook()
        {
            Activate("Chaos Space finds a Dragon", ChaosSpace);
            Activate("The Bystial Lubellion searches a Bystial", Lubellion, () => V.InHand(Lubellion) > 0);
            Activate("Dragon Ravine sends a Dragon", DragonRavine, () => !V.InSpellTrapZone(DragonRavine));
            Activate("Bystial Magnamhut summons itself", Magnamhut);
            Activate("Bystial Druiswurm summons itself", Druiswurm);
            Activate("Bystial Saronir summons itself", Saronir);
            SpecialSummon("The Bystial Lubellion Tributes a Bystial", Lubellion, () => V.OnFieldAny(Bystials) > 0 && V.InDeckAny(BrandedRegained, BrandedBeast) > 0);
            Activate("Lubellion places a Branded card", Lubellion);
            NormalSummon("Rokket Tracer", Tracer, () => V.InHand(Absorouter) > 0 || V.InDeckAny(Recharger, RokketSynchron) > 0);
            SpecialSummon("Absorouter Dragon joins a Rokket", Absorouter);
            Activate("Rokket Tracer destroys Absorouter for a Rokket", Tracer, () => V.OnField(Absorouter) > 0 && V.IsMyTurn);
            SpecialSummon("White Dragon Wyverburster", Wyverburster);
            SpecialSummon("Black Dragon Collapserpent", Collapserpent);
            Activate("Starliege Seyfert finds a Dragon", Seyfert);
            SpecialSummon("a Level 4 Dragon into Striker Dragon", Striker, () => V.InDeck(BootSector) > 0 && V.MyMonsterCount <= 2);
            Activate("Boot Sector Launch", BootSector);
            SpecialSummon("Synchro into Baronne de Fleur", Baronne);
            SpecialSummon("Synchro into Borreload Savage Dragon", Borreload);
            SpecialSummon("Links into Borrelend Dragon", Borrelend, () => V.MyMonsterCount >= 3);
            SpecialSummon("Links into Accesscode Talker to finish", Accesscode, () => !V.IsFirstTurn && V.MyMonsterCount >= 3);

            HoldActivation(ChaosEmperor, () => V.OpponentCardCount < 3);
            HoldSpecialSummon(Pisty);
            HoldSpecialSummon(Hieratic);
            HoldSpecialSummon(Romulus);
            HoldNormalSummon(Noctovision);
            HoldNormalSummon(Magnamhut);
            HoldNormalSummon(Druiswurm);

            Prefer(Searches, ChaosSpace, Lubellion, Levianeer, Collapserpent, Wyverburster);
            Prefer(Searches, Lubellion, Druiswurm, Magnamhut, Saronir);
            Prefer(Searches, Magnamhut, Lubellion, Absorouter, Tracer, Seyfert);
            Prefer(Searches, Absorouter, Tracer, Recharger, RokketSynchron);
            Prefer(Searches, Striker, BootSector);
            Prefer(Searches, 0, Lubellion, Druiswurm, Magnamhut, Tracer);
            Prefer(ToGrave, ChaosSpace, Druiswurm, Magnamhut, Saronir, Noctovision);
            Prefer(ToGrave, DragonRavine, Saronir, Absorouter, Druiswurm);
            Prefer(ToGrave, Saronir, BrandedRegained, Lubellion, Druiswurm);
            Prefer(Tributes, Lubellion, Saronir, Magnamhut, Druiswurm);
            Prefer(SummonPicks, Tracer, Recharger, RokketSynchron);
            Prefer(SummonPicks, QuickLaunch, Tracer, Recharger);
            Prefer(Materials, 0, Absorouter, Tracer, Recharger, RokketSynchron, Striker, Noctovision, Saronir, Wyverburster, Collapserpent);
            Avoid(Materials, 0, Borrelend, Baronne, Lubellion);
        }
    }
}
