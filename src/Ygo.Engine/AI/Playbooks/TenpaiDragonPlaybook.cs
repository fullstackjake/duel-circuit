namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Tenpai Dragon (2024): clear the board first (Lightning Storm needs no face-up cards; Harpie's Feather Duster; Santa
    /// Claws Tributes an opposing monster), then Sangen Summoning (via Terraforming) searches a Tenpai Dragon (Genroku,
    /// added, summons itself as a Tuner), Paidra adds Sangen Kaimen, Fadra revives a FIRE Dragon and Chundra joins from
    /// the hand. The Tenpai Dragons Synchro Summon during the Battle Phase, so the Main Phase only builds bodies.
    /// </summary>
    public sealed class TenpaiDragonPlaybook : Playbook
    {
        private const uint Fadra = 65326118, Paidra = 39931513, Genroku = 23657016, Chundra = 91810826, SantaClaws = 46565218,
            Magnamhut = 33854624, Harpies = 18144507, Terraforming = 73628505, LightningStorm = 14532163, Prosperity = 84211599,
            SangenSummoning = 30336082, Kaimen = 66730191, Purulia = 84192580, Fuwalos = 42141493, DimensionShifter = 91800273,
            Bident = 82570174, Transcendent = 18969888, Trident = 39402797, Ruddy = 40139997, Kuibelt = 87837090, Hieratic = 24361622,
            // No Limits (Sasha's nl-tenpai, docs/dev/NO-LIMITS-SPEC-2026-11-02.md 7.2): cards the as-built list doesn't run.
            Trunade = 42703248, Raigeki = 12580477, Blaster = 53804307, Burner = 53797637;

        private const uint Fire = 0x04;

        private static readonly uint[] TenpaiDragons = { Fadra, Paidra, Genroku, Chundra };

        public override string Deck => "tenpai-dragon";

        public TenpaiDragonPlaybook()
        {
            Activate("Lightning Storm clears the board", LightningStorm, () => V.OpponentCardCount >= 2);
            Activate("Harpie's Feather Duster", Harpies, () => V.OpponentCardCount > V.OpponentMonsterCount);
            SpecialSummon("Santa Claws takes an opposing monster", SantaClaws, () => V.OpponentMonsterCount > 0);
            Activate("Pot of Prosperity digs", Prosperity);
            Activate("Terraforming finds Sangen Summoning", Terraforming, () => !V.InSpellTrapZone(SangenSummoning) && !V.InHandAny(SangenSummoning));
            Activate("Sangen Summoning", SangenSummoning, () => !V.InSpellTrapZone(SangenSummoning));
            Activate("Sangen Summoning searches a Tenpai Dragon", SangenSummoning, perTurn: 2);
            NormalSummon("Tenpai Dragon Paidra adds a Sangen card", Paidra);
            NormalSummon("Tenpai Dragon Fadra revives a Dragon", Fadra, () => V.InGraveyardAny(TenpaiDragons) > 0 || V.InHand(Paidra) == 0);
            NormalSummon("Tenpai Dragon Chundra", Chundra, () => !V.InHandAny(Paidra, Fadra));
            SpecialSummon("Tenpai Dragon Chundra joins", Chundra);
            Activate("Tenpai Dragon Chundra joins", Chundra, () => V.InHand(Chundra) > 0);
            Activate("Sangen Kaimen finds a Tenpai Dragon", Kaimen, () => V.MyMonsterCount < 2);
            Activate("Bystial Magnamhut", Magnamhut);

            SpecialSummon("Synchro into Sangenpai Transcendent Dragion", Transcendent);
            SpecialSummon("Synchro into Trident Dragion", Trident, () => V.OpponentMonsterCount > 0);
            SpecialSummon("Synchro into Kuibelt the Blade Dragon", Kuibelt, () => V.OpponentCardCount > 0);
            SpecialSummon("Synchro into Sangenpai Bident Dragion", Bident);
            HoldSpecialSummon(Hieratic);
            HoldActivation(Kaimen, () => V.MyMonsterCount >= 2);

            Prefer(Searches, SangenSummoning, Genroku, Paidra, Chundra, Fadra);
            Prefer(Searches, Paidra, Kaimen, SangenSummoning);
            Prefer(Searches, Kaimen, Genroku, Chundra, Paidra, Fadra);
            Prefer(Searches, Terraforming, SangenSummoning);
            Prefer(Searches, 0, Genroku, Paidra, Chundra, Kaimen);
            Prefer(ToGrave, SangenSummoning, Fuwalos, Purulia, DimensionShifter, SantaClaws, Fadra, Genroku);
            Prefer(SummonPicks, Genroku, Chundra, Paidra, Fadra);
            Prefer(SummonPicks, Fadra, Paidra, Chundra, Genroku);
            Prefer(SummonPicks, Kaimen, Chundra, Paidra, Fadra);
            Prefer(SummonPicks, 0, Transcendent, Trident, Bident, Ruddy);
            Avoid(Banish, Prosperity, Transcendent, Bident, Trident, Ruddy, Kuibelt);

            // ---- No Limits (Sasha's nl-tenpai, 7.2): the picks. Additive only: every card below is one the as-built
            // tenpai-dragon list doesn't run, so the base plays as before (7.1).
            // Raigeki with 2+ opposing monsters; Giant Trunade before the Battle Phase, once the bodies are out, against set backrow.
            Activate("Raigeki clears their monsters", Raigeki, () => V.OpponentMonsterCount >= 2);
            Activate("Giant Trunade before the Battle Phase", Trunade, () => V.IsMyMainPhase1 && V.OpponentSpellTrapCount > 0 && V.MyMonsterCount > 0);
            // The Dragon Rulers: Burner discards itself with a FIRE to call Blaster from the Deck; Blaster discards with a FIRE to
            // destroy a card, and revives by banishing two FIRE from the GY (it searches a FIRE Dragon when banished).
            ActivateEffect("Burner calls Blaster from the Deck", Burner, 0,
                () => V.IsMyMainPhase1 && V.InHand(Burner) > 0 && V.InDeck(Blaster) > 0 && V.HandMonstersOfAttribute(Fire) >= 2 && V.FreeMonsterZones > 0);
            ActivateEffect("Blaster discards with a FIRE to destroy a card", Blaster, 2,
                () => V.IsMyMainPhase1 && V.InHand(Blaster) > 0 && V.OpponentCardCount > 0 && V.HandMonstersOfAttribute(Fire) >= 2);
            ActivateEffect("Blaster revives by banishing two FIRE", Blaster, 0,
                () => V.IsMyMainPhase1 && V.InGraveyard(Blaster) > 0 && V.GraveyardMonstersOfAttribute(Fire) >= 3 && V.FreeMonsterZones > 0);
            Prefer(ToGrave, Burner, Chundra, Fadra, Genroku, Paidra);
            Prefer(ToGrave, Blaster, Burner, Chundra, Fadra, Genroku, Paidra);
            Prefer(Banish, Blaster, Burner, Genroku, Fadra);
            Avoid(Banish, Blaster, Chundra, Paidra);
            Prefer(Searches, Blaster, Chundra, Paidra, Fadra, Burner);
        }
    }
}
