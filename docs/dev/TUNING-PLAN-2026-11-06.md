# Tuning plan, 2026-11-06

The owner un-parked tuning today ([NEXT-BUILD-2026-11-06c.md](NEXT-BUILD-2026-11-06c.md)). This is the plan only.
Nothing is measured or retuned until the misplay hunt's fixes (chunk M, `docs/dev/MISPLAY-HUNT-2026-11-06.md`) land. Today's
numbers are [TUNING-MEASURE-2026-11-06.md](TUNING-MEASURE-2026-11-06.md) (c80b436). Earlier passes are in
[ALLEY-DIFFICULTY-2026-11-03.md](ALLEY-DIFFICULTY-2026-11-03.md) and TODO.

**Rules. Breaking any of them is a bug.**
- Every CPU plays at its set skill, with the planner and its playbook. Difficulty moves only by deck choice: which deck,
  which list, which seat.
- No skill goes below what's set, and there are no handicaps.
- A DP change goes through `Profile.Spend`/`Earn`, and `--arbitrage` stays at 0 DP-positive after every data change.
- Old saves load. Rebuilt decks keep their file names: a saved build seat whose deck file goes missing isn't redrawn.
- No real players' names, in deck notes or `#about`.

**How everything is measured.**
- A frozen harness copy of the combined build, made after M lands (`build/bin/tuning/`).
- The QA stand-in is Jolt, and studies load `--load build/qa-night5/mods`, as in TUNING-MEASURE.
- **Tune on seed 1, pass the gate on seed 2**, so no number is fitted to one set of seeds.
- "±" is one standard error. A row is settled when its point estimate is more than one error inside the band.
  Otherwise that row is re-run at twice the duels.

## 1. What the misplay fixes change

Both seats run the same AI: Jolt is a CPU too. So a fix moves a win rate only where it helps one deck more than the
other. The last paired fix moved 4 duels in 1,240 (548 against 544), so most rows should move by less than their error.
TUNING-MEASURE also predates two landed changes: the forced-attack loop fix (the Yubel prompt-cap stops and the
peak-check hang) and 43057bb's battle-memory fixes. **So no figure carries over. Every row below is re-run on the new
build.** In order of dependency:

1. **Ratings.**
   - Read M's per-deck A/B first. If any deck moved by more than two errors, re-rate the frozen pool (`--rate-decks`,
     deck-ratings.txt) before anything else.
   - Then always re-rate the anchored decks (`--rate-anchored decks/no-limits --write`, peak-ratings.txt).
   - Every Elo-based row reads these files: the No Limits window, `--bracket-sim`, the Ladder's order and the Brawl.
2. **The Library's measured rates** (`--library-study`, library-advice.txt). They feed the Locals odds and the final's
   rounds.
3. **Decks M is most likely to move:**
   - battle-trigger payoffs, M's B.E.S. and Gladiator Beast work (lightsworn runs a B.E.S.; M's findings name the rest);
   - decks with battle-proof walls (goat-control, chaos-yata, stall-burn: Dumpster Row's bases);
   - Yubel and Fiendsmith lines, which the forced-attack fix touched.
4. **Rows near an edge, where a small move flips the verdict:** Junia's chair 63.3%, Detonator 66.7%, Albion 53.3%,
   HAT & Shaddoll 46.1%, the Tearlaments Cup 44.8%, the Midnight Open 29.6%, Orvel 53.3% and the Brawl 46.4% / 49.4%.

## 2. Targets, how each is measured, and today's number

Duel counts and errors are per row. Times are the overnight's, on a shared machine (load 140-230 on 10 cores). Time the
first run on an idle machine and rescale the rest.

| Area / target | Band | Command (seed 1 to tune, seed 2 for the gate) | Duels, ± | Today | Status |
|---|---|---|---|---|---|
| NL: each build vs the reference (tearlaments) | 45-65% | `--alley-study --only "No Limits" --games 5 --load …` (about 6.5 h) | 50 a build, ±7 | Spoils 63, Habakiri 77, Detonator 67, Noroi 60, Sangen 90, Albion 53, Lacrima 73 | 4 of 7 OFF |
| NL: pool mean, 20 drawn nights | 55 ± 5% | same run | 300, ±2.9 | 71.1% | OFF |
| NL: end chair (Junia) | band only (decision 2) | same run | 50, ±7 | 63.3% | ok, at the edge |
| NL: speed vs base | ≤ 1 turn faster; t2 wins ≤ max(10%, base + 5) | same run, and `--peak-check` | 30-60 | Spoils OFF (-1.5 turns, 13% by t2); Noroi OFF in `--peak-check` (16.7% by t2) | 2 OFF |
| NL: gain over base | +100 to +250 Elo (under +100 is only reported; a negative gain fails 5.1's `--playbook-ab` gate) | `--peak-check all --games 60` (about 5 h) | 60, ±45 Elo | Habakiri -120, Detonator -83, Albion -70, Lacrima -70 (20 duels); Spoils +83 | 4 fail |
| NL: use rate of each restored card and pick | ≥ 60% | same run | 60 games a card | 9 rows low; Azathot 2% | OFF |
| NL: rating window | 1840-1990 | `--rate-anchored decks/no-limits` (about 2 h) | 600, ±25 Elo | 1673-1865 | 6 of 7 under |
| NL: endless, per deck | prompt cap < 0.5%, turn 40 < 2%, draws < 1% | `--endless-test --games 15000`, seeds 1 and 3 (8 min each) | about 455 a deck, ±0.7 on 2% | Albion 2.2% at turn 40; Yubel decks 1.5% / 2.2% at the prompt cap (fixed since) | re-run |
| NL Invitational | 10-20% for the reference | `--bracket-sim` | 20,000 brackets | smoke 15.5-23.0% | needs wave 2 |
| Remy's end chair | 50 ± 5% | NL study, wave 2 | 50 | not built | needs wave 2 |
| Black Market: best deck vs regulars / closer / headliners | 60 / 60 / 55% | `--alley-study --only "The Black Market" --games 5` (1.8 h) | 250 ±3; 50 ±7 | 83.6 / 84.0 / 86.0 (Marlo), 96.0 (Gail) | OFF |
| Black Market: starter | 40% | same | 250, ±2 | 8.4% | OFF (see §4) |
| Nyx's Table, by seat | 55%, ramped (owner: seat 1 about 70-75; seat 3 waits for Lab Coat Lyle) | `--only "Nyx's Table" --games 5` (about 1 h) | 100 / 50 | 66.7 / 63.3 / 76.7 / 63.3 | seats 2 and 4 ok |
| Orvel's Table | 55-60% | `--only "Orvel's Table" --games 5` (7 min) | 50, ±7 | 53.3% ±9 | within noise |
| Dumpster Row; The Basement Game | 75/65/70; 65/50/60 | `--games 3`, recorded only (owner, 2026-11-05) | 168 / 30 | 98.8 / 35.7; 95.7 / 35.2 | off, accepted |
| Alley Brawl after The Black Market; Midnight Open | 30-45% (accepted at 46/49); 15-30% | `--bracket-sim`, then `--bracket-calibrate` (about 1 h) | ±0.35, plus ±25 Elo of rating | 46.4 / 49.4; 26.4 / 29.6 | re-check |
| Library Locals | 30-45% title | `--library-study --only <cup>`, then `--bracket-sim` | library-advice's sample | HAT & Shaddoll 46.1, ABC 54.7, Synchro Peak 70.3, TOSS 61.1, Rabbits & Rulers 73.8 | 5 OFF |
| Every Era final: goat, six-samurai, kozmo, eldlich rounds | re-measure (TODO) | `--library-study`, 72 duels | ±5.9 | 24 duels, ±20 | stale |
| Ladder, each rung, for chaos-ritual (1896; the QA deck's row is information) | 40-75%, none under 30 ("broken"), order non-increasing within one error `[PLACEHOLDER]` | §3, step 5 | 60 a rung, ±6.4 | unmeasured. By Elo: 76% at rung 1 to 54% at rung 8, 50% at the top | new |
| Ladder: Ilse's sitting; whole climb; visitors | sitting 25-50%; climb ≤ 30 duels expected (40 is broken); visitors 55-75% `[PLACEHOLDER]` | product of her four rows; the climb by the spec's simulation from measured odds | 60 a deck | by Elo: sitting about 73% (too easy for 25-50%), climb about 20 duels, visitors 78-84% | new |
| Vera's rematch | 45-60% for a champion's best deck `[PLACEHOLDER]` | same as the Ladder | 60, ±6.4 | circuit warriors (1418) at skill 0.85: about 94% by Elo | needs a deck |
| Card versions phase 3: each `#texts era` deck | its rating holds within error or rises | `--remeasure` per deck (CV spec 3.2), `--endless-test` with originals | 600, ±25 Elo | not built | after the pass |

## 3. Order and machine time

Every run uses all ten cores, so runs go one after another, never side by side.

| Step | Depends on | Runs | Machine time |
|---|---|---|---|
| 0. Smoke | M merged and reviewed | `--campaign-test`, `--nolimits-test`, `--forced-attack-test`, `--endless-test --games 3000` | 15 min |
| 1. Ratings | step 0 | `--rate-decks` (only if M's A/B moved a deck), then `--rate-anchored decks/no-limits` | 2-6 h |
| 2. No Limits baseline | 1 | endless seeds 1 and 3; `--peak-check all --games 60`; NL study `--games 3` | about 9.5 h |
| 3. Alley baseline | 1 | Black Market, Nyx and Orvel at games 5; Dumpster Row and Basement at games 3; `--bracket-sim`, `--bracket-calibrate` | about 5.5 h |
| 4. Library | 1 | `--library-study` for the five easy cups and the four final rounds; `--bracket-sim` | 6-8 h (overnight) |
| 5. Ladder and Vera | 1 | a scratch root with the holders' profiles at skill 1.0, and a circuits-format file of the rungs, Ilse's four decks and the visitors under TCG September 2026; `--circuit-study --circuits <file> --era-limited --option chaos-ritual` (no new code; a `--ladder-study` flag is optional harness work) | 2-3 h |
| 6. No Limits rebuilds | 2, and the playbook work | each candidate: `AlleyData.Problems`, `--rate-anchored` on that deck, `--peak-check <pilot> --games 60`, `--playbook-ab`, `--turn1`. Then a full NL study at games 3 per round, two rounds | about 1 h a candidate (12-18 h); 8 h of studies |
| 7. Black Market candidate | 3 | scratch root; the table at games 5; Nyx at games 5 (its seats 1 and 3 draw from it); `--alley-gen`, `--endless-test`, `--arbitrage`; headliner rebuild screens | about 4-5 h |
| 8. Locals, Vera, Ladder candidates | 4, 5 | `--locals-check`, `--rate-anchored` per new list, the cup's `--library-study`; Vera's list in the step 5 setup | about 8 h |
| 9. Gate, seed 2 | 6-8 | §5's runs, `--campaign-test`, `--arbitrage` | about 12 h |

**Total: about 70-85 machine hours at the overnight's pace. That's three to four days of nonstop runs, and probably much
less on an idle machine.**
- Steps 2-5 are about 25-30 h. They give the post-M picture, which is worth reporting to the owner before any rebuild.
- Deck-building and playbook work (step 6) is people time. It runs while steps 3-5 measure.

## 4. Deck-choice levers for each miss

**NEW LIST** marks a deck file that doesn't exist yet. The deck builder is an agent that builds against the rated pool,
with `--rate-anchored`, `--peak-check`, `--playbook-ab` and `AlleyData.Problems`. Playbook code goes to a Senior
Developer: additive only through `#recipe` (spec 7.1), with the base's `--turn1` and its 200-duel `--playbook-ab` inside
noise before and after.

### No Limits

**The size of the gap.** The pool needs about **+120 Elo on average** to move from 71% to 55%. A build stays at or above
its peak list, uses 2-4 picks with at most one power card, shares at most 2 picks with another build, and rates at most
+250 over its base.

| Build | Needs (to 65% / to 55%) | Lever | Cost |
|---|---|---|---|
| Sangen (nl-tenpai, cap 1925) | +274 / +350 Elo | **NEW LIST rebuild**: every pick slot used, Trunade (36% use) replaced. Tenpai playbook: Raigeki/Trunade timing going second. Its mirror against Spoils is 17% where the ratings expect 42% | The most likely miss even at the +250 cap (decision 2) |
| Habakiri (nl-mitsurugi, base ryzeal) | +99 / +172 | **NEW LIST rebuild**: cut King of the Feral Imps (16% use). Playbook: Mass Driver and Pharaonic Advent tributes (7.2). Consider `base: mitsurugi` (1742), its truer base | Playbook code. Changing the base moves its gain figure, not the gate |
| Lacrima (nl-yubel, cap 1903) | +67 / +140 | **NEW LIST rebuild**: Fiendsmith's Lacrima is at 23% use, so either fix its Tract/Sequence lines or cut it for a pick | Playbook code (YubelPlaybook's Lacrima lines) |
| Detonator (nl-ryzeal) | +12 / +85 | Replace Daigusto Emeral (7% use) and Lavalval Chain (19%) with Extra Deck cards that Ryzeal/Fiendsmith reach. Fiendsmith helper rules | List edit, small |
| Albion (nl-branded) | in band | Its gain is negative (-70) and it reaches turn 40 in 2.2-3.3%: trim picks back toward branded-despia until `--playbook-ab` is positive and turn 40 is under 2% | Could fall under the band; re-run the study |
| Noroi (nl-vanquish-soul) | in band | Speed: swap Azathot (2% use) for a pick that doesn't add turn-2 wins | List edit, small |
| Spoils (Junia, end chair) | in band | Speed: find the pick behind her 13% turn-2 wins (goldfish; Soul Charge or Union Carrier, the latter at 12% use) and swap it. Her row must stay 45-65% | Her gain (+83) may drop, which is only reported |

Not levers: the reference and the night draw. Both are owner decisions (§6). Seats and families are fixed by spec 4.4.

### Alley

**The Black Market.**
- **The lead candidate:** `bases: thunder-dragon, danger-dark-world`, `closer: thunder-dragon`,
  `size: thunder-dragon 2`. It's expected at 66-68%, still over 60%, because the era-true pool is used up.
  - Cost: two bases instead of three.
  - Thunder Dragon's edge may be specific to the QA deck: Colossus stops a Snake-Eye deck that searches. Check it with a
    second strong deck.
  - Nyx's seats 1 and 3 draw from this table, so remeasure Nyx after it.
- **Headliners (NEW LIST rebuilds, within their era, under HEADLINERS-2026-11-01):** Marlo's spyral (1466) needs about
  1620, about +155; Gail's gouki (1259) needs about +360.
  - Gail's +360 is likely out of reach on gouki. If her rebuild can't get there, she stays the easy headliner (the
    Headliners author's call).
  - Gail's 3.6% turn-40 line also needs her list fixed.
- **The starter's 40%: no lever.** Classic wins 2-16% against every eligible base.

**Nyx, Dumpster Row and The Basement Game.** No lever is left that isn't a weaker CPU, and the owner has accepted these
as they are. Re-measure them for the record only.

**The Brawl.** Accepted. Only if the re-rating moves the four Brawl decks more than 25 Elo, re-run `--bracket-sim` and
report.

### Library Locals

The lever is each cup's three `local:` lines: **NEW LIST**s of era-true decks stronger than today's locals.
- Each must pass `--locals-check`: rated, 90% of its cards out by the cup's pool, an era at or before the cup's, listed in
  its pilot's `decks:`, and no character twice in the field.
- Cost per cup: a deck builder's list, a rating, and a profile `decks:` line.
- Run `--bracket-calibrate` first. TODO says Elo runs about 85 points off at these cups; the `--bracket-matchups` flag
  TODO names doesn't exist.
- The fallback is a smaller prize (DP through the ledger). That isn't a difficulty change.

### Ladder

- **Reorder the rungs** by their measured odds (the line order in ladder.txt).
  - Cost: `Climbed` counts the rungs taken from the bottom in a row, so a save that already climbed some can show fewer.
  - No prize pays twice, since wins are kept per rung key.
  - The `ladderrung:<n>` beats are numbered, so check their text.
- **Ilse's sitting, at about 73% by Elo, is over the 25-50% target.** Her decks are fixed (her guest decks), so the only
  lever is format, which is deferred, or accepting it.
- **Rungs 7 and 10 are both Chaos rituals.** Kept unless a rung is out of order.
- **Visitors** keep their 2024 bases. They stay dormant until the Invitational.

### Vera's rematch

- **A NEW LIST:** a trap-heavy warriors build that waits on its traps, at a champion's strength (decision 6).
- Her circuit profile can't change: it is the circuit's beatdown.
- So the guest line needs a small engine change (Senior Developer): a per-guest style override (patience and caution up,
  aggression down) and skill 1.0, as `Ladder.Opponent` does.
  - A second profile id would break `guestbeat:vera`.

## 5. No Limits ship gate (before the table and builds go `ready: yes`)

All runs are on the post-M build, gate seed 2.
1. **5.1 data rules:** `AlleyData.Problems` is empty, and `--nolimits-test` and `--campaign-test` pass.
2. **Ratings:** every ready build is inside the table's `elo:` window (1840-1990, re-set if the reference's rating
   moves), from `--rate-anchored`. Gain over base is at most +250; under +100 is reported, not a fail.
3. **Playbooks:** `--peak-check all --games 60`, with every restored card and pick at 60% use or more (Extra Deck cards
   over the games where they're reachable, or cut). `--playbook-ab <build> <base>` is positive, and the base's
   `--turn1` and A/B are unchanged.
4. **Speed:** every build's average winning turn is at least 3 and at most 1 faster than its base. Wins by turn 2 are at
   most max(10%, the base's + 5). The reference loses under 10% by turn 2; otherwise `NoLimitsGuardTurns` goes to 2.
5. **The study** (`--games 5`):
   - every ready build is 45-65%;
   - the pool mean is 55 ± 5;
   - Junia is inside the band;
   - a row within one error of an edge is re-run at `--games 10`.
6. **Endless:** `--endless-test --games 15000` on seeds 1 and 3, per deck: no prompt-cap stop on turns 1-3, prompt cap
   under 0.5%, turn 40 under 2%, draws under 1%.
7. **Junia and at least `NoLimitsShipPool` (6) pool builds pass 1-6.** A build that misses stays `ready: no` and is
   retuned by its picks, never cut below its peak list.
8. **Then:**
   - `--arbitrage` shows 0 DP-positive;
   - `-alleycheck` steps 19-24 pass;
   - the table and the passing builds go `ready: yes`, with each `#why` line carrying its measured date.
   - The Invitational stays "Coming soon" until wave 2 passes its own gate: Remy 50 ± 5, Junia wildcarding inside the
     band, and the title at 10-20%.

## 6. Decisions for the owner (recommendation first)

1. **No Limits' reference.**
   - Recommended: keep tearlaments (1947) and raise the builds, about +120 Elo across the pool and five rebuilds. No
     Limits stays the table that needs a 2026-strength deck.
   - *Or* re-anchor on the QA-type deck. The pool already reads 58% for Fire King Snake-Eye, so it would ship nearly as
     built, but a 2026 deck would then win about 71%.
2. **Ship with five pool builds.**
   - Recommended: if one build (likely Sangen) still misses after two rebuild rounds, `NoLimitsShipPool` goes 6 → 5 and
     that build stays `ready: no`. `NoLimitsMinPool` 4 keeps the nights working.
   - *Or* hold the table until all six pass.
3. **When No Limits ships.**
   - Recommended: as soon as its gate passes, ahead of wave 2. The Invitational and the Ladder's visitors stay dormant.
   - *Or* hold the table until wave 2 and the Invitational are ready (Toon's data lands 2026-11-13).
4. **The endless gate is per deck, not the aggregate the harness prints.**
   - Recommended: per deck, as spec 4.7 words it ("every build and Brawl deck"). Albion's 2.2% then needs its list
     fixed.
   - *Or* the aggregate, with per-deck lines as information.
5. **The regulars' event skills (0.7 / 0.85 / 0.95).**
   - Recommended: keep them, as you decided on 2026-11-05. They are what's set, a difficulty step.
   - *Or* first spend about 2.5 h measuring them at 1.0 in a scratch root and decide on numbers. Raising them makes the
     best deck's rows harder and the starter's rows worse.
6. **Vera's rematch.**
   - Recommended: a new trap-heavy warriors list at a champion's strength (45-60% `[PLACEHOLDER]`), with a guest-line
     style override and skill 1.0. That's a fight worth 10,000 DP.
   - *Or* an era-true trap warriors list on her profile: the right flavor, but about 90% for a champion.
7. **The Locals.**
   - Recommended: commission era-true `local:` lists for the four cups more than 10 points over (Synchro Peak, Rabbits &
     Rulers, TOSS & Eldlich, ABC to SPYRAL), and accept HAT & Shaddoll at 46%.
   - *Or* later-era visitors, which breaks `--locals-check`'s era rule.
   - *Or* smaller prizes there.
8. **Card versions phase 3.**
   - Recommended: land 3.1, base-code playbook matching, with M before step 1. It changes CPU strength, so it belongs in
     the measured build. Alley splash originals and `#texts era` come after this pass, each with its own re-measure.
   - *Or* all of phase 3 first, which is one more session before anything is measured.

## The owner's answers (2026-11-06)

1-4. No Limits: keep Tearlaments as the reference and raise the builds; ship with five pool builds if one still misses
   after two rebuild rounds; ship as soon as its gate passes, ahead of wave 2 and the Invitational; the endless gate per
   deck.
5. The regulars' event skills: measure at 1.0 first; if full-strength regulars land in band, set them to 1.0 and tune by
   deck choice.
6. Vera: a new trap-warriors list at a champion's strength (45-60%), skill 1.0 on her guest line.
7. New `local:` lists for the four Locals more than 10 points over; HAT & Shaddoll accepted.
8. Card versions phase 3.1 (base-code playbook matching) lands with the misplay fixes; the rest of phase 3 after the pass.
Measuring starts after the misplay hunt's fixes land.
