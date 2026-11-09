using System.Linq;

namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Chaos Return (2005), Dumpster Row's headliner "Return Rook" (docs/dev/HEADLINERS-2026-11-01.md). Dimensional Fissure
    /// banishes every monster sent to the GY, so Foolish Burial, Graceful Charity, Card Destruction and Thunder Dragon's
    /// discard bank the deck's best monsters in banishment; without it, Chaos Sorcerer and Black Luster Soldier banish a
    /// LIGHT and a DARK from the GY, Dark Necrofear three DARKs, Bazoo three of anything, Soul Release five. Then Return
    /// from the Different Dimension (half your LP; Set a turn ahead) or Dimension Fusion (2000) brings every banished
    /// monster back at once, in Main Phase 1: the payoff, once two or more are banked (the smaller show, the owner,
    /// 2026-11-05; the spec's "field's worth", three, finished 34-36% of goldfish games, under the 40% go line). The meter
    /// counts the monsters that come home.
    /// </summary>
    public sealed class ReturnRookPlaybook : Playbook
    {
        private const uint Return = 27174286, DimensionFusion = 23557835, ChaosSorcerer = 9596126, Bls = 72989439, Survivor = 48092532,
            ScoutPlane = 3773196, Sangan = 26202165, Breaker = 71413901, Jinzo = 77585513, MagicianOfFaith = 31560081, Zaborg = 51945556,
            ThunderDragon = 31786629, Bazoo = 40133511, Necrofear = 31829185, SoulRelease = 5758500, Fissure = 81674782,
            PotOfGreed = 55144522, GracefulCharity = 79571449, CardDestruction = 72892473, Upstart = 70368879, Foolish = 81439173,
            Premature = 70828912, Banisher = 61528025, MorphingJar = 33508719,
            DdWarrior = 37043180, Ddwl = 7572887, Tiv = 33184167, Vortex = 69162969;

        private static readonly uint[] Lights = { Bls, MagicianOfFaith, Zaborg, ThunderDragon, Ddwl, Banisher };
        private static readonly uint[] Darks = { ChaosSorcerer, ScoutPlane, Survivor, Sangan, Breaker, Jinzo, Necrofear };
        /// <summary>
        /// What Return wants back most (banished first by Foolish Burial and the discards under Fissure): the big bodies, which
        /// were never Special Summoned and so can come back. Chaos Sorcerer, BLS and Dark Necrofear can't (not properly summoned).
        /// </summary>
        private static readonly uint[] BringBack = { Zaborg, Jinzo, Survivor, ThunderDragon, Breaker, Tiv, DdWarrior, Ddwl, ScoutPlane, Sangan, MagicianOfFaith, Bazoo };
        /// <summary>What a cost banishes from the GY first: Scout Plane comes back by itself, then the bodies Return wants.</summary>
        private static readonly uint[] BanishFirst = { ScoutPlane, Zaborg, Jinzo, Survivor, ThunderDragon, Breaker, Sangan, MagicianOfFaith, Bazoo, MorphingJar, Banisher };
        private static readonly uint[] Discards = { ThunderDragon, Zaborg, Jinzo, Survivor, Tiv, ScoutPlane, Breaker, DdWarrior, Bazoo, Sangan, MagicianOfFaith, Necrofear };
        private static readonly uint[] Payoffs = { Return, DimensionFusion };

        public override string Deck => "return-rook";

        /// <summary>Dimensional Fissure is up, or Banisher of the Light is face-up: every monster sent to the GY is banished instead.</summary>
        private bool FissureUp => V.InSpellTrapZone(Fissure) || V.OnField(Banisher) > 0;
        private bool LightAndDark => V.InGraveyardAny(Lights) > 0 && V.InGraveyardAny(Darks) > 0;
        private int GyMonsters => V.GraveyardMonstersAtOrBelowLevel(12);
        /// <summary>Return from the Different Dimension is Set, or Dimension Fusion is in hand with the LP for it (not on the duel's first turn: no attacks).</summary>
        private bool PayoffInReach => !V.IsFirstTurn && (V.InSpellTrapZone(Return) || V.InHand(DimensionFusion) > 0 && V.MyLifePoints > 2000 && V.OpponentBanishedMonsters <= 1);

        /// <summary>
        /// About how many more monsters this turn can bank: under Fissure (or with it in hand) each send to the GY is a
        /// banish (Foolish Burial one, Graceful Charity two, Card Destruction the hand, Thunder Dragon itself); without it,
        /// the summons that banish from the GY and Soul Release.
        /// </summary>
        private int BanishableNow
        {
            get
            {
                int fissure = FissureUp || V.InHand(Fissure) > 0 || V.InHand(Banisher) > 0 && !V.NormalSummonUsed
                    ? V.InHand(Foolish) + (V.InHand(GracefulCharity) > 0 ? 2 : 0) + (V.InHand(CardDestruction) > 0 ? 2 : 0) + System.Math.Min(1, V.InHand(ThunderDragon))
                    : 0;
                int costs = (V.InHandAny(ChaosSorcerer, Bls) && LightAndDark ? 2 : 0) + (V.InHand(Necrofear) > 0 && V.InGraveyardAny(Darks) >= 3 ? 3 : 0);
                int gy = System.Math.Max(V.InHand(SoulRelease) > 0 ? System.Math.Min(5, GyMonsters) : 0,
                    V.OnField(Bazoo) > 0 || V.InHand(Bazoo) > 0 && !V.NormalSummonUsed ? System.Math.Min(3, GyMonsters) : 0);
                return System.Math.Max(fissure, costs + gy);
            }
        }

        /// <summary>Dimension Fusion brings back theirs too: only when ours outnumber theirs by two or more.</summary>
        private bool FusionFavorsUs => Returnable >= V.OpponentBanishedMonsters + 2;

        /// <summary>The monsters that must first be Special Summoned their own way (EnableReviveLimit): from banishment only once they were.</summary>
        private static readonly uint[] ReviveLimited = { ChaosSorcerer, Bls, Necrofear };
        /// <summary>The core's STATUS_PROC_COMPLETE: the card was properly summoned (its revive limit no longer applies).</summary>
        private const uint ProcComplete = 0x8;

        /// <summary>
        /// Your banished monsters Return and Dimension Fusion can bring back (their filter: face-up, and can be Special
        /// Summoned): not Chaos Sorcerer, Black Luster Soldier or Dark Necrofear banished before they were properly summoned
        /// (Fissure banishing one discarded or milled), which the core's revive limit keeps out.
        /// </summary>
        private int Returnable => V.Context.Board.Players[V.Me].Banished.Count(c => c != null && (c.Type & CardType.Monster) != 0 && c.IsFaceUp
            && !(IndexOf(ReviveLimited, c.Code) >= 0 && (c.Status & ProcComplete) == 0));

        /// <summary>Enough to bring home for the payoff: two or more that can come back.</summary>
        private bool Banked => Returnable >= Need;
        /// <summary>
        /// The smaller show (the owner, 2026-11-05): two monsters home is the payoff, and the bank's steps play while they get
        /// it there (HEADLINERS: 51-55% of goldfish games finish, counting only payoffs with two or more home; a field's worth, three, was 34-36%, a no-go).
        /// </summary>
        public const int Need = 2;

        /// <summary>
        /// The meter: monsters that have left your banishment (Return's and Dimension Fusion's homecoming), counted from the
        /// bank's drops between readings (the route reads it at each signal; it moves from the route's start, so a drop
        /// before then, D.D. Scout Plane's own return, is in its base). Banking never lowers it.
        /// </summary>
        private int MonstersHome()
        {
            int bank = V.BanishedMonsters;
            if (bank < _lastBank) _home += _lastBank - bank;
            _lastBank = bank;
            return _home;
        }
        private int _home, _lastBank;

        public ReturnRookPlaybook()
        {
            var cost = new[] { DuelContext.HintDiscard, DuelContext.HintToGrave };
            // The comeback: bank what's left to bank (two or more), then bring them all back in Main Phase 1 and attack.
            ComboRoute("Chaos Return", () => V.IsMyMainPhase1 && PayoffInReach && Returnable + BanishableNow >= Need)
                .AnyDecision()
                .Counts("monsters home", MonstersHome)
                .Activate("Dimensional Fissure", Fissure, () => !Banked && !FissureUp)
                .NormalSummon("Banisher of the Light", Banisher, () => !Banked && !FissureUp)
                .Activate("Thunder Dragon discards itself", ThunderDragon, () => !Banked && FissureUp)
                .Activate("Foolish Burial", Foolish, () => !Banked)
                    .Pick(ToGrave, BringBack)
                .Activate("Graceful Charity", GracefulCharity, () => !Banked)
                    .Pick(cost, Discards)
                    .Keep(cost, Payoffs)
                .Activate("Foolish Burial", Foolish, () => !Banked)
                    .Pick(ToGrave, BringBack)
                .Activate("Card Destruction", CardDestruction, () => !Banked)
                .Activate("Soul Release banishes the GY", SoulRelease, () => !Banked && Returnable + System.Math.Min(5, GyMonsters) >= Need)
                    .Pick(Banish, BanishFirst)
                .SpecialSummon("Chaos Sorcerer (a LIGHT and a DARK banished)", ChaosSorcerer, () => !Banked)
                    .Pick(Banish, BanishFirst)
                .SpecialSummon("Black Luster Soldier (a LIGHT and a DARK banished)", Bls, () => !Banked)
                    .Pick(Banish, BanishFirst)
                .SpecialSummon("Dark Necrofear (three DARKs banished)", Necrofear, () => !Banked)
                    .Pick(Banish, BanishFirst)
                .NormalSummon("Bazoo the Soul-Eater", Bazoo, () => !Banked && GyMonsters > 0)
                .Activate("Bazoo eats the GY", Bazoo, () => !Banked && Returnable + System.Math.Min(3, GyMonsters) >= Need)
                    .Pick(Banish, BanishFirst)
                .Activate("Return from the Different Dimension brings them home", Return, () => Banked && V.FreeMonsterZones >= 2)
                    .Payoff()
                .Activate("Dimension Fusion brings them home", DimensionFusion, () => Banked && V.FreeMonsterZones >= 2 && V.MyLifePoints > 2000 && FusionFavorsUs)
                    .Payoff();

            // ---- Building to it: Return Set a turn ahead, Fissure up, the banished bank and the GY's LIGHTs and DARKs.
            SetSpellTrap("Return from the Different Dimension waits a turn", Return, () => !V.InSpellTrapZone(Return));
            Activate("Pot of Greed", PotOfGreed);
            Activate("Upstart Goblin", Upstart);
            // The bank: Fissure (or Banisher) up early, so every Foolish Burial and discard from here on is a monster banked.
            Activate("Dimensional Fissure", Fissure, () => !FissureUp);
            NormalSummon("Banisher of the Light", Banisher, () => !FissureUp);
            Activate("Thunder Dragon finds two more", ThunderDragon, () => V.InDeck(ThunderDragon) > 0 || FissureUp);
            Activate("Foolish Burial", Foolish);
            Activate("Graceful Charity", GracefulCharity);
            Activate("Card Destruction", CardDestruction, () => V.HandCount >= 3);
            SpecialSummon("Chaos Sorcerer", ChaosSorcerer, () => LightAndDark);
            SpecialSummon("Black Luster Soldier", Bls, () => LightAndDark);
            SpecialSummon("Dark Necrofear", Necrofear, () => V.InGraveyardAny(Darks) >= 3);
            Activate("Bazoo eats the GY", Bazoo, () => V.InSpellTrapZone(Return) || V.InHandAny(Payoffs));
            Activate("Premature Burial", Premature);

            // The payoff only in the route: never with fewer than two to bring back, never after Main Phase 1.
            HoldActivation(Return, () => !V.IsMyMainPhase1 || !Banked);
            HoldActivation(DimensionFusion, () => !V.IsMyMainPhase1 || !Banked || V.IsFirstTurn || !FusionFavorsUs);
            // Soul Release only for the comeback (the opponent's banished come back with Dimension Fusion too).
            HoldActivation(SoulRelease, () => !V.InSpellTrapZone(Return) && !V.InHandAny(Payoffs));
            // Fissure leaves Chaos Sorcerer nothing to banish: not while it's the only way to bank this turn.
            HoldActivation(Fissure, () => V.InHandAny(ChaosSorcerer, Bls) && LightAndDark && V.InHand(Foolish) == 0 && V.InHand(GracefulCharity) == 0);

            Prefer(cost, 0, Discards);
            Avoid(cost, 0, Return, DimensionFusion, Fissure, SoulRelease, Foolish);
            Prefer(Banish, 0, BanishFirst);
            // Soul Release and Bazoo bank as many of our monsters as they can.
            TakeAll(Banish, SoulRelease, BanishFirst);
            TakeAll(Banish, Bazoo, BanishFirst);
            Avoid(Banish, 0, ChaosSorcerer, Bls);
            Prefer(Searches, 0, ThunderDragon, MagicianOfFaith, Zaborg);
            // Foolish Burial without Fissure: the missing half of Chaos Sorcerer's cost; with it, what Return wants back.
            PreferIf(() => !FissureUp && V.InGraveyardAny(Lights) == 0, ToGrave, Foolish, ThunderDragon, Zaborg, MagicianOfFaith);
            PreferIf(() => !FissureUp && V.InGraveyardAny(Darks) == 0, ToGrave, Foolish, ScoutPlane, Survivor, Jinzo);
            Prefer(ToGrave, Foolish, BringBack);
            Prefer(SummonPicks, Premature, Jinzo, Zaborg, Survivor);
            Prefer(new[] { DuelContext.HintAddToHand }, MagicianOfFaith, GracefulCharity, PotOfGreed, Foolish);
        }
    }
}
