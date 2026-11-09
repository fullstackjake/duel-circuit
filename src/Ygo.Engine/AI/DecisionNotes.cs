using System.Collections.Generic;

namespace Ygo.Engine.AI
{
    /// <summary>
    /// One option the bot weighed in a decision, as it scored it (SmartDuelist.Notes): the response, the bot's own words
    /// for it, its heuristic score, and the rule that ruled it out, if one did. Read-only reporting for the replay
    /// analyzer (docs/dev/REPLAY-ANALYZER-SPEC-2026-11-06.md, section 3c): nothing here feeds back into a decision.
    /// </summary>
    public sealed class OptionNote
    {
        public byte[] Response = System.Array.Empty<byte>();
        public string Label = "";
        public double Score;
        /// <summary>Why the option was ruled out (a blunder check, "tried twice", a playbook veto); null if it wasn't.</summary>
        public string? Rejected;
        /// <summary>An attack's target (controller, Monster Zone); null for anything else or a direct attack.</summary>
        public (int controller, uint sequence)? Target;
    }

    /// <summary>One candidate the turn planner played out, with its rollout scores (in the planner's board points).</summary>
    public sealed class PlannerScore
    {
        public byte[] Response = System.Array.Empty<byte>();
        public string Label = "";
        /// <summary>An attack's target; null for a direct attack or anything that isn't an attack.</summary>
        public (int controller, uint sequence)? Target;
        public bool Direct;
        public double Plain;
        /// <summary>The score with the line's first interruptible step negated, when that was played too.</summary>
        public double? Interrupted;
        public double Risk;
        public double Final;
        public bool Usual;
    }

    /// <summary>A planner decision: every candidate's scores, the margin an override needed, and the one chosen (null: kept the usual play).</summary>
    public sealed class PlannerDecision
    {
        public string Kind = "";
        public List<PlannerScore> Scores = new List<PlannerScore>();
        public double Margin;
        public PlannerScore? Chosen;
        /// <summary>A rollout's replay drifted, so the decision went unplanned (the scores so far are kept).</summary>
        public bool Diverged;
    }
}
