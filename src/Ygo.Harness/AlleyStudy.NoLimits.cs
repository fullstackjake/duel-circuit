using System.Diagnostics;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--alley-study --only "No Limits"` (docs/dev/NO-LIMITS-SPEC-2026-11-02.md 4.7 and 14.3, chunk 3): the No Limits tier measured as the
/// spec says, for every build whose deck exists, ready or not (the study is what decides `ready:`):
/// <list type="bullet">
/// <item>The reference deck: the highest-rated deck of deck-ratings.txt that is legal under the No Limits list as built (made legal
/// for that list, not the Alley's), played by the QA stand-in (the roster's best planner, knowing its deck).</item>
/// <item>Every build played directly, 10 × `--games` duels each (alternating first, the turn cap), against the 45-65% band.</item>
/// <item>The pool's mean over 20 drawn nights (seats 1-3 as the game draws them), against 55% ± 5.</item>
/// <item>The end chair against 50% ± 5 once Remy has it, else the band (Junia, decision 2).</item>
/// <item>Comparison rows: the QA save's best deck (`--option`, `--load`), and the mirror (each build against the end chair's).</item>
/// <item>The speed figures per build against its base, measured the same way (average winning turn, wins by turn 2).</item>
/// </list>
/// </summary>
public static partial class AlleyStudy
{
    /// <summary>Section 4.7 / 13's harness targets.</summary>
    public const double BandLow = 0.45, BandHigh = 0.65, PoolTarget = 0.55, PoolTolerance = 0.05, ChairTarget = 0.50, ChairTolerance = 0.05;

    private readonly record struct Res(int Winner, int Turns, bool Capped, int First);

    /// <summary>One batch job: <paramref name="Duels"/> duels of you (the stand-in or a CPU) against one opponent.</summary>
    private sealed record Job(string Label, AlleyOpponent Opp, int Duels, int SeedBase, Personality You, Deck Mine, Deck? Theirs = null);

    /// <summary>Runs every job's duels in parallel (alternating who goes first), each result kept by job.</summary>
    private static Res[][] RunBatch(ICardDataProvider cards, IScriptProvider scripts, AlleyContext ctx, IReadOnlyList<Job> jobs)
    {
        var results = jobs.Select(j => new Res[j.Duels]).ToArray();
        var work = jobs.SelectMany((j, ji) => Enumerable.Range(0, j.Duels).Select(g => (ji, g))).ToList();
        Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, w =>
        {
            var j = jobs[w.ji];
            int first = w.g % 2;
            var theirs = j.Theirs?.Clone() ?? BackAlley.DeckOf(j.Opp, ctx);
            var them = BackAlley.PersonalityOf(j.Opp, ctx);
            var (winner, turns, capped) = StudyDuel(cards, scripts, j.SeedBase + w.g, first, j.You, j.Mine.Clone(), them, theirs);
            results[w.ji][w.g] = new Res(winner, turns, capped, first);
        });
        return results;
    }

    private sealed class Tally
    {
        public int Won, Lost, Draws, Stopped, AtCap, FirstTurnLoss, LostBy2, WonBy2, N;
        public double WinTurnSum, LossTurnSum;
        public double P => Won / (double)Math.Max(1, Won + Lost + Draws);
        public double Se => Math.Sqrt(P * (1 - P) / Math.Max(1, Won + Lost + Draws));
        public double AvgLossTurn => Lost == 0 ? 0 : LossTurnSum / Lost;
        public static Tally Of(IEnumerable<Res> rs)
        {
            var r = new Tally();
            foreach (var x in rs)
            {
                r.N++;
                if (x.Winner == 0) { r.Won++; r.WinTurnSum += x.Turns; if (x.Turns <= 2) r.WonBy2++; }
                else if (x.Winner == 1) { r.Lost++; r.LossTurnSum += x.Turns; if (x.Turns <= 2) r.LostBy2++; if (x.Turns <= 1 && x.First == 1) r.FirstTurnLoss++; }
                else if (x.Winner == 2) r.Draws++;
                else r.Stopped++;
                if (x.Capped) r.AtCap++;
            }
            return r;
        }
        public string Pct(int x) => $"{100.0 * x / Math.Max(1, N):0.#}%";
    }

    private static Deck? BestFromSave(string saveFolder, AlleyContext ctx)
    {
        if (!Directory.Exists(Path.Combine(saveFolder, "decks"))) return null;
        return Directory.GetFiles(Path.Combine(saveFolder, "decks"), "*.ydk").Select(f => { var d = Deck.LoadYdk(f); d.Name = Path.GetFileNameWithoutExtension(f); return d; })
            .OrderByDescending(d => ctx.Ratings.TryGetValue(Playbooks.RecipeOf(d), out var r) ? r : 0).FirstOrDefault();
    }

    /// <summary>A deck made legal for a list (Loaners.Adjust, no Side), its recipe note kept so its playbook stays.</summary>
    private static Deck LegalFor(Deck d, Banlist list, ICardDataProvider cards)
    {
        var legal = Loaners.Adjust(d, list, cards).Deck;
        legal.Side.Clear();
        legal.Name = d.Name;
        if (d.Note("recipe").Length > 0) legal.Notes["recipe"] = d.Note("recipe");
        return legal;
    }

    /// <summary>
    /// The No Limits tier of `--alley-study`. <paramref name="qaBest"/> is the QA save's best deck (null: no comparison row).
    /// Prints one line per measurement with its target beside it; returns the number of measurements off target.
    /// </summary>
    private static int NoLimitsStudy(ICardDataProvider cards, IScriptProvider scripts, string root, int seed, int games, Deck? qaBest, string qaLabel,
        AlleyTest.Setup a, PrintingIndex printings, List<Personality> roster, Library library, Deck starter, Personality you)
    {
        var timer = Stopwatch.StartNew();
        var nl = AlleyTest.ReadyNoLimits(a, cards, null, anyWave: true);
        var data = nl.Data;
        var ctx = nl.Ctx;
        int ti = data.IndexOf("no-limits");
        var t = data.Tables[ti];
        var list = Alley.NoLimitsList(cards);
        int off = 0;
        string Flag(bool bad) { if (bad) off++; return bad ? "OFF" : "ok"; }

        // ---- The reference deck ----
        var roster0 = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName));
        var legalRefs = roster0.OrderByDescending(r => r.Value).ThenBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => (name: r.Key, elo: r.Value, deck: ctx.LoadDeck(r.Key))).Where(x => x.deck != null)
            .Where(x => { var d = x.deck!.Clone(); d.Side.Clear(); return DeckRules.Validate(d, list, cards).Count == 0; }).Take(3).ToList();
        var reference = legalRefs[0];
        var refDeck = LegalFor(reference.deck!, list, cards);
        Console.WriteLine($"\n{t.Name} ({t.Tier}, stake {Ante.StakeFor(t.Tier):N0} held, no list): seats 1-3 from a pool of {t.Pool.Count - t.Headliners.Count} builds, seat 4 the end chair; "
            + $"the table is {(a.Data.Tables[ti].Ready ? "ready" : "ready: no (the study measures every build whose deck exists, which is what decides ready:)")}");
        Console.WriteLine($"  Reference (your best No Limits deck): {reference.name} ({reference.elo:0}), the highest-rated deck legal under the No Limits list as built "
            + $"(next: {string.Join(", ", legalRefs.Skip(1).Select(x => $"{x.name} {x.elo:0}"))}); played by {you.Name} (skill {you.Skill:0.00}, planner on)");
        if (qaBest != null)
            Console.WriteLine($"  QA save's best: {qaBest.Name} (rated {(roster0.TryGetValue(Playbooks.RecipeOf(qaBest), out var qr) ? qr.ToString("0") : "?")}), made legal for the No Limits list; comparison rows only");
        Console.WriteLine($"  Targets: any build {BandLow:P0}-{BandHigh:P0}; seats 1-3 {PoolTarget:P0} ± {PoolTolerance * 100:0} (the pool's mean over 20 drawn nights); end chair {ChairTarget:P0} ± {ChairTolerance * 100:0} "
            + $"(Junia: the band only, spec decision 2); {10 * games} duels a build, {games} a drawn seat, turn cap {AlleyEconomy.AlleyTurns}, seed {seed}. The table's elo: window {t.MinElo:0}-{t.MaxElo:0} "
            + $"(spec: R - 110 to R + 35 = {reference.elo - 110:0}-{reference.elo + 35:0} for seats 1-3 against R {reference.elo:0}).");

        // ---- The builds, the nights, the end chair ----
        AlleyOpponent Opp(Headliner h, int seat, bool chair) => new AlleyOpponent
        {
            Table = t.Id, Seat = seat, Night = 0, Kind = chair ? "headliner" : BackAlley.BuildKind, Character = h.Id, Handle = h.Handle, Base = h.Deck, IsBuild = true, Skill = 1.0,
            Elo = ctx.Ratings.TryGetValue(h.Deck, out var e) ? e : 0, Binder = "nl:" + h.Id,
        };
        var builds = data.Builds.Where(b => b.Deck.Length > 0 && ctx.LoadDeck(b.Deck) != null).ToList();
        var chairBuild = t.Headliners.Select(data.FindBuild).FirstOrDefault(b => b != null && b.Deck.Length > 0 && ctx.LoadDeck(b.Deck) != null) ?? builds[0];
        bool chairIsRemy = chairBuild.Id == "remy";
        var nightsProfile = AlleyTest.NoLimitsOpen(starter, cards, printings, library, (uint)seed * 2654435761u + 71);
        var nights = new List<(int night, AlleyOpponent o)>();
        for (int night = 0; night < 20; night++)
            foreach (var o in AlleyTest.NightAt(nightsProfile, nl, ti, night).Where(o => o.Seat < BackAlley.Matches)) nights.Add((night, o));
        var seatedCount = nights.GroupBy(x => x.o.Character).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine($"  Builds: {string.Join(", ", builds.Select(b => $"{b.Handle} ({b.Id}, {b.Deck} {(ctx.Ratings.TryGetValue(b.Deck, out var r) ? r.ToString("0") : "unrated")}, wave {b.Wave}{(a.Data.FindBuild(b.Id)?.Ready == true ? "" : ", ready: no")})"))}; "
            + $"the end chair is {chairBuild.Handle}; 20 drawn nights seat {nights.Count} pool seats ({string.Join(", ", seatedCount.OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value}"))})");

        var yoursList = new List<(string label, Deck deck)> { ("reference", refDeck) };
        if (qaBest != null) yoursList.Add(("QA best", LegalFor(qaBest, list, cards)));
        var direct = new Dictionary<string, Tally>();           // "<label>/<build id>"
        var allRefResults = new List<Res>();
        for (int yi = 0; yi < yoursList.Count; yi++)
        {
            var (label, mine) = yoursList[yi];
            // Direct: every build, 10 × games; and the 20 nights' pool seats, games each (one batch, so the cores stay busy).
            var jobs = builds.Select((b, bi) => new Job("direct " + b.Id, Opp(b, b == chairBuild ? 4 : 1, b == chairBuild), 10 * games, seed * 1000003 + bi * 10007, you, mine)).ToList();
            int nightJobs0 = jobs.Count;
            jobs.AddRange(nights.Select((x, k) => new Job($"night {x.night} seat {x.o.Seat} {x.o.Character}", x.o, games, seed * 1000003 + 500000 + k * 101, you, mine)));
            var res = RunBatch(cards, scripts, ctx, jobs);
            Console.WriteLine($"\n  {(yi == 0 ? "Reference" : "Comparison")}: {label} ({mine.Name}) against each build, played directly:");
            Console.WriteLine($"  {"build",-26} {"rated",6} {"won",7} {"of",5} {"± err",7}  {"band",9}  turn-40  draws  1st-turn  lost-by-2  its avg win turn");
            for (int bi = 0; bi < builds.Count; bi++)
            {
                var b = builds[bi];
                var r = Tally.Of(res[bi]);
                direct[$"{label}/{b.Id}"] = r;
                if (yi == 0) allRefResults.AddRange(res[bi]);
                bool bad = r.P < BandLow || r.P > BandHigh;
                if (yi == 0 && b != chairBuild) { /* counted below with the pool */ }
                Console.WriteLine($"  {(yi == 0 ? Flag(bad) : bad ? "out" : "in "),-4}{b.Handle + " (" + b.Id + ")" + (b == chairBuild ? " chair" : ""),-22} {(ctx.Ratings.TryGetValue(b.Deck, out var er) ? er : 0),6:0} {r.P,7:P1} {r.N,5} {"± " + r.Se.ToString("P1"),7}  "
                    + $"{(bad ? "OUTSIDE" : "inside"),9}  {r.Pct(r.AtCap),7}  {r.Pct(r.Draws),5}  {r.Pct(r.FirstTurnLoss),8}  {r.Pct(r.LostBy2),9}  {r.AvgLossTurn,5:0.0}");
            }
            var poolRes = Tally.Of(res.Skip(nightJobs0).SelectMany(x => x));
            var weighted = builds.Where(b => seatedCount.ContainsKey(b.Id)).Select(b => (w: seatedCount[b.Id], r: direct[$"{label}/{b.Id}"])).ToList();
            double wMean = weighted.Sum(x => x.w * x.r.P) / Math.Max(1, weighted.Sum(x => x.w));
            double wSe = Math.Sqrt(weighted.Sum(x => x.w * x.w * x.r.Se * x.r.Se)) / Math.Max(1, weighted.Sum(x => x.w));
            bool poolBad = Math.Abs(poolRes.P - PoolTarget) > PoolTolerance;
            bool wBad = Math.Abs(wMean - PoolTarget) > PoolTolerance;
            Console.WriteLine($"  {(yi == 0 ? Flag(poolBad) : poolBad ? "out" : "in "),-4}pool, 20 drawn nights ({nights.Count} seats, {games} duels each): won {poolRes.P:P1} of {poolRes.N} (± {poolRes.Se:P1}) (target {PoolTarget:P0} ± {PoolTolerance * 100:0}); "
                + $"decided at turn 40 {poolRes.Pct(poolRes.AtCap)}, draws {poolRes.Pct(poolRes.Draws)}, first-turn losses {poolRes.Pct(poolRes.FirstTurnLoss)}");
            Console.WriteLine($"      the same nights from the direct rows ({weighted.Sum(x => x.w)} seats weighted by how often each build sat; {10 * games} duels a build): {wMean:P1} (± {wSe:P1}) {(wBad ? "(outside the target)" : "(inside)")}");
            var chair = direct[$"{label}/{chairBuild.Id}"];
            double ct = chairIsRemy ? ChairTarget : 0;
            bool chairBad = chairIsRemy ? Math.Abs(chair.P - ChairTarget) > ChairTolerance : chair.P < BandLow || chair.P > BandHigh;
            Console.WriteLine($"  {(yi == 0 ? Flag(chairBad) : chairBad ? "out" : "in "),-4}end chair {chairBuild.Handle} ({chairBuild.Id}): won {chair.P:P1} of {chair.N} (± {chair.Se:P1}) "
                + $"(target {(chairIsRemy ? $"{ChairTarget:P0} ± {ChairTolerance * 100:0}" : $"{BandLow:P0}-{BandHigh:P0}, Junia's band only until Remy takes the chair")}); decided at turn 40 {chair.Pct(chair.AtCap)}, draws {chair.Pct(chair.Draws)}, first-turn losses {chair.Pct(chair.FirstTurnLoss)}");
            if (yi == 0)
            {
                // The band is the ship gate for every build: the number of builds inside it.
                int inBand = builds.Count(b => direct[$"reference/{b.Id}"].P is >= BandLow and <= BandHigh);
                Console.WriteLine($"      {inBand} of {builds.Count} builds inside {BandLow:P0}-{BandHigh:P0}; the ship gate needs Junia and at least {BackAlley.NoLimitsShipPool} pool builds inside, the pool mean {PoolTarget:P0} ± {PoolTolerance * 100:0}, and --endless-test");
            }
        }

        // ---- The mirror: each build (a CPU at full strength) against the end chair's build ----
        {
            var chairOpp = Opp(chairBuild, 4, chair: true);
            var chairDeck = BackAlley.DeckOf(chairOpp, ctx);
            var mirrorJobs = builds.Where(b => b != chairBuild).Select((b, bi) =>
            {
                var o = Opp(b, 1, chair: false);
                return new Job("mirror " + b.Id, chairOpp, 10 * games, seed * 1000003 + 900000 + bi * 10007, BackAlley.PersonalityOf(o, ctx), BackAlley.DeckOf(o, ctx));
            }).ToList();
            var mirror = RunBatch(cards, scripts, ctx, mirrorJobs);
            Console.WriteLine($"\n  Mirror (comparison): each build against the end chair's build ({chairBuild.Handle}, {chairBuild.Deck}), both CPUs at full strength, {10 * games} duels each:");
            for (int k = 0; k < mirrorJobs.Count; k++)
            {
                var r = Tally.Of(mirror[k]);
                var b = builds.Where(x => x != chairBuild).ElementAt(k);
                double eloGap = (ctx.Ratings.TryGetValue(b.Deck, out var be) ? be : 0) - (ctx.Ratings.TryGetValue(chairBuild.Deck, out var ce) ? ce : 0);
                Console.WriteLine($"      {b.Handle + " (" + b.Id + ")",-24} won {r.P,6:P1} of {r.N} (± {r.Se:P1}) against {chairBuild.Handle}; Elo odds from the ratings {1 / (1 + Math.Pow(10, -eloGap / 400)):P0}");
            }
        }

        // ---- Speed: each build against its base, the same duels against the reference ----
        {
            Console.WriteLine($"\n  Speed gate (spec 4.7), per build against its base, both played by the CPU against the reference ({10 * games} duels each, the same seeds): "
                + "average winning turn at least 3 and at most 1 turn faster than its base; wins by turn 2 at most max(10%, the base's rate + 5 points).");
            Console.WriteLine($"  {"build",-22} {"won",14} {"avg win turn",18} {"wins by turn 2",20}   verdict");
            var jobsB = new List<Job>();
            foreach (var (b, bi) in builds.Select((b, bi) => (b, bi)))
            {
                var o = Opp(b, 1, b == chairBuild);
                jobsB.Add(new Job("build " + b.Id, o, 10 * games, seed * 1000003 + bi * 10007, you, refDeck));
                var baseOpp = new AlleyOpponent { Table = t.Id, Seat = 1, Kind = BackAlley.BuildKind, Character = b.Id, Handle = b.Handle, Base = b.Base, IsBuild = true, Skill = 1.0, Binder = "nl:" + b.Id };
                jobsB.Add(new Job("base " + b.Id, baseOpp, 10 * games, seed * 1000003 + bi * 10007, you, refDeck));
            }
            var speed = RunBatch(cards, scripts, ctx, jobsB);
            int speedBad = 0;
            double lostBy2Overall = 0, lostOverall = 0;
            for (int k = 0; k < builds.Count; k++)
            {
                var b = builds[k];
                var rb = speed[2 * k]; var rs = speed[2 * k + 1];
                // The CPU's side: its wins are the reference's losses.
                var cpu = rb.Select(x => x.Winner == 0 ? x with { Winner = 1 } : x.Winner == 1 ? x with { Winner = 0 } : x).ToList();
                var cpuBase = rs.Select(x => x.Winner == 0 ? x with { Winner = 1 } : x.Winner == 1 ? x with { Winner = 0 } : x).ToList();
                var build = Tally.Of(cpu); var baseR = Tally.Of(cpuBase);
                lostBy2Overall += build.WonBy2; lostOverall += build.N;
                double avg = build.Won == 0 ? 0 : build.WinTurnSum / build.Won, avgBase = baseR.Won == 0 ? 0 : baseR.WinTurnSum / baseR.Won;
                double by2 = build.WonBy2 / (double)Math.Max(1, build.N), by2Base = baseR.WonBy2 / (double)Math.Max(1, baseR.N);
                bool slow3 = avg > 0 && avg < 3, faster = avg > 0 && avgBase > 0 && avgBase - avg > 1, early = by2 > Math.Max(0.10, by2Base + 0.05);
                bool bad = slow3 || faster || early;
                if (bad) speedBad++;
                Console.WriteLine($"  {(bad ? "OFF " : "ok  ")}{b.Handle + " (" + b.Id + ")",-20} {build.P,6:P0} / {baseR.P,5:P0}   {avg,5:0.0} / {avgBase,5:0.0} ({(avg - avgBase):+0.0;-0.0})   {by2,6:P1} / {by2Base,6:P1}   "
                    + $"{(bad ? string.Join(", ", new[] { slow3 ? "average win turn under 3" : "", faster ? "more than 1 turn faster than its base" : "", early ? "too many wins by turn 2" + (b.Id == "sasha" ? " (Tenpai goes second and wins on turn 2 by design: judged against its base)" : "") : "" }.Where(x => x.Length > 0)) : "within the gate")}");
            }
            off += speedBad;
            double lostBy2 = lostBy2Overall / Math.Max(1, lostOverall);
            Console.WriteLine($"  {Flag(lostBy2 > 0.10)}   the reference loses {lostBy2:P1} of its duels to the builds by turn 2 (over 10% would make NoLimitsGuardTurns 2 the lever; it is {AlleyEconomy.NoLimitsGuardTurns} now); "
                + $"first-turn losses overall {Tally.Of(allRefResults).Pct(Tally.Of(allRefResults).FirstTurnLoss)}");
        }
        Console.WriteLine($"\n  No Limits: {off} measurement(s) off target   ({timer.Elapsed.TotalMinutes:0.0} min)");
        return off;
    }
}
