# The Borrowed Road: generator tables (Level Designer, 2026-11-25)

The generator's content for [ENDGAME-SPEC-2026-11-25.md](ENDGAME-SPEC-2026-11-25.md), from the Level Designer agent
(Opus). Every p is **predicted** from Elo plus a toss model built from the first/second splits; no duel was played.

> **Superseded in part by a fix round** after the spec review (same day). The spec's §2-§4 hold the current numbers:
> - the toss rule now uses both decks' splits;
> - RR6 adds the Kit-window wall deck and fork centres +20; RR9 adds fork centres +15; RR10's Kit window is 1,740-1,950;
> - there's a 13th poster, Bargain Bin;
> - Graveyard Shift and Trap Night were loosened;
> - all 86 rentals are reachable;
> - human-pilot columns were added;
> - draws replay the seat.
>
> The tables below are the first pass; `tools/road-prototype/out-final-10k*.txt`, `out-reach.txt` and `out-kitwall.txt`
> are the current outputs.

The prototype is in [tools/road-prototype/](../../tools/road-prototype/): `road.py` holds the generator, the gate and the
bot sim; `final.py` runs the rank table; `posters.py` the posters. Their outputs are `out-final-10k.txt`,
`out-posters-2k.txt` and `pool-table.txt`. Run from the repo root.

## 1. The pool

- **Seat pool: 72 decks.** These are the `decks/history` decks with a same-id character, minus **frog-ftk** (1054,
  285 Elo below the next deck, a first-turn burn loop) and **geargia** (16.9 turns, a trap stall).
- **Wall-only: 15 decks.** The root and structure decks (no character), which suits the "shop wall" fiction; Kit can
  pilot them.
- **Out:** stall-burn (18.6 turns) and gadgets (14.7 turns); rentals must average 14.0 turns or fewer.

That leaves 86 rental decks; the Pilot's Log grid uses the live pool size. Style and mechanic tags and the easy flag
(E) are the designer's, open to change. W = wall-only. The full table:
[pool-table.txt](../../tools/road-prototype/pool-table.txt) (slug, Elo, era/bucket, average turns, first/second split
with * where significant, style/mechanic, W, E).

**Rated under which list? None.** `DeckRating.Run` (src/Ygo.Harness/DeckRating.cs) sets only a seed: Master Rule 5,
8,000 LP, no lflist; the core doesn't enforce lists. All 74 history decks were checked against their own `#banlist`
line and none breaks it, so Elo was measured "as built under its own list" and Time-Travel Rules match.

Caveats:
- Ratings used the **balanced** bot, not `--skilled`. An older skilled run (Oct 26) moved some decks a lot: qliphort
  1225→1485, runick-spright 1206→1425, yummy 1198→1381, orcust 1472→1637.
- `deck-ratings.txt` is dated Oct 29; the decks carry the Nov 8 release.
- **Re-rate with `--rate-decks --skilled` before shipping, then rerun `road.py`.**

## 2. Rank 0 windows (tuned; the concept's failed)

The concept's S1 low road (1,310-1,349) held no seat deck, so all 1,000 test seeds needed the fallback.

| Pick | Elo |
|---|---|
| Start offer | 1,420-1,500 (S1 centre 1,400 +60 ±40) |
| S1 | centre 1,390 |
| S2 | centre 1,460 |
| S3 house champion | 1,520 ±55 |
| S4 | centre 1,580 |
| S5 | centre 1,630 |
| S6 Kit | 1,660-1,740 |

- **Forks:** the low road is the centre −60..−1, the high road the centre +0..+60.
- **Walls:** 2 decks at the rank-0 centre of the next stop +40 ±40 (targets 1,460 / 1,520 / 1,580 / 1,630 / 1,700).
  Walls don't follow rank shifts.
- **Seats are fair by construction:** each is filtered by p against a reference deck (the best start offer, then the
  best wall decks so far), so the gate stays as a backstop.
- **Build order:** champion first, then featured stops, then the rest from S5 back to S1, with deterministic internal
  retries.
- **Results over 10,000 runs per rank, posters rotating:**
  - no empty window at any rank (the smallest is the wall after S5 at RR0, with 3 decks);
  - seat-fill retries discard 0.2-0.5% of attempts (8 at most);
  - **0 gate rejects, fallback never reached**;
  - predicted clear at RR0 without boons 34.9% (p10-p90: 31-39%); **bot clear 61.8%** (best offer, low road, greedy
    swaps, boons);
  - mean pairwise Jaccard of opponent sets 0.132 (0.086 over the six decks faced); deck coverage 98% mean, 94% minimum
    of the 51 decks rank 0 can reach, per 50 runs;
  - 0 (poster, champion, Kit) repeats within 10 runs.
- **Span rule:** 3+ eras and 3+ styles on every path through the forks, Kit excluded; 4 styles failed 19% of attempts.
- **Turn rule:** a seat's matchup mean must be ≤13.5 turns (a run expects ~100 turns, so "≤75 per run" was unreachable).
- **Era-gap cap:** a seat within 15 years of the reference deck. It costs almost nothing: clear 62.5→62.4. Without it,
  the median gap is 5 years, p95 14, maximum 18, and 3.1% of duels are 15+ years apart; no 2005-vs-2026 duel occurs.

## 3. Road Rank (cumulative; bot clear from `final.py`)

| RR | Rule | Bot clear | Drop |
|---|---|---|---|
| 0 | Base | 61.8% | — |
| 1 | The wall offers 1 deck | 58.3% | 3.5 |
| 2 | Champion window +40; the champion calls the toss | 54.6% | 3.6 |
| 3 | Boon offers show 2 | 50.0% | 4.6 |
| 4 | Wall decks −20 | 43.1% | 6.9 |
| 5 | Every CPU calls the toss; gate p 0.33 | 33.4% | 9.7 |
| 6 | Kit 1,760-1,840 | 27.6% | 5.7 |
| 7 | 2 strikes (Spare Tire repairs back to 3) | 18.2% | 9.4 |
| 8 | Boon offers show 1 | 13.1% | 5.2 |
| 9 | No Spare Tire | 9.5% | 3.6 |
| 10 | Kit plays two decks (1,740-1,840) in one sitting; a loss replays the deck that beat you | 4.4% | 5.1 |

**Dropped from the concept:**
- start offer −40: only −2.0;
- no walls at S4-S6: −22, and it fails the gate against a 1,760+ Kit;
- "a loss restarts the sitting" at RR10: 3.8%, under the floor.

**Boons are once per run, and Spare Tire repairs a strike** (up to the starting strikes). At the concept's offsets,
boons pushed the bot to 82% against 53% without them.

**"CPU calls the toss" works:**
- 78 of 87 decks do better going second, by 6.8 points engine-wide; only 23 splits are significant.
- A CPU call costs the player 4.5 points per duel on average (worst 26.6): 4.1 points of clear for the champion alone,
  11 for every seat.
- **The CPU must pick using both splits** (the side worse for the player): calling by its own split alone helps the
  player in some pairs, by up to 10.6 points.

## 4. House champions and Kit

**Champions:**

| Champion | Elo | Era | Style | Used today |
|---|---|---|---|---|
| spyral | 1466 | 2017 | combo/link | Library ABC to SPYRAL Cup |
| tri-brigade | 1480 | 2021 | graveyard/link | Hybrid Engines Invitational |
| nekroz | 1481 | 2015 | combo/ritual | Library Nekroz & PePe Cup |
| power-patron | 1546 | 2026 | combo/pendulum | nowhere |
| dracotail | 1549 | 2026 | midrange/fusion | Fall Format |
| elfnote | 1557 | 2026 | combo/synchro | The New Wave |
| drytron | 1566 | 2021 | combo/ritual | Rituals and Fire |
| mermail-atlantean | 1578 | 2026 | midrange/xyz | Fall Format (RR2+ only) |

- **Later (bus stage 4):** lunalight (1563) and artmage (1613), both Fall Format.
- **Coverage:** RR0-1 has 7 champions in its window, RR2+ has 5. No 2005-10 deck fits the band (teledad is used 5
  times; magistus-fairy-tail, used nowhere, sits at 1628).
- **Rule:** the wall before S3 must leave at least 2 champions with p ≥ pmin.

**Kit's deck, chosen at run time:**
1. The strongest *passed-up* deck in Kit's window (seats not taken plus wall decks declined) that leaves the player
   p ≥ pmin.
2. Else a seeded pick from window decks not met this run that pass.
3. Else the window's weakest deck.

**Kit's windows** (uses today in brackets; ×2 seeded weight for 0-1 uses):
- **RR0-5 (1,660-1,740):** sacred-beast (1), k9-vanquish-soul (4), radiant-typhoon (0), tenpai-dragon (4),
  bystial-dragon-link (3), mekk-knight-invoked (0, W), spright (3), thunder-dragon (6).
- **RR6-9 (1,760-1,840):** eldlich (5), light-and-darkness (4), angelechy (2), kashtira (3), branded-despia (3),
  dark-magician-chaos-ritual (2).
- **RR10 pairing:** the two strongest passing decks in 1,740-1,840 (the RR6 set plus mitsurugi); per-duel p ≥ 0.30.

**A gap:** Kit plays a passed-up deck in 96% of RR0 runs but **0% from RR6**, because no wall reaches 1,760. Idea, not
measured: from RR6, the wall after S5 shows one deck from Kit's window, and if you don't take it, Kit plays it.

## 5. Run posters (12; each run has one, in a fixed seeded lap)

A "featured" stop shows at least one seat that meets the test, on whichever road can carry it. `posters.py` ran 2,000
runs per rank per poster: all pass RR0-RR10 with 0 fallbacks, 0 gate rejects, discarded attempts 0-2.3%, and bot clear
within 3 points of the no-poster run (Underdog Day 1-4 points lower).

| Poster | Exact constraint | RR0 / RR10 clear |
|---|---|---|
| Throwback Weekend | S1, S2 and S4 each feature a deck from 2016 or earlier | 61.6 / 5.3 |
| Fresh Off the Truck | S4 and S5 each feature a 2024-26 deck | 61.2 / 5.1 |
| Extra Deck Expo | S1 features Synchro, S2 Xyz, S4 Fusion | 61.0 / 4.7 |
| Speed Week | S2, S4 and S5 each feature a deck averaging ≤10 turns | 60.6 / 5.3 |
| The Long Game | S1 and S2 each feature a deck averaging ≥12 turns | 61.5 / 5.1 |
| Fusion Festival | S4 and S5 each feature a Fusion deck | 61.0 / 5.3 |
| Graveyard Shift | S1 and S2 each feature a graveyard deck | 61.5 / 5.0 |
| Trap Night | S1, S2 and S4 each feature a control or stun deck | 61.7 / 5.0 |
| Beatdown Boulevard | S2 and S5 each feature a beatdown deck | 61.4 / 5.1 |
| Rookie Road | 2 of the 3 start offers are easy to pilot (2+ styles) | 61.9 / 5.0 |
| Underdog Day | Start offer 60 Elo lower | 58.9 / 4.9 |
| Back Roads | Champion used ≤1 time today; S1, S2 and S4 each feature a deck used ≤2 times | 61.1 / 4.9 |

The fixed lap needs **10 or more posters at the start** (with 8, the poster/champion/Kit triple can repeat within 10
runs): 10 at the start, with Underdog Day and Back Roads held back for bus stage 1.

## 6. Risks to watch in `--road-sim`

- **Slowest matchups at the bot's seats** (mean ≥12.5 turns, 4.6% of bot duels at RR0): wind-up vs chaos-yata, synchron
  vs goat-control, hat vs floowandereeze, adamancipator vs goat-control, mekk-knight vs goat-control, dragon-ruler vs
  goat-control.
- **Goat Control mirror:** impossible (no deck twice in a run; stall-burn is out).
- **Duels past turn 40:** watch chaos-yata (a Yata draw lock) and radiant-typhoon (13.8 turns). Ratings count
  unfinished duels as draws, which hides stalls.
- **Elo gap** to the opponent is capped by construction: at most 140, p95 127.
