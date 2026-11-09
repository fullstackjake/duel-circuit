using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;
using Ygo.Engine.Presentation;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-alleycheck &lt;absolute folder&gt;` (with -mods &lt;empty folder&gt;): the Back Alley's Phase A, B and C loop
    /// (docs/dev/ALLEY-SPEC-2026-11-01.md section 16, steps 1-15), screenshotted ("alley-&lt;step&gt;.png"): no tab before
    /// a cup is cleared; the first cup clear opens it after its own scene; the tab in the BackAlley theme with its map and
    /// Dumpster Row's panel; the versus with the contraband and the stake, and a deck the Alley refuses; the stakes held,
    /// lost, won; a concession, the menu and a closed app all losing; the turn cap; the first-turn guard; Orvel's Table
    /// (your spare card, his slab, the rail, the buy-back at the van); the van's three tabs, its shelf and Sell desk; the
    /// Unlisted binder; Baz's chalkboard: the Alley Brawl entered, played (a real duel under the turn cap), paid, skipped to
    /// its end and won (two Contraband Packs revealed), the Midnight Open's 16 slots, its best-of-three final over a save (the
    /// pips, game 2's loser choosing, the title's packs and token) and a CPU final watched a game at a time; Nyx's Table's
    /// four seats; main street again; the ledger and the save; then the headliners' show (steps 16-18,
    /// DuelController.ShowCheck): Mass Driver Moss's route fizzling (the Combo Breaker), its payoff, the exhibition and Watch
    /// the combo. Logs "alley check: ok/FAIL ...".
    /// </summary>
    public sealed partial class DuelController
    {
        private System.Collections.IEnumerator AlleyCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            void Report(string what, bool ok) => Debug.Log($"alley check: {(ok ? "ok  " : "FAIL")} {what}");
            System.Collections.IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"alley-{name}.png"));
                yield return null;
                yield return null;
            }
            System.Collections.IEnumerator Until(Func<bool> done, float seconds)
            {
                for (float until = Time.realtimeSinceStartup + seconds; !done() && Time.realtimeSinceStartup < until;) yield return null;
            }
            yield return new WaitForSecondsRealtime(0.5f);
            if (!_campaign.AlleyReady) { Report("the alley's data, the Traditional list and printings are installed", false); Application.Quit(1); yield break; }

            // A champion's save: every circuit won, the Library open, no cup cleared yet.
            _campaign.Start(CampaignState.Starters[1]);
            var profile = _campaign.Profile!;
            for (int c = 0; c < _campaign.Circuits.All.Count; c++)
                for (int m = 0; m < _campaign.Circuits.All[c].Matches.Count; m++) profile.MatchWins[Profile.MatchKey(c, m)] = 1;
            profile.LibraryOpen = true;
            profile.Earn(Ledger.Debug, 20_000);
            profile.Exp = 50_000_000; // no level-ups in the way
            _campaign.Save();
            int start = profile.Dp - Ledger.Net(profile);
            // Nights drawn without Penny's cameo (B1), as the harness's AlleyTest draws them: steps 3-9 need a regular's stake at
            // seat 1, and her roll comes from the save's salt (a fresh one each run). The harness checks Penny.
            double pennyChance = AlleyEconomy.PennyChance;
            AlleyEconomy.PennyChance = 0;
            var data = _campaign.AlleyData;
            var ctx = _campaign.AlleyContext!;
            int dumpster = data.IndexOf("dumpster-row");
            // The starter (Warrior's Charge) runs three Reinforcement of the Army, which the Alley limits to one: a deck of
            // yours made legal for it (as a loaner is: its own cards topped up), every card in it owned.
            var legal = Loaners.Adjust(_library.Load(profile.Deck), _campaign.AlleyRules.List!, _cardPack!).Deck;
            legal.Side.Clear();
            legal.Name = "Alley Check Legal";
            legal.Campaign = true;
            legal.Notes.Remove("loaner");
            foreach (var g in legal.Main.Concat(legal.Extra).GroupBy(c => Banlist.BaseCode(c, _cardPack!)))
                for (int k = profile.Collection.Count(g.Key); k < g.Count(); k++)
                    profile.Collection.Insert(new CardCopy { Code = g.Key, Rarity = Rarity.Common, Grade = 8, Id = profile.Collection.NextId++, Source = CopySource.Pack, Paid = 10 });
            _library.Save(legal);
            var starterDeck = profile.Deck;
            profile.Deck = legal.Name;
            _campaign.Save();
            Report($"0. a deck of yours legal in the alley ({legal.Main.Count} cards; the starter wasn't: {_campaign.AlleyProblems(_library.Load(starterDeck)).FirstOrDefault()?.Message})",
                _campaign.AlleyProblems(_library.Load(legal.Name)).Count == 0);
            string Spent(Profile p) => (p.DpSpent.TryGetValue(Ledger.AlleyStake, out var n) ? n : 0).ToString();
            int StakeSpent(Profile p) => p.DpSpent.TryGetValue(Ledger.AlleyStake, out var n) ? n : 0;

            // ---- 1. No cup cleared: no tab ----
            _campaignScreen.Open();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("1-no-cup");
            var tab = _campaignScreen.DebugAlleyTab();
            Report($"1. a champion save with no cup cleared has no Back Alley tab (AlleyOpen {profile.AlleyOpen})", !tab.shown && !profile.AlleyOpen);
            _campaignScreen.Close();

            // ---- 2. The first cup clear opens it, its scene after the cup's ----
            var lib = _campaign.Library;
            int cup = 0;
            int last = lib.Cups[cup].Matches.FindLastIndex(m => !m.Champion);
            for (int m = 0; m < lib.Cups[cup].Matches.Count; m++)
                if (!lib.Cups[cup].Matches[m].Champion && m != last) profile.LibraryWins[lib.Key(cup, m)] = 1;
            var cupReward = lib.Record(profile, cup, last, 0, loaner: false, built: true, deck: profile.Deck);
            _campaign.Save();
            _campaignCup = (cup, last);
            _lastCupEvents.Clear();
            BeginCupResults(cup, last, cupReward, 0, false, true, false);
            yield return Until(() => _scenes.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("2-cup-cleared");
            int clearAt = _lastCupEvents.FindIndex(e => e.StartsWith("cupclear:")), alleyAt = _lastCupEvents.IndexOf(BackAlley.OpenEvent);
            Report($"2. a cup cleared opens the alley, and its scene follows cupclear: ({string.Join(", ", _lastCupEvents)})",
                profile.AlleyOpen && clearAt >= 0 && alleyAt > clearAt && _story.Pending(profile, new[] { BackAlley.OpenEvent }).Count > 0);
            _scenes.Close();
            _campaignCup = null;
            // The scenes are the story check's: marked seen here so they don't close the screens under test.
            foreach (var trigger in BackAlley.Triggers(data).Concat(Library.Events(cupReward)))
                foreach (var scene in _story.Pending(profile, new[] { trigger })) Story.MarkSeen(profile, scene);
            _campaign.Save();
            _campaignScreen.Open();
            yield return new WaitForSecondsRealtime(0.5f);
            tab = _campaignScreen.DebugAlleyTab();
            yield return Shot("2-tab-new");
            Report($"2. the tab shows \"{tab.label}\" while main street keeps its theme", tab.shown && tab.label.Contains("(new)") && !UiTheme.Current.IsAlley);

            // ---- 3. The tab: the theme, the map, Dumpster Row's panel ----
            _campaignScreen.DebugShowAlley(dumpster);
            yield return new WaitForSecondsRealtime(0.7f);
            yield return Shot("3-alley");
            Report($"3. the Back Alley tab draws the whole screen in the BackAlley theme (base {ColorUtility.ToHtmlStringRGB(_campaignScreen.DebugBase)})",
                UiTheme.Current.IsAlley && _campaignScreen.IsOpen && _campaignScreen.InAlley && _campaignScreen.DebugBase == UiTheme.BackAlley.Base
                && !_campaignScreen.DebugAlleyTab().label.Contains("(new)"));
            var nodes = _campaignScreen.DebugAlleyNodes;
            // Moth's van opens with the alley (Phase B); every other table and Baz's chalkboard wait.
            int vanNode = nodes.ToList().FindIndex(n => n.name == "Moth's van");
            Report($"3. the map: {string.Join(" · ", nodes.Select(n => n.open ? n.name : $"{n.name} [{n.locked}]"))}",
                nodes.Count == data.Tables.Count + 2 && nodes[dumpster].open && vanNode >= 0 && nodes[vanNode].open
                && nodes.Where((n, i) => i != dumpster && i != vanNode).All(n => !n.open && n.locked.Length > 0)
                && nodes.Any(n => n.locked == "Clear Dumpster Row"));
            var seats = _campaignScreen.AlleySeats;
            Report($"3. Dumpster Row has {seats.Count} seats ({string.Join(", ", seats.Select(s => s.Handle))}), \"{_campaignScreen.AlleyStakeLine}\", "
                   + $"seat 1 puts up {seats.FirstOrDefault()?.Stake} worth {seats.FirstOrDefault()?.StakeValue:N0}",
                seats.Count == 4 && seats.All(s => s.Handle.Length > 0) && _campaignScreen.AlleyStakeLine.Contains("300 DP")
                && seats[0].Next && seats[0].StakeValue >= AlleyEconomy.StreetStake && seats.All(s => s.StakeValue <= AlleyEconomy.StreetCap));

            // ---- 4. The versus: the contraband, the stake, an illegal deck refused ----
            _campaignScreen.DebugAlleyDuel();
            yield return Until(() => _scenes.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("4-versus");
            var seat1 = _campaign.AlleySeat(dumpster, 1)!;
            var shown = _alleyVersusShowcase;
            var expected = seat1.Kind == "regular" ? seat1.Added : data.FindHeadliner(seat1.Character)?.Pieces ?? new List<uint>();
            Report($"4. the versus shows three contraband cards ({string.Join(", ", shown.Select(c => _cardPack!.Get(c)?.Name))}), all {(seat1.Kind == "regular" ? "the regular's splash" : "the headliner's pieces")}",
                _scenes.IsOpen && shown.Count == 3 && shown.All(expected.Contains));
            var stake = _alleyVersusStake;
            int value = stake != null ? Ante.Value(stake, ctx) : -1;
            Report($"4. the stake chip shows the card and its value ({System.Text.RegularExpressions.Regex.Replace(_alleyVersusChip, "<.*?>", "")}; Ante.Value {value:N0})",
                stake != null && _alleyVersusChip.Contains($"{value:N0} DP"));
            // A deck of yours with two Pots of Greed (owned): refused with the Alley's words, nothing held.
            var deckBefore = profile.Deck;
            uint pot = Alley.CodeOf("Pot of Greed", _cardPack!);
            var pots = _library.Load(profile.Deck);
            pots.Name = "Alley Check Two Pots";
            pots.Campaign = true;
            pots.Main.Add(pot);
            pots.Main.Add(pot);
            for (int k = 0; k < 2; k++) profile.Collection.Insert(new CardCopy { Code = pot, Rarity = Rarity.Common, Grade = 8, Id = profile.Collection.NextId++, Source = CopySource.Pack, Paid = 10 });
            _library.Save(pots);
            profile.Deck = pots.Name;
            _scenes.Close();
            ShowAlleyVersus(dumpster, null);
            yield return new WaitForSecondsRealtime(0.4f);
            int dpBefore = profile.Dp;
            if (_scenes.Buttons.TryGetValue("Duel!", out var refusedDuel)) refusedDuel();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("4-refused");
            Report($"4. a deck with 2 Pot of Greed is refused: \"{System.Text.RegularExpressions.Regex.Replace(_alleyLastNote, "<.*?>", "")}\"",
                _alleyLastNote.Contains("Pot of Greed: the Alley allows 1, you have 2.") && profile.AnteLive == null && profile.Dp == dpBefore && _scenes.IsOpen);
            _scenes.Close();
            profile.Deck = deckBefore;
            _campaign.Save();

            // ---- 5. Start (held), then a forced loss ----
            string SaveText() => File.ReadAllText(_campaign.SavePath);
            string OppLine(string text, int seat) => text.Split('\n').FirstOrDefault(l => l.StartsWith($"alleyopp {seat1.Table} {seat} ")) ?? "";
            var o1 = _campaign.AlleySeat(dumpster, 1)!;
            var theirs = Ante.Next(profile, o1.Binder, ctx);
            int spent0 = StakeSpent(profile);
            dpBefore = profile.Dp;
            var live = Ante.Hold(profile, data, dumpster, o1, null, _campaign.NeededByDecks, ctx);
            _campaign.Save();
            var held = SaveText();
            _campaignScreen.Open();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("5-held");
            var wallet = _campaignScreen.DebugWallet;
            Report($"5. Duel! holds the stake: {dpBefore - profile.Dp} DP out of your wallet, the header says \"{System.Text.RegularExpressions.Regex.Replace(wallet.Split('\n')[0], "<.*?>", "")}\", the save has antelive",
                live != null && dpBefore - profile.Dp == AlleyEconomy.StreetStake && wallet.Contains($"({AlleyEconomy.StreetStake:N0} held)")
                && held.Contains("\nantelive dumpster-row 1 300 ") && StakeSpent(profile) == spent0);
            _campaignScreen.Close();
            var oppBefore = OppLine(held, 1);
            var lost = BackAlley.Record(profile, data, dumpster, 1, 12, 0, ctx);
            _campaign.Save();
            _campaign.DebugReload();
            profile = _campaign.Profile!;
            _campaign.Save();
            var binderTop = Ante.Binder(profile, o1.Binder, ctx).Cards.FirstOrDefault();
            Report($"5. a loss spends the stake to alley.stake (+{StakeSpent(profile) - spent0}), their {_cardPack!.Get(theirs?.Code ?? 0)?.Name} goes back on top of the binder, "
                   + $"and the seat's alleyopp line is the same after a reload",
                StakeSpent(profile) - spent0 == AlleyEconomy.StreetStake && profile.Dp == dpBefore - AlleyEconomy.StreetStake && profile.AnteLive == null
                && theirs != null && binderTop != null && binderTop.Code == theirs.Code && binderTop.Seed == theirs.Seed
                && oppBefore.Length > 0 && OppLine(SaveText(), 1) == oppBefore && BackAlley.NextSeat(profile, data, dumpster) == 1 && lost.Events.Contains("antelose"));

            // ---- 6. A forced win ----
            o1 = _campaign.AlleySeat(dumpster, 1)!;
            theirs = Ante.Next(profile, o1.Binder, ctx);
            dpBefore = profile.Dp;
            Ante.Hold(profile, data, dumpster, o1, null, _campaign.NeededByDecks, ctx);
            var won = BackAlley.Record(profile, data, dumpster, 0, 12, 0, ctx);
            _campaign.Save();
            var copy = won.Ante?.Won;
            bool kept = won.Ante?.Added?.Kept ?? false;
            var refusal = copy != null && kept ? SellDesk.Blocked(profile, copy, _campaign.NeededByDecks) : null;
            _campaignScreen.DebugShowAlley(dumpster);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("6-won");
            seats = _campaignScreen.AlleySeats;
            Report($"6. a win: the stake back ({profile.Dp - dpBefore:+0;-0} DP), {_campaign.StakeText(theirs ?? new Stake())} is yours (Source {copy?.Source}, Paid {copy?.Paid}), "
                   + $"the Sell desk says \"{refusal}\"",
                profile.Dp == dpBefore && copy != null && copy.Source == CopySource.Ante && copy.Paid == 0 && theirs != null && copy.Code == theirs.Code
                && (!kept || (profile.Collection.All.Contains(copy) && refusal == SellDesk.AnteRefusal)) && won.Events.Contains("antewin"));
            Report($"6. the seat shows ✓ and the next seat is playable (next: {BackAlley.NextSeat(profile, data, dumpster)})",
                seats.Count == 4 && seats[0].Won && seats[1].Next && BackAlley.NextSeat(profile, data, dumpster) == 2);

            // ---- 7. Quitting is a loss: conceding, leaving for the menu, the app closed ----
            System.Collections.IEnumerator RealDuel(string name)
            {
                _campaignScreen.Close();
                _scenes.LastResults = null;
                ShowAlleyVersus(dumpster, null);
                yield return new WaitForSecondsRealtime(0.4f);
                if (_scenes.Buttons.TryGetValue("Duel!", out var duel)) duel();
                yield return Until(() => _runner != null && _runner.IsWaiting && _runner.WaitingFor == 0 && _human?.Current != null, 30);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot(name);
            }
            int spent7 = StakeSpent(profile);
            dpBefore = profile.Dp;
            yield return RealDuel("7-duel");
            var stake7 = profile.AnteLive?.Held ?? 0;
            // (For step 11: an alley duel's top bar and music.)
            var bar7 = _view.DebugBar;
            var music7 = Sounds.DebugMusic().duelTheme;
            var music7Wanted = BossMusic.ForAlley(data, dumpster, 2);
            bool dueling = _alleyTable == dumpster && _turnCap != null && profile.AnteLive != null && !_campaignScreen.IsOpen && _players[1].Name == _campaign.AlleySeat(dumpster, 2)?.Handle;
            ConcedeDuel();
            yield return Until(() => _announcedResult, 20);
            yield return Until(() => _scenes.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("7-conceded");
            Report($"7. seat 2 plays as a real duel under the turn cap ({_players[1].Name}); conceding is a loss: -{StakeSpent(profile) - spent7} DP, the seat stays",
                dueling && _announcedResult && _board.Winner == 1 && profile.AnteLive == null && StakeSpent(profile) - spent7 == stake7 && stake7 == AlleyEconomy.StreetStake
                && BackAlley.NextSeat(profile, data, dumpster) == 2 && _scenes.LastResults?.Stakes.Any(s => s.value.StartsWith("-")) == true);
            _scenes.Close();
            // Leaving for the menu mid-duel.
            spent7 = StakeSpent(profile);
            yield return RealDuel("7-duel-again");
            stake7 = profile.AnteLive?.Held ?? 0;
            AbandonAlleyDuel();
            _paused = true;
            UpdateControls();
            Report($"7. leaving a held duel for the menu settles it as a loss at once (-{StakeSpent(profile) - spent7} DP: \"{_campaign.AlleyNotice}\")",
                profile.AnteLive == null && StakeSpent(profile) - spent7 == stake7 && _campaign.AlleyNotice.Contains("mid-duel. That counts as a loss.")
                && Profile.Load(_campaign.SavePath)?.AnteLive == null);
            _campaign.AlleyNotice = "";
            _campaign.ClearAlleyStory();
            seed++;
            PickRandomBots();
            NewDuel();
            _paused = true;
            UpdateControls();
            // The app closed mid-duel: the save has antelive; the next launch settles it.
            spent7 = StakeSpent(profile);
            var o2 = _campaign.AlleySeat(dumpster, 2)!;
            Ante.Hold(profile, data, dumpster, o2, null, _campaign.NeededByDecks, ctx);
            _campaign.Save();
            bool savedHeld = SaveText().Contains("\nantelive ");
            _campaign.DebugReload();
            profile = _campaign.Profile!;
            _campaignScreen.Open();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("7-notice");
            Report($"7. a save closed mid-duel loads as a loss with the notice (\"{System.Text.RegularExpressions.Regex.Replace(_campaignScreen.StatusText, "<.*?>", "")}\")",
                savedHeld && profile.AnteLive == null && StakeSpent(profile) - spent7 == AlleyEconomy.StreetStake
                && _campaignScreen.StatusText.Contains("You left Dumpster Row mid-duel. That counts as a loss."));

            // ---- 8. The turn cap: a real duel capped after turn 1, then a forced draw ----
            _alleyTurnCapOverride = 1;
            int counterFrom = AlleyEconomy.TurnCounterFrom;
            AlleyEconomy.TurnCounterFrom = 1;
            spent7 = StakeSpent(profile);
            dpBefore = profile.Dp;
            yield return RealDuel("8-turn-counter");
            bool counter = _view.TurnCapLast == 1;
            speedIndex = Speeds.Length - 1;
            UpdateControls();
            var clicker = StartCoroutine(AutoClick());
            yield return Until(() => _announcedResult, 120);
            StopCoroutine(clicker);
            int capWinner = _board.Winner;
            var lp = _turnCap != null ? (_turnCap.Lp(0), _turnCap.Lp(1)) : (0, 0);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("8-turncap-board");
            yield return Until(() => _scenes.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("8-turncap-results");
            var capLine = _scenes.LastResults?.Unlocks.FirstOrDefault() ?? "";
            bool rightWinner = lp.Item1 == lp.Item2 ? capWinner > 1 : capWinner == (lp.Item1 > lp.Item2 ? 0 : 1);
            Report($"8. turn 2 never starts: \"{capLine}\" (LP {lp.Item1:N0} to {lp.Item2:N0}, winner {capWinner}; the counter read \"Turn 1/1\")",
                counter && _runner?.Result.Reason == DuelRunner.ReasonTurnCap && rightWinner && capLine.StartsWith("Turn 1: ")
                && (capWinner > 1 ? capLine.Contains("A draw") : capLine.Contains("on life points")));
            _scenes.Close();
            _alleyTurnCapOverride = 0;
            AlleyEconomy.TurnCounterFrom = counterFrom;
            // Equal LP at turn 41: a draw, the stakes back, the seat unchanged.
            int next = BackAlley.NextSeat(profile, data, dumpster);
            var oNext = _campaign.AlleySeat(dumpster, next)!;
            theirs = Ante.Next(profile, oNext.Binder, ctx);
            spent7 = StakeSpent(profile);
            dpBefore = profile.Dp;
            var oppLine = OppLine(SaveText(), next);
            Ante.Hold(profile, data, dumpster, oNext, null, _campaign.NeededByDecks, ctx);
            var draw = BackAlley.Record(profile, data, dumpster, 2, AlleyEconomy.AlleyTurns + 1, 0, ctx);
            _campaign.Save();
            binderTop = Ante.Binder(profile, oNext.Binder, ctx).Cards.FirstOrDefault();
            Report($"8. a draw at turn 41: the stake back, their card back, the seat as it was ({draw.Summary}; events {string.Join(", ", draw.Events)})",
                profile.Dp == dpBefore && StakeSpent(profile) == spent7 && draw.Replay && BackAlley.NextSeat(profile, data, dumpster) == next
                && OppLine(SaveText(), next) == oppLine && binderTop?.Code == theirs?.Code && draw.Events.Contains("turncap"));
            Report($"8. the turn-40 line reads \"{DuelNarrator.TurnCapText(40, "Three-Pot Teddy", 3200, 1900)}\"",
                DuelNarrator.TurnCapText(40, "Three-Pot Teddy", 3200, 1900) == "Turn 40: Three-Pot Teddy wins on life points, 3,200 to 1,900."
                && DuelNarrator.TurnCapText(40, null, 0, 0) == "Turn 40: level on life points. A draw.");

            // ---- 9. The first-turn guard ----
            oNext = _campaign.AlleySeat(dumpster, next)!;
            theirs = Ante.Next(profile, oNext.Binder, ctx);
            spent7 = StakeSpent(profile);
            dpBefore = profile.Dp;
            Ante.Hold(profile, data, dumpster, oNext, null, _campaign.NeededByDecks, ctx);
            var guard = BackAlley.Record(profile, data, dumpster, 1, 1, 1, ctx);
            _campaign.Save();
            binderTop = Ante.Binder(profile, oNext.Binder, ctx).Cards.FirstOrDefault();
            Report($"9. lost on the duel's first turn (they went first): the stake back, no alley.stake line, their card back ({guard.Summary})",
                profile.Dp == dpBefore && StakeSpent(profile) == spent7 && binderTop?.Code == theirs?.Code && guard.Ante?.Outcome == "firstturn"
                && (theirs == null || guard.Events.Contains("firstturn")));

            // ---- 10. Orvel's Table: your spare card, his slab, the rail, the buy-back at the van ----
            profile.Earn(Ledger.Debug, 60_000); // the stake and a buy-back of a valuable card
            foreach (var id in new[] { "dumpster-row", "basement", "black-market" })
            {
                if (!profile.AlleyTables.TryGetValue(id, out var st)) profile.AlleyTables[id] = st = new AlleyTableState();
                st.Cleared = true;
            }
            _campaign.Save();
            int orvel = data.IndexOf("orvels-table");
            var orvelOpp = orvel >= 0 && BackAlley.IsUnlocked(profile, data, orvel) ? _campaign.AlleySeat(orvel, 1) : null;
            Report($"10. Orvel's Table opens with The Black Market's clear ({(orvelOpp != null ? $"{orvelOpp.Handle}, binder \"{orvelOpp.Binder}\"" : "no seat")})",
                orvelOpp != null && orvelOpp.Kind == "authored");
            if (orvelOpp != null)
            {
                // Two spare cards Orvel would match (each the same printing and grade as a slab in his binder, so worth 1×), and
                // a card a saved deck needs, made valuable: it must never be offered.
                var binder = Ante.Binder(profile, orvelOpp.Binder, ctx);
                var spares = binder.Cards.Take(2).Select(st => new CardCopy
                {
                    Code = st.Code, Rarity = st.Rarity, Grade = st.Grade, Slab = st.Slab, Floor = st.Slab, Seed = st.Seed, Id = profile.Collection.NextId++,
                    Source = CopySource.Pack, Paid = Ante.Value(st, ctx),
                }).ToList();
                foreach (var c in spares) profile.Collection.Insert(c);
                var deckCards = _library.Load(profile.Deck).Main.Select(c => Banlist.BaseCode(c, _cardPack!)).Distinct()
                    .Where(c => profile.Collection.Count(c) == _campaign.NeededByDecks(c)).ToList();
                var needed = deckCards.Select(c => profile.Collection.CopiesOf(c).FirstOrDefault()).OfType<CardCopy>().FirstOrDefault();
                if (needed != null)
                {
                    needed.Rarity = _campaign.Printings!.Of(needed.Code, _cardPack!.Get(needed.Code)?.Alias ?? 0).Select(pr => pr.Rarity).DefaultIfEmpty(needed.Rarity).Max();
                    needed.Grade = needed.Slab = needed.Floor = 10;
                }
                _campaign.Save();
                _campaignScreen.DebugShowAlley(orvel);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("10-orvel-panel");
                var rows = _campaignScreen.OrvelRows;
                Report($"10. Orvel's panel: one seat, both stakes (\"{_campaignScreen.AlleyStakeLine}\"), {rows.Count} spare cards, {rows.Count(r => r.his != null)} he matches",
                    _campaignScreen.AlleyStakeLine.Contains($"{AlleyEconomy.OrvelStake:N0} DP") && spares.All(c => rows.Any(r => r.copy == c && r.his != null)));
                _campaignScreen.DebugAlleyDuel();
                yield return Until(() => _scenes.IsOpen, 5);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("10-orvel-versus");
                var offered = _orvelChoices.ToList();
                bool onlySpares = offered.Count > 0 && offered.All(c => profile.Collection.Count(c.Code) - 1 >= _campaign.NeededByDecks(c.Code)
                    && profile.Collection.Value(c) >= AlleyEconomy.OrvelMinCard && !c.AtDesk) && (needed == null || !offered.Contains(needed));
                Report($"10. the stake picker lists only spare copies worth {AlleyEconomy.OrvelMinCard:N0}+ ({offered.Count}: {string.Join(", ", offered.Take(4).Select(c => _campaign.CopyText(c)))}; "
                       + $"the deck's {(needed != null ? $"{_cardPack!.Get(needed.Code)?.Name} at {profile.Collection.Value(needed):N0} DP" : "card")} isn't offered)",
                    onlySpares && _scenes.Buttons.ContainsKey("stake>"));
                // The arrows walk the spares; then back to one Orvel matches.
                var first = _orvelPick;
                if (_scenes.Buttons.TryGetValue("stake>", out var nextStake)) nextStake();
                yield return new WaitForSecondsRealtime(0.3f);
                bool moved = offered.Count < 2 || _orvelPick != first;
                _orvelPick = spares[0];
                ShowAlleyVersus(orvel, null, cycled: true);
                yield return new WaitForSecondsRealtime(0.4f);
                var his = _alleyVersusStake;
                int mine = profile.Collection.Value(spares[0]);
                Report($"10. the arrows change your card ({moved}); against {_campaign.CopyText(spares[0])} ({mine:N0} DP) he puts up {(his != null ? $"{_campaign.StakeText(his)} ({Ante.Value(his, ctx):N0} DP)" : "nothing")}",
                    moved && his != null && Ante.Value(his, ctx) >= AlleyEconomy.OrvelMatchMin * mine && Ante.Value(his, ctx) <= AlleyEconomy.OrvelMatchMax * mine);
                _scenes.Close();
                // Held (as Duel! holds it), then a forced loss: your card goes on Moth's rail at 1.5× its value.
                dpBefore = profile.Dp;
                var pick = _orvelPick!;
                int pickValue = profile.Collection.Value(pick);
                var orvelLive = Ante.Hold(profile, data, orvel, orvelOpp, pick, _campaign.NeededByDecks, ctx);
                _campaign.Save();
                bool heldBoth = orvelLive != null && orvelLive.Yours == pick && !profile.Collection.All.Contains(pick) && dpBefore - profile.Dp == AlleyEconomy.OrvelStake;
                var orvelLoss = BackAlley.Record(profile, data, orvel, 1, 12, 0, ctx);
                _campaign.Save();
                var railed = orvelLoss.Ante?.Railed;
                Report($"10. Duel! holds {AlleyEconomy.OrvelStake:N0} DP and your card; a loss puts it on the rail at {railed?.Price:N0} DP (1.5 × {pickValue:N0}), "
                       + $"{(railed != null ? Ante.DuelsLeft(profile, railed) : 0)} duels left ({string.Join(", ", orvelLoss.Events)})",
                    heldBoth && railed != null && railed.Copy.Id == pick.Id && railed.Price == AlleyEconomy.RailPrice(pickValue)
                    && profile.Rail.Contains(railed) && Ante.DuelsLeft(profile, railed) == AlleyEconomy.RailDuels && orvelLoss.Events.Contains("rail"));
                // The van's Used & graded tab shows it, top row; buying it back returns the same copy.
                if (railed != null)
                {
                    _campaignScreen.DebugShowAlley(orvel);
                    yield return new WaitForSecondsRealtime(0.3f);
                    _campaignScreen.DebugVan();
                    yield return Until(() => _van.IsOpen, 5);
                    _van.DebugShow(1);
                    yield return new WaitForSecondsRealtime(0.5f);
                    yield return Shot("10-van-rail");
                    var tag = $"Your {_cardPack!.Get(pick.Code)?.Name} · {(pick.IsSlab ? $"Grade {pick.Slab} slab" : Grades.RawLabel(pick.Grade))} · {railed.Price:N0} DP · {AlleyEconomy.RailDuels} duels left";
                    var listText = _van.DebugListText();
                    // The list's text comes tag-stripped, and so must the line it's looked for in ("Maliss <Q> White Binder").
                    var plainTag = System.Text.RegularExpressions.Regex.Replace(tag, "<.*?>", "");
                    Report($"10. the van's Used & graded tab shows it first: \"{tag}\"", listText.Contains(plainTag) && listText.IndexOf(plainTag) < listText.IndexOf("USED CONTRABAND"));
                    int railSpent = profile.DpSpent.GetValueOrDefault(Ledger.AlleyRail), paidBefore = pick.Paid;
                    dpBefore = profile.Dp;
                    bool back = _van.DebugBuyBack(railed);
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Shot("10-van-bought-back");
                    var returned = profile.Collection.All.FirstOrDefault(c => c.Id == pick.Id);
                    Report($"10. buying it back returns the same copy (id {pick.Id}, Paid {paidBefore:N0} → {returned?.Paid:N0}; alley.rail +{profile.DpSpent.GetValueOrDefault(Ledger.AlleyRail) - railSpent:N0})",
                        back && returned != null && returned.Slab == pick.Slab && returned.Seed == pick.Seed && returned.Paid == paidBefore + railed.Price
                        && profile.Dp == dpBefore - railed.Price && profile.DpSpent.GetValueOrDefault(Ledger.AlleyRail) - railSpent == railed.Price && profile.Rail.Count == 0);
                    _van.Hide();
                }
            }

            // ---- 11. The van's three tabs, its shelf and Sell desk; the Unlisted binder ----
            _campaignScreen.DebugShowAlley(dumpster);
            yield return new WaitForSecondsRealtime(0.4f);
            bool bedAlley = Sounds.DebugBed().wanted;
            // The van's first-visit scene (seen again for this) plays over the alley before the van opens: the bed stays on.
            var fenceScenes = _story.Scenes.Where(sc => sc.Trigger == BackAlley.FenceEvent).Select(sc => sc.Id).ToList();
            foreach (var id in fenceScenes) profile.Seen.Remove(id);
            _campaignScreen.DebugVan();
            yield return Until(() => _storyScreen.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.3f);
            bool bedScene = _storyScreen.IsOpen && Sounds.DebugBed().wanted;
            if (_storyScreen.IsOpen) _storyScreen.DebugFinish();
            yield return Until(() => _van.IsOpen, 5);
            _van.DebugShow(0);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("11-van-shelf");
            var vanTabs = _van.DebugTabs();
            var plate = _van.DebugPlate;
            bool bedVan = Sounds.DebugBed().wanted;
            Report($"11. the van: \"{plate.plate}\" under \"{plate.sign}\", tabs {string.Join(" | ", vanTabs)}; the rain bed under the alley ({bedAlley}), the fence scene before the van ({bedScene}) and the van ({bedVan})",
                vanTabs.SequenceEqual(new[] { "Shelf", "Used & graded", "Sell" }) && plate.plate == "UNDER THE COUNTER" && plate.plateColor == UiTheme.Current.Sign
                && plate.signColor == UiTheme.NeonCyan && UiTheme.Current.IsAlley && bedAlley && bedScene && bedVan);
            var shelf = _campaign.VanShelf();
            var shelfText = _van.DebugListText();
            Report($"11. the shelf: {shelf.Count} slabs ({string.Join(", ", shelf.Select(l => $"{_cardPack!.Get(l.Copy.Code)?.Name} {l.Copy.Slab} at {l.Price:N0}"))}), each at ShelfPrice(V)",
                shelf.Count == AlleyEconomy.ShelfSize && shelf.All(l => l.Copy.IsSlab && l.Copy.Slab >= 9 && l.Price == AlleyEconomy.ShelfPrice(profile.Collection.Value(l.Copy))
                    && profile.Collection.Value(l.Copy) >= AlleyEconomy.ShelfMinValue && shelfText.Contains($"{l.Price:N0} DP"))
                && shelf.Select(l => l.Copy.Code).Distinct().Count() == shelf.Count);
            _van.DebugShow(1);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("11-van-used");
            var used = _campaign.VanUsed();
            Report($"11. Used & graded: {used.Count} listings at value, slabs up to {AlleyEconomy.VanUsedSlabMax}, and the HELD row ({MothsVan.HeldShown(profile)})",
                used.Count == AlleyEconomy.VanUsedSize && used.All(l => l.Price == profile.Collection.Value(l.Copy) && (!l.Copy.IsSlab || l.Copy.Slab <= AlleyEconomy.VanUsedSlabMax))
                && _van.DebugListText().Contains(MothsVan.HeldLabel) == MothsVan.HeldShown(profile));
            // Card versions (phase 2): each listing printing an original text carries the tag; the HELD row gives its printing's year.
            int usedOriginals = used.Count(l => CardDetail.CarriesOriginal(l.Copy));
            Report($"11. the used listings tag {_van.DebugOriginalBands} ORIGINAL TEXT ({usedOriginals} print one: {string.Join(", ", used.Where(l => CardDetail.CarriesOriginal(l.Copy)).Select(l => $"{_cardPack!.Get(l.Copy.Code)?.Name} {l.Copy.Set}"))}); "
                   + $"{used.Count(l => l.Copy.Set.Length > 0)} of {used.Count} have a printing; the HELD row reads \"{_van.DebugHeldLabel}\"",
                _van.DebugOriginalBands == usedOriginals && used.All(l => l.Copy.Set.Length > 0 || !CardDetail.CarriesOriginal(l.Copy))
                && (!MothsVan.HeldShown(profile) || _van.DebugListText().Contains(_van.DebugHeldLabel) && _van.DebugHeldLabel.EndsWith(" printing")));
            // A later period whose listings print an original text (looked ahead by the duel count, then put back): the tile
            // carries the ORIGINAL TEXT tag and the detail its printing.
            {
                int duelsBefore = profile.DuelsPlayed, epochBefore = profile.VanUsedEpoch;
                var soldBefore = profile.VanUsedSold.ToList();
                var ahead = used;
                for (int k = 1; k <= 80 && ahead.All(l => !CardDetail.CarriesOriginal(l.Copy)); k++)
                {
                    profile.DuelsPlayed = duelsBefore + k * Math.Max(1, AlleyEconomy.VanUsedEvery);
                    ahead = _campaign.VanUsed();
                }
                var orig = ahead.FirstOrDefault(l => CardDetail.CarriesOriginal(l.Copy));
                if (orig != null)
                {
                    _van.DebugShow(1);
                    _van.DebugPickUsed(orig.Slot);
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Shot("11-van-used-original");
                }
                int bands = _van.DebugOriginalBands;
                var detailText = _van.DebugDetailText();
                Report($"11. a period {(profile.DuelsPlayed - duelsBefore)} duels on lists {ahead.Count(l => CardDetail.CarriesOriginal(l.Copy))} original-text cop(ies) "
                       + $"({(orig == null ? "-" : $"{_cardPack!.Get(orig.Copy.Code)?.Name} {orig.Copy.Set} {orig.Copy.Printed}")}): {bands} tagged; the detail reads its printing",
                    orig != null && bands == ahead.Count(l => CardDetail.CarriesOriginal(l.Copy)) && detailText.Contains($"{orig.Copy.Set} · {orig.Copy.Printed.Substring(0, 4)} · ORIGINAL TEXT"));
                profile.DuelsPlayed = duelsBefore;
                profile.VanUsedEpoch = epochBefore;
                profile.VanUsedSold.Clear();
                foreach (var slot in soldBefore) profile.VanUsedSold.Add(slot);
                _van.DebugShow(1);
            }
            // A sale on the Sell tab pays exactly the Sell desk's price, to the ledger's "sell".
            var forSale = profile.Collection.All.Where(c => SellDesk.Blocked(profile, c, _campaign.NeededByDecks) == null && profile.Collection.SellPrice(c) > 0)
                .OrderByDescending(profile.Collection.SellPrice).FirstOrDefault();
            int deskPrice = forSale != null ? profile.Collection.SellPrice(forSale) : 0, sold0 = profile.DpEarned.GetValueOrDefault(Ledger.Sell);
            int paid = forSale != null ? _van.DebugSell(new[] { forSale }) : -1;
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("11-van-sell");
            var wonCopy = profile.Collection.All.FirstOrDefault(c => c.Source == CopySource.Ante);
            Report($"11. Sell: {(forSale != null ? _campaign.CopyText(forSale) : "nothing to sell")} pays {paid:N0} DP, the desk's {deskPrice:N0}; a card won at a table is refused (\"{(wonCopy != null ? SellDesk.Blocked(profile, wonCopy, _campaign.NeededByDecks) : "")}\")",
                forSale != null && paid == deskPrice && profile.DpEarned.GetValueOrDefault(Ledger.Sell) - sold0 == deskPrice && !profile.Collection.All.Contains(forSale)
                && (wonCopy == null || SellDesk.Blocked(profile, wonCopy, _campaign.NeededByDecks) == SellDesk.AnteRefusal));
            _van.Close();
            yield return new WaitForSecondsRealtime(0.3f);
            // An alley duel's top bar and music (seen in step 7).
            Report($"11. an alley duel's top bar reads the alley's theme (title {ColorUtility.ToHtmlStringRGB(bar7.title)}) and plays {music7} (ForAlley: {music7Wanted.theme}:{music7Wanted.transpose})",
                bar7.title == UiTheme.BackAlley.Text && bar7.band.a > 0.5f && music7 == $"{music7Wanted.theme}:{music7Wanted.transpose}");
            // The Unlisted binder on the Library's shelf, its page, and "Open as a new deck".
            int pageCount = profile.UnlistedPages.Count;
            // (The Library's own scenes are the campaign check's: seen here, so they don't close the screen under test.)
            foreach (var trigger in new[] { "library", "final", "open:" + lib.Final.Name }.Concat(lib.Cups.Select(c => "open:" + c.Name)))
                foreach (var scene in _story.Pending(profile, new[] { trigger })) Story.MarkSeen(profile, scene);
            bool unlistedShown = _campaignScreen.DebugShowUnlisted();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("11-unlisted");
            var page = _campaignScreen.UnlistedPageShown;
            Report($"11. the Unlisted binder on the Library's shelf with {pageCount} page{(pageCount == 1 ? "" : "s")} ({page?.Handle}, {page?.Table}, {page?.Kind} {page?.Base})",
                unlistedShown && pageCount >= 1 && page != null && page.Key == Unlisted.Pages(profile)[0].Key && !UiTheme.Current.IsAlley);
            _campaignScreen.DebugOpenUnlisted();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("11-unlisted-editor");
            var built = _campaignScreen.LastUnlistedDeck;
            Report($"11. \"Open as a new deck\" builds \"{built}\" from your cards and opens the editor",
                built.Length > 0 && _library.Contains(built) && _editor.IsOpen && _library.Load(built).Main.Count >= 40);
            _editor.Close();
            yield return new WaitForSecondsRealtime(0.3f);
            // Past 14 pages the column scrolls: every page gets a row, and the last one can be picked and shown (copies of the
            // first page, taken out again after).
            var extraPages = new List<UnlistedPage>();
            var firstPage = profile.UnlistedPages[0];
            for (int k = profile.UnlistedPages.Count; k < 17; k++)
            {
                var extraPage = new UnlistedPage { Key = $"{firstPage.Key}#{k}", Table = firstPage.Table, Kind = firstPage.Kind, Base = firstPage.Base, Handle = $"{firstPage.Handle} {k + 1}",
                    Added = firstPage.Added.ToList(), Cut = firstPage.Cut.ToList() };
                profile.UnlistedPages.Add(extraPage);
                extraPages.Add(extraPage);
            }
            _campaignScreen.DebugShowUnlisted();
            _campaignScreen.DebugPickUnlisted(profile.UnlistedPages.Count - 1);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("11-unlisted-scrolled");
            var lastPage = profile.UnlistedPages[profile.UnlistedPages.Count - 1];
            Report($"11. the binder's pages scroll: {_campaignScreen.UnlistedRows} rows for {profile.UnlistedPages.Count} pages; the last ({_campaignScreen.UnlistedPageShown?.Handle}) shows",
                _campaignScreen.UnlistedRows == profile.UnlistedPages.Count && _campaignScreen.UnlistedPageShown == lastPage);
            foreach (var extraPage in extraPages) profile.UnlistedPages.Remove(extraPage);
            _campaignScreen.DebugPickUnlisted(0);
            yield return new WaitForSecondsRealtime(0.2f);
            _campaignScreen.Close();

            // ---- 12. Baz's chalkboard: the Alley Brawl entered, played and paid; Skip to end; a title's Contraband Packs ----
            profile.Deck = legal.Name;
            _campaign.Save();
            int board = _campaignScreen.DebugChalkboard;
            _campaignScreen.DebugShowAlley(board);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("12-chalkboard");
            var boardRows = _campaignScreen.ChalkboardRows.ToList();
            Report($"12. Baz's chalkboard opens with Dumpster Row's clear: {string.Join(" · ", boardRows.Select(r => $"{r.kind} \"{r.label}\""))}",
                _campaignScreen.DebugAlleyNodes.Any(n => n.name == "Baz's chalkboard" && n.open) && UiTheme.Current.IsAlley && boardRows.Count == 2
                && boardRows[0] == (Brackets.BrawlKind, $"Enter · {AlleyEconomy.BrawlEntry:N0}", true) && boardRows[1] == (Brackets.MidnightKind, $"Enter · {AlleyEconomy.OpenEntry:N0}", true));
            int EntrySpent(Profile p) => p.DpSpent.GetValueOrDefault(Ledger.AlleyBrackets);
            int AlleyEarned(Profile p) => p.DpEarned.GetValueOrDefault(Ledger.Alley);
            int entry0 = EntrySpent(profile), earned0 = AlleyEarned(profile);
            dpBefore = profile.Dp;
            _campaignScreen.DebugChalkboardButton(Brackets.BrawlKind);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("12-brawl-entry");
            var preview = _bracketScreen.DebugEntryNames.ToList();
            var entryText = _bracketScreen.DebugEntryText;
            Report($"12. its Enter opens the entry dialog over the board: {_bracketScreen.DebugEntryRows} seeds ({string.Join(", ", preview.Take(3))}...), the entry, the Contraband Pack's odds, the Alley list's rule, "
                   + "that leaving a duel counts as a loss, Enter on",
                _bracketScreen.EntryOpen && _bracketScreen.DebugEntryRows == 8 && entryText.Contains("Entry: ") && entryText.Contains($"{AlleyEconomy.BrawlEntry:N0} DP")
                && entryText.Contains("A Contraband Pack") && entryText.Contains("Grade 10") && entryText.Contains("Alley list") && _campaignScreen.IsOpen
                && entryText.Contains("Leaving a duel counts as a loss.") && _bracketScreen.DebugEntryEnterable);
            _bracketScreen.DebugEnter();
            yield return Until(() => _bracketScreen.IsOpen, 3);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("12-brawl");
            var brawl = profile.Bracket;
            // The Black Market is cleared (step 10): the four out-of-towners sit at seeds 8, 5, 2 and 3 (NL-1, section 8), the rest regulars.
            var outOfTowners = brawl != null ? BackAlley.OutOfTowners(brawl, data) : new List<string>();
            var ootSeeds = brawl?.Slots.Where(e => !e.You && e.Splash == null && data.FindBuild(e.Character) != null).OrderBy(e => e.Character).Select(e => $"{e.Character} #{e.Seed}").ToList() ?? new List<string>();
            string NameOfEntrant(BracketEntrant e) => e.You ? "You" : e.Splash?.Handle ?? (_campaign.Cast.TryGetValue(e.Character, out var cast) ? cast.Name : e.Character);
            var seededNames = brawl?.Slots.OrderBy(e => e.Seed).Select(NameOfEntrant).ToList() ?? new List<string>();
            Report($"12. Enter takes {dpBefore - profile.Dp:N0} DP (alley.brackets +{EntrySpent(profile) - entry0:N0}) and opens the Brawl in the alley's theme, seated as the dialog showed ({string.Join(", ", seededNames)}); "
                   + $"the out-of-towners after The Black Market: {string.Join(", ", ootSeeds)} (marked on the bracket: {_bracketScreen.DebugOutOfTowners.Count})",
                brawl != null && brawl.Kind == Brackets.BrawlKind && brawl.Size == 8 && dpBefore - profile.Dp == AlleyEconomy.BrawlEntry && EntrySpent(profile) - entry0 == AlleyEconomy.BrawlEntry
                && _bracketScreen.IsOpen && UiTheme.Current.IsAlley && seededNames.SequenceEqual(preview) && brawl.Slots.Count(e => e.Splash != null) == 7 - outOfTowners.Count && brawl.Slots[0].You
                && outOfTowners.Count == 4 && ootSeeds.SequenceEqual(new[] { "augie #5", "junia #8", "rue #2", "sasha #3" }) && _bracketScreen.DebugOutOfTowners.Count == 4);
            Report("12. the bracket screen says leaving a duel counts as a loss", _bracketScreen.DebugText.Contains("Leaving a duel counts as a loss."));
            var (bracketBoxes, bracketOverlaps) = _bracketScreen.DebugOverlaps();
            Report($"12. no Brawl slots overlap ({bracketBoxes} boxes{(bracketOverlaps.Count > 0 ? ": " + string.Join(", ", bracketOverlaps.Take(3)) : "")})", bracketBoxes == 15 && bracketOverlaps.Count == 0);
            if (brawl == null) { Report("12. the Brawl is running", false); Application.Quit(1); yield break; }
            // Your quarter-final: the versus under the Alley's rules, then a real duel (the turn cap, the alley's bar and music), conceded.
            int brawlMatch = brawl.YourMatch(), brawlThem = brawl.Opponent(brawlMatch);
            var brawlHandle = brawl.Slots[brawlThem].Splash?.Handle ?? BracketPerson(brawl, brawlThem).Name;
            _bracketVersusRules = null;
            _bracketScreen.DebugDuel();
            yield return Until(() => _scenes.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("12-brawl-versus");
            Report($"12. your quarter-final's versus: {brawlHandle}, under the Alley's rules (your own decks only)",
                _scenes.IsOpen && _scenes.Buttons.ContainsKey("Duel!") && _bracketVersusRules == _campaign.AlleyRules && UiTheme.Current.IsAlley);
            // Stuck? at the Brawl: after losses in a row at this round, the advice opens by itself; nothing's lent here, so it
            // names stronger recipes to own (Make it yours, under the Alley list) and never says borrow.
            var brawlKey = Brackets.Key(brawl, brawlMatch);
            profile.LossStreaks[brawlKey] = StuckAfter;
            ShowBracketVersus();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("12-brawl-stuck");
            var brawlEntrant = brawl.Slots[brawlThem];
            var brawlTips = Stuck(brawlKey, Enumerable.Empty<MeasuredOption>(), false, new[] { BracketPerson(brawl, brawlThem).Id }, false,
                brawlEntrant.Splash?.Base ?? brawlEntrant.Deck) ?? new List<string>();
            Report($"12. after {StuckAfter} losses in a row at this round, the Brawl's versus opens the Stuck? advice, with no loaner in it ({string.Join(" / ", brawlTips.Skip(1).Take(2))})"
                   + (_scenes.Buttons.ContainsKey("Own stuck deck") ? ", with Make it yours" : ""),
                _scenes.ScoutingOpen && brawlTips.Count >= 2 && !brawlTips.Any(t => t.StartsWith("Borrow")) && UiTheme.Current.IsAlley);
            if (_scenes.Buttons.TryGetValue("Close scouting", out var closeBrawlStuck)) closeBrawlStuck();
            profile.LossStreaks.Remove(brawlKey);
            if (_scenes.Buttons.TryGetValue("Duel!", out var brawlDuel)) brawlDuel();
            yield return Until(() => _runner != null && _runner.IsWaiting && _runner.WaitingFor == 0 && _human?.Current != null, 30);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("12-brawl-duel");
            var brawlBar = _view.DebugBar;
            var brawlMusic = Sounds.DebugMusic().duelTheme;
            Report($"12. it's a real duel against {_players[1].Name} under the turn cap ({_turnCap?.LastTurn}), the alley's top bar and music ({brawlMusic}), the splash regular's deck",
                _bracketMatch == brawlMatch && _turnCap != null && _players[1].Name == brawlHandle && brawlBar.title == UiTheme.BackAlley.Text && brawlMusic == $"{BossMusic.Alley}:0"
                && _recording?.DeckNames.ElementAtOrDefault(1) == brawl.Slots[brawlThem].Deck);
            ConcedeDuel();
            yield return Until(() => _announcedResult, 20);
            yield return Until(() => _scenes.IsOpen, 6);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("12-brawl-results");
            var outLine = _scenes.LastResults?.Unlocks.FirstOrDefault() ?? "";
            Report($"12. a loss pays its placement at once into \"alley\" (+{AlleyEarned(profile) - earned0:N0}: \"{outLine}\"), Baz calls it ({_scenes.LastResults?.Unlocks.Skip(1).FirstOrDefault()})",
                brawl.YouOut && brawl.Placement() == 3 && AlleyEarned(profile) - earned0 == AlleyEconomy.BrawlPay[3] && outLine.StartsWith("Out in the quarter-finals")
                && _scenes.LastResults?.Unlocks.Any(u => u.StartsWith("Baz")) == true);
            _scenes.Close();
            ShowBracket();
            yield return new WaitForSecondsRealtime(0.3f);
            _bracketScreen.DebugSkip();
            yield return Until(() => !_bracketScreen.Animating, 8);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("12-brawl-skipped");
            Report($"12. Skip to end settles the Brawl (champion: {(brawl.Champion >= 0 ? brawl.Slots[brawl.Champion].Splash?.Handle : "none")})", brawl.Done && brawl.Champion >= 0 && !brawl.Slots[brawl.Champion].You);
            _bracketScreen.DebugFinish();
            yield return new WaitForSecondsRealtime(0.6f);
            var brawlRecord = Brackets.AlleyRecord(profile, Brackets.BrawlKind);
            Report($"12. Finish closes it into the alley's record ({brawlRecord.Entries} entry, best {brawlRecord.Best}), apart from the Locals', and returns to the chalkboard",
                profile.Bracket == null && brawlRecord.Entries == 1 && brawlRecord.Best == 3 && profile.Locals.Count == 0 && _campaignScreen.IsOpen && _campaignScreen.InAlley
                && _campaignScreen.ChalkboardRows.Count == 2);
            // A forced title: every match of yours won; Finish reveals two Contraband Packs (four Raw cards and a sealed slab each).
            _campaignScreen.Close();
            earned0 = AlleyEarned(profile);
            var brawlTitle = Brackets.EnterAlley(profile, data, Brackets.BrawlKind, ctx, profile.Deck);
            while (brawlTitle != null && brawlTitle.YourMatch() >= 0) Brackets.RecordYours(profile, lib, brawlTitle, 0, false, _campaign.Matchups);
            _campaign.Save();
            ShowBracket();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("12-brawl-title");
            _bracketScreen.DebugFinish();
            yield return new WaitForSecondsRealtime(0.8f);
            yield return _shop.DebugRevealPrize();
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("12-contraband-pack");
            var contraband = _shop.DebugPrize();
            var brawlReward = _lastBracketReward;
            bool PackOk(List<PrizePull> pack) => pack.Count == ContrabandPacks.Size && pack.Count(p => p.Slab > 0) == 1 && pack[pack.Count - 1].Slab >= 9
                && pack.Take(pack.Count - 1).All(p => p.Slab == 0) && pack.All(p => p.Copy == null || (p.Copy.Paid == 0 && p.Copy.Source == CopySource.Prize));
            var prizeLine = _shop.DebugPrizeLine;
            Report($"12. a title's Finish reveals its Contraband Packs: {contraband.cards} cards, {contraband.revealed} face up, {contraband.left} more; four Raw and a sealed slab each, Paid 0 "
                   + $"(\"{System.Text.RegularExpressions.Regex.Replace(prizeLine.Replace('\n', ' '), "<.*?>", "")}\"); +{AlleyEarned(profile) - earned0:N0} DP; events {string.Join(", ", brawlReward?.Events ?? new List<string>())}",
                brawlTitle != null && contraband.open && contraband.cards == ContrabandPacks.Size && contraband.revealed == ContrabandPacks.Size && contraband.left == AlleyEconomy.BrawlPacks[0] - 1
                && brawlReward != null && brawlReward.Packs.Count == AlleyEconomy.BrawlPacks[0] && brawlReward.Packs.All(PackOk) && prizeLine.StartsWith("Contraband Pack 1 of 2")
                && AlleyEarned(profile) - earned0 == AlleyEconomy.BrawlPay[0] && Brackets.AlleyRecord(profile, Brackets.BrawlKind).Titles == 1
                && brawlReward.Events.Contains("bracketwin:Alley Brawl") && !brawlReward.Events.Contains(Brackets.WinEvent) && Brackets.TitlesTotal(profile) == 0);
            _shop.DebugFinishPrizes();
            yield return new WaitForSecondsRealtime(0.5f);
            Report("12. the prizes' Done returns to the chalkboard, in the alley's theme", _campaignScreen.IsOpen && _campaignScreen.InAlley && UiTheme.Current.IsAlley && !_shop.IsOpen);
            // The app closed during the reveal: the title's scene was never seen. The next load queues it, and the chalkboard plays it, once.
            _campaignScreen.Close();
            var brawlWin = "bracketwin:" + data.Brackets.First(x => x.Kind == Brackets.BrawlKind).Name;
            var winScenes = _story.Pending(new Profile(), new[] { brawlWin });
            foreach (var scene in winScenes) profile.Seen.Remove(scene.Id);
            _campaign.Save();
            _campaign.DebugReload();
            profile = _campaign.Profile!;
            bool queuedAtLoad = _campaign.AlleyNoticeEvents.Contains(brawlWin) && _story.Pending(profile, new[] { brawlWin }).Count == winScenes.Count;
            _campaign.AlleyNotice = "";
            _campaign.ClearAlleyStory();
            _campaignScreen.OpenChalkboard();
            yield return Until(() => _storyScreen.IsOpen, 5);
            yield return Shot("12-title-catch-up");
            bool caughtUp = _storyScreen.IsOpen;
            if (_storyScreen.IsOpen) _storyScreen.DebugFinish();
            yield return Until(() => _campaignScreen.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.4f);
            _campaignScreen.Close();
            _campaignScreen.OpenChalkboard();
            yield return new WaitForSecondsRealtime(0.8f);
            Report($"12. a title whose scene the reveal never reached ({brawlWin}): queued at load, played when the chalkboard opens, and once only",
                winScenes.Count > 0 && queuedAtLoad && caughtUp && winScenes.All(sc => profile.Seen.Contains(sc.Id)) && !_storyScreen.IsOpen && _campaignScreen.IsOpen);

            // ---- 13. The Midnight Open: its 16 slots, the best-of-three final over a save; a CPU final watched; Nyx's Table ----
            entry0 = EntrySpent(profile);
            earned0 = AlleyEarned(profile);
            dpBefore = profile.Dp;
            _campaignScreen.DebugChalkboardButton(Brackets.MidnightKind);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("13-midnight-entry");
            preview = _bracketScreen.DebugEntryNames.ToList();
            var ilara = _campaign.Cast.TryGetValue("radiant-typhoon", out var champ) ? champ.Name : "radiant-typhoon";
            Report($"13. the Midnight Open's entry dialog: {_bracketScreen.DebugEntryRows} seeds, {preview.FirstOrDefault()} seed 1 and you seed 2, entry {AlleyEconomy.OpenEntry:N0}",
                _bracketScreen.DebugEntryRows == 16 && preview.Count == 16 && preview[0] == ilara && preview[1] == "You" && _bracketScreen.DebugEntryText.Contains($"{AlleyEconomy.OpenEntry:N0} DP"));
            _bracketScreen.DebugEnter();
            yield return Until(() => _bracketScreen.IsOpen, 3);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("13-midnight");
            Bracket open = profile.Bracket!;
            (bracketBoxes, bracketOverlaps) = _bracketScreen.DebugOverlaps();
            Report($"13. the Midnight Open: 16 slots laid out with the plaque ({bracketBoxes} boxes{(bracketOverlaps.Count > 0 ? ": " + string.Join(", ", bracketOverlaps.Take(3)) : "")}), the entry {dpBefore - profile.Dp:N0} DP, best of {open?.FinalBestOf} in the final",
                open != null && open.Kind == Brackets.MidnightKind && open.Size == 16 && open.FinalBestOf == 3 && bracketBoxes == 31 && bracketOverlaps.Count == 0
                && dpBefore - profile.Dp == AlleyEconomy.OpenEntry && EntrySpent(profile) - entry0 == AlleyEconomy.OpenEntry && open.Slots[open.YourSlot].Seed == 2);
            if (open == null) { Report("13. the Midnight Open is running", false); Application.Quit(1); yield break; }
            // Through to the final (forced); game 1 won; the save read again keeps the series.
            while (open.YourMatch() >= 0 && open.YourMatch() != open.Final) Brackets.RecordYours(profile, lib, open, 0, false, _campaign.Matchups);
            var game1 = Brackets.RecordYours(profile, lib, open, 0, false, _campaign.Matchups);
            _campaign.Save();
            var gamesLine = SaveText().Split('\n').FirstOrDefault(l => l.StartsWith("bracketgames ")) ?? "";
            _campaign.DebugReload();
            profile = _campaign.Profile!;
            open = profile.Bracket!;
            int yourSide = open.Pair(open.Final).a == open.YourSlot ? 0 : 1;
            Report($"13. game 1 of the final won: \"{game1?.Reward.Summary}\"; the save keeps it (\"{gamesLine}\") and reads it back (games {open.FinalGamesA}-{open.FinalGamesB}, the last loser side {open.FinalLastLoser})",
                game1 != null && game1.Game == 1 && game1.NextGame && !open.Done && open.YourMatch() == open.Final && (yourSide == 0 ? open.FinalGamesA : open.FinalGamesB) == 1
                && open.FinalGamesA + open.FinalGamesB == 1 && open.FinalLastLoser == 1 - yourSide && gamesLine == $"bracketgames {open.FinalGamesA} {open.FinalGamesB} 3 {open.FinalLastLoser}");
            ShowBracket();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("13-bo3-pips");
            var pips = _bracketScreen.DebugPips;
            string Plain(string t) => System.Text.RegularExpressions.Regex.Replace(t, "<.*?>", "");
            Report($"13. the final's pips: \"{Plain(pips.a)}\" over \"{Plain(pips.b)}\"; the Duel reads game 2",
                Plain(yourSide == 0 ? pips.a : pips.b) == "● ○" && Plain(yourSide == 0 ? pips.b : pips.a) == "○ ○" && _bracketScreen.DebugText.Contains("Duel: Final, game 2"));
            // Game 2: no toss, the last game's loser (them) chooses; a real duel, conceded: 1-1.
            _autoToss = false;
            _bracketScreen.DebugDuel();
            yield return Until(() => _scenes.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("13-game2-versus");
            if (_scenes.Buttons.TryGetValue("Duel!", out var game2)) game2();
            // (The coin's flight first: its buttons come after it.)
            yield return Until(() => _scenes.Buttons.ContainsKey("Continue") || _scenes.Buttons.ContainsKey("Go first"), 5);
            yield return Shot("13-game2-toss");
            bool theyChoose = _scenes.IsOpen && _scenes.Buttons.ContainsKey("Continue") && !_scenes.Buttons.ContainsKey("Go first");
            if (_scenes.Buttons.TryGetValue("Continue", out var go)) go();
            yield return Until(() => _runner != null && _runner.IsWaiting && _runner.WaitingFor == 0 && _human?.Current != null && _bracketMatch == open.Final, 60);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("13-game2-duel");
            Report($"13. game 2 has no toss: {_players[1].Name} lost game 1 and chooses; it's a real duel under the turn cap",
                theyChoose && _bracketMatch == open.Final && _turnCap != null && _view.DebugBar.title == UiTheme.BackAlley.Text);
            ConcedeDuel();
            yield return Until(() => _announcedResult, 20);
            yield return Until(() => _scenes.IsOpen, 6);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("13-game2-results");
            Report($"13. game 2 lost: 1-1 (\"{_scenes.LastResults?.Unlocks.FirstOrDefault()}\"), and the results lead to game 3",
                open.FinalGamesA == 1 && open.FinalGamesB == 1 && open.FinalLastLoser == yourSide && !open.Done && _scenes.Buttons.ContainsKey("Game 3"));
            // Game 3: you lost game 2, so you choose.
            if (_scenes.Buttons.TryGetValue("Game 3", out var game3)) game3();
            yield return Until(() => _scenes.IsOpen && _scenes.Buttons.ContainsKey("Duel!"), 5);
            yield return new WaitForSecondsRealtime(0.4f);
            if (_scenes.Buttons.TryGetValue("Duel!", out var game3Duel)) game3Duel();
            yield return Until(() => _scenes.Buttons.ContainsKey("Continue") || _scenes.Buttons.ContainsKey("Go first"), 5);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("13-game3-toss");
            Report("13. game 3: you lost game 2, so you choose (Go first / Go second)", _scenes.IsOpen && _scenes.Buttons.ContainsKey("Go first") && _scenes.Buttons.ContainsKey("Go second"));
            _scenes.Close();
            _autoToss = true;
            // Game 3 won (forced): the title, four Contraband Packs and the first title's Card Token.
            int tokens0 = profile.Tokens;
            earned0 = AlleyEarned(profile);
            var game3Result = Brackets.RecordYours(profile, lib, open, 0, false, _campaign.Matchups);
            _campaign.Save();
            ShowBracket();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("13-midnight-title");
            _bracketScreen.DebugFinish();
            yield return new WaitForSecondsRealtime(0.8f);
            yield return _shop.DebugRevealPrize();
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("13-contraband-packs");
            var openReward = _lastBracketReward;
            contraband = _shop.DebugPrize();
            Report($"13. game 3 won takes the title 2-1 (\"{game3Result?.Reward.Summary}\"): +{AlleyEarned(profile) - earned0:N0} DP, {openReward?.Packs.Count} Contraband Packs ({contraband.left} more to open), +{profile.Tokens - tokens0} Card Token; "
                   + $"events {string.Join(", ", openReward?.Events ?? new List<string>())}",
                game3Result != null && game3Result.Game == 3 && !game3Result.NextGame && openReward != null && openReward.Placement == 0 && openReward.Packs.Count == AlleyEconomy.OpenPacks[0]
                && openReward.Packs.All(PackOk) && contraband.left == AlleyEconomy.OpenPacks[0] - 1 && AlleyEarned(profile) - earned0 == AlleyEconomy.OpenPay[0]
                && profile.Tokens - tokens0 == AlleyEconomy.OpenTokens && openReward.Tokens == AlleyEconomy.OpenTokens && profile.Seen.Contains(Brackets.OpenTokenKey)
                && Brackets.AlleyRecord(profile, Brackets.MidnightKind).Titles == 1 && openReward.Events.Contains("bracketwin:Midnight Open") && BackAlley.Nick(profile) == BackAlley.NickLadder[4]);
            _shop.DebugFinishPrizes();
            yield return new WaitForSecondsRealtime(0.5f);
            // A CPU final, watched a game at a time: out in the round of 16, the rounds before the final settled, game 1 watched
            // (capped at turn 2 to keep it short); then Skip to end and Finish.
            _campaignScreen.Close();
            earned0 = AlleyEarned(profile);
            var watched = Brackets.EnterAlley(profile, data, Brackets.MidnightKind, ctx, profile.Deck);
            var outR16 = watched != null ? Brackets.RecordYours(profile, lib, watched, 1, false, _campaign.Matchups) : null;
            for (int r = watched?.CurrentRound() ?? 0; watched != null && r < watched.Rounds - 1; r++) Brackets.SimulateRound(watched, r, _campaign.Matchups);
            _campaign.Save();
            ShowBracket();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("13-watch-ready");
            Report($"13. out in the round of 16 (+{AlleyEarned(profile) - earned0:N0} DP), the final CPU against CPU: \"Watch game 1\"",
                watched != null && outR16 != null && outR16.Placement == 4 && AlleyEarned(profile) - earned0 == AlleyEconomy.OpenPay[4]
                && watched.Pending(watched.Final) && !watched.IsYours(watched.Final) && _bracketScreen.DebugText.Contains("Watch game 1"));
            if (watched != null)
            {
                _alleyTurnCapOverride = 2;
                speedIndex = Speeds.Length - 1;
                UpdateControls();
                var (wa, wb) = watched.Pair(watched.Final);
                _bracketScreen.DebugWatch();
                yield return Until(() => _runner != null && !_runner.IsFinished && _bracketWatch, 10);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot("13-watch-duel");
                bool watching = _bracketWatch && _turnCap != null && _players[0].Name == BracketPerson(watched, wa).Name && _players[1].Name == BracketPerson(watched, wb).Name;
                yield return Until(() => _announcedResult, 120);
                int watchedWinner = _board.Winner;
                yield return Until(() => _bracketScreen.IsOpen, 8);
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot("13-watched");
                Report($"13. watching game 1 ({_players[0].Name} v {_players[1].Name}) counts it: {watched.FinalGamesA}-{watched.FinalGamesB} (winner {watchedWinner}); the bracket offers game {watched.FinalGamesA + watched.FinalGamesB + 1}",
                    watching && _announcedResult && !watched.Done
                    && (watchedWinner > 1 ? watched.FinalGamesA + watched.FinalGamesB == 0 : watched.FinalGamesA + watched.FinalGamesB == 1 && watched.FinalLastLoser == 1 - watchedWinner)
                    && _bracketScreen.DebugText.Contains($"Watch game {watched.FinalGamesA + watched.FinalGamesB + 1}"));
                _alleyTurnCapOverride = 0;
                _bracketScreen.DebugSkip();
                yield return Until(() => !_bracketScreen.Animating, 8);
                Report($"13. Skip to end decides the rest of the series ({watched.FinalGamesA}-{watched.FinalGamesB} watched; champion {(watched.Champion >= 0 ? BracketPerson(watched, watched.Champion).Name : "none")})", watched.Done);
                _bracketScreen.DebugFinish();
                yield return new WaitForSecondsRealtime(0.6f);
                Report($"13. Finish closes it: the Midnight Open's record ({Brackets.AlleyRecord(profile, Brackets.MidnightKind).Entries} entries, best {Brackets.AlleyRecord(profile, Brackets.MidnightKind).Best})",
                    profile.Bracket == null && Brackets.AlleyRecord(profile, Brackets.MidnightKind).Entries == 2 && Brackets.AlleyRecord(profile, Brackets.MidnightKind).Best == 0);
            }
            // Leaving an alley bracket duel counts as a loss, as at the tables: for the menu (recorded at once) or a closed app (at load).
            _campaignScreen.Close();
            profile.Earn(Ledger.Debug, 2 * AlleyEconomy.BrawlEntry);
            earned0 = AlleyEarned(profile);
            var quitBrawl = Brackets.EnterAlley(profile, data, Brackets.BrawlKind, ctx, profile.Deck);
            _campaign.Save();
            ShowBracket();
            yield return new WaitForSecondsRealtime(0.4f);
            _bracketScreen.DebugDuel();
            yield return Until(() => _scenes.IsOpen, 5);
            if (_scenes.Buttons.TryGetValue("Duel!", out var quitDuel)) quitDuel();
            yield return Until(() => _runner != null && _runner.IsWaiting && _runner.WaitingFor == 0 && _human?.Current != null && _bracketMatch != null, 30);
            yield return new WaitForSecondsRealtime(0.5f);
            bool quitLive = quitBrawl != null && quitBrawl.LiveMatch >= 0 && _bracketMatch == quitBrawl.LiveMatch && Profile.Load(_campaign.SavePath)?.Bracket?.LiveMatch == quitBrawl.LiveMatch;
            AbandonAlleyDuel();
            AbandonAlleyBracketDuel();
            _paused = true;
            UpdateControls();
            var quitSaved = Profile.Load(_campaign.SavePath)?.Bracket;
            Report($"13. leaving an alley bracket duel for the menu is a loss at once (+{AlleyEarned(profile) - earned0:N0} DP: \"{_campaign.AlleyNotice}\")",
                quitLive && quitBrawl!.YouOut && quitBrawl.Placement() == 3 && quitBrawl.LiveMatch == -1 && _bracketMatch == null
                && AlleyEarned(profile) - earned0 == AlleyEconomy.BrawlPay[3] && _campaign.AlleyNotice.StartsWith("You left the Alley Brawl mid-duel. That counts as a loss.")
                && quitSaved != null && quitSaved.YouOut && quitSaved.LiveMatch == -1);
            _campaign.AlleyNotice = "";
            _campaign.ClearAlleyStory();
            seed++;
            PickRandomBots();
            NewDuel();
            _paused = true;
            UpdateControls();
            if (quitBrawl != null) { Brackets.SkipToEnd(quitBrawl, _campaign.Matchups); Brackets.Finish(profile, lib, quitBrawl, null, null); }
            // The app closed mid-duel: the save has the match live; the next launch settles it as a loss, with the notice.
            earned0 = AlleyEarned(profile);
            var crashBrawl = Brackets.EnterAlley(profile, data, Brackets.BrawlKind, ctx, profile.Deck);
            if (crashBrawl != null) Brackets.StartYours(crashBrawl);
            _campaign.Save();
            bool crashLive = crashBrawl != null && Profile.Load(_campaign.SavePath)?.Bracket?.LiveMatch == crashBrawl.YourMatch();
            _campaign.DebugReload();
            profile = _campaign.Profile!;
            _campaignScreen.Open();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("13-bracket-notice");
            var crashed = profile.Bracket;
            Report($"13. a save closed mid-duel in an alley bracket loads as a loss with the notice (\"{System.Text.RegularExpressions.Regex.Replace(_campaignScreen.StatusText, "<.*?>", "")}\")",
                crashLive && crashed != null && crashed.YouOut && crashed.LiveMatch == -1 && crashed.Placement() == 3 && AlleyEarned(profile) - earned0 == AlleyEconomy.BrawlPay[3]
                && _campaignScreen.StatusText.Contains("You left the Alley Brawl mid-duel. That counts as a loss."));
            _campaignScreen.Close();
            if (crashed != null) { Brackets.SkipToEnd(crashed, _campaign.Matchups); Brackets.Finish(profile, lib, crashed, null, null); }
            _campaign.Save();
            // Nyx's Table: four seats, the house's but the first (seat 3 stands in for its headliner until it's ready).
            int nyxTable = data.IndexOf("nyx");
            _campaignScreen.DebugShowAlley(nyxTable);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("13-nyx");
            seats = _campaignScreen.AlleySeats;
            var nyxLineup = nyxTable >= 0 ? _campaign.AlleyLineup(nyxTable) : new List<AlleyOpponent>();
            Report($"13. Nyx's Table: {seats.Count} seats ({string.Join(", ", nyxLineup.Select(o => $"{o.Seat} {o.Kind} {o.Handle}"))}), \"{_campaignScreen.AlleyStakeLine}\"",
                nyxTable >= 0 && BackAlley.IsUnlocked(profile, data, nyxTable) && seats.Count == 4 && nyxLineup.Count == 4 && _campaignScreen.AlleyStakeLine.Contains($"{AlleyEconomy.DenStake:N0} DP")
                && nyxLineup[0].Kind == "regular" && nyxLineup[1].Kind == "authored"
                && nyxLineup[1].Character == (data.Tables[nyxTable].Seats.FirstOrDefault(s => s.Seat == 2)?.Character ?? "")
                && nyxLineup[3].Kind == "authored" && nyxLineup[3].Character == "nyx" && seats[0].Next);
            _campaignScreen.Close();

            // ---- 14. Back to Circuits: main street ----
            _campaignScreen.DebugShowAlley(dumpster);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("14-alley-after");
            _campaignScreen.DebugShowCircuits();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("14-circuits");
            yield return null;
            Report($"14. Circuits again: main street's theme, the whole screen (base {ColorUtility.ToHtmlStringRGB(_campaignScreen.DebugBase)}), the rain bed off",
                UiTheme.Current == UiTheme.MainStreet && !_campaignScreen.InAlley && _campaignScreen.DebugBase == UiTheme.MainStreet.Base && !Sounds.DebugBed().wanted);
            _campaignScreen.Close();

            // ---- 15. The ledger and the save ----
            _campaign.Save();
            var text = SaveText();
            _campaign.DebugReload();
            profile = _campaign.Profile!;
            _campaign.Save();
            var again = SaveText();
            var keys = new[] { "alley 1", "alleyduels ", "ante ", "alleytable ", "alleyopp ", "binder ", "stake " };
            Report($"15. the ledger reconciles (net {Ledger.Net(profile):N0} = {profile.Dp:N0} - {start:N0}; alley.stake {Spent(profile)}) with no duel held",
                profile.AnteLive == null && Ledger.Net(profile) == profile.Dp - start);
            Report($"15. the save round-trips with the alley's lines ({string.Join(", ", keys.Select(k => $"{k.Trim()} ×{text.Split('\n').Count(l => l.StartsWith(k))}"))})",
                text == again && keys.All(k => text.Split('\n').Any(l => l.StartsWith(k))));
            // ---- 16-18. The headliners' show: a fizzle and its Combo Breaker, a payoff, the exhibition and Watch the combo ----
            yield return HeadlinerShowCheck(Report, Shot, Until);
            // ---- 19-22. No Limits: the wall, the panel, the versus, a forced win (NL-1 chunk 2) ----
            yield return NoLimitsCheck(Report, Shot, Until);
            // ---- 23-24. The No Limits Invitational and Moth's restored slots (NL-2 chunk 2) ----
            yield return InvitationalCheck(Report, Shot, Until);
            AlleyEconomy.PennyChance = pennyChance;
            Debug.Log("alley check: done");
            Application.Quit();
        }

        /// <summary>
        /// The No Limits table and its wave-1 builds made ready in this run's data alone (they ship `ready: no` until the
        /// harness's ship gate; the data file is never edited): the table, and every build in its pool or champions whose deck
        /// file is here. Returns what undoes it.
        /// </summary>
        private List<Action> ForceNoLimitsReady(out int table)
        {
            var data = _campaign.AlleyData;
            var ctx = _campaign.AlleyContext!;
            var undo = new List<Action>();
            table = -1;
            for (int i = 0; i < data.Tables.Count && table < 0; i++) if (data.Tables[i].Tier == AlleyTier.NoLimits) table = i;
            if (table < 0) return undo;
            var t = data.Tables[table];
            bool was = t.Ready;
            t.Ready = true;
            undo.Add(() => t.Ready = was);
            foreach (var id in t.Pool.Concat(t.Headliners).Distinct())
            {
                var h = data.FindBuild(id);
                if (h == null || h.Ready || h.Deck.Length == 0 || ctx.LoadDeck(h.Deck) == null) continue;
                h.Ready = true;
                undo.Add(() => h.Ready = false);
            }
            return undo;
        }

        /// <summary>
        /// Steps 19-22 (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 14.3, chunk 2; numbered after the show's 16-18): No Limits locked
        /// until the wall opens, and the nolimits scene on the next tab show after a forced Nyx clear and the alley-closing scene,
        /// never with them; the panel (four seats, THE END CHAIR on seat 4, UNBANNED rows, the 2,500 stake); the versus (UNBANNED
        /// and CONTRABAND over the pieces; 3 Pot of Greed accepted here and refused at Dumpster Row; Djinn refused at both); a
        /// forced win (the restored card in the collection, Paid 0; restored and pilotbeat: play, the pilot speaking for "opponent";
        /// the deck editor from this panel under the No Limits rules).
        /// </summary>
        private System.Collections.IEnumerator NoLimitsCheck(Action<string, bool> Report, Func<string, System.Collections.IEnumerator> Shot,
            Func<Func<bool>, float, System.Collections.IEnumerator> Until)
        {
            var profile = _campaign.Profile!;
            var data = _campaign.AlleyData;
            var ctx = _campaign.AlleyContext!;
            string Plain(string t) => System.Text.RegularExpressions.Regex.Replace(t, "<.*?>", "");
            var undo = ForceNoLimitsReady(out int nl);
            int nyx = data.IndexOf("nyx"), dumpster = data.IndexOf("dumpster-row");
            if (nl < 0 || nyx < 0 || dumpster < 0)
            {
                Report("19. the No Limits table, Nyx's Table and Dumpster Row are in the data", false);
                yield break;
            }
            var table = data.Tables[nl];
            var ready = table.Pool.Concat(table.Headliners).Select(data.FindBuild).Where(h => h != null && h.Ready).Select(h => h!.Id).ToList();
            Report($"19. No Limits forced ready in this run's data (not the file): {ready.Count} builds with decks ({string.Join(", ", ready)}), table ready {table.Ready}",
                table.Ready && ready.Count >= 4 && ready.Contains("junia"));
            // The scenes this step is about, unseen again (step 2 marked every alley scene seen): the closing, the wall's, the pilots', the restored card's.
            void Unsee(params string[] triggers)
            {
                foreach (var scene in _story.Pending(new Profile(), triggers)) profile.Seen.Remove(scene.Id);
            }
            Unsee("alleyclear:" + data.Tables[nyx].Name, BackAlley.WallEvent);
            profile.Seen.Remove(BackAlley.WallEvent);
            profile.Seen.Remove(BackAlley.ClosingScene);
            // The Library's strand undone, so the alley's closing alone can be seen to raise no finale.
            profile.Seen.Remove(Story.LibraryStrandScene);
            profile.Seen.Remove(Story.FinaleScene);
            if (!profile.AlleyTables.TryGetValue(data.Tables[nyx].Id, out var nyxState)) profile.AlleyTables[data.Tables[nyx].Id] = nyxState = new AlleyTableState();
            nyxState.Cleared = false;
            _campaign.Save();

            // ---- 19. Locked until the wall opens ----
            _campaignScreen.DebugShowAlley(nyx);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("19-locked");
            var nodes = _campaignScreen.DebugAlleyNodes;
            (string name, bool open, string locked) nlNode = nodes.Count > nl ? nodes[nl] : ("", false, "");
            Report($"19. before Nyx's clear the map shows \"{nlNode.name}\" locked: \"{nlNode.locked}\" (after Nyx's Table in the data; WallDue {_campaign.WallDue})",
                nlNode.name == table.Name && !nlNode.open && nlNode.locked == "Behind Nyx's wall" && nl == nyx + 1 && !_campaign.WallDue && !BackAlley.IsUnlocked(profile, data, nl));
            // Nyx's Table cleared (forced), the closing scene not seen yet: the tab shows it still locked, and no wall opens.
            nyxState.Cleared = true;
            _campaign.Save();
            _campaignScreen.DebugShowCircuits();
            _campaignScreen.DebugShowAlley(nyx);
            yield return new WaitForSecondsRealtime(0.5f);
            nlNode = _campaignScreen.DebugAlleyNodes[nl];
            bool stillLocked = !nlNode.open && !_storyScreen.IsOpen && !profile.Seen.Contains(BackAlley.WallEvent) && !_campaign.WallDue;
            // The closing scene plays (as Nyx's clear plays it): the wall is due from here, but never opens on this screen.
            _campaignScreen.Close();
            bool wallDuringClosing = false, closingSeen = false, nothingElseQueued = false;
            // With the real continuation (the results' Continue reopens the campaign screen in alley view, review fix 1).
            PlayStory(new[] { "alleyclear:" + data.Tables[nyx].Name }, "", () => _campaignScreen.Open());
            yield return Until(() => _storyScreen.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.3f);
            // Nyx's clear plays nyx-table-clear first, then alley-closing: through to the closing's first line.
            for (int k = 0; k < 40 && _storyScreen.IsOpen && !profile.Seen.Contains(BackAlley.ClosingScene); k++) { _storyScreen.DebugAdvance(); yield return null; }
            if (_storyScreen.IsOpen)
            {
                closingSeen = profile.Seen.Contains(BackAlley.ClosingScene);
                wallDuringClosing = profile.Seen.Contains(BackAlley.WallEvent);
                // The closing's own lines are what's on screen, not the wall's.
                nothingElseQueued = _storyScreen.DebugText.Contains("Two in the morning");
                yield return Shot("19-closing");
                _storyScreen.DebugFinish();
            }
            // The screen the Continue reopens is the same night: the wall stays shut there, and no wall scene follows the closing.
            yield return Until(() => _campaignScreen.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.6f);
            bool shutAfterClosing = _campaignScreen.IsOpen && !_storyScreen.IsOpen && !profile.Seen.Contains(BackAlley.WallEvent);
            Report($"19. a forced Nyx clear alone keeps it locked ({stillLocked}); the alley-closing scene plays (seen {closingSeen}) with the wall still shut ({!wallDuringClosing}), and the screen its Continue reopens keeps it shut ({shutAfterClosing}), WallDue now {_campaign.WallDue}",
                stillLocked && closingSeen && !wallDuringClosing && nothingElseQueued && shutAfterClosing && _campaign.WallDue && !BackAlley.IsUnlocked(profile, data, nl));
            // The alley's strand alone (the Library's undone: every-era-corkboard unseen) raises no finale (Story.FinaleDue), on
            // the screen the closing's Continue reopened either.
            Report($"19. Nyx's Table cleared and alley-closing read, the Library's strand undone: the finale isn't raised (FinaleDue {_campaign.FinaleDue}, seen {profile.Seen.Contains(Story.FinaleScene)})",
                closingSeen && !profile.Seen.Contains(Story.LibraryStrandScene) && !_campaign.FinaleDue && !profile.Seen.Contains(Story.FinaleScene));
            // The next tab show: the wall opens (nolimits in Seen) and its scene plays, then the table is open.
            _campaignScreen.DebugShowCircuits();
            yield return new WaitForSecondsRealtime(0.3f);
            _campaignScreen.DebugShowAlley(nl);
            yield return Until(() => _storyScreen.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("19-wall-opens");
            bool wallScene = _storyScreen.IsOpen && _storyScreen.DebugText.Contains("Friday again");
            bool wallOpen = profile.Seen.Contains(BackAlley.WallEvent) && !_campaign.WallDue;
            if (_storyScreen.IsOpen) _storyScreen.DebugFinish();
            yield return Until(() => _campaignScreen.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.5f);
            nlNode = _campaignScreen.DebugAlleyNodes[nl];
            Report($"19. the next tab show opens the wall: the nl-wall-opens scene plays (\"{Plain(_storyScreen.DebugText).Split('\n')[0]}\"), nolimits in Seen, and the map shows {nlNode.name} open ({nlNode.open})",
                wallScene && wallOpen && nlNode.open && BackAlley.IsUnlocked(profile, data, nl) && _campaignScreen.IsOpen && _campaignScreen.InAlley);

            // ---- 20. The panel ----
            _campaignScreen.DebugShowAlley(nl);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("20-no-limits-panel");
            var seats = _campaignScreen.AlleySeats;
            var lineup = _campaign.AlleyLineup(nl);
            bool fourBuilds = lineup.Count == 4 && lineup.All(o => o.IsBuild) && lineup.Take(3).All(o => o.Kind is BackAlley.BuildKind or "wildcard")
                && lineup[3].Kind == "headliner" && lineup[3].Character == "junia" && lineup.Select(o => o.Character).Distinct().Count() == 4;
            Report($"20. the panel: {seats.Count} seats ({string.Join(", ", lineup.Select(o => $"{o.Seat} {o.Kind} {o.Handle}"))}), seat 4 tagged \"{seats.ElementAtOrDefault(3)?.Tag}\", "
                   + $"\"{_campaignScreen.AlleyStakeLine}\", rules \"{_campaignScreen.AlleyRulesLine}\"",
                seats.Count == 4 && fourBuilds && seats[3].Tag == CampaignScreen.EndChairTag && seats.Take(3).All(s => s.Tag != CampaignScreen.EndChairTag)
                && _campaignScreen.AlleyStakeLine.Contains($"{AlleyEconomy.NoLimitsStake:N0} DP") && _campaignScreen.AlleyStakeLine.StartsWith("No Limits")
                && _campaignScreen.AlleyRulesLine == CampaignScreen.NoLimitsRulesChip && seats[0].Next
                && _campaignScreen.StatusText.StartsWith("No Limits · no list, three of anything"));
            // Baz MCs the depot on its panel (baz:nolimits): a line of that pool after the rules.
            var bazPool = File.ReadAllLines(Path.Combine(FindDataFolder("campaign", "campaign"), "alley-voice.txt"))
                .SkipWhile(l => l.Trim() != "pool: baz:nolimits").Skip(1).TakeWhile(l => l.StartsWith("baz:")).Select(l => l.Substring(4).Trim()).ToList();
            Report($"20. Baz calls the room on the No Limits panel (\"{_campaignScreen.BazLine}\", one of baz:nolimits' {bazPool.Count})",
                bazPool.Contains(_campaignScreen.BazLine) && _campaignScreen.StatusText.Contains(_campaignScreen.BazLine));
            Report($"20. an UNBANNED row under every seat's deck title ({string.Join(" · ", seats.Select(s => s.Unbanned))}), each a restored card of the build at the deck's count",
                seats.Count == 4 && seats.All(s => s.Unbanned.StartsWith("UNBANNED: ") && s.Unbanned.Contains(" ×"))
                && lineup.All(o => BackAlley.Unbanned(o, ctx).Count > 0 && data.FindBuild(o.Character)!.Restored.Contains(BackAlley.Unbanned(o, ctx)[0])
                                    && BackAlley.CopiesIn(o, BackAlley.Unbanned(o, ctx)[0], ctx) >= 1)
                && seats.All(s => s.StakeValue >= AlleyEconomy.NoLimitsStake && s.StakeValue <= AlleyEconomy.NoLimitsCap));
            var musicChair = BossMusic.ForAlley(data, nl, table.SeatCount);
            var musicSeat1 = BossMusic.ForAlley(data, nl, 1);
            Report($"20. the music (never heard here): the end chair plays {musicChair.theme}:{musicChair.transpose} through the host-seat rule, seat 1 {musicSeat1.theme}:{musicSeat1.transpose}",
                musicChair == ("no-limits", 0) && musicSeat1 == (BossMusic.Alley, 0));

            // ---- 21. The versus ----
            var o1 = lineup[0];
            var build1 = data.FindBuild(o1.Character)!;
            Unsee(BackAlley.PilotEvent + o1.Character);
            _campaign.Save();
            _campaignScreen.DebugAlleyDuel();
            yield return Until(() => _storyScreen.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.3f);
            bool pilotScene = _storyScreen.IsOpen && _story.Pending(new Profile(), new[] { BackAlley.PilotEvent + o1.Character }).Count > 0;
            if (_storyScreen.IsOpen) { yield return Shot("21-pilot"); _storyScreen.DebugFinish(); }
            yield return Until(() => _scenes.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("21-versus");
            var shown = _alleyVersusShowcase;
            var labels = _alleyVersusLabels;
            var picks = build1.Picks.ToList();
            bool labelled = shown.Count == 3 && labels.Count == 3 && labels[0] == "UNBANNED" && labels.Contains("CONTRABAND")
                && shown.Select((c, i) => (c, i)).All(x => labels[x.i] == (build1.Restored.Contains(x.c) ? "UNBANNED" : picks.Contains(x.c) ? "CONTRABAND" : ""));
            Report($"21. {o1.Handle}'s versus: pilot:{o1.Character} plays first ({pilotScene}); the pieces {string.Join(", ", shown.Select(c => _cardPack!.Get(c)?.Name))} labelled {string.Join("/", labels)}",
                pilotScene && _scenes.IsOpen && labelled && profile.Seen.Contains("nl-" + o1.Character));
            int stakeValue = _alleyVersusStake != null ? Ante.Value(_alleyVersusStake, ctx) : -1;
            Report($"21. the deck line \"{_alleyVersusDeckLine}\" (#title + picks), the UNBANNED row and Baz's line (\"{Plain(_alleyVersusDetail).Replace('\n', ' ')}\"), the stake chip ({stakeValue:N0} DP)",
                _alleyVersusDeckLine.Contains(" + ") && picks.All(p => _alleyVersusDeckLine.Contains(_cardPack!.Get(p)?.Name ?? "?"))
                && Plain(_alleyVersusDetail).StartsWith("UNBANNED: ") && Plain(_alleyVersusDetail).Contains("Baz") && _alleyVersusStake != null
                && _alleyVersusChip.Contains($"{stakeValue:N0} DP") && stakeValue >= AlleyEconomy.NoLimitsStake && stakeValue <= AlleyEconomy.NoLimitsCap);
            _scenes.Close();
            // A deck of yours with 3 Pot of Greed (owned): legal here, refused at Dumpster Row; one with Djinn: refused at both.
            var deckBefore = profile.Deck;
            uint pot = Alley.CodeOf("Pot of Greed", _cardPack!), djinn = Alley.CodeOf("Djinn Releaser of Rituals", _cardPack!);
            var basis = _library.Load(deckBefore);
            var pots = basis.Clone();
            pots.Name = "Alley Check Three Pots";
            pots.Campaign = true;
            for (int k = 0; k < 3; k++) pots.Main.Add(pot);
            for (int k = profile.Collection.Count(pot); k < 3; k++) profile.Collection.Insert(new CardCopy { Code = pot, Rarity = Rarity.Common, Grade = 8, Id = profile.Collection.NextId++, Source = CopySource.Pack, Paid = 10 });
            var djinns = basis.Clone();
            djinns.Name = "Alley Check Djinn";
            djinns.Campaign = true;
            djinns.Main.Add(djinn);
            if (profile.Collection.Count(djinn) < 1) profile.Collection.Insert(new CardCopy { Code = djinn, Rarity = Rarity.Common, Grade = 8, Id = profile.Collection.NextId++, Source = CopySource.Pack, Paid = 10 });
            _library.Save(pots);
            _library.Save(djinns);
            var potsHere = _campaign.AlleyProblems(pots, nl);
            var potsRow = _campaign.AlleyProblems(pots, dumpster);
            var djinnHere = _campaign.AlleyProblems(djinns, nl);
            var djinnRow = _campaign.AlleyProblems(djinns, dumpster);
            // The versus with the three Pots: no refusal here (the note stays empty); at Dumpster Row the Alley's words.
            profile.Deck = pots.Name;
            _versusDeck = null;
            ShowAlleyVersus(nl, null);
            yield return new WaitForSecondsRealtime(0.4f);
            var potsNote = _alleyLastNote;
            bool potsVersus = _scenes.IsOpen && _scenes.Buttons.ContainsKey("Duel!");
            yield return Shot("21-three-pots");
            _scenes.Close();
            string rowNote = "";
            if (BackAlley.NextSeat(profile, data, dumpster) >= 1)
            {
                ShowAlleyVersus(dumpster, null);
                yield return new WaitForSecondsRealtime(0.4f);
                rowNote = _alleyLastNote;
                _scenes.Close();
            }
            Report($"21. a deck with 3 Pot of Greed is accepted here (\"{Plain(potsNote)}\") and refused at Dumpster Row (\"{potsRow.FirstOrDefault()?.Message}\"{(rowNote.Length > 0 ? $"; the versus: \"{Plain(rowNote)}\"" : "")})",
                potsHere.Count == 0 && potsNote.Length == 0 && potsVersus && potsRow.Any(p => p.Message == "Pot of Greed: the Alley allows 1, you have 3.")
                && (rowNote.Length == 0 || rowNote.Contains("Pot of Greed: the Alley allows 1, you have 3.")));
            profile.Deck = djinns.Name;
            _versusDeck = null;
            ShowAlleyVersus(nl, null);
            yield return new WaitForSecondsRealtime(0.4f);
            int dpBefore = profile.Dp;
            if (_scenes.Buttons.TryGetValue("Duel!", out var refused)) refused();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("21-djinn-refused");
            Report($"21. a deck with Djinn Releaser of Rituals is refused here (\"{Plain(_alleyLastNote)}\") and at Dumpster Row (\"{djinnRow.FirstOrDefault()?.Message}\"), nothing held",
                djinnHere.Any(p => p.Message == "Djinn Releaser of Rituals: No Limits doesn't allow it (you have 1).") && djinnRow.Any(p => p.Message == "Djinn Releaser of Rituals: the Alley doesn't allow it (you have 1).")
                && _alleyLastNote.Contains("isn't legal at No Limits") && _alleyLastNote.Contains("No Limits doesn't allow it") && profile.AnteLive == null && profile.Dp == dpBefore);
            _scenes.Close();
            profile.Deck = deckBefore;
            _campaign.Save();

            // ---- 22. A forced win at seat 1: the restored card, Paid 0; its scenes; the deck editor ----
            Unsee(BackAlley.PilotBeatEvent + o1.Character, BackAlley.RestoredEvent);
            o1 = _campaign.AlleySeat(nl, 1)!;
            var theirs = Ante.Next(profile, o1.Binder, ctx);
            bool leadRestored = theirs != null && build1.Restored.Contains(theirs.Code) && build1.Stakes.Contains(theirs.Code);
            if (profile.Dp < AlleyEconomy.NoLimitsStake) profile.Earn(Ledger.Debug, AlleyEconomy.NoLimitsStake);
            dpBefore = profile.Dp;
            var held = Ante.Hold(profile, data, nl, o1, null, _campaign.NeededByDecks, ctx);
            int heldDp = held?.Held ?? 0;
            var won = BackAlley.Record(profile, data, nl, 0, 12, 0, ctx);
            _campaign.Save();
            var copy = won.Ante?.Won;
            bool kept = won.Ante?.Added?.Kept ?? false;
            var refusal = copy != null && kept ? SellDesk.Blocked(profile, copy, _campaign.NeededByDecks) : null;
            Report($"22. a forced win at seat 1 ({o1.Handle}): {heldDp:N0} DP held and back ({profile.Dp - dpBefore:+0;-0}), {(theirs != null ? _campaign.StakeText(theirs) : "nothing")} is yours "
                   + $"(Source {copy?.Source}, Paid {copy?.Paid}; a restored lead: {leadRestored}); events {string.Join(", ", won.Events)}; the Sell desk says \"{refusal}\"",
                held != null && heldDp == AlleyEconomy.NoLimitsStake && profile.Dp == dpBefore && copy != null && copy.Source == CopySource.Ante && copy.Paid == 0 && theirs != null && copy.Code == theirs.Code
                && (!kept || (profile.Collection.All.Contains(copy) && refusal == SellDesk.AnteRefusal)) && won.Events.Contains("antewin")
                && won.Events.Contains(BackAlley.PilotBeatEvent + o1.Character) && won.Events.Contains(BackAlley.RestoredEvent) == leadRestored && won.SeatWon
                && BackAlley.NextSeat(profile, data, nl) == 2);
            // Its scenes, as the results screen plays them: pilotbeat:<id>'s, then nl-restored with the pilot speaking for "opponent".
            var who = BackAlley.PersonalityOf(o1, ctx);
            won.Fill[StoryScreen.OpponentSpeaker] = o1.Character;
            var pending = _story.Pending(profile, won.Events).Select(s => s.Id).ToList();
            PlayStory(won.Events, "", () => { }, won.Reward.Grants, won.Fill);
            yield return Until(() => _storyScreen.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.3f);
            bool beatScene = _storyScreen.IsOpen && pending.Contains("nl-" + o1.Character + "-beat");
            yield return Shot("22-pilotbeat");
            // Through the beat scene's lines to nl-restored's "opponent" line (the pilot's name on the plate).
            string opponentName = "", restoredText = "";
            for (int k = 0; k < 60 && _storyScreen.IsOpen && opponentName.Length == 0; k++)
            {
                if (_storyScreen.DebugText.Contains("The list took this")) { opponentName = _storyScreen.DebugSpeaker; restoredText = Plain(_storyScreen.DebugText); break; }
                _storyScreen.DebugAdvance();
                yield return null;
            }
            if (opponentName.Length > 0) { yield return new WaitForSecondsRealtime(0.3f); yield return Shot("22-restored"); }
            if (_storyScreen.IsOpen) _storyScreen.DebugFinish();
            yield return new WaitForSecondsRealtime(0.3f);
            Report($"22. the scenes play: {string.Join(", ", pending)}; in nl-restored \"opponent\" is {o1.Handle}'s pilot (\"{restoredText}\")",
                beatScene && pending.Contains("nl-" + o1.Character + "-beat") && (pending.Contains("nl-restored") == leadRestored)
                && (!leadRestored || (opponentName.StartsWith(who.Name) && restoredText.Contains("The list took this")))
                && profile.Seen.Contains("nl-" + o1.Character + "-beat"));
            // The deck editor from this panel: the No Limits rules; from Dumpster Row's, the Alley's.
            _campaignScreen.DebugShowAlley(nl);
            yield return new WaitForSecondsRealtime(0.4f);
            _campaignScreen.DebugEditDeck();
            yield return Until(() => _editor.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("22-editor");
            var editorList = _editor.IsOpen ? _editor.BanlistName : "";
            _editor.Close();
            yield return new WaitForSecondsRealtime(0.3f);
            _campaignScreen.DebugShowAlley(dumpster);
            yield return new WaitForSecondsRealtime(0.3f);
            _campaignScreen.DebugEditDeck();
            yield return Until(() => _editor.IsOpen, 5);
            var rowList = _editor.IsOpen ? _editor.BanlistName : "";
            _editor.Close();
            yield return new WaitForSecondsRealtime(0.3f);
            Report($"22. the deck editor from the No Limits panel uses the No Limits list (\"{editorList}\"); from Dumpster Row's the Alley's (\"{rowList}\")",
                editorList == (ctx.NoLimits?.Name ?? "No Limits") && rowList == _campaign.AlleyRules.List?.Name && editorList != rowList);
            _campaignScreen.Close();
            foreach (var back in undo) back();
            _campaign.Save();
        }
    
        /// <summary>
        /// Step 23 (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 14.4, chunk 2): the No Limits Invitational. The chalkboard hides it
        /// until the wall opens (whatever else is true); then "Coming soon" until its field is ready, "Clear No Limits to unlock"
        /// until No Limits is cleared, and Enter. With No Limits and the field forced ready in this run's data (never the file)
        /// and No Limits cleared: the entry dialog (16 seeds, Remy 1, you 2, 3,000 DP), Enter charges nolimits.entry and plays
        /// its scene; the bracket (16 slots, Bo3 pips, the No Limits rules chip); the versus under the No Limits rules (3 Pot of
        /// Greed accepted); a forced title (1,500 into nolimits, 2 Card Tokens once, No Limits Packs revealed, its scenes, the
        /// invitational-bill poster). The Locker hides No Limits' posters until the wall opens; the chalkboard's header reads as
        /// before with two blocks and bracket-neutral with three; a pilot on its peak build (Junia) is no out-of-towner at the
        /// Invitational. Step 24: with the wall open and wave 1 forced ready, Moth's shelf shows its restored slots, tagged.
        /// What the steps force is put back (the data's Ready flags, No Limits' clear, the wall and closing marks, your deck, the
        /// Invitational's record, token key and tokens); the rest stays, as the run's last step on a throwaway save: the DP and
        /// its ledger lines, BracketsEntered, the Pots of Greed and pack cards, head-to-head records, the No Limits table entry.
        /// </summary>
        private System.Collections.IEnumerator InvitationalCheck(Action<string, bool> Report, Func<string, System.Collections.IEnumerator> Shot,
            Func<Func<bool>, float, System.Collections.IEnumerator> Until)
        {
            var profile = _campaign.Profile!;
            var data = _campaign.AlleyData;
            var ctx = _campaign.AlleyContext!;
            var lib = _campaign.Library;
            const string kind = Brackets.InvitationalKind;
            string Plain(string t) => System.Text.RegularExpressions.Regex.Replace(t, "<.*?>", "");
            void Unsee(params string[] triggers)
            {
                foreach (var scene in _story.Pending(new Profile(), triggers)) profile.Seen.Remove(scene.Id);
            }
            var spec = data.Brackets.FirstOrDefault(x => x.Kind == kind);
            var undo = ForceNoLimitsReady(out int nl);
            if (spec == null || nl < 0)
            {
                Report("23. the No Limits Invitational and the No Limits table are in the data", false);
                foreach (var back in undo) back();
                yield break;
            }
            var nlId = data.Tables[nl].Id;
            if (!profile.AlleyTables.TryGetValue(nlId, out var nlState)) profile.AlleyTables[nlId] = nlState = new AlleyTableState();
            bool clearedWas = nlState.Cleared, wallWas = profile.Seen.Contains(BackAlley.WallEvent);
            var deckWas = profile.Deck;
            int tokensWas = profile.Tokens;
            bool tokenKeyWas = profile.Seen.Contains(Brackets.InvitationalTokenKey);
            bool hadRecord = profile.AlleyBrackets.TryGetValue(kind, out var recordWas);
            recordWas = recordWas == null ? null : new LocalsRecord { Entries = recordWas.Entries, Titles = recordWas.Titles, Best = recordWas.Best };
            var fieldUndo = new List<Action>();
            if (profile.Dp < 3 * AlleyEconomy.InvitationalEntry) profile.Earn(Ledger.Debug, 3 * AlleyEconomy.InvitationalEntry);
            int board = _campaignScreen.DebugChalkboard;
            List<(string kind, string label, bool open)> Board() => _campaignScreen.ChalkboardRows.ToList();
            string Rows(List<(string kind, string label, bool open)> rows) => string.Join(" · ", rows.Select(r => $"{r.kind} \"{r.label}\""));

            // ---- 23. Hidden before the wall opens, even with No Limits cleared ----
            // (The closing unseen too, so the tab show doesn't open the wall: BackAlley.WallDue.)
            nlState.Cleared = true;
            profile.Seen.Remove(BackAlley.WallEvent);
            bool closingWas = profile.Seen.Remove(BackAlley.ClosingScene);
            _campaign.Save();
            _campaignScreen.DebugShowAlley(board);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("23-chalkboard-before-wall");
            var hidden = Board();
            Report($"23. before the wall opens the chalkboard doesn't list the Invitational (No Limits cleared, forced): {Rows(hidden)}",
                hidden.Count == 2 && hidden.All(r => r.kind != kind) && _campaignScreen.InAlley && !profile.Seen.Contains(BackAlley.WallEvent));
            var twoHeader = Plain(_campaignScreen.DebugPanelText);
            string twoStake = _campaignScreen.AlleyStakeLine;
            Report($"23. with two blocks the chalkboard's header reads as before (\"{twoStake}\")",
                twoStake == "Alley brackets · an entry fee, placement pay and Contraband Packs · no stakes"
                && twoHeader.Contains("Alley list · your own decks, every round · turn 40 · higher LP wins  ·  Baz calls it, Mika streams it.") && !twoHeader.Contains("Invitational"));
            _campaignScreen.Close();
            // The Locker before the wall: No Limits' posters (the-wall, invitational-bill) aren't listed; the Midnight Open's is.
            _campaignScreen.Open();
            _campaignScreen.DebugOpenLocker();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("23-locker-before-wall");
            bool lockerHides = !_campaignScreen.DebugLockerShows("the-wall") && !_campaignScreen.DebugLockerShows("invitational-bill") && _campaignScreen.DebugLockerShows("midnight-bill");
            _campaignScreen.DebugCloseLocker();
            _campaignScreen.Close();
            if (closingWas) profile.Seen.Add(BackAlley.ClosingScene);
            profile.Seen.Add(BackAlley.WallEvent);
            _campaign.Save();
            _campaignScreen.Open();
            _campaignScreen.DebugOpenLocker();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("23-locker-after-wall");
            bool lockerShows = _campaignScreen.DebugLockerShows("the-wall") && _campaignScreen.DebugLockerShows("invitational-bill") && _campaignScreen.DebugLockerShows("midnight-bill");
            _campaignScreen.DebugCloseLocker();
            _campaignScreen.Close();
            Report($"23. the Locker lists No Limits' posters (Poster.HiddenUntil: the wall) only once the wall has opened (before: hidden {lockerHides}; after: shown {lockerShows}); Posters.All still ends with {Posters.All[^1].Id}",
                lockerHides && lockerShows && Posters.All[^1].Id == "midnight-bill"
                && Posters.All.Where(x => x.HiddenUntil.Length > 0).Select(x => x.Id).OrderBy(x => x).SequenceEqual(new[] { "invitational-bill", "the-wall" }));
            // The wall open, the field not ready (wave 2 ships `ready: no`): "Coming soon", whatever's cleared.
            var notReady = Brackets.FieldBuilds(data, spec).Where(h => !h.Ready).Select(h => h.Id).ToList();
            _campaign.Save();
            _campaignScreen.DebugShowAlley(board);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("23-chalkboard-coming-soon");
            var soon = Board();
            Report($"23. with the wall open its block appears, \"Coming soon\" while {notReady.Count} field builds aren't ready ({string.Join(", ", notReady)}): {Rows(soon)}",
                soon.Count == 3 && soon[2] == (kind, "Coming soon", false) && notReady.Count > 0 && soon[0].kind == Brackets.BrawlKind && soon[1].kind == Brackets.MidnightKind);
            _campaignScreen.Close();
            // The field forced ready (this run's data only), No Limits not cleared: the lock line.
            foreach (var h in Brackets.FieldBuilds(data, spec).Where(h => !h.Ready))
            {
                var build = h;
                build.Ready = true;
                fieldUndo.Add(() => build.Ready = false);
            }
            nlState.Cleared = false;
            _campaign.Save();
            _campaignScreen.DebugShowAlley(board);
            yield return new WaitForSecondsRealtime(0.5f);
            var locked = Board();
            Report($"23. the field forced ready, No Limits not cleared: {Rows(locked)}",
                locked.Count == 3 && locked[2] == (kind, $"Clear {spec.Opens} to unlock", false) && Brackets.FieldReady(data, spec));
            _campaignScreen.Close();
            // No Limits cleared: Enter.
            nlState.Cleared = true;
            _campaign.Save();
            _campaignScreen.DebugShowAlley(board);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("23-chalkboard");
            var rows = Board();
            Report($"23. No Limits cleared: the chalkboard shows the Invitational ({Rows(rows)}); the Brawl's and the Open's blocks as before",
                rows.Count == 3 && rows[2] == (kind, $"Enter · {AlleyEconomy.InvitationalEntry:N0}", true)
                && rows[0] == (Brackets.BrawlKind, $"Enter · {AlleyEconomy.BrawlEntry:N0}", true) && rows[1] == (Brackets.MidnightKind, $"Enter · {AlleyEconomy.OpenEntry:N0}", true)
                && _campaignScreen.InAlley && UiTheme.Current.IsAlley);
            var threeHeader = Plain(_campaignScreen.DebugPanelText);
            Report($"23. with the Invitational up the header is bracket-neutral (\"{_campaignScreen.AlleyStakeLine}\")",
                _campaignScreen.AlleyStakeLine.Contains("Contraband or No Limits Packs") && threeHeader.Contains("The Alley list, or no list at the Invitational"));

            // ---- 23. The entry dialog and Enter ----
            Unsee(kind);
            _campaign.Save();
            int spent0 = profile.DpSpent.GetValueOrDefault(Ledger.NoLimitsEntry), dpBefore = profile.Dp;
            _campaignScreen.DebugChalkboardButton(kind);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("23-entry");
            var preview = _bracketScreen.DebugEntryNames.ToList();
            var entryText = _bracketScreen.DebugEntryText;
            var remyName = _campaign.Cast.TryGetValue("remy", out var remyCast) ? remyCast.Name : "remy";
            Report($"23. the entry dialog: {_bracketScreen.DebugEntryRows} seeds ({string.Join(", ", preview.Take(3))}...), entry {AlleyEconomy.InvitationalEntry:N0} DP, No Limits Packs and their odds, the No Limits rules",
                _bracketScreen.EntryOpen && _bracketScreen.DebugEntryRows == 16 && preview.Count == 16 && preview[0] == remyName && preview[1] == "You"
                && entryText.Contains($"{AlleyEconomy.InvitationalEntry:N0} DP") && entryText.Contains("No Limits Pack") && !entryText.Contains("Contraband Pack")
                && entryText.Contains("No list · three of anything") && entryText.Contains("the No Limits rules") && entryText.Contains($"+{AlleyEconomy.InvitationalTokens} Card Tokens")
                && _bracketScreen.DebugEntryEnterable);
            _bracketScreen.DebugEnter();
            yield return Until(() => _storyScreen.IsOpen || _bracketScreen.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.4f);
            bool entryScene = _storyScreen.IsOpen;
            if (_storyScreen.IsOpen) { yield return Shot("23-invitational-scene"); _storyScreen.DebugFinish(); }
            entryScene &= profile.Seen.Contains("nl-invitational");
            yield return Until(() => _bracketScreen.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("23-invitational");
            var inv = profile.Bracket;
            var (boxes, overlaps) = _bracketScreen.DebugOverlaps();
            var remySlot = inv?.Slots.FirstOrDefault(e => e.Seed == 1);
            Report($"23. Enter charges {dpBefore - profile.Dp:N0} DP (nolimits.entry +{profile.DpSpent.GetValueOrDefault(Ledger.NoLimitsEntry) - spent0:N0}), plays the invitational scene ({entryScene}), and opens "
                   + $"16 slots ({boxes} boxes{(overlaps.Count > 0 ? ": " + string.Join(", ", overlaps.Take(3)) : "")}): {remySlot?.Character} seed 1, you seed {inv?.Slots[inv.YourSlot].Seed}, best of {inv?.FinalBestOf} in the final",
                inv != null && inv.Kind == kind && inv.Size == 16 && inv.FinalBestOf == 3 && boxes == 31 && overlaps.Count == 0 && remySlot?.Character == "remy"
                && inv.Slots[inv.YourSlot].Seed == 2 && dpBefore - profile.Dp == AlleyEconomy.InvitationalEntry && AlleyEconomy.InvitationalEntry == 3000
                && profile.DpSpent.GetValueOrDefault(Ledger.NoLimitsEntry) - spent0 == AlleyEconomy.InvitationalEntry && entryScene && UiTheme.Current.IsAlley
                && inv.Slots.Count(e => e.You) == 1);
            if (inv == null) { foreach (var back in fieldUndo.Concat(undo)) back(); yield break; }
            var chip = Plain(_bracketScreen.DebugText);
            Report("23. the bracket's rules chip reads the No Limits rules (\"No list, three of anything, turn 40, higher LP wins · final best of 3\")",
                chip.Contains("No list, three of anything, turn 40, higher LP wins · final best of 3") && chip.Contains("under the No Limits rules"));

            // ---- 23. The versus: the No Limits rules (a deck with 3 Pot of Greed accepted), Baz's round ----
            uint pot = Alley.CodeOf("Pot of Greed", _cardPack!);
            var pots = _library.Contains("Alley Check Three Pots") ? _library.Load("Alley Check Three Pots") : null;
            if (pots != null) pots.Name = "Alley Check Three Pots";
            for (int k = profile.Collection.Count(pot); k < 3; k++) profile.Collection.Insert(new CardCopy { Code = pot, Rarity = Rarity.Common, Grade = 8, Id = profile.Collection.NextId++, Source = CopySource.Pack, Paid = 10 });
            var potsHere = pots == null ? null : _campaign.AlleyProblems(pots, _campaign.AlleyBracketRules(kind));
            var potsAlley = pots == null ? null : _campaign.AlleyProblems(pots, _campaign.AlleyBracketRules(Brackets.MidnightKind));
            if (pots != null) profile.Deck = pots.Name;
            _versusDeck = null;
            _bracketVersusRules = null;
            _bracketScreen.DebugDuel();
            yield return Until(() => _scenes.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("23-versus");
            var mcPool = File.ReadAllLines(Path.Combine(FindDataFolder("campaign", "campaign"), "alley-voice.txt"))
                .SkipWhile(l => l.Trim() != "pool: mc:invitational:round-of-16").Skip(1).TakeWhile(l => l.StartsWith("baz:")).Select(l => l.Substring(4).Trim()).ToList();
            var mcText = _bracketMc?.text ?? "";
            Report($"23. your round of 16's versus: under the No Limits rules ({_bracketVersusRules?.List?.Name}), the deck with 3 Pot of Greed accepted (\"{Plain(_alleyLastNote)}\"; under the Alley's: \"{potsAlley?.FirstOrDefault()?.Message}\"); "
                   + $"Baz from mc:invitational:round-of-16 (\"{mcText}\")",
                _scenes.IsOpen && _scenes.Buttons.ContainsKey("Duel!") && _bracketVersusRules != null && _bracketVersusRules.List == ctx.NoLimits && _bracketVersusRules != _campaign.AlleyRules
                && pots != null && potsHere != null && potsHere.Count == 0 && _alleyLastNote.Length == 0 && potsAlley != null && potsAlley.Any(x => x.Message == "Pot of Greed: the Alley allows 1, you have 3.")
                && mcPool.Count > 0 && mcPool.Any(line => System.Text.RegularExpressions.Regex.IsMatch(mcText,
                    "^" + string.Join(".*", System.Text.RegularExpressions.Regex.Split(line, "\\{[a-z]+\\}").Select(System.Text.RegularExpressions.Regex.Escape)) + "$")));
            _scenes.Close();
            profile.Deck = deckWas;
            _campaign.Save();
            // Junia on her own peak build: a pilot, not the Brawl's out-of-towner (no outoftowner:junia scene, her pilot:intro line).
            int juniaSlot = Array.FindIndex(inv.Slots, e => e != null && e.Character == "junia");
            int oppSlot = inv.Opponent(inv.YourMatch());
            if (juniaSlot >= 0 && oppSlot >= 0)
            {
                (inv.Slots[juniaSlot], inv.Slots[oppSlot]) = (inv.Slots[oppSlot], inv.Slots[juniaSlot]);
                Unsee(BackAlley.OutOfTownerEvent + ":junia");
                _campaign.Save();
                // Every block of the pool (some wait on an `after:` scene), its junia: lines.
                var introPool = new List<string>();
                bool inPool = false;
                foreach (var raw in File.ReadAllLines(Path.Combine(FindDataFolder("campaign", "campaign"), "alley-voice.txt")))
                {
                    var l = raw.Trim();
                    if (l.StartsWith("pool:")) inPool = l == "pool: pilot:intro:junia";
                    else if (inPool && l.StartsWith("junia:")) introPool.Add(l.Substring(6).Trim());
                }
                _bracketLinesFor = "";
                _versusDeck = null;
                _bracketScreen.DebugDuel();
                yield return Until(() => _scenes.IsOpen || _storyScreen.IsOpen, 5);
                yield return new WaitForSecondsRealtime(0.6f);
                bool oot = _storyScreen.IsOpen;
                if (_storyScreen.IsOpen) _storyScreen.DebugFinish();
                yield return Until(() => _scenes.IsOpen, 5);
                yield return Shot("23-versus-junia");
                Report($"23. against Junia on her peak build: no out-of-towner scene ({!oot}), her pilot:intro line (\"{_bracketQuote}\"), not out of town ({OutOfTownerAt(inv, oppSlot) == null}), a pilot ({PilotAt(inv, oppSlot)})",
                    !oot && _scenes.IsOpen && OutOfTownerAt(inv, oppSlot) == null && PilotAt(inv, oppSlot) == "junia" && introPool.Count > 0 && introPool.Contains(_bracketQuote)
                    && _story.Pending(profile, new[] { BackAlley.OutOfTownerEvent + ":junia" }).Count > 0);
                _scenes.Close();
                (inv.Slots[juniaSlot], inv.Slots[oppSlot]) = (inv.Slots[oppSlot], inv.Slots[juniaSlot]);
                _bracketLinesFor = "";
                _campaign.Save();
            }
            else Report("23. Junia is in the Invitational's field", false);

            // ---- 23. Through to the final: the Bo3 pips ----
            while (inv.YourMatch() >= 0 && inv.YourMatch() != inv.Final) Brackets.RecordYours(profile, lib, inv, 0, false, _campaign.Matchups);
            var game1 = Brackets.RecordYours(profile, lib, inv, 0, false, _campaign.Matchups);
            _campaign.Save();
            ShowBracket();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("23-bo3-pips");
            int yourSide = inv.Pair(inv.Final).a == inv.YourSlot ? 0 : 1;
            var pips = _bracketScreen.DebugPips;
            Report($"23. game 1 of the final won (\"{game1?.Reward.Summary}\"): the pips \"{Plain(pips.a)}\" over \"{Plain(pips.b)}\"; the Duel reads game 2",
                game1 != null && game1.NextGame && Plain(yourSide == 0 ? pips.a : pips.b) == "● ○" && Plain(yourSide == 0 ? pips.b : pips.a) == "○ ○"
                && _bracketScreen.DebugText.Contains("Duel: Final, game 2"));

            // ---- 23. The title: 1,500 into nolimits, 2 Card Tokens (once), No Limits Packs revealed, its scenes, the poster ----
            Unsee(Brackets.WinEvent + ":" + spec.Name);
            int earned0 = profile.DpEarned.GetValueOrDefault(Ledger.NoLimits), tokens0 = profile.Tokens;
            var poster = Posters.All.FirstOrDefault(x => x.Id == "invitational-bill");
            bool posterBefore = poster != null && Posters.IsUnlocked(poster, _campaign.Circuits, profile);
            var title = Brackets.RecordYours(profile, lib, inv, 0, false, _campaign.Matchups);
            _campaign.Save();
            ShowBracket();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("23-title");
            var pool = new HashSet<uint>(BackAlley.NoLimitsPackPool(data, ctx));
            // Held back first: no build ready, so the No Limits Pack's pool is empty and can't fill a pack (Brackets.FinishBlocked).
            // Finish pays nothing and keeps the bracket, done; the bracket screen and the chalkboard say why; Finish tries again.
            var readyNow = data.Builds.Where(h => h.Ready).ToList();
            foreach (var h in readyNow) h.Ready = false;
            int heldDp = profile.Dp, heldTokens = profile.Tokens;
            _lastBracketReward = null;
            _bracketScreen.DebugFinish();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("23-held-back");
            var heldText = Plain(_bracketScreen.DebugText);
            bool heldKept = profile.Bracket == inv && inv.Done && profile.Dp == heldDp && profile.Tokens == heldTokens && _lastBracketReward == null
                && Profile.Load(_campaign.SavePath)?.Bracket?.Done == true;
            _bracketScreen.Close();
            _campaignScreen.DebugShowAlley(board);
            yield return new WaitForSecondsRealtime(0.5f);
            var heldBoard = Board();
            var heldDetail = Plain(_campaignScreen.DebugPanelText);
            _campaignScreen.Close();
            Report($"23. with no No Limits Pack to open, Finish holds the title back (nothing paid, the bracket kept, done): the bracket screen says \"{heldText.Split('\n').FirstOrDefault(l => l.Contains("can't fill a pack")) ?? ""}\"; "
                   + $"the chalkboard's button {heldBoard.ElementAtOrDefault(2).label}",
                heldKept && heldText.Contains("can't fill a pack") && heldText.Contains("Finish tries again") && heldBoard.Count == 3 && heldBoard[2] == (kind, "Results ▶", true)
                && heldDetail.Contains("Finished, not paid yet"));
            foreach (var h in readyNow) h.Ready = true;
            ShowBracket();
            yield return new WaitForSecondsRealtime(0.4f);
            _bracketScreen.DebugFinish();
            yield return new WaitForSecondsRealtime(0.8f);
            yield return _shop.DebugRevealPrize();
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("23-no-limits-pack");
            var reward = _lastBracketReward;
            var shown = _shop.DebugPrize();
            var prizeLine = Plain(_shop.DebugPrizeLine.Replace('\n', ' '));
            bool PackOk(List<PrizePull> pack) => pack.Count == ContrabandPacks.Size && pack.Count(x => x.Slab > 0) == 1 && pack[pack.Count - 1].Slab >= 9
                && pack.All(x => pool.Contains(Banlist.BaseCode(x.Code, _cardPack!)) || pool.Contains(x.Code))
                && pack.All(x => x.Copy == null || (x.Copy.Paid == 0 && x.Copy.Source == CopySource.Prize));
            Report($"23. the title's Finish (\"{reward?.Summary}\"): +{profile.DpEarned.GetValueOrDefault(Ledger.NoLimits) - earned0:N0} DP into nolimits, +{profile.Tokens - tokens0} Card Tokens, "
                   + $"{reward?.Packs.Count} No Limits Packs from the {pool.Count}-card pool, revealed (\"{prizeLine}\"; {shown.cards} cards, {shown.left} more); events {string.Join(", ", reward?.Events ?? new List<string>())}",
                title != null && reward != null && reward.Placement == 0 && profile.DpEarned.GetValueOrDefault(Ledger.NoLimits) - earned0 == AlleyEconomy.InvitationalPay[0] && AlleyEconomy.InvitationalPay[0] == 1500
                && profile.Tokens - tokens0 == AlleyEconomy.InvitationalTokens && AlleyEconomy.InvitationalTokens == 2 && reward.Tokens == 2 && profile.Seen.Contains(Brackets.InvitationalTokenKey)
                && reward.Packs.Count == AlleyEconomy.InvitationalPacks[0] && pool.Count > 0 && reward.Packs.All(PackOk) && shown.open && shown.cards == ContrabandPacks.Size
                && shown.left == AlleyEconomy.InvitationalPacks[0] - 1 && prizeLine.StartsWith($"No Limits Pack 1 of {AlleyEconomy.InvitationalPacks[0]}") && reward.Summary.Contains("No Limits Pack")
                && reward.Events.Contains("bracketwin:" + spec.Name) && Brackets.AlleyRecord(profile, kind).Titles >= 1);
            _shop.DebugFinishPrizes();
            yield return Until(() => _storyScreen.IsOpen || (_campaignScreen.IsOpen && !_shop.IsOpen), 5);
            yield return new WaitForSecondsRealtime(0.4f);
            bool winScene = _storyScreen.IsOpen;
            if (_storyScreen.IsOpen) yield return Shot("23-invitational-win");
            for (int k = 0; k < 4 && _storyScreen.IsOpen; k++) { _storyScreen.DebugFinish(); yield return new WaitForSecondsRealtime(0.3f); }
            winScene &= profile.Seen.Contains("nl-invitational-win") && profile.Seen.Contains("nl-invitational-home");
            yield return Until(() => _campaignScreen.IsOpen, 5);
            yield return new WaitForSecondsRealtime(0.5f);
            bool posterAfter = poster != null && Posters.IsUnlocked(poster, _campaign.Circuits, profile);
            Report($"23. its scenes play ({winScene}), then the chalkboard; the invitational-bill poster is earned (before {posterBefore}, after {posterAfter})",
                winScene && posterAfter && !posterBefore && _campaignScreen.IsOpen && _campaignScreen.InAlley && _story.Pending(profile, new[] { "bracketwin:" + spec.Name }).Count == 0);
            _campaignScreen.Close();
            // A second title pays the DP and packs again, never the tokens.
            earned0 = profile.DpEarned.GetValueOrDefault(Ledger.NoLimits);
            tokens0 = profile.Tokens;
            var again = Brackets.EnterAlley(profile, data, kind, ctx, profile.Deck);
            while (again != null && again.YourMatch() >= 0) Brackets.RecordYours(profile, lib, again, 0, false, _campaign.Matchups);
            var second = again != null ? Brackets.Finish(profile, lib, again, null, null, noLimits: _campaign.NoLimitsPacks()) : null;
            _campaign.Save();
            Report($"23. a second title (\"{second?.Summary}\"): +{profile.DpEarned.GetValueOrDefault(Ledger.NoLimits) - earned0:N0} DP, {second?.Packs.Count} packs, +{profile.Tokens - tokens0} Card Tokens (once only)",
                second != null && second.Placement == 0 && profile.DpEarned.GetValueOrDefault(Ledger.NoLimits) - earned0 == AlleyEconomy.InvitationalPay[0]
                && second.Packs.Count == AlleyEconomy.InvitationalPacks[0] && second.Tokens == 0 && profile.Tokens == tokens0 && profile.Bracket == null);
            // 23b. Held back, settled in the session (TODO 2026-11-06): with no build ready the bracket screen offers "Settle now",
            // which pays as a launch's settle does (the DP and record now, the No Limits Packs owed), then the chalkboard says so;
            // once the pool can fill a pack again, the owed packs open (Brackets.PayOwedPacks, as at the next load).
            {
                earned0 = profile.DpEarned.GetValueOrDefault(Ledger.NoLimits);
                var third = Brackets.EnterAlley(profile, data, kind, ctx, profile.Deck);
                while (third != null && third.YourMatch() >= 0) Brackets.RecordYours(profile, lib, third, 0, false, _campaign.Matchups);
                var readyAgain = data.Builds.Where(h => h.Ready).ToList();
                foreach (var h in readyAgain) h.Ready = false;
                _campaign.Save();
                ShowBracket();
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("23b-settle-offered");
                var offerText = Plain(_bracketScreen.DebugText);
                bool pressed = _bracketScreen.DebugSettle();
                yield return Until(() => _storyScreen.IsOpen || _campaignScreen.IsOpen, 5);
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot("23b-settled");
                var notice = Plain(_campaignScreen.DebugStatus);
                for (int k = 0; k < 4 && _storyScreen.IsOpen; k++) { _storyScreen.DebugFinish(); yield return new WaitForSecondsRealtime(0.3f); }
                int owedNow = profile.NoLimitsPacksOwed.Count;
                var saved = Profile.Load(_campaign.SavePath);
                Report($"23b. held back, \"{BracketScreen.SettleLabel}\" is offered ({third != null && offerText.Contains("Settle now")}) and settles in the session: "
                       + $"+{profile.DpEarned.GetValueOrDefault(Ledger.NoLimits) - earned0:N0} DP, {owedNow} No Limits Packs owed, the bracket closed, saved ({saved?.NoLimitsPacksOwed.Count} owed in the save); the chalkboard: \"{notice}\"",
                    third != null && pressed && offerText.Contains("Settle now") && profile.Bracket == null && owedNow == AlleyEconomy.InvitationalPacks[0]
                    && profile.DpEarned.GetValueOrDefault(Ledger.NoLimits) - earned0 == AlleyEconomy.InvitationalPay[0]
                    && saved != null && saved.Bracket == null && saved.NoLimitsPacksOwed.Count == owedNow && _lastBracketReward?.Placement == 0);
                foreach (var h in readyAgain) h.Ready = true;
                var paidOwed = Brackets.PayOwedPacks(profile, _campaign.NoLimitsPacks());
                _campaign.Save();
                Report($"23b. once a build is ready again the owed packs open ({paidOwed.Count} packs, {paidOwed.Sum(k => k.Count)} cards; {profile.NoLimitsPacksOwed.Count} still owed)",
                    paidOwed.Count == owedNow && profile.NoLimitsPacksOwed.Count == 0);
                _campaignScreen.Close();
            }
            // Put back what step 23 changed: the field's readiness, the record, the token, the table's clear.
            foreach (var back in fieldUndo) back();
            if (hadRecord && recordWas != null) profile.AlleyBrackets[kind] = recordWas; else profile.AlleyBrackets.Remove(kind);
            if (!tokenKeyWas) profile.Seen.Remove(Brackets.InvitationalTokenKey);
            profile.Tokens = tokensWas;
            nlState.Cleared = clearedWas;
            profile.Deck = deckWas;
            _campaign.Save();

            // ---- 24. Moth's shelf: the restored slots, tagged, once the wall is open (wave 1 forced ready) ----
            var readyStakes = new HashSet<uint>(data.Builds.Where(h => h.Ready).SelectMany(h => h.Stakes));
            profile.Seen.Remove(BackAlley.WallEvent);
            var shut = _campaign.VanShelf();
            profile.Seen.Add(BackAlley.WallEvent);
            var shelf = _campaign.VanShelf();
            var restored = shelf.Where(l => l.Restored).ToList();
            _van.DebugShow(0);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("24-van-restored");
            int tags = _van.DebugRestoredTags;
            var listText = _van.DebugListText();
            Report($"24. with the wall open the shelf gives {restored.Count} of its {shelf.Count} slots to restored cards ({string.Join(", ", restored.Select(l => $"{_cardPack!.Get(l.Copy.Code)?.Name} {Rarities.Name(l.Copy.Rarity)} {l.Copy.Slab} at {l.Price:N0}"))}), "
                   + $"{tags} tagged {VanScreen.RestoredTag}; with it shut, none ({shut.Count(l => l.Restored)}) and the same first {shelf.Count - restored.Count} slabs",
                shelf.Count == AlleyEconomy.ShelfSize && restored.Count == AlleyEconomy.ShelfRestoredSlots && tags == restored.Count && listText.Contains(VanScreen.RestoredTag)
                && restored.All(l => readyStakes.Contains(l.Copy.Code) && l.Copy.IsSlab && l.Copy.Slab >= 9 && l.Copy.Rarity >= AlleyEconomy.ShelfRestoredMinRarity
                    && profile.Collection.Value(l.Copy) >= AlleyEconomy.ShelfRestoredMinValue && l.Price == AlleyEconomy.ShelfPrice(profile.Collection.Value(l.Copy))
                    && 6 * Math.Min(profile.Collection.Value(l.Copy) / 4, l.Price / 2) <= l.Price)
                && shelf.Select(l => l.Copy.Code).Distinct().Count() == shelf.Count && shut.All(l => !l.Restored) && shut.Count == AlleyEconomy.ShelfSize
                && shelf.Where(l => !l.Restored).Take(shelf.Count - restored.Count).Select(l => MothsVan.ShelfKey(l.Copy.Code, l.Copy.Rarity, l.Copy.Grade))
                    .SequenceEqual(shut.Take(shelf.Count - restored.Count).Select(l => MothsVan.ShelfKey(l.Copy.Code, l.Copy.Rarity, l.Copy.Grade))));
            if (restored.Count > 0)
            {
                _van.DebugPickShelf(restored[0].Slot);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("24-van-restored-detail");
                var detail = _van.DebugDetailText();
                Report($"24. a restored slot's detail says so (\"{detail.Split('\n').FirstOrDefault(l => l.Contains(VanScreen.RestoredTag))}\")", detail.Contains(VanScreen.RestoredTag) && detail.Contains("off the depot"));
            }
            _van.Close();
            yield return new WaitForSecondsRealtime(0.3f);
            _campaignScreen.Close();
            if (!wallWas) profile.Seen.Remove(BackAlley.WallEvent);
            foreach (var back in undo) back();
            _campaign.Save();
        }
    }
}
