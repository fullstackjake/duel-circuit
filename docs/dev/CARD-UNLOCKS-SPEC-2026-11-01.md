# Card unlocks spec (POST-GAME-PLAN step 1c)

A Game Designer agent's spec, 2026-11-01, written against the code as it stood. There are four unlocks: rival gifts,
bond gifts and trades, Gus's bounty board, and set completion. All of them pay cards or tokens, never DP, stay within
15% of each band's acquisitions, and pay once per save (New Game+ never repeats them). Build order follows decision 19.

## Findings in the code

- **Wins are bumped in six places:**
  - `Circuits.Record` and `RecordTag` (Profile.cs);
  - the guest records (Guests.cs);
  - `Library.Record` and `RecordFinal` (Library.cs).

  `Wins` survives `NewGamePlus()`, so crossing 0→1, 2→3 or 4→5 wins happens once per save.
- **Bond is simply `WinsAgainst(id)`:**
  - `TagPartners.DeckBond = 3` and `SharpBond = 5`;
  - the text comes from `TagPartners.Bond()` and is shown on the campaign screen;
  - Kit is excluded.
- **Gift copies:** `CopySource.Reward` rolls `Grades.Fresh` (grade 8, Raw · Near Mint). With `Paid = 0` a gift sells
  for 0, so it can never turn into DP.
- **Checks that already exist:**
  - era: `RecipeShop.InEra` and `Circuits.ShopYear`;
  - the no-pass list: `CampaignState.NoPass`;
  - shop sets: `PackCatalog.Sets`, where a set's cards are its pool's distinct codes and `IsBooster` requires 30 or
    more cards.
- **Duel data:** `DuelEvent.Parse` gives summons, activations (with codes), attacks, damage and new turns, and
  `BoardState` gives LP, turn and winner. Events are cleared every frame, so a tally collects them as the duel runs.

## Data

**Signatures:** a new `signature: <card name>` line in `personalities/*.profile`, parsed into
`Personality.Signature`. A signature must be:
- in one of the character's decks;
- first printed by the era of the band where you meet them;
- not on the no-pass list;
- not Forbidden on that circuit's list.

| Band (era) | Signatures |
|---|---|
| Beginner (2005) | rookie-ryo: Luster Dragon · brask: Marauding Captain · old-gus: Blue-Eyes White Dragon · tink: Cyber Dragon · vera: Gearfried the Swordmaster · hollis: Wave-Motion Cannon |
| Novice (2011) | frog-ftk: Treeborn Frog · six-samurai: Grandmaster of the Six Samurai · geargia: Geargiano |
| Intermediate (2015) | blackwing: Blackwing Armor Master · lightsworn: Judgment Dragon · infernity: Infernity Archfiend |
| Advanced (2019) | goat-control: Airknight Parshath · true-draco: Dinomight Knight, the True Dracofighter · plant-synchro: Lonefire Blossom |
| Expert (2023) | shaddoll: El Shaddoll Construct · drytron: Drytron Alpha Thuban · kozmo: Kozmo Dark Destroyer |
| Master (2026) | tearlaments: Tearlaments Rulkallos · snake-eye: Snake-Eyes Flamberge Dragon · mitsurugi: Ame no Murakumo no Mitsurugi |

Three obvious picks were dropped because they're Forbidden in their band's list: Substitoad, Blaster and Master Peace.
The Small Engines decks and Sky Striker have no ace released by their era, so they get no signature.

**Bounties:** a new `campaign/bounties.txt`, with `band: <band>` followed by `bounty: <id> | <checks> | <text>` lines.
All of a bounty's checks must hold:
- `finisher:normal|extra|effect`: what took the opponent's last LP;
- `nodamage`, `byturn:N`, `comeback:LP`, `bighit:N`, `turn:N` (damage in one turn), `summons:N` (in one turn),
  `traps:N`;
- `second` (you went second) and `own` (no loaner).

| Band | Bounties |
|---|---|
| Beginner | finisher:normal · nodamage · byturn:7 |
| Novice | comeback:1000 · bighit:3000 · own traps:2 |
| Intermediate | finisher:extra · summons:5 · second byturn:9 |
| Advanced | traps:4 · turn:8000 · finisher:effect |
| Expert | second comeback:2000 · own nodamage · summons:8 |
| Master (the Gauntlet counts here) | byturn:4 · second turn:8000 · own finisher:normal |

Bounties count only in one-on-one circuit duels you win in that band.

## Profile fields and save lines

- `HashSet<string> Unlocks`, saved as `unlock <key>`. The keys are `gift:<id>`, `giftowed:<id>`, `bondtrade:<id>`
  (offered), `bondtraded:<id>`, `bondgift:<id>`, `bounty:<id>` and `bountytoken:<band>`.
- `Dictionary<string,uint> SetsDone`, saved as `setdone <code> <card>`.
- `int BountyOwed`, saved as `bountyowed N`.
- `NewGamePlus()` leaves all of these alone.
- **Old saves:** a one-time `unlocks 1` flag pays the first-win gifts for duelists already beaten, in one "Gus kept
  these for you" note.

## Engine (new `Campaign/CardUnlocks.cs`)

- **`Profile.AddWin(string id, DuelReward r)`** replaces the six increments and appends `(id, winsAfter)` to
  `DuelReward.WinsAdded`.
- **`DuelReward.Grants`:** a list of `Grant` (Kind: RivalGift / Drop / BondTrade / BondGift / Bounty / BountyToken /
  SetComplete; Character, Code, Rarity, Text, Event).
- **`DuelTally.Observe(byte[])`** tracks:
  - the first player;
  - the lowest LP, the biggest hit, and the most damage and summons in one turn;
  - the traps activated;
  - the finisher (its code, and whether it was a battle).
- **`CardUnlocks.AfterDuel(profile, circuits, reward, band, tally, loaner, tag)`:**
  - **First win (0→1):** pays the signature, one copy at its commonest printing (`CopySource.Reward`, paid 0). If the
    card isn't in era yet, it's saved as `giftowed`.
  - **Later wins:** a 15% chance of a Common or Rare from their deck, only when `Cycle == 0`. The seed is
    `Hash.Mix(Salt, DuelsPlayed, id)`, so QA runs stay deterministic.
  - **3rd win:** the trade is offered.
  - **5th win:** the signature again, at its **rarest** printing.
  - **Bounties:** pay from `Wanted()`, filtered to in-era, non-no-pass cards: most copies missing first, then the
    highest `CardTrader.Cost`, then the lowest code. With no wishlist, the bounty pays 1 token if this band's token
    hasn't been paid yet; otherwise `BountyOwed++`.
- **`CheckSets(profile)`:** pays the set's rarest-printed card (skipping no-pass cards) and records it in `SetsDone`.
- **`PayOwed`** pays owed gifts and owed bounties.
- **`TradeOffers` and `Trade`:** you give one spare copy whose token cost is at least that of the card you take. A
  copy a saved deck needs is refused.
- **`static Events(grants)`** returns `beat:<id>`, `bond:<id>:3|5`, `setcomplete:<code>` and `setcomplete`.
- **Story placeholders:** `{card}` and `{set}`.
- **Posters:** a `set:<code>` poster per completed set, framing the `SetsDone` card and titled with the set's name.

## Hooks

- **Creation:** the `CardUnlocks` object is created in `CampaignState` after `NoPass`.
- **Tally:** a new `_tally` starts wherever `_board = new BoardState()` is set, and subscribes with the board.
- **After a duel:** `AfterDuel` runs before `Save()` in every result branch (circuit, tag, cup, round) and in the guest
  results.
- **Set completion:** `CheckSets` runs after every pack opening, structure deck, trade and used-market buy.
- **Results screens:** grant lines go into `r.Unlocks`, and `CardUnlocks.Events(...)` joins every result's story
  events.

## UI

- **Results lines:** "Vera's gift: Gearfried the Swordmaster · Raw · Near Mint". A drop reads "Tink slipped you a
  spare: Red Gadget."
- **Bounties button:** beside Lessons on the campaign screen, with an open-bounty count. It opens "Gus's bounty
  board": the band's 3 rows, each Open or Claimed, and "Pays: <card> from your Recipe" (or "1 Card Token (set a Recipe
  first)").
- **Bond:** the bond text adds "bond 3: one trade · bond 5: their <signature>", with a Trade button by the partner
  picker.
- **QA stand-in:** takes trades and logs every grant.

## Sample scenes

```
scene: vera-gift
when: beat:vera
vera: Fine. You earned it. Take this.
-: She slides a {card} across the counter, its sleeve worn soft.
vera: It's been in my binder since I was nine. Don't make me regret it.

scene: gus-gift
when: beat:old-gus
old-gus: A {card} from my first booster box. Two bucks a pack back then.
old-gus: Never won me a tournament. Maybe it'll win you one.

scene: first-set
when: setcomplete
old-gus: Every card in {set}? Every last common? That deserves a frame.
```

## The 15% cap

- **Acquisitions per band:** the QA stand-in's collection grew by about 125 / 88 / 85 / 85 / 148 / 123 cards per band,
  Beginner to Master.
- **The most the unlocks can pay per band:** about 12 / 7 / 7 / 7 / 8 / 7 cards (signatures, 3 bounties, about 1 drop,
  about 2 bond events), which is 9.6% / 8% / 8.2% / 8.2% / 5.4% / 5.7% of acquisitions.
- **Set completion** pays at most 1 card per 30 or more owned, which adds at most 3.3%.

## Checks (CampaignTest, plus TriggerOk)

1. **Triggers:** `beat:`, `bond:<id>:3|5`, `setcomplete` and `setcomplete:<code>` pass, and the story.txt header lists
   them.
2. **Signatures:** each one resolves, is in the character's decks, is in era for its band, is not no-pass, and is not
   Forbidden.
3. **Rival gift:**
   - the first win pays a grade 8 reward copy that sells for 0, and a second win doesn't pay it again;
   - after New Game+ a win pays nothing (no gift, no drop);
   - in era-limited play the gift is held until it's in era, then paid.
4. **Bond:**
   - the 3rd win raises the event once;
   - a trade of a copy a saved deck needs is refused;
   - the 5th win pays the rarest printing.
5. **Bounties:**
   - the file has 3 per band and every check is known;
   - a built tally meets and misses as expected;
   - with an empty wishlist: 1 token, then owed, then paid once a Recipe is set.
6. **Set completion:** it pays once and earns the poster.
7. **Budget:** per band, the most unlocks stay at or below 15%, and `--campaign-sim` gets per-band unlock and
   acquisition counters.

## Build order

1. The Profile fields, `AddWin`, `signature:`, `Grant` and the save lines.
2. Rival gifts, drops, owed gifts, `beat:` scenes, and checks 1-3.
3. Set completion and set posters, then check 6.
4. The bond 5 gift, then the bond 3 trade UI, then check 4.
5. `DuelTally`, `bounties.txt` and the board panel, then check 5.
6. The sim counters and check 7.
