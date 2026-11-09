using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Protocol;

namespace Ygo.Harness;

/// <summary>
/// `--forced-attack-test`: a forced attack whose only legal target is one the CPU doesn't want (the No Limits Yubel stall of
/// 2026-11-06, docs/dev/TUNING-MEASURE-2026-11-06.md 1b). The CPU goes first and may attack on turn 1; the opponent holds Yubel -
/// Terror Incarnate and Kuriboh, both in face-up Defense Position, with Nightmare Pain face-up: the CPU's monsters must
/// attack, and must attack a "Yubel" monster. The CPU's own target (Kuriboh) isn't offered, Terror Incarnate isn't worth
/// attacking (it can't be destroyed by battle), and the target
/// selection may be cancelled, which only brings the same Battle Phase menu back (no Main Phase 2, no End Phase): the CPU
/// used to call the attack off forever (20,000 prompts in the endless test, and a planner duel in `--peak-check` that never
/// ended). Now it attacks Yubel and its turn ends. The CPU's attacker is a Link Monster (Proxy Dragon), which can't change
/// to Defense Position to get out of the attack (the planner does that with Blue-Eyes, a fair escape). Both the plain bot
/// and the planner (skill 1.0) are checked, and Blue-Eyes with the planner: whatever it does, the turn ends.
/// </summary>
public static class ForcedAttackTest
{
    private const uint Stone = 13039848, TerrorIncarnate = 4779091, Kuriboh = 40640057, NightmarePain = 65261141, BlueEyes = 89631139, ProxyDragon = 22862454;

    /// <summary>DUEL_ATTACK_FIRST_TURN (ocgapi_constants.h): the first player may attack on turn 1.</summary>
    private const ulong AttackFirstTurn = 0x02;

    private sealed record Case(string Name, uint Attacker, bool Planner, bool MustAttack = true);

    public static int Run(ICardDataProvider cards, IScriptProvider scripts)
    {
        var cases = new[]
        {
            new Case("attacks Yubel when Nightmare Pain forces it (the bot)", ProxyDragon, Planner: false),
            new Case("attacks Yubel when Nightmare Pain forces it (the planner)", ProxyDragon, Planner: true),
            new Case("ends its turn under Nightmare Pain with Blue-Eyes (the planner; it may change position instead)", BlueEyes, Planner: true, MustAttack: false),
        };
        int bad = 0;
        foreach (var c in cases)
        {
            var filler = new Deck { Name = "filler" };
            while (filler.Main.Count < 40) filler.Main.Add(Stone);
            var recipe = new DuelRecipe
            {
                Cards = cards, Scripts = scripts, Settings = new DuelSettings { Flags = DuelFlags.ModeMR5 | AttackFirstTurn, Seed = 17, StartingHand = 0, FirstPlayer = 1 },
                Deck0 = filler, Deck1 = filler.Clone(),
            };
            // The opponent (seat 0, a script that only passes) has Terror Incarnate and Kuriboh; the CPU (seat 1) goes first with its attacker.
            recipe.Setup.Add(new PlacedCard(0, TerrorIncarnate, Location.MZone, 0, Position.FaceUpDefense));
            recipe.Setup.Add(new PlacedCard(0, Kuriboh, Location.MZone, 1, Position.FaceUpDefense));
            recipe.Setup.Add(new PlacedCard(0, NightmarePain, Location.SZone, 0, Position.FaceUpAttack));
            recipe.Setup.Add(new PlacedCard(1, c.Attacker, Location.MZone, 2));
            using var duel = recipe.Create();
            var context = new DuelContext(duel, cards);
            var answers = new ResponseLog();
            var log = new List<string>();
            var style = new Personality { Name = "CPU", Skill = 1.0 };
            var cpu = new SmartDuelist("CPU", 3, context, s => log.Add("> " + s), style, null,
                c.Planner ? new TurnPlanner(recipe, answers, style, () => null) : null);
            var runner = new DuelRunner(duel, new ScriptedDuelist(5, cards), cpu, answers);
            runner.MessageReceived += context.Observe;
            var attacked = new List<uint>();
            runner.MessageReceived += m =>
            {
                if (m[0] != Msg.Attack) return;
                var r = new MessageReader(m, 1);
                var attacker = r.LocInfo(0);
                var target = r.LocInfo(0);
                if ((attacker.Controller & 1) == 1 && (target.Location & Location.MZone) != 0)
                    attacked.Add(duel.QueryLocation(0, Location.MZone).ElementAtOrDefault((int)target.Sequence)?.Code ?? 0);
            };
            var narrator = new Ygo.Engine.Presentation.DuelNarrator(cards, "Script", "CPU");
            narrator.Line += l => log.Add(l.Trim());
            runner.MessageReceived += narrator.Handle;
            bool ok;
            int steps = 0;
            try
            {
                // The CPU's turn 1, through its End Phase (turn 2 starting); a loop runs out of steps.
                for (; steps < 3000 && runner.Result.Turns <= 1 && runner.Step(); steps++) { }
                bool turnEnded = runner.Result.Turns > 1 || runner.IsFinished;
                ok = turnEnded && attacked.All(code => code == TerrorIncarnate) && (!c.MustAttack || attacked.Count > 0);
            }
            catch (InvalidOperationException ex) { log.Add(ex.Message); ok = false; }
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} the CPU {c.Name} ({steps} steps, {runner.Result.Prompts} prompts, attacked [{string.Join(", ", attacked.Select(a => cards.Get(a)?.Name ?? a.ToString()))}])");
            if (!ok || Environment.GetEnvironmentVariable("FORCED_ATTACK_LOG") == "1")
                foreach (var line in log.Where(l => l.Length > 0).TakeLast(16)) Console.WriteLine("       " + line);
        }
        Console.WriteLine(bad == 0 ? "forced attack test: all passed" : $"forced attack test: {bad} failed");
        return bad == 0 ? 0 : 1;
    }
}
