# Card looks: foil, rarity, grade wear and slabs (plan, 2026-11-07)

**Goal:** cards that read like the real printings in hand, and better where a screen can do more than a scan (tilt,
light that follows you, slabs that feel like objects). This is a plan for review. Nothing has been changed yet.

**Inputs:** a web research pass (real rarities, condition, slabs, the best digital card renderers) and a read-only
Technical Artist audit of `CardFoil.shader`, `CardFoil.cs`, `CardWear.cs`, `SlabView.cs` and `Rarities.FinishOf`.
Before shots are in `build/foil-review-2026-11-07/` (`foil-*.png` every rarity, `closeA/closeB-*.png` close-ups at
250 px, `board-*.png` board size, `grades.png`, `z-*.png` crops).

## Where we are

The system is already wide: 27 rarities mapped to layers (art, name, whole-card, border, stars, outline), wear by grade
from a generated atlas, a prized-moment sweep and a slab. What holds it back is how it's drawn, not the coverage.

**What looks right today:** the Ghost Rare's silver hologram, the Secret Rare's silver-rainbow name, the gold borders
(you can spot them at any size), Damaged stains, and text that stays readable under wear.

**What reads wrong, and why:**

1. **Foil looks like an overlay.** Rainbow is *added* on top in gamma space with a 0.3 floor, so art washes pastel and
   dark ink still glows. Real foil shows *through* transparent ink: strongest under light ink, gone under dark ink,
   and never on the text box (it has a white underprint).
2. **Patterns are visible lines, not foil.** The Secret lattice is about 7x too coarse and looks like chain-link fence;
   Prismatic and Starlight look like graph paper. Real gratings are far below pixel size; the eye sees glitter plus
   broad rainbow bands.
3. **No light model.** The light is a 2D glare blob at the pointer, so foil never goes dark off-angle and flashes
   on-angle. That contrast is what sells holo. At rest the glare sits over the name as a permanent haze.
4. **Names are muddy.** Gold names render brown and silver ones grey (the metal is lerped toward the black ink), which
   hurts Rare, Ultra, Gold and every Secret.
5. **The worst five:** Collector's Rare (rainbow rings wreck the art), Starlight (screen door over the whole card),
   Shatterfoil (a regular window grid), Mosaic (looks like JPEG blocks), Ultimate (can't tell it from a Common). Also
   Parallel dots damage the name and rules text, Platinum reads as a washed-out Common, and Starfoil stars are always
   lit like stickers.
6. **Board size collapses.** At about 85 px Super, Ultra, Secret, Prismatic, Starlight and Quarter Century all become
   the same rainbow tint; Platinum and Ultimate become Common.
7. **Square corners.** No card has rounded corners, so gold borders look like picture frames and corner wear is a
   white disc painted on a square corner.
8. **Wear tells by label more than by look.** Edge whitening is a uniform frame, scratches are white pen lines that
   ignore the light, and creases have no ridge. Grades 10, 8 and 5 look the same on the board.
9. **The slab is a grey panel.** All of its case layers sit under the card, and its inner highlight is invisible (bug:
   the Shine image has alpha 0, and `Outline` takes the graphic's alpha).
10. **Source pictures set a ceiling.** EDOPro pictures are 177x254 JPEGs with no mipmaps: stretched 1.4-3x in detail
    views (crisp foil over a blurry print), aliased on the board. The game ships no art, so we can't fix the source,
    only stop fighting it.

**Rows in `FinishOf` to correct:**
- Quarter Century Secret is Secret foil over the whole face, plus a champagne-gold speckled name and a translucent
  25th watermark in the text box. It is not a diagonal Starlight grid.
- Starlight is the Prismatic horizontal pattern over the whole face, text box excluded.
- Prismatic Secret is a grid on the art only, with a multicolour speckled name.
- Super Rare foils the Level stars and Attribute (2017 on).
- Ultimate's embossing covers the art frame and border as well as the art.

Four rarities never appear in the showcase (Pharaoh's, Extra Secret, Platinum Secret, Ghost/Gold). We'll find cards
for them in `printings.bin`.

## The approach

The techniques that matter most, ranked by research and audit together:

1. Ink-aware foil (multiply through ink, masked by darkness and region).
2. A light model with a specular lobe, so foil is dark off-angle, flashes on-angle, and its hue follows angle rather
   than time.
3. Patterns at real scale, resolved as coherent sparkle (flakes fixed to the card that light up as the light crosses
   them) plus broad bands.
4. Perspective tilt with spring inertia in the big views.
5. Embossing through lighting (Ultimate, Premium Gold, Collector's).
6. Physical cues: rounded corners, gloss laminate, scratches that only show in the highlight, slab bevels and
   reflections.

## Phases

Each phase ends with before/after screenshots from the fixed-light harness and the owner's look. Shader phases go to one
Technical Artist in sequence (it's one file), and C#/UI work runs alongside it on separate files.

### Phase 0: the harness (S) [Unity Architect]
We can't judge changes while the light drifts randomly, so this comes first.
- `CardFoil.FixedLight`: a fixed light, plus a hover sweep over N frames.
- `-foilstrength`, and a board row at the real 0.6 strength.
- A slab panel (Full, Compact, Reveal at a few grades).
- Real board strength in `GradeShowcase`.
- The missing four rarities.
- A contact sheet laying before and after side by side.
- Running the same command twice must give identical PNGs.
- Files: `FoilShowcase.cs`, `GradeShowcase.cs`, `CardFoil.cs` (light hook only), `DuelController.cs` (args).

### Phase 0b: HD pictures (S–M) [Unity Architect]
The biggest single lift for every foil: a sharp print under the foil instead of a 177x254 JPEG stretched 1.4-3x.
- **Getting them (player side, the game still ships no art):** players supply their own pictures; EDOPro-compatible
  HD picture folders are 813x1185, about 150 KB each, with the same layout as EDOPro's pictures. (Since 2026-11-08 the
  public docs name no download tool: docs/dev/README-PLAN-2026-11-08.md, decision 5.)
- **The catch:** the game looks in `mods/cards/images/` *first*, so a player with low-res pictures there would never
  see HD pictures from another folder. Fix: when a card has a picture in more than one folder, prefer the larger one.
- **Memory:** 813x1185 is about 21x the pixels: roughly 2.9 MB each uncompressed, so about 870 MB at `MaxCached` 300
  (vs about 40 MB today). Plan: downscale on load to a cap that still covers the biggest view on Retina (the 525 px
  reading view is about 1050 px tall at 2x, so the full size only pays off there), then mipmaps and
  `Texture2D.Compress` (DXT1, 1/6). A 512-wide cap comes to about 0.25 MB per card (about 75 MB at 300), with the
  full-size picture loaded only for the reading view and slab.
- **Hitches:** pictures decode on the main thread (`CardArt.Get`), and an HD JPEG costs about 20x as much. The deck
  editor opens about 130 at once. Plan: decode on a worker (`ImageConversion` off-thread isn't allowed, so read bytes
  off-thread and spread decodes across frames), with the text look until each arrives.
- **Shader:** relief and outline already scale by texel size; the layout rects are fractions, and YGOPRODeck pictures
  share EDOPro's layout. Check the art window still lines up.
- **Docs:** a README section on getting HD pictures (no built-in downloader).
- Files: `CardArt.cs`, README.
- **As built (2026-11-07):**
  - New `ArtLoader.cs` handles the 768 px cap, mipmaps with sharpened levels, DXT1, and a disk cache trimmed to
    1 GB. `CardArt.GetFull` keeps the last 4 full-size pictures for the reading view, card reader and Full slab.
    `hd_pics:` is repeatable, with `-hdpics <path>|off`. `-artcheck` measures load times.
  - Numbers: about 264 KB per HD card in memory. A load takes 33 ms the first time and 0.12 ms from the disk cache.
    The deck editor opens in about 200 ms once warm (352 ms with the old pictures), but takes 2.5 s the first time
    ever.
  - **Left open:** a background pre-warm of the decks' cards at idle, to remove that one-time first-view stall.
  - The "before" for later phases is `build/foil-review-2026-11-07/hd/`.

### Phase 1: foundations (M) [Technical Artist]
- **Ink-aware blend:** foil = ink × (base + lobe × rainbow) plus a small screen term for highlights. The mask comes
  from luma and (1 − saturation) per region, and the floors go.
- **Light model:** a pseudo-normal per pattern (grating, facet, emboss), the light/view lobe, and a gloss laminate on
  all cards (commons too).
- **Metal names:** replace the letters fully, brightness 0.75-1.4 with a specular flash, a 1 px dark rim for
  legibility.
- **Rounded corners:** SDF alpha (adds `UNITY_UI_ALPHACLIP` for Masks). Plain cards get the material too; each card is
  already its own draw call, so this costs nothing.
- **Text-box protection** for whole-card foils, the same way wear is kept off the text.
- **Glare:** glare only on hover or glint, not parked over the name. The idle glint stops under Reduced Motion (today
  it ignores the setting).
- **Mipmapped art** in `CardArt.cs`, and relief taken from a blurred mip; Relief runs only inside the art.
- Sine-free hash, ready for finer patterns.
- Files: `CardFoil.shader`, `CardFoil.cs`, `CardArt.cs`.

### Phase 2: rarity by rarity (M–L) [Technical Artist; Printings.cs rows by the same agent]
- **Real-scale patterns** with sparkle: Secret diagonals (~250 lines per card height), the Prismatic/Starlight
  horizontal grid, broad rainbow bands on top.
- **Rewrites:**
  - Collector's: fine directionless ridges, mostly silver, ink-masked.
  - Shatterfoil: irregular Voronoi shards.
  - Mosaic: fine squares, under the art's colour.
  - Starfoil: stars that show only when lit.
  - Parallel: a fine dotted film that keeps off the name and text.
  - Pharaoh's: glyphs that catch the light rather than sitting on top.
- **Premium rarities:**
  - Ultimate: strong emboss, desaturated gold-silver, embossed frame and stars.
  - Platinum: bright brushed metal with a moving highlight.
  - Gold: foil sheen on the frame and text-box border, rounded.
  - Ghost: add the faint rainbow and a hint of depth.
  - Quarter Century: whole-face Secret foil, the champagne name, the watermark.
- **`FinishOf` corrections** from the list above.
- **Board readability:** at 110 px and below, boost name-foil brightness, border glint and band contrast so Ultra,
  Secret and Starlight stay tellable across the table. Optional `_LQ` path for the deck-editor grid.
- Files: `CardFoil.shader`, `src/Ygo.Engine/Data/Printings.cs`.

### Phase 3: slabs (M) [UI Designer spec, then Unity Architect; runs alongside Phase 2]
- Fix the invisible Shine.
- Fix the grade text running past the label on the Reveal and Compact slabs ("GRADE 10 GEM MINT" is cut off, and on
  Compact it covers the gauge). `Text()` sets `horizontalOverflow = Overflow`, which defeats best-fit. Seen in Phase
  0's `baseline/slabs.png`.
- A new `Resources/SlabGlass.shader` case-top drawn *over* the card: bevel highlights that turn with the shared light,
  a moving reflection streak, an inner-well shadow offset from the light, and a thin chromatic fringe at the bevel.
- Case thickness (offset side strip), a soft drop shadow, and a ridge between the label and card wells.
- Our own hologram sticker on the label: a hex-gauge "G&G" monogram in fine lines that shifts with the light.
- What stays: the parchment-and-umber label at the bottom, our own grade words. What it avoids: anything trade-dress
  like (red top label, lighthouse mark, colour-coded gold/silver/black labels, subgrade boxes, blue label, black
  insert).
- Files: `SlabView.cs`, `SlabGlass.shader` (new), `CardFoil.cs` (shares the light).

**Phases 1 and 2 as built (2026-11-07):**
- Phase 1: one light model (`Foil()`), metal names, rounded corners on every card, text-box protection, glare only
  on hover or glint, the Foil intensity setting.
- Phase 2: real-scale patterns with sparkle fixed to the card; the rewrites; `NameFoil.Speckled`/`Champagne` and
  `BorderFoil.Embossed`; the QCSR and Starlight fixes; the board boost (`_Small`); letterboxed art skips the layout
  rects; `-foilintensity`. `--campaign-test` passes.
- Still weak:
  - the QCSR "25" mark mostly hides under the rules text, because of the ±12% rule;
  - Ghost depth is subtle (Phase 5's tilt will help);
  - Shatterfoil costs about 2.5x the old figure (the fix if needed: skip its Voronoi search when `_Small` is 1);
  - Gold Rare art washes out at the centre of the hover lobe.

**Phase 3 as built (2026-11-07):**
- Built to the slab spec. Every spec item landed; the deviations are listed in the builder's report.
- Checks: `-campaigncheck` 179, `-alleycheck` 120, `-themecheck` 31, `-shopcheck` 89, `-stylecheck` 18, `-menucheck` 6,
  all 0 FAIL. The label fit check is 94/94.
- Weak:
  - the glare bands barely show over very bright cards;
  - the "G&G" monogram reads as texture at rest;
  - the rim glass doesn't follow a card's hover scale.

### Phase 4: grade wear (M) [Technical Artist, after Phase 2]
- Corners become a change of *shape* (alpha with fuzz) on the new rounded corner.
- Edge whitening becomes patchy nicks gathered near corners, not a frame.
- Scratches show only in the light lobe (slightly dark otherwise).
- Gloss dulling by grade, the strongest "played" cue and visible on the board too.
- Faint print lines on some foils, a seeded centering offset (1-2%), dents that catch the light, and crease ridge
  shading.
- The ±12% legibility rule stays.
- Files: `CardWear.cs` (table and atlas), `CardFoil.shader` (wear block).

**Phase 4 as built (2026-11-07):**
- All eight wear items are in; `-gradesweep N` shows wear under a moving light.
- Checks: `-campaigncheck` 179, `-alleycheck` 120, style and menu, all 0 FAIL.
- Weak:
  - board-size dulling is modest, weakest on white-frame cards;
  - the print line is faint at 150 px;
  - dents are subtle;
  - grade 8's corner cut is small;
  - card backs are still not done.

### Phase 5: tilt (M) [Unity Architect + Technical Artist]
- A vertex-shader perspective tilt, ±8°, on the picture only, with a spring following the pointer. The tilt also
  drives the light model's view vector.
- Only in picture-only views: card detail, reading view, pack reveal, slab, cut-in. Board and hand cards have child
  overlays that wouldn't tilt with them.
- A new setting, "Card tilt", forced off by Reduced Motion.
- Known small issue: `RectMask2D` clips on the untilted rect.
- Files: `CardFoil.shader` (vertex), `CardFoil.cs`, `FxSettings.cs`, menus.

**Phase 5 as built (2026-11-07):**
- Vertex perspective tilt, up to 8°, with a fixed-step spring (stiffness 160, damping 19). The tilt drives the light's
  view vector, and the Ghost's depth image shifts with it.
- Where it applies: the card detail, the reading view, the ace cut-in and the menu showcase card.
- Where it doesn't: board, hand and grid cards, pack reveals and the spotlight (their stats strips are siblings), and
  slabs (left flat).
- "Card tilt" setting, plus `-cardtilt` and `-foiltilt`. All earlier sheets are byte-identical.
- Weak: the detail picture's rarity tag and Artwork chip stay flat; the live spring was never exercised on screen.

**Phase 6 reviews (2026-11-07):**
- **Code review:** no blockers, nine should-fixes. The real bugs:
  - a QCSR NaN on letterboxed art;
  - the print line rolls on the hidden true grade;
  - the slab's `_LabelText` is never set;
  - stale full-size pictures survive style changes.
- **Reality check:** B−, NEEDS WORK. A big improvement, but too quiet at rest and at board size, three rarities wash
  out under hover, Ultimate's emboss is crude, and grade wear doesn't read on the board.
- **Fixes:** round A covers the C# side (pictures, cache, slabs, rims, stats strip); round B covers the shader and
  visuals.
- **Calls made by the lead** (the owner can overrule):
  - print lines only on visible grade 8 and below or a 9 slab, so a Raw "Mint" never shows one and the hidden 9/10
    stays hidden;
  - the biggest picture still wins over the player's own folder;
  - under the pointer, the card's near side dips away, as if pressed with a fingertip.

**Fix rounds (2026-11-07):**
- **Round A** (the C# side) fixed the stale full-size cache, hardened the disk cache, added a disk cache for
  full-size pictures (a reader opens in 2.3 ms once cached), set `_LabelText`, made the sticker monogram legible,
  quieted owner borders on slab rims, and rounded the stats strip.
- **Round B** (the shader) added:
  - a resting-light floor and a stronger board boost;
  - the hover-wash cap;
  - the Ultimate rework;
  - the Secret family's 45° band;
  - wear as white corner nicks, with corners at grades 8 and 7 and brighter scratches;
  - the QCSR name and mark moved;
  - Collector's whorls and Pharaoh's glyphs;
  - the code-review items (QCSR guard, the print-line rule, the tilt direction, `_Small` for worn plain cards).
- **Final shots:** `build/foil-review-2026-11-07/fixB/`.
- **Checks:** campaign 179, alley 120, theme 31, shop 89, style 18/4 and menu, all 0 FAIL; deterministic.
- **Leftovers:** in TODO.md under "Card looks".

### Phase 6: review and fixes
- A Code Reviewer reads the diff.
- A Reality Checker compares the after shots against the research rarity by rarity, at detail and board size, and
  checks the deck editor and board frame rate on the slowest Mac we have.
- Then a fix round.
- README (foil and grade sections), `UI-THEME.md`, and a HANDOFF note.

**Performance budget (from the audit):** today's shader takes about 150-300 ALU ops and 1-2 samples on a typical foil
card. The deck editor (about 130 cards) can afford 2-3x that. The 1-3 big cards in detail views can take a
high-quality path for free.

**Not in this plan (later):**
- Wear on card backs (back-edge whitening is the most visible real wear).
- Bending the card mesh for foil curl.
- 3D pop-out or parallax art layers for top tiers.
- Anything that downloads pictures.

## Owner decisions (recommendation first)

**Decided (owner, 2026-11-07): accept all.** The checks were run against a local folder of HD pictures (813x1185),
kept outside the repository and never shared.

1. **Which era each rarity follows.** *Recommend: the modern TCG printing, one look per rarity* (e.g. Super Rare
   with foiled stars). Alternative: per-set era looks, which needs set dates per printing.
2. **Quarter Century watermark.** *Recommend our own plain "25" numeral mark* drawn in the shader, not Konami's
   logo. Alternative: no watermark (the gold name and whole-face foil still tell it apart).
3. **Card tilt.** *Recommend on by default in the big views only*, with a "Card tilt" setting and off under Reduced
   Motion. Alternative: no tilt (Phases 1-4 still stand alone).
4. **Rounded corners and gloss on every card**, commons included. *Recommend yes.*
5. **Board readability over strict realism.** Real cards at 85 px would blur together too. *Recommend a modest boost
   at board size* so rarities stay tellable. Alternative: true-to-life subtlety.
6. **Grade wear on the board.** *Recommend gloss dulling shows on the board; the finer wear (nicks, scratches)
   only at detail size.* The label stays the main tell on the board.
7. **A "Foil intensity" setting** (Subtle / Standard / Vivid). *Recommend yes*: cheap, and a safety valve for
   readability.
8. **HD pictures.** *Recommend Phase 0b: the game prefers the larger picture, compresses and loads them without
   hitches, and the README points players to the HD downloader.* No built-in downloader (the game fetching Konami's
   art itself is a different thing from a tool the player runs). Alternative: build our own "Get HD pictures" button.
9. **Order.** *Recommend Phase 0 → 0b → 1 → 2, with 3 alongside 2, then 4, 5, 6.* After Phase 2 the owner can stop if it's
   good enough.
