# Duel Circuit: what's working, what isn't (2026-10-26)

A step back from the bug-by-bug QA. This review draws on:
- **About 250 campaign duels** played through the real screens by the QA stand-in (parts 1-3 and the late-band run so
  far: every band up to the Master League);
- **the harness measurements:** deck ratings (78 decks, bot-vs-bot Elo), CPU blunder audits, A/B tests;
- **the earlier UI and UX review** ([UI-UX-HISTORY.md](UI-UX-HISTORY.md));
- **the code.**

Portraits and expression art are left out (in progress).

## The short version

**The duel itself is the strong part.** The rules are right (EDOPro's core), and the board reads well after the Link
Evolution and Master Duel lessons. The CPU no longer embarrasses itself, and long CPU turns can be skipped.

**The campaign around it needs shape.** Five things matter most:
1. **The difficulty curve isn't a curve.** The Beginner band is harder than the Novice band. Everything up to
   Advanced is a walk with a modern deck. Then the Expert band is a cliff, because that's where the CPU starts
   planning its turns.
2. **DP stops meaning anything.** 106,000 DP by the end of the Expert band, with packs at 150-200 DP.
3. **Walls have exits, but nothing points to them.** Loaners, recipe packs, Selection Packs and Recipe exist. The QA
   player needed hard-coded rules to find them; a real player gets no hint after three losses.
4. **Tag duels are the weakest mode.** You win 36% of them, and 0 of 16 at the Expert and Master Tag Cups.
5. **There's no first-duel tutorial.** Gus's lessons teach chains, after the first circuit; nothing teaches the
   board, zones, phases or the prompt panel.

**Found and fixed after this review: you always went first.** The rules core always gave turn 1 to player 0, which
was always you. That skewed every number here: your setup was never interrupted, and decks built to go second
(Mekk-Knight) opened with dead hands. Now a coin toss decides, and the winner chooses (see the README's "Playing a
duel"). The win rates above were measured before the toss. Re-measured with it ([part 5](QA-PLAYTEST-2026-10-26-PART5.md)),
the Expert cliff and the Houston wall mostly disappear: they were the QA player going first with a going-second deck.

## What's working (keep it)

| Area | Why it works | Evidence |
|------|--------------|----------|
| Rules and engine | EDOPro's ocgcore: every card behaves as in the real game; tag duels, Yata-lock and house rules on top | Thousands of harness duels; no rules bugs found in QA |
| The duel screen | Big card detail and pins, chain panel and badges, Check Field, column guides, big pile counts, zone icons, level numbers in hand, Spell Speed and genre tags, glossary | QA: 0 layout issues; thousands of decisions, all made through the on-screen buttons |
| Respecting the player's time | Skip to my turn, speed settings, short repeat animations, Chain Auto/On/Off | Duels average 6-13 turns; the 8-second prompt lag is fixed |
| CPU behaviour | Rules sense (no removal on its own cards, no boosting yours, no trading bosses for less), 30 deck playbooks, a turn planner for skilled characters | 200-duel audits: own-card removal 45 to 4, downgrades 78 to 14, Acid Golem and Allure blunders fixed |
| Collection paths | Era shop, Selection Packs, recipe packs, Structure Decks, Card Trader (any era, priced by rarity), loaners, Ban Passes | Every path exercised in QA; DP and token bookkeeping always matched |
| Deck building | Recipe, Auto-fix, Build, Save as new, test hands, opening-hand odds, bot tests | Era and Save-as-new bugs found and fixed in QA |
| A world | 20 circuits on a map, 77 characters with lines and styles, 4 guests, Tag Cups with bonds, Kit, 26 story scenes | |
| Testing | -qaplay plays the campaign like a player; in-app checks; harness tests; replay audits | Found about 30 real bugs in two days, most before you saw them |

## What isn't working

### 1. The difficulty curve (high)

| Band | Opponent skill | Opponent deck strength (Elo) | QA win rate (Mekk-Knight Invoked) |
|------|----------------|------------------------------|-----------------------------------|
| Beginner | 0.67 | 1480 | 86% (12-2) |
| Novice | **0.55** | **1304** | 92% (12-1) |
| Intermediate | 0.70 | 1381 | 100% (19-0) |
| Advanced | 0.82 | 1483 | 100% (12-0) |
| Expert | **0.92** | **1610** | **19% (12-50)**, mostly with loaners |
| Master | 1.00 | 1791 | 44% (4-5), with loaners |
| Gauntlet | 1.00 | 1858 | (not reached yet) |

Three problems in one table:
- **Novice is the easiest band.** Its decks are the weakest in the game and its duelists the sloppiest, but it comes
  second. A player learns to win easily, then coasts through Intermediate and Advanced too.
- **The cliff is the turn planner.** Characters at skill 0.9 and above plan their turns; below, they play card by card.
  Advanced averages 0.82 (almost no planners), Expert 0.92 (nearly all). The decks get stronger too (1483 to 1610),
  but the planner is the step that makes the stand-in lose 50 of 62.
- **The starter decides the first half.** A modern starter (Mekk-Knight Invoked) clears four bands without trying.
  The classic starters are measured (the campaign sim: a skilled pilot clears everything in about 140 duels); the
  Mekk-Knight one isn't even in the deck ratings.

**The rule (from you, after this review):** never weaken an opponent to fix a wall, as long as it plays within the
rules. Walls are beaten with tools the player gets, not softer CPUs. So no skill cuts, no throttled planner, no easy
mode; the suggestions below are about order and about the player's tools.

**Suggestions:**
- **Reorder the early bands** so strength rises band by band: move the Novice band's circuits (the weakest decks)
  before the Beginner band's. The duelists stay exactly as they are; only the order changes.
- **Rate the new starter and re-run the campaign sim per starter.** If the modern starter stays, say plainly on its
  tile that the first bands will be easy.
- **Tools for the cliff**, where the planner switches on at the Expert band: see the walls section (a "Stuck?" panel,
  scouting the opponent's deck, practice against it, Recipe showing strength).

### 2. The economy inflates (high)

DP in the QA campaign: 3,000 after the first circuit, 25,000 by the Novice band, 106,000 by the end of the Expert
band, 119,000 at the Master League. Packs cost 150-200 DP; completing a recipe cost under 1,100 DP. The DP sinks added
this week (tokens at a rising price, three Ban Pass slots) took a few thousand.

**Why it matters:** DP is the reward for every win. Once it can't buy anything you want, wins stop feeling like
progress, and the shop (one of the best-built parts) goes quiet.

**Suggestions:**
- **Make late-band rewards things, not DP.** A token, a guaranteed rare from the opponent's deck (Tag Force's "you
  won their card"), a Selection Pack ticket, a playmat or sleeve.
- **Price by band.** Late-band sets and Selection Packs cost more (they hold the strongest cards).
- **Worthwhile sinks:** card alt-art and foil upgrades of cards you own, deck sleeves and mats, or rerolling the
  shop's featured sets. More Ban Pass slots past the current three, at a steeper price.
- **Keep a running "net worth" stat** in the campaign header so the inflation is visible while tuning.

### 3. Walls have exits, but no signposts (high)

When the stand-in hit walls, the way through was: Selection Packs for an unlocked deck, Recipe, Save as new, and
finally a loaner. Each of those works. But in the game:
- nothing appears after repeated losses;
- loaners hide behind the versus screen's deck arrows;
- Recipe picks the deck you own most of (the stand-in kept rebuilding Stall Burn for the Expert band), not one that
  could win.

**Suggestions:**
- **A "Stuck?" panel after three losses to one opponent**, on the results screen. "Try a loaner (Kozmo, half DP)",
  "Recipes you're close to: Drytron 80%", "Your partner X could help in the Tag Cup". One click each.
- **Recipe should show strength.** Order recipes by owned % but show each one's measured strength (stars from the deck
  ratings), so you can pick a deck that can win, not just one you can finish.
- **Show difficulty on the map and guests.** Stars per circuit and guest (from deck Elo and skill), so the cliff is at
  least visible before you walk into it. Rhea (0-24 in QA) and Seraph (1-6) would show 5 stars.

### 4. Tag duels underperform (medium)

36% overall (13-23), and 0-16 at the Expert and Master Tag Cups.
- **Your partner rarely matches the band.** Partners are duelists you've beaten, so mostly early, weaker characters;
  bonds (other decks, sharper play) help but cap at what they own. Kit plays your own deck.
- **The duel itself is harder for your side.** One shared field and LP, and your partner's turn can undo yours. The
  opposing pairs at the Expert and Master Tag Cups are two top-tier decks with planning duelists.

**Suggestions:**
- **Let a partner borrow one of your decks** (like Kit) once the bond reaches 3, so a late partner can bring a
  late deck.
- **Pick the partner for the opponents.** The Tag Cup screen could show each opposing pair's decks and suggest the
  partner and deck that match up best.
- **Partner turns with intent.** The partner bot plays its own turn without knowing your plan. Even simple
  rules for the shared field (keep the lead's boss as the field's centrepiece, don't spend the lead's set cards)
  would help.

### 5. No basic tutorial (medium)

Onboarding today: pick a starter, read the opening story, play Ryo. Gus's three lessons are excellent but come after
the first circuit and teach only chains. The board, the zones, the prompt panel, phases, Battle Phase targets,
Check Field, Skip to my turn and the Chain pill are learned by trial (or from tooltips).

**Suggestion:** Ryo's first duel as a guided duel. A scripted opening hand and a few overlays ("Your hand: click a
monster to summon it", "Battle Phase: drag to attack", "Hold Space to see the whole field"), using the same
set-board mechanism the lessons use. Skippable, and only on a new campaign.

### 6. Smaller things

- **Repeat faces early.** Ryo, Brask, Gus, Tink and Vera fill the first four circuits; the Beginner band reads as five
  people. A couple of the 77 characters moved forward would help.
- **Story density.** 26 scenes, mostly at milestones (first clear, Tag Cups, levels 10 and 25). Circuits past the
  first get no scene of their own; a two-line intro from each circuit's champion before its last match would make
  the climb feel like one.
- **Guests are all-or-nothing.** Ines and Nocturne are fair; Rhea and Seraph are walls even when they unlock. A
  guest could scale: a first meeting with a mid deck, and their signature deck after you've beaten them once.
- **The code is concentrated.** DuelController.cs is 3,660 lines, BoardView.cs 2,285 and DeckEditor.cs 2,051.
  The two worst bugs this week were interactions there: the versus screen undoing the deck pick, and a hand-trap rule
  catching Aleister. Splitting by feature (versus, results, tag, campaign wiring) and adding a `--sense-test` case
  per AI rule would make regressions rarer.

## Ranked: what I'd do next

| # | Change | Impact | Effort |
|---|--------|--------|--------|
| 1 | Reorder the early bands (opponents unchanged) | Fixes the Novice dip | Small: circuit order; then a campaign-sim run per starter |
| 2 | "Stuck?" panel after three losses (loaner, close recipes, partner) | Turns walls into choices | Small-medium: the data exists (loaners, Recipe, partners) |
| 3 | Difficulty stars on the map, Tag Cups and guests | Sets expectations; makes the curve visible | Small: deck Elo plus skill to 1-5 stars |
| 4 | Late-band rewards as things (tokens, the opponent's card, tickets) and band-priced packs | DP means something again | Medium |
| 5 | Guided first duel against Ryo | Onboarding for new players | Medium: lesson machinery plus overlays |
| 6 | Partners borrowing your decks; matchup advice on the Tag Cup screen | Tag mode stops being a trap | Small |
| 7 | Scout and practice: see a wall's deck after a loss, and goldfish or test against it in the editor | A way through any wall, opponents untouched | Small-medium: bot tests exist |
| 8 | Recipe and Recipe Book show deck strength | Better rebuilds | Small |
| 9 | Champion intro lines per circuit; early faces reshuffled | Warmth and variety | Small (writing) |
| 10 | Split DuelController, BoardView, DeckEditor by feature | Fewer regressions | Medium, no player-visible change |

Already on the roadmap and still worth doing: the practice board (puzzles and sandbox), "how this deck plays" pages
from the playbooks, showing the luck after a loss, target arrows, a colorblind palette, Quick Duel.

## How these numbers were made

- **Win rates:** the QA stand-in (the strongest planning character's style, with the Mekk-Knight playbook) playing
  the campaign through the screens at 16 events per second. Reruns of the same match count; walls show as many
  losses. It is one pilot with one deck, so treat the Expert-band rate as "a strong modern deck without a planner's
  help", not as a player's.
- **Skill and Elo:** each band's opponents, from `personalities/*.profile` and `campaign/deck-ratings.txt`
  (300 rounds of bot-vs-bot duels).
- **DP:** the QA save at each circuit clear.
