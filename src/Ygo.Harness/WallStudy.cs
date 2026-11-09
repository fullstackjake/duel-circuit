using System.Diagnostics;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Campaign;
using Ygo.Engine.DeckBuilding;
using Ygo.Engine.Protocol;

namespace Ygo.Harness;

/// <summary>
/// `--wall-study [--games N] [--seed N] [--only text] [--pool all] [--loaners N] [--partners N] [--kit N] [--write file]`:
/// what the player's tools are worth at the campaign's last walls
/// (the Expert and Master Tag Cups, Seraph alone and in tag). Each wall is played as the campaign sets it up, the CPUs
/// exactly as the game makes them, with a skilled stand-in for you (the QA player's style: plans ahead, knows its deck).
/// What changes is only what the player chooses: your deck (the starter, or the strongest loaners unlocked by then), your
/// tag partner (Old Gus with Zombies as the QA player picks, the strongest partners you'd have beaten by then, or Kit with
/// a loaner), and who goes first. Every option plays the same seeds, so the differences are the options'.
/// --pool all draws the options from every recipe and duelist in the campaign (a player can be further along than the wall
/// needs); --loaners, --partners and --kit set how many loaners, partners and Kit decks to try (2, 2, 1). --write merges the
/// results into an advice file (campaign/wall-advice.txt), which the Stuck? advice reads.
/// How the stand-in for you plays can be set to match the game (see <see cref="YouAs"/>): `--option text` plays only the
/// options whose label has that text, `--you-plan off` plays your turns without the turn planner (as the QA stand-in does
/// in a tag duel), `--you-chain auto` passes the chances to respond that the game's Chain: Auto wouldn't ask about.
/// `--remeasure` plays the options the advice file (--write) already has for each wall, instead of choosing them afresh.
/// `--era-limited` plays as era-limited play does: your team's decks made legal for the circuit's ban list.
/// With `--option`, --write adds or updates just the options played and keeps the wall's others.
/// </summary>
public static class WallStudy
{
    private const string Starter = "mekk-knight-invoked";

    private sealed record Option(string Label, string Deck, Personality? Partner, string PartnerDeck);

    /// <summary>How the stand-in for you plays, and which options to play.</summary>
    public sealed class YouAs
    {
        public string Option = "";
        public bool Plans = true;
        public bool ChainAuto;
        /// <summary>`--circuit-study --you-seat app`: your seat as the app's QA play has it (see CircuitStudy.AppSeat).</summary>
        public bool AppSeat;
        /// <summary>Era-limited play (`--era-limited`): your team's decks follow the circuit's ban list. Otherwise nobody's do.</summary>
        public bool EraLimited;
        public bool Remeasure;
        public override string ToString() => $"{(Plans ? "with" : "without")} the planner, chain prompts {(ChainAuto ? "as Chain: Auto asks" : "always")}"
            + (EraLimited ? ", era-limited (your team follows the ban lists)" : "");
    }

    /// <summary>The game's Chain: Auto in front of a bot: a chance to respond the game wouldn't ask about is passed.</summary>
    internal sealed class AskedOnly : IDuelist
    {
        private readonly IDuelist _inner;
        private readonly Ygo.Engine.Interactive.PromptPresenter _presenter;

        public AskedOnly(IDuelist inner, DuelContext context)
        {
            _inner = inner;
            _presenter = new Ygo.Engine.Interactive.PromptPresenter(context) { Mode = Ygo.Engine.Interactive.ResponseMode.Auto };
        }

        public byte[]? Respond(Prompt prompt, int attempt) =>
            attempt == 0 && prompt is ChainPrompt && _presenter.AutoAnswer(prompt) is { } pass ? pass : _inner.Respond(prompt, attempt);

        public void OnPhaseChanged() => _inner.OnPhaseChanged();
    }

    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, int games, int seed, string only,
        bool wholePool = false, int deckCount = 2, int partnerCount = 2, int kitCount = 1, string write = "", YouAs? youAs = null)
    {
        youAs ??= new YouAs();
        var timer = Stopwatch.StartNew();
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities"));
        var circuits = Circuits.Load(StudyFiles.CircuitsPath(root), roster);
        var guests = GuestList.Load(Path.Combine(root, "campaign", "guests.txt"), roster);
        var lists = Banlist.LoadFolders(new[] { Path.Combine(root, "vendor", "LFLists"), Path.Combine(root, "lflists") });
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        var ratings = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName));
        double Elo(string deck) => ratings.TryGetValue(deck, out var e) ? e : 0;
        Deck Load(string name) { var d = Deck.LoadYdk(deckFiles[name]); d.Name = name; return d; }
        // You: the QA player's style (the roster's best planner that knows its deck).
        var you = roster.Where(p => p.PlansAhead && p.KnowsDeck).OrderByDescending(p => p.Skill).First();
        var gus = roster.First(p => p.Id == "old-gus");
        var kit = roster.First(p => p.Id == TagPartners.ShopRegularId);
        Console.WriteLine($"Wall study: {games} duels per option, you play as {you.Name} (skill {you.Skill:0.00}) {youAs}, seed {seed}");

        // What the player has by a wall: the recipes (loaners) and partners from every circuit up to `after`, and from the
        // Tag Cups those circuits open.
        List<Recipe> RecipesBy(int after) => RecipeBook.All(circuits).Where(r => (wholePool || r.Circuit <= after) && deckFiles.ContainsKey(r.Deck)).ToList();
        List<Option> Options(int after, bool tag, params string[] exclude)
        {
            var list = after >= 0 && circuits.All[after].Banlist.Length > 0 ? lists.FirstOrDefault(l => l.Name == circuits.All[after].Banlist) : null;
            var loaners = RecipesBy(after).Select(r => r.Deck).Distinct().Where(d => d != Starter).OrderByDescending(Elo).Take(Math.Max(deckCount, kitCount + 1)).ToList();
            var decks = new List<(string label, string deck)> { ("Mekk-Knight Evolved", Starter) };
            decks.AddRange(loaners.Take(deckCount).Select(d => ($"loaner {d}", d)));
            if (!tag) return decks.Select(d => new Option(d.label, d.deck, null, "")).ToList();
            // Partners: anyone beaten by then, with the deck you first met them with (their first deck); the two strongest.
            var beaten = RecipesBy(after).Select(r => r.Owner).GroupBy(p => p.Id).Select(g => g.First())
                .Where(p => p.Decks.Count > 0 && deckFiles.ContainsKey(p.Decks[0]) && !TagPartners.IsShopRegular(p) && !exclude.Contains(p.Id)).ToList();
            var partners = new List<(string label, Personality who, string deck)> { ("Gus+zombies", gus, "zombies") };
            partners.AddRange(beaten.Where(p => p.Id != gus.Id).OrderByDescending(p => Elo(p.Decks[0])).Take(partnerCount)
                .Select(p => ($"{p.Name}+{p.Decks[0]}", p, p.Decks[0])));
            var options = new List<Option>();
            foreach (var (label, deck) in decks)
            {
                foreach (var (plabel, who, pdeck) in partners)
                    options.Add(new Option($"{label} | {plabel}", deck, who, pdeck));
                // Kit with the strongest loaners that aren't yours (Kit plays sharp: TagPartners.SharpSkill).
                foreach (var kitDeck in loaners.Where(d => d != deck).Take(kitCount))
                    options.Add(new Option($"{label} | Kit+{kitDeck}", deck, TagPartners.Playing(kit, new Profile()), kitDeck));
            }
            return options;
        }

        // The options an advice file already has for a wall, to play again (labels as Options makes them).
        List<Option> Measured(IReadOnlyList<MeasuredOption> measured) => measured.Select(m =>
        {
            var deck = m.Borrows ? m.Deck : Starter;
            var label = m.Borrows ? $"loaner {m.Deck}" : "Mekk-Knight Evolved";
            if (m.Partner.Length == 0) return new Option(label, deck, null, "");
            var who = m.Partner == kit.Id ? TagPartners.Playing(kit, new Profile()) : roster.First(p => p.Id == m.Partner);
            var name = m.Partner == kit.Id ? "Kit" : m.Partner == gus.Id ? "Gus" : who.Name;
            return new Option($"{label} | {name}+{m.PartnerDeck}", deck, who, m.PartnerDeck);
        }).ToList();

        var walls = new List<(string name, string key, int after, Personality them, string theirDeck, Personality? theirPartner, string theirPartnerDeck)>();
        for (int t = 0; t < circuits.TagCups.Count; t++)
        {
            var cup = circuits.TagCups[t];
            if (!cup.Name.StartsWith("Expert") && !cup.Name.StartsWith("Master")) continue;
            for (int m = 0; m < cup.Matches.Count; m++)
            {
                var (lead, partner) = cup.Matches[m];
                walls.Add(($"{cup.Name} {m + 1}: {lead.Deck} + {partner.Deck}", Circuits.TagKey(t, m), cup.After, lead.Character, lead.Deck, partner.Character, partner.Deck));
            }
        }
        var seraph = guests.All.First(g => g.Character.Id == "seraph");
        int lastAdvanced = Enumerable.Range(0, circuits.All.Count).Last(i => circuits.All[i].Band == "Advanced");
        walls.Add(($"Seraph: {seraph.Deck}", GuestList.Key(seraph, false), lastAdvanced, seraph.Character, seraph.Deck, null, ""));
        walls.Add(($"Seraph tag: {seraph.Deck} + {seraph.PartnerDeck}", GuestList.Key(seraph, true), lastAdvanced, seraph.Character, seraph.Deck, seraph.Partner, seraph.PartnerDeck));
        var advice = write.Length > 0 ? WallAdvice.Load(write) : WallAdvice.Empty;
        if (only.Length > 0) walls = walls.Where(w => w.name.Contains(only, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var wall in walls)
        {
            bool tag = wall.theirPartner != null;
            // (Nobody partners you against themselves: the Tag Cup refuses it.)
            var all = youAs.Remeasure ? Measured(advice.For(wall.key)) : Options(wall.after, tag, wall.them.Id, wall.theirPartner?.Id ?? "");
            var options = all.Where(o => o.Label.Contains(youAs.Option, StringComparison.OrdinalIgnoreCase)).ToList();
            if (options.Count == 0) Console.WriteLine($"{wall.name}: no option has \"{youAs.Option}\"; options: {string.Join("; ", all.Select(o => o.Label))}");
            var list = circuits.All[wall.after].Banlist.Length > 0 ? lists.FirstOrDefault(l => l.Name == circuits.All[wall.after].Banlist) ?? Banlist.None : Banlist.None;
            // Your team's decks as the game hands them over: in era-limited play a loaner made legal for the ban list, else
            // as built; a partner's own recipe and the CPUs' recipes always as they are (CampaignState.PartnerPlays).
            if (!youAs.EraLimited) list = Banlist.None;
            Deck Yours(string name) => name == Starter ? Load(name) : Loaners.Build(Load(name), list, cards);
            Deck Partners(string name) => Load(name);
            var wins = new int[options.Count, 2];
            var plays = new int[options.Count, 2];
            int errors = 0;
            Parallel.For(0, options.Count * games, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, k =>
            {
                var o = options[k / games];
                int g = k % games, gameSeed = seed * 1000 + g;
                int first = g % 2; // 0: your team starts
                try
                {
                    bool won = tag
                        ? PlayTag(cards, scripts, gameSeed, first, you, youAs, Yours(o.Deck), o.Partner!, o.Partner == kit || TagPartners.IsShopRegular(o.Partner!) ? Yours(o.PartnerDeck) : Partners(o.PartnerDeck),
                            wall.them, Load(wall.theirDeck), wall.theirPartner!, Load(wall.theirPartnerDeck))
                        : PlayOne(cards, scripts, gameSeed, first, you, Yours(o.Deck), wall.them, Load(wall.theirDeck));
                    lock (wins)
                    {
                        plays[k / games, first]++;
                        if (won) wins[k / games, first]++;
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errors);
                    Console.WriteLine($"    {o.Label} seed {gameSeed}: {ex.Message}");
                }
            });
            Console.WriteLine($"\n{wall.name}  (rules of {circuits.All[wall.after].Name}; them: {wall.them.Name} skill {wall.them.Skill:0.00}"
                              + (tag ? $", {wall.theirPartner!.Name} skill {wall.theirPartner.Skill:0.00})" : ")"));
            Console.WriteLine($"  {"option",-62} {"won",6} {"you first",10} {"you second",11}");
            var order = Enumerable.Range(0, options.Count).OrderByDescending(i => wins[i, 0] + wins[i, 1]).ToList();
            foreach (var i in order)
            {
                int w = wins[i, 0] + wins[i, 1], n = plays[i, 0] + plays[i, 1];
                string Pct(int a, int b) => b == 0 ? "-" : $"{a * 100 / b}%";
                Console.WriteLine($"  {options[i].Label,-62} {Pct(w, n),6} {Pct(wins[i, 0], plays[i, 0]),10} {Pct(wins[i, 1], plays[i, 1]),11}");
            }
            if (errors > 0) Console.WriteLine($"  ({errors} duels failed)");
            if (write.Length > 0)
            {
                int Pc(int a, int b) => b == 0 ? -1 : a * 100 / b;
                // With --option, only the options played are written (merged into what the file has); else the wall's whole list.
                Action<string, IEnumerable<MeasuredOption>> store = youAs.Option.Length > 0 ? advice.Merge : advice.Set;
                store(wall.key, Enumerable.Range(0, options.Count).Select(i => new MeasuredOption
                {
                    Deck = options[i].Deck == Starter ? MeasuredOption.YourDeck : options[i].Deck,
                    Partner = options[i].Partner?.Id ?? "", PartnerDeck = options[i].Partner != null ? options[i].PartnerDeck : "",
                    Percent = Pc(wins[i, 0] + wins[i, 1], plays[i, 0] + plays[i, 1]), YouFirst = Pc(wins[i, 0], plays[i, 0]), YouSecond = Pc(wins[i, 1], plays[i, 1]),
                    Games = plays[i, 0] + plays[i, 1],
                }));
                advice.Save(write, "Measured by `--wall-study` (src/Ygo.Harness/WallStudy.cs): each option's wins in 100 at a hard match, best first.\n"
                                   + "deck=yours is your own deck (measured with the Mekk-Knight Evolved starter); anything else is a recipe to borrow.\n"
                                   + $"Your stand-in plays {youAs}; first= and second= are by who took the first turn.\n"
                                   + "The Stuck? advice names the best option the player has.");
            }
        }
        Console.WriteLine($"\nDone in {timer.Elapsed.TotalMinutes:0.0} min");
        return 0;
    }

    private static IDuelist Bot(Personality style, string deckName, int salt, DuelContext context, DuelRecipe recipe, ResponseLog answers)
    {
        if (!style.PlansAhead)
            return new SmartDuelist(style.Name, salt, context, personality: style, playbook: style.KnowsDeck ? Playbooks.ForDeck(deckName) : null);
        return new SmartDuelist(style.Name, salt, context, personality: style, playbook: Playbooks.ForDeck(deckName),
            planner: new TurnPlanner(recipe, answers, style, () => Playbooks.ForDeck(deckName)));
    }

    private static bool PlayOne(ICardDataProvider cards, IScriptProvider scripts, int gameSeed, int first, Personality you, Deck mine,
        Personality them, Deck theirs)
    {
        var recipe = new DuelRecipe
        {
            Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)gameSeed, FirstPlayer = first },
            Deck0 = mine.Clone(), Deck1 = theirs.Clone(),
        };
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var answers = new ResponseLog();
        // --keep-replays: the duel as a replay, with how each seat's bot was built (the seats shown to its watcher, answers untouched).
        var kept = ReplayKeeper.Current?.Begin("wall", $"s{gameSeed}-{mine.Name}-vs-{theirs.Name}", recipe, duel, answers, new[] { you.Name, them.Name },
            new[] { SeatSpec.Of(0, you, mine.Name, gameSeed * 4, you.PlansAhead), SeatSpec.Of(1, them, theirs.Name, gameSeed * 4 + 1, them.PlansAhead) });
        var runner = new DuelRunner(duel, ReplayKeeper.Wrap(kept, Bot(you, mine.Name, gameSeed * 4, context, recipe, answers)),
            ReplayKeeper.Wrap(kept, Bot(them, theirs.Name, gameSeed * 4 + 1, context, recipe, answers)), answers);
        runner.MessageReceived += context.Observe;
        YataLock.Attach(runner, duel);
        ReplayKeeper.Attach(kept, runner);
        var result = runner.Run(20000);
        ReplayKeeper.Current?.Finish(kept, result);
        return result.Winner == 0;
    }

    private static bool PlayTag(ICardDataProvider cards, IScriptProvider scripts, int gameSeed, int first, Personality you, YouAs youAs, Deck mine,
        Personality partner, Deck partnerDeck, Personality them, Deck theirs, Personality theirPartner, Deck theirPartnerDeck)
    {
        var settings = new DuelSettings { Seed = (ulong)gameSeed, FirstPlayer = first };
        var recipe = TagDuel.Recipe(cards, scripts, settings, mine.Clone(), partnerDeck.Clone(), theirs.Clone(), theirPartnerDeck.Clone());
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var answers = new ResponseLog();
        IDuelist yours = youAs.Plans ? Bot(you, mine.Name, gameSeed * 4, context, recipe, answers)
            : new SmartDuelist(you.Name, gameSeed * 4, context, personality: you, playbook: Playbooks.ForDeck(mine));
        if (youAs.ChainAuto) yours = new AskedOnly(yours, context);
        var (a, b) = TagDuel.Seats(yours, Bot(partner, partnerDeck.Name, gameSeed * 4 + 2, context, recipe, answers),
            Bot(them, theirs.Name, gameSeed * 4 + 1, context, recipe, answers), Bot(theirPartner, theirPartnerDeck.Name, gameSeed * 4 + 3, context, recipe, answers), first);
        var kept = ReplayKeeper.Current?.Begin("wall-tag", $"s{gameSeed}-{mine.Name}-vs-{theirs.Name}", recipe, duel, answers, new[] { you.Name, them.Name },
            new[] { SeatSpec.Of(0, you, youAs.Plans ? mine.Name : Playbooks.RecipeOf(mine), gameSeed * 4, youAs.Plans && you.PlansAhead, "",
                        youAs.ChainAuto ? "asked-only" : ""),
                    SeatSpec.Of(2, partner, partnerDeck.Name, gameSeed * 4 + 2, partner.PlansAhead),
                    SeatSpec.Of(1, them, theirs.Name, gameSeed * 4 + 1, them.PlansAhead),
                    SeatSpec.Of(3, theirPartner, theirPartnerDeck.Name, gameSeed * 4 + 3, theirPartner.PlansAhead) });
        var runner = new DuelRunner(duel, ReplayKeeper.Wrap(kept, a), ReplayKeeper.Wrap(kept, b), answers);
        runner.MessageReceived += context.Observe;
        runner.MessageReceived += a.Observe;
        runner.MessageReceived += b.Observe;
        YataLock.Attach(runner, duel);
        ReplayKeeper.Attach(kept, runner);
        var result = runner.Run(20000);
        ReplayKeeper.Current?.Finish(kept, result);
        return result.Winner == 0;
    }
}
