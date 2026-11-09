#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Interactive;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-clickcheck &lt;folder&gt;`: clicks that must land the first time, through a test mouse the UI treats as the real one.
    /// <list type="number">
    /// <item>A press that moves 0, 8 or 14 pixels before its release clicks a button, inside a list that fits, inside one that
    /// scrolls, and outside any list; a real drag still scrolls a long list. The old settings run the same presses after
    /// (logged as "before", for comparison: no ok/FAIL).</item>
    /// <item>In a duel: the pointer parked at a hand card's bottom edge doesn't make it flicker (enters and exits counted);
    /// a right-click held on a card while the board redraws still pins it; the effects layer and the detail panel's
    /// background never cover a card you can act on (the cover watch, run through every check run).</item>
    /// </list>
    /// </summary>
    public sealed partial class DuelController
    {
        private const uint blueEyesCode = 89631139;

        private IEnumerator ClickCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            int bad = 0;
            void Report(string what, bool ok) { if (!ok) bad++; Debug.Log($"click check: {(ok ? "ok  " : "FAIL")} {what}"); }
            void Before(string what) => Debug.Log($"click check: before {what}");

            yield return new WaitForSecondsRealtime(1.5f); // the screens are built, the EventSystem is up
            var mouse = InputSystem.AddDevice<Mouse>("ClickCheckMouse");
            void Put(Vector2 at, bool left, bool right = false) =>
                InputSystem.QueueStateEvent(mouse, new MouseState { position = at }.WithButton(MouseButton.Left, left).WithButton(MouseButton.Right, right));
            IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
            // A press at a point, moved by `move` (pixels) while held, released there.
            IEnumerator Press(Vector2 at, Vector2 move, bool right = false, Func<IEnumerator>? whileHeld = null)
            {
                Put(at, false);
                yield return Frames(3);
                Put(at, !right, right);
                yield return Frames(2);
                Put(at + move * 0.5f, !right, right);
                yield return Frames(1);
                Put(at + move, !right, right);
                yield return Frames(2);
                if (whileHeld != null) yield return whileHeld();
                Put(at + move, false);
                yield return Frames(3);
            }
            Vector2 Center(RectTransform rt) => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));

            Report($"the virtual controller mouse is off while no controller is in use",
                ControllerSupport.Instance == null || !ControllerSupport.Instance.IsActive && !InputSystem.devices.Any(d => d is Mouse && d.name == "ControllerCursor" && d.enabled));
            var firstMouse = ClickSupport.FirstMouseReport;
            Report($"the first click on the window reaches the game (view classes: {firstMouse ?? "none yet"}; background behavior {InputSystem.settings.backgroundBehavior})",
                !string.IsNullOrEmpty(firstMouse) && !firstMouse!.Contains("no view class") && !firstMouse.Contains("has its own")
                && InputSystem.settings.backgroundBehavior == InputSettings.BackgroundBehavior.IgnoreFocus);
            // Pixels per point as WindowSize measures a window: Screen.width over the content view's width in points.
            float contentPt = ClickSupport.ContentWidthPoints();
            float ppp = !Screen.fullScreen && contentPt > 1 ? Screen.width / contentPt : ClickSupport.PixelsPerPoint > 0 ? ClickSupport.PixelsPerPoint : 1;
            int wantThreshold = Mathf.Max(10, Mathf.RoundToInt(ClickSupport.DragThresholdPoints * ppp));
            Report($"the drag threshold is {ClickSupport.DragThresholdPoints:0} points: {EventSystem.current?.pixelDragThreshold} px "
                   + $"(Screen {Screen.width} px over {contentPt:0} pt = {ppp:0.##} px per pt)",
                EventSystem.current != null && EventSystem.current.pixelDragThreshold == wantThreshold);

            // The window going to the background lets go of everything held, and switches the controllers off until it's back.
            {
                var keys = InputSystem.AddDevice<Keyboard>("ClickCheckKeys");
                var pad = InputSystem.AddDevice<Gamepad>("ClickCheckPad");
                var target = Ui.Overlay(transform, "ClickCheckFocus", 900);
                int focusClicks = 0;
                var b = Ui.Button(target, "Held", Vector2.zero, new Vector2(360, 90), () => focusClicks++);
                InputSystem.QueueStateEvent(keys, new KeyboardState(Key.LeftShift));
                var at = Center((RectTransform)b.transform);
                Put(at, false);
                yield return Frames(3);
                Put(at, true);
                yield return Frames(3);
                bool heldBefore = mouse.leftButton.isPressed && keys.leftShiftKey.isPressed;
                ClickSupport.FocusLost();
                yield return Frames(4);
                Report($"focus lost: the held mouse button and Shift let go, with no click from the release ({focusClicks} clicks), and the pad is off",
                    heldBefore && !mouse.leftButton.isPressed && !keys.leftShiftKey.isPressed && focusClicks == 0 && !pad.enabled && mouse.enabled && keys.enabled);
                ClickSupport.FocusGained();
                yield return Frames(2);
                Report("focus back: the pad is on again", pad.enabled);
                InputSystem.RemoveDevice(keys);
                InputSystem.RemoveDevice(pad);
                Destroy(target.parent.gameObject);
                yield return Frames(2);
            }

            // ---- 1. Presses that move a little, inside lists and out ----------------------------------------------------
            var root = Ui.Overlay(transform, "ClickCheck", 900);
            Ui.Image(root, new Color(0.08f, 0.09f, 0.12f, 1));
            var clicks = new Dictionary<string, int>();
            Button Target(Transform parent, string name, Vector2 center)
            {
                var b = Ui.Button(parent, name, center, new Vector2(360, 90), () => clicks[name] = clicks.TryGetValue(name, out var n) ? n + 1 : 1);
                if (parent != root)
                {
                    // Near the top of its list's rows (the rows hang from the top of the view).
                    var rt = (RectTransform)b.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1);
                    rt.anchoredPosition = new Vector2(0, -70);
                }
                return b;
            }
            (RectTransform content, ScrollRect scroll) List(string name, Vector2 center, float contentHeight)
            {
                var viewport = Ui.Placed(name, root, center, new Vector2(380, 320));
                Ui.Image(viewport, new Color(0, 0, 0, 0.3f));
                viewport.gameObject.AddComponent<RectMask2D>();
                var content = Ui.Rect("Rows", viewport);
                content.anchorMin = new Vector2(0, 1);
                content.anchorMax = new Vector2(1, 1);
                content.pivot = new Vector2(0.5f, 1);
                content.sizeDelta = new Vector2(0, contentHeight);
                var scroll = viewport.gameObject.AddComponent<ScrollRect>();
                scroll.viewport = viewport;
                scroll.content = content;
                scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                return (content, scroll);
            }
            var control = Target(root, "outside", new Vector2(-520, 0));
            var (fitRows, fitScroll) = List("fits", new Vector2(0, 0), 200);
            var fitButton = Target(fitRows, "in a list that fits", new Vector2(0, -60));
            var (longRows, longScroll) = List("scrolls", new Vector2(520, 0), 1400);
            var longButton = Target(longRows, "in a list that scrolls", new Vector2(0, -60));
            yield return new WaitForSecondsRealtime(0.8f); // the lists are found and settle
            Report("a list whose rows fit doesn't scroll (so it can't turn a press into a drag)", fitScroll.GetComponent<ScrollFit>()?.Off == true && !fitScroll.enabled);
            Report("a list that's longer than its view still scrolls", longScroll.enabled);

            // 0, 4 and 7 points: 0, 8 and 14 pixels on Retina.
            var moves = new[] { 0, Mathf.RoundToInt(4 * ppp), Mathf.RoundToInt(7 * ppp) };
            IEnumerator Cases(bool before)
            {
                foreach (var (button, name) in new[] { (control, "outside"), (fitButton, "in a list that fits"), (longButton, "in a list that scrolls") })
                    foreach (var px in moves)
                    {
                        clicks.Clear();
                        longScroll.verticalNormalizedPosition = 1;
                        yield return Frames(2);
                        yield return Press(Center((RectTransform)button.transform), new Vector2(px, 0));
                        bool clicked = clicks.TryGetValue(name, out var n) && n == 1;
                        if (before) Before($"(10 px threshold, lists always scroll): a press moved {px} px {name} {(clicked ? "clicks" : "is LOST")}");
                        else Report($"a press moved {px} px ({px / ppp:0} pt) {name} clicks", clicked);
                    }
            }
            yield return Cases(false);
            // A real drag on the long list scrolls it, and isn't a click.
            clicks.Clear();
            longScroll.verticalNormalizedPosition = 1;
            yield return Frames(2);
            float startY = longRows.anchoredPosition.y;
            yield return Press(Center((RectTransform)longButton.transform), new Vector2(0, 120 * ppp));
            Report($"a real drag ({120 * ppp:0} px) scrolls the long list and isn't a click (moved {longRows.anchoredPosition.y - startY:0})",
                longRows.anchoredPosition.y - startY > 20 && !clicks.ContainsKey("in a list that scrolls"));

            // The old settings, for comparison.
            ClickSupport.ThresholdOverride = 10;
            ClickSupport.FitScrolls = false;
            if (EventSystem.current != null) EventSystem.current.pixelDragThreshold = 10;
            yield return new WaitForSecondsRealtime(0.7f);
            yield return Cases(true);
            ClickSupport.ThresholdOverride = null;
            ClickSupport.FitScrolls = true;
            yield return new WaitForSecondsRealtime(0.7f);
            Destroy(root.parent.gameObject);
            yield return null;

            // The guard knows a pick by its card, so it holds across decisions (an incremental pick asks again after each card).
            {
                var card = new Ygo.Engine.Protocol.CardRef(blueEyesCode, 0, Location.MZone, 1);
                ChoiceSet Set() => new ChoiceSet { Mode = ChoiceMode.Incremental, Prompt = null!, Choices = { new Choice { Label = "A", Card = card } } };
                var one = Set();
                bool taken = !ClickGuard.Repeat(one, 0), again = ClickGuard.Repeat(Set(), 0);
                yield return new WaitForSecondsRealtime(0.15f);
                bool stillAgain = ClickGuard.Repeat(Set(), 0); // 150 ms after the taken click: dropped, and the window not stretched
                yield return new WaitForSecondsRealtime(0.15f);
                bool later = !ClickGuard.Repeat(Set(), 0); // 300 ms after the taken click: a new click
                Report("the same card picked again in a new decision within 250 ms is dropped; the dropped repeats don't stretch the window",
                    taken && again && stillAgain && later);
            }

            // ---- 2. A duel: hand cards, a press across a redraw, the cover watch ------------------------------------------
            const uint blueEyes = blueEyesCode, luster = 11091375, kuriboh = 40640057;
            _checkSetup = new List<PlacedCard>
            {
                new PlacedCard(0, blueEyes, Location.MZone, 1, Ygo.Engine.Position.FaceUpAttack),
                new PlacedCard(0, kuriboh, Location.MZone, 3, Ygo.Engine.Position.FaceUpDefense), // a second Tribute
                new PlacedCard(0, blueEyes, Location.Hand), // to Tribute Summon: the pick clicked twice
                new PlacedCard(1, luster, Location.MZone, 2, Ygo.Engine.Position.FaceUpAttack),
                new PlacedCard(1, kuriboh, Location.MZone, 1, Ygo.Engine.Position.FaceUpDefense),
            };
            _pickCharacter[0] = -1;
            _pickDeck[0] = "classic";
            _pickCharacter[1] = Math.Max(0, _roster.FindIndex(p => p.Id == "rookie-ryo"));
            _pickDeck[1] = "classic";
            seed = 21;
            _paused = false;
            _nextFirstPlayer = 0;
            NewDuel();
            speedIndex = Speeds.Length - 1;
            UpdateControls();
            var until = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < until)
            {
                var set = _view.Prompt.Current;
                if (set?.Prompt is Ygo.Engine.Protocol.IdlePrompt && _board.TurnPlayer == 0) break;
                if (set != null && set.Prompt is not Ygo.Engine.Protocol.IdlePrompt)
                {
                    if (set.IsChain && set.CanCancel) _view.Prompt.CancelSelection();
                    else if (set.Mode == ChoiceMode.Single) _view.Prompt.ClickChoice(Math.Max(0, set.Choices.FindIndex(c => c.Label == "No")));
                    else { for (int i = 0; i < Math.Max(1, set.Min); i++) _view.Prompt.ClickChoice(i); _view.Prompt.Confirm(); }
                }
                yield return null;
            }
            Report("your Main Phase came", _view.Prompt.Current?.Prompt is Ygo.Engine.Protocol.IdlePrompt);
            yield return new WaitForSecondsRealtime(1.2f); // the turn's banners are done
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "click-duel.png"));
            yield return null;

            // The pointer parked on a hand card's bottom edge: the card lifts once and stays up.
            IEnumerator Park(int sequence, Action<int, int> result)
            {
                var card = _view.ViewAt(0, Location.Hand, sequence);
                if (card == null) { result(-1, -1); yield break; }
                var r = card.rect;
                var edge = RectTransformUtility.WorldToScreenPoint(null, card.TransformPoint(new Vector2(r.center.x, r.yMin + r.height * 0.08f)));
                Put(edge + new Vector2(0, -200), false); // off the card first
                yield return Frames(4);
                HandLift.Enters = HandLift.Exits = 0;
                Put(edge, false);
                yield return new WaitForSecondsRealtime(1.5f);
                result(HandLift.Enters, HandLift.Exits);
                Put(edge + new Vector2(0, -200), false);
                yield return new WaitForSecondsRealtime(0.5f);
            }
            int enters = 0, exits = 0;
            yield return Park(0, (a, b) => { enters = a; exits = b; });
            Report($"the pointer at a hand card's bottom edge: it lifts once and stays ({enters} enters, {exits} exits in 1.5 s)", enters == 1 && exits == 0);
            HandLift.KeepHomeHit = false;
            yield return Park(2, (a, b) => { enters = a; exits = b; });
            Before($"(no resting footprint): the pointer at a hand card's bottom edge: {enters} enters, {exits} exits in 1.5 s");
            HandLift.KeepHomeHit = true;

            // A right-click held on the opponent's Luster Dragon while the board redraws twice: it still pins.
            IEnumerator PinAcrossRedraws(Action<string?> result)
            {
                if (_view.PinState.pinned != null) _view.TogglePin();
                var target = _view.ViewAt(1, Location.MZone, 2);
                if (target == null) { result("(no card)"); yield break; }
                var at = Center(target);
                yield return Press(at, Vector2.zero, right: true, whileHeld: () => Redraws());
                yield return Frames(3);
                result(_view.PinState.pinned);
            }
            IEnumerator Redraws()
            {
                for (int i = 0; i < 2; i++) { _view.Render(_board); yield return Frames(2); }
            }
            string? pinned = null;
            yield return PinAcrossRedraws(p => pinned = p);
            Report($"a right-click held across two board redraws still pins the card (pinned: {pinned ?? "nothing"})", pinned == "Luster Dragon");
            BoardView.DeferRenders = false;
            yield return PinAcrossRedraws(p => pinned = p);
            Before($"(redraws at once): a right-click held across two redraws pins {pinned ?? "nothing (the click is LOST)"}");
            BoardView.DeferRenders = true;
            if (_view.PinState.pinned != null) _view.TogglePin();

            // A Tribute Summon: a Tribute clicked twice within a moment (real clicks) stays picked; clicked again later, it's
            // unpicked, as a change of mind.
            {
                int hand = _board.Players[0].Hand.FindIndex(c => c != null && c.Code == blueEyes);
                _view.DebugClickCard(0, Location.Hand, hand);
                var set = _view.Prompt.Current;
                int summon = set?.Choices.FindIndex(c => c.Card is { } r && (r.Location & 0x7f) == Location.Hand && r.Sequence == hand && c.Verb == "Summon") ?? -1;
                if (summon >= 0) _view.Prompt.ClickChoice(summon);
                ChoiceSet? tribute = null;
                var wait = Time.realtimeSinceStartup + 5;
                while (Time.realtimeSinceStartup < wait && (tribute = _view.Prompt.Current)?.Prompt is not Ygo.Engine.Protocol.TributePrompt) yield return null;
                yield return new WaitForSecondsRealtime(0.4f);
                int Pick(int seq) => _view.Prompt.Current?.Choices.FindIndex(c => c.Card is { } r && r.Controller == 0 && (r.Location & 0x7f) == Location.MZone && r.Sequence == seq) ?? -1;
                bool Picked(int seq) { var cur = _view.Prompt.Current; int i = Pick(seq); return cur != null && i >= 0 && (_view.Prompt.IsSelected(i) || cur.Choices[i].AlreadySelected); }
                var a = _view.ViewAt(0, Location.MZone, 1);
                if (tribute?.Prompt is Ygo.Engine.Protocol.TributePrompt && a != null)
                {
                    var at = Center(a);
                    Put(at, false);
                    yield return Frames(3);
                    // Two clicks 3 frames apart (about 50 ms): one pick.
                    Put(at, true); yield return Frames(1); Put(at, false); yield return Frames(3);
                    a = _view.ViewAt(0, Location.MZone, 1) ?? a; // the board redraws after a pick
                    Put(Center(a), true); yield return Frames(1); Put(Center(a), false); yield return Frames(4);
                    bool once = Picked(1);
                    yield return new WaitForSecondsRealtime(0.4f);
                    a = _view.ViewAt(0, Location.MZone, 1) ?? a;
                    Put(Center(a), true); yield return Frames(1); Put(Center(a), false); yield return Frames(4);
                    bool undone = !Picked(1);
                    Report($"a Tribute clicked twice within 250 ms stays picked ({(once ? "picked" : "not picked")}); clicked again later it's unpicked ({(undone ? "unpicked" : "still picked")})",
                        once && undone);
                }
                else Report($"the Tribute Summon asked for Tributes (got: {_view.Prompt.Current?.Title ?? "nothing"})", false);
                // Finish the summon (both Tributes) so the duel goes on as before.
                for (var t = Time.realtimeSinceStartup + 3; Time.realtimeSinceStartup < t && _view.Prompt.Current?.Prompt is Ygo.Engine.Protocol.TributePrompt;)
                {
                    var cur = _view.Prompt.Current!;
                    if (cur.Mode == ChoiceMode.Multi)
                    {
                        for (int i = 0; i < cur.Choices.Count && i < cur.Min; i++) if (!_view.Prompt.IsSelected(i)) _view.Prompt.ClickChoice(i);
                        _view.Prompt.Confirm();
                    }
                    else _view.Prompt.ClickChoice(cur.Choices.FindIndex(c => !c.AlreadySelected));
                    yield return new WaitForSecondsRealtime(0.3f);
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }

            // Your next turn's Battle Phase: its banner sweeps over the field while the attack is asked for (the cover
            // watch looks at every decision frame on the way).
            _view.GoToPhase("end");
            until = Time.realtimeSinceStartup + 60;
            while (Time.realtimeSinceStartup < until)
            {
                var set = _view.Prompt.Current;
                if (set?.Prompt is Ygo.Engine.Protocol.IdlePrompt && _board.TurnPlayer == 0 && _board.Turn >= 3) break;
                if (set != null && set.Prompt is not Ygo.Engine.Protocol.IdlePrompt && set.Prompt is not Ygo.Engine.Protocol.BattlePrompt)
                {
                    if (set.IsChain && set.CanCancel) _view.Prompt.CancelSelection();
                    else if (set.Mode == ChoiceMode.Single) _view.Prompt.ClickChoice(Math.Max(0, set.Choices.FindIndex(c => c.Label == "No")));
                    else { for (int i = 0; i < Math.Max(1, set.Min); i++) _view.Prompt.ClickChoice(i); _view.Prompt.Confirm(); }
                }
                else if (set?.Prompt is Ygo.Engine.Protocol.IdlePrompt && _board.TurnPlayer == 0) _view.GoToPhase("end");
                yield return null;
            }
            bool battle = _view.GoToPhase("battle");
            yield return new WaitForSecondsRealtime(2.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "click-battle.png"));
            yield return null;
            Report($"your turn {_board.Turn} went to the Battle Phase (the attack asked for under its banner)", battle && _view.Prompt.Current?.Prompt is Ygo.Engine.Protocol.BattlePrompt);
            Debug.Log(BoardView.CoverSummary());
            Report($"nothing in the effects layer covers a card you can act on, hand cards at rest included ({BoardView.CoverFx} times; "
                   + $"{BoardView.CoverFxHeldOff} times an effect was over one and let the click through)", BoardView.CoverFx == 0);
            Report($"nothing else covers one but the intended overlays and the shown detail panel (other: {BoardView.CoverOther}; "
                   + $"intended {BoardView.CoverIntended}, detail panel {BoardView.CoverPanelShown}, no hit at a card's centre {BoardView.CoverNoHit})",
                BoardView.CoverOther == 0);
            InputSystem.RemoveDevice(mouse);
            Debug.Log($"click check: done, {bad} failed");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }
    }
}
#endif
