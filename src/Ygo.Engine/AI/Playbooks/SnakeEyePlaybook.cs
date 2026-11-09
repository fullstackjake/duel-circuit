using System;

namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Snake-Eye / Diabellstar (2024): Bonfire searches a Pyro and WANTED searches Diabellstar; Snake-Eye Ash, Normal
    /// Summoned, adds a Level 1 FIRE (Poplar, Oak); Poplar and Diabellstar Special Summon themselves; Flamberge Dragon comes
    /// back from the hand or GY. The FIRE bodies become Link monsters: Promethean Princess (Link-3, revives FIRE monsters),
    /// S:P Little Knight going first, I:P Masquerena to Link again on the opponent's turn, Relinquished Anima to steal.
    /// </summary>
    public sealed class SnakeEyePlaybook : Playbook
    {
        private const uint Ash = 9674034, Flamberge = 48452496, Poplar = 90241276, SnakeEyesDiabellstar = 27260347, Oak = 45663742,
            Diabellstar = 72270339, Bonfire = 85106525, Wanted = 80845034, OriginalSpoils = 89023486, Temple = 53639887,
            Anima = 94259633, LinkSpider = 98978921, Princess = 2772337, Masquerena = 65741786, LittleKnight = 29301450,
            Hiita = 48815792, Dharc = 8264361, Accesscode = 86066372, Apollousa = 4280258, KnightmarePhoenix = 2857636,
            // No Limits (Junia's nl-snake-eye, docs/dev/NO-LIMITS-SPEC-2026-11-02.md 7.2): cards the as-built list doesn't run.
            UnionCarrier = 83152482, CardOfSafeReturn = 57953380, SoulCharge = 54447022, Blaster = 53804307;

        private const uint Fire = 0x04;

        private static readonly uint[] Bodies = { Ash, Poplar, Oak, Diabellstar, SnakeEyesDiabellstar, Flamberge };

        public override string Deck => "snake-eye";

        public SnakeEyePlaybook()
        {
            Activate("Bonfire searches a Pyro", Bonfire);
            Activate("WANTED searches Diabellstar", Wanted, () => V.InHand(Diabellstar) == 0);
            NormalSummon("Snake-Eye Ash searches a Level 1 FIRE", Ash);
            Activate("Snake-Eye effect", Ash, perTurn: 2);
            Activate("Poplar summons itself", Poplar, perTurn: 2);
            Activate("Oak", Oak, perTurn: 2);
            Activate("Diabellstar summons itself", Diabellstar);
            Activate("Snake-Eyes Diabellstar", SnakeEyesDiabellstar);
            Activate("Original Sinful Spoils", OriginalSpoils);
            Activate("Divine Temple of the Snake-Eye", Temple);
            Activate("Flamberge Dragon comes back", Flamberge, perTurn: 2);
            SpecialSummon("FIRE bodies into Promethean Princess", Princess, () => V.MyMonsterCount >= 2);
            SpecialSummon("two bodies into S:P Little Knight (going first)", LittleKnight, () => V.IsFirstTurn && V.MyMonsterCount >= 2);
            SpecialSummon("Relinquished Anima steals a monster", Anima, () => V.OpponentMonsterCount > 0);
            SpecialSummon("Hiita the Fire Charmer", Hiita, () => V.OpponentMonsterCount > 0);
            SpecialSummon("Accesscode Talker to finish", Accesscode, () => !V.IsFirstTurn && V.MyMonsterCount >= 3);
            Activate("Promethean Princess revives a FIRE", Princess);

            Prefer(Searches, Bonfire, Ash, Diabellstar, Poplar);
            Prefer(Searches, Wanted, Diabellstar);
            Prefer(Searches, Ash, Poplar, Oak);
            Prefer(Searches, Diabellstar, OriginalSpoils, Temple);
            Prefer(Searches, 0, Ash, Poplar, Diabellstar, OriginalSpoils);
            Prefer(SummonPicks, 0, Flamberge, Ash, Poplar, Diabellstar, Oak);
            Prefer(Materials, 0, Poplar, Oak, Ash, Diabellstar, SnakeEyesDiabellstar);
            Avoid(Materials, 0, Flamberge, Princess);
            HoldNormalSummon(Diabellstar);
            HoldNormalSummon(Poplar, () => V.InHand(Ash) > 0);

            // ---- No Limits (Junia's nl-snake-eye, 7.2): the Fiendsmith engine and the four picks. Additive only: every card
            // below is one the as-built snake-eye list doesn't run, so the base plays as before (7.1).
            FiendsmithEngine();
            // Union Carrier: two FIRE monsters; equip Ash or Poplar from the Deck as a face-up card for Original Sinful Spoils.
            SpecialSummon("two FIRE bodies into Union Carrier", UnionCarrier,
                () => V.MyMonsterCount >= 2 && (V.InHand(OriginalSpoils) > 0 || V.InSpellTrapZone(OriginalSpoils)) && V.InDeckAny(Ash, Poplar) > 0);
            ActivateEffect("Union Carrier equips Ash or Poplar from the Deck", UnionCarrier, 0, () => V.InDeckAny(Ash, Poplar) > 0);
            Prefer(new[] { DuelContext.HintEquip }, UnionCarrier, Poplar, Ash);
            Prefer(Materials, UnionCarrier, Oak, Poplar, Ash, SnakeEyesDiabellstar, Diabellstar);
            // Card of Safe Return: out early, so every revive draws.
            Activate("Card of Safe Return", CardOfSafeReturn, () => V.IsMyMainPhase);
            // Soul Charge: at least 2 FIRE bodies back for a Link climb, only at 4,000 LP or more, and never below 2,000.
            Activate("Soul Charge revives the FIRE bodies", SoulCharge,
                () => V.IsMyMainPhase && V.MyLifePoints >= 4000 && V.GraveyardMonstersOfAttribute(Fire) >= 2 && V.FreeMonsterZones >= 2);
            TakeAtMost(SummonPicks, SoulCharge, () => Math.Min(3, Math.Min(V.FreeMonsterZones, (V.MyLifePoints - 2000) / 1000)));
            Prefer(SummonPicks, SoulCharge, Flamberge, Diabellstar, Ash, Poplar, Oak, SnakeEyesDiabellstar, Blaster);
            // Blaster: discard it with a FIRE to destroy a card; revive it by banishing two FIRE from the GY (it searches when banished).
            ActivateEffect("Blaster discards with a FIRE to destroy a card", Blaster, 2,
                () => V.InHand(Blaster) > 0 && V.OpponentCardCount > 0 && V.HandMonstersOfAttribute(Fire) >= 2 && V.HandCount >= 3);
            ActivateEffect("Blaster revives by banishing two FIRE", Blaster, 0,
                () => V.IsMyMainPhase && V.InGraveyard(Blaster) > 0 && V.GraveyardMonstersOfAttribute(Fire) >= 3 && V.FreeMonsterZones > 0);
            Prefer(ToGrave, Blaster, Oak, Poplar, Ash, Flamberge);
            Prefer(Banish, Blaster, Oak, Poplar, Ash);
            Avoid(Banish, Blaster, Flamberge, Diabellstar, SnakeEyesDiabellstar);
            Prefer(Searches, Blaster, Flamberge, Blaster);
            Avoid(Materials, 0, Blaster);
        }
    }
}
