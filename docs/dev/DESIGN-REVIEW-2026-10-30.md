# Design review, 2026-10-30

The agency-agents **Design division** reviewed Duel Circuit as a team, from screenshots of the built game (not the code
alone), in three gates: **visual design**, **copy** and **usability**. Each round: the gate teams reviewed fresh
screenshots and returned ranked findings, the fixes went in (presentation only), the app was rebuilt, all nine in-app
checks re-run, and the round committed. A gate counts as signed off only when its signer passed fresh screenshots of
the current build with no open must-fix item.

| Gate | Team | Signer | Result |
|---|---|---|---|
| Visual design | UI Designer, UX Architect, Brand Guardian | UI Finish-Gate Reviewer | **PASS** on f66413f (round 3) |
| Copy | Brand Guardian (voice), Visual Storyteller | Brand Guardian | **PASS** on 88c53cc (round 2), re-confirmed on f66413f |
| Usability | UX Researcher, Persona Walkthrough, Accessibility Auditor | UX Researcher | **PASS** on f66413f (round 3) |

Optional input came from the Whimsy Injector (round 1). Inclusive Visuals was not needed (no image generation) and the
Image Prompt Engineer was skipped as asked.

**Commits:** 88c53cc (round 1), f66413f (round 2). No game rules, CPU decks, difficulty or economy changed.

**Evidence.** Every round ran the nine checks (`-campaigncheck`, `-controlscheck`, `-layoutcheck`, `-boardcheck`,
`-pilecheck`, `-ordercheck`, `-tagcheck`, `-replaycheck`, `-menucheck`) in fresh temp `-mods` folders with the card
pictures read-only, plus a 15-duel `-qaplay -qafull` playthrough. Before/after screenshots (same file names) are kept
in `build/design-review-2026-10-30/before/` (baseline build) and `.../after/` (final build f66413f), with the check
summaries (`checks-r0.txt`, `-r1`, `-r2`) and the fix lists each round worked from (`ROUND1-FIXES.md`,
`ROUND2-FIXES.md`, `ROUND2B-FIXES.md`).

| Check | Baseline | Round 1 | Round 2 (final) |
|---|---|---|---|
| campaign | 102 ok | 102 | 102 |
| controls | 32 | 32 | 32 |
| layout | 24 | 24 | 24 |
| board | 23 | 23 | 23 |
| pile | 21 | 21 | 21 |
| order | 10 | 10 | 10 |
| tag | 4 | 4 | 4 |
| replay | 11 | 11 | 11 |
| menu | 6 | 6 | 6 |
| QA playthrough | 15 duels, 0 issues | 15, 0 issues | 15, 0 issues |

Paths below are relative to `build/design-review-2026-10-30/`.

## Visual design

**Found (round 1, NEEDS WORK).** The duel board's overlays collided: card text ran under Details/Pin, the turn dial sat
under the card detail and the log drawer, the log drawer covered callouts, prompt banners piled onto the toast and
callouts, the RECENT strip drew over the detail panel and the decision list, and the card menu's "Summon" label sat on
the Extra pile ("E Summon"). Hand cards cut their ATK/DEF ("L4 1800/160"). Deck file names showed on the LP plates
("burning-abyss", "classic"). The same role had four button styles (and the secondary Scout outshone Duel), screen
titles had four treatments, card backs were flat brown placeholders, Link monsters read "LINK-0" in the Extra Deck
viewer, a locked Tag Cup offered Duel buttons after New Game+, and modals were half empty.

**Changed.**
- Board overlays each have their own space: the detail's buttons get a strip under the text; the dial moved left of
  the right column; callouts and the chain panel stay left of the log drawer, under prompt banners and field-pick
  discs, off the pile counts, and stop above the dial (older callouts collapse); the RECENT strip hides under the
  detail and the decision list. — `before/campaign-duel-layout.png` → `after/`, `layout-text-log.png`,
  `board-dialog-position.png`, `controls-arrow-preview.png`, `before/qa-r1-g7-t07.png` → `after/qa-g7-t07.png`
- Hand cards show ATK/DEF on the side the fan leaves visible (the big digit is the Level). — `pile-callouts-crowded.png`
- Deck titles everywhere a deck is named: plates, tag swaps, replays, tag setup. — `tag-setup.png`, `replays-list.png`
- One button rule: the main action red `#B8432F` (one per group: the next match to win, "Buy a pack"), the rest slate;
  the coin toss neutral unless the toss tip names a side; New game moved beside Menu in a warning tint. Every screen
  title gold caps. — `campaign-circuits.png`, `campaign-shop.png`, `campaign-coin-toss-won.png`
- A drawn card back (navy, gold inset border, four-point gold sparkle), and piles show their count without it. —
  `campaign-pack-closed.png`, `board-chain.png`
- Results merge level-ups and keep the buttons clear of the edge; the result banner backs its buttons and colors the
  headline by outcome; modals size to content; the editor's x-copies tag moved off the stat strip; attract mode hides
  the duel controls under a dimmed backdrop. — `campaign-results-unlock.png`, `layout-result.png`,
  `campaign-tag-stuck.png`, `campaign-editor-browser.png`, `menu-attract.png`
- Smaller fixes: Link ratings in the Extra Deck viewer (`pile-extra-viewer.png`), label pills on the card menu
  (`board-card-menu.png`), the locked Tag Cup after New Game+ (`campaign-new-game-plus.png`), "set this turn" no
  longer on the card name (`campaign-first-duel-phase.png`), replay dial colored by the bottom seat.

**Sign-off.** UI Finish-Gate Reviewer, round 3, build f66413f: *VERDICT: PASS*, "none of my MUST-level problems are
still open". Remaining (SHOULD/NICE, not blocking): in the Free Duel developer layout, a collapsed callout can touch
the opponent's Extra pile, the position banner covers the opponent's portrait, and a partial fourth line of card text
can peek above Details/Pin; the pack-reveal summary is tight under the captions.

## Copy

**Found (round 1, NEEDS WORK).** Internal names shown to players (deck slugs, "SP-classic" set codes, "band"); numbers
that contradicted each other ("Win: +350 DP" before a duel, "Won ×1 +150 DP" after; the menu's "ten circuits" against
22; "49 cards" read as the deck size; "1 packs"); a misleading Stuck? tip ("each report below says what" when the
reports only list what to watch for); "Chain pill" jargon in the lessons; a scouting line with a double colon; and
muddled story lines (the tag-night "one field", the novice scene's contradictory ban-list lines, the garbled New
Game+ directions).

**Changed.**
- Menu "climb from the local shop to the Gauntlet"; match rows "First win: +350 DP" / "Rematch: +150 DP", results
  "First-win bonus"; versus "You have 1,000 DP · 49 cards in your collection"; "1 pack"; shop codes hidden and rarity
  counts labeled "Printings by rarity"; pack reveal "You have 1,000 DP"; recipe unlock ends with a period; skip recap
  "While you skipped:"; guest duel "Guest duel. Rules: <circuit>."; scouting "If you win the toss: go second. <Reason>."
  — `after/menu-main.png`, `after/campaign-after-duel.png`, `after/campaign-shop.png`
- Stuck? advice: "3 losses in a row here. Before you try again, one of these will help more:", Kit introduced as the
  shop regular, "Or partner with someone strong: X (Y).", "Add answers to the cards each report below says to watch
  for. Hand traps and removal decide these duels." — `after/campaign-tag-stuck.png`
- Lessons say "the Chain button (bottom right)". Tag Night line, bond line, starter toggle, replay caption, trader
  "(1 card)", "Level 1", "Leave lesson", "{Partner}'s turn", "Responding to: <card>".
- `campaign/story.txt`, wording only (no plot or character changes): the tag-night rules and friendship line, the
  novice scene's ban-list lines, "We share Life Points now.", Hollis's era lines ("the shop now stocks sets up to
  2015", "Every step up the circuit map brings a newer era"), the Expert Tag Cup line, "Forbidden and Limited List",
  "They won't bother.", the New Game+ directions. "Alphabetical is rude." was kept as a character joke.

**Sign-off.** Brand Guardian, round 2 (build 88c53cc): *VERDICT: PASS*, "No copy MUST is left"; re-confirmed on build
f66413f: *VERDICT: PASS*. Remaining NICE: "Every step up the circuit map" slightly overstates (several circuits share
an era; "Every new tier on the circuit map" was suggested); the replay caption "Keep one to hold on to it." could read
"Press Keep on a duel to hold on to it."; "(none yet)" repeats in a scouting list.

## Usability

**Found (round 1, NEEDS WORK).** Heuristic review of the first session in order, two personas (Sam, a casual Master
Duel player who skims; Pat, a 2004 paper player who reads everything) and a contrast audit with measured WCAG ratios.
The intro and story scenes opened over a stale, unrelated board (Gus says "2000 Life Points each" over Professor
Hollis at 8000, the campaign's first line over the title screen's attract duel); lesson LP bars were scaled to 8000 so
a full 2000 LP bar looked like danger; reward numbers disagreed between screens; the deck editor had two unlabeled
buttons the help text referred to; "New game" sat among navigation; the lesson banner covered the opponent's hand and
its "Back" read as "previous step"; the chain prompt didn't say what it responded to; the same action had two names
("Don't chain" / "Cancel", "Select" / "Confirm"). Contrast: the map's LOCKED captions (3.4:1), the shop search hint
(3.2:1), the board title over bright mats (down to 2.6:1), and labels players need at 10-11 px (the chain-mode help,
the RECENT tags). Round 2 (on the QA playthrough of the round-1 build) added: callouts covering the turn dial when
three pile up, a chain-link "1" left on an empty zone, "Your turn" shown on a tag partner's turn, and "Place here" at
2.25:1.

**Changed.**
- Story and lesson-intro scenes on an opaque backdrop. — `before/campaign-first-duel-intro.png` → `after/`,
  `before/qa-story-000.png` → `after/qa-story-000.png`
- Lesson LP bars scale to the duel's starting LP. — `campaign-first-duel-hand.png`
- Lesson banner top-right, "Leave lesson", 20 px text; the chain prompt names the trigger ("Battle Ox attacks Celtic
  Guardian", "Responding to: <card>") when the board knows it; one word per action in the dialog and the side list.
- Callouts stop above the dial; the chain-link badge leaves with its card; "{Partner}'s turn" on your partner's turn.
  — `before/qa-r1-g7-t07.png` → `after/qa-g7-t07.png`
- Editor buttons labeled and dimmed until a card is picked (`campaign-collection-editor.png`); New game beside Menu.
- Contrast and size: LOCKED 0.75 alpha at 15 px (7.2:1), search hint 6.2:1, board title opaque gold with an outline,
  "Place here" dark on gold (~10:1), chain-mode help 14 px, RECENT tags 13 px, callout/LP/chain labels 14 px, status
  line 15 px, legal footer 7.3:1. No meaning is carried by color alone.

**Sign-off.** UX Researcher, round 3, build f66413f, including a 15-duel QA playthrough of that build (0 issues):
*VERDICT: PASS*, "Every open item from round 2 is fixed except one cosmetic clip in the Free Duel developer panel."
Remaining (not blocking): the developer panel's card text can end on a half line; the chain prompt's trigger line is
~14 px (16 would match the line under it); the "{Partner}'s turn" pill can shrink to 10 px for a long partner name
(widen the pill or raise its minimum, accessibility auditor, SHOULD); a squeezed callout's text can best-fit to ~11 px
behind the dimmed chain dialog.

## Follow-up: five of the open decisions, done

After the sign-offs, the user picked five of the decisions below to do (one more build; all nine checks passed with
the usual counts):
- **Your plate** shows a drawn emblem (a gold ring around the card back's sparkle) instead of the faded "YOU"; it
  doesn't pick a face for you.
- **Rematch lines:** a `rematch:` key in character profiles, shown when a duelist turns up a second time in the same
  circuit (circuit list and versus). Ryo's: "Round two! New deck, same me!" (he's the only repeat today).
- **Loaner names:** beside an opponent playing the same deck, your loaner is named after whose recipe it is ("Ryo's
  Classic (loaner)", from the first unlocked recipe with that deck); otherwise it keeps its short name. No check
  stages that clash, so this one is untested on screen.
- **Starter tags:** a grey line above each starter's title ("Classic era · big monsters, simple plays", "Modern ·
  hand traps and the Extra Deck").
- **Free Duel tools:** the gear entry is "Free play tools (F12)", the matchup pickers show deck titles ("Chaos /
  Yata-Lock"), and the log shows gold "Turn 3 · You  LP 8000 / 8000" headings and the result without stars or a
  reason code (the narrator's plain text, which the harness's console logs use, is unchanged).

## For the user to decide

- **Primary color.** The red `#B8432F` now marks the main action everywhere (it was already "Duel!"). One reviewer
  noted red is also the opponent's side color and suggested gold; recommendation: keep red, since gold already marks
  playable cards, targets and currency on the board.
- **Art gaps, in priority order:** a painted card back (seen every duel; the drawn one stands in), portraits for Ines
  and Rhea (they show an initial/"?"), a stronger defeat crack effect, and last a story backdrop image (the plain dark
  backdrop works).
- **Newcomer terms:** keep GY and hand traps (standard, and the card-term glossary covers them); consider trimming the
  CPU trait words ("reckless, spends freely") from the circuit and versus lines and keeping "skill 3/10".
- **A 14 px minimum text size:** recommended as a layout-check warning rather than a forced size (forcing it would
  break hand cards and badges).
- QA's per-turn screenshot fires as the turn changes, so a frame can show the new turn's pill and dial while the old
  turn's END PHASE banner still fades (`after/qa-g1-t02.png`). It's the same in the baseline and lasts a moment;
  nothing to change.
