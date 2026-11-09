namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Fire King Snake-Eye (2024): the Snake-Eye engine (Bonfire for Ash, WANTED for Diabellstar, Poplar and Oak) with
    /// the Fire Kings, which destroy each other for value: Fire King Island and Ulcanix destroy a FIRE monster to search,
    /// Kirin destroys one to summon itself, and every destroyed Fire King floats (Kirin revives, Ulcanix calls Garunix,
    /// Sacred Garunix and Ponix come back). The FIRE bodies become Promethean Princess or Garunix Eternity.
    /// </summary>
    public sealed class FireKingSnakeEyePlaybook : Playbook
    {
        private const uint Ash = 9674034, Oak = 45663742, Ponix = 90681088, Poplar = 90241276, Arvata = 18621798, Ulcanix = 44455560,
            Diabellstar = 72270339, HighGarunix = 23015896, Flamberge = 48452496, Kirin = 2526224, SacredGarunix = 66431519,
            OriginalSpoils = 89023486, Hallowed = 94845588, OneForOne = 2295440, Bonfire = 85106525, Island = 57554544,
            Deception = 66328392, Sanctuary = 65305978, Wanted = 80845034, DDCrow = 24508238, Fuwalos = 42141493,
            Silvia = 46396218, Rcielago = 73391962, Eternity = 64182380, Princess = 2772337, Anima = 94259633,
            Hiita = 48815792, LittleKnight = 29301450, Zealantis = 45112597, Amblowhale = 20665527;

        public override string Deck => "fire-king-snake-eye";

        public FireKingSnakeEyePlaybook()
        {
            Activate("Bonfire searches Snake-Eye Ash", Bonfire);
            Activate("WANTED searches Diabellstar", Wanted, () => V.InHand(Diabellstar) == 0);
            Activate("Fire King Sanctuary places Fire King Island", Sanctuary, () => !V.InSpellTrapZone(Island));
            Activate("Fire King Island", Island, () => !V.InSpellTrapZone(Island));
            Activate("Fire King Island destroys for a Fire King", Island, () => V.InHandAny(Kirin, Arvata, Ponix) || V.MyMonsterCount > 0, perTurn: 2);
            NormalSummon("Snake-Eye Ash searches a Level 1 FIRE", Ash);
            NormalSummon("Fire King Courtier Ulcanix destroys to search", Ulcanix, () => V.InHand(Ash) == 0);
            Activate("Snake-Eye Ash", Ash, perTurn: 2);
            Activate("Snake-Eyes Poplar", Poplar, perTurn: 2);
            Activate("Snake-Eye Oak", Oak, perTurn: 2);
            Activate("Diabellstar sets a Sinful Spoils", Diabellstar);
            SpecialSummon("Diabellstar summons itself", Diabellstar);
            Activate("Original Sinful Spoils", OriginalSpoils);
            Activate("Fire King High Avatar Kirin destroys to summon itself", Kirin, () => V.MyMonsterCount > 0);
            Activate("Sacred Fire King Garunix", SacredGarunix);
            Activate("Snake-Eyes Flamberge Dragon", Flamberge, perTurn: 2);
            Activate("Deception of the Sinful Spoils finds an Azamina card", Deception, () => V.HandCount >= 3);
            Activate("The Hallowed Azamina", Hallowed);
            SpecialSummon("two Level 8s into Garunix Eternity", Eternity, () => V.OpponentMonsterCount > 0 || V.OpponentCardCount > 1);
            SpecialSummon("FIRE bodies into Promethean Princess", Princess, () => V.MyMonsterCount >= 2);
            Activate("Promethean Princess revives a FIRE", Princess);
            SpecialSummon("two bodies into S:P Little Knight (going first)", LittleKnight, () => V.IsFirstTurn && V.MyMonsterCount >= 2);
            SpecialSummon("Relinquished Anima steals a monster", Anima, () => V.OpponentMonsterCount > 0);

            HoldNormalSummon(Diabellstar);
            HoldNormalSummon(Poplar, () => V.InHand(Ash) > 0);
            HoldSpecialSummon(Zealantis);
            HoldSpecialSummon(Amblowhale, () => V.OpponentCardCount == 0);

            Prefer(Searches, Bonfire, Ash, Poplar, Oak);
            Prefer(Searches, Wanted, Diabellstar);
            Prefer(Searches, Ash, Poplar, Ponix, Oak);
            Prefer(Searches, Poplar, OriginalSpoils, Wanted);
            Prefer(Searches, Ponix, Sanctuary, Island);
            Prefer(Searches, Island, Kirin, Ulcanix, Ponix, Arvata);
            Prefer(Searches, Ulcanix, Kirin, SacredGarunix, Ponix, Arvata);
            Prefer(Searches, 0, Ash, Diabellstar, Kirin, OriginalSpoils);
            Prefer(SummonPicks, 0, Flamberge, Ash, Poplar, Diabellstar, Oak, Kirin, HighGarunix);
            Prefer(Materials, 0, Poplar, Oak, Ash, Ponix, Ulcanix, Arvata, Diabellstar);
            Avoid(Materials, 0, Flamberge, Princess, Kirin);
            // Destroying our own FIRE monsters: the ones that float or come back.
            Prefer(new[] { DuelContext.HintDestroy }, 0, Ponix, Ulcanix, Kirin, Arvata, SacredGarunix, HighGarunix);
            Prefer(ToGrave, 0, Poplar, Flamberge, Diabellstar, DDCrow, Fuwalos);
        }
    }
}
