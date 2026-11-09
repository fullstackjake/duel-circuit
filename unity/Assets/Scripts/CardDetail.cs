using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;
using Ygo.Engine.Interactive;
using Ygo.Engine.Presentation;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The hovered card, Master Duel style: a name bar in the card's frame color with its Attribute, the picture with a
    /// rarity badge, Level / ATK / DEF with icons, a [Type/Ability] bar, and the effect text (scrolls when long). Older
    /// card versions (Goat, pre-errata) are marked. With less height and enough width (free play's panel) the text sits
    /// beside the picture instead of under it. The duel's card reader (reading) puts the picture big on the left and
    /// everything else, larger, in a column on the right. With <see cref="StylePicker"/> on, an Artwork button on the picture
    /// cycles through the art styles you have pictures of for the card (see <see cref="CardArt"/>).
    /// </summary>
    public sealed class CardDetail
    {
        private static readonly Dictionary<uint, (string glyph, string color)> AttributeLooks = new Dictionary<uint, (string, string)>
        {
            [0x01] = ("地", "#8A6A3A"), [0x02] = ("水", "#2E7FD0"), [0x04] = ("炎", "#D0402E"), [0x08] = ("風", "#3FA04A"),
            [0x10] = ("光", "#D9B52E"), [0x20] = ("闇", "#7B3FA0"), [0x40] = ("神", "#C9A227"),
        };

        private readonly RectTransform _root;
        private readonly RectTransform _header, _artRt, _stats, _typeBar, _textView, _textContent, _rarityRt;
        private readonly Image _headerImage, _typeImage, _rarityImage;
        private readonly Text _name, _attrGlyph, _type, _text, _rarity, _version, _printing, _hint;
        private readonly RawImage _art, _attrDisc;
        private readonly List<(RectTransform row, RawImage icon, Text value)> _rows = new List<(RectTransform, RawImage, Text)>();
        private readonly ScrollRect _scroll;
        private readonly RectTransform _helpChip;
        private readonly RectTransform _styleChip;
        private readonly Text _styleLabel;
        private readonly TipOnHover _styleTip;

        /// <summary>
        /// Room kept free under the effect text (the duel's Details / Pin buttons sit there), so the text never runs under
        /// anything; set it before <see cref="Layout"/>.
        /// </summary>
        public float BottomReserve { get; set; }

        /// <summary>Show the Artwork button on cards with pictures in more than one style (deck editor, card reader).</summary>
        public bool StylePicker { get; set; }
        /// <summary>The tag row under the type bar: the Spell Speed, then genre tags (Search, Negate, Banish...), then (editor) archetypes.</summary>
        private readonly RectTransform _tagRow;
        private readonly List<(RectTransform chip, float width, string label, bool archetype)> _tags = new List<(RectTransform, float, string, bool)>();

        /// <summary>
        /// The deck editor's "more cards like this": a genre chip's click searches that genre. Null (a duel's detail): the
        /// chips stay hover-only.
        /// </summary>
        public Action<CardTags.Genre>? GenreClicked { get; set; }
        /// <summary>The editor's archetype chips after the genres: the card's archetypes (code and name), and what a click does.</summary>
        public Func<CardInfo, List<(ushort code, string name)>>? ArchetypesOf { get; set; }
        public Action<ushort, string>? ArchetypeClicked { get; set; }
        private CardInfo? _shown;
        private readonly bool _reading;
        /// <summary>The reader's backing behind the right-hand column (so the board doesn't show through the text).</summary>
        private readonly RectTransform? _columnBg;

        /// <summary>The glossary terms in the shown text: where each sits in the marked-up and the plain text.</summary>
        private readonly List<(int richStart, int richLength, int plainStart, int plainLength, CardGlossary.Term term)> _terms =
            new List<(int, int, int, int, CardGlossary.Term)>();
        private string _richText = "", _plainText = "";
        private const string TermColor = "#8CC8FF";

        public CardDetail(RectTransform root, bool reading = false)
        {
            _root = root;
            _reading = reading;
            if (reading)
            {
                _columnBg = Box("Column");
                Ui.Image(_columnBg, new Color(0.07f, 0.08f, 0.12f, 0.97f)).raycastTarget = false;
                var edge = _columnBg.gameObject.AddComponent<Outline>();
                edge.effectColor = new Color(0.35f, 0.6f, 1f, 0.5f);
                edge.effectDistance = new Vector2(2, -2);
            }
            _header = Box("Header");
            _headerImage = Ui.Image(_header, Ui.Hex("#2A3140"));
            _name = Ui.Text(Ui.Stretched("Name", _header, Vector2.zero, Vector2.one), "", 20, Color.white, TextAnchor.MiddleLeft);
            _name.fontStyle = FontStyle.Bold;
            _name.rectTransform.offsetMin = new Vector2(10, 0);
            _name.rectTransform.offsetMax = new Vector2(-40, 0);
            _name.resizeTextForBestFit = true;
            _name.resizeTextMinSize = 12;
            _name.resizeTextMaxSize = 20;
            Shadow(_name);
            var disc = Ui.Rect("Attribute", _header);
            disc.anchorMin = disc.anchorMax = new Vector2(1, 0.5f);
            disc.anchoredPosition = new Vector2(-18, 0);
            disc.sizeDelta = new Vector2(30, 30);
            var rim = Ui.Placed("Rim", disc, Vector2.zero, new Vector2(34, 34)).gameObject.AddComponent<RawImage>();
            rim.texture = Icons.Round;
            rim.color = new Color(1, 1, 1, 0.85f);
            rim.raycastTarget = false;
            var fill = Ui.Placed("Fill", disc, Vector2.zero, new Vector2(29, 29));
            _attrDisc = fill.gameObject.AddComponent<RawImage>();
            _attrDisc.texture = Icons.Round;
            _attrGlyph = Ui.Text(Ui.Stretched("Glyph", disc, Vector2.zero, Vector2.one), "", 17, Color.white);
            _attrGlyph.fontStyle = FontStyle.Bold;
            Shadow(_attrGlyph);

            _artRt = Box("Art");
            _art = _artRt.gameObject.AddComponent<RawImage>();
            _art.raycastTarget = false;
            _rarityRt = Ui.Rect("Rarity", _artRt);
            _rarityRt.anchorMin = _rarityRt.anchorMax = new Vector2(1, 1);
            _rarityRt.pivot = new Vector2(1, 1);
            _rarityRt.anchoredPosition = new Vector2(4, 8);
            _rarityRt.sizeDelta = new Vector2(44, 22);
            _rarityImage = Ui.Image(_rarityRt, Ui.Hex("#E07A1F"));
            _rarity = Ui.Text(_rarityRt, "", 14, Color.white);
            _rarity.fontStyle = FontStyle.Bold;

            // Artwork: the card's next art style (right-click: the previous one).
            _styleChip = Ui.Rect("Artwork", _artRt);
            _styleChip.anchorMin = _styleChip.anchorMax = Vector2.zero;
            _styleChip.pivot = Vector2.zero;
            _styleChip.anchoredPosition = new Vector2(5, 5);
            _styleChip.sizeDelta = reading ? new Vector2(220, 40) : new Vector2(124, 24);
            Ui.Image(_styleChip, new Color(0.04f, 0.05f, 0.08f, 0.85f));
            _styleChip.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 0.79f, 0.25f, 0.7f);
            _styleLabel = Ui.Text(Ui.Stretched("Label", _styleChip, Vector2.zero, Vector2.one), "", reading ? 19 : 13, Ui.Hex("#FFC940"));
            _styleLabel.fontStyle = FontStyle.Bold;
            _styleLabel.raycastTarget = false;
            _styleLabel.resizeTextForBestFit = true;
            _styleLabel.resizeTextMinSize = 9;
            _styleLabel.resizeTextMaxSize = reading ? 19 : 13;
            Ui.Clickable(_styleChip, () => CycleStyle(+1), () => CycleStyle(-1));
            _styleTip = _styleChip.gameObject.AddComponent<TipOnHover>();
            _styleTip.Title = "Artwork";

            _stats = Box("Stats");
            for (int i = 0; i < 3; i++)
            {
                var row = Ui.Rect($"Row{i}", _stats);
                row.anchorMin = new Vector2(0, 1);
                row.anchorMax = new Vector2(1, 1);
                row.pivot = new Vector2(0, 1);
                var icon = Ui.Rect("Icon", row);
                icon.anchorMin = icon.anchorMax = new Vector2(0, 0.5f);
                icon.pivot = new Vector2(0, 0.5f);
                icon.sizeDelta = new Vector2(26, 26);
                var image = icon.gameObject.AddComponent<RawImage>();
                image.raycastTarget = false;
                var value = Ui.Text(Ui.Stretched("Value", row, Vector2.zero, Vector2.one), "", 24, Color.white, TextAnchor.MiddleLeft);
                value.rectTransform.offsetMin = new Vector2(34, 0);
                value.fontStyle = FontStyle.Bold;
                value.supportRichText = true;
                value.resizeTextForBestFit = true;
                value.resizeTextMinSize = 12;
                value.resizeTextMaxSize = 24;
                _rows.Add((row, image, value));
            }
            _version = Ui.Text(Ui.Rect("Version", _stats), "", 14, Ui.Hex("#FFB347"), TextAnchor.UpperLeft);
            _printing = Ui.Text(Ui.Rect("Printing", _stats), "", 14, new Color(0.75f, 0.78f, 0.85f), TextAnchor.UpperLeft);
            _printing.supportRichText = true;

            _typeBar = Box("TypeBar");
            _typeImage = Ui.Image(_typeBar, Ui.Hex("#2A3140"));
            _type = Ui.Text(Ui.Stretched("Type", _typeBar, Vector2.zero, Vector2.one), "", 16, Color.white, TextAnchor.MiddleLeft);
            _type.fontStyle = FontStyle.Bold;
            _type.rectTransform.offsetMin = new Vector2(8, 0);
            _type.rectTransform.offsetMax = new Vector2(-30, 0); // room for the "?" chip
            _type.resizeTextForBestFit = true;
            _type.resizeTextMinSize = 10;
            _type.resizeTextMaxSize = 16;
            Shadow(_type);

            _tagRow = Box("Tags");

            // The effect text scrolls when it is longer than its space (mouse wheel or drag).
            _textView = Box("TextView");
            Ui.Image(_textView, new Color(0, 0, 0, 0.25f));
            _textView.gameObject.AddComponent<RectMask2D>();
            _textContent = Ui.Rect("Text", _textView);
            _textContent.anchorMin = new Vector2(0, 1);
            _textContent.anchorMax = new Vector2(1, 1);
            _textContent.pivot = new Vector2(0.5f, 1);
            _textContent.offsetMin = new Vector2(8, -10);
            _textContent.offsetMax = new Vector2(-8, -6);
            _text = Ui.Text(_textContent, "", 16, Ui.Hex("#D8DEE8"), TextAnchor.UpperLeft);
            _text.supportRichText = true;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.raycastTarget = true;
            _textContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll = _textView.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = _textView;
            _scroll.content = _textContent;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30;

            // Card term help: the term under the pointer explains itself; the "?" on the type bar explains the grammar.
            _text.gameObject.AddComponent<TermHover>().TermAt = TermAt;
            _helpChip = Ui.Rect("Help", _typeBar);
            _helpChip.anchorMin = _helpChip.anchorMax = new Vector2(1, 0.5f);
            _helpChip.pivot = new Vector2(1, 0.5f);
            _helpChip.anchoredPosition = new Vector2(-4, 0);
            _helpChip.sizeDelta = new Vector2(22, 22);
            var chipDisc = _helpChip.gameObject.AddComponent<RawImage>();
            chipDisc.texture = Icons.Round;
            chipDisc.color = new Color(0, 0, 0, 0.35f);
            var chipText = Ui.Text(Ui.Stretched("Q", _helpChip, Vector2.zero, Vector2.one), "?", 15, Ui.Hex(TermColor));
            chipText.fontStyle = FontStyle.Bold;
            var tip = _helpChip.gameObject.AddComponent<TipOnHover>();
            tip.Title = "Reading card text";
            tip.Body = CardGlossary.ReadingHelp;
            Ui.Clickable(_textView, () => { if (_shown != null) Clicked?.Invoke(_shown); });
            _art.raycastTarget = true;
            Ui.Clickable(_artRt, () => { if (_shown != null) Clicked?.Invoke(_shown); });
            _hint = Ui.Text(Ui.Stretched("Hint", root, Vector2.zero, Vector2.one), "Hover a card to read it.", 16, new Color(0.85f, 0.87f, 0.9f), TextAnchor.UpperLeft);
            if (reading)
            {
                _name.fontSize = _name.resizeTextMaxSize = 30;
                _attrGlyph.fontSize = 24;
                disc.sizeDelta = new Vector2(42, 42);
                disc.anchoredPosition = new Vector2(-28, 0);
                ((RectTransform)rim.transform).sizeDelta = new Vector2(46, 46);
                fill.sizeDelta = new Vector2(40, 40);
                _name.rectTransform.offsetMin = new Vector2(18, 0);
                _name.rectTransform.offsetMax = new Vector2(-60, 0);
                _type.fontSize = _type.resizeTextMaxSize = 22;
                _type.rectTransform.offsetMin = new Vector2(14, 0);
                _version.fontSize = 16;
                _printing.fontSize = 15;
                foreach (var (_, icon, value) in _rows)
                {
                    icon.rectTransform.sizeDelta = new Vector2(36, 36);
                    value.rectTransform.offsetMin = new Vector2(46, 0);
                }
            }
            Show(null);
        }

        /// <summary>The card's picture or text was clicked (to open a bigger reading view).</summary>
        public event Action<CardInfo>? Clicked;

        /// <summary>The card shown, if any.</summary>
        public CardInfo? Shown => _shown;

        /// <summary>The card's text (for checks).</summary>
        public string Text => _shown == null ? _hint.text : _plainText;

        public void Show(CardInfo? info)
        {
            _shown = info;
            bool has = info != null;
            foreach (var part in new[] { _header, _artRt, _stats, _typeBar, _tagRow, _textView }) part.gameObject.SetActive(has);
            _columnBg?.gameObject.SetActive(has);
            _hint.gameObject.SetActive(!has);
            if (info == null) return;

            bool monster = (info.Type & CardType.Monster) != 0;
            var frame = CardView.FrameColor(info.Type);
            _headerImage.color = Color.Lerp(frame, Color.black, 0.35f);
            _typeImage.color = Color.Lerp(frame, Color.black, 0.2f);
            _name.text = info.Name;
            var (glyph, color) = monster && AttributeLooks.TryGetValue(info.Attribute, out var look) ? look
                : (info.Type & CardType.Spell) != 0 ? ("魔", "#1D9C73") : ("罠", "#B0457F");
            _attrGlyph.text = glyph;
            _attrDisc.color = Ui.Hex(color);

            var shownLook = ShowArt(info);
            var rarity = shownLook is CopyLook shownCopy && CardFoil.Mode == CardFoil.Display.On ? shownCopy.Rarity : CardFoil.RarityOf(info);
            _rarity.text = Short(rarity);
            _rarityImage.color = rarity >= Rarity.UltraRare ? Ui.Hex("#C9A227") : rarity >= Rarity.SuperRare ? Ui.Hex("#E07A1F") : rarity >= Rarity.Rare ? Ui.Hex("#5A7FB5") : Ui.Hex("#5A6070");
            _rarityRt.gameObject.SetActive(CardFoil.Printings != null);

            // Level (or Rank / Link), ATK, DEF; Spells and Traps say what kind they are.
            var lines = new List<(string icon, string text)>();
            if (monster)
            {
                uint level = info.Level & 0xff;
                if ((info.Type & CardType.Link) != 0) lines.Add(("link", $"LINK-{level}"));
                else lines.Add(((info.Type & CardType.Xyz) != 0 ? "rank" : "star", $"{level}{Scales(info)}"));
                lines.Add(("attack", Stat(info.Attack)));
                if ((info.Type & CardType.Link) == 0) lines.Add(("shield", Stat(info.Defense)));
            }
            else lines.Add(("", SpellTrapKind(info.Type)));
            for (int i = 0; i < _rows.Count; i++)
            {
                var (row, icon, value) = _rows[i];
                row.gameObject.SetActive(i < lines.Count);
                if (i >= lines.Count) continue;
                icon.gameObject.SetActive(lines[i].icon.Length > 0);
                icon.texture = lines[i].icon.Length > 0 ? Icons.For(lines[i].icon) : null;
                icon.color = lines[i].icon is "star" ? Ui.Hex("#FFC940") : lines[i].icon is "rank" ? Ui.Hex("#9AA0AA") : Color.white;
                value.rectTransform.offsetMin = new Vector2(lines[i].icon.Length > 0 ? (_reading ? 46 : 34) : 0, 0);
                value.text = lines[i].text;
            }
            var kind = CardVersions.KindOf(info);
            _version.text = kind == CardVersions.TextKind.Current ? "" : $"◆ {CardVersions.Label(kind)}";
            _printingFull = CardFoil.PrintingsText(info, 1);
            _copiesText = CopiesText(info);
            _printing.text = _copiesText.Length > 0 ? _copiesText : _printingFull;
            _type.text = TypeLine(info);
            BuildTags(info);
            SetText(info.Text);
            _text.fontSize = FxSettings.Scaled(_reading ? 22 : 16);
            Layout();
            _scroll.verticalNormalizedPosition = 1; // each card starts at the top of its text
        }

        /// <summary>The card's picture in its art style, and the Artwork button when it has more than one style.</summary>
        private CopyLook? ShowArt(CardInfo info)
        {
            var styles = CardView.Art;
            var art = _reading ? styles?.GetFull(info.Code) : styles?.Get(info.Code); // the reader is big enough for the whole picture
            _art.texture = art;
            _art.color = art != null ? Color.white : CardView.FrameColor(info.Type);
            // The copy you're pointing at (on the board: that card's own copy; in your collection: your best one).
            var (hoveredCode, hoveredLook) = CardView.Hovered;
            CopyLook? look = hoveredCode == info.Code ? hoveredLook
                : CardFoil.DuelLooks == null ? CardFoil.OwnedLook?.Invoke(info.Alias != 0 ? info.Alias : info.Code) : null;
            if (look is CopyLook copy && CardFoil.Mode == CardFoil.Display.On) CardFoil.Apply(_art, info, copy.Rarity, 1f, copy);
            else CardFoil.Apply(_art, info, CardFoil.RarityOf(info), 1f, look ?? default);
            CardFoil.Tilting(_art); // the picture alone tilts toward the pointer (its rarity tag and Artwork chip stay put)
            var options = StylePicker && styles != null ? styles.StylesFor(info.Code) : null;
            _styleChip.gameObject.SetActive(options != null && options.Count > 1);
            if (options == null || options.Count < 2) return look;
            var style = styles!.StyleOf(info.Code);
            _styleLabel.text = $"Art: {CardArt.Label(style)}  ▸";
            var names = string.Join(", ", options.Select(CardArt.Label));
            _styleTip.Body = $"Click for the next picture of this card you have ({names}); right-click for the previous one. "
                             + "It shows wherever the card is drawn. Styles are folders in mods/cards/styles/.";
            return look;
        }

        private void CycleStyle(int step)
        {
            if (_shown == null || CardView.Art == null) return;
            Sounds.Play("click");
            CardView.Art.CycleStyle(_shown.Code, step);
            ShowArt(_shown);
        }

        /// <summary>For checks: the Artwork button's label, or null when it isn't shown.</summary>
        public string? StyleButton => _shown != null && _styleChip.gameObject.activeSelf ? _styleLabel.text : null;

        /// <summary>For checks: what the Artwork button does when clicked.</summary>
        public void DebugClickStyle() => CycleStyle(+1);

        /// <summary>The effect text, with its glossary terms in blue and bold when card term help is on.</summary>
        private void SetText(string text)
        {
            _terms.Clear();
            _termCursor = -1;
            _plainText = text;
            TermTooltip.Hide();
            bool help = FxSettings.CardTermHelp;
            _helpChip.gameObject.SetActive(help);
            if (!help)
            {
                _richText = text;
                _text.text = text;
                return;
            }
            var found = CardGlossary.Find(text);
            var sb = new System.Text.StringBuilder(text.Length + found.Count * 30);
            int at = 0;
            foreach (var f in found)
            {
                sb.Append(text, at, f.Start - at);
                string open = $"<b><color={TermColor}>", close = "</color></b>";
                sb.Append(open);
                _terms.Add((sb.Length, f.Length, f.Start, f.Length, f.Term));
                sb.Append(text, f.Start, f.Length);
                sb.Append(close);
                at = f.Start + f.Length;
            }
            sb.Append(text, at, text.Length - at);
            _richText = sb.ToString();
            _text.text = _richText;
        }

        /// <summary>The glossary term under a screen point in the effect text, or null.</summary>
        private (string title, string meaning)? TermAt(Vector2 screen, Camera? cam)
        {
            int index = CharAt(screen, cam);
            if (index < 0) return null;
            // The text generator counts rich-text tags as characters in some Unity versions and not in others.
            bool richIndices = _text.cachedTextGenerator.characterCount >= _richText.Length;
            foreach (var (richStart, richLength, plainStart, plainLength, term) in _terms)
            {
                int start = richIndices ? richStart : plainStart, length = richIndices ? richLength : plainLength;
                if (index >= start && index < start + length) return (term.Title, term.Meaning);
            }
            return null;
        }

        /// <summary>The character of the effect text under a screen point (generator index), or -1.</summary>
        private int CharAt(Vector2 screen, Camera? cam)
        {
            if (_terms.Count == 0 || !_text.isActiveAndEnabled) return -1;
            var rt = _text.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, cam, out var local)) return -1;
            var generator = _text.cachedTextGenerator;
            var lines = generator.lines;
            var chars = generator.characters;
            float unit = 1f / _text.pixelsPerUnit;
            for (int l = 0; l < lines.Count; l++)
            {
                float top = lines[l].topY * unit, bottom = top - lines[l].height * unit;
                if (local.y > top || local.y <= bottom) continue;
                int end = l + 1 < lines.Count ? lines[l + 1].startCharIdx : chars.Count;
                for (int c = lines[l].startCharIdx; c < end && c < chars.Count; c++)
                {
                    float x0 = chars[c].cursorPos.x * unit, x1 = x0 + chars[c].charWidth * unit;
                    if (local.x >= x0 && local.x < x1) return c;
                }
                return -1;
            }
            return -1;
        }

        /// <summary>
        /// For checks: points at the middle of the nth glossary term's first letter and returns the tooltip's title (null
        /// when nothing showed), plus that term's title.
        /// </summary>
        public (string? shown, string? expected) DebugHoverTerm(int nth)
        {
            if (nth >= _terms.Count) return (null, null);
            var (richStart, _, plainStart, _, term) = _terms[nth];
            var generator = _text.cachedTextGenerator;
            int index = generator.characterCount >= _richText.Length ? richStart : plainStart;
            if (index >= generator.characters.Count) return (null, term.Title);
            float unit = 1f / _text.pixelsPerUnit;
            var ch = generator.characters[index];
            int line = 0;
            for (int l = 0; l < generator.lines.Count; l++) if (generator.lines[l].startCharIdx <= index) line = l;
            var info = generator.lines[line];
            var local = new Vector2(ch.cursorPos.x * unit + ch.charWidth * unit / 2, info.topY * unit - info.height * unit / 2);
            var screen = RectTransformUtility.WorldToScreenPoint(null, _text.rectTransform.TransformPoint(local));
            var hover = _text.GetComponent<TermHover>();
            hover.Check(screen, null);
            return (TermTooltip.Shown, term.Title);
        }

        private int _termCursor = -1;

        /// <summary>
        /// The next explained term in the card's text, shown as if pointed at (the controller's Card terms button: a
        /// stick can't easily land on one word). Returns its title, or null when the text has none.
        /// </summary>
        public string? ShowNextTerm()
        {
            if (_terms.Count == 0) return null;
            _termCursor = (_termCursor + 1) % _terms.Count;
            return DebugHoverTerm(_termCursor).shown;
        }

        /// <summary>For checks: how many glossary terms the shown text marks.</summary>
        public int TermCount => _terms.Count;

        /// <summary>For checks: how wide the effect text's column is.</summary>
        public float TextWidth => _textView.rect.width;

        /// <summary>Places the parts for the panel's current size (call after the panel is resized).</summary>
        public void Layout()
        {
            var size = _root.rect.size;
            if (size.x <= 0 || size.y <= 0) return;
            if (_reading) { LayoutReading(size); return; }
            const float headerH = 34, gap = 6, typeH = 28;
            // Text beside the picture only when the panel is short and wide enough for a real column of text: in the
            // play layout's narrow column it would be a sliver one word wide.
            float besideWidth = size.x - (size.y - headerH - gap) * 0.686f - 12;
            bool full = size.y >= 360 || besideWidth < 260;
            Place(_header, 0, 0, size.x, headerH);
            float artH = full ? Mathf.Min(200, size.y * 0.42f) : size.y - headerH - gap;
            float artW = artH * 0.686f;
            Place(_artRt, 0, headerH + gap, artW, artH);
            float statX = artW + 12, statW = size.x - statX;
            if (full)
            {
                // Master Duel: stats beside the picture, then the type bar and the text across the width.
                Place(_stats, statX, headerH + gap, statW, artH);
                float rowH = Mathf.Min(40, artH / 5);
                for (int i = 0; i < _rows.Count; i++) Place(_rows[i].row, 0, i * rowH, statW, rowH);
                Place((RectTransform)_version.transform, 0, 3 * rowH + 4, statW, 20);
                Place((RectTransform)_printing.transform, 0, 3 * rowH + 24, statW, artH - 3 * rowH - 24);
                Place(_typeBar, 0, headerH + gap + artH + gap, size.x, typeH);
                float tagsTop = headerH + gap + artH + gap + typeH + 4, tagsH = _tags.Count > 0 ? 24 : 0;
                Place(_tagRow, 0, tagsTop, size.x, tagsH);
                PlaceTags(size.x);
                float textTop = tagsTop + tagsH + (tagsH > 0 ? 4 : -2);
                Place(_textView, 0, textTop, size.x, WholeLines(size.y - textTop - BottomReserve));
                FitPrinting();
            }
            else
            {
                // Compact: one line of stats, the type bar and the text, all beside the picture.
                Place(_stats, statX, headerH + gap, statW, 28);
                for (int i = 0; i < _rows.Count; i++) Place(_rows[i].row, i * statW / 3, 0, statW / 3, 28);
                Place((RectTransform)_version.transform, 0, 0, 0, 0);
                Place((RectTransform)_printing.transform, 0, 0, 0, 0);
                Place(_typeBar, statX, headerH + gap + 30, statW, 24);
                float tagsH = _tags.Count > 0 ? 22 : 0;
                Place(_tagRow, statX, headerH + gap + 57, statW, tagsH);
                PlaceTags(statW);
                float textTop = headerH + gap + 56 + (tagsH > 0 ? tagsH + 4 : 0);
                Place(_textView, statX, textTop, statW, WholeLines(size.y - textTop - BottomReserve));
            }
            foreach (var (_, _, value) in _rows) value.resizeTextMaxSize = full ? 24 : 15;
        }

        /// <summary>
        /// The text box's height cut to a whole number of lines (its top padding plus N lines), so the last line showing is
        /// never sliced through its letters; the rest scrolls. Less than a line's room is left as it is.
        /// </summary>
        private float WholeLines(float height)
        {
            const float pad = 6; // the text starts this far below the box's top (see _textContent)
            float lineH = _text.fontSize * 1.15f; // the built-in font's line height
            if (_text.font != null && _text.font.fontSize > 0 && _text.font.lineHeight > 0)
                lineH = Mathf.Clamp(_text.font.lineHeight * _text.fontSize / _text.font.fontSize, _text.fontSize, _text.fontSize * 1.4f);
            lineH *= _text.lineSpacing;
            int lines = Mathf.FloorToInt((height - pad) / lineH);
            return lines >= 1 ? Mathf.Min(height, pad + lines * lineH + 2) : height;
        }

        private string _printingFull = "";
        private string _copiesText = "";

        /// <summary>Your copies of a card by base card id, best first (campaign only; null elsewhere).</summary>
        public static Func<uint, IReadOnlyList<CardCopy>>? CopiesOf { get; set; }

        /// <summary>
        /// In the campaign, a line per copy you own in place of the printings: rarity and condition, the glyph beside
        /// the word so it never reads by color alone ("◆ UR · Raw · Near Mint"), then its printing's set and
        /// year with an ORIGINAL TEXT tag where it carries one (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 5.4), or "printing
        /// not recorded" for a copy from before printings were (4.4).
        /// </summary>
        private static string CopiesText(CardInfo info)
        {
            if (CopiesOf == null) return "";
            var copies = CopiesOf(info.Alias != 0 ? info.Alias : info.Code);
            if (copies.Count == 0) return "<color=#8A8F9A>Not owned</color>";
            return string.Join("\n", copies.Take(3).Select(c =>
                $"<color={Grades.Color(c.Shown)}>{Grades.Glyph(c.Shown)}</color> {Short(c.Rarity)} · <color={Grades.Color(c.Shown)}>{ShortCondition(c)}</color>"
                + $" · {PrintingTag(c)}"
                + (c.AtDesk ? " <color=#8A93A6>(at the desk)</color>" : "")));
        }

        /// <summary>
        /// A copy's condition in a few words, its printing following on the same line: "Raw · Near Mint", "Grade 9 · Mint"
        /// (the wear's flavor, "a soft corner", is in the reading view's copy list).
        /// </summary>
        private static string ShortCondition(CardCopy c) => c.IsSlab ? $"Grade {c.Slab} · {Grades.Word(c.Slab)}" : Grades.RawLabel(Math.Min(c.Grade, 9));

        /// <summary>The text timeline the copy tags read (campaign/texts.txt; set with the editor's).</summary>
        public static CardTexts Texts { get; set; } = CardTexts.Empty;

        /// <summary>
        /// The original text a copy plays (null: current), by the same test as play (PlayTexts.CopyText): its stored text must
        /// be a collectible row of its own card, so a copy whose row later turns `never`, `cpu` or goes reads current everywhere.
        /// </summary>
        public static TextRow? OriginalRow(CardCopy c, CardTexts? texts = null) =>
            c.Text != 0 && (texts ?? Texts).Row(c.Text) is TextRow row && row.Base == c.Code && row.Collectible ? row : null;

        /// <summary>Whether a copy plays an original text (<see cref="OriginalRow"/>).</summary>
        public static bool CarriesOriginal(CardCopy c, CardTexts? texts = null) => OriginalRow(c, texts) != null;

        /// <summary>A copy's printing as a short tag: "MRD-069 2002 · ORIGINAL TEXT", "LOB-EN005 2002", or "printing not recorded".</summary>
        public static string PrintingTag(CardCopy c)
        {
            if (c.Set.Length == 0) return "<color=#8A93A6>printing not recorded</color>";
            var year = c.Printed.Length >= 4 ? " " + c.Printed.Substring(0, 4) : "";
            return $"<color=#A9B4C8>{c.Set}{year}</color>" + (CarriesOriginal(c) ? $" · <color={UiTheme.Current.GoldSoftHex}>ORIGINAL TEXT</color>" : "");
        }

        /// <summary>A listing's tag for a copy printing an original text: "ORIGINAL TEXT · 2004" (spec 4.2); "" for any other copy.</summary>
        public static string OriginalTag(CardCopy c) =>
            !CarriesOriginal(c) ? "" : c.Printed.Length >= 4 ? $"ORIGINAL TEXT · {c.Printed.Substring(0, 4)}" : "ORIGINAL TEXT";

        /// <summary>A listing detail's printing line: "MRD-069 · 2002 · ORIGINAL TEXT" ("" when the printing isn't recorded).</summary>
        public static string PrintingLine(CardCopy c) =>
            c.Set.Length == 0 ? "" : $"{c.Set}{(c.Printed.Length >= 4 ? " · " + c.Printed.Substring(0, 4) : "")}"
                                     + (CarriesOriginal(c) ? $" · <color={UiTheme.Current.GoldSoftHex}><b>ORIGINAL TEXT</b></color>" : "");

        /// <summary>
        /// The <see cref="OriginalTag"/> as a band over the lower part of a listing's card, clear of its stats line (<paramref name="x"/>, <paramref name="y"/>:
        /// its box's top left in the tile); nothing for a copy with the current text.
        /// </summary>
        public static void OriginalBand(RectTransform tile, CardCopy c, float x, float y, float w)
        {
            var tag = OriginalTag(c);
            if (tag.Length == 0) return;
            var T = UiTheme.Current;
            var band = Ui.Box(tile, x, y, w, 16);
            band.name = "Original";
            Ui.Image(band, T.GlassSoft.WithAlpha(0.9f)).raycastTarget = false;
            var text = Ui.Text(band, tag, 10, T.GoldSoft);
            text.fontStyle = FontStyle.Bold;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 7;
            text.resizeTextMaxSize = 10;
            text.raycastTarget = false;
        }

        /// <summary>For checks: the copies lines shown for the card on show (no markup); "" outside the campaign.</summary>
        public string DebugCopiesShown => System.Text.RegularExpressions.Regex.Replace(_copiesText, "<.*?>", "");

        /// <summary>
        /// The printings line in its box: when it runs long, the first set's rarities are cut at a whole name (with "…",
        /// never a dangling comma) so the "+N more printings" line under it still shows.
        /// </summary>
        private void FitPrinting()
        {
            // Your copies: up to three short lines, shrunk to fit rather than cut.
            _printing.resizeTextForBestFit = _copiesText.Length > 0;
            _printing.resizeTextMinSize = 9;
            _printing.resizeTextMaxSize = Mathf.Max(9, _printing.fontSize);
            if (_copiesText.Length > 0)
            {
                _printing.text = _copiesText;
                return;
            }
            _printing.text = _printingFull;
            float room = _printing.rectTransform.rect.height;
            if (room <= 0 || _printing.preferredHeight <= room + 0.5f) return;
            var lines = _printingFull.Split('\n');
            var rest = lines.Length > 1 ? "\n" + string.Join("\n", lines.Skip(1)) : "";
            var parts = lines[0].Split(new[] { ", " }, StringSplitOptions.None).ToList();
            while (parts.Count > 1)
            {
                parts.RemoveAt(parts.Count - 1);
                _printing.text = string.Join(", ", parts) + " …" + rest;
                if (_printing.preferredHeight <= room + 0.5f) return;
            }
            // Still too long: the set code alone, then what's left.
            var code = lines[0].Split(new[] { "  " }, StringSplitOptions.None)[0];
            _printing.text = code + " …" + rest;
        }

        /// <summary>
        /// The card reader: the picture as tall as the panel on the left; the name bar, Level / ATK / DEF (Level on its
        /// own line, ATK and DEF side by side), the type bar, tags and the text in a column on the right.
        /// </summary>
        private void LayoutReading(Vector2 size)
        {
            const float headerH = 60, gap = 14, typeH = 40;
            float artH = size.y, artW = artH * 0.686f;
            if (artW > size.x * 0.4f) { artW = size.x * 0.4f; artH = artW / 0.686f; }
            Place(_artRt, 0, (size.y - artH) / 2, artW, artH);
            float x = artW + 56, w = size.x - x - 20, y = 20;
            if (_columnBg != null) Place(_columnBg, artW + 36, 0, size.x - artW - 36, size.y);
            Place(_header, x, y, w, headerH);
            y += headerH + gap;
            const float rowH = 50;
            int shown = _rows.Count(r => r.row.gameObject.activeSelf);
            float statsH = shown > 1 ? rowH * 2 + 6 : rowH;
            Place(_stats, x, y, w, statsH);
            Place(_rows[0].row, 0, 0, w * 0.5f, rowH);
            Place(_rows[1].row, 0, rowH + 6, w * 0.25f, rowH);
            Place(_rows[2].row, w * 0.25f, rowH + 6, w * 0.25f, rowH);
            // The older-version mark and the printings beside the stats.
            Place((RectTransform)_version.transform, w * 0.52f, 4, w * 0.48f, 24);
            Place((RectTransform)_printing.transform, w * 0.52f, 30, w * 0.48f, Mathf.Max(statsH - 30, 24));
            FitPrinting();
            y += statsH + gap;
            Place(_typeBar, x, y, w, typeH);
            y += typeH + 6;
            float tagsH = _tags.Count > 0 ? 28 : 0;
            Place(_tagRow, x, y, w, tagsH);
            PlaceTags(w);
            y += tagsH + (tagsH > 0 ? 8 : 0);
            Place(_textView, x, y, w, size.y - y - 20);
            foreach (var (_, _, value) in _rows) value.resizeTextMaxSize = 32;
        }

        /// <summary>
        /// The tag chips for a card: its Spell Speed (gold), then what it does, from the card database (Link Evolution's
        /// "Card Genre"). Each explains itself on hover.
        /// </summary>
        private void BuildTags(CardInfo info)
        {
            foreach (var (chip, _, _, _) in _tags) UnityEngine.Object.Destroy(chip.gameObject);
            _tags.Clear();
            if (CardTags.SpellSpeed(info) is { } speed)
                AddTag($"Spell Speed {speed.speed}", $"Spell Speed {speed.speed}", speed.why + "\n\n" + SpeedRule, true);
            foreach (var tag in CardTags.GenresOf(info))
            {
                // In the editor a genre is a search: click it for more cards that do this.
                var genre = GenreClicked != null ? CardTags.Genres.FirstOrDefault(g => g.Label == tag.Label) : null;
                var search = GenreClicked;
                AddTag(tag.Label, tag.Label, genre != null ? tag.Meaning + "\n\nClick: more cards that do this." : tag.Meaning, false,
                    genre != null && search != null ? () => search(genre) : (Action?)null);
            }
            // The editor's archetype chips (a card can be in several): click one to search its members.
            if (ArchetypesOf != null && ArchetypeClicked is { } pick)
                foreach (var (code, name) in ArchetypesOf(info))
                    AddTag(name, name, "Archetype.\n\nClick: its members.", false, () => pick(code, name), archetype: true);
        }

        private const string SpeedRule = "A card can only respond with Spell Speed 2 or higher, and never lower than the card it answers: " +
            "Spell Speed 1 starts chains, 2 answers 1 or 2, 3 (Counter Traps) answers anything.";

        private void AddTag(string label, string title, string meaning, bool speed, Action? click = null, bool archetype = false)
        {
            var chip = Ui.Rect("Tag", _tagRow);
            // An archetype chip is blue, so it reads apart from what the card does.
            var bg = Ui.Image(chip, speed ? Ui.Hex("#5A4512") : archetype ? new Color(0.24f, 0.44f, 0.71f, 0.45f) : new Color(1, 1, 1, 0.1f));
            var edge = chip.gameObject.AddComponent<Outline>();
            edge.effectColor = speed ? Ui.Hex("#FFC940") : archetype ? new Color(0.55f, 0.75f, 1f, 0.6f) : new Color(1, 1, 1, 0.25f);
            edge.effectDistance = new Vector2(1, -1);
            var text = Ui.Text(Ui.Stretched("Label", chip, Vector2.zero, Vector2.one), label, FxSettings.Scaled(13), speed ? Ui.Hex("#FFD86A") : Ui.Hex("#D8DEE8"));
            text.fontStyle = speed ? FontStyle.Bold : FontStyle.Normal;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            bg.raycastTarget = true;
            if (click != null) Ui.Clickable(chip, click, style: HoverStyle.Button);
            var tip = chip.gameObject.AddComponent<TipOnHover>();
            tip.Title = title;
            tip.Body = meaning;
            _tags.Add((chip, text.preferredWidth + 16, label, archetype));
        }

        /// <summary>
        /// Lays the chips out left to right; the ones that don't fit are left out (the most telling come first). Archetype
        /// chips go last but keep their room (up to 60% of the row), so a card with many genres still shows what it belongs to.
        /// </summary>
        private void PlaceTags(float width)
        {
            float x = 0, h = _tagRow.sizeDelta.y, keep = 0;
            foreach (var (_, w, _, archetype) in _tags)
                if (archetype && keep + w <= width * 0.6f) keep += w + 6;
            foreach (bool archetypes in new[] { false, true })
                foreach (var (chip, w, _, archetype) in _tags)
                {
                    if (archetype != archetypes) continue;
                    bool fits = x + w <= (archetypes ? width : width - keep);
                    chip.gameObject.SetActive(fits && h > 0);
                    if (!fits) continue;
                    Place(chip, x, 0, w, h);
                    x += w + 6;
                }
        }

        /// <summary>For checks: the tags showing (the Spell Speed first, archetypes last).</summary>
        public List<string> ShownTags => _tags.Where(t => t.chip.gameObject.activeSelf).Select(t => t.label).ToList();

        /// <summary>For checks: clicks the shown tag with this label as a player would; false when it isn't there or isn't clickable.</summary>
        public bool DebugClickTag(string label)
        {
            var chip = _tags.FirstOrDefault(t => t.label == label && t.chip.gameObject.activeSelf).chip;
            if (chip == null || chip.GetComponent<Clickable>() is not Clickable c || c.Left == null) return false;
            c.Left();
            return true;
        }

        private RectTransform Box(string name)
        {
            var rt = Ui.Rect(name, _root);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            return rt;
        }

        /// <summary>Puts a top-left-anchored box at (x, y) from its parent's top-left corner.</summary>
        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Shadow(Text text)
        {
            var o = text.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0, 0, 0, 0.8f);
            o.effectDistance = new Vector2(1, -1);
        }

        /// <summary>A printed ATK or DEF as cards show it: "?" for a "?" stat (the data stores it as -2), else the number.</summary>
        public static string Stat(int value) => value < 0 ? "?" : value.ToString();

        private static string Scales(CardInfo info) =>
            (info.Type & CardType.Pendulum) != 0 ? $"   <size=16><color=#9FE6D8>Scale {info.LScale}</color></size>" : "";

        /// <summary>Master Duel's rarity badge: N, R, SR, UR, then short forms of the rest.</summary>
        private static string Short(Rarity r) => r switch
        {
            Rarity.Common or Rarity.ShortPrint => "N",
            Rarity.Rare => "R",
            Rarity.SuperRare => "SR",
            Rarity.UltraRare => "UR",
            Rarity.SecretRare => "ScR",
            _ => string.Concat(Rarities.Name(r).Split(' ').Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0]))),
        };

        private static string SpellTrapKind(uint type)
        {
            bool spell = (type & CardType.Spell) != 0;
            string kind = (type & 0x10000) != 0 ? "Quick-Play" : (type & 0x20000) != 0 ? "Continuous" : (type & 0x40000) != 0 ? "Equip"
                : (type & 0x80000) != 0 ? "Field" : (type & 0x80) != 0 ? "Ritual" : (type & 0x100000) != 0 ? "Counter" : "Normal";
            return $"{kind} {(spell ? "Spell" : "Trap")}";
        }

        /// <summary>"[Fiend/Tuner/Effect]", "[Spell]".</summary>
        private static string TypeLine(CardInfo info)
        {
            if ((info.Type & CardType.Monster) == 0) return (info.Type & CardType.Spell) != 0 ? "[Spell]" : "[Trap]";
            var parts = new List<string>();
            var race = DuelText.Races.FirstOrDefault(r => (info.Race & r.bit) != 0).name;
            if (!string.IsNullOrEmpty(race)) parts.Add(race);
            foreach (var (bit, name) in new (uint, string)[]
                     {
                         (0x40, "Fusion"), (0x80, "Ritual"), (0x2000, "Synchro"), (0x800000, "Xyz"), (0x1000000, "Pendulum"), (0x4000000, "Link"),
                         (0x200000, "Flip"), (0x400000, "Toon"), (0x200, "Spirit"), (0x400, "Union"), (0x800, "Gemini"), (0x1000, "Tuner"),
                     })
                if ((info.Type & bit) != 0) parts.Add(name);
            parts.Add((info.Type & 0x20) != 0 ? "Effect" : (info.Type & 0x10) != 0 ? "Normal" : "");
            return $"[{string.Join("/", parts.Where(p => p.Length > 0))}]";
        }
    }
}
