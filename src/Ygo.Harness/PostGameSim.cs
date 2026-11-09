using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--postgame-sim [--load &lt;mods&gt;] [--persona minimal|frugal|collector|gambler|all] [--seed N] [--cups 10]` (plan step 11):
/// from a champion save, a pilot plays the Format Library the way a player would (the cups in order, the Every Era
/// final, Locals) and spends by persona. Duels are not played: each match is won with the measured win rate of the deck
/// played there (campaign/library-advice.txt, `--circuit-study`), Locals matches by the bracket's Elo odds (Brackets.Odds),
/// so a persona runs in seconds. Every payout, price and purchase goes through the engine (Library.Record, RecipeShop,
/// Cosmetics, Brackets, PrizePacks, GradingDesk, UsedMarket), so the DP ledger is the game's.
///   frugal:    loaners at every cup; buys (Make it yours) only the decks the final's rounds need.
///   minimal:   owns the cup's best measured deck (recipe packs or Make it yours, the cheaper), nothing else.
///   collector: minimal, plus the cup's mat and an engraved plate, one booster set completed per cup, 5 copies graded
///              and up to 3 slabs from the used market per cup.
///   gambler:   minimal, plus Locals after every clear (until a title, up to 3 entries), 20 boosters per cup, and grading
///              its Mint-looking Raw copies (resubmitting 9s) for 10s.
/// Reports after <c>--cups</c> cups (10 by default: the plan's target) and at the end.
/// </summary>
public static class PostGameSim
{
    private const int MaxTries = 40;

    public static readonly string[] Personas = { "frugal", "minimal", "collector", "gambler" };

    private sealed class World
    {
        public ICardDataProvider Cards = null!;
        public PrintingIndex Printings = null!;
        public PackCatalog Catalog = null!;
        public List<Personality> Roster = null!;
        public Circuits Circuits = null!;
        public Library Library = null!;
        public List<Banlist> Lists = null!;
        public WallAdvice Advice = null!;
        public Dictionary<string, double> Ratings = null!;
        public Matchups Matchups = null!;
        public PrizePacks Prizes = null!;
        public Dictionary<string, string> DeckFiles = null!;
        public CardUnlocks Unlocks = null!;
        public string Root = "";
        public Deck? LoadDeck(string name) { if (!DeckFiles.TryGetValue(name, out var f)) return null; var d = Deck.LoadYdk(f); d.Name = name; return d; }
    }

    public static int Run(ICardDataProvider cards, string root, string? load, string persona, int seed, int reportAt)
    {
        var w = new World { Cards = cards, Root = root };
        // Card versions phase 2: the save's collection stamps new copies and tells texts apart, as the app's does (YGO_CV2_OFF=1: as before).
        if (Environment.GetEnvironmentVariable("YGO_CV2_OFF") != "1") VersionSetup.Use(VersionSetup.Load(cards, root));
        w.Printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        w.Catalog = new PackCatalog(w.Printings, cards);
        w.Roster = Personality.LoadFolder(Path.Combine(root, "personalities")).ToList();
        w.Circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), w.Roster);
        w.Library = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), w.Roster, w.Circuits);
        w.Lists = Banlist.LoadFolders(new[] { Path.Combine(root, "vendor", "LFLists"), Path.Combine(root, "lflists") }).ToList();
        w.Advice = WallAdvice.Load(Path.Combine(root, "campaign", Library.AdviceFile));
        w.Ratings = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName)).ToDictionary(p => p.Key, p => p.Value);
        var matchupsFile = Path.Combine(root, "campaign", Matchups.FileName);
        w.Matchups = File.Exists(matchupsFile) ? Matchups.Parse(File.ReadAllText(matchupsFile)) : Matchups.Empty;
        w.DeckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        w.Prizes = new PrizePacks(cards, w.Printings, w.LoadDeck);
        var noPass = BanPasses.LoadNoPass(Path.Combine(root, "campaign", "no-pass.txt"), cards);
        w.Unlocks = new CardUnlocks(cards, w.Printings, w.Circuits, w.Roster, noPass, w.LoadDeck, w.Catalog,
            BountyBoard.Load(Path.Combine(root, "campaign", BountyBoard.FileName)));

        load ??= new[] { Path.Combine(root, "build", "proof-economy", "champion"), Path.Combine(root, "build", "qa-night5", "mods") }
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "save", "profile.txt")));
        if (load == null) { Console.Error.WriteLine("No champion save: pass --load <mods folder> (run --campaign-sim --save <folder> first)."); return 1; }
        var profilePath = load.EndsWith(".txt") ? load : Path.Combine(load, "save", "profile.txt");
        Console.WriteLine($"Post-game pilots from {profilePath}. Duels by measured win rates ({Library.AdviceFile}), Locals by Elo odds; seed {seed}.");
        int failures = 0;
        foreach (var who in persona == "all" ? Personas : new[] { persona })
        {
            var profile = Profile.Load(profilePath)!;
            profile.Collection.CommonestOf = c => w.Printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
            VersionSetup.Apply(profile, w.Printings);
            w.Circuits.Migrate(profile);
            if (!w.Library.Open(profile, w.Circuits) && !profile.LibraryOpen) { Console.Error.WriteLine("The save hasn't cleared the Gauntlet."); return 1; }
            var decksDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(profilePath)!)!, "decks");
            var own = Directory.Exists(decksDir)
                ? Directory.GetFiles(decksDir, "*.ydk").Select(f => { var d = Deck.LoadYdk(f); d.Name = Path.GetFileNameWithoutExtension(f); return d; }).ToList()
                : new List<Deck>();
            failures += new Pilot(w, profile, who, seed, own, reportAt).Play();
        }
        return failures == 0 ? 0 : 1;
    }

    private sealed class Pilot
    {
        private readonly World _w;
        private readonly Profile _p;
        private readonly string _persona;
        private readonly Random _rng;
        private readonly int _reportAt;
        /// <summary>Decks of yours: built-in recipe name -> your copy (legal where it was bought).</summary>
        private readonly Dictionary<string, Deck> _owned = new Dictionary<string, Deck>();
        private readonly List<Deck> _champion;
        private readonly Dictionary<string, int> _earned0, _spent0;
        private readonly int _dp0;
        private int _duels, _localsDuels, _walls;
        private readonly List<string> _log = new List<string>();

        public Pilot(World w, Profile p, string persona, int seed, List<Deck> champion, int reportAt)
        {
            _w = w; _p = p; _persona = persona; _champion = champion; _reportAt = reportAt;
            _rng = new Random(seed * 7919 + (int)(CardUnlocks.StableHash(persona) % 1000));
            _earned0 = new Dictionary<string, int>(p.DpEarned);
            _spent0 = new Dictionary<string, int>(p.DpSpent);
            _dp0 = p.Dp;
        }

        private bool Collector => _persona == "collector";
        private bool Gambler => _persona == "gambler";
        private bool Frugal => _persona == "frugal";

        private bool Legal(Deck deck, FormatRules rules) =>
            deck.Main.Count >= DeckRules.MainMin && DeckRules.Validate(deck, rules.List ?? Banlist.None, _w.Cards).Count == 0
            && deck.Main.Concat(deck.Extra).All(c => DeckRules.Released(Banlist.BaseCode(c, _w.Cards), rules.Pool, _w.Printings, _w.Cards));

        private Deck LoanerList(string recipe, FormatRules rules)
        {
            var d = _w.LoadDeck(recipe)!;
            return Loaners.Build(d, rules.List ?? Banlist.None, _w.Cards, c => DeckRules.Released(c, rules.Pool, _w.Printings, _w.Cards));
        }

        /// <summary>The cheaper of Make it yours (outright) and recipe packs for a cup-legal list of <paramref name="recipe"/>: buys it, or false.</summary>
        private bool Buy(string recipe, FormatRules rules, string where)
        {
            var list = LoanerList(recipe, rules);
            int packPrice = RecipeShop.PackPriceFor(recipe, _w.Circuits);
            int outright = RecipeShop.CompletePrice(list, _p, Banlist.None, 9999, _w.Printings, _w.Cards, packPrice);
            int copies = RecipeShop.Missing(list, _p, Banlist.None, 9999, _w.Printings, _w.Cards).Values.Sum();
            bool inBook = RecipeBook.Unlocked(_w.Circuits, _p).Any(r => r.Deck == recipe);
            int packs = (copies + PackCatalog.PackSize - 1) / PackCatalog.PackSize * packPrice;
            int dpBefore = _p.Dp;
            if (copies > 0 && !Frugal && inBook && packs < outright && _p.Dp >= packs)
            {
                while (_p.Dp >= packPrice && RecipeShop.Missing(list, _p, Banlist.None, 9999, _w.Printings, _w.Cards).Count > 0)
                    RecipeShop.OpenPack(list, _p, Banlist.None, 9999, _w.Printings, _w.Cards, _rng, packPrice);
            }
            else if (copies > 0 && (outright > _p.Dp || RecipeShop.Complete(list, _p, Banlist.None, 9999, _w.Printings, _w.Cards, packPrice, Ledger.MakeItYours) == 0))
                return false;
            var mine = list.Clone();
            mine.Name = recipe + " (recipe)";
            mine.Side.Clear();
            mine.Campaign = true;
            _owned[recipe] = mine;
            _log.Add($"    bought {recipe} for {where}: {dpBefore - _p.Dp:N0} DP ({copies} copies; outright {outright:N0}, packs ~{packs:N0})");
            return true;
        }

        /// <summary>The deck options at a match: (name, recipe, win rate, a loaner?). Yours where it's legal, the advice's recipes as loaners.</summary>
        private List<(string recipe, double p)> Options(int cup, int match) =>
            _w.Advice.For($"{cup}.{match}").Where(o => o.Borrows && _w.DeckFiles.ContainsKey(o.Deck)).Select(o => (o.Deck, o.Percent / 100.0)).ToList();

        /// <summary>The cup's best measured deck: the fewest expected duels (sum of 1/p) over its matches.</summary>
        private (string recipe, double duels) BestDeck(int cup, IEnumerable<int> matches)
        {
            var all = matches.SelectMany(m => Options(cup, m)).Select(o => o.recipe).Distinct();
            return all.Select(r => (r, matches.Sum(m => 1 / Math.Max(0.05, Options(cup, m).Where(o => o.recipe == r).Select(o => o.p).DefaultIfEmpty(0.25).Max()))))
                .OrderBy(x => x.Item2).FirstOrDefault();
        }

        private double Rate(int cup, int match, string recipe) => Options(cup, match).Where(o => o.recipe == recipe).Select(o => o.p).DefaultIfEmpty(0.25).Max();

        public int Play()
        {
            var lib = _w.Library;
            Console.WriteLine($"\n== {_persona}: start {_p.Dp:N0} DP, level {_p.Level}, {_p.Collection.Total} copies, {_champion.Count} champion decks");
            bool reported = false;
            for (int cup = 0; cup < lib.Cups.Count; cup++)
            {
                var c = lib.Cups[cup];
                var rules = lib.Rules(cup, _w.Lists);
                var matches = Enumerable.Range(0, c.Matches.Count).Where(m => !c.Matches[m].Champion).ToList();
                var (best, expect) = BestDeck(cup, matches);
                if (best == null) { Console.WriteLine($"  {c.Name}: no measured deck"); continue; }
                // A deck of yours legal here that the advice measured, else buy the best one (frugal: borrow it).
                var mine = _owned.Where(o => Legal(o.Value, rules) && matches.All(m => Options(cup, m).Any(x => x.recipe == o.Key)))
                    .OrderBy(o => matches.Sum(m => 1 / Math.Max(0.05, Rate(cup, m, o.Key)))).Select(o => o.Key).FirstOrDefault();
                if (mine != null && matches.Sum(m => 1 / Math.Max(0.05, Rate(cup, m, mine))) > expect + 1.5) mine = null; // much worse: get the best
                if (mine == null && !Frugal && Buy(best, rules, c.Name)) mine = best;
                string recipe = mine ?? best;
                bool loaner = mine == null;
                int tries = 0;
                foreach (var m in matches)
                {
                    double pr = Rate(cup, m, recipe);
                    int t = 0;
                    while (!lib.HasWon(_p, cup, m) && t++ < MaxTries)
                    {
                        int winner = _rng.NextDouble() < pr ? 0 : 1;
                        lib.Record(_p, cup, m, winner, loaner, built: !loaner, deck: loaner ? "" : _owned[recipe].Name);
                        _duels++;
                        Collect();
                    }
                    tries += t;
                    if (!lib.HasWon(_p, cup, m)) _walls++;
                }
                bool cleared = lib.IsCleared(cup, _p);
                _log.Add($"  {c.Name}: {(cleared ? "cleared" : "NOT cleared")} with {recipe}{(loaner ? " (loaner)" : "")} in {tries} duels{(lib.HasStamp(cup, _p) ? ", stamped" : "")}");
                if (cleared) AfterClear(cup, recipe, loaner);
                Flush();
                if (!reported && lib.ClearedCount(_p) >= _reportAt) { Report($"after {_reportAt} cups"); reported = true; }
            }
            if (lib.FinalOpen(_p)) PlayFinal();
            Flush();
            Report("at the end");
            return 0;
        }

        private void PlayFinal()
        {
            var lib = _w.Library;
            for (int r = 0; r < lib.Rounds.Count; r++)
            {
                var rules = lib.RoundRules(r, _w.Lists);
                int cup = lib.Rounds[r].Cup;
                int boss = lib.Cups[cup].Matches.FindLastIndex(m => !m.Champion);
                var mine = _owned.Where(o => Legal(o.Value, rules)).OrderByDescending(o => Rate(cup, boss, o.Key)).Select(o => o.Key).FirstOrDefault();
                var best = Options(cup, boss).OrderByDescending(o => o.p).Select(o => o.recipe).FirstOrDefault();
                if ((mine == null || (best != null && Rate(cup, boss, best) > Rate(cup, boss, mine) + 0.1)) && best != null && Buy(best, rules, $"final round {r + 1}")) mine = best;
                if (mine == null) { _log.Add($"  final round {r + 1}: no deck of yours legal for it"); break; }
                int t = 0;
                while (!lib.FinalWon(_p, r) && t++ < MaxTries)
                {
                    lib.RecordFinal(_p, r, _rng.NextDouble() < Rate(cup, boss, mine) ? 0 : 1, _owned[mine].Name);
                    _duels++;
                    Collect();
                }
                _log.Add($"  final round {r + 1} ({lib.Cups[cup].Name} boss): {(lib.FinalWon(_p, r) ? "won" : "NOT won")} with {mine} in {t} duels");
                if (!lib.FinalWon(_p, r)) break;
            }
            if (lib.FinalCleared(_p)) _log.Add($"  {lib.Final.Name} won");
        }

        private void AfterClear(int cup, string recipe, bool loaner)
        {
            var lib = _w.Library;
            var c = lib.Cups[cup];
            if (Collector)
            {
                if (Cosmetics.BuyMat(_p, lib, cup)) _log.Add($"    mat {Cosmetics.MatId(c)}");
                if (!loaner && Cosmetics.Engrave(_p, lib, cup, _owned[recipe].Name, recipe)) _log.Add($"    engraved {c.Name}: {recipe}");
                CompleteSet(c.Era);
                Grade(5, gamble: false);
                BuyUsed(3);
            }
            if (Gambler)
            {
                Boosters(c.Era, 20);
                Grade(8, gamble: true);
                for (int entry = 0; entry < 3 && (_p.Locals.GetValueOrDefault(Library.Id(c))?.Titles ?? 0) == 0; entry++)
                    if (!Locals(cup, loaner ? Loaners.NameFor(recipe) : recipe, loaner)) break;
            }
            _w.Unlocks.CheckSets(_p);
        }

        /// <summary>One Locals bracket at a cup, your matches by Elo odds. False if it couldn't be entered.</summary>
        private bool Locals(int cup, string deck, bool loaner)
        {
            var lib = _w.Library;
            var b = Brackets.EnterLocals(_p, lib, cup, _w.Ratings, deck);
            if (b == null) return false;
            while (!b.Done && b.YourMatch() is int m and >= 0)
            {
                Brackets.StartYours(b);
                var (a, bb) = b.Pair(m);
                var you = b.Slots[b.YourSlot];
                var them = b.Slots[a == b.YourSlot ? bb : a];
                double odds = Brackets.Odds(you, them, _w.Matchups);
                Brackets.RecordYours(_p, lib, b, _rng.NextDouble() < odds ? 0 : 1, loaner, _w.Matchups);
                _localsDuels++;
                Collect();
            }
            Brackets.SkipToEnd(b, _w.Matchups);
            var reward = Brackets.Finish(_p, lib, b, _w.Prizes);
            _log.Add($"    {b.Name}: {reward?.Summary}");
            return true;
        }

        /// <summary>Opens boosters of a set from the cup's era until it's complete (at most 150 packs): set completion by packs.</summary>
        private void CompleteSet(int era)
        {
            var set = _w.Catalog.Sets.Where(s => s.Year.Length == 4 && int.Parse(s.Year) <= Math.Max(era, 2002) && !_p.SetsDone.ContainsKey(s.Info.Code)
                                                 && s.Pool.ContainsKey(Rarity.Common))
                .OrderByDescending(s => s.Year).ThenBy(s => s.CardCount).FirstOrDefault();
            if (set == null) return;
            int opened = 0, dp = _p.Dp;
            bool Done() => set.Pool.Values.SelectMany(x => x).All(_p.Collection.Owns);
            while (!Done() && opened < 150 && _p.Spend(set.Sink, set.Price)) { _p.OpenPack(set, _rng, out _); opened++; }
            _w.Unlocks.CheckSets(_p);
            _log.Add($"    {set.Info.Name}: {opened} packs, {dp - _p.Dp:N0} DP net, {(Done() ? "complete" : "not complete")}");
        }

        private void Boosters(int era, int packs)
        {
            var sets = _w.Catalog.Sets.Where(s => s.Year.Length == 4 && int.Parse(s.Year) <= Math.Max(era, 2002)).Take(6).ToList();
            for (int i = 0; i < packs && sets.Count > 0; i++)
            {
                var set = sets[_rng.Next(sets.Count)];
                if (!_p.Spend(set.Sink, set.Price)) break;
                _p.OpenPack(set, _rng, out _);
            }
        }

        private int NeededByDecks(uint code) => _owned.Values.Concat(_champion).Select(d => d.Main.Concat(d.Extra).Count(c => Banlist.BaseCode(c, _w.Cards) == code))
            .DefaultIfEmpty(0).Max();

        /// <summary>
        /// Grading: the collector sends its most valuable Raw copies (standard); the gambler sends Raw copies that look Mint
        /// express, and cracks and resubmits 9s, hoping for 10s.
        /// </summary>
        private void Grade(int count, bool gamble)
        {
            var picks = gamble
                ? _p.Collection.All.Where(c => !c.AtDesk && (c.IsSlab ? c.Slab == 9 : c.Shown == 9) && c.Submissions < 3)
                    .OrderByDescending(c => _p.Collection.Value(c)).Take(count).ToList()
                : _p.Collection.All.Where(c => !c.AtDesk && !c.IsSlab).OrderByDescending(c => _p.Collection.Value(c)).Take(count).ToList();
            int tens = 0;
            foreach (var copy in picks)
            {
                if (GradingDesk.Blocked(_p, copy, gamble, NeededByDecks) != null) continue;
                int slab = GradingDesk.Submit(_p, copy, gamble, NeededByDecks);
                if (slab == 10) tens++;
            }
            if (gamble && picks.Count > 0) _log.Add($"    graded {picks.Count} for 10s: {tens} came back 10");
        }

        private void Collect()
        {
            foreach (var copy in GradingDesk.AtDesk(_p).Where(c => GradingDesk.IsReady(_p, c)).ToList()) GradingDesk.Collect(_p, copy);
        }

        private void BuyUsed(int count)
        {
            var pool = _owned.Values.Concat(_champion).SelectMany(d => d.Main.Concat(d.Extra)).Select(c => Banlist.BaseCode(c, _w.Cards)).Distinct().ToList();
            if (pool.Count == 0) return;
            var picks = UsedMarket.Listings(_p, pool, 9999, _w.Printings, _w.Cards).Where(l => l.Copy.IsSlab && UsedMarket.Blocked(_p, l) == null)
                .OrderByDescending(l => l.Copy.Slab).ThenBy(l => l.Price).Take(count).ToList();
            foreach (var l in picks) UsedMarket.Buy(_p, l);
            if (picks.Count > 0) _log.Add($"    used market: {picks.Count} slabs for {picks.Sum(l => l.Price):N0} DP");
        }

        private void Flush()
        {
            foreach (var line in _log) Console.WriteLine(line);
            _log.Clear();
        }

        private void Report(string when)
        {
            var lib = _w.Library;
            var earned = CampaignSim.Diff(_p.DpEarned, _earned0);
            var spent = CampaignSim.Diff(_p.DpSpent, _spent0);
            int e = earned.Values.Sum(), s = spent.Values.Sum();
            Console.WriteLine($"  -- {_persona} {when}: {lib.ClearedCount(_p)} cups cleared, {lib.StampCount(_p)} stamped, final {(lib.FinalCleared(_p) ? "won" : "not won")}, "
                              + $"{_duels} cup/final duels + {_localsDuels} Locals duels, {_walls} walls; Locals {_p.Locals.Values.Sum(r => r.Entries)} entries, "
                              + $"{Brackets.TitlesTotal(_p)} titles; level {_p.Level}");
            CampaignSim.PrintLedger($"{_persona} {when}", earned, spent, _dp0, _p.Dp);
            double share = s / (double)Math.Max(1, _dp0 + e);
            string target = _persona switch { "minimal" => " (target: at least 15%)", "collector" => " (target: at least 40%)", _ => "" };
            Console.WriteLine($"    share spent of the champion's DP plus earnings: {share:P1}{target}");
        }
    }
}
