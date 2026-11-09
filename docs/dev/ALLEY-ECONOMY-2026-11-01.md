# The Back Alley's economy, measured (2026-11-01)

The Economy Designer's numbers for the Back Alley (POST-GAME-PLAN step 9), set and measured before anything is built.
It's checked against [POST-GAME-PLAN-2026-10-31.md](POST-GAME-PLAN-2026-10-31.md) ("## The Back Alley", "## Tournaments
(brackets)", "## Targets", "## Decided") and the economy as it stands in [PROOF-2026-11-01.md](PROOF-2026-11-01.md) and
[SHOP-PRICES-2026-11-01.md](SHOP-PRICES-2026-11-01.md).

Nothing is built. The alley's rules are modeled in a new harness probe, on the engine's own pieces: the price function,
the Sell desk, the Grading Desk, the Card Trader, the used market, the splash builder and the Elo odds. The Game
Designer's build spec (ALLEY-SPEC-2026-11-01.md) didn't exist when this was written, so the knob names below are mine;
match them to the spec's.

## Verdict

**The alley is never a DP faucet, at any win rate.** Every loop the probe tried loses DP or breaks even, 16,104
sequences included.
- **Stakes are held, not paid.** Your DP goes on the table at the duel's start. A win gives it back with the CPU's card;
  a loss or a quit loses it. At a 100% win rate the alley pays 0 DP, never more.
- **Alley duels pay no DP.** They earn EXP, and their level-ups pay Card Tokens only, as Locals and repeats do. With
  level DP, Street at 75% would net +87 DP a duel.
- **Won cards sell for 0.** Grading one adds its fee to what you paid, so it sells for at most half the fee.
- **Moth pays the Sell desk's price.** His shelf sells at 1.5× value and his used tab at value, so nothing he sells comes
  back at more than a sixth (shelf) or a quarter (used) of its price.
- **Alley brackets lose DP in expectation and even on a title.** A title pays less than the entry.

**Five changes to the plan's numbers:**
- Stakes are held (back on a win), not a fee.
- The CPU's card is worth at least your DP stake.
- The Contraband Pack's fifth card is a sealed 9-10 slab, as in a Prize Pack.
- Alley Brawl pays 2 packs to the winner and 1 to the finalist.
- Midnight Open pays 4 / 2 / 1 packs.

## The recommended numbers

Proposed knobs, in one place (an `AlleyEconomy` class like `ShopPrices`), defaults as below:

| Knob | Plan | Recommended |
|---|---|---|
| `StreetStake` (Dumpster Row) | 300 DP entry | 300 DP, held: back on a win or a draw |
| `DenStake` (Basement, Black Market, Nyx's Table) | 1,000 DP entry | 1,000 DP, held |
| `OrvelStake` | 3,000 DP entry, your card too | 3,000 DP held, plus one spare card worth at least 1,000 (`OrvelMinCard`) |
| `StreetCap` / `DenCap` | CPU stake up to 1,000 / 5,000 | the same |
| `StakeFloor` | none | 1.0: the CPU's card is worth at least your DP stake (Street 300-1,000, Den 1,000-5,000) |
| `OrvelMatch` | slabs 9-10 | Orvel stakes a slab 9-10 worth 1-2× your card |
| `BinderSize`, `RefillEvery` | 12, 1 per 3 alley duels | the same |
| Empty binder | (not said) | that CPU plays without stakes until it refills |
| Alley duel DP | (not said) | 0 |
| Alley EXP | (not said) | the Library's (375 a win, 125 a loss, +40 a first win); level-ups pay tokens only (`payDp: false`) |
| Won copies | sell for 0 | Paid 0, as Prize copies; a 4th copy replaces your least valuable Raw copy for 0 DP (as `PrizePacks.Give`) |
| `RailDuels`, `RailMarkup` | 5 duels, 1.5× | the same; the buy-back price is added to the copy's Paid |
| Moth's used tab | used cards | the used market over the contraband pool, at value (one price), slabs up to 8 |
| `ShelfMarkup`, `ShelfMin`/`ShelfMax` | 5,000-25,000 | 1.5× value, rounded to 500; slabs 9-10 at Secret Rare and up worth 3,500+ (5,500-22,500) |
| What Moth pays | (not said) | the Sell desk's price, `min(V/4, Paid/2)`; won, prize and traded copies 0 |
| Contraband Pack | 5 cards, real printed rarities | 4 Raw cards (foil grade odds) and a sealed slab 9-10 (Super Rare and up; 9 at 75%, 10 at 25%), Paid 0 |
| Contraband Packs at the van | (not said) | not sold: bracket prizes only |
| `BrawlEntry`, `BrawlPay`, `BrawlPacks` | 1,000; winner 1 pack | 1,000; 900 / 650 / 400 / 150 (Locals' table); packs 2 / 1 / 0 / 0 |
| `OpenEntry`, `OpenPay`, `OpenPacks` | 2,500; winner 4 packs and a token the first time | 2,500; 1,500 / 1,000 / 600 / 300 / 100; packs 4 / 2 / 1 / 0 / 0, and the token the first time |

**Ledger lines** (`Profile.Earn` / `Spend`):
- **Sinks:** `alley.stake` (a stake lost), `alley.rail`, `alley.shelf`, `alley.used` and `alley.brackets` (entries).
- **Source:** `alley` (bracket placement pay).
- A stake that comes back isn't income. Hold it in the save and write a line only when it's lost.

## How it was measured

- **`--alley-probe` (new, `src/Ygo.Harness/AlleyProbe.cs`):** deterministic, a few seconds.
  - **Splash decks:** 12 an event, from the real splash builder and `campaign/alley.txt`.
  - **Binders:** built from those decks and `printings.bin`, and valued by `Pricing.V`.
  - **Brackets:** settled by Elo odds, 20,000 of each.
  - **Loops:** through the engine's `SellDesk`, `GradingDesk`, `CardTrader` and `UsedMarket`.
  - **The exhaustive search:** every sequence of 1-4 of 11 alley actions, then sell everything.
- **Win rates:**
  - The plan's targets: the starter as "typical", the best option as "strong".
  - Next to them, the Elo odds of real decks against each event's splash Elo: the base deck's rating plus Phase 0's
    measured gain (+52, +57, +50).
  - The DP verdicts don't depend on the win rate. Card value per duel does.
- **Locals for comparison:** `--bracket-sim` (DP and Prize Packs per entry).
- **Output:** `build/alley-econ/` (`probe-1.log`, `probe-2.log` for a second seed, `bracket-sim.log`).
- **Run:** `dotnet build/bin/alley-econ/Ygo.Harness.dll --alley-probe [--seed N] [--games K]`, with K splash decks an
  event.

## Evidence 1: win rates

| Event | CPU skill | Splash Elo | Typical | Strong | Elo odds of a 1,700 / 1,800 / 1,947 deck |
|---|---|---|---|---|---|
| Dumpster Row | 0.70 | 1,453-1,470 | 65% | 75% | 80% / 87% / 94% |
| The Basement Game | 0.85 | 1,473-1,649 | 50% | 65% | 76% / 85% / 93% |
| The Black Market | 0.95 | 1,642-1,668 | 40% | 60% | 56% / 69% / 84% |
| Nyx's Table | 0.92-1.0 | about 1,668 | 40% | 55% | 55% / 68% / 83% |

- **The Elo odds of a 1,800 deck sit 9-20 points above the strong targets.** They're bot against bot at full skill, and Phase 0 found the
  ratings about 85 Elo off head-to-head. The targets are used below; the Elo column is the ceiling.
- **A typical player plays like a deck of about 1,500-1,600 Elo** in the alley (implied by the targets), a strong one
  about 1,610-1,730.

## Evidence 2: what the CPUs stake

Each binder holds 12 different cards from the CPU's deck. Its splashed contraband is weighted 3×, at a real printing
(rarity weighted 10000/Unit, as the used market and Prize Packs pick).
- **Street:** Raw copies only, often worn (Raw 4, 6, 8 and 9 at 15/25/40/20%).
- **The Den:** Raw 8-9 and slabs 8-10.

| Table | Stake value | Mean | Median | p10-p90 | Slabs | Contraband |
|---|---|---|---|---|---|---|
| Street | 300-1,000 | 507-517 | 420 | 325-780 | 0% | 19-21% |
| Street at half floor | 150-1,000 | 284-291 | 195 | 150-600 | 0% | 20% |
| Den | 1,000-5,000 | 1,861-1,882 | 1,500 | 1,170-3,600 | 86-88% | 22-26% |
| Den at half floor | 500-5,000 | 956-1,082 | 780-800 | 500-1,500/2,400 | 64-69% | 24-26% |
| Orvel's (slabs 9-10, Super Rare and up) | any | | 1,200 | 300-15,000 | 100% | 100% |

- **Every binder fills.** 12 of 12 Street binders and 24 of 24 Den binders hold 12 stakes.
- **Examples:**
  - Street: Pot of Greed Quarter Century Secret Raw 8 (900), Harpie's Feather Duster Secret Raw 8 (400).
  - The Den: Change of Heart Collector's Rare slab 10 (3,600), Chaos Sorcerer Ultra slab 10 (1,500).
- **The floor is a fair-looking bet.** At half floor, the median Street stake (195) is worth less than your 300 DP.
- **The Den's floor means mostly slabs.** Few Raw copies are worth 1,000. If the Den should show Raw cards, use half
  floor there.
- **The stake for a duel:**
  - It's the next card in the binder's seeded order, fixed until that duel is played.
  - The versus screen shows it, with its value, before you commit.
  - Walking away before the duel costs nothing and rerolls nothing.

## Evidence 3: DP and card value per duel

Held means the stake comes back on a win; fee means it never does. Won cards are valued at `Pricing.V` but sell for 0.

| Table, player | Win | DP, held | DP, fee | Card value won | Value per DP lost, held / fee |
|---|---|---|---|---|---|
| Street, typical | 65% | −105 | −300 | 336 | 3.2 / 1.1 |
| Street, strong | 75% | −75 | −300 | 388 | 5.2 / 1.3 |
| Street, Elo 1,800 | 87% | −38 | −300 | 452 | 12 / 1.5 |
| Street, every duel | 100% | 0 | −300 | 517 | ∞ / 1.7 |
| Basement, typical | 50% | −500 | −1,000 | 931 | 1.9 / 0.9 |
| Basement, strong | 65% | −350 | −1,000 | 1,210 | 3.5 / 1.2 |
| Black Market, typical | 40% | −600 | −1,000 | 745 | 1.2 / 0.7 |
| Black Market, strong | 60% | −400 | −1,000 | 1,117 | 2.8 / 1.1 |
| Nyx's Table, strong | 55% | −450 | −1,000 | 1,024 | 2.3 / 1.0 |
| Den, every duel | 100% | 0 | −1,000 | 1,861 | ∞ / 1.9 |

- **Neither is ever DP-positive.** Held tops out at 0 when you win every duel.
- **Held stakes make ante as rewarding as Locals.** Locals return 3-10 value per DP lost (`--bracket-sim`: −233 to −447
  DP an entry for 0.84-1.56 Prize Packs of about 1,591).
- **A fee makes ante worse than the shop for a typical Den player** (0.7-0.9 against the shop's 1.0 and a box's
  1.25). Buying would beat gambling, and the Den would empty.
- **Held stakes still drain DP:**
  - **What it costs:** a Den duel loses 350-600 DP; post-game farming pays 150-209 DP a duel (PROOF).
  - So a typical Den duel costs about 3 farming duels. That's the extra DP sink the plan asks for.
- **"Win their card or lose your fee"** (decision 3's wording) is a held stake.

## Evidence 4: EXP and level-ups

Alley duels at the Library's EXP: 375 a win, 125 a loss. **If their level-ups paid DP** (0.51-0.52 DP an EXP at levels
40-70):

| Level | Street 75% | Street 90% | Den 65% | Den 90% |
|---|---|---|---|---|
| 40 | 164 level DP, net **+89** | 183, **+153** | 151, −199 | 183, **+83** |
| 50 | 162, **+87** | 182, **+152** | 149, −201 | 182, **+82** |
| 70 | 161, **+86** | 180, **+150** | 148, −202 | 180, **+80** |

- **Level DP would make the alley a faucet** for any good player at Street, and for a very good one at the Den.
- **So:** alley duels and alley brackets call `Leveling.AddExp(profile, exp, payDp: false)`: tokens only, at least one
  a level (a level every 7-12 alley duels).
- **Tokens never become DP:** a Trader copy is Paid 0 and sells for 0.
- **The per-duel cap:** 0 DP a duel all-in, against the cap of 300.

## Evidence 5: the binder refill

Cards won in 90 alley duels, with 12 stakes a binder and one back every 3 alley duels:

| Win rate | Farming one CPU | No binder | Rotating an event's 4 | The whole alley's 13 |
|---|---|---|---|---|
| 65% | 41 | 59 | 58 | 58 |
| 75% | 41 | 68 | 69 | 69 |
| 90% | 41 | 81 | 80 | 80 |

- **The binder only bites on camping.** One CPU runs dry after about 29 duels at 75%, then gives a card every 3 duels.
  Playing the alley as built (4 matches an event, encores) never hits it.
- **Keep 12 and 1 per 3.** A smaller binder or slower refill would only punish rematches of a favourite opponent.

## Evidence 6: Orvel's Table and Moth's rail

Orvel's slab averages 1.5× your card (1-2×). DP and value per duel:

| Your card | Win | DP, no buy-back | DP, buy back | Value, no buy-back | Value, buy back |
|---|---|---|---|---|---|
| 1,000 | 40% | −1,800 | −2,700 | 0 | 600 |
| 1,000 | 55% | −1,350 | −2,025 | 375 | 825 |
| 1,000 | 70% | −900 | −1,350 | 750 | 1,050 |
| 3,000 | 55% | −1,350 | −3,375 | 1,125 | 2,475 |
| 7,500 | 55% | −1,350 | −6,413 | 2,813 | 6,188 |
| any | 100% | 0 | 0 | 1.5× your card | 1.5× your card |

- **The high-roller table:** at 40% a player only trades cards evenly and loses 1,800 DP a duel; at 55% the cards grow.
- **The ladder:** stake a slab, win one up to twice its worth, stake that. Four wins climb from 1,000 to the 15,000
  Starlight 10. It's collection value only, and every step risks 3,000 DP.
- **Can buy-back and re-lose loop anything? No:**
  - **Each round** costs the 3,000 stake and 1.5× the card. The card's grade never changes, and it sells for at most a
    quarter of its value.
  - **Measured:** 1-5 rounds of lose, buy back, then sell: the best is −11,100 DP on 12,000.
  - **A won card** staked, lost, bought back, graded and sold: −8,100 on 9,000.
  - **Buying back a won card** raises its Paid from 0 to 1.5× its value, so it sells for a quarter of its value: still
    −1.25× its value.
- **Rail rules:**
  - The rail counts any duel, as the Grading Desk does.
  - The price is set at the loss.
  - After 5 duels the card is gone for good: a card sink.

## Evidence 7: Moth's van

**The shelf** (slabs 9-10 of contraband at Secret Rare and up worth 3,500 or more, at 1.5× value, rounded to 500):

| Printing | Slab | Value | Shelf | Cards |
|---|---|---|---|---|
| Platinum, Prismatic or Gold Secret, Collector's, Ultimate | 10 | 3,600 | 5,500 | 238 |
| Starlight Rare | 9 | 5,000 | 7,500 | 38 |
| Quarter Century Secret Rare | 10 | 5,400 | 8,000 | 111 |
| Ghost Rare | 10 | 9,000 | 13,500 | 4 |
| Starlight Rare | 10 | 15,000 | 22,500 | 38 |

- **The range:** 429 items from 147 cards, 5,500-22,500, inside the plan's 5,000-25,000. The function tops out at
  15,000 (a Starlight 10), so 1.5× fits under 25,000.
- **The floor of 3,500** drops Ghost 9s (3,000). The 5,000 floor would have priced them at 1.67×.
- **A small shelf, rotating** (suggested: 6 at a time, new every 10 alley duels; not measured), keeps it a collector's
  find.
- **Shelf loops:**
  - shelf → Sell desk or Moth: at most a sixth back (−4,600 DP on 5,500);
  - shelf → crack, regrade → sell: −13,750 on 17,500.
- **The used tab:** the used market's own code over the contraband pool, at value (measured with the market's full slab
  odds, 9s and 10s included). Buy → sell gets 20% back at best, buy → grade → sell 24%.
- **What Moth pays: exactly the Sell desk's price.** One price everywhere (E2), so selling at the van or the desk is the
  same and there's nothing to arbitrage between them, or against the Trader (tokens in, never out) or the Grading Desk
  (the fee joins Paid).
- **No Contraband Packs for sale.** The owner decided against a banned-card store, and a pack for sale is one. The van's
  "Packs" tab becomes the shelf.

## Evidence 8: the Contraband Pack

Cards from the ever-Forbidden TCG pool after the Alley's exclusions (231 cards), a printing weighted 10000/Unit:

| Pack | Mean | Median | p10-p90 |
|---|---|---|---|
| As written: 5 Raw cards | 1,099-1,102 | 990 | 500-1,780 |
| Recommended: 4 Raw cards and a sealed slab 9-10 (Super Rare and up) | 1,816-1,860 | 1,550-1,570 | 835-3,060 |
| A Locals Prize Pack, for comparison | 1,591 (1,191-1,966 by cup) | | |

- **As written, 30% of the cards are Commons,** and the pack is worth two-thirds of a Prize Pack, though it's the harder
  bracket's prize.
- **The slab brings it level with a Prize Pack.** It also gives contraband slabs a route to Orvel's Table.
- **Every copy is Paid 0 and sells for 0.** Its odds are shown as the Prize Pack's are.

## Evidence 9: the alley brackets

By Elo odds. DP is placement pay minus the entry; cards are the expected packs at the recommended pack's value.

| Bracket, player | Title | Final | DP an entry | Cards | Value per DP lost |
|---|---|---|---|---|---|
| Alley Brawl after Dumpster Row, typical | 28% | 43% | −506 | 1,323 | 2.6 |
| Alley Brawl after Dumpster Row, strong | 43% | 57% | −410 | 1,855 | 4.5 |
| Alley Brawl after the Basement, typical / strong | 12% / 27% | 28% / 45% | −611 / −496 | 758 / 1,355 | 1.2 / 2.7 |
| Alley Brawl after the Black Market, typical / strong | 7% / 22% | 17% / 36% | −684 / −551 | 444 / 1,076 | 0.7 / 2.0 |
| Alley Brawl, a 1,947 deck | 60% | 72% | −305 | 2,460 | 8.1 |
| Midnight Open, 1,800 / 1,900 / 1,947 | 7% / 20% / 28% | 19% / 36% / 44% | −2,031 / −1,825 / −1,727 | 1,333 / 2,455 / 3,016 | 0.7 / 1.3 / 1.7 |

- **Never DP-positive, even on a title.**
  - Alley Brawl's title pays 900 for a 1,000 entry: 300 a duel over 3 duels, the cap.
  - Midnight Open's pays 1,500 for 2,500: 300 a duel over a champion's 5-6 duels.
- **The plan's prizes as written are the stingiest in the game:**
  - Alley Brawl with 1 Raw pack to the winner: 1.0-1.9 value per DP;
  - Midnight Open with 4 packs to the winner only: 0.3-1.2.
  - Locals give 3-10. The recommended 2 / 1 and 4 / 2 / 1 with the slab pack bring the Brawl near Locals.
- **Title odds against the targets** (30-45% at 8, 15-30% at 16):
  - **Alley Brawl after Dumpster Row** (28-43%) and **the Midnight Open for a champion's best deck** (20-28% at
    1,900-1,947) are on target.
  - **An Alley Brawl after the Black Market** is too hard for a typical player (7%). That's the field (the Game
    Designer's): it should draw from the event you've cleared, not one tougher.
- **The Midnight Open is the prestige sink.** It loses about 1,700-2,200 DP an entry, for 0.7-1.7 value per DP lost.

## What clearing the alley costs

Four matches an event, at the target win rates, held stakes (duels to clear = 4 ÷ win rate):

| Event | Typical | Strong |
|---|---|---|
| Dumpster Row | 6 duels, −650 | 5 duels, −400 |
| The Basement Game | 8 duels, −4,000 | 6 duels, −2,150 |
| The Black Market | 10 duels, −6,000 | 7 duels, −2,700 |
| Nyx's Table (one match) | 2.5 duels, −1,500 | 2 duels, −800 |
| **Total** | **about 26 duels, −12,000 DP** | **about 20 duels, −6,000 DP** |

- **What it costs:** about one cup of post-game earnings for a typical player (about 11k a cup, SHOP-PRICES), half that
  for a strong one.
- **What it pays:** 13 cards (one a win), worth about 19,000 in all, plus tokens.
- **Rematches and Orvel's Table** keep it a sink after that.

## Rules the build must keep (with the plan's E-checks)

- **Stakes are held in the save at the duel's start.** A quit, a crash or a reload is a loss. A draw, or a headliner's
  first-turn kill (HEADLINERS), gives the stake back.
- **Alley duels pay 0 DP**, and their level-ups pay tokens only (`payDp: false`). Check: the alley's DP a duel all-in
  is at most 0, and alley brackets' at most 300 (E7).
- **Won and prize copies are Paid 0.** A 4th copy replaces your least valuable Raw copy for 0 DP, as in
  `PrizePacks.Give`. Check: none of them sells for DP, graded or not (E1).
- **Every DP put into a copy joins its Paid:** a rail buy-back, a shelf or used price, a grading fee. Check: a copy
  returns at most half (E1).
- **Moth's Sell tab calls `SellDesk.Sell`**, the same price as the desk (E2).
- **Your Orvel stake** must be a spare copy: not one a saved deck needs, not at the Grading Desk, not on the rail.
- **Odds are shown (E5):**
  - the CPU's stake and its value on the versus screen;
  - the Contraband Pack's odds, as the Prize Pack's;
  - the Grading Desk's odds, as today.
- **New Game+ re-pays none of it:** binders, the rail and bracket history carry over as they are.
- **An `--arbitrage` section for the alley** once it's built: the probe's loops moved onto the real code.

## Risks

- **The win rates aren't measured in real duels.** They're the plan's targets, and the Elo odds of a 1,800 deck are 9-20
  points above them.
  - The DP verdicts hold at any rate. Card value per duel and the bracket figures move with it.
  - Re-run `--circuit-study` on the built events, and `--bracket-matchups` on the brackets.
- **Strong players get a lot of cards.** At Street, 5 value per DP at 75%, and free cards at 100%.
  - That's ante's point, and the cards sell for 0.
  - It's bounded by 4 matches an event, encores and the binder.
  - The cost lands on the shop: fewer purchases of contraband printings. Watch shelf and used-tab sales.
- **The Den is expensive for a typical player** (−500 to −600 a duel). If the alley stalls there, lower `DenStake` to
  750 before touching the cap.
- **The 4th-copy rule throws away a paid Raw copy for 0 DP**, as Prize Packs already do.
  - The alternative refunds it at the Sell desk's price. That's never DP-positive either, but it can pay up to 1,250 DP
    in one Den duel (a quarter of the cap), which reads as over the 300 cap in the ledger.
- **The Den's 1,000 floor makes its binders 86% slabs.** If that looks samey, half floor gives a Raw-heavier Den (mean
  about 1,000).
- **Orvel's ladder** reaches a 15,000 slab in four wins from 1,000. That's fine for prestige, but the Gem Mint poster
  and Orvel's Marker should come from it no faster than intended.
- **The contraband pool** is "ever Forbidden in a TCG list", so it includes oddities (Gearfried the Iron Knight,
  Number 42, Master Peace). Binders draw from the CPU's own deck, so only the Contraband Pack and the shelf see them.

## Decisions for the owner

1. **Stakes held, not a fee.** *Recommended:* held. Your DP comes back on a win. It never pays DP, and it's as
   rewarding as Locals. *Or a fee:* a bigger sink, but ante is then worse than the shop for a typical Den player.
2. **The CPU's card is worth at least your stake** (`StakeFloor` 1.0). *Or half:* more Commons and Raw cards, and a
   Street median below the stake.
3. **Orvel's Table: your stake is any spare card worth 1,000 or more, and his is a slab 9-10 worth 1-2× it.** *Or both
   stakes must be slabs 9-10:* a stricter high-roller table that gives graded and Prize Pack slabs a use.
4. **The Contraband Pack gets a sealed slab** as its fifth card (about 1,840 against 1,100). *Or 5 Raw cards, as
   written.*
5. **Alley Brawl pays 2 packs to the winner and 1 to the finalist**, with Locals' placement pay. *Or the plan's 1 pack
   to the winner:* half the cards for the same DP.
6. **Midnight Open pays 4 / 2 / 1 packs**, keeps 2,500 and the first-title token. *Or 4 to the winner only.* If it
   feels like a toll in playtests, lower the entry to 2,000 before adding packs.
7. **No Contraband Packs for sale at the van.** *Or on sale* at about 1.5× their value (2,750): a sink, but a
   banned-card store in pack form.

## Files

- **New:** `src/Ygo.Harness/AlleyProbe.cs` (`--alley-probe`): every number above and the loops. It fails if any loop or
  bracket is DP-positive.
- **Changed:** `src/Ygo.Harness/Program.cs`: the `--alley-probe` flag.
- **Logs:** `build/alley-econ/probe-1.log`, `probe-2.log`, `bracket-sim.log`. The build is in `build/bin/alley-econ/`.
- No engine or Unity file changed.
