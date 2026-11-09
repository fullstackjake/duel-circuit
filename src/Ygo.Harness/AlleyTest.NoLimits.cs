using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--campaign-test`'s No Limits checks (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 14.3, "AlleyTest checks (C3)", groups 1-9):
/// the list, the data (with its bites), the unlock, 1,000 seeded nights, binders and the restock, the ante, Unlisted pages, the
/// story, and the Brawl's out-of-towners. The table and the wave-1 builds ship `ready: no` until the ship gate, so these checks
/// force them ready in the test's own parsed copy of the data (<see cref="ReadyNoLimits"/>), never in the file.
/// <para>
/// `ALLEY_BITE=nl-pick|nl-power|nl-excluded|nl-lead` (test-only) feeds the data check a build that breaks one of section 5.1's
/// rules, to prove it bites.
/// </para>
/// </summary>
public static partial class AlleyTest
{
    /// <summary>The wave-1 pilots and the Brawl's four, by NO-LIMITS-STORY's ids.</summary>
    private static readonly string[] Wave1 = { "junia", "anselm", "tova", "augie", "sasha", "imogen", "rue" };
    private static readonly string[] OutOfTowners = { "junia", "augie", "rue", "sasha" };

    /// <summary>
    /// The alley's data with No Limits forced open for a test: the table and every wave-1 build whose deck file exists
    /// `ready: yes`, in a copy parsed from <see cref="Setup.EventsText"/> (the file is never edited), with its own context.
    /// <paramref name="mutate"/> edits the text first (a bite, a stand-in champion). <paramref name="allBuilds"/> forces every build ready
    /// whether its deck file exists or not (the Invitational's field wants all eleven; wave 2's decks aren't written yet), for the
    /// Invitational's checks and loops (<see cref="ReadyInvitational"/>).
    /// </summary>
    public static Setup ReadyNoLimits(Setup s, ICardDataProvider cards, Func<string, string>? mutate = null, bool anyWave = false, bool allBuilds = false)
    {
        var text = mutate != null ? mutate(s.EventsText) : s.EventsText;
        var probe = AlleyData.Parse(text, cards);
        var lines = text.Split('\n').ToList();
        string kind = "", id = "";
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].TrimEnd('\r').Trim();
            // A block runs to its blank line; a build's own `table:` line is a key, not a new block.
            if (t.Length == 0) { kind = ""; id = ""; }
            else if (kind == "" && t.StartsWith("table:")) { kind = "table"; id = t.Substring(6).Trim(); }
            else if (kind == "" && t.StartsWith("build:")) { kind = "build"; id = t.Substring(6).Trim(); }
            else if (kind == "" && (t.StartsWith("headliner:") || t.StartsWith("bracket:"))) { kind = "other"; id = ""; }
            else if (t.StartsWith("ready:"))
            {
                bool pick = (kind == "table" && probe.Tables.Any(x => x.Name == id && x.Tier == AlleyTier.NoLimits))
                            || (kind == "build" && probe.FindBuild(id) is { } b && (allBuilds || ((anyWave || b.Wave == 1) && b.Deck.Length > 0 && s.Ctx.LoadDeck(b.Deck) != null)));
                if (pick) lines[i] = "ready: yes";
            }
        }
        return Rebuild(s, string.Join("\n", lines), cards);
    }

    /// <summary>A setup over other events text: the same context (cards, ratings, decks), its own parsed data and pool cache.</summary>
    public static Setup Rebuild(Setup s, string text, ICardDataProvider cards)
    {
        var data = AlleyData.Parse(text, cards);
        var o = s.Ctx;
        var ctx = new AlleyContext
        {
            Cards = o.Cards, Printings = o.Printings, Lists = o.Lists, List = o.List, NoLimits = o.NoLimits, LoadDeck = o.LoadDeck, Ratings = o.Ratings,
            Curated = o.Curated, Roster = o.Roster, Library = o.Library, Data = data,
        };
        return new Setup { Data = data, Ctx = ctx, Traditional = s.Traditional, List = s.List, Lists = s.Lists, EventsText = text, Root = s.Root };
    }

    /// <summary>The text of a build's (or table's) block with one key's value changed (its comment dropped); unchanged when the block or key isn't there.</summary>
    public static string EditBlock(string text, string header, string key, Func<string, string> change)
    {
        var lines = text.Split('\n').ToList();
        int start = lines.FindIndex(l => l.TrimEnd('\r') == header);
        if (start < 0) return text;
        for (int i = start + 1; i < lines.Count; i++)
        {
            var t = lines[i].TrimEnd('\r');
            if (t.Length == 0 || t.StartsWith("build:") || t.StartsWith("headliner:") || t.StartsWith("bracket:")) break;
            if (!t.StartsWith(key + ":")) continue;
            var value = t.Substring(key.Length + 1);
            int hash = value.IndexOf('#');
            if (hash >= 0) value = value.Substring(0, hash);
            lines[i] = $"{key}: {change(value.Trim())}";
            break;
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Problems() lines about a Brawl deck (decks/no-limits/nl-*-brawl.ydk) that peak-ratings.txt hasn't rated yet are skipped here and
    /// printed, never hidden; every other line stays. A deck that is rated or absent from the field changes nothing.
    /// </summary>
    public static List<string> SkipUnratedBrawl(List<string> problems, AlleyData data, AlleyContext ctx)
    {
        var unrated = data.Brackets.SelectMany(b => b.Field.Select(AlleyBracketSpec.ParseField)).Where(f => f.after.Length > 0 && f.words.Length > 1)
            .Select(f => f.words[1]).Distinct().Where(x => ctx.LoadDeck(x) != null && !ctx.Ratings.ContainsKey(x)).ToList();
        var skipped = problems.Where(x => x.Contains("has no rating") && unrated.Any(x.Contains)).ToList();
        if (skipped.Count > 0)
            Console.WriteLine($"         skipped: {skipped.Count} Problems line(s) about Brawl decks peak-ratings.txt hasn't rated yet (ratings are parked): {string.Join("; ", skipped)}");
        return problems.Except(skipped).ToList();
    }

    /// <summary>A save with No Limits open: the alley's four earlier tables cleared, the wall opened (alley-closing seen, nolimits raised), DP to stake.</summary>
    public static Profile NoLimitsOpen(Deck starter, ICardDataProvider cards, PrintingIndex printings, Library library, uint salt, int dp = 200000)
    {
        var p = Opened(starter, cards, printings, library, salt);
        foreach (var id in new[] { "dumpster-row", "basement", "black-market", "nyx" }) p.AlleyTables[id] = new AlleyTableState { Cleared = true };
        p.Seen.Add(BackAlley.ClosingScene);
        p.Seen.Add(BackAlley.WallEvent);
        p.Earn(Ledger.Debug, dp);
        return p;
    }

    /// <summary>The night's lineup at No Limits for night <paramref name="night"/> (a profile whose state is moved there; the previous night's saved seats are redrawn).</summary>
    public static IReadOnlyList<AlleyOpponent> NightAt(Profile p, Setup nl, int table, int night)
    {
        p.AlleyTables[nl.Data.Tables[table].Id] = new AlleyTableState { Night = night, Cleared = p.AlleyTables.TryGetValue(nl.Data.Tables[table].Id, out var st) && st.Cleared };
        return BackAlley.Lineup(p, nl.Data, table, nl.Ctx);
    }

    /// <summary>`--nolimits-test`: only the No Limits checks of <see cref="Run"/> (they also run inside `--campaign-test`), for quick iterations.</summary>
    public static int RunNoLimits(ICardDataProvider cards, string root)
    {
        int failures = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        var printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities")).ToList();
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var library = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), roster, circuits);
        var starter = Deck.LoadYdk(Path.Combine(root, "decks", "classic.ydk"));
        starter.Name = "Starter";
        var s = Load(cards, root, printings, roster, library);
        NoLimitsChecks(s, starter, cards, printings, library, roster, root, Check, new HashSet<string>());
        Console.WriteLine(failures == 0 ? "\nAll No Limits checks passed." : $"\n{failures} No Limits check(s) FAILED.");
        return failures == 0 ? 0 : 1;
    }

    private static void NoLimitsChecks(Setup s, Deck starter, ICardDataProvider cards, PrintingIndex printings, Library library, List<Personality> roster,
        string root, Action<string, bool> check, HashSet<string> events)
    {
        int added = 0;
        void Check(string what, bool ok) { added++; check(what, ok); }
        Console.WriteLine("\nNo Limits (NO-LIMITS-SPEC section 14.3, AlleyTest checks C3, groups 1-9):");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var real = s.Data;
        var nl = ReadyNoLimits(s, cards);
        var d = nl.Data;
        var ctx = nl.Ctx;
        int nlt = d.IndexOf("no-limits"), dumpster = d.IndexOf("dumpster-row"), basement = d.IndexOf("basement"), nyx = d.IndexOf("nyx");
        int none(uint _) => 0;
        string Name(uint c) => cards.Get(c)?.Name ?? c.ToString();
        uint Code(string name) => Alley.CodeOf(name, cards);
        var table = d.Tables[nlt];
        var champions = table.Headliners;
        var readyBuilds = d.Builds.Where(b => b.Ready && ctx.LoadDeck(b.Deck) != null).ToList();
        var readyPool = readyBuilds.Where(b => table.Pool.Contains(b.Id) && !champions.Contains(b.Id)).ToList();
        Check($"No Limits setup: the real file's table is ready: no (the ship gate), and the test's own copy forces it ready with {readyBuilds.Count} wave-1 builds "
              + $"({string.Join(", ", readyBuilds.Select(b => b.Id))}); {readyPool.Count} are pool builds",
            nlt >= 0 && !real.Tables[nlt].Ready && d.Tables[nlt].Ready && table.Tier == AlleyTier.NoLimits && readyBuilds.Count == Wave1.Length && Wave1.All(id => readyBuilds.Any(b => b.Id == id))
            && readyPool.Count == 6 && real.Builds.Count() == 11 && real.Builds.Where(b => b.Wave == 1).All(b => b.Ready == false));

        // ---- NL 1. The list (the rest of the group is in "the No Limits list" above) ----
        var excludedCodes = Alley.Excluded.Select(Code).ToHashSet();
        var rulesNl = BackAlley.RulesFor(d, nlt, ctx);
        var rulesRow = BackAlley.RulesFor(d, dumpster, ctx);
        uint pot = Code("Pot of Greed"), djinn = Code("Djinn Releaser of Rituals"), protos = Code("Archnemeses Protos");
        Deck Pots(int n, params uint[] extra)
        {
            var deck = starter.Clone();
            deck.Side.Clear();
            deck.Main.RemoveAll(c => Banlist.BaseCode(c, cards) == pot || extra.Contains(Banlist.BaseCode(c, cards)));
            foreach (var c in extra) deck.Main.Add(c);
            for (int i = 0; i < n; i++) deck.Main.Add(pot);
            return deck;
        }
        var potsNl = DeckRules.Validate(Pots(3), rulesNl.List!, cards);
        var potsRow = DeckRules.Validate(Pots(3), rulesRow.List!, cards);
        var djinnNl = DeckRules.Validate(Pots(0, djinn), rulesNl.List!, cards);
        var djinnRow = DeckRules.Validate(Pots(0, djinn), rulesRow.List!, cards);
        var protosNl = DeckRules.Validate(Pots(0, protos), rulesNl.List!, cards);
        var protosRow = DeckRules.Validate(Pots(0, protos), rulesRow.List!, cards);
        Check("1. the list through RulesFor: 3 Pot of Greed are legal at the No Limits table and illegal at Dumpster Row; Djinn Releaser of Rituals and Archnemeses Protos are illegal at both; "
              + $"the No Limits list is exactly the {Alley.Excluded.Length} exclusions at 0 (Never and BossOnly disjoint)",
            potsNl.Count == 0 && potsRow.Any(p => p.Code == pot) && djinnNl.Any(p => p.Code == djinn) && djinnRow.Any(p => p.Code == djinn) && protosNl.Any(p => p.Code == protos)
            && protosRow.Any(p => p.Code == protos) && rulesNl.List!.Name == "No Limits" && rulesRow.List!.Name != "No Limits" && rulesNl.List.Limits.Count == 35
            && rulesNl.List.Limits.Keys.All(excludedCodes.Contains) && !Alley.Never.Intersect(Alley.BossOnly).Any());

        // ---- NL 2. Data: Problems() is empty, and every section 5.1 rule bites ----
        string Mutate(string bite, string text) => bite switch
        {
            "nl-pick" => EditBlock(text, "build: anselm", "over", v => v + ", Union Carrier, Card of Safe Return, Soul Charge"),
            "nl-power" => EditBlock(text, "build: anselm", "over", v => v + ", Heavy Storm, Dark Hole"),
            "nl-excluded" => EditBlock(text, "build: anselm", "over", v => v + ", Wind-Up Hunter"),
            "nl-lead" => EditBlock(text, "build: imogen", "stakes", v => "Branded Expulsion, " + v),
            _ => text,
        };
        List<string> ProblemsOf(string text, bool authors = true) => AlleyData.Parse(text, cards).Problems(ctx, authors);
        var bite = Bite.StartsWith("nl-") ? Bite : "";
        var dataProblems = SkipUnratedBrawl(ProblemsOf(bite.Length > 0 ? Mutate(bite, s.EventsText) : s.EventsText), d, ctx);
        foreach (var line in dataProblems) Console.WriteLine($"         problem: {line}");
        var seats = table.Seats.Select(x => x.Kind).ToList();
        Check($"2. data: the No Limits table parses (opens {table.Opens}, gate {table.Gate}, elo {table.MinElo:0}-{table.MaxElo:0}, pool {table.Pool.Count}, champions {string.Join("/", champions)}, "
              + $"seats {string.Join(",", seats)}) and {real.Builds.Count()} build: blocks ({real.Builds.Count(b => b.Wave == 1)} wave 1); Problems() is empty on every build whose deck exists, "
              + $"ready or not{(dataProblems.Count > 0 ? ": " + string.Join("; ", dataProblems.Take(4)) : "")}",
            table.Opens == "Nyx's Table" && table.Gate == "nolimits" && table.MinElo == 1840 && table.MaxElo == 1990 && table.Pool.Count == 9 && champions.SequenceEqual(new[] { "remy", "junia" })
            && seats.SequenceEqual(new[] { "pool", "pool", "pool", "champion" }) && real.Builds.Count() == 11 && dataProblems.Count == 0);
        // The bites: each mutated build trips its own rule, and only through AlleyData.Problems.
        var bites = new (string name, string what, string expect)[]
        {
            ("nl-pick", "rule 5: two builds sharing 3 picks", "share 3 picks"), ("nl-power", "rule 3: two power cards", "power cards"),
            ("nl-excluded", "rule 4: an excluded card", "an excluded card"), ("nl-lead", "rule 7: a lead that fits no printing", "fits no printing"),
        };
        var caught = bites.Select(b => (b.name, b.what, lines: ProblemsOf(Mutate(b.name, s.EventsText)).Where(x => x.Contains(b.expect)).ToList())).ToList();
        Check($"2. data bites: {string.Join("; ", caught.Select(c => $"{c.name} ({c.what}) -> {(c.lines.Count > 0 ? "\"" + c.lines[0] + "\"" : "NOT CAUGHT")}"))}",
            caught.All(c => c.lines.Count > 0) && ProblemsOf(s.EventsText).Count(x => bites.Any(b => x.Contains(b.expect))) == 0);
        // The ready-only rules: a ready build rated outside the window or over its base +250 is a problem; one that isn't ready skips them.
        var ratedLow = AlleyData.Parse(nl.EventsText, cards).Problems(new AlleyContext
        {
            Cards = cards, Printings = printings, Lists = ctx.Lists, List = ctx.List, LoadDeck = ctx.LoadDeck, Curated = ctx.Curated, Roster = roster, Library = library,
            Ratings = ctx.Ratings.ToDictionary(r => r.Key, r => r.Key == "nl-snake-eye" ? 1500.0 : r.Value), Data = d,
        });
        Check($"2. data: a ready build rated outside the table's window is a problem (nl-snake-eye at 1500: \"{ratedLow.FirstOrDefault(x => x.Contains("outside"))}\")",
            ratedLow.Any(x => x.Contains("build junia") && x.Contains("outside")));

        // ---- NL 3. Unlock ----
        {
            var p = Opened(starter, cards, printings, library, 0xA11E3001);
            foreach (var id in new[] { "dumpster-row", "basement", "black-market" }) p.AlleyTables[id] = new AlleyTableState { Cleared = true };
            bool Locked() => !BackAlley.IsUnlocked(p, d, nlt);
            bool beforeNyx = Locked() && !BackAlley.WallDue(p, d);
            p.AlleyTables["nyx"] = new AlleyTableState { Cleared = true };
            bool noClosing = Locked() && !BackAlley.WallDue(p, d);
            p.Seen.Add(BackAlley.ClosingScene);
            bool dueNotOpen = BackAlley.WallDue(p, d) && Locked();
            // The caller (CampaignScreen on the tab show, and at load) raises it: WallDue is true once, then the table is open.
            int raised = 0;
            for (int k = 0; k < 5; k++)
                if (BackAlley.WallDue(p, d)) { raised++; p.Seen.Add(BackAlley.WallEvent); events.Add(BackAlley.WallEvent); }
            bool open = raised == 1 && BackAlley.IsUnlocked(p, d, nlt) && !BackAlley.WallDue(p, d) && BackAlley.NextSeat(p, d, nlt) == 1;
            // An old save past both opens it at load; one with Nyx not cleared (or the scene unseen) stays shut.
            var old = Opened(starter, cards, printings, library, 0xA11E3002);
            foreach (var id in new[] { "dumpster-row", "basement", "black-market", "nyx" }) old.AlleyTables[id] = new AlleyTableState { Cleared = true };
            old.Seen.Add(BackAlley.ClosingScene);
            var loaded = RoundTrip(old, printings, cards);
            bool atLoad = BackAlley.WallDue(loaded, d) && !BackAlley.IsUnlocked(loaded, d, nlt);
            loaded.Seen.Add(BackAlley.WallEvent);
            var reloaded = RoundTrip(loaded, printings, cards);
            bool keeps = BackAlley.IsUnlocked(reloaded, d, nlt) && !BackAlley.WallDue(reloaded, d) && reloaded.Seen.Contains(BackAlley.WallEvent);
            bool realShut = !BackAlley.IsUnlocked(reloaded, real, nlt) && !BackAlley.IsUnlocked(p, real, nlt);
            // A table that opens from Nyx's Table needs the wall's key (its gate) even with Nyx cleared and the closing scene seen.
            var gateOnly = Opened(starter, cards, printings, library, 0xA11E3004);
            foreach (var id in new[] { "dumpster-row", "basement", "black-market", "nyx" }) gateOnly.AlleyTables[id] = new AlleyTableState { Cleared = true };
            gateOnly.Seen.Add(BackAlley.ClosingScene);
            Check($"3. unlock: locked before Nyx's Table is cleared and before alley-closing is seen; WallDue is true exactly once ({raised}), then the table opens at seat 1; "
                  + "an old save past both has WallDue true at load and keeps the table once it's raised; the gate key is what opens it (not Nyx's clear alone); "
                  + "the real file's ready: no table stays shut whatever the profile holds",
                beforeNyx && noClosing && dueNotOpen && open && atLoad && keeps && !BackAlley.IsUnlocked(gateOnly, d, nlt) && realShut);
        }

        // ---- NL 4. Draws: 1,000 seeded nights ----
        {
            const int Nights = 1000;
            var pn = NoLimitsOpen(starter, cards, printings, library, 0x4E110001);
            int n = readyPool.Count;
            int famViolations = 0, sameBuild = 0, wrongChair = 0, short_ = 0, maxGap = 0;
            var sits = readyPool.ToDictionary(b => b.Id, _ => 0);
            var last = readyPool.ToDictionary(b => b.Id, _ => -1);
            var faces = new System.Text.StringBuilder();
            for (int k = 0; k < Nights; k++)
            {
                var lineup = NightAt(pn, nl, nlt, k);
                if (lineup.Count != 4) { short_++; continue; }
                var pool = lineup.Where(o => o.Seat <= 3).ToList();
                if (pool.Select(o => o.Character).Distinct().Count() != 3 || pool.Any(o => o.Kind != BackAlley.BuildKind || !o.IsBuild)) sameBuild++;
                for (int a = 0; a < 3; a++)
                    for (int b = a + 1; b < 3; b++)
                        if (d.FindBuild(pool[a].Character)!.Family.Intersect(d.FindBuild(pool[b].Character)!.Family).Any()) famViolations++;
                if (lineup[3].Character != "junia" || lineup[3].Kind != "headliner" || lineup[3].Seat != 4 || !lineup[3].IsBuild) wrongChair++;
                foreach (var o in pool)
                {
                    if (!sits.ContainsKey(o.Character)) { sameBuild++; continue; }
                    sits[o.Character]++;
                    maxGap = Math.Max(maxGap, k - last[o.Character]);
                    last[o.Character] = k;
                }
                faces.Append(string.Join("|", lineup.Select(Face))).Append('\n');
            }
            int bound = (int)Math.Ceiling(n / 3.0) + 1;
            Check($"4. draws ({Nights} seeded nights): seats 1-3 are three different builds every night ({sameBuild} breaks) with no shared family among them ({famViolations} pairs); "
                  + $"every ready pool build ({n}) sits {sits.Values.Min()}-{sits.Values.Max()} times, never more than {maxGap} nights apart (bound ⌈{n}/3⌉+1 = {bound}); nights short of 4 seats: {short_}",
                short_ == 0 && sameBuild == 0 && famViolations == 0 && maxGap <= bound && sits.Values.All(v => v > 0) && last.Values.All(v => Nights - 1 - v <= bound));
            Check($"4. draws: Junia closes seat 4 as a headliner every night until beaten ({wrongChair} nights otherwise); Remy (ready: no) never sits", wrongChair == 0
                  && !faces.ToString().Contains("remy"));
            // Beaten: the encore opens, and seat 4 is a fourth pool build.
            bool encoreShut = !BackAlley.EncoreOpen(pn, d, nlt);
            pn.Headliners["junia"] = new HeadlinerState { Beaten = 1, Met = 1 };
            int fourth = 0, chairWrong = 0;
            for (int k = 0; k < 200; k++)
            {
                var lineup = NightAt(pn, nl, nlt, k);
                if (lineup.Count != 4) { chairWrong++; continue; }
                if (lineup[3].Kind != BackAlley.BuildKind || lineup[3].Character == "junia" || lineup.Take(3).Any(o => o.Character == lineup[3].Character)) chairWrong++;
                else fourth++;
            }
            var encore = BackAlley.Encore(pn, d, nlt, "junia", ctx);
            Check($"4. draws: once Junia is the only ready champion and she's beaten, the Encore opens (it was shut: {encoreShut}) and seat 4 draws a fourth pool build ({fourth} of 200 nights, {chairWrong} wrong); "
                  + $"the encore is a one-seat night from her own binder ({encore?.Binder}, seat {encore?.Seat})",
                encoreShut && BackAlley.EncoreOpen(pn, d, nlt) && chairWrong == 0 && encore is { Kind: "encore", Seat: 0, Binder: "nl:junia" });
            // Determinism: the same salt draws the same 1,000 nights; another salt another order; a loss changes nothing; save and load are byte-identical.
            var twin = NoLimitsOpen(starter, cards, printings, library, 0x4E110001);
            var faces2 = new System.Text.StringBuilder();
            for (int k = 0; k < Nights; k++) faces2.Append(string.Join("|", NightAt(twin, nl, nlt, k).Select(Face))).Append('\n');
            var other = NoLimitsOpen(starter, cards, printings, library, 0x4E110002);
            var otherFaces = Enumerable.Range(0, 30).Select(k => string.Join("|", NightAt(other, nl, nlt, k).Select(Face))).ToList();
            var firstFaces = faces.ToString().Split('\n').Take(30).ToList();
            bool deterministic = faces.ToString() == faces2.ToString();
            bool differs = otherFaces.Zip(firstFaces).Any(x => x.First != x.Second);
            var lp = NoLimitsOpen(starter, cards, printings, library, 0x4E110003);
            var night0 = BackAlley.Lineup(lp, d, nlt, ctx).Select(Face).ToList();
            int dpLoss0 = lp.Dp;
            var loss = Duel(lp, nl, nlt, 1, 12, 0);
            var afterLoss = BackAlley.Lineup(lp, d, nlt, ctx).Select(Face).ToList();
            var draw = Duel(lp, nl, nlt, 2, AlleyEconomy.AlleyTurns + 1, 0);
            var afterDraw = BackAlley.Lineup(lp, d, nlt, ctx).Select(Face).ToList();
            bool lossKeeps = loss != null && !loss.SeatWon && night0.SequenceEqual(afterLoss) && afterDraw.SequenceEqual(night0) && draw?.Replay == true
                             && lp.AlleyTables[table.Id].Night == 0 && lp.AlleyTables[table.Id].WonMask == 0 && BackAlley.NextSeat(lp, d, nlt) == 1 && lp.Dp == dpLoss0 - AlleyEconomy.NoLimitsStake;
            Duel(lp, nl, nlt, 0);
            Duel(lp, nl, nlt, 1);
            var text = SaveText(lp);
            var back = FromText(text, printings, cards);
            bool identical = SaveText(back) == text;
            bool sameNight = BackAlley.Lineup(back, d, nlt, ctx).Select(Face).SequenceEqual(BackAlley.Lineup(lp, d, nlt, ctx).Select(Face));
            Check($"4. draws: deterministic (the same salt draws the same {Nights} nights, another salt another order: {differs}); a loss and a draw change nothing (same four faces, night 0, no seat won, -{AlleyEconomy.NoLimitsStake} DP for the loss); "
                  + "save and load are byte-identical mid-night and draw the same lineup",
                deterministic && differs && lossKeeps && identical && sameNight);
            // The wildcard, with a stand-in second champion (Remy's deck is wave 2): the other unbeaten champion takes a pool seat 1/3 of the nights.
            var wild = ReadyNoLimits(s, cards, t => t.Replace("champions: remy, junia", "champions: junia, rue"));
            int wt = wild.Data.IndexOf("no-limits"), wildNights = 0, total = 0, closerWrong = 0;
            var seatHits = new int[4];
            for (uint salt = 0; salt < 6; salt++)
            {
                var wp = NoLimitsOpen(starter, cards, printings, library, 0x4E110100 + salt);
                for (int k = 0; k < 1000; k++, total++)
                {
                    var lineup = NightAt(wp, wild, wt, k);
                    if (lineup[3].Character != "junia") closerWrong++;
                    var w = lineup.FirstOrDefault(o => o.Kind == "wildcard");
                    if (w != null) { wildNights++; seatHits[w.Seat]++; if (w.Character != "rue" || w.Seat > 3) closerWrong++; }
                }
            }
            var wp2 = NoLimitsOpen(starter, cards, printings, library, 0x4E110200);
            wp2.Headliners["junia"] = new HeadlinerState { Beaten = 1 };
            var swapped = NightAt(wp2, wild, wt, 0);
            wp2.Headliners["rue"] = new HeadlinerState { Beaten = 1 };
            var bothBeaten = NightAt(wp2, wild, wt, 1);
            double wildRate = 100.0 * wildNights / total;
            Check($"4. draws (the wildcard, with Rue as a stand-in second champion): the first unbeaten champion closes ({closerWrong} breaks), the other takes one of seats 1-3 in {wildRate:0.0}% of {total} nights (target 33% ± 2; seats {seatHits[1]}/{seatHits[2]}/{seatHits[3]}); "
                  + $"with Junia beaten Rue closes ({swapped[3].Character}); with both beaten seat 4 is a pool build ({bothBeaten[3].Kind}) and the Encore opens ({BackAlley.EncoreOpen(wp2, wild.Data, wt)})",
                closerWrong == 0 && Math.Abs(wildRate - 100.0 / 3) <= 2 && seatHits.Skip(1).All(h => h > wildNights / 3 * 0.8) && swapped[3].Character == "rue" && swapped.All(o => o.Kind != "wildcard")
                && bothBeaten[3].Kind == BackAlley.BuildKind && BackAlley.EncoreOpen(wp2, wild.Data, wt));
            Console.WriteLine("         skipped (NL-2): the Remy wildcard check (Remy closes and Junia wildcards in 33% ± 2): Remy's deck nl-kewl-tune is wave 2, so he never sits in NL-1; the stand-in above exercises the same code");
        }

        // ---- NL 5. Binders and the restock ----
        {
            var pb = NoLimitsOpen(starter, cards, printings, library, 0x4E110010);
            var bad = new List<string>();
            var conditions = AlleyEconomy.NoLimitsGrades.Select(g => (g.grade, g.slab)).ToHashSet();
            int floor = (int)Math.Ceiling(AlleyEconomy.StakeFloor * AlleyEconomy.NoLimitsStake);
            var forbidden = new HashSet<uint>(ctx.Pool(9999));
            var leadLines = new List<string>();
            foreach (var b in readyBuilds)
            {
                var binder = Ante.Binder(pb, "nl:" + b.Id, ctx);
                var deckCodes = ctx.LoadDeck(b.Deck)!.Main.Concat(ctx.LoadDeck(b.Deck)!.Extra).Select(c => Banlist.BaseCode(c, cards)).ToHashSet();
                if (binder.Cards.Count != AlleyEconomy.BinderSize) bad.Add($"{b.Id}: {binder.Cards.Count} cards");
                if (binder.Cards.Select(c => c.Code).Distinct().Count() != binder.Cards.Count) bad.Add($"{b.Id}: a card twice");
                if (binder.Cards.Count == 0 || binder.Cards[0].Code != b.Stakes[0]) bad.Add($"{b.Id}: leads with {(binder.Cards.Count > 0 ? Name(binder.Cards[0].Code) : "nothing")}, not {Name(b.Stakes[0])}");
                int leads = b.Stakes.Count(c => Ante.CanStake(c, AlleyTier.NoLimits, ctx));
                if (!binder.Cards.Take(leads).Select(c => c.Code).SequenceEqual(b.Stakes.Where(c => Ante.CanStake(c, AlleyTier.NoLimits, ctx)))) bad.Add($"{b.Id}: its stakes: don't lead in order");
                foreach (var st in binder.Cards)
                {
                    int v = Ante.Value(st, ctx);
                    if (!deckCodes.Contains(st.Code)) bad.Add($"{b.Id}: {Name(st.Code)} isn't in its deck");
                    if (v < floor || v > AlleyEconomy.NoLimitsCap) bad.Add($"{b.Id}: {Name(st.Code)} worth {v:N0}");
                    if (!conditions.Contains((st.Slab > 0 ? st.Slab : st.Grade, st.Slab > 0))) bad.Add($"{b.Id}: {Name(st.Code)} grade {st.Grade} slab {st.Slab}");
                }
                leadLines.Add($"{b.Handle} {string.Join("/", binder.Cards.Take(leads).Select(c => $"{Name(c.Code)} {Ante.Value(c, ctx):N0}"))}");
            }
            Check($"5. binders: each of the {readyBuilds.Count} ready builds' binders hold {AlleyEconomy.BinderSize} different cards of its deck, lead with its first stakes: card ({string.Join("; ", leadLines)}), "
                  + $"and every stake is worth {floor:N0}-{AlleyEconomy.NoLimitsCap:N0} at NoLimitsGrades (Raw 8-9, slabs 9-10){(bad.Count > 0 ? ": " + string.Join("; ", bad.Take(4)) : "")}",
                bad.Count == 0 && readyBuilds.Count == 7);
            // Contraband weight: the first free draw (after the leads) is one weighted pick; its expected chance of being ever-Forbidden at 3×.
            double exp3 = 0, exp1 = 0, var3 = 0;
            int observed = 0, samples = 0;
            var notCandidate = new List<string>();
            foreach (var b in readyBuilds)
            {
                var deck = ctx.LoadDeck(b.Deck)!;
                var leadCodes = b.Stakes.Where(c => Ante.CanStake(c, AlleyTier.NoLimits, ctx)).ToList();
                double wc = 0, wn = 0;
                var candidates = new HashSet<(uint, Rarity, int, int)>();
                foreach (var code in deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, cards)).Distinct().Where(c => !leadCodes.Contains(c)))
                {
                    var alias = cards.Get(code)?.Alias ?? 0;
                    var commonest = printings.Commonest(code, alias);
                    foreach (var r in printings.Of(code, alias).Select(x => x.Rarity).Distinct())
                        foreach (var (grade, slab, w) in AlleyEconomy.NoLimitsGrades)
                        {
                            int v = Pricing.V(new CardCopy { Code = code, Rarity = r, Grade = grade, Slab = slab ? grade : 0 }, commonest);
                            if (v < floor || v > AlleyEconomy.NoLimitsCap) continue;
                            double weight = w * 10000.0 / Pricing.Unit(r);
                            if (forbidden.Contains(code)) wc += weight; else wn += weight;
                            candidates.Add((code, r, grade, slab ? grade : 0));
                        }
                }
                if (wc + wn <= 0) continue;
                double p3 = AlleyEconomy.ContrabandWeight * wc / (AlleyEconomy.ContrabandWeight * wc + wn), p1 = wc / (wc + wn);
                for (uint salt = 0; salt < 120; salt++)
                {
                    var q = new Profile();
                    q.Collection.Salt = Hash.Mix(0xB1D4u, salt);
                    var first = Ante.Binder(q, "nl:" + b.Id, ctx).Cards[leadCodes.Count];
                    if (!candidates.Contains((first.Code, first.Rarity, first.Grade, first.Slab))) notCandidate.Add($"{Name(first.Code)} {first.Rarity} {first.Grade}/{first.Slab}");
                    if (forbidden.Contains(first.Code)) observed++;
                    exp3 += p3; exp1 += p1; var3 += p3 * (1 - p3);
                    samples++;
                }
            }
            double sigma = Math.Sqrt(var3);
            Check($"5. binders: ever-Forbidden cards are drawn at {AlleyEconomy.ContrabandWeight}× their share: of {samples} first free stakes {observed} were, expected {exp3:0} at {AlleyEconomy.ContrabandWeight}× (± {sigma:0}; {exp1:0} if unweighted); "
                  + $"every stake is one of the tier's candidates{(notCandidate.Count > 0 ? " (not: " + string.Join(", ", notCandidate.Take(3)) + ")" : "")}",
                Math.Abs(observed - exp3) <= 4 * sigma && Math.Abs(observed - exp1) > 4 * sigma && notCandidate.Count == 0);

            // The restock: a won lead goes back on the next night its pilot sits, only then, once.
            var rp = NoLimitsOpen(starter, cards, printings, library, 0x4E110020);
            var first0 = BackAlley.Lineup(rp, d, nlt, ctx);
            var target = first0[0];
            var tb = d.FindBuild(target.Character)!;
            uint lead = tb.Stakes[0];
            var won = Duel(rp, nl, nlt, 0, 9, 0);
            int Copies(string owner) => Ante.Binder(rp, owner, ctx).Cards.Count(c => c.Code == lead);
            string Print(string owner) => string.Join(",", Ante.Binder(rp, owner, ctx).Cards.Select(c => $"{c.Code}/{c.Rarity}/{c.Grade}/{c.Slab}/{c.Seed}")) + "#" + Ante.Binder(rp, owner, ctx).Serial;
            bool gone = won?.SeatWon == true && won.Ante?.Won?.Code == lead && Copies(target.Binder) == 0;
            // The nights move on by the state (no more duels: a binder's lazy refill, one card per 3 alley duels, stays out of it).
            int nightsAway = 0, restoredOn = -1, serialBefore = Ante.Binder(rp, target.Binder, ctx).Serial, stayedGone = 0, otherChanged = 0, dupes = 0, leadsFront = 0;
            var othersBefore = readyBuilds.Where(b => b.Id != target.Character).ToDictionary(b => b.Id, b => Print("nl:" + b.Id));
            for (int k = 1; k <= 8 && restoredOn < 0; k++)
            {
                var night = NightAt(rp, nl, nlt, k);
                if (night.Any(o => o.Character == target.Character)) { restoredOn = k; break; }
                nightsAway++;
                if (Copies(target.Binder) == 0 && Ante.Binder(rp, target.Binder, ctx).Serial == serialBefore) stayedGone++;
                foreach (var b in readyBuilds.Where(b => b.Id != target.Character && night.All(o => o.Character != b.Id)))
                    if (othersBefore[b.Id] != Print("nl:" + b.Id)) otherChanged++;
                foreach (var b in readyBuilds.Where(b => b.Id != target.Character)) othersBefore[b.Id] = Print("nl:" + b.Id);
            }
            var tbinder = Ante.Binder(rp, target.Binder, ctx);
            bool back = restoredOn > 0 && Copies(target.Binder) == 1 && tbinder.Serial == serialBefore + 1 && tbinder.Cards.Count <= AlleyEconomy.BinderSize;
            leadsFront = tbinder.Cards.TakeWhile(c => tb.Stakes.Contains(c.Code)).Count();
            // Seen again the same night, and on later nights it sits (unplayed): still one copy, no more draws.
            int serialAfter = tbinder.Serial;
            BackAlley.Lineup(rp, d, nlt, ctx);
            bool onceThisNight = Copies(target.Binder) == 1 && Ante.Binder(rp, target.Binder, ctx).Serial == serialAfter;
            for (int k = Math.Max(restoredOn, 1) + 1; k <= Math.Max(restoredOn, 1) + 8; k++)
            {
                NightAt(rp, nl, nlt, k);
                if (Copies(target.Binder) != 1) dupes++;
            }
            Check($"5. restock: {target.Handle}'s won lead ({Name(lead)}) leaves its binder; it stays out on the {nightsAway} night(s) the pilot doesn't sit ({stayedGone} checked, {otherChanged} other binders changed), "
                  + $"comes back on night {restoredOn} when it sits (one copy, one new draw, {leadsFront} lead(s) at the front), and the next 8 nights find one copy ({dupes} with more or fewer)",
                gone && stayedGone == nightsAway && otherChanged == 0 && back && onceThisNight && dupes == 0 && leadsFront == tb.Stakes.Count(c => Ante.CanStake(c, AlleyTier.NoLimits, ctx)));
            // An encore is not a night: no restock.
            var ep = NoLimitsOpen(starter, cards, printings, library, 0x4E110021);
            ep.Headliners["junia"] = new HeadlinerState { Beaten = 1, Met = 1 };
            var enc = BackAlley.Encore(ep, d, nlt, "junia", ctx)!;
            var jb = Ante.Binder(ep, "nl:junia", ctx);
            var spoils = jb.Cards.First();
            jb.Cards.RemoveAt(0);
            int jSerial = jb.Serial;
            var encAgain = BackAlley.Encore(ep, d, nlt, "junia", ctx);
            bool noRestock = !Ante.Binder(ep, "nl:junia", ctx).Cards.Any(c => c.Code == spoils.Code) && Ante.Binder(ep, "nl:junia", ctx).Serial == jSerial && encAgain != null;
            var restocked = Ante.Restock(ep, "nl:junia", ctx);
            Check($"5. restock: an encore is not a night (the encore asks leave Junia's binder as it is); Ante.Restock itself puts {Name(spoils.Code)} back once ({restocked.Cards.Count(c => c.Code == spoils.Code)} copy) and a second call adds nothing "
                  + $"({Ante.Restock(ep, "nl:junia", ctx).Cards.Count(c => c.Code == spoils.Code)}); any other owner's binder is unchanged by it",
                enc.Kind == "encore" && noRestock && restocked.Cards.Count(c => c.Code == spoils.Code) == 1 && Ante.Restock(ep, "nl:junia", ctx).Cards.Count(c => c.Code == spoils.Code) == 1
                && Ante.Restock(ep, "hl:yata-mags", ctx) is var yata && yata.Cards.Count(c => c.Code == spoils.Code) == 0);
        }

        // ---- NL 6. Ante ----
        {
            var pa = NoLimitsOpen(starter, cards, printings, library, 0x4E110030);
            bool Balanced() => Ledger.Net(pa) == pa.Dp + (pa.AnteLive?.Held ?? 0) - Profile.StartingDp;
            var lineup = BackAlley.Lineup(pa, d, nlt, ctx);
            var o = lineup[0];
            var stake = Ante.Next(pa, o.Binder, ctx)!;
            int dp0 = pa.Dp, spent0 = pa.DpSpent.Values.Sum(), earned0 = pa.DpEarned.Values.Sum();
            var live = Ante.Hold(pa, d, nlt, o, null, none, ctx);
            bool held = live != null && live.Held == AlleyEconomy.NoLimitsStake && pa.Dp == dp0 - AlleyEconomy.NoLimitsStake && AlleyEconomy.NoLimitsStake == 2500
                        && pa.DpSpent.Values.Sum() == spent0 && pa.DpEarned.Values.Sum() == earned0 && Same(live.Theirs, stake) && live.Yours == null && Balanced()
                        && Ante.StakeFor(AlleyTier.NoLimits) == 2500 && Ante.Value(stake, ctx) >= 2500 && Ante.Value(stake, ctx) <= AlleyEconomy.NoLimitsCap;
            var seatsWon = new List<AlleyResult>();
            var win = BackAlley.Record(pa, d, nlt, 0, 12, 0, ctx);
            events.UnionWith(win.Events);
            seatsWon.Add(win);
            var wonCopy = win.Ante?.Won;
            // A win: the stake back, their lead a Paid-0 Ante copy the Sell desk refuses.
            bool winOk = win.Ante?.Outcome == "win" && wonCopy != null && pa.Dp == dp0 && wonCopy.Paid == 0 && wonCopy.Source == CopySource.Ante && wonCopy.Code == stake.Code
                         && SellDesk.Blocked(pa, wonCopy, none) == SellDesk.AnteRefusal && SellDesk.Sell(pa, wonCopy, none) == 0 && Balanced() && pa.DpSpent.Values.Sum() == spent0;
            Check($"6. ante: Hold moves {AlleyEconomy.NoLimitsStake:N0} DP into AnteLive with no ledger line and takes their card ({Name(stake.Code)}, worth {Ante.Value(stake, ctx):N0}); a win returns it, "
                  + $"and the lead is a Paid-0 Ante copy the Sell desk refuses (\"{(wonCopy != null ? SellDesk.Blocked(pa, wonCopy, none) : "")}\")",
                held && winOk);
            // A loss writes nolimits.stake (not alley.stake); the card goes back on top; a turn-2 loss going second is a loss; a quit is one.
            var seat2 = lineup[1];
            AlleyResult? Settle(int winner, int turns, int first, bool quit = false)
            {
                if (Ante.Hold(pa, d, nlt, seat2, null, none, ctx) == null) return null;
                if (quit) { var q = Ante.SettleAbandoned(pa, ctx); return q == null ? null : new AlleyResult { Ante = q }; }
                var r = BackAlley.Record(pa, d, nlt, winner, turns, first, ctx);
                events.UnionWith(r.Events);
                return r;
            }
            var top = Ante.Next(pa, seat2.Binder, ctx)!;
            int nlSpent0 = pa.DpSpent.GetValueOrDefault(Ledger.NoLimitsStake), aleySpent0 = pa.DpSpent.GetValueOrDefault(Ledger.AlleyStake), dpBefore = pa.Dp;
            var loss = Settle(1, 9, 0);
            bool lossOk = loss?.Ante?.Outcome == "loss" && pa.Dp == dpBefore - 2500 && pa.DpSpent.GetValueOrDefault(Ledger.NoLimitsStake) == nlSpent0 + 2500
                          && pa.DpSpent.GetValueOrDefault(Ledger.AlleyStake) == aleySpent0 && Same(Ante.Binder(pa, seat2.Binder, ctx).Cards[0], top) && loss.Events.Contains("antelose") && Balanced();
            var late = Settle(1, 2, 1);
            var quit = Settle(1, 0, 0, quit: true);
            bool lateQuit = late?.Ante?.Outcome == "loss" && quit?.Ante?.Outcome == "quit" && pa.DpSpent.GetValueOrDefault(Ledger.NoLimitsStake) == nlSpent0 + 3 * 2500 && pa.AnteLive == null
                            && BackAlley.NextSeat(pa, d, nlt) == 2 && Balanced() && Ledger.NoLimitsStake == "nolimits.stake";
            Check($"6. ante: a loss writes {Ledger.NoLimitsStake} (-2,500, never {Ledger.AlleyStake}), their card goes back on top ({Name(top.Code)}) and the seat stays; a turn-2 loss going second is a loss (the guard is turn {AlleyEconomy.NoLimitsGuardTurns}); a quit is a loss",
                lossOk && lateQuit);
            // The guard: a first-turn loss gives it back with no line; a draw too; a held save loaded settles as a loss.
            int dpG = pa.Dp, nlG = pa.DpSpent.GetValueOrDefault(Ledger.NoLimitsStake), spentG = pa.DpSpent.Values.Sum(), earnedG = pa.DpEarned.Values.Sum();
            var guard = Settle(1, 1, 1);
            var drawn = Settle(2, AlleyEconomy.AlleyTurns + 1, 0);
            bool guardOk = guard?.Ante?.Outcome == "firstturn" && guard.Ante.Lost == 0 && guard.Ante.Returned == 2500 && guard.Events.Contains("firstturn") && pa.Dp == dpG
                           && pa.DpSpent.GetValueOrDefault(Ledger.NoLimitsStake) == nlG && pa.DpSpent.Values.Sum() == spentG && pa.DpEarned.Values.Sum() == earnedG && drawn?.Ante?.Outcome == "draw" && pa.Dp == dpG
                           && Same(Ante.Binder(pa, seat2.Binder, ctx).Cards[0], top) && Balanced() && BackAlley.NextSeat(pa, d, nlt) == 2;
            // Turn 1 with the pilot NOT going first is a loss (they cannot have won before your first turn): the guard needs them first.
            var notFirst = Settle(1, 1, 0);
            bool guardNeedsFirst = notFirst?.Ante?.Outcome == "loss" && pa.Dp == dpG - 2500;
            Ante.Hold(pa, d, nlt, seat2, null, none, ctx);
            var crashed = RoundTrip(pa, printings, cards);
            var atLoad = Ante.SettleAbandoned(crashed, ctx);
            bool loadOk = atLoad?.Outcome == "quit" && crashed.AnteLive == null && crashed.DpSpent.GetValueOrDefault(Ledger.NoLimitsStake) == pa.DpSpent.GetValueOrDefault(Ledger.NoLimitsStake) + 2500
                          && Ledger.Net(crashed) == crashed.Dp - Profile.StartingDp;
            Ante.SettleAbandoned(pa, ctx);
            Check("6. ante: the guard at turn 1 (they went first) gives the stake back with no ledger line and their card; a draw too; turn 1 with you first is a loss; a save loaded mid-duel settles as a loss to nolimits.stake",
                guardOk && guardNeedsFirst && loadOk);
            // Refused below the stake.
            var poor = NoLimitsOpen(starter, cards, printings, library, 0x4E110031, dp: 0);
            poor.Spend(Ledger.Debug, poor.Dp - 2400);
            var poorSeat = BackAlley.Lineup(poor, d, nlt, ctx)[0];
            Check($"6. ante: with {poor.Dp:N0} DP the {AlleyEconomy.NoLimitsStake:N0} DP stake is refused (\"{Ante.Blocked(poor, AlleyTier.NoLimits, Ante.Next(poor, poorSeat.Binder, ctx), null, none, ctx)}\")",
                Ante.Hold(poor, d, nlt, poorSeat, null, none, ctx) == null && poor.AnteLive == null && poor.Dp == 2400);
            // The pilots: first wins raise pilotbeat:<id> (never headlinerbeat:), 'restored' fires once for the first restored lead won.
            var np = NoLimitsOpen(starter, cards, printings, library, 0x4E110032);
            var night = BackAlley.Lineup(np, d, nlt, ctx).ToList();
            var results = new List<(AlleyOpponent who, AlleyResult r)>();
            for (int k = 0; k < 4; k++)
            {
                var who = night[k];
                var r = Duel(np, nl, nlt, 0, 10, 0)!;
                events.UnionWith(r.Events);
                results.Add((who, r));
            }
            var restoredLeads = d.Builds.SelectMany(b => b.Stakes.Where(b.Restored.Contains)).ToHashSet();
            int restoredEvents = results.Count(x => x.r.Events.Contains(BackAlley.RestoredEvent));
            var firstRestored = results.FirstOrDefault(x => x.r.Ante?.Won is CardCopy w && restoredLeads.Contains(w.Code));
            bool oneRestored = restoredEvents == 1 && results.IndexOf(firstRestored) == results.FindIndex(x => x.r.Events.Contains(BackAlley.RestoredEvent))
                               && results.Count(x => x.r.Ante?.Won is CardCopy w && restoredLeads.Contains(w.Code)) >= 2;
            bool beats = results.All(x => x.r.Events.Contains(BackAlley.PilotBeatEvent + x.who.Character) && !x.r.Events.Any(e => e.StartsWith("headlinerbeat:")))
                         && results.All(x => x.r.Ante?.Won is CardCopy w && w.Paid == 0 && w.Source == CopySource.Ante) && np.Wins.Count >= 4;
            var noPay = Ledger.Net(np) == np.Dp - Profile.StartingDp && np.DpEarned.GetValueOrDefault(Ledger.Alley) == 0 && results[3].r.Reward.Tokens == AlleyEconomy.ClearTokens && results.Take(3).All(x => x.r.Reward.Tokens == 0)
                        && results[3].r.FirstClear && results[3].r.Events.Contains("alleyclear:No Limits") && results.All(x => x.r.Reward.Dp == 0);
            Check($"6. ante: a night of four wins pays 0 DP a duel and the clear token once; each first win raises {BackAlley.PilotBeatEvent}<pilot> and never headlinerbeat: ({string.Join(", ", results.Select(x => x.who.Character))}); "
                  + $"restored fires once, with the first of {results.Count(x => x.r.Ante?.Won is CardCopy w && restoredLeads.Contains(w.Code))} restored leads won ({restoredEvents} event)",
                oneRestored && beats && noPay);
            // ---- NL 7. Unlisted ----
            var pages = np.UnlistedPages.Where(pg => pg.Key.StartsWith("nl:")).ToList();
            var sources = d.Builds.Select(b => b.Deck).Concat(new[] { "nl-snake-eye-brawl", "nl-vanquish-soul-brawl", "nl-yubel-brawl", "nl-tenpai-brawl" }).Distinct()
                .Select(name => (name, path: Directory.GetFiles(Path.Combine(root, "decks", "no-limits"), name + ".ydk").FirstOrDefault())).Where(x => x.path != null).ToList();
            var withSource = sources.Where(x => File.ReadAllLines(x.path!).Any(l => l.TrimStart().StartsWith("#source", StringComparison.OrdinalIgnoreCase))).Select(x => x.name).ToList();
            // A second win against the same pilot (an encore) adds no page; the page's deck is the build as written.
            var junia = results.First(x => x.who.Character == "junia" || x.who.Seat == 4);
            var againOpp = BackAlley.Encore(np, d, nlt, junia.who.Character, ctx);
            bool again = againOpp != null && Ante.Hold(np, d, nlt, againOpp, null, none, ctx) != null && BackAlley.Record(np, d, nlt, 0, 10, 0, ctx) is { SeatWon: true } r2 && r2.Page == null;
            var firstPage = pages.FirstOrDefault();
            var pageDeck = firstPage != null ? Unlisted.DeckOf(firstPage, ctx) : null;
            Check($"7. unlisted: one page per pilot beaten, key nl:<pilot> ({string.Join(", ", pages.Select(pg => pg.Key))}), whatever seat it sat at, a second win adds none ({again}); "
                  + $"no #source line in any of the {sources.Count} decks/no-limits decks{(withSource.Count > 0 ? " (has: " + string.Join(", ", withSource) + ")" : "")}; the page's deck is the build as written (no source note)",
                pages.Count == 4 && pages.All(pg => pg.Key == $"nl:{night.First(x => x.Handle == pg.Handle).Character}") && pages.Select(pg => pg.Key).Distinct().Count() == 4
                && results.Count(x => x.r.Page != null) == 4 && results.All(x => x.r.Events.Contains(Unlisted.Event)) && again && withSource.Count == 0
                && pageDeck != null && pageDeck.Note("source").Length == 0 && Unlisted.Contraband(firstPage!, ctx).Count > 0 && sources.Count >= 11);
        }

        // ---- NL 8. Story ----
        {
            var story = Story.Load(Path.Combine(root, "campaign", "story.txt"));
            var sceneTriggers = story.Scenes.Select(sc => sc.Trigger).ToHashSet();
            var all = BackAlley.Triggers(d);
            var nlTriggers = all.Where(BackAlley.IsNoLimitsTrigger).ToList();
            var ids = roster.Select(r => r.Id).ToHashSet();
            var raised = events.Where(e => e is "nolimits" or "restored" || e.StartsWith("pilotbeat:") || e.StartsWith("alleyclear:No Limits") || e.StartsWith("headlinerbeat:")).ToList();
            var missingScenes = nlTriggers.Where(t => !sceneTriggers.Contains(t)).ToList();
            var unknown = raised.Where(e => !all.Contains(e)).ToList();
            var noScene = raised.Where(e => !sceneTriggers.Contains(e)).ToList();
            var pilots = d.Builds.Select(b => b.Id).ToList();
            var noProfile = pilots.Where(id => !ids.Contains(id)).ToList();
            var badProfile = roster.Where(r => pilots.Contains(r.Id)).Where(r => Math.Abs(r.Skill - 1.0) > 1e-9 || r.Intro.Length == 0 || r.Decks.Count == 0 || r.Decks[0] != d.FindBuild(r.Id)!.Deck).Select(r => r.Id).ToList();
            bool noHeadlinerTriggers = pilots.All(id => !all.Contains("headliner:" + id) && !all.Contains("headlinerbeat:" + id) && all.Contains(BackAlley.PilotEvent + id) && all.Contains(BackAlley.PilotBeatEvent + id));
            var nlSpeakers = story.Scenes.Where(sc => BackAlley.IsNoLimitsTrigger(sc.Trigger)).SelectMany(sc => sc.Lines).Select(l => l.Speaker)
                .Where(sp => sp.Length > 0 && sp != "-" && sp != "opponent" && !ids.Contains(sp)).Distinct().ToList();
            var voicePath = Path.Combine(root, "campaign", "alley-voice.txt");
            var voiceText = File.Exists(voicePath) ? File.ReadAllLines(voicePath) : Array.Empty<string>();
            var hooks = voiceText.Where(l => l.StartsWith("pool: ")).Select(l => l.Substring(6).Trim()).ToList();
            var badHooks = hooks.Where(h => (h.StartsWith("pilot:") || h.StartsWith("outoftowner:")) && !BackAlley.IsVoiceHook(h, d, cards)).ToList();
            Check($"8. story: the {nlTriggers.Count} No Limits triggers in BackAlley.Triggers all have scenes{(missingScenes.Count > 0 ? " (pending: " + string.Join(", ", missingScenes) + ")" : "")}; the {raised.Count} the checks raised "
                  + $"({string.Join(", ", raised.OrderBy(x => x))}) are in Triggers and have scenes{(noScene.Count + unknown.Count > 0 ? " (not: " + string.Join(", ", noScene.Concat(unknown)) + ")" : "")}; "
                  + $"builds raise pilot:/pilotbeat: and no headliner:/headlinerbeat: trigger; nolimits is raised by the unlock",
                (missingScenes.Count == 0 || missingScenes.All(t => d.Builds.Any(b => b.Wave > 1 && t.EndsWith(":" + b.Id)))) && unknown.Count == 0 && noScene.Count == 0 && noHeadlinerTriggers
                && raised.Contains("nolimits") && raised.Contains("restored") && raised.Count(e => e.StartsWith("pilotbeat:")) >= 4 && !raised.Any(e => e.StartsWith("headlinerbeat:") && d.FindBuild(e.Substring(14)) != null));
            if (missingScenes.Count > 0) Console.WriteLine($"         pending scenes (listed, not failures): {string.Join(", ", missingScenes)}");
            Check($"8. story: all {pilots.Count} pilots have profiles ({string.Join(", ", pilots)}) at skill 1.0 with a line and their build first{(noProfile.Count + badProfile.Count > 0 ? " (not: " + string.Join(", ", noProfile.Concat(badProfile)) + ")" : "")}; "
                  + $"the No Limits scenes' speakers are all profiles or opponent{(nlSpeakers.Count > 0 ? " (not: " + string.Join(", ", nlSpeakers) + ")" : "")}; pilot:/outoftowner: voice pools are valid hooks{(badHooks.Count > 0 ? " (not: " + string.Join(", ", badHooks) + ")" : "")}",
                noProfile.Count == 0 && badProfile.Count == 0 && nlSpeakers.Count == 0 && badHooks.Count == 0);
        }

        // ---- NL 9. The Brawl's out-of-towners ----
        {
            var spec = d.Brackets.First(b => b.Kind == Brackets.BrawlKind);
            Profile Champ(params string[] cleared)
            {
                var p = Opened(starter, cards, printings, library, 0x4E110090);
                foreach (var id in cleared) p.AlleyTables[id] = new AlleyTableState { Cleared = true };
                return p;
            }
            var before = Champ("dumpster-row", "basement");
            var after = Champ("dumpster-row", "basement", "black-market");
            var fieldBefore = BackAlley.BrawlField(before, d, ctx, 0xB7A110);
            var fieldAfter = BackAlley.BrawlField(after, d, ctx, 0xB7A110);
            var seedOf = new Dictionary<string, int> { ["junia"] = 8, ["augie"] = 5, ["rue"] = 2, ["sasha"] = 3 };
            var brawlDeckOf = spec.Field.Select(AlleyBracketSpec.ParseField).Where(f => f.after.Length > 0).ToDictionary(f => f.words[0], f => f.words[1]);
            var named = fieldAfter.Where(e => e.Splash == null).ToList();
            bool beforeOk = fieldBefore.Count == 7 && fieldBefore.All(e => e.Splash != null && e.Character == BackAlley.RegularId) && fieldBefore.Select(e => e.Seed).Distinct().Count() == 7;
            bool afterOk = fieldAfter.Count == 7 && named.Count == 4 && named.All(e => seedOf.TryGetValue(e.Character, out var sd) && e.Seed == sd) && fieldAfter.Count(e => e.Splash != null) == 3
                           && named.All(e => brawlDeckOf.TryGetValue(e.Character, out var bd) && e.Deck == bd && bd.EndsWith("-brawl")) && fieldAfter.Select(e => e.Seed).Distinct().Count() == 7
                           && !fieldAfter.Any(e => e.Seed == 1);
            var bBefore = Brackets.CreateAlley(d, Brackets.BrawlKind, before, ctx, 0xB7A111);
            var bAfter = Brackets.CreateAlley(d, Brackets.BrawlKind, after, ctx, 0xB7A111);
            var townersBefore = BackAlley.OutOfTowners(bBefore, d);
            var townersAfter = BackAlley.OutOfTowners(bAfter, d);
            int youSeed = bAfter.Slots.First(e => e.You).Seed;
            Check($"9. Brawl: before The Black Market's clear the field is 7 regulars (no out-of-towner: {townersBefore.Count}); after it, the four sit at seeds {string.Join(", ", named.OrderByDescending(e => e.Seed).Select(e => $"{e.Character} {e.Seed}"))} "
                  + $"with 3 regulars (you are seed {youSeed}); OutOfTowners lists {string.Join(", ", townersAfter)}; the field has {spec.Size - 1} CPUs both ways",
                beforeOk && afterOk && townersBefore.Count == 0 && townersAfter.OrderBy(x => x).SequenceEqual(OutOfTowners.OrderBy(x => x)) && youSeed == 1
                && bBefore.Size == 8 && bAfter.Size == 8);
            // Every Brawl deck: legal under the Alley list, three added cards from The Basement Game's pool, no excluded card.
            var basementEvent = Alley.Events.First(e => e.Name == "The Basement Game");
            var pool = new HashSet<uint>(ctx.Pool(basementEvent.EraCap));
            var lines = new List<string>();
            bool decksOk = true;
            foreach (var pilot in OutOfTowners)
            {
                var build = d.FindBuild(pilot)!;
                var deckName = brawlDeckOf[pilot];
                var deck = ctx.LoadDeck(deckName);
                var baseDeck = ctx.LoadDeck(build.Base);
                if (deck == null || baseDeck == null) { lines.Add($"{deckName}: missing"); decksOk = false; continue; }
                var played = deck.Clone();
                played.Side.Clear();
                var problems = DeckRules.Validate(played, ctx.List, cards);
                var legalBase = Loaners.Adjust(baseDeck, ctx.List, cards).Deck;
                var baseCodes = new HashSet<uint>(baseDeck.Main.Concat(baseDeck.Extra).Concat(legalBase.Main).Concat(legalBase.Extra).Concat(legalBase.Side).Select(c => Banlist.BaseCode(c, cards)));
                var addedCodes = played.Main.Concat(played.Extra).Select(c => Banlist.BaseCode(c, cards)).Distinct().Where(c => !baseCodes.Contains(c)).ToList();
                bool ok = problems.Count == 0 && addedCodes.Count == 3 && addedCodes.All(pool.Contains) && played.Extra.All(c => baseCodes.Contains(Banlist.BaseCode(c, cards)))
                          && !played.Main.Concat(played.Extra).Any(c => excludedCodes.Contains(Banlist.BaseCode(c, cards))) && deck.Note("source").Length == 0
                          && ctx.Ratings.ContainsKey(deckName) && deck.Note("recipe").Length > 0;
                decksOk &= ok;
                lines.Add($"{deckName} {(ok ? "ok" : "NOT OK")}: {problems.Count} Alley-list problems, + {string.Join(", ", addedCodes.Select(Name))}{(ctx.Ratings.TryGetValue(deckName, out var r) ? $", rated {r:0}" : ", unrated")}");
            }
            Check($"9. Brawl: every Brawl deck is legal under the Alley list, holds exactly three Basement-pool cards over its base made Alley-legal (Main Deck only), no excluded card, no #source, a recipe and a rating: {string.Join("; ", lines)}",
                decksOk);
            // The twin rule bites: a peak build and its as-built base in one bracket is a problem.
            var twinText = s.EventsText.Replace("field: junia nl-snake-eye-brawl 8", "field: junia nl-snake-eye 8").Replace("field: rue nl-yubel-brawl 2", "field: rue snake-eye 2");
            var twin = AlleyData.Parse(twinText, cards).Problems(ctx).Where(x => x.Contains("both in the field")).ToList();
            // CPUs counted the same before and after: a field with a missing out-of-towner line is a problem.
            var short_ = AlleyData.Parse(s.EventsText.Replace("field: sasha nl-tenpai-brawl 3 | after: The Black Market\n", ""), cards).Problems(ctx).Where(x => x.Contains("CPUs, not")).ToList();
            Check($"9. Brawl: Problems catches a peak build beside its as-built base (\"{twin.FirstOrDefault()}\") and a field that loses an out-of-towner (\"{short_.FirstOrDefault()}\")",
                twin.Count == 1 && short_.Count == 1);
        }
        Console.WriteLine($"  (No Limits checks: {added} added, {timer.Elapsed.TotalSeconds:0.0} s)");
        // The Invitational and Moth's restored slots (NL2 build plan, C3): their own count line.
        InvitationalChecks(s, nl, starter, cards, printings, library, roster, root, check, events);
    }
}
