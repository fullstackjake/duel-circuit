# The Back Alley's difficulty pass (ALLEY-SPEC Phase D, NL-1 chunk C4), 2026-11-03

Three tables were off target. This page records every measurement, the decisions and the alternatives rejected. The
coordinator folds it into ALLEY-SPEC's Phase D As-built.

## How it was measured

- `--alley-study --only <table> --games 3 --load build/qa-night5/mods`: 60 regulars over 20 nights, 3 duels each (180),
  30 a closer, headliner or authored seat. "Best" is the QA save's best deck, Fire King Snake-Eye - Azamina (rated 1654),
  made legal for the Alley (Bonfire to 1), as the earlier Phase C and D studies used it. "Starter" is classic (1276).
- The stand-in is the roster's best planner: **Jolt** (skill 1.0, planner), as in the Phase C and D studies. On 2026-11-03,
  before C3 kept No Limits pilots out of the chair, it was Anselm Quist. Nyx's seat 4 read 70.0% of 30 with Anselm and 63.3%
  of 30 with Jolt, a 2-duel gap that is noise (it would take about 700 duels a stand-in to see 7 points). The Anselm runs
  are kept only as history. Every number below is Jolt's unless it says Anselm. The coordinator's scratchpad
  baselines used the default best deck (goat-control) and are not used.
- A session restart between 2026-11-03 and 2026-11-05 wiped the scratchpad and its runs; everything was rerun on 2026-11-05 on a rebuilt c4
  harness (with C1b's playbooks and ratings and C3's harness changes).
- Runs are deterministic (seed 1); the same files give the same numbers. The error is ± one standard error.
- `--bracket-sim` (20,000 brackets a row, Elo odds) for the Alley Brawl.

## 1. Dumpster Row

Targets: best 75%, starter 65%, headliners 70%. Levers (owner): the bases, the closer and the curated list in
campaign/alley.txt. Only three bases are eligible (Elo 1276-1480, era 2006): goat-control, chaos-yata, warriors;
stall-burn sits out. Floodgates are boss-only, so the list can't use Imperial Order or Royal Oppression.

### Before

The list as shipped (16 cards, a three-card splash), closer chaos-yata, headliner Yata Mags (Return Rook isn't ready).

| Seat | Best deck won | Target | Starter won | Target |
|---|---|---|---|---|
| regulars (60 over 20 nights, 3 each) | 97.2% of 180 (± 1.2) | 75%, OFF | 35.6% of 180 (± 3.6) | 65%, OFF |
| closer, chaos-yata at 1.0 | 100.0% of 30 (± 0.0) | 75%, OFF | 13.3% of 30 (± 6.2) | 65%, OFF |
| Yata Mags (headliner) | 96.7% of 30 (± 3.3) | 70%, OFF | 30.0% of 30 (± 8.4) | none |

The starter's duels reach turn 40 in 4.4% of its regular duels (the cap decides them); nothing else does. No draws, no
first-turn losses. Measured with Anselm on 2026-11-03; Jolt on 2026-11-05 gave the same counts in every row it ran (all
but the starter against Yata Mags, which was stopped).

### Changes tried

- **V1, rejected: a five-card splash led by cards that punish combo.** Each base took five cards (`size:` 5), the list
  led with Chaos Emperor Dragon, Jowgen the Spiritualist, Yata-Garasu and Delinquent Duo, the rest as shipped.
  `--alley-gen`: 9 of 9 full and legal. `--alley-rate` (30 bot duels a deck against its base): +40 Elo on average
  (target +60 to +150), chaos-yata's two decks -47 and -23 (Jowgen and Solemn Judgment replace its Tribe-Infecting
  Virus and Ring of Destruction). The study: the best deck won **97.2% of 180 against the regulars and 100% of 30 against
  the closer, the same counts as the shipped list**. Era-2006 contraband doesn't touch a 1654 deck played by a planner.
  It can only make the starter's table harder, so the list went back to the shipped one (the run was cut short before
  its starter rows; not needed for the decision). The identical 175 of 180 is a coincidence, not a list that failed to
  load: the study reads alley.txt at start, and the game's regular builder takes its size from `size:` (Alley.cs:433).
  It says nothing about weaker decks than the QA deck.
- **Why the list can't do it.** The three eligible bases are rated 1401-1418, plus the table's +52 splash. 97% means an
  effective gap of about 600 Elo; 75% means about 190. Closing 400 Elo with 2006 cards isn't possible, and every base
  in the band (Elo 1276-1480, 90% of cards out by 2006) is already on the table; stall-burn sits out for its turn-40
  duels. The starter needs the opposite: at 35.6% it is about 100 Elo short of the regulars, and 65% would need them
  about 200 Elo weaker, which the owner rule forbids.

- **The bases and the closer, one base at a time.** Three scratch tables, each with one base as every regular and as the
  closer (games 3, Jolt):

  | Base | Best vs regulars | Best vs it as closer | Starter vs regulars | Starter vs it as closer |
  |---|---|---|---|---|
  | goat-control | 98.9% of 180 | 93.3% of 30 | 31.1% of 180 (10% at turn 40) | 16.7% of 30 |
  | chaos-yata (the shipped closer) | 97.8% of 180 | 100.0% of 30 | 35.6% of 180 | 13.3% of 30 |
  | warriors | 96.7% of 180 | 100.0% of 30 | 40.0% of 180 | 16.7% of 30 |

  The three starter rates average 35.6%, the shipped mix's figure. Dropping a base buys nothing: chaos-yata is the
  average, warriors alone would cost the table its variety for 4 points, and goat-control must stay first (a pinned
  `--campaign-test` check edits the line by the text `bases: goat-control,`). A closer at skill 1.0 beats the starter
  83-87% whichever base it is.
- **First pick, superseded: goat-control closing.** On the QA deck alone it looked best: the only closer the best deck drops
  games to (96.0% of 50 at games 5, against 100%), and the starter 22.0% of 50 against 13.3%. But the QA deck can't tell
  the closers apart, so they were measured again with goat-control as the best deck (the deck a player has when the alley
  opens; see the trade-off below), games 3, Jolt:

  | Closer | Best: QA deck (1654) | Best: goat-control (unlock) | Starter | Turn 40 or draws, goat-control |
  |---|---|---|---|---|
  | chaos-yata (shipped) | 100.0% of 30 | 53.3% of 30 (± 9.1) | 13.3% of 30 | none |
  | goat-control | 93.3% of 30 | 40.0% of 30 (± 8.9) | 16.7% of 30 | 6.7% at turn 40, 1 draw (3.3%) |
  | warriors | 100.0% of 30 | 56.7% of 30 (± 9.0) | 16.7% of 30 | none |

  A goat-control closer against a goat-control player is a control mirror: the hardest closer for that deck, and the
  only one with turn-40 duels and a draw.
- **Decision: warriors closes** (`closer: warriors`), on design grounds more than data. The data separates warriors from
  goat-control (2-5 duels in 30, and goat-control is a control mirror with turn-40 duels against the unlock deck), but not
  from the shipped chaos-yata: they differ by one duel in 30 in two rows and tie in the third. Telling a 10-point gap
  apart at about 60% would take about 200 duels a closer. The closer sits only once Yata Mags is beaten, so the change is
  small either way; going back to chaos-yata costs nothing measurable.

### After and the trade-off (Jolt, games 5, 2026-11-05)

`closer: warriors`; the bases and the list as shipped.

| Seat | Best: QA deck, before | Best: QA deck, after | Best: goat-control, after | Target | Starter before | Starter after | Target |
|---|---|---|---|---|---|---|---|
| regulars | 97.2% of 180 (± 1.2) | 97.7% of 300 (± 0.9) | 66.7% of 300 (± 2.7) | 75% | 35.6% of 180 (± 3.6) | 33.7% of 300 (± 2.7) | 65%, OFF |
| closer | 100.0% of 30, chaos-yata | 100.0% of 50, warriors | 60.0% of 50 (± 6.9) | 75% | 13.3% of 30 (± 6.2) | 22.0% of 50 (± 5.9) | 65%, OFF |
| Yata Mags | 96.7% of 30 (± 3.3) | 98.0% of 50 (± 2.0) | 56.0% of 50 (± 7.0) | 70% | 30.0% of 30 (± 8.4) | 26.0% of 50 (± 6.2) | none |

With the QA deck every best-deck row is OFF (over by 22-28 points). With goat-control the regulars are ok (8 under) and the
closer and Yata Mags are OFF (15 and 14 under). The regulars don't change in either (the same list and bases). The
starter's and goat-control's duels against the regulars reach turn 40 in 4.3%, and goat-control's against the closer in
2% (1 of 50); nothing else does. No draws, no first-turn losses.

**Neither target can be met with era-2006 material and full-strength CPUs.** The two targets ask for one table to give a
1654 deck 75% and a 1276 deck 65%. On Elo odds that needs a seat near 1460 (190 under the first) and near 1170 (110
under the second), and no seat can be both. The best trade-off is the shipped bases and list with warriors closing: for the starter the
closer row goes from 13.3% to 22.0%, for the QA deck nothing changes, and for goat-control it is the closest closer to 75%.

**Which target I'd move: the best deck's, by measuring it with the deck a player has when the table opens.** The alley
opens at the first Library cup clear, and the measured best deck there is goat-control (the Goat Cup, playing like 1559
by library-advice.txt). The QA save's Fire King Snake-Eye is a late-Library deck, rated 1654, about 175 over the top of the
table's base band (1480). With goat-control as the best deck, the table reads 66.7% against the regulars (target 75%, ok), 60.0% against the
closer and 56.0% against Yata Mags: a table that is a little hard for the unlock deck, not one that runs easy. The starter's 65% I'd
move to about 35%, what the table gives classic at full strength, unless the owner wants a lever that isn't a weaker CPU
(open questions).

## 2. Nyx's Table, seats 1-3

Target: best 55% at each seat. Levers (META-DECKS owner decision 5): era-true, older decks; as-built roster decks from
decks/history seated `| as-built`, or a different event and splash for the regular seats; skills up, never down.

### Before

Seats as shipped: seat 1 a Black Market regular at 0.92, seat 2 Baron Corvus's chaos-yata as built (1414) at 0.95, seat 3
the stand-in (a Black Market regular at 0.97; Lab Coat Lyle isn't ready), seat 4 Nyx's Gravekeeper's Contraband
(anchored 1826) at 1.0. Jolt, 2026-11-05, games 3:

| Seat | Opponent | Best deck won | Target |
|---|---|---|---|
| 1 | Black Market regulars, 0.92 | 70.0% of 60 (± 5.9) | 55%, OFF |
| 2 | Baron Corvus, chaos-yata as built, 0.95 | 96.7% of 30 (± 3.3) | 55%, OFF |
| 3 | stand-in regulars, 0.97 | 78.3% of 60 (± 5.3) | 55%, OFF |
| 4 | Nyx, gravekeepers-contraband, 1.0 | 63.3% of 30 (± 8.8) | 55%, ok |
| all | every seat | 76.1% of 180 (± 3.2) | 55%, OFF |

No duel reached turn 40, drew or was lost on the first turn. (With Anselm on 2026-11-03 the same seats read 76.7%,
93.3%, 78.3% and 70.0%.)

### Changes tried

First tried on 2026-11-03 from a scratch root, **with Anselm** on the old build (superseded by the Jolt runs below):

- **Skill alone, seat 1 at 1.0 (from 0.92):** 71.7% of 60 (± 5.8), against Anselm's 76.7%. The skill knob (sloppy
  judgment calls at (1 - skill) × 0.6, chain refusals at (1 - skill) × 0.5) is too small to carry a regular to 55%.
- **Seat 2, thunder-dragon as built (Volta Kask, 1693, era 2018, skill 1.0) for chaos-yata (1414):** 50.0% of 30
  (± 9.1), on target. It is the strongest era-true (2019 or earlier) rated roster deck; everything rated above it in
  decks/history is 2020 or later (eldlich 2020, light-and-darkness 2026, chaos-ritual, kashtira, angelechy). Baron
  Corvus has no story line at Nyx's table (CHARACTERS.md only), so the chair changes hands without a story edit.

**Remeasured with Jolt on today's build (2026-11-05).** The session restart wiped the runs above, and C3's stand-in
filter (no No Limits pilots) puts Jolt back in the chair, so these replace them. Seats 1 and 3 with the edits applied
(regulars at 1.0, seat 2 Thunder Dragon), the Black Market list as shipped (four cards a base):

| Variant | Seat 1 | Seat 3 |
|---|---|---|
| regulars at 1.0, four-card Black Market splash (as shipped) | 66.7% of 60 (± 6.1), OFF | 76.7% of 60 (± 5.5), OFF |
| the same with a six-card splash (`size:` 6 for shaddoll, kozmo, danger-dark-world) | 73.3% of 60 (± 5.7), OFF | 81.7% of 60 (± 5.0), OFF |

The six-card splash is easier: the extra cards cut the bases' own cards (kozmo loses Fiendish Chain, Solemn Warning and
Breakthrough Skill; shaddoll loses Effect Veiler and Super Polymerization). Rejected.

**The era-true pool is used up.** Every rated decks/history deck with `#era` 2019 or earlier, by rating: thunder-dragon
1693 (now seat 2), danger-dark-world 1618, kozmo 1613, shaddoll 1592 (The Black Market's three bases), abc 1585 (skipped
by the Black Market list: -81 with a splash), then wind-up 1526 and lower. So no other older deck can sit at seats 1 or 3
as built and be stronger than the Black Market regulars already there.

**The Black Market table itself** (not in this pass's scope; measured because Nyx's regular seats draw from it): the best
deck wins 82.0% of 150 (± 3.1) against its regulars (target 60%, OFF).

### More splash sizes tried (Jolt, games 3, seats 1 and 3, `size:` lines for the three Black Market bases)

| Black Market splash | Seat 1 | Seat 3 |
|---|---|---|
| two cards | 75.0% of 60 | 83.3% of 60 |
| three cards | 73.3% of 60 | 80.0% of 60 |
| four cards (as shipped) | 66.7% of 60 | 76.7% of 60 |
| six cards | 73.3% of 60 | 81.7% of 60 |

Four is lowest by 4-5 duels in 60, under one standard error, and picking the lowest of four is biased toward a lucky
draw. So no size is shown to be harder; the list stays as shipped because nothing beat it. (Six cards at the Black Market table itself: 80.0% of 150 against 82.0%
shipped.) Why seat 2 does so much better than regulars rated only 35 lower: not the personality, since every regular
at Nyx plays at 0.9 or above and so plans as Volta Kask does. Most likely the matchup (not measured): Thunder Dragon
Colossus stops the opponent adding cards from the deck except by drawing, and the QA deck is a Snake-Eye deck that
searches. If so, seating Danger Dark World or Kozmo as built wouldn't repeat it.

### After (Jolt, games 5, 2026-11-05)

Seat 1 a Black Market regular at 1.0, seat 2 Volta Kask's thunder-dragon as built at 1.0, seat 3's stand-in a Black Market
regular at 1.0, seat 4 unchanged; the Black Market list as shipped.

| Seat | Before | After | Target |
|---|---|---|---|
| 1 | 70.0% of 60 (± 5.9) | 75.0% of 100 (± 4.3) | 55%, OFF |
| 2 | 96.7% of 30 (± 3.3), chaos-yata | **56.0% of 50 (± 7.0), thunder-dragon** | 55%, ok |
| 3 | 78.3% of 60 (± 5.3) | 76.0% of 100 (± 4.3) | 55%, OFF |
| 4 (unchanged) | 63.3% of 30 (± 8.8) | 60.0% of 50 (± 6.9) | 55%, ok |
| every seat | 76.1% of 180 (± 3.2) | 69.7% of 300 (± 2.7) | 55%, OFF |

No duel reached turn 40, drew or was lost on the first turn. `--endless-test`: thunder-dragon at Nyx, 50 duels, none at the
prompt cap or turn 40.

- **Seat 2 is probably in band** with an era-true deck: Thunder Dragon (2018, 1693, Volta Kask's) for chaos-yata (2004,
  1414). 56.0% of 50 is ± 7, so the true rate is roughly 42-70%; about 200 duels would pin it to ± 3.5.
- **Seats 1 and 3 stay about 20 points off.** Their skill went to 1.0 (owner rule: full strength), which showed no large
  change (70.0% to 75.0% and 78.3% to 76.0%; each difference has a standard error of about 7, so only a change of about
  15 points would show). It is kept for the rule, but it can go back to 0.92 and
  0.97 if the coordinator would rather shrink the pinned-test edits. The Black Market bases are the strongest era-true
  decks after Thunder Dragon, the splash size is at its best, and the table's own regulars run 82% too. So era-true
  material can't bring these seats to 55%; seating the bases as built doesn't either (open question 5). Seat 3 changes
  when Lab Coat Lyle is ready (his seat, not the stand-in's).
  The levers left are the owner's (open questions).

## 3. The Alley Brawl after The Black Market

Target: a champion's best deck (the late-Library best, 1879, and the best legal in the Alley, chaos-ritual 1896) takes
the title 30-45%. Lever: NL-1's out-of-towners (spec section 8); the next lever is a fourth Basement card a Brawl deck.

### Before (2026-11-03, the c4 build with C1a's `after:` lines and C1b's four Brawl decks on disk, unrated)

`--bracket-sim`, 20,000 brackets a row, Elo odds:

| Alley Brawl field | late-Library best (1879) | chaos-ritual (1896) | target |
|---|---|---|---|
| after Dumpster Row (7 Dumpster regulars, mean 1453-1470) | 77.6% | 79.4% | 30-45%, OFF |
| after The Basement Game (7 Basement regulars) | 69.9% | 72.2% | 30-45%, OFF |
| after The Black Market: 4 out-of-towners at seeds 8/5/2/3 + 3 Basement regulars | 70.6% | 72.6% | 30-45%, OFF |

The out-of-towners already sit (rue #2, sasha #3, augie #5, junia #8) but each shows Elo 1500: the placeholder for a
deck campaign/peak-ratings.txt doesn't rate yet (it holds gravekeepers-contraband only). The row is not a measurement
of the fix until the decks chunk writes the four Brawl decks' anchored ratings; then `--bracket-sim` reads them in 9 s.

### After: the out-of-towners rated (C1b's anchored ratings, 2026-11-03; C3's new rows)

campaign/peak-ratings.txt now rates the four Brawl decks: nl-vanquish-soul-brawl 1715, nl-snake-eye-brawl 1695,
nl-yubel-brawl 1679, nl-tenpai-brawl 1586. `--bracket-sim`, 20,000 brackets a row:

| Alley Brawl after The Black Market | late-Library best (1879) | chaos-ritual (1896) | target |
|---|---|---|---|
| out-of-towners at seeds 8/5/2/3 + 3 Basement regulars (as shipped) | 46.4% | 49.4% | 30-45%, OFF (just over) |
| comparison: the four as built, seeded by Elo | 57.8% | 60.4% | 30-45%, OFF |

The spec's model said 42-43% and 46%; the measurement is 3 points higher, mostly because tenpai's Brawl deck rates 89
under its base (1675 as built, 1586 Alley-legal with Raigeki, Heavy Storm and Change of Heart for two Lightning Storm and
a Koa'ki Meiru Drago; how the 89 splits between the Alley list and the splash is unmeasured).

**How much the next lever has to add.** The same simulation with the four Brawl decks' ratings raised in a scratch copy:

| Brawl decks' rating change | late-Library best (1879) | chaos-ritual (1896) |
|---|---|---|
| none | 46.4% OFF | 49.4% OFF |
| +15 | 43.7% | 46.8% OFF |
| +25 | 42.0% | 45.0% OFF |
| +40 | 39.3% | 42.2% |
| +60 | 35.5% | 38.7% |

So each Brawl deck needs about +30 to +40 Elo on average.

**The fourth card, as the splash builder picks it.** `--brawl-gen snake-eye,k9-vanquish-soul,yubel,tenpai-dragon --seed 1`
from a scratch copy of the harness with `BrawlSplash = 4` (the shipped one is 3): each keeps its three cards and adds one.

| Brawl deck | Fourth card | Cut for it |
|---|---|---|
| nl-snake-eye-brawl | Harpie's Feather Duster | nothing (43 to 44 cards) |
| nl-vanquish-soul-brawl | Raigeki | nothing (43 to 44 cards) |
| nl-yubel-brawl | Crush Card Virus | Gruesome Grave Squirmer |
| nl-tenpai-brawl | Pot of Greed | the second Koa'ki Meiru Drago |

**Rated (2026-11-05)**, `--rate-anchored` (600 duels a deck against the frozen pool, seed 1) from a scratch root, the four-card
decks beside the shipped three-card ones in the same run:

| Brawl deck | Three cards (official, C1b) | Three cards (this run) | Four cards (this run) | Fourth card's change |
|---|---|---|---|---|
| nl-vanquish-soul-brawl, + Raigeki | 1715 | 1741 | 1763 | +22 |
| nl-snake-eye-brawl, + Harpie's Feather Duster | 1695 | 1729 | 1703 | -26 |
| nl-yubel-brawl, + Crush Card Virus | 1679 | 1655 | 1634 | -21 |
| nl-tenpai-brawl, + Pot of Greed | 1586 | 1558 | 1554 | -4 |

The fourth card changes the decks by -7 on average (-26 to +22), against the +30 to +40 the Brawl needs. The same
three-card decks re-rated 24-34 points away from C1b's figures, and the run's calibration failed on one pool deck
(lunalight, -52; the other four within 16), so a single anchored rating carries about ± 25 here. The fourth card's
effect is inside that noise. Title odds from each set of ratings (`--bracket-sim`, ratings swapped in a scratch copy):

| Ratings used | late-Library best (1879) | chaos-ritual (1896) |
|---|---|---|
| C1b's official three-card ratings (as shipped) | 46.4% OFF | 49.4% OFF |
| this run's three-card ratings | 43.8% | 46.8% OFF |
| this run's four-card ratings | 44.7% | 47.7% OFF |
| whichever version rated higher per deck | 42.3% | 45.4% OFF |

**Seeding doesn't help either.** Augie at 8 and Junia at 5: 46.1% / 49.0%. Also Sasha at 2 and Rue at 3: 46.2% / 49.1%.
You meet each out-of-towner once whatever their seed.

**Decision: the fourth card isn't recommended.** It doesn't raise the decks and makes three of four weaker. The list
above is handed to the coordinator with this measurement; the decks chunk owns decks/no-limits and would re-rate. The
Brawl after The Black Market stays at 46.4% / 49.4%: at the edge, and the data can't say which side. A rating here is
about ± 25 at one standard error (± 50 at 95%), and the 20,000-bracket simulation adds only ± 0.35 points; settling it
would take about 5-6 fresh-seed re-ratings a deck (about 3,500 duels each), or playing the brackets as duels. Rejected alternative: the seeding swaps above. Not tried: a smaller splash for tenpai (the spec fixes three
cards, and how much of its -89 is the splash rather than the Alley list's one Chundra and one Sangen Kaimen is
unmeasured). The levers left are the owner's (open questions).


## Checks

Run 2026-11-05 on the c4 build with this pass's edits (Nyx's seats, Dumpster Row's closer; alley.txt as shipped).
`--campaign-test` was run with both closers (goat-control, then warriors): the same 5 failures both times. The other
checks ran with goat-control closing; the closer is a splash regular, which `--endless-test` and `--arbitrage` cover
through the event's splash decks, not by name.

- `--alley-gen`: 0 decks short or illegal (every event: Dumpster Row 3 bases, The Basement Game 8, The Black Market 3).
- `--campaign-test`: **5 checks failed**, all Nyx's pinned seats in src/Ygo.Harness/AlleyTest.cs (C3's file): the seat
  skills 0.92 / 0.95 / 0.97 and seat 2 as chaos-yata (lines 1709-1716, 1721-1723, 1740), seat 2's as-built deck running an
  excluded card (1753-1758; Thunder Dragon runs none) and chaos-yata's rating removed (1776). The same build on the shipped
  seat lines passes every check, and the closer change fails none. Unity's `-alleycheck` step 13 pins seat 2 as chaos-yata
  too (DuelController.AlleyCheck.cs:882, C2's file; not run). The edits each needs are in the shared notes' C4 hand-over.
- `--endless-test --games 1200`: the alley group ok (1,600 duels, 18 splash decks and 8 authored decks: prompt cap 0.1%,
  turn 40 0.4%, draws 0%). Nyx's seat 2, thunder-dragon: 50 duels, won 72%, none at the prompt cap or turn 40. One authored
  deck off: the Gumblar Gail headliner (1 of 50 decided at turn 40), unchanged by this pass. The No Limits group is off
  (nl-branded turn 40 5.6%, nl-yubel prompt cap 5.6%, nl-snake-eye-brawl turn 40 2.8%): C1b's decks.
- `--arbitrage --load build/qa-night5/mods`: no loop is DP-positive. Exit 1 for one No Limits bound: loop 5, a restored
  shelf slot sells back 18.0% through Moth's Sell tab (limit one sixth, 16.7%). Not this pass's files.

## Files changed

- campaign/alley-events.txt: Nyx's Table seats 1-3 (seat 1 skill 1.0; seat 2 `authored thunder-dragon thunder-dragon |
  skill 1.0 | as-built`; seat 3's stand-in skill 1.0) and their comment; Dumpster Row `closer: warriors` and its
  comment. Nothing else (the No Limits table, the builds and the Brawl's `after:` lines untouched).
- campaign/alley.txt: no net change (the five-card Dumpster Row list was tried and reverted to HEAD).
- docs/dev/ALLEY-DIFFICULTY-2026-11-03.md: this record (new).
- Outside the repo: the shared notes' C4-handover-2026-11-05.md (the pinned checks to update, the Brawl's fourth-card list).
- Nothing committed. decks/no-limits, the harness, the engine and Unity untouched.

## Owner decisions

Decided (owner, 2026-11-05), answering the questions below:

1. **Dumpster Row's targets stay as written** (best 75%, starter 65%, headliner 70%). The table is recorded as off target
   with the QA deck (97.7% / 33.7%); goat-control, the deck at unlock, reads 66.7% against its regulars.
2. **The regulars' event skills stay as intended** (0.7 at Dumpster Row, 0.85 at The Basement Game, 0.95 at The Black
   Market): a deliberate difficulty step, not weakening.
3. **Nyx's Table ramps.** Seat 1 runs about 70-75% as the easier opener; seat 3 gets fixed when Lab Coat Lyle is ready.
   No more work on seats 1 and 3 in this pass.
4. **The Alley Brawl is accepted as is** at 46.4% / 49.4%. No fourth card, no further measuring.
5. **The Black Market gets a difficulty pass of its own, later** (with its headliners under the Headliners doc).
6. **Dumpster Row keeps warriors as its closer.**
7. **Nyx's seat 2 isn't rechecked against a second deck for now** (it stays as measured with the QA deck).

## Open questions for the owner

1. **Dumpster Row's targets.** Measure the best-deck target with the deck a player has at unlock (goat-control), not the
   late QA deck? And move the starter's 65% to about 35%, or name a lever that isn't a weaker CPU?
2. **Nyx's seats 1 and 3 stay at about 75%** with era-true material (seat 2 is fixed at 56%). The levers left: an alley
   event of Nyx's own (a bigger or different 2019 splash, which is engine code in Alley.Events); readying Lab Coat Lyle for
   seat 3; or accepting Nyx's Table at about 70% overall. Which?
3. **The Alley Brawl after The Black Market reads 46.4% / 49.4%** (1-4 points over 45%), and the fourth Basement card
   doesn't help (-7 on average). Accept it inside the rating noise, or try a structural lever (one of the three Basement
   regulars swapped for a stronger entrant; NO-LIMITS-SPEC section 8 fixes the field)? Both reviews would first check the
   Elo model against real duels: the champion's deck against each Brawl deck, about 100 duels each.
4. **The regulars' skill vs the full-strength rule.** Alley.cs gives Dumpster Row's regulars skill 0.7 and The Basement
   Game's 0.85 (ALLEY-SPEC section 7). Below 0.9 a CPU doesn't plan ahead, and below 1.0 it makes sloppy calls ((1 - skill)
   × 0.6 of judgment calls) and skips chains ((1 - skill) × 0.5). That reads as weakening a CPU. Raise them to 1.0 (engine
   code, then remeasure; the starter's 33.7% would drop), or confirm the event skills are intended? (Raised by a Game
   Designer review, 2026-11-05.)
5. **Nyx's seats 1 and 3 as authored, as-built decks** (a Game Designer review's first option) were **measured and don't
   help**. Seat 1 as built, Jolt, 100 duels each (2026-11-05): danger-dark-world 71.0% (± 4.5), kozmo 85.0% (± 3.6),
   against the generated regulars' 75.0% (± 4.3). So the regulars aren't weak because they're generated; the Black
   Market's bases are just a poor match for the QA deck. Thunder Dragon's 56% at seat 2 is the odd one out, most likely
   its matchup (Thunder Dragon Colossus stops searching; the QA deck is a Snake-Eye deck that searches). That also means
   seat 2's fix may be specific to this QA deck: worth checking with a second strong deck before calling it done.
   Mekk-knight-invoked would need a 2019 cut from the decks chunk (its list is era 2026); unmeasured.
6. **The Black Market table runs easy too** (outside this pass): the best deck wins 82.0% of its regulars (target 60%),
   80.0% of its closer and 86.7% / 93.3% of its headliners (target 55%). A pass of its own?
