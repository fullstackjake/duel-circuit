using Ygo.Engine.AI;
using Ygo.Engine.Interactive;
using Ygo.Engine.Protocol;

namespace Ygo.Harness;

/// <summary>
/// Stands in for a person: answers every prompt only through the ChoiceSet the UI would show,
/// picking randomly. Exercises the whole human-play path without Unity.
/// </summary>
public sealed class ScriptedClicker : IDuelist
{
    private readonly HumanDuelist _human;
    private readonly Random _rng;
    private readonly UiTestStats _stats;

    public ScriptedClicker(HumanDuelist human, int seed, UiTestStats stats)
    {
        _human = human;
        _rng = new Random(seed);
        _stats = stats;
    }

    public void OnPhaseChanged() => _human.OnPhaseChanged();

    public byte[]? Respond(Prompt prompt, int attempt)
    {
        int skippedBefore = _human.Presenter.SkippedResponses;
        var answer = _human.Respond(prompt, attempt);
        _stats.SkippedResponses += _human.Presenter.SkippedResponses - skippedBefore;
        if (answer != null) return answer; // answered automatically
        var set = _human.Current ?? throw new InvalidOperationException("Human player is waiting without choices");
        _stats.Record(set, attempt);
        _human.Submit(Pick(set));
        return _human.Respond(prompt, attempt) ?? throw new InvalidOperationException("Submitted answer was not delivered");
    }

    private byte[] Pick(ChoiceSet set)
    {
        if (set.CanCancel && (set.Choices.Count == 0 || _rng.NextDouble() < 0.3)) return set.Cancel();
        switch (set.Mode)
        {
            case ChoiceMode.Multi:
            {
                int count = _rng.Next(set.Min, Math.Min(set.Max, set.Choices.Count) + 1);
                return set.Choose(Enumerable.Range(0, set.Choices.Count).OrderBy(_ => _rng.Next()).Take(count).ToList());
            }
            case ChoiceMode.Incremental:
            {
                var open = Enumerable.Range(0, set.Choices.Count).Where(i => !set.Choices[i].AlreadySelected).ToList();
                if (set.CanFinish && (open.Count == 0 || _rng.NextDouble() < 0.5)) return set.Finish();
                if (open.Count == 0) return set.Choose(0);
                return set.Choose(open[_rng.Next(open.Count)]);
            }
            default:
                return set.Choose(_rng.Next(set.Choices.Count));
        }
    }
}

public sealed class UiTestStats
{
    private readonly Dictionary<string, (int shown, int rejected)> _byType = new();
    public int Unreadable;
    /// <summary>Chances to respond the response mode let pass without asking.</summary>
    public int SkippedResponses;
    /// <summary>Selection titles that say what they're for.</summary>
    public List<string> PurposeExamples = new();
    public List<string> UnreadableExamples = new();

    public void Record(ChoiceSet set, int attempt)
    {
        var type = set.Prompt.GetType().Name.Replace("Prompt", "");
        if ((set.Title.Contains("(to activate") || set.Title.EndsWith("'s effect)")) && PurposeExamples.Count < 6 && !PurposeExamples.Contains(set.Title))
            PurposeExamples.Add(set.Title);
        var (shown, rejected) = _byType.TryGetValue(type, out var v) ? v : (0, 0);
        _byType[type] = attempt == 0 ? (shown + 1, rejected) : (shown, rejected + 1);

        // Labels the UI would show that carry no real information.
        foreach (var text in set.Choices.Select(c => c.Label).Append(set.Title))
        {
            if (text.StartsWith("Option ") || text == "Confirm?" || text.Contains("card #"))
            {
                Unreadable++;
                if (UnreadableExamples.Count < 8) UnreadableExamples.Add($"{type}: {set.Title} / {text}");
            }
        }
    }

    public void Print()
    {
        Console.WriteLine($"\n{"prompt type",-18} {"shown",8} {"rejected",9}");
        foreach (var (type, (shown, rejected)) in _byType.OrderByDescending(kv => kv.Value.shown))
            Console.WriteLine($"{type,-18} {shown,8} {rejected,9}");
        Console.WriteLine($"chances to respond passed without asking: {SkippedResponses}");
        foreach (var e in PurposeExamples) Console.WriteLine($"  says what it's for: {e}");
        Console.WriteLine($"unreadable labels: {Unreadable}");
        foreach (var e in UnreadableExamples) Console.WriteLine($"  e.g. {e}");
    }
}
