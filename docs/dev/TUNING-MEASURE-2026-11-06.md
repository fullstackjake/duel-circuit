# Tuning measurements, 2026-11-06 (overnight job 6)

**These are measurements, not decisions.** The owner un-parked the tuning *measurements* only: nothing was retuned, no
data or source file was changed, nothing was committed. Every number comes from the frozen harness copy
`build/bin/overnight/Ygo.Harness.dll` (c80b436, tonight's starting point). The harness reads the repo's data files
(`campaign/`, `decks/`, `personalities/`) live; the only data file that changed during the runs was `campaign/story.txt`,
which no study reads, so the figures are all c80b436's tuning. The QA stand-in is Jolt (skill 1.00, planner on); "your best
deck" in the Alley studies is Fire King Snake-Eye - Azamina (rated 1654, `--load build/qa-night5/mods`). Raw logs are in
`build/overnight/measure/` (file names are given per study). Runs are deterministic: the same flags replay the same seeds.

"± err" is one standard error of the win rate (a coin-flip estimate: about two of them is a rough 95% interval). The
studies' OFF flag is the harness's own: more than 10 points from a point target, outside a range target, or outside the band.

## Summary of the findings

1. **No Limits does not pass its ship gate as built.** With the reference (tearlaments, 1947) the builds win
   53% to 90%: 3 of 7 inside the 45-65% band (the gate needs Junia and all 6 pool builds). Pool mean **71.1% ± 3.4**
   against 55% ± 5. Junia's end chair 63.3% ± 8.8 is inside the band, at its top edge. The pool is too soft (the builds are
   rated 1673-1865; the table's window is 1840-1990). Sangen (90%), Habakiri (77%), Lacrima (73%) are the loosest.
2. **Gain over base is mostly off the +100 to +250 Elo window**, and the measured gain is negative for four builds
   (Detonator, Habakiri, Albion, Lacrima), at 20 duels per base (± 11%); a 60-duel rerun (six builds) keeps the signs: Habakiri -120, Detonator -83, Albion -70 Elo, Spoils +83, Noroi +120, Sangen +134 (section 1c). One of Lacrima's 60-duel games never ended (the engine looped in a battle command), which may be related to the Yubel prompt-cap stops of the endless gate (the hung game's pairing is unknown).
3. **Endless gate:** the aggregate passes at three sample sizes (prompt cap 0.4-0.5%, turn 40 0.8%, draws 0.0%), but the
   prompt-cap figure is right at the 0.5% line (24 of 5,000) and is **Yubel-shaped**: Lacrima (nl-yubel) 1.5% and the
   Yubel Brawl deck 2.2% of their duels, in both large runs. Albion (nl-branded) is over the 2% turn-40 line in both (3.3%,
   2.2%).
4. **The Black Market at --games 5:** best deck 83.6% against the regulars (target 60%), starter 8.4% (target 40%): the
   same picture as TODO's 82.0% / 11.3%. The lead candidate (thunder-dragon + danger-dark-world) was **not** measured: it
   needs a data edit, which this job may not make.
5. **Brackets:** the Alley Brawl is OVER at every stage for the champion's best deck (78-79% after Dumpster Row, 70-72% after
   The Basement Game, 46-49% after The Black Market's out-of-towners, target 30-45%). The Midnight Open is inside
   (26-30%, target 15-30%). The Invitational is 12.1% (late-Library best) and 23.0% (tearlaments) on the smoke field with
   Remy's wave-2 stand-ins at 1915 (target 10-20%). Five Locals are still OVER: HAT & Shaddoll 46.1%, ABC to SPYRAL 54.7%,
   Synchro Peak 70.3%, TOSS & Eldlich 61.1%, Rabbits & Rulers 73.8%.
6. **The regular tables, an up-to-date picture (games 3):** Dumpster Row 98.8% / starter 35.7% (targets 75 / 65), The
   Basement Game 95.7% / 35.2% (65 / 50), Nyx's Table 68.9% over every seat (55), Orvel's Table 53.3% (55-60).

## 1. No Limits' ship gate (NO-LIMITS-SPEC §4.7, §14.3)

### 1a. `--alley-study --only "No Limits" --games 3 --load build/qa-night5/mods` (log: `nl-study.txt`, 237 min)

Setup: the reference ("your best No Limits deck") is **tearlaments (1947)**, the highest-rated deck legal under the No Limits
list (next: chaos-ritual 1896, ryzeal 1867), played by Jolt. 30 duels a build (10 × games, first turn alternating, turn cap
40), 3 duels a drawn seat over 20 nights (60 pool seats). All seven wave-1 builds are `ready: no` (the study measures
every build whose deck exists). The table's `elo:` window is 1840-1990.

**Every build against the reference (the 45-65% band):**

| Build (pilot, deck, rated) | Wins of 30 | ± err | Band 45-65% | OK/OFF | Lost by turn 2 | Build's avg win turn |
|---|---|---|---|---|---|---|
| Spoils (junia, nl-snake-eye, 1728), end chair | 63.3% (19) | 8.8% | inside | OK | 13.3% | 3.6 |
| Habakiri (anselm, nl-mitsurugi, 1683) | 76.7% (23) | 7.7% | above | OFF | 3.3% | 4.0 |
| Detonator (tova, nl-ryzeal, 1673) | 66.7% (20) | 8.6% | above (by 1.7 points) | OFF | 3.3% | 5.8 |
| Noroi (augie, nl-vanquish-soul, 1763) | 60.0% (18) | 8.9% | inside | OK | 6.7% | 4.1 |
| Sangen (sasha, nl-tenpai, 1675) | 90.0% (27) | 5.5% | above | OFF | 0% | 4.7 |
| Albion (imogen, nl-branded, 1865) | 53.3% (16) | 9.1% | inside | OK | 20.0% | 3.5 |
| Lacrima (rue, nl-yubel, 1696) | 73.3% (22) | 8.1% | above | OFF | 3.3% | 5.9 |

Decided at turn 40, draws and first-turn losses were 0% in every row.

**The gate's other lines:**

| Line | Result | Target | OK/OFF |
|---|---|---|---|
| Builds inside the band | 3 of 7 (Junia + Noroi + Albion) | Junia and at least 6 pool builds (`NoLimitsShipPool`) | OFF |
| Pool, 20 drawn nights (60 seats x 3 duels) | 71.1% of 180, ± 3.4% | 55% ± 5 | OFF |
| Same nights from the direct rows (weighted by seat counts) | 70.0%, ± 3.3% | 55% ± 5 | OFF |
| End chair Spoils (Junia) | 63.3% of 30, ± 8.8% | the 45-65% band only (decision 2) | OK |

What it implies: the reference beats the pool by about 16 points more than the design wants (about +120 Elo of reference
advantage over the design's 55%). The builds' own ratings predict the pattern: Sangen (1675) at 90% and Habakiri (1683) at 77%
are the lowest-rated and the loosest; Albion (1865) at 53% sits where the spec's window put it. The spec's seats-1-3 window is
R - 110 to R + 35 = 1837-1982 for R = 1947, and six of the seven builds are rated below 1837. The end chair is
inside but close to the top of the band, and its 13.3% "lost by turn 2" is the highest of the table apart from Albion's 20%.

**Comparison rows (not gate rows): the QA save's best deck, Fire King Snake-Eye (1654), made legal for the No Limits list:**

| Build | Wins of 30 | ± err | In band |
|---|---|---|---|
| Spoils (chair) | 43.3% | 9.0% | out (2 points under) |
| Habakiri | 60.0% | 8.9% | in |
| Detonator | 56.7% | 9.0% | in |
| Noroi | 46.7% | 9.1% | in |
| Sangen | 73.3% | 8.1% | out |
| Albion | 36.7% | 8.8% | out |
| Lacrima | 43.3% | 9.0% | out |
| Pool, 20 nights | 58.3% of 180 | 3.7% | in (target 55% ± 5) |

The QA deck at 1654 wins 58.3% of the pool nights, against the spec's expectation of 25-35% for it ("the QA deck would win
about 25-35% here"). Either the QA deck plays above its rating here or the pool is softer than the ratings say; the reference
row says the same thing from the other side.

**Mirror (each build, a CPU at full strength, against the end chair Spoils; 30 duels each):**

| Build | Won | ± err | Elo odds from the ratings |
|---|---|---|---|
| Habakiri | 46.7% | 9.1% | 44% |
| Detonator | 40.0% | 8.9% | 42% |
| Noroi | 46.7% | 9.1% | 55% |
| Sangen | 16.7% | 6.8% | 42% |
| Albion | 50.0% | 9.1% | 69% |
| Lacrima | 31.0% | 8.6% | 45% |

Sangen (Tenpai) is far under its Elo odds against Spoils (16.7% against 42%) and Albion is 19 points under; the others are
within error of their ratings.

**Speed gate (§4.7): per build against its base, both played by the CPU against the reference (30 duels each, same seeds):**

| Build | Won build / base | Avg win turn build / base (diff) | Wins by turn 2 build / base | Verdict |
|---|---|---|---|---|
| Spoils (junia) | 37% / 37% | 3.6 / 5.2 (-1.5) | 13.3% / 0.0% | **OFF**: more than 1 turn faster, too many wins by turn 2 |
| Habakiri | 23% / 43% | 4.0 / 4.6 (-0.6) | 3.3% / 10.0% | OK |
| Detonator | 33% / 27% | 5.8 / 4.3 (+1.6) | 3.3% / 3.3% | OK |
| Noroi | 40% / 37% | 4.1 / 4.1 (0.0) | 6.7% / 3.3% | OK |
| Sangen | 10% / 13% | 4.7 / 5.3 (-0.6) | 0.0% / 3.3% | OK |
| Albion | 47% / 50% | 3.5 / 4.0 (-0.5) | 20.0% / 16.7% | OK |
| Lacrima | 27% / 23% | 5.9 / 5.7 (+0.2) | 3.3% / 0.0% | OK |

The reference loses **7.1%** of its duels to the builds by turn 2 (the lever `NoLimitsGuardTurns` 2 would be needed above
10%; it is 1 now). First-turn losses overall: 0%. The harness's closing line: "6 measurement(s) off target".

### 1b. `--endless-test` (logs: `endless.txt` default, `endless-3000.txt` games 3000, `endless-15000-seed3.txt` games 15000 seed 3)

The default run (no `--games`) is a smoke run: 220 No Limits duels, and the authored decks get 0. At that size two
duels (0.9%) fail the prompt-cap line, so the larger runs are the ones to read. The harness's gate line is the aggregate;
per-deck lines are also shown because the spec says "every build and Brawl deck".

**The aggregate gate (builds and Brawl decks together):**

| Run | No Limits duels | Prompt cap (< 0.5%) | Turn 40 (< 2%) | Draws (< 1%) | OK/OFF |
|---|---|---|---|---|---|
| default, seed 1 | 220 | 0.9% (2) | 0.9% | 0.0% | OFF (tiny sample) |
| games 3000, seed 1 | 1,000 | 0.4% | 0.8% | 0.0% | OK |
| games 15000, seed 3 | 5,000 | 0.5% displayed (24 of 5,000 = 0.48%) | 0.8% | 0.0% | OK, by one duel |

The whole-alley line (splash decks and the 9 authored decks included) is OK in the larger runs: 0.1% prompt cap, 0.5-0.7% turn
40, 0.0-0.4% draws; every authored deck (Orvel's house, Nyx's two, the six headliners) is OK.

**Per deck, games 15000 (454-455 duels each, ± err about 2 points on a win rate; the two columns are the gate lines):**

| Deck | Duels | Won | Prompt cap | Turn 40 | OK/OFF |
|---|---|---|---|---|---|
| build Spoils (nl-snake-eye) | 455 | 67.5% | 0.0% | 1.3% | OK |
| build Habakiri (nl-mitsurugi) | 455 | 58.5% | 0.4% | 0.0% | OK |
| build Detonator (nl-ryzeal) | 455 | 78.7% | 0.2% | 0.7% | OK |
| build Noroi (nl-vanquish-soul) | 455 | 73.2% | 0.0% | 0.4% | OK |
| build Sangen (nl-tenpai) | 455 | 59.3% | 0.2% | 0.4% | OK |
| build Albion (nl-branded) | 455 | 77.6% | 0.0% | **2.2%** | OFF (turn 40) |
| build Lacrima (nl-yubel) | 454 | 58.8% | **1.5%** | 0.4% | OFF (prompt cap) |
| Brawl nl-snake-eye-brawl | 454 | 65.0% | 0.2% | 0.7% | OK |
| Brawl nl-vanquish-soul-brawl | 454 | 65.6% | 0.4% | 0.2% | OK |
| Brawl nl-yubel-brawl | 454 | 57.0% | **2.2%** | 0.4% | OFF (prompt cap) |
| Brawl nl-tenpai-brawl | 454 | 42.7% | 0.0% | 1.8% | OK (just under 2%) |

What it implies: three of the same four decks are off in the games-3000 run (the fourth, nl-snake-eye-brawl, was at 2.2% turn 40 there) (Albion 3.3% turn 40; Lacrima 3.3% prompt cap;
nl-yubel-brawl 1.1% prompt cap), so Albion's long games and **Yubel's prompt-cap stops
repeat across seeds**. Nearly every logged prompt-cap stop involves a Yubel deck (Lacrima, nl-yubel-brawl), plus a few
with nl-snake-eye-brawl and nl-vanquish-soul-brawl. The logged example seeds (game seed / turn): 701160 / 12 and
701215 / 9 (games 3000), 707992 / 16 and 707981 / 3 (games 15000). Two of the logged stops are on turn 2-3. This is a
stalled-prompt problem to look at (a loop or an unanswerable prompt in a Yubel chain), not a tuning number.

### 1c. `--peak-check all` (logs: `peak-check.txt` default, 20 duels per opponent; `peak-check-60.txt` 60, see below)

Per build: 20 duels against its base, 20 against the reference tearlaments and 20 against the next build; the use rate is
over all 60 games a card was in (Extra Deck cards: over all 60). Gate: 60% use. **Gain over base** is the build against
its base, both at full strength (window +100 to +250 Elo). The sample is small: 20 duels carry ± 10-11% (about ± 70 Elo).

| Build | Gain over base (20 duels) | Window | OK/OFF | Rated gain | Avg win turn build / base | Wins by turn 2 build / base | Speed |
|---|---|---|---|---|---|---|---|
| Spoils (junia) | 60% ± 11%: +70 Elo | +100..+250 | OFF (low) | +116 | 4.4 / 5.1 (-0.7) | 5% / 5% | OK |
| Habakiri (anselm) | 45% ± 11%: -35 | | OFF (negative) | -184 | 4.6 / 4.9 (-0.4) | 5% / 10% | OK |
| Detonator (tova) | 25% ± 10%: -191 | | OFF (negative) | -194 | 5.0 / 5.9 (-0.9) | 0% / 15% | OK |
| Noroi (augie) | 70% ± 10%: +147 | | OK | +90 | 4.2 / 4.5 (-0.3) | **20%** / 0% | **OVER** (4 of 20 vs 0 of 20) |
| Sangen (sasha) | 75% ± 10%: +191 | | OK | +0 | 6.7 / 6.6 (+0.1) | 5% / 0% | OK |
| Albion (imogen) | 40% ± 11%: -70 | | OFF (negative) | +35 | 3.4 / 3.6 (-0.2) | 15% / 30% | OK |
| Lacrima (rue) | 40% ± 11%: -70 | | OFF (negative) | +43 | 5.5 / 6.2 (-0.7) | 5% / 0% | OK |

The goldfish lines (the turn each deck wins against a dummy that only sets) are in the log. Against the reference tearlaments the builds
won 35% / 30% / 15% / 20% / 10% / 45% / 25% (in the table's order, 20 duels each): these are the builds as an opponent
playing against tearlaments with both at full strength (the CPU's own pilots), not Jolt as in 1a; the two views agree (Sangen wins 10% here, 90% for Jolt's reference in 1a).

**Use rates (gate 60%):** 10 rows are under the gate, and the harness's closing line is OFF ("10 use rate(s) under 60%, cards
missing from their deck, or speed gates missed"). The LOW rows:

| Build | Card | Used / games it was in | Rate |
|---|---|---|---|
| Spoils | restored Fiendsmith's Lacrima x1 (Extra Deck) | 13 / 60 | 22% |
| Spoils | pick Union Carrier x1 (Extra Deck) | 9 / 60 | 15% |
| Habakiri | pick King of the Feral Imps x1 (Extra Deck) | 10 / 60 | 17% |
| Detonator | pick Fairy Tail - Snow x1 | 5 / 9 | 56% |
| Detonator | pick Lavalval Chain x1 (Extra Deck) | 9 / 60 | 15% |
| Detonator | pick Daigusto Emeral x1 (Extra Deck) | 5 / 60 | 8% |
| Noroi | pick Outer Entity Azathot x1 (Extra Deck) | 0 / 60 | **0%** |
| Sangen | pick Giant Trunade x1 | 3 / 10 | 30% |
| Lacrima | restored Fiendsmith's Lacrima x1 (Extra Deck) | 14 / 60 | 23% |

(Detonator's restored Ryzeal Detonator x2, Albion's restored Albion the Branded Dragon x2 and Chaos Sorcerer are at 67%,
63% and 60%: at or just over the gate.) The Extra Deck rows are measured over all games, so a card that only a long
route reaches reads low; Azathot at 0 of 60 is never summoned at all.

**Rerun at 60 duels per opponent (`--peak-check all --games 60`, log `peak-check-60.txt`, extra to what was asked):** the
default run's gain-over-base figures carry about ± 70 Elo, so the owner-facing numbers are these. Six builds finished; the
run was **killed with Lacrima (nl-yubel) unfinished**: one duel in it never ended. The process sat on one core inside the
engine's battle command (`field::process(BattleCommand)`, `get_attack_target`) for over an hour (the peak check has no
prompt cap or turn limit). Which duel it was is unknown (the run was in nl-yubel's block, so one of its three opponents); it may be related to the Yubel prompt-cap stops in 1b. Lacrima's 20-duel figures above are all there is.

| Build | Gain over base (60 duels) | Window +100..+250 | Avg win turn build / base | Wins by turn 2 build / base (limit) | Against the reference |
|---|---|---|---|---|---|
| Spoils (junia) | 62% ± 6%: +83 Elo | OFF (low; rated +116) | 4.7 / 5.4 (-0.7) | 8.3% / 1.7% (10%): OK | 38% |
| Habakiri (anselm) | 33% ± 6%: **-120** | OFF (negative) | 4.4 / 5.3 (-1.0 faster) | 8.3% / 8.3% (13.3%): OK | 30% |
| Detonator (tova) | 38% ± 6%: **-83** | OFF (negative) | 5.7 / 5.6 (+0.1) | 1.7% / 10.0% (15%): OK | 18% |
| Noroi (augie) | 67% ± 6%: +120 | OK | 4.1 / 4.2 (-0.1) | **16.7%** / 6.7% (11.7%): **OVER** | 28% |
| Sangen (sasha) | 68% ± 6%: +134 | OK | 6.1 / 5.3 (+0.8) | 5.0% / 3.3% (10%): OK | 20% |
| Albion (imogen) | 40% ± 6%: **-70** | OFF (negative) | 3.2 / 4.0 (-0.8) | 23.3% / 23.3% (28.3%): OK | 53% |
| Lacrima (rue) | not finished (20 duels: 40%, -70) | | | | |

What it implies (measurement only): the 20-duel signs hold at 60: **Habakiri, Detonator and Albion are measurably worse than
their own bases** (their bases are rated 1867, 1867 and 1830; the builds 1683, 1673 and 1865 in the rating files), Spoils is
a real but small gain (+83, under the +100 window), Noroi and Sangen are inside the window. Noroi's turn-2 wins (16.7%) fail
the speed gate at both sample sizes (20% and 16.7%). The use rates at 60: the same nine rows as above, nothing new LOW and
nothing recovered (Spoils' Lacrima 22%, Union Carrier 12%; Habakiri's Feral Imps 16%; Detonator's Lavalval Chain 19% and
Daigusto Emeral 7%; Noroi's Azathot 2% (3 of 180); Sangen's Giant Trunade 36%); Fairy Tail - Snow is now 77% (ok), Detonator
72%, Albion 72%, Chaos Sorcerer 76%.


## 2. The Black Market's difficulty (TODO's unfinished entry)

`--alley-study --only "The Black Market" --games 5 --load build/qa-night5/mods` (log: `blackmarket.txt`, 105.9 min). The
shipped table: 50 regulars (20 nights x seats), the closer (kozmo) and 2 headliners (Master Plan Marlo, Gumblar Gail);
5 duels a regular seat, 50 a closer or headliner. TODO's numbers were measured at games 3.

| Opponent group | Your deck | Wins | ± err | Target | OK/OFF | TODO (games 3) |
|---|---|---|---|---|---|---|
| regulars | best (1654) | 83.6% of 250 | 2.3% | 60% | OFF | 82.0% of 150 |
| closer (kozmo) | best | 84.0% of 50 | 5.2% | 60% | OFF | 80.0% of 30 |
| Master Plan Marlo | best | 86.0% of 50 | 4.9% | 55% | OFF | 86.7% |
| Gumblar Gail | best | 96.0% of 50 | 2.8% | 55% | OFF | 93.3% |
| regulars | starter (classic) | 8.4% of 250 | 1.8% | 40% | OFF | 11.3% of 150 |
| closer (kozmo) | starter | 10.0% of 50 | 4.2% | 40% | OFF | 6.7% |
| Master Plan Marlo | starter | 2.0% of 50 | 2.0% | (none) | | 0.0% |
| Gumblar Gail | starter | 12.0% of 50 | 4.6% | (none) | | 10.0% |

Decided at turn 40, draws and first-turn losses: 0% everywhere.

What it implies: the picture is unchanged by the larger sample (all TODO figures are inside one or two standard errors of
these): the best deck is 23.6 points over its 60% target, the headliners 31 and 41 points over 55%, and the starter 31.6
points under 40%. TODO's reading (a table's rate is about the mean of its bases' rates, 83.3%) still fits.
**The lead candidate** (`bases: thunder-dragon, danger-dark-world`, `closer: thunder-dragon`, `size: thunder-dragon 2` for
its `skip:`, expected about 66-68%) **was not measured**: it needs `campaign/alley-events.txt` edited (or a scratch root),
and this job may not touch data files. Nyx's remeasure after it is likewise pending. Not run (all need the candidate or
other edits): `--alley-gen`, `--campaign-test`, `--arbitrage` and the BLACK-MARKET-DIFFICULTY doc.
`--endless-test` ran (section 1b) and its authored-deck lines are OK.

## 3. Brackets

`--bracket-sim` (log: `bracket-sim.txt`, 18 s, 20,000 brackets a row). The Locals rows use `library-advice.txt`'s measured
rates for your best measured deck (those rates are inputs; their own sample sizes are not in this study), so a title-odds
figure here is as exact as those rates. The `--bracket-matchups` flag that TODO names for the Locals does not exist in the
harness at c80b436 (the nearest are `--bracket-sim` and `--bracket-calibrate`; the latter was not run).

### 3a. Library Locals (target 30-45% title odds)

| Cup | Your deck | Elo | Title | Final | Semi | OK/OFF |
|---|---|---|---|---|---|---|
| Goat Cup | goat-control | 1559 | 33.9% | 52.5% | 74.9% | OK |
| TeleDAD Cup | teledad | 1594 | 39.1% | 53.5% | 71.8% | OK |
| **Synchro Peak Cup** | six-samurai | 1767 | **70.3%** | 81.6% | 94.9% | OVER |
| **Rabbits & Rulers Cup** | dragon-ruler | 1869 | **73.8%** | 81.9% | 90.9% | OVER |
| **HAT & Shaddoll Cup** | six-samurai | 1699 | **46.1%** | 69.4% | 92.9% | OVER (by 1.1) |
| Nekroz & PePe Cup | kozmo | 1585 | 30.4% | 53.8% | 90.8% | OK |
| **ABC to SPYRAL Cup** | abc | 1746 | **54.7%** | 76.7% | 94.8% | OVER |
| Link Dawn Cup | thunder-dragon | 1758 | 41.1% | 63.2% | 90.0% | OK |
| **TOSS & Eldlich Cup** | eldlich | 1992 | **61.1%** | 84.0% | 96.8% | OVER |
| Tearlaments Cup | tearlaments | 1951 | 44.8% | 74.2% | 94.8% | OK |
| Kashtira & Snake-Eye Cup | kashtira | 1788 | 30.5% | 55.6% | 96.9% | OK |
| Ryzeal Cup | ryzeal | 1787 | 31.2% | 57.4% | 93.0% | OK |

Average 46.4%, 7 of 12 cups in the target. **The five easy Locals (bold)** against TODO's figures: HAT & Shaddoll 46.1% (TODO
46%), ABC to SPYRAL 54.7% (55%), Synchro Peak 70.3% (63%), TOSS & Eldlich 61.1% (66%), Rabbits & Rulers 73.8% (74%). Three
are unchanged; Synchro Peak is 7 points higher and TOSS & Eldlich 5 points lower than TODO recorded. Over the 30-45% target by:
1, 10, 25, 16 and 29 points. The Tearlaments Cup (44.8%) is at the top of the target.

### 3b. The Back Alley's brackets (target: Brawl 30-45%, Midnight Open 15-30%, Invitational 10-20%)

The champion's best deck is chaos-ritual (1896), the highest-rated deck legal in the Alley; "late-Library best" is 1879 (the
measured rates at the last four cups); the Invitational's reference is tearlaments (1947).

| Bracket | Late-Library best 1879 | chaos-ritual 1896 | tearlaments 1947 | Target | OK/OFF |
|---|---|---|---|---|---|
| Alley Brawl after Dumpster Row | 77.6% | 79.4% | | 30-45% | OVER |
| Alley Brawl after The Basement Game | 69.9% | 72.2% | | 30-45% | OVER |
| **Alley Brawl after The Black Market (out-of-towners)** | 46.4% | 49.4% | | 30-45% | OVER (by 1.4 / 4.4) |
| Alley Brawl after The Black Market (the four as built, seeded by Elo; comparison) | 57.8% | 60.4% | | 30-45% | OVER |
| Midnight Open (16, Bo3 final) | 26.4% | 29.6% | | 15-30% | OK (the 1896 row 0.4 under the top) |
| **No Limits Invitational, smoke: wave 1 as rated, wave 2 stand-ins at 1915, Remy 1947** | 12.1% | | 23.0% | 10-20% | OK / OVER |
| No Limits Invitational, smoke, spec 9.2 model (all eleven builds at 1915, Remy 1947) | 7.5% (UNDER) | | 15.5% | 10-20% | UNDER / OK |

The Brawl after The Black Market (the "rows" the spec asked for): the field is Rue (nl-yubel-brawl, mean Elo 1679) at seed 2, Sasha
(nl-tenpai-brawl, 1586) at 3, Augie (nl-vanquish-soul-brawl, 1715) at 5 and Junia (nl-snake-eye-brawl, 1695) at 8, with
regulars at 4, 6, 7. A rating sweep for that row: 1500 2.4%, 1600 7.1%, 1700 17.1%, 1800 32.1%, 1879 46.4%, 1896 49.4%,
1900 50.2%. The out-of-towners shave about 23 points off the Brawl after The Basement Game (69.9 -> 46.4% for the
late-Library best); the row is still over because the stage before it (70-72%) was already far over. The Invitational rows
are the harness's own smoke runs ("unconfirmed hints until the tuning pass"): they use stand-in ratings for wave 2 and Remy, and
`regulars from` is empty. The two Invitational models differ by 4.6 points at the late-Library best because the first uses
the builds' actual ratings (1673-1865) and the second the window's 1915.

## 4. The regular tables, up to date (`--alley-study --only <table> --games 3 --load build/qa-night5/mods`)

Logs: `study-dumpster.txt` (47.5 min), `study-basement.txt` (46.7), `study-nyx.txt` (37.3), `study-orvel.txt` (4.2 min).
"Best" is Fire King Snake-Eye - Azamina (1654), made legal for the Alley; "starter" is classic (1276). All rows: decided at
turn 40 0% and first-turn losses 0% unless stated.

### Dumpster Row (Street): targets best 75%, starter 65%, headliners 70%

| Group | Deck | Wins | ± err | Target | OK/OFF |
|---|---|---|---|---|---|
| regulars (56 seats, 3 duels each) | best | 98.8% of 168 | 0.8% | 75% | OFF |
| closer (warriors) | best | 100.0% of 30 | 0.0% | 75% | OFF |
| Yata Mags | best | 96.7% of 30 | 3.3% | 70% | OFF |
| Return Rook | best | 96.7% of 30 | 3.3% | 70% | OFF |
| regulars | starter | 35.7% of 168 (turn 40: 5.4%) | 3.7% | 65% | OFF |
| closer (warriors) | starter | 16.7% of 30 | 6.8% | 65% | OFF |
| Yata Mags | starter | 30.0% of 30 | 8.4% | (none) | |
| Return Rook | starter | 40.0% of 30 | 8.9% | (none) | |

TODO's 2026-11-05 figures (97.7% / 33.7%) are within error of these. The owner already keeps the targets and records this
table as off target.

### The Basement Game (Den): targets best 65%, starter 50%, headliners 60%

| Group | Deck | Wins | ± err | Target | OK/OFF |
|---|---|---|---|---|---|
| regulars (54 seats) | best | 95.7% of 162 | 1.6% | 65% | OFF |
| closer (six-samurai) | best | 90.0% of 30 | 5.5% | 65% | OFF |
| Mass Driver Moss | best | 96.7% of 30 | 3.3% | 60% | OFF |
| Gateway Gwen | best | 93.3% of 30 | 4.6% | 60% | OFF |
| regulars | starter | 35.2% of 162 (turn 40: 0.6%) | 3.8% | 50% | OFF |
| closer (six-samurai) | starter | 6.7% of 30 | 4.6% | 50% | OFF |
| Mass Driver Moss | starter | 56.7% of 30 | 9.0% | (none) | |
| Gateway Gwen | starter | 16.7% of 30 | 6.8% | (none) | |

### Nyx's Table (authored, 4 seats): target best 55% (the starter has no target)

| Seat | Wins | ± err | Target | OK/OFF |
|---|---|---|---|---|
| 1: regulars | 66.7% of 60 | 6.1% | 55% | OFF (11.7 over) |
| 2: Volta Kask (authored) | 63.3% of 30 | 8.8% | 55% | OK |
| 3: regulars | 76.7% of 60 | 5.5% | 55% | OFF (21.7 over) |
| 4: Nyx (authored) | 63.3% of 30 | 8.8% | 55% | OK |
| every seat | 68.9% of 180 | 3.5% | 55% | OFF (13.9 over) |

TODO's note ("seats 1-3 run easy: 77-90% for the best QA deck, Nyx's own seat 54%") is not what the frozen build gives now:
seat 1 and seat 3 are lower (66.7%, 76.7%) and Nyx's own seat higher (63.3%). Why was not investigated here.

### Orvel's Table (authored, 1 seat): target best 55-60%

| Seat | Wins | ± err | Target | OK/OFF |
|---|---|---|---|---|
| 1: Patron Orvel | 53.3% of 30 | 9.1% | 55-60% | OFF by the harness's range test; 1.7 points under the range, well inside one standard error |

## What could not run, and caveats

- **The Black Market's lead candidate** (section 2), Nyx's remeasure after it, `--alley-gen`, `--campaign-test`, `--arbitrage`:
  not run (a data edit is needed for the first two; the rest are outside this job's list).
- **`--bracket-matchups`**: not a flag in this harness; the Locals were measured by `--bracket-sim` (it uses the library-advice
  rates directly) and `--bracket-calibrate` was not run.
- Sample sizes are the owner's: `--games 3` gives 30 duels a No Limits build (± 9 points) and 3 a drawn seat; a build sitting
  inside or outside the 45-65% band by less than about 8 points is not settled by this run.
- The machine was heavily shared during the runs (load average 140-230 for the first hours), which affects only the times, not
  the results.
