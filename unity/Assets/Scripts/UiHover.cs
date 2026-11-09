using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>What a hover looks like (docs/dev/HOVER-DESIGN-2026-11-07.md, sections 1 and 2).</summary>
    public enum HoverStyle
    {
        /// <summary>No hover (Ui.Clickable's default: scrims, card backgrounds).</summary>
        None,
        /// <summary>Buttons: fill toward the text colour (gentler on accents), a 1 px inner edge; pressed darker and drawn at 0.97. A picked tab doesn't change.</summary>
        Button,
        /// <summary>List rows: a faint lightening and a 2 px left bar, weaker than the selected row's fill.</summary>
        Row,
        /// <summary>Slabs, tiles, posters, mats: drawn 4 px higher with a hairline edge (and the row's faint lightening).</summary>
        Tile,
        /// <summary>Map nodes: the ring brightens, the name strip lightens, drawn at 1.06 (a locked node: the ring only).</summary>
        Node,
    }

    /// <summary>
    /// The shared hover, press and disabled look: one per button (Ui.Button, Ui.BoxButton, PromptPanel's, DeckEditor's) and
    /// on the rows, tiles and map nodes that opt in through <see cref="Ui.Clickable"/>. It works on the fill's colour as it
    /// stands when the pointer comes in, so a screen that recolours a button (Primary red, the picked tab, a done lesson)
    /// keeps working, and a recolour while hovered becomes the new rest. A disabled Selectable doesn't react; in highlight
    /// mode (<see cref="InputMode"/>) nothing lights up. Scale and lift are drawn only (<see cref="HoverMesh"/> moves the
    /// vertices): the RectTransform, the layout and the hit area never change. Reduced motion keeps the colour, with no
    /// fade, scale or lift. Only hovered or fading ones are ticked (by the InputMode driver), so a screen of idle buttons
    /// costs nothing a frame, and nothing allocates once a hover has started once.
    /// </summary>
    public sealed class UiHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        public HoverStyle Style = HoverStyle.Button;
        /// <summary>The colour that changes (the button's fill, the row's, the node's ring).</summary>
        public Graphic? Fill;
        /// <summary>A second colour that lightens with it (the map node's name strip); optional.</summary>
        public Graphic? Strip;
        /// <summary>A Selectable whose <c>interactable</c> gates the hover (a button); none: always.</summary>
        public Selectable? Gate;
        /// <summary>What is drawn scaled or lifted, and where the edge goes; this object when null.</summary>
        public RectTransform? Visual;
        /// <summary>False: colour only, never scale or lift (a locked map node).</summary>
        public bool Moves = true;

        private bool _inside, _down, _focused, _tracking, _live, _meshed;
        private float _hover, _press, _drawnScale = 1, _drawnLift;
        private Color _rest, _applied, _stripRest, _stripApplied;
        private HoverEdge? _edge;
        private readonly List<HoverMesh> _meshes = new List<HoverMesh>();

        private static readonly List<UiHover> Live = new List<UiHover>();
        private static readonly List<Graphic> Buffer = new List<Graphic>();

        /// <summary>The scale and lift (reference pixels, up) drawn now, read by <see cref="HoverMesh"/>.</summary>
        internal float DrawnScale => _drawnScale;
        internal float DrawnLift => _drawnLift;
        internal RectTransform VisualRect => Visual != null ? Visual : (RectTransform)transform;

        /// <summary>Puts a hover on <paramref name="rt"/> (its own Graphic as the fill unless one is given).</summary>
        public static UiHover Add(RectTransform rt, HoverStyle style, Graphic? fill = null, RectTransform? visual = null, Selectable? gate = null)
        {
            // TryGetComponent, not ?? (Unity's fake null in the Editor defeats it).
            if (!rt.TryGetComponent<UiHover>(out var h)) h = rt.gameObject.AddComponent<UiHover>();
            h._theme = UiTheme.Current; // the theme the screen draws with (screens set it as they open, and rebuild on a change)
            h.Style = style;
            h.Fill = fill != null ? fill : rt.GetComponent<Graphic>();
            h.Visual = visual;
            h.Gate = gate;
            return h;
        }

        /// <summary>
        /// A built button: Unity's own tint does only the disabled look (the theme's <see cref="UiTheme.Disabled"/>), and
        /// this component draws hover and press.
        /// </summary>
        public static UiHover Attach(Button button)
        {
            button.transition = Selectable.Transition.ColorTint;
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = colors.highlightedColor = colors.pressedColor = colors.selectedColor = Color.white;
            colors.disabledColor = UiTheme.Current.Disabled;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            var h = Add((RectTransform)button.transform, HoverStyle.Button, button.targetGraphic, gate: button);
            // A button built once and kept (the duel board's header) follows a theme switch: its disabled tint and tokens.
            if (!h._followsTheme)
            {
                h._followsTheme = true;
                UiTheme.Changed += h.OnThemeChanged;
            }
            return h;
        }

        private UiTheme _theme = UiTheme.MainStreet;
        private bool _followsTheme;

        private void OnThemeChanged()
        {
            if (this == null) { UiTheme.Changed -= OnThemeChanged; return; } // destroyed without ever waking
            _theme = UiTheme.Current;
            if (Gate is Button b)
            {
                var colors = b.colors;
                colors.disabledColor = _theme.Disabled;
                b.colors = colors;
            }
        }

        /// <summary>For checks: whether the hover is drawn fully in (1), fading, or out (0).</summary>
        public float DebugAmount => _hover;

        public void OnPointerEnter(PointerEventData e)
        {
            _inside = true;
            Wake();
        }

        public void OnPointerExit(PointerEventData e)
        {
            _inside = false;
            _down = false;
            Wake();
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || !Interactable) return;
            _down = true;
            Wake();
            _press = 1; // pressed is instant
            Apply();
        }

        public void OnPointerUp(PointerEventData e)
        {
            _down = false;
            Wake();
        }

        /// <summary>The UI's selection (arrow navigation, or the last click): drawn as the hover while the keys drive.</summary>
        public void OnSelect(BaseEventData e)
        {
            // Focus only from the keys (arrow navigation, or a selection made while they drive): a button a click selected
            // doesn't light up when an arrow is pressed next.
            if (InputMode.Pointer && !InputMode.NavigatedThisFrame()) return;
            _focused = true;
            Wake();
        }

        public void OnDeselect(BaseEventData e)
        {
            _focused = false;
            Wake();
        }

        private bool Interactable => Gate == null || Gate.IsInteractable();

        /// <summary>Starts ticking; takes the fill's rest colour if nothing is drawn yet.</summary>
        private void Wake()
        {
            if (!_tracking && Fill != null)
            {
                _rest = _applied = Fill.color;
                if (Strip != null) _stripRest = _stripApplied = Strip.color;
                _tracking = true;
            }
            if (_live) return;
            _live = true;
            Live.Add(this);
        }

        /// <summary>The hover's target: inside in pointer mode (or the keyboard's focus in highlight mode), enabled, and not the picked tab.</summary>
        private bool Wanted()
        {
            bool on = InputMode.Pointer ? _inside : _focused;
            if (!on || !Interactable) return false;
            if (Style == HoverStyle.Button && _rest == _theme.TabOn) return false;
            return true;
        }

        internal static void TickAll(float dt)
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                if (i >= Live.Count) continue;
                var h = Live[i];
                if (h == null) { Live.RemoveAt(i); continue; }
                h.Tick(dt);
            }
        }

        private void Tick(float dt)
        {
            bool reduced = FxSettings.ReducedMotion;
            float hoverTo = Wanted() ? 1 : 0, pressTo = _down && Interactable ? 1 : 0;
            if (reduced) { _hover = hoverTo; _press = pressTo; }
            else
            {
                _hover = Mathf.MoveTowards(_hover, hoverTo, dt / (hoverTo > _hover ? UiTheme.HoverIn : UiTheme.HoverOut));
                _press = pressTo > _press ? pressTo : Mathf.MoveTowards(_press, pressTo, dt / UiTheme.PressOut);
            }
            Apply();
            if (_hover > 0 || _press > 0 || _inside || _down || _focused) return;
            Rest();
        }

        /// <summary>Back to rest: the fill as it was (unless someone recoloured it meanwhile), nothing drawn, not ticked.</summary>
        private void Rest()
        {
            if (_tracking && Fill != null && Fill.color == _applied) Fill.color = _rest;
            if (_tracking && Strip != null && Strip.color == _stripApplied) Strip.color = _stripRest;
            _tracking = false;
            _hover = _press = 0;
            if (_edge != null) _edge.color = Color.clear;
            SetDrawn(1, 0);
            if (_live) { _live = false; Live.Remove(this); }
        }

        private void Apply()
        {
            var T = _theme;
            if (Fill != null && _tracking)
            {
                if (Fill.color != _applied) _rest = Fill.color; // recoloured while hovered: that's the rest now
                var c = Mix(_rest, Style == HoverStyle.Node ? Color.white : T.Text, HoverAmount(T, _rest) * _hover);
                if (_press > 0) c = Color.Lerp(c, Mix(_rest, T.Base, T.PressMix), _press); // pressed: from the rest, not the hover
                Fill.color = _applied = c;
            }
            if (Strip != null && _tracking)
            {
                if (Strip.color != _stripApplied) _stripRest = Strip.color;
                Strip.color = _stripApplied = Mix(_stripRest, T.Text, T.HoverMix * _hover);
            }
            if (Style != HoverStyle.Node && _hover > 0 && _edge == null) MakeEdge();
            if (_edge != null) _edge.color = T.HoverEdge.WithAlpha(T.HoverEdge.a * _hover);
            float scale = 1, lift = 0;
            if (Moves && !FxSettings.ReducedMotion)
            {
                if (Style == HoverStyle.Button) scale = Mathf.Lerp(1, T.PressScale, _press);
                else if (Style == HoverStyle.Node) scale = Mathf.Lerp(1, UiTheme.NodeHoverScale, _hover);
                else if (Style == HoverStyle.Tile) lift = UiTheme.TileLift * _hover;
            }
            SetDrawn(scale, lift);
        }

        private float HoverAmount(UiTheme T, Color rest) => Style switch
        {
            HoverStyle.Row or HoverStyle.Tile => T.RowHoverMix,
            HoverStyle.Node => UiTheme.RingHoverMix,
            _ => IsAccent(rest) ? T.AccentHoverMix : T.HoverMix,
        };

        /// <summary>A saturated, bright fill (Primary red, a chip): its white label needs a gentler hover.</summary>
        private static bool IsAccent(Color c)
        {
            Color.RGBToHSV(c, out _, out float s, out float v);
            return s > 0.5f && v > 0.45f;
        }

        /// <summary>Toward <paramref name="to"/> by <paramref name="t"/>; a see-through fill gains a little opacity (a faint wash).</summary>
        private static Color Mix(Color from, Color to, float t) =>
            new Color(Mathf.Lerp(from.r, to.r, t), Mathf.Lerp(from.g, to.g, t), Mathf.Lerp(from.b, to.b, t), Mathf.Lerp(from.a, 1, t));

        private void MakeEdge()
        {
            var rt = Ui.Stretched("HoverEdge", VisualRect, Vector2.zero, Vector2.one);
            rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            _edge = rt.gameObject.AddComponent<HoverEdge>();
            _edge.raycastTarget = false;
            _edge.Bar = Style == HoverStyle.Row;
            _edge.color = Color.clear;
            if (_meshed) _meshes.Add(_edge.gameObject.AddComponent<HoverMesh>().For(this));
        }

        /// <summary>Re-meshes what's drawn (scale and lift) only when they change.</summary>
        private void SetDrawn(float scale, float lift)
        {
            if (Mathf.Approximately(scale, _drawnScale) && Mathf.Approximately(lift, _drawnLift)) return;
            bool starting = Mathf.Approximately(_drawnScale, 1) && Mathf.Approximately(_drawnLift, 0);
            _drawnScale = scale;
            _drawnLift = lift;
            if (starting)
            {
                // Each time something starts to move (not every frame): every graphic under the visual draws through a
                // HoverMesh, graphics added since the last hover included.
                _meshed = true;
                VisualRect.GetComponentsInChildren(true, Buffer);
                foreach (var g in Buffer)
                {
                    if (!g.TryGetComponent<HoverMesh>(out var m)) m = g.gameObject.AddComponent<HoverMesh>();
                    if (!_meshes.Contains(m)) _meshes.Add(m.For(this));
                }
                Buffer.Clear();
            }
            for (int i = 0; i < _meshes.Count; i++)
                if (_meshes[i] != null) _meshes[i].Dirty();
        }

        private void OnDisable()
        {
            _inside = _down = _focused = false;
            if (_tracking || _live) Rest();
        }

        private void OnDestroy()
        {
            if (_live) Live.Remove(this);
            _live = false;
            if (_followsTheme) UiTheme.Changed -= OnThemeChanged;
        }

        /// <summary>For checks: the theme this hover draws its tokens from.</summary>
        public UiTheme DebugTheme => _theme;
    }

    /// <summary>
    /// Draws a graphic scaled about its <see cref="UiHover"/>'s visual centre and lifted, by moving the mesh's vertices:
    /// the transform, the layout and the raycast rect stay where they are.
    /// </summary>
    public sealed class HoverMesh : BaseMeshEffect
    {
        private UiHover? _owner;
        private Graphic? _graphic;

        internal HoverMesh For(UiHover owner)
        {
            _owner = owner;
            _graphic = GetComponent<Graphic>();
            return this;
        }

        internal void Dirty()
        {
            if (_graphic != null) _graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || _owner == null) return;
            float s = _owner.DrawnScale, lift = _owner.DrawnLift;
            if (Mathf.Approximately(s, 1) && Mathf.Approximately(lift, 0)) return;
            var visual = _owner.VisualRect;
            var self = transform;
            Vector3 c = self.InverseTransformPoint(visual.TransformPoint(visual.rect.center));
            Vector3 up = self.InverseTransformVector(visual.TransformVector(new Vector3(0, lift, 0)));
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                var p = v.position;
                v.position = new Vector3(c.x + (p.x - c.x) * s + up.x, c.y + (p.y - c.y) * s + up.y, p.z);
                vh.SetUIVertex(v, i);
            }
        }
    }

    /// <summary>The hover's hairline: a 1 px inner frame, or (rows) a 2 px bar down the left side. One graphic, no sprite.</summary>
    public sealed class HoverEdge : MaskableGraphic
    {
        public bool Bar;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            if (Bar) { Quad(vh, r.xMin, r.yMin, r.xMin + 2, r.yMax); return; }
            const float w = 1;
            Quad(vh, r.xMin, r.yMax - w, r.xMax, r.yMax);
            Quad(vh, r.xMin, r.yMin, r.xMax, r.yMin + w);
            Quad(vh, r.xMin, r.yMin + w, r.xMin + w, r.yMax - w);
            Quad(vh, r.xMax - w, r.yMin + w, r.xMax, r.yMax - w);
        }

        private void Quad(VertexHelper vh, float x0, float y0, float x1, float y1)
        {
            int i = vh.currentVertCount;
            var c = (Color32)color;
            vh.AddVert(new Vector3(x0, y0), c, Vector2.zero);
            vh.AddVert(new Vector3(x0, y1), c, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), c, Vector2.zero);
            vh.AddVert(new Vector3(x1, y0), c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }

    /// <summary>
    /// On a keyboard highlight's gold frame (ArrowNav.Mark's, the grading desk's row frame): drawn in highlight mode only,
    /// hidden while the mouse is in use (<see cref="InputMode"/>). The object stays active, so where the highlight is
    /// (and the checks that read it) doesn't change.
    /// </summary>
    public sealed class KeyFrame : MonoBehaviour
    {
        private CanvasGroup? _group;

        /// <summary>Whether the frame is drawn now (highlight mode), for checks.</summary>
        public bool Shown => _group != null && _group.alpha > 0.5f;

        private void OnEnable()
        {
            // A CanvasGroup on the frame itself: it covers graphics added under it later (the grading desk draws its four
            // bars after adding this) and leaves each Graphic's enabled alone.
            if (_group == null && !TryGetComponent(out _group)) _group = gameObject.AddComponent<CanvasGroup>();
            _group!.blocksRaycasts = false;
            _group.interactable = false;
            InputMode.Changed += Apply;
            Apply();
        }

        private void OnDisable() => InputMode.Changed -= Apply;

        private void Apply()
        {
            if (_group != null) _group.alpha = InputMode.Pointer ? 0 : 1;
        }
    }
}
