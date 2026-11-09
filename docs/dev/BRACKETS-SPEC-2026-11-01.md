# Brackets spec: Library Locals (POST-GAME-PLAN step 7)

A Game Designer agent's spec, 2026-11-01, written against the code as it stood. Only Library Locals for now:
Alley Brawl and the Midnight Open wait for the Back Alley (step 9). Values marked [P] are placeholders until measured.

## Findings that shape it

- **The Phase 0 field ignored era.** Phase 0 built a Locals field as "the cup's 4 decks plus the 3 rated decks nearest
  the boss" (`AlleyStudy.BracketSim`), which ignores era. Elo odds are also off by about 85 points against
  head-to-head results.
- **Early cups have almost no extra decks:**
  - The `#era` tags in each `.ydk` leave Goat Cup with nothing outside its own four decks.
  - TeleDAD has only older decks (chaos-yata, goat-control).
- **So fields are written by hand and checked by the harness,** not picked at runtime.

## 1. Data

- **`local:` lines:** add `local: <character> <deck>` to `campaign/library.txt`, 3 per cup, in the same syntax as
  `match:`. Parse them into a new `Circuit.Locals`.
- **`--locals-check` rules** for each line:
  - the deck is rated;
  - at least 90% of its cards are released by the cup's `pool:` month (`DeckRules.Released`; CPUs ignore ban lists);
  - its `#era` is at or before the cup's `era:`;
  - the deck is in the pilot's `decks:` list;
  - no character appears twice in a field.
- **Mirrors:** where an era has too few decks, a cup deck can return with a second pilot (e.g. `tink gadgets`). The
  check marks these as mirrors.
- **One Elo parser:** move the Elo regex (copied in 5 harness files and `CampaignState`) into a single engine helper,
  `DeckRatings.Parse`.

## 2. Engine (`src/Ygo.Engine/Campaign/Brackets.cs`)

- **`BracketEntrant`:** `Character`, `Deck`, `Elo`, `You`.
- **`Bracket`:**
  - fields: `Kind` ("locals"), `Event` (cup id), `Name` ("Goat Cup Locals"), `uint Seed`, `Slots[8]` in bracket
    order, `Winners[7]` (slot index or -1; matches 0-3 are quarter-finals, 4-5 semi-finals, 6 the final), `Loaned`,
    `LiveMatch`;
  - helpers: `Pair(m)`, `YourMatch()`, `YouOut`, `Placement()` (0 winner, 1 finalist, 2 semi, 3 quarter), `Done`.
- **`Brackets` constants:** `LocalsEntry = 1000`, `LocalsPay = {900, 650, 400, 150}` [P], `LocalsPacks = {2, 1, 0,
  0}`, `Sink = "brackets"`.
- **Seeding:** you are seed 1, as the cup's champion. The 7 CPUs are seeds 2-8 by Elo, highest first (ties by deck
  name; unrated counts as 1500). Bracket order is [1, 8, 4, 5 | 2, 7, 3, 6], so seeds 1 and 2 meet only in the final.
- **Methods:**
  - `LocalsField(library, cup, ratings, roster)` and `Create(...)`.
  - `EnterBlocked` refuses when the cup isn't cleared, you have under 1,000 DP, or another bracket is running.
  - `EnterLocals` takes the fee with `profile.Spend(Sink, 1000)`. Seed = `Hash.Mix(Collection.Salt, 0xB7AC ^ cup,
    BracketsEntered++)`.
  - `Odds(a, b, matchups)`: Elo p = 1/(1 + 10^((Eb − Ea)/400)). Once `campaign/matchups.txt` exists, p = (wins +
    20·pElo)/(played + 20). Clamp to [0.03, 0.97].
  - `Coin(seed, m) = Hash.Mix(seed, 0xC014, m) / 2^32`. Every coin is fixed at entry, and a CPU match goes to side A
    when coin < p.
  - `SimulateRound(b, round)` and `SkipToEnd(b)`.
  - `SettleWatched(b, m, side)`: a watched final's real result counts; a drawn one falls back to the coin.
- **`RecordYours(profile, library, b, winner, loaner)`:**
  - A draw changes nothing and the match is replayed.
  - Any result calls `NoteResult("locals:<cup>.<m>")`, updates Wins/Losses, gives EXP as a repeat Library duel, and
    pays no DP.
  - Then the round's other CPU matches are settled.
  - A loss pays your placement at once (half if `Loaned`).
- **`Finish(...)`** returns a `BracketReward` (DP, packs, events) and moves the bracket into history.
- **Events:** `bracketwin` (the first title anywhere), `bracketwin:<bracket name>`, and `level:n`.

## Prize Pack (`PrizePacks.Open`, 5 pulls, revealed least rare first)

1. **Slots 1-3:** different cards from the 7 CPU decks (base codes, released by the pool), at a printed rarity of Rare
   or better. Rarity is weighted by 10000/`Pricing.Unit(r)`, as `UsedMarket` does.
2. **Slot 4:** one card from a real tournament set in printings.bin, at its printed rarity:
   - names matching `OTS Tournament Pack \d+` (not "(POR)"), `Tournament Pack \d+`, `Tournament Pack: \w+ Season`,
     `Turbo Pack: Booster \w+`, `Ultimate Tournament Pack \d+`;
   - not Speed Duel;
   - the set dated by the pool month, else the earliest such set.
3. **Slot 5:** a sealed slab, Grade 9 at 75% or Grade 10 at 25% [P], from the slot 1-3 pool at Super Rare or better.
   `Slab = Floor = Grade`.

Prize cards get a new `CopySource.Prize` (appended to the enum), graded at the foil odds, `Paid = 0`. The Sell desk
pays min(V/4, Paid/2), so prizes return 0 DP and the loop never makes DP.

## 3. Save lines

- **The lines:**
  - `bracket <kind> <event> <seed> <loaned> <live>`
  - `bracketslot <i> <char|you> <deck> <elo>` × 8
  - `bracketresult <m> <slot>`
  - `brackets <entered>`
  - `locals <cupId> <entries> <titles> <best>`
- **When it saves:** at entry (no refund), at the start of your duel (`live = m`) and after every result.
- **Live duels:** a save that loads with `live` set replays that match. (The spec offered "counts as a loss" as the
  alternative; replaying is kinder in a solo game. This is the owner's call.)
- **Prize cards** go into the collection when won; there's no unopened-pack state.
- **New Game+** keeps the bracket and its history.

## 4. Unity flow

1. **Cup panel** (`CampaignScreen.ShowCup`): a Locals button in the header. Its label:
   - "Clear to unlock" (disabled);
   - "Locals · 1,000";
   - "Locals: Semi ▶" in gold while this cup's bracket runs;
   - disabled "<cup> running" when another cup's bracket runs.
2. **Entry dialog:** the 8 seeds with Elo, the cup's list and pool, "Your deck must be legal each round; loaners
   halve placement DP", the prize ladder, and Enter / Cancel.
3. **`BracketScreen`** (new, 1920×1080 reference):
   - five columns of 300×96 slots at x = 60, 435, 810 (final), 1185, 1560;
   - slots: an 80px portrait, name, deck, "#3 · Elo 1,612", and a state chip:
     - pending: "—";
     - ▶ YOUR MATCH: a red border and the screen's one red Duel button;
     - won: ♛, and the path lights gold;
     - OUT: dimmed to 45%;
   - controller: A = Duel or Scout, Y = Skip to end (only once you're out), B = Back (the bracket is kept).
4. **Duel:** `ShowBracketVersus` reuses `ShowCupVersus` with `CupRules(cup)`, loaners allowed, Back returning to the
   bracket.
   - A new `NewDuel(..., bracketMatch: m)` parameter and a branch in `AnnounceResult` call `RecordYours`.
   - Your duel seed is `Hash.Mix(seed, 0xD0E1, m*16 + attempt)`.
   - After a result, the bracket animates the round's other results at 0.4 s a match.
5. **Knocked out:** "Out in the quarter-finals · +150 DP", then **Skip to the end** (primary) or **Follow the
   bracket**. At a CPU final, **Watch the final** plays a real CPU-vs-CPU duel that counts, or Skip.
6. **Prize reveal:** `ShopScreen.OpenPrize(title, pulls)` reuses the pack-opening overlay, then returns to the
   bracket and the cup.

## 5. DP (a sink that pays in cards)

| Placement | Duels | Pay | Packs | Net DP |
|---|---|---|---|---|
| Winner | 3 | 900 | 2 | −100 |
| Finalist | 3 | 650 | 1 | −350 |
| Semi-final | 2 | 400 | 0 | −600 |
| Quarter-final | 1 | 150 | 0 | −850 |

## 6. Story

- **`locals`:** the first entry; Hollis explains the format.
- **`bracketwin`:** Gus pins up the first bill (the "Bracket bill" poster hook).
- **`bracketwin:<name>`:** optional scenes per cup.

## 7. Checks

- **`--locals-check`:** the rules in section 1.
- **`--campaign-test`:**
  - entry blocks and the fee;
  - the field has 8 distinct pilots, with you as seed 1;
  - the same salt and entry count give the same bracket;
  - a draw replays; a loss pays and ends your run; a title pays 900 and 2 packs and raises both events;
  - the save round-trips mid-bracket.
- **`--prize-check`:** 1,000 packs per cup:
  - every slot is filled;
  - slots 1-3 are Rare or better from the field;
  - slot 4 comes from the right sets;
  - slabs split about 75/25;
  - 0 DP comes back;
  - it prints the average pack value.
- **`--bracket-sim`:** rebuilt on `LocalsField` and `Odds`, with your matches taken from library-advice.txt's measured
  rates. Target: 30-45% title odds.
- **`-campaigncheck`:**
  - the button states and the dialog;
  - no overlapping slots;
  - Duel opens the versus under the cup's rules;
  - Skip to the end;
  - a 5-card reveal.

## 8. Build order

1. Engine (`DeckRatings`, `local:`, `Brackets`, `PrizePacks`, `CopySource.Prize`, save lines).
2. Harness (`--locals-check` and the 36 `local:` lines, then `--campaign-test`, `--prize-check` and `--bracket-sim`).
3. Unity (the cup button and dialog, `BracketScreen`, the bracket versus, result and watched final, `OpenPrize`).
4. Story scenes, `-campaigncheck`, then a QA play from a champion save.
