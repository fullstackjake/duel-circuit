using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Presentation;
using Ygo.Engine.Protocol;

namespace Ygo.Harness;

/// <summary>
/// `--removal-timing-test`: when the CPU spends Spell/Trap removal on a card being activated. A scripted opponent goes
/// first and activates one card; the CPU (skill 1.0, fires at anything: patience 0) holds set removal.
/// A Normal Spell (Pot of Greed), a Quick-Play Spell (Poison of the Old Man) or a Normal Trap (Reckless Greed) on the
/// chain resolves even if it's destroyed there, and leaves after: removal spent on it is a card for nothing (the CPUs
/// did it, mostly in the Beginner circuits). Kept: removal in answer to a Continuous Spell (Messenger of Peace) or a
/// Normal Spell that stays on the field (Swords of Revealing Light), aimed at a set card instead of the resolving one,
/// a set removal card that is itself the target of their removal (Dust Tornado answers the MST on it), a negation
/// (Magic Jammer negates the Normal Spell, then destroys it), and plays an independent review showed the first version
/// of the fix lost: a Normal Trap that summons itself (Shade Brigandine) or equips itself (Altergeist Manifestation) on
/// the chain, a resolved Manifestation on the CPU's turn, Artifact Ignition and Sanctum chained to Heavy Storm, Filia
/// Regis banishing the target of Monster Reborn, and Swords of Revealing Light beside a set card.
/// </summary>
public static class RemovalTimingTest
{
    private const uint Stone = 13039848, Mst = 5318639, DustTornado = 60082869, PotOfGreed = 55144522, Poison = 8842266,
        RecklessGreed = 37576645, Messenger = 44656491, Swords = 72302403, MirrorForce = 44095762, TwinTwisters = 43898403,
        MagicJammer = 77414722, Filia = 70538272, Reborn = 83764718, BlueEyes = 89631139, Shade = 98827725, Manifestation = 35146019,
        Silquitous = 89538537, Marionetter = 53143898, Ignition = 29223325, Sanctum = 12444060, Moralltach = 85103922, HeavyStorm = 19613556;

    /// <summary>What happened in the opponent's turn: the CPU's activations (and whether they answered a chain), and destructions.</summary>
    private sealed class Seen
    {
        public readonly List<(uint code, bool inChain)> CpuActivations = new();
        public readonly List<uint> Destroyed = new();
        public readonly List<string> Log = new();
        public bool CpuActivated(uint code, bool? inChain = null) => CpuActivations.Any(a => a.code == code && (inChain == null || a.inChain == inChain));
    }

    private sealed record Case(string Name, PlacedCard[] Setup, uint[] Activate, Func<Seen, Duel, bool> Expect, int Turns = 1);

    public static int Run(ICardDataProvider cards, IScriptProvider scripts)
    {
        PlacedCard Set(byte team, uint code, uint zone) => new(team, code, Location.SZone, zone, Position.FaceDownDefense);
        // The CPU (seat 1) holds two set removal cards in every case.
        PlacedCard[] Cpu() => new[] { Set(1, Mst, 1), Set(1, DustTornado, 2) };
        int Monsters(Duel duel, byte player) => duel.QueryLocation(player, Location.MZone).Count(c => c != null);
        bool Untouched(Duel duel) => duel.QueryLocation(1, Location.SZone).Count(c => c?.Code is Mst or DustTornado) == 2;
        var cases = new[]
        {
            new Case("doesn't destroy a Normal Spell on the chain (Pot of Greed resolves anyway)",
                Cpu().Append(new PlacedCard(0, PotOfGreed, Location.Hand)).ToArray(), new[] { PotOfGreed },
                (seen, duel) => !seen.CpuActivations.Any() && Untouched(duel)),
            new Case("doesn't destroy a Quick-Play Spell on the chain (Poison of the Old Man)",
                Cpu().Append(new PlacedCard(0, Poison, Location.Hand)).ToArray(), new[] { Poison },
                (seen, duel) => !seen.CpuActivations.Any() && Untouched(duel)),
            new Case("doesn't destroy a Normal Trap on the chain (Reckless Greed)",
                Cpu().Append(Set(0, RecklessGreed, 2)).ToArray(), new[] { RecklessGreed },
                (seen, duel) => !seen.CpuActivations.Any() && Untouched(duel)),
            new Case("doesn't discard for Twin Twisters to hit only a Normal Spell on the chain (Pot of Greed)",
                new[] { Set(1, TwinTwisters, 1), new PlacedCard(1, Stone, Location.Hand), new PlacedCard(0, PotOfGreed, Location.Hand) }, new[] { PotOfGreed },
                (seen, duel) => !seen.CpuActivations.Any() && duel.QueryLocation(1, Location.Hand).Count(c => c != null) == 1),
            new Case("still negates a Normal Spell with Magic Jammer (a negation stops it)",
                new[] { Set(1, MagicJammer, 1), new PlacedCard(1, Stone, Location.Hand), new PlacedCard(0, PotOfGreed, Location.Hand) }, new[] { PotOfGreed },
                (seen, _) => seen.CpuActivated(MagicJammer, inChain: true)),
            new Case("destroys a Continuous Spell in answer to its activation (Messenger of Peace)",
                Cpu().Append(new PlacedCard(0, Messenger, Location.Hand)).ToArray(), new[] { Messenger },
                (seen, _) => seen.CpuActivations.Any(a => a.inChain) && seen.Destroyed.Contains(Messenger)),
            new Case("destroys a Normal Spell that stays on the field (Swords of Revealing Light)",
                Cpu().Append(new PlacedCard(0, Swords, Location.Hand)).ToArray(), new[] { Swords },
                (seen, _) => seen.Destroyed.Contains(Swords)),
            new Case("aims its removal at a set card, not the Normal Spell on the chain (Mirror Force beside Pot of Greed)",
                Cpu().Append(new PlacedCard(0, PotOfGreed, Location.Hand)).Append(Set(0, MirrorForce, 3)).ToArray(), new[] { PotOfGreed },
                (seen, _) => seen.Destroyed.Contains(MirrorForce) && !seen.Destroyed.Contains(PotOfGreed)),
            // Kept plays the first version of the fix lost (an independent review found them; the old CPU got them right).
            new Case("still destroys The Phantom Knights of Shade Brigandine on the chain (it summons itself: off the field it can't)",
                Cpu().Append(Set(0, Shade, 2)).ToArray(), new[] { Shade },
                (seen, duel) => seen.Destroyed.Contains(Shade) && Monsters(duel, 0) == 0),
            new Case("still destroys Altergeist Manifestation on the chain (it equips itself), and Silquitous goes with it",
                Cpu().Append(Set(0, Manifestation, 2)).Append(new PlacedCard(0, Silquitous, Location.Grave)).ToArray(), new[] { Manifestation },
                (seen, duel) => seen.Destroyed.Contains(Manifestation) && Monsters(duel, 0) == 0, Turns: 2),
            new Case("MSTs a resolved Altergeist Manifestation on its own turn (one for two: Marionetter goes too) and attacks directly",
                new[] { new PlacedCard(1, Mst, Location.Hand), Set(0, Manifestation, 2), new PlacedCard(0, Marionetter, Location.Grave) }, new[] { Manifestation },
                (seen, duel) => seen.Destroyed.Contains(Manifestation) && Monsters(duel, 0) == 0 && seen.Log.Any(l => l.Contains("Script takes 1300")), Turns: 2),
            new Case("chains Artifact Ignition to Heavy Storm (it Sets Moralltach, which the Storm destroys into a summon)",
                new[] { Set(1, Ignition, 1), new PlacedCard(0, HeavyStorm, Location.Hand), new PlacedCard(1, Moralltach, Location.Deck), new PlacedCard(1, Moralltach, Location.Deck) },
                new[] { HeavyStorm }, (seen, duel) => duel.QueryLocation(1, Location.MZone).Any(c => c?.Code == Moralltach)),
            new Case("chains Artifact Sanctum to Heavy Storm (it summons Moralltach)",
                new[] { Set(1, Sanctum, 1), new PlacedCard(0, HeavyStorm, Location.Hand), new PlacedCard(1, Moralltach, Location.Deck), new PlacedCard(1, Moralltach, Location.Deck) },
                new[] { HeavyStorm }, (seen, _) => seen.CpuActivated(Sanctum, inChain: true)),
            new Case("answers Monster Reborn with Filia Regis (it banishes the Blue-Eyes in the GY: Reborn has nothing left)",
                new[] { new PlacedCard(1, Filia, Location.MZone, 2), new PlacedCard(0, BlueEyes, Location.Grave), new PlacedCard(0, Reborn, Location.Hand) }, new[] { Reborn },
                (seen, duel) => seen.CpuActivated(Filia, inChain: true) && Monsters(duel, 0) == 0),
            new Case("destroys Swords of Revealing Light, not just the set card beside it (Swords stays and locks its attacks)",
                Cpu().Append(new PlacedCard(0, Swords, Location.Hand)).Append(Set(0, MirrorForce, 3)).ToArray(), new[] { Swords },
                (seen, _) => seen.Destroyed.Contains(Swords)),
            new Case("answers the MST aimed at its set Dust Tornado with the Dust Tornado (it's going anyway)",
                new[] { Set(1, DustTornado, 2), new PlacedCard(0, Mst, Location.Hand) }, new[] { Mst },
                (seen, _) => seen.CpuActivated(DustTornado, inChain: true)),
        };
        int bad = 0;
        foreach (var c in cases)
        {
            var filler = new Deck { Name = "filler" };
            while (filler.Main.Count < 40) filler.Main.Add(Stone);
            var recipe = new DuelRecipe
            {
                Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = 11, StartingHand = 0, FirstPlayer = 0 },
                Deck0 = filler, Deck1 = filler.Clone(),
            };
            recipe.Setup.AddRange(c.Setup);
            using var duel = recipe.Create();
            var context = new DuelContext(duel, cards);
            var seen = new Seen();
            var script = new ScriptedDuelist(5, cards);
            foreach (var code in c.Activate) script.Activate.Add(code);
            var cpu = new SmartDuelist("CPU", 3, context, s => seen.Log.Add("> " + s), new Personality { Name = "CPU", Skill = 1.0, Patience = 0.0 });
            var runner = new DuelRunner(duel, script, cpu);
            runner.MessageReceived += context.Observe;
            runner.MessageReceived += m => { if (m[0] == Msg.NewTurn || m[0] == Msg.NewPhase) cpu.OnPhaseChanged(); };
            int depth = 0;
            runner.MessageReceived += m =>
            {
                var r = new MessageReader(m, 1);
                switch (m[0])
                {
                    case Msg.Chaining:
                    {
                        var card = r.LocInfo(r.U32());
                        if ((card.Controller & 1) == 1) seen.CpuActivations.Add((card.Code, depth > 0));
                        depth++;
                        break;
                    }
                    case Msg.ChainEnd:
                        depth = 0;
                        break;
                    case Msg.Move:
                    {
                        var code = r.U32();
                        var from = r.LocInfo(code);
                        r.LocInfo(code);
                        uint reason = r.AtEnd ? 0 : r.U32();
                        if ((from.Location & 0x7f) == Location.SZone && (reason & 0x1) != 0) seen.Destroyed.Add(code);
                        break;
                    }
                }
            };
            var narrator = new DuelNarrator(cards, "Script", "CPU");
            narrator.Line += l => seen.Log.Add(l.Trim());
            runner.MessageReceived += narrator.Handle;
            bool ok;
            try
            {
                // The opponent's turn (turn 1), through its End Phase (and the CPU's turn 2 for some).
                for (int i = 0; i < 5000 && runner.Result.Turns <= c.Turns && runner.Step(); i++) { }
                ok = c.Expect(seen, duel);
            }
            catch (InvalidOperationException ex) { seen.Log.Add(ex.Message); ok = false; }
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} the CPU {c.Name}");
            if (!ok || Environment.GetEnvironmentVariable("REMOVAL_LOG") == "1")
                foreach (var line in seen.Log.Where(l => l.Length > 0).Take(24)) Console.WriteLine("       " + line);
        }
        Console.WriteLine(bad == 0 ? "removal timing test: all passed" : $"removal timing test: {bad} failed");
        return bad == 0 ? 0 : 1;
    }

    /// <summary>
    /// `--removal-ab deck,deck,... [--games N] [--seed N]`: for each deck, N duels piloted by its campaign character
    /// against characters drawn from the whole roster (their first decks), each duel played twice on the same seed: the
    /// pilot with the removal-timing sense on (the new CPU) and off (the CPU as before). The opponent is the same new CPU
    /// both times. Prints both win rates and how many paired duels changed winner.
    /// </summary>
    public static int RunAb(ICardDataProvider cards, IScriptProvider scripts, string root, string deckList, int games, int seed)
    {
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        var everyone = Directory.GetFiles(Path.Combine(root, "personalities"), "*.profile").Select(Personality.Load).OrderBy(p => p.Id).ToList();
        var roster = everyone.Where(p => p.Decks.Count > 0 && deckFiles.ContainsKey(p.Decks[0])).ToList();
        Deck LoadDeck(string name) { var d = Deck.LoadYdk(deckFiles[name]); d.Name = name; return d; }
        foreach (var name in deckList.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0))
        {
            var pilot = everyone.FirstOrDefault(p => p.Decks.Contains(name)) ?? new Personality { Id = name, Name = name };
            var wins = new int[2];
            int changed = 0, played = 0;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            // REMOVAL_AB_GAME=N: just that duel, printing where the two versions first part ways (both narrations).
            int only = int.TryParse(Environment.GetEnvironmentVariable("REMOVAL_AB_GAME"), out var o) ? o : -1;
            Parallel.For(only >= 0 ? only : 0, only >= 0 ? only + 1 : games, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, g =>
            {
                var logs = new[] { new List<string>(), new List<string>() };
                var rng = new Random(seed * 1000 + g);
                var them = roster[rng.Next(roster.Count)];
                int pilotSeat = g % 2; // half the duels going first
                var winner = new int[2];
                for (int variant = 0; variant < 2; variant++) // 0: new, 1: as before
                {
                    var styles = new Personality[2];
                    styles[pilotSeat] = pilot; styles[1 - pilotSeat] = them;
                    var decks = new Deck[2];
                    decks[pilotSeat] = LoadDeck(name); decks[1 - pilotSeat] = LoadDeck(them.Decks[0]);
                    var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)(seed * 1000 + g) }, Deck0 = decks[0], Deck1 = decks[1] };
                    using var duel = recipe.Create();
                    var context = new DuelContext(duel, cards);
                    var answers = new ResponseLog();
                    IDuelist Bot(int seat)
                    {
                        var style = styles[seat];
                        bool timing = seat != pilotSeat || variant == 0;
                        var playbook = style.KnowsDeck ? Ygo.Engine.AI.Playbooks.Playbooks.ForDeck(decks[seat]) : null;
                        var planner = style.PlansAhead
                            ? new TurnPlanner(recipe, answers, style, () => style.KnowsDeck ? Ygo.Engine.AI.Playbooks.Playbooks.ForDeck(decks[seat]) : null) { RemovalTiming = timing }
                            : null;
                        var log = only >= 0 ? logs[variant] : null;
                        return new SmartDuelist(style.Name, g * 2 + seat, context, log == null ? null : l => log.Add("> " + l), style, playbook, planner) { UseRemovalTiming = timing };
                    }
                    try
                    {
                        var runner = new DuelRunner(duel, Bot(0), Bot(1), answers);
                        runner.MessageReceived += context.Observe;
                        YataLock.Attach(runner, duel);
                        if (only >= 0)
                        {
                            var narrator = new DuelNarrator(cards, styles[0].Name, styles[1].Name) { CodeAt = place => BoardState.PublicCodeAt(duel, place) };
                            var into = logs[variant];
                            narrator.Line += l => into.Add("  | " + l.Trim());
                            runner.MessageReceived += narrator.Handle;
                        }
                        var result = runner.Run(8000);
                        winner[variant] = result.Winner == pilotSeat ? 1 : 0;
                    }
                    catch (Exception ex) { Console.Error.WriteLine($"{name} game {g}: {ex.Message}"); winner[variant] = -1; }
                }
                if (only >= 0)
                {
                    int first = Enumerable.Range(0, Math.Min(logs[0].Count, logs[1].Count)).FirstOrDefault(i => logs[0][i] != logs[1][i], -1);
                    Console.WriteLine($"game {g}: first difference at line {first}");
                    for (int v = 0; v < 2; v++)
                    {
                        Console.WriteLine(v == 0 ? "--- new" : "--- as before");
                        foreach (var l in logs[v].Skip(Math.Max(0, first - 30)).Take(50)) Console.WriteLine(l);
                    }
                }
                if (winner[0] < 0 || winner[1] < 0) return;
                lock (wins)
                {
                    played++;
                    wins[0] += winner[0];
                    wins[1] += winner[1];
                    if (winner[0] != winner[1])
                    {
                        changed++;
                        Console.WriteLine($"  changed: game {g} vs {them.Id} ({them.Decks[0]}), {(pilotSeat == 0 ? "first" : "second")}: the new CPU {(winner[0] == 1 ? "won" : "lost")}");
                    }
                }
            });
            Console.WriteLine($"{name} ({pilot.Name}, skill {pilot.Skill}): {played} paired duels in {timer.Elapsed:mm\\:ss}: new CPU wins {wins[0]} ({100.0 * wins[0] / Math.Max(1, played):0.0}%), "
                              + $"as before {wins[1]} ({100.0 * wins[1] / Math.Max(1, played):0.0}%); {changed} changed winner");
        }
        return 0;
    }
}
