using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Campaign;
using Ygo.Engine.Replays;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The headliners' show in the checks (ALLEY-SPEC Phase D; HEADLINERS Phase 2's "done when"): `-alleycheck` steps 16-18
    /// play Mass Driver Moss at The Basement Game's fourth seat, once through a fizzle (Mask of Restrict on your field: the
    /// shot can't Tribute, so the route stops at its payoff step; Moss then concedes and the Combo Breaker poster is yours),
    /// once as an encore through a negated payoff (16b: your Dust Tornado on Mass Driver as its first shot resolves, so the
    /// shot never lands: a fizzle at the payoff step, the Combo Breaker's test passed, no second poster) and once, as an
    /// encore, through its payoff; then the exhibition on Baz's chalkboard and Watch the combo on Moss's
    /// Unlisted page. `-themecheck` shoots the banner, the meter, the payoff and the fizzle in both themes
    /// (<see cref="ShowThemeShots"/>). Moss's hand is set for the line (the pile check's set-up): the route starts on turn 1.
    /// </summary>
    public sealed partial class DuelController
    {
        private const uint MassDriver = 34906152, Substitoad = 20663556, SwapFrog = 9126351, Ronintoadin = 1357146, TreebornFrog = 12538374,
            MaskOfRestrict = 29549364, DustTornado = 60082869;

        /// <summary>For 16b: your answer went on Moss's payoff (the chain's top link was Mass Driver's shot).</summary>
        private bool _checkAnswerOnPayoff;

        /// <summary>Moss's opening hand for the line: Mass Driver, Substitoad, Swap Frog and two WATER Frogs to discard and fuel the loop.</summary>
        private static List<PlacedCard> MossHand() => new List<PlacedCard>
        {
            new PlacedCard(1, MassDriver, Location.Hand), new PlacedCard(1, Substitoad, Location.Hand), new PlacedCard(1, SwapFrog, Location.Hand),
            new PlacedCard(1, Ronintoadin, Location.Hand), new PlacedCard(1, TreebornFrog, Location.Hand),
        };

        private IEnumerator HeadlinerShowCheck(Action<string, bool> Report, Func<string, IEnumerator> Shot, Func<Func<bool>, float, IEnumerator> Until)
        {
            var profile = _campaign.Profile!;
            var data = _campaign.AlleyData;
            const string id = "mass-driver-moss";
            int basement = data.IndexOf("basement"), dumpster = data.IndexOf("dumpster-row");
            var moss = data.FindHeadliner(id);
            if (basement < 0 || dumpster < 0 || moss == null || !moss.Ready)
            {
                Report("16. Mass Driver Moss is ready at The Basement Game", false);
                yield break;
            }
            // The Basement Game open (Dumpster Row cleared) and tonight's seats 1-3 won: seat 4, its closer, is next.
            foreach (var tid in new[] { data.Tables[dumpster].Id, data.Tables[basement].Id })
                if (!profile.AlleyTables.ContainsKey(tid)) profile.AlleyTables[tid] = new AlleyTableState();
            profile.AlleyTables[data.Tables[dumpster].Id].Cleared = true;
            profile.AlleyTables[data.Tables[basement].Id].WonMask = 0b0111;
            profile.Earn(Ledger.Debug, 10_000);
            foreach (var trigger in new[] { "headliner:" + id, "open:" + data.Tables[basement].Name })
                foreach (var scene in _story.Pending(profile, new[] { trigger })) Story.MarkSeen(profile, scene);
            _campaign.Save();
            var seat4 = _campaign.AlleySeat(basement, 4);
            // The Basement Game has two headliners (Moss and Gateway Gwen): if tonight's closer is the other, it's beaten
            // (recorded, no duel) and the next night's closer is Moss, the one left unbeaten.
            if (seat4 != null && seat4.IsHeadliner && seat4.Character != id)
            {
                BackAlley.Record(profile, data, basement, 0, 5, 0, _campaign.AlleyContext!);
                profile.AlleyTables[data.Tables[basement].Id].WonMask = 0b0111;
                _campaign.Save();
                seat4 = _campaign.AlleySeat(basement, 4);
            }
            Report($"16. The Basement Game's seat 4 is Mass Driver Moss ({seat4?.Kind} {seat4?.Handle}), next up",
                seat4 != null && seat4.IsHeadliner && seat4.Character == id && BackAlley.NextSeat(profile, data, basement) == 4);
            if (seat4 == null || seat4.Character != id) yield break;

            int speedBefore = speedIndex;
            var slowBefore = FxSettings.SlowForCombosOverride;
            FxSettings.SlowForCombosOverride = true;

            // ---- 16. A fizzle: Mask of Restrict face-up on your field ----
            IEnumerator Play(string? encore, bool restrict, uint answer = 0)
            {
                _scenes.LastResults = null;
                _campaignScreen.Close();
                ShowAlleyVersus(basement, encore);
                yield return new WaitForSecondsRealtime(0.4f);
                _checkFirstPlayer = 1; // Moss wins the toss and goes first, as a headliner would
                _checkSetup = MossHand();
                // Set face-down: you activate it while Moss's route plays (a chain link of yours: you stopped it, not the draw).
                if (restrict) _checkSetup.Add(new PlacedCard(0, MaskOfRestrict, Location.SZone, 2, Ygo.Engine.Position.FaceDownAttack | Ygo.Engine.Position.FaceDownDefense));
                if (answer != 0) _checkSetup.Add(new PlacedCard(0, answer, Location.SZone, 2, Ygo.Engine.Position.FaceDownAttack | Ygo.Engine.Position.FaceDownDefense));
                speedIndex = Speeds.Length - 1; // 60 events a second: the route must still play at Normal speed
                if (_scenes.Buttons.TryGetValue("Duel!", out var duel)) duel();
                yield return Until(() => _runner != null && !_runner.IsFinished && _alleyOpp?.Character == id, 20);
                _checkFirstPlayer = 0;
                UpdateControls();
            }

            var played0 = Sounds.Played.ToDictionary(p => p.Key, p => p.Value);
            int Played(string sound) => Sounds.Played.GetValueOrDefault(sound) - played0.GetValueOrDefault(sound);
            // Moss's deck can bounce the Mask (Giant Trunade, a draw away): then its combo lands and the duel is his, the seat
            // stays, and the fizzle is tried again with the next duel's seed (at most four tries).
            bool started = false, bannerAnimated = false;
            string banner = "", meterMid = "";
            int tries = 0;
            for (; tries < 4; tries++)
            {
                yield return Play(null, restrict: true);
                started = _runner != null && _alleyOpp?.Character == id;
                var clicker = StartCoroutine(ClickFor(MaskOfRestrict));
                yield return Until(() => _view.ComboBannerShowing || _announcedResult, 90);
                yield return Shot("16-banner");
                banner = _view.ComboBannerText;
                bannerAnimated = _view.ComboBannerAnimated;
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Shot("16-meter");
                meterMid = _view.ComboMeterText;
                yield return Until(() => _showLog.Any(l => l.StartsWith("fizzled")) || _showLog.Contains("payoff") || _announcedResult, 120);
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Shot("16-fizzle");
                StopCoroutine(clicker);
                if (_showLog.Any(l => l.StartsWith("fizzled")) || !started) break;
                Debug.Log($"alley check: (16. try {tries + 1}: Moss's combo got past the Mask: {string.Join(", ", _showLog)}; trying again)");
                yield return Until(() => _announcedResult, 240);
                yield return Until(() => _scenes.IsOpen, 8);
                _scenes.Close();
            }
            var log16 = _showLog.ToList();
            var calls16 = _showCalls.ToList();
            var meter16 = _view.ComboMeterText;
            var stamp16 = _view.ComboStampText;
            float slowest = _showSpeedDuringRoute;
            Report($"16. Moss's route starts on turn 1 (try {tries + 1}): the banner reads \"{banner}\" ({(bannerAnimated ? "sweeping in" : "still")}), the meter \"{meterMid}\", the sting played {Played(ShowSounds.Sting)}×",
                started && banner == "MOSS IS GOING FOR IT: FROG FTK" && meterMid.StartsWith("step ") && meterMid.Contains(" of ") && Sounds.HasEffect(ShowSounds.Sting));
            // Baz's combo pool has Mika's stream line too (the pool plays in turn, so a retry may get hers).
            Report($"16. The call: {calls16.FirstOrDefault()}", calls16.Count > 0 && (calls16[0].StartsWith("Baz: ") || calls16[0].StartsWith("Mika: ")));
            Report($"16. Slow down for combos: the route ran at {slowest:0.#} events/s at most with the duel set to {Speeds[^1]:0}/s",
                slowest > 0 && slowest <= Speeds[NormalSpeed]);
            var fizzleCalls = calls16.Skip(1).ToList();
            Report($"16. Mask of Restrict stops the shot: \"{meter16}\", the stamp \"{stamp16}\"; {string.Join(" / ", fizzleCalls)}; the crowd groaned {Played(ShowSounds.Groan)}×",
                log16.Contains("started Frog FTK") && log16.Any(l => l.StartsWith("fizzled at ")) && !log16.Contains("payoff")
                && meter16.StartsWith("fizzled at step") && stamp16.StartsWith("FIZZLED") && fizzleCalls.Count >= 2
                && fizzleCalls.Select(c => c.Split(':')[0]).Distinct().Count() >= 2 && Sounds.HasEffect(ShowSounds.Groan));
            // The engine counts the fizzle at Moss's next decision: the duel plays on (your side only passing) until it has, or a
            // while; then Moss gives up the duel: your win, with the route stopped before its payoff.
            var passer = StartCoroutine(ClickFor(MaskOfRestrict));
            yield return Until(() => _comboWatch.Fizzled > 0 || _announcedResult, 30);
            StopCoroutine(passer);
            bool engineSaw = _comboWatch.Stopped;
            bool broken = ComboBroken;
            if (_runner != null && !_runner.IsFinished)
            {
                _runner.Concede(1, DuelRunner.ReasonConceded);
                _paused = false;
                _answerReady = true;
            }
            yield return Until(() => _announcedResult, 30);
            yield return Until(() => _scenes.IsOpen, 8);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("16-results");
            var unlocks = _scenes.LastResults?.Unlocks ?? new List<string>();
            var poster = Posters.BreakerFor(id);
            bool earned = poster != null && poster.Earned != null && poster.Earned(_campaign.Circuits, profile);
            Report($"16. Moss concedes: your win earns the Combo Breaker (the engine's ComboWatch.Stopped {engineSaw}, {_comboWatch.StoppedFizzles} fizzle(s) you caused; \"{unlocks.FirstOrDefault(u => u.StartsWith("Combo Breaker"))}\"; poster {poster?.Id} earned {earned})",
                broken && _board.Winner == 0 && profile.Headliners.TryGetValue(id, out var hs) && hs.Breaker > 0 && hs.Beaten > 0 && earned
                && unlocks.Any(u => u.StartsWith("Combo Breaker")));
            _scenes.Close();

            // ---- 16b. A negated payoff: an encore, Dust Tornado Set; you play it only on Mass Driver's first shot (the route's
            // payoff), destroying Mass Driver before the shot resolves. The shot never lands: no PAYOFF! stamp, a fizzle at the
            // payoff step, and the Combo Breaker's test passes (an encore earns no second poster). Moss's deck can bounce the
            // Set card first (Giant Trunade): then the duel is tried again (at most four tries).
            foreach (var scene in _story.Pending(profile, new[] { "encore", BackAlley.ComboBreakerEvent + id, "headlinerbeat:" + id })) Story.MarkSeen(profile, scene);
            _campaign.Save();
            int breakerBefore = profile.Headliners.TryGetValue(id, out var hsb) ? hsb.Breaker : 0;
            var log16b = new List<string>();
            string stamp16b = "", meter16b = "";
            bool negated16b = false, watch16b = false, broken16b = false;
            int paid16b = -1, tries16b = 0;
            for (; tries16b < 4; tries16b++)
            {
                _checkAnswerOnPayoff = false;
                yield return Play(id, restrict: false, answer: DustTornado);
                var sniper = StartCoroutine(ClickAtPayoff(DustTornado, MassDriver));
                yield return Until(() => _showLog.Any(l => l.StartsWith("fizzled")) || _showLog.Contains("payoff") || _announcedResult, 150);
                yield return new WaitForSecondsRealtime(0.3f);
                log16b = _showLog.ToList();
                stamp16b = _view.ComboStampText;
                meter16b = _view.ComboMeterText;
                if (_checkAnswerOnPayoff && !log16b.Contains("payoff")) yield return Shot("16b-negated");
                yield return Until(() => _comboWatch.Fizzled > 0 || _announcedResult, 20);
                StopCoroutine(sniper);
                negated16b = _checkAnswerOnPayoff && !log16b.Contains("payoff") && log16b.Contains("started Frog FTK") && log16b.Contains($"fizzled at {_show.Steps}");
                // The duel's end (Moss concedes) counts a route you stopped that the playbook hadn't ended yet.
                if (_runner != null && !_runner.IsFinished)
                {
                    _runner.Concede(1, DuelRunner.ReasonConceded);
                    _paused = false;
                    _answerReady = true;
                }
                yield return Until(() => _announcedResult, 30);
                paid16b = _comboWatch.PaidOff;
                watch16b = _comboWatch.PaidOff == 0 && _comboWatch.Stopped;
                broken16b = ComboBroken;
                yield return Until(() => _scenes.IsOpen, 8);
                if (negated16b) break;
                Debug.Log($"alley check: (16b. try {tries16b + 1}: Dust Tornado didn't meet the shot: {string.Join(", ", log16b)}; trying again)");
                _scenes.Close();
            }
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("16b-results");
            var unlocks16b = _scenes.LastResults?.Unlocks ?? new List<string>();
            int breakerAfter = profile.Headliners.TryGetValue(id, out var hsa) ? hsa.Breaker : -1;
            Report($"16b. A negated payoff (try {tries16b + 1}): Dust Tornado on Mass Driver's first shot; the show \"{meter16b}\", the stamp \"{stamp16b}\" ({string.Join(", ", log16b)}); "
                   + $"ComboWatch paid off {paid16b}, stopped {watch16b}; the Combo Breaker's test {broken16b}; the encore earns no second poster (breaker {breakerBefore} → {breakerAfter})",
                negated16b && !stamp16b.StartsWith("PAYOFF") && stamp16b.StartsWith("FIZZLED") && meter16b.StartsWith("fizzled at step") && watch16b && broken16b
                && _board.Winner == 0 && breakerAfter == breakerBefore && breakerBefore > 0 && !unlocks16b.Any(u => u.StartsWith("Combo Breaker")));
            _scenes.Close();

            // ---- 17. A payoff: Moss's encore, nothing in the way ----
            foreach (var scene in _story.Pending(profile, new[] { "encore", BackAlley.ComboBreakerEvent + id, "headlinerbeat:" + id })) Story.MarkSeen(profile, scene);
            _campaign.Save();
            yield return Play(id, restrict: false);
            bool encore = _alleyOpp?.Seat == 0 && _alleyOpp.IsHeadliner;
            var clicker17 = StartCoroutine(AutoClick());
            bool held = false;
            yield return Until(() => _showLog.Contains("payoff") || _announcedResult, 120);
            held = ShowHolding;
            // Read during the beat: the payoff is staged as it resolves now, so the route's end follows the hold at once.
            var stamp17 = _view.ComboStampText;
            var meterPaid = _view.ComboMeterText;
            yield return Shot("17-payoff");
            yield return Until(() => _announcedResult, 240);
            StopCoroutine(clicker17);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("17-end");
            var log17 = _showLog.ToList();
            var meter17 = _view.ComboMeterText;
            Report($"17. Moss's encore: the payoff beat holds the duel ({(held ? "held" : "not held")}), the stamp \"{stamp17}\", the meter \"{meterPaid}\", the call {_showCalls.Skip(1).FirstOrDefault()}, the crowd roared {Played(ShowSounds.Roar)}×",
                encore && log17.Contains("payoff") && held && stamp17.StartsWith("PAYOFF!") && meterPaid.StartsWith("payoff · step") && _showCalls.Count >= 2
                && Sounds.HasEffect(ShowSounds.Roar));
            Report($"17. The route finished: \"{meter17}\" ({string.Join(", ", log17)}); the duel {(_board.Winner == 1 ? "went to Moss" : $"ended, winner {_board.Winner}")} on turn {_board.Turn}",
                log17.Contains("finished") && meter17.StartsWith("finished")
                // The loop goes on after the route's payoff step: the meter counts it to the end of the turn (all 8,000 for a turn-1 kill).
                && (_board.Winner == 1 && _board.Turn == 1 ? _show.Total >= 8000 : _show.Total > 0));
            yield return Until(() => _scenes.IsOpen, 8);
            _scenes.Close();

            // ---- 18. The exhibition on Baz's chalkboard; Watch the combo on Moss's Unlisted page ----
            FxSettings.SlowForCombosOverride = false;
            int dp = profile.Dp, duels = profile.AlleyDuels;
            var record = profile.Headliners.TryGetValue(id, out var r18) ? (r18.Met, r18.Beaten, r18.Breaker) : (0, 0, 0);
            _campaignScreen.DebugShowAlley(_campaignScreen.DebugChalkboard);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("18-chalkboard");
            // Moss on the left (other headliners met tonight, a wildcard Yata Mags with Return Rook closing, may come first).
            _campaignScreen.DebugPickExhibition("hl:" + id, "");
            var left = _campaignScreen.ExhibitionLeft.ToList();
            var right = _campaignScreen.ExhibitionRight.ToList();
            Report($"18. The chalkboard's exhibition offers {string.Join(", ", left.Select(l => l.label))} against {right.Count} ({string.Join(", ", right.Take(3).Select(x => x.label))}...)",
                left.Any(l => l.key == "hl:" + id) && right.Count > 0 && right.All(x => x.key != "hl:" + id));
            bool picked = right.Count > 0 && _campaignScreen.DebugPickExhibition("hl:" + id, right[0].key);
            _campaignScreen.DebugWatchExhibition();
            yield return Until(() => _runner != null && !_runner.IsFinished && _showDuel?.Kind == "exhibition", 20);
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("18-exhibition");
            bool watching = _showDuel?.Kind == "exhibition" && _human == null && _turnCap != null && _alleyTable == null && profile.AnteLive == null
                            && _players[0].Id == id && _view.DuelTitleText.Contains("Exhibition");
            speedIndex = Speeds.Length - 1;
            UpdateControls();
            yield return Until(() => _announcedResult, 180);
            yield return Until(() => _campaignScreen.IsOpen, 8);
            var after = profile.Headliners.TryGetValue(id, out var r18b) ? (r18b.Met, r18b.Beaten, r18b.Breaker) : (0, 0, 0);
            Report($"18. The exhibition plays CPU against CPU ({_players[0].Name} vs {_players[1].Name}, \"{_view.DuelTitleText}\"); nothing won or lost (DP {dp:N0} → {profile.Dp:N0}, alley duels {duels} → {profile.AlleyDuels})",
                picked && watching && _announcedResult && profile.Dp == dp && profile.AlleyDuels == duels && after == record && profile.AnteLive == null);
            // Watch the combo: Moss's page in the Unlisted binder.
            _campaignScreen.OpenUnlistedPage(id);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("18-unlisted");
            bool button = _campaignScreen.WatchComboShown == id;
            var clip = HeadlinerReplays.Load(FindDataFolder("campaign", "campaign"), id);
            FxSettings.SlowForCombosOverride = true;
            _campaignScreen.DebugWatchCombo();
            yield return Until(() => _runner != null && _showDuel?.Kind == "clip", 10);
            yield return Until(() => _showLog.Contains("payoff") || _announcedResult, 120);
            yield return Shot("18-clip");
            var log18 = _showLog.ToList();
            Report($"18. Watch the combo on Moss's page: {(clip != null ? $"its saved clip (seed {clip.Settings.Seed})" : "Phase 1's goldfish game (no saved clip yet)")} plays the route to its payoff ({string.Join(", ", log18)})",
                button && _showDuel?.Kind == "clip" && log18.Contains($"started {_show.Route}") && log18.Contains("payoff") && _players[1].Name == "The stand-in");
            speedIndex = Speeds.Length - 1;
            FxSettings.SlowForCombosOverride = false;
            UpdateControls();
            yield return Until(() => _announcedResult, 120);
            yield return Until(() => _campaignScreen.IsOpen, 8);
            Report($"18. After the clip, back on Moss's Unlisted page (\"{System.Text.RegularExpressions.Regex.Replace(_campaignScreen.StatusText, "<.*?>", "")}\")",
                _campaignScreen.IsOpen && _campaignScreen.UnlistedPageShown?.Key == "hl:" + id && profile.Dp == dp);
            _campaignScreen.Close();
            speedIndex = speedBefore;
            FxSettings.SlowForCombosOverride = slowBefore;
            UpdateControls();
        }

        /// <summary>
        /// Your side in the fizzle duel: a chance to activate <paramref name="code"/> takes it; other chances to chain are
        /// declined (so nothing else of yours gets in the way); everything else as <see cref="AutoClick"/> does.
        /// </summary>
        private IEnumerator ClickFor(uint code)
        {
            var rng = new System.Random(seed);
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.05f);
                var set = _view.Prompt.Current;
                if (set == null || _runner == null || !_runner.IsWaiting) continue;
                int want = set.Choices.FindIndex(c => c.Card?.Code == code && !c.AlreadySelected);
                if (want >= 0) { _view.Prompt.ClickChoice(want); continue; }
                if (set.IsChain)
                {
                    int no = set.Choices.FindIndex(c => c.Card == null);
                    if (no >= 0) { _view.Prompt.ClickChoice(no); continue; }
                    if (set.CanCancel) { _view.Prompt.CancelSelection(); continue; }
                }
                if (set.Mode == Ygo.Engine.Interactive.ChoiceMode.Multi)
                {
                    foreach (var i in Enumerable.Range(0, set.Choices.Count).OrderBy(_ => rng.Next()).Take(Math.Max(set.Min, 1))) _view.Prompt.ClickChoice(i);
                    _view.Prompt.Confirm();
                    continue;
                }
                var open = Enumerable.Range(0, set.Choices.Count).Where(i => !set.Choices[i].AlreadySelected).ToList();
                _view.Prompt.ClickChoice(open.Count > 0 ? open[rng.Next(open.Count)] : 0);
            }
        }

        /// <summary>
        /// Your side in the negated-payoff duel (16b): <paramref name="answer"/> only when the chain's newest link is Moss's
        /// Mass Driver shot (its effect 0: the route's payoff), aimed at <paramref name="target"/>; every other chance to chain
        /// declined; the rest as <see cref="ClickFor"/> does, never the answer.
        /// </summary>
        private IEnumerator ClickAtPayoff(uint answer, uint target)
        {
            var rng = new System.Random(seed);
            ulong shot = (ulong)MassDriver << 20;
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.05f);
                var set = _view.Prompt.Current;
                if (set == null || _runner == null || !_runner.IsWaiting) continue;
                if (set.IsChain)
                {
                    var top = _narrator?.Chain.Links.LastOrDefault();
                    int want = set.Choices.FindIndex(c => c.Card?.Code == answer);
                    if (want >= 0 && top != null && top.Code == MassDriver && top.Controller == 1 && top.Description == shot)
                    {
                        _checkAnswerOnPayoff = true;
                        _view.Prompt.ClickChoice(want);
                        continue;
                    }
                    int no = set.Choices.FindIndex(c => c.Card == null);
                    if (no >= 0) { _view.Prompt.ClickChoice(no); continue; }
                    if (set.CanCancel) { _view.Prompt.CancelSelection(); continue; }
                }
                // The answer's target: Mass Driver.
                int aim = set.Choices.FindIndex(c => c.Card?.Code == target && !c.AlreadySelected);
                if (aim >= 0 && _checkAnswerOnPayoff)
                {
                    _view.Prompt.ClickChoice(aim);
                    if (set.Mode == Ygo.Engine.Interactive.ChoiceMode.Multi) _view.Prompt.Confirm();
                    continue;
                }
                if (set.Mode == Ygo.Engine.Interactive.ChoiceMode.Multi)
                {
                    foreach (var i in Enumerable.Range(0, set.Choices.Count).OrderBy(_ => rng.Next()).Take(Math.Max(set.Min, 1))) _view.Prompt.ClickChoice(i);
                    _view.Prompt.Confirm();
                    continue;
                }
                var open = Enumerable.Range(0, set.Choices.Count).Where(i => !set.Choices[i].AlreadySelected && set.Choices[i].Card?.Code != answer).ToList();
                _view.Prompt.ClickChoice(open.Count > 0 ? open[rng.Next(open.Count)] : 0);
            }
        }

        // ---- -themecheck: the show's pieces in a theme --------------------------------------------------------------------

        /// <summary>
        /// The banner, the meter with the payoff stamp, and the fizzle, staged from made-up route signals over the board as it
        /// is (no duel needed), in the current theme: "theme-&lt;name&gt;-show-banner.png", "-show-payoff.png", "-show-fizzle.png".
        /// </summary>
        private IEnumerator ShowThemeShots(string name, Func<string, IEnumerator> shot, Action<string, bool> report)
        {
            var moss = _campaign.AlleyData.FindHeadliner("mass-driver-moss");
            if (moss == null) { report($"the {name} theme's show pieces (Mass Driver Moss in the alley's data)", false); yield break; }
            _paused = true;
            UpdateControls();
            // The alley's pass runs under Reduced motion: the banner still shows, without its sweep.
            var reducedBefore = FxSettings.ReducedMotionOverride;
            bool reduced = name == "back-alley";
            FxSettings.ReducedMotionOverride = reduced;
            _show.Reset("", "", moss.Id, moss.Handle);
            _view.ClearCombo();
            int turn = _board.Turn;
            Playbook.RouteSignal Signal(Playbook.RouteEvent kind, int step, string stepName = "", bool finished = false) =>
                new Playbook.RouteSignal { Kind = kind, Route = "Frog FTK", Turn = turn, Step = step, StepName = stepName, Steps = 5, Finished = finished };
            void Stage(params Playbook.RouteSignal[] signals)
            {
                foreach (var s in signals) _show.Enqueue(1, s);
                foreach (var m in _show.Take(Array.Empty<Ygo.Engine.Presentation.DuelEvent>(), turn)) this.Stage(m);
            }
            Stage(Signal(Playbook.RouteEvent.Started, 1, "Swap Frog"), Signal(Playbook.RouteEvent.Step, 1, "Swap Frog"));
            yield return new WaitForSecondsRealtime(0.5f);
            yield return shot($"{name}-show-banner");
            bool banner = _view.ComboBannerShowing && _view.ComboMeterShowing;
            var bannerText = _view.ComboBannerText;
            bool still = !_view.ComboBannerAnimated;
            Stage(Signal(Playbook.RouteEvent.Step, 2, "Substitoad"), Signal(Playbook.RouteEvent.Step, 3, "Mass Driver"),
                Signal(Playbook.RouteEvent.Step, 4, "Mass Driver's first shot with the loop assembled"),
                Signal(Playbook.RouteEvent.Payoff, 4, "Mass Driver's first shot with the loop assembled"));
            yield return new WaitForSecondsRealtime(0.5f);
            yield return shot($"{name}-show-payoff");
            bool payoff = _view.ComboStampText.StartsWith("PAYOFF!") && _view.ComboMeterText.StartsWith("payoff");
            _view.ClearCombo();
            _show.Reset("", "", moss.Id, moss.Handle);
            Stage(Signal(Playbook.RouteEvent.Started, 1, "Swap Frog"), Signal(Playbook.RouteEvent.Step, 1), Signal(Playbook.RouteEvent.Step, 2),
                Signal(Playbook.RouteEvent.Step, 3), Signal(Playbook.RouteEvent.Ended, 3));
            _view.ClearCombo();
            _view.SetComboMeter(_show);
            this.StageFizzleOnly();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return shot($"{name}-show-fizzle");
            bool fizzle = _view.ComboStampText.StartsWith("FIZZLED") && _view.ComboMeterText.StartsWith("fizzled at step 5");
            FxSettings.ReducedMotionOverride = reducedBefore;
            report($"the {name} theme draws the show: the banner \"{bannerText}\" ({(still ? "still, under Reduced motion" : "sweeping in")}), the payoff and the fizzle (\"{_view.ComboMeterText}\")", banner && bannerText == "MOSS IS GOING FOR IT: FROG FTK" && still == reduced && payoff && fizzle);
            _view.ClearCombo();
            _show.Reset("", "", "", "");
        }

        /// <summary>For the theme shots: the fizzle's stamp again over a cleared board (the banner from its start gone).</summary>
        private void StageFizzleOnly() => Stage(ComboShow.Moment.Fizzled);
    }
}
