using System;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The duelist panels (docs/dev/DUELIST-PANEL-UX.md): an angular HUD frame per duelist at the field's left edge, the
    /// character standing over it, then a slanted name tab, the LP (a tag and a large number, with the last change and its
    /// reason beside it) and a bar of eight segments of 1,000 LP, with the deck underneath.
    /// The bar warns in steps, each with a shape as well as a color: amber below half, red with stripes at a quarter, and
    /// at 1,000 or less pulsing with "Low" on the LP tag. The active duelist's panel is lit and tagged with whose turn it
    /// is; the other dims. In a tag duel the partner waits in a small round portrait at the panel's edge. At 0 LP the
    /// portrait greys and a crack runs across the panel.
    /// </summary>
    public sealed partial class BoardView
    {
        private const int Segments = 8;
        private const float SegmentWidth = 33, SegmentGap = 4, SegmentHeight = 12, SegmentSlant = 6;
        private static readonly Color Amber = Ui.Hex("#F0B429"), Danger = Ui.Hex("#FF4A4A");
        /// <summary>The opponent's healthy bar: a light coral, so red on their panel only ever means danger.</summary>
        private static readonly Color OpponentHealthy = Ui.Hex("#FF9AA2");

        private readonly Image[][] _segFill = new Image[2][], _segTrail = new Image[2][];
        private readonly RawImage[][] _segStripes = new RawImage[2][];
        private readonly Text[] _lpTag = new Text[2];
        private readonly UiPolygon[] _lpTagEdge = new UiPolygon[2];
        private readonly RectTransform[] _chip = new RectTransform[2];
        private readonly CanvasGroup[] _chipFade = new CanvasGroup[2];
        private readonly UiPolygon[] _chipBack = new UiPolygon[2];
        private readonly RawImage[] _chipIcon = new RawImage[2];
        private readonly Text[] _chipText = new Text[2];
        private readonly float[] _chipUntil = new float[2];
        private readonly UiPolygon[] _nameTab = new UiPolygon[2];
        private readonly GameObject[] _turnTag = new GameObject[2];
        private readonly Text[] _turnText = new Text[2];
        private readonly CanvasGroup[] _plateFade = new CanvasGroup[2];
        private readonly RectTransform[] _partnerChip = new RectTransform[2];
        private readonly Text[] _partnerName = new Text[2];
        private readonly string[] _partnerId = { "", "" };
        private readonly GameObject[] _crack = new GameObject[2];
        private int _activePanel = -1;
        private static Texture2D? _stripes;

        /// <summary>Whose panel is lit (the turn player's), -1 before the first turn.</summary>
        public int ActivePanel => _activePanel;
        /// <summary>For checks: what a panel's change chip says (empty when it's hidden), and its LP tag.</summary>
        public string ChangeChip(int p) => _chipFade[p] != null && _chipFade[p].alpha > 0.01f ? _chipText[p].text : "";
        public string LifePointTag(int p) => _lpTag[p]?.text ?? "";
        /// <summary>For checks: the LP bar's segments (lit share of each) and the bar's color.</summary>
        public (float[] lit, Color color) LifePointBar(int p) =>
            (Array.ConvertAll(_segFill[p], f => f.rectTransform.sizeDelta.x / SegmentWidth), _segFill[p][0].color);

        private void BuildPanels()
        {
            for (int p = 0; p < 2; p++)
            {
                var accent = p == 0 ? Ui.Hex("#4FA3FF") : Ui.Hex("#FF4F5E");
                var plate = Ui.Placed($"Plate{p}", _field, new Vector2(-760, p == 0 ? -295 : 295), new Vector2(PanelSize.x, PanelSize.y));
                _plateFade[p] = plate.gameObject.AddComponent<CanvasGroup>();
                _plateFade[p].blocksRaycasts = false;
                // Frame: a dark pane, a bright edge (it pulses in danger: the Outline below), an inner line, two shards.
                var shape = UiPolygon.Chamfered(380, 300, 46, 30, 56, 18);
                Poly("Pane", plate, shape, new Vector2(0, -40), new Vector2(380, 300), new Color(0.03f, 0.05f, 0.1f, 0.88f), 0);
                var glow = Ui.Placed("Glow", plate, new Vector2(-20, 40), new Vector2(360, 360)).gameObject.AddComponent<RawImage>();
                glow.texture = BoardFx.Textures.Glow;
                glow.color = new Color(accent.r, accent.g, accent.b, 0.28f);
                glow.raycastTarget = false;
                // The frame comes before the character, so the character stands over its edge; the name, LP and bar
                // come after, over the character.
                var edgePoly = Poly("Edge", plate, shape, new Vector2(0, -40), new Vector2(380, 300), accent, 4);
                var frame = edgePoly.gameObject.AddComponent<Outline>();
                frame.effectColor = new Color(accent.r, accent.g, accent.b, 0.8f);
                frame.effectDistance = new Vector2(2, -2);
                Poly("Inner", plate, UiPolygon.Chamfered(356, 276, 38, 24, 48, 12), new Vector2(0, -40), new Vector2(380, 300),
                    new Color(accent.r, accent.g, accent.b, 0.4f), 1.5f);
                for (int k = 0; k < 2; k++)
                {
                    var shard = Ui.Placed("Shard", plate, new Vector2(186 + k * 12, 106 + k * 12), new Vector2(8, 30 - k * 10));
                    shard.localRotation = Quaternion.Euler(0, 0, 45);
                    Ui.Image(shard, new Color(accent.r, accent.g, accent.b, 0.9f - k * 0.35f)).raycastTarget = false;
                }
                // Portrait: the character stands in the frame, head and shoulders above its top edge (no mask).
                var window = Ui.Placed("Portrait", plate, new Vector2(-30, 50), new Vector2(300, 320));
                _plates[p] = window;
                // Behind the text, a shadow over the character's lower half keeps the name and LP readable.
                var shade = Ui.Placed("Shade", plate, new Vector2(0, -128), new Vector2(372, 120)).gameObject.AddComponent<RawImage>();
                shade.texture = BoardFx.Textures.Glow;
                shade.color = new Color(0, 0, 0, 0.75f);
                shade.raycastTarget = false;

                // Name: dark text on a slanted tab in the side's color (sized to the name when it's drawn).
                _nameTab[p] = Poly("NameTab", plate, Slanted(0, 36, 10), new Vector2(-176, -60), new Vector2(10, 36), accent, 0);
                _nameTab[p].rectTransform.pivot = new Vector2(0, 0.5f);
                _nameTab[p].rectTransform.anchoredPosition = new Vector2(-176, -60);
                _plateName[p] = Ui.Text(Ui.Placed("Name", plate, Vector2.zero, new Vector2(260, 34)), "", 24, Ui.Hex("#06101F"), TextAnchor.MiddleLeft);
                _plateName[p].rectTransform.pivot = new Vector2(0, 0.5f);
                _plateName[p].rectTransform.anchoredPosition = new Vector2(-162, -60);
                _plateName[p].fontStyle = FontStyle.Bold;
                _plateName[p].supportRichText = true;
                _plateName[p].horizontalOverflow = HorizontalWrapMode.Overflow;
                // "Your turn" / "Their turn": a light slanted tag at the right of the name row, on the active panel only.
                var turn = Ui.Placed("TurnTag", plate, new Vector2(140, -60), new Vector2(96, 26));
                Poly("Back", turn, Slanted(96, 26, 8), Vector2.zero, new Vector2(96, 26), Ui.Hex("#E9F1FF"), 0);
                _turnText[p] = Ui.Text(Ui.Stretched("Label", turn, Vector2.zero, Vector2.one), "", 14, Ui.Hex("#06101F"));
                _turnText[p].fontStyle = FontStyle.Bold;
                _turnText[p].resizeTextForBestFit = true; // a partner's name ("Ryo's turn") still fits the tag
                _turnText[p].resizeTextMinSize = 10;
                _turnText[p].resizeTextMaxSize = 14;
                _turnTag[p] = turn.gameObject;
                turn.gameObject.SetActive(false);

                // LP: an outlined slanted tag ("LP", or "! Low" in danger), the number, then the last change.
                var tag = Ui.Placed("LPTag", plate, new Vector2(-155, -102), new Vector2(44, 26));
                _lpTagEdge[p] = Poly("Edge", tag, Slanted(44, 26, 6), Vector2.zero, new Vector2(44, 26), accent, 1.6f);
                _lpTag[p] = Ui.Text(Ui.Stretched("Label", tag, Vector2.zero, Vector2.one), "LP", 15, accent);
                _lpTag[p].fontStyle = FontStyle.Bold;
                _lpTag[p].supportRichText = true;
                // The number starts right after the tag (its pivot at its left edge: the danger pulse grows from there).
                var lpBox = Ui.Placed("LP", plate, Vector2.zero, new Vector2(200, 60));
                lpBox.pivot = new Vector2(0, 0.5f);
                lpBox.anchoredPosition = new Vector2(-122, -102);
                _lifePoints[p] = Ui.Text(lpBox, "", 50, Color.white, TextAnchor.MiddleLeft);
                _lifePoints[p].supportRichText = true;
                _lifePoints[p].fontStyle = FontStyle.Bold;
                _lifePoints[p].horizontalOverflow = HorizontalWrapMode.Overflow;
                var chip = Ui.Placed("Change", plate, Vector2.zero, new Vector2(96, 26));
                chip.pivot = new Vector2(0, 0.5f);
                _chip[p] = chip;
                _chipFade[p] = chip.gameObject.AddComponent<CanvasGroup>();
                _chipFade[p].alpha = 0;
                _chipFade[p].blocksRaycasts = false;
                _chipBack[p] = Poly("Back", chip, Slanted(96, 26, 8), new Vector2(48, 0), new Vector2(96, 26), Color.black, 0);
                _chipIcon[p] = Ui.Placed("Icon", chip, new Vector2(18, 0), new Vector2(16, 16)).gameObject.AddComponent<RawImage>();
                _chipIcon[p].raycastTarget = false;
                _chipText[p] = Ui.Text(Ui.Placed("Amount", chip, new Vector2(58, 0), new Vector2(70, 26)), "", 15, Color.white, TextAnchor.MiddleLeft);
                _chipText[p].fontStyle = FontStyle.Bold;

                // The bar: eight slanted segments of 1,000 LP, each a mask over its trail (white) and its fill.
                _segFill[p] = new Image[Segments];
                _segTrail[p] = new Image[Segments];
                _segStripes[p] = new RawImage[Segments];
                for (int i = 0; i < Segments; i++)
                {
                    float x = -170 + i * (SegmentWidth + SegmentGap) + SegmentWidth / 2;
                    var seg = Poly($"Segment{i}", plate, Slanted(SegmentWidth, SegmentHeight, SegmentSlant), new Vector2(x, -138),
                        new Vector2(SegmentWidth, SegmentHeight), new Color(0.1f, 0.13f, 0.21f, 1), 0);
                    seg.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                    Image Layer(string name, Color color)
                    {
                        var rt = Ui.Rect(name, seg.rectTransform);
                        rt.anchorMin = new Vector2(0, 0);
                        rt.anchorMax = new Vector2(0, 1);
                        rt.pivot = new Vector2(0, 0.5f);
                        rt.offsetMin = rt.offsetMax = Vector2.zero;
                        rt.sizeDelta = new Vector2(SegmentWidth, 0);
                        var img = Ui.Image(rt, color);
                        img.raycastTarget = false;
                        return img;
                    }
                    _segTrail[p][i] = Layer("Trail", new Color(1, 1, 1, 0.9f));
                    _segFill[p][i] = Layer("Fill", accent);
                    var stripes = Ui.Stretched("Stripes", _segFill[p][i].rectTransform, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
                    stripes.texture = Stripes;
                    stripes.uvRect = new Rect(0, 0, SegmentWidth / 12f, 1);
                    stripes.raycastTarget = false;
                    stripes.enabled = false;
                    _segStripes[p][i] = stripes;
                }
                _plateRoot[p] = plate;
                _plateHome[p] = plate.anchoredPosition;
                _plateFrame[p] = frame;
                _plateAccent[p] = accent;
                var emblem = Ui.Placed("Emblem", plate, new Vector2(-157, -167), new Vector2(26, 26)).gameObject.AddComponent<RawImage>();
                emblem.texture = Icons.For("shield");
                emblem.color = accent;
                emblem.raycastTarget = false;
                _plateDeck[p] = Ui.Text(Ui.Placed("Deck", plate, new Vector2(7, -167), new Vector2(290, 30)), "", 20, new Color(0.82f, 0.86f, 0.94f), TextAnchor.MiddleLeft);

                // A tag duel's partner: a small round portrait at the frame's right edge, with their name under it.
                var partner = Ui.Placed("Partner", plate, new Vector2(150, 22), new Vector2(66, 66));
                Ui.Image(partner, accent).sprite = HudShapes.Circle;
                var face = Ui.Placed("Face", partner, Vector2.zero, new Vector2(60, 60));
                var faceMask = Ui.Image(face, new Color(0.11f, 0.15f, 0.25f, 1));
                faceMask.sprite = HudShapes.Circle;
                face.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                _partnerName[p] = Ui.Text(Ui.Placed("Name", partner, new Vector2(0, -45), new Vector2(130, 22)), "", 15, new Color(0.9f, 0.93f, 0.98f));
                _partnerName[p].fontStyle = FontStyle.Bold;
                // A dark edge so the name reads over the mat and the character art.
                var partnerEdge = _partnerName[p].gameObject.AddComponent<Outline>();
                partnerEdge.effectColor = new Color(0, 0, 0, 0.9f);
                partnerEdge.effectDistance = new Vector2(1.5f, -1.5f);
                _partnerChip[p] = partner;
                partner.gameObject.SetActive(false);

                // Defeat: a crack across the panel (three jagged strokes), shown at 0 LP.
                var crack = Ui.Rect("Crack", plate);
                crack.sizeDelta = new Vector2(380, 300);
                crack.anchoredPosition = new Vector2(0, -40);
                foreach (var (from, to) in new[] { (new Vector2(-150, 120), new Vector2(-60, 20)), (new Vector2(-60, 20), new Vector2(10, 60)), (new Vector2(10, 60), new Vector2(120, -110)) })
                {
                    var line = Ui.Placed("Stroke", crack, (from + to) / 2, new Vector2(Vector2.Distance(from, to), 4));
                    line.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg);
                    Ui.Image(line, new Color(1f, 0.92f, 0.9f, 0.9f)).raycastTarget = false;
                }
                _crack[p] = crack.gameObject;
                crack.gameObject.SetActive(false);
            }
        }

        private static UiPolygon Poly(string name, RectTransform parent, Vector2[] points, Vector2 at, Vector2 size, Color color, float thickness)
        {
            var rt = Ui.Placed(name, parent, at, size);
            var poly = rt.gameObject.AddComponent<UiPolygon>();
            poly.Thickness = thickness;
            poly.SetPoints(points);
            poly.color = color;
            poly.raycastTarget = false;
            return poly;
        }

        /// <summary>A parallelogram leaning right, <paramref name="width"/> by <paramref name="height"/>, centered.</summary>
        private static Vector2[] Slanted(float width, float height, float slant) => new[]
        {
            new Vector2(-width / 2 + slant / 2, height / 2), new Vector2(width / 2 + slant / 2, height / 2),
            new Vector2(width / 2 - slant / 2, -height / 2), new Vector2(-width / 2 - slant / 2, -height / 2),
        };

        /// <summary>Diagonal warning stripes (dark over transparent), tiled across a red bar at a quarter of the LP or less.</summary>
        private static Texture2D Stripes
        {
            get
            {
                if (_stripes != null) return _stripes;
                const int size = 16;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = (x + y) % size < 6 ? new Color(0.28f, 0.03f, 0.06f, 0.85f) : new Color(0, 0, 0, 0);
                tex.SetPixels(px);
                tex.Apply();
                return _stripes = tex;
            }
        }

        /// <summary>A tag duel's waiting partner on a panel (null or "" hides the chip).</summary>
        public void SetPartner(int player, string? characterId, string name = "")
        {
            _partnerId[player] = characterId ?? "";
            var chip = _partnerChip[player];
            if (chip == null) return;
            chip.gameObject.SetActive(!string.IsNullOrEmpty(characterId));
            if (string.IsNullOrEmpty(characterId)) return;
            var face = (RectTransform)chip.Find("Face");
            for (int i = face.childCount - 1; i >= 0; i--) Destroy(face.GetChild(i).gameObject);
            if (!Portraits.PlaceHead(face, characterId!, mirror: player == 1))
                Ui.Text(Ui.Stretched("Initial", face, Vector2.zero, Vector2.one), name.Length > 0 ? name.Substring(0, 1) : "?", 26, Color.white).fontStyle = FontStyle.Bold;
            _partnerName[player].text = name;
        }

        /// <summary>The last LP change on a panel: its amount and why (a sword, a flame, a coin, a heart), for a few seconds.</summary>
        private void ShowChange(int p, int delta, LpChange why)
        {
            if (_chip[p] == null || delta == 0) return;
            var (icon, back, ink) = why switch
            {
                LpChange.Battle => ("attack", Ui.Hex("#5A1018"), Ui.Hex("#FFD0D4")),
                LpChange.Cost => ("coin", Ui.Hex("#4A3205"), Ui.Hex("#FFE3A8")),
                LpChange.Gain => ("heart", Ui.Hex("#0F4021"), Ui.Hex("#C9F5D6")),
                _ => delta > 0 ? ("heart", Ui.Hex("#0F4021"), Ui.Hex("#C9F5D6")) : ("activate", Ui.Hex("#5A1018"), Ui.Hex("#FFD0D4")),
            };
            _chipBack[p].color = back;
            _chipIcon[p].texture = Icons.For(icon);
            _chipIcon[p].color = ink;
            _chipText[p].color = ink;
            _chipText[p].text = delta > 0 ? $"+{delta}" : $"−{-delta}";
            _chipFade[p].alpha = 1;
            _chipUntil[p] = Time.unscaledTime + 4f;
        }

        /// <summary>Draws a panel's name tab, LP tag, number, bar and defeat state from the LP shown now.</summary>
        private void DrawPanel(int p, string lp)
        {
            // The name: the duelist in play (in a tag duel, the partner waits in the round chip).
            var name = _names[p];
            var amp = name.IndexOf(" & ", StringComparison.Ordinal);
            var who = amp > 0 ? name.Substring(0, amp) : name;
            _plateName[p].text = who.Length > 18 ? $"<size=18>{who}</size>" : who.Length > 13 ? $"<size=21>{who}</size>" : who;
            float tab = Mathf.Min(_plateName[p].preferredWidth + 30, 260);
            _nameTab[p].rectTransform.sizeDelta = new Vector2(tab, 36);
            _nameTab[p].SetPoints(Slanted(tab, 36, 10));
            _nameTab[p].rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _nameTab[p].rectTransform.anchoredPosition = new Vector2(-176 + tab / 2, -60);
            _lifePoints[p].text = lp;

            float shown = Mathf.Max(0, _lpShown[p]);
            float trail = Mathf.Max(_lpTrail[p], shown);
            // A full bar is the duel's starting LP, in eight equal segments (1,000 each from 8,000).
            float scale = Mathf.Max(1, _lpScale[p]), segment = scale / Segments;
            float frac = Mathf.Clamp01(_lpTarget[p] / scale);
            // Danger steps: the side's color above half, amber at half or less, red with stripes at a quarter or less.
            var color = frac > 0.5f ? (p == 1 ? OpponentHealthy : _plateAccent[p]) : frac > 0.25f ? Amber : Danger;
            bool quarter = frac <= 0.25f && _lpTarget[p] > 0;
            for (int i = 0; i < Segments; i++)
            {
                float lit = Mathf.Clamp01((shown - i * segment) / segment), behind = Mathf.Clamp01((trail - i * segment) / segment);
                _segFill[p][i].rectTransform.sizeDelta = new Vector2(SegmentWidth * lit, 0);
                _segTrail[p][i].rectTransform.sizeDelta = new Vector2(SegmentWidth * behind, 0);
                _segFill[p][i].color = new Color(color.r, color.g, color.b, _segFill[p][i].color.a);
                _segStripes[p][i].enabled = quarter;
            }
            // At 1,000 or less the tag says so (with a mark, not only a color); elsewhere it reads "LP" in the bar's color.
            bool low = _lpTarget[p] > 0 && _lpTarget[p] <= 1000;
            _lpTag[p].text = low ? "! Low" : "LP";
            float tagWidth = low ? 64 : 44;
            var tagRt = (RectTransform)_lpTag[p].transform.parent;
            tagRt.sizeDelta = new Vector2(tagWidth, 26);
            tagRt.anchoredPosition = new Vector2(-177 + tagWidth / 2, -102);
            _lpTagEdge[p].rectTransform.sizeDelta = new Vector2(tagWidth, 26);
            _lpTagEdge[p].SetPoints(Slanted(tagWidth, 26, 6));
            _lpTag[p].color = low ? Danger : color;
            _lpTagEdge[p].color = low ? Danger : color;
            ((RectTransform)_lifePoints[p].transform).anchoredPosition = new Vector2(-177 + tagWidth + 10, -102);
            _chip[p].anchoredPosition = new Vector2(-177 + tagWidth + 10 + _lifePoints[p].preferredWidth + 4, -102);
            // Defeat: the portrait greys and the panel cracks.
            bool defeated = _lpTarget[p] <= 0 && _lpShown[p] <= 0.5f;
            _crack[p].SetActive(defeated);
            if (_portraitBase[p] is RawImage portrait && portrait != null)
                portrait.color = defeated ? new Color(0.42f, 0.42f, 0.46f, 1) : Color.white;
        }

        /// <summary>Each frame: the change chip fades, low LP pulses, and the active panel is lit while the other dims.</summary>
        private void UpdatePanels()
        {
            bool calm = FxSettings.ReducedMotion;
            for (int p = 0; p < 2; p++)
            {
                if (_plateRoot[p] == null) continue;
                if (_chipFade[p].alpha > 0 && Time.unscaledTime > _chipUntil[p])
                    _chipFade[p].alpha = Mathf.MoveTowards(_chipFade[p].alpha, 0, Time.unscaledDeltaTime * 3);
                bool low = _lpTarget[p] > 0 && _lpTarget[p] <= 1000;
                float pulse = low && !calm ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4.5f)) : 1;
                foreach (var fill in _segFill[p]) { var c = fill.color; c.a = pulse; fill.color = c; }
                bool active = _activePanel == p;
                _turnTag[p].SetActive(active);
                if (active) _turnText[p].text = HumanSeat < 0 ? "Turn" : p == HumanSeat ? YourSideTurn(p) : "Their turn";
                float target = _activePanel < 0 || active ? 1 : 0.8f;
                _plateFade[p].alpha = calm ? target : Mathf.MoveTowards(_plateFade[p].alpha, target, Time.unscaledDeltaTime * 2);
            }
        }

        /// <summary>
        /// Your side's turn tag: "Your turn", or in a tag duel on your partner's turn (they're the one on your plate, you
        /// wait in the round chip) "{Partner}'s turn".
        /// </summary>
        private string YourSideTurn(int p)
        {
            var who = PlayerName(p);
            bool partnerUp = who.Length > 0 && who != "You" && _partnerChip[p] != null && _partnerChip[p].gameObject.activeSelf;
            return partnerUp ? $"{who}'s turn" : "Your turn";
        }

        /// <summary>The turn player's panel is lit (called on each render).</summary>
        private void SetActivePanel(int turnPlayer) => _activePanel = turnPlayer is 0 or 1 ? turnPlayer : -1;
    }
}
