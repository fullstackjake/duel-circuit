using System.Collections.Generic;
using System.Linq;

namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Plant Synchro (2011), the Basement Game's headliner "Trishula Trish" (docs/dev/HEADLINERS-2026-11-01.md). Trishula, Dragon
    /// of the Ice Barrier is a Level 9 Synchro (a Tuner and two or more non-Tuners) that banishes a card from the opponent's
    /// hand, field and GY. The Plants make the Levels: Dandylion sent to the GY leaves two Level 1 Fluff Tokens, Debris
    /// Dragon (a Level 4 Tuner for Dragons) brings Dandylion back from the GY, so Foolish Burial and Debris Dragon are
    /// Debris + Dandylion + two Fluffs: nine. Glow-Up Bulb and Spore come back from the GY as Tuners for whatever Levels
    /// are left; Lonefire Blossom trades a Plant for one from the Deck. Its deck plays as "#recipe trishula-trish" (the
    /// plant-synchro deck the Basement Game's regulars splash plays without it).
    /// </summary>
    public sealed class TrishulaPlaybook : Playbook
    {
        private const uint Trishula = 52687916, Debris = 14943837, Dandylion = 15341821, Fluff1 = 15341822, Fluff2 = 15341823,
            GlowUp = 67441435, Spore = 11747708, Lonefire = 48686504, Tengu = 10028593, Trooper = 85087012, Sangan = 26202165,
            Fishborg = 93369354, TourGuide = 10802915, Foolish = 81439173, OneForOne = 2295440, Reborn = 83764718, Duality = 98645731,
            Avarice = 67169062, CallOfTheHaunted = 97077563, Veiler = 97268402, Gorz = 44330098,
            BlackRose = 73580471, Formula = 50091196, Armory = 29071332;

        private const uint Stardust = 44508094, Scrap = 76774528, Brionac = 50321796, Catastor = 26593852, AncientFairy = 25862681,
            Android = 43385557, IronChain = 19974580;
        private static readonly uint[] OtherSynchros = { Stardust, Scrap, Brionac, Catastor, AncientFairy, Android, IronChain, Formula, Armory };
        private static readonly uint[] Plants = { Dandylion, GlowUp, Spore, Lonefire };
        private static readonly uint[] Fluffs = { Fluff1, Fluff2 };
        /// <summary>Trishula's materials, best first: the Tuners, then what's spent most cheaply.</summary>
        private static readonly uint[] TrishulaMaterials = { Debris, GlowUp, Spore, Fishborg, Formula, Armory, Fluff1, Fluff2, Dandylion, Tengu, TourGuide, Lonefire, Sangan, Trooper };
        /// <summary>What to discard (One for One, Fishborg) or send first: Dandylion makes Fluffs, the GY Tuners come back.</summary>
        private static readonly uint[] Discards = { Dandylion, GlowUp, Spore, Fishborg, Tengu, TourGuide, Gorz, Veiler };

        public override string Deck => "trishula-trish";

        private bool TrishulaReady => V.InExtraDeck(Trishula) > 0;

        /// <summary>A subset of these Levels, two or more of them, makes exactly <paramref name="sum"/> (Debris: none of Level 4).</summary>
        private static bool Makes(List<int> levels, int sum, bool debris)
        {
            var usable = levels.Where(l => !debris || l != 4).ToList();
            int n = usable.Count;
            for (int mask = 0; mask < 1 << n; mask++)
            {
                int count = 0, total = 0;
                for (int i = 0; i < n; i++)
                    if ((mask & (1 << i)) != 0) { count++; total += usable[i]; }
                if (count >= 2 && total == sum) return true;
            }
            return false;
        }

        /// <summary>Trishula can be made now from the field: a Tuner there and non-Tuners for the rest of nine.</summary>
        private bool NineOnField()
        {
            var tuners = V.FaceUpLevels(true);
            var others = V.FaceUpLevels(false);
            bool debris = V.OnField(Debris) > 0;
            return tuners.Any(t => Makes(others, 9 - t, debris && t == 4));
        }

        /// <summary>A Tuner back from the GY (Glow-Up Bulb at 1, Spore at 1 plus a Plant it banishes) makes nine with the field.</summary>
        private bool NineWithGyTuner()
        {
            if (V.FreeMonsterZones == 0 || V.ControlTuner) return false;
            var others = V.FaceUpLevels(false);
            if (V.InGraveyard(GlowUp) > 0 && Makes(others, 8, false)) return true;
            if (V.InGraveyard(Spore) > 0)
            {
                // Spore banishes a Plant from the GY (or the field) and adds its Level: Dandylion or Lonefire 3, a Glow-Up Bulb 1.
                if (V.InGraveyardAny(Dandylion, Lonefire) > 0 && Makes(others, 5, false)) return true;
                if (V.InGraveyard(GlowUp) > 0 && Makes(others, 7, false)) return true;
            }
            return false;
        }

        /// <summary>A Tuner Normal Summoned from the hand (Level <paramref name="level"/>) makes nine with the field.</summary>
        private bool NineWithHandTuner(uint tuner, int level) =>
            V.InHand(tuner) > 0 && !V.NormalSummonUsed && V.FreeMonsterZones > 0 && Makes(V.FaceUpLevels(false), 9 - level, tuner == Debris);

        private bool AnyHandTuner => NineWithHandTuner(Debris, 4) || NineWithHandTuner(GlowUp, 1) || NineWithHandTuner(Spore, 1) || NineWithHandTuner(Fishborg, 1)
                                     || NineWithHandTuner(Veiler, 1);

        /// <summary>Debris Dragon's line: it's in hand with the Normal Summon free, and Dandylion is in the GY or on its way.</summary>
        private bool DebrisLine => V.InHand(Debris) > 0 && !V.NormalSummonUsed && V.FreeMonsterZones >= 3
                                   && (V.InGraveyard(Dandylion) > 0 || V.InHand(Foolish) > 0 && V.InDeck(Dandylion) > 0
                                       || V.InHand(OneForOne) > 0 && V.InHand(Dandylion) > 0);

        public TrishulaPlaybook()
        {
            var cost = new[] { DuelContext.HintDiscard, DuelContext.HintToGrave };
            // Foolish Burial (or One for One's discard) puts Dandylion in the GY: two Fluffs. Debris Dragon brings it back:
            // Debris 4, Dandylion 3, two Fluffs 1: Trishula.
            ComboRoute("Debris Dragon into Trishula", () => V.IsMyMainPhase && TrishulaReady && DebrisLine)
                .AnyDecision()
                .Counts("summons", () => V.SpecialSummonsThisDuel)
                .Activate("Foolish Burial sends Dandylion", Foolish, () => V.InGraveyard(Dandylion) == 0)
                    .Pick(ToGrave, Dandylion)
                .Activate("One for One discards Dandylion", OneForOne, () => V.InGraveyard(Dandylion) == 0 && V.InHand(Dandylion) > 0)
                    .Pick(cost, Dandylion)
                    .Pick(SummonPicks, GlowUp, Spore, Fishborg)
                .NormalSummon("Debris Dragon brings Dandylion back", Debris, () => V.InGraveyard(Dandylion) > 0)
                    .Pick(SummonPicks, Dandylion, GlowUp, Spore)
                .SpecialSummon("Trishula, Dragon of the Ice Barrier", Trishula, NineOnField)
                    .Pick(Materials, TrishulaMaterials)
                    .Payoff();
            // The garden: bodies from earlier turns, and a Tuner from the hand, the field or the GY for the rest of nine.
            // A Tuner from the hand for the rest of nine.
            ComboRoute("A Tuner to nine", () => V.IsMyMainPhase && TrishulaReady && !NineOnField() && !NineWithGyTuner() && AnyHandTuner)
                .AnyDecision()
                .Counts("summons", () => V.SpecialSummonsThisDuel)
                .NormalSummon("Debris Dragon", Debris, () => NineWithHandTuner(Debris, 4))
                    .Pick(SummonPicks, Dandylion, GlowUp, Spore)
                .NormalSummon("Glow-Up Bulb", GlowUp, () => NineWithHandTuner(GlowUp, 1))
                .NormalSummon("Spore", Spore, () => NineWithHandTuner(Spore, 1))
                .NormalSummon("Fishborg Blaster", Fishborg, () => NineWithHandTuner(Fishborg, 1))
                .NormalSummon("Effect Veiler", Veiler, () => NineWithHandTuner(Veiler, 1))
                .SpecialSummon("Trishula, Dragon of the Ice Barrier", Trishula, NineOnField)
                    .Pick(Materials, TrishulaMaterials)
                    .Payoff();
            ComboRoute("Plants to nine", () => V.IsMyMainPhase && TrishulaReady && (NineOnField() || NineWithGyTuner()))
                .AnyDecision()
                .Counts("summons", () => V.SpecialSummonsThisDuel)
                .Activate("Glow-Up Bulb comes back", GlowUp, () => !NineOnField())
                .Activate("Spore comes back", Spore, () => !NineOnField())
                    .Pick(Banish, Dandylion, Lonefire, GlowUp, Spore)
                .SpecialSummon("Trishula, Dragon of the Ice Barrier", Trishula, NineOnField)
                    .Pick(Materials, TrishulaMaterials)
                    .Payoff();

            // Any other turn the rules make nine: the route waits for Trishula all turn.
            ComboRoute("The garden to nine", () => V.IsMyMainPhase && TrishulaReady)
                .AnyDecision()
                .Waiting()
                .Counts("summons", () => V.SpecialSummonsThisDuel)
                .SpecialSummon("Trishula, Dragon of the Ice Barrier", Trishula)
                    .Pick(Materials, TrishulaMaterials)
                    .Payoff();

            // ---- Building toward nine (the rules, checked in order).
            SpecialSummon("Trishula, Dragon of the Ice Barrier", Trishula);
            NormalSummon("Debris Dragon makes nine", Debris, () => NineWithHandTuner(Debris, 4));
            NormalSummon("Glow-Up Bulb makes nine", GlowUp, () => NineWithHandTuner(GlowUp, 1));
            NormalSummon("Spore makes nine", Spore, () => NineWithHandTuner(Spore, 1));
            NormalSummon("Fishborg Blaster makes nine", Fishborg, () => NineWithHandTuner(Fishborg, 1));
            NormalSummon("Effect Veiler makes nine", Veiler, () => NineWithHandTuner(Veiler, 1));
            Activate("Foolish Burial sends Dandylion", Foolish, () => V.InDeck(Dandylion) > 0);
            Activate("One for One summons a Tuner", OneForOne, () => V.FreeMonsterZones > 0);
            Activate("Sangan's search", Sangan);
            Activate("Pot of Duality digs", Duality);
            Activate("Pot of Avarice", Avarice);
            NormalSummon("Debris Dragon brings back a Plant", Debris, () => V.InGraveyardAny(Dandylion, GlowUp, Spore) > 0);
            NormalSummon("Lonefire Blossom", Lonefire, () => !DebrisLine);
            Activate("Lonefire Blossom trades a Plant for one from the Deck", Lonefire, () => V.InDeckAny(Plants) > 0, perTurn: 2);
            NormalSummon("Tour Guide From the Underworld", TourGuide);
            NormalSummon("Reborn Tengu", Tengu);
            NormalSummon("Card Trooper", Trooper);
            Activate("Card Trooper mills three", Trooper);
            Activate("Fishborg Blaster comes back", Fishborg, () => V.InHandAny(Dandylion, Tengu, Gorz) || NineWithGyTuner());
            Activate("Glow-Up Bulb comes back", GlowUp, NineWithGyTuner);
            Activate("Spore comes back", Spore, NineWithGyTuner);
            Activate("Call of the Haunted brings back Dandylion", CallOfTheHaunted, () => V.InGraveyard(Dandylion) > 0 && V.FreeMonsterZones > 0);

            // Black Rose Dragon wipes our own board: never while Trishula can still be made.
            HoldSpecialSummon(BlackRose, () => TrishulaReady);
            // The other Synchros spend the Levels nine is made of: none while Trishula is still to come.
            foreach (var other in OtherSynchros) HoldSpecialSummon(other, () => TrishulaReady && V.OnField(Trishula) == 0);
            // The Debris turn keeps the Normal Summon for Debris.
            HoldNormalSummons(() => DebrisLine && TrishulaReady);
            Boss(Trishula);

            Prefer(ToGrave, Foolish, Dandylion, GlowUp, Spore);
            Prefer(Searches, Sangan, Debris, Dandylion, GlowUp, Spore);
            Prefer(Searches, Duality, Debris, Foolish, OneForOne, Dandylion, Lonefire, GlowUp);
            Prefer(SummonPicks, Lonefire, Dandylion, GlowUp, Spore);
            Prefer(SummonPicks, OneForOne, GlowUp, Spore, Fishborg);
            Prefer(SummonPicks, Debris, Dandylion, GlowUp, Spore, Fishborg);
            Prefer(SummonPicks, CallOfTheHaunted, Dandylion, Debris, GlowUp);
            Prefer(SummonPicks, TourGuide, Sangan);
            Prefer(Tributes, Lonefire, Fluffs.Concat(new[] { Dandylion, Lonefire, GlowUp, Spore }).ToArray());
            Prefer(cost, 0, Discards);
            Avoid(cost, 0, Debris, Foolish, OneForOne);
            Prefer(Materials, 0, TrishulaMaterials);
            Prefer(Banish, Spore, Dandylion, Lonefire, GlowUp, Spore);
        }
    }
}
