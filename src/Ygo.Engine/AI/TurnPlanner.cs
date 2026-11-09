using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.AI
{
    /// <summary>
    /// One-step lookahead for Main Phase 1 and Battle Phase decisions. For each option available right now it
    /// replays the duel in a scratch copy (same seed, same answers so far), takes that option, lets the generic
    /// bot finish the turn, and scores the board when the turn ends. It overrides the bot's usual choice only
    /// when another option is clearly better.
    ///
    /// Risk, using only public information:
    /// <list type="bullet">
    /// <item>Interruptions: if the opponent holds cards, each line is also played with its first interruptible
    /// step (a monster effect, a search, a summon from the Deck) negated, and the two results are blended by
    /// how likely an interruption is (from the opponent's hand size).</item>
    /// <item>Battle traps: each attack is charged a penalty for the chance a set card stops it and destroys the
    /// attacker (from how many cards the opponent has set).</item>
    /// </list>
    /// Simplifications: the opponent otherwise does not act during rollouts, and draw effects in the scratch
    /// copy draw the real next cards (a slight peek at your own deck).
    /// </summary>
    public sealed partial class TurnPlanner
    {
        private readonly DuelRecipe _recipe;
        private readonly ResponseLog _log;
        private readonly Personality _style;
        private readonly Func<Playbook?> _playbook;

        /// <summary>At most this many distinct options are tried per decision.</summary>
        public int MaxCandidates { get; set; } = 12;
        /// <summary>
        /// How much better (in board points; a card in hand is 350, 1 LP is 1) a line must score than the bot's
        /// usual choice before the planner overrides it. Keeps long-term plans (stall, traps) that the one-turn
        /// rollout can't see from being thrown away for marginal gains.
        /// </summary>
        public double OverrideMargin { get; set; } = 400;
        /// <summary>A rollout stops after this many steps even if the turn has not ended.</summary>
        public int RolloutPromptLimit { get; set; } = 400;

        /// <summary>
        /// In rollouts the opponent answers with the cards everyone can see (face-up on the field, in the Graveyard,
        /// banished), played by the generic bot; their hand and set cards stay hidden and are covered by
        /// <see cref="ModelInterruptions"/>. Off: the opponent never responds. Over 1,200 games (ten decks against
        /// Synchron, Eldlich and True Draco) this won 644 against 620, most of it against True Draco's face-up
        /// interruptions, at the same thinking time.
        /// </summary>
        public bool ModelOpponent { get; set; } = true;
        /// <summary>Play each line a second time with its first interruptible step negated.</summary>
        public bool ModelInterruptions { get; set; } = true;
        /// <summary>Also plan attacks (attacker and target) in the Battle Phase.</summary>
        public bool PlanBattle { get; set; } = true;
        /// <summary>
        /// Also plan one-card selections in our own turn (what to search, summon, discard or target): each option is
        /// played out like a Main Phase action. In 960 games against Synchron this won 6 points more than planning
        /// Main Phase actions alone.
        /// </summary>
        public bool PlanSelections { get; set; } = true;
        /// <summary>
        /// Also plan selections of several cards and materials (pick-one-at-a-time selections, Synchro level sums,
        /// tributes, and multi-card picks). Off by default: over 1,120 games on fourteen material-heavy decks it came out
        /// even (697 wins against 692), since those choices rarely change the line. The harness bot expert-multi turns it on.
        /// </summary>
        public bool PlanMultiSelections { get; set; }
        /// <summary>How much better a picked card's line must score than the usual pick (150 beat 400 in testing).</summary>
        public double SelectionMargin { get; set; } = 150;
        /// <summary>Cost of the card spent on an effect that gets negated.</summary>
        public double NegatedCardCost { get; set; } = 600;

        public int Decisions { get; private set; }
        public int Rollouts { get; private set; }
        /// <summary>Rollouts whose replay didn't reproduce the duel (that decision goes unplanned).</summary>
        public int DivergedRollouts { get; private set; }

        /// <summary>Process-wide timing, for reporting how long planning takes per decision.</summary>
        public static long TotalDecisions, TotalMilliseconds, SlowestMilliseconds;

        /// <summary>The planned duelist's removal-timing sense (see SmartDuelist.UseRemovalTiming): off only for the A/B that measures it.</summary>
        public bool RemovalTiming { get; set; } = true;
        /// <summary>The planned duelist's battle memory by card (SmartDuelist.UseCardIdentity): off only for the A/B that measures it.</summary>
        public bool CardIdentity { get; set; } = true;
        /// <summary>The planned duelist's sense for monsters battle can't destroy (SmartDuelist.UseIndestructibleSense): off only for its A/B.</summary>
        public bool IndestructibleSense { get; set; } = true;
        /// <summary>The planned duelist's sense for battle triggers (SmartDuelist.UseBattleTriggerSense): off only for its A/B.</summary>
        public bool BattleTriggerSense { get; set; } = true;
        /// <summary>The planned duelist's Main Phase use of Set Rotation-style Quick-Plays (SmartDuelist.UseDeckSetSense): off only for its A/B.</summary>
        public bool DeckSetSense { get; set; } = true;
        /// <summary>The planned duelist's GY-recovery advantage (SmartDuelist.UseRecoverySense): off only for its A/B.</summary>
        public bool RecoverySense { get; set; } = true;
        /// <summary>The planned duelist's attack-before-optional-Spells sense (SmartDuelist.UseActivationWindowSense): off only for its A/B.</summary>
        public bool ActivationWindowSense { get; set; } = true;
        /// <summary>The planned duelist's self-damage cost check (SmartDuelist.UseSelfDamageSense): off only for its A/B.</summary>
        public bool SelfDamageSense { get; set; } = true;

        public TurnPlanner(DuelRecipe recipe, ResponseLog log, Personality style, Func<Playbook?> playbook)
        {
            _recipe = recipe;
            _log = log;
            _style = style;
            _playbook = playbook;
        }

        /// <summary>What the opponent shows: drives how much the planner fears interruptions and battle traps.</summary>
        public struct Exposure
        {
            public int OpponentHand;
            public int OpponentSetCards;
            /// <summary>The opponent has already interrupted with a hand trap or negation this duel.</summary>
            public bool OpponentShownInterruptions;
        }

        /// <summary>A clearly better Main Phase action than <paramref name="usual"/>, or null to keep it.</summary>
        public byte[]? Choose(IdlePrompt prompt, int me, byte[] usual, Exposure exposure, out string why, int turn = -1)
        {
            if (ComboSearch && turn >= 0)
            {
                var combo = ChooseCombo(prompt, me, usual, turn, out why);
                if (combo != null) return combo;
            }
            var candidates = MainCandidates(prompt);
            if (!candidates.Any(c => c.Response.SequenceEqual(usual))) candidates.Insert(0, new Candidate(usual, "usual"));
            var best = Decide(candidates, prompt, me, c => c.Response.SequenceEqual(usual), exposure, out why, kind: "main");
            return best?.Response;
        }

        /// <summary>
        /// A clearly better Battle Phase action than the usual one, or null to keep it. For an attack,
        /// <paramref name="target"/> is the monster to attack (null for a direct attack).
        /// </summary>
        public byte[]? ChooseAttack(BattlePrompt prompt, int me, byte[] usual, (int controller, uint sequence)? usualTarget,
            Exposure exposure, BoardState board, out (int controller, uint sequence)? target, out string why,
            ICollection<uint>? calledOff = null)
        {
            target = null;
            var candidates = BattleCandidates(prompt, me, exposure, board, calledOff);
            bool IsUsual(Candidate c) => c.Response.SequenceEqual(usual)
                && (c.Follow == null || Equals(c.Follow.Target, usualTarget) || (c.Follow.Direct && usualTarget == null));
            if (!candidates.Any(IsUsual))
                candidates.Insert(0, new Candidate(usual, "usual") { Follow = new FollowUp { Target = usualTarget, Direct = usualTarget == null } });
            var best = Decide(candidates, prompt, me, IsUsual, exposure, out why, kind: "battle");
            if (best == null) return null;
            target = best.Follow?.Target;
            return best.Response;
        }

        /// <summary>
        /// A clearly better answer than <paramref name="usual"/> to a card selection in our own turn (what to search,
        /// summon, discard, target, or use as material), or null to keep the usual answer. The caller lists the
        /// <paramref name="options"/> worth trying; each is played out like a Main Phase action.
        /// </summary>
        public byte[]? ChooseSelection(Prompt prompt, int me, byte[] usual, IEnumerable<(byte[] response, string label)> options,
            bool several, Exposure exposure, out string why)
        {
            why = "";
            if (!PlanSelections || (several && !PlanMultiSelections)) return null;
            var candidates = new List<Candidate> { new Candidate(usual, "usual") };
            foreach (var (response, label) in options)
                if (!candidates.Any(c => c.Response.SequenceEqual(response))) candidates.Add(new Candidate(response, label));
            var best = Decide(candidates, prompt, me, c => c.Response.SequenceEqual(usual), exposure, out why, SelectionMargin, "selection");
            return best?.Response;
        }

        // ------------------------------------------------------------ Decision

        private sealed class Candidate
        {
            public byte[] Response;
            public string Label;
            public FollowUp? Follow;
            /// <summary>Expected loss from things the rollout cannot simulate (battle traps).</summary>
            public double Risk;

            public Candidate(byte[] response, string label)
            {
                Response = response;
                Label = label;
            }
        }

        /// <summary>Answers owed right after a candidate: which monster to attack, or "yes, attack directly".</summary>
        private sealed class FollowUp
        {
            public bool Direct;
            public (int controller, uint sequence)? Target;
        }

        private Candidate? Decide(List<Candidate> candidates, Prompt prompt, int me, Func<Candidate, bool> isUsual,
            Exposure exposure, out string why, double? margin = null, string kind = "")
        {
            why = "";
            if (candidates.Count < 2) return null;
            Decisions++;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var report = Scored != null ? new List<PlannerScore>() : null;
            try
            {
                var scored = ScoreCandidates(candidates, prompt, me, exposure, report);
                if (scored == null) // replay diverged; don't trust any of it
                {
                    Scored?.Invoke(new PlannerDecision { Kind = kind, Scores = report!, Margin = margin ?? OverrideMargin, Diverged = true });
                    return null;
                }
                double bestScore = double.MinValue, usualScore = double.MinValue;
                Candidate? best = null;
                foreach (var (c, score) in scored)
                {
                    if (isUsual(c)) usualScore = score;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }
                var chosen = best == null || isUsual(best) || bestScore < usualScore + (margin ?? OverrideMargin) ? null : best;
                if (report != null)
                {
                    for (int k = 0; k < scored.Count; k++) report[k].Usual = isUsual(scored[k].c);
                    Scored!.Invoke(new PlannerDecision
                    {
                        Kind = kind, Scores = report, Margin = margin ?? OverrideMargin,
                        Chosen = chosen == null ? null : report[scored.FindIndex(x => x.c == chosen)],
                    });
                }
                if (chosen == null) return null;
                why = $"{chosen.Label} (+{bestScore - usualScore:F0} over the usual play)";
                return chosen;
            }
            finally
            {
                var ms = timer.ElapsedMilliseconds;
                System.Threading.Interlocked.Increment(ref TotalDecisions);
                System.Threading.Interlocked.Add(ref TotalMilliseconds, ms);
                if (ms > SlowestMilliseconds) SlowestMilliseconds = ms;
            }
        }

        /// <summary>
        /// Each candidate's score (at most <see cref="MaxCandidates"/>, in order): its rollout, less its risk, blended with
        /// the interrupted rollout by how likely an interruption is. Null if a rollout's replay diverged. With
        /// <paramref name="report"/>, each score is also written down there (for <see cref="Scored"/>).
        /// </summary>
        private List<(Candidate c, double score)>? ScoreCandidates(List<Candidate> candidates, Prompt prompt, int me, Exposure exposure,
            List<PlannerScore>? report)
        {
            // Chance the opponent stops our first interruptible step: more cards in hand, more risk, and
            // much more once they've shown they play hand traps or negations.
            double interruptChance = ModelInterruptions
                ? Math.Min(0.6, 0.12 * exposure.OpponentHand) * (exposure.OpponentShownInterruptions ? 1.0 : 0.35)
                : 0;
            var scored = new List<(Candidate c, double score)>();
            double lethalCharge = double.MaxValue;
            foreach (var c in candidates.Take(MaxCandidates))
            {
                var plain = Rollout(c, prompt, me, blocked: 0, out var firstInterruptible);
                if (plain == null) return null;
                double score = plain.Value - c.Risk;
                double? interruptedScore = null;
                if (interruptChance > 0 && firstInterruptible != 0)
                {
                    var interrupted = Rollout(c, prompt, me, firstInterruptible, out _);
                    if (interrupted == null) return null;
                    interruptedScore = interrupted.Value;
                    score = (1 - interruptChance) * score
                            + interruptChance * (interrupted.Value - c.Risk - NegatedCardCost);
                }
                scored.Add((c, score));
                if (c.Follow?.Direct == true && plain.Value >= WinScore) lethalCharge = Math.Min(lethalCharge, c.Risk);
                report?.Add(new PlannerScore
                {
                    Response = c.Response, Label = c.Label, Target = c.Follow?.Target, Direct = c.Follow?.Direct == true,
                    Plain = plain.Value, Interrupted = interruptedScore, Risk = c.Risk, Final = score,
                });
            }
            // A direct attack wins as played out: stopping carries the same charge (see LethalRiskSense).
            if (LethalRiskSense && prompt is BattlePrompt && lethalCharge < double.MaxValue && lethalCharge > 0)
                for (int k = 0; k < scored.Count; k++)
                {
                    var (c, score) = scored[k];
                    if (!IsStop(c.Response)) continue;
                    scored[k] = (c, score - lethalCharge);
                    if (report != null && report.FirstOrDefault(r => r.Response == c.Response) is PlannerScore entry)
                    {
                        entry.Risk += lethalCharge;
                        entry.Final -= lethalCharge;
                    }
                }
            return scored;
        }

        /// <summary>
        /// Read-only reporting for the replay analyzer (docs/dev/REPLAY-ANALYZER-SPEC-2026-11-06.md, section 3c): every planned
        /// decision's candidates and scores, the margin and the one chosen. Null in play; it never changes a choice.
        /// </summary>
        public Action<PlannerDecision>? Scored { get; set; }

        /// <summary>
        /// The replay analyzer's second look: plays out each of <paramref name="options"/> at <paramref name="prompt"/> the way
        /// <see cref="Decide"/> does (same rollouts, risk and interruption blend; the first option counts as the usual one,
        /// and no margin applies). Main Phase and Battle Phase menus and card selections only. Null if a replay diverged.
        /// Analysis only: no bot calls it, so it changes no play.
        /// </summary>
        public List<PlannerScore>? ScoreOptions(Prompt prompt, int me, IEnumerable<(byte[] response, string label, (int controller, uint sequence)? target, bool direct)> options,
            Exposure exposure, BoardState? board = null)
        {
            double trapChance = Math.Min(0.5, 0.15 * exposure.OpponentSetCards);
            var candidates = new List<Candidate>();
            foreach (var (response, label, target, direct) in options)
            {
                var c = new Candidate(response, label);
                if (prompt is BattlePrompt bp && BitConverter.ToInt32(response, 0) is var v && (BattleAction)(v & 0xffff) == BattleAction.Attack)
                {
                    c.Follow = new FollowUp { Target = target, Direct = direct || target == null };
                    int i = v >> 16;
                    var live = board != null && i < bp.Attackers.Count && bp.Attackers[i].Sequence < board.Players[me].Monsters.Count
                        ? board.Players[me].Monsters[(int)bp.Attackers[i].Sequence] : null;
                    c.Risk = trapChance * ((live?.Attack ?? 0) + 300);
                }
                candidates.Add(c);
            }
            if (candidates.Count == 0) return new List<PlannerScore>();
            var report = new List<PlannerScore>();
            if (ScoreCandidates(candidates, prompt, me, exposure, report) == null) return null;
            report[0].Usual = true;
            return report;
        }

        private List<Candidate> MainCandidates(IdlePrompt p)
        {
            var list = new List<Candidate>();
            var seen = new HashSet<(IdleAction, uint, ulong)>();
            void Add(IdleAction action, int index, uint code, ulong extra, string label)
            {
                if (seen.Add((action, code, extra))) list.Add(new Candidate(Responses.Idle(action, index), label));
            }
            for (int i = 0; i < p.Activatable.Count; i++)
                if (SmartDuelist.Pump(_recipe.Cards.Get(p.Activatable[i].Card.Code)) == null) // ATK pumps wait for the battle they turn
                    Add(IdleAction.Activate, i, p.Activatable[i].Card.Code, p.Activatable[i].Description, "activate");
            for (int i = 0; i < p.SpSummonable.Count; i++)
                Add(IdleAction.SpecialSummon, i, p.SpSummonable[i].Code, 0, "special summon");
            for (int i = 0; i < p.Summonable.Count; i++)
                Add(IdleAction.Summon, i, p.Summonable[i].Code, 0, "normal summon");
            for (int i = 0; i < p.MonsterSettable.Count; i++)
                Add(IdleAction.SetMonster, i, p.MonsterSettable[i].Code, 0, "set monster");
            for (int i = 0; i < p.Repositionable.Count; i++)
                Add(IdleAction.ChangePosition, i, p.Repositionable[i].Code, p.Repositionable[i].Sequence, "change position");
            if (p.CanBattlePhase) Add(IdleAction.ToBattlePhase, 0, 0, 0, "battle");
            else if (p.CanEndPhase) Add(IdleAction.ToEndPhase, 0, 0, 0, "end turn");
            return list;
        }

        /// <summary>
        /// Each attack (by attacker and target) and stopping. Attackers in <paramref name="calledOff"/> are left out: their
        /// attack was called off this turn because no target offered was worth it, and declaring it again would only loop.
        /// </summary>
        private List<Candidate> BattleCandidates(BattlePrompt p, int me, Exposure exposure, BoardState board, ICollection<uint>? calledOff = null)
        {
            var list = new List<Candidate>();
            var targets = board.Players[1 - me].Monsters
                .Select((m, seq) => (m, seq: (uint)seq)).Where(t => t.m != null).ToList();
            // A set card might be Mirror Force or similar: charge each attack for the chance it costs the attacker.
            double trapChance = Math.Min(0.5, 0.15 * exposure.OpponentSetCards);
            for (int i = 0; i < p.Attackers.Count; i++)
            {
                var attacker = p.Attackers[i];
                if (calledOff != null && calledOff.Contains(attacker.Sequence)) continue;
                var live = attacker.Sequence < board.Players[me].Monsters.Count ? board.Players[me].Monsters[(int)attacker.Sequence] : null;
                double risk = trapChance * ((live?.Attack ?? 0) + 300);
                var name = _recipe.Cards.Get(attacker.Code)?.Name ?? "monster";
                if (targets.Count == 0 || p.CanDirectAttack[i])
                    list.Add(new Candidate(Responses.Battle(BattleAction.Attack, i), $"attack directly with {name}")
                        { Follow = new FollowUp { Direct = true }, Risk = risk });
                foreach (var (m, seq) in targets)
                    list.Add(new Candidate(Responses.Battle(BattleAction.Attack, i), $"attack zone {seq + 1} with {name}")
                        { Follow = new FollowUp { Target = (1 - me, seq) }, Risk = risk });
            }
            if (p.CanMain2) list.Add(new Candidate(Responses.Battle(BattleAction.ToMain2), "stop attacking"));
            else if (p.CanEndPhase) list.Add(new Candidate(Responses.Battle(BattleAction.ToEndPhase), "end the turn"));
            // Neither (a forced attack): only the attacks are candidates.
            // Keep the "stop" option even when trimming.
            if (list.Count > MaxCandidates)
            {
                var stop = list[list.Count - 1];
                list = list.Take(MaxCandidates - 1).ToList();
                list.Add(stop);
            }
            return list;
        }

        /// <summary>
        /// When a direct attack's own rollout wins, stopping (Main Phase 2, the End Phase) is charged as much as the cheapest
        /// such attack: a win later in the turn can be stopped by the opponent's hidden cards too, and charging only the
        /// attack handed ties between two winning lines to the later win (three "missed lethal" duels of the 2026-11-06
        /// misplay hunt). The attacks keep their charges, so a weaker attacker still goes first as bait. Off only for its A/B.
        /// </summary>
        public bool LethalRiskSense { get; set; } = true;

        // ------------------------------------------------------------ Rollouts

        /// <summary>
        /// Board score at the end of this turn after taking <paramref name="candidate"/>; null if the replay
        /// diverged. With <paramref name="blocked"/> set, that card's effects are treated as negated.
        /// <paramref name="firstInterruptible"/> reports our first interruptible activation in the line.
        /// </summary>
        private double? Rollout(Candidate candidate, Prompt current, int me, uint blocked, out uint firstInterruptible)
        {
            Rollouts++;
            uint first = 0;
            using var duel = _recipe.Create();
            var ctx = new DuelContext(duel, _recipe.Cards);
            bool deferCandidate = blocked != 0 && IsActivationOf(candidate.Response, current, blocked);
            var feed = new ReplayFeed(_log.Responses, candidate, current, deferCandidate);
            IDuelist policy = new SmartDuelist("plan", 7 + me, ctx, null, _style, _playbook()) { UseRemovalTiming = RemovalTiming, UseCardIdentity = CardIdentity, UseIndestructibleSense = IndestructibleSense, UseBattleTriggerSense = BattleTriggerSense, UseDeckSetSense = DeckSetSense, UseRecoverySense = RecoverySense, UseActivationWindowSense = ActivationWindowSense, UseSelfDamageSense = SelfDamageSense };
            if (blocked != 0) policy = new NegatingPolicy(policy, blocked);
            IDuelist[] seats = new IDuelist[2];
            seats[me] = new FedDuelist(feed, policy, ctx, me);
            IDuelist opponent = ModelOpponent
                ? new PublicResponder(new SmartDuelist("plan-opponent", 8 - me, ctx, null, Personality.Balanced, null), _recipe.Cards)
                : new PassiveDuelist(_recipe.Cards);
            seats[1 - me] = new FedDuelist(feed, opponent, ctx, 1 - me);

            var runner = new DuelRunner(duel, seats[0], seats[1]);
            bool turnOver = false;
            runner.MessageReceived += ctx.Observe;
            runner.MessageReceived += m =>
            {
                if (!feed.Exhausted) return;
                // The planned decision is in our turn, so the next new turn after it means our turn is over.
                if (m[0] == Msg.NewTurn) turnOver = true;
                if (m[0] == Msg.Chaining && first == 0)
                {
                    var r = new MessageReader(m, 1);
                    var code = r.U32();
                    r.LocInfo(code);
                    if (r.U8() == me && IsInterruptible(code)) first = code;
                }
            };

            int steps = 0;
            try
            {
                while (!turnOver && runner.Step())
                {
                    if (feed.Diverged) break;
                    if (feed.Exhausted && ++steps > RolloutPromptLimit * 4) break;
                }
            }
            catch (InvalidOperationException)
            {
                firstInterruptible = first;
                return feed.Exhausted ? -1e7 : (double?)null; // an unanswerable rollout is just a bad line
            }
            firstInterruptible = first;
            if (feed.Diverged || !feed.Exhausted) { DivergedRollouts++; return null; }
            return Score(ctx, duel, me, runner.Result.Winner);
        }

        /// <summary>Effects a hand trap could stop: monster effects, searches, and summons from the Deck.</summary>
        private bool IsInterruptible(uint code)
        {
            var info = _recipe.Cards.Get(code);
            if (info == null) return false;
            if ((info.Type & CardType.Monster) != 0) return true;
            var traits = CardTraits.Of(info);
            return traits.Has(Trait.Search) || traits.Has(Trait.SpecialSummon);
        }

        /// <summary>The score of a rollout we win.</summary>
        private const double WinScore = 1e6;

        /// <summary>Leaving the Battle Phase: Main Phase 2 or the End Phase.</summary>
        private static bool IsStop(byte[] response) =>
            (BattleAction)(BitConverter.ToInt32(response, 0) & 0xffff) is BattleAction.ToMain2 or BattleAction.ToEndPhase;

        private static bool IsActivationOf(byte[] response, Prompt prompt, uint code)
        {
            var value = BitConverter.ToInt32(response, 0);
            int index = value >> 16;
            switch (prompt)
            {
                case IdlePrompt idle when (value & 0xffff) == (int)IdleAction.Activate:
                    return index < idle.Activatable.Count && idle.Activatable[index].Card.Code == code;
                case BattlePrompt battle when (value & 0xffff) == (int)BattleAction.Activate:
                    return index < battle.Activatable.Count && battle.Activatable[index].Card.Code == code;
                default:
                    return false;
            }
        }

        private double Score(DuelContext ctx, Duel duel, int me, int winner)
        {
            if (winner == me) return 1e6;
            if (winner == 1 - me) return -1e6;
            var board = new BoardState();
            board.Refresh(duel);
            // LP comes from the messages the context saw.
            board.Players[0].LifePoints = ctx.Board.Players[0].LifePoints;
            board.Players[1].LifePoints = ctx.Board.Players[1].LifePoints;
            return Side(board.Players[me], true) - Side(board.Players[1 - me], false);
        }

        /// <summary>
        /// What a face-up monster that can interrupt the opponent's turn (a quick negate, banish, destroy or bounce: K9-17
        /// "Ripper", Mirrorjade, Invoked Mechaba) is worth on top of its body. YGO_INTERRUPT_VALUE sets it, for measuring.
        /// </summary>
        public static readonly double InterruptionValue =
            double.TryParse(Environment.GetEnvironmentVariable("YGO_INTERRUPT_VALUE"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 600;

        private static bool HoldsInterruption(CardInfo info) =>
            info.Text.Contains("negate") || (info.Text.Contains("Quick Effect") && (info.Text.Contains("banish") || info.Text.Contains("destroy") || info.Text.Contains("return")));

        private double Side(PlayerBoard p, bool mine)
        {
            double total = 0;
            foreach (var m in p.Monsters)
            {
                if (m == null) continue;
                if (!m.IsFaceUp && !mine) { total += 1200; continue; }
                var info = _recipe.Cards.Get(m.Code);
                double body = m.IsDefense ? Math.Max(m.Attack, m.Defense) * 0.6 : m.Attack;
                if (info != null && info.IsExtraDeckMonster) body += 400;
                if (info != null && m.IsFaceUp && HoldsInterruption(info)) body += InterruptionValue; // holds up an interruption
                total += body;
            }
            total += 350 * p.SpellTraps.Take(5).Count(c => c != null && !c.IsFaceUp);
            total += 200 * p.SpellTraps.Take(6).Count(c => c != null && c.IsFaceUp);
            // A card in hand is potential, worth a bit less than damage already dealt; LP counts in full,
            // so burn and direct damage register as progress.
            total += 350 * p.Hand.Count;
            total += p.LifePoints;
            return total;
        }

        /// <summary>Replays the recorded answers in order, then the candidate, then its follow-up answers.</summary>
        private sealed class ReplayFeed
        {
            private readonly IReadOnlyList<byte[]> _responses;
            private readonly Candidate _candidate;
            private readonly Prompt _expected;
            private readonly bool _defer;
            private int _next;

            public bool Exhausted { get; private set; }
            public bool Diverged { get; private set; }
            public FollowUp? PendingFollowUp { get; set; }

            public ReplayFeed(IReadOnlyList<byte[]> responses, Candidate candidate, Prompt expected, bool deferCandidate)
            {
                _responses = responses;
                _candidate = candidate;
                _expected = expected;
                _defer = deferCandidate;
            }

            /// <summary>The replayed or planned answer; null once the policy should decide.</summary>
            public byte[]? Next(Prompt prompt)
            {
                if (_next < _responses.Count) return _responses[_next++];
                if (Exhausted) return null;
                // This must be the very decision being planned; anything else means the replay drifted.
                if (!Matches(prompt, _expected))
                {
                    Diverged = true;
                    return null;
                }
                Exhausted = true;
                PendingFollowUp = _candidate.Follow;
                // In the "negated" scenario, a candidate that activates the negated card is replaced by the policy's choice.
                return _defer ? null : _candidate.Response;
            }

            private static bool Matches(Prompt p, Prompt expected)
            {
                if (p.Player != expected.Player) return false;
                switch (expected)
                {
                    case IdlePrompt e when p is IdlePrompt i:
                        return i.Activatable.Count == e.Activatable.Count && i.Summonable.Count == e.Summonable.Count
                            && i.SpSummonable.Count == e.SpSummonable.Count && i.SpellSettable.Count == e.SpellSettable.Count;
                    case BattlePrompt e when p is BattlePrompt b:
                        return b.Attackers.Count == e.Attackers.Count && b.Activatable.Count == e.Activatable.Count;
                    case SelectCardPrompt e when p is SelectCardPrompt s:
                        return s.Cards.Count == e.Cards.Count && s.Cards.Select(c => c.Code).SequenceEqual(e.Cards.Select(c => c.Code));
                    case SelectUnselectPrompt e when p is SelectUnselectPrompt u:
                        return u.Unselectable.Count == e.Unselectable.Count && u.Selectable.Select(c => c.Code).SequenceEqual(e.Selectable.Select(c => c.Code));
                    case SumPrompt e when p is SumPrompt m:
                        return m.Cards.Select(c => c.Code).SequenceEqual(e.Cards.Select(c => c.Code));
                    case TributePrompt e when p is TributePrompt t:
                        return t.Cards.Select(c => c.Code).SequenceEqual(e.Cards.Select(c => c.Code));
                    default:
                        return false;
                }
            }
        }

        private sealed class FedDuelist : IDuelist
        {
            private readonly ReplayFeed _feed;
            private readonly IDuelist _policy;
            private readonly DuelContext _ctx;
            private readonly int _seat;

            public FedDuelist(ReplayFeed feed, IDuelist policy, DuelContext ctx, int seat)
            {
                _feed = feed;
                _policy = policy;
                _ctx = ctx;
                _seat = seat;
            }

            public void OnPhaseChanged() => _policy.OnPhaseChanged();

            public byte[]? Respond(Prompt prompt, int attempt)
            {
                if (!_feed.Exhausted)
                {
                    var replayed = _feed.Next(prompt);
                    if (replayed != null) return replayed;
                    if (_feed.Diverged) return Responses.NoChain();
                }
                // Answers that belong to the planned attack.
                var follow = _feed.PendingFollowUp;
                if (follow != null && prompt.Player == _seat && attempt == 0)
                {
                    if (prompt is YesNoPrompt yn && yn.Description == DuelContext.DescDirectAttack)
                    {
                        _feed.PendingFollowUp = follow.Target == null ? null : follow;
                        return Responses.YesNo(follow.Direct);
                    }
                    if (prompt is SelectCardPrompt sc && _ctx.PromptHint[_seat & 1] == DuelContext.HintAttackTarget && follow.Target is var (controller, sequence))
                    {
                        _feed.PendingFollowUp = null;
                        var index = sc.Cards.FindIndex(c => c.Controller == controller && c.Sequence == sequence && c.Location == Location.MZone);
                        if (index >= 0) return Responses.Cards(new[] { index });
                    }
                }
                return _policy.Respond(prompt, attempt);
            }
        }

        /// <summary>Plays as the inner policy, except that one card's effects never resolve (as if negated).</summary>
        private sealed class NegatingPolicy : IDuelist
        {
            private readonly IDuelist _inner;
            private readonly uint _negated;

            public NegatingPolicy(IDuelist inner, uint negated)
            {
                _inner = inner;
                _negated = negated;
            }

            public void OnPhaseChanged() => _inner.OnPhaseChanged();

            public byte[]? Respond(Prompt prompt, int attempt)
            {
                switch (prompt)
                {
                    case IdlePrompt idle when idle.Activatable.Any(o => o.Card.Code == _negated):
                    {
                        var kept = idle.Activatable.Where(o => o.Card.Code != _negated).ToList();
                        var filtered = new IdlePrompt
                        {
                            Player = idle.Player, Summonable = idle.Summonable, SpSummonable = idle.SpSummonable,
                            Repositionable = idle.Repositionable, MonsterSettable = idle.MonsterSettable,
                            SpellSettable = idle.SpellSettable, Activatable = kept, CanBattlePhase = idle.CanBattlePhase,
                            CanEndPhase = idle.CanEndPhase, CanShuffle = idle.CanShuffle,
                        };
                        var r = _inner.Respond(filtered, attempt);
                        if (r == null) return null;
                        var value = BitConverter.ToInt32(r, 0);
                        if ((value & 0xffff) != (int)IdleAction.Activate) return r;
                        return Responses.Idle(IdleAction.Activate, idle.Activatable.IndexOf(kept[value >> 16]));
                    }
                    case ChainPrompt chain when chain.Chains.Any(o => o.Card.Code == _negated):
                    {
                        var kept = chain.Chains.Where(o => o.Card.Code != _negated).ToList();
                        if (kept.Count == 0) return chain.Forced ? Responses.Int32(0) : Responses.NoChain();
                        var filtered = new ChainPrompt { Player = chain.Player, SpeCount = chain.SpeCount, Forced = chain.Forced, Chains = kept };
                        var r = _inner.Respond(filtered, attempt);
                        if (r == null) return null;
                        var index = BitConverter.ToInt32(r, 0);
                        return index < 0 ? r : Responses.Int32(chain.Chains.IndexOf(kept[index]));
                    }
                    case EffectYesNoPrompt yn when yn.Card.Code == _negated:
                        return Responses.YesNo(false);
                    default:
                        return _inner.Respond(prompt, attempt);
                }
            }
        }
    }

    /// <summary>The opponent in a planning rollout: never interferes, answers forced choices at random.</summary>
    /// <summary>
    /// The opponent in a rollout: plays like the inner bot, but only activates cards that are public information
    /// (face-up on the field, in the Graveyard, or banished), so the planner never peeks at their hand or set cards.
    /// </summary>
    public sealed class PublicResponder : IDuelist
    {
        private readonly IDuelist _inner;
        private readonly PassiveDuelist _passive;

        public PublicResponder(IDuelist inner, ICardDataProvider cards)
        {
            _inner = inner;
            _passive = new PassiveDuelist(cards);
        }

        public void OnPhaseChanged() => _inner.OnPhaseChanged();

        /// <summary>Chain activations made in rollouts (all threads), for testing.</summary>
        public static long Activations;

        public static bool IsPublic(CardRef card) =>
            (card.Location & (Location.Grave | Location.Removed)) != 0
            || ((card.Location & (Location.MZone | Location.SZone)) != 0
                && (card.Position & (Ygo.Engine.Position.FaceUpAttack | Ygo.Engine.Position.FaceUpDefense)) != 0);

        public byte[]? Respond(Prompt prompt, int attempt)
        {
            switch (prompt)
            {
                case ChainPrompt p when !p.Forced:
                    var open = Enumerable.Range(0, p.Chains.Count).Where(i => IsPublic(p.Chains[i].Card)).ToList();
                    if (open.Count == 0) return Responses.NoChain();
                    var visible = new ChainPrompt { Player = p.Player, SpeCount = p.SpeCount, Chains = open.Select(i => p.Chains[i]).ToList() };
                    var answer = _inner.Respond(visible, attempt);
                    if (answer == null || answer.Length < 4) return Responses.NoChain();
                    var pick = BitConverter.ToInt32(answer, 0);
                    if (pick < 0 || pick >= open.Count) return Responses.NoChain();
                    System.Threading.Interlocked.Increment(ref Activations);
                    return Responses.Int32(open[pick]);
                case EffectYesNoPrompt p when !IsPublic(p.Card):
                    return Responses.YesNo(false);
                case IdlePrompt _:
                case BattlePrompt _:
                    return _passive.Respond(prompt, attempt);
                default:
                    return _inner.Respond(prompt, attempt);
            }
        }
    }

    public sealed class PassiveDuelist : IDuelist
    {
        private readonly RandomDuelist _fallback;

        public PassiveDuelist(ICardDataProvider cards) => _fallback = new RandomDuelist("passive", 1, cards);

        public void OnPhaseChanged() { }

        public byte[]? Respond(Prompt prompt, int attempt)
        {
            if (attempt > 0) return _fallback.Respond(prompt, attempt);
            return prompt switch
            {
                ChainPrompt p => p.Forced && p.Chains.Count > 0 ? Responses.Int32(0) : Responses.NoChain(),
                EffectYesNoPrompt _ => Responses.YesNo(false),
                YesNoPrompt _ => Responses.YesNo(false),
                IdlePrompt p => Responses.Idle(p.CanEndPhase ? IdleAction.ToEndPhase : IdleAction.ToBattlePhase),
                BattlePrompt p => Responses.LeaveBattle(p),
                _ => _fallback.Respond(prompt, 0),
            };
        }
    }
}
