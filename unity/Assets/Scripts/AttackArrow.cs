using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Where an attack is going (Master Duel style): a glowing beam of chevrons flowing from the attacker to its target
    /// (or to the player, for a direct attack), and a readout beside the target ("1800 vs 1500"). Blue for your
    /// attacks, red for your opponent's; faint while you're only choosing a target.
    /// </summary>
    public sealed class AttackArrow : MonoBehaviour
    {
        private const float Spacing = 44, StartGap = 70, EndGap = 78;
        private RectTransform? _root;
        private readonly List<RectTransform> _chevrons = new List<RectTransform>();
        private float _length;
        private Color _color;

        /// <summary>Whether an arrow is up (for checks), and whether it's only a preview (choosing a target).</summary>
        public bool Showing => _root != null;
        public bool Preview { get; private set; }
        public string Readout { get; private set; } = "";

        public void Show(Vector2 from, Vector2 to, bool mine, string readout, Color readoutColor, bool preview)
        {
            Hide();
            Preview = preview;
            Readout = readout;
            var layer = (RectTransform)transform;
            var delta = to - from;
            float distance = delta.magnitude;
            _length = Mathf.Max(40, distance - StartGap - EndGap);
            _color = mine ? Ui.Hex("#7FD3FF") : Ui.Hex("#FF6A5A");
            if (preview) _color.a = 0.85f;

            _root = Ui.Rect("AttackArrow", layer);
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            // The arrow's local "up" points from the attacker to the target.
            var dir = distance > 0.01f ? delta / distance : Vector2.up;
            _root.anchoredPosition = from + dir * (StartGap + _length / 2);
            _root.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90);
            _root.sizeDelta = new Vector2(60, _length);

            // A soft glow, a solid core, then the chevrons flowing along it.
            var glow = Ui.Placed("Glow", _root, Vector2.zero, new Vector2(110, _length + 40)).gameObject.AddComponent<RawImage>();
            glow.texture = BoardFx.Textures.Glow;
            glow.color = new Color(_color.r, _color.g, _color.b, preview ? 0.3f : 0.45f);
            glow.raycastTarget = false;
            var core = Ui.Placed("Core", _root, Vector2.zero, new Vector2(30, _length));
            Ui.Image(core, new Color(_color.r, _color.g, _color.b, preview ? 0.22f : 0.32f)).raycastTarget = false;
            // The target glows too.
            var mark = Ui.Placed("Target", layer, to, new Vector2(230, 230)).gameObject.AddComponent<RawImage>();
            mark.texture = BoardFx.Textures.Glow;
            mark.color = new Color(_color.r, _color.g, _color.b, preview ? 0.4f : 0.6f);
            mark.raycastTarget = false;
            mark.transform.SetParent(_root, true);
            _chevrons.Clear();
            int count = Mathf.Max(2, Mathf.CeilToInt(_length / Spacing));
            for (int i = 0; i < count; i++)
            {
                var c = Ui.Placed("Chevron", _root, Vector2.zero, new Vector2(66, 44));
                var image = c.gameObject.AddComponent<RawImage>();
                image.texture = Icons.For("chevron");
                image.color = _color;
                image.raycastTarget = false;
                _chevrons.Add(c);
            }

            if (readout.Length > 0)
            {
                // Beside the target, on the side away from the field's middle.
                var at = to + new Vector2(to.x > 0 ? -150 : 150, 0);
                var plate = Ui.Placed("Readout", layer, at, new Vector2(170, 40));
                Ui.Image(plate, new Color(0.04f, 0.06f, 0.1f, 0.92f)).raycastTarget = false;
                var edge = plate.gameObject.AddComponent<Outline>();
                edge.effectColor = new Color(_color.r, _color.g, _color.b, 0.8f);
                edge.effectDistance = new Vector2(1.5f, -1.5f);
                var text = Ui.Text(plate, readout, 18, readoutColor);
                text.fontStyle = FontStyle.Bold;
                text.supportRichText = true;
                plate.SetParent(_root, true); // goes away with the arrow
            }
            Update();
        }

        public void Hide()
        {
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
            _chevrons.Clear();
            Readout = "";
        }

        private void Update()
        {
            if (_root == null) return;
            // Chevrons flow towards the target and fade in and out at the ends.
            float flow = FxSettings.ReducedMotion ? 0 : Time.unscaledTime * 90f;
            for (int i = 0; i < _chevrons.Count; i++)
            {
                float y = Mathf.Repeat(i * Spacing + flow, _chevrons.Count * Spacing);
                if (y > _length) { _chevrons[i].gameObject.SetActive(false); continue; }
                _chevrons[i].gameObject.SetActive(true);
                _chevrons[i].anchoredPosition = new Vector2(0, y - _length / 2);
                float edgeFade = Mathf.Clamp01(Mathf.Min(y, _length - y) / 40f);
                var image = _chevrons[i].GetComponent<RawImage>();
                image.color = new Color(_color.r, _color.g, _color.b, _color.a * (0.45f + 0.55f * edgeFade));
            }
        }
    }
}
