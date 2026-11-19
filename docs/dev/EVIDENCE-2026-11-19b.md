# Evidence pass (2026-11-19b): final pass before the commit

Evidence Collector (Opus), on the whole uncommitted tree: the card refresh and printings importer fix
([CARD-REFRESH-2026-11-19.md](CARD-REFRESH-2026-11-19.md)), the alley, partner and hyphenation fixes
([NEXT-BUILD-2026-11-19.md](NEXT-BUILD-2026-11-19.md), first pass in [EVIDENCE-2026-11-19.md](EVIDENCE-2026-11-19.md)),
and the deck editor's click to focus and change signals ([EDITOR-CLICK-FOCUS-SPEC-2026-11-19.md](EDITOR-CLICK-FOCUS-SPEC-2026-11-19.md)).
Tested and reported, nothing fixed. No tracked file was edited: `git diff --stat` plus `git status --short` are the same
before and after. All output is under `build/qa-final2/` and `build/bin/final/`. `build/windows` wasn't touched.

## Result in short

- Every harness check passes. All 23 app runs pass with **0 FAIL** and no exception in any log.
- The alley FAIL from the first pass (step 13, CRLF) is gone. The check now trims `\r`, and this run's save has no CR
  at all.
- Nothing I found should block the commit. I found five small things, all cosmetic or a matter of wording. The known
  nit is real: after Sort, the status line still says "Added Pot of Greed (2 of 3)."
- Five check steps are weaker than they look. One passes for any code ("closing the editor leaves no message").

## 1. Builds

| Build | Command | Result |
|---|---|---|
| Harness | `dotnet build src/Ygo.Harness -c Release -o build/bin/final` | 0 errors, 7 warnings (old). No `.cs` under `src/` is newer than the DLLs |
| Game | `Unity.exe -batchmode -quit -projectPath unity -buildTarget Win64 -executeMethod ProjectSetup.BuildWindows -buildout "E:/Projects/duel-circuit/build/qa-final2/Duel Circuit.exe" -logFile .../build/qa-final2/build.log` | `Build Finished, Result: Success.`, exit 0 |

The game's `Assembly-CSharp.dll` was built at 12:22:17. The newest script, `DuelController.FocusCheck.cs`, is from 12:12:09.

## 2. Harness (`build/qa-final2/harness/*.log`)

| Check | Result |
|---|---|
| `--campaign-test` | All campaign checks passed. |
| `--deckbuilding-test` | All deck-building checks passed. |
| `--texts-check` | All texts checks passed. |
| `--copies-test` | All copy checks passed. |
| `--nolimits-test` | All No Limits checks passed. |
| `--arbitrage` | "No buy -> sell loop is DP-positive." Every exhaustive pass reports 0 DP-positive (2,379, 1,463, 22,620 and 88,740 sequences); no FAIL |
| `--games 30` | classic 8, modern 22; script errors 0, crashed games 0 |
| `python tools/check_history_decks.py` | 0 problems |

## 3. App checks (`build/qa-final2/checks/<name>/`, runner `build/qa-final2/run-checks.sh`)

The checks ran one at a time, each with a fresh absolute `-mods` and absolute `-logFile`/out paths (`cygpath -w`).
Every run had `-mute` except menu, and `-hdpics off` except import (import makes its own flat-colour pictures, as in the
first pass). No run used `-batchmode` or `-nographics`. The window was 1280x720 (`-screen-width 1280 -screen-height 720
-screen-fullscreen 0`), except focus1080. The drag and controller tests ran as `-editor classic -editor-dragtest` /
`-controllertest` with `-autoshot`.

| Run | ok | FAIL | Time |
|---|---|---|---|
| focus (1280x720) | 71 | 0 | 24 s |
| focus (1920x1080) | 71 | 0 | 23 s |
| editor drag test | 5 | 0 | 14 s |
| editor controller test | 8 | 0 | 14 s |
| board | 25 | 0 | 41 s |
| tag | 6 | 0 | 65 s |
| pile | 21 | 0 | 24 s |
| controls | 40 | 0 | 19 s |
| order | 10 | 0 | 11 s |
| layout | 54 | 0 | 47 s |
| replay | 16 | 0 | 28 s |
| mat | 8 | 0 | 87 s |
| menu (sound on) | 6 | 0 | 17 s |
| campaign | 189 | 0 | 215 s |
| final | 46 | 0 | 336 s |
| theme | 31 | 0 | 48 s |
| shop | 89 | 0 | 51 s |
| alley | 129 | 0 | 283 s |
| search | 61 | 0 | 27 s |
| import | 65 | 0 | 16 s |
| about | 30 | 0 | 15 s |
| click | 26 | 0 | 23 s |
| ladder | 42 | 0 | 176 s |

All exited 0, and no log has an exception. Nothing failed, so nothing needed a rerun. The focus check logs
`InputSettings.scrollDeltaBehavior = UniformAcrossAllPlatforms` (one wheel notch reads 1).

## 4. The -focuscheck screenshots (16 at 1280x720, 16 at 1920x1080)

| Shot | What I see (both sizes unless said) |
|---|---|
| open | New deck, the first result (30,000-Year White Turtle) ringed in white, shown on the left. Status: "New deck. Search on the right: click a card to see it, click it again to add it." The three help lines sit under the results |
| hover | The left panel stays on Dark Magician. Main 4 (the last card pointed at) is lifted; nothing else changed |
| ring-result | A white ring with a dark gap round Dark Magician Knight (result 2), outside the blue border. The left panel shows DMK |
| pill-add | The new Main 8 tile has a green inner edge (checked at 3x). The DMK result has gold "in deck" and "x1" inside the white ring. "Main 9 (+1)" is in green, and the pill "+ Dark Magician Knight" has a green edge and a blue **Undo**. Status: "Added Dark Magician Knight (1 of 3)." |
| pill-remove | The ring is on the other Sangan (Main 2), not on MST, which slid into slot 0. The Sangan result has a red edge outside its gold (checked at 4x). "Main 7 (−1)" is red; the pill "− Sangan" has a red edge and Undo |
| ring-deck | A 1 + 3 px ring round Main 0 Sangan |
| pill-move | MST is in the Side Deck with a white ring. "Main 5 (−1)" and "Side 1 (+1)"; the pill "Mystical Space Typhoon → Side" sits beside the Side header with a white edge |
| pill-refused | "You already have 3." in a red-edged pill with no button. Status: "You already have 3 copies of Pot of Greed." The deck is unchanged |
| pill-undo-clear | "Undone: Clear (6 cards)" with **Redo**, an Info-blue edge, "Main 6 (+6)", no tile flashes |
| text-hint | D/D/D/D Super-Dimensional Sovereign Emperor Zero Paradox: the text fades at the bottom, with "▼ more" and a thin scrollbar |
| text-glow | Scrolled down by three notches; the text box's edge glows white |
| ring-hand | Test hand open, a 4 px ring round hand card 1 (MST). The "+ Sangan" pill is hidden; the deck headers' counts still show above the overlay |
| pill-bulk | "Sorted" pill with Undo; "Main 6 (+1)". **Status line: "Added Pot of Greed (2 of 3)."** (the known nit, confirmed) |
| hand-open | Test hand open over the deck. **Main 0's white ring shows as a bracket at the overlay's top-left corner** (finding 2) |
| hand-open-extra-message | Test hand open after an Extra Deck add. The pill is hidden; the Extra tile below the overlay keeps its green edge |
| ring-not-owned | Campaign deck, "dragon" search. Unowned results are dimmed brown with "not owned"; Dragonmaid Cehrmba has a full-brightness white ring. The left panel says in red where to get it |

The Back Alley theme: the theme check's `theme-back-alley-editor.png` shows the ring (`#EDE6DA`) round the first result,
clearly visible. No check shows a change message (pill) in the Back Alley theme; its colours are the same Good/Bad/Info
as Main Street over `PanelDeep #161210` (code only).

### Findings (none blocks the commit)

1. **The status line after Sort or Clear is stale (CONFIRMED, both sizes).** `pill-bulk.png`: the pill says "Sorted"
   while the status says "Added Pot of Greed (2 of 3)." The Sort and Clear buttons call `Change(...)`, which signals but
   never calls `SetStatus` (`DeckEditor.cs:793`, `ClearDeck` at `DeckEditor.cs:1478`). The Sort button never set a
   status before either; it shows now because the pill contradicts it. Low.
2. **The deck ring peeks out from under the test hand (CONFIRMED, both sizes).** `hand-open.png`: with a deck card in
   the top-left slot focused, its ring (4 to 5 px outside the card) draws as a white L along the overlay's top and left
   edges. It's new with the ring. Cosmetic, low.
3. **"▼ more" touches the last line of text (CONFIRMED at 1920x1080).** It's drawn over the faded line with no gap
   ("…Sovereign▼ more"). Cosmetic.
4. **Enter on a Side Deck copy adds a copy to Main, not to the Side (by code and check log).** `KeyAdd` always calls
   `Add(info, side: false)` (`DeckEditor.Focus.cs:385`), and focus step 9 asserts it ("Enter adds one more copy
   (Main 5)"). The spec's "Add one more copy" doesn't say where. A wording/design question, not a bug.
5. **Four fast clicks on a deck card take out two cards (by code).** Click 1 arms, 2 takes it out, 3 arms the card that
   slid into the gap, 4 takes that one out. PLAYING.md's "an extra click never adds or takes out a card you didn't mean"
   holds for one extra click, not for two. Designed behaviour; worth knowing.

Old and not part of this change: in the campaign results the captions run together ("own 2 · ✦ Near Mint own 1 · ✦
Near Mint").

## 5. -focuscheck steps that could pass with their feature broken

Read against `DuelController.FocusCheck.cs`:

1. **"closing the editor leaves no message" passes for any code (CONFIRMED).** Line 734 calls
   `_editor.DebugClearSignals()`, which runs `ClearFeedback()`, right before `_editor.Close()` (735). The message is
   gone before Close runs, so the step can't fail even if `Close` stopped clearing. "Another deck" is covered only by
   `DebugNewDeck` (step "a new deck clears the signals"); `LoadDeck` (opening a deck from the list) has no step. By
   code it calls `ClearFeedback` and clears the undo stack.
2. **Keys typed into text fields: the Delete, Backspace and Page Down parts are weak.** The focus is on a result (Pot
   of Greed, a one-page search) while they're pressed. Delete and Backspace do nothing to a result, and Page Down
   changes nothing visible on a single page, so those keys pass even unguarded. Enter and the arrows carry the step.
   They do bite (the guard is all or nothing). `s`, `a` and `-` aren't editor keys at all.
3. **Ring, edges, glow, "▼ more", slide and shake are read from the editor's own data** (`DebugRing`, `DebugEdges`,
   `DebugScroll`, `DebugDeckTile.dx`), not from pixels. A ring hidden behind something or an edge under a mask would
   still pass. The screenshots above are the only proof they draw (they do).
4. **Sounds are the requested-sound log** (`editor sound: ... (interface volume)`), not the mixer. They prove the
   right clip, volume and `ui: true` flag were asked for, not that the interface volume or mute applies.
5. **The Enter-in-a-text-box steps are simulated.** The check calls `onSubmit` and `DeactivateInputField` itself (the
   test keyboard makes no IMGUI events, as the As built says). If the real `InputField` ordering differs, the one-frame
   grace is untested in reality.

Also: the Page Down step has a fallback branch (results ≤ 25) that only asserts page 0. It didn't run here ("dark
magician" gives 99 results).

## 6. Awkward inputs (by checks and code; I can't drive the real mouse)

| Input | How | Result |
|---|---|---|
| Very fast repeated clicks, results | Code (`ClickResult`) plus focus step 2 | Click 1 focuses and arms, every later click adds; the 4th copy is refused with a shake and a red pill. A double-click adds exactly one |
| Very fast repeated clicks, deck | Code (`ClickDeck`, `RefocusAfterRemoval`) plus steps 5 and Fix 5 | A double-click takes out one; the refocus isn't armed, so a 3rd click only arms the card that slid in. A 4th takes that out (finding 5). During the 0.15 s slide the slot is briefly empty, so a click there hits no card |
| Undo/Redo of merged messages | Steps "×2 Undo / Redo / two Undos", "×3 Undo" | Pass. By code: a merge needs the same kind, card and section within 2 s, and the pill must still exist; any other change, Undo or refusal replaces the pill and resets the merge. The toolbar Undo after a ×N message undoes one step and gives its own "Undone: …" pill |
| Opening another deck mid-feedback | Code (`LoadDeck` at `DeckEditor.cs:1726`) plus the NewDeck step | `ClearFeedback` and both undo stacks are cleared, so a pill's Undo can't act on the other deck. The pill hides while the deck list is open (`Covered`). Not run through the deck list |
| Back Alley theme ring and pill | `theme-back-alley-editor.png` plus `UiTheme.cs` | The ring shows in `#EDE6DA`. The pill was never shown in that theme; same edge colours by code |
| Duels keep hover | Code | `CardView.EditorMode` is called only from `DeckEditor.cs` (3 places). No check step looks at a duel card's hover detail; the board, layout, click and pile checks pass |

## Not tested

- Real hand input (mouse, keyboard, controller): everything went through the Input System test devices or code reading.
- The change message (pill) in the Back Alley theme; opening a deck from the deck list mid-feedback; the real
  `InputField` Enter path.
- Sound wasn't heard: checks run muted, menu passed its output-level sound checks.
- The Mac app.

## Files

- Builds: `build/bin/final/`, `build/qa-final2/Duel Circuit.exe` (`build.log`)
- Harness logs: `build/qa-final2/harness/` (`summary.txt`)
- App checks: `build/qa-final2/checks/<run>/{log.txt,out/}`, `checks/summary.txt`; runners `build/qa-final2/run-checks.sh`, `run-all.sh`
