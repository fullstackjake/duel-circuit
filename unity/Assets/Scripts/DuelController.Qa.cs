using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Campaign;
using Ygo.Engine.Interactive;
using Ygo.Engine.Protocol;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-qaplay &lt;folder&gt; [-qagames N] [-qafull] [-qacontinue]`: plays the campaign like a player, to find bugs. A new save (in the
    /// -mods folder given) with the Mekk-Knight Invoked starter, then the next match each time through the real screens
    /// (circuit, versus, duel, results). Your decisions are made by a skilled bot with the deck's playbook and given
    /// through the prompt panel the way clicks are. Watches for errors, waits with nothing to answer, animations that
    /// never finish, stalls, attack arrows left over, rejected answers and overlong duels; logs everything to qa-log.txt
    /// with screenshots (each turn, each screen, each problem). The first game plays at the default speed (4/s), the rest
    /// at 16/s, both with animations.
    /// With -qafull, the rest of the campaign a player meets too, all at 16/s: Gus's lessons after the first circuit,
    /// the Card Shop (and the Card Trader with any tokens) after each circuit cleared, a deck's Recipe once for a
    /// wishlist, the Tag Cups as they open, Ban Passes through the deck editor when a circuit's list stops the deck, and
    /// a rebuild (Recipe, Auto-fix, packs) after three losses in a row to one opponent. Also: DP for Card Tokens and Ban
    /// Pass slots, trades of any era, Tag Cup partners picked by bond (and Kit), and the special guests as they unlock.
    /// -qacontinue picks up the campaign saved in the -mods folder instead of starting one.
    /// Once the save has the Format Library open (after the Gauntlet, or at once on a champion's save), the post-game comes
    /// first: the cups in shelf order (a legal deck of yours, else the measured loaner, Make it yours when the Stuck? advice
    /// offers it; a Champion's table left after two losses), each cleared cup's era mat and engraving, Library Locals once
    /// at each cleared cup (Skip to end when out, Finish, the Prize Packs), and the "Every Era" final's rounds in order with
    /// a deck of yours; then the rest of the campaign. Its counts go on the last line.
    /// </summary>
    public sealed partial class DuelController
    {
        private bool _qaPlaying;
        private SmartDuelist? _qaAdvisor;

        /// <summary>The style your stand-in plays with: the most skilled character who plans ahead and knows their deck.</summary>
        private Personality QaStyle()
        {
            // Campaign characters only (on the ladder): the alley's headliners and No Limits pilots sort among them by id and
            // would change every QA run's style (review fix 4).
            return _roster.Where(p => p.Ladder > 0 && p.PlansAhead && p.KnowsDeck).OrderByDescending(p => p.Skill).FirstOrDefault() ?? Personality.Balanced;
        }

        private System.Collections.IEnumerator QaPlay(string folder, int games)
        {
            Directory.CreateDirectory(folder);
            bool full = Array.IndexOf(Environment.GetCommandLineArgs(), "-qafull") >= 0;
            var logPath = Path.Combine(folder, "qa-log.txt");
            File.WriteAllText(logPath, $"Duel Circuit QA play{(full ? " (full campaign)" : "")}, {DateTime.Now:yyyy-MM-dd HH:mm}\n");
            int game = 0;
            // Every duel the harness starts (circuit, post-game, guest, tag, lesson): what the second-deck check counts.
            int duelsPlayed = 0;
            string where = "setup";
            void Log(string line)
            {
                var text = $"[{DateTime.Now:HH:mm:ss}] g{game} {where}: {line}";
                Debug.Log("qa: " + text);
                File.AppendAllText(logPath, text + "\n");
            }
            var issueKeys = new HashSet<string>();
            int shots = 0, storyShots = 0;
            System.Collections.IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"{name}.png"));
                yield return null;
                yield return null;
            }
            // Story scenes, read like a player: each line once it's typed out, then a click.
            System.Collections.IEnumerator ReadStory(string when)
            {
                var deadline = Time.realtimeSinceStartup + 120;
                string last = "";
                while (_storyScreen.IsOpen && Time.realtimeSinceStartup < deadline)
                {
                    if (_storyScreen.DebugText != last)
                    {
                        last = _storyScreen.DebugText;
                        Log($"story ({when}): {last}");
                        yield return new WaitForSecondsRealtime(full ? 0.5f : 1.2f);
                        if (storyShots < 400) yield return Shot($"story-{storyShots++:000}");
                    }
                    _storyScreen.DebugAdvance();
                    yield return new WaitForSecondsRealtime(0.4f);
                }
                if (_storyScreen.IsOpen) Log($"story ({when}) still open after 2 minutes of clicking");
            }
            void Issue(string kind, string text)
            {
                if (!issueKeys.Add(kind + "|" + text)) return;
                Log($"ISSUE [{kind}] {text}");
                if (shots++ < 60) ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"issue-{issueKeys.Count:00}-g{game}-t{_board.Turn}.png"));
            }
            // Errors from anywhere in the game, with what was happening.
            var errors = new HashSet<string>();
            void OnLog(string message, string stack, LogType type)
            {
                if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
                var first = message.Split('\n')[0];
                if (errors.Add(first)) Issue("error", $"{type}: {first}  at {stack.Split('\n').FirstOrDefault()?.Trim()}  (turn {_board.Turn}, {Phase.Name(_board.Phase)})");
            }
            Application.logMessageReceived += OnLog;
            // Every card unlock paid, held or offered (gifts, drops, bonds, bounties, sets).
            void OnGrant(Grant g) => Log($"unlock ({g.Kind}{(g.Owed ? ", held" : "")}): {StripTags(g.Text)}");
            _campaign.Granted += OnGrant;
            _qaPlaying = true;
            BoardFx.DebugBadges = Array.IndexOf(Environment.GetCommandLineArgs(), "-qabadges") >= 0;
            yield return new WaitForSecondsRealtime(1f);

            // A new campaign with the Mekk-Knight starter, through the starter screen (-qacontinue: the save already there).
            bool resume = Array.IndexOf(Environment.GetCommandLineArgs(), "-qacontinue") >= 0 && _campaign.Profile != null;
            _campaignScreen.Open();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("00-starters");
            if (!resume)
            {
                if (_campaign.Profile != null) Issue("setup", "the test folder already had a save");
                var pick = CampaignState.Starters.First(s => s.Deck == "mekk-knight-invoked");
                _campaignScreen.DebugStartWith(pick);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return ReadStory("campaign start");
            }
            yield return Shot("01-campaign");
            var profile = _campaign.Profile!;
            Log($"started: deck {profile.Deck}, {profile.Dp} DP, {profile.Collection.Total} cards, era-limited {profile.EraLimited}, your style from {QaStyle().Name}");

            int won = 0, lost = 0, mapped = 0, unmapped = 0;
            bool structureBought = !full, newGamePlusDone = !full;
            int expectedFirst = -1, tossWon = 0, tossLost = 0, tossShots = 0, startedFirst = 0, startedSecond = 0;
            var bandsSeen = new HashSet<string>();
            int winner = -1;
            float duelStart = 0;

            // One duel, answered like a player until <paramref name="over"/>: wait for the animations, think a moment, click.
            System.Collections.IEnumerator PlayDuel(string them, float think, Func<bool> over)
            {
                duelsPlayed++;
                float start = Time.realtimeSinceStartup;
                duelStart = start;
                float waitingSince = -1, busySince = -1, arrowSince = -1, badgeSince = -1, lastProgress = start, shownAt = start;
                int busyBatches = 0;
                int lastTurn = -1, lastTicks = _ticks;
                ChoiceSet? shown = null, answered = null;
                var attempts = new Dictionary<Prompt, int>();
                bool gaveUp = false;
                bool turnOneSeen = false;
                while (!over())
                {
                    yield return null;
                    float now = Time.realtimeSinceStartup;
                    if (_storyScreen.IsOpen) { Issue("story", $"a story scene is open over the duel: {_storyScreen.DebugText}"); yield return ReadStory("during a duel"); }
                    // Who the engine gave turn 1 (recorded as the duel starts; a turn can pass within a frame).
                    int turnOne = FirstTurnPlayer;
                    if (turnOne >= 0 && !turnOneSeen)
                    {
                        turnOneSeen = true;
                        // Tossed duels only (lessons set their own board and turn).
                        if (expectedFirst >= 0)
                        {
                            if (turnOne == 0) startedFirst++; else startedSecond++;
                            if (turnOne != expectedFirst)
                                Issue("toss", $"the toss gave turn 1 to {(expectedFirst == 0 ? "you" : them)}, but {(turnOne == 0 ? "you" : them)} took it");
                        }
                        expectedFirst = -1;
                    }
                    if (_board.Turn != lastTurn)
                    {
                        lastTurn = _board.Turn;
                        Log($"turn {lastTurn} ({(_board.TurnPlayer == 0 ? "you" : them)}): LP {_board.Players[0].LifePoints} / {_board.Players[1].LifePoints}");
                        yield return Shot($"g{game}-t{lastTurn:00}");
                    }
                    if (_ticks != lastTicks) { lastTicks = _ticks; lastProgress = now; }
                    bool onMe = _runner != null && _runner.IsWaiting && _runner.WaitingFor == 0 && _human != null;
                    var set = _view.Prompt.Current;
                    if (set != shown) { shown = set; shownAt = now; }

                    // Waiting on you with nothing on screen to answer.
                    if (onMe && (set == null || _human!.Current == null))
                    {
                        if (waitingSince < 0) waitingSince = now;
                        else if (now - waitingSince > 6) Issue("hang", $"the duel waits on you but nothing is shown to answer (status: {_view.StatusLine})");
                        if (waitingSince >= 0 && now - waitingSince > 25 && !gaveUp) { Log("giving up on this duel (conceding)"); gaveUp = true; ConcedeDuel(); }
                    }
                    else waitingSince = -1;
                    // An animation that never finishes holds the duel.
                    // (A long combo animates batch after batch without a break; only one batch that long is a problem.)
                    if (_view.Fx.Busy)
                    {
                        if (busySince < 0) { busySince = now; busyBatches = _view.Fx.Batches; }
                        else if (now - busySince > 20)
                        {
                            int batches = _view.Fx.Batches - busyBatches;
                            if (batches <= 1) Issue("animation", $"the board stayed busy animating for 20 s in one batch (longest batch {_view.Fx.LongestBatch:0.0} s)");
                            else Log($"a long animated stretch: 20 s without a break, {batches} batches (longest batch so far {_view.Fx.LongestBatch:0.0} s)");
                            busySince = float.MaxValue;
                        }
                    }
                    else busySince = -1;
                    // Nothing happening while it isn't your decision.
                    if (!onMe && now - lastProgress > 60) { Issue("stall", $"no progress for 60 s, not waiting on you (status: {_view.StatusLine})"); lastProgress = now; }
                    // An attack arrow still up at a Main Phase decision.
                    var arrow = _view.AttackArrowState;
                    if (arrow.showing && !arrow.preview && set?.Prompt is IdlePrompt)
                    {
                        if (arrowSince < 0) arrowSince = now;
                        else if (now - arrowSince > 1.5f) { Issue("arrow", $"an attack arrow ({arrow.readout}) is still up at a Main Phase decision"); arrowSince = float.MaxValue; }
                    }
                    else arrowSince = -1;
                    // A chain-link number left on the board after its chain is over.
                    if (_narrator != null && _narrator.Chain.Links.Count == 0 && !_view.Fx.Busy && _view.Fx.OldestBadgeAge > 3)
                    {
                        if (badgeSince < 0) badgeSince = now;
                        else if (now - badgeSince > 2) { Issue("chain badge", $"{_view.Fx.ChainBadgesShown} chain-link number(s) still on the board with no chain"); badgeSince = float.MaxValue; }
                    }
                    else badgeSince = -1;
                    if (!gaveUp && (now - start > 1500 || _board.Turn > 80)) { Issue("long", $"duel still going after {(now - start) / 60:0} min, turn {_board.Turn}; conceding"); gaveUp = true; ConcedeDuel(); }

                    // Your decision.
                    if (!onMe || set == null || set == answered || _human!.Current != set || _view.Fx.Busy || now - shownAt < think) continue;
                    attempts.TryGetValue(set.Prompt, out int attempt);
                    attempts[set.Prompt] = attempt + 1;
                    if (_human.PreviousAnswerRejected && attempt >= 3) Issue("rejected", $"\"{set.Title}\" rejected {attempt} answers in a row");
                    byte[]? want = null;
                    try { want = _qaAdvisor?.Respond(set.Prompt, attempt); }
                    catch (Exception e) { Issue("advisor", $"your stand-in failed at \"{set.Title}\": {e.Message}"); }
                    answered = set;
                    if (ClickLikeAPlayer(set, want)) { mapped++; continue; }
                    unmapped++;
                    Log($"answered \"{set.Title}\" directly (no button matched the stand-in's answer: {set.Mode}, pick {set.Min}-{set.Max} of {set.Choices.Count}, "
                        + $"cancel {set.CanCancel}, finish {set.CanFinish}, answer {(want == null ? "none" : BitConverter.ToString(want))})");
                    if (want != null) { _human.Submit(want); _answerReady = true; _view.ShowChoices(null, false); }
                    else if (set.CanCancel) _view.Prompt.CancelSelection();
                    else _view.Prompt.ClickChoice(0);
                }
                winner = _board.Winner;
            }

            // The versus screen's Duel!, at the speed for this game; with <paramref name="deck"/>, its deck arrows first,
            // until that deck (a loaner) is the one shown.
            bool versusOk = false;
            int dpAtDuel = 0;
            // Stuck, with -qatools: the player takes the Stuck? panel's purchase (owning the measured deck) when it offers one.
            var bought = new HashSet<string>();
            System.Collections.IEnumerator Versus(string? deck = null, bool stuck = false, bool buy = false)
            {
                versusOk = false;
                var until = Time.realtimeSinceStartup + 10;
                while (Time.realtimeSinceStartup < until && !(_scenes.IsOpen && _scenes.Buttons.ContainsKey("Duel!"))) yield return null;
                if (!_scenes.Buttons.ContainsKey("Duel!")) { Issue("flow", "no versus screen after the Duel button"); yield break; }
                // After three losses in a row the versus screen opens its Stuck? advice by itself (the player reads it).
                if (stuck)
                {
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return Shot($"g{game}-b-stuck");
                    if (!_scenes.ScoutingOpen) Issue("tools", "three losses in a row, but the versus screen didn't open the Stuck? advice");
                    else Log("versus: the Stuck? advice opened");
                    if (buy && _scenes.Buttons.TryGetValue("Own stuck deck", out var own))
                    {
                        string before = _versusDeck ?? "", deckBefore = profile.Deck;
                        int dpBefore = profile.Dp;
                        own(); // a purchase arms (a free build or your own copy goes at once)
                        yield return new WaitForSecondsRealtime(CampaignScenes.ConfirmDelay + 0.2f);
                        if (_versusDeck == before && _scenes.ScoutingOpen)
                        {
                            yield return Shot($"g{game}-b-stuck-confirm");
                            own();
                            yield return new WaitForSecondsRealtime(0.3f);
                        }
                        if (_versusDeck != before && _versusDeck != null)
                        {
                            bought.Add(RecipeOfDeck(_versusDeck));
                            Log($"versus: took the Stuck? purchase: {_versusDeck} ({dpBefore - profile.Dp:N0} DP, {dpBefore:N0} -> {profile.Dp:N0}), was playing {deckBefore}");
                            yield return Shot($"g{game}-b-stuck-owned");
                            if (profile.Deck != _versusDeck && profile.Dp != dpBefore) Issue("tools", $"the Stuck? purchase picked {_versusDeck} but your deck is {profile.Deck}");
                            if (_campaign.IsLoanerDeck(_versusDeck)) Issue("tools", $"the deck the Stuck? purchase built is still a loaner ({_versusDeck})");
                        }
                        else Log($"versus: the Stuck? purchase didn't go through ({dpBefore:N0} DP)");
                    }
                    if (_scenes.Buttons.TryGetValue("Close scouting", out var closeAdvice)) closeAdvice();
                }
                // A loaner of a deck you've bought: play your own copy instead.
                if (deck != null && Loaners.IsLoaner(deck) && bought.Contains(Loaners.RecipeOf(deck))) deck = null;
                if (deck != null)
                {
                    for (int n = 0; n < 200 && _versusDeck != deck; n++)
                    {
                        var arrow = _scenes.Buttons.Keys.FirstOrDefault(k => k.StartsWith(">@"));
                        if (arrow == null) break;
                        _scenes.Buttons[arrow]();
                    }
                    Log($"versus: deck arrows to {VersusDeckName(_versusDeck ?? "")}{(_campaign.IsLoanerDeck(_versusDeck ?? "") ? " (a loaner, half DP)" : "")}");
                    if (_versusDeck != deck) Issue("loaners", $"the deck arrows never reached {deck} (shows {_versusDeck})");
                }
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot($"g{game}-b-versus");
                dpAtDuel = profile.Dp;
                _scenes.Buttons["Duel!"]();
                // The coin toss: won, choose as the screen's tip says (measured for this team, else the deck's preference); lost, Continue.
                expectedFirst = -1;
                var tossUntil = Time.realtimeSinceStartup + 5;
                while (Time.realtimeSinceStartup < tossUntil && !(_scenes.IsOpen && (_scenes.Buttons.ContainsKey("Go first") || _scenes.Buttons.ContainsKey("Continue"))))
                    yield return null;
                yield return new WaitForSecondsRealtime(full ? 0.2f : 1.2f);
                if (tossShots++ < 6) yield return Shot($"g{game}-b-toss");
                if (_scenes.Buttons.ContainsKey("Go first"))
                {
                    var mine = PlaybookDeck(deck ?? profile.Deck);
                    bool goFirst = _tossSuggestsFirst;
                    expectedFirst = goFirst ? 0 : 1;
                    tossWon++;
                    Log($"coin toss: won; {(goFirst ? "going first" : "going second")} ({DeckTitle(mine)})");
                    _scenes.Buttons[goFirst ? "Go first" : "Go second"]();
                }
                else if (_scenes.Buttons.TryGetValue("Continue", out var afterToss))
                {
                    tossLost++;
                    afterToss();
                    expectedFirst = _tossFirstPlayer;
                    Log($"coin toss: lost; they chose {(expectedFirst == 1 ? "to go first" : "to let you go first")}; Continue");
                }
                else Issue("toss", "no coin toss after Duel!");
                versusOk = true;
                speedIndex = game == 1 && !full ? 3 : 5;
                UpdateControls();
                Log($"duel starts at {Speeds[speedIndex]}/s");
            }

            void DuelOverLine() =>
                Log($"duel over: {(winner == 0 ? "won" : winner == 1 ? "lost" : "draw")} on turn {_board.Turn}, LP {_board.Players[0].LifePoints} / {_board.Players[1].LifePoints}, {(Time.realtimeSinceStartup - duelStart) / 60:0.0} min");
            // The results screen's lines, the save, and whether what it lists is what was paid.
            void ResultsLine()
            {
                var r = _scenes.LastResults;
                Log($"results: {r?.Headline}, {r?.Earnings.Sum(e => e.Item2)} DP earned ({string.Join(", ", r?.Earnings.Select(e => $"{e.Item1} {e.Item2}") ?? Array.Empty<string>())}), now {profile.Dp} DP; news: {string.Join(" | ", r?.Unlocks ?? new List<string>())}");
                var saved = Profile.Load(_campaign.SavePath);
                if (saved == null || saved.Dp != profile.Dp) Issue("save", $"the save has {saved?.Dp} DP, the game {profile.Dp}");
                // What the results screen lists is what was paid.
                if (r != null && r.Earnings.Sum(e => e.Item2) != profile.Dp - dpAtDuel)
                    Issue("results", $"the results list {r.Earnings.Sum(e => e.Item2)} DP ({string.Join(", ", r.Earnings.Select(e => $"{e.Item1} {e.Item2}"))}), but {profile.Dp - dpAtDuel} DP was paid");
            }

            // The results screen, then Continue back to the circuits. False if it didn't go that way.
            System.Collections.IEnumerator Results(Action<bool> done, string? backToGuests = null)
            {
                DuelOverLine();
                var until = Time.realtimeSinceStartup + 12;
                while (Time.realtimeSinceStartup < until && !(_scenes.IsOpen && _scenes.Buttons.ContainsKey("Continue"))) yield return null;
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot($"g{game}-c-results");
                if (!_scenes.Buttons.TryGetValue("Continue", out var next)) { Issue("flow", "no results screen with Continue after the duel"); done(false); yield break; }
                ResultsLine();
                next();
                yield return new WaitForSecondsRealtime(0.8f);
                yield return ReadStory("after the results");
                if (backToGuests != null)
                {
                    if (!(_scenes.IsOpen && _scenes.Buttons.ContainsKey(backToGuests))) Issue("flow", "Continue after a guest duel didn't go back to the guests");
                    if (_scenes.Buttons.TryGetValue("Back", out var leaveGuests)) leaveGuests();
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                if (!_campaignScreen.IsOpen) Issue("flow", "Continue didn't go back to the circuits");
                yield return Shot($"g{game}-d-after");
                done(true);
            }

            // Gus's lessons, each played until passed (two tries), from the campaign's Lessons button.
            System.Collections.IEnumerator Lessons()
            {
                where = "Gus's lessons";
                _campaignScreen.Close();
                ShowLessons();
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("lessons");
                for (int i = 0; i < ChainLessons.All.Count; i++)
                {
                    var lesson = ChainLessons.All[i];
                    for (int attempt = 1; attempt <= 2; attempt++)
                    {
                        where = $"lesson {i + 1} ({lesson.Title}), try {attempt}";
                        var start = attempt == 1 && i == 0 ? "Lesson 1" : attempt == 1 ? "Next lesson" : "Try again";
                        if (!_scenes.Buttons.TryGetValue(start, out var go) && !_scenes.Buttons.TryGetValue($"Lesson {i + 1}", out go))
                        {
                            Issue("lessons", $"no \"{start}\" button (buttons: {string.Join(", ", _scenes.Buttons.Keys)})");
                            yield break;
                        }
                        go();
                        yield return new WaitForSecondsRealtime(0.5f);
                        yield return ReadStory("lesson intro");
                        speedIndex = 5;
                        UpdateControls();
                        // As Gus says for this one: the Chain pill to ON, so the game stops in his End Phase.
                        if (lesson.Id == "end-phase")
                            for (int n = 0; n < 3 && _human?.Presenter.Mode != ResponseMode.On; n++) CycleResponses();
                        yield return PlayDuel("Gus", 0.2f, () => _lessonOver && _scenes.IsOpen);
                        yield return new WaitForSecondsRealtime(0.6f);
                        yield return Shot($"lesson-{i + 1}-{attempt}");
                        bool passed = profile.Seen.Contains(lesson.SeenKey);
                        Log($"{(passed ? "passed" : "not passed")} on turn {_board.Turn}; Gus: {StripTags(_lessonJudge?.Hint ?? "")}");
                        if (passed) break;
                        if (attempt == 2) Issue("lessons", $"your stand-in didn't pass \"{lesson.Title}\" in two tries; last hint: {StripTags(_lessonJudge?.Hint ?? "")}");
                    }
                    if (!profile.Seen.Contains(lesson.SeenKey)) break;
                }
                if (_scenes.Buttons.TryGetValue("Lessons", out var back)) { back(); yield return new WaitForSecondsRealtime(0.4f); }
                if (_scenes.Buttons.TryGetValue("Back", out var leave)) leave();
                yield return new WaitForSecondsRealtime(0.6f);
                if (!_campaignScreen.IsOpen) { Issue("lessons", "Back from the lessons didn't return to the campaign"); _campaignScreen.Open(); }
            }

            // After a circuit: the Card Shop with real DP (packs of the default pick, the first featured set with commons, and
            // one booster box a run once DP covers it), the Card Trader with any tokens.
            System.Collections.IEnumerator Shopping(string why, bool selection = false)
            {
                where = $"Card Shop ({why})";
                _campaignScreen.Close();
                _shop.Open();
                yield return _shop.DebugShop(selection ? 6 : 3, 1000, Log, text => Issue("shop", text), name => Shot($"g{game}-shop-{name}"),
                    selection ? _editor.RecipeDecks?.Invoke() : null, box: _shop.DebugBoxes == 0);
                // A Structure Deck once in a run, when one's on sale and DP allows: its cards join the collection and it's
                // saved as a campaign deck.
                if (!structureBought && _structures.DebugOnSale() > 0 && profile.Dp >= 8000)
                {
                    structureBought = true;
                    where = "Structure Decks";
                    int cardsBefore = profile.Collection.Total, dpBefore = profile.Dp;
                    _structures.Open();
                    yield return new WaitForSecondsRealtime(0.6f);
                    _structures.DebugBuyFirst();
                    _library.Refresh();
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return Shot($"g{game}-structure-deck");
                    Log($"structure deck: bought {_structures.LastBought} for {dpBefore - profile.Dp:N0} DP, +{profile.Collection.Total - cardsBefore} cards ({_structures.DebugOnSale()} on sale)");
                    if (_structures.LastBought.Length == 0 || !_library.IsCampaign(_structures.LastBought) || profile.Dp >= dpBefore)
                        Issue("structure decks", $"buying one: deck '{_structures.LastBought}' ({(_library.IsCampaign(_structures.LastBought) ? "a campaign deck" : "not saved as a campaign deck")}), DP {dpBefore} -> {profile.Dp}");
                    _structures.Close();
                    yield return new WaitForSecondsRealtime(0.4f);
                }

                // The Card Trader: DP for a token when there's plenty, then tokens for cards of any era
                // (modern staples first, then the deck's own cards, then the wishlist), up to three trades.
                where = "Card Trader";
                _trader.Open();
                if (profile.Dp >= 9000 && profile.TokensBought < 4)
                {
                    int tokens = profile.Tokens, dp = profile.Dp, price = DpServices.TokenPrice(profile);
                    var said = _trader.DebugBuyToken();
                    Log($"trader: bought a token for {price:N0} DP: {StripTags(said)}");
                    if (profile.Tokens != tokens + 1 || profile.Dp != dp - price) Issue("trader", $"buying a token: tokens {tokens} -> {profile.Tokens}, DP {dp} -> {profile.Dp} (price {price})");
                }
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot($"g{game}-trader-dp");
                var staples = new[] { "Ash Blossom & Joyous Spring", "Infinite Impermanence", "Called by the Grave", "Crossout Designator", "Triple Tactics Talent" }
                    .Select(n => _cardPack!.All.FirstOrDefault(c => c.Name == n && c.Alias == 0)?.Code ?? 0).Where(c => c != 0);
                var deckNow = _library.Load(profile.Deck);
                for (int trade = 0; trade < 3 && profile.Tokens > 0; trade++)
                {
                    var wants = staples.Concat(deckNow.Main.Concat(deckNow.Extra).GroupBy(c => c).OrderByDescending(g => g.Count()).Select(g => g.Key))
                        .Concat(profile.Wanted()).Distinct().Where(c => profile.Collection.Count(c) < Collection.MaxCopies).ToList();
                    int tokens = profile.Tokens;
                    var (card, refused) = _trader.DebugTradeFor(wants);
                    if (refused.Count > 0 && trade == 0) Log($"trader refused: {string.Join("; ", refused.Take(4).Select(StripTags))}{(refused.Count > 4 ? $" (+{refused.Count - 4} more)" : "")}");
                    if (card == null) { Log($"trader: nothing to trade {tokens} token(s) for among {wants.Count} wanted cards"); break; }
                    int cost = CardTrader.Cost(card.Code, CardFoil.Printings, _cardPack!);
                    Log($"trader: {cost} token{(cost == 1 ? "" : "s")} for {card.Name} (first printed {CardFoil.Printings?.FirstYear(card.Code, card.Alias)}); now own {profile.Collection.Count(card.Code)}, {profile.Tokens} left");
                    if (profile.Tokens != tokens - cost) Issue("trader", $"tokens went from {tokens} to {profile.Tokens} for a {cost}-token trade");
                }
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot($"g{game}-trader");
                _trader.Close();
                _shop.Close();
                yield return new WaitForSecondsRealtime(0.5f);
                if (!_campaignScreen.IsOpen) _campaignScreen.Open();
            }

            // Save over most of a deck asks; QA answers Save as new: the new deck must be the campaign deck, the old kept.
            void CheckSavedAsNew(List<string> steps, string from)
            {
                if (!steps.Any(st => st.StartsWith("Save asked"))) return;
                Log($"saved as new: {profile.Deck} (was {from})");
                if (profile.Deck == from || profile.Deck != _editor.CurrentDeckName || !_library.Contains(from))
                    Issue("deck", $"Save as new: campaign deck {profile.Deck}, editor {_editor.CurrentDeckName}, old {from} {(_library.Contains(from) ? "kept" : "gone")}");
            }

            // The deck editor, as a player meets it when the Duel button refuses: + Pass on what's over the limit, Save.
            System.Collections.IEnumerator FixDeck(int circuit)
            {
                _campaignScreen.DebugShowCircuit(circuit);
                _campaignScreen.DebugEditDeck();
                yield return new WaitForSecondsRealtime(0.6f);
                if (!_editor.IsOpen) { Issue("deck", "Edit deck didn't open the editor"); yield break; }
                yield return Shot($"g{game}-editor-before");
                var deckWas = profile.Deck;
                var (steps, problems) = _editor.DebugPassAndSave();
                foreach (var step in steps) Log($"editor: {StripTags(step)}");
                CheckSavedAsNew(steps, deckWas);
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot($"g{game}-editor-after");
                if (problems > 0) Issue("deck", $"{problems} problem(s) left after Ban Passes and Auto-fix");
                _editor.Close();
                yield return new WaitForSecondsRealtime(0.5f);
                if (!_campaignScreen.IsOpen) { Issue("flow", "closing the editor didn't go back to the campaign"); _campaignScreen.Open(); }
            }

            // Three losses in a row to one opponent: a player rebuilds. Packs (a house pack of a deck they've unlocked if one's
            // on sale, else the default pick), then Recipe (the best deck they've unlocked, its missing cards on the wishlist), passes and Auto-fix
            // for what's left, Save.
            System.Collections.IEnumerator Rebuild(int circuit)
            {
                yield return Shopping("rebuilding", selection: true);
                where = "deck editor (rebuilding after 3 losses)";
                _campaignScreen.DebugShowCircuit(circuit);
                _campaignScreen.DebugEditDeck();
                yield return new WaitForSecondsRealtime(0.6f);
                if (!_editor.IsOpen) { Issue("deck", "Edit deck didn't open the editor"); yield break; }
                _editor.DebugRecipe(out var status);
                Log($"Recipe: {StripTags(status)}; wishlist now {profile.Wanted().Count} cards for {profile.WishlistFor}");
                var rebuiltFrom = profile.Deck;
                var (steps, problems) = _editor.DebugPassAndSave();
                foreach (var step in steps) Log($"editor: {StripTags(step)}");
                CheckSavedAsNew(steps, rebuiltFrom);
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot($"g{game}-editor-rebuilt");
                if (problems > 0) Issue("deck", $"{problems} problem(s) left after Recipe, Ban Passes and Auto-fix");
                _editor.Close();
                yield return new WaitForSecondsRealtime(0.5f);
                if (!_campaignScreen.IsOpen) _campaignScreen.Open();
            }

            // Deck strength as measured (campaign/deck-ratings.txt: bot-vs-bot Elo), to borrow the best loaner.
            var elo = new Dictionary<string, int>();
            try
            {
                foreach (var r in DeckRatings.Load(Path.Combine(FindDataFolder("campaign", "campaign"), DeckRatings.FileName))) elo[r.Key] = (int)r.Value;
            }
            catch (IOException) { }

            // -qatools: the player's tools at hard matches. Tag Cup matches and guests get six tries instead of two, and
            // once the Stuck? advice shows (three losses in a row) the player follows it: Kit with the strongest recipe,
            // and a stronger deck borrowed for themselves.
            bool tools = Array.IndexOf(Environment.GetCommandLineArgs(), "-qatools") >= 0;
            int retryLimit = tools ? 6 : 2;
            int Elo(string deck) => elo.GetValueOrDefault(Loaners.RecipeOf(Playbooks.Slug(deck)), elo.GetValueOrDefault(Loaners.RecipeOf(deck)));
            // A deck of yours by the recipe it plays as (one built from a recipe goes by that recipe's rating).
            string RecipeOfDeck(string name) => _library.Contains(name) ? Playbooks.RecipeOf(_library.Load(name)) : name;
            // The strongest loaner, if it's stronger than your own deck (the ratings; 1500 for a deck without one).
            string? StrongerLoaner(string except = "")
            {
                var best = _campaign.LoanerDecks().Where(l => l != except).OrderByDescending(Elo).FirstOrDefault();
                return best != null && Elo(best) > elo.GetValueOrDefault(Playbooks.Slug(RecipeOfDeck(profile.Deck)), 1500) ? best : null;
            }
            string? KitLoaner() => _campaign.LoanerDecks().OrderByDescending(Elo).FirstOrDefault();

            // A special guest from the Guests screen: one on one, or a tag duel with your partner against theirs.
            System.Collections.IEnumerator GuestDuel(Guest g, bool tag)
            {
                where = $"special guest {g.Character.Name}{(tag ? $" & {g.Partner.Name} (tag)" : "")}";
                _campaignScreen.Close();
                ShowGuests();
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot($"g{game}-guests");
                var key = (tag ? "Tag:" : "Duel:") + g.Character.Id;
                if (!_scenes.Buttons.TryGetValue(key, out var go)) { Issue("guests", $"no {key} button (buttons: {string.Join(", ", _scenes.Buttons.Keys)})"); _scenes.Close(); _campaignScreen.Open(); yield break; }
                int dpBefore = profile.Dp;
                bool firstWin = GuestList.PrizeDue(g, profile, tag);
                // Stuck: as the advice says, Kit with the strongest recipe (picked as on the Tag Cup screen) and a
                // stronger deck for yourself.
                bool stuckGuest = tools && profile.LossesInARow(GuestList.Key(g, tag)) >= StuckAfter;
                string? guestLoaner = null;
                var measuredGuest = stuckGuest ? MeasuredPicks(GuestList.Key(g, tag), tag, g.Character.Id, g.Partner.Id).FirstOrDefault() : null;
                if (measuredGuest != null)
                {
                    // The measured best, as the advice says (the partner picked as on the Tag Cup screen).
                    if (measuredGuest.Partner is { } advised) { profile.TagPartner = advised.Id; profile.PartnerDecks[advised.Id] = measuredGuest.PartnerDeck; _campaign.Save(); }
                    guestLoaner = measuredGuest.Deck;
                    Log($"stuck against {g.Character.Name} (measured: {measuredGuest.Option.Percent}%): {Describe(measuredGuest)}");
                    if (!_scenes.Buttons.TryGetValue(key, out go)) { _scenes.Close(); _campaignScreen.Open(); yield break; }
                }
                else if (stuckGuest)
                {
                    var kitDeck = tag ? KitLoaner() : null;
                    if (tag && _campaign.ShopRegular is { } kit && kitDeck != null) { profile.TagPartner = kit.Id; profile.PartnerDecks[kit.Id] = kitDeck; _campaign.Save(); }
                    guestLoaner = StrongerLoaner(kitDeck ?? "");
                    Log($"stuck against {g.Character.Name}: {(tag ? $"Kit with {DeckTitle(kitDeck ?? "")}, " : "")}you with {(guestLoaner != null ? DeckTitle(guestLoaner) : "your own deck")}");
                    if (!_scenes.Buttons.TryGetValue(key, out go)) { _scenes.Close(); _campaignScreen.Open(); yield break; }
                }
                go();
                // A guest's first meeting (guest:<id>, Ilse's ilse-table) plays before the versus: read it through. The deck they
                // play comes from GuestList.DeckFor in GuestVersus (Ilse's turns over with each win).
                var meetBy = Time.realtimeSinceStartup + 1.5f;
                while (!_storyScreen.IsOpen && !_scenes.Buttons.ContainsKey("Duel!") && Time.realtimeSinceStartup < meetBy) yield return null;
                if (_storyScreen.IsOpen) yield return ReadStory($"meeting {g.Character.Name}");
                if (!tag) Log($"guest: {g.Character.Name} plays {DeckTitle(GuestList.DeckFor(g, profile))}");
                yield return Versus(guestLoaner ?? (_versusDeck != null && _campaign.IsLoanerDeck(_versusDeck) ? profile.Deck : null), stuckGuest);
                if (!versusOk)
                {
                    Issue("guests", $"{key} didn't reach the versus screen{(_campaignScreen.IsOpen ? $": {StripTags(_campaignScreen.StatusText)}" : "")}");
                    if (!_campaignScreen.IsOpen) _campaignScreen.Open();
                    yield break;
                }
                if (tag)
                {
                    Log($"guest tag: you & {_partners.ElementAtOrDefault(0)?.Name} ({_tag?.Decks[1]}) against {g.Character.Name} & {g.Partner.Name}");
                    if (profile.TagPartner.Length > 0 && _partners.ElementAtOrDefault(0)?.Id != profile.TagPartner && profile.TagPartner != g.Character.Id && profile.TagPartner != g.Partner.Id)
                        Issue("guests", $"the guest tag duel's partner is {_partners.ElementAtOrDefault(0)?.Id}, not your pick {profile.TagPartner}");
                }
                yield return PlayDuel(tag ? $"{g.Character.Name} & {g.Partner.Name}" : g.Character.Name, 0.2f, () => _announcedResult);
                if (winner == 0) won++; else lost++;
                bool back = false;
                yield return Results(ok => back = ok, key);
                if (winner == 0 && firstWin && profile.Dp - dpBefore < g.Prize) Issue("guests", $"a first win against {g.Character.Name} paid {profile.Dp - dpBefore} DP, under the {g.Prize} DP prize");
                if (winner == 0 && !_campaign.TagPartnerChoices().Any(p => p.Id == g.Character.Id)) Issue("guests", $"{g.Character.Name} was beaten but isn't offered as a partner");
            }

            // A Tag Cup partner as a player might pick: Kit every third tag match, else the partner with the strongest
            // bond (bringing a deck other than their first when the bond allows it), else the default. Stuck: as the advice
            // says, the measured best partner and deck when the match was measured, else Kit with the strongest recipe.
            int tagMatches = 0;
            (Personality partner, string deck) PickPartner(bool stuck = false, MeasuredPick? measured = null)
            {
                tagMatches++;
                var choices = _campaign.TagPartnerChoices();
                if (measured?.Partner is { } advised)
                {
                    var (shownId, shownDeck2) = _campaignScreen.DebugPickPartner(advised.Id, measured.PartnerDeck);
                    if (shownId != advised.Id || shownDeck2 != measured.PartnerDeck) Issue("partners", $"picked {advised.Id} with {measured.PartnerDeck}, the cup shows {shownId} with {shownDeck2}");
                    Log($"tag partner picked (measured advice): {advised.DisplayName} with {DeckTitle(shownDeck2)}");
                    return (advised, shownDeck2);
                }
                Personality? pick = tagMatches % 3 == 1 || stuck ? choices.FirstOrDefault(TagPartners.IsShopRegular) : null;
                pick ??= choices.Where(c => !TagPartners.IsShopRegular(c) && profile.WinsAgainst(c.Id) >= TagPartners.DeckBond)
                    .OrderByDescending(c => profile.WinsAgainst(c.Id)).FirstOrDefault();
                pick ??= choices.First();
                var decks = _campaign.PartnerDeckChoices(pick);
                // (Kit: one of your own decks, as before the loaners; stuck, the strongest recipe.)
                var deck = stuck && TagPartners.IsShopRegular(pick) && KitLoaner() is { } kitLoaner ? kitLoaner
                    : (TagPartners.IsShopRegular(pick) ? decks.LastOrDefault(d => !Loaners.IsLoaner(d)) : decks.LastOrDefault()) ?? "";
                var (id, shownDeck) = _campaignScreen.DebugPickPartner(pick.Id, deck);
                if (id != pick.Id || (deck.Length > 0 && decks.Contains(deck) && shownDeck != deck)) Issue("partners", $"picked {pick.Id} with {deck}, the cup shows {id} with {shownDeck}");
                Log($"tag partner picked: {pick.DisplayName} with {DeckTitle(shownDeck)} ({TagPartners.Bond(pick, profile)}; {decks.Count} deck{(decks.Count == 1 ? "" : "s")} to choose from)");
                return (pick, shownDeck);
            }

            // Once a run: a second deck as a player makes one. Recipe (or Build when no recipe is ready), Save, which asks
            // over most of the deck: Save as new. One circuit duel with it follows (the loop's next match), then the
            // campaign's deck picker goes back to the old one.
            bool secondDeckDone = !full, backToFirstDeck = false;
            string firstDeck = profile.Deck, secondDeck = "";
            int duelsAtSecondDeck = 0;
            System.Collections.IEnumerator SecondDeck()
            {
                where = "deck editor (a second deck)";
                firstDeck = profile.Deck;
                _campaignScreen.DebugShowCircuit(_campaign.Circuits.Current(profile));
                _campaignScreen.DebugEditDeck();
                yield return new WaitForSecondsRealtime(0.6f);
                if (!_editor.IsOpen) { Issue("deck", "Edit deck didn't open the editor"); yield break; }
                _editor.DebugRecipe(out var status);
                Log($"Recipe: {StripTags(status)}");
                if (status.Contains("don't own enough") || status.Contains("No recipes")) Log($"Build: {StripTags(_editor.DebugBuild())}");
                var (steps, problems) = _editor.DebugPassAndSave();
                foreach (var step in steps) Log($"editor: {StripTags(step)}");
                // (The recipe can be the deck you play already: Save then says nothing changed, and there's no second deck.)
                if (steps.Any(st => st.Contains("Nothing to save"))) Log("the recipe is the deck you play already: no second deck this run");
                else if (!steps.Any(st => st.StartsWith("Save asked"))) Issue("deck", "Save after replacing most of the deck didn't ask overwrite or save as new");
                CheckSavedAsNew(steps, firstDeck);
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot($"g{game}-editor-second-deck");
                if (problems > 0) Log($"the second deck has {problems} problem(s) left; it'll be refused and fixed like any deck");
                _editor.Close();
                yield return new WaitForSecondsRealtime(0.5f);
                backToFirstDeck = profile.Deck != firstDeck;
                secondDeck = profile.Deck;
                duelsAtSecondDeck = duelsPlayed;
            }

            // The Recipe Book, once per band: the recipes unlocked against what the circuits say, and a recipe pack for
            // the one you own most of when DP allows.
            System.Collections.IEnumerator RecipeBookVisit(string band)
            {
                where = $"Recipe Book ({band} band)";
                _campaignScreen.Close();
                _recipeBook.Open();
                yield return new WaitForSecondsRealtime(0.6f);
                int total = _recipeBook.DebugCount(out var unlocked);
                int expected = RecipeBook.Unlocked(_campaign.Circuits, profile).Count;
                Log($"recipe book: {unlocked} of {total} unlocked (the circuits say {expected})");
                if (unlocked != expected) Issue("recipe book", $"shows {unlocked} unlocked, the circuits say {expected}");
                var best = RecipeBook.Unlocked(_campaign.Circuits, profile)
                    .Where(r => _library.Contains(r.Deck))
                    .OrderByDescending(r => AutoDeck.Owned(_library.Load(r.Deck), profile.Collection, Ygo.Engine.DeckBuilding.Banlist.None, _cardPack!)).FirstOrDefault();
                if (best != null && _recipeBook.DebugSelect(best.Deck))
                {
                    int price = _recipeBook.DebugCompletePrice();
                    if (profile.Dp >= 6000)
                    {
                        int spent = _recipeBook.DebugRecipePack();
                        Log($"recipe book: a recipe pack for {DeckTitle(best.Deck)} ({best.Owner.Name}) for {spent:N0} DP; completing it would cost {price:N0} DP");
                        if (spent <= 0) Issue("recipe book", $"a recipe pack for {best.Deck} cost nothing (DP {profile.Dp})");
                    }
                    else Log($"recipe book: {DeckTitle(best.Deck)} selected; completing it would cost {price:N0} DP");
                }
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot($"g{game}-recipe-book");
                _recipeBook.Close();
                yield return new WaitForSecondsRealtime(0.5f);
                if (!_campaignScreen.IsOpen) { Issue("flow", "closing the Recipe Book didn't go back to the campaign"); _campaignScreen.Open(); }
            }

            // Gus offers his guided first duel after the opening scene: taken, as a new player would.
            System.Collections.IEnumerator FirstDuel()
            {
                yield return new WaitForSecondsRealtime(0.4f);
                if (!(_scenes.IsOpen && _scenes.Buttons.TryGetValue("Practice duel", out var practice)))
                {
                    Issue("first duel", $"the opening scene didn't lead to Gus's first-duel offer (buttons: {string.Join(", ", _scenes.IsOpen ? _scenes.Buttons.Keys : new string[0])})");
                    yield break;
                }
                yield return Shot("first-duel-offer");
                where = "Gus's first duel";
                practice();
                yield return new WaitForSecondsRealtime(0.5f);
                yield return ReadStory("first duel intro");
                speedIndex = 5;
                UpdateControls();
                yield return PlayDuel("Gus", 0.2f, () => _lessonOver && _scenes.IsOpen);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("first-duel-result");
                bool passed = _campaign.Profile!.Seen.Contains(ChainLessons.FirstDuel.SeenKey);
                Log($"first duel {(passed ? "passed" : "not passed")} on turn {_board.Turn}; Gus: {StripTags(_lessonJudge?.Hint ?? "")}");
                var onward = passed ? "On to the circuit" : "Skip for now";
                if (_scenes.Buttons.TryGetValue(onward, out var go)) go();
                else Issue("first duel", $"no \"{onward}\" button after the first duel (buttons: {string.Join(", ", _scenes.Buttons.Keys)})");
                yield return new WaitForSecondsRealtime(0.6f);
                if (!_campaignScreen.IsOpen) Issue("first duel", "the first duel's result didn't lead to the circuits");
            }

            // Gus's bounty board, once per band (as a player checks what's posted): its rows and what they pay.
            var bountyBands = new HashSet<string>();
            System.Collections.IEnumerator BountyBoardVisit(string band)
            {
                where = $"bounty board ({band})";
                if (!_campaignScreen.IsOpen) _campaignScreen.Open();
                int rows = _campaignScreen.DebugOpenBounties();
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot($"g{game}-bounties-{band.ToLowerInvariant()}");
                var board = _campaign.Unlocks.Board(profile);
                Log($"bounty board: {rows} rows, {_campaign.Unlocks.OpenBounties(profile)} open ({string.Join("; ", board.Select(r => $"{r.Bounty.Id} {(r.Claimed ? "claimed" : "open")}"))}); {_campaign.Unlocks.BountyPays(profile)}");
                if (rows != 3 || !_campaignScreen.DebugBountiesOpen) Issue("bounties", $"the {band} board shows {rows} rows (open: {_campaignScreen.DebugBountiesOpen})");
                _campaignScreen.DebugCloseBounties();
            }

            // A bond 3 trade on offer: taken as a player would, from the Trade button by the Tag Cup's partner picker
            // (their first card, your first spare).
            System.Collections.IEnumerator BondTrades(int cup)
            {
                foreach (var p in _campaign.TagPartnerChoices().Where(p => _campaign.Unlocks.CanTrade(profile, p.Id)).ToList())
                {
                    where = $"bond trade with {p.Name}";
                    string partnerBefore = profile.TagPartner;
                    _campaignScreen.DebugShowTagCup(cup);
                    _campaignScreen.DebugPickPartner(p.Id);
                    var (opened, offers, gives) = _campaignScreen.DebugOpenTrade();
                    yield return new WaitForSecondsRealtime(0.5f);
                    yield return Shot($"g{game}-bond-trade-{p.Id}");
                    if (!opened) { Issue("bond trade", $"{p.Name}'s trade is on offer but the Trade dialog didn't open"); continue; }
                    int cards = profile.Collection.Total;
                    var said = _campaignScreen.DebugTakeTrade();
                    Log($"bond trade with {p.Name}: {offers} offered, {gives} spares for the first; {StripTags(said)}");
                    if (_campaign.Unlocks.CanTrade(profile, p.Id) && offers > 0 && gives > 0) Issue("bond trade", $"the trade with {p.Name} didn't go through: {StripTags(said)}");
                    var saved = Profile.Load(_campaign.SavePath);
                    if (saved == null || saved.Collection.Total != profile.Collection.Total) Issue("save", $"after the bond trade the save has {saved?.Collection.Total} cards, the game {profile.Collection.Total}");
                    _campaignScreen.DebugPickPartner(partnerBefore);
                }
            }

            if (!resume) yield return FirstDuel();
            var circuits = _campaign.Circuits;
            var walls = new HashSet<(int, int)>();
            var loanerFor = new Dictionary<(int, int), string>();
            var guestTries = new Dictionary<(string, bool), int>();
            var losses = new Dictionary<(int, int), int>();
            var tagTries = new Dictionary<(int, int), int>();
            bool lessonsDone = !full, recipeDone = !full;
            int cleared = circuits.ClearedCount(profile);
            // Picking up a save: the second deck first (a fresh campaign makes it after its first circuit).
            if (resume && full && Array.IndexOf(Environment.GetCommandLineArgs(), "-qanosecond") < 0) { secondDeckDone = true; yield return SecondDeck(); }
            else if (resume) secondDeckDone = true;

            // ---- The post-game: the Format Library ------------------------------------------------------------------------
            // Once the Library is open (the Gauntlet cleared, or a champion's save with -qacontinue), one step per game: a
            // running Locals bracket first; a cleared cup's era mat and engraving; Locals at a cup cleared this run; the next
            // cup match in shelf order; the "Every Era" final's next round; Locals at the other cleared cups. Then the campaign.
            var lib = _campaign.Library;
            bool postGameStarted = false, postGameDone = false, postGameStop = false, plaqueClicked = false, bracketBroken = false, finalStuck = false;
            var cupTries = new Dictionary<(int, int), int>();
            var roundTries = new Dictionary<int, int>();
            var binderClicked = new HashSet<int>();
            var clearedCups = new HashSet<int>();
            var clearedThisRun = new HashSet<int>();
            var localsTried = new HashSet<int>();
            var localsBlocked = new HashSet<string>();
            var cosmeticsQueue = new Queue<int>();
            int pgCupDuels = 0, pgCupsCleared = 0, pgStampsAtStart = 0, pgRoundsPlayed = 0, pgRoundsWon = 0, pgLocalsEntries = 0, pgLocalsDuels = 0, pgLocalsTitles = 0;
            var spentAtStart = new[] { Cosmetics.MatSink, Cosmetics.EngraveSink, Brackets.Sink }.ToDictionary(s => s, s => profile.DpSpent.GetValueOrDefault(s));
            int SpentSince(string sink) => profile.DpSpent.GetValueOrDefault(sink) - spentAtStart[sink];
            // The shelf's order: by year, oldest first (as CampaignScreen shelves them).
            List<int> Shelf() => Enumerable.Range(0, lib.Cups.Count).OrderBy(c => lib.Cups[c].Era).ThenBy(c => c).ToList();
            List<int> ClearedNow() => Shelf().Where(c => lib.IsCleared(c, profile)).ToList();

            // A deck of yours (no loaner, not a partner's edit) legal under these rules: your campaign deck first, or with
            // preferWon one that has won at the cup (the final, the Locals).
            string? OwnLegalDeck(FormatRules rules, string cupId, bool preferWon)
            {
                var wonHere = profile.LibraryDecks.TryGetValue(cupId, out var w) ? w : new HashSet<string>();
                int Rank(string d) => preferWon ? (wonHere.Contains(d) ? 0 : d == profile.Deck ? 1 : 2) : (d == profile.Deck ? 0 : wonHere.Contains(d) ? 1 : 2);
                return _library.CampaignNames.Where(_campaign.IsYours)
                    .OrderBy(d => Rank(d)).ThenBy(d => d, StringComparer.Ordinal)
                    .FirstOrDefault(d => OwnDeckProblems(d, rules).Count == 0);
            }

            // After the versus screen's Duel! and the toss: a new duel is on (a refused deck redraws the versus instead).
            bool duelOn = false;
            System.Collections.IEnumerator DuelBegins(object? runnerBefore)
            {
                var until = Time.realtimeSinceStartup + 8;
                while (Time.realtimeSinceStartup < until && (object.ReferenceEquals(_runner, runnerBefore) || _announcedResult)) yield return null;
                duelOn = !object.ReferenceEquals(_runner, runnerBefore) && !_announcedResult;
                if (duelOn) yield break;
                Issue("flow", $"Duel! didn't start a duel{(_scenes.IsOpen ? $" (the screen's buttons: {string.Join(", ", _scenes.Buttons.Keys.Take(8))}; {StripTags(_ownedNote)})" : "")}");
                if (_scenes.IsOpen && _scenes.Buttons.TryGetValue("Back", out var back)) back();
                yield return new WaitForSecondsRealtime(0.5f);
                if (!_campaignScreen.IsOpen && !_bracketScreen.IsOpen) _campaignScreen.Open();
            }

            // Cups cleared since the last look: their mats and engravings, and their Locals, come next.
            void NoteClears()
            {
                foreach (var c in ClearedNow())
                    if (clearedCups.Add(c))
                    {
                        clearedThisRun.Add(c);
                        cosmeticsQueue.Enqueue(c);
                        pgCupsCleared++;
                        Log($"cup cleared: {lib.Cups[c].Name} ({lib.Cups[c].Era}); Library {lib.ClearedCount(profile)}/{lib.Cups.Count} cleared, {lib.StampCount(profile)} stamps");
                    }
            }

            System.Collections.IEnumerator PostGameStart()
            {
                where = "Format Library";
                foreach (var c in ClearedNow()) clearedCups.Add(c);
                pgStampsAtStart = lib.StampCount(profile);
                var finalState = lib.FinalCleared(profile) ? "won" : lib.FinalOpen(profile) ? $"open, round {lib.FinalNext(profile) + 1} next" : "not open";
                Log($"post-game: the Format Library is open: {lib.ClearedCount(profile)}/{lib.Cups.Count} cups cleared, {pgStampsAtStart} stamps, the {lib.Final.Name} final {finalState}; "
                    + $"{profile.Dp:N0} DP, {profile.Tokens} tokens, {profile.Mats.Count} era mats, {profile.Engravings.Count} engravings, {Brackets.TitlesTotal(profile)} Locals titles"
                    + (profile.Bracket != null ? $"; the {profile.Bracket.Name} bracket is running" : ""));
                // The Library tab, as clicked: its scenes the first time.
                _campaignScreen.DebugShowDistrict(true);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return ReadStory("the Library");
                if (!_campaignScreen.IsOpen) _campaignScreen.Open();
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot($"g{game}-pg-library");
                if (_campaignScreen.LibraryCup < 0) Issue("library", "the Library tab didn't show the shelf");
                // Back from the Gauntlet (or a champion's save): one cleared cup's mat and engraving to start with.
                var shopFirst = ClearedNow().Where(c => !profile.Mats.Contains(Cosmetics.MatId(lib.Cups[c])) || Cosmetics.Engraving(profile, lib, c) == null).Take(1);
                foreach (var c in shopFirst) cosmeticsQueue.Enqueue(c);
            }

            // A cleared cup's shop on its panel: its era mat when DP allows (keeping a Locals entry), and an engraving with a
            // deck of yours that won there (picked under YOUR DECK first, then back).
            System.Collections.IEnumerator CupCosmetics(int cup)
            {
                var c = lib.Cups[cup];
                where = $"{c.Name} (era mat and engraving)";
                _campaignScreen.DebugShowLibrary(cup);
                yield return new WaitForSecondsRealtime(0.5f);
                var matId = Cosmetics.MatId(c);
                if (profile.Mats.Contains(matId)) Log($"era mat: the {c.Era} mat is yours already");
                else if (profile.Dp < Cosmetics.MatPrice + Brackets.LocalsEntry) Log($"era mat: {Cosmetics.MatPrice:N0} DP and you have {profile.Dp:N0} (keeping {Brackets.LocalsEntry:N0} for a Locals entry); not now");
                else
                {
                    int dp = profile.Dp;
                    var said = _campaignScreen.DebugCupMat(cup);
                    Log($"era mat: {StripTags(said)} ({dp:N0} -> {profile.Dp:N0} DP)");
                    if (!profile.Mats.Contains(matId) || profile.Mat != matId || dp - profile.Dp != Cosmetics.MatPrice)
                        Issue("mats", $"buying the {c.Era} mat: owned {profile.Mats.Contains(matId)}, on your side {profile.Mat == matId}, DP {dp} -> {profile.Dp} (price {Cosmetics.MatPrice})");
                    var saved = Profile.Load(_campaign.SavePath);
                    if (saved == null || !saved.Mats.Contains(matId)) Issue("save", $"the save doesn't have the {c.Era} mat just bought");
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Shot($"g{game}-pg-mat");
                }
                var decks = Cosmetics.EngraveDecks(profile, lib, cup).Where(d => _library.Contains(d))
                    .OrderBy(d => d == profile.Deck ? 0 : 1).ThenBy(d => d, StringComparer.Ordinal).ToList();
                var engraved = Cosmetics.Engraving(profile, lib, cup);
                if (engraved != null) Log($"engraving: the {c.Name} binder is engraved with {engraved} already");
                else if (decks.Count == 0) Log($"engraving: no deck of yours has won at the {c.Name} (only loaners), so nothing to engrave");
                else if (profile.Dp < Cosmetics.EngravePrice + Brackets.LocalsEntry) Log($"engraving: {Cosmetics.EngravePrice:N0} DP and you have {profile.Dp:N0} (keeping {Brackets.LocalsEntry:N0} for a Locals entry); not now");
                else
                {
                    string was = profile.Deck, pick = decks[0];
                    var shown = pick == was ? was : _campaignScreen.DebugPickDeck(pick);
                    if (shown != pick) Issue("engrave", $"the deck picker didn't reach {pick} (shows {shown}) to engrave the {c.Name} binder");
                    else
                    {
                        _campaignScreen.DebugShowLibrary(cup);
                        int dp = profile.Dp;
                        var said = _campaignScreen.DebugEngrave(cup);
                        var title = _campaign.DeckTitle(pick);
                        Log($"engraving: {StripTags(said)} ({dp:N0} -> {profile.Dp:N0} DP; {(lib.HasStamp(cup, profile) ? "gold, stamped" : "silver, not stamped")})");
                        if (Cosmetics.Engraving(profile, lib, cup) != title || dp - profile.Dp != Cosmetics.EngravePrice)
                            Issue("engrave", $"engraving the {c.Name} binder with {title}: it reads {Cosmetics.Engraving(profile, lib, cup) ?? "nothing"}, DP {dp} -> {profile.Dp} (price {Cosmetics.EngravePrice})");
                        yield return new WaitForSecondsRealtime(0.4f);
                        yield return Shot($"g{game}-pg-engraved");
                    }
                    if (profile.Deck != was && _campaignScreen.DebugPickDeck(was) != was) Issue("deck", $"the deck picker didn't get back to {was} after engraving (shows {profile.Deck})");
                }
                if (!_campaignScreen.IsOpen) _campaignScreen.Open();
            }

            // A Library cup match from its panel: a legal deck of yours, else what was measured here (else the first loaner);
            // after three losses in a row the Stuck? advice opens, and its Make it yours is taken when you have no legal deck.
            System.Collections.IEnumerator CupDuel(int cup, int match)
            {
                var c = lib.Cups[cup];
                var m = c.Matches[match];
                cupTries[(cup, match)] = cupTries.GetValueOrDefault((cup, match)) + 1;
                where = $"{c.Name} {(m.Champion ? "Champion's table" : $"match {match + 1}")} ({m.Character.Name}, {m.Deck}), try {cupTries[(cup, match)]}";
                if (binderClicked.Add(cup))
                {
                    // The binder on the shelf, as a player picks a cup: its scene the first time.
                    _campaignScreen.DebugClickBinder(cup);
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return ReadStory("the cup's binder");
                    if (!_campaignScreen.IsOpen) _campaignScreen.Open();
                }
                _campaignScreen.DebugShowLibrary(cup);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot($"g{game}-a-cup");
                if (_campaignScreen.LibraryCup != cup) Issue("library", $"the shelf shows cup {_campaignScreen.LibraryCup}, not the {c.Name} ({cup})");
                var rules = _campaign.CupRules(cup);
                var streak = lib.Key(cup, match);
                int inARow = profile.LossesInARow(streak);
                bool stuck = inARow >= StuckAfter;
                var own = OwnLegalDeck(rules, Library.Id(c), preferWon: false);
                string? deck = own;
                if (own != null) Log($"cup deck: {VersusDeckName(own)}, a deck of yours legal here ({rules.List?.Name ?? c.Banlist + " (not installed)"}, cards out by {c.Pool})");
                else
                {
                    var lend = MeasuredPicks(_campaign.LibraryAdvice.For(Profile.MatchKey(cup, match)), false, m.Character.Id).FirstOrDefault(p => p.Option.Borrows);
                    deck = lend?.Deck ?? _campaign.LoanerDecks().FirstOrDefault();
                    Log(lend != null ? $"cup deck: no deck of yours is legal here; what was measured: {Describe(lend)} (won {lend.Option.Percent}%)"
                        : deck != null ? $"cup deck: no deck of yours is legal here and nothing was measured; the first loaner, {VersusDeckName(deck)}"
                        : "cup deck: no deck of yours is legal here and there's no loaner to borrow");
                }
                if (stuck) Log($"{inARow} losses in a row here: the Stuck? advice{(own == null ? ", and its Make it yours if it offers one" : "")}");
                var refused = _campaignScreen.DebugTryCupDuel(cup, match);
                if (refused != null && own != null && own != profile.Deck)
                {
                    // Under YOUR DECK on the cup's panel, as a player would, then Duel again.
                    Log($"the cup's Duel button refused: {StripTags(refused)}; picking {own} under YOUR DECK");
                    _campaignScreen.DebugPickDeck(own);
                    _campaignScreen.DebugShowLibrary(cup);
                    refused = _campaignScreen.DebugTryCupDuel(cup, match);
                }
                if (refused != null) { Log($"the cup's Duel button refused: {StripTags(refused)}; leaving this match for now"); yield break; }
                var runner = _runner;
                yield return Versus(deck, stuck, buy: own == null);
                if (!versusOk) { postGameStop = true; yield break; }
                yield return DuelBegins(runner);
                if (!duelOn) yield break;
                pgCupDuels++;
                var playing = _pickDeck[0];
                bool lent = _campaign.IsLoanerDeck(playing);
                Log($"cup duel with {VersusDeckName(playing)}{(lent ? " (a loaner: half DP, no stamp)" : "")}");
                int stampsBefore = lib.StampCount(profile);
                yield return PlayDuel(m.Character.Name, 0.2f, () => _announcedResult);
                if (winner == 0) won++; else lost++;
                bool back = false;
                yield return Results(ok => back = ok);
                if (winner == 0 && !lib.HasWon(profile, cup, match)) Issue("library", $"won {where}, but the match isn't recorded as won");
                if (winner == 0 && !lent && !Cosmetics.EngraveDecks(profile, lib, cup).Contains(playing))
                    Issue("library", $"won at the {c.Name} with {playing}, a deck of yours, but it isn't one the binder can be engraved with");
                int needed = c.Matches.Count(x => !x.Champion), wonHere = Enumerable.Range(0, c.Matches.Count).Count(k => !c.Matches[k].Champion && lib.HasWon(profile, cup, k));
                Log($"{c.Name}: {wonHere} of {needed} won{(lib.IsCleared(cup, profile) ? ", cleared" : "")}{(lib.HasStamp(cup, profile) ? ", stamped" : "")}"
                    + $"{(winner == 0 ? profile.LibraryBuilt.Contains(streak) ? " (this win counts toward the stamp)" : " (this win doesn't count toward the stamp)" : "")}");
                if (lib.StampCount(profile) > stampsBefore) Log($"Format Master stamp: {c.Name} ({lib.StampCount(profile)} stamps)");
                if (winner != 0 && cupTries[(cup, match)] >= (m.Champion ? 2 : retryLimit))
                    Log(m.Champion ? "two losses at the Champion's table: it's optional, on to the next cup" : $"{retryLimit} tries at this match: on to the next");
                NoteClears();
                if (!back) postGameStop = true;
            }

            // Library Locals at a cleared cup: its Locals button, the entry dialog's Enter (1,000 DP), the bracket.
            System.Collections.IEnumerator EnterLocals(int cup)
            {
                var c = lib.Cups[cup];
                localsTried.Add(cup);
                where = $"{Brackets.NameOf(c)} (entry)";
                _campaignScreen.DebugShowLibrary(cup);
                yield return new WaitForSecondsRealtime(0.5f);
                var label = StripTags(_campaignScreen.DebugLocalsLabel(cup));
                int dp = profile.Dp;
                _campaignScreen.DebugLocals(cup);
                yield return new WaitForSecondsRealtime(0.5f);
                if (!_bracketScreen.EntryOpen) { Issue("locals", $"the {c.Name}'s Locals button (\"{label}\") didn't open the entry dialog: {StripTags(_campaignScreen.StatusText)}"); yield break; }
                yield return Shot($"g{game}-pg-locals-entry");
                Log($"locals: \"{label}\" opened the entry dialog ({_bracketScreen.DebugEntryRows} seeds); Enter");
                _bracketScreen.DebugEnter();
                var until = Time.realtimeSinceStartup + 4;
                while (Time.realtimeSinceStartup < until && !_bracketScreen.IsOpen && !_storyScreen.IsOpen) yield return null;
                yield return ReadStory("Locals entry");
                until = Time.realtimeSinceStartup + 4;
                while (Time.realtimeSinceStartup < until && !_bracketScreen.IsOpen) yield return null;
                var b = profile.Bracket;
                if (b == null || !_bracketScreen.IsOpen)
                {
                    Issue("locals", $"Enter didn't open the bracket (bracket {(b == null ? "none" : b.Name)}, DP {dp} -> {profile.Dp})");
                    if (!_campaignScreen.IsOpen && !_bracketScreen.IsOpen) _campaignScreen.Open();
                    yield break;
                }
                pgLocalsEntries++;
                var field = b.Slots.Where(s => s != null).OrderBy(s => s.Seed)
                    .Select(s => $"#{s.Seed} {(s.You ? "you" : BracketCharacter(s.Character).Name)} ({(s.You ? VersusDeckName(profile.Deck) : DeckTitle(s.Deck))})");
                Log($"locals: entered the {b.Name} for {dp - profile.Dp:N0} DP ({dp:N0} -> {profile.Dp:N0}); the field: {string.Join(", ", field)}");
                if (dp - profile.Dp != Brackets.LocalsEntry) Issue("locals", $"entering took {dp - profile.Dp} DP, not {Brackets.LocalsEntry}");
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot($"g{game}-pg-bracket");
            }

            // The running bracket's screen (the cup's Locals button reopens it, as a player would).
            System.Collections.IEnumerator ToBracket()
            {
                if (_bracketScreen.IsOpen) yield break;
                int cup = BracketCup();
                if (cup >= 0) { _campaignScreen.DebugShowLibrary(cup); _campaignScreen.DebugLocals(cup); }
                yield return new WaitForSecondsRealtime(0.5f);
                if (!_bracketScreen.IsOpen) { Issue("locals", $"the Locals button didn't reopen the running {profile.Bracket?.Name} bracket"); ShowBracket(); }
                var until = Time.realtimeSinceStartup + 8;
                while (Time.realtimeSinceStartup < until && _bracketScreen.Animating) yield return null;
            }

            // The bracket results: what it paid, then its button on ("Skip to the end" once you're out, else back to the bracket).
            System.Collections.IEnumerator BracketResults(Action<bool> done)
            {
                DuelOverLine();
                string[] onward = { "Skip to the end", "Back to the bracket", "Follow the bracket" };
                var until = Time.realtimeSinceStartup + 12;
                while (Time.realtimeSinceStartup < until && !(_scenes.IsOpen && onward.Any(_scenes.Buttons.ContainsKey))) yield return null;
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot($"g{game}-c-results");
                var label = onward.FirstOrDefault(_scenes.Buttons.ContainsKey);
                if (label == null) { Issue("flow", $"no bracket results screen after the duel (buttons: {string.Join(", ", _scenes.Buttons.Keys.Take(8))})"); done(false); yield break; }
                ResultsLine();
                Log($"results: {label}");
                _scenes.Buttons[label]();
                yield return new WaitForSecondsRealtime(0.8f);
                yield return ReadStory("after the bracket results");
                until = Time.realtimeSinceStartup + 4;
                while (Time.realtimeSinceStartup < until && !_bracketScreen.IsOpen) yield return null;
                until = Time.realtimeSinceStartup + 8;
                while (Time.realtimeSinceStartup < until && _bracketScreen.Animating) yield return null;
                if (!_bracketScreen.IsOpen) Issue("flow", $"\"{label}\" after a bracket duel didn't go back to the bracket");
                yield return Shot($"g{game}-d-bracket");
                var b = profile.Bracket;
                Log(b == null ? "locals: no bracket after the results" : $"locals: {(b.Done ? "the bracket is settled" : b.YouOut ? $"out ({Brackets.PlacementText(b.Placement())})" : $"through: the {b.RoundName(b.CurrentRound())} next")}");
                done(_bracketScreen.IsOpen);
            }

            // Your bracket match: the bracket's Duel, a deck legal under the cup's rules (yours, else a loaner), the duel.
            System.Collections.IEnumerator BracketDuel()
            {
                var b = profile.Bracket!;
                int cup = BracketCup(), m = b.YourMatch();
                var them = b.Slots[b.Opponent(m)];
                var who = BracketCharacter(them.Character);
                where = $"{b.Name} {b.RoundName(b.RoundOf(m))} ({who.Name}, {them.Deck})";
                yield return ToBracket();
                string? deck = null;
                if (cup >= 0)
                {
                    deck = OwnLegalDeck(_campaign.CupRules(cup), b.Event, preferWon: true);
                    var lend = deck == null ? MeasuredPicks(BracketMeasured(them), false, them.Character).FirstOrDefault(p => p.Option.Borrows) : null;
                    if (deck == null) deck = lend?.Deck ?? _campaign.LoanerDecks().FirstOrDefault();
                    Log($"locals deck: {(deck == null ? "none legal (the versus picks)" : VersusDeckName(deck))}{(lend != null ? $", as measured against them ({lend.Option.Percent}%)" : "")}"
                        + $"{(deck != null && _campaign.IsLoanerDeck(deck) ? " (a loaner: placement DP halved)" : "")}");
                }
                var runner = _runner;
                _bracketScreen.DebugDuel();
                yield return Versus(deck);
                if (!versusOk) { postGameStop = true; yield break; }
                yield return DuelBegins(runner);
                if (!duelOn) { bracketBroken = true; yield break; }
                pgLocalsDuels++;
                yield return PlayDuel(who.Name, 0.2f, () => _announcedResult);
                if (winner == 0) won++; else lost++;
                bool back = false;
                yield return BracketResults(ok => back = ok);
                if (!back) bracketBroken = true;
            }

            // A bracket with no match of yours left: Skip to end when you're out, Finish, the Prize Packs (each revealed),
            // its story, back to the cup.
            System.Collections.IEnumerator FinishLocals()
            {
                var b = profile.Bracket!;
                int cup = BracketCup();
                where = $"{b.Name} (finish)";
                yield return ToBracket();
                if (!b.Done && b.YouOut)
                {
                    Log($"locals: out ({Brackets.PlacementText(b.Placement())}); Skip to end");
                    _bracketScreen.DebugSkip();
                    yield return new WaitForSecondsRealtime(0.3f);
                    var until = Time.realtimeSinceStartup + 8;
                    while (Time.realtimeSinceStartup < until && _bracketScreen.Animating) yield return null;
                    yield return Shot($"g{game}-pg-bracket-skipped");
                }
                if (!b.Done) { Issue("locals", $"the {b.Name} isn't settled and has no match of yours (out: {b.YouOut})"); bracketBroken = true; yield break; }
                int place = b.Placement(), dp = profile.Dp;
                string name = b.Name, cupId = b.Event;
                var champion = b.Champion >= 0 ? b.Slots[b.Champion] : null;
                int titlesBefore = profile.Locals.TryGetValue(cupId, out var was) ? was.Titles : 0;
                var prizesBefore = new HashSet<int>(profile.Collection.All.Where(x => x.Source == CopySource.Prize).Select(x => x.Id));
                Log($"locals: the {name} is settled, champion {(champion == null ? "none" : champion.You ? "you" : BracketCharacter(champion.Character).Name)}; you {Brackets.PlacementText(place)}; Finish");
                _bracketScreen.DebugFinish();
                yield return new WaitForSecondsRealtime(0.8f);
                int packs = 0;
                while (_shop.DebugPrize().open && packs < 6)
                {
                    packs++;
                    yield return _shop.DebugRevealPrize();
                    yield return new WaitForSecondsRealtime(1.2f);
                    yield return Shot($"g{game}-pg-prize-{packs}");
                    var p = _shop.DebugPrize();
                    Log($"prize pack {packs}: {p.revealed} of {p.cards} cards revealed, {p.left} more pack(s)");
                    if (p.cards != PrizePacks.Size || p.revealed != p.cards) Issue("prizes", $"a Prize Pack showed {p.cards} cards ({p.revealed} revealed), not {PrizePacks.Size}");
                    if (!_shop.DebugNextPrize()) { _shop.DebugFinishPrizes(); break; }
                    yield return new WaitForSecondsRealtime(0.6f);
                }
                yield return new WaitForSecondsRealtime(0.6f);
                yield return ReadStory("after the Locals");
                var back = Time.realtimeSinceStartup + 4;
                while (Time.realtimeSinceStartup < back && !_campaignScreen.IsOpen) yield return null;
                var pulls = profile.Collection.All.Where(x => x.Source == CopySource.Prize && !prizesBefore.Contains(x.Id))
                    .Select(x => $"{_cardPack!.Get(x.Code)?.Name ?? x.Code.ToString()} ({x.Rarity}{(x.IsSlab ? $", slab {x.Slab}" : "")})").ToList();
                var rec = Brackets.Record(profile, cupId);
                if (place == 0) pgLocalsTitles++;
                Log($"locals: finished the {name}: {Brackets.PlacementText(place)}, +{profile.Dp - dp:N0} DP at the finish, {packs} Prize Pack{(packs == 1 ? "" : "s")}"
                    + (pulls.Count > 0 ? $"; pulls: {string.Join(", ", pulls)}" : "") + $"; at this cup: {rec.Entries} entries, {rec.Titles} titles, best {Brackets.PlacementText(rec.Best)}");
                yield return Shot($"g{game}-pg-locals-done");
                if (profile.Bracket != null) { Issue("locals", $"Finish left the {name} bracket running"); bracketBroken = true; }
                if (place >= 0 && place < Brackets.LocalsPacks.Length && packs != Brackets.LocalsPacks[place])
                    Issue("prizes", $"{Brackets.PlacementText(place)} showed {packs} Prize Pack(s), not {Brackets.LocalsPacks[place]}");
                if (place == 0 && rec.Titles != titlesBefore + 1) Issue("locals", $"a title at the {name}, but the cup's titles went {titlesBefore} -> {rec.Titles}");
                if (!_campaignScreen.IsOpen) { Issue("flow", $"after the {name}'s prizes the campaign screen didn't come back"); _campaignScreen.Open(); }
                else if (cup >= 0 && _campaignScreen.LibraryCup != cup) Issue("flow", $"after the {name} the Library shows cup {_campaignScreen.LibraryCup}, not its cup {cup}");
            }

            // A round of the final with no legal deck of yours: its versus, as a player looks (Scout, and the Stuck? panel's
            // Make it yours if it shows one), else what Make it yours does: the measured deck owned under the round's rules.
            System.Collections.IEnumerator OwnForRound(int round)
            {
                var rd = lib.Rounds[round];
                var c = lib.Cups[rd.Cup];
                var rules = _campaign.RoundRules(round);
                _campaignScreen.DebugRoundDuel(round);
                var until = Time.realtimeSinceStartup + 6;
                while (Time.realtimeSinceStartup < until && !(_scenes.IsOpen && _scenes.Buttons.ContainsKey("Duel!"))) yield return null;
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot($"g{game}-b-round-no-deck");
                if (_scenes.Buttons.TryGetValue("Scout", out var scout)) { scout(); yield return new WaitForSecondsRealtime(0.4f); }
                if (_scenes.Buttons.TryGetValue("Own stuck deck", out var offer))
                {
                    int dp = profile.Dp;
                    var before = _versusDeck;
                    offer();
                    yield return new WaitForSecondsRealtime(CampaignScenes.ConfirmDelay + 0.2f);
                    if (_versusDeck == before && _scenes.ScoutingOpen) { offer(); yield return new WaitForSecondsRealtime(0.3f); }
                    Log($"round {round + 1}: took Make it yours: {_versusDeck} ({dp - profile.Dp:N0} DP)");
                }
                else
                {
                    Issue("final", $"no deck of yours is legal for round {round + 1} ({c.Era}: {c.Banlist} list, cards out by {c.Pool}, no Ban Passes) and its versus offers no Make it yours "
                        + $"(the offer waits for {StuckAfter} losses in a row, and the round's versus has no Stuck? advice to show it)");
                    var pick = MeasuredPicks(RoundMeasured(round), false, rd.Match.Character.Id).FirstOrDefault(p => p.Option.Borrows);
                    var loaner = pick?.Deck ?? Loaners.NameFor(rd.Match.Deck);
                    var quote = _campaign.QuoteOwn(loaner, rules);
                    int dp = profile.Dp;
                    var built = quote == null ? null : quote.Existing.Length > 0 ? quote.Existing : _campaign.Own(quote, rules);
                    Log(built != null
                        ? $"round {round + 1}: owned {DeckTitle(Loaners.RecipeOf(loaner))} as Make it yours does{(pick != null ? $" (measured: {pick.Option.Percent}%)" : " (the boss's own deck)")}: {built}, {dp - profile.Dp:N0} DP (not a click: the versus doesn't show the offer)"
                        : $"round {round + 1}: couldn't own {DeckTitle(Loaners.RecipeOf(loaner))} ({(quote == null ? "no quote" : $"{quote.Price:N0} DP, you have {profile.Dp:N0}, {quote.StandIns} stand-ins")})");
                }
                if (_scenes.Buttons.TryGetValue("Close scouting", out var close)) close();
                if (_scenes.Buttons.TryGetValue("Back", out var leave)) leave();
                yield return new WaitForSecondsRealtime(0.5f);
                if (!_campaignScreen.IsOpen) _campaignScreen.Open();
            }

            // The "Every Era" final's next round: the plaque (Ilse's rules, once), a deck of yours legal for the round's era
            // with no Ban Passes (one that won at its cup first), the round's versus, the duel, its results and story.
            System.Collections.IEnumerator RoundDuel(int round)
            {
                var rd = lib.Rounds[round];
                var c = lib.Cups[rd.Cup];
                var m = rd.Match;
                roundTries[round] = roundTries.GetValueOrDefault(round) + 1;
                where = $"{lib.Final.Name} round {round + 1} ({c.Era}: {m.Character.Name}, {m.Deck}), try {roundTries[round]}";
                if (!plaqueClicked)
                {
                    plaqueClicked = true;
                    _campaignScreen.DebugShowLibrary(lib.Cups.Count);
                    _campaignScreen.DebugClickFinalPlaque();
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return ReadStory("the final's plaque");
                    if (!_campaignScreen.IsOpen) _campaignScreen.Open();
                }
                _campaignScreen.DebugShowLibrary(lib.Cups.Count);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot($"g{game}-a-final");
                if (_campaignScreen.LibraryCup != lib.Cups.Count) Issue("final", $"the final's plaque didn't show its panel (shows cup {_campaignScreen.LibraryCup})");
                var rules = _campaign.RoundRules(round);
                var cupId = Library.Id(c);
                var own = OwnLegalDeck(rules, cupId, preferWon: true);
                if (own == null)
                {
                    yield return OwnForRound(round);
                    own = OwnLegalDeck(rules, cupId, preferWon: true);
                    if (own == null) { Log($"still no deck of yours legal for round {round + 1}: the final stops here"); finalStuck = true; yield break; }
                    _campaignScreen.DebugShowLibrary(lib.Cups.Count);
                    yield return new WaitForSecondsRealtime(0.4f);
                }
                bool wonThere = profile.LibraryDecks.TryGetValue(cupId, out var winners) && winners.Contains(own);
                Log($"round deck: {VersusDeckName(own)}{(wonThere ? $", which won at the {c.Name}" : "")}, legal for {c.Era} with no Ban Passes");
                // (Three losses in a row at a round: the versus should open its Stuck? advice, as at any match.)
                int roundLosses = profile.LossesInARow(lib.FinalKey(round));
                if (roundLosses >= StuckAfter) Log($"{roundLosses} losses in a row at this round: looking for the Stuck? advice");
                var runner = _runner;
                _campaignScreen.DebugRoundDuel(round);
                yield return Versus(own, stuck: roundLosses >= StuckAfter);
                if (!versusOk) { postGameStop = true; yield break; }
                yield return DuelBegins(runner);
                if (!duelOn) yield break;
                pgRoundsPlayed++;
                bool finalBefore = lib.FinalCleared(profile);
                yield return PlayDuel(m.Character.Name, 0.2f, () => _announcedResult);
                if (winner == 0) { won++; pgRoundsWon++; } else lost++;
                bool back = false;
                yield return Results(ok => back = ok);
                if (winner == 0 && !lib.FinalWon(profile, round)) Issue("final", $"won round {round + 1}, but it isn't recorded as won");
                int roundsWon = Enumerable.Range(0, lib.Rounds.Count).Count(r => lib.FinalWon(profile, r));
                Log($"{lib.Final.Name}: {roundsWon} of {lib.Rounds.Count} rounds won{(lib.FinalCleared(profile) ? ", the final is won" : winner != 0 && roundTries[round] >= retryLimit ? $"; {retryLimit} tries at round {round + 1}, the final waits" : "")}");
                if (!finalBefore && lib.FinalCleared(profile))
                {
                    Log($"the {lib.Final.Name} final is won (its epilogue read above)");
                    if (!profile.Seen.Contains("every-era-clear")) Issue("final", "the final was won, but its epilogue scene (every-era-clear) wasn't shown");
                }
                if (!back) postGameStop = true;
            }

            // The next Library cup match: in shelf order, a cup's matches not won yet (each tried up to the retry limit),
            // then, once it's cleared, its Champion's table (twice at most: it's optional).
            (int cup, int match) NextCupMatch()
            {
                foreach (var c in Shelf())
                {
                    var matches = lib.Cups[c].Matches;
                    for (int m = 0; m < matches.Count; m++)
                        if (!matches[m].Champion && !lib.HasWon(profile, c, m) && cupTries.GetValueOrDefault((c, m)) < retryLimit) return (c, m);
                    if (!lib.IsCleared(c, profile)) continue;
                    for (int m = 0; m < matches.Count; m++)
                        if (matches[m].Champion && !lib.HasWon(profile, c, m) && cupTries.GetValueOrDefault((c, m)) < 2) return (c, m);
                }
                return (-1, -1);
            }

            int NextRound()
            {
                if (finalStuck || !lib.FinalOpen(profile) || lib.FinalCleared(profile)) return -1;
                int r = lib.FinalNext(profile);
                return r >= 0 && roundTries.GetValueOrDefault(r) < retryLimit ? r : -1;
            }

            // A cleared cup to enter Locals at (once a run; thisRun: only cups cleared in this run), or -1.
            int NextLocals(bool thisRun)
            {
                if (profile.Bracket != null) return -1;
                foreach (var c in Shelf())
                {
                    if (localsTried.Contains(c) || !lib.IsCleared(c, profile) || (thisRun && !clearedThisRun.Contains(c))) continue;
                    var why = Brackets.EnterBlocked(profile, lib, c);
                    if (why == null) return c;
                    if (localsBlocked.Add($"{c}|{why}")) Log($"locals: can't enter the {Brackets.NameOf(lib.Cups[c])}: {why}");
                }
                return -1;
            }

            // One post-game step: what needs no duel (a finished bracket, a cup's shop, a Locals entry), then one duel.
            System.Collections.IEnumerator PostGameStep()
            {
                for (int step = 0; step < 8; step++)
                {
                    if (profile.Bracket != null && !bracketBroken)
                    {
                        if (profile.Bracket.YourMatch() >= 0) { yield return BracketDuel(); yield break; }
                        yield return FinishLocals();
                        continue;
                    }
                    if (cosmeticsQueue.Count > 0) { yield return CupCosmetics(cosmeticsQueue.Dequeue()); continue; }
                    int at = NextLocals(thisRun: true);
                    if (at >= 0) { yield return EnterLocals(at); continue; }
                    var (cup, match) = NextCupMatch();
                    if (cup >= 0) { yield return CupDuel(cup, match); yield break; }
                    int round = NextRound();
                    if (round >= 0) { yield return RoundDuel(round); yield break; }
                    at = NextLocals(thisRun: false);
                    if (at >= 0) { yield return EnterLocals(at); continue; }
                    postGameDone = true;
                    yield break;
                }
            }

            for (game = 1; game <= games; game++)
            {
                // Between duels, what a player does once a circuit is cleared.
                if (full && circuits.ClearedCount(profile) > cleared)
                {
                    cleared = circuits.ClearedCount(profile);
                    if (!lessonsDone) { lessonsDone = true; yield return Lessons(); }
                    if (!recipeDone)
                    {
                        // A deck they'd like to build next: Recipe in the editor puts its missing cards on the wishlist.
                        recipeDone = true;
                        where = "deck editor (Recipe)";
                        _campaignScreen.DebugEditDeck();
                        yield return new WaitForSecondsRealtime(0.6f);
                        _editor.DebugRecipe(out var status);
                        Log($"Recipe: {StripTags(status)}; wishlist now {profile.Wanted().Count} cards for {profile.WishlistFor}");
                        yield return Shot($"g{game}-editor-recipe");
                        _editor.Close();
                        yield return new WaitForSecondsRealtime(0.5f);
                    }
                    yield return Shopping($"{circuits.All[cleared - 1].Name} cleared");
                    if (!secondDeckDone) { secondDeckDone = true; yield return SecondDeck(); }
                    var band = circuits.All[Math.Min(cleared, circuits.All.Count - 1)].Band;
                    if (full && bandsSeen.Add(band)) yield return RecipeBookVisit(band);
                }

                // The post-game once the Format Library is open (after the Gauntlet, or straight away on a champion's save);
                // the campaign again once there's nothing left in it to play.
                if (!postGameDone && profile.LibraryOpen && lib.Cups.Count > 0)
                {
                    if (!postGameStarted) { postGameStarted = true; yield return PostGameStart(); }
                    yield return PostGameStep();
                    if (postGameStop) break;
                    if (!postGameDone) continue;
                    where = "Format Library";
                    Log("post-game: nothing left to play in the Library this run; back to the campaign");
                    _campaignScreen.DebugShowDistrict(false);
                    yield return new WaitForSecondsRealtime(0.4f);
                }

                // A Tag Cup when one is open (each match tried twice at most), else the next circuit match.
                int cup = -1, cupMatch = -1;
                for (int t = 0; full && t < circuits.TagCups.Count && cup < 0; t++)
                    if (circuits.IsTagCupOpen(t, profile) && !circuits.IsTagCupCleared(t, profile))
                        for (int m = 0; m < circuits.TagCups[t].Matches.Count; m++)
                            if (!profile.MatchWins.ContainsKey(Circuits.TagKey(t, m)) && tagTries.GetValueOrDefault((t, m)) < retryLimit) { cup = t; cupMatch = m; break; }
                // The bounty board when a band opens (the newest circuit's band).
                if (_campaign.Unlocks.BoardBand(profile) is { Length: > 0 } boardBand && bountyBands.Add(boardBand))
                    yield return BountyBoardVisit(boardBand);
                if (cup >= 0)
                {
                    yield return BondTrades(cup);
                    var (lead, second) = circuits.TagCups[cup].Matches[cupMatch];
                    tagTries[(cup, cupMatch)] = tagTries.GetValueOrDefault((cup, cupMatch)) + 1;
                    where = $"{circuits.TagCups[cup].Name} match {cupMatch + 1} ({lead.Character.Name} & {second.Character.Name})";
                    _campaignScreen.DebugShowTagCup(cup);
                    yield return new WaitForSecondsRealtime(0.4f);
                    bool stuckTag = tools && profile.LossesInARow(Circuits.TagKey(cup, cupMatch)) >= StuckAfter;
                    var measuredTag = stuckTag ? MeasuredPicks(Circuits.TagKey(cup, cupMatch), true, lead.Character.Id, second.Character.Id).FirstOrDefault() : null;
                    var (picked, pickedDeck) = PickPartner(stuckTag, measuredTag);
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Shot($"g{game}-a-tagcup");
                    var refusedTag = _campaignScreen.DebugTryTagDuel(cup, cupMatch);
                    if (refusedTag != null)
                    {
                        Log($"the tag Duel button refused: {StripTags(refusedTag)}");
                        yield return FixDeck(circuits.TagCups[cup].After);
                        _campaignScreen.DebugShowTagCup(cup);
                        refusedTag = _campaignScreen.DebugTryTagDuel(cup, cupMatch);
                        if (refusedTag != null) { Issue("deck", $"tag duel still refused after the editor: {StripTags(refusedTag)}"); tagTries[(cup, cupMatch)] = 2; continue; }
                    }
                    yield return ReadStory("partner");
                    var tagLoaner = measuredTag != null ? measuredTag.Deck : stuckTag ? StrongerLoaner(pickedDeck) : null;
                    if (stuckTag) Log($"stuck at this match{(measuredTag != null ? $" (measured: {measuredTag.Option.Percent}%)" : "")}: {picked.Name} with {DeckTitle(pickedDeck)}, you with {(tagLoaner != null && Loaners.IsLoaner(tagLoaner) ? DeckTitle(tagLoaner) : "your own deck")}");
                    yield return Versus(tagLoaner ?? (_versusDeck != null && _campaign.IsLoanerDeck(_versusDeck) ? profile.Deck : null), stuckTag, buy: tools);
                    if (!versusOk) break;
                    var playing = _partners.ElementAtOrDefault(0);
                    Log($"tag partner: {playing?.Name} with {_tag?.Decks[1]}, skill {playing?.Skill:0.00}");
                    double wantSkill = TagPartners.Playing(picked, profile).Skill;
                    if (playing?.Id != picked.Id || (pickedDeck.Length > 0 && _tag?.Decks[1] != pickedDeck) || Math.Abs((playing?.Skill ?? 0) - wantSkill) > 0.001)
                        Issue("partners", $"picked {picked.Id} with {pickedDeck} (skill {wantSkill:0.00}); the duel has {playing?.Id} with {_tag?.Decks[1]} (skill {playing?.Skill:0.00})");
                    yield return PlayDuel($"{lead.Character.Name} & {second.Character.Name}", 0.2f, () => _announcedResult);
                    if (winner == 0) won++; else lost++;
                    bool backTag = false;
                    yield return Results(ok => backTag = ok);
                    if (!backTag) break;
                    continue;
                }

                // A special guest once unlocked: one on one, then a tag duel (each tried twice at most).
                Guest? guest = null;
                bool guestTag = false;
                foreach (var g in full && Array.IndexOf(Environment.GetCommandLineArgs(), "-qanoguests") < 0 ? _campaign.Guests.All : Array.Empty<Guest>())
                {
                    if (!GuestList.IsUnlocked(g, circuits, profile)) continue;
                    foreach (var t in new[] { false, true })
                        if (guest == null && (!t || GuestList.IsTagUnlocked(g, circuits, profile)) // a tag duel waits for its gate
                            && !profile.MatchWins.ContainsKey(GuestList.Key(g, t)) && guestTries.GetValueOrDefault((g.Character.Id, t)) < retryLimit) { guest = g; guestTag = t; }
                    if (guest != null) break;
                }
                if (guest != null)
                {
                    guestTries[(guest.Character.Id, guestTag)] = guestTries.GetValueOrDefault((guest.Character.Id, guestTag)) + 1;
                    yield return GuestDuel(guest, guestTag);
                    continue;
                }

                // The next match a player would play: the first unlocked circuit with a match not won yet.
                int ci = -1, mi = -1;
                for (int i = 0; i < circuits.All.Count && ci < 0; i++)
                    if (circuits.IsUnlocked(i, profile))
                        for (int m = 0; m < circuits.All[i].Matches.Count; m++)
                            if (!profile.HasWon(i, m)) { ci = i; mi = m; break; }
                if (ci < 0)
                {
                    Log("every circuit cleared");
                    if (newGamePlusDone) break;
                    // New Game+: the circuits start over; cards, DP, level and tokens stay.
                    newGamePlusDone = true;
                    where = "New Game+";
                    int cards = profile.Collection.Total, dp = profile.Dp, level = profile.Level, tokens = profile.Tokens;
                    _campaignScreen.DebugChooseNewGamePlus();
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return ReadStory("New Game+");
                    yield return Shot($"g{game}-new-game-plus");
                    Log($"New Game+ {profile.Cycle}: {circuits.ClearedCount(profile)} circuits cleared, {profile.Collection.Total} cards, {profile.Dp:N0} DP, level {profile.Level}, {profile.Tokens} tokens");
                    if (profile.Cycle != 1 || circuits.ClearedCount(profile) != 0 || profile.Collection.Total != cards || profile.Dp != dp || profile.Level != level || profile.Tokens != tokens)
                        Issue("new game+", $"cycle {profile.Cycle}, {circuits.ClearedCount(profile)} cleared; cards {cards} -> {profile.Collection.Total}, DP {dp} -> {profile.Dp}, level {level} -> {profile.Level}, tokens {tokens} -> {profile.Tokens}");
                    cleared = 0;
                    continue;
                }
                // A wall: three losses in a row. The first time, rebuild; the second, borrow the strongest loaner (by the
                // measured deck ratings) on the versus screen until the match is won.
                // (-qaloaners: past the rebuild, which earlier runs covered: a loaner at the first wall, and from then on.)
                bool loanersFirst = Array.IndexOf(Environment.GetCommandLineArgs(), "-qaloaners") >= 0;
                if (full && loanersFirst && loanerFor.Count > 0 && !loanerFor.ContainsKey((ci, mi)))
                {
                    // Only one stronger than your own deck (in New Game+ the loaners start over with the first circuits').
                    var keep = _campaign.LoanerDecks().OrderByDescending(l => elo.GetValueOrDefault(Loaners.RecipeOf(l))).FirstOrDefault();
                    if (keep != null && elo.GetValueOrDefault(Loaners.RecipeOf(keep)) > elo.GetValueOrDefault(Playbooks.Slug(RecipeOfDeck(profile.Deck)), 1500))
                        loanerFor[(ci, mi)] = keep;
                }
                if (full && losses.GetValueOrDefault((ci, mi)) >= 3)
                {
                    losses[(ci, mi)] = 0;
                    if (!loanersFirst && walls.Add((ci, mi))) yield return Rebuild(ci);
                    else if (!loanerFor.ContainsKey((ci, mi)))
                    {
                        var pickLoaner = _campaign.LoanerDecks().OrderByDescending(l => elo.GetValueOrDefault(Loaners.RecipeOf(l))).FirstOrDefault();
                        if (pickLoaner != null)
                        {
                            loanerFor[(ci, mi)] = pickLoaner;
                            Log($"a second wall: borrowing {VersusDeckName(pickLoaner)} (Elo {elo.GetValueOrDefault(Loaners.RecipeOf(pickLoaner))}) for this match");
                        }
                    }
                }
                var opponent = circuits.All[ci].Matches[mi].Character;
                where = $"{circuits.All[ci].Name} match {mi + 1} ({opponent.Name}, {circuits.All[ci].Matches[mi].Deck})";
                yield return ReadStory("before the match");
                _campaignScreen.DebugShowCircuit(ci);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot($"g{game}-a-circuit");
                var blocked = _campaignScreen.DebugTryDuel(ci, mi);
                if (blocked != null)
                {
                    Log($"the Duel button refused: {StripTags(blocked)}");
                    // What a player would do: Ban Passes for the Forbidden and Limited cards, in the deck editor.
                    if (full) yield return FixDeck(ci);
                    else if (!UseBanPasses(ci, Log)) { Log("can't make the deck legal here; stopping"); break; }
                    _campaignScreen.DebugShowCircuit(ci);
                    blocked = _campaignScreen.DebugTryDuel(ci, mi);
                    if (blocked != null) { Issue("deck", $"still refused after Ban Passes: {StripTags(blocked)}"); break; }
                }
                // (A loaner stays picked on the next versus screen: past the wall, the arrows go back to your own deck.)
                bool stuckHere = tools && profile.LossesInARow(Profile.MatchKey(ci, mi)) >= StuckAfter;
                yield return Versus(loanerFor.GetValueOrDefault((ci, mi)) ?? (_versusDeck != null && _campaign.IsLoanerDeck(_versusDeck) ? profile.Deck : null), stuckHere, buy: tools);
                if (!versusOk) break;
                if (backToFirstDeck && duelsPlayed != duelsAtSecondDeck)
                {
                    // Other duels came first (-qacontinue on a champion's save: the Library's cups and the guests, whose deck
                    // pickers change your deck): the second deck is no longer "the next circuit duel's", so no check, and
                    // no switching back to a first deck that's stale too.
                    Log($"the second deck ({secondDeck}) isn't checked: {duelsPlayed - duelsAtSecondDeck} other duel(s) came first; playing {profile.Deck}");
                    backToFirstDeck = false;
                }
                if (backToFirstDeck)
                {
                    Log($"playing with the second deck: {_pickDeck[0]} (shown as {DeckTitle(_pickDeck[0])})");
                    if (_pickDeck[0] != secondDeck) Issue("deck", $"the duel uses {_pickDeck[0]}, not the deck you play now ({secondDeck})");
                }
                yield return PlayDuel(opponent.Name, game == 1 && !full ? 0.8f : 0.2f, () => _announcedResult);
                if (winner == 0) won++; else lost++;
                losses[(ci, mi)] = winner == 0 ? 0 : losses.GetValueOrDefault((ci, mi)) + 1;
                bool back = false;
                yield return Results(ok => back = ok);
                if (!back) break;
                if (backToFirstDeck)
                {
                    backToFirstDeck = false;
                    var shown = _campaignScreen.DebugPickDeck(firstDeck);
                    Log($"back to {firstDeck} with the deck picker (shows {shown})");
                    if (shown != firstDeck) Issue("deck", $"the deck picker didn't get back to {firstDeck} (shows {shown})");
                }
            }

            where = "end";
            Log($"coin tosses: {tossWon} won, {tossLost} lost; you took turn 1 in {startedFirst} duels, the other side in {startedSecond}");
            Log($"done: {won} won, {lost} lost; {mapped} decisions clicked, {unmapped} answered directly; {issueKeys.Count} issues; "
                + $"{circuits.ClearedCount(profile)} circuits and {Enumerable.Range(0, circuits.TagCups.Count).Count(t => circuits.IsTagCupCleared(t, profile))} Tag Cups cleared, level {profile.Level}, {profile.Dp} DP, {profile.Collection.Total} cards"
                + (!postGameStarted ? "" : $"; post-game: {pgCupDuels} cup duels, {pgCupsCleared} cups cleared ({lib.ClearedCount(profile)}/{lib.Cups.Count} in all), "
                    + $"stamps {pgStampsAtStart} -> {lib.StampCount(profile)}, final rounds {pgRoundsWon} won of {pgRoundsPlayed} played "
                    + $"({(lib.FinalCleared(profile) ? "the final won" : lib.FinalOpen(profile) ? $"round {lib.FinalNext(profile) + 1} next" : "the final not open")}), "
                    + $"Locals {pgLocalsEntries} entries, {pgLocalsDuels} duels, {pgLocalsTitles} titles; "
                    + $"DP spent: mats {SpentSince(Cosmetics.MatSink):N0}, engravings {SpentSince(Cosmetics.EngraveSink):N0}, Locals {SpentSince(Brackets.Sink):N0}"));
            Application.logMessageReceived -= OnLog;
            _campaign.Granted -= OnGrant;
            _qaPlaying = false;
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        /// <summary>
        /// `-qareplay &lt;file&gt; &lt;folder&gt;`: watches a recorded duel at 16/s and logs (with a screenshot) every time a
        /// chain-link number or an attack arrow is left on the board when nothing is happening.
        /// </summary>
        private System.Collections.IEnumerator QaReplay(string file, string folder)
        {
            Directory.CreateDirectory(folder);
            var logPath = Path.Combine(folder, "qa-replay.txt");
            void Log(string line) { Debug.Log("qa: " + line); File.AppendAllText(logPath, line + "\n"); }
            yield return new WaitForSecondsRealtime(1f);
            BoardFx.DebugBadges = true;
            var replay = Ygo.Engine.Replays.Replay.Load(file);
            if (replay == null) { Log($"not a replay: {file}"); Application.Quit(); yield break; }
            WatchReplay(replay, 0);
            speedIndex = 5;
            _paused = false;
            UpdateControls();
            float badgeSince = -1, start = Time.realtimeSinceStartup;
            int lastTurn = -1, found = 0;
            while (!_announcedResult && Time.realtimeSinceStartup - start < 600)
            {
                yield return null;
                float now = Time.realtimeSinceStartup;
                if (_board.Turn != lastTurn) { lastTurn = _board.Turn; Log($"turn {lastTurn}"); }
                bool idle = _narrator != null && _narrator.Chain.Links.Count == 0 && !_view.Fx.Busy;
                // A badge that has outlived its chain: no chain now, and it has been up longer than any chain's end takes.
                if (idle && _view.Fx.OldestBadgeAge > 3)
                {
                    if (badgeSince < 0) badgeSince = now;
                    else if (now - badgeSince > 1)
                    {
                        Log($"LEFTOVER: {_view.Fx.ChainBadgesShown} chain badge(s) on turn {_board.Turn} ({Phase.Name(_board.Phase)}); {_view.Fx.BadgeDebug}");
                        ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"leftover-{found++}-t{_board.Turn}.png"));
                        badgeSince = float.MaxValue;
                    }
                }
                else badgeSince = -1;
            }
            Log($"done: turn {_board.Turn}, {found} leftovers");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        private static string StripTags(string text) => System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");

        /// <summary>Ban Passes for every copy the circuit's list doesn't allow, as far as the allowance goes.</summary>
        private bool UseBanPasses(int circuit, Action<string> log)
        {
            var profile = _campaign.Profile!;
            var list = _campaign.ListFor(circuit);
            if (list == null) return false;
            var deck = _library.Load(profile.Deck);
            var passes = BanPasses.Of(deck);
            int allowance = BanPasses.Allowance(_campaign.Circuits, profile);
            foreach (var group in deck.Main.Concat(deck.Extra).GroupBy(c => c))
            {
                int over = group.Count() - list.Allowed(group.Key, _cardPack!) - passes.Count(p => p == group.Key);
                for (int i = 0; i < over && passes.Count < allowance; i++) passes.Add(group.Key);
            }
            BanPasses.Set(deck, passes);
            _library.Save(deck);
            log($"used Ban Passes ({passes.Count} of {allowance}): {string.Join(", ", passes.Select(p => _cardPack!.Get(p)?.Name ?? p.ToString()))}");
            return true;
        }

        /// <summary>
        /// Gives the stand-in's answer through the prompt panel, as a click (or clicks and Confirm) would: the choice whose
        /// response is that answer. False when no button gives it.
        /// </summary>
        private bool ClickLikeAPlayer(ChoiceSet set, byte[]? want)
        {
            if (want == null) return false;
            // A card selection lists indices: the same cards in another order is the same answer.
            static byte[] Canonical(byte[] r)
            {
                if (r.Length < 8 || BitConverter.ToInt32(r, 0) != 0 || r.Length != 8 + 4 * BitConverter.ToInt32(r, 4)) return r;
                var picks = Enumerable.Range(0, BitConverter.ToInt32(r, 4)).Select(i => BitConverter.ToInt32(r, 8 + 4 * i)).OrderBy(i => i);
                return r.Take(8).Concat(picks.SelectMany(BitConverter.GetBytes)).ToArray();
            }
            want = Canonical(want);
            static bool Same(byte[]? a, byte[] b) => a != null && Canonical(a).AsSpan().SequenceEqual(b);
            byte[]? Try(params int[] picks) { try { return set.Choose(picks); } catch { return null; } }
            if (set.CanCancel && Same(set.Cancel(), want)) { _view.Prompt.CancelSelection(); return true; }
            if (set.CanFinish && Same(set.Finish(), want)) { _view.Prompt.FinishSelection(); return true; }
            int n = set.Choices.Count;
            for (int i = 0; i < n; i++)
                if (Same(Try(i), want))
                {
                    _view.Prompt.ClickChoice(i);
                    if (set.Mode == ChoiceMode.Multi) _view.Prompt.Confirm();
                    return true;
                }
            if (set.Mode != ChoiceMode.Multi || n > 16) return false;
            foreach (var picks in Combos(n, Math.Max(2, set.Min), Math.Min(set.Max, 5)))
                if (Same(Try(picks), want))
                {
                    foreach (var i in picks) _view.Prompt.ClickChoice(i);
                    _view.Prompt.Confirm();
                    return true;
                }
            return false;
        }

        private static IEnumerable<int[]> Combos(int n, int min, int max)
        {
            for (int k = min; k <= max && k <= n; k++)
            {
                var idx = Enumerable.Range(0, k).ToArray();
                while (true)
                {
                    yield return (int[])idx.Clone();
                    int i = k - 1;
                    while (i >= 0 && idx[i] == n - k + i) i--;
                    if (i < 0) break;
                    idx[i]++;
                    for (int j = i + 1; j < k; j++) idx[j] = idx[j - 1] + 1;
                }
            }
        }
    }
}
