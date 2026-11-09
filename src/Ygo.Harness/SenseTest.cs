using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Presentation;

namespace Ygo.Harness;

/// <summary>
/// `--sense-test`: the CPU's rules sense on set boards, one turn each (CPU going first, a passive opponent).
/// Allure of Darkness isn't cast without a DARK monster in hand to spare, and is when there is one; an Acid Golem with no material left (a 2,000 LP upkeep) goes as the tribute for a Tribute Summon.
/// Aleister the Invoker fetches Invocation when Normal Summoned, and the duel log says so (a search is shown, so it is named).
/// </summary>
public static class SenseTest
{
    private const uint Allure = 1475311, Shura = 58820853, Kalut = 85215458, AcidGolem = 81330115, SummonedSkull = 70781052,
        Mst = 5318639, Stone = 13039848, Aleister = 86120751, Invocation = 74063034,
        Transcendent = 18969888, Mechaba = 75286621, BlueEyes = 89631139;

    private sealed record Case(string Name, PlacedCard[] Setup, Func<List<string>, Duel, bool> Expect, double Skill = 1.0, int FirstPlayer = 0, uint StartingLP = 8000);

    public static int Run(ICardDataProvider cards, IScriptProvider scripts)
    {
        var cases = new[]
        {
            new Case("doesn't cast Allure of Darkness without a DARK monster to spare (Shura is its only monster)",
                new[] { new PlacedCard(0, Allure, Location.Hand), new PlacedCard(0, Shura, Location.Hand), new PlacedCard(0, Mst, Location.Hand), new PlacedCard(0, Mst, Location.Hand) },
                (log, _) => !log.Any(l => l.Contains("Allure of Darkness"))),
            new Case("casts Allure of Darkness with a spare DARK monster (Kalut beside Shura)",
                new[] { new PlacedCard(0, Allure, Location.Hand), new PlacedCard(0, Shura, Location.Hand), new PlacedCard(0, Kalut, Location.Hand), new PlacedCard(0, Mst, Location.Hand) },
                (log, _) => log.Any(l => l.Contains("activates Allure of Darkness"))),
            new Case("doesn't make Acid Golem (it stops our Special Summons) when it can't win this turn",
                new[] { new PlacedCard(0, Stone, Location.MZone, 1), new PlacedCard(0, Stone, Location.MZone, 3), new PlacedCard(0, AcidGolem, Location.Extra) },
                (log, _) => !log.Any(l => l.Contains("Special Summons Number 30"))),
            new Case("tributes an Acid Golem with no material for Summoned Skull",
                new[] { new PlacedCard(0, AcidGolem, Location.MZone, 2), new PlacedCard(0, SummonedSkull, Location.Hand) },
                (log, duel) => duel.QueryLocation(0, Location.MZone).All(c => c?.Code != AcidGolem) && log.Any(l => l.Contains("Summoned Skull"))),
            new Case("summons Aleister the Invoker and adds Invocation from the Deck (planning)",
                new[] { new PlacedCard(0, Aleister, Location.Hand), new PlacedCard(0, Invocation, Location.Deck) },
                (log, duel) => duel.QueryLocation(0, Location.Hand).Any(c => c?.Code == Invocation)
                    && log.Any(l => l.Contains("CPU adds Invocation from the Deck to the hand"))),
            new Case("summons Aleister the Invoker and adds Invocation from the Deck (card by card, skill 0.7)",
                new[] { new PlacedCard(0, Aleister, Location.Hand), new PlacedCard(0, Invocation, Location.Deck) },
                (log, duel) => duel.QueryLocation(0, Location.Hand).Any(c => c?.Code == Invocation), Skill: 0.7),
            // Going second, into Sangenpai Transcendent Dragion: its monsters must attack, so it can't just end the
            // Battle Phase (it used to try forever and the duel was abandoned).
            new Case("attacks when it must (Sangenpai Transcendent Dragion), going second",
                new[] { new PlacedCard(0, SummonedSkull, Location.MZone, 2), new PlacedCard(1, Transcendent, Location.MZone, 2) },
                (log, _) => log.Any(l => l.Contains("must attack")), FirstPlayer: 1),
            // Aleister the Invoker's hand effect (+1000 ATK/DEF to a Fusion Monster, this turn only) is for a battle it turns
            // or for lethal; otherwise both copies stay in hand.
            // (It Normal Summons one Aleister for Invocation; the others stay in hand for the boost.)
            new Case("keeps two Aleisters in hand when +1000 changes nothing (direct attacks that aren't lethal)",
                new[] { new PlacedCard(0, Mechaba, Location.MZone, 2), new PlacedCard(0, Aleister, Location.Hand), new PlacedCard(0, Aleister, Location.Hand), new PlacedCard(0, Aleister, Location.Hand) },
                (log, duel) => !log.Any(l => l.Contains("activates Aleister the Invoker (from Hand)")) && log.Any(l => l.Contains("takes 2500"))
                               && duel.QueryLocation(0, Location.Hand).Count(c => c?.Code == Aleister) == 2, FirstPlayer: 1),
            new Case("attacks a bigger monster and uses Aleister to get over it (Mechaba 2500 + 1000 into Blue-Eyes 3000)",
                new[] { new PlacedCard(0, Mechaba, Location.MZone, 2), new PlacedCard(0, Aleister, Location.Hand), new PlacedCard(0, Aleister, Location.Hand), new PlacedCard(1, BlueEyes, Location.MZone, 2) },
                (log, duel) => log.Any(l => l.Contains("activates Aleister the Invoker (from Hand)"))
                               && duel.QueryLocation(1, Location.MZone).All(c => c?.Code != BlueEyes), FirstPlayer: 1),
            new Case("uses Aleister when +1000 makes the turn's attacks lethal (Mechaba 2500 and Aleister 1000 into 4000 LP)",
                new[] { new PlacedCard(0, Mechaba, Location.MZone, 2), new PlacedCard(0, Aleister, Location.Hand), new PlacedCard(0, Aleister, Location.Hand) },
                (log, _) => log.Any(l => l.Contains("activates Aleister the Invoker (from Hand)")) && log.Any(l => l.Contains("CPU wins")), FirstPlayer: 1, StartingLP: 4000),
        };
        int bad = 0;
        foreach (var c in cases)
        {
            var filler = new Deck { Name = "filler" };
            while (filler.Main.Count < 40) filler.Main.Add(Stone);
            var recipe = new DuelRecipe
            {
                Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = 7, StartingHand = 0, FirstPlayer = c.FirstPlayer, StartingLP = c.StartingLP },
                Deck0 = filler, Deck1 = filler.Clone(),
            };
            recipe.Setup.AddRange(c.Setup);
            using var duel = recipe.Create();
            var context = new DuelContext(duel, cards);
            var log = new List<string>();
            var cpu = new SmartDuelist("CPU", 3, context, s => log.Add("> " + s), new Personality { Name = "CPU", Skill = c.Skill });
            var passive = new SmartDuelist("Passive", 4, context, null, new Personality { Name = "Passive", Skill = 0.0 });
            var runner = new DuelRunner(duel, cpu, passive);
            runner.MessageReceived += context.Observe;
            runner.MessageReceived += m => { if (m[0] == Msg.NewTurn || m[0] == Msg.NewPhase) cpu.OnPhaseChanged(); };
            var narrator = new DuelNarrator(cards, "CPU", "Passive") { HiddenPlayer = 0 }; // the CPU's hand is hidden, as in the game
            narrator.Line += l => log.Add(l.Trim());
            runner.MessageReceived += narrator.Handle;
            // The CPU's first turn (turn 2 when it goes second).
            bool ok;
            try
            {
                for (int i = 0; i < 5000 && runner.Result.Turns <= 1 + c.FirstPlayer && runner.Step(); i++) { }
                ok = c.Expect(log, duel);
            }
            catch (InvalidOperationException ex) { log.Add(ex.Message); ok = false; }
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} the CPU {c.Name}");
            if (!ok || Environment.GetEnvironmentVariable("SENSE_LOG") == "1") foreach (var line in log.Where(l => l.Length > 0).Take(20)) Console.WriteLine("       " + line);
        }
        Console.WriteLine(bad == 0 ? "sense test: all passed" : $"sense test: {bad} failed");
        return bad == 0 ? 0 : 1;
    }
}
