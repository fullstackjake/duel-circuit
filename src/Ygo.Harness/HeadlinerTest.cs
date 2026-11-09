using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.Protocol;
using Ygo.Engine.Replays;

namespace Ygo.Harness;

/// <summary>
/// `--campaign-test`'s headliner checks (docs/dev/HEADLINERS-2026-11-01.md phase 3; ALLEY-SPEC section 16, Phase D), also alone
/// as `--headliner-test`: every headliner's deck, playbook (a route with a payoff and a meter), profile, scenes and tell;
/// ready headliners' Watch the combo clips (they play back, seat 0 is the headliner) and route signals in a real duel;
/// Combo Breaker posters and BackAlley.Record's comboBroken; ComboWatch; and that the base decks a headliner is built from
/// play as they did (their playbooks unchanged).
/// </summary>
public static class HeadlinerTest
{
    /// <summary>Alone: loads what <see cref="Run(ICardDataProvider, string, PrintingIndex, List{Personality}, Circuits, Library, Deck)"/> needs.</summary>
    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root)
    {
        var printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities"));
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var library = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), roster, circuits);
        var starter = Deck.LoadYdk(Path.Combine(root, "decks", "classic.ydk"));
        int failures = Run(cards, root, printings, roster, circuits, library, starter, scripts);
        Console.WriteLine($"\n{(failures == 0 ? "All headliner checks passed." : $"{failures} headliner checks failed.")}");
        return failures == 0 ? 0 : 1;
    }

    public static int Run(ICardDataProvider cards, string root, PrintingIndex printings, List<Personality> roster, Circuits circuits, Library library, Deck starter,
        IScriptProvider? scripts = null)
    {
        scripts ??= new FileScriptProvider(Path.Combine(root, "vendor", "CardScripts"));
        int failures = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine("\nHeadliners (HEADLINERS-2026-11-01 phase 3; ALLEY-SPEC Phase D):");
        var s = AlleyTest.Load(cards, root, printings, roster, library);
        var data = s.Data;
        var ctx = s.Ctx;
        string Name(uint c) => cards.Get(c)?.Name ?? c.ToString();
        // No Limits' peak builds (build: blocks) parse as headliners (NO-LIMITS-SPEC 10.1) but aren't ones for these checks.
        var headliners = data.Headliners.Where(h => !h.IsBuild).ToList();

        // ---- 1. Decks and playbooks: every headliner's deck plays its own route, with a payoff and a meter.
        var noRoute = new List<string>();
        foreach (var h in headliners)
        {
            var deck = ctx.LoadDeck(h.Deck);
            var book = deck != null ? Playbooks.ForDeck(deck) : null;
            var routes = book?.Routes ?? new List<Playbook.RouteInfo>();
            if (deck == null || book == null || routes.Count == 0 || routes.Any(r => r.PayoffStep < 0 && r.Steps.Count == 0) || routes.All(r => r.PayoffStep < 0)
                || routes.Any(r => r.Counter.Length == 0))
                noRoute.Add(h.Id);
        }
        Check($"every headliner ({headliners.Count}) has a deck in decks/headliners whose playbook has a route with a payoff, and every route a meter"
              + $"{(noRoute.Count > 0 ? " (not: " + string.Join(", ", noRoute) + ")" : "")}: "
              + string.Join("; ", headliners.Select(h => ctx.LoadDeck(h.Deck) is Deck d && Playbooks.ForDeck(d) is Playbook b
                  ? $"{h.Id} {string.Join("/", b.Routes.Select(r => $"{r.Name} ({r.Steps.Count} steps, payoff {r.PayoffStep + 1}, {r.Counter})"))}"
                  : h.Id)),
            noRoute.Count == 0 && headliners.All(h => File.Exists(Path.Combine(root, "decks", "headliners", h.Deck + ".ydk"))));

        // A ready headliner validates under its own list with its over: cards (AlleyData.Problems), and the problems name none.
        var problems = data.Problems(ctx).Where(p => p.StartsWith("headliner ")).ToList();
        var ready = headliners.Where(h => h.Ready).ToList();
        Check($"the {ready.Count} ready headliners ({string.Join(", ", ready.Select(h => h.Handle))}) are legal under their own lists, their pieces and stakes in their decks"
              + $"{(problems.Count > 0 ? ": " + string.Join("; ", problems.Take(4)) : "")}", problems.Count == 0);

        // The base decks a headliner is built from play as they did: no playbook for plant-synchro or gouki, and spyral's
        // without the Master Plan route (the headliners' routes are theirs alone).
        var spyral = Playbooks.ForDeck("spyral");
        var marlo = Playbooks.ForDeck("master-plan-marlo");
        Check($"the base decks keep their playbooks: plant-synchro {Playbooks.ForDeck("plant-synchro")?.GetType().Name ?? "none"}, gouki {Playbooks.ForDeck("gouki")?.GetType().Name ?? "none"}, "
              + $"spyral {spyral?.Routes.Count} routes; master-plan-marlo {marlo?.Routes.Count} ({string.Join(", ", marlo?.Routes.Select(r => r.Name) ?? Array.Empty<string>())})",
            Playbooks.ForDeck("plant-synchro") == null && Playbooks.ForDeck("gouki") == null && spyral != null && spyral.Routes.Count == 0
            && marlo != null && marlo.Routes.Count > 0);

        // A headliner on its own recipe shows the rated deck its "#rating" names on the panel.
        var elos = new List<string>();
        bool elosOk = true;
        foreach (var h in headliners)
        {
            var deck = ctx.LoadDeck(h.Deck);
            if (deck == null) continue;
            var rated = deck.Note("rating");
            if (rated.Length == 0) continue;
            var o = new AlleyOpponent { Table = "test", Seat = 0, Kind = "headliner", Character = h.Id, Handle = h.Handle, Base = h.Deck };
            BackAlley.PersonalityOf(o, ctx); // fills its skill and Elo
            elos.Add($"{h.Id} {o.Elo:0} ({rated})");
            elosOk &= ctx.Ratings.TryGetValue(rated, out var want) && Math.Abs(o.Elo - want) < 0.5;
        }
        Check($"a headliner with \"#rating\" shows that deck's Elo: {string.Join(", ", elos)}", elosOk && elos.Count > 0);

        // ---- 2. Profiles, scenes, tells.
        var story = Story.Load(Path.Combine(root, "campaign", "story.txt"));
        var voiceText = File.ReadAllLines(Path.Combine(root, "campaign", "alley-voice.txt"));
        List<string> Pool(string hook) => voiceText.SkipWhile(l => l.Trim() != "pool: " + hook).Skip(1)
            .TakeWhile(l => !l.StartsWith("pool:") && !l.StartsWith("# ----")).Where(l => l.Contains(':') && !l.TrimStart().StartsWith("#")).ToList();
        var missing = new List<string>();
        foreach (var h in headliners)
        {
            var p = roster.FirstOrDefault(r => r.Id == h.Id);
            if (p == null || p.Name != h.Handle || Math.Abs(p.Skill - 1.0) > 1e-9 || p.Intro.Length == 0 || !p.Decks.Contains(h.Deck)
                || !p.Lines.TryGetValue("combo", out var combo) || combo.Count == 0 || !p.Lines.TryGetValue("fizzle", out var fizzle) || fizzle.Count == 0)
                missing.Add($"{h.Id}: profile");
            foreach (var (id, trigger) in new[] { ($"hl-{h.Id}", $"headliner:{h.Id}"), ($"hl-{h.Id}-beat", $"headlinerbeat:{h.Id}"), ($"hl-{h.Id}-breaker", BackAlley.ComboBreakerEvent + h.Id) })
                if (!story.Scenes.Any(sc => sc.Id == id && sc.Trigger == trigger)) missing.Add($"{h.Id}: scene {id}");
            if (Pool("tell:" + h.Id).Count == 0) missing.Add($"{h.Id}: tell");
        }
        Check($"every headliner has a profile (its handle, skill 1.0, intro, combo and fizzle lines, its deck), the three scenes (hl-<id>, -beat, -breaker) and Baz's tell"
              + $"{(missing.Count > 0 ? " (missing: " + string.Join("; ", missing) + ")" : "")}", missing.Count == 0);

        // ---- 3. Combo Breaker posters: one per ready headliner (the card the combo turns on), none for anyone else.
        var breakers = Posters.All.Where(p => p.Id.StartsWith(Posters.BreakerPrefix)).ToList();
        var orphan = breakers.Where(p => data.FindHeadliner(p.Id.Substring(Posters.BreakerPrefix.Length)) is not { Ready: true }).Select(p => p.Id).ToList();
        var lacking = ready.Where(h => Posters.BreakerFor(h.Id) == null).Select(h => h.Id).ToList();
        Check($"Combo Breaker posters: {breakers.Count} ({string.Join(", ", breakers.Select(p => $"{p.Tagline.Split('·')[0].Trim()}: {Name(p.Card)}"))}), "
              + $"every ready headliner has one{(lacking.Count > 0 ? " (not: " + string.Join(", ", lacking) + ")" : "")}, each names a ready one{(orphan.Count > 0 ? " (not: " + string.Join(", ", orphan) + ")" : "")}, each frames a real card",
            lacking.Count == 0 && orphan.Count == 0 && breakers.All(p => cards.Get(p.Card) != null) && breakers.Select(p => p.Id).Distinct().Count() == breakers.Count);

        // BackAlley.Record with comboBroken: a win at a headliner's seat earns its poster once ("combobreaker:<id>"); a win
        // without it, a loss with it, and an encore win with it earn nothing.
        int dumpster = data.IndexOf("dumpster-row");
        var headliner = ready.FirstOrDefault(h => h.Table == data.Tables[dumpster].Name);
        if (headliner != null)
        {
            AlleyResult? AtSeat(Profile p, int seat, int winner, bool broken)
            {
                while (BackAlley.NextSeat(p, data, dumpster) is var next && next >= 1 && next < seat) AlleyTest.Duel(p, s, dumpster, 0);
                var o = BackAlley.Lineup(p, data, dumpster, ctx).FirstOrDefault(x => x.Seat == BackAlley.NextSeat(p, data, dumpster));
                if (o == null || Ante.Hold(p, data, dumpster, o, null, _ => 0, ctx) == null) return null;
                return BackAlley.Record(p, data, dumpster, winner, 12, 0, ctx, comboBroken: broken);
            }
            var poster = Posters.BreakerFor(headliner.Id)!;
            // The closer (seat 4) is the table's first ready headliner (its night's seed may pick the other: then seat 4 is that one).
            var p1 = AlleyTest.Opened(starter, cards, printings, library, 0xB4EA0001);
            var lost = AtSeat(p1, 4, 1, true);
            var seat4 = BackAlley.Lineup(p1, data, dumpster, ctx).FirstOrDefault(x => x.Seat == 4);
            bool lossNothing = lost != null && !lost.ComboBreaker && !lost.Events.Any(e => e.StartsWith(BackAlley.ComboBreakerEvent));
            var plain = AtSeat(p1, 4, 0, false);
            bool plainNothing = plain != null && !plain.ComboBreaker && seat4 != null && !(p1.Headliners.TryGetValue(seat4.Character, out var st1) && st1.Breaker > 0);
            var p2 = AlleyTest.Opened(starter, cards, printings, library, 0xB4EA0001);
            var won = AtSeat(p2, 4, 0, true);
            var who = seat4?.Character ?? "";
            var whoPoster = Posters.BreakerFor(who);
            bool earned = won != null && won.ComboBreaker && won.Events.Contains(BackAlley.ComboBreakerEvent + who) && whoPoster != null && whoPoster.Earned(circuits, p2)
                          && story.Scenes.Any(sc => sc.Trigger == BackAlley.ComboBreakerEvent + who) && won.Summary.Contains("Combo Breaker");
            var back = AlleyTest.RoundTrip(p2, printings, cards);
            bool saved = back.Headliners.TryGetValue(who, out var st2) && st2.Breaker == 1 && whoPoster != null && whoPoster.Earned(circuits, back);
            // An encore of it (beaten now): a broken route and a win earn no poster and no second event.
            var encore = BackAlley.Encore(p2, data, dumpster, who, ctx);
            AlleyResult? again = null;
            if (encore != null && Ante.Hold(p2, data, dumpster, encore, null, _ => 0, ctx) != null) again = BackAlley.Record(p2, data, dumpster, 0, 12, 0, ctx, comboBroken: true);
            bool encoreNothing = again != null && !again.ComboBreaker && !again.Events.Any(e => e.StartsWith(BackAlley.ComboBreakerEvent)) && p2.Headliners[who].Breaker == 1;
            var fresh = AlleyTest.Opened(starter, cards, printings, library, 0xB4EA0002);
            Check($"Combo Breaker: a win at {who}'s seat with its route broken earns \"{whoPoster?.Title}\" ({whoPoster?.Tagline}), raises {BackAlley.ComboBreakerEvent}{who} and keeps "
                  + $"after saving; a loss with it broken, a win without, an encore win and a fresh save earn nothing ({string.Join(", ", new[] { lossNothing, plainNothing, earned, saved, encoreNothing }.Select(b => b ? "y" : "n"))})",
                lossNothing && plainNothing && earned && saved && encoreNothing && !Posters.BreakerFor(who)!.Earned(circuits, fresh) && poster != null);
        }
        else Check("Combo Breaker: Dumpster Row has a ready headliner to test with", false);

        // ComboWatch: a route stopped before its payoff, with none landing, is the poster's "broken".
        Playbook.RouteSignal Sig(Playbook.RouteEvent k, bool finished = false) => new Playbook.RouteSignal { Kind = k, Route = "r", Steps = 4, Step = 2, Finished = finished };
        var w1 = new ComboWatch();
        foreach (var x in new[] { Sig(Playbook.RouteEvent.Started), Sig(Playbook.RouteEvent.Step), Sig(Playbook.RouteEvent.Ended) }) w1.Observe(x);
        var w2 = new ComboWatch();
        foreach (var x in new[] { Sig(Playbook.RouteEvent.Started), Sig(Playbook.RouteEvent.Payoff), Sig(Playbook.RouteEvent.Ended, true) }) w2.Observe(x);
        var w3 = new ComboWatch();
        foreach (var x in new[] { Sig(Playbook.RouteEvent.Started), Sig(Playbook.RouteEvent.Ended), Sig(Playbook.RouteEvent.Started), Sig(Playbook.RouteEvent.Payoff) }) w3.Observe(x);
        var w4 = new ComboWatch();
        Check($"ComboWatch: stopped and never landed is broken ({w1.Broken}, at step {w1.FizzledAt} of {w1.FizzledOf}); a payoff is not ({w2.Broken}); stopped, then landed later, is not ({w3.Broken}); never started is not ({w4.Broken})",
            w1.Broken && w1.FizzledAt == 2 && w1.FizzledOf == 4 && !w2.Broken && !w3.Broken && !w4.Broken);

        // ComboWatch with the duel's context (the review's fixes): the opponent's links count only while the route is live,
        // up to the end of its own turn; the duel ending with a route live that they acted against counts as their fizzle.
        {
            using var duel = new DuelRecipe
            {
                Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = 1, FirstPlayer = 0 },
                Deck0 = ComboGoldfish.Dummy(root), Deck1 = ComboGoldfish.Dummy(root),
            }.Create();
            byte[] Chaining(byte who)
            {
                var b = new List<byte> { Msg.Chaining };
                b.AddRange(BitConverter.GetBytes(83555667u)); // Ring of Destruction
                b.Add(who); b.Add((byte)Location.SZone); b.AddRange(BitConverter.GetBytes(0u)); b.AddRange(BitConverter.GetBytes(0u));
                b.Add(who); b.Add((byte)Location.SZone); b.AddRange(BitConverter.GetBytes(0u)); b.AddRange(BitConverter.GetBytes(0UL));
                return b.ToArray();
            }
            byte[] NewTurn(byte who) => new[] { Msg.NewTurn, who };
            byte[] Win(byte who) => new[] { Msg.Win, who, (byte)0 };
            ComboWatch Watch(out DuelContext c)
            {
                c = new DuelContext(duel, cards);
                return new ComboWatch(Playbooks.ForDeck(ComboGoldfish.LoadDeck(root, "frog-ftk")!)!, c, 0);
            }
            // A route stuck in its own turn, ended at the headliner's next decision on yours, after your link there: the draw's.
            var late = Watch(out var c1);
            late.Observe(Sig(Playbook.RouteEvent.Started));
            late.Observe(Sig(Playbook.RouteEvent.Step));
            c1.Observe(NewTurn(1));
            c1.Observe(Chaining(1));
            late.Observe(Sig(Playbook.RouteEvent.Ended));
            // Your link while it plays, then the duel ends (you win, or it concedes) before it decides again: yours.
            var won = Watch(out var c2);
            won.Observe(Sig(Playbook.RouteEvent.Started));
            c2.Observe(Chaining(1));
            c2.Observe(Win(1));
            bool wonAtEnd = won.Stopped && won.Fizzled == 1;
            won.Observe(Sig(Playbook.RouteEvent.Ended)); // (a late Ended isn't counted twice)
            // The same with the payoff landing first: not stopped. And no link of yours: the duel's end counts nothing.
            var paid = Watch(out var c3);
            paid.Observe(Sig(Playbook.RouteEvent.Started));
            c3.Observe(Chaining(1));
            paid.Observe(Sig(Playbook.RouteEvent.Payoff));
            c3.Observe(Win(1));
            var quiet = Watch(out var c4);
            quiet.Observe(Sig(Playbook.RouteEvent.Started));
            c4.Observe(Chaining(0));
            c4.Observe(Win(1));
            Check($"ComboWatch's window: your link on the next turn isn't against a route stuck in its own ({late.Fizzled} fizzle, stopped {late.Stopped}); a route you "
                  + $"interrupt still live as the duel ends is yours ({won.Fizzled} fizzle, stopped {won.Stopped}); not once it paid off ({paid.Stopped}), nor with no link of yours ({quiet.Fizzled} fizzles)",
                late.Fizzled == 1 && !late.Stopped && wonAtEnd && won.Fizzled == 1 && won.Stopped && won.StoppedFizzles == 1 && !paid.Stopped && paid.Fizzled == 0 && quiet.Fizzled == 0 && !quiet.Stopped);
        }

        // ---- 4. The ready headliners in a duel: their Watch the combo clip plays back the same, and the route's signals
        // (started, each step, the payoff at its index, the meter) come as the show reads them.
        var dummy = ComboGoldfish.Dummy(root);
        foreach (var h in ready)
        {
            var deck = ctx.LoadDeck(h.Deck)!;
            var clip = HeadlinerReplays.Load(Path.Combine(root, "campaign"), h.Id);
            bool clipOk = false;
            string clipNote = "no clip";
            if (clip != null)
            {
                using var duel = clip.Recipe(cards, scripts).Create();
                var feed = new ReplayFeed(clip.Responses);
                var runner = new DuelRunner(duel, new ReplayDuelist(feed), new ReplayDuelist(feed));
                YataLock.Attach(runner, duel);
                var r = runner.Run(40000);
                clipOk = r.Winner == clip.Winner && r.Turns == clip.Turns && clip.Winner == 0 && clip.Decks[0].Main.OrderBy(c => c).SequenceEqual(deck.Main.OrderBy(c => c))
                         && clip.Characters[0] == h.Id;
                clipNote = $"seed {clip.Settings.Seed}, won on duel turn {clip.Turns}";
            }
            // The clip's seed replayed with the bot: the route's signals as the show gets them.
            int g = clip != null ? (int)(clip.Settings.Seed % 7919) : 0, set = clip != null ? (int)(clip.Settings.Seed / 7919) : 1;
            var played = new List<Playbook.RouteSignal>();
            var book = Playbooks.ForDeck(deck)!;
            ComboWatch? watch = null;
            book.RouteSignaled += played.Add;
            using (var duel = new DuelRecipe
            {
                Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)(set * 7919 + g), FirstPlayer = g % 2 == 0 ? 0 : 1 },
                Deck0 = deck.Clone(), Deck1 = dummy.Clone(),
            }.Create())
            {
                var context = new DuelContext(duel, cards);
                watch = new ComboWatch(book, context, 0);
                var runner = new DuelRunner(duel, new SmartDuelist("Headliner", set * 7919 + g, context, playbook: book), new DummyDuelist(set * 7919 + g + 1, cards));
                runner.MessageReceived += context.Observe;
                YataLock.Attach(runner, duel);
                runner.Run(40000);
            }
            var payoff = played.FirstOrDefault(x => x.Kind == Playbook.RouteEvent.Payoff);
            var info = payoff != null ? book.Routes.FirstOrDefault(r => r.Name == payoff.Route) : null;
            // A route may hold more than one payoff step (Return Rook: Return from the Different Dimension, else Dimension Fusion);
            // its PayoffStep is the first, and a Payoff signal only ever comes from a payoff step, so one at or after it is one.
            bool signalsOk = played.Count > 0 && played[0].Kind == Playbook.RouteEvent.Started && payoff != null && info != null
                             && payoff.Step >= info.PayoffStep && payoff.Step < info.Steps.Count && info.Steps[payoff.Step] == payoff.StepName
                             && payoff.PayoffStep == info.PayoffStep && payoff.Counter == info.Counter && payoff.Counter.Length > 0
                             && payoff.Count >= ComboGoldfish.MinAtPayoff(book) // Return Rook: two or more monsters home
                             && watch != null && !watch.Broken && !watch.Stopped && watch.PaidOff > 0;
            Check($"{h.Handle}: the Watch the combo clip plays back the same ({clipNote}), and its duel's route signals start, pay off at step {(payoff?.Step ?? -1) + 1} of {payoff?.Steps} "
                  + $"(\"{payoff?.StepName}\", the route's payoff), and carry the meter ({payoff?.Counter} {played.LastOrDefault()?.Count}, {payoff?.Count} at the payoff, at least {ComboGoldfish.MinAtPayoff(book)}; {played.Count(x => x.Kind == Playbook.RouteEvent.Count)} Count signals)",
                clipOk && signalsOk);
        }

        // Every ready headliner's route names its payoff: a route without one would read "finished" off its last step, and
        // a stuck one could pass for the poster's fizzle.
        var noPayoff = ready.SelectMany(h => (ctx.LoadDeck(h.Deck) is Deck d && Playbooks.ForDeck(d) is Playbook b ? b.Routes : new List<Playbook.RouteInfo>())
            .Where(r => r.PayoffStep < 0).Select(r => $"{h.Id}: {r.Name}")).ToList();
        Check($"every route of the ready headliners ({ready.Count}) has a payoff step{(noPayoff.Count > 0 ? $" (none: {string.Join(", ", noPayoff)})" : "")}",
            noPayoff.Count == 0 && ready.Count > 0);

        // ---- 5. A negated payoff is a fizzle at its step, not a payoff: the stand-in holds one answer and plays it only on the
        // payoff (Solemn Warning on Gwen's Shi En, the summon; Ash Blossom on Marlo's Master Plan search, the activation).
        foreach (var (who, answer, where) in new[] { ("gateway-gwen", 84749824u, ComboInterrupt.Where.Set), ("master-plan-marlo", 14558127u, ComboInterrupt.Where.Hand) })
        {
            var h = data.FindHeadliner(who);
            var deck = h != null && h.Ready ? ctx.LoadDeck(h.Deck) : null;
            if (deck == null) { Check($"a negated payoff: {who} is ready", false); continue; }
            var placed = new[] { where == ComboInterrupt.Where.Hand ? new PlacedCard(1, answer, Location.Hand) : new PlacedCard(1, answer, Location.SZone, 0, Position.FaceDownDefense) };
            string note = "no duel reached the payoff with the answer up";
            bool ok = false;
            for (int g = 0; g < 40 && !ok; g++)
            {
                PayoffSniper? sniper = null;
                ComboWatch? watch = null;
                DuelRunner? running = null;
                var signals = new List<Playbook.RouteSignal>();
                bool? stoppedAtEnd = null;
                int paidAtEnd = -1;
                var game = ComboGoldfish.Play(cards, scripts, deck, dummy, g, 11, (seedOf, context) => sniper = new PayoffSniper(seedOf, context, answer, cards), placed,
                    attach: (runner, _) => running = runner,
                    watch: (book, context) =>
                    {
                        watch = new ComboWatch(book, context, 0);
                        book.RouteSignaled += signal =>
                        {
                            signals.Add(signal);
                            if (signal.Kind == Playbook.RouteEvent.Step && signal.Step == signal.PayoffStep && sniper != null && !sniper.Fired) sniper.Armed = true;
                            // The route after the answer: it ends unfinished; the headliner then gives up the duel (as Moss does in the app's check).
                            if (signal.Kind == Playbook.RouteEvent.Ended && sniper?.Fired == true && stoppedAtEnd == null)
                            {
                                stoppedAtEnd = !signal.Finished;
                                paidAtEnd = watch!.PaidOff;
                                running?.Concede(0, DuelRunner.ReasonConceded);
                            }
                        };
                    });
                if (sniper?.Fired != true) continue;
                int fired = signals.FindIndex(x => x.Kind == Playbook.RouteEvent.Step && x.Step == x.PayoffStep);
                var after = fired >= 0 ? signals.Skip(fired + 1).TakeWhile(x => x.Kind != Playbook.RouteEvent.Started).ToList() : new List<Playbook.RouteSignal>();
                ok = stoppedAtEnd == true && paidAtEnd == 0 && after.All(x => x.Kind != Playbook.RouteEvent.Payoff) && after.Any(x => x.Kind == Playbook.RouteEvent.Ended && !x.Finished)
                     && watch != null && watch.PaidOff == 0 && watch.Fizzled > 0 && watch.Stopped && game.Winner == 1 && !game.Finished;
                note = $"seed {game.Seed}: {Name(answer)} on \"{signals[Math.Max(0, fired)].StepName}\" (step {signals[Math.Max(0, fired)].Step + 1} of {signals[0].Steps}); "
                       + $"ComboWatch paid off {watch?.PaidOff}, fizzled {watch?.Fizzled} ({watch?.FizzledAt} steps played), stopped {watch?.Stopped}; the goldfish's payoff {(game.Finished ? "counted" : "not counted")}";
            }
            Check($"{h!.Handle}: a negated payoff is a fizzle at its step, not a payoff ({note})", ok);
        }

        Console.WriteLine($"  ({timer.Elapsed.TotalSeconds:0.0} s)");
        return failures;
    }

    /// <summary>
    /// The stand-in for the negated-payoff check: plays as the dummy (sets a monster, never attacks) and passes every chance
    /// to chain, except that once <see cref="Armed"/> (the headliner's payoff step is being played) it chains its one answer
    /// at the first chance; costs and targets as the skill-1.0 bot.
    /// </summary>
    private sealed class PayoffSniper : IDuelist
    {
        private readonly DummyDuelist _dummy;
        private readonly SmartDuelist _smart;
        private readonly uint _answer;
        private readonly ICardDataProvider _cards;
        public bool Armed, Fired;

        public PayoffSniper(int seed, DuelContext context, uint answer, ICardDataProvider cards)
        {
            _dummy = new DummyDuelist(seed, cards);
            _smart = new SmartDuelist("The stand-in", seed, context, personality: new Personality { Id = "alley-stand-in", Name = "The stand-in", Skill = 1.0 });
            _answer = answer;
            _cards = cards;
        }

        public byte[]? Respond(Prompt prompt, int attempt)
        {
            if (prompt is IdlePrompt or BattlePrompt) return _dummy.Respond(prompt, attempt);
            if (prompt is ChainPrompt chain && attempt == 0)
            {
                int at = chain.Chains.FindIndex(o => Ygo.Engine.DeckBuilding.Banlist.BaseCode(o.Card.Code, _cards) == _answer);
                if (Armed && !Fired && at >= 0)
                {
                    Fired = true;
                    Armed = false;
                    return Responses.Int32(at);
                }
                if (!chain.Forced) return Responses.NoChain();
            }
            return _smart.Respond(prompt, attempt);
        }

        public void OnPhaseChanged()
        {
            _dummy.OnPhaseChanged();
            _smart.OnPhaseChanged();
        }
    }
}
