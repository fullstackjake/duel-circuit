using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Protocol;

namespace Ygo.Harness;

/// <summary>
/// A debugging wrapper round a duelist: keeps the last prompts it was asked and its answers, and counts prompts by type,
/// so a duel stopped at the prompt cap shows the prompt the bot kept answering (`ENDLESS_REPLAY`, `--stall-test`).
/// </summary>
public sealed class PromptTrace : IDuelist
{
    private readonly IDuelist _inner;
    private readonly ICardDataProvider _cards;
    private readonly Queue<string> _last = new Queue<string>();
    private readonly int _keep;
    public readonly Dictionary<string, int> ByType = new Dictionary<string, int>();
    public string Seat;
    public Func<string>? Where;

    public PromptTrace(IDuelist inner, ICardDataProvider cards, string seat, int keep = 40)
    {
        _inner = inner; _cards = cards; Seat = seat; _keep = keep;
    }

    public IEnumerable<string> Last => _last;

    /// <summary>A line from the duelist's own log (its decisions), kept with the prompts.</summary>
    public void Note(string line)
    {
        _last.Enqueue($"{Where?.Invoke()} {Seat}   . {line}");
        while (_last.Count > _keep) _last.Dequeue();
    }

    public void OnPhaseChanged() => _inner.OnPhaseChanged();

    public byte[]? Respond(Prompt prompt, int attempt)
    {
        var answer = _inner.Respond(prompt, attempt);
        var type = prompt.GetType().Name;
        ByType[type] = ByType.TryGetValue(type, out var n) ? n + 1 : 1;
        _last.Enqueue($"{Where?.Invoke()} {Seat} {type}{(attempt > 0 ? $" (retry {attempt})" : "")}{Describe(prompt)} -> {(answer == null ? "null" : BitConverter.ToString(answer))}");
        while (_last.Count > _keep) _last.Dequeue();
        return answer;
    }

    private string Name(uint code) => code == 0 ? "?" : _cards.Get(code)?.Name ?? code.ToString();
    private string Card(CardRef c) => $"{Name(c.Code)}@{c.Controller}:{c.Location:x}/{c.Sequence}";
    private string Cards(IEnumerable<CardRef> cs) => string.Join(", ", cs.Select(Card));

    private string Describe(Prompt prompt) => prompt switch
    {
        IdlePrompt p => $": act [{string.Join(", ", p.Activatable.Select(c => $"{Name(c.Card.Code)}/{c.Description}"))}] sum [{Cards(p.Summonable)}] sp [{Cards(p.SpSummonable)}] bp {p.CanBattlePhase} ep {p.CanEndPhase}",
        BattlePrompt p => $": act [{string.Join(", ", p.Activatable.Select(c => $"{Name(c.Card.Code)}/{c.Description}"))}] atk [{Cards(p.Attackers)}] direct [{string.Join(",", p.CanDirectAttack)}] m2 {p.CanMain2} ep {p.CanEndPhase}",
        ChainPrompt p => $": forced {p.Forced} [{string.Join(", ", p.Chains.Select(c => $"{Name(c.Card.Code)}/{c.Description}"))}]",
        SelectCardPrompt p => $": {p.Min}-{p.Max} cancel {p.Cancelable} [{Cards(p.Cards)}]",
        SelectUnselectPrompt p => $": {p.Min}-{p.Max} finish {p.Finishable} cancel {p.Cancelable} sel [{Cards(p.Selectable)}] unsel [{Cards(p.Unselectable)}]",
        TributePrompt p => $": {p.Min}-{p.Max} cancel {p.Cancelable} [{Cards(p.Cards)}]",
        EffectYesNoPrompt p => $": {Name(p.Card.Code)} desc {p.Description}",
        YesNoPrompt p => $": desc {p.Description}",
        OptionPrompt p => $": [{string.Join(", ", p.Options)}]",
        PlacePrompt p => $": count {p.Count} blocked {p.BlockedZones:x8}",
        PositionPrompt p => $": {Name(p.Code)} positions {p.Positions:x}",
        SumPrompt p => $": exact {p.Exact} target {p.Target} {p.Min}-{p.Max} must [{Cards(p.MustCards)}] [{Cards(p.Cards)}] values [{string.Join(",", p.Values)}]",
        SortPrompt p => $": chain {p.IsChain} [{Cards(p.Cards)}]",
        CounterPrompt p => $": type {p.CounterType} count {p.Count} [{Cards(p.Cards)}]",
        _ => "",
    };
}
