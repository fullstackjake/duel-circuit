using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.AI;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.Interactive
{
    /// <summary>
    /// Turns core prompts into <see cref="ChoiceSet"/>s a person can answer, describing cards only as
    /// the prompted player is allowed to see them. Routine prompts with an obvious answer
    /// (no chain available, default sort order, ordinary zone placement) are answered automatically.
    /// </summary>
    public sealed class PromptPresenter
    {
        private readonly DuelContext _ctx;
        private readonly ICardDataProvider _cards;

        /// <summary>When false, normal summons/sets go to the first free zone instead of asking.</summary>
        public bool AskForZones { get; set; }

        /// <summary>When the game stops to ask "Chain?" (Master Duel's Auto / On / Off).</summary>
        public ResponseMode Mode { get; set; } = ResponseMode.On;

        /// <summary>Held down by the player: ask at the next chance to respond whatever the mode.</summary>
        public Func<bool>? HoldToAsk { get; set; }

        /// <summary>Chances to respond the mode let pass without asking (for checks and the duel log).</summary>
        public int SkippedResponses { get; private set; }

        private int _askedSerial = -1;

        /// <summary>
        /// Whether to ask about an optional chance to respond. The core's SpeCount counts optional triggered effects
        /// waiting to activate plus effects whose script marks this moment as a good time to use them.
        /// <list type="bullet">
        /// <item>On: every chance.</item>
        /// <item>Auto: when your opponent has just activated something (the newest chain link is theirs), summoned or
        /// attacked (once per such play), or when the game says one of your effects suits this moment.</item>
        /// <item>Off: only for your own cards' effects right after your own plays (triggers you'd otherwise lose).</item>
        /// </list>
        /// Holding the ask key overrides Auto and Off for that moment.
        /// </summary>
        private bool ShouldAsk(ChainPrompt p)
        {
            if (p.Forced || Mode == ResponseMode.On || HoldToAsk?.Invoke() == true) return true;
            int me = p.Player;
            bool chain = _ctx.Chain.Count > 0;
            bool afterMine = chain ? _ctx.Chain[_ctx.Chain.Count - 1].Controller == me : _ctx.LastEventPlayer == me;
            if (Mode == ResponseMode.Off) return p.SpeCount > 0 && afterMine && !chain;
            if (chain) return !afterMine || p.SpeCount > 0;
            if (p.SpeCount > 0) return true;
            if (_ctx.LastEventPlayer >= 0 && !afterMine && _ctx.LastEvent != DuelEventKind.None && _ctx.EventSerial != _askedSerial)
            {
                _askedSerial = _ctx.EventSerial;
                return true;
            }
            return false;
        }

        public PromptPresenter(DuelContext context)
        {
            _ctx = context;
            _cards = context.Cards;
        }

        /// <summary>Returns the response for prompts that need no decision, or null if a person should decide.</summary>
        public byte[]? AutoAnswer(Prompt prompt)
        {
            switch (prompt)
            {
                case ChainPrompt p when p.Chains.Count == 0:
                    return Responses.NoChain();
                case ChainPrompt p when !ShouldAsk(p):
                    SkippedResponses++;
                    return Responses.NoChain();
                case PlacePrompt p when !p.DisableField && !AskForZones:
                    return Responses.Places(FreeZones(p).Take(p.Count)); // own zones first, center-out
                case CounterPrompt p:
                    return AutoCounters(p);
                default:
                    return null;
            }
        }

        public ChoiceSet Present(Prompt prompt)
        {
            var set = prompt switch
            {
                IdlePrompt p => Idle(p),
                BattlePrompt p => Battle(p),
                ChainPrompt p => Chain(p),
                EffectYesNoPrompt p => YesNo(p, $"Use the effect of {Name(p.Card, p.Player)}?", DuelText.Describe(p.Description, _cards)),
                YesNoPrompt p => YesNo(p, Fallback(DuelText.Describe(p.Description, _cards), "Confirm?"), ""),
                OptionPrompt p => Options(p, "Choose an effect", p.Options.Select(o => Fallback(DuelText.Describe(o, _cards), $"Option {p.Options.IndexOf(o) + 1}")).ToList()),
                SelectCardPrompt p => Cards(p, p.Cards, p.Min, p.Max, p.Cancelable),
                TributePrompt p => Tribute(p),
                SelectUnselectPrompt p => SelectUnselect(p),
                SumPrompt p => Sum(p),
                PlacePrompt p => Place(p),
                PositionPrompt p => Position(p),
                AnnounceRacePrompt p => Flags(p, "Declare a Type", p.Count, DuelText.Races.Where(r => (p.Available & r.bit) != 0).ToList(),
                    bits => Responses.Race(bits)),
                AnnounceAttributePrompt p => Flags(p, "Declare an Attribute", p.Count,
                    DuelText.Attributes.Where(a => (p.Available & a.bit) != 0).Select(a => ((ulong)a.bit, a.name)).ToList(),
                    bits => Responses.Attribute((uint)bits)),
                AnnounceCardPrompt p => AnnounceCard(p),
                AnnounceNumberPrompt p => Options(p, "Declare a number", p.Options.Select(o => o.ToString()).ToList()),
                RockPaperScissorsPrompt p => RockPaperScissors(p),
                CounterPrompt p => Auto(p, "Placing counters", AutoCounters(p)),
                SortPrompt p => Order(p),
                _ => throw new NotSupportedException(prompt.GetType().Name),
            };
            set.Prompt = prompt;
            return set;
        }

        // ---------------------------------------------------------------- Turn structure

        private ChoiceSet Idle(IdlePrompt p)
        {
            int me = p.Player;
            var set = new ChoiceSet { Title = $"{Phase.Name(_ctx.Board.Phase)}: choose an action" };
            var responses = new List<byte[]>();
            void Add(string label, CardRef? card, byte[] response, string verb = "")
            {
                set.Choices.Add(new Choice { Label = label, Card = card, Verb = verb });
                responses.Add(response);
            }

            for (int i = 0; i < p.Activatable.Count; i++)
            {
                var o = p.Activatable[i];
                Add(Activation(o, me), o.Card, Responses.Idle(IdleAction.Activate, i), EffectVerb(p.Activatable, i));
            }
            for (int i = 0; i < p.Summonable.Count; i++)
                Add($"Normal Summon {Name(p.Summonable[i], me)}", p.Summonable[i], Responses.Idle(IdleAction.Summon, i), "Summon");
            for (int i = 0; i < p.SpSummonable.Count; i++)
            {
                var c = p.SpSummonable[i];
                var from = c.Location == Location.Extra ? " from the Extra Deck" : c.Location == Location.Grave ? " from the GY" : "";
                Add($"Special Summon {Name(c, me)}{from}", c, Responses.Idle(IdleAction.SpecialSummon, i), "Special Summon");
            }
            for (int i = 0; i < p.MonsterSettable.Count; i++)
                Add($"Set {Name(p.MonsterSettable[i], me)} (monster)", p.MonsterSettable[i], Responses.Idle(IdleAction.SetMonster, i), "Set");
            for (int i = 0; i < p.SpellSettable.Count; i++)
                Add($"Set {Name(p.SpellSettable[i], me)}", p.SpellSettable[i], Responses.Idle(IdleAction.SetSpellTrap, i), "Set");
            for (int i = 0; i < p.Repositionable.Count; i++)
            {
                var c = p.Repositionable[i];
                var live = Live(c);
                var label = live == null ? "Change the position of" : !live.IsFaceUp ? "Flip Summon" : live.IsDefense ? "Switch to Attack Position:" : "Switch to Defense Position:";
                var verb = live == null ? "Change position" : !live.IsFaceUp ? "Flip Summon" : live.IsDefense ? "To Attack" : "To Defense";
                Add($"{label} {Name(c, me)}", c, Responses.Idle(IdleAction.ChangePosition, i), verb);
            }
            if (p.CanBattlePhase) Add("Go to the Battle Phase", null, Responses.Idle(IdleAction.ToBattlePhase));
            if (p.CanEndPhase) Add("End your turn", null, Responses.Idle(IdleAction.ToEndPhase));

            set.Build = idx => responses[idx[0]];
            return set;
        }

        /// <summary>"Activate", or "Effect 2" when the same card has more than one effect to choose from.</summary>
        private static string EffectVerb(List<ChainOption> options, int i)
        {
            var same = options.Select((o, k) => (o, k)).Where(x => x.o.Card.Code == options[i].Card.Code && x.o.Card.Location == options[i].Card.Location
                                                             && x.o.Card.Sequence == options[i].Card.Sequence).Select(x => x.k).ToList();
            return same.Count > 1 ? $"Effect {same.IndexOf(i) + 1}" : "Activate";
        }

        private ChoiceSet Battle(BattlePrompt p)
        {
            int me = p.Player;
            var set = new ChoiceSet { Title = "Battle Phase: choose an action" };
            var responses = new List<byte[]>();
            void Add(string label, CardRef? card, byte[] response, string verb = "")
            {
                set.Choices.Add(new Choice { Label = label, Card = card, Verb = verb });
                responses.Add(response);
            }

            for (int i = 0; i < p.Attackers.Count; i++)
            {
                var a = p.Attackers[i];
                var atk = Live(a)?.Attack;
                Add($"Attack with {Name(a, me)}{(atk.HasValue ? $" ({atk} ATK)" : "")}", a, Responses.Battle(BattleAction.Attack, i), "Attack");
            }
            for (int i = 0; i < p.Activatable.Count; i++)
                Add(Activation(p.Activatable[i], me), p.Activatable[i].Card, Responses.Battle(BattleAction.Activate, i), EffectVerb(p.Activatable, i));
            if (p.CanMain2) Add("Go to Main Phase 2", null, Responses.Battle(BattleAction.ToMain2));
            if (p.CanEndPhase) Add("End your turn", null, Responses.Battle(BattleAction.ToEndPhase));

            set.Build = idx => responses[idx[0]];
            return set;
        }

        /// <summary>
        /// Effects of yours that activate at the same time (two mandatory triggers), or cards you place in an order (on
        /// top of the Deck): you choose the order. The core takes each card's position, 0 first: for a chain the first goes
        /// on the chain first (the lowest Chain Link, so it resolves last); for cards the first ends up on top.
        /// </summary>
        private ChoiceSet Order(SortPrompt p)
        {
            int me = p.Player, n = p.Cards.Count;
            var set = new ChoiceSet
            {
                Mode = ChoiceMode.Order,
                Min = n,
                Max = n,
                Title = p.IsChain ? "Choose the chain order" : "Choose the order of the cards",
                Detail = p.IsChain
                    ? $"{n} of your effects activate at the same time. Click them in the order they go on the chain: the first becomes the lowest Chain Link and resolves last."
                    : "Click the cards in order: the first ends up nearest the top of the Deck.",
                ChainLength = p.IsChain ? _ctx.Chain.Count : 0,
            };
            foreach (var c in p.Cards) set.Choices.Add(new Choice { Label = Label(c, me), Card = c });
            set.Build = picks =>
            {
                var positions = new byte[n];
                for (int k = 0; k < picks.Count && k < n; k++) positions[picks[k]] = (byte)k;
                return positions;
            };
            set.FinishBytes = Responses.DefaultSort(); // "Default order"
            return set;
        }

        private ChoiceSet Chain(ChainPrompt p)
        {
            int me = p.Player;
            var last = _ctx.Chain.Count > 0 ? _ctx.Chain[_ctx.Chain.Count - 1] : null;
            string title;
            if (last != null)
                title = $"{(last.Controller == me ? "You" : "Your opponent")} activated {_cards.Get(last.Code)?.Name ?? "a card"}. Respond?";
            else
                title = p.Forced ? "Activate a required effect" : "You can activate a card or effect";
            var set = new ChoiceSet { Title = title };
            for (int i = 0; i < p.Chains.Count; i++)
                set.Choices.Add(new Choice { Label = Activation(p.Chains[i], me), Card = p.Chains[i].Card, Verb = EffectVerb(p.Chains, i) });
            set.IsChain = true;
            set.ChainLength = _ctx.Chain.Count;
            set.Build = idx => Responses.Int32(idx[0]);
            if (!p.Forced) set.CancelBytes = Responses.NoChain();
            set.Detail = p.Forced ? "" : "Click Don't chain to let it go.";
            return set;
        }

        private ChoiceSet YesNo(Prompt p, string title, string detail)
        {
            var set = new ChoiceSet { Title = title, Detail = detail };
            set.Choices.Add(new Choice { Label = "Yes" });
            set.Choices.Add(new Choice { Label = "No" });
            set.Build = idx => Responses.YesNo(idx[0] == 0);
            return set;
        }

        private ChoiceSet Options(Prompt p, string title, List<string> labels)
        {
            var set = new ChoiceSet { Title = title };
            foreach (var label in labels) set.Choices.Add(new Choice { Label = label });
            set.Build = idx => Responses.Int32(idx[0]);
            return set;
        }

        // ---------------------------------------------------------------- Card selections

        private ChoiceSet Cards(Prompt p, List<CardRef> cards, int min, int max, bool cancelable)
        {
            int me = p.Player;
            var hint = _ctx.PromptHint[me & 1];
            var title = Fallback(DuelText.Describe(hint, _cards), "Select card(s)") + Purpose();
            var set = new ChoiceSet
            {
                Title = title,
                Detail = min == max ? $"Choose {min}." : $"Choose {min} to {max}.",
                Mode = max > 1 ? ChoiceMode.Multi : ChoiceMode.Single,
                Min = min,
                Max = max,
            };
            foreach (var c in cards) set.Choices.Add(new Choice { Label = Label(c, me), Card = c });
            set.Build = idx => Responses.Cards(idx);
            if (cancelable) set.CancelBytes = Responses.CancelSelection();
            return set;
        }

        private ChoiceSet Tribute(TributePrompt p)
        {
            var set = Cards(p, p.Cards, 1, p.Max, p.Cancelable);
            set.Title = "Select the monster(s) to Tribute";
            set.Detail = p.Min == 1 ? "Tribute 1 monster." : $"Tribute {p.Min} monsters.";
            // Usually each monster is one Tribute, so the count is known; a few count as two,
            // and then the core checks the total.
            set.Min = p.ReleaseValues.All(v => v == 1) ? Math.Min(p.Min, p.Max) : 1;
            return set;
        }

        private ChoiceSet SelectUnselect(SelectUnselectPrompt p)
        {
            int me = p.Player;
            var hint = _ctx.PromptHint[me & 1];
            var set = new ChoiceSet
            {
                Title = Fallback(DuelText.Describe(hint, _cards), "Select card(s)") + Purpose(),
                Detail = $"Picked {p.Unselectable.Count} so far (need {p.Min}{(p.Max > p.Min ? $" to {p.Max}" : "")}).",
                Mode = ChoiceMode.Incremental,
            };
            foreach (var c in p.Selectable) set.Choices.Add(new Choice { Label = Label(c, me), Card = c });
            foreach (var c in p.Unselectable) set.Choices.Add(new Choice { Label = $"{Label(c, me)} (picked, choose to remove)", Card = c, AlreadySelected = true });
            set.Build = idx => Responses.PickOne(idx[0]);
            if (p.Finishable) set.FinishBytes = Responses.CancelSelection();
            if (p.Cancelable) set.CancelBytes = Responses.CancelSelection();
            return set;
        }

        private ChoiceSet Sum(SumPrompt p)
        {
            int me = p.Player;
            var set = Cards(p, p.Cards, 1, Math.Max(1, p.Cards.Count), false); // the core checks the Level total
            set.Title = Fallback(DuelText.Describe(_ctx.PromptHint[me & 1], _cards), "Select materials") + Purpose();
            set.Detail = p.Exact ? $"Their Levels must add up to exactly {p.Target}." : $"Their Levels must add up to at least {p.Target}.";
            set.Mode = ChoiceMode.Multi;
            for (int i = 0; i < p.Cards.Count; i++)
                set.Choices[i].Label += $" - Level {p.Values[i] & 0xffff}";
            return set;
        }

        private ChoiceSet Place(PlacePrompt p)
        {
            int me = p.Player;
            var zones = FreeZones(p).ToList();
            var set = new ChoiceSet
            {
                Title = p.DisableField ? "Choose a zone to disable" : "Choose a zone",
                Mode = p.Count > 1 ? ChoiceMode.Multi : ChoiceMode.Single,
                Min = p.Count,
                Max = p.Count,
            };
            // Each zone is also a place on the board (an empty zone the UI can light up and take clicks on).
            foreach (var z in zones)
                set.Choices.Add(new Choice
                {
                    Label = DuelText.Zone(new CardRef(0, z.player, z.location, z.sequence), me),
                    Card = new CardRef(0, z.player, z.location, z.sequence),
                });
            set.Build = idx => Responses.Places(idx.Select(i => zones[i]));
            return set;
        }

        private ChoiceSet Position(PositionPrompt p)
        {
            var name = _cards.Get(p.Code)?.Name ?? "the monster";
            var set = new ChoiceSet { Title = $"Choose a position for {name}" };
            var positions = new[] { Ygo.Engine.Position.FaceUpAttack, Ygo.Engine.Position.FaceUpDefense, Ygo.Engine.Position.FaceDownDefense, Ygo.Engine.Position.FaceDownAttack }
                .Where(pos => (p.Positions & pos) != 0).ToList();
            foreach (var pos in positions) set.Choices.Add(new Choice { Label = DuelText.Position(pos) });
            set.Build = idx => Responses.Int32((int)positions[idx[0]]);
            return set;
        }

        private ChoiceSet Flags(Prompt p, string title, int count, List<(ulong bit, string name)> available, Func<ulong, byte[]> build)
        {
            var set = new ChoiceSet
            {
                Title = title,
                Mode = count > 1 ? ChoiceMode.Multi : ChoiceMode.Single,
                Min = count,
                Max = count,
            };
            foreach (var a in available) set.Choices.Add(new Choice { Label = a.name });
            set.Build = idx => build(idx.Aggregate(0UL, (acc, i) => acc | available[i].bit));
            return set;
        }

        /// <summary>
        /// Declaring a card name: offers every legal name among cards seen so far in the duel
        /// (both players' public cards, plus your own hand and Deck).
        /// </summary>
        private ChoiceSet AnnounceCard(AnnounceCardPrompt p)
        {
            int me = p.Player;
            var board = _ctx.Board;
            var seen = new HashSet<uint>();
            for (int player = 0; player < 2; player++)
            {
                var pb = board.Players[player];
                var visible = pb.Graveyard.Concat(pb.Banished).Concat(pb.Monsters).Concat(pb.SpellTraps)
                    .Where(c => c != null && (player == me || c.IsFaceUp || pb.Graveyard.Contains(c) || pb.Banished.Contains(c)));
                if (player == me) visible = visible.Concat(pb.Hand).Concat(pb.ExtraDeck);
                foreach (var c in visible) if (c != null && c.Code != 0) seen.Add(c.Code);
            }
            var legal = seen.Select(code => _cards.Get(code)).Where(i => i != null && CardDeclaration.IsDeclarable(i, p.Opcodes))
                .Select(i => i!).OrderBy(i => i.Name).ToList();
            if (legal.Count == 0)
                legal = _cards.All.Where(i => CardDeclaration.IsDeclarable(i, p.Opcodes)).OrderBy(i => i.Name).Take(30).ToList();

            var set = new ChoiceSet { Title = "Declare a card name", Detail = "Cards seen so far that you can declare." };
            foreach (var info in legal) set.Choices.Add(new Choice { Label = info.Name });
            set.Build = idx => Responses.Int32((int)legal[idx[0]].Code);
            return set;
        }

        private ChoiceSet RockPaperScissors(Prompt p)
        {
            var set = new ChoiceSet { Title = "Rock, paper, scissors!" };
            foreach (var label in new[] { "Scissors", "Rock", "Paper" }) set.Choices.Add(new Choice { Label = label });
            set.Build = idx => Responses.Int32(idx[0] + 1);
            return set;
        }

        private static ChoiceSet Auto(Prompt p, string title, byte[] response)
        {
            var set = new ChoiceSet { Title = title };
            set.Choices.Add(new Choice { Label = "OK" });
            set.Build = _ => response;
            return set;
        }

        // ---------------------------------------------------------------- Helpers

        private IEnumerable<(byte player, uint location, byte sequence)> FreeZones(PlacePrompt p)
        {
            byte self = p.Player, opp = (byte)(1 - p.Player);
            byte[] centerOut = { 2, 1, 3, 0, 4, 5, 6, 7 };
            foreach (var (player, shift) in new[] { (self, 0), (opp, 16) })
            {
                foreach (var seq in centerOut)
                    if (seq < 7 && (p.BlockedZones & (1u << (shift + seq))) == 0) yield return (player, Location.MZone, seq);
                foreach (var seq in centerOut)
                    if ((p.BlockedZones & (1u << (shift + 8 + seq))) == 0) yield return (player, Location.SZone, seq);
            }
        }

        private static byte[] AutoCounters(CounterPrompt p)
        {
            var result = new ushort[p.Cards.Count];
            int remaining = p.Count;
            for (int i = 0; i < p.Cards.Count && remaining > 0; i++)
            {
                var take = Math.Min(remaining, p.Counters[i]);
                result[i] = (ushort)take;
                remaining -= take;
            }
            return Responses.Counters(result);
        }

        private string Activation(ChainOption o, int me)
        {
            var effect = DuelText.Describe(o.Description, _cards);
            var from = (o.Card.Location & 0x7f) == Location.Grave ? " (in GY)" : (o.Card.Location & 0x7f) == Location.Hand ? " (in hand)" : "";
            return effect.Length > 0 ? $"Activate {Name(o.Card, me)}{from}: {effect}" : $"Activate {Name(o.Card, me)}{from}";
        }

        private string Label(CardRef c, int me)
        {
            var zone = DuelText.Zone(c, me);
            return zone.Length > 0 ? $"{Name(c, me)} ({zone})" : Name(c, me);
        }

        /// <summary>A card's name if the viewer may know it, otherwise a neutral description.</summary>
        private string Name(CardRef c, int me)
        {
            if (IsHiddenFrom(c, me)) return (c.Location & 0x7f) == Location.Hand ? "a card in hand" : "a face-down card";
            return _cards.Get(c.Code)?.Name ?? $"card #{c.Code}";
        }

        private bool IsHiddenFrom(CardRef c, int me)
        {
            if (c.Controller == me) return false; // you always know your own cards
            var loc = c.Location & 0x7f;
            if (loc == Location.Grave || loc == Location.Removed) return false;
            if (loc == Location.Hand || loc == Location.Deck || loc == Location.Extra) return true;
            var live = Live(c);
            const uint FaceUp = 0x1 | 0x4;
            return live != null ? !live.IsFaceUp : (c.Position & FaceUp) == 0;
        }

        private FieldCard? Live(CardRef c)
        {
            var p = _ctx.Board.Players[c.Controller & 1];
            List<FieldCard?>? list = (c.Location & 0x7f) switch
            {
                Location.MZone => p.Monsters,
                Location.SZone => p.SpellTraps,
                Location.Hand => p.Hand,
                Location.Grave => p.Graveyard,
                Location.Removed => p.Banished,
                _ => null,
            };
            return list != null && c.Sequence < list.Count ? list[(int)c.Sequence] : null;
        }

        private static string Fallback(string text, string fallback) => string.IsNullOrEmpty(text) ? fallback : text;

        /// <summary>
        /// What a selection is for, from the chain: "(to activate Called by the Grave)" while a card's costs and
        /// targets are being chosen, "(for Pot of Desires's effect)" while it resolves. "" otherwise.
        /// </summary>
        private string Purpose()
        {
            if (_ctx.ActivatingCode != 0) return $" (to activate {_cards.Get(_ctx.ActivatingCode)?.Name ?? "a card"})";
            if (_ctx.ChainResolving && _ctx.ResolvingCode != 0) return $" (for {_cards.Get(_ctx.ResolvingCode)?.Name ?? "a card"}'s effect)";
            return "";
        }
    }

    /// <summary>When the game asks about optional chances to respond.</summary>
    public enum ResponseMode { Auto, On, Off }
}
