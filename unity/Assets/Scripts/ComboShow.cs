using System.Collections.Concurrent;
using System.Collections.Generic;
using Ygo.Engine;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Presentation;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The headliners' show, as data (docs/dev/HEADLINERS-2026-11-01.md, "The show"): one duel's headliner, the route it's
    /// playing, the step it's on, the running total the meter counts, and how it went (paid off, finished, fizzled). Fed by
    /// the headliner's <see cref="Playbook.RouteSignaled"/> (queued: they arrive while the engine runs) and by the duel's
    /// events; <see cref="Take"/> turns both into the moments the duel controller stages (the banner, the payoff beat, the
    /// fizzle). A route still playing when its turn ends is over: paid off, it finished; otherwise it fizzled where it
    /// stopped. Only the presentation reads it: the duel plays the same with or without it.
    /// </summary>
    public sealed class ComboShow
    {
        public enum Phase { None, Playing, PaidOff, Finished, Fizzled }
        public enum Moment { Started, Step, Payoff, Finished, Fizzled }

        /// <summary>
        /// The seat whose route is (or was last) on, -1 before any; its headliner's id, handle and short name ("Moss"). An
        /// exhibition can seat two headliners: each seat's routes are watched, one at a time (a route plays on its own turn).
        /// </summary>
        public int Seat { get; private set; } = -1;
        public string Id { get; private set; } = "";
        public string Handle { get; private set; } = "";
        public string Short { get; private set; } = "";
        private readonly string[] _ids = { "", "" }, _handles = { "", "" };

        public string Route { get; private set; } = "";
        /// <summary>The step just played (1-based, as the meter says it), of how many; the turn the route started on.</summary>
        public int Step { get; private set; }
        public int Steps { get; private set; }
        public int RouteTurn { get; private set; }
        public Phase State { get; private set; }
        /// <summary>Fizzled: the step it stopped at (the one after the last it played).</summary>
        public int FizzledAt { get; private set; }
        /// <summary>
        /// What the meter counts for this route and the running total: the route's own counter when its playbook names one
        /// (<see cref="Playbook.Route.Counts"/>: the signals carry it), else counted here from the duel's events ("damage",
        /// "cards" sent to the GY, "summons").
        /// </summary>
        public string Counts { get; private set; } = "summons";
        /// <summary>The route names its own counter (the total comes with its signals).</summary>
        public bool RouteCounts { get; private set; }
        public int Total { get; private set; }
        /// <summary>This duel: routes started, payoffs reached, routes that fizzled before their payoff.</summary>
        public int Starts { get; private set; }
        public int Payoffs { get; private set; }
        public int Fizzles { get; private set; }
        /// <summary>The payoff step's name, for the payoff strip ("Mass Driver's first shot with the loop assembled").</summary>
        public string PayoffName { get; private set; } = "";

        /// <summary>A route is on: started, not yet finished or fizzled.</summary>
        public bool Active => State is Phase.Playing or Phase.PaidOff;

        /// <summary>
        /// The combo's turn is still on: from its start until the turn moves on (or it fizzles). A route can finish at its
        /// payoff while the deck's rules play the loop on (Frog FTK's shots after the first): the meter keeps counting and
        /// the slow-down holds until the turn ends.
        /// </summary>
        public bool Live { get; private set; }

        /// <summary>What the duel's events add to the total ("damage", "cards", "banished", "summons"; "" for nothing).</summary>
        private string _eventKind = "";
        /// <summary>The opponent's LP when the route started (-1: unknown), for a damage meter.</summary>
        private int _lpAtStart = -1;

        /// <summary>Whether this duel seats a headliner whose routes are watched.</summary>
        public bool On => _ids[0].Length > 0 || _ids[1].Length > 0;

        /// <summary>Whether a seat is a headliner's.</summary>
        public bool IsHeadliner(int seat) => seat is 0 or 1 && _ids[seat].Length > 0;

        private readonly ConcurrentQueue<(int seat, Playbook.RouteSignal signal)> _signals = new ConcurrentQueue<(int, Playbook.RouteSignal)>();

        /// <summary>A new duel: each seat's headliner id and handle ("" for a seat that isn't one).</summary>
        public void Reset(string id0, string handle0, string id1, string handle1)
        {
            _ids[0] = id0;
            _ids[1] = id1;
            _handles[0] = handle0;
            _handles[1] = handle1;
            Seat = -1;
            Id = Handle = Short = "";
            Route = PayoffName = "";
            Step = Steps = RouteTurn = FizzledAt = Total = Starts = Payoffs = Fizzles = 0;
            State = Phase.None;
            Live = false;
            while (_signals.TryDequeue(out _)) { }
        }

        /// <summary>A handle's short name, the way Baz calls it: its last word ("Mass Driver Moss" is Moss).</summary>
        public static string ShortName(string handle)
        {
            var parts = handle.Trim().Split(' ');
            return parts.Length == 0 ? handle : parts[parts.Length - 1];
        }

        /// <summary>The playbook's signal (from the engine's thread or the main one): staged at the next <see cref="Take"/>.</summary>
        public void Enqueue(int seat, Playbook.RouteSignal signal) { if (IsHeadliner(seat)) _signals.Enqueue((seat, signal)); }

        /// <summary>What the meter counts for a route that doesn't name its own counter.</summary>
        public static string CounterFor(string route) => route switch
        {
            "Frog FTK" => "damage",
            "Yata-lock" or "Chaos Emperor wipe" => "cards",
            _ => "summons",
        };

        /// <summary>The meter's total in words: "4,800 damage", "9 cards to the GY", "5 summons".</summary>
        public string TotalText => RouteCounts ? $"{Total:N0} {Counts}" : Counts switch
        {
            "damage" => $"{Total:N0} damage",
            "cards" => $"{Total} card{(Total == 1 ? "" : "s")} to the GY",
            _ => $"{Total} summon{(Total == 1 ? "" : "s")}",
        };

        /// <summary>
        /// The signals since the last call, then the duel's events (the running total; a new turn ends a route still on).
        /// Returns the moments to stage, in order. <paramref name="boardTurn"/> is the duel's turn after these events.
        /// With <paramref name="holdAtPayoff"/>, a payoff is the last moment taken: the signals after it (the route's end,
        /// which can come in the same engine step now that the payoff is staged as it resolves) wait for the next call, so
        /// the payoff beat shows "PAYOFF" while it holds.
        /// </summary>
        public List<Moment> Take(IReadOnlyList<DuelEvent> events, int boardTurn, int[]? lifePoints = null, bool holdAtPayoff = true)
        {
            var moments = new List<Moment>();
            if (!On) return moments;
            // Damage is read off the opponent's LP since the route started (exact even when the duel ends mid-loop, which
            // sends no last signal); other counters come with the signals, then from the events once those stop.
            int Lp() => lifePoints != null && Seat is 0 or 1 ? lifePoints[1 - Seat] : -1;
            bool held = false;
            while (!held && _signals.TryDequeue(out var item))
            {
                var (seat, s) = item;
                // Another seat's signal while a route is on (its Ended, late): not this route's.
                if (s.Kind != Playbook.RouteEvent.Started && seat != Seat) continue;
                switch (s.Kind)
                {
                    case Playbook.RouteEvent.Started:
                        if (Active) moments.Add(End(State == Phase.PaidOff));
                        Seat = seat;
                        Id = _ids[seat];
                        Handle = _handles[seat];
                        Short = ShortName(Handle);
                        Route = s.Route;
                        Steps = s.Steps;
                        Step = 0;
                        RouteTurn = s.Turn;
                        RouteCounts = s.Counter.Length > 0;
                        Counts = RouteCounts ? s.Counter : CounterFor(s.Route);
                        Total = RouteCounts ? s.Count : 0;
                        _eventKind = EventKind(Counts);
                        _lpAtStart = lifePoints != null ? lifePoints[1 - seat] : -1;
                        PayoffName = "";
                        State = Phase.Playing;
                        Live = true;
                        Starts++;
                        moments.Add(Moment.Started);
                        break;
                    case Playbook.RouteEvent.Count when Active && s.Route == Route && RouteCounts:
                        Total = s.Count;
                        break;
                    case Playbook.RouteEvent.Step when Active && s.Route == Route:
                        Step = s.Step + 1;
                        if (RouteCounts) Total = s.Count;
                        moments.Add(Moment.Step);
                        break;
                    case Playbook.RouteEvent.Payoff when Active && s.Route == Route:
                        PayoffName = s.StepName;
                        if (RouteCounts) Total = s.Count;
                        if (State != Phase.PaidOff) Payoffs++;
                        State = Phase.PaidOff;
                        moments.Add(Moment.Payoff);
                        held = holdAtPayoff;
                        break;
                    case Playbook.RouteEvent.Ended when Active && s.Route == Route:
                        if (RouteCounts) Total = s.Count;
                        moments.Add(End(s.Finished || State == Phase.PaidOff));
                        break;
                }
            }
            // The events count while the combo's turn is on (a route that names its counter: once its signals stop).
            foreach (var e in events)
            {
                if (!Live || (RouteCounts && Active) || _eventKind.Length == 0 || (_eventKind == "damage" && _lpAtStart >= 0)) break;
                switch (e.Kind)
                {
                    case DuelEventKind.Damage when _eventKind == "damage" && e.Player == 1 - Seat:
                        Total += e.Amount;
                        break;
                    case DuelEventKind.Move when _eventKind == "cards" && e.To.Location == Location.Grave
                                                 && (e.From.Location & (Location.Hand | Location.MZone | Location.SZone)) != 0:
                        Total++;
                        break;
                    case DuelEventKind.Move when _eventKind == "banished" && e.To.Location == Location.Removed && e.From.Location != Location.Removed:
                        Total++;
                        break;
                    case DuelEventKind.SpecialSummon or DuelEventKind.NormalSummon when _eventKind == "summons" && e.Player == Seat:
                        Total++;
                        break;
                }
            }
            // The turn moved on with the route still playing: it's over (its own end comes at the playbook's next decision).
            if (Live && _eventKind == "damage" && _lpAtStart >= 0 && Lp() >= 0) Total = System.Math.Max(Total, _lpAtStart - Lp());
            if (!held && Live && boardTurn > RouteTurn)
            {
                if (Active) moments.Add(End(State == Phase.PaidOff));
                Live = false;
            }
            return moments;
        }

        /// <summary>What a counter's name counts in the duel's events (a route's own names: "damage", "cards banished"...).</summary>
        private static string EventKind(string counter)
        {
            var c = counter.ToLowerInvariant();
            return c.Contains("damage") ? "damage" : c.Contains("banish") ? "banished" : c.Contains("summon") ? "summons"
                : c.Contains("card") || c.Contains("gy") || c.Contains("grave") ? "cards" : "";
        }

        private Moment End(bool finished)
        {
            if (finished)
            {
                State = Phase.Finished;
                return Moment.Finished;
            }
            State = Phase.Fizzled;
            Live = false;
            FizzledAt = Steps > 0 ? System.Math.Min(Steps, Step + 1) : Step + 1;
            Fizzles++;
            return Moment.Fizzled;
        }

        /// <summary>The duel ended: a route still playing finished if it had paid off (else it simply stops with the duel).</summary>
        public void DuelOver()
        {
            if (State == Phase.PaidOff) State = Phase.Finished;
            Live = false;
        }
    }
}
