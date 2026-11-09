using Ygo.Engine.Protocol;

namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// A headliner's routes over one duel (docs/dev/HEADLINERS-2026-11-01.md, phase 3), from <see cref="Playbook.RouteSignaled"/>:
    /// how many started, reached their payoff (it resolved: a negated payoff is a fizzle at its step), and fizzled (ended
    /// stuck). <see cref="Broken"/> is the Combo Breaker's test: a route was stopped before its payoff and none ever reached
    /// it, so pass it to <c>BackAlley.Record(comboBroken:)</c> when you win. Attach it before the duel starts; it never
    /// changes how the playbook plays.
    /// </summary>
    public sealed class ComboWatch
    {
        public ComboWatch() { }

        public ComboWatch(Playbook playbook) => playbook.RouteSignaled += Observe;

        /// <summary>
        /// Also watches what the opponent of <paramref name="seat"/> does: a fizzle with an opponent's chain link while the
        /// route was live (from its start to its end, and no later than the end of the turn it started on) is one they
        /// caused (<see cref="Stopped"/>), not the draw's. The duel ending with a route live that they stopped counts too
        /// (you interrupt it and win, or it concedes, before the playbook decides again).
        /// </summary>
        public ComboWatch(Playbook playbook, DuelContext context, int seat) : this(playbook)
        {
            _context = context;
            _seat = seat;
            context.Observed += Hear;
        }

        private readonly DuelContext? _context;
        private readonly int _seat;
        /// <summary>The opponent's chain links when the live route started, and when its turn ended (-1: not yet).</summary>
        private int _theirLinks = -1, _theirLinksAtTurnEnd = -1;
        /// <summary>A route is live (started, not ended), and whether it paid off; the step it last played, of how many.</summary>
        private bool _live, _livePaid;
        private string _liveRoute = "";
        private int _liveStep, _liveSteps;

        /// <summary>Fizzles during which the opponent activated something (only with a context).</summary>
        public int StoppedFizzles;

        /// <summary>
        /// Like <see cref="Broken"/>, and the opponent acted during a fizzled route: they stopped it, the draw didn't (SPYRAL's
        /// guesses and the Gouki's searches fail on their own). With no context it's <see cref="Broken"/>.
        /// </summary>
        public bool Stopped => _context == null ? Broken : StoppedFizzles > 0 && PaidOff == 0;

        /// <summary>Routes started, paid off (their payoff step resolved) and fizzled (ended before the payoff), this duel.</summary>
        public int Started, PaidOff, Fizzled;

        /// <summary>The first route that started (empty: none), and where the last fizzle stopped: the steps it had played, of how many.</summary>
        public string FirstRoute = "", FizzledRoute = "";
        public int FizzledAt, FizzledOf;

        /// <summary>The headliner's combo was stopped before its payoff and never landed: what earns its Combo Breaker poster with a win.</summary>
        public bool Broken => Fizzled > 0 && PaidOff == 0;

        /// <summary>The opponent's chain links so far (0 without a context).</summary>
        private int TheirLinks => _context?.ChainLinksTotal[(_seat + 1) & 1] ?? 0;

        /// <summary>Whether the opponent made a chain link while the live route was on (up to its turn's end).</summary>
        private bool TheyActed => _context != null && _theirLinks >= 0 && (_theirLinksAtTurnEnd >= 0 ? _theirLinksAtTurnEnd : TheirLinks) > _theirLinks;

        public void Observe(Playbook.RouteSignal signal)
        {
            switch (signal.Kind)
            {
                case Playbook.RouteEvent.Started:
                    Started++;
                    _live = true;
                    _livePaid = false;
                    _liveRoute = signal.Route;
                    _liveStep = 0;
                    _liveSteps = signal.Steps;
                    _theirLinks = _context != null ? TheirLinks : -1;
                    _theirLinksAtTurnEnd = -1;
                    if (FirstRoute.Length == 0) FirstRoute = signal.Route;
                    break;
                case Playbook.RouteEvent.Step:
                    _liveStep = signal.Step + 1;
                    break;
                case Playbook.RouteEvent.Payoff:
                    PaidOff++;
                    _livePaid = true;
                    break;
                case Playbook.RouteEvent.Ended:
                    // (Not twice: a route the duel's end already counted.)
                    if (_live && !signal.Finished) Fizzle(signal.Route, signal.Step, signal.Steps, TheyActed);
                    _live = false;
                    break;
            }
        }

        private void Fizzle(string route, int at, int of, bool theyActed)
        {
            Fizzled++;
            if (theyActed) StoppedFizzles++;
            FizzledRoute = route;
            FizzledAt = at;
            FizzledOf = of;
        }

        /// <summary>
        /// The duel is over (a win message, a concession or the turn cap): a route still live, unpaid, that the opponent acted
        /// against is a fizzle they caused, though the playbook never got to end it. Called on its own as the duel's last
        /// message passes (with a context); idempotent.
        /// </summary>
        public void DuelOver()
        {
            if (_live && !_livePaid && TheyActed) Fizzle(_liveRoute, _liveStep, _liveSteps, true);
            _live = false;
        }

        private void Hear(byte[] m)
        {
            if (m.Length == 0) return;
            // The route's window closes with its turn: the opponent's links on the next turn aren't against it.
            if (m[0] == Msg.NewTurn && _live && _theirLinksAtTurnEnd < 0) _theirLinksAtTurnEnd = TheirLinks;
            else if (m[0] == Msg.Win) DuelOver();
        }
    }
}
