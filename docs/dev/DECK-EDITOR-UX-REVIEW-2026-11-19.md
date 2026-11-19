# Deck editor UX review (2026-11-19)

Part U of [NEXT-BUILD-2026-11-19.md](NEXT-BUILD-2026-11-19.md). A review, not a build: no code changed.

**Question from the owner:** is getting around the deck editor good for building decks and finding cards, and is the
screen usable overall?

## Verdict

The editor is strong where it counts most: adding, moving and removing cards is fast (click, right-click, drag with lit
drop zones, undo for everything), the whole deck is visible at once, and search is far richer than most fan tools
(forgiving names, 10 quick chips, 30 "what it does" genres, archetypes, Seen in duels). It loses players in three
places. **Leaving with unsaved changes can lose work** after a warning most people won't see. **Legality is checked
against "No limits" by default**, so a deck with 3 Pot of Greed reads green "legal", and picking a real list means
stepping through about 47 lists one at a time. **Text is too small at 1280x720**, and since the game ships with no card
pictures, the tiny name on each tile is how most players tell cards apart. Behind those: results come 25 at a time
with Prev/Next only (80 pages, no wheel), a plain two-word search ranks badly, there are no keyboard shortcuts, and a
controller can't type. None of these needs a redesign to fix; most are quick fixes.

## How it was looked at

- Code read: `DeckEditor.cs`, `DeckEditor.Filters.cs`, `DeckEditor.Versions.cs`, `DuelController.SearchCheck.cs`,
  `CardSearch.cs`, plus `CardDetail.cs` and `ControllerSupport.cs` where a finding needed them. Docs: PLAYING.md's editor,
  search and controls sections, CARD-SEARCH-SPEC, UI-THEME, UI-ROADMAP.
- App runs in `build/qa-ux` (a copy of the 2026-11-11 check build), one at a time, fresh `-mods` each, `-hdpics off`
  (no card pictures: every shot is the text look). Output is under `build/qa-ux/runs/<name>/`:

| Run | Size | Result |
|---|---|---|
| `search` (`-searchcheck`) | 1920x1080 | 61 ok, 0 FAIL, 15 shots |
| `search-720` (`-searchcheck`) | 1280x720 | 61 ok, 0 FAIL, 15 shots |
| `version` (`-editor chaos-yata -versioncheck`) | 1920x1080 | 20 ok, 0 FAIL, 8 shots |
| `drag-1080` (`-editor modern -editor-dragtest`) | 1920x1080 | 4 ok, shot mid-drag |
| `pad-1080` (`-editor modern -controllertest`) | 1920x1080 | 6 ok, shot with the hint bar |
| `hand-1080`, `hand-720` (`-editor-hand`) | both | test hand shots |
| `search-1080` (`-editor-search "destroy all"`) | 1920x1080 | results shot |
| `deck-720` (`-editor modern`) | 1280x720 | shot, plus 2x crops `crop-deck.png`, `crop-help.png` |

- The campaign catalog: the existing campaign and .ydk check shots, copied with the build into
  `build/qa-ux/checks/campaign/out/` and `build/qa-ux/checks/ydk/out/` (3840x2160; same build). I didn't rerun
  `-campaigncheck`.
- Not reported here (Part B is fixing them): the count badge covering card names, and the "don't own enough of any
  recipe" message shown twice.

## Findings, ranked by how much they hurt deck building

Severity: **High** = loses work or gives a wrong answer about the deck; **Medium** = slows a common task a lot or
blocks one kind of player; **Low** = friction. Status: CONFIRMED (seen in a run or plain in the code) or PLAUSIBLE.

### 1. Leaving with unsaved changes can lose the work (High, everyone; CONFIRMED in code)

- **Back**, **New** and opening a deck from **Decks...** all go through `Confirmed` (DeckEditor.cs:2181-2192). With
  unsaved changes the first click only writes "You have unsaved changes. Click again to discard them." on the status
  line (DeckEditor.cs:787, at x 410, y 98). Back is at x 1270, y 10 (DeckEditor.cs:761), about 860 px away.
- A second click on Back leaves and **deletes the recovery copy** (`Close`, DeckEditor.cs:583). A double-click on Back
  loses the changes for good.
- There's no **Save and leave**, and no sign anywhere that the deck has unsaved changes (no "*" or "unsaved" by the
  name: `_dirty` is never shown).
- Master Duel and Duel Links stop you with a dialog when you leave an edited deck; EDOPro asks too. Here the safety net
  is a line of text.
- Not shot: no check flag reaches this state. Code only.

### 2. Legality is checked against "No limits" unless you change it (High, new players and veterans; CONFIRMED)

- A new free-play deck, and any deck without a `#banlist` note, opens under **No limits** (`_banIndex` starts at 0, the
  "No limits" list, DeckEditor.cs:121 and 179; only `LoadDeck` changes it, DeckEditor.cs:1588-1589).
- The problems box then says "This deck is legal under No limits." in green. An imported deck with 3 Pot of Greed and 3
  Graceful Charity reads green "legal" (`build/qa-ux/checks/ydk/out/ydk-free-import.png`); so does `modern` with 3 Pot
  of Prosperity (`build/qa-ux/runs/search-1080/out/shot.png`).
- Picking a real list: the **< >** arrows step through about 47 lists one at a time (No limits, 5 current lists, 41
  historic TCG lists; DeckEditor.cs:784-786, 3016-3027). EDOPro, Master Duel and YGOPRODeck all open on the current
  list and offer a menu to pick another.
- The campaign is different: a circuit passes its list. Its catalog shows "No limits" outside era-limited play
  (`build/qa-ux/checks/campaign/out/campaign-collection-editor.png`), which is right there, but reads the same as the
  free-play trap.

### 3. Text is too small at 1280x720, and the text look is what most players see (High, everyone below 1080p; CONFIRMED)

- The editor draws a fixed 1920x1080 layout scaled to the window (DeckEditor.cs:622-624). At 1280x720 everything is 2/3
  size. Measured on the shots:
  - Help lines under the results: 13 px at 1080, about 8-9 px at 720 (DeckEditor.cs:902-905;
    `build/qa-ux/runs/deck-720/out/crop-help.png`).
  - The name under each result: 11 px shrinking to 10 (DeckEditor.cs:2701-2706), about 7 px at 720.
  - Deck tile names shrink to fit the 62x90 tile; long names ("Infinite Impermanence", "Pot of Prosperity",
    "Forbidden Droplet") end up about 5 px tall at 720 and can't be read
    (`build/qa-ux/runs/deck-720/out/crop-deck.png`).
  - Chip and picker labels: 12-13 px shrinking to 9 (DeckEditor.cs:836-854), 6-9 px at 720
    (`build/qa-ux/runs/search-720/out/search-more-genres.png`).
- The game ships no card pictures (owner rule), so without Import Cards every tile is the text look. The name on the
  tile is the player's main way to read their deck.
- Settings' card text size (Normal / Large / Larger) reaches the reading view (DeckEditor.cs:981) but not the tiles,
  chips or help.

### 4. A plain multi-word search ranks badly, and the filter chip suggests a phrase search (Medium-High, new players; CONFIRMED)

- Typing `destroy all` (no quotes) returns 870 cards. Page 1 is Ally of Justice Core Destroyer, then Level 12, 11 and
  10 monsters (`build/qa-ux/runs/search-1080/out/shot.png`). Cards that do "destroy all" (Raigeki, Dark Hole,
  Torrential Tribute) aren't on page 1.
- Why: text-only matches are ordered by kind, then **Level, highest first** (CardSearch.cs:317-318). Nothing ranks the
  exact phrase higher.
- The active row shows the box as `"destroy all" ×`, in quotes (DeckEditor.cs:1258). In this search language quotes
  mean "this exact phrase", so the chip says the search is something it isn't.
- Typing `"destroy all"` with quotes works, but the box's placeholder doesn't mention quotes.

### 5. Results come 25 at a time, Prev/Next only, and the count stops at 2000 (Medium, everyone; CONFIRMED)

- 5x5 grid, 80 pages for all cards (DeckEditor.cs:27, 896-898). No mouse wheel, no keyboard paging (no scroll handler
  in the editor). Only the controller's LB/RB turn pages.
- Results are capped at 2000 and the count reads "2000+ cards" (DeckEditor.cs:2595, 2639). A filter that keeps over 2000
  cards gives no feedback at all: Banish or Draw still reads "2000+"
  (`build/qa-ux/runs/search/out/search-more-genres.png`).
- With no search, results open A to Z from "30,000-Year White Turtle" (`search-panel.png`). Building from empty has no
  starting point (see 10).
- EDOPro and YGOPRODeck show results as a scrolling list you can read many names from at once.

### 6. Fixing an illegal deck: the editor says what's wrong but not where (Medium, everyone; CONFIRMED)

- Problems are a text list under the charts, up to 7 lines, not clickable (DeckEditor.cs:2405-2408).
- In the deck, a card over its limit wears the same badge as one within it: the list's limit number ("1"), in amber
  (DeckEditor.cs:2456-2460). Three Sangans under a limit of 1 all show "1"
  (`build/qa-ux/runs/version/out/versions-campaign-shortfall.png`). You find the extra copies by reading names.
- Free play has no Auto-fix; the campaign does (DeckEditor.cs:2013-2020), and it's good.
- The problem lines themselves are good: each names its card and reason ("Sangan: 3 copies, limited to 1 in TCG
  April 2004").

### 7. A controller can't type (Medium, controller players; PLAUSIBLE, from code)

- Search and the Archetype box are text fields. `ControllerSupport` has no on-screen keyboard or text entry
  (ControllerSupport.cs; it only finds a `Selectable` under the cursor, line 333).
- Everything else works on a pad: A adds, X sends to Side, hold A drags, RB pages, B closes the test hand (6 ok in
  `pad-1080`), and the hint bar shows the editor's own buttons (`build/qa-ux/runs/pad-1080/out/shot.png`).
- So a pad player can browse by chips, pickers and the More panel, but can't look a card up by name. Steam's own
  keyboard may cover a Steam Deck; not checked.
- The d-pad jump from the results' left column goes diagonally into the Main Deck (1460,830 to 1365,787 in the log:
  from the 5th results row to the 2nd deck row). Not wrong, but it lands far from where you were.

### 8. No keyboard shortcuts (Medium, mouse-and-keyboard veterans; CONFIRMED)

- The editor reads one key: Esc closes the More panel (DeckEditor.cs:597-602). No Ctrl+S, Ctrl+Z / Ctrl+Y, no key to
  focus search, no Esc for the test hand, deck list, reading view or the editor itself.
- The search box isn't focused when the editor opens (no `ActivateInputField` call): you click it first.
- PLAYING.md's controls table lists no editor keys, which is accurate.

### 9. Filters: the More panel hides the results, words repeat with different meanings, and zero results is a dead end (Medium, new players; CONFIRMED)

- The More panel opens over the results grid (spec section 7). While you pick genres you see only the count, which is
  often "2000+" (finding 5).
- The quick chips and the More genres share words with different meanings: quick "Draw", "Negate", "Banish" are the
  bots' roles; More's "Draw", "Negate", "Banish" are the card database's categories (DeckEditor.cs:32-33;
  `search-more-genres.png`). Two "Draw" buttons can give different results.
- Zero results: "0 cards", an empty grid, no hint which filter emptied it and no "clear all"
  (`build/qa-ux/runs/search/out/search-active-row-long.png`: there the cause is a chip scrolled out of sight past the
  row's 300 px). "Clear these" clears only the More panel's filters.

### 10. Building from empty: no starting point in free play (Medium, new players; CONFIRMED)

- New gives an empty deck, "No limits", and results A to Z (`search-panel.png`). The status line helps ("Search on the
  right and click cards to add them").
- There's no "start from an archetype" or "start from a built-in deck" here. Copy of a built-in deck works, but you
  have to know to open it with Decks... first.
- The campaign is better: Build, Recipe and Auto-fix (`campaign-collection-editor.png`).

### 11. "Seen in duels" is hard to find (Medium-Low, everyone; CONFIRMED)

- It's the 4th state of a small 104x26 button that reads "All cards" (DeckEditor.cs:880-886). Nothing says it's a
  menu. Free play: 3 clicks; campaign: 4 (All, Owned only, Favorites, Seen in duels). Right-click goes back, but
  nothing says that either except the help's line about pickers.
- Once there it works well: the cards in order, "vs Ryo" under each, a duel picker
  (`build/qa-ux/runs/search/out/search-seen.png`).
- Nothing on the results screen leads to it (spec owner decision 1 chose this).

### 12. Saving: three ways to make a new deck, no Rename (Low-Medium, everyone; CONFIRMED)

- Edit the name field and Save: writes a new file, the old one stays (DeckEditor.cs:752, 1740-1765).
- Copy: saves a copy at once and switches to it (DeckEditor.cs:2259-2272).
- Save's "Save as new" bar: only when most of the deck changed or after Import / Build / Recipe (DeckEditor.cs:1608-1619).
- No Rename and no plain "Save as". EDOPro has both. Saving a built-in deck makes your own copy with the same name,
  which is right and the status line says so.

### 13. Smaller things (Low)

- **Free play says "Not owned".** The card detail shows the campaign collection in free play too
  (`CardDetail.CopiesOf`, set for the whole game at DuelController.cs:261). With no campaign, every card reads "Not
  owned" (`build/qa-ux/runs/hand-1080/out/shot.png`), though free play lets you use any card. CONFIRMED.
- **Removing by click is silent.** A left-click on a deck card removes it with no status line (DeckEditor.cs:2453);
  drag-removal says "Removed X." (DeckEditor.cs:1411). Undo covers it. CONFIRMED.
- **Test hand** draws 5 or 6 again and again; there's no "draw the next card". It covers the Main Deck while open,
  closes with Close or B, not Esc (DeckEditor.cs:1417-1427). CONFIRMED.
- **Clear** empties the whole deck with no question (DeckEditor.cs:766). Undo brings it back. CONFIRMED.

## By task

| Task | Steps (mouse) | What's hard | Keyboard | Controller |
|---|---|---|---|---|
| Build 40 from empty | New, then search and click each card (one click per copy) | No start point (10); "No limits" (2); 80 pages (5) | Typing only; no shortcuts (8) | Browse only, can't type (7) |
| Fix an illegal deck | Pick a list (up to ~23 clicks one way or the other), read problems, find copies by name, click to remove | Over-limit copies not marked (6); no free-play Auto-fix | No | Yes, slowly |
| Find a card by what it does | Quick chip (1 click) or More, genre (2-3 clicks) | Panel hides results; count stops at 2000; repeated words (9); phrase ranking (4) | Typed `does:` works | Chips and More work |
| Find a card seen in a duel | Scope button 3-4 clicks | Button doesn't look like a menu (11) | No | Yes |
| Main / Side / Extra | Right-click a deck card, or drag (lit zones) | Fine. Extra cards go home on their own | No | X, or hold A and move |
| Check a ban list | < > on the list name, repeatedly | Default "No limits"; no list menu (2) | No | Yes |
| Switch card versions | Select card, ART < >, CARD TEXT | Clear status lines ("Switched 1 of 3 Sangan...") | No | Yes |
| Test-draw | Test hand, Draw again, Going first / second | No next-card draw | No Esc | B closes |
| Save / save as new / leave | Save; Copy or rename-and-save; Back twice | Unsaved warning far from Back, deletes recovery (1); no Rename (12) | No Ctrl+S | Yes |

## What already works well (keep it)

- **Adding and moving cards.** Click to add, right-click to Side, drag anywhere with lit drop zones, drag back onto the
  results to remove (`drag-1080` shot, 4 ok). Extra Deck monsters go home on their own. As quick as EDOPro.
- **Undo / Redo on every change**, and the recovery bar that offers unsaved work back next time
  (`campaign-editor-recovery.png`).
- **The whole deck on one screen**: 60 Main, 15 Extra, 15 Side, no scrolling, with the split and Level charts.
- **Forgiving search.** "blueeyes", "bleu-eyes" and "dark magican" all land; the status line says "showing close names".
  `dark magician` puts Dark Magician first (`hand-720` shot).
- **Finding by what a card does.** 10 quick chips, 30 genres in plain groups with Match all / any, archetypes with
  member counts, and the card detail's chips as "more like this" (`search-detail-chips.png`). Better than Master Duel's
  and EDOPro's filters.
- **Seen in duels and Recently viewed**, once found, with clear empty states (`search-viewed-empty.png`).
- **Feedback on results**: "x2" in-deck badges, limit badges, favorites stars; in the campaign "own 2 · Near Mint",
  dimmed unowned cards and where to get them.
- **Problems that name the card and the reason**; historic decks opening under their own era's list; version switching
  with exact status lines (`versions-*.png`).
- **Campaign tools**: Auto-fix, Build (warns before replacing a legal deck), Recipe, Ban Passes.
- **Controller basics**: A / X / hold A / LB RB / B / Y / View all mapped, with the editor's own hint bar.
- **Starter odds and the 100-game bot test**: nothing like them in Master Duel or Duel Links.

## Proposed changes

Costs are guesses in agent-days, including a check that fails without the change. All touch the 1920x1080 layout, so
each needs the search, layout and campaign app checks after it.

### Quick fixes (a few hours each)

| # | Change | Fixes | Files | Cost |
|---|---|---|---|---|
| Q1 | Unsaved changes: a dialog on Back, New and Decks... with **Save**, **Don't save**, **Cancel**; an "unsaved" mark by the deck name | 1 | DeckEditor.cs | 0.5 |
| Q2 | New free-play decks (and decks without `#banlist`) open on the newest TCG list; "legal under No limits" reads amber, not green | 2 | DeckEditor.cs | 0.25 |
| Q3 | The box's chip without quotes ("destroy all ×"); unquoted multi-word searches rank cards with the exact phrase first, then by name | 4 | DeckEditor.cs:1258, CardSearch.cs (Rank), the search baseline | 0.5 |
| Q4 | Mouse wheel over the results turns pages; Page Up / Page Down too | 5 | DeckEditor.cs | 0.25 |
| Q5 | Keys: Ctrl+S, Ctrl+Z / Ctrl+Y, Ctrl+F or / focuses search (focused on open), Esc closes the topmost overlay then acts as Back | 8 | DeckEditor.cs; PLAYING.md controls | 0.5 |
| Q6 | Over-limit copies in the deck get a red mark; a problem line is clickable and selects its card | 6 | DeckEditor.cs (Grid, RefreshDeck) | 0.75 |
| Q7 | Zero results: "No cards match. On: ..." with a **Clear all** that clears box, chips, pickers and More | 9 | DeckEditor.cs, DeckEditor.Filters.cs | 0.5 |
| Q8 | The real count: "8,214 cards (first 2,000 shown)" | 5 | DeckEditor.cs:2595, 2639 | 0.1 |
| Q9 | Free play: no "Not owned" in the card detail | 13 | DeckEditor.cs or CardDetail.cs | 0.1 |
| Q10 | A status line for click-removal ("Removed X. Undo brings it back.") | 13 | DeckEditor.cs:2453 | 0.1 |

### Medium (1-3 days each)

| # | Change | Fixes | Files | Cost |
|---|---|---|---|---|
| M1 | Legibility floor: no editor text under 14 px at 1080 (about 9-10 px at 720); tile names stop shrinking below a floor and wrap or cut with "…" instead; the card text size setting reaches tiles, chips and help | 3 | DeckEditor.cs, DeckEditor.Filters.cs, CardView.cs | 2-3 |
| M2 | A ban list menu: click the list's name for Current / Historic (by year) / No limits | 2 | DeckEditor.cs | 1 |
| M3 | The scope as visible tabs (All · Owned · ★ · Seen · Recent); optional "Cards you faced" on the results screen opening Seen in duels on that duel | 11 | DeckEditor.cs; results screen (DuelController.Results.cs or its scene) | 1-2 |
| M4 | More filters as a drawer that leaves results in view; quick chips keep only what genres can't say (Hand trap, Searcher, Revive, Board wipe...), so no word appears twice | 9 | DeckEditor.Filters.cs, DeckEditor.cs | 1-2 |
| M5 | Save model: Save, **Save as...**, **Rename**; Copy stays | 12 | DeckEditor.cs; PLAYING.md | 1 |
| M6 | Controller text entry: an on-screen keyboard for the search and Archetype boxes; d-pad moves within a panel first | 7 | ControllerSupport.cs, a new on-screen keyboard script, DeckEditor.cs | 2-3 |
| M7 | Free-play Auto-fix: trim copies over the list's limits (Undo reverts) | 6 | DeckEditor.cs | 1 |

### Bigger redesigns (a week or more)

| # | Change | Fixes | Files | Cost |
|---|---|---|---|---|
| B1 | A results **list view** beside the grid: name, kind, stats, limit, first line of text; scrolls. Lets text-look players read 15-20 names at once | 3, 5 | DeckEditor.cs results area, CardView.cs | 4-5 |
| B2 | A **new-deck start**: empty, around an archetype, from a built-in deck, from a recipe, or paste | 10 | DeckEditor.cs, a new start panel | 3 |
| B3 | A **1280x720 / 1280x800 layout**: the left column folds (bot test and odds in a tab) so results and deck tiles get bigger | 3 | DeckEditor.cs Build*, Letterbox | 5-8 |

## Owner decisions (recommendation first)

1. **Which list a new deck checks against.** *Recommended:* the newest TCG list for new free-play decks and decks with
   no `#banlist` (Q2); historic decks keep their era, the campaign keeps its circuit's. *Or:* the last list you picked.
   *Or:* keep No limits but in amber. Default chosen if building starts: the newest TCG list.
2. **Leaving with unsaved changes.** *Recommended:* a Save / Don't save / Cancel dialog (Q1), like Master Duel. *Or:*
   keep the two clicks but put the warning next to Back and keep the recovery copy after leaving.
3. **Two-word searches.** *Recommended:* exact phrase first for unquoted words (Q3); the set of cards found doesn't
   change, only the order, so the old-search baseline still holds as sets. *Or:* only drop the quotes from the chip
   and add "use quotes for a phrase" to the placeholder.
4. **Results browsing.** *Recommended:* the wheel and real count now (Q4, Q8), the list view later (B1). *Or:* replace
   paging with one scrolling grid.
5. **Controller typing.** *Recommended:* an on-screen keyboard (M6). *Or:* leave typing to Steam's or the OS keyboard
   and say so in PLAYING.md.
6. **What to build first.** *Recommended:* Q1-Q10 as one spec (about 3.5 agent-days), then M1 and M2. *Or:* M1 first if
   1280x720 players matter most.

## Not verified

- **Unsaved-changes, Save, Save as new, Delete and Back flows:** no check flag reaches them; findings 1 and 12 are from
  the code. The save-as-new bar's look wasn't shot.
- **Real mouse and keyboard input:** the checks call the editor's actions or send simulated events; no real typing,
  hover timing or double-click was tried.
- **The campaign catalog** was judged from existing 3840x2160 shots of the same build, not a fresh `-campaigncheck`.
- **Card pictures on:** every shot is the text look (`-hdpics off`). How tiles read with pictures wasn't checked.
- **The build is from 2026-11-11.** The code read is current; DeckEditor.cs last changed 2026-11-08, so they should
  match, but Part B is changing it now.
- **Steam Deck / 1280x800** and its own keyboard: not tried. 1280x720 was the smallest size run.
- Ban list order in the < > cycle: counted from the shipped files (about 47), not stepped through.

## Appendix: click to focus instead of hover (design only, 2026-11-19)

The owner's proposal: no hover focus; a click focuses a card anywhere; double-click adds (results) or removes (deck);
drag moves; a key adds or removes the focused card; after a focus the wheel scrolls that card's text. Answered as the
player's advocate, recommendation first. Comparisons are from how those games behave as I know them, not rechecked here.

**1. Does it make sense?** *Yes, with one change:* make "double-click" mean **"click the focused card again"** (no
timer), and keep a one-click fast path on the right button (3).
- It fixes: reading a card can no longer change the deck (today a left click to read adds or removes); the detail
  stops jumping as the pointer crosses the grid; deck cards and test-hand cards can be read safely; mouse, pad and
  keyboard can share one model (focus, then act).
- It costs: adding 40 cards goes from 40 clicks to 40 double-clicks. A new player's first click does nothing visible to
  the deck, so the hint line and the panel's Add button must say what to do. A timed double-click is hard on trackpads,
  with a tremor, and when the deck reflows under the pointer.
- "Click the focused card again" fixes the timing: a fast double-click still works (click 1 focuses, click 2 acts), a
  slow one works too, and after a deck card is removed the card that slides into its place isn't focused, so an extra
  click focuses it instead of removing it.
- Other editors: EDOPro shows the card on hover, adds and removes with a right-click, and drags. Master Duel and Duel
  Links add with one click or tap and keep the details a separate step. YGOPRODeck shows the card on hover and adds
  with a click. None makes adding take two clicks with nothing faster, so keep (3).

**2. Should hover still do anything?** *Yes, a highlight only:* an outline on the card under the pointer, and the left
panel never changes on hover. Over the detail panel, hover keeps its job: the blue card terms explain themselves
(CardDetail's `TermHover`). A delayed preview would bring back the jumping the change removes. If veterans miss
EDOPro's hover reading, add a Settings switch later ("Card detail follows the pointer", off by default).

**3. Keep right click?** *Yes, as the one-click fast path:* right-click a result adds it to Main/Extra; right-click a
deck card removes one. **Shift+right-click** sends a result to the Side Deck, or moves a deck card between Main and
Side. That's EDOPro's habit, and 40 cards stay 40 clicks. *Or:* keep today's meaning (right-click = Side), and adding
is double-click, keys or drag only.

**4. Keyboard shortcuts.** None fire while any text field has focus (search, deck name, label, Archetype, the save-as
name): check `InputField.isFocused` on all of them.

| Key | Does |
|---|---|
| Enter (or +) | Add the focused card to Main/Extra (another copy if it's already in the deck) |
| Shift+Enter | Add the focused card to the Side Deck |
| Delete, Backspace (or -) | Remove one copy of the focused card |
| S | Move the focused deck copy between Main and Side |
| Arrow keys | Move the focus within the results grid or the deck |
| Page Up / Page Down | Results page |
| Enter in the search box | Leave the box and focus the first result (so type, Enter, Enter adds it) |
| / or Ctrl+F | Back into the search box. Esc leaves the box, then closes the top overlay, then acts as Back |
| Ctrl+S, Ctrl+Z, Ctrl+Y | Save, Undo, Redo |

**5. The wheel.** *Recommended rule: the wheel scrolls what's under the pointer.* Over the results it pages them (wheel
paging from the review); over the detail panel it scrolls the focused card's text (already built); over the deck it
does nothing. "Scroll the focused card's text anywhere" fights wheel paging, and every other Windows screen scrolls the
thing under the pointer. Most card text fits; the few long ones are a short move to the left panel away. Page Up / Page
Down cover paging from the keyboard.

**6. Controller.** The pad has no hover, so its cursor follows the same rule as the mouse.

| Button | Does |
|---|---|
| A | Focus the card under the cursor; A again on the focused card adds it (results) or removes one (deck) |
| X | Results: add to Side. Deck: move between Main and Side (no focus needed) |
| Hold A and move | Drag, as now |
| Right stick | Scroll the focused card's text (the pad's wheel) |
| Y, LB / RB, B, View, D-pad | As now: favorite, page, back, More filters, jump |

Three copies cost 4 presses (focus, then A three times) instead of 3 today. The hint bar's first item becomes
"A  read · A again  add / remove".

**7. Risks, and the checks that would prove it.**
- *New players click and nothing goes in.* The help line and the first-focus status say "Double-click or right-click to
  add". Check: the hint and status text after the first focus.
- *A small move during a click starts a drag and eats the click* (Unity's drag threshold). Check: a click with 3 px of
  movement still focuses.
- *Keys firing while typing.* Check: type "s", "a", "-" and Enter into each text field; the deck doesn't change.
- *Reflow after a removal.* Check: click, click on a deck card removes one; a third click focuses the card now in that
  slot and removes nothing.
- *Hover still changing the panel.* Check: send pointer-enter over 10 cards; the detail and the focused card don't
  change.
- *Wheel.* Check: over the results the page changes; over the detail the text scrolls; over the deck nothing changes;
  with More open nothing pages.
- *Controller.* Rewrite `-controllertest`: A focuses (deck unchanged), A again adds, X sides, the right stick scrolls
  the text, B still closes the test hand.
- Every step must fail with today's behaviour (one left click adds), so the checks prove the change. Also rerun
  `-editor-dragtest`, `-clickcheck`, `-searchcheck` (its layout rules cover the new help text) at 1920x1080 and 1280x720.
- *Duels:* leave hover there. In a duel a click is already an action, and reading the opponent's cards fast by hover
  matters more than matching the editor.
