using Ygo.Engine;
using Ygo.Engine.AI;

namespace Ygo.Harness;

/// <summary>
/// `--first-turn-test`: who takes the first turn (DuelSettings.FirstPlayer, the core patch in patches/ygopro-core).
/// One on one: the starter's first turn has no draw, the other's has. Tag: each duelist gets a deck of one card, so
/// the hand shows who really plays each turn; the order must be the starting team's lead, the other lead, the starting
/// team's partner, the other partner, and TagDuel.Role must agree.
/// </summary>
public static class FirstTurnTest
{
    private const uint Stone = 13039848, Skull = 70781052, Magician = 46986414, BlueEyes = 89631139;

    public static int Run(ICardDataProvider cards, IScriptProvider scripts)
    {
        int bad = 0;
        void Check(string what, bool ok) { if (!ok) bad++; Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}"); }
        Deck Of(uint code, string name) { var d = new Deck { Name = name }; while (d.Main.Count < 40) d.Main.Add(code); return d; }

        foreach (int first in new[] { 0, 1 })
        {
            var settings = new DuelSettings { Seed = 3, FirstPlayer = first };
            var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = settings, Deck0 = Of(Stone, "a"), Deck1 = Of(Skull, "b") };
            using var duel = recipe.Create();
            var context = new DuelContext(duel, cards);
            var runner = new DuelRunner(duel, new SmartDuelist("A", 1, context), new SmartDuelist("B", 2, context));
            runner.MessageReceived += context.Observe;
            var starters = new List<int>();
            var handAtMain = new List<int>();
            int turnPlayer = -1;
            runner.MessageReceived += m =>
            {
                if (m[0] == Msg.NewTurn) { turnPlayer = m[1]; starters.Add(m[1]); }
                if (m[0] == Msg.NewPhase && BitConverter.ToUInt16(m, 1) == Phase.Main1 && turnPlayer >= 0 && handAtMain.Count < starters.Count)
                    handAtMain.Add(duel.QueryLocation((byte)turnPlayer, Location.Hand).Count);
            };
            for (int i = 0; i < 20000 && starters.Count < 3 && runner.Step(); i++) { }
            Check($"one on one, player {first} to start: turns go {string.Join(", ", starters)}; hands at Main Phase 1 {string.Join(", ", handAtMain)} (no draw on turn 1)",
                starters.Take(3).SequenceEqual(new[] { first, 1 - first, first }) && handAtMain.Count >= 2 && handAtMain[0] == 5 && handAtMain[1] == 6);
        }

        foreach (int first in new[] { 0, 1 })
        {
            var settings = new DuelSettings { Seed = 4, FirstPlayer = first };
            // Team A: lead Stone, partner Skull; team B: lead Magician, partner Blue-Eyes.
            var recipe = TagDuel.Recipe(cards, scripts, settings, Of(Stone, "A lead"), Of(Skull, "A partner"), Of(Magician, "B lead"), Of(BlueEyes, "B partner"));
            using var duel = recipe.Create();
            var context = new DuelContext(duel, cards);
            IDuelist Bot(int i) => new SmartDuelist($"bot{i}", 10 + i, context);
            var (a, b) = TagDuel.Seats(Bot(0), Bot(1), Bot(2), Bot(3), first);
            var runner = new DuelRunner(duel, a, b);
            runner.MessageReceived += context.Observe;
            runner.MessageReceived += a.Observe;
            runner.MessageReceived += b.Observe;
            var names = new Dictionary<uint, string> { [Stone] = "A lead", [Skull] = "A partner", [Magician] = "B lead", [BlueEyes] = "B partner" };
            var byHand = new List<string>();
            var byRole = new List<string>();
            int turnPlayer = -1;
            runner.MessageReceived += m =>
            {
                if (m[0] == Msg.NewTurn) turnPlayer = m[1];
                if (m[0] == Msg.NewPhase && BitConverter.ToUInt16(m, 1) == Phase.Main1 && turnPlayer >= 0)
                {
                    var hand = duel.QueryLocation((byte)turnPlayer, Location.Hand);
                    byHand.Add(hand.FirstOrDefault(c => c != null) is FieldCard c ? names.GetValueOrDefault(c.Code, "?") : "?");
                    var seat = turnPlayer == 0 ? a : b;
                    byRole.Add($"{(turnPlayer == 0 ? "A" : "B")} {(TagDuel.Role(seat) == 0 ? "lead" : "partner")}");
                    turnPlayer = -1;
                }
            };
            for (int i = 0; i < 40000 && byHand.Count < 4 && runner.Step(); i++) { }
            var want = first == 0 ? new[] { "A lead", "B lead", "A partner", "B partner" } : new[] { "B lead", "A lead", "B partner", "A partner" };
            Check($"tag, team {(first == 0 ? "A" : "B")} to start: {string.Join(", ", byHand)} (seats say {string.Join(", ", byRole)})",
                byHand.SequenceEqual(want) && byRole.SequenceEqual(want));
        }
        // The toss: decks that would rather go second, under their file names and campaign titles alike.
        foreach (var (deck, first) in new[] { ("mekk-knight-invoked", false), ("Mekk-Knight Invoked (3)", false), ("Mekk-Knight Invoked", false), ("Mekk-Knight Invoked Evolved", false),
                     ("eldlich", true), ("Tenpai Dragon", false), ("classic", true), ("Stall Burn (new)", Ygo.Engine.AI.Playbooks.Playbooks.GoesFirst("stall-burn")),
                     ("branded-despia", true), ("exosister", true), ("Unchained", true) })
            Check($"the toss: {deck} {(first ? "goes first" : "goes second")}", Ygo.Engine.AI.Playbooks.Playbooks.GoesFirst(deck) == first);
        Console.WriteLine(bad == 0 ? "first turn test: all passed" : $"first turn test: {bad} failed");
        return bad == 0 ? 0 : 1;
    }
}
