# Slab look: build spec (2026-11-07)

A UI Designer's spec for Phase 3 ("slabs") of docs/dev/CARD-LOOKS-PLAN-2026-11-07.md. It covers what builders change in
`SlabView.cs`, the new `Resources/SlabGlass.shader`, a small light hook in `CardFoil.cs`, the reveal in
`GradingDeskScreen.cs` and the harness. Nothing is built yet. Today's look: `build/foil-review-2026-11-07/hd/slabs.png`.

**What's wrong today:** (1) every case layer is under the card, so nothing reads as plastic over it; (2) the Shine
outline is invisible (Image alpha 0, and `Outline` multiplies by it); (3) Reveal and Compact grade text runs off the
label and over the gauge, because `Text()` sets `horizontalOverflow = Overflow`, which turns best-fit off; (4) the
case reads as a flat grey panel.

**The idea:** a Gauge & Gilt slab is two clear halves about 5 mm thick. The card sits in a recessed well, and the label
has its own well at the **bottom**. Plastic is sold by its edges and reflections, not by tint, so most of the work is
one glass pass drawn **over** the card. The label stays printed and flat.

## 1. House rules (trade dress)

- Keep: parchment `#EFE6D2` label with umber type and `#A8823A` rules, at the bottom of the case. The grade sits in a
  gauge on the left of the label. Our own grade words and glyphs (`Grades.Word`, `Grades.Glyph`).
- **The label never changes colour by grade.** `Grades.Color` stays off the slab.
- Never: a red-bordered white label, a logo band across the top, a lighthouse mark, a "21" moulded into the case, gold,
  silver or black labels by grade, "Black Label" or "Pristine", subgrade boxes, a visible inner sleeve, a black insert
  around the card, a blue label, or a hologram in the top left.
- The well gap round the card is at most 4 px (Full) and translucent, so it never reads as a black insert. The well is
  moulded into the case (one edge), not a second clear rectangle, so it never reads as a sleeve.

## 2. Geometry per size

Layout px at the 1920x1080 reference. Origin is the case centre, y up. Outer sizes don't change (callers depend on
them). Reveal and Compact have side walls thicker than top and bottom, because their case has the card's aspect.

| | Full 360x586 | Reveal 184x268 | Compact 121x176 | Rim (tile WxH) |
|---|---|---|---|---|
| Corner radius / bevel width | 12 / 6 | 8 / 4 | 6 / 3 | 4 / 2 |
| Card (unchanged size) | 332x484 at (0, 37) | 143x208 at (0, 19) | 94x137 at (0, 13.5) | the tile |
| Card well (card + margin) | 340x492, margin 4 | 149x214, margin 3 | 98x141, margin 2 | margin 1 |
| Walls: side / top / bottom | 10 / 10 / 10 | 17.5 / 8 / 8 | 11.5 / 4 / 4 | ring 4 |
| Ridge between wells | y -209 to -215 | y -88 to -93 | y -57 to -60 | none |
| Label well | 340x68 at (0, -249) | 168x33 at (0, -109.5) | 113x24 at (0, -72) | none |
| Label (well minus 2/2/1) | 336x64 | 164x29 | 111x22 | none |

In Reveal and Compact the label well spans the inner width, so it's wider than the card well. That gives the text
room. The Full slab is also shown at 0.62 scale in the desk and van detail panes (223x363 on screen), so every
hairline must be drawn anti-aliased in screen pixels (`fwidth`), at least 1 screen px wide.

## 3. Layer stack, back to front

Hierarchy names in brackets (checks find children by path; keep `Label/Number`).

1. **Drop shadow** [`Shadow`]: generated soft rounded-rect sprite (9-slice), black. Offset (0,-10), blur 18, alpha
   0.50 Full; (0,-6) / 10 / 0.45 Reveal; (0,-4) / 6 / 0.40 Compact; (0,-2) / 3 / 0.35 Rim. Static: it's the room's
   overhead light, not the pointer. A shadow that slides with the pointer looks wrong.
2. **Thickness** [`Side`]: the case's rounded rect again, offset (+3,-4) Full, (+2,-3) Reveal, (+1,-2) Compact, (+1,-1)
   Rim, colour rgba(0.42, 0.48, 0.55, 0.55) with a 1 px rgba(0,0,0,0.5) outline baked into the sprite. Only a 3-4 px
   band shows at the right and bottom: the case's edge. Static (Phase 5 tilt may drive it later).
3. **Well floors** [`WellFloor`, `LabelWellFloor`]: rounded rects at the two well rects, `#06080C` at alpha 0.60 (card)
   and 0.45 (label). Only the margin ring shows: the gap line where the card sits. The radius is the card's corner
   radius plus the margin, so the recess shows dark through the card's rounded corners (Phase 1).
4. **Card** [`Art`]: RawImage plus `CardFoil` as today.
5. **Label** [`Label`]: parchment, rules, gauge, text (section 5). Flat print: no gloss of its own.
6. **Glass** [`Glass`]: one RawImage over the whole case, material `SlabGlass`, always the last sibling,
   `raycastTarget = false`. It draws everything in section 4, the sticker included.

Remove today's `Edge`, `Case` and `Shine`. Generated sprites (shadow, rounded rect, hexagon, sticker texture) are built
once in C# and cached in statics, like `CardWear`'s atlas.

## 4. The glass pass (SlabGlass.shader)

**Blend:** `Blend One OneMinusSrcAlpha` (premultiplied), so one pass can both lighten and darken. Each effect adds
light (rgb) or darkens (alpha, with black). Output is `(sum of adds + veil rgb, 1 - product of (1 - darks))`, all
multiplied by vertex alpha, so `CanvasGroup` fades still work. Support UI stencil masking and `UNITY_UI_CLIP_RECT`
like UI-Default. No GrabPass, no camera texture, and one texture (the sticker), sampled only inside the sticker rect.

**Uniforms:** `_CaseSize` (px), `_Radius`, `_Bevel`, `_CardRect`, `_CardWell`, `_LabelWell`, `_LabelText` (the text
column, protected), `_NameStrip`, `_StickerRect` (zero = none), `_Light` (x, y in case uv from bottom left, z = hover
0..1), `_Seed`, `_ScuffCount`, `_StickerFlash`, `_Glint` (perimeter glint phase, -1 = off). Rects are in case px.

**The light:** `L2 = normalize(_Light.xy - 0.5)` (the light's direction across the case), `h = _Light.z`, and
`lit(n) = saturate(dot(n, L2))` for an edge's outward normal `n`. At the idle light (0.30, 0.78 in card uv) the key is
upper left, so the top and left edges are lit.

| # | Effect | Shape | Colour and strength | Light response | Sizes |
|---|---|---|---|---|---|
| 1 | Outer rim | 1 px at the case edge | dark 0.55 | none | all |
| 2 | Bevel face | bevel band, fading inward | add white x (0.05 + 0.20 lit(n)); dark 0.18 x lit(-n) | turns with the light | all |
| 3 | Bevel crest | 1 px line, 1.5 px inside the edge (1 px below Full) | add `#F4F8FF` x (0.30 + 0.45 lit(n) + 0.15 h) | bright on the lit side, about 0.3 on the far side | all |
| 4 | Chromatic fringe | 2 lines of 1 px at the bevel's inner edge | outer line add (0.10, 0.02, 0); inner line add (0, 0.03, 0.12); x (0.5 + 0.5 h) x lit(n) | lit side only | Full, Reveal |
| 5 | Wall veil | walls, outside both wells | dark 0.16 plus rgb (0.05, 0.06, 0.075), x 1.15 at the top to 0.85 at the bottom; add 0.04 h | brightens on hover | all |
| 6 | Well wall (refraction) | 2 px band just outside each well edge (1.5 Reveal, 1 Compact) | lit side add 0.06 rgb (0.85, 0.95, 1); shadow side dark 0.10; 1 px fringe as in #4 at 0.5x | per edge normal | Full, Reveal, Compact |
| 7 | Contact shadow | inside the card edge, 3 px fade (2 Reveal, 1.5 Compact) | dark 0.30 x (0.2 + 0.8 lit(-n)) | darkest on the edges away from the light | Full, Reveal, Compact |
| 8 | Ridge | 1 px light line over 1 px dark line, at the ridge's top | add 0.25 + 0.20 lit(up); dark 0.35 | brighter with the light above | Full, Reveal (Compact: light line only) |
| 9 | Outer glare A | wide soft band (below) | add `#EEF4FF` x (0.06 + 0.08 h) | moves with the light | Full, Reveal, Compact |
| 10 | Outer glare B | thin band beside A | 0.6x A | moves with A | Full, Reveal |
| 11 | Inner reflection | band A again, fainter, offset | 0.45x A, masked to the two wells | travels further than A (parallax) | Full, Reveal |
| 12 | Scuffs | hairline segments | add 0.35 x (A + 0.5 bevel lit) | visible only in the glare | Full 7, Reveal 4 |
| 13 | Sticker | section 6 | | | Full, Reveal |
| 14 | Perimeter glint | bright segment running round the crest | add 0.5 | reveal only | Full |

**Glare bands (9-11).** Work in px from the case centre, `p`. The band normal is `nb = normalize(1, -0.7)`, so the bands
run steeply from lower left to upper right. Let `q = dot(p, nb)` and `lp = (_Light.xy - 0.5) * _CaseSize`.
- Band A: centre `cA = 0.55 dot(lp, nb)`, half-width `wA = 0.11 caseWidth`,
  intensity `smoothstep(wA, 0.4 wA, |q - cA|)`.
- Band B: centre `cA + 0.24 caseWidth`, half-width `0.025 caseWidth`.
- Inner reflection: centre `0.75 dot(lp, nb) + 0.03 caseWidth`. Because it travels further than A, the two
  reflections separate as the light moves off centre and meet near the middle: that is what reads as depth.

**Scuffs (12).** `_ScuffCount` segments from `_Seed` (the copy's seed, so each slab keeps its own). Each is 0.06-0.22
case widths long, at a shared base angle ±25° (from sliding), with a 0.8 px core. They sit anywhere on the case top
except where their midpoint falls in `_LabelText`. They're fixed to the case and show only where glare A or a lit
bevel crosses them.

**Never hide the name or the grade.** Inside `_NameStrip` (the card's name bar: 4.5% to 12.5% of the card height from
its top, full width) and `_LabelText`, total add is capped at 0.05 and total dark at 0.10. Over the rest of the card,
total add is capped at 0.14.

**Cost:** about 150 ALU ops on a typical pixel and at most about 260 inside the scuff loop. One material per slab
(per-slab uniforms), created by SlabView and destroyed in `OnDestroy`: the reveal makes and drops slabs, so this
avoids leaking materials. At most 3 big slabs are ever on screen.

## 5. Label layout per size

Font: the legacy built-in (`Ui.Font`). x is from the label's centre. **Gauge:** a flat-top hexagon (generated sprite),
gold ring `#C9A04A`, centre `#2B2118`, number `#F3E3B5` bold. Today's diamond can't fit "10" at Compact. A flat-top
hexagon stays wide near its middle, and it echoes the house mark.

| | Full label 336x64 | Reveal label 164x29 | Compact label 111x22 |
|---|---|---|---|
| Rules (top and bottom) | 2 px `#A8823A` | 1 px | 1 px |
| Gauge (w x h, ring) at x | 58x50, ring 2.5, at -132 | 29x25, ring 1.5, at -63.5 | 23x20, ring 1.5, at -42 |
| Number | 28 bold | 15 bold | 11 bold |
| Text column x (width) | -95..121 (216) | -44..56 (100) | -26..52 (78) |
| Row 1 | y +20, h 14: `GAUGE & GILT` 10 bold `#6B5226`, left; cert `No. 4417-0293` 10 `#6B5226`, right (decision 1) | y +6, h 14: grade word + glyph, 12 bold `#2B2118`, best fit 9-12 | y 0, h 13: grade word only, 10 bold `#2B2118`, best fit 8-10 |
| Row 2 | y +1, h 22: grade word + glyph (`GEM MINT ✦`), 18 bold `#2B2118`, best fit 13-18 | y -7.5, h 11: `GAUGE & GILT` 9 bold `#6B5226` | none |
| Row 3 | y -19, h 15: `Name · SET-CODE` 12 `#4A3B28`, best fit 9-12, ellipsis fallback | none | none |
| Sticker (w x h) at x | 34x39 at 145 | 17x20 at 69.5 | none |
| Sealed | `SEALED · GAUGE & GILT` 14 bold `#6B5226`, gauge empty, no sticker | `SEALED` 11 bold | `SEALED` 9 bold |

- Text is left-aligned (`MiddleLeft`), except the cert (`MiddleRight`). The grade row drops "GRADE 10": the gauge
  carries the number (decision 3). `LabelText()` still says "GRADE 10 GEM MINT" for checks and screen readers, and it
  adds the cert number when one is shown.
- Contrast on `#EFE6D2`: `#2B2118` about 13:1, `#4A3B28` about 8.6:1, `#6B5226` about 5.6:1. All pass AA.
- **The overflow fix:** `SlabView.Text()` sets `horizontalOverflow = Wrap` and `verticalOverflow = Truncate`. Every
  label text gets explicit best-fit min and max (above). A row's rect is at least 1.2x its max size tall, so one line
  fits. The name row adds `FitOneLine(text, full)`: if `preferredWidth` at the min size still exceeds the rect,
  trim words and end with "…" until it fits. The widest grade rows ("NEAR MINT ◆", "EXCELLENT ●") fit every column
  at their max size, so they never shrink in practice.
- At 0.62 scale (desk and van detail panes), the number (17 px) and grade word (11 px) stay readable. Row 1 and the
  name row become texture there, which is fine: those panes print the name and grade beside the slab.

## 6. The hologram sticker

- **Shape:** a pointy-top hexagon (the gauge is flat-top, so the two marks don't merge), on the right end of the label,
  vertically centred, 6 px from the edge (Full) or 4 px (Reveal). It is not drawn at Compact or on the Rim (it would be
  noise at 10 px), and not on a sealed label.
- **Texture:** generated once in C#, 112x128 RGBA, the hexagon filling it.
  - B: the outer hex border (3 texels) and an inner ring at 82% (1 texel): the gauge.
  - R: fine lines between 30% and 78% radius: concentric hexagons every 4 texels, crossed by 24 spokes every 15°
    (a gauge dial engraved in fine lines).
  - G: the monogram "G&G", in 3-texel strokes, 56% of the hexagon's width, centred. Each glyph is a polyline on a
    10x14 grid (y up), and the glyphs are spaced 1 unit apart:
    - G: an ellipse arc centred (5, 7), radii 4.5 x 6, from 40° to 320° counter-clockwise, then up to (8.45, 6.2), then
      left to (5.6, 6.2).
    - &: at 0.8x scale: (9.2,0) (2.8,8.6) (2.8,11.2) (4.6,13.2) (6.6,11.6) (6.6,9.8) (1.6,5.0) (1.6,2.0) (3.6,0.2)
      (6.2,0.4) (9.4,4.2).
- **Colour (in the glass pass, under the glass effects).**
  - `lobe = 0.15 + (0.5 + 0.5 h) x saturate(1 - d / 0.6)^2`, where d is the distance from the light to the sticker's
    centre in case uv.
  - Base: silver at `0.62 + 0.25 lobe`.
  - R lines: `rainbow(frac(2.2 r + 1.6 L.x + 1.1 L.y)) x (0.25 + 0.75 lobe)`, where r is the hex radius. Each ring
    takes its own hue, and the hues walk outward as the light moves.
  - G monogram: `rainbow(frac(2.2 u - 1.4 v + 1.6 L.x + 1.1 L.y + 0.5)) x 0.9 x (0.4 + 0.6 lobe)`, over the base
    darkened to 0.7.
  - B border: gilt `#C9A04A x (0.8 + 0.4 lobe)`.
  - `rainbow` is a cosine palette. Saturation is 0.45 at rest and rises to 0.75 with hover, so at rest the sticker is a
    quiet silver hex with a hint of colour, and under the pointer it shifts about 1.5 hue cycles across its width,
    with the same light as the card foil.
- **Reveal flash:** `_StickerFlash` (1 to 0) pushes lobe and saturation to full.

## 7. Light hookup

- `CardFoil` exposes `public Vector3 CurrentLight` (its eased `_light`, `_hover`).
- A new `SlabGlass : MonoBehaviour` on the Glass maps that light from card uv to case uv each frame:
  `case = (cardMin + light x cardSize - caseMin) / caseSize`. It sets `_Light` only when the value changes.
  - `CardFoil` already tracks the pointer within half a card of the art, which covers the label, so the case and the
    card always share one light.
  - Under `CardFoil.FixedLight` the mapping is exact, so the harness stays deterministic.
  - If the slab has no picture (so no `CardFoil`), `SlabGlass` runs the same tracking. Move the pointer and drift
    code into a static `CardFoil.Track(RectTransform, ref LightState)` that both call.
- `SlabGlass.Override(Vector3? light)` lets the reveal drive a sweep.
- **Reduced Motion:** with no pointer over it, the slab uses `IdleLight` exactly: no drift, no idle glint (even before
  Phase 1 stops CardFoil's), no sweeps, no flash, no perimeter glint. Pointer-driven light still follows the pointer;
  the player moves it.

## 8. The rim (grid tiles)

Rims are used in the deck editor results (88x128), van, desk and shop tiles. Recommended (decision 2): a static glass
made of two shared generated sprites, so it costs nothing per frame and batches.
- A ring sprite (9-slice, 4 px): veil, a crest lit on the top and left at the idle light, a dark rim, and a 1 px gilt
  `#B08A3E` line at the well edge. Keep the gilt: it's the house tell at grid size.
- A diagonal band sprite over the card: add 0.05, never over the top 13% of the card (the name).
- Under the card: shadow and side, as in section 3.
- Today `Rim()` is called before the card at 3 of the 4 call sites. Keep `Rim()` for the under layers, add
  `SlabView.RimGlass(parent, center, size)` for the top layers, and call it right after the card view is made (VanScreen
  ~224, GradingDeskScreen ~209, ShopScreen ~1062, DeckEditor ~2360).

## 9. The slab-reveal moment (GradingDeskScreen.PlayReveals)

The stamping order stays: sealed, then the number, then the label. Add the following, and stop destroying and
recreating the slab between stages: add `SlabView.SetLabel(slab, copy, info, setCode, LabelStage.Sealed|Number|Printed)`,
which rebuilds only `Label`'s children, so the glass light runs on smoothly.
1. **Arrival** (the existing 0.4 s fade and scale): from t = 0.25 s, a light sweep over 0.7 s, eased in and out. The
   override goes from case uv (-0.3, 1.25) to (1.3, -0.25) with hover 1, so the crest runs bright round the case and
   the bands cross it once.
2. **The number stamps** (existing 1.6 to 1 scale): the label also presses 1.5 px down for 0.06 s and back, and the
   gauge ring flashes `#F6DE8E` back to `#C9A04A` over 0.25 s.
3. **The label prints:** the sticker appears. `_StickerFlash` runs from 1 to 0 over 0.6 s, and the sticker rect scales
   from 1.15 to 1 over 0.12 s. For grade 10, add one perimeter glint: an 18%-of-perimeter segment runs clockwise round
   the crest in 0.9 s.
- **Reduced Motion:** the stages change on the same timings (that's content, not motion), but there's no sweep, press,
  flash, scale or glint. A skip click jumps to Printed with the light at rest.

## 10. Performance budget

- **Glass:** one pass, one draw, no GrabPass, at most 1 texture sample. ALU as in section 4.
- **Whole slab:** about 6 draws (shadow and side batch with the floors and label images; then the card; the text; the
  glass).
- **SlabGlass.Update:** no allocations, one `SetVector` only when the light changed.
- **Rims:** shared sprites only, so no material and no Update.
- **Check:** in the Frame Debugger the Glass is 1 pass. With 3 Full slabs and 30 rims on screen, there's no new GC
  per frame and the CPU cost of SlabGlass is under 0.1 ms.

## 11. Acceptance checks (harness)

All runs go through `tools/foil_shots.sh` under the fixed light, and the same command twice must give identical PNGs
(`tools/foil_compare.py` says "identical").

| Sheet | How | What it shows / passes |
|---|---|---|
| `slabs.png` (exists) | `-foilslabs` | Full, Reveal and Compact at 10, 9 and 6. Passes when: the top and left crests are lit and the bottom and right darker; the case reads as clear over the card (a band crosses the art); the well gap is visible but no black frame; there's a 3-4 px side band and a soft shadow; no text leaves its label; the Compact word sits clear of the gauge; the sticker shows on Full and Reveal. |
| `slabs-1..5.png` (new line in the script) | `-foilslabs -foilsweep 5` | The light crosses top left to bottom right. Passes when: bands A and B travel; the inner reflection separates from A off centre; the crest highlight moves round the bevel; the sticker hue changes on every shot; scuffs show only inside a band; the card name strip never brightens by more than the cap. |
| `slabs-labels.png` (new mode) | `-foilslabs labels` | Rows: grades 10, 9, 8, 6, 4, 2 and Sealed. Columns: Full at 1, Full at 0.62, Reveal, Compact, on the installed card with the longest name. Each label text logs `slabfit: <size> <grade> <child> ok|FAIL` (preferredWidth at the used size vs rect width). The script fails on any FAIL. |
| `slabs-context.png` (new mode) | `-foilslabs context` | Full at 0.62 on the Panel colour `#1A1F2B`; Full on the reveal scrim (black 0.78); rims at 88x128 and at the van, desk and shop tile sizes on their screens' backgrounds; one Full on parchment-light `#EFE6D2`, to show the glass still reads on a light ground. |
| `slab-reveal-*.png` (new) | `-slabreveal <dir>`, with `Time.captureDeltaTime = 1/60` | Frames at 0.15, 0.6, 1.0, 1.35 and 2.2 s: sweep, stamp, sticker flash, final. With `-reducedmotion`, the same frames show no sweep or flash, and two extra frames 1 s apart at rest are byte-identical. |

The owner then judges the before and after (Phase 0 `baseline/slabs.png` against the new one) in
`foil_compare.py`'s page.

## Owner decisions (recommendation first)

**Decided (owner, 2026-11-07): accept all five recommendations.**

1. **Cert number and barcode on the label.** *Recommend a cert number on Full only, no barcode or QR.* The number is
   `No. dddd-dddd` from the copy's seed, so it's stable per copy. It gives each slab an identity at almost no cost, and
   it's in `LabelText`. A barcode at our sizes is grey mush, and at 0.62 scale it's noise next to the grade.
   Alternatives: (a) cert plus a short 1-D barcode under the sticker at Full; (b) neither (a cleaner label).
2. **Glass on grid-tile rims.** *Recommend static glass from shared sprites* (lit crest, veil, gilt line, one faint
   band at the idle light): it reads as a case at a glance and costs nothing. Alternatives: (a) live SlabGlass per rim
   (moves with the pointer, but one material each and no batching, with dozens possible in the deck editor); (b)
   today's flat rim, just tidied.
3. **The grade row's wording.** *Recommend the word and glyph only ("GEM MINT ✦"), with the number in the gauge,* at
   every size. That's what makes Reveal and Compact fit. Alternative: keep "GRADE 10 GEM MINT ✦" on Full only.
4. **The gauge shape.** *Recommend a flat-top hexagon,* which fits "10" at Compact and matches the house mark and the
   class's own comment. Alternative: keep the diamond, with the Compact number dropped to 9 px.
5. **Where Reveal and Compact appear.** No game screen uses them today (only the harness; the reveal uses Full).
   *Recommend no new placements in Phase 3.* Alternative: show a Compact slab in the card detail for slabbed copies, as
   SlabView's comment intends.
