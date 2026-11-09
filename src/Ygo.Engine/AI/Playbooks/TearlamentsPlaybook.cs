namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Tearlaments (2022), also the Ishizu build: every Tearlaments monster sent to the GY by a card effect Fusion Summons
    /// by putting its materials back into the Deck, so the plan is to mill. Perlereino (via Terraforming) adds Reinoheart,
    /// whose summon sends Merrli, Havnis or Scheiren from the Deck; Scheiren summons itself by sending a monster from the
    /// hand and milling 3; Merrli mills 3. The Fusions: Kitkallos (adds or sends a Tearlaments card, then sends itself),
    /// Rulkallos (negates Special Summons), Kaleido-Heart (shuffles a card away). Ishizu adds the Ancient Vanguard Fairies: Kelbek and Agido,
    /// sent from the hand (Keldo's and Mudora's discard, Scheiren's send), mill 5 from both Decks.
    /// </summary>
    public sealed class TearlamentsPlaybook : Playbook
    {
        private const uint Havnis = 37961969, Merrli = 74078255, Reinoheart = 73956664, Scheiren = 572850, Magnamhut = 33854624,
            Druiswurm = 6637331, Albion = 25451383, Cartesia = 95515789, KingOfTheSwamp = 79109599, Perlereino = 77103950,
            Scream = 6767771, Heartbeat = 60362066, HighSpirits = 29948294, InstantFusion = 1845204, Polymerization = 24094653,
            Terraforming = 73628505, SuperPoly = 48130397, Sulliek = 74920585,
            GuardianChimera = 11321089, KaleidoHeart = 28226490, Rulkallos = 84330567, Dragostapelia = 69946549, Garura = 11765832,
            Kitkallos = 92731385, Mudragon = 54757758, Goddess = 98127546, Elf = 27381364, Sprind = 72329844,
            TearKashtira = 4928565, Metanoise = 38436986, Kelbek = 25926710, Keldo = 63542003, Mudora = 99937011, Agido = 62320425;

        private static readonly uint[] TearlamentsMonsters = { Havnis, Merrli, Reinoheart, Scheiren };
        private readonly string _deck;
        public override string Deck => _deck;

        public TearlamentsPlaybook(string deck = "tearlaments")
        {
            _deck = deck;
            Activate("Terraforming finds Perlereino", Terraforming, () => !V.InSpellTrapZone(Perlereino) && !V.InHandAny(Perlereino));
            Activate("Primeval Planet Perlereino searches", Perlereino, () => !V.InSpellTrapZone(Perlereino));
            Activate("Tearlaments Scream", Scream, () => !V.InSpellTrapZone(Scream));
            NormalSummon("Tearlaments Reinoheart sends a Tearlaments", Reinoheart, () => V.InDeckAny(Havnis, Merrli, Scheiren) > 0);
            Activate("Tearlaments Scheiren summons itself and mills", Scheiren, () => V.InHand(Scheiren) > 0 && V.HandCount >= 2);
            NormalSummon("Tearlaments Merrli mills 3", Merrli);
            Activate("Keldo discards a Fairy to mill", Keldo, () => V.InHandAny(Kelbek, Agido));
            Activate("Mudora discards a Fairy to mill", Mudora, () => V.InHandAny(Kelbek, Agido));
            Activate("Tearlaments Kashtira joins and mills", TearKashtira, () => V.IsMyTurn);
            Activate("Tearlaments Kitkallos summons a Tearlaments", Kitkallos);
            Activate("King of the Swamp finds Polymerization", KingOfTheSwamp, () => V.InDeck(Polymerization) > 0 && V.InHandAny(TearlamentsMonsters));
            Activate("Polymerization", Polymerization);
            Activate("Branded in High Spirits", HighSpirits);
            Activate("Albion the Shrouded Dragon", Albion);
            Activate("Blazing Cartesia", Cartesia, () => V.IsMyTurn);

            HoldSpecialSummon(Garura);
            HoldSpecialSummon(Mudragon, () => V.OnFieldAny(Rulkallos, KaleidoHeart, Kitkallos) == 0 && V.MyMonsterCount < 2);
            HoldSpecialSummon(Elf);
            HoldSpecialSummon(Sprind);
            HoldNormalSummon(Havnis);
            HoldActivation(InstantFusion, () => V.InHandAny(TearlamentsMonsters));

            // The GY trigger's Fusion: Rulkallos if it can, then Kaleido-Heart, then Kitkallos.
            Prefer(SummonPicks, 0, Rulkallos, KaleidoHeart, Kitkallos, Dragostapelia, GuardianChimera);
            Prefer(new[] { DuelContext.HintFusionMaterial }, 0, Merrli, Havnis, Scheiren, KingOfTheSwamp, Kitkallos, Reinoheart);
            Prefer(Searches, Perlereino, Reinoheart, Scheiren, Merrli);
            Prefer(Searches, Kitkallos, Scheiren, Reinoheart, Merrli);
            Prefer(Searches, KingOfTheSwamp, Polymerization);
            Prefer(Searches, Sulliek, Scheiren, Reinoheart);
            Prefer(Searches, 0, Reinoheart, Scheiren, Perlereino, Merrli);
            Prefer(ToGrave, Reinoheart, Merrli, Havnis, Scheiren);
            Prefer(ToGrave, Scheiren, Kelbek, Agido, Merrli, Havnis, Reinoheart, KingOfTheSwamp);
            Prefer(ToGrave, Keldo, Kelbek, Agido, Mudora);
            Prefer(ToGrave, Mudora, Kelbek, Agido, Keldo);
            Prefer(ToGrave, Metanoise, Merrli, Havnis, Scheiren);
            Prefer(ToGrave, Kitkallos, Merrli, Havnis, Scheiren);
            Prefer(ToGrave, 0, Kelbek, Agido, Merrli, Havnis, Scheiren, Reinoheart, KingOfTheSwamp);
            Prefer(ToGrave, HighSpirits, Rulkallos, Dragostapelia);
            Prefer(Materials, 0, Merrli, Havnis, Scheiren, Reinoheart, Mudora, Keldo, Kelbek, Agido);
        }
    }
}
