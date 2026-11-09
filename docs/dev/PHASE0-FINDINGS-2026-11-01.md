# Phase 0 findings (2026-11-01)

What the measuring tools say before the Library, the Back Alley and brackets are built (POST-GAME-PLAN-2026-10-31,
step 2 and "Targets"). Tools: `--library-check/-plan/-study/-summary`, `--alley-gen`, `--alley-rate`, `--endless-test`,
`--bracket-sim`, `--bracket-calibrate` (README, "Measuring the post-game"). Raw results: `campaign/library-advice.txt`
and the logs in `build/phase0/`.

## The Library (12 cups, 48 matches)

**Method.** For each cup, `--circuit-study` (the QA stand-in, skill 1.0 with the turn planner, against the CPUs as the
game makes them) with four decks: the three best-rated decks entirely out by the cup's pool date, plus the boss's own
deck (the mirror). 24 duels per match, seats alternating. At 24 duels a single figure is good to about ±20 points
(95%), so results within a few points of a target are a coin flip.

**6 of 48 matches are under target** (65% at openers, 60% in the middle, 55% at the boss):

| Cup | Match | Best option | Won | Going first / second | Target |
|---|---|---|---|---|---|
| TeleDAD | 2: machine-re-volt | teledad | 58% | 50 / 66 | 60% |
| HAT & Shaddoll | 4: shaddoll (boss) | the mirror | 45% | 41 / 50 | 55% |
| Link Dawn | 4: thunder-dragon (boss) | the mirror | 41% | 25 / 58 | 55% |
| Kashtira & Snake-Eye | 3: snake-eye | eldlich | 50% | 50 / 50 | 60% |
| Kashtira & Snake-Eye | 4: kashtira (boss) | eldlich | 50% | 33 / 66 | 55% |
| Ryzeal | 4: ryzeal (boss) | the mirror | 54% | 41 / 66 | 55% |

- **The proposal's walls mostly hold, and the mirror answers about half of them.** ABC (83% with the mirror) and Eldlich
  (70%) are cleared by their mirrors. Shaddoll, Thunder Dragon, Kashtira and Ryzeal aren't.
- **The toss matters at three of the walls.** Going second: Thunder Dragon 58% (first 25%), Kashtira 66% (33%), Ryzeal
  66% (41%). A toss tip ("go second") takes all three near or over 55%.
- **Every opener and most middle matches clear easily,** often 90-100%: the Library's early matches are a warm-up for a
  player with any top deck.
- **Two walls are partly the data's doing.** machine-re-volt and snake-eye have cards from after their cup's pool (see
  the check below). A pool-filtered build of those CPU decks may lose some strength; measure again after.

**Next for the Library:** `--wall-study` at the six (more duels, the Stuck? options), toss tips for Thunder Dragon,
Kashtira and Ryzeal, and the plan's fallback (an optional "Champion's table") for whatever is still under 55%.

**Re-measured at 72 duels a match (2026-11-01, later):** TeleDAD's machine-re-volt (62%) and HAT & Shaddoll's boss
(56%) now meet their targets. The study tries only the three best-rated decks in a cup's pool, so 15 more in-pool decks
were screened against the late walls at 24 duels and the best confirmed at 72 (`build/walls/`, merged into
library-advice.txt). Two pools moved a month to take in their own CPU decks' cards: Kashtira & Snake-Eye to 2024-04
(Snake-Eyes Diabellstar, Metaltronus) and Ryzeal to 2025-05 (Maliss). That made snake-eye a player option, and it is
the answer to Kashtira:

| Cup | Match | Best option | Won | First / second | Target |
|---|---|---|---|---|---|
| Link Dawn | 4: thunder-dragon (boss) | the mirror | 54% | 50 / 58 | 55% |
| Kashtira & Snake-Eye | 3: snake-eye | the mirror | 56% | 50 / 63 | 60% |
| Kashtira & Snake-Eye | 4: kashtira (boss) | snake-eye | **61%** | 52 / 69 | 55% |
| Ryzeal | 3: fire-king-snake-eye | ryzeal | 55% | 52 / 58 | 60% |
| Ryzeal | 4: ryzeal (boss) | fire-king-snake-eye | 48% | 44 / 52 | 55% |

The first three short ones are within noise of their targets (±12 at 72 duels), and going second clears each. Ryzeal's
boss is the one real wall left: nothing in its pool beats it more than half the time (fire-king-snake-eye 48%, the
mirror 45%, kashtira 38%, bystial-dragon-link 37%; screened: mekk-knight-invoked, tenpai-dragon, yubel, snake-eye,
thunder-dragon, spright). CPUs play at full strength (the owner's rule), so what's left is the proposal's fallback,
the Champion's table, or accepting the wall.

**Decided 2026-11-01: the Champion's table.** The Ryzeal duo is now `champion:` in campaign/library.txt: optional for
the cup's clear (Garunix's Fire King Snake-Eye at match 3 is the boss, held to 55%: ryzeal 55%), needed for its stamp.
Its first win plays the `table:Ryzeal Cup` scene. `--library-summary` now reports 2 matches under target, both within noise.

## Cup decks out of their pool (`--library-check`)

5 cup decks have cards first printed after their cup's pool month:
- stall-burn (Goat): Marshmallon, Pot of Duality.
- gadgets (Goat): 19 cards, all the Gadgets among them. The Gadgets were out in 2005, so the printing data likely lacks
  their first printings: check `printings.bin` before changing the deck.
- machine-re-volt (TeleDAD): Cyber Dragon Core, Cyber Emergency.
- snake-eye (Kashtira & Snake-Eye): Snake-Eyes Diabellstar, Metaltronus.
- maliss (Ryzeal): Wizard @Ignister, Maliss <P> March Hare, Maliss in the Mirror, Allied Code Talker @Ignister.

Several CPU decks are over their cup's list by 1-6 cards ("visitors", kept as built); their loaners are made legal.

## The Back Alley

**Splash decks are too weak** (`--alley-rate`, 8 decks an event, 200 duels each against their own base):

| Event | Average Elo gain | Target | Average speed-up |
|---|---|---|---|
| Dumpster Row | +38 | +60 to +150 | 0.4 turns |
| The Basement Game | -15 | +60 to +150 | 0.0 |
| The Black Market | -25 | +60 to +150 | 0.1 |

- **Why:** the builder ranks contraband by a generic value score, so Elemental HERO Stratos lands in most decks and the
  later events splash Extra Deck cards their bases barely use (Fairy Tail - Snow, Performage Damage Juggler, D/D/D Wave
  High King Caesar). What it cuts to make room is sometimes worth more.
- **Speed is fine:** no event's splash decks are a turn faster on average (the one "too fast" deck, stall-burn, barely
  wins either way).
- **Next:** curated contraband per event (the plan's own lists: Raigeki, Dark Hole, Pot of Greed, Graceful Charity,
  Change of Heart at Dumpster Row; Heavy Storm, Crush Card Virus, Maxx "C" at the Basement...), weighted by what the base
  deck lacks, then measure again.

**Curated (same day; Game Designer's lists in `campaign/alley.txt`).** Each event picks from 16 one-card effects the bot
plays well, filling the roles its base lacks first (draw, wipe, backrow, steal, revive...; one a role, two for draw),
cutting by role and never the archetype, a draw or search card, a limited staple or a card on the list. Extra Deck
splashes are off, and 13 more locks and dice are excluded. Measured the same way (8 decks an event, 200 duels each):

| Event | Splash | Before | Curated | Target | Speed-up |
|---|---|---|---|---|---|
| Dumpster Row | 3 (was 2) | +38 | +65 | +60 to +150 | 0.3 turns |
| The Basement Game | 4 (was 3) | -15 | +45 | +60 to +150 | 0.4 |
| The Black Market | 4 | -25 | +36 | +60 to +150 | 0.8 |

- Dumpster Row meets its target. The Basement and the Black Market gain, but not enough: a fifth card at the Black
  Market measured +36 again and made it 0.9 turns faster, so it stays at four. Each card adds about +12 Elo, not the
  +20 to +40 the designers expected; the late bases (Kozmo, Thunder Dragon, Shaddoll) already run efficient engines, and
  old power cards help them least.
- Endless duels still pass: 0.0% capped, 1.1% past turn 40, 0.1% draws.
- **Options for the last two events:** contraband from their own era (2012-2019 Forbidden cards, built into the base's
  engine) rather than classic staples; or accept +35 to +45 there and let stakes and CPU skill (0.85, 0.95) carry the
  difficulty; or lower the target for late events, where a +60 card is rare.

**Era cards (same day; the owner chose era-specific contraband).** Each base of the two late events gets 1 to 3 cards
from its own era that plug into its engine (`for:` lines in `campaign/alley.txt`: Steam the Cloak for Blackwing, Glow-Up
Bulb and Level Eater for the synchro decks, Goyo Guardian, Artifact Scythe for HAT, Exciton Knight for the Xyz decks,
Norden for Thunder Dragon, Mirage of Nightmare for Dark World, That Grass Looks Greener for Shaddoll). Wind-Up and Dragon
Ruler sit the Basement out (nothing in the era helps Wind-Up; the 1-copy list guts Dragon Ruler); Kozmo takes one draw
card. Cuts now keep a deck's big monsters (2400 ATK and up) and replace a spell with a spell. The sampler now takes every
base once before repeating, so these numbers cover bases the earlier runs never drew:

| Event | Earlier (fewer bases) | Era cards (every base) | Target |
|---|---|---|---|
| The Basement Game | +45 | +49 | +60 to +150 |
| The Black Market | +36 | +11 | +60 to +150 |

- **Era cards work where they fill a gap:** TeleDAD +119 (Glow-Up Bulb), Blackwing +143 (Steam the Cloak), Kozmo +74,
  Dark World +56, Shaddoll +31 to +53.
- **Tuned decks lose to any splash:** ABC -81 (its Twin Twisters and Solemn Strikes go), HAT -35, Thunder Dragon -21 to
  -3. The Black Market's +36 before was mostly sampling: ABC and Dark World weren't drawn.
- Without ABC the Black Market averages about +40; without Thunder Dragon too, about +55.
- Endless duels: 2.3% past turn 40 (target under 2%), 0% capped, 0% draws.
- **Next:** let a base take a smaller splash or sit an event out (ABC, HAT, Thunder Dragon), then measure again.

**Tuned decks sit out (same day).** `size:` lets a base take fewer cards; a smaller splash didn't help HAT (-38 with one
card) or Thunder Dragon (-7 to -3 with two), so they sit out with ABC; Stall-Burn sits Dumpster Row out too (-3 to +5,
and its duels with Goat Control were most of the long ones). Every base measured, 200 duels each:

| Event | Bases | Gain | Target | Speed-up |
|---|---|---|---|---|
| Dumpster Row | goat-control, chaos-yata, warriors | +52 | +60 to +150 | 0.4 turns |
| The Basement Game | 8 (all but Wind-Up, Dragon Ruler, HAT) | +57 | +60 to +150 | 0.6 |
| The Black Market | shaddoll, kozmo, danger-dark-world | +50 | +60 to +150 | 0.6 |

- All three are within about 10 points of the target, which is about the measuring error over 6-8 decks at 200 duels.
- Endless duels pass again: 0.8% past turn 40 (from 2.8%), 0% capped, 0% draws.
- **The cost is variety:** Dumpster Row and the Black Market have three bases each. When the Alley is built (step 9),
  more bases from those eras would help: decks built for the Alley rather than ones that only fit its rating range.

**Endless duels pass** (`--endless-test`, 3,000 alley duels among 18 splash decks): 0.0% hit the prompt cap (target
under 0.5%), 1.8% went past turn 40 (under 2%, barely; mostly Warriors against Stall-Burn), 0.0% draws (under 1%).

## Brackets

**The Elo odds don't predict head-to-head results closely enough** (`--bracket-calibrate`, 15 deck pairs across the
range, 400 bot duels each): the measured gap is off from the rated gap by **85 Elo on average**, against a target of
about 8 (chance alone at 400 duels is about 17). Some pairs are far off: Snake-Eye against Performapal is rated +200 and
plays +413; Dragon Ruler against Qliphort is rated +297 and plays +72. Matchups matter more than one number per deck.

- **Next:** settle CPU-vs-CPU bracket matches from a measured matchup table for the decks a bracket can draw (bot duels
  are fast: about 50 a second), or rebuild the ratings with far more games and accept the error.

**Title odds by rating** (`--bracket-sim`, on the current ratings, so rough): a 1,800-rated deck takes an early Library
Locals 70-75% of the time (target 30-45%) but a late one 6-11%; Alley Brawl 62%; Midnight Open (16) 6% at 1,800 and 22%
at 1,947 (target 15-30%). Fields need tuning per cup once the matchup table exists.
