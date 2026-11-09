using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--pack-ev [--seed N] [--games N]` (card versions phase 2, spec 7 loop 3): the same seeded packs of every booster set opened
/// with the 4th-copy rule as it was (<see cref="Collection.KeepLoneText"/> off) and as it is now (on), in three states of the
/// collection: from nothing, a set finished with current-text copies (an old save's, or gifts), and a set finished by its own
/// packs (each copy stamped as its pack stamps it). It reports the DP refunded for 4th copies, what they sold for before the
/// pack's cap, the value of the collection afterwards, and how many originals it holds. The rule is DP-neutral or negative when
/// the refund with it never exceeds the refund without it, and never exceeds half a pack's price. Pack contents are the same in
/// both runs (the check compares them): only which copy a 4th copy displaces changes.
/// </summary>
public static class PackEv
{
    private sealed class Tally
    {
        public int Sets, Packs, Spent, Fourths, Sold, Refund, ValueKept, Originals, OriginalsPulled, OverCap;
        public void Add(Tally t)
        {
            Sets += t.Sets; Packs += t.Packs; Spent += t.Spent; Fourths += t.Fourths; Sold += t.Sold; Refund += t.Refund;
            ValueKept += t.ValueKept; Originals += t.Originals; OriginalsPulled += t.OriginalsPulled; OverCap += t.OverCap;
        }
    }

    private sealed class SetRun
    {
        public string Code = "";
        public Tally T = new Tally();
        public string Pulls = "";
    }

    public static int Run(ICardDataProvider cards, string root, int seed, int packsPer)
    {
        var printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        var texts = VersionSetup.Load(cards, root);
        var catalog = new PackCatalog(printings, cards);
        var sets = catalog.Sets.Where(s => s.Price > 0).ToList();
        Rarity Commonest(uint c) => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
        packsPer = Math.Max(10, packsPer);
        var timelineCards = texts.Rows.Where(r => r.Collectible).Select(r => r.Base).ToHashSet();
        Console.WriteLine($"Pack EV: {sets.Count} booster sets, {packsPer} seeded packs each per state, seed {seed}; {texts.Rows.Count()} text rows ({timelineCards.Count} cards with an original); "
                          + $"{sets.Count(s => s.Pool.Values.SelectMany(x => x).Any(timelineCards.Contains))} sets hold at least one such card.");

        SetRun One(PackSet set, string state, bool keep)
        {
            var run = new SetRun { Code = set.Info.Code };
            var p = new Profile { Dp = 1_000_000_000 };
            p.Collection.Salt = Hash.Mix((uint)seed, CardUnlocks.StableHash(set.Info.Code));
            p.Collection.CommonestOf = Commonest;
            p.Collection.Texts = texts;
            p.Collection.Printings = printings;
            if (state != "empty")
            {
                // A set finished: 3 copies of every card in it, bought in packs (Paid is its share: 2 x its sell value, as an old save's copy).
                foreach (var code in set.Pool.Values.SelectMany(x => x).Distinct().OrderBy(c => c))
                {
                    var rarity = set.Pool.Where(r => r.Value.Contains(code)).Select(r => r.Key).OrderBy(r => r).First();
                    var (setCode, printed) = state == "own packs" ? Profile.PackPrint(set, new Pull(code, rarity), printings) : ("", "");
                    for (int i = 0; i < Collection.MaxCopies; i++) p.Collection.AddPulled(code, rarity, CopySource.Pack, -1, setCode, printed);
                }
            }
            int valueBefore = p.Collection.All.Sum(c => p.Collection.Value(c));
            var pulls = new System.Text.StringBuilder();
            bool saved = Collection.KeepLoneText;
            for (int k = 0; k < packsPer; k++)
            {
                var rng = new Random(unchecked((int)Hash.Mix((uint)seed, CardUnlocks.StableHash(set.Info.Code), (uint)k)));
                var opened = p.OpenPack(set, rng, out var refund);
                run.T.Packs++;
                run.T.Spent += set.Price;
                run.T.Refund += refund;
                if (refund > set.Price / 2) run.T.OverCap++;
                foreach (var o in opened)
                {
                    pulls.Append(o.pull.Code).Append('/').Append((int)o.pull.Rarity).Append(' ');
                    run.T.Sold += o.added.Dp;
                    if (!o.added.Kept || o.added.Replaced != null) run.T.Fourths++;
                    if (o.added.Copy is CardCopy c && c.Text != 0) run.T.OriginalsPulled++;
                }
            }
            run.T.Sets = 1;
            run.T.ValueKept = p.Collection.All.Sum(c => p.Collection.Value(c)) - valueBefore;
            run.T.Originals = p.Collection.All.Count(c => c.Text != 0);
            run.Pulls = pulls.ToString();
            return run;
        }

        var saved = Collection.KeepLoneText;
        int failures = 0;
        var states = new[] { "empty", "current copies", "own packs" };
        var summary = new List<string>();
        try
        {
            foreach (var state in states)
            {
                var results = new Dictionary<bool, SetRun[]>();
                foreach (var keep in new[] { false, true })
                {
                    Collection.KeepLoneText = keep;
                    var runs = new SetRun[sets.Count];
                    Parallel.For(0, sets.Count, i => runs[i] = One(sets[i], state, keep));
                    results[keep] = runs;
                }
                Collection.KeepLoneText = saved;
                var off = results[false];
                var on = results[true];
                bool samePulls = off.Zip(on, (a, b) => a.Pulls == b.Pulls).All(x => x);
                // The sets whose packs hold an original text at all (the rule can only act there).
                var affected = Enumerable.Range(0, sets.Count).Where(i => on[i].T.OriginalsPulled > 0 || off[i].T.OriginalsPulled > 0 || state != "empty" && on[i].T.Originals > 0).ToList();
                Tally Sum(SetRun[] r, IEnumerable<int> idx) { var t = new Tally(); foreach (var i in idx) t.Add(r[i].T); return t; }
                var all = Enumerable.Range(0, sets.Count).ToList();
                Console.WriteLine($"\n== Collection state: {state} ({(state == "empty" ? "opening from nothing, 4th copies as they come" : state == "current copies" ? "every card of the set owned x3 as current-text copies, paid 2 x sell value" : "every card of the set owned x3 as its own pack stamps it")}) ==");
                Console.WriteLine($"  the same packs in both runs: {(samePulls ? "yes" : "NO")} ({sets.Count} sets x {packsPer} packs)");
                if (!samePulls) failures++;
                foreach (var (label, idx) in new[] { ($"all {sets.Count} sets", all), ($"{affected.Count} sets with an original in their packs", affected) })
                {
                    var o = Sum(off, idx); var n = Sum(on, idx);
                    Console.WriteLine($"  {label}:");
                    Console.WriteLine($"    {"",-22} {"packs",7} {"spent DP",13} {"4th copies",11} {"sold (raw)",12} {"refunded",12} {"refund/spent",12} {"value kept",13} {"originals held",15}");
                    foreach (var (mode, t) in new[] { ("as it was (off)", o), ("keep lone text (on)", n) })
                        Console.WriteLine($"    {mode,-22} {t.Packs,7:N0} {t.Spent,13:N0} {t.Fourths,11:N0} {t.Sold,12:N0} {t.Refund,12:N0} {(t.Spent == 0 ? 0 : t.Refund / (double)t.Spent),12:P2} {t.ValueKept,13:N0} {t.Originals,15:N0}");
                    Console.WriteLine($"    {"change",-22} {"",7} {"",13} {n.Fourths - o.Fourths,11:+#,0;-#,0;0} {n.Sold - o.Sold,12:+#,0;-#,0;0} {n.Refund - o.Refund,12:+#,0;-#,0;0} {"",12} {n.ValueKept - o.ValueKept,13:+#,0;-#,0;0} {n.Originals - o.Originals,15:+#,0;-#,0;0}");
                    summary.Add($"{state} / {label}: refund {o.Refund:N0} -> {n.Refund:N0} ({n.Refund - o.Refund:+#,0;-#,0;0}), value kept {o.ValueKept:N0} -> {n.ValueKept:N0}, originals held {o.Originals:N0} -> {n.Originals:N0}");
                }
                int moreRefund = Enumerable.Range(0, sets.Count).Count(i => on[i].T.Refund > off[i].T.Refund);
                int lessRefund = Enumerable.Range(0, sets.Count).Count(i => on[i].T.Refund < off[i].T.Refund);
                int overCap = on.Sum(r => r.T.OverCap) + off.Sum(r => r.T.OverCap);
                var worst = Enumerable.Range(0, sets.Count).OrderByDescending(i => on[i].T.Refund - off[i].T.Refund).Take(4).Where(i => on[i].T.Refund > off[i].T.Refund)
                    .Select(i => $"{sets[i].Info.Code} +{on[i].T.Refund - off[i].T.Refund} (of {on[i].T.Spent / 2:N0} cap)").ToList();
                var sold = Enumerable.Range(0, sets.Count).Count(i => on[i].T.Sold > off[i].T.Sold);
                Console.WriteLine($"  per set: refund up in {moreRefund}, down in {lessRefund}, equal in {sets.Count - moreRefund - lessRefund}; raw sales (before the pack's cap) up in {sold}; packs over half their price: {overCap}"
                                  + (worst.Count > 0 ? $"; largest rises: {string.Join(", ", worst)}" : ""));
                var totalOn = Sum(on, all); var totalOff = Sum(off, all);
                bool ok = overCap == 0 && totalOn.Refund <= totalOff.Refund;
                Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {state}: refund with the rule {totalOn.Refund:N0} against {totalOff.Refund:N0} without: {(totalOn.Refund <= totalOff.Refund ? "DP-neutral or negative" : "DP-POSITIVE")}; no pack refunds more than half its price");
                if (!ok) failures++;
            }
        }
        finally { Collection.KeepLoneText = saved; }
        Console.WriteLine("\nSummary:");
        foreach (var l in summary) Console.WriteLine("  " + l);
        Console.WriteLine($"\n{(failures == 0 ? "The 4th-copy rule is DP-neutral or negative in every state." : $"{failures} pack-EV checks failed.")}");
        return failures == 0 ? 0 : 1;
    }
}
