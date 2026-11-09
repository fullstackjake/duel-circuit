using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Presentation;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.Replays
{
    public enum MomentKind { Turn, Chain, Hit, Summon, End }

    /// <summary>A point in a replay worth jumping to, with the replay position just before it.</summary>
    public sealed class Moment
    {
        /// <summary>Replay position (redraws played) just before the moment, so jumping there shows it happen next.</summary>
        public int Tick;
        public int Turn;
        public MomentKind Kind;
        public int Player = -1;
        public string Text = "";
    }

    /// <summary>
    /// The key moments of a replay, found by playing it through without drawing: each turn, chains of two or more links
    /// (or with a negation), hits of 1000 or more, and ace summons (Level 7+, Extra Deck), plus the end. Positions are
    /// counted exactly as the game counts them while watching (a redraw per visible event; see <see cref="Visibility"/>).
    /// </summary>
    public static class ReplayMoments
    {
        public const int MaxStepsPerTick = 2000;

        public static List<Moment> Find(Replay replay, ICardDataProvider cards, IScriptProvider scripts, int maxTicks = 20000)
        {
            var moments = new List<Moment>();
            using var duel = replay.Recipe(cards, scripts).Create();
            var feed = new ReplayFeed(replay.Responses);
            var runner = new DuelRunner(duel, new ReplayDuelist(feed), new ReplayDuelist(feed));
            YataLock.Attach(runner, duel);
            var chain = new ChainTracker();
            var names = replay.IsTag ? new[] { $"{replay.Names[0]} & {replay.PartnerNames![0]}", $"{replay.Names[1]} & {replay.PartnerNames[1]}" } : replay.Names;
            int ticks = 0, turn = 0, turnPlayer = 0;
            int[] lp = { 8000, 8000 };
            bool saw = false;
            int chainStart = 0;
            string Name(uint code) => cards.Get(code)?.Name ?? "a card";
            void Add(MomentKind kind, int player, string text) =>
                moments.Add(new Moment { Tick = ticks, Turn = turn, Kind = kind, Player = player, Text = text });

            runner.MessageReceived += m =>
            {
                saw |= Visibility.IsVisible(m[0]);
                chain.Handle(m);
                var r = new MessageReader(m, 1);
                switch (m[0])
                {
                    case Msg.NewTurn:
                        turn++;
                        turnPlayer = r.U8();
                        Add(MomentKind.Turn, turnPlayer, $"Turn {turn}: {names[turnPlayer & 1]}  (LP {lp[0]} / {lp[1]})");
                        break;
                    case Msg.Damage:
                    {
                        int player = r.U8(), amount = (int)r.U32();
                        lp[player & 1] = Math.Max(0, lp[player & 1] - amount);
                        if (amount >= 1000) Add(MomentKind.Hit, player, $"{names[player & 1]} takes {amount} ({lp[player & 1]} LP left)");
                        break;
                    }
                    case Msg.PayLpCost:
                    {
                        int player = r.U8();
                        lp[player & 1] = Math.Max(0, lp[player & 1] - (int)r.U32());
                        break;
                    }
                    case Msg.Recover:
                    {
                        int player = r.U8();
                        lp[player & 1] += (int)r.U32();
                        break;
                    }
                    case Msg.LpUpdate:
                    {
                        int player = r.U8();
                        lp[player & 1] = (int)r.U32();
                        break;
                    }
                    case Msg.SpSummoning:
                    case Msg.Summoning:
                    {
                        var card = r.LocInfo(r.U32());
                        var info = cards.Get(card.Code);
                        if (info != null && ((info.Type & CardType.ExtraDeckTypes) != 0 || (info.Level & 0xff) >= 7))
                            Add(MomentKind.Summon, card.Controller, $"{names[card.Controller & 1]} summons {info.Name}");
                        break;
                    }
                    case Msg.ChainEnd:
                    {
                        var links = chain.LastChain;
                        var negated = links.FirstOrDefault(l => l.State is LinkState.Negated or LinkState.EffectNegated);
                        if (links.Count >= 2 || negated != null)
                        {
                            // The moment is when the chain started, so the jump shows it from its first link.
                            var text = negated != null
                                ? (negated.NegatedBy > 0 ? $"{Name(links.First(l => l.Number == negated.NegatedBy).Code)} negates {Name(negated.Code)}" : $"{Name(negated.Code)} is negated")
                                : $"Chain of {links.Count}: {string.Join(", ", links.OrderBy(l => l.Number).Select(l => Name(l.Code)))}";
                            moments.Add(new Moment { Tick = chainStart, Turn = turn, Kind = MomentKind.Chain, Player = links[0].Controller, Text = text });
                        }
                        break;
                    }
                    case Msg.Chaining:
                        if (chain.Links.Count == 1) chainStart = ticks;
                        break;
                    case Msg.Win:
                    {
                        int winner = r.U8();
                        Add(MomentKind.End, winner, winner > 1 ? "The duel ends in a draw" : $"{names[winner & 1]} wins");
                        break;
                    }
                }
            };
            // Play it tick by tick, exactly as watching does.
            while (ticks < maxTicks && !runner.IsFinished)
            {
                saw = false;
                bool more = true;
                for (int i = 0; i < MaxStepsPerTick && !saw; i++)
                    if (!runner.Step() || runner.IsWaiting) { more = false; break; }
                ticks++;
                if (!more && (runner.IsFinished || runner.IsWaiting)) break;
            }
            return moments.OrderBy(m => m.Tick).ToList();
        }
    }

    /// <summary>Core messages a viewer can see; watching redraws the board after each of these.</summary>
    public static class Visibility
    {
        public static bool IsVisible(byte message)
        {
            switch (message)
            {
                case Msg.NewTurn: case Msg.NewPhase: case Msg.Draw: case Msg.Move: case Msg.PosChange: case Msg.Set:
                case Msg.Summoning: case Msg.SpSummoning: case Msg.FlipSummoning: case Msg.Chaining: case Msg.Attack:
                case Msg.Damage: case Msg.Recover: case Msg.PayLpCost: case Msg.LpUpdate: case Msg.Win:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// A Replay Lesson: a recorded duel of a deck played by its character, opened at the deck's key turn. Plays a few
    /// seeded bot duels against a fair opponent, keeps the first the deck wins (or the closest), and picks the deck's
    /// turn with the most happening (summons, activations, damage dealt). Everything shown is what the engine did.
    /// </summary>
    public static class ReplayLesson
    {
        public sealed class Result
        {
            public Replay Replay = new Replay();
            /// <summary>The key turn's number, and what happened in it.</summary>
            public int Turn;
            public string Summary = "";
        }

        public static Result Make(Personality owner, Deck deck, string deckName, Personality rival, Deck rivalDeck, string rivalDeckName,
            ICardDataProvider cards, IScriptProvider scripts, int seeds = 4)
        {
            Result? best = null;
            int bestScore = int.MinValue;
            for (int s = 1; s <= seeds; s++)
            {
                var replay = new Replay
                {
                    Settings = new DuelSettings { Seed = (ulong)(7000 + s) },
                    Decks = new[] { deck.Clone(), rivalDeck.Clone() },
                    Names = new[] { owner.Name, rival.Name },
                    Characters = new[] { owner.Id, rival.Id },
                    DeckNames = new[] { deckName, rivalDeckName },
                    CardCount = cards.All.Count(),
                };
                var recipe = replay.Recipe(cards, scripts);
                var answers = new ResponseLog();
                var perTurn = new Dictionary<int, (int summons, int activations, int damage)>();
                DuelResult result;
                using (var duel = recipe.Create())
                {
                    var context = new DuelContext(duel, cards);
                    // The lesson's deck is played at its best (planning, its playbook); the rival plays fairly.
                    var expert = new Personality { Id = owner.Id, Name = owner.Name, Skill = 1.0, Aggression = owner.Aggression, Caution = owner.Caution,
                        Patience = owner.Patience, Spending = owner.Spending };
                    var seat0 = new SmartDuelist(owner.Name, s * 2, context, personality: expert, playbook: Playbooks.ForDeck(deckName),
                        planner: new TurnPlanner(recipe, answers, expert, () => Playbooks.ForDeck(deckName)));
                    var seat1 = new SmartDuelist(rival.Name, s * 2 + 1, context, personality: rival, playbook: Playbooks.ForDeck(rivalDeckName));
                    var runner = new DuelRunner(duel, seat0, seat1, answers);
                    runner.MessageReceived += context.Observe;
                    int turn = 0, turnPlayer = 0;
                    runner.MessageReceived += m =>
                    {
                        var r = new MessageReader(m, 1);
                        if (m[0] == Msg.NewTurn) { turn++; turnPlayer = r.U8(); }
                        if (turnPlayer != 0) return;
                        var (su, ac, dm) = perTurn.TryGetValue(turn, out var v) ? v : (0, 0, 0);
                        if (m[0] is Msg.Summoning or Msg.SpSummoning) su++;
                        else if (m[0] == Msg.Chaining && r.LocInfo(r.U32()).Controller == 0) ac++;
                        else if (m[0] == Msg.Damage && r.U8() == 1) dm += (int)r.U32();
                        perTurn[turn] = (su, ac, dm);
                    };
                    result = runner.Run();
                }
                replay.Responses = answers.Responses.ToList();
                replay.Winner = result.Winner;
                replay.Turns = result.Turns;
                if (perTurn.Count == 0) continue;
                var key = perTurn.OrderByDescending(p => p.Value.summons * 3 + p.Value.activations * 2 + p.Value.damage / 500).First();
                int score = (result.Winner == 0 ? 1000 : 0) - result.Turns;
                if (score <= bestScore) continue;
                bestScore = score;
                var (summons, activations, damage) = key.Value;
                best = new Result
                {
                    Replay = replay, Turn = key.Key,
                    Summary = $"Turn {key.Key}: {summons} summon{(summons == 1 ? "" : "s")}, {activations} activation{(activations == 1 ? "" : "s")}"
                              + (damage > 0 ? $", {damage} damage" : "") + (result.Winner == 0 ? $"; {owner.Name} wins on turn {result.Turns}" : ""),
                };
                if (result.Winner == 0) break;
            }
            return best ?? throw new InvalidOperationException("The lesson duel didn't start.");
        }
    }
}
