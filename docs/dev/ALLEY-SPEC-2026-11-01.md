# The Back Alley: build spec (2026-11-01)

A Game Designer agent's build spec for POST-GAME-PLAN-2026-10-31 step 9 (the owner said "go"). It turns "The Back
Alley", "Tournaments (brackets)", "The alley's look", the Headliners doc and the owner's decisions into something
implementation agents can build without guessing. Read-only on code: nothing here is built yet.

- **Prices are the Economy Designer's** (docs/dev/ALLEY-ECONOMY-2026-11-01.md, landed while this was written). This spec
  adopts its model and its knob names (one class, `AlleyEconomy`, like `ShopPrices`) as the recommended defaults,
  pending the owner's confirmation. Every DP number is a named knob, marked [P] until confirmed. The model in short:
  - Street and Den stakes are **held DP**: back on a win or a draw, lost on a loss or a quit. No entry fee.
  - The CPU's staked card is worth at least your stake (`StakeFloor`).
  - Alley duels pay no DP; EXP at the Library's rate; level-ups pay tokens only (`payDp: false`).
  - Won copies are Paid 0, like Prize copies; a 4th copy keeps your best three for 0 DP.
  - Moth pays exactly the Sell desk's price; no Contraband Packs for sale; the pack's 5th card is a sealed 9-10 slab.
  - Alley Brawl packs 2 / 1; Midnight Open 4 / 2 / 1.
- **Lines are not written here.** The Narrative Designer writes them in parallel (docs/dev/ALLEY-STORY-2026-11-01.md).
  This spec defines the triggers, hooks and data formats they fill (section 12).
- **CPUs are not weakened.** They play their decks at full strength and are exempt from ban lists. The Alley list
  binds you; the splash builder builds to it by construction (section 3).
- **Owner rules this spec keeps:**
  - your own cards are at stake only at Orvel's Table: one spare card, never one a saved deck needs; a lost card waits
    on Moth's rail for 5 duels at 1.5× its value;
  - slabs play in decks;
  - the Alley list is Traditional minus the exclusions;
  - the alley opens after your first Library cup clear;
  - no banned-card store: Moth sells a small shelf of rare, high-grade contraband;
  - brackets of 8 now, 16 after The Black Market;
  - at turn 40 in the alley, the higher LP wins.
- **Headliners decisions this spec keeps** (docs/dev/HEADLINERS-2026-11-01.md): a headliner closes each event and the other
  can turn up earlier as a wildcard; a normal coin toss; a duel lost on its first turn costs no ante; the burn kills
  only at Nyx's Table; a watch-only exhibition, no betting.

**Changelog**
- v1 (2026-11-01): first spec.
- v1.2 (2026-11-02): aligned with docs/dev/ALLEY-STORY-2026-11-01.md: Ines out of the Midnight Open (six-samurai in),
  plain `bracketwin` for Locals only with alley records kept apart, `heldcard` in place of `inescard`, `pennywin`,
  the story doc's placeholders, speaker token and extra hooks, the HELD poster, no "Fen", "the Unlisted binder".
- v1.1 (2026-11-02): aligned with docs/dev/ALLEY-ECONOMY-2026-11-01.md: held stakes instead of fees, `AlleyEconomy` knob
  names, per-CPU binders drawn from the CPU's deck, Orvel's matching rule, the van's shelf markup and no packs for
  sale, the slab in the Contraband Pack, bracket packs and pay, the ledger keys, `--alley-probe` for the arbitrage
  checks.

## Contents

1. What already exists (don't re-spec it)
2. Findings in the code (read before building)
3. The Alley list and legality
4. The turn-40 rule
5. Unlock, the district tab and the alley map
6. Tables and events
7. Generated opponents and the splash builder
8. Headliners in the alley
9. Ante, end to end
10. Moth's van: Under the Counter
11. Alley brackets
12. The Unlisted binder, cast, story hooks and data formats
13. The alley's look: theme, mat, sound, assets
14. Save, New Game+ and the ledger
15. Knobs
16. Build split
17. Owner decisions

## 1. What already exists (don't re-spec it)

**Engine (src/Ygo.Engine/Campaign/):**
- `Alley.cs`:
  - `Alley.Events`: Dumpster Row (Elo 1276-1480, era 2006, splash 3, skill 0.7), The Basement Game (1416-1592, 2014,
    4, 0.85), The Black Market (1585-1693, 2019, 4, Extra splashes off, 0.95).
  - `Alley.Excluded` (33 names), `Alley.List(traditional)` (Traditional with the exclusions at 0).
  - `Alley.Contraband(lists, alley, eraCap, printings)`: every card Forbidden in some TCG list, out by the year,
    allowed in the Alley.
  - `Alley.LoadCurated` (campaign/alley.txt), `Alley.Pick`, `Alley.Lacks`, `Alley.Roles`, `Alley.FromEra`.
  - `Alley.Splash(base, event, pool, alley, seed, cards, curation)`: the seeded splash builder.
- `campaign/alley.txt`: the curated contraband per event, `for:` era cards, `skip:` bases, `cap:` and `size:`.
- `Brackets.cs`, `PrizePacks.cs`, `DeckRatings.cs` (`Matchups`): Library Locals (8 players, single elimination, CPU
  matches by Elo odds and a coin fixed at entry, Watch the final, a save mid-bracket replays a live match).
- `GradeEconomy.cs`: `Pricing.V` (one price), `SellDesk` (min(V/4, Paid/2); refuses Paid 0), `UsedMarket` (24
  listings by epoch of 5 duels), `GradingDesk`.
- `Profile.cs`: per-copy `CardCopy`, `CopySource`, `Grades`, `Collection.Take` (the 4th-copy rule), `Ledger`,
  `Profile.Earn/Spend`, `NoteResult` (counts `DuelsPlayed`), `NewGamePlus`.
- `Library.cs`: cups, `Library.Record` (returns `ClearedCircuit` on a first clear), `Library.Events`, `FormatRules`.
- `Posters.cs`: card-art posters with an `Earned` predicate.
- `Story.cs` + `campaign/story.txt`: scenes on triggers, shown once.
- `ShopVoice.cs` + `campaign/shop.txt`: pools of lines by hook (the van reuses it).
- `HouseRules.cs`: `YataLock` (a house rule that ends a duel through `DuelRunner.Concede`).
- `AI/Playbooks`: `Playbook.RouteSignaled`, `YataLockPlaybook`, `FrogFtkPlaybook`; `decks/headliners/yata-mags.ydk`
  and `mass-driver-moss.ydk` (they play as `chaos-yata` and `frog-ftk` through `#recipe`).

**Harness (src/Ygo.Harness/):** `--alley-gen`, `--alley-rate`, `--endless-test`, `--bracket-sim`,
`--bracket-calibrate` (AlleyStudy.cs), `--combo-goldfish` (ComboGoldfish.cs), `--arbitrage`, `--campaign-test`
(CampaignTest.cs, with `BracketChecks`, `LedgerCheck`, `ShopTest`), and the Economy Designer's `--alley-probe`
(AlleyProbe.cs: binders, stakes, Orvel's ladder, the shelf, the pack, brackets and an exhaustive loop search over 11
alley actions, on a model of the rules; it fails if any loop is DP-positive).

**Unity (unity/Assets/Scripts/):**
- `UiTheme.cs`: `MainStreet`, `BackAlley` (the plan's tokens), `NeonCyan`, `UiTheme.Current`, `IsAlley`; the roles
  and rules in docs/dev/UI-THEME.md; `-themecheck` (DuelController.ThemeCheck.cs).
- `CampaignScreen.cs`: the "Circuits | Library" switch (`BuildDistricts`, `ShowDistrict`), the Library shelf.
- `BracketScreen.cs` + `DuelController.Brackets.cs`: the 8-player bracket screen, versus, results, Watch the final.
- `ShopScreen.OpenPrize` / `OpenPrizes`: the pack reveal for `PrizePull` lists.
- `GradingDeskScreen.cs`: tabs Grade | Sell | Used.
- `Playmat.cs` (procedural mats from campaign/mats.txt), `Sounds.cs` + `Sfx.Themes` (procedural music).

**Measured (docs/dev/PHASE0-FINDINGS-2026-11-01.md, "The Back Alley"; logs in build/phase0/):**
- Splash gain with the curated lists and the tuned bases sitting out: Dumpster Row +52, Basement +57, Black Market
  +50 (target +60 to +150; within the measuring error). Speed-up 0.4 / 0.6 / 0.6 turns.
- Endless duels: 0% at the prompt cap, 0.8% past turn 40, 0% draws.
- Bracket odds: Elo odds are off by about 85 points against head-to-head duels; Alley Brawl title 62% at 1,800;
  Midnight Open 6% at 1,800 and 22% at 1,947.
- `--alley-gen` today (run for this spec, 0 decks short or illegal):
  - Dumpster Row: goat-control, chaos-yata, warriors;
  - The Basement Game: inzektor, plant-synchro, warriors, blackwing, dino-rabbit, teledad, six-samurai, shaddoll;
  - The Black Market: shaddoll, kozmo, danger-dark-world.

## 2. Findings in the code (read before building)

- **F1. Splash sizes differ from the plan's table.** The code's 3 / 4 / 4 with Extra Deck splashes off were measured
  after the plan's 2 / 3 / 4 (1 Extra). Build with the code's values.
- **F2. The exclusion list grew.** `Alley.Excluded` has 33 names: the plan's 21 plus 12 more locks and dice (Phase 0
  says 13). All 33 resolve to one card each with alias 0 in `vendor/BabelCDB/cards.cdb` (checked for this spec).
- **F3. Base decks are chosen in the harness only.** `AlleyStudy.Bases` reads the ratings at run time. The app needs
  them fixed in data (section 6).
- **F4. A splash deck doesn't name its base's playbook.** The harness passes the base's name to `Playbooks.ForDeck`.
  In the app, `Playbooks.ForDeck(deck)` reads `#recipe`, so `Alley.Splash` must set `Deck.Notes["recipe"] = base`.
- **F5. There is no turn cap.** `--endless-test` only counts duels past turn 40.
- **F6. Brackets are fixed at 8.** `Bracket.Size`, `MatchCount`, `Final`, `RoundOf`, `MatchesIn`, `Placement`, the
  save lines and `BracketScreen` all assume 8. The Midnight Open needs 16 and a Bo3 final.
- **F7. `Pricing.V` tops out at 15,000** (a Starlight Rare slab 10: 2,500 × 6). The plan's shelf runs 5,000-25,000.
  ALLEY-ECONOMY resolves it with a shelf markup (`ShelfMarkup` 1.5× value, rounded to 500: 5,500-22,500) while Moth
  buys at the Sell desk's price. Adopted (section 10).
- **F8. No art for the new cast.** No portraits for Moth, Baz, Odile or Penny, no hooded silhouette, no stencil font
  (section 13).
- **F9. The Sell desk's refusal reads "A gift: not for sale."** Ante winnings are Paid 0 too, so they already sell for
  nothing; they need their own words.
- **F10. "Cyber-Jar Wick" can't appear.** Cyber Jar is ever-Forbidden (lflists/history.lflist.conf) but isn't in any
  curated list, and regulars only splash curated cards.
- **F11. The district switch is 240 px wide** (`CampaignScreen.BuildDistricts`) and holds two tabs.

## 3. The Alley list and legality

**The list.** `Alley.List(traditional, cards)`, unchanged: the Traditional list (0 Forbidden, every card Forbidden on
today's list at 1) with the 33 exclusions at 0.

**The exclusions, in two groups** (new: `Alley.Never` and `Alley.BossOnly`, both subsets of `Alley.Excluded`):

| Group | Cards (names as in cards.cdb) | Why |
|---|---|---|
| `Never` (10) | Butterfly Dagger - Elma, Ultimate Offering, Wind-Up Hunter, Self-Destruct Button, Last Turn, Sixth Sense, Exchange of the Spirit, Fiber Jar, Number 67: Pair-a-Dice Smasher, Chicken Game | Loops, a draw, coin and dice endings, a deck-out win, a reset, an LP stall the bot can't judge. No deck in the alley ever runs them |
| `BossOnly` (23) | Magical Scientist, Cyber-Stein, Dark Strike Fighter; Imperial Order, Royal Oppression, Vanity's Emptiness, Kaiser Colosseum, Number 16: Shock Master, Anti-Spell Fragrance, Rivalry of Warlords, Gozen Match, Dimensional Barrier, Mystic Mine, Barrier Statue of the Torrent, Barrier Statue of the Drought, Barrier Statue of the Inferno, Barrier Statue of the Stormwinds, Archlord Kristya, There Can Be Only One, Summon Limit, Spell Canceller, Secret Village of the Spellcasters, Number 41: Bagooska the Terribly Tired Tapir | Turn-1 FTKs and floodgates. Allowed only in an authored boss deck that names them in `allow:` and passes the endless-duel check |

- Ids (cards.cdb): Elma 69243953, Ultimate Offering 80604091, Wind-Up Hunter 16923472, Self-Destruct Button 57585212,
  Last Turn 28566710, Sixth Sense 3280747, Exchange of the Spirit 17484499, Fiber Jar 78706415, Pair-a-Dice 35772782,
  Chicken Game 67616300, Magical Scientist 34206604, Cyber-Stein 69015963, Dark Strike Fighter 32646477.
- Old texts count as the same card: Goat and Pre-Errata entries alias their modern card, and `Banlist.Allowed` counts
  them together.

**Your decks.**
- New: `Alley.Rules(Banlist alley)` returns `FormatRules { List = alley, Pool = "", Passes = 0, Forced = true }`.
  - No Ban Passes (the plan: "Back Alley: no passes. It has its own list").
  - No pool: any card released by now.
  - One copy of every card Forbidden today; 0 of the exclusions; the 3-copy cap; 40-60 Main, up to 15 Extra.
- **Where it applies:** every duel you play in the alley (tables, Orvel's Table, Nyx's Table, Alley Brawl, Midnight
  Open) and the deck editor opened from the alley. Nowhere else.
- **Enforced like a cup:** the versus screen checks `CampaignState.Problems(deck, AlleyRules)` and refuses to start
  with the first problem shown ("Pot of Greed: the Alley allows 1, you have 2."), as the bracket versus does today.
- **No loaners in the alley** (decision O3). The versus arrows cycle your own decks only.
- **No leaks:** `CampaignState.Rules(circuit)`, `CupRules`, `RoundRules` and the Format Master stamp never see the
  Alley list. A harness check proves it (section 16, A3).

**CPU decks.** CPUs are exempt from ban lists. Each kind of alley deck is built to its own rule, and the harness
checks it (`AlleyData.Problems`):

| Deck | Rule it's built to | Checked |
|---|---|---|
| Generated regular (splash) | The Alley list, by construction (`Loaners.Adjust` to it, then the splash) | `DeckRules.Validate(deck, alleyList)` is empty for 1,000 seeds an event |
| Headliner | Its own era's list (`list:`), as a visitor, with owner-approved copies over it (`over:`) | Validates under `list:` once the `over:` cards are counted at their deck counts |
| Authored boss (Orvel, Nyx, Nyx's Table seats) | Plays as built | Every `Excluded` card in it is named in its `allow:`; `allow:` names no `Never` card; it passes `--endless-test` (section 4's targets) |

- "Authored boss decks may keep floodgates" means exactly the last row: a `BossOnly` card in an authored deck is
  legal for that CPU only when named in `allow:` and measured. The splash builder never picks an excluded card (the
  contraband pool already drops them).

## 4. The turn-40 rule

**Where:** a new house rule in `src/Ygo.Engine/HouseRules.cs`, beside `YataLock`, attached the same way.

```csharp
// HouseRules.cs
public sealed class TurnCap
{
    public const int AlleyTurns = 40;
    /// <summary>Watches a duel: when turn lastTurn + 1 would start, the higher LP wins; equal LP is a draw.</summary>
    public static TurnCap Attach(DuelRunner runner, Duel duel, int lastTurn = AlleyTurns);
    /// <summary>Both players' LP as the rule tracks them (from Damage, PayLpCost, Recover and LpUpdate, as BoardState does).</summary>
    public int Lp(int player);
}
// DuelRunner.cs
public const byte ReasonTurnCap = 0xF2;
/// <summary>Ends the duel as a draw once the messages being handled are done (MSG_WIN with player 2).</summary>
public void EndInDraw(byte reason);
```

- **The moment:** on the `Msg.NewTurn` that starts turn 41 (the runner's `Result.Turns` becomes 41), before any
  prompt of that turn. Turn 40 is played in full.
- **Who wins:** the player with more LP (`Concede(lower, ReasonTurnCap)`). Equal LP: `EndInDraw(ReasonTurnCap)`.
- **LP tracking:** starts at `duel.StartingLP` and follows the four LP messages, as `BoardState` does (lines 101-119).
- **Order with other endings:** a core win in the same message batch counts first (`Concede` already defers);
  `YataLock` and `TurnCap` can both be attached.
- **Only in the alley.** Attached in the app for every alley duel (tables, brackets, the exhibition), and in the
  harness for every alley study. Never in circuits, cups, Locals or the final.
- **Draw handling:** an alley draw replays the match (section 9 for the stakes). A bracket draw replays the match as
  Locals already does.
- **What you see:**
  - the versus screen's rule chip: "Turn 40 · higher LP wins";
  - from turn 30 [P] in an alley duel, the board's turn counter reads "Turn 34 / 40";
  - the result line: "Turn 40: Three-Pot Teddy wins on life points, 3,200 to 1,900." / "Turn 40: level on life
    points. A draw.";
  - `DuelNarrator` gets the same line for logs and replays.
- **Targets** (the plan's, kept): prompt-cap stops under 0.5%, duels decided by the cap under 2%, draws under 1%.
  `--endless-test` reports "decided at turn 40" in place of "past turn 40".

## 5. Unlock, the district tab and the alley map

**Opening.**
- New: `BackAlley.Open(Profile profile, Library library)`. It sets `profile.AlleyOpen` and returns true once
  `library.ClearedCount(profile) >= 1` and the alley isn't open yet. The caller raises `alley` (Kit's rumor scene).
- **Where it's called:**
  - after a Library cup result whose `reward.ClearedCircuit` is set: `alley` is added after that duel's `cupclear:`
    events, so the rumor plays after the cup's own scene;
  - at load (`CampaignState`), for a save that cleared a cup before the alley shipped: the tab appears, and `alley`
    plays the next time the campaign screen opens.
- Never before a cup clear; never from a Locals title alone.

**The district tab.**
- The switch becomes **Circuits | Library | Back Alley**, left of the header as today (CampaignScreen.cs:1437). The
  box grows from 240 to 390 px: Circuits 104, Library 130, Back Alley 140, 6 px gaps (F11).
- Back Alley shows once `AlleyOpen`; "(new)" until first shown (seen key `district:alley`), like the Library's.
- Showing it:
  - sets `UiTheme.Current = UiTheme.BackAlley` and rebuilds the screen, with a 300 ms crossfade (instant under
    Reduced motion);
  - starts the alley's rain bed (section 13);
  - raises `alleyvisit` the first time (Kit takes you down; Baz and Moth say hello) and unlocks the NO LIST poster;
  - the status line: the alley's help line (Narrative).
- Leaving it (Circuits or Library) sets `MainStreet` back, with the same crossfade, and stops the bed.
- `CampaignScreen.InAlley` (new, public) tells the deck editor and the duel controller to use the Alley's rules.

**The alley map** (the left side, reusing the circuit map's list-of-nodes layout and scroll, not its art):
- A procedural backdrop: brick, a chain-link fence and tape (section 13), no new image.
- Nodes, top to bottom, each locked until it opens (lock line "Clear The Basement Game"):

| Node | Opens | Panel on the right |
|---|---|---|
| Dumpster Row | with the alley | Table panel (4 seats) |
| The Basement Game | Dumpster Row cleared | Table panel |
| The Black Market | The Basement Game cleared | Table panel |
| Orvel's Table | The Black Market cleared (decision O1) | Orvel's panel (one seat, both stakes) |
| Nyx's Table | The Black Market cleared | Table panel (4 authored seats) |
| Moth's van | with the alley | Opens Under the Counter (section 10) |
| Baz's chalkboard | Dumpster Row cleared | Alley Brawl entry; the Midnight Open's once The Black Market is cleared; the exhibition (Headliners phase 3) |

- A table node shows ✓ once cleared, and its seat progress tonight ("2 / 4").

**The table panel:**
- Title, tier and stake: "DUMPSTER ROW · Street · stake 300 DP (back if you win)".
- Four seat rows in order: hooded silhouette (tinted), handle, "???" for the deck until met, then the base deck's
  title ("Goat Control, splashed"); the card they put up (card name, rarity, condition, value); won tonight ✓.
- One red **Duel** button for the next unbeaten seat. Seats are played in order.
- Lines: the seat's binder count ("Binder: 9 / 12"), the rules chip ("Alley list · no Ban Passes · turn 40 · higher
  LP wins"), "Leaving a duel counts as a loss."
- **Encore** (after both of the table's headliners are beaten): a second button that picks a headliner to rematch at
  the same stake, from its own binder.

## 6. Tables and events

**Data: `campaign/alley-events.txt` (new).** The structure of the alley. The contraband stays in `alley.txt`.

```
# The Back Alley's tables and brackets (docs/dev/ALLEY-SPEC-2026-11-01.md). Contraband per event: alley.txt.
#   table: <name>                  starts a table
#   id: <slug>                     saves and keys
#   tier: street | den | orvel | nyx
#   event: <Alley.Events name>     the splash knobs: era cap, splash size, skill, Elo range
#   opens: alley | <table name>    with the alley, or once that table is cleared
#   bases: <deck>, <deck>, ...     the base decks regulars are built from (fixed; the harness checks them)
#   splash-elo: <n>                the measured splash gain, for bracket odds and the panel's Elo
#   headliners: <id>, <id>         the table's two headliners, in order
#   closer: <base>                 the 4th seat while no headliner of the table is ready: this base, a full splash, skill 1.0
#   seat: <n> <kind> ...           an authored seat (orvel and nyx tiers): kind regular | headliner | authored
#   mat: <mats.txt key>            music: <Sfx theme>
#   headliner: <id>                starts a headliner: handle:, deck:, table:, list:, over:, pieces:, stakes:, ready:
#   bracket: <name>                starts a bracket: kind:, size:, opens:, entry:, pay:, packs:, tokens:, final:, field:
#   handle: <card name> | <prefix> a regular's handle prefix when that card is its best splash
#   names: <name>, <name>, ...     first names for handles
```

**The tables:**

```
table: Dumpster Row
id: dumpster-row
tier: street
event: Dumpster Row
opens: alley
bases: goat-control, chaos-yata, warriors
splash-elo: 52
headliners: yata-mags, return-rook
closer: chaos-yata
mat: alley-dumpster-row
music: alley

table: The Basement Game
id: basement
tier: den
event: The Basement Game
opens: Dumpster Row
bases: inzektor, plant-synchro, warriors, blackwing, dino-rabbit, teledad, six-samurai, shaddoll
splash-elo: 57
headliners: mass-driver-moss, trishula-trish
closer: six-samurai
mat: alley-basement
music: alley

table: The Black Market
id: black-market
tier: den
event: The Black Market
opens: The Basement Game
bases: shaddoll, kozmo, danger-dark-world
splash-elo: 50
headliners: master-plan-marlo, gumblar-gail
closer: kozmo
mat: alley-black-market
music: alley

table: Orvel's Table
id: orvels-table
tier: orvel
opens: The Black Market
seat: 1 authored power-patron orvels-house | skill 1.0
mat: power-patron
music: orvel

table: Nyx's Table
id: nyx
tier: nyx
opens: The Black Market
seat: 1 regular | event: The Black Market | skill 0.92
seat: 2 authored chaos-yata chaos-yata | skill 0.95
seat: 3 headliner scientist-turtle | else regular 0.97
seat: 4 authored nyx gravekeepers-contraband | skill 1.0 | allow:
mat: nyx
music: nyx
```

- `closer:` picks: the table's highest-rated base (chaos-yata 1414, six-samurai 1491, kozmo 1613). [P] until
  `--alley-study` measures them.
- `orvels-house` and `gravekeepers-contraband` are new authored decks (decks/alley/, Phase C). Until they're built,
  those tables stay locked with "Coming soon" (the harness check skips them).

**Nights.** A table's four seats are one **night**.
- The night's lineup is drawn when the table first opens (night 0), and again after the night's fourth seat is won
  (night 1, 2...). Seats you've beaten stay beaten until the night is over.
- Seats are played in order. The next unbeaten seat is the one you can duel.
- A lost or drawn seat stays exactly as it was (same opponent, same deck, same stake). Quitting never rerolls.
- **First clear** of a table (night 0's fourth win): its scene (`alleyclear:<table>`), the next table opens, its
  prize (`AlleyEconomy.ClearTokens` [P] 1 Card Token, no DP; this spec's, not in ALLEY-ECONOMY: the Economy Designer
  should confirm it), and Baz chalks your name on the board (a line).
- **Repeat nights** pay only the cards won. Nothing else is paid twice.
- Orvel's Table has one seat: every sitting is a night of one; Orvel's stake changes each sitting (his binder).
- Nyx's Table's authored seats stay the same every night; seat 1 (and seat 3's fallback) are drawn anew each night.

**Who sits where (regular tables):**
- Seats 1-3: generated regulars (section 7), or a wildcard headliner (section 8).
- Seat 4: the table's headliner (section 8), or its `closer:` until one is ready.

**Duel rewards** (all knobs, section 15):
- No DP for a win, a loss or a draw (`AlleyEconomy.DuelDp` 0). The CPU's card is the prize; your held stake comes
  back on a win.
- EXP at the Library's rate (`Leveling.DuelExp(library.ExpCircuit, winner, firstWin)`: 375 a win, 125 a loss, +40 a
  first win against that seat's character), level-ups paying tokens only (`payDp: false`), as Locals does.
  ALLEY-ECONOMY Evidence 4: with level DP, Street at 75% would net about +87 DP a duel.
- `profile.NoteResult("alley:<table id>.<seat>", winner)`, so `DuelsPlayed` counts alley duels.
- Wins and losses count against the character id (`alley-regular` for every generated regular; the headliner's,
  Orvel's or Nyx's id otherwise). Card unlocks don't fire for `alley-regular` (it has no signature).

**Targets** (the plan's): your best option wins Dumpster Row 75%, Basement 65%, Black Market 60%, Nyx 55%; the
starter 65% / 50% / 40%. Orvel's Table [P] 55-60% (not in the plan; decision O1). Headliners 70% / 60% / 55%.

## 7. Generated opponents and the splash builder

**Reuse `Alley.Splash` as it is**, with its curated path. The rules, as built and measured:
1. **Base deck:** the seat's base, drawn from the table's `bases:` (seeded; a night uses three different bases when
   the table has three or more, else repeats as few as possible).
2. **Made legal first:** `Loaners.Adjust(base, alleyList)` (its own cards topped up, as a loaner is). Side Deck
   cleared.
3. **Contraband:** the event's curated list (alley.txt), filtered to the event's pool (`Alley.Contraband`: ever
   TCG-Forbidden, out by the era cap, not excluded). Picked by `Alley.Pick`:
   - the base's own era cards first (`for:` lines, up to half the splash);
   - then the roles it lacks (`Alley.Lacks`), in priority order with a seeded nudge;
   - then the rest, best first;
   - one card a role, two for draw (the plan's "one board wipe, one steal, one Maxx "C"" holds per added role);
   - `cap:` and `size:` per base; no card the deck already runs.
   - **Fitted cards** need their support (`needs:`): Crush Card Virus 3 DARKs with 1000 ATK or less; Black Luster
     Soldier and Chaos Sorcerer 4 LIGHT and 4 DARK; Xyz, Synchro and Fusion payoffs need that Extra Deck.
4. **Cut** (`CutFor`, by `AutoDeck.Value`, lowest first): never the archetype (a setcode on 6+ cards), a draw or
   search card, a card the Alley limits below 3, a card on the event's list, a 2400+ ATK monster unless a beater
   replaces it, or contraband just added. First the weakest card of the same role and kind, then one copy of a
   two-of, then a one-of. An Extra Deck payoff (only from `for:` lines) replaces the least valuable non-archetype Extra
   card.
5. **Validate:** `DeckRules.Validate(deck, alleyList)` must be empty and the deck must have its full splash. If not,
   the builder tries the next sub-seed (up to 8), then plays the adjusted base unsplashed (logged; the harness check
   fails if any seed of any base needs that).
6. **New (F4):** `Splash` sets `Deck.Notes["recipe"] = baseDeck.Name`, so the base's playbook plays it.

**Seeds (deterministic, saved):**
- A seat's seed: `Hash.Mix(profile.Collection.Salt, 0xA11E ^ tableIndex, (uint)(night * 16 + seat))`.
- From it, in order: the base, the splash seed, the handle's name, the tint, the wildcard roll (section 8).
- **Saved as built** (`alleyopp` line, section 14): base, the added and cut codes, the handle, the tint. The deck is
  rebuilt from the saved lists (`Loaners.Adjust(base)`, then remove the cuts, add the adds), never re-rolled. So an
  update to alley.txt never changes an opponent you've already met. If the base deck file is gone, the seat is drawn
  again with the next sub-seed.

**Who they are:**
- **Handle:** the prefix of the splash's most valuable added card that has a `handle:` line, plus a first name from
  `names:` not used in the same night ("Three-Pot Teddy"). No handle line: "<Base title> Regular". Lists filled by
  the Narrative Designer; the plan's examples go in as the first lines:

```
handle: Pot of Greed | Three-Pot
handle: Raigeki | Thunder
handle: Change of Heart | Heartbreak
names: Teddy, Wick, Rosa, Dutch, Sal, Juno, Pike   # no "Fen": Firewall Fen is retired (HEADLINERS)
```

- **Personality:** a copy of the base deck's owner character (the roster character whose `decks:` lists it first;
  `Personality.Balanced` if none) with Id `alley-regular`, Name = the handle, Title = the table's name, and Skill = the
  event's (0.7 / 0.85 / 0.95). Intro, win and lose lines come from the `regular:` voice pools (section 12).
- **Look:** the hooded silhouette (section 13) tinted by `Tint` (hue from the seed, saturation 0.45, value 0.75).
- **Elo** (panel and brackets): the base's rating plus the table's `splash-elo`.

**The versus screen shows the contraband:**
- The CPU side's three show-off cards are its added contraband, most valuable first (`BackAlley.Showcase`). Fewer than
  three added: the rest are its base's showcase. A headliner shows its `pieces:`.
- A label over them: "CONTRABAND". The deck line: "Goat Control + Raigeki, Pot of Greed, Change of Heart".
- The stake chip under the CPU side (section 9), and your stake under yours at Orvel's Table.
- The scouting button reads the base deck's report, with the added cards listed.

## 8. Headliners in the alley

The headliner itself (deck, route, show, measuring) is docs/dev/HEADLINERS-2026-11-01.md. This is its place in the alley.

**Data** (in alley-events.txt):

```
headliner: yata-mags
handle: Yata Mags
deck: yata-mags
table: Dumpster Row
list: TCG April 2004
over: Chaos Emperor Dragon - Envoy of the End
pieces: Chaos Emperor Dragon - Envoy of the End, Yata-Garasu, Witch of the Black Forest
stakes: Yata-Garasu, Chaos Emperor Dragon - Envoy of the End, Sangan, Witch of the Black Forest
ready: yes

headliner: mass-driver-moss
handle: Mass Driver Moss
deck: mass-driver-moss
table: The Basement Game
list: TCG September 2010          # the list the deck was built to (the Headliners author confirms the name)
pieces: Mass Driver, Substitoad, Ronintoadin
stakes: Mass Driver, Ronintoadin, Fishborg Blaster
ready: yes
```

- The other four (`return-rook`, `trishula-trish`, `master-plan-marlo`, `gumblar-gail`) and `scientist-turtle` go in
  with `ready: no` until they pass the Headliners go/no-go. `ready: no` headliners never sit.
- Each headliner has a personality file of its id (name = handle, skill 1.0, and the `combo` / `fizzle` lines the
  Headliners doc names). New files: `personalities/<id>.profile`, no `ladder:` line.

**Placement:**
- **Seat 4 closer:** of the table's ready headliners not yet beaten, the first in `headliners:` order... unless the
  night's seed picks the other (50/50 when both are ready and unbeaten). None left unbeaten: a regular at the
  `closer:` rules, and the Encore button opens.
- **Wildcard:** when a night is drawn, if the table has another ready, unbeaten headliner that isn't the closer, it
  takes one of seats 1-3 with chance `WildcardChance` [P] 1/3 (seat by seed). It's saved like any seat, so quitting
  never rerolls it.
- **Encore:** a beaten headliner sits only when you pick it with Encore. An encore is a one-seat night: the table's
  stake, and the card from the headliner's own binder. No first-clear pay, no poster.
- **Its list:** it plays its deck as authored (its era's list plus `over:`). Not the Alley list.
- **Its stake:** its own binder, led by `stakes:` in order (section 9). A `stakes:` card worth less than the floor
  at its rolled printing is rolled again at a rarer printing, so the combo piece always leads.
- **Turn order:** a normal coin toss. The first-turn guard (section 9) applies.
- **Beaten:** `headlinerbeat:<id>` the first time; its deck joins the Unlisted binder with Watch the combo.
- **The show layer** (banner, meter, payoff, fizzle, commentary, Combo Breaker posters, Watch the combo, the
  exhibition) is the Headliners doc's Phases 2-3, built in Phase D here.

## 9. Ante, end to end

The model is ALLEY-ECONOMY's: **your stake is DP held in the save**, back on a win or a draw, lost on a loss or a
quit. The CPU puts up a card from its binder. Only at Orvel's Table do you also stake a card.

**Tables and stakes** (knobs on `AlleyEconomy`, section 15):

| Tier | Tables | Your stake (held) | The CPU's card |
|---|---|---|---|
| Street | Dumpster Row | `StreetStake` [P] 300 DP | Raw only, worth `StreetStake × StakeFloor` to `StreetCap` [P] 1,000 (300-1,000) |
| Den | The Basement Game, The Black Market, Nyx's Table | `DenStake` [P] 1,000 DP | Raw 8-9 or a slab 8-10, worth 1,000 to `DenCap` [P] 5,000 |
| Orvel | Orvel's Table | `OrvelStake` [P] 3,000 DP **and** one spare card worth at least `OrvelMinCard` [P] 1,000 | A slab 9-10 at Super Rare or better, worth 1-2× your card (`OrvelMatch` [P] 1.0-2.0) |

- `StakeFloor` [P] 1.0: the CPU's card is always worth at least your DP stake, so a bet looks fair.
- Street Raw grades: 4 / 6 / 8 / 9 at 15 / 25 / 40 / 20% [P] (ALLEY-ECONOMY Evidence 2).

**Binders** (per CPU, as ALLEY-ECONOMY measured):
- Each CPU has a binder of `BinderSize` [P] 12 **different cards from its own deck**, its splashed contraband
  weighted `ContrabandWeight` [P] 3×, each at a real printing (rarity weighted 10000 / `Pricing.Unit`, as the used
  market picks), graded and valued by `Pricing.V`, kept only if inside its tier's floor and cap (a card that can't
  fit at any of its printings is skipped).
- **Owners:** a generated regular (its seat and night: `regular:<table>.<seat>.<night>`), a headliner (`hl:<id>`,
  led by its `stakes:` in order), Orvel (`orvel`: slabs from the ever-Forbidden pool minus the exclusions), each
  authored Nyx's Table seat (`nyx:<seat>`).
- **Refill:** one stake back per `RefillEvery` [P] 3 alley duels (any alley duel, brackets excluded), up to 12.
  Lazy: on access, add `(AlleyDuels - RefilledAt) / 3`. ALLEY-ECONOMY Evidence 5: it only bites on camping one CPU.
- **The stake for a duel** is the binder's next card in its seeded order, fixed until that duel is played. The versus
  screen shows it with its value before you commit; walking away before the toss costs nothing and rerolls nothing.
- **An empty binder:** that CPU plays without stakes until it refills: nothing of yours is held either, and the panel
  says "Binder's empty: no stakes tonight."
- Deterministic: a stake comes from `Hash.Mix(salt, hash(owner), serial++)`.

**Your card at Orvel's Table:**
- You pick one copy at the versus screen. Eligible: not needed by a saved deck (`Collection.Count(code) - 1 >=
  NeededByDecks(code)`), not at the Grading Desk, not on the rail, worth at least `OrvelMinCard`. Raw or slab.
- Then Orvel's stake is the next slab in his binder's order worth 1-2× your card's value (`OrvelMatch`). None fits:
  "Nothing in my binder matches that." Pick another card, or leave (nothing is held).
- No eligible copy: "Orvel looks through your binder. 'Nothing I'd take.'" and no Duel button.

**The flow:**
1. **Versus:** your stake ("300 DP, back if you win"), the CPU's card (card, rarity, condition word or slab grade,
   value), your card and Orvel's at Orvel's Table, the binder count, the rules chip and "Leaving counts as a loss."
2. **Start (`Ante.Hold`):**
   - checks your DP and your deck's legality;
   - moves your DP stake from `Dp` into `AnteLive.Held` (not a ledger line: it isn't spent yet);
   - takes the binder's stake into `AnteLive.Theirs`;
   - at Orvel's, removes your copy from the collection into `AnteLive.Yours`;
   - **saves**, then tosses the coin. From here the stakes are held in the save.
3. **The duel**, with `TurnCap` attached.
4. **Settle (`Ante.Settle`):**

| Result | Your DP stake | The CPU's card | Your card (Orvel's) | Seat |
|---|---|---|---|---|
| You win | Back to `Dp` | Yours: `Source = Ante`, `Paid = 0`, its grade and seed kept. A 4th copy keeps your best three and pays 0 DP (as `PrizePacks.Give`: the `Added.Dp` is dropped) | Back | Beaten |
| You lose | Lost: `Spend(Ledger.AlleyStake, held)` | Back on top of its binder | On Moth's rail | Unchanged |
| Draw (turn 40, equal LP) | Back | Back on top | Back | Unchanged (played again) |
| You lose on the duel's first turn (the guard) | Back | Back on top | Back | Unchanged |
| You quit, or the save loads with `AnteLive` | Lost | Back on top | On the rail | Unchanged |

   - **The first-turn guard:** you lose while `Result.Turns == 1` (the CPU went first and won before your first
     turn). It applies at every alley table, not only headliners (decision O4). Baz says so once (`firstturn`).
   - **Quitting is a loss:** conceding, or leaving to the menu or the campaign screen mid-duel, settles at once; a save
     loaded with `AnteLive` (the app closed or crashed) settles at load with a notice: "You left Dumpster Row
     mid-duel. That counts as a loss." (`Ante.SettleAbandoned`). The guard doesn't apply to a quit. ALLEY-ECONOMY
     agrees: a quit, a crash or a reload is a loss.
5. **After any alley duel:** `AlleyDuels++`, binders refill lazily, the rail ticks (below).

- `Profile.Dp` shown anywhere during a held duel is the DP not held; the header shows "(300 held)" beside it.
- `Ledger.Net` stays exact: held DP leaves `Dp` without a line and either comes back or is spent as `alley.stake`.
  `LedgerCheck` reconciles `Dp + AnteLive.Held` against the ledger.

**Won cards sell for 0.**
- `Paid = 0`, so `SellDesk` pays nothing (min(V/4, 0)). Its refusal for `Source == Ante` reads "Won at ante: Moth
  doesn't buy back his own." (F9).
- They play, grade, crack and trade like any copy. Grading one adds the fee to `Paid` as today, so it can return at
  most half the fee.

**Moth's rail (your lost card at Orvel's):**
- `RailEntry { Copy, Price = Round5(RailMarkup [P] 1.5 × V(copy)), ExpiresAt = DuelsPlayed + RailDuels [P] 5 }`.
  The price is set at the loss. Any duel counts toward the 5, as the Grading Desk counts.
- Shown in the van's Used & graded tab, top row: "Your Pot of Greed · Grade 9 slab · 4,500 DP · 3 duels left".
- **Buy back:** `Spend(Ledger.AlleyRail, price)`; the same copy comes back (same id, grade, seed, slab);
  `Paid += price`.
- **Expired:** gone for good (a card sink). A notice next time you open the campaign screen: "Moth sold your Pot of
  Greed." (`railgone` the first time).

## 10. Moth's van: Under the Counter

**A new screen, `VanScreen.cs`**, opened from the van node. Theme-native (built on `UiTheme.Current`, which is
`BackAlley` there). Layout as the shop's (list left, detail right, header plate "UNDER THE COUNTER" in `Sign`, the van's
sign in `NeonCyan`). Moth's portrait slot on the left of the header (silhouette until a portrait exists).

**Tabs: Shelf | Used & graded | Sell.** The plan's "Packs" tab becomes the shelf: Contraband Packs aren't sold
(ALLEY-ECONOMY: a pack for sale is a banned-card store in pack form). Won Contraband Packs open at the bracket.

**Shelf** (the contraband shelf):
- `ShelfSize` [P] 6 slabs at a time, grade 9 or 10, of contraband printings at Secret Rare and up worth at least
  `ShelfMinValue` [P] 3,500 at `Pricing.V`.
- Priced at `ShelfMarkup` [P] 1.5 × V, rounded to 500: 5,500 (a Platinum or Prismatic Secret 10) to 22,500 (a
  Starlight 10). 429 possible items from 147 cards (ALLEY-ECONOMY Evidence 7).
- New every `ShelfEvery` [P] 10 alley duels, from the save's salt and the epoch, so reloading never rerolls.
- Odile's line on each ("Authenticated by the Loupe").

**Used & graded:**
- **Moth's rail** first: your lost cards (section 9).
- **Used contraband:** the used market's own code (`UsedMarket.Listings`' rules) over the Contraband Pack pool: Raw at
  the used odds and slabs up to 8 [P], priced at exactly V, `VanUsedSize` [P] 18 listings, its own salt (`0x0A11`),
  an epoch of 5 duels. No wishlist slots, no special orders (Gus's desk keeps those).
- Buying either: `Spend(Ledger.AlleyShelf | Ledger.AlleyUsed, price)`, `Paid = price`, the 4th-copy rule through
  `Collection.Take` (a displaced copy's `SellPrice` goes to `refund.alley`, a key this spec adds; ALLEY-ECONOMY lists
  none for it).

**Sell:** the Sell desk exactly (`SellDesk.Sell`, ledger `sell`): Moth pays the desk's price, `min(V/4, Paid/2)`; won,
prize and traded copies 0. F9's refusal wording. Moth's lines on top (`van:sell`).

**The Contraband Pack** (bracket prizes only; `ContrabandPacks`):
- The pool: every card Forbidden on some TCG list (vendor/LFLists and lflists/history.lflist.conf), minus all 33
  exclusions, with at least one printing in printings.bin: 231 cards (ALLEY-ECONOMY Evidence 8). Era: none.
- Slots 1-4: Raw cards, uniform from the pool, at a printing weighted 10000 / `Pricing.Unit`, graded at the foil
  odds (`CopySource.Prize`).
- Slot 5: a **sealed slab** of a card at Super Rare or better, grade 9 at `PackSlabNine` [P] 75%, else 10, as a Prize
  Pack's slot 5.
- No card twice in a pack. Every copy Paid 0 (sells for 0). Its odds block is shown as the Prize Pack's is (E5).

**Voice:** `campaign/van.txt` in `shop.txt`'s format (section 12). Moth never says "stolen".

## 11. Alley brackets

**Reuse `Brackets` and `BracketScreen`**, generalized (F6).

**Engine changes (`Brackets.cs`):**
- `Bracket.Size` becomes an instance field (8 or 16), with `MatchCount = Size - 1`, `Final = Size - 2`,
  `Rounds = log2(Size)`. The static `Bracket.Size`/`MatchCount`/`Final` constants become `Bracket.LocalsSize = 8`
  etc., and the old static helpers stay as size-8 wrappers so Locals code compiles unchanged.
- Instance helpers: `b.RoundOf(m)`, `b.MatchesIn(round)`, `b.RoundName(round)` ("Round of 16", "Quarter-final",
  "Semi-final", "Final").
- **Bracket order for 16** (seeds by slot): 1, 16, 8, 9, 4, 13, 5, 12, 2, 15, 7, 10, 3, 14, 6, 11.
- `Pair(m)`: round 0 is slots (2m, 2m+1); a later match pairs the winners of the two matches feeding it.
- `Placement()`: 0 winner, 1 finalist, 2 semi-finals, 3 quarter-finals, 4 round of 16.
- **Bo3 final** (`b.FinalBestOf = 3`): `b.FinalGames = (a, b)` wins. Your final records one game per duel; the match
  is decided at 2. A draw replays the game. Game 1 has a coin toss; later games, the previous game's loser chooses.
  A CPU-vs-CPU Bo3 final is one coin against the series odds `p²(3 − 2p)`. Watch the final plays games until one
  side has 2; each game's result counts.
- **Generated seats:** a `BracketEntrant` can be a splash regular: `Character = "alley-regular"`, `Deck =
  "alley:bracket.<slot>"`, with its base, adds, cuts, handle and tint saved (`bracketsplash` line). The deck is rebuilt
  from them as at a table.
- `Kind`: `locals` | `brawl` | `midnight`. One bracket runs at a time, of any kind (as today).

**The two brackets** (alley-events.txt):

```
bracket: Alley Brawl
kind: brawl
size: 8
opens: Dumpster Row
entry: 1000
pay: 900 650 400 150
packs: 2 1 0 0
field: you 1
field: splash 7 cleared

bracket: Midnight Open
kind: midnight
size: 16
opens: The Black Market
entry: 2500
final: bo3
pay: 1500 1000 600 300 100
packs: 4 2 1 0 0
tokens: 1 first
field: radiant-typhoon radiant-typhoon 1
field: you 2
field: mitsurugi mitsurugi
field: angelechy angelechy               # C review: was k9-vanquish-soul (retuned, see As built C1)
field: branded-despia branded-despia
field: nocturne hat
field: six-samurai six-samurai
field: ryzeal ryzeal                     # C review: was dragon-ruler
field: thunder-dragon thunder-dragon
field: eldlich eldlich
field: kashtira kashtira
field: splash 5 The Black Market
```

- **Alley Brawl:** you as seed 1, and 7 regulars drawn from the bracket's seed, seeded 2-8 by Elo (base +
  `splash-elo`).
  - **The field's table** (`splash 7 cleared`; decision O6): Dumpster Row's regulars until The Basement Game is
    cleared, then The Basement Game's. Never The Black Market's: ALLEY-ECONOMY Evidence 9 puts a typical player's title odds at 7%
    there (target 30-45%). Black Market regulars play in the Midnight Open instead.
  - Entry `BrawlEntry` [P] 1,000. Pay `BrawlPay` [P] 900 / 650 / 400 / 150 (the Locals values). Packs `BrawlPacks`
    [P] 2 to the winner, 1 to the finalist (the plan said 1 to the winner).
- **Midnight Open:** Ilara Gale, the reigning champion, is seed 1; you are seed 2, so you can meet her only in the final
  (decision O5). Five circuit characters, five Library bosses (never Clausolas, Pepe, Helix, Ilse or Ines: Ines never comes down, ALLEY-STORY), five Black
  Market regulars; seeds 3-16 by Elo.
  - The field above is a [P] first pick. `--bracket-sim` must put a champion's best deck at 15-30% for the title
    before it ships (Phase 0 measured 6% at 1,800 and 22% at 1,947 with the planned field).
  - Entry `OpenEntry` [P] 2,500. Pay `OpenPay` [P] 1,500 / 1,000 / 600 / 300 / 100. Packs `OpenPacks` [P] 4 / 2 / 1
    (winner, finalist, semi-finalists; the plan said 4 to the winner only), and `OpenTokens` [P] 1 Card Token on the
    first title only (seen key `midnight:token`).
  - Neither bracket is DP-positive even on a title (900 for 1,000; 1,500 for 2,500).
- **Your deck:** the Alley list each round (`Alley.Rules`), no loaners. The duel has `TurnCap`.
- **No ante** in brackets: entry fee and placement only. Entries are `Spend(Ledger.AlleyBrackets, entry)`; placement
  pay is `Earn(Ledger.Alley, dp)`.
- **Duels:** no DP of their own; EXP as Locals; level-ups pay tokens. They don't count toward binder refills.
- **CPU matches:** Elo odds with the `Matchups` table where it has a pair; splash seats use their base's pairs.
- **Prizes:** `Brackets.Finish` opens the Contraband Packs (Paid 0) through `ContrabandPacks.Give`; the reveal is
  `ShopScreen.OpenPrizes`.
- **Records:** a new `profile.AlleyBrackets` (kind → `LocalsRecord`; save line `alleybracket <kind> <entries>
  <titles> <best>`), kept apart from `profile.Locals`, so `Brackets.TitlesTotal` still counts Locals titles only.
- **Plain `bracketwin` is for Locals only** (ALLEY-STORY conflict 2: the `first-bracket-title` scene is Gus's
  corkboard and Hollis's Prize Packs). `Finish` raises `bracketwin` only for a first Locals title; an alley title
  raises only `bracketwin:Alley Brawl` or `bracketwin:Midnight Open`.
- **Story:** `brawl` and `midnight` (first entry), `bracketwin:Alley Brawl`, `bracketwin:Midnight Open`. Baz MCs and
  Mika streams (voice hooks, section 12).

**The screen (`BracketScreen`):**
- 8: today's layout, alley theme.
- 16: the plan's mirrored layout with the final in the center, 240×72 slots (1920×1080 reference), round-of-16
  columns at the edges.
- The Bo3 final shows game pips under the final's slots ("● ○").
- The entry dialog: field, entry, pay, prize odds (`ContrabandPacks.Odds`), the Alley list line.

## 12. The Unlisted binder, cast, story hooks and data formats

**The Unlisted binder** (never "the 13th": the Every Era epilogue may put another spine on the shelf).
- After the 12 cups on the Library shelf, a binder "Unlisted" (no ✓ or ★, its own plate). It appears with the first
  page.
- **A page** is added when you beat a contraband deck for the first time:
  - a regular: one page per (table, base), the first one you beat;
  - a headliner: one page each, with **Watch the combo** (Headliners phase 3);
  - an authored deck: one page each.
- A page: handle and silhouette, table, the deck list with the contraband marked, "Open as a new deck" (the editor's
  Build and Save as new, as for recipes; you build it from what you own).
- Story: `unlisted` (the first page; Hollis upstairs, amused).
- `Unlisted` pages never count toward cup clears, stamps or the final.

**Cast and where they appear:**

| Who | Id | Where | Needs |
|---|---|---|---|
| Kit | `kit` | The `alley` rumor scene, `alleyvisit` | exists |
| Moth | `moth` (new) | The van's face and voice, the rail notices, `fence`, `rail`, `buyback`, `railgone` | profile; portrait optional |
| Baz "the Bell" Calloway | `baz` (new) | Table panels' and versus tells, brackets' MC, headliner calls, the turn-40 and first-turn rules, the exhibition | profile; portrait optional |
| Odile Varga, "the Loupe" | `odile` (new) | The contraband shelf's authentication, Orvel's slab stakes | profile; portrait optional |
| Penny | `penny` (new) | A Dumpster Row cameo (below), scenes | profile; portrait optional |
| Patron Orvel | `power-patron` | Hosts and plays Orvel's Table | exists |
| Ilara Gale | `radiant-typhoon` | Midnight Open seed 1, its scenes | exists |
| Mika | `mika` | Streams the brackets with Baz (voice), headliner reactions | exists |
| Nyx | `nyx` | Hosts Nyx's Table, seat 4 | exists; a new deck |
| Nocturne | `nocturne` | The oldest regular: scenes, Midnight Open entrant | exists |
| Gus, Vera, Hollis, Ines | existing | Scenes only (Hollis never comes down; Ilse never appears) | exist |
| Headliners | their ids | Their seats, their `combo` / `fizzle` lines | profiles (section 8) |

- **Penny's cameo (low priority, Phase B):** on a Dumpster Row night, seat 1 is Penny with chance `PennyChance` [P]
  1/5 (seeded, saved): warriors as built plus a full splash, skill 0.7, and her binder holds one card, the same
  Common every night (a Kuriboh). It's worth less than `StreetStake`, so her seat is the one exception to
  `StakeFloor`: nothing of yours is held ("Penny plays for the Common."). Beating her wins it (Paid 0) and raises `pennywin` the first time. Her binder then holds a fresh Kuriboh every night ("Night one of a
  new streak."). Her card is Kuriboh (ALLEY-STORY; the owner confirms).
- **Ines's card: `heldcard`** (ALLEY-STORY section 3; replaces this spec's earlier optional `inescard`):
  - Ines's 2005 Scapegoat (Raw 6, Excellent) hangs at the end of Moth's rail from the first van visit: one static row
    in Used & graded, "HELD · not for sale", face down, not selectable, no price. DP can't free it.
  - **The composite trigger:** `heldcard` fires once both hold, whichever comes second: Orvel's Table is cleared
    (`BackAlley.IsCleared`) and the `cup-goat-clear` scene has been seen (`profile.Seen`). New:
    `BackAlley.HeldCardDue(profile, data)` (both hold and the `held-card` scene isn't seen). Callers check it after
    an alley result, after a Library result's scenes play, and when the campaign screen opens (catch-up), and raise
    `heldcard`. Its scenes are `held-card` then `held-card-home`.
  - **The card never enters your collection.** It goes to Ines; there's no copy, no ledger line and no arbitrage
    surface. The HELD row shows until `held-card-home` is seen, then the van plays `van:held-gone` / `van:held-paid`
    once.
  - No save field: `profile.Seen` holds it all. The HELD poster unlocks with `held-card-home` (section 13).

**Story triggers** (story.txt `when:` values; all shown once):

| Trigger | When |
|---|---|
| `alley` | The alley opens (after the first cup clear's scenes) |
| `alleyvisit` | The Back Alley tab is first shown |
| `open:<table name>` | A table's panel first opens (Dumpster Row, The Basement Game, The Black Market, Orvel's Table, Nyx's Table) |
| `alleyclear:<table name>` | A table's first clear |
| `headliner:<id>` | Before your first duel against a headliner (before the versus) |
| `headlinerbeat:<id>` | Your first win against a headliner |
| `combobreaker:<id>` | A headliner's Combo Breaker poster (Headliners phase 3) |
| `encore` | Your first encore |
| `fence` | The van's first visit |
| `antewin` | Your first stake won |
| `antelose` | Your first alley loss with a stake on the table |
| `rail` | Your first card on Moth's rail |
| `buyback` | Your first buy-back |
| `railgone` | The first time Moth sells a card of yours off the rail |
| `turncap` | Your first duel decided at turn 40 |
| `firstturn` | The first time the first-turn guard gives your stake back |
| `unlisted` | The first Unlisted page |
| `brawl`, `midnight` | Your first entry to each |
| `bracketwin:Alley Brawl`, `bracketwin:Midnight Open` | Your first title in each (plain `bracketwin` stays Locals-only) |
| `pennywin` | Your first win against Penny |
| `heldcard` | Composite: Orvel's Table cleared and `cup-goat-clear` seen, whichever comes second (above) |

- Placeholders: `{card}` (the stake or lost card), `{table}`, `{handle}`, plus the existing ones; from ALLEY-STORY:
  `{grade}`, `{nick}` (Baz's chalk name for you: Upstairs → Curbside → Downstairs → Boots → Midnight, one step per
  first clear of Dumpster Row, The Basement Game, The Black Market and the Midnight Open title; computed, not saved),
  `{price}` (a rail tag), `{left}` (rail duels left) and `{step}` (where a combo fizzled).
- Speaker token `opponent` (ALLEY-STORY): the CPU you just played, resolved like `partner`.
- `--campaign-test` flags any speaker without a profile and any trigger the code raises with no scene (pending until
  the story doc lands).

**Voice pools** (lines picked by hook, not scenes; `ShopVoice`'s format, file `campaign/alley-voice.txt`, and
`campaign/van.txt` for Moth):
- `regular:intro`, `regular:win`, `regular:lose`, each optionally `:<table id>` (specific over general). The regular
  speaks them; `{handle}` and `{card}` fill. ALLEY-STORY adds `regular:intro:card:<card name>` (a taunt for the card
  that made the handle; most specific wins).
- `tell:<headliner id>`: Baz's warning on the versus ("Moss runs a Mass Driver. Bring something for the Graveyard.").
- `baz:stake`, `baz:empty` (an empty binder), `baz:turncap`, `baz:firstturn`, `baz:orvel`; ALLEY-STORY adds
  `baz:contraband`, `baz:antewin`, `baz:antelose`, `baz:rail` and `orvel:pays`.
- `mc:<kind>:<round>` and `stream:<kind>:<round>` for brackets (Baz, Mika).
- Van: `van:hello` (first), `van:footer`, `van:rail`, `van:buyback`, `van:shelf`, `van:pack` (a won pack's reveal),
  `van:sell`, `van:empty`; ALLEY-STORY adds `van:used`, `van:sell-refused`, `van:held`, `van:held-gone`,
  `van:held-paid`.
- The hook list each file checks against is the union of this section's and ALLEY-STORY's.
- `ShopVoice.Problems` gets an optional `knownHooks` parameter so each file checks against its own hooks.

## 13. The alley's look: theme, mat, sound, assets

**Which screens are in the alley theme** (whole screens only, UI-THEME rule 9):

| Screen | Theme |
|---|---|
| Campaign screen with the Back Alley tab shown (map, table panels, Baz's chalkboard) | BackAlley |
| Versus, coin toss, results for an alley duel or alley bracket | BackAlley |
| `BracketScreen` for Alley Brawl and the Midnight Open | BackAlley |
| `VanScreen` | BackAlley (theme-native) |
| The deck editor's frame, opened from the alley | BackAlley |
| Gus's shop, the Card Trader, the Grading Desk, the Recipe Book, the Library, opened from anywhere | MainStreet (they're on main street: opening one from the alley switches to MainStreet for that screen and back on close) |
| The duel board | Gameplay colors unchanged; only the mat and the top bar change |

- `-themecheck` adds the new screens in both themes: the alley panel, the van's three tabs, an alley versus, a 16
  bracket.
- Neon (`Sign`, `NeonCyan`, `Divider`) on signs and rules only, never on a button, a row or the board.

**Materials (procedural, no images):**
- Brick (running bond, mortar lines, a wet sheen gradient), chain-link (a diamond lattice at 20% alpha), tape (two
  torn strips with a slight rotation). Built in code like `BoardFx.Textures`, cached.
- Used by the alley map's backdrop, the table panel's header and the van's frame.

**Type:** one stencil font for titles only (the map's node names, panel titles, the van's plate). Everything else
keeps `LegacyRuntime.ttf`. Owner supplies the font (below); until then, titles use the default font in bold with
+2 letter spacing.

**The mat and the top bar:**
- New Playmat motif `brick` (and the existing `chains`). Mats in campaign/mats.txt:

```
# The Back Alley's tables (ALLEY-SPEC)
alley-dumpster-row: #0D0B0A #2A211C #FF4FA3 brick
alley-basement: #0A0B0D #1E2630 #3FE0D0 chains
alley-black-market: #0E0A0D #2E1A26 #FF4FA3 chains
```

- Orvel's and Nyx's tables use their character mats (`power-patron`, `nyx`) if mats.txt has them, else the table's.
- The duel board's top bar reads `UiTheme.Current` panel and text roles in alley duels (and only there).

**Sound (procedural, `Sfx`):**
- `alley` duel theme (the everyday alley music), `orvel` and `nyx` boss themes in `Sfx.Themes`. `BossMusic.ForAlley
  (table, seat)`: seat 4 and Orvel/Nyx get their theme, the rest `alley`.
- **The rain bed:** a loop of filtered noise (rain), a 40-55 Hz sub-bass drone and vinyl-style crackle, at
  `MusicVolume` × 0.35 [P], while the Back Alley tab or the van is shown. Fades with the 300 ms crossfade.
- The headliner sting is the Headliners doc's.
- `--sound-export` writes the new themes and the bed for listening.

**Assets the owner must supply:**
1. **A stencil font** (TTF, OFL-licensed; e.g. Allerta Stencil or Stardos Stencil from Google Fonts) at
   `unity/Assets/Fonts/`. Needed for Phase B's polish; nothing blocks without it.
2. **Optional portraits** for `moth`, `baz`, `odile`, `penny` (the portrait contract, art/portraits/<id>.png). Until
   they exist, the hooded silhouette stands in.
3. **Optional: one hooded-silhouette PNG** (600×800, transparent, white figure) at `art/portraits/alley-hood.png`,
   tinted per duelist. Without it, the silhouette is drawn in code (a hood, shoulders and a shadowed face, as
   `Icons` draws shapes).
- Nothing else: posters use card art, mats and sounds are procedural.

**Posters** (new entries in `Posters.All`, card art from cards a stock install has pictures of; the Narrative
Designer may change titles and taglines):

| Id | Title | Card | Earned |
|---|---|---|---|
| `no-list` | NO LIST | Pot of Greed | `alleyvisit` seen |
| `lost-card` | LOST CARD | Change of Heart | a card of yours went on the rail once |
| `orvels-marker` | Orvel's Marker | Patron card of Orvel's deck (the Narrative picks) | Orvel's Table won once |
| `held` | HELD | Scapegoat | the `held-card-home` scene seen (ALLEY-STORY) |
| `brawl-bill` | Alley Brawl | Raigeki | Alley Brawl title |
| `midnight-bill` | Midnight Open | Dark Hole | Midnight Open title |
| `combo-breaker:<id>` | Combo Breaker | the headliner's first `pieces:` card | Headliners phase 3 |

## 14. Save, New Game+ and the ledger

**New `Profile` fields, one line each** (written in `Profile.Save`, read in `Load`, all kept by `NewGamePlus()`):

| Field | Save line | Default for old saves |
|---|---|---|
| `bool AlleyOpen` | `alley 1` | false; opened at load if a cup is cleared |
| `int AlleyDuels` | `alleyduels N` | 0 |
| `Dictionary<string, AlleyTableState> AlleyTables` (night, seats won tonight, first clear) | `alleytable <id> <night> <wonMask> <cleared 0/1>` | none |
| `Dictionary<string, LocalsRecord> AlleyBrackets` (Alley Brawl, Midnight Open) | `alleybracket <kind> <entries> <titles> <best>` | none |
| `List<AlleyOpponent>` (the current night's seats, and an encore) | `alleyopp <table> <seat> <night> <kind> <character> <base> <tint> <added csv\|-> <cut csv\|-> <handle...>` | none (drawn when the table opens) |
| `Dictionary<string, HeadlinerState>` (met, beaten, encores, combo breaker) | `headliner <id> <met> <beaten> <encores> <breaker>` | none |
| `Dictionary<string, StakeBinder>` | `binder <owner> <serial> <refilledAt>` then `stake <owner> <i> <code> <rarity> <grade> <slab> <seed> <flag\|->` | none (made on first access) |
| `AnteLive?` | `antelive <table> <seat> <held DP> <startedAt>`, `antetheirs <stake fields>`, `anteyours <copy fields>` | none |
| `List<RailEntry>` | `rail <price> <expiresAt> <copy fields>` | none |
| `List<UnlistedPage>` | `unlisted <key> <table> <kind> <base> <added csv> <cut csv> <handle...>` | none |
| Van epochs and sold slots | `vanused <epoch> <sold csv>`, `vanshelf <epoch> <sold csv> <sold items>` (the third field added 2026-11-02: the shelf's slabs bought, `code.rarity.grade`; older saves have none) | none |
| `int AnteWins, AnteLosses` | `ante <wins> <losses>` | 0 |

- `<copy fields>` is the `copy` line's field list (code rarity grade seed id source paid slab floor submissions
  readyAt), so a held or railed copy round-trips exactly.
- **Bracket lines:** new `bracketsize <n>` (absent: 8), `bracketgames <a> <b>` (a Bo3 final), and `bracketsplash
  <slot> <base> <tint> <added csv> <cut csv> <handle...>`. Old saves read as before.
- **At load:** `AnteLive` set → settle as a loss (section 9). A rail entry past `ExpiresAt` → removed with a notice.

**New Game+:**
- The alley is post-game and untouched: open tables, nights, binders, the rail, Unlisted, headliner records, van
  epochs all stay.
- Nothing re-pays: a table's first-clear prize is keyed by `alleytable … cleared 1`; the Midnight Open's token by
  `midnight:token`; posters by their conditions.
- New Game+ can't start mid-duel, so `AnteLive` is never set across it.

**The ledger** (ALLEY-ECONOMY's keys as new `Ledger` constants; `LedgerCheck` lists them; `Ledger.Net` must
reconcile):

| Kind | Key | Constant | What |
|---|---|---|---|
| Sink | `alley.stake` | `Ledger.AlleyStake` | A held DP stake lost (a loss or a quit). A stake that comes back writes no line |
| Sink | `alley.rail` | `Ledger.AlleyRail` | Moth's rail buy-backs |
| Sink | `alley.shelf` | `Ledger.AlleyShelf` | The contraband shelf |
| Sink | `alley.used` | `Ledger.AlleyUsed` | Moth's used contraband |
| Sink | `alley.brackets` | `Ledger.AlleyBrackets` | Alley Brawl and Midnight Open entries |
| Source | `alley` | `Ledger.Alley` | Alley bracket placement pay |
| Source | `refund.alley` | `Ledger.AlleyRefund` | A copy displaced by the 4th-copy rule when you buy at the van (this spec's addition; won and prize copies pay 0 instead) |
| Source | `sell` | `Ledger.Sell` | The van's Sell tab (existing) |

- Held DP: `LedgerCheck` reconciles `Dp + (AnteLive?.Held ?? 0)` against the ledger while a duel is held.
- `Leveling.AddExp(..., payDp: false)` for every alley and alley-bracket duel, so level-ups never pay DP there.

## 15. Knobs

One class, **`AlleyEconomy`** (new, `src/Ygo.Engine/Campaign/AlleyEconomy.cs`, like `ShopPrices`): every alley
number lives there as `public static` fields with these defaults, so the Economy Designer tunes one file and the
harness can swap values. Names are ALLEY-ECONOMY's where it has one. [P] = recommended default, pending the owner.

| Knob | [P] value | Source |
|---|---|---|
| `StreetStake`, `DenStake` (Den covers The Basement Game, The Black Market, Nyx's Table) | 300, 1,000 DP, held | ALLEY-ECONOMY |
| `OrvelStake`, `OrvelMinCard` | 3,000 DP held; your spare card worth 1,000+ | ALLEY-ECONOMY |
| `StreetCap`, `DenCap` | 1,000, 5,000 | plan |
| `StakeFloor` | 1.0 (the CPU's card is worth at least your DP stake) | ALLEY-ECONOMY |
| `OrvelMatch` (min, max) | 1.0, 2.0 (his slab 9-10 is worth 1-2× your card) | ALLEY-ECONOMY |
| `StreetGrades` | Raw 4 / 6 / 8 / 9 at 15 / 25 / 40 / 20% | ALLEY-ECONOMY |
| `DenGrades` | Raw 8-9 and slabs 8-10 (the probe's mix) | ALLEY-ECONOMY |
| `ContrabandWeight` | 3× (a binder draws its splashed contraband 3× as often) | ALLEY-ECONOMY |
| `BinderSize`, `RefillEvery` | 12, 1 per 3 alley duels | plan |
| `RailDuels`, `RailMarkup` | 5, 1.5× (added to the copy's Paid) | owner |
| `DuelDp` | 0 (alley duels pay no DP) | ALLEY-ECONOMY |
| `ClearTokens` | 1 Card Token on a table's first clear | this spec (the Economy Designer to confirm) |
| `ShelfSize`, `ShelfEvery`, `ShelfMinValue`, `ShelfMarkup` | 6, 10 alley duels, 3,500, 1.5× rounded to 500 | ALLEY-ECONOMY (size and refresh suggested, not measured) |
| `VanUsedSize`, `VanUsedSlabMax` | 18, slabs up to 8 | this spec; ALLEY-ECONOMY |
| `PackSlabNine` | 75% (the pack's slab is 9, else 10) | ALLEY-ECONOMY |
| `BrawlEntry`, `BrawlPay`, `BrawlPacks` | 1,000; 900 / 650 / 400 / 150; 2 / 1 / 0 / 0 | ALLEY-ECONOMY |
| `OpenEntry`, `OpenPay`, `OpenPacks`, `OpenTokens` | 2,500; 1,500 / 1,000 / 600 / 300 / 100; 4 / 2 / 1 / 0 / 0; 1 on the first title | ALLEY-ECONOMY |
| `WildcardChance`, `PennyChance` | 1/3, 1/5 | Headliners; this spec |
| `AlleyTurns`, `TurnCounterFrom` | 40, 30 | owner; this spec |

- `ContrabandPacks.Size` (5) and `TurnCap.AlleyTurns` read from `AlleyEconomy`.
- **Hard rules no knob may break** (the plan's checks, and ALLEY-ECONOMY's "Rules the build must keep"): DP returned
  for a copy is at most half of what went into it; one price everywhere (Moth buys at the Sell desk's price); stakes
  are held in the save at the duel's start; the CPU's card and its value are shown before you commit, and the pack's
  odds are shown; alley duels pay at most 0 DP all-in and alley brackets at most 300 a duel; no loop is DP-positive;
  New Game+ re-pays none of it.

## 16. Build split

Four phases, about 3.5 sessions. Within a phase, **chunk 1 (engine and data) lands its API first** (the signatures
below are binding), then chunks 2 (Unity) and 3 (harness) run in parallel against it. Files are disjoint within a
phase. Unity loads the engine as a local package: new `.cs` files under src/Ygo.Engine need `.meta` files, and new
campaign/*.txt and decks/**/*.ydk reach the app through `ProjectSetup`'s copy.

### Coordination before anything starts

- **Read the parallel docs:** docs/dev/ALLEY-ECONOMY-2026-11-01.md (landed; its numbers are section 15's defaults; if
  the owner changes one, change only `AlleyEconomy`) and docs/dev/ALLEY-STORY-2026-11-01.md (landed: its scenes go in
  story.txt after the Every Era block and before the level scenes, its pools in alley-voice.txt and van.txt, Gus's
  footers in shop.txt, plus its profiles, handles and names) (scenes into story.txt, pools into
  alley-voice.txt and van.txt, profiles, handles and names). Until then, chunks ship with placeholder scenes marked
  `# PENDING ALLEY-STORY` and the check reports them.
- **Tell the Economy Designer:** this spec adds `refund.alley` (van purchases displacing a 4th copy) and
  `ClearTokens` (1 token a table's first clear), and per-CPU binders keyed per regular's night; `--postgame-sim`
  needs an alley pilot (Phase D); `--alley-probe` should keep running on its model until A3 moves its loops onto the
  real code.
- **Tell the Headliners author:** seat 4 and the wildcard read `ready:`; their Phases 2-3 land in Phase D here.
- **Owner decisions** (section 17) with defaults: build the recommendation unless the owner says otherwise.

### Phase A: the loop (about 1 session)

Unlock, the tab, the three regular tables with regulars and the two ready headliners, legality, the turn cap, Street
and Den ante with binders, the first-turn guard, quitting as a loss, save and ledger. No van, no Orvel, no Nyx, no
brackets, no Unlisted.

**Chunk A1: engine and data.** Files:
- `src/Ygo.Engine/Campaign/Alley.cs`: `Never`, `BossOnly`, `DeckPrefix`, `Rules`, `IsAlleyDeck`, the recipe note in
  `Splash`, the sub-seed retry.
- New `src/Ygo.Engine/Campaign/BackAlley.cs`: `AlleyData` (alley-events.txt), `AlleyContext`, `AlleyOpponent`,
  `BackAlley`.
- New `src/Ygo.Engine/Campaign/Ante.cs`: `Stake`, `StakeBinder`, `AnteLive`, `RailEntry`, `AnteSettlement`, `Ante`.
- New `src/Ygo.Engine/Campaign/AlleyEconomy.cs`: every knob of section 15 (all phases' knobs now, so B and C add none).
- `src/Ygo.Engine/HouseRules.cs` (`TurnCap`), `src/Ygo.Engine/DuelRunner.cs` (`ReasonTurnCap`, `EndInDraw`),
  `src/Ygo.Engine/Presentation/DuelNarrator.cs` (the turn-40 line).
- `src/Ygo.Engine/Campaign/Profile.cs`: the fields and save lines of section 14 (all of them now, so later phases add
  no save code), `CopySource.Ante`, its grade odds (= Used), `Ledger` constants.
- `src/Ygo.Engine/Campaign/GradeEconomy.cs`: the Ante refusal at the Sell desk.
- `src/Ygo.Engine/Campaign/Story.cs` (the trigger list in the doc comment), `ShopVoice.cs` (`knownHooks`).
- Data: `campaign/alley-events.txt` (section 6 and 8; headliners other than the two ready ones with `ready: no`;
  brackets present but unused until Phase C), `campaign/alley-voice.txt` (pending pools), `campaign/story.txt` (the
  Phase A scenes: `alley`, `alleyvisit`, `open:`, `alleyclear:`, `headliner:`, `headlinerbeat:`, `antewin`,
  `antelose`, `turncap`, `firstturn`), `personalities/baz.profile`, `moth.profile`, `odile.profile`,
  `penny.profile`, `yata-mags.profile`, `mass-driver-moss.profile`.

**API (binding for A2 and A3):**

```csharp
// Alley.cs (additions)
public static readonly string[] Never, BossOnly;                // subsets of Excluded (section 3)
public const string DeckPrefix = "alley:";
public static bool IsAlleyDeck(string deckName);                 // starts with DeckPrefix
public static FormatRules Rules(Banlist alley);                  // { List = alley, Pool = "", Passes = 0, Forced = true }

// BackAlley.cs
public enum AlleyTier { Street, Den, Orvel, Nyx }
public sealed class AlleySeatSpec { public int Seat; public string Kind = "", Character = "", Deck = "", Event = ""; public double Skill; public HashSet<uint> Allow = new HashSet<uint>(); public string Else = ""; }
public sealed class AlleyTable
{
    public string Id = "", Name = "", Opens = "", Mat = "", Music = "", Closer = "";
    public AlleyTier Tier; public AlleyEvent? Event; public double SplashElo;
    public List<string> Bases = new List<string>(), Headliners = new List<string>();
    public List<AlleySeatSpec> Seats = new List<AlleySeatSpec>();   // authored seats (orvel, nyx)
    public int SeatCount => Seats.Count > 0 ? Seats.Count : BackAlley.Matches;
}
public sealed class Headliner
{
    public string Id = "", Handle = "", Deck = "", Table = "", List = "";
    public List<uint> Pieces = new List<uint>(), Stakes = new List<uint>(); public HashSet<uint> Over = new HashSet<uint>(); public bool Ready;
}
public sealed class AlleyBracketSpec { public string Name = "", Kind = "", Opens = ""; public int Size, Entry, FinalBestOf = 1, Tokens; public int[] Pay = new int[0], Packs = new int[0]; public List<string> Field = new List<string>(); }
public sealed class AlleyData
{
    public const string FileName = "alley-events.txt";
    public IReadOnlyList<AlleyTable> Tables { get; }
    public IReadOnlyList<Headliner> Headliners { get; }
    public IReadOnlyList<AlleyBracketSpec> Brackets { get; }
    public IReadOnlyList<(uint card, string prefix)> Handles { get; }
    public IReadOnlyList<string> Names { get; }
    public static AlleyData Load(string path, ICardDataProvider cards);   // a missing file: no tables
    public static AlleyData Parse(string text, ICardDataProvider cards);
    public int IndexOf(string tableName);
    public Headliner? FindHeadliner(string id);
    public List<string> Problems(AlleyContext ctx);   // names, bases (in range, FromEra, not skipped), headliner decks under list+over, allow ⊆ BossOnly
}
public sealed class AlleyContext
{
    public ICardDataProvider Cards = null!; public PrintingIndex Printings = null!;
    public IReadOnlyList<Banlist> Lists = null!; public Banlist List = null!;           // every list; the Alley list
    public Func<string, Deck?> LoadDeck = null!; public IReadOnlyDictionary<string, double> Ratings = null!;
    public IReadOnlyDictionary<string, Curation> Curated = null!; public IReadOnlyList<Personality> Roster = null!;
    public Library Library = null!;
}
public sealed class AlleyOpponent
{
    public string Table = "", Kind = "", Character = "", Handle = "", Base = "";   // Kind: regular | headliner | wildcard | authored | encore
    public int Seat, Night; public uint Tint; public double Skill, Elo;
    public List<uint> Added = new List<uint>(), Cut = new List<uint>();
    public string DeckKey => $"{Alley.DeckPrefix}{Table}.{Seat}";
    public string Binder = "";                                                       // the stake binder's owner key
}
public sealed class AlleyResult
{
    public DuelReward Reward = new DuelReward(); public AnteSettlement? Ante;
    public bool SeatWon, NightOver, FirstClear, Replay; public string? Opened;      // Opened: a table that opened now
    public List<string> Events = new List<string>(); public string Summary = "";
}
public static class BackAlley
{
    public const int Matches = 4; public const string OpenEvent = "alley", RegularId = "alley-regular";
    public static bool Open(Profile profile, Library library);
    public static bool IsUnlocked(Profile profile, AlleyData data, int table);
    public static bool IsCleared(Profile profile, AlleyData data, int table);       // first clear done
    public static IReadOnlyList<AlleyOpponent> Lineup(Profile profile, AlleyData data, int table, AlleyContext ctx);  // draws the night if none; save after
    public static int NextSeat(Profile profile, AlleyData data, int table);         // -1: none (locked or a night just ended)
    public static AlleyOpponent? Encore(Profile profile, AlleyData data, int table, string headliner, AlleyContext ctx);
    public static Deck DeckOf(AlleyOpponent o, AlleyContext ctx);                    // Name = DeckKey, Notes["recipe"] = base / headliner deck
    public static Deck? ResolveDeck(Profile profile, string deckKey, AlleyContext ctx); // "alley:<table>.<seat>" and "alley:bracket.<slot>"
    public static Personality PersonalityOf(AlleyOpponent o, AlleyContext ctx);
    public static IReadOnlyList<uint> Showcase(AlleyOpponent o, AlleyContext ctx);  // 3 codes
    public static AlleyTier TierOf(AlleyData data, int table);
    /// <summary>Records your duel at the next seat (winner 0 you, 1 them, 2 draw; turns and who went first, for the guard). Settles the ante. Save after.</summary>
    public static AlleyResult Record(Profile profile, AlleyData data, int table, int winner, int turns, int firstPlayer, AlleyContext ctx);
}

// Ante.cs
public sealed class Stake { public uint Code; public Rarity Rarity; public int Grade, Slab; public uint Seed; public string Flag = ""; }
public sealed class StakeBinder { public string Owner = ""; public List<Stake> Cards = new List<Stake>(); public int Serial, RefilledAt; }
public sealed class AnteLive { public string Table = ""; public int Seat, Held, StartedAt; public Stake? Theirs; public CardCopy? Yours; }
public sealed class RailEntry { public CardCopy Copy = null!; public int Price, ExpiresAt; }
public sealed class AnteSettlement { public string Outcome = ""; public int Returned, Lost; public CardCopy? Won; public Added? Added; public RailEntry? Railed; public List<string> Events = new List<string>(); public string Summary = ""; }
public static class Ante
{
    public static int StakeFor(AlleyTier tier);                                              // AlleyEconomy.StreetStake / DenStake / OrvelStake
    public static StakeBinder Binder(Profile profile, string owner, AlleyContext ctx, Deck? deck = null); // made from the CPU's deck and refilled lazily
    public static Stake? Next(Profile profile, string owner, AlleyContext ctx, CardCopy? yours = null);   // the next stake in order (Orvel: the next fitting 1-2× yours)
    public static int Value(Stake stake, AlleyContext ctx);                                  // Pricing.V at the card's commonest printing
    public static CardCopy ToCopy(Stake stake, Profile profile);                             // Source Ante, Paid 0, a new id
    public static List<CardCopy> Stakeable(Profile profile, Func<uint, int> neededByDecks, int minValue);
    public static string? Blocked(Profile profile, AlleyTier tier, Stake? theirs, CardCopy? yours, Func<uint, int> neededByDecks, AlleyContext ctx);
    public static AnteLive? Hold(Profile profile, AlleyData data, int table, AlleyOpponent o, CardCopy? yours, Func<uint, int> neededByDecks, AlleyContext ctx); // moves the DP stake out of Dp; save right after
    public static AnteSettlement Settle(Profile profile, int winner, bool firstTurnLoss, AlleyContext ctx);   // called by BackAlley.Record
    public static AnteSettlement? SettleAbandoned(Profile profile, AlleyContext ctx);                       // a quit, or a load with AnteLive
    public static List<string> TickRail(Profile profile);                                    // expired names; call after any duel and at load
    public static string? BuyBackBlocked(Profile profile, RailEntry entry);
    public static bool BuyBack(Profile profile, RailEntry entry);
}

// AlleyEconomy.cs: section 15's knobs as public static fields (ints, doubles and int[] arrays), e.g.
public static class AlleyEconomy
{
    public static int StreetStake = 300, DenStake = 1000, OrvelStake = 3000, OrvelMinCard = 1000;
    public static int StreetCap = 1000, DenCap = 5000;
    public static double StakeFloor = 1.0, OrvelMatchMin = 1.0, OrvelMatchMax = 2.0, ContrabandWeight = 3, RailMarkup = 1.5;
    public static int BinderSize = 12, RefillEvery = 3, RailDuels = 5, DuelDp = 0, ClearTokens = 1;
    public static int ShelfSize = 6, ShelfEvery = 10, ShelfMinValue = 3500, VanUsedSize = 18, VanUsedSlabMax = 8;
    public static double ShelfMarkup = 1.5, PackSlabNine = 0.75, WildcardChance = 1.0 / 3, PennyChance = 0.2;
    public static int BrawlEntry = 1000, OpenEntry = 2500, OpenTokens = 1;
    public static int[] BrawlPay = { 900, 650, 400, 150 }, BrawlPacks = { 2, 1, 0, 0 };
    public static int[] OpenPay = { 1500, 1000, 600, 300, 100 }, OpenPacks = { 4, 2, 1, 0, 0 };
    public static int AlleyTurns = 40, TurnCounterFrom = 30;
    public static int ShelfPrice(int value);                  // ShelfMarkup × value, rounded to 500
}

// HouseRules.cs / DuelRunner.cs: section 4.
```

**As built (A1, 2026-11-02).** The API above holds; where the build differs or adds, A2 and A3 build from this:
- **Seats are 1-4** (an encore is seat 0): `AlleyOpponent.Seat`, `DeckKey` "alley:<table id>.<seat>", the match key
  "alley:<table id>.<seat>". `NextSeat` returns 1-4, or -1 while the table is locked (after a night's fourth win it's
  the next night's seat 1).
- **`TurnCap.Attach(runner, duel, int lastTurn = 0)`**: 0 reads `AlleyEconomy.AlleyTurns` (a const default can't read a
  knob; `AlleyEconomy.AlleyTurns` starts at `TurnCap.AlleyTurns` = 40). `TurnCap` also has `LastTurn` and `Decided`.
  `DuelNarrator.TurnCapText(lastTurn, winner, winnerLp, loserLp)` (static) gives the results screen the log's line.
- **`AnteLive.Binder`** (new): the binder their card goes back to; saved as `antelive <table> <seat> <held> <startedAt>
  <binder>`.
- **`Ante.Hold` always sets `AnteLive`** when the duel can start: an empty binder holds `Held` 0 and no card, so leaving
  that duel still counts as a loss at its seat. `BackAlley.Record` settles only its own table's `AnteLive` and records
  at its seat (so an encore is recorded through `Hold`); without one it records at `NextSeat`.
- **`Ante.SettleAbandoned`** also counts the duel (`AlleyDuels`, `NoteResult` as a loss, the character's loss), and its
  summary starts "You left <table> mid-duel. That counts as a loss.".
- **`Ante.TickRail(profile, Func<uint, string>? nameOf = null)`**: names need the cards (codes without them).
- **`AlleyContext.Data`** (new, `AlleyData?`): binders need a table's tier and a headliner's `stakes:`. Every
  `BackAlley`/`Ante.Hold` call with the data sets it (`??=`); set it yourself if you call `Ante.Binder` or `Next` first.
  Also `ctx.Pool(eraCap)` (the cached contraband pool; 9999 the ever-Forbidden one) and `ctx.Commonest(code)`.
- **`AlleyTable.Ready`** (new, `ready: no` in the data): Orvel's and Nyx's tables ship `ready: no` ("Coming soon") until
  their authored decks exist; `IsUnlocked` needs it. Phases B and C flip them.
- **The sub-seed retry** is `Alley.SplashChecked(baseDeck, e, pool, alley, seed, cards, curation, out int subSeed)`
  (the first try is `seed` itself, so it equals `Splash`; -1: the base made legal and unsplashed). `Splash` itself only
  gains the `#recipe` note. Also `Alley.CodeOf(name, cards)`, `Alley.SplashSize(e, base, curation)`, `Alley.SubSeeds`.
- **A headliner's deck keeps its own `#recipe`** (yata-mags plays as chaos-yata, mass-driver-moss as frog-ftk): setting
  it to the headliner's deck name would lose its playbook. A regular's is its base.
- **Seeds:** the night's own seed (seat 0) shuffles the bases (seats 1-3 take them in turn) and rolls the closer and
  the wildcard; each seat's seed then gives the splash seed, the name and the tint. A regular whose base deck is gone is
  redrawn from `seed + 4096·k`. `Tint` is 0xRRGGBB. Skill, Elo and `Binder` aren't saved: `Lineup` fills them.
- **A night's end** drops that night's regular binders from the save (those regulars never sit again).
- **`Profile.UnlistedPages`** (not `Unlisted`, which Phase B's static class takes); `UnlistedPage`, `AlleyTableState`
  and `HeadlinerState` are in BackAlley.cs (Phase B uses this `UnlistedPage`, doesn't define another). Van epochs:
  `VanUsedEpoch`/`VanUsedSold`, `VanShelfEpoch`/`VanShelfSold`; `AnteWins`/`AnteLosses` count stakes won and lost.
- **Not in A1:** the bracket save lines (`bracketsize`, `bracketgames`, `bracketsplash`) need Phase C's instance
  `Bracket.Size` and live in `Brackets.Read`/`Write`, so Phase C adds them; `ResolveDeck` returns null for
  "alley:bracket.<slot>" until then.
- **Additions:** `BackAlley.VisitEvent`, `FenceEvent`, `NickLadder`, `Nick(profile, data)` ({nick}),
  `EncoreOpen(profile, data, table)`, `Triggers(data)`/`IsStoryTrigger(t, data)` (every alley story trigger),
  `IsVoiceHook(hook, data, cards)`, `VanHooks`, `Placeholders`; `Ante.DuelsLeft`; `SellDesk.AnteRefusal`;
  `ShopVoice.OpponentSpeaker`; `ShopVoice.Problems(..., Func<string, bool>? knownHooks = null)` (with it: its hooks, the
  alley's placeholders, speaker `opponent`, no shelf: pools); `AlleyEconomy` also has `StreetGrades`, `DenGrades`,
  `OrvelGrades` ((grade, slab, weight) arrays), `OrvelMinRarity`, `ShelfMinRarity`, `PackSlabMinRarity`, `PackSize`,
  `VanUsedEvery` and `RailPrice(value)`; `AlleyResult.Events` adds `turncap` (turns past `AlleyTurns`) and `railgone`.
- **`AlleyData.Problems`** checks a headliner's Main and Extra only (a CPU never sides: Yata Mags's second Card
  Destruction is in its side deck). **Data:** Mass Driver Moss is `list: TCG March 2010` (its deck's `#banlist`; this
  spec's draft said September 2010) with `over: Treeborn Frog` (it runs 3; that list allowed 2): for the Headliners
  author and the owner to confirm.
- **Section 3 vs the decks:** Yata Mags runs Fiber Jar (`Never`) and Magical Scientist (`BossOnly`). Headliners are
  checked under their own list (the table in section 3), so nothing fails, but "no deck in the alley ever runs" a
  `Never` card isn't true of it. The deck is unchanged (CPUs play at full strength); the owner decides.
- **After A2 and the Phase A review (2026-11-02):**
  - `BackAlley.Record(..., bool conceded = false, bool? turnCap = null)`: a conceded duel never takes the first-turn
    guard; `turncap` is raised when `turnCap` is true (the app passes `reason == DuelRunner.ReasonTurnCap`), and only when
    it's null is it judged by turns past `AlleyTurns`. `TurnCap.Decided` is true only when the duel ended with
    `ReasonTurnCap` (a core win in the same batch, such as a deck-out at turn 41's draw, isn't the cap's).
  - An alley win counts in `Profile.Wins` but not in `DuelReward.WinsAdded`, so `CardUnlocks.AfterDuel` never gives a
    bond, gift, drop or trade for `alley-regular` or a headliner: the staked card is the only prize.
  - Placeholders: `AlleyResult.Fill` (and `AnteSettlement.Fill` for a quit) from `BackAlley.FillFor(profile, data,
    opponent, stake, cards)`, passed with the scenes: `Story.Fill(..., fill)` fills {card} (a grant's first), {table},
    {handle} and {grade} from it, and {nick} from `BackAlley.Nick(profile)` (computed from the profile alone). Nothing is
    kept on the profile, so no fill carries over to later scenes. Voice lines use the same `FillFor`.
  - The app queues an alley result's scenes (with their fill and grants) in `CampaignState` until its results screen
    shows; if the menu, editor or shop opens first, the next campaign screen plays them. Circuits, Tag Cups, guests,
    cups and brackets share the old pattern (their scenes are lost the same way) and weren't changed.
  - Save: `alleyopp` writes an empty kind, character or base as "-"; settling an `antelive` line from before it kept
    its binder finds the binder from the seat's opponent.
- **The harness:** `CampaignTest`'s story-trigger check accepts `BackAlley.IsStoryTrigger` (the alley scenes would
  fail it otherwise); A3 owns the rest.

**Acceptance:** the harness builds; A3's checks pass; `--alley-gen` still reports 0 short or illegal; existing
`--campaign-test` checks unchanged.

**Chunk A2: Unity.** Files:
- `unity/Assets/Scripts/CampaignState.cs`: load `AlleyData` and alley.txt, build `AlleyContext` (the Traditional list
  from the lflists folder), `AlleyRules`, `ResolveDeck`, the load-time `BackAlley.Open` and `Ante.SettleAbandoned` /
  `TickRail` with their notices.
- `unity/Assets/Scripts/CampaignScreen.cs`: the third tab, the theme switch and crossfade, the alley map and table
  panel, `InAlley`, the `AlleyDuel(int table)` event.
- `unity/Assets/Scripts/CampaignScenes.cs`: the versus "CONTRABAND" label, stake chips, rules chip; results lines for
  stakes.
- New `unity/Assets/Scripts/DuelController.Alley.cs`: versus, `Ante.Hold`, the coin toss, `StartAlleyDuel`,
  `FinishAlleyDuel` (`BackAlley.Record`), quit-as-loss on concede and on leaving.
- `unity/Assets/Scripts/DuelController.cs`: `NewDuel(alleyTable: int?)`, `LoadDeck` resolving `alley:` keys,
  `TurnCap.Attach` for alley duels, the turn-40 result lines, the `-alleycheck` flag.
- New `unity/Assets/Scripts/DuelController.AlleyCheck.cs`.
- `unity/Assets/Scripts/DeckEditor.cs`: Alley rules when opened with `InAlley`.
- `unity/Assets/Scripts/BoardView.cs`: the "Turn N / 40" counter in alley duels.

**`-alleycheck <absolute folder>`** (absolute paths: relative ones write nothing). Screenshots and `Report` lines;
Phase A covers steps 1-9:
1. A champion save with no cup cleared: no Back Alley tab; `AlleyOpen` false.
2. A cup cleared (forced): the `alley` scene follows `cupclear:`; the tab shows "(new)".
3. The tab: `UiTheme.Current.IsAlley`; the map's nodes and locks; the Dumpster Row panel has 4 seats, handles, the
   stake (300 DP, held) and the card the first seat puts up, worth at least 300.
4. The versus: three showcase cards, all in the regular's `Added`; the stake chip's value equals `Ante.Value`; a deck
   with 2 Pot of Greed is refused with the Alley's message.
5. Start: `Dp` fell by the stake and the header shows it held; the save has `antelive`; a forced loss: `alley.stake`
   grew by 300, their card is back on top of the binder, the seat's `alleyopp` line is byte-identical after a
   reload.
6. A forced win: the 300 DP is back; their card is in the collection with Paid 0 and Source Ante; the Sell desk
   refuses it with the Ante words; the seat shows ✓; the next seat is playable.
7. Quit mid-duel: concede → a loss; a save written with `antelive` and reloaded → a loss and the notice.
8. A forced turn-41 start: the turn-40 line, the winner by LP; equal LP: a draw, stakes back, the seat unchanged.
9. A forced first-turn loss: the stake is back and no `alley.stake` line was written; their card is back.
10. (Phase B) Orvel's stake picker lists only spare copies; a loss puts the copy on the rail at 1.5×; the van's Used &
    graded tab shows it; buying it back returns the same copy id.
11. (Phase B) The van's three tabs (Shelf | Used & graded | Sell); the shelf's six slabs priced at `ShelfPrice`; a
    Sell-tab sale pays exactly the Sell desk's price; the Unlisted binder with one page.
12. (Phase C) Alley Brawl entry, its bracket of 8, Skip to end, a Contraband Pack reveal (4 Raw and a sealed slab).
13. (Phase C) The Midnight Open's 16 slots and the Bo3 pips; Nyx's Table's four seats.
14. Back to Circuits: `MainStreet`; the campaign screenshot matches `-themecheck`'s main-street baseline.
15. The save after the check: `Ledger.Net(profile) == profile.Dp - start` (no duel held), and every new line
    round-trips.

**Acceptance:** `-alleycheck` steps 1-9 and 14-15 pass with 0 issues; `-campaigncheck` and `-themecheck` pass;
MainStreet screens unchanged.

**Chunk A3: harness.** Files: new `src/Ygo.Harness/AlleyTest.cs` (called from `CampaignTest.Run`, one line in
`CampaignTest.cs`), `src/Ygo.Harness/LedgerCheck.cs`, `src/Ygo.Harness/Arbitrage.cs`, `src/Ygo.Harness/AlleyStudy.cs`
(TurnCap in its duels; `--alley-study`), `src/Ygo.Harness/Program.cs` (the `--alley-study` flag). Not
`AlleyProbe.cs`: it stays the Economy Designer's model study.

**Checks (in `--campaign-test`):**
1. **The list:** all 33 `Excluded` names resolve (alias 0); `Never` ∪ `BossOnly` = `Excluded`, disjoint; the Alley
   list has each at 0 and every other Traditional limit unchanged.
2. **Legality:** under `Alley.Rules`, 1 Pot of Greed legal, 2 illegal; an excluded card illegal; Ban Passes 0.
3. **No leaks:** for every circuit, cup and final round, `Rules`/`CupRules`/`RoundRules` never return the Alley
   list; a deck legal only in the alley stays illegal in a cup; a stamp can't be earned by an alley win.
4. **Turn cap:** a scripted duel of two passing duelists ends on turn 41's start with the higher-LP side winning; with
   equal LP a draw (`ReasonTurnCap`); a duel without `TurnCap` plays on.
5. **Data:** alley-events.txt parses; `Problems()` is empty (tables match `Alley.Events`, bases exist, are in range,
   `FromEra` and not `skip:`ped; ready headliners' decks validate under `list:` with `over:`; `allow:` ⊆ `BossOnly`).
6. **Splash decks:** 1,000 seeds per table: every deck legal under the Alley list, full splash (or the base's
   `size:`), no `Never` or `BossOnly` card, archetype cards all kept, `Notes["recipe"]` = base, no fallback to
   unsplashed.
7. **Determinism:** a lineup drawn twice from one save is identical; a loss changes nothing; save and load change
   nothing; after the 4th win the next night differs; the wildcard turns up in 33% ± 2% of 3,000 seeded nights with
   two ready headliners.
8. **Unlock:** the alley opens exactly at the first cup clear; an old save with a cleared cup opens at load.
9. **Ante:** `Hold` moves the stake out of `Dp` with no ledger line; `AnteLive` round-trips; a win returns it and adds
   a Paid-0 Ante copy the Sell desk refuses; a loss and `SettleAbandoned` spend it to `alley.stake` and put their card
   back on top; a draw and the first-turn guard return it with no line; a won 4th copy keeps your best three and pays
   0 DP; an empty binder holds nothing of yours.
10. **Binders:** 12 different cards from the CPU's deck; contraband drawn about 3× as often as its share; every stake
    between `StakeFloor × stake` and the tier's cap; Street all Raw; Orvel's 9-10 slabs at Super Rare and up, and the
    one offered is worth 1-2× your card; one back per 3 alley duels up to 12; a headliner's binder starts with its
    `stakes:` in order; the same save gives the same stakes.
11. **Save and New Game+:** every new line round-trips; old saves load with defaults; `NewGamePlus()` keeps the alley
    and re-pays no first clear.
12. **Ledger:** every new key is listed in `LedgerCheck`; a scripted 30-duel alley session reconciles
    (`Ledger.Net == Dp + held - start`); alley duels' DP all-in is at most 0 (E7).
13. **Story:** every trigger the code raises has a scene or is listed as pending; every speaker has a profile; a first
    Alley Brawl title raises `bracketwin:Alley Brawl` and not `bracketwin`; `heldcard` fires exactly once, in either
    order of Orvel's clear and the `cup-goat-clear` scene, and never with only one of them.

**`--arbitrage`, an alley section (new):** ALLEY-ECONOMY asks for "the probe's loops moved onto the real code".
Reuse `AlleyProbe`'s action list and its exhaustive search (every sequence of 1-4 of its 11 actions, then sell
everything), with each action bound to the real `Ante`, `MothsVan`, `ContrabandPacks`, `GradingDesk` and
`SellDesk` calls instead of the model. Phase A binds the ante actions; B the rail, shelf and used actions; C the
bracket entries. Fail if any sequence is DP-positive. Named loops besides the search:
- "Win every stake at a table at 100%, sell everything": 0 DP, never more.
- "Win a stake as a 4th copy, sell everything": 0 DP from the won copy and the displaced one.
- "Lose on the first turn, repeat": 0, never positive.

**`--alley-study [--only table] [--games N]` (new, measuring):** the QA stand-in's best deck and the starter against
each seat of each table (regulars over 20 seeded nights, the closer, the ready headliners), with `TurnCap`, against
the targets in section 6. Reports win rates, duels decided at turn 40, first-turn losses.

**Acceptance:** `--campaign-test` and `--arbitrage` pass (`build/bin/check/Ygo.Harness.dll`); the new checks fail when
fed a deck with 2 Pot of Greed, an `allow:` naming Wind-Up Hunter, or a TurnCap that ends on turn 40's start.

### Phase B: Orvel, the van, Unlisted and the look (about 1 session)

Orvel's Table (your card, the rail), Moth's van, Contraband Packs (as prizes), the Unlisted binder, posters, Penny, the alley's
materials, font, mat, sound and crossfade.

**Chunk B1: engine and data.** Files:
- New `src/Ygo.Engine/Campaign/MothsVan.cs` (`MothsVan`), new `src/Ygo.Engine/Campaign/ContrabandPacks.cs`, new
  `src/Ygo.Engine/Campaign/Unlisted.cs`.
- `src/Ygo.Engine/Campaign/Posters.cs` (the alley posters), `src/Ygo.Engine/Audio/Sfx.Themes.cs` (`alley`, `orvel`,
  `nyx`, the bed), `src/Ygo.Engine/Campaign/BossMusic.cs` (`ForAlley`).
- `src/Ygo.Engine/Campaign/BackAlley.cs` (Orvel's seat, the Penny cameo, Unlisted pages on a win).
- Data: `decks/alley/orvels-house.ydk` (authored, power-patron plus Black Market contraband; measured with
  `--alley-study` and `--endless-test`), `campaign/van.txt`, `campaign/mats.txt` (the three alley mats),
  `campaign/story.txt` (`fence`, `rail`, `buyback`, `railgone`, `unlisted`, `pennywin`, `heldcard`,
  `open:Orvel's Table`, `alleyclear:Orvel's Table`), `alley-events.txt` (Orvel's seat), the HELD row and
  `BackAlley.HeldCardDue`.

**API (binding for B2 and B3):**

```csharp
public sealed class ContrabandPacks
{
    public const int Size = 5;
    public ContrabandPacks(ICardDataProvider cards, PrintingIndex printings, IEnumerable<Banlist> lists, Banlist alley);
    public IReadOnlyList<uint> Pool { get; }
    public List<PrizePull> Roll(uint seed);                    // 4 Raw cards and a sealed slab 9-10 (Super Rare and up), all different
    public List<PrizePull> Give(Profile profile, uint seed);   // into the collection: Source Prize, Paid 0, a 4th copy for 0 DP (as PrizePacks.Give)
    public PrizeOdds Odds();
}
public static class MothsVan
{
    public static List<Listing> Used(Profile profile, ContrabandPacks packs, AlleyContext ctx);   // used-market rules over the pack pool, at V, slabs up to 8
    public static List<Listing> Shelf(Profile profile, ContrabandPacks packs, AlleyContext ctx);  // 9-10 slabs, Secret Rare and up, V >= ShelfMinValue, at ShelfPrice(V)
    public static string? Blocked(Profile profile, Listing listing);
    public static int Buy(Profile profile, Listing listing, bool shelf);       // -1 refused; else the DP a displaced 4th copy paid (refund.alley)
    public static int Sell(Profile profile, CardCopy copy, Func<uint, int> neededByDecks) => SellDesk.Sell(profile, copy, neededByDecks);
    public static int DuelsToRefresh(Profile profile, bool shelf);
}
public sealed class UnlistedPage { public string Key = "", Table = "", Kind = "", Base = "", Handle = ""; public List<uint> Added = new List<uint>(), Cut = new List<uint>(); }
public static class Unlisted
{
    public static bool Add(Profile profile, AlleyOpponent beaten);   // false: that page exists
    public static IReadOnlyList<UnlistedPage> Pages(Profile profile);
    public static Deck DeckOf(UnlistedPage page, AlleyContext ctx);
}
public static class BossMusic { public static (string theme, int transpose) ForAlley(AlleyData data, int table, int seat); }
```

**As built (B1, 2026-11-02).** The API above holds as written; B2 and B3 build from these additions and choices:
- **`ContrabandPacks`:** `Pool` is `Alley.Contraband(lists, alley, 9999, ...)` (231 cards); `Roll(seed)` returns slot
  order (four Raw, then slot 5's slab, never a card twice), `Give` returns `PrizePacks.RevealOrder` (the slab last), as Prize
  Packs do. `Size` stays the const 5; `Roll` reads `AlleyEconomy.PackSize` (Raw slots = `PackSize - 1`). `Odds()` fills
  `Field` (slots 1-4, exact for each slot: every slot's card is uniform over the pool), `FieldCards`, `Slab` and
  `SlabTenPercent`; `TournamentSet` is null and `Tournament` empty. **For B3:** at 20,000 packs the slab slot's
  standard error is about 0.32 points, so "within 0.5 points" fails about one seed batch in three (seed base 0xBEEF:
  Ultra Rare 0.70 off; five other bases 0.26-0.44; 200,000 packs 0.10). Check the slab slot at 1 point, or at 100,000
  packs.
- **`MothsVan`:** also `UsedEpoch`, `ShelfEpoch`, `ShelfItems(packs, ctx)` (every (card, printing, grade) the shelf can
  show: 429 from 147 cards), `HeldShown(profile)` (the HELD row, until held-card-home is seen), `HeldCode` (Scapegoat),
  `HeldGrade` (6), `HeldLabel`, and `RailFill(profile, entry, cards)` ({card}, {grade}, {price}, {left} for van:rail
  lines and the rail row). The used tab is 13 Raw and 5 slabs (the used market's 17 in 24), slab grades 8 down to 3 at
  the used market's odds; the shelf draws its six items evenly from `ShelfItems`, no card twice. Van copies are
  `CopySource.Used`. `Buy` also refuses (-1) a listing shown before a restock (its `Listing.Epoch`, stamped when listed,
  against now) and a slot already in the sold set (a second listing of it). Listings' salts: used 0x0A11, shelf 0x5E1F.
  **Changed 2026-11-02 (review fix):** the shelf's order and a listing's seed hang on the item itself (card, printing,
  grade: `MothsVan.ShelfKey`), not its index in `ShelfItems`, and a bought slab is marked by that key
  (`Profile.VanShelfSoldCards`), so a pool change mid-period (an exclusion, a list's history) keeps the other cards and
  keeps a bought slab sold. An older save's sold slots are matched to the slab bought (its copy's seed, the slot's) and
  marked by its card; a slot whose copy has left the collection keeps marking its slot. Loading such a save shows a
  reordered shelf once (the period's pool had already changed with NL-0's exclusions). The used tab is unchanged (its
  slots; it restocks every few duels).
- **`Unlisted`:** also `Event` ("unlisted"), `KeyOf(o)` (`<table>:<base>`, `hl:<id>`, `authored:<deck>`; Penny's is `Unlisted.PennyKey`, `authored:penny`, a
  regular's page otherwise, so the Dumpster Row warriors regular gets its own), `Backfill(profile, data)` (at load: a
  page for each headliner a Phase A save already beat, no event; regulars can't be backfilled) and
  `Contraband(page, ctx)` (the cards to mark: a regular's splash; a headliner's or authored deck's ever-Forbidden
  cards). A page's `Kind` is regular, headliner (wildcards and encores too) or authored; a regular whose splash added
  nothing gets no page. `BackAlley.Record` adds the page on a win, raises "unlisted" for every new page (its scene plays
  once) and returns it in the new `AlleyResult.Page`.
- **`BackAlley`:** `HeldEvent`, `HeldScene`, `HeldHomeScene`, `GoatClearScene`, `PennyId`, `PennyEvent` and
  `HeldCardDue(profile, data)`. `Record` never raises `heldcard` itself: the callers check `HeldCardDue` (after an alley
  result, after a Library result's scenes, at the campaign screen) and raise it. `Record` raises `pennywin` (your first
  win against Penny) and `unlisted`; with `railgone` its fill's `{sold}` (`BackAlley.SoldKey`) is the card Moth sold (the
  app's own rail tick sets it too), for first-railgone. Callers raise
  `fence` (`BackAlley.FenceEvent`, the van's first open) and `buyback` (after `Ante.BuyBack` returns true). An authored
  seat's `Handle` is its character's name ("Patron Orvel"); its `Elo` is its recipe's rating plus the splash gain of the
  table it opens from (Orvel: 1,546 + 50).
- **Penny:** `Kind` "regular", `Character` "penny", her profile's deck (warriors) with a full Dumpster Row splash, at
  seat 1 with `PennyChance` from its own roll (`Hash.Mix(night seed, 0x9E77)`, so the night's other seats draw as
  before), never when the wildcard has seat 1. Her binder is "penny" (one across nights): a Kuriboh with
  `Stake.Flag` "penny" (`Ante.PennyFlag`, `Ante.PennyCard`), Common, Raw 4 for the first; once won, a fresh one the next
  night (its `RefilledAt` is the Street table's night, not a duel count). `Ante.Hold` holds 0 DP against it, `Blocked`
  skips the DP check, a loss to her raises no `antelose` and a first-turn loss no `firstturn`. `PersonalityOf` returns
  her own profile. Lines: alley-voice.txt's `regular:intro:penny` / `regular:win:penny`.
- **The rail's clock (an A1 fix):** `ExpiresAt = DuelsPlayed + 1 + RailDuels`. The losing duel is counted
  (`NoteResult`) right after the stakes settle, so A1's `+ RailDuels` left the card 4 duels, not 5. `Ante.Settle` is
  for `Record` (and `SettleAbandoned`), which count the duel after it.
- **`BossMusic.ForAlley`:** a regular table's seats 1-3 ("alley", 0), seat 4 and an encore (seat 0) ("alley", +5); a
  table whose `music:` is its own theme plays it at its host's seat (Orvel's one seat: "orvel"; Nyx's last seat: "nyx")
  and "alley" elsewhere. `BossMusic.Alley` = "alley".
- **Sound:** themes `alley` (no tune, as "duel"; D minor, a walking bass, new groove "alley"), `orvel` (a brass tango in
  thirds, F minor, new groove "tango") and `nyx` (a choir doubled at the octave, A minor). The bed is
  `Sfx.RainBed()` (`Sfx.RainBedSeconds` = 12 s, mono, peak 0.6, loops without a seam; a second or two to render, so
  off the main thread): B2 plays it at MusicVolume × 0.35.
- **Posters** (`Posters.All`, after every-era): no-list (Pot of Greed; earned: the `alley-kit` scene seen), lost-card
  (Change of Heart; a rail entry now, `alley.rail` ever spent, or `orvel-first-loss` / `first-railgone` seen),
  orvels-marker (Jupiter the Power Patron of Destruction, the Narrative didn't pick; orvels-table cleared), held
  (Scapegoat; held-card-home seen), brawl-bill (Raigeki) and midnight-bill (Dark Hole) by `AlleyBrackets` titles. Unity
  note: `-campaigncheck` uses `Posters.All[^1]` as its locked poster, now midnight-bill (still locked there).
- **Orvel's Table:** `ready:` flipped (opens with The Black Market's clear). `decks/alley/orvels-house.ydk` is
  power-patron (#recipe power-patron, its playbook; legal under the Alley list) without its hand traps and board
  breakers and with a leaner engine (out: 3 Mulcharmy Fuwalos, 3 Infinite Impermanence, 3 Pot of Sloth, 2 Lightning
  Storm, 2 Book of Eclipse, 2 Medius the Pure, 1 Pendulum Treasure), and one copy each of sixteen Black Market
  contraband cards in their place (Pot of Greed, Graceful Charity, Monster Reborn, Change of Heart, Harpie's Feather
  Duster, Heavy Storm, Snatch Steal, Brain Control, Premature Burial, Solemn Judgment, Morphing Jar, Return from the
  Different Dimension, Mirage of Nightmare, Crush Card Virus, That Grass Looks Greener, Chaos Sorcerer). Measured with
  the A3 study's duel (the QA stand-in planning with its best deck, Fire King Snake-Eye from build/qa-night5/mods,
  against Orvel planning at skill 1.0, the turn cap) from a scratch copy, since `--alley-study` covers Street and Den
  tables only (widened as is, its closer pick would throw on a one-seat table): **the player wins 52.9% of 240**
  (±3.2; target 55-60%), 0 duels decided at turn 40, no first-turn losses. On the way: the list as built 51% of 100;
  the October swaps undone plus five contraband 35% of 40; only the hand traps swapped 48% of 560 over three seed
  sets. The matchup moves little with the non-engine slots: Phase D's full study should retune it if it stays under
  55% (the next lever is the engine's 3-ofs). Its endless check (1,200 bot duels against 40 alley regulars): prompt cap
  0%, decided at turn 40 0%, draws 0.1%. **For B3/C3:** add the Orvel and Nyx tiers to `--alley-study` and the
  authored decks to `--endless-test`.
- **Data:** van.txt's `van:held-paid` is gated `after: held-card-home` (the story doc only said so in a comment); Gus's
  footers are in shop.txt, Kit's with `after: tag-night` too (SHOP-REWORK's rule for Kit lines). mats.txt's
  alley-dumpster-row uses the `brick` motif: `Playmat` warns and skips the line until B2 adds the motif.
- **Harness call sites fixed:** `CampaignTest`'s poster check (its everything-cleared save now earns the alley's
  posters), `AlleyTest` unlock (Orvel's Table opens), save (more than one Unlisted page) and ante (its seat-1 night
  draws with `PennyChance` 0, so seat 1 is a regular's stake).

**Chunk B2: Unity.** Files: new `unity/Assets/Scripts/VanScreen.cs`; `CampaignScreen.cs` (the Unlisted binder on the
Library shelf, the van node, Orvel's panel); `DuelController.Alley.cs` (Orvel's stake picker, the rail notices);
`ShopScreen.cs` (only if `OpenPrize` needs a title/odds parameter for Contraband Packs); `Playmat.cs` (the `brick`
motif); `Sounds.cs` (the bed and its fade); new `unity/Assets/Scripts/AlleyMaterials.cs` (brick, chain-link, tape,
the silhouette); `UiTheme.cs` (a `TitleFont` slot: the stencil font or the fallback); `BoardView.cs` (the top bar
reads the theme in alley duels); `DuelController.AlleyCheck.cs` (steps 10-11); `DuelController.ThemeCheck.cs` (the
new screens); `DuelController.cs` (wiring).

**Chunk B3: harness.** Files: `AlleyTest.cs` (van, packs, rail, Unlisted, posters), `Arbitrage.cs` (van loops),
`LedgerCheck.cs`, `SoundExport.cs` (the new themes and the bed).
- **Checks:** Contraband Packs: 5 distinct ever-Forbidden cards, none excluded, real printings, slot 5 a sealed 9-10
  slab at Super Rare and up (9 about 75%), odds sum to 1, `Odds()` within 0.5 points of 20,000 seeded packs, every
  copy Paid 0. The shelf: 6 slabs, grades 9-10, values at least 3,500, priced `ShelfPrice(V)` (5,500-22,500), stable
  within an epoch. Moth's used listings stable within an epoch, priced at V, no slab over 8. Moth's Sell tab pays
  exactly `SellDesk`'s price. The rail: price 1.5 V, added to Paid on a buy-back, expires after 5 duels, buy-back
  restores the same copy, a needed-by-deck copy can't be staked, Orvel's offered slab is worth 1-2× your card. Unlisted: one page per (table, base), per
  headliner, per authored deck. Posters earned by their conditions.
- **`--arbitrage`:** the alley search with the rail, shelf and used actions bound (ALLEY-ECONOMY's bests: shelf →
  sell at most a sixth back, used → sell 20%, 1-5 rounds of lose, buy back, sell −11,100 on 12,000); all must stay
  negative.

**Acceptance:** `-alleycheck` steps 10-11 pass; `--campaign-test`, `--arbitrage` pass; `-themecheck` shoots the van and
the alley panel in both themes; Orvel's Table meets its [P] target in `--alley-study`, or the house deck is retuned.

### Phase C: brackets and Nyx's Table (about 1 session)

**Chunk C1: engine and data.** Files: `src/Ygo.Engine/Campaign/Brackets.cs` (section 11), `BackAlley.cs` (Nyx's
seats, bracket fields: `BackAlley.BrawlField`, `MidnightField`), `decks/alley/gravekeepers-contraband.ydk`
(authored, Nyx), `campaign/alley-events.txt` (brackets and Nyx final), `campaign/story.txt` (`brawl`, `midnight`,
`bracketwin:` scenes, `open:Nyx's Table`, `alleyclear:Nyx's Table`).

```csharp
// Brackets.cs (additions; Locals' API unchanged)
public sealed partial class Bracket
{
    public const int LocalsSize = 8;
    public int Size = LocalsSize; public int MatchCount => Size - 1; public int Final => Size - 2; public int Rounds { get; }
    public int FinalBestOf = 1; public int FinalGamesA, FinalGamesB;
    public int RoundOf(int m); public IEnumerable<int> MatchesIn(int round); public string RoundName(int round);
}
public static partial class Brackets
{
    public const string BrawlKind = "brawl", MidnightKind = "midnight";
    // Entry, pay, packs and the token: AlleyEconomy.BrawlEntry/BrawlPay/BrawlPacks, OpenEntry/OpenPay/OpenPacks/OpenTokens.
    public static int[] Order16 { get; }                                                      // 1,16,8,9,4,13,5,12,2,15,7,10,3,14,6,11
    public static string? EnterBlocked(Profile profile, AlleyData data, string kind);
    public static Bracket? EnterAlley(Profile profile, AlleyData data, string kind, AlleyContext ctx, string yourDeck = "");
    public static Bracket CreateAlley(AlleyData data, string kind, Profile profile, AlleyContext ctx, uint seed, string yourDeck = "");
    public static double SeriesOdds(double p, int bestOf);                                    // p²(3 − 2p) for 3
    public static BracketReward? Finish(Profile profile, Library library, Bracket b, PrizePacks? prizes, ContrabandPacks? contraband);
}
```

- `RecordYours` handles the Bo3 final (a game won or lost updates the games; the match is decided at 2) and pays
  `brawl` / `midnight` placements for alley kinds.

**As built (C1, 2026-11-02).** The API above holds as written; C2 and C3 build from these additions and choices:
- **The old statics are gone, not wrapped.** C# can't have a static and an instance member of the same name, so
  section 11's "size-8 wrappers" can't exist: `Bracket.Size`, `MatchCount`, `Final`, `RoundOf`, `MatchesIn` and
  `RoundName` are instance members only (`Bracket.LocalsSize` = 8 is the const). Every Locals behaviour is unchanged
  and the harness compiles as it was, but **Unity's static call sites must become instance calls (`b.Final`,
  `b.RoundName(b.RoundOf(m))`, `b.MatchesIn(k)`, `b.Size`) in C2**: BracketScreen.cs (lines 144, 170-171, 204, 210, 224,
  238, 288, 310, 373), DuelController.Brackets.cs (77, 119, 188, 226, 236, 250-251, 282-283, 290, 374),
  DuelController.Qa.cs (1054, 1065), CampaignScreen.cs (1764), DuelController.cs (1569). Unity doesn't compile until then.
  Other places assumed 8: `Enumerable.Range(0, 3)` over rounds (use `b.Rounds`), `round == 2` for the final (use
  `b.Rounds - 1`), `DebugEntryRows == Bracket.Size`.
- **`Bracket`:** `new Bracket(size)` sizes `Slots` and `Winners` (the parameterless one is 8; setting `Size` alone
  doesn't resize). `Rounds`, `GamesToWin` (`FinalBestOf / 2 + 1`), `FinalLastLoser` (the side, 0 A / 1 B, that lost
  the final's last game: it chooses who goes first in the next; -1 before game 1). `Placement()` is 0-4 (4: out in the
  round of 16), `CurrentRound()` is `Rounds` once done, `PlacementText(4)` "Out in the round of 16". `Pair` at 16:
  round 0 is (2m, 2m+1), matches 8-11 the quarter-finals, 12-13 the semi-finals, 14 the final.
- **`BracketEntrant.Splash`** (an `AlleyOpponent`, null for everyone else): a splash regular's build, with `Table`
  `BackAlley.BracketTable` ("bracket"), `Seat` its slot (so `DeckKey` and `entrant.Deck` are "alley:bracket.<slot>"), and
  the new `AlleyOpponent.From` (the table it was drawn from: its event, skill, Elo and the personality's title).
  `Character` is "alley-regular". `BackAlley.ResolveDeck` resolves "alley:bracket.<slot>" from `profile.Bracket`;
  **`BackAlley.BracketOpponent(profile, slot, ctx)`** (new) gives it with skill and Elo filled (they aren't saved), for
  `PersonalityOf` / `Showcase`; `PersonalityOf` also fills them itself when given one loaded without. CPU-vs-CPU odds
  look up a splash seat's head-to-head pairs by its base (`Brackets.MatchupDeck`).
- **`BracketResult.Game`** (the final's game just recorded, 1-3; 0 otherwise) and **`NextGame`** (the series goes on;
  start the next game with `StartYours`, which moves the duel seed on). A drawn game replays (`Replay`, `Game` 0). Each
  game counts as a duel (`NoteResult`, EXP, the win or loss against the character). **Every alley bracket win goes into
  `Profile.Wins` directly, a named entrant's too** (not `AddWin`: an alley win never pays a card unlock's bond, gift or
  drop, `BackAlley.Record`'s rule; owner decision, review fix 5). A Locals win is `AddWin`, as before.
- **`SettleWatched` in a Bo3 final** counts one game a call (side 0/1) and decides the match once a side has 2
  (`b.Done`); a drawn game counts nothing and returns false (play it again). A CPU final settled by a coin
  (`CoinSide`, `SkipToEnd`) is one coin against the series odds from the games already counted.
- **Kinds:** `Brackets.BrawlKind`, `MidnightKind` (also their first-entry story events: the caller raises the kind after
  `EnterAlley`), `IsAlley(b)`, `IsAlleyKind`, `EntryFor`/`PayFor`/`PacksFor(kind)` (AlleyEconomy's for the alley),
  `PayLedger(kind)` ("alley" / "locals"), `EntryLedger(kind)` ("alley.brackets" / "brackets"), `OrderFor(size)`,
  `AlleyRecord(profile, kind)` (`profile.AlleyBrackets`), `AlleyEvent` ("alley": an alley bracket's `Event`, so its match
  keys are "brawl:alley.<m>", "midnight:alley.<m>"). `EnterAlley` draws the bracket first (`TryCreateAlley`: null, never
  a throw, when the field can't be drawn) and only then takes the entry, so a field that can't be built returns null and
  costs nothing; it shares `profile.BracketsEntered` with Locals. Its seed is **`Brackets.AlleySeed(profile, kind)`**
  (the salt, `0xA11B ^ kind`, the entry count), which the entry dialog's preview draws from too (Enter is off when the
  preview can't be drawn).
- **`Finish(..., ContrabandPacks? contraband = null)`**: the 5th parameter has a default, so Locals' 4-argument calls are
  unchanged. Alley packs come from `contraband.Give(profile, Hash.Mix(b.Seed, 0xC0B7, k))` (null counts them without
  opening, as `prizes` does). **`BracketReward.Tokens`** (new): the Midnight Open's first title pays `OpenTokens` once,
  keyed by `profile.Seen` "midnight:token" (`Brackets.OpenTokenKey`). An alley title raises only
  "bracketwin:<name>"; plain "bracketwin" stays the first Locals title's.
- **Fields** (`BackAlley`): `BracketField(profile, data, spec, ctx, seed)` → `(field, yourSeed)` reads the `field:`
  lines: "you <seed>", "<character> [deck] [seed]", "splash <n> <table> | cleared"; unseeded CPUs take the seeds left,
  by Elo, highest first. `BrawlField` / `MidnightField(profile, data, ctx, seed)` return the CPUs only.
  `BrawlTable(profile, data)` is "cleared" (decision O6): the last cleared Street/Den table before the last one, so
  Dumpster Row's, then The Basement Game's, never The Black Market's. Splash regulars' bases are the table's, shuffled
  by the seed and taken in turn; handles are unique in the field.
- **Save lines** (Profile.cs's reader routes the three new keys to `Brackets.Read`): `bracketsize <n>` (only for 16), `bracketgames <a> <b> <bestOf> <lastLoser>` (a Bo3 bracket, from
  entry), and `bracketsplash <slot> <table id> <base> <tint> <added csv|-> <cut csv|-> <handle...>`: **the table id is
  added** to section 14's format (skill, Elo and the title need it). A save whose splash regular lost its
  `bracketsplash` line drops the bracket, as one missing a seat does. Phase A/B saves and a Locals bracket mid-run read
  as before (no new lines are written for an 8-player Bo1 bracket).
- **Nyx's Table** (`ready:` flipped): seat 3's headliner sits whenever it's ready (beaten or not; Nyx's seats are the
  same every night), else its stand-in, a regular at the `else regular <skill>` skill. A regular seat on a table with
  no event of its own draws from its `event:` table, else from the table it opens from (The Black Market), so seat 3's
  stand-in is a Black Market regular. Seat 2 is chaos-yata as built ("Baron Corvus", unsplashed). Seat 4 is Nyx with
  `decks/alley/gravekeepers-contraband.ydk`, "Gravekeeper's Contraband" read as *the Gravekeeper's* (Nyx's
  title): Eldlich the Golden Lord's Zombies (decks/history/eldlich.ydk, `#recipe eldlich`, its playbook) without Gozen
  Match (boss-only; her `allow:` stays empty) or hand traps, and with eleven of the oldest contraband cards (Pot of
  Greed, Graceful Charity, Raigeki, Harpie's Feather Duster, Change of Heart, Snatch Steal, Heavy Storm, Monster
  Reborn, Premature Burial, Witch of the Black Forest, Sangan). Measured with the A3 study's duel from a scratch copy
  (seat 4 only; the QA stand-in planning with Fire King Snake-Eye from build/qa-night5/mods, Nyx planning at skill 1.0,
  the turn cap): **the player wins 55.6% of 180** (56.7% of 60 at seed 2, 55.0% of 120 at seed 3; target 55%), 0
  decided at turn 40, no first-turn losses. A Gravekeeper's archetype deck with twenty contraband cards (no playbook)
  was tried first: the player won 82.5% of 40. `--endless-test --games 1500`: her deck's 100 duels 0% prompt cap, 0%
  at turn 40, 0% draws (all 1500: 0%, 0.2%, 0%). Her panel Elo reads eldlich's 1,764 + 50 = 1,814 (the measured
  strength is nearer 1,620).
- **`AlleyData.Problems`** also checks the brackets: the data's entry, pay, packs and tokens equal `AlleyEconomy`'s
  (the code pays from the knobs), one "you" line, seeds in range and unique, roster characters with rated decks, splash
  tables that are Street or Den, and size − 1 CPUs. **An authored seat marked `| as-built` (a rated roster deck seated as
  built) is exempt from the exclusion check** (as a headliner is checked under its own list): chaos-yata as built runs
  Fiber Jar (`Never`), Magical Scientist and Imperial Order (`BossOnly`), and `allow:` can't name a `Never` card (the
  `ALLEY_BITE=allow` check proves it). Same situation as Yata Mags in A1's note: the deck is unchanged (CPUs play at full
  strength); the owner decides. Decks built for the alley (Orvel's, Nyx's) are always checked, rated or not: Nyx's runs no
  excluded card. (Review fix 1: the exemption was keyed on "has a rating", and the next `--rate-decks` would have rated
  decks/alley and turned the check off. Now it's the seat's `as-built` flag, which must name a rated deck of that
  character's on the roster; `--rate-decks` skips decks/alley; a deck built for the alley shows its recipe's rating plus
  the splash, rated or not, so Nyx's panel stays 1,814; an as-built seat shows its own rating.)
- **Data:** the scenes `alley-brawl`, `alley-brawl-win`, `midnight-open`, `midnight-open-win`, `nyx-table`,
  `nyx-table-clear` and `alley-closing` are in story.txt; the bracket pools (`mc:*`, `stream:*`) were already in
  alley-voice.txt from Phase A and match ALLEY-STORY. Baz's game calls are `mc:game:2` and `mc:game:3` (review fix 9:
  one pick a game; plain `mc:game` is the fallback for game-neutral lines, none yet). `hl-scientist-turtle` and its beat wait for Phase D (Lab Coat Lyle
  has no profile and isn't ready).

- **Review fixes (2026-11-02), beyond the bullets above:**
  - **Leaving an alley bracket duel is a loss** (owner decision), as at the tables: `Brackets.SettleAbandoned(profile,
    library, matchups)` records your live alley match as a loss through `RecordYours` (a best-of-three final's game:
    that game lost), its summary "You left the <name> mid-duel. That counts as a loss."; the menu calls it at once, and a
    load settles a live alley match the way `antelive` is. A Locals match left live still replays. The entry dialog and
    the bracket screen say so.
  - **A title's scene caught up:** `Brackets.AlleyTitleEvents(profile, data)` ("bracketwin:<name>" for each alley bracket
    with a title) is raised when Baz's chalkboard opens and queued at load; the story plays it once (seen), so an app
    closed during the Contraband reveal still gets it.
  - `AlleyResult.Opened` is a list (never saved): The Black Market's clear announces both Orvel's Table and Nyx's Table.
  - **The Midnight Open's field, retuned** (field choice only; no CPU deck changed): dragon-ruler out for the Ryzeal Cup's
    boss (`ryzeal ryzeal`, 1,867), then, with 1,896 still at 31.8%, k9-vanquish-soul out for `angelechy` (1,810). Nocturne
    and six-samurai stay (story); Ines stays out. `--bracket-sim` (20,000 brackets a row), a champion's best deck's title odds:

    | Midnight Open | before | ryzeal for dragon-ruler | and angelechy for k9-vanquish-soul (as built) |
    |---|---|---|---|
    | measured late-Library best (1,879) | 33.7% OVER | 28.4% | **26.4%** |
    | chaos-ritual, best legal in the Alley (1,896) | 36.7% OVER | 31.8% OVER | **29.6%** |

    Target 15-30%: met. The Alley Brawl wasn't retuned (not in the review's scope) and still reads well over its 30-45%
    target: 77.6% / 79.4% after Dumpster Row, 69.9% / 72.2% after The Basement Game (unchanged by these fixes).

**Chunk C2: Unity.** Files: `BracketScreen.cs` (16 layout, Bo3 pips, the alley entry dialog),
`DuelController.Brackets.cs` (alley entry from Baz's chalkboard, alley rules and `TurnCap` in bracket duels, the
Bo3 flow and Watch the final over games, Contraband prize reveal), `CampaignScreen.cs` (Baz's chalkboard panel,
Nyx's Table panel), `DuelController.AlleyCheck.cs` (steps 12-13).

**Chunk C3: harness.** Files: `BracketStudy.cs` (`BracketChecks`: sizes 8 and 16, `Order16`, `Pair`/`RoundOf`/
`Placement` for 16, Bo3 settle and save, Locals unchanged; `--bracket-sim` for the Brawl and Midnight fields),
`AlleyTest.cs` (Nyx's seats, `allow:` checks), `AlleyStudy.cs` (`--endless-test` with Orvel's and Nyx's decks and the
ready headliners), `Arbitrage.cs` ("enter every alley bracket, sell the prizes" < 100% of entries).

**Acceptance:** `-alleycheck` steps 12-13 pass; `--bracket-sim` puts a champion's best deck at 30-45% for the Brawl
title and 15-30% for the Midnight Open (or the field is retuned and the numbers recorded); Nyx's Table's authored
decks pass `--endless-test` (cap stops under 0.5%, decided at turn 40 under 2%, draws under 1%); Nyx meets 55% for
the best option in `--alley-study`, or is retuned.

### Phase D: the headliners' show and the proof (about 0.5-1 session)

docs/dev/HEADLINERS-2026-11-01.md Phases 2 and 3 inside the alley (the show layer, the other four headliners and
`scientist-turtle` as they pass go/no-go, Combo Breaker posters, Watch the combo on Unlisted pages, the exhibition on
Baz's chalkboard), then:
- `--alley-study` over every table against section 6's targets; retune `closer:`, bases and headliners where they
  miss;
- the Economy Designer's `--postgame-sim` with an alley pilot (held stakes, Orvel's Table, the van, bracket entries) and the knob values from
  ALLEY-ECONOMY;
- a 60-duel `-qaplay` run from a champion save that spends a session in the alley: 0 issues, ledger exact.

**Acceptance:** every target in section 6 met or recorded with the owner's acceptance; `--arbitrage` passes over every
loop; the QA run's ledger reconciles.

**As built (Phase D, engine, 2026-11-02).** HEADLINERS Phase 3's engine, data and harness (results and go/no-go:
HEADLINERS "Phase 3 results"). Everything below is additive; a deck without these plays as before (`--turn1` boards of
twelve playbook decks identical before and after).
- **Route signals, additive fields** (`Playbook.RouteSignal`): `PayoffStep` (the route's payoff step, 0-based; -1 none),
  `Counter` (what its meter counts: "damage", "cards banished", "summons"; empty: no meter) and `Count` (the running total
  since the route started). New `RouteEvent.Count` (last in the enum): raised at the headliner's next decision when the
  meter moved between steps (a loop its rules play: Moss's shots). A duel that ends mid-route (the kill) sends no last
  Count or Ended: read LP for the final number. `Route.Counts(name, Func<int>)` declares the meter (every headliner
  route has one; Frog FTK and the Yata routes count damage); `Playbook.Routes` lists a playbook's routes before they play
  (`RouteInfo`: Name, Steps (names), PayoffStep, Counter), so the meter can show "step 3 of 5" from the start.
- **`ComboWatch`** (AI/Playbooks; attach with `new ComboWatch(playbook)` before the duel): Started, PaidOff, Fizzled,
  FirstRoute, FizzledRoute, FizzledAt (steps it had played), FizzledOf; `Broken` = a route fizzled and none paid off.
  With the duel's context (`new ComboWatch(playbook, context, seat)`): `Stopped` = a fizzle with a chain link of the
  opponent's while the route was live, the window capped at the end of the route's own turn (a link on the next turn
  isn't against it); the duel ending (a win message, a concession, the turn cap) with a route live, unpaid and acted
  against counts as that fizzle (`DuelOver()`), so interrupting and winning in the route's turn earns it.
- **The payoff is confirmed as it resolves** (review fix, 2026-11-02): the payoff step chosen signals Step; `Payoff`
  (and Ended's `Finished`) only once its chain link resolves un-negated (`MSG_CHAIN_NEGATED`/`DISABLED` before its
  `CHAIN_SOLVED` is a negation; a Continuous/Field/Equip Spell or Trap that leaves before it resolves too; a win while it
  resolves lands it), or its summon goes through (`MSG_SUMMONED`/`SPSUMMONED`, not the card sent off the field). A
  negated payoff is a fizzle at its step for the show, ComboWatch, the poster and `--combo-interrupt`. The playbook hears
  the duel through `DuelContext.Observed` (new event, raised after each message). Play is unchanged (`--turn1` boards of
  fourteen playbook decks identical). It also stops counting a payoff the bot chose and then skipped (the blunder check:
  Chaos Emperor's 1000 LP with 1000 left; 4 of 300 Yata Mags goldfish games).
- **Combo Breaker:** `BackAlley.Record(..., bool comboBroken = false)` (last parameter, default false): with a win at a
  headliner's seat (closer or wildcard, never an encore) it adds `HeadlinerState.Breaker`; the first time it raises
  `BackAlley.ComboBreakerEvent + id` ("combobreaker:<id>", whose scene is hl-<id>-breaker), sets `AlleyResult.ComboBreaker`
  and adds "Combo Breaker! <handle>'s poster is yours" to the summary. Posters: `Posters.Breaker(id, handle, card, ink)`,
  `Posters.BreakerFor(id)`, ids `"breaker:<id>"` (`Posters.BreakerPrefix`), title COMBO BREAKER, earned by `Breaker > 0`;
  one per ready headliner, before brawl-bill (so `Posters.All[^1]` is still midnight-bill). HeadlinerTest holds the
  posters and the ready headliners equal: readying one means adding its `Breaker(...)` line.
- **Watch the combo:** `HeadlinerReplays` (Replays): `PathFor(campaignFolder, id)`, `Load`, `Has`; the clips are
  campaign/replays/<id>.txt (the build copies campaign/**/*.txt), written by `--combo-goldfish <id> --save-replay`
  (the finished duel won on its payoff turn, soonest; checked to play back the same before it's saved). Seat 0 is the
  headliner (Names[0] its handle, Characters[0] its id), seat 1 "The stand-in" on decks/classic.ydk; Settings.Seed is the
  bot's seed (the dummy's is one more), as the live clip needs.
- **Headliners on their own recipe:** a headliner whose route its base deck's other copies mustn't play runs
  `#recipe <id>` (its playbook is looked up by its own id everywhere, the app's duel and clip included) and
  `#rating <deck>` (a new deck note: the rated deck its panel Elo shows; `BackAlley` reads it for a headliner's Elo).
  gateway-gwen (#rating six-samurai), master-plan-marlo (spyral), gumblar-gail (gouki), trishula-trish (plant-synchro),
  return-rook and scientist-turtle (no rating: Elo 0). The base decks keep their playbooks unchanged (spyral's
  `SpyralPlaybook()` has no route; `SpyralPlaybook(masterPlan: true)` and `SixSamuraiPlaybook(gateway: true)` are the
  headliners').
- **Profiles:** `Personality` now reads `combo:`, `payoff:` and `fizzle:` into `Lines` (it ignored them before), so
  `who.Lines["combo"]` has the headliner's lines.
- **For playbooks** (additive; only decks that use them change): `TakeAll(hints, source, codes)` (an "up to five"
  selection takes as many of your own as it allows: Soul Release), `ChooseOption(code, index)` (an option prompt picks
  that effect description: Double Helix's "Special Summon it"), `Route.ActivateEffect`; `PlaybookView` adds
  BanishedMonsters, Banished, BanishedAny, CardsBanishedThisDuel, SpecialSummonsThisDuel/ThisTurn, OpponentGraveyardCount,
  OpponentBanishedCount/Monsters, GraveyardMonstersOfAttribute, FaceUpLevels, FaceUpCodes, FreeMonsterZones, FieldLevels,
  InExtraDeckAny, IsMyMainPhase1; `DuelContext` counts SpecialSummonsTotal and BanishedTotal per player.
- **Data:** the five new headliner blocks (list:, pieces:, stakes:), Gateway Gwen in The Basement Game's
  `headliners:` (Trishula Trish no-go), ready: yes for gateway-gwen, master-plan-marlo and gumblar-gail; return-rook,
  trishula-trish and scientist-turtle stay ready: no (Nyx's seat 3 keeps its stand-in). Profiles for all, story.txt's
  hl-* scenes and every -breaker scene, Gwen's tell.

**As built (Phase D, Unity, 2026-11-02).** The show layer (HEADLINERS Phase 2, and Phase 3's Unity parts), built on
Phase 1's route signals and the engine's Phase D additions above (`ComboWatch`, `Record(comboBroken:)`,
`Posters.BreakerFor`, the signals' `Counter` / `Count` and `RouteEvent.Count`, `HeadlinerReplays`):
- **Where it plays:** any duel with a headliner in a seat (its profile id names a headliner in alley-events.txt): its
  table, a wildcard, an encore, Nyx's Table, the exhibition, a combo clip. `ComboShow` (Unity) takes the seat's
  `Playbook.RouteSignaled` (queued, staged after each step's events) and the duel's events. A route still on when the
  turn moves on is over: paid off, it finished; else it fizzled "at step N" (the step after the last it played). The
  show doesn't wait for the playbook's own Ended (that comes at its next decision, which may be a turn later).
- **The banner** ("MOSS IS GOING FOR IT" / "FROG FTK", Baz's `baz:combo[:<id>]` line under it, {handle} the short name in
  caps), the sting (`ShowSounds`, synthesized in Unity), the headliner's own `combo` line in a bubble. Reduced motion:
  the banner shows still. **The meter** sits at the top of the callout column (the callouts and the chain stack under
  it): route and handle, "STEP 3 OF 5" / "PAYOFF" / "FINISHED" / "FIZZLED AT STEP 5", the steps as pips, the running
  total: the route's own counter from its signals (a damage counter is read off the opponent's LP since the start, exact
  when the duel ends mid-loop), and from the duel's events once the signals stop; a route without one counts damage
  (Frog FTK), cards to the GY (the Yata routes) or summons. A route that finishes at its payoff step while the rules play the loop on (Frog
  FTK) keeps counting, and keeps the slow-down, until the turn ends. **The payoff:** a "PAYOFF!" stamp with the step's
  name and Baz's `baz:payoff`, the headliner's `payoff` line, the crowd's roar, and the duel held (about 1.8 s at Normal speed). **The fizzle:** a
  "FIZZLED" stamp, two `baz:fizzle` lines from different speakers (Baz and Mika), the crowd's groan, the headliner's
  `fizzle` line.
- **Slow down for combos** (Settings, Duel presentation; `fxSlowCombos`, default On): Normal speed (4/s) while the combo's
  turn is on, whatever your speed; Off keeps yours.
- **Combo Breaker:** the seat-1 headliner's playbook gets `new ComboWatch(playbook, context, 1)`; `FinishAlleyDuel` passes
  `comboBroken: o.IsHeadliner && ComboWatch.Stopped` to `Record` (the engine's window and its duel's-end count cover a
  win before the playbook decides again; the show's own count is gone). The results list "Combo Breaker!" when
  `AlleyResult.ComboBreaker`. The payoff is staged as it resolves (the hold follows it, and the route's end, which can
  come in the same engine step, waits behind the beat: `ComboShow.Take(holdAtPayoff:)`), so a negated payoff stages the
  fizzle, never PAYOFF!.
- **Skips and replays** (review fix): skipping the opponent's turn stages nothing after the fact (no banner, stamp, hold
  or calls when your turn comes): the show takes the skipped moments quietly (a log line for a start and its end, the
  meter as it ended), and a duel that ends in the skip ends the show. A replay starts with the show cleared.
- **The exhibition** (Baz's chalkboard, under your deck; the chalkboard now also opens with the first headliner you
  meet): a headliner you've met (`Met` or `Beaten`) against another, or against tonight's regulars at the open Street and
  Den tables; arrows or Random, then Watch: a coin toss and CPU against CPU in the duel view under the turn cap, the
  alley's music and top bar. Nothing is held, recorded or paid (no DP, no duel counted, no bounties, no posters, not in
  recent replays); its calls and a clip's don't mark lines heard (`ShopVoice.Peek`). Neither starts over a live alley
  duel (the board's Menu settles that first; a guard refuses it anyway).
- **Watch the combo** (an Unlisted headliner page): the headliner's goldfish clip played again *live*, the headliner's
  bot (`SmartDuelist`, its playbook, no personality, as `--combo-goldfish`) against `DummyDuelist` with the clip's seed,
  first player and decks (`HeadlinerReplays.Load`), so the banner, meter and payoff play; a plain replay carries no route
  signals. With no saved clip, Phase 1's finishing goldfish game (Moss seed 8, Yata Mags seed 37, × 7919). Back to the
  page after. Not saved to recent replays.
- **Also:** `CampaignState` loads the deck ratings with the peak builds' file merged (`DeckRatings.PeakFileName`, NL-0).
- **Checks:** `-alleycheck` steps 16-18 (DuelController.ShowCheck): Moss at The Basement Game's seat 4 with a set hand,
  your set Mask of Restrict activated while the route plays, stopping the shot (fizzled at step 5; the duel plays on
  until the engine's ComboWatch has the fizzle, then Moss concedes: the Combo Breaker; tried up to four times, since
  Giant Trunade, a draw away, can bounce the Mask); 16b, an encore with your Set Dust Tornado played only on Mass Driver's
  first shot (the payoff), destroying Mass Driver before it resolves: no PAYOFF!, a fizzle at step 5, ComboWatch
  Stopped and the Combo Breaker's test passed, no second poster; Moss's encore through
  its payoff (the meter counts the whole turn-1 kill); the exhibition; Watch the combo. `-themecheck` shoots the banner,
  the payoff and the fizzle in both themes (the alley pass under Reduced motion).

**As built (Phase D, the difficulty pass, 2026-11-05).** Measured and decided in docs/dev/ALLEY-DIFFICULTY-2026-11-03.md
(every number, the alternatives rejected, the owner's decisions). Shipped in e2a95d5: Nyx's seat 2 is thunder-dragon as
built (56% for the QA deck, was chaos-yata at 93%), seats 1 and 3 at skill 1.0; Dumpster Row's closer is warriors. Recorded
as off target and accepted by the owner: Dumpster Row (97.7% best / 33.7% starter with the QA deck), Nyx's seats 1 and 3
(about 70-75%), the Alley Brawl after The Black Market (46.4% / 49.4% with NL-1's out-of-towners). The Black Market's own
pass and the regulars' event skills are parked for a later difficulty and CPU pass (TODO).

## 17. Owner decisions

The recommendation comes first; build it unless told otherwise.

**Decided (owner, 2026-11-02): every recommendation below is accepted.**
- ALLEY-ECONOMY's seven decisions, as written.
- ALLEY-STORY: the Ines payoff (`heldcard`, her Scapegoat held on Moth's rail, Ines out of the Midnight Open);
  Penny's streak can break and her card is Kuriboh; Nocturne's name stays hidden; Baz's `{nick}` ladder; Gus never
  comes down; **Moth is he/him**.
- O1-O8 on the recommended side. O4: the first-turn guard returns the stake **at every alley table**.
- Assets: the stencil font and portraits are the owner's to supply later; build with the code-drawn fallbacks.

**ALLEY-ECONOMY's seven decisions** (held stakes; `StakeFloor` 1.0; Orvel's card rule; the slab in the Contraband
Pack; Brawl packs 2 / 1; Midnight Open packs 4 / 2 / 1; no Contraband Packs for sale) are this spec's defaults. This
spec agrees with all seven and builds them; the owner confirms them in that doc.

**ALLEY-STORY's conflicts** are built on its recommended side, pending the owner (its "Owner questions" 1-2): Ines
out of the Midnight Open field (six-samurai in); plain `bracketwin` for Locals only; `heldcard` in place of
`inescard` (Ines's card is held on the rail, never for sale, and goes to Ines). Its other questions (Penny's streak
and Kuriboh, Nocturne's name, Baz's `{nick}` ladder, Ines's worn Scapegoat on her board, Gus never coming down, Moth
"he") are story calls the build follows either way; only `{nick}`'s names and Penny's card touch data.

The items below are this spec's own, or where it differs.

1. **O1. Orvel's Table.** Opens at The Black Market's clear (with Nyx's Table and the Midnight Open); one seat, Orvel
   with an authored house deck (power-patron plus Black Market contraband); repeatable, a new stake each sitting;
   target 55-60% for your best deck. *Or open it at The Basement Game's clear, as a Den-and-up side table.*
2. **O2. Nyx's Table's lineup.** Den stakes (as ALLEY-ECONOMY has it), four seats: a Black Market regular, chaos-yata
   as built, the burn-kill headliner (or a regular at 0.97 until it passes), Nyx's Gravekeeper's Contraband. *Or one
   boss seat only (ALLEY-ECONOMY costed Nyx's Table as one match).*
3. **O3. Loaners in the alley.** None: your own decks only (the Alley list is so open that almost any deck you own is
   legal, and a stake should ride on your deck). *Or loaners, with the stake held as usual.*
4. **O4. The first-turn guard everywhere** (where this spec differs from ALLEY-ECONOMY, which gives the stake back
   only for a headliner's first-turn kill). Every alley table, since a first-turn loss is under 2% by target and
   costs you a stake you never got to defend. It returns the stake, so it can't be DP-positive. *Or headliners only.*
5. **O5. Midnight Open seeding.** Ilara Gale seed 1, you seed 2 (you meet her only in the final). *Or seed by
   rating.*
6. **O6. Alley Brawl's field never comes from The Black Market** (ALLEY-ECONOMY Evidence 9: 7% title odds for a
   typical player there): Dumpster Row's regulars, then The Basement Game's. *Or the highest cleared table, as the
   plan's "your highest cleared event" reads.*
7. **O7. "Cyber-Jar Wick"** (F10). Add Cyber Jar to Dumpster Row's curated list after an `--alley-rate` run, or drop
   the handle.
8. **O8. A token for a table's first clear** (`ClearTokens` 1). Not in ALLEY-ECONOMY; the Economy Designer confirms.
   *Or nothing but the cards won.*
9. **Assets.** The stencil font (needed for polish only) and, optionally, portraits for Moth, Baz, Odile and Penny and
   one hooded-silhouette PNG (section 13).
