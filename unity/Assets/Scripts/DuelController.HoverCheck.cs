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

namespace YgoCampaign.Game
{
    /// <summary>
    /// Hover in the checks (docs/dev/HOVER-DESIGN-2026-11-07.md). `-layoutcheck` ends with <see cref="HoverCheck"/>: in both
    /// themes a test Mouse hovers a built button (its fill goes to the hover token, the hit area and layout unchanged, a
    /// press darkens it and draws it at 0.97 without moving its rect), a disabled button (no change), Reduced motion
    /// (colour only), and the keyboard's gold frame (hidden when the mouse moves, back on an arrow key). `-themecheck`
    /// shoots a hovered button, a row, a map node and a van slab in each theme (<see cref="HoverThemeShots"/>).
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>Moves a test mouse to <paramref name="at"/> (screen pixels), the left button as given, and waits a few frames.</summary>
        private static IEnumerator MouseTo(Mouse mouse, Vector2 at, bool down = false, float wait = 0.2f)
        {
            var state = new MouseState { position = at };
            if (down) state = state.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(mouse, state);
            yield return null;
            yield return new WaitForSecondsRealtime(wait);
        }

        private static Vector2 CenterOf(RectTransform rt) => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));

        private static bool NearColor(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.012f && Mathf.Abs(a.g - b.g) < 0.012f && Mathf.Abs(a.b - b.b) < 0.012f && Mathf.Abs(a.a - b.a) < 0.012f;

        private static string HexOf(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        private IEnumerator HoverCheck(string folder, Action<string, bool> report)
        {
            var mouse = InputSystem.AddDevice<Mouse>("HoverCheckMouse");
            var keys = InputSystem.AddDevice<Keyboard>("HoverCheckKeys");
            var reduced0 = FxSettings.ReducedMotionOverride;
            var theme0 = UiTheme.Current;
            try
            {
                yield return MouseTo(mouse, new Vector2(4, 4));
                foreach (var (name, theme) in new[] { ("main-street", UiTheme.MainStreet), ("back-alley", UiTheme.BackAlley) })
                {
                    UiTheme.Current = theme;
                    var T = theme;
                    FxSettings.ReducedMotionOverride = false;
                    var root = Ui.Overlay(transform, "HoverCheck", 95);
                    Ui.Image(root, T.Base);
                    var on = Ui.BoxButton(root, 760, 300, 400, 64, "Hover me", () => { }, 20);
                    var off = Ui.BoxButton(root, 760, 420, 400, 64, "Disabled", () => { }, 20);
                    off.interactable = false;
                    var primary = Ui.BoxButton(root, 760, 540, 400, 64, "Primary", () => { }, 20);
                    primary.targetGraphic.color = T.Primary;
                    var target = Ui.Box(root, 760, 700, 400, 64);
                    Ui.Image(target, T.RowA);
                    RectTransform? mark = null;
                    ArrowNav.Mark(ref mark, root, target, 6);
                    yield return new WaitForSecondsRealtime(0.2f);

                    // Hover: the fill goes 10% toward the text colour; the rect, its scale and the layout stay as they were.
                    var onRt = (RectTransform)on.transform;
                    var rest = on.targetGraphic.color;
                    var (pos0, size0, scale0) = (onRt.anchoredPosition, onRt.sizeDelta, onRt.localScale);
                    yield return MouseTo(mouse, CenterOf(onRt));
                    var hovered = on.targetGraphic.color;
                    var want = Color.Lerp(rest, T.Text, T.HoverMix);
                    want.a = 1;
                    var edge = onRt.GetComponentInChildren<HoverEdge>();
                    report($"{name}: a hovered button's fill goes {HexOf(rest)} -> {HexOf(hovered)} (the hover token: {HexOf(want)}), with its 1 px edge ({(edge != null ? edge.color.a.ToString("0.00") : "none")})",
                        NearColor(hovered, want) && !NearColor(hovered, rest) && edge != null && Mathf.Abs(edge.color.a - T.HoverEdge.a) < 0.02f);
                    report($"{name}: hovering moves nothing (position, size and scale of the rect the same)",
                        onRt.anchoredPosition == pos0 && onRt.sizeDelta == size0 && onRt.localScale == scale0);
                    yield return HoverShot(folder, $"hover-{name}-button");
                    // Pressed: darker at once, drawn at 0.97, the hit rect untouched.
                    yield return MouseTo(mouse, CenterOf(onRt), down: true, wait: 0.05f);
                    var pressed = on.targetGraphic.color;
                    var hover = on.GetComponent<UiHover>();
                    report($"{name}: pressed, the fill darkens ({HexOf(pressed)}) and is drawn at {hover.DrawnScale:0.00} while its rect keeps scale {onRt.localScale.x:0.00} and size {onRt.sizeDelta}",
                        pressed.r < hovered.r && Mathf.Abs(hover.DrawnScale - T.PressScale) < 0.005f && onRt.localScale == scale0 && onRt.sizeDelta == size0);
                    yield return MouseTo(mouse, CenterOf(onRt));

                    // A disabled button doesn't react.
                    var offRest = off.targetGraphic.color;
                    yield return MouseTo(mouse, CenterOf((RectTransform)off.transform));
                    var offHovered = off.targetGraphic.color;
                    report($"{name}: a disabled button doesn't change when hovered ({HexOf(offRest)} -> {HexOf(offHovered)}), and the one left goes back to its rest ({HexOf(on.targetGraphic.color)})",
                        NearColor(offHovered, offRest) && NearColor(on.targetGraphic.color, rest));

                    // An accent fill: the gentler 6%.
                    var primaryRest = primary.targetGraphic.color;
                    yield return MouseTo(mouse, CenterOf((RectTransform)primary.transform));
                    var accentWant = Color.Lerp(primaryRest, T.Text, T.AccentHoverMix);
                    accentWant.a = 1;
                    report($"{name}: a Primary button lightens 6% ({HexOf(primaryRest)} -> {HexOf(primary.targetGraphic.color)}, token {HexOf(accentWant)})",
                        NearColor(primary.targetGraphic.color, accentWant));

                    // Reduced motion: the colour still changes, nothing scales.
                    FxSettings.ReducedMotionOverride = true;
                    yield return MouseTo(mouse, CenterOf(onRt), down: true, wait: 0.05f);
                    report($"{name}: with Reduced motion a press changes the colour ({HexOf(on.targetGraphic.color)}) but draws no scale ({hover.DrawnScale:0.00})",
                        !NearColor(on.targetGraphic.color, rest) && Mathf.Approximately(hover.DrawnScale, 1));
                    yield return MouseTo(mouse, CenterOf(onRt));
                    FxSettings.ReducedMotionOverride = false;

                    // Last input wins: an arrow shows the gold frame and turns hover off; the mouse moving hides it again.
                    // The press left the button selected (Unity selects on mouse-down): an arrow mustn't light it up as focus.
                    var frame = mark!.GetComponent<KeyFrame>();
                    report($"{name}: with the mouse in use the keyboard's gold frame is hidden ({frame.Shown})", InputMode.Pointer && !frame.Shown);
                    InputSystem.QueueStateEvent(keys, new KeyboardState(Key.RightArrow));
                    yield return null;
                    yield return null;
                    InputSystem.QueueStateEvent(keys, new KeyboardState());
                    yield return new WaitForSecondsRealtime(0.2f);
                    report($"{name}: an arrow key brings the frame back ({frame.Shown}) and neither the hover under the resting mouse nor the clicked (selected) button lights up (fill {HexOf(on.targetGraphic.color)}, selected {EventSystem.current?.currentSelectedGameObject == on.gameObject})",
                        !InputMode.Pointer && frame.Shown && NearColor(on.targetGraphic.color, rest));
                    yield return HoverShot(folder, $"hover-{name}-keys");
                    yield return MouseTo(mouse, CenterOf(onRt) + new Vector2(12, 0));
                    report($"{name}: the mouse moving hides the frame again ({frame.Shown}) and the hover is back (fill {HexOf(on.targetGraphic.color)})",
                        InputMode.Pointer && !frame.Shown && NearColor(on.targetGraphic.color, want));
                    InputSystem.QueueStateEvent(keys, new KeyboardState(Key.DownArrow));
                    yield return null;
                    yield return null;
                    InputSystem.QueueStateEvent(keys, new KeyboardState());
                    yield return new WaitForSecondsRealtime(0.1f);
                    report($"{name}: and the next arrow shows it once more ({frame.Shown})", frame.Shown);
                    yield return MouseTo(mouse, new Vector2(4, 4));
                    Destroy(root.GetComponentInParent<Canvas>().gameObject);
                    yield return null;
                }
            }
            finally
            {
                FxSettings.ReducedMotionOverride = reduced0;
                UiTheme.Current = theme0;
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(keys);
                InputMode.DebugReset();
            }
        }

        private static IEnumerator HoverShot(string folder, string name)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"layout-{name}.png"));
            yield return null;
            yield return null;
        }

        private static readonly List<RaycastResult> HoverHits = new List<RaycastResult>();

        /// <summary>The first hover of a style under <paramref name="root"/> that's on screen and on top at its centre (and passes <paramref name="where"/>).</summary>
        private static UiHover? FindHover(Transform root, HoverStyle style, Func<UiHover, bool>? where = null)
        {
            var events = EventSystem.current;
            if (events == null) return null;
            foreach (var h in root.GetComponentsInChildren<UiHover>())
            {
                if (h.Style != style || !h.isActiveAndEnabled || (where != null && !where(h))) continue;
                var p = CenterOf((RectTransform)h.transform);
                if (p.x < 10 || p.y < 10 || p.x > Screen.width - 10 || p.y > Screen.height - 10) continue;
                HoverHits.Clear();
                events.RaycastAll(new PointerEventData(events) { position = p }, HoverHits);
                if (HoverHits.Count > 0 && HoverHits[0].gameObject.transform.IsChildOf(h.transform)) return h;
            }
            return null;
        }

        /// <summary>
        /// The theme check's hover shots, in theme <paramref name="name"/>: a hovered header button and map node (the circuit
        /// map), a shop row and a slab on Moth's van's Shelf, each with the test mouse on it.
        /// </summary>
        private IEnumerator HoverThemeShots(string name, UiTheme theme, Func<string, IEnumerator> shot, Action<string, bool> report)
        {
            var mouse = InputSystem.AddDevice<Mouse>("HoverShotMouse");
            var keys = InputSystem.AddDevice<Keyboard>("HoverShotKeys");
            var found = new List<string>();
            try
            {
                yield return MouseTo(mouse, new Vector2(4, 4));
                IEnumerator Hover(Transform root, HoverStyle style, string what, Func<UiHover, bool>? where = null)
                {
                    var h = FindHover(root, style, where);
                    if (h == null || h.Fill == null) { report($"{name}: a {what} to hover is on screen", false); yield break; }
                    var T = h.DebugTheme;
                    var rest = h.Fill.color;
                    // The token the fill should reach: rows and tiles 5% toward the text, a node's ring 35% toward white,
                    // a button 10% (6% on an accent); a see-through fill gains that much opacity.
                    var to = style == HoverStyle.Node ? Color.white : T.Text;
                    Color.RGBToHSV(rest, out _, out float sat, out float val);
                    float mix = style == HoverStyle.Node ? UiTheme.RingHoverMix : style == HoverStyle.Button ? (sat > 0.5f && val > 0.45f ? T.AccentHoverMix : T.HoverMix) : T.RowHoverMix;
                    var want = new Color(Mathf.Lerp(rest.r, to.r, mix), Mathf.Lerp(rest.g, to.g, mix), Mathf.Lerp(rest.b, to.b, mix), Mathf.Lerp(rest.a, 1, mix));
                    yield return MouseTo(mouse, CenterOf((RectTransform)h.transform), wait: 0.3f);
                    if (h == null || h.Fill == null) yield break;
                    var got = h.Fill.color;
                    bool moved = style switch
                    {
                        HoverStyle.Tile => Mathf.Abs(h.DrawnLift - UiTheme.TileLift) < 0.01f,
                        HoverStyle.Node => Mathf.Abs(h.DrawnScale - (h.Moves ? UiTheme.NodeHoverScale : 1)) < 0.005f, // a locked node: the ring only
                        _ => true,
                    };
                    bool ok = NearColor(got, want) && !NearColor(got, rest) && moved && T == theme;
                    report($"{name}: a hovered {what}'s fill goes {HexOf(rest)} -> {HexOf(got)} (token {HexOf(want)}, theme {T.Name})"
                           + (style == HoverStyle.Tile ? $", drawn {h.DrawnLift:0.0} px higher" : style == HoverStyle.Node ? $", drawn at {h.DrawnScale:0.00}" : ""), ok);
                    if (ok) found.Add(what);
                    yield return shot($"{name}-hover-{what}");
                    yield return MouseTo(mouse, new Vector2(4, 4));
                }
                _campaignScreen.Open();
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Hover(_campaignScreen.transform, HoverStyle.Button, "button", h => h.Gate != null && h.Gate.IsInteractable());
                // A node whose ring isn't already white (the selected one), so the brightening shows; an open one if any.
                bool NotWhite(UiHover h) => h.Fill != null && h.Fill.color != Color.white;
                bool openNode = FindHover(_campaignScreen.transform, HoverStyle.Node, h => h.Moves && NotWhite(h)) != null;
                yield return Hover(_campaignScreen.transform, HoverStyle.Node, "map-node", h => NotWhite(h) && (h.Moves || !openNode));
                _campaignScreen.Close();
                _shop.Open();
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Hover(_shop.transform, HoverStyle.Row, "shop-row", h => h.Fill != null && h.Fill.color != theme.RowSelected);
                _shop.Close();
                _van.ThemeOverride = theme;
                _van.DebugShow(0);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Hover(_van.transform, HoverStyle.Tile, "van-slab");
                _van.Hide();
                _van.ThemeOverride = null;
                _campaignScreen.Close();

                // The grading desk's Sell frame (drawn after its KeyFrame is added): an arrow shows it, the mouse hides it,
                // and a redraw while the mouse is in use keeps it hidden.
                _gradingDesk.DebugShow(1);
                yield return new WaitForSecondsRealtime(0.4f);
                InputSystem.QueueStateEvent(keys, new KeyboardState(Key.DownArrow));
                yield return null;
                yield return null;
                InputSystem.QueueStateEvent(keys, new KeyboardState());
                yield return new WaitForSecondsRealtime(0.2f);
                var deskFrame = _gradingDesk.GetComponentInChildren<KeyFrame>();
                bool keyShown = deskFrame != null && deskFrame.Shown;
                yield return MouseTo(mouse, new Vector2(40, 40));
                bool mouseHid = deskFrame != null && !deskFrame.Shown;
                _gradingDesk.DebugShow(1); // a redraw in pointer mode
                yield return new WaitForSecondsRealtime(0.3f);
                var redrawn = _gradingDesk.GetComponentInChildren<KeyFrame>();
                bool redrawHidden = redrawn != null && !redrawn.Shown && redrawn != deskFrame;
                yield return shot($"{name}-hover-grading-frame");
                report($"{name}: the grading desk's Sell frame shows on an arrow ({keyShown}), hides when the mouse moves ({mouseHid}) and stays hidden through a redraw ({redrawHidden})",
                    keyShown && mouseHid && redrawHidden);
                _gradingDesk.Close();
                _campaignScreen.Close();
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(keys);
                InputMode.DebugReset();
            }
            report($"the {name} theme draws a hovered button, map node, shop row and van slab ({string.Join(", ", found)})", found.Count == 4);
        }
    }
}
