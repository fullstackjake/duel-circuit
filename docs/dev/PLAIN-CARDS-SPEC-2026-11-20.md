# Plain cards and first impressions (spec, 2026-11-20)

The first build from [PRODUCTION-REVIEW-UI-2026-11-20.md](PRODUCTION-REVIEW-UI-2026-11-20.md): its Q1-Q5, Q10 and M1.
The game ships no card pictures, so most players see every card without one. Today that card is a fallback: a flat
colour slab with a small name, drawn a different way on each screen, and blank on some. This build gives it a designed
face, the **plain card**, drawn in code by one renderer that every screen calls, so a card is never blank and rarity,
foil and grade show on it. Pictures stay an upgrade: where the player has one, it wins, and it looks as it does today.
Line numbers are from commit 3b36438.

**Not in this build:** text sizes and fonts (M2, M3: the next spec), the pack wrapper and results (M4, M5), the player
crest (M6), theming the remaining screens (M7), blending the mats (M8). No engine or harness change: everything is in
`unity/` (and two docs).

## What the player gets

Without pictures (the shipped game):

| Screen | Today | After |
|---|---|---|
| Title screen | A wall of brown blank rectangles (MenuScreens.cs:90-99) | A wall of plain cards drifting, a third of them foiled, as with pictures |
| Main menu | The right half empty: the showcase isn't drawn without a picture (MenuScreens.cs:404-405) | Your rarest card (or a starter's, before a campaign) as a plain card, foiled and tilting |
| Duel board, hand, piles | Flat frame colour, an 8 px name at the top, a stat line | A plain card: name plate, attribute glyph, a Level/Rank/Link chip, a pattern, stats; foil by rarity |
| Spotlight (a card activating), summon cut-in | A 150x205 px slab with an 8 px name; the cut-in has no art | A plain card at that size, with its type line and effect text; the cut-in shows its window |
| Chain panel, callouts, history strip | Empty dark squares or a flat colour | Thumb plain cards: frame colour and the attribute or Spell/Trap glyph |
| Pack reveal | Nine flat slabs; an Ultra Rare looks like a Common | Plain cards with foil per rarity and a rarity mark (UR, ScR...) |
| Versus, Free Duel | Black boxes with a white name (CampaignScenes.cs:612) | Plain cards, foiled |
| Card detail, reading view | A solid colour block (240x350 px in the reading view) | The plain card, foiled, with its text |
| Slabs (grading desk, Van, slab reveal) | A solid colour card with no name (SlabView.cs:104) | The plain card in the slab, with its grade's wear |
| Locker posters, the duel plate | A big letter on a colour ("S") | The plain card's window (pattern and glyph) on the poster |
| Story (a speaker without a portrait), Trader, Structure Decks | Nothing, or a blank | The plain card |
| Deck editor tiles | Text look | Plain cards laid out round the limit badge and the in-deck count |

With pictures: nothing changes where a picture exists. A deck with pictures for some cards shows those pictures and
plain cards for the rest, side by side, with the same size, corners, border and stat strip (see "Mixed").

Docs: CARD-PICTURES.md stops promising "a clean card frame" with text it didn't show, and describes the plain card.

## The plain card

### What it's made of

A plain card is a **texture**, generated once per card, layout and resolution, and cached. Being a texture is what
lets every screen treat it like a picture: `CardFoil` (foil, rounded corners, gloss, wear), slabs, the shatter, the
title wall, posters and cut-ins all already work on a `RawImage` with a card-shaped texture. It is drawn by laying out
ordinary uGUI elements (the game's `Ui.Font`, `CardView.FitWords`) on a hidden canvas and rendering them into a
`RenderTexture` with a hidden camera, then copying into a mipmapped `Texture2D` (see "How it's drawn").

It must not reproduce the official card frame. What it takes from the card game is only the colour code by kind
(owner decision N1). Everything else is ours and laid out differently from the official cards: no beige text box (the
text sits on a dark plate), no row of stars (a chip with a number), no round attribute icon with a symbol in it (our own
glyphs, below), no Link arrows on the frame (a small 3x3 grid), no set code or passcode line, no edition marks.

### Colours

`K` is the kind colour, `CardView.FrameColor(type)` as today:

| Kind | K | Ink on K |
|---|---|---|
| Normal monster | `#D9B85C` | dark |
| Effect monster | `#D07A35` | dark |
| Ritual | `#5B8DD9` | dark |
| Fusion | `#8853B0` | light |
| Synchro | `#E4E4E4` | dark |
| Xyz | `#262626` | light |
| Link | `#245FAF` | light |
| Token | `#9A9A9A` | dark |
| Spell | `#1D9C73` | light |
| Trap | `#B0457F` | light |
| Unknown card (no data) | `#6B3F1F` | light |

- **Ink:** dark `#1B1408`, light `#F4F1EA`; which one is `CardView.TextColor`'s rule (light for Xyz, Link, Spell, Trap,
  Fusion), plus unknown cards.
- **Rim:** `Lerp(K, white, 0.35)`, a line 0.02 W wide, inset 0.03 W from the edge (W = card width, H = card height).
- **Plate:** `Lerp(K, #10131A, t)` with `t = 0.68` when K's luma > 0.6, else `0.5`. Plates always carry light ink, so
  every name is light on dark (the foil shader's `_Ink = 1` for every plain card). Light ink on every plate must be at
  least 4.5:1 (a test in `-plaincheck`).
- **Window ground:** `Lerp(K, #10131A, 0.25)`; its pattern in `Lerp(K, white, 0.2)` at 50% alpha.
- **Pendulum:** K runs into the Spell green over the lower 40% of the card (a vertical gradient on the base and rim
  only; plates and the window keep the monster's K).
- **Attribute colours** (the glyph disc behind the glyph at Compact size) are CardDetail's existing ones (EARTH
  `#8A6A3A`, WATER `#2E7FD0`, FIRE `#D0402E`, WIND `#3FA04A`, LIGHT `#D9B52E`, DARK `#7B3FA0`, DIVINE `#C9A227`); Spell
  and Trap discs use K.

### Glyphs (new, in `Icons`, drawn as shapes like the existing icons)

| Key | Shape |
|---|---|
| `attr-light` | A disc with eight short rays |
| `attr-dark` | A crescent opening to the right |
| `attr-fire` | A flame of three tongues |
| `attr-water` | Two stacked wave crests |
| `attr-earth` | Two peaks, the left one taller |
| `attr-wind` | Three horizontal swoosh strokes of different lengths |
| `attr-divine` | A rhombus inside a thin ring |
| `kind-spell` | A circle with three arcs fanning out from its upper right |
| `kind-trap` | A square with two diagonal bars across it |

Monsters show their attribute glyph; Spells and Traps their kind glyph; unknown cards none.

### Patterns

The window's pattern is picked by `code % 6`: concentric rings, diagonal bands, nested chevrons, a dot grid, rays from a
corner, horizontal waves. Drawn by code into small tiling textures (one per pattern, 64x64, cached). They make two
plain cards of the same kind tell apart at a glance and give foil something to play on.

### Layouts

A plain card has one of four layouts, picked by its size **in layout units** (the 1920x1080 reference), not by screen
pixels, so a card shows the same things at every window size. Positions are fractions of the card: x from the left,
y from the bottom. The studio lays each layout out at its **reference size** in layout units, so `FitWords` and text
sizes keep today's meaning; the texture's resolution is separate (see "How it's drawn").

| Layout | Used when the card's layout width is | Reference size | Where it shows |
|---|---|---|---|
| Thumb | under 60 | 40 x 58 | Chain panel, callouts, history tiles, drag ghost |
| Compact | 60 to 139 | 88 x 128 | Field, hand, piles, editor tiles, test hand, grading and Van lists, posters |
| Standard | 140 to 279 | 200 x 292 | Pack reveal, versus, spotlight, title wall, detail panel, shop shelf faces |
| Full | 280 and up | 360 x 525 | Main menu showcase, reading view, slab reveal, full-size slabs |

**Thumb:** base K, rim, the glyph centred at 60% of the width in light ink on its disc. No text (the row beside it
names the card).

**Compact** (normal fit):

| Element | Rect (x0, y0, x1, y1) | Content |
|---|---|---|
| Name plate | 0.06, 0.56, 0.94, 0.95 | The name, centred, up to 3 lines, `FitWords` max 14, min 10 (`MinWordSize`), then best fit down to 7. Today: 12 |
| Window | 0.06, 0.20, 0.94, 0.54 | The pattern; the glyph disc (diameter 0.30 W) centred at x 0.25; the chip centred at x 0.68 |
| Chip | 0.42, 0.29, 0.94, 0.45 | Plate colour, light ink, bold 12: `LV 4`, `RK 4`, `LINK 3`; Spells and Traps their subtype in capitals (`QUICK`, `CONT`, `EQUIP`, `FIELD`, `RITUAL`, `COUNTER`; none for Normal). Pendulum adds nothing here |
| Link grid | in place of the chip | A 3x3 grid, 0.30 W square: the eight outer cells lit (light ink) where the card has a Link Marker, dim (plate) where not; the rating in the centre cell |
| Footer | 0.04, 0.03, 0.96, 0.18 | Printed stats, bold 12: `1800/1600`, `ATK 2300` (Link), `?` for printed 0 as `CardDetail.Stat` does; `SPELL` / `TRAP` |

**Compact, badged fit** (the deck editor's results and deck tiles, which carry a limit badge 20x20 at the top left and
an in-deck count at the bottom right, DeckEditor.cs:2799-2810 and 2970):

| Element | Rect | Content |
|---|---|---|
| Top band | 0.40, 0.78, 0.94, 0.97 | The chip (or Link grid, 0.22 W) at the band's right; the left 0.38 W stays empty for the badge |
| Name plate | 0.06, 0.40, 0.94, 0.76 | As above |
| Window | 0.06, 0.20, 0.58, 0.38 | The pattern and the glyph disc; the right of this band stays empty for the in-deck count |
| Footer | as above | As above |

**Standard:**

| Element | Rect | Content |
|---|---|---|
| Name plate | 0.05, 0.865, 0.95, 0.965 | The name, left-aligned, up to 2 lines, `FitWords` max 20, min 13 |
| Info row | 0.05, 0.80, 0.95, 0.86 | Glyph disc (0.06 H across) at the left; the chip after it; the type line right-aligned, 11, ink on K: `Warrior · Effect`, `Dragon · Tuner · Effect`, `Quick-Play Spell` (CardDetail's words without the brackets) |
| Window | 0.05, 0.50, 0.95, 0.79 | The pattern and the glyph, 0.5 of the window's height, light ink at 0.9 |
| Text box | 0.06, 0.20, 0.94, 0.49 | Plate colour; the card's text as the data has it, light ink, 11, best fit down to 8; if it still doesn't fit, cut at a word with "…" (the detail panel has the whole text). A Normal monster's text in italics. A Pendulum's Pendulum effect first, then a thin rule, then the monster text |
| Footer | 0.05, 0.04, 0.95, 0.18 | `ATK 1800   DEF 1600` (13, bold), `ATK 2300` and the Link grid (0.16 W) at the right for Link, `SCALE 3 / 3` added for Pendulum; `SPELL` / `TRAP` for the rest |

**Full:** as Standard with sizes for its reference: name max 30, min 18; type line 15; window 0.05, 0.66, 0.95, 0.79
(a band of pattern with the glyph at its left); text box 0.06, 0.20, 0.94, 0.65 at 15, best fit down to 11; footer 18.

**Unknown card** (the face is shown but there's no data): K `#6B3F1F`, the name plate reads "Card <code>", no glyph, no
chip, no text, footer empty.

**Face-down cards:** unchanged. `CardView.DrawBack` (the navy back with the gold star, or the player's own back
picture), the peek tag on your own set cards, "SET" and "set this turn" all stay as they are. A plain texture is never
made for a card the viewer may not see.

### Rarity, foil and grade

- **The foil shader works on plain cards as on pictures.** `CardFoil.Set` gives a plain texture its own layout's rects
  instead of the picture rects (CardFoil.cs:392-395): `_NameRect` = name plate, `_ArtRect` = window, `_AttrRect` = glyph
  disc, `_StarsRect` = chip, `_TextRect` = text box (Standard, Full) or footer (Compact); `_Ink = 1`. So each rarity's
  `Finish` (Printings.cs:108-151) lands where it does on a picture: Rare a silver name; Super Rare a holo window and
  foiled glyph and chip; Ultra a holo window and a gold name; Secret the secret foil and a rainbow name; Ultimate embossed
  window, name, rim and chip; Ghost a muted silver window; Gold rares a gold rim; whole-card foils (Platinum, Starlight,
  Quarter Century, Parallel, Starfoil, Mosaic, Shatterfoil) over the whole card with the text box kept readable. Thumb
  takes only whole-card foils (no name).
- **A rarity mark** (plain cards only; pictures unchanged): on Standard and Full faces, for Rare and up, a tag at the
  window's bottom right, 0.22 W x 0.07 H, plate colour, bold text in the rarity's colour: `R` `#D8DCE6`, `SR` `#6FC3FF`,
  `UR` `#FFD45A`, `ScR` `#FF8FE0`, the others their initials (CardDetail's `Short`, CardDetail.cs:868) in
  `CardFoil.PrizeColor`. It is a UI child (`Image` + `Text`, named `RarityMark`), not part of the texture, because a
  card's rarity is the copy's, and `CardFoil.Apply` adds or updates it. It shows with Foils off too (it's information).
- **Grades:** `CardWear` works through the same material, so a copy's corners, scratches and creases show on its plain
  card as on a picture (Settings > Card grades as today).
- **Slabs:** the slab's card is the plain card at the slab's size (Full for a full slab), foiled and worn, with today's
  case, label and glass.
- **The prized moment** runs on plain cards unchanged (it is the material's).

### Turned and sideways cards

- The opponent's cards are turned 180° (`CardView.Create(flip: true)`). A picture stays turned; a **plain card is
  turned back** so its writing reads upright, as the text look's writing does today (owner decision N2). The border keeps
  the owner's colour.
- Defence-position monsters lie sideways with their face, as pictures do.

### Mixed: some pictures, some not

Per card: `CardArt.Get` (or `GetFull`) first; a plain card only when it returns null. The art style picked for a card
(Settings, the Artwork chip) is the picture's business; a plain card has no styles and the chip doesn't show on it.
Both kinds share the 0.686 shape, the shader's rounded corners, the owner border, the live stat strip (bottom 0.18 of
Compact cards on the board, hand and editor, which covers the plain footer with live values exactly as it covers a
picture's) and the foil. The title wall mixes: with fewer than 12 pictures installed it fills with plain cards
(owner decision N4).

## How it's drawn

- **The studio.** One hidden `Canvas` (`RenderMode.ScreenSpaceCamera`) and one disabled orthographic `Camera` on layer
  31 (`cullingMask = 1 << 31`, no named layer needed), owned by `PlainCards`. One card template per layout, built once
  and reused (texts, colours and sprites set per card), never rebuilt per card. To draw: fill the template, `Font.
  RequestCharactersInTexture` for the card's strings, `Canvas.ForceUpdateCanvases()`, `camera.Render()` into a
  `RenderTexture` (mipmaps on, auto-generated), then `Graphics.CopyTexture` into a non-readable `Texture2D` with the same
  mip count. Trilinear filtering, aniso 2. The texture's `name` is `plain:<code>:<layout>:<fit>:<px>` (how
  `CardFace.IsPlain` knows it, as `ArtLoader.IsLetterboxed` uses names).
- **Resolution.** The texture's width is the card's on-screen width in pixels (layout width x the canvas scale,
  `min(Screen.width / 1920, Screen.height / 1080)`) rounded up to the next of 64, 96, 128, 192, 288, 432, 648, 972, at
  most the layout's cap (Thumb 96, Compact 288, Standard 648, Full 972). Height = width / 0.686.
- **Cache.** Keyed by (code, layout, fit, width). Least recently used, bounded by bytes: **96 MB** in all (counted with
  mips). The title wall and the editor reuse the same few hundred entries. `CardFace.Invalidate()` empties it (the font
  changing in M3, `Ui.Font`; a check).
- **Budget.** A card drawn the first time costs at most **2 ms median, 5 ms at the 95th percentile** on the owner's Mac
  at Compact 128 px, measured by `-plaincheck`. At most **6 ms of drawing per frame**: beyond that, the face gets its
  texture at once but filled only with K and the rim (a per-kind blank drawn once and copied), and is queued; queued
  faces are drawn in later frames, same texture object, so the screen fills in without redrawing. Opening the deck
  editor (25 results, up to 90 tiles) must be fully drawn within **20 frames**, and a pack's 9 cards in the frame they
  appear. Drawn again from the cache: no cost beyond today's picture path.
- **Pictures are untouched.** `CardArt`, `ArtLoader` and their caches don't change; the plain cache is separate.

## Parts and files

Two builders, no file shared at the same time. **Part R** is built first and alone owns Unity until its `-plaincheck`
passes; **part S** writes its code against R's interface meanwhile and runs Unity after R hands over. Both are Unity
(Unity Architect, Opus). There is no engine or harness part.

### Part R: the renderer (Unity Architect, Opus)

Owns:

| File | What changes |
|---|---|
| `unity/Assets/Scripts/CardFace.cs` (new) | The public face API below; the cache, tags, audit |
| `unity/Assets/Scripts/PlainCards.cs` (new) | The studio, the four layouts and two fits, patterns, the queue |
| `unity/Assets/Scripts/CardView.cs` | `Build` (lines 112-148): every shown face goes through `CardFace` and `DrawArt`; the text-look branch goes. `Create` (43-59) gains `fit`; flipped plain faces turn back. `FrameColor` and `TextColor` stay (public `TextColor`). Peek, back, badges, `Pile`, `FitWords`, `SyllableCut`, `BrokenWords`, `SplitDigraphs` unchanged |
| `unity/Assets/Scripts/CardFoil.cs` | `Set` (367-410): the plain rects and `_Ink` when `CardFace.IsPlain`; `Apply` (289-301): the rarity mark |
| `unity/Assets/Scripts/Icons.cs` | Nine glyph keys (adds to the `For` switch; nothing existing changes) |
| `unity/Assets/Scripts/DuelController.PlainCheck.cs` (new) | `-plaincheck` |
| `unity/Assets/Scripts/DuelController.cs` | Only two lines: `-plaincheck`'s start after line 713 and its name in the `checks` list (line 716). Written first, then handed to S |

Interface (exact; S codes against it):

```csharp
namespace YgoCampaign.Game
{
    /// <summary>The four plain-card layouts, by the card's size in layout units.</summary>
    public enum CardLayout { Thumb, Compact, Standard, Full }

    /// <summary>What the face must leave room for: nothing, or the deck editor's limit badge and in-deck count.</summary>
    public enum CardFit { Normal, Badged }

    /// <summary>Every card face in the game: the player's picture when there is one, else the plain card. Never null for a
    /// card the game has data for; null only for code 0.</summary>
    public static class CardFace
    {
        public static CardLayout LayoutFor(Vector2 layoutSize);                 // by width: <60, <140, <280, else Full

        /// <summary>The face texture for a card at a size in layout units. <paramref name="full"/> asks for the whole
        /// picture (CardArt.GetFull) where one exists, for the big views.</summary>
        public static Texture? Picture(uint code, CardInfo? info, Vector2 layoutSize, CardFit fit = CardFit.Normal, bool full = false);

        /// <summary>The plain card alone (never a picture), for checks and the title wall's fill.</summary>
        public static Texture PlainOnly(uint code, CardInfo? info, CardLayout layout, int pixelWidth, CardFit fit = CardFit.Normal);

        public static bool IsPlain(Texture? texture);
        public static CardLayout? LayoutOf(Texture? texture);                   // null for a picture

        /// <summary>Puts a face on <paramref name="image"/> (texture, tag; a plain card's uvRect reset) and returns it.
        /// Callers then call CardFoil.Apply as they do today.</summary>
        public static RawImage Draw(RawImage image, uint code, CardInfo? info, Vector2 layoutSize, CardFit fit = CardFit.Normal, bool full = false);

        /// <summary>The plain layout's window in UV (posters, cut-ins crop to it); a picture's art window otherwise
        /// (PosterView.ArtBox's values).</summary>
        public static Rect WindowUv(Texture texture);

        /// <summary>A rarity's short mark: N, R, SR, UR, ScR, then initials (moved from CardDetail.Short).</summary>
        public static string RarityShort(Rarity rarity);
        public static Color RarityColor(Rarity rarity);

        public static void Invalidate();                                       // empties the plain cache

        // For checks.
        public static long DebugBytes { get; }
        public static int DebugQueued { get; }
        public static (double medianMs, double p95Ms, int drawn) DebugTimings { get; }
        /// <summary>The name as laid out on a plain texture: its text with line breaks, size, rect (card fractions), and
        /// CardView.BrokenWords / SplitDigraphs problems found when it was drawn.</summary>
        public static PlainName? DebugName(Texture texture);
        /// <summary>Every tagged face under <paramref name="root"/> (active and on screen), and problems: a CardView that
        /// shows a face without a tag, a tagged face with no texture.</summary>
        public static (List<FaceTag> faces, List<string> problems) DebugAudit(Transform root);
        /// <summary>Whether a face looks blank in a screenshot: 97% or more of a 24x24 sample grid within 24/255 of the
        /// median colour in every channel.</summary>
        public static bool DebugFlat(Texture2D screenshot, RectTransform face);
    }

    public sealed class PlainName { public string Text = ""; public int Size; public Rect Rect; public List<string> Problems = new(); }

    /// <summary>On every RawImage CardFace.Draw fills (and on CardView faces): what it shows.</summary>
    public sealed class FaceTag : MonoBehaviour { public uint Code; public bool Plain; public CardLayout Layout; public CardFit Fit; }
}
```

`CardView.Create` gains one parameter at the end (callers unchanged):

```csharp
public static CardView Create(Transform parent, Vector2 center, Vector2 size, FieldCard card, CardInfo? info,
    int owner, bool reveal, Action<CardInfo>? onHover, bool offField = false, Action? onClick = null, bool highlight = false,
    Action? onRightClick = null, bool flip = false, bool hand = false, CardFit fit = CardFit.Normal);
```

`-plaincheck <folder>` (new; fresh `-mods`; run with `-pics <empty folder> -hdpics off`; it makes its own test
pictures, flat colour with a diagonal stripe, named by passcode, in its mods `cards/images`, never real ones). Each step
fails on today's code:

1. **Every layout and kind:** draws one card of each kind in the colour table (Normal, Effect, Ritual, Fusion, Synchro,
   Xyz, Link, Pendulum, Token, each Spell and Trap subtype, an unknown code) at Thumb, Compact, Compact badged, Standard
   and Full; each is tagged, plain, not flat; a shot sheet `plain-sheet-<layout>.png`.
2. **Contrast:** light ink on each kind's plate is at least 4.5:1.
3. **Names:** the 13 names `-layoutcheck` uses plus "Number 38: Hope Harbinger Dragon Titanic Galaxy" at Compact and
   Compact badged: no `DebugName` problems; the badged name rect clears the top-left 20x20 and the bottom-right count box.
4. **Text:** a long-text card (Ash Blossom & Joyous Spring) at Standard ends in "…" inside its box; at Full it fits or
   ends in "…"; a Normal monster's text is italic; a Pendulum shows both texts.
5. **Foil:** the same Ultra Rare as a plain card and as a test picture: the plain material has the plain rects and
   `_Ink = 1`; the picture's material has `_ArtRect (0.12, 0.285, 0.885, 0.82)`, `_TextRect (0.06, 0.05, 0.94, 0.262)`
   and its `_Ink` as today (a pendulum test picture its pendulum rects); a Rare's plain card has a rarity mark "R", a
   Common none, a picture none.
6. **Pictures win:** with test pictures for 3 of a deck's codes, the editor shows those 3 as pictures (`Plain` false, the
   texture is `CardArt.Get`'s) and the rest plain; Import Cards' own test pictures stay pictures.
7. **Turned:** an opponent's plain card is turned back (its face's world rotation is 0); an opponent's picture is turned.
8. **Face-down:** a set card shows the back, untagged; your own set card's peek tag still names it.
9. **Budget:** 120 Compact, 9 Standard, 1 Full drawn cold: median and 95th percentile within budget; the editor opened
   on a 60-card deck is fully drawn (`DebugQueued == 0`) within 20 frames; `DebugBytes <= 96 MB` after the title wall, the
   editor and a pack.
10. **Cache:** the same card at the same size twice is the same texture object; `Invalidate()` drops them; a redraw after
    it matches.

### Part S: the screens and the no-pictures pass (Unity Architect, Opus)

Every place that draws a card face without `CardView` moves to `CardFace.Draw`; no screen keeps its own fallback.
Owns these files, and in the shared ones only these ranges:

| File | Method (lines) | Change |
|---|---|---|
| `MenuScreens.cs` | `TitleScreen.Build` (74-102) | Fewer than 12 pictures installed: fill with plain cards (Standard, `PlainOnly`) of the starter decks' showcase codes and the built-in decks' showcase codes (`CampaignScenes.ShowcaseOf`), about a third foiled as today |
| `MenuScreens.cs` | `MainMenu.ShowCard` (400-411) | `CardFace.Draw` at the showcase's size (Full); no early return when there's no picture |
| `DuelController.cs` | the `_menu.Showcase` lambda (487-500) | Your rarest card whether or not it has a picture; before a campaign, a starter showcase card as a Secret Rare |
| `CampaignScenes.cs` | `DrawSide` (598-613) | The showcase slots: `CardFace.Draw` + `CardFoil.Apply`; the name-text fallback goes |
| `CampaignScenes.cs` | `ShowLessons` (770-775) | The lesson's showcase card the same way |
| `StoryScreen.cs` | `ShowPortrait` (228-243) | The plain card when the deck's showcase has no picture |
| `PosterView.cs` | `Create` (36-55) | `uvRect = CardFace.WindowUv(texture)`; the letter fallback goes |
| `SlabView.cs` | `Create` (95-104) | `CardFace.Draw(..., full: size == Size.Full)`; the colour fallback goes |
| `TraderScreen.cs` | `ShowList` (around 208), `Select` (around 273) | `CardFace` for both faces |
| `StructureScreen.cs` | `Show` (around 85) | `CardFace` for the box's face card |
| `CardDetail.cs` | `ShowArt` (390-402), `Show` (around 263) | `CardFace.Draw` at the art box's size; `Short` moves to `CardFace.RarityShort`; the kanji attribute badge (lines 26-29, 105-109, around 224) becomes the new glyph on the same disc (owner decision N3) |
| `DeckEditor.cs` | `ShowReading` (1029-1053), `BeginDrag` (1448-1455), the tile `Create` calls (2722, 2952: `fit: CardFit.Badged`), `Badge` (2799-2810: its name offset line goes), `DebugNameProblems` (464-500: reads `CardFace.DebugName` of each tile's face instead of `Face/Name`) | As said |
| `BoardFx.cs` | `Shatter` (370-380), `CutIn` (977-1000) | Shards cut from the face texture (plain or picture); the cut-in shows the face's window (`WindowUv`) |
| `BoardView.cs` | `ShowChain` (194-199) | Thumbs via `CardFace.Draw` |
| `BoardView.Callouts.cs` | `ShowCallout` (164-169) | Thumb via `CardFace.Draw` |
| `BoardView.Play.cs` | `Tile` (456-460), `DrawHistoryDrawer` (532-536) | Thumbs via `CardFace.Draw` |
| `DuelController.FaceAudit.cs` (new) | | The no-pictures pass's shared step (below) |
| `DuelController.cs` | `CampaignCheck` (2709-4020), `MenuCheck` (4456-4519), `LayoutCheck` (4999-5359) | Audit steps at their shots |
| `DuelController.Shop.cs`, `DuelController.ThemeCheck.cs` | the check bodies | Audit steps at their shots |

Not touched (they already apply foil to every `RawImage` under a `CardView`, so plain faces get foil and the rarity mark
with no change): `ShopScreen.cs` (`Face`, 708-720; the reveal sounds at 1014-1086 are Audio pass 1's), 
`GradingDeskScreen.cs` (`Thumb`, 207-216; line 407 is Audio pass 1's), `VanScreen.cs`. Also untouched: `Playmat.cs`
(field spell mats keep today's behaviour without pictures, owner decision N6), `FoilShowcase.cs`, `GradeShowcase.cs`,
`ArtCheck.cs` (developer tools about pictures).

**The no-pictures pass (Q1).** A shared step, `FaceAudit(report, name, root, expected)` in
`DuelController.FaceAudit.cs`: at the end of the frame it captures the screen
(`ScreenCapture.CaptureScreenshotAsTexture`), runs `CardFace.DebugAudit` on the screen's root, and reports FAIL for
each problem, for each tagged face that `DebugFlat` calls blank, and when fewer tagged faces than `expected` are on
screen. It runs only when the check was started with `-pics` naming an empty folder (`CardView.Art.Installed.Count ==
0`), so the with-pictures runs are unchanged. Expected counts, each 0 on today's code:

| Check | Shot | Expected tagged faces |
|---|---|---|
| `-menucheck` | title, main menu | at least 24; 1 showcase |
| `-campaigncheck` | starters, versus, duel layout, pack revealed, slab reveal, catalog reading, collection editor, recipe book, locker | 12; 5; hand and field cards; 9 with a rarity mark on each Rare and up; 1 Full; 1 Full; every tile; every strip card; every unlocked poster |
| `-shopcheck` | the shop's first shot, a pack, a box summary | 5 shelf faces; 9; every card |
| `-themecheck` | versus, shop, Van shelf, both themes | as above, per screen |
| `-layoutcheck` | board, chain panel, history, pinned detail | every card; one thumb per link; one per tile with a card; 1 |

### After the build

Code Reviewer (Opus) on both diffs against this spec; one fix round by the builders; Evidence Collector (Opus) on a fresh
check copy; Technical Writer (Sonnet) for the docs below and `docs/DEVELOPING.md` (the `-plaincheck` row; the no-pictures
pass: which checks, with which flags).

## Docs (Q10)

- `docs/CARD-PICTURES.md` lines 19-20 become: "Without pictures, every card shows its **plain card**: its name, kind,
  attribute, Level or Rank, stats and, when it's shown big enough, its text, in a card frame drawn by the game, with its
  rarity's foil. You can play the whole game this way. Pictures only change how cards look." Line 95: "A card without a
  picture shows its plain card."
- `docs/PLAYING.md`, "Rarity and foils": one line that foils and grades show on plain cards too, and what the rarity
  mark is.
- `docs/dev/TODO.md`: the review's Q1-Q5, Q10 and M1 checked off with a pointer here; "the QCSR 25 mark is invisible on
  full-text cards" (TODO.md:52) re-checked on plain cards.

## Traps

- **The studio and the font atlas.** Legacy `Text` shares the dynamic font's atlas with every screen; drawing new
  characters in the studio can rebuild the atlas and blank other text for a frame. Request the characters
  (`Font.RequestCharactersInTexture`) before laying out, and test with a long-text card while the board shows text.
- **`Canvas.ForceUpdateCanvases()`** before `camera.Render()`, or the template renders last card's layout.
- **Mipmaps through `CopyTexture`:** source and destination need the same size, format and mip count; a mismatch copies
  mip 0 only and small cards shimmer.
- **Every `RawImage` under a `CardView` gets foil** in ShopScreen, GradingDesk and Van: the rarity mark must be an
  `Image` + `Text`, never a `RawImage`.
- **Turning back flipped faces:** `CardView.Create` counter-rotates every `Text` (CardView.cs:56); the plain face's
  `RawImage` needs the same, and its foil light must follow the turned face (check the light direction on an
  opponent's foil).
- **`DebugNameProblems`** finds names by `Face/Name` today; with plain faces there is no such `Text`, so it would count 0
  names and pass vacuously. It must read `DebugName` and fail when it finds no names.
- **Queued faces:** a face drawn as the blank and filled later is the same texture; a check that shoots in the first
  frame sees blanks. Shots wait until `DebugQueued == 0`.
- **Retina:** `Screen.width` and `height` are pixels; layout picking uses layout units, resolution uses pixels. Don't
  mix them, or the same card shows text on one Mac and not another.
- **Unknown cards** (`info == null` with a face shown, e.g. a card missing from the pack) must still get a plain card,
  not crash the studio.
- **Opponent hand reveals** (spectating, Hands: BOTH) are turned cards too.
- **The drag ghost** (DeckEditor `BeginDrag`) is created mid-drag: a queued face there must not stall the drag.
- `CampaignScenes.cs` is also being changed by "The climb" (the versus screen); this build touches only `DrawSide`'s
  showcase loop and `ShowLessons`' card. `MenuScreens.cs`' settings rows are Audio pass 1's; this build touches only the
  title wall and `ShowCard`.
- Old saves: nothing saved changes. The plain cache isn't saved.

## Verification

App only (no harness change), from a fresh check copy `build/qa-plain` (`tools/qa_copy.sh`), one run at a time, fresh
`-mods` each, absolute paths, `-mute`, at 1280x720 and 1920x1080:

1. **Without pictures** (`-pics <empty folder> -hdpics off`): `-plaincheck`, `-menucheck` (its two sound steps FAIL
   muted; ignore them), `-campaigncheck`, `-shopcheck`, `-themecheck`, `-layoutcheck`, `-searchcheck`, `-focuscheck`,
   `-editorcheck`, `-alleycheck`, `-fxshots`. No FAIL besides the menu's sound steps; every shot looked at.
2. **With pictures** (the machine's EDOPro folder, as found today; and once with `-hdpics <HD folder>`): `-menucheck`,
   `-campaigncheck`, `-layoutcheck`, `-stylecheck` (twice, as documented), `-importcheck` (without `-hdpics off`: it needs
   the HD folder), `-artcheck`. Same results as before the build, and the shots compared side by side with
   `build/qa-evidence/checks/` (today's): cards with pictures look the same.
3. `--texts-check` and `--campaign-test` from a harness build, to show nothing else moved (no engine change expected).

No DP change, so `--arbitrage` isn't needed. Audio isn't heard (muted).

## Owner decisions

Decided (owner, 2026-11-20): as recommended in the review.

1. **The look.** Decided: the no-pictures look is the game's default look and gets designed; pictures are an upgrade.
2. **Fonts.** Decided: one display face and one body face, open licence (next spec; shortlist in the appendix).
3. **720 players.** Decided: a type floor now (next spec), a 720 layout only later.
4. **The board's size.** Decided: leave it; blend the mats (later).
5. **The player's face.** Decided: a code-drawn crest (later).
6. **Build order.** Decided: this spec, then type and fonts (M2 + M3), then rewards (M4 + M5).

New, recommendation first (building the recommendation unless you say otherwise):

- **N1. Kind colours.** *Recommended:* keep today's colour per kind (orange Effect, yellow Normal, purple Fusion and so
  on). It's the colour code every player reads at once; the layout, glyphs and marks are ours. *Or:* our own palette,
  which players would have to learn.
- **N2. The opponent's plain cards read upright.** *Recommended:* yes, as the text look does today; the red border says
  whose. *Or:* turned like pictures (upside-down writing).
- **N3. CardDetail's attribute badge.** Today it's a coloured disc with the attribute's kanji (地 水 炎 風 光 闇 神), which
  is close to the official attribute icons. *Recommended:* replace the kanji with this build's glyphs (about 2 hours in
  part S). *Or:* leave it.
- **N4. The title wall with few pictures.** *Recommended:* under 12 pictures installed, fill it with plain cards. *Or:*
  plain cards only when there are none.
- **N5. The rarity mark.** *Recommended:* on plain cards only, so pictures look exactly as today. *Or:* on both.
- **N6. Field spell mats.** *Recommended:* unchanged without pictures (no plain card as a mat). *Or:* the plain card's
  window as the mat's art.
- **N7. A "plain cards for everything" setting** for players with some pictures who want one look. *Recommended:* not
  now. *Or:* add it to Settings (half a day).

**Cost:** part R about 4 agent-days, part S about 2.5 (including the no-pictures pass), review and fixes about 1,
evidence about 0.5, docs about 0.25 (Sonnet): about 8 agent-days. The review's estimate was 5.5; the studio, the slab
and foil integration and the audit are what grew.

## Appendix: font pairs for the type spec (M3)

Names and reasons only; nothing downloaded. All three are under the SIL Open Font License 1.1, which allows bundling in
a game with the licence text (it goes in About's third-party notices). Each needs checking for the symbols the game
draws in text (▲ ▼ ⊘ ⬢ ★ ☆ · …): where a face lacks one, the type spec keeps the built-in font as a fallback.

1. **Barlow Condensed (display) + Barlow (body).** OFL. From Google Fonts or the designer's repository
   (github.com/jpt/barlow). A grotesk modelled on road signs and transit lettering: the condensed bold reads like a
   tournament banner ("VICTORY!", "BEGINNER CIRCUIT"), and the regular family is plain and legible at small sizes, with
   many weights. One family for both keeps the look consistent. *My pick.*
2. **Oswald (display) + Source Sans 3 (body).** OFL. From Google Fonts; Source Sans 3 also from Adobe's repository
   (github.com/adobe-fonts/source-sans). Oswald is a tall poster gothic, loud and arcade-like for titles and banners;
   Source Sans 3 is one of the most legible UI faces at 9-11 px, which suits a 720 floor.
3. **Chakra Petch (display) + IBM Plex Sans (body).** OFL. From Google Fonts; Plex also from IBM's repository
   (github.com/IBM/plex). Chakra Petch's cut corners echo the logo's circuit-board edges, the most "Duel Circuit" of the
   three; Plex Sans is sturdy and clear in body text. The display face is the most distinctive and the least neutral.
