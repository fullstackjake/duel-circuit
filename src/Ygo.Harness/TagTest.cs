using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Protocol;

namespace Ygo.Harness;

/// <summary>
/// `--tag-test [--games N] [--seed N]`: tag duels (2 vs 2, shared field and LP, the core swapping each team's duelist on
/// its turns). Checks that the swaps happen, that both duelists of each team take turns and answer prompts, that
/// planning bots work with partner decks in their scratch duels, and that duels finish without errors.
/// </summary>
public static class TagTest
{
    /// <summary>Counts the prompts a duelist answers, and the turns it starts.</summary>
    private sealed class Counted : IDuelist
    {
        private readonly IDuelist _inner;
        public int Prompts;
        public Counted(IDuelist inner) => _inner = inner;
        public void OnPhaseChanged() => _inner.OnPhaseChanged();
        public byte[]? Respond(Prompt prompt, int attempt)
        {
            Prompts++;
            return _inner.Respond(prompt, attempt);
        }
    }

    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, int games, int seed)
    {
        int failures = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        Deck Load(string name)
        {
            var file = Directory.GetFiles(Path.Combine(root, "decks"), name + ".ydk", SearchOption.AllDirectories).First();
            var deck = Deck.LoadYdk(file);
            deck.Name = name;
            return deck;
        }

        // Team A: Blue-Eyes and warriors; team B: zombies and gadgets (simple decks, quick duels); then planners.
        var matchups = new[]
        {
            (new[] { "classic", "warriors", "zombies", "gadgets" }, "smart"),
            (new[] { "dragon-ruler", "hat", "burning-abyss", "lightsworn" }, "playbook"),
            (new[] { "snake-eye", "sky-striker", "nekroz", "spyral" }, "expert"),
        };
        foreach (var (names, kind) in matchups)
        {
            var decks = names.Select(Load).ToArray();
            int finished = 0, errors = 0, swapsSeen = 0, winsA = 0;
            var firstOrder = new List<string>();
            var secondOrder = new List<string>();
            var turnsBy = new int[4];
            var promptsBy = new int[4];
            int count = kind == "expert" ? Math.Max(1, games / 4) : games;
            for (int g = 0; g < count; g++)
            {
                int gameSeed = seed + g;
                // Who starts alternates (the coin toss): team A on even duels, team B on odd ones.
                var settings = new DuelSettings { Seed = (ulong)gameSeed, FirstPlayer = g % 2 };
                var recipe = TagDuel.Recipe(cards, scripts, settings, decks[0], decks[1], decks[2], decks[3]);
                using var duel = recipe.Create();
                duel.Log += (text, type) => { if (type == 0) errors++; };
                var context = new DuelContext(duel, cards);
                var answers = new ResponseLog();
                var members = new Counted[4];
                IDuelist Make(int i)
                {
                    var deck = decks[i];
                    var playbook = kind == "smart" ? null : Playbooks.ForDeck(deck);
                    var planner = kind == "expert" ? new TurnPlanner(recipe, answers, Personality.Balanced, () => Playbooks.ForDeck(deck)) : null;
                    return members[i] = new Counted(new SmartDuelist(deck.Name, gameSeed * 4 + i, context, null, Personality.Balanced, playbook, planner));
                }
                var (teamA, teamB) = TagDuel.Seats(Make(0), Make(1), Make(2), Make(3), settings.FirstPlayer);
                var runner = new DuelRunner(duel, teamA, teamB, answers);
                runner.MessageReceived += context.Observe;
                runner.MessageReceived += teamA.Observe;
                runner.MessageReceived += teamB.Observe;
                // The core announces a new turn, then swaps; who plays it shows at the turn's first phase.
                int turnPlayer = -1;
                var order = new List<string>();
                runner.MessageReceived += m =>
                {
                    if (m[0] == Msg.TagSwap) swapsSeen++;
                    if (m[0] == Msg.NewTurn) turnPlayer = m[1];
                    if (m[0] == Msg.NewPhase && turnPlayer >= 0)
                    {
                        int who = turnPlayer == 0 ? TagDuel.Role(teamA) : 2 + TagDuel.Role(teamB);
                        turnsBy[who]++;
                        if (order.Count < 8) order.Add(new[] { "A1", "A2", "B1", "B2" }[who]);
                        turnPlayer = -1;
                    }
                };
                try
                {
                    var result = runner.Run(20000);
                    if (result.Winner is 0 or 1 or 2) finished++;
                    if (result.Winner == 0) winsA++;
                }
                catch (Exception ex) { Console.WriteLine($"    game {gameSeed} failed: {ex.Message}"); }
                for (int i = 0; i < 4; i++) promptsBy[i] += members[i].Prompts;
                if (g == 0) firstOrder = order;
                if (g == 1) secondOrder = order;
            }
            Console.WriteLine($"\n{string.Join(" & ", names.Take(2))}  vs  {string.Join(" & ", names.Skip(2))}  ({kind}, {count} duels)");
            Console.WriteLine($"  team A won {winsA}; turns started by each duelist (A1, A2, B1, B2): {string.Join(", ", turnsBy)}; prompts: {string.Join(", ", promptsBy)}");
            Check($"turns go A1, B1, A2, B2 like Tag Force ({string.Join(" ", firstOrder)})", string.Join(" ", firstOrder.Take(4)) == "A1 B1 A2 B2");
            if (count > 1)
                Check($"with team B starting, B1, A1, B2, A2 ({string.Join(" ", secondOrder)})", string.Join(" ", secondOrder.Take(4)) == "B1 A1 B2 A2");
            Check($"every duel finished ({finished}/{count})", finished == count);
            Check($"tag swaps happened ({swapsSeen})", swapsSeen >= count * 2);
            Check("all four duelists took turns and answered prompts", turnsBy.All(t => t > 0) && promptsBy.All(p => p > 0));
            Check($"no script errors ({errors})", errors == 0);
        }
        // You as team A's lead (random clicks through the same choice lists the game shows), a bot partner.
        {
            var d = new[] { "classic", "warriors", "zombies", "gadgets" }.Select(Load).ToArray();
            var stats = new UiTestStats();
            int done = 0, clicks = 0;
            for (int g = 0; g < 10; g++)
            {
                int gameSeed = seed + 100 + g;
                var settings = new DuelSettings { Seed = (ulong)gameSeed, FirstPlayer = g % 2 }; // you start half of them
                var recipe = TagDuel.Recipe(cards, scripts, settings, d[0], d[1], d[2], d[3]);
                using var duel = recipe.Create();
                var context = new DuelContext(duel, cards);
                var you = new Counted(new ScriptedClicker(new Ygo.Engine.Interactive.HumanDuelist(context, gameSeed), gameSeed, stats));
                SmartDuelist Bot(int i) => new SmartDuelist(d[i].Name, gameSeed * 4 + i, context);
                var (a, b) = TagDuel.Seats(you, Bot(1), Bot(2), Bot(3), settings.FirstPlayer);
                var runner = new DuelRunner(duel, a, b);
                runner.MessageReceived += context.Observe;
                runner.MessageReceived += a.Observe;
                runner.MessageReceived += b.Observe;
                try { if (runner.Run(20000).Winner is 0 or 1 or 2) done++; }
                catch (Exception ex) { Console.WriteLine($"    game {gameSeed} failed: {ex.Message}"); }
                clicks += you.Prompts;
            }
            Console.WriteLine($"\nYou + bot partner: {clicks} prompts answered by you over 10 duels");
            Check($"tag duels with you as a lead finish ({done}/10) and you get prompts", done == 10 && clicks > 0);
        }

        // A tag duel's replay: recorded, turned into a share code, read back, and played to the same end.
        {
            var d = new[] { "classic", "warriors", "zombies", "gadgets" }.Select(Load).ToArray();
            var replay = new Ygo.Engine.Replays.Replay
            {
                Settings = new DuelSettings { Seed = (ulong)seed, FirstPlayer = 1 }, // team B starts: the replay must keep that
                Decks = new[] { d[0].Clone(), d[2].Clone() }, DeckNames = new[] { d[0].Name, d[2].Name },
                Names = new[] { "Lead A", "Lead B" }, Characters = new[] { "", "" },
                Partners = new[] { d[1].Clone(), d[3].Clone() }, PartnerDeckNames = new[] { d[1].Name, d[3].Name },
                PartnerNames = new[] { "Partner A", "Partner B" }, PartnerCharacters = new[] { "", "" },
            };
            var recipe = replay.Recipe(cards, scripts);
            var answers = new ResponseLog();
            ulong Hash(DuelRunner runner, Func<DuelResult> run, out DuelResult result)
            {
                ulong h = 14695981039346656037UL;
                runner.MessageReceived += m => { foreach (var b in m) { h ^= b; h *= 1099511628211UL; } };
                result = run();
                return h;
            }
            ulong original;
            DuelResult first;
            using (var duel = recipe.Create())
            {
                var context = new DuelContext(duel, cards);
                SmartDuelist Bot(int i) => new SmartDuelist(d[i].Name, seed * 4 + i, context);
                var (a, b) = TagDuel.Seats(Bot(0), Bot(1), Bot(2), Bot(3), replay.Settings.FirstPlayer);
                var runner = new DuelRunner(duel, a, b, answers);
                runner.MessageReceived += context.Observe;
                runner.MessageReceived += a.Observe;
                runner.MessageReceived += b.Observe;
                original = Hash(runner, () => runner.Run(20000), out first);
            }
            replay.Responses = answers.Responses.ToList();
            replay.Winner = first.Winner;
            replay.Turns = first.Turns;
            var loaded = Ygo.Engine.Replays.Replay.FromCode(replay.ToCode());
            ulong replayed = 0;
            DuelResult second = new DuelResult();
            if (loaded != null)
                using (var duel = loaded.Recipe(cards, scripts).Create())
                {
                    var feed = new Ygo.Engine.Replays.ReplayFeed(loaded.Responses);
                    var runner = new DuelRunner(duel, new Ygo.Engine.Replays.ReplayDuelist(feed), new Ygo.Engine.Replays.ReplayDuelist(feed));
                    replayed = Hash(runner, () => runner.Run(20000), out second);
                }
            Console.WriteLine($"\nTag replay: {loaded?.Title}");
            Check("a tag duel's replay code reads back as a tag duel and plays to the same end",
                loaded != null && loaded.IsTag && replayed == original && second.Winner == first.Winner && second.Turns == first.Turns);
        }

        Console.WriteLine(failures == 0 ? "\nAll tag duel checks passed." : $"\n{failures} tag duel check(s) failed.");
        return failures == 0 ? 0 : 1;
    }
}
