# The Borrowed Road: economy (Economy Designer, 2026-11-25)

The DP and Miles design for [ENDGAME-SPEC-2026-11-25.md](ENDGAME-SPEC-2026-11-25.md), from the Economy Designer agent
(Opus). Scratch models in `build/scratch-econ/` (`faucet.py`, `road2.py`, `final.py`; not committed); harness copy
`build/bin/snap-econ`. **(guess)** marks assumptions. Key code claims were spot-checked by the session (Leveling.cs:58-72,
LedgerCheck.cs:254, Library.cs:204-207, Profile.cs:1584).

## 1. Road duels pay nothing: every path, and the hook

The road gets its own `Road.RecordDuel`. It must **not** reuse `Circuits.Record`, `Ladder.Record` or `LiveDuels.Settle`,
which bundle the paths below.

| Path | Where | Road rule |
|---|---|---|
| Win/loss/draw DP (loss 50, draw 100) | `Circuits.PayDuel` Profile.cs:1925; :1581 | Skip. A loss is a strike, 0 DP, no `road.loss` kind |
| First-win bonus 200 | Profile.cs:1581 (applied 1892, 1978) | Skip |
| Clear prizes and tokens | Profile.cs:1994-2002; Ladder.cs:427-435 | Skip. A clear pays rank, a Log entry and Miles |
| Level-up DP and tokens | `Leveling.AddExp` (Leveling.cs:58-72): at least 1 token per level even with `payDp: false` | **Skip `AddExp`: no EXP on the road.** At the Library's rate a champion would gain ~1 level a run, ~50 tokens over 50 runs |
| Gifts, drops, bond trades and gifts, bounties, owed gifts, set checks | `Profile.AddWin` :770 → `CardUnlocks.AfterDuel` | Never call `AddWin` or `AfterDuel`; road wins go to the Pilot's Log only |
| `Wins[id]` side effects (tag partners, NG+ recipe unlocks, tag wins) | Profile.cs:1870, :1764; Guests.cs:148 | Covered by no `AddWin` |
| `DuelsPlayed++` (drives Moth's rail expiry, grading turnaround, used market) | `NoteResult` Profile.cs:692 | Don't call `NoteResult`; the road keeps its own duel counter |
| A live duel settled as a paid loss | LiveDuels.cs:76-129 | Road live state in `RoadState` (a `roadlive` line), never `Profile.LiveDuel`; settling costs a strike, 0 DP |
| Rental cards in the collection | `Collection.Add`/`NewCopy`/`Take` | Rentals come from `.ydk` files and never touch the collection: not sellable, not stakeable |
| Posters, story | Posters.cs; story grants | Milestone posters fine (cosmetic); no `{card}` grants in road scenes |
| Make it yours | Profile.cs:645 | Not offered on the road |

## 2. A champion's bank today

`--campaign-sim` isn't usable here: it spends everything by design (105.8% in the Beginner band) and cleared 6 circuits
in 19 minutes. Code math over `circuits.txt` (no rematch farming):

| Source to the Gauntlet's clear | DP |
|---|---|
| First wins (83 circuit + 21 tag matches) | 150,179 |
| Circuit and Tag Cup prizes | 127,200 |
| Level-up DP (~level 36) | ~20,000 |
| Losses (~1 per win) | ~4,000 |
| Guests 1-4 (guess) | ~27,000 |
| **Gross** | **~325k** |

What's left depends on spending: ~300k for a QA-like player (7-9% spent), **130-195k** like the owner's save (82% spent
by circuit 10, then late prizes pile up; guess), ~0 like the sim. **Planning bank: 150k median (guess), range 60-300k.**
The old 139k-412k QA banks predate the rematch cut (a rematch now pays 150).

**Post-game income** (guess: ~6 duels an hour):
- **One-time pool, ~130k:** Library first wins and the Every Era final (~83k), Ladder (13k), Ilse and Vera (20k),
  level DP (~10k).
- **Repeatable:** rematch farming ~120 DP a duel (~700/h); New Game+ ~222k a lap (~10k/h), the only large repeatable
  faucet.
- **Already sinks:** Locals (−349 to −416 an entry) and the alley.

## 3. Fares and the bus

Runs simulated with clear rates from 57% at RR0 to 7% at RR10, 2 strikes from RR5, a player pushing to the top open rank:

| Fare | 40 runs | 50 runs | 80 runs |
|---|---|---|---|
| 800 + 400 × RR (concept) | 134k | 181k | 324k |
| **600 + 200 × RR (recommended)** | 75k | **100k** (58k for a player staying at RR ≤ 3) | 178k |

A pusher reaches RR9-10 by about run 40. With the concept's fares plus the 120k bus, that's ~300k over 60 hours: over 100%
of a 150k bank, so a pusher runs dry around run 35-40.

**Recommended:**
- **Fares:** 600 + 200 × RR (600 to 2,600).
- **The bus:** 10k / 15k / 20k / 30k / 45k = 120k. Stage 1 is affordable in the first session.
- **Total:** 178-220k over ~50 runs (~60 h at ~70 minutes a run, guess). That's 59-73% of a 300k bank, or 61-76% of a
  150k bank plus the 130k one-time pool.
- A low-bank player pays fares from the bank and funds the bus from the Library and New Game+. That's intended: the bus
  is the optional big goal.

**No "can't afford to play" wall:** any opened rank stays selectable, so the floor is the RR0 fare.

**"Kit's spare seat"** (the free broke fare):
- **Conditions:** RR0 only; `Dp < 600` when the run starts; no run active; half Miles.
- **Counts for:** rank and the Log.
- **Not abusable:** DP can't be hidden (only `HoldStake` holds DP off the books, and no run starts while an alley duel
  is live); spending down to ride free costs real DP; it creates 0 DP.

**Rules:**
- No refund on forfeit.
- Pay when the start offer is picked; the run number counts up only on payment, so declining shows the same offer (no
  reroll by refusing).
- Route-code runs: no fare, no Miles, no Log, no rank.

## 4. Miles

**Earn per win:** base × (1 + 0.1 × RR), rounded to 5; the Postcard boon gives +25 × the same multiplier. The concept's
10/20/30/50 would give only ~4,700 Miles by run 50, so a 12k catalogue would last ~150 runs.

| RR | Low road | High road | House champion | Kit |
|---|---|---|---|---|
| 0 | 20 | 35 | 50 | 100 |
| 3 | 25 | 45 | 65 | 130 |
| 5 | 30 | 50 | 75 | 150 |
| 7 | 35 | 60 | 85 | 170 |
| 10 | 40 | 70 | 100 | 200 |

**Yield:** a pusher has 7,190 Miles at run 40 and 9,939 at run 60, reaching ~12,000 at run 76. A player at RR ≤ 3 has
8,260 at run 40 and 12,400 at run 60. The catalogue lasts ~60-76 runs.

**Catalogue: 12,180 Miles, all drawn in code.**

| Items | Prices | Opens when | Total |
|---|---|---|---|
| Deck pins, one per road deck (87) | 40 each | You've beaten that deck on the road; a free gold rim once it's in your Pilot's Log | 3,480 |
| Card backs: Road Map, Bus Ticket, Postmark, Mileage Grid, Night Route, Souvenir Stamp, Kit's Doodles, The Long Way | 250 / 350 / 450 / 550 / 650 / 800 / 950 / 1,200 | First five at 1, 2, 4, 6, 9 Kit wins; last three at RR4, RR7, RR10 clears | 5,200 |
| Mat tints (overlay on any owned mat) | 12 × 125 | One per 4 finished runs (runs 4-48) | 1,500 |
| Poster frames: Postcard, Brass, Timber, Chrome, Neon, Gold Leaf | 200 / 250 / 300 / 350 / 400 / 500 | After 2, 6, 12, 20, 30, 40 runs | 2,000 |

At run 40, a pusher has ~10.1k unlocked against 7.2k earned; a cautious player ~8.7k against 8.3k (only the rank-gated
backs remain, a nudge to push). No Miles cap.

**The firewall:**
1. Miles live only in `RoadState` (Miles, MilesEarned, MilesSpent): not on `Profile.Dp`, not in the Ledger.
2. One source: `RoadState.EarnMiles`, called only from `Road.RecordDuel` and the Postcard boon.
3. One sink: `RoadState.SpendMiles`, called only from `Souvenirs.Buy`.
4. No method both changes Miles and calls `Spend`/`Earn`, or touches `Tokens`, `Exp` or `Collection`.
5. Souvenirs aren't `CardCopy`s: no sell, refund or stake path; unequipping returns nothing.
6. Bus stages are one-time and capped; they never grant Miles or gate catalogue items. Stage 3's boon helps the win rate
   slightly: an indirect, bounded tie, not an exchange rate.

## 5. Related sinks

**The review's B4 should be a separate DP feature after the road, not Miles** (a different audience, and a simpler
firewall).
- **Champion's shelf:**
  - 4 free slots, then 6 more at 2,500-15,000 (52.5k total, `Ledger.Shelf`); nameplates 1,000 each.
  - A shelved slab stays in the collection; sold or lost to a stake, its slot empties with no refund.
  - Orvel's stake picker should skip shelved copies, or warn first.
- **Collection log:** rewards are earned cosmetics only, **never DP or tokens** (that would make a
  buy-packs → complete-log → DP loop).

At ~50-70k, B4 fits the remaining 20-40% of the bank.

**The alley** interacts cleanly if:
- a road live duel blocks `Ante.Hold`, and a live alley duel blocks a road duel;
- road duels don't tick `DuelsPlayed`;
- rentals are never stakeable;
- the fare is a plain `Spend`, not held.

## 6. Proofs

**Ledger kinds:** sinks `RoadFare = "road.fare"`, `RoadBus = "road.bus"`, added to `wantSinks` (LedgerCheck.cs:254; the
reflection listing forces it).

**`Arbitrage.Road.cs` (`RoadLoops`).** For each RR 0-10, each outcome, each boon and each mid-run event, check every step:
- DP changed by exactly −fare (0 for a broke seat or route code);
- `DpEarned` unchanged;
- Tokens, Exp, Collection, Wins, Losses, MatchWins, LibraryWins, Unlocks and DuelsPlayed unchanged.

Loops:
- fare → run → any result → liquidate;
- the broke seat at 599 and at 600;
- route codes;
- forfeit at each stop;
- a road live duel settled at load;
- New Game+ mid-run;
- save/load after paying and after each duel (no double charge, no reroll);
- buy every souvenir (DP unchanged);
- every bus stage (no refund path);
- an exhaustive search over 1-3-action sequences of {start(rr), win, lose, forfeit, buy souvenir, bus stage, NG+,
  save/load, settle live, alley hold/settle}: DP never rises on a sequence with road actions, and
  `Ledger.Net == Dp − 1000` (plus held stakes) after every step.

**Farming bound:**
- road DP per duel is exactly 0 over 500 random runs at every RR;
- Miles per run ≤ `Road.MaxMilesPerRun(rr)`: 340 at RR0, 830 at RR10.

**LedgerCheck:**
- in the scripted mix: an RR0 fare, an RR3 fare, a broke ride and bus stage 1, with `Balanced()` after each;
- assert no `road.*` key in `DpEarned`;
- `AcrossSaves` gains `road.fare` and `road.bus`.
- `SourceGuard` gains:
  - a `MilesWrite` regex allowing Miles writes only in `RoadState.EarnMiles`, `SpendMiles` and `ReadLine`;
  - checks that `EarnMiles` is called only from `Road.RecordDuel` and the boon, and `SpendMiles` only from
    `Souvenirs.Buy`;
  - a forbidden-call scan of `Road*.cs` (`.Earn(`, `PayDuel`, `AddExp`, `AddWin`, `AfterDuel`, `NoteResult`,
    `Tokens +=`, collection writes);
  - a rule that no method mixes Miles and `Spend`/`Earn`;
  - a probe file showing the scan catches each form.

## 7. Old saves and New Game+

- **Old saves:** a save with no road lines loads as defaults (RR0, 0 Miles, no bus stage, run 0); load ignores unknown
  keys.
- **Unlock on `profile.LibraryOpen`** (Library.cs:202-208), which survives New Game+. `IsCleared(Gauntlet)` wouldn't:
  New Game+ clears MatchWins, so the road would re-lock each lap. Old champion saves get the road at once.
- **Downgrades:** an older build drops the road lines but keeps the generic `spent road.*` lines, so the ledger still
  balances.
- **New Game+ resets nothing on the road:** rank, Log, Miles, souvenirs, bus, run counter and a run in progress all
  carry over. New Game+ re-pays ~222k a lap, which is what funds fares for a broke player.

## Not verified

- Clear rates are bot targets; humans may clear less, which means slower ranks, lower fares and fewer Miles.
- Run length (~70 minutes) and pace (~6 duels an hour) are guesses.
- The 150k median bank comes from one real save.
