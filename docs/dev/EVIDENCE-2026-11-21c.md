# Evidence pass (2026-11-21c): type scale and fonts, parts R2 and R3

Evidence Collector (Opus), on the working tree at HEAD 96d5aea (R2 committed, R3 uncommitted), for
[TYPE-AND-FONTS-SPEC-2026-11-21.md](TYPE-AND-FONTS-SPEC-2026-11-21.md). Tested and reported, nothing fixed. The only repo
file I wrote is this one. Output is under `build/qa-type-ev/` and `build/bin/type-ev/`. Every run but `-menucheck` was
muted; nothing was heard.

## Result in short

- Harness: all pass (`--texts-check`, `--campaign-test`, `--deckbuilding-test`, `--games 3`).
- 51 app runs on one fresh build (queue plus reruns and awkward inputs): 4,471 ok lines, 10 FAIL lines, 0 Exception lines,
  every run exited 0. All 44 runs the brief asked for finished; 42 of them with 0 FAIL.
- The FAILs: `-layoutcheck` at 1600x900 (a window size no check uses) fails the long yes/no step, twice in a row (finding
  1); `-tagcheck` failed once on a timing step, the rerun passed (finding 4); 7 text-rule FAILs come from my deck named
  with rich-text tags (finding 2); 2 expected FAILs from running `-campaigncheck` on a copy of the owner's save (finding 5).
- By eye at 1280x720, 1920x1080 and 3024x1964: no cut name, no foil over a name, every small Spell/Trap tile has its footer.
  Editor deck-tile names at 720 are 7 px: readable up close, small.

## 1. Builds

| Build | Command | Result |
|---|---|---|
| Harness | `dotnet build src/Ygo.Harness -c Release -o build/bin/type-ev` | 0 errors. No `bin/` or `obj/` left under `src/` |
| Game | `Unity -batchmode -quit -projectPath unity -buildTarget OSXUniversal -executeMethod ProjectSetup.BuildMac -buildout "build/qa-type-ev/Duel Circuit.app" -logFile build/qa-type-ev/build.log` | `Build Finished, Result: Success.`, exit 0, 22:45 |

The build log shows "Recompiling scripts for player build"; the app's `Assembly-CSharp.dll` is from 22:45. The newest
scripts are `DuelController.PlainCheck.cs` (21:51), `CardFoil.cs` (21:50), `DuelController.cs` and `PlainCards.cs`
(21:49). No file under `unity/Assets`, `src/Ygo.Engine` or `campaign/` is newer than the build log.

## 2. Harness (`build/qa-type-ev/harness/*.log`)

| Check | Result |
|---|---|
| `--texts-check` | exit 0, `All texts checks passed.` (corpus 44 characters, hash a4b38e3d794b) |
| `--campaign-test` | exit 0, `All campaign checks passed.` |
| `--deckbuilding-test` | exit 0, `All deck-building checks passed.` |
| `--games 3` | classic 1, modern 2, 0 script errors, 0 crashed |

## 3. App checks (`build/qa-type-ev/checks/<tag>/{log.txt,out/,args.txt}`, runner `build/qa-type-ev/run.sh`, queue `queue.sh`, summary `checks/summary.txt`)

One run at a time, a fresh absolute `-mods`, absolute `-logFile` and out folder, `-screen-width W -screen-height H
-screen-fullscreen 0`, `-mute` except `-menucheck`. "No pictures" = `-pics <empty folder> -hdpics off`; "with pictures"
= neither flag (the machine's own pictures).

### No pictures, 1280x720

| Run | ok | FAIL | Audit |
|---|---|---|---|
| plain | 38 | 0 | |
| editor | 74 | 0 | |
| search | 89 | 0 | |
| focus | 94 | 0 | |
| layout | 89 | 0 | 7 ran, 0 skipped |
| campaign | 356 | 0 | 12 ran, 0 skipped |
| type | 58 | 0 | |
| menu (unmuted) | 15 | 0 | 2 ran, 0 skipped |
| shop | 117 | 0 | 4 ran, 0 skipped |
| theme | 107 | 0 | 10 ran, 0 skipped |
| alley | 230 | 0 | |
| climb | 22 | 0 | |
| tag | 10 | **1** | |
| tag (rerun) | 11 | 0 | |
| about | 39 | 0 | |
| replay | 18 | 0 | |
| board | 24 | 0 | |
| pile | 21 | 0 | |
| controls | 40 | 0 | |
| sound | 51 | 0 | |
| click | 26 | 0 | |
| style | 19 | 0 | |
| fxshots | no step lines (24 shots) | 0 | |
| layout `-textsize 2` | 89 | 0 | 7 ran, 0 skipped |
| editor `-textsize 2` | 74 | 0 | |

### No pictures, 1920x1080 and 3024x1964

| Run | 1920x1080 ok / FAIL | 3024x1964 ok / FAIL |
|---|---|---|
| plain | 38 / 0 | 38 / 0 |
| editor | 74 / 0 | 74 / 0 |
| search | 89 / 0 | 89 / 0 |
| focus | 94 / 0 | 94 / 0 |
| layout | 89 / 0 | 89 / 0 |
| campaign | 356 / 0 | 356 / 0 |
| type | 58 / 0 | 58 / 0 |

### With pictures, 1280x720

| Run | ok | FAIL | Audit |
|---|---|---|---|
| plain | 6 | 0 | (step 17 and the pictured-faces step only, as designed) |
| editor | 74 | 0 | |
| layout | 82 | 0 | 0 ran, 7 skipped |
| campaign | 344 | 0 | 0 ran, 12 skipped |
| shop | 113 | 0 | 0 ran, 4 skipped |
| import | 65 | 0 | |

### Window sizes no check uses, no pictures

| Run | ok | FAIL |
|---|---|---|
| layout 1600x900 | 88 | **1** |
| layout 1600x900 (rerun) | 88 | **1** |
| editor 1600x900 | 74 | 0 |
| layout 1152x648 | 89 | 0 |
| editor 1152x648 | 74 | 0 |

### Awkward inputs

| Run | ok | FAIL | What |
|---|---|---|---|
| weird-editor | 67 | **7** | `-editorcheck` with three oddly named decks in `mods/decks` |
| weird-plate-1/2/3 | (screenshot only) | | `-p0 vera -deck0 <odd name> -p1 anselm -deck1 classic -autoshot` |
| owner-campaign | 248 | **2** | `-campaigncheck` on a copy of the owner's save |

## 4. What I looked at (by eye)

At 1280x720 without pictures:
- Every `-editorcheck` shot (`checks/editor-720/out/*`) and every `-plaincheck` shot (`checks/plain-720/out/*`). Deck tiles
  are name-only faces; the long names wrap to four lines or end in "…" (`crops/ed720-decktiles.png`,
  `crops/plain720-ed-row1.png`). Result tiles are badged Compact faces with no name repeated under them. The sheets show
  every kind; Compact name-only, Compact, Standard without text and Full all have their names whole. Small Spell/Trap
  tiles keep the SPELL/TRAP footer (`plain-sheet-compact.png`, `plain-board-atlas.png`).
- Main menu and spotlight (`menu-720/out/menu-main.png`), Settings (`menu-settings.png`), campaign screen
  (`campaign-720/out/campaign-circuits.png`), a pack reveal (`campaign-pack-revealed.png`, `shop-720/out/shop-5-pack.png`),
  the versus (`campaign-versus.png`), the shop (`campaign-shop.png`), the grading desk and its slab reveal
  (`campaign-grading-desk.png`, `campaign-slab-reveal.png`), Moth's van and its detail slab
  (`shop-720/out/shop-10-van-keys.png`, `crops/van720-detail.png`), the board with a callout
  (`campaign-first-duel-field.png`), each long prompt (`layout-720/out/layout-prompt-*.png`), the bracket
  (`campaign-bracket.png`), the alley's Orvel panel (`alley-720/out/alley-10-orvel-panel.png`), the fx shots
  (`fx-720/out/fx-negated.png`, `fx-set-this-turn.png`).
- The van's detail slab now draws a Standard face without text (name, glyph, rarity mark, TRAP); in the R2 build it drew
  a text box at about 5 px (`crops/cmp-r2-van-keys.png` against `crops/ev-van-keys.png`). Foil sits under, not over, the
  name on every foil face I looked at.

With pictures at 1280x720: the editor (`editor-720-pics/out/editor-mark.png`: pictured result tiles keep a line under
them, plain ones don't), a pack reveal (`campaign-720-pics/out/campaign-pack-revealed.png`: pictures and one plain face
side by side), the van (`shop-720-pics/out/shop-10-van-keys.png`), the board (`layout-720-pics/out/layout-board.png`).

At 3024x1964: the editor (`editor-1964/out/editor-no-limits.png`), the board (`layout-1964/out/layout-board.png`,
`crops/l1964-hand.png`), the campaign screen (`campaign-1964/out/campaign-circuits.png`), a pack reveal
(`campaign-1964/out/campaign-pack-revealed.png`: packs at 271 px show their text box, as the spec says).

With `-textsize 2` at 1280x720: the hand (`crops/ts2-hand.png` against `crops/ts0-hand.png`: the Level digit grows, its
box grows with it, not cut), the card detail (`layout-720-ts2/out/layout-detail.png`, `crops/ts2-detail.png`), a prompt
(`layout-prompt-yesno-long.png`, `layout-prompt-options-larger.png`).

## 5. Findings, ranked

1. **`-layoutcheck` fails at 1600x900, every time: "a yes/no over 1187 characters of card text keeps all of it (... size
   14, scrolls False)".** CONFIRMED, two runs (`checks/layout-900/log.txt`, `checks/layout-900-rerun/log.txt`). The shot
   (`checks/layout-900/out/layout-prompt-yesno-long.png`) shows the whole text, ending "THE-LAST-WORDS", at 14 without a
   scrollbar: at that window size it fits. The player sees nothing wrong. The step demands a scroll
   (`DuelController.cs:5347-5349`, `&& ynScrolls`), so it fails whenever the text happens to fit. A check bug, not a
   game bug; it would also fail on any future layout that gives the dialog more room. Medium (a red check that cries wolf).
2. **A deck name with rich-text tags is drawn as markup.** CONFIRMED (`checks/weird-editor/log.txt`, 7 FAIL lines; shot
   `checks/weird-editor/out/editor-mark.png`). The deck `a <b>bold <size=60>big <color=red>deck` clips the editor's
   "with ..." opponent line under the test-against-a-bot block: "with a bold big deck (15 of 43 characters cut by its
   284x30 box)". The Decks list and the duel plate show the tags as literal text (`crops/plate-2.png`). File names are not
   escaped before they reach a rich-text `Text`. Low (a player has to name a deck that way), but the text rule catches it.
3. **The emoji in a deck name vanishes.** CONFIRMED (`crops/plate-3.png`: "Dragons  deck", two spaces; Decks list in
   `checks/weird-editor/out/editor-esc-all-open.png`). Known limit (DEVELOPING.md, "Symbols and other scripts"); nothing
   breaks. Low.
4. **`-tagcheck` failed once: "the tag duel is under way (1 swaps, turn 2)".** CONFIRMED once, not reproduced
   (`checks/tag-720/log.txt`; rerun `checks/tag-720-rerun/log.txt`, 3 swaps, ok). The shot
   (`checks/tag-720/out/tag-duel.png`) shows a long frog loop in turn 2 that ate the 60 s window. Timing in a random
   lineup, not R2 or R3. Low.
5. **The owner's save loads.** Copy at `build/qa-type-ev/owner-copy/mods` (the original only read). `-campaigncheck` on
   it draws the campaign screen with 9,563 DP, Level 16, 10/22 circuits, deck "Chaos - Yata-Lock (recipe)"
   (`checks/owner-campaign/out/campaign-starters.png`). Its 2 FAILs are the check assuming a fresh save ("no save yet",
   and a trade the owner's collection already has). A plain launch on the copy started and drew the title, but I could not
   click into it (the window opened on another Space). Not a finding against the build.
6. **The board's card detail at Larger cuts its text at the box's bottom with no "more" cue.** Seen, PLAUSIBLE as a
   player complaint (`crops/ts2-detail.png`): "faced this awesome / creature and lived to tell the" is sliced mid-line.
   The text scrolls (a mask, not a cut), so the rule passes it; the "▼ more" hint is the deck editor's only. Probably
   older than R2. Low.
7. **The hand's big Level digit sits over the "LV n" chip text.** Seen at every size and in the R2 build too
   (`crops/l1964-hand.png`, `crops/cmp-r2-l1964-hand.png`, `crops/ts2-hand.png`). Two Level readings, one overlapping the
   other. Not new. Low.
8. **DEVELOPING.md is stale.** It still says R3 "is not built" and lists four R3 exclusions in the text rule; the code now
   excludes only the developer layout (`DuelController.TextRule.cs:25-30`). For the Technical Writer.

No cut, doubled or overlapping card name, no foil over a name, no missing SPELL/TRAP footer, and no empty tooltip in any
shot I opened.

## 6. Would the tests catch a break?

| Feature | Caught? | Evidence |
|---|---|---|
| The 14 floor for shell text | Yes | Pre-R2 runs fail it hundreds of times (`build/qa-type2/checks/base-campaign-720/log.txt` 118 FAIL, `base-theme-720` 65, `base-alley-720` 42). Self-test probes Small/Shrunk/Nested/Run, `TextRule.cs:244-278` |
| 7 / 8 px floors on cards | Yes, on drawn textures | `PlainCheck.cs:128-133` over 741 faces at 13 widths. Live strip and badges: `CardText` judged in screen px, `TextRule.cs:133-137`, self-test CardSix/CardSeven `TextRule.cs:272-273`. The px values come from the renderer's own generator (`PlainCards.cs:988-997`), not from pixels |
| Variant at 52 and 200 px | Yes | Expected variant worked out independently of `VariantFor` (`PlainCheck.cs:121-124`), at 41/52 and 199/200 plus the cache test at 199/201 (`:168-172`) |
| Variant at 70 and 150 px | Half | Only the upper side is asserted: chip words from 70 (`:148`), type line from 150 (`:141`). Below, nothing asserts they drop; only the floors would |
| Names never dropped | Yes | `PlainCheck.cs:126`, every known card at every width; long name at 41 px `:156-164` |
| Names under pictured editor tiles | Yes, only with pictures | `EditorCheck.cs:276-285`; bites in `build/qa-type3/checks/q-ed-pics/log.txt` (11 pictured). In a no-pictures run it is vacuous (0 pictured). It checks for a line, not that the line is the name |
| Foil not over a name | Partly | The table (`PlainCheck.cs:174-193`) passes the variant itself, so it would not catch `CardFoil` dropping it. Step 5 (`:487-493`) checks the material's rects on a live face; it bites at 1280x720 and 1920x1080, where that face is StandardNoText, not at 3024x1964. Nothing checks the foil after a face is redrawn for a new width |
| Card text size reaching prompt options | Yes | `DuelController.cs:5360-5377` asserts 26 at Normal and 36 at Larger |
| Orvel's stake lines keep both DP values | Yes | `AlleyCheck.cs:472-492`, including a built 171-character line; the pre-fix cut is in `base-alley-720/log.txt` ("stakes: the entr…") |
| Prompts don't cut silently | Yes, too strictly | `DuelController.cs:5331-5359`. It demands a scroll, so it fails where the text fits (finding 1) |
| Sideways spill | Yes | Self-test Sideways/Neighbour, `TextRule.cs:266-269, 278` |
| Glyph coverage | Yes | `-typecheck`: U+E8FF must be missing (`TypeCheck.cs:111`); harness `--texts-check` fails a stale corpus hash |

Features with no test that would catch their break:
- A live face drawn for the wrong width (a scaled parent misread) at a size the checks don't shoot. Step 5 checks one face;
  the face audit counts faces, not variants.
- Foil regions after a face is redrawn at a new width (a window resize).
- Deck and file names that carry markup (finding 2): only my scratch run found it.
- The board's card detail at Larger (finding 6): masked text passes the rule.

## 7. Not tested

- Audio is never heard (every run muted except `-menucheck`, which no one listened to).
- Windows: no run.
- A real 4K or Retina screen at its native size: the 3024x1964 runs are a window of that size.
- Clicking through the app on the owner's save copy (window on another Space); the check run drew its campaign screen.
- `-finalcheck`, `-laddercheck`, `-musiccheck`, `-matcheck`, `-ordercheck`, `-versioncheck`, `-artcheck` were not run.
- Runs with pictures at 1920x1080 and 3024x1964.
