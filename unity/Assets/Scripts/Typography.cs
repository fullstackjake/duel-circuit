using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>The game's five font faces (docs/dev/TYPE-AND-FONTS-SPEC-2026-11-21.md, section 3).</summary>
    public enum Face { Body, Italic, Strong, Condensed, CondensedBold }

    /// <summary>
    /// The game's fonts: Barlow for reading (Regular, Italic, SemiBold) and Barlow Condensed for display (SemiBold, Bold), each
    /// its own font asset in unity/Assets/Resources/GameFonts/ (not Resources/Fonts/: that is the alley stencil's folder).
    /// Weights are separate faces, so text never sets <see cref="FontStyle.Bold"/> or <see cref="FontStyle.Italic"/> (a faux
    /// weight): <see cref="Strong"/> and <see cref="Italic"/> pick the face. Symbols Barlow lacks (★ ▲ ⬢...) are drawn by the
    /// fallback chain in each face's import settings: Noto Sans Symbols 2 in the project, then the system's fonts.
    /// </summary>
    public static class Typography
    {
        private static readonly string[] Files =
            { "Barlow-Regular", "Barlow-Italic", "Barlow-SemiBold", "BarlowCondensed-SemiBold", "BarlowCondensed-Bold" };
        private static readonly Font?[] Fonts = new Font?[Files.Length];
        private static readonly bool[] Absent = new bool[Files.Length];
        private static Font? _builtin, _symbols;

        /// <summary>
        /// The face's font asset. A file missing from the build gives Unity's built-in font (with a warning once, the miss
        /// remembered); <see cref="Strong"/> and <see cref="Italic"/> then use the built-in font's own bold and italic.
        /// </summary>
        public static Font Font(Face face)
        {
            int i = (int)face;
            if (Fonts[i] != null) return Fonts[i]!;
            if (Absent[i]) return Builtin;
            Fonts[i] = Resources.Load<Font>("GameFonts/" + Files[i]);
            if (Fonts[i] != null) return Fonts[i]!;
            Absent[i] = true;
            Debug.LogWarning($"Typography: GameFonts/{Files[i]} isn't in this build; that face draws in the built-in font.");
            return Builtin;
        }

        /// <summary>Whether the face's file is in this build (false: it draws in the built-in font).</summary>
        public static bool Has(Face face) => !ReferenceEquals(Font(face), Builtin);

        /// <summary>The symbol fallback the faces name first (Noto Sans Symbols 2), for checks; null when it isn't in the build.</summary>
        public static Font? Symbols => _symbols != null ? _symbols : _symbols = Resources.Load<Font>("GameFonts/NotoSansSymbols2-Regular");

        /// <summary>The scale's face for a role (section 1's Face column).</summary>
        public static Face FaceOf(TextRole role) => role switch
        {
            TextRole.Caption => Face.Strong,
            TextRole.Small => Face.Body,
            TextRole.Body => Face.Body,
            TextRole.Label => Face.Condensed,
            TextRole.Subhead => Face.Strong,
            TextRole.Heading => Face.Condensed,
            _ => Face.CondensedBold, // Title, Display
        };

        /// <summary>Unity's built-in font (LegacyRuntime.ttf): what every text used before 0.4.</summary>
        public static Font Builtin => _builtin != null ? _builtin : _builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>Which of the five faces <paramref name="font"/> is (null: the built-in font, the alley stencil, another).</summary>
        public static Face? FaceOfFont(Font? font)
        {
            if (font == null || ReferenceEquals(font, Builtin)) return null;
            for (int i = 0; i < Files.Length; i++)
                if (ReferenceEquals(font, Font((Face)i))) return (Face)i;
            return null;
        }

        /// <summary>Sets the face, or (its file missing) the built-in font with <paramref name="fallbackStyle"/>, so emphasis still shows.</summary>
        private static Text Apply(Text text, Face face, FontStyle fallbackStyle)
        {
            if (Has(face)) { text.font = Font(face); text.fontStyle = FontStyle.Normal; }
            else { text.font = Builtin; text.fontStyle = fallbackStyle; }
            return text;
        }

        private static bool IsCondensed(Face face) => face == Face.Condensed || face == Face.CondensedBold;

        /// <summary>
        /// The heavier face of the text's family: Barlow SemiBold for Barlow, Barlow Condensed Bold for Barlow Condensed. Text at
        /// a display size (the Title and Display roles by <see cref="UiTheme.RoleFor"/>: 30 and up) takes the display face,
        /// Barlow Condensed Bold, as screen titles, "Victory!", "VS" and banners do in the scale. The face the text had is
        /// remembered on it, so <see cref="Weight"/>(false) puts exactly that back, and calling Strong again after a size
        /// change picks again from it. The built-in font keeps its own bold; a stencil or other font is left as it is. Always
        /// upright: there is no bold italic.
        /// </summary>
        public static Text Strong(Text text)
        {
            var memory = text.GetComponent<FaceMemory>();
            Face? regular = memory != null ? memory.Regular : FaceOfFont(text.font);
            if (regular is not Face face)
            {
                text.fontStyle = ReferenceEquals(text.font, Builtin) ? FontStyle.Bold : FontStyle.Normal;
                return text;
            }
            if (memory == null) text.gameObject.AddComponent<FaceMemory>().Regular = face;
            bool condensed = IsCondensed(face) || UiTheme.RoleFor(text.fontSize) >= TextRole.Title;
            return Apply(text, condensed ? Face.CondensedBold : Face.Strong, FontStyle.Bold);
        }

        /// <summary>
        /// <see cref="Strong"/> when <paramref name="strong"/>, else the face the text had before it was made strong (for texts
        /// that turn bold and back: a picked tab, a chosen rule).
        /// </summary>
        public static Text Weight(Text text, bool strong)
        {
            if (strong) return Strong(text);
            var memory = text.GetComponent<FaceMemory>();
            if (memory != null) return Apply(text, memory.Regular, memory.Regular == Face.Italic ? FontStyle.Italic : FontStyle.Normal);
            if (ReferenceEquals(text.font, Builtin)) text.fontStyle = FontStyle.Normal;
            return text;
        }

        /// <summary>Barlow Italic (quotes, flavour text, hints); the built-in font (or a missing Italic file) uses its own italic.</summary>
        public static Text Italic(Text text)
        {
            if (FaceOfFont(text.font) == null && text.font != null && !ReferenceEquals(text.font, Builtin)) return text; // a stencil or another font: as it is
            return Apply(text, Face.Italic, FontStyle.Italic);
        }

        /// <summary>A counter that changes while seen (LP, DP, pile counts): fixed-width digits (<see cref="TabularDigits"/>).</summary>
        public static Text Counter(Text text)
        {
            if (text.GetComponent<TabularDigits>() == null) text.gameObject.AddComponent<TabularDigits>();
            return text;
        }

        /// <summary>
        /// Marks a hero number, outside the scale with its literal size (section 1): the LP numbers, pile counts, the turn
        /// disc, the results' DP total, the title logo's text fallback. The layout rule (R2) skips texts where <see cref="IsHero"/>.
        /// </summary>
        public static Text Hero(Text text)
        {
            if (text.GetComponent<HeroNumber>() == null) text.gameObject.AddComponent<HeroNumber>();
            return text;
        }

        /// <summary>Whether <see cref="Hero"/> marked the text.</summary>
        public static bool IsHero(Text text) => text.GetComponent<HeroNumber>() != null;

        /// <summary>
        /// Characters of <paramref name="s"/> no font in <paramref name="face"/>'s chain draws (the face, then its fallbacks:
        /// Noto Sans Symbols 2, then the system's fonts named in its import settings). Whitespace and control characters
        /// don't count. A character outside the Basic Multilingual Plane (a surrogate pair) is always missing: legacy Text
        /// draws UTF-16 units one by one.
        /// </summary>
        public static IReadOnlyList<char> Missing(string s, Face face)
        {
            var font = Font(face);
            var missing = new List<char>();
            const int size = 32;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c) || char.IsControl(c) || missing.Contains(c)) continue;
                if (char.IsSurrogate(c)) { missing.Add(c); continue; }
                font.RequestCharactersInTexture(c.ToString(), size, FontStyle.Normal);
                if (!font.GetCharacterInfo(c, out var info, size, FontStyle.Normal) || info.advance <= 0) missing.Add(c);
            }
            return missing;
        }

        /// <summary>For checks: a character's advance in <paramref name="font"/> at <paramref name="size"/> (0 when it has none).</summary>
        internal static int DebugAdvance(Font font, char c, int size)
        {
            font.RequestCharactersInTexture(c.ToString(), size, FontStyle.Normal);
            return font.GetCharacterInfo(c, out var info, size, FontStyle.Normal) ? info.advance : 0;
        }
    }

    /// <summary>
    /// Tabular figures for legacy Text, which can't turn on the font's own: each digit gets a cell as wide as the widest of
    /// the ten digits at its own size (a &lt;size&gt; run's digits get that run's cell), its ink centred in the cell, so a
    /// counter's digits don't shift sideways as it rolls ("1111" is as wide as "8888"). The line then moves back by its
    /// alignment, as <see cref="LetterSpacing"/> does. The digits' glyphs are asked of the font in LateUpdate, never while the
    /// canvas rebuilds; a digit whose glyph isn't there yet keeps its own width for one frame. Text whose quads can't be
    /// matched to its characters is left as it is.
    /// </summary>
    public sealed class TabularDigits : BaseMeshEffect
    {
        private const string Digits = "0123456789";
        private static readonly List<UIVertex> Stream = new List<UIVertex>();
        private static readonly List<int> Visible = new List<int>(), PxOf = new List<int>(), LineOf = new List<int>();
        private static readonly List<float> Shift = new List<float>(), LineGrowth = new List<float>();
        private static readonly List<int> SizeStack = new List<int>();
        private static readonly string[] Tags = { "color", "b", "i", "size", "material", "quad" };
        private static readonly Dictionary<(Font, int), int> CellCache = new Dictionary<(Font, int), int>();

        private Text? _text;
        private string? _seen;
        private Font? _seenFont;
        private float _seenPpu;
        private int _seenSize, _missedPx;

        /// <summary>For checks: each quad's centre x after the last pass (local units), and whether the pass ran.</summary>
        internal readonly List<float> DebugCenters = new List<float>();
        internal bool DebugApplied;
        /// <summary>For checks: the drawn ink's width (left of the first quad to right of the last, local units) after the last pass.</summary>
        internal float DebugWidth;

        private static float InkWidth()
        {
            if (Stream.Count == 0) return 0;
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < Stream.Count; i++) { min = Mathf.Min(min, Stream[i].position.x); max = Mathf.Max(max, Stream[i].position.x); }
            return max - min;
        }

        private Text? TextOf => _text != null ? _text : _text = GetComponent<Text>();

        protected override void OnEnable()
        {
            base.OnEnable();
            Prepare(true);
        }

        private void LateUpdate() => Prepare(false);

        /// <summary>For checks that measure in the same frame they set the text: asks the font for the digits now.</summary>
        internal void PrepareNow() => Prepare(true);

        /// <summary>Asks the font for the digits at every pixel size the text draws them at (outside the canvas rebuild).</summary>
        private void Prepare(bool force)
        {
            var text = TextOf;
            if (text == null || text.font == null) return;
            float ppu = text.pixelsPerUnit;
            var s = text.text ?? "";
            if (!force && _missedPx == 0 && ReferenceEquals(s, _seen) && ReferenceEquals(text.font, _seenFont) && ppu == _seenPpu && text.fontSize == _seenSize) return;
            _seen = s;
            _seenFont = text.font;
            _seenPpu = ppu;
            _seenSize = text.fontSize;
            var gen = text.cachedTextGenerator;
            Request(text.font, BasePx(text, gen));
            Request(text.font, Mathf.RoundToInt(text.fontSize * ppu));
            if (text.supportRichText)
                for (int i = 0; i < s.Length; i++)
                    if (s[i] == '<' && SizeTag(s, i, out int size, out int close)) { Request(text.font, Mathf.RoundToInt(size * ppu)); i = close; }
            if (_missedPx > 0) { Request(text.font, _missedPx); _missedPx = 0; }
            if (!force) text.SetVerticesDirty();
        }

        private static void Request(Font font, int px)
        {
            if (px <= 0) return;
            font.RequestCharactersInTexture(Digits, px, FontStyle.Normal);
        }

        private static int BasePx(Text text, TextGenerator gen) =>
            gen.characterCount > 0 && gen.fontSizeUsedForBestFit > 0 ? gen.fontSizeUsedForBestFit : Mathf.RoundToInt(text.fontSize * text.pixelsPerUnit);

        /// <summary>The widest digit's advance at <paramref name="px"/>, or 0 when a digit isn't in the font's atlas yet (no request).</summary>
        private static int Cell(Font font, int px)
        {
            if (CellCache.TryGetValue((font, px), out int cell)) return cell;
            for (int i = 0; i < Digits.Length; i++)
            {
                if (!font.GetCharacterInfo(Digits[i], out var info, px, FontStyle.Normal)) return 0;
                cell = Mathf.Max(cell, info.advance);
            }
            if (CellCache.Count > 512) CellCache.Clear();
            CellCache[(font, px)] = cell;
            return cell;
        }

        /// <summary>A rich-text tag at <paramref name="open"/>: &lt;size=N&gt; gives N; true only for size tags (closing ones give 0).</summary>
        private static bool SizeTag(string s, int open, out int size, out int close)
        {
            size = 0;
            close = s.IndexOf('>', open);
            if (close < 0) return false;
            if (string.CompareOrdinal(s, open, "</size>", 0, 7) == 0) return true;
            if (string.CompareOrdinal(s, open, "<size=", 0, 6) != 0) return false;
            for (int i = open + 6; i < close; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') return false;
                size = size * 10 + (c - '0');
            }
            return size > 0;
        }

        private static bool IsTag(string s, int open, out int close)
        {
            close = s.IndexOf('>', open);
            if (close < 0) return false;
            int at = open + 1;
            if (at < close && s[at] == '/') at++;
            foreach (var tag in Tags)
                if (string.CompareOrdinal(s, at, tag, 0, tag.Length) == 0 && (at + tag.Length == close || s[at + tag.Length] == '=' || s[at + tag.Length] == '>'))
                    return true;
            return false;
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            DebugApplied = false;
            DebugCenters.Clear();
            var text = TextOf;
            if (!IsActive() || text == null || text.font == null) return;
            var gen = text.cachedTextGenerator;
            var s = text.text ?? "";
            Stream.Clear();
            vh.GetUIVertexStream(Stream);
            int quads = Stream.Count / 6;
            DebugWidth = InkWidth();
            if (quads == 0 || gen.characterCount == 0) return;
            float ppu = text.pixelsPerUnit;
            int basePx = BasePx(text, gen);

            // The characters that made quads, in order (not whitespace, not inside a rich-text tag), with each one's pixel size.
            Visible.Clear();
            PxOf.Clear();
            SizeStack.Clear();
            for (int i = 0; i < s.Length && i < gen.characterCount; i++)
            {
                if (text.supportRichText && s[i] == '<')
                {
                    if (SizeTag(s, i, out int size, out int close))
                    {
                        if (size > 0) SizeStack.Add(Mathf.RoundToInt(size * ppu));
                        else if (SizeStack.Count > 0) SizeStack.RemoveAt(SizeStack.Count - 1);
                        i = close;
                        continue;
                    }
                    if (IsTag(s, i, out close)) { i = close; continue; }
                }
                if (char.IsWhiteSpace(s[i])) continue;
                Visible.Add(i);
                PxOf.Add(SizeStack.Count > 0 ? SizeStack[SizeStack.Count - 1] : basePx);
            }
            if (Visible.Count != quads) return;
            var chars = gen.characters;
            var lines = gen.lines;

            // Per line: each digit gets its cell, its ink centred in it; everything after a digit shifts by what the cells added.
            Shift.Clear();
            LineOf.Clear();
            LineGrowth.Clear();
            for (int l = 0; l < lines.Count; l++) LineGrowth.Add(0);
            int line = 0;
            float run = 0;
            for (int q = 0; q < quads; q++)
            {
                int ci = Visible[q];
                int nl = line;
                while (nl + 1 < lines.Count && lines[nl + 1].startCharIdx <= ci) nl++;
                if (nl != line) { LineGrowth[line] = run; line = nl; run = 0; }
                LineOf.Add(line);
                char c = s[ci];
                int cell = c >= '0' && c <= '9' ? Cell(text.font, PxOf[q]) : 0;
                if (c >= '0' && c <= '9' && cell == 0) _missedPx = PxOf[q]; // not in the atlas yet: LateUpdate asks for it
                if (cell > 0)
                {
                    float own = chars[ci].charWidth;
                    float cellCentre = chars[ci].cursorPos.x / ppu + run + cell / ppu / 2;
                    float inkCentre = 0;
                    for (int k = 0; k < 6; k++) inkCentre += Stream[q * 6 + k].position.x;
                    Shift.Add(cellCentre - inkCentre / 6);
                    run += (cell - own) / ppu;
                }
                else Shift.Add(run);
            }
            if (lines.Count > 0) LineGrowth[line] = run;

            int column = (int)text.alignment % 3; // left, centre, right
            for (int q = 0; q < quads; q++)
            {
                float grow = LineOf[q] < LineGrowth.Count ? LineGrowth[LineOf[q]] : 0;
                float back = column == 1 ? -grow / 2 : column == 2 ? -grow : 0;
                float centre = 0;
                for (int k = 0; k < 6; k++)
                {
                    var v = Stream[q * 6 + k];
                    v.position.x += Shift[q] + back;
                    Stream[q * 6 + k] = v;
                    centre += v.position.x;
                }
                DebugCenters.Add(centre / 6);
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(Stream);
            DebugApplied = true;
            DebugWidth = InkWidth();
        }

        /// <summary>
        /// The text's width with tabular digits (its preferred width plus what the digits gain), for code that places
        /// something just after a counter, every frame: at the pixel size it draws at, without asking the font for anything.
        /// One size only (no &lt;size&gt; runs).
        /// </summary>
        public static float PreferredWidth(Text text)
        {
            float width = text.preferredWidth;
            var s = text.text;
            if (text.font == null || string.IsNullOrEmpty(s)) return width;
            int px = Mathf.RoundToInt(text.fontSize * text.pixelsPerUnit);
            int cell = Cell(text.font, px);
            if (cell <= 0) return width;
            float extra = 0;
            for (int i = 0; i < s.Length; i++)
                if (s[i] >= '0' && s[i] <= '9' && text.font.GetCharacterInfo(s[i], out var info, px, FontStyle.Normal)) extra += cell - info.advance;
            return width + extra / text.pixelsPerUnit;
        }

        /// <summary>
        /// <paramref name="s"/> with every digit as the font's widest one, so a plain measure (a TextGenerator's preferred
        /// width) comes out as wide as the text draws with tabular digits. Outside the canvas rebuild (it asks the font).
        /// </summary>
        public static string AsDrawn(string s, Font font)
        {
            font.RequestCharactersInTexture(Digits, 64, FontStyle.Normal);
            char widest = '0';
            int best = -1;
            for (int i = 0; i < Digits.Length; i++)
                if (font.GetCharacterInfo(Digits[i], out var info, 64, FontStyle.Normal) && info.advance > best) { best = info.advance; widest = Digits[i]; }
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++) if (chars[i] >= '0' && chars[i] <= '9') chars[i] = widest;
            return new string(chars);
        }
    }

    /// <summary>The face a text had before <see cref="Typography.Strong"/> made it strong (Weight(false) puts it back).</summary>
    public sealed class FaceMemory : MonoBehaviour
    {
        public Face Regular;
    }

    /// <summary>Marks a hero number (<see cref="Typography.Hero"/>): outside the type scale.</summary>
    public sealed class HeroNumber : MonoBehaviour { }
}
