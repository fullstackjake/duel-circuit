using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Presentation;

namespace Ygo.Harness;

/// <summary>
/// `--yata-test`: a set-up duel where Yata-Garasu attacks a player with an empty hand and field (they're Yata-locked and
/// lose), and one where that player still holds a card (the duel goes on).
/// </summary>
public static class YataTest
{
    public static int Run(ICardDataProvider cards, IScriptProvider scripts)
    {
        const uint fissure = 66788016, kuriboh = 40640057;
        int bad = 0;
        foreach (var (name, defenderHand, expectLock) in new[] { ("empty hand and field", Array.Empty<uint>(), true), ("a card in hand", new[] { kuriboh }, false) })
        {
            // Seat 0 goes first with nothing (no draw on turn 1); seat 1 summons Yata-Garasu on turn 2 and attacks.
            var filler = new Deck { Name = "filler" };
            for (int i = 0; i < 20; i++) filler.Main.Add(fissure); // a spell with nothing to do here: Yata is the only monster
            var recipe = new DuelRecipe
            {
                Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = 3, StartingHand = 0 },
                Deck0 = filler.Clone(), Deck1 = filler.Clone(),
                Setup = { new PlacedCard(1, YataLock.YataGarasu, Location.Hand) },
            };
            foreach (var code in defenderHand) recipe.Setup.Add(new PlacedCard(0, code, Location.Hand));
            using var duel = recipe.Create();
            var context = new DuelContext(duel, cards);
            var defender = new ScriptedDuelist(1, cards);
            var attacker = new SmartDuelist("Attacker", 2, context, null, new Personality { Name = "Attacker", Aggression = 1 });
            var runner = new DuelRunner(duel, defender, attacker);
            runner.MessageReceived += context.Observe;
            YataLock.Attach(runner, duel);
            var log = new List<string>();
            var narrator = new DuelNarrator(cards, "Defender", "Attacker");
            narrator.Line += log.Add;
            runner.MessageReceived += narrator.Handle;
            for (int i = 0; i < 3000 && runner.Step() && runner.Result.Turns <= 4; i++) { }
            bool locked = runner.Result.Winner == 1 && runner.Result.Reason == DuelRunner.ReasonYataLock;
            bool ok = locked == expectLock;
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} Yata-Garasu attacks a player with {name}: {(locked ? $"Yata-locked on turn {runner.Result.Turns}" : $"no lock (turn {runner.Result.Turns}, winner {runner.Result.Winner})")}");
            foreach (var line in log.Where(l => l.Contains("Yata") || l.Contains("***") || l.Contains("attacks")).Take(6)) Console.WriteLine("       " + line.Trim());
        }
        Console.WriteLine(bad == 0 ? "yata test: all passed" : $"yata test: {bad} failed");
        return bad == 0 ? 0 : 1;
    }
}
