# Hover and highlight design (2026-11-07)

Design recommendation only; no code changed. Written by the UI Designer agent from Ui.cs, UiTheme.cs, ArrowNav.cs,
CardView.cs, BoardView.cs (HandLift, HoverTip), TermHelp.cs, Icons.cs, ControllerSupport.cs and the qa-quick screenshots.

## Recommendation

Add one shared `UiHover` component and put it on every button the builders make. It draws four states from theme tokens:
**hover** lightens the fill slightly and adds a hairline edge, **pressed** darkens it and scales it to 0.97, **disabled**
switches to the off colour and stops responding, and **keyboard focus** stays the existing gold `ArrowNav.Mark` frame. Hover
is never gold: gold means "where Enter goes" (and currency). Changes are fast (60 ms in, 100 ms out, pressed instant),
and the hover look is hidden while the d-pad or keyboard is driving, so the hover look and the gold frame never show together.

Today the buttons use Unity's default ColorTint. Its highlight is a multiply by about 0.96, which on `#2A3448` comes out as
a change of about 2 levels, so hover is effectively invisible. Multiplying can only darken, so on a dark UI the hover has to
be a *lerp toward the text colour*, not a tint.

## 1. Button states (new UiTheme tokens, both themes)

| State | Rule | Main Street (`Button #2A3448`) | Back Alley (`Button #2E2622`) |
|---|---|---|---|
| Rest | theme fill | `#2A3448` | `#2E2622` |
| Hover | `Lerp(rest, Text, HoverMix)`, `HoverMix = 0.10` (accent fills: `AccentHoverMix = 0.06`) + 1 px inner edge `HoverEdge` = Text at 22% alpha | `#3F485A` | `#413934` |
| Pressed | `Lerp(rest, Base, PressMix = 0.35)`, scale `PressScale = 0.97` | `#22293A` | `#221C19` |
| Focus (keys / d-pad) | existing gold `Mark`, 4 to 8 px border, no fill change | Gold `#FFC940` | Gold |
| Disabled | `ButtonOff` fill, label `TextFaint`, no hover or press, cursor unchanged | `#1C2333` | `#1A1613` |

- Because the rule works on the button's *current* rest colour, it works on Primary red, `TabOn`, `ButtonDone` and the
  rows alike, and screens that recolour a button (83 places set `targetGraphic.color` or `interactable`) keep working.
  Primary `#B8432F` with the 0.06 accent mix gives about `#BC5240`, which keeps white text at about 4.9:1. A 0.10 mix would drop it to 4.5:1.
- No scale on hover for text buttons: legacy `Text` shimmers at fractional scales, and the header buttons sit close together.
  Scale is only for the press, and for round or picture targets (below).
- Cursor: keep the arrow (decision 2). The game already draws its own controller cursor, and a hand cursor on buttons but not on
  cards would read as inconsistent.

## 2. What else responds to hover

| Target | Today | Recommended |
|---|---|---|
| Header, Duel, Scout, Prev/Next, deck arrows (Ui.Button/BoxButton, PromptPanel, DeckEditor's own button) | invisible tint | full button states above |
| Tabs (Circuits / Library / Back Alley) | invisible | hover on *unselected* tabs only; the selected tab (`TabOn`) doesn't change |
| Map nodes (campaign) | none; selection only | portrait ring brightens + scale 1.06 (like Icons.cs's discs, which already scale to 1.08), label strip lightens; locked nodes: ring only, no scale |
| Match rows, guest rows, shop set rows, recipe and list rows | none (rows use `RowA/RowB`, selected `RowSelected`) | `RowHover = Lerp(row, Text, 0.05)` + 2 px left bar in `HoverEdge`; it must stay visibly weaker than `RowSelected` |
| Shop slabs, van tiles, grading slabs, poster/mat tiles | gold `Mark` with keys only | lift 4 px + hairline edge; same 1.03 scale as card tiles if it reads better in test |
| Close / X buttons, dialog Cancel | invisible | button states; X glyph goes from `TextMuted` to `Text` |
| Cards (CardView), hand (HandLift), HoverTip, TermHelp tips | already respond | keep as they are; don't add a tint to cards (the art is the feedback) |
| Click-anywhere scrims, card backgrounds | n/a | no hover (Ui.Clickable stays plain by default) |

## 3. Mouse hover and the keyboard frame

One rule: **last input wins.** `ControllerSupport.HighlightMode` already tracks this for the pad. Extend it to arrow keys
(arrow key = highlight mode; mouse movement over 2 px = pointer mode).
- Pointer mode: hover visuals on, gold `Mark` hidden.
- Highlight mode: hover visuals off (an item under a resting mouse doesn't stay lit), gold `Mark` on.
- On the ArrowNav screens (shop, van, trader, grading desk, recipe book), pointer-enter also **moves the nav index** to that item, without
  showing the frame. Switching back to keys then starts from where the mouse was instead of jumping.
- The stick's virtual cursor counts as the pointer, so it gets hover too, which is correct.

## 4. Motion

Hover in 60 ms, out 100 ms, using unscaled time. Pressed is applied instantly on pointer-down and released over 80 ms. Row
and tile lifts use the same timings. With `FxSettings.ReducedMotion` on: colour still changes, with no scale or lift and
no fade. Nothing animates longer than 100 ms. (Icons.cs's `HoverShrink` eases back over roughly half a second;
retune it to 100 ms for consistency.)

## 5. Accessibility

- Contrast: white label on hover fill `#3F485A` is about 9:1; on pressed `#22293A` about 14:1; Alley text `#EDE6DA` on
  `#413934` about 9:1. The gold frame against `#121620` is about 11:1, well above the 3:1 rule for focus indicators.
- Not colour alone: hover adds an edge, pressed adds scale, focus is a shape (the frame), and selected rows keep their
  existing fill *plus* (recommended) the left bar. Disabled buttons also stop reacting to hover. Where the reason matters,
  the label or tooltip should already say why ("LOCKED", price shortfall); keep that.
- Hover never carries information that isn't available otherwise: help text that is hover-only (CampaignScreen's
  `hover: () => help.text`) should also update on keyboard focus.

## 6. Implementation shape

A shared component, not per-screen work.
- `UiHover : MonoBehaviour` (pointer enter/exit/down/up). It captures the rest colour on enter and restores it on exit only if
  nobody recoloured the button meanwhile; it checks `Selectable.interactable` and the input mode, and draws the edge as one
  child Image. About 120 lines, plus 7 tokens added to both themes in UiTheme.cs.
- In `Ui.Button`, `Ui.BoxButton`, PromptPanel's two buttons and DeckEditor's one: set `transition = None` and add `UiHover`.
  That covers about 180 call sites with 5 edits.
- `Ui.Clickable` gets an opt-in style (`Hover.Row`, `Hover.Tile`, `Hover.Node`). About 25 of the 64 call sites opt in (rows, tiles,
  map nodes); the rest stay plain.
- ArrowNav: a pointer-enter hook that sets the index, and `Mark` hidden in pointer mode, on 5 screens.
- Rough size: about 300 to 400 lines touched, a medium change, 1 to 2 sessions including a menucheck screenshot pass in both themes.

## Decisions for the owner

1. Hover strength: 0.10 lightening (recommended) or a stronger 0.15 that reads more clearly but looks less "dark iron" in the Alley.
2. Cursor: keep the arrow (recommended), or a hand cursor over clickable UI (it needs one small texture and is easy to add later).
3. Should the gold frame hide when the mouse moves (recommended), or stay visible until the next key press?
4. Map nodes and slabs: scale on hover (recommended for nodes), or brighten only?

## The owner's answers (2026-11-07)

Hover strength 0.10 (6% on accent fills); last input wins (the mouse hides the gold frame, arrows or the d-pad bring it
back, and hovering moves the keyboard position); map nodes scale to 1.06 and slabs lift 4 px (reduced motion keeps colour
only); keep the arrow cursor. Also: fix all eight missed-click causes found the same day (built alongside).

## As built (2026-11-07)

**Hover** (UiHover.cs, InputMode.cs, UiTheme tokens): every button from Ui.Button/BoxButton, PromptPanel and the deck
editor; 28 opt-ins (rows, tiles, map nodes, button-style choices); hit areas never move (HoverMesh draws the scale and
lift); last input wins with the gold frame (KeyFrame hides through a CanvasGroup); clicked buttons don't show the hover
look in highlight mode; arrows typed in a text field don't switch modes; Icons.Disc splits into a growing visual and a
fixed hit area.

**Missed clicks** (ClickSupport.cs, BoardView.Clicks.cs, diagnosis in the 2026-11-07 session): the first click on an
unfocused window (IgnoreFocus plus acceptsFirstMouse on PlayerWindowView; the owner confirmed one click works); a
10-point drag threshold and ScrollFit (lists that fit don't drag); the duel FX layer and result banner never block; hand
cards keep a fixed hit area while lifted; board redraws wait while a press over cards is held (8 s safety); the card
panel blocks while shown and passes through when slid away; ClickGuard drops a repeat multi-pick within 250 ms by card
identity; the controller's virtual mouse stays off until the stick moves from rest; on focus loss held keys and buttons
are released and gamepads paused. Check runs ignore the real mouse. New `-clickcheck` (the 16th app check).

**Checks** (one combined build, qa bundle id): board 22, tag 4, pile 21, controls 39, order 10, layout 51, replay 14,
mat 8, menu 6, campaign 179, final 46, theme 31, shop 89, alley 120, ladder 42, click 25, all green; `-versioncheck` 20;
`--campaign-test` passes; the player's prefs unchanged. Reviewed by an independent Code Reviewer; its fixes are in.
Not verified: a real gamepad; the owner's recheck of the app-switch click after the focus-loss change.
