# Post-game plan: the Library, the Back Alley, brackets, grades and posters (2026-10-31)

A plan, not built. It keeps the Format Library ([POST-GAME-PROPOSAL-2026-10-30.md](POST-GAME-PROPOSAL-2026-10-30.md))
and adds what the owner asked for on 2026-10-31:
- an illegal circuit in a back alley, with its own look and store;
- bracket tournaments;
- card grades;
- posters on your plate (a character model later).

Six agency-agents designers worked on it in parallel, and their claims were checked against the repo before going in
here:
- **Game Designer** (lead): structure, unlocks, stakes and the merged order.
- **Economy Designer**: prices, grading, the used market, ante, fees and prizes.
- **Level Designer**: alley events, contraband, exclusions, brackets and measuring.
- **Narrative Designer**: story, cast, scenes and posters.
- **UI Designer**: the reskin, brackets, grade labels and the poster slot.
- **Technical Artist**: the wear shader, slabs, the sparkle and the chime.

Where they disagreed, this plan picks one and lists the other under "Decisions for you". Nothing in the game changed.

## What the repo already does (it shapes everything below)

- **Forbidden cards are already legal for you in the usual campaign.** Your team answers to a list only in era mode
  (`CampaignState.ListFor`, CampaignState.cs:214).
- **The Card Trader already sells Forbidden cards.** Its only check is the era year in era mode (Leveling.cs:84-96).
- **CPU decks already hold today's Forbidden cards**, because they play as built in their day. For example,
  adamancipator holds 10 cards that are Forbidden on the 2026.10 list.
- So "banned" can't mean "only here". The alley's identity is **opponents who bring contraband from their own era**, plus
  **the stakes**.
- **A rules list fits the alley:** `vendor/LFLists/Traditional.lflist.conf` has 0 Forbidden and 252 Limited cards. Every
  card Forbidden on today's 158-card list is allowed at one copy.
- **Every card ever Forbidden in the repo's lists has a script** (266 names).
- **The collection stores counts, not copies:** card, then rarity, then count (Profile.cs:52). The save line is
  `card <id> <rarity> <copies>` and the loader takes exactly 3 fields (Profile.cs:334). **Grades need one record per
  copy**: the biggest single change in this plan.
- **There is no UI theme:** about 300 colors are typed in place (`Ui.Hex`), with one font (`LegacyRuntime.ttf`). The
  alley's reskin needs a theme object first.
- **All sound and many textures are procedural** (`Sfx.Effects`, `BoardFx.Textures`), so the chime and the wear need
  no asset files.
- **Your plate isn't empty:** it shows a gold emblem (`BoardView.Emblem`, BoardView.cs:61, 73-86). Posters replace it.
- **A README bug** found along the way: it says a 4th copy sells for "up to 400" DP. The code pays at most 300
  (`SellValue`, Profile.cs:42-50).

## The hub and the unlocks

The campaign screen gets district tabs: **Circuits | Library | Back Alley**. The top bar is full (CampaignScreen.cs:119-133),
so the switch sits on the left. Tournaments aren't a tab: each district has a bracket board.

| Feature | Opens | Why |
|---|---|---|
| Posters | **Now**, before the post-game | No dependencies; every band clear can pay one |
| Format Library | Clearing the Tier 0 Gauntlet | Stays the first post-game road |
| Grades, the Grading Desk, the used market | With the Library | The show-off layer arrives once a champion is buying again |
| Library brackets | A cup's bracket opens once you clear that cup | Its entry fee replaces the Library's flat repeat fee; fields come only from measured decks |
| Back Alley | Your first Library cup clear, then a rumor scene | Story: after learning the lists, you're shown the room that ignores them. Build: it needs the forced-list legality from Library Phase 1 |

## Grades (collection only: never card effects, legality, the AI or deck limits)

**Names.**
- The word is **Grade**. Labels are number plus word:

| Grade | Label |
|---|---|
| 10 | Gem Mint |
| 9 | Mint |
| 7-8 | Near Mint |
| 5-6 | Excellent |
| 3-4 | Played |
| 1-2 | Damaged |

- An ungraded copy is **Raw**.
- The grading house is fictional: **Gauge & Gilt** (recommended). Never PSA, Beckett/BGS, CGC or SGC, and nothing that
  looks like their labels.

**Where grades come from.**
- Every copy gets a true grade when you get it, stored with a seed and never rerolled.
- A Raw copy shows its condition word (for example "Raw · Played"), not the number.
- **Grading** at the Grading Desk seals it in a slab with the exact number.

| True grade | Pack pull | Foil slot | Used market |
|---|---|---|---|
| 10 Gem Mint | 10% | 6% | 1% |
| 9 Mint | 35% | 34% | 6% |
| 7-8 Near Mint | 45% | 48% | 35% |
| 5-6 Excellent | 9% | 10% | 32% |
| 3-4 Played | 1% | 2% | 17% |
| 1-2 Damaged | 0% | 0% | 9% |

- Trader, recipe-shop, structure-deck and starter copies arrive Raw Near Mint (8).
- **Existing saves** get hidden grades of 7-10 from a seed (card, rarity, copy number), so collections look as they did.

**The Grading Desk** (Economy Designer).
- **Fees by the copy's Raw value:** 100 / 300 / 1,000 / 2,500 DP.
- **Turnaround:** the slab comes back after 3 duels; express costs double.
- **The result:** 75% the true grade, 10% one higher, 15% one lower.
- **Crack and resubmit:** cracking is free. A resubmit never comes back lower than the cracked slab, with a 10% chance
  of +1.
- **The odds are shown.** Chasing a 10 is a deliberate, bottomless sink.
- Odile Varga, "the Loupe", is the house's grader in town.

**One price for every screen.**
- V = max(today's `CopyPrice`, rarity value × grade multiplier).
- **Rarity values:** 40 / 80 / 150 / 250 / 400 for Common to Secret, as today. New top tiers: Ultimate to Platinum
  600, Quarter Century 900, Ghost 1,500, Starlight 2,500.
- **Grade multipliers:** a slab is ×6 at 10, ×2 at 9, ×1.1 at Near Mint, ×0.7 Excellent, ×0.45 Played, ×0.25 Damaged.
  A Raw copy is ×1.3 pack-fresh down to ×0.25 Damaged.
- **A Raw Near Mint copy prices exactly at today's `CopyPrice`**, so existing quotes don't move ("Make it yours" stays
  2,500 / 3,370 / 6,460).
- **The top of the scale:** a Starlight 10 is worth 15,000 DP.

**Selling.**
- **A Sell desk** pays min(25% of V, 50% of the DP you put into that copy). "Buy low, grade, sell high" loses by
  construction.
- **Slabs are never sold automatically.** The 4th-copy rule compares V, not rarity.

**The used market.**
- 24 listings, refreshed every 5 duels: 70% Raw, 30% slabs, and some from your wishlist.
- Priced at exactly V, with any real printing, not only the commonest.
- **Special orders:** a chosen printing at V + 50%.
- This **replaces the Library's flat rarity upgrades** (1,000 / 2,500 / 10,000), which broke the one-price rule.

**Which copy plays.** Your best copy by value, assigned to a deck's copies in a fixed order, so a card looks the same in
every duel. **Slabs play** (see the decision below). Opponents' cards are Raw.

**How it looks** (Technical Artist and UI Designer):
- **Wear only on the art and border.** The name bar, text box, stars and ATK/DEF are capped at ±12% luminance at every
  grade.
- **Wear by band:**

| Band | Shows |
|---|---|
| Near Mint | 1-2 soft corners |
| Excellent | whitened edges, a micro scratch |
| Played | scratches, grime, fade |
| Damaged | creases, chips, stains |

- **Inside a band, the lower grade shows more of the same.**
- **How it's drawn:** one generated 512² wear atlas (about 1.3 MB), with a per-copy seed for pattern and creases.
  Shader variants `_WEAR` and `_PRIZE`. Nothing baked per card.
- **Slabs** (Near Mint and up) appear in the collection, card detail and pull reveal, never on the board. The label
  reads `GAUGE & GILT · GRADE 9 MINT · Pot of Greed · LOB-119`.
- **Labels:** "9 Mint" under each card in the collection and deck editor; "Grade 3 · Played: light crease" in the
  detail. Never color alone.
- **The prized moment** (the owner's sparkle and chime):

| Rule | What happens |
|---|---|
| Who gets the full moment | Gem Mint Secret Rare to Collector's Rare; Mint or Gem Mint Ghost, Quarter Century or Starlight |
| Who gets a glint only | Gem Mint Ultra, Gold and Platinum |
| When | Your own copy arrives face-up, or a Spell/Trap is activated or flipped |
| How often | Once per duel per copy, at most one per event batch, never while fast-forwarding |
| What you see | A shader sweep plus 8-12 star sprites in the rarity's color for 1.1 s, alongside the summon, not added to its timing |
| What you hear | Three procedural chimes (Secret, Ghost, Starlight), tuned to the duel music's key, plus a top-note ping at Gem Mint |

- **Settings:**
  - Reduced motion: no sparkle, a short static luster, and the chime stays.
  - New: "Card grades: Full / Label only", "Prized moments" and "Slab every graded copy".

## The Back Alley

**Story** (Narrative Designer).
- **Two back rooms:** Hollis's Library keeps every list since 2004, and the Alley, behind the shop's back door, keeps
  none.
- **Who brings you:** Kit, the regular with a binder of spares.
- **How the cast reacts:**
  - Gus ran the alley in 2004 and shut it in 2005 after ante cost Ines a card. He disapproves quietly.
  - Vera loves it until her first ante.
  - Hollis is amused and never comes down.
- **Tone:** string lights, chalk brackets, crates and a parked ice-cream van. Shady like a garage sale, never violent.
- **The payoff:** each contraband deck you beat adds its list to a 13th Library binder, "Unlisted".

**Cast.**

| Who | Id | Role |
|---|---|---|
| Moth | new | The fence, from the van: the buy-back rail, a contraband shelf and used cards. Never says "stolen" |
| Baz "the Bell" Calloway | new | Bracket MC, chalks the brackets, nicknames everyone |
| Odile Varga, "the Loupe" | new | Gauge & Gilt's grader |
| Penny | new | A regular who antes the same Common every night and has never lost it |
| Patron Orvel | `power-patron` | The House; hosts Orvel's Table |
| Ilara Gale | `radiant-typhoon` | The reigning bracket champion |
| Mika | `mika` | Streams the brackets with Baz; in no circuit today |
| Nyx | `nyx` | The boss table's host, "Nyx's Table"; in no circuit today |
| Nocturne | `nocturne` | The oldest regular |

- **Library-only duelists:** Clausolas, Pepe and Helix stay in the Library. Ilse never comes down.

**Random opponents.**
- Chalk handles that name the contraband: Three-Pot Teddy, Yata Mags, Firewall Fen, Cyber-Jar Wick.
- Hooded silhouettes, tinted per duelist, so no new art is needed.
- **The versus screen's three show-off cards become the contraband**, so you see it before the duel.

**Rules: the Alley list.**
- The Traditional list (Forbidden cards at one copy), for both sides.
- Yata-lock as now.
- A turn cap: at turn 40 the higher LP wins.
- An exclusion list set to 0:

| Excluded | Why |
|---|---|
| Butterfly Dagger - Elma, Ultimate Offering, Wind-Up Hunter | Loops |
| Self-Destruct Button | A draw pays 100 DP |
| Last Turn, Sixth Sense | Coin-flip endings the bot can't evaluate |
| Exchange of the Spirit | Deck-out alternate win |
| Fiber Jar | Resets the duel |
| Magical Scientist, Cyber-Stein, Dark Strike Fighter | Turn-1 FTKs |
| Floodgates: Imperial Order, Royal Oppression, Vanity's Emptiness, Kaiser Colosseum, Number 16, Anti-Spell Fragrance, Rivalry of Warlords, Gozen Match, Dimensional Barrier, Mystic Mine | Two bots stall. Allowed only in authored boss decks that pass the endless-duel check |

- **Old contraband plays with its original text** where the repo has it (Witch, Sangan, Yata, CED and others).

**Events** (Level Designer).
- Four matches each.
- Generated opponents are **fixed until beaten**, so quitting never rerolls.

| Event | Base decks (Elo) | Contraband from its era | Splash | CPU skill |
|---|---|---|---|---|
| Dumpster Row | classic to gadgets (1276-1480) | Raigeki, Dark Hole, Harpie's Feather Duster, Change of Heart, Pot of Greed, Graceful Charity... | 2 | 0.7 |
| The Basement Game | dino-rabbit to shaddoll (1416-1592) | Heavy Storm, Card of Safe Return, Cold Wave, Crush Card Virus, Glow-Up Bulb, Stratos... | 3 | 0.85 |
| The Black Market | abc to thunder-dragon (1585-1693) | Maxx "C", Triple Tactics Talent, Called by the Grave, Dandylion, Apollousa, Baronne de Fleur... | 4 (1 Extra at most) | 0.95 |
| Nyx's Table (boss) | chaos-yata as built; Nyx's authored Gravekeeper's Contraband; a Black Market splash | | | 0.92-1.0 |

**How a splash deck is built.**
- The same code runs in the app and the harness, from a seed.
- **The steps:**
  1. Pick a base deck.
  2. Add N contraband cards. "Fitted" cards need support: Crush Card Virus needs DARKs with 1000 ATK or less; CED
     needs 4 LIGHT and 4 DARK.
  3. Apply the caps: one board wipe, one steal, one Maxx "C".
  4. Cut the lowest-value cards (`AutoDeck.Value`), never the archetype.
  5. Validate against the Alley list.

**Stakes and the store.** The owner asked whether winning cards beats an illegal store. The designers agree it does:
- The Trader already sells Forbidden cards, so a store selling banned cards adds nothing in open play and undercuts the
  Library's lists.
- **Ante is the source of power.** You win the CPU's staked card.
- **Moth's van is a collector's sink and safety net:**
  - the buy-back rail;
  - used cards;
  - a small contraband shelf of rare, high-grade printings (5,000-25,000 DP).
- The recommended stake rule is under decision 3.

| Table | Entry | CPU stake |
|---|---|---|
| Street (Dumpster Row) | 300 DP | value up to 1,000 |
| Den (Basement, Black Market) | 1,000 DP | up to 5,000 |
| Orvel's Table | 3,000 DP | slabs at 9-10 only; your card is staked too |

- **Stake safeguards:**
  - Each CPU has a finite binder of 12 stakes, refilled one per 3 alley duels.
  - Stakes are held in the save when the duel starts, so quitting is a loss.
  - Won cards sell for 0 at the Sell desk.
- **Currency:** DP only.

## Tournaments (brackets)

- **Format:** single elimination, one duel per round.
- **CPU-vs-CPU matches** are settled by the Elo odds with a coin from the bracket's seed, fixed at entry. Skip is
  instant and the harness can reproduce any bracket.
- **Watch the final** (decided before it resolves) plays it for real, and that result counts.

| Bracket | Size | Where | Field | Entry | Prize |
|---|---|---|---|---|---|
| Library Locals | 8 | Each cleared cup | That cup's decks plus that era's rated decks, seeded by Elo | 1,000 | Winner: 2 Prize Packs; finalist 1 |
| Alley Brawl | 8 | After Dumpster Row | 7 splashers from your highest cleared event | 1,000 | Winner: a Contraband Pack |
| Midnight Open | 16 | After The Black Market | 5 circuit characters, 5 Library bosses, 5 splashers | 2,500 | Winner: 4 Contraband Packs and a Card Token the first time; Bo3 final |
| Later: Regional and Championship | 16 and 32 | Library | | 2,500 and 6,000 | |

- **Prize Pack (5 cards):**
  - 3 from the bracket's decks at Rare or better;
  - 1 from the real tournament sets in printings.bin (OTS, Tournament Pack, Turbo Pack, Ultimate Tournament Pack),
    which the shop leaves out today (`NotBoosters`, Packs.cs:66-72);
  - 1 sealed slab at grade 9 or 10.
- **Contraband Pack (5 cards):** from the ever-Forbidden pool, minus the exclusions, at real printed rarities.
- **Placements and DP:** placements pay DP. Tournament duels pay no DP of their own.
- **After you're knocked out:** "Out in the quarter-finals" pays at once, then **Skip to the end** (primary) or
  **Follow the bracket**.

**The screen** (UI Designer):
- A mirrored bracket with the final in the center, in 1920×1080 reference pixels.
- **Slot sizes:** 240×72 slots at 16, and 300×96 at 8.
- **Slot states:** pending, ▶ YOUR MATCH (the one red Duel button), won (a crown, the path lights gold) and OUT
  (dimmed). Each has a word or icon.
- **Controller:** A = Duel or Scout, Y = Skip to end, B = Back.
- **Prize:** reuses the shop's pack reveal.

## The alley's look

- **One `UiTheme` object** with `MainStreet` (today's values) and `BackAlley`.
- **Only the shell screens move to it:** Campaign, Shop, Trader, Results, Versus and the deck editor's frame.
- **The duel board keeps its gameplay colors** (blue you, red them, gold can act). Only the mat and top bar change.
- **Alley tokens:**

| Role | Value |
|---|---|
| Base | #0D0B0A wet asphalt |
| Panel | #1A1614 |
| Text | #EDE6DA |
| Primary action | Red, unchanged, as a stenciled plate |
| Currency | Gold, unchanged |
| Neon (#FF4FA3 / #3FE0D0) | Signs and dividers only, never on a button or the board |

- **Materials, type and sound:**
  - procedural brick, chain-link and tape;
  - one stencil font, for titles only;
  - an "alley" sound theme (rain bed, sub bass, crackle);
  - a 300 ms crossfade into the alley, instant under Reduced motion.
- **Screens:**
  - the alley map reuses the circuit map;
  - **Under the Counter** (Moth's van) reuses the shop layout, with tabs Packs | Used & graded | Sell.
- **Convert whole screens, never half**, and add a check that captures both themes.

## Posters (now) and the avatar (later)

- **A poster replaces the emblem** on your plate.
- **The spec:**
  - 600×800 source, shown at 240×320, tilted -3°;
  - paper border and tape;
  - keep the lower third quiet, since the name and LP sit there;
  - it clears the name tab and the partner chip.
- In the world, it's your square of Gus's corkboard. You pick posters in a **Locker**.

| Poster | Unlock |
|---|---|
| Shop Bell | Start |
| Rookie Cup | Beginner Circuit |
| Odd Couples | Novice Tag Cup |
| Different Sky | Intermediate |
| Golden Age | Advanced |
| Houston 2026 | Master |
| Tier 0 | Champion |
| 12 era bills | Each Library stamp (gold edge with a built deck) |
| Every Era | The Library final |
| NO LIST | First alley visit |
| Bracket bill | Each bracket title |
| LOST CARD | First ante loss |
| Gem Mint | First Grade 10 |
| Orvel's Marker | Beating Orvel's Table |

**Later:** the setting "Your plate: Emblem / Poster / Avatar". The avatar follows the portrait contract
(`Portraits.Place`, id `you_avatar`) and can take mood and expression swaps; posters become the wall behind it.

## Ban lists, Ban Passes, era mode and card unlocks (main campaign, asked 2026-10-31)

The owner set the rules; the Game, Economy, Level and Narrative Designers answered the open questions.

**The rules (decided).**
- **CPUs are exempt from ban lists** and play their decks at full strength. True today (`CampaignState.ListFor`,
  CampaignState.cs:214).
- **A CPU partner's default deck is exempt too.** This is a change: in era mode, `PartnerPlays` makes a partner's own
  recipe legal today (CampaignState.cs:393-401). Drop that adjustment; `PartnerChanges` returns nothing for own
  recipes. Loaners stay adjusted, since a loaner is your deck on loan.
- **A partner deck you edit must follow the list.** Partners can't be edited today. An edit is built from your
  collection and saved as a deck of yours with a partner note, so the list, your Ban Passes and the 3-copy cap all
  apply. **Reset to their own deck** restores the exemption. The partner's ace cards stay locked, so an edit can't
  strip their identity (the "partner identity locks" item in UI-ROADMAP.md:97).
- **In the world:** opponents are "invited exhibitors" who play their decks as they won with them; your team is
  "registered". These lines go in the `novice` and `first-partner` scenes:

```
hollis [era]: Your opponents are invited exhibitors. They play their decks exactly as they won with them; that's why we invite them.
hollis [era]: You're registered. Registered decks play by today's list.
partner [era]: It's my exhibition deck, so the list doesn't touch it. Change one card and it's yours, and the list applies.
```

**Ban Passes: 5 at most.**
- **Today:** 3 to start, +1 each for clearing the Advanced and Expert bands (BanPasses.cs:18-28), plus up to 3 bought
  at 4,000 / 8,000 / 12,000 DP (Leveling.cs:120-123). That makes 8.
- **5 is the right cap** (Level Designer, counted over 90 decks):

| Band | Decks needing 3 passes or fewer | Needing 4-5 | Needing more than 5 |
|---|---|---|---|
| Novice | 8 | 1 | 6 |
| Intermediate | 8 | 5 | 15 |
| Advanced | 20 | 6 | 22 |
| Expert | 30 | 11 | 20 |
| Master | 48 | 17 | 24 |

- **What the counts show:**
  - The starters need 0-2 passes in every band.
  - Going from 3 to 5 roughly doubles the decks that play as built in the middle bands.
  - Even 5 never makes the oppressive lists legal wholesale (chaos-yata needs 7-15, dragon-ruler 17-20).
- **How they arrive** (in the world, a Ban Pass is a signed exhibition waiver):

| Passes | When | From |
|---|---|---|
| 1-3 | The first list (Novice) | Hollis |
| 4 | The Advanced clear | Shepherd Wren |
| 5 | The Expert clear | Vera |

- **Buying** (Economy and Level Designers) only brings the 4th or 5th forward, never past 5:
  - the 4th from the Novice band for 12,000 DP;
  - the 5th after the Advanced clear for 30,000 DP;
  - half the price comes back when you'd have earned it.
  - *(The Game and Narrative Designers would stop selling passes. Old saves would get the 4,000 / 8,000 / 12,000 DP
    back, keeping their slots up to 5.)*
- **In the post-game:**
  - **Library:** passes count for a cup clear, never for a Format Master stamp or the "Every Era" final. Era-off
    players get the same 5 there.
  - **Back Alley:** no passes. It has its own list.
- **An edited partner deck** uses your allowance like any deck of yours.

**Era mode at the start.**
- **It stays a choice on the starter screen, off by default, renamed Historic Rules.** It's a challenge mode, not a
  difficulty.
- **The screen shows two choices side by side:**
  - **The shop's way:** "Build any deck from what you own. Nobody checks a list."
  - **The history way:** "Your deck answers to each circuit's real ban list and year of cards, as it really was."
  - Under both: "The champions you face play the same either way."
- **Gus confirms the pick** in the `welcome` scene.
- **Switching later:** from the campaign screen, with a second click to confirm.
  - Off to on is allowed any time; `Problems()` flags any deck that's now illegal.
  - On to off is allowed too. Circuits cleared under Historic Rules keep a "Historic" stamp.
- **The starters under Historic Rules** (copies over the list / cards not yet released):

| Starter | Novice 2011 | Intermediate 2015 | Advanced 2019 | Expert 2023 | Master 2026 |
|---|---|---|---|---|---|
| classic | 0 / 2 (Silver's Cry is 2013) | 1 / 0 | clean | clean | clean |
| warriors | 2 (Reinforcement of the Army ×3) | 1 | 2 | 2 | 2 |
| zombies | 2 (Mezuki ×3) / 4 | 1 / 2 | clean | clean | clean |
| mekk-knight-invoked | 1 / 51 | 3 / 51 | 1 / 8 | 0 / 4 | clean |

- **Mekk-Knight can't play from Novice to Expert under Historic Rules.** A pass can't fix a release date. The fix:
  - ship "2011 editions" of classic, warriors and zombies;
  - label Mekk-Knight "Beginner only under Historic Rules" on the starter screen.
  - That way the 3 starting passes go to power cards, not repairs.

**Link monsters while the band's opponents play Synchro.**
- **The premise doesn't hold:** CPUs already summon Links from the first bands. 5 of the 12 Novice decks run Links
  (altergeist, floowandereeze, runick-spright, purrely, yummy).
- **No separate gate for Synchro, Xyz, Pendulum or Link** (all four designers). The existing gates already follow the
  bands:
  - Shop and selection packs hold sets up to `ShopYear` (Profile.cs:555), so Links reach packs around mid-Intermediate
    (the first TCG Links are July 2017).
  - Under Historic Rules, the release-date rule (DeckRules.cs:59-90) and the Trader's era filter (Leveling.cs:92)
    keep Links out of a Synchro-era circuit.
  - Outside Historic Rules, Card Tokens can buy a Link early. That's what the shop's way means, and the Mekk-Knight
    starter is already a Link deck.
- **One addition:** an "ahead of your band" tag in the Trader and the Recipe Book.

**How hard building a specific deck should be.**
- **Today it's cheap everywhere:** about 4-6 wins at Novice, 2-3 at Master, almost nothing after the champion
  (GAME-REVIEW-2026-10-26.md).
- **The causes:**
  - recipe packs are 300 DP for 9 cards (DeckAccess.cs:109), capping a deck at about 2,100;
  - win pay grows to about 1,900 DP a win at circuit 15 (Profile.cs:654).

| Stage | Duels to complete a recipe | DP | Share of the wallet then |
|---|---|---|---|
| Novice | 6-10 | 3,000-6,000 | 25-40% (today's prices fit) |
| Master | 4-8 | 15,000-30,000 | 8-15% |
| Post-game | 2-4 | 6,000-15,000 | 2-5%; depth comes from grades and rarity instead |

- **The proposed fix: price recipe packs by band.** Card prices stay the same everywhere, so "Make it yours" quotes
  don't change.
- **Found while building step 1b: this alone doesn't work.** The Recipe Book's "buy every missing card" button prices
  cards at `CopyPrice` (a Tearlaments deck about 6,460 DP). With 2,500 DP packs, a player buys the missing cards
  outright instead, and late recipes cost what they do today. Pricing packs by band needs that button priced by band
  too (a service fee on top of the card prices), or a different late sink.
- **Decided and built (2026-10-31):** packs are priced by the band you first meet the deck in, and buying outright adds
  a service fee of the packs' price above 300 DP (none at Beginner and Novice). "Make it yours" pays the same. Mitsurugi
  from nothing: 22,500 DP in packs, 30,390 outright.

| Band | Recipe pack price |
|---|---|
| Beginner / Novice | 300 |
| Intermediate | 600 |
| Advanced | 1,000 |
| Expert | 1,500 |
| Master / Gauntlet | 2,500 |
| Library | 1,000 |

- **Under Historic Rules, "beat it now, own it a band or two later" stays.** Floowandereeze, a Novice opponent, still
  has 26 cards unreleased at Advanced. Older decks become buildable at Novice (six-samurai, plant-synchro).

**Fun ways to unlock cards.** All four pay cards or tokens, never DP, and stay within 15% of each band's acquisitions.
New Game+ never repeats them.

| Unlock | Reward | Story beat |
|---|---|---|
| **Rival gift** | Your first win against a duelist gives one copy of their signature card (Raw Near Mint, released in your era, never a no-pass card); later wins have a 15% chance of a Common or Rare from their deck | `beat:<character>`, e.g. Vera: "It's been in my binder since I was nine." |
| **Bond gift and trade** | At bond 3, one swap with your partner; at bond 5, their signature card | `bond:<character>:<n>` |
| **Gus's bounty board** | 3 challenges per band ("win with a Normal Monster finisher"), each paying a card from your wishlist recipe; at most 1 token a band | |
| **Set completion** | Owning every card in a set: one card at its rarest printing, plus the set's poster | `setcomplete:<set>` |

**Housekeeping found along the way.** Fix the README:
- README.md:419 and 529 say partners' own recipes are made legal;
- README.md:529-530 names Pot of Greed, Raigeki and Heavy Storm, which no starter runs;
- README.md:574 says the Mekk-Knight starter needs a pass only for Thousand-Eyes Restrict;
- the 4th-copy sale says "up to 400" DP, but the code pays 300.

## Story triggers and engine needs

- **Story triggers:**
  - `library` and the cup names, from the Library proposal;
  - `alley`, `fence`, `bracketwin`, `bracketwin:<name>`, `antewin`, `antelose`;
  - `slab`, `grade:10`, `crack`, `poster`.
- **New placeholders:** `{grade}` and `{card}`. Today only {partner}, {partnerdeck}, {you} and {level} exist.
- **New profiles:** `moth`, `baz`, `odile`, `penny`. `--campaign-test` flags the speakers until they exist.

## The phase program

Effort is in sessions of focused work. Steps 1, 1b, 1c, 4, 5 and 8 don't depend on the Library and can run beside it.

| Step | What | Effort | Needs |
|---|---|---|---|
| 1 | **Posters and the Locker**: poster slot, unlocks, picker. *Built 2026-10-31: the seven main-campaign posters frame card art (Kuriboh, Blue-Eyes, Gemini Elf, Stardust Dragon, Chaos Sorcerer, Accesscode Talker, Tearlaments Reinoheart: cards a stock EDOPro install has pictures of); the emblem stays a choice. The post-game posters wait for their steps.* | 1 | nothing |
| 1b | **Ban rules and era mode**: partners' own decks exempt, editing partner decks, Ban Passes earned only (5), Historic Rules on the starter screen, 2011 starter editions, an "ahead of your band" tag, the README fixes. *Built 2026-10-31. Left for later: switching Historic Rules mid-campaign with a "Historic" stamp, locking a partner's ace cards in an edit, band-priced recipe packs (decision 18, built after).* | 2 | nothing |
| 1c | **Card unlocks**: rival gifts, bond gifts and trades, the bounty board, set completion; their story triggers | 2 | nothing (set-completion posters need step 1) |
| | *Built 2026-11-01 (spec docs/dev/CARD-UNLOCKS-SPEC-2026-11-01.md by a Game Designer agent; engine by a Senior Developer agent, game by a Unity Architect agent, scenes by the Narrative Designer): `CardUnlocks` and `DuelTally` (src/Ygo.Engine/Campaign/), `signature:` on 22 characters, `campaign/bounties.txt` (3 per band), save lines `unlock`, `setdone`, `bountyowed`. A first win pays the rival's signature (Raw Near Mint, sells for 0; the rarest printing if you own three; held until in era), later wins a 15% spare from their time, bond 3 a trade, bond 5 their signature at its rarest; Gus's bounty board pays a wishlist card or a token; completing a set frames its rarest card and adds the set's poster. All once per save, deterministic, never DP: at most 6-11% of a band's acquisitions. 34 scenes (`beat:`, `bond:<id>:3/5`, `setcomplete`).* | | |
| 2 | **Phase 0, extended (measure first)**: the Library's studies, plus `--alley-gen`, splash ratings, goldfish speed, `--endless-test`, alley circuit studies, `--bracket-sim` and `--bracket-calibrate` | 2-3 | harness |
| | *Tools built and first runs done 2026-11-01; findings in docs/dev/PHASE0-FINDINGS-2026-11-01.md: 6 of 48 Library matches under target (Shaddoll, Thunder Dragon, Kashtira, Ryzeal bosses; machine-re-volt, snake-eye), 5 cup decks out of their pool, splash decks too weak (+38 / -15 / -25 Elo against +60 to +150; curated lists with era cards in campaign/alley.txt, tuned decks sitting out, bring them to +52 / +57 / +50, every base measured), endless duels pass, Elo odds off by 85 points against head-to-head duels. Not yet: alley circuit studies (waiting on curated contraband), --wall-study at the six walls.* | | |
| 3 | **Library Phases 1-2**: formats, forced legality, Library screen, story, stamps, rewards (the flat repeat fee becomes brackets) | 3-5 | 2 |
| | *Built 2026-11-01. Engine: `Library` (src/Ygo.Engine/Campaign/Library.cs) reads campaign/library.txt (`pool:`, and `champion:` for an optional Champion's table match); wins are kept by cup name apart from the circuits (`libwin`, `libbuilt`, `library` save lines), so New Game+ keeps them and never re-pays a first clear (C6); a first win pays 600 + 200, a repeat 150 (C3), a loaner half; a first clear 2,500 and 2 tokens; the Format Master stamp (every match won with a deck of yours, no loaner, no Ban Passes) 1 token more; it opens after the Tier 0 Gauntlet (or for a save that saw the champion scene). `FormatRules` carries a list, a pool and the passes: circuits build theirs from the mode as before, a cup forces its own, and loaners, Make it yours (C5) and the deck editor keep to it. Game: a Circuits / Library switch, the shelf of 12 binders (✓ cleared, ★ stamped), the cup panel, versus, results, Stuck? and toss tips from campaign/library-advice.txt, Library decks you beat lent as loaners. Story: `library`, `open:`, `cupclear:` and `stamp:` scenes, and the champion's last line points at the Library. Checks: `--campaign-test` (payouts, the Champion's table, the stamp, save and New Game+) and `-campaigncheck` (the Library opens, a cup's list and pool, a cup loaner, a cup versus). Library Trader (built the same day): a Card Trader button on each cup's panel opens the trader under the cup's pool, whatever the mode; it lists and trades only cards first printed in the TCG by the pool month (`CardTrader.Blocked`/`InPool` with a pool), tags each card's place on the cup's list, and Back returns to the cup; the shop's trader is unchanged (`--campaign-test` and `-campaigncheck` cover both). Phase 3 (built the same day; the Economy Designer's prices and the Technical Artist's mats): of the proposal's sinks, rarity upgrades became grades and the used market (step 6) and the structure-deck refund cap is in; what's new is `Cosmetics` (src/Ygo.Engine/Campaign/Cosmetics.cs): an era mat for your side of the board per cleared cup, 8,000 DP, painted from campaign/mats.txt (`era-<cup id>`), bought on the cup's panel or in the Locker's new Mats tab and switched there for free; and an engraved plate on a cleared cup's binder naming a deck of yours that won a match there (loaners never), 2,500 DP, again at full price for another deck, silver until the cup is stamped, then gold. Flat prices whatever the era; ten mats and ten plates are about 32% of a champion's savings. `Profile.Spend(sink, dp)` and `DpSpent` start the per-sink ledger (step 11) with these two sinks; the older sinks aren't routed through it yet. The "Every Era" final: built the same day, see step 10.* | | |
| 4 | **Copies, not counts**: per-copy records with rarity, grade and seed; save migration; deck-to-copy order; replay snapshot. *Built 2026-10-31: `CardCopy` (rarity, true grade, seed, id, source), grade odds by source, old saves graded 7-10 from a fixed seed, `Collection.Assign` for deck slots, replay format 3 with each slot's look, `--profile-check`. Still by rarity until step 6: the 4th-copy rule (it should compare V) and which copy a duel's card shows (the board doesn't track copies yet; step 5 maps slots to cards on the field).* | 1.5-2 | nothing |
| 5 | **Grade looks**: wear atlas and shader, text protection, labels, slabs, settings, `-gradeshowcase` | 3-4 | 4 |
| | *Built 2026-10-31 (Technical Artist, UI Designer and Unity Architect specs): `CardWear` (512² atlas made in code at startup, per-grade table, seeded corners and crease), `_WEAR` in CardFoil.shader with ±12% text protection, copies on the board by stamping each card's deck slot in the core's `cover` field (`CopyLooks`, `--copies-test`; old replays deal the same), condition labels in the deck editor, card detail, reading view, pack pulls and pack info, "Card grades: Full / Label only", `-gradeshowcase`. Not yet: slabs (they need grading, step 6), clicking a copy row to pick which copy the detail draws, and "light crease" in the plan's example line (creases are Damaged, so Played reads "scratched, faded").* | | |
| 6 | **Grade economy and the prized moment**: one price function, Grading Desk, used market, Sell desk, sparkle and chime | 3 | 4, 5 |
| | *Built 2026-10-31 (Economy Designer, UI Designer, Game Audio Engineer, Technical Artist specs): `Pricing` (basis × grade, Raw Near Mint = CopyPrice, Starlight 10 = 15,000), Paid per copy (pack shares, fees; gifts 0; old copies 2 × SellValue), `GradingDesk` (fees by basis, 3 duels or express ×2, 75/10/15 odds, crack/floor), `SellDesk` (min(V/4, Paid/2)), the 4th-copy rule by value, structure-deck refunds capped at half, `UsedMarket` (24 by epoch, wishlist rarer-only, special orders), the Grading Desk screen with SlabView and the reveal, the prized moment (_PRIZE sweep, seeded stars, five synthesized chimes tuned to the music), "Prized moments" setting. Decided: Raw 9 and 10 both read Mint and price alike, and only the visible grade triggers a moment, so nothing gives a hidden 10 away. Not yet: the used market filters by a card's first year (printings carry no date), a Sell-desk warning that lists the decks a copy is in (the desk refuses deck-needed copies instead), the Compact slab in the card detail panel (the detail shows the slab's grade in words).* | | |
| 7 | **Brackets**: tournament engine, bracket screen, Prize Pack, Library Locals | 2 | 3 |
| | *Built 2026-11-01, Library Locals only (spec docs/dev/BRACKETS-SPEC-2026-11-01.md; engine and harness by a Senior Developer agent, game by a Unity Architect agent): `Brackets`, `PrizePacks`, `DeckRatings` (src/Ygo.Engine/Campaign/), 36 hand-picked `local:` lines in library.txt (`--locals-check`), CPU matches by Elo odds and a coin fixed at entry, 1,000 DP entry, placement pay 900/650/400/150, Prize Packs (3 from the field at Rare+, 1 from a real tournament set, 1 sealed slab G9/G10; `--prize-check`: about 1,600 DP of cards a pack, 0 DP back), the bracket screen, Watch the final, a save mid-bracket replays a live match. `--bracket-sim`: title odds 30-45% at 7 of 12 cups; HAT, ABC, Synchro Peak, TOSS and Rabbits run 46-74% (their eras have no stronger legal decks). Not yet: Alley Brawl and the Midnight Open (step 9), `--bracket-matchups`.* | | |
| 8 | **Theme object**: `UiTheme`, shell screens converted whole, both-theme check | 1-2 | nothing |
| | *Built 2026-11-01 (a UI Designer's tokens, five Unity Architect agents converting a screen each): `UiTheme` (unity/Assets/Scripts/UiTheme.cs, roles and rules in docs/dev/UI-THEME.md) with `MainStreet` (today's colors: the campaign check's screenshots match, the differences being run-to-run content) and `BackAlley` (the plan's alley tokens); the campaign screen, the campaign scenes (Versus, Results, toss, guests...), the shop, the trader and the deck editor's frame read it, and `Ui.BoxButton`/`Ui.Input` too. `-themecheck` shoots every shell screen in both themes. Out of the theme on purpose: the duel board, card faces and rarity/grade colors, the circuit map's art, the Library's materials. Not yet: the alley's materials, stencil font, sound and the 300 ms crossfade (step 9).* | | |
| 9 | **The Back Alley**: events, the splash builder, the Alley list, ante, Moth's van, alley brackets, cast, scenes, mat and sound | 3-4 | 2, 3, 6, 7, 8 |
| | *Phase A built 2026-11-02 (docs/dev/ALLEY-SPEC-2026-11-01.md): the unlock and tab, three tables with seeded nights and two headliners, the Alley list, the turn-40 rule, held Street and Den stakes, 20 scenes, `-alleycheck`. Phase B built 2026-11-02: Orvel's Table and Moth's rail, Moth's van, Contraband Packs, the Unlisted binder, Penny, the HELD card, alley posters, the alley's look and sound. Phase C built 2026-11-02: Alley Brawl and the Midnight Open (16 players, a best-of-3 final), Contraband Pack prizes, Nyx's Table. Phase D to come.* | | |
| | *Headliners (docs/dev/HEADLINERS-2026-11-01.md): each event closes on an authored combo deck that needs a card banned today (Yata-lock, Frog FTK, SPYRAL...), with a route, a show layer and `--combo-goldfish`. Its Phase 1 is harness-only and can come first.* | | |
| 10 | **Library Phase 4**: the "Every Era" final | 1 | 3 |
| | *Built 2026-11-01 (Game Designer's check, Narrative Designer's scenes): campaign/library.txt's `final:`, `finalprize:` and `round:` lines, read by `Library.LoadFinal`: six rounds in order (Ines's goat-control 2005, six-samurai 2012, kozmo 2016, thunder-dragon 2019, eldlich 2021, the Ryzeal duo 2025), each its cup's boss under that cup's list and pool with no Ban Passes (`RoundRules`), opened by the last cup's clear (`final` event); only a deck of yours that's legal for the round can start it (no loaners); a lost round is played again and won rounds stay won (`lib:every-era.<r>`); round wins pay like cup matches, the clear 10,000 DP and 3 tokens and the Every Era poster (Time Wizard). Game: a gold plaque over the shelf (six year pips, Ilse) and the final's panel, the round versus, results, the Master theme a semitone higher each round. Story: Ilse arrives (`final`), her rules (`open:Every Era`), beats at `round:2/4/6`, a two-scene epilogue (`cupclear:Every Era`: this year's binder, the corkboard, a hint of the Champion's Ladder). Expected about 10 duels with the best measured decks (the Game Designer's 1/p sum); four rounds rest on 24-duel measurements and are worth re-running at 72. Checks: `--campaign-test` (rounds, order, replay, prize, events, poster) and `-campaigncheck` (locked plaque, the final opening, round 1's versus refusing a deck not legal in 2005).* | | |
| 11 | **Proof**: `--campaign-sim` with an earned/spent ledger, post-game pilots (minimal, collector, gambler), an arbitrage bot over every loop, a full QA run from a champion save | 1.5-2 | all |
| | *Run 2026-11-01, results in docs/dev/PROOF-2026-11-01.md: the ledger (`Profile.Earn`/`Spend`, every DP change), `--postgame-sim` pilots (minimal 19.3%, collector 68.3%, gambler 38.7% spent after 10 cups: met), `--arbitrage` (no buy→sell loop DP-positive: met), a 60-duel post-game QA run from the champion save (0 issues, ledger exact). Level-up DP made post-game duels pay over 300 and a Locals title run net positive; fixed the same day (post-game repeat and Locals level-ups pay tokens, not DP): cup farming 150 DP a duel, a Locals title run −100. Fixed along the way: the 4-copy cap with a copy at the Grading Desk, the Prize Pack slab reveal, Make it yours in the final's rounds.* | | |
| Later | Champion's Ladder; the character model; 32-player Championships; a No Limits alley table | | |

**About 26-32 sessions in all.** Steps 1b and 1c change the main campaign and can ship before the post-game.

## Targets

| What | Target |
|---|---|
| Library | 65% at openers, 60% in the middle, 55% at bosses, with the best tool |
| Alley, best option | Dumpster Row 75%, Basement 65%, Black Market 60%, Nyx 55% |
| Alley, the starter | Dumpster Row 65%, Basement 50%, Black Market 40% |
| Splash strength | +60 to +150 Elo over the base deck; above +250 means too many cards |
| Speed | A splash deck is at most 1 turn faster than its base, and never averages under turn 3 |
| FTKs | The CPU wins before your first turn in under 2% of duels |
| Endless duels | Prompt-cap stops under 0.5%, past turn 40 under 2%, draws under 1% |
| Brackets | A champion's best deck takes the title 30-45% at 8 and 15-30% at 16; the Elo sim within about 8 points of real duels |
| DP spent after 10 cups | A minimal player at least 15%, a collector at least 40% (6.6% today) |
| Loops | Never DP-positive |

**Checks for when it's built** (Economy Designer's E1-E11, with the Library's C1-C6):
- DP returned for a copy is at most half of what went into it.
- One price everywhere.
- Hidden grades are deterministic.
- Stakes are held before the duel.
- Odds are shown for grading, packs and ante.
- Alley legality never leaks into circuits, cups or stamps.
- Tournament and alley duels pay at most 300 DP a duel.
- New Game+ re-pays none of it.
- The structure-deck refund gets capped.

## Risks

1. **Copies instead of counts** touches selling, replacing, deck checks, saves and replays. Build it first and alone
   (step 4).
2. **Too much wear makes the game look dirty.** Grades are weighted to 7-9; existing saves get 7-10.
3. **The alley walls.** Splashed late decks can go past Elo 1947. Each event gets an Elo ceiling from Phase 0.
4. **Bot ratings undervalue combo decks.** The bracket calibration is the guard.
5. **Bots misplay Forbidden cards or loop.** The exclusion list, the turn cap and `--endless-test` cover this.
6. **Feel-bad losses to unseen contraband.** The versus screen shows it.
7. **Half-converted screens** mixing the two themes.
8. **Trade dress:** nothing that looks like a real grading company.
9. **Scope:** about 2.5 times the Library alone. It ships in the steps above.

## Decided (owner, 2026-10-31)

- **Your own cards are at stake only at Orvel's Table**: one spare card, never one a saved deck needs, and a lost card
  waits on Moth's rail for 5 duels at 1.5× its value. Street and the Den stake DP against the CPU's card.
- **Slabs play in decks**, so a Gem Mint Secret Rare sparkles on the board.
- **The Alley list is Traditional** (every Forbidden card at one copy), minus the exclusions above.
- **Ban lists and CPUs:**
  - CPUs are narratively exempt from ban lists and play their decks at full strength.
  - A CPU partner's default deck is exempt too.
  - A partner deck you edit must follow the list.
  - Ban Passes go up to 5, **earned only**: 3 at the first list, the 4th at the Advanced clear, the 5th at the Expert
    clear. DP doesn't buy them; old saves get their DP back. The game still needs another DP sink.
- **The alley's open questions (2026-11-01, the recommendations):**
  - the alley opens after your first Library cup clear;
  - no banned-card store: Moth sells a small shelf of rare, high-grade contraband;
  - brackets of 8 now, 16 after The Black Market;
  - at turn 40 in the alley, the higher LP wins.

## Decisions for you

The recommendation comes first. Where the designers disagreed, both sides are shown. Decisions 3, 4 and 2 are settled
above.

1. **Unlocks.** Posters now; the Library at the Gauntlet; the alley after your first cup clear; brackets per cleared
   cup. *(The Level Designer opened the alley right after the Gauntlet.)*
2. **The Alley list.** Traditional minus the exclusions, at one copy. *(The Economy Designer suggested no list at all;
   a No Limits table can come later.)*
3. **Can you lose your own cards?** The designers split:
   - **Recommended:** DP stakes at Street and the Den, where you win their card or lose your fee. Only Orvel's Table
     stakes one of your spare cards, never one a saved deck needs, and a lost card waits on Moth's rail for 5 duels at
     1.5× its value.
   - *Game Designer:* never.
   - *Economy and Narrative Designers:* yes, at every table, with a buy-back.
4. **Slabs in decks.** Slabs play, so a Gem Mint Secret Rare can sparkle on the board. *(The Narrative Designer wanted
   "crack it to play it", which would rule out the sparkle you asked for.)*
5. **The grading house's name.** Gauge & Gilt. *Also offered: Lumen Grading House, Tenfold Authentication, Hollis
   Archive Grading.*
6. **Grading results.** Some luck, shown on the screen, and a resubmit never comes back lower. *Or always the true
   grade.*
7. **Selling.** A capped Sell desk. *Or no selling.*
8. **The illegal store.** No store selling banned cards; ante is the source, and Moth sells a small shelf of rare,
   high-grade contraband as a collector's item. *Or a real banned-card store (about 120 cards).*
9. **Bracket sizes.** 8 now and 16 after The Black Market; 32-player Championships later.
10. **The turn cap in the alley.** At turn 40 the higher LP wins. *Or a draw.*
11. **Rarity upgrades.** Replaced by grades, the used market and special orders.
12. **Raw cards.** Show their condition's look, so a used Played card looks played. Existing copies get 7-10, so they
    barely change.
13. **The cast.** Moth, Baz, Odile and Penny as new characters; Nyx's Table as the boss; Kit as your guide.
14. **Gus's 2004 Scapegoat** (optional lore payoff): graded 6 Excellent and given back to Ines after your first slab.
15. ~~Buying Ban Passes.~~ Decided: earned only.
16. **Historic Rules.** A starter-screen choice, off by default, with two choices side by side, switchable later and a
    "Historic" stamp for circuits cleared under it.
17. **Historic starters.** 2011 editions of classic, warriors and zombies; Mekk-Knight labeled Beginner-only under
    Historic Rules.
18. ~~Recipe pricing.~~ Decided: packs by band plus a service fee on buying outright (built 2026-10-31). Grades and
    the Alley stay the late sinks on top.
19. **Card unlocks.** Rival gifts and set completion first, bond trades and the bounty board after. Gift cards are
    named per character, not random.
20. **Mechanics.** No Synchro, Xyz, Pendulum or Link gate beyond the shop's years and Historic Rules, plus the "ahead
    of your band" tag.
21. **Measuring.** Per-source and per-sink counters in `--campaign-sim`, and DP by band re-measured under Historic
    Rules (today's figures are from open play).
