# The type scale and the fonts (spec, 2026-11-21)

M2 and M3 of [PRODUCTION-REVIEW-UI-2026-11-20.md](PRODUCTION-REVIEW-UI-2026-11-20.md): a named type scale with a floor,
applied to every screen, and two open-licence faces, Barlow Condensed for display and Barlow for body. It also settles
the text that can't meet the floor: card faces, deck tiles, best-fit names. The 1280x720 measurements it answers are in
[EVIDENCE-2026-11-21b.md](EVIDENCE-2026-11-21b.md) ("Legibility at 1280x720") and the deck editor review's finding 3.
Line numbers are from commit 5a69c47 (0.3.0).

**Not in this build:** a 1280x720 layout (B2; decided: later), the editor's own redesigns (B1, B3), TextMeshPro (B3 of
the review), the alley stencil font (the owner's file; its hook stays and still wins in the alley), the results and pack
screens (M4, M5). No engine change; one small harness addition (the glyph corpus's freshness, section 7).

## What the player gets

Sizes are layout units at 1920x1080; px at 1280x720 is two thirds of that, at 3024x1964 about 1.58 times.

| Where | Today | After |
|---|---|---|
| Every screen | Unity's built-in Arial-like font, 40 sizes from 8 to 140 | Barlow for reading, Barlow Condensed for titles, buttons and chips; 8 named sizes; nothing under 14 (9.3 px at 720) |
| Screen titles, "Victory!", "VS", banners, turn and phase banners | Bold Arial | Barlow Condensed Bold |
| Buttons, tabs, chips, the campaign top bar | Arial 16-18 | Barlow Condensed SemiBold at Label (20): bigger and fits the same boxes |
| Circuit rows' second lines, Settings help, shop odds, bracket Elo lines, results strips | 9-10 px at 720 (6-7 px on the bracket) | At least 9.3 px |
| The board's card detail | About 8 px at 720 | Small (16): 10.7 px |
| DP and LP counters | Digits shift as they roll | Fixed-width digits |
| Plain cards at Standard size (pack, versus, spotlight, title wall) | A text box of about 5 px at 720 | Below 200 px wide on screen the text box goes: a bigger name, the type line and the glyph instead |
| Editor deck tiles at 720 (41 px wide) | Names about 6 px | A name-only face: the name at 7 px or more, cut with "…" past four lines |
| Field and hand names at 720 | 6-7 px | Barlow Condensed, 7.8 px or more |
| Symbols ★ ☆ ▲ ▼ ◆ ✦ → × · … ⊘ ⬢, accented names | Drawn by the built-in font | Drawn by Barlow or, where it has none, by a bundled symbol font; a check proves no missing-glyph box |
| About | Lists the game's licences | Also the fonts' licence (SIL OFL 1.1) |

## 1. The type scale

`UiTheme` gets the roles (one scale for both themes):

| Role | Size at 1080 | px at 720 | px at 3024x1964 | For | Face |
|---|---|---|---|---|---|
| Caption | 14 | 9.3 | 22 | Small caps captions ("YOUR DECK"), footers, help lines, zone labels, counts under things | Barlow SemiBold |
| Small | 16 | 10.7 | 25 | Second lines of rows, the status line, the duel log, the board's card detail | Barlow |
| Body | 18 | 12 | 28 | Reading text: dialogs, story, card text in the editor's detail and reading view, scouting | Barlow |
| Label | 20 | 13.3 | 32 | Buttons, tabs, chips, list row titles, settings values | Barlow Condensed SemiBold |
| Subhead | 22 | 14.7 | 35 | Panel titles, names in rows ("Match 1: Ryo, the Rookie") | Barlow SemiBold |
| Heading | 26 | 17.3 | 41 | Section and screen subtitles, deck names | Barlow Condensed SemiBold |
| Title | 34 | 22.7 | 54 | Screen titles ("CAMPAIGN", "SETTINGS"), menu items | Barlow Condensed Bold |
| Display | 60 | 40 | 95 | "Victory!", "VS", "Defeat", turn and phase banners | Barlow Condensed Bold |

- **The floor is 14 at 1080** (9.3 px at 720), as proposed. Confirmed against the measurements: today's unreadable
  text is 5-7 px, the stars at 10.7 px read, Settings help at 10 px reads with effort. 9.3 px in Barlow (larger x-height
  than the built-in font at the same size) is the smallest size that reads at 720. Captions use SemiBold for that reason.
- **Hero numbers are outside the scale** and keep their literal sizes: LP numbers, pile counts, the turn disc, the
  results' DP total, the title logo's text fallback. They are listed in `Typography.Hero` so the checks know them.
- **The mapping rule** for today's literals (`Ui.Text(..., size, ...)`, `fontSize =`, `resizeTextMaxSize`): the nearest
  role, ties go up, never below Caption:

  | Literal | Role | | Literal | Role |
  |---|---|---|---|---|
  | 8-14 | Caption 14 | | 21-23 | Subhead 22 |
  | 15-16 | Small 16 | | 24-29 | Heading 26 |
  | 17-18 | Body 18 | | 30-46 | Title 34 |
  | 19-20 | Label 20 | | 47 and up | Display 60, or Hero (listed) |

  A builder applies it mechanically, then checks the screen; where the rule makes a line overflow, the box grows or the
  text is shortened (section 5), never the role lowered below the rule.
- **Best fit:** `resizeTextMinSize` may not go below the role's floor (Caption 14 for shell text). Where text can't fit
  at the floor, it wraps, cuts with "…", or the box grows (section 5).
- **Settings > Card text size** (Normal, Large, Larger: 1, 1.2, 1.4 today, `FxSettings.TextScale`) scales the reading
  roles (Caption, Small, Body) **in reading areas only**: card text in the card detail and reading view, the duel log,
  prompt dialogs, story text, card-term tooltips. Everywhere else the scale is fixed, so layouts don't move. This is the
  same reach the setting has today (`FxSettings.Scaled`), now through `UiTheme.Size(role, reading: true)`.

**Order of screens** (part R2): campaign (header, circuit panel, guests, lessons, bounties, locker, library, ladder,
alley), results and versus (`CampaignScenes.cs`), the shop (shop, trader, grading desk, Van, structure decks, recipe
book), Settings (`MenuScreens.cs` settings rows and `SettingsSound.cs`: text only), the board (card detail, zone labels,
chain panel, callouts, history, prompts, banners, plates' small text), then the main menu, title, story, Free Duel, tag
setup, replays, About and Import Cards.

## 2. Text that can't meet the floor

Shell text never goes under the floor (section 1). **Text drawn on a card** (plain faces, the stat strip, badges on
tiles) scales with the card, so its rule is in **screen pixels**, computed as
`size in reference units × card width on screen ÷ reference width` (the reference widths are the plain card's: Compact 88,
Standard 200, Full 360). Card floors: **7 px** for names, chips and stats, **8 px** for card text. When an element can't
meet its floor it is dropped, in this order: card text, type line, chip words (a Level digit stays), the glyph disc.
**Names never drop:** they cut with "…".

| Face | On-screen width (w) | What it shows |
|---|---|---|
| Thumb | any | As today (no text) |
| Compact | w ≥ 52 px | As today, the name in Barlow Condensed SemiBold, max 17, min 12 (7 px at 52), up to 3 lines then "…"; chip "LV 4" (Condensed, 12; at w < 70 the subtype words go, `QUICK` and so on, and the chip shows only a number or nothing) |
| Compact, name-only | w < 52 px | The name plate fills 0.06-0.94 x 0.22-0.95 (badged: 0.06-0.94 x 0.20-0.76, the top band empty for the limit badge); the name at the size that makes 7 px on screen (`7 × 88 / w`, so 15 at 41 px), up to 4 lines then "…"; no window, chip or glyph; the footer stays |
| Standard | w ≥ 200 px | As today; card text min 9 (9 px at 200), cut with "…" |
| Standard, no text | w < 200 px | No text box. Name plate 0.05, 0.58, 0.95, 0.965: the name, max 26, min 16, up to 3 lines; type line 0.05, 0.52, 0.95, 0.57 (11) only when w ≥ 150 px; window 0.05, 0.20, 0.95, 0.50 with the glyph and the chip; footer as today |
| Full | any | Card text min `8 × 360 / w` (so 12 at 240 px, the reading view at 720), cut with "…"; the panel beside it shows the whole text |

At 1280x720 that makes: field and hand cards (57 px) Compact with 7.8 px names; editor results (59 px) Compact badged;
editor deck tiles (41 px) name-only badged, names 7 px; packs (115 px), versus (153 px) and the spotlight (150 px)
Standard without text; the reading view (240 px) Full with 8 px text. At 1920x1080 packs (172 px) are still without text;
at 3024x1964 they (271 px) show it. The variant is part of the plain cache's key (it already keys by width).

**The live stat strip** over Compact faces (CardView.cs `DrawArt`): min 7 px on screen; when "1800/1600" doesn't fit at
7 px it shows ATK only. **Editor badges** (limit, in-deck count) are UI text on the tile: Caption at 1080 is 9.3 px at
720 and they keep their 20x20 box (Caption Condensed fits two characters).

**Other best-fit text:**

| Today | After |
|---|---|
| Result tile names under the grid (11 shrinking to 10) | Gone: the face carries the name (Compact badged) |
| Chips and pickers in the editor (12-13 shrinking to 9) | Label (Condensed 20 at 1080; 14 at minimum by best fit): wider chips where they fit, shorter words where they don't (section 5) |
| The editor's help lines (13) | Caption 14; the four lines become three (keys line shortened) |
| Status lines (editor, campaign footer) | Small 16, one line, cut with "…"; the full text in its tooltip |
| Map node labels, bracket lines | Caption 14 min; bracket second lines may drop the Elo at 720 ("#4 · Elo 1,414" becomes "#4") |
| Chain panel lines, callout lines | Small 16, two lines, then "…" |

## 3. The fonts

**Files** (five TTFs and one symbol fallback; static TTFs, not variable fonts):

| File | Face | Used for |
|---|---|---|
| `Barlow-Regular.ttf` | Barlow Regular | Small, Body |
| `Barlow-Italic.ttf` | Barlow Italic | Quotes, flavour text (today's 27 italic texts) |
| `Barlow-SemiBold.ttf` | Barlow SemiBold | Caption, Subhead, and every "bold" body text (today's `FontStyle.Bold`) |
| `BarlowCondensed-SemiBold.ttf` | Barlow Condensed SemiBold | Label, Heading, Compact card names and chips, the stat strip |
| `BarlowCondensed-Bold.ttf` | Barlow Condensed Bold | Title, Display, banners |
| `NotoSansSymbols2-Regular.ttf` | Noto Sans Symbols 2 | Fallback for symbols Barlow lacks (owner decision N1) |

No bold italic: the few bold quotes become SemiBold upright.

**Where they come from** (fetched at build time, after the owner has seen this list; pin the commit and record each
file's SHA-256 in THIRD_PARTY_NOTICES.md):

- Barlow: github.com/google/fonts, `ofl/barlow/` (the three Barlow files and `OFL.txt`) and `ofl/barlowcondensed/` (the
  two condensed files). Upstream project: github.com/jpt/barlow (Jeremy Tribby), "Copyright 2017 The Barlow Project
  Authors".
- Noto Sans Symbols 2: github.com/google/fonts, `ofl/notosanssymbols2/` (`NotoSansSymbols2-Regular.ttf`, `OFL.txt`).
  Upstream: github.com/notofonts/symbols, "Copyright 2022 The Noto Project Authors".
- Read each `OFL.txt` before adding: if it names a Reserved Font Name, we ship the files unmodified under that name
  (we don't subset or rename them, so the RFN clause isn't triggered).

**Where they go:** `unity/Assets/Resources/GameFonts/` (Unity loads runtime fonts only from `Resources`). **Not**
`Resources/Fonts/`: `UiTheme.TitleFont` takes the first font found there as the alley stencil (UiTheme.cs:411).

**Import settings** (in each `.ttf.meta`, committed): Character: Dynamic; Include Font Data on; Rendering Mode Smooth;
Font Size irrelevant (dynamic). Barlow and Barlow Condensed: their **font references** (fallbacks in the project) name
`NotoSansSymbols2-Regular`, then the **font names** (fallbacks on the system) `Helvetica Neue, Arial, Segoe UI Symbol,
Arial Unicode MS`.

**Legacy `Text` and weights.** A dynamic font asset is one face; `FontStyle.Bold` on Barlow Regular would be a faux
bold. So weights are separate font assets chosen in code: `Typography.Strong(text)` sets Barlow SemiBold (or Barlow
Condensed Bold on a condensed text), `Typography.Italic(text)` sets Barlow Italic, and `fontStyle` stays Normal. Part R1
replaces today's 239 `fontStyle = FontStyle.Bold` and 27 `FontStyle.Italic` lines with these calls (mechanical). Rich
text `<b>` inside strings stays faux bold (it can't pick a font); a check lists strings that use it (section 7), and
the common ones (results lines, shop lines) move to separate texts or lose the tag.

**Figures.** Legacy Text can't switch on OpenType features, so tabular figures come from a mesh effect,
`TabularDigits`, that gives each digit the advance of "0" and centres it. It goes on every counter that changes while
seen: LP numbers, the LP change floaters, DP in headers and results, pile counts, the turn disc, the results' EXP, the
shop's prices while they update. Static numbers keep Barlow's own figures.

**Wiring:**

- Body: `Ui.Font` (Ui.cs:12) returns Barlow Regular. Every `Ui.Text`, `Ui.Input` and hand-built `Text` (Ui.cs:53, 144,
  154; PlainCards.cs:610) already takes `Ui.Font`, so one change moves the whole game.
- Display: `UiTheme.Title(text)` (UiTheme.cs:422-429) applies Barlow Condensed Bold on Main Street too; in the alley the
  stencil wins when present, else Barlow Condensed Bold with today's +2 letter spacing. Screen titles, banners,
  "Victory!" and "VS" call `Title` (11 calls today; R1 adds the rest, listed by the check in section 7).
- `Ui.Text` gains a role overload (below); the int overload stays during the migration and maps through `RoleFor`.
- The plain card studio (`PlainCards.cs`) takes its fonts from `Typography`; `CardFace.Invalidate()` after a font change.
- **Atlas.** Each font asset has its own dynamic atlas. More faces means more atlases, not bigger ones. The studio
  already requests characters per text's font before drawing (PlainCards.cs:285-295); keep it per font.

**Glyph coverage.** Barlow covers Latin, Latin Extended and common punctuation (× · … are expected; → ★ ☆ ▲ ▼ ◆ ✦ ⊘ ⬢
are not). Every character must be drawn by some font:

- `tools/glyph_corpus.py` (new) writes `unity/Assets/Resources/glyph-corpus.txt`: every non-ASCII character in string
  literals under `unity/Assets/Scripts/` and in `campaign/` and `personalities/`, one per line with one file it came
  from. Rerun when strings change (the check fails on a stale corpus: section 7).
- At run time the check adds every character of every card name and text in the card data.
- For each face, each character: `RequestCharactersInTexture` then `GetCharacterInfo` must succeed with a width above 0
  (the fallback chain included). A character no font draws fails the check, with where it came from.
- If the fallback chain doesn't work in legacy `Text` on a platform (R1 tries it first, below), plan B: `Ui.Text` gives a
  text whose string holds a character Barlow lacks the built-in font (`Typography.Builtin`) for that text only.

## 4. Licence and notices

- **How it fits:** the fonts are under the SIL Open Font License 1.1, which allows bundling them with any software,
  free or not, as long as they keep their licence and copyright and aren't sold alone. They stay under the OFL; our code
  stays AGPL-3.0 and our content CC BY-SA 4.0. Bundling doesn't change either.
- **Files:** `LICENSES/OFL-1.1-Barlow.txt` (Barlow's `OFL.txt` word for word) and `LICENSES/OFL-1.1-Noto.txt`
  (Noto's). Copied into builds by `ProjectSetup.cs` with the other licence texts (lines 212-233).
- **THIRD_PARTY_NOTICES.md:** two summary rows and two sections:

  | Component | What it is | Version (pinned commit) | License | Changed? | Ships in the game? |
  |---|---|---|---|---|---|
  | Barlow | The game's fonts (Barlow, Barlow Condensed) | google/fonts `<commit>` | SIL OFL 1.1 | No | Yes, 5 font files |
  | Noto Sans Symbols 2 | Fallback font for symbols | google/fonts `<commit>` | SIL OFL 1.1 | No | Yes, 1 font file |

  Each section: the copyright line, the upstream, the files with their SHA-256, "unmodified", and the licence file.
- **LICENSING.md:** one row: "Fonts | `unity/Assets/Resources/GameFonts/` | SIL OFL 1.1, their own terms |
  `LICENSES/OFL-1.1-Barlow.txt`, `LICENSES/OFL-1.1-Noto.txt`", and the licence-texts row names them.
- **About:** credits gain "Fonts: Barlow by Jeremy Tribby, Noto Sans Symbols 2 by the Noto Project (SIL Open Font
  License)." `LicenseFiles` gains `Doc.Fonts` (both OFL files, shown one after the other in the reader) and its online
  URL; the About screen gets a "Font licences" button or the third-party reader shows them after the notices (owner
  decision N3).

## 5. Layout fallout

- **Finding it:** the layout rule in section 7 (no text under the floor, none clipped) runs on every screen check at the
  three sizes; each part fixes what its screens report. "Clipped": a `Text` whose preferred height is more than its box
  (vertical truncate), a single word wider than its box, or best fit at its minimum and still over.
- **Fixing it, in this order:** let the box grow into free space; shorten the words (one shorter wording, recorded in
  the part's report); wrap to a second line where the row has room; cut with "…" and put the full text in a tooltip.
  Never lower a role below the mapping.
- **Expected hand work:** the editor's toolbar and chip rows; the campaign header (eight top-bar buttons, the DP and
  level block); Settings rows (name at Heading, help at Caption, values at Label); the versus panels; the Recipe Book
  lines (The climb's two-line line); the chain panel and callouts; the bracket; the shop's odds block; results strips.
- **Must not move:** the deck editor's buttons, tiles and result cells keep their rects (`-focuscheck` and
  `-editorcheck` click by position); the Settings sound block (`SettingsSound.cs`) keeps its slider rects; the board's
  zones and the hand's positions; map node positions. Text changes inside those rects only.
- Barlow Condensed is narrower than today's font, so buttons and chips gain room; Barlow body is about as wide as
  today's font, so rows that grow by the mapping (13 to 14, 15 to 16) are where overflows come from.

## 6. Parts

One Unity at a time, so the parts run in sequence. Each ends in a build that passes its checks; the owner can stop
after any of them. All Unity Architect (Opus).

**R1: fonts and the scale, wired, sizes unchanged (about 2 agent-days).** The faces change everywhere; no size changes
except where the new face makes a line overflow.

| File | Change |
|---|---|
| `unity/Assets/Resources/GameFonts/*.ttf` (+ `.meta`) | The six files, imported as in section 3 |
| `unity/Assets/Scripts/Typography.cs` (new) | Fonts, roles' faces, `Strong`, `Italic`, `TabularDigits`, coverage |
| `unity/Assets/Scripts/UiTheme.cs` | `TextRole`, `Size`, `RoleFor`, `Floor`; `Title` (422-429) on Main Street; `TitleFont` (403-416) unchanged |
| `unity/Assets/Scripts/Ui.cs` | `Font` (12) to Barlow; the role overload of `Text` |
| Every script with `FontStyle.Bold` / `Italic` | Replaced by `Typography.Strong` / `Italic` (mechanical; no other change in those files) |
| `PlainCards.cs` | Fonts from `Typography` (no layout change in R1) |
| `LicenseFiles.cs`, `AboutScreen.cs`, `unity/Assets/Editor/ProjectSetup.cs` (212-233) | Section 4 |
| `LICENSES/OFL-1.1-Barlow.txt`, `LICENSES/OFL-1.1-Noto.txt`, `THIRD_PARTY_NOTICES.md`, `LICENSING.md` | Section 4 |
| `tools/glyph_corpus.py` (new), `unity/Assets/Resources/glyph-corpus.txt` (new) | Section 3 |
| `src/Ygo.Harness/TextsCheck.cs` | The corpus freshness test (section 7, step 3); harness build in `build/bin/type` |
| `DuelController.TypeCheck.cs` (new), `DuelController.cs` (the start line and the `checks` list, about 705-716) | `-typecheck` |

R1 starts with a half-day spike before the mechanical work: Barlow imported, one screen and the plain card drawn,
the fallback chain tried on ★ and ⬢ on the Mac, `<b>` looked at. If the project-font fallback doesn't draw them, plan B
(section 3) is built instead.

**R2: the scale on the screens (about 3.5 agent-days).** In the section 1 order: every literal size mapped, the layout
rule's allowlist shrinking to the editor and the plain card. Owns the screen scripts (`CampaignScreen*.cs`,
`CampaignScenes.cs`, `ShopScreen.cs`, `TraderScreen.cs`, `GradingDeskScreen.cs`, `VanScreen.cs`, `StructureScreen.cs`,
`RecipeBookScreen*.cs`, `BracketScreen.cs`, `MenuScreens.cs`, `SettingsSound.cs` (text only), `StoryScreen.cs`,
`TagSetupScreen.cs`, `ReplayBrowser.cs`, `AboutScreen.cs`, `ImportScreen.cs`, `CardDetail.cs`, `BoardView*.cs`,
`PromptDialogs.cs`, `PromptPanel.cs`, `TermHelp.cs`, `PosterView.cs`, `SlabView.cs` (label text), `BoardFx.cs` (banner
text only)), plus the floor and clip steps in the screen checks (`DuelController.cs` `LayoutCheck` and `SmallTexts`,
5050-5440; `DuelController.Shop.cs`, `ThemeCheck.cs`, `FaceAudit.cs`).

**R3: the plain card and the editor (about 2.5 agent-days).** Section 2's pixel rules in `PlainCards.cs` and
`CardFace.cs` (the variants and their cache key), `CardView.cs` (the stat strip rule), and the editor's text
(`DeckEditor*.cs`: help lines, chips, status, badges, the result names under tiles go); `DuelController.PlainCheck.cs`,
`SearchCheck.cs`, `FocusCheck.cs`, `EditorCheck.cs` steps. The editor review's M1 is done by this part.

After R3: Code Reviewer (Opus) on the three diffs against this spec, one fix round, Evidence Collector (Opus), Technical
Writer (Sonnet) for PLAYING.md (Settings > Card text size), DEVELOPING.md (`-typecheck`, the layout rule), TODO.md.
About 1.5 agent-days. **Total about 9.5 agent-days** (R1 2, R2 3.5, R3 2.5, after 1.5).

### Interfaces (R1; R2 and R3 code against them)

```csharp
namespace YgoCampaign.Game
{
    // UiTheme.cs
    public enum TextRole { Caption, Small, Body, Label, Subhead, Heading, Title, Display }
    // Added to UiTheme (static members):
    {
        public const int Floor = 14;
        /// <summary>The role's size at 1080; reading roles times FxSettings.TextScale when <paramref name="reading"/>.</summary>
        public static int Size(TextRole role, bool reading = false);
        /// <summary>The mapping rule for a literal size (section 1).</summary>
        public static TextRole RoleFor(int literal);
    }

    // Typography.cs
    public enum Face { Body, Italic, Strong, Condensed, CondensedBold }
    public static class Typography
    {
        public static Font Font(Face face);
        public static Face FaceOf(TextRole role);                       // section 1's Face column
        public static Font Builtin { get; }                             // Unity's LegacyRuntime.ttf
        public static Text Strong(Text text);                           // the heavier face of the text's family
        public static Text Italic(Text text);
        public static Text Counter(Text text);                          // adds TabularDigits
        public static readonly IReadOnlyCollection<string> Hero;        // names of texts outside the scale
        /// <summary>Characters of <paramref name="s"/> no font in <paramref name="face"/>'s chain draws.</summary>
        public static IReadOnlyList<char> Missing(string s, Face face);
    }
    public sealed class TabularDigits : BaseMeshEffect { }

    // Ui.cs
    // Added to Ui:
    {
        public static Text Text(RectTransform rt, string text, TextRole role, Color color,
            TextAnchor anchor = TextAnchor.MiddleCenter, bool reading = false);
    }
}

// R3, CardFace.cs: the variant a face gets at a width, for checks.
public enum FaceVariant { Thumb, Compact, CompactNameOnly, Standard, StandardNoText, Full }
public static FaceVariant VariantFor(CardLayout layout, float screenWidthPx);
/// <summary>Each text on a drawn plain face with its on-screen size in px at the width it's drawn for.</summary>
public static IReadOnlyList<(string element, string text, float px, bool cut)> DebugTexts(Texture texture);
```

## 7. Checks

All runs from a fresh check copy, one at a time, fresh `-mods`, absolute paths, `-mute`, at 1280x720, 1920x1080 and
3024x1964 (`-screen-width 3024 -screen-height 1964`), each **without pictures** (`-pics <empty folder> -hdpics off`)
and, where a step says so, **with** (this Mac finds the owner's pictures by itself). Each step fails on today's code.

**`-typecheck <folder>` (new, R1):**

1. `Ui.Font` is Barlow Regular; each `Face` loads; the alley's stencil lookup still finds no font (Resources/Fonts empty).
2. Coverage: every character of `glyph-corpus.txt` and of every card name and text in the card data is drawn by each
   face's chain (section 3); a sheet of the symbols in each face is shot (`type-symbols-<face>.png`).
3. The corpus is current: `glyph_corpus.py` writes a hash of its sources into the corpus file, and `--texts-check` in
   the harness fails when the hash is stale (a small harness addition in R1, the only `src/` change of this build).
4. `Strong` picks SemiBold, `Italic` picks Italic, and no active text has `fontStyle` Bold or Italic.
5. `TabularDigits`: "1111" and "8888" in a counter text are the same width; an LP roll's digits don't move sideways.
6. Strings with `<b>` are listed (a note, not a FAIL, until R2 ends; then a FAIL outside an allowlist).
7. About lists the font licences, and `LicenseFiles.Find(Doc.Fonts)` finds both files (also in `-aboutcheck`).

**The layout rule (R2, extended in R3)** at every shot of `-layoutcheck`, `-menucheck`, `-campaigncheck`,
`-shopcheck`, `-themecheck`, `-editorcheck`, `-searchcheck`, `-focuscheck`, `-alleycheck`: today's `SmallTexts`
note (DuelController.cs:5405) becomes a FAIL for any visible shell text under `UiTheme.Floor` (best fit at the size it
came out), except `Typography.Hero` and an allowlist that each part shrinks (empty at the end of R3); and a FAIL for
any clipped text (section 5). Also run at Settings > Card text size Larger (`-textsize 2`) at 1280x720.

**`-plaincheck` (R3):** for widths 30, 41, 52, 57, 70, 115, 150, 172, 200, 240, 271 px, each kind: `VariantFor` gives
section 2's variant; every `DebugTexts` entry is at least 7 px (8 px for card text) or marked cut; names are never
dropped; a long name (Number 38: Hope Harbinger Dragon Titanic Galaxy) at 41 px ends in "…" within four lines; the
cache keys the variant (same card, 199 and 201 px, two textures). With pictures: pictured faces unchanged (the plain
spec's step 5 still passes).

**Other checks that must still pass:** `-focuscheck`, `-editorcheck`, `-clickcheck` (nothing clickable moved),
`-stylecheck`, `-importcheck` (without `-hdpics off`), `-aboutcheck`, `-replaycheck`, `-fxshots` (banners in the display
face), with and without pictures at 1280x720; `--texts-check` from a harness build.

## Traps

- **`Resources/Fonts/` is the alley stencil's folder** (UiTheme.cs:411 takes the first font there): the game fonts go in
  `Resources/GameFonts/`.
- **Faux bold:** `FontStyle.Bold` on a Barlow asset thickens strokes and widens text unpredictably; after R1 no code
  sets it. Rich text `<b>` still does.
- **The fallback chain in legacy Text** is per font asset and per platform; prove it on the Mac in the spike and on
  Windows before release (the owner's PC; not verifiable here).
- **Line height:** Barlow's ascent and descent differ from the built-in font's; boxes sized to the old line height clip
  descenders at the same size. The clip rule catches them.
- **Best fit with a new face** comes out at different sizes; checks that read `fontSizeUsedForBestFit` see new numbers.
- **The plain cache** must be invalidated when fonts change, and its key gains the variant (R3).
- **Measured widths in code** (`CardView.TextWidth`, `FitWords`, `ImportScreen.ShortPath`, `SlabView.Fit`) use the
  text's font: correct automatically, but their thresholds (e.g. `MinWordSize = 10`) were tuned for the old face; R3
  re-checks the 13 hyphenation cases in `-layoutcheck`.
- **The mechanical Bold/Italic pass touches most scripts:** run it when no other build is editing Unity scripts.
- **Old saves** are untouched; `textSize` keeps its values and meaning.

## Verification

From a fresh check copy (`tools/qa_copy.sh build/qa-type`), after each part: that part's checks in section 7 at the
three sizes, without pictures, plus `-menucheck`, `-campaigncheck` and `-layoutcheck` with pictures at 1280x720. Every
screenshot looked at side by side with the 0.3.0 shots (`build/qa-final/checks/`, `build/qa-evidence3/checks/`). From
a harness build: `--texts-check`, `--campaign-test`. No DP change: no `--arbitrage`.

## Owner decisions

Decided (owner, 2026-11-21): **Barlow Condensed for display and Barlow for body.** Building the rest as recommended
unless you say otherwise:

Decided (owner, 2026-11-21): the font pair is Barlow Condensed for display and Barlow for body; N1 to N5 as recommended;
**build R1 now, then decide on R2 and R3**; the six font files are approved for download from github.com/google/fonts.

- **N1. Symbols Barlow lacks.** *Recommended:* bundle Noto Sans Symbols 2 (one more OFL file) as the fallback, so every
  platform draws them the same. *Or:* rely on the system's fonts (Helvetica Neue, Segoe UI Symbol...) and replace any
  symbol a check finds missing with a drawn icon.
- **N2. The floor.** *Recommended:* 14 at 1080 (9.3 px at 720) for all shell text; card faces 7 px (names, chips,
  stats) and 8 px (card text) on screen. *Or:* 15 (10 px at 720), with more hand work in the editor and the bracket.
- **N3. Font licences in About.** *Recommended:* shown in the third-party reader after the notices, no new button.
  *Or:* a "Font licences" button.
- **N4. Plain cards without text below 200 px.** *Recommended:* yes (packs and versus show name, type line and glyph at
  720 and 1080; their text is one click away). *Or:* keep the text box and accept 5 px text at 720.
- **N5. Card text size Larger** scales reading text only (as today). *Recommended:* yes. *Or:* scale the whole scale,
  which needs a 720 layout first.

## Not verified

- **No Barlow on screen yet:** widths, line heights and the 9.3 px floor's legibility in Barlow are judged from the face's
  published metrics and today's 1280x720 shots, not from stills. The R1 spike produces them.
- **Which symbols Barlow lacks:** expected from its character set, not checked (nothing was downloaded).
- **Whether legacy `Text` uses a project font as fallback on macOS and Windows:** the spike's first question.
- **No new stills were made** (the game wasn't run for this spec). The stills that would help, from a check copy:
  `APP="$PWD/build/qa-type/Duel Circuit.app/Contents/MacOS/Duel Circuit"; OUT="$PWD/build/qa-type/checks/layout-720"; rm -rf "$OUT"; mkdir -p "$OUT/mods" "$OUT/out" && "$APP" -mods "$OUT/mods" -layoutcheck "$OUT/out" -mute -hdpics off -pics "$PWD/build/qa-type/nopics" -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile "$OUT/log.txt"`,
  then the same with `-plaincheck` and at 3024x1964.
- **The exact pixel widths** quoted for 720 (57, 41, 115, 153, 150, 240 px) come from the evidence pass and the layout
  constants, not from a new measurement.

## As built: R1 (2026-11-21)

R1 is built from the code in the working tree (uncommitted). R2 (the scale applied screen by screen) and R3 (card and
editor text) are **not built**. Models: this spec by a UI Designer (Opus); R1 by a Unity Architect (Opus); reviewed by a
Code Reviewer (Opus); one fix round. There was no separate evidence pass for R1 (usage was tight): the builder reran
every check on one final build and the session reran `-typecheck` and `-themecheck` itself.

### What exists

- **Six font files** in `unity/Assets/Resources/GameFonts/`: Barlow Regular, Italic and SemiBold; Barlow Condensed
  SemiBold and Bold; Noto Sans Symbols 2. SIL OFL 1.1, unmodified, from github.com/google/fonts at commit
  `06bfe0d62a98375cb09c942fa18e68bd32cff057`. Checksums are in `THIRD_PARTY_NOTICES.md` (the writer checked all six
  against the files on disk). The licence texts are `LICENSES/OFL-1.1-Barlow.txt` and `LICENSES/OFL-1.1-Noto.txt`,
  copied into every build's `licenses` folder by `ProjectSetup.CopyLicenses`, and shown after the notices in About's
  third-party reader (heading `FONT LICENCES (SIL OPEN FONT LICENSE 1.1)`), with a credit line in About's credits.
  `LICENSING.md` lists the fonts. `*.ttf binary` is in `.gitattributes`.
- **`Typography.cs`:** `Face` (Body, Italic, Strong, Condensed, CondensedBold), `Typography.Font`, `Has`, `Symbols`,
  `FaceOf(TextRole)`, `Strong`, `Italic`, `Weight`, `Counter`, `Hero`, `IsHero`, `Missing`, and the components
  `TabularDigits`, `FaceMemory` and `HeroNumber`. `Ui.Font` is Barlow Regular. `Ui.Text` has a role overload
  (`TextRole`, optional `reading`). Buttons made by `Ui.Button` and its siblings use Barlow Condensed SemiBold.
  `UiTheme.Current.Title` uses Barlow Condensed Bold (the alley's stencil when it's there, else with +2 letter spacing).
- **Each weight is its own font.** No text sets `FontStyle.Bold` or `Italic` on the checked screens; `Strong`, `Italic`
  and `Weight` replace them. If a file is missing from a build, the face draws in the built-in font (one warning) and
  uses its own bold and italic.
- **Symbols.** Each face's import settings list Noto Sans Symbols 2 as its fallback font, then named system fonts: Helvetica
  Neue, Arial, Segoe UI Symbol, Arial Unicode MS, Segoe UI, Yu Gothic, Meiryo, Microsoft YaHei, MS Gothic, Hiragino Sans,
  PingFang SC. This is the spec's plan A; it was found to work in a built Mac app. Windows is untested.
- **Fixed-width digits** (`TabularDigits`, set by `Typography.Counter`) on the LP numbers, pile counts, the turn disc,
  DP headers, the results' totals and EXP, and shop prices. Each digit gets a cell as wide as the widest digit at its own
  size.
- **The type scale is defined, not applied.** `TextRole` (Caption, Small, Body, Label, Subhead, Heading, Title, Display),
  `UiTheme.Floor` (14), `UiTheme.Size` and `UiTheme.RoleFor` exist; no text size changed in R1. The layout changes are the
  About screen's credits box (taller, for the font credit) and the van's card names (now their own bold text).
- **`-typecheck <folder>`** (`DuelController.TypeCheck.cs`): see the App checks table in `docs/DEVELOPING.md`. The builder
  reports 57 lines at 1280x720, 1920x1080 and 3024x1964; the writer did not run it.
- **The glyph corpus.** `tools/glyph_corpus.py` writes `unity/Assets/Resources/glyph-corpus.txt` (`--check` writes nothing
  and exits 1 when stale). `harness --texts-check` recomputes it and fails when it is stale (`TextsCheck.GlyphCorpus`).
- **`-aboutcheck`** also checks that the font licences are in the app's licence folder and that About shows the credit.
- **`-themecheck`'s story crossfade step** (`DuelController.BackdropCheck.cs`) now measures the fade in the fade's own clock
  (each frame's unscaled time capped at 1/30 s), with a floor of `FadeSeconds - 1/30 - 0.001` (0.566 s) and at least 17
  frames, plus an upper bound of 1.5 s on the wall clock. It used to compare wall-clock time with 0.54 s and failed now and
  then on fast machines. The fade itself is unchanged.
- **Other small changes:** the pin badge is now "●" (legacy `Text` can't draw the emoji); a Normal Spell or Trap plain card
  no longer shows a lit empty chip (`PlainCards.HasChip`).

### Where the build differs from this spec

- **Strong text at size 30 and up** takes Barlow Condensed Bold (the Title and Display roles by `UiTheme.RoleFor`), as well as
  condensed text. Other strong text takes Barlow SemiBold.
- **Buttons** use Barlow Condensed SemiBold at their current literal sizes (the scale's Label size is not applied yet).
- **The plain `Ui.Text` overload** (a literal size, no role) stays Barlow Regular at that size.
- **Hero numbers are marked in code** (`Typography.Hero(Text)`, `Typography.IsHero`) and not by GameObject name.
- **Additions beyond the spec's interfaces:** `Typography.Weight`, `Has`, `FaceMemory`, `HeroNumber`;
  `TabularDigits.AsDrawn`, `PrepareNow`, `PreferredWidth`; `PlainCards.HasChip`; `LicenseFiles.Doc.Fonts` and
  `LicenseFiles.FindAll`.

### Not verified

Windows (run `-typecheck` on the PC); a real missing font file (the fallback to the built-in font); a real 4K screen; that
the wallet step would fail without its fix; audio is never heard. The writer ran no check, build or game.

### Still open

R2 and R3 (owner's go-ahead). About 100 script lines still use rich-text `<b>` / `<i>`. Both are in `docs/dev/TODO.md`,
"Type scale and fonts".
