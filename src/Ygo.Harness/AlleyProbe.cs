using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--alley-probe [--seed N] [--games N]` (docs/dev/ALLEY-ECONOMY-2026-11-01.md): the Back Alley's DP numbers, measured before
/// it's built. Nothing here is in the engine yet, so the alley's rules are modeled here on the engine's own pieces (the
/// price function, the Sell desk, the Grading Desk, the Card Trader, the used market, the splash builder, the Elo odds):
/// <list type="bullet">
/// <item>win rates per event: the plan's targets, and the Elo odds of real decks against the splash decks' ratings;</item>
/// <item>each table's CPU binder (12 stakes) from real splash decks and real printings, valued by the one price;</item>
/// <item>DP and card value per duel at each table and win rate, with stakes held (back on a win) or as a plain fee;</item>
/// <item>the binder refill (farming one CPU against rotating), Orvel's Table and Moth's rail;</item>
/// <item>Moth's van: the contraband shelf, the used tab, what Moth pays;</item>
/// <item>the Contraband Pack and the two alley brackets (Alley Brawl, Midnight Open) by Elo odds;</item>
/// <item>every loop through the engine: won cards, grading, the Sell desk, the Trader, the rail, the shelf, the used tab,
/// and an exhaustive search over short sequences. Fails if any loop or bracket is DP-positive.</item>
/// </list>
/// </summary>
public static partial class AlleyProbe
{
    // ---- The knobs (recommended values; docs/dev/ALLEY-ECONOMY-2026-11-01.md) ------------------------------------------

    public sealed class Table
    {
        public string Name = "";
        /// <summary>DP held at the duel's start: back on a win or a draw, gone on a loss or a quit.</summary>
        public int Stake;
        /// <summary>The CPU's stake is worth at least Floor × Stake and at most Cap.</summary>
        public double Floor = 1.0;
        public int Cap;
        /// <summary>How the binder's copies come: (raw grade or slab grade, is slab, weight).</summary>
        public (int grade, bool slab, double weight)[] Conditions = Array.Empty<(int, bool, double)>();
        /// <summary>The lowest rarity a binder copy may be.</summary>
        public Rarity MinRarity = Rarity.Common;
    }

    public static readonly Table Street = new Table
    {
        Name = "Street (Dumpster Row)", Stake = 300, Cap = 1000,
        // A garage sale: Raw copies, often worn.
        Conditions = new[] { (4, false, 0.15), (6, false, 0.25), (8, false, 0.40), (9, false, 0.20) },
    };
    public static readonly Table Den = new Table
    {
        Name = "Den (Basement, Black Market, Nyx)", Stake = 1000, Cap = 5000,
        Conditions = new[] { (8, false, 0.25), (9, false, 0.25), (8, true, 0.20), (9, true, 0.20), (10, true, 0.10) },
    };
    public const int OrvelStake = 3000, OrvelMinCard = 1000;
    /// <summary>Orvel stakes a slab worth between 1× and this times your card.</summary>
    public const double OrvelMatch = 2.0;
    public const int BinderSize = 12, RefillEvery = 3;
    public const int RailDuels = 5;
    public const double RailMarkup = 1.5, ShelfMarkup = 1.5;
    public const int ShelfMin = 5000, ShelfMax = 25000, ShelfMinValue = 3500;
    public const int BrawlEntry = 1000, OpenEntry = 2500;
    /// <summary>Placement pay (winner, finalist, semi-final, quarter-final[, round of 16]): Locals' table at 8.</summary>
    public static readonly int[] BrawlPay = { 900, 650, 400, 150 };
    public static readonly int[] BrawlPacks = { 2, 1, 0, 0 };
    /// <summary>The plan's prize as written (one pack to the winner), for comparison.</summary>
    public static readonly int[] BrawlPacksPlan = { 1, 0, 0, 0 };
    public static readonly int[] OpenPay = { 1500, 1000, 600, 300, 100 };
    public static readonly int[] OpenPacks = { 4, 2, 1, 0, 0 };
    public static readonly int[] OpenPacksPlan = { 4, 0, 0, 0, 0 };
    public const int ContrabandPackSize = 5;
    /// <summary>Phase 0's measured splash gains (Elo over the base): Dumpster Row, the Basement, the Black Market.</summary>
    private static readonly double[] Gain = { 52, 57, 50 };
    /// <summary>The plan's win-rate targets: the starter (typical) and the best option (strong); Nyx's typical is ours.</summary>
    private static readonly (string ev, double typical, double strong)[] Targets =
        { ("Dumpster Row", 0.65, 0.75), ("The Basement Game", 0.50, 0.65), ("The Black Market", 0.40, 0.60), ("Nyx's Table", 0.40, 0.55) };

    private const string AlleyStakeSink = "alley.stake", AlleyStakeBack = "alley.stake.back", RailSink = "alley.rail", ShelfSink = "alley.shelf", MothUsed = "alley.used";

    private sealed class Setup
    {
        public ICardDataProvider Cards = null!;
        public PrintingIndex Printings = null!;
        public List<Banlist> Lists = null!;
        public Banlist AlleyList = null!;
        public Dictionary<string, string> DeckFiles = null!;
        public Dictionary<string, double> Elo = null!;
        public Dictionary<string, Curation> Curated = new Dictionary<string, Curation>();
        public Library Library = null!;
        public Deck Load(string name) { var d = Deck.LoadYdk(DeckFiles[name]); d.Name = name; return d; }
        public Rarity Commonest(uint c) => Printings.Commonest(c, Cards.Get(c)?.Alias ?? 0);
        public Printing[] Of(uint c) => Printings.Of(c, Cards.Get(c)?.Alias ?? 0);
        public string Name(uint c) => Cards.Get(c)?.Name ?? c.ToString();
    }

    private static Setup Load(ICardDataProvider cards, string root)
    {
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities")).ToList();
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var s = new Setup
        {
            Cards = cards,
            Printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin")),
            Lists = Banlist.LoadFolders(new[] { Path.Combine(root, "vendor", "LFLists"), Path.Combine(root, "lflists") }).ToList(),
            DeckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
                .Where(f => !f.Contains("structure")).GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First()),
            Elo = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName)),
            Library = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), roster, circuits),
        };
        s.AlleyList = Alley.List(s.Lists.First(l => l.Name.Contains("Traditional")), cards);
        var curated = Path.Combine(root, "campaign", "alley.txt");
        if (File.Exists(curated)) s.Curated = Alley.LoadCurated(curated, cards);
        return s;
    }

    private static List<string> Bases(AlleyEvent e, Setup s) =>
        s.Elo.Where(p => p.Value >= e.MinElo && p.Value <= e.MaxElo && s.DeckFiles.ContainsKey(p.Key) && Alley.FromEra(s.Load(p.Key), e, s.Printings, s.Cards))
            .Where(p => !(s.Curated.TryGetValue(e.Name, out var c) && c.Skip.Contains(p.Key)))
            .OrderBy(p => p.Value).Select(p => p.Key).ToList();

    private static double Odds(double a, double b) => 1 / (1 + Math.Pow(10, (b - a) / 400));
    private static double EloFor(double p, double against) => against + 400 * Math.Log10(p / (1 - p));

    public static int Run(ICardDataProvider cards, string root, int seed, int decksPerEvent, string only = "")
    {
        // `--alley-probe --only "No Limits"`: just section 10, the No Limits binders from the authored decks (AlleyProbe.NoLimits.cs).
        if (only.Length > 0 && "No Limits".Contains(only, StringComparison.OrdinalIgnoreCase))
        {
            int bad = NoLimitsSection(cards, root, seed);
            Console.WriteLine(bad == 0 ? "\nNo Limits binders fill and every night makes no DP." : $"\n{bad} checks failed.");
            return bad == 0 ? 0 : 1;
        }
        var s = Load(cards, root);
        int failures = 0;
        void Check(string what, bool ok) { Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}"); if (!ok) failures++; }
        decksPerEvent = Math.Max(6, decksPerEvent);
        Console.WriteLine($"Alley probe (seed {seed}, {decksPerEvent} splash decks an event). Every DP figure is per duel unless it says otherwise.");

        // ---- 1. Win rates ------------------------------------------------------------------------------------------
        Console.WriteLine("\n1. Win rates: the plan's targets, and the Elo odds of real decks against each event's splash decks (base Elo + measured gain).");
        var splash = new List<(AlleyEvent e, List<SplashDeck> decks, List<double> elo)>();
        for (int i = 0; i < Alley.Events.Count; i++)
        {
            var e = Alley.Events[i];
            var pool = Alley.Contraband(s.Lists, s.AlleyList, e.EraCap, s.Printings, cards);
            var bases = Bases(e, s);
            var decks = Enumerable.Range(0, decksPerEvent).Select(k => Alley.Splash(s.Load(bases[k % bases.Count]), e, pool, s.AlleyList, seed * 1000 + i * 100 + k, cards,
                s.Curated.TryGetValue(e.Name, out var c) ? c : null)).ToList();
            splash.Add((e, decks, decks.Select(d => s.Elo[d.Base] + Gain[i]).ToList()));
        }
        double MeanElo(int i) => splash[i].elo.Average();
        // Nyx's Table: Black Market splashers and chaos-yata as built; take the Black Market's top plus its gain.
        double nyxElo = splash[2].elo.Max();
        var yous = new[] { 1700.0, 1800, 1947 };
        Console.WriteLine($"  {"event",-18} {"CPU skill",9} {"splash Elo",14} {"typical",8} {"strong",7}   Elo odds of a deck at {string.Join(" / ", yous.Select(y => y.ToString("0")))}   typical player's implied Elo");
        for (int i = 0; i < 4; i++)
        {
            double elo = i < 3 ? MeanElo(i) : nyxElo;
            string band = i < 3 ? $"{splash[i].elo.Min():0}-{splash[i].elo.Max():0}" : $"~{nyxElo:0}";
            string skill = i < 3 ? $"{splash[i].e.Skill:0.00}" : "0.92-1.0";
            Console.WriteLine($"  {Targets[i].ev,-18} {skill,9} {band,14} {Targets[i].typical,8:P0} {Targets[i].strong,7:P0}   "
                + $"{string.Join(" / ", yous.Select(y => Odds(y, elo).ToString("P0"))),-24}   {EloFor(Targets[i].typical, elo):0} (strong {EloFor(Targets[i].strong, elo):0})");
        }
        Console.WriteLine("  The Elo odds are bot against bot at full skill; the targets are what a player should see. The DP verdicts below hold at any win rate.");

        // ---- 2. Binders ---------------------------------------------------------------------------------------------
        Console.WriteLine($"\n2. CPU binders: {BinderSize} stakes from the CPU's deck (its contraband weighted 3×), a printing weighted 10000/Unit, valued by Pricing.V.");
        var contrabandAll = Alley.Contraband(s.Lists, s.AlleyList, 9999, s.Printings, cards);
        var streetBinders = splash[0].decks.Select((d, k) => Binder(d, Street, s, seed * 31 + k)).ToList();
        var denBinders = splash[1].decks.Concat(splash[2].decks).Select((d, k) => Binder(d, Den, s, seed * 37 + k)).ToList();
        var looseDen = new Table { Name = "Den at half floor", Stake = Den.Stake, Cap = Den.Cap, Floor = 0.5, Conditions = Den.Conditions };
        var looseStreet = new Table { Name = "Street at half floor", Stake = Street.Stake, Cap = Street.Cap, Floor = 0.5, Conditions = Street.Conditions };
        var streetLoose = splash[0].decks.Select((d, k) => Binder(d, looseStreet, s, seed * 31 + k)).ToList();
        var denLoose = splash[1].decks.Concat(splash[2].decks).Select((d, k) => Binder(d, looseDen, s, seed * 37 + k)).ToList();
        void BinderLine(string label, List<List<Stake>> binders)
        {
            var all = binders.SelectMany(b => b).ToList();
            var v = all.Select(x => (double)x.Value).OrderBy(x => x).ToList();
            int full = binders.Count(b => b.Count == BinderSize);
            Console.WriteLine($"  {label,-30} {binders.Count} binders, {full} full; {all.Count} stakes: mean {v.Average(),5:0}, median {v[v.Count / 2],5:0}, p10 {v[v.Count / 10],5:0}, p90 {v[v.Count * 9 / 10],5:0}; "
                + $"{all.Count(x => x.Contraband) / (double)all.Count:P0} contraband, {all.Count(x => x.Slab > 0) / (double)all.Count:P0} slabs");
        }
        BinderLine($"Street {Street.Stake}-{Street.Cap}", streetBinders);
        BinderLine($"Street {Street.Stake / 2}-{Street.Cap} (half floor)", streetLoose);
        BinderLine($"Den {Den.Stake}-{Den.Cap}", denBinders);
        BinderLine($"Den {Den.Stake / 2}-{Den.Cap} (half floor)", denLoose);
        foreach (var b in streetBinders.Take(1).Concat(denBinders.Take(1)))
            Console.WriteLine($"    e.g. {string.Join("; ", b.Take(6).Select(x => $"{s.Name(x.Code)} {Rarities.Name(x.Rarity)} {(x.Slab > 0 ? "slab " + x.Slab : "raw " + x.Grade)} {x.Value}"))}");
        // Orvel's: slabs 9-10 at Super Rare and up, of any contraband.
        var orvelSlabs = new List<int>();
        foreach (var c in contrabandAll)
            foreach (var r in s.Of(c).Select(p => p.Rarity).Distinct().Where(r => r >= Rarity.SuperRare))
                foreach (var g in new[] { 9, 10 })
                    orvelSlabs.Add(Pricing.V(new CardCopy { Code = c, Rarity = r, Grade = g, Slab = g }, s.Commonest(c)));
        orvelSlabs.Sort();
        Console.WriteLine($"  Orvel's slabs (9-10, Super Rare and up, {contrabandAll.Count} contraband cards): {orvelSlabs.Count} kinds, {orvelSlabs.First()}-{orvelSlabs.Last()}, median {orvelSlabs[orvelSlabs.Count / 2]}");
        double streetV = streetBinders.SelectMany(b => b).Average(x => x.Value), denV = denBinders.SelectMany(b => b).Average(x => x.Value);

        // ---- 3. DP and value per duel ------------------------------------------------------------------------------
        Console.WriteLine("\n3. DP and card value per duel. Held: the stake comes back on a win. Fee: it never does. Won cards sell for 0 DP.");
        Console.WriteLine($"  {"table",-20} {"win",5} {"held DP",8} {"fee DP",8} {"value won",9} {"value per DP lost: held / fee",30}");
        var rows = new List<(string t, int stake, double v, double p)>();
        foreach (var (p, who) in new[] { (Targets[0].typical, "typical"), (Targets[0].strong, "strong"), (Odds(1800, MeanElo(0)), "Elo 1800"), (1.0, "every duel") })
            rows.Add(($"Street, {who}", Street.Stake, streetV, p));
        foreach (var (i, who) in new[] { (1, "typical"), (1, "strong"), (2, "typical"), (2, "strong"), (3, "strong") })
            rows.Add(($"Den {(i == 1 ? "Basement" : i == 2 ? "Black Mkt" : "Nyx")}, {who}", Den.Stake, denV, who == "typical" ? Targets[i].typical : Targets[i].strong));
        rows.Add(("Den, every duel", Den.Stake, denV, 1.0));
        bool positive = false;
        foreach (var r in rows)
        {
            double held = -(1 - r.p) * r.stake, fee = -r.stake, value = r.p * r.v;
            positive |= held > 0 || fee > 0;
            string ratio = $"{(held < 0 ? (value / -held).ToString("0.0") : "∞"),6} / {value / -fee,4:0.0}";
            Console.WriteLine($"  {r.t,-20} {r.p,5:P0} {held,8:0} {fee,8:0} {value,9:0} {ratio,30}");
        }
        Console.WriteLine("  The shop's rate is 1 value per DP (used market, special orders at 1.5: 0.67); a booster box about 1.25.");
        Check("no table pays DP at any win rate (held stakes: 0 at a 100% win rate, never above)", !positive);

        // ---- 3b. EXP and level-ups -----------------------------------------------------------------------------------
        int expWin = Leveling.DuelExp(s.Library.ExpCircuit, 0, false), expLoss = Leveling.DuelExp(s.Library.ExpCircuit, 1, false);
        Console.WriteLine($"\n3b. Alley duels at the Library's EXP ({expWin} a win, {expLoss} a loss; +40 a first win). If their level-ups paid DP:");
        bool levelDpPositive = false;
        foreach (var level in new[] { 40, 50, 70 })
        {
            double dpPerExp = (100 + 25 * level) / (double)Leveling.ExpToNext(level);
            var cells = new List<string>();
            foreach (var (p, stake, label) in new[] { (0.75, Street.Stake, "Street 75%"), (0.90, Street.Stake, "Street 90%"), (0.65, Den.Stake, "Den 65%"), (0.90, Den.Stake, "Den 90%") })
            {
                double lv = (p * expWin + (1 - p) * expLoss) * dpPerExp, net = lv - (1 - p) * stake;
                levelDpPositive |= net > 0;
                cells.Add($"{label}: {lv:0} level DP, net {net:+0;-0}");
            }
            Console.WriteLine($"  level {level} ({dpPerExp:0.00} DP an EXP, a level every {Leveling.ExpToNext(level) / (0.75 * expWin + 0.25 * expLoss):0.0} duels): {string.Join("; ", cells)}");
        }
        Console.WriteLine($"  So alley level-ups pay Card Tokens only (payDp: false, at least 1 a level), as Locals and repeats do{(levelDpPositive ? ": with level DP the alley would be DP-positive" : "")}.");

        // ---- 4. The binder refill ----------------------------------------------------------------------------------
        Console.WriteLine($"\n4. The binder ({BinderSize} stakes, one back every {RefillEvery} alley duels): cards won in 90 alley duels.");
        foreach (var p in new[] { 0.65, 0.75, 0.90 })
        {
            int Camp(int cpus)
            {
                var binder = Enumerable.Repeat(BinderSize, cpus).ToArray();
                var rng = new Random(seed);
                int won = 0, staked = 0;
                for (int d = 1; d <= 90; d++)
                {
                    int cpu = binder.Select((n, i) => (n, i)).OrderByDescending(x => x.n).First().i; // the fullest binder
                    if (binder[cpu] > 0) { staked++; if (rng.NextDouble() < p) { won++; binder[cpu]--; } }
                    if (d % RefillEvery == 0) for (int i = 0; i < cpus; i++) binder[i] = Math.Min(BinderSize, binder[i] + 1);
                }
                return won;
            }
            Console.WriteLine($"  at {p:P0}: farming one CPU {Camp(1)} cards (without a binder {p * 90:0}); rotating an event's 4 {Camp(4)}; the whole alley's 13 {Camp(13)}");
        }
        Console.WriteLine($"  One CPU runs dry after about {BinderSize / (0.75 - 1.0 / RefillEvery):0} duels at 75%, then pays one card per {RefillEvery} duels.");

        // ---- 5. Orvel's Table and Moth's rail -----------------------------------------------------------------------
        Console.WriteLine($"\n5. Orvel's Table: {OrvelStake:N0} DP held and one spare card (worth at least {OrvelMinCard:N0}); Orvel stakes a slab worth 1-{OrvelMatch}× your card. "
            + $"A lost card waits on Moth's rail for {RailDuels} duels at {RailMarkup}× its value.");
        Console.WriteLine($"  {"your card",9} {"win",5} {"DP, no buy-back",16} {"DP, buy back",13} {"value, no buy-back",19} {"value, buy back",16}");
        foreach (var vy in new[] { 1000, 3000, 7500 })
            foreach (var p in new[] { 0.40, 0.55, 0.70, 1.0 })
            {
                double vo = vy * (1 + OrvelMatch) / 2;
                double dpNo = -(1 - p) * OrvelStake, dpBuy = dpNo - (1 - p) * RailMarkup * vy;
                double valNo = p * vo - (1 - p) * vy, valBuy = p * vo;
                Console.WriteLine($"  {vy,9:N0} {p,5:P0} {dpNo,16:0} {dpBuy,13:0} {valNo,19:0} {valBuy,16:0}");
            }

        // ---- 6. Moth's van ------------------------------------------------------------------------------------------
        Console.WriteLine($"\n6. Moth's van. The shelf: slabs 9-10 of contraband at Secret Rare and up worth {ShelfMinValue:N0}+, at {ShelfMarkup}× value, {ShelfMin:N0}-{ShelfMax:N0}.");
        var shelf = new List<(uint code, Rarity r, int g, int v, int price)>();
        foreach (var c in contrabandAll)
            foreach (var r in s.Of(c).Select(p => p.Rarity).Distinct().Where(r => r >= Rarity.SecretRare))
                foreach (var g in new[] { 9, 10 })
                {
                    int v = Pricing.V(new CardCopy { Code = c, Rarity = r, Grade = g, Slab = g }, s.Commonest(c));
                    if (v >= ShelfMinValue) shelf.Add((c, r, g, v, ShelfPrice(v)));
                }
        var byRarity = shelf.GroupBy(x => (x.r, x.g)).OrderBy(g => g.First().v);
        foreach (var g in byRarity) Console.WriteLine($"  {Rarities.Name(g.Key.r),-28} slab {g.Key.g,2}: value {g.First().v,6:N0}, shelf {g.First().price,6:N0} ({g.First().price / (double)g.First().v:0.00}×), {g.Count()} cards");
        Console.WriteLine($"  {shelf.Count} shelf items from {shelf.Select(x => x.code).Distinct().Count()} cards; prices {shelf.Min(x => x.price):N0}-{shelf.Max(x => x.price):N0}");

        // ---- 7. The Contraband Pack ---------------------------------------------------------------------------------
        Console.WriteLine($"\n7. The Contraband Pack: {ContrabandPackSize} different cards from the ever-Forbidden pool ({contrabandAll.Count} after exclusions), a printing weighted 10000/Unit, graded at the foil odds.");
        var rawValues = Enumerable.Range(0, 4000).Select(k => ContrabandPack(contrabandAll, s, Hash.Mix((uint)seed, 0xC0B7u, (uint)k), false).Sum(x => Pricing.V(x, s.Commonest(x.Code)))).OrderBy(x => x).ToList();
        Console.WriteLine($"  as written (5 Raw cards): mean {rawValues.Average():N0}, median {rawValues[2000]:N0}, p10 {rawValues[400]:N0}, p90 {rawValues[3600]:N0}");
        Console.WriteLine("  recommended (4 Raw cards and a sealed slab 9-10 at Super Rare or better, as a Prize Pack's slot 5):");
        var packValues = new List<int>();
        var packRarities = new Dictionary<Rarity, int>();
        for (int k = 0; k < 4000; k++)
        {
            var pulls = ContrabandPack(contrabandAll, s, Hash.Mix((uint)seed, 0xC0B7u, (uint)k));
            packValues.Add(pulls.Sum(x => Pricing.V(x, s.Commonest(x.Code))));
            foreach (var x in pulls) packRarities[x.Rarity] = packRarities.GetValueOrDefault(x.Rarity) + 1;
        }
        packValues.Sort();
        double packV = packValues.Average();
        Console.WriteLine($"  value: mean {packV:N0}, median {packValues[packValues.Count / 2]:N0}, p10 {packValues[packValues.Count / 10]:N0}, p90 {packValues[packValues.Count * 9 / 10]:N0}");
        Console.WriteLine($"  rarities: {string.Join(", ", packRarities.OrderBy(r => r.Key).Where(r => r.Value * 1000 >= 4000 * ContrabandPackSize).Select(r => $"{Rarities.Name(r.Key)} {100.0 * r.Value / (4000 * ContrabandPackSize):0.0}%"))}");
        // A Locals Prize Pack, for comparison.
        var prizes = new PrizePacks(cards, s.Printings, n => s.DeckFiles.ContainsKey(n) ? s.Load(n) : null);
        var prizeValues = new List<double>();
        for (int c = 0; c < s.Library.Cups.Count; c++)
        {
            var cup = s.Library.Cups[c];
            if (cup.Locals.Count != Brackets.LocalsPerCup) continue;
            var b = Brackets.Create(cup, s.Elo, Hash.Mix(20261101u, (uint)c), "");
            prizeValues.Add(Enumerable.Range(0, 100).Average(k => prizes.Roll(b, cup.Pool, k).Sum(x =>
                Pricing.V(new CardCopy { Code = x.Code, Rarity = x.Rarity, Grade = x.Slab > 0 ? x.Slab : Grades.Roll(CopySource.Prize, Hash.Mix((uint)k, x.Code)), Slab = x.Slab }, s.Commonest(x.Code)))));
        }
        Console.WriteLine($"  a Locals Prize Pack for comparison: mean {prizeValues.Average():N0} ({prizeValues.Min():N0}-{prizeValues.Max():N0} by cup)");

        // ---- 8. Brackets --------------------------------------------------------------------------------------------
        Console.WriteLine($"\n8. Alley brackets by Elo odds (20,000 each). Placement DP minus entry; prize packs at the pack value above (unsellable).");
        bool bracketPositive = false;
        var rng8 = new Random(seed);
        void BracketRow(string label, double you, Func<Random, List<double>> field, int size, int entry, int[] pay, int[] packs, bool bo3Final)
        {
            var place = new int[pay.Length];
            for (int n = 0; n < 20000; n++) place[Bracket(you, field(rng8), size, bo3Final, rng8)]++;
            double ev = place.Select((c, i) => c / 20000.0 * pay[i]).Sum() - entry;
            double packEv = place.Select((c, i) => c / 20000.0 * packs[i]).Sum() * packV;
            bracketPositive |= ev > 0;
            Console.WriteLine($"  {label,-44} you {you,5:0}: title {place[0] / 20000.0,5:P0}, final {(place[0] + place[1]) / 20000.0,5:P0}; DP {ev,6:+0;-0} an entry, cards {packEv,5:0}"
                + $" ({(ev < 0 ? (packEv / -ev).ToString("0.0") : "∞")} value per DP lost)");
        }
        List<double> Field(int ev, int n, Random r) => Enumerable.Range(0, n).Select(_ => splash[ev].elo[r.Next(splash[ev].elo.Count)]).ToList();
        foreach (var (ev, label) in new[] { (0, "Dumpster Row"), (1, "the Basement"), (2, "the Black Market") })
            foreach (var (p, who) in new[] { (Targets[ev].typical, "typical"), (Targets[ev].strong, "strong") })
                BracketRow($"Alley Brawl after {label}, {who} ({p:P0})", EloFor(p, MeanElo(ev)), r => Field(ev, 7, r), 8, BrawlEntry, BrawlPay, BrawlPacks, false);
        BracketRow("Alley Brawl after the Black Market, a top deck", 1947, r => Field(2, 7, r), 8, BrawlEntry, BrawlPay, BrawlPacks, false);
        Console.WriteLine("  The plan's prize as written (1 pack to the winner only):");
        foreach (var (p, who) in new[] { (Targets[0].typical, "typical"), (Targets[0].strong, "strong") })
            BracketRow($"Alley Brawl after Dumpster Row, {who} ({p:P0})", EloFor(p, MeanElo(0)), r => Field(0, 7, r), 8, BrawlEntry, BrawlPay, BrawlPacksPlan, false);
        // Midnight Open: 5 strong circuit decks (ranks 6-10), 5 late Library bosses, 5 Black Market splashers.
        var circuitChars = s.Elo.Values.OrderByDescending(v => v).Skip(5).Take(5).ToList();
        var bosses = s.Library.Cups.Skip(7).Select(c => s.Elo.TryGetValue(c.Matches[^1].Deck, out var e) ? e : 1700).Take(5).ToList();
        foreach (var you in new[] { 1700.0, 1800, 1900, 1947 })
            BracketRow("Midnight Open (16, Bo3 final)", you, r => circuitChars.Concat(bosses).Concat(Field(2, 5, r)).ToList(), 16, OpenEntry, OpenPay, OpenPacks, true);
        Console.WriteLine("  The plan's prize as written (4 packs to the winner only):");
        foreach (var you in new[] { 1800.0, 1947 })
            BracketRow("Midnight Open (16, Bo3 final)", you, r => circuitChars.Concat(bosses).Concat(Field(2, 5, r)).ToList(), 16, OpenEntry, OpenPay, OpenPacksPlan, true);
        Console.WriteLine($"  Field: circuit decks {string.Join(" ", circuitChars.Select(x => x.ToString("0")))}; bosses {string.Join(" ", bosses.Select(x => x.ToString("0")))}; Black Market splashers {splash[2].elo.Min():0}-{splash[2].elo.Max():0}");
        Check($"Alley Brawl: the title pays {BrawlPay[0]} for a {BrawlEntry} entry ({BrawlPay[0] / 3.0:0} a duel over 3 duels, cap 300)", BrawlPay[0] < BrawlEntry && BrawlPay[0] / 3.0 <= 300);
        Check($"Midnight Open: the title pays {OpenPay[0]} for a {OpenEntry} entry ({OpenPay[0] / 5.0:0} a duel over at least 5 duels, cap 300)", OpenPay[0] < OpenEntry && OpenPay[0] / 5.0 <= 300);
        Check("no bracket row is DP-positive in expectation", !bracketPositive);

        // ---- 9. Loops through the engine ----------------------------------------------------------------------------
        Console.WriteLine("\n9. Loops, through the engine's Sell desk, Grading Desk, Card Trader and used market (net DP after selling everything that sells):");
        failures += Loops(s, seed, contrabandAll, streetBinders, denBinders, shelf);

        // ---- 10. No Limits: the binders from the authored decks (AlleyProbe.NoLimits.cs) ----
        failures += NoLimitsSection(cards, root, seed);

        Console.WriteLine(failures == 0 ? "\nNo alley loop or bracket is DP-positive." : $"\n{failures} checks failed.");
        return failures == 0 ? 0 : 1;
    }

    // ---- Binders ------------------------------------------------------------------------------------------------------

    public sealed class Stake
    {
        public uint Code;
        public Rarity Rarity;
        public int Grade, Slab, Value;
        public bool Contraband;
    }

    /// <summary>
    /// A CPU's binder: up to 12 different copies of its deck's cards (the contraband it splashed weighted 3×), each at one
    /// of the card's real printings (rarity weighted 10000/Unit, as the used market and Prize Packs pick) in one of the
    /// table's conditions, worth between Floor × Stake and Cap. Seeded: the same deck, the same binder.
    /// </summary>
    private static List<Stake> Binder(SplashDeck deck, Table t, Setup s, int seed)
    {
        var rng = new Random(seed);
        var added = deck.Added.Select(c => Banlist.BaseCode(c, s.Cards)).ToHashSet();
        var codes = deck.Deck.Main.Concat(deck.Deck.Extra).Select(c => Banlist.BaseCode(c, s.Cards)).Distinct().ToList();
        var candidates = new List<(Stake stake, double weight)>();
        foreach (var c in codes)
            foreach (var r in s.Of(c).Select(p => p.Rarity).Distinct().Where(r => r >= t.MinRarity))
                foreach (var (grade, slab, w) in t.Conditions)
                {
                    int v = Pricing.V(new CardCopy { Code = c, Rarity = r, Grade = grade, Slab = slab ? grade : 0 }, s.Commonest(c));
                    if (v < t.Floor * t.Stake || v > t.Cap) continue;
                    candidates.Add((new Stake { Code = c, Rarity = r, Grade = grade, Slab = slab ? grade : 0, Value = v, Contraband = added.Contains(c) },
                        w * 10000.0 / Pricing.Unit(r) * (added.Contains(c) ? 3 : 1)));
                }
        var binder = new List<Stake>();
        while (binder.Count < BinderSize && candidates.Count > 0)
        {
            double pick = rng.NextDouble() * candidates.Sum(x => x.weight);
            int i = 0;
            for (; i < candidates.Count - 1; i++) if ((pick -= candidates[i].weight) < 0) break;
            var chosen = candidates[i].stake;
            binder.Add(chosen);
            candidates.RemoveAll(x => x.stake.Code == chosen.Code); // different cards
        }
        return binder;
    }

    public static int ShelfPrice(int value) => Math.Min(ShelfMax, Math.Max(ShelfMin, (int)Math.Round(value * ShelfMarkup / 500.0) * 500));

    /// <summary>A Contraband Pack's copies (paid 0): five different cards, a printing weighted 10000/Unit, the foil grade odds.</summary>
    private static List<CardCopy> ContrabandPack(IReadOnlyList<uint> pool, Setup s, uint seed) => ContrabandPack(pool, s, seed, SlabSlot);

    /// <summary>The pack's fifth card a sealed slab (Super Rare and up, Grade 9 at 75%, 10 at 25%), as a Prize Pack's slot 5.</summary>
    public static bool SlabSlot = true;

    private static List<CardCopy> ContrabandPack(IReadOnlyList<uint> pool, Setup s, uint seed, bool slabSlot)
    {
        uint k = 0;
        uint R() => Hash.Mix(seed, 0xC7A8u, k++);
        var left = pool.ToList();
        var pulls = new List<CardCopy>();
        for (int i = 0; i < ContrabandPackSize && left.Count > 0; i++)
        {
            bool slab = slabSlot && i == ContrabandPackSize - 1;
            var from = slab ? left.Where(c => s.Of(c).Any(p => p.Rarity >= Rarity.SuperRare)).ToList() : left;
            if (from.Count == 0) break;
            var code = from[(int)(R() % (uint)from.Count)];
            left.Remove(code);
            var rarities = s.Of(code).Select(p => p.Rarity).Distinct().Where(r => !slab || r >= Rarity.SuperRare).OrderBy(r => r).ToList();
            if (rarities.Count == 0) rarities.Add(Rarity.Common);
            double total = rarities.Sum(r => 10000.0 / Pricing.Unit(r)), pick = R() / 4294967296.0 * total;
            var rarity = rarities[^1];
            foreach (var r in rarities) { pick -= 10000.0 / Pricing.Unit(r); if (pick < 0) { rarity = r; break; } }
            uint cs = R();
            int grade = slab ? (R() % 100 < 25 ? 10 : 9) : Grades.Roll(CopySource.Prize, cs);
            pulls.Add(new CardCopy { Code = code, Rarity = rarity, Seed = cs, Source = CopySource.Prize, Grade = grade, Slab = slab ? grade : 0, Floor = slab ? grade : 0, Paid = 0 });
        }
        return pulls;
    }

    // ---- Brackets -----------------------------------------------------------------------------------------------------

    /// <summary>One single-elimination bracket with you as seed 1 (the rest seeded by Elo): your placement (0 the title).</summary>
    private static int Bracket(double you, List<double> others, int size, bool bo3Final, Random rng)
    {
        var seeded = new[] { you }.Concat(others.OrderByDescending(x => x)).Select((elo, i) => (elo, i)).ToList();
        int[] order = size == 8 ? new[] { 0, 7, 3, 4, 1, 6, 2, 5 } : new[] { 0, 15, 7, 8, 3, 12, 4, 11, 1, 14, 6, 9, 2, 13, 5, 10 };
        var round = order.Select(i => seeded[i]).ToList();
        int rounds = size == 8 ? 3 : 4, r = 0;
        while (round.Count > 1)
        {
            var next = new List<(double elo, int i)>();
            bool final = round.Count == 2;
            for (int k = 0; k < round.Count; k += 2)
            {
                double p = Odds(round[k].elo, round[k + 1].elo);
                if (final && bo3Final) p = p * p * (3 - 2 * p);
                var w = rng.NextDouble() < p ? round[k] : round[k + 1];
                if ((round[k].i == 0 || round[k + 1].i == 0) && w.i != 0) return rounds - r; // out: 1 finalist ... rounds (first round)
                next.Add(w);
            }
            round = next;
            r++;
        }
        return 0;
    }

    // ---- Loops --------------------------------------------------------------------------------------------------------

    private static int Loops(Setup s, int seed, List<uint> contraband, List<List<Stake>> streetBinders, List<List<Stake>> denBinders,
        List<(uint code, Rarity r, int g, int v, int price)> shelf)
    {
        int failures = 0;
        int none(uint _) => 0;
        Profile Fresh()
        {
            var p = new Profile { Dp = 10_000_000, Exp = 50_000_000 };
            p.Collection.Salt = (uint)seed * 2654435761u + 11;
            p.Collection.CommonestOf = s.Commonest;
            p.LibraryOpen = true;
            return p;
        }
        int Liquidate(Profile p)
        {
            foreach (var c in GradingDesk.AtDesk(p).ToList()) { for (int d = 0; d < GradingDesk.Turnaround; d++) p.NoteResult("probe", 0); GradingDesk.Collect(p, c); }
            int got = 0;
            foreach (var c in p.Collection.All.Where(c => !c.AtDesk).ToList()) got += SellDesk.Sell(p, c, none);
            return got;
        }
        // A won stake or prize: paid 0; a 4th copy replaces your least valuable Raw copy for 0 DP, as Prize Packs do.
        CardCopy Win(Profile p, Stake st)
        {
            var copy = p.Collection.NewCopy(st.Code, st.Rarity, CopySource.Prize);
            copy.Paid = 0;
            copy.Grade = st.Slab > 0 ? st.Slab : st.Grade;
            copy.Slab = copy.Floor = st.Slab;
            p.Collection.Take(copy);
            return copy;
        }
        void Grade(Profile p, IEnumerable<CardCopy> copies)
        {
            foreach (var c in copies.ToList())
            {
                GradingDesk.Submit(p, c, express: true, none);
                for (int k = 0; k < 3 && c.Slab < 10; k++) { GradingDesk.Crack(p, c); GradingDesk.Submit(p, c, express: true, none); }
            }
        }
        // Moth's rail: the lost copy leaves, and comes back for 1.5× its value (the price joins what you paid for it).
        bool BuyBack(Profile p, CardCopy copy)
        {
            int price = Pricing.Round5(p.Collection.Value(copy) * RailMarkup);
            if (!p.Collection.Remove(copy) || !p.Spend(RailSink, price)) return false;
            copy.Paid += price;
            p.Collection.Insert(copy);
            return true;
        }
        var results = new List<(string loop, string what, int spent, int net)>();
        void Verdict(string loop, IEnumerable<(string what, int spent, int net)> tries)
        {
            var list = tries.ToList();
            var best = list.OrderByDescending(t => t.net).First();
            results.Add((loop, best.what, best.spent, best.net));
            bool bad = best.net > 0;
            if (bad) failures++;
            Console.WriteLine($"  {(bad ? "FAIL" : "ok  ")} {loop}: best {best.what}: net {best.net:+#,0;-#,0;0} DP on {best.spent:N0} spent ({list.Count} tried)");
        }
        (string, int, int) Measure(string what, Action<Profile> act)
        {
            var p = Fresh();
            int dp0 = p.Dp, spent0 = p.DpSpent.Values.Sum();
            act(p);
            Liquidate(p);
            return (what, p.DpSpent.Values.Sum() - spent0, p.Dp - dp0);
        }
        var stakes = streetBinders.Concat(denBinders).SelectMany(b => b).ToList();
        var bestStakes = stakes.OrderByDescending(x => x.Value).Take(40).Concat(stakes.Where((x, i) => i % 7 == 0)).ToList();

        // Held stakes: a win gives the stake back and the CPU's card; then sell, grade-and-sell, or sell the 4th copy it pushed out.
        Verdict("stake won -> Sell desk", bestStakes.Select(st => Measure($"{s.Name(st.Code)} {st.Value}", p =>
        {
            p.Spend(AlleyStakeSink, Den.Stake); Win(p, st); p.Earn(AlleyStakeBack, Den.Stake);
        })));
        Verdict("stake won -> grade (to 10s, crack and resubmit) -> sell", bestStakes.Select(st => Measure($"{s.Name(st.Code)} {st.Value}", p =>
        {
            p.Spend(AlleyStakeSink, Den.Stake); var c = Win(p, st); p.Earn(AlleyStakeBack, Den.Stake); Grade(p, new[] { c });
        })));
        Verdict("3 bought copies, then a won 4th -> sell everything", bestStakes.Take(30).Select(st => Measure($"{s.Name(st.Code)} {st.Value}", p =>
        {
            for (int k = 0; k < 3; k++) { var o = p.Collection.NewCopy(st.Code, s.Commonest(st.Code), CopySource.Shop); o.Paid = RecipeShop.CopyPrice(s.Commonest(st.Code)); p.Spend(Ledger.Order, o.Paid); p.Collection.Insert(o); }
            Win(p, st);
        })));
        Verdict("stake lost (Street, Den, Orvel's)", new[] { Street.Stake, Den.Stake, OrvelStake }.Select(x => Measure($"{x}", p => p.Spend(AlleyStakeSink, x))));
        // Orvel's: your card lost, bought back off the rail, re-staked and re-lost; and a won card staked, lost and bought back.
        Verdict("Orvel's: lose your card, buy it back, sell it (1-5 rounds)", bestStakes.Take(20).SelectMany(st => Enumerable.Range(1, 5).Select(n => Measure($"{s.Name(st.Code)} {st.Value} x{n}", p =>
        {
            var c = p.Collection.NewCopy(st.Code, st.Rarity, CopySource.Used); c.Grade = st.Grade; c.Slab = c.Floor = st.Slab;
            c.Paid = st.Value; p.Spend(MothUsed, st.Value); p.Collection.Insert(c);
            for (int k = 0; k < n; k++) { p.Spend(AlleyStakeSink, OrvelStake); BuyBack(p, c); }
        }))));
        Verdict("Orvel's: a won card staked, lost, bought back, graded, sold", bestStakes.Take(30).Select(st => Measure($"{s.Name(st.Code)} {st.Value}", p =>
        {
            var c = Win(p, st); p.Spend(AlleyStakeSink, OrvelStake); BuyBack(p, c); Grade(p, new[] { c });
        })));
        // Moth's shelf: buy, sell; buy, crack, regrade (the 10% chance at a 10), sell.
        var shelfTries = shelf.Where((x, i) => i % 3 == 0).ToList();
        Verdict($"Moth's shelf -> Sell desk / Moth", shelfTries.Select(x => Measure($"{s.Name(x.code)} {Rarities.Name(x.r)} {x.g} at {x.price}", p =>
        {
            var c = p.Collection.NewCopy(x.code, x.r, CopySource.Used); c.Grade = x.g; c.Slab = c.Floor = x.g; c.Paid = x.price; p.Spend(ShelfSink, x.price); p.Collection.Insert(c);
        })));
        Verdict($"Moth's shelf -> crack, regrade -> sell", shelfTries.Where(x => x.g == 9).Select(x => Measure($"{s.Name(x.code)} {Rarities.Name(x.r)} 9 at {x.price}", p =>
        {
            var c = p.Collection.NewCopy(x.code, x.r, CopySource.Used); c.Grade = x.g; c.Slab = c.Floor = x.g; c.Paid = x.price; p.Spend(ShelfSink, x.price); p.Collection.Insert(c);
            Grade(p, new[] { c });
        })));
        // Moth's used tab: the used market's machinery over the contraband pool, priced at value.
        foreach (var grade in new[] { false, true })
        {
            var tries = new List<(string, int, int)>();
            for (int epoch = 0; epoch < 20; epoch++)
            {
                var probe = Fresh();
                for (int d = 0; d < epoch * UsedMarket.Every; d++) probe.NoteResult("probe", 0);
                foreach (var l in UsedMarket.Listings(probe, contraband, 2026, s.Printings, s.Cards))
                    tries.Add(Measure($"{s.Name(l.Copy.Code)} {Rarities.Name(l.Copy.Rarity)} {(l.Copy.IsSlab ? "slab " + l.Copy.Slab : "raw " + l.Copy.Grade)} at {l.Price}", p =>
                    {
                        for (int d = 0; d < epoch * UsedMarket.Every; d++) p.NoteResult("probe", 0);
                        UsedMarket.Buy(p, UsedMarket.Listings(p, contraband, 2026, s.Printings, s.Cards)[l.Slot]);
                        if (grade) Grade(p, p.Collection.All);
                    }));
            }
            Verdict(grade ? "Moth's used tab -> grade -> sell" : "Moth's used tab -> Sell desk / Moth", tries);
        }
        // Contraband Packs (bracket prizes) and tokens from alley level-ups: paid 0.
        Verdict("Contraband Packs -> sell, and -> grade the best -> sell", Enumerable.Range(0, 60).Select(k => Measure($"pack {k}", p =>
        {
            foreach (var c in ContrabandPack(contraband, s, Hash.Mix((uint)seed, 0x7777u, (uint)k))) { var n = p.Collection.NewCopy(c.Code, c.Rarity, CopySource.Prize); n.Grade = c.Grade; n.Paid = 0; p.Collection.Take(n); }
            if (k % 2 == 1) Grade(p, p.Collection.All.OrderByDescending(x => p.Collection.Value(x)).Take(2));
        })));
        var tradeable = contraband.OrderByDescending(c => CardTrader.Cost(c, s.Printings, s.Cards)).Take(20).ToList();
        Verdict("alley level-up tokens -> Card Trader -> sell (and -> grade -> sell)", tradeable.Select((c, i) => Measure(s.Name(c), p =>
        {
            p.Tokens += 3;
            CardTrader.Trade(c, p, 2026, s.Printings, s.Cards);
            if (i % 2 == 1) Grade(p, p.Collection.All);
        })));

        // The exhaustive search: every sequence of 1-4 alley actions, then sell everything.
        var top = stakes.OrderByDescending(x => x.Value).First();
        var shelfItem = shelf.Where(x => x.g == 9).OrderByDescending(x => x.v).First();
        var actions = new List<(string name, Action<Profile> act)>
        {
            ("street win", p => { p.Spend(AlleyStakeSink, Street.Stake); Win(p, streetBinders[0][0]); p.Earn(AlleyStakeBack, Street.Stake); }),
            ("den win", p => { p.Spend(AlleyStakeSink, Den.Stake); Win(p, top); p.Earn(AlleyStakeBack, Den.Stake); }),
            ("den loss", p => p.Spend(AlleyStakeSink, Den.Stake)),
            ("orvel loss+buy back", p => { var c = p.Collection.All.Where(x => !x.AtDesk).OrderByDescending(x => p.Collection.Value(x)).FirstOrDefault(); p.Spend(AlleyStakeSink, OrvelStake); if (c != null) BuyBack(p, c); }),
            ("orvel loss, let it go", p => { var c = p.Collection.All.Where(x => !x.AtDesk).OrderByDescending(x => p.Collection.Value(x)).FirstOrDefault(); p.Spend(AlleyStakeSink, OrvelStake); if (c != null) p.Collection.Remove(c); }),
            ("shelf", p => { var c = p.Collection.NewCopy(shelfItem.code, shelfItem.r, CopySource.Used); c.Grade = c.Slab = c.Floor = shelfItem.g; c.Paid = shelfItem.price; if (p.Spend(ShelfSink, shelfItem.price)) p.Collection.Take(c); }),
            ("contraband pack", p => { foreach (var c in ContrabandPack(contraband, s, (uint)p.DpSpent.Values.Sum())) { var n = p.Collection.NewCopy(c.Code, c.Rarity, CopySource.Prize); n.Grade = c.Grade; p.Collection.Take(n); } }),
            ("token trade", p => { p.Tokens += 3; CardTrader.Trade(tradeable[0], p, 2026, s.Printings, s.Cards); }),
            ("grade best", p => { var c = p.Collection.All.Where(x => !x.AtDesk).OrderByDescending(x => p.Collection.Value(x)).FirstOrDefault(); if (c != null) GradingDesk.Submit(p, c, true, none); }),
            ("crack+regrade", p => { var c = p.Collection.All.FirstOrDefault(x => x.IsSlab); if (c != null) { GradingDesk.Crack(p, c); GradingDesk.Submit(p, c, true, none); } }),
            ("sell best", p => { var c = p.Collection.All.Where(x => !x.AtDesk).OrderByDescending(x => p.Collection.SellPrice(x)).FirstOrDefault(); if (c != null) SellDesk.Sell(p, c, none); }),
        };
        var seqs = new List<(string, int, int)>();
        foreach (var len in new[] { 1, 2, 3, 4 })
            foreach (var seq in Sequences(actions.Count, len))
                seqs.Add(Measure(string.Join(" > ", seq.Select(i => actions[i].name)), p => { foreach (var i in seq) actions[i].act(p); }));
        Verdict($"exhaustive: every sequence of 1-4 of {actions.Count} alley actions, then sell all ({seqs.Count} sequences, {seqs.Count(x => x.Item3 > 0)} DP-positive)", seqs);
        var zero = seqs.Where(x => x.Item3 == 0).ToList();
        bool zeroOk = zero.All(x => !x.Item1.Contains("loss") && !x.Item1.Contains("shelf"));
        if (!zeroOk) failures++;
        Console.WriteLine($"  {(zeroOk ? "ok  " : "FAIL")} {zero.Count} sequences break even (net 0): only won stakes coming back, trades and prizes (none with a loss or a shelf buy)");
        var nearest = seqs.Where(x => x.Item2 > 0 && x.Item3 < 0).OrderByDescending(x => x.Item3 / (double)x.Item2).First();
        Console.WriteLine($"  the closest losing sequence: {nearest.Item1}: net {nearest.Item3:+#,0;-#,0} on {nearest.Item2:N0} spent");
        return failures;
    }

    private static IEnumerable<int[]> Sequences(int n, int len)
    {
        if (len == 0) { yield return Array.Empty<int>(); yield break; }
        foreach (var head in Sequences(n, len - 1))
            for (int i = 0; i < n; i++) yield return head.Append(i).ToArray();
    }
}
