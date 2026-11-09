using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The keyboard's arrows and Enter over a screen's highlighted things (the shop's rows, a pack's cards, the used
    /// market's tiles, Moth's van): one per screen, read in its Update. Arrows move the highlight, Enter (or Space) does what a click
    /// on the highlighted thing does. A grid goes by where its things sit (<see cref="Step"/>): left/right along a row,
    /// up/down to the nearest row; at an edge the highlight stays put, as the controller's d-pad does. A list goes up and
    /// down through its rows (across its pages) and left/right turns the page.
    /// Nothing is read while a text field has the keyboard (or had it last frame: the Enter that ends a search), or while
    /// something else covers the screen (a screen opened over it, the pause menu, a dialog).
    /// A controller's d-pad drives the same highlight (ControllerSupport leaves the virtual cursor to the stick on such a
    /// screen), and its select button is Enter once the d-pad has moved the highlight. Buying goes through a
    /// <see cref="KeyConfirm"/>: Enter opens it, a second Enter buys, Escape (or the back button) cancels.
    /// </summary>
    public sealed class ArrowNav
    {
        /// <summary>This frame's keys: the arrow pressed (x: -1 left, +1 right; y: -1 up, +1 down) and Enter.</summary>
        public struct Keys
        {
            public Vector2Int Move;
            public bool Enter;
            public bool Any => Move != Vector2Int.zero || Enter;
        }

        private bool _wasTyping;
        /// <summary>Something covered the screen at the last read, or there was no read last frame (closed, or just opened).</summary>
        private bool _wasCovered = true;
        private int _readFrame = -10;

        /// <summary>Whether this screen's keys have been used since it last reset (a grid shows its highlight only then).</summary>
        public bool Used
        {
            get => _used;
            set { _used = value; if (!value) _pointed = -1; } // a reset also forgets where the mouse pointed
        }
        private bool _used;

        /// <summary>
        /// This frame's keys for the screen under <paramref name="owner"/>, if it's the one in front where
        /// <paramref name="probe"/> sits (and not under <paramref name="except"/>, a layer of its own over it): none otherwise.
        /// Call it every frame the screen is open (it looks whether it's covered every frame, one raycast): a press is
        /// dropped on the first frame the screen is uncovered, so the Enter that closes a cover (the pause menu's choice,
        /// a story's last line) doesn't also act on the screen under it, whichever screen reads the key first.
        /// </summary>
        public Keys Read(RectTransform owner, RectTransform probe, RectTransform? except = null)
        {
            int frame = Time.frameCount;
            bool typing = TypingSomewhere();
            bool wasTyping = _wasTyping;
            _wasTyping = typing;
            bool front = InFront(owner, probe, except);
            bool wasCovered = _wasCovered || _readFrame != frame - 1;
            _wasCovered = !front;
            _readFrame = frame;
            if (front) FrontFrame = frame;
            var keys = Pressed();
            if (!keys.Any || typing || wasTyping || !front || wasCovered) return default;
            Used = true;
            if (keys.Move != Vector2Int.zero) InputMode.KeysUsed(); // the gold frame back, hover off (last input wins)
            // The keys are ours: a button last clicked mustn't also take Enter (a second purchase) or the arrows.
            DropButtonFocus(owner);
            return keys;
        }

        /// <summary>The thing the mouse last pointed at (the screen's own index), -1 for none.</summary>
        private int _pointed = -1;

        /// <summary>
        /// The mouse is over thing <paramref name="index"/> (a row's or tile's hover): the keyboard's position moves there,
        /// without showing the frame, so the arrows go on from where the mouse was. Ignored while the keys drive (a list
        /// redrawn under a resting mouse doesn't move the highlight).
        /// </summary>
        public void Point(int index)
        {
            if (InputMode.Pointer) _pointed = index;
        }

        /// <summary>
        /// Where an arrow press starts: the thing the mouse last pointed at (once; <see cref="Point"/>), else
        /// <paramref name="at"/> (the screen's own highlight). Indexes outside 0..<paramref name="count"/>-1 are dropped.
        /// </summary>
        public int From(int at, int count)
        {
            int p = _pointed;
            _pointed = -1;
            return p >= 0 && p < count ? p : at;
        }

        /// <summary>The last frame a screen read its keys while in front (ControllerSupport: the d-pad is the highlight's then).</summary>
        public static int FrontFrame { get; private set; } = -10;

        /// <summary>Whether a screen with a keyboard highlight is in front (read this frame or the last).</summary>
        public static bool ScreenInFront => Time.frameCount - FrontFrame <= 1;

        /// <summary>The arrows and Enter pressed this frame on any keyboard (the checks plug in one of their own), and the d-pad.</summary>
        private static Keys Pressed()
        {
            var keys = new Keys();
#if ENABLE_INPUT_SYSTEM
            // The controller: the d-pad moves the highlight; its select button is Enter once the d-pad drives (not the stick).
            if (UnityEngine.InputSystem.Gamepad.current is UnityEngine.InputSystem.Gamepad pad)
            {
                if (pad.dpad.left.wasPressedThisFrame) keys.Move.x = -1;
                else if (pad.dpad.right.wasPressedThisFrame) keys.Move.x = 1;
                else if (pad.dpad.up.wasPressedThisFrame) keys.Move.y = -1;
                else if (pad.dpad.down.wasPressedThisFrame) keys.Move.y = 1;
                if (ControllerSupport.HighlightMode && ControllerSupport.WasPressed(PadAction.Select)) keys.Enter = true;
            }
            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
            {
                if (!(device is UnityEngine.InputSystem.Keyboard k)) continue;
                if (k.leftArrowKey.wasPressedThisFrame) keys.Move.x = -1;
                else if (k.rightArrowKey.wasPressedThisFrame) keys.Move.x = 1;
                else if (k.upArrowKey.wasPressedThisFrame) keys.Move.y = -1;
                else if (k.downArrowKey.wasPressedThisFrame) keys.Move.y = 1;
                if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame) keys.Enter = true;
            }
#else
            if (Input.GetKeyDown(KeyCode.LeftArrow)) keys.Move.x = -1;
            else if (Input.GetKeyDown(KeyCode.RightArrow)) keys.Move.x = 1;
            else if (Input.GetKeyDown(KeyCode.UpArrow)) keys.Move.y = -1;
            else if (Input.GetKeyDown(KeyCode.DownArrow)) keys.Move.y = 1;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) keys.Enter = true;
#endif
            if (keys.Move != Vector2Int.zero && keys.Move.x != 0) keys.Move.y = 0; // one direction a press
            return keys;
        }

        /// <summary>Whether a text field has the keyboard (the shop's search, the trader's).</summary>
        public static bool TypingSomewhere()
        {
            // Unity-null checks: the selected object may have just been destroyed.
            var events = EventSystem.current;
            if (events == null) return false;
            var selected = events.currentSelectedGameObject;
            if (selected == null) return false;
            var field = selected.GetComponent<InputField>();
            return field != null && field.isFocused;
        }

        private static readonly List<RaycastResult> Hits = new List<RaycastResult>();

        /// <summary>Whether the topmost thing at <paramref name="probe"/>'s center is under <paramref name="owner"/> (and not <paramref name="except"/>).</summary>
        public static bool InFront(RectTransform owner, RectTransform probe, RectTransform? except = null)
        {
            var events = EventSystem.current;
            if (events == null || owner == null || probe == null || !owner.gameObject.activeInHierarchy) return false;
            var center = RectTransformUtility.WorldToScreenPoint(null, probe.TransformPoint(probe.rect.center));
            Hits.Clear();
            events.RaycastAll(new PointerEventData(events) { position = center }, Hits);
            if (Hits.Count == 0) return false;
            var top = Hits[0].gameObject.transform;
            return top.IsChildOf(owner) && (except == null || !except.gameObject.activeInHierarchy || !top.IsChildOf(except));
        }

        /// <summary>
        /// A button under <paramref name="owner"/> keeps the UI's selection after it's clicked, and the UI would press it again
        /// on Enter (or move to its neighbour on an arrow): the selection goes. A text field keeps it (it's where you type).
        /// Called every frame a screen is open, so a click never leaves its button armed for the keyboard.
        /// </summary>
        public static void DropButtonFocus(RectTransform owner)
        {
            var events = EventSystem.current;
            if (events == null) return;
            var selected = events.currentSelectedGameObject;
            if (selected == null || owner == null || !selected.transform.IsChildOf(owner)) return;
            if (selected.GetComponent<InputField>() != null) return;
            events.SetSelectedGameObject(null);
        }

        /// <summary>
        /// The thing to move to from <paramref name="from"/> in a direction, among things laid out at <paramref name="points"/>
        /// (y down, the layout's own units): left/right, the nearest in the same row (within <paramref name="rowTolerance"/>);
        /// up/down, the nearest row that way, and in it the closest across. -1 when there's nothing that way (stay put).
        /// </summary>
        public static int Step(IReadOnlyList<Vector2> points, int from, Vector2Int move, float rowTolerance)
        {
            if (from < 0 || from >= points.Count || move == Vector2Int.zero) return -1;
            var here = points[from];
            int best = -1;
            float bestAlong = float.MaxValue, bestAcross = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                if (i == from) continue;
                var d = points[i] - here;
                if (move.x != 0)
                {
                    if (Mathf.Abs(d.y) > rowTolerance) continue; // another row
                    float along = d.x * move.x;
                    if (along <= 0.5f || along >= bestAlong) continue;
                    best = i;
                    bestAlong = along;
                }
                else
                {
                    float along = d.y * move.y;
                    if (along <= rowTolerance) continue; // this row, or the other way
                    float across = Mathf.Abs(d.x);
                    bool nearerRow = along < bestAlong - rowTolerance;
                    bool sameRow = Mathf.Abs(along - bestAlong) <= rowTolerance;
                    if (!nearerRow && !(sameRow && across < bestAcross)) continue;
                    best = i;
                    bestAlong = along;
                    bestAcross = across;
                }
            }
            return best;
        }

        /// <summary>
        /// A list's next row from <paramref name="from"/> one way (<paramref name="delta"/> -1 or +1), skipping rows that
        /// can't be highlighted (headers); -1 at either end. From nowhere (-1), the first (or last) row that can.
        /// </summary>
        public static int NextRow(int count, int from, int delta, System.Func<int, bool> canHighlight)
        {
            int i = from < 0 ? (delta > 0 ? -1 : count) : from;
            for (i += delta; i >= 0 && i < count; i += delta)
                if (canHighlight(i)) return i;
            return -1;
        }

        /// <summary>
        /// A list's page turned from <paramref name="page"/> by <paramref name="delta"/> (left/right): the first row on that
        /// page that can be highlighted, or -1 when there's no such page (or nothing on it).
        /// </summary>
        public static int PageRow(int count, int perPage, int page, int delta, System.Func<int, bool> canHighlight)
        {
            page += delta;
            if (page < 0 || page * perPage >= count) return -1;
            for (int i = page * perPage; i < count && i < (page + 1) * perPage; i++)
                if (canHighlight(i)) return i;
            return -1;
        }

        /// <summary>
        /// A paged list's arrow: up/down, the next row that can be highlighted from <paramref name="at"/> (across pages), or
        /// when the highlighted row isn't on the page shown, the page's first (down) or last (up) one; left/right, the first
        /// row of the page before or after. -1: nothing that way (stay put).
        /// </summary>
        public static int ListStep(int count, int perPage, int page, int at, Vector2Int move, System.Func<int, bool> canHighlight)
        {
            if (move.x != 0) return PageRow(count, perPage, page, move.x, canHighlight);
            if (move.y == 0) return -1;
            bool here = at >= 0 && at / perPage == page;
            if (here) return NextRow(count, at, move.y, canHighlight);
            int from = move.y > 0 ? page * perPage - 1 : System.Math.Min(count, (page + 1) * perPage);
            int next = NextRow(count, from, move.y, canHighlight);
            return next;
        }

        /// <summary>
        /// A gold frame behind a card (<paramref name="marker"/>, made on first use under <paramref name="parent"/>) to show the
        /// keyboard's highlight; hidden when <paramref name="target"/> is null.
        /// </summary>
        public static void Mark(ref RectTransform? marker, RectTransform parent, RectTransform? target, float border = 8)
        {
            if (target == null)
            {
                if (marker != null) marker.gameObject.SetActive(false);
                return;
            }
            if (marker == null)
            {
                marker = Ui.Rect("KeyHighlight", parent);
                var image = Ui.Image(marker, UiTheme.Current.Gold);
                image.raycastTarget = false;
                marker.gameObject.AddComponent<KeyFrame>(); // hidden while the mouse is in use
            }
            marker.gameObject.SetActive(true);
            marker.SetParent(target.parent, false);
            marker.anchorMin = target.anchorMin;
            marker.anchorMax = target.anchorMax;
            marker.pivot = target.pivot;
            // Grown by the border on every side, whatever the target's pivot (a top-left one, the van's tiles, included).
            marker.anchoredPosition = target.anchoredPosition + new Vector2(border * (2 * target.pivot.x - 1), border * (2 * target.pivot.y - 1));
            marker.sizeDelta = target.sizeDelta + new Vector2(border * 2, border * 2);
            marker.localRotation = Quaternion.identity;
            marker.localScale = Vector3.one;
            // Just behind the card, so only the border shows (last first, so the card's index is read after the move).
            marker.SetAsLastSibling();
            marker.SetSiblingIndex(target.GetSiblingIndex());
        }
    }
    /// <summary>
    /// The keyboard's (and the d-pad's) way to buy: Enter on a highlighted pack or card opens this small question over the
    /// screen ("Buy X for N DP?"); a second Enter (or the controller's select) does it, Escape (or the back button) cancels,
    /// and its two buttons click too. The press that opens it never also answers it. The action is the screen's own buy
    /// (the same path as its Buy button), so no new way to spend DP.
    /// </summary>
    public sealed class KeyConfirm
    {
        private RectTransform? _root;
        private Action? _yes, _pending;
        private int _openedFrame, _pendingFrame;
        private Text? _question;
        private Button? _ok, _cancel;
        /// <summary>Whether this confirm counts in <see cref="AnyOpen"/> (kept apart from _root, which Unity can destroy under it).</summary>
        private bool _isOpen;

        /// <summary>Whether a confirm is up anywhere (ControllerSupport: the back button is its cancel then, not the screen's Back).</summary>
        public static bool AnyOpen => _open > 0 || Time.frameCount - _closedFrame <= 1;
        private static int _open, _closedFrame = -10;

        public bool IsOpen => _isOpen && _root != null;

        /// <summary>The question as shown ("" when closed), for checks.</summary>
        public string Question => IsOpen && _question != null ? _question.text : "";

        /// <summary>For checks: the screen point at the centre of its yes (or Cancel) button; null when closed.</summary>
        public Vector2? ButtonPoint(bool yes) => IsOpen && (yes ? _ok : _cancel) is Button b && b != null
            ? RectTransformUtility.WorldToScreenPoint(null, ((RectTransform)b.transform).TransformPoint(((RectTransform)b.transform).rect.center)) : (Vector2?)null;

        /// <summary>Opens the question over <paramref name="parent"/> (stretched across it); <paramref name="yes"/> runs on a second Enter or a click.</summary>
        public void Open(RectTransform parent, string question, string yesLabel, Action yes)
        {
            Close();
            var T = UiTheme.Current;
            _root = Ui.Stretched("KeyConfirm", parent, Vector2.zero, Vector2.one);
            _root.SetAsLastSibling();
            Ui.Image(_root, new Color(0, 0, 0, 0.55f)); // blocks clicks to the screen under it
            var panel = Ui.Placed("Panel", _root, Vector2.zero, new Vector2(760, 240));
            Ui.Image(panel, T.Panel);
            Ui.Image(Ui.Placed("Edge", panel, new Vector2(0, 118), new Vector2(760, 4)), T.Gold).raycastTarget = false;
            _question = Ui.Text(Ui.Placed("Question", panel, new Vector2(0, 40), new Vector2(700, 110)), question, 24, T.Text);
            _question.supportRichText = true;
            _question.resizeTextForBestFit = true;
            _question.resizeTextMinSize = 16;
            _question.resizeTextMaxSize = 24;
            _ok = Ui.Button(panel, $"{yesLabel}  (Enter)", new Vector2(-170, -70), new Vector2(320, 60), Confirm);
            _ok.targetGraphic.color = T.Primary;
            _cancel = Ui.Button(panel, "Cancel  (Esc)", new Vector2(170, -70), new Vector2(320, 60), Close);
            _cancel.targetGraphic.color = T.Button;
            _yes = yes;
            _openedFrame = Time.frameCount;
            _isOpen = true;
            _open++;
        }

        public void Close()
        {
            if (!_isOpen) return;
            _isOpen = false;
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
            _yes = null;
            _open = Math.Max(0, _open - 1);
            _closedFrame = Time.frameCount; // the back button that closed it doesn't also go back a screen
        }

        /// <summary>Its yes button clicked: done now (a click isn't a key a screen opened by it could also read).</summary>
        private void Confirm()
        {
            var yes = _yes;
            Close();
            yes?.Invoke();
        }

        /// <summary>
        /// Call every frame from the screen's Update before its own keys: true while the question is up (the screen reads
        /// nothing else then). Enter (or the controller's select while it drives the highlight) answers yes, Escape (or the
        /// back button) no; never on the frame it opened. A yes by key runs on the next frame, so a screen it opens (a story
        /// scene after a buy-back) doesn't read the same Enter. With the stick's cursor, the buttons take the click instead.
        /// </summary>
        public bool Update()
        {
            if (_pending != null)
            {
                if (Time.frameCount <= _pendingFrame) return true;
                var run = _pending;
                _pending = null;
                run();
                return true;
            }
            if (!_isOpen) return false;
            if (_root == null || !_root.gameObject.activeInHierarchy) { Close(); return false; } // its screen closed under it
            if (Time.frameCount == _openedFrame) return true;
            bool yes = false, no = false;
#if ENABLE_INPUT_SYSTEM
            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
            {
                if (!(device is UnityEngine.InputSystem.Keyboard k)) continue;
                yes |= k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame;
                no |= k.escapeKey.wasPressedThisFrame;
            }
            if (UnityEngine.InputSystem.Gamepad.current != null)
            {
                // A is yes only while the d-pad drives the highlight; aimed with the stick, A clicks whichever button it's on.
                yes |= ControllerSupport.HighlightMode && ControllerSupport.WasPressed(PadAction.Select);
                no |= ControllerSupport.WasPressed(PadAction.Back);
            }
#else
            yes = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space);
            no = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (no) Close();
            else if (yes)
            {
                _pending = _yes;
                _pendingFrame = Time.frameCount;
                Close();
            }
            return true;
        }
    }
}
