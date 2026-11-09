using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-finalcheck &lt;folder&gt;` (with -mods &lt;empty folder&gt;): the Library's "Every Era" final from the last cup clear to
    /// its epilogue, as a player sees it, every story line screenshotted. The cups are won but the Ryzeal Cup's last match,
    /// whose win goes through the cup results (its clear, Ilse's arrival); the plaque opens Ilse's rules; a deck of yours
    /// made with Make it yours plays round 1 for real and concedes (the duel, its result, the round's results screen); then
    /// each round is won through the same results path a won duel takes (the round beats), the last one raising the final's
    /// clear, the epilogue, the shelf's won plaque and the Every Era poster in the Locker.
    /// </summary>
    public sealed partial class DuelController
    {
        private System.Collections.IEnumerator FinalCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            int shots = 0;
            System.Collections.IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"final-{shots++:00}-{name}.png"));
                yield return null;
                yield return null;
            }
            void Report(string what, bool ok) => Debug.Log($"final check: {(ok ? "ok  " : "FAIL")} {what}");
            var lines = new List<string>();
            var places = new List<(string when, string place)>();
            // A story, read like a player: each line once it's typed out (a screenshot of it), then a click.
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
                        places.Add((when, _storyScreen.DebugPlace));
                        Debug.Log($"final check: story ({when}) {last}");
                        yield return new WaitForSecondsRealtime(2.6f); // the longest lines type out in about 2.5 s
                        yield return Shot($"{when}-line");
                    }
                    _storyScreen.DebugAdvance();
                    yield return new WaitForSecondsRealtime(0.4f);
                }
            }
            // The results screen a duel's end shows, then its Continue and the story it raises.
            System.Collections.IEnumerator ResultsThenStory(string when, bool shoot)
            {
                var shown = Time.realtimeSinceStartup + 8;
                while (!_scenes.IsOpen && Time.realtimeSinceStartup < shown) yield return null;
                yield return new WaitForSecondsRealtime(0.6f);
                if (shoot) yield return Shot($"{when}-results");
                bool hadContinue = _scenes.Buttons.TryGetValue("Continue", out var next);
                next?.Invoke();
                var storyBy = Time.realtimeSinceStartup + 3;
                while (!_storyScreen.IsOpen && !_campaignScreen.IsOpen && Time.realtimeSinceStartup < storyBy) yield return null;
                yield return ReadStory(when);
                var back = Time.realtimeSinceStartup + 4;
                while (!_campaignScreen.IsOpen && Time.realtimeSinceStartup < back) yield return null;
                Report($"{when}: the results screen's Continue goes back to the campaign", hadContinue && _campaignScreen.IsOpen);
            }
            yield return new WaitForSecondsRealtime(0.5f);

            _campaign.Start(CampaignState.Starters[1]);
            var profile = _campaign.Profile!;
            var lib = _campaign.Library;
            profile.LibraryOpen = true;
            DebugSetDp(profile, 400_000);
            profile.Exp = 50_000_000; // no level-ups in the way of the scenes

            // Every cup won but the Ryzeal Cup's boss match: its win clears the 12th cup and opens the final.
            int ryzeal = lib.IndexOf("Ryzeal Cup");
            int lastMatch = lib.Cups[ryzeal].Matches.FindLastIndex(m => !m.Champion);
            for (int c = 0; c < lib.Cups.Count; c++)
                for (int m = 0; m < lib.Cups[c].Matches.Count; m++)
                    if (!lib.Cups[c].Matches[m].Champion && !(c == ryzeal && m == lastMatch)) profile.LibraryWins[lib.Key(c, m)] = 1;
            _campaign.Save();
            var cupReward = lib.Record(profile, ryzeal, lastMatch, 0, loaner: false, built: true, deck: profile.Deck);
            _campaign.Save();
            Report($"the 12th cup's clear opens the final ({string.Join(", ", Library.Events(cupReward))})",
                lib.FinalOpen(profile) && Library.Events(cupReward).Contains("final"));
            _campaignCup = (ryzeal, lastMatch);
            BeginCupResults(ryzeal, lastMatch, cupReward, 0, false, true, false);
            yield return ResultsThenStory("last-cup", true);
            _campaignCup = null;
            // Level 99 with EXP past the cap (50,000,000): the results' EXP line says so instead of "... EXP to level 100".
            Report($"at the level cap (level {profile.Level}) the results' EXP line reads \"{_scenes.ExpLine}\"",
                profile.Level == Leveling.MaxLevel && _scenes.ExpLine == "Max level");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("shelf-final-open");
            Report("back on the campaign screen, the final's panel is shown first", _campaignScreen.LibraryCup == lib.Cups.Count);

            // The panel judges your deck as the round's versus does: a custom loaner isn't ready (the final takes decks you built).
            {
                string deckBefore = profile.Deck;
                var custom = _campaign.CustomizeLoaner(lib.Rounds[0].Match.Deck, "Final Check Loaner");
                profile.Deck = custom;
                _campaignScreen.DebugShowLibrary(lib.Cups.Count);
                yield return new WaitForSecondsRealtime(0.3f);
                var panelLine = _campaignScreen.YourDeckLine;
                var versusSays = _campaign.RoundProblems(custom, 0);
                Report($"with a custom loaner picked, the final's panel says it isn't ready (\"{panelLine}\"), as round 1's versus refuses it",
                    profile.Deck == custom && versusSays.Count > 0 && panelLine.StartsWith(versusSays[0].Message) && panelLine.Contains("loaner"));
                _library.Delete(custom);
                profile.Deck = deckBefore;
                _campaign.Save();
                _campaignScreen.DebugShowLibrary(lib.Cups.Count);
                yield return new WaitForSecondsRealtime(0.3f);
            }

            // The plaque, clicked: Ilse's rules.
            _campaignScreen.DebugClickFinalPlaque();
            var rulesBy = Time.realtimeSinceStartup + 3;
            while (!_storyScreen.IsOpen && Time.realtimeSinceStartup < rulesBy) yield return null;
            yield return ReadStory("rules");
            var openBy = Time.realtimeSinceStartup + 4;
            while (!_campaignScreen.IsOpen && Time.realtimeSinceStartup < openBy) yield return null;

            // Round 1 for real: Make it yours builds Ines's Goat Control for 2005; it's legal there; the duel starts, you
            // concede, and the round's results screen follows (a loss: the round stays next).
            var rules = _campaign.RoundRules(0);
            var quote = _campaign.QuoteOwn("loaner:" + lib.Rounds[0].Match.Deck, rules);
            var owned = quote == null ? null : _campaign.Own(quote, rules);
            bool legal = owned != null && _campaign.Problems(_library.Load(owned), rules).Count == 0;
            Report($"Make it yours builds a deck of yours for round 1 ({owned}), legal in 2005", legal);
            _campaignScreen.DebugShowLibrary(lib.Cups.Count);
            _campaignScreen.DebugRoundDuel(0);
            var versusBy = Time.realtimeSinceStartup + 6;
            while (!_scenes.IsOpen && Time.realtimeSinceStartup < versusBy) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("round1-versus");
            Report($"round 1's versus starts on your legal deck ({_versusDeck})", _versusDeck == owned);
            if (_scenes.Buttons.TryGetValue("Duel!", out var duel)) duel();
            var duelBy = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < duelBy && !(_runner != null && _runner.IsWaiting && _runner.WaitingFor == 0 && _human?.Current != null)) yield return null;
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("round1-duel");
            bool dueling = _campaignRound == 0 && _runner != null && !_campaignScreen.IsOpen;
            ConcedeDuel();
            var endBy = Time.realtimeSinceStartup + 20;
            while (!_announcedResult && Time.realtimeSinceStartup < endBy) yield return null;
            Report($"round 1 plays as a real duel with the campaign screen closed, and conceding ends it ({_board.Winner})", dueling && _announcedResult && _board.Winner == 1);
            yield return ResultsThenStory("round1-loss", true);
            Report("a lost round stays next", lib.FinalNext(profile) == 0);

            // Rounds 1-6 won, each through the results a won duel shows; the round beats on the way, the epilogue at the end.
            for (int r = 0; r < lib.Rounds.Count; r++)
            {
                var reward = lib.RecordFinal(profile, r, 0, owned ?? profile.Deck);
                _campaign.Save();
                _campaignRound = r;
                if (r == 2)
                {
                    // Another duel starts in the 2 s the results wait (the board's Rematch, a replay): the old results never
                    // open over it, and its scenes wait in the queue for the next campaign screen, which plays them.
                    _campaignScreen.Close();
                    BeginRoundResults(r, reward, 0);
                    yield return new WaitForSecondsRealtime(0.5f);
                    seed++;
                    PickRandomBots();
                    NewDuel();
                    _paused = true;
                    UpdateControls();
                    yield return new WaitForSecondsRealtime(2.5f);
                    var waiting = _campaign.AlleyNoticeEvents.ToList();
                    Report($"round 3's results don't open over a duel started in their 2 s, and its scenes wait in the queue ({string.Join(", ", waiting)})",
                        !_scenes.IsOpen && Library.Events(reward).All(waiting.Contains));
                    // The app quits here (TODO 2026-11-06): the queue is in the save, and the relaunch reads it back for the next
                    // campaign screen. Nothing is paid again on the way (DP, copies), and once played it's gone from the save.
                    var inSave = Profile.Load(_campaign.SavePath)?.StoryQueue.SelectMany(b => b.Events).ToList() ?? new List<string>();
                    int dpBefore = profile.Dp, copiesBefore = profile.Collection.All.Count();
                    _campaign.DebugReload();
                    profile = _campaign.Profile!;
                    var reloadedQueue = _campaign.AlleyNoticeEvents.ToList();
                    Report($"the queued scenes are in the save ({string.Join(", ", inSave)}), and a relaunch (the save read again) queues them for the next campaign screen ({string.Join(", ", reloadedQueue)})",
                        Library.Events(reward).All(inSave.Contains) && Library.Events(reward).All(reloadedQueue.Contains) && !profile.Seen.Contains("every-era-round-4"));
                    _campaignScreen.Open();
                    var queuedBy = Time.realtimeSinceStartup + 3;
                    while (!_storyScreen.IsOpen && Time.realtimeSinceStartup < queuedBy) yield return null;
                    yield return ReadStory("round3-queued");
                    var reopened = Time.realtimeSinceStartup + 4;
                    while (!_campaignScreen.IsOpen && Time.realtimeSinceStartup < reopened) yield return null;
                    Report("the next campaign screen plays them (round 4's opening beat), and the queue is empty after",
                        profile.Seen.Contains("every-era-round-4") && _campaign.AlleyNoticeEvents.Count == 0 && _campaignScreen.IsOpen);
                    // Played once: the save's queue is empty, nothing was paid again, and another relaunch plays nothing.
                    var leftInSave = Profile.Load(_campaign.SavePath)?.StoryQueue.Count ?? -1;
                    _campaignScreen.Close();
                    _campaign.DebugReload();
                    profile = _campaign.Profile!;
                    _campaignScreen.Open();
                    yield return new WaitForSecondsRealtime(1.5f);
                    Report($"they play once: the save's queue is empty after ({leftInSave} batches), the relaunch replayed no DP or card ({profile.Dp - dpBefore:+0;-0;0} DP, {profile.Collection.All.Count() - copiesBefore:+0;-0;0} copies), and a second relaunch's campaign screen plays nothing",
                        leftInSave == 0 && profile.Dp == dpBefore && profile.Collection.All.Count() == copiesBefore && !_storyScreen.IsOpen && _campaignScreen.IsOpen && _campaign.AlleyNoticeEvents.Count == 0);
                    continue;
                }
                BeginRoundResults(r, reward, 0);
                yield return ResultsThenStory($"round{r + 1}", r == 0 || r == lib.Rounds.Count - 1);
                if (r == lib.Rounds.Count - 1)
                    Report($"the last round's win clears the final ({lib.Final.Prize:N0} DP and {Library.FinalTokens} tokens) and raises its epilogue",
                        reward.ClearedCircuit == lib.Final && Library.Events(reward).Contains("cupclear:" + lib.Final.Name));
            }
            _campaignRound = null;
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("shelf-final-won");
            Report("the final is won and its plaque says so", lib.FinalCleared(profile));
            _campaignScreen.DebugOpenLocker();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("locker-poster");
            Report("the Every Era poster is in the Locker", Posters.Find("every-era") is Poster p && Posters.IsUnlocked(p, _campaign.Circuits, profile));
            yield return IlseSteps(profile, Report, Shot, ReadStory, places);
            profile = _campaign.Profile!;
            foreach (var scene in new[] { "every-era-final", "every-era-rules", "every-era-round-2", "every-era-round-4", "every-era-round-6", "every-era-clear", "every-era-corkboard",
                         "ilse-table", "gift-ilse", Story.FinaleScene }
                         // Vera's rematch scenes, whatever the writers call them (guest:vera, guestbeat:vera).
                         .Concat(_story.Scenes.Where(sc => sc.Trigger == Story.GuestEvent("vera") || sc.Trigger == Story.GuestBeatEvent("vera")).Select(sc => sc.Id)))
                Report($"scene {scene} was shown{(_story.Scenes.Any(sc => sc.Id == scene) ? "" : " (no such scene in story.txt yet)")}", profile.Seen.Contains(scene));
            File.WriteAllLines(Path.Combine(folder, "final-story.txt"), lines);
            Debug.Log("final check: done");
            Application.Quit();
        }

        /// <summary>
        /// Vera's rematch, the sixth guest (docs/dev/NEXT-BUILD-2026-11-06b.md chunk G), after Ilse's win: her row open after the final
        /// (Duel:vera, no Tag:vera, the first-win prize though she was beaten in the circuits); Duel:vera plays her guest:vera scene
        /// when there is one, then a versus with Warriors under the Tier 0 Gauntlet's rules; a first win through the guest results
        /// path pays the prize, gives no second Gearfried (her gift was the circuit's) and plays the guestbeat:vera scene when there is one.
        /// </summary>
        private System.Collections.IEnumerator VeraSteps(Profile profile, Action<string, bool> Report, Func<string, System.Collections.IEnumerator> Shot,
            Func<string, System.Collections.IEnumerator> ReadStory)
        {
            var vera = _campaign.Guests.All.FirstOrDefault(g => g.Character.Id == "vera");
            if (vera == null) { Report("Vera is a special guest", false); yield break; }
            bool HasScene(string trigger) => _story.Scenes.Any(sc => sc.Trigger == trigger);
            var meeting = Story.GuestEvent("vera");
            var beaten = Story.GuestBeatEvent("vera");
            // A real save beat her in the Town Tournament long before the final: her circuit win and its gift (gift-vera read).
            int vc = _campaign.Circuits.All.ToList().FindIndex(c => c.Matches.Any(m => m.Character.Id == "vera"));
            int vm = vc < 0 ? -1 : _campaign.Circuits.All[vc].Matches.FindIndex(m => m.Character.Id == "vera");
            if (vc >= 0 && !profile.HasBeaten("vera"))
            {
                var circuitWin = _campaign.Circuits.Record(profile, vc, vm, 0);
                _campaign.AfterDuel(circuitWin, _campaign.Circuits.All[vc].Band, null, false, false);
                foreach (var scene in _story.Pending(profile, CardUnlocks.Events(circuitWin.Grants))) Story.MarkSeen(profile, scene);
                _campaign.Save();
            }
            int gearfriedBefore = _campaign.Owned("Gearfried the Swordmaster");
            ShowGuests();
            yield return new WaitForSecondsRealtime(0.6f);
            var row = _scenes.LastGuestRows.FirstOrDefault(r => r.Id == "vera");
            Report($"Vera's row after the final (beaten {profile.WinsAgainst("vera")}x in the circuits): \"{row?.Decks}\" / \"{row?.Status}\"; Duel:vera and Scout:vera, no Tag:vera",
                row != null && row.Unlocked && row.Decks == $"Plays {DeckTitle("warriors")} · no tag duel" && row.Status == $"First win: +{vera.Prize:N0} DP"
                && _scenes.Buttons.ContainsKey("Duel:vera") && _scenes.Buttons.ContainsKey("Scout:vera") && !_scenes.Buttons.ContainsKey("Tag:vera"));
            if (_scenes.Buttons.TryGetValue("Duel:vera", out var fight)) fight();
            var meetBy = Time.realtimeSinceStartup + 4;
            while (!_storyScreen.IsOpen && !_scenes.Buttons.ContainsKey("Duel!") && Time.realtimeSinceStartup < meetBy) yield return null;
            bool met = _storyScreen.IsOpen;
            yield return ReadStory("vera-rematch");
            var versusBy = Time.realtimeSinceStartup + 5;
            while (!_scenes.Buttons.ContainsKey("Duel!") && Time.realtimeSinceStartup < versusBy) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("vera-versus");
            var sides = _scenes.LastVersus;
            var lastCircuit = _campaign.Circuits.All[_campaign.Circuits.All.Count - 1].Name;
            Report($"Duel:vera plays her meeting ({(HasScene(meeting) ? (met ? "played" : "NOT played") : "no guest:vera scene yet, straight to the versus")}), then the versus: {sides?.them.Deck} under \"{sides?.you.Detail.Split('\n')[0]}\"",
                (!HasScene(meeting) || met) && _scenes.Buttons.ContainsKey("Duel!") && sides?.them.Deck == DeckTitle("warriors") && sides?.you.Detail.Contains($"Rules: {lastCircuit}.") == true);
            if (_scenes.Buttons.TryGetValue("Back", out var versusBack)) versusBack();
            yield return new WaitForSecondsRealtime(0.4f);

            // A first win through the guest results path: the prize, no second gift, then the guestbeat:vera scene.
            _scenes.Close();
            var reward = GuestList.Record(profile, _campaign.Circuits, vera, false, 0);
            _campaign.AfterDuel(reward, "", null, false, false);
            _campaign.Save();
            BeginGuestResults((vera, false, ""), reward, 0);
            var shownBy = Time.realtimeSinceStartup + 8;
            while (!(_scenes.IsOpen && _scenes.Buttons.ContainsKey("Continue")) && Time.realtimeSinceStartup < shownBy) yield return null;
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("vera-results");
            var r = _scenes.LastResults;
            Report($"her first guest win's results: {string.Join(", ", r?.Earnings.Select(e => $"{e.what} {e.dp:N0}") ?? Enumerable.Empty<string>())}; unlocks: {string.Join(" | ", r?.Unlocks ?? new List<string>())}; "
                   + $"Gearfried the Swordmaster {gearfriedBefore} then {_campaign.Owned("Gearfried the Swordmaster")}",
                r != null && r.Earnings.Any(e => e.what == $"First win against {vera.Character.Name}" && e.dp == vera.Prize)
                && !reward.Grants.Any(g => g.Kind == GrantKind.RivalGift && g.Character == "vera") && !CardUnlocks.Events(reward.Grants).Contains("beat:vera")
                && _campaign.Owned("Gearfried the Swordmaster") == gearfriedBefore);
            if (_scenes.Buttons.TryGetValue("Continue", out var next)) next();
            var storyBy = Time.realtimeSinceStartup + 3;
            while (!_storyScreen.IsOpen && !_scenes.Buttons.ContainsKey("Duel:vera") && Time.realtimeSinceStartup < storyBy) yield return null;
            bool winScene = _storyScreen.IsOpen;
            yield return ReadStory("vera-win");
            var guestsBy = Time.realtimeSinceStartup + 4;
            while (!_scenes.Buttons.ContainsKey("Duel:vera") && Time.realtimeSinceStartup < guestsBy) yield return null;
            yield return new WaitForSecondsRealtime(0.4f);
            row = _scenes.LastGuestRows.FirstOrDefault(x => x.Id == "vera");
            bool beatSeen = _story.Scenes.Where(sc => sc.Trigger == beaten).All(sc => profile.Seen.Contains(sc.Id));
            Report($"Continue plays her win scene ({(HasScene(beaten) ? (winScene && beatSeen ? "guestbeat:vera seen" : "guestbeat:vera NOT played") : "no guestbeat:vera scene yet")}), then the guests: her row says \"{row?.Status}\"",
                (!HasScene(beaten) || (winScene && beatSeen)) && row != null && row.Status.StartsWith("Beaten "));
            yield return Shot("guests-after-vera");
        }

        /// <summary>
        /// After the final (docs/dev/ILSE-GUEST-SPEC-2026-11-06.md section 4): the finale not raised yet; Ilse open on the guests screen
        /// (five rows clear of Back, no Tag:ilse); Duel:ilse plays her meeting (guest:ilse), then a versus with Synchron under the
        /// Tier 0 Gauntlet's rules; a win through the guest results path (the prize, the Mirror Force gift, gift-ilse at the
        /// corkboard-top); her row then says the next deck and she can partner you; with alley-closing seen the next Open plays
        /// the finale once, and a relaunch plays nothing.
        /// </summary>
        private System.Collections.IEnumerator IlseSteps(Profile profile, Action<string, bool> Report, Func<string, System.Collections.IEnumerator> Shot,
            Func<string, System.Collections.IEnumerator> ReadStory, List<(string when, string place)> places)
        {
            var ilse = _campaign.Guests.All.FirstOrDefault(g => g.Character.Id == "ilse");
            if (ilse == null) { Report("Ilse is a special guest", false); yield break; }
            bool HasScene(string id) => _story.Scenes.Any(sc => sc.Id == id);
            Report($"after the final, the finale isn't raised yet (alley-closing unseen; FinaleDue {_campaign.FinaleDue})", !_campaign.FinaleDue && !profile.Seen.Contains(Story.FinaleScene));
            _campaignScreen.Close();
            ShowGuests();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("guests-six");
            var boxes = _scenes.GuestRowBoxes;
            Report($"the guests screen has {boxes.Count} rows of {_campaign.Guests.All.Count}, the last ending at {(boxes.Count > 0 ? boxes[boxes.Count - 1].bottom : 0)} (Back at {CampaignScenes.GuestsBackTop}), none overlapping; Ilse is open with Duel:ilse and no Tag:ilse",
                boxes.Count == 6 && boxes.Count == _campaign.Guests.All.Count && boxes.All(b => b.bottom <= CampaignScenes.GuestsBackTop && b.top >= 120)
                && boxes.Zip(boxes.Skip(1), (a, b) => a.bottom <= b.top).All(x => x) && GuestList.IsUnlocked(ilse, _campaign.Circuits, profile)
                && _scenes.Buttons.ContainsKey("Duel:ilse") && !_scenes.Buttons.ContainsKey("Tag:ilse") && _scenes.Buttons.ContainsKey("Scout:ilse"));
            var row = _scenes.LastGuestRows.FirstOrDefault(r => r.Id == "ilse");
            Report($"her row: \"{row?.Decks}\" / \"{row?.Status}\"",
                row != null && row.Decks == "Plays four decks, one a duel: Synchron, Zombies, Gadgets, Modern · no tag duel"
                && row.Status == $"First win: +{ilse.Prize:N0} DP · next: Synchron (1 of 4)");

            // Duel: her meeting first (when the story has it), then the versus.
            if (_scenes.Buttons.TryGetValue("Duel:ilse", out var fightIlse)) fightIlse();
            var meetBy = Time.realtimeSinceStartup + 4;
            while (!_storyScreen.IsOpen && !_scenes.Buttons.ContainsKey("Duel!") && Time.realtimeSinceStartup < meetBy) yield return null;
            bool met = _storyScreen.IsOpen;
            yield return ReadStory("ilse-table");
            var versusBy = Time.realtimeSinceStartup + 5;
            while (!_scenes.Buttons.ContainsKey("Duel!") && Time.realtimeSinceStartup < versusBy) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("ilse-versus");
            var sides = _scenes.LastVersus;
            var lastCircuit = _campaign.Circuits.All[_campaign.Circuits.All.Count - 1].Name;
            Report($"Duel:ilse plays her meeting ({(HasScene("ilse-table") ? (met && profile.Seen.Contains("ilse-table") ? "ilse-table seen" : "ilse-table NOT played") : "no scene yet, straight to the versus")}), "
                   + $"then the versus: {sides?.them.Deck} under \"{sides?.you.Detail.Split('\n')[0]}\"",
                (!HasScene("ilse-table") || (met && profile.Seen.Contains("ilse-table"))) && _scenes.Buttons.ContainsKey("Duel!")
                && sides?.them.Deck == DeckTitle("synchron") && sides?.you.Detail.Contains($"Rules: {lastCircuit}.") == true);
            if (_scenes.Buttons.TryGetValue("Back", out var versusBack)) versusBack();
            yield return new WaitForSecondsRealtime(0.4f);

            // A win through the guest results path: the prize line, the gift, then gift-ilse at the corkboard-top.
            _scenes.Close();
            int dpBefore = profile.Dp;
            var reward = GuestList.Record(profile, _campaign.Circuits, ilse, false, 0);
            _campaign.AfterDuel(reward, "", null, false, false);
            _campaign.Save();
            BeginGuestResults((ilse, false, ""), reward, 0);
            var shownBy = Time.realtimeSinceStartup + 8;
            while (!(_scenes.IsOpen && _scenes.Buttons.ContainsKey("Continue")) && Time.realtimeSinceStartup < shownBy) yield return null;
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("ilse-results");
            var r = _scenes.LastResults;
            Report($"her first win's results: {string.Join(", ", r?.Earnings.Select(e => $"{e.what} {e.dp:N0}") ?? Enumerable.Empty<string>())}; {string.Join(" | ", r?.Unlocks ?? new List<string>())} (+{profile.Dp - dpBefore:N0} DP)",
                r != null && r.Earnings.Any(e => e.what == $"First win against {ilse.Character.Name}" && e.dp == ilse.Prize) && r.Unlocks.Any(u => u.Contains("Mirror Force"))
                && reward.Grants.Any(g => g.Event == "beat:ilse"));
            if (_scenes.Buttons.TryGetValue("Continue", out var next)) next();
            var giftBy = Time.realtimeSinceStartup + 3;
            while (!_storyScreen.IsOpen && Time.realtimeSinceStartup < giftBy) yield return null;
            int from = places.Count;
            yield return ReadStory("gift-ilse");
            var giftPlaces = places.Skip(from).Select(p => p.place).Distinct().ToList();
            var guestsBy = Time.realtimeSinceStartup + 4;
            while (!_scenes.Buttons.ContainsKey("Duel:ilse") && Time.realtimeSinceStartup < guestsBy) yield return null;
            yield return new WaitForSecondsRealtime(0.4f);
            row = _scenes.LastGuestRows.FirstOrDefault(x => x.Id == "ilse");
            Report($"gift-ilse plays at {string.Join(", ", giftPlaces)} ({(HasScene("gift-ilse") ? "" : "no scene yet; ")}seen {profile.Seen.Contains("gift-ilse")}), Continue goes back to the guests, her row says \"{row?.Status}\", and she can partner you",
                (!HasScene("gift-ilse") || (profile.Seen.Contains("gift-ilse") && giftPlaces.Contains(StoryPlaces.CorkboardTop)))
                && row != null && row.Status.Contains("next: Zombies (2 of 4)") && _campaign.TagPartnerChoices().Any(p => p.Id == "ilse"));
            // The backdrop itself (the plain card on top), for the eye.
            yield return Shot("guests-after-ilse");
            yield return VeraSteps(profile, Report, Shot, ReadStory);

            // The alley's strand closed: the next Open plays the finale once; a relaunch plays nothing.
            profile.Seen.Add(BackAlley.ClosingScene);
            _campaign.Save();
            bool due = _campaign.FinaleDue;
            if (_scenes.Buttons.TryGetValue("Back", out var guestsBack)) guestsBack(); else _campaignScreen.Open();
            var finaleBy = Time.realtimeSinceStartup + 4;
            while (!_storyScreen.IsOpen && Time.realtimeSinceStartup < finaleBy) yield return null;
            bool finalePlayed = _storyScreen.IsOpen;
            yield return ReadStory("finale");
            var backBy = Time.realtimeSinceStartup + 4;
            while (!_campaignScreen.IsOpen && Time.realtimeSinceStartup < backBy) yield return null;
            Report($"with alley-closing seen the finale is due ({due}); the next Open plays it ({finalePlayed}, seen {profile.Seen.Contains(Story.FinaleScene)}) and it's due no more ({_campaign.FinaleDue})",
                due && (!HasScene(Story.FinaleScene) || (finalePlayed && profile.Seen.Contains(Story.FinaleScene))) && !_campaign.FinaleDue);
            _campaignScreen.Close();
            _campaign.DebugReload();
            _campaignScreen.Open();
            yield return new WaitForSecondsRealtime(1.5f);
            Report("a relaunch's campaign screen plays nothing (the finale once)", !_storyScreen.IsOpen && _campaignScreen.IsOpen && _campaign.Profile!.Seen.Contains(Story.FinaleScene));
        }
    }
}
