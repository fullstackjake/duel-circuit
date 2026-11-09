using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// A Gauge &amp; Gilt slab: two clear halves round the card, the card in a recessed well and the parchment label in its
    /// own well at the bottom, with the grade in a flat-top hexagonal gauge on the label's left, our hologram sticker on its
    /// right (docs/dev/SLAB-LOOK-SPEC-2026-11-07.md). Back to front: a soft drop shadow, the wells' floors, the card, the
    /// label, then the glass (<see cref="SlabGlass"/>): the case's edges, reflections and side, drawn over everything.
    /// Full (the reading view, the desk's and van's detail panes at 0.62, the reveal), Reveal and Compact (the harness
    /// only so far). Grid tiles get a static case instead (<see cref="Rim"/> under the card, <see cref="RimGlass"/> over it).
    /// Never on the board.
    /// </summary>
    public static class SlabView
    {
        public enum Size { Full, Compact, Reveal }

        /// <summary>The reveal's stages: the label sealed, then the number stamped into the gauge, then printed.</summary>
        public enum LabelStage { Sealed, Number, Printed }

        /// <summary>A size's measures (layout px at the 1920x1080 reference, from the case's centre, y up).</summary>
        private sealed class Shape
        {
            public Vector2 Case, Card, CardAt, LabelWell, LabelWellAt;
            public float Radius, Bevel, Margin, LabelInset, LabelRadius;
            public Vector2 ShadowOffset, SideOffset;
            public float ShadowBlur, ShadowAlpha, Crest, WellBand, Contact, Pad;
            public bool Fringe, Bands, RidgeDark;
            public int Scuffs;
            public float CardRadius => 0.028f * Card.y; // the card's own rounded corners (CardFoil)
        }

        private static readonly Shape FullShape = new Shape
        {
            Case = new Vector2(360, 586), Radius = 12, Bevel = 6, Card = new Vector2(332, 484), CardAt = new Vector2(0, 37), Margin = 4,
            LabelWell = new Vector2(340, 68), LabelWellAt = new Vector2(0, -249), LabelInset = 2, LabelRadius = 3,
            ShadowOffset = new Vector2(0, -10), ShadowBlur = 18, ShadowAlpha = 0.5f, SideOffset = new Vector2(3, -4),
            Crest = 1.5f, WellBand = 2, Contact = 3, Pad = 6, Fringe = true, Bands = true, RidgeDark = true, Scuffs = 7,
        };

        private static readonly Shape RevealShape = new Shape
        {
            Case = new Vector2(184, 268), Radius = 8, Bevel = 4, Card = new Vector2(143, 208), CardAt = new Vector2(0, 19), Margin = 3,
            LabelWell = new Vector2(168, 33), LabelWellAt = new Vector2(0, -109.5f), LabelInset = 2, LabelRadius = 2,
            ShadowOffset = new Vector2(0, -6), ShadowBlur = 10, ShadowAlpha = 0.45f, SideOffset = new Vector2(2, -3),
            Crest = 1, WellBand = 1.5f, Contact = 2, Pad = 4, Fringe = true, Bands = true, RidgeDark = true, Scuffs = 4,
        };

        private static readonly Shape CompactShape = new Shape
        {
            Case = new Vector2(121, 176), Radius = 6, Bevel = 3, Card = new Vector2(94, 137), CardAt = new Vector2(0, 13.5f), Margin = 2,
            LabelWell = new Vector2(113, 24), LabelWellAt = new Vector2(0, -72), LabelInset = 1, LabelRadius = 1.5f,
            ShadowOffset = new Vector2(0, -4), ShadowBlur = 6, ShadowAlpha = 0.4f, SideOffset = new Vector2(1, -2),
            Crest = 1, WellBand = 1, Contact = 1.5f, Pad = 3, Fringe = false, Bands = false, RidgeDark = false, Scuffs = 0,
        };

        private static Shape ShapeOf(Size size) => size switch { Size.Full => FullShape, Size.Reveal => RevealShape, _ => CompactShape };

        // House colours: parchment and umber, gilt rules; the label never changes colour by grade.
        private static readonly Color Parchment = Ui.Hex("#EFE6D2"), Rule = Ui.Hex("#A8823A"), Ring = Ui.Hex("#C9A04A"), RingFlash = Ui.Hex("#F6DE8E");
        private static readonly Color Umber = Ui.Hex("#2B2118"), Brown = Ui.Hex("#4A3B28"), House = Ui.Hex("#6B5226"), Numeral = Ui.Hex("#F3E3B5");

        /// <summary>The certificate number on a Full label, from the copy's seed (stable per copy): "No. 4417-0293".</summary>
        public static string CertNumber(CardCopy copy)
        {
            uint h = Hash.Mix(copy.Seed, 0xCE47u, (uint)copy.Code);
            return $"No. {h % 10000:0000}-{h / 10000 % 10000:0000}";
        }

        /// <summary>
        /// The label's words, for checks and screen readers: "GAUGE &amp; GILT · GRADE 9 MINT · Pot of Greed · LOB-119", and
        /// the cert number where it's printed (Full).
        /// </summary>
        public static string LabelText(CardCopy copy, CardInfo? info, string setCode, Size size = Size.Full) =>
            $"GAUGE & GILT · GRADE {copy.Slab} {Grades.Word(copy.Slab).ToUpperInvariant()} · {info?.Name ?? copy.Code.ToString()}"
            + (setCode.Length > 0 ? $" · {setCode}" : "") + (size == Size.Full ? $" · {CertNumber(copy)}" : "");

        /// <summary>Builds a slab centered at <paramref name="center"/>. <paramref name="sealedLabel"/>: the label not yet printed (the reveal stamps it).</summary>
        public static RectTransform Create(Transform parent, Vector2 center, Size size, CardCopy copy, CardInfo? info, string setCode, bool sealedLabel = false)
        {
            var s = ShapeOf(size);
            var root = Ui.Placed("Slab", parent, center, s.Case);
            var black = new Color(0, 0, 0, s.ShadowAlpha);
            // The room's overhead light: a soft shadow under the case (static; it doesn't follow the pointer).
            Sliced(root, "Shadow", s.ShadowOffset, s.Case + 2 * s.ShadowBlur * Vector2.one, SlabTextures.RoundRect(s.Radius, s.ShadowBlur, black));
            // The wells' floors: only the margin ring shows round the card (the recess, dark through its rounded corners).
            var well = s.Card + 2 * s.Margin * Vector2.one;
            Sliced(root, "WellFloor", s.CardAt, well, SlabTextures.RoundRect(s.CardRadius + s.Margin, 0, new Color(0.024f, 0.031f, 0.047f, 0.6f)));
            Sliced(root, "LabelWellFloor", s.LabelWellAt, s.LabelWell, SlabTextures.RoundRect(s.LabelRadius, 0, new Color(0.024f, 0.031f, 0.047f, 0.45f)));

            // The card.
            var art = Ui.Placed("Art", root, s.CardAt, s.Card).gameObject.AddComponent<RawImage>();
            art.raycastTarget = false;
            var picture = size == Size.Full ? CardView.Art?.GetFull(copy.Code) : CardView.Art?.Get(copy.Code); // Full is big enough for the whole picture
            if (picture != null)
            {
                art.texture = picture;
                CardFoil.Apply(art, info, copy.Rarity, 1f, copy.Look);
            }
            else art.color = info != null ? CardView.FrameColor(info.Type) : Ui.Hex("#6B3F1F");

            // The label, flat print in its well.
            var label = Ui.Placed("Label", root, s.LabelWellAt, s.LabelWell - 2 * s.LabelInset * Vector2.one);
            Ui.Image(label, Parchment).raycastTarget = false;

            // The glass over it all, the case's side included (the quad reaches a little past the case for it).
            var glassRt = Ui.Placed("Glass", root, Vector2.zero, s.Case + 2 * s.Pad * Vector2.one);
            var glass = glassRt.gameObject.AddComponent<RawImage>();
            glass.raycastTarget = false;
            glass.texture = SlabTextures.Sticker;
            var slabGlass = glassRt.gameObject.AddComponent<SlabGlass>();
            slabGlass.Size = size;
            if (SlabGlass.LoadShader() is Shader shader)
            {
                var material = new Material(shader) { name = "Slab glass" };
                Measure(material, s, size);
                slabGlass.Init(size, glass, (RectTransform)art.transform, s.Case, material, copy.Seed);
                slabGlass.Follow(art.GetComponent<CardFoil>());
            }
            else glass.enabled = false;
            SetLabel(root, copy, info, setCode, sealedLabel ? LabelStage.Sealed : LabelStage.Printed);
            return root;
        }

        /// <summary>The case's measures into its glass material (case px from the centre).</summary>
        private static void Measure(Material m, Shape s, Size size)
        {
            Vector4 Rect(Vector2 at, Vector2 size) => new Vector4(at.x - size.x / 2, at.y - size.y / 2, at.x + size.x / 2, at.y + size.y / 2);
            m.SetVector("_CaseSize", new Vector4(s.Case.x, s.Case.y, s.Radius, s.Bevel));
            m.SetVector("_CardRect", Rect(s.CardAt, s.Card));
            m.SetVector("_CardWell", Rect(s.CardAt, s.Card + 2 * s.Margin * Vector2.one));
            m.SetVector("_LabelWell", Rect(s.LabelWellAt, s.LabelWell));
            // The card's name bar: 4.5% to 12.5% of the card's height from its top, the card's width.
            float top = s.CardAt.y + s.Card.y / 2;
            m.SetVector("_NameStrip", new Vector4(s.CardAt.x - s.Card.x / 2, top - 0.125f * s.Card.y, s.CardAt.x + s.Card.x / 2, top - 0.045f * s.Card.y));
            // The label's text column (the grade, the house, the cert and the name): glare and scuffs never cross it.
            var l = LayoutOf(size);
            float labelHalf = (s.LabelWell.y - 2 * s.LabelInset) / 2;
            m.SetVector("_LabelText", new Vector4(s.LabelWellAt.x + l.From, s.LabelWellAt.y - labelHalf, s.LabelWellAt.x + l.To, s.LabelWellAt.y + labelHalf));
            m.SetVector("_Radii", new Vector4(s.CardRadius, s.CardRadius + s.Margin, s.LabelRadius, s.Crest));
            m.SetVector("_Bands", new Vector4(s.WellBand, s.Contact, s.Fringe ? 1 : 0, s.Bands ? 1 : 0));
            m.SetVector("_Side", new Vector4(s.Pad, s.SideOffset.x, s.SideOffset.y, 0));
            m.SetFloat("_ScuffCount", s.Scuffs);
            m.SetFloat("_RidgeDark", s.RidgeDark ? 1 : 0);
            m.SetVector("_StickerRect", Vector4.zero);
            m.SetFloat("_Glint", -1);
            m.SetFloat("_StickerFlash", 0);
        }

        // ---- The label --------------------------------------------------------------------------------------

        /// <summary>
        /// A size's label layout: the gauge (size, ring width, x), the number's size, the text column (x from, x to), the
        /// sticker (size, x; zero: none) and the rules' width.
        /// </summary>
        private readonly struct LabelLayout
        {
            public readonly Vector2 Gauge, Sticker;
            public readonly float Ring, GaugeX, From, To, StickerX, Rules;
            public readonly int Number;

            public LabelLayout(Vector2 gauge, float ring, float gaugeX, int number, float from, float to, Vector2 sticker, float stickerX, float rules)
            {
                Gauge = gauge; Ring = ring; GaugeX = gaugeX; Number = number; From = from; To = to; Sticker = sticker; StickerX = stickerX; Rules = rules;
            }
        }

        private static LabelLayout LayoutOf(Size size) => size switch
        {
            Size.Full => new LabelLayout(new Vector2(58, 50), 2.5f, -132, 28, -95, 121, new Vector2(34, 39), 145, 2),
            Size.Reveal => new LabelLayout(new Vector2(29, 25), 1.5f, -63.5f, 15, -44, 56, new Vector2(17, 20), 69.5f, 1),
            _ => new LabelLayout(new Vector2(23, 20), 1.5f, -42, 11, -26, 52, Vector2.zero, 0, 1),
        };

        /// <summary>
        /// Prints a slab's label at a stage (rebuilding only the label's children, so the glass and its light run on):
        /// Sealed (the gauge empty), Number (stamped into the gauge, the label still sealed), Printed (the grade, the house,
        /// the cert and the card's name, and the sticker). Text never leaves its row: each row shrinks to its minimum size,
        /// and the name row then ends in "…".
        /// </summary>
        public static void SetLabel(RectTransform slab, CardCopy copy, CardInfo? info, string setCode, LabelStage stage)
        {
            var label = (RectTransform)slab.Find("Label");
            var glass = slab.GetComponentInChildren<SlabGlass>();
            var size = glass != null ? glass.Size : Size.Full;
            for (int i = label.childCount - 1; i >= 0; i--)
            {
                var child = label.GetChild(i);
                child.SetParent(null, false);
                Object.Destroy(child.gameObject);
            }
            var l = LayoutOf(size);
            var w = label.sizeDelta.x;
            float half = label.sizeDelta.y / 2;
            Bar(label, "RuleTop", new Vector2(0, half - l.Rules / 2), new Vector2(w, l.Rules), Rule);
            Bar(label, "RuleBottom", new Vector2(0, -half + l.Rules / 2), new Vector2(w, l.Rules), Rule);
            // The gauge: a flat-top hexagon, a gold ring round a dark centre, the number on it.
            var at = new Vector2(l.GaugeX, 0);
            Hex(label, "Gauge", at, l.Gauge, Ring);
            Hex(label, "GaugeInner", at, new Vector2(l.Gauge.x - 2 * l.Ring * 1.155f, l.Gauge.y - 2 * l.Ring), Umber);
            var number = Text(label, "Number", at, l.Gauge, stage == LabelStage.Sealed ? "" : copy.Slab.ToString(), l.Number, l.Number, Numeral, TextAnchor.MiddleCenter);
            number.fontStyle = FontStyle.Bold;

            float colW = l.To - l.From, colX = (l.From + l.To) / 2;
            string word = Grades.Word(copy.Slab).ToUpperInvariant();
            if (stage != LabelStage.Printed)
            {
                var (seal, max, min) = size switch { Size.Full => ("SEALED · GAUGE & GILT", 14, 10), Size.Reveal => ("SEALED", 11, 9), _ => ("SEALED", 9, 8) };
                Text(label, "Seal", new Vector2(colX, 0), new Vector2(colW, Mathf.Ceil(max * 1.25f)), seal, max, min, House).fontStyle = FontStyle.Bold;
            }
            else if (size == Size.Full)
            {
                Text(label, "House", new Vector2(l.From + 52.5f, 20), new Vector2(105, 14), "GAUGE & GILT", 10, 10, House).fontStyle = FontStyle.Bold;
                Text(label, "Cert", new Vector2((10 + l.To) / 2, 20), new Vector2(l.To - 10, 14), CertNumber(copy), 10, 10, House, TextAnchor.MiddleRight);
                Text(label, "Grade", new Vector2(colX, 1), new Vector2(colW, 22), $"{word} {Grades.Glyph(copy.Slab)}", 18, 13, Umber).fontStyle = FontStyle.Bold;
                var name = Text(label, "Name", new Vector2(colX, -19), new Vector2(colW, 15), "", 12, 9, Brown);
                name.text = FitOneLine(name, (info?.Name ?? "") + (setCode.Length > 0 ? $" · {setCode}" : ""));
            }
            else if (size == Size.Reveal)
            {
                Text(label, "Grade", new Vector2(colX, 6), new Vector2(colW, 14), $"{word} {Grades.Glyph(copy.Slab)}", 12, 9, Umber).fontStyle = FontStyle.Bold;
                Text(label, "House", new Vector2(colX, -7.5f), new Vector2(colW, 11), "GAUGE & GILT", 9, 9, House).fontStyle = FontStyle.Bold;
            }
            else Text(label, "Grade", new Vector2(colX, 0), new Vector2(colW, 13), word, 10, 8, Umber).fontStyle = FontStyle.Bold;

            // The sticker (in the glass, so it takes the light): printed labels at Full and Reveal only.
            if (glass != null)
                glass.SetSticker(stage == LabelStage.Printed && l.Sticker.x > 0 ? StickerRect(slab, label, l, 1) : Vector4.zero);
        }

        /// <summary>The sticker's rect in case px, scaled about its centre (the reveal pops it in from 1.15).</summary>
        private static Vector4 StickerRect(RectTransform slab, RectTransform label, LabelLayout l, float scale)
        {
            var c = label.anchoredPosition + new Vector2(l.StickerX, 0);
            var h = l.Sticker * scale / 2;
            return new Vector4(c.x - h.x, c.y - h.y, c.x + h.x, c.y + h.y);
        }

        /// <summary>Scales a printed slab's sticker (the reveal), 1 at rest.</summary>
        public static void ScaleSticker(RectTransform slab, float scale)
        {
            var glass = slab.GetComponentInChildren<SlabGlass>();
            if (glass == null || glass.Size == Size.Compact) return;
            glass.SetSticker(StickerRect(slab, (RectTransform)slab.Find("Label"), LayoutOf(glass.Size), scale));
        }

        /// <summary>The gauge ring's colour, from the stamp's flash (1) back to gold (0).</summary>
        public static void FlashGauge(RectTransform slab, float flash)
        {
            if (slab.Find("Label/Gauge")?.GetComponent<Image>() is Image ring) ring.color = Color.Lerp(Ring, RingFlash, flash);
        }

        private static void Bar(RectTransform parent, string name, Vector2 at, Vector2 size, Color color) =>
            Ui.Image(Ui.Placed(name, parent, at, size), color).raycastTarget = false;

        private static void Hex(RectTransform parent, string name, Vector2 at, Vector2 size, Color color)
        {
            var image = Ui.Image(Ui.Placed(name, parent, at, size), color);
            image.sprite = SlabTextures.Hexagon;
            image.raycastTarget = false;
        }

        private static void Sliced(RectTransform parent, string name, Vector2 at, Vector2 size, Sprite sprite)
        {
            var image = Ui.Image(Ui.Placed(name, parent, at, size), Color.white);
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
        }

        /// <summary>
        /// A label row: wraps (so best fit works) and truncates, shrinking from <paramref name="max"/> to
        /// <paramref name="min"/> to stay on its one line.
        /// </summary>
        private static Text Text(RectTransform parent, string name, Vector2 center, Vector2 size, string text, int max, int min, Color color,
            TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var t = Ui.Text(Ui.Placed(name, parent, center, size), text, max, color, anchor);
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            if (min < max)
            {
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = min;
                t.resizeTextMaxSize = max;
            }
            return t;
        }

        /// <summary>A text's width on one line at a font size (layout units).</summary>
        private static float WidthAt(Text t, string text, int size)
        {
            var settings = t.GetGenerationSettings(Vector2.zero);
            settings.fontSize = size;
            settings.resizeTextForBestFit = false;
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t.cachedTextGeneratorForLayout.GetPreferredWidth(text, settings) / t.pixelsPerUnit;
        }

        /// <summary><paramref name="text"/>, or as many of its words as fit on one line at the row's minimum size, then "…".</summary>
        private static string FitOneLine(Text t, string text)
        {
            float room = ((RectTransform)t.transform).sizeDelta.x;
            int min = t.resizeTextForBestFit ? t.resizeTextMinSize : t.fontSize;
            if (WidthAt(t, text, min) <= room) return text;
            var words = new List<string>(text.Split(' '));
            while (words.Count > 1)
            {
                words.RemoveAt(words.Count - 1);
                var cut = string.Join(" ", words).TrimEnd('·', ' ') + "…";
                if (WidthAt(t, cut, min) <= room) return cut;
            }
            return "…";
        }

        /// <summary>
        /// For the label fit check: each of a slab's label texts, the size best fit settles on (the largest from its max
        /// down that fits on one line) and whether it fits at all: (child, size, width, room, ok).
        /// </summary>
        public static List<(string child, int size, float width, float room, bool ok)> Fit(RectTransform slab)
        {
            var result = new List<(string, int, float, float, bool)>();
            foreach (var t in slab.Find("Label").GetComponentsInChildren<Text>())
            {
                if (t.text.Length == 0) continue;
                float room = ((RectTransform)t.transform).sizeDelta.x;
                int max = t.resizeTextForBestFit ? t.resizeTextMaxSize : t.fontSize, min = t.resizeTextForBestFit ? t.resizeTextMinSize : t.fontSize;
                int used = min;
                for (int size = max; size >= min; size--)
                    if (WidthAt(t, t.text, size) <= room) { used = size; break; }
                float width = WidthAt(t, t.text, used);
                result.Add((t.name, used, width, room, width <= room + 0.5f));
            }
            return result;
        }

        // ---- Grid tiles -------------------------------------------------------------------------------------

        /// <summary>The ring's width round a grid tile (4 on cards 100 tall or more, 2 on the small list thumbs).</summary>
        private static int RimRing(Vector2 cardSize) => cardSize.y >= 100 ? 4 : 2;

        /// <summary>
        /// The case under a grid tile (the collection grid, the market, the shelves): its soft shadow and its side. Call it
        /// before the card, and <see cref="RimGlass"/> after.
        /// </summary>
        public static void Rim(Transform parent, Vector2 center, Vector2 cardSize)
        {
            int ring = RimRing(cardSize);
            var outer = cardSize + 2 * (ring + 1) * Vector2.one;
            var rim = Ui.Placed("SlabRim", parent, center, outer);
            rim.SetAsFirstSibling();
            bool big = cardSize.y >= 100;
            Sliced(rim, "Shadow", new Vector2(0, -2), outer + (big ? 6 : 4) * Vector2.one, SlabTextures.RoundRect(4, big ? 3 : 2, new Color(0, 0, 0, 0.35f)));
            Sliced(rim, "Side", new Vector2(1, -1), outer, SlabTextures.RoundRect(4, 0, new Color(0.42f, 0.48f, 0.55f, 0.55f), new Color(0, 0, 0, 0.5f), 1));
        }

        /// <summary>
        /// The case over a grid tile: a static glass (shared sprites, so no material and no Update) with a dark rim, a veil,
        /// the crest lit on the top and left, the house's gilt line at the card's edge and one faint reflection band. Call
        /// it right after the card is made, and pass the card (a <see cref="CardView"/>) so its bright owner border turns
        /// into the case's dark well and the gilt reads round it.
        /// </summary>
        public static void RimGlass(Transform parent, Vector2 center, Vector2 cardSize, Component? card = null)
        {
            if (card != null && card.GetComponent<CardView>() is CardView view) view.QuietBorder(new Color(0.024f, 0.031f, 0.047f, 0.85f));
            int ring = RimRing(cardSize);
            var outer = cardSize + 2 * (ring + 1) * Vector2.one;
            var glass = Ui.Placed("SlabRimGlass", parent, center, outer);
            Sliced(glass, "Ring", Vector2.zero, outer, SlabTextures.RimRing(ring));
            var band = Ui.Image(Ui.Placed("Band", glass, Vector2.zero, cardSize), Color.white);
            band.sprite = SlabTextures.RimBand;
            band.raycastTarget = false;
        }
    }
}
