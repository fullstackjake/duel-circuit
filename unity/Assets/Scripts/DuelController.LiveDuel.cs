using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Your campaign duel, live in the save (CampaignState.LiveDuel.cs): Duel! at a circuit, Tag Cup, guest, Library cup,
    /// "Every Era" round or Locals versus screen writes it before the coin toss (as the alley holds its stakes there), and
    /// NewDuel writes it for a duel started any other way; its result clears it in the same save. The app closed between the
    /// two (the menu's Quit, Cmd-Q, a crash) leaves it there, and the next launch settles it as a loss. The alley's tables and
    /// brackets keep their own record (DuelController.Alley, .Brackets); Free Duel, lessons, exhibitions, the combo clips,
    /// a watched final and replays have none.
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>Duel! at a campaign versus screen: the duel goes live in the save now, before the toss (your deck from the arrows).</summary>
        private void BeginLiveDuel(string kind, int ev = -1, int match = -1, string guest = "", bool tag = false, string partnerDeck = "")
        {
            if (_campaign.Profile is not Profile profile) return;
            var deck = _versusDeck ?? profile.Deck;
            _campaign.BeginLiveDuel(new LiveDuel
            {
                Kind = kind, Event = ev, Match = match, Guest = guest, Tag = tag,
                Loaner = _campaign.IsLoanerDeck(deck) || (partnerDeck.Length > 0 && _campaign.IsLoanerDeck(partnerDeck)),
            });
        }

        /// <summary>
        /// The duel NewDuel is starting as a live duel of yours (its fields are set, your deck in <see cref="_pickDeck"/> or
        /// the tag lineup), or null when it isn't one: Free Duel, a lesson, a show, the alley's, a watched final.
        /// </summary>
        private LiveDuel? LiveDuelNow(TagLineup? tag)
        {
            bool loaner = TeamHasLoaner(tag, _pickDeck[0]);
            if (_campaignMatch is { } cm) return new LiveDuel { Kind = LiveDuels.Circuit, Event = cm.circuit, Match = cm.match, Loaner = loaner };
            if (_campaignTag is { } ct) return new LiveDuel { Kind = LiveDuels.TagCup, Event = ct.cup, Match = ct.match, Loaner = loaner };
            if (_campaignCup is { } lc) return new LiveDuel { Kind = LiveDuels.Cup, Event = lc.cup, Match = lc.match, Loaner = loaner };
            if (_campaignRound is int r) return new LiveDuel { Kind = LiveDuels.Round, Event = r, Loaner = loaner };
            if (_guestDuel is { } g) return new LiveDuel { Kind = LiveDuels.Guest, Guest = g.guest.Character.Id, Tag = g.tag, Loaner = loaner };
            if (_ladderDuel is LadderSeat ls) return new LiveDuel { Kind = LiveDuels.Ladder, Guest = ls.Key, Loaner = loaner };
            if (_bracketMatch is int m && !_bracketWatch && _campaign.Profile?.Bracket is Bracket b && !Brackets.IsAlley(b))
                return new LiveDuel { Kind = LiveDuels.Locals, Match = m, Loaner = loaner };
            return null;
        }

        // ---- -campaigncheck ------------------------------------------------------------------------------------------------

        /// <summary>
        /// The app closed mid-duel, at its end: a circuit match's Duel! puts it live in the save before the coin toss; the menu
        /// over it says Quit is a loss and leaves the record alone; then the app goes (the duel dropped, the save read again
        /// from disk as a relaunch reads it): the match loads as a loss with the notice, once. Then a Locals game with a
        /// loaner, the same way: lost in the bracket at half its placement, not replayed. The save isn't put back (Start over
        /// follows).
        /// </summary>
        private System.Collections.IEnumerator LiveDuelCampaignCheck(Action<string, bool> report, Func<string, System.Collections.IEnumerator> shot)
        {
            System.Collections.IEnumerator Until(Func<bool> done, float seconds)
            {
                for (float until = Time.realtimeSinceStartup + seconds; !done() && Time.realtimeSinceStartup < until;) yield return null;
            }
            string Status() => Regex.Replace(_campaignScreen.StatusText, "<.*?>", "");
            bool YourTurnWaits() => _runner != null && _runner.IsWaiting && _runner.WaitingFor == 0 && _human?.Current != null;
            // The app gone: whatever duel was on screen is dropped (a bot duel takes its place), and the save is read again.
            void Relaunch()
            {
                seed++;
                PickRandomBots();
                NewDuel();
                _paused = true;
                UpdateControls();
                _campaign.DebugReload();
                // The scenes the loss raised (a level reached) are the story check's: seen, so they don't close the screen under test.
                if (_campaign.Profile is Profile reloaded)
                    foreach (var scene in _story.Pending(reloaded, _campaign.AlleyNoticeEvents.ToArray())) Story.MarkSeen(reloaded, scene);
            }
            var profile = _campaign.Profile!;
            var circuits = _campaign.Circuits;
            var key = Profile.MatchKey(0, 1);
            var opp = circuits.All[0].Matches[1].Character;
            _campaignScreen.Close();

            // A circuit match: Duel! writes it live before the coin is tossed (as the alley holds its stakes).
            _autoToss = false;
            ShowVersus(0, 1);
            yield return new WaitForSecondsRealtime(0.4f);
            if (_scenes.Buttons.TryGetValue("Duel!", out var duel)) duel();
            yield return Until(() => _scenes.Buttons.ContainsKey("Go first") || _scenes.Buttons.ContainsKey("Continue"), 4);
            var atToss = Profile.Load(_campaign.SavePath)?.LiveDuel;
            report($"a circuit match's Duel! puts it live in the save before the coin toss (liveduel {atToss?.Kind} {atToss?.Event} {atToss?.Match})",
                atToss is { Kind: LiveDuels.Circuit, Event: 0, Match: 1 });
            if (_scenes.Buttons.TryGetValue("Go first", out var first)) first();
            else if (_scenes.Buttons.TryGetValue("Continue", out var cont)) cont();
            _autoToss = true;
            yield return Until(YourTurnWaits, 30);
            // The menu over it: Quit says it's a loss, and opening the menu records nothing (the next launch settles it).
            OpenMenuFromDuel();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return shot("live-menu");
            bool menuLeaves = _menu.IsOpen && _menu.QuitLoses() && Profile.Load(_campaign.SavePath)?.LiveDuel is { Kind: LiveDuels.Circuit, Match: 1 }
                              && _menu.DebugItems.Any(i => i.choice == MainMenu.Choice.Quit && !i.locked);
            ResumeFromMenu();
            report("the menu over it offers Quit as a loss and leaves the record to the next launch (nothing recorded now)",
                menuLeaves && _campaignMatch is { circuit: 0, match: 1 } && !_runner!.IsFinished);
            // Quit, Cmd-Q or a crash: the save as it is on disk is what the next launch reads.
            var before = Profile.Load(_campaign.SavePath)!;
            int dp0 = before.Dp, net0 = Ledger.Net(before), losses0 = before.Losses.GetValueOrDefault(opp.Id), played0 = before.DuelsPlayed;
            int run0 = before.LossesInARow(key), lossDp0 = before.DpEarned.GetValueOrDefault(Ledger.Loss(Ledger.Circuit)), exp0 = before.Exp;
            Relaunch();
            profile = _campaign.Profile!;
            _campaignScreen.Open();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return shot("live-notice");
            var notice = Status();
            report($"relaunched, the circuit match is a loss with the notice (\"{notice}\"): +{profile.Dp - dp0} DP, the loss and its run counted, EXP {exp0} -> {profile.Exp}",
                profile.LiveDuel == null && Profile.Load(_campaign.SavePath)?.LiveDuel == null
                && profile.Losses.GetValueOrDefault(opp.Id) == losses0 + 1 && profile.DuelsPlayed == played0 + 1 && profile.LossesInARow(key) == run0 + 1
                && profile.DpEarned.GetValueOrDefault(Ledger.Loss(Ledger.Circuit)) - lossDp0 == Circuits.LossDp && profile.Dp - dp0 == Ledger.Net(profile) - net0
                && profile.Exp > exp0 && notice.Contains($"You left your match against {opp.Name} mid-duel. That counts as a loss."));
            _campaignScreen.Close();
            int lossesOnce = profile.Losses.GetValueOrDefault(opp.Id), dpOnce = profile.Dp;
            _campaign.DebugReload();
            profile = _campaign.Profile!;
            report("launched again, nothing more: the loss was counted once", profile.Losses.GetValueOrDefault(opp.Id) == lossesOnce && profile.Dp == dpOnce
                && _campaign.AlleyNotice.Length == 0);

            // A Locals game with a loaner: lost in the bracket (half its placement), never replayed.
            var lib = _campaign.Library;
            const int cup = 0;
            for (int m = 0; m < lib.Cups[cup].Matches.Count; m++) profile.LibraryWins[lib.Key(cup, m)] = 1;
            profile.LibraryOpen = true;
            profile.Bracket = null;
            foreach (var scene in _story.Pending(profile, new[] { Brackets.EntryEvent })) Story.MarkSeen(profile, scene);
            profile.Earn(Ledger.Debug, Brackets.LocalsEntry);
            var b = Brackets.EnterLocals(profile, lib, cup, _campaign.Ratings, profile.Deck);
            _campaign.Save();
            _versusDeck = Loaners.NameFor(lib.Cups[cup].Matches[0].Deck);
            ShowBracket();
            yield return new WaitForSecondsRealtime(0.4f);
            _bracketScreen.DebugDuel();
            yield return Until(() => _scenes.IsOpen && _scenes.Buttons.ContainsKey("Duel!"), 5);
            if (_scenes.Buttons.TryGetValue("Duel!", out var localsDuel)) localsDuel();
            yield return Until(() => YourTurnWaits() && _bracketMatch != null, 30);
            var live = Profile.Load(_campaign.SavePath);
            int match = b?.YourMatch() ?? -1;
            var them = b == null ? "" : _roster.FirstOrDefault(p => p.Id == b.Slots[b.Opponent(match)].Character)?.Name ?? "";
            report($"a Locals game with a loaner is live in the save while it's played (liveduel {live?.LiveDuel?.Kind} {live?.LiveDuel?.Match}, loaner {live?.LiveDuel?.Loaner})",
                b != null && match >= 0 && _bracketMatch == match && live?.LiveDuel is { Kind: LiveDuels.Locals, Loaner: true } ld && ld.Match == match
                && live.Bracket?.LiveMatch == match);
            int localsPay0 = profile.DpEarned.GetValueOrDefault(Ledger.Locals);
            Relaunch();
            profile = _campaign.Profile!;
            _campaignScreen.Open();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return shot("live-locals-notice");
            var lb = profile.Bracket;
            notice = Status();
            report($"relaunched, the Locals game is lost in the bracket (\"{notice}\"): out in the quarter-finals at half pay (+{profile.DpEarned.GetValueOrDefault(Ledger.Locals) - localsPay0} DP), not replayed",
                lb != null && lb.YouOut && lb.LiveMatch == -1 && lb.Loaned && lb.Placement() == 3 && profile.LiveDuel == null
                && profile.DpEarned.GetValueOrDefault(Ledger.Locals) - localsPay0 == Brackets.PlacementDp(lb, 3) && Brackets.PlacementDp(lb, 3) < Brackets.LocalsPay[3]
                && notice.Contains($"You left your match against {them} mid-duel. That counts as a loss."));
            _campaignScreen.Close();
        }
    }
}
