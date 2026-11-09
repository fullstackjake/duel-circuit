using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The Champion's Ladder's duels (docs/dev/CHAMPIONS-LADDER-SPEC-2026-11-06.md section 4; the board is CampaignScreen.Ladder):
    /// the guest flow as the template. Duel on a row: its "ladderseat:" scene the first time, then the versus (the last
    /// circuit's rules, your own decks only, the holder at skill 1.0 on the deck the rung or the sitting has reached), the
    /// coin toss, the duel live in the save, the result recorded (<see cref="Ladder.Record"/>) and its results and scenes.
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>The ladder seat being dueled (null for any other duel). Set by NewDuel.</summary>
        private LadderSeat? _ladderDuel;

        /// <summary>The ladder's Stuck? and loss-streak key: the one <see cref="Ladder.Record"/> notes the result under.</summary>
        private static string LadderStreakKey(LadderSeat seat) => Ladder.LedgerKind + ":" + seat.Key;

        /// <summary>A seat's music: a boss theme of their own (Ilse's), else the Gauntlet's for the top, else the everyday.</summary>
        private static (string theme, int transpose) LadderMusic(Ladder ladder, LadderSeat seat) =>
            Ygo.Engine.Audio.Sfx.Themes.ContainsKey(seat.Character.Id) ? (seat.Character.Id, 0)
            : !seat.IsVisitor && seat.Rung == ladder.Rungs.Count && Ygo.Engine.Audio.Sfx.Themes.ContainsKey("gauntlet") ? ("gauntlet", 3)
            : (BossMusic.Everyday, 0);

        /// <summary>The versus's heading for a seat: its rung (or the visitor's), and the sitting's deck where there is one.</summary>
        private string LadderHeading(Ladder ladder, LadderSeat seat, LadderState state)
        {
            if (seat.IsVisitor) return Ladder.Beaten(state, seat) ? $"Rematch · {seat.Character.Name}, a visitor" : $"Visiting · above rung {ladder.Climbed(state)}";
            if (Ladder.Beaten(state, seat)) return $"Rematch · Rung {seat.Rung} of {ladder.Rungs.Count}";
            if (!seat.IsSitting) return $"Rung {seat.Rung} of {ladder.Rungs.Count}";
            var (won, of) = ladder.SittingProgress(seat, state);
            return $"Rung {seat.Rung} of {ladder.Rungs.Count} · {DeckTitle(ladder.DeckFor(seat, state))} ({won + 1} of {of})";
        }

        /// <summary>Before a ladder duel: your side against the seat's holder (their first meeting's scene before, once).</summary>
        private void ShowLadderVersus(string key, bool cycled = false)
        {
            var profile = _campaign.Profile;
            if (profile == null) return;
            var ladder = _campaign.Ladder;
            var state = profile.Ladder;
            var seat = ladder.SeatByKey(key);
            if (seat == null || !ladder.CanChallenge(profile, state, _campaign.Library, seat))
            {
                _campaignScreen.OpenLadder($"<color=#FF8A8A>That rung isn't yours to challenge now.</color>");
                return;
            }
            // Their entrance (ladderseat:<id>, Ilse's "all four, one sitting", the top's holder, a visitor): before the versus, once.
            var seatEvents = Ladder.SeatEvents(state, seat);
            if (!cycled && seatEvents.Count > 0 && _story.Pending(profile, seatEvents).Count > 0)
            {
                PlayStory(seatEvents, "", () => ShowLadderVersus(key));
                return;
            }
            int circuit = _campaign.LadderRulesCircuit;
            var rules = _campaign.LadderRules;
            var rulesName = _campaign.Circuits.All.Count > circuit ? _campaign.Circuits.All[circuit].Name : "";
            var theirDeckName = ladder.DeckFor(seat, state);
            var opponent = Ladder.Opponent(seat);
            // Your own decks only (no loaners, no partner's edit): the arrows go round those, starting on one that's legal.
            List<DeckProblem> Problems(string d) => _campaign.LadderProblems(d);
            var choices = PickVersusDeck(cycled).Where(_campaign.IsYours).ToList();
            if (choices.Count == 0) choices.Add(profile.Deck);
            if (!cycled && (!choices.Contains(_versusDeck!) || Problems(_versusDeck!).Count > 0))
                _versusDeck = choices.FirstOrDefault(d => Problems(d).Count == 0) ?? (choices.Contains(profile.Deck) ? profile.Deck : choices[0]);
            var mine = _library.Contains(_versusDeck!) ? _library.Load(_versusDeck!) : new Deck();
            var theirDeck = _library.Contains(theirDeckName) ? _library.Load(theirDeckName) : new Deck();
            var problems = Problems(_versusDeck!);
            int wins = profile.WinsAgainst(seat.Character.Id), losses = profile.Losses.TryGetValue(seat.Character.Id, out var l) ? l : 0;
            var you = new CampaignScenes.Side
            {
                Name = "You", Deck = VersusDeckName(_versusDeck!), DeckNote = problems.Count > 0 ? "(not legal here)" : "",
                Showcase = CampaignScenes.ShowcaseOf(mine, _cardPack!).Take(3).ToList(),
                Detail = $"Champion's Ladder. Rules: {rulesName}" + (rules.List != null ? $" ({rules.List.Name})" : "") + ".\nYour own decks only: no loaners."
                         + (problems.Count > 0 ? $"\n<color=#E3A21A>{VersusDeckName(_versusDeck!)} can't climb: {problems[0].Message}</color>" : ""),
            };
            var (won, of) = ladder.SittingProgress(seat, state);
            var them = new CampaignScenes.Side
            {
                Name = seat.Character.Name, Title = seat.Character.Title, Deck = DeckTitle(theirDeckName),
                DeckNote = seat.IsSitting && !Ladder.Beaten(state, seat) ? $"({won + 1} of {of})" : "",
                Showcase = CampaignScenes.ShowcaseOf(theirDeck, _cardPack!).Take(3).ToList(),
                Portraits = new List<string> { seat.Character.Id }, Quote = seat.Character.IntroFor(wins + losses > 0),
                Detail = opponent.StyleSummary + (wins + losses > 0 ? $"\nYour record against them: {wins}-{losses}" : "\nFirst meeting")
                         + (seat.IsSitting && !Ladder.Beaten(state, seat) ? "\nOne sitting: a loss or a draw starts it again at the first deck." : ""),
            };
            BossSting(LadderMusic(ladder, seat), cycled);
            _scenes.ShowVersus("The Champion's Ladder", LadderHeading(ladder, seat, state), you, them,
                start: () =>
                {
                    // A loaner, a partner's edit or a deck that isn't legal: refused with its first problem (as an Every Era round).
                    if (Problems(_versusDeck ?? profile.Deck) is { Count: > 0 } refused)
                    {
                        _ownedNote = $"<color=#FF8A8A>{VersusDeckName(_versusDeck ?? profile.Deck)} can't climb:</color> <color=#F0E6D8>{refused[0].Message}</color>";
                        ShowLadderVersus(key, cycled: true);
                        return;
                    }
                    seed++;
                    BeginLiveDuel(LiveDuels.Ladder, guest: seat.Key);
                    CoinToss("You", seat.Character.Name, _versusDeck ?? profile.Deck, theirDeckName, () => StartLadderDuel(key));
                },
                back: () => _campaignScreen.OpenLadder(),
                cycleDeck: delta => { CycleVersusDeck(choices, delta); ShowLadderVersus(key, cycled: true); },
                scouts: ScoutsFor((seat.Character.Name, theirDeckName, seat.Character.Id)),
                stuck: Stuck(LadderStreakKey(seat), Array.Empty<MeasuredOption>(), false, new[] { seat.Character.Id }, false, theirDeckName),
                quiet: cycled, note: TakeOwnedNote(), arrowsHint: "Arrows: switch between your own decks (no loaners on the ladder).",
                texts: _campaign.TextsLine(mine, rules));
        }

        private void StartLadderDuel(string key)
        {
            var profile = _campaign.Profile!;
            var seat = _campaign.Ladder.SeatByKey(key);
            // The board's Rematch, or a deck changed since the versus: back to the versus, which checks again.
            if (seat == null || _campaign.LadderProblems(_versusDeck ?? profile.Deck).Count > 0
                || !_campaign.Ladder.CanChallenge(profile, profile.Ladder, _campaign.Library, seat))
            {
                ShowLadderVersus(key, cycled: true);
                return;
            }
            _loanerCircuit = _campaign.LadderRulesCircuit;
            var deck = _campaign.Ladder.DeckFor(seat, profile.Ladder);
            _pickCharacter[0] = -1;
            _pickDeck[0] = TakeVersusDeck();
            _pickCharacter[1] = Math.Max(0, _roster.FindIndex(p => p.Id == seat.Character.Id));
            _pickDeck[1] = _allDecks.Contains(deck) ? deck : OwnDecks(seat.Character)[0];
            ShowPicks();
            _paused = false;
            NewDuel(ladderSeat: seat);
            UpdateControls();
        }

        /// <summary>For checks: the last ladder result recorded at a duel's end (FinishLadderDuel), and the results' second button's label.</summary>
        private LadderResult? _lastLadderResult;
        private string LadderAgainLabel = "";

        /// <summary>At the end of a ladder duel: records it and shows the results. False when this isn't one.</summary>
        private bool FinishLadderDuel()
        {
            if (_ladderDuel is not LadderSeat seat || _watching != null || _campaign.Profile == null) return false;
            int winner = _board.Winner > 1 ? 2 : _board.Winner;
            var result = _campaign.RecordLadder(seat, winner);
            _lastLadderResult = result;
            _campaign.EndLiveDuel();
            if (result == null)
            {
                // A seat that couldn't be challenged any more (it shouldn't happen: the versus checks): nothing recorded.
                _campaign.Save();
                _view.SetResult(winner == 0 ? "You win!" : winner == 1 ? $"{seat.Character.Name} wins!" : "Draw!", "", "Nothing recorded: that rung wasn't yours to challenge.");
                return true;
            }
            var reward = result.Reward;
            _campaign.AfterDuel(reward, "", _tally, false, false);
            _campaign.Save();
            var line = winner == 0 ? seat.Character.LoseLine : winner == 1 ? seat.Character.WinLine : "";
            if (_runner?.Result.Reason == DuelRunner.ReasonYataLock && winner is 0 or 1) line = YataLockLine(seat.Character.Name, "");
            _view.SetResult(winner == 2 ? "Draw!" : winner == 0 ? "You win!" : $"{seat.Character.Name} wins!", line, $"{reward.Summary}   ·   {_campaign.Profile.Dp:N0} DP");
            _view.AppendLog($"Campaign: {reward.Summary}.");
            BeginLadderResults(result, winner);
            return true;
        }

        /// <summary>After a ladder duel (recorded, its card unlocks paid): its scenes queued, then the results in 2 s (DuelController.Results).</summary>
        private void BeginLadderResults(LadderResult result, int winner)
        {
            var reward = result.Reward;
            // The save's first loss (Gus's), the card unlocks' scenes, then the ladder's own (rung, holder, the top), in that order.
            var events = Story.LossEvents(_campaign.Profile!, winner);
            AddGrantEvents(events, reward);
            events.AddRange(result.Events.Where(e => !events.Contains(e)));
            ShowResultsSoon(events, reward.Grants, story => ShowLadderResults(result, winner, story));
        }

        private void ShowLadderResults(LadderResult result, int winner, List<CampaignState.QueuedStory> story)
        {
            var profile = _campaign.Profile!;
            var ladder = _campaign.Ladder;
            var seat = result.Seat;
            var reward = result.Reward;
            var state = profile.Ladder;
            var r = new CampaignScenes.Results
            {
                Won = winner == 0,
                Headline = winner > 1 ? "Draw" : winner == 0 ? (result.TopReached ? "The top!" : "Victory!") : "Defeat",
                Speaker = seat.Character.Name,
                Quote = winner == 0 ? seat.Character.LoseLine : winner == 1 ? seat.Character.WinLine : "",
                Circuit = $"The Champion's Ladder · {(seat.IsVisitor ? $"{seat.Character.DisplayName}, visiting" : $"Rung {seat.Rung}, {seat.Character.DisplayName}")}",
                TotalDp = profile.Dp,
                Progress = ladder.Rungs.Select(s => Ladder.Beaten(state, s)).ToList(),
                ProgressItem = "Rung", ProgressTitle = "THE LADDER",
            };
            if (winner == 0)
            {
                int duelDp = reward.Dp - (result.TopReached ? ladder.Prize : 0);
                r.Earnings.Add((result.RungTaken > 0 ? $"Rung {result.RungTaken}" : result.VisitorBeaten ? $"First win against {seat.Character.Name}"
                    : result.SittingWin ? $"Sitting: {result.SittingAfter} of {seat.Decks.Count}" : "Rematch", duelDp));
                if (result.TopReached) r.Earnings.Add(("The top: prize", ladder.Prize));
            }
            else r.Earnings.Add((winner == 1 ? "For the duel" : "Draw", reward.Dp));
            foreach (var up in reward.LevelUps.Where(u => u.Dp > 0)) r.Earnings.Add(($"Level {up.Level} reward", up.Dp));
            var (into, needed) = Leveling.Progress(profile.Exp);
            r.Exp = (reward.Exp, profile.Level, into, needed);
            int wins = profile.WinsAgainst(seat.Character.Id), losses = profile.Losses.TryGetValue(seat.Character.Id, out var l) ? l : 0;
            r.Record = $"Against {seat.Character.Name}: <b>{wins}</b> win{(wins == 1 ? "" : "s")}, <b>{losses}</b> loss{(losses == 1 ? "" : "es")}\n"
                       + (ladder.HoldsTop(state) ? "The ladder: Top: You" : $"The ladder: rung {ladder.Climbed(state)} of {ladder.Rungs.Count}");
            foreach (var up in reward.LevelUps)
                r.Unlocks.Add($"Level {up.Level}!" + (up.Tokens > 0 ? $" +{up.Tokens} Card Token{(up.Tokens > 1 ? "s" : "")}" : ""));
            if (result.TopReached)
            {
                r.Unlocks.Add($"The top of the Champion's Ladder! +{Ladder.TopTokens} Card Tokens: trade for any card at the Card Trader");
                if (Posters.Find(Ladder.PosterId) is Poster poster) r.Unlocks.Add($"New poster: {poster.Title}. Hang it on your plate in the Locker");
            }
            else if (result.RungTaken > 0 && ladder.Next(profile, state, _campaign.Library) is LadderSeat next)
                r.Unlocks.Add(next.IsVisitor ? $"{next.Character.Name} is visiting: beat them to go on" : $"Next: rung {next.Rung}, {next.Character.Name}");
            else if (result.VisitorBeaten && ladder.Next(profile, state, _campaign.Library) is LadderSeat after)
                r.Unlocks.Add(after.IsVisitor ? $"Next visitor: {after.Character.Name}" : $"Back to the climb: rung {after.Rung}, {after.Character.Name}");
            else if (result.SittingWin) r.Unlocks.Add($"Next in the sitting: {DeckTitle(ladder.DeckFor(seat, state))} ({result.SittingAfter + 1} of {seat.Decks.Count})");
            else if (result.SittingReset) r.Unlocks.Add($"The sitting starts again at {DeckTitle(ladder.DeckFor(seat, state))}. Your rung stays yours");
            else if (winner == 1) r.Unlocks.Add("A loss keeps your rung: try again when you're ready");
            else if (winner == 2) r.Unlocks.Add("A draw keeps your rung: try again when you're ready");
            AddGrants(r, reward);
            _scenes.LastResults = r;
            var key = seat.Key;
            // Rematch: the same seat when it can be played again (a loss, a sitting, a rematch after the top), else the next one.
            // Labelled for what it opens: "Rematch" (the same seat), "Next rung" or "Next visitor" (after a first climb or a visitor
            // beaten), "The ladder" (nobody next: the board).
            var nextSeat = ladder.Next(profile, state, _campaign.Library);
            bool same = ladder.CanChallenge(profile, state, _campaign.Library, seat);
            var againKey = same ? key : nextSeat?.Key;
            var againLabel = same ? "Rematch" : nextSeat == null ? "The ladder" : nextSeat.IsVisitor ? "Next visitor" : "Next rung";
            LadderAgainLabel = againLabel;
            Action cont = () => PlayResultStory(story, () => _campaignScreen.OpenLadder(reward.Summary));
            Action again = Requeued(story, () => { if (againKey != null) ShowLadderVersus(againKey); else _campaignScreen.OpenLadder(); });
            Action shop = Requeued(story, () => _shop.Open());
            _scenes.ShowResults(r, cont, again, shop, new List<(string, Action)> { ("Continue", cont), (againLabel, again), ("Card Shop", shop) });
        }
    }
}
