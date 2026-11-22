using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>Small helpers for building uGUI from code, so the scene needs no hand-authored prefabs.</summary>
    public static class Ui
    {
        /// <summary>The body face, Barlow Regular, which every text starts in (<see cref="Typography"/> has the others).</summary>
        public static Font Font => Typography.Font(Face.Body);

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

        /// <summary>
        /// A text at a role of the type scale (UiTheme.Size, in the role's face). <paramref name="reading"/>: a reading area
        /// (card text, the duel log, dialogs, story), where Settings > Card text size scales Caption, Small and Body.
        /// </summary>
        public static Text Text(RectTransform rt, string text, TextRole role, Color color,
            TextAnchor anchor = TextAnchor.MiddleCenter, bool reading = false)
        {
            var t = Text(rt, text, UiTheme.Size(role, reading), color, anchor);
            t.font = Typography.Font(Typography.FaceOf(role));
            return t;
        }

        public static Button Button(Transform parent, string label, Vector2 center, Vector2 size, Action onClick)
        {
            var rt = Placed(label, parent, center, size);
            var image = Image(rt, new Color(0.22f, 0.25f, 0.32f));
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => { Sounds.Play("click"); onClick(); });
            Text(Stretched("Label", rt, Vector2.zero, Vector2.one), label, UiTheme.Size(TextRole.Label), Color.white).font = Typography.Font(Face.Condensed); // buttons: the Label role
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
            Text(rt, label, size, UiTheme.Current.Text).font = Typography.Font(Face.Condensed); // buttons: the Label face
            UiHover.Attach(button);
            return button;
        }

        private static readonly TextGenerator FitGen = new TextGenerator();

        /// <summary>A one-line string's width in layout units in <paramref name="font"/> at <paramref name="size"/> (no Text needed).</summary>
        public static float LineWidth(string s, Font font, int size)
        {
            var settings = new TextGenerationSettings
            {
                font = font, fontSize = size, scaleFactor = 1, richText = true, lineSpacing = 1, color = Color.white,
                horizontalOverflow = HorizontalWrapMode.Overflow, verticalOverflow = VerticalWrapMode.Overflow,
                textAnchor = TextAnchor.UpperLeft, pivot = Vector2.zero, generationExtents = new Vector2(10000, 1000),
            };
            return FitGen.GetPreferredWidth(s, settings);
        }

        /// <summary>
        /// One line in the text's box: the largest size from its own down to <paramref name="min"/> (default: its own) at
        /// which it fits the box's width, else cut with "…" at <paramref name="min"/> (best fit turned off). For a name or a
        /// line whose whole text is a hover or click away. A cut keeps rich-text tags (closing the open ones after the "…"),
        /// a literal "&lt;" and whole surrogate pairs.
        /// </summary>
        /// <summary>
        /// Shows <paramref name="s"/> (a deck's or a file's name, or a line built round one) exactly as typed: rich text off,
        /// so "a &lt;b&gt;bold" shows its "&lt;b&gt;". (Legacy Text has no escape for "&lt;": a zero-width character after it
        /// draws a box in Barlow, so a name never goes into a rich-text line; the line goes plain.)
        /// </summary>
        public static Text Plain(Text t, string? s)
        {
            t.supportRichText = false;
            t.text = s ?? "";
            return t;
        }

        public static Text OneLine(Text t, int? min = null)
        {
            float room = t.rectTransform.rect.width;
            if (room <= 0 || string.IsNullOrEmpty(t.text)) return t;
            var settings = t.GetGenerationSettings(Vector2.zero);
            settings.resizeTextForBestFit = false;
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            float Width(string s, int size) { settings.fontSize = size; return FitGen.GetPreferredWidth(s, settings) / t.pixelsPerUnit; }
            int max = t.resizeTextForBestFit ? t.resizeTextMaxSize : t.fontSize, least = Mathf.Min(max, min ?? (t.resizeTextForBestFit ? t.resizeTextMinSize : max));
            t.resizeTextForBestFit = false;
            for (int size = max; size >= least; size--)
                if (Width(t.text, size) <= room) { t.fontSize = size; return t; }
            t.fontSize = least;
            var full = t.text;
            int n = VisibleCount(full, t.supportRichText);
            while (n > 1 && Width(Cut(full, n, t.supportRichText), least) > room) n--;
            t.text = Cut(full, n, t.supportRichText);
            return t;
        }

        private static readonly System.Text.RegularExpressions.Regex RichTag =
            new System.Text.RegularExpressions.Regex(@"^<(/?)(b|i|color|size|material)(=[^<>]*)?>");

        /// <summary>The text split into rich-text tags (Unity's own: b, i, color, size, material) and visible characters (a surrogate pair is one); a "&lt;" that starts no tag is a character.</summary>
        private static List<(bool tag, string s)> Tokens(string text, bool rich)
        {
            var tokens = new List<(bool, string)>();
            for (int i = 0; i < text.Length;)
            {
                if (rich && text[i] == '<' && RichTag.Match(text.Substring(i)) is { Success: true } m) { tokens.Add((true, m.Value)); i += m.Length; continue; }
                int len = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
                tokens.Add((false, text.Substring(i, len)));
                i += len;
            }
            return tokens;
        }

        private static int VisibleCount(string text, bool rich) => Tokens(text, rich).Count(k => !k.tag);

        /// <summary>
        /// The first <paramref name="visible"/> characters with "…", rich-text tags kept and the ones still open closed after
        /// the "…" (a colour or bold runs on to the cut); trailing spaces and separators go.
        /// </summary>
        internal static string Cut(string text, int visible, bool rich)
        {
            var tokens = Tokens(text, rich);
            var kept = new List<(bool tag, string s)>();
            int seen = 0;
            foreach (var k in tokens)
            {
                if (!k.tag && seen >= visible) break;
                kept.Add(k);
                if (!k.tag) seen++;
            }
            // Trailing spaces and separators go (tags after them stay, so the closing logic sees them).
            for (int j = kept.Count - 1; j >= 0; j--)
            {
                if (kept[j].tag) continue;
                if (kept[j].s.Length == 1 && " ,·:".IndexOf(kept[j].s[0]) >= 0 && kept.Count(k => !k.tag) > 1) kept.RemoveAt(j);
                else break;
            }
            var open = new List<string>();
            var sb = new System.Text.StringBuilder();
            foreach (var k in kept)
            {
                sb.Append(k.s);
                if (!k.tag) continue;
                var m = RichTag.Match(k.s);
                if (m.Groups[1].Value == "/") { int at = open.LastIndexOf(m.Groups[2].Value); if (at >= 0) open.RemoveAt(at); }
                else open.Add(m.Groups[2].Value);
            }
            sb.Append('…');
            for (int i = open.Count - 1; i >= 0; i--) sb.Append("</").Append(open[i]).Append('>');
            return sb.ToString();
        }

        /// <summary>
        /// A reading text filling <paramref name="area"/> (an empty rect): the largest size from <paramref name="max"/> down
        /// to <paramref name="min"/> at which it fits; when it doesn't fit even at <paramref name="min"/>, it stays at
        /// <paramref name="min"/> and the area scrolls (wheel, drag, and a scrollbar at its right). Never cut.
        /// </summary>
        public static Text FitOrScroll(RectTransform area, string text, int max, int min, Color color, TextAnchor anchor)
        {
            var t = Text(Stretched("Text", area, Vector2.zero, Vector2.one), text, max, color, anchor);
            t.supportRichText = true;
            var size = area.rect.size;
            float Height(int fontSize, float width)
            {
                var settings = t.GetGenerationSettings(new Vector2(width, 0));
                settings.resizeTextForBestFit = false;
                settings.fontSize = fontSize;
                return FitGen.GetPreferredHeight(text, settings) / t.pixelsPerUnit;
            }
            if (size.x <= 0 || size.y <= 0) return t;
            for (int s = max; s >= min; s--)
                if (Height(s, size.x) <= size.y) { t.fontSize = s; return t; }
            // Scrolls: the text at the floor in a column as tall as it needs, a scrollbar beside it.
            const float bar = 10, gap = 6;
            t.fontSize = min;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.alignment = anchor switch { TextAnchor.MiddleCenter or TextAnchor.LowerCenter => TextAnchor.UpperCenter, _ => TextAnchor.UpperLeft };
            area.gameObject.AddComponent<RectMask2D>();
            if (area.GetComponent<Graphic>() == null) Image(area, new Color(0, 0, 0, 0.001f)); // something to catch the wheel
            var content = t.rectTransform;
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            float h = Mathf.Ceil(Height(min, size.x - bar - gap)) + 4;
            content.offsetMin = new Vector2(0, -h);
            content.offsetMax = new Vector2(-(bar + gap), 0);
            var scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = area;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            var track = Rect("Scrollbar", area);
            track.anchorMin = new Vector2(1, 0);
            track.anchorMax = Vector2.one;
            track.pivot = new Vector2(1, 0.5f);
            track.sizeDelta = new Vector2(bar, 0);
            Image(track, new Color(1, 1, 1, 0.12f));
            var handle = Stretched("Handle", track, Vector2.zero, Vector2.one);
            var handleImage = Image(handle, new Color(1, 1, 1, 0.5f));
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalNormalizedPosition = 1;
            return t;
        }

        /// <summary>
        /// A button at a role of the type scale (Label for buttons, tabs and chips; a bigger role for a big button), always in
        /// the condensed face buttons use.
        /// </summary>
        public static Button BoxButton(Transform parent, float x, float y, float w, float h, string label, Action onClick, TextRole role) =>
            BoxButton(parent, x, y, w, h, label, onClick, UiTheme.Size(role));

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
            Typography.Italic(hint);
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
