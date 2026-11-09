using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.Interactive;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The controls check's last step: Skip to my turn stops for each decision of yours on the opponent's turn, shows
    /// it, and goes on once you answer. A chance to chain passed, then Call of the Haunted activated from the skip (its
    /// target picked) and the zone its monster goes to clicked on the board. Before the fix the skip held forever on the
    /// first answer: the owner's duel froze at a chain chance in Old Gus's End Phase (2026-11-02).
    /// </summary>
    public sealed partial class DuelController
    {
        private System.Collections.IEnumerator SkipDecisionsCheck(Action<string, bool> report, Func<string, System.Collections.IEnumerator> shot)
        {
            const uint call = 97077563, blueEyes = 89631139; // Call of the Haunted, Blue-Eyes White Dragon
            _checkSetup = new List<PlacedCard>
            {
                new PlacedCard(0, call, Location.SZone, 2, Ygo.Engine.Position.FaceDownAttack | Ygo.Engine.Position.FaceDownDefense),
                new PlacedCard(0, blueEyes, Location.Grave),
            };
            _pickCharacter[0] = -1;
            _pickDeck[0] = "classic";
            _pickCharacter[1] = Math.Max(0, _roster.FindIndex(p => p.Id == "rookie-ryo"));
            _pickDeck[1] = "classic";
            seed = 31;
            _paused = false;
            _nextFirstPlayer = 0; // your turn 1, then theirs to skip
            NewDuel();
            speedIndex = Speeds.Length - 1;
            UpdateControls();

            // Anything but the decisions under test: no chains, "No", the first pick.
            void AnswerPlainly(ChoiceSet set)
            {
                if (set.IsChain && set.CanCancel) _view.Prompt.CancelSelection();
                else if (set.Prompt is Ygo.Engine.Protocol.EffectYesNoPrompt || set.Prompt is Ygo.Engine.Protocol.YesNoPrompt)
                    _view.Prompt.ClickChoice(Math.Max(0, set.Choices.FindIndex(c => c.Label == "No")));
                else if (set.Mode == ChoiceMode.Single) _view.Prompt.ClickChoice(0);
                else { for (int i = 0; i < Math.Max(1, set.Min); i++) _view.Prompt.ClickChoice(i); _view.Prompt.Confirm(); }
            }

            // Your turn 1: straight to the End Phase.
            var until = Time.realtimeSinceStartup + 30;
            bool ended = false;
            while (Time.realtimeSinceStartup < until && !(_board.TurnPlayer == 1 && CanSkipTurn()))
            {
                var set = _view.Prompt.Current;
                if (set != null && _runner != null && _runner.IsWaiting)
                {
                    if (set.Prompt is Ygo.Engine.Protocol.IdlePrompt && !ended) { ended = true; _view.GoToPhase("end"); }
                    else if (!(set.Prompt is Ygo.Engine.Protocol.IdlePrompt)) AnswerPlainly(set);
                }
                yield return null;
            }
            yield return null;
            if (!(_board.TurnPlayer == 1 && CanSkipTurn()))
            {
                report($"the skip-decisions duel reached the opponent's turn (turn {_board.Turn}, turn player {_board.TurnPlayer})", false);
                yield break;
            }

            // Their turn, skipped, with every chance to respond asked (as holding Shift does).
            if (_human != null) _human.Presenter.Mode = Ygo.Engine.Interactive.ResponseMode.On;
            _view.RequestSkip();
            int shown = 0, passes = 0, steps = 0;
            bool activated = false, targeted = false, zoned = false, zoneOnBoard = false;
            string chainKind = "", zonePicked = "";
            int zoneSeq = -1;
            var skipStart = Time.realtimeSinceStartup;
            until = skipStart + 60;
            while (Time.realtimeSinceStartup < until && (_skipping || _board.TurnPlayer != 0))
            {
                var set = _view.Prompt.Current;
                if (set == null || _runner == null || !_runner.IsWaiting || _runner.WaitingFor != 0 || _human?.Current != set)
                {
                    yield return null;
                    continue;
                }
                if (_skipping) shown++;
                if (set.IsChain)
                {
                    int haunted = set.Choices.FindIndex(c => c.Card?.Code == call);
                    if (passes > 0 && !activated && haunted >= 0 && _skipping)
                    {
                        chainKind = _view.Dialogs.Kind;
                        yield return shot("skip-chain");
                        activated = true;
                        _view.Prompt.ClickChoice(haunted);
                    }
                    else if (set.CanCancel) { if (_skipping && haunted >= 0) passes++; _view.Prompt.CancelSelection(); }
                    else AnswerPlainly(set);
                }
                else if (set.Title.StartsWith("Choose a zone"))
                {
                    var free = set.Choices.FirstOrDefault(c => c.Card is { } z && z.Controller == 0 && (z.Location & 0x7f) == Location.MZone)?.Card;
                    zoneOnBoard = _skipping && !_view.PromptListShown;
                    yield return shot("skip-zone");
                    if (free is { } zone)
                    {
                        zoneSeq = (int)zone.Sequence;
                        _view.DebugClickCard(0, Location.MZone, zoneSeq); // the click on the glowing zone
                        zoned = _view.Prompt.Current != set;
                        zonePicked = $"zone {zoneSeq + 1}";
                    }
                    else AnswerPlainly(set);
                }
                else if (activated && !targeted && set.Choices.FindIndex(c => c.Card?.Code == blueEyes) is var target and >= 0)
                {
                    targeted = true;
                    _view.Prompt.ClickChoice(target);
                    if (_view.Prompt.Current == set && set.Mode != ChoiceMode.Single) _view.Prompt.Confirm();
                }
                else AnswerPlainly(set);
                steps++;
                yield return null;
            }
            if (_human != null) _human.Presenter.Mode = ResponseSetting;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return shot("skip-decisions-recap");
            var monsters = _board.Players[0].Monsters;
            bool summoned = zoneSeq >= 0 && zoneSeq < monsters.Count && monsters[zoneSeq]?.Code == blueEyes;
            report($"Skip to my turn stops for each decision of yours on their turn and shows it ({shown} shown while skipping, the chance to chain as \"{chainKind}\")",
                shown >= 3 && chainKind == "chain");
            report($"a chance to chain passed while skipping lets the skip go on ({passes} passed, then Call of the Haunted activated)", passes >= 1 && activated);
            report("its target, picked while skipping, goes in too", targeted);
            report($"the zone choice shows on the board while skipping and a click on a zone answers it ({zonePicked})", zoneOnBoard && zoned);
            report($"the skip then goes on to your turn with the recap, Blue-Eyes in the zone you picked ({Time.realtimeSinceStartup - skipStart - 0.3f:0.0} s, {steps} answers)",
                !_skipping && _board.TurnPlayer == 0 && _view.SkipRecap != null && summoned);
            _view.CloseSkipRecap();
        }
    }
}
