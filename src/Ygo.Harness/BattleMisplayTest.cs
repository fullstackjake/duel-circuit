using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Presentation;
using Ygo.Engine.Protocol;

namespace Ygo.Harness;

/// <summary>
/// `--battle-misplay-test`: two CPU misplays fixed on 2026-11-06 (docs/dev/TODO.md, "From the 2026-11-06 build").
/// 1. The bot's per-turn battle memory ("this monster survived an attack, don't attack it again") was kept by zone, so a
///    monster that replaced itself in the zone (Reborn Tengu: "If this face-up card leaves the field: Special Summon 1
///    Reborn Tengu from your Deck"), or any new monster there, was never attacked for the rest of the turn (the No Limits
///    Albion duel at seed 701115). Now it's kept by card (DuelContext.MonsterIdentity).
/// 2. AttackBenefit only read lowercase "cannot be destroyed by battle", so base Yubel, The Ultimate Nightmare and Spirit
///    of Yubel ("Cannot be destroyed by battle" at the start of their text) looked like free kills, and even Terror
///    Incarnate (lowercase) was attacked for "chip damage" its controller never takes: the CPU attacked into the burn.
/// Each case runs the fixed bot and the bot as before (both toggles off): the fixed bot must pass, and the old one must
/// show the misplay, so the case is known to catch it. Control cases (an ordinary monster, Marshmallon's chip damage)
/// and a conditional one (Dogmatika Ecclesia: the first version of fix 2 left it alone, endless-test seed 701099) must
/// be attacked the same by both. The plain bot and the planner (skill 1.0) are both checked.
///
/// `--battle-misplay-ab decks [--against decks] [--games N] [--seed S]`: a paired A/B, each duel played twice from the
/// same seed, the pilot with the fixes and as before (its opponent always as now), as `--removal-ab` does. It also
/// reports where the two versions of a duel first part ways (turn and prompt), so it shows the fix changes only battle play.
/// </summary>
public static class BattleMisplayTest
{
    private const uint Stone = 13039848, BlueEyes = 89631139, Tengu = 10028593, LabyrinthWall = 67284908, Kuriboh = 40640057,
        Marshmallon = 31305911, Ecclesia = 60303688, UtopicFuture = 65305468, Arahime = 75771170, BesBigCore = 14148099, Laquari = 78868776, Bestiari = 41470137, Blocker = 34743446, SolarFlare = 45985838, JarOfGreed = 83968380, YaibaRobo = 10315429, Sakuretsu = 56120475, CyberdarkEdge = 77625948, Yubel = 78371393, TerrorIncarnate = 4779091, UltimateNightmare = 31764700, SpiritOfYubel = 90829280;

    /// <summary>DUEL_ATTACK_FIRST_TURN (ocgapi_constants.h): the first player may attack on turn 1.</summary>
    private const ulong AttackFirstTurn = 0x02;

    /// <summary>
    /// A board for the opponent (seat 0, a script that only passes); the CPU (seat 1) goes first with Blue-Eyes (or
    /// <c>Attacker</c>, with <c>CpuDeck</c> on top of its Deck). <c>OpponentFirst</c>: the opponent's turn comes first, and
    /// it Normal Summons the monster in its hand (B.E.S. gets its counters that way); the CPU's turn is then turn 2.
    /// <c>TriggersOnly</c>: "as before" turns off only the battle-trigger sense (the earlier fixes stay on). <c>BotOnly</c>:
    /// the planner may judge otherwise (it plays the battle out and scores the board).
    /// </summary>
    private sealed record Case(string Name, PlacedCard[] Opponent, uint[] OpponentDeck, int Attackers,
        Func<Outcome, bool> Fixed, Func<Outcome, bool> AsBefore, uint Attacker = BlueEyes, uint[]? CpuDeck = null, bool OpponentFirst = false,
        bool TriggersOnly = false, bool BotOnly = false, bool LethalOnly = false, uint StartingLp = 8000, uint[]? CpuField = null, uint[]? OpponentActivates = null);

    private sealed class Outcome
    {
        public readonly List<uint> Targets = new();
        /// <summary>The CPU's attackers, in the order they attacked.</summary>
        public readonly List<uint> Attackers = new();
        /// <summary>The planner's battle decisions, with each candidate's charges (TurnPlanner.Scored).</summary>
        public readonly List<PlannerDecision> Decisions = new();
        public int CpuLp, OpponentLp;
        public bool TurnEnded;
        public readonly List<string> Log = new();
        public int Hits(uint code) => Targets.Count(t => t == code);
        public override string ToString() =>
            $"attacked [{string.Join(", ", Targets.Select(t => t == 0 ? "directly" : t.ToString()))}], LP CPU {CpuLp} / opponent {OpponentLp}";
    }

    public static int Run(ICardDataProvider cards, IScriptProvider scripts)
    {
        PlacedCard Up(uint code, uint zone, uint position = Position.FaceUpAttack) => new(0, code, Location.MZone, zone, position);
        PlacedCard Wall(uint zone) => Up(LabyrinthWall, zone, Position.FaceUpDefense);
        var cases = new List<Case>
        {
            // Zones 1-4 hold Labyrinth Walls (3000 DEF: Blue-Eyes can't get through), so each new Tengu comes to zone 0.
            new("attacks each Reborn Tengu that replaces itself in the same zone (3 Blue-Eyes, 3 Tengu)",
                new[] { Up(Tengu, 0), Wall(1), Wall(2), Wall(3), Wall(4) }, new[] { Tengu, Tengu }, 3,
                o => o.Hits(Tengu) == 3, o => o.Hits(Tengu) == 1),
            new("attacks a new monster in a zone it already attacked (Tengu's last replacement, then nothing left)",
                new[] { Up(Tengu, 0), Wall(1), Wall(2), Wall(3), Wall(4) }, new[] { Tengu }, 3,
                o => o.Hits(Tengu) == 2, o => o.Hits(Tengu) == 1),
            new("doesn't attack Yubel in Attack Position (\"Cannot be destroyed by battle\"; it burns the attacker)",
                new[] { Up(Yubel, 0) }, Array.Empty<uint>(), 1,
                o => o.Targets.Count == 0 && o.CpuLp == 8000, o => o.Hits(Yubel) == 1 && o.CpuLp < 8000),
            new("doesn't attack Yubel - Terror Incarnate in Attack Position (no chip damage reaches its controller)",
                new[] { Up(TerrorIncarnate, 0) }, Array.Empty<uint>(), 1,
                o => o.Targets.Count == 0 && o.CpuLp == 8000, o => o.Hits(TerrorIncarnate) == 1 && o.CpuLp < 8000),
            new("doesn't attack Yubel - The Ultimate Nightmare in Attack Position",
                new[] { Up(UltimateNightmare, 0) }, Array.Empty<uint>(), 1,
                o => o.Targets.Count == 0 && o.CpuLp == 8000, o => o.Hits(UltimateNightmare) == 1),
            new("doesn't attack Spirit of Yubel (\"Cannot be destroyed by battle, also you take no damage\")",
                new[] { Up(SpiritOfYubel, 0) }, Array.Empty<uint>(), 1,
                o => o.Targets.Count == 0, o => o.Hits(SpiritOfYubel) == 1),
            new("doesn't attack Number F0: Utopic Future (\"neither player takes any battle damage\"; it takes the attacker)",
                new[] { Up(UtopicFuture, 0) }, Array.Empty<uint>(), 1,
                o => o.Targets.Count == 0 && o.CpuLp == 8000, o => o.Hits(UtopicFuture) == 1),
            new("doesn't attack Arahime the Manifested Mikanko (\"your opponent takes any battle damage you would have taken\")",
                new[] { Up(Arahime, 0) }, Array.Empty<uint>(), 1,
                o => o.Targets.Count == 0 && o.CpuLp == 8000, o => o.Hits(Arahime) == 1 && o.CpuLp < 8000),
            // Battle triggers (from the review): a B.E.S. loses a counter each battle, so every attacker that lives through
            // it goes on; a Gladiator Beast that battles and lives tags out for one from the Deck.
            new("attacks B.E.S. Big Core again after each counter it loses (3 Blue-Eyes, 3 counters)",
                new[] { new PlacedCard(0, BesBigCore, Location.Hand), Up(Stone, 0, Position.FaceUpDefense) }, Array.Empty<uint>(), 3,
                o => o.Hits(BesBigCore) == 3, o => o.Hits(BesBigCore) == 1, OpponentFirst: true, TriggersOnly: true),
            new("attacks a B.E.S. Big Core in Defense Position with no counter left (it's destroyed)",
                new[] { Up(BesBigCore, 0, Position.FaceUpDefense) }, Array.Empty<uint>(), 1,
                o => o.Hits(BesBigCore) == 1, o => o.Targets.Count == 0, TriggersOnly: true),
            new("attacks with Gladiator Beast Laquari into a wall it ties (Blocker, 1800 DEF), to tag out for Bestiari",
                new[] { Up(Blocker, 0, Position.FaceUpDefense) }, Array.Empty<uint>(), 1,
                o => o.Hits(Blocker) == 1, o => o.Targets.Count == 0, Attacker: Laquari, CpuDeck: new[] { Bestiari }, TriggersOnly: true, BotOnly: true),
            new("doesn't attack with Laquari into a higher DEF for the tag-out (Giant Soldier of Stone, 2000 DEF: it costs LP)",
                new[] { Up(Stone, 0, Position.FaceUpDefense) }, Array.Empty<uint>(), 1,
                o => o.Targets.Count == 0, o => o.Targets.Count == 0, Attacker: Laquari, CpuDeck: new[] { Bestiari }, TriggersOnly: true),
            new("doesn't attack with Laquari into that tie when no Gladiator Beast is left in the Deck",
                new[] { Up(Blocker, 0, Position.FaceUpDefense) }, Array.Empty<uint>(), 1,
                o => o.Targets.Count == 0, o => o.Targets.Count == 0, Attacker: Laquari, TriggersOnly: true),
            // The misplay hunt's "missed lethal" (2026-11-06, three full-strength planners: Count Aurum, Augie Marr, Anselm
            // Quist): a direct attack that wins was charged for the set cards' trap risk, so a later win in the same turn
            // (here Solar Flare Dragon's End Phase burn) looked better, though the opponent's hidden cards can stop it.
            // Only the planner charges that risk; the plain bot attacks either way.
            new("takes the lethal direct attack rather than a later win (Blue-Eyes into 500 LP, Solar Flare Dragon's burn, 2 set cards)",
                new[] { new PlacedCard(0, JarOfGreed, Location.SZone, 1, Position.FaceDownDefense), new PlacedCard(0, JarOfGreed, Location.SZone, 2, Position.FaceDownDefense) },
                Array.Empty<uint>(), 1,
                o => o.Hits(0) >= 1, o => o.Hits(0) == 0, LethalOnly: true, StartingLp: 500, CpuField: new[] { SolarFlare }),
            // Review: the lethal fix must keep the planner's bait ordering. Blue-Eyes and a 1000-ATK attacker against 2500 LP
            // and three set cards (one is Sakuretsu Armor, which the opponent springs): the weaker one attacks first, eats
            // Sakuretsu, and Blue-Eyes wins.
            new("baits a set card with its weaker attacker before the lethal one (Yaiba Robo, then Blue-Eyes, into 2500 LP and Sakuretsu Armor)",
                new[] { new PlacedCard(0, Sakuretsu, Location.SZone, 1, Position.FaceDownDefense), new PlacedCard(0, JarOfGreed, Location.SZone, 2, Position.FaceDownDefense),
                        new PlacedCard(0, JarOfGreed, Location.SZone, 3, Position.FaceDownDefense) },
                Array.Empty<uint>(), 1,
                o => o.OpponentLp == 0 && o.Attackers.FirstOrDefault() == YaibaRobo, o => o.OpponentLp == 0,
                LethalOnly: true, StartingLp: 2500, CpuField: new[] { YaibaRobo }, OpponentActivates: new[] { Sakuretsu }),
            // Review: lethal is the rollout's win, not the printed ATK. Cyberdark Edge attacks directly for half (400) into
            // 500 LP: not lethal, so the attack keeps its trap charge and stopping gets none.
            new("charges a halved direct attack as usual (Cyberdark Edge's 400 into 500 LP is no lethal), and charges stopping nothing",
                new[] { Up(Stone, 0, Position.FaceUpDefense), new PlacedCard(0, JarOfGreed, Location.SZone, 1, Position.FaceDownDefense),
                        new PlacedCard(0, JarOfGreed, Location.SZone, 2, Position.FaceDownDefense) },
                Array.Empty<uint>(), 1,
                o => o.Decisions.Any(d => d.Scores.Any(x => x.Direct && x.Risk > 0) && d.Scores.Any(x => x.Label == "stop attacking" && x.Risk == 0)),
                o => true, Attacker: CyberdarkEdge, LethalOnly: true, StartingLp: 500),
            // Controls: where the fix shouldn't apply, both versions attack alike.
            new("still attacks an ordinary monster (Kuriboh), with every attacker", new[] { Up(Kuriboh, 0) }, Array.Empty<uint>(), 2,
                o => o.Hits(Kuriboh) == 1 && o.Hits(0) == 1, o => o.Hits(Kuriboh) == 1 && o.Hits(0) == 1),
            new("still attacks Marshmallon in Attack Position for the damage (\"Cannot be destroyed by battle\", no damage shield)",
                new[] { Up(Marshmallon, 0) }, Array.Empty<uint>(), 1,
                o => o.Hits(Marshmallon) == 1 && o.OpponentLp < 8000, o => o.Hits(Marshmallon) == 1 && o.OpponentLp < 8000),
            new("still attacks Dogmatika Ecclesia with a Main Deck monster (\"Cannot be destroyed by battle with a monster Special Summoned from the Extra Deck\")",
                new[] { Up(Ecclesia, 0, Position.FaceUpDefense) }, Array.Empty<uint>(), 2,
                o => o.Hits(Ecclesia) == 1 && o.Hits(0) == 1, o => o.Hits(Ecclesia) == 1 && o.Hits(0) == 1),
        };
        int bad = IdentityChecks(cards, scripts);
        foreach (var c in cases)
            foreach (bool planner in new[] { false, true })
            {
                var now = Play(cards, scripts, c, planner, fixes: true);
                var before = Play(cards, scripts, c, planner, fixes: false);
                // The planner tests each attack in a scratch duel, so it may already have dodged a misplay: only the plain bot must show it.
                // LethalOnly: the planner's case (the plain bot attacks either way, as before).
                bool ok = c.LethalOnly
                    ? !planner || (now.TurnEnded && c.Fixed(now) && c.AsBefore(before))
                    : now.TurnEnded && (c.Fixed(now) || planner && c.BotOnly) && (planner || c.AsBefore(before));
                if (!ok) bad++;
                Console.WriteLine($"{(ok ? "ok  " : "FAIL")} the CPU {c.Name} ({(planner ? "the planner" : "the bot")}): now {Named(now, cards)}; "
                                  + $"as before {Named(before, cards)}{(planner || c.AsBefore(before) ? "" : " (the old misplay didn't show)")}");
                if (!ok || Environment.GetEnvironmentVariable("BATTLE_MISPLAY_LOG") == "1")
                    foreach (var line in now.Log.Where(l => l.Length > 0).TakeLast(20)) Console.WriteLine("       " + line);
            }
        Console.WriteLine(bad == 0 ? "battle misplay test: all passed" : $"battle misplay test: {bad} failed");
        return bad == 0 ? 0 : 1;
    }

    /// <summary>
    /// DuelContext.MonsterIdentity fed hand-built MSG_MOVE / MSG_SWAP messages: a card keeps its number while it moves
    /// between monster zones (a zone shift, a change of control, a swap sent as MSG_SWAP or as two MSG_MOVEs), and every card
    /// that arrives from anywhere else gets a new one (a summon, an Xyz Monster on top of its materials, a Reborn Tengu
    /// replacing itself in the same zone).
    /// </summary>
    private static int IdentityChecks(ICardDataProvider cards, IScriptProvider scripts)
    {
        var filler = new Deck { Name = "filler" };
        while (filler.Main.Count < 40) filler.Main.Add(Stone);
        var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = 3 }, Deck0 = filler, Deck1 = filler.Clone() };
        using var duel = recipe.Create();
        var ctx = new DuelContext(duel, cards);
        static byte[] Loc(byte controller, byte location, uint sequence, uint position = Position.FaceUpAttack) =>
            new[] { controller, location }.Concat(BitConverter.GetBytes(sequence)).Concat(BitConverter.GetBytes(position)).ToArray();
        static byte[] Move(uint code, byte[] from, byte[] to) =>
            new[] { Msg.Move }.Concat(BitConverter.GetBytes(code)).Concat(from).Concat(to).Concat(BitConverter.GetBytes(0u)).ToArray();
        static byte[] Swap(uint codeA, byte[] a, uint codeB, byte[] b) =>
            new byte[] { 55 }.Concat(BitConverter.GetBytes(codeA)).Concat(a).Concat(BitConverter.GetBytes(codeB)).Concat(b).ToArray();
        byte mz = (byte)Location.MZone, hand = (byte)Location.Hand, deck = (byte)Location.Deck, extra = (byte)Location.Extra,
            grave = (byte)Location.Grave, overlay = (byte)(Location.MZone | Location.Overlay);
        int Id(int controller, uint seq) => ctx.MonsterIdentity(controller, seq);
        int bad = 0;
        void Check(string what, bool ok)
        {
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} card identity: {what}");
        }

        ctx.Observe(Move(BlueEyes, Loc(0, hand, 0), Loc(0, mz, 0)));
        int a = Id(0, 0);
        ctx.Observe(Move(Kuriboh, Loc(0, hand, 1), Loc(0, mz, 1)));
        int b = Id(0, 1);
        Check("a summon gets a new number (and two summons differ)", a != 0 && b != 0 && a != b);
        ctx.Observe(Move(BlueEyes, Loc(0, mz, 0), Loc(0, mz, 2)));
        Check("a zone shift keeps it", Id(0, 2) == a);
        ctx.Observe(Move(BlueEyes, Loc(0, mz, 2), Loc(1, mz, 3)));
        Check("a change of control keeps it", Id(1, 3) == a);
        ctx.Observe(Swap(BlueEyes, Loc(1, mz, 3), Kuriboh, Loc(0, mz, 1)));
        Check("MSG_SWAP trades the two", Id(0, 1) == a && Id(1, 3) == b);
        // field.cpp swap_card with a sequence change: two MSG_MOVEs, ordered so neither lands on the other's zone first.
        ctx.Observe(Move(Kuriboh, Loc(1, mz, 3), Loc(0, mz, 4)));
        ctx.Observe(Move(BlueEyes, Loc(0, mz, 1), Loc(1, mz, 3)));
        Check("a swap sent as two MSG_MOVEs keeps both", Id(0, 4) == b && Id(1, 3) == a);
        // An Xyz Summon over Kuriboh: the material goes under, the Xyz Monster comes from the Extra Deck into a zone.
        ctx.Observe(Move(Kuriboh, Loc(0, mz, 4), Loc(0, overlay, 0)));
        ctx.Observe(Move(UtopicFuture, Loc(0, extra, 0), Loc(0, mz, 4)));
        int xyz = Id(0, 4);
        Check("Xyz material leaving its zone hands its number to nothing (the Xyz Monster there is new)", xyz != b && xyz != a);
        // Reborn Tengu: destroyed, and a new one from the Deck lands in the same zone.
        ctx.Observe(Move(Tengu, Loc(1, hand, 0), Loc(1, mz, 0)));
        int tengu = Id(1, 0);
        ctx.Observe(Move(Tengu, Loc(1, mz, 0), Loc(1, grave, 0)));
        ctx.Observe(Move(Tengu, Loc(1, deck, 0), Loc(1, mz, 0)));
        Check("a Reborn Tengu replacing itself in the same zone is a new card", Id(1, 0) != tengu && Id(1, 0) != 0);
        Check("cards elsewhere are untouched by all that", Id(1, 3) == a && Id(0, 4) == xyz);
        return bad;
    }

    private static string Named(Outcome o, ICardDataProvider cards) =>
        $"attacked [{string.Join(", ", o.Targets.Select(t => t == 0 ? "directly" : cards.Get(t)?.Name ?? t.ToString()))}], LP {o.CpuLp}/{o.OpponentLp}";

    private static Outcome Play(ICardDataProvider cards, IScriptProvider scripts, Case c, bool planner, bool fixes)
    {
        var filler = new Deck { Name = "filler" };
        while (filler.Main.Count < 40) filler.Main.Add(Stone);
        var theirs = filler.Clone();
        for (int i = 0; i < c.OpponentDeck.Length; i++) theirs.Main[i] = c.OpponentDeck[i];
        var mine = filler.Clone();
        for (int i = 0; i < (c.CpuDeck?.Length ?? 0); i++) mine.Main[i] = c.CpuDeck![i];
        var recipe = new DuelRecipe
        {
            Cards = cards, Scripts = scripts,
            Settings = new DuelSettings { Flags = DuelFlags.ModeMR5 | AttackFirstTurn, Seed = 17, StartingHand = 0, FirstPlayer = c.OpponentFirst ? 0 : 1, StartingLP = c.StartingLp },
            Deck0 = theirs, Deck1 = mine,
        };
        foreach (var card in c.Opponent) recipe.Setup.Add(card);
        for (int i = 0; i < c.Attackers; i++) recipe.Setup.Add(new PlacedCard(1, c.Attacker, Location.MZone, (uint)i));
        for (int i = 0; i < (c.CpuField?.Length ?? 0); i++) recipe.Setup.Add(new PlacedCard(1, c.CpuField![i], Location.MZone, (uint)(c.Attackers + i)));
        using var duel = recipe.Create();
        var context = new DuelContext(duel, cards);
        var answers = new ResponseLog();
        var o = new Outcome();
        var style = new Personality { Name = "CPU", Skill = 1.0 };
        var cpu = new SmartDuelist("CPU", 3, context, s => o.Log.Add("> " + s), style, null,
            planner ? new TurnPlanner(recipe, answers, style, () => null)
            {
                CardIdentity = fixes || c.TriggersOnly || c.LethalOnly, IndestructibleSense = fixes || c.TriggersOnly || c.LethalOnly,
                BattleTriggerSense = fixes || c.LethalOnly, LethalRiskSense = fixes || !c.LethalOnly,
                Scored = d => o.Decisions.Add(d),
            } : null)
        {
            UseCardIdentity = fixes || c.TriggersOnly, UseIndestructibleSense = fixes || c.TriggersOnly, UseBattleTriggerSense = fixes,
        };
        var script = new ScriptedDuelist(5, cards);
        foreach (var card in c.Opponent.Where(x => x.Location == Location.Hand)) script.NormalSummon.Add(card.Code);
        foreach (var code in c.OpponentActivates ?? Array.Empty<uint>()) script.Activate.Add(code);
        var runner = new DuelRunner(duel, script, cpu, answers);
        runner.MessageReceived += context.Observe;
        runner.MessageReceived += m =>
        {
            if (m[0] != Msg.Attack) return;
            var r = new MessageReader(m, 1);
            var attacker = r.LocInfo(0);
            var target = r.LocInfo(0);
            if ((attacker.Controller & 1) != 1) return;
            o.Attackers.Add(duel.QueryLocation(1, Location.MZone).ElementAtOrDefault((int)attacker.Sequence)?.Code ?? 0);
            o.Targets.Add((target.Location & Location.MZone) != 0
                ? duel.QueryLocation(0, Location.MZone).ElementAtOrDefault((int)target.Sequence)?.Code ?? 0
                : 0);
        };
        var narrator = new DuelNarrator(cards, "Script", "CPU");
        narrator.Line += l => o.Log.Add(l.Trim());
        runner.MessageReceived += narrator.Handle;
        try
        {
            // The CPU's turn (1, or 2 after the opponent's), through its End Phase.
            int cpuTurn = c.OpponentFirst ? 2 : 1;
            for (int steps = 0; steps < 6000 && runner.Result.Turns <= cpuTurn && runner.Step(); steps++) { }
            o.TurnEnded = runner.Result.Turns > cpuTurn || runner.IsFinished;
        }
        catch (InvalidOperationException ex) { o.Log.Add(ex.Message); }
        o.CpuLp = context.Board.Players[1].LifePoints;
        o.OpponentLp = context.Board.Players[0].LifePoints;
        return o;
    }

    // ------------------------------------------------------------------ the paired A/B

    /// <summary>Records a duelist's answers with the turn and prompt, to find where two versions of a duel part ways.</summary>
    private sealed class Recorder : IDuelist
    {
        private readonly IDuelist _inner;
        private readonly DuelContext _ctx;
        public readonly List<(int turn, string prompt, string answer)> Steps = new();
        public Recorder(IDuelist inner, DuelContext ctx) { _inner = inner; _ctx = ctx; }
        public void OnPhaseChanged() => _inner.OnPhaseChanged();
        public byte[]? Respond(Prompt prompt, int attempt)
        {
            var answer = _inner.Respond(prompt, attempt);
            string kind = prompt.GetType().Name;
            if (prompt is SelectCardPrompt && _ctx.PromptHint[prompt.Player & 1] == DuelContext.HintAttackTarget) kind = "AttackTarget";
            Steps.Add((_ctx.Board.Turn, kind, answer == null ? "" : Convert.ToHexString(answer)));
            return answer;
        }
    }

    /// <summary>
    /// <paramref name="fix"/>: what the pilot plays without in the "as before" duel. "battle" (the 2026-11-06 battle
    /// memory and indestructible fixes); the misplay hunt's one at a time: "deckset" (Set Rotation), "triggers" (B.E.S.,
    /// Gladiator Beasts), "selfdamage", "recovery" (GY to hand), "window" (attack before optional Spells), "lethal" (the
    /// planner's lethal charge), "basecodes" (card versions 3.1); "hunt" (deckset, triggers, selfdamage), "huntall"
    /// (every hunt fix), or "all".
    /// </summary>
    private sealed class AbStats
    {
        public string Pilot = "";
        public readonly int[] Wins = new int[2];
        public int Changed, Played, Identical, TurnOne, PilotTurnOne;
        public long TurnsNew, TurnsOld;
        public readonly Dictionary<string, int> PartAt = new();
        public readonly List<string> Notes = new();
    }

    public static int RunAb(ICardDataProvider cards, IScriptProvider scripts, string root, string deckList, string? againstList, int games, int seed,
        string fix = "battle")
    {
        var fixes = new[] { "battle", "deckset", "triggers", "selfdamage", "recovery", "window", "lethal", "basecodes" };
        if (!fixes.Contains(fix) && fix is not ("hunt" or "huntall" or "all")) { Console.Error.WriteLine($"--fix {fix}: use {string.Join(", ", fixes)}, huntall or all"); return 1; }
        // huntall: everything the misplay hunt changed (battle triggers, Set Rotation, GY recovery, the activation window, base codes).
        // hunt: deckset, triggers and selfdamage together.
        bool Off(string which, bool fixes) => fixes || (fix != which && fix != "all" && !(fix == "huntall" && which != "battle")
                                                         && !(fix == "hunt" && which is "deckset" or "triggers" or "selfdamage"));
        Console.WriteLine($"paired A/B: the pilot as now against the pilot without '{fix}'");
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        var everyone = Directory.GetFiles(Path.Combine(root, "personalities"), "*.profile").Select(Personality.Load).OrderBy(p => p.Id).ToList();
        var roster = everyone.Where(p => p.Decks.Count > 0 && deckFiles.ContainsKey(p.Decks[0])).ToList();
        // --against: the opponents' decks, each with its own pilot (or a plain one); otherwise the whole roster, each on its first deck.
        var against = againstList == null
            ? roster.Select(p => (pilot: p, deck: p.Decks[0])).ToList()
            : againstList.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0)
                .Select(n => (pilot: everyone.FirstOrDefault(p => p.Decks.Contains(n)) ?? new Personality { Id = n, Name = n }, deck: n)).ToList();
        Deck LoadDeck(string name) { var d = Deck.LoadYdk(deckFiles[name]); d.Name = name; return d; }
        // "roster": every deck a character plays (the whole campaign, post-game and No Limits included).
        var names = deckList == "roster"
            ? everyone.SelectMany(p => p.Decks).Where(deckFiles.ContainsKey).Distinct().OrderBy(n => n).ToList()
            : deckList.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
        var stats = names.ToDictionary(n => n, _ => new AbStats());
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var work = names.SelectMany(n => Enumerable.Range(0, games).Select(g => (name: n, g))).ToList();
        int done = 0;
        // One loop over every (deck, game): no deck waits on its slowest duel.
        Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, item =>
        {
            var (name, g) = item;
            var st = stats[name];
            var pilot = everyone.FirstOrDefault(p => p.Decks.Contains(name)) ?? new Personality { Id = name, Name = name };
            var rng = new Random(seed * 1000 + g);
            var them = against[rng.Next(against.Count)];
            int pilotSeat = g % 2; // half the duels going first
            var winner = new int[2];
            var turns = new int[2];
            var steps = new List<(int turn, string prompt, string answer)>[2];
            for (int variant = 0; variant < 2; variant++) // 0: with the fixes, 1: as before
            {
                var styles = new Personality[2];
                styles[pilotSeat] = pilot; styles[1 - pilotSeat] = them.pilot;
                var decks = new Deck[2];
                decks[pilotSeat] = LoadDeck(name); decks[1 - pilotSeat] = LoadDeck(them.deck);
                var recipe = new DuelRecipe { Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)(seed * 1000 + g) }, Deck0 = decks[0], Deck1 = decks[1] };
                using var duel = recipe.Create();
                var context = new DuelContext(duel, cards);
                var answers = new ResponseLog();
                Recorder? recorder = null;
                IDuelist Bot(int seat)
                {
                    var style = styles[seat];
                    bool fixes = seat != pilotSeat || variant == 0;
                    bool battle = Off("battle", fixes), deckSet = Off("deckset", fixes), triggers = Off("triggers", fixes),
                         selfDamage = Off("selfdamage", fixes), baseCodes = Off("basecodes", fixes),
                         recovery = Off("recovery", fixes), window = Off("window", fixes), lethal = Off("lethal", fixes);
                    Ygo.Engine.AI.Playbooks.Playbook? Book()
                    {
                        var book = style.KnowsDeck ? Ygo.Engine.AI.Playbooks.Playbooks.ForDeck(decks[seat]) : null;
                        if (book != null) book.UseBaseCodes = baseCodes;
                        return book;
                    }
                    var playbook = Book();
                    var planner = style.PlansAhead
                        ? new TurnPlanner(recipe, answers, style, Book)
                            { CardIdentity = battle, IndestructibleSense = battle, BattleTriggerSense = triggers, DeckSetSense = deckSet,
                              RecoverySense = recovery, ActivationWindowSense = window, LethalRiskSense = lethal, SelfDamageSense = selfDamage }
                        : null;
                    IDuelist bot = new SmartDuelist(style.Name, g * 2 + seat, context, null, style, playbook, planner)
                        { UseCardIdentity = battle, UseIndestructibleSense = battle, UseBattleTriggerSense = triggers, UseDeckSetSense = deckSet,
                          UseRecoverySense = recovery, UseActivationWindowSense = window, UseSelfDamageSense = selfDamage };
                    if (seat == pilotSeat) bot = recorder = new Recorder(bot, context);
                    return bot;
                }
                try
                {
                    var runner = new DuelRunner(duel, Bot(0), Bot(1), answers);
                    runner.MessageReceived += context.Observe;
                    YataLock.Attach(runner, duel);
                    var result = runner.Run(8000);
                    winner[variant] = result.Winner == pilotSeat ? 1 : 0;
                    turns[variant] = result.Turns;
                }
                catch (Exception ex) { Console.Error.WriteLine($"{name} game {g}: {ex.Message}"); winner[variant] = -1; }
                if (recorder == null) { winner[variant] = -1; continue; } // Bot() threw before the pilot was made
                steps[variant] = recorder.Steps;
            }
            if (Interlocked.Increment(ref done) % 200 == 0) Console.Error.WriteLine($"  ... {done} of {work.Count} paired duels, {timer.Elapsed:hh\\:mm\\:ss}");
            if (winner[0] < 0 || winner[1] < 0) return;
            int first = Enumerable.Range(0, Math.Min(steps[0].Count, steps[1].Count)).FirstOrDefault(i => steps[0][i] != steps[1][i], -1);
            if (first < 0 && steps[0].Count != steps[1].Count) first = Math.Min(steps[0].Count, steps[1].Count);
            lock (st)
            {
                st.Pilot = $"{pilot.Name}, skill {pilot.Skill}";
                st.Played++;
                st.Wins[0] += winner[0];
                st.Wins[1] += winner[1];
                st.TurnsNew += turns[0];
                st.TurnsOld += turns[1];
                if (first < 0) st.Identical++;
                else
                {
                    var at = first < steps[0].Count ? steps[0][first] : steps[1][first];
                    st.PartAt[at.prompt] = st.PartAt.TryGetValue(at.prompt, out var n) ? n + 1 : 1;
                    if (at.turn <= 1) st.TurnOne++;
                    if (at.turn <= 2 && (at.turn % 2 == 1) == (pilotSeat == 0)) st.PilotTurnOne++;
                }
                if (winner[0] != winner[1])
                {
                    st.Changed++;
                    st.Notes.Add($"  changed: {name} game {g} vs {them.pilot.Id} ({them.deck}), {(pilotSeat == 0 ? "first" : "second")}: the fixed CPU {(winner[0] == 1 ? "won" : "lost")}");
                }
            }
        });
        int totalPlayed = 0, totalNew = 0, totalOld = 0, moved = 0;
        foreach (var name in names)
        {
            var st = stats[name];
            foreach (var n in st.Notes) Console.WriteLine(n);
            totalPlayed += st.Played; totalNew += st.Wins[0]; totalOld += st.Wins[1];
            // The paired error: only duels that changed winner move the difference, each by one, either way.
            int net = st.Wins[0] - st.Wins[1];
            double err = Math.Sqrt(Math.Max(1, st.Changed));
            string mark = net < -2 * err ? "DROP" : net > 2 * err ? "RISE" : "    ";
            if (mark != "    ") moved++;
            Console.WriteLine($"{mark} {name} ({st.Pilot}){(againstList == null ? "" : $" vs {againstList}")}: {st.Played} paired duels: "
                              + $"fixed CPU wins {st.Wins[0]} ({100.0 * st.Wins[0] / Math.Max(1, st.Played):0.0}%), as before {st.Wins[1]} ({100.0 * st.Wins[1] / Math.Max(1, st.Played):0.0}%); "
                              + $"net {net:+0;-0;0} (error {err:0.0}); {st.Changed} changed winner; {st.Identical} identical; parted at [{string.Join(", ", st.PartAt.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"))}]; "
                              + $"on turn 1: {st.TurnOne}, on the pilot's first turn: {st.PilotTurnOne}; mean turns {st.TurnsNew / (double)Math.Max(1, st.Played):0.0} vs {st.TurnsOld / (double)Math.Max(1, st.Played):0.0}");
        }
        Console.WriteLine($"total: {totalPlayed} paired duels over {names.Count} decks in {timer.Elapsed:hh\\:mm\\:ss}: fixed CPU wins {totalNew}, as before {totalOld}; "
                          + $"{moved} decks moved by more than two errors");
        return 0;
    }
}
