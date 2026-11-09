using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Tall windows (5:4, 4:3: taller than 16:9): a screen's 1920x1080 layout sits in the middle with bars above and below,
    /// instead of at the top. Every canvas scales with Expand, so a tall window gives the canvas extra height (1920 wide,
    /// more than 1080 tall); this moves the screen's root into a centred band (its bars rounded to whole pixels). Wide
    /// windows are untouched. The duel board isn't letterboxed: its layout stretches proportionally and reads right tall.
    ///
    /// What fills the whole window keeps doing so, bars included, so no hard edge shows: the root's own background, and its
    /// direct children that run its full height with an Image or RawImage (dims, the story backdrop, side panels). An Image
    /// gets a strip of its look in each bar; a picture is drawn by a copy scaled to cover the window (its aspect kept) while
    /// the original stops drawing (so a crossfade doesn't double). They are the target's own first children, kept in step
    /// every frame (colour, texture, shown or not), so they fade, hide and block clicks as the original does.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Letterbox : MonoBehaviour
    {
        private const float LayoutHeight = 1080;
        private const string FillName = "LetterboxFill", TopName = FillName + "Top", BottomName = FillName + "Bottom";
        private RectTransform _root = null!, _canvas = null!;
        private Canvas? _canvasComponent;
        private float _bar = -1;
        /// <summary>The children given fills (they lose them when they stop running the full height, or the bars go).</summary>
        private readonly HashSet<RectTransform> _filled = new HashSet<RectTransform>();
        private readonly List<RectTransform> _stale = new List<RectTransform>();

        /// <summary>Puts a screen's root (stretched over its canvas) in the middle of tall windows.</summary>
        public static void Attach(RectTransform root)
        {
            if (root.GetComponent<Letterbox>() == null) root.gameObject.AddComponent<Letterbox>();
        }

        /// <summary>For checks: the bar's height above and below the layout (0 in a 16:9 or wider window).</summary>
        public static float Bar(RectTransform root) => root.GetComponent<Letterbox>() is Letterbox l ? Mathf.Max(0, l._bar) : 0;

        /// <summary>For checks: a child's top strip, if it has one shown.</summary>
        public static Image? TopFill(RectTransform child) => child.Find(TopName) is Transform t && t.gameObject.activeInHierarchy ? t.GetComponent<Image>() : null;

        private void Awake()
        {
            _root = (RectTransform)transform;
            _canvas = (RectTransform)transform.parent;
            _canvasComponent = _canvas.GetComponent<Canvas>();
        }

        private void LateUpdate()
        {
            float h = _canvas.rect.height;
            float bar = 0;
            if (h > LayoutHeight + 0.5f)
            {
                // Whole pixels: the canvas's units times its scale are pixels.
                float scale = _canvasComponent != null && _canvasComponent.scaleFactor > 0 ? _canvasComponent.scaleFactor : 1;
                bar = Mathf.Round((h - LayoutHeight) / 2 * scale) / scale;
            }
            if (bar <= 0 && _bar <= 0 && _filled.Count == 0) { _bar = 0; return; } // 16:9 or wider, nothing to undo
            if (!Mathf.Approximately(bar, _bar))
            {
                _bar = bar;
                _root.anchorMin = Vector2.zero;
                _root.anchorMax = Vector2.one;
                _root.offsetMin = new Vector2(0, bar);
                _root.offsetMax = new Vector2(0, -bar);
            }
            Fill(_root, bar);
            if (bar > 0) _filled.Add(_root);
            for (int i = 0; i < _root.childCount; i++)
            {
                var child = _root.GetChild(i) as RectTransform;
                if (child == null) continue;
                // Runs the root's full height (a dim, a backdrop, a side panel), at no offset of its own (the fills don't).
                bool full = child.anchorMin.y == 0 && child.anchorMax.y == 1 && child.offsetMin.y == 0 && child.offsetMax.y == 0;
                if (!full || !child.gameObject.activeSelf) continue;
                Fill(child, bar);
                if (bar > 0) _filled.Add(child);
            }
            // Children that stopped qualifying (moved, resized, gone), or every one when the bars went: their fills off.
            _stale.Clear();
            foreach (var t in _filled)
            {
                if (t == null) { _stale.Add(t!); continue; }
                bool still = bar > 0 && (t == _root || t.parent == _root && t.anchorMin.y == 0 && t.anchorMax.y == 1 && t.offsetMin.y == 0 && t.offsetMax.y == 0);
                if (still) continue;
                Unfill(t);
                _stale.Add(t);
            }
            foreach (var t in _stale) _filled.Remove(t);
        }

        private static void Unfill(RectTransform target)
        {
            foreach (var name in new[] { TopName, BottomName, FillName })
                if (target.Find(name) is Transform f) f.gameObject.SetActive(false);
            if (target.GetComponent<RawImage>() is RawImage raw) raw.canvasRenderer.cull = false;
        }

        private static void Fill(RectTransform target, float bar)
        {
            var source = target.GetComponent<Graphic>();
            if (source is RawImage raw) FillPicture(target, raw, bar);
            else if (source is Image image)
            {
                // Two strips, one in each bar (not one copy over the whole: a see-through dim would come out twice as dark).
                Strip(target, image, bar, TopName, top: true);
                Strip(target, image, bar, BottomName, top: false);
            }
        }

        /// <summary>A plain fill (a colour, a dim) carried into one bar: a strip of the same look just above or below the target.</summary>
        private static void Strip(RectTransform target, Image source, float bar, string name, bool top)
        {
            var rt = target.Find(name) as RectTransform;
            if (bar <= 0) { if (rt != null) rt.gameObject.SetActive(false); return; }
            if (rt == null)
            {
                rt = Ui.Rect(name, target);
                rt.gameObject.AddComponent<Image>();
            }
            rt.gameObject.SetActive(true);
            if (rt.GetSiblingIndex() > 1) rt.SetSiblingIndex(0); // behind everything the target holds
            // A pixel of overlap hides a seam under an opaque fill; a see-through one meets the edge exactly (no darker line).
            float overlap = source.color.a >= 0.99f ? 1 : 0;
            rt.anchorMin = new Vector2(0, top ? 1 : 0);
            rt.anchorMax = new Vector2(1, top ? 1 : 0);
            rt.pivot = new Vector2(0.5f, top ? 0 : 1);
            rt.sizeDelta = new Vector2(0, bar + overlap);
            rt.anchoredPosition = new Vector2(0, top ? -overlap : overlap);
            var copy = rt.GetComponent<Image>();
            Sync(copy, source);
            copy.sprite = source.sprite;
            copy.type = source.type;
        }

        /// <summary>
        /// A picture (the story backdrop) drawn by a copy scaled to cover the whole window, its aspect kept; the original stops
        /// drawing meanwhile (its renderer culled), so a crossfade shows one image at the original's alpha.
        /// </summary>
        private static void FillPicture(RectTransform target, RawImage source, float bar)
        {
            var rt = target.Find(FillName) as RectTransform;
            if (bar <= 0)
            {
                if (rt != null) rt.gameObject.SetActive(false);
                source.canvasRenderer.cull = false;
                return;
            }
            if (rt == null)
            {
                rt = Ui.Rect(FillName, target);
                rt.gameObject.AddComponent<RawImage>();
            }
            rt.gameObject.SetActive(true);
            if (rt.GetSiblingIndex() != 0) rt.SetSiblingIndex(0);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            var copy = rt.GetComponent<RawImage>();
            Sync(copy, source);
            copy.texture = source.texture;
            copy.uvRect = source.uvRect;
            source.canvasRenderer.cull = true;
            // Across the whole width it covers the window with its aspect kept (overflowing the sides).
            float width = target.rect.width;
            bool fullWidth = target.anchorMin.x == 0 && target.anchorMax.x == 1;
            float grow = fullWidth && width > 0 ? width * ((LayoutHeight + 2 * bar) / LayoutHeight - 1) / 2 : 0;
            rt.offsetMin = new Vector2(-grow, -bar);
            rt.offsetMax = new Vector2(grow, bar);
        }

        private static void Sync(Graphic copy, Graphic source)
        {
            copy.color = source.color;
            copy.enabled = source.enabled;
            copy.raycastTarget = source.raycastTarget;
            copy.material = source.material;
        }
    }
}
