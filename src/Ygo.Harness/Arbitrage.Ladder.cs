using Ygo.Engine.AI;
using Ygo.Engine.Campaign;

namespace Ygo.Harness;

/// <summary>
/// The Champion's Ladder's loops (docs/dev/CHAMPIONS-LADDER-SPEC-2026-11-06.md sections 5 and 9): climb everything (Ilse's
/// sitting restarted twice on the way, the visitors on), 50 rematches, then New Game+ and climb again. Ladder DP must stay
/// within 16,200 + 150 a win that isn't a first climb, nothing is spent, and New Game+ pays no first climb. Then the
/// duel-paid farms at a realistic champion level, level-up DP included, against the 300 DP a duel cap: rematches, a
/// sitting won three times and lost, and losses. Run by `--ladder-test`; chunk 1b adds the call to <see cref="Run"/>.
/// </summary>
public static partial class Arbitrage
{
    /// <summary>The ladder's loops; the number that fail (0: none is DP-positive or over the cap).</summary>
    public static int LadderLoops(string root, List<Personality> roster, Circuits circuits, Library library, Ladder ladder, string? load = null)
    {
        int bad = 0;
        void Verdict(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) bad++;
        }
        Console.WriteLine("\nThe Champion's Ladder (arbitrage):");
        if (ladder.Rungs.Count == 0 || library.Rounds.Count == 0 || ladder.Opener == null)
        {
            Console.WriteLine("  FAIL no ladder, no Every Era final or no opener: nothing to measure");
            return 1;
        }
        const int Top = 50_000_000; // past level 99: no level-up DP
        int onceEver = ladder.Rungs.Count * Ladder.FirstClimbDp + ladder.Visitors.Count * Ladder.FirstClimbDp + ladder.Prize;

        Profile Veteran(int exp)
        {
            var p = new Profile { Exp = exp };
            for (int r = 0; r < library.Rounds.Count; r++) p.LibraryWins[library.FinalKey(r)] = 1;
            p.Wins[ladder.Opener!.Id] = 1;
            p.AlleyBrackets[Brackets.InvitationalKind] = new LocalsRecord { Entries = 1, Titles = 1, Best = 0 };
            return p;
        }
        int LadderDp(Profile p) => p.DpEarned.GetValueOrDefault(Ladder.LedgerKind) + p.DpEarned.GetValueOrDefault(Ledger.Prize(Ladder.LedgerKind));
        int LossDp(Profile p) => p.DpEarned.GetValueOrDefault(Ledger.Loss(Ladder.LedgerKind));

        // 1. Climb everything, 50 rematches, New Game+, climb again (at the top level: duel DP only).
        {
            var p = Veteran(Top);
            var s = new LadderState();
            int firsts = 0, others = 0, losses = 0, guard = 0, sittingRestarts = 0;
            void Win(LadderSeat seat)
            {
                var r = ladder.Record(p, s, library, seat, 0);
                if (r == null) return;
                if (r.Reward.FirstWin) firsts++; else others++;
            }
            while (ladder.Next(p, s, library) is LadderSeat next && guard++ < 200)
            {
                // Ilse's sitting: three wins and a loss, twice, before it's won (the sitting's 150s are capped as rematches).
                if (next.IsSitting && sittingRestarts < 2)
                {
                    for (int k = 0; k < next.Decks.Count - 1; k++) Win(next);
                    ladder.Record(p, s, library, next, 1);
                    losses++;
                    sittingRestarts++;
                    continue;
                }
                Win(next);
            }
            int climbDp = LadderDp(p);
            bool top = ladder.HoldsTop(s) && Ladder.TopReached(s);
            Verdict($"the climb: {firsts} first wins ({ladder.Rungs.Count} rungs, {ladder.Visitors.Count} visitors), {others} sitting wins, {losses} losses: "
                    + $"{climbDp:N0} ladder DP + {LossDp(p)} loss DP (once-ever pay {onceEver:N0} + 150 x {others})",
                top && firsts == ladder.Rungs.Count + ladder.Visitors.Count && climbDp <= onceEver + Ladder.RepeatWinDp * others);
            var seats = ladder.Rungs.Concat(ladder.Visitors).ToList();
            int rematch0 = LadderDp(p), rematches = 0;
            for (int d = 0; d < 50; d++)
            {
                var r = ladder.Record(p, s, library, seats[d % seats.Count], 0);
                if (r != null && r.Rematch && !r.Reward.FirstWin) rematches++;
            }
            int rematchDp = LadderDp(p) - rematch0;
            Verdict($"50 rematches pay {rematchDp:N0} DP ({rematchDp / 50.0:0} a duel; {rematches} recorded as rematches)", rematches == 50 && rematchDp == 50 * Ladder.RepeatWinDp);
            int prize0 = p.DpEarned.GetValueOrDefault(Ledger.Prize(Ladder.LedgerKind)), tokens0 = p.Tokens, ng0 = LadderDp(p);
            p.NewGamePlus();
            int ngFirsts = 0, ngWins = 0, ngTops = 0;
            foreach (var seat in seats)
            {
                var r = ladder.Record(p, s, library, seat, 0);
                if (r == null) continue;
                ngWins++;
                if (r.Reward.FirstWin || r.RungTaken > 0 || r.VisitorBeaten) ngFirsts++;
                if (r.TopReached) ngTops++;
            }
            int ngDp = LadderDp(p) - ng0;
            Verdict($"after New Game+ the ladder is still held, and climbing it again is {ngWins} rematches: {ngDp:N0} DP, {ngFirsts} first climbs, {ngTops} tops, "
                    + $"prize line {p.DpEarned.GetValueOrDefault(Ledger.Prize(Ladder.LedgerKind)) - prize0:+0;-0}, tokens {p.Tokens - tokens0:+0;-0}",
                ladder.HoldsTop(s) && ngWins == seats.Count && ngFirsts == 0 && ngTops == 0 && ngDp == ngWins * Ladder.RepeatWinDp
                && p.DpEarned.GetValueOrDefault(Ledger.Prize(Ladder.LedgerKind)) == prize0 && p.Tokens == tokens0);
            int allOthers = others + rematches + ngWins;
            Verdict($"in all: {LadderDp(p):N0} ladder DP against the cap {onceEver:N0} + 150 x {allOthers} = {onceEver + Ladder.RepeatWinDp * allOthers:N0}; "
                    + $"nothing spent ({p.DpSpent.Values.Sum()} DP); the ledger balances",
                LadderDp(p) <= onceEver + Ladder.RepeatWinDp * allOthers && p.DpSpent.Values.Sum() == 0 && Ledger.Net(p) == p.Dp - Profile.StartingDp);
        }

        // 2. Duel-paid DP at a realistic champion level, level-ups included (cap 300 DP a duel).
        int champExp = 0;
        load ??= new[] { Path.Combine(root, "build", "proof-economy", "champion"), Path.Combine(root, "build", "qa-night5", "mods") }
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "save", "profile.txt")));
        if (load != null && Profile.Load(load.EndsWith(".txt") ? load : Path.Combine(load, "save", "profile.txt")) is { } saved) champExp = saved.Exp;
        var levels = new List<(string, int)> { ("level 30", ExpFor(30)), ("level 40", ExpFor(40)) };
        if (champExp > 0) levels.Insert(0, ($"the champion save (level {Leveling.LevelFor(champExp)})", champExp));
        foreach (var (label, exp) in levels)
        {
            // Rematches: climb to the top first (its first climbs pay level DP, once), then 200 rematch wins.
            var p = Veteran(exp);
            var s = new LadderState();
            for (int guard = 0; ladder.Next(p, s, library) is LadderSeat next && guard < 200; guard++) ladder.Record(p, s, library, next, 0);
            var seats = ladder.Rungs.Concat(ladder.Visitors).ToList();
            int duel0 = LadderDp(p), lv0 = p.DpEarned.GetValueOrDefault(Ledger.LevelUp), level0 = p.Level;
            for (int d = 0; d < 200; d++) ladder.Record(p, s, library, seats[d % seats.Count], 0);
            int duel = LadderDp(p) - duel0, lv = p.DpEarned.GetValueOrDefault(Ledger.LevelUp) - lv0;
            double perDuel = (duel + lv) / 200.0;
            Verdict($"{label}: 200 rematches pay {duel / 200.0:0} + {lv / 200.0:0} level DP = {perDuel:0} DP a duel (level {level0} -> {p.Level})", perDuel <= 300);

            // Ilse's sitting farmed: three wins and a loss, 50 times (200 duels), before her rung is taken.
            var q = Veteran(exp);
            var t = new LadderState();
            for (int guard = 0; ladder.Next(q, t, library) is LadderSeat next && !next.IsSitting && guard < 200; guard++) ladder.Record(q, t, library, next, 0);
            var ilse = ladder.Next(q, t, library);
            if (ilse == null || !ilse.IsSitting) { Verdict($"{label}: no sitting to farm", false); continue; }
            int sit0 = LadderDp(q) + LossDp(q), slv0 = q.DpEarned.GetValueOrDefault(Ledger.LevelUp), duels = 0;
            for (int k = 0; k < 50; k++)
            {
                for (int w = 0; w < ilse.Decks.Count - 1; w++) { ladder.Record(q, t, library, ilse, 0); duels++; }
                ladder.Record(q, t, library, ilse, 1);
                duels++;
            }
            int sit = LadderDp(q) + LossDp(q) - sit0, slv = q.DpEarned.GetValueOrDefault(Ledger.LevelUp) - slv0;
            double sitPer = (sit + slv) / (double)duels;
            Verdict($"{label}: Ilse's sitting won {ilse.Decks.Count - 1} times and lost, 50 times: {sit / (double)duels:0} + {slv / (double)duels:0} level DP = {sitPer:0} DP a duel, "
                    + $"rung {ilse.Rung} still not taken", sitPer <= 300 && ladder.Climbed(t) == ilse.Rung - 1);

            // Losses at the next rung: 200 of them.
            var u = Veteran(exp);
            var v = new LadderState();
            int loss0 = LossDp(u), llv0 = u.DpEarned.GetValueOrDefault(Ledger.LevelUp);
            for (int d = 0; d < 200; d++) ladder.Record(u, v, library, ladder.Rungs[0], 1);
            int loss = LossDp(u) - loss0, llv = u.DpEarned.GetValueOrDefault(Ledger.LevelUp) - llv0;
            Verdict($"{label}: 200 losses pay {loss / 200.0:0} + {llv / 200.0:0} level DP a duel, rung kept", (loss + llv) / 200.0 <= 300 && ladder.Climbed(v) == 0);
        }
        Console.WriteLine(bad == 0 ? "  No ladder loop is DP-positive or over the cap." : $"  {bad} ladder loops fail.");
        return bad;
    }
}
