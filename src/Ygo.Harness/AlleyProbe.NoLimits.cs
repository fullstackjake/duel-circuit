using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--alley-probe`'s No Limits section (docs/dev/NO-LIMITS-ECONOMY-2026-11-02.md "How it was measured": "port the probe into --alley-probe as a No
/// Limits section"): the binders of section 1.2 re-measured from the authored decks through the engine's own candidate rule
/// (<see cref="Ante.Binder"/> at the No Limits tier: the tier's floor and cap, <see cref="AlleyEconomy.NoLimitsGrades"/>, a printing weighted
/// 10000/Unit, ever-Forbidden cards 3×, led by the build's `stakes:`), over many salts; then section 1.4's night: duels, DP and card value at the targets.
/// </summary>
public static partial class AlleyProbe
{
    /// <summary>NO-LIMITS-ECONOMY 1.2's table, for the eye: (build fits, binder mean, ever-Forbidden share), by pilot, measured on the base decks plus Appendix B's picks.</summary>
    private static readonly Dictionary<string, (int fits, int mean, int share)> EconomyDoc = new Dictionary<string, (int, int, int)>
    {
        ["junia"] = (38, 3933, 46), ["anselm"] = (47, 3909, 34), ["tova"] = (33, 4016, 58), ["augie"] = (28, 3879, 16), ["sasha"] = (28, 3951, 56), ["imogen"] = (42, 3936, 43), ["rue"] = (36, 3966, 34),
    };

    private static int NoLimitsSection(ICardDataProvider cards, string root, int seed, int salts = 200)
    {
        int failures = 0;
        void Check(string what, bool ok) { Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}"); if (!ok) failures++; }
        var printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities")).ToList();
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var library = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), roster, circuits);
        var a = AlleyTest.Load(cards, root, printings, roster, library);
        var ctx = a.Ctx;
        var data = a.Data;
        var builds = data.Builds.Where(b => b.Deck.Length > 0 && ctx.LoadDeck(b.Deck) != null).ToList();
        var forbidden = new HashSet<uint>(ctx.Pool(9999));
        int floor = (int)Math.Ceiling(AlleyEconomy.StakeFloor * AlleyEconomy.NoLimitsStake);
        string Name(uint c) => cards.Get(c)?.Name ?? c.ToString();
        Console.WriteLine($"\n10. No Limits (NO-LIMITS-ECONOMY 1.2-1.4), from the authored decks (decks/no-limits), through the engine's binder rule: stakes {AlleyEconomy.NoLimitsStake:N0} DP held, "
            + $"a card worth {floor:N0}-{AlleyEconomy.NoLimitsCap:N0} at Raw 8 {AlleyEconomy.NoLimitsGrades[0].weight:P0}, Raw 9 {AlleyEconomy.NoLimitsGrades[1].weight:P0}, slab 9 {AlleyEconomy.NoLimitsGrades[2].weight:P0}, slab 10 {AlleyEconomy.NoLimitsGrades[3].weight:P0}; "
            + $"{salts} salts a build; {builds.Count} builds with a deck.");
        Console.WriteLine($"  {"build",-22} {"fits",9} {"binder mean",16} {"ever-Forbidden",18}  lead card(s) as staked (mean value, printings drawn)");
        var means = new List<double>();
        foreach (var b in builds)
        {
            var deck = ctx.LoadDeck(b.Deck)!;
            var codes = deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, cards)).Distinct().ToList();
            int fits = codes.Count(c => Ante.CanStake(c, AlleyTier.NoLimits, ctx));
            var all = new List<int>();
            int contraband = 0, stakes = 0, notFull = 0;
            var leadValues = b.Stakes.ToDictionary(c => c, _ => new List<int>());
            var leadPrintings = b.Stakes.ToDictionary(c => c, _ => new Dictionary<string, int>());
            for (uint k = 0; k < salts; k++)
            {
                var q = new Profile();
                q.Collection.Salt = Hash.Mix((uint)seed, 0x1D0Fu, k);
                var binder = Ante.Binder(q, "nl:" + b.Id, ctx);
                if (binder.Cards.Count != AlleyEconomy.BinderSize) notFull++;
                foreach (var st in binder.Cards)
                {
                    int v = Ante.Value(st, ctx);
                    all.Add(v);
                    stakes++;
                    if (forbidden.Contains(st.Code)) contraband++;
                    if (leadValues.TryGetValue(st.Code, out var lv))
                    {
                        lv.Add(v);
                        var label = $"{Rarities.Name(st.Rarity)} {(st.Slab > 0 ? "slab " + st.Slab : "Raw " + st.Grade)}";
                        leadPrintings[st.Code][label] = leadPrintings[st.Code].GetValueOrDefault(label) + 1;
                    }
                }
            }
            double mean = all.Count == 0 ? 0 : all.Average();
            means.Add(mean);
            EconomyDoc.TryGetValue(b.Id, out var doc);
            var leads = b.Stakes.Select(c => leadValues[c].Count == 0 ? $"{Name(c)}: never staked"
                : $"{Name(c)} {leadValues[c].Average():N0} ({string.Join(", ", leadPrintings[c].OrderByDescending(x => x.Value).Take(3).Select(x => x.Key))})");
            Console.WriteLine($"  {b.Handle + " (" + b.Id + ")",-22} {fits,3}{(doc.fits > 0 ? $" ({doc.fits})" : ""),-6} {mean,6:N0}{(doc.mean > 0 ? $" ({doc.mean:N0})" : ""),-9} {100.0 * contraband / Math.Max(1, stakes),5:0}%{(doc.share > 0 ? $" ({doc.share}%)" : ""),-8}  {string.Join("; ", leads)}"
                + (b.Ready ? "" : "  [ready: no]"));
            Check($"{b.Id}: every binder holds {AlleyEconomy.BinderSize} different cards ({salts - notFull} of {salts}), the first lead {Name(b.Stakes[0])} fits a printing, {fits} cards fit",
                notFull == 0 && b.Stakes.Count > 0 && Ante.CanStake(b.Stakes[0], AlleyTier.NoLimits, ctx) && fits >= AlleyEconomy.BinderSize);
        }
        Console.WriteLine($"  (in brackets, NO-LIMITS-ECONOMY 1.2's figures from the base decks plus Appendix B's picks)  mean of the binder means {means.Average():N0} (the doc: 3,946)");
        // 1.4: a night at the targets, from the measured binder mean.
        double binderMean = means.Average();
        int stake = AlleyEconomy.NoLimitsStake;
        Console.WriteLine($"  A night is 4 seats; you play each until you beat it (losses a seat (1 - p) / p). Card value a night: 4 × {binderMean:N0} = {4 * binderMean:N0}.");
        Console.WriteLine($"  {"player",-14} {"seats 1-3 / 4",14} {"duels a night",14} {"DP a night",11} {"value per DP lost",18}");
        foreach (var (who, p13, p4) in new[] { ("at the targets", 0.55, 0.50), ("typical", 0.45, 0.40), ("strong", 0.65, 0.60), ("every duel", 1.0, 1.0) })
        {
            double duels = 3 / p13 + 1 / p4, lost = 3 * (1 - p13) / p13 + (1 - p4) / p4, dp = lost == 0 ? 0 : -lost * stake;
            Console.WriteLine($"  {who,-14} {p13,6:P0} / {p4:P0} {duels,14:0.00} {dp,11:N0} {(lost > 0 ? (4 * binderMean / (lost * stake)).ToString("0.0") : "-"),18}");
            Check($"{who}: a No Limits night at {p13:P0} / {p4:P0} pays 0 DP a duel and never makes DP ({dp:N0} DP a night; held stakes come back on a win)", dp <= 0 && AlleyEconomy.DuelDp == 0);
        }
        return failures;
    }
}
