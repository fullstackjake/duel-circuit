using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Campaign;
using Ygo.Engine.Replays;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The headliners' show (docs/dev/HEADLINERS-2026-11-01.md, "The show"; ALLEY-SPEC Phase D), wherever a headliner sits (its
    /// table, an encore, Nyx's Table, the exhibition, a combo clip): its playbook's route signals (<see cref="ComboShow"/>)
    /// staged on the board. A route starting raises the banner, the sting and Baz's call (and the headliner's own `combo`
    /// line); the meter follows it step by step with its running total; the payoff holds the duel a moment under a stamp,
    /// the crowd roars and Baz calls it; a fizzle shows where it stopped, Baz and Mika react and the crowd groans (and the
    /// headliner says its `fizzle` line). While a route plays, "Slow down for combos" holds the duel at Normal speed.
    /// A headliner's route stopped before its payoff counts toward its Combo Breaker (<see cref="ComboWatch"/>, passed to
    /// <see cref="BackAlley.Record"/>). The payoff is staged as it resolves (the playbook confirms it then): a negated payoff
    /// is a fizzle at its step. Skipping the opponent's turn stages nothing: the show catches up quietly (a line in the log,
    /// the meter as it ended). A show-only duel's calls aren't marked heard (<see cref="ShopVoice.Peek"/>). Also here: the watch-only exhibition on Baz's chalkboard (two headliners you've met,
    /// or a headliner against a splasher; no stakes, no DP, no posters) and Watch the combo on an Unlisted page (the
    /// headliner's goldfish clip, <see cref="HeadlinerReplays"/>, played again live so the show plays with it).
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>Normal speed (4 events a second): what a route plays at with "Slow down for combos" on.</summary>
        private const int NormalSpeed = 3;

        private readonly ComboShow _show = new ComboShow();
        /// <summary>This duel's headliner routes, for the Combo Breaker (the engine's own count of them).</summary>
        private ComboWatch _comboWatch = new ComboWatch();
        /// <summary>The payoff beat: the duel holds until then (real time).</summary>
        private float _showHoldUntil;
        private int _showTotalShown = -1;
        /// <summary>Whether the last frame ran slowed (so the controls are redrawn when it changes).</summary>
        private bool _showSlowed;

        /// <summary>A show duel (an exhibition, a combo clip) about to start, and the one running: its sides, decks and seats.</summary>
        private sealed class ShowDuel
        {
            public string Kind = "";          // "exhibition" | "clip"
            public string Title = "";
            public Personality[] Who = Array.Empty<Personality>();
            public Deck[] Decks = Array.Empty<Deck>();
            /// <summary>A seat's duelist when the show supplies its own (the clip's goldfish bot and its dummy); null: a character.</summary>
            public Func<int, DuelContext, IDuelist?>? Bot;
            /// <summary>The headliner whose Unlisted page the clip came from (back there after).</summary>
            public string Headliner = "";
            /// <summary>The clip's replay, for checking the live run against it.</summary>
            public Replay? Replay;
        }

        private ShowDuel? _showDuelNext, _showDuel;

        /// <summary>For checks: the moments the show staged this duel, in order ("started Frog FTK", "payoff", "fizzled at 5").</summary>
        private readonly List<string> _showLog = new List<string>();
        /// <summary>For checks: the slowest the duel ran while a route played (events a second), and the calls made.</summary>
        private float _showSpeedDuringRoute;
        private readonly List<string> _showCalls = new List<string>();

        /// <summary>The speed the duel runs at now: Normal while a route plays (unless the setting's off), else yours.</summary>
        private int PlaySpeed => _show.Live && FxSettings.SlowForCombos && _watching == null ? Math.Min(speedIndex, NormalSpeed) : speedIndex;

        /// <summary>The payoff beat holds the duel (as an animation does).</summary>
        private bool ShowHolding => Time.unscaledTime < _showHoldUntil;

        private void InitShow()
        {
            ShowSounds.Register();
            _campaignScreen.ExhibitionClicked += (left, right) => StartExhibition(left, right);
            _campaignScreen.WatchComboClicked += WatchCombo;
        }

        /// <summary>A new duel: which seats are headliners' (their profile ids name a headliner in the alley's data).</summary>
        private void ResetShow()
        {
            string Id(int seat) => _players.Length > seat && _campaign.AlleyReady && _campaign.AlleyData.FindHeadliner(_players[seat].Id) != null ? _players[seat].Id : "";
            string Handle(int seat) => Id(seat).Length > 0 ? _campaign.AlleyData.FindHeadliner(Id(seat))!.Handle : "";
            ClearShow(Id(0), Handle(0), Id(1), Handle(1));
        }

        /// <summary>A replay: no show (its bots aren't playing, so no route signals come), and nothing left from the last duel.</summary>
        private void StopShow() => ClearShow("", "", "", "");

        private void ClearShow(string id0, string handle0, string id1, string handle1)
        {
            _show.Reset(id0, handle0, id1, handle1);
            _comboWatch = new ComboWatch();
            _showHoldUntil = 0;
            _showTotalShown = -1;
            _showSlowed = false;
            _showLog.Clear();
            _showCalls.Clear();
            _showSpeedDuringRoute = 0;
            _showEnding = false;
            _showVoiceTurn = 0;
            _view.ClearCombo();
        }

        /// <summary>
        /// A seat's playbook (salt 0 or 1: the seat) is watched when that seat is a headliner's. Across the table from you, the
        /// engine's <see cref="ComboWatch"/> counts its routes for the Combo Breaker (a fizzle you caused: a chain link of
        /// yours while the route was live, in its own turn; the duel ending with a route you stopped still live counts too).
        /// </summary>
        private void WatchRoutes(int salt, Playbook? book, DuelContext context)
        {
            if (book == null || salt is not (0 or 1) || !_show.IsHeadliner(salt)) return;
            book.RouteSignaled += signal => _show.Enqueue(salt, signal);
            if (salt == 1) _comboWatch = new ComboWatch(book, context, 1);
        }

        /// <summary>An alley duel (a table's, or an alley bracket's game of yours) is being played: no show may start over it.</summary>
        private bool AlleyDuelLive => _runner != null && !_runner.IsFinished && _watching == null
                                      && (_alleyTable != null || (_bracketMatch != null && !_bracketWatch && _campaign.Profile?.Bracket is Bracket b && Brackets.IsAlley(b)));

        /// <summary>A show-only duel's calls so far (the exhibition, a combo clip): which line of a pool comes next, none recorded.</summary>
        private int _showVoiceTurn;

        /// <summary>A duel that's only a show: nothing it says is marked heard in your save.</summary>
        private bool ShowOnlyDuel => _showDuel != null;

        /// <summary>Each frame the duel steps: the show's moments, staged; the meter's total; the speed.</summary>
        private void StepShow()
        {
            if (!_show.On) { _showEnding = false; return; }
            foreach (var moment in _show.Take(_events, _board.Turn, new[] { _board.Players[0].LifePoints, _board.Players[1].LifePoints })) Stage(moment);
            if (_show.State != ComboShow.Phase.None && _show.Total != _showTotalShown && _show.Live)
            {
                _showTotalShown = _show.Total;
                _view.SetComboMeter(_show);
            }
            if (_show.Live) _showSpeedDuringRoute = Math.Max(_showSpeedDuringRoute, Speeds[PlaySpeed]);
            if (_showEnding) EndShow();
            if (_show.Live != _showSlowed) { _showSlowed = _show.Live; UpdateControls(); }
        }

        /// <summary>
        /// Skipping the opponent's turn: the show's moments since, taken without the banner, the stamps, the hold or the calls
        /// (nothing to stage after the fact); a short line in the log for each start and end, and the meter as it stands.
        /// </summary>
        private void CatchUpShow()
        {
            if (!_show.On) return;
            foreach (var moment in _show.Take(_events, _board.Turn, new[] { _board.Players[0].LifePoints, _board.Players[1].LifePoints }, holdAtPayoff: false))
                switch (moment)
                {
                    case ComboShow.Moment.Started:
                        _view.AppendLog($"{_show.Handle} went for {_show.Route}.");
                        _showLog.Add($"started {_show.Route} (skipped)");
                        break;
                    case ComboShow.Moment.Payoff:
                        _showLog.Add("payoff (skipped)");
                        break;
                    case ComboShow.Moment.Finished:
                        _view.AppendLog($"{_show.Handle}'s {_show.Route} got there ({_show.TotalText}).");
                        _showLog.Add("finished (skipped)");
                        break;
                    case ComboShow.Moment.Fizzled:
                        _view.AppendLog($"{_show.Handle}'s {_show.Route} stopped at step {_show.FizzledAt} of {_show.Steps}.");
                        _showLog.Add($"fizzled at {_show.FizzledAt} (skipped)");
                        break;
                }
            _showHoldUntil = 0;
            _showTotalShown = -1;
        }

        /// <summary>The skip is over: the meter as the show stands (no banner or stamp), and the duel's end if it came.</summary>
        private void EndSkipShow()
        {
            if (!_show.On) return;
            if (_showEnding) EndShow();
            if (_show.State != ComboShow.Phase.None) _view.SetComboMeter(_show);
            _showHoldUntil = 0;
            if (_show.Live != _showSlowed) { _showSlowed = _show.Live; UpdateControls(); }
        }

        /// <summary>A line from the alley's voice: the headliner's own pool first ("baz:combo:mass-driver-moss"), else everyone's.</summary>
        private string? ShowCall(string hook, Dictionary<string, string> fill)
        {
            var line = _campaign.AlleyLine($"{hook}:{_show.Id}", _show.Handle, fill, ShowOnlyDuel ? _showVoiceTurn++ : null);
            return line is { } l ? $"{l.speaker}: “{l.text}”" : null;
        }

        private Dictionary<string, string> ShowFill(bool loud)
        {
            var fill = _campaign.Profile != null ? BackAlley.FillFor(_campaign.Profile, _campaign.AlleyData, null, null, _cardPack!) : new Dictionary<string, string>();
            fill["handle"] = loud ? _show.Short.ToUpperInvariant() : _show.Short;
            fill["step"] = _show.FizzledAt.ToString();
            return fill;
        }

        /// <summary>The headliner's own line for a moment ("combo", "fizzle"), in a bubble by its portrait.</summary>
        private void HeadlinerSays(string kind)
        {
            var who = _players.Length > _show.Seat && _show.Seat >= 0 ? _players[_show.Seat] : null;
            if (who == null || !who.Lines.TryGetValue(kind, out var lines) || lines.Count == 0) return;
            var line = lines[_reactionRng.Next(lines.Count)];
            _view.React(_show.Seat, kind, line, 0.4f);
            _view.AppendLog($"{who.Name}: \"{line}\"");
        }

        /// <summary>How long a banner or stamp stays: its full time at Normal speed, shorter when the duel runs faster.</summary>
        private float ShowSeconds(float full) => Mathf.Max(full * 0.45f, full * Mathf.Max(_view.Fx.Tempo, 0.45f));

        private void Stage(ComboShow.Moment moment)
        {
            switch (moment)
            {
                case ComboShow.Moment.Started:
                {
                    var call = ShowCall("baz:combo", ShowFill(loud: true));
                    if (call != null) { _showCalls.Add(call); _view.AppendLog(call); }
                    _view.ShowComboBanner(_show.Short, _show.Route, call ?? "", ShowSeconds(3.2f));
                    _view.AppendLog($"{_show.Handle} is going for it: {_show.Route}.");
                    _view.SetComboMeter(_show);
                    Sounds.Play(ShowSounds.Sting);
                    HeadlinerSays("combo");
                    _showLog.Add($"started {_show.Route}");
                    break;
                }
                case ComboShow.Moment.Step:
                    _view.SetComboMeter(_show);
                    break;
                case ComboShow.Moment.Payoff:
                {
                    var call = ShowCall("baz:payoff", ShowFill(loud: false));
                    if (call != null) { _showCalls.Add(call); _view.AppendLog(call); }
                    _view.SetComboMeter(_show);
                    float hold = ShowSeconds(2.2f);
                    _view.ShowComboStamp(true, $"{_show.Handle} · {_show.PayoffName}", call != null ? new[] { call } : Array.Empty<string>(), hold);
                    // The camera holds: the payoff has just resolved (the playbook confirms it then), and the duel waits out the stamp.
                    _showHoldUntil = Time.unscaledTime + hold * 0.8f;
                    Sounds.Play(ShowSounds.Roar);
                    HeadlinerSays("payoff");
                    _showLog.Add("payoff");
                    break;
                }
                case ComboShow.Moment.Finished:
                    _view.SetComboMeter(_show);
                    _showLog.Add("finished");
                    break;
                case ComboShow.Moment.Fizzled:
                {
                    // Baz and Mika both react (two lines of the pool, different speakers when the pool has them).
                    var fill = ShowFill(loud: false);
                    var calls = new List<string>();
                    var speakers = new HashSet<string>();
                    for (int tries = 0; tries < 4 && calls.Count < 2; tries++)
                    {
                        var line = _campaign.AlleyLine($"baz:fizzle:{_show.Id}", _show.Handle, fill, ShowOnlyDuel ? _showVoiceTurn++ : null);
                        if (line is not { } l) break;
                        if (speakers.Add(l.speaker)) calls.Add($"{l.speaker}: “{l.text}”");
                    }
                    foreach (var c in calls) { _showCalls.Add(c); _view.AppendLog(c); }
                    _view.SetComboMeter(_show);
                    _view.ShowComboStamp(false, $"{_show.Handle}'s {_show.Route} stopped at step {_show.FizzledAt} of {_show.Steps}", calls, ShowSeconds(3f));
                    _view.AppendLog($"{_show.Handle}'s {_show.Route} fizzled at step {_show.FizzledAt}.");
                    Sounds.Play(ShowSounds.Groan);
                    HeadlinerSays("fizzle");
                    _showLog.Add($"fizzled at {_show.FizzledAt}");
                    break;
                }
            }
        }

        /// <summary>The duel ended: the frame's last moments are staged (<see cref="StepShow"/>), then the route still on stops there.</summary>
        private void ShowDuelOver() => _showEnding = true;

        private bool _showEnding;

        /// <summary>After the last moments: a route still on finished if it paid off; the meter says so; no hold, no slow-down.</summary>
        private void EndShow()
        {
            _showEnding = false;
            // Signals held behind a payoff beat (its route's end) are staged now: the duel won't step again.
            foreach (var moment in _show.Take(Array.Empty<Ygo.Engine.Presentation.DuelEvent>(), _board.Turn, new[] { _board.Players[0].LifePoints, _board.Players[1].LifePoints }, holdAtPayoff: false)) Stage(moment);
            if (_show.State == ComboShow.Phase.None) return;
            _show.DuelOver();
            _view.SetComboMeter(_show);
            _showHoldUntil = 0;
        }

        /// <summary>
        /// The Combo Breaker's test for this duel: the headliner across the table had a route stopped by you (a chain link of
        /// yours while it was live, in its own turn) before its payoff (a negated payoff counts), and none landed
        /// (<see cref="ComboWatch.Stopped"/>). The engine counts a fizzle at the playbook's next decision, or as the duel
        /// ends with the route still live (you interrupt it and win, or the headliner concedes, before it decides again).
        /// </summary>
        private bool ComboBroken => _comboWatch.Stopped;

        // ---- The exhibition (Baz's chalkboard) and Watch the combo (an Unlisted page) ----------------------------------------

        /// <summary>An exhibition side by its chalkboard key: "hl:&lt;id&gt;" a headliner, "rg:&lt;table&gt;.&lt;seat&gt;" tonight's regular.</summary>
        private AlleyOpponent? ExhibitionSide(string key)
        {
            var profile = _campaign.Profile;
            var data = _campaign.AlleyData;
            if (profile == null || _campaign.AlleyContext == null) return null;
            if (key.StartsWith("hl:"))
            {
                var h = data.FindHeadliner(key.Substring(3));
                var table = data.Tables.FirstOrDefault(t => t.Headliners.Contains(h?.Id ?? ""));
                return h == null ? null : new AlleyOpponent { Table = table?.Id ?? "exhibition", Seat = 0, Kind = "headliner", Character = h.Id, Handle = h.Handle, Base = h.Deck };
            }
            if (!key.StartsWith("rg:")) return null;
            var dot = key.LastIndexOf('.');
            if (dot < 0 || !int.TryParse(key.Substring(dot + 1), out var seat)) return null;
            var tableId = key.Substring(3, dot - 3);
            return profile.AlleyOpponents.FirstOrDefault(o => o.Table == tableId && o.Seat == seat && o.Kind == "regular");
        }

        /// <summary>
        /// The exhibition: two sides from the chalkboard, both played by their bots in the duel view (a coin toss between them),
        /// Baz and Mika calling it. Nothing is held, paid or recorded.
        /// </summary>
        private void StartExhibition(string left, string right)
        {
            // Not over a live alley duel (starting one would settle that duel as a loss): the board's Menu settles it first.
            if (AlleyDuelLive) { _campaignScreen.OpenChalkboard("Finish the duel at your table first: Baz doesn't run a show over a live game."); return; }
            var ctx = _campaign.AlleyContext;
            var a = ExhibitionSide(left);
            var b = ExhibitionSide(right);
            if (ctx == null || a == null || b == null) { _campaignScreen.OpenChalkboard("That exhibition can't be set up."); return; }
            var who = new[] { BackAlley.PersonalityOf(a, ctx), BackAlley.PersonalityOf(b, ctx) };
            var decks = new[] { BackAlley.DeckOf(a, ctx), BackAlley.DeckOf(b, ctx) };
            UiTheme.Current = UiTheme.BackAlley;
            _campaignScreen.Close();
            seed++;
            var show = new ShowDuel { Kind = "exhibition", Title = $"Exhibition  <color=#B8C0D0>·  {a.Handle} vs {b.Handle} · watch only</color>", Who = who, Decks = decks };
            // Baz opens it, as he does a bracket's round (the exhibition has no pool of its own: his combo pool's general line).
            _view.AppendLog($"Exhibition: {a.Handle} against {b.Handle}. No stakes, no points: just the show.");
            CoinToss(a.Handle, b.Handle, Playbooks.RecipeOf(decks[0]), Playbooks.RecipeOf(decks[1]), () =>
            {
                _showDuelNext = show;
                _pickCharacter[0] = Math.Max(0, _roster.FindIndex(p => p.Id == who[0].Id));
                _pickCharacter[1] = Math.Max(0, _roster.FindIndex(p => p.Id == who[1].Id));
                _paused = false;
                NewDuel();
                UpdateControls();
            }, youChoose: false);
        }

        /// <summary>
        /// Watch the combo: the headliner's goldfish clip (its saved replay's seed, first player and decks), played again live,
        /// the headliner's bot against the stand-in that only sets a monster, as `--combo-goldfish` played it, so the banner,
        /// the meter and the payoff play with it. Without a saved clip, the goldfish game Phase 1 recorded as finishing.
        /// </summary>
        private void WatchCombo(string headliner)
        {
            if (AlleyDuelLive) { _campaignScreen.Open("Finish the duel at your table first."); return; }
            var ctx = _campaign.AlleyContext;
            var h = _campaign.AlleyData.FindHeadliner(headliner);
            if (ctx == null || h == null) { _campaignScreen.Open("That combo has no clip yet."); return; }
            var o = new AlleyOpponent { Table = "clip", Seat = 0, Kind = "headliner", Character = h.Id, Handle = h.Handle, Base = h.Deck };
            var deck = BackAlley.DeckOf(o, ctx);
            var replay = HeadlinerReplays.Load(FindDataFolder("campaign", "campaign"), h.Id);
            // The goldfish game: its seed (the bot's too; the stand-in's is one more) and who went first.
            int s = replay != null ? (int)replay.Settings.Seed : ClipSeeds.TryGetValue(h.Id, out var known) ? known : 7919;
            int first = replay?.Settings.FirstPlayer ?? 0;
            var decks = new[] { replay?.Decks[0].Clone() ?? deck, replay?.Decks[1].Clone() ?? _library.Load("classic") };
            decks[0].Name = deck.Name;
            decks[0].Notes["recipe"] = Playbooks.RecipeOf(deck);
            decks[1].Name = "The stand-in";
            var who = new[] { BackAlley.PersonalityOf(o, ctx), new Personality { Id = "alley-stand-in", Name = "The stand-in", Title = "sets a monster, passes" } };
            var cards = _cardPack!;
            _showDuelNext = new ShowDuel
            {
                Kind = "clip", Headliner = h.Id, Replay = replay, Who = who, Decks = decks,
                Title = $"Watch the combo  <color=#B8C0D0>·  {h.Handle} · an empty board</color>",
                Bot = (seat, context) =>
                {
                    if (seat == 1) return new DummyDuelist(s + 1, cards);
                    var book = Playbooks.ForDeck(decks[0]);
                    WatchRoutes(0, book, context);
                    return new SmartDuelist("Headliner", s, context, playbook: book);
                },
            };
            UiTheme.Current = UiTheme.BackAlley;
            _campaignScreen.Close();
            seed = s;
            _nextFirstPlayer = first;
            _pickCharacter[0] = _pickCharacter[1] = 0;
            _paused = false;
            NewDuel();
            UpdateControls();
        }

        /// <summary>Phase 1's goldfish games that finish (docs/dev/HEADLINERS, "Phase 1 results"): `--combo-goldfish --games 1 --seed n` is seed n × 7919.</summary>
        private static readonly Dictionary<string, int> ClipSeeds = new Dictionary<string, int>
        {
            ["mass-driver-moss"] = 8 * 7919,
            ["yata-mags"] = 37 * 7919,
        };

        /// <summary>The end of an exhibition or a clip: who won, Baz's word, then back to the chalkboard or the page. False otherwise.</summary>
        private bool FinishShowDuel()
        {
            if (_showDuel is not ShowDuel show || _watching != null) return false;
            int winner = _board.Winner;
            string line = show.Kind == "clip"
                ? _show.Payoffs > 0 ? $"{_players[0].Name}'s {_show.Route} from start to payoff." : $"{_players[0].Name} didn't get there this time."
                : winner > 1 ? "A draw. Baz calls it a wash." : $"{_players[winner].Name} takes the exhibition.";
            var headline = winner > 1 ? "Draw!" : $"{_players[winner].Name} wins!";
            _view.SetResult(headline, winner <= 1 ? _players[winner].WinLine : "", line + "   ·   watch only: nothing won or lost");
            _view.AppendLog(line);
            StartCoroutine(AfterShowDuel(show, line));
            return true;
        }

        private System.Collections.IEnumerator AfterShowDuel(ShowDuel show, string line)
        {
            yield return new WaitForSecondsRealtime(3f);
            if (_showDuel != show || _editor.IsOpen || _shop.IsOpen || _menu.IsOpen) yield break;
            if (show.Kind == "clip") _campaignScreen.OpenUnlistedPage(show.Headliner, line);
            else _campaignScreen.OpenChalkboard(line);
        }
    }
}
