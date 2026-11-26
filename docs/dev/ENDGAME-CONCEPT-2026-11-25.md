# The Borrowed Road: concept (Game Designer, 2026-11-25)

The lead design for [ENDGAME-SPEC-2026-11-25.md](ENDGAME-SPEC-2026-11-25.md), from the Game Designer agent (Opus), working
from [ENDGAME-FACTS-2026-11-25.md](ENDGAME-FACTS-2026-11-25.md) and [ENDGAME-RESEARCH-2026-11-25.md](ENDGAME-RESEARCH-2026-11-25.md).
Every number is a placeholder until measured; (guess) marks guesses.

## Concepts considered

| | Fantasy | Generated | Why not / why |
|---|---|---|---|
| A. Gus's Time Capsule (sealed draft) | "Crack a box from 2008 and win with what's inside" | Pack pools, CPUs' sealed decks | Fails measurability: random-pile CPU decks have no Elo and no playbook. Paid random packs next to the Alley's stakes add gambling flavour |
| **B. The Borrowed Road** (recommended) | "Win with anything. Every deck you beat can become yours for the next town" | Route, opponents, deck walls, boss deck, theme poster | All decks are rated, so difficulty is known before a duel; rentals make the collection irrelevant on purpose; no stakes, prizes or packs; new next to the Library (your deck, one era), Ladder (fixed rungs) and Alley (stakes) |
| C. Mika's Regional Season | "Tour generated weekend events with your own deck" | Event formats and fields | Duplicates the Library and Locals; a champion's collection solves it |

## The Borrowed Road

### Framing

Host: **Kit, the Shop Regular** (intro: "Lend me a deck and I'll pilot it"). After the Tier 0 Gauntlet, Kit thinks the
champion has become their deck and books a summer bus tour of other towns' shops, where house rules say you play off the
shop's deck wall, never your own binder. **Time-Travel Rules:** every deck plays under the list of its own day (ban lists
bind only the player's rental, to its own era).

### A run

- Six stops, about 60-90 minutes (guess): S1, S2 towns; S3 the town's **house champion** (authored); S4, S5 towns; S6
  **Kit**. Saved after every duel; reload never rerolls.
- **Start:** pick 1 of 3 rentals from different era buckets (2005-10, 2011-16, 2017-21, 2022-26) and style tags, one
  tagged easy to pilot; each shows its `history.json` "how" line and Elo stars.
- **Each town:** a fork of two seats (deck, character, era, stars). The low road sits in the bottom half of the stop's
  Elo window (10 Miles); the high road in the top half (20 Miles). The road not taken counts as "passed up".
- **Three strikes:** a loss costs one and replays the same seat with a fresh shuffle; the third ends the run.
- **After a win:**
  1. Swap: take the deck you beat, one of the town wall's 2 decks, or keep yours.
  2. Pick 1 of 3 boons, all player-facing:
     - Call the Toss (you choose first or second next duel);
     - Spare Tire (+1 strike, max 4);
     - Scouting Report (the next fork's full lists);
     - Detour (reroll one fork seat);
     - Second Look (swap for either of your last two beaten decks);
     - Rest Stop (the next loss costs no strike);
     - Postcard (+15 Miles);
     - Long Way Round (the next fork shows 3 seats; bus stage 3).

### The generator

- **Pool:** the rated decks in `campaign/deck-ratings.txt` minus outliers (guess: frog-ftk 1054, stall-burn and geargia
  for 16-19-turn duels), about 87. The opponent is each deck's same-id character. Alley, headliner and No Limits decks
  stay the Alley's own.
- **Seed:** `Hash.Mix(salt, "road", runNumber)`; the run number is saved and counts up.
- **Rank 0 Elo windows** (centre; low road −40..0, high road 0..+40):

  | Pick | Elo |
  |---|---|
  | Start offer | 1,410-1,490 |
  | S1 | 1,350 |
  | S2 | 1,420 |
  | S3 house champion | 1,500 ±50 |
  | S4 | 1,560 |
  | S5 | 1,620 |
  | S6 Kit | 1,660-1,740 |

  Walls offer 2 decks at the next stop's centre +100 (±40). A greedy swapper then sits about +100 Elo over the next seat,
  so p ≈ 0.64 per duel and p^6(1 + 6q + 21q²) ≈ 0.43-0.50 per run.
- **Anti-oatmeal rules:**
  - no deck twice in a run;
  - the six opponents span 3+ era buckets and 4+ style tags;
  - no two consecutive stops share both era and style;
  - one authored **poster** per run with its own hook (Throwback Weekend, Extra Deck Expo, Speed Week, The Long Game,
    Underdog Day...).
- **Winnability gate:** reject and retry deterministically (attempt+1) when any stop's predicted p is below 0.40 (0.33
  from rank 5), the predicted clear is under half the tier's floor, expected turns exceed 75, or a window is empty.
  After 32 tries, fall back to an authored safe route. The harness proves the fallback is never reached on 10,000 seeds.
- **Bosses:** S3 is one of 8 authored house champions in the 1,450-1,620 band. **Kit** at S6 plays the highest-Elo
  *passed-up* deck inside Kit's window (else a seeded pick), at skill 1.0 through a copy as in `Ladder.Opponent`.
- **Seeds:** run numbers, not a clock. Route codes (6-character custom seeds) cost no fare and pay nothing. One free
  reroll of the start offer per run; no forced bad events; forfeiting allowed, no refund.

### Road Rank (cumulative; clear rank n to open n+1)

No rank touches a CPU's skill or deck list.

| RR | Rule |
|---|---|
| 0 | As above |
| 1 | The wall offers 1 deck, not 2 |
| 2 | The start offer sits at S1's centre +60, not +100 |
| 3 | Wall decks at the next centre +60, not +100 |
| 4 | The house champion's window +60, and it calls the toss (its better side from deck-ratings' first/second split) |
| 5 | Start with 2 strikes |
| 6 | Boon offers show 2, not 3 |
| 7 | Every CPU seat calls the toss |
| 8 | Kit's window becomes 1,760-1,840 |
| 9 | No wall at S4-S6; swaps there are for the beaten deck only |
| 10 | Kit plays two decks in one sitting (the two strongest passed-up decks in 1,780-1,950); a loss restarts the sitting |

Target clear rates (bot pilot): 50-65% at RR0 down to 4-10% at RR10. Each rank drops 3-12 points: no tier that changes
nothing, no cliff.

### Progression and rewards

- **Persists:** Road Rank; the **Pilot's Log** (for each deck, the best rank at which you beat Kit holding it: an
  87 × 11 grid; the start offer weights unlogged decks ×2); Miles; bus stages.
- **Narrative:** Kit's postcards (a scene for each first rank clear; at RR10 Kit builds a first deck of their own); lines
  for house champions; posters at log milestones (10, 25, 50, 87).
- **Cosmetic (Miles only):** pins per deck, card backs, mat tints, poster frames (all drawn in code).
- **No power rewards:** no DP, cards or tokens from road duels.

### The sink

- **Fare** (`road.fare`): 800 + 400 × RR (800 to 4,800). Free at RR0 with half Miles when broke (guess).
- **Kit's bus restoration** (`road.bus`), paid once in stages that visibly change the bus:

  | Stage | Cost (DP) | What it adds |
  |---|---|---|
  | 1 | 10k | 4 more posters |
  | 2 | 15k | Route codes |
  | 3 | 20k | The Long Way Round boon |
  | 4 | 30k | 2 more house champions, the Pilot's Log wall |
  | 5 | 45k | The bus mat, Kit's last postcard |

- **Miles firewall:** earned only by road wins (10/20/30/50 × (1 + 0.1 RR)), spent only at Kit's souvenir stand; never
  bought with or turned into DP, cards or tokens; never refunded.
- **Over 50 runs (~60 hours, guess):** fares about −100k, bus −120k, road wins 0, Miles ~8,000 earned against a
  ~12,000-Mile catalogue. Target: 60-80% of a champion's ~280k spent over the first ~60 hours (this table: ~79%).

### What the harness proves

- **`--road-check`** (no duels, 10,000 seeds per rank):
  - every constraint and the winnability gate hold;
  - the fallback is never needed;
  - the same salt and seed give a byte-identical run;
  - mid-run save, load and resume work;
  - a save with no road lines loads;
  - the oatmeal metrics pass: mean pairwise Jaccard of opponent sets ≤0.15 over 50 runs; every eligible deck seen once
    per 50; no (poster, champion, Kit deck) repeat within 10 runs.
- **`--road-sim --rr N --runs 500`:** a skill-1.0 bot pilot, greedy swaps. It reports clear rate, measured against
  predicted p per stop (within ±8 points), turns and deck counts.
- **`--arbitrage` and LedgerCheck:** a road loop; no `road.*` Earn kind exists.
- **`--campaign-test`:** a road step, the story hooks, Kit's seat at skill 1.0.

## Open questions for the specialists

- **Level:**
  - Under which ban list was each deck rated? Time-Travel Rules predict well only if decks were rated as built.
  - Author style tags and an easy-to-pilot flag for all decks.
  - Choose the house champions, preferring decks the circuits use least (all 8 candidates appear in 1-2 circuit matches).
  - Write the posters' constraints.
  - Set Kit's fallback and the RR10 pairing.
  - Is "CPU calls the toss" strong enough for RR4 and RR7?
  - Can the bots handle time-travel matchups without stalls?
- **Economy:**
  - Do road duels pay 0 DP through every path?
  - Fares before and after the climb's repeat-pay cut.
  - The bus total against QA champion banks of 139k-412k.
  - The Miles catalogue and its pacing.
  - The firewall proof.
  - Is the free broke fare needed?
  - Refund on forfeit (recommendation: no).
  - Should the B4 sinks share Miles? (Recommendation: no, they stay DP.)
- **Narrative:**
  - Kit's arc in 11 postcards.
  - The unlock scene.
  - Kit's line naming the passed-up deck.
  - Lines for 10 house champions.
  - 12 posters' copy.
  - The bus stages and the souvenir stand.
  - Named towns or generic?
  - A Vera cameo at RR5?
  - How do the milestone posters fit `Posters.cs`?
