using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.AI
{
    /// <summary>
    /// Combo search: a beam search over sequences of Main Phase 1 actions, for lines the one-step lookahead can't see
    /// (spend a card now for a payoff three plays later). Each candidate line is played in a scratch copy of the duel:
    /// the line's actions at each of our Main Phase decisions, the bot for everything else (targets, materials) and for the
    /// rest of the turn, then the board is scored at the end of the turn. The best few lines at each depth are extended.
    /// The winning line is then followed step by step; if the duel goes off script (the opponent responds), it searches again.
    /// </summary>
    public sealed partial class TurnPlanner
    {
        /// <summary>
        /// Search multi-step lines at our own Main Phase 1 decisions. Off by default: in 40-game tests on six combo decks it
        /// came out even with the one-step planner (within a few wins either way) at twice the thinking time, because
        /// the bot inside each line still picks targets and materials the ordinary way. Harness bot: expert-combo.
        /// </summary>
        public bool ComboSearch { get; set; }
        /// <summary>Actions per line (the bot finishes the turn after that).</summary>
        public int SearchDepth { get; set; } = 4;
        /// <summary>Lines kept and extended at each depth.</summary>
        public int BeamWidth { get; set; } = 3;
        /// <summary>Options tried at each decision of a line.</summary>
        public int BranchLimit { get; set; } = 6;
        /// <summary>Time budget for one search, in milliseconds.</summary>
        public int SearchBudgetMs { get; set; } = 900;

        public static long Searches, SearchRollouts, SearchMilliseconds;

        private sealed class Line
        {
            public List<byte[]> Path = new List<byte[]>();
            /// <summary>Signature of the decision each step of the path answers.</summary>
            public List<string> Signatures = new List<string>();
            public double Score;
            /// <summary>The next Main Phase decision after the path (null once the turn moves on).</summary>
            public IdlePrompt? Next;
            public string? NextSignature;
            public string Label = "";
        }

        private List<byte[]>? _plan;
        private List<string>? _planSignatures;
        private int _planStep;
        private int _planTurn = -1;

        /// <summary>
        /// The next action of the best combo line from here, or null to leave the decision to the one-step planner.
        /// </summary>
        private byte[]? ChooseCombo(IdlePrompt prompt, int me, byte[] usual, int turn, out string why)
        {
            why = "";
            var signature = Signature(prompt);
            // Following a line found earlier this turn, as long as the duel is where the search said it would be.
            if (_plan != null && _planTurn == turn && _planStep < _plan.Count && _planSignatures![_planStep] == signature)
            {
                why = $"combo line, step {_planStep + 1} of {_plan.Count}";
                return _plan[_planStep++];
            }
            _plan = null;

            var timer = Stopwatch.StartNew();
            Searches++;
            try
            {
                var candidates = MainCandidates(prompt);
                if (candidates.Count < 2) return null;
                if (!candidates.Any(c => c.Response.SequenceEqual(usual))) candidates.Insert(0, new Candidate(usual, "usual"));

                // Depth 1: every option (the usual one first), each finished by the bot.
                var all = new List<Line>();
                Line? usualLine = null;
                foreach (var c in candidates.Take(Math.Max(BranchLimit, 2)))
                {
                    var line = Play(new Line { Path = { c.Response }, Signatures = { signature }, Label = Describe(c, prompt) }, me);
                    if (line == null) return null; // the replay didn't reproduce the duel: don't trust any of it
                    all.Add(line);
                    if (c.Response.SequenceEqual(usual)) usualLine = line;
                }
                var frontier = all.OrderByDescending(l => l.Score).Take(BeamWidth).ToList();

                for (int depth = 2; depth <= SearchDepth && timer.ElapsedMilliseconds < SearchBudgetMs; depth++)
                {
                    var next = new List<Line>();
                    foreach (var parent in frontier)
                    {
                        if (parent.Next == null) continue;
                        foreach (var c in MainCandidates(parent.Next).Take(BranchLimit))
                        {
                            if (timer.ElapsedMilliseconds >= SearchBudgetMs) break;
                            var child = new Line
                            {
                                Path = parent.Path.Append(c.Response).ToList(),
                                Signatures = parent.Signatures.Append(parent.NextSignature!).ToList(),
                                Label = parent.Label + ", " + Describe(c, parent.Next),
                            };
                            var played = Play(child, me);
                            if (played == null) continue;
                            next.Add(played);
                            all.Add(played);
                        }
                    }
                    if (next.Count == 0) break;
                    frontier = next.OrderByDescending(l => l.Score).Take(BeamWidth).ToList();
                }

                double usualScore = usualLine?.Score ?? double.MinValue;
                // A line only replaces the usual play when it's clearly better; otherwise the best line that starts
                // with the usual play is kept, so the next few decisions don't need a search of their own.
                var best = all.OrderByDescending(l => l.Score).ThenBy(l => l.Path.Count).First();
                bool overrides = best.Path.Count >= 2 && best.Score >= usualScore + OverrideMargin
                                 && !best.Path[0].SequenceEqual(usual);
                var keep = overrides ? best
                    : all.Where(l => l.Path[0].SequenceEqual(usual)).OrderByDescending(l => l.Score).ThenBy(l => l.Path.Count).FirstOrDefault();
                if (keep != null && keep.Path.Count >= 2)
                {
                    _plan = keep.Path;
                    _planSignatures = keep.Signatures;
                    _planStep = 1;
                    _planTurn = turn;
                }
                if (!overrides) return null;
                why = $"combo line: {best.Label} (+{best.Score - usualScore:F0})";
                return best.Path[0];
            }
            finally
            {
                SearchMilliseconds += timer.ElapsedMilliseconds;
            }
        }

        /// <summary>Plays a line in a scratch duel; scores the end of the turn and notes the decision after the path.</summary>
        private Line? Play(Line line, int me)
        {
            SearchRollouts++;
            Rollouts++;
            using var duel = _recipe.Create();
            var ctx = new DuelContext(duel, _recipe.Cards);
            var feed = new PathFeed(_log.Responses, line.Path, me, ctx);
            IDuelist policy = new SmartDuelist("plan", 7 + me, ctx, null, _style, _playbook()) { UseRemovalTiming = RemovalTiming, UseCardIdentity = CardIdentity, UseIndestructibleSense = IndestructibleSense, UseBattleTriggerSense = BattleTriggerSense, UseDeckSetSense = DeckSetSense, UseRecoverySense = RecoverySense, UseActivationWindowSense = ActivationWindowSense, UseSelfDamageSense = SelfDamageSense };
            var seats = new IDuelist[2];
            seats[me] = new PathDuelist(feed, policy, me);
            seats[1 - me] = new PathDuelist(feed, new PassiveDuelist(_recipe.Cards), 1 - me);
            var runner = new DuelRunner(duel, seats[0], seats[1]);
            bool turnOver = false;
            runner.MessageReceived += ctx.Observe;
            runner.MessageReceived += m =>
            {
                if (feed.ReplayDone && m[0] == Msg.NewTurn) turnOver = true;
            };
            int steps = 0;
            try
            {
                while (!turnOver && runner.Step())
                {
                    if (feed.Diverged) return null;
                    if (feed.ReplayDone && ++steps > RolloutPromptLimit * 4) break;
                }
            }
            catch (InvalidOperationException)
            {
                line.Score = -1e7;
                return line;
            }
            if (feed.Diverged || !feed.PathDone) return feed.Diverged ? null : Unreached(line);
            line.Score = Score(ctx, duel, me, runner.Result.Winner);
            line.Next = feed.NextDecision;
            line.NextSignature = feed.NextDecision == null ? null : Signature(feed.NextDecision);
            return line;
        }

        /// <summary>The turn ended before the line's later steps came up: it isn't a real line.</summary>
        private static Line? Unreached(Line line) => null;

        private static string Signature(IdlePrompt p) =>
            string.Join("|",
                string.Join(",", p.Activatable.Select(o => $"{o.Card.Code}:{o.Description}")),
                string.Join(",", p.Summonable.Select(c => c.Code)),
                string.Join(",", p.SpSummonable.Select(c => c.Code)),
                string.Join(",", p.MonsterSettable.Select(c => c.Code)),
                string.Join(",", p.SpellSettable.Select(c => c.Code)),
                p.CanBattlePhase ? "B" : "", p.CanEndPhase ? "E" : "");

        private string Describe(Candidate c, IdlePrompt p)
        {
            var value = BitConverter.ToInt32(c.Response, 0);
            int index = value >> 16;
            string Name(uint code) => _recipe.Cards.Get(code)?.Name ?? code.ToString();
            return (IdleAction)(value & 0xffff) switch
            {
                IdleAction.Activate when index < p.Activatable.Count => "activate " + Name(p.Activatable[index].Card.Code),
                IdleAction.SpecialSummon when index < p.SpSummonable.Count => "summon " + Name(p.SpSummonable[index].Code),
                IdleAction.Summon when index < p.Summonable.Count => "Normal Summon " + Name(p.Summonable[index].Code),
                IdleAction.SetMonster when index < p.MonsterSettable.Count => "set " + Name(p.MonsterSettable[index].Code),
                IdleAction.ToBattlePhase => "battle",
                IdleAction.ToEndPhase => "end",
                _ => c.Label,
            };
        }

        /// <summary>The duel so far, then the line's actions at our Main Phase 1 decisions this turn.</summary>
        private sealed class PathFeed
        {
            private readonly IReadOnlyList<byte[]> _responses;
            private readonly List<byte[]> _path;
            private readonly int _me;
            private readonly DuelContext _ctx;
            private int _next, _step, _turn = -1;

            public bool ReplayDone => _next >= _responses.Count;
            public bool PathDone => _step >= _path.Count;
            public bool Diverged { get; private set; }
            /// <summary>Our first Main Phase 1 decision after the whole path was played.</summary>
            public IdlePrompt? NextDecision { get; private set; }

            public PathFeed(IReadOnlyList<byte[]> responses, List<byte[]> path, int me, DuelContext ctx)
            {
                _responses = responses;
                _path = path;
                _me = me;
                _ctx = ctx;
            }

            public byte[]? Next(Prompt prompt, int seat)
            {
                if (_next < _responses.Count) return _responses[_next++];
                if (_turn < 0) _turn = _ctx.Board.Turn;
                bool ourDecision = seat == _me && prompt is IdlePrompt && _ctx.Board.Turn == _turn
                                   && _ctx.Board.TurnPlayer == _me && _ctx.Board.Phase == Phase.Main1;
                if (!ourDecision) return null;
                if (_step < _path.Count)
                {
                    var choice = _path[_step++];
                    var idle = (IdlePrompt)prompt;
                    // The choice must still be on the menu (indexes into the option lists).
                    var value = BitConverter.ToInt32(choice, 0);
                    int index = value >> 16;
                    bool valid = (IdleAction)(value & 0xffff) switch
                    {
                        IdleAction.Activate => index < idle.Activatable.Count,
                        IdleAction.SpecialSummon => index < idle.SpSummonable.Count,
                        IdleAction.Summon => index < idle.Summonable.Count,
                        IdleAction.SetMonster => index < idle.MonsterSettable.Count,
                        IdleAction.SetSpellTrap => index < idle.SpellSettable.Count,
                        IdleAction.ChangePosition => index < idle.Repositionable.Count,
                        IdleAction.ToBattlePhase => idle.CanBattlePhase,
                        IdleAction.ToEndPhase => idle.CanEndPhase,
                        _ => true,
                    };
                    if (!valid) { Diverged = true; return null; }
                    return choice;
                }
                if (NextDecision == null) NextDecision = (IdlePrompt)prompt;
                return null;
            }
        }

        private sealed class PathDuelist : IDuelist
        {
            private readonly PathFeed _feed;
            private readonly IDuelist _policy;
            private readonly int _seat;

            public PathDuelist(PathFeed feed, IDuelist policy, int seat)
            {
                _feed = feed;
                _policy = policy;
                _seat = seat;
            }

            public void OnPhaseChanged() => _policy.OnPhaseChanged();

            public byte[]? Respond(Prompt prompt, int attempt)
            {
                if (attempt == 0)
                {
                    var fed = _feed.Next(prompt, _seat);
                    if (fed != null) return fed;
                    if (_feed.Diverged) return Responses.NoChain();
                }
                return _policy.Respond(prompt, attempt);
            }
        }
    }
}
