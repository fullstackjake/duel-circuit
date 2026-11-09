using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Interactive;
using Ygo.Engine.Protocol;

namespace Ygo.Harness;

/// <summary>
/// `--lesson-test`: plays Gus's chain lessons through the human-play prompts (with the response modes) as a student
/// who does it right and one who does it wrong, and checks each lesson's judge passes and fails them. Then the CPU as
/// the student (as in QA play), which should pass the first two.
/// </summary>
public static class LessonTest
{
    /// <summary>A student who clicks the named cards when the prompts offer them, and otherwise ends the turn or passes.</summary>
    private sealed class Student : IDuelist
    {
        private readonly HumanDuelist _human;
        private readonly HashSet<uint> _activate, _chain, _targets;
        private readonly HashSet<uint> _used = new();
        public int Asked;

        private readonly DuelContext _ctx;
        private readonly Case _case;
        private ushort _phase;
        private int _turnPlayer;

        public Student(HumanDuelist human, DuelContext ctx, Case c, uint[] activate, uint[] chain, uint[] targets)
        {
            _human = human;
            _ctx = ctx;
            _case = c;
            _activate = activate.ToHashSet();
            _chain = chain.ToHashSet();
            _targets = targets.ToHashSet();
        }

        public void OnPhaseChanged() => _human.OnPhaseChanged();

        public void Observe(byte[] message)
        {
            if (message[0] == Msg.NewPhase) _phase = new MessageReader(message, 1).U16();
            if (message[0] == Msg.NewTurn) _turnPlayer = message[1];
        }

        public byte[]? Respond(Prompt prompt, int attempt)
        {
            var answer = _human.Respond(prompt, attempt);
            if (answer != null) return answer;
            var set = _human.Current ?? throw new InvalidOperationException("waiting without choices");
            _human.Submit(Pick(set));
            return _human.Respond(prompt, attempt);
        }

        private byte[] Pick(ChoiceSet set)
        {
            int Find(Func<Choice, bool> want) => set.Choices.FindIndex(c => want(c));
            if (set.IsChain)
            {
                Asked++;
                bool now = (_case.InPhase == 0 || (_phase == _case.InPhase && _turnPlayer == 1)) && (!_case.InResponse || _ctx.Chain.Count > 0);
                int i = now ? Find(c => c.Card is { } card && _chain.Contains(card.Code) && !_used.Contains(card.Code)) : -1;
                if (i >= 0) { _used.Add(set.Choices[i].Card!.Value.Code); return set.Choose(i); }
                return set.CanCancel ? set.Cancel() : set.Choose(0);
            }
            if (set.Prompt is IdlePrompt)
            {
                int i = Find(c => c.Card is { } card && _activate.Contains(card.Code) && !_used.Contains(card.Code) && c.Label.StartsWith("Activate"));
                if (i >= 0) { _used.Add(set.Choices[i].Card!.Value.Code); return set.Choose(i); }
                return set.Choose(Math.Max(0, Find(c => c.Label == "End your turn")));
            }
            if (set.Prompt is BattlePrompt)
                return set.Choose(Math.Max(0, Find(c => c.Label.Contains("End"))));
            int target = Find(c => c.Card is { } card && _targets.Contains(card.Code));
            switch (set.Mode)
            {
                case ChoiceMode.Multi:
                    return set.Choose(Enumerable.Range(0, set.Min).Select(n => n == 0 && target >= 0 ? target : n).Distinct().ToList());
                case ChoiceMode.Incremental:
                    if (set.CanFinish) return set.Finish();
                    return set.Choose(Math.Max(0, target));
                default:
                    return set.Choose(Math.Max(0, target));
            }
        }
    }

    /// <summary>
    /// A first-duel student: on its own turns, the first choice whose label starts with one of <paramref name="plan"/>'s
    /// entries (in the plan's order; "3:..." from turn 3 on), else the next phase; chains the listed cards when asked;
    /// targets the listed cards.
    /// </summary>
    private sealed class PlanStudent : IDuelist
    {
        private readonly HumanDuelist _human;
        private readonly string[] _plan;
        private readonly HashSet<uint> _chain, _targets;
        public int Asked;
        public PlanStudent(HumanDuelist human, string[] plan, uint[] chain, uint[] targets)
        {
            _human = human; _plan = plan; _chain = chain.ToHashSet(); _targets = targets.ToHashSet();
        }
        public int Turn;
        public void OnPhaseChanged() => _human.OnPhaseChanged();
        public byte[]? Respond(Prompt prompt, int attempt)
        {
            var answer = _human.Respond(prompt, attempt);
            if (answer != null) return answer;
            _human.Submit(Pick(_human.Current ?? throw new InvalidOperationException("waiting without choices")));
            return _human.Respond(prompt, attempt);
        }
        private byte[] Pick(ChoiceSet set)
        {
            int Find(Func<Choice, bool> want) => set.Choices.FindIndex(c => want(c));
            if (set.IsChain)
            {
                Asked++;
                int i = Find(c => c.Card is { } card && _chain.Contains(card.Code));
                return i >= 0 ? set.Choose(i) : set.CanCancel ? set.Cancel() : set.Choose(0);
            }
            if (set.Prompt is IdlePrompt or BattlePrompt)
            {
                foreach (var entry in _plan)
                {
                    var want = entry.Length > 2 && entry[1] == ':' ? entry.Substring(2) : entry;
                    if (want != entry && Turn < entry[0] - '0') continue;
                    int i = Find(c => c.Label.StartsWith(want));
                    if (i >= 0) return set.Choose(i);
                }
                int next = Find(c => c.Label == "Go to the Battle Phase");
                return set.Choose(Math.Max(0, next >= 0 ? next : Find(c => c.Label == "End your turn")));
            }
            int target = Find(c => c.Card is { } card && _targets.Contains(card.Code));
            return set.Mode switch
            {
                ChoiceMode.Multi => set.Choose(Enumerable.Range(0, set.Min).Select(n => n == 0 && target >= 0 ? target : n).Distinct().ToList()),
                ChoiceMode.Incremental when set.CanFinish => set.Finish(),
                _ => set.Choose(Math.Max(0, target)),
            };
        }
    }

    /// <summary>
    /// Gus's guided first duel: a student who follows the hints wins it (asked about Sakuretsu Armor on Chain Auto, every
    /// hint pointing somewhere sensible); one who lets Battle Ox's attack through fails with the reason; the CPU as the
    /// student, for information.
    /// </summary>
    private static int FirstDuel(ICardDataProvider cards, IScriptProvider scripts)
    {
        var lesson = ChainLessons.FirstDuel;
        const uint celtic = ChainLessons.CelticGuardian, armor = ChainLessons.SakuretsuArmor, axe = ChainLessons.AxeOfDespair;
        var right = new[] { "Normal Summon Celtic Guardian", "Set Sakuretsu Armor", "3:Activate Axe of Despair", "Attack with Celtic Guardian" };
        var cases = new (string name, string[] plan, uint[] chain, LessonOutcome expected, string hintHas)[]
        {
            ("follows the hints", right, new[] { armor }, LessonOutcome.Passed, "won"),
            ("lets Battle Ox's attack through", right, Array.Empty<uint>(), LessonOutcome.Failed, "Sakuretsu Armor"),
            ("never attacks", right.Take(3).ToArray(), new[] { armor }, LessonOutcome.Failed, "attack directly"),
        };
        int bad = 0;
        foreach (var c in cases)
        {
            using var duel = lesson.Recipe(cards, scripts).Create();
            var context = new DuelContext(duel, cards);
            var human = new HumanDuelist(context);
            human.Presenter.Mode = ResponseMode.Auto;
            var student = new PlanStudent(human, c.plan, c.chain, new[] { celtic });
            var runner = new DuelRunner(duel, student, lesson.Gus(cards));
            var judge = lesson.Judge();
            var hints = new List<string>();
            void Note() { var h = $"{judge.Hint} -> [{judge.Pointer}]"; if (hints.LastOrDefault() != h) hints.Add(h); }
            Note();
            runner.MessageReceived += context.Observe;
            runner.MessageReceived += m => { if (m[0] == Msg.NewTurn) student.Turn++; judge.Handle(m); Note(); };
            for (int steps = 0; steps < 5000 && judge.Outcome == LessonOutcome.Playing && runner.Step(); steps++) { }
            bool ok = judge.Outcome == c.expected && judge.Hint.Contains(c.hintHas) && student.Asked > 0;
            if (c.expected == LessonOutcome.Passed)
            {
                // Each step was hinted, with the pointer on its card or control.
                foreach (var want in new[] { "hand:" + celtic, "hand:" + armor, "phase", "hand:" + axe, "field:" + celtic })
                    if (!hints.Any(h => h.EndsWith($"[{want}]"))) { ok = false; Console.WriteLine($"       no hint pointed at {want}"); }
                ok &= hints.Any(h => h.StartsWith("Gus attacks!"));
            }
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {lesson.Title}: a student who {c.name} -> {judge.Outcome} (turn {judge.Turn}, asked {student.Asked}x): {judge.Hint}");
            if (!ok || c.expected == LessonOutcome.Passed) foreach (var h in hints) Console.WriteLine("       hint: " + h);
        }
        {
            // The CPU as the student, as QA play's stand-in plays it.
            var recipe = lesson.Recipe(cards, scripts);
            using var duel = recipe.Create();
            var context = new DuelContext(duel, cards);
            var human = new HumanDuelist(context);
            var style = new Personality { Name = "Student" };
            var answers = new ResponseLog();
            var bot = new SmartDuelist("You", 5, context, personality: style, planner: new TurnPlanner(recipe, answers, style, () => null));
            var runner = new DuelRunner(duel, new BotStudent(human, bot), lesson.Gus(cards), answers);
            var judge = lesson.Judge();
            runner.MessageReceived += context.Observe;
            runner.MessageReceived += judge.Handle;
            runner.MessageReceived += m => { if (m[0] == Msg.NewTurn || m[0] == Msg.NewPhase) bot.OnPhaseChanged(); };
            for (int steps = 0; steps < 5000 && judge.Outcome == LessonOutcome.Playing && runner.Step(); steps++) { }
            Console.WriteLine($"info {lesson.Title}: the CPU as the student -> {judge.Outcome} (turn {judge.Turn}): {judge.Hint}");
        }
        return bad;
    }

    /// <summary>The CPU answering the prompts a player is shown (the stand-in in QA play).</summary>
    private sealed class BotStudent : IDuelist
    {
        private readonly HumanDuelist _human;
        private readonly SmartDuelist _bot;
        public BotStudent(HumanDuelist human, SmartDuelist bot) { _human = human; _bot = bot; }
        public void OnPhaseChanged() => _human.OnPhaseChanged();
        public void Observe(byte[] message) { }
        public byte[]? Respond(Prompt prompt, int attempt)
        {
            var answer = _human.Respond(prompt, attempt);
            if (answer != null) return answer;
            _human.Submit(_bot.Respond(prompt, attempt) ?? throw new InvalidOperationException("the bot had no answer"));
            return _human.Respond(prompt, attempt);
        }
    }

    /// <summary>A student's plan. InPhase: chain only in that phase of the opponent's turn (0: any time); InResponse: chain only to a card already on the chain.</summary>
    private sealed record Case(string Lesson, string Name, ResponseMode Mode, bool Hold, uint[] Activate, uint[] Chain, uint[] Targets, LessonOutcome Expected,
        ushort InPhase = 0, bool InResponse = false);

    public static int Run(ICardDataProvider cards, IScriptProvider scripts)
    {
        const uint rota = ChainLessons.ReinforcementOfTheArmy, cbtg = ChainLessons.CalledByTheGrave, ash = ChainLessons.AshBlossom,
            jar = ChainLessons.JarOfGreed,
            hole = ChainLessons.DarkHole, imt = ChainLessons.InterdimensionalMatterTransporter, bewd = ChainLessons.BlueEyes;
        var cases = new[]
        {
            new Case("last-in-first-out", "answers Ash with Called by the Grave", ResponseMode.Auto, false, new[] { rota }, new[] { cbtg }, new[] { ash, ChainLessons.Gearfried }, LessonOutcome.Passed),
            new Case("last-in-first-out", "lets Ash resolve", ResponseMode.Auto, false, new[] { rota }, Array.Empty<uint>(), new[] { ChainLessons.Gearfried }, LessonOutcome.Failed),
            new Case("end-phase", "waits on Auto", ResponseMode.Auto, false, Array.Empty<uint>(), new[] { jar }, Array.Empty<uint>(), LessonOutcome.Failed),
            new Case("end-phase", "turns Chain ON and waits for the End Phase", ResponseMode.On, false, Array.Empty<uint>(), new[] { jar }, Array.Empty<uint>(), LessonOutcome.Passed, InPhase: Phase.End),
            new Case("end-phase", "holds the ask key in the End Phase", ResponseMode.Auto, true, Array.Empty<uint>(), new[] { jar }, Array.Empty<uint>(), LessonOutcome.Passed, InPhase: Phase.End),
            new Case("end-phase", "draws on their own turn", ResponseMode.On, false, new[] { jar }, Array.Empty<uint>(), Array.Empty<uint>(), LessonOutcome.Failed),
            new Case("your-own-chain", "chains the Transporter to Dark Hole", ResponseMode.Auto, false, new[] { hole }, new[] { imt }, new[] { bewd }, LessonOutcome.Passed, InResponse: true),
            new Case("your-own-chain", "lets Dark Hole resolve", ResponseMode.Auto, false, new[] { hole }, Array.Empty<uint>(), new[] { bewd }, LessonOutcome.Failed),
            new Case("your-own-chain", "chains it with Chain ON", ResponseMode.On, false, new[] { hole }, new[] { imt }, new[] { bewd }, LessonOutcome.Passed, InResponse: true),
        };
        int bad = 0;
        foreach (var c in cases)
        {
            var lesson = ChainLessons.Find(c.Lesson)!;
            using var duel = lesson.Recipe(cards, scripts).Create();
            var context = new DuelContext(duel, cards);
            var human = new HumanDuelist(context);
            human.Presenter.Mode = c.Mode;
            human.Presenter.HoldToAsk = () => c.Hold;
            var student = new Student(human, context, c, c.Activate, c.Chain, c.Targets);
            var runner = new DuelRunner(duel, student, lesson.Gus(cards));
            var judge = lesson.Judge();
            var log = new List<string>();
            var narrator = new Ygo.Engine.Presentation.DuelNarrator(cards, "You", "Gus");
            narrator.Line += log.Add;
            narrator.CodeAt = place => BoardState.PublicCodeAt(duel, place);
            runner.MessageReceived += context.Observe;
            runner.MessageReceived += student.Observe;
            runner.MessageReceived += judge.Handle;
            runner.MessageReceived += narrator.Handle;
            // The duel's history, told from your seat: what you'd be shown as messages.
            var history = new Ygo.Engine.Presentation.DuelHistory(cards, "You", "Gus") { Viewer = 0, HiddenPlayer = 1 };
            history.CodeAt = place => BoardState.PublicCodeAt(duel, place);
            var notable = new List<string>();
            history.Notable += (_, _, text) => notable.Add(text);
            runner.MessageReceived += history.Handle;
            for (int steps = 0; steps < 5000 && judge.Outcome == LessonOutcome.Playing && runner.Step(); steps++) { }
            bool ok = judge.Outcome == c.Expected;
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {lesson.Title}: a student who {c.Name} -> {judge.Outcome} (turn {judge.Turn}, asked {student.Asked}x): {judge.Hint}");
            if (!ok) foreach (var line in log.TakeLast(25)) Console.WriteLine("       " + line);
            foreach (var text in notable) Console.WriteLine("       message: " + text);
            if (c.Lesson == "last-in-first-out") foreach (var e in history.Entries) Console.WriteLine($"       history: [{e.Kind}] {e.Text}");
        }
        // The CPU as the student (QA play's stand-in): through the same prompts, Chain ON where the lesson asks for it.
        foreach (var lesson in ChainLessons.All)
        {
            var mode = lesson.Id == "end-phase" ? ResponseMode.On : ResponseMode.Auto;
            var recipe = lesson.Recipe(cards, scripts);
            using var duel = recipe.Create();
            var context = new DuelContext(duel, cards);
            var human = new HumanDuelist(context);
            human.Presenter.Mode = mode;
            var style = new Personality { Name = "Student" };
            var answers = new ResponseLog();
            // Planning ahead, as QA play's stand-in does.
            var bot = new SmartDuelist("You", 5, context, personality: style, planner: new TurnPlanner(recipe, answers, style, () => null));
            var student = new BotStudent(human, bot);
            var runner = new DuelRunner(duel, student, lesson.Gus(cards), answers);
            var judge = lesson.Judge();
            var log = new List<string>();
            var narrator = new Ygo.Engine.Presentation.DuelNarrator(cards, "You", "Gus");
            narrator.Line += log.Add;
            narrator.CodeAt = place => BoardState.PublicCodeAt(duel, place);
            runner.MessageReceived += context.Observe;
            runner.MessageReceived += judge.Handle;
            runner.MessageReceived += narrator.Handle;
            runner.MessageReceived += m => { if (m[0] == Msg.NewTurn || m[0] == Msg.NewPhase) bot.OnPhaseChanged(); };
            for (int steps = 0; steps < 5000 && judge.Outcome == LessonOutcome.Playing && runner.Step(); steps++) { }
            // Answering Ash and holding Jar of Greed for the End Phase are expected of the CPU; saving its own monster from
            // its own Dark Hole is beyond it, shown for information.
            bool ok = judge.Outcome == LessonOutcome.Passed, expected = lesson.Id != "your-own-chain";
            if (!ok && expected) bad++;
            Console.WriteLine($"{(ok ? "ok  " : expected ? "FAIL" : "info")} {lesson.Title}: the CPU as the student (Chain {mode}) -> {judge.Outcome} (turn {judge.Turn}): {judge.Hint}");
            if (!ok && expected) foreach (var line in log.TakeLast(25)) Console.WriteLine("       " + line);
        }
        bad += FirstDuel(cards, scripts);
        Console.WriteLine(bad == 0 ? "lesson test: all passed" : $"lesson test: {bad} failed");
        return bad == 0 ? 0 : 1;
    }
}
