# Post-game proposal: the Format Library (2026-10-30)

A proposal, not built. Four agency-agents designers worked on it, and their claims were checked against the repo
before going in here.
- **Game Designer** (lead): the options and the recommendation.
- **Narrative Designer**: the story.
- **Level Designer**: the cups, built from the existing decks, lists and Elo ratings.
- **Economy Designer**: the DP, from the code's real prices.

Nothing in the game changed.

## The problem

Today the campaign ends at the Tier 0 Gauntlet and the champion scenes; New Game+ (the circuits start over, you keep
everything) is the only thing left. A champion holds about 277k DP (337k after New Game+), has spent 6.6% of what they
earned (19.7k of 296.8k in the 10-30 run), and replays solved challenges with a collection that crushes the early
bands. Meanwhile 75 history decks with their stories and 36 historic ban lists are barely seen (era mode is off by
default), and seven duelists have no circuit at all (Lady Clausolas, Ringmaster Pepe, Agent Helix, Duchess Ilse, Mika,
Ilara Gale, Patron Orvel).

## Options considered

| | Loop | For | Against |
|---|---|---|---|
| **A. Champion's Ladder** | Defend the title on a 16-rung ladder of the strongest duelists; lose and drop two rungs | Small to build; uses the unplaced duelists | Only top-tier mirrors; no reason to build anything new |
| **B. Format Library Cups** (recommended) | Hollis opens a back room of historic formats: 12 cups, each under its own ban list and card pool, whatever the save's era setting | Makes the collection matter again; shows off the history decks and lists; reuses era legality, loaners, Stuck? advice | Medium effort; the late cups are hard (see Walls) |
| **C. Gus's Weekly Open** | A generated circuit each week with a twist | Endless | Every generated circuit needs measuring before it ships: open-ended cost |

**Recommended: B, with A later as its capstone** (after the "Every Era" final, Duchess Ilse points to a ladder).

## The cups (Level Designer, checked)

All 12 open at once, shelved by year. Only the "Every Era" final is gated, behind all 12 clears. Each cup's anchor list is
its boss deck's own list (names as in `lflists/history.lflist.conf`), and the pool is every card released up to the
date shown. CPUs play their decks as built, as everywhere. Matches are listed in Elo order (`campaign/deck-ratings.txt`).

| # | Cup | Anchor list | Pool to | Matches (boss last) |
|---|---|---|---|---|
| 1 | Goat | TCG April 2005 | Sep 2005 | stall-burn 1367, chaos-yata 1414, gadgets 1480, **goat-control 1401** (Ines) |
| 2 | TeleDAD | TCG September 2008 | Aug 2009 | lightsworn 1368, machine-re-volt 1435 (Hollis, a new deck for him), blackwing 1430, **teledad 1465** |
| 3 | Synchro Peak | TCG March 2011 | Feb 2012 | frog-ftk 1054, infernity 1351, plant-synchro 1418, **six-samurai 1491** |
| 4 | Rabbits & Rulers | TCG September 2012 | Dec 2013 | spellbook 1180, inzektor 1416, dino-rabbit 1438, **dragon-ruler 1522** |
| 5 | HAT & Shaddoll | TCG July 2014 | Dec 2014 | qliphort 1225, burning-abyss 1297, hat 1478, **shaddoll 1592** |
| 6 | Nekroz & PePe | TCG April 2015 | Jul 2016 | domain-monarch 1202, performapal 1412, nekroz 1481, **kozmo 1613** |
| 7 | ABC to SPYRAL | TCG March 2017 | Oct 2017 | zoodiac 1289, true-draco 1435, spyral 1466, **abc 1585** |
| 8 | Link Dawn | TCG September 2018 | Sep 2019 | altergeist 1237, sky-striker 1397, danger-dark-world 1618, **thunder-dragon 1693** |
| 9 | TOSS & Eldlich | TCG June 2020 | Jun 2021 | salamangreat 1376, orcust 1472, adamancipator 1526, **eldlich 1764** |
| 10 | Tearlaments | TCG October 2022 | Nov 2022 | floowandereeze 1387, exosister 1531, spright 1693, **tearlaments 1947** |
| 11 | Kashtira & Snake-Eye | TCG June 2023 | Mar 2024 | purrely 1179, unchained 1598, snake-eye 1612, **kashtira 1828** |
| 12 | Ryzeal | TCG December 2024 | Mar 2025 | maliss 1392, yubel 1653, fire-king-snake-eye 1654, **ryzeal 1867** |

Notes:
- A few CPU decks are over their cup's list by 1-9 cards ("visitors": chaos-yata by 9, inzektor by 3). They keep their
  cards as built; a player's loaner of the same deck is made legal.
- maliss, domain-monarch and performapal were built after their cup's pool date. Check that a pool-filtered build of
  each still works.
- Beating Pepe, Clausolas and Helix in cups 6-7 unlocks their recipes mid-cup.

**Rules:**
- **Loaners are allowed** for a cup clear, at half DP as now.
- **A Format Master stamp per cup needs a clear with a deck you built.** A structure deck counts as built.
- **The "Every Era" final:** six rounds, each under its own era's list and pool, with a deck you built for each
  round. No loaners and no Ban Passes. A lost round is replayed.
  - Rounds, in order: goat-control 1401, six-samurai 1491, kozmo 1613, thunder-dragon 1693, eldlich 1764,
    ryzeal 1867.
  - Tearlaments stays the boss of cup 10 only; only a mirror answers it.

## Walls: the main design risk

From cup 5 on, the Elo odds for the best non-mirror loaner against each boss are 20-48%. The Level Designer's figures:

| Cup | Boss | Best non-mirror option | Elo odds |
|---|---|---|---|
| 5 | shaddoll | hat | 34% |
| 7 | abc | spyral | 33% |
| 9 | eldlich | adamancipator | 20% |
| 11 | kashtira | bystial-dragon-link | 31% |
| 12 | ryzeal | tenpai-dragon | 25% |

The real answer there is the **mirror loaner**. One measured mirror so far: the Tearlaments loaner beat Kitkallos's
Tearlaments 57% as built. Era-limited mirrors haven't been measured.

Tools, in order (CPUs never weakened):
1. **Toss tips** where they help. Going second helps against six-samurai (+19), abc (+19) and dragon-ruler (+12).
   It helps little against eldlich, thunder-dragon, tearlaments, kashtira and ryzeal.
2. **"Make it yours"** for the mirror. It needs format-aware legality (see the risks below).
3. **If a boss still measures under 55%:** make it an optional "Champion's table" match. The Format Master stamp
   requires it, but the cup clear doesn't.

Targets with the best tool: at least 65% at openers, 60% in the middle and 55% at bosses.

## DP economy (Economy Designer, from the code's prices)

**Starting state** (10-30 run):
- At the Gauntlet's clear: 277,302 DP, 736 cards, 6.6% of earnings spent.
- After 48 New Game+ duels: 336,882 DP, 934 cards.
- New Game+ pays about 1,250 DP a duel, because it re-pays first wins and prizes.

**What an era-legal deck costs, from zero:**

| Format | Make it yours / buy what's missing | Recipe packs (300 for 9 cards) | Old-set boosters (rough) |
|---|---|---|---|
| Goat 2005 | about 2,500 | 7 packs = 2,100 | about 38k |
| Dino Rabbit 2012 | 3,370 | 2,100 | about 66k |
| Tearlaments 2022 | 6,460 | 2,100 | about 133k |

**The key finding:** recipe packs cap any 55-card deck at about 2,100 DP, under 1% of the savings, so **decks can't
absorb the DP pile.** Boosters are a hobby sink, not a requirement. Card prices should stay the same on every screen,
so the sinks that matter have to be cosmetic:
- **Rarity upgrades:** raise an owned copy to a rarity it was really printed at (Ultra 1,000 / Secret 2,500 /
  Starlight 10,000 [placeholder prices]). With 934 copies, this sink has no practical bottom.
- **Era mats** for your side of the board: 5,000 each, 12 eras.
- **Library shelf engravings:** 2,000 a cup.

**Faucets (kept small; a champion doesn't need DP):**
- **First runs** are free.
- **Repeat runs** of a cleared cup cost 1,000 to enter [placeholder]. That fee is the brake on farming.
- **Match wins** pay 600, plus the existing 200 first-win bonus.
- **A cup's first clear** pays 2,500 and 2 tokens. A first clear with a deck you built adds 1 token and a trophy plate.
- **The "Every Era" final** pays 10,000 and 3 tokens.
- **Repeat runs** pay match wins only.
- **Library first-clear flags** live apart from `MatchWins`, so New Game+ never re-pays them.

**Library Card Trader:** inside the Library, tokens buy only cards printed by the format's end, reusing the trader's
era filter.

**Targets after 10 cups** (spending ÷ (starting wallet + earnings)):
- at least 30% for a player who buys cosmetics;
- at least 12% for a minimal player, against 6.6% now;
- no repeatable source beats New Game+.

**Risks, as checks for when it's built:**
- **C1:** opening packs never nets DP. The booster refund is capped at half the price. The structure-deck refund
  isn't capped (`Profile.BuyStructureDeck`); that's harmless now, since every card enters as a Common worth 5 DP, but
  cap it anyway.
- **C3:** farming a cleared cup pays 300 DP a duel or less.
- **C5:** "Make it yours" today checks only `ShopYear` and `EraLimited` (`CampaignState.QuoteOwn`/`Own`). In the
  Library it must price and validate against the cup's own format (`DeckRules.FormatEnd` of the anchor list).
  Check: at every cup, no stand-ins and no `Problems()`.
- **C6:** New Game+ never re-pays a Library first clear.

## Story (Narrative Designer, checked: every speaker id exists in `personalities/`)

**The spine:**
1. **The morning after.** The case holds nothing new.
2. **Hollis opens the back room.** It's the Library: one binder per year since 2004. This is the in-world reason
   the save's era setting doesn't apply: each cup brings its own list and pool.
3. **Gus stops teaching.** He becomes the voice of the Stuck? advice.
4. **Ines opens Goat**, the format she and Gus played together.
5. **Vera learns patience** in the slow formats.
6. **Clausolas, Pepe and Helix** each own one year and play nowhere else.
7. **The shelf fills with stamps.**
8. **The "Every Era" final**, framed by Duchess Ilse.
9. **The epilogue.** Hollis starts this year's binder with the champion's recipe, and Gus moves the champion's name
   above his own on the corkboard.

**What it changes for each of them:**
- **Gus:** from teacher to proud spectator.
- **Vera:** from rival to student of patience.
- **Hollis:** from rules voice to curator.
- **Ines:** gets her Goat rivalry back.
- **Ilse:** from distant title-holder to the one who points to the ladder.

**Draft scenes, in `campaign/story.txt` format.** The opening scene:
```
scene: library-opens
when: library
-: The shop's back room smells of old sleeves. Binders line the shelves, one per year, back to 2004.
old-gus: Morning, champ. Nothing left in the case you don't own, huh?
hollis: Then you're ready for this. Every Forbidden and Limited List since 2004, and the decks that ruled under each.
hollis: The circuits sent you each era's champions. The Library sends you their whole formats.
hollis [era]: Your era setting waits at the door. Each cup brings its own list and its own card pool.
hollis [open]: You never played by the lists. In here, everyone does. Each cup brings its own list and card pool.
vera: So it's old decks. Old, slow decks.
old-gus: Old, slow decks beat me plenty. Go on. I'm out of lessons, kid. These binders aren't.
vera: ...Dibs on the 2009 shelf.
```

Cup intros and clears, voiced by each boss. Two samples:
```
scene: cup-goat
when: open:Goat
ines: Goat Format. Gus and I played this at a folding table for a whole summer. Scapegoat is a turn, not a card.
old-gus: She's not wrong. She's never once been wrong. It's exhausting.

scene: cup-pepe
-: A hippo plushie sits on the 2016 shelf. Then it sits on a shoulder, under a top hat.
performapal: Ladies, gentlemen, champion! In 2016, every table was my stage.
performapal: Scales up, five monsters down, every turn. No intermissions!
vera: Is he doing a bit? Is it the whole duel?
performapal: The whole duel, my dear. Then the bow.
```

The final and the epilogue:
```
scene: every-era-clear
when: cupclear:Every Era
ilse: Every era, on its own terms. I waited a long time for someone to do that to me.
hollis: One shelf left: this year's. I'll need a binder, and a recipe for it. Yours.
ines: Gus. Your student just beat the Duchess. Say something.
old-gus: Already did. Moved the kid's name above mine on the corkboard. Took my glasses off for it and everything.
```

The champion scene's last line would change to point at the Library, with New Game+ kept as an option:
`old-gus: Hollis has a back room he's been itching to show you: the Library, on the campaign screen. Or start fresh from the first table with New Game+.`

The Narrative Designer also wrote lines for:
- Chaos 2004, TeleDAD, Plant Synchro, Dino Rabbit, Dragon Ruler, TOSS, Drytron and Tearlaments;
- Clausolas's, Helix's and Ilse's first scenes;
- the final's intro.

Its format list differs a little from the Level Designer's cups (its own Chaos 2004 and Drytron 2021 cups; separate PePe
and SPYRAL years). The cups above are the structure; its lines get re-voiced to fit them when the story is written.

## Decisions for you

1. **Option B,** with the ladder later?
2. **The late walls.** Accept that bosses from cup 5 on are answered mainly by mirror loaners, with the optional
   "Champion's table" as the fallback? Or does a cup need a non-mirror answer of 55% or more first?
3. **Cosmetic sinks.** Are rarity upgrades, era mats and shelf engravings worth building? They're the only way the
   post-game moves the DP pile.
4. **Ilse.** She plays house decks only (Synchron, Zombies, Gadgets, Modern), so the proposal makes her the final's
   storyteller, not an era opponent. Or give her a deck for a seventh round?
5. **Entry fees** on repeat runs (1,000 DP), or none?

## Phased build plan (rough effort, in sessions of focused work)

| Phase | What | Effort |
|---|---|---|
| 0. Measure first | Harness support for Library cups (a list and pool per match); `--loaner-check` on each cup's loaners; `--circuit-study` of the top 3 loaners plus the mirror per cup (about 48 runs); `--wall-study` where under 55% | 1 day, mostly unattended runs |
| 1. Core | `campaign/library.txt` format definitions; cup flow reusing circuits; per-format legality (era-mode machinery, forced by the cup); format-aware Make it yours (C5); Library screen; story triggers (`when: library`, cup names in `open:`/`cupclear:`); champion line | 2-3 days |
| 2. Goals and rewards | Format Master stamps (built-deck rule), trophy shelf, prizes, repeat fees, Library trader filter, first-clear flags apart from `MatchWins` (C6); Hollis's new deck | 1-2 days |
| 3. Sinks | Rarity upgrades, era mats, engravings; structure-deck refund cap (C1) | 1-2 days |
| 4. Final | The "Every Era" final (six rounds) and epilogue | 1 day |
| 5. Proof | `--campaign-sim` pilots past the Gauntlet (DP targets); a full `-qaplay -qafull` from a champion save | 1 day, unattended |
| Later | The Champion's Ladder (option A) | 1-2 days |

Also found, unrelated: the README's campaign section still lists the Gauntlet as Nekroz, PePe, SPYRAL and Ishizu;
`campaign/circuits.txt` has Eldlich, K9, Tearlaments and Ishizu.
