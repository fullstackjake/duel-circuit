namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Light and Darkness Ritual / Dark Magician Chaos (2026): Ragged Records of Rites reveals Light and Darkness Ritual to
    /// add Celtic Mystic, whose summon draws 3 and discards 2 (the discards are the pieces with hand or GY effects: Griffoh
    /// sets a Quick-Play, Black Chaos places Mind Shuffle, Skull Archfiend adds a Ritual Monster). Celtic Tributes itself to
    /// summon a Ritual Monster from the hand; Griffoh is a whole Ritual Tribute; Black Chaos shuffles a Ritual Monster away
    /// to summon itself and banishes two. Dark Magical Curtain and the Pharaoh's Servant bring the Dark Magician side.
    /// </summary>
    public sealed class LightAndDarknessPlaybook : Playbook
    {
        private const uint SoldierOfLD = 70405001, MagicianOfDarkChaos = 44001993, IllusionOfChaos = 12266229, DarkMagician = 46986414,
            PharaohsServant = 88570003, BlackChaos = 98684220, CelticMystic = 50073633, SkullArchfiend = 24088928,
            BlackSkullDragon = 97818130, Griffoh = 97462632, MagiciansRod = 7084129, MagiciansSouls = 97631303, Diabellstar = 72270339,
            Fuwalos = 42141493, LDRitual = 33599853, SpellShatteringSword = 77456448, Curtain = 41350417, Gaze = 22283204,
            Hallowed = 94845588, Deception = 66328392, Wanted = 80845034, RaggedRecords = 24461358, MindShuffle = 24749710,
            DMDestruction = 59400890, Dragoon = 37818794, ContractWitch = 37458564, Zenna = 7594154, CrossSheep = 50277355,
            Goddess = 98127546, Firewall = 5043010, FancyBall = 4993187;

        private static readonly uint[] Rituals = { SoldierOfLD, MagicianOfDarkChaos };

        public override string Deck => "light-and-darkness";

        public LightAndDarknessPlaybook()
        {
            Activate("Ragged Records of Rites finds Celtic Mystic", RaggedRecords);
            Activate("WANTED searches Diabellstar", Wanted, () => V.InHand(Diabellstar) == 0);
            NormalSummon("Celtic Mystic draws 3", CelticMystic);
            NormalSummon("Magician's Rod adds a Dark Magician card", MagiciansRod, () => V.InHand(CelticMystic) == 0);
            Activate("Celtic Mystic Tributes itself for a Ritual Monster", CelticMystic, () => V.InHandAny(Rituals));
            Activate("Light and Darkness Ritual", LDRitual, () => V.InHandAny(Rituals) || V.InGraveyardAny(Rituals) > 0);
            Activate("Light and Darkness Ritual comes back", LDRitual, perTurn: 2);
            SpecialSummon("Black Chaos", BlackChaos);
            Activate("Black Chaos banishes two", BlackChaos, () => V.OnField(BlackChaos) > 0 && V.OpponentCardCount > 0);
            Activate("Magician of Dark Chaos banishes", MagicianOfDarkChaos, () => V.OpponentCardCount > 0);
            Activate("Black Skull Dragon joins", BlackSkullDragon);
            Activate("Dark Magician, the Pharaoh's Servant joins", PharaohsServant, () => V.InHand(PharaohsServant) > 0);
            Activate("Dark Magical Curtain calls Dark Magician", Curtain, () => V.InDeckAny(DarkMagician, PharaohsServant) > 0);
            SpecialSummon("Diabellstar summons itself", Diabellstar);
            Activate("Skull Archfiend of Chaos", SkullArchfiend);
            Activate("Illusion of Chaos finds a Dark Magician", IllusionOfChaos, () => V.InHand(IllusionOfChaos) > 0);
            Activate("Magicians' Souls", MagiciansSouls, () => V.InHand(MagiciansSouls) > 0);

            HoldSpecialSummon(Zenna);
            HoldSpecialSummon(CrossSheep);
            HoldSpecialSummon(Firewall);
            HoldSpecialSummon(Goddess, () => V.OpponentMonsterCount == 0);
            HoldNormalSummon(Diabellstar);
            HoldNormalSummon(Griffoh);
            HoldNormalSummon(BlackChaos);

            Prefer(Searches, RaggedRecords, CelticMystic, Griffoh, SkullArchfiend, BlackSkullDragon);
            Prefer(Searches, MagiciansRod, Curtain, Gaze, SpellShatteringSword);
            Prefer(Searches, SkullArchfiend, MagicianOfDarkChaos, SoldierOfLD);
            Prefer(Searches, MindShuffle, CelticMystic, Griffoh, SkullArchfiend);
            Prefer(Searches, Curtain, Gaze, SpellShatteringSword);
            Prefer(Searches, Wanted, Diabellstar);
            Prefer(Searches, 0, CelticMystic, LDRitual, MagicianOfDarkChaos, SoldierOfLD);
            Prefer(SummonPicks, CelticMystic, MagicianOfDarkChaos, SoldierOfLD);
            Prefer(SummonPicks, LDRitual, MagicianOfDarkChaos, SoldierOfLD);
            Prefer(SummonPicks, Curtain, DarkMagician, PharaohsServant, Diabellstar);
            // Celtic Mystic's discards and other costs: the cards that pay off from the hand or GY.
            Prefer(ToGrave, 0, Griffoh, SkullArchfiend, BlackChaos, Fuwalos, MagiciansSouls, Diabellstar);
            Avoid(ToGrave, 0, LDRitual, MagicianOfDarkChaos, SoldierOfLD, CelticMystic);
            Prefer(Tributes, 0, Griffoh, CelticMystic, MagiciansRod, MagiciansSouls, DarkMagician);
            Avoid(Tributes, 0, BlackChaos, MagicianOfDarkChaos, SoldierOfLD);
            Prefer(Materials, 0, MagiciansRod, MagiciansSouls, Griffoh, CelticMystic, Diabellstar);
            Avoid(Materials, 0, BlackChaos, MagicianOfDarkChaos, SoldierOfLD);
        }
    }
}
