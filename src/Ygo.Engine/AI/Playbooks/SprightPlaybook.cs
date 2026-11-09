namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Spright (2022): everything is Level, Rank or Link 2. Spright Starter calls Spright Blue from the Deck (Blue adds Jet,
    /// Jet adds a Spright Spell/Trap); the Sprights Special Summon themselves next to a Level 2; Nimble Beaver and Swap Frog
    /// (discarding Nimble Angler, which calls two more Nimbles) add bodies. Two Level 2s become Gigantic Spright, which
    /// summons another Level 2 from the Deck, then Spright Elf. Carrot and Red stay on the field as negates.
    /// </summary>
    public sealed class SprightPlaybook : Playbook
    {
        private const uint Blue = 76145933, Jet = 13533678, Carrot = 2311090, Pixies = 49928686, Red = 75922381, Beaver = 68353324,
            Angler = 88686573, SwapFrog = 9126351, Magnamhut = 33854624, Druiswurm = 6637331, Fenrir = 32909498, Starter = 15443125,
            Smashers = 88836438, Prosperity = 84211599, DoubleCross = 68250822,
            Toadally = 90809975, Onibimaru = 9486959, Gigantic = 54498517, DjinnBuster = 3790062, Avramax = 21887175,
            Goddess = 98127546, Unicorn = 38342335, Earlie = 47759571, Elf = 27381364, Sprind = 72329844, Masquerena = 65741786;

        public override string Deck => "spright";

        public SprightPlaybook()
        {
            Activate("Pot of Prosperity digs", Prosperity, () => V.MyMonsterCount == 0);
            Activate("Spright Starter calls a Spright", Starter, () => V.NonTunersOfLevel(2) == 0);
            NormalSummon("Nimble Beaver calls a Nimble", Beaver);
            SpecialSummon("Swap Frog discards a WATER", SwapFrog, () => V.InHand(Angler) > 0);
            SpecialSummon("Spright Blue joins a Level 2", Blue);
            SpecialSummon("Spright Jet joins a Level 2", Jet);
            SpecialSummon("Spright Carrot joins a Level 2", Carrot);
            SpecialSummon("Spright Red joins a Level 2", Red);
            SpecialSummon("Spright Pixies joins a Level 2", Pixies);
            SpecialSummon("two Level 2s into Gigantic Spright", Gigantic, () => V.OnField(Gigantic) == 0 && V.MyMonsterCount >= 2);
            Activate("Gigantic Spright calls a Level 2", Gigantic);
            SpecialSummon("Level 2s into Spright Elf", Elf, () => V.MyMonsterCount >= 3);
            SpecialSummon("Level 2s into Spright Sprind", Sprind, () => V.MyMonsterCount >= 3 && V.OnField(Elf) > 0);
            SpecialSummon("Kashtira Fenrir on an empty field", Fenrir, () => V.MyMonsterCount == 0 && !V.InHandAny(Starter, Beaver));
            Activate("Kashtira Fenrir searches", Fenrir);

            HoldSpecialSummon(Goddess, () => V.OpponentMonsterCount == 0);
            HoldSpecialSummon(Masquerena);
            HoldSpecialSummon(DjinnBuster);
            HoldActivation(Magnamhut, () => V.IsMyTurn && V.InHandAny(Starter, Blue, Jet, Beaver));
            HoldActivation(Druiswurm, () => V.IsMyTurn && V.InHandAny(Starter, Blue, Jet, Beaver));

            Prefer(SummonPicks, Starter, Blue, Jet, Carrot, Red);
            Prefer(SummonPicks, Gigantic, Blue, Jet, Carrot, Red, Beaver, Pixies);
            Prefer(SummonPicks, Elf, Gigantic, Blue, Jet, Carrot, Red);
            Prefer(SummonPicks, Beaver, Angler);
            Prefer(Searches, Blue, Jet, Carrot, Red, Pixies);
            Prefer(Searches, Jet, Starter, DoubleCross, Smashers);
            Prefer(Searches, Fenrir, Fenrir);
            Prefer(Searches, 0, Blue, Jet, Starter);
            Prefer(ToGrave, SwapFrog, Angler, Beaver);
            Prefer(ToGrave, 0, Angler, Beaver, SwapFrog, Pixies);
            Prefer(Materials, 0, Beaver, Angler, SwapFrog, Blue, Jet, Pixies);
            Avoid(Materials, 0, Carrot, Red, Elf);
            Prefer(Materials, Gigantic, Beaver, Angler, SwapFrog, Blue, Jet);
            Avoid(Banish, Prosperity, Gigantic, Elf, Sprind, Toadally);
            Prefer(Tributes, 0, Beaver, Angler, SwapFrog, Blue, Jet, Pixies);
        }
    }
}
