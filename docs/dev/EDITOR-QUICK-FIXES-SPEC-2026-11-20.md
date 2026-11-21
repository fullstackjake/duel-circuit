# Deck editor: the review's quick fixes (spec, 2026-11-20)

The rest of the quick fixes from [DECK-EDITOR-UX-REVIEW-2026-11-19.md](DECK-EDITOR-UX-REVIEW-2026-11-19.md) (its Q1-Q10)
and the five leftovers of the click-to-focus test pass ([EVIDENCE-2026-11-19b.md](EVIDENCE-2026-11-19b.md)), as one
build. It follows [EDITOR-CLICK-FOCUS-SPEC-2026-11-19.md](EDITOR-CLICK-FOCUS-SPEC-2026-11-19.md), whose rules stay.
Line numbers are from commit 25e6376.

**Not in this build:** small text at 1280x720 (M1), the ban list menu (M2), Save as / Rename (M5), the list view and
the other redesigns. Q4 (the wheel turns pages) is dropped: the owner decided on 2026-11-19 that the wheel scrolls the
card text; Page Up / Page Down already turn pages. Q10 (a status line for a click removal) is built already.

## What the player gets

| # | Today | After |
|---|---|---|
| A | With unsaved changes, Back, New and opening another deck write a warning on the status line; a second click discards the changes and the recovery copy. Nothing shows that a deck is unsaved | A dialog: **Save**, **Don't save**, **Cancel**. A dot on the Save button while the deck has unsaved changes |
| B | A new free-play deck, and any deck without a list of its own, is checked against "No limits" and reads green "legal" | They open on the newest TCG list. "No limits" with no problems reads amber and says no list is picked. A free-play deck remembers the list you picked for it |
| C | `destroy all` lists Level 12 monsters first; its filter chip shows quotes; "2000+ cards"; zero results is an empty grid | Cards with the exact words rank first; the chip shows what you typed; the real count; "No cards match" says what's on and has **Clear all** |
| D | The only key the editor reads besides the focus keys is Esc for the More panel | Ctrl/Cmd+S, Ctrl/Cmd+Z, Ctrl/Cmd+Y, Ctrl/Cmd+F or `/`, and Esc closes what's on top, then leaves |
| E | A card over its limit wears the same amber badge as one within it; the problem lines aren't clickable | A card a problem names wears a red badge; clicking its problem line focuses it in the deck |
| F | Free play's card detail says "Not owned" (it reads the campaign collection) | No ownership line in free play |
| G | Five small leftovers (below) | Fixed |

## A. Leaving with unsaved changes

- **The mark.** While `_dirty` is true, a 10 px dot in `T.Warn` sits inside the Save button's top-right corner
  (`raycastTarget` off). Nothing moves in the toolbar.
- **Who asks.** Back, New, a deck picked in Decks..., the controller's B (`ControllerBack`) and Esc when it leaves
  (part D). They ask only while `_dirty`; otherwise they act at once, as today. `Close()` stays public and immediate
  (the campaign, checks and the duel call it).
- **The dialog.** A layer over everything in the editor: `T.Scrim` at 0.7 (as the deck list's), a 560 x 220 panel in
  `T.PanelDeep`, centred.
  - Title, 22 px bold: "Save changes to <deck name>?"
  - Body, 15 px: "You changed this deck and haven't saved it."
  - Buttons, right-aligned, 40 px tall: **Save** (the accent button), **Don't save**, **Cancel**.
  - Keys: Enter is Save, Esc and B are Cancel. A click on the scrim is Cancel. The focus keys, the shortcuts, the
    wheel and the change message are off while it's open (add it to `Covered`, DeckEditor.Feedback.cs:256, and to the
    block in `FocusInput`, DeckEditor.Focus.cs:320).
- **Save** runs `Save()`. If the deck is then saved (`_dirty` false), the waiting action runs. If Save opened its own
  "how to save" bar (`_savePrompt != null`: Recipe, Build, Import or most of the deck replaced), the dialog closes,
  the waiting action is dropped and the status says "Choose how to save, then leave." The player presses Back again.
- **Don't save** drops the recovery copy (`ClearRecovery`) for all three actions, then runs the action. Today New and
  opening another deck leave a stale recovery file behind, which the next opening offers back.
- **Cancel** closes the dialog; nothing else changes.
- **Delete** keeps its "click Delete again" question on the status line (`Confirmed("delete", ...)`); `Confirmed`'s
  unsaved-changes branch goes.

## B. Which list a deck is checked against

- **The newest TCG list** is `Banlist.NewestTcg` (engine, below): among the editor's lists whose name has the word
  "TCG", the one with the latest `Date`; the first in list order on a tie (the current lists come before the historic
  ones, so "2026.10 TCG" beats "TCG October 2026"). "Traditional", "Worlds", "GOAT" and "OCG" names never count.
- **Free play** (`OwnedCards == null` and no `FormatRules`):
  - `Open` with no `banlist` argument starts on the newest TCG list.
  - `LoadDeck`: a deck with a `#banlist` note opens under it, as today. A deck without one opens under the newest TCG
    list, unless the player picked a list by hand in this opening (`_listPickedByHand`), which stays.
  - `NewDeck` (the New button) keeps the list that's showing.
  - **Remembering the pick.** When a free-play deck is saved and the list was picked by hand (`_listPickedByHand`),
    Save writes the note `banlist` with the list's name ("No limits" included), so the deck reopens under it and the
    deck list shows it. A deck that already has a note keeps it unless the player picked another list by hand. Built-in
    and historic decks are untouched (saving one makes your own copy, which follows the same rule).
- **The campaign** is unchanged: the circuit, cup or table passes its list.
- **"No limits" reads amber in free play.** With no problems under "No limits":
  `<color=Warn>No ban list picked (No limits): only deck size and 3 copies are checked. Pick a list with < >.</color>`
  In the campaign it stays green, as does every real list.
- Checks that rely on "No limits" at open (the `(2 of 3)` status steps of `-focuscheck`, `-searchcheck`, `-ydkcheck`'s
  free import, `-layoutcheck`'s editor steps) pick it themselves (`CycleBanlist(-_banIndex)`, DeckEditor.cs:519, shows
  how) or assert the new list.

## C. Search

- **Exact words first** (engine, `CardSearch.Run`). When the box holds two or more plain words next to each other and
  nothing else between them (no quotes, no `:` `@` `<` `>` `=` terms, no `-`, no OR), the words joined by single spaces
  are the phrase. Cards that match only by text (rank 3 today) split in two: those whose text contains the phrase come
  first, ordered by where the phrase starts in the text (earliest first), then by name; the rest keep today's order
  (kind, Level, name). Name matches (ranks 0 to 2) still come before both. It applies in Name + text and Text only;
  Name only is unchanged. The set of cards found never changes, only the order, so the old-query baseline (compared as
  sets) holds.
  - Measured on 2026-11-20 with this order over every `vendor/BabelCDB` file (more cards than the game loads, so the
    game's ranks can only be as good or better): `destroy all` puts Dark Hole, Raigeki and Heavy Storm in the first 25
    of 664; `draw 2 cards` puts Pot of Greed in the first 11 of 316. Name matches weren't part of the measurement.
- **The chip** in the active row shows the box as typed, without added quotes: `destroy all ×` (DeckEditor.cs:1288).
  Quotes the player typed stay.
- **The real count.** `RunSearch` keeps the number of cards found before `Take(2000)` (DeckEditor.cs:2765). The count
  label reads it with a thousands separator ("8,214 cards", "1 card"). Past 2,000 the page label adds that only the
  first 2,000 are shown ("1 / 80 · first 2,000"). The genre status line (DeckEditor.Filters.cs:426) reads the real
  count too. Nothing in the results column may overlap at either size.
- **Zero results.** When a search finds nothing and something narrows it (the box, a chip, a picker, a More filter,
  Related, a typed ban term), the empty grid shows, centred:
  - "No cards match." (17 px), then "On: <the active row's labels, comma-separated>" (13 px, up to two lines, then
    "…"). When the scope isn't All cards: "Looking in: <scope>." on its own line.
  - A **Clear all** button: clears the box, the chips, the four pickers, the More filters and Related, then searches.
    The scope and the sort stay. The focus goes to the first result, not armed.
  - The Seen in duels and Recently viewed notes ("Cards your opponents play show here after a duel.") keep priority
    when their list is empty.

## D. Keys

| Key | Does | While a text field has the keyboard |
|---|---|---|
| Ctrl+S / Cmd+S | Save (the Save button) | Works |
| Ctrl+Z / Cmd+Z | Undo | Nothing (the field keeps the key) |
| Ctrl+Y, Ctrl+Shift+Z / Cmd+Shift+Z | Redo | Nothing |
| Ctrl+F / Cmd+F, or `/` | The search box takes the keyboard, its text selected | Ctrl/Cmd+F works; `/` types a slash |
| Esc | Closes the top thing: the leave dialog (Cancel), Save's bar, the reading view, the deck list, the test hand, the More panel. With none open: Back (through part A) | Leaves the field only (Unity's own Esc); it never also closes or leaves in the same press |

- Ctrl and Cmd both work on both systems. All are off mid-drag and while the leave dialog is open (Esc and Enter
  excepted, as in part A).
- The shortcuts read every enabled keyboard, as the focus keys do (`Down`, DeckEditor.Focus.cs:326), and keep the
  one-frame grace after typing (`_typingLastFrame`) for Esc and `/`.
- The search box is **not** focused when the editor opens: the first result is, so the arrows and Enter work at once
  (click-to-focus spec, "On open").
- The help lines under the results gain one line: "Ctrl+S save · Ctrl+Z undo · Ctrl+F or / search · Esc back" ("Cmd"
  on a Mac). PLAYING.md's editor tables follow.

## E. Finding what's illegal

- **Red badge.** Every copy of a card that a problem line names (`DeckProblem.Code != 0`: over its limit, Forbidden,
  not released in the format, not owned or not enough copies owned, wrong section) wears a red badge
  (`#D9433F`, the Forbidden X's red) where the limit badge sits: the limit number when the card is over a limit of 1
  or 2, "X" when Forbidden, "!" for anything else. A card within its limit keeps the amber number; a Ban Pass card
  keeps its gold P unless a problem names it.
- **Clickable lines.** A problem line that names a card is a button: pointing underlines it, a click focuses that
  card's first copy in the deck (Main, then Extra, then Side; `FocusDeck(section, index, viewed: false)`, not armed)
  so the ring and the left panel show it. Deck-wide lines (size) aren't clickable. The "...and N more" line and the
  analysis, Ban Pass and story lines stay plain text.
- Only the first seven problems have lines today; that stays.

## F. Free play's ownership line

`CardDetail.CopiesText` shows the campaign's copies wherever `CardDetail.CopiesOf` is set, which is the whole game
(DuelController.cs:261). The editor's detail shows no ownership line while `OwnedCards == null`; the campaign editor
and duels are unchanged.

## G. Leftovers of the click-to-focus pass

1. **Stale status.** After any deck change the status line names that change: a bulk `Change` writes its words
   ("Sorted.", "Cleared 40 cards. Undo brings them back."), so Sort and Clear no longer leave "Added Pot of Greed
   (2 of 3)." under a "Sorted" message.
2. **The ring under the test hand.** A deck card's ring isn't drawn while the test hand covers the deck (it comes back
   when the hand closes).
3. **"▼ more".** The label gets its own strip in the box's colour, so it never touches the last faded line of text at
   1920x1080.
4. **Enter on a deck copy** adds one more copy to the section that copy is in (Enter on a Side Deck copy adds to the
   Side Deck). `-focuscheck` step 9 follows.
5. **Two weak check steps.** "closing the editor leaves no message" makes a message and calls `Close()` without
   `DebugClearSignals()` first. A new step opens a deck from the deck list while a message shows: the message goes and
   its Undo can't change the new deck.

## Parts and files

Two builders at the same time; no file is shared.

### Part E: engine and harness (general-purpose, Opus)

Owns `src/Ygo.Engine/DeckBuilding/CardSearch.cs`, `src/Ygo.Engine/DeckBuilding/Banlist.cs`,
`src/Ygo.Harness/CardSearchTest.cs`, `src/Ygo.Harness/DeckBuildingTest.cs`,
`src/Ygo.Harness/fixtures/card-search-baseline.txt`. Harness build: `build/bin/editfix`.

```csharp
// Banlist.cs
/// <summary>The index of the newest TCG list in <paramref name="lists"/> (the word "TCG" in its name, latest Date,
/// first in order on a tie), or -1 when there is none.</summary>
public static int NewestTcg(IReadOnlyList<Banlist> lists);

// CardSearch.cs: Find's signatures don't change.
/// <summary>The phrase the last Find ranked exact matches of first ("destroy all"), or null when it ranked none.</summary>
public string? LastPhrase { get; }
```

Tests that fail without the change:

- `--card-search-test`: `destroy all` has Dark Hole and Raigeki in its first 25, and every card before the first
  text-only match without the phrase either matches by name or has the phrase; `"destroy all"` (quoted), `destroy`,
  `destroy lv4 all`, `destroy OR all` and Name only set no `LastPhrase` and keep today's order; the same query finds
  the same set of cards as before (count and hash); Text only ranks the phrase first too.
- `--deckbuilding-test`: `NewestTcg` over the game's lists is "2026.10 TCG" or newer and never a Traditional, Worlds,
  GOAT, OCG, Rush or Speed list; over a list without a TCG name it is -1; a tie picks the first.

### Part U: the editor (Unity Architect, Opus)

Owns `unity/Assets/Scripts/DeckEditor.cs`, `DeckEditor.Filters.cs`, `DeckEditor.Focus.cs`, `DeckEditor.Feedback.cs`,
`CardDetail.cs`, new `DeckEditor.Leave.cs` (the dialog and the mark) and `DeckEditor.Keys.cs` (the shortcuts), new
`DuelController.EditorCheck.cs` (`-editorcheck`), `DuelController.FocusCheck.cs`, `DuelController.SearchCheck.cs`,
`DuelController.cs` (only the check's start line and its name in the `checks` list, lines 698-713), and any other
check file whose editor steps relied on "No limits" at open. Check build: `build/qa-editfix`. Only this part runs
Unity.

For checks (public, on `DeckEditor`):

```csharp
public bool DebugUnsavedMark { get; }                 // the Save button's dot is showing
public bool DebugLeaveDialogOpen { get; }
public string DebugLeaveDialogTitle { get; }          // "" when shut
public void DebugLeaveAnswer(string answer);          // "save", "discard" or "cancel"
public void DebugBack();                              // the Back button
public void DebugOpenFromList(string name);           // a deck picked in Decks...
public string DebugProblemsText { get; }              // the problems box as shown, rich text included
public IReadOnlyList<(DeckSection section, int index, string text, string color)> DebugProblemBadges();
public bool DebugClickProblem(uint code);             // clicks that card's problem line; false when it has none
public (int shown, int found, string count, string page) DebugResultCount { get; }
public (string note, bool clearAll) DebugEmptyResults { get; }
public void DebugClearAll();                          // the Clear all button
public bool DebugOwnershipLine { get; }               // the detail shows an ownership line for the selected card
public bool DebugSearchHasKeyboard { get; }
```

`-editorcheck <folder>` (new; fresh `-mods`, muted, through the test keyboard and mouse as `-focuscheck` does), each
step failing on today's code:

- **A:** a change shows the mark and Save clears it; Back with changes opens the dialog and leaves the editor open;
  Cancel keeps the deck and the mark; Don't save leaves and no recovery copy is offered at the next opening; Save
  saves the file and leaves; New and a deck from the list ask too; Don't save on New leaves no recovery file; Save on
  a rebuilt deck opens Save's bar, drops the action and says so; Back with no changes leaves at once; the focus keys
  and the wheel do nothing under the dialog; the controller's B asks, and B again cancels.
- **B:** free play opens a new deck on the newest TCG list; 3 Pot of Greed read as a problem, not "legal"; a built-in
  historic deck opens under its era; a saved deck without a note opens on the newest TCG list; a list picked by hand
  survives opening a note-less deck; a deck saved after a hand pick reopens under that list in a new opening of the
  editor; "No limits" with no problems is amber and has the new words; the campaign editor still opens on the
  circuit's list and reads green.
- **D:** Ctrl+S saves; Ctrl+Z and Ctrl+Y undo and redo; Ctrl+Z typed in the search box changes nothing in the deck;
  Ctrl+F and `/` give the search box the keyboard; `/` typed in the deck name stays a slash; Esc closes the More
  panel, the test hand, the reading view, the deck list and the dialog one at a time, then leaves; Esc in a text field
  only leaves the field.
- **E:** 3 copies of a card limited to 1 wear a red "1"; a Forbidden card a red "X"; a card within its limit an amber
  number; a not-yet-released card a red "!"; clicking the limited card's line focuses its first copy (ring, left
  panel) and the deck is unchanged; the size line isn't clickable.
- **F:** no ownership line in free play; the campaign editor still has one.

`-searchcheck` gains part C: the chip's label for `destroy all`; the first page holds Dark Hole and Raigeki; the count
for an empty box is the real number and the page label says "first 2,000"; a search with 0 results shows the note with
its "On:" labels; Clear all brings results back and leaves the scope; an empty Favorites scope still shows its own
note. `-focuscheck` gains part G (five steps, and step 9 changed). `-layoutcheck` must still pass at both sizes.

### After the build

Code Reviewer (Opus) on both diffs against this spec; one fix round; Evidence Collector (Opus) on a fresh build;
Technical Writer (Sonnet) for `docs/PLAYING.md` (the editor's keys and tables, saving, the ban list default),
`docs/DEVELOPING.md` (the `-editorcheck` row) and `docs/dev/TODO.md`.

## Traps

- `DeckEditor.cs` is 3,241 lines and one class: Part U is one builder, never two.
- Unity's `InputField` puts back its old text on Esc and fires its change event, which runs a search. Esc while typing
  must not also act as Back (the one-frame grace).
- The Input System's test keyboard makes no IMGUI events: typed text in checks goes through the field, not key
  presses (see the As built of the click-to-focus spec).
- `ArrowNav` and `BoardView` read Esc too (ArrowNav.cs:389, BoardView.cs:2573): the editor's Esc must not also reach a
  screen under it.
- `_problems` is one rich-text label that also holds the analysis, the Ban Pass line and a historic deck's story
  (DeckEditor.cs:2573-2596): the clickable lines need their own targets without moving that block.
- The card flashes of the change signals are read by `-focuscheck` through `DebugEdges`: the red badge is a badge, not
  an edge, and must not show up there.
- The old-query baseline of `--card-search-test` is skipped since the card data moved on 2026-11-19 ("the card
  database changed"). Rewrite it with `--write-baseline` **before** changing `CardSearch.cs`, so the set comparison
  runs against the old search.
- A campaign deck and a free deck share the deck folder: the `banlist` note is written only by a free-play save.
- Old decks must open: a deck with a `banlist` note naming a list that no longer exists opens on the newest TCG list.

## Verification

Harness, from `build/bin/editfix`: `--card-search-test`, `--deckbuilding-test`, `--campaign-test`, `--texts-check`
(none may fail). App, from `build/qa-editfix`, one at a time, fresh `-mods` each, at 1280x720 and 1920x1080:
`-editorcheck`, `-searchcheck`, `-focuscheck`, `-layoutcheck`, `-campaigncheck`, `-clickcheck`, `-ydkcheck`, the drag
and controller tests. Every screenshot looked at. Nothing here touches DP, so `--arbitrage` isn't needed.

## Owner decisions (recommendation first)

1. **A free-play deck remembers its list.** *Recommended:* yes, when you picked the list by hand: Save writes it into
   the deck. Without it, a Goat deck you built reopens under the newest list and reads illegal. *Or:* don't write
   anything; you pick the list again each time. Building the recommendation.
2. **Enter on a Side Deck copy.** *Recommended:* one more copy to the Side Deck (the section the copy is in). *Or:*
   keep it adding to the Main Deck. Building the recommendation.
3. **The search box on open.** *Recommended:* not focused; `/` or Ctrl+F gets there, and the arrows and Enter work at
   once on the first result, as you decided on 2026-11-19. *Or:* focus the search box on open (the review's Q5), and
   the focus keys wait until you leave it. Building the recommendation.
4. **Which cards get the red badge.** *Recommended:* every card a problem names, with "!" where the problem isn't a
   limit (not released, not owned). *Or:* only cards over a limit. Building the recommendation.
5. **Delete.** *Recommended:* leave its "click Delete again" question for now. *Or:* give it the same dialog
   (**Delete**, **Cancel**) in this build, about an hour more.
6. **The review's own decisions 1 to 3**, not answered yet. *Recommended* (and what gets built unless you say
   otherwise): new decks check against the newest TCG list; the Save / Don't save / Cancel dialog; exact words first
   for unquoted searches. *Or,* for each: keep "No limits" in amber only; keep the two clicks with the warning beside
   Back; only drop the chip's quotes.

Decided (owner, 2026-11-20): build it with the defaults above, 1 to 6 as recommended. The session plans, briefs and
checks; agents do the building, the review, the test pass and the docs.

## As built (2026-11-20)

Built as specified, then one fix round after an independent code review, with the deviations below. Code: engine and
harness: `CardSearch.cs` (`Find(..., exactWords)`, `PhraseOf`, `LastPhrase`, `TakeBanFilters`), `Banlist.NewestTcg`;
editor: `DeckEditor.Leave.cs` (the question, the dot), `DeckEditor.Keys.cs` (shortcuts, `BackOne`),
`DeckEditor.cs` (`Open`, `LoadDeck`, `NewDeck`, `NoteListPick`, `RefreshDeck`'s problems and badges, `RunSearch`,
`ShowResults`), `DeckEditor.Filters.cs` ("No cards match", Clear all), `DeckEditor.Feedback.cs` (badges, problem links),
`CardDetail` (`ShowOwnership`, the "▼ more" strip), `BoardView.cs` (the Esc guard), `DuelController.EditorCheck.cs`.
Models: engine and harness by a `general-purpose` agent (Opus), the editor by a Unity Architect (Opus), review by a
Code Reviewer (Opus), the test pass by an Evidence Collector (Opus), docs by a Technical Writer (Sonnet).

Deviations:

1. **Exact words are opt-in.** `Find(..., exactWords: true)` turns the ranking on; the editor passes it and the Card
   Trader doesn't, so the Trader's order is unchanged. The phrase can also come from a run trimmed of keywords at its
   ends (`spell destroy all` ranks "destroy all", after trying the whole run); a keyword inside the run gives no second
   try.
2. **`TakeBanFilters` leaves a bare `limit:`** where it took a term from between two words, so `destroy limit:1 all`
   still has no phrase. `Find` ignores the bare term.
3. **`LastPhrase` is null when no card's text has the phrase**, not only when no phrase was tried.
4. **Which list a deck opens under** is not the spec's simpler rule. A deck with a `banlist` note opens under it. A deck
   with none opens under the list last picked by hand since the editor opened (`_handList`), else the newest TCG list.
   Save writes the note only when the list was picked while that deck was open (`_deckListPicked`); a new deck counts
   if the picked list was still showing when New was pressed. Picking a list doesn't set `_dirty`, so the dot doesn't
   show for it (Save still writes it).
5. **Free play and the campaign remember their own last list** (`_freeBanIndex`, `_campaignBanIndex`), so free play's
   default never reaches the campaign editor. Free play's own last list is only a fallback when no TCG list exists.
6. **Badges on every problem card, also past the seven lines shown.** The badge reads the problems list, the links
   read only the first seven lines; a line cut off by the box isn't a link.
7. **The Favorites empty note** is new: "Cards you mark ☆ Favorite show here." (it was an empty grid).
8. **The count label shrinks to fit** (10 to 13 px) instead of running under the active row.
9. **A bulk change writes the status line for every bulk caller** (Sort, Clear, Auto-fix, Import, Build...), not only
   Sort and Clear; a caller with more to say writes its own line after.
10. **Shortcut rules under overlays.** Undo and Redo wait while the reading view, the deck list, Save's bar or the test
    hand covers the deck; Ctrl+S and Ctrl+F wait under the reading view, the deck list and Save's bar only (they work
    under the test hand and the More panel). Under the leave question nothing acts but Enter (Save) and Esc (Cancel).
11. **`BoardView`'s Esc guard.** The board no longer closes its overlays on an Esc the editor used: `DeckEditor.ShownLately`
    (the editor is open, or was a frame ago) holds it off, whichever Update runs first.
12. **"▼ more" needed no duel change.** Only the editor sets `ScrollHints`, so the new strip shows nowhere else.
13. **The amber "No limits" line** tests `OwnedCards == null`, not `FreePlay`; they differ only when a cup's format is
    set with no collection (not checked whether that can happen).

Checks: `-editorcheck` (new, 65 steps), `-searchcheck` 71 steps, `-focuscheck` 77 steps; `--card-search-test` 202 steps
(the old-query baseline was rewritten from the old search before the change: 203 of 203 old queries return the same
sets); `--deckbuilding-test` has `NewestTcg` steps. The Evidence Collector's report is
[EVIDENCE-2026-11-20.md](EVIDENCE-2026-11-20.md).

Not verified: the real keyboard, mouse and controller (the checks use test devices, and the `/` step can't fail with
the test keyboard); a real window resize for the problem-line targets; the Back Alley theme; sound (checks run muted);
Windows; the legacy Input Manager branches (`#else` paths).

Evidence (2026-11-20): the Evidence Collector tested fresh builds of the harness and the Mac app
([EVIDENCE-2026-11-20.md](EVIDENCE-2026-11-20.md)). Harness: `--card-search-test` 202 ok, 0 FAIL; `--deckbuilding-test`,
`--campaign-test`, `--texts-check` and `--copies-test` pass; `--games 30` has 0 script errors and 0 crashed games. App:
24 runs (its `checks/summary.txt`), 0 FAIL and no exception in any log; its section 3 has the table of checks with their step
counts (`-editorcheck` 65, `-searchcheck` 71, `-focuscheck` 77, `-layoutcheck` 54, `-campaigncheck` 189 at 1280x720).
The Card Trader's search, old code against new over 217 queries at its two limits (600 and 2,000): all 434 result lists
are identical. The report has three small findings and a list of check steps that assert less than they look (its
sections 5 and 6); none blocks the commit.
