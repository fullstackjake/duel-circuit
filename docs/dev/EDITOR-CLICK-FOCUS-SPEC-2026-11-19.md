# Deck editor: click to focus (spec, 2026-11-19)

The owner's request: no hover focus in the deck editor. Hovering a card made the left panel jump to every card the
pointer crossed on the way to the panel's buttons. Design from the UX Researcher (appendix of
[DECK-EDITOR-UX-REVIEW-2026-11-19.md](DECK-EDITOR-UX-REVIEW-2026-11-19.md)) and a UI Designer; choices by the owner.

## What the player gets

| Input | Results | Deck (Main / Extra / Side) | Test hand |
|---|---|---|---|
| Point | Lift + hairline only; the left panel never changes | same | same |
| Click | Focus: white ring, shown on the left | Focus | Focus (look only) |
| Click the focused card again (any speed) | Add one to Main/Extra | Take that copy out | Nothing |
| Shift+click | Add one at once (no focus step) | Take that copy out at once | Nothing |
| Right click / Shift+right click | Add one to the Side Deck | Move that copy between Main and Side | Nothing |
| Drag | Into a section | To another section, or back onto the results to remove | Nothing |
| Enter | Add the focused card to Main/Extra | Add one more copy | Nothing |
| Shift+Enter | Add the focused card to Side | Move the focused copy between Main and Side | Nothing |
| Delete / Backspace | Nothing | Take the focused copy out | Nothing |
| Arrow keys | Move the focus in the grid | Move the focus in the deck | Move the focus in the hand |
| Page Up / Page Down | Turn the results page | same | same |
| Mouse wheel | Scrolls the focused card's text on the left, anywhere, except over what scrolls on its own: the More filters lists, the active chip row, the reading view | same | same |

Keys never fire while a text field has focus (search, deck name, label, archetype, save-as name) or the reading view is
open. Controller: A focuses, A on the focused card adds or removes; X to/from Side; hold A to drag; the right stick
scrolls the card text; Y, B, LB/RB, View and the D-pad as now. Duels keep hover as it is.

## Rules

- **The ring:** outside the card, colour `T.Text` (a `CardFocus` theme role), with a gap in the background colour; a
  child with `raycastTarget` off. Gold (in deck / starter) and the blue border keep their meanings. Deck: 1 px gap,
  3 px ring; results: 2 px gap, 3 px ring around the card and its plates; test hand: 2 px gap, 4 px ring.
- **What focus remembers:** in the results, the base code (the ring comes back when that card is on the page; search and
  paging never move the focus); in the deck, section + base code + which copy (survives Sort and version switches).
  Adding keeps the focus where it is; moving follows the card. Focusing only moves the ring: it never rebuilds the
  grids.
- **Taking a copy out:** the ring moves to the next copy of that card in the section, else to its result tile, else
  nowhere; the panel keeps the card (Enter adds it back). It never lands on the card that slides into the gap.
- **On open:** the first result is focused. A search no longer shows its first result (DeckEditor.cs:391 goes).
- **Clicks:** focus on release, at once. A press that moves past `EventSystem.pixelDragThreshold` is a drag and focuses
  the dragged card; a drag is never a click.
- **Not owned (campaign):** focuses as usual (full-brightness ring); adding says "You don't own X yet. Get it in the
  Card Shop." Past the copies you own: the existing message.
- **The text hint:** a 24 px fade, "▼ more" and a 4 px scrollbar while the text runs on; the box edge glows briefly when
  the wheel scrolls it from elsewhere. Check the Input System's wheel units (1 or 120 per notch).
- **Words:** help lines "Click a card to see it on the left. Click it again to add it." / "Click a card in your deck,
  then again, to take it out. Shift+click does it at once." / "Right-click: Side Deck. Enter: add. Delete: remove.
  Drag cards anywhere." Status after an add: "Added Pot of Greed (2 of 3)."; after a removal: "Took out Pot of Greed.
  Undo brings it back." Controller legend: "A  look  ·  A again  add / remove  ·  X  to / from Side  ·  Y  favorite  ·
  hold A  drag  ·  B  back  ·  LB / RB  page  ·  View  more filters  ·  D-pad  jump". PLAYING.md's editor tables
  follow.
- **Removed:** the 0.15 s hover settle (`HoverSettle`, `SettleHover`, `HoverDetail`, `DebugHoverTest`).

## Showing what changed (owner, 2026-11-19: "don't want accidental moving of cards and no indication")

Every add, removal, move, Undo and Redo gives three signals at once (UI Designer's design):

- **The card.**
  - Added: a 3 px `T.Good` edge inside the new tile, held 0.25 s and faded over 0.45 s, plus a scale from 1.12 to 1.0
    over 0.18 s.
  - Moved (Main↔Side, or a drag): the same flash in its new place, in `T.Text`.
  - Removed: the cards after it slide 66 px left over 0.15 s, so the gap visibly closes; the result tile, if it's on
    the page, flashes a `T.Bad` edge.
  - A result's in-deck count pulses from 1.25 to 1.0 over 0.2 s when it changes.
  - Reduced motion: no scale, slide or pulse; the edge shows 0.7 s and goes off without a fade.
- **The section count.** "(+1)" or "(−1)" beside the header's count for 1.5 s, green for adds, red for removals.
- **A message next to the section.** A pill at the right of the changed section's header row
  (`Box(1094, headerY−1, 296, 24)`; header y 126 Main, 538 Extra, 662 Side): `T.PanelDeep` at 0.95, a 1 px edge in the
  kind's colour, 15 px `T.Text`, and an **Undo** button (56 px, `T.Info`) that does what Undo does.
  - Words: "+ Pot of Greed", "− Pot of Greed", "Pot of Greed → Side", "→ Main", "→ Extra".
  - Timing: in over 0.12 s, held 3.5 s (kept open while pointed at), out over 0.4 s.
  - Quick changes: the same card and kind within 2 s merge ("+ Pot of Greed ×2"); anything else replaces the message.
    At most 5 tiles flash at once; past that, only the counts signal.
  - Undo shows "Undone: + Pot of Greed" with **Redo**; a bulk undo shows "Undone: Clear (40 cards)", with no flashes.
  - The status line keeps the same text (for checks and logs).
- **Refused changes.** The tile shakes ±4 px three times in 0.24 s (reduced motion: a 0.7 s red edge), and a red-edged
  pill with no Undo appears at the target section: "Main Deck is full (60).", "You already have 3.", "You don't own it
  yet.", "All your copies are in." (An Extra Deck monster can always go to the Side.)
- **Sound** (under the interface volume and mute): add `place` 0.35; remove `draw` 0.30, pitch 0.9; move `move` 0.30;
  Undo / Redo `move` 0.30, pitch 0.85 / 1.1; refused `error` 0.50. `Sounds.Play` needs a way to send a clip through the
  interface volume (only click, hover and error do today); duels keep `place`/`draw`/`move` on the effects volume.

Checks: each signal appears for each kind of change (the flash's colour on the right tile, the "(+1)", the pill's
words and its Undo restoring the deck, the refused pill and no change); merging; reduced motion; the sound calls
(logged, since checks run muted).

## Files

`unity/Assets/Scripts/DeckEditor.cs`, `DeckEditor.Filters.cs`, `CardDetail.cs` (text hint, scroll from outside),
`CardView.cs` (editor-only click/hover options: duels unchanged), `UiTheme.cs` (`CardFocus`), `ControllerSupport.cs`
only if the mapping needs it, the checks (`DuelController.SearchCheck.cs`, the drag test, `-clickcheck`, the controller
test), `docs/PLAYING.md`.

## Verification

Each needs a check step that fails on today's code: hovering 10 cards leaves the panel alone; click focuses without
changing the deck; click again adds / removes; Shift+click adds / removes at once; right click and Shift+right click do
the Side action; a 3 px wobble still focuses; removing a copy never focuses the card that slides in; keys typed into
every text field change nothing; Enter / Shift+Enter / Delete / arrows / Page keys; the wheel over the results, deck
and panel scrolls the text, and over the More panel scrolls its list; a search keeps the focus; the controller's A,
A again and X. Then `-searchcheck`, `-clickcheck`, `-campaigncheck`, `-layoutcheck`, `-controlscheck`, the drag and
controller tests, `--campaign-test`, `--deckbuilding-test`, at 1280x720 and 1920x1080.

## Owner decisions

Decided (owner, 2026-11-19):
1. The wheel scrolls the focused card's text, also over the results; pages turn by buttons, keys and LB/RB.
2. "Click the focused card again" adds or removes, at any speed (no double-click timer).
3. Right click stays the Side Deck action. Shift+click adds or removes at once; Shift+right click does the Side
   action (the same as right click).
4. Arrow keys move the focus in this build.

Decided (owner, 2026-11-19, the recommendations):
5. The change message sits beside the changed section's header.
6. Sound on, under the interface volume.
7. The message stays 3.5 s, kept open while pointed at.
8. Refused changes shake and play `error`.

## As built (2026-11-19)

Built as specified, then one fix round after an independent code review, with the deviations below. Code:
`DeckEditor.Focus.cs` (focus, arming, ring, clicks, keys, wheel, stick), `DeckEditor.Feedback.cs` (the three signals,
the message layer, the sounds), `DeckEditor.cs` (every deck change through `Change`, which signals; Add / RemoveAt /
MoveSection / UndoSteps / RedoSteps; the grids wired to them; the editor's 0.15 s hover settle, from the uncommitted
round before, is gone), `CardView.EditorMode` (editor-only clicks and a lift-only hover: duel boards never call it),
`CardDetail` (`ScrollHints`, `ScrollBy`, `KeepScrollOnSameCard`, `EmptyHint`; the `ScrollHint` component),
`UiTheme.CardFocus`, `Sounds.Play(..., ui: true)`, `FrameGraphic` and `UiHover.Adopt` (in `UiHover.cs`).

- **Focus by data.** Results: the base card. Deck: section, base card and which copy of it in that section. Test hand:
  its slot. Focusing calls `ShowDetail` and moves the ring only; every rebuild puts the ring back from the data. Loading
  another deck drops a deck or hand focus; closing the test hand moves a hand focus to that card's result.
- **Armed focus** (owner's rule, decided by the coordinator, 2026-11-19, on "no accidental moving of cards"). "Click the
  focused card again" acts only on a focus the player made by clicking that tile, while the card is still on it: the
  tile is remembered (its place in the results, or section and slot). A focus the editor set (on open, after a removal's
  refocus, after the test hand), the arrows' or a drag's isn't armed, and a card that moved (a card before it went, a
  move, a search that placed it elsewhere) no longer matches: the next click on it only arms it. Keys act on any focus.
- **Taking a copy out.** The ring goes to the next copy of that card after the gap, else the one before it, never the
  card that now sits in the gap; else to its result; else nowhere. Not armed.
- **Keys** read every enabled keyboard. They're skipped while the UI's selection is a focused `InputField` (and for one
  frame after), while the reading view or the deck list is open, while the test hand or the More panel is open (the
  hand's own look-only focus still moves with the arrows), and mid-drag (nor do LB/RB or Page keys turn pages then). The
  editor clears the UI's selection of a clicked button each frame, so Enter can't press it again. Enter in the search
  box focuses the first result (not armed) and lets the box go, so the next Enter adds it.
- **Viewed.** Only a click focus or the reading view adds a card to Recently viewed; arrows and drags don't.
- **The wheel.** Input System 1.20 under Unity 6 reports **one per notch** (`InputSettings.scrollDeltaBehavior` is
  `UniformAcrossAllPlatforms`, logged by the check); `WheelTicks` also reads 120-per-notch values. A notch moves the text
  three of its lines (about 55 px), from elsewhere and over the text alike (the text's ScrollRect sensitivity is set
  against the UI module's `scrollDeltaPerTick`, 6; one notch over the text used to move 180 px). The right stick scrolls
  only once it has been at rest since the editor opened (a drifting stick never does).
- **Signals.** Every deck change signals: the counts and a message with Undo, which replaces the one before. Single-card
  ones read "+ X", "− X", "X → Side"; the rest "Sorted", "Cleared N cards", "Built from <recipe>", "Built from your
  collection" / "Built around X", "Auto-fixed for the list", "Imported from the clipboard", "Imported <file>", "Starter:
  X" / "Not a starter: X", "Version: X", "Ban Pass: + X". Messages merge only for the same kind, card and section within
  2 s, and a merged "×N" message's Undo takes all N back (one signal, "Undone: + X ×N", whose Redo does all N again).
  Card flashes come from diffing the decks: a single-slot add, removal or move flashes; anything bigger shows only
  counts and the message. The messages live on a layer under every overlay and hide (no pointer either) while the test
  hand, More, the reading view, the deck list or Save's bar is open; pointed at (the top thing under the pointer), they
  stay open. A new deck, another deck or closing the editor clears the message, counts, edges and motion. Sounds asked
  for are logged in check runs only (`editor sound: ...`).
- **Status after an add**: "(n of m)", m being the least of 3, the copies you own and the picked list's allowance (with
  Ban Passes); past it, "(n; <list> allows m)".

Deviations:

1. **The More panel has no list that scrolls.** The wheel over it leaves the card text alone.
2. **Redo's message** is "Redone: …" with **Undo**; Undo and Redo also write "Undone: …" / "Redone: …" on the status line.
3. **A move into a full section is refused** (red message, the tile shakes); before, a right click could push the Side
   Deck past 15.
4. **The panel's empty line** reads "Click a card to read it."
5. **Frames are at least one screen pixel** (a 1 px line is 2/3 px in a 1280x720 window, and a side could drop out).
6. **A bulk change's message** has the Info colour's edge and plays `move` 0.30.

Checks: `-focuscheck` (new, 71 steps; driven through a test mouse, keyboard and gamepad), the drag test's "a drag
focuses the dragged card", the controller test rewritten (A looks, A again adds, X sides with the focus staying, the
right stick scrolls the text). Keys typed into a text field: the Input System test keyboard makes no IMGUI events, which
is what `InputField` reads, so the check does what `InputField.OnUpdateSelected` does with a Return (`onSubmit`, then
`DeactivateInputField`) in the frame before the key reaches the editor, the order where the one-frame grace matters.
