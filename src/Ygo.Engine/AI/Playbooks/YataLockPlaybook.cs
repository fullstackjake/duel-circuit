using System.Linq;

namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Chaos / Yata-Lock (2004, the 2004 texts), the deck of Dumpster Row's headliner "Yata Mags"
    /// (docs/dev/HEADLINERS-2026-11-01.md). Chaos Emperor Dragon, summoned by banishing a LIGHT and a DARK from the GY, pays
    /// 1000 to send every card in both hands and on the field to the GY. Sangan or Witch of the Black Forest, sent with
    /// them, finds Yata-Garasu, and Yata's attack on an opponent with no cards skips their draws for good (the Yata-lock
    /// house rule ends the duel there). The turn before, Sangan or Witch goes on the field; Graceful Charity, Painful
    /// Choice and Card Destruction put the LIGHT and the DARK in the GY.
    /// When the lock can't come together, the wipe on its own is the payoff (300 damage a card, and both boards gone).
    /// </summary>
    public sealed class YataLockPlaybook : Playbook
    {
        private const uint ChaosEmperor = 511000819, Yata = 3078576, Sangan = 511002631, Witch = 511003012, Envoy = 504700118,
            Airknight = 18036057, MagicianOfFaith = 31560081, Lily = 504700138, ShiningAngel = 95956346, Jinzo = 77585513,
            DarkMagicianOfChaos = 511001039, MaskedSorcerer = 10189126, CyberJar = 504700188, Scientist = 34206604,
            GracefulCharity = 79571449, PainfulChoice = 74191942, CardDestruction = 72892473;

        private static readonly uint[] Lights = { Airknight, MagicianOfFaith, Lily, ShiningAngel, Envoy };
        private static readonly uint[] Darks = { Jinzo, DarkMagicianOfChaos, MaskedSorcerer, CyberJar, Scientist, Sangan, Witch };
        /// <summary>What to discard or banish first: the LIGHTs and DARKs the lock doesn't need.</summary>
        private static readonly uint[] Fodder = { ShiningAngel, MaskedSorcerer, CyberJar, MagicianOfFaith, Scientist, Lily, Airknight, Jinzo, DarkMagicianOfChaos };
        private static readonly uint[] LockPieces = { ChaosEmperor, Yata, Sangan, Witch };

        public override string Deck => "chaos-yata";

        private bool LightAndDark => V.InGraveyardAny(Lights) > 0 && V.InGraveyardAny(Darks) > 0;
        private bool Searcher => V.OnFieldAny(Sangan, Witch) > 0;
        /// <summary>Yata can attack this turn: not the duel's first turn, and it's in the Deck for Sangan or Witch to find.</summary>
        private bool YataReady => !V.IsFirstTurn && V.InDeck(Yata) > 0 && !V.NormalSummonUsed;
        /// <summary>
        /// The lock can come together next turn: Sangan or Witch is in hand to go on the field, or is on the field but Yata
        /// can't attack this turn. Chaos Emperor Dragon waits a turn for it.
        /// </summary>
        private bool WaitForLock => V.InDeck(Yata) > 0 && (!Searcher && V.InHandAny(Sangan, Witch) || Searcher && !YataReady);
        /// <summary>The wipe is worth its 1000 LP: the opponent has at least three cards to lose (300 damage each).</summary>
        private bool WipeWorthIt => V.OpponentHandCount + V.OpponentCardCount >= 3;

        public YataLockPlaybook()
        {
            var cost = new[] { DuelContext.HintDiscard, DuelContext.HintToGrave };
            ComboRoute("Yata-lock", () => V.IsMyMainPhase && V.InHand(ChaosEmperor) > 0 && Searcher && YataReady)
                .AnyDecision()
                .Waiting()
                .Counts("damage", () => -V.OpponentLifePoints)
                .Activate("Graceful Charity puts a LIGHT and a DARK in the GY", GracefulCharity, () => !LightAndDark)
                    .Pick(cost, Fodder)
                    .Keep(cost, LockPieces)
                .Activate("Card Destruction refills and fills the GY", CardDestruction, () => !LightAndDark && V.HandCount >= 3)
                    .Keep(cost, LockPieces)
                .SpecialSummon("Chaos Emperor Dragon (a LIGHT and a DARK banished)", ChaosEmperor, () => LightAndDark)
                    .Pick(Banish, Fodder)
                .Activate("Chaos Emperor Dragon sends everything to the GY", ChaosEmperor, () => Searcher)
                    .Pick(Searches, Yata)
                    .Payoff()
                .NormalSummon("Yata-Garasu", Yata);
            // No lock in reach: the wipe on its own is the show (and Sangan or Witch, if they're on the field, still search).
            ComboRoute("Chaos Emperor wipe", () => V.IsMyMainPhase && V.InHand(ChaosEmperor) > 0 && !WaitForLock && WipeWorthIt)
                .AnyDecision()
                .Waiting()
                .Counts("damage", () => -V.OpponentLifePoints)
                .Activate("Graceful Charity puts a LIGHT and a DARK in the GY", GracefulCharity, () => !LightAndDark)
                    .Pick(cost, Fodder)
                    .Keep(cost, LockPieces)
                .Activate("Card Destruction refills and fills the GY", CardDestruction, () => !LightAndDark && V.HandCount >= 3)
                    .Keep(cost, LockPieces)
                .SpecialSummon("Chaos Emperor Dragon (a LIGHT and a DARK banished)", ChaosEmperor, () => LightAndDark)
                    .Pick(Banish, Fodder)
                .Activate("Chaos Emperor Dragon sends everything to the GY", ChaosEmperor, () => WipeWorthIt)
                    .Payoff();

            // The turn before: Sangan or Witch on the field, the LIGHT and DARK in the GY.
            NormalSummon("Witch of the Black Forest waits for Chaos Emperor Dragon", Witch, () => !Searcher && V.InDeck(Yata) > 0);
            NormalSummon("Sangan waits for Chaos Emperor Dragon", Sangan, () => !Searcher && V.InDeck(Yata) > 0);
            Activate("Graceful Charity", GracefulCharity);
            Activate("Painful Choice", PainfulChoice);
            Activate("Card Destruction digs for the lock", CardDestruction, () => !V.InHandAny(ChaosEmperor) && V.HandCount >= 3);

            // Hold the pieces for the lock while it can still come together.
            // (Not this turn, if Sangan or Witch took the Normal Summon: next turn, with the Summon free for Yata.)
            HoldSpecialSummon(ChaosEmperor, () => WaitForLock);
            HoldActivation(ChaosEmperor, () => WaitForLock);
            // The lock's turn: Yata-Garasu needs the Normal Summon.
            HoldNormalSummons(() => V.InHand(ChaosEmperor) > 0 && Searcher && YataReady);
            // Black Luster Soldier spends the LIGHT and DARK Chaos Emperor Dragon needs.
            HoldSpecialSummon(Envoy, () => V.InHand(ChaosEmperor) > 0);

            // Sangan's and Witch's searches (the bot can't always tell which card asks): Yata-Garasu first.
            Prefer(Searches, 0, Yata);
            Prefer(cost, 0, Fodder);
            Avoid(cost, 0, LockPieces);
            Prefer(Banish, ChaosEmperor, Fodder);
            Avoid(Banish, 0, Sangan, Witch, Yata);
        }
    }
}
