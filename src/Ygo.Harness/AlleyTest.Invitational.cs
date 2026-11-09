using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--campaign-test`'s and `--nolimits-test`'s checks of the No Limits Invitational and Moth's restored shelf slots
/// (docs/dev/NL2-INVITATIONAL-BUILD-2026-11-05.md, C3; docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 9 and 14.4; NO-LIMITS-ECONOMY 3, 4 and 6):
/// the numbers against <see cref="AlleyEconomy"/>, <see cref="Brackets.Opening"/>'s states, seeding, the twin rule and the data's bites,
/// <see cref="Brackets.AlleySeed"/>'s per-kind salt, <see cref="Brackets.RulesFor"/>, the No Limits Pack's pool, a title's ledger at every
/// placement, the poster, the Library engraving's partner test, the story, and the shelf (a sixth back at most, the restored slots only with the
/// wall open and ready builds, a save without the wall unchanged). The real data has no ready build, so these force builds ready in their own
/// parsed copy (<see cref="ReadyInvitational"/>), never in the file.
/// </summary>
public static partial class AlleyTest
{
    /// <summary>
    /// The Invitational's field wants all eleven builds ready; wave 2's decks (Remy, Margo, Lux, Inka) aren't written yet, so this forces
    /// every build ready in a copy of the data whether its deck exists or not. The entrants of the missing decks sit unrated
    /// (<see cref="DeckRatings.Unrated"/>) unless a caller gives them stand-ins (<see cref="WithRatings"/>).
    /// </summary>
    public static Setup ReadyInvitational(Setup s, ICardDataProvider cards) => ReadyNoLimits(s, cards, allBuilds: true);

    /// <summary>The same setup over other deck ratings (the same cards, decks and parsed data; its own pool cache).</summary>
    public static Setup WithRatings(Setup s, IReadOnlyDictionary<string, double> ratings)
    {
        var o = s.Ctx;
        var ctx = new AlleyContext
        {
            Cards = o.Cards, Printings = o.Printings, Lists = o.Lists, List = o.List, NoLimits = o.NoLimits, LoadDeck = o.LoadDeck, Ratings = ratings,
            Curated = o.Curated, Roster = o.Roster, Library = o.Library, Data = s.Data,
        };
        return new Setup { Data = s.Data, Ctx = ctx, Traditional = s.Traditional, List = s.List, Lists = s.Lists, EventsText = s.EventsText, Root = s.Root };
    }

    /// <summary>EXP past level 99: a duel's level-up pays no DP, as the arbitrage loops start.</summary>
    public const int TopExp = 50_000_000;

    /// <summary>No Limits cleared (the Invitational's `opens:`), on a save that has the wall open already (<see cref="NoLimitsOpen"/>).</summary>
    public static Profile ClearNoLimits(Profile p)
    {
        if (!p.AlleyTables.TryGetValue("no-limits", out var st)) p.AlleyTables["no-limits"] = st = new AlleyTableState();
        st.Cleared = true;
        return p;
    }

    /// <summary>
    /// One Invitational played through the engine up to the end, not yet finished: you win every duel until the round you lose in (-1: a title;
    /// round 3, the final, is lost 0-2). The bracket is done and still in <see cref="Profile.Bracket"/>.
    /// </summary>
    public static (Bracket bracket, BracketResult? lost, int duels) PlayInvitationalToEnd(Profile q, Setup on, int loseIn, Library library, string yourDeck = "x")
    {
        var b = Brackets.EnterAlley(q, on.Data, Brackets.InvitationalKind, on.Ctx, yourDeck)
                ?? throw new InvalidOperationException($"the Invitational can't be entered: {Brackets.EnterBlocked(q, on.Data, Brackets.InvitationalKind) ?? "its field can't be drawn"} (DP {q.Dp:N0})");
        BracketResult? lost = null;
        int duels = 0;
        while (b.YourMatch() >= 0)
        {
            Brackets.StartYours(b);
            var r = Brackets.RecordYours(q, library, b, b.RoundOf(b.YourMatch()) == loseIn ? 1 : 0, false)!;
            duels++;
            if (r.Out) lost = r;
        }
        Brackets.SkipToEnd(b);
        return (b, lost, duels);
    }

    /// <summary>
    /// One Invitational played and finished (<see cref="PlayInvitationalToEnd"/>, then <see cref="Brackets.Finish"/>, which opens its No Limits Packs from
    /// <paramref name="noLimits"/>; Contraband Packs <paramref name="contraband"/> only for another kind). Throws when it can't finish (no pack source).
    /// </summary>
    public static (Bracket bracket, BracketResult? lost, BracketReward end, int duels) PlayInvitational(Profile q, Setup on, Library library, ContrabandPacks? contraband,
        ContrabandPacks? noLimits, int loseIn, string yourDeck = "x")
    {
        var (b, lost, duels) = PlayInvitationalToEnd(q, on, loseIn, library, yourDeck);
        var end = Brackets.Finish(q, library, b, null, contraband, noLimits)
                  ?? throw new InvalidOperationException($"the Invitational didn't finish after {duels} duels (done {b.Done}, placement {b.Placement()}): {Brackets.FinishBlocked(b, noLimits)}");
        return (b, lost, end, duels);
    }

    /// <summary>The text with <paramref name="old"/> replaced by <paramref name="with"/> once, in the block that starts at <paramref name="header"/> (not an earlier block's twin line).</summary>
    private static string InBlock(string text, string header, string old, string with)
    {
        int start = text.IndexOf(header, StringComparison.Ordinal);
        int at = start < 0 ? -1 : text.IndexOf(old, start, StringComparison.Ordinal);
        return at < 0 ? text : text.Substring(0, at) + with + text.Substring(at + old.Length);
    }

    /// <summary>Wave 1's No Limits Pack pool with the wave-1 builds forced ready (docs/dev/NL2-INVITATIONAL-BUILD-2026-11-05.md; C1's count). Recount it in the change that moves it.</summary>
    private const int NoLimitsPackPoolWave1 = 44;

    private static void InvitationalChecks(Setup s, Setup nl, Deck starter, ICardDataProvider cards, PrintingIndex printings, Library library, List<Personality> roster,
        string root, Action<string, bool> check, HashSet<string> events)
    {
        int added = 0;
        void Check(string what, bool ok) { added++; check(what, ok); }
        Console.WriteLine("\nThe No Limits Invitational and Moth's restored slots (NL2 build plan, C3):");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var real = s.Data;
        var inv = ReadyInvitational(s, cards);
        string Name(uint c) => cards.Get(c)?.Name ?? c.ToString();
        int none(uint _) => 0;
        const string Kind = Brackets.InvitationalKind, Header = "bracket: No Limits Invitational";
        var spec = real.Brackets.FirstOrDefault(x => x.Kind == Kind);
        var packs = s.Packs;
        var nlPacks = ContrabandPacks.NoLimits(nl.Data, nl.Ctx);
        var nlPool = nlPacks.Pool.ToHashSet();

        // ---- I1. The numbers ----
        {
            var notLocals = new[] { Brackets.BrawlKind, Brackets.MidnightKind };
            Check($"I1. the numbers: the data's bracket ({spec?.Name}: {spec?.Size} seats, opens {spec?.Opens}, entry {spec?.Entry:N0}, Bo3 final, pay {string.Join("/", spec?.Pay ?? new int[0])}, packs {string.Join("/", spec?.Packs ?? new int[0])}, "
                  + $"{spec?.Tokens} tokens) is AlleyEconomy's (entry 3,000, pay 1,500/1,000/600/300/100, packs 4/3/2/1/0, 2 tokens on the first title only, key {Brackets.InvitationalTokenKey}); "
                  + $"the ledger kinds are {Ledger.NoLimitsEntry} and {Ledger.NoLimits} (the Brawl's and the Open's stay {Ledger.AlleyBrackets} and {Ledger.Alley}); the pack is the \"No Limits Pack\"",
                spec != null && spec.Name == "No Limits Invitational" && spec.Size == 16 && spec.Opens == "No Limits" && spec.FinalBestOf == 3 && spec.Entry == 3000
                && spec.Pay.SequenceEqual(new[] { 1500, 1000, 600, 300, 100 }) && spec.Packs.SequenceEqual(new[] { 4, 3, 2, 1, 0 }) && spec.Tokens == 2
                && AlleyEconomy.InvitationalEntry == 3000 && AlleyEconomy.InvitationalTokens == 2 && AlleyEconomy.InvitationalPay.SequenceEqual(spec.Pay) && AlleyEconomy.InvitationalPacks.SequenceEqual(spec.Packs)
                && Brackets.EntryFor(Kind) == 3000 && Brackets.PayFor(Kind).SequenceEqual(spec.Pay) && Brackets.PacksFor(Kind).SequenceEqual(spec.Packs) && Brackets.TokensFor(Kind) == 2
                && Brackets.TokenKeyFor(Kind) == "invitational:token" && Brackets.InvitationalTokenKey == "invitational:token" && Brackets.PackNameFor(Kind) == "No Limits Pack"
                && Brackets.PayLedger(Kind) == "nolimits" && Brackets.EntryLedger(Kind) == "nolimits.entry" && Ledger.NoLimits == "nolimits" && Ledger.NoLimitsEntry == "nolimits.entry"
                && Brackets.IsAlleyKind(Kind) && !Brackets.IsAlleyKind(Brackets.LocalsKind) && Brackets.AlleyKinds.SequenceEqual(new[] { "brawl", "midnight", "invitational" })
                && notLocals.All(k => Brackets.PayLedger(k) == Ledger.Alley && Brackets.EntryLedger(k) == Ledger.AlleyBrackets && Brackets.PackNameFor(k) == "Contraband Pack")
                && Brackets.PayLedger(Brackets.LocalsKind) == Ledger.Locals && Brackets.EntryLedger(Brackets.LocalsKind) == Brackets.Sink && Brackets.PackNameFor(Brackets.LocalsKind) == "Prize Pack"
                && Brackets.TokenKeyFor(Brackets.MidnightKind) == Brackets.OpenTokenKey && Brackets.TokensFor(Brackets.BrawlKind) == 0 && Brackets.TokenKeyFor(Brackets.BrawlKind) == "");
        }

        // ---- I2. Opening's states, in order ----
        {
            var closed = Profile.New(starter, cards, printings);
            var shut = Brackets.Opening(closed, real, Kind);
            var onlyAlley = Opened(starter, cards, printings, library, 0xA11E2001);
            var soon = Brackets.Opening(ClearNoLimits(NoLimitsOpen(starter, cards, printings, library, 0xA11E2002)), real, Kind);
            var soonFields = real.Brackets.First(x => x.Kind == Kind);
            var wave1 = Brackets.Opening(NoLimitsOpen(starter, cards, printings, library, 0xA11E2003), nl.Data, Kind);          // not cleared, wave 2 not ready: still Coming soon
            var wave1Cleared = Brackets.Opening(ClearNoLimits(NoLimitsOpen(starter, cards, printings, library, 0xA11E2003)), nl.Data, Kind);
            var locked = Brackets.Opening(NoLimitsOpen(starter, cards, printings, library, 0xA11E2004), inv.Data, Kind);
            var openP = ClearNoLimits(NoLimitsOpen(starter, cards, printings, library, 0xA11E2005));
            var open = Brackets.Opening(openP, inv.Data, Kind);
            var wave2 = new[] { "remy", "margo", "lux", "inka" };
            Check($"I2. Opening, in order: the alley shut says \"{shut.Why}\" (not Coming soon, whatever the data); with the alley open and No Limits cleared the real data says \"{soon.Why}\" "
                  + $"naming all {soon.NotReady.Count} builds; wave 1 ready names {string.Join(", ", wave1.NotReady)} and says Coming soon before the lock line, cleared or not; all ready and No Limits not cleared the lock says "
                  + $"\"{locked.Why}\"; cleared it is open (Why \"{open.Why}\"); FieldReady follows the builds",
                !shut.Open && !shut.ComingSoon && shut.Why == "The alley isn't open." && shut.NotReady.Count == 0 && Brackets.EnterBlocked(closed, real, Kind) == "The alley isn't open."
                && !Brackets.Opening(onlyAlley, real, Kind).Open && Brackets.Opening(onlyAlley, real, Kind).ComingSoon
                && soon.ComingSoon && !soon.Open && soon.Why == "Coming soon" && soon.NotReady.Count == 11 && soonFields.Field.Count == 16 && !Brackets.FieldReady(real, soonFields)
                && soon.NotReady.OrderBy(x => x).SequenceEqual(real.Builds.Select(b => b.Id).OrderBy(x => x)) && Brackets.EnterBlocked(NoLimitsOpen(starter, cards, printings, library, 0xA11E2002), real, Kind) == "Coming soon"
                && wave1.ComingSoon && wave1.Why == "Coming soon" && wave1.NotReady.OrderBy(x => x).SequenceEqual(wave2.OrderBy(x => x)) && wave1Cleared.ComingSoon && !wave1Cleared.Open
                && !locked.Open && !locked.ComingSoon && locked.Why == "Clear No Limits to unlock" && locked.NotReady.Count == 0 && Brackets.FieldReady(inv.Data, inv.Data.Brackets.First(x => x.Kind == Kind))
                && open.Open && !open.ComingSoon && open.Why == "" && open.NotReady.Count == 0 && Brackets.EnterBlocked(openP, inv.Data, Kind) == null);
            // The older brackets open as they always did, with the Invitational waiting.
            var brawlOpen = Brackets.Opening(NoLimitsOpen(starter, cards, printings, library, 0xA11E2006), real, Brackets.BrawlKind);
            var midnightOpen = Brackets.Opening(NoLimitsOpen(starter, cards, printings, library, 0xA11E2006), real, Brackets.MidnightKind);
            var fresh = Opened(starter, cards, printings, library, 0xA11E2007);
            fresh.Dp = AlleyEconomy.InvitationalEntry - 1;
            var poor = Brackets.EnterBlocked(ClearNoLimits(fresh), inv.Data, Kind);
            var running = ClearNoLimits(NoLimitsOpen(starter, cards, printings, library, 0xA11E2008));
            var runB = Brackets.EnterAlley(running, inv.Data, Kind, inv.Ctx, "x");
            var otherSays = Brackets.EnterBlocked(running, inv.Data, Brackets.BrawlKind);
            var sameSays = Brackets.EnterBlocked(running, inv.Data, Kind);
            Check($"I2. the older brackets are open as before beside the waiting Invitational (Brawl {brawlOpen.Open}, Open {midnightOpen.Open}); short of the entry it says \"{poor}\"; one running it says \"{otherSays}\" at another kind "
                  + $"and \"{sameSays}\" at its own; a second entry is refused",
                brawlOpen.Open && midnightOpen.Open && poor == $"Entry is 3,000 DP; you have {AlleyEconomy.InvitationalEntry - 1:N0}." && runB != null && running.Bracket == runB
                && otherSays == "No Limits Invitational running" && sameSays == "Your No Limits Invitational bracket is running." && Brackets.EnterAlley(running, inv.Data, Kind, inv.Ctx, "x") == null
                && running.DpSpent.GetValueOrDefault(Ledger.NoLimitsEntry) == 3000);
        }

        // ---- I3. Seeding, the field and the rules of the data ----
        {
            var p = ClearNoLimits(NoLimitsOpen(starter, cards, printings, library, 0xA11E2010));
            var b = Brackets.CreateAlley(inv.Data, Kind, p, inv.Ctx, Brackets.AlleySeed(p, Kind), "x");
            var others = b.Slots.Where(e => !e.You && e.Character != "remy").OrderBy(e => e.Seed).ToList();
            var order = Brackets.OrderFor(16);
            int slotOf(int seed) => Array.FindIndex(b.Slots, e => e.Seed == seed);
            var pilots = inv.Data.Builds.ToDictionary(h => h.Id, h => h.Deck);
            var fieldNames = spec!.Field.Select(AlleyBracketSpec.ParseField).Select(f => f.words[0]).Where(w => w != "you").OrderBy(w => w).ToList();
            var again = Brackets.CreateAlley(inv.Data, Kind, p, inv.Ctx, Brackets.AlleySeed(p, Kind), "x");
            Check($"I3. seeding: {b.Slots.Length} seats, Remy seed {b.Slots.First(e => e.Character == "remy").Seed}, you {b.Slots.First(e => e.You).Seed}, the other {others.Count} seeded {others.First().Seed}-{others.Last().Seed} by Elo "
                  + $"({string.Join(" ", others.Select(e => $"{e.Character} {e.Elo:0}"))}); Remy and you are in opposite halves (meet only in the final); every build pilot plays its own build's deck, no regular, no out-of-towner; the field is the data's {fieldNames.Count} names, seeds 1-16 once each, the draw is the same twice",
                b.Size == 16 && b.FinalBestOf == 3 && b.Kind == Kind && b.Slots.Select(e => e.Seed).OrderBy(x => x).SequenceEqual(Enumerable.Range(1, 16))
                && b.Slots.First(e => e.Character == "remy").Seed == 1 && b.Slots.First(e => e.You).Seed == 2 && others.Select(e => e.Seed).SequenceEqual(Enumerable.Range(3, 14))
                && others.Zip(others.Skip(1), (x, y) => x.Elo >= y.Elo).All(ok => ok) && (slotOf(1) < 8) != (slotOf(2) < 8) && order.Length == 16
                && b.Slots.Where(e => !e.You).All(e => e.Splash == null && (!pilots.TryGetValue(e.Character, out var d) || e.Deck == d)) && BackAlley.OutOfTowners(b, inv.Data).Count == 0
                && b.Slots.Where(e => !e.You).Select(e => e.Character).OrderBy(x => x).SequenceEqual(fieldNames)
                && again.Slots.Select(e => $"{e.Character}/{e.Deck}/{e.Seed}").SequenceEqual(b.Slots.Select(e => $"{e.Character}/{e.Deck}/{e.Seed}")));
            // The alley names sit on the Appendix's decks, the eleven builds on their own.
            var names = new[] { "radiant-typhoon", "power-patron", "nyx", "nocturne" };
            Check($"I3. the four alley names ({string.Join(", ", names)}) sit among the {b.Slots.Count(e => !e.You && !pilots.ContainsKey(e.Character))} non-build seats, the eleven builds on {string.Join(", ", pilots.Values.Take(3))}...",
                names.All(n => b.Slots.Any(e => e.Character == n)) && b.Slots.Count(e => !e.You && pilots.ContainsKey(e.Character)) == 11 && b.Slots.Count(e => !e.You && !pilots.ContainsKey(e.Character)) == 4);
        }
        {
            // The twin rule and the numbers bite through Problems.
            List<string> ProblemsOf(string text) => AlleyData.Parse(text, cards).Problems(s.Ctx);
            var realProblems = ProblemsOf(s.EventsText);
            bool Mine(string line) => line.Contains("No Limits Invitational");
            var twinText = InBlock(s.EventsText, Header, "field: nocturne hat", "field: nocturne snake-eye");
            var twin = ProblemsOf(twinText).Where(x => x.Contains("both in the field")).ToList();
            var entry = ProblemsOf(EditBlock(s.EventsText, Header, "entry", _ => "2500")).Where(x => Mine(x) && x.Contains("differ from AlleyEconomy's")).ToList();
            var tokens = ProblemsOf(EditBlock(s.EventsText, Header, "tokens", _ => "1 first")).Where(x => Mine(x) && x.Contains("differ from AlleyEconomy's")).ToList();
            var cap = ProblemsOf(EditBlock(s.EventsText, Header, "pay", _ => "1800 1000 600 300 100")).Where(x => Mine(x) && x.Contains("a duel")).ToList();
            var kind = ProblemsOf(EditBlock(s.EventsText, Header, "kind", _ => "gala")).Where(x => Mine(x) && x.Contains("unknown kind")).ToList();
            var twice = ProblemsOf(s.EventsText.Replace(Header, "bracket: Second Invitational\nkind: invitational\nsize: 16\nopens: No Limits\nentry: 3000\nfinal: bo3\npay: 1500 1000 600 300 100\npacks: 4 3 2 1 0\ntokens: 2 first\n\n" + Header))
                .Where(x => x.Contains("kind invitational used twice")).ToList();
            var noDeckText = InBlock(s.EventsText, Header, "field: junia nl-snake-eye", "field: junia");
            var noDeckLine = ProblemsOf(noDeckText).Where(x => Mine(x) && x.Contains("junia")).ToList();
            var readyNoDeck = inv.Data.Problems(inv.Ctx).Where(x => x.Contains("field: remy has no deck nl-kewl-tune")).ToList();
            Check($"I3. the data bites: a build beside its base \"{twin.FirstOrDefault()}\"; entry 2,500 \"{entry.FirstOrDefault()}\"; 1 token \"{tokens.FirstOrDefault()?.Substring(0, Math.Min(60, tokens.FirstOrDefault()?.Length ?? 0))}\"; "
                  + $"a title of 1,800 \"{cap.FirstOrDefault()}\"; an unknown kind; a second invitational; a ready field build with no deck \"{readyNoDeck.FirstOrDefault()}\"; a build pilot's field line with no deck \"{noDeckLine.FirstOrDefault()}\"; none of them in the real file ({realProblems.Count} problems)",
                realProblems.Count == 0 && twin.Count == 1 && twin[0].Contains("nl-snake-eye") && entry.Count == 1 && tokens.Count == 1 && cap.Count >= 1 && kind.Count == 1 && twice.Count >= 1 && readyNoDeck.Count == 1 && noDeckLine.Count >= 1);
        }

        // ---- I4. AlleySeed per kind ----
        {
            bool same = true, apart = true;
            int tried = 0;
            for (uint salt = 1; salt <= 20; salt++)
                for (int entered = 0; entered <= 5; entered++)
                {
                    var p = Opened(starter, cards, printings, library, 0xA11E2020 + salt);
                    p.BracketsEntered = entered;
                    // The seeds the Brawl and the Open drew before the Invitational existed (the salt table was a ternary on the Open).
                    uint brawl0 = Hash.Mix(p.Collection.Salt, 0xA11Bu ^ 0u, (uint)entered), midnight0 = Hash.Mix(p.Collection.Salt, 0xA11Bu ^ 1u, (uint)entered);
                    uint invitational = Hash.Mix(p.Collection.Salt, 0xA11Bu ^ 2u, (uint)entered);
                    same &= Brackets.AlleySeed(p, Brackets.BrawlKind) == brawl0 && Brackets.AlleySeed(p, Brackets.MidnightKind) == midnight0 && Brackets.AlleySeed(p, Kind) == invitational;
                    apart &= new[] { brawl0, midnight0, invitational }.Distinct().Count() == 3;
                    tried++;
                }
            // And an entered Brawl and Open still draw the same field: the preview's seed is the entered bracket's.
            var q = OrvelOpen(starter, cards, printings, library, 0xA11E2030);
            var previewSeed = Brackets.AlleySeed(q, Brackets.MidnightKind);
            var entered1 = Brackets.EnterAlley(q, real, Brackets.MidnightKind, s.Ctx, "x");
            // The Invitational's preview is the bracket EnterAlley seats (the same seed, the same field).
            var ip = ClearNoLimits(NoLimitsOpen(starter, cards, printings, library, 0xA11E2031));
            ip.BracketsEntered = 3;
            uint invSeed = Brackets.AlleySeed(ip, Kind);
            var invPreview = Brackets.TryCreateAlley(inv.Data, Kind, ip, inv.Ctx, invSeed, "x");
            var invEntered = Brackets.EnterAlley(ip, inv.Data, Kind, inv.Ctx, "x");
            string Sig(Bracket b) => string.Join("|", b.Slots.Select(e => $"{e.Seed}:{e.Character}:{e.Deck}:{e.Elo:0}"));
            Check($"I4. AlleySeed: the Brawl keeps salt 0 and the Open salt 1 (the values they drew before), the Invitational draws salt 2, all different, over {tried} saves; an entered Open is the seed its preview showed ({entered1?.Seed == previewSeed}), "
                  + $"and so is the Invitational's, with the same field ({invEntered != null && invPreview != null && Sig(invPreview) == Sig(invEntered)})",
                same && apart && entered1 != null && entered1.Seed == previewSeed && invEntered != null && invPreview != null && invEntered.Seed == invSeed && Sig(invPreview) == Sig(invEntered) && ip.BracketsEntered == 4);
        }

        // ---- I5. RulesFor ----
        {
            uint pot = Alley.CodeOf("Pot of Greed", cards), hunter = Alley.CodeOf("Wind-Up Hunter", cards), djinn = Alley.CodeOf("Djinn Releaser of Rituals", cards);
            Deck With(uint code, int n)
            {
                var d = starter.Clone();
                d.Side.Clear();
                d.Main.RemoveAll(c => Banlist.BaseCode(c, cards) == pot || c == code);
                for (int i = 0; i < n; i++) d.Main.Add(code);
                return d;
            }
            var ctx = s.Ctx;
            var inv5 = RulesForKinds(ctx);
            var threePots = With(pot, 3);
            int Illegal(Deck d, FormatRules r) => DeckRules.Validate(d, r.List!, cards).Count;
            Check($"I5. RulesFor: the Invitational gets the No Limits rules (\"{inv5.invitational.List?.Name}\": 3 Pot of Greed legal, {Illegal(threePots, inv5.invitational)} problems), the Brawl (\"{inv5.brawl.List?.Name}\") and the Open (\"{inv5.midnight.List?.Name}\") the Alley's "
                  + $"(3 Pot of Greed illegal, {Illegal(threePots, inv5.brawl)} problem(s)); a Never card and a BossOnly card are illegal in all three; no pool, no Ban Passes, forced",
                inv5.invitational.List?.Name == "No Limits" && Illegal(threePots, inv5.invitational) == 0 && inv5.brawl.List == ctx.List && inv5.midnight.List == ctx.List
                && DeckRules.Validate(threePots, inv5.brawl.List!, cards).Any(x => x.Code == pot) && DeckRules.Validate(threePots, inv5.midnight.List!, cards).Any(x => x.Code == pot)
                && new[] { inv5.invitational, inv5.brawl, inv5.midnight }.All(r => DeckRules.Validate(With(hunter, 1), r.List!, cards).Any(x => x.Code == hunter) && DeckRules.Validate(With(djinn, 1), r.List!, cards).Any(x => x.Code == djinn)
                    && r.Pool == "" && r.Passes == 0 && r.Forced)
                && BackAlley.RulesFor(inv.Data, inv.Data.IndexOf("no-limits"), inv.Ctx).List?.Name == "No Limits" && Brackets.RulesFor(Kind, inv.Ctx).List == BackAlley.RulesFor(inv.Data, inv.Data.IndexOf("no-limits"), inv.Ctx).List);
        }

        // ---- I6. The No Limits Pack's pool ----
        {
            var realPool = BackAlley.NoLimitsPackPool(real, s.Ctx);
            var realPacks = ContrabandPacks.NoLimits(real, s.Ctx);
            var forbidden = new HashSet<uint>(nl.Ctx.Pool(9999));
            var ready = nl.Data.Builds.Where(b => b.Ready).ToList();
            var expected = new SortedSet<uint>();
            foreach (var h in ready)
            {
                var deck = nl.Ctx.LoadDeck(h.Deck);
                if (deck == null) continue;
                foreach (var c in deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, cards)).Where(forbidden.Contains))
                    if (printings.Of(c, cards.Get(c)?.Alias ?? 0).Any()) expected.Add(c);
            }
            var pool = BackAlley.NoLimitsPackPool(nl.Data, nl.Ctx);
            var rolls = Enumerable.Range(0, 200).Select(k => nlPacks.Roll((uint)k * 7919u)).ToList();
            var emptyRoll = realPacks.Roll(1);
            // The pool grows with each ready build: one build not ready is a smaller pool.
            var tova = nl.Data.FindBuild("tova")!;
            int smaller;
            tova.Ready = false;
            try { smaller = BackAlley.NoLimitsPackPool(nl.Data, nl.Ctx).Count; }
            finally { tova.Ready = true; }
            Check($"I6. the No Limits Pack's pool is the ready builds' decks ∩ the era-9999 pool with a printing: {realPool.Count} with the real data (no build ready: an empty pack, {emptyRoll.Count} pulls), "
                  + $"{pool.Count} with wave 1 forced ready ({NoLimitsPackPoolWave1} pinned), {smaller} with Tova not ready; recomputed here it is the same {expected.Count} cards, sorted; 200 packs draw only from it, 5 different cards a pack, the slab last",
                realPool.Count == 0 && realPacks.Pool.Count == 0 && emptyRoll.Count == 0 && pool.Count == NoLimitsPackPoolWave1 && pool.SequenceEqual(expected) && nlPacks.Pool.Count == pool.Count && smaller < pool.Count
                && pool.SequenceEqual(pool.OrderBy(c => c)) && pool.Distinct().Count() == pool.Count && pool.All(forbidden.Contains)
                && rolls.All(r => r.Count == ContrabandPacks.Size && r.All(x => nlPool.Contains(x.Code)) && r.Select(x => x.Code).Distinct().Count() == r.Count && r[^1].Slab >= 9)
                && ready.Count == 7);
        }

        // ---- I7. A title's ledger at every placement ----
        {
            var played = new List<string>();
            bool allOk = true;
            for (int loseIn = 3; loseIn >= -1; loseIn--)
            {
                var q = ClearNoLimits(NoLimitsOpen(starter, cards, printings, library, 0xA11E2040 + (uint)(loseIn + 1) * 7));
                q.Exp = TopExp; // past level 99: a bracket duel's level-up pays no DP or tokens
                int tokens0 = q.Tokens;
                var (b, lost, end, duels) = PlayInvitational(q, inv, library, packs, ContrabandPacks.NoLimits(inv.Data, inv.Ctx), loseIn);
                int place = loseIn < 0 ? 0 : 4 - loseIn;
                int pay = Brackets.PayFor(Kind)[place], packsWon = Brackets.PacksFor(Kind)[place];
                var copies = end.Packs.SelectMany(x => x).Where(x => x.Copy != null).Select(x => x.Copy!).ToList();
                var invPool = ContrabandPacks.NoLimits(inv.Data, inv.Ctx).Pool.ToHashSet();
                bool ok = b.Placement() == place && end.Placement == place && (loseIn < 0 ? end.Dp == pay && lost == null : lost?.Reward.Dp == pay && end.Dp == 0)
                          && q.DpEarned.GetValueOrDefault(Ledger.NoLimits) == pay && q.DpSpent.GetValueOrDefault(Ledger.NoLimitsEntry) == 3000
                          && !q.DpEarned.ContainsKey(Ledger.Alley) && !q.DpSpent.ContainsKey(Ledger.AlleyBrackets) && !q.DpEarned.ContainsKey(Ledger.Locals) && !q.DpSpent.ContainsKey(Ledger.Brackets)
                          && Ledger.Net(q) == q.Dp - Profile.StartingDp && pay <= 300 * duels && duels == (place == 3 ? 2 : place == 4 ? 1 : place == 2 ? 3 : 5)
                          && end.PacksWon == packsWon && end.Packs.Count == packsWon && end.Packs.All(x => x.Count == ContrabandPacks.Size)
                          && copies.All(c => c.Source == CopySource.Prize && c.Paid == 0 && q.Collection.SellPrice(c) == 0 && SellDesk.Sell(q, c, none) == 0 && invPool.Contains(Banlist.BaseCode(c.Code, cards)))
                          && end.Events.SequenceEqual(place == 0 ? new[] { BackAlley.InvitationalWinEvent } : new string[0]) && end.Tokens == (place == 0 ? 2 : 0) && q.Tokens - tokens0 == (place == 0 ? 2 : 0)
                          && q.Seen.Contains(Brackets.InvitationalTokenKey) == (place == 0) && !q.Seen.Contains(Brackets.OpenTokenKey)
                          && q.AlleyBrackets[Kind].Entries == 1 && q.AlleyBrackets[Kind].Titles == (place == 0 ? 1 : 0) && q.AlleyBrackets[Kind].Best == place && !q.AlleyBrackets.ContainsKey(Brackets.BrawlKind)
                          && q.Bracket == null && q.Locals.Count == 0 && Brackets.TitlesTotal(q) == 0
                          && (packsWon == 0 || end.Summary.Contains(packsWon == 1 ? "1 No Limits Pack" : $"{packsWon} No Limits Packs")) && !end.Summary.Contains("Contraband");
                allOk &= ok;
                played.Add($"{Brackets.PlacementText(place)} {pay:N0}{(packsWon > 0 ? $"+{packsWon}p" : "")} in {duels} duel{(duels == 1 ? "" : "s")}{(ok ? "" : " (FAIL)")}");
                events.UnionWith(end.Events);
            }
            Check($"I7. a title's ledger at every placement: the entry is {Ledger.NoLimitsEntry} (3,000), the pay {Ledger.NoLimits} (a loss at once, a title at the end), never {Ledger.Alley} or {Ledger.AlleyBrackets}; the No Limits Packs open from the "
                  + $"Invitational's pool as Prize copies, Paid 0, that sell for 0; the pay is at most 300 a duel; the ledger balances; tokens only on a title ({string.Join("; ", played)})",
                allOk && played.Count == 5);
            // The first title pays 2 tokens, the second none; the Contraband Pack never stands in for a No Limits Pack; New Game+ keeps it all.
            var tp = ClearNoLimits(NoLimitsOpen(starter, cards, printings, library, 0xA11E2050));
            tp.Exp = TopExp;
            int tpTokens0 = tp.Tokens;
            var invPacks = ContrabandPacks.NoLimits(inv.Data, inv.Ctx);
            var first = PlayInvitational(tp, inv, library, packs, invPacks, -1);
            int afterFirst = tp.Tokens - tpTokens0;
            tp.Earn(Ledger.Debug, 10000);
            var second = PlayInvitational(tp, inv, library, packs, invPacks, -1);
            int afterSecond = tp.Tokens - tpTokens0;
            var saved = AlleyLines(SaveText(tp)).Where(l => l.StartsWith("alleybracket ")).ToList();
            var rt = RoundTrip(tp, printings, cards);
            bool ledgerKept = rt.DpEarned.GetValueOrDefault(Ledger.NoLimits) == tp.DpEarned.GetValueOrDefault(Ledger.NoLimits) && rt.DpSpent.GetValueOrDefault(Ledger.NoLimitsEntry) == tp.DpSpent.GetValueOrDefault(Ledger.NoLimitsEntry);
            rt.NewGamePlus();
            Check($"I7. the first title pays {first.end.Tokens} tokens ({first.end.Summary}), the second {second.end.Tokens} ({afterSecond - afterFirst} more); the record ({string.Join(", ", saved)}) and {Brackets.InvitationalTokenKey} "
                  + "survive a save and New Game+, which re-pays nothing",
                first.end.Tokens == 2 && afterFirst == 2 && second.end.Tokens == 0 && afterSecond == 2 && first.end.PacksWon == 4 && first.end.Packs.Count == 4 && second.end.Packs.Count == 4
                && tp.AlleyBrackets[Kind].Titles == 2 && tp.AlleyBrackets[Kind].Entries == 2
                && saved.Count == 1 && rt.AlleyBrackets[Kind] is { Entries: 2, Titles: 2, Best: 0 } && rt.Seen.Contains(Brackets.InvitationalTokenKey) && ledgerKept
                && Brackets.AlleyTitleEvents(rt, inv.Data).SequenceEqual(new[] { BackAlley.InvitationalWinEvent }));
            // The packs can't be lost: a title with no No Limits pack source (none, the Contraband Pack in its place, or a pool too small to fill a pack) isn't finished.
            // Finish returns null and changes nothing, the bracket stays, and a later call with the packs pays it whole. A round-of-16 exit (no packs) always finishes.
            var up = ClearNoLimits(NoLimitsOpen(starter, cards, printings, library, 0xA11E2053));
            up.Exp = TopExp;
            var (ub, _, uduels) = PlayInvitationalToEnd(up, inv, -1, library);
            string State(Profile q) => $"{q.Dp}|{q.Tokens}|{q.DpEarned.GetValueOrDefault(Ledger.NoLimits)}|{q.Seen.Contains(Brackets.InvitationalTokenKey)}|{q.AlleyBrackets[Kind].Titles}|{q.AlleyBrackets[Kind].Best}|{q.Collection.All.Count()}";
            string before = State(up);
            var tiny = new ContrabandPacks(cards, printings, invPacks.Pool.Take(3).ToList());
            var noSource = Brackets.Finish(up, library, ub, null);
            var wrongSource = Brackets.Finish(up, library, ub, null, packs, null);
            var smallPool = Brackets.Finish(up, library, ub, null, packs, tiny);
            var whyNone = Brackets.FinishBlocked(ub, null);
            var whyTiny = Brackets.FinishBlocked(ub, tiny);
            bool kept = State(up) == before && up.Bracket == ub && ub.Done && !up.Seen.Contains(Brackets.InvitationalTokenKey);
            var reloaded = FromText(SaveText(up), printings, cards);
            bool survives = reloaded.Bracket != null && reloaded.Bracket.Kind == Kind && reloaded.Bracket.Done;
            var paid = Brackets.Finish(reloaded, library, reloaded.Bracket!, null, packs, invPacks);
            // A round-of-16 exit owes no packs: finishes with no source at all.
            var ep = ClearNoLimits(NoLimitsOpen(starter, cards, printings, library, 0xA11E2054));
            ep.Exp = TopExp;
            var (eb, _, _) = PlayInvitationalToEnd(ep, inv, 0, library);
            var earlyOut = Brackets.Finish(ep, library, eb, null);
            Check($"I7. a title with no No Limits pack source isn't finished (\"{whyNone}\"; the Contraband Pack in its place or a pool of {tiny.Pool.Count} cards, \"{whyTiny}\", the same): Finish returns null and changes nothing (DP, tokens, record, collection), "
                  + $"the bracket stays done in the save; a later call with the packs pays it whole ({paid?.Dp:N0} DP, {paid?.Tokens} tokens, {paid?.Packs.Count} packs opened); "
                  + "a round-of-16 exit owes no packs and finishes with no source",
                uduels == 5 && noSource == null && wrongSource == null && smallPool == null && whyNone != null && whyTiny != null && !tiny.CanFill && invPacks.CanFill && kept && survives
                && paid != null && paid.Dp == 1500 && paid.Tokens == 2 && paid.Packs.Count == 4 && paid.PacksWon == 4 && reloaded.Seen.Contains(Brackets.InvitationalTokenKey) && reloaded.Bracket == null
                && reloaded.AlleyBrackets[Kind].Titles == 1 && reloaded.DpEarned.GetValueOrDefault(Ledger.NoLimits) == 1500
                && earlyOut != null && earlyOut.Placement == 4 && earlyOut.Packs.Count == 0 && Brackets.FinishBlocked(eb, null) == null);
            // The held finish's way out (TODO 2026-11-06, Brackets.SettleHeld, at a load): a title whose packs still can't be opened is
            // finished owing them: the DP, tokens, record and events paid now, exactly as its twin's Finish with the packs pays them;
            // the packs owed by seed (in the save), never opened from a pool that can't fill, then opened whole once it can, the same
            // packs the twin opened, once. A held title with a pool that fills isn't settled (it's a normal Finish), nor is anything else.
            {
                Profile Opened() { var q = ClearNoLimits(NoLimitsOpen(starter, cards, printings, library, 0xA11E2055)); q.Exp = TopExp; return q; }
                var hp = Opened();
                var twin = Opened();
                var (hb, _, _) = PlayInvitationalToEnd(hp, inv, -1, library);
                var (tb, _, _) = PlayInvitationalToEnd(twin, inv, -1, library);
                var notHeld = Brackets.SettleHeld(twin, library, invPacks);
                var twinEnd = Brackets.Finish(twin, library, tb, null, packs, invPacks)!;
                int hDp = hp.Dp, hTokens = hp.Tokens, hCopies = hp.Collection.All.Count();
                var tinyPool = new ContrabandPacks(cards, printings, invPacks.Pool.Take(3).ToList());
                var settled = Brackets.SettleHeld(hp, library, tinyPool);
                var again = Brackets.SettleHeld(hp, library, tinyPool);
                var owedLines = SaveText(hp).Split('\n').Count(l => l.StartsWith("nlowed "));
                var hrt = RoundTrip(hp, printings, cards);
                var hrt2 = RoundTrip(hp, printings, cards);
                var stillTiny = Brackets.PayOwedPacks(hrt, tinyPool);
                int owedAfterTiny = hrt.NoLimitsPacksOwed.Count;
                var paidOwed = Brackets.PayOwedPacks(hrt, invPacks);
                var paidTwice = Brackets.PayOwedPacks(hrt, invPacks);
                static string Pulls(IEnumerable<List<PrizePull>> ps) => string.Join(";", ps.Select(pk => string.Join(",", pk.Select(x => $"{x.Code}/{x.Rarity}/{x.Slab}"))));
                // The next Invitational finish that can open its packs opens the owed ones too.
                hrt2.Earn(Ledger.Debug, 10000);
                var (nb, _, _) = PlayInvitationalToEnd(hrt2, inv, -1, library);
                var nextEnd = Brackets.Finish(hrt2, library, nb, null, packs, invPacks);
                Check($"I7. a held title's way out (Brackets.SettleHeld at a load, the pool still of {tinyPool.Pool.Count} cards): finished owing its packs (\"{settled?.Summary}\"): "
                      + $"+{hp.Dp - hDp:N0} DP, +{hp.Tokens - hTokens} tokens, the record and events as its twin's Finish pays them (\"{twinEnd.Summary}\"), no card yet, {hp.NoLimitsPacksOwed.Count} packs owed in the save; "
                      + $"brackets free again; a pool that still can't fill opens none, then the full pool opens all {paidOwed.Count}, the twin's own packs, once; "
                      + $"the next title's Finish opens owed ones with its own (\"{nextEnd?.Summary}\"); a title whose pool fills is never settled this way",
                    settled != null && notHeld == null && again == null && settled.Placement == 0 && settled.Dp == twinEnd.Dp && settled.Dp == 1500 && settled.Tokens == twinEnd.Tokens && settled.Tokens == 2
                    && settled.PacksWon == 4 && settled.PacksOwed == 4 && settled.Packs.Count == 0 && settled.Events.SequenceEqual(twinEnd.Events)
                    && hp.Dp - hDp == settled.Dp && hp.Tokens - hTokens == settled.Tokens && hp.Collection.All.Count() == hCopies && hp.Bracket == null
                    && hp.AlleyBrackets[Kind].Titles == twin.AlleyBrackets[Kind].Titles && hp.DpEarned.GetValueOrDefault(Ledger.NoLimits) == twin.DpEarned.GetValueOrDefault(Ledger.NoLimits)
                    && Brackets.EnterBlocked(hp, inv.Data, Kind) == null && hp.NoLimitsPacksOwed.Count == 4 && hrt.NoLimitsPacksOwed.Count == 0 && owedAfterTiny == 4 && stillTiny.Count == 0
                    && paidOwed.Count == 4 && Pulls(paidOwed) == Pulls(twinEnd.Packs) && paidTwice.Count == 0 && hrt.Dp == twin.Dp && hrt.Collection.All.Count() == twin.Collection.All.Count()
                    && nextEnd != null && nextEnd.OwedPaid == 4 && nextEnd.Packs.Count == 8 && hrt2.NoLimitsPacksOwed.Count == 0 && Pulls(nextEnd.Packs.Skip(4)) == Pulls(twinEnd.Packs) && owedLines == 4);
            }
            events.Add(Kind);
            events.UnionWith(first.end.Events);
            // Leaving a live Invitational duel is a loss, paid to nolimits (the round of 16's 100); a save closed mid-duel settles the same.
            var lp = ClearNoLimits(NoLimitsOpen(starter, cards, printings, library, 0xA11E2052));
            lp.Exp = TopExp;
            var lb = Brackets.EnterAlley(lp, inv.Data, Kind, inv.Ctx, "x")!;
            var notLive = Brackets.SettleAbandoned(lp, library);
            Brackets.StartYours(lb);
            var saved2 = SaveText(lp);
            var left = Brackets.SettleAbandoned(lp, library);
            var closed = FromText(saved2, printings, cards);
            var settledAtLoad = closed.Bracket != null ? Brackets.SettleAbandoned(closed, library) : null;
            Check($"I7. leaving a live Invitational duel is a loss (\"{left?.Reward.Summary}\"): out in the round of 16, {AlleyEconomy.InvitationalPay[4]} DP into {Ledger.NoLimits} and none into {Ledger.Alley}; with no duel live it does nothing; "
                  + "a save closed mid-duel settles the same on load",
                notLive == null && left != null && left.Out && left.Placement == 4 && lb.YouOut && lp.DpEarned.GetValueOrDefault(Ledger.NoLimits) == AlleyEconomy.InvitationalPay[4] && !lp.DpEarned.ContainsKey(Ledger.Alley)
                && left.Reward.Summary.Contains("No Limits Invitational") && settledAtLoad != null && settledAtLoad.Out && closed.DpEarned.GetValueOrDefault(Ledger.NoLimits) == AlleyEconomy.InvitationalPay[4]);
            // A bracket saved mid-way comes back as it was and finishes the same.
            var mid = ClearNoLimits(NoLimitsOpen(starter, cards, printings, library, 0xA11E2051));
            var mb = Brackets.EnterAlley(mid, inv.Data, Kind, inv.Ctx, "x")!;
            Brackets.StartYours(mb);
            Brackets.RecordYours(mid, library, mb, 0, false);
            var back = RoundTrip(mid, printings, cards);
            var bb = back.Bracket;
            Check($"I7. an Invitational saved after one win comes back as it was ({bb?.Kind}, {bb?.Slots.Length} seats, round {(bb != null ? bb.RoundOf(bb.YourMatch()) + 1 : -1)} next, best-of-{bb?.FinalBestOf} final) and the record's one entry stays",
                bb != null && bb.Kind == Kind && bb.Size == 16 && bb.FinalBestOf == 3 && bb.Slots.Select(e => $"{e.Character}/{e.Deck}/{e.Seed}").SequenceEqual(mb.Slots.Select(e => $"{e.Character}/{e.Deck}/{e.Seed}"))
                && bb.YourMatch() == mb.YourMatch() && back.AlleyBrackets[Kind].Entries == 1 && back.DpSpent.GetValueOrDefault(Ledger.NoLimitsEntry) == 3000);
        }

        // ---- I8. The poster ----
        {
            var bill = Posters.Find("invitational-bill");
            var entered = Opened(starter, cards, printings, library, 0xA11E2060);
            entered.AlleyBrackets[Kind] = new LocalsRecord { Entries = 3, Titles = 0, Best = 1 };
            var otherTitles = Opened(starter, cards, printings, library, 0xA11E2060);
            otherTitles.AlleyBrackets["brawl"] = new LocalsRecord { Entries = 1, Titles = 1, Best = 0 };
            otherTitles.AlleyBrackets["midnight"] = new LocalsRecord { Entries = 1, Titles = 1, Best = 0 };
            var won = Opened(starter, cards, printings, library, 0xA11E2060);
            won.AlleyBrackets[Kind] = new LocalsRecord { Entries = 1, Titles = 1, Best = 0 };
            Check($"I8. the poster \"{bill?.Title}\" ({bill?.Id}, {bill?.Unlock}): earned by the Invitational's title alone, not by entering it, nor by the Brawl's and the Open's titles; the card {bill?.Card} ({Name(bill?.Card ?? 0)}) exists; Posters.All still ends with midnight-bill",
                bill != null && bill.Earned(circuits, won) && !bill.Earned(circuits, entered) && !bill.Earned(circuits, otherTitles) && cards.Get(bill.Card) != null && bill.Unlock == "Win the No Limits Invitational"
                && Posters.All[^1].Id == "midnight-bill" && Posters.All.Select(p => p.Id).Distinct().Count() == Posters.All.Count);
        }

        // ---- I9. The Library engraving skips a partner's edit ----
        {
            string main = string.Join("\n", starter.Main);
            var yours = Deck.ParseYdk("#campaign\n#title My Warriors\n#main\n" + main + "\n#extra\n!side\n", "my-warriors");
            var edit = Deck.ParseYdk("#campaign\n#title Vera's Warriors (your edit)\n#recipe vera-warriors\n#partner vera\n#main\n" + main + "\n#extra\n!side\n", "Vera's Warriors (your edit)");
            var custom = yours.Clone();
            custom.Name = "My Warriors (custom loaner)";
            custom.Notes["loaner"] = "vera-warriors";
            var loaner = yours.Clone();
            loaner.Name = Loaners.NameFor("vera-warriors");
            var p = Profile.New(starter, cards, printings);
            p.LibraryOpen = true;
            int cup = 0, matches = library.Cups[cup].Matches.Count;
            library.Record(p, cup, 0, 0, loaner: false, built: false, deck: edit.Name, played: edit);
            library.Record(p, cup, 0, 0, loaner: false, built: false, deck: custom.Name, played: custom);   // a custom loaner passed as a build: still not engraved
            library.Record(p, cup, 0, 0, loaner: true, built: false, deck: loaner.Name, played: loaner);
            var skipped = Cosmetics.EngraveDecks(p, library, cup).ToList();
            library.Record(p, cup, 0, 0, loaner: false, built: true, deck: "my-warriors", played: yours);
            var engraved = Cosmetics.EngraveDecks(p, library, cup).ToList();
            library.Record(p, cup, 0, 0, loaner: false, built: true, deck: "legacy-caller");                // no deck passed: trusted on the name alone, as before
            for (int m = 1; m < matches; m++) library.Record(p, cup, m, 0, loaner: false, built: true, deck: "my-warriors", played: yours);
            p.Dp = 10 * Cosmetics.EngravePrice;
            var editSays = Cosmetics.EngraveBlocked(p, library, cup, edit.Name, "Vera's Warriors");
            bool engraves = Cosmetics.Engrave(p, library, cup, "my-warriors", "My Warriors") && Cosmetics.Engraving(p, library, cup) == "My Warriors";
            bool editRefused = !Cosmetics.Engrave(p, library, cup, edit.Name, "Vera's Warriors") && Cosmetics.Engraving(p, library, cup) == "My Warriors";
            Check($"I9. a Library win with a partner's edit, a custom loaner or a loaner engraves nothing ({string.Join(", ", skipped)}{(skipped.Count == 0 ? "none" : "")}), your own deck does ({string.Join(", ", engraved)}); a caller that passes no deck is trusted on the name ({Cosmetics.EngraveDecks(p, library, cup).Contains("legacy-caller")}); "
                  + $"the edit's plate is refused (\"{editSays}\") while your own deck's engraves",
                skipped.Count == 0 && engraved.SequenceEqual(new[] { "my-warriors" }) && Cosmetics.EngraveDecks(p, library, cup).Contains("legacy-caller") && !Cosmetics.EngraveDecks(p, library, cup).Contains(edit.Name)
                && editSays != null && editSays.Contains("win a match") && engraves && editRefused && library.IsCleared(cup, p) && Library.CountsForStamp(yours, 0) && !Library.CountsForStamp(edit, 0));
        }

        // ---- I10. Story and voice ----
        {
            var story = Story.Load(Path.Combine(root, "campaign", "story.txt"));
            var wins = story.Scenes.Where(sc => sc.Trigger == BackAlley.InvitationalWinEvent).ToList();
            var firstEntry = story.Scenes.Where(sc => sc.Trigger == Kind).ToList();
            var ids = roster.Select(r => r.Id).ToHashSet();
            var speakers = firstEntry.Concat(wins).SelectMany(sc => sc.Lines).Select(l => l.Speaker).Where(sp => sp.Length > 0 && sp != "-" && sp != "opponent").Distinct().ToList();
            var voiceText = File.ReadAllLines(Path.Combine(root, "campaign", "alley-voice.txt"));
            List<string> Pool(string hook) => voiceText.SkipWhile(l => l.Trim() != "pool: " + hook).Skip(1).TakeWhile(l => !l.StartsWith("pool:") && !l.StartsWith("# ----")).Where(l => l.Contains(':') && !l.TrimStart().StartsWith("#")).ToList();
            var rounds = new[] { "round-of-16", "quarter-final", "semi-final", "final" };
            var empty = rounds.SelectMany(r => new[] { "mc:invitational:" + r, "stream:invitational:" + r }).Where(h => Pool(h).Count == 0).ToList();
            var triggers = BackAlley.Triggers(real);
            Check($"I10. story: the Invitational's first entry ({firstEntry.Count} scene{(firstEntry.Count == 1 ? "" : "s")}) and its title ({wins.Count}: {string.Join(", ", wins.Select(w => w.Id))}) have scenes whose speakers all have profiles ({string.Join(", ", speakers.Where(sp => !ids.Contains(sp)))}{(speakers.All(ids.Contains) ? "none missing" : "")}); "
                  + $"mc: and stream: have a pool for each of the four rounds{(empty.Count > 0 ? " (empty: " + string.Join(", ", empty) + ")" : "")}; they're all voice hooks; BackAlley.Triggers lists invitational and {BackAlley.InvitationalWinEvent}, "
                  + "and no Invitational-only pilot is an out-of-towner",
                firstEntry.Count == 1 && wins.Count == 2 && speakers.All(ids.Contains) && empty.Count == 0 && rounds.All(r => BackAlley.IsVoiceHook("mc:invitational:" + r, real, cards) && BackAlley.IsVoiceHook("stream:invitational:" + r, real, cards))
                && triggers.Contains(Kind) && triggers.Contains(BackAlley.InvitationalWinEvent) && BackAlley.IsStoryTrigger(Kind, real) && BackAlley.IsStoryTrigger(BackAlley.InvitationalWinEvent, real) && BackAlley.IsNoLimitsTrigger(Kind)
                && !triggers.Contains("outoftowner:tova") && !triggers.Contains("outoftowner:remy") && triggers.Contains("outoftowner:junia") && BackAlley.InvitationalWinEvent == "bracketwin:No Limits Invitational");
        }

        ShelfChecks(s, nl, starter, cards, printings, library, Check);
        Console.WriteLine($"  (Invitational and shelf checks: {added} added, {timer.Elapsed.TotalSeconds:0.0} s)");
    }

    private static (FormatRules invitational, FormatRules brawl, FormatRules midnight) RulesForKinds(AlleyContext ctx) =>
        (Brackets.RulesFor(Brackets.InvitationalKind, ctx), Brackets.RulesFor(Brackets.BrawlKind, ctx), Brackets.RulesFor(Brackets.MidnightKind, ctx));

    // ---- The shelf: a sixth back at most, the restored slots, a save without the wall ------------------------------------

    private static void ShelfChecks(Setup s, Setup nl, Deck starter, ICardDataProvider cards, PrintingIndex printings, Library library, Action<string, bool> Check)
    {
        string Name(uint c) => cards.Get(c)?.Name ?? c.ToString();
        int none(uint _) => 0;
        var packs = s.Packs;

        // S1. The price rounds up to 500, so no slab sells back more than a sixth.
        {
            bool up = true, multiple = true, tight = true;
            for (int v = 1; v <= 40000; v++)
            {
                int price = AlleyEconomy.ShelfPrice(v);
                up &= price >= v * AlleyEconomy.ShelfMarkup - 1e-9;
                multiple &= price % 500 == 0 && price >= 500;
                tight &= price - v * AlleyEconomy.ShelfMarkup < 500 + 1e-9;
            }
            Check($"S1. ShelfPrice rounds {AlleyEconomy.ShelfMarkup}x the value up to 500 for every value from 1 to 40,000 (never under it, within 500 over, a multiple of 500, at least 500): "
                  + $"a Quarter Century slab 10 (5,400) is {AlleyEconomy.ShelfPrice(5400):N0} (not 8,000), a slab 9 at 1,800 is {AlleyEconomy.ShelfPrice(1800):N0} (not 2,500), a slab 9 at 1,500 is {AlleyEconomy.ShelfPrice(1500):N0} (2,250 rounds to 2,500)",
                up && multiple && tight && AlleyEconomy.ShelfPrice(5400) == 8500 && AlleyEconomy.ShelfPrice(1800) == 3000 && AlleyEconomy.ShelfPrice(1500) == 2500 && AlleyEconomy.ShelfPrice(2000) == 3000
                && AlleyEconomy.ShelfPrice(1) == 500 && AlleyEconomy.ShelfPrice(3500) == 5500 && AlleyEconomy.ShelfPrice(5000) == 7500);
        }

        // S2. Every shelf item sells back at most a sixth of its price, through the real buy and Moth's Sell tab.
        {
            var regular = MothsVan.ShelfItems(packs, s.Ctx);
            var restored = MothsVan.RestoredItems(nl.Data, nl.Ctx);
            (double worst, string who, int over, int refused) Backs(IReadOnlyList<(uint code, Rarity rarity, int grade, int value)> items, uint salt)
            {
                var p = Opened(starter, cards, printings, library, salt);
                p.Earn(Ledger.Debug, 100_000_000);
                MothsVan.Shelf(p, new List<(uint code, Rarity rarity, int grade, int value)>()); // sets the period on the save
                double worst = 0;
                string who = "";
                int over = 0, refused = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    var (code, rarity, grade, value) = items[i];
                    var copy = new CardCopy { Code = code, Rarity = rarity, Grade = grade, Slab = grade, Floor = grade, Seed = Hash.Mix(0xC0DEu, (uint)i), Source = CopySource.Used };
                    var l = new Listing { Slot = 1000 + i, Copy = copy, Price = AlleyEconomy.ShelfPrice(value), Epoch = MothsVan.ShelfEpoch(p) };
                    if (MothsVan.Buy(p, l, shelf: true) < 0) { refused++; continue; }
                    var bought = p.Collection.All.FirstOrDefault(c => c.Seed == copy.Seed && c.Code == code && c.Paid == l.Price);
                    if (bought == null) { refused++; continue; }
                    double back = MothsVan.Sell(p, bought, none) / (double)l.Price;
                    if (back > 1.0 / 6 + 1e-9) over++;
                    if (back > worst) { worst = back; who = $"{Name(code)} {rarity} slab {grade} (value {value:N0}, price {l.Price:N0})"; }
                }
                return (worst, who, over, refused);
            }
            var r1 = Backs(regular, 0xA11E3001);
            var r2 = Backs(restored, 0xA11E3002);
            Check($"S2. every shelf item sells back at most a sixth of its price through the real buy and Moth's Sell tab: the {regular.Count} regular items (425 pinned) worst {r1.worst:P2} ({r1.who}), "
                  + $"the {restored.Count} restored items (wave 1 ready) worst {r2.worst:P2} ({r2.who}); {r1.over + r2.over} over, {r1.refused + r2.refused} refused",
                regular.Count == ShelfItems && restored.Count > 0 && r1.over == 0 && r2.over == 0 && r1.refused == 0 && r2.refused == 0 && r1.worst <= 1.0 / 6 + 1e-9 && r2.worst <= 1.0 / 6 + 1e-9);
        }

        // S3. The restored slots: only with the wall open and ready builds.
        {
            var restored = MothsVan.RestoredItems(nl.Data, nl.Ctx);
            var none1 = MothsVan.RestoredItems(s.Data, s.Ctx);
            var stakes = nl.Data.Builds.Where(b => b.Ready).SelectMany(b => b.Stakes).ToHashSet();
            string Key(Listing l) => $"{l.Slot} {l.Copy.Code} {(int)l.Copy.Rarity} {l.Copy.Slab} {l.Copy.Seed} {l.Price} {l.Restored}";
            bool itemsOk = restored.All(x => x.rarity >= AlleyEconomy.ShelfRestoredMinRarity && x.grade is 9 or 10 && x.value >= AlleyEconomy.ShelfRestoredMinValue && stakes.Contains(x.code))
                           && restored.Select(x => x.code).Distinct().All(stakes.Contains) && restored.Count > 0 && none1.Count == 0;
            // One build ready: only its stakes.
            var one = nl.Data.Builds.Where(b => b.Ready).ToList();
            var keep = one.First();
            var others = one.Skip(1).ToList();
            foreach (var o in others) o.Ready = false;
            List<uint> onlyKeep;
            try { onlyKeep = MothsVan.RestoredItems(nl.Data, nl.Ctx).Select(x => x.code).Distinct().ToList(); }
            finally { foreach (var o in others) o.Ready = true; }
            bool same = true, restoredOk = true, flags = true, distinct = true;
            int checkedSaves = 0, restoredSeen = 0;
            for (uint salt = 1; salt <= 40; salt++)
                for (int epoch = 0; epoch < 3; epoch++)
                {
                    Profile Save(bool wall) { var p = Opened(starter, cards, printings, library, 0xA11E3100 + salt); if (wall) p.Seen.Add(BackAlley.WallEvent); p.AlleyDuels = epoch * AlleyEconomy.ShelfEvery; return p; }
                    var closedReal = MothsVan.Shelf(Save(false), packs, s.Ctx);
                    var closedReady = MothsVan.Shelf(Save(false), packs, nl.Ctx);        // ready builds, wall closed
                    var openReal = MothsVan.Shelf(Save(true), packs, s.Ctx);             // wall open, no build ready
                    var openReady = MothsVan.Shelf(Save(true), packs, nl.Ctx);
                    var plain = MothsVan.Shelf(Save(false), MothsVan.ShelfItems(packs, s.Ctx), s.Ctx);   // the shelf without the new parameter
                    same &= closedReal.Select(Key).SequenceEqual(closedReady.Select(Key)) && closedReal.Select(Key).SequenceEqual(openReal.Select(Key)) && closedReal.Select(Key).SequenceEqual(plain.Select(Key))
                            && closedReal.Count == AlleyEconomy.ShelfSize && closedReal.All(l => !l.Restored) && openReal.All(l => !l.Restored);
                    restoredOk &= openReady.Count == AlleyEconomy.ShelfSize && openReady.Take(AlleyEconomy.ShelfSize - AlleyEconomy.ShelfRestoredSlots).Select(Key).SequenceEqual(closedReal.Take(AlleyEconomy.ShelfSize - AlleyEconomy.ShelfRestoredSlots).Select(Key))
                                  && openReady.Skip(AlleyEconomy.ShelfSize - AlleyEconomy.ShelfRestoredSlots).All(l => l.Restored && restored.Any(r => r.code == l.Copy.Code && r.rarity == l.Copy.Rarity && r.grade == l.Copy.Slab && AlleyEconomy.ShelfPrice(r.value) == l.Price))
                                  && openReady.Count(l => l.Restored) == AlleyEconomy.ShelfRestoredSlots;
                    distinct &= openReady.Select(l => l.Copy.Code).Distinct().Count() == openReady.Count;
                    flags &= openReady.All(l => l.Epoch == epoch && l.Copy.Source == CopySource.Used && l.Copy.Slab is 9 or 10 && l.Copy.Grade == l.Copy.Slab);
                    restoredSeen += openReady.Count(l => l.Restored);
                    checkedSaves++;
                }
            // Buying a restored slot and a regular one: alley.shelf, Paid the price, sold marks that survive a reload.
            var buyer = Opened(starter, cards, printings, library, 0xA11E3200);
            buyer.Seen.Add(BackAlley.WallEvent);
            buyer.Earn(Ledger.Debug, 100000);
            var shelf0 = MothsVan.Shelf(buyer, packs, nl.Ctx);
            var reg = shelf0.First(l => !l.Restored);
            var res = shelf0.Last(l => l.Restored);
            int dp0 = buyer.Dp;
            bool bought = MothsVan.Buy(buyer, res, shelf: true) >= 0 && MothsVan.Buy(buyer, reg, shelf: true) >= 0;
            var reloaded = MothsVan.Shelf(RoundTrip(buyer, printings, cards), packs, nl.Ctx);
            bool marks = reloaded.Where(l => l.Slot == res.Slot || l.Slot == reg.Slot).All(l => l.Sold) && reloaded.Count(l => l.Sold) == 2 && reloaded.First(l => l.Slot == res.Slot).Restored;
            bool ledger = buyer.DpSpent.GetValueOrDefault(Ledger.AlleyShelf) == res.Price + reg.Price && dp0 - buyer.Dp == res.Price + reg.Price && Ledger.Net(buyer) == buyer.Dp - Profile.StartingDp;
            // A listing no longer on the shelf is refused (the wall closed, the period moved on); a hand-built one keeps only the period and sold checks.
            var stale = Opened(starter, cards, printings, library, 0xA11E3202);
            stale.Seen.Add(BackAlley.WallEvent);
            stale.Earn(Ledger.Debug, 100000);
            var staleRes = MothsVan.Shelf(stale, packs, nl.Ctx).Last(l => l.Restored);
            stale.Seen.Remove(BackAlley.WallEvent);
            bool staleRefused = MothsVan.Buy(stale, staleRes, shelf: true) < 0 && stale.DpSpent.GetValueOrDefault(Ledger.AlleyShelf) == 0;
            stale.Seen.Add(BackAlley.WallEvent);
            staleRefused &= MothsVan.Buy(stale, staleRes, shelf: true) >= 0;
            var movedOn = Opened(starter, cards, printings, library, 0xA11E3203);
            movedOn.Seen.Add(BackAlley.WallEvent);
            movedOn.Earn(Ledger.Debug, 100000);
            var shown = MothsVan.Shelf(movedOn, packs, nl.Ctx).First();
            movedOn.AlleyDuels = AlleyEconomy.ShelfEvery;
            staleRefused &= MothsVan.Buy(movedOn, shown, shelf: true) < 0;
            Check($"S3. the restored slots appear only with the wall open and ready builds: {restored.Count} restored items from {restored.Select(x => x.code).Distinct().Count()} cards (Ultra Rare and up, slabs 9-10, worth {AlleyEconomy.ShelfRestoredMinValue:N0}+, only ready builds' stakes; "
                  + $"{none1.Count} with the real data, one ready build gives only its own: {onlyKeep.Count} cards of {keep.Id}); over {checkedSaves} shelves (40 saves x 3 periods) the wall-closed shelf is the same in either context and without the parameter, "
                  + $"the wall open with no ready build is the same, and with ready builds the first {AlleyEconomy.ShelfSize - AlleyEconomy.ShelfRestoredSlots} are kept and {AlleyEconomy.ShelfRestoredSlots} restored slabs follow ({restoredSeen} seen), all different cards; "
                  + $"a restored and a regular slot bought: {Ledger.AlleyShelf} {buyer.DpSpent.GetValueOrDefault(Ledger.AlleyShelf):N0}, both sold after a reload, the restored one still flagged; "
                  + $"a restored listing shown with the wall open is refused once the wall is closed ({staleRefused}) and a listing from the last period is refused",
                itemsOk && onlyKeep.Count > 0 && onlyKeep.All(keep.Stakes.Contains) && same && restoredOk && distinct && flags && restoredSeen == checkedSaves * AlleyEconomy.ShelfRestoredSlots
                && !MothsVan.RestoredOpen(Opened(starter, cards, printings, library, 0xA11E3201)) && MothsVan.RestoredOpen(buyer) && bought && marks && ledger && staleRefused);
        }
    }
}
