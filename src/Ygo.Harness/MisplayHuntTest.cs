using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Presentation;
using Ygo.Engine.Protocol;

namespace Ygo.Harness;

/// <summary>
/// `--misplay-hunt-test`: the misplays the 2026-11-06 hunt fixed (docs/dev/MISPLAY-HUNT-2026-11-06.md), each on a small set-up
/// board. Every case runs the fixed bot and the bot with that fix's toggle off: the fixed one must pass and the old one
/// must show the misplay. The plain bot (no planner) is the one checked: it's what every CPU below skill 0.9 plays with,
/// and what the planner's rollouts play.
/// </summary>
public static class MisplayHuntTest
{
    private const uint VorseRaider = 14898066, VorseRaiderAltArt = 14898067;

    /// <summary>A playbook holding Vorse Raider's Normal Summon, written with its base code.</summary>
    private sealed class HoldVorse : Ygo.Engine.AI.Playbooks.Playbook
    {
        public override string Deck => "test";
        public HoldVorse() { HoldNormalSummon(VorseRaider); }
    }

    private const uint Stone = 13039848, SetRotation = 73468603, Map = 28126717, MysticMine = 76375976, Robina = 18940725,
        Stamping = 81385346, TradeIn = 38120068, Kaibaman = 34627841, LittleKnight = 29301450, Impermanence = 10045474,
        WarriorReturningAlive = 95281259, Gearfried = 423705, Sakuretsu = 56120475, GoblinAttackForce = 78658564, AxeOfDespair = 40619825,
        LightningBlade = 55226821, BlueEyes = 89631133, Zephyros = 14785765, Albaz = 68468459, WhiteDragon = 73819701,
        Excavation = 98494543, QuickCharger = 49479374, Salvage = 96947648, BatterymanC = 19733961;

    /// <summary>A playbook holding Fallen of the White Dragon's Normal Summon ("always treated as" Fallen of Albaz).</summary>
    private sealed class HoldWhiteDragon : Ygo.Engine.AI.Playbooks.Playbook
    {
        public override string Deck => "test";
        public HoldWhiteDragon() { HoldNormalSummon(WhiteDragon); }
    }

    /// <summary>A playbook with one Main Phase rule (Normal Summon Gearfried): it goes before any card's heuristic score.</summary>
    private sealed class SummonGearfried : Ygo.Engine.AI.Playbooks.Playbook
    {
        public override string Deck => "test";
        public SummonGearfried() { NormalSummon("Gearfried first", Gearfried); }
    }

    /// <summary>
    /// The CPU (seat 1) goes first (and may attack on turn 1). <c>Board</c>: more cards placed before the duel (either side);
    /// <c>OpponentActivates</c>: what the scripted opponent activates whenever it may (S:P Little Knight's answer).
    /// </summary>
    private sealed record Case(string Name, uint[] CpuHand, uint[] CpuDeckTop, Action<SmartDuelist, bool> Toggle,
        Func<Outcome, bool> Fixed, Func<Outcome, bool> AsBefore, Func<bool, Ygo.Engine.AI.Playbooks.Playbook?>? Book = null,
        PlacedCard[]? Board = null, uint[]? OpponentActivates = null, uint StartingLp = 8000);

    /// <summary>DUEL_ATTACK_FIRST_TURN (ocgapi_constants.h).</summary>
    private const ulong AttackFirstTurn = 0x02;

    private sealed class Outcome
    {
        public readonly List<uint> Activated = new();
        public readonly List<uint> Summoned = new();
        /// <summary>The CPU's plays in order: "act:code", "ns:code", "attack".</summary>
        public readonly List<string> Plays = new();
        public int OpponentLp;
        public int First(string play) => Plays.IndexOf(play) is var i and >= 0 ? i : int.MaxValue;
        public readonly List<string> Log = new();
        public bool TurnEnded;
    }

    public static int Run(ICardDataProvider cards, IScriptProvider scripts)
    {
        var cases = new[]
        {
            // Juniper Pell #243 (owner replay 2026-11-05 1634): the bot held Set Rotation (a Quick-Play) for the opponent's
            // turn, so it never set up its Field Spells in its own Main Phase; the planner rated it 5,297 points better.
            new Case("activates Set Rotation in its Main Phase (its Field Spells from the Deck), not holding it",
                new[] { SetRotation, Robina }, new[] { Map, MysticMine }, (bot, on) => bot.UseDeckSetSense = on,
                o => o.Activated.FirstOrDefault() == SetRotation, o => !o.Activated.Contains(SetRotation)),
            // Card versions 3.1 (F8): a playbook names a card by its base code; a deck holding another version of it (an
            // original text, an alt art) used to slip past every rule about it. Vorse Raider's alt-art id in hand, a
            // playbook that holds Vorse Raider's Normal Summon.
            new Case("follows its playbook for another version of a card (alt-art Vorse Raider: the playbook holds its Normal Summon)",
                new[] { VorseRaiderAltArt }, Array.Empty<uint>(), (bot, on) => { },
                o => !o.Summoned.Contains(VorseRaiderAltArt), o => o.Summoned.Contains(VorseRaiderAltArt),
                Book: on => new HoldVorse { UseBaseCodes = on }),
            // Vera #99 (owner replay 2026-11-02 2354): with The Warrior Returning Alive in hand and Goblin Attack Force in the
            // GY, the bot Normal Summoned Gearfried (1800); the planner rates fetching Goblin Attack Force first +2,880.
            new Case("returns Goblin Attack Force with The Warrior Returning Alive before its Normal Summon (Vera #99)",
                new[] { WarriorReturningAlive, Gearfried, Sakuretsu }, Array.Empty<uint>(), (bot, on) => bot.UseRecoverySense = on,
                o => o.First($"act:{WarriorReturningAlive}") < o.First($"ns:{Gearfried}") && o.Summoned.Contains(GoblinAttackForce),
                o => o.Summoned.Contains(Gearfried) && !o.Summoned.Contains(GoblinAttackForce),
                Board: new[]
                {
                    new PlacedCard(1, GoblinAttackForce, Location.Grave), new PlacedCard(1, AxeOfDespair, Location.Grave),
                    new PlacedCard(1, LightningBlade, Location.Grave),
                }),
            // Old Gus #114 (owner replay 2026-11-02 2058): Blue-Eyes on the field and the opponent's S:P Little Knight
            // face-up: an optional Spell first let Little Knight banish Blue-Eyes in answer (no attack); the planner rates
            // attacking first +3,800. Here the optional Spell is Trade-In (it draws: the attack doesn't need it). The
            // scripted opponent answers any activation with Little Knight.
            new Case("attacks with Blue-Eyes before Trade-In while S:P Little Knight could answer it (Old Gus #114)",
                new[] { TradeIn, BlueEyes, Kaibaman }, Array.Empty<uint>(), (bot, on) => bot.UseActivationWindowSense = on,
                o => o.First("attack") < o.First($"act:{TradeIn}") && o.OpponentLp < 8000,
                o => o.First($"act:{TradeIn}") < o.First("attack"),
                Board: new[] { new PlacedCard(1, BlueEyes, Location.MZone, 2), new PlacedCard(0, LittleKnight, Location.MZone, 5) },
                OpponentActivates: new[] { LittleKnight }),
            // Review: Spell/Trap removal is what the attack needs while the opponent has a set card (Old Gus's own Stamping
            // Destruction into a set Infinite Impermanence): it still goes first, Little Knight or not.
            new Case("still plays Stamping Destruction first against a set card, Little Knight or not (removal the attack needs)",
                new[] { Stamping, TradeIn, Kaibaman }, Array.Empty<uint>(), (bot, on) => bot.UseActivationWindowSense = on,
                o => o.First($"act:{Stamping}") < o.First("attack"), o => o.First($"act:{Stamping}") < o.First("attack"),
                Board: new[]
                {
                    new PlacedCard(1, BlueEyes, Location.MZone, 2),
                    new PlacedCard(0, LittleKnight, Location.MZone, 5),
                    new PlacedCard(0, Impermanence, Location.SZone, 3, Position.FaceDownDefense),
                },
                OpponentActivates: new[] { LittleKnight }),
            // Where the rule shouldn't apply: no Little Knight, so Trade-In goes first as before.
            new Case("still plays Trade-In before attacking when nothing could answer it",
                new[] { TradeIn, BlueEyes, Kaibaman }, Array.Empty<uint>(), (bot, on) => bot.UseActivationWindowSense = on,
                o => o.First($"act:{TradeIn}") < o.First("attack"), o => o.First($"act:{TradeIn}") < o.First("attack"),
                Board: new[] { new PlacedCard(1, BlueEyes, Location.MZone, 2), new PlacedCard(0, Stone, Location.MZone, 1, Position.FaceUpDefense) }),
            // Review: Set Rotation Sets a Field Spell in our Field Zone, which sends a face-up one of ours to the GY by rule.
            new Case("doesn't play Set Rotation over its own face-up Field Spell (the Magnificent Map)",
                new[] { SetRotation, Robina }, new[] { Map, MysticMine }, (bot, on) => bot.UseDeckSetSense = on,
                o => !o.Activated.Contains(SetRotation), o => !o.Activated.Contains(SetRotation),
                Board: new[] { new PlacedCard(1, Map, Location.SZone, 5) }),
            // Review: the card database aliases a different card "always treated as" another (Fallen of the White Dragon is
            // always treated as Fallen of Albaz): base-code matching must not take Albaz for the White Dragon.
            new Case("doesn't take Fallen of Albaz for Fallen of the White Dragon (a playbook holding the White Dragon's summon)",
                new[] { Albaz }, Array.Empty<uint>(), (bot, on) => { },
                o => o.Summoned.Contains(Albaz), o => o.Summoned.Contains(Albaz),
                Book: on => new HoldWhiteDragon { UseBaseCodes = on }),
            // Review: a Recover card mustn't make the bot skip its playbook (the playbook's rule goes first, as before).
            new Case("follows its playbook's Main Phase rule before The Warrior Returning Alive",
                new[] { WarriorReturningAlive, Gearfried }, Array.Empty<uint>(), (bot, on) => bot.UseRecoverySense = on,
                o => o.First($"ns:{Gearfried}") < o.First($"act:{WarriorReturningAlive}"), o => o.First($"ns:{Gearfried}") < o.First($"act:{WarriorReturningAlive}"),
                Book: on => new SummonGearfried(),
                Board: new[] { new PlacedCard(1, GoblinAttackForce, Location.Grave) }),
            // Fern Vasquez (post-game audit, game 34): at low LP the bot revived Blackwing - Zephyros the Elite ("Special
            // Summon this card, and if you do, take 400 damage") and lost to its own card: the cost check read only "you take".
            new Case("doesn't revive Zephyros the Elite at 300 LP (its 400 damage would end the duel)",
                Array.Empty<uint>(), Array.Empty<uint>(), (bot, on) => bot.UseSelfDamageSense = on,
                o => !o.Activated.Contains(Zephyros), o => o.Activated.Contains(Zephyros),
                Board: new[] { new PlacedCard(1, Zephyros, Location.Grave), new PlacedCard(1, Stone, Location.MZone, 1, Position.FaceUpDefense) },
                StartingLp: 300),
            // Where the rule shouldn't apply: no Little Knight, so Stamping Destruction goes first as before.
            new Case("still plays Stamping Destruction before attacking when nothing could answer it",
                new[] { Stamping, TradeIn, Kaibaman }, Array.Empty<uint>(), (bot, on) => bot.UseActivationWindowSense = on,
                o => o.First($"act:{Stamping}") < o.First("attack"), o => o.First($"act:{Stamping}") < o.First("attack"),
                Board: new[]
                {
                    new PlacedCard(1, BlueEyes, Location.MZone, 2),
                    new PlacedCard(0, Stone, Location.MZone, 1, Position.FaceUpDefense),
                    new PlacedCard(0, Impermanence, Location.SZone, 3, Position.FaceDownDefense),
                }),
        };
        int bad = 0;
        void Check(string what, bool ok)
        {
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
        }
        // Review: Recover only for recovery that costs no cards from the hand.
        Check("The Warrior Returning Alive and Salvage are Recover; Magical Stone Excavation (discard 2) isn't",
            CardTraits.Of(cards.Get(WarriorReturningAlive)).Has(Trait.Recover) && CardTraits.Of(cards.Get(Salvage)).Has(Trait.Recover)
            && !CardTraits.Of(cards.Get(Excavation)).Has(Trait.Recover));
        // Review: a Quick-Play or Trap with Recover keeps its timing (no Main Phase 90-plus score): Quick Charger stays in hand.
        {
            var held = Play(cards, scripts, new Case("", new[] { QuickCharger }, Array.Empty<uint>(), (bot, on) => bot.UseRecoverySense = on,
                _ => true, _ => true, Board: new[] { new PlacedCard(1, BatterymanC, Location.Grave), new PlacedCard(1, BatterymanC, Location.Grave) }), true);
            Check($"a Quick-Play that recovers (Quick Charger) keeps its timing: activated [{string.Join(", ", held.Activated)}]", !held.Activated.Contains(QuickCharger));
        }
        // Review: the playbook view doesn't count Fallen of Albaz as Fallen of the White Dragon, but does count an alt art.
        {
            var recipe = new DuelRecipe
            {
                Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = 17, StartingHand = 0, FirstPlayer = 1 },
                Deck0 = new Deck { Main = Enumerable.Repeat(Stone, 40).ToList() }, Deck1 = new Deck { Main = Enumerable.Repeat(Stone, 40).ToList() },
            };
            recipe.Setup.Add(new PlacedCard(1, Albaz, Location.Hand));
            recipe.Setup.Add(new PlacedCard(1, VorseRaiderAltArt, Location.Hand));
            using var duel = recipe.Create();
            var view = new Ygo.Engine.AI.Playbooks.PlaybookView(new DuelContext(duel, cards), 1);
            Check($"the playbook view: InHand(White Dragon) {view.InHand(WhiteDragon)} with Albaz in hand, InHand(Albaz) {view.InHand(Albaz)}, InHand(Vorse Raider) {view.InHand(VorseRaider)} with its alt art",
                view.InHand(WhiteDragon) == 0 && view.InHand(Albaz) == 1 && view.InHand(VorseRaider) == 1);
        }
        foreach (var c in cases)
        {
            var now = Play(cards, scripts, c, true);
            var before = Play(cards, scripts, c, false);
            bool ok = now.TurnEnded && c.Fixed(now) && c.AsBefore(before);
            if (!ok) bad++;
            string Acts(Outcome o) => string.Join(", ", o.Activated.Select(a => cards.Get(a)?.Name ?? a.ToString()));
            string Plays(Outcome o) => string.Join(", ", o.Plays.Select(x => x.Contains(':') ? $"{x[..x.IndexOf(':')]} {cards.Get(uint.Parse(x[(x.IndexOf(':') + 1)..]))?.Name}" : x));
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} the CPU {c.Name}: now [{Plays(now)}]; as before [{Plays(before)}]");
            if (!ok || Environment.GetEnvironmentVariable("MISPLAY_HUNT_LOG") == "1")
                foreach (var line in now.Log.Where(l => l.Length > 0).TakeLast(20)) Console.WriteLine("       " + line);
        }
        Console.WriteLine(bad == 0 ? "misplay hunt test: all passed" : $"misplay hunt test: {bad} failed");
        return bad == 0 ? 0 : 1;
    }

    private static Outcome Play(ICardDataProvider cards, IScriptProvider scripts, Case c, bool fixes)
    {
        var filler = new Deck { Name = "filler" };
        while (filler.Main.Count < 40) filler.Main.Add(Stone);
        var mine = filler.Clone();
        for (int i = 0; i < c.CpuDeckTop.Length; i++) mine.Main[i] = c.CpuDeckTop[i];
        var recipe = new DuelRecipe
        {
            Cards = cards, Scripts = scripts,
            Settings = new DuelSettings { Flags = DuelFlags.ModeMR5 | AttackFirstTurn, Seed = 17, StartingHand = 0, FirstPlayer = 1, StartingLP = c.StartingLp },
            Deck0 = filler.Clone(), Deck1 = mine,
        };
        foreach (var code in c.CpuHand) recipe.Setup.Add(new PlacedCard(1, code, Location.Hand));
        foreach (var card in c.Board ?? Array.Empty<PlacedCard>()) recipe.Setup.Add(card);
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var o = new Outcome();
        if (Environment.GetEnvironmentVariable("MISPLAY_HUNT_LOG") == "2")
            o.Log.Add("deck: " + string.Join(",", duel.QueryLocation(1, Location.Deck, 0x1).Where(x => x != null && x.Code != Stone).Select(x => x!.Code)));
        var cpu = new SmartDuelist("CPU", 3, context, s => o.Log.Add("> " + s), new Personality { Name = "CPU", Skill = 1.0 }, c.Book?.Invoke(fixes));
        c.Toggle(cpu, fixes);
        cpu.Notes = n => o.Log.Add($"  (option) {n.Label} {n.Score:0.#}{(n.Rejected != null ? " rejected: " + n.Rejected : "")}");
        var trace = new PromptTrace(cpu, cards, "CPU");
        var script = new ScriptedDuelist(5, cards);
        foreach (var code in c.OpponentActivates ?? Array.Empty<uint>()) script.Activate.Add(code);
        var runner = new DuelRunner(duel, script, Environment.GetEnvironmentVariable("MISPLAY_HUNT_LOG") == "2" ? trace : cpu);
        runner.MessageReceived += context.Observe;
        runner.MessageReceived += m =>
        {
            if (m[0] == Msg.Summoning && runner.Result.Turns <= 1)
            {
                var summoned = BitConverter.ToUInt32(m, 1);
                if (new MessageReader(m, 5).LocInfo(summoned).Controller == 1) { o.Summoned.Add(summoned); o.Plays.Add($"ns:{summoned}"); }
            }
            if (m[0] == Msg.Attack && runner.Result.Turns <= 1 && (new MessageReader(m, 1).LocInfo(0).Controller & 1) == 1) o.Plays.Add("attack");
            if (m[0] != Msg.Chaining || runner.Result.Turns > 1) return;
            var code = BitConverter.ToUInt32(m, 1);
            if (new MessageReader(m, 5).LocInfo(code).Controller == 1) { o.Activated.Add(code); o.Plays.Add($"act:{code}"); }
        };
        var narrator = new DuelNarrator(cards, "Script", "CPU");
        narrator.Line += l => o.Log.Add(l.Trim());
        runner.MessageReceived += narrator.Handle;
        try
        {
            for (int steps = 0; steps < 3000 && runner.Result.Turns <= 1 && runner.Step(); steps++) { }
            o.TurnEnded = runner.Result.Turns > 1 || runner.IsFinished;
        }
        catch (InvalidOperationException ex) { o.Log.Add(ex.Message); }
        o.OpponentLp = context.Board.Players[0].LifePoints;
        if (Environment.GetEnvironmentVariable("MISPLAY_HUNT_LOG") == "2") o.Log.AddRange(trace.Last);
        return o;
    }
}
