using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Presentation;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Clicks on the board that must not be lost:
    /// <list type="bullet">
    /// <item>A press held on a card holds the duel (no step, no redraw) until the release has been handled: Render destroys
    /// and recreates every card, and a card destroyed between press and release never gets its click.</item>
    /// <item>In check runs, a watch over every card you can act on while a decision is up: whatever else is on top at its
    /// centre is counted by kind (the effects layer, the detail panel's background, anything else) and summed up at quit.</item>
    /// </list>
    /// </summary>
    public sealed partial class BoardView
    {
        /// <summary>For checks: redraws wait for a held press (off: the old immediate redraw, for comparison).</summary>
        public static bool DeferRenders = true;

        private BoardState? _renderPending;
        private bool _pressOnCards, _releasePending;
        private int _pressCheckedFrame = -1;
        private float _pressSince;
        private int _releaseFrame = -1;
        /// <summary>The longest a held press holds the duel (seconds).</summary>
        private const float PressHoldLimit = 8f;
        private static readonly List<RaycastResult> ClickHits = new List<RaycastResult>();

        /// <summary>Whether a redraw is waiting for a press to end (for checks).</summary>
        public bool RenderDeferred => _renderPending != null;

        /// <summary>
        /// A mouse button is held on a card (or was let go this frame and the UI hasn't finished with it until LateUpdate).
        /// Order-proof: whichever script asks first in a frame sees the same answer.
        /// </summary>
        private bool CardPressHeld()
        {
            if (!DeferRenders) return false;
#if ENABLE_INPUT_SYSTEM
            UnityEngine.InputSystem.Mouse? pressing = null;
            bool justPressed = false;
            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
            {
                if (device is not UnityEngine.InputSystem.Mouse m || !m.enabled) continue;
                if (!m.leftButton.isPressed && !m.rightButton.isPressed && !m.middleButton.isPressed) continue;
                pressing = m;
                justPressed = m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame;
                break;
            }
            // A release still waiting after its frame (LateUpdate didn't run: the board was off): it's over.
            if (_releasePending && Time.frameCount > _releaseFrame) _releasePending = false;
            if (pressing == null)
            {
                if (_pressOnCards) { _pressOnCards = false; _releasePending = true; _releaseFrame = Time.frameCount; }
                _pressCheckedFrame = -1;
                return _releasePending;
            }
            if (_pressCheckedFrame < 0 || (justPressed && _pressCheckedFrame != Time.frameCount))
            {
                _pressCheckedFrame = Time.frameCount;
                _pressSince = Time.unscaledTime;
                _pressOnCards = OverCards(pressing.position.ReadValue());
            }
            // A release never seen: the duel isn't held for good (well inside the checks' 20 s watchdog).
            if (_pressOnCards && Time.unscaledTime - _pressSince > PressHoldLimit) _pressOnCards = false;
            return _pressOnCards || _releasePending;
#else
            return false;
#endif
        }

        private bool OverCards(Vector2 screen)
        {
            var events = EventSystem.current;
            if (events == null || _cardLayer == null) return false;
            ClickHits.Clear();
            events.RaycastAll(new PointerEventData(events) { position = screen }, ClickHits);
            return ClickHits.Count > 0 && ClickHits[0].gameObject.transform.IsChildOf(_cardLayer);
        }

        private void UpdateClicks()
        {
            CardPressHeld();
            if (CoverWatchOn) WatchCovers();
        }

        private void OnDisable()
        {
            _releasePending = false;
            _pressOnCards = false;
            _pressCheckedFrame = -1;
        }

        private void LateUpdate()
        {
            // The UI has handled this frame's release (EventSystem runs in Update): the held redraw can happen now.
            CardPressHeld();
            _releasePending = false;
            if (_renderPending != null && !CardPressHeld())
            {
                var board = _renderPending;
                _renderPending = null;
                Render(board);
            }
        }

        // ---- The cover watch (check runs) ----------------------------------------------------------------------

        private static bool? _coverWatch;
        private static bool CoverWatchOn => _coverWatch ??= DuelController.IsCheckRun;

        /// <summary>The watch's totals: decision frames looked at, card centres tested, and what covered them.</summary>
        public static int CoverFrames, CoverTests, CoverFx, CoverPanelShown, CoverIntended, CoverNoHit, CoverOther, CoverFxHeldOff;
        private static readonly Dictionary<string, int> CoverNames = new Dictionary<string, int>();
        private static bool _coverSummaryHooked;
        private static readonly List<Graphic> CoverGraphics = new List<Graphic>();

        /// <summary>Overlays meant to sit over the cards while they're up (menus, pickers, dialogs, the reader).</summary>
        private static readonly string[] IntendedCovers = { "PromptDialog", "PhasePicker", "ActionMenu", "PileViewer", "PilePicker", "CardReader", "Concede", "SkipRecap" };

        private void WatchCovers()
        {
            if (!_coverSummaryHooked)
            {
                _coverSummaryHooked = true;
                Application.quitting += () => Debug.Log(CoverSummary());
            }
            var set = Prompt?.Current;
            if (set == null || !_humanMode || _lastBoard == null || Time.frameCount % 3 != 0) return;
            var events = EventSystem.current;
            if (events == null) return;
            CoverFrames++;
            foreach (var (controller, location, sequence) in _actionable.ToList())
            {
                if (location == Location.Deck || location == Location.Extra) continue; // stacked: the pile, not a card
                if (ViewAt(controller, location, (int)sequence) is not RectTransform view || !view.gameObject.activeInHierarchy) continue;
                // A hand card at rest where it sits in the fan (a lifted one, where it rests: its footprint stays clickable).
                var world = view.GetComponent<HandLift>() is HandLift lift ? lift.RestingCenter() : view.TransformPoint(view.rect.center);
                var center = RectTransformUtility.WorldToScreenPoint(null, world);
                if (center.x < 0 || center.y < 0 || center.x > Screen.width || center.y > Screen.height) continue;
                CoverTests++;
                ClickHits.Clear();
                events.RaycastAll(new PointerEventData(events) { position = center }, ClickHits);
                // What the effects layer has over the card, that the layer's CanvasGroup keeps from taking the click.
                _fxLayer.GetComponentsInChildren(false, CoverGraphics);
                if (CoverGraphics.Any(g => g.raycastTarget && g.isActiveAndEnabled && RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform, center, null)))
                    CoverFxHeldOff++;
                CoverGraphics.Clear();
                if (ClickHits.Count == 0) continue;
                var top = ClickHits[0].gameObject.transform;
                if (top.IsChildOf(view)) continue;
                string kind;
                var path = HierarchyPath(top);
                if (top.IsChildOf(_fxLayer)) { CoverFx++; kind = "fx"; }
                else if (_detailGroup != null && top.IsChildOf(_detailGroup)) { CoverPanelShown++; kind = "detail panel (shown)"; }
                else if (top.IsChildOf(_cardLayer)) continue; // another card (a lifted or overlapping one): what you'd see on top
                else if (IntendedCovers.Any(n => path.Contains(n))) { CoverIntended++; kind = "intended"; }
                else if (!top.IsChildOf(_canvas)) { CoverIntended++; kind = "another screen over the duel"; } // menu, settings...
                else if (top == _canvas || top.name == "Background" && top.parent == _canvas)
                {
                    // Nothing of the card's own at its centre: the card isn't hit-testable there (not a cover).
                    CoverNoHit++;
                    kind = $"no hit on {Pile(location)} {sequence} of player {controller}";
                    path = view.name;
                }
                else { CoverOther++; kind = "other"; }
                var name = $"{kind}: {path}";
                CoverNames[name] = CoverNames.TryGetValue(name, out var n) ? n + 1 : 1;
            }
        }

        private static string Pile(uint location) => location switch
        {
            Location.Hand => "hand", Location.MZone => "monster zone", Location.SZone => "spell zone", Location.Grave => "GY",
            Location.Removed => "banished", _ => $"location {location}",
        };

        private static string HierarchyPath(Transform t)
        {
            var parts = new List<string>();
            for (var x = t; x != null && parts.Count < 4; x = x.parent) parts.Add(x.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>One line for the log: the watch's totals and the most frequent covers.</summary>
        public static string CoverSummary() =>
            $"cover watch: {CoverFrames} decision frames, {CoverTests} card centres; covered by fx {CoverFx}, other {CoverOther}; "
            + $"by the shown detail panel {CoverPanelShown}, by intended overlays {CoverIntended}; card not hit at its centre {CoverNoHit}; "
            + $"fx graphics held off a card {CoverFxHeldOff}"
            + (CoverNames.Count > 0 ? " [" + string.Join("; ", CoverNames.OrderByDescending(p => p.Value).Take(8).Select(p => $"{p.Key} x{p.Value}")) + "]" : "");
    }
}
