namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Exosister (2022): Level 4 sisters that come in pairs (Elis + Stella, Martha + Elis, Sophia + Irene). Martha summons
    /// itself and Elis from the Deck on an empty field; Pax searches any Exosister (and summons a monster whose partner is
    /// out); Vadis summons a pair from the Deck. Each pair becomes a Rank 4: Mikailis searches an Exosister Spell/Trap,
    /// Kaspitell an Exosister monster. Going first, two Rank 4 Exosisters become Exosisters Magnifica: it banishes a card,
    /// and when the opponent activates something it turns back into Mikailis (which banishes again). Returnia and the
    /// sisters' "a card left a GY" Xyz stay up for the opponent's turn. Going second, Dark Ruler No More and Mikailis /
    /// Returnia's banishing break the board.
    /// </summary>
    public sealed class ExosisterPlaybook : Playbook
    {
        private const uint Elis = 16474916, Martha = 37343995, Stella = 43863925, Sophia = 5352328, Irene = 79858629,
            Pax = 77913594, DarkRulerNoMore = 54693926, Prosperity = 84211599, Terraforming = 73628505, Necrovalley = 47355498,
            Returnia = 197042, Vadis = 77891946,
            Zeus = 90448279, Magnifica = 59242457, Vespenato = 39317553, Mikailis = 42741437, Kaspitell = 78135071,
            Asophiel = 41524885, Gibrine = 5530780, ExcitonKnight = 46772449, GagagaCowboy = 12014404, Lyna = 9839945,
            DayBreaker = 91336701;


        public override string Deck => "exosister";

        public ExosisterPlaybook()
        {
            Activate("Pot of Prosperity digs", Prosperity);
            Activate("Terraforming finds Necrovalley", Terraforming, () => !V.InSpellTrapZone(Necrovalley) && !V.InHandAny(Necrovalley));
            Activate("Necrovalley", Necrovalley, () => !V.InSpellTrapZone(Necrovalley));
            Activate("Dark Ruler No More opens the board", DarkRulerNoMore, () => V.OpponentMonsterCount > 0);
            // Martha first: it needs an empty field (or only Xyz), and brings Elis from the Deck.
            Activate("Exosister Martha summons itself and Elis", Martha);
            Activate("Exosister Pax finds a sister", Pax, () => V.IsMyTurn);
            Activate("Exosister Vadis summons a pair", Vadis, () => V.MyMonsterCount == 0 && V.InHand(Martha) == 0);
            NormalSummon("Exosister Stella summons a sister from the hand", Stella, () => V.InHandAny(Elis, Martha, Sophia, Irene));
            NormalSummon("Exosister Elis", Elis, () => V.InHandAny(Stella, Sophia, Irene) || V.MyMonsterCount > 0);
            NormalSummon("Exosister Sophia draws", Sophia, () => V.MyMonsterCount > 0);
            Activate("Exosister Stella summons a sister", Stella, () => V.OnField(Stella) > 0);
            Activate("Exosister Elis joins", Elis, () => V.InHand(Elis) > 0);
            Activate("Exosister Sophia draws", Sophia, () => V.OnField(Sophia) > 0);
            // Pairs into the Rank 4s: Mikailis (Spell/Trap search) first, then Kaspitell (monster search).
            SpecialSummon("a pair into Exosister Mikailis", Mikailis, () => V.OnFieldAny(Mikailis) == 0);
            Activate("Exosister Mikailis searches", Mikailis, () => V.IsMyTurn);
            SpecialSummon("a pair into Exosister Kaspitell", Kaspitell, () => V.OnFieldAny(Kaspitell) == 0);
            Activate("Exosister Kaspitell searches", Kaspitell, () => V.IsMyTurn);
            SpecialSummon("two Rank 4 Exosisters into Exosisters Magnifica", Magnifica);
            SpecialSummon("a pair into Exosister Mikailis", Mikailis);

            // Rank 4s that leave the Exosister line: Vespenato eats a Rank 4, the rest aren't Exosister (Vadis locks them).
            HoldSpecialSummon(Vespenato);
            HoldSpecialSummon(GagagaCowboy);
            HoldSpecialSummon(Lyna);
            HoldSpecialSummon(DayBreaker);
            HoldSpecialSummon(ExcitonKnight, () => V.IsFirstTurn || V.OpponentCardCount < 4);
            HoldSpecialSummon(Zeus, () => V.OnField(Magnifica) > 0);
            // Martha's own summon needs an empty field: never Normal Summon it.
            HoldNormalSummon(Martha);
            HoldNormalSummon(Irene, () => V.InHandAny(Stella, Elis, Sophia));
            // Returnia waits for a monster to banish (or a GY card): not a chained Spell that resolves anyway.
            HoldActivation(Returnia, () => !V.IsMyTurn && V.OpponentMonsterCount == 0);
            Boss(Magnifica);

            Prefer(Searches, Pax, Martha, Stella, Elis, Returnia);
            PreferIf(() => V.MyMonsterCount > 0 || V.InHand(Martha) > 0, Searches, Pax, Stella, Elis, Returnia);
            Prefer(Searches, Mikailis, Returnia, Vadis, Pax);
            PreferIf(() => V.InSpellTrapZone(Returnia) || V.InHand(Returnia) > 0, Searches, Mikailis, Pax, Vadis);
            Prefer(Searches, Kaspitell, Stella, Elis, Martha, Sophia, Irene);
            Prefer(Searches, Prosperity, Pax, Martha, Stella, DarkRulerNoMore, Returnia);
            Prefer(SummonPicks, Vadis, Stella, Elis, Martha, Sophia, Irene);
            Prefer(SummonPicks, Stella, Elis, Sophia, Irene, Martha);
            Prefer(SummonPicks, 0, Magnifica, Mikailis, Kaspitell, Gibrine, Asophiel);
            // Magnifica from the Rank 4s that already searched; the sisters for the Rank 4s.
            Prefer(Materials, Magnifica, Kaspitell, Mikailis, Asophiel, Gibrine);
            Prefer(Materials, 0, Elis, Stella, Martha, Sophia, Irene);
            Avoid(Materials, 0, Magnifica);
            // Pot of Prosperity banishes Extra Deck cards: never Magnifica or the Rank 4s it's made from.
            Avoid(Banish, Prosperity, Magnifica, Mikailis, Kaspitell);
            Prefer(Banish, Prosperity, Vespenato, GagagaCowboy, Lyna, DayBreaker, ExcitonKnight, Zeus);
            Prefer(new[] { DuelContext.HintToDeck }, Irene, Sophia, Martha);
        }
    }
}
