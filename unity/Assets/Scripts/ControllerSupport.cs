#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Game controller support (Xbox, PlayStation, Switch Pro...) through a virtual mouse, so every screen's clicks,
    /// right-clicks, drags, and hover details work unchanged:
    /// left stick moves the cursor, the d-pad jumps it to the nearest clickable thing in that direction,
    /// A clicks (hold and move to drag), X right-clicks, B goes back, LB/RB change pages, Start pauses (the buttons can
    /// be remapped in Settings; see <see cref="ControllerBindings"/>).
    /// The cursor appears when the controller is used and hides when the real mouse moves.
    /// On a screen with a keyboard highlight (the shop, its sub-screens, Moth's van: <see cref="ArrowNav"/>) the d-pad drives
    /// that highlight instead of jumping the cursor, and A answers it as Enter does (the cursor hides) until the stick moves.
    /// </summary>
    public sealed class ControllerSupport : MonoBehaviour
    {
        private const float CursorSpeed = 1500f; // reference pixels per second at full tilt (1080p)
        private const float StickDeadZone = 0.15f;

        private Mouse? _virtualMouse;
        private Vector2 _position;
        private Vector2 _lastSent;
        private bool _active;
        private RectTransform _cursor = null!;
        private GameObject _legend = null!;

        /// <summary>B: cancel / close / back. LB/RB: previous / next page (-1 / +1). Start: pause.</summary>
        public event Action? Back, Pause;
        public event Action<int>? Page;

        public static ControllerSupport? Instance { get; private set; }

        public bool IsActive => _active;

        /// <summary>
        /// The d-pad drives a screen's keyboard highlight (ArrowNav), so the select button is that screen's Enter and the
        /// virtual mouse doesn't click; off again once the stick (or the real mouse) moves.
        /// </summary>
        public static bool HighlightMode { get; private set; }

        /// <summary>Whether an action's button was pressed this frame on the current controller.</summary>
        public static bool WasPressed(PadAction action) => Gamepad.current is Gamepad pad && Pressed(pad, action);

        /// <summary>A screen's own button hints (the deck editor's), shown instead of the general ones; null for those.</summary>
        public static string? Legend { get; set; }
        private Text _legendText = null!;

        /// <summary>Y: the screen's extra action (the deck editor: favorite).</summary>
        public event Action? North;
        /// <summary>View: the next explained term in the card detail's text.</summary>
        public event Action? Terms;
        public Vector2 CursorPosition => _position;

        /// <summary>
        /// How much of the screen's right side (reference pixels) a screen keeps for a column of its own (the duel's side
        /// panel or watch bar): the button hints stay left of it. Null or 0: the whole width.
        /// </summary>
        public static Func<float>? ReservedRight { get; set; }

        /// <summary>Until when a check's <see cref="DebugMoveTo"/> keeps the cursor up without a controller.</summary>
        private float _debugUntil = -1f;

        private void Start()
        {
            Instance = this;
            _virtualMouse = InputSystem.AddDevice<Mouse>("ControllerCursor");
            _position = new Vector2(Screen.width / 2f, Screen.height / 2f);
            BuildOverlay();
            SetActive(false);
            // Adding a mouse makes it the current one: the real mouse is the current one until the controller is used.
            foreach (var device in InputSystem.devices)
                if (device is Mouse m && m != _virtualMouse) { m.MakeCurrent(); break; }
        }

        private void OnDestroy()
        {
            if (_virtualMouse != null && _virtualMouse.added) InputSystem.RemoveDevice(_virtualMouse);
        }

        private void BuildOverlay()
        {
            var canvasGo = new GameObject("ControllerCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // above every screen
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // the whole 1920x1080 layout stays visible at any aspect ratio

            // A small gold diamond with a dark edge; its center is the click point.
            _cursor = Ui.Rect("Cursor", canvasGo.transform);
            _cursor.anchorMin = _cursor.anchorMax = Vector2.zero;
            _cursor.pivot = new Vector2(0.5f, 0.5f);
            _cursor.sizeDelta = new Vector2(26, 26);
            _cursor.localRotation = Quaternion.Euler(0, 0, 45);
            Ui.Image(_cursor, new Color(0.08f, 0.08f, 0.1f, 0.9f)).raycastTarget = false;
            var inner = Ui.Stretched("Inner", _cursor, Vector2.zero, Vector2.one);
            inner.offsetMin = new Vector2(4, 4);
            inner.offsetMax = new Vector2(-4, -4);
            Ui.Image(inner, Ui.Hex("#FFC940")).raycastTarget = false;

            var legend = Ui.Rect("Legend", canvasGo.transform);
            legend.anchorMin = legend.anchorMax = new Vector2(0.5f, 0);
            legend.pivot = new Vector2(0.5f, 0);
            legend.anchoredPosition = new Vector2(0, 4);
            legend.sizeDelta = new Vector2(1100, 26);
            Ui.Image(legend, new Color(0, 0, 0, 0.7f)).raycastTarget = false;
            _legendText = Ui.Text(legend, ControllerBindings.Legend(), 15, new Color(1, 1, 1, 0.85f));
            _legendText.resizeTextForBestFit = true; // shrinks when a narrower strip has to hold it
            _legendText.resizeTextMinSize = 11;
            _legendText.resizeTextMaxSize = 15;
            _legend = legend.gameObject;
        }

        private void Update()
        {
            if (_virtualMouse == null) return;
            var pad = Gamepad.current;
            if (pad != null && !pad.enabled) pad = null; // switched off while the window is in the background (ClickSupport)

            // Real mouse movement hands control back to the mouse.
            foreach (var device in InputSystem.devices)
            {
                if (device is Mouse mouse && mouse != _virtualMouse && mouse.delta.ReadValue().sqrMagnitude > 1f)
                {
                    HighlightMode = false;
                    SetActive(false);
                    break;
                }
            }
            if (pad == null)
            {
                if (ControllerBindings.Capturing != null && Time.unscaledTime > ControllerBindings.CaptureUntil) ControllerBindings.EndCapture(null);
                // No controller: the cursor and hints go (a check that moved it keeps them a few seconds).
                if (_active && Time.unscaledTime > _debugUntil) SetActive(false);
                if (_active) PlaceLegend();
                return;
            }

            // Remapping (Settings): the next button pressed is bound, and does nothing else this frame.
            if (ControllerBindings.Capturing != null)
            {
                var pressed = ControllerBindings.Buttons.FirstOrDefault(b => Button(pad, b)?.wasPressedThisFrame == true);
                if (pressed != null || Time.unscaledTime > ControllerBindings.CaptureUntil)
                {
                    ControllerBindings.EndCapture(pressed);
                    _swallow = pressed;
                }
                return;
            }
            // The button just bound is ignored until it's let go (so binding Select doesn't also click).
            if (_swallow != null)
            {
                if (Button(pad, _swallow)?.isPressed == true) return;
                _swallow = null;
            }

            var stick = pad.leftStick.ReadValue();
            // The stick counts only once it has come back to rest since the cursor last went: a stick that drifts past the
            // dead zone on its own never wakes the cursor (nor pulls the UI's pointer away from the real mouse).
            if (stick.magnitude <= StickDeadZone) _stickRested = true;
            bool used = (_stickRested && stick.magnitude > StickDeadZone) || pad.wasUpdatedThisFrame && AnyButtonPressed(pad);
            if (used && !_active)
            {
                SetActive(true);
                _position = _lastSent = Mouse.current != null && Mouse.current != _virtualMouse ? Mouse.current.position.ReadValue() : _position;
            }
            if (!_active) return;

            // Stick: faster the further it's pushed (squared response for fine control near the center).
            if (stick.magnitude > StickDeadZone)
            {
                var scale = Screen.height / 1080f;
                _position += stick * stick.magnitude * CursorSpeed * scale * Time.unscaledDeltaTime;
            }
            // The stick takes the cursor back; on a screen with a keyboard highlight the d-pad drives that highlight
            // (ArrowNav reads it), elsewhere it jumps the cursor to the nearest clickable thing.
            bool dpad = pad.dpad.right.wasPressedThisFrame || pad.dpad.left.wasPressedThisFrame || pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame;
            // A confirm over the screen keeps the highlight (the screen doesn't read its keys while it's up).
            if (stick.magnitude > StickDeadZone || !(ArrowNav.ScreenInFront || KeyConfirm.AnyOpen)) HighlightMode = false;
            if (dpad && ArrowNav.ScreenInFront) HighlightMode = true;
            else
            {
                if (pad.dpad.right.wasPressedThisFrame) Snap(Vector2.right);
                if (pad.dpad.left.wasPressedThisFrame) Snap(Vector2.left);
                if (pad.dpad.up.wasPressedThisFrame) Snap(Vector2.up);
                if (pad.dpad.down.wasPressedThisFrame) Snap(Vector2.down);
            }
            if (_cursor != null) _cursor.gameObject.SetActive(!HighlightMode);
            _position = new Vector2(Mathf.Clamp(_position.x, 0, Screen.width - 1), Mathf.Clamp(_position.y, 0, Screen.height - 1));

            // Driving the highlight, the select button is the screen's Enter (ArrowNav, KeyConfirm), not a click under the cursor;
            // pressed then, it never becomes a click until it's let go (were the highlight to end while it's held).
            if (HighlightMode && Pressed(pad, PadAction.Select)) _selectFromHighlight = true;
            if (!Held(pad, PadAction.Select)) _selectFromHighlight = false;
            Send(!HighlightMode && !_selectFromHighlight && Held(pad, PadAction.Select), !HighlightMode && Held(pad, PadAction.RightClick));
            _cursor.position = _position;

            // With a confirm up (KeyConfirm), the back button is its Cancel, not the screen's Back.
            if (Pressed(pad, PadAction.Back) && !KeyConfirm.AnyOpen) Back?.Invoke();
            if (Pressed(pad, PadAction.PagePrev)) Page?.Invoke(-1);
            if (Pressed(pad, PadAction.PageNext)) Page?.Invoke(+1);
            if (Pressed(pad, PadAction.Pause)) Pause?.Invoke();
            if (Pressed(pad, PadAction.Extra)) North?.Invoke();
            if (Pressed(pad, PadAction.Terms)) Terms?.Invoke();
            if (_legendText != null) _legendText.text = Legend ?? ControllerBindings.Legend();
            PlaceLegend();
        }

        /// <summary>The hints along the bottom, centered in the room left of any reserved right column (12 px clear of it).</summary>
        private void PlaceLegend()
        {
            if (_legend == null) return;
            var rt = (RectTransform)_legend.transform;
            float canvasWidth = ((RectTransform)rt.parent).rect.width;
            float reserved = Mathf.Max(0, ReservedRight?.Invoke() ?? 0);
            float room = reserved > 0 ? canvasWidth - reserved - 24 : canvasWidth - 24;
            rt.sizeDelta = new Vector2(Mathf.Clamp(room, 200, 1100), rt.sizeDelta.y);
            // Centered between the left edge and the column (the anchor is the screen's bottom center).
            rt.anchoredPosition = new Vector2(-reserved / 2, rt.anchoredPosition.y);
        }

        /// <summary>The stick has been at rest since the cursor last went (see Update).</summary>
        private bool _stickRested;

        private static bool AnyButtonPressed(Gamepad pad)
        {
            foreach (var control in pad.allControls)
            {
                if (control is not UnityEngine.InputSystem.Controls.ButtonControl b || !b.wasPressedThisFrame) continue;
                // A stick's own direction "buttons" (up, down, left, right) are the stick: it wakes the cursor only from rest.
                if (b.parent is UnityEngine.InputSystem.Controls.StickControl) continue;
                return true;
            }
            return false;
        }

        private string? _swallow;
        /// <summary>The select button went down while the d-pad drove the highlight: no virtual click until it's released.</summary>
        private bool _selectFromHighlight;

        private static UnityEngine.InputSystem.Controls.ButtonControl? Button(Gamepad pad, string name) =>
            pad.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>(name);

        private static bool Held(Gamepad pad, PadAction action) => Button(pad, ControllerBindings.Get(action))?.isPressed == true;
        private static bool Pressed(Gamepad pad, PadAction action) => Button(pad, ControllerBindings.Get(action))?.wasPressedThisFrame == true;

        /// <summary>Whether an action's button is held on the current controller (the ask-to-chain button).</summary>
        public static bool IsHeld(PadAction action) => Gamepad.current is Gamepad pad && Held(pad, action);

        /// <summary>
        /// Puts the cursor on a point (screen pixels) if the controller is in use: focus going back to a card after a
        /// cancel. With the mouse, nothing moves.
        /// </summary>
        public void FocusAt(Vector2 screen)
        {
            if (!_active) return;
            _position = screen;
            _cursor.position = _position;
        }

        /// <summary>Feeds the virtual mouse: the UI treats it like a real mouse (hover, click, right-click, drag).</summary>
        private void Send(bool left, bool right)
        {
            var state = new MouseState { position = _position, delta = _position - _lastSent }
                .WithButton(MouseButton.Left, left)
                .WithButton(MouseButton.Right, right);
            InputState.Change(_virtualMouse!, state);
            _lastSent = _position;
        }

        private void SetActive(bool on)
        {
            // Off, the virtual mouse is switched off too: an idle controller never feeds the UI's pointer.
            if (_virtualMouse != null && _virtualMouse.added && _virtualMouse.enabled != on)
            {
                if (on) InputSystem.EnableDevice(_virtualMouse);
                else
                {
                    InputSystem.DisableDevice(_virtualMouse);
                    foreach (var device in InputSystem.devices)
                        if (device is Mouse m && m != _virtualMouse && m.enabled) { m.MakeCurrent(); break; }
                }
            }
            if (!on) _stickRested = false;
            _active = on;
            Cursor.visible = !on;
            if (_cursor != null) _cursor.gameObject.SetActive(on);
            if (_legend != null) _legend.SetActive(on);
        }

        /// <summary>
        /// Jumps to the nearest clickable thing in a direction: anything with a click, drag, or button handler
        /// that is actually visible at its center (not hidden under another screen).
        /// </summary>
        private void Snap(Vector2 direction)
        {
            var best = (score: float.MaxValue, point: _position);
            foreach (var target in Targets())
            {
                var offset = target - _position;
                var along = Vector2.Dot(offset, direction);
                if (along < 12f) continue; // must be in that direction
                var across = Mathf.Abs(offset.x * direction.y - offset.y * direction.x);
                if (across > along) continue; // within a 45° cone, so "left" never lands mostly up or down
                var score = along + 2.5f * across; // prefer things straight ahead
                if (score < best.score) best = (score, target);
            }
            _position = best.point;
        }

        private List<Vector2> Targets()
        {
            var points = new List<Vector2>();
            var seen = new HashSet<Component>();
            var hits = new List<RaycastResult>();
            foreach (var graphic in FindObjectsByType<Graphic>(FindObjectsSortMode.None))
            {
                if (!graphic.raycastTarget || !graphic.isActiveAndEnabled) continue;
                // Unity objects need explicit null checks (a destroyed object isn't C# null).
                Component? handler = graphic.GetComponentInParent<IPointerClickHandler>() as Component;
                if (handler == null) handler = graphic.GetComponentInParent<IBeginDragHandler>() as Component;
                if (handler == null) handler = graphic.GetComponentInParent<Selectable>();
                if (handler == null || !seen.Add(handler)) continue;
                var rt = (RectTransform)handler.transform;
                var center = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
                if (center.x < 0 || center.y < 0 || center.x > Screen.width || center.y > Screen.height) continue;
                // Only things the cursor could actually click there (not covered by another screen).
                hits.Clear();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = center }, hits);
                if (hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(handler.transform)) points.Add(center);
            }
            return points;
        }

        /// <summary>For automated checks: put the cursor somewhere (screen pixels) and make the controller active.</summary>
        public void DebugMoveTo(Vector2 screen)
        {
            SetActive(true);
            _debugUntil = Time.unscaledTime + 10f;
            _position = screen;
        }
    }
}
#endif
