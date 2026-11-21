using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Data;

namespace YgoCampaign.Game
{
    /// <summary>
    /// A card drawn with the player's picture of it or, without one, its plain card (<see cref="CardFace"/>: the game's own
    /// drawn face), either with its rarity's foil. The game ships no card art: official images are Konami's.
    /// </summary>
    public sealed class CardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public static readonly Vector2 FieldSize = new Vector2(86, 120);

        /// <summary>The fill of the drawn card back (see <see cref="DrawBack"/>): also the color of a card you can't see.</summary>
        public static readonly Color BackFill = Ui.Hex("#13203A");
        private static readonly Color BackTrim = Ui.Hex("#C8901E");
        private static readonly Color[] OwnerBorder = { Ui.Hex("#4FA3FF"), Ui.Hex("#FF5A5A") };
        /// <summary>Border for cards you can act on right now.</summary>
        public static readonly Color Actionable = Ui.Hex("#FFC940");

        /// <summary>Player-supplied card pictures; null or missing pictures fall back to text cards.</summary>
        public static CardArt? Art { get; set; }

        private CardInfo? _info;
        private CopyLook? _look;
        /// <summary>The border round the card: the owner's colour, or the playable (highlight) colour.</summary>
        private Image _border = null!;
        private bool _highlighted;
        private Action<CardInfo>? _onHover;
        private Action? _onClick;

        /// <summary>Whether this view shows a card you may read (not a back or a hidden card).</summary>
        public bool ShowsFace => _info != null;

        /// <summary>The face drawn (picture or plain card), and whether it's a plain one.</summary>
        private RawImage? _face;
        private bool _plainFace;

        /// <summary>For checks (CardFace.DebugAudit): this view draws a card's face (not a back, not a peek).</summary>
        public bool DebugFaceDrawn => _face != null;
        private Action? _onRightClick;

        /// <param name="reveal">Show the face even if the card is face-down (your own set cards, or spectating).</param>
        /// <param name="offField">In hand / pile: never sideways, no SET tag.</param>
        /// <param name="hand">A card in the duel's fanned hand (see <see cref="HandStats"/>).</param>
        /// <param name="fit">What a plain face leaves room for (the deck editor's limit badge and in-deck count).</param>
        public static CardView Create(Transform parent, Vector2 center, Vector2 size, FieldCard card, CardInfo? info,
            int owner, bool reveal, Action<CardInfo>? onHover, bool offField = false, Action? onClick = null, bool highlight = false,
            Action? onRightClick = null, bool flip = false, bool hand = false, CardFit fit = CardFit.Normal)
        {
            var view = Build(parent, center, size, card, info, owner, reveal, onHover, offField, onClick, highlight, onRightClick, fit);
            if (hand) HandStats(view, card, info, flip);
            if (!offField) StatusBadges(view, card, size, reveal || card.IsFaceUp);
            if (highlight && FxSettings.PlayableTabs) ActionTab(view.transform, size);
            if (flip)
            {
                // The opponent's cards face them: turned around, with their writing turned back so it reads upright.
                var root = (RectTransform)view.transform;
                root.localRotation *= Quaternion.Euler(0, 0, 180);
                foreach (var text in view.GetComponentsInChildren<Text>()) text.rectTransform.localRotation = Quaternion.Euler(0, 0, 180);
                if (view._plainFace) TurnBack(view);
            }
            return view;
        }

        /// <summary>
        /// A turned plain card is turned back so its writing reads upright, as the text look's did (owner decision N2): the
        /// face and its rarity mark, and the live stat strip moved to the face's far end (the bottom as you see it), so it
        /// covers the plain card's footer as it covers a picture's. The border keeps the owner's colour.
        /// </summary>
        private static void TurnBack(CardView view)
        {
            var art = view._face!.rectTransform;
            art.localRotation = Quaternion.Euler(0, 0, 180);
            foreach (var text in art.GetComponentsInChildren<Text>(true)) text.rectTransform.localRotation = Quaternion.identity;
            if (view.transform.Find("Face/Stats") is RectTransform strip)
            {
                strip.anchorMin = new Vector2(0, 0.82f);
                strip.anchorMax = Vector2.one;
                strip.localRotation = Quaternion.Euler(0, 0, 180);
                foreach (var text in strip.GetComponentsInChildren<Text>(true)) text.rectTransform.localRotation = Quaternion.identity;
            }
        }

        private static CardView Build(Transform parent, Vector2 center, Vector2 size, FieldCard card, CardInfo? info,
            int owner, bool reveal, Action<CardInfo>? onHover, bool offField, Action? onClick, bool highlight, Action? onRightClick, CardFit fit)
        {
            var root = Ui.Placed("Card", parent, center, size);
            var view = root.gameObject.AddComponent<CardView>();
            view._info = info;
            view._onHover = onHover;
            view._onClick = onClick;
            view._onRightClick = onRightClick;

            // Defense position monsters on the field lie sideways, like on a real mat. The core also
            // reports cards in hand / GY with a defense position, so those stay upright.
            if (!offField && card.IsDefense && (card.Type & CardType.Monster) != 0)
                root.localRotation = Quaternion.Euler(0, 0, 90);

            view._border = Ui.Image(root, highlight ? Actionable : OwnerBorder[owner]);
            view._border.raycastTarget = true;
            view._highlighted = highlight;
            var face = Ui.Stretched("Face", root, Vector2.zero, Vector2.one);
            var border = highlight ? 5 : 3;
            face.offsetMin = new Vector2(border, border);
            face.offsetMax = new Vector2(-border, -border);

            // On the field a face-down card always shows its back. If you may see it (your own set card, or spectating),
            // hovering it still reads it, and a small tag names it.
            if (!card.IsFaceUp && !offField && (card.Position & 0x0f) != 0)
            {
                DrawBack(face);
                if (!reveal || info == null) { view._info = null; return view; }
                // A Trap set this turn wears its "set this turn" tag along the bottom: the name goes just above it.
                bool setTag = card.SetThisTurn && (card.Type & CardType.Trap) != 0;
                var peek = Ui.Stretched("Peek", face, new Vector2(0, setTag ? 0.2f : 0), new Vector2(1, setTag ? 0.44f : 0.24f));
                Ui.Image(peek, new Color(0, 0, 0, 0.7f)).raycastTarget = false;
                var peekText = Ui.Text(peek, info.Name, 9, new Color(1f, 0.9f, 0.6f));
                peekText.resizeTextForBestFit = true;
                peekText.resizeTextMinSize = 7;
                peekText.resizeTextMaxSize = 10;
                return view;
            }

            bool showFace = card.IsFaceUp || reveal;
            if (!showFace)
            {
                DrawBack(face);
                view._info = null; // hovering a hidden card must not reveal it
                return view;
            }
            // Every shown face: the player's picture, else the plain card (a card with no data too: "Card <code>"). Under
            // either the face is see-through, so the picture's rounded corners (CardFoil) show round.
            var texture = CardFace.Picture(card.Code, info, size, fit);
            Ui.Image(face, texture != null ? Color.clear : Ui.Hex("#6B3F1F")); // only code 0 has no face at all
            if (texture == null) return view;
            if (info != null) view._look = LookOf(card, info, offField);
            view._face = DrawArt(face, texture, card, info, offField, size, view._look);
            view._plainFace = CardFace.IsPlain(texture);
            CardFace.Tag(view._face, card.Code, size, fit, info);
            return view;
        }

        // ---- Fitting words ------------------------------------------------------------------------------

        /// <summary>
        /// The smallest size <see cref="FitWords"/> goes to before it breaks a word: smaller reads worse than a hyphen at
        /// 1280x720 (a deck editor card's name at 8 is about 5 px).
        /// </summary>
        public const int MinWordSize = 10;

        /// <summary>A string's width on one line in <paramref name="t"/>'s font and style at <paramref name="size"/> (layout units).</summary>
        public static float TextWidth(Text t, string s, int size)
        {
            var settings = Settings(t, size, 0);
            return t.cachedTextGeneratorForLayout.GetPreferredWidth(s, settings);
        }

        /// <summary>A string's height wrapped at <paramref name="width"/> in <paramref name="t"/>'s font and style at <paramref name="size"/>.</summary>
        public static float TextHeight(Text t, string s, int size, float width)
        {
            var settings = Settings(t, size, width);
            return t.cachedTextGeneratorForLayout.GetPreferredHeight(s, settings);
        }

        private static TextGenerationSettings Settings(Text t, int size, float width)
        {
            var settings = t.GetGenerationSettings(new Vector2(width > 0 ? width : 100000, 0));
            settings.fontSize = size;
            settings.scaleFactor = 1; // layout units in, layout units out
            settings.resizeTextForBestFit = false;
            settings.horizontalOverflow = width > 0 ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            settings.richText = false;
            return settings;
        }

        /// <summary>
        /// Keeps a name's words whole on its lines (the text's own wrap breaks a word too long for the line anywhere,
        /// "Polymerizatio n"): the largest size from <paramref name="max"/> down to <see cref="MinWordSize"/> where every
        /// word fits <paramref name="width"/>; when even that's too wide, a word breaks after its own hyphen ("Mekk-" /
        /// "Knight"), then with a hyphen added. The text then shrinks to fit its box's height (best fit), never wider.
        /// </summary>
        public static void FitWords(Text t, float width, int max)
        {
            if (width <= 4 || string.IsNullOrEmpty(t.text)) return;
            var text = t.text;
            var words = text.Split(' ');
            int size = max;
            while (size > MinWordSize && !AllFit(t, words, size, width)) size--;
            if (!AllFit(t, words, size, width)) text = BreakWords(t, words, size, width);
            t.text = text;
            t.fontSize = size;
            t.resizeTextForBestFit = true;
            t.resizeTextMaxSize = size;
            t.resizeTextMinSize = Mathf.Min(size, 6);
        }

        /// <summary>
        /// For checks: where a drawn text's lines break inside a word (a line starting with a letter right after one, with
        /// no space, hyphen or line break between). Read a frame after it's drawn.
        /// </summary>
        public static System.Collections.Generic.List<string> BrokenWords(Text t)
        {
            var problems = new System.Collections.Generic.List<string>();
            var text = t.text ?? "";
            var gen = t.cachedTextGenerator;
            var lines = gen.lines;
            for (int i = 1; i < lines.Count; i++)
            {
                int at = lines[i].startCharIdx;
                if (at <= 0 || at >= text.Length) continue;
                if (char.IsLetterOrDigit(text[at - 1]) && char.IsLetterOrDigit(text[at]))
                    problems.Add($"\"{text.Replace("\n", "/")}\" breaks \"{text.Substring(Math.Max(0, at - 6), Math.Min(text.Length, at + 6) - Math.Max(0, at - 6))}\" mid-word");
            }
            return problems;
        }

        private static bool AllFit(Text t, string[] words, int size, float width)
        {
            foreach (var w in words)
                if (w.Length > 0 && TextWidth(t, w, size) > width) return false;
            return true;
        }

        /// <summary>
        /// Where to hyphenate <paramref name="word"/> at or before <paramref name="most"/> letters (three or more on each
        /// side). First inside a run of consonants between two vowels, where the next syllable takes the longest start a word
        /// can have (<see cref="Onsets"/>): "Des-truction", "Con-struction", "Thunder-strike", "Syn-chron", "After-glow",
        /// "Air-knight", "Knight-mare", "Black-wing", "Arch-fiend"; never inside a <see cref="Digraph"/> ("Knig-htmare") or
        /// before an r or l that belongs with the consonant before it ("Dest-ruction"). The latest such place that fits wins.
        /// Else after a vowel before a single consonant ("Polymeri-zation"); else at <paramref name="most"/>, moved off a
        /// digraph when it can be.
        /// </summary>
        private static int SyllableCut(string word, int most)
        {
            static bool Vowel(char c) => "aeiouyAEIOUY".IndexOf(c) >= 0;
            static bool Consonant(char c) => char.IsLetter(c) && !Vowel(c);
            int hi = Math.Min(most, word.Length - 3), best = -1;
            for (int s = 1; s < word.Length; s++)
            {
                if (!(Vowel(word[s - 1]) && Consonant(word[s]))) continue;
                int e = s;
                while (e + 1 < word.Length && Consonant(word[e + 1])) e++;
                if (e == s || e + 1 >= word.Length || !Vowel(word[e + 1])) continue;
                // The run word[s..e]: the first cut from its left whose right part can start a syllable (the longest start).
                for (int c = s; c <= e; c++)
                {
                    if (c < 3 || Digraph(word[c - 1], word[c]) || !Onset(word.Substring(c, e + 1 - c))) continue;
                    if (c <= hi) best = Math.Max(best, c);
                    break;
                }
            }
            if (best > 0) return best;
            for (int c = hi; c >= 3; c--)
                if (Vowel(word[c - 1]) && Consonant(word[c]) && c + 1 < word.Length && Vowel(word[c + 1])) return c;
            if (most > 3 && most < word.Length && Digraph(word[most - 1], word[most])) return most - 1;
            return most;
        }

        /// <summary>
        /// Letters that can start a syllable together (one consonant always can). Not "sm", "sn", "gn": "Trans-migration" and
        /// "Electromag-netic", not "Tran-smigration" or "Electroma-gnetic".
        /// </summary>
        private static readonly string[] Onsets =
        {
            "bl", "br", "ch", "cl", "cr", "dr", "fl", "fr", "gl", "gr", "kn", "ph", "pl", "pr", "qu", "sc", "sh", "sk", "sl", "sp", "st", "sw",
            "th", "tr", "tw", "wh", "wr", "chr", "phr", "shr", "thr", "scr", "spl", "spr", "str", "squ", "sch",
        };

        private static bool Onset(string s) =>
            s.Length == 1 ? char.IsLetter(s[0]) && "aeiouyAEIOUY".IndexOf(s[0]) < 0 : Array.IndexOf(Onsets, s.ToLowerInvariant()) >= 0;

        /// <summary>For checks: <paramref name="word"/> hyphenated where a card <paramref name="most"/> letters wide would break it ("Des-truction").</summary>
        public static string DebugHyphenate(string word, int most)
        {
            int c = SyllableCut(word, most);
            return word.Substring(0, c) + "-" + word.Substring(c);
        }

        /// <summary>Two letters read as one sound ("gh", "th", "ck", "qu"...): a hyphen never goes between them.</summary>
        public static bool Digraph(char a, char b) =>
            (char.ToLowerInvariant(a), char.ToLowerInvariant(b)) is ('c', 'h') or ('g', 'h') or ('p', 'h') or ('s', 'h') or ('t', 'h')
                or ('w', 'h') or ('c', 'k') or ('q', 'u');

        /// <summary>
        /// For checks: hyphens this name was given that fall inside a digraph ("Knig-" / "htmare"). The word's own hyphens
        /// ("Mekk-" / "Knight") don't count: they're between a letter and a capital.
        /// </summary>
        public static System.Collections.Generic.List<string> SplitDigraphs(Text t)
        {
            var problems = new System.Collections.Generic.List<string>();
            var text = t.text ?? "";
            for (int i = text.IndexOf("-\n", StringComparison.Ordinal); i > 0 && i + 2 < text.Length; i = text.IndexOf("-\n", i + 1, StringComparison.Ordinal))
                if (char.IsLetter(text[i - 1]) && char.IsLower(text[i + 2]) && Digraph(text[i - 1], text[i + 2]))
                    problems.Add($"\"{text.Replace("\n", "/")}\" hyphenates inside \"{text[i - 1]}{text[i + 2]}\"");
            return problems;
        }

        /// <summary>The words with each one too wide broken onto lines: after its own hyphens first, then with a hyphen added.</summary>
        private static string BreakWords(Text t, string[] words, int size, float width)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var word in words)
            {
                if (word.Length == 0 || TextWidth(t, word, size) <= width) { parts.Add(word); continue; }
                // Its own hyphens: as many pieces on a line as fit ("Mekk-Knight" -> "Mekk-" / "Knight").
                var pieces = new System.Collections.Generic.List<string>();
                int start = 0;
                for (int i = 0; i < word.Length; i++)
                    if (word[i] == '-' && i > 0 && i < word.Length - 1) { pieces.Add(word.Substring(start, i + 1 - start)); start = i + 1; }
                pieces.Add(word.Substring(start));
                var lines = new System.Collections.Generic.List<string>();
                var line = "";
                foreach (var piece in pieces)
                {
                    if (line.Length > 0 && TextWidth(t, line + piece, size) <= width) { line += piece; continue; }
                    if (line.Length > 0) lines.Add(line);
                    line = piece;
                    // A piece still too wide: as many letters as fit with a hyphen, at least three on either side, cut where
                    // a syllable likely ends ("Bottom-less", "Polymeri-zation") when there's such a place.
                    while (line.Length > 5 && TextWidth(t, line, size) > width)
                    {
                        int cut = line.Length - 3;
                        while (cut > 3 && TextWidth(t, line.Substring(0, cut) + "-", size) > width) cut--;
                        cut = SyllableCut(line, cut);
                        lines.Add(line.Substring(0, cut) + "-");
                        line = line.Substring(cut);
                    }
                }
                lines.Add(line);
                parts.Add(string.Join("\n", lines));
            }
            return string.Join(" ", parts);
        }

        /// <summary>
        /// A hand card's stat line: the next card in the fan covers this one's far side (its right, or its left as the
        /// opponent sees it turned around), so ATK/DEF sit on the side still in view, without the Level (the hand shows
        /// it big on the card's edge).
        /// </summary>
        private static void HandStats(CardView view, FieldCard card, CardInfo? info, bool flip)
        {
            if (info == null || (info.Type & CardType.Monster) == 0) return;
            var face = view.transform.Find("Face");
            if (face == null || face.Find("Stats") is not Transform strip) return;
            var text = strip.GetComponentInChildren<Text>();
            if (text == null) return;
            text.text = StatLine(card, info, withLevel: false);
            bool lower = text.alignment == TextAnchor.LowerCenter; // the text-only card's stats sit at its bottom
            text.alignment = flip ? (lower ? TextAnchor.LowerRight : TextAnchor.MiddleRight) : (lower ? TextAnchor.LowerLeft : TextAnchor.MiddleLeft);
            text.rectTransform.offsetMin = new Vector2(5, text.rectTransform.offsetMin.y);
            text.rectTransform.offsetMax = new Vector2(-5, text.rectTransform.offsetMax.y);
        }

        /// <summary>
        /// What the engine says about a card on the field, drawn on it: NEGATED (its effects are negated right now) with a
        /// dimmed face, "set this turn" on a Trap that can't be activated yet, and its counters. Each is a word or a
        /// symbol as well as a color.
        /// </summary>
        private static void StatusBadges(CardView view, FieldCard card, Vector2 size, bool known)
        {
            var root = (RectTransform)view.transform;
            if (card.IsNegated)
            {
                var shade = Ui.Stretched("Negated", root, Vector2.zero, Vector2.one);
                Ui.Image(shade, new Color(0.15f, 0.05f, 0.25f, 0.45f)).raycastTarget = false;
                var strip = Ui.Stretched("NegatedTag", root, new Vector2(0, 0.6f), new Vector2(1, 0.78f));
                Ui.Image(strip, new Color(0.35f, 0.08f, 0.45f, 0.92f)).raycastTarget = false;
                var label = Ui.Text(strip, "⊘ NEGATED", 11, Color.white);
                label.fontStyle = FontStyle.Bold;
                label.raycastTarget = false;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 7;
                label.resizeTextMaxSize = 11;
            }
            if (card.SetThisTurn && known && (card.Type & CardType.Trap) != 0 && !card.IsFaceUp)
            {
                // Opaque, so nothing under it shows through.
                var tag = Ui.Stretched("SetThisTurn", root, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.2f));
                Ui.Image(tag, new Color(0.04f, 0.04f, 0.06f, 1f)).raycastTarget = false;
                var t = Ui.Text(tag, "set this turn", 10, new Color(1f, 0.85f, 0.5f));
                t.raycastTarget = false;
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = 7;
                t.resizeTextMaxSize = 10;
            }
            if (card.Counters > 0)
            {
                var badge = Ui.Rect("Counters", root);
                badge.anchorMin = badge.anchorMax = new Vector2(1, 0);
                badge.pivot = new Vector2(1, 0);
                badge.anchoredPosition = new Vector2(-2, 22);
                badge.sizeDelta = new Vector2(30, 22);
                Ui.Image(badge, new Color(0.1f, 0.3f, 0.5f, 0.95f)).raycastTarget = false;
                var t = Ui.Text(badge, $"⬢{card.Counters}", 13, Color.white);
                t.fontStyle = FontStyle.Bold;
                t.raycastTarget = false;
            }
        }

        /// <summary>The card's picture over its whole face, plus a strip with live stats for monsters.</summary>
        /// <summary>
        /// Which of your copies a card is: in a duel, the copy its deck slot stamped on it (a CPU's card or a borrowed
        /// one has none); in your collection and decks, your best copy.
        /// </summary>
        private static CopyLook? LookOf(FieldCard card, CardInfo info, bool offField)
        {
            if (card.Cover != 0) return CardFoil.DuelLooks?.Of(card);
            return offField ? CardFoil.OwnedLook?.Invoke(info.Alias != 0 ? info.Alias : info.Code) : null;
        }

        /// <summary>
        /// A slabbed grid tile (see <see cref="SlabView.RimGlass"/>): the owner's bright border becomes the case's dark well,
        /// so the rim's gilt line reads round the card. A highlight (playable, in the deck) keeps its colour: it means something.
        /// </summary>
        public void QuietBorder(Color well)
        {
            if (!_highlighted && _border != null) _border.color = well;
        }

        /// <summary>The copy under the pointer (its card id and look), for the detail panel to draw the same copy.</summary>
        public static (uint code, CopyLook? look) Hovered { get; private set; }

        /// <summary>The card's face (its picture or plain card) over its whole face, plus a strip with live stats for monsters.</summary>
        private static RawImage DrawArt(RectTransform face, Texture art, FieldCard card, CardInfo? info, bool offField, Vector2 size, CopyLook? look)
        {
            var picture = Ui.Stretched("Art", face, Vector2.zero, Vector2.one);
            var raw = picture.gameObject.AddComponent<RawImage>();
            raw.texture = art;
            raw.raycastTarget = false;
            if (info == null)
            {
                // A card the game has no data for: its plain card, square (no foil without data).
                SetTag(face, card, offField);
                return raw;
            }
            // Your copy: its own rarity and wear. Anything else: the card's look as before (your best rarity or its printings).
            if (look is CopyLook copy && CardFoil.Mode == CardFoil.Display.On)
                CardFoil.Apply(raw, info, copy.Rarity, offField ? 0.75f : 0.6f, copy, card.Cover);
            else CardFoil.Apply(raw, info, info == null ? Rarity.Common : CardFoil.RarityOf(info), offField ? 0.75f : 0.6f, look ?? default);

            // Printed ATK/DEF can't show boosts or reductions, so monsters get the live values on top.
            if ((info.Type & CardType.Monster) != 0)
            {
                var strip = Ui.Stretched("Stats", face, new Vector2(0, 0), new Vector2(1, 0.18f));
                // Over a plain card's footer (its printed stats, inside this strip's 0.18 of the card) the strip is opaque, so
                // nothing printed shows through under the live numbers. Pictures keep today's see-through strip.
                var stripBack = Ui.Image(strip, new Color(0, 0, 0, CardFace.IsPlain(art) ? 1f : 0.72f));
                stripBack.raycastTarget = false;
                // Its bottom corners round with the card's own (0.028 card heights; see CardFoil), so they don't poke out.
                float height = face.rect.height > 0 ? face.rect.height : size.y;
                stripBack.sprite = SlabTextures.RoundBottom(0.028f * height);
                stripBack.type = Image.Type.Sliced;
                var stripText = Ui.Text(strip, StatLine(card, info, size.x >= MinWidthForLevel), 11, Color.white);
                stripText.fontStyle = FontStyle.Bold;
                stripText.supportRichText = true;
                // Shrinks to fit rather than cutting off "2500/2000" (or a ▲ changed stat) on narrow hand cards; on the
                // narrowest cards (the deck editor's list) the Level gives way so ATK/DEF always fit at a readable size.
                stripText.resizeTextForBestFit = true;
                stripText.resizeTextMinSize = 9;
                stripText.resizeTextMaxSize = 11;
            }
            SetTag(face, card, offField);
            return raw;
        }

        /// <summary>A face-down card revealed to the viewer (spectating) still says it's set.</summary>
        private static void SetTag(RectTransform face, FieldCard card, bool offField)
        {
            if (card.IsFaceUp || offField) return;
            var tag = Ui.Stretched("SetTag", face, new Vector2(0, 0.42f), new Vector2(1, 0.58f));
            Ui.Image(tag, new Color(0, 0, 0, 0.6f)).raycastTarget = false;
            Ui.Text(tag, "SET", 11, Color.white);
        }

        /// <summary>
        /// The back of a card over a rect: the player's card back picture if they have one, else one drawn here: deep navy
        /// with a gold border set in from the edge and a gold star in the middle. Returns the rect's own graphic.
        /// </summary>
        public static Graphic DrawBack(RectTransform rt, bool star = true)
        {
            var picture = Art?.Back;
            if (picture != null)
            {
                var raw = rt.gameObject.AddComponent<RawImage>();
                raw.texture = picture;
                return raw;
            }
            var fill = Ui.Image(rt, BackFill);
            // The border: four thin strips, set in a little from the edge (thicker on big cards, like a pack's).
            float width = rt.rect.width > 0 ? rt.rect.width : FieldSize.x;
            float inset = Mathf.Max(4, width * 0.045f), line = Mathf.Max(2, width * 0.03f);
            void Edge(string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
            {
                var edge = Ui.Stretched(name, rt, min, max);
                edge.offsetMin = offsetMin;
                edge.offsetMax = offsetMax;
                Ui.Image(edge, BackTrim).raycastTarget = false;
            }
            Edge("Top", new Vector2(0, 1), Vector2.one, new Vector2(inset, -inset - line), new Vector2(-inset, -inset));
            Edge("Bottom", Vector2.zero, new Vector2(1, 0), new Vector2(inset, inset), new Vector2(-inset, inset + line));
            Edge("Left", Vector2.zero, new Vector2(0, 1), new Vector2(inset, inset), new Vector2(inset + line, -inset));
            Edge("Right", new Vector2(1, 0), Vector2.one, new Vector2(-inset - line, inset), new Vector2(-inset, -inset));
            // The star: 40% of the card's width, square on a card-shaped back (about 0.72 wide to tall). Piles leave it
            // out: their count sits in the middle.
            if (!star) return fill;
            var mark = Ui.Stretched("Star", rt, new Vector2(0.3f, 0.356f), new Vector2(0.7f, 0.644f)).gameObject.AddComponent<RawImage>();
            mark.texture = Sparkle;
            mark.color = BackTrim;
            mark.raycastTarget = false;
            return fill;
        }

        private static Texture2D? _sparkle;

        /// <summary>
        /// A sharp four-point sparkle (like the mat's stars) for the card back: concave sides meeting in fine points, so it
        /// stays crisp at hand size (a five-point star blurs into a pinwheel there).
        /// </summary>
        internal static Texture2D Sparkle
        {
            get
            {
                if (_sparkle != null) return _sparkle;
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2 - 1, v = (y + 0.5f) / n * 2 - 1;
                    // |u|^p + |v|^p <= 1 with p < 1 bows the sides in; the edge is softened over a pixel or so.
                    float d = Mathf.Pow(Mathf.Abs(u), 0.62f) + Mathf.Pow(Mathf.Abs(v), 0.62f);
                    px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01((1 - d) * 18));
                }
                tex.SetPixels(px);
                tex.Apply();
                return _sparkle = tex;
            }
        }

        /// <summary>A face-down stack (deck / extra deck) or the top card of a pile, with a count badge.</summary>
        /// <summary>
        /// Cards you can act on get a gold tab with ▶ above their top edge, a second signal beside the gold border
        /// (not only color).
        /// </summary>
        private static void ActionTab(Transform card, Vector2 size)
        {
            var tab = Ui.Placed("Act", card, new Vector2(0, size.y / 2 + 9), new Vector2(40, 20));
            Ui.Image(tab, Actionable).raycastTarget = false;
            var glyph = Ui.Placed("Play", tab, Vector2.zero, new Vector2(18, 18)).gameObject.AddComponent<RawImage>();
            glyph.texture = Icons.For("play");
            glyph.color = Ui.Hex("#1A1408");
            glyph.raycastTarget = false;
        }

        public static RectTransform Pile(Transform parent, string label, Vector2 center, int count, FieldCard? top, CardInfo? topInfo,
            int owner, Action<CardInfo>? onHover, Action? onClick = null, bool highlight = false)
        {
            var slot = Ui.Placed(label, parent, center, FieldSize);
            Ui.Image(slot, highlight ? Actionable : new Color(1, 1, 1, 0.06f));
            slot.gameObject.AddComponent<CardView>()._onClick = onClick;
            if (count > 0)
            {
                if (top != null && topInfo != null)
                    Create(slot, Vector2.zero, FieldSize - (highlight ? new Vector2(8, 8) : Vector2.zero), top, topInfo, owner, true,
                        onHover, offField: true, onClick: onClick);
                else
                {
                    var back = Ui.Stretched("Back", slot, Vector2.zero, Vector2.one);
                    if (highlight) back.offsetMin = new Vector2(4, 4);
                    if (highlight) back.offsetMax = new Vector2(-4, -4);
                    DrawBack(back, star: false).raycastTarget = false;
                }
            }
            if (count > 0)
            {
                // The count sits where a card back's star would: no star under it, however the pile's top was drawn.
                foreach (var part in slot.GetComponentsInChildren<Transform>(true))
                    if (part.name == "Star") part.gameObject.SetActive(false);
                // The count big on the pile itself (Link Evolution): readable at a glance across the table.
                var number = Ui.Text(Ui.Stretched("Number", slot, Vector2.zero, Vector2.one), count.ToString(), FxSettings.Scaled(34), Color.white);
                number.fontStyle = FontStyle.Bold;
                number.raycastTarget = false;
                var outline = number.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0, 0, 0, 0.95f);
                outline.effectDistance = new Vector2(2, -2);
                number.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -3);
            }
            else
            {
                // Empty: the pile's own mark, faint (a cross for the GY, a crossed card for banished...).
                var icon = Ui.Placed("Empty", slot, Vector2.zero, new Vector2(48, 48)).gameObject.AddComponent<RawImage>();
                icon.texture = Icons.For(label switch { "GY" => "zone-gy", "Banished" => "zone-banish", "Extra" => "zone-extra", _ => "zone-deck" });
                icon.color = new Color(1, 1, 1, 0.28f);
                icon.raycastTarget = false;
            }
            // Its name on a dark plate underneath, so it reads on any mat picture.
            var badge = Ui.Placed("Count", slot, new Vector2(0, -FieldSize.y / 2 - 11), new Vector2(Mathf.Min(FieldSize.x + 24, label.Length * 7.5f + 14), 19));
            Ui.Image(badge, new Color(0.02f, 0.03f, 0.06f, 0.72f)).raycastTarget = false;
            Ui.Text(badge, label, 13, new Color(1, 1, 1, 0.92f)).raycastTarget = false;
            if (highlight && FxSettings.PlayableTabs) ActionTab(slot, FieldSize);
            return slot;
        }

        /// <summary>The card under the pointer (any screen), or null: the play layout keeps the card detail open while there is one.</summary>
        public static CardView? PointerCard { get; private set; }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_info == null) return;
            PointerCard = this;
            Hovered = (_info.Code, _look);
            _onHover?.Invoke(_info);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (PointerCard == this) PointerCard = null;
        }

        private void OnDisable()
        {
            if (PointerCard == this) PointerCard = null;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.dragging) return; // a drag that ends on the card isn't a click
            if (_editorClick != null) { _editorClick(eventData.button); return; }
            if (eventData.button == PointerEventData.InputButton.Right) _onRightClick?.Invoke();
            else _onClick?.Invoke();
        }

        private Action<PointerEventData.InputButton>? _editorClick;

        /// <summary>
        /// The deck editor's cards (click to focus, docs/dev/EDITOR-CLICK-FOCUS-SPEC-2026-11-19.md): every click goes to
        /// <paramref name="click"/> with its button (a drag that ends on the card still isn't one), and pointing at the card
        /// only lifts it with a hairline (a Tile hover, its border's colour left alone). Make the view with no hover action,
        /// so pointing never changes the card detail. Duel boards never call this: their cards keep hover as it is.
        /// </summary>
        public void EditorMode(Action<PointerEventData.InputButton> click)
        {
            _editorClick = click;
            var hover = UiHover.Add((RectTransform)transform, HoverStyle.Tile);
            hover.Fill = null; // lift and hairline only: the border keeps its meaning (gold, the owner's blue)
        }

        /// <summary>Cards narrower than this leave the Level / Rank out of their stat line (ATK/DEF come first).</summary>
        private const float MinWidthForLevel = 80;

        private static string StatLine(FieldCard card, CardInfo? info, bool withLevel = true)
        {
            var type = info?.Type ?? card.Type;
            if ((type & CardType.Monster) == 0) return (type & CardType.Spell) != 0 ? "SPELL" : "TRAP";
            // Stats above their printed values show in blue with ▲, below in red with ▼ (Master Duel's colors, plus an
            // arrow so it isn't color alone). "?" stats (printed 0) are left plain.
            string Stat(int now, int printed) =>
                info == null || printed <= 0 || now == printed || !card.IsFaceUp ? CardDetail.Stat(now)
                : now > printed ? $"<color=#6FC3FF>▲{now}</color>" : $"<color=#FF7070>▼{now}</color>";
            if ((type & CardType.Link) != 0)
                return withLevel ? $"ATK {Stat(card.Attack, info?.Attack ?? 0)} LINK-{card.LinkRating}" : $"ATK {Stat(card.Attack, info?.Attack ?? 0)}";
            // A card drawn outside a duel (shop, packs, editor) often has no Rank set: its printed one is in the card's
            // Level (low byte), as the data stores it.
            int rank = card.Rank > 0 ? (int)card.Rank : (int)((info?.Level ?? (uint)card.Level) & 0xff);
            var stars = (type & CardType.Xyz) != 0 ? $"R{rank}" : $"L{card.Level}";
            var mats = card.OverlayCount > 0 ? $" [{card.OverlayCount}]" : "";
            var stats = $"{Stat(card.Attack, info?.Attack ?? 0)}/{Stat(card.Defense, info?.Defense ?? 0)}{mats}";
            return withLevel ? $"{stars} {stats}" : stats;
        }

        public static Color FrameColor(uint type)
        {
            if ((type & CardType.Spell) != 0) return Ui.Hex("#1D9C73");
            if ((type & CardType.Trap) != 0) return Ui.Hex("#B0457F");
            if ((type & CardType.Token) != 0) return Ui.Hex("#9A9A9A");
            if ((type & CardType.Link) != 0) return Ui.Hex("#245FAF");
            if ((type & CardType.Xyz) != 0) return Ui.Hex("#262626");
            if ((type & CardType.Synchro) != 0) return Ui.Hex("#E4E4E4");
            if ((type & CardType.Fusion) != 0) return Ui.Hex("#8853B0");
            if ((type & CardType.Ritual) != 0) return Ui.Hex("#5B8DD9");
            if ((type & 0x20) != 0) return Ui.Hex("#D07A35"); // Effect
            return Ui.Hex("#D9B85C"); // Normal
        }

        public static Color TextColor(uint type) =>
            (type & (CardType.Xyz | CardType.Link | CardType.Spell | CardType.Trap | CardType.Fusion)) != 0
                ? Color.white
                : new Color(0.1f, 0.08f, 0.05f);
    }
}
