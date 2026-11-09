using Ygo.Engine.Protocol;

namespace Ygo.Engine.AI
{
    /// <summary>
    /// A practice dummy for measuring a deck ("goldfishing"): sets one monster per turn when it can,
    /// never activates or responds, and otherwise passes.
    /// </summary>
    public sealed class DummyDuelist : IDuelist
    {
        private readonly RandomDuelist _fallback;
        private bool _setThisTurn;

        public DummyDuelist(int seed, ICardDataProvider cards)
        {
            _fallback = new RandomDuelist("Dummy", seed, cards);
        }

        public void OnPhaseChanged() { }

        public byte[]? Respond(Prompt prompt, int attempt)
        {
            if (attempt > 0) return _fallback.Respond(prompt, attempt);
            switch (prompt)
            {
                case IdlePrompt p:
                    if (!_setThisTurn && p.MonsterSettable.Count > 0)
                    {
                        _setThisTurn = true;
                        return Responses.Idle(IdleAction.SetMonster, 0);
                    }
                    _setThisTurn = false;
                    return Responses.Idle(p.CanEndPhase ? IdleAction.ToEndPhase : IdleAction.ToBattlePhase);
                case BattlePrompt p:
                    return Responses.LeaveBattle(p);
                case ChainPrompt p:
                    return p.Forced && p.Chains.Count > 0 ? Responses.Int32(0) : Responses.NoChain();
                case EffectYesNoPrompt _:
                case YesNoPrompt _:
                    return Responses.YesNo(false);
                default:
                    return _fallback.Respond(prompt, 0);
            }
        }
    }
}
