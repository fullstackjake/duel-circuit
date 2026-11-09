using System.Linq;

namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// ABC / Union Hangar (2016): Union Hangar (Terraforming finds it) searches a LIGHT Machine Union, and when one is
    /// summoned it equips another from the Deck. A-Assault Core, B-Buster Drake and C-Crush Wyvern each refill when they
    /// go from the field to the GY; with all three on the field or in the GY they become ABC-Dragon Buster, which banishes a
    /// card each turn (it's the deck's boss: a Union equipped to it protects it). Brilliant Fusion sends Gem-Knight Garnet
    /// and a Union from the Deck for Gem-Knight Seraphinite (an extra Normal Summon); the Gadgets summon a second Level 4
    /// Machine for a Rank 4 (Gear Gigant X searches a Union, and its materials fill the GY); Instant Fusion makes Norden,
    /// which revives a Union. Going second the same Buster banishes through the board, with Dark Hole and Twin Twisters.
    /// </summary>
    public sealed class AbcPlaybook : Playbook
    {
        private const uint A = 30012506, B = 77411244, C = 3405259, GoldGadget = 55010259, SilverGadget = 29021114,
            PhotonThrasher = 65367484, Garnet = 91731841, TrickClown = 67696066, MaxxC = 23434538,
            BrilliantFusion = 7394770, InstantFusion = 1845204, PotOfDesires = 35261759, Terraforming = 73628505,
            Hangar = 66399653, DarkHole = 53129443, TwinTwisters = 43898403, FoolishBurial = 81439173,
            Buster = 1561110, Norden = 17412721, Seraphinite = 3113836, GearGigant = 28912357, Castel = 82633039,
            DireWolf = 95169481, AbyssDweller = 21044178, Tsukuyomi = 73289035, Emeral = 581014, Utopia = 84013237,
            Rhapsody = 93568288, UtopiaLightning = 56832966;

        private static readonly uint[] Unions = { A, B, C };

        public override string Deck => "abc";

        public AbcPlaybook()
        {
            Activate("Pot of Desires", PotOfDesires);
            Activate("Terraforming finds Union Hangar", Terraforming, () => !V.InSpellTrapZone(Hangar) && !V.InHandAny(Hangar));
            Activate("Union Hangar searches a Union", Hangar, () => !V.InSpellTrapZone(Hangar));
            Activate("Foolish Burial sends a Union", FoolishBurial, () => V.InGraveyardAny(Unions) < 3);
            // The boss whenever A, B and C are out: first, before anything else spends them.
            SpecialSummon("A, B and C into ABC-Dragon Buster", Buster, perTurn: 2);
            // A Union on the Buster protects it (and comes back off it in the opponent's turn).
            Activate("a Union equips to ABC-Dragon Buster", A, () => V.OnField(Buster) > 0 && V.OnField(A) > 0);
            Activate("a Union equips to ABC-Dragon Buster", B, () => V.OnField(Buster) > 0 && V.OnField(B) > 0);
            Activate("a Union equips to ABC-Dragon Buster", C, () => V.OnField(Buster) > 0 && V.OnField(C) > 0);
            Activate("Brilliant Fusion sends Garnet and a Union", BrilliantFusion, () => V.InHand(BrilliantFusion) > 0 && V.OnField(Seraphinite) == 0);
            SpecialSummon("Photon Thrasher on an empty field", PhotonThrasher, () => V.MyMonsterCount == 0);
            // A Gadget brings a second Level 4 Machine from the hand: a Rank 4 that fills the GY.
            NormalSummon("Gold Gadget brings a Machine", GoldGadget, () => V.InHandAny(A, B, C, SilverGadget) || V.InHand(GoldGadget) > 1);
            NormalSummon("Silver Gadget brings a Machine", SilverGadget, () => V.InHandAny(A, B, C, GoldGadget) || V.InHand(SilverGadget) > 1);
            NormalSummon("A-Assault Core (Union Hangar equips another Union)", A, () => V.InSpellTrapZone(Hangar));
            NormalSummon("B-Buster Drake (Union Hangar equips another Union)", B, () => V.InSpellTrapZone(Hangar));
            NormalSummon("C-Crush Wyvern (Union Hangar equips another Union)", C, () => V.InSpellTrapZone(Hangar));
            NormalSummon("A-Assault Core", A);
            Activate("Instant Fusion: Norden revives a Union", InstantFusion, () => V.InGraveyardAny(Unions) > 0 && V.MyMonsterCount <= 3);
            // Rank 4: Gear Gigant X searches the missing Union and its materials go to the GY for the Buster.
            SpecialSummon("two Level 4 Machines into Gear Gigant X", GearGigant, () => !BusterReady);
            Activate("Gear Gigant X searches", GearGigant, () => V.IsMyTurn);
            SpecialSummon("Norden and a Union into a Rank 4", Castel, () => V.OnField(Norden) > 0);
            SpecialSummon("Norden and a Union into a Rank 4", DireWolf, () => V.OnField(Norden) > 0);

            // Brilliant Fusion's ATK boost costs a Spell: only for an attack, never on turn 1.
            // (A new copy in hand is another Fusion Summon: that one's fine.)
            HoldActivation(BrilliantFusion, () => V.InSpellTrapZone(BrilliantFusion) && V.InHand(BrilliantFusion) == 0
                                                  && (V.IsFirstTurn || V.OpponentMonsterCount == 0));
            HoldSpecialSummon(Norden, () => V.InGraveyardAny(Unions) == 0);
            HoldSpecialSummon(Rhapsody);
            HoldSpecialSummon(UtopiaLightning, () => V.IsFirstTurn);
            Boss(Buster);

            Prefer(Searches, Hangar, A, B, C);
            PreferIf(() => V.InHand(A) > 0, Searches, Hangar, B, C);
            Prefer(Searches, Terraforming, Hangar);
            Prefer(Searches, B, C, A);
            PreferIf(() => V.InHand(C) > 0 || V.InGraveyard(C) > 0, Searches, B, A);
            Prefer(Searches, A, C, B);
            Prefer(Searches, GearGigant, C, B, A, GoldGadget, SilverGadget);
            PreferIf(() => V.InGraveyard(C) > 0, Searches, GearGigant, B, A);
            // Union Hangar's equip: B first (it searches when it goes to the GY), then C.
            Prefer(new[] { DuelContext.HintEquip }, Hangar, B, C, A);
            // Brilliant Fusion: Garnet and a Union from the Deck (C first: it's the hardest to get into the GY).
            Prefer(Materials, BrilliantFusion, Garnet, C, B, A, PhotonThrasher);
            Prefer(Materials, Buster, A, B, C);
            Prefer(Materials, 0, GoldGadget, SilverGadget, A, B, C, PhotonThrasher, Garnet);
            Avoid(Materials, 0, Buster);
            Prefer(SummonPicks, GoldGadget, A, B, C, SilverGadget, GoldGadget);
            Prefer(SummonPicks, SilverGadget, A, B, C, GoldGadget, SilverGadget);
            Prefer(SummonPicks, InstantFusion, Norden, Seraphinite);
            Prefer(SummonPicks, Norden, A, B, C, GoldGadget, SilverGadget);
            Prefer(SummonPicks, 0, Buster, A, B, C);
            Prefer(new[] { DuelContext.HintDetachMaterial }, GearGigant, A, B, C);
            // ABC-Dragon Buster's discard: spare monsters before Spells/Traps.
            Prefer(ToGrave, Buster, A, B, C, TrickClown, Garnet, MaxxC);
            Prefer(ToGrave, BrilliantFusion, TwinTwisters, DarkHole);
            Prefer(ToGrave, TwinTwisters, A, B, C, Garnet, TrickClown);
        }

        /// <summary>A, B and C all on the field (equipped counts) or in the GY, and a Buster left to make.</summary>
        private bool BusterReady => V.InExtraDeck(Buster) > 0 && Unions.All(u => V.OnField(u) > 0 || V.InSpellTrapZone(u) || V.InGraveyard(u) > 0);
    }
}
