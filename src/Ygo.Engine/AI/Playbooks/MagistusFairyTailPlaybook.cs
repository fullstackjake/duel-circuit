namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Magistus Fairy Tail (2026): two Spellcaster engines. Fairy Tail - Luna searches another 1850-ATK Spellcaster, and
    /// Matchgiru then Special Summons itself and searches Tails of the Fairy Tails, which Fusion Summons Weaver (it calls
    /// a Fairy Tail from the Deck) or Teller (a negate). Spenta discards itself to search Crowley, which Special Summons
    /// itself when added and Fusion Summons a Magistus (Aiwass steals a monster on the opponent's turn). Fairy Tail - Snow
    /// comes back from the GY by banishing 7 cards: late in the duel, not on turn 1.
    /// </summary>
    public sealed class MagistusFairyTailPlaybook : Playbook
    {
        private const uint Genni = 64756282, WitchOfTheBlackForest = 78010363, Snow = 55623480, Luna = 86937530, RillionaVerre = 72498838,
            ZoroaFlame = 36099130, Spenta = 42544773, Crowley = 875572, Regulus = 96228804, Matchgiru = 19144622, Genia = 40041559,
            InstantFusion = 1845204, Tails = 82119326, Ball = 56725612, Empire = 34041788, Lacrima = 73664385, OnceUpon = 19326613,
            Chorozo = 66532962, Teller = 4026187, Weaver = 78021082, Aiwass = 35877582, ZoroaVerethragna = 37260677,
            Exciton = 46772449, Ninaruru = 8660395, RillionaWardrobe = 74689476, Wiccat = 27632520, Artemis = 34755994,
            Endymion = 20714553, SummonSorceress = 61665245;

        public override string Deck => "magistus-fairy-tail";

        public MagistusFairyTailPlaybook()
        {
            LeadWithRules();
            // Spenta into Crowley, which summons itself and Fusion Summons.
            Activate("Spenta, the Magistus Sealer discards itself for Crowley", Spenta,
                () => V.InHand(Spenta) > 0 && V.InDeck(Crowley) > 0 && V.IsMyMainPhase);
            ChainWhen("Crowley, the Gifted Magistus summons itself and Fusion Summons", Crowley, () => V.IsMyTurn);
            // Luna searches; Matchgiru joins and searches Tails; Genia joins off any monster.
            NormalSummon("Fairy Tail - Luna searches an 1850-ATK Spellcaster", Luna);
            Activate("Fairy Tail - Matchgiru joins and finds Tails of the Fairy Tails", Matchgiru);
            Activate("Genia of the Ring joins", Genia, () => V.MyMonsterCount > 0 && V.MyMonsterCount < 5);
            Activate("Regulus, the Prince of Endymion joins", Regulus);
            Activate("Empire of Endymion finds Regulus", Empire);
            Activate("Weaver of Fairy Tails calls a Fairy Tail from the Deck", Weaver);
            Activate("Tails of the Fairy Tails Fusion Summons", Tails, () => V.MyMonsterCount >= 2);
            Activate("Instant Fusion makes Weaver", InstantFusion, () => V.OnField(Weaver) == 0 && V.InExtraDeck(Weaver) > 0);
            NormalSummon("Rilliona, the Magistus of Verre finds Lacrima of Light", RillionaVerre);
            Activate("Verre Magic - Lacrima of Light", Lacrima, () => V.IsMyTurn);
            SpecialSummon("Xyz into Fairy Tail - Wiccat", Wiccat, () => V.MyMonsterCount >= 3);

            // Spenta's search is worth more than its body.
            HoldNormalSummon(Spenta, () => V.InDeck(Crowley) > 0);
            // Snow from the GY banishes 7 cards: only with a GY to pay it from.
            HoldEffect(Snow, 1, () => V.GraveyardCount < 12);
            // Aiwass equips itself to steal a monster: it waits for one of theirs.
            HoldActivation(Aiwass, () => V.OpponentMonsterCount == 0);
            HoldSpecialSummon(Chorozo, () => V.IsFirstTurn);
            HoldSpecialSummon(Exciton);
            HoldSpecialSummon(SummonSorceress);
            // Not removal: Weaver summons "from your Deck or banishment", Genia protects the monster it targets.
            OwnTargets(Weaver, Genia, Spenta);
            Boss(Teller, Aiwass, ZoroaVerethragna);

            Prefer(Searches, Spenta, Crowley, RillionaVerre, ZoroaFlame);
            PreferIf(() => V.InHand(Matchgiru) == 0 && V.InDeck(Matchgiru) > 0, Searches, Luna, Matchgiru);
            Prefer(Searches, Luna, Genia, Snow, Luna);
            PreferIf(() => V.InHand(Tails) == 0, Searches, Matchgiru, Tails);
            Prefer(Searches, Matchgiru, Ball, OnceUpon, Tails);
            Prefer(Searches, Ball, Matchgiru, Luna, Snow);
            Prefer(Searches, RillionaVerre, Lacrima);
            Prefer(SummonPicks, Crowley, Aiwass, ZoroaVerethragna, Chorozo);
            Prefer(SummonPicks, Tails, Weaver, Teller);
            PreferIf(() => V.OnField(Weaver) > 0, SummonPicks, Tails, Teller);
            Prefer(SummonPicks, InstantFusion, Weaver, Teller);
            Prefer(SummonPicks, Weaver, Matchgiru, Luna, Snow);
            Prefer(SummonPicks, Lacrima, RillionaVerre, Spenta, ZoroaFlame);
            Prefer(Materials, 0, Luna, Genia, Regulus, Matchgiru, Spenta, Crowley, WitchOfTheBlackForest);
            Avoid(Materials, 0, Teller, Aiwass, Weaver);
            Prefer(ToGrave, 0, Spenta, WitchOfTheBlackForest, Snow, OnceUpon, Genni);
        }
    }
}
