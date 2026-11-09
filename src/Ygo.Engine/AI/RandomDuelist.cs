using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.AI
{
    /// <summary>
    /// Picks a random legal answer to every prompt. Plays badly on purpose: its job is to
    /// exercise the engine and to be the fallback that smarter deck-specific bots build on.
    /// </summary>
    public sealed class RandomDuelist : IDuelist
    {
        private const int MaxActionsPerPhase = 25;
        private const int GiveUpAfterAttempts = 50;

        private readonly Random _rng;
        private readonly ICardDataProvider _cards;
        private readonly Action<string>? _log;
        private readonly string _name;
        private int _actionsThisPhase;
        private List<CardInfo>? _declarationPool;

        public RandomDuelist(string name, int seed, ICardDataProvider cards, Action<string>? log = null)
        {
            _name = name;
            _rng = new Random(seed);
            _cards = cards;
            _log = log;
        }

        public void OnPhaseChanged() => _actionsThisPhase = 0;

        public byte[] Respond(Prompt prompt, int attempt)
        {
            return prompt switch
            {
                IdlePrompt p => Idle(p),
                BattlePrompt p => Battle(p),
                EffectYesNoPrompt p => Responses.YesNo(_rng.NextDouble() < 0.8),
                YesNoPrompt _ => Responses.YesNo(_rng.NextDouble() < 0.7),
                OptionPrompt p => Responses.Int32(_rng.Next(p.Options.Count)),
                SelectCardPrompt p => SelectCards(p, attempt),
                TributePrompt p => Tribute(p, attempt),
                SelectUnselectPrompt p => SelectUnselect(p, attempt),
                ChainPrompt p => Chain(p),
                PlacePrompt p => Place(p),
                PositionPrompt p => Responses.Int32((int)RandomBit(p.Positions & 0xf)),
                CounterPrompt p => Counters(p),
                SumPrompt p => Sum(p),
                SortPrompt _ => Responses.DefaultSort(),
                AnnounceRacePrompt p => Responses.Race(RandomBits(p.Available, p.Count)),
                AnnounceAttributePrompt p => Responses.Attribute((uint)RandomBits(p.Available, p.Count)),
                AnnounceCardPrompt p => Responses.Int32((int)DeclareCard(p, attempt)),
                AnnounceNumberPrompt p => Responses.Int32(_rng.Next(p.Options.Count)),
                RockPaperScissorsPrompt _ => Responses.Int32(_rng.Next(1, 4)),
                _ => throw new NotSupportedException(prompt.GetType().Name),
            };
        }

        private byte[] Idle(IdlePrompt p)
        {
            var actions = new List<(IdleAction action, int index, string what, int weight)>();
            void AddAll(List<CardRef> cards, IdleAction action, string verb, int weight)
            {
                for (int i = 0; i < cards.Count; i++) actions.Add((action, i, $"{verb} {Name(cards[i].Code)}", weight));
            }
            AddAll(p.Summonable, IdleAction.Summon, "Normal Summon", 4);
            AddAll(p.SpSummonable, IdleAction.SpecialSummon, "Special Summon", 4);
            AddAll(p.MonsterSettable, IdleAction.SetMonster, "Set monster", 1);
            AddAll(p.SpellSettable, IdleAction.SetSpellTrap, "Set", 2);
            for (int i = 0; i < p.Activatable.Count; i++)
                actions.Add((IdleAction.Activate, i, $"activate {Name(p.Activatable[i].Card.Code)}", 4));

            bool wantsToMoveOn = actions.Count == 0 || _actionsThisPhase >= MaxActionsPerPhase || _rng.NextDouble() < 0.12;
            if (wantsToMoveOn && (p.CanBattlePhase || p.CanEndPhase))
            {
                _actionsThisPhase = 0;
                if (p.CanBattlePhase && _rng.NextDouble() < 0.85)
                {
                    Decide("goes to the Battle Phase");
                    return Responses.Idle(IdleAction.ToBattlePhase);
                }
                if (p.CanEndPhase)
                {
                    Decide("ends the turn");
                    return Responses.Idle(IdleAction.ToEndPhase);
                }
                return Responses.Idle(IdleAction.ToBattlePhase);
            }
            if (actions.Count == 0) return Responses.Idle(IdleAction.ToEndPhase);

            _actionsThisPhase++;
            var choice = Weighted(actions);
            Decide(choice.what);
            return Responses.Idle(choice.action, choice.index);
        }

        private byte[] Battle(BattlePrompt p)
        {
            _actionsThisPhase++;
            if (_actionsThisPhase <= MaxActionsPerPhase)
            {
                if (p.Activatable.Count > 0 && _rng.NextDouble() < 0.15)
                {
                    int i = _rng.Next(p.Activatable.Count);
                    Decide($"activates {Name(p.Activatable[i].Card.Code)} in battle");
                    return Responses.Battle(BattleAction.Activate, i);
                }
                if (p.Attackers.Count > 0 && _rng.NextDouble() < 0.9)
                {
                    int i = _rng.Next(p.Attackers.Count);
                    Decide($"attacks with {Name(p.Attackers[i].Code)}");
                    return Responses.Battle(BattleAction.Attack, i);
                }
            }
            if (p.CanMain2 && _rng.NextDouble() < 0.6)
            {
                Decide("goes to Main Phase 2");
                return Responses.Battle(BattleAction.ToMain2);
            }
            if (!p.CanMain2 && !p.CanEndPhase && p.Attackers.Count > 0)
            {
                // A forced attack (a card says monsters must attack): any attacker.
                int i = _rng.Next(p.Attackers.Count);
                Decide($"must attack: attacks with {Name(p.Attackers[i].Code)}");
                return Responses.Battle(BattleAction.Attack, i);
            }
            Decide("ends the turn");
            return Responses.LeaveBattle(p);
        }

        private byte[] SelectCards(SelectCardPrompt p, int attempt)
        {
            if (p.Cancelable && attempt >= GiveUpAfterAttempts) return Responses.CancelSelection();
            int min = Math.Max(p.Min, 1);
            int max = Math.Max(min, p.Max);
            int count = Math.Min(p.Cards.Count, _rng.Next(min, max + 1));
            return Responses.Cards(Shuffled(p.Cards.Count).Take(count).ToList());
        }

        private byte[] Tribute(TributePrompt p, int attempt)
        {
            if (p.Cancelable && attempt >= GiveUpAfterAttempts) return Responses.CancelSelection();
            var picked = new List<int>();
            int total = 0;
            foreach (var i in Shuffled(p.Cards.Count))
            {
                if (total >= p.Min || picked.Count >= p.Max) break;
                picked.Add(i);
                total += p.ReleaseValues[i];
            }
            return Responses.Cards(picked);
        }

        private byte[] SelectUnselect(SelectUnselectPrompt p, int attempt)
        {
            bool canStop = p.Finishable || p.Cancelable;
            if (canStop && (p.Selectable.Count == 0 || attempt >= GiveUpAfterAttempts || (p.Finishable && _rng.NextDouble() < 0.4)))
                return Responses.CancelSelection();
            if (p.Selectable.Count > 0) return Responses.PickOne(_rng.Next(p.Selectable.Count));
            return Responses.PickOne(p.Selectable.Count + _rng.Next(p.Unselectable.Count));
        }

        private byte[] Chain(ChainPrompt p)
        {
            if (p.Chains.Count == 0) return Responses.NoChain();
            if (p.Forced || _rng.NextDouble() < 0.5)
            {
                int i = _rng.Next(p.Chains.Count);
                Decide($"chains {Name(p.Chains[i].Card.Code)}");
                return Responses.Int32(i);
            }
            return Responses.NoChain();
        }

        private byte[] Place(PlacePrompt p)
        {
            var available = new List<(byte player, uint location, byte sequence)>();
            byte self = p.Player, opponent = (byte)(1 - p.Player);
            // Prompted player's zones live in the low 16 bits, opponent's in the high 16.
            foreach (var (player, shift) in new[] { (self, 0), (opponent, 16) })
            {
                for (byte seq = 0; seq < 7; seq++)
                    if ((p.BlockedZones & (1u << (shift + seq))) == 0) available.Add((player, Location.MZone, seq));
                for (byte seq = 0; seq < 8; seq++)
                    if ((p.BlockedZones & (1u << (shift + 8 + seq))) == 0) available.Add((player, Location.SZone, seq));
            }
            // Prefer own zones when placing cards; disabling targets the opponent first.
            var preferred = available.Where(z => (z.player == self) != p.DisableField).ToList();
            var pool = preferred.Count >= p.Count ? preferred : available;
            return Responses.Places(pool.OrderBy(_ => _rng.Next()).Take(p.Count));
        }

        private byte[] Counters(CounterPrompt p)
        {
            var result = new ushort[p.Cards.Count];
            int remaining = p.Count;
            foreach (var i in Shuffled(p.Cards.Count))
            {
                var take = Math.Min(remaining, p.Counters[i]);
                result[i] = (ushort)take;
                remaining -= take;
            }
            return Responses.Counters(result);
        }

        private byte[] Sum(SumPrompt p)
        {
            for (int tries = 0; tries < 500; tries++)
            {
                var order = Shuffled(p.Cards.Count);
                var picked = new List<int>();
                foreach (var i in order)
                {
                    if (picked.Count >= Math.Max(p.Max, p.Min) && p.Exact) break;
                    picked.Add(i);
                    if (SumIsValid(p, picked)) return Responses.Cards(picked);
                }
            }
            // Nothing found; let the core say RETRY rather than hang.
            return Responses.Cards(Enumerable.Range(0, Math.Min(p.Cards.Count, Math.Max(p.Min, 1))).ToList());
        }

        /// <summary>Mirrors the core's SelectSum validation (playerop.cpp).</summary>
        internal static bool SumIsValid(SumPrompt p, List<int> picked)
        {
            var values = p.MustValues.Concat(picked.Select(i => p.Values[i])).ToList();
            if (p.Exact)
            {
                if (picked.Count < p.Min || picked.Count > p.Max) return false;
                // Each card counts as either its low or (if non-zero) high value.
                var reachable = new HashSet<int> { 0 };
                foreach (var v in values)
                {
                    int o1 = (int)(v & 0xffff), o2 = (int)(v >> 16);
                    var next = new HashSet<int>();
                    foreach (var s in reachable)
                    {
                        next.Add(s + o1);
                        if (o2 > 0) next.Add(s + o2);
                    }
                    reachable = next;
                }
                return reachable.Contains(p.Target);
            }
            int sum = 0, max = 0, smallest = int.MaxValue;
            foreach (var v in values)
            {
                int o1 = (int)(v & 0xffff), o2 = (int)(v >> 16);
                int low = (o2 != 0 && o2 < o1) ? o2 : o1;
                sum += low;
                max += Math.Max(o1, o2);
                smallest = Math.Min(smallest, low);
            }
            return max >= p.Target && sum - smallest < p.Target;
        }

        private uint DeclareCard(AnnounceCardPrompt p, int attempt)
        {
            _declarationPool ??= _cards.All.Where(c => c.Code != 0).OrderBy(_ => _rng.Next()).ToList();
            var legal = _declarationPool.Where(c => CardDeclaration.IsDeclarable(c, p.Opcodes)).ToList();
            if (legal.Count == 0) return _declarationPool[attempt % _declarationPool.Count].Code;
            var pick = legal[_rng.Next(legal.Count)];
            Decide($"declares {pick.Name}");
            return pick.Code;
        }

        private (IdleAction action, int index, string what, int weight) Weighted(
            List<(IdleAction action, int index, string what, int weight)> options)
        {
            int roll = _rng.Next(options.Sum(o => o.weight));
            foreach (var o in options)
            {
                if (roll < o.weight) return o;
                roll -= o.weight;
            }
            return options[options.Count - 1];
        }

        private List<int> Shuffled(int count) => Enumerable.Range(0, count).OrderBy(_ => _rng.Next()).ToList();

        private uint RandomBit(uint mask)
        {
            var bits = Enumerable.Range(0, 32).Where(b => (mask & (1u << b)) != 0).ToList();
            return bits.Count == 0 ? 1u : 1u << bits[_rng.Next(bits.Count)];
        }

        private ulong RandomBits(ulong mask, int count)
        {
            var bits = Enumerable.Range(0, 64).Where(b => (mask & (1UL << b)) != 0).OrderBy(_ => _rng.Next()).Take(count);
            return bits.Aggregate(0UL, (acc, b) => acc | (1UL << b));
        }

        private string Name(uint code) => _cards.Get(code)?.Name ?? code.ToString();

        private void Decide(string what) => _log?.Invoke($"{_name} {what}");
    }
}
