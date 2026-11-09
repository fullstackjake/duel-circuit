using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Gus's first duel: a pulsing gold frame, with a bobbing arrow above it, on the card or control the lesson's hint
    /// is about (a card in your hand or on your field, the turn disc, or a Life Points plate). It follows its target as
    /// the board redraws, and hides while the target isn't there.
    /// </summary>
    public sealed partial class BoardView
    {
        private string _pointAt = "";
        private RectTransform? _pointer, _pointerArrow;
        private Image[] _pointerEdges = new Image[0];
        private Text? _pointerArrowText;
        private RectTransform _phaseRt = null!;

        /// <summary>What to point at: "hand:&lt;code&gt;", "field:&lt;code&gt;", "phase", "lp:&lt;player&gt;", or null / "" for nothing.</summary>
        public void PointAt(string? target)
        {
            _pointAt = target ?? "";
            UpdatePointer();
        }

        /// <summary>For checks: what the pointer is on now ("" when hidden).</summary>
        public string PointingAt => _pointer != null && _pointer.gameObject.activeSelf ? _pointAt : "";

        private RectTransform? PointTarget()
        {
            if (_pointAt.Length == 0) return null;
            if (_pointAt == "phase") return _phaseRt;
            var parts = _pointAt.Split(':');
            if (parts.Length != 2) return null;
            if (parts[0] == "lp" && int.TryParse(parts[1], out var player) && player is 0 or 1) return _plateRoot[player];
            if (!uint.TryParse(parts[1], out var code) || _lastBoard == null || HumanSeat < 0) return null;
            var me = _lastBoard.Players[HumanSeat];
            if (parts[0] == "hand")
            {
                for (int i = 0; i < me.Hand.Count; i++)
                    if (me.Hand[i]?.Code == code) return ViewAt(HumanSeat, Location.Hand, i);
            }
            else if (parts[0] == "field")
            {
                for (int i = 0; i < me.Monsters.Count; i++)
                    if (me.Monsters[i]?.Code == code) return ViewAt(HumanSeat, Location.MZone, i);
            }
            return null;
        }

        /// <summary>Every frame: onto the target, pulsing (hidden without one, or while a phase picker covers the board).</summary>
        private void UpdatePointer()
        {
            var target = PointTarget();
            if (target == null || _phasePicker != null)
            {
                if (_pointer != null) _pointer.gameObject.SetActive(false);
                return;
            }
            if (_pointer == null) BuildPointer();
            var pointer = _pointer!;
            if (!pointer.gameObject.activeSelf || pointer.GetSiblingIndex() != pointer.parent.childCount - 1) pointer.SetAsLastSibling();
            pointer.gameObject.SetActive(true);
            float scale = _field.lossyScale.x > 0 ? target.lossyScale.x / _field.lossyScale.x : 1;
            var size = target.rect.size * scale;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            float pad = 10 + 6 * pulse;
            pointer.position = target.TransformPoint(target.rect.center);
            pointer.sizeDelta = size + new Vector2(pad, pad) * 2;
            var gold = new Color(1f, 0.79f, 0.25f, 0.65f + 0.35f * pulse);
            foreach (var edge in _pointerEdges) edge.color = gold;
            _pointerArrow!.anchoredPosition = new Vector2(0, pointer.sizeDelta.y / 2 + 26 + 10 * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.2f)));
            _pointerArrowText!.color = gold;
        }

        private void BuildPointer()
        {
            _pointer = Ui.Placed("LessonPointer", _field, Vector2.zero, new Vector2(100, 100));
            const float thick = 5;
            var edges = new[]
            {
                (new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, thick)), (new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, thick)),
                (new Vector2(0, 0), new Vector2(0, 1), new Vector2(thick, 0)), (new Vector2(1, 0), new Vector2(1, 1), new Vector2(thick, 0)),
            };
            _pointerEdges = new Image[edges.Length];
            for (int i = 0; i < edges.Length; i++)
            {
                var (min, max, sz) = edges[i];
                var rt = Ui.Placed("Edge", _pointer, Vector2.zero, sz);
                rt.anchorMin = min;
                rt.anchorMax = max;
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = sz;
                _pointerEdges[i] = Ui.Image(rt, Color.clear);
                _pointerEdges[i].raycastTarget = false;
            }
            _pointerArrow = Ui.Placed("Arrow", _pointer, Vector2.zero, new Vector2(60, 50));
            _pointerArrowText = Ui.Text(_pointerArrow, "▼", 40, Color.clear);
            _pointerArrowText.raycastTarget = false;
            _pointerArrowText.gameObject.AddComponent<Outline>().effectDistance = new Vector2(2, -2);
        }
    }
}
