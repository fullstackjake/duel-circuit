using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.AI
{
    /// <summary>
    /// An opponent that plays only what it's told (lessons and puzzles): sets the listed cards when it can, activates or
    /// chains the listed ones whenever offered, Normal Summons the listed monsters, and otherwise passes straight to the
    /// End Phase. Attacks only when told to.
    /// </summary>
    public sealed class ScriptedDuelist : IDuelist
    {
        private readonly RandomDuelist _fallback;

        /// <summary>Cards it activates whenever it may: from the Main Phase or in response.</summary>
        public HashSet<uint> Activate { get; } = new HashSet<uint>();
        /// <summary>Monsters it Special Summons whenever it can (an Extra Deck monster, for a set-up board).</summary>
        public HashSet<uint> Summon { get; } = new HashSet<uint>();
        /// <summary>Spells and Traps it sets face-down when it can.</summary>
        public HashSet<uint> Set { get; } = new HashSet<uint>();
        /// <summary>Monsters it Normal Summons from its hand when it can.</summary>
        public HashSet<uint> NormalSummon { get; } = new HashSet<uint>();
        /// <summary>Goes to the Battle Phase and attacks with every monster that can (the first target offered).</summary>
        public bool Attacks { get; set; }

        public ScriptedDuelist(int seed, ICardDataProvider cards)
        {
            _fallback = new RandomDuelist("Script", seed, cards);
        }

        public void OnPhaseChanged() { }

        public byte[]? Respond(Prompt prompt, int attempt)
        {
            if (attempt > 0) return _fallback.Respond(prompt, attempt);
            switch (prompt)
            {
                case IdlePrompt p:
                {
                    int activate = p.Activatable.FindIndex(o => Activate.Contains(o.Card.Code));
                    if (activate >= 0) return Responses.Idle(IdleAction.Activate, activate);
                    int summon = p.SpSummonable.FindIndex(c => Summon.Contains(c.Code));
                    if (summon >= 0) return Responses.Idle(IdleAction.SpecialSummon, summon);
                    int normal = p.Summonable.FindIndex(c => NormalSummon.Contains(c.Code));
                    if (normal >= 0) return Responses.Idle(IdleAction.Summon, normal);
                    int set = p.SpellSettable.FindIndex(c => Set.Contains(c.Code));
                    if (set >= 0) return Responses.Idle(IdleAction.SetSpellTrap, set);
                    if (Attacks && p.CanBattlePhase) return Responses.Idle(IdleAction.ToBattlePhase);
                    return Responses.Idle(p.CanEndPhase ? IdleAction.ToEndPhase : IdleAction.ToBattlePhase);
                }
                case BattlePrompt p:
                    return Attacks && p.Attackers.Count > 0 ? Responses.Battle(BattleAction.Attack, 0) : Responses.LeaveBattle(p);
                case ChainPrompt p:
                {
                    int chain = p.Chains.FindIndex(o => Activate.Contains(o.Card.Code));
                    if (chain >= 0) return Responses.Int32(chain);
                    return p.Forced && p.Chains.Count > 0 ? Responses.Int32(0) : Responses.NoChain();
                }
                case EffectYesNoPrompt p:
                    return Responses.YesNo(Activate.Contains(p.Card.Code));
                case YesNoPrompt _:
                    return Responses.YesNo(false);
                default:
                    return _fallback.Respond(prompt, 0);
            }
        }
    }
}
