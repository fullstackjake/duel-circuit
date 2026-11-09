# QA playtest, 2026-10-30 evening (after the day's work)

A fresh save, `-qaplay -qafull -qaloaners -qatools -qagames 230`, muted, on f58e14e. That build has everything from
2026-10-30:
- the loaner playbook fix;
- the Design division's review fixes;
- the Stuck? "Make it yours" purchase (the QA stand-in takes it when offered);
- plain trait words;
- the first-hour fixes from the persona walkthrough.

All nine app checks passed on the build first. Log: `build/qa-night5/out/qa-log.txt`. The run to compare with is the
10-30 morning one ([QA-PLAYTEST-2026-10-30.md](QA-PLAYTEST-2026-10-30.md), `build/qa-night3`).

## Results

**229 duels, 168 won, 61 lost, 0 issues**, 3 h 53 min. The first campaign ended at duel 170 (last run: 182).

| | 10-30 morning | Tonight |
|---|---|---|
| Whole run | 158-71 | **168-61** |
| Cleared by the end (incl. New Game+) | 9 circuits, 3 Tag Cups | **11 circuits, 4 Tag Cups** |
| Master circuits | 15-15 | 15-16 |
| Tier 0 Gauntlet | 4-1 | 4-2 |
| DP at the Gauntlet's clear | 277,302 | 276,379 |
| DP spent by then (share of earnings) | 19,490 (6.6%) | **26,320 (8.7%)** |
| End of run | 336,882 DP, 934 cards | 354,554 DP, 1,005 cards |

| Circuit | 10-30 morning | Tonight |
|---|---|---|
| Master League: Link Wars | 4-12 | **4-3** |
| Expert Series: The New Wave | 4-7 | **4-0** |
| Expert Series: Fall Format | 4-3 | **4-0** |
| Expert Tag Cup | 3-6 | **3-1** |
| Advanced Tag Cup | 3-3 | 3-0 |
| Expert Series: Shadows and Storms | 4-9 | 4-9 |
| Master League: October 2026 | 4-2 | 4-9 |
| Fall Format Tag Cup | 3-4 | 3-6 |
| Master League: Graveyard Kings | 4-1 | 4-3 |
| Master Tag Cup | 3-9 | 3-8 |

- **Link Wars, 4-12 down to 4-3:** what the loaner playbook fix (01d2b8e) was expected to do. The QA stand-in plays the
  Ryzeal loaner with its playbook now.
- **Kit's new line was seen:** "I play Mekk-Knight Invoked Evolved, and I play it my way." The "(new)" suffix is gone.
- **Late CPUs going second:** Master circuits plus the Gauntlet went 19-18, which is 51%. The Game Designer's rule
  switches K9, Ishizu and Eldlich to going second only above 65%, so **they stay as they are**.

## "Make it yours": taken once, and a bug found (fixed: 3fb1cd0)

At the Master Tag Cup's match 1 (stuck, measured advice: the Fire King Snake-Eye loaner with Garunix), the stand-in
bought the deck for 7,070 DP. That is all of tonight's extra spending: 8.7% of earnings against 6.6%.

**The bug:**
- The library saves a deck under a file-safe name, so "Fire King Snake-Eye / Azamina (recipe)" became
  "Fire King Snake-Eye - Azamina (recipe)".
- The purchase pointed your deck at the name with the slash, so the duel after it fell back to the loaner.
- The next visit found the saved copy and offered "Play your own copy". At match 2 the panel offered it again while
  it was already the deck being played.
- The Recipe Book's "Build this deck" named its decks the same way.

**The fix:** both now use the library's file-safe name, and the panel doesn't offer the copy you're already playing.
A new campaign check owns a deck with a slash in its title (campaign check 111; all nine pass).

## Two walls

**Shadows and Storms match 4 (Nessie Grapha, Danger! Dark World): 1-7 for the second run in a row.** Re-measured
tonight with `--circuit-study` (64 duels, `build/ss-why/now.txt`):

| Your deck | Won | Going first | Going second | In the app tonight |
|---|---|---|---|---|
| Mekk-Knight starter | 45% | 21% | 68% | 0-3, all going second |
| Kozmo loaner | 54% | 43% | 65% | 1-4, mostly going second |

From those rates, tonight's record (and last night's 1-7) is about a 1-in-500 outcome. **The app plays this match
worse than the harness does.** That points to an app/harness difference, like last night's missing loaner playbook,
not to luck. It's not found yet; the next step is to replay a lost app duel's seed in the harness and compare move by
move.

*Wrong, see the follow-up below: the harness replays these app duels exactly. The two runs were the same eight duels.*

**October 2026 match 2 (Warden Hask, Sacred Beast): 1-5.** The stand-in won every toss and went first with the
Tearlaments loaner. That deck measures 43% first and 62% second, a 19-point gap, just under the toss tip's 20-point
threshold, so no tip showed. Mostly bad luck. A 15-point threshold would show the tip here (your call).

## Follow-up, 2026-10-31: the Danger! Dark World wall was luck, not an app difference

**The harness replays the app's duels exactly.** The app's log names each duel's seed ("duel 90: You vs Nessie
Grapha, Nessie Grapha first"); match 4 was seeds 90-97. Replayed in the harness with your seat as the app has it
(`--circuit-study --only "Shadows and Storms" --match 4 --seeds 90-92 --first them --you-seat app`), every duel ends on
the same turn with the same LP as in the app:

| Seed | Deck | App (tonight) | Harness |
|---|---|---|---|
| 90 | Mekk-Knight starter | lost on turn 5, 0 / 8000 | lost on turn 5, 0 / 8000 |
| 91 | Mekk-Knight starter | lost on turn 3, 0 / 7700 | lost on turn 3, 0 / 7700 |
| 92 | Mekk-Knight starter | lost on turn 3, 0 / 7700 | lost on turn 3, 0 / 7700 |
| 93-97 | Kozmo loaner | 4 lost, then won on turn 8, 3900 / 0 | the same five endings |

**The two runs were one sample.** QA play starts at seed 1 and adds one per duel, so a fresh run that reaches a match
after the same number of duels plays the same duels. Both runs met Nessie at duels 90-97 and played them almost move
for move (the morning run's build ended seed 95 on turn 9 instead of turn 3). The "1 in 500" counted them twice.

**What else was checked** (`build/ss-why/`):
- **Your seat as the app has it** (`--you-seat app`: a HumanDuelist on Chain: Auto, picking zones, the stand-in
  answering what it shows): 42% overall, 21% first, 62% second. The same as Chain: Auto alone (`--you-chain auto`,
  the wall study's switch, now in the circuit study too).
- **Chain: Auto** costs about 6 points going second here (68% to 62%) and 3 overall: real, but small.
- **The planner's replays** never diverged, in the harness or the app-style seat (0 of about 7,500 rollouts at match
  4): the stand-in plans every decision it would in the harness.
- **The deck, card data and core** are the same: the saved "Mekk-Knight Invoked Evolved" is the starter, card for card
  and in order; `cards.bin` and the core library were built from the same sources.
- **Going second, from the seeds after the app's** (90-153, 64 duels, app seat): Mekk-Knight starter 53%, Kozmo loaner
  59%. At those rates 1-7 is about a 1-in-55 outcome, among some forty measured matches in a run.

**Where that leaves the match:** a wall at about 45% for the starter (the stand-in already goes second when it wins
the toss), and the Kozmo loaner a little better. No fix needed in the app.

**For future QA runs:** two runs from a fresh save are not independent samples. Compare a run against the harness at
the same seeds (`--seeds`), or give the app a different starting seed before reading a second run as new evidence.

New harness switches (`src/Ygo.Harness/CircuitStudy.cs`): `--you-chain auto`, `--you-seat app`, `--seeds a-b`,
`--first them|you`, `--match N`. Each duel played with `--seeds` prints how it ended. Every match also prints how many
of your planner's rollouts diverged (`TurnPlanner.DivergedRollouts`).
