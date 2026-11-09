namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Ryzeal / Mitsurugi (2025): Bonfire finds Ice Ryzeal, whose Normal Summon calls Sword Ryzeal from the Deck (Sword adds
    /// a LIGHT Pyro, Ext adds a FIRE Thunder); Ext, Sword and Node Special Summon themselves, so two to four Level 4s
    /// become Ryzeal Duo Drive (searches two Ryzeal) and Ryzeal Detonator (destroys on the opponent's turn). The
    /// Mitsurugi side: Saji adds Mitsurugi Ritual, which summons Ame no Murakumo from the Deck to wipe the opponent's field.
    /// </summary>
    public sealed class RyzealPlaybook : Playbook
    {
        private const uint Murakumo = 19899073, Futsu = 55397172, Saji = 18176525, Kusanagi = 82782870, MitsurugiRitual = 81560239,
            Prayers = 45171524, Ext = 34022970, Ice = 8633261, Node = 72238166, Sword = 35844557, Bonfire = 85106525,
            Prosperity = 84211599, Cross = 6798031, Magnamhut = 33854624, Druiswurm = 6637331,
            DuoDrive = 7511613, Detonator = 34909328, EclipseTwins = 45852939, Exciton = 46772449, Gagaga = 12014404,
            FeralImps = 11398059, Dugares = 66011101, AbyssDweller = 21044178, Tornado = 6983839, Lizardose = 9763474,
            Aggregator = 9940036, Typhon = 93039339, ChaosAngel = 22850702, Fuwalos = 42141493,
            // No Limits (docs/dev/NO-LIMITS-SPEC-2026-11-02.md 7.2): cards the as-built list doesn't run. Anselm's nl-mitsurugi:
            Habakiri = 13332685, Aramasa = 40543231, MassDriver = 34906152, PharaonicAdvent = 23099524, SinisterSerpent = 8131171,
            Dragoon = 37818794, Kusanagi8 = 82782870,
            // Tova's nl-ryzeal:
            Snow = 55623480, LavalvalChain = 34086406, Emeral = 581014, Nibiru = 27204311, Veiler = 97268402, Ash = 14558127,
            Imperm = 10045474, Talent = 25311006, Called = 24224830;

        private static readonly uint[] Ryzeals = { Ext, Ice, Node, Sword };
        /// <summary>Spent cards Fairy Tail - Snow's revive may banish from the GY (hand traps and one-shot Spells).</summary>
        private static readonly uint[] SnowFodder = { Nibiru, Fuwalos, Veiler, Ash, Imperm, Talent, Called, Bonfire, Cross, Magnamhut, Druiswurm, 99989863 };

        public override string Deck => "ryzeal";

        public RyzealPlaybook()
        {
            Activate("Ryzeal Cross", Cross, () => !V.InSpellTrapZone(Cross));
            Activate("Bonfire finds a Ryzeal", Bonfire);
            NormalSummon("Ice Ryzeal calls a Ryzeal from the Deck", Ice);
            NormalSummon("Mitsurugi no Mikoto, Saji adds Mitsurugi Ritual", Saji, () => V.InHand(Ice) == 0);
            SpecialSummon("Sword Ryzeal summons itself", Sword);
            SpecialSummon("Ext Ryzeal summons itself", Ext);
            SpecialSummon("Node Ryzeal summons itself", Node);
            SpecialSummon("Ice Ryzeal summons itself", Ice, () => V.MyMonsterCount == 1);
            Activate("Node Ryzeal revives a Ryzeal", Node, () => V.InGraveyardAny(Ext, Ice, Sword) > 0);
            Activate("Mitsurugi Ritual summons Ame no Murakumo", MitsurugiRitual, () => V.OpponentMonsterCount > 0 || V.IsFirstTurn);
            Activate("Mitsurugi Prayers adds a Mitsurugi", Prayers, () => V.IsMyTurn);
            SpecialSummon("two Level 4s into Ryzeal Duo Drive", DuoDrive, () => V.OnField(DuoDrive) == 0);
            Activate("Ryzeal Duo Drive searches two Ryzeal", DuoDrive);
            SpecialSummon("Level 4s into Ryzeal Detonator", Detonator);
            SpecialSummon("two Level 4s into King of the Feral Imps", FeralImps, () => V.OnField(Detonator) > 0 && V.InDeckAny(Saji, Kusanagi, Murakumo) > 0);
            Activate("King of the Feral Imps finds a Reptile", FeralImps);

            HoldSpecialSummon(Exciton, () => V.OpponentCardCount < 3);
            HoldSpecialSummon(Dugares);
            HoldSpecialSummon(Typhon);
            HoldSpecialSummon(Lizardose);
            HoldActivation(Prosperity, () => V.InHandAny(Ice, Bonfire));

            Prefer(Searches, Bonfire, Ice, Ext);
            Prefer(Searches, Sword, Ice, Ext);
            Prefer(Searches, Ext, Sword, Node);
            Prefer(Searches, DuoDrive, Ice, Sword, Ext, Node, Cross);
            Prefer(Searches, Saji, MitsurugiRitual, Prayers);
            Prefer(Searches, Prayers, Saji, Kusanagi, Murakumo);
            Prefer(Searches, FeralImps, Saji, Kusanagi);
            Prefer(Searches, 0, Ice, Sword, Ext, MitsurugiRitual);
            Prefer(SummonPicks, Ice, Sword, Ext, Node);
            Prefer(SummonPicks, Node, Ice, Sword, Ext);
            Prefer(SummonPicks, MitsurugiRitual, Murakumo, Futsu);
            // Ext Ryzeal's cost sends an Xyz from the Extra Deck: never the Ryzeal ones.
            Prefer(ToGrave, Ext, Dugares, Gagaga, Tornado, AbyssDweller, Exciton, EclipseTwins);
            Avoid(ToGrave, Ext, DuoDrive, Detonator);
            Prefer(ToGrave, 0, Fuwalos, Saji);
            Prefer(Materials, 0, Ice, Node, Sword, Ext, Saji, Kusanagi);
            Prefer(Tributes, 0, Saji, Kusanagi);
            Avoid(Banish, Prosperity, DuoDrive, Detonator, FeralImps, EclipseTwins);

            // ---- No Limits, Anselm's nl-mitsurugi (7.2): Habakiri and the free altars. Additive only: every card below is one
            // the as-built ryzeal list doesn't run, so the base plays as before (7.1).
            // Habakiri, revealed in the hand, calls a Mitsurugi from the Deck and Tributes one (Saji and Aramasa search when Tributed).
            ActivateEffect("Ame no Habakiri calls a Mitsurugi from the Deck", Habakiri, 0, () => V.IsMyMainPhase && V.InHand(Habakiri) > 0);
            Prefer(SummonPicks, Habakiri, Saji, Aramasa, Murakumo);
            Prefer(Tributes, Habakiri, SinisterSerpent, Saji, Aramasa);
            Prefer(Searches, Habakiri, MitsurugiRitual, Saji, Prayers);
            NormalSummon("Mitsurugi no Mikoto, Aramasa adds a Mitsurugi monster", Aramasa, () => V.InHand(Ice) == 0 && V.InHand(Saji) == 0);
            Prefer(Searches, Aramasa, Habakiri, Saji, Murakumo);
            // Mass Driver: Tribute only the monsters with a "when Tributed" trigger (Saji, Aramasa, Habakiri) or the Serpent that
            // comes back; never Murakumo, Kusanagi or Dragoon.
            Activate("Mass Driver", MassDriver, () => V.IsMyMainPhase && !V.InSpellTrapZone(MassDriver));
            ActivateEffect("Mass Driver Tributes a Mitsurugi for its trigger", MassDriver, 0,
                () => V.IsMyTurn && V.InSpellTrapZone(MassDriver) && V.OnFieldAny(Saji, Aramasa, Habakiri, SinisterSerpent) > 0, perTurn: 3);
            Prefer(Tributes, MassDriver, SinisterSerpent, Saji, Aramasa, Habakiri);
            Avoid(Tributes, MassDriver, Murakumo, Kusanagi8, Dragoon, Detonator, DuoDrive);
            // Pharaonic Advent: Tribute a Mitsurugi Reptile (or the Serpent) to summon a Level 8 from the hand; its search locks
            // Special Summons for the turn, so never.
            ActivateEffect("Pharaonic Advent Tributes a Mitsurugi to summon itself", PharaonicAdvent, 0,
                () => V.IsMyMainPhase && V.InHand(PharaonicAdvent) > 0 && V.OnFieldAny(Saji, Aramasa, SinisterSerpent) > 0 && V.FreeMonsterZones > 0);
            HoldEffect(PharaonicAdvent, 1);
            Prefer(Tributes, PharaonicAdvent, SinisterSerpent, Saji, Aramasa);
            Avoid(Tributes, PharaonicAdvent, Murakumo, Habakiri, Kusanagi8, Dragoon, Detonator, DuoDrive);
            // Sinister Serpent: Tribute fodder that returns every Standby Phase (a Normal Summon only when nothing searches).
            NormalSummon("Sinister Serpent as Tribute fodder", SinisterSerpent,
                () => !V.InHandAny(Ice, Saji, Aramasa) && (V.InHandAny(PharaonicAdvent, Habakiri) || V.InSpellTrapZone(MassDriver) || V.InHand(Prayers) > 0));
            PreferIf(() => V.IsMyTurn, Tributes, Prayers, SinisterSerpent);
            PreferIf(() => V.IsMyTurn, Tributes, MitsurugiRitual, SinisterSerpent);
            Avoid(Materials, 0, SinisterSerpent);

            // ---- No Limits, Tova's nl-ryzeal (7.2): the Fiendsmith engine and the banned Rank 4 toolbox.
            FiendsmithEngine();
            // Fairy Tail - Snow: a Level 4 that turns a monster face-down when summoned, and comes back from the GY (banishing
            // 7 spent cards) as fresh Xyz material.
            NormalSummon("Fairy Tail - Snow flips a monster face-down", Snow, () => !V.InHandAny(Ice, Saji) && V.OpponentMonsterCount > 0);
            // Its cost is 7 cards from the hand, field or GY, so only once the GY alone holds 7 spent cards and the hand is
            // nearly empty (the generic bot would otherwise pay with the hand and the board, on either turn).
            bool SnowRevive() => V.IsMyMainPhase && V.InGraveyard(Snow) > 0 && V.NonTunersOfLevel(4) >= 1 && V.FreeMonsterZones > 0
                                 && V.HandCount <= 1 && V.InGraveyardAny(SnowFodder) >= 7 && V.InExtraDeckAny(Detonator, DuoDrive, LavalvalChain, Emeral);
            ActivateEffect("Fairy Tail - Snow comes back as Xyz material", Snow, 1, SnowRevive);
            HoldEffect(Snow, 1, () => !SnowRevive());
            Prefer(Banish, Snow, SnowFodder);
            Avoid(Banish, Snow, Ext, Ice, Sword, Node, FiendsmithEngraver, LacrimaCrimsonTears, FabledLurrie, FiendsmithTract, FiendsmithLacrima,
                Detonator, DuoDrive, LavalvalChain, Emeral, FeralImps, FiendsmithRequiem, FiendsmithSequence, FiendsmithDesirae, Saji, Kusanagi, Murakumo);
            // Lavalval Chain: once Detonator is up, a second Rank 4 that sends Snow (or a spare Ryzeal) from the Deck to the GY.
            SpecialSummon("two Level 4s into Lavalval Chain", LavalvalChain,
                () => V.OnField(Detonator) > 0 && V.NonTunersOfLevel(4) >= 2 && V.InDeckAny(Snow, Sword, Ext, Ice, FiendsmithEngraver) > 0);
            ActivateEffect("Lavalval Chain sends a card from the Deck", LavalvalChain, 0, () => V.InDeckAny(Snow, FiendsmithEngraver, Sword, Ext, Ice) > 0);
            ChooseOption(LavalvalChain, 1);
            Prefer(ToGrave, LavalvalChain, Snow, FiendsmithEngraver, Sword, Ext, Ice);
            Avoid(ToGrave, Ext, LavalvalChain);
            // Daigusto Emeral: once the GY holds three spent monsters, recycle them and draw.
            SpecialSummon("two Level 4s into Daigusto Emeral", Emeral,
                () => V.OnField(Detonator) > 0 && V.NonTunersOfLevel(4) >= 2 && V.GraveyardMonstersAtOrBelowLevel(12) >= 3);
            ActivateEffect("Daigusto Emeral shuffles three monsters back and draws", Emeral, 0, () => V.GraveyardMonstersAtOrBelowLevel(12) >= 3);
            ChooseOption(Emeral, 1);
            Prefer(new[] { DuelContext.HintToDeck }, Emeral, Nibiru, Fuwalos, Veiler, Ash, Magnamhut, Druiswurm, Snow);
            Avoid(new[] { DuelContext.HintToDeck }, Emeral, Ext, Ice, Sword, Node, FiendsmithEngraver, LacrimaCrimsonTears, FabledLurrie);
            Prefer(Materials, LavalvalChain, Snow, Sword, Node, Ice, Ext);
            Prefer(Materials, Emeral, Snow, Sword, Node, Ice, Ext);
        }
    }
}
