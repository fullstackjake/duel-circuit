using System;
using System.Linq;
using UnityEngine;
using Ygo.Engine.Presentation;

namespace YgoCampaign.Game
{
    /// <summary>
    /// "Skip to my turn" in your own duels: during the opponent's turn, their plays run at full speed with no
    /// animations. A decision of yours on the way (a chance to chain, a zone or card to pick) stops it with the board
    /// and the decision shown; once you answer it goes on (DuelController.SkipCheck tests both). When your turn comes, a recap lists what they did and the board marks what changed. A headliner's combo
    /// played meanwhile isn't staged after the fact (DuelController.Show's CatchUpShow).
    /// </summary>
    public sealed partial class DuelController
    {
        private bool _skipping, _skipHeld;
        private BoardSnapshot? _skipBefore;
        private int _skipHistoryFrom, _skipTurn, _skipSteps;

        /// <summary>Whether the opponent's turn can be skipped now (your own duel, their turn, not a lesson).</summary>
        private bool CanSkipTurn() =>
            !_skipping && _human != null && _runner != null && !_runner.IsFinished && _watching == null && _lessonJudge == null
            && _tag == null && _board.Winner < 0 && _board.TurnPlayer != 0 && _board.Turn > 0;

        private void StartSkip()
        {
            if (!CanSkipTurn()) return;
            _skipping = true;
            _skipHeld = false;
            _skipBefore = BoardSnapshot.Of(_board);
            _skipHistoryFrom = _history?.Entries.Count ?? 0;
            _skipTurn = _board.Turn;
            _skipSteps = 0;
            _view.Skipping = true;
            _view.Fx.Clear();
            _view.Fx.Tempo = 0; // no animations, toasts or reactions while skipping
        }

        /// <summary>
        /// Called each frame while skipping: runs the duel for a slice of the frame, stopping at your decisions.
        /// True while the skip owns the frame.
        /// </summary>
        private bool SkipStep()
        {
            if (!_skipping) return false;
            if (_runner == null || _duel == null || _runner.IsFinished) { EndSkip(); return true; }
            bool answered = false;
            if (_runner.IsWaiting)
            {
                // Your turn has come: the skip is over.
                if (_board.TurnPlayer == 0) { EndSkip(); return true; }
                // A decision of yours during their turn (a chance to chain, a zone, a card, yes or no): the board as it
                // stands with the decision on it, held until you answer.
                if (!_answerReady)
                {
                    if (!_skipHeld) { _skipHeld = true; ShowBoardNow(); }
                    return true;
                }
                // Answered. The runner still waits until its next step hands your answer over, so that step is taken
                // here: the loop below stops at a waiting runner, and without it the skip held forever on an answer
                // already given (a chain chance in the opponent's End Phase froze the board, 2026-11-02).
                _answerReady = false;
                _skipHeld = false;
                answered = true;
            }
            var until = Time.realtimeSinceStartup + 0.03f;
            try
            {
                while (Time.realtimeSinceStartup < until && !_runner.IsFinished && (answered || !_runner.IsWaiting))
                {
                    answered = false;
                    if (!Advance()) break;
                    _skipSteps++;
                }
                _board.Refresh(_duel);
                CatchUpShow(); // a headliner's route while you skip: taken quietly, not staged when your turn comes
                _events.Clear();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EndSkip();
                return true;
            }
            if (_runner.IsFinished || _board.Winner >= 0) EndSkip();
            return true;
        }

        private void ShowBoardNow()
        {
            _view.Fx.Clear();
            _board.Refresh(_duel!);
            _view.Render(_board);
            if (_narrator != null) _view.ShowChain(_narrator.Chain.Links);
        }

        private void EndSkip()
        {
            _skipping = false;
            _view.Skipping = false;
            UpdateControls(); // the speed's own animation tempo again
            if (_duel != null) _board.Refresh(_duel);
            CatchUpShow();
            _events.Clear();
            ShowBoardNow();
            if (_board.Winner >= 0 && !_announcedResult) AnnounceResult();
            EndSkipShow(); // the meter as it stands; a duel that ended in the skip ends the show (the frame loop stops with the duel)
            // What they did, from the history (the turn headings left out), and the board's changes marked.
            var lines = _history == null ? new System.Collections.Generic.List<string>()
                : _history.Entries.Skip(_skipHistoryFrom).Where(e => e.Kind != HistoryKind.Turn).Select(e => e.Text).ToList();
            var who = _players.Length > 1 ? _players[1].Name : "The opponent";
            _view.ShowSkipRecap(lines.Count == 0 ? $"{who} did nothing you could see." : "While you skipped:", lines);
            if (_skipBefore != null) ShowChangesSince(_skipBefore);
            _skipBefore = null;
        }

        /// <summary>For checks: whether a skip is running, and how many steps it ran.</summary>
        public (bool running, int steps) SkipState => (_skipping, _skipSteps);
    }
}
