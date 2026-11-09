using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Protocol;

namespace Ygo.Harness;

/// <summary>
/// `--combo-interrupt deck [--games N] [--seed N]` (docs/dev/HEADLINERS-2026-11-01.md): the goldfish again, against a stand-in
/// that holds one era-fair answer (a hand trap in its opening hand, or a Trap Set before the duel). The stand-in plays as the
/// dummy (it sets a monster, never attacks) and answers as the skill-1.0 bot does. Each answer's duels use the goldfish's seeds,
/// so they pair up: of the duels the headliner's route finishes unopposed, how many the answer stops (no payoff), the
/// target for the answer Baz's tell names being 60%. Also how often the stand-in used it, and how often the headliner won.
/// </summary>
public static class ComboInterrupt
{
    /// <summary>Where an answer starts: in the stand-in's hand, or Set in its Spell/Trap Zone.</summary>
    public enum Where { Hand, Set }

    /// <summary>The era-fair answers per headliner: the cards a player of its year had for it (its tell names the first that works).</summary>
    public static readonly IReadOnlyDictionary<string, (string name, uint code, Where where)[]> Answers = new Dictionary<string, (string, uint, Where)[]>
    {
        // 2004: the wipe is Chaos Emperor Dragon's summon and its ignition; the lock needs Sangan or Witch to search.
        ["yata-mags"] = new[]
        {
            ("Solemn Judgment", 41420027u, Where.Set), ("Torrential Tribute", 53582587u, Where.Set), ("D.D. Crow", 24508238u, Where.Hand),
            ("Magic Jammer", 77414722u, Where.Set), ("Ring of Destruction", 83555667u, Where.Set),
        },
        // 2005: Return from the Different Dimension waits Set a turn; Royal Decree and the Spell/Trap removal answer it.
        ["return-rook"] = new[]
        {
            ("Mystical Space Typhoon", 5318639u, Where.Hand), ("Dust Tornado", 60082869u, Where.Set), ("Royal Decree", 51452091u, Where.Set),
            ("Seven Tools of the Bandit", 3819470u, Where.Set), ("Solemn Judgment", 41420027u, Where.Set), ("D.D. Crow", 24508238u, Where.Hand),
        },
        // 2010: the Graveyard answers (Ronintoadin), the summon negations (Substitoad), Effect Veiler on Substitoad.
        ["mass-driver-moss"] = new[]
        {
            ("D.D. Crow", 24508238u, Where.Hand), ("Macro Cosmos", 30241314u, Where.Set), ("Effect Veiler", 97268402u, Where.Hand),
            ("Solemn Warning", 84749824u, Where.Set), ("Bottomless Trap Hole", 29401950u, Where.Set),
        },
        // 2011: negate the Synchro Summon, or take the Tuner's target from the GY.
        ["trishula-trish"] = new[]
        {
            ("Solemn Warning", 84749824u, Where.Set), ("D.D. Crow", 24508238u, Where.Hand), ("Effect Veiler", 97268402u, Where.Hand),
            ("Solemn Judgment", 41420027u, Where.Set), ("Bottomless Trap Hole", 29401950u, Where.Set),
        },
        // 2017: the first search (Resort, Terraforming) is the whole board.
        ["master-plan-marlo"] = new[]
        {
            ("Ash Blossom & Joyous Spring", 14558127u, Where.Hand), ("Ghost Ogre & Snow Rabbit", 59438930u, Where.Hand),
            ("Droll & Lock Bird", 94145021u, Where.Hand), ("Effect Veiler", 97268402u, Where.Hand), ("Solemn Strike", 40605147u, Where.Set),
        },
        // 2018: the Gouki searches (Ash, Ghost Belle on the GY ones, Droll after the first), Impermanence on Suprex.
        ["gumblar-gail"] = new[]
        {
            ("Ash Blossom & Joyous Spring", 14558127u, Where.Hand), ("Droll & Lock Bird", 94145021u, Where.Hand),
            ("Ghost Belle & Haunted Mansion", 73642296u, Where.Hand), ("Infinite Impermanence", 10045474u, Where.Set),
            ("Effect Veiler", 97268402u, Where.Hand), ("Solemn Strike", 40605147u, Where.Set),
        },
        // 2010: the swarm starts with Kageki's Normal Summon and Gateway's searches.
        ["gateway-gwen"] = new[]
        {
            ("Solemn Warning", 84749824u, Where.Set), ("Effect Veiler", 97268402u, Where.Hand), ("Bottomless Trap Hole", 29401950u, Where.Set),
            ("Mystical Space Typhoon", 5318639u, Where.Hand), ("Solemn Judgment", 41420027u, Where.Set),
        },
        // 2004: the turtle needs its Normal Summon and the scientist its fusion.
        ["scientist-turtle"] = new[]
        {
            ("Trap Hole", 4206964u, Where.Set), ("Torrential Tribute", 53582587u, Where.Set), ("Ring of Destruction", 83555667u, Where.Set),
            ("Solemn Judgment", 41420027u, Where.Set),
        },
    };

    /// <summary>Plays as the dummy (sets a monster, never attacks) and answers chains, targets and costs as the skill-1.0 bot.</summary>
    private sealed class AnswerHolder : IDuelist
    {
        private readonly DummyDuelist _dummy;
        private readonly SmartDuelist _smart;

        public AnswerHolder(int seed, DuelContext context)
        {
            _dummy = new DummyDuelist(seed, context.Cards);
            _smart = new SmartDuelist("The stand-in", seed, context, personality: new Personality { Id = "alley-stand-in", Name = "The stand-in", Skill = 1.0 });
        }

        public byte[]? Respond(Prompt prompt, int attempt) =>
            prompt is IdlePrompt or BattlePrompt ? _dummy.Respond(prompt, attempt) : _smart.Respond(prompt, attempt);

        public void OnPhaseChanged()
        {
            _dummy.OnPhaseChanged();
            _smart.OnPhaseChanged();
        }
    }

    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, string deckName, int games, int seed)
    {
        var deck = ComboGoldfish.LoadDeck(root, deckName);
        if (deck == null || Playbooks.ForDeck(deck) == null) { Console.WriteLine($"no headliner deck {deckName} with a playbook"); return 1; }
        if (!Answers.TryGetValue(deckName, out var answers)) { Console.WriteLine($"no era-fair answers listed for {deckName}"); return 1; }
        var dummy = ComboGoldfish.Dummy(root);
        var unopposed = Enumerable.Range(0, games).Select(g => ComboGoldfish.Play(cards, scripts, deck, dummy, g, seed)).ToList();
        int finished = unopposed.Count(r => r.Finished);
        int tried = unopposed.Count(r => r.FirstTry > 0);
        int free = unopposed.Count(r => r.Fizzled && !r.Finished);
        Console.WriteLine($"{deckName}: {games} duels per answer, half going first; unopposed the route finishes {Pct(finished, games)} ({finished}), its first try "
                          + $"fizzles {Pct(unopposed.Count(r => r.FirstTry == 2), tried)} of {tried}, fizzles and never lands {Pct(free, games)}, the headliner wins {Pct(unopposed.Count(r => r.Winner == 0), games)}");
        Console.WriteLine($"  {"answer",-30} {"used",6} {"1st try stopped",16} {"payoff",7} {"never lands",12} {"of",5} {"wins",6}");
        foreach (var (name, code, where) in answers)
        {
            var placed = new[] { where == Where.Hand ? new PlacedCard(1, code, Location.Hand) : new PlacedCard(1, code, Location.SZone, 0, Position.FaceDownDefense) };
            int used = 0, paid = 0, stops = 0, wins = 0, triedHere = 0, firstStopped = 0;
            for (int g = 0; g < games; g++)
            {
                bool fired = false;
                var game = ComboGoldfish.Play(cards, scripts, deck, dummy, g, seed, (s, ctx) => new AnswerHolder(s, ctx), placed,
                    attach: (runner, _) => runner.MessageReceived += m =>
                    {
                        if (m[0] != Msg.Chaining || fired) return;
                        var r = new MessageReader(m, 1);
                        var c = r.U32();
                        var at = r.LocInfo(c);
                        if (at.Controller == 1 && Ygo.Engine.DeckBuilding.Banlist.BaseCode(c, cards) == code) fired = true;
                    });
                if (fired) used++;
                if (game.Finished) paid++;
                if (game.Winner == 0) wins++;
                if (unopposed[g].Finished && !game.Finished) stops++;
                if (game.FirstTry > 0) triedHere++;
                if (game.FirstTry == 2) firstStopped++;
            }
            Console.WriteLine($"  {name,-30} {Pct(used, games),6} {Pct(firstStopped, triedHere) + $" of {triedHere}",16} {Pct(paid, games),7} {Pct(stops, finished),12} {finished,5} {Pct(wins, games),6}");
        }
        Console.WriteLine("  (used: the stand-in activated it; 1st try stopped: the headliner's first route fizzled, the show's fizzle; payoff: the route reached its payoff");
        Console.WriteLine("   that duel; never lands: of the duels it finishes unopposed, those with no payoff at all; wins: the headliner's)");
        return 0;
    }

    private static string Pct(int n, int of) => $"{100.0 * n / Math.Max(1, of):0}%";
}
