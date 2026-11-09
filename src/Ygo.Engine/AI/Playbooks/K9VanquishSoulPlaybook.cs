namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// K9 Vanquish Soul (2025): Razen, Normal Summoned, adds a non-Warrior Vanquish Soul; Dr. Mad Love adds a Vanquish Soul
    /// Spell/Trap; Vanquish Soul, Start! and Stake your Soul! fetch or summon more by Attribute. Heavy Borger and Caesar
    /// Valius summon themselves by returning a Vanquish Soul to the hand (Razen or Mad Love, to search again later); the
    /// revealed Attributes power their quick effects. The K9 side: Jokul brings two Level 5s, which become K9-17 "Ripper".
    /// </summary>
    public sealed class K9VanquishSoulPlaybook : Playbook
    {
        private const uint Razen = 29302858, HollieSue = 93156774, MadLove = 29280200, CaesarValius = 91073013, HeavyBorger = 92895501,
            Jiaolong = 9091064, Izuna = 92248362, Jokul = 28642461, Lupis = 91025875, Phantazmay = 78661338, Stake = 54562327,
            Start = 35550352, Extravagance = 49238328, Reinforcement = 32807846, SnowDevil = 60883493,
            Rock = 28168628, LittleKnight = 29301450, Vallon = 40673853, Ripper = 27420823, Rocks = 77894049, PairADice = 35772782,
            // No Limits (Augie's nl-vanquish-soul, docs/dev/NO-LIMITS-SPEC-2026-11-02.md 7.2): cards the as-built list doesn't run.
            Noroi = 47960073, Confiscation = 17375316, Dustshoot = 64697231, Azathot = 34945480;

        /// <summary>The hand traps and quick answers a hand-strip takes first (the known-hand view of 7.3 is NL-2; the selection shows the hand today).</summary>
        private static readonly uint[] HandTraps =
        {
            14558127, 10045474, 27204311, 97268402, 42141493, 84192580, 94145021, 59438930, 52038441, 73642296, 23434538, 24508238, 91800273,
            24224830, 65681983, 24299458, 54693926, 14532163, 15693423, 25311006, 40366667, 46502744, 40975574,
        };

        private static readonly uint[] Level5s = { Izuna, Lupis, HollieSue, Jiaolong };

        public override string Deck => "k9-vanquish-soul";

        public K9VanquishSoulPlaybook()
        {
            // ---- Combo routes. Jokul with another Level 5: both come out, Jokul searches a K9, and the two make K9-17
            // "Ripper", which negates a hand trap or GY effect on the opponent's turn and searches again.
            ComboRoute("Jokul into Ripper", () => V.IsMyTurn && V.IsFirstTurn && V.InHand(Jokul) > 0 && V.InHandAny(Level5s))
                .Activate("K9-66a Jokul brings another Level 5", Jokul, () => V.InHand(Jokul) > 0)
                    .Pick(SummonPicks, Izuna, Lupis, Jiaolong, HollieSue)
                .Activate("K9-66a Jokul searches a K9", Jokul, () => V.OnField(Jokul) > 0)
                    .Pick(Searches, Izuna, Lupis)
                .SpecialSummon("Jokul and a Level 5 make K9-17 \"Ripper\"", Ripper)
                    .Pick(new[] { DuelContext.HintXyzMaterial }, Jokul, Izuna, Lupis, Jiaolong, HollieSue)
                .Activate("K9-17 \"Ripper\" searches a K9", Ripper, () => V.IsMyTurn)
                    .Pick(Searches, Lupis, Izuna, Jokul)
                .NormalSummon("Vanquish Soul Razen searches", Razen)
                    .Pick(Searches, HollieSue, Jiaolong, CaesarValius)
                .Activate("Vanquish Soul Hollie Sue joins", HollieSue, () => V.InHand(HollieSue) > 0);
            // Razen: the Vanquish Soul engine, with Ripper once two Level 5s are out.
            ComboRoute("Razen and Hollie Sue", () => V.IsMyTurn && V.IsFirstTurn && V.InHandAny(Razen, Reinforcement))
                .Activate("Reinforcement of the Army finds Razen", Reinforcement, () => V.InHand(Razen) == 0)
                    .Pick(Searches, Razen)
                .NormalSummon("Vanquish Soul Razen searches Hollie Sue", Razen)
                    .Pick(Searches, HollieSue, Jiaolong, CaesarValius)
                .Activate("Vanquish Soul Hollie Sue joins", HollieSue, () => V.InHand(HollieSue) > 0)
                .Activate("Vanquish Soul, Start! adds another Attribute", Start)
                    .Pick(Searches, CaesarValius, HeavyBorger, MadLove, Jiaolong)
                .Activate("K9-66a Jokul brings another Level 5", Jokul, () => V.InHand(Jokul) > 0 && V.InHandAny(Level5s))
                    .Pick(SummonPicks, Izuna, Lupis, Jiaolong, HollieSue)
                .SpecialSummon("two Level 5s make K9-17 \"Ripper\"", Ripper, () => V.MyMonsterCount >= 3)
                    .Pick(new[] { DuelContext.HintXyzMaterial }, Jokul, Izuna, Lupis, Jiaolong, HollieSue);
            // Any other hand: whenever two Level 5s are out (Izuna and Jiaolong), Ripper: the board scores it as one
            // body for two, but its negate on their turn is the point.
            ComboRoute("two Level 5s into Ripper", () => V.IsMyTurn && V.IsFirstTurn)
                .SpecialSummon("two Level 5s make K9-17 \"Ripper\"", Ripper)
                    .Pick(new[] { DuelContext.HintXyzMaterial }, Jokul, Izuna, Lupis, Jiaolong, HollieSue)
                .Activate("K9-17 \"Ripper\" searches a K9", Ripper, () => V.IsMyTurn)
                    .Pick(Searches, Lupis, Izuna, Jokul);

            Activate("Pot of Extravagance", Extravagance);
            Activate("Reinforcement of the Army finds Razen", Reinforcement, () => V.InHand(Razen) == 0);
            Activate("Vanquish Soul, Start!", Start, () => !V.InSpellTrapZone(Start));
            NormalSummon("Vanquish Soul Razen searches", Razen);
            NormalSummon("Vanquish Soul Dr. Mad Love searches", MadLove);
            Activate("K9-66a Jokul brings two Level 5s", Jokul, () => V.InHandAny(Level5s));
            Activate("Stake your Soul! calls a Vanquish Soul", Stake);
            Activate("Vanquish Soul, Start! adds another Attribute", Start, perTurn: 2);
            Activate("Vanquish Soul Hollie Sue joins", HollieSue, () => V.InHand(HollieSue) > 0);
            Activate("Vanquish Soul Heavy Borger swaps in", HeavyBorger, () => V.InHand(HeavyBorger) > 0 && V.OnFieldAny(Razen, MadLove) > 0);
            Activate("Vanquish Soul Caesar Valius swaps in", CaesarValius, () => V.InHand(CaesarValius) > 0 && V.OnFieldAny(Razen, MadLove, HollieSue) > 0);
            Activate("K9-66a Jokul searches a K9", Jokul, perTurn: 2);
            NormalSummon("K9-17 Izuna without a Tribute", Izuna);
            SpecialSummon("two Level 5s into K9-17 \"Ripper\"", Ripper);
            Activate("K9-17 \"Ripper\" searches a K9", Ripper, () => V.IsMyTurn);
            SpecialSummon("two Level 5s into Vallon", Vallon, () => V.OpponentMonsterCount > 0);

            HoldSpecialSummon(PairADice);
            HoldSpecialSummon(Rock, () => V.HandCount < 2);
            HoldNormalSummon(Jokul);

            Prefer(Searches, Razen, MadLove, HollieSue, Jiaolong, CaesarValius, HeavyBorger);
            Prefer(Searches, MadLove, Start, Stake, SnowDevil);
            Prefer(Searches, Start, HollieSue, MadLove, Razen, Jiaolong, CaesarValius);
            Prefer(Searches, Reinforcement, Razen);
            Prefer(Searches, Jokul, Izuna, Lupis);
            Prefer(Searches, Ripper, Lupis, Izuna, Jokul);
            Prefer(Searches, Jiaolong, Start, Stake, Razen);
            Prefer(Searches, 0, Razen, MadLove, Start, Izuna);
            Prefer(SummonPicks, Stake, Razen, MadLove, HollieSue, Jiaolong, HeavyBorger);
            Prefer(SummonPicks, HollieSue, Razen, MadLove, Jiaolong);
            // Borger and Caesar return a Vanquish Soul to the hand: the ones that search when summoned again.
            Prefer(SummonPicks, HeavyBorger, Razen, MadLove);
            Prefer(SummonPicks, CaesarValius, Razen, MadLove, HollieSue);
            Prefer(SummonPicks, Jokul, Izuna, Lupis, HollieSue, Jiaolong);
            Prefer(ToGrave, Izuna, Lupis);
            Prefer(Materials, 0, Izuna, Jokul, Lupis, HollieSue, Razen, MadLove);
            Avoid(Materials, 0, CaesarValius, HeavyBorger);

            // ---- No Limits (Augie's nl-vanquish-soul, 7.2): Noroi and the picks. Additive only: every card below is one the
            // as-built k9-vanquish-soul list doesn't run, so the base plays as before (7.1).
            // Noroi: a free Normal Summon while they hold 2+ cards; it calls a non-Machine K9 from the Deck. Its hand peek
            // costs a face-up K9 card, so it's held.
            NormalSummon("K9-04 Noroi calls a K9 from the Deck", Noroi, () => V.OpponentHandCount >= 2 && V.InDeckAny(Izuna, Lupis, Jokul) > 0);
            Prefer(SummonPicks, Noroi, Izuna, Lupis, Jokul);
            HoldEffect(Noroi, 2);
            Prefer(Materials, 0, Noroi);
            // Confiscation and Trap Dustshoot: hand traps first, from what the selection shows (the known-hand view is NL-2).
            Activate("Confiscation strips a hand trap", Confiscation, () => V.IsMyMainPhase && V.OpponentHandCount >= 1);
            Prefer(new[] { DuelContext.HintDiscard }, Confiscation, HandTraps);
            SetSpellTrap("Trap Dustshoot waits for their hand", Dustshoot);
            ChainWhen("Trap Dustshoot sends a monster back", Dustshoot, () => V.OpponentHandCount >= 4);
            Prefer(new[] { DuelContext.HintToDeck }, Dustshoot, HandTraps);
            // Azathot: three Level 5s, summoned on our turn so their monster effects stay off for the rest of it.
            SpecialSummon("three Level 5s into Outer Entity Azathot", Azathot, () => V.IsMyTurn && V.NonTunersOfLevel(5) >= 3 && V.OpponentHandCount >= 2);
            Prefer(Materials, Azathot, Noroi, Jokul, Izuna, Lupis, Jiaolong, HollieSue);
            Avoid(Materials, 0, Azathot);
        }
    }
}
