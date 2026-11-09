namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Mitsurugi (2026): Saji, summoned, adds a Mitsurugi Spell/Trap and Aramasa a Mitsurugi monster; both search again when
    /// Tributed, so they are the Ritual's Tributes. Mitsurugi Ritual summons Ame no Murakumo from the Deck (it destroys every
    /// monster the opponent controls) and Habakiri, revealed, calls a Mitsurugi from the Deck. Pre-Preparation of Rites
    /// finds a Ritual Spell and its monster; the Chaos side (Light and Darkness Ritual, Black Chaos) and Diabellstar add
    /// threats.
    /// </summary>
    public sealed class MitsurugiPlaybook : Playbook
    {
        private const uint MagicianOfDarkChaos = 44001993, IllusionOfChaos = 12266229, DarkMagician = 46986414, BlackChaos = 98684220,
            SkullArchfiend = 24088928, Griffoh = 97462632, MagiciansRod = 7084129, MagiciansSouls = 97631303, Diabellstar = 72270339,
            GallantBandit = 24203749, Aramasa = 40543231, Saji = 18176525, Murakumo = 19899073, Habakiri = 13332685,
            LDRitual = 33599853, Curtain = 41350417, Gaze = 22283204, Hallowed = 94845588, Deception = 66328392, Wanted = 80845034,
            MitsurugiMirror = 49721684, MitsurugiRitual = 81560239, Prayers = 45171524, PrePreparation = 13048472,
            RaggedRecords = 24461358, MindShuffle = 24749710,
            DMDestruction = 59400890, Dragoon = 37818794, ContractWitch = 37458564, Zenna = 7594154, CrossSheep = 50277355,
            Goddess = 98127546, Mediclius = 4063756;

        private static readonly uint[] Reptiles = { Aramasa, Saji, Murakumo, Habakiri };

        public override string Deck => "mitsurugi";

        public MitsurugiPlaybook()
        {
            Activate("Pre-Preparation of Rites finds a Ritual", PrePreparation);
            Activate("Ragged Records of Rites finds a monster", RaggedRecords);
            Activate("WANTED searches Diabellstar", Wanted, () => V.InHand(Diabellstar) == 0);
            NormalSummon("Mitsurugi no Mikoto, Aramasa searches", Aramasa, () => V.InHand(Saji) == 0 || V.InHandAny(MitsurugiRitual));
            NormalSummon("Mitsurugi no Mikoto, Saji searches", Saji);
            Activate("Ame no Habakiri calls a Mitsurugi", Habakiri, () => V.InHand(Habakiri) > 0);
            Activate("Mitsurugi Ritual summons Ame no Murakumo", MitsurugiRitual, () => V.OpponentMonsterCount > 0 || V.IsFirstTurn);
            Activate("Mitsurugi Mirror", MitsurugiMirror, () => V.InHandAny(Murakumo, Habakiri) || V.InGraveyardAny(Murakumo, Habakiri) > 0);
            // Mitsurugi Prayers: on your turn only as an extender when there's no Murakumo to protect; with Murakumo out it
            // waits Set for the opponent's turn: it Tributes Murakumo, which comes back (Tributed) and wipes their board
            // (Special Summoned), a second wipe in one turn cycle.
            Activate("Mitsurugi Prayers adds a Mitsurugi", Prayers, () => V.IsMyTurn && V.OnField(Murakumo) == 0);
            SetSpellTrap("Mitsurugi Prayers waits for their turn", Prayers, () => V.OnField(Murakumo) > 0);
            ChainWhen("Mitsurugi Prayers Tributes Murakumo for another wipe", Prayers,
                () => !V.IsMyTurn && V.OnField(Murakumo) > 0 && V.OpponentMonsterCount >= 2);
            PreferIf(() => !V.IsMyTurn && V.OpponentMonsterCount >= 1, Tributes, Prayers, Murakumo, Habakiri, Saji, Aramasa);
            Activate("Light and Darkness Ritual", LDRitual, () => V.InHand(MagicianOfDarkChaos) > 0);
            SpecialSummon("Black Chaos", BlackChaos);
            Activate("Black Chaos banishes two", BlackChaos, () => V.OnField(BlackChaos) > 0 && V.OpponentCardCount > 0);
            Activate("Magician of Dark Chaos banishes", MagicianOfDarkChaos, () => V.OpponentCardCount > 0);
            SpecialSummon("Diabellstar summons itself", Diabellstar);
            Activate("Skull Archfiend of Chaos", SkullArchfiend);
            Activate("Dark Magical Curtain calls Dark Magician", Curtain, () => V.InDeck(DarkMagician) > 0);

            HoldSpecialSummon(Zenna);
            HoldSpecialSummon(CrossSheep);
            HoldSpecialSummon(Goddess, () => V.OpponentMonsterCount == 0);
            HoldNormalSummon(Diabellstar);
            HoldNormalSummon(Griffoh);

            Prefer(Searches, PrePreparation, MitsurugiRitual, Murakumo, LDRitual, MagicianOfDarkChaos, Habakiri);
            Prefer(Searches, RaggedRecords, Saji, Aramasa, SkullArchfiend, Griffoh);
            Prefer(Searches, Saji, MitsurugiRitual, Prayers, MitsurugiMirror);
            Prefer(Searches, Aramasa, Saji, Murakumo, Habakiri);
            Prefer(Searches, Prayers, Aramasa, Saji, Murakumo);
            Prefer(Searches, Murakumo, Saji, Aramasa, MitsurugiRitual, Prayers);
            Prefer(Searches, Habakiri, Saji, Aramasa, MitsurugiRitual);
            Prefer(Searches, Wanted, Diabellstar);
            Prefer(Searches, 0, Saji, Aramasa, MitsurugiRitual, Murakumo);
            Prefer(SummonPicks, MitsurugiRitual, Murakumo, Habakiri);
            Prefer(SummonPicks, MitsurugiMirror, Murakumo, Habakiri);
            Prefer(SummonPicks, Habakiri, Saji, Aramasa);
            Prefer(SummonPicks, LDRitual, MagicianOfDarkChaos);
            // Saji and Aramasa search again when Tributed: they pay for the Rituals.
            Prefer(Tributes, 0, Griffoh, Saji, Aramasa, MagiciansSouls);
            Avoid(Tributes, 0, Murakumo, BlackChaos, MagicianOfDarkChaos);
            Prefer(ToGrave, Diabellstar, MagiciansSouls, Griffoh, Curtain);
            Prefer(Materials, 0, Saji, Aramasa, Griffoh, MagiciansRod, MagiciansSouls, Diabellstar);
            Avoid(Materials, 0, Murakumo, BlackChaos, MagicianOfDarkChaos);
        }
    }
}
