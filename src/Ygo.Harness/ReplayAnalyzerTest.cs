using System.Text.Json;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Protocol;
using Ygo.Engine.Replays;

namespace Ygo.Harness;

/// <summary>
/// `--analyze-test` (docs/dev/REPLAY-ANALYZER-SPEC-2026-11-06.md, section 6): the replay analyzer on known duels.
/// 1. Structure: a headliner clip and a seeded tag duel: one row per answered prompt, a board for every row, JSON that
///    parses, no web or absolute paths, the recorded turns and winner reproduced.
/// 2. A known flag: a seeded duel where a saboteur ends the Battle Phase with lethal on an empty field; the page must hold
///    a high "missed lethal" on that decision, with the bot disagreeing.
/// 3. Fidelity: a planner duel saved with --keep-replays' sidecar and analyzed --as-played agrees on every decision, and so
///    does the tag duel (exact seeds, no planner).
/// 4. Play is unchanged: 8 seeded duels (planner characters and sloppier ones) played with the score callbacks attached
///    and without give byte-identical answers.
/// 5. Baseline: the duels the build before the misplay fix saved (build/analysis/misplay-before-audit, from
///    `build/bin/misplay-before --audit --games 30 --seed 21 --out ...`) replay answer for answer with today's bots when
///    UseCardIdentity / UseIndestructibleSense and the planner's CardIdentity / IndestructibleSense are off.
/// 6. Retries: a replay with a rejected answer inserted shows "played after 1 rejected", compares the bot with the first
///    answer, and plays on reproduced.
/// 7. Severity caps: no disagreement on a minor or non-judgement prompt rises above info, nor a fresh follow-up above low.
/// 8. --keep-replays: flagged mode keeps a sabotaged duel (the missed lethal comes through the wrapped seats) and wrapping
///    changes no answer; a seat run inside AskedOnly is recorded as wrapper=asked-only and the analyzer, wrapping its shadow
///    the same way, agrees on every decision.
/// 9. Path guards: case, symlinks (/tmp and a link to the save) and a lowercase ~/library/application support path all
///    count as inside a stand-in save; --out and --keep-replays refuse it.
/// </summary>
public static class ReplayAnalyzerTest
{
    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root)
    {
        int ok = 0, failed = 0;
        void Check(string what, bool pass)
        {
            Console.WriteLine($"  {(pass ? "ok  " : "FAIL")} {what}");
            if (pass) ok++; else failed++;
        }
        var setup = ReplayAnalyzer.Setup.Load(cards, scripts, root);
        var outDir = Path.Combine(root, "build", "analysis", "analyze-test");
        Directory.CreateDirectory(outDir);
        var fast = new ReplayAnalyzer.Options { Fast = true, Jobs = 1 };
        var timer = System.Diagnostics.Stopwatch.StartNew();

        // ---- 1. Structure: the headliner clip
        Console.WriteLine("Structure: campaign/replays/return-rook.txt");
        var clipPath = Path.Combine(root, "campaign", "replays", "return-rook.txt");
        var clip = Replay.Load(clipPath)!;
        var a = ReplayAnalyzer.Analyze(clip, "return-rook.txt", null, setup, fast, ReplayAnalyzer.Version(File.ReadAllText(clipPath)));
        Structure(a, clip, cards, scripts, root, outDir, fast, Check);

        // ---- 1b + 3. A seeded tag duel (CPU partners, exact seeds): structure, and fidelity as played
        Console.WriteLine("Structure and fidelity: a seeded tag duel");
        // A seed long enough that all four duelists play (the first few end too soon for the last partner).
        Replay tag = null!;
        BotSidecar tagCar = null!;
        for (int s = 7; s < 40; s++)
            if ((tag = TagDuel(cards, scripts, setup, s, firstPlayer: 1, out tagCar)).Turns >= 5) break;
        var tagA = ReplayAnalyzer.Analyze(tag, $"tag-seed{tag.Settings.Seed}.replay", tagCar, setup, new ReplayAnalyzer.Options { Fast = true, AsPlayed = true, Jobs = 1 }, 5);
        Structure(tagA, tag, cards, scripts, root, outDir, fast, Check);
        Check($"tag: all four duelists answered prompts ({string.Join(", ", tagA.Seats.Select(s => $"{s.Name} {tagA.Decisions.Count(d => d.Slot == s.Slot)}"))})",
            tagA.Seats.All(s => tagA.Decisions.Any(d => d.Slot == s.Slot)));
        Check($"tag, as played: the bots agree on every decision ({tagA.Agreed} of {tagA.Analyzed})", tagA.Analyzed > 0 && tagA.Agreed == tagA.Analyzed);

        // ---- 2. A known flag: missed lethal, sabotaged
        Console.WriteLine("A known flag: the saboteur's missed lethal");
        var (sab, sabCar, sabId) = Sabotaged(cards, scripts, setup);
        Check($"a seed with lethal on an empty field was found (decision #{sabId})", sab != null);
        if (sab != null)
        {
            var sa = ReplayAnalyzer.Analyze(sab, "sabotaged.replay", sabCar, setup, new ReplayAnalyzer.Options { Jobs = 1 }, 4);
            File.WriteAllText(Path.Combine(outDir, "sabotaged.html"), ReplayAnalyzer.Page(sa, new ReplayAnalyzer.Options(), outDir));
            var d = sa.Decisions.FirstOrDefault(x => x.Id == sabId);
            Check($"the sabotaged decision is a Battle Phase menu ({d?.Kind}: played {d?.Played})", d?.Kind == "battle");
            Check($"it carries a high \"missed lethal\" ({string.Join(", ", d?.Flags.Select(f => $"{f.S} {f.K}") ?? Array.Empty<string>())})",
                d?.Flags.Any(f => f.K == "missed lethal" && f.S == "high") == true);
            Check($"the full-strength bot disagrees with it (bot: {d?.Bot})", d?.Agree == false && d.Flags.Any(f => f.K == "bot disagrees"));
            var dis = d?.Flags.FirstOrDefault(f => f.K == "bot disagrees");
            Check($"the planner scores it: regret {d?.Regret}, the disagreement {dis?.S} ({d?.ScoreWhy})",
                d?.Regret >= 800 && d.Scores?.Any(x => x.P) == true && d.Scores.Any(x => x.B) && dis?.S is "medium" or "high");
            Check("it ranks among the index's five most suspicious decisions",
                JsonDocument.Parse(ReplayAnalyzer.SummaryJson(new List<ReplayAnalyzer.Analysis> { sa }, new ReplayAnalyzer.Options()))
                    .RootElement.GetProperty("top").EnumerateArray().Take(5).Any(f => f.GetProperty("Id").GetInt32() == sabId));
        }

        // ---- 3. Fidelity: a planner duel through --keep-replays and back, analyzed as played
        Console.WriteLine("Fidelity: a planner duel, kept with its sidecar, analyzed --as-played");
        var keepDir = Path.Combine(outDir, "kept");
        if (Directory.Exists(keepDir)) Directory.Delete(keepDir, true);
        var keeper = new ReplayKeeper(keepDir, "all", 10);
        var path = PlannerDuel(cards, scripts, setup, keeper, 11);
        var kept = Replay.Load(path);
        var car = BotSidecar.Load(BotSidecar.PathFor(path));
        Check($"the kept replay and its sidecar load ({Path.GetFileName(path)}, {car?.Seats.Count} seats, planner {car?.Seats.All(s => s.Planner)})",
            kept != null && car?.Seats.Count == 2 && car.Seats.All(s => s.Planner));
        if (kept != null)
        {
            var fa = ReplayAnalyzer.Analyze(kept, Path.GetFileName(path), car, setup, new ReplayAnalyzer.Options { AsPlayed = true, Jobs = 1 }, 4);
            Check($"as played, with the planner: the bots agree on every decision ({fa.Agreed} of {fa.Analyzed}, {fa.Seconds:F1}s)",
                fa.Analyzed > 0 && fa.Agreed == fa.Analyzed);
            Check($"playback reproduced it (turn {fa.PlayedTurns} of {kept.Turns}, winner {fa.PlayedWinner} of {kept.Winner})",
                !fa.Diverged && fa.PlayedTurns == kept.Turns && fa.PlayedWinner == kept.Winner);
        }

        // ---- 4. Play is unchanged with the callbacks on
        Console.WriteLine("Play is unchanged with the score callbacks attached");
        var planners = Characters(setup, p => p.PlansAhead && p.Skill >= 1.0, 6);
        var sloppy = Characters(setup, p => p.KnowsDeck && !p.PlansAhead, 4);
        var pairs = new List<((Personality, Deck) a, (Personality, Deck) b, int seed, bool plan)>
        {
            (planners[0], planners[1], 31, true), (planners[2], planners[3], 32, true), (planners[4], planners[5], 33, true),
            (planners[1], planners[4], 34, true), (planners[3], planners[0], 37, true), (planners[5], planners[2], 38, true),
            (sloppy[0], sloppy[1], 35, false), (sloppy[2], sloppy[3], 36, false),
        };
        foreach (var (pa, pb, seed, plan) in pairs)
        {
            var off = PlayAb(cards, scripts, pa, pb, seed, plan, attach: false, out _, out _);
            var on = PlayAb(cards, scripts, pa, pb, seed, plan, attach: true, out var notes, out var planned);
            bool same = off.Count == on.Count && off.Zip(on).All(x => x.First.SequenceEqual(x.Second));
            Check($"seed {seed}, {pa.Item1.Name} vs {pb.Item1.Name}{(plan ? " (planners)" : "")}: {off.Count} answers identical with the callbacks on " +
                  $"({notes} option notes, {planned} planned decisions reported)", same && notes > 0 && (!plan || planned > 0));
        }

        var analyses = new List<ReplayAnalyzer.Analysis> { a, tagA };

        // ---- 5. Baseline: the toggles off reproduce the build before the misplay fix
        Console.WriteLine("Baseline: today's bots with the misplay fix's toggles off, against the build before it");
        var before = Path.Combine(root, "build", "analysis", "misplay-before-audit");
        if (!Directory.Exists(before)) Check($"the baseline replays exist ({Path.GetRelativePath(root, before)}; see the summary above for the command)", false);
        else
        {
            int same = 0, total = 0;
            var differ = new List<string>();
            foreach (var file in Directory.GetFiles(before, "*.replay").OrderBy(f => f, StringComparer.Ordinal))
            {
                var old = Replay.Load(file)!;
                int g = int.Parse(new string(Path.GetFileNameWithoutExtension(file).Where(char.IsDigit).ToArray()));
                var now = Rebuilt(cards, scripts, setup, old, g, togglesOff: true);
                total++;
                if (now.Count == old.Responses.Count && now.Zip(old.Responses).All(x => x.First.SequenceEqual(x.Second))) same++;
                else differ.Add($"{Path.GetFileName(file)} ({now.Count} vs {old.Responses.Count} answers)");
            }
            Check($"{same} of {total} pre-fix audit duels replayed answer for answer with the toggles off{(differ.Count > 0 ? $"; differ: {string.Join(", ", differ)}" : "")}",
                total > 0 && same == total);
        }

        // ---- 6. Retries
        Console.WriteLine("Retries: a rejected answer inserted before a Main Phase answer");
        var (withRetry, retryAt) = InsertRejected(tag, cards, scripts);
        Check($"a Main Phase answer to put a rejected one before was found (answer #{retryAt})", retryAt >= 0);
        if (retryAt >= 0)
        {
            var ra = ReplayAnalyzer.Analyze(withRetry, "retry.replay", tagCar, setup, new ReplayAnalyzer.Options { Fast = true, AsPlayed = true, Jobs = 1 }, 5);
            var rd = ra.Decisions.FirstOrDefault(x => x.Retried > 0);
            Check($"the decision shows the rejected answer ({rd?.Played})", rd?.Retried == 1 && rd.Played.Contains("played after 1 rejected"));
            Check($"the bot is compared with the first (rejected) answer: it disagrees (bot: {rd?.Bot})", rd?.Agree == false);
            Check($"playback still reproduces the duel (turn {ra.PlayedTurns} of {tag.Turns}, winner {ra.PlayedWinner}; {string.Join("; ", ra.Warnings)})",
                !ra.Diverged && ra.PlayedTurns == tag.Turns && ra.PlayedWinner == tag.Winner);
            Check($"until it, the bots agreed on every other decision, and the rows after the rebuild are marked (before it: {ra.AgreedBeforeDrift} of {ra.AnalyzedBeforeDrift}, drift from #{ra.DriftFrom})",
                rd != null && ra.DriftFrom > rd.Id && ra.AgreedBeforeDrift == ra.AnalyzedBeforeDrift - 1
                && ra.Decisions.Where(x => x.Id >= ra.DriftFrom).All(x => x.AfterDrift) && ra.Decisions.Where(x => x.Id < ra.DriftFrom).All(x => !x.AfterDrift));
            analyses.Add(ra);
        }

        // ---- 8. --keep-replays: flagged mode through wrapped seats, and a wrapped seat's sidecar
        Console.WriteLine("--keep-replays: flagged mode and wrapped seats");
        var flaggedDir = Path.Combine(outDir, "kept-flagged");
        if (Directory.Exists(flaggedDir)) Directory.Delete(flaggedDir, true);
        var flaggedKeeper = new ReplayKeeper(flaggedDir, "flagged", 10);
        var (plainAnswers, _) = SabotagedRun(cards, scripts, setup, sabCar, null);
        var (keptAnswers, keptPath) = SabotagedRun(cards, scripts, setup, sabCar, flaggedKeeper);
        Check($"flagged mode kept the sabotaged duel (the missed lethal came through the wrapped seat): {(keptPath == null ? "nothing kept" : Path.GetFileName(keptPath))}",
            keptPath != null);
        Check($"wrapping the seats changed no answer ({keptAnswers.Count} answers, {plainAnswers.Count} without --keep-replays)",
            plainAnswers.Count > 0 && plainAnswers.Count == keptAnswers.Count && plainAnswers.Zip(keptAnswers).All(x => x.First.SequenceEqual(x.Second)));
        Check("severities: a risky summon that won is info (flagged mode skips it), self damage under 1000 low, from 1000 medium",
            ReplayAnalyzer.Severity("risky summon, then won") == "info" && ReplayAnalyzer.Severity("self damage", "took 500 from their own X") == "low"
            && ReplayAnalyzer.Severity("self damage", "took 1000 from their own X") == "medium");
        var wrapDir = Path.Combine(outDir, "kept-wrapped");
        if (Directory.Exists(wrapDir)) Directory.Delete(wrapDir, true);
        var wrapPath = AskedOnlyDuel(cards, scripts, setup, new ReplayKeeper(wrapDir, "all", 10), 41);
        var wrapCar = BotSidecar.Load(BotSidecar.PathFor(wrapPath))!;
        Check($"the wrapped seat's sidecar says wrapper=asked-only ({File.ReadAllLines(BotSidecar.PathFor(wrapPath)).FirstOrDefault(l => l.StartsWith("seat 0"))})",
            wrapCar.Seats.First(x => x.Slot == 0).Wrapper == "asked-only");
        var wa = ReplayAnalyzer.Analyze(Replay.Load(wrapPath)!, "wrapped.replay", wrapCar, setup, new ReplayAnalyzer.Options { Fast = true, AsPlayed = true, Jobs = 1 }, 4);
        var unwrapped = BotSidecar.Load(BotSidecar.PathFor(wrapPath))!;
        foreach (var x in unwrapped.Seats) x.Wrapper = "";
        var ua = ReplayAnalyzer.Analyze(Replay.Load(wrapPath)!, "unwrapped.replay", unwrapped, setup, new ReplayAnalyzer.Options { Fast = true, AsPlayed = true, Jobs = 1 }, 4);
        Check($"the analyzer wraps its shadow the same way: {wa.Agreed} of {wa.Analyzed} agree (unwrapped it would be {ua.AgreedBeforeDrift} of {ua.AnalyzedBeforeDrift} up to the first rebuild)",
            wa.Analyzed > 0 && wa.Agreed == wa.Analyzed);
        Check("a playbook that is off is written empty in the sidecar", SeatSpec.Of(0, null, "no-such-deck", 1, false).Playbook == "");

        // ---- 7. Severity caps, over every analysis above (and the owner's duel against Nerio when its copy is here)
        Console.WriteLine("Severity caps");
        var nerio = Directory.Exists(Path.Combine(root, "build", "analysis", "input", "owner"))
            ? Directory.GetFiles(Path.Combine(root, "build", "analysis", "input", "owner"), "*Nerio*.replay").FirstOrDefault() : null;
        if (nerio != null)
        {
            var na = ReplayAnalyzer.Analyze(Replay.Load(nerio)!, Path.GetFileName(nerio), null, setup, new ReplayAnalyzer.Options { Jobs = 1 }, 4);
            var n93 = na.Decisions.FirstOrDefault(x => x.Id == 93);
            Check($"the owner's duel against Nerio, decision #93 (fresh {n93?.Fresh}, {n93?.Kind}): {string.Join(", ", n93?.Flags.Select(f => $"{f.S} {f.K}") ?? Array.Empty<string>())}",
                n93 != null && n93.Flags.Where(f => f.K == "bot disagrees").All(f => ReplayAnalyzer.Weight(f.S) <= 1));
            analyses.Add(na);
        }
        if (sab != null) analyses.Add(ReplayAnalyzer.Analyze(sab, "sabotaged.replay", sabCar, setup, new ReplayAnalyzer.Options { Jobs = 1 }, 4));
        var dis2 = analyses.SelectMany(x => x.Decisions).SelectMany(d => d.Flags.Where(f => f.K == "bot disagrees").Select(f => (d, f))).ToList();
        int fresh = dis2.Count(x => x.d.Fresh && x.d.FollowUp), minor = dis2.Count(x => x.d.Minor || !x.d.Judgement);
        Check($"{fresh} fresh follow-ups are at most low, {minor} minor or non-judgement disagreements are info ({dis2.Count} disagreements)",
            dis2.Where(x => x.d.Fresh && x.d.FollowUp).All(x => ReplayAnalyzer.Weight(x.f.S) <= 1)
            && dis2.Where(x => x.d.Minor || !x.d.Judgement).All(x => x.f.S == "info"));

        // ---- 7b. The index ranks by the capped view: a fresh follow-up's big regret doesn't outrank a plain row's
        {
            var fake = new ReplayAnalyzer.Analysis { File = "fake.replay", Page = "fake.html" };
            ReplayAnalyzer.Decision Row(int id, bool fresh, double regret) => new ReplayAnalyzer.Decision
            {
                Id = id, Who = "CPU", Kind = "select", Fresh = fresh, FollowUp = true, Judgement = true, Regret = regret,
                Flags = { new ReplayAnalyzer.Flag { S = "low", K = "bot disagrees", T = "", Src = "bot" } },
            };
            fake.Decisions.Add(Row(0, fresh: true, regret: 9000));
            fake.Decisions.Add(Row(1, fresh: false, regret: 500));
            var top = JsonDocument.Parse(ReplayAnalyzer.SummaryJson(new List<ReplayAnalyzer.Analysis> { fake }, new ReplayAnalyzer.Options()))
                .RootElement.GetProperty("top").EnumerateArray().Select(f => f.GetProperty("Id").GetInt32()).ToList();
            Check($"the index ranks a plain low row above a fresh follow-up's (order {string.Join(", ", top)})", top.Count == 2 && top[0] == 1);
        }

        // ---- 9. Path guards, against a stand-in save
        Console.WriteLine("Path guards (a stand-in save, never the real one)");
        PathGuards(cards, scripts, root, outDir, clipPath, Check);

        Console.WriteLine($"{ok} ok, {failed} failed ({timer.Elapsed.TotalSeconds:F0}s); pages in {Path.GetRelativePath(root, outDir)}");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>The page-level checks every analyzed replay must pass.</summary>
    private static void Structure(ReplayAnalyzer.Analysis a, Replay replay, ICardDataProvider cards, IScriptProvider scripts, string root, string outDir,
        ReplayAnalyzer.Options o, Action<string, bool> check)
    {
        check($"{a.File}: played back without diverging ({string.Join("; ", a.Warnings)})", a.Error == null && !a.Diverged);
        check($"{a.File}: turns and winner reproduced (turn {a.PlayedTurns} of {replay.Turns}, winner {a.PlayedWinner} of {replay.Winner})",
            a.PlayedTurns == replay.Turns && (replay.Winner < 0 || a.PlayedWinner == replay.Winner));
        int answered = Answered(replay, cards, scripts);
        check($"{a.File}: one row per answered prompt ({a.Decisions.Count} rows, {answered} prompts)", a.Decisions.Count == answered);
        check($"{a.File}: every row has a board and options or a title",
            a.Decisions.All(d => d.Board?.P[0] != null && d.Board.P[1] != null && d.Title.Length > 0 && d.Played.Length > 0));
        var html = ReplayAnalyzer.Page(a, o, outDir);
        File.WriteAllText(Path.Combine(outDir, Path.ChangeExtension(a.File, ".html")), html);
        const string open = "<script type=\"application/json\" id=\"data\">";
        int start = html.IndexOf(open, StringComparison.Ordinal) + open.Length, end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        bool parses;
        int items = -1;
        try { using var doc = JsonDocument.Parse(html[start..end]); items = doc.RootElement.GetProperty("items").GetArrayLength(); parses = true; }
        catch (Exception) { parses = false; }
        check($"{a.File}: the page's data parses ({items} items) and the page is well formed",
            parses && items == a.Items.Count && html.StartsWith("<!doctype html>") && html.TrimEnd().EndsWith("</html>"));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        check($"{a.File}: no web links, no absolute paths, no account name",
            !html.Contains("http://") && !html.Contains("https://") && !html.Contains(root) && !html.Contains(home)
            && !html.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Prompts answered in a plain playback (retries not counted): what the page's rows must match.</summary>
    private static int Answered(Replay replay, ICardDataProvider cards, IScriptProvider scripts)
    {
        using var duel = replay.Recipe(cards, scripts).Create();
        var feed = new ReplayFeed(replay.Responses);
        int count = 0;
        var seat = new Counting(feed, () => count++);
        DuelRunner runner;
        if (replay.IsTag)
        {
            var (ta, tb) = Ygo.Engine.TagDuel.Seats(seat, seat, seat, seat, replay.Settings.FirstPlayer);
            runner = new DuelRunner(duel, ta, tb);
            runner.MessageReceived += ta.Observe;
            runner.MessageReceived += tb.Observe;
        }
        else runner = new DuelRunner(duel, seat, seat);
        YataLock.Attach(runner, duel);
        if (replay.Turns == TurnCap.AlleyTurns && replay.Winner >= 0) TurnCap.Attach(runner, duel);
        while (runner.Step() && !(runner.IsWaiting && feed.Finished)) { }
        return count;
    }

    private sealed class Counting : IDuelist
    {
        private readonly ReplayFeed _feed;
        private readonly Action _counted;
        public Counting(ReplayFeed feed, Action counted) { _feed = feed; _counted = counted; }
        public byte[]? Respond(Prompt prompt, int attempt)
        {
            var r = _feed.Take();
            if (r != null && attempt == 0) _counted();
            return r;
        }
        public void OnPhaseChanged() { }
    }

    private static (Personality style, Deck deck)[] Characters(ReplayAnalyzer.Setup setup, Func<Personality, bool> which, int count) =>
        setup.Roster.Where(p => p.Decks.Count > 0 && setup.DeckByName.ContainsKey(p.Decks[0]) && which(p)).OrderBy(p => p.Id, StringComparer.Ordinal)
            .Take(count).Select(p => (p, Load(setup, p.Decks[0]))).ToArray();

    private static Deck Load(ReplayAnalyzer.Setup setup, string name)
    {
        var d = Deck.LoadYdk(setup.DeckByName[name]);
        d.Name = name;
        return d;
    }

    /// <summary>A seeded tag duel of four characters below the planner's skill (exact seeds, so the bots can be rebuilt as played).</summary>
    private static Replay TagDuel(ICardDataProvider cards, IScriptProvider scripts, ReplayAnalyzer.Setup setup, int seed, int firstPlayer, out BotSidecar car)
    {
        var four = Characters(setup, p => p.KnowsDeck && !p.PlansAhead, 4);
        var settings = new DuelSettings { Seed = (ulong)seed, FirstPlayer = firstPlayer };
        // Slots: 0 team A's lead, 2 its partner, 1 team B's lead, 3 its partner.
        var bySlot = new[] { four[0], four[2], four[1], four[3] };
        var recipe = Ygo.Engine.TagDuel.Recipe(cards, scripts, settings, bySlot[0].deck.Clone(), bySlot[2].deck.Clone(), bySlot[1].deck.Clone(), bySlot[3].deck.Clone());
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var answers = new ResponseLog();
        IDuelist Bot(int slot) => new SmartDuelist(bySlot[slot].style.Name, seed * 4 + slot, context, null, bySlot[slot].style, Playbooks.ForDeck(bySlot[slot].deck.Name));
        var (ta, tb) = Ygo.Engine.TagDuel.Seats(Bot(0), Bot(2), Bot(1), Bot(3), firstPlayer);
        var runner = new DuelRunner(duel, ta, tb, answers);
        runner.MessageReceived += context.Observe;
        runner.MessageReceived += ta.Observe;
        runner.MessageReceived += tb.Observe;
        YataLock.Attach(runner, duel);
        var result = runner.Run(20000);
        var seats = Enumerable.Range(0, 4).Select(s => SeatSpec.Of(s, bySlot[s].style, bySlot[s].deck.Name, seed * 4 + s, false)).ToArray();
        car = new BotSidecar { Study = "analyze-test", Seats = seats.ToList() };
        return ReplayKeeper.Build(recipe, answers, new[] { bySlot[0].style.Name, bySlot[1].style.Name }, seats, result);
    }

    /// <summary>
    /// Seeds 1-200 until seat 0's Battle Phase menu offers lethal against an empty field; the saboteur leaves the Battle
    /// Phase there instead (once). Returns the replay, its sidecar and the sabotaged decision's number.
    /// </summary>
    private static (Replay? replay, BotSidecar? car, int id) Sabotaged(ICardDataProvider cards, IScriptProvider scripts, ReplayAnalyzer.Setup setup)
    {
        var two = Characters(setup, p => p.KnowsDeck && !p.PlansAhead && p.Aggression >= 0.6, 2);
        if (two.Length < 2) two = Characters(setup, p => p.KnowsDeck && !p.PlansAhead, 2);
        for (int seed = 1; seed <= 200; seed++)
        {
            var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)seed }, Deck0 = two[0].deck.Clone(), Deck1 = two[1].deck.Clone() };
            using var duel = recipe.Create();
            var context = new DuelContext(duel, cards);
            var answers = new ResponseLog();
            DuelRunner? runner = null;
            int sabotaged = -1;
            var inner = new SmartDuelist(two[0].style.Name, seed * 2, context, null, two[0].style, Playbooks.ForDeck(two[0].deck.Name));
            var saboteur = new Saboteur(inner, context, p =>
            {
                if (sabotaged >= 0) return false;
                sabotaged = runner!.Result.Prompts - 1;
                return true;
            });
            runner = new DuelRunner(duel, saboteur,
                new SmartDuelist(two[1].style.Name, seed * 2 + 1, context, null, two[1].style, Playbooks.ForDeck(two[1].deck.Name)), answers);
            runner.MessageReceived += context.Observe;
            YataLock.Attach(runner, duel);
            var result = runner.Run(20000);
            if (sabotaged < 0) continue;
            var seats = new[] { SeatSpec.Of(0, two[0].style, two[0].deck.Name, seed * 2, false), SeatSpec.Of(1, two[1].style, two[1].deck.Name, seed * 2 + 1, false) };
            return (ReplayKeeper.Build(recipe, answers, new[] { two[0].style.Name, two[1].style.Name }, seats, result),
                new BotSidecar { Study = "analyze-test", Seats = seats.ToList() }, sabotaged);
        }
        return (null, null, -1);
    }

    /// <summary>Plays its bot, except that once it leaves the Battle Phase with lethal against an empty field.</summary>
    private sealed class Saboteur : IDuelist
    {
        private readonly IDuelist _inner;
        private readonly DuelContext _ctx;
        private readonly Func<BattlePrompt, bool> _take;
        public Saboteur(IDuelist inner, DuelContext ctx, Func<BattlePrompt, bool> take) { _inner = inner; _ctx = ctx; _take = take; }
        public void OnPhaseChanged() => _inner.OnPhaseChanged();
        public byte[]? Respond(Prompt prompt, int attempt)
        {
            var answer = _inner.Respond(prompt, attempt);
            if (attempt == 0 && prompt is BattlePrompt p && (p.CanMain2 || p.CanEndPhase))
            {
                int me = p.Player & 1, opp = 1 - me;
                var board = _ctx.Board;
                bool empty = board.Players[opp].Monsters.All(m => m == null);
                int total = 0;
                for (int i = 0; i < p.Attackers.Count; i++)
                {
                    if (p.CanDirectAttack.Count > i && !p.CanDirectAttack[i]) continue;
                    var a = p.Attackers[i];
                    total += a.Sequence < board.Players[me].Monsters.Count ? board.Players[me].Monsters[(int)a.Sequence]?.Attack ?? 0 : 0;
                }
                int lp = board.Players[opp].LifePoints;
                if (empty && lp > 0 && total >= lp && _take(p)) return Responses.LeaveBattle(p);
            }
            return answer;
        }
    }

    /// <summary>One seeded duel's answers; with <paramref name="attach"/>, every bot and planner reports through the callbacks.</summary>
    private static List<byte[]> PlayAb(ICardDataProvider cards, IScriptProvider scripts, (Personality style, Deck deck) a, (Personality style, Deck deck) b, int seed,
        bool plan, bool attach, out int notes, out int planned)
    {
        var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)seed }, Deck0 = a.deck.Clone(), Deck1 = b.deck.Clone() };
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var answers = new ResponseLog();
        int n = 0, p = 0;
        var seen = new List<string>();
        IDuelist Bot((Personality style, Deck deck) c, int seat)
        {
            var planner = plan ? new TurnPlanner(recipe, answers, c.style, () => Playbooks.ForDeck(c.deck.Name)) : null;
            var bot = new SmartDuelist(c.style.Name, seed * 4 + seat, context, null, c.style, Playbooks.ForDeck(c.deck.Name), planner);
            if (attach)
            {
                // The collectors do real work with what they get (as the analyzer does), to show reading it changes nothing.
                bot.Notes = note => { n++; seen.Add($"{note.Label} {note.Score:0.0} {note.Rejected}"); };
                if (planner != null) planner.Scored = dec => { p++; seen.Add(string.Join(",", dec.Scores.Select(x => $"{x.Label}:{x.Final:0}"))); };
            }
            return bot;
        }
        var runner = new DuelRunner(duel, Bot(a, 0), Bot(b, 1), answers);
        runner.MessageReceived += context.Observe;
        YataLock.Attach(runner, duel);
        runner.Run(20000);
        notes = n;
        planned = p;
        return answers.Responses.ToList();
    }

    /// <summary>
    /// An audit replay's duel played again by today's bots, built as --audit builds them (seed g*2+seat, the character's
    /// playbook if it knows decks, the planner if it plans), with the misplay fix's toggles off: the answers it gives.
    /// </summary>
    private static List<byte[]> Rebuilt(ICardDataProvider cards, IScriptProvider scripts, ReplayAnalyzer.Setup setup, Replay old, int g, bool togglesOff)
    {
        var recipe = old.Recipe(cards, scripts);
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var answers = new ResponseLog();
        IDuelist Bot(int seat)
        {
            var style = setup.Roster.First(p => p.Id == old.Characters[seat]);
            // The deck file itself when it's the same deck: its notes (#recipe) name the playbook, and a replay doesn't keep them.
            var deck = setup.DeckByName.ContainsKey(old.DeckNames[seat]) && Load(setup, old.DeckNames[seat]) is var file
                       && ReplayAnalyzer.Setup.Contents(file) == ReplayAnalyzer.Setup.Contents(old.Decks[seat]) ? file : old.Decks[seat];
            // The misplay fixes since that build: 2026-11-06's battle fixes and the misplay hunt's (docs/dev/MISPLAY-HUNT-2026-11-06.md).
            bool on = !togglesOff;
            Playbook? Book() { var b = style.KnowsDeck ? Playbooks.ForDeck(deck) : null; if (b != null) b.UseBaseCodes = on; return b; }
            var playbook = Book();
            var planner = style.PlansAhead ? new TurnPlanner(recipe, answers, style, Book)
                {
                    CardIdentity = on, IndestructibleSense = on, BattleTriggerSense = on, DeckSetSense = on, RecoverySense = on,
                    ActivationWindowSense = on, LethalRiskSense = on, SelfDamageSense = on,
                } : null;
            return new SmartDuelist(style.Name, g * 2 + seat, context, null, style, playbook, planner)
                {
                    UseCardIdentity = on, UseIndestructibleSense = on, UseBattleTriggerSense = on, UseDeckSetSense = on,
                    UseRecoverySense = on, UseActivationWindowSense = on, UseSelfDamageSense = on,
                };
        }
        var runner = new DuelRunner(duel, Bot(0), Bot(1), answers);
        runner.MessageReceived += context.Observe;
        YataLock.Attach(runner, duel);
        runner.Run(8000);
        return answers.Responses.ToList();
    }

    /// <summary>The replay with a rejected answer (an activation that isn't offered) put before its first Main Phase answer; -1 if none.</summary>
    private static (Replay replay, int at) InsertRejected(Replay replay, ICardDataProvider cards, IScriptProvider scripts)
    {
        using var duel = replay.Recipe(cards, scripts).Create();
        var feed = new ReplayFeed(replay.Responses);
        int at = -1, index = 0;
        var seat = new Peek(feed, (prompt, attempt) =>
        {
            if (at < 0 && attempt == 0 && prompt is IdlePrompt && index > 20) at = index;
            index++;
        });
        var (ta, tb) = Ygo.Engine.TagDuel.Seats(seat, seat, seat, seat, replay.Settings.FirstPlayer);
        var runner = new DuelRunner(duel, ta, tb);
        runner.MessageReceived += ta.Observe;
        runner.MessageReceived += tb.Observe;
        YataLock.Attach(runner, duel);
        while (at < 0 && runner.Step() && !(runner.IsWaiting && feed.Finished)) { }
        if (at < 0) return (replay, -1);
        var copy = Replay.FromCode(replay.ToCode())!;
        copy.Responses.Insert(at, Responses.Idle(IdleAction.Activate, 99));
        return (copy, at);
    }

    private sealed class Peek : IDuelist
    {
        private readonly ReplayFeed _feed;
        private readonly Action<Prompt, int> _seen;
        public Peek(ReplayFeed feed, Action<Prompt, int> seen) { _feed = feed; _seen = seen; }
        public byte[]? Respond(Prompt prompt, int attempt) { _seen(prompt, attempt); return _feed.Take(); }
        public void OnPhaseChanged() { }
    }

    /// <summary>
    /// The sabotaged duel played again, through a keeper (Begin, Wrap, Attach) or without one: its answers, and the kept
    /// replay's path (null if nothing was kept).
    /// </summary>
    private static (List<byte[]> answers, string? kept) SabotagedRun(ICardDataProvider cards, IScriptProvider scripts, ReplayAnalyzer.Setup setup,
        BotSidecar? car, ReplayKeeper? keeper)
    {
        if (car == null) return (new List<byte[]>(), null);
        var styles = car.Seats.OrderBy(x => x.Slot).Select(x => setup.Roster.First(p => p.Id == x.Character)).ToArray();
        var decks = car.Seats.OrderBy(x => x.Slot).Select(x => Load(setup, x.Deck)).ToArray();
        int seed = car.Seats.First(x => x.Slot == 0).Seed / 2;
        var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)seed }, Deck0 = decks[0].Clone(), Deck1 = decks[1].Clone() };
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var answers = new ResponseLog();
        var w = keeper?.Begin("analyze-test", $"sabotaged-s{seed}", recipe, duel, answers, new[] { styles[0].Name, styles[1].Name }, car.Seats.ToArray());
        bool done = false;
        var saboteur = new Saboteur(new SmartDuelist(styles[0].Name, seed * 2, context, null, styles[0], Playbooks.ForDeck(decks[0].Name)), context,
            _ => { if (done) return false; done = true; return true; });
        var runner = new DuelRunner(duel, ReplayKeeper.Wrap(w, saboteur),
            ReplayKeeper.Wrap(w, new SmartDuelist(styles[1].Name, seed * 2 + 1, context, null, styles[1], Playbooks.ForDeck(decks[1].Name))), answers);
        runner.MessageReceived += context.Observe;
        YataLock.Attach(runner, duel);
        ReplayKeeper.Attach(w, runner);
        var result = runner.Run(20000);
        keeper?.Finish(w, result);
        return (answers.Responses.ToList(), keeper == null ? null : Directory.GetFiles(keeper.Folder, "*.replay").FirstOrDefault());
    }

    /// <summary>A duel with seat 0 inside WallStudy.AskedOnly (as --circuit-study's auto-chain), kept with its sidecar; the replay's path.</summary>
    private static string AskedOnlyDuel(ICardDataProvider cards, IScriptProvider scripts, ReplayAnalyzer.Setup setup, ReplayKeeper keeper, int seed)
    {
        var two = Characters(setup, p => p.KnowsDeck && !p.PlansAhead, 2);
        var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)seed }, Deck0 = two[0].deck.Clone(), Deck1 = two[1].deck.Clone() };
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var answers = new ResponseLog();
        var w = keeper.Begin("analyze-test", $"asked-only-s{seed}", recipe, duel, answers, new[] { two[0].style.Name, two[1].style.Name },
            new[] { SeatSpec.Of(0, two[0].style, two[0].deck.Name, seed * 4, false, "", "asked-only"), SeatSpec.Of(1, two[1].style, two[1].deck.Name, seed * 4 + 1, false) });
        IDuelist yours = new WallStudy.AskedOnly(new SmartDuelist(two[0].style.Name, seed * 4, context, null, two[0].style, Playbooks.ForDeck(two[0].deck.Name)), context);
        var runner = new DuelRunner(duel, ReplayKeeper.Wrap(w, yours),
            ReplayKeeper.Wrap(w, new SmartDuelist(two[1].style.Name, seed * 4 + 1, context, null, two[1].style, Playbooks.ForDeck(two[1].deck.Name))), answers);
        runner.MessageReceived += context.Observe;
        YataLock.Attach(runner, duel);
        ReplayKeeper.Attach(w, runner);
        keeper.Finish(w, runner.Run(20000));
        return Directory.GetFiles(keeper.Folder, "*.replay").Single();
    }

    /// <summary>The guards on a stand-in save: case, symlinks, /tmp, a lowercase Library path; the analyzer's --out and --keep-replays refuse it.</summary>
    private static void PathGuards(ICardDataProvider cards, IScriptProvider scripts, string root, string outDir, string clipPath, Action<string, bool> check)
    {
        var realSave = SafePaths.SaveRoot;
        var stand = Path.Combine(outDir, "guard");
        if (Directory.Exists(stand)) Directory.Delete(stand, true);
        // A stand-in home with the save where the game keeps it: Library/Application Support/YgoCampaign/YgoCampaign/mods.
        var save = Path.Combine(stand, "Home", "Library", "Application Support", "YgoCampaign");
        Directory.CreateDirectory(Path.Combine(save, "YgoCampaign", "mods", "replays"));
        SafePaths.SaveRoot = save;
        try
        {
            var lower = Path.Combine(stand, "Home", "library", "application support", "ygocampaign", "YgoCampaign", "mods", "out");
            check("a lowercase library/application support path is inside the save", SafePaths.IsInside(lower, save));
            check("an upper-case one is too", SafePaths.IsInside(Path.Combine(stand, "HOME", "LIBRARY", "APPLICATION SUPPORT", "YGOCAMPAIGN", "x"), save));
            var link = Path.Combine(stand, "link-to-save");
            Directory.CreateSymbolicLink(link, save);
            check("a symlink into the save is inside it", SafePaths.IsInside(Path.Combine(link, "YgoCampaign", "mods", "out"), save));
            check("/tmp/... is inside /private/tmp (macOS's own link)", !OperatingSystem.IsMacOS() || SafePaths.IsInside("/tmp/analysis-guard", "/private/tmp"));
            check("a sibling folder with a longer name isn't", !SafePaths.IsInside(save + "-other", save));
            int lowerRun = ReplayAnalyzer.Run(cards, scripts, root, new[] { "--analyze-replay", clipPath, "--out", lower });
            int linkRun = ReplayAnalyzer.Run(cards, scripts, root, new[] { "--analyze-replay", clipPath, "--out", Path.Combine(link, "out") });
            check($"--out refuses both (exit {lowerRun} and {linkRun}) and writes nothing",
                lowerRun == 1 && linkRun == 1 && !Directory.Exists(lower) && !Directory.Exists(Path.Combine(save, "out")));
            check("--keep-replays refuses the save (lowercase), a bad --keep and a bad --keep-max",
                ReplayKeeper.Check(lower, "flagged", null, out _) != null && ReplayKeeper.Check(Path.Combine(stand, "ok"), "everything", null, out _) != null
                && ReplayKeeper.Check(Path.Combine(stand, "ok"), "all", "0", out _) != null && ReplayKeeper.Check(Path.Combine(stand, "ok"), "all", "5", out var m) == null && m == 5);
            check("the home folder and project root are scrubbed from page text",
                SafePaths.Scrub($"{root}/x and {SafePaths.Home}/y", root) == "<project>/x and ~/y");
        }
        finally { SafePaths.SaveRoot = realSave; }
    }

    /// <summary>A planner duel of two full-strength characters, kept with its sidecar; returns the replay's path.</summary>
    private static string PlannerDuel(ICardDataProvider cards, IScriptProvider scripts, ReplayAnalyzer.Setup setup, ReplayKeeper keeper, int seed)
    {
        var two = Characters(setup, p => p.PlansAhead && p.Skill >= 1.0, 2);
        var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)seed }, Deck0 = two[0].deck.Clone(), Deck1 = two[1].deck.Clone() };
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var answers = new ResponseLog();
        IDuelist Bot(int seat) => new SmartDuelist(two[seat].style.Name, seed * 4 + seat, context, null, two[seat].style, Playbooks.ForDeck(two[seat].deck.Name),
            new TurnPlanner(recipe, answers, two[seat].style, () => Playbooks.ForDeck(two[seat].deck.Name)));
        var w = keeper.Begin("analyze-test", $"planner-s{seed}", recipe, duel, answers, new[] { two[0].style.Name, two[1].style.Name },
            new[] { SeatSpec.Of(0, two[0].style, two[0].deck.Name, seed * 4, true), SeatSpec.Of(1, two[1].style, two[1].deck.Name, seed * 4 + 1, true) });
        var runner = new DuelRunner(duel, ReplayKeeper.Wrap(w, Bot(0)), ReplayKeeper.Wrap(w, Bot(1)), answers);
        runner.MessageReceived += context.Observe;
        YataLock.Attach(runner, duel);
        ReplayKeeper.Attach(w, runner);
        var result = runner.Run(20000);
        keeper.Finish(w, result);
        return Directory.GetFiles(keeper.Folder, "*.replay").Single();
    }
}
