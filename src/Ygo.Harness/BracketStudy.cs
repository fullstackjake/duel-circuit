using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// Library Locals (BRACKETS-SPEC-2026-11-01):
/// <list type="bullet">
/// <item>`--locals-check`: each cup's three `local:` lines (campaign/library.txt): the deck is rated, 90% of its cards are
/// out by the cup's pool and its #era is at or before the cup's (a mirror of the cup's own deck is exempt from both), it's in the
/// pilot's `decks:` list, and no character appears twice in the field. Prints each field by seed, and the other locals
/// that would pass.</item>
/// <item>`--prize-check [--games N]`: N Prize Packs per cup (default 1,000): every slot filled, slots 1-3 Rare or better
/// from the field, slot 4 from the cup's tournament set, slabs about 75/25, 0 DP back; the average pack value.</item>
/// <item>`--bracket-sim [--games N]`: brackets made by <see cref="Brackets.Create"/> and settled by <see cref="Brackets.Odds"/>,
/// your matches at the cup's measured rates (library-advice.txt, your best measured deck there): title odds per cup.
/// Target 30-45%. Then the Back Alley's Alley Brawl and Midnight Open (<see cref="AlleySim"/>): fields drawn by
/// <see cref="Brackets.CreateAlley"/>, title odds by your rating. Targets: 30-45% and 15-30% for a champion's best deck. Since NL-2 the
/// No Limits Invitational too (target 10-20% for the reference deck, NO-LIMITS-SPEC 9.2): its field is the eleven peak builds on their
/// own decks, so the rows use stand-in ratings for the builds without a rated deck (wave 2's) and are smoke runs, unconfirmed hints
/// until the tuning pass.</item>
/// </list>
/// </summary>
public static class BracketStudy
{
    public const double ReleasedShare = 0.9;

    private sealed class Setup
    {
        public List<Personality> Roster = null!;
        public Library Library = null!;
        public Dictionary<string, double> Ratings = null!;
        public Dictionary<string, string> DeckFiles = null!;
        public PrintingIndex Printings = null!;
        public WallAdvice Advice = null!;

        public Deck? Load(string name)
        {
            if (!DeckFiles.TryGetValue(name, out var file)) return null;
            var d = Deck.LoadYdk(file);
            d.Name = name;
            return d;
        }
    }

    private static Setup Load(string root)
    {
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities")).ToList();
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        return new Setup
        {
            Roster = roster,
            Library = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), roster, circuits),
            Ratings = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName)),
            DeckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
                .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First()),
            Printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin")),
            Advice = WallAdvice.Load(Path.Combine(root, "campaign", Library.AdviceFile)),
        };
    }

    // ---- --locals-check ------------------------------------------------------------------------------

    private static int Era(Deck deck) => int.TryParse(deck.Note("era"), out var y) ? y : 0;

    private static double Released(Deck deck, string pool, Setup s, ICardDataProvider cards)
    {
        var all = deck.Main.Concat(deck.Extra).ToList();
        return all.Count == 0 ? 0 : all.Count(c => DeckRules.Released(Banlist.BaseCode(c, cards), pool, s.Printings, cards)) / (double)all.Count;
    }

    /// <summary>Why a pilot and deck can't be a local at a cup (null: they can), given who's in the field already.</summary>
    private static string? LocalProblem(Circuit cup, Personality who, string deckName, IEnumerable<string> taken, Setup s, ICardDataProvider cards)
    {
        if (!s.Ratings.ContainsKey(deckName)) return "the deck isn't rated";
        var deck = s.Load(deckName);
        if (deck == null) return "no deck file";
        if (!who.Decks.Contains(deckName)) return $"not in {who.Id}'s decks: list ({string.Join(", ", who.Decks)})";
        // A mirror of one of the cup's own decks is as much of its era as the cup's match is (CPUs ignore the pool).
        bool cupDeck = cup.Matches.Any(m => m.Deck == deckName);
        double share = Released(deck, cup.Pool, s, cards);
        if (share < ReleasedShare && !cupDeck) return $"only {share:P0} of its cards are out by {cup.Pool}";
        int era = Era(deck);
        if (!cupDeck && era == 0) return "no #era tag";
        if (!cupDeck && era > cup.Era) return $"#era {era} is after the cup's {cup.Era}";
        if (taken.Contains(who.Id)) return $"{who.Id} is in the field already";
        return null;
    }

    public static int LocalsCheck(ICardDataProvider cards, string root)
    {
        var s = Load(root);
        int problems = 0, mirrors = 0;
        foreach (var cup in s.Library.Cups)
        {
            Console.WriteLine($"\n{cup.Name} (era {cup.Era}, pool {cup.Pool})");
            if (cup.Locals.Count != Brackets.LocalsPerCup) { Console.WriteLine($"  FAIL {cup.Locals.Count} local: lines, not {Brackets.LocalsPerCup}"); problems++; }
            var field = cup.Matches.Concat(cup.Locals).ToList();
            foreach (var dup in field.GroupBy(m => m.Character.Id).Where(g => g.Count() > 1))
            {
                Console.WriteLine($"  FAIL {dup.Key} appears {dup.Count()} times in the field");
                problems++;
            }
            for (int i = 0; i < cup.Locals.Count; i++)
            {
                var local = cup.Locals[i];
                var others = field.Where(m => m != local).Select(m => m.Character.Id).ToList();
                var why = LocalProblem(cup, local.Character, local.Deck, others, s, cards);
                bool mirror = field.Count(m => m.Deck == local.Deck) > 1;
                if (mirror) mirrors++;
                if (why != null) problems++;
                var deck = s.Load(local.Deck);
                Console.WriteLine($"  {(why == null ? "ok  " : "FAIL")} local: {local.Character.Id} {local.Deck}  Elo {DeckRatings.Of(s.Ratings, local.Deck):0}"
                    + $", era {(deck != null && Era(deck) > 0 ? Era(deck).ToString() : "-")}, {(deck != null ? Released(deck, cup.Pool, s, cards) : 0):P0} out by the pool"
                    + (mirror ? "  MIRROR" : "") + (why != null ? $"  ({why})" : ""));
            }
            var seeded = Brackets.LocalsField(cup, s.Ratings);
            Console.WriteLine($"  field: {string.Join(" · ", seeded.Select(e => $"#{e.Seed} {e.Character}/{e.Deck} {e.Elo:0}"))}");
            // The other locals that would pass here (not in the field already).
            var inField = field.Select(m => m.Character.Id).ToHashSet();
            var spare = s.Roster.Where(p => !inField.Contains(p.Id))
                .SelectMany(p => p.Decks.Select(d => (p, d)))
                .Where(x => LocalProblem(cup, x.p, x.d, inField, s, cards) == null)
                .Select(x => $"{x.p.Id} {x.d} {DeckRatings.Of(s.Ratings, x.d):0}{(field.Any(m => m.Deck == x.d) ? " (mirror)" : "")}").ToList();
            Console.WriteLine($"  other candidates: {(spare.Count == 0 ? "none" : string.Join(", ", spare))}");
            if (Environment.GetEnvironmentVariable("LOCALS_WHY") == "1")
                foreach (var p in s.Roster.Where(p => !inField.Contains(p.Id)))
                    foreach (var d in p.Decks)
                        if (LocalProblem(cup, p, d, inField, s, cards) is string w && !w.Contains("#era") && !w.Contains("era tag")) Console.WriteLine($"    {p.Id} {d}: {w}");
        }
        Console.WriteLine($"\n{s.Library.Cups.Sum(c => c.Locals.Count)} local: lines, {mirrors} mirrors, {problems} problem{(problems == 1 ? "" : "s")}");
        return problems == 0 ? 0 : 1;
    }

    // ---- --bracket-sim -------------------------------------------------------------------------------

    private static double EloGap(double p) => 400 * Math.Log10(Math.Clamp(p, 0.02, 0.98) / (1 - Math.Clamp(p, 0.02, 0.98)));

    /// <summary>Your deck at a cup (the measured option with the best average over its matches) and your chance against each entrant.</summary>
    private static (string deck, double elo, Func<BracketEntrant, double> chance) You(Setup s, int c)
    {
        var cup = s.Library.Cups[c];
        var keys = Enumerable.Range(0, cup.Matches.Count).Select(m => Profile.MatchKey(c, m)).ToList();
        var decks = keys.SelectMany(k => s.Advice.For(k)).Select(o => o.Deck).Distinct()
            .Where(d => keys.All(k => s.Advice.For(k).Any(o => o.Deck == d))).ToList();
        if (decks.Count == 0) return ("(unmeasured)", 1500, e => Brackets.Odds(1500, e.Elo));
        double Rate(string deck, int m) => s.Advice.For(keys[m]).First(o => o.Deck == deck).Percent / 100.0;
        var best = decks.OrderByDescending(d => Enumerable.Range(0, keys.Count).Average(m => Rate(d, m))).ThenBy(d => d, StringComparer.Ordinal).First();
        // Your effective rating: what your measured rates imply against the cup's own decks.
        double elo = Enumerable.Range(0, keys.Count).Average(m => DeckRatings.Of(s.Ratings, cup.Matches[m].Deck) + EloGap(Rate(best, m)));
        double Chance(BracketEntrant e)
        {
            // A cup deck (or a mirror of one) at its measured rate; a local at your effective rating's odds.
            int m = cup.Matches.FindIndex(x => x.Deck == e.Deck);
            return m >= 0 ? Math.Clamp(Rate(best, m), Brackets.MinOdds, Brackets.MaxOdds) : Brackets.Odds(elo, e.Elo);
        }
        return (best, elo, Chance);
    }

    public static int Sim(ICardDataProvider cards, string root, int seed, int brackets)
    {
        var s = Load(root);
        Console.WriteLine($"Library Locals: {brackets} brackets per cup from Brackets.Create, CPU matches by Brackets.Odds and the seed's coins, "
            + "your matches at library-advice.txt's measured rates (your best measured deck; locals at the Elo those rates imply).");
        Console.WriteLine($"  {"cup",-26} {"your deck",-20} {"Elo",5}  {"title",6} {"final",6} {"semi",6}  {"DP/entry",8} {"packs",5}  field odds (#seed: your chance)");
        var titles = new List<double>();
        var scratch = new Profile();
        for (int c = 0; c < s.Library.Cups.Count; c++)
        {
            var cup = s.Library.Cups[c];
            if (cup.Locals.Count != Brackets.LocalsPerCup) { Console.WriteLine($"  {cup.Name,-26} no Locals field"); continue; }
            var (deck, elo, chance) = You(s, c);
            var place = new int[4];
            long dp = 0, packs = 0;
            for (int n = 0; n < brackets; n++)
            {
                uint bseed = Hash.Mix((uint)seed, 0xB5u ^ (uint)c, (uint)n);
                var b = Brackets.Create(cup, s.Ratings, bseed, deck);
                for (int m; (m = b.YourMatch()) >= 0;)
                {
                    bool won = Hash.Mix(bseed, 0x50FFu, (uint)m) / 4294967296.0 < chance(b.Slots[b.Opponent(m)]);
                    var result = Brackets.RecordYours(scratch, s.Library, b, won ? 0 : 1, loaner: false)!;
                    dp += result.Reward.Dp;
                }
                Brackets.SkipToEnd(b);
                int p = b.Placement();
                place[p]++;
                if (p == 0) dp += Brackets.PlacementDp(b, 0);
                packs += Brackets.LocalsPacks[p];
            }
            double title = place[0] / (double)brackets;
            titles.Add(title);
            var field = Brackets.LocalsField(cup, s.Ratings);
            Console.WriteLine($"  {cup.Name,-26} {deck,-20} {elo,5:0}  {title,6:P1} {(place[0] + place[1]) / (double)brackets,6:P1} {(place[0] + place[1] + place[2]) / (double)brackets,6:P1}"
                + $"  {dp / (double)brackets - Brackets.LocalsEntry,8:+0;-0} {packs / (double)brackets,5:0.00}  "
                + string.Join(" ", field.Select(e => $"#{e.Seed}:{chance(e):P0}"))
                + (title < 0.30 ? "  UNDER" : title > 0.45 ? "  OVER" : ""));
        }
        Console.WriteLine($"\nTitle odds: average {titles.Average():P1}, {titles.Count(t => t >= 0.30 && t <= 0.45)} of {titles.Count} cups in the 30-45% target");
        return 0;
    }

    /// <summary>The Elo your best measured deck plays like at each cup (library-advice.txt's rates against the cup's decks), by cup.</summary>
    public static List<(string cup, string deck, double elo)> MeasuredElo(string root)
    {
        var s = Load(root);
        return Enumerable.Range(0, s.Library.Cups.Count).Select(c =>
        {
            var y = You(s, c);
            return (s.Library.Cups[c].Name, y.deck, y.elo);
        }).ToList();
    }

    // ---- --bracket-sim: the alley's brackets ------------------------------------------------------------

    /// <summary>
    /// The Back Alley's brackets (docs/dev/ALLEY-SPEC-2026-11-01.md section 11) as the game makes them: fields drawn by
    /// <see cref="Brackets.CreateAlley"/> (the Alley Brawl's from <see cref="BackAlley.BrawlTable"/> after Dumpster Row and
    /// after The Basement Game; the Midnight Open's), CPU matches by their coins against <see cref="Brackets.Odds"/> and
    /// matchups.txt (a CPU Bo3 final: one coin against the series odds), your matches (each game of your Bo3 final) by a coin
    /// against the Elo odds of a deck at your rating, through <see cref="Brackets.RecordYours"/>. <paramref name="fields"/>
    /// fields, each played over <paramref name="brackets"/> / fields coin seeds. Title odds for a sweep of ratings and for a
    /// champion's best deck: the highest-rated deck legal in the Alley as built, and the rating library-advice.txt's
    /// measured rates imply for your best deck at the Library's last four cups. Targets: 30-45% (Brawl), 15-30% (Midnight Open).
    /// </summary>
    public static int AlleySim(ICardDataProvider cards, string root, int seed, int brackets, int fields = 200)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities")).ToList();
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var library = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), roster, circuits);
        var a = AlleyTest.Load(cards, root, printings, roster, library);
        var data = a.Data;
        var ctx = a.Ctx;
        var matchups = Matchups.Load(Path.Combine(root, "campaign", Matchups.FileName));
        var starter = Deck.LoadYdk(Path.Combine(root, "decks", "classic.ydk"));
        fields = Math.Clamp(fields, 1, Math.Max(1, brackets));
        int replays = Math.Max(1, brackets / fields);

        // A champion's best deck: the highest-rated deck that's legal in the Alley as built (sideboard aside: a CPU never sides,
        // and you'd leave it at home); and what the Library's measured rates say your best deck plays like late on.
        var bestLegal = ctx.Ratings.OrderByDescending(r => r.Value).ThenBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => (name: r.Key, elo: r.Value, deck: ctx.LoadDeck(r.Key)))
            .Where(x => x.deck != null)
            .First(x => { var d = x.deck!.Clone(); d.Side.Clear(); return DeckRules.Validate(d, a.List, cards).Count == 0; });
        var measured = MeasuredElo(root);
        var late = measured.Skip(Math.Max(0, measured.Count - 4)).ToList();
        double lateElo = late.Average(x => x.elo);
        // The No Limits Invitational's reference (NO-LIMITS-SPEC 4.7): the highest-rated deck that's legal under the No Limits list.
        var noLimitsList = Alley.NoLimitsList(cards);
        var bestNoLimits = ctx.Ratings.OrderByDescending(r => r.Value).ThenBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => (name: r.Key, elo: r.Value, deck: ctx.LoadDeck(r.Key)))
            .Where(x => x.deck != null)
            .First(x => { var d = x.deck!.Clone(); d.Side.Clear(); return DeckRules.Validate(d, noLimitsList, cards).Count == 0; });
        var baseRatings = new[] { 1500.0, 1600, 1700, 1800, 1900 }.Append(Math.Round(lateElo)).ToList();
        Console.WriteLine($"\nThe Back Alley's brackets: {fields} fields each, drawn by Brackets.CreateAlley; each field over {replays} coin seeds ({fields * replays:N0} brackets a row); "
            + "CPU matches by Brackets.Odds and matchups.txt, your duels by the Elo odds of a deck at the row's rating.");
        Console.WriteLine($"  A champion's best deck: {bestLegal.name} ({bestLegal.elo:0}), the highest-rated deck legal in the Alley as built; "
            + $"the Library's measured rates put your best decks at the last four cups at {lateElo:0} ({string.Join(", ", late.Select(x => $"{x.deck} {x.elo:0}"))}). "
            + $"The Invitational's reference is {bestNoLimits.name} ({bestNoLimits.elo:0}), the highest-rated deck legal under the No Limits list.");

        Profile Champ(params string[] cleared)
        {
            var p = AlleyTest.Opened(starter, cards, printings, library, (uint)seed * 2654435761u + 0xB5u);
            foreach (var id in cleared) p.AlleyTables[id] = new AlleyTableState { Cleared = true };
            return p;
        }
        // NO-LIMITS-SPEC section 8: after The Black Market's clear the Brawl seats the four out-of-towners (Brawl decks, seeds 8/5/2/3). The
        // comparison row seats the same four as built (their bases, unseeded: seeded by Elo), the model's "4 as built" row.
        var asBuiltText = a.EventsText
            .Replace("junia nl-snake-eye-brawl 8", "junia snake-eye").Replace("augie nl-vanquish-soul-brawl 5", "augie k9-vanquish-soul")
            .Replace("rue nl-yubel-brawl 2", "rue yubel").Replace("sasha nl-tenpai-brawl 3", "sasha tenpai-dragon");
        var asBuilt = AlleyData.Parse(asBuiltText, cards);
        // The Invitational's rows (smoke runs; NO-LIMITS-SPEC 9.2): the field is the eleven peak builds on their own decks. Wave 2's decks don't
        // exist, so their ratings are stand-ins: the table's `elo:` window midpoint, Remy (the end chair) at the reference. One row keeps the
        // wave-1 builds at their measured peak-ratings.txt ratings (below the window until the tuning pass); the other puts all eleven at the
        // window, the spec's own model (section 9.2: about 12.5% for the reference).
        var nlTable = data.Tables.First(t => t.Tier == AlleyTier.NoLimits);
        double windowMid = (nlTable.MinElo + nlTable.MaxElo) / 2;
        var invBuilds = data.Builds.ToList();
        var asRated = new Dictionary<string, double>(ctx.Ratings);
        var atWindow = new Dictionary<string, double>(ctx.Ratings);
        foreach (var h in invBuilds)
        {
            double standIn = h.Id == "remy" ? bestNoLimits.elo : windowMid;
            if (!asRated.ContainsKey(h.Deck)) asRated[h.Deck] = standIn;
            atWindow[h.Deck] = standIn;
        }
        var invAsRated = AlleyTest.WithRatings(a, asRated);
        var invAtWindow = AlleyTest.WithRatings(a, atWindow);
        var states = new List<(string name, string kind, Profile p, double low, double high, AlleyData data, AlleyContext rowCtx, string refName, double refElo)>
        {
            ("Alley Brawl after Dumpster Row", Brackets.BrawlKind, Champ("dumpster-row"), 0.30, 0.45, data, ctx, bestLegal.name, bestLegal.elo),
            ("Alley Brawl after The Basement Game", Brackets.BrawlKind, Champ("dumpster-row", "basement"), 0.30, 0.45, data, ctx, bestLegal.name, bestLegal.elo),
            ("Alley Brawl after The Black Market (out-of-towners)", Brackets.BrawlKind, Champ("dumpster-row", "basement", "black-market"), 0.30, 0.45, data, ctx, bestLegal.name, bestLegal.elo),
            ("Alley Brawl after The Black Market (comparison: the four as built, seeded by Elo)", Brackets.BrawlKind, Champ("dumpster-row", "basement", "black-market"), 0.30, 0.45, asBuilt, ctx, bestLegal.name, bestLegal.elo),
            ("Midnight Open", Brackets.MidnightKind, Champ("dumpster-row", "basement", "black-market"), 0.15, 0.30, data, ctx, bestLegal.name, bestLegal.elo),
            ($"No Limits Invitational (smoke: wave 1 as rated in peak-ratings.txt, wave 2 stand-ins at {windowMid:0}, Remy at {bestNoLimits.elo:0})", Brackets.InvitationalKind,
                Champ("dumpster-row", "basement", "black-market", "nyx", "no-limits"), 0.10, 0.20, data, invAsRated.Ctx, bestNoLimits.name, bestNoLimits.elo),
            ($"No Limits Invitational (smoke, the spec's 9.2 model: all eleven builds at the window's {windowMid:0}, Remy at {bestNoLimits.elo:0})", Brackets.InvitationalKind,
                Champ("dumpster-row", "basement", "black-market", "nyx", "no-limits"), 0.10, 0.20, data, invAtWindow.Ctx, bestNoLimits.name, bestNoLimits.elo),
        };
        var scratch = new Profile();
        var verdicts = new List<string>();
        foreach (var (name, kind, profile, low, high, rowData, rowCtx, refName, refElo) in states)
        {
            var ratingsToTry = baseRatings.Append(refElo).Distinct().OrderBy(x => x).ToList();
            var protos = Enumerable.Range(0, fields).Select(f => Brackets.CreateAlley(rowData, kind, profile, rowCtx, Hash.Mix((uint)seed, 0xA5u ^ (uint)Math.Max(0, Array.IndexOf(Brackets.AlleyKinds, kind)), (uint)f))).ToList();
            var first = protos[0];
            int size = first.Size;
            var from = first.Slots.Where(e => e.Splash != null).Select(e => rowData.Tables[rowData.IndexOf(e.Splash!.From)].Name).Distinct().ToList();
            var townersHere = BackAlley.OutOfTowners(first, rowData);
            Console.WriteLine($"\n{name} ({size}, {(first.FinalBestOf > 1 ? $"best-of-{first.FinalBestOf} final, " : "")}entry {Brackets.EntryFor(kind):N0}, pay {string.Join("/", Brackets.PayFor(kind))}, "
                + $"packs {string.Join("/", Brackets.PacksFor(kind))}; regulars from {string.Join(", ", from)}{(townersHere.Count > 0 ? $"; out-of-towners {string.Join(", ", townersHere)}" : "")}): you are seed {first.Slots.First(e => e.You).Seed}");
            // The field by seed: the named entrants as written, the regulars' Elo over the fields drawn.
            var bySeed = Enumerable.Range(1, size).Where(sd => !first.Slots.First(e => e.Seed == sd).You).Select(sd =>
            {
                var at = protos.Select(b => b.Slots.First(e => e.Seed == sd)).ToList();
                var named = at.Where(e => e.Splash == null).Select(e => e.Character).Distinct().ToList();
                string who = named.Count == 1 && at.All(e => e.Splash == null) ? $"{named[0]}/{at[0].Deck}" : at.All(e => e.Splash != null) ? "regular" : "mixed";
                return $"#{sd} {who} {at.Average(e => e.Elo):0}";
            });
            Console.WriteLine($"  field (mean Elo over {fields} fields): {string.Join(" · ", bySeed)}");
            Console.WriteLine($"  {"your Elo",8}  {"title",6} {"final",6} {"semi",6}  {"DP/entry",8} {"packs",5}  your chance by seed");
            foreach (var elo in ratingsToTry)
            {
                var place = new int[size == 16 ? 5 : 4];
                long dp = 0, packs = 0;
                double Chance(BracketEntrant e) => Brackets.Odds(elo, e.Elo);
                for (int f = 0; f < fields; f++)
                    for (int r = 0; r < replays; r++)
                    {
                        var proto = protos[f];
                        var b = new Bracket(size) { Kind = proto.Kind, Event = proto.Event, Name = proto.Name, FinalBestOf = proto.FinalBestOf, Seed = Hash.Mix(proto.Seed, 0xC01u, (uint)r) };
                        Array.Copy(proto.Slots, b.Slots, size);
                        int game = 0;
                        for (int m; (m = b.YourMatch()) >= 0; game++)
                        {
                            bool won = Hash.Mix(b.Seed, 0x50FFu, (uint)game) / 4294967296.0 < Chance(b.Slots[b.Opponent(m)]);
                            var result = Brackets.RecordYours(scratch, library, b, won ? 0 : 1, loaner: false, matchups)!;
                            dp += result.Reward.Dp;
                        }
                        Brackets.SkipToEnd(b, matchups);
                        int pl = b.Placement();
                        place[pl]++;
                        if (pl == 0) dp += Brackets.PlacementDp(b, 0);
                        packs += Brackets.PacksFor(kind)[pl];
                    }
                int n = fields * replays;
                double title = place[0] / (double)n;
                bool marked = elo == refElo || elo == Math.Round(lateElo);
                string mark = elo == refElo ? $"  <- {refName}" : elo == Math.Round(lateElo) ? "  <- measured late-Library best" : "";
                string flag = !marked ? "" : title < low ? "  UNDER" : title > high ? "  OVER" : "  ok";
                var odds = Enumerable.Range(1, size).Where(sd => !first.Slots.First(e => e.Seed == sd).You)
                    .Select(sd => $"#{sd}:{protos.Average(b => Chance(b.Slots.First(e => e.Seed == sd))):P0}");
                Console.WriteLine($"  {elo,8:0}  {title,6:P1} {(place[0] + place[1]) / (double)n,6:P1} {(place[0] + place[1] + place[2]) / (double)n,6:P1}"
                    + $"  {dp / (double)n - Brackets.EntryFor(kind),8:+0;-0} {packs / (double)n,5:0.00}  {string.Join(" ", odds)}{mark}{flag}");
                if (marked) verdicts.Add($"{name}, {(elo == refElo ? refName : "measured late-Library best")} ({elo:0}): {title:P1} (target {low:P0}-{high:P0}){flag}");
            }
        }
        Console.WriteLine($"\nA champion's best deck's title odds (targets: 30-45% Brawl, 15-30% Midnight Open, 10-20% No Limits Invitational; the Invitational rows are smoke runs on stand-in ratings, unconfirmed until the tuning pass):");
        foreach (var v in verdicts) Console.WriteLine("  " + v);
        Console.WriteLine($"({timer.Elapsed.TotalSeconds:0} s)");
        return 0;
    }

    // ---- --prize-check -------------------------------------------------------------------------------

    public static int PrizeCheck(ICardDataProvider cards, string root, int packs)
    {
        var s = Load(root);
        var prizes = new PrizePacks(cards, s.Printings, s.Load);
        int failures = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        Console.WriteLine($"Tournament sets for slot 4: {string.Join(", ", prizes.TournamentSets.Select(t => $"{t.Code} {t.Name} ({t.Date})"))}");
        var values = new List<double>();
        for (int c = 0; c < s.Library.Cups.Count; c++)
        {
            var cup = s.Library.Cups[c];
            if (cup.Locals.Count != Brackets.LocalsPerCup) continue;
            var b = Brackets.Create(cup, s.Ratings, Hash.Mix(20261101u, (uint)c), "");
            var field = prizes.FieldCards(b, cup.Pool).ToHashSet();
            var set = prizes.TournamentSet(cup.Pool);
            var profile = new Profile();
            profile.Collection.Salt = 0xC0FFEE + (uint)c;
            profile.Collection.CommonestOf = code => s.Printings.Commonest(code, cards.Get(code)?.Alias ?? 0);
            int dp0 = profile.Dp, filled = 0, rareOk = 0, fieldOk = 0, distinct = 0, setOk = 0, tens = 0, slabs = 0, slabOk = 0, prizeCopies = 0, sellable = 0, ordered = 0;
            long value = 0;
            var rarities = new Dictionary<Rarity, int>();
            for (int k = 0; k < packs; k++)
            {
                var pulls = prizes.Give(profile, b, cup.Pool, k);
                if (pulls.Count == PrizePacks.Size && pulls.Select(p => p.Slot).OrderBy(x => x).SequenceEqual(new[] { 1, 2, 3, 4, 5 })) filled++;
                var fieldPulls = pulls.Where(p => p.Slot <= 3).ToList();
                if (fieldPulls.All(p => p.Rarity >= Rarity.Rare)) rareOk++;
                if (fieldPulls.All(p => field.Contains(p.Code))) fieldOk++;
                if (fieldPulls.Select(p => p.Code).Distinct().Count() == fieldPulls.Count) distinct++;
                var four = pulls.FirstOrDefault(p => p.Slot == 4);
                if (four != null && set != null && PrintingIndex.SetPrefix(four.SetCode) == set.Code) setOk++;
                var slab = pulls.FirstOrDefault(p => p.Slot == 5);
                if (slab != null)
                {
                    slabs++;
                    if (slab.Slab == 10) tens++;
                    if (slab.Rarity >= Rarity.SuperRare && field.Contains(slab.Code) && (slab.Slab == 9 || slab.Slab == 10)
                        && (slab.Copy == null || (slab.Copy.Slab == slab.Slab && slab.Copy.Floor == slab.Slab && slab.Copy.Grade == slab.Slab))) slabOk++;
                }
                if (pulls.Last().Slot == 5 && pulls.Zip(pulls.Skip(1), (x, y) => x.Slab > 0 || y.Slab > 0 || Pricing.Unit(x.Rarity) <= Pricing.Unit(y.Rarity)).All(ok => ok)) ordered++;
                foreach (var p in pulls)
                {
                    value += p.Value;
                    if (p.Slot <= 4) rarities[p.Rarity] = rarities.GetValueOrDefault(p.Rarity) + 1;
                    if (p.Copy != null)
                    {
                        if (p.Copy.Source == CopySource.Prize && p.Copy.Paid == 0) prizeCopies++;
                        if (SellDesk.Blocked(profile, p.Copy, _ => 0) == null || profile.Collection.SellPrice(p.Copy) > 0) sellable++;
                    }
                }
            }
            double avg = value / (double)packs;
            values.Add(avg);
            Console.WriteLine($"\n{cup.Name} (pool {cup.Pool}): {field.Count} field cards; slot 4 from {set?.Name ?? "none"} ({set?.Date}); average pack {avg:N0} DP of value");
            Console.WriteLine($"  slots 1-4 by rarity: {string.Join(", ", rarities.OrderBy(r => r.Key).Select(r => $"{Rarities.Name(r.Key)} {100.0 * r.Value / (4.0 * packs):0.0}%"))}");
            Check($"every pack has 5 pulls, one per slot ({filled}/{packs})", filled == packs);
            Check($"slots 1-3 are 3 different field cards at Rare or better ({Math.Min(Math.Min(rareOk, fieldOk), distinct)}/{packs})", rareOk == packs && fieldOk == packs && distinct == packs);
            Check($"slot 4 comes from {set?.Code} ({setOk}/{packs})", setOk == packs);
            Check($"slabs: field cards at Super Rare or better, sealed 9 or 10 (Slab = Floor = Grade): {slabOk}/{slabs}; Grade 10 {100.0 * tens / Math.Max(1, slabs):0.0}% (target about 25%)",
                slabs == packs && slabOk == slabs && Math.Abs(tens / (double)slabs - PrizePacks.SlabTenPercent / 100.0) < 0.04);
            Check($"revealed least rare first, the slab last ({ordered}/{packs})", ordered == packs);
            Check($"0 DP back over {packs} packs ({profile.Dp - dp0:+0;-0} DP; {prizeCopies} kept copies are Prize, paid 0; {sellable} sell for DP)",
                profile.Dp == dp0 && sellable == 0 && prizeCopies > 0);
            var again = prizes.Roll(b, cup.Pool, 7).Select(p => (p.Code, p.Rarity, p.Slab));
            Check("the same bracket and pack number give the same pack", again.SequenceEqual(prizes.Roll(b, cup.Pool, 7).Select(p => (p.Code, p.Rarity, p.Slab))));
        }
        Console.WriteLine($"\nAverage pack value across cups: {values.Average():N0} DP (from {values.Min():N0} to {values.Max():N0})");
        Console.WriteLine(failures == 0 ? "All prize checks passed." : $"{failures} prize checks failed.");
        return failures == 0 ? 0 : 1;
    }
}

/// <summary>`--campaign-test`'s bracket checks (BRACKETS-SPEC-2026-11-01 section 7; sizes 8 and 16 and the best-of-three final: ALLEY-SPEC section 11).</summary>
public static class BracketChecks
{
    public static int Run(ICardDataProvider cards, string root, PrintingIndex printings, List<Personality> roster, Library library, Deck starter)
    {
        int failures = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        Console.WriteLine("\nBrackets (Library Locals):");
        var ratings = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName));
        Check($"DeckRatings.Parse reads {ratings.Count} rated decks (tearlaments {DeckRatings.Of(ratings, "tearlaments"):0}, an unrated deck counts as {DeckRatings.Of(ratings, "no-such-deck"):0})",
            ratings.Count >= 90 && ratings["tearlaments"] == 1947 && ratings["frog-ftk"] == 1054 && DeckRatings.Of(ratings, "no-such-deck") == 1500
            && DeckRatings.Parse("  1. a-deck   Elo  1600   won 50%\nBetter going second\n  a-deck   32  (first...)").Count == 1);
        Check($"library.txt: every cup has {Brackets.LocalsPerCup} local: lines, kept apart from its matches ({library.Cups.Sum(c => c.Locals.Count)} in all)",
            library.Cups.All(c => c.Locals.Count == Brackets.LocalsPerCup && c.Matches.Count == 4 && c.Locals.All(l => !c.Matches.Contains(l))));
        Check("Odds: even at equal Elo, 400 points is 10 to 1, clamped to [3%, 97%], blended with measured results",
            Math.Abs(Brackets.Odds(1500, 1500) - 0.5) < 1e-9 && Math.Abs(Brackets.Odds(1900, 1500) - 10 / 11.0) < 1e-9 && Brackets.Odds(2500, 1000) == Brackets.MaxOdds
            && Brackets.Odds(1000, 2500) == Brackets.MinOdds && Math.Abs(Brackets.Odds(1500, 1500, 20, 20) - 0.75) < 1e-9);

        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        Deck? LoadDeck(string name) => deckFiles.TryGetValue(name, out var f) ? Deck.LoadYdk(f) : null;
        var prizes = new PrizePacks(cards, printings, LoadDeck);
        Profile Champion(uint salt)
        {
            var p = Profile.New(starter, cards, printings);
            p.Collection.Salt = salt;
            p.Collection.CommonestOf = c => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
            p.LibraryOpen = true;
            return p;
        }
        void Clear(Profile p, int cup)
        {
            for (int m = 0; m < library.Cups[cup].Matches.Count; m++) library.Record(p, cup, m, 0, loaner: false, built: true);
        }
        // Wins (0) or losses (1) at your matches until the bracket is done or you're out; the rest by coin.
        // Returns the DP the level-ups on the way paid (EXP is the only other DP a bracket duel moves).
        int Play(Profile p, Bracket b, params int[] results)
        {
            int levels = 0;
            foreach (var r in results)
            {
                Brackets.StartYours(b);
                levels += Brackets.RecordYours(p, library, b, r, loaner: false)!.Reward.LevelUps.Sum(u => u.Dp);
            }
            return levels;
        }
        int LevelDp(BracketResult r) => r.Reward.LevelUps.Sum(u => u.Dp);

        // Entry: blocks, then the fee.
        var you = Champion(20261101);
        var shut = Brackets.EnterBlocked(you, library, 0);
        Clear(you, 0);
        Clear(you, 1);
        you.Dp = Brackets.LocalsEntry - 1;
        var poor = Brackets.EnterBlocked(you, library, 0);
        you.Dp = 5000;
        var b0 = Brackets.EnterLocals(you, library, 0, ratings, "my-goat");
        var same = Brackets.EnterBlocked(you, library, 0);
        var other = Brackets.EnterBlocked(you, library, 1);
        Check($"entry: \"{shut}\" before the clear, \"{poor}\" under {Brackets.LocalsEntry:N0} DP, then the fee (a sink); \"{other}\" at another cup while it runs",
            shut == "Clear to unlock" && poor?.Contains("1,000 DP") == true && b0 != null && you.Dp == 4000 && you.DpSpent[Brackets.Sink] == Brackets.LocalsEntry
            && you.Bracket == b0 && you.BracketsEntered == 1 && you.Locals["goat-cup"].Entries == 1 && same?.Contains("running") == true
            && other == "Goat Cup Locals running" && Brackets.EnterLocals(you, library, 1, ratings) == null && you.Dp == 4000);
        var b = b0!;
        var cpuSeeds = Enumerable.Range(2, 7).Select(s => b.Slots.First(e => e.Seed == s)).ToList();
        Check($"the field: 8 distinct pilots, you seed 1, in bracket order [1,8,4,5 | 2,7,3,6] ({string.Join(", ", b.Slots.Select(e => $"#{e.Seed} {e.Character}"))})",
            b.Name == "Goat Cup Locals" && b.Event == "goat-cup" && b.Slots.Select(e => e.Character).Distinct().Count() == 8 && b.Slots[0].You && b.Slots[0].Seed == 1
            && b.Slots.Select(e => e.Seed).SequenceEqual(Brackets.Order) && cpuSeeds.Zip(cpuSeeds.Skip(1), (x, y) => x.Elo >= y.Elo).All(ok => ok)
            && b.Pair(0) == (0, 1) && b.Pair(4) == (-1, -1) && b.YourMatch() == 0);
        var twin = Champion(20261101);
        Clear(twin, 0);
        twin.Dp = 5000;
        var b1 = Brackets.EnterLocals(twin, library, 0, ratings, "my-goat")!;
        Check("the same salt and entry count give the same bracket (seed, field, every coin)",
            b1.Seed == b.Seed && b1.Slots.Select(e => e.Character + e.Deck).SequenceEqual(b.Slots.Select(e => e.Character + e.Deck))
            && Enumerable.Range(0, 7).All(m => Brackets.Coin(b1.Seed, m) == Brackets.Coin(b.Seed, m))
            && b.Seed == Hash.Mix(you.Collection.Salt, 0xB7ACu ^ 0u, 0u));

        // A draw replays; the seed moves on with each start.
        int duels = you.DuelsPlayed, dp = you.Dp;
        uint first = Brackets.StartYours(b);
        bool live = b.LiveMatch == 0;
        var draw = Brackets.RecordYours(you, library, b, 2, loaner: false)!;
        uint second = Brackets.StartYours(b);
        Check($"a draw replays the match: nothing settled, no DP, a duel counted, a fresh duel seed ({draw.Reward.Summary})",
            live && draw.Replay && !draw.Out && draw.Settled.Count == 0 && b.YourMatch() == 0 && b.Winners.All(w => w < 0) && you.Dp == dp + LevelDp(draw)
            && you.DuelsPlayed == duels + 1 && first == Brackets.DuelSeed(b, 0, 0) && second == Brackets.DuelSeed(b, 0, 1) && first != second);

        // The save round-trips mid-bracket; a save with a live duel replays it (not a loss).
        var winQf = Brackets.RecordYours(you, library, b, 0, loaner: false)!;
        Brackets.StartYours(b);
        var tmp = Path.Combine(Path.GetTempPath(), $"ygo-bracket-{Guid.NewGuid():N}.txt");
        you.Save(tmp);
        var back = Profile.Load(tmp)!;
        File.Delete(tmp);
        var lb = back.Bracket;
        int semiOpp = b.Opponent(b.YourMatch());
        Check($"a win settles the round's other quarter-finals ({string.Join(", ", winQf.Settled)}) and pays no DP until the end ({winQf.Reward.Summary})",
            winQf.Settled.SequenceEqual(new[] { 1, 2, 3 }) && winQf.Reward.Dp == 0 && b.YourMatch() == 4 && b.Placement() == -1 && you.Wins.ContainsKey(b.Slots[1].Character));
        Check("the save round-trips mid-bracket: slots, results, seed, live match and attempt; a live duel replays (no loss), New Game+ keeps it",
            lb != null && lb.Seed == b.Seed && lb.Name == b.Name && lb.Event == b.Event && lb.Kind == "locals" && lb.Loaned == b.Loaned && lb.LiveMatch == 4 && lb.Attempt == b.Attempt
            && lb.Slots.Select(e => (e.Character, e.Deck, e.Elo, e.Seed, e.You)).SequenceEqual(b.Slots.Select(e => (e.Character, e.Deck, Math.Round(e.Elo), e.Seed, e.You)))
            && lb.Winners.SequenceEqual(b.Winners) && lb.YourMatch() == 4 && lb.Opponent(4) == semiOpp && lb.Placement() == -1
            && back.Losses.Count == you.Losses.Count && back.BracketsEntered == 1 && back.Locals["goat-cup"].Entries == 1
            && Run2(back));
        bool Run2(Profile p) { p.NewGamePlus(); return p.Bracket != null && p.Locals.Count == 1; }

        // A loss pays its placement at once and ends your run; skip to the end finishes the bracket.
        var loser = Champion(7);
        Clear(loser, 0);
        loser.Dp = 1000;
        var lbk = Brackets.EnterLocals(loser, library, 0, ratings)!;
        Brackets.StartYours(lbk);
        var lost = Brackets.RecordYours(loser, library, lbk, 1, loaner: true)!;
        int loserDp = 75 + LevelDp(lost);
        bool outNow = lost.Out && lost.Placement == 3 && lost.Reward.Dp == 75 && loser.Dp == loserDp && lbk.YouOut && lbk.YourMatch() == -1
            && Brackets.RecordYours(loser, library, lbk, 0, false) == null && loser.Losses.ContainsKey(lbk.Slots[1].Character);
        var rest = Brackets.SkipToEnd(lbk);
        var lostEnd = Brackets.Finish(loser, library, lbk, prizes)!;
        Check($"a loss pays at once and ends your run ({lost.Reward.Summary}); Skip to the end settles {rest.Count} matches; it pays nothing more",
            outNow && lbk.Done && lost.Settled.Count == 3 && rest.Count == 3 && lostEnd.Dp == 0 && lostEnd.PacksWon == 0 && lostEnd.Packs.Count == 0 && loser.Dp == loserDp
            && loser.Bracket == null && loser.Locals["goat-cup"].Best == 3 && lostEnd.Events.Count == 0);

        // A finalist: 650 at the loss, 1 Prize Pack at the end. A watched final's result counts.
        var finalist = Champion(11);
        Clear(finalist, 0);
        finalist.Dp = 1000;
        var fb = Brackets.EnterLocals(finalist, library, 0, ratings)!;
        int finalistLevels = Play(finalist, fb, 0, 0, 1);
        var fEnd = Brackets.Finish(finalist, library, fb, prizes)!;
        Check($"a finalist: {Brackets.LocalsPay[1]} DP at the loss, 1 Prize Pack of 5 at the end ({fEnd.Summary})",
            fEnd.Placement == 1 && finalist.Dp == Brackets.LocalsPay[1] + finalistLevels && fEnd.Dp == 0 && fEnd.Packs.Count == 1 && fEnd.Packs[0].Count == PrizePacks.Size);
        var watcher = Champion(13);
        Clear(watcher, 0);
        watcher.Dp = 1000;
        var wb = Brackets.EnterLocals(watcher, library, 0, ratings)!;
        Play(watcher, wb, 1);
        Brackets.SimulateRound(wb, 1);
        bool cpuFinal = wb.Pending(6) && !wb.IsYours(6);
        var (fa, fbSide) = wb.Pair(6);
        bool watched = Brackets.SettleWatched(wb, 6, 1) && wb.Champion == fbSide && !Brackets.SettleWatched(wb, 6, 0);
        Check("Follow the bracket: a CPU final can be watched and its real result counts", cpuFinal && watched && wb.Done && fa >= 0);

        // A title: 900 DP and 2 Prize Packs; the first title raises "bracketwin", every one "bracketwin:<name>".
        var winner = Champion(17);
        Clear(winner, 0);
        winner.Dp = 1000;
        var tb = Brackets.EnterLocals(winner, library, 0, ratings)!;
        Play(winner, tb, 0, 0, 0);
        int before = winner.Dp, copies = winner.Collection.Total;
        var title = Brackets.Finish(winner, library, tb, prizes)!;
        var pulls = title.Packs.SelectMany(p => p).ToList();
        Check($"a title pays {Brackets.LocalsPay[0]} DP and 2 Prize Packs, raising {string.Join(" and ", title.Events)} ({title.Summary})",
            title.Placement == 0 && title.Dp == 900 && winner.Dp == before + 900 && title.Packs.Count == 2 && pulls.Count == 10
            && title.Events.SequenceEqual(new[] { "bracketwin", "bracketwin:Goat Cup Locals" }) && winner.Locals["goat-cup"].Titles == 1 && winner.Locals["goat-cup"].Best == 0
            && winner.Bracket == null);
        Check($"prize cards join the collection as Prize copies, paid 0, never sold for DP ({pulls.Count(p => p.Kept)} kept: {string.Join(", ", pulls.Take(5).Select(p => $"{cards.Get(p.Code)?.Name} {Rarities.Name(p.Rarity)}{(p.Slab > 0 ? $" slab {p.Slab}" : "")}"))})",
            winner.Collection.Total == copies + pulls.Count(p => p.Kept) && pulls.All(p => p.Copy == null || (p.Copy.Source == CopySource.Prize && p.Copy.Paid == 0))
            && pulls.Where(p => p.Copy != null).All(p => SellDesk.Blocked(winner, p.Copy!, _ => 0) != null && winner.Collection.SellPrice(p.Copy!) == 0)
            && pulls.Count(p => p.Slab > 0) == 2);
        winner.Dp = 1000;
        var tb2 = Brackets.EnterLocals(winner, library, 0, ratings)!;
        Play(winner, tb2, 0, 0, 0);
        var title2 = Brackets.Finish(winner, library, tb2, null)!;
        Check("a second title raises only its bracket's event", title2.Events.SequenceEqual(new[] { "bracketwin:Goat Cup Locals" }) && winner.Locals["goat-cup"].Titles == 2
            && winner.Locals["goat-cup"].Entries == 2 && tb2.Seed != tb.Seed);

        SizeChecks(cards, root, printings, roster, library, starter, ratings, Check);
        AlleyEntryChecks(cards, root, printings, roster, library, starter, ratings, Check);
        return failures;
    }

    /// <summary>
    /// The alley brackets' entry and leaving (the Phase C review): <see cref="Brackets.AlleySeed"/> is the seed the entry
    /// dialog's preview and <see cref="Brackets.EnterAlley"/> both draw from; a field that can't be drawn enters nothing and
    /// charges nothing; leaving an alley bracket duel (the menu, or a save loaded with it live) is a loss, a best-of-three
    /// final's game too, while a Locals match left live still replays; an alley win never counts for a card unlock; a title's
    /// scene can be caught up.
    /// </summary>
    private static void AlleyEntryChecks(ICardDataProvider cards, string root, PrintingIndex printings, List<Personality> roster, Library library, Deck starter,
        IReadOnlyDictionary<string, double> ratings, Action<string, bool> Check)
    {
        Console.WriteLine("\nAlley brackets: entry, leaving mid-duel, wins (the Phase C review):");
        var a = AlleyTest.Load(cards, root, printings, roster, library);
        var data = a.Data;
        var ctx = a.Ctx;
        Profile Champ(uint salt) => AlleyTest.OrvelOpen(starter, cards, printings, library, salt, 20000);
        string Sig(Bracket b) => string.Join("|", b.Slots.Select(e => $"{e.Seed}:{e.Character}:{e.Deck}:{e.Elo:0}:{e.Splash?.Base}:{e.Splash?.Handle}:{e.Splash?.Tint}:"
            + $"{string.Join(",", e.Splash?.Added ?? new List<uint>())}:{string.Join(",", e.Splash?.Cut ?? new List<uint>())}"));

        // The preview's seed is the entry's: for both kinds, at a few entry counts.
        int mismatches = 0, seeds = 0;
        foreach (uint salt in new uint[] { 0xB3C00001, 0xB3C00002, 0xB3C00003 })
            foreach (var kind in new[] { Brackets.BrawlKind, Brackets.MidnightKind })
            {
                var p = Champ(salt);
                p.BracketsEntered = (int)(salt % 4);
                uint seed = Brackets.AlleySeed(p, kind);
                if (seed == Hash.Mix(p.Collection.Salt, 0xA11Bu ^ (kind == Brackets.MidnightKind ? 1u : 0u), (uint)p.BracketsEntered)) seeds++;
                var preview = Brackets.TryCreateAlley(data, kind, p, ctx, seed, "x");
                var entered = Brackets.EnterAlley(p, data, kind, ctx, "x");
                if (preview == null || entered == null || Sig(preview) != Sig(entered) || entered.Seed != seed) mismatches++;
            }
        Check($"AlleySeed is the entry's seed (6 of 6: {seeds}); the preview drawn from it seats exactly what EnterAlley seats ({mismatches} mismatches)", seeds == 6 && mismatches == 0);

        // A field that can't be drawn (the Brawl one regular short): no bracket, nothing charged, nothing counted (the review's P7).
        var broken = AlleyData.Parse(a.EventsText.Replace("field: splash 3 cleared", "field: splash 2 cleared"), cards);
        var bp = Champ(0xB3C00010);
        int dp0 = bp.Dp, entered0 = bp.BracketsEntered;
        Bracket? brokenPreview = null, brokenEntry = null;
        bool threw = false;
        try
        {
            brokenPreview = Brackets.TryCreateAlley(broken, Brackets.BrawlKind, bp, ctx, Brackets.AlleySeed(bp, Brackets.BrawlKind), "x");
            brokenEntry = Brackets.EnterAlley(bp, broken, Brackets.BrawlKind, ctx, "x");
        }
        catch (Exception) { threw = true; }
        finally { ctx.Data = data; }
        Check($"a field the data can't fill: no preview and EnterAlley returns null without throwing; nothing charged ({bp.Dp - dp0:+0;-0} DP), no entry counted, no bracket",
            !threw && brokenPreview == null && brokenEntry == null && bp.Dp == dp0 && bp.BracketsEntered == entered0 && bp.Bracket == null
            && !bp.DpSpent.ContainsKey(Ledger.AlleyBrackets) && !bp.AlleyBrackets.ContainsKey(Brackets.BrawlKind));

        // Leaving your alley bracket duel for the menu: a loss, recorded as one (the placement paid, the loss counted).
        var lp = Champ(0xB3C00020);
        var lb = Brackets.EnterAlley(lp, data, Brackets.BrawlKind, ctx, "x")!;
        var notLive = Brackets.SettleAbandoned(lp, library);
        Brackets.StartYours(lb);
        var them = lb.Slots[lb.Opponent(lb.YourMatch())].Character;
        int losses0 = lp.Losses.GetValueOrDefault(them), alley0 = lp.DpEarned.GetValueOrDefault(Ledger.Alley), played0 = lp.DuelsPlayed;
        var left = Brackets.SettleAbandoned(lp, library);
        Check($"leaving a live alley bracket duel is a loss (\"{left?.Reward.Summary}\"): out in the quarter-finals, paid {AlleyEconomy.BrawlPay[3]} DP into \"alley\", the loss counted; "
              + "with no duel live it does nothing",
            notLive == null && left != null && left.Out && left.Placement == 3 && lb.YouOut && lb.LiveMatch == -1 && lp.Losses.GetValueOrDefault(them) == losses0 + 1
            && lp.DpEarned.GetValueOrDefault(Ledger.Alley) - alley0 == AlleyEconomy.BrawlPay[3] && left.Reward.Summary.StartsWith("You left the Alley Brawl mid-duel. That counts as a loss.")
            && lp.DuelsPlayed == played0 + 1 && Brackets.SettleAbandoned(lp, library) == null);

        // A best-of-three final's game left: that game is lost, the series goes on.
        var fp = Champ(0xB3C00030);
        var fb = Brackets.EnterAlley(fp, data, Brackets.MidnightKind, ctx, "x")!;
        while (fb.YourMatch() >= 0 && fb.YourMatch() != fb.Final) { Brackets.StartYours(fb); Brackets.RecordYours(fp, library, fb, 0, false); }
        Brackets.StartYours(fb);
        Brackets.RecordYours(fp, library, fb, 0, false);
        Brackets.StartYours(fb);
        var leftGame = Brackets.SettleAbandoned(fp, library);
        bool youA = fb.Pair(fb.Final).a == fb.YourSlot;
        Check($"leaving game 2 of the Midnight Open's final loses that game only (\"{leftGame?.Reward.Summary}\"): 1-1, game 3 next",
            leftGame != null && leftGame.Game == 2 && leftGame.NextGame && !leftGame.Out && !fb.Done && fb.FinalGamesA == 1 && fb.FinalGamesB == 1
            && fb.FinalLastLoser == (youA ? 0 : 1) && fb.YourMatch() == fb.Final && fb.LiveMatch == -1);

        // A save closed mid-duel: it loads with the match live and settles it as a loss (as antelive is); a Locals one replays.
        var cp = Champ(0xB3C00040);
        var cb = Brackets.EnterAlley(cp, data, Brackets.BrawlKind, ctx, "x")!;
        Brackets.StartYours(cb);
        var crashed = AlleyTest.FromText(AlleyTest.SaveText(cp), printings, cards);
        bool liveSaved = crashed.Bracket?.LiveMatch == cb.YourMatch() && cb.YourMatch() >= 0;
        var atLoad = Brackets.SettleAbandoned(crashed, library);
        var after = AlleyTest.FromText(AlleyTest.SaveText(crashed), printings, cards);
        var loc = AlleyTest.Opened(starter, cards, printings, library, 0xB3C00041);
        loc.Earn(Ledger.Debug, 3000);
        var lk = Brackets.EnterLocals(loc, library, 0, ratings, "goat-control")!;
        Brackets.StartYours(lk);
        var locBack = AlleyTest.FromText(AlleyTest.SaveText(loc), printings, cards);
        int locLive = locBack.Bracket?.LiveMatch ?? -2;
        var locSettled = Brackets.SettleAbandoned(locBack, library);
        Check($"a save closed mid-duel in an alley bracket settles at load as a loss (\"{atLoad?.Reward.Summary}\") and saves that way; a Locals match left live is untouched (live {locLive}: a reload replays it)",
            liveSaved && atLoad != null && atLoad.Out && crashed.Bracket!.YouOut && crashed.Bracket.LiveMatch == -1 && after.Bracket?.YouOut == true && after.Bracket.LiveMatch == -1
            && locSettled == null && locBack.Bracket!.LiveMatch == locLive && locLive == lk.YourMatch() && !locBack.Bracket.YouOut);

        // Alley wins never feed the card unlocks (BackAlley.Record's rule): the Midnight Open's named entrants are counted in Wins only.
        var wp = Champ(0xB3C00050);
        var wb = Brackets.EnterAlley(wp, data, Brackets.MidnightKind, ctx, "x")!;
        var named = new List<string>();
        bool winsOk = true;
        while (wb.YourMatch() >= 0 && wb.YourMatch() != wb.Final)
        {
            var who = wb.Slots[wb.Opponent(wb.YourMatch())];
            int w0 = wp.WinsAgainst(who.Character);
            Brackets.StartYours(wb);
            var r = Brackets.RecordYours(wp, library, wb, 0, false)!;
            winsOk &= r.Reward.WinsAdded.Count == 0 && wp.WinsAgainst(who.Character) == w0 + 1;
            if (who.Splash == null) named.Add(who.Character);
        }
        var locWin = Brackets.RecordYours(loc, library, lk, 0, false);
        Check($"an alley bracket win never counts toward a card unlock (no WinsAdded: no bond, gift or drop), a named entrant's either ({string.Join(", ", named)}), "
              + $"though Wins counts it; a Locals win still does ({locWin?.Reward.WinsAdded.Count} added)",
            winsOk && named.Count > 0 && locWin != null && locWin.Reward.WinsAdded.Count == 1);

        // A title's scene caught up: "bracketwin:<name>" for each alley bracket with a title, read without touching the records.
        var tp = Champ(0xB3C00060);
        var none = Brackets.AlleyTitleEvents(tp, data);
        bool noRecords = tp.AlleyBrackets.Count == 0;
        var tb = Brackets.EnterAlley(tp, data, Brackets.BrawlKind, ctx, "x")!;
        while (tb.YourMatch() >= 0) { Brackets.StartYours(tb); Brackets.RecordYours(tp, library, tb, 0, false); }
        Brackets.Finish(tp, library, tb, null, null);
        var due = Brackets.AlleyTitleEvents(tp, data);
        Check($"a title's scene can be caught up (the app closed during its reveal): none before a title, then {string.Join(", ", due)}",
            none.Count == 0 && noRecords && due.SequenceEqual(new[] { "bracketwin:Alley Brawl" }) && tp.AlleyBrackets.Count == 1);
    }

    /// <summary>
    /// Brackets of 8 and 16 and the best-of-three final (docs/dev/ALLEY-SPEC-2026-11-01.md section 11, chunk C3): the shapes,
    /// Order16, Pair, RoundOf and Placement at 16, the Bo3 final played, saved mid-series, a draw replayed, settled when
    /// watched or by its coin, a save that loses a line, and Locals as they were.
    /// </summary>
    private static void SizeChecks(ICardDataProvider cards, string root, PrintingIndex printings, List<Personality> roster, Library library, Deck starter,
        IReadOnlyDictionary<string, double> ratings, Action<string, bool> Check)
    {
        Console.WriteLine("\nBrackets of 8 and 16, the best-of-three final (ALLEY-SPEC section 11):");
        var a = AlleyTest.Load(cards, root, printings, roster, library);
        var data = a.Data;
        var ctx = a.Ctx;

        // The shapes.
        var b8 = new Bracket();
        var b16 = new Bracket(16);
        Check($"8: Size {b8.Size}, {b8.MatchCount} matches, final {b8.Final}, {b8.Rounds} rounds; RoundOf 0-3/4-5/6, MatchesIn, Quarter-final/Semi-final/Final",
            b8.Size == Bracket.LocalsSize && b8.MatchCount == 7 && b8.Final == 6 && b8.Rounds == 3 && b8.Slots.Length == 8 && b8.Winners.Length == 7 && b8.FinalBestOf == 1
            && Enumerable.Range(0, 7).Select(b8.RoundOf).SequenceEqual(new[] { 0, 0, 0, 0, 1, 1, 2 })
            && b8.MatchesIn(0).SequenceEqual(new[] { 0, 1, 2, 3 }) && b8.MatchesIn(1).SequenceEqual(new[] { 4, 5 }) && b8.MatchesIn(2).SequenceEqual(new[] { 6 }) && !b8.MatchesIn(3).Any()
            && b8.RoundName(0) == "Quarter-final" && b8.RoundName(1) == "Semi-final" && b8.RoundName(2) == "Final" && b8.CurrentRound() == 0);
        Check($"16: Size {b16.Size}, {b16.MatchCount} matches, final {b16.Final}, {b16.Rounds} rounds; RoundOf 0-7/8-11/12-13/14, MatchesIn, Round of 16 to Final",
            b16.Size == 16 && b16.MatchCount == 15 && b16.Final == 14 && b16.Rounds == 4 && b16.Slots.Length == 16 && b16.Winners.Length == 15 && b16.Winners.All(w => w < 0)
            && Enumerable.Range(0, 15).Select(b16.RoundOf).SequenceEqual(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 3 })
            && b16.MatchesIn(0).SequenceEqual(Enumerable.Range(0, 8)) && b16.MatchesIn(1).SequenceEqual(new[] { 8, 9, 10, 11 }) && b16.MatchesIn(2).SequenceEqual(new[] { 12, 13 })
            && b16.MatchesIn(3).SequenceEqual(new[] { 14 }) && !b16.MatchesIn(4).Any()
            && b16.RoundName(0) == "Round of 16" && b16.RoundName(1) == "Quarter-final" && b16.RoundName(2) == "Semi-final" && b16.RoundName(3) == "Final");

        // Order16 and Pair: in a bracket where the better seed always wins, round r pairs seeds that sum to size/2^r + 1, so 1 and 2 meet in the final.
        bool Chalk(int size, out string rounds)
        {
            var b = new Bracket(size);
            var order = Brackets.OrderFor(size);
            for (int i = 0; i < size; i++) b.Slots[i] = new BracketEntrant { Character = "s" + order[i], Seed = order[i] };
            bool ok = true;
            var shown = new List<string>();
            for (int r = 0; r < b.Rounds; r++)
            {
                var sums = new List<int>();
                foreach (var m in b.MatchesIn(r))
                {
                    var (x, y) = b.Pair(m);
                    ok &= x >= 0 && y >= 0 && b.Pending(m);
                    if (x < 0 || y < 0) continue;
                    sums.Add(b.Slots[x].Seed + b.Slots[y].Seed);
                    Brackets.Decide(b, m, b.Slots[x].Seed < b.Slots[y].Seed ? 0 : 1);
                }
                ok &= sums.All(sum => sum == (size >> r) + 1);
                shown.Add(string.Join("/", sums.Distinct()));
            }
            rounds = string.Join(", ", shown);
            return ok && b.Done && b.Slots[b.Champion].Seed == 1 && b.Slots[b.Pair(b.Final).a].Seed + b.Slots[b.Pair(b.Final).b].Seed == 3;
        }
        bool chalk16 = Chalk(16, out var sums16), chalk8 = Chalk(8, out var sums8);
        Check($"Order16 is 1,16,8,9,4,13,5,12 | 2,15,7,10,3,14,6,11 (each seed once); OrderFor(8) is Order and OrderFor(16) is Order16; with the better seed always winning "
              + $"each round pairs seeds summing to size/2^r + 1 (16: {sums16}; 8: {sums8}), so 1 and 2 meet only in the final",
            Brackets.Order16.SequenceEqual(new[] { 1, 16, 8, 9, 4, 13, 5, 12, 2, 15, 7, 10, 3, 14, 6, 11 }) && Brackets.Order16.OrderBy(x => x).SequenceEqual(Enumerable.Range(1, 16))
            && Brackets.OrderFor(8) == Brackets.Order && Brackets.OrderFor(16) == Brackets.Order16 && chalk16 && chalk8);

        // Pair, RoundOf and Placement at 16: you (seed 2, slot 8) out in each round in turn, and a title.
        var placements = new List<string>();
        bool placeOk = true;
        for (int outIn = 0; outIn <= 4; outIn++)
        {
            var b = new Bracket(16) { FinalBestOf = 1 };
            for (int i = 0; i < 16; i++) b.Slots[i] = new BracketEntrant { Character = i == 8 ? BracketEntrant.YouId : "s" + Brackets.Order16[i], Seed = Brackets.Order16[i], You = i == 8 };
            bool pairsOk = b.Pair(0) == (0, 1) && b.Pair(7) == (14, 15) && b.Pair(8) == (-1, -1) && b.Pair(14) == (-1, -1) && b.YourSlot == 8 && b.YourMatch() == 4;
            int played = 0;
            for (int round = 0; round < b.Rounds; round++)
                foreach (var m in b.MatchesIn(round))
                {
                    if (!b.Pending(m)) { placeOk = false; continue; }
                    bool yours = b.IsYours(m);
                    int youSide = b.Pair(m).a == 8 ? 0 : 1;
                    if (yours && b.YourMatch() != m) placeOk = false;
                    if (yours) played++;
                    Brackets.Decide(b, m, yours ? (round == outIn ? 1 - youSide : youSide) : 0);
                    if (yours && round == outIn) placeOk &= b.YouOut && b.Placement() == 4 - outIn && b.YourMatch() == -1;
                }
            // Each later match pairs the winners of the two feeding it.
            pairsOk &= Enumerable.Range(8, 7).All(m => b.Pair(m) == (b.Winners[2 * (m - 8)], b.Winners[2 * (m - 8) + 1]));
            int place = b.Placement();
            placements.Add($"{(outIn < 4 ? b.RoundName(outIn) : "title")} -> {place} \"{Brackets.PlacementText(place)}\" after {played} match{(played == 1 ? "" : "es")}");
            placeOk &= pairsOk && b.Done && place == (outIn < 4 ? 4 - outIn : 0) && played == Math.Min(outIn + 1, 4) && b.CurrentRound() == b.Rounds
                       && (outIn < 4 ? b.Champion != 8 : b.Champion == 8);
        }
        Check($"16: Pair feeds each match from the two before it; Placement as you go out in each round ({string.Join("; ", placements)})",
            placeOk && Brackets.PlacementText(4) == "Out in the round of 16" && Brackets.PlacementText(3) == "Out in the quarter-finals");
        Check($"SeriesOdds: best of three is p²(3 - 2p) ({Brackets.SeriesOdds(0.6, 3):0.000} at 0.6), best of one is p, and the two sides sum to 1",
            Math.Abs(Brackets.SeriesOdds(0.6, 3) - 0.648) < 1e-9 && Brackets.SeriesOdds(0.3, 1) == 0.3 && Math.Abs(Brackets.SeriesOdds(0.5, 3) - 0.5) < 1e-12
            && new[] { 0.1, 0.37, 0.8 }.All(p => Math.Abs(Brackets.SeriesOdds(p, 3) + Brackets.SeriesOdds(1 - p, 3) - 1) < 1e-12 && Math.Abs(Brackets.SeriesOdds(p, 3) - p * p * (3 - 2 * p)) < 1e-12));

        // The best-of-three final, played: the Midnight Open from a save that has cleared the Black Market.
        Profile Champ(uint salt)
        {
            var p = AlleyTest.OrvelOpen(starter, cards, printings, library, salt, 20000);
            return p;
        }
        var mp = Champ(0xB3B30001);
        var mb = Brackets.EnterAlley(mp, data, Brackets.MidnightKind, ctx, "fire-king-snake-eye")!;
        int youSlot = mb.YourSlot;
        while (mb.YourMatch() >= 0 && mb.YourMatch() != mb.Final)
        {
            Brackets.StartYours(mb);
            Brackets.RecordYours(mp, library, mb, 0, false);
        }
        bool toFinal = mb.YourMatch() == mb.Final && mb.FinalBestOf == 3 && mb.GamesToWin == 2 && mb.FinalGamesA + mb.FinalGamesB == 0 && mb.FinalLastLoser == -1;
        bool youA = mb.Pair(mb.Final).a == youSlot;
        uint g1Seed = Brackets.StartYours(mb);
        var g1 = Brackets.RecordYours(mp, library, mb, 0, false)!;
        int Yours(Bracket b) => youA ? b.FinalGamesA : b.FinalGamesB;
        int Theirs(Bracket b) => youA ? b.FinalGamesB : b.FinalGamesA;
        Check($"Bo3: game 1 won is one game, not the match ({g1.Reward.Summary}): Game 1, NextGame, 1-0, the loser's side chooses next, the match still yours to play",
            toFinal && g1.Game == 1 && g1.NextGame && !g1.Out && !g1.Replay && !mb.Done && Yours(mb) == 1 && Theirs(mb) == 0 && mb.FinalLastLoser == (youA ? 1 : 0)
            && mb.YourMatch() == mb.Final && g1.Reward.Dp == 0 && g1.Settled.Count == 0);
        // Saved and loaded mid-series, with game 2 live.
        uint g2Seed = Brackets.StartYours(mb);
        var text = AlleyTest.SaveText(mp);
        var back = AlleyTest.FromText(text, printings, cards);
        var lb = back.Bracket!;
        var gamesLine = text.Split('\n').FirstOrDefault(l => l.StartsWith("bracketgames "))?.Trim() ?? "";
        int splashLines = text.Split('\n').Count(l => l.StartsWith("bracketsplash "));
        Check($"Bo3 save mid-series: \"bracketsize 16\", \"{gamesLine}\", {splashLines} bracketsplash lines; it loads as 16, bo3, the same games, last loser, live game, attempt, results "
              + "and seats (splash builds and their decks)",
            text.Contains("bracketsize 16") && gamesLine == $"bracketgames {mb.FinalGamesA} {mb.FinalGamesB} 3 {mb.FinalLastLoser}" && splashLines == 5
            && lb.Size == 16 && lb.FinalBestOf == 3 && lb.Kind == Brackets.MidnightKind && lb.FinalGamesA == mb.FinalGamesA && lb.FinalGamesB == mb.FinalGamesB
            && lb.FinalLastLoser == mb.FinalLastLoser && lb.LiveMatch == mb.Final && lb.Attempt == mb.Attempt && lb.Winners.SequenceEqual(mb.Winners) && lb.Seed == mb.Seed
            && lb.Slots.Select(e => e.Seed).SequenceEqual(Brackets.Order16) && lb.YourMatch() == lb.Final
            && lb.Slots.Zip(mb.Slots).All(z => z.First.Deck == z.Second.Deck && z.First.Character == z.Second.Character && (z.First.Splash == null) == (z.Second.Splash == null)
                && (z.First.Splash == null || (z.First.Splash.Base == z.Second.Splash!.Base && z.First.Splash.Handle == z.Second.Splash.Handle && z.First.Splash.From == z.Second.Splash.From
                    && z.First.Splash.Tint == z.Second.Splash.Tint && z.First.Splash.Added.SequenceEqual(z.Second.Splash.Added) && z.First.Splash.Cut.SequenceEqual(z.Second.Splash.Cut))))
            && Enumerable.Range(0, 16).Where(i => mb.Slots[i].Splash != null)
                .All(i => BackAlley.ResolveDeck(back, lb.Slots[i].Deck, ctx)?.Main.SequenceEqual(BackAlley.ResolveDeck(mp, mb.Slots[i].Deck, ctx)!.Main) == true)
            && g1Seed != g2Seed);
        // On the loaded save: a drawn game replays, game 2 lost, game 3 won.
        int drawAttempt = lb.Attempt;
        uint replaySeed = Brackets.StartYours(lb);
        var draw = Brackets.RecordYours(back, library, lb, 2, false)!;
        bool drawOk = draw.Replay && draw.Game == 0 && !draw.NextGame && !draw.Out && lb.FinalGamesA + lb.FinalGamesB == 1 && lb.YourMatch() == lb.Final && !lb.Done
                      && replaySeed != g2Seed && Brackets.StartYours(lb) != replaySeed && lb.Attempt == drawAttempt + 2;
        var g2 = Brackets.RecordYours(back, library, lb, 1, false)!;
        bool g2Ok = g2.Game == 2 && g2.NextGame && !g2.Out && Yours(lb) == 1 && Theirs(lb) == 1 && lb.FinalLastLoser == (youA ? 0 : 1) && g2.Reward.Dp == 0;
        Brackets.StartYours(lb);
        var g3 = Brackets.RecordYours(back, library, lb, 0, false)!;
        Check($"Bo3 on the loaded save: a drawn game replays ({draw.Reward.Summary}: Game 0, the games unchanged, a fresh duel seed); game 2 lost ({g2.Reward.Summary}); "
              + $"game 3 won decides it 2-1 ({g3.Reward.Summary})",
            drawOk && g2Ok && g3.Game == 3 && !g3.NextGame && lb.Done && lb.Champion == youSlot && lb.Placement() == 0 && Yours(lb) == 2 && Theirs(lb) == 1 && lb.YourMatch() == -1);
        // A final lost 0-2: out as the finalist, paid at the loss into "alley".
        var lp = Champ(0xB3B30002);
        var lbk = Brackets.EnterAlley(lp, data, Brackets.MidnightKind, ctx)!;
        while (lbk.YourMatch() >= 0 && lbk.YourMatch() != lbk.Final) { Brackets.StartYours(lbk); Brackets.RecordYours(lp, library, lbk, 0, false); }
        int alleyBefore = lp.DpEarned.GetValueOrDefault(Ledger.Alley);
        Brackets.StartYours(lbk);
        var l1 = Brackets.RecordYours(lp, library, lbk, 1, false)!;
        Brackets.StartYours(lbk);
        var l2 = Brackets.RecordYours(lp, library, lbk, 1, false)!;
        Check($"Bo3 lost 0-2: game 1 goes on ({l1.Reward.Summary}), game 2 ends it as the finalist ({l2.Reward.Summary}), {AlleyEconomy.OpenPay[1]} DP into \"alley\" at the loss",
            l1.NextGame && !l1.Out && l2.Game == 2 && l2.Out && l2.Placement == 1 && l2.Reward.Dp == AlleyEconomy.OpenPay[1] && lbk.Done && lbk.Placement() == 1
            && lp.DpEarned.GetValueOrDefault(Ledger.Alley) - alleyBefore == AlleyEconomy.OpenPay[1] && !lp.DpEarned.ContainsKey(Ledger.Locals));

        // A CPU final, watched or settled by its coin: you go out in the round of 16.
        var matchups = Matchups.Load(Path.Combine(root, "campaign", Matchups.FileName));
        Bracket CpuFinal(uint seed)
        {
            var cb = Brackets.CreateAlley(data, Brackets.MidnightKind, Champ(0xB3B30003), ctx, seed);
            for (int r = 0; r < cb.Rounds - 1; r++)
                foreach (var m in cb.MatchesIn(r)) Brackets.Decide(cb, m, cb.IsYours(m) ? (cb.Pair(m).a == cb.YourSlot ? 1 : 0) : 0);
            return cb;
        }
        var w = CpuFinal(77);
        var (fa, fb) = w.Pair(w.Final);
        double p0 = Brackets.Odds(w.Slots[fa], w.Slots[fb], matchups);
        double coin = Brackets.Coin(w.Seed, w.Final);
        bool coinBefore = Brackets.CoinSide(w, w.Final, matchups) == (coin < Brackets.SeriesOdds(p0, 3) ? 0 : 1);
        bool one = Brackets.SettleWatched(w, w.Final, 0, matchups);
        bool coinAfter = Brackets.CoinSide(w, w.Final, matchups) == (coin < p0 * (2 - p0) ? 0 : 1);
        bool drawn = Brackets.SettleWatched(w, w.Final, 2, matchups);
        bool notYet = !w.Done && w.FinalGamesA == 1 && w.FinalGamesB == 0;
        bool two = Brackets.SettleWatched(w, w.Final, 1, matchups), three = Brackets.SettleWatched(w, w.Final, 1, matchups);
        bool watched = one && !drawn && notYet && two && three && w.Done && w.Champion == fb && w.FinalGamesA == 1 && w.FinalGamesB == 2 && w.FinalLastLoser == 0
                       && !Brackets.SettleWatched(w, w.Final, 0, matchups) && w.Placement() == 4;
        var skipped = CpuFinal(77);
        var settled = Brackets.SkipToEnd(skipped, matchups);
        Check($"a watched CPU Bo3 final counts each game (1-0, a draw counts nothing, then 1-1 and 1-2: {w.Slots[fb].Splash?.Handle ?? w.Slots[fb].Character} takes it); "
              + $"its coin is one coin against the series odds from the games counted (p {p0:0.00}: {Brackets.SeriesOdds(p0, 3):0.000} at 0-0, {p0 * (2 - p0):0.000} at 1-0); "
              + "Skip to the end settles it with that coin",
            watched && coinBefore && coinAfter && settled.SequenceEqual(new[] { skipped.Final }) && skipped.Done
            && skipped.Champion == (coin < Brackets.SeriesOdds(p0, 3) ? fa : fb) && skipped.FinalGamesA + skipped.FinalGamesB == 0);

        // A save that lost a line drops the bracket (no crash): a splash regular's build, or the size line of a 16.
        string Without(string t, Func<string, bool> drop) => string.Join("\n", t.Split('\n').Where(l => !drop(l)));
        var noSplash = AlleyTest.FromText(Without(text, l => l.StartsWith("bracketsplash ") && l.Split(' ')[1] == Array.FindIndex(mb.Slots, e => e.Splash != null).ToString()), printings, cards);
        var noSize = AlleyTest.FromText(Without(text, l => l.StartsWith("bracketsize ")), printings, cards);
        var noGames = AlleyTest.FromText(Without(text, l => l.StartsWith("bracketgames ")), printings, cards);
        Check("a save whose splash regular lost its bracketsplash line, or a 16 that lost its bracketsize line, drops the bracket; one without bracketgames plays a best-of-one final",
            noSplash.Bracket == null && noSize.Bracket == null && noGames.Bracket != null && noGames.Bracket.FinalBestOf == 1 && noGames.Bracket.Size == 16
            && noSplash.AlleyBrackets[Brackets.MidnightKind].Entries == 1 && noSplash.BracketsEntered == mp.BracketsEntered);

        // Locals unchanged: 8, best of one, Order, the "brackets" sink; its save writes none of the new lines and an old one loads.
        var loc = AlleyTest.Opened(starter, cards, printings, library, 0xB3B30004);
        loc.Earn(Ledger.Debug, 3000);
        var lk = Brackets.EnterLocals(loc, library, 0, ratings, "goat-control")!;
        Brackets.StartYours(lk);
        Brackets.RecordYours(loc, library, lk, 0, false);
        var ltext = AlleyTest.SaveText(loc);
        var lback = AlleyTest.FromText(ltext, printings, cards);
        var lold = AlleyTest.FromText(Without(ltext, l => l.StartsWith("bracketsize") || l.StartsWith("bracketgames") || l.StartsWith("bracketsplash")), printings, cards);
        Check("Locals unchanged: 8 seats in Order, best of one, the entry to \"brackets\"; its save has no bracketsize, bracketgames or bracketsplash line, and loads (as a Phase B save does) mid-bracket",
            lk.Size == 8 && lk.FinalBestOf == 1 && lk.Kind == Brackets.LocalsKind && lk.Slots.Select(e => e.Seed).SequenceEqual(Brackets.Order) && lk.Slots.All(e => e.Splash == null)
            && loc.DpSpent[Ledger.Brackets] == Brackets.LocalsEntry && !loc.DpSpent.ContainsKey(Ledger.AlleyBrackets)
            && !ltext.Contains("bracketsize") && !ltext.Contains("bracketgames") && !ltext.Contains("bracketsplash")
            && lback.Bracket is { Size: 8, FinalBestOf: 1 } && lback.Bracket.Winners.SequenceEqual(lk.Winners) && lback.Bracket.YourMatch() == 4
            && lold.Bracket is { Size: 8 } && lold.Bracket.YourMatch() == 4 && lold.Bracket.Kind == Brackets.LocalsKind);
    }
}
