using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The card-term tooltip: one small panel over everything (title and meaning) beside the pointer, for the glossary
    /// terms in a card's text and the detail's "?" (how card text is punctuated).
    /// </summary>
    public static class TermTooltip
    {
        private const float Width = 400;
        private static RectTransform? _root;
        private static Text _title = null!, _body = null!;

        /// <summary>The term showing (for checks), or null.</summary>
        public static string? Shown => _root != null && _root.gameObject.activeSelf ? _title.text : null;

        public static void Show(Component near, string title, string body, Vector2 screen, Camera? cam)
        {
            var canvas = near.GetComponentInParent<Canvas>()?.rootCanvas;
            if (canvas == null) return;
            if (_root == null || _root.transform.parent != canvas.transform) Build(canvas.transform);
            var root = _root!;
            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
            _title.text = title;
            _body.text = body;
            _body.fontSize = FxSettings.Scaled(15);
            _title.fontSize = FxSettings.Scaled(17);
            float bodyH = _body.preferredHeight;
            float titleH = _title.preferredHeight;
            root.sizeDelta = new Vector2(Width, 14 + titleH + 6 + bodyH + 14);
            ((RectTransform)_title.transform).sizeDelta = new Vector2(Width - 28, titleH);
            ((RectTransform)_body.transform).anchoredPosition = new Vector2(14, -(14 + titleH + 6));
            ((RectTransform)_body.transform).sizeDelta = new Vector2(Width - 28, bodyH);

            // Beside the pointer, kept on screen (to the left of it near the right edge, above it near the bottom).
            var canvasRt = (RectTransform)canvas.transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screen,
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : cam, out var local);
            var half = canvasRt.rect.size / 2;
            var size = root.sizeDelta;
            float x = local.x + 18, y = local.y - 18;
            if (x + size.x > half.x - 8)
            {
                // No room on the right: beside the panel the pointer is in (the card detail column), not over its text.
                var panel = near.GetComponentInParent<ScrollRect>()?.transform as RectTransform ?? near.transform as RectTransform;
                var corners = new Vector3[4];
                panel!.GetWorldCorners(corners);
                var panelLeft = canvasRt.InverseTransformPoint(corners[0]).x;
                x = panelLeft - 12 - size.x >= -half.x + 8 ? panelLeft - 12 - size.x : local.x - 18 - size.x;
            }
            if (y - size.y < -half.y + 8) y = local.y + 18 + size.y;
            root.anchoredPosition = new Vector2(Mathf.Max(-half.x + 8, x), Mathf.Min(half.y - 8, y));
        }

        public static void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        private static void Build(Transform canvas)
        {
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _root = Ui.Rect("TermTooltip", canvas);
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0, 1);
            var bg = Ui.Image(_root, new Color(0.05f, 0.07f, 0.12f, 0.97f));
            bg.raycastTarget = false;
            var outline = _root.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.78f, 1f, 0.55f);
            outline.effectDistance = new Vector2(1, -1);
            _title = Ui.Text(Top("Title", 14), "", 17, Ui.Hex("#8CC8FF"), TextAnchor.UpperLeft);
            _title.fontStyle = FontStyle.Bold;
            _title.verticalOverflow = VerticalWrapMode.Overflow;
            _body = Ui.Text(Top("Meaning", 40), "", 15, Ui.Hex("#D8DEE8"), TextAnchor.UpperLeft);
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            _root.gameObject.SetActive(false);
        }

        private static RectTransform Top(string name, float y)
        {
            var rt = Ui.Rect(name, _root!);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(14, -y);
            rt.sizeDelta = new Vector2(Width - 28, 20);
            return rt;
        }
    }

    /// <summary>
    /// On a card's effect text: as the pointer moves over it, the glossary term under the pointer (if any) shows its
    /// tooltip. Clicks and scrolling pass through to the text's panel.
    /// </summary>
    public sealed class TermHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        /// <summary>The term (title, meaning) at a screen point, or null.</summary>
        public Func<Vector2, Camera?, (string title, string meaning)?>? TermAt;
        private string? _showing;

        public void OnPointerEnter(PointerEventData e) => Check(e.position, e.enterEventCamera);
        public void OnPointerMove(PointerEventData e) => Check(e.position, e.enterEventCamera);

        public void OnPointerExit(PointerEventData e)
        {
            _showing = null;
            TermTooltip.Hide();
        }

        private void OnDisable()
        {
            if (_showing != null) TermTooltip.Hide();
            _showing = null;
        }

        /// <summary>Shows (or hides) the tooltip for whatever is under this screen point.</summary>
        public void Check(Vector2 screen, Camera? cam)
        {
            var term = TermAt?.Invoke(screen, cam);
            if (term == null)
            {
                if (_showing != null) TermTooltip.Hide();
                _showing = null;
                return;
            }
            _showing = term.Value.title;
            TermTooltip.Show(this, term.Value.title, term.Value.meaning, screen, cam);
        }
    }

    /// <summary>A fixed tooltip on hover (the detail's "?").</summary>
    public sealed class TipOnHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string Title = "", Body = "";
        public void OnPointerEnter(PointerEventData e) => TermTooltip.Show(this, Title, Body, e.position, e.enterEventCamera);
        public void OnPointerExit(PointerEventData e) => TermTooltip.Hide();
        private void OnDisable() { if (TermTooltip.Shown == Title) TermTooltip.Hide(); }
    }
}
