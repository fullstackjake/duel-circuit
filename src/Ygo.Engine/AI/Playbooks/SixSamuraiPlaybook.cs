namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Six Samurai (2011): Gateway of the Six and Six Samurai United go up first, so every summon after them pays out
    /// (Gateway's counters search a Six Samurai at 4, United draws). Shien's Smoke Signal and Reinforcement of the Army find
    /// Kageki, whose Normal Summon brings a Level 4 or lower Six Samurai from the hand (Kagemusha, the Tuner); Kizan and
    /// Grandmaster summon themselves next to another Six Samurai. Kagemusha + Kageki become Legendary Six Samurai - Shi En,
    /// which negates a Spell/Trap each turn: the turn-1 board, with Gateway's counters and Solemns behind it. Enishi comes
    /// from two Six Samurai in the GY; going second Double-Edged Sword Technique and the removal push damage.
    /// The Basement Game's reserve headliner "Gateway Gwen" (docs/dev/HEADLINERS-2026-11-01.md; "#recipe gateway-gwen", three
    /// Gateway of the Six under the March 2010 list) adds a route: the Gateways and United up, then each summon pays two
    /// counters a Gateway, Gateway's searches find the rest, and Shi En is the payoff. The six-samurai deck plays without it.
    /// </summary>
    public sealed class SixSamuraiPlaybook : Playbook
    {
        private const uint Kagemusha = 1498130, Kizan = 49721904, Kageki = 2511717, Grandmaster = 83039729, Enishi = 38280762,
            HandOfSix = 78792195, Upstart = 70368879, SmokeSignal = 54031490, United = 72345736, Rota = 32807846,
            Gateway = 27970830, DoubleEdged = 21007444, DarkHole = 53129443, Trunade = 42703248,
            ShiEn = 29981921, Brionac = 50321796, Barkion = 2956282, Colossal = 23693634, GaiaKnight = 97204936,
            ScrapDragon = 76774528, ScrapArchfiend = 45815891, Stardust = 44508094, Chimeratech = 79229522, Catastor = 26593852,
            MagicalAndroid = 43385557, BlackRose = 73580471, MistWurm = 27315304, AncientFairy = 25862681;

        public override string Deck => "six-samurai";

        public SixSamuraiPlaybook() : this(false) { }

        /// <summary>With <paramref name="gateway"/>, the headliner's route (Gateway Gwen); without, the playbook as it always was.</summary>
        public SixSamuraiPlaybook(bool gateway)
        {
            _gateway = gateway;
            if (gateway) GatewayRoute();
            Activate("Upstart Goblin", Upstart);
            if (gateway) Activate("Pot of Duality digs for Kageki and Kagemusha", 98645731);
            // The counters' cards before any summon.
            Activate("Gateway of the Six", Gateway, () => !V.InSpellTrapZone(Gateway));
            Activate("Six Samurai United", United, () => !V.InSpellTrapZone(United));
            ActivateEffect("Gateway of the Six searches (4 counters)", Gateway, 1, () => V.InSpellTrapZone(Gateway), perTurn: 2);
            NormalSummon("Legendary Six Samurai - Kageki brings a Six Samurai", Kageki, () => V.InHandAny(Kagemusha, Kizan, HandOfSix) || V.InHand(Kageki) > 1);
            NormalSummon("Kagemusha of the Six Samurai, then Kizan or Grandmaster", Kagemusha, () => V.InHandAny(Kizan, Grandmaster) && !KagekiLine);
            SpecialSummon("Legendary Six Samurai - Kizan joins", Kizan, perTurn: 3);
            SpecialSummon("Grandmaster of the Six Samurai joins", Grandmaster);
            SpecialSummon("Enishi, Shien's Chancellor", Enishi);
            // Synchros: Shi En first (Kagemusha + Kageki), then a second Synchro if a Tuner is left.
            SpecialSummon("Kagemusha + Kageki into Legendary Six Samurai - Shi En", ShiEn, perTurn: 2);
            SpecialSummon("Synchro into Brionac", Brionac, () => (V.OnField(ShiEn) > 0 || !V.IsFirstTurn) && !ShiEnFirst);
            SpecialSummon("Synchro into Naturia Barkion", Barkion, () => !ShiEnFirst);
            SpecialSummon("Synchro into Gaia Knight", GaiaKnight, () => !ShiEnFirst);
            SpecialSummon("Synchro into Ancient Fairy Dragon", AncientFairy, () => !ShiEnFirst);
            SpecialSummon("Synchro into Stardust Dragon", Stardust, () => !ShiEnFirst);
            SpecialSummon("Synchro into Scrap Dragon", ScrapDragon, () => V.OpponentCardCount > 0 && !ShiEnFirst);

            // Gateway's +500 ATK eats the counters the search needs: only for an attack.
            HoldEffect(Gateway, 0, () => V.IsFirstTurn || V.OpponentMonsterCount == 0);
            // United's draw waits for its second counter.
            HoldActivation(United, () => V.InSpellTrapZone(United) && V.HandCount >= 2 && V.MyMonsterCount < 2);
            HoldSpecialSummon(BlackRose, () => V.OpponentMonsterCount < 2);
            HoldSpecialSummon(MistWurm, () => V.OpponentCardCount < 2);
            HoldSpecialSummon(Catastor, () => V.IsFirstTurn);
            HoldSpecialSummon(MagicalAndroid, () => V.IsFirstTurn);
            HoldSpecialSummon(Colossal, () => V.IsFirstTurn);
            HoldSpecialSummon(ScrapArchfiend, () => V.IsFirstTurn);
            HoldSpecialSummon(Chimeratech);
            HoldNormalSummon(HandOfSix, () => V.InHandAny(Kageki, Kagemusha));
            // Double-Edged Sword Technique burns us for both monsters at the End Phase: only our own turn, for a Synchro or an attack.
            HoldActivation(DoubleEdged, () => V.IsFirstTurn || !V.IsMyTurn);
            HoldActivation(DarkHole, () => V.OpponentMonsterCount == 0);
            HoldActivation(Trunade, () => V.OpponentCardCount - V.OpponentMonsterCount < 2);
            Boss(ShiEn);

            // (A rank in these lists is worth less than the gap between two cards' values: the must-have gets its own line.)
            PreferIf(() => V.InHand(Kageki) == 0, Searches, SmokeSignal, Kageki);
            Prefer(Searches, SmokeSignal, Kageki, Kagemusha, HandOfSix);
            PreferIf(() => V.InHand(Kageki) > 0 && V.InHand(Kagemusha) == 0, Searches, SmokeSignal, Kagemusha);
            PreferIf(() => V.InHand(Kageki) == 0, Searches, Rota, Kageki);
            if (gateway) Prefer(Searches, 98645731, Kageki, Kagemusha, SmokeSignal, Gateway, Rota, Kizan);
            Prefer(Searches, Rota, Kageki, Kizan, Kagemusha, HandOfSix);
            PreferIf(() => V.InHand(Kageki) > 0 && V.InHand(Kagemusha) == 0, Searches, Rota, Kagemusha);
            Prefer(Searches, Gateway, Kizan, Grandmaster, Kagemusha, Kageki);
            PreferIf(() => V.OnField(ShiEn) == 0 && V.InHand(Kagemusha) == 0 && V.OnField(Kagemusha) == 0 && V.OnField(Kageki) > 0,
                Searches, Gateway, Kagemusha);
            // Kageki brings the Tuner (Kizan can summon itself anyway).
            Prefer(SummonPicks, Kageki, Kagemusha);
            Prefer(SummonPicks, Kageki, Kagemusha, Kizan, HandOfSix);
            Prefer(Materials, ShiEn, Kagemusha, Kageki, HandOfSix, Kizan);
            Prefer(Materials, 0, Kagemusha, Kageki, HandOfSix, Kizan, Grandmaster);
            Avoid(Materials, 0, ShiEn);
            Prefer(Banish, Enishi, HandOfSix, Kagemusha, Kageki, Kizan);
            Prefer(Tributes, HandOfSix, Kagemusha, Kageki, Kizan);
        }

        private readonly bool _gateway;

        private static readonly uint[] Samurai = { Kagemusha, Kizan, Kageki, Grandmaster, HandOfSix };

        /// <summary>Gwen's line: Kageki's Normal Summon brings Kagemusha (Kageki in hand or a search away, Kagemusha in hand or Gateway's).</summary>
        private bool KagekiLine => _gateway && !V.NormalSummonUsed && V.InExtraDeck(ShiEn) > 0 && V.OnField(ShiEn) == 0
                                   && (V.InHand(Kageki) > 0 || V.InHandAny(SmokeSignal, Rota))
                                   && (V.InHand(Kagemusha) > 0 || V.InHandAny(SmokeSignal, Rota) && V.InHand(Kageki) > 0 || GatewayCanSearch);

        /// <summary>A Gateway is up (or in hand) and Kizan or Grandmaster can summon itself next to a Six Samurai already there: 4 counters.</summary>
        private bool GatewayCanSearch => (V.InSpellTrapZone(Gateway) || V.InHand(Gateway) > 0) && V.OnFieldAny(Samurai) > 0 && V.InHandAny(Kizan, Grandmaster);

        /// <summary>Gwen holds the other Synchros while Shi En can still come this turn.</summary>
        private bool ShiEnFirst => _gateway && V.InExtraDeck(ShiEn) > 0 && V.OnField(ShiEn) == 0 && (KagekiLine || V.OnField(Kagemusha) > 0 && V.OnFieldAny(Kageki, HandOfSix) > 0);

        private void GatewayRoute()
        {
            ComboRoute("Gateway of the Six", () => V.IsMyMainPhase && KagekiLine)
                .AnyDecision()
                .Waiting()
                .Counts("summons", () => V.SpecialSummonsThisDuel + (V.NormalSummonUsed ? 1 : 0))
                .Activate("Gateway of the Six", Gateway, () => V.SpellTrapCount < 5)
                .Activate("another Gateway of the Six", Gateway, () => V.SpellTrapCount < 5)
                .Activate("a third Gateway of the Six", Gateway, () => V.SpellTrapCount < 5)
                .Activate("Six Samurai United", United, () => !V.InSpellTrapZone(United))
                .Activate("Shien's Smoke Signal finds Kageki", SmokeSignal, () => V.InHand(Kageki) == 0)
                    .Pick(Searches, Kageki)
                .Activate("Reinforcement of the Army finds the other", Rota, () => V.InHand(Kageki) == 0 || V.InHand(Kagemusha) == 0)
                    .Pick(Searches, Kageki, Kagemusha)
                .SpecialSummon("Kizan joins (counters)", Kizan, () => V.InHand(Kagemusha) == 0)
                .SpecialSummon("Grandmaster joins (counters)", Grandmaster, () => V.InHand(Kagemusha) == 0)
                .ActivateEffect("Gateway's counters find Kagemusha", Gateway, 1, () => V.InHand(Kagemusha) == 0)
                    .Pick(Searches, Kagemusha)
                .Activate("Shien's Smoke Signal finds Kagemusha", SmokeSignal, () => V.InHand(Kagemusha) == 0)
                    .Pick(Searches, Kagemusha)
                .NormalSummon("Kageki brings Kagemusha", Kageki, () => V.InHand(Kagemusha) > 0)
                    .Pick(SummonPicks, Kagemusha)
                .SpecialSummon("Legendary Six Samurai - Shi En", ShiEn)
                    .Pick(Materials, Kagemusha, Kageki, HandOfSix, Kizan)
                    .Payoff();
        }
    }
}
