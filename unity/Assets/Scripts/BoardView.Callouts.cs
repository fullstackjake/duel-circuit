using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine.Interactive;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Effect callouts: each activation pops up at the top right with the card's full text, drawn like a big chain
    /// link (owner stripe, link number, art, name). They stack top to bottom in the order they were played, each stays
    /// three seconds, then they fade in the same order and the rest slide up. The chain panel sits under them. They
    /// keep to the room right of the board: when the stack would reach a zone, a pile or the Turn button (or passes
    /// three), the oldest leave early.
    /// </summary>
    public sealed partial class BoardView
    {
        private sealed class Callout
        {
            public RectTransform Rt = null!;
            public CanvasGroup Group = null!;
            public Text Context = null!;
            /// <summary>The card text's box, and the callout's least height with it squeezed (its text shrinks to fit).</summary>
            public RectTransform Body = null!;
            public float MinHeight;
            /// <summary>Its height with the card text gone (name, effect line and context): the last resort over the board.</summary>
            public float Collapsed;
            public uint Code;
            public int Player;
            public string Name = "";
            public float Born, Until, Y;
            public bool Placed;
        }

        private const float CalloutLife = 3f, CalloutFade = 0.35f, CalloutGap = 8, ChainScale = 1.25f, ChainWidth = 360;
        private const int MaxCallouts = 3;
        private const float TextLeft = 108, TextRight = 14;
        /// <summary>The least room a callout's card text keeps when it has to make way (three lines at the smallest size).</summary>
        private const float SqueezedText = 40;
        private readonly List<Callout> _callouts = new List<Callout>();
        private readonly List<(uint code, int player, string text, float at)> _pendingContext = new List<(uint, int, string, float)>();
        private RectTransform? _calloutRoot;
        private float _columnDodge, _chainY = -1;

        /// <summary>Right edge of the column: clear of the recent-actions strip in the campaign layout.</summary>
        private float ColumnMargin => _playLayout ? 84 : 16;

        /// <summary>
        /// How wide callouts are: the room right of the board (the opponent's Extra Deck and Field Zone), so they cover
        /// no zones or piles; where that's too narrow (the developer layout), 600 and they stack over the board.
        /// </summary>
        private float CalloutWidth
        {
            get
            {
                float room = _field.rect.width / 2 - ColumnMargin - (3 * Column + CardView.FieldSize.x / 2 + 14);
                float width = room >= 400 ? Mathf.Min(600, room) : 600;
                // With the text log open the column moves left of the drawer, over the board: narrower there, clear of
                // the opponent's hand (and the stack keeps off their piles, see StackCovers).
                return _playLayout && _logOpen ? Mathf.Min(width, 360) : width;
            }
        }

        /// <summary>The chain panel's size: a size up from its rows, but no wider than the callouts.</summary>
        private float ChainPanelScale => Mathf.Min(ChainScale, CalloutWidth / ChainWidth);

        /// <summary>
        /// Whether a rect (in field coordinates, from the field's center) covers part of the board: a zone, a pile
        /// or the Turn button.
        /// </summary>
        private bool CoversBoard(Rect r)
        {
            float w = CardView.FieldSize.x / 2 + 8, h = CardView.FieldSize.y / 2 + 8;
            // Each side's rows, from the Banished pile to the Deck (the halves are mirrored).
            var top = Rect.MinMaxRect(-4 * Column - w, -h, 3 * Column + w, 2 * Row + h);
            var bottom = Rect.MinMaxRect(-3 * Column - w, -2 * Row - h, 4 * Column + w, h);
            // The turn dial keeps 12 px of clear space around it.
            var phase = new Rect(_phaseRt.anchoredPosition - _phaseRt.sizeDelta / 2 - new Vector2(12, 12), _phaseRt.sizeDelta + new Vector2(24, 24));
            return r.Overlaps(top) || r.Overlaps(bottom) || r.Overlaps(phase);
        }

        /// <summary>For checks: whether the stack as it settles (without the ones leaving) covers part of the board.</summary>
        public bool CalloutsCoverBoard => StackCovers(_callouts.Where(c => Time.unscaledTime < c.Until).ToList());

        /// <summary>The stack laid out as it will settle, right-aligned at the column (the chain panel under it).</summary>
        private bool StackCovers(List<Callout> stack)
        {
            var size = _field.rect.size;
            // Where the stack really is: moved left of the open text log (the detail's brief slide-in doesn't count).
            float right = size.x / 2 - ColumnMargin + (_playLayout && _logOpen ? _columnDodge : 0), y = CalloutTop();
            bool covers = false;
            foreach (var c in stack)
            {
                var s = c.Rt.sizeDelta;
                covers |= CoversBoard(new Rect(right - s.x, size.y / 2 - y - s.y, s.x, s.y));
                y += s.y + CalloutGap;
            }
            if (_chainPanel != null && stack.Count > 0)
            {
                var s = _chainPanel.sizeDelta * _chainPanel.localScale.x;
                covers |= CoversBoard(new Rect(right - s.x, size.y / 2 - (y + 4) - s.y, s.x, s.y));
            }
            return covers;
        }

        /// <summary>For checks: the callouts on screen, top to bottom ("name|context"), and their tops.</summary>
        public List<string> Callouts => _callouts.Select(c => $"{c.Name}|{c.Context.text}").ToList();
        public List<float> CalloutTops => _callouts.Select(c => c.Y).ToList();
        public float ChainPanelTop => _chainPanel == null ? -1 : -_chainPanel.anchoredPosition.y;

        /// <summary>The newest callout for the opponent's play, with what it answers or targets (for checks).</summary>
        public string OpponentCallout => _callouts.LastOrDefault(c => c.Player != (HumanSeat >= 0 ? HumanSeat : 0)) is { } c
            ? c.Context.text.Length > 0 ? $"{c.Name}: {c.Context.text}" : c.Name : "";

        /// <summary>A card's effect was activated: its callout joins the bottom of the stack.</summary>
        public void ShowCallout(uint code, int player, int link, ulong description, bool force = false)
        {
            if (Fx.Tempo <= 0 && !force || code == 0 || _cards.Get(code) is not { } info) return;
            if (_calloutRoot == null)
            {
                _calloutRoot = Ui.Stretched("Callouts", _field, Vector2.zero, Vector2.one);
            }
            _calloutRoot.SetAsLastSibling();

            var text = CalloutText(info.Text);
            float width = CalloutWidth, textW = width - TextLeft - TextRight;
            // Room for the text: about 7.4 units per letter at size 15, at most 9 lines (longer text shrinks to fit).
            int lines = text.Split('\n').Sum(p => Mathf.Max(1, Mathf.CeilToInt(p.Length * 7.4f / textW)));
            float textH = Mathf.Clamp(lines * 18f, 18, 162);
            // The effect's short name ("Negate that effect"); a long one only repeats the text below.
            var summary = DuelText.Describe(description, _cards) ?? "";
            if (summary.Length > 60) summary = "";
            float textTop = summary.Length > 0 ? 58 : 40;
            float height = Mathf.Max(textTop + textH + 12, 96);

            var rt = Ui.Rect("Callout", _calloutRoot);
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.one;
            rt.sizeDelta = new Vector2(width, height);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.alpha = 0;
            var owner = OwnerColor(player);
            // Opaque: a callout sliding over another (or over the chain panel) never lets the text under it show through.
            Ui.Image(rt, new Color(0.04f, 0.06f, 0.1f, 1f)).raycastTarget = false;
            var edge = rt.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(owner.r, owner.g, owner.b, 0.55f);
            edge.effectDistance = new Vector2(1.5f, -1.5f);

            var stripe = Ui.Rect("Owner", rt);
            stripe.anchorMin = Vector2.zero;
            stripe.anchorMax = new Vector2(0, 1);
            stripe.pivot = new Vector2(0, 0.5f);
            stripe.sizeDelta = new Vector2(6, 0);
            Ui.Image(stripe, owner).raycastTarget = false;

            var num = Ui.Text(TopLeft(rt, "N", 8, 12, 40, 36), link > 0 ? link.ToString() : "•", 30, Ui.Hex("#FFC940"));
            num.fontStyle = FontStyle.Bold;
            // Whose play it is in words too, under its number (the stripe's color isn't the only sign).
            var who = Ui.Text(TopLeft(rt, "Who", 6, 48, 44, 18), OwnerTag(player), 14, new Color(0.8f, 0.84f, 0.92f));
            who.fontStyle = FontStyle.Bold;
            who.resizeTextForBestFit = true; // a six-letter name (when watching) still fits under the number
            who.resizeTextMinSize = 10;
            who.resizeTextMaxSize = 14;

            var thumb = TopLeft(rt, "Art", 50, 12, 48, 70);
            var art = CardView.Art?.Get(code);
            if (art != null) thumb.gameObject.AddComponent<RawImage>().texture = art;
            else Ui.Image(thumb, CardView.FrameColor(info.Type));

            var name = Ui.Text(TopLeft(rt, "Name", TextLeft, 8, textW, 30), info.Name, 22, Color.white, TextAnchor.MiddleLeft);
            name.fontStyle = FontStyle.Bold;
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 14;
            name.resizeTextMaxSize = 22;
            if (summary.Length > 0)
            {
                var what = Ui.Text(TopLeft(rt, "Effect", TextLeft, 38, textW, 18), summary, 14, Ui.Hex("#9FD8FF"), TextAnchor.MiddleLeft);
                what.fontStyle = FontStyle.Italic;
            }
            var body = Ui.Text(TopLeft(rt, "Text", TextLeft, textTop, textW, textH), text, 15, new Color(0.86f, 0.89f, 0.95f), TextAnchor.UpperLeft);
            body.resizeTextForBestFit = true;
            body.resizeTextMinSize = 10;
            body.resizeTextMaxSize = 15;

            // What it answers or targets, at the bottom once the duel knows it.
            var contextRt = Ui.Rect("Context", rt);
            contextRt.anchorMin = Vector2.zero;
            contextRt.anchorMax = new Vector2(1, 0);
            contextRt.pivot = new Vector2(0.5f, 0);
            contextRt.sizeDelta = new Vector2(-(TextLeft + TextRight), 18);
            contextRt.anchoredPosition = new Vector2((TextLeft - TextRight) / 2, 8);
            var context = Ui.Text(contextRt, "", 14, Ui.Hex("#FFC940"), TextAnchor.MiddleLeft);
            context.fontStyle = FontStyle.Italic;
            foreach (var g in rt.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;

            float now = Time.unscaledTime;
            var callout = new Callout
            {
                Rt = rt, Group = group, Context = context, Body = body.rectTransform, MinHeight = Mathf.Min(height, textTop + SqueezedText + 12),
                Collapsed = Mathf.Min(height, textTop + 8),
                Code = code, Player = player, Name = info.Name, Born = now, Until = now + CalloutLife,
            };
            _callouts.Add(callout);
            _pendingContext.RemoveAll(p => now - p.at > 6);
            int pending = _pendingContext.FindIndex(p => p.code == code && p.player == player);
            if (pending >= 0)
            {
                SetContext(callout, _pendingContext[pending].text);
                _pendingContext.RemoveAt(pending);
            }
        }

        /// <summary>The card's text without blank lines or a database's "* The above text is unofficial..." note.</summary>
        private static string CalloutText(string text) => string.Join("\n", text.Split('\n')
            .Select(l => l.TrimEnd()).Where(l => l.Length > 0 && !l.StartsWith("* The above text is unofficial")));

        private static RectTransform TopLeft(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var rt = Ui.Rect(name, parent);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        private static void SetContext(Callout c, string text)
        {
            if (text.Length == 0 || c.Context.text.Length > 0) return;
            c.Context.text = text;
            c.Rt.sizeDelta += new Vector2(0, 24);
            c.MinHeight += 24;
            c.Collapsed += 24;
        }

        /// <summary>
        /// The opponent's activation was announced (with what it answers or targets): the words go on its callout,
        /// or wait for it when the animation hasn't reached it yet.
        /// </summary>
        private void CalloutContext(uint code, int player, string text)
        {
            var cardName = _cards.Get(code)?.Name ?? "";
            int at = cardName.Length > 0 ? text.IndexOf(" activates " + cardName, System.StringComparison.Ordinal) : -1;
            var rest = at >= 0 ? text.Substring(at + " activates ".Length + cardName.Length).TrimStart(',', ' ') : "";
            if (rest.Length > 0) rest = char.ToUpperInvariant(rest[0]) + rest.Substring(1);
            var open = _callouts.LastOrDefault(c => c.Code == code && c.Player == player && c.Context.text.Length == 0 && Time.unscaledTime - c.Born < 1.5f);
            if (open != null) SetContext(open, rest);
            else if (rest.Length > 0) _pendingContext.Add((code, player, rest, Time.unscaledTime));
        }

        /// <summary>Every frame: callouts fade in and out in order, the rest slide up, and the chain panel follows them.</summary>
        private void UpdateCallouts()
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            bool reduced = FxSettings.ReducedMotion;
            // At most a few at once, and none over the board: a new one hurries the oldest along (the newest always stays).
            var staying = _callouts.Where(c => now < c.Until).ToList();
            while (staying.Count > 1 && (staying.Count > MaxCallouts || StackCovers(staying)))
            {
                staying[0].Until = now;
                staying.RemoveAt(0);
            }
            // Still over the board with only the newest (a long card text above a chain): its text box gives way, the text
            // shrinking to fit, until the chain panel under it clears the piles.
            // If even three lines of text are too many (the turn dial or a pile still under the stack), the text goes and
            // only the name, the effect line and its context stay.
            if (staying.Count == 1)
            {
                var last = staying[0];
                foreach (float floor in new[] { last.MinHeight, last.Collapsed })
                    while (StackCovers(staying) && last.Rt.sizeDelta.y > floor)
                    {
                        float cut = Mathf.Min(8, last.Rt.sizeDelta.y - floor);
                        last.Rt.sizeDelta -= new Vector2(0, cut);
                        last.Body.sizeDelta = new Vector2(last.Body.sizeDelta.x, Mathf.Max(0, last.Body.sizeDelta.y - cut));
                    }
            }
            // Oldest first: one never outlasts the one above it.
            for (int i = 1; i < _callouts.Count; i++) _callouts[i].Until = Mathf.Max(_callouts[i].Until, _callouts[i - 1].Until);
            for (int i = _callouts.Count - 1; i >= 0; i--)
            {
                if (now < _callouts[i].Until + CalloutFade && _callouts[i].Rt != null) continue;
                if (_callouts[i].Rt != null) Destroy(_callouts[i].Rt.gameObject);
                _callouts.RemoveAt(i);
            }

            // Out of the way of the card detail when it slides in at the right, and of the text log while it's open.
            float dodge = 0;
            if (_playLayout && _detailGroup != null)
            {
                float column = Mathf.Max(0, ((RectTransform)_canvas).rect.width * ColumnWidth + 10 - ColumnMargin);
                if (_logOpen) dodge = -column - 10;
                else if (!_detailOnLeft) dodge = -_detailSlide * column;
            }
            _columnDodge = reduced ? dodge : Mathf.Lerp(_columnDodge, dodge, 1 - Mathf.Exp(-14 * dt));
            float follow = reduced ? 1 : 1 - Mathf.Exp(-12 * dt);

            // A fading callout keeps its place while the rest fit; when keeping it would push the stack onto the board (the
            // turn dial, a pile), the ones below slide up over it at once.
            bool tight = StackCovers(_callouts);
            float y = CalloutTop();
            foreach (var c in _callouts)
            {
                float appear = Mathf.Clamp01((now - c.Born) / 0.2f);
                float leave = Mathf.Clamp01((now - c.Until) / CalloutFade);
                c.Group.alpha = Mathf.Min(reduced ? 1 : appear, 1 - leave);
                c.Y = c.Placed ? Mathf.Lerp(c.Y, y, follow) : y;
                c.Placed = true;
                float slideIn = reduced ? 0 : (1 - appear) * (1 - appear) * 40;
                c.Rt.anchoredPosition = new Vector2(-ColumnMargin + _columnDodge + slideIn, -c.Y);
                // A fading callout keeps its place; the ones below slide up once it's gone (at once when it's tight).
                if (!(tight && now >= c.Until)) y += c.Rt.sizeDelta.y + CalloutGap;
            }

            if (_chainPanel != null)
            {
                float target = y + (_callouts.Count > 0 ? 4 : 0);
                _chainY = _chainY < 0 ? target : Mathf.Lerp(_chainY, target, follow);
                _chainPanel.anchoredPosition = new Vector2(-ColumnMargin + _columnDodge, -_chainY);
            }
            else _chainY = -1;
        }

        /// <summary>For checks: a callout as an activation shows it, at any speed.</summary>
        public void DebugCallout(uint code, int player, int link) => ShowCallout(code, player, link, 0, force: true);
    }
}
