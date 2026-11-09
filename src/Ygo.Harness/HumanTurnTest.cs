using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Interactive;
using Ygo.Engine.Presentation;
using Ygo.Engine.Protocol;

namespace Ygo.Harness;

/// <summary>
/// `--human-turn-test`: a player's first turn through the human-play path, the way the game shows it. You (Mekk-Knight
/// Invoked, Aleister the Invoker in hand) Normal Summon Aleister and accept whatever it offers, with the ask-to-chain key
/// held and not held, against the Beginner Circuit's first CPU. Prints every decision the screen would show, and fails
/// if the duel ever waits on you with nothing to show (a hang).
/// </summary>
public static class HumanTurnTest
{
    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root)
    {
        const uint aleister = 86120751;
        int bad = 0;
        foreach (bool holding in new[] { false, true })
        {
            var mine = Deck.LoadYdk(Path.Combine(root, "decks", "mekk-knight-invoked.ydk"));
            var theirs = Deck.LoadYdk(Path.Combine(root, "decks", "classic.ydk"));
            var recipe = new DuelRecipe
            {
                Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = 5 },
                Deck0 = mine, Deck1 = theirs,
                Setup = { new PlacedCard(0, aleister, Location.Hand) },
            };
            using var duel = recipe.Create();
            var context = new DuelContext(duel, cards);
            var human = new HumanDuelist(context, 1);
            human.Presenter.Mode = ResponseMode.Auto;
            human.Presenter.AskForZones = true;
            human.Presenter.HoldToAsk = () => holding;
            var cpu = new SmartDuelist("Ryo", 3, context, null, new Personality { Name = "Ryo", Skill = 0.3 });
            var runner = new DuelRunner(duel, human, cpu);
            runner.MessageReceived += context.Observe;
            var narrator = new DuelNarrator(cards, "You", "Ryo");
            narrator.Line += l => { if (l.Trim().Length > 0) Console.WriteLine("      " + l.Trim()); };
            runner.MessageReceived += narrator.Handle;
            Console.WriteLine($"== ask key {(holding ? "held" : "not held")}");
            bool summoned = false;
            int shown = 0, idle = 0;
            for (int i = 0; i < 20000 && runner.Result.Turns <= 1; i++)
            {
                if (!runner.Step()) break;
                if (!runner.IsWaiting) continue;
                if (runner.WaitingFor != 0) continue;
                var set = human.Current;
                if (set == null)
                {
                    // The runner waits on you, but there's nothing to show: the screen would sit on "Resolving...".
                    if (++idle > 50) { Console.WriteLine("   FAIL the duel waits on you with no decision to show"); bad++; break; }
                    continue;
                }
                idle = 0;
                shown++;
                Console.WriteLine($"   [{set.Prompt.GetType().Name}] {set.Title} | {string.Join(" / ", set.Choices.Select(c => c.Label).Take(6))}{(set.CanCancel ? " | (cancel)" : "")}");
                byte[] answer;
                if (set.Prompt is IdlePrompt)
                {
                    int pick = set.Choices.FindIndex(c => !summoned && c.Card?.Code == aleister && c.Label.Contains("Normal Summon"));
                    if (pick >= 0) summoned = true;
                    else pick = set.Choices.FindIndex(c => c.Label.Contains("End"));
                    answer = set.Choose(pick >= 0 ? pick : 0);
                }
                else if (set.IsChain) answer = set.CanCancel ? set.Cancel() : set.Choose(0);
                else answer = set.Choices.Count > 0 ? set.Choose(Enumerable.Range(0, Math.Max(1, set.Min)).ToArray()) : set.Finish();
                human.Submit(answer);
                if (shown > 40) break;
            }
            Console.WriteLine($"   turn ended: {runner.Result.Turns > 1}, decisions shown {shown}");
        }
        Console.WriteLine(bad == 0 ? "human turn test: all passed" : $"human turn test: {bad} failed");
        return bad == 0 ? 0 : 1;
    }
}
