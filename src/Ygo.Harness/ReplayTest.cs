using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Replays;

namespace Ygo.Harness;

/// <summary>
/// `--replay-test`: plays bot games (planning characters included), saves each as a replay code, reads it back, plays
/// it through replay seats, and checks every core message matches the original, move for move.
/// </summary>
public static class ReplayTest
{
    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, int games, int firstSeed)
    {
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk").OrderBy(f => f).ToArray();
        var styles = Personality.LoadFolder(Path.Combine(root, "personalities")).ToList();
        int matched = 0, failed = 0;
        long codeChars = 0, maxCode = 0;
        // REPLAY_FROM=n starts at the nth game (same decks and seed as in a full run), to look at one game again.
        int from = Environment.GetEnvironmentVariable("REPLAY_FROM") is string fromText ? int.Parse(fromText) : 0;
        for (int g = from; g < games; g++)
        {
            int seed = firstSeed + g;
            var decks = new[] { Deck.LoadYdk(deckFiles[g % deckFiles.Length]), Deck.LoadYdk(deckFiles[(g * 3 + 1) % deckFiles.Length]) };
            var style = new[] { styles[g % styles.Count], styles[(g + 4) % styles.Count] };
            var replay = new Replay
            {
                Settings = new DuelSettings { Seed = (ulong)seed, FirstPlayer = Environment.GetEnvironmentVariable("REPLAY_FIRST") is string f ? int.Parse(f) : g % 2 }, // either side may start (the coin toss)
                Decks = decks.Select(d => d.Clone()).ToArray(),
                Names = style.Select(s => s.Name).ToArray(),
                Characters = style.Select(s => s.Id).ToArray(),
                DeckNames = decks.Select(d => d.Name).ToArray(),
            };
            var recipe = replay.Recipe(cards, scripts);
            var answers = new ResponseLog();

            ulong original;
            DuelResult originalResult;
            var originalMessages = new List<byte[]>();
            var playedMessages = new List<byte[]>();
            using (var duel = recipe.Create())
            {
                var context = new DuelContext(duel, cards);
                IDuelist Bot(int seat)
                {
                    var deckName = decks[seat].Name;
                    return style[seat].PlansAhead
                        ? new SmartDuelist(style[seat].Name, seed * 2 + seat, context, personality: style[seat],
                            playbook: Playbooks.ForDeck(deckName), planner: new TurnPlanner(recipe, answers, style[seat], () => Playbooks.ForDeck(deckName)))
                        : new SmartDuelist(style[seat].Name, seed * 2 + seat, context, personality: style[seat]);
                }
                var runner = new DuelRunner(duel, Bot(0), Bot(1), answers);
                runner.MessageReceived += context.Observe;
                var hash = new Fingerprint();
                runner.MessageReceived += hash.Add;
                runner.MessageReceived += originalMessages.Add;
                originalResult = runner.Run();
                original = hash.Value;
            }
            replay.Responses = answers.Responses.ToList();
            replay.Winner = originalResult.Winner;
            replay.Turns = originalResult.Turns;
            replay.CardCount = cards.All.Count();

            var code = replay.ToCode();
            codeChars += code.Length;
            maxCode = Math.Max(maxCode, code.Length);
            var loaded = Replay.FromCode(code) ?? throw new InvalidOperationException("code did not read back");

            ulong played;
            DuelResult playedResult;
            using (var duel = loaded.Recipe(cards, scripts).Create())
            {
                var feed = new ReplayFeed(loaded.Responses);
                var runner = new DuelRunner(duel, new ReplayDuelist(feed), new ReplayDuelist(feed));
                var hash = new Fingerprint();
                runner.MessageReceived += hash.Add;
                runner.MessageReceived += playedMessages.Add;
                playedResult = runner.Run();
                played = hash.Value;
            }

            bool same = played == original && playedResult.Winner == originalResult.Winner && playedResult.Turns == originalResult.Turns;
            if (same) matched++;
            else
            {
                failed++;
                ShowDifference(originalMessages, playedMessages);
            }

            // Moments: each must happen in the tick right after its position (that's what jumping to it shows).
            var moments = ReplayMoments.Find(loaded, cards, scripts);
            int turnMoments = moments.Count(m => m.Kind == MomentKind.Turn), placed = CheckMoments(loaded, moments, cards, scripts);
            bool momentsOk = turnMoments == playedResult.Turns && placed == moments.Count(m => m.Kind is MomentKind.Turn or MomentKind.Summon);
            if (!momentsOk) failed++;
            Console.WriteLine($"    moments: {moments.Count} ({string.Join(", ", moments.GroupBy(m => m.Kind).Select(g => $"{g.Count()} {g.Key}"))}); "
                              + $"{placed} land on their tick {(momentsOk ? "ok" : "FAIL")}  e.g. {string.Join(" | ", moments.Where(m => m.Kind != MomentKind.Turn).Take(3).Select(m => m.Text))}");
            Console.WriteLine($"  seed {seed}: {loaded.Title}  ({loaded.Responses.Count} answers, code {code.Length} chars)  {(same ? "identical" : $"DIFFERENT (winner {playedResult.Winner} vs {originalResult.Winner}, turns {playedResult.Turns} vs {originalResult.Turns})")}");
        }
        Console.WriteLine($"\nReplays identical: {matched}/{games - from}   average code {codeChars / Math.Max(1, games - from)} chars, longest {maxCode}");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>Plays a replay tick by tick and counts turn and summon moments whose event comes in the very next tick.</summary>
    private static int CheckMoments(Replay replay, List<Moment> moments, ICardDataProvider cards, IScriptProvider scripts)
    {
        using var duel = replay.Recipe(cards, scripts).Create();
        var feed = new ReplayFeed(replay.Responses);
        var runner = new DuelRunner(duel, new ReplayDuelist(feed), new ReplayDuelist(feed));
        var byTick = new Dictionary<int, List<byte>>();
        int ticks = 0;
        bool saw = false;
        runner.MessageReceived += m =>
        {
            saw |= Visibility.IsVisible(m[0]);
            if (!byTick.TryGetValue(ticks, out var list)) byTick[ticks] = list = new List<byte>();
            list.Add(m[0]);
        };
        while (ticks < 20000 && !runner.IsFinished)
        {
            saw = false;
            bool more = true;
            for (int i = 0; i < ReplayMoments.MaxStepsPerTick && !saw; i++)
                if (!runner.Step() || runner.IsWaiting) { more = false; break; }
            ticks++;
            if (!more && (runner.IsFinished || runner.IsWaiting)) break;
        }
        return moments.Count(m => m.Kind switch
        {
            MomentKind.Turn => byTick.TryGetValue(m.Tick, out var l) && l.Contains(Msg.NewTurn),
            MomentKind.Summon => byTick.TryGetValue(m.Tick, out var l) && (l.Contains(Msg.Summoning) || l.Contains(Msg.SpSummoning)),
            _ => false,
        });
    }

    /// <summary>Where two message streams first part: the messages around it, from both.</summary>
    private static void ShowDifference(List<byte[]> original, List<byte[]> played)
    {
        int i = 0;
        while (i < original.Count && i < played.Count && original[i].AsSpan().SequenceEqual(played[i])) i++;
        var names = typeof(Msg).GetFields().Where(f => f.IsLiteral).GroupBy(f => (byte)f.GetRawConstantValue()!).ToDictionary(g => g.Key, g => g.First().Name);
        string Show(List<byte[]> list, int at) => at < list.Count
            ? $"{(names.TryGetValue(list[at][0], out var n) ? n : list[at][0].ToString())} {BitConverter.ToString(list[at], 1, Math.Min(48, list[at].Length - 1))}"
            : "(end)";
        Console.WriteLine($"    first difference at message {i} of {original.Count} / {played.Count}:");
        for (int k = Math.Max(0, i - 3); k <= i + 2; k++)
        {
            Console.WriteLine($"      {k,5} original {Show(original, k)}");
            if (k >= i) Console.WriteLine($"      {k,5} replay   {Show(played, k)}");
        }
    }

    /// <summary>FNV-1a over every message, in order.</summary>
    private sealed class Fingerprint
    {
        public ulong Value { get; private set; } = 14695981039346656037UL;

        public void Add(byte[] message)
        {
            var v = Value;
            foreach (var b in message) { v ^= b; v *= 1099511628211UL; }
            Value = v;
        }
    }
}
