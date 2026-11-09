# No Limits: the economy (2026-11-02)

The Economy Designer's numbers for the No Limits table, the No Limits Invitational, Moth's shelf and the Alley Brawl's
out-of-towners ([META-DECKS-2026-11-02.md](META-DECKS-2026-11-02.md), owner-approved). They extend
[ALLEY-ECONOMY-2026-11-01.md](ALLEY-ECONOMY-2026-11-01.md) and the alley as built (ALLEY-SPEC sections 9-11, 14-15 and
the As-built notes A1, B1, C1). The Game Designer's build spec (NO-LIMITS-SPEC-2026-11-02.md) takes its numbers from
here; knob names are mine, so match them to the spec's.

Nothing is built and no repo file changed but this one. Every number marked [P] is a placeholder until the built table
is measured.

## Verdict

**No Limits stays a sink at every win rate, and it is by far the cheapest way to own the restored cards.**
- **Stakes:** 2,500 DP held, as at the Den. You stake no card. The CPU's card is worth 2,500 to 10,000; its binder leads
  with its own restored card, and that card comes back each night the pilot sits.
- **Per night at the targets** (55% at seats 1-3, 50% at seat 4): about 7.5 duels, **−8,600 DP**, and 4 cards worth about
  15,800, 3-4 of them restored modern contraband. At 100% it pays exactly 0 DP.
- **Restored cards cost about 2,000 DP of lost stakes each** at the table. Gus's boosters charge 3,700-37,500 DP a copy
  (Original Sinful Spoils about 33,000).
- **One price stays.** The game prices these cards by rarity and grade only (Original Sinful Spoils is 250 DP Raw), and
  that's right. The stake rule picks the pilot's premium printing so the bet looks fair. Won copies sell for 0.
- **The Invitational:** 3,000 entry, the Midnight Open's pay, 4 / 3 / 2 / 1 / 0 **No Limits Packs**, and 2 Card Tokens
  on the first title. That is −2,400 to −2,600 DP an entry and 50% of the entry back at best.
- **Moth's shelf** already sells modern contraband (about 1 slab in 6). Recommended: once No Limits opens, 2 of its 6
  slots go to the restored cards, at 2,500-22,500 DP.
- **The Alley Brawl's payouts stay.** They were set for 30-45% title odds. Today it runs at 70-78%, which pays 11-14
  value per DP lost. The out-of-towners bring it back to the 3-5 it was built for.

## 1. Stakes at No Limits

### 1.1 The tier

| | Den (Nyx's Table) | Orvel's Table | **No Limits** (recommended) |
|---|---|---|---|
| Your stake | 1,000 DP held | 3,000 DP held + a spare card worth 1,000+ | **2,500 DP held** (`NoLimitsStake`), no card |
| The CPU's card | 1,000-5,000, Raw 8-9 or slabs 8-10 | slab 9-10 worth 1-2× yours | **2,500-10,000** (`StakeFloor` 1.0 × stake, `NoLimitsCap`), Raw 8-9 or slab 9-10 |
| Targets | 55% | 55-60% | 55% seats 1-3, 50% seat 4 |
| A duel at the target | −450 DP | −1,350 DP | −1,125 DP (seats 1-3), −1,250 (seat 4) |
| Card value a duel | about 1,024 | +375 net (a 1,000 card, no buy-back) | about 2,170 (seats 1-3), 1,975 (seat 4) |
| Value per DP lost | 2.3 | 0.3 (a 1,000 card) | 1.9 (seats 1-3), 1.6 (seat 4) |

- **Why held DP and no card from you.**
  - The table's point is the CPU's restored card.
  - Orvel is the alley's card-for-card table. A second one would only add rail churn and the matching rule.
  - Your best spare cards at No Limits are the restored copies you need to build the peak builds.
- **Why 2,500 and not 3,000 or 5,000.**
  - **3,000:** the binders hold the same cards (mean 3,970 against 3,946), because almost nothing is worth between 2,500
    and 3,000. You'd pay 20% more for the same cards.
  - **5,000:** White Binder and Albion have no printing worth 5,000, so they can't lead. The binder mean rises to about
    6,900, and a night costs −17,250.
  - 2,500 sits below Orvel's 3,000 because here nothing of yours is on the rail and the decks are stronger.
- **Grades** (`NoLimitsGrades` [P]): Raw 8 at 15%, Raw 9 at 15%, slab 9 at 35%, slab 10 at 35%. The out-of-towners keep
  their cards sleeved and slabbed. At a 2,500 floor, only premium printings fit as Raw (a Starlight Raw 8 is exactly
  2,500).
- **The cap is 10,000, not 15,000.**
  - At 15,000 a Starlight 10 shows up in the binders, and the mean rises to 4,383.
  - A Starlight 10 feeds Orvel's ladder (15,000 is its top) two steps early.
  - The cap keeps Starlight 10s at Orvel's Table and on Moth's shelf.

### 1.2 The binders

Each pilot has one binder for good (`nl:<pilot>`), like a headliner's (`hl:<id>`). It is not a regular's per-night
binder, which a night's end drops. 12 different cards from its authored build, at `BinderSize` 12 and `RefillEvery` 3,
as everywhere.

- **What's in them.** Measured on the base decks plus Appendix B's picks (stand-ins for the authored builds), at the
  recommended rules:

| Build | Fits | Binder mean | Contraband share | Lead card(s) as staked (mean value) |
|---|---|---|---|---|
| Snake-Eye | 38 cards | 3,933 | 46% | Original Sinful Spoils: Prismatic or Quarter Century slab 10 (4,320); Fiendsmith's Lacrima: Starlight Raw or slab 9, Collector's / Ultimate / Platinum slab 10 (3,654) |
| Maliss | 32 | 3,987 | 30% | White Binder: Collector's or Prismatic slab 10 (3,600) |
| Mitsurugi | 47 | 3,909 | 34% | Ame no Habakiri: Starlight Raw or slab 9 (4,019) |
| Ryzeal | 33 | 4,016 | 58% | none ever-Forbidden of its own; lead Ryzeal Detonator (Limited, restored to 3): Prismatic / Collector's / QCSR slab 10 |
| Kewl Tune | (no deck yet) | | | Kewl Tune Rotary: Starlight Raw or slab 9 (4,019) |
| Vanquish Soul | 28 | 3,879 | 16% | K9-04 Noroi: Starlight Raw or slab 9 (4,019) |
| Tenpai Dragon | 28 | 3,951 | 56% | none fits (Sangen Summoning tops out at 1,500); lead Giant Trunade or Cold Wave (QCSR / Platinum slab 10) |
| Branded Despia | 42 | 3,936 | 43% | Albion: Prismatic slab 10 (3,600). Branded Expulsion never fits (Common only, 240 at best) |
| Yubel | 36 | 3,966 | 34% | Fiendsmith's Lacrima (3,654) |
| Light and Darkness | 42 | 3,950 | 55% | Magicians' Souls (3,927), Red-Eyes Dark Dragoon (3,871) |
| Toon | (no deck yet) | | | **Toon World the Perfect World has no printing in printings.bin, so it can't be staked** (see 1.5) |

- **Every binder fills.** 28-47 different cards fit each real build; a binder needs 12. The mean of the binder means
  is 3,946.
- **"Contraband"** for the 3× weight is the build's ever-Forbidden cards (the pool at era 9999 ∩ its deck), as a
  headliner's is.
- **Leads go first**, from the seat's `stakes:` line in order, each at a printing that fits the range. That is
  `Ante.Binder`'s headliner path as built: the lead is drawn only from the candidates that fit, so the floor upgrades it
  to the pilot's premium copy by itself.
- **A lead that fits no printing is skipped**, as the code does today. No exemption: a 2,500 DP bet against a 240 DP
  Expulsion would break the "a bet looks fair" rule that Penny's Common is the only exception to.

### 1.3 The restock: what makes it a steady source

Without a rule, a pilot stakes its restored card once. After that it comes back only through the refill (one draw per 3
alley duels), at 1-9% a draw: about one copy per pilot, ever.

- **Recommended (`NoLimitsRestock` [P] on):** at the start of each night a pilot sits, any lead card missing from its
  binder goes back in, behind the leads still there. So a two-lead pilot rotates: Snake-Eye stakes Original Sinful
  Spoils, then Fiendsmith's Lacrima the next night, then Spoils again.
  - At most one copy of each lead a night, and only on nights the pilot sits.
  - It's Penny's rule ("a fresh one the next night she sits"), generalized.
  - The restocked copy is a new draw (new serial, printing and seed), from the same seeded stream.
- **What it yields.**
  - **Wave 1 night:** Snake-Eye at seat 4, plus 3 of the 6 regulars. Five of the six lead with a restored card (Tenpai
    doesn't). That's about **3.5 restored cards a night** out of 4 wins.
  - **3 copies of each wave-1 restored card** (Original Sinful Spoils, Lacrima, Habakiri, Detonator, Noroi, Albion):
    about **6 nights, 45 duels, −52,000 DP** at the targets.
  - **Bought from Gus's boosters instead** (expected cost a copy, below): about 347,000 DP. The table is about 7×
    cheaper. That is why it's the steady source, though not the only one (section 2.3).
- **The 4th-copy rule holds:** a 4th won copy keeps your best three, for 0 DP, as `PrizePacks.Give` does.

### 1.4 DP and cards a night

A night is 4 seats; you play each until you beat it. Losses at a seat average (1 − p) / p.

| Player | Seats 1-3 / seat 4 | Duels a night | DP a night (2,500 held) | Cards | Card value |
|---|---|---|---|---|---|
| At the targets | 55% / 50% | 7.45 | **−8,630** | 4 | about 15,800 (1.8 per DP lost) |
| Typical | 45% / 40% | 9.17 | −12,920 | 4 | about 15,800 (1.2) |
| Strong | 65% / 60% | 6.28 | −5,700 | 4 | about 15,800 (2.8) |
| Every duel | 100% | 4 | 0 | 4 | about 15,800 |

- The first clear is night 0's fourth win: −8,600 DP at the targets, 4 cards, `ClearTokens` (1 token), its poster.
- **What it costs:** a night is about 41-57 post-game farming duels (150-209 DP each, PROOF), or about 0.8 of a cup's
  post-game earnings (about 11k).
- **What players actually hold:** the QA champions banked 139k-412k (build/qa-late-resume 138,864; qa-tag 196,106;
  qa-curve 271,766; qa-night5 354,554; proof/chk-final 411,600). The `--postgame-sim` stand-in ends near 0 only because
  it spends everything on packs.
- **Affordability:** a duel needs 2,500 DP in hand. Hold as at every table: `Ante.Blocked` refuses with the stake and
  your DP.

### 1.5 First-turn guard, duels, and what the spec must handle

- **The guard stays at turn 1** (`Result.Turns == 1`: the CPU went first and won before your first turn). The stake comes
  back and the card goes back on top.
  - Tenpai's going-second kill lands on turn 2, after you've had a turn: that's a real loss.
  - Knob `NoLimitsGuardTurns` [P] 1. Raise it to 2 only if `--alley-study` shows over 10% of the player's losses by turn
    2 (Appendix A's speed gate).
  - The guard can't be farmed: nothing pays on it, and a quit is a loss.
- **Duels pay no DP** (`DuelDp` 0). EXP is the Library's; level-ups pay tokens only (`payDp: false`). Turn cap 40.
- **For the spec:**
  - `AlleyTier.NoLimits`, with `StakeFor`, `Range` and `Conditions` reading the new knobs.
  - The `nl:<pilot>` owner, with its tier and its `stakes:`.
  - The restock.
- **Toon World the Perfect World has no printing in `printings.bin`** (OCG-only; import packs only). `Ante.Candidates`
  iterates `ctx.Printings.Of`, so it can never be a stake, a pack pull or a shelf slab. Two options:
  - Lead Toon with Change of Heart (Quarter Century slab 10) or Toon Bookmark (Limited, restored; Prismatic slab 10).
  - Or wait until Magnificent Maestros' printings land in printings.bin.
  - Toon's seat is wave 2, so neither blocks wave 1.

## 2. The value of restored modern contraband

### 2.1 How the game values them

One price everywhere: `Pricing.V` = basis × grade multiplier.
- **Basis:** the card's price at its commonest printing (Common 40, Rare 80, Super 150, Ultra 250, Secret+ 400), or the
  copy's own rarity if that's worth more (Prismatic, Platinum, Collector's, Ultimate 600; Quarter Century 900; Ghost
  1,500; Starlight 2,500).
- **Grade multiplier:** Raw 9 is 1.3×, a slab 9 2×, a slab 10 6×.
- **Banned status, power and scarcity don't enter it.**

| Card | Commonest | Raw NM at commonest | Slab 10 at commonest | Best slab 10 | Gus's boosters, expected DP a copy |
|---|---|---|---|---|---|
| Original Sinful Spoils - Snake-Eye | Ultra | 250 | 1,500 | 5,400 (QCSR) | 33,333 (AGOV, 0.018 a pack at 600) |
| Fiendsmith's Lacrima | Common | 40 | 240 | 15,000 (Starlight) | 4,545 (INFO) |
| Maliss <Q> White Binder | Super | 150 | 900 | 3,600 | 18,000 (CRBR) |
| Ame no Habakiri no Mitsurugi | Super | 150 | 900 | 15,000 | 9,836 (ALIN) |
| Kewl Tune Rotary | Ultra | 250 | 1,500 | 15,000 | 25,000 (BLZD) |
| K9-04 Noroi | Ultra | 250 | 1,500 | 15,000 | 37,500 (DOOD) |
| Albion the Branded Dragon | Common | 40 | 240 | 3,600 | 19,565 (LIOV) |
| Branded Expulsion | Common | 40 | 240 | 240 | 3,659 (POTE) |
| Magicians' Souls / Red-Eyes Dark Dragoon | Super | 150 | 900 | 5,400 / 15,000 | 13,393 / 14,423 (RA02) |
| Ryzeal Detonator (Limited) | Ultra | 250 | 1,500 | 5,400 | 10,976 (CRBR) |

(Booster cost = the set's price ÷ `PackCatalog.Expected` copies a pack, 2,000 sample packs. The post-game shop year is
2026, so every set above is on sale.)

### 2.2 Is it right? Yes, keep one price

- **Nothing sells for DP anyway.**
  - Won copies are Paid 0 and sell for 0 (`SellDesk.Blocked`: "Won at ante: Moth doesn't buy back his own.").
  - Pack copies are Paid 0 too.
  - So a "contraband premium" on V would change no income. It would only raise what Moth and Gus charge.
- **The one place value bites is the stake floor,** and section 1.2 handles it by choosing the printing, not the price.
  The pilot's Original Sinful Spoils is a Quarter Century slab 10 worth 5,400, not a Raw Ultra worth 250.
- **A premium would break "one price for every screen".**
  - The same card would be worth more in the alley than at Gus's.
  - It would add a second rule to every price check (E2): the Sell desk, the van, the Grading Desk's fee by basis.
- **Scarcity is already expressed by the source:** 33,000 DP of boosters, or one seat win.

### 2.3 The sell rules hold, and they must

- **Won at the table:** Paid 0, sells for 0.
  - At 55% a seat win costs about 2,045 DP of lost stakes for a card worth about 3,950. If won copies sold at V/4 (990),
    a 100% player would gain +990 DP a duel: a faucet.
  - Graded: Paid becomes the fee, so it returns at most half the fee.
- **Other routes to these cards** (DP sinks, not faucets; not the table's to fix):
  - **Gus's boosters:** expected costs in 2.1.
  - **Gus's used market:** its pool is every card the campaign's duelists play, so Original Sinful Spoils, White Binder,
    Habakiri and Albion are in it, as their roster decks run them. A wishlisted card gets a used-market slot every 5
    duels, at a rarer printing or in a slab, at V (Original Sinful Spoils about 600-1,200). That's a cheap route for those
    four. It's a sink, and it never gives the six that no roster deck runs (Lacrima, Noroi, Rotary, Expulsion, Souls,
    Dragoon). From the code, not run.
  - **Moth's used tab:** draws uniformly from the 231-card pool. A given card shows in about 7.5% of 5-duel periods, at V.

## 3. The No Limits Invitational

16 players, Bo3 final (the Midnight Open's engine), opened by the table's first clear. Title target 10-20% for a
champion's best No Limits deck.

### 3.1 Entry and pay

| Knob | Recommended | Why |
|---|---|---|
| `InvitationalEntry` | **3,000** | Above the Open's 2,500: the dearest door in the alley |
| `InvitationalPay` (winner, finalist, semi, quarter, round of 16) | **1,500 / 1,000 / 600 / 300 / 100** | The Open's. A title is 5-6 duels, and the 300-a-duel cap (E7) allows at most 1,500 |
| `InvitationalPacks` | **4 / 3 / 2 / 1 / 0** No Limits Packs | Quarter-finalists get a pack. At 10-20% odds, the Open's 4 / 2 / 1 would pay 0.5-0.8 value per DP lost, half the Open's |
| `InvitationalTokens` | **2**, first title only (`invitational:token`) | The Open pays 1. Tokens make Trader copies (Paid 0), so they're harmless |

By per-round odds that give the title target (Bo3 final), at a No Limits Pack of about 1,790:

| Title | Per round | Placements W / F / SF / QF / R16 | DP an entry | Duels | Packs | Value per DP lost |
|---|---|---|---|---|---|---|
| 10% | 0.556 | 10 / 7 / 14 / 25 / 44% | **−2,578** | 2.29 | 1.14 (about 2,030) | 0.79 |
| 15% | 0.610 | 15 / 8 / 15 / 24 / 39% | **−2,501** | 2.54 | 1.36 (about 2,430) | 0.97 |
| 20% | 0.652 | 20 / 8 / 15 / 23 / 35% | **−2,431** | 2.76 | 1.55 (about 2,780) | 1.14 |

- **About the Midnight Open's 0.8-1.5 at its own odds:** the prestige sink, a little dearer.
- **At best 50% of the entry back in DP** (a title: 1,500 of 3,000). Packs and tokens add nothing that sells.
- **Rejected:**
  - Entry 5,000: −4,400 to −4,600 an entry, 0.44-0.63 value per DP lost. A toll.
  - The Open's prizes at 3,000: 0.47-0.81.

### 3.2 The prize: a No Limits Pack

The Contraband Pack's format, unchanged: 4 Raw at the foil odds, then a sealed slab 9-10 at Super Rare and up (9 at 75%).
No card twice; Paid 0. Only its pool changes. 20,000 packs each, same seed:

| Pool | Cards | Mean | Median | p10-p90 | A given card a pack |
|---|---|---|---|---|---|
| The Contraband Pack (ever-Forbidden, minus exclusions) | 231 | 1,841 | 1,570 | 845-3,040 | 2.2% |
| First printed 2020-2026 only | 43 | 2,143 | 1,750 | 1,105-3,650 | 11.6% |
| **The peak builds' contraband, wave 1** | 34 (8 modern) | 1,810 | 1,480 | 770-3,105 | 14.7% |
| **The peak builds' contraband, waves 1-2 with Toon** | 52 (14 modern) | 1,774 | 1,470 | 765-3,040 | 9.6% |
| Peak builds, modern only | 14 | 2,191 | 1,805 | 1,110-3,870 | 36% |

- **Recommended pool: every ever-Forbidden card the shipped peak builds run.** It's data-driven (the authored decks ∩
  the era-9999 pool), so it grows with each wave.
  - It serves the collection goal: "rebuild the peak builds from what you win upstairs".
  - It's worth what a Contraband Pack is (1,774-1,810 against 1,841).
- **Not modern-only.** 14 cards make each restored card a 36% pull a pack. An entry would hand out about 5 restored
  copies, and the Invitational would beat the table as the way to own them.
  - With the recommended pool, an entry gives about 1.2-1.3 copies of the restored cards themselves (Raw mostly).
  - The table gives about 3.5 a night as premium slabs.
  - Both feed the goal; neither replaces the other.
- **Not the Contraband Pack** (2.2% a card): the Invitational would add nothing toward the peak builds.
- **Odds shown** as the Contraband Pack's (`Odds()` over its pool; E5). Never sold.

## 4. Moth's shelf

- **It already carries modern contraband.**
  - The shelf draws from the era-9999 pool: 70 of its 429 items are first printed 2020+, from 24 cards (Original Sinful
    Spoils, Lacrima, Habakiri, Rotary, Noroi, White Binder, Albion, Souls, Dragoon, Tinia among them).
  - That's 16.3% of items: about **1 modern slab on a 6-slab shelf**, at 5,500-22,500 (1.5× V rounded to 500).
  - Nothing changes when No Limits opens.
- **Recommended: 2 restored slots once No Limits opens** (`ShelfRestoredSlots` [P] 2 of `ShelfSize` 6).
  - **What they draw:** the shipped builds' lead cards, at Ultra Rare and up, slabs 9-10, worth at least
    `ShelfRestoredMinValue` [P] 1,500.
  - **The shelf's own rules:** 1.5× rounded to 500, new every 10 alley duels, no card twice, `alley.shelf`.
  - **What's in them:** 45 items from 10 cards, **2,500-22,500, median 5,500**. For example: an Original Sinful Spoils
    Ultra slab 10 at 2,500, its Quarter Century slab 10 at 8,000, a Fiendsmith's Lacrima Starlight 10 at 22,500.
  - A given card shows about once in 5 restocks (more often for cards with many printings). A collector's trickle, and a
    sink for the 140k-410k banks.
- **The bound holds:** a shelf slab sells back for at most V/4 of 1.5 V, a sixth.
  - **Alternative:** leave the shelf as is (about 1 modern slab a shelf, never the cheap Ultra 10s).

## 5. The Alley Brawl's out-of-towners

**No payout change.** The Brawl's pay (900 / 650 / 400 / 150, entry 1,000, packs 2 / 1) was set for the 30-45% title
target. As built it runs at 70-78% (C1's review: 77.6% / 79.4% after Dumpster Row, 69.9% / 72.2% after The Basement Game).

| Title odds | DP an entry | Packs | Value per DP lost |
|---|---|---|---|
| 77.6% (today) | −215 | 1.62 | 13.9 |
| 45% (target top) | −399 | 1.04 | 4.8 |
| 37.5% | −446 | 0.90 | 3.7 |
| 30% (target bottom) | −496 | 0.75 | 2.8 |
| 20% | −568 | 0.54 | 1.8 |
| 10% | −655 | 0.32 | 0.9 |

- **Today the Brawl is the alley's most generous loop** (11-14 value per DP lost, above Locals' 3-10). Harder fields are
  the fix, and the payouts are already right for it.
- **Watch the low end.** The Brawl opens after Dumpster Row, when a typical player plays like a deck of about
  1,500-1,600. The out-of-towners' 2024 decks are 1,612-1,675.
  - Under 20% title odds it falls below 2 value per DP lost: a toll.
  - If `--bracket-sim` puts a typical player there, seat the out-of-towners from The Basement Game's clear on (the Game
    Designer's call), rather than adding pay.
- **Still never DP-positive:** 900 for 1,000 on a title.

## 6. Arbitrage and the ledger

### 6.1 Loops that must stay negative (`--arbitrage`, on the real code)

Named loops. Each starts from the arbitrage template save with No Limits open and every pilot's binder made.

| # | Loop | Must be |
|---|---|---|
| 1 | No Limits ante at every seat (seat 4 too): win, loss, draw, first-turn loss, quit, reload mid-duel; sell everything | Wins, draws and the guard exactly 0; any loss or quit < 0 |
| 2 | The restock: win every seat for 10 nights (leads rotate), sell everything | Exactly 0 |
| 3 | A won lead slab cracked, regraded (to 10s), sold | Below 0 (at most half the fee back) |
| 4 | A won No Limits slab staked at Orvel's, lost, bought back off the rail (1-5 rounds), graded, sold | Below 0 (ALLEY-ECONOMY: −8,100 on 9,000 for the same shape) |
| 5 | The restored shelf slots → Moth's Sell tab; → crack, regrade, sell | At most a sixth back; below 0 |
| 6 | The Invitational at every placement: packs opened, tokens traded, sell everything | Every placement under 100%; a title at most 50% |
| 7 | The Invitational's packs graded (to 10s, crack, resubmit) and sold | Below 0 |
| 8 | All three alley brackets won in turn, prizes sold | Under 100% |
| 9 | New Game+ after a No Limits clear and an Invitational title | The clear token, the title tokens and the posters don't re-pay; binders and nights carry over |
| 10 | A won lead that's a 4th copy displacing a bought Raw copy | 0 DP and no `refund.*` line (the ante path drops `Added.Dp`) |

- **A separate exhaustive search for No Limits.** The alley's current search is 17 actions over 1-4 steps (88,740
  sequences); adding the new actions to it would make it several hundred thousand.
  - 12 actions over 1-4 steps (22,620 sequences): nl win, nl loss, nl draw, nl first-turn loss, nl quit, next night
    (restock), restored shelf buy, Orvel stake of a No Limits slab (loss), buy back, grade best, crack and regrade, sell
    best.
  - Pass: none DP-positive; every sequence with a lost stake or a quit below 0.
- **Add to the bracket search:** "invitational title", "invitational out first" and "invitational final lost". That's
  11 actions over 1-3 steps (1,463 sequences).
- **The best-ratio printout** gains three lines: restored shelf → sell, Invitational title → sell, No Limits slab →
  Orvel → rail → sell.

### 6.2 Ledger kinds

Three new keys, as `Ledger` constants. `LedgerCheck` lists every key, so add them there.

| Kind | Key | Constant | What |
|---|---|---|---|
| Sink | `nolimits.stake` | `Ledger.NoLimitsStake` | A No Limits stake lost (a loss or a quit). A stake that comes back writes no line |
| Sink | `nolimits.entry` | `Ledger.NoLimitsEntry` | Invitational entries |
| Source | `nolimits` | `Ledger.NoLimits` | Invitational placement pay |
| (existing) | `alley.shelf`, `refund.alley`, `sell`, `grading`, `alley.rail` | | The restored shelf slots are the shelf; Orvel and the rail as built |

- **Why separate keys, not `alley.stake`:** No Limits will be the post-game's largest sink (−8,600 a night). Telemetry
  and `--postgame-sim` need to see it apart from Street and Den stakes. `Brackets.PayLedger` and `EntryLedger` gain the
  `invitational` kind.
- **Held DP reconciles as built:** `Dp + AnteLive.Held` against the ledger mid-duel.
- **Checks:**
  - A No Limits duel's DP all-in is at most 0 (E7).
  - The Invitational's pay is at most 300 a duel.

## 7. Knobs

Add a "No Limits" block to `AlleyEconomy`. It stays the one file of alley numbers, and the harness can swap its values.
The existing knobs it reuses are listed too.

| Knob | Default [P] | Notes |
|---|---|---|
| `NoLimitsStake` | 2,500 | Held. Back on a win, a draw or a first-turn loss |
| `StakeFloor` (existing) | 1.0 | The CPU's card is worth at least 2,500 |
| `NoLimitsCap` | 10,000 | 15,000 lets Starlight 10s in (mean 4,383) |
| `NoLimitsGrades` | Raw 8 15%, Raw 9 15%, slab 9 35%, slab 10 35% | |
| `NoLimitsMinRarity` | none | The floor already filters |
| `BinderSize`, `RefillEvery`, `ContrabandWeight` (existing) | 12, 3, 3× | Contraband = the build's ever-Forbidden cards |
| `NoLimitsRestock` | on | Missing leads back in each night the pilot sits, behind the leads still there |
| `NoLimitsGuardTurns` | 1 | 2 only if wins by turn 2 exceed 10% |
| `DuelDp` (existing) | 0 | `payDp: false` for No Limits and Invitational duels |
| `ClearTokens` (existing) | 1 | No Limits' first clear |
| `InvitationalEntry` | 3,000 | |
| `InvitationalPay` | 1,500 / 1,000 / 600 / 300 / 100 | ≤ 300 a duel |
| `InvitationalPacks` | 4 / 3 / 2 / 1 / 0 | No Limits Packs |
| `InvitationalTokens` | 2 | First title only, `invitational:token` |
| `NoLimitsPackPool` | the shipped peak builds' ever-Forbidden cards | Computed from the authored decks, not listed |
| `PackSize`, `PackSlabNine`, `PackSlabMinRarity` (existing) | 5, 75%, Super Rare | Shared by both packs |
| `ShelfRestoredSlots` | 2 (of `ShelfSize` 6) | From No Limits' opening |
| `ShelfRestoredMinRarity`, `ShelfRestoredMinValue` | Ultra Rare, 1,500 | `ShelfMarkup` 1.5 as the rest |
| `BrawlEntry`, `BrawlPay`, `BrawlPacks` (existing) | unchanged | |

**Hard rules no knob may break** (ALLEY-SPEC section 15, unchanged):
- DP back for a copy is at most half of what went into it.
- One price everywhere.
- Stakes are held from the duel's start.
- The CPU's card and its value are shown before you commit; the packs' odds are shown.
- Alley duels pay at most 0 DP, and alley brackets at most 300 a duel.
- No loop is DP-positive.
- New Game+ re-pays none of it.

## How it was measured

- **The probe:** a scratch probe on the engine's own code, outside the repo, built against build/bin/check.
  - **Prices and printings:** `Pricing.V`, `PrintingIndex` (printings.bin), `PackCatalog` and its `Expected`.
  - **The pool:** `Alley.Contraband` at era 9999 (231 cards, 43 first printed 2020+).
  - **The binders:** `Ante`'s candidate rule (the tier's floor, cap and grades; 10000/Unit × 3× contraband).
  - **The packs:** `ContrabandPacks.Roll`'s draw over each pool, 20,000 packs each.
  - **The shelf:** `MothsVan.ShelfItems`' rule.
- **Stand-ins:** the peak builds don't exist yet, so binders use the base deck (decks/history) plus Appendix B's picks.
  Kewl Tune and Toon have no deck file, so their binder rows are blank.
- **Brackets:** analytic, with equal per-round odds that give the title target (Bo3 final: p²(3 − 2p)), and the pay and
  packs above.
- **Win rates:** Appendix A's targets, not measured duels.
  - The DP verdicts hold at any win rate. Card value per duel and the bracket figures move with it.
  - Re-run on the built table: `--alley-study` (seats), `--bracket-sim` (the Invitational, the Brawl with
    out-of-towners).
- **For the build:** port the probe into `--alley-probe` as a No Limits section (binder fill and mean per build, lead
  printing, pack values), so these numbers re-measure from the authored decks.

## Risks

- **Strong players get a lot of premium slabs:** about 15,800 of value a night, all Paid 0. That's the table's point. It's
  bounded by 4 seats a night and one restock per lead a night, and it costs nothing in DP balance.
- **Orvel's ladder speeds up.** A 4,000 No Limits slab staked at Orvel's starts the climb two steps up: about 2 wins to a
  15,000 slab instead of 4. If the Gem Mint poster and Orvel's Marker come too fast, raise `OrvelMinCard` for staking
  restored cards. Don't lower `NoLimitsCap` further.
- **The typical player:** −12,900 a night at 45% / 40%, and the Invitational at under 10% title odds (about −2,600 an
  entry for about 0.8 value per DP). If No Limits stalls, lower `NoLimitsStake` to 2,000 first: mean 3,413, −6,900 a
  night, every lead still fits.
- **Cheaper routes exist for four restored cards** (Gus's used-market wishlist slots). They're DP sinks, and the table
  still owns the premium copies and the other six. If the owner wants the table to be the only route, exclude
  currently Forbidden cards from wishlist slots. That's a shop change, not recommended here.
- **The pack pool depends on the authored decks.** Every build added grows it, and adds a Raw route to its restored
  card. Re-measure the pack's mean and a card's share each wave.
- **Toon's identity card can't be staked or pulled** until it has a printing (1.5).

## Decisions for the owner (recommendation first)

**Decided (owner, 2026-11-02): every recommendation below is accepted.** The out-of-towners' timing follows
NO-LIMITS-SPEC section 16 (The Black Market's clear).

1. **No Limits stake:** 2,500 DP held, no card of yours; the CPU's card 2,500-10,000.
   - *Or 3,000 held:* the same cards for 20% more.
   - *Or Orvel's rule:* 3,000 plus a spare card, slab for slab.
   - *Or 5,000:* White Binder and Albion can't lead, −17,250 a night.
2. **The restock:** a pilot's restored card comes back each night it sits, rotating when it has two.
   - *Or refill only:* about one copy per pilot, ever, and the table is no longer the steady source.
3. **One price stays:** no contraband premium on V. Leads take the pilot's premium printing that fits the floor. No floor
   exemption (Branded Expulsion, Sangen Summoning and Perfect World don't lead).
   - *Or exempt leads, as Penny's Common:* bets like 2,500 DP against a 240 DP Expulsion.
4. **The Invitational:** entry 3,000; the Open's pay; 4 / 3 / 2 / 1 / 0 packs; 2 tokens on the first title.
   - *Or the Open's 4 / 2 / 1:* 0.5-0.8 value per DP lost.
5. **Its prize is a No Limits Pack** drawn from the shipped peak builds' ever-Forbidden cards.
   - *Or the Contraband Pack:* nothing aimed at the peak builds.
   - *Or modern-only:* it out-supplies the table.
6. **Moth's shelf:** 2 of 6 slots for restored cards once No Limits opens (2,500-22,500).
   - *Or as is:* about 1 modern slab a shelf already.
7. **The Alley Brawl's payouts stay.** If the out-of-towners push a typical player under 20% title odds, seat them later;
   don't pay more.
8. **New ledger keys** `nolimits.stake`, `nolimits.entry`, `nolimits`.
   - *Or reuse `alley.stake`, `alley.brackets`, `alley`:* fewer checks, no telemetry for the top tier.
9. **Toon leads with Change of Heart or Toon Bookmark** until Perfect World has a printing.
