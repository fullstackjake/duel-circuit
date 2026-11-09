using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;
using Ygo.Engine.Presentation;

namespace Ygo.Harness;

/// <summary>
/// `--campaign-test`'s Back Alley checks (docs/dev/ALLEY-SPEC-2026-11-01.md section 16, chunk A3, checks 1-11 and 13; check
/// 12, the ledger, is <see cref="LedgerCheck"/>'s): the Alley list, legality, no leaks into cups, the turn cap, the data,
/// the splash decks, the night's determinism, the unlock, ante end to end, binders, the save and New Game+, and the story;
/// chunk B3's (<see cref="PhaseBChecks"/>): Contraband Packs, Moth's van, Orvel's Table and the rail, Unlisted, the
/// alley's posters, the held card, Penny, the van's voice and the alley's music; and chunk C3's (<see cref="PhaseCChecks"/>):
/// Nyx's Table and its `allow:` rule, the Alley Brawl and the Midnight Open through Finish (the bracket shapes, the Bo3
/// final and its save are <see cref="BracketChecks"/>'). Every check runs through the engine
/// (BackAlley, Ante, MothsVan, ContrabandPacks, Unlisted, TurnCap, DuelRunner) on scripted saves.
/// <para>
/// `ALLEY_BITE=pot|allow|turncap` (test-only) feeds the checks a broken input, to prove they bite: a splash deck given a
/// second Pot of Greed, alley-events.txt with an `allow:` naming Wind-Up Hunter, a TurnCap that ends on turn 40's start.
/// </para>
/// </summary>
public static partial class AlleyTest
{
    /// <summary>
    /// The pinned counts that move with the data (docs/dev/NO-LIMITS-SPEC-2026-11-02.md N7), recounted for NL-0: the
    /// Contraband Pack pool (the era-9999 ever-Forbidden cards with a printing, none excluded) and Moth's shelf's
    /// possible items and their cards. Recount and update them in the change that moves them.
    /// </summary>
    private const int ContrabandPool = 229, ShelfItems = 425, ShelfCards = 146;

    /// <summary>The alley's data and the context its engine calls need, as the app builds them (cached per root).</summary>
    public sealed class Setup
    {
        public AlleyData Data = null!;
        public AlleyContext Ctx = null!;
        public Banlist Traditional = null!, List = null!;
        public List<Banlist> Lists = null!;
        public string EventsText = "", Root = "";
        private ContrabandPacks? _packs;
        /// <summary>The Contraband Pack and its pool (the van's used tab and shelf draw from it), built once.</summary>
        public ContrabandPacks Packs => _packs ??= new ContrabandPacks(Ctx.Cards, Ctx.Printings, Lists, List);
    }

    private static readonly ConcurrentDictionary<string, Setup> _setups = new ConcurrentDictionary<string, Setup>();

    /// <summary>
    /// The alley as the app loads it: every ban list (vendor/LFLists and lflists), the Alley list from Traditional, decks
    /// by name (structure decks aside; each load a fresh copy), the ratings (deck-ratings.txt, then the anchored
    /// peak-ratings.txt), alley.txt's curation, the roster, the Library.
    /// </summary>
    public static Setup Load(ICardDataProvider cards, string root, PrintingIndex printings, IReadOnlyList<Personality> roster, Library library) =>
        _setups.GetOrAdd(root, _ =>
        {
            var lists = Banlist.LoadFolders(new[] { Path.Combine(root, "vendor", "LFLists"), Path.Combine(root, "lflists") }).ToList();
            var traditional = lists.First(l => l.Name.Contains("Traditional"));
            var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "structure" + Path.DirectorySeparatorChar))
                .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
            var decks = new ConcurrentDictionary<string, Deck?>();
            Deck? LoadDeck(string name)
            {
                var deck = decks.GetOrAdd(name, n => deckFiles.TryGetValue(n, out var f) ? Deck.LoadYdk(f) : null);
                if (deck == null) return null;
                var copy = deck.Clone();
                copy.Name = name;
                return copy;
            }
            var curatedFile = Path.Combine(root, "campaign", "alley.txt");
            var ctx = new AlleyContext
            {
                Cards = cards, Printings = printings, Lists = lists, List = Alley.List(traditional, cards), LoadDeck = LoadDeck,
                Ratings = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName), Path.Combine(root, "campaign", DeckRatings.PeakFileName)),
                Curated = File.Exists(curatedFile) ? Alley.LoadCurated(curatedFile, cards) : new Dictionary<string, Curation>(),
                Roster = roster, Library = library,
            };
            var eventsPath = Path.Combine(root, "campaign", AlleyData.FileName);
            var text = File.Exists(eventsPath) ? File.ReadAllText(eventsPath) : "";
            var data = AlleyData.Parse(text, cards);
            ctx.Data = data;
            return new Setup { Data = data, Ctx = ctx, Traditional = traditional, List = ctx.List, Lists = lists, EventsText = text, Root = root };
        });

    /// <summary>A fresh save that has cleared the first Library cup (so the alley is open), with a fixed salt.</summary>
    public static Profile Opened(Deck starter, ICardDataProvider cards, PrintingIndex printings, Library library, uint salt)
    {
        var p = Profile.New(starter, cards, printings);
        p.Collection.Salt = salt;
        p.Collection.CommonestOf = c => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
        p.LibraryOpen = true;
        for (int m = 0; m < library.Cups[0].Matches.Count; m++)
            if (!library.Cups[0].Matches[m].Champion) library.Record(p, 0, m, 0, loaner: false, built: true, deck: "mine");
        BackAlley.Open(p, library);
        return p;
    }

    /// <summary>One alley duel at a table's next seat through the engine: Ante.Hold, then BackAlley.Record (null if it couldn't start).</summary>
    public static AlleyResult? Duel(Profile p, Setup s, int table, int winner, int turns = 12, int first = 0)
    {
        var lineup = BackAlley.Lineup(p, s.Data, table, s.Ctx);
        int seat = BackAlley.NextSeat(p, s.Data, table);
        var o = lineup.FirstOrDefault(x => x.Seat == seat);
        if (o == null || Ante.Hold(p, s.Data, table, o, null, _ => 0, s.Ctx) == null) return null;
        return BackAlley.Record(p, s.Data, table, winner, turns, first, s.Ctx);
    }

    /// <summary>A save with Orvel's Table open (Dumpster Row, The Basement Game and The Black Market cleared) and DP to stake.</summary>
    public static Profile OrvelOpen(Deck starter, ICardDataProvider cards, PrintingIndex printings, Library library, uint salt, int dp = 50000)
    {
        var p = Opened(starter, cards, printings, library, salt);
        foreach (var id in new[] { "dumpster-row", "basement", "black-market" }) p.AlleyTables[id] = new AlleyTableState { Cleared = true };
        p.Earn(Ledger.Debug, dp);
        return p;
    }

    /// <summary>
    /// A spare slab of yours to stake at Orvel's Table, worth what one of his own slabs is (<paramref name="rank"/>: 0 his
    /// cheapest worth at least <see cref="AlleyEconomy.OrvelMinCard"/>, 1 his dearest), so his binder always has a match:
    /// a Prize copy, Paid 0, unless said otherwise. Into the collection.
    /// </summary>
    public static CardCopy SpareFor(Profile p, Setup s, double rank, CopySource source = CopySource.Prize, int paid = 0)
    {
        var slabs = Ante.Binder(p, "orvel", s.Ctx).Cards.Where(c => Ante.Value(c, s.Ctx) >= AlleyEconomy.OrvelMinCard).OrderBy(c => Ante.Value(c, s.Ctx)).ThenBy(c => c.Code).ToList();
        var copy = Ante.ToCopy(slabs[(int)Math.Round(rank * (slabs.Count - 1))], p);
        copy.Source = source;
        copy.Paid = paid;
        p.Collection.Insert(copy);
        return copy;
    }

    /// <summary>
    /// One duel at Orvel's Table through the engine (Ante.Hold with your card, then BackAlley.Record): <paramref name="yours"/>,
    /// or the first spare he has a match for. Null if it couldn't start.
    /// </summary>
    public static AlleyResult? OrvelDuel(Profile p, Setup s, CardCopy? yours, int winner, int turns = 12, int first = 0)
    {
        int table = s.Data.IndexOf("orvels-table");
        var o = BackAlley.Lineup(p, s.Data, table, s.Ctx).FirstOrDefault(x => x.Seat == BackAlley.NextSeat(p, s.Data, table));
        if (o == null) return null;
        yours ??= Ante.Stakeable(p, _ => 0, AlleyEconomy.OrvelMinCard).FirstOrDefault(c => Ante.Next(p, o.Binder, s.Ctx, c) != null);
        if (yours == null || Ante.Hold(p, s.Data, table, o, yours, _ => 0, s.Ctx) == null) return null;
        return BackAlley.Record(p, s.Data, table, winner, turns, first, s.Ctx);
    }

    /// <summary>A save's lines, as text (via a temp file: the save only writes to paths).</summary>
    public static string SaveText(Profile p)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ygo-alley-{Guid.NewGuid():N}.txt");
        p.Save(path);
        var text = File.ReadAllText(path);
        File.Delete(path);
        return text;
    }

    public static Profile FromText(string text, PrintingIndex printings, ICardDataProvider cards)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ygo-alley-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, text);
        var p = Profile.Load(path)!;
        File.Delete(path);
        p.Collection.CommonestOf = c => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
        VersionSetup.Apply(p, printings); // the text timeline and printings the economy checks stamp by (off unless a check turns it on)
        return p;
    }

    public static Profile RoundTrip(Profile p, PrintingIndex printings, ICardDataProvider cards) => FromText(SaveText(p), printings, cards);

    /// <summary>The save's alley lines (section 14's keys), in order.</summary>
    public static readonly HashSet<string> AlleyKeys = new HashSet<string>
    {
        "alley", "alleyduels", "alleytable", "alleybracket", "alleyopp", "headliner", "binder", "stake", "antelive", "antetheirs", "anteyours",
        "rail", "unlisted", "vanused", "vanshelf", "ante",
    };

    public static List<string> AlleyLines(string saveText) =>
        saveText.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => AlleyKeys.Contains(l.Split(' ')[0])).ToList();

    private static string Line(AlleyOpponent o) =>
        $"{o.Table} {o.Seat} {o.Night} {o.Kind} {o.Character} {o.Base} {o.Tint:X6} {string.Join(",", o.Added)} {string.Join(",", o.Cut)} {o.Handle}";

    /// <summary>An opponent as met (night aside): what must not change on a loss or a reload, and must change with a new night.</summary>
    private static string Face(AlleyOpponent o) => $"{o.Kind} {o.Character} {o.Base} {o.Tint:X6} {string.Join(",", o.Added)} {string.Join(",", o.Cut)} {o.Handle}";

    private static string DeckLine(Deck d) => $"{string.Join(",", d.Main)}|{string.Join(",", d.Extra)}|{d.Note("recipe")}";

    private static bool Same(Stake? a, Stake? b) =>
        a != null && b != null && a.Code == b.Code && a.Rarity == b.Rarity && a.Grade == b.Grade && a.Slab == b.Slab && a.Seed == b.Seed && a.Flag == b.Flag;

    private static string Bite => Environment.GetEnvironmentVariable("ALLEY_BITE") ?? "";

    public static int Run(ICardDataProvider cards, string root, PrintingIndex printings, List<Personality> roster, Circuits circuits, Library library, Deck starter)
    {
        int failures = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"\nThe Back Alley (ALLEY-SPEC section 16, A3, B3 and C3){(Bite.Length > 0 ? $" [ALLEY_BITE={Bite}]" : "")}:");
        var s = Load(cards, root, printings, roster, library);
        var data = s.Data;
        var ctx = s.Ctx;
        var alley = s.List;
        int none(uint _) => 0;
        string Name(uint c) => cards.Get(c)?.Name ?? c.ToString();
        uint Code(string name) => Alley.CodeOf(name, cards);
        var events = new HashSet<string>();
        AlleyResult? Play(Profile p, int table, int winner, int turns = 12, int first = 0)
        {
            var r = Duel(p, s, table, winner, turns, first);
            if (r != null) events.UnionWith(r.Events);
            return r;
        }
        int dumpster = data.IndexOf("dumpster-row"), basement = data.IndexOf("basement"), blackMarket = data.IndexOf("black-market");
        var regularTables = Enumerable.Range(0, data.Tables.Count).Where(i => data.Tables[i].Tier is AlleyTier.Street or AlleyTier.Den && data.Tables[i].Ready).ToList();

        // ---- 1. The list ----
        var excluded = Alley.Excluded.Select(n => (name: n, code: Code(n), matches: cards.All.Count(c => c.Alias == 0 && c.Name == n))).ToList();
        var excludedCodes = excluded.Select(e => e.code).ToHashSet();
        var never = Alley.Never.ToHashSet();
        var bossOnly = Alley.BossOnly.ToHashSet();
        Check($"the list: all {Alley.Excluded.Length} exclusions resolve to one card each, alias 0"
              + (excluded.Any(e => e.code == 0 || e.matches != 1) ? $" (not: {string.Join(", ", excluded.Where(e => e.code == 0 || e.matches != 1).Select(e => e.name))})" : ""),
            Alley.Excluded.Length == 35 && excluded.All(e => e.code != 0 && e.matches == 1 && cards.Get(e.code)?.Alias == 0));
        Check($"Never ({never.Count}) and BossOnly ({bossOnly.Count}) split Excluded: their union is Excluded, they're disjoint",
            never.Count == 10 && bossOnly.Count == 25 && !never.Overlaps(bossOnly) && never.Union(bossOnly).ToHashSet().SetEquals(Alley.Excluded));
        var changed = s.Traditional.Limits.Where(l => !excludedCodes.Contains(Banlist.BaseCode(l.Key, cards)) && (!alley.Limits.TryGetValue(l.Key, out var a) || a != l.Value)).ToList();
        var extraKeys = alley.Limits.Keys.Where(k => !s.Traditional.Limits.ContainsKey(k) && !excludedCodes.Contains(k)).ToList();
        Check($"the Alley list ({alley.Name}): every exclusion at 0, every other Traditional limit unchanged ({s.Traditional.Limits.Count} limits from {s.Traditional.Name})"
              + (changed.Count + extraKeys.Count > 0 ? $"; changed: {string.Join(", ", changed.Select(c => Name(c.Key)).Concat(extraKeys.Select(Name)).Take(5))}" : ""),
            excludedCodes.All(c => alley.Allowed(c, cards) == 0) && changed.Count == 0 && extraKeys.Count == 0 && !alley.Whitelist);

        // ---- 2. Legality ----
        var rules = Alley.Rules(alley);
        uint pot = Code("Pot of Greed");
        var legalBase = Loaners.Adjust(starter, alley, cards).Deck;
        legalBase.Side.Clear();
        legalBase.Main.RemoveAll(c => Banlist.BaseCode(c, cards) == pot);
        Deck WithPots(int n)
        {
            var d = legalBase.Clone();
            for (int i = 0; i < n; i++) { d.Main.RemoveAt(d.Main.Count - 1); d.Main.Insert(0, pot); }
            return d;
        }
        var onePot = DeckRules.Validate(WithPots(1), rules.List!, cards);
        var twoPots = DeckRules.Validate(WithPots(2), rules.List!, cards);
        var withHunter = WithPots(0);
        withHunter.Main[0] = Code("Wind-Up Hunter");
        var hunter = DeckRules.Validate(withHunter, rules.List!, cards);
        Check($"legality: Alley.Rules is the Alley list, no pool, 0 Ban Passes, forced; 1 Pot of Greed legal, 2 not (\"{twoPots.FirstOrDefault()?.Message}\"); "
              + $"an excluded card not (\"{hunter.FirstOrDefault()?.Message}\")",
            rules.List == alley && rules.Pool == "" && rules.Passes == 0 && rules.Forced && onePot.Count == 0
            && twoPots.Count == 1 && twoPots[0].Code == pot && twoPots[0].Message.Contains("limited to 1 in The Alley")
            && hunter.Count == 1 && hunter[0].Message.Contains("Forbidden in The Alley"));

        // ---- 3. No leaks ----
        var alleyOnly = new List<uint>();
        var probe = legalBase.Clone();
        for (int cup = 0; cup < library.Cups.Count; cup++)
        {
            var cupRules = library.Rules(cup, s.Lists);
            if (DeckRules.Validate(probe, cupRules.List ?? Banlist.None, cards).Any(p => alleyOnly.Contains(p.Code))) continue;
            // A card the Alley allows that this cup forbids: added once, it makes the deck illegal there.
            var add = alley.Limits.Where(l => l.Value >= 1 && cupRules.List != null && cupRules.List.Allowed(l.Key, cards) == 0 && !probe.Main.Contains(l.Key)
                                              && cards.Get(l.Key) is CardInfo i && !i.IsExtraDeckMonster).Select(l => l.Key).OrderBy(c => c).FirstOrDefault();
            if (add == 0) continue;
            probe.Main.RemoveAt(probe.Main.Count - 1);
            probe.Main.Add(add);
            alleyOnly.Add(add);
        }
        bool legalInAlley = DeckRules.Validate(probe, alley, cards).Count == 0;
        var stillLegal = Enumerable.Range(0, library.Cups.Count).Where(c => library.Rules(c, s.Lists) is var r
            && !DeckRules.Validate(probe, r.List ?? Banlist.None, cards).Concat(DeckRules.Releases(probe, r.List ?? Banlist.None, r.Pool, printings, cards)).Any(p => alleyOnly.Contains(p.Code))).ToList();
        bool cupsClean = Enumerable.Range(0, library.Cups.Count).All(c => library.Rules(c, s.Lists).List is var l && l != alley && l?.Name != alley.Name)
                         && Enumerable.Range(0, library.Rounds.Count).All(r => library.RoundRules(r, s.Lists).List is var l && l != alley && l?.Name != alley.Name);
        bool circuitsClean = circuits.All.All(c => c.Banlist != alley.Name) && !s.Lists.Any(l => l.Name == alley.Name || l == alley);
        var stampProfile = Opened(starter, cards, printings, library, 0xA11E0003);
        int stamps0 = library.StampCount(stampProfile), libWins0 = stampProfile.LibraryWins.Values.Sum();
        for (int k = 0; k < BackAlley.Matches; k++) Play(stampProfile, dumpster, 0);
        Check($"no leaks: no cup, final round or circuit gets the Alley list (no installed list has its name); a deck legal only in the alley "
              + $"({string.Join(", ", alleyOnly.Select(Name))}) stays illegal at all {library.Cups.Count} cups; alley wins earn no stamp and no Library win",
            cupsClean && circuitsClean && legalInAlley && alleyOnly.Count > 0 && stillLegal.Count == 0
            && library.StampCount(stampProfile) == stamps0 && stampProfile.LibraryWins.Values.Sum() == libWins0 && stampProfile.AlleyTables["dumpster-row"].Cleared);

        // ---- 3b. The No Limits list (docs/dev/NO-LIMITS-SPEC-2026-11-02.md P2, 4.2, 14.2) ----
        var noLimits = Alley.NoLimitsList(cards);
        var power = Alley.PowerCards.Select(n => (name: n, code: Code(n), matches: cards.All.Count(c => c.Alias == 0 && c.Name == n))).ToList();
        var everForbidden = s.Lists.Where(l => l.Name.Contains("TCG") && !l.Whitelist).SelectMany(l => l.Limits).Where(l => l.Value == 0)
            .Select(l => Banlist.BaseCode(l.Key, cards)).ToHashSet();
        Check($"the No Limits list (\"{noLimits.Name}\"): exactly the {Alley.Excluded.Length} exclusions at 0, nothing else (Djinn Releaser of Rituals and "
              + $"Archnemeses Protos BossOnly); the {power.Count} power cards resolve to one card each, all ever-Forbidden, none excluded",
            noLimits.Name == "No Limits" && !noLimits.Whitelist && noLimits.Limits.Count == Alley.Excluded.Length
            && noLimits.Limits.All(l => l.Value == 0 && excludedCodes.Contains(l.Key))
            && bossOnly.Contains("Djinn Releaser of Rituals") && bossOnly.Contains("Archnemeses Protos")
            && power.Count == 8 && power.All(p => p.code != 0 && p.matches == 1 && everForbidden.Contains(p.code) && !excludedCodes.Contains(p.code)));
        var nlRules = Alley.Rules(noLimits);
        var threePots = WithPots(3);
        uint spoils = Code("Original Sinful Spoils - Snake-Eye"), djinn = Code("Djinn Releaser of Rituals");
        for (int k = 0; k < 3; k++) { threePots.Main.RemoveAt(threePots.Main.Count - 1); threePots.Main.Add(spoils); }
        var nlThree = DeckRules.Validate(threePots, nlRules.List!, cards);
        var nlHunter = DeckRules.Validate(withHunter, nlRules.List!, cards);
        var withDjinn = WithPots(0);
        withDjinn.Main[0] = djinn;
        var nlDjinn = DeckRules.Validate(withDjinn, nlRules.List!, cards);
        var alleyThree = DeckRules.Validate(threePots, alley, cards);
        Check($"No Limits legality: 3 Pot of Greed and 3 Original Sinful Spoils legal (the Alley refuses the deck: \"{alleyThree.FirstOrDefault()?.Message}\"); a Never card "
              + $"(\"{nlHunter.FirstOrDefault()?.Message}\") and a BossOnly card (\"{nlDjinn.FirstOrDefault()?.Message}\") not",
            nlThree.Count == 0 && alleyThree.Any(p => p.Code == pot) && nlHunter.Count == 1 && nlHunter[0].Message.Contains("Forbidden in No Limits")
            && nlDjinn.Count == 1 && nlDjinn[0].Code == djinn && nlRules.Pool == "" && nlRules.Passes == 0 && nlRules.Forced);
        bool nlCupsClean = Enumerable.Range(0, library.Cups.Count).All(c => library.Rules(c, s.Lists).List?.Name != noLimits.Name)
                           && Enumerable.Range(0, library.Rounds.Count).All(r => library.RoundRules(r, s.Lists).List?.Name != noLimits.Name);
        bool nlCircuitsClean = circuits.All.All(c => c.Banlist != noLimits.Name) && !s.Lists.Any(l => l.Name == noLimits.Name);
        var alleyAgain = Alley.List(s.Traditional, cards);
        Check("No Limits no leaks: no cup, final round or circuit gets the No Limits list and no installed list has its name; the Alley list is unchanged "
              + $"by it (Pot of Greed still {alley.Allowed(pot, cards)}, {alleyAgain.Limits.Count} limits both times)",
            nlCircuitsClean && nlCupsClean && alley.Allowed(pot, cards) == 1 && alleyAgain.Limits.Count == alley.Limits.Count
            && alleyAgain.Limits.All(l => alley.Limits.TryGetValue(l.Key, out var n) && n == l.Value) && alley.Name != noLimits.Name);

        // ---- 4. The turn cap ----
        TurnCapChecks(cards, root, Check);

        // ---- 5. Data ----
        var problems = SkipUnratedBrawl(data.Problems(ctx), data, ctx);
        if (Bite == "allow")
            problems = AlleyData.Parse(s.EventsText.Replace("| allow:", "| allow: Wind-Up Hunter"), cards).Problems(ctx);
        var withHunterAllow = AlleyData.Parse(s.EventsText.Replace("| allow:", "| allow: Wind-Up Hunter"), cards).Problems(ctx);
        var badBase = AlleyData.Parse(s.EventsText.Replace("bases: goat-control,", "bases: no-such-deck, goat-control,"), cards).Problems(ctx);
        Check($"data: {AlleyData.FileName} parses ({data.Tables.Count} tables, {regularTables.Count} ready regular tables, {data.Headliners.Count(h => h.Ready)} of "
              + $"{data.Headliners.Count} headliners ready, {data.Brackets.Count} brackets); Problems() is empty{(problems.Count > 0 ? ": " + string.Join("; ", problems.Take(4)) : "")}",
            problems.Count == 0 && data.Tables.Count == 6 && regularTables.Count == 3 && data.Headliners.Count(h => h.Ready) >= 2);
        Check($"data: Problems() catches an allow: naming Wind-Up Hunter (\"{withHunterAllow.FirstOrDefault()}\") and a base deck that doesn't exist",
            withHunterAllow.Any(p => p.Contains("Wind-Up Hunter")) && badBase.Any(p => p.Contains("no-such-deck")));

        // ---- 6. Splash decks ----
        const int SplashSeeds = 1000;
        foreach (var ti in regularTables)
        {
            var t = data.Tables[ti];
            var e = t.Event!;
            var curation = ctx.Curated.TryGetValue(e.Name, out var cur) ? cur : null;
            var bases = t.Bases.Append(t.Closer).Distinct().Select(b => ctx.LoadDeck(b)!).ToList();
            int illegal = 0, short_ = 0, fallback = 0, banned = 0, archetypeCut = 0, recipe = 0;
            string firstBad = "";
            for (int k = 0; k < SplashSeeds; k++)
            {
                var b = bases[k % bases.Count];
                var splash = Alley.SplashChecked(b, e, ctx.Pool(e.EraCap), alley, unchecked((int)Hash.Mix(0x5EED, (uint)ti, (uint)k)), cards, curation, out var sub);
                if (Bite == "pot" && k == 0) { splash.Deck.Main.RemoveAt(0); splash.Deck.Main.Add(pot); splash.Deck.Main.RemoveAt(0); splash.Deck.Main.Add(pot); }
                var bad = DeckRules.Validate(splash.Deck, alley, cards);
                if (bad.Count > 0) { illegal++; if (firstBad.Length == 0) firstBad = $"{b.Name}: {bad[0].Message}"; }
                if (splash.Added.Count != Alley.SplashSize(e, b.Name, curation)) short_++;
                if (sub < 0) fallback++;
                if (splash.Deck.Main.Concat(splash.Deck.Extra).Any(c => excludedCodes.Contains(Banlist.BaseCode(c, cards)))) banned++;
                var archetype = b.Main.Concat(b.Extra).Select(cards.Get).OfType<CardInfo>().SelectMany(c => c.Setcodes).GroupBy(x => x).Where(g => g.Count() >= 6).Select(g => g.Key).ToHashSet();
                if (splash.Cut.Any(c => cards.Get(c) is CardInfo ci && ci.Setcodes.Any(archetype.Contains))) archetypeCut++;
                if (splash.Deck.Note("recipe") != b.Name) recipe++;
            }
            Check($"splash decks, {t.Name}: {SplashSeeds} seeds over {bases.Count} bases: legal under the Alley list ({illegal} not{(firstBad.Length > 0 ? ": " + firstBad : "")}), "
                  + $"full splash ({short_} short), no fallback ({fallback}), no excluded card ({banned}), archetype kept ({archetypeCut} cut one), #recipe = base ({recipe} not)",
                illegal + short_ + fallback + banned + archetypeCut + recipe == 0);
        }

        // ---- 7. Determinism ----
        {
            var p = Opened(starter, cards, printings, library, 0xA11E0007);
            var first = BackAlley.Lineup(p, data, dumpster, ctx).Select(Line).ToList();
            var decks = BackAlley.Lineup(p, data, dumpster, ctx).Select(o => DeckLine(BackAlley.DeckOf(o, ctx))).ToList();
            var again = BackAlley.Lineup(p, data, dumpster, ctx).Select(Line).ToList();
            var twin = Opened(starter, cards, printings, library, 0xA11E0007);
            var twinLine = BackAlley.Lineup(twin, data, dumpster, ctx).Select(Line).ToList();
            var loss = Play(p, dumpster, 1);
            var afterLoss = BackAlley.Lineup(p, data, dumpster, ctx).Select(Line).ToList();
            var loaded = RoundTrip(p, printings, cards);
            var afterLoad = BackAlley.Lineup(loaded, data, dumpster, ctx).Select(Line).ToList();
            var loadedDecks = BackAlley.Lineup(loaded, data, dumpster, ctx).Select(o => DeckLine(BackAlley.DeckOf(o, ctx))).ToList();
            var resolved = BackAlley.Lineup(loaded, data, dumpster, ctx).Select(o => BackAlley.ResolveDeck(loaded, o.DeckKey, ctx) is Deck d ? DeckLine(d) : "").ToList();
            var faces0 = BackAlley.Lineup(p, data, dumpster, ctx).Select(Face).ToList();
            for (int k = 0; k < BackAlley.Matches; k++) Play(p, dumpster, 0);
            var faces1 = BackAlley.Lineup(p, data, dumpster, ctx).Select(Face).ToList();
            int differ = faces0.Zip(faces1).Count(f => f.First != f.Second);
            Check($"determinism: a lineup drawn twice, or from the same salt, is identical; a loss, a save and a load change nothing (seats, decks, #recipe, ResolveDeck); "
                  + $"the 4th win draws a new night ({differ} of {faces0.Count} seats differ)",
                first.Count == 4 && first.SequenceEqual(again) && first.SequenceEqual(twinLine) && loss != null && !loss.SeatWon && first.SequenceEqual(afterLoss)
                && first.SequenceEqual(afterLoad) && decks.SequenceEqual(loadedDecks) && decks.SequenceEqual(resolved) && p.AlleyTables["dumpster-row"].Night == 1 && differ == faces0.Count);

            // The wildcard: with two ready headliners at a table, it takes one of seats 1-3 in about a third of nights. Seating only
            // (a context with no base decks, so no splash is built): the roll comes from the night's own seed, before any seat.
            var two = AlleyData.Parse(s.EventsText, cards);
            two.FindHeadliner("return-rook")!.Ready = true;
            // And with one: Yata Mags alone (Return Rook not ready).
            var oneReady = AlleyData.Parse(s.EventsText, cards);
            oneReady.FindHeadliner("return-rook")!.Ready = false;
            var seating = new AlleyContext
            {
                Cards = cards, Printings = printings, Lists = s.Lists, List = alley, Ratings = ctx.Ratings, Curated = ctx.Curated, Roster = roster, Library = library,
                LoadDeck = name => two.Headliners.Any(h => h.Deck == name) ? ctx.LoadDeck(name) : null, Data = two,
            };
            const int Nights = 3000;
            int wild = 0, mags = 0, wildOne = 0, regularCloser = 0;
            var q = Opened(starter, cards, printings, library, 0xA11E0077);
            var single = Opened(starter, cards, printings, library, 0xA11E0077);
            for (int n = 0; n < Nights; n++)
            {
                q.AlleyTables["dumpster-row"] = new AlleyTableState { Night = n };
                var seats = BackAlley.Lineup(q, two, dumpster, seating);
                if (seats.Any(o => o.Kind == "wildcard" && o.Seat is >= 1 and <= 3)) wild++;
                if (seats[3].Character == "yata-mags") mags++;
                single.AlleyTables["dumpster-row"] = new AlleyTableState { Night = n };
                var one = BackAlley.Lineup(single, oneReady, dumpster, new AlleyContext
                {
                    Cards = cards, Printings = printings, Lists = s.Lists, List = alley, Ratings = ctx.Ratings, Curated = ctx.Curated, Roster = roster, Library = library,
                    LoadDeck = seating.LoadDeck, Data = oneReady,
                });
                if (one.Any(o => o.Kind == "wildcard")) wildOne++;
                if (one[3].Kind != "headliner" || one[3].Character != "yata-mags") regularCloser++;
            }
            double share = wild / (double)Nights;
            Check($"the wildcard: with two ready headliners it sits in {share:P1} of {Nights} nights (target {AlleyEconomy.WildcardChance:P1} ± 2%), the closer is "
                  + $"either one about half the time (Yata Mags {mags / (double)Nights:P0}); with one ready headliner, never ({wildOne}) and it closes every night ({regularCloser} not)",
                Math.Abs(share - AlleyEconomy.WildcardChance) <= 0.02 && Math.Abs(mags / (double)Nights - 0.5) <= 0.03 && wildOne == 0 && regularCloser == 0);
        }

        // ---- 8. Unlock ----
        {
            var fresh = Profile.New(starter, cards, printings);
            fresh.LibraryOpen = true;
            bool shut = !BackAlley.Open(fresh, library) && !fresh.AlleyOpen && BackAlley.NextSeat(fresh, data, dumpster) == -1 && BackAlley.Lineup(fresh, data, dumpster, ctx).Count == 0;
            fresh.Locals["x"] = new LocalsRecord { Entries = 1, Titles = 1, Best = 0 };
            bool notByLocals = !BackAlley.Open(fresh, library);
            for (int m = 0; m < library.Cups[0].Matches.Count - 1; m++) library.Record(fresh, 0, m, 0, loaner: false, built: true, deck: "mine");
            bool notYet = library.ClearedCount(fresh) > 0 || !BackAlley.Open(fresh, library);
            var cleared = Opened(starter, cards, printings, library, 0xA11E0008);
            bool opensOnce = cleared.AlleyOpen && !BackAlley.Open(cleared, library);
            bool tables = BackAlley.IsUnlocked(cleared, data, dumpster) && !BackAlley.IsUnlocked(cleared, data, basement) && !BackAlley.IsUnlocked(cleared, data, blackMarket);
            // An old save: a cup cleared before the alley shipped (no "alley 1" line) opens at load.
            var old = FromText(string.Join("\n", SaveText(cleared).Split('\n').Where(l => !l.StartsWith("alley "))), printings, cards);
            bool oldOpens = !old.AlleyOpen && BackAlley.Open(old, library) && old.AlleyOpen;
            // Orvel's Table (Phase B) and Nyx's (Phase C) open with The Black Market's clear.
            bool orvelShut = !BackAlley.IsUnlocked(cleared, data, data.IndexOf("orvels-table")) && !BackAlley.IsUnlocked(cleared, data, data.IndexOf("nyx"));
            foreach (var id in new[] { "dumpster-row", "basement", "black-market" }) cleared.AlleyTables[id] = new AlleyTableState { Cleared = true };
            bool soon = BackAlley.IsUnlocked(cleared, data, blackMarket) && orvelShut && BackAlley.IsUnlocked(cleared, data, data.IndexOf("orvels-table"))
                        && BackAlley.IsUnlocked(cleared, data, data.IndexOf("nyx"));
            Check("unlock: shut with no cup cleared (no seat, no lineup), not by a Locals title, not one match short; opens exactly at the first cup clear, once; "
                  + "Dumpster Row only at first; an old save with a cleared cup opens at load; Orvel's and Nyx's Tables open with The Black Market's clear",
                shut && notByLocals && notYet && opensOnce && tables && oldOpens && soon);
        }

        // ---- 9. Ante ----
        {
            var p = Opened(starter, cards, printings, library, 0xA11E0009);
            p.Earn(Ledger.Debug, 20000);
            bool Balanced() => Ledger.Net(p) == p.Dp + (p.AnteLive?.Held ?? 0) - Profile.StartingDp;
            // These checks want a regular's stake at seat 1: Penny's cameo (her Common holds nothing of yours) sits this night out.
            var pennyChance = AlleyEconomy.PennyChance;
            AlleyEconomy.PennyChance = 0;
            var lineup = BackAlley.Lineup(p, data, dumpster, ctx);
            AlleyEconomy.PennyChance = pennyChance;
            var o = lineup[0];
            var stake = Ante.Next(p, o.Binder, ctx)!;
            int dp0 = p.Dp, spent0 = p.DpSpent.Values.Sum(), earned0 = p.DpEarned.Values.Sum();
            var live = Ante.Hold(p, data, dumpster, o, null, none, ctx);
            bool held = live != null && p.Dp == dp0 - AlleyEconomy.StreetStake && live.Held == AlleyEconomy.StreetStake && Same(live.Theirs, stake)
                        && p.DpSpent.Values.Sum() == spent0 && p.DpEarned.Values.Sum() == earned0 && !Ante.Binder(p, o.Binder, ctx).Cards.Contains(live.Theirs!) && Balanced();
            var reloaded = RoundTrip(p, printings, cards);
            bool liveTrips = reloaded.AnteLive is AnteLive rl && rl.Table == live!.Table && rl.Seat == live.Seat && rl.Held == live.Held && rl.StartedAt == live.StartedAt
                             && rl.Binder == live.Binder && Same(rl.Theirs, live.Theirs) && rl.Yours == null && reloaded.Dp == p.Dp;
            bool blockedTwice = Ante.Blocked(p, AlleyTier.Street, stake, null, none, ctx) != null && Ante.Hold(p, data, dumpster, o, null, none, ctx) == null;
            var win = BackAlley.Record(p, data, dumpster, 0, 14, 0, ctx);
            events.UnionWith(win.Events);
            var won = win.Ante?.Won;
            Check($"ante: Hold moves {AlleyEconomy.StreetStake} DP out of Dp with no ledger line, takes their card ({Name(stake.Code)}, worth {Ante.Value(stake, ctx):N0}); "
                  + "AnteLive round-trips a save; a second Hold is refused while one's held",
                held && liveTrips && blockedTwice && Ante.Value(stake, ctx) >= AlleyEconomy.StreetStake);
            Check($"ante: a win returns the stake (no ledger line) and adds their card as a Paid-0 Ante copy, its grade and seed kept; the Sell desk refuses it "
                  + $"(\"{(won != null ? SellDesk.Blocked(p, won, none) : "")}\") and pays 0; the seat is won",
                won != null && p.Dp == dp0 && p.DpSpent.Values.Sum() == spent0 && p.DpEarned.Values.Sum() == earned0 && won.Source == CopySource.Ante && won.Paid == 0
                && won.Code == stake.Code && won.Rarity == stake.Rarity && won.Grade == stake.Grade && won.Slab == stake.Slab && won.Seed == stake.Seed
                && p.Collection.All.Contains(won) && SellDesk.Blocked(p, won, none) == SellDesk.AnteRefusal && SellDesk.Sell(p, won, none) == 0
                && win.SeatWon && BackAlley.NextSeat(p, data, dumpster) == 2 && win.Events.Contains("antewin") && p.AnteWins == 1 && Balanced());

            // A loss: the stake spent to alley.stake, their card back on top of its binder, the seat as it was.
            var seat2 = lineup[1];
            AnteSettlement? Settle(int winner, int turns, int first, out AlleyResult? result, bool quit = false)
            {
                result = null;
                if (Ante.Hold(p, data, dumpster, seat2, null, none, ctx) == null) return null;
                if (quit) return Ante.SettleAbandoned(p, ctx);
                result = BackAlley.Record(p, data, dumpster, winner, turns, first, ctx);
                events.UnionWith(result.Events);
                return result.Ante;
            }
            var top = Ante.Next(p, seat2.Binder, ctx)!;
            int lost0 = p.DpSpent.GetValueOrDefault(Ledger.AlleyStake), dpBefore = p.Dp, duels0 = p.AlleyDuels;
            var lost = Settle(1, 9, 0, out var lossResult);
            bool lossOk = lost?.Outcome == "loss" && p.Dp == dpBefore - AlleyEconomy.StreetStake && p.DpSpent.GetValueOrDefault(Ledger.AlleyStake) == lost0 + AlleyEconomy.StreetStake
                          && Same(Ante.Binder(p, seat2.Binder, ctx).Cards[0], top) && BackAlley.NextSeat(p, data, dumpster) == 2 && lossResult!.Events.Contains("antelose") && Balanced();
            // A second-turn loss going second is still a loss: the guard is the first turn only.
            var lateLoss = Settle(1, 2, 1, out _);
            var quit = Settle(1, 0, 0, out _, quit: true);
            bool quitOk = quit?.Outcome == "quit" && quit.Summary.StartsWith("You left Dumpster Row mid-duel. That counts as a loss.") && p.AnteLive == null
                          && p.DpSpent.GetValueOrDefault(Ledger.AlleyStake) == lost0 + 3 * AlleyEconomy.StreetStake && Same(Ante.Binder(p, seat2.Binder, ctx).Cards[0], top)
                          && p.AlleyDuels == duels0 + 3 && BackAlley.NextSeat(p, data, dumpster) == 2 && Balanced();
            // A save loaded with a held duel settles it as a loss.
            Ante.Hold(p, data, dumpster, seat2, null, none, ctx);
            var crashed = RoundTrip(p, printings, cards);
            int crashedDp = crashed.Dp;
            var atLoad = Ante.SettleAbandoned(crashed, ctx);
            bool loadOk = atLoad?.Outcome == "quit" && crashed.AnteLive == null && crashed.Dp == crashedDp && Ledger.Net(crashed) == crashed.Dp - Profile.StartingDp
                          && crashed.DpSpent.GetValueOrDefault(Ledger.AlleyStake) == lost0 + 4 * AlleyEconomy.StreetStake && Same(Ante.Binder(crashed, seat2.Binder, ctx).Cards[0], top);
            Ante.SettleAbandoned(p, ctx);
            Check($"ante: a loss and a quit spend the stake to {Ledger.AlleyStake} and put their card back on top ({Name(top.Code)}); a 2nd-turn loss is a loss; "
                  + "a save loaded mid-duel settles as a loss; the seat stays as it was",
                lossOk && lateLoss?.Outcome == "loss" && quitOk && loadOk);
            int dpDraw = p.Dp, stakeSpent = p.DpSpent.GetValueOrDefault(Ledger.AlleyStake);
            var draw = Settle(2, AlleyEconomy.AlleyTurns + 1, 0, out var drawResult);
            var guard = Settle(1, 1, 1, out var guardResult);
            var den = Opened(starter, cards, printings, library, 0xA11E0099);
            den.AlleyTables["dumpster-row"] = new AlleyTableState { Cleared = true };
            den.Earn(Ledger.Debug, 5000);
            var guardDen = Duel(den, s, basement, 1, 1, 1);
            Check("ante: a draw (turn 41) and the first-turn guard (they went first and won on turn 1) return the stake with no line and their card; "
                  + "the seat is played again; the guard holds at a Den table too",
                draw?.Outcome == "draw" && guard?.Outcome == "firstturn" && p.Dp == dpDraw && p.DpSpent.GetValueOrDefault(Ledger.AlleyStake) == stakeSpent
                && Same(Ante.Binder(p, seat2.Binder, ctx).Cards[0], top) && drawResult!.Replay && drawResult.Events.Contains("turncap") && guardResult!.Events.Contains("firstturn")
                && BackAlley.NextSeat(p, data, dumpster) == 2 && guardDen?.Ante?.Outcome == "firstturn" && guardDen.Ante.Lost == 0 && guardDen.Ante.Returned == AlleyEconomy.DenStake
                && Balanced());

            // A won 4th copy: three bought Raw copies of their next card, then the win.
            var next = Ante.Next(p, seat2.Binder, ctx)!;
            while (p.Collection.Owned(next.Code) < Collection.MaxCopies)
            {
                var c = p.Collection.NewCopy(next.Code, printings.Commonest(next.Code, cards.Get(next.Code)?.Alias ?? 0), CopySource.Shop);
                c.Grade = 1;
                c.Paid = 200;
                p.Spend(Ledger.Order, 200);
                p.Collection.Insert(c);
            }
            int earnedBefore = p.DpEarned.Values.Sum(), dpFourth = p.Dp;
            var fourth = Settle(0, 10, 0, out _);
            Check($"ante: a won 4th copy ({Name(next.Code)}) keeps your best three and pays 0 DP (the displaced copy's refund is dropped)",
                fourth?.Outcome == "win" && fourth.Added != null && p.Collection.Owned(next.Code) == Collection.MaxCopies && p.DpEarned.Values.Sum() == earnedBefore
                && p.Dp == dpFourth && (fourth.Added.Value.Kept ? p.Collection.All.Contains(fourth.Won!) : !p.Collection.All.Contains(fourth.Won!)) && Balanced());

            // An empty binder: nothing of theirs, nothing of yours held; leaving still counts as a loss at the seat.
            var seat3 = BackAlley.Lineup(p, data, dumpster, ctx)[2];
            var emptied = Ante.Binder(p, seat3.Binder, ctx);
            emptied.Cards.Clear();
            emptied.RefilledAt = p.AlleyDuels;
            int dpEmpty = p.Dp, cardsBefore = p.Collection.Total;
            var emptyLive = Ante.Hold(p, data, dumpster, seat3, null, none, ctx);
            bool emptyHeld = emptyLive != null && emptyLive.Held == 0 && emptyLive.Theirs == null && p.Dp == dpEmpty;
            var emptyWin = BackAlley.Record(p, data, dumpster, 0, 10, 0, ctx);
            var poor = Opened(starter, cards, printings, library, 0xA11E0019);
            poor.Spend(Ledger.Debug, poor.Dp - 100);
            var poorSeat = BackAlley.Lineup(poor, data, dumpster, ctx)[0];
            Check($"ante: an empty binder holds nothing of yours and pays no card (the seat is still won); with {poor.Dp} DP the {AlleyEconomy.StreetStake} DP stake is refused "
                  + $"(\"{Ante.Blocked(poor, AlleyTier.Street, Ante.Next(poor, poorSeat.Binder, ctx), null, none, ctx)}\")",
                emptyHeld && emptyWin.SeatWon && emptyWin.Ante?.Won == null && p.Dp == dpEmpty && p.Collection.Total == cardsBefore
                && Ante.Hold(poor, data, dumpster, poorSeat, null, none, ctx) == null && poor.AnteLive == null);

            // A concession is a quit: the first-turn guard never applies, even when they went first and you gave up on turn 1.
            var q = Opened(starter, cards, printings, library, 0xA11E0071);
            q.Earn(Ledger.Debug, 5000);
            var qSeat = BackAlley.Lineup(q, data, dumpster, ctx)[0];
            int qSpent = q.DpSpent.GetValueOrDefault(Ledger.AlleyStake);
            Ante.Hold(q, data, dumpster, qSeat, null, none, ctx);
            var conceded = BackAlley.Record(q, data, dumpster, 1, 1, 1, ctx, conceded: true);
            Ante.Hold(q, data, dumpster, qSeat, null, none, ctx);
            var guarded = BackAlley.Record(q, data, dumpster, 1, 1, 1, ctx);
            Check($"ante: conceding on their first turn is a loss ({conceded.Ante?.Outcome}, -{conceded.Ante?.Lost} DP); the same loss not conceded takes the guard ({guarded.Ante?.Outcome})",
                conceded.Ante?.Outcome == "loss" && conceded.Ante.Lost == AlleyEconomy.StreetStake && !conceded.Events.Contains("firstturn")
                && q.DpSpent.GetValueOrDefault(Ledger.AlleyStake) == qSpent + AlleyEconomy.StreetStake && guarded.Ante?.Outcome == "firstturn");

            // "turncap" comes from the cap's own decision (a lower cap too), not the turn count; without one, turns past AlleyTurns.
            Ante.Hold(q, data, dumpster, qSeat, null, none, ctx);
            var lowCap = BackAlley.Record(q, data, dumpster, 2, 11, 0, ctx, turnCap: true);
            Ante.Hold(q, data, dumpster, qSeat, null, none, ctx);
            var longNoCap = BackAlley.Record(q, data, dumpster, 2, AlleyEconomy.AlleyTurns + 5, 0, ctx, turnCap: false);
            Ante.Hold(q, data, dumpster, qSeat, null, none, ctx);
            var byTurns = BackAlley.Record(q, data, dumpster, 2, AlleyEconomy.AlleyTurns + 1, 0, ctx);
            Check("story: turncap is raised by a cap that decided the duel at turn 11, not by a long duel the cap didn't decide, and by turns past 40 when no cap is said",
                lowCap.Events.Contains("turncap") && !longNoCap.Events.Contains("turncap") && byTurns.Events.Contains("turncap"));

            // The alley scenes' placeholders fill from the engine: the stake won ({card}, {grade}), the table, the handle, {nick}.
            Ante.Hold(q, data, dumpster, qSeat, null, none, ctx);
            var qTop = q.AnteLive!.Theirs!;
            var qWin = BackAlley.Record(q, data, dumpster, 0, 9, 0, ctx);
            var filled = Story.Fill("{card}|{grade}|{table}|{handle}|{nick}", null, q, fill: qWin.Fill);
            var antewinScene = Story.Load(Path.Combine(root, "campaign", "story.txt")).Scenes.First(sc => sc.Trigger == "antewin");
            var sceneText = string.Join(" ", antewinScene.Lines.Select(l => Story.Fill(l.Text, null, q, fill: qWin.Fill)));
            var fillWords = filled.Split('|');
            Check($"story: the alley's placeholders fill ({filled}); the first-stake-won scene reads \"{antewinScene.Lines[0].Text}\" as \"{Story.Fill(antewinScene.Lines[0].Text, null, q, fill: qWin.Fill)}\"; nothing carries over to a scene played without the fill",
                qWin.Ante?.Outcome == "win" && fillWords[0] == Name(qTop.Code) && fillWords[1] == (qTop.Slab > 0 ? $"Grade {qTop.Slab}" : Grades.RawLabel(qTop.Grade))
                && fillWords[2] == "Dumpster Row" && fillWords[3] == qSeat.Handle && fillWords[4] == BackAlley.NickLadder[0] && !sceneText.Contains('{')
                && Story.Fill("{nick}", null, Cleared(q)) == BackAlley.NickLadder[1] && Story.Fill("A {card}", null, q, card: "Sangan", fill: qWin.Fill) == "A Sangan"
                && Story.Fill("{card} at {table}", null, q) == "card at the table");
            Profile Cleared(Profile x) { x.AlleyTables["dumpster-row"] = new AlleyTableState { Cleared = true }; return x; }

            // Alley wins never reach the card unlocks: no bond, gift, drop or trade for alley-regular or a headliner, however
            // many wins (the CPU's staked card is the only prize); the alley still counts the wins itself.
            var unlocks = new CardUnlocks(cards, printings, circuits, roster, new HashSet<uint>(), ctx.LoadDeck, library: library);
            var b = Opened(starter, cards, printings, library, 0xA11E0B0D);
            b.Earn(Ledger.Debug, 20000);
            var alleyGrants = new List<Grant>();
            int addedWins = 0;
            for (int k = 0; k < 12; k++)
            {
                // Two nights (Mags closes the first), then encores against Mags.
                var r = k < 8 ? Duel(b, s, dumpster, 0, 9, 0)
                    : BackAlley.Encore(b, data, dumpster, "yata-mags", ctx) is AlleyOpponent e && Ante.Hold(b, data, dumpster, e, null, none, ctx) != null
                        ? BackAlley.Record(b, data, dumpster, 0, 9, 0, ctx) : null;
                if (r == null) continue;
                addedWins += r.Reward.WinsAdded.Count;
                alleyGrants.AddRange(unlocks.AfterDuel(b, r.Reward, "", null, false, false).Where(g => g.Character == BackAlley.RegularId || g.Character == "yata-mags"));
            }
            Check($"unlocks: {b.WinsAgainst(BackAlley.RegularId)} wins against alley regulars and {b.WinsAgainst("yata-mags")} against Yata Mags through CardUnlocks.AfterDuel: "
                  + $"{alleyGrants.Count} grants, {addedWins} wins added to rewards, no bond/gift unlock",
                b.WinsAgainst(BackAlley.RegularId) >= 3 && b.WinsAgainst("yata-mags") >= 3 && alleyGrants.Count == 0 && addedWins == 0
                && !b.Unlocks.Any(u => u.Contains(BackAlley.RegularId) || u.Contains("yata-mags")));
        }

        // ---- 10. Binders ----
        BinderChecks(s, starter, cards, printings, library, roster, Check);

        // ---- 11. Save and New Game+ ----
        {
            var p = Opened(starter, cards, printings, library, 0xA11E0011);
            p.Earn(Ledger.Debug, 50000);
            for (int k = 0; k < 6; k++) Play(p, dumpster, k % 3 == 1 ? 1 : 0);
            Play(p, basement, 0);
            BackAlley.Lineup(p, data, basement, ctx);
            // Every line the alley writes, Phase B's and C's too: a bracket record, the rail, Unlisted, the van, a held duel with your card.
            p.AlleyBrackets["brawl"] = new LocalsRecord { Entries = 2, Titles = 1, Best = 0 };
            p.Headliners["mass-driver-moss"] = new HeadlinerState { Met = 3, Beaten = 1, Encores = 1, Breaker = 2 };
            var railCopy = p.Collection.NewCopy(pot, Rarity.UltraRare, CopySource.Used);
            railCopy.Grade = 9; railCopy.Slab = 9; railCopy.Floor = 8; railCopy.Paid = 1234; railCopy.Submissions = 2;
            p.Rail.Add(new RailEntry { Copy = railCopy, Price = 4500, ExpiresAt = p.DuelsPlayed + 3 });
            p.UnlistedPages.Add(new UnlistedPage { Key = "dumpster-row.1.0", Table = "dumpster-row", Kind = "regular", Base = "goat-control", Added = { pot, 4031928 }, Cut = { 12345 }, Handle = "Three-Pot Teddy" });
            p.VanUsedEpoch = 4; p.VanUsedSold.Add(2); p.VanUsedSold.Add(7); p.VanShelfEpoch = 1; p.VanShelfSold.Add(0); p.VanShelfSoldCards.Add(MothsVan.ShelfKey(pot, Rarity.SecretRare, 10));
            var yours = p.Collection.All.First();
            p.Collection.Remove(yours);
            var heldStake = Ante.Next(p, BackAlley.Lineup(p, data, basement, ctx)[1].Binder, ctx);
            p.AnteLive = new AnteLive { Table = "basement", Seat = 2, Held = AlleyEconomy.DenStake, StartedAt = p.DuelsPlayed, Theirs = heldStake, Yours = yours, Binder = "regular:basement.2.0" };
            p.Dp -= AlleyEconomy.DenStake;
            var text = SaveText(p);
            var lines = AlleyLines(text);
            var back = FromText(text, printings, cards);
            var again = AlleyLines(SaveText(back));
            var keys = lines.Select(l => l.Split(' ')[0]).ToHashSet();
            var missingKeys = AlleyKeys.Where(k => !keys.Contains(k)).ToList();
            bool fields = back.AlleyOpen && back.AlleyDuels == p.AlleyDuels && back.AnteWins == p.AnteWins && back.AnteLosses == p.AnteLosses && back.Rail.Count == 1
                          && back.Rail[0].Copy.Id == railCopy.Id && back.Rail[0].Copy.Floor == 8 && back.Rail[0].Copy.Submissions == 2 && back.AnteLive?.Yours?.Id == yours.Id
                          && back.UnlistedPages.Count == p.UnlistedPages.Count && back.UnlistedPages.Last().Handle == "Three-Pot Teddy" && back.VanUsedSold.SetEquals(new[] { 2, 7 }) && back.VanShelfEpoch == 1
                          && back.AlleyOpponents.Select(Line).SequenceEqual(p.AlleyOpponents.OrderBy(o => o.Table, StringComparer.Ordinal).ThenBy(o => o.Seat).Select(Line));
            Check($"save: every alley line round-trips ({lines.Count} lines, {keys.Count} of {AlleyKeys.Count} keys{(missingKeys.Count > 0 ? "; not written: " + string.Join(", ", missingKeys) : "")}); "
                  + "handles with spaces, copies and held stakes come back exactly",
                lines.SequenceEqual(again) && missingKeys.Count == 0 && fields);
            var oldSave = FromText(string.Join("\n", text.Split('\n').Where(l => !AlleyKeys.Contains(l.Split(' ')[0]))), printings, cards);
            Check("save: an old save (no alley lines) loads with the defaults",
                !oldSave.AlleyOpen && oldSave.AlleyDuels == 0 && oldSave.AlleyTables.Count == 0 && oldSave.AlleyOpponents.Count == 0 && oldSave.Binders.Count == 0
                && oldSave.AnteLive == null && oldSave.Rail.Count == 0 && oldSave.UnlistedPages.Count == 0 && oldSave.VanUsedEpoch == -1 && oldSave.VanShelfEpoch == -1
                && oldSave.Headliners.Count == 0 && oldSave.AlleyBrackets.Count == 0 && oldSave.AnteWins + oldSave.AnteLosses == 0);

            // New Game+ keeps the alley (between duels: nothing held) and never pays a first clear twice.
            Ante.SettleAbandoned(back, ctx);
            back.Rail.Clear();
            var before = AlleyLines(SaveText(back));
            back.NewGamePlus();
            var after = AlleyLines(SaveText(back));
            int tokens = back.Tokens;
            var night = back.AlleyTables["dumpster-row"].Night;
            var clears = new List<AlleyResult?>();
            back.Earn(Ledger.Debug, 10000);
            for (int k = BackAlley.NextSeat(back, data, dumpster); k <= BackAlley.Matches; k++) clears.Add(Play(back, dumpster, 0));
            Check($"New Game+ keeps every alley line ({before.Count}); clearing Dumpster Row again after it pays no first clear (no token, no alleyclear: scene)",
                before.SequenceEqual(after) && clears.Count > 0 && clears.All(r => r != null) && clears.Last()!.NightOver && !clears.Any(r => r!.FirstClear)
                && back.Tokens == tokens + clears.Sum(r => r!.Reward.LevelUps.Sum(u => u.Tokens)) && !clears.Any(r => r!.Events.Any(e => e.StartsWith("alleyclear:")))
                && back.AlleyTables["dumpster-row"].Night == night + 1);
        }

        // ---- The first clear, headliners and the encore (scripted nights at Dumpster Row) ----
        // Dumpster Row has two ready headliners (Yata Mags, Return Rook): night 0's closer is either (its 50/50) and the other may
        // sit as its wildcard. The other closes the next night until it's beaten too; then the encore opens and the closer is a regular.
        {
            var p = Opened(starter, cards, printings, library, 0xA11E0012);
            p.Earn(Ledger.Debug, 20000);
            var lineup = BackAlley.Lineup(p, data, dumpster, ctx);
            var tableHeadliners = data.Tables[dumpster].Headliners.Where(id => data.FindHeadliner(id) is { Ready: true }).ToList();
            string closer0 = lineup[3].Character;
            string other = tableHeadliners.FirstOrDefault(id => id != closer0) ?? "";
            bool otherSat = lineup.Any(o => o.Kind == "wildcard" && o.Character == other);
            var results = Enumerable.Range(0, BackAlley.Matches).Select(_ => Play(p, dumpster, 0)).ToList();
            var clear = results.Last()!;
            bool firstClear = clear.FirstClear && clear.NightOver && clear.Reward.Tokens == AlleyEconomy.ClearTokens && clear.Events.Contains("alleyclear:Dumpster Row")
                              && clear.Opened.SequenceEqual(new[] { "The Basement Game" }) && BackAlley.IsUnlocked(p, data, basement) && lineup[3].Kind == "headliner"
                              && tableHeadliners.Count == 2 && tableHeadliners.Contains(closer0)
                              && clear.Events.Contains("headlinerbeat:" + closer0) && !p.Binders.Keys.Any(k => k.StartsWith("regular:dumpster-row.") && k.EndsWith(".0"));
            // The other headliner: beaten tonight as the wildcard, or it closes night 1 (no encore until both are beaten).
            bool encoreShut = otherSat || !BackAlley.EncoreOpen(p, data, dumpster);
            var night1 = BackAlley.Lineup(p, data, dumpster, ctx);
            bool otherCloses = otherSat || night1[3].Kind == "headliner" && night1[3].Character == other;
            AlleyResult? otherClear = null;
            if (!otherSat) for (int k = 0; k < BackAlley.Matches; k++) otherClear = Play(p, dumpster, 0);
            bool otherBeat = otherSat || otherClear != null && otherClear.NightOver && !otherClear.FirstClear && otherClear.Events.Contains("headlinerbeat:" + other);
            int nights = p.AlleyTables["dumpster-row"].Night;
            bool encoreOpen = BackAlley.EncoreOpen(p, data, dumpster);
            var encore = BackAlley.Encore(p, data, dumpster, closer0, ctx);
            var held = encore != null ? Ante.Hold(p, data, dumpster, encore, null, none, ctx) : null;
            var encoreWin = held != null ? BackAlley.Record(p, data, dumpster, 0, 12, 0, ctx) : null;
            if (encoreWin != null) events.UnionWith(encoreWin.Events);
            var after = BackAlley.Lineup(p, data, dumpster, ctx);
            Check($"first clear: Dumpster Row's 4th win pays {AlleyEconomy.ClearTokens} token, raises alleyclear: and headlinerbeat:{closer0} (night 0's closer), opens The Basement Game, "
                  + $"drops the night's binders; {other} {(otherSat ? "sat as the wildcard and was beaten that night" : "closes night 1 (no encore before) and is beaten")}; "
                  + $"the encore (seat 0, {closer0}'s binder) is won at the stake and raises encore; night {nights}'s closer is a regular",
                firstClear && encoreShut && otherCloses && otherBeat && encoreOpen && encore?.Seat == 0 && held?.Binder == "hl:" + closer0 && held.Held == AlleyEconomy.StreetStake
                && encoreWin?.Events.Contains("encore") == true && encoreWin.SeatWon && !p.AlleyOpponents.Any(o => o.Seat == 0) && p.AlleyTables["dumpster-row"].Night == (otherSat ? 1 : 2)
                && after[3].Kind == "regular" && after[3].Base == data.Tables[dumpster].Closer && after[3].Skill == 1.0);
        }

        // ---- Phase B: Orvel's Table, the rail, Moth's van, Contraband Packs, Unlisted, posters, the held card, Penny ----
        PhaseBChecks(s, starter, cards, printings, library, roster, circuits, root, Check, events);

        // ---- Phase C: Nyx's Table and the alley's brackets ----
        PhaseCChecks(s, starter, cards, printings, library, roster, Check, events);

        // ---- No Limits (NL-1 chunk 3): the nine check groups of docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 14.3 ----
        NoLimitsChecks(s, starter, cards, printings, library, roster, root, Check, events);

        // ---- 13. Story ----
        {
            var story = Story.Load(Path.Combine(root, "campaign", "story.txt"));
            var triggers = story.Scenes.Select(sc => sc.Trigger).ToHashSet();
            var phaseA = new List<string> { BackAlley.OpenEvent, BackAlley.VisitEvent, "antewin", "antelose", "turncap", "firstturn", "encore" };
            foreach (var ti in regularTables) { phaseA.Add("open:" + data.Tables[ti].Name); phaseA.Add("alleyclear:" + data.Tables[ti].Name); }
            // Phase B's: the van, the rail, Unlisted, Penny, the held card and Orvel's Table.
            phaseA.AddRange(new[] { BackAlley.FenceEvent, "rail", "buyback", "railgone", Unlisted.Event, BackAlley.PennyEvent, BackAlley.HeldEvent });
            foreach (var t in data.Tables.Where(t => t.Ready && t.Tier == AlleyTier.Orvel)) { phaseA.Add("open:" + t.Name); phaseA.Add("alleyclear:" + t.Name); }
            // Phase C's: Nyx's Table, the brackets' first entries (brawl, midnight, invitational) and their titles (bracketwin:<name>; plain
            // bracketwin stays the Locals' first title, ALLEY-STORY conflict 2).
            var phaseC = new List<string> { Brackets.BrawlKind, Brackets.MidnightKind, Brackets.InvitationalKind };
            foreach (var t in data.Tables.Where(t => t.Ready && t.Tier == AlleyTier.Nyx)) { phaseC.Add("open:" + t.Name); phaseC.Add("alleyclear:" + t.Name); }
            phaseC.AddRange(data.Brackets.Select(b => "bracketwin:" + b.Name)); // the Invitational's first entry and title included (NL-2)
            phaseA.AddRange(phaseC);
            foreach (var h in data.Headliners.Where(h => h.Ready && !h.IsBuild)) { phaseA.Add("headliner:" + h.Id); phaseA.Add("headlinerbeat:" + h.Id); }
            var noScene = phaseA.Where(t => !triggers.Contains(t)).ToList();
            var all = BackAlley.Triggers(data);
            var pending = all.Where(t => !triggers.Contains(t) && !phaseA.Contains(t)).ToList();
            var raised = events.Where(e => !e.StartsWith("level:")).ToList();
            var unknown = raised.Where(e => !all.Contains(e)).ToList();
            var unseen = raised.Where(e => !triggers.Contains(e)).ToList();
            var phaseBRaised = new[] { BackAlley.FenceEvent, "rail", "buyback", "railgone", Unlisted.Event, BackAlley.PennyEvent, BackAlley.HeldEvent, "alleyclear:Orvel's Table" };
            var notRaised = phaseC.Where(t => !raised.Contains(t)).ToList();
            Check($"story: every Phase A, B and C trigger has a scene ({phaseA.Count}, {phaseC.Count} of them Phase C's{(noScene.Count > 0 ? "; none for: " + string.Join(", ", noScene) : "")}); the {raised.Count} "
                  + $"the checks raised ({string.Join(", ", raised.OrderBy(x => x))}) are all BackAlley.Triggers and have scenes, every Phase C one among them"
                  + $"{(notRaised.Count > 0 ? " (not: " + string.Join(", ", notRaised) + ")" : "")}; plain {Brackets.WinEvent} is never the alley's; {pending.Count} pending for Phase D",
                noScene.Count == 0 && unknown.Count == 0 && unseen.Count == 0 && raised.Contains("antewin") && raised.Contains("antelose") && raised.Contains("firstturn")
                && phaseBRaised.All(raised.Contains) && notRaised.Count == 0 && !raised.Contains(Brackets.WinEvent) && !all.Contains(Brackets.WinEvent)
                && pending.All(t => t.StartsWith("headliner:") || t.StartsWith("headlinerbeat:") || t.StartsWith("combobreaker:") || BackAlley.IsNoLimitsTrigger(t)));
            Console.WriteLine($"         pending (Phase D: the headliners' show): {string.Join(", ", pending)}");
            var ids = roster.Select(r => r.Id).ToHashSet();
            var alleyScenes = story.Scenes.Where(sc => BackAlley.IsStoryTrigger(sc.Trigger, data)).ToList();
            var badSpeakers = alleyScenes.SelectMany(sc => sc.Lines).Select(l => l.Speaker).Where(sp => sp.Length > 0 && sp != "-" && sp != "opponent" && !ids.Contains(sp)).Distinct().ToList();
            var voicePath = Path.Combine(root, "campaign", "alley-voice.txt");
            var voice = ShopVoice.Load(voicePath);
            var voiceProblems = voice.Problems(roster, story.Scenes.Select(sc => sc.Id), circuits, h => BackAlley.IsVoiceHook(h, data, cards));
            var cast = new[] { "baz", "moth", "odile", "penny", "yata-mags", "mass-driver-moss" }.Where(id => !ids.Contains(id)).ToList();
            // Baz's game calls: a pool per later game of a best of three, each line naming its own game and no other (the app
            // picks once a game from mc:game:<n>, plain mc:game only as a game-neutral fallback).
            var voiceText = File.Exists(voicePath) ? File.ReadAllLines(voicePath) : Array.Empty<string>();
            List<string> PoolLines(string hook) => voiceText.SkipWhile(l => l.Trim() != "pool: " + hook).Skip(1)
                .TakeWhile(l => !l.StartsWith("pool:") && !l.StartsWith("# ----")).Where(l => l.Contains(':') && !l.TrimStart().StartsWith("#")).Select(l => l.ToLowerInvariant()).ToList();
            var gameWords = new Dictionary<int, string> { [2] = "game two", [3] = "game three" };
            var gamePools = gameWords.ToDictionary(g => g.Key, g => PoolLines($"mc:game:{g.Key}"));
            var neutral = PoolLines("mc:game");
            bool gameCalls = gamePools.All(g => g.Value.Count > 0 && g.Value.All(l => l.Contains(gameWords[g.Key]) && gameWords.Where(w => w.Key != g.Key).All(w => !l.Contains(w.Value))))
                             && neutral.All(l => gameWords.Values.All(w => !l.Contains(w)))
                             && BackAlley.IsVoiceHook("mc:game:2", data, cards) && BackAlley.IsVoiceHook("mc:game:3", data, cards);
            Check($"Baz's game calls: mc:game:2 ({gamePools[2].Count} line{(gamePools[2].Count == 1 ? "" : "s")}) and mc:game:3 ({gamePools[3].Count}) each name only their own game; "
                  + $"plain mc:game holds {neutral.Count} game-neutral line{(neutral.Count == 1 ? "" : "s")}", gameCalls);
            Check($"story: the {alleyScenes.Count} alley scenes' speakers all have profiles{(badSpeakers.Count > 0 ? " (not: " + string.Join(", ", badSpeakers) + ")" : "")}; "
                  + $"alley-voice.txt passes ShopVoice.Problems with the alley's hooks{(voiceProblems.Count > 0 ? ": " + string.Join("; ", voiceProblems.Take(4)) : "")}; "
                  + $"the new cast has profiles{(cast.Count > 0 ? " (missing: " + string.Join(", ", cast) + ")" : "")}",
                File.Exists(voicePath) && badSpeakers.Count == 0 && voiceProblems.Count == 0 && cast.Count == 0 && alleyScenes.Count >= 10);
        }

        Console.WriteLine($"  (alley checks: {timer.Elapsed.TotalSeconds:0.0} s)");
        return failures;
    }

    // ---- 4. The turn cap ---------------------------------------------------------------------------

    /// <summary>
    /// Scripted duels of passing duelists: one side only activates Dian Keto (so its LP climbs), the other passes; both pass
    /// (equal LP). With TurnCap the duel ends on turn 41's start, the higher LP winning or a draw (ReasonTurnCap), with the
    /// narrator's line; without it the duel plays on.
    /// </summary>
    private static void TurnCapChecks(ICardDataProvider cards, string root, Action<string, bool> Check)
    {
        var scripts = new FileScriptProvider(Path.Combine(root, "vendor", "CardScripts"));
        uint fissure = Alley.CodeOf("Fissure", cards), dian = Alley.CodeOf("Dian Keto the Cure Master", cards);
        Deck Of(uint code) { var d = new Deck { Name = "filler" }; for (int i = 0; i < 40; i++) d.Main.Add(code); return d; }
        int lastTurn = Bite == "turncap" ? AlleyEconomy.AlleyTurns - 1 : 0;
        (DuelResult result, TurnCap? cap, List<string> log) Play(bool healer, bool capped, int first)
        {
            var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = 41, FirstPlayer = first }, Deck0 = Of(fissure), Deck1 = Of(healer ? dian : fissure) };
            using var duel = recipe.Create();
            var two = new ScriptedDuelist(2, cards);
            if (healer) two.Activate.Add(dian);
            var runner = new DuelRunner(duel, new PassiveDuelist(cards), two);
            var cap = capped ? TurnCap.Attach(runner, duel, lastTurn) : null;
            var log = new List<string>();
            var narrator = new DuelNarrator(cards, "You", "Dian Keto Dan");
            narrator.Line += log.Add;
            runner.MessageReceived += narrator.Handle;
            for (int i = 0; i < 30000 && runner.Step() && runner.Result.Turns <= AlleyEconomy.AlleyTurns + 5; i++) { }
            return (runner.Result, cap, log);
        }
        var lp = Play(healer: true, capped: true, first: 0);
        var level = Play(healer: false, capped: true, first: 1);
        var open = Play(healer: false, capped: false, first: 0);
        string Ending(List<string> log) => log.LastOrDefault(l => l.Contains("***"))?.Trim() ?? "";
        Check($"turn cap: a duel of passing duelists ends on turn {lp.result.Turns}'s start, the higher LP winning ({lp.cap?.Lp(1):N0} to {lp.cap?.Lp(0):N0}: \"{Ending(lp.log)}\")",
            lp.result.Turns == AlleyEconomy.AlleyTurns + 1 && lp.result.Winner == 1 && lp.result.Reason == DuelRunner.ReasonTurnCap && lp.cap!.Decided
            && lp.cap.Lp(1) > lp.cap.Lp(0) && Ending(lp.log).Contains($"Turn {AlleyEconomy.AlleyTurns}: Dian Keto Dan wins on life points"));
        Check($"turn cap: with equal LP it's a draw on turn {level.result.Turns}'s start (ReasonTurnCap: \"{Ending(level.log)}\"); without TurnCap the duel plays on (turn {open.result.Turns}, no winner yet)",
            level.result.Turns == AlleyEconomy.AlleyTurns + 1 && level.result.Winner == 2 && level.result.Reason == DuelRunner.ReasonTurnCap && level.cap!.Lp(0) == level.cap.Lp(1)
            && Ending(level.log).Contains($"Turn {AlleyEconomy.AlleyTurns}: level on life points. A draw.")
            && open.result.Turns > AlleyEconomy.AlleyTurns + 1 && open.result.Winner < 0);
    }

    // ---- 10. Binders --------------------------------------------------------------------------------

    private static void BinderChecks(Setup s, Deck starter, ICardDataProvider cards, PrintingIndex printings, Library library, List<Personality> roster, Action<string, bool> Check)
    {
        var data = s.Data;
        var ctx = s.Ctx;
        string Name(uint c) => cards.Get(c)?.Name ?? c.ToString();
        int dumpster = data.IndexOf("dumpster-row"), basement = data.IndexOf("basement"), blackMarket = data.IndexOf("black-market");
        var p = Opened(starter, cards, printings, library, 0xA11E0010);
        foreach (var id in new[] { "dumpster-row", "basement" }) p.AlleyTables[id] = new AlleyTableState { Cleared = true };
        var regulars = new[] { dumpster, basement, blackMarket }.SelectMany(t => BackAlley.Lineup(p, data, t, ctx).Where(o => o.Kind == "regular").Select(o => (t, o))).ToList();

        // Each regular's binder: 12 different cards of its own deck, in its tier's range and conditions.
        var bad = new List<string>();
        foreach (var (t, o) in regulars)
        {
            var tier = data.Tables[t].Tier;
            var binder = Ante.Binder(p, o.Binder, ctx);
            var deckCodes = BackAlley.DeckOf(o, ctx) is var d ? d.Main.Concat(d.Extra).Select(c => Banlist.BaseCode(c, cards)).ToHashSet() : new HashSet<uint>();
            int floor = (int)Math.Ceiling(AlleyEconomy.StakeFloor * Ante.StakeFor(tier)), cap = tier == AlleyTier.Street ? AlleyEconomy.StreetCap : AlleyEconomy.DenCap;
            var conditions = (tier == AlleyTier.Street ? AlleyEconomy.StreetGrades : AlleyEconomy.DenGrades).Select(g => (g.grade, g.slab)).ToHashSet();
            if (binder.Cards.Count != AlleyEconomy.BinderSize) bad.Add($"{o.Binder}: {binder.Cards.Count} cards");
            if (binder.Cards.Select(c => c.Code).Distinct().Count() != binder.Cards.Count) bad.Add($"{o.Binder}: a card twice");
            foreach (var st in binder.Cards)
            {
                int v = Ante.Value(st, ctx);
                if (!deckCodes.Contains(st.Code)) bad.Add($"{o.Binder}: {Name(st.Code)} isn't in its deck");
                if (v < floor || v > cap) bad.Add($"{o.Binder}: {Name(st.Code)} worth {v}");
                if (!conditions.Contains((st.Slab > 0 ? st.Slab : st.Grade, st.Slab > 0))) bad.Add($"{o.Binder}: {Name(st.Code)} grade {st.Grade} slab {st.Slab}");
            }
        }
        Check($"binders: {regulars.Count} regulars' binders hold {AlleyEconomy.BinderSize} different cards of their own decks, every stake worth {AlleyEconomy.StakeFloor:0.0}× "
              + $"the stake to the tier's cap (Street {AlleyEconomy.StreetStake}-{AlleyEconomy.StreetCap}, all Raw 4/6/8/9; Den {AlleyEconomy.DenStake}-{AlleyEconomy.DenCap}, Raw 8-9 or slabs 8-10)"
              + (bad.Count > 0 ? ": " + string.Join("; ", bad.Take(4)) : ""),
            regulars.Count >= 8 && bad.Count == 0); // a ready wildcard can take one regular's seat

        // Contraband weight: the first stake of a new binder is one weighted pick. Its expected chance of being contraband,
        // from the same candidates and weights (condition × 10000 / Unit × 3 for contraband), against what the binders drew.
        double exp3 = 0, exp1 = 0, var3 = 0;
        int observed = 0, samples = 0;
        var notCandidate = new List<string>();
        foreach (var (t, o) in regulars)
        {
            var tier = data.Tables[t].Tier;
            var deck = BackAlley.DeckOf(o, ctx);
            var contraband = o.Added.Select(c => Banlist.BaseCode(c, cards)).ToHashSet();
            double floor = AlleyEconomy.StakeFloor * Ante.StakeFor(tier), cap = tier == AlleyTier.Street ? AlleyEconomy.StreetCap : AlleyEconomy.DenCap;
            double wc = 0, wn = 0;
            var candidates = new HashSet<(uint, Rarity, int, int)>();
            foreach (var code in deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, cards)).Distinct())
            {
                var alias = cards.Get(code)?.Alias ?? 0;
                var commonest = printings.Commonest(code, alias);
                foreach (var r in printings.Of(code, alias).Select(x => x.Rarity).Distinct())
                    foreach (var (grade, slab, w) in tier == AlleyTier.Street ? AlleyEconomy.StreetGrades : AlleyEconomy.DenGrades)
                    {
                        int v = Pricing.V(new CardCopy { Code = code, Rarity = r, Grade = grade, Slab = slab ? grade : 0 }, commonest);
                        if (v < floor || v > cap) continue;
                        double weight = w * 10000.0 / Pricing.Unit(r);
                        if (contraband.Contains(code)) wc += weight; else wn += weight;
                        candidates.Add((code, r, grade, slab ? grade : 0));
                    }
            }
            double p3 = AlleyEconomy.ContrabandWeight * wc / (AlleyEconomy.ContrabandWeight * wc + wn), p1 = wc / (wc + wn);
            for (uint salt = 0; salt < 80; salt++)
            {
                var q = new Profile();
                q.Collection.Salt = Hash.Mix(0xB1D3u, salt);
                q.AlleyOpponents.Add(o);
                var first = Ante.Binder(q, o.Binder, ctx).Cards[0];
                if (!candidates.Contains((first.Code, first.Rarity, first.Grade, first.Slab))) notCandidate.Add($"{Name(first.Code)} {first.Rarity} {first.Grade}/{first.Slab}");
                if (contraband.Contains(first.Code)) observed++;
                exp3 += p3; exp1 += p1; var3 += p3 * (1 - p3);
                samples++;
            }
        }
        double sigma = Math.Sqrt(var3);
        Check($"binders: contraband weighted {AlleyEconomy.ContrabandWeight}×: of {samples} first stakes {observed} were contraband, expected {exp3:0} at {AlleyEconomy.ContrabandWeight}× "
              + $"(± {sigma:0}; {exp1:0} if unweighted); every stake is one of its tier's candidates{(notCandidate.Count > 0 ? " (not: " + string.Join(", ", notCandidate.Take(3)) + ")" : "")}",
            Math.Abs(observed - exp3) <= 4 * sigma && Math.Abs(observed - exp1) > 4 * sigma && notCandidate.Count == 0);

        // Headliners lead with their stakes: in order; Orvel's slabs (his binder, though his table opens in Phase B). A stakes:
        // card worth under its tier's floor at every printing can't be staked at all (the binder skips it): those are listed
        // as a data conflict for the owner, and the rest must lead in order.
        var leads = new List<string>();
        var unstakeable = new List<string>();
        bool leadOk = true;
        foreach (var h in data.Headliners.Where(h => h.Ready))
        {
            var tier = data.Tables[data.IndexOf(h.Table)].Tier;
            double floor = AlleyEconomy.StakeFloor * Ante.StakeFor(tier);
            int cap = tier == AlleyTier.Street ? AlleyEconomy.StreetCap : AlleyEconomy.DenCap;
            (Rarity r, int grade, bool slab, int v) Best(uint code)
            {
                var alias = cards.Get(code)?.Alias ?? 0;
                return printings.Of(code, alias).Select(x => x.Rarity).Distinct()
                    .SelectMany(r => (tier == AlleyTier.Street ? AlleyEconomy.StreetGrades : AlleyEconomy.DenGrades).Select(g => (r, g.grade, g.slab,
                        v: Pricing.V(new CardCopy { Code = code, Rarity = r, Grade = g.grade, Slab = g.slab ? g.grade : 0 }, printings.Commonest(code, alias)))))
                    .Where(x => x.v <= cap).OrderByDescending(x => x.v).FirstOrDefault();
            }
            var fit = h.Stakes.Where(c => Best(c).v >= floor).ToList();
            foreach (var code in h.Stakes.Except(fit))
            {
                var b = Best(code);
                unstakeable.Add($"{h.Handle}'s {Name(code)} (printings {string.Join("/", printings.Of(code, cards.Get(code)?.Alias ?? 0).Select(x => x.Rarity).Distinct())}: "
                                + $"at best {b.v:N0} as a {b.r} {(b.slab ? "slab" : "Raw")} {b.grade}, under the {tier} floor of {floor:N0})");
            }
            var binder = Ante.Binder(p, "hl:" + h.Id, ctx);
            leadOk &= binder.Cards.Take(fit.Count).Select(c => c.Code).SequenceEqual(fit) && binder.Cards.Count == AlleyEconomy.BinderSize
                      && binder.Cards.Select(c => c.Code).Distinct().Count() == binder.Cards.Count;
            leads.Add($"{h.Handle}: {string.Join(", ", binder.Cards.Take(fit.Count).Select(c => $"{Name(c.Code)} {Ante.Value(c, ctx):N0}"))}");
        }
        var orvel = Ante.Binder(p, "orvel", ctx);
        // Every slab of his can be staked: worth at least what the cheapest card you may put up (OrvelMinCard) matches.
        bool orvelSlabs = orvel.Cards.Count == AlleyEconomy.BinderSize && orvel.Cards.All(c => c.Slab is 9 or 10 && c.Rarity >= AlleyEconomy.OrvelMinRarity
                                                                                              && Ante.Value(c, ctx) >= AlleyEconomy.OrvelMatchMin * AlleyEconomy.OrvelMinCard);
        // Your card: a copy of his median slab, so the offer is the first slab in his binder's order worth 1-2× it.
        var median = orvel.Cards.OrderBy(c => Ante.Value(c, ctx)).ElementAt(orvel.Cards.Count / 2);
        var mine = Ante.ToCopy(median, p);
        int myValue = p.Collection.Value(mine);
        bool Fits(Stake c) => Ante.Value(c, ctx) >= AlleyEconomy.OrvelMatchMin * myValue && Ante.Value(c, ctx) <= AlleyEconomy.OrvelMatchMax * myValue;
        var match = Ante.Next(p, "orvel", ctx, mine);
        bool orvelMatch = match != null && Fits(match) && orvel.Cards.First(Fits) == match;
        Check($"binders: ready headliners lead with their stakes: in order ({string.Join("; ", leads)}); Orvel's are slabs 9-10 at Super Rare and up worth "
              + $"{AlleyEconomy.OrvelMatchMin * AlleyEconomy.OrvelMinCard:N0}+ ({string.Join(", ", orvel.Cards.Select(c => Ante.Value(c, ctx)).OrderBy(x => x).Select(x => x.ToString("N0")))}), and his offer for a {myValue:N0} DP card is worth 1-2× it ({(match != null ? $"{Name(match.Code)} {Ante.Value(match, ctx):N0}" : "none fits")})",
            leadOk && orvelSlabs && orvelMatch);
        if (unstakeable.Count > 0)
            Console.WriteLine($"         NOTE (data conflict, for the owner): a stakes: card that can never be staked, so it can't lead its binder: {string.Join("; ", unstakeable)}");

        // Refills: one per 3 alley duels since, up to 12; new cards only. The same save, the same stakes; a save keeps them.
        var (_, first0) = regulars[0];
        var refill = Ante.Binder(p, first0.Binder, ctx);
        refill.Cards.RemoveRange(0, 5);
        int start = p.AlleyDuels;
        p.AlleyDuels = start + 7;
        int after7 = Ante.Binder(p, first0.Binder, ctx).Cards.Count;
        p.AlleyDuels = start + 9;
        int after9 = Ante.Binder(p, first0.Binder, ctx).Cards.Count;
        p.AlleyDuels = start + 60;
        var full = Ante.Binder(p, first0.Binder, ctx);
        bool refills = after7 == 9 && after9 == 10 && full.Cards.Count == AlleyEconomy.BinderSize && full.Cards.Select(c => c.Code).Distinct().Count() == full.Cards.Count;
        var twin = Opened(starter, cards, printings, library, 0xA11E0010);
        foreach (var id in new[] { "dumpster-row", "basement" }) twin.AlleyTables[id] = new AlleyTableState { Cleared = true };
        var twinRegulars = new[] { dumpster, basement, blackMarket }.SelectMany(t => BackAlley.Lineup(twin, data, t, ctx).Where(o => o.Kind == "regular")).ToList();
        bool sameStakes = twinRegulars.Skip(1).All(o => Ante.Binder(twin, o.Binder, ctx).Cards.Zip(p.Binders[o.Binder].Cards).All(x => Same(x.First, x.Second)));
        var reloaded = RoundTrip(p, printings, cards);
        bool saved = p.Binders.All(b => reloaded.Binders.TryGetValue(b.Key, out var rb) && rb.Serial == b.Value.Serial && rb.RefilledAt == b.Value.RefilledAt
                                        && rb.Cards.Count == b.Value.Cards.Count && rb.Cards.Zip(b.Value.Cards).All(x => Same(x.First, x.Second)));
        Check($"binders: refills one card per {AlleyEconomy.RefillEvery} alley duels (7 duels: 7 -> {after7}, 9: {after9}, 60: {full.Cards.Count}), never past {AlleyEconomy.BinderSize}, new cards only; "
              + "the same save draws the same stakes; a save keeps every binder exactly",
            refills && sameStakes && saved);
    }

    // ---- Phase B: Orvel's Table, the rail, Moth's van, Contraband Packs, Unlisted, posters, the held card, Penny ----

    /// <summary>Contraband Packs are rolled this many times for their odds check (ALLEY-SPEC B3).</summary>
    public const int OddsPacks = 20000;

    /// <summary>
    /// How far (in percentage points) the Contraband Pack's measured odds may sit from <see cref="ContrabandPacks.Odds"/> over
    /// <see cref="OddsPacks"/> packs: slots 1-4 (80,000 cards, a standard error of 0.1-0.2 points) at the spec's 0.5; slot 5
    /// (20,000 slabs, a standard error of about 0.32 points on its commonest rarity) at 1 point, as B1 recommended.
    /// </summary>
    public const double FieldTolerance = 0.5, SlabTolerance = 1.0;

    /// <summary>
    /// ALLEY-SPEC section 16, chunk B3's checks, all through the engine on scripted saves: the Contraband Pack (contents,
    /// odds, Give), Moth's shelf and used tab (stability, prices, buying), his Sell tab (the Sell desk exactly), Orvel's
    /// Table and the rail end to end (your card's rules, the offer, a loss, the clock, a buy-back, a win and the clear), the
    /// Unlisted binder, the alley's posters, the held card, Penny's cameo, the van's voice and the alley's music.
    /// </summary>
    private static void PhaseBChecks(Setup s, Deck starter, ICardDataProvider cards, PrintingIndex printings, Library library, List<Personality> roster, Circuits circuits,
        string root, Action<string, bool> Check, HashSet<string> events)
    {
        var data = s.Data;
        var ctx = s.Ctx;
        int none(uint _) => 0;
        string Name(uint c) => cards.Get(c)?.Name ?? c.ToString();
        uint Alias(uint c) => cards.Get(c)?.Alias ?? 0;
        int V(CardCopy c) => Pricing.V(c, ctx.Commonest(c.Code));
        int dumpster = data.IndexOf("dumpster-row"), orvel = data.IndexOf("orvels-table"), nyx = data.IndexOf("nyx");
        var packs = s.Packs;
        var pool = packs.Pool.ToHashSet();
        var story = Story.Load(Path.Combine(root, "campaign", "story.txt"));
        AlleyResult? Note(AlleyResult? r) { if (r != null) events.UnionWith(r.Events); return r; }

        // ---- Contraband Packs ----
        var everForbidden = ctx.Pool(9999).ToHashSet();
        var excluded = Alley.Excluded.Select(n => Alley.CodeOf(n, cards)).ToHashSet();
        bool poolOk = packs.Pool.Count > 0 && packs.Pool.All(everForbidden.Contains) && !packs.Pool.Any(excluded.Contains) && packs.Pool.Distinct().Count() == packs.Pool.Count
                      && packs.Pool.All(c => printings.Of(c, Alias(c)).Length > 0) && everForbidden.Where(c => printings.Of(c, Alias(c)).Length > 0).All(pool.Contains);
        var field = new Dictionary<Rarity, int>();
        var slabMix = new Dictionary<Rarity, int>();
        int raws = 0, slabs = 0, tens = 0, badSize = 0, dupes = 0, notPool = 0, notPrinted = 0, badSlab = 0, badOrder = 0;
        for (int k = 0; k < OddsPacks; k++)
        {
            var pulls = packs.Roll(Hash.Mix(0xC0B7u, (uint)k));
            if (pulls.Count != ContrabandPacks.Size) badSize++;
            if (pulls.Select(x => x.Code).Distinct().Count() != pulls.Count) dupes++;
            if (!pulls.Select(x => x.Slot).SequenceEqual(Enumerable.Range(1, pulls.Count))) badOrder++;
            foreach (var x in pulls)
            {
                if (!pool.Contains(x.Code)) notPool++;
                if (!printings.Of(x.Code, Alias(x.Code)).Any(pr => pr.Rarity == x.Rarity && pr.SetCode == x.SetCode)) notPrinted++;
                if (x.Slot == ContrabandPacks.Size)
                {
                    if (x.Slab is not (9 or 10) || x.Rarity < AlleyEconomy.PackSlabMinRarity) badSlab++;
                    slabs++;
                    if (x.Slab == 10) tens++;
                    slabMix[x.Rarity] = slabMix.GetValueOrDefault(x.Rarity) + 1;
                }
                else
                {
                    if (x.Slab != 0) badSlab++;
                    raws++;
                    field[x.Rarity] = field.GetValueOrDefault(x.Rarity) + 1;
                }
            }
        }
        Check($"Contraband Packs: the pool is the ever-Forbidden cards with a printing, none excluded ({packs.Pool.Count} cards); {OddsPacks:N0} seeded packs each hold "
              + $"{ContrabandPacks.Size} different pool cards at real printings in slot order ({badSize} wrong size, {dupes} with a card twice, {notPool} off the pool, {notPrinted} unprinted, "
              + $"{badOrder} out of order), slots 1-4 Raw and slot 5 a sealed 9-10 slab at Super Rare and up ({badSlab} not; {100.0 * (slabs - tens) / Math.Max(1, slabs):0.0}% 9s, "
              + $"target {AlleyEconomy.PackSlabNine:P0})",
            poolOk && packs.Pool.Count == ContrabandPool && badSize + dupes + notPool + notPrinted + badSlab + badOrder == 0 && slabs == OddsPacks
            && Math.Abs((slabs - tens) / (double)slabs - AlleyEconomy.PackSlabNine) * 100 <= SlabTolerance);
        var odds = packs.Odds();
        double Off(List<(Rarity rarity, double share)> expected, Dictionary<Rarity, int> seen, int n) =>
            expected.Select(e => e.rarity).Union(seen.Keys).Max(r => Math.Abs(expected.FirstOrDefault(e => e.rarity == r).share - seen.GetValueOrDefault(r) / (double)n)) * 100;
        double fieldOff = Off(odds.Field, field, raws), slabOff = Off(odds.Slab, slabMix, slabs);
        double tenOff = Math.Abs(odds.SlabTenPercent - 100.0 * tens / slabs);
        Check($"Contraband Packs: Odds() sums to 1 (slots 1-4 {odds.Field.Sum(x => x.share):0.0000} over {odds.FieldCards} cards, slot 5 {odds.Slab.Sum(x => x.share):0.0000}, "
              + $"{odds.SlabTenPercent}% 10s) and matches {OddsPacks:N0} seeded packs: slots 1-4 within {fieldOff:0.00} points (tolerance {FieldTolerance}), slot 5's rarity within "
              + $"{slabOff:0.00} and its 10s within {tenOff:0.00} (tolerance {SlabTolerance}: slot 5's standard error is about 0.32 points); no tournament slot",
            Math.Abs(odds.Field.Sum(x => x.share) - 1) < 1e-9 && Math.Abs(odds.Slab.Sum(x => x.share) - 1) < 1e-9 && odds.FieldCards == packs.Pool.Count
            && odds.SlabTenPercent == (int)Math.Round((1 - AlleyEconomy.PackSlabNine) * 100) && fieldOff <= FieldTolerance && slabOff <= SlabTolerance && tenOff <= SlabTolerance
            && odds.TournamentSet == null && odds.Tournament.Count == 0 && odds.Slab.All(x => x.rarity >= AlleyEconomy.PackSlabMinRarity));
        {
            // Give: a 4th copy (three worn copies of the first pack's first card owned already) keeps your best three for 0 DP.
            var g = Opened(starter, cards, printings, library, 0xA11E0B10);
            var firstPack = packs.Roll(77);
            var fourth = firstPack[0].Code;
            for (int i = 0; i < Collection.MaxCopies; i++)
            {
                var c = g.Collection.NewCopy(fourth, firstPack[0].Rarity, CopySource.Shop);
                c.Grade = 1;
                c.Paid = 100;
                g.Collection.Insert(c);
            }
            int dp0 = g.Dp, earned0 = g.DpEarned.Values.Sum();
            var opened = packs.Give(g, 77);
            var given = opened.ToList();
            for (uint k = 1; k < 40; k++) given.AddRange(packs.Give(g, Hash.Mix(0xC0B8u, k)));
            var copies = given.Where(x => x.Copy != null).Select(x => x.Copy!).ToList();
            bool same = packs.Roll(77).Select(x => (x.Code, x.Rarity, x.Slab, x.SetCode)).SequenceEqual(firstPack.Select(x => (x.Code, x.Rarity, x.Slab, x.SetCode)));
            Check($"Contraband Packs: Give puts every pull in as a Prize copy, Paid 0 (the Sell desk pays 0: \"{(copies.Count > 0 ? SellDesk.Blocked(g, copies[0], none) : "")}\"), "
                  + $"the slab sealed at its grade, revealed last; a 4th copy ({Name(fourth)}) keeps your best three and pays no DP; the same seed rolls the same pack",
                same && copies.Count > 0 && copies.All(c => c.Paid == 0 && c.Source == CopySource.Prize && SellDesk.Sell(g, c, none) == 0)
                && given.Where(x => x.Slab > 0 && x.Copy != null).All(x => x.Copy!.Slab == x.Slab && x.Copy.Grade == x.Slab && x.Copy.Floor == x.Slab)
                && given.Where(x => x.Slab == 0 && x.Copy != null).All(x => !x.Copy!.IsSlab) && opened.Count == ContrabandPacks.Size && opened[^1].Slab > 0
                && g.Collection.Owned(fourth) == Collection.MaxCopies && g.Dp == dp0 && g.DpEarned.Values.Sum() == earned0);
        }

        // ---- Moth's van: the shelf and the used tab ----
        var v = Opened(starter, cards, printings, library, 0xA11E0B20);
        v.Earn(Ledger.Debug, 500000);
        string Key(Listing l) => $"{l.Slot} {l.Copy.Code} {l.Copy.Rarity} {l.Copy.Grade} {l.Copy.Slab} {l.Copy.Seed} {l.Price}";
        {
            var items = MothsVan.ShelfItems(packs, ctx);
            var shelf = MothsVan.Shelf(v, packs, ctx);
            var again = MothsVan.Shelf(v, packs, ctx).Select(Key).ToList();
            var twin = MothsVan.Shelf(Opened(starter, cards, printings, library, 0xA11E0B20), packs, ctx).Select(Key).ToList();
            var loaded = MothsVan.Shelf(RoundTrip(v, printings, cards), packs, ctx).Select(Key).ToList();
            int toNext = MothsVan.DuelsToRefresh(v, shelf: true);
            v.AlleyDuels += toNext - 1;
            var late = MothsVan.Shelf(v, packs, ctx).Select(Key).ToList();
            v.AlleyDuels++;
            var next = MothsVan.Shelf(v, packs, ctx);
            v.AlleyDuels -= toNext;
            MothsVan.Shelf(v, packs, ctx);
            var bad = shelf.Concat(next).Where(l => l.Copy.Slab is not (9 or 10) || l.Copy.Grade != l.Copy.Slab || l.Copy.Rarity < AlleyEconomy.ShelfMinRarity || !pool.Contains(l.Copy.Code)
                                                    || V(l.Copy) < AlleyEconomy.ShelfMinValue || l.Price != AlleyEconomy.ShelfPrice(V(l.Copy)) || l.Price < 5500 || l.Price > 22500).ToList();
            var prices = items.Select(i => AlleyEconomy.ShelfPrice(i.value)).ToList();
            Check($"the shelf: {shelf.Count} slabs of different cards, grades 9-10 at Secret Rare and up, worth {AlleyEconomy.ShelfMinValue:N0}+ and priced ShelfPrice(V) "
                  + $"({string.Join(", ", shelf.Select(l => $"{Name(l.Copy.Code)} {l.Copy.Slab} {l.Price:N0}"))}){(bad.Count > 0 ? $"; not: {Name(bad[0].Copy.Code)}" : "")}; "
                  + $"{items.Count} possible items from {items.Select(i => i.code).Distinct().Count()} cards at {prices.Min():N0}-{prices.Max():N0}; the same within its {AlleyEconomy.ShelfEvery} "
                  + "alley duels (a twin save and a reload too), new after",
                shelf.Count == AlleyEconomy.ShelfSize && next.Count == AlleyEconomy.ShelfSize && shelf.Select(l => l.Copy.Code).Distinct().Count() == shelf.Count && bad.Count == 0
                && items.Count == ShelfItems && items.Select(i => i.code).Distinct().Count() == ShelfCards && prices.Min() == 5500 && prices.Max() == 22500
                && shelf.Select(Key).SequenceEqual(again) && shelf.Select(Key).SequenceEqual(twin) && shelf.Select(Key).SequenceEqual(loaded) && shelf.Select(Key).SequenceEqual(late)
                && !shelf.Select(Key).SequenceEqual(next.Select(Key)));

            var used = MothsVan.Used(v, packs, ctx);
            var usedAgain = MothsVan.Used(v, packs, ctx).Select(Key).ToList();
            int usedNext = MothsVan.DuelsToRefresh(v, shelf: false);
            for (int k = 0; k < usedNext - 1; k++) v.NoteResult("check", 0);
            var usedLate = MothsVan.Used(v, packs, ctx).Select(Key).ToList();
            v.NoteResult("check", 0);
            var usedNew = MothsVan.Used(v, packs, ctx);
            var usedBad = used.Concat(usedNew).Where(l => !pool.Contains(l.Copy.Code) || l.Price != V(l.Copy) || l.Copy.Slab > AlleyEconomy.VanUsedSlabMax || l.Copy.Source != CopySource.Used
                                                         || !printings.Of(l.Copy.Code, Alias(l.Copy.Code)).Any(pr => pr.Rarity == l.Copy.Rarity)).ToList();
            int raw = used.Count(l => !l.Copy.IsSlab);
            Check($"Moth's used tab: {used.Count} listings of the pack's pool ({raw} Raw, {used.Count - raw} slabs graded {string.Join("/", used.Where(l => l.Copy.IsSlab).Select(l => l.Copy.Slab))}), "
                  + $"each priced at exactly its value, no slab over {AlleyEconomy.VanUsedSlabMax}{(usedBad.Count > 0 ? $"; not: {Name(usedBad[0].Copy.Code)} at {usedBad[0].Price}" : "")}; "
                  + $"the same for its {AlleyEconomy.VanUsedEvery} duels, new after",
                used.Count == AlleyEconomy.VanUsedSize && raw == 13 && usedBad.Count == 0 && used.Select(Key).SequenceEqual(usedAgain) && used.Select(Key).SequenceEqual(usedLate)
                && !used.Select(Key).SequenceEqual(usedNew.Select(Key)));

            // Buying: the shelf's first slab and the used tab's dearest copy.
            var buy = MothsVan.Shelf(v, packs, ctx)[0];
            int dp0 = v.Dp, shelfSpent = v.DpSpent.GetValueOrDefault(Ledger.AlleyShelf);
            int refund = MothsVan.Buy(v, buy, shelf: true);
            var got = v.Collection.CopiesOf(buy.Copy.Code).FirstOrDefault(c => c.Seed == buy.Copy.Seed);
            bool shelfBuy = refund == 0 && got != null && v.Dp == dp0 - buy.Price && v.DpSpent.GetValueOrDefault(Ledger.AlleyShelf) == shelfSpent + buy.Price && got.Paid == buy.Price
                            && got.Slab == buy.Copy.Slab && got.Rarity == buy.Copy.Rarity && got.Source == CopySource.Used && MothsVan.Shelf(v, packs, ctx)[0].Sold
                            && MothsVan.Blocked(v, MothsVan.Shelf(v, packs, ctx)[0]) == "Sold." && MothsVan.Buy(v, MothsVan.Shelf(v, packs, ctx)[0], true) == -1;
            var stale = MothsVan.Shelf(v, packs, ctx)[1];
            v.AlleyDuels += AlleyEconomy.ShelfEvery;
            bool staleRefused = MothsVan.Buy(v, stale, true) == -1 && MothsVan.Shelf(v, packs, ctx).All(l => !l.Sold);
            var dear = MothsVan.Used(v, packs, ctx).OrderByDescending(l => l.Price).First();
            int usedSpent = v.DpSpent.GetValueOrDefault(Ledger.AlleyUsed);
            int usedRefund = MothsVan.Buy(v, dear, shelf: false);
            var gotUsed = v.Collection.CopiesOf(dear.Copy.Code).FirstOrDefault(c => c.Seed == dear.Copy.Seed && c.Paid == dear.Price);
            // A 4th copy from the van: three worn copies of a Raw listing's card owned already; the one it displaces pays refund.alley.
            var rawListing = MothsVan.Used(v, packs, ctx).Where(l => !l.Sold && !l.Copy.IsSlab && l.Copy.Grade >= 5 && v.Collection.Owned(l.Copy.Code) == 0).OrderByDescending(l => l.Price).First();
            for (int i = 0; i < Collection.MaxCopies; i++)
            {
                var c = v.Collection.NewCopy(rawListing.Copy.Code, rawListing.Copy.Rarity, CopySource.Shop);
                c.Grade = 1;
                c.Paid = 400;
                v.Spend(Ledger.Order, 400);
                v.Collection.Insert(c);
            }
            int refundBefore = v.DpEarned.GetValueOrDefault(Ledger.AlleyRefund);
            int fourthRefund = MothsVan.Buy(v, rawListing, shelf: false);
            var poor = Opened(starter, cards, printings, library, 0xA11E0B21);
            poor.Spend(Ledger.Debug, poor.Dp - 100);
            var poorShelf = MothsVan.Shelf(poor, packs, ctx)[0];
            Check($"buying at the van: a shelf slab ({Name(buy.Copy.Code)}, {buy.Price:N0} DP) and a used copy ({Name(dear.Copy.Code)}, {dear.Price:N0}) spend {Ledger.AlleyShelf} and "
                  + $"{Ledger.AlleyUsed}, come in Paid their price, their slots sold (a second buy refused); a listing shown before a restock is refused; a 4th copy displaces your worst "
                  + $"and pays its Sell price to {Ledger.AlleyRefund} ({fourthRefund} DP); short of DP: \"{MothsVan.Blocked(poor, poorShelf)}\"",
                shelfBuy && staleRefused && usedRefund == 0 && gotUsed != null && v.DpSpent.GetValueOrDefault(Ledger.AlleyUsed) == usedSpent + dear.Price + rawListing.Price
                && fourthRefund > 0 && v.DpEarned.GetValueOrDefault(Ledger.AlleyRefund) == refundBefore + fourthRefund && v.Collection.Owned(rawListing.Copy.Code) == Collection.MaxCopies
                && MothsVan.Blocked(poor, poorShelf)!.StartsWith("It costs") && MothsVan.Buy(poor, poorShelf, true) == -1 && Ledger.Net(v) == v.Dp - Profile.StartingDp);

            // Moth's Sell tab is the Sell desk: the same price for the same copy, ledger "sell", the same refusals.
            var text = SaveText(v);
            var atMoth = FromText(text, printings, cards);
            var atDesk = FromText(text, printings, cards);
            var sold = new List<(string name, int moth, int desk, int paid, int value)>();
            foreach (var id in new[] { got?.Id ?? -1, gotUsed?.Id ?? -1 })
            {
                var a = atMoth.Collection.All.FirstOrDefault(c => c.Id == id);
                var b = atDesk.Collection.All.FirstOrDefault(c => c.Id == id);
                if (a != null && b != null) sold.Add((Name(a.Code), MothsVan.Sell(atMoth, a, none), SellDesk.Sell(atDesk, b, none), a.Paid, V(a)));
            }
            var won = Ante.ToCopy(new Stake { Code = buy.Copy.Code, Rarity = buy.Copy.Rarity, Grade = 9, Slab = 9 }, atMoth);
            atMoth.Collection.Insert(won);
            Check($"Moth's Sell tab pays exactly the Sell desk's price ({string.Join(", ", sold.Select(x => $"{x.name} {x.moth:N0} for {x.paid:N0} paid, {100.0 * x.moth / Math.Max(1, x.paid):0.0}%"))}: "
                  + $"a quarter of its value at most), to \"{Ledger.Sell}\"; a card won at ante: \"{SellDesk.Blocked(atMoth, won, none)}\", 0 DP",
                sold.Count == 2 && sold.All(x => x.moth == x.desk && x.moth > 0 && x.moth <= x.value / 4) && atMoth.DpEarned.GetValueOrDefault(Ledger.Sell) == atDesk.DpEarned.GetValueOrDefault(Ledger.Sell)
                && atMoth.Dp == atDesk.Dp && SellDesk.Blocked(atMoth, won, none) == SellDesk.AnteRefusal && MothsVan.Sell(atMoth, won, none) == 0);
        }

        // ---- Orvel's Table and Moth's rail ----
        var o = OrvelOpen(starter, cards, printings, library, 0xA11E0B30);
        events.Add(BackAlley.FenceEvent); // the van's first open (the app raises it)
        var seat = BackAlley.Lineup(o, data, orvel, ctx);
        var house = seat.Count == 1 ? BackAlley.DeckOf(seat[0], ctx) : new Deck();
        bool orvelSeatOk = BackAlley.IsUnlocked(o, data, orvel) && seat.Count == 1 && seat[0].Kind == "authored" && seat[0].Character == "power-patron" && seat[0].Base == "orvels-house"
                      && seat[0].Binder == "orvel" && seat[0].Skill == 1.0 && seat[0].Handle == "Patron Orvel" && house.Main.Count >= 40
                      && DeckRules.Validate(house, s.List, cards).Count == 0 && house.Note("recipe") == "power-patron";
        var mine = SpareFor(o, s, 0.5);
        int myValue = o.Collection.Value(mine);
        var offer = Ante.Next(o, "orvel", ctx, mine);
        Func<uint, int> needs = c => c == mine.Code ? o.Collection.Count(c) : 0;
        string? neededSays = Ante.Blocked(o, AlleyTier.Orvel, offer, mine, needs, ctx);
        bool neededOk = neededSays == "A saved deck needs it. Spares only." && !Ante.Stakeable(o, needs, AlleyEconomy.OrvelMinCard).Contains(mine)
                        && Ante.Hold(o, data, orvel, seat[0], mine, needs, ctx) == null && o.AnteLive == null;
        mine.ReadyAt = o.DuelsPlayed + 3;
        string? deskSays = Ante.Blocked(o, AlleyTier.Orvel, offer, mine, none, ctx);
        bool deskOk = deskSays == "It's at the Grading Desk." && !Ante.Stakeable(o, none, AlleyEconomy.OrvelMinCard).Contains(mine);
        mine.ReadyAt = 0;
        var cheap = o.Collection.All.Where(c => o.Collection.Value(c) < AlleyEconomy.OrvelMinCard).First();
        string? cheapSays = Ante.Blocked(o, AlleyTier.Orvel, Ante.Next(o, "orvel", ctx, cheap), cheap, none, ctx);
        string? nothingSays = Ante.Blocked(o, AlleyTier.Orvel, null, null, _ => 99, ctx);
        Check($"Orvel's Table: one authored seat (Patron Orvel, orvels-house, legal in the Alley, skill 1.0, his binder); your card must be a spare (\"{neededSays}\"), "
              + $"not at the Grading Desk (\"{deskSays}\"), worth {AlleyEconomy.OrvelMinCard:N0}+ (\"{cheapSays}\"); nothing to stake: \"{nothingSays}\"; nothing matching: "
              + $"\"{Ante.Blocked(o, AlleyTier.Orvel, null, mine, none, ctx)}\"",
            orvelSeatOk && neededOk && deskOk && cheapSays == $"Orvel takes cards worth {AlleyEconomy.OrvelMinCard:N0} DP or more." && nothingSays!.StartsWith("Orvel looks through your binder.")
            && Ante.Blocked(o, AlleyTier.Orvel, null, mine, none, ctx) == "\"Nothing in my binder matches that.\"");

        // A loss: 3,000 DP spent, your card on the rail at 1.5× its value, his slab back on top.
        int dpHold = o.Dp, stakeSpent = o.DpSpent.GetValueOrDefault(Ledger.AlleyStake);
        var live = Ante.Hold(o, data, orvel, seat[0], mine, none, ctx);
        int offerValue = offer != null ? Ante.Value(offer, ctx) : 0;
        bool holdOk = live != null && live.Held == AlleyEconomy.OrvelStake && o.Dp == dpHold - AlleyEconomy.OrvelStake && live.Yours == mine && !o.Collection.All.Contains(mine)
                      && Same(live.Theirs, offer) && offer!.Slab is 9 or 10 && offer.Rarity >= AlleyEconomy.OrvelMinRarity
                      && offerValue >= AlleyEconomy.OrvelMatchMin * myValue && offerValue <= AlleyEconomy.OrvelMatchMax * myValue;
        var heldTrip = RoundTrip(o, printings, cards);
        holdOk &= heldTrip.AnteLive?.Yours?.Id == mine.Id && heldTrip.AnteLive.Yours.Seed == mine.Seed && Same(heldTrip.AnteLive.Theirs, offer);
        var loss = Note(BackAlley.Record(o, data, orvel, 1, 12, 0, ctx))!;
        var entry = loss.Ante?.Railed;
        bool railOk = entry != null && entry.Copy == mine && entry.Price == AlleyEconomy.RailPrice(myValue) && entry.Price == Pricing.Round5(AlleyEconomy.RailMarkup * myValue)
                      && o.Rail.Contains(entry) && Ante.DuelsLeft(o, entry) == AlleyEconomy.RailDuels && loss.Events.Contains("rail") && loss.Events.Contains("antelose")
                      && o.DpSpent.GetValueOrDefault(Ledger.AlleyStake) == stakeSpent + AlleyEconomy.OrvelStake && o.Dp == dpHold - AlleyEconomy.OrvelStake
                      && Same(Ante.Binder(o, "orvel", ctx).Cards[0], offer) && !o.Collection.All.Contains(mine) && loss.Fill["card"] == Name(mine.Code)
                      && Ledger.Net(o) == o.Dp - Profile.StartingDp;
        var railTrip = RoundTrip(o, printings, cards).Rail;
        railOk &= railTrip.Count == 1 && railTrip[0].Copy.Id == mine.Id && railTrip[0].Price == entry!.Price && railTrip[0].ExpiresAt == entry.ExpiresAt;
        var lostPoster = Posters.Find("lost-card")!.Earned(circuits, o);
        // The clock: any duel counts; it hangs for RailDuels duels and goes on the last.
        for (int k = 0; k < AlleyEconomy.RailDuels - 1; k++) Note(Duel(o, s, dumpster, k % 2));
        bool hanging = o.Rail.Contains(entry!) && Ante.DuelsLeft(o, entry!) == 1 && MothsVan.RailFill(o, entry!, cards)["left"] == "1"
                       && MothsVan.RailFill(o, entry!, cards)["price"] == $"{entry!.Price:N0} DP" && MothsVan.RailFill(o, entry!, cards)["card"] == Name(mine.Code);
        var last = Note(Duel(o, s, dumpster, 1))!;
        bool gone = !o.Rail.Contains(entry!) && last.Events.Contains("railgone") && last.Summary.Contains($"Moth sold your {Name(mine.Code)}") && Ante.BuyBackBlocked(o, entry!) == "Moth sold it."
                    && !Ante.BuyBack(o, entry!) && !o.Collection.All.Contains(mine);
        Check($"Orvel's Table, a loss: Hold holds {AlleyEconomy.OrvelStake:N0} DP and your {Name(mine.Code)} ({myValue:N0}) against his {(offer != null ? Name(offer.Code) : "-")} "
              + $"(Grade {offer?.Slab} slab, {offerValue:N0}: 1-2× yours), a save keeps both; the loss spends the stake and hangs your card on the rail at {entry?.Price:N0} "
              + $"(1.5× its value) for {AlleyEconomy.RailDuels} duels (a save keeps it), his slab back on top; LOST CARD's poster is earned",
            holdOk && railOk && lostPoster);
        Check($"the rail's clock: any duel counts; {AlleyEconomy.RailDuels - 1} duels on it still hangs (1 left), the {AlleyEconomy.RailDuels}th sells it "
              + $"(\"{last.Summary.Split(" · ").LastOrDefault()}\", railgone); claiming it then: \"{Ante.BuyBackBlocked(o, entry!)}\"",
            hanging && gone);

        // A buy-back: the same copy (id, grade, seed, slab) for its tag, added to its Paid; it sells for at most a quarter of its value.
        var mine2 = SpareFor(o, s, 0.25);
        var (id2, grade2, seed2, slab2, rarity2) = (mine2.Id, mine2.Grade, mine2.Seed, mine2.Slab, mine2.Rarity);
        var loss2 = Note(OrvelDuel(o, s, mine2, 1));
        var entry2 = loss2?.Ante?.Railed;
        var railBroke = RoundTrip(o, printings, cards);
        railBroke.Spend(Ledger.Debug, railBroke.Dp - 10);
        string? brokeSays = railBroke.Rail.Count > 0 ? Ante.BuyBackBlocked(railBroke, railBroke.Rail[0]) : null;
        int dpBack = o.Dp, railSpent = o.DpSpent.GetValueOrDefault(Ledger.AlleyRail);
        bool bought = entry2 != null && Ante.BuyBack(o, entry2);
        if (bought) events.Add("buyback"); // the app raises it after BuyBack returns true
        var back = o.Collection.All.FirstOrDefault(c => c.Id == id2);
        bool buyOk = bought && back == mine2 && back.Grade == grade2 && back.Seed == seed2 && back.Slab == slab2 && back.Rarity == rarity2 && back.Paid == entry2!.Price
                     && o.Dp == dpBack - entry2.Price && o.DpSpent.GetValueOrDefault(Ledger.AlleyRail) == railSpent + entry2.Price && !o.Rail.Contains(entry2) && !Ante.BuyBack(o, entry2)
                     && o.Collection.SellPrice(back) <= o.Collection.Value(back) / 4 && brokeSays != null && brokeSays.StartsWith("The tag's") && Ledger.Net(o) == o.Dp - Profile.StartingDp;
        Check($"a buy-back: {Name(mine2.Code)} comes back for its tag ({entry2?.Price:N0} DP, {Ledger.AlleyRail}), the same copy (id, grade, seed, slab), its Paid {back?.Paid:N0}; "
              + $"it sells for {(back != null ? o.Collection.SellPrice(back) : 0):N0} (a quarter of its value at most); short of DP: \"{brokeSays}\"",
            buyOk);

        // A win: the stake back, your card back, his slab yours (Paid 0); one seat, so the first win clears the table.
        var mine3 = SpareFor(o, s, 0.75);
        var offer3 = Ante.Next(o, "orvel", ctx, mine3);
        int dpWin = o.Dp, pagesBefore = o.UnlistedPages.Count;
        var win = Note(OrvelDuel(o, s, mine3, 0));
        var wonSlab = win?.Ante?.Won;
        bool orvelWinOk = win?.Ante?.Outcome == "win" && o.Dp == dpWin && o.Collection.All.Contains(mine3) && wonSlab != null && offer3 != null && wonSlab.Code == offer3.Code && wonSlab.Rarity == offer3.Rarity && wonSlab.Slab == offer3.Slab && wonSlab.Seed == offer3.Seed
                     && wonSlab.Source == CopySource.Ante && wonSlab.Paid == 0 && wonSlab.IsSlab && o.Collection.All.Contains(wonSlab) && win.FirstClear && win.NightOver
                     && win.Events.Contains("alleyclear:Orvel's Table") && BackAlley.IsCleared(o, data, orvel) && win.Page?.Key == "authored:orvels-house" && win.Page.Kind == "authored"
                     && win.Events.Contains(Unlisted.Event) && o.UnlistedPages.Count == pagesBefore + 1 && Posters.Find("orvels-marker")!.Earned(circuits, o);
        // The next sitting: a new stake, no second clear, no second page; a draw and the first-turn guard give everything back.
        int dpAgain = o.Dp;
        var nextSitting = Note(OrvelDuel(o, s, null, 0));
        var spare = Ante.Stakeable(o, none, AlleyEconomy.OrvelMinCard).First(c => Ante.Next(o, "orvel", ctx, c) != null);
        var top = Ante.Next(o, "orvel", ctx, spare);
        var draw = Note(OrvelDuel(o, s, spare, 2, AlleyEconomy.AlleyTurns + 1));
        var guard = Note(OrvelDuel(o, s, spare, 1, 1, 1));
        bool laterOk = nextSitting?.Ante?.Outcome == "win" && !nextSitting.FirstClear && nextSitting.Page == null && !nextSitting.Events.Contains(Unlisted.Event) && o.UnlistedPages.Count == pagesBefore + 1
                       && draw?.Ante?.Outcome == "draw" && guard?.Ante?.Outcome == "firstturn" && o.Collection.All.Contains(spare) && Same(Ante.Binder(o, "orvel", ctx).Cards[0], top)
                       && o.Dp == dpAgain && Ledger.Net(o) == o.Dp - Profile.StartingDp && BossMusic.ForAlley(data, orvel, 1) == ("orvel", 0);
        Check($"Orvel's Table, a win: the stake back, your card back, his {(wonSlab != null ? Name(wonSlab.Code) : "-")} yours (Paid 0, a slab); the one-seat night clears the table "
              + "(alleyclear:, an Unlisted page, Orvel's Marker); the next sitting pays no clear and no page; a draw and a first-turn loss give everything back; his theme is orvel",
            orvelWinOk && laterOk);

        // ---- The held card ----
        {
            var h = Opened(starter, cards, printings, library, 0xA11E0B40);
            bool none0 = !BackAlley.HeldCardDue(h, data);
            h.Seen.Add(BackAlley.GoatClearScene);
            bool goatOnly = !BackAlley.HeldCardDue(h, data);
            h.AlleyTables["orvels-table"] = new AlleyTableState { Cleared = true };
            bool goatFirst = BackAlley.HeldCardDue(h, data);
            var h2 = Opened(starter, cards, printings, library, 0xA11E0B41);
            h2.AlleyTables["orvels-table"] = new AlleyTableState { Cleared = true };
            bool orvelOnly = !BackAlley.HeldCardDue(h2, data);
            h2.Seen.Add(BackAlley.GoatClearScene);
            bool orvelFirst = BackAlley.HeldCardDue(h2, data);
            if (orvelFirst) events.Add(BackAlley.HeldEvent); // the app raises it when it's due
            // The real clear above: due once the cup's scene is seen.
            bool realClear = !BackAlley.HeldCardDue(o, data);
            o.Seen.Add(BackAlley.GoatClearScene);
            realClear &= BackAlley.HeldCardDue(o, data);
            int owned = h2.Collection.Owned(MothsVan.HeldCode), dp = h2.Dp;
            bool shown = MothsVan.HeldShown(h2);
            h2.Seen.Add(BackAlley.HeldScene);
            bool once = !BackAlley.HeldCardDue(h2, data) && MothsVan.HeldShown(h2);
            h2.Seen.Add(BackAlley.HeldHomeScene);
            bool home = !MothsVan.HeldShown(h2) && h2.Collection.Owned(MothsVan.HeldCode) == owned && h2.Dp == dp && Posters.Find("held")!.Earned(circuits, h2);
            var heldScenes = story.Scenes.Where(sc => sc.Trigger == BackAlley.HeldEvent).Select(sc => sc.Id).ToList();
            var vanText = File.ReadAllText(Path.Combine(root, "campaign", "van.txt"));
            bool gated = Regex.IsMatch(vanText, @"pool: van:held-paid\s*\n(#[^\n]*\n)*after: " + Regex.Escape(BackAlley.HeldHomeScene));
            Check($"the held card: heldcard is due once Orvel's Table is cleared and {BackAlley.GoatClearScene} is seen, in either order (not with one alone), once; "
                  + $"its scenes are {string.Join(" then ", heldScenes)}; the HELD row ({Name(MothsVan.HeldCode)}, {Grades.RawLabel(MothsVan.HeldGrade)}) shows until "
                  + $"{BackAlley.HeldHomeScene}, the card never enters the collection, HELD's poster is earned then, and van:held-paid waits for it",
                none0 && goatOnly && goatFirst && orvelOnly && orvelFirst && realClear && shown && once && home && gated && MothsVan.HeldCode == Alley.CodeOf("Scapegoat", cards)
                && heldScenes.SequenceEqual(new[] { BackAlley.HeldScene, BackAlley.HeldHomeScene }) && story.Scenes.Any(sc => sc.Id == BackAlley.GoatClearScene));
        }

        // ---- The Unlisted binder ----
        {
            var u = new Profile();
            uint pot = Alley.CodeOf("Pot of Greed", cards), raigeki = Alley.CodeOf("Raigeki", cards);
            AlleyOpponent Of(string table, string kind, string character, string deck, string handle, params uint[] added) =>
                new AlleyOpponent { Table = table, Kind = kind, Character = character, Base = deck, Handle = handle, Added = added.ToList() };
            var adds = new (string what, bool want, bool got)[]
            {
                ("a regular", true, Unlisted.Add(u, Of("dumpster-row", "regular", BackAlley.RegularId, "goat-control", "Three-Pot Teddy", pot))),
                ("the same table and base", false, Unlisted.Add(u, Of("dumpster-row", "regular", BackAlley.RegularId, "goat-control", "Raigeki Rita", raigeki))),
                ("the same base at another table", true, Unlisted.Add(u, Of("basement", "regular", BackAlley.RegularId, "goat-control", "Pot Pete", pot))),
                ("a regular with no splash", false, Unlisted.Add(u, Of("dumpster-row", "regular", BackAlley.RegularId, "warriors", "Warriors Regular"))),
                ("a headliner", true, Unlisted.Add(u, Of("dumpster-row", "headliner", "yata-mags", "yata-mags", "Yata Mags"))),
                ("its encore", false, Unlisted.Add(u, Of("dumpster-row", "encore", "yata-mags", "yata-mags", "Yata Mags"))),
                ("its wildcard", false, Unlisted.Add(u, Of("basement", "wildcard", "yata-mags", "yata-mags", "Yata Mags"))),
                ("an authored deck", true, Unlisted.Add(u, Of("orvels-table", "authored", "power-patron", "orvels-house", "Patron Orvel"))),
                ("it again", false, Unlisted.Add(u, Of("orvels-table", "authored", "power-patron", "orvels-house", "Patron Orvel"))),
            };
            var wrong = adds.Where(a => a.want != a.got).Select(a => a.what).ToList();
            // Through play: three nights at Dumpster Row; a page for each (table, base) first beaten with a splash, and for its headliner.
            var p = Opened(starter, cards, printings, library, 0xA11E0B50);
            p.Earn(Ledger.Debug, 20000);
            var expected = new List<string>();
            int raisedPages = 0;
            AlleyOpponent? firstRegular = null;
            for (int d = 0; d < 3 * BackAlley.Matches; d++)
            {
                var opp = BackAlley.Lineup(p, data, dumpster, ctx).First(x => x.Seat == BackAlley.NextSeat(p, data, dumpster));
                var key = Unlisted.KeyOf(opp);
                if (!(opp.Kind == "regular" && opp.Added.Count == 0) && !expected.Contains(key)) expected.Add(key);
                if (opp.Kind == "regular" && opp.Added.Count > 0) firstRegular ??= opp;
                var r = Note(Duel(p, s, dumpster, 0));
                if (r != null && r.Events.Contains(Unlisted.Event)) raisedPages++;
            }
            var page = p.UnlistedPages.FirstOrDefault(pg => firstRegular != null && pg.Key == Unlisted.KeyOf(firstRegular));
            bool deckOk = page != null && firstRegular != null && DeckLine(Unlisted.DeckOf(page, ctx)) == DeckLine(BackAlley.DeckOf(firstRegular, ctx))
                          && Unlisted.DeckOf(page, ctx).Name == page.Handle && Unlisted.Contraband(page, ctx).SequenceEqual(firstRegular.Added.Select(c => Banlist.BaseCode(c, cards)).Distinct());
            var orvelPage = o.UnlistedPages.FirstOrDefault(pg => pg.Key == "authored:orvels-house");
            var orvelMarks = orvelPage != null ? Unlisted.Contraband(orvelPage, ctx) : new List<uint>();
            Check($"Unlisted: one page per (table, base), per headliner (its encores and wildcard sittings too) and per authored deck"
                  + $"{(wrong.Count > 0 ? " (wrong: " + string.Join(", ", wrong) + ")" : "")}; three nights at Dumpster Row wrote {p.UnlistedPages.Count} pages "
                  + $"({string.Join(", ", p.UnlistedPages.Select(pg => pg.Key))}) and raised unlisted for each; a page's deck is the deck you met, its contraband marked; "
                  + $"Orvel's page marks {orvelMarks.Count} contraband cards ({string.Join(", ", orvelMarks.Take(3).Select(Name))}...)",
                wrong.Count == 0 && u.UnlistedPages.Select(pg => pg.Kind).SequenceEqual(new[] { "regular", "regular", "headliner", "authored" })
                && p.UnlistedPages.Select(pg => pg.Key).SequenceEqual(expected) && raisedPages == expected.Count && deckOk && orvelMarks.Contains(pot) && orvelMarks.Count >= 10);
        }

        // ---- Posters ----
        {
            var alleyPosters = new[] { "no-list", "lost-card", "orvels-marker", "held", "brawl-bill", "invitational-bill", "midnight-bill" };
            var railCopy = new CardCopy { Code = Alley.CodeOf("Change of Heart", cards), Rarity = Rarity.UltraRare, Grade = 9, Slab = 9 };
            var conditions = new Dictionary<string, Action<Profile>>
            {
                ["no-list"] = p => p.Seen.Add("alley-kit"),
                ["lost-card"] = p => p.Rail.Add(new RailEntry { Copy = railCopy, Price = 4500, ExpiresAt = p.DuelsPlayed + 5 }),
                ["orvels-marker"] = p => p.AlleyTables["orvels-table"] = new AlleyTableState { Cleared = true },
                ["held"] = p => p.Seen.Add(BackAlley.HeldHomeScene),
                ["brawl-bill"] = p => p.AlleyBrackets["brawl"] = new LocalsRecord { Entries = 1, Titles = 1, Best = 0 },
                ["invitational-bill"] = p => p.AlleyBrackets["invitational"] = new LocalsRecord { Entries = 1, Titles = 1, Best = 0 },
                ["midnight-bill"] = p => p.AlleyBrackets["midnight"] = new LocalsRecord { Entries = 1, Titles = 1, Best = 0 },
            };
            List<string> Earned(Profile p) => alleyPosters.Where(id => Posters.Find(id)!.Earned(circuits, p)).ToList();
            var fresh = Opened(starter, cards, printings, library, 0xA11E0B60);
            var atStart = Earned(fresh);
            var wrong = new List<string>();
            foreach (var id in alleyPosters)
            {
                var p = Opened(starter, cards, printings, library, 0xA11E0B60);
                conditions[id](p);
                if (!Earned(p).SequenceEqual(new[] { id })) wrong.Add($"{id}: {string.Join("/", Earned(p))}");
            }
            // Near misses: a bracket entered but not won, a table played but not cleared, the held card's first scene only.
            var near = Opened(starter, cards, printings, library, 0xA11E0B60);
            near.AlleyBrackets["brawl"] = new LocalsRecord { Entries = 3, Titles = 0, Best = 1 };
            near.AlleyBrackets["invitational"] = new LocalsRecord { Entries = 2, Titles = 0, Best = 2 };
            near.AlleyTables["orvels-table"] = new AlleyTableState { Night = 0, WonMask = 0 };
            near.Seen.Add(BackAlley.HeldScene);
            var kitScene = story.Scenes.FirstOrDefault(sc => sc.Id == "alley-kit");
            var bills = alleyPosters.Select(id => Posters.Find(id)!).ToList();
            Check($"posters: the alley's {alleyPosters.Length} ({string.Join(", ", bills.Select(b => $"{b.Title}: {Name(b.Card)}"))}) are locked on a fresh alley save ({atStart.Count} earned), "
                  + $"each earned by its own condition alone{(wrong.Count > 0 ? " (not: " + string.Join("; ", wrong) + ")" : "")}, none by a near miss ({Earned(near).Count}); "
                  + $"NO LIST's scene (alley-kit) plays on {kitScene?.Trigger}",
                atStart.Count == 0 && wrong.Count == 0 && Earned(near).Count == 0 && kitScene?.Trigger == BackAlley.VisitEvent && bills.All(b => cards.Get(b.Card) != null)
                && bills.Select(b => b.Id).Distinct().Count() == bills.Count);
        }

        // ---- Penny's cameo ----
        {
            var pennyDeck = roster.First(r => r.Id == BackAlley.PennyId).Decks.First();
            var e = data.Tables[dumpster].Event!;
            var curation = ctx.Curated.TryGetValue(e.Name, out var cur) ? cur : null;
            var chance = AlleyEconomy.PennyChance;
            bool seatOk, stakeOk, lossOk, winOk, nextOk;
            string stakeSays = "";
            AlleyEconomy.PennyChance = 1;
            try
            {
                var p = Opened(starter, cards, printings, library, 0xA11E0B70);
                p.Earn(Ledger.Debug, 20000);
                var penny = BackAlley.Lineup(p, data, dumpster, ctx)[0];
                var who = BackAlley.PersonalityOf(penny, ctx);
                seatOk = penny.Character == BackAlley.PennyId && penny.Kind == "regular" && penny.Binder == BackAlley.PennyId && penny.Handle == "Penny" && penny.Base == pennyDeck
                         && penny.Added.Count == Alley.SplashSize(e, pennyDeck, curation) && Math.Abs(penny.Skill - e.Skill) < 1e-9 && who.Name == "Penny" && who.Id == BackAlley.PennyId
                         && DeckRules.Validate(BackAlley.DeckOf(penny, ctx), s.List, cards).Count == 0;
                var kuriboh = Ante.Next(p, BackAlley.PennyId, ctx);
                stakeOk = kuriboh != null && kuriboh.Code == Ante.PennyCard && kuriboh.Flag == Ante.PennyFlag && kuriboh.Rarity == Rarity.Common && kuriboh.Grade == 4 && kuriboh.Slab == 0
                          && Ante.Value(kuriboh, ctx) < AlleyEconomy.StreetStake && Ante.Binder(p, BackAlley.PennyId, ctx).Cards.Count == 1 && Ante.PennyCard == Alley.CodeOf("Kuriboh", cards);
                stakeSays = kuriboh != null ? $"Raw {kuriboh.Grade} {Name(kuriboh.Code)}, worth {Ante.Value(kuriboh, ctx):N0}" : "none";
                // A loss and a first-turn loss: nothing of yours held, nothing lost, no antelose or firstturn; her Kuriboh back on top.
                int dp = p.Dp, losses = p.AnteLosses, spent = p.DpSpent.GetValueOrDefault(Ledger.AlleyStake);
                var held = Ante.Hold(p, data, dumpster, penny, null, none, ctx);
                bool heldNothing = held != null && held.Held == 0 && p.Dp == dp && Same(held.Theirs, kuriboh);
                var lost = Note(BackAlley.Record(p, data, dumpster, 1, 10, 0, ctx));
                Ante.Hold(p, data, dumpster, penny, null, none, ctx);
                var firstTurn = Note(BackAlley.Record(p, data, dumpster, 1, 1, 1, ctx));
                // A player with 100 DP can still sit with her (no stake of yours to cover).
                var broke = Opened(starter, cards, printings, library, 0xA11E0B70);
                broke.Spend(Ledger.Debug, broke.Dp - 100);
                bool brokeSits = Ante.Hold(broke, data, dumpster, BackAlley.Lineup(broke, data, dumpster, ctx)[0], null, none, ctx) != null;
                lossOk = heldNothing && lost?.Ante?.Outcome == "loss" && lost.Ante.Lost == 0 && !lost.Events.Contains("antelose") && firstTurn?.Ante?.Outcome == "firstturn"
                         && !firstTurn.Events.Contains("firstturn") && p.Dp == dp && p.AnteLosses == losses && p.DpSpent.GetValueOrDefault(Ledger.AlleyStake) == spent
                         && Same(Ante.Binder(p, BackAlley.PennyId, ctx).Cards[0], kuriboh) && brokeSits;
                // A win: her Kuriboh is yours (Paid 0) and pennywin, the first time; her binder is empty until the next night.
                var won = Note(Duel(p, s, dumpster, 0));
                winOk = won?.Ante?.Won is CardCopy k && k.Code == Ante.PennyCard && k.Paid == 0 && k.Source == CopySource.Ante && won.Events.Contains(BackAlley.PennyEvent)
                        && won.Events.Contains("antewin") && Ante.Next(p, BackAlley.PennyId, ctx) == null;
                for (int k2 = 1; k2 < BackAlley.Matches; k2++) Note(Duel(p, s, dumpster, 0));
                var night1 = BackAlley.Lineup(p, data, dumpster, ctx)[0];
                var fresh = Ante.Next(p, BackAlley.PennyId, ctx);
                var again = Note(Duel(p, s, dumpster, 0));
                nextOk = p.AlleyTables["dumpster-row"].Night == 1 && night1.Character == BackAlley.PennyId && fresh != null && fresh.Code == Ante.PennyCard && fresh.Flag == Ante.PennyFlag
                         && fresh.Seed != kuriboh!.Seed && again?.Ante?.Won?.Code == Ante.PennyCard && !again.Events.Contains(BackAlley.PennyEvent);
            }
            finally { AlleyEconomy.PennyChance = chance; }
            // How often she sits: seating only (a context that builds only her splash), 3,000 nights.
            var seating = new AlleyContext
            {
                Cards = cards, Printings = printings, Lists = s.Lists, List = s.List, Ratings = ctx.Ratings, Curated = ctx.Curated, Roster = roster, Library = library,
                LoadDeck = name => name == pennyDeck || data.Headliners.Any(h => h.Deck == name) ? ctx.LoadDeck(name) : null, Data = data,
            };
            const int Nights = 3000;
            int sat = 0, notSeat1 = 0;
            var q = Opened(starter, cards, printings, library, 0xA11E0B71);
            for (int n = 0; n < Nights; n++)
            {
                q.AlleyTables["dumpster-row"] = new AlleyTableState { Night = n };
                var seats = BackAlley.Lineup(q, data, dumpster, seating);
                if (seats.Any(x => x.Character == BackAlley.PennyId)) sat++;
                if (seats.Skip(1).Any(x => x.Character == BackAlley.PennyId)) notSeat1++;
            }
            double share = sat / (double)Nights;
            // Lineup's rule: her own roll (PennyChance) unless the wildcard took seat 1, which it does WildcardChance x 1/3 of nights
            // when the table has a second unbeaten ready headliner to seat (Dumpster Row: Yata Mags and Return Rook).
            bool hasWildcard = data.Tables[dumpster].Headliners.Count(id => data.FindHeadliner(id) is { Ready: true }) >= 2;
            double pennyTarget = AlleyEconomy.PennyChance * (1 - (hasWildcard ? AlleyEconomy.WildcardChance / 3 : 0));
            Check($"Penny: seat 1 (her warriors, a full Dumpster Row splash, skill {e.Skill}, her own name), her binder one {stakeSays} (under the {AlleyEconomy.StreetStake} stake: "
                  + $"nothing of yours held, a loss costs nothing and raises no antelose or firstturn); beating her wins it (Paid 0) and raises pennywin once; the next night, a fresh "
                  + $"one; she sits {share:P1} of {Nights} nights (target {pennyTarget:P1} ± 2%: {AlleyEconomy.PennyChance:P0}{(hasWildcard ? $", less the wildcard's seat-1 nights" : "")}), "
                  + $"always at seat 1 ({notSeat1} not) [{string.Join("", new[] { seatOk, stakeOk, lossOk, winOk, nextOk }.Select(b => b ? "y" : "n"))}]",
                seatOk && stakeOk && lossOk && winOk && nextOk && Math.Abs(share - pennyTarget) <= 0.02 && notSeat1 == 0);
        }

        // ---- The van's voice and the alley's music ----
        {
            var vanPath = Path.Combine(root, "campaign", "van.txt");
            var van = ShopVoice.Load(vanPath);
            var problems = van.Problems(roster, story.Scenes.Select(sc => sc.Id), circuits, h => BackAlley.IsVoiceHook(h, data, cards));
            var lines = File.Exists(vanPath) ? File.ReadAllLines(vanPath) : Array.Empty<string>();
            var hooks = lines.Where(l => l.StartsWith("pool:")).Select(l => l.Substring(5).Trim()).ToHashSet();
            var missing = BackAlley.VanHooks.Where(h => !hooks.Contains(h)).ToList();
            var stolen = lines.Where(l => !l.TrimStart().StartsWith("#") && l.Contains("stolen", StringComparison.OrdinalIgnoreCase)).ToList();
            var speakers = lines.Where(l => !l.StartsWith("#") && !l.StartsWith("pool:") && !l.StartsWith("after:") && !l.StartsWith("before:") && !l.StartsWith("band:") && l.Contains(':'))
                .Select(l => l.Split(':')[0].Trim()).Distinct().ToList();
            Check($"the van's voice: van.txt passes ShopVoice.Problems with the alley's hooks{(problems.Count > 0 ? ": " + string.Join("; ", problems.Take(3)) : "")}; every van hook has a pool"
                  + $"{(missing.Count > 0 ? " (none for: " + string.Join(", ", missing) + ")" : "")}; speakers {string.Join(", ", speakers)}; Moth never says \"stolen\" ({stolen.Count})",
                File.Exists(vanPath) && problems.Count == 0 && missing.Count == 0 && stolen.Count == 0 && speakers.Contains("moth"));
            var regular = Enumerable.Range(0, BackAlley.Matches + 1).Select(seat => BossMusic.ForAlley(data, dumpster, seat)).ToList();
            var nyxSeats = nyx >= 0 ? Enumerable.Range(1, data.Tables[nyx].SeatCount).Select(seat => BossMusic.ForAlley(data, nyx, seat)).ToList() : new List<(string, int)>();
            Check($"the alley's music: Dumpster Row's seats 1-3 play alley, seat 4 and an encore a fourth up ({string.Join(", ", regular.Select(m => $"{m.theme} {m.transpose:+0;-0;0}"))}); "
                  + $"Orvel's seat plays orvel, Nyx's last seat nyx ({string.Join(", ", nyxSeats.Select(m => m.Item1))}); all three themes exist",
                regular[0] == ("alley", 5) && regular.Skip(1).Take(3).All(m => m == ("alley", 0)) && regular[4] == ("alley", 5) && BossMusic.ForAlley(data, orvel, 1) == ("orvel", 0)
                && nyxSeats.Count == 4 && nyxSeats.Take(3).All(m => m == ("alley", 0)) && nyxSeats[3] == ("nyx", 0)
                && new[] { "alley", "orvel", "nyx" }.All(Ygo.Engine.Audio.Sfx.Themes.ContainsKey));
        }

        // ---- The Phase B review's fixes ----
        // Orvel's Table, your card and his slab the same card, three owned: your copy comes home before his goes in, so the
        // 4th-copy rule sees all of them (never 4 owned) and your copy (id, seed, slab) stays.
        {
            CardCopy? orig = null;
            AlleyResult? won4 = null;
            Profile? q = null;
            int idBefore = 0, ownedBefore = 0;
            uint seedBefore = 0, code = 0;
            for (uint salt = 1; salt < 200 && won4 == null; salt++)
            {
                var p = OrvelOpen(starter, cards, printings, library, salt);
                foreach (var st in Ante.Binder(p, "orvel", ctx).Cards.ToList())
                {
                    if (p.Collection.Owned(st.Code) > 0) continue;
                    for (int g = st.Slab - 1; g >= 1 && won4 == null; g--)
                    {
                        var mines = Enumerable.Range(0, Collection.MaxCopies).Select(k =>
                        {
                            var c = Ante.ToCopy(new Stake { Code = st.Code, Rarity = st.Rarity, Grade = g, Slab = g, Seed = (uint)k + 1 }, p);
                            c.Source = CopySource.Prize;
                            return c;
                        }).ToList();
                        if (p.Collection.Value(mines[0]) < AlleyEconomy.OrvelMinCard) continue;
                        foreach (var c in mines) p.Collection.Insert(c);
                        if (Ante.Next(p, "orvel", ctx, mines[0])?.Code != st.Code) { foreach (var c in mines) p.Collection.Remove(c); continue; }
                        (orig, q, code, idBefore, seedBefore, ownedBefore) = (mines[0], p, st.Code, mines[0].Id, mines[0].Seed, p.Collection.Owned(st.Code));
                        won4 = OrvelDuel(p, s, orig, 0);
                    }
                    if (won4 != null) break;
                }
            }
            int ownedAfter = q?.Collection.Owned(code) ?? -1, reloaded = q != null ? RoundTrip(q, printings, cards).Collection.Owned(code) : -1;
            bool origStays = q != null && orig != null && q.Collection.CopiesOf(code).Any(c => c == orig && c.Id == idBefore && c.Seed == seedBefore && c.IsSlab);
            Check($"Orvel's Table: staking one of 3 {(code != 0 ? Name(code) : "-")} against his slab of the same card and winning leaves {ownedAfter} owned ({reloaded} after a reload; "
                  + $"{ownedBefore} before), your staked copy back as it was ({origStays}), his slab {(won4?.Ante?.Added?.Kept == true ? "kept" : "a 4th copy, for 0 DP")}",
                won4?.Ante?.Outcome == "win" && ownedBefore == Collection.MaxCopies && ownedAfter <= Collection.MaxCopies && reloaded <= Collection.MaxCopies && origStays
                && Ledger.Net(q!) == q!.Dp - Profile.StartingDp);

            // Hold at Orvel's refuses a card that isn't in the collection (sold, or a copy never yours): nothing moves.
            var h = OrvelOpen(starter, cards, printings, library, 0xA11E0B71);
            var soldOff = SpareFor(h, s, 0.5);
            h.Collection.Remove(soldOff);
            var stranger = Ante.ToCopy(Ante.Binder(h, "orvel", ctx).Cards[0], h);
            int dpH = h.Dp, binderH = Ante.Binder(h, "orvel", ctx).Cards.Count;
            var hOpp = BackAlley.Lineup(h, data, orvel, ctx)[0];
            bool refused = Ante.Hold(h, data, orvel, hOpp, soldOff, none, ctx) == null && Ante.Hold(h, data, orvel, hOpp, stranger, none, ctx) == null;
            Check($"Ante.Hold at Orvel's refuses a staked card you don't hold (sold, or never yours): nothing held, no DP or binder change",
                refused && h.AnteLive == null && h.Dp == dpH && Ante.Binder(h, "orvel", ctx).Cards.Count == binderH && !h.Collection.All.Contains(soldOff));
        }

        // The rail runs out on a regular table's win (a stake won, so {card} is theirs): railgone's {sold} names the card Moth
        // sold, and the first-railgone scene says it. Every story placeholder is one Story.Fill fills.
        {
            var r = OrvelOpen(starter, cards, printings, library, 77);
            var railedCard = SpareFor(r, s, 0.2);
            var lost = OrvelDuel(r, s, railedCard, 1);
            AlleyResult? lastDuel = null;
            for (int k = 0; k < 2 * AlleyEconomy.RailDuels && r.Rail.Count > 0; k++) lastDuel = Note(Duel(r, s, dumpster, 0));
            var scene = story.Scenes.FirstOrDefault(sc => sc.Id == "first-railgone");
            var said = scene != null && lastDuel != null ? Story.Fill(scene.Lines[0].Text, null, r, fill: lastDuel.Fill) : "";
            var used = story.Scenes.SelectMany(sc => sc.Lines).SelectMany(l => System.Text.RegularExpressions.Regex.Matches(l.Text, @"\{([^}]*)\}").Cast<System.Text.RegularExpressions.Match>())
                .Select(m => m.Groups[1].Value).Distinct().ToList();
            var unknownMarks = used.Where(m => !Story.Placeholders.Contains(m)).ToList();
            var allFilled = Story.Fill(string.Join(" ", Story.Placeholders.Select(x => "{" + x + "}")), null, r);
            Check($"railgone on a Dumpster Row win ({string.Join(", ", lastDuel?.Events ?? new List<string>())}): {{sold}} is {(lastDuel?.Fill.GetValueOrDefault(BackAlley.SoldKey) ?? "-")} "
                  + $"(the rail held {Name(railedCard.Code)}), {{card}} the stake won ({lastDuel?.Fill.GetValueOrDefault("card")}); first-railgone reads \"{said}\"; story.txt's placeholders "
                  + $"({string.Join(", ", used)}) are all Story.Fill's{(unknownMarks.Count > 0 ? " (not: " + string.Join(", ", unknownMarks) + ")" : "")}",
                lost?.Ante?.Railed != null && lastDuel != null && lastDuel.Events.Contains("railgone") && lastDuel.Events.Contains("antewin") && r.Rail.Count == 0
                && lastDuel.Fill.GetValueOrDefault(BackAlley.SoldKey) == Name(railedCard.Code) && lastDuel.Fill.GetValueOrDefault("card") == Name(lastDuel.Ante!.Won!.Code)
                && said.Contains(Name(railedCard.Code)) && !said.Contains('{') && unknownMarks.Count == 0 && used.Contains(BackAlley.SoldKey) && !allFilled.Contains('{')
                && BackAlley.Placeholders.Contains(BackAlley.SoldKey));
        }

        // The van: a listing shown before a restock is refused even after the tab is shown again, and a slot already sold
        // is refused through a second listing of it (shown twice, its Sold flag stale).
        {
            var w = OrvelOpen(starter, cards, printings, library, 0xA11E0B72, 500000);
            var shelfA = MothsVan.Shelf(w, packs, ctx);
            var shelfB = MothsVan.Shelf(w, packs, ctx);
            bool shelfFirst = MothsVan.Buy(w, shelfA[0], true) >= 0;
            int dpW = w.Dp;
            bool shelfTwice = shelfB[0].Epoch == shelfA[0].Epoch && !shelfB[0].Sold && MothsVan.Buy(w, shelfB[0], true) == -1 && w.Dp == dpW;
            var shelfOld = MothsVan.Shelf(w, packs, ctx)[1];
            w.AlleyDuels += AlleyEconomy.ShelfEvery;
            var shelfNew = MothsVan.Shelf(w, packs, ctx);
            bool shelfStale = shelfOld.Epoch != shelfNew[0].Epoch && MothsVan.Buy(w, shelfOld, true) == -1 && w.Dp == dpW && shelfNew.All(l => !l.Sold);
            var usedA = MothsVan.Used(w, packs, ctx);
            var usedB = MothsVan.Used(w, packs, ctx);
            int cheapest = usedA.Select((l, i) => (l, i)).Where(x => MothsVan.Blocked(w, x.l) == null).OrderBy(x => x.l.Price).First().i;
            bool usedFirst = MothsVan.Buy(w, usedA[cheapest], false) >= 0;
            dpW = w.Dp;
            bool usedTwice = MothsVan.Buy(w, usedB[cheapest], false) == -1 && w.Dp == dpW;
            var usedOld = MothsVan.Used(w, packs, ctx).First(l => !l.Sold && MothsVan.Blocked(w, l) == null);
            for (int k = 0; k < AlleyEconomy.VanUsedEvery; k++) w.NoteResult("check", 0);
            MothsVan.Used(w, packs, ctx);
            bool usedStale = MothsVan.Buy(w, usedOld, false) == -1 && w.Dp == dpW;
            Check($"the van refuses a stale listing (shown before a restock, the tab shown again since: shelf {shelfStale}, used {usedStale}) and a slot already sold "
                  + $"through a second listing of it (shelf {shelfTwice}, used {usedTwice}); nothing spent",
                shelfFirst && usedFirst && shelfTwice && shelfStale && usedTwice && usedStale && Ledger.Net(w) == w.Dp - Profile.StartingDp);
        }

        // The shelf mid-period when the pool changes (an exclusion, a list's history): the cards still possible keep their
        // order and a bought slab stays sold; an older save's sold slot is matched to the slab it bought (by its seed).
        {
            var w = OrvelOpen(starter, cards, printings, library, 0xA11E0B73, 500000);
            var items = MothsVan.ShelfItems(packs, ctx);
            var before = MothsVan.Shelf(w, items);
            bool shelfBought = MothsVan.Buy(w, before[2], true) >= 0;
            string K(Listing l) => MothsVan.ShelfKey(l.Copy.Code, l.Copy.Rarity, l.Copy.Slab);
            // The pool shrinks: one card on the shelf (not the one bought) and four that aren't go.
            var shelfGone = new HashSet<uint> { before[0].Copy.Code };
            foreach (var code in items.Select(i => i.code).Distinct().Where(c => before.All(l => l.Copy.Code != c)).Take(4)) shelfGone.Add(code);
            var after = MothsVan.Shelf(w, items.Where(i => !shelfGone.Contains(i.code)).ToList());
            var kept = before.Skip(1).Select(K).ToList();
            bool stable = kept.All(k => after.Any(l => K(l) == k)) && after.First(l => K(l) == K(before[2])).Sold
                          && after.Count(l => l.Sold) == 1 && MothsVan.Buy(w, after.First(l => K(l) == K(before[2])), true) == -1;
            var shelfBack = RoundTrip(w, printings, cards);
            bool keptSold = MothsVan.Shelf(shelfBack, items).First(l => K(l) == K(before[2])).Sold;
            // An older save: slot 3 sold, its slab in the collection under the slot's seed; the shelf now shows that card sold.
            var old = OrvelOpen(starter, cards, printings, library, 0xA11E0B74, 500000);
            var oldShelf = MothsVan.Shelf(old, items);
            var target = oldShelf[1];
            uint legacySeed = Hash.Mix(old.Collection.Salt ^ 0x5E1Fu ^ 0x5EED, (uint)MothsVan.ShelfEpoch(old), 3u);
            var slab = old.Collection.NewCopy(target.Copy.Code, target.Copy.Rarity, CopySource.Used);
            slab.Seed = legacySeed; slab.Grade = slab.Slab = slab.Floor = target.Copy.Slab;
            old.Collection.Insert(slab);
            old.VanShelfSold.Add(3);
            var oldAfter = MothsVan.Shelf(RoundTrip(old, printings, cards), items);
            bool legacy = oldAfter.First(l => K(l) == K(target)).Sold && oldAfter.Count(l => l.Sold) == 1;
            Check($"Moth's shelf when the pool changes mid-period: the other cards stay ({stable}), the slab bought stays sold, after a reload too ({keptSold}); "
                  + $"an older save's sold slot marks the slab it bought ({Name(target.Copy.Code)}: {legacy})",
                shelfBought && stable && keptSold && legacy);
        }

        // Unlisted: a Phase A save's beaten headliners get their pages at load (once, no event); Penny's page has its own key,
        // so the Dumpster Row warriors regular still gets one.
        {
            var b = Opened(starter, cards, printings, library, 0xA11E0B73);
            var beaten = data.Headliners.First(hl => hl.Ready);
            var metOnly = data.Headliners.First(hl => hl.Ready && hl.Id != beaten.Id);
            b.Headliners[beaten.Id] = new HeadlinerState { Met = 2, Beaten = 1 };
            b.Headliners[metOnly.Id] = new HeadlinerState { Met = 1 };
            int seenBefore = b.Seen.Count;
            int filled = Unlisted.Backfill(b, data), again = Unlisted.Backfill(RoundTrip(b, printings, cards), data);
            var hlPage = b.UnlistedPages.FirstOrDefault();
            var hlTable = data.Tables.First(t => t.Headliners.Contains(beaten.Id));
            bool backfillOk = filled == 1 && again == 0 && Unlisted.Backfill(b, data) == 0 && b.UnlistedPages.Count == 1 && hlPage?.Key == "hl:" + beaten.Id && hlPage.Kind == "headliner"
                              && hlPage.Table == hlTable.Id && hlPage.Base == beaten.Deck && hlPage.Handle == beaten.Handle && Unlisted.DeckOf(hlPage, ctx).Main.Count >= 40 && b.Seen.Count == seenBefore;
            var u = new Profile();
            var pennyDeck = roster.First(r => r.Id == BackAlley.PennyId).Decks.First();
            uint pot = Alley.CodeOf("Pot of Greed", cards);
            var penny = new AlleyOpponent { Table = "dumpster-row", Kind = "regular", Character = BackAlley.PennyId, Base = pennyDeck, Handle = "Penny", Added = new List<uint> { pot } };
            var warriors = new AlleyOpponent { Table = "dumpster-row", Kind = "regular", Character = BackAlley.RegularId, Base = pennyDeck, Handle = "Two-Sword Tam", Added = new List<uint> { pot } };
            bool pennyOk = Unlisted.Add(u, penny) && Unlisted.Add(u, warriors) && !Unlisted.Add(u, penny) && u.UnlistedPages.Count == 2 && u.UnlistedPages[0].Key == Unlisted.PennyKey
                           && u.UnlistedPages[0].Kind == "regular" && u.UnlistedPages[1].Key == $"dumpster-row:{pennyDeck}"
                           && DeckLine(Unlisted.DeckOf(u.UnlistedPages[0], ctx)) == DeckLine(BackAlley.DeckOf(penny, ctx));
            Check($"Unlisted: a save from before the binder gets {beaten.Handle}'s page at load ({hlPage?.Key}, {filled} added, then {again}; no scene), not {metOnly.Handle}'s (met, not "
                  + $"beaten); Penny's page is {Unlisted.PennyKey} (her splash, as met) beside the {pennyDeck} regular's ({u.UnlistedPages.ElementAtOrDefault(1)?.Key})",
                backfillOk && pennyOk);
        }

        // Orvel's versus: his showcase is his deck's contraband (the CONTRABAND label is over contraband); a regular's leads with its splash.
        {
            var vs = OrvelOpen(starter, cards, printings, library, 0xA11E0B74);
            var houseSeat = BackAlley.Lineup(vs, data, orvel, ctx)[0];
            var shown = BackAlley.Showcase(houseSeat, ctx);
            var marks = BackAlley.ShowcaseContraband(houseSeat, ctx);
            var reg = BackAlley.Lineup(vs, data, dumpster, ctx).First(x => x.Kind == "regular" && x.Added.Count > 0);
            var regShown = BackAlley.Showcase(reg, ctx);
            Check($"Orvel's versus shows his contraband ({string.Join(", ", shown.Select(Name))}; {marks.Count} in his deck); a regular's leads with its splash "
                  + $"({string.Join(", ", regShown.Select(Name))})",
                shown.Count == 3 && shown.All(marks.Contains) && marks.SetEquals(Unlisted.Contraband(new UnlistedPage { Table = "orvels-table", Kind = "authored", Base = houseSeat.Base, Handle = houseSeat.Handle }, ctx))
                && BackAlley.ShowcaseContraband(reg, ctx).Contains(regShown[0]));
        }
    }

    // ---- Phase C: Nyx's Table and the alley's brackets ------------------------------------------------

    /// <summary>
    /// Chunk C3's alley checks (docs/dev/ALLEY-SPEC-2026-11-01.md section 16, Phase C): Nyx's four seats (seat 3 the headliner
    /// when it's ready, else a stand-in drawn from the table Nyx's opens from), her deck and the `allow:` rule, a night
    /// cleared; the Alley Brawl's and the Midnight Open's entries, placements and ledger kinds through
    /// <see cref="Brackets.Finish"/>, their Contraband Packs (Paid 0, sell for 0), the Open's token on the first title only,
    /// the records, and the story events they raise.
    /// </summary>
    private static void PhaseCChecks(Setup s, Deck starter, ICardDataProvider cards, PrintingIndex printings, Library library, List<Personality> roster,
        Action<string, bool> Check, HashSet<string> events)
    {
        var data = s.Data;
        var ctx = s.Ctx;
        int none(uint _) => 0;
        string Name(uint c) => cards.Get(c)?.Name ?? c.ToString();
        int nyx = data.IndexOf("nyx"), blackMarket = data.IndexOf("black-market"), basement = data.IndexOf("basement");
        var bm = data.Tables[blackMarket];
        var bmPool = ctx.Pool(bm.Event!.EraCap).ToHashSet();
        var excluded = Alley.Excluded.Select(n => Alley.CodeOf(n, cards)).ToHashSet();
        AlleyContext With(AlleyData d, Func<string, Deck?>? load = null, Dictionary<string, double>? ratings = null) => new AlleyContext
        {
            Cards = cards, Printings = printings, Lists = s.Lists, List = s.List, Ratings = ratings ?? ctx.Ratings, Curated = ctx.Curated, Roster = roster, Library = library,
            LoadDeck = load ?? ctx.LoadDeck, Data = d,
        };

        // ---- Nyx's seats ----
        var p = OrvelOpen(starter, cards, printings, library, 0xA11E0C01);
        var seats = BackAlley.Lineup(p, data, nyx, ctx);
        bool shutBefore = !BackAlley.IsUnlocked(Opened(starter, cards, printings, library, 0xA11E0C01), data, nyx);
        string Seat(AlleyOpponent o) => $"{o.Seat}: {o.Kind} {o.Handle} [{o.Base}] {o.Skill:0.00}";
        // The expected seats come from the parsed data (the difficulty pass retunes them): seat 1 and seat 3's stand-in are regulars at the
        // skill the data writes (seat 3's in its `else regular <skill>`), seat 2 the authored as-built deck, seat 4 Nyx's own.
        var nyxSpecs = data.Tables[nyx].Seats.OrderBy(x => x.Seat).ToList();
        double StandInSkill(AlleySeatSpec spec) => double.Parse(spec.Else.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], System.Globalization.CultureInfo.InvariantCulture);
        double seat1Skill = nyxSpecs[0].Skill, seat3Skill = StandInSkill(nyxSpecs[2]);
        var asBuiltSpec = nyxSpecs[1];
        string asBuilt = asBuiltSpec.Character;
        string asBuiltLine = Regex.Match(s.EventsText, @"^seat: 2 authored .*\| as-built[^\r\n]*", RegexOptions.Multiline).Value;
        bool Regular(AlleyOpponent o, double skill) =>
            o.Kind == "regular" && o.Character == BackAlley.RegularId && bm.Bases.Contains(o.Base) && Math.Abs(o.Skill - skill) < 1e-9 && o.Added.Count > 0
            && o.Added.All(bmPool.Contains) && Math.Abs(o.Elo - (ctx.Ratings[o.Base] + bm.SplashElo)) < 1e-6 && o.Binder == $"regular:nyx.{o.Seat}.{o.Night}"
            && DeckRules.Validate(BackAlley.DeckOf(o, ctx), s.List, cards).Count == 0;
        bool seatsOk = seats.Count == 4 && seats.Select(o => o.Seat).SequenceEqual(new[] { 1, 2, 3, 4 })
                       && Regular(seats[0], seat1Skill)
                       && seats[1].Kind == "authored" && seats[1].Character == asBuiltSpec.Character && seats[1].Base == asBuiltSpec.Deck && asBuiltSpec.AsBuilt && Math.Abs(seats[1].Skill - asBuiltSpec.Skill) < 1e-9 && seats[1].Binder == "nyx:2"
                       && Regular(seats[2], seat3Skill) && seats[2].Handle != seats[0].Handle
                       && seats[3].Kind == "authored" && seats[3].Character == "nyx" && seats[3].Base == "gravekeepers-contraband" && seats[3].Skill == 1.0 && seats[3].Binder == "nyx:4"
                       && seats.All(o => Ante.Binder(p, o.Binder, ctx).Cards.Count > 0) && Ante.StakeFor(data.Tables[nyx].Tier) == AlleyEconomy.DenStake;
        Check($"Nyx's Table: shut until The Black Market is cleared, then four seats ({string.Join("; ", seats.Select(Seat))}): a Black Market regular at {seat1Skill:0.00}, {asBuilt} ({asBuiltSpec.Deck}) as built "
              + $"at {asBuiltSpec.Skill:0.00}, seat 3's stand-in (Lab Coat Lyle isn't ready) a Black Market regular at {seat3Skill:0.00}, Nyx's Gravekeeper's Contraband at 1.0 (all read from {AlleyData.FileName}); Den stakes ({AlleyEconomy.DenStake:N0}), a binder each",
            shutBefore && BackAlley.IsUnlocked(p, data, nyx) && seatsOk);
        // The next night: the authored seats stay, seat 1 and the stand-in are drawn anew.
        var night1 = OrvelOpen(starter, cards, printings, library, 0xA11E0C01);
        night1.AlleyTables["nyx"] = new AlleyTableState { Night = 1 };
        var seats1 = BackAlley.Lineup(night1, data, nyx, ctx);
        Check($"Nyx's Table, night 2: seats 2 and 4 the same, seat 1 and seat 3's stand-in drawn anew ({Seat(seats1[0])}; {Seat(seats1[2])})",
            seats1.Count == 4 && Face(seats1[1]) == Face(seats[1]) && Face(seats1[3]) == Face(seats[3]) && Face(seats1[0]) != Face(seats[0]) && Face(seats1[2]) != Face(seats[2])
            && Regular(seats1[0], seat1Skill) && Regular(seats1[2], seat3Skill));
        // Seat 3's stand-in draws from the table Nyx's opens from (it has no event: of its own): moved to open from The Basement
        // Game, it draws Basement regulars while seat 1 (event: The Black Market) still draws the Black Market's.
        var moved = AlleyData.Parse(s.EventsText.Replace("tier: nyx\nopens: The Black Market", "tier: nyx\nopens: The Basement Game"), cards);
        var movedP = OrvelOpen(starter, cards, printings, library, 0xA11E0C02);
        var movedSeats = BackAlley.Lineup(movedP, moved, moved.IndexOf("nyx"), With(moved));
        var bg = moved.Tables[moved.IndexOf("basement")];
        // With Lab Coat Lyle ready, seat 3 is his, beaten or not.
        var ready = AlleyData.Parse(s.EventsText, cards);
        ready.FindHeadliner("scientist-turtle")!.Ready = true;
        var readyP = OrvelOpen(starter, cards, printings, library, 0xA11E0C03);
        var withLyle = BackAlley.Lineup(readyP, ready, ready.IndexOf("nyx"), With(ready));
        var beatenP = OrvelOpen(starter, cards, printings, library, 0xA11E0C04);
        beatenP.Headliners["scientist-turtle"] = new HeadlinerState { Met = 1, Beaten = 1 };
        var afterBeaten = BackAlley.Lineup(beatenP, ready, ready.IndexOf("nyx"), With(ready));
        Check($"seat 3: the stand-in draws from the table Nyx's opens from (opened from The Basement Game it's {movedSeats[2].Base}, seat 1 still {movedSeats[0].Base}); "
              + $"with Lab Coat Lyle ready the seat is his ({withLyle[2].Kind} {withLyle[2].Handle}, binder {withLyle[2].Binder}), beaten or not",
            movedSeats.Count == 4 && bg.Bases.Contains(movedSeats[2].Base) && Math.Abs(movedSeats[2].Skill - seat3Skill) < 1e-9 && bm.Bases.Contains(movedSeats[0].Base)
            && withLyle.Count == 4 && withLyle[2].Kind == "headliner" && withLyle[2].Character == "scientist-turtle" && withLyle[2].Handle == "Lab Coat Lyle" && withLyle[2].Binder == "hl:scientist-turtle"
            && afterBeaten.Count == 4 && afterBeaten[2].Kind == "headliner" && afterBeaten[2].Character == "scientist-turtle"
            && Face(withLyle[1]) == Face(seats[1]) && Face(withLyle[3]) == Face(seats[3]));

        // ---- Nyx's deck and the allow: rule ----
        var nyxDeck = BackAlley.DeckOf(seats[3], ctx);
        var nyxPersonality = BackAlley.PersonalityOf(seats[3], ctx);
        var yataDeck = BackAlley.DeckOf(seats[1], ctx);   // seat 2's as-built roster deck (the data names it)
        var yataExcluded = yataDeck.Main.Concat(yataDeck.Extra).Select(c => Banlist.BaseCode(c, cards)).Where(excluded.Contains).Distinct().ToList();
        var nyxIllegal = DeckRules.Validate(nyxDeck, s.List, cards);
        // CPUs are exempt from ban lists (the Alley list binds only you): a deck built for the alley is held to the alley's own
        // rule, no excluded card its seat doesn't allow; the Alley list's other limits are shown, not enforced.
        Check($"Nyx's deck: {nyxDeck.Main.Count} main, #recipe {nyxDeck.Note("recipe")}, no excluded card and an empty allow: (over the Alley list's limits, CPU-exempt: "
              + $"{(nyxIllegal.Count > 0 ? string.Join("; ", nyxIllegal.Select(x => x.Message)) : "none")}); she plays as {nyxPersonality.Name} "
              + $"(skill {nyxPersonality.Skill:0.0}); {asBuiltSpec.Deck} sits as built ({(yataExcluded.Count > 0 ? string.Join(", ", yataExcluded.Select(Name)) + " excluded" : "no excluded card")}, exempt as an as-built rated roster deck)",
            nyxDeck.Main.Count == 40 && nyxDeck.Note("recipe") == "eldlich" && nyxIllegal.All(x => !excluded.Contains(Banlist.BaseCode(x.Code, cards)))
            && !nyxDeck.Main.Concat(nyxDeck.Extra).Any(c => excluded.Contains(Banlist.BaseCode(c, cards))) && data.Tables[nyx].Seats[3].Allow.Count == 0
            && nyxPersonality.Id == "nyx" && nyxPersonality.Skill == 1.0 && ctx.Ratings.ContainsKey(asBuiltSpec.Deck)
            && data.Tables[nyx].Seats[1].AsBuilt && !data.Tables[nyx].Seats[3].AsBuilt);
        // A boss-only card in Nyx's deck: Problems names it until her allow: does; a Never card can't be allowed; the as-built seat
        // without its rating loses the exemption.
        var bossCard = Alley.BossOnly.Select(n => Alley.CodeOf(n, cards)).First(c => cards.Get(c) is CardInfo ci && !ci.IsExtraDeckMonster && !ci.Name.Contains(',') && !nyxDeck.Main.Contains(c));
        Deck? Spiked(string name)
        {
            var d = ctx.LoadDeck(name);
            if (d != null && name == "gravekeepers-contraband") { d.Main.RemoveAt(d.Main.Count - 1); d.Main.Add(bossCard); }
            return d;
        }
        var spiked = data.Problems(With(data, Spiked)).Where(x => x.Contains("seat 4")).ToList();
        var allowedText = s.EventsText.Replace("gravekeepers-contraband | skill 1.0 | allow:", $"gravekeepers-contraband | skill 1.0 | allow: {Name(bossCard)}");
        var allowedData = AlleyData.Parse(allowedText, cards);
        var allowed = allowedData.Problems(With(allowedData, Spiked)).Where(x => x.Contains("seat 4")).ToList();
        var neverText = s.EventsText.Replace("gravekeepers-contraband | skill 1.0 | allow:", "gravekeepers-contraband | skill 1.0 | allow: Wind-Up Hunter");
        var neverData = AlleyData.Parse(neverText, cards);
        var never = neverData.Problems(With(neverData)).Where(x => x.Contains("seat 4")).ToList();
        var unrated = data.Problems(With(data, ratings: ctx.Ratings.Where(r => r.Key != asBuiltSpec.Deck).ToDictionary(r => r.Key, r => r.Value))).Where(x => x.Contains("seat 2")).ToList();
        Check($"allow: Nyx's deck given {Name(bossCard)} (boss-only) is a problem (\"{spiked.FirstOrDefault()}\") until her allow: names it ({allowed.Count} then); "
              + $"allow: can't name a Never card (\"{never.FirstOrDefault()}\"); an as-built seat must be a rated roster deck ({asBuiltSpec.Deck} unrated: \"{unrated.FirstOrDefault()}\")",
            allowedData.Tables[allowedData.IndexOf("nyx")].Seats[3].Allow.Contains(bossCard) && spiked.Count == 1 && spiked[0].Contains(Name(bossCard)) && allowed.Count == 0
            && never.Count == 1 && never[0].Contains("Wind-Up Hunter") && unrated.Count == 1 && unrated[0].Contains($"as-built {asBuiltSpec.Deck} isn't a rated deck"));
        // The exemption is the seat's `as-built`, never "has a rating": --rate-decks leaves decks/alley out, and a rating given
        // to Nyx's deck anyway (the review's P8) still leaves it checked, and still shown at eldlich's Elo plus the splash.
        var rated = new Dictionary<string, double>(ctx.Ratings) { ["gravekeepers-contraband"] = 1620, ["orvels-house"] = 1700 };
        var ratedSpiked = data.Problems(With(data, Spiked, rated)).Where(x => x.Contains("seat 4")).ToList();
        var notAsBuiltText = s.EventsText.Replace(asBuiltLine, asBuiltLine.Replace(" | as-built", ""));
        var notAsBuilt = AlleyData.Parse(notAsBuiltText, cards);
        var yataChecked = notAsBuilt.Problems(With(notAsBuilt)).Where(x => x.Contains("seat 2")).ToList();
        var asBuiltNyxText = s.EventsText.Replace("gravekeepers-contraband | skill 1.0 | allow:", "gravekeepers-contraband | skill 1.0 | allow: | as-built");
        var asBuiltNyx = AlleyData.Parse(asBuiltNyxText, cards);
        var asBuiltNyxProblems = asBuiltNyx.Problems(With(asBuiltNyx, null, rated)).Where(x => x.Contains("seat 4")).ToList();
        var ratedCtx = With(data, null, rated);
        var nyxRated = BackAlley.Lineup(OrvelOpen(starter, cards, printings, library, 0xA11E0C01), data, nyx, ratedCtx)[3];
        var orvelRated = BackAlley.Lineup(OrvelOpen(starter, cards, printings, library, 0xA11E0C01), data, data.IndexOf("orvels-table"), ratedCtx).FirstOrDefault();
        double nyxElo = ctx.Ratings["eldlich"] + bm.SplashElo, orvelElo = ctx.Ratings["power-patron"] + bm.SplashElo;
        var rateFiles = DeckRating.DeckFiles(s.Root).Select(f => Path.GetFileNameWithoutExtension(f)).ToHashSet();
        bool rateSkips = !rateFiles.Contains("gravekeepers-contraband") && !rateFiles.Contains("orvels-house") && rateFiles.Contains("eldlich") && rateFiles.Contains(asBuiltSpec.Deck);
        Check($"the authored-seat exemption is `as-built`, not a rating: Nyx's deck rated {rated["gravekeepers-contraband"]:0} and given {Name(bossCard)} is still flagged "
              + $"(\"{ratedSpiked.FirstOrDefault()}\"); {asBuiltSpec.Deck} without as-built is checked ({yataChecked.Count} problems); Nyx's seat marked as-built isn't a roster deck "
              + $"(\"{asBuiltNyxProblems.FirstOrDefault()}\"); rated, the panels still read eldlich's {nyxRated.Elo:0} and power-patron's {orvelRated?.Elo:0} plus the splash; "
              + "--rate-decks leaves decks/alley out",
            ratedSpiked.Count == 1 && ratedSpiked[0].Contains(Name(bossCard)) && yataChecked.Count == yataExcluded.Count && yataChecked.All(x => x.Contains($"{asBuiltSpec.Deck} runs"))
            && asBuiltNyxProblems.Any(x => x.Contains("as-built gravekeepers-contraband isn't a rated deck")) && Math.Abs(nyxRated.Elo - nyxElo) < 1e-6
            && orvelRated != null && Math.Abs(orvelRated.Elo - orvelElo) < 1e-6 && Math.Abs(seats[3].Elo - nyxElo) < 1e-6 && rateSkips);

        // ---- The Black Market's first clear opens two tables: both announced ----
        {
            var bmp = Opened(starter, cards, printings, library, 0xA11E0C09);
            foreach (var id in new[] { "dumpster-row", "basement" }) bmp.AlleyTables[id] = new AlleyTableState { Cleared = true };
            bmp.Earn(Ledger.Debug, 20000);
            var bmNight = Enumerable.Range(0, BackAlley.Matches).Select(_ => Duel(bmp, s, blackMarket, 0)).ToList();
            var bmClear = bmNight.Last();
            if (bmClear != null) events.UnionWith(bmClear.Events);
            Check($"The Black Market's first clear opens Orvel's Table and Nyx's Table, and the result names both (Opened: {string.Join(", ", bmClear?.Opened ?? new List<string>())}; \"{bmClear?.Summary}\")",
                bmClear != null && bmClear.FirstClear && bmClear.Opened.SequenceEqual(new[] { "Orvel's Table", "Nyx's Table" })
                && bmClear.Summary.Contains("Orvel's Table and Nyx's Table open") && BackAlley.IsUnlocked(bmp, data, nyx));
        }

        // ---- A night at Nyx's Table: four wins clear it ----
        p.Earn(Ledger.Debug, 20000);
        int tokens0 = p.Tokens;
        var night = new List<AlleyResult?>();
        for (int k = 0; k < 4; k++)
        {
            var r = Duel(p, s, nyx, 0);
            if (r != null) events.UnionWith(r.Events);
            night.Add(r);
        }
        var last = night.Last();
        Check($"a night at Nyx's Table: four wins at {AlleyEconomy.DenStake:N0} a stake clear it ({last?.Summary}), raising alleyclear:Nyx's Table, a token, and a new night with the same authored seats",
            night.All(r => r != null && r.SeatWon && r.Ante?.Outcome == "win" && r.Ante.Returned == AlleyEconomy.DenStake) && last!.FirstClear && last.NightOver && last.Events.Contains("alleyclear:Nyx's Table")
            && p.Tokens - tokens0 >= AlleyEconomy.ClearTokens && p.AlleyTables["nyx"].Cleared && p.AlleyTables["nyx"].Night == 1
            && BackAlley.Lineup(p, data, nyx, ctx) is var again && Face(again[1]) == Face(seats[1]) && Face(again[3]) == Face(seats[3]));
        events.Add("open:Nyx's Table"); // the app raises it when the table's panel is first opened on the alley map

        // ---- The alley's brackets: entries, placements and their ledger kinds, Contraband Packs, the token, the records ----
        var packs = s.Packs;
        // One bracket of a kind played through the engine: you win until the round you lose in (-1: never; a Bo3 final lost 0-2).
        (Profile p, Bracket b, BracketResult? lost, BracketReward end) Play(string kind, int loseIn, uint salt, Profile? on = null)
        {
            var q = on ?? OrvelOpen(starter, cards, printings, library, salt);
            var b = Brackets.EnterAlley(q, data, kind, ctx, "fire-king-snake-eye")!;
            BracketResult? lost = null;
            while (b.YourMatch() >= 0)
            {
                Brackets.StartYours(b);
                var r = Brackets.RecordYours(q, library, b, b.RoundOf(b.YourMatch()) == loseIn ? 1 : 0, false)!;
                if (r.Out) lost = r;
            }
            Brackets.SkipToEnd(b);
            var end = Brackets.Finish(q, library, b, null, packs)!;
            events.UnionWith(end.Events);
            return (q, b, lost, end);
        }
        var placements = new List<string>();
        bool placeOk = true;
        foreach (var kind in new[] { Brackets.BrawlKind, Brackets.MidnightKind })
        {
            int rounds = kind == Brackets.BrawlKind ? 3 : 4;
            for (int loseIn = rounds - 1; loseIn >= -1; loseIn--)
            {
                var (q, b, lost, end) = Play(kind, loseIn, 0xA11E0C10 + (uint)(loseIn + 1) * 7 + (kind == Brackets.MidnightKind ? 100u : 0u));
                int place = loseIn < 0 ? 0 : rounds - loseIn;
                int pay = Brackets.PayFor(kind)[place], packsWon = Brackets.PacksFor(kind)[place];
                var copies = end.Packs.SelectMany(x => x).Where(x => x.Copy != null).Select(x => x.Copy!).ToList();
                bool ok = b.Placement() == place && end.Placement == place && (loseIn < 0 ? end.Dp == pay && lost == null : lost?.Reward.Dp == pay && end.Dp == 0)
                          && q.DpEarned.GetValueOrDefault(Ledger.Alley) == pay && q.DpSpent.GetValueOrDefault(Ledger.AlleyBrackets) == Brackets.EntryFor(kind)
                          && !q.DpEarned.ContainsKey(Ledger.Locals) && !q.DpSpent.ContainsKey(Ledger.Brackets) && Ledger.Net(q) == q.Dp - Profile.StartingDp
                          && end.PacksWon == packsWon && end.Packs.Count == packsWon && end.Packs.All(x => x.Count == ContrabandPacks.Size)
                          && copies.All(c => c.Source == CopySource.Prize && c.Paid == 0 && q.Collection.SellPrice(c) == 0 && SellDesk.Sell(q, c, none) == 0)
                          && end.Events.SequenceEqual(place == 0 ? new[] { "bracketwin:" + b.Name } : new string[0]) && end.Tokens == (place == 0 && kind == Brackets.MidnightKind ? AlleyEconomy.OpenTokens : 0)
                          && q.AlleyBrackets[kind].Entries == 1 && q.AlleyBrackets[kind].Titles == (place == 0 ? 1 : 0) && q.AlleyBrackets[kind].Best == place && q.Bracket == null
                          && q.Locals.Count == 0 && Brackets.TitlesTotal(q) == 0;
                placeOk &= ok;
                placements.Add($"{b.Name} {place}: {pay}{(packsWon > 0 ? $"+{packsWon}p" : "")}{(ok ? "" : " (FAIL)")}");
            }
        }
        Check($"Finish: every placement pays its kind's knob into \"{Ledger.Alley}\" (a loss at once, a title at the end) and the entry is \"{Ledger.AlleyBrackets}\", nothing into "
              + $"Locals' ledger; its Contraband Packs open as Prize copies, Paid 0, that sell for 0; only a title raises bracketwin:<name> ({string.Join(", ", placements)})",
            placeOk && placements.Count == 9);
        // The Midnight Open's token: the first title only. A plain "bracketwin" never comes from the alley, even as your first title of any kind.
        var (tp, _, _, firstTitle) = Play(Brackets.MidnightKind, -1, 0xA11E0C30);
        tp.Earn(Ledger.Debug, 10000);
        var (_, _, _, second) = Play(Brackets.MidnightKind, -1, 0, tp);
        var (bp, _, _, brawlTitle) = Play(Brackets.BrawlKind, -1, 0xA11E0C31);
        Check($"the Midnight Open's token: {firstTitle.Tokens} on the first title ({firstTitle.Summary}), {second.Tokens} on the second ({second.Summary}); seen key {Brackets.OpenTokenKey}; "
              + "an alley title raises only bracketwin:<name>, never the Locals' plain bracketwin",
            firstTitle.Tokens == AlleyEconomy.OpenTokens && second.Tokens == 0 && tp.Seen.Contains(Brackets.OpenTokenKey) && tp.AlleyBrackets["midnight"].Titles == 2
            && brawlTitle.Tokens == 0 && brawlTitle.Events.SequenceEqual(new[] { "bracketwin:Alley Brawl" }) && !firstTitle.Events.Contains(Brackets.WinEvent) && bp.Locals.Count == 0);
        // The records round-trip a save, and New Game+ keeps them; Baz's chalkboard says why you can't enter.
        var rt = RoundTrip(tp, printings, cards);
        var fresh = Opened(starter, cards, printings, library, 0xA11E0C32);
        var shutSays = Brackets.EnterBlocked(fresh, data, Brackets.MidnightKind);
        var brawlOpen = Brackets.EnterBlocked(fresh, data, Brackets.BrawlKind);
        fresh.AlleyTables["dumpster-row"] = new AlleyTableState { Cleared = true };
        fresh.Dp = AlleyEconomy.BrawlEntry - 1;
        var poorSays = Brackets.EnterBlocked(fresh, data, Brackets.BrawlKind);
        var running = OrvelOpen(starter, cards, printings, library, 0xA11E0C33);
        var runB = Brackets.EnterAlley(running, data, Brackets.BrawlKind, ctx)!;
        var runningSays = Brackets.EnterBlocked(running, data, Brackets.MidnightKind);
        var sameSays = Brackets.EnterBlocked(running, data, Brackets.BrawlKind);
        var lineSays = AlleyLines(SaveText(tp)).Where(l => l.StartsWith("alleybracket ")).ToList();
        rt.NewGamePlus();
        Check($"records: {string.Join(", ", lineSays)} round-trip and survive New Game+; entry blocked: \"{shutSays}\", \"{brawlOpen}\", \"{poorSays}\", \"{runningSays}\", \"{sameSays}\"",
            rt.AlleyBrackets["midnight"] is { Entries: 2, Titles: 2, Best: 0 } && lineSays.Count == 1 && rt.Seen.Contains(Brackets.OpenTokenKey)
            && shutSays == "Clear The Black Market to unlock" && brawlOpen == "Clear Dumpster Row to unlock" && poorSays == $"Entry is {AlleyEconomy.BrawlEntry:N0} DP; you have {fresh.Dp:N0}."
            && runningSays == "Alley Brawl running" && sameSays == "Your Alley Brawl bracket is running." && Brackets.EnterAlley(running, data, Brackets.MidnightKind, ctx) == null
            && running.Bracket == runB);
        // The first entries' story events (the caller raises the kind after EnterAlley).
        events.Add(Brackets.BrawlKind);
        events.Add(Brackets.MidnightKind);
    }
}
