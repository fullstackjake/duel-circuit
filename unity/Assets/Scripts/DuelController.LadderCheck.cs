using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-laddercheck &lt;folder&gt;` (with -mods &lt;empty folder&gt;; docs/dev/CHAMPIONS-LADDER-SPEC-2026-11-06.md section 9, app 1-9):
    /// the Champion's Ladder as a player meets it. A save past the Every Era final: the plaque locked until Ilse is beaten;
    /// then open (ladder-opens, once), the board at 1920x1080 and 1280x720, rung 1's versus (the Gauntlet's rules, skill
    /// 10/10, a loaner refused), a win and a real conceded duel, Ilse's sitting (her entrance, two wins and a concede), the
    /// top (the holder's entrance, the prize, the scenes), a relaunch, a rematch, New Game+ and a forced visitor. Wins go
    /// through the results path a won duel takes; the concedes are real duels. Every story line is screenshotted, and a
    /// scene that isn't in story.txt yet is reported as missing, not waited for.
    /// </summary>
    public sealed partial class DuelController
    {
        private System.Collections.IEnumerator LadderCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            int shots = 0;
            System.Collections.IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"ladder-{shots++:00}-{name}.png"));
                yield return null;
                yield return null;
            }
            void Report(string what, bool ok) => Debug.Log($"ladder check: {(ok ? "ok  " : "FAIL")} {what}");
            System.Collections.IEnumerator Until(Func<bool> done, float seconds)
            {
                for (float until = Time.realtimeSinceStartup + seconds; !done() && Time.realtimeSinceStartup < until;) yield return null;
            }
            var lines = new List<string>();
            System.Collections.IEnumerator ReadStory(string when)
            {
                var deadline = Time.realtimeSinceStartup + 120;
                string last = "";
                while (_storyScreen.IsOpen && Time.realtimeSinceStartup < deadline)
                {
                    if (_storyScreen.DebugText != last)
                    {
                        last = _storyScreen.DebugText;
                        lines.Add($"{when}: {last}");
                        Debug.Log($"ladder check: story ({when}) {last}");
                        yield return new WaitForSecondsRealtime(2.6f);
                        yield return Shot($"{when}-line");
                    }
                    _storyScreen.DebugAdvance();
                    yield return new WaitForSecondsRealtime(0.4f);
                }
            }
            // A scene the check reads through: shown, or reported as missing from story.txt (the writer's) rather than waited for.
            bool HasScene(string id) => _story.Scenes.Any(sc => sc.Id == id);
            string SceneNote(string id) => HasScene(id) ? "" : " (no such scene in story.txt yet)";
            System.Collections.IEnumerator ResultsThenStory(string when, bool shoot)
            {
                yield return Until(() => _scenes.IsOpen && _scenes.Buttons.ContainsKey("Continue"), 8);
                yield return new WaitForSecondsRealtime(0.6f);
                if (shoot) yield return Shot($"{when}-results");
                bool hadContinue = _scenes.Buttons.TryGetValue("Continue", out var next);
                next?.Invoke();
                yield return Until(() => _storyScreen.IsOpen || _campaignScreen.IsOpen, 3);
                yield return ReadStory(when);
                yield return Until(() => _campaignScreen.IsOpen, 4);
                Report($"{when}: the results screen's Continue goes back to the campaign", hadContinue && _campaignScreen.IsOpen);
            }
            // Duel pressed on a row; its scene (when there is one) read through; then the versus.
            System.Collections.IEnumerator PressDuel(string key, string when)
            {
                _campaignScreen.DebugShowLadder();
                yield return new WaitForSecondsRealtime(0.3f);
                _campaignScreen.DebugLadderDuel(key);
                yield return Until(() => _storyScreen.IsOpen || _scenes.IsOpen && _scenes.Buttons.ContainsKey("Duel!"), 6);
                if (_storyScreen.IsOpen) yield return ReadStory(when);
                yield return Until(() => _scenes.IsOpen && _scenes.Buttons.ContainsKey("Duel!"), 6);
                yield return new WaitForSecondsRealtime(0.5f);
            }
            bool YourTurnWaits() => _runner != null && _runner.IsWaiting && _runner.WaitingFor == 0 && _human?.Current != null;
            // Duel! on the versus shown, a real duel to your first decision, then a concede and its results.
            System.Collections.IEnumerator ConcedeFromVersus(string when)
            {
                if (_scenes.Buttons.TryGetValue("Duel!", out var duel)) duel();
                yield return Until(YourTurnWaits, 30);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot($"{when}-duel");
                var live = Profile.Load(_campaign.SavePath)?.LiveDuel;
                bool dueling = _ladderDuel != null && _runner != null && !_campaignScreen.IsOpen && live is { Kind: LiveDuels.Ladder };
                ConcedeDuel();
                yield return Until(() => _announcedResult, 20);
                Report($"{when}: a real ladder duel, live in the save while it's played ({live?.Kind} {live?.Guest}), and conceding ends it",
                    dueling && _announcedResult && _board.Winner == 1);
                yield return ResultsThenStory(when, true);
            }
            string Plain(string s) => Regex.Replace(s, "<.*?>", "");
            yield return new WaitForSecondsRealtime(0.5f);

            // A champion's save: every cup and the final won, nothing seen but the ladder's scenes still to come.
            _campaign.Start(CampaignState.Starters[1]);
            var profile = _campaign.Profile!;
            var lib = _campaign.Library;
            var ladder = _campaign.Ladder;
            profile.LibraryOpen = true;
            DebugSetDp(profile, 400_000);
            profile.Exp = 50_000_000; // no level-ups in the way of the scenes
            for (int c = 0; c < lib.Cups.Count; c++)
                for (int m = 0; m < lib.Cups[c].Matches.Count; m++) profile.LibraryWins[lib.Key(c, m)] = 1;
            for (int r = 0; r < lib.Rounds.Count; r++) profile.LibraryWins[lib.FinalKey(r)] = 1;
            foreach (var sc in _story.Scenes.Where(sc => !ladder.IsTrigger(sc.Trigger))) Story.MarkSeen(profile, sc);
            // A deck of yours that can climb: Make it yours on the top's deck, legal under the Gauntlet's list (the starter may not be).
            var climbRules = _campaign.LadderRules;
            var quote = _campaign.QuoteOwn(Loaners.NameFor(ladder.Rungs.Count > 0 ? ladder.Rungs[^1].Deck : "chaos-ritual"), climbRules);
            var owned = quote == null ? null : _campaign.Own(quote, climbRules);
            if (owned != null) profile.Deck = owned;
            _campaign.Save();
            Report($"Make it yours builds a deck of yours that can climb ({owned}: {_campaign.LadderProblems(owned ?? profile.Deck).FirstOrDefault()?.Message ?? "legal"})",
                owned != null && _campaign.LadderProblems(owned).Count == 0);
            Report($"the ladder is loaded: {ladder.Rungs.Count} rungs, {ladder.Visitors.Count} visitors{(ladder.Problems.Count > 0 ? $"; problems: {string.Join("; ", ladder.Problems)}" : "")}",
                ladder.Rungs.Count == 10 && ladder.Visitors.Count == 4 && ladder.Problems.Count == 0 && lib.FinalCleared(profile));

            // 1. Past the final, no win over Ilse: the plaque is there, locked, and says why.
            bool shown = _campaignScreen.DebugShowLadder();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("plaque-locked");
            var plaque = _campaignScreen.DebugLadderPlaque;
            Report($"1. past the final with no win over Ilse, the plaque is on the shelf and locked (\"{plaque.line}\"), the board has no Duel",
                shown && plaque.shown && plaque.line == "Locked: Sit across from Duchess Ilse first (Campaign > Guests)"
                && !_campaignScreen.LadderRows.Any(r => r.EndsWith("| duel")) && !ladder.IsOpen(profile, lib));

            // 2. Ilse beaten once (at her table) and the campaign opened: ladder-opens plays, once.
            _campaignScreen.Close();
            profile.Wins["ilse"] = 1;
            _campaign.Save();
            _campaignScreen.Open();
            yield return Until(() => _storyScreen.IsOpen, 3);
            yield return ReadStory("opens");
            yield return Until(() => _campaignScreen.IsOpen, 4);
            bool opensSeen = profile.Seen.Contains(Ladder.OpensScene);
            _campaignScreen.Close();
            _campaignScreen.Open();
            yield return new WaitForSecondsRealtime(1.2f);
            Report($"2. with Ilse beaten, the next Open plays {Ladder.OpensScene}{SceneNote(Ladder.OpensScene)}, and the one after plays nothing",
                opensSeen && !_storyScreen.IsOpen && !_campaign.LadderDue && ladder.IsOpen(profile, lib));

            // 3. The board: ten rungs with you at the foot, inside the panel and the screen, at 1920x1080 and at 1280x720.
            var rects = new List<string>();
            bool fits = true;
            // The window as it was, put back after (Unity keeps the last size for the next launch, other checks included).
            int width0 = Screen.width, height0 = Screen.height;
            var mode0 = Screen.fullScreenMode;
            var rows = new List<string>();
            var tallest = new List<string>();
            bool clear = true;
            try
            {
                foreach (var (w, h) in new[] { (1920, 1080), (1280, 720) })
                {
                    Screen.SetResolution(w, h, FullScreenMode.Windowed);
                    yield return new WaitForSecondsRealtime(1.2f);
                    _campaignScreen.DebugShowLadder();
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return Shot($"board-{h}");
                    rows = _campaignScreen.LadderRows.ToList();
                    var d = _campaignScreen.DebugDetailRect;
                    rects.Add($"{Screen.width}x{Screen.height}: panel {d.xMin:0},{d.yMin:0} to {d.xMax:0},{d.yMax:0}");
                    fits &= d.xMin >= -1 && d.yMin >= -1 && d.xMax <= Screen.width + 1 && d.yMax <= Screen.height + 1;
                    // The tallest board: a visitor forced at climbed 0 (11 rows and the foot), its bottom row and YOUR DECK inside
                    // the panel, above the status line, and clear of any Back button.
                    ladder.VisitorsForced = true;
                    _campaignScreen.DebugShowLadder();
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return Shot($"board-tallest-{h}");
                    var g = _campaignScreen.DebugLadderGeometry();
                    int rowCount = _campaignScreen.LadderRows.Count(r => r.StartsWith("rung ") || r.StartsWith("visitor "));
                    bool inside = g.lastRow.yMin >= g.panel.yMin - 1 && g.contentBottom >= g.panel.yMin - 1 && g.status.yMax <= g.contentBottom + 1
                                  && !g.backs.Any(b => b.Overlaps(g.lastRow) || b.Overlaps(new Rect(g.panel.xMin, g.contentBottom, g.panel.width, g.panel.yMax - g.contentBottom)));
                    tallest.Add($"{Screen.width}x{Screen.height}: {rowCount} rows, last row bottom {g.lastRow.yMin:0}, YOUR DECK bottom {g.contentBottom:0}, panel bottom {g.panel.yMin:0}, status top {g.status.yMax:0}, {g.backs.Count} Back");
                    clear &= inside && rowCount == 11;
                    ladder.VisitorsForced = false;
                }
            }
            finally
            {
                // Every way out of this step (an exception included) puts the window back and the visitors away.
                ladder.VisitorsForced = false;
                Screen.SetResolution(width0, height0, mode0);
            }
            yield return new WaitForSecondsRealtime(1.0f);
            Report($"3. the tallest board (a visitor at the foot) stays inside the panel and clear of the status line and Back ({string.Join("; ", tallest)})", clear);
            Report($"3. the board shows the 10 rungs top first with you at the foot ({string.Join(" / ", rows.Take(2))} ... {rows.LastOrDefault()}); the panel fits the screen ({string.Join("; ", rects)})",
                rows.Count(r => r.StartsWith("rung ")) == 10 && rows[0].StartsWith("rung 10 |") && rows[9].StartsWith("rung 1 |") && rows[9].EndsWith("| duel")
                && rows.Last().Contains("You: at the foot") && fits && _campaignScreen.DebugLadderPlaque.line.StartsWith("Rung 0 of 10"));
            Report($"3. before ladder-holder the top's holder is a stranger on the board ({rows.FirstOrDefault()})",
                rows.Count > 0 && rows[0].StartsWith("rung 10 | ??? |") && !profile.Seen.Contains(Ladder.HolderScene));

            // 4. Rung 1's versus: the Gauntlet's rules, skill 10/10; a loaner is refused (nothing goes live).
            yield return PressDuel(ladder.Rungs[0].Key, "rung1");
            yield return Shot("rung1-versus");
            var sides = _scenes.LastVersus;
            Report($"4. rung 1's versus: \"{Plain(sides?.you.Detail ?? "").Replace("\n", " ")}\" against \"{Plain(sides?.them.Detail ?? "").Replace("\n", " ")}\"",
                sides != null && sides.Value.you.Detail.Contains("Tier 0 Gauntlet") && sides.Value.them.Detail.Contains("skill 10/10") && sides.Value.them.Name == "Rhea"
                && _versusDeck != null && _campaign.LadderProblems(_versusDeck).Count == 0);
            var legalDeck = _versusDeck;
            // A custom loaner of yours (a campaign deck, not one you built): the arrows never offer it here, and Duel! refuses it.
            var loaner = _campaign.CustomizeLoaner(ladder.Rungs[0].Deck, "Ladder Check Loaner");
            _versusDeck = loaner;
            ShowLadderVersus(ladder.Rungs[0].Key, cycled: true);
            yield return new WaitForSecondsRealtime(0.4f);
            if (_scenes.Buttons.TryGetValue("Duel!", out var withLoaner)) withLoaner();
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("rung1-loaner-refused");
            var refusedLive = Profile.Load(_campaign.SavePath)?.LiveDuel;
            Report($"4. a loaner is refused at Duel! ({_campaign.LadderProblems(loaner).FirstOrDefault()?.Message}): no toss, nothing live",
                _scenes.IsOpen && _scenes.Buttons.ContainsKey("Duel!") && refusedLive == null && _ladderDuel == null
                && _campaign.LadderProblems(loaner).FirstOrDefault()?.Message.Contains("loaner") == true);
            _library.Delete(loaner);
            _versusDeck = legalDeck;

            // 4. An open-mode save (this one): the ladder still plays the Gauntlet's list, so a deck over it is refused, though
            // Ilse's table (the mode's rules: no list in open play) would take it.
            {
                var list = _campaign.LadderRules.List;
                var forbidden = list?.Limits.Where(kv => kv.Value == 0).Select(kv => kv.Key)
                    .FirstOrDefault(c => _cardPack!.Get(c) is { } info && !info.IsExtraDeckMonster) ?? 0;
                var over = _library.Load(legalDeck!).Clone();
                over.Name = "Ladder Check Over The List";
                over.Campaign = true;
                over.Notes["title"] = "Ladder Check Over The List";
                if (forbidden != 0) { over.Main.Add(forbidden); profile.Collection.Add(forbidden, Ygo.Engine.Data.Rarity.Common, 1, CopySource.Starter, 0); }
                _library.Save(over);
                var ladderSays = _campaign.LadderProblems(over.Name);
                var tableSays = _campaign.Problems(over, _campaign.Rules(_campaign.LadderRulesCircuit));
                _versusDeck = over.Name;
                ShowLadderVersus(ladder.Rungs[0].Key, cycled: true);
                yield return new WaitForSecondsRealtime(0.4f);
                if (_scenes.Buttons.TryGetValue("Duel!", out var overDuel)) overDuel();
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot("rung1-over-the-list");
                Report($"4. open play (era-limited {profile.EraLimited}): the ladder plays {list?.Name}; a deck with {_cardPack!.Get(forbidden)?.Name} is refused "
                       + $"(\"{ladderSays.FirstOrDefault()?.Message}\"), nothing goes live; under the mode's rules it would pass ({tableSays.Count} problems)",
                    !profile.EraLimited && list?.Name == "TCG September 2026" && forbidden != 0 && ladderSays.Any(p => p.Message.Contains(list.Name))
                    && tableSays.Count == 0 && Profile.Load(_campaign.SavePath)?.LiveDuel == null && _ladderDuel == null && _scenes.Buttons.ContainsKey("Duel!"));
                _library.Delete(over.Name);
                _versusDeck = legalDeck;
            }

            // 5. A real win on rung 1 (the duel played until your first decision, then Rhea gives it up): FinishLadderDuel records
            // it, pays it (AfterDuel's bond with her crosses 3), ends the live duel and shows "Rung 1 +800"; then a real concede.
            profile.Wins["rhea"] = 2;
            _campaign.Save();
            ShowLadderVersus(ladder.Rungs[0].Key);
            yield return Until(() => _scenes.IsOpen && _scenes.Buttons.ContainsKey("Duel!"), 4);
            if (_scenes.Buttons.TryGetValue("Duel!", out var winDuel)) winDuel();
            yield return Until(YourTurnWaits, 30);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("rung1-duel");
            var liveWin = Profile.Load(_campaign.SavePath)?.LiveDuel;
            _lastLadderResult = null;
            if (_runner != null && !_runner.IsFinished)
            {
                _runner.Concede(1, DuelRunner.ReasonConceded);
                _paused = false;
                _answerReady = true;
            }
            yield return Until(() => _announcedResult, 20);
            yield return Until(() => _scenes.IsOpen && _scenes.Buttons.ContainsKey("Continue"), 8);
            var results1 = _scenes.LastResults;
            var r1 = _lastLadderResult;
            Report($"5. a real win on rung 1 through the duel's end shows \"{r1?.Reward.Summary}\" ({string.Join(", ", results1?.Earnings.Select(e => $"{e.what} {e.dp}") ?? new string[0])}); "
                   + $"live while played ({liveWin?.Kind} {liveWin?.Guest}), cleared after; AfterDuel ran (bondtrade:rhea {profile.Unlocks.Contains("bondtrade:rhea")}); the second button reads \"{LadderAgainLabel}\"",
                _board.Winner == 0 && r1 != null && r1.Reward.Summary.StartsWith("Rung 1 +800") && results1?.Earnings.Any(e => e.what == "Rung 1" && e.dp == 800) == true
                && ladder.Climbed(profile.Ladder) == 1 && liveWin is { Kind: LiveDuels.Ladder } && profile.LiveDuel == null && Profile.Load(_campaign.SavePath)?.LiveDuel == null
                && profile.Unlocks.Contains("bondtrade:rhea") && LadderAgainLabel == "Next rung" && _scenes.Buttons.ContainsKey("Next rung"));
            yield return ResultsThenStory("rung1-win", true);
            int lossDp0 = profile.DpEarned.GetValueOrDefault(Ledger.Loss(Ladder.LedgerKind));
            yield return PressDuel(ladder.Rungs[1].Key, "rung2");
            yield return ConcedeFromVersus("rung2-concede");
            Report($"5. the concede keeps rung 1 (+{profile.DpEarned.GetValueOrDefault(Ledger.Loss(Ladder.LedgerKind)) - lossDp0} as ladder.loss), nothing left live",
                ladder.Climbed(profile.Ladder) == 1 && profile.DpEarned.GetValueOrDefault(Ledger.Loss(Ladder.LedgerKind)) - lossDp0 == Circuits.LossDp
                && profile.LiveDuel == null && Profile.Load(_campaign.SavePath)?.LiveDuel == null);

            // 6. Rungs 2-8 taken: Ilse's entrance before her first versus, Synchron (1 of 4); two wins and a concede: Synchron again.
            for (int n = 2; n <= 8; n++) _campaign.RecordLadder(ladder.Rungs[n - 1], 0);
            _campaign.Save();
            var ilse = ladder.Rungs[8];
            yield return PressDuel(ilse.Key, "ilse-seat");
            yield return Shot("ilse-versus-1");
            sides = _scenes.LastVersus;
            Report($"6. at 8 climbed, Duel on Ilse plays ladder-ilse{SceneNote("ladder-ilse")}, then her versus: {sides?.them.Deck} {sides?.them.DeckNote}",
                profile.Seen.Contains("ladder-ilse") && sides?.them.Deck == DeckTitle("synchron") && sides?.them.DeckNote == "(1 of 4)");
            for (int k = 0; k < 2; k++)
            {
                var sw = _campaign.RecordLadder(ilse, 0)!;
                _campaign.Save();
                _scenes.Close();
                BeginLadderResults(sw, 0);
                yield return ResultsThenStory($"ilse-win{k + 1}", k == 0);
            }
            yield return PressDuel(ilse.Key, "ilse-third");
            sides = _scenes.LastVersus;
            bool third = sides?.them.Deck == DeckTitle("gadgets") && sides?.them.DeckNote == "(3 of 4)";
            yield return ConcedeFromVersus("ilse-concede");
            yield return PressDuel(ilse.Key, "ilse-again");
            yield return Shot("ilse-versus-again");
            sides = _scenes.LastVersus;
            Report($"6. two wins bring Gadgets (3 of 4: {third}); a concede starts the sitting again: {sides?.them.Deck} {sides?.them.DeckNote}, rung 8 kept",
                third && sides?.them.Deck == DeckTitle("synchron") && sides?.them.DeckNote == "(1 of 4)" && profile.Ladder.Sitting == 0 && ladder.Climbed(profile.Ladder) == 8);
            _scenes.Close();

            // 7. Ilse's rung falls (her beat), then the top's holder: their entrance, chaos-ritual, and the top won.
            LadderResult? fourth = null;
            for (int k = 0; k < 4; k++) fourth = _campaign.RecordLadder(ilse, 0);
            _campaign.Save();
            BeginLadderResults(fourth!, 0);
            yield return ResultsThenStory("ilse-falls", true);
            Report($"7. Ilse's fourth win takes rung 9 and plays ladder-ilse-beat{SceneNote("ladder-ilse-beat")}",
                ladder.Climbed(profile.Ladder) == 9 && profile.Seen.Contains("ladder-ilse-beat"));
            var topSeat = ladder.Rungs[9];
            yield return PressDuel(topSeat.Key, "holder");
            yield return Shot("top-versus");
            sides = _scenes.LastVersus;
            Report($"7. Duel on rung 10 plays ladder-holder{SceneNote("ladder-holder")} before a versus with {sides?.them.Name} on {sides?.them.Deck} ({Plain(sides?.them.Detail ?? "").Split('\n')[0]})",
                profile.Seen.Contains("ladder-holder") && sides?.them.Deck == DeckTitle("chaos-ritual") && sides.Value.them.Detail.Contains("skill 10/10"));
            _scenes.Close();
            int tokens0 = profile.Tokens;
            var topWin = _campaign.RecordLadder(topSeat, 0)!;
            _campaign.Save();
            BeginLadderResults(topWin, 0);
            yield return Until(() => _scenes.IsOpen && _scenes.Buttons.ContainsKey("Continue"), 6);
            var topResults = _scenes.LastResults;
            Report($"7. the top's results: {string.Join(", ", topResults?.Earnings.Select(e => $"{e.what} {e.dp}") ?? new string[0])}; {string.Join(" · ", topResults?.Unlocks ?? new List<string>())}",
                topWin.TopReached && topResults?.Earnings.Any(e => e.what == "The top: prize" && e.dp == ladder.Prize) == true
                && topResults.Unlocks.Any(u => u.Contains($"+{Ladder.TopTokens} Card Tokens")) && topResults.Unlocks.Any(u => u.StartsWith("New poster"))
                && profile.Tokens - tokens0 == Ladder.TopTokens);
            yield return ResultsThenStory("top", true);
            Report($"7. then ladder-top{SceneNote("ladder-top")} and ladder-top-home{SceneNote("ladder-top-home")}",
                profile.Seen.Contains("ladder-top") && profile.Seen.Contains("ladder-top-home"));
            _campaignScreen.DebugShowLadder();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("board-top");
            Report($"7. the board says {_campaignScreen.DebugLadderPlaque.line}, rung 10 names its holder now ({_campaignScreen.LadderRows.FirstOrDefault()}); the poster is earned",
                _campaignScreen.DebugLadderPlaque.line == "Top: You" && _campaignScreen.LadderRows.FirstOrDefault()?.StartsWith($"rung 10 | {ladder.Rungs[9].Character.Name} |") == true && Posters.Find(Ladder.PosterId) is Poster lp && Posters.IsUnlocked(lp, _campaign.Circuits, profile));

            // 8. A relaunch replays nothing; a rematch pays 150; New Game+ leaves the board as it was.
            _campaignScreen.Close();
            int dpRelaunch = profile.Dp;
            _campaign.DebugReload();
            profile = _campaign.Profile!;
            _campaignScreen.Open();
            yield return new WaitForSecondsRealtime(1.5f);
            Report($"8. a relaunch plays nothing and pays nothing ({profile.Dp - dpRelaunch:+0;-0;0} DP), the ladder as it was (rung {ladder.Climbed(profile.Ladder)})",
                !_storyScreen.IsOpen && profile.Dp == dpRelaunch && ladder.HoldsTop(profile.Ladder) && _campaign.AlleyNoticeEvents.Count == 0);
            var rematch = _campaign.RecordLadder(ladder.Rungs[2], 0);
            _campaign.Save();
            Report($"8. a rematch pays {rematch?.Reward.Dp} ({rematch?.Reward.Summary})", rematch != null && rematch.Rematch && rematch.Reward.Dp == Ladder.RepeatWinDp);
            _campaignScreen.Close();
            _campaign.NewGamePlus();
            profile = _campaign.Profile!;
            foreach (var sc in _story.Pending(profile, new[] { "ngplus" })) Story.MarkSeen(profile, sc);
            _campaign.Save();
            _campaignScreen.DebugShowLadder();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("board-after-ngplus");
            Report($"8. after New Game+ the board is unchanged ({_campaignScreen.DebugLadderPlaque.line}, cycle {profile.Cycle})",
                profile.Cycle == 1 && ladder.HoldsTop(profile.Ladder) && _campaignScreen.DebugLadderPlaque.line == "Top: You" && _campaignScreen.LadderShown);

            // 9. The visitors forced (no Invitational yet): Rue's row over yours, her scene before her duel when there is one.
            ladder.VisitorsForced = true;
            _campaignScreen.DebugShowLadder();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("board-visitor");
            rows = _campaignScreen.LadderRows.ToList();
            bool rowAbove = rows.Count > 1 && rows[0].StartsWith("visitor | Rue") && rows[0].EndsWith("| duel") && rows[1].StartsWith("rung 10 |");
            yield return PressDuel("visitor.rue", "rue-seat");
            yield return Shot("rue-versus");
            sides = _scenes.LastVersus;
            bool rueScene = !HasScene("ladder-visit-rue") || profile.Seen.Contains("ladder-visit-rue");
            Report($"9. with visitors forced, Rue's row is over yours ({rows.FirstOrDefault()}), ladder-visit-rue{SceneNote("ladder-visit-rue")} plays before her versus ({sides?.them.Deck})",
                rowAbove && rueScene && sides?.them.Name == "Rue Marchbanks" && sides?.them.Deck == DeckTitle("yubel"));
            _scenes.Close();
            // Rue, Sasha and Augie beaten; then Junia, last: her scene before her duel, and her beat after the win.
            foreach (var v in ladder.Visitors.Take(3)) _campaign.RecordLadder(v, 0);
            _campaign.Save();
            var junia = ladder.Visitors[^1];
            yield return PressDuel(junia.Key, "junia-seat");
            yield return Shot("junia-versus");
            sides = _scenes.LastVersus;
            _scenes.Close();
            var juniaWin = _campaign.RecordLadder(junia, 0)!;
            _campaign.Save();
            BeginLadderResults(juniaWin, 0);
            yield return ResultsThenStory("junia-beat", true);
            Report($"9. Junia comes last: ladder-visit-junia{SceneNote("ladder-visit-junia")} before her versus ({sides?.them.Deck}), ladder-visit-junia-beat{SceneNote("ladder-visit-junia-beat")} after; then nobody is next",
                profile.Seen.Contains("ladder-visit-junia") && profile.Seen.Contains("ladder-visit-junia-beat") && sides?.them.Name == junia.Character.Name
                && juniaWin.VisitorBeaten && ladder.Next(profile, profile.Ladder, lib) == null);
            ladder.VisitorsForced = false;

            foreach (var (scene, trigger) in Ladder.RequiredScenes)
                Report($"scene {scene} ({trigger}) was shown{SceneNote(scene)}", profile.Seen.Contains(scene));
            Debug.Log($"ladder check: (the ladder's scenes in story.txt: {string.Join(", ", _story.Scenes.Where(sc => ladder.IsTrigger(sc.Trigger)).Select(sc => sc.Id))})");
            File.WriteAllLines(Path.Combine(folder, "ladder-story.txt"), lines);
            Debug.Log("ladder check: done");
            Application.Quit();
        }
    }
}
