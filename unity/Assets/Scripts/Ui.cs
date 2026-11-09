using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>Small helpers for building uGUI from code, so the scene needs no hand-authored prefabs.</summary>
    public static class Ui
    {
        private static Font? _font;
        public static Font Font => _font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>A rect of fixed size centered on a point relative to the parent's center.</summary>
        public static RectTransform Placed(string name, Transform parent, Vector2 center, Vector2 size)
        {
            var rt = Rect(name, parent);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = center;
            return rt;
        }

        /// <summary>A rect stretched over a fraction of the parent (anchors in 0..1).</summary>
        public static RectTransform Stretched(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var rt = Rect(name, parent);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Image(RectTransform rt, Color color)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static Text Text(RectTransform rt, string text, int size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            // A GameObject can hold only one Graphic, so text over a background goes on a child.
            if (rt.GetComponent<Graphic>() != null) rt = Stretched("Text", rt, Vector2.zero, Vector2.one);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(Transform parent, string label, Vector2 center, Vector2 size, Action onClick)
        {
            var rt = Placed(label, parent, center, size);
            var image = Image(rt, new Color(0.22f, 0.25f, 0.32f));
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => { Sounds.Play("click"); onClick(); });
            Text(Stretched("Label", rt, Vector2.zero, Vector2.one), label, 18, Color.white);
            UiHover.Attach(button);
            return button;
        }

        /// <summary>A full-screen screen of its own (1920x1080 layout, scaled), drawn above lower sorting orders.</summary>
        public static RectTransform Overlay(Transform owner, string name, int sortingOrder)
        {
            var canvasGo = new GameObject(name + "Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(owner, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // the whole 1920x1080 layout stays visible at any aspect ratio
            var root = Stretched(name, canvasGo.transform, Vector2.zero, Vector2.one);
            Letterbox.Attach(root); // in a window taller than 16:9, the layout in the middle with bars above and below
            return root;
        }

        /// <summary>A rect positioned from the top-left of a 1920x1080 layout.</summary>
        public static RectTransform Box(Transform parent, float x, float y, float w, float h)
        {
            var rt = Rect("Box", parent);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>A button positioned from the top-left (see <see cref="Box"/>).</summary>
        public static Button BoxButton(Transform parent, float x, float y, float w, float h, string label, Action onClick, int size = 16)
        {
            var rt = Box(parent, x, y, w, h);
            // The theme's button and text colors (UiTheme: Main Street today, the Back Alley's later).
            var image = Image(rt, UiTheme.Current.Button);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => { Sounds.Play("click"); onClick(); });
            Text(rt, label, size, UiTheme.Current.Text);
            UiHover.Attach(button);
            return button;
        }

        /// <summary>
        /// Lets the keyboard's focus (the UI's arrow navigation, Enter) reach a clicked rect as it reaches buttons, its hover
        /// look standing for the focus while the keys drive (<see cref="UiHover"/>).
        /// </summary>
        public static Selectable Focusable(RectTransform rt)
        {
            // TryGetComponent, not ?? (Unity's fake null in the Editor defeats it).
            if (!rt.TryGetComponent<Selectable>(out var s)) s = rt.gameObject.AddComponent<Selectable>();
            s.transition = Selectable.Transition.None;
            s.targetGraphic = rt.GetComponent<Graphic>();
            return s;
        }

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        /// <summary>A single-line text box with a placeholder; <paramref name="onChange"/> fires on every edit.</summary>
        public static InputField Input(RectTransform rt, string placeholder, int size, Action<string>? onChange = null, Action<string>? onSubmit = null)
        {
            var background = Image(rt, UiTheme.Current.Well);
            var field = rt.gameObject.AddComponent<InputField>();
            field.targetGraphic = background;

            var textRt = Stretched("Text", rt, Vector2.zero, Vector2.one);
            textRt.offsetMin = new Vector2(10, 2);
            textRt.offsetMax = new Vector2(-10, -2);
            var text = textRt.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;

            var placeholderRt = Stretched("Placeholder", rt, Vector2.zero, Vector2.one);
            placeholderRt.offsetMin = textRt.offsetMin;
            placeholderRt.offsetMax = textRt.offsetMax;
            var hint = placeholderRt.gameObject.AddComponent<Text>();
            hint.font = Font;
            hint.fontSize = size;
            hint.fontStyle = FontStyle.Italic;
            hint.color = new Color(1, 1, 1, 0.55f); // dim, but still readable (about 5:1 on the field's dark fill)
            hint.alignment = TextAnchor.MiddleLeft;
            hint.text = placeholder;

            field.textComponent = text;
            field.placeholder = hint;
            field.lineType = InputField.LineType.SingleLine;
            if (onChange != null) field.onValueChanged.AddListener(v => onChange(v));
            if (onSubmit != null) field.onEndEdit.AddListener(v => onSubmit(v));
            return field;
        }

        /// <summary>
        /// Makes a rect respond to left / right clicks and hovering. <paramref name="style"/> opts it into the shared hover
        /// look (<see cref="UiHover"/>: rows, tiles, map nodes); plain by default (scrims, card backgrounds).
        /// </summary>
        public static Clickable Clickable(RectTransform rt, Action? left, Action? right = null, Action? hover = null, HoverStyle style = HoverStyle.None)
        {
            var c = rt.gameObject.AddComponent<Clickable>();
            c.Left = left;
            c.Right = right;
            c.Hover = hover;
            if (rt.GetComponent<Graphic>() == null) Image(rt, new Color(0, 0, 0, 0)); // something to receive the pointer
            if (style != HoverStyle.None) UiHover.Add(rt, style);
            return c;
        }
    }

    /// <summary>
    /// Lets a rect be dragged. The owner shows a ghost while dragging and decides what a drop means
    /// from the screen position. Clicks still work: a press only becomes a drag once the pointer moves.
    /// </summary>
    public sealed class DragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Action<Vector2>? Began, Moved, Dropped;

        public void OnBeginDrag(PointerEventData e) => Began?.Invoke(e.position);
        public void OnDrag(PointerEventData e) => Moved?.Invoke(e.position);
        public void OnEndDrag(PointerEventData e) => Dropped?.Invoke(e.position);
    }

    /// <summary>
    /// Left / right clicks and a hover callback. Where the rect is also a Selectable (<see cref="Ui.Focusable"/>), the
    /// keyboard's focus does what the pointer does: selecting it runs <see cref="Hover"/> (help text that was hover-only),
    /// Enter (submit) runs <see cref="Left"/>.
    /// </summary>
    public sealed class Clickable : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, ISelectHandler, ISubmitHandler
    {
        public Action? Left, Right, Hover;

        public void OnSelect(BaseEventData e) => Hover?.Invoke();

        public void OnSubmit(BaseEventData e)
        {
            if (Left != null) Sounds.Play("click", 0.6f);
            Left?.Invoke();
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right) Right?.Invoke();
            else if (e.button == PointerEventData.InputButton.Left)
            {
                if (Left != null) Sounds.Play("click", 0.6f);
                Left?.Invoke();
            }
        }

        public void OnPointerEnter(PointerEventData e) => Hover?.Invoke();
    }
}
