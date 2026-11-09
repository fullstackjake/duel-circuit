using System;
using UnityEngine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Every color role the shell screens use (Campaign, Shop, Trader, Results, Versus and the deck editor's frame),
    /// in two themes: <see cref="MainStreet"/> (today's colors, unchanged) and <see cref="BackAlley"/> (wet asphalt,
    /// warm paper text, neon on signs and dividers only).
    ///
    /// Not part of the theme: the duel board's gameplay colors (blue you, red them, gold can act, so also the
    /// Versus side colors #4C8DFF / #FF5A5A), card faces, rarity / grade / foil colors, limit and ban-pass badges
    /// on cards, pack-reveal tags, deck-editor data colors (chips, charts, card kinds, drag zones), the circuit
    /// map's region art, the coin, and the Library's materials (wood shelf, brass plates, final plaque).
    ///
    /// Usage: take <c>var T = UiTheme.Current;</c> at the top of a Build/Refresh and read roles from it.
    /// Rich text: <c>$"&lt;color={T.GoldHex}&gt;"</c>, or <c>T.Paint("text", T.Good)</c>.
    /// Alpha variants: <c>T.Text.WithAlpha(0.45f)</c>.
    /// The full mapping (old hex → role) and conversion rules are in docs/dev/UI-THEME.md.
    /// </summary>
    public sealed class UiTheme
    {
        public string Name { get; private set; } = "";

        // ---------- Surfaces ----------

        /// <summary>Screen background (Campaign / Shop / Trader root, deck editor root, Versus header band). Was #121620, #141821, #11151F.</summary>
        public Color Base { get; private set; }
        /// <summary>The darkest ground: top header bars (90 px) and the full-screen scene backgrounds (Versus, Results, Coin toss). Was #0D1017, #0B0E15.</summary>
        public Color Deep { get; private set; }
        /// <summary>Main panels: detail panes, the deck editor's side columns, Locker / New game panels, editor charts. Was #1A1F2B.</summary>
        public Color Panel { get; private set; }
        /// <summary>The darker panel tier: Versus side panels, Results cards, guest / lesson panels, editor modals and deck browser. Was #161B27.</summary>
        public Color PanelDeep { get; private set; }
        /// <summary>Recessed wells: input fields, card / showcase slots, XP bar track, trader card face. Was #0E1118.</summary>
        public Color Well { get; private set; }
        /// <summary>Raised tiles inside a panel: Locker poster / mat tiles, editor filter pickers, Results pips not yet won. Was #232A38, #232B3D, #232A3A.</summary>
        public Color Inset { get; private set; }
        /// <summary>List row, even index (shop sets, trader cards, match rows not won). Was #20273A.</summary>
        public Color RowA { get; private set; }
        /// <summary>List row, odd index; also starter tiles, offer rows, deck browser rows, locked final rounds. Was #1C2232, #1E2535, #1C2130.</summary>
        public Color RowB { get; private set; }
        /// <summary>The selected row (shop set, trader card, current deck in the browser). Was #2E3A55.</summary>
        public Color RowSelected { get; private set; }
        /// <summary>A match row you have won. Was #232B1E.</summary>
        public Color RowWon { get; private set; }
        /// <summary>The Library's Champion's-table row. Was #2B2534.</summary>
        public Color RowChampion { get; private set; }
        /// <summary>A playable round of the Every Era final. Was #2B2716.</summary>
        public Color RowPlayable { get; private set; }
        /// <summary>Gold-tinted ground for the chosen poster / mat tile. Was #3A3220.</summary>
        public Color GoldWash { get; private set; }
        /// <summary>Results: the "unlocked" rows (text in <see cref="GoldSoft"/>). Was #3A2F12.</summary>
        public Color Unlock { get; private set; }

        // ---------- Controls ----------

        /// <summary>Default button fill (Ui.BoxButton, Scout, Back, tabs not picked in the Locker). Was #2A3448.</summary>
        public Color Button { get; private set; }
        /// <summary>A tab that isn't picked (Rules / Library tabs) and a disabled button (tag locked). Was #1C2333, #1E2433.</summary>
        public Color ButtonOff { get; private set; }
        /// <summary>The picked tab (Rules / Library tabs, Locker Posters / Mats). Was #3A4A66, #3A4A6A.</summary>
        public Color TabOn { get; private set; }
        /// <summary>A button for something already done (a passed lesson, label in <see cref="Good"/>). Was #24402C.</summary>
        public Color ButtonDone { get; private set; }
        /// <summary>The one primary action per screen (Duel, Buy a pack, Trade, Continue). Red in both themes. Was #B8432F.</summary>
        public Color Primary { get; private set; }
        /// <summary>Active filter chip in the deck editor ("Spell ×"). Was #3D6FB5.</summary>
        public Color Chip { get; private set; }
        /// <summary>The trader's "OCG" chip. Was #C8622F.</summary>
        public Color ImportChip { get; private set; }
        /// <summary>Selection frames and gold accents: chosen poster / mat outline, drag ghost frame, offer-row stripe, armed button, Versus edge outline. Was #FFC940.</summary>
        public Color Highlight { get; private set; }
        /// <summary>Outline of the editor's card-detail modal. Was rgba(0.35, 0.6, 1, 0.6).</summary>
        public Color Focus { get; private set; }

        // ---------- Hover, pressed, disabled (UiHover; docs/dev/HOVER-DESIGN-2026-11-07.md) ----------

        /// <summary>A hovered button's fill: <c>Lerp(rest, Text, HoverMix)</c>. Lerp, not a tint: a multiply can only darken a dark fill.</summary>
        public float HoverMix { get; private set; }
        /// <summary>The same on an accent fill (Primary red, a chip), gentler so white text keeps its contrast (about 4.9:1 on Primary).</summary>
        public float AccentHoverMix { get; private set; }
        /// <summary>A hovered row or tile: <c>Lerp(row, Text, RowHoverMix)</c>, always weaker than <see cref="RowSelected"/>.</summary>
        public float RowHoverMix { get; private set; }
        /// <summary>The hover's hairline: a 1 px inner edge on buttons and tiles, a 2 px left bar on rows. Text at 22%.</summary>
        public Color HoverEdge { get; private set; }
        /// <summary>A pressed fill: <c>Lerp(rest, Base, PressMix)</c>, applied the moment the pointer goes down.</summary>
        public float PressMix { get; private set; }
        /// <summary>A pressed button's drawn size (its hit area keeps its size).</summary>
        public float PressScale { get; private set; }
        /// <summary>A disabled button's fill multiplier (the Button's ColorTint disabled colour): about <see cref="ButtonOff"/> on <see cref="Button"/>, opaque.</summary>
        public Color Disabled { get; private set; }

        /// <summary>Hover fades in over this (seconds, unscaled time).</summary>
        public const float HoverIn = 0.06f;
        /// <summary>Hover fades out over this; a press lets go over <see cref="PressOut"/>; nothing runs longer than 0.1 s.</summary>
        public const float HoverOut = 0.1f, PressOut = 0.08f;
        /// <summary>A hovered map node's drawn size; a hovered slab or tile is drawn this many reference pixels higher.</summary>
        public const float NodeHoverScale = 1.06f, TileLift = 4f;
        /// <summary>A hovered map node's ring: <c>Lerp(ring, white, RingHoverMix)</c>.</summary>
        public const float RingHoverMix = 0.35f;

        // ---------- Overlays, bars, stripes ----------

        /// <summary>Full-screen dims behind modals. Opaque black: apply the call site's alpha, e.g. <c>T.Scrim.WithAlpha(0.75f)</c>.</summary>
        public Color Scrim { get; private set; }
        /// <summary>Near-black glass: the pack-opening backdrop, map name backings and band strips (keep the call site's alpha). Was rgb(0.03, 0.04, 0.07), rgb(0.04, 0.05, 0.08).</summary>
        public Color Glass { get; private set; }
        /// <summary>Slightly lifted glass: the pack-opening info box, the editor's opening-hand overlay (keep the alpha). Was rgb(0.06, 0.08, 0.12), rgb(0.05, 0.06, 0.09).</summary>
        public Color GlassSoft { get; private set; }
        /// <summary>Thin rules between sections (Results "You have" line). Neon in the alley. Was white at 0.15.</summary>
        public Color Divider { get; private set; }
        /// <summary>Deck editor notice bar (save as new prompt). Was #3A2E52.</summary>
        public Color NoticeBar { get; private set; }
        /// <summary>Deck editor warning bar (recovered unsaved changes). Was #4A3D1C.</summary>
        public Color WarnBar { get; private set; }
        /// <summary>Brass stripes on guest / lesson panels (unlocked, not passed). Was #B8862B.</summary>
        public Color Brass { get; private set; }
        /// <summary>The same stripe when locked. Was #3A4050.</summary>
        public Color Locked { get; private set; }
        /// <summary>Lesson stripe once passed. Was #3FA56B.</summary>
        public Color GoodStripe { get; private set; }
        /// <summary>Results: a won progress pip. Was #2F5A35.</summary>
        public Color PipOn { get; private set; }
        /// <summary>Results: the XP bar fill. Was #4CAF6A.</summary>
        public Color Meter { get; private set; }
        /// <summary>Placeholder portrait tile (a guest's initial). Was #13203A.</summary>
        public Color Placeholder { get; private set; }

        // ---------- Text ----------

        /// <summary>Primary text and button labels. Was Color.white.</summary>
        public Color Text { get; private set; }
        /// <summary>Bright reading copy: card text in the editor modal, scouting tips. Was #E2E7F0, rgb(0.88, 0.9, 0.95), rgb(0.92, 0.94, 0.98).</summary>
        public Color TextBright { get; private set; }
        /// <summary>Body copy: card text, card names under reveals, lesson results. Was rgb(0.85, 0.87, 0.92), rgb(0.86, 0.88, 0.93), rgb(0.85, 0.88, 0.94).</summary>
        public Color TextBody { get; private set; }
        /// <summary>Status bar, help text, blurbs, scouting lines. Was rgb(0.8, 0.83, 0.9) and its 0.78-0.82 neighbours.</summary>
        public Color TextSoft { get; private set; }
        /// <summary>Subtitles and secondary lines (header subtitles, circuit blurbs, "You have"). Was rgb(0.75, 0.78, 0.86), rgb(0.75, 0.78, 0.85).</summary>
        public Color TextMuted { get; private set; }
        /// <summary>Row second lines (deck / style, "you own"), empty-list notes. Was rgb(0.7, 0.74, 0.82), rgb(0.72, 0.76, 0.84), rgb(0.7, 0.72, 0.8).</summary>
        public Color TextFaint { get; private set; }
        /// <summary>Quiet notes: starter tags, partner bond, guest decks, "Not passed yet". Was rgb(0.62, 0.67, 0.77) and 0.6-0.65 neighbours.</summary>
        public Color TextQuiet { get; private set; }
        /// <summary>The dim rich-text gray: counts, "(locked)", meta in headers, tabs not picked. Was #8A93A6.</summary>
        public Color TextDim { get; private set; }
        /// <summary>Small caps captions ("YOUR DECK", "DUEL POINTS", "OPENING HAND"). Alpha is part of the role. Was white at 0.5 / 0.55.</summary>
        public Color Caption { get; private set; }
        /// <summary>Dark text on a gold fill (starter ribbon, armed button label). Was #161B27.</summary>
        public Color OnAccent { get; private set; }
        /// <summary>Spoken quotes (Versus, Results, lessons). Was #F2E6B8.</summary>
        public Color Quote { get; private set; }
        /// <summary>Italic opponent intros on match rows. Was rgb(0.85, 0.85, 0.7).</summary>
        public Color Intro { get; private set; }

        // ---------- Signs and meaning ----------

        /// <summary>A screen's name plate: CAMPAIGN, CARD SHOP, CARD TRADER and the scene banners (circuit name, COIN TOSS, FREE DUEL, SPECIAL GUESTS, GUS'S LESSONS). Neon pink in the alley. Was #FFC940.</summary>
        public Color Sign { get; private set; }
        /// <summary>Currency and the gold everything else: DP, prices, prizes, header DP line, "Cleared", in-panel headings. Unchanged in the alley. Was #FFC940.</summary>
        public Color Gold { get; private set; }
        /// <summary>Soft gold: tokens, trader header, cup / final names, wishlist, Results unlock text, cup map paths. Was #FFD86B.</summary>
        public Color GoldSoft { get; private set; }
        /// <summary>Library cup titles. Was #E6C27A.</summary>
        public Color LibraryGold { get; private set; }
        /// <summary>Won, ready, OK, "(new)" counts, EXP. Was #7BD88F, #7FE0A0.</summary>
        public Color Good { get; private set; }
        /// <summary>Amber warnings: limited, ahead of your band, your edit follows the list. Was #E3A21A.</summary>
        public Color Warn { get; private set; }
        /// <summary>Error / can't text, New game label. Was #FF8A8A.</summary>
        public Color Bad { get; private set; }
        /// <summary>Hard red text: Forbidden, not legal. Was #D9433F.</summary>
        public Color BadStrong { get; private set; }
        /// <summary>The warm red of the lesson "Not quite" headline. Was #FF8A6A.</summary>
        public Color BadWarm { get; private set; }
        /// <summary>Info blue: Scouting report title, "If you win the toss". Was #7FB2FF.</summary>
        public Color Info { get; private set; }
        /// <summary>Pale blue: "own N" in editor captions. Was #9FD8FF.</summary>
        public Color InfoSoft { get; private set; }
        /// <summary>Champion's table label. Was #B98CFF.</summary>
        public Color Violet { get; private set; }
        /// <summary>Scouting "Build against it". Was #D7A6FF.</summary>
        public Color VioletSoft { get; private set; }
        /// <summary>"OCG import" text in the shop. Was #FF9E6B.</summary>
        public Color Import { get; private set; }

        // ---------- Rich text ----------

        /// <summary>"#RRGGBB" for any color (alpha dropped), for <c>&lt;color=...&gt;</c> tags.</summary>
        public string Tag(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        /// <summary><c>&lt;color=#RRGGBB&gt;text&lt;/color&gt;</c>.</summary>
        public string Paint(string text, Color c) => $"<color={Tag(c)}>{text}</color>";

        public string GoldHex => Tag(Gold);
        public string GoldSoftHex => Tag(GoldSoft);
        public string GoodHex => Tag(Good);
        public string WarnHex => Tag(Warn);
        public string BadHex => Tag(Bad);
        public string BadStrongHex => Tag(BadStrong);
        public string TextDimHex => Tag(TextDim);
        public string SignHex => Tag(Sign);
        public string TextSoftHex => Tag(TextSoft);
        public string InfoHex => Tag(Info);
        public string InfoSoftHex => Tag(InfoSoft);
        public string VioletHex => Tag(Violet);
        public string VioletSoftHex => Tag(VioletSoft);
        public string ImportHex => Tag(Import);

        // ---------- The themes ----------

        public static readonly UiTheme MainStreet = new UiTheme
        {
            Name = "MainStreet",
            Base = H("#121620"),
            Deep = H("#0D1017"),
            Panel = H("#1A1F2B"),
            PanelDeep = H("#161B27"),
            Well = H("#0E1118"),
            Inset = H("#232A38"),
            RowA = H("#20273A"),
            RowB = H("#1C2232"),
            RowSelected = H("#2E3A55"),
            RowWon = H("#232B1E"),
            RowChampion = H("#2B2534"),
            RowPlayable = H("#2B2716"),
            GoldWash = H("#3A3220"),
            Unlock = H("#3A2F12"),

            Button = H("#2A3448"),
            ButtonOff = H("#1C2333"),
            TabOn = H("#3A4A66"),
            ButtonDone = H("#24402C"),
            Primary = H("#B8432F"),
            Chip = H("#3D6FB5"),
            ImportChip = H("#C8622F"),
            Highlight = H("#FFC940"),
            Focus = new Color(0.35f, 0.6f, 1f, 0.6f),

            HoverMix = 0.10f,
            AccentHoverMix = 0.06f,
            RowHoverMix = 0.05f,
            HoverEdge = new Color(1, 1, 1, 0.22f),
            PressMix = 0.35f,
            PressScale = 0.97f,
            Disabled = new Color(0.67f, 0.67f, 0.72f, 1f), // #2A3448 -> about #1C2333

            Scrim = Color.black,
            Glass = new Color(0.03f, 0.04f, 0.07f),
            GlassSoft = new Color(0.06f, 0.08f, 0.12f),
            Divider = new Color(1, 1, 1, 0.15f),
            NoticeBar = H("#3A2E52"),
            WarnBar = H("#4A3D1C"),
            Brass = H("#B8862B"),
            Locked = H("#3A4050"),
            GoodStripe = H("#3FA56B"),
            PipOn = H("#2F5A35"),
            Meter = H("#4CAF6A"),
            Placeholder = H("#13203A"),

            Text = Color.white,
            TextBright = new Color(0.88f, 0.9f, 0.95f),
            TextBody = new Color(0.85f, 0.87f, 0.92f),
            TextSoft = new Color(0.8f, 0.83f, 0.9f),
            TextMuted = new Color(0.75f, 0.78f, 0.86f),
            TextFaint = new Color(0.7f, 0.74f, 0.82f),
            TextQuiet = new Color(0.62f, 0.67f, 0.77f),
            TextDim = H("#8A93A6"),
            Caption = new Color(1, 1, 1, 0.5f),
            OnAccent = H("#161B27"),
            Quote = H("#F2E6B8"),
            Intro = new Color(0.85f, 0.85f, 0.7f),

            Sign = H("#FFC940"),
            Gold = H("#FFC940"),
            GoldSoft = H("#FFD86B"),
            LibraryGold = H("#E6C27A"),
            Good = H("#7BD88F"),
            Warn = H("#E3A21A"),
            Bad = H("#FF8A8A"),
            BadStrong = H("#D9433F"),
            BadWarm = H("#FF8A6A"),
            Info = H("#7FB2FF"),
            InfoSoft = H("#9FD8FF"),
            Violet = H("#B98CFF"),
            VioletSoft = H("#D7A6FF"),
            Import = H("#FF9E6B"),
        };

        public static readonly UiTheme BackAlley = new UiTheme
        {
            Name = "BackAlley",
            Base = H("#0D0B0A"),        // wet asphalt
            Deep = H("#080706"),
            Panel = H("#1A1614"),
            PanelDeep = H("#161210"),
            Well = H("#090807"),
            Inset = H("#241E1A"),
            RowA = H("#221C18"),
            RowB = H("#1C1714"),
            RowSelected = H("#3D2E25"),
            RowWon = H("#1F2619"),
            RowChampion = H("#2A1F2C"),
            RowPlayable = H("#2C2414"),
            GoldWash = H("#3A2F1C"),
            Unlock = H("#3A2C10"),

            Button = H("#2E2622"),      // dark iron; never neon
            ButtonOff = H("#1A1613"),
            TabOn = H("#4A3A30"),
            ButtonDone = H("#22381F"),
            Primary = H("#B8432F"),     // unchanged: the stenciled red plate
            Chip = H("#4F6470"),
            ImportChip = H("#C8622F"),
            Highlight = H("#FFC940"),
            Focus = new Color(0.88f, 0.69f, 0.44f, 0.6f),

            HoverMix = 0.10f,
            AccentHoverMix = 0.06f,
            RowHoverMix = 0.05f,
            HoverEdge = WithAlphaOf(H("#EDE6DA"), 0.22f),
            PressMix = 0.35f,
            PressScale = 0.97f,
            Disabled = new Color(0.57f, 0.58f, 0.58f, 1f), // #2E2622 -> about #1A1613

            Scrim = Color.black,
            Glass = H("#070605"),
            GlassSoft = H("#100D0B"),
            Divider = WithAlphaOf(H("#3FE0D0"), 0.55f), // neon cyan rule
            NoticeBar = H("#3A2438"),
            WarnBar = H("#4A3A18"),
            Brass = H("#B8862B"),
            Locked = H("#3A332E"),
            GoodStripe = H("#3FA56B"),
            PipOn = H("#2E5230"),
            Meter = H("#4CAF6A"),
            Placeholder = H("#2A201A"),

            Text = H("#EDE6DA"),
            TextBright = H("#E6DED1"),
            TextBody = H("#DDD5C8"),
            TextSoft = H("#D0C7B8"),
            TextMuted = H("#C2B8A8"),
            TextFaint = H("#B3A999"),
            TextQuiet = H("#A0968A"),
            TextDim = H("#8E8476"),
            Caption = WithAlphaOf(H("#EDE6DA"), 0.55f),
            OnAccent = H("#120F0D"),
            Quote = H("#F2E6B8"),
            Intro = H("#D9D2B0"),

            Sign = H("#FF4FA3"),        // neon pink name plates
            Gold = H("#FFC940"),        // currency: unchanged
            GoldSoft = H("#FFD86B"),
            LibraryGold = H("#E6C27A"),
            Good = H("#7BD88F"),
            Warn = H("#E3A21A"),
            Bad = H("#FF8A8A"),
            BadStrong = H("#D9433F"),
            BadWarm = H("#FF8A6A"),
            Info = H("#7FB2FF"),
            InfoSoft = H("#9FD8FF"),
            Violet = H("#B98CFF"),
            VioletSoft = H("#D7A6FF"),
            Import = H("#FF9E6B"),
        };

        /// <summary>The second neon (pink is <see cref="Sign"/> in the alley): for alley-only signage such as the van's sign. Never a button or the board.</summary>
        public static readonly Color NeonCyan = H("#3FE0D0");

        private static UiTheme _current = MainStreet;

        /// <summary>The theme the shell screens build with. Setting a different theme raises <see cref="Changed"/>; screens rebuild on their next Open / Refresh.</summary>
        public static UiTheme Current
        {
            get => _current;
            set
            {
                if (value == null || ReferenceEquals(value, _current)) return;
                _current = value;
                Changed?.Invoke();
            }
        }

        public static event Action? Changed;

        public bool IsAlley => ReferenceEquals(this, BackAlley);

        // ---------- Type ----------

        private static Font? _stencil;
        private static bool _stencilLooked;

        /// <summary>
        /// The alley's title font (docs/dev/ALLEY-SPEC-2026-11-01.md section 13): the owner's stencil TTF (OFL; any file in
        /// unity/Assets/Resources/Fonts/, which the build carries), or null until it's there. Main street has none.
        /// </summary>
        public Font? TitleFont
        {
            get
            {
                if (!IsAlley) return null;
                if (!_stencilLooked)
                {
                    _stencilLooked = true;
                    var fonts = Resources.LoadAll<Font>("Fonts");
                    _stencil = fonts.Length > 0 ? fonts[0] : null;
                }
                return _stencil;
            }
        }

        /// <summary>
        /// A title in this theme's title face (the map's node names, panel titles, the van's plate; never body text): in the
        /// alley the stencil font, or until it's supplied the default font in bold with +2 letter spacing. Main street: as is.
        /// </summary>
        public UnityEngine.UI.Text Title(UnityEngine.UI.Text text)
        {
            if (!IsAlley) return text;
            if (TitleFont is Font stencil) { text.font = stencil; return text; }
            text.fontStyle = FontStyle.Bold;
            if (text.GetComponent<LetterSpacing>() == null) text.gameObject.AddComponent<LetterSpacing>().Spacing = 2;
            return text;
        }

        private UiTheme() { }

        private static Color H(string hex) => Ui.Hex(hex);
        private static Color WithAlphaOf(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }

    /// <summary>
    /// Extra space between a one-line title's letters (the legacy Text has none of its own): each glyph's quad moves right
    /// by <see cref="Spacing"/> per glyph before it, and the line shifts back by its alignment so it stays centered or
    /// right-aligned.
    /// </summary>
    public sealed class LetterSpacing : UnityEngine.UI.BaseMeshEffect
    {
        public float Spacing = 2;
        private static readonly System.Collections.Generic.List<UIVertex> Stream = new System.Collections.Generic.List<UIVertex>();

        public override void ModifyMesh(UnityEngine.UI.VertexHelper vh)
        {
            if (!IsActive() || Spacing == 0) return;
            Stream.Clear();
            vh.GetUIVertexStream(Stream);
            int quads = Stream.Count / 6;
            if (quads < 2) return;
            float total = (quads - 1) * Spacing, shift = 0;
            if (GetComponent<UnityEngine.UI.Text>() is UnityEngine.UI.Text text)
            {
                int column = (int)text.alignment % 3; // left, center, right
                shift = column == 1 ? -total / 2 : column == 2 ? -total : 0;
            }
            for (int i = 0; i < Stream.Count; i++)
            {
                var v = Stream[i];
                v.position.x += (i / 6) * Spacing + shift;
                Stream[i] = v;
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(Stream);
        }
    }

    public static class ColorAlpha
    {
        /// <summary>The same color at alpha <paramref name="a"/> (clamped): <c>T.Text.WithAlpha(0.45f)</c>.</summary>
        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, Mathf.Clamp01(a));
    }
}
