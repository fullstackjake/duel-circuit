# The Borrowed Road: spec (2026-11-25)

A procedurally generated end-game mode and a DP sink. **Status: designed, not built; owner decisions recorded 2026-11-26 (§7).** Nothing in the game has changed;
every number below is a placeholder until measured in the app. The owner decisions at the end are not yet confirmed.

How it was made (CLAUDE.md pipeline, step 2: design first). Every claim about code was spot-checked by the session:
- **Research** (Sonnet, web): [ENDGAME-RESEARCH-2026-11-25.md](ENDGAME-RESEARCH-2026-11-25.md), how other games do it.
- **Fact sheet** (Sonnet, Explore): [ENDGAME-FACTS-2026-11-25.md](ENDGAME-FACTS-2026-11-25.md), the end game and
  economy as built.
- **Game Designer** (Opus): [ENDGAME-CONCEPT-2026-11-25.md](ENDGAME-CONCEPT-2026-11-25.md), three concepts and the pick.
- **Level Designer** (Opus): [ENDGAME-LEVEL-2026-11-25.md](ENDGAME-LEVEL-2026-11-25.md), with a prototype generator in
  [tools/road-prototype/](../../tools/road-prototype/) simulated over 10,000 routes per rank.
- **Economy Designer** (Opus): [ENDGAME-ECONOMY-2026-11-25.md](ENDGAME-ECONOMY-2026-11-25.md).
- **Narrative Designer** (Opus): [ENDGAME-LINES-2026-11-25.md](ENDGAME-LINES-2026-11-25.md).

Where the parts disagreed, this page decides and says so.

## 1. What the player gets

After the Tier 0 Gauntlet, Kit, the Shop Regular ("Lend me a deck and I'll pilot it"), takes the champion on a summer
bus tour of other towns' card shops. House rules out there: **you play off the shop's deck wall, never your own
binder.**
- **A run** is six stops on a rented deck.
- **At each town** you pick one of two opponents.
- **After a win** you may swap your deck for the one you just beat.
- **At the end of the line**, Kit waits with the strongest deck you passed up.

Every run is different: the route, the opponents, the walls, the theme of the week and Kit's deck come from the save's
seed. You climb 11 **Road Ranks** that change the rules (never the CPUs), fill a **Pilot's Log** of every deck you've
won with, and read Kit's postcards about why Kit never plays a deck of their own.

**Why this design** (concepts compared in ENDGAME-CONCEPT):
- **It's new.** The Library is your deck in one era, the Ladder is fixed rungs, the Alley is stakes. Nothing else
  makes you pilot unfamiliar decks across eras.
- **Your collection can't solve it,** so a 300k-DP champion has a real challenge again.
- **Difficulty is known before a duel:** every deck is already rated.
- **No gambling flavour on top of the Alley:** no stakes, no packs, no DP prizes.

**The money sink:** a DP fare per run and a 120k DP restoration of Kit's bus, plus a separate road currency, **Miles**,
that only buys cosmetics and never touches DP.

## 2. The rules (as decided here)

### A run

| Stop | What | Notes |
|---|---|---|
| Start | Pick 1 of 3 rentals | Different era buckets (2005-10, 2011-16, 2017-21, 2022-26) and styles; at least one easy to pilot; each shows its `history.json` "how" line and stars. One free reroll per run. |
| S1, S2 | Towns | A fork of two seats: the low road (centre −60..−1 Elo) or the high road (centre +0..+60) |
| S3 | The town's house champion | Authored character, 1,520 ±55 |
| S4, S5 | Towns | Forks as above |
| S6 | Kit | Plays the strongest **passed-up** deck in Kit's window (section 3) |

- **Strikes:** three. A loss costs one and replays the same seat with a fresh shuffle; the third ends the run.
  **A draw** replays the seat with no strike. Forfeiting is allowed, with no refund.
- **After each win:**
  - **Swap:** take the deck you beat, one of the town wall's 2 decks, or keep yours. Walls sit at the next stop's
    rank-0 centre +40 ±40.
  - **One boon from 3**, each at most once per run (ENDGAME-LEVEL §3):
    - Call the Toss (you choose first or second next duel);
    - Spare Tire (repairs a strike, up to the starting strikes);
    - Scouting Report (the next fork's full lists);
    - Detour (reroll one fork seat);
    - Second Look (swap for either of your last two beaten decks);
    - Rest Stop (the next loss costs no strike);
    - Postcard (+Miles);
    - Long Way Round (the next fork shows 3 seats; from bus stage 3).
- **Saves:** after every duel and every choice; a reload never rerolls. The generated route is stored in the active
  run, and so is every reroll (the start offer's, and Detour's) once used. A run takes about **90-120 minutes**
  (~100 turns plus replays, per the Level Designer's model; the concept's 60-90 was low).
- **Time-Travel Rules:** every deck plays as built under its own era's list. Decks were rated exactly that way
  (`DeckRating.Run` loads no lflist, and all 74 history decks fit their own `#banlist`), so the ratings predict road
  duels.
- **CPUs:** every seat at skill 1.0, copied like `Ladder.Opponent` (Ladder.cs:354). Nothing ever weakens a CPU.

### Road Rank (cumulative; clear rank n to open n+1)

The Level Designer's measured table replaces the concept's. Bot clear rates are from the prototype.

Predicted clear rates come from `tools/road-prototype/final.py`, 10,000 runs per rank with posters rotating
(`out-final-10k*.txt`). "Greedy pilot" is the model's perfect player. The human columns lower the player's chance by 5
and by 10 points every duel, to stand in for flying unfamiliar decks.

| RR | Rule added | Greedy pilot | Human −5 | Human −10 |
|---|---|---|---|---|
| 0 | Base | 62.4% | 49.9% | 37.8% |
| 1 | The wall offers 1 deck | 59.0% | 46.8% | 34.3% |
| 2 | Champion window +40; the champion calls the toss | 54.8% | 42.3% | 30.7% |
| 3 | Boon offers show 2 | 49.8% | 38.2% | 26.8% |
| 4 | Wall decks −20 | 42.8% | 31.4% | 21.5% |
| 5 | Every CPU calls the toss; gate p 0.33 | 32.2% | 21.9% | 14.0% |
| 6 | Kit's window 1,760-1,840; the wall after S5 shows a Kit-window deck (refuse it and Kit plays it); fork centres +20 | 25.9% | 17.2% | 10.4% |
| 7 | 2 strikes (Spare Tire repairs back to 3) | 17.1% | 10.8% | 6.2% |
| 8 | Boon offers show 1 | 11.9% | 7.4% | 4.0% |
| 9 | No Spare Tire; fork centres +15 | 8.0% | 4.8% | 2.5% |
| 10 | Kit plays two decks (1,740-1,950, each leaving p ≥ 0.33) in one sitting; a loss replays the deck that beat you | 4.4% | 2.3% | 1.1% |

Every greedy step drops 3-12 points (RR0→1 at 3.4 and RR9→10 at 3.5 are near the floor). For a human the steps flatten
to 1-3 points from RR8 (−10) or RR9 (−5), so the top ranks will feel like a wall. The human playtest decides whether to
soften RR8-10. Draws (about 1% of duels) don't move the clear rate; they only add duels.

- **Calling the toss** means the CPU picks first or second using **both** decks' first/second splits: the side worse
  for the player. That is full-strength play, not a handicap (owner decision 5). The player's **Call the Toss** boon
  wins over a CPU's call.
- **RR7:** the run starts with 2 strikes; Spare Tire repairs one, up to 3 (so it adds a strike back).
- **Clear rates are predictions,** not played duels: an Elo model of a perfect greedy pilot (`road.py`) that always takes
  the strongest swap. Humans flying unfamiliar combo rentals will clear less. `--road-sim` (real bot duels) and a human
  playtest at RR0-2 decide the final numbers.
- **Any opened rank stays selectable.**

## 3. The generator

Deterministic from the save, so a reload never rerolls:
- **Seed:** `Hash.Mix(Hash.Mix(profile.Collection.Salt, RoadSalt, (uint)runNumber), (uint)rank, (uint)attempt)` (the
  prototype seeds from salt|road|run|rank|attempt; `attempt` is the generator's internal retry; the salt lives on the
  collection, Profile.cs:312, as BackAlley.cs:983 uses it; `RoadSalt` is a fixed constant). `runNumber` counts up when
  a run starts, paid or on Kit's spare seat, so declining the offer shows the same offer again and a broke player
  still gets new routes.
- **Frozen at the start:** `Generate` runs once when a run starts, with every input fixed then (the rank, the Log's ×2
  weights, the bus stage's posters and champions). The whole route is stored in the active run and never regenerated,
  so buying a bus stage or a Log change mid-run changes nothing.
- **Inputs:**
  - **The pool** (ENDGAME-LEVEL §1): 72 seat decks (`decks/history` with a same-id character, minus frog-ftk and
    geargia); 14 wall-only decks (root and structure); stall-burn and gadgets out. **86 rentals**; rentals must average
    ≤14.0 turns.
  - **Per-deck data:** Elo, average turns and the first/second split from `deck-ratings.txt`; era from `history.json`
    (root decks dated by hand).
  - **Authored per deck in `campaign/road.txt`:** a style tag, a summon-mechanic tag and an easy-to-pilot flag.
- **Seats are fair by construction.** Each seat is filtered by predicted p against a reference deck (the best start
  offer, then the best wall decks so far).
  - **Build order:** champion first, then featured stops, then S5 back to S1.
  - **Retries:** deterministic, internal, 8 at most.
  - **The winnability gate** (reject when any stop's p < 0.40, or < 0.33 from RR5) stays as a backstop. In the
    prototype it never fired.
  - **The authored safe route** is the last fallback, and the harness proves it is never reached.
- **Anti-oatmeal rules:**
  - no deck twice in a run;
  - 3+ eras and 3+ styles on every path through the forks (Kit excluded);
  - a seat's matchup must average ≤13.5 turns;
  - a seat must be within 15 years of the reference deck (RR10's widened Kit window can exceed it, in under 0.05% of
    duels, up to 21 years);
  - one **poster** (run theme) per run, in a fixed seeded lap: 11 at the start, 2 more at bus stage 1 (ENDGAME-LEVEL
    §5, plus **Bargain Bin**: S1's low road from 1,170-1,330 and one start offer from 1,230-1,330. It seats the
    weakest decks so every rental is reachable, and is the easiest poster, 4-5 points above the no-poster clear.
    Graveyard Shift and Trap Night were loosened so every poster discards ≤6.5% of attempts at every rank);
- **House champions** (S3): spyral, tri-brigade, nekroz, power-patron, dracotail, elfnote, drytron, mermail-atlantean;
  lunalight and artmage arrive at bus stage 4. The wall before S3 must leave at least 2 champions with p ≥ pmin.
- **Kit's deck:**
  1. The strongest passed-up deck (seats not taken plus wall decks declined) in Kit's window that leaves p ≥ pmin.
  2. Else a seeded pick from window decks not met this run.
  3. Else the window's weakest deck.

  Kit's windows: 1,660-1,740 at RR0-5, 1,760-1,840 at RR6-9, and two decks from 1,740-1,950 at RR10 (each leaving
  p ≥ 0.33). Decks used 0-1 times elsewhere get ×2 weight. At RR0-5, Kit plays a passed-up deck in 69-96% of runs.
- **From RR6, Kit's deck is your choice** (`kitwall.py`): the wall after S5 shows one deck from Kit's window. Take it and
  you fly it against Kit; leave it and Kit plays it (about half the time; otherwise it would leave you under the p
  floor and Kit picks a fairer one). A greedy pilot always takes it, so Kit plays a passed-up deck in 0% of RR6+ runs
  for that pilot; refusing costs ~4-6 points of clear.
- **Route codes:** 6-character custom seeds, unlocked at bus stage 2. No fare, and no Miles, Log or rank.
- **Measured head-to-head (later):** a `Matchups` class exists (DeckRatings.cs:58), but no `campaign/matchups.txt` is
  written yet. If the re-rating in part 0 writes one, the generator could use real pair results in place of the Elo
  prediction. Not required.

## 4. Progress, rewards and the sink

**Nothing on the road pays power.** Road duels pay **no DP, no EXP, no tokens, no cards, no gifts**: the road never
calls `PayDuel`, `AddExp`, `AddWin`, `AfterDuel` or `NoteResult` (ENDGAME-ECONOMY §1). There is no EXP because
`Leveling.AddExp` grants at least 1 token per level even with `payDp: false` (Leveling.cs:66), about 50 tokens over 50
runs.

**What persists** (survives New Game+, like the Library and Ladder):
- Road Rank;
- the **Pilot's Log**: for each pool deck, the best rank at which you beat Kit holding it. The start offer weights
  unlogged decks ×2.
- Miles and souvenirs;
- bus stages;
- the run counter, and any run in progress.

**DP sinks** (ledger sinks `road.fare` and `road.bus`):
- **Fare:** 600 + 200 × RR (600 to 2,600), paid when the start offer is picked. The economy model shows the concept's
  800 + 400 × RR would bankrupt a rank-pushing player around run 35-40.
- **Kit's spare seat:** a free ride at RR0 only, when `Dp < 600` and no run is active. It pays half Miles, still counts
  for rank and the Log, and advances the run counter like a paid run. It can't make DP; it exists so a broke player
  isn't locked out. (Half-rate RR0 Miles from forfeiting free runs early is bounded by Miles per win, so it isn't
  farmable for anything but souvenirs.)
- **Kit's bus restoration,** paid once in stages that visibly change the bus:

  | Stage | DP | Adds |
  |---|---|---|
  | 1 | 10,000 | 2 more posters (Underdog Day, Back Roads) |
  | 2 | 15,000 | Route codes |
  | 3 | 20,000 | The Long Way Round boon |
  | 4 | 30,000 | 2 more house champions; the Pilot's Log wall |
  | 5 | 45,000 | The bus mat; a photo of the finished bus |
  | **Total** | **120,000** | |

**Target:** 178-220k spent over about 50 runs, which is 64-79% of a 280k champion (bank plus the post-game's one-time
~130k). The champion's bank is estimated at 150k median (60-300k) after the climb's rematch cut. At 90-120 minutes a
run, 50 runs is ~75-100 hours, not 60: the sink lasts longer than the economy doc assumed, which is fine.

**Miles** (mode-local; firewalled):
- **Earn per win:** base × (1 + 0.1 × RR), rounded to the nearest 5 with halves rounded up (`MidpointRounding.AwayFromZero`;
  the economy doc's tables used banker's rounding and may shift by 5); base 20 (low road), 35 (high road), 50
  (champion), 100 (Kit). The Postcard boon gives 25 × the same multiplier. `MaxMilesPerRun` is 315 at RR0 and 830 at
  RR10.
- **Spend:** only at Mabel's souvenir stand, on code-drawn cosmetics: 12,180 Miles in all, lasting ~60-76 runs.
  - **86 deck pins** at 40 each, one per rental (`reach.py`: all 86 rentals can be reached, 79 through an offer or a
    wall and 7 only by beating them at a seat, once Bargain Bin and RR10's wider Kit window were added); a gold rim once
    the deck is in your Log. The "every deck" goal is 86/86. 25 of those decks sit under 1,400 Elo: beating Kit holding
    one is a stunt run (p ≈ 0.15 against Kit), so say so in the Log;
  - 8 card backs, 250-1,200, gated by Kit wins and RR4/7/10 clears;
  - 12 mat tints at 125;
  - 6 poster frames, 200-500.
- **The firewall:** Miles are never bought with DP, never turned into DP, cards or tokens, never refunded. One source
  (`RoadState.EarnMiles`), one sink (`RoadState.SpendMiles`). No method both changes Miles and calls `Spend`/`Earn`.
  The source guard enforces this (section 6).

**Story** (ENDGAME-LINES):
- Kit's 11 postcards, one per first rank clear. Kit lost the card at the heart of their deck and stopped building
  decks; at RR10 Kit builds one of their own.
- The invite scene after the Gauntlet.
- Kit's last-stop lines naming the passed-up deck.
- House champions' first-meeting and beaten lines.
- 12 named towns.
- Mabel at the souvenir stand.
- Vera's cameo on the RR5 postcard.
- Four Pilot's Log milestone posters (10, 25, 50, all reachable decks). They are **ink-only** (`Card = 0`): posters
  normally frame a card's artwork (Posters.cs:14).

The lines match these rules after a fix round: the 10 champions and their towns, the Level Designer's 12 posters, Vera
at RR5 (the toss rule) and the strike joke at RR7, and RR10 as Kit's last postcard. Lines for shaddoll, exosister and
adamancipator are kept as unused spares.

## 5. Parts, files and interfaces

The build follows CLAUDE.md's pipeline. Parts A, B and C can run in parallel on separate files once part 0 is done.

**Part 0: re-rate the decks first** (study, hours, background). `deck-ratings.txt` was measured with the balanced bot
(dated Oct 29); road seats play at skill 1.0, and an older skilled run moved some decks by 200+ Elo. Run
`--rate-decks --skilled` from a harness copy, update the ratings, then rerun `tools/road-prototype/final.py` and
`posters.py` and adjust the windows before anything else is tuned.

**Part A: engine** (`src/Ygo.Engine/Campaign/`, general-purpose agent; model per owner decision 13). Copy the
Ladder's shape: `Ladder.Parse`, `Load`, `LadderState`, `Record`, `OpenDue`.

```csharp
// ---- Data (campaign/road.txt + deck-ratings.txt + history.json) ----
public sealed class RoadDeck {
  public string Slug = "", Character = "";           // Character "" for wall-only decks
  public double Elo, Turns, FirstPct, SecondPct; public int Era;
  public string Style = "", Mechanic = ""; public bool Easy, WallOnly, Reachable;  // Reachable: meetable or rentable at some rank
}
public sealed class RoadChampion { public string Id = "", Deck = "", Town = ""; public int FromBusStage; }
public sealed class RoadPoster { public string Id = "", Name = ""; public string Constraint = ""; public int FromBusStage; }
public sealed class RoadRankRule { public int Rank; public string Rule = ""; }   // rules as data, applied by the generator
public sealed class Road {
  public List<RoadDeck> Pool = new(); public List<RoadChampion> Champions = new(); public List<RoadPoster> Posters = new();
  public List<string> Towns = new(); public List<RoadRankRule> Ranks = new(); public List<string> Problems = new();
  public static Road Parse(string roadText, IEnumerable<Personality> roster, string ratingsText, string historyJson,
                           Func<string, bool>? deckExists = null);
  public static Road Load(string campaignDir, IEnumerable<Personality> roster, Func<string, bool>? deckExists = null);
  public bool IsOpen(Profile profile);                   // profile.LibraryOpen (survives New Game+)
  public bool OpenDue(Profile profile);                  // open and "road-invite" not seen: raises the `road` story trigger
  public int Fare(int rank);                             // 600 + 200 * rank
  public static int MaxMilesPerRun(int rank);            // 315 at RR0 ... 830 at RR10
}

// ---- One generated route: built once at run start from frozen inputs, then stored ----
public enum RoadSeatKind { Low, High, Third, Champion, Kit }
public sealed class RoadSeat { public RoadSeatKind Kind; public string Character = "", Deck = ""; public string? Deck2; }
public sealed class RoadStop {
  public int Index; public string Town = "";             // Index 1..6; Kit's town is "the end of the line"
  public List<RoadSeat> Seats = new();                   // 2 (fork), 3 (Long Way Round), or 1 (champion, Kit)
  public List<string> Wall = new();                      // decks offered after a win here
}
public sealed class RoadRoute {
  public int Rank, RunNumber; public uint Seed; public RoadPoster Poster = new();
  public List<string> StartOffer = new(); public List<RoadStop> Stops = new();
  public static RoadRoute Generate(Road road, RoadState state, int rank, uint salt, int runNumber); // pure; reads frozen inputs only
  public string Describe();                              // stable text, for byte-identical checks
}

// ---- Per-save state (Write/ReadLine like LadderState; hooked at Profile.cs:1091 and :1187) ----
public sealed class RoadProgress {
  public RoadRoute Route = new(); public int Stop; public int Strikes; public string Deck = "";
  public RoadSeatKind? Chosen;                           // the seat picked at this stop
  public List<string> BoonsUsed = new(), Beaten = new(), PassedUp = new();
  public string? BoonOffer;                              // the 1-3 boons offered after the last win, saved
  public bool OfferRerolled, FreeSeat, RouteCode; public bool? PlayerGoesFirst;  // set by Call the Toss or a CPU call
}
public sealed class RoadState {
  public int MaxRank, RunNumber, Miles, MilesEarned, MilesSpent, BusStage;
  public Dictionary<string, int> Log = new(); public HashSet<string> Souvenirs = new(); public RoadProgress? Active;
  public string? LiveDuel;                               // the `roadlive` line: a road duel in progress
  public void Write(StringBuilder sb); public bool ReadLine(string key, string[] parts);
  public static RoadState Read(IEnumerable<string> lines);
  internal void EarnMiles(int miles); internal bool SpendMiles(int miles);   // the only Miles writes
}

// ---- Play ----
public sealed class RoadDuelResult { public bool Won, Draw, RunOver, RunCleared; public int MilesEarned, StrikesLeft;
                                     public int? RankOpened; public List<string> NewLogDecks = new(); }
public static class RoadPlay {
  public static string? Blocked(Profile p, RoadState s, Road road, int rank);   // reason, or null (fare, live duels, open rank)
  public static RoadProgress? Start(Profile p, RoadState s, Road road, int rank, string? routeCode = null); // pays fare or spare seat; runNumber++
  public static bool RerollOffer(RoadState s, Road road);                       // once per run, saved
  public static void PickRental(RoadState s, string deck);                      // from Route.StartOffer
  public static void PickSeat(RoadState s, RoadSeatKind kind);                  // fork, or Third with Long Way Round
  public static bool? CpuCallsToss(RoadState s, Road road);                     // per rank rules; both splits; null = coin toss
  public static (RoadSeat seat, Personality opponent) Next(RoadState s, Road road); // opponent is a skill-1.0 copy
  public static RoadDuelResult RecordDuel(Profile p, RoadState s, Road road, int winner);  // strikes, draws, Log, Miles; no DP, no EXP
  public static void Swap(RoadState s, string? deck);                           // beaten deck, a wall deck, or null to keep
  public static List<string> OfferBoons(RoadState s, Road road);                // 3/2/1 by rank; saved in BoonOffer
  public static void TakeBoon(RoadState s, Road road, string boon, string? target = null); // Detour: seat kind; Second Look: deck; Call the Toss: "first"/"second"
  public static void Forfeit(RoadState s);
  public static RoadDuelResult? SettleLive(Profile p, RoadState s, Road road);  // at load: a live road duel = a loss (a strike), 0 DP
  public static bool PayBus(Profile p, RoadState s);                            // next stage, Spend(Ledger.RoadBus)
}
public static class Souvenirs { public static IReadOnlyList<Souvenir> Catalogue(Road road, RoadState s);
                                public static bool Buy(RoadState s, Road road, string id); }
// Ledger.cs: public const string RoadFare = "road.fare", RoadBus = "road.bus";   // sinks only; no road Earn kind
```

The economy doc's `Road.RecordDuel` and `Souvenirs.Buy` are these `RoadPlay.RecordDuel` and `Souvenirs.Buy`. A save
with no road lines loads as defaults.

**Live duels block each other.** A road duel can't start while `Profile.LiveDuel`, a bracket's live match or an alley
ante is live, and none of those can start while `RoadState.LiveDuel` is set. A road duel in progress is never written
to `Profile.LiveDuel`.

**Part A also owns:**
- `Posters.cs`: the four ink-only milestone posters;
- `Story.cs`: the new triggers (`road`, `roadclear:<n>`, `roadchamp:<id>`, `roadchampbeat:<id>`, `bus:<n>`), the tokens
  (`{deck}`, `{deck2}`, `{town}`), and `bus` in `StoryPlaces` (Story.cs:303);
- the `Ledger` constants and the `Profile.Save`/`Load` hooks.

**Part B: data and story** (general-purpose, Sonnet for the data entry, Opus to review):
- `campaign/road.txt` (new, with a commented format header):
  - per-deck style, mechanic and easy tags;
  - era overrides for root decks;
  - towns, champions with home towns, posters with their constraints;
  - boons, ranks, Kit's windows, the bus stages, the souvenir catalogue;
  - the line pools `road-kit`, `road-kit-double`, `road-kit-fallback`, `stand`.
- `campaign/story.txt`: the invite and postcard scenes, the bus scenes and the champion scenes, using the triggers
  and tokens part A adds.
- `personalities/mabel.profile` (new; no portrait yet, like Ada Quill).
- **Owner art, optional:** a portrait for Mabel and a bus picture. The game draws placeholders without them.

**Part C: harness** (general-purpose; model per owner decision 13):
- `--road-test` (`RoadTest.cs`): parse with no problems; deterministic generation; save, load and resume; old saves;
  New Game+; strikes; boons; swaps; Kit's rule.
- `--road-check`, 10,000 seeds per rank, no duels:
  - every constraint;
  - the gate and the fallback never reached;
  - byte-identical `Describe()` twice;
  - the oatmeal metrics (mean pairwise Jaccard ≤0.15 over 50 runs; reachable-deck coverage; no repeat triple within 10
    runs).
- `--road-sim --rr N --runs 500`: a skill-1.0 bot pilot, real duels. It reports clear rate, measured p against
  predicted (within ±8 points), turns, duels past turn 40, and deck counts.
- `Arbitrage.Road.cs` and LedgerCheck/SourceGuard additions: as listed in ENDGAME-ECONOMY §6.
- A `--campaign-test` step.

**Part D: Unity** (Unity Architect; model per owner decision 13), after A's interfaces are fixed:
- `CampaignState.Road.cs` and `CampaignScreen.Road.cs`, copying the Ladder's two files:
  - the road screen: start offer, fork, swap and boon, strikes, Kit;
  - the bus screen and Mabel's stand;
  - the Pilot's Log grid.
- Road duels wired in `DuelController` like Ladder duels.
- Code-drawn pins, card backs, mat tints and poster frames, and a code-drawn bus backdrop for the `bus` place, with
  no card pictures.
- Showing the milestone posters part A adds (`HiddenUntil = "road-invite"`, before `midnight-bill`).
- An app check, `-roadcheck`.

Then, per the pipeline: Code Reviewer, one fix round, Evidence Collector (including a played road run in the app),
Technical Writer.

**Rough effort** (guess): part 0, a day of unattended study. A+C, 3-4 days. B, 1-2 days. D, 3-4 days. Review and
evidence, 1-2 days. About **9-13 working days**.

## 6. How to verify

- `--road-test`, `--road-check`, `--campaign-test`, `--deckbuilding-test`, `--texts-check`: none may fail.
- `--arbitrage`: 0 DP-positive. The road loops (ENDGAME-ECONOMY §6) prove DP moves only by −fare or −bus stage. They
  also prove `DpEarned` never gains a `road.*` key, and that tokens, EXP, collection, wins and `DuelsPlayed` never
  change on the road.
- **LedgerCheck:** the invariant holds across fares, the spare seat and every bus stage, and across save and load.
- **SourceGuard:** Miles writes only in `RoadState`; no forbidden call (`.Earn(`, `PayDuel`, `AddExp`, `AddWin`,
  `AfterDuel`, `NoteResult`, `Tokens +=`, collection writes) in `Road*.cs`; a probe file shows the scan catches each.
- **`--road-sim`:**
  - each rank drops the clear rate by 3-12 points, and RR0 lands at 50-65%;
  - measured per-stop p is within ±8 points of predicted, or the tag or rating is wrong;
  - no duel past turn 40 more than rarely, with chaos-yata and radiant-typhoon watched.
- **App:** a full run played to Kit; the bus and stand bought from; a reload mid-run (no reroll, no double charge, the
  same boon offer); a draw; an old champion save from before the road (the road opens at once, nothing else changes).
- **A human playtest at RR0-2** before the ranks are tuned: `--road-sim` can't tell whether flying a stranger's combo
  deck is fun, or how much lower a human clears than the greedy model.

## 7. Owner decisions (decided by the owner, 2026-11-26)

1. **All three concepts:** the Borrowed Road, Mika's Regional Season and the sealed draft (Gus's Time Capsule). **All
   three are designed before anything is built.** The Season and the draft need their own specs; the draft must solve
   measuring CPU sealed decks, which have no ratings.
2. **Unlock with the Library** (`LibraryOpen`, at the Gauntlet's clear).
3. **Mix eras** with Time-Travel Rules.
4. **Fares 600 + 200 × rank, plus the 120k bus** in 5 stages.
5. **A CPU calling the toss counts as full strength** (ranks 2 and 5 keep it).
6. **Rewards: cosmetics, story, and very rare cards, one per first clear of each Road Rank** (11 cards over the whole
   road, from a curated rare list). This changes §4's "no power rewards":
   - The card is the only value the road pays.
   - It comes once per rank, never per run, so it can't be farmed.
   - `--arbitrage` must prove that selling all 11 at the sell desk returns less than the fares needed to clear the ranks.
   - The list goes in `campaign/road.txt`, picked by the Economy Designer before the build.
7. **Kit hosts; Mabel is a new character; the 12 towns are named.**
8. **Named towns** (as 7).
9. **No re-rating first:** use the current `deck-ratings.txt`. Part 0 is dropped. The risk stays: those ratings used the
   balanced bot, while road CPUs play at full strength, and an older skilled run moved some decks by 200+ Elo.
   `--road-sim` (real duels at skill 1.0) must check measured win rates against predicted, and the windows get tuned
   from that.
10. **After Rank 10, Kit's Tag Cup partner deck becomes Kit's own deck**, as a follow-up after the road ships.
11. **The review's B4 sinks** (Champion's shelf, collection log) come later, separately, in DP, never Miles.
12. **Route codes pay nothing.**
13. **Opus builds** parts A, C and D; Sonnet does the `road.txt` data entry and the docs.
14. **Kit keeps the fairness floor:** the strongest passed-up deck that still leaves the player p ≥ pmin.
