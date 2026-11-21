# Evidence pass (2026-11-21b): plain cards and first impressions

Evidence Collector (Opus), on the uncommitted change set at HEAD 4e1b492, for
[PLAIN-CARDS-SPEC-2026-11-20.md](PLAIN-CARDS-SPEC-2026-11-20.md), after its review and one fix round. Tested and reported,
nothing fixed. The only repo file I wrote is this one. Output is under `build/qa-evidence3/` and `build/bin/evidence3/`.
Every check but `-menucheck` ran muted; nothing was heard.

**The build also carries the uncommitted game data** (`campaign/circuits.txt`, `campaign/featured.txt`, 24
`personalities/*.profile` skill lines, two deck swaps undone). The app's `StreamingAssets` copies are byte-identical to
the repo's. Two step-count changes below come from that data, not from this build.

## Result in short

- Harness: all pass. `--games 30`: classic 8, modern 22, 0 script errors, 0 crashed (as before).
- 41 app check runs: **39 with 0 FAIL, 2 with one FAIL each**. No "Exception" in any log; every run exited 0.
  - `-plaincheck` at the real large window (3024x1964) **FAILS step 1**: the three Trap thumbs "look blank" (finding 1).
    At 1280x720 and 1920x1080 it passes 34 of 34.
  - `-themecheck` at 1280x720 failed once on an old timing step (the story crossfade, 0.54 s against a 0.54 s floor); the
    rerun passed 41 of 41. Not this build (finding 6).
- Every no-pictures run of an audited check logs its audit count (menu 2, campaign 12, layout 7, shop 4, theme 10), each
  audit step ok. Every with-pictures run logs them all as skipped.
- By eye: no flat blank card slot on any screen I looked at, with or without pictures. Legibility at 1280x720 is the weak
  point: Standard text boxes and editor deck-tile names can't be read (finding 3).
- Content rule: the glyphs, chips, grid and frame are our own shapes; no picture file in `git status`; `-plaincheck`
  left its `-mods` folder empty of pictures.

## 1. Builds

| Build | Command | Result |
|---|---|---|
| Harness | `dotnet build src/Ygo.Harness -c Release -o build/bin/evidence3` | 0 errors, 4 old warnings. `Ygo.Harness.dll` 09:43:26; `Ygo.Engine.dll` 2026-11-20 23:37:13 (no engine change) |
| Game | `Unity -batchmode -quit -projectPath unity -buildTarget OSXUniversal -executeMethod ProjectSetup.BuildMac -buildout "build/qa-evidence3/Duel Circuit.app" -logFile build/qa-evidence3/build.log` | `Build Finished, Result: Success.`, exit 0 at 09:43:40 |

The app's `Assembly-CSharp.dll` and `Ygo.Engine.dll` are from 09:43:31 (2026-11-21). The newest sources are
`DuelController.cs` (09:09:29), `DuelController.PlainCheck.cs` (09:06:09), `PlainCards.cs` (09:05:17), `CardFoil.cs`
(09:02:51). No `.cs` under `src/` or `unity/Assets/Scripts` is newer than the app's DLL. The DLL holds the new strings
("plain check", "face audit: skipped"). Bundle id `com.ygocampaign.game.qa`.

## 2. Harness (`build/qa-evidence3/harness/*.log`)

| Check | Result |
|---|---|
| `--campaign-test` | exit 0, `All campaign checks passed.` |
| `--deckbuilding-test` | exit 0, `All deck-building checks passed.` |
| `--texts-check` | exit 0, `All texts checks passed.` |
| `--games 30` | `classic wins: 8   modern wins: 22   draws/unfinished: 0`; `script errors: 0   crashed games: 0` |
| `check_history_decks.py` | `0 problems` |

## 3. App checks (`build/qa-evidence3/checks/<run>/{log.txt,out/,args.txt}`, runner `run.sh`, queues `queue1.sh`, `queue2.sh`, summary `checks/summary.txt`)

One run at a time, a fresh absolute `-mods`, absolute `-logFile` and out folder, `-screen-width W -screen-height H
-screen-fullscreen 0`, `-mute` except `-menucheck`. No `-batchmode`/`-nographics`. "No pictures" = `-pics
build/qa-evidence3/empty-pics -hdpics off`; "with pictures" = no `-pics`, `-hdpics off` (the machine's EDOPro folder:
3551 pictures found).

### No pictures, 1280x720 (23 runs)

| Run | ok | FAIL | Audit line | Time |
|---|---|---|---|---|
| plain | 34 | 0 | (none: not an audited check) | 20 s |
| campaign | 221 | 0 | 12 ran, 0 skipped | 235 s |
| layout | 61 | 0 | 7 ran, 0 skipped | 56 s |
| menu (unmuted) | 8 | 0 | 2 ran, 0 skipped | 20 s |
| shop | 93 | 0 | 4 ran, 0 skipped | 56 s |
| theme | 40 | **1** | 10 ran, 0 skipped | 50 s |
| theme (rerun) | 41 | 0 | 10 ran, 0 skipped | 50 s |
| editor | 65 | 0 | | 20 s |
| search | 71 | 0 | | 35 s |
| focus | 77 | 0 | | 30 s |
| click | 26 | 0 | | 25 s |
| board | 24 | 0 | | 50 s |
| pile | 21 | 0 | | 25 s |
| controls | 40 | 0 | | 20 s |
| tag | 6 | 0 | | 75 s |
| ladder | 42 | 0 | | 181 s |
| final | 46 | 0 | | 341 s |
| climb | 20 | 0 | | 20 s |
| sound | 51 | 0 | | 185 s |
| music | 20 | 0 | | 45 s |
| replay | 16 | 0 | | 40 s |
| alley | 129 | 0 | | 296 s |
| fxshots | (no step lines) | 0 | | 160 s |

### No pictures, 1920x1080 (4 runs)

| Run | ok | FAIL | Audit line |
|---|---|---|---|
| plain | 34 | 0 | |
| campaign | 221 | 0 | 12 ran, 0 skipped |
| layout | 61 | 0 | 7 ran, 0 skipped |
| menu (unmuted) | 8 | 0 | 2 ran, 0 skipped |

### No pictures, the largest window (2 runs)

Asked for 3024x1964; the game got **3024x1964 pixels** (`plain check: window 3024x1964, canvas scale 1.58`; the shots are
3024x1964).

| Run | ok | FAIL | Audit line |
|---|---|---|---|
| plain | 33 | **1** | |
| menu (unmuted) | 8 | 0 | 2 ran, 0 skipped (title wall 35 faces, showcase 1 Full) |

### With the owner's pictures as found, 1280x720 (7 runs)

| Run | ok | FAIL | Audit line |
|---|---|---|---|
| menu (unmuted) | 6 | 0 | 0 ran, 2 skipped |
| campaign | 209 | 0 | 0 ran, 12 skipped |
| layout | 54 | 0 | 0 ran, 7 skipped |
| shop | 89 | 0 | 0 ran, 4 skipped |
| style | 18 | 0 | |
| board | 24 | 0 | |
| import (no `-hdpics off`) | 65 | 0 | |

The with-pictures counts are the no-pictures counts less the audit steps (menu 8-2, campaign 221-12, layout 61-7, shop
93-4). Each skipped step logs `face audit: skipped "<shot>": 3551 pictures installed`.

### Awkward-input runs, 1280x720 (5 check runs, 2 editor runs; section 8)

| Run | ok | FAIL | Audit line |
|---|---|---|---|
| menu, `-pics` holding 2 generated pictures | 6 | 0 | 0 ran, 2 skipped |
| layout, the same | 54 | 0 | 0 ran, 7 skipped |
| menu, `-pics` a path that doesn't exist | 8 | 0 | 2 ran, 0 skipped |
| menu, `-pics` empty and `-hdpics` an empty folder | 8 | 0 | 2 ran, 0 skipped |
| style, no pictures | 19 | 0 | |
| `-editor` on a 90-card deck, no pictures / 2 pictures | (no step lines) | | |

**41 check runs** (23 + 4 + 2 + 7 + 5) plus the 2 editor runs. The 2 FAIL lines, as logged:

```
plain check: FAIL 1. thumb: 19 cards (Normal, Effect, Ritual, Fusion, Synchro, Xyz, Link, Pendulum, Token, Spell, Quick-Play, Continuous, Equip, Field, Ritual Spell, Trap, Cont. Trap, Counter, Unknown) tagged, plain, Thumb, none blank (texture 64x93): Trap looks blank; Cont. Trap looks blank; Counter looks blank
theme check: FAIL a cold place's crossfade doesn't jump: the corkboard painted on the click (268 ms), 0.009 shown after that frame, the fade done in 0.54 s (its time 0.6 s; at most 1.5 s with the shots)
```

Changed counts that are not this build: `-boardcheck` 24 (26 last pass): which dialogs a random-click duel meets
differs (no yesno/picker/position dialog, a chain dialog instead); `-climbcheck` 20 (21): step 7's "former recipe from
the data" became a note because the data's `was:` lines changed (the undone deck swaps). `-fxshots` still misses
scene-fusion, scene-synchro, scene-ritual, as in the last two passes.

`-plaincheck` measurements (1280 / 1920 / 3024): 120 Compact faces cold, median 0.34 / - / 0.35 ms, p95 1.42 / - / 0.60
ms (budget 2 and 5); the editor on a 60-card deck fully drawn in 8 frames (budget 20); cache after editor + wall of 48 +
pack 19.5 MB at 1280, **95.9 MB at 3024 (cap 96)**; step 12 at 3024x1964: 53.8 MB shown, wall faces 288 px, 54 of 54
let go when hidden, 90.6 MB with 40 more Full faces, all drawn again when shown. 20 Full faces at 972 px (7 MB each) keep
the cache at 91.3 MB with 7 released. The note "9 Standard faces asked for in one frame: **-10** drawn at once" is wrong
arithmetic (finding 7).

## 4. Screenshots, no pictures

All at 1280x720 unless said; 1920x1080 checked for the board, hand and detail.

| Screen (shot) | What I see | Blank slot? |
|---|---|---|
| Title wall (`menu-title`; also 3024 and with 2 pictures) | Tilted columns of Standard plain cards behind the logo: name plate, glyph and chip row, a pattern window, dark text box, stats. About a third foiled, with marks (SR, UR, ScR, GhR, StR). With 2 pictures, one test picture sits among the plain cards | None |
| Main menu showcase (`menu-main`) | Aleister the Invoker as a Full plain card, ScR mark, foiled window and name, tilted; text readable | None |
| Versus (`campaign-versus`) | Your three Standard plain cards (Gearfried the Swordmaster with the light glyph, Goblin Attack Force, Gearfried the Iron Knight); Ryo's two showcase cards, foiled ScR, small | None. Ryo's showcase text is unreadable at 720 |
| Pack reveal (`campaign-pack-revealed`, `shop-5-pack`) | Nine Standard plain cards with grade tags; **marks only on the two Super Rares** ("SR"); Commons have none. The live stat strip covers the footer, but the plain footer's "ATK 700 DEF 800" shows faintly through it (finding 4) | None |
| Slab (`campaign-slab-reveal`) | A Full plain card (Catapult Turtle, SR mark, foiled window) in the slab case with its label "8 NEAR MINT" | None |
| Card detail | The art box holds a small plain card; the name bar's disc now holds our glyph (waves for WATER, a rayed disc for LIGHT), no kanji | None |
| Reading view (`campaign-catalog-reading`) | Dark Magician as a Full plain card, ScR, italic Normal text; readable | None |
| Editor (`campaign-collection-editor`, `plain-editor-mixed`) | Badged Compact tiles: limit badge top left clear of the name, "x2" count bottom right clear; stat strip at the foot. **Deck tile names are about 6 px at 720** (finding 3) | None |
| Board (`campaign-duel-layout`, `layout-*`, 720 and 1080) | Hand and field plain cards; the opponent's face-up plain cards read upright (Number 20: Giga-Brilliant), a defence one lies sideways; face-down cards keep the navy back. In the hand, the big Level digit sits over the glyph disc (finding 5) | None |
| Chain panel (`layout-chain-panel`, `-many`) | Thumb plain cards per link (Ash Blossom's fire glyph on orange, Twin Twisters' spell glyph on green); callout and recent strip the same | None |
| Shatter (`fx-shatter`) | Green shards cut from the Spell's plain face | n/a |
| Cut-in (`fx-cutin`) | The window band (pattern and the light glyph) beside "NIBIRU, THE PRIMAL BEING / ATK 3000" | n/a |
| Spotlight (`fx-spotlight`) | Rescue Rabbit as a Standard plain card, ScR mark; text box unreadable at 720; footer ghost under the strip (finding 4) | None |
| Shop box summary (`shop-6-box-summary`) | Ten face-down backs (correct: the audit expects 0 faces) | n/a |
| `-plaincheck` sheets | Thumb, Compact, Compact badged, Standard, Full: 19 cards each, every kind's colour, chip (`LV 7`, `RK 4`, `LINK 3`, `QUICK`, `CONT`, `EQUIP`, `FIELD`, `RITUAL`, `COUNTER`), the Link 3x3 grid, the Pendulum green fade, "Card 100" for the unknown code. `COUNTER` on the badged Compact is tiny | None at 1280; at 3024 the Trap thumbs read flat to the check (finding 1) |
| `-plaincheck` foil sheet | Plain UR (holo window, gold name, "UR"), plain R ("R"), plain N (no mark), the test pictures with no mark. **4 of the 10 "every rarity on plain cards" faces are test pictures**, not plain (finding 8) | None |
| `-plaincheck` mixed editor | The 3 test pictures (flat colour, a white stripe) among plain tiles, same size, corners and strip | None |

**Legibility at 1280x720**, honestly: Standard text boxes (pack, versus, spotlight, title wall) are about 5 px and can't be
read; editor deck-tile and field-card names are 6-7 px, readable only for short names; the board's stat strips and the
hand's names read. At 1920x1080 hand, field and detail text read. Full faces (menu, reading view, slab) read at both.

With pictures (`own-*`): the menu showcase is a pictured card with no rarity mark; the pack reveal shows nine pictures,
the Rare and Ultra Rare with no mark (N5); side by side with `build/qa-evidence2/checks/campaign-1280/` the pictured
cards look the same (different pulls, from the data). That pass's pack had one card with no picture drawn as a flat green
slab; this one has none. I did not compare foil on pictures frame by frame.

## 5. The content rule

- **Glyphs** (read in `Icons.cs`, seen in the sheets): LIGHT a small disc with eight short rays; DARK a crescent opening
  right; FIRE three flame tongues; WATER two stacked wave strokes; EARTH two peaks, left taller; WIND three horizontal
  swooshes; DIVINE a rhombus in a thin ring; Spell a dot with three arcs fanning to the upper right (like a signal icon);
  Trap a square outline with two diagonal bars. None is a kanji, a letter in a ring, or the official Spell/Trap marks; the
  Spell/Trap subtypes are words in a chip, not the official subtype icons.
- **Frame:** dark name plate on the kind colour, a chip with `LV n` instead of a row of stars, a patterned window, a
  dark text box (no beige box), a 3x3 grid instead of arrows on the frame, no set code, passcode or edition line. The
  only borrowing is the kind colour (N1). I see nothing that reproduces the official layout.
- `git status --untracked-files=all` lists no picture file. `-plaincheck`'s `-mods/cards/images` is empty after each of
  its 3 runs (its test pictures deleted). The only pictures I made are two flat-colour PNGs with a stripe
  (`build/qa-evidence3/awk/two-pics/`). All screenshots and crops are under `build/qa-evidence3/`.

## 6. Findings, ranked

1. **`-plaincheck` fails step 1 at the real large window: the Trap thumbs read as blank (CONFIRMED).** Reproduce:
   `run.sh np-plain-big plaincheck 3024 1964 none 1`. The FAIL line is above. In `plain-sheet-thumb.png` (cropped:
   `compare/big-thumbs.png`) each Trap thumb is a pink tile with a small light square mark, about a fifth of its width;
   Spell thumbs (a slightly bigger mark) pass. Either the Trap thumb is too close to flat (the glyph is thin and its disc
   is the kind colour), or the 97% rule is too strict at high resolution; the audit uses the same `DebugFlat`, so a Trap
   thumb in a chain panel on a large screen could FAIL the audit too (PLAUSIBLE; the large-window runs didn't show a
   Trap link). It passes at 1280x720 and 1920x1080.
2. **The review's memory finding, on the real screen: the cache sits at its cap (CONFIRMED, within budget).** At 3024x1964
   the step-9 total is 95.88 MB of 96 MB (19.5 MB at 1280). Step 12 passes for real. Nothing over the cap, but a screen
   this size runs the cache full.
3. **Text too small to read at 1280x720 (CONFIRMED, by eye).** Standard text boxes (~5 px) and editor deck-tile names
   (~6 px) on every screen that shows them; `COUNTER` on a badged chip. This is decision 3's "type floor" (next spec),
   noted so it isn't lost.
4. **The plain footer shows through the live stat strip on Standard faces (CONFIRMED).** Pack reveal and spotlight: a
   faint "ATK 700 DEF 800" left of "L3 700/800" (`np-campaign-1280/out/campaign-pack-revealed.png` on Dragon
   Manipulator and The Rock Spirit; `np-fxshots-1280/out/fx-spotlight.png`). Cosmetic.
5. **The hand's big Level digit covers the glyph disc on Compact plain cards (CONFIRMED, cosmetic).** `layout-pinned.png`
   at 1080: Snake-Eye Ash's "1" over its fire glyph.
6. **`-themecheck` "a cold place's crossfade doesn't jump" is a boundary flake (CONFIRMED).** It failed with the fade done
   in 0.54 s against `took >= FadeSeconds * 0.9` (0.54); the rerun passed; earlier passes logged 0.54-0.57 s and passed.
   `DuelController.BackdropCheck.cs` is unchanged since 0.1.1. Not this build.
7. **`-plaincheck`'s burst note prints "-10 drawn at once" (CONFIRMED, cosmetic).** `9 - burstQueued` counts every queued
   face, not just the nine (`DuelController.PlainCheck.cs:484-487`). A note, not an assert.
8. **A whole-card foil hides a plain name in the editor (CONFIRMED by eye, cosmetic).** In the 90-card editor shot, the
   "Swords of Revealing Light" deck tile is under heavy sparkle foil and its name can't be read (`compare/odd-tile.png`).
   The spec keeps the text box readable under whole-card foils; the name plate isn't protected.

## 7. Steps that could pass with their feature broken, or assert less than the spec

`-plaincheck` (`DuelController.PlainCheck.cs`):

1. Step 5's "every rarity family on plain cards" row uses codes that have test pictures at that moment: Super Rare, Ghost,
   Gold and Collector's Rare are shown on pictures, not plain cards, and the row is a shot only (no assert). The step
   also compares a plain Normal with a pictured Effect, not "the same Ultra Rare" as the spec says.
2. Step 6 doesn't check that Import Cards' own test pictures stay pictures (spec step 6).
3. Step 12 simulates the screen with `DebugScale`; it passed, and the real large window passed it too, but step 1 is what
   failed there.
4. Step 9's pack is drawn on the check's own page, not in `ShopScreen`; the shop's real reveal is covered only by the
   audits' "no blank" and count.

The audit (`DuelController.FaceAudit.cs`, used in `DuelController.cs`, `.Shop.cs`, `.ThemeCheck.cs`):

5. It sees only tagged faces and untagged `CardView`s. A slot that draws no face and isn't a `CardView` (a bare
   `RawImage`) is caught only by the count floor, and several floors are 1 or 0: Card Trader in `-themecheck` (0, an
   empty wishlist), "a box's summary" (0, correct: face-down), recipe book, locker, strip, history drawer, structure
   decks, slab, reading view and showcase (1).
6. `DebugFlat` calls a face "not blank" when under 30% of its grid is visible; a mostly clipped blank face passes.
7. `-campaigncheck`'s pack marks are compared with marks rebuilt from the shop's own captions through the same
   `RarityShort`, so a wrong mark table would pass there; `-shopcheck`'s seeded DB1 pack derives its wanted marks from
   `PackCatalog.Open` and is the stronger one. A missing mark fails both.
8. The audit never runs with pictures installed, by design; the with-pictures runs only log skips.

Each other step I read (plaincheck 1-4, 7-11, 13-16; the audit's count, blank and problem rules) asserts what it says.
I didn't revert features to prove failures; this is from reading the code against the spec.

## 8. Awkward inputs

| Input | How | Result |
|---|---|---|
| A few pictures (2 generated flat-colour PNGs named 423705 and 57046845) | `-pics awk/two-pics -hdpics off`: `-menucheck`, `-layoutcheck`, `-editor` | Title wall (under 12 pictures) mixes one test picture with plain cards; the main menu before a campaign shows a **test picture** as the showcase; audits log skipped (pictures installed). 0 FAIL. The layout duel's hand held neither code, so no mixed hand in a real duel (`-plaincheck` step 7 has a mixed row) |
| `-pics` path that doesn't exist | `-menucheck` | Treated as no pictures (the EDOPro search is skipped): audits ran, 8 ok |
| `-hdpics` an empty folder (with `-pics` empty) | `-menucheck` | 8 ok, audits ran |
| Card art styles with no pictures | `-stylecheck`, `-pics` empty | 19 ok, 0 FAIL |
| A deck with an unknown code, 60 + 15 + 15 | `awk/ninety.ydk` copied to the run's `mods/decks`, `-editor ninety -autoshot` (shot at 6 s) | Opened in 247 ms; all 90 tiles plus 25 results drawn, no blank; id 999999999 shows as a brown "Card 999999999" tile with a "!" badge and the line "Unknown card id 999999999." With 2 pictures, both show as pictures among plain tiles. No exception |
| Any blank one second after opening | | **Not tested**: the game's own shot is at 6 s, and `screencapture` is refused here ("could not create image from display"). `-plaincheck` step 9 measures the cold editor: 8 frames to fully drawn |

My first two `-editor` launches passed a file path, which the editor doesn't take (it opened an empty "new deck"); I
reran them with the deck name. The tables count only the reruns.

## Not tested

- Real mouse, keyboard, controller or focus: all through the checks' test devices. The drag ghost mid-drag wasn't seen.
- A blank one second after the editor opens (above). A Trap thumb in a real chain panel at 3024x1964.
- Sound: nothing heard.
- Retina scaling other than 3024x1964 and 1280/1920 windows; a 3840x2160 screen (only simulated in step 12).
- The rarity mark turning with a tilting face: seen tilted with the face on the main menu, not measured.
- Foil on pictures compared frame by frame with `build/qa-evidence2`; `-artcheck`; `-stylecheck` run twice; `-importcheck`
  with an explicit `-hdpics <HD folder>`; `-hdpics <owner's HD folder>` runs.
- The Windows build. Reverting each feature to prove a step fails (section 7 is by reading).

## Files

- Builds: `build/bin/evidence3/`, `build/qa-evidence3/Duel Circuit.app` (`build.log`)
- Harness logs: `build/qa-evidence3/harness/`
- App checks: `build/qa-evidence3/checks/<run>/{log.txt,args.txt,out/}`, `checks/summary.txt`; `run.sh`, `queue1.sh`,
  `queue2.sh`, `editor.sh`
- Awkward inputs: `build/qa-evidence3/awk/` (two test pictures, `ninety.ydk`, an empty HD folder); crops in
  `build/qa-evidence3/compare/`
