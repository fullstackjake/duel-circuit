# Shop prices, measured (2026-11-01)

The Economy Designer's measurement of the price changes in [PACKS-REVIEW-2026-11-01.md](PACKS-REVIEW-2026-11-01.md)
section 3, with the tools from [PROOF-2026-11-01.md](PROOF-2026-11-01.md). It's checked against the targets in
[POST-GAME-PLAN-2026-10-31.md](POST-GAME-PLAN-2026-10-31.md) ("## Targets", and "How hard building a specific deck
should be"). It also reads [SHOP-REWORK-SPEC-2026-11-01.md](SHOP-REWORK-SPEC-2026-11-01.md): house packs, the catalog
fix (A0), rarity sets (A1) and booster boxes.

Nothing is switched on. Every knob is in `src/Ygo.Engine/Campaign/ShopPrices.cs`, and its defaults are today's prices.

## Verdict

**Ship four of the five changes, two of them with different numbers:**
- **Selection (house) packs priced by band, at half a recipe pack, not two-thirds.**
  - At two-thirds, buying any house packs before a recipe costs *more* than recipe packs alone (8-17%), so they become a
    trap.
  - At 200 flat (today), a sharp player builds Advanced-Master decks for 33-57% less, which is the leak.
- **Novice recipe packs at 450, as proposed.**
- **Real boosters by year, as proposed.**
- **Rarity sets priced by their base rarity, not a flat 2×.**
- **Booster boxes: 20% off, the Classic guarantee, and refunds capped at half.**

No loop goes DP-positive at any of these prices.

## The recommended prices

| Knob | Today | Recommended |
|---|---|---|
| House pack (`SelectionByBand`) | 200 flat | Beginner 200, Novice 200, Intermediate 300, Advanced 500, Expert 750, Master and Gauntlet 1,250 |
| Recipe pack, Novice (`NoviceRecipePack`) | 300 | 450 (Beginner stays 300; Intermediate and up unchanged) |
| Real booster (`BoosterByYear`) | 150 | up to 2008: 150; 2009-13: 200; 2014-18: 300; 2019-22: 450; 2023 on: 600 |
| Rarity set (`NoCommonsByBase`) | 1× | Rare base 1.5×, Super base 2.5×, Ultra or better 3.5× (of its year's price, to the nearest 50) |
| Box discount (`BoxDiscount`) | none | 0.20 |
| Box guarantee (`BoxGuarantee`) | none | Classic: the set's rarest up to Secret Rare |
| Box refund cap (`BoxRefundShare`) | none | 0.5 of the box price |

**Example prices:**
- **2023-on set:** pack 600, box 11,520; a Rare-base rarity set 900, Super-base 1,500, Ultra-base 2,100.
- **2019-22 set:** 450 (box 8,640); 700 / 1,100 / 1,600.
- **2014-18 set:** 300 (box 5,760); 450 / 750 / 1,050.
- **2009-13 set:** 200 (box 3,840); Hidden Arsenal (Super base) 500.
- **Up to 2008:** 150 (box 2,880).

**To switch them on** (one place, `ShopPrices`):
- `SelectionBandPricing = true`, with `SelectionByBand = { 200, 200, 300, 500, 750, 1250 }`;
- `NoviceRecipePack = 450`;
- `BoosterYearPricing = true`;
- `NoCommonsByBase = { (UltraRare, 3.5), (SuperRare, 2.5), (Common, 1.5) }`.
- The box knobs already default to the recommendation (nothing sells boxes yet).
- In the harness: `--prices sel=200/200/300/500/750/1250,novice,year,foiltier=1.5/2.5/3.5`.

## How it was measured

- **`--shop-probe` (new):** the cost of completing each band's decks at the given prices. It's deterministic and takes
  10 seconds.
  - **Paths:** recipe packs only, house packs only, the best booster in the band's shop years (first pack), outright,
    and **the mix**. The mix is house packs while they bring more missing copies per DP than a recipe pack would, then
    recipe packs to finish. It's what a sharp player does, and where the leak lives.
  - **The collection:** `--snapshots` starts each band from the campaign sim's collection as that band starts (Beginner
    to Expert). Master and the Gauntlet start from the starter collection, since the sims hadn't reached them.
  - `--a0` uses a rough version of the spec's catalog fix; the rarity-set and box sections open packs with A1.
- **`--campaign-sim` with `--prices`:** the main campaign with real duels and the ledger per band, run for baseline,
  each change and the combinations.
- **`--postgame-sim`** (the four personas from the champion save) and **`--arbitrage`** for each variant.
- **Output:** `build/shop-prices/` (`probe*-*.log`, `campaign-*.log`, `postgame-*.log`, `arbitrage-*.log`).

## Evidence 1: completing a deck, per band

From the sim's collection at the band's start; net DP after refunds. "Wins" means first-win pay (win DP, the first-win
bonus and a share of the circuit prize).

| Band | First-win pay | Recipe packs only | Mix today (200 flat) | Mix, two-thirds | Mix, recommended |
|---|---|---|---|---|---|
| Beginner | 762 | 1,450 (38 a card) | 1,472 | 1,472 | 1,472 |
| Novice | 1,125 | 1,793 → **2,690** at 450 (35 → 53 a card) | 1,806 | 1,806 (at 300) | 2,377 |
| Intermediate | 1,500 | 2,980 (73) | 2,586 (**13% under**) | 3,368 (13% over) | 2,916 (2% under) |
| Advanced | 1,875 | 4,980 (116) | 3,327 (**33% under**) | 5,401 (8% over) | 4,874 (2% under) |
| Expert | 3,744 | 7,490 (178) | 4,871 (**35% under**) | 8,797 (17% over) | 8,008 (7% over) |
| Master* | 4,881 | 17,453 (317) | 7,505 (**57% under**) | 16,353 (6% under) | 14,953 (14% under) |
| Gauntlet* | 6,538 | 17,453 (312) | 9,519 (45% under) | 17,191 (2% under) | 17,321 (1% under) |

\* From the starter collection. From a real collection, Master comes out cheaper: see "A Master deck from a real
collection" below.

- **The leak is real, and it's the mix.** Today a Master deck costs 7.5k (1.5 wins) instead of 17.5k, under the plan's
  15-30k target.
- **The recommended prices close it.** The mix lands within about 15% of recipe packs at every band, and Master comes
  to about 15k (3.1 wins), on the target's floor.
- **Two-thirds overshoots.** From Intermediate to Expert, any house pack bought before beating the deck loses DP
  against recipe packs alone.
- **House packs alone are never the way to finish a deck** at any price (coupon collecting). A Master deck from house
  packs alone takes 253 packs. That's by design: once the deck is beaten, it moves to recipe packs.

**Duels to complete a recipe, against the plan's table:**
- **Novice:**
  - The sim earns about 890 DP a duel there, not counting refunds.
  - At 300 a deck is about 2 duels and 12% of the band's earnings. At 450 it's about 3 duels and 18% (2,995 DP from the
    starter collection).
  - The target is 6-10 duels, 3,000-6,000 DP and 25-40%. 450 reaches the DP floor but not the duels: the sim's casual
    pilot wins most Novice matches first try. 600 would make the Novice→Intermediate step flat.
  - Recommend 450, and re-check against telemetry.
- **Master, from the starter collection:**
  - Recipe packs only: 17.5k, about 3.6 first wins. With the recommended house packs about 15k.
  - Inside the 15-30k target, and inside 4-8 duels once losses are counted.

**A Master deck from a real collection** (added 2026-11-02): the `all` run's collection as Master started (1,246
different cards). Probed on the current build, which has the real A0 catalog (229 sets) and still defaults to today's
prices.

| Path | Today | Approved prices |
|---|---|---|
| Recipe packs only (43 missing copies) | 12,490 (2.6 first wins) | 12,490 (recipe packs don't change) |
| The house-then-recipe mix | 7,023 (44% under recipe packs) | 13,531 (8% over) |
| Best booster per useful card | 243 | 973 |

- **The approved house-pack price closes the Master leak** on a real collection too: 7.0k becomes 13.5k.
- **Flag: a Master deck is under the plan's 15-30k target from a real collection.** Recipe packs alone come to 12.5k,
  because a player owns about a fifth of a Master deck's cards by then.
  - This is the Master recipe pack's price (2,500, decided 2026-10-31), not one of the approved changes.
  - If the floor matters, about 3,000 a pack (and the service fee that follows it) puts recipe packs alone at about
    15k.
  - One collection, from the review-prices run, which owns fewer cards than baseline. Treat it as a single sample.

**Cost per useful card, at the recommended prices** (DP per missing copy brought in):
- **Recipe packs:** 38 / 53 / 73 / 116 / 178 / 317 from Beginner to Master.
- **The mix:** 39 / 47 / 71 / 113 / 191 / 272.
- **The best booster** (first pack, in the band's shop years): 257 / 1,091 / 509 / 818 / 973 / 706.
  - Today: 257 / 643 / 257 / 257 / 250 / 176.
  - With the A0 catalog: 257 / 750 / 391 / 391 / 923 / 600.
- **Boosters stop competing for deck building:** they cost 4-7× a recipe pack per useful card at every band. Today a
  booster beats a recipe pack at Master (176 against 317).

## Evidence 2: the main-campaign sim

Seed 1, casual pilot, ledgers per band in `build/shop-prices/campaign-<variant>.log`.

**The bot's limits:**
- **It spends everything after every duel,** so "spent ÷ earned" is about 100% in every variant. That's the bot's
  policy, not the prices. The spend mix and the refunds are what move.
- **It never bought a Novice recipe pack.** Its target picker prefers the historic recipes it owns most of, so the
  `novice` variant was identical to baseline through Intermediate, and was stopped. Change 2 is measured by the probe
  only.

**Spend by sink, Advanced band:**

| Variant | Earned | Of which 4th-copy refunds | Selection packs | Boosters | Useful copies from selection, DP each net of refunds |
|---|---|---|---|---|---|
| Baseline | 38,168 | 13,680 (36%) | 36,200 (95%) | 1,950 | 104, about 217 |
| Selection by band (two-thirds) | 23,056 | 1,268 (5%) | 17,200 (75%) | 5,850 | 59, about 270 |
| Boosters by year | 37,006 | 13,493 (36%) | 36,000 (97%) | 1,050 | 107, about 210 |
| All four (review) | 24,682 | 1,132 (5%) | 20,200 (82%) | 4,500 | 44, about 433 |
| Recommended | 25,387 | 2,212 (9%) | 22,500 (89%) | 2,850 | 62, about 327 |

- **Flat selection packs churn.** The bot opens packs whose cards it mostly owns, and a third of its Advanced "earnings"
  are 4th-copy refunds. Band pricing ends the churn (refunds 5-9%).
- **The early bands barely move:**
  - Beginner spends 12-13.5k in every variant: selection 38-49%, boosters 28-39%, recipe packs and outright 17-24%;
  - Novice and Intermediate spend 15-21k in every variant, mostly boosters the bot can't use.
- **Decks finished:** 4-6 at Beginner and 1-3 at Advanced in every variant (ryzeal at duel 74-86, the same deck the
  bot aims at everywhere).
- **The wall fallback:** no run hit a 15-try wall. The wider re-pick after 3 losses fired 6-8 times a run.
  - In Rituals and Fire, the runs won matches 1-3 in 2-14 tries (`rec` won match 1 on try 14, with a loaner).
  - Baseline cleared the whole circuit (4/13 duels), past match 4, the old stall.
- **House packs (the rework's narrowing, `YGO_SIM_HOUSE=1`):**
  - At flat prices the bot still put 38-45% of Beginner and Novice spending into house packs, and finished a Novice
    deck (geargia) it never finished before.
  - At Intermediate and Advanced it bought no house packs at all, in either price variant (boosters 98-100%), and
    finished no deck there. Without the narrowing it finished 1-3 Advanced decks, all through flat selection-pack
    churn.
  - That's the bot's target picker (it aims at a historic recipe, never a beaten opponent's), not the prices. It does
    show that once house packs narrow, the late-band deck path is recipe packs after the win. The shop has to point
    there (the spec's Recipe Book button).
  - The flat and recommended runs earned and spent within 5% of each other in every band.
- **Expert, from the one run that finished it** (`all`, the review's prices: two-thirds house packs and the rest):
  - earned 63,711 and spent 62,650;
  - house packs took 49,600 (79%) and brought in 14 useful copies, about 3,500 DP each, buying next-band Master decks
    by house packs alone.
  - That's the trap from Evidence 1 in action: at two-thirds the bot pays about 12× what a Master recipe pack costs
    per card.
- **No run reached the end.** All seven stopped between 23:25 and 23:40 on 2026-11-01. The furthest was `all`, at
  Master League: Link Wars match 3. Baseline, sel and year were in Expert: Fall Format; rec and both house-pack runs in
  Expert: The New Wave.
  - None hit a 15-try wall. Every run cleared Rituals and Fire, the old stall.
  - There are no Master or Gauntlet ledgers and no normal-wallet champion save.

## Evidence 3: the post-game (champion save, 10 cups)

| Persona | Target | Today | Boosters by year | Recommended |
|---|---|---|---|---|
| Minimal | ≥ 15% spent | 19.3% | 19.4% | 19.3% |
| Collector | ≥ 40% | 70.7% (packs 115k) | 81.1% (packs 159k) | 81.7% (packs 181k) |
| Gambler | (none) | 39.7% (packs 30k) | 45.3% (packs 59k) | 57.3% (packs 107k) |
| Frugal | (none) | 0% | 0% | 0% |

- **Boosters by year make packs a real late sink.** Completing a 2023-on set by packs goes from about 15k DP to 60k+ (125
  packs, still incomplete), about 6 cups of a collector's post-game earnings.
- **Minimal and frugal players don't feel it.** Their spending is recipes and Make it yours, which don't change
  (Library recipe packs stay 1,000).

## Evidence 4: no loop makes DP

`--arbitrage` at baseline, each change, the review's combination and the recommended prices: **0 DP-positive loops
in all of them.**
- **Recommended:** boosters → refunds 49.2%, → Sell desk 49.2%, → grade → sell 43.0%; selection packs 48.5%; recipe
  packs 49.6%; the exhaustive 1-3 action search 26.7%.
- **Boxes** (probe, a finished set, a box at 25% off, refunds then the Sell desk): 27-49.8%. The cap holds because
  every copy's share comes out of the box price.
- `--campaign-test`: all ok at the defaults. With Novice at 450, one check fails by design (next section).

## Booster boxes

Measured on 10 core sets across the year brackets and the spec's watch list (RA04, RA05, BLGG and Battles of Legend),
150 boxes each, opened with A1:

| | Classic (rarest up to Secret) | Top (the set's rarest) |
|---|---|---|
| Boxes where the guarantee fires | 0-5% | 54-71% in sets with a Starlight or Ghost |
| Worth it adds per box | 0-220 | 550-1,700 |
| What it means | a floor nobody notices on average | a Starlight or Ghost sold in most boxes |

- **Classic.** Under Top the chase card stops being a chase: a Starlight is about 1 in 70 packs on its own, and Top
  makes it 1 per 24. Classic keeps "at least one Secret Rare in every box" true at no cost to the odds.
- **20% off.** A box's worth per DP is 25% above singles at 20% off and 33% above at 25% off. Boxes stay a collector's
  deal, not a deck-building one (still 4-7× a recipe pack per useful card).
- **Refunds capped at half the box price, as for packs.** The finished-set loop tops out at 49.8%.
- **The odds line** for a typical core set: about 7 Ultra Rares and 3.5-3.9 Secret Rares a box.

## Rarity sets (the spec's predicate and A1)

Fewer than 5 Common/SP cards, base = the lowest rarity with at least 5 cards, opened with A1. Worth is per pack,
against a core pack of the same years (A0 catalog, 61 rarity sets):

| Base | Sets | Worth vs core | Recommended price |
|---|---|---|---|
| Rare (Maze of Muertos, Wild Survivors, Maximum Gold...) | 17 | 1.3-1.7× (median 1.5×) | 1.5× |
| Super (Hidden Arsenal, Rarity Collection, Secret Slayers...) | 28 | 2.3-2.5× (median 2.5×) | 2.5× |
| Ultra or Gold (Battles of Legend, Premium Gold, Ghosts From the Past...) | 16 | 3.3-4.1× (median 3.9×) | 3.5× |

- **A flat 2× is wrong both ways.** It overcharges the Rare-base side sets by a third and undercharges Battles of
  Legend by half.
- **3.5× for Ultra base** is just under its worth, so the premium shelf stays a slightly good deal.
- **The deck-building side:** at 1× (today) rarity sets are often the best booster for a deck's staples. Priced by
  base, they're a collector's shelf like the rest.
- **A1 is confirmed:** every Rare-base set's cards are reachable (Wild Survivors 60/60 in 400 packs).

## The catalog fix (A0)

- **The rough A0** (pool by the prefix's most common printing name, whole-word filters, distinct card numbers for the
  80% rule) grows the catalog from 157 to 224 sets.
  - Up to 2008: 25 → 34 sets; 2009-13: 21 → 42; 2014-18: 37 → 58; 2019-22: 32 → 46; 2023 on: 42 → 44.
- **Most added sets are 2002-2018 cores at 150-300.** So year pricing leaves the back shelf cheap, as intended.
- **Effect on prices:** the best booster per useful card gets 10-50% cheaper from Novice to Advanced (the archetype
  homes come back), and is still 4-7× a recipe pack. No price needs to change for A0.
- **Re-run** `--shop-probe --prices <recommended>` after the real A0 lands.

## House packs (the narrowing)

House packs on sale as each band starts: today's rule, then the rework's.

| Band | Today | Rework, at the recommended prices |
|---|---|---|
| Beginner | 14 decks | 3 decks (200) |
| Novice | 16 (200-300) | 8 (200) |
| Intermediate | 16 (300-500) | 8 (300) |
| Advanced | 20 (500-750) | 8 (500) |
| Expert | 19 (750-1,250) | 8 (750) |
| Master | 7 | 8 (1,250) |

- **The narrowing halves what's on sale** and drops beaten decks, but it doesn't change what one deck costs. The leak
  per deck is the mix in Evidence 1, so band pricing is still needed.
- **The next-band jump mostly goes away.** A pricier next-band pack shows only during a band's last circuit (current
  + next). Under today's rule a whole band's worth sits on the shelf.

## Risks

- **Next-band packs jumping:**
  - In a band's last circuit the next band's house packs cost 1.5-1.7× the current ones (Advanced 500 → Expert 750,
    Expert 750 → Master 1,250).
  - A player who bought them at 200 before the change sees 3.75-6× (Master 1,250 against 200).
  - The house-pack row should show the band ("Expert deck").
- **New Game+:** the shop's era resets with the circuits (`BandEra`), so a New Game+ run pays the same ladder as the
  first one, from a bigger wallet. The tax is on post-game collectors buying 2023-on sets (600, or 900-2,100 for
  rarity sets), and that's the intended late sink.
- **The post-game collector of a normal wallet:**
  - Completing a 2023-on set by packs costs about 60-75k at the recommended prices, against about 11k earned per cup.
  - Fine for a prestige goal; too much if set completion is meant to be routine. Boxes (20% off) and the A1 fix (every
    card reachable) soften it.
  - If telemetry shows collectors stalling, lower 2023-on to 500 first.
- **Old saves:**
  - No save field changes.
  - Selection packs keep their code and sink (`packs.selection`).
  - Prices are computed, not stored, so a save simply sees new prices.
- **Novice players:** a second deck takes about 1.5× the DP. Beginner (300) is untouched, so the first recipe stays easy.
- **The test to update when Novice goes to 450:** `CampaignTest.cs` "recipe packs by band" checks
  `novicePack == RecipeShop.BasePackPrice`. Change it to `ShopPrices.NoviceRecipePack`.
- **The 4 stray-Common sets** (Maze of Millennia, Justice Hunters, RA04, RA05) are priced as rarity sets by
  `ShopPrices.NoCommons` (the spec's predicate) but still open as commons sets until A1. Ship A1 and the rarity
  pricing together.

## What the sims can't show

- **Whether players pick the mix.** The probe's mix is a sharp player. A casual one buying house packs until the deck
  is done pays far more (the "house packs only" column), and the shop's copy should steer them to recipe packs once a
  deck is beaten.
- **Feel:**
  - whether 600-DP boosters feel like a toll in the Expert and Master bands, where a first win buys about 6-8 packs
    (today 25-32);
  - whether a 2,100-DP Battles of Legend pack reads as premium or greedy.
- **The Novice duels target.** The casual pilot rarely loses at Novice, so its DP a duel is higher than a real
  player's. Real duel counts per deck will be higher than the sim's 3.
- **The normal-wallet post-game.** The post-game pilots start from the 354k QA champion, so their shares overstate what
  a normal wallet can spend.
- **Box behaviour:** how many players buy boxes over singles, and whether the Classic guarantee line is enough of a
  hook.

## Still to measure

- **Master and the Gauntlet in the main-campaign sim,** for every variant, and the house-pack runs past Advanced.
  - The seven measurement runs stopped in Expert and Master (see Evidence 2). Their partial logs are in
    `build/shop-prices/campaign-<variant>.log`, and the Master-start collection of the `all` run is in
    `build/shop-prices/snap-all/Master.txt`.
  - Re-run on the shipped code (the approved prices with A0 and A1), not the measurement build: the narrowed house
    packs change what the bot buys from Novice on. A rework run (`build/rework-economy`) was already going on
    2026-11-02.
  - The `rec` runs used rarity sets at 1.5/3/3, set before the A1 measurement moved them to 1.5/2.5/3.5. The bot almost
    never buys a rarity set, so it doesn't matter there.
  - To re-run:
    `YGO_SIM_SNAPSHOTS=<dir> dotnet build/bin/check/Ygo.Harness.dll --campaign-sim --seed 1 --prices <variant> --save <dir>`
    (add `YGO_SIM_HOUSE=1` for the narrowing).
  - Then run `--shop-probe --snapshots <dir>` for Master and Gauntlet costs from a real collection.
- **The probe for every band on the shipped code,** with the real A1, and the spec's box loop in `--arbitrage`
  (chunk C). The real A0 is already in the build: the catalog has 229 sets, against 224 for the rough fix.

## Files

- **New:**
  - `src/Ygo.Engine/Campaign/ShopPrices.cs`: every knob (today's values as defaults), `Selection(band)`, `Booster(set)`,
    the rarity-set predicate and base (`NoCommons`, `BaseRarity`, `CommonFloor = 5`, per the spec), and the box knobs
    (`BoxPacks`, `BoxDiscount`, `BoxGuarantee`, `BoxRefundShare`, `Box(set)`, `BoxTop(set)`).
  - `src/Ygo.Harness/ShopProbe.cs`: `--prices` and `--shop-probe`.
- **Changed:**
  - `Packs.cs`: sets are priced by `ShopPrices.Booster` once their pool is known.
  - `DeckAccess.cs`:
    - Novice recipe packs read `ShopPrices.NoviceRecipePack`;
    - `SelectionPacks.Price` is now a property (today's flat price);
    - new `SelectionPacks.PriceFor(deck, circuits)`;
    - `Build` takes an optional price. `CampaignState` must pass `PriceFor`, as the spec says.
  - `CampaignSim.cs`:
    - the wall fallback (every 3 losses it tries every loaner and owned deck; past 15 tries the skilled pilot plays that
      match, up to 30);
    - useful copies by sink and decks finished in each band's ledger;
    - selection packs at `PriceFor`;
    - boosters picked per DP;
    - `YGO_SIM_SNAPSHOTS` and `YGO_SIM_HOUSE`.
  - `Arbitrage.cs`: selection packs at `PriceFor`.
  - `Program.cs`: the `--prices`, `--shop-probe`, `--snapshots` and `--a0` flags.
