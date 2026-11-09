namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Zoodiac (2017): one Level 4 Zoodiac becomes a stack of Rank 4 Xyz. Ratpier summons a second Ratpier; two Level 4s
    /// make Broadbull, which searches another Zoodiac; then Drident (destroys a card) or Tigermortar goes on top using the
    /// Zoodiac Xyz as its material. Whiptail and Thoroughblade attach themselves from the hand. Zoodiac Barrage destroys a
    /// card to summon a Zoodiac from the Deck; Fire Formation - Tenki searches Ratpier; Instant Fusion makes Elder Entity
    /// Norden to revive a Zoodiac for another Rank 4.
    /// </summary>
    public sealed class ZoodiacPlaybook : Playbook
    {
        private const uint Ratpier = 78872731, Thoroughblade = 77150143, Whiptail = 31755044, Barrage = 46060017, Tenki = 57103969,
            Broadbull = 85115440, Drident = 48905153, Tigermortar = 11510448, Boarbow = 74393852, Emeral = 581014,
            Norden = 17412721, InstantFusion = 1845204, Terrortop = 81275020, Taketomborg = 53932291;

        private static readonly uint[] Zoodiacs = { Ratpier, Thoroughblade, Whiptail };
        private static readonly uint[] ZoodiacXyz = { Broadbull, Drident, Tigermortar, Boarbow };

        public override string Deck => "zoodiac";

        public ZoodiacPlaybook()
        {
            Activate("Fire Formation - Tenki finds Ratpier", Tenki, () => V.InHand(Ratpier) == 0 && V.InDeck(Ratpier) > 0);
            NormalSummon("Ratpier (summons another Ratpier)", Ratpier);
            Activate("Zoodiac Barrage summons a Zoodiac", Barrage, () => V.InDeckAny(Zoodiacs) > 0);
            Activate("Instant Fusion: Norden revives a Zoodiac", InstantFusion, () => V.InGraveyardAny(Zoodiacs) > 0);
            SpecialSummon("two Level 4 Zoodiacs into Broadbull", Broadbull, () => V.OnFieldAny(Zoodiacs) >= 2);
            Activate("Broadbull searches a Zoodiac", Broadbull);
            SpecialSummon("Drident on top of a Zoodiac Xyz", Drident, () => V.OnFieldAny(ZoodiacXyz) > 0 && V.OpponentCardCount > 0);
            SpecialSummon("Tigermortar on top of a Zoodiac Xyz", Tigermortar, () => V.OnFieldAny(ZoodiacXyz) > 0 && V.InGraveyardAny(Zoodiacs) > 0);
            SpecialSummon("Boarbow on top for direct attacks", Boarbow, () => !V.IsFirstTurn && V.OnFieldAny(ZoodiacXyz) > 0);
            Activate("Drident destroys a card", Drident, () => V.OpponentCardCount > 0);
            Activate("Tigermortar revives a Zoodiac", Tigermortar);
            SpecialSummon("two Level 4s into Daigusto Emeral", Emeral, () => V.MyMonsterCount >= 2 && V.OnFieldAny(Zoodiacs) < 2);

            Prefer(Searches, Tenki, Ratpier, Thoroughblade, Whiptail);
            Prefer(Searches, Broadbull, Whiptail, Thoroughblade, Ratpier);
            Prefer(Searches, 0, Ratpier, Whiptail, Thoroughblade);
            Prefer(SummonPicks, Barrage, Ratpier, Thoroughblade, Whiptail);
            Prefer(SummonPicks, Norden, Ratpier, Thoroughblade, Whiptail);
            Prefer(SummonPicks, InstantFusion, Norden);
            Prefer(Materials, 0, Ratpier, Whiptail, Thoroughblade, Norden, Taketomborg, Terrortop);
            Prefer(Materials, Drident, Broadbull, Tigermortar, Boarbow);
            Prefer(Materials, Tigermortar, Broadbull, Boarbow);
            HoldNormalSummon(Thoroughblade, () => V.InHand(Ratpier) > 0);
            HoldNormalSummon(Whiptail, () => V.InHand(Ratpier) > 0);
        }
    }
}
