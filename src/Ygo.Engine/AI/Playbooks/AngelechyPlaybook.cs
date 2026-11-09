namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Angelechy (2026): a Main Deck of Spells and Traps; every Angelechy monster waits in the Extra Deck. Angelechy Problem
    /// (via Terraforming or Rainbow Bridge Bifrost) discards a Spell/Trap to Special Summon Enlisted and place another
    /// Angelechy as a Continuous Spell; Strategy Score searches the next piece, Brilliant Move and Opposition bring more
    /// out, and Foolish Burial Goods sends the pieces with GY effects. The Synchros banish what they can reach.
    /// </summary>
    public sealed class AngelechyPlaybook : Playbook
    {
        private const uint Problem = 17782288, StrategyScore = 101402092, Opposition = 101402094, EndgameProblem = 101402095,
            BrilliantMove = 101402093, Verdict = 101402096, Bifrost = 32236916, PotOfDesires = 35261759, BurialGoods = 35726888,
            Terraforming = 73628505, OpeningE4 = 80565021, Disturbance = 54171327, DominusPurge = 97045737, DominusImpulse = 40366667,
            Shatranga = 42410161, Enlisted = 81797573, Castellan = 101402090, Seneschal = 101402091, Bastion = 28904860,
            Destrier = 55393975, ChaosAngel = 22850702, Legatia = 15982593, Hyperion = 63101468;

        public override string Deck => "angelechy";

        public AngelechyPlaybook()
        {
            Activate("Pot of Desires", PotOfDesires);
            Activate("Terraforming finds Angelechy Problem", Terraforming, () => !V.InSpellTrapZone(Problem) && !V.InHandAny(Problem));
            Activate("Rainbow Bridge Bifrost finds Angelechy Problem", Bifrost, () => !V.InSpellTrapZone(Problem) && !V.InHandAny(Problem));
            Activate("Angelechy Strategy Score searches", StrategyScore);
            Activate("Angelechy Problem", Problem, () => !V.InSpellTrapZone(Problem));
            Activate("Angelechy Problem summons Enlisted", Problem, perTurn: 2);
            Activate("Foolish Burial Goods sends a GY piece", BurialGoods);
            Activate("Angelechy Brilliant Move", BrilliantMove);
            Activate("Angelechy Endgame Problem", EndgameProblem);
            Activate("Angelechy Enlisted banishes", Enlisted, () => V.OpponentMonsterCount > 0);
            Activate("Angelechy Shatranga banishes", Shatranga, () => V.OpponentMonsterCount > 0);
            Activate("Angelechy Destrier banishes", Destrier, () => V.OpponentMonsterCount > 0);
            Activate("Angelechy Castellan banishes", Castellan, () => V.OpponentCardCount > 0);
            Activate("Angelechy Bastion banishes", Bastion, () => V.OpponentCardCount > 0);
            SpecialSummon("Synchro into Angelechy Shatranga", Shatranga, () => V.OpponentMonsterCount > 0);
            SpecialSummon("Synchro into Angelechy Bastion", Bastion);
            SpecialSummon("Synchro into Angelechy Destrier", Destrier);

            HoldActivation(Opposition, () => V.OpponentMonsterCount == 0 && !V.IsFirstTurn);

            Prefer(Searches, StrategyScore, Problem, BrilliantMove, OpeningE4, Opposition, Verdict, EndgameProblem);
            Prefer(Searches, Terraforming, Problem);
            Prefer(Searches, Bifrost, Problem);
            Prefer(Searches, 0, Problem, StrategyScore, OpeningE4, BrilliantMove);
            Prefer(ToGrave, BurialGoods, BrilliantMove, StrategyScore, Verdict, Opposition, Disturbance);
            Prefer(ToGrave, Problem, BurialGoods, Disturbance, Opposition, DominusPurge, DominusImpulse, Bifrost);
            Prefer(ToGrave, 0, BurialGoods, Bifrost, Disturbance, Opposition);
            Avoid(ToGrave, 0, Problem, StrategyScore, OpeningE4);
            Prefer(SummonPicks, 0, Shatranga, Bastion, Destrier, Castellan, Enlisted);
            Avoid(Banish, Bifrost, Shatranga, Bastion, Destrier);
        }
    }
}
