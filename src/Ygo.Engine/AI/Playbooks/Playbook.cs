using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Deck knowledge layered on top of <see cref="SmartDuelist"/>, in the spirit of WindBot's executors:
    /// <list type="bullet">
    /// <item>Rules: "when this holds, do this action with this card", checked in order each Main Phase decision.</item>
    /// <item>Preferences: which cards to pick for a selection (by hint and by the card whose effect is asking).</item>
    /// <item>Vetoes: stop the generic bot from spending combo pieces badly.</item>
    /// </list>
    /// Anything a playbook does not cover falls through to the generic bot, so playbooks can stay small.
    /// </summary>
    public abstract class Playbook
    {
        internal enum Kind { Activate, NormalSummon, SpecialSummon, SetSpellTrap }

        private sealed class Rule
        {
            public string Name = "";
            public Kind Kind;
            public uint Code;
            /// <summary>For Activate: which of the card's effects (its script's Stringid index), or -1 for any.</summary>
            public int Effect = -1;
            public Func<bool>? When;
            public int PerTurn;
            public int FiredThisTurn;
        }

        internal sealed class Preference
        {
            public HashSet<ulong> Hints = new HashSet<ulong>();
            public uint Source;
            public uint[] Codes = Array.Empty<uint>();
            public double Weight;
            public Func<bool>? When;
        }

        private readonly List<Rule> _rules = new List<Rule>();
        private readonly List<Preference> _preferences = new List<Preference>();

        // ------------------------------------------------------------ Routes

        /// <summary>
        /// A combo route: the order a real player plays a starter in (their combo notes), with the picks each step wants
        /// (the search, the materials, the cost). It starts when its condition holds at the first Main Phase decision of
        /// your turn; then each decision takes the next step the duel offers (steps it can't take are passed over, so a
        /// route picks up where it is after an interruption) until none is left, and the usual rules take over.
        /// </summary>
        protected sealed class Route
        {
            public string Name = "";
            public Func<bool> Start = () => true;
            /// <summary>The route may start at any decision of the turn (a draw spell finds the piece), not only the first.</summary>
            public bool Late;
            internal readonly List<Step> Steps = new List<Step>();

            /// <summary>The route waits out decisions where none of its steps is on offer (the deck's rules play them), and
            /// ends only with the turn: a loop deck's route marks milestones while its rules do the work.</summary>
            public bool Waits;

            public Route Waiting()
            {
                Waits = true;
                return this;
            }

            /// <summary>Let the route start later in the turn, once its condition holds.</summary>
            public Route AnyDecision()
            {
                Late = true;
                return this;
            }

            internal Route Add(Step step) { Steps.Add(step); return this; }

            public Route Activate(string name, uint code, Func<bool>? when = null, int effect = -1) =>
                Add(new Step { Name = name, Kind = Kind.Activate, Code = code, Effect = effect, When = when });
            /// <summary>One effect of a card with several (its script's Stringid index), like the playbook's ActivateEffect.</summary>
            public Route ActivateEffect(string name, uint code, int effect, Func<bool>? when = null) => Activate(name, code, when, effect);
            public Route NormalSummon(string name, uint code, Func<bool>? when = null) =>
                Add(new Step { Name = name, Kind = Kind.NormalSummon, Code = code, When = when });
            public Route SpecialSummon(string name, uint code, Func<bool>? when = null) =>
                Add(new Step { Name = name, Kind = Kind.SpecialSummon, Code = code, When = when });
            public Route Set(string name, uint code, Func<bool>? when = null) =>
                Add(new Step { Name = name, Kind = Kind.SetSpellTrap, Code = code, When = when });

            /// <summary>What the last step picks when the duel asks (a selection with one of <paramref name="hints"/>), in order.</summary>
            public Route Pick(ulong[] hints, params uint[] codes)
            {
                Steps[Steps.Count - 1].Picks.Add(new Preference { Hints = new HashSet<ulong>(hints), Codes = codes, Weight = 3 });
                return this;
            }

            /// <summary>What the combo meter counts (<see cref="Counts"/>): its name ("damage", "cards banished"); empty for none.</summary>
            public string Counter = "";
            internal Func<int>? CountOf;

            /// <summary>
            /// The combo meter's running total: <paramref name="value"/> is read at each signal, and the meter shows how far it
            /// has moved since the route started (damage dealt, cards banished, monsters summoned).
            /// </summary>
            public Route Counts(string name, Func<int> value)
            {
                Counter = name;
                CountOf = value;
                return this;
            }

            /// <summary>The payoff step's index (0-based), or -1 when the route has none (its last step is then what "finished" means).</summary>
            public int PayoffStep => Steps.FindIndex(st => st.IsPayoff);

            /// <summary>The last step is the route's payoff: the moment the show holds on, and what "finished" means.</summary>
            public Route Payoff()
            {
                Steps[Steps.Count - 1].IsPayoff = true;
                return this;
            }

            /// <summary>What the last step picks last (a cost it shouldn't pay with these).</summary>
            public Route Keep(ulong[] hints, params uint[] codes)
            {
                Steps[Steps.Count - 1].Picks.Add(new Preference { Hints = new HashSet<ulong>(hints), Codes = codes, Weight = -3 });
                return this;
            }
        }

        internal sealed class Step
        {
            public string Name = "";
            public Kind Kind;
            public uint Code;
            public int Effect = -1;
            public Func<bool>? When;
            public bool IsPayoff;
            public readonly List<Preference> Picks = new List<Preference>();
        }

        // ------------------------------------------------------------ Route signals (for the show and --combo-goldfish)

        /// <summary>Count (added in Headliners phase 3) is raised when the route's meter moves between steps (a loop its rules play).</summary>
        public enum RouteEvent { Started, Step, Payoff, Ended, Count }

        /// <summary>A route's progress: started (at its first step played), each step, its payoff, and its end.</summary>
        public sealed class RouteSignal
        {
            public RouteEvent Kind;
            public string Route = "";
            public int Turn;
            /// <summary>The step just played (0-based) and its name; for Ended, the steps played.</summary>
            public int Step;
            public string StepName = "";
            public int Steps;
            /// <summary>Ended: the payoff was played (or, for a route without one, its last step).</summary>
            public bool Finished;
            /// <summary>The payoff step's index (0-based; -1: the route has none, and its last step finishes it).</summary>
            public int PayoffStep = -1;
            /// <summary>What the combo meter counts ("damage", "cards banished"...; empty: the route has no meter) and its running total since the route started.</summary>
            public string Counter = "";
            public int Count;
        }

        /// <summary>Raised as a route plays. Steps the duel never offers are passed over, so Step can skip numbers.</summary>
        public event Action<RouteSignal>? RouteSignaled;

        private bool _routeStarted, _routePaidOff;
        private int _routePlayed, _routeLastStep;
        /// <summary>The meter's reading when the route started, and the total last signaled.</summary>
        private int _countBase, _countShown;

        private int RouteCount => _route?.CountOf == null ? 0 : _route.CountOf() - _countBase;

        private void Signal(RouteEvent kind, int step = -1, string stepName = "")
        {
            if (_route == null) return;
            _countShown = RouteCount;
            RouteSignaled?.Invoke(new RouteSignal
            {
                Kind = kind, Route = _route.Name, Turn = V.Turn, Step = step < 0 ? _routePlayed : step, StepName = stepName,
                Steps = _route.Steps.Count, Finished = _routePaidOff, PayoffStep = _route.PayoffStep, Counter = _route.Counter, Count = _countShown,
            });
        }

        /// <summary>The route's meter moved since its last signal (the deck's rules played the loop): a Count signal.</summary>
        private void SignalCount()
        {
            if (_route?.CountOf != null && _routeStarted && RouteSignaled != null && RouteCount != _countShown) Signal(RouteEvent.Count, _routeLastStep);
        }

        /// <summary>The route playing ends (it ran out of steps it could take, or the turn moved on).</summary>
        private void EndRoute()
        {
            _payoff = null; // a payoff still waiting on its resolution never landed
            if (_route != null && _routeStarted) Signal(RouteEvent.Ended);
            _route = null;
            _routeStarted = _routePaidOff = false;
            _routePlayed = 0;
            _countBase = _countShown = 0;
        }

        // ---- The payoff, confirmed as it resolves (Headliners phase 3, the review): a payoff step chosen is only played once
        // its chain link resolves un-negated (an activation), or its summon goes through (a Normal or Special Summon). A
        // negated payoff (Ash Blossom on the search, Solemn on the Synchro Summon, the Continuous Spell destroyed in chain)
        // never signals Payoff: the route ends at its payoff step, unfinished, as a fizzle. Only the signals change.

        /// <summary>The payoff step played and waiting on its resolution: its chain link (0: not seen yet) or its summon.</summary>
        private sealed class PendingPayoff
        {
            public Step Step = null!;
            public int Index;
            public int Link;
            public bool Negated, Resolving, Summoning;
            /// <summary>Where the card is: a Continuous (Field, Equip...) Spell/Trap whose effect is skipped if it leaves before it resolves, or the summoned monster.</summary>
            public bool WatchPlace;
            public byte Controller;
            public uint Location, Sequence;
        }

        private PendingPayoff? _payoff;
        /// <summary>Chain links on the chain building or resolving now (the core numbers them 1..n), and whether one is open.</summary>
        private int _chainLinks;
        private bool _chainSolving;
        /// <summary>The context whose messages this playbook hears (its view's).</summary>
        private DuelContext? _heard;

        private const byte MsgSummoned = 61, MsgSpSummoned = 63; // MSG_SUMMONED, MSG_SPSUMMONED: a summon went through

        /// <summary>The payoff step was played: it counts once it resolves (a Set counts at once: nothing can stop it).</summary>
        private void AwaitPayoff(Step step, int index)
        {
            _payoff = new PendingPayoff { Step = step, Index = index };
            if (step.Kind == Kind.SetSpellTrap) PayoffLanded();
        }

        private void PayoffLanded()
        {
            var p = _payoff;
            _payoff = null;
            if (p == null || _route == null) return;
            _routePaidOff = true;
            if (p.Step.IsPayoff) Signal(RouteEvent.Payoff, p.Index, p.Step.Name);
        }

        /// <summary>Each message of the duel (after the context took it in): the pending payoff's chain link or summon, followed.</summary>
        private void Hear(byte[] m)
        {
            if (m.Length == 0) return;
            // Numbered as the core does, and as the context keeps its chain (a link after the last resolved starts a new chain).
            if (m[0] == Msg.Chaining) { if (_chainSolving) { _chainLinks = 0; _chainSolving = false; } _chainLinks++; }
            else if (m[0] == Msg.ChainSolving) _chainSolving = true;
            else if (m[0] == Msg.ChainEnd) { _chainLinks = 0; _chainSolving = false; }
            var p = _payoff;
            if (p == null || V == null) return;
            var r = new MessageReader(m, 1);
            switch (m[0])
            {
                case Msg.Chaining when p.Step.Kind == Kind.Activate && p.Link == 0:
                {
                    var code = r.U32();
                    var at = r.LocInfo(code);
                    var controller = r.U8();
                    if ((controller & 1) != V.Me || !Same(code, p.Step.Code)) break;
                    p.Link = _chainLinks;
                    var type = V.TypeOf(code);
                    // A Continuous, Field or Equip Spell/Trap's effect doesn't resolve if the card leaves first (the core's CHAIN_CONTINUOUS_CARD).
                    p.WatchPlace = (at.Location & Location.SZone) != 0 && (type & (CardType.Spell | CardType.Trap)) != 0
                                   && (type & (TypeContinuous | TypeField | TypeEquip | CardType.Pendulum | CardType.Link)) != 0;
                    p.Controller = at.Controller;
                    p.Location = at.Location;
                    p.Sequence = at.Sequence;
                    break;
                }
                case Msg.ChainNegated or Msg.ChainDisabled when p.Link > 0:
                    if (r.U8() == p.Link) p.Negated = true;
                    break;
                case Msg.ChainSolving when p.Link > 0:
                    if (r.U8() == p.Link) p.Resolving = true;
                    break;
                case Msg.ChainSolved when p.Link > 0:
                    if (r.U8() != p.Link) break;
                    if (p.Negated) _payoff = null;
                    else PayoffLanded();
                    break;
                case Msg.Summoning or Msg.SpSummoning when !p.Summoning && (p.Step.Kind == Kind.NormalSummon && m[0] == Msg.Summoning
                                                                              || p.Step.Kind == Kind.SpecialSummon && m[0] == Msg.SpSummoning):
                {
                    var at = r.LocInfo(r.U32());
                    if ((at.Controller & 1) != V.Me || !Same(at.Code, p.Step.Code)) break;
                    p.Summoning = p.WatchPlace = true;
                    p.Controller = at.Controller;
                    p.Location = at.Location;
                    p.Sequence = at.Sequence;
                    break;
                }
                // The summon went through (outside a chain: a summon inside the negation window's chain is another card's).
                case MsgSummoned or MsgSpSummoned when p.Summoning && _chainLinks == 0:
                    if (p.Negated) _payoff = null;
                    else PayoffLanded();
                    break;
                case Msg.Move when p.WatchPlace && !p.Resolving:
                {
                    var code = r.U32();
                    var from = r.LocInfo(code);
                    // The card left its place first: the summon was negated (sent to the GY), or the Spell/Trap's effect won't resolve.
                    if (from.Controller == p.Controller && from.Location == p.Location && from.Sequence == p.Sequence) p.Negated = true;
                    break;
                }
                // The payoff won the duel as it resolved (Chaos Emperor's damage): it landed.
                case Msg.Win when p.Resolving && !p.Negated:
                    PayoffLanded();
                    break;
            }
        }

        private const uint TypeContinuous = 0x20000, TypeField = 0x80000, TypeEquip = 0x40000;

        private readonly List<Route> _routes = new List<Route>();
        private Route? _route;
        private bool _routeChosen;
        /// <summary>A route was taken this turn (a late route starts at most once a turn).</summary>
        private bool _routeTaken;
        private int _routeStep;
        /// <summary>The route step being played: its picks lead every selection until the next Main Phase decision.</summary>
        private Step? _currentStep;

        /// <summary>A route as the show lists it before it plays: its name, its steps' names, the payoff step (-1: none) and what its meter counts.</summary>
        public sealed class RouteInfo
        {
            public string Name = "", Counter = "";
            public IReadOnlyList<string> Steps = Array.Empty<string>();
            public int PayoffStep = -1;
        }

        /// <summary>This playbook's combo routes, in the order they're tried (empty for a deck without one).</summary>
        public IReadOnlyList<RouteInfo> Routes => _routes.Select(r => new RouteInfo
        {
            Name = r.Name, Counter = r.Counter, Steps = r.Steps.Select(st => st.Name).ToList(), PayoffStep = r.PayoffStep,
        }).ToList();

        /// <summary>A combo route for when <paramref name="start"/> holds (a starter in hand); chain its steps on the result.</summary>
        protected Route ComboRoute(string name, Func<bool> start)
        {
            var route = new Route { Name = name, Start = start };
            _routes.Add(route);
            return route;
        }

        /// <summary>Off: playbooks play without their combo routes (for measuring what the routes are worth).</summary>
        public static bool RoutesEnabled = true;

        /// <summary>
        /// On: a route step is the usual move, and a skilled character's planner may still override it (and the step's
        /// picks) when its line is clearly better. Off: the route plays as written.
        /// </summary>
        public static bool PlannerOverridesRoutes = false;

        /// <summary>The route playing this turn, if any (for logs and checks).</summary>
        public string? ActiveRoute => _route?.Name;

        /// <summary>The next route step on offer: the route this turn (chosen at its first decision), and the step.</summary>
        private (Step step, int index, byte[] response)? NextStep(IdlePrompt p)
        {
            if (!RoutesEnabled) return null;
            if (V.IsMyTurn && (!_routeChosen || (_route == null && !_routeTaken)))
            {
                var candidates = _routeChosen ? _routes.Where(r => r.Late) : _routes;
                _routeChosen = true;
                _route = candidates.FirstOrDefault(r => r.Start());
                _routeTaken = _route != null;
                _routeStep = 0;
                if (Environment.GetEnvironmentVariable("YGO_DEBUG_ROUTES") is "1" or "2")
                    Console.Error.WriteLine($"      [route] turn {V.Turn}: {(_route?.Name ?? "no route")} (my turn {V.IsMyTurn}, hand {V.HandCount}, white dragon {V.InHand(73819701)})");
            }
            if (_route == null) return null;
            if (Environment.GetEnvironmentVariable("YGO_DEBUG_ROUTES") is "1" or "2")
                Console.Error.WriteLine($"      [route] {_route.Name} at step {_routeStep}: activatable {string.Join(", ", p.Activatable.Select(o => o.Card.Code))}; summonable {string.Join(", ", p.Summonable.Select(c => c.Code))}; spsummonable {string.Join(", ", p.SpSummonable.Select(c => c.Code))}");
            for (int i = _routeStep; i < _route.Steps.Count; i++)
            {
                var step = _route.Steps[i];
                var response = Find(p, step.Kind, step.Code, step.Effect);
                if (response == null || (step.When != null && !step.When())) continue;
                return (step, i, response);
            }
            return null;
        }

        /// <summary>Whether a route step would be played at this Main Phase decision (the planner leaves it alone).</summary>
        internal bool RouteStepReady(IdlePrompt p) => _routes.Count > 0 && NextStep(p) != null;

        /// <summary>A route step is ready and plays as written (the planner leaves it alone).</summary>
        internal bool RouteStepLocked(IdlePrompt p) => !PlannerOverridesRoutes && RouteStepReady(p);

        /// <summary>
        /// On (<see cref="LeadWithRules"/>): the deck's rules are its plan, so the planner stands aside while one applies
        /// and only fills in around them (decks whose engine the planner's one-step look misjudges: a free summon from the
        /// hand looks worse to it than a big Tribute Summon).
        /// </summary>
        private bool _rulesLead;

        /// <summary>The planner stands aside while one of this deck's rules (or a route step) applies.</summary>
        protected void LeadWithRules() => _rulesLead = true;

        /// <summary>A rule of a deck that leads with its rules is ready at this decision (the planner leaves it alone).</summary>
        internal bool RuleLocked(IdlePrompt p) => _rulesLead && Idle(p, commit: false, out _) != null;

        /// <summary>A route step is being played: its picks lead the selections it brings.</summary>
        internal bool InRouteStep => _currentStep != null && !PlannerOverridesRoutes;

        /// <summary>
        /// The deck would rather go second when it wins the coin toss (it works off the opponent's cards, like
        /// Mekk-Knights); most decks go first, as in the TCG.
        /// </summary>
        public bool GoesSecond => !Playbooks.GoesFirst(Deck);
        private readonly List<(Kind kind, uint code, int effect, Func<bool>? when)> _vetoes = new List<(Kind, uint, int, Func<bool>?)>();
        private int _turn = -1;

        /// <summary>Your own side of the duel, for rule conditions. Valid while the playbook is being asked.</summary>
        protected PlaybookView V { get; private set; } = null!;

        /// <summary>
        /// Whether a card the duel names is this playbook card, by base code on both sides (card versions F8): an original
        /// or GOAT text (an alias id) still matches its playbook entry, and an entry written with a version id still sees
        /// the current card. Off (UseBaseCodes false) only for the A/B that measures it.
        /// </summary>
        protected bool Same(uint card, uint entry) => card == entry || UseBaseCodes && V != null && V.Base(card) == V.Base(entry);

        /// <summary>The index of a card among these entries (see <see cref="Same"/>), or -1.</summary>
        protected int IndexOf(uint[] entries, uint card)
        {
            int at = Array.IndexOf(entries, card);
            if (at >= 0 || !UseBaseCodes || V == null) return at;
            for (int i = 0; i < entries.Length; i++) if (Same(card, entries[i])) return i;
            return -1;
        }

        /// <summary>An effect description with its card part as a base code ((code &lt;&lt; 20) | index; system hints unchanged).</summary>
        private ulong BaseDescription(ulong description) =>
            !UseBaseCodes || V == null || description < (1UL << 20) ? description
                : ((ulong)V.Base((uint)(description >> 20)) << 20) | (description & 0xFFFFF);

        private bool SameDescription(ulong offered, ulong entry) => offered == entry || BaseDescription(offered) == BaseDescription(entry);

        private bool HasHint(HashSet<ulong> hints, ulong hint) => hints.Contains(hint) || UseBaseCodes && V != null && hints.Any(h => SameDescription(hint, h));

        /// <summary>Card versions phase 3.1: playbooks match cards by base code (see <see cref="Same"/>). Off only for its A/B.</summary>
        public bool UseBaseCodes { get; set; } = true;

        // Selection groups most playbooks need.
        protected static readonly ulong[] Materials =
            { DuelContext.HintLinkMaterial, DuelContext.HintSynchroMaterial, DuelContext.HintXyzMaterial, DuelContext.HintFusionMaterial };
        protected static readonly ulong[] SummonPicks = { DuelContext.HintSpSummon, DuelContext.HintTarget };
        protected static readonly ulong[] Searches = { DuelContext.HintAddToHand };
        protected static readonly ulong[] ToGrave = { DuelContext.HintToGrave, DuelContext.HintDiscard };
        protected static readonly ulong[] Tributes = { DuelContext.HintRelease, DuelContext.HintTribute };
        protected static readonly ulong[] Banish = { DuelContext.HintRemove };

        /// <summary>A card's own selection message (its script's aux.Stringid(code, index)), for picks it asks with its own wording.</summary>
        protected static ulong[] Asks(uint code, int index) => new[] { EffectDescription(code, index) };

        /// <summary>Actions the bot may take in one phase (30 for most decks: a guard against loops).</summary>
        public virtual int ActionsPerPhase => 30;

        /// <summary>The deck this playbook is for (matches the .ydk file name).</summary>
        public abstract string Deck { get; }

        // ------------------------------------------------------------ Building blocks for subclasses

        protected void Activate(string name, uint code, Func<bool>? when = null, int perTurn = 1) => AddRule(name, Kind.Activate, code, when, perTurn);
        /// <summary>Activate one particular effect of a card with several (its script's Stringid index: Gateway of the Six's 4-counter search is 1).</summary>
        protected void ActivateEffect(string name, uint code, int effect, Func<bool>? when = null, int perTurn = 1) =>
            _rules.Add(new Rule { Name = name, Kind = Kind.Activate, Code = code, Effect = effect, When = when, PerTurn = perTurn });
        protected void NormalSummon(string name, uint code, Func<bool>? when = null) => AddRule(name, Kind.NormalSummon, code, when, 1);
        protected void SpecialSummon(string name, uint code, Func<bool>? when = null, int perTurn = 1) => AddRule(name, Kind.SpecialSummon, code, when, perTurn);

        /// <summary>When a selection has one of <paramref name="hints"/> (and, if given, comes from <paramref name="source"/>), pick these cards first, in order.</summary>
        protected void Prefer(ulong[] hints, uint source, params uint[] codes) =>
            _preferences.Add(new Preference { Hints = new HashSet<ulong>(hints), Source = source, Codes = codes, Weight = 1 });

        /// <summary>Like <see cref="Prefer"/>, but only while a condition holds (search the second piece once the first is in hand).</summary>
        protected void PreferIf(Func<bool> when, ulong[] hints, uint source, params uint[] codes) =>
            _preferences.Add(new Preference { Hints = new HashSet<ulong>(hints), Source = source, Codes = codes, Weight = 1, When = when });

        /// <summary>
        /// "Up to five" selections the deck fills: when one has a hint of <paramref name="hints"/> (and comes from
        /// <paramref name="source"/>, 0 any), take as many of your own cards among <paramref name="codes"/> (none: any of yours) as
        /// it allows, rather than the usual one (Soul Release banishing your GY for Return from the Different Dimension).
        /// </summary>
        protected void TakeAll(ulong[] hints, uint source, params uint[] codes) => _takeAll.Add((new HashSet<ulong>(hints), source, codes));

        private readonly List<(HashSet<ulong> hints, uint source, uint[] codes)> _takeAll = new List<(HashSet<ulong>, uint, uint[])>();

        /// <summary>
        /// A selection the bot would fill to its maximum (a "Special Summon any number" it takes whole) capped at what the
        /// deck can afford now: when one has a hint of <paramref name="hints"/> from <paramref name="source"/>, take at most
        /// <paramref name="limit"/>() (Soul Charge at 1,000 LP a monster: never below 2,000 LP).
        /// </summary>
        protected void TakeAtMost(ulong[] hints, uint source, Func<int> limit) => _takeAtMost.Add((new HashSet<ulong>(hints), source, limit));

        private readonly List<(HashSet<ulong> hints, uint source, Func<int> limit)> _takeAtMost = new List<(HashSet<ulong>, uint, Func<int>)>();

        /// <summary>How many to take in a selection a <see cref="TakeAll"/> or <see cref="TakeAtMost"/> covers; null when none does (the bot's usual count).</summary>
        internal int? TakeCount(ulong hint, uint source, IReadOnlyList<CardRef> cards, int me, int min, int max)
        {
            foreach (var (hints, from, codes) in _takeAll)
            {
                if (!HasHint(hints, hint) || (from != 0 && !Same(source, from))) continue;
                int n = cards.Count(c => c.Controller == me && (codes.Length == 0 || IndexOf(codes, c.Code) >= 0));
                return Math.Max(min, Math.Min(max, n));
            }
            foreach (var (hints, from, limit) in _takeAtMost)
            {
                if (!HasHint(hints, hint) || (from != 0 && !Same(source, from))) continue;
                return Math.Max(min, Math.Min(max, limit()));
            }
            return null;
        }

        /// <summary>
        /// When the duel offers a choice of options that includes this card's effect description (its script's
        /// aux.Stringid(code, index)), take it (Double Helix's "Special Summon it" over "add it to your hand"). Otherwise the
        /// bot takes the first option, as it always has.
        /// </summary>
        protected void ChooseOption(uint code, int index) => _options.Add(EffectDescription(code, index));

        private readonly List<ulong> _options = new List<ulong>();

        /// <summary>The option a <see cref="ChooseOption"/> picks from these, or null (the usual first option).</summary>
        internal int? OptionIndex(List<ulong> options)
        {
            foreach (var wanted in _options)
            {
                int at = options.IndexOf(wanted);
                if (at < 0 && UseBaseCodes) at = options.FindIndex(o => SameDescription(o, wanted));
                if (at >= 0) return at;
            }
            return null;
        }

        /// <summary>Like <see cref="Prefer"/>, but pick these cards last.</summary>
        protected void Avoid(ulong[] hints, uint source, params uint[] codes) =>
            _preferences.Add(new Preference { Hints = new HashSet<ulong>(hints), Source = source, Codes = codes, Weight = -1 });

        /// <summary>
        /// Chain this card when it's offered and the condition holds (a set Trap or a quick effect the deck plays at a
        /// particular moment of the opponent's turn: Mitsurugi Prayers to Tribute Murakumo for its wipe).
        /// </summary>
        protected void ChainWhen(string name, uint code, Func<bool> when) => _chainRules.Add((name, code, when));

        private readonly List<(string name, uint code, Func<bool> when)> _chainRules = new List<(string, uint, Func<bool>)>();

        /// <summary>The chain rule that wants this card now, if any.</summary>
        internal string? ChainRule(uint code) => !ChainRulesEnabled ? null : _chainRules.FirstOrDefault(r => Same(code, r.code) && r.when()).name;

        /// <summary>Off (YGO_CHAIN_RULES=0): playbooks' chain rules are ignored, for measuring what they're worth.</summary>
        public static readonly bool ChainRulesEnabled = Environment.GetEnvironmentVariable("YGO_CHAIN_RULES") != "0";

        /// <summary>Stop the generic bot from Normal Summoning this card (rules can still do it), optionally only while a condition holds.</summary>
        protected void HoldNormalSummon(uint code, Func<bool>? when = null) => _vetoes.Add((Kind.NormalSummon, code, -1, when));
        protected void HoldSpecialSummon(uint code, Func<bool>? when = null) => _vetoes.Add((Kind.SpecialSummon, code, -1, when));
        /// <summary>Keep the turn's Normal Summon (or Set) for the deck's rules and routes while a condition holds.</summary>
        protected void HoldNormalSummons(Func<bool> when) => _normalSummonHolds.Add(when);

        private readonly List<Func<bool>> _normalSummonHolds = new List<Func<bool>>();
        protected void HoldActivation(uint code, Func<bool>? when = null) => _vetoes.Add((Kind.Activate, code, -1, when));
        /// <summary>Hold back one effect of a card with several (its script's Stringid index), like <see cref="ActivateEffect"/>.</summary>
        protected void HoldEffect(uint code, int effect, Func<bool>? when = null) => _vetoes.Add((Kind.Activate, code, effect, when));
        /// <summary>Stop the generic bot from setting this Spell/Trap (0: any Spell/Trap) while a condition holds.</summary>
        protected void HoldSet(uint code, Func<bool>? when = null) => _vetoes.Add((Kind.SetSpellTrap, code, -1, when));
        protected void SetSpellTrap(string name, uint code, Func<bool>? when = null) => AddRule(name, Kind.SetSpellTrap, code, when, 1);

        /// <summary>
        /// The deck's boss monsters: once on the field they're built on, not spent. The bot values them well above their
        /// ATK, so it won't use one as material (or Tribute) for something lesser unless it's stuck.
        /// </summary>
        protected void Boss(params uint[] codes) { foreach (var c in codes) _bosses.Add(c); }

        private readonly HashSet<uint> _bosses = new HashSet<uint>();

        /// <summary>Whether a card is one of the deck's bosses.</summary>
        public bool IsBoss(uint code) => _bosses.Contains(code) || UseBaseCodes && V != null && _bosses.Any(b => Same(code, b));

        /// <summary>
        /// Cards the bot's "it could only hit our own cards" check leaves alone: removal the deck plays on its own cards on
        /// purpose (Radiant Typhoon's Mystical Space Typhoon on its own Spells, which Set themselves again), and effects it
        /// misreads as removal (Weaver of Fairy Tails summoning "from your Deck or banishment").
        /// </summary>
        protected void OwnTargets(params uint[] codes) { foreach (var c in codes) _ownTargets.Add(c); }

        private readonly HashSet<uint> _ownTargets = new HashSet<uint>();

        /// <summary>Whether the deck plays this removal on its own cards.</summary>
        internal bool TargetsOwn(uint code) => _ownTargets.Contains(code) || UseBaseCodes && V != null && _ownTargets.Any(t => Same(code, t));

        private void AddRule(string name, Kind kind, uint code, Func<bool>? when, int perTurn) =>
            _rules.Add(new Rule { Name = name, Kind = kind, Code = code, When = when, PerTurn = perTurn });

        // ------------------------------------------------------------ The Fiendsmith engine (No Limits, docs/dev/NO-LIMITS-SPEC-2026-11-02.md 7.2)

        /// <summary>The Fiendsmith cards, for <see cref="FiendsmithEngine"/> and the builds that run it.</summary>
        protected const uint FiendsmithEngraver = 60764609, FiendsmithTract = 98567237, LacrimaCrimsonTears = 28803166, FabledLurrie = 97651498,
            FiendsmithRequiem = 2463794, FiendsmithSequence = 49867899, FiendsmithLacrima = 46640168, FiendsmithDesirae = 82135803,
            FiendsmithAgnumday = 32991300;

        /// <summary>The LIGHT Fiends a Fiendsmith line puts on the field (Requiem's and Sequence's material).</summary>
        protected static readonly uint[] FiendsmithLightFiends = { FiendsmithEngraver, LacrimaCrimsonTears, FabledLurrie, FiendsmithLacrima, FiendsmithDesirae };

        /// <summary>
        /// The Fiendsmith engine, for a build that adds it to a base deck without it (Junia's Snake-Eye, Tova's Ryzeal;
        /// Yubel keeps its own rules): Engraver discards for Tract; Tract adds Lacrima the Crimson Tears or Lurrie (and
        /// discards Lurrie, which summons itself); a LIGHT Fiend becomes Requiem, which Tributes itself for a Fiendsmith
        /// from the Deck; two bodies with a LIGHT Fiend become Sequence, which Fusion Summons from the GY; Tract's GY
        /// effect and Sequence make Fiendsmith's Lacrima (46640168), which revives a LIGHT Fiend and, sent to the GY,
        /// shuffles one back to burn 1200. Every card here is one the as-built snake-eye and ryzeal lists don't run, so
        /// the bases play exactly as before (7.1). <paramref name="when"/> gates every rule (null: always).
        /// </summary>
        protected void FiendsmithEngine(Func<bool>? when = null)
        {
            bool On() => when == null || when();
            ActivateEffect("Fiendsmith Engraver discards for Fiendsmith's Tract", FiendsmithEngraver, 0,
                () => On() && V.InHand(FiendsmithEngraver) > 0 && V.InDeck(FiendsmithTract) > 0 && V.IsMyMainPhase);
            ActivateEffect("Fiendsmith's Tract adds a LIGHT Fiend", FiendsmithTract, 0, () => On() && V.InHand(FiendsmithTract) > 0 && V.IsMyMainPhase);
            NormalSummon("Lacrima the Crimson Tears sends a Fiendsmith card", LacrimaCrimsonTears, On);
            SpecialSummon("a LIGHT Fiend into Fiendsmith's Requiem", FiendsmithRequiem,
                () => On() && V.OnFieldAny(FiendsmithLightFiends) > 0 && V.InDeckAny(FiendsmithEngraver, LacrimaCrimsonTears) > 0);
            ActivateEffect("Fiendsmith's Requiem summons a Fiendsmith from the Deck", FiendsmithRequiem, 0,
                () => On() && V.IsMyTurn && V.InDeckAny(FiendsmithEngraver, LacrimaCrimsonTears) > 0);
            SpecialSummon("Links into Fiendsmith's Sequence", FiendsmithSequence,
                () => On() && V.MyMonsterCount >= 2 && V.OnFieldAny(FiendsmithLightFiends) + V.OnField(FiendsmithRequiem) > 0
                      && V.InExtraDeckAny(FiendsmithLacrima, FiendsmithDesirae));
            ActivateEffect("Fiendsmith's Sequence Fusion Summons from the GY", FiendsmithSequence, 0,
                () => On() && V.IsMyTurn && V.InGraveyardAny(FiendsmithLightFiends) >= 2);
            ActivateEffect("Fiendsmith's Tract Fusion Summons from the GY", FiendsmithTract, 1,
                () => On() && V.InGraveyard(FiendsmithTract) > 0 && V.InExtraDeckAny(FiendsmithLacrima, FiendsmithDesirae)
                      && V.OnFieldAny(FiendsmithLightFiends) + V.InHandCountAny(FiendsmithLightFiends) >= 2);
            ActivateEffect("Fiendsmith Engraver comes back", FiendsmithEngraver, 2,
                () => On() && V.InGraveyard(FiendsmithEngraver) > 0 && V.InGraveyardAny(LacrimaCrimsonTears, FabledLurrie, FiendsmithLacrima) > 0 && V.FreeMonsterZones > 0);

            Prefer(Searches, FiendsmithEngraver, FiendsmithTract);
            Prefer(Searches, FiendsmithTract, LacrimaCrimsonTears, FabledLurrie, FiendsmithEngraver);
            // Tract's discard: Lurrie summons itself from it; a spare Engraver revives later.
            Prefer(ToGrave, FiendsmithTract, FabledLurrie, FiendsmithEngraver);
            Prefer(SummonPicks, FiendsmithRequiem, LacrimaCrimsonTears, FiendsmithEngraver);
            Prefer(SummonPicks, FiendsmithSequence, FiendsmithLacrima, FiendsmithDesirae);
            Prefer(SummonPicks, FiendsmithTract, FiendsmithLacrima, FiendsmithDesirae);
            Prefer(Materials, FiendsmithRequiem, FabledLurrie, LacrimaCrimsonTears, FiendsmithEngraver);
            Prefer(Materials, FiendsmithSequence, FiendsmithRequiem, FabledLurrie, LacrimaCrimsonTears, FiendsmithEngraver);
            Prefer(new[] { DuelContext.HintFusionMaterial }, FiendsmithTract, FabledLurrie, FiendsmithRequiem, LacrimaCrimsonTears, FiendsmithEngraver);
            // Fiendsmith's Lacrima: revive Engraver (its own GY effects) rather than add it; shuffle Lurrie for the burn.
            Prefer(SummonPicks, FiendsmithLacrima, FiendsmithEngraver, LacrimaCrimsonTears, FabledLurrie);
            ChooseOption(FiendsmithLacrima, 3);
            Prefer(new[] { DuelContext.HintToDeck }, FiendsmithLacrima, FabledLurrie, LacrimaCrimsonTears, FiendsmithEngraver);
            Prefer(new[] { DuelContext.HintToDeck }, FiendsmithEngraver, FabledLurrie, LacrimaCrimsonTears, FiendsmithLacrima);
            Avoid(Materials, 0, FiendsmithLacrima, FiendsmithDesirae);
        }

        // ------------------------------------------------------------ Called by SmartDuelist

        internal void Attach(PlaybookView view)
        {
            V = view;
            view.UseBaseCodes = UseBaseCodes;
            if (!ReferenceEquals(_heard, view.Context))
            {
                if (_heard != null) _heard.Observed -= Hear;
                _heard = view.Context;
                _heard.Observed += Hear;
                _chainLinks = view.Context.Chain.Count;
                _chainSolving = view.Context.ChainResolving;
            }
            if (view.Turn != _turn)
            {
                _turn = view.Turn;
                foreach (var r in _rules) r.FiredThisTurn = 0;
                EndRoute();
                _routeChosen = _routeTaken = false;
                _routeStep = 0;
                _currentStep = null;
            }
        }

        /// <summary>The first rule that applies to this Main Phase decision, as a response; null to let the generic bot decide.</summary>
        internal byte[]? Idle(IdlePrompt p, bool commit, out string what)
        {
            what = "";
            if (commit) _currentStep = null;
            if (commit) SignalCount();
            // YGO_DEBUG_ROUTES=2: every decision's offered actions as well.
            if (commit && Environment.GetEnvironmentVariable("YGO_DEBUG_ROUTES") == "2")
                Console.Error.WriteLine($"      [idle] turn {V.Turn} activatable {string.Join(", ", p.Activatable.Select(o => $"{o.Card.Code}:{o.Description & 0xF}"))}; summonable {string.Join(", ", p.Summonable.Select(c => c.Code))}; field {V.DebugField}");
            // A combo route first: its next step on offer.
            if (_routes.Count > 0 && NextStep(p) is var (step, index, routed))
            {
                if (commit)
                {
                    _routeStep = index + 1;
                    _currentStep = step;
                    if (!_routeStarted)
                    {
                        _routeStarted = true;
                        _countBase = _route!.CountOf?.Invoke() ?? 0;
                        Signal(RouteEvent.Started, index, step.Name);
                    }
                    _routePlayed++;
                    _routeLastStep = index;
                    bool last = !_route!.Steps.Any(st => st.IsPayoff) && index == _route.Steps.Count - 1;
                    Signal(RouteEvent.Step, index, step.Name);
                    // The payoff (or a route's last step without one) counts once it resolves (Hear): not here, as it's chosen.
                    if (step.IsPayoff || last) AwaitPayoff(step, index);
                }
                what = $"route {_route!.Name}: {step.Name}";
                return routed;
            }
            if (_route != null && commit && (!_route.Waits || _routeStep >= _route.Steps.Count)) EndRoute(); // the route is done: the usual rules from here
            foreach (var rule in _rules)
            {
                if (rule.FiredThisTurn >= rule.PerTurn) continue;
                var response = Find(p, rule.Kind, rule.Code, rule.Effect);
                if (response == null) continue;
                if (rule.When != null && !rule.When()) continue;
                if (commit) rule.FiredThisTurn++;
                what = rule.Name;
                return response;
            }
            return null;
        }

        internal bool Vetoes(IdleAction action, uint code, ulong description = 0)
        {
            var kind = action switch
            {
                IdleAction.Activate => Kind.Activate,
                IdleAction.Summon => Kind.NormalSummon,
                IdleAction.SpecialSummon => Kind.SpecialSummon,
                IdleAction.SetSpellTrap => Kind.SetSpellTrap,
                _ => (Kind?)null,
            };
            if (kind == Kind.NormalSummon && _normalSummonHolds.Any(w => w())) return true;
            return kind != null && _vetoes.Any(v => v.kind == kind && (Same(code, v.code) || (v.code == 0 && kind == Kind.SetSpellTrap))
                                                    && (v.effect < 0 || SameDescription(description, EffectDescription(v.code, v.effect)))
                                                    && (v.when == null || v.when()));
        }

        /// <summary>Extra score for a selection candidate: large positive to pick it first, negative to pick it last.</summary>
        internal double SelectionBias(CardRef card, ulong hint, uint source)
        {
            double bias = 0;
            // The route step being played picks first.
            if (_currentStep != null)
                foreach (var pick in _currentStep.Picks)
                {
                    if (!HasHint(pick.Hints, hint)) continue;
                    var at = IndexOf(pick.Codes, card.Code);
                    if (at >= 0) bias += pick.Weight * (100000 - at * 1000);
                }
            foreach (var pref in _preferences)
            {
                if (!HasHint(pref.Hints, hint) || (pref.Source != 0 && !Same(source, pref.Source))) continue;
                if (pref.When != null && !pref.When()) continue;
                var rank = IndexOf(pref.Codes, card.Code);
                if (rank >= 0) bias += pref.Weight * (100000 - rank * 1000);
            }
            return bias;
        }

        /// <summary>A card effect's description as the core reports it: aux.Stringid(code, index), (code << 20) | index.</summary>
        private static ulong EffectDescription(uint code, int effect) => ((ulong)code << 20) | (uint)effect;

        private byte[]? Find(IdlePrompt p, Kind kind, uint code, int effect = -1)
        {
            switch (kind)
            {
                case Kind.Activate:
                    var a = p.Activatable.FindIndex(o => Same(o.Card.Code, code) && (effect < 0 || SameDescription(o.Description, EffectDescription(code, effect))));
                    return a < 0 ? null : Responses.Idle(IdleAction.Activate, a);
                case Kind.NormalSummon:
                    var n = p.Summonable.FindIndex(c => Same(c.Code, code));
                    return n < 0 ? null : Responses.Idle(IdleAction.Summon, n);
                case Kind.SpecialSummon:
                    var s = p.SpSummonable.FindIndex(c => Same(c.Code, code));
                    return s < 0 ? null : Responses.Idle(IdleAction.SpecialSummon, s);
                case Kind.SetSpellTrap:
                    var t = p.SpellSettable.FindIndex(c => Same(c.Code, code));
                    return t < 0 ? null : Responses.Idle(IdleAction.SetSpellTrap, t);
                default:
                    return null;
            }
        }
    }

    /// <summary>What a playbook rule may look at: your own cards (hand, field, GY, Deck contents) and public board facts.</summary>
    public sealed class PlaybookView
    {
        private const uint TypeTuner = 0x1000;

        private readonly DuelContext _ctx;
        private readonly ICardDataProvider _cards;

        public int Me { get; }

        public PlaybookView(DuelContext context, int me)
        {
            _ctx = context;
            _cards = context.Cards;
            Me = me;
        }

        /// <summary>The duel's context (the playbook hears its messages: a payoff is confirmed as it resolves).</summary>
        internal DuelContext Context => _ctx;

        private readonly Dictionary<uint, uint> _base = new Dictionary<uint, uint>();

        /// <summary>A card's base code: an alias id (an original or GOAT text, an alt art) as the card it is a version of.</summary>
        public uint Base(uint code)
        {
            lock (_base)
            {
                if (!_base.TryGetValue(code, out var b))
                {
                    b = DeckBuilding.Banlist.BaseCode(code, _cards);
                    // The card database also aliases a different card that is "always treated as" another (Fallen of the
                    // White Dragon to Fallen of Albaz, Power Bond (Anime) to Polymerization, Pacifis to Umi): a version
                    // keeps its card's name, up to a trailing "(Pre-Errata)", "(Anime)"... tag.
                    if (b != code && PlainName(_cards.Get(code)?.Name) != PlainName(_cards.Get(b)?.Name)) b = code;
                    _base[code] = b;
                }
                return b;
            }
        }

        private static string PlainName(string? name) =>
            name == null ? "" : System.Text.RegularExpressions.Regex.Replace(name, @"\s*\([^)]*\)\s*$", "");

        /// <summary>Base-code matching on (card versions 3.1); the playbook sets it.</summary>
        internal bool UseBaseCodes = true;

        private uint B(uint code) => UseBaseCodes ? Base(code) : code;
        private bool Is(uint card, uint entry) => card == entry || UseBaseCodes && Base(card) == Base(entry);
        private bool In(uint[] entries, uint card)
        {
            if (Array.IndexOf(entries, card) >= 0) return true;
            if (!UseBaseCodes) return false;
            foreach (var e in entries) if (Is(card, e)) return true;
            return false;
        }

        /// <summary>A card's type bits (0 when unknown).</summary>
        internal uint TypeOf(uint code) => _cards.Get(code)?.Type ?? 0;

        private PlayerBoard Mine => _ctx.Board.Players[Me];
        private PlayerBoard Theirs => _ctx.Board.Players[1 - Me];

        public int Turn => _ctx.Board.Turn;
        public bool IsMyTurn => _ctx.Board.TurnPlayer == Me;
        /// <summary>The first turn of the duel: nobody can attack yet.</summary>
        public bool IsFirstTurn => _ctx.Board.Turn <= 1;
        /// <summary>Your own Main Phase (1 or 2): where a combo is played, rather than in the Draw or Standby Phase.</summary>
        public bool IsMyMainPhase => IsMyTurn && _ctx.Board.Phase is Phase.Main1 or Phase.Main2;
        /// <summary>Your Main Phase 1: a combo that ends in attacks is played here.</summary>
        public bool IsMyMainPhase1 => IsMyTurn && _ctx.Board.Phase == Phase.Main1;

        public int InHand(uint code) => Mine.Hand.Count(c => c != null && Is(c.Code, code));
        public int OnField(uint code) => Mine.Monsters.Count(c => c != null && Is(c.Code, code));
        public int InGraveyard(uint code) => Mine.Graveyard.Count(c => c != null && Is(c.Code, code));
        /// <summary>Copies left in your Main Deck (you know your Deck's contents, not its order).</summary>
        public int InDeck(uint code) => Mine.Deck.Count(c => c != null && Is(c.Code, code));
        public int InExtraDeck(uint code) => Mine.ExtraDeck.Count(c => c != null && Is(c.Code, code));
        public bool InExtraDeckAny(params uint[] codes) => Mine.ExtraDeck.Any(c => c != null && In(codes, c.Code));

        /// <summary>Your monsters as code/position, for debug logs.</summary>
        public string DebugField => string.Join(",", Mine.Monsters.Where(c => c != null).Select(c => $"{c!.Code}/{c.Position}"));
        /// <summary>You've made your Normal Summon (or Set a monster) this turn.</summary>
        public bool NormalSummonUsed => _ctx.NormalSummonsThisTurn[Me & 1] > 0;
        public int MyLifePoints => Mine.LifePoints;
        public int OpponentLifePoints => Theirs.LifePoints;
        public int MyMonsterCount => Mine.Monsters.Count(c => c != null);
        public int OpponentMonsterCount => Theirs.Monsters.Count(c => c != null);
        /// <summary>Cards in the opponent's hand (their number is public).</summary>
        public int OpponentHandCount => Theirs.Hand.Count;
        public int OpponentCardCount => Theirs.Monsters.Count(c => c != null) + Theirs.SpellTraps.Take(6).Count(c => c != null);
        public bool ControlTuner => Mine.Monsters.Any(c => c != null && c.IsFaceUp && (c.Type & TypeTuner) != 0);

        /// <summary>Monsters in your GY at or below a Level (Link monsters have none, so they never count).</summary>
        public int GraveyardMonstersAtOrBelowLevel(int level) =>
            Mine.Graveyard.Count(c => c != null && (c.Type & CardType.Monster) != 0 && (c.Type & CardType.Link) == 0
                                      && (_cards.Get(c.Code)?.Level ?? 99) <= level);

        /// <summary>Your face-up non-Tuner monsters of exactly this Level (Synchro / Xyz material).</summary>
        public int NonTunersOfLevel(int level) =>
            Mine.Monsters.Count(c => c != null && c.IsFaceUp && (c.Type & TypeTuner) == 0 && (c.Type & CardType.Link) == 0 && c.Level == level);

        public bool InHandAny(params uint[] codes) => Mine.Hand.Any(c => c != null && In(codes, c.Code));
        public int InHandCountAny(params uint[] codes) => Mine.Hand.Count(c => c != null && In(codes, c.Code));
        public int OnFieldAny(params uint[] codes) => Mine.Monsters.Count(c => c != null && In(codes, c.Code));
        public int InGraveyardAny(params uint[] codes) => Mine.Graveyard.Count(c => c != null && In(codes, c.Code));
        public int InDeckAny(params uint[] codes) => Mine.Deck.Count(c => c != null && In(codes, c.Code));
        /// <summary>Your Spell/Trap Zones in use (Field Zone not counted; Pendulum Scales are).</summary>
        public int SpellTrapCount => Mine.SpellTraps.Take(5).Count(c => c != null);
        /// <summary>Your Pendulum Scales in place (Pendulum monsters in the Spell/Trap Zones).</summary>
        public int PendulumScales => Mine.SpellTraps.Take(5).Count(c => c != null && (c.Type & 0x1000000) != 0);
        public bool InSpellTrapZone(uint code) => Mine.SpellTraps.Any(c => c != null && Is(c.Code, code));
        public bool InSpellTrapZoneAny(params uint[] codes) => Mine.SpellTraps.Any(c => c != null && In(codes, c.Code));
        public int HandCount => Mine.Hand.Count(c => c != null);
        public int GraveyardCount => Mine.Graveyard.Count(c => c != null);

        // Combo meters and banish decks (Headliners, phase 3).
        /// <summary>Your banished monsters (face-up ones: what Return from the Different Dimension can bring back).</summary>
        public int BanishedMonsters => Mine.Banished.Count(c => c != null && (c.Type & CardType.Monster) != 0);
        public int Banished(uint code) => Mine.Banished.Count(c => c != null && Is(c.Code, code));
        public int BanishedAny(params uint[] codes) => Mine.Banished.Count(c => c != null && In(codes, c.Code));
        /// <summary>Cards banished from your side this duel (each time, so a card banished twice counts twice).</summary>
        public int CardsBanishedThisDuel => _ctx.BanishedTotal[Me & 1];
        /// <summary>Your Special Summons this duel, and this turn.</summary>
        public int SpecialSummonsThisDuel => _ctx.SpecialSummonsTotal[Me & 1];
        public int SpecialSummonsThisTurn => _ctx.SpecialSummonsThisTurn[Me & 1];
        /// <summary>Cards in the opponent's Graveyard and banished.</summary>
        public int OpponentGraveyardCount => Theirs.Graveyard.Count(c => c != null);
        public int OpponentBanishedCount => Theirs.Banished.Count(c => c != null);
        public int OpponentBanishedMonsters => Theirs.Banished.Count(c => c != null && (c.Type & CardType.Monster) != 0);
        /// <summary>Your monsters in the GY with these Attributes (CardInfo attribute bits: LIGHT 0x10, DARK 0x20...).</summary>
        public int GraveyardMonstersOfAttribute(uint attribute) =>
            Mine.Graveyard.Count(c => c != null && (c.Type & CardType.Monster) != 0 && ((_cards.Get(c.Code)?.Attribute ?? 0u) & attribute) != 0);
        /// <summary>Monsters in your hand with these Attributes (a discard cost that asks for a FIRE monster: the Dragon Rulers').</summary>
        public int HandMonstersOfAttribute(uint attribute) =>
            Mine.Hand.Count(c => c != null && (c.Type & CardType.Monster) != 0 && ((_cards.Get(c.Code)?.Attribute ?? 0u) & attribute) != 0);
        /// <summary>Non-Fusion monsters in your GY or banished (what Branded Expulsion brings back).</summary>
        public int RecoverableNonFusionMonsters =>
            Mine.Graveyard.Concat(Mine.Banished).Count(c => c != null && (c.Type & CardType.Monster) != 0 && (c.Type & CardType.Fusion) == 0);
        /// <summary>The opponent's End Phase: the last window to use a set card before your turn.</summary>
        public bool IsOpponentEndPhase => !IsMyTurn && _ctx.Board.Phase == Phase.End;
        /// <summary>The Levels of your face-up Tuners, or of your face-up non-Tuners (Link monsters have none): Synchro sums.</summary>
        public List<int> FaceUpLevels(bool tuners) =>
            Mine.Monsters.Where(c => c != null && c.IsFaceUp && (c.Type & CardType.Link) == 0 && ((c.Type & TypeTuner) != 0) == tuners)
                .Select(c => (int)c!.Level).ToList();
        /// <summary>Your face-up monsters (codes), for routes that pick materials by what's there.</summary>
        public List<uint> FaceUpCodes => Mine.Monsters.Where(c => c != null && c.IsFaceUp).Select(c => B(c!.Code)).ToList();
        /// <summary>Free Main Monster Zones.</summary>
        public int FreeMonsterZones => Mine.Monsters.Take(5).Count(c => c == null);
        /// <summary>The sum of your face-up monsters' Levels, the given codes excepted (Synchro material counts).</summary>
        public int FieldLevels(params uint[] except) =>
            Mine.Monsters.Where(c => c != null && c.IsFaceUp && (c.Type & CardType.Link) == 0 && !In(except, c!.Code)).Sum(c => (int)c!.Level);

        /// <summary>The newest link of the chain being built is ours and one of these cards (to chain to our own activation).</summary>
        public bool ChainTopIsMine(params uint[] codes) =>
            !_ctx.ChainResolving && _ctx.Chain.Count > 0 && _ctx.Chain[^1].Controller == Me && In(codes, _ctx.Chain[^1].Code);

        /// <summary>A chain is being built (not resolving) and one of its links is ours and one of these cards.</summary>
        public bool ChainHasMine(params uint[] codes) =>
            !_ctx.ChainResolving && _ctx.Chain.Exists(l => l.Controller == Me && In(codes, l.Code));

        /// <summary>A chain is being built (not resolving) and its newest link is the opponent's.</summary>
        public bool ChainTopIsTheirs => !_ctx.ChainResolving && _ctx.Chain.Count > 0 && _ctx.Chain[^1].Controller != Me;

        public int BestAttack => Mine.Monsters.Where(c => c != null).Select(c => c!.Attack).DefaultIfEmpty(0).Max();

        /// <summary>
        /// The most opposing cards in one column where a Mekk-Knight can land from the hand (2 or more cards there, our Main
        /// Monster Zone in it free): what Mekk-Knight Blue Sky would search. -1 when no column qualifies.
        /// </summary>
        public int BestMekkColumn
        {
            get
            {
                var total = Presentation.Columns.CardsIn(_ctx.Board);
                int best = -1;
                for (int column = 0; column < Presentation.Columns.Count; column++)
                {
                    int seq = Me == 0 ? column : 4 - column;
                    if (total[column] < 2 || seq >= Mine.Monsters.Count || Mine.Monsters[seq] != null) continue;
                    best = Math.Max(best, OpposingCardsIn(column));
                }
                return best;
            }
        }

        private int OpposingCardsIn(int column)
        {
            int n = 0, opp = 1 - Me;
            for (int s = 0; s < Theirs.Monsters.Count && s < 7; s++)
                if (Theirs.Monsters[s] != null && Presentation.Columns.Of(opp, Location.MZone, s) == column) n++;
            for (int s = 0; s < Theirs.SpellTraps.Count && s < 5; s++)
                if (Theirs.SpellTraps[s] != null && Presentation.Columns.Of(opp, Location.SZone, s) == column) n++;
            return n;
        }

        /// <summary>The opponent's Spell/Trap Zones in use (Field Zone not counted).</summary>
        public int OpponentSpellTrapCount => Theirs.SpellTraps.Take(5).Count(c => c != null);

        /// <summary>Your face-up monsters, except the given codes (e.g. a deck's bosses), with different names: material counts.</summary>
        public int DifferentNamesOnField(params uint[] except) =>
            Mine.Monsters.Where(c => c != null && c.IsFaceUp && !In(except, c!.Code)).Select(c => B(c!.Code)).Distinct().Count();

        /// <summary>Your face-up monsters at or above a Level (Link monsters have none), except the given codes.</summary>
        public int OnFieldAtOrAboveLevel(int level, params uint[] except) =>
            Mine.Monsters.Count(c => c != null && c.IsFaceUp && (c.Type & CardType.Link) == 0 && c.Level >= level && !In(except, c.Code));
    }
}
