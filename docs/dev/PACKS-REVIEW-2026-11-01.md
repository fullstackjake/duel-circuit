# Card packs review (2026-11-01)

The owner asked: "Should we redo the card packs to better match the game, story, pacing, balance?" Three agents
answered read-only:
- **Economy Designer:** DP and pacing (section 3, added when its report arrived);
- **Game Designer:** progression and choice;
- **Narrative Designer:** story and world.

Nothing was changed by the review. It was revised and built on 2026-11-01: see
[SHOP-REWORK-SPEC-2026-11-01.md](SHOP-REWORK-SPEC-2026-11-01.md).

## Verdict

**Keep the pack engine. Change how the shop organizes and presents packs.** A full redo isn't worth it:
- **The engine is fine:** real sets by year, foil-slot odds, and a wishlist ranking that estimates cards per pack.
- **Packs aren't a real choice:** DP never runs short, the 71 sets are sorted only by year (not tied to the circuit
  or its opponents), and a real set is never the best way to build a deck, so pulls don't change what you play.
- **The shop has no voice:** the story calls it Gus's shop, but the screen reads like a neutral catalog. Nobody is
  behind the counter, big pulls get no reaction, and a new era arrives as a header change.

## 1. Game design (Game Designer)

**Evidence:**
- **DP isn't a limit.** The QA runs spent 6.6-8.7% of earnings by the Gauntlet clear and banked 277k DP. When DP never
  runs out, the dominant play is whatever is guaranteed, and packs become a fun purchase, not a decision.
- **What dominates depends on your state:**
  - after a recipe unlocks: its recipe pack (9 missing cards first, extras refunded) or Complete / Make it yours;
  - before that: selection packs, which cover every opened circuit plus the whole next band;
  - instead of either: a loaner, which needs no cards at all.
- **Pulls don't change decks.** The QA stand-in bought about 3 packs a visit while holding 3-14k DP, pulled DR04
  commons next to a Mekk-Knight starter, and never rebuilt around a pull.
- **The default "newest set" is often a poor pack:** Hidden Arsenal 1, 5 and 6 are all Super Rare, so the foil reveal
  means nothing.
- **The era link is weak:** ShopYear is the band's era plus a year a level. It ties sets to the calendar, not to
  Brask, Tink or the Six Samurai. Players see 71 look-alike sets (code, date, card count).
- **The two kinds of player:**
  - the pack lover gets cheap, real, nostalgic packs, but no follow-through ("fits your deck", "Tink plays this",
    "set 41/60");
  - the deck-wanter goes Recipe Book, then recipe pack or Complete, and never opens a real set.

**Recommendations, by value:**
1. **A featured shelf per circuit** (high value, about a day): a data file (e.g. `campaign/featured.txt`) names 3-4
   real sets per circuit that hold its opponents' archetypes and signatures (e.g. Cybernetic Revolution for Tink, a
   Six Samurai set for Novice), with a Gus or Kit line per shelf. Everything else moves to a collapsed "Back catalog"
   tab, so nothing is deleted.
2. **Reveal and list tags** (high, about half a day): "in your deck / recipe", "<opponent> plays this", "set progress
   n/N". It ties into set completion (docs/dev/CARD-UNLOCKS-SPEC-2026-11-01.md) and gives pack lovers a reason to build.
3. **One job per pack type** (medium, small effort):
   - real sets: cheapest per card, foils, set completion and the poster;
   - selection packs: only the current and next circuit, labelled "the decks you'll face";
   - recipe packs: finishing a deck you've already chosen.
4. **A booster box per featured set** (medium, about half a day): 24 packs at about 20-25% off, with one guaranteed
   top-rarity foil. A DP sink for hoarders and the pack lover's chase.
5. **All-foil reprint sets** (low to medium, small effort): badge Hidden Arsenal and similar sets as "reprint/foil
   set", or drop the lowest tier's foil styling so the reveal still builds to the last card.
6. **Make DP a real limit** (economy, section 3): without it, items 1-4 add flavour but don't force a choice.

**Skip:** packs themed to the next opponents. They'd duplicate selection packs, and a featured shelf of real sets does
the same more authentically.

**Stale comments:** two comments say selection packs start "from the Expert band on" (ShopScreen `Available()`,
`SelectionPacks.Bands`); they start at the beginning of the campaign. The same note is true for the OCG import shelf.

## 2. Story and world (Narrative Designer)

**Evidence:**
- **No voice on the screen.** ShopScreen.cs's header just says "CARD SHOP", and the footer is instructions.
- **The story already builds the shop as a place:** Gus's glass case (`bond-old-gus-5`, `first-set`), "buy packs
  over there" (`welcome`), and Hollis's "newer packs in the shop" at each era step.
- **Era openings are silent:** the shop changes only its header ("sets up to 2015") and a footer count.
- **The pack reveal is well shaped** (best card last, a summary, a sound on rare pulls), but a Secret Rare gets the
  same plain line as a pack of commons.
- **Selection Packs show a year** ("2009"), which reads as a real set's release date.
- **Kit**, "binder of spares always under one arm", appears only as a tag partner.

**Ideas, by value for effort** (no economy change in any):
1. **One line from Gus or Kit on a big pull** (Ultra or better, or a card an upcoming opponent plays), under the
   opening summary. At most one per opening, none on ordinary packs. High value, low effort.
   - `old-gus: Secret Rare. Hold it by the edges, kid. I've got a case for those.`
   - `kit: That's in Vey's deck. He's going to hate seeing it across the table.`
2. **"New arrivals" when an era opens:** newly opened sets go to the top for a while, with a Gus line in the footer.
   - `old-gus: Truck came in. 2015 stock's on the top shelf. Don't open it all at the counter.`
3. **Gus's voice on the shop screen:**
   - rename the header (e.g. "Gus's Cards");
   - make the footer a rotating Gus remark;
   - call Selection Packs "Gus's house packs: what the regulars are playing", and drop the fake year.

   Low effort.
4. **Faces on the side counters:** Kit runs the Card Trader (his binder of spares), and Hollis the Grading Desk ("I'd
   grade it, but you've outgrown my rubric"). Medium effort: portraits and lines per screen.
5. **Old 2005 stock:** after the Goat Cup, a sealed 2005 box from under the counter, at normal odds and price. It ties
   to Ines and the card she lost in 2005. Medium effort.

**Watch for:** no "as you know" lines or tutorial patter in the footer, and ration the pull lines so they stay a
moment.

## 3. Economy and pacing (Economy Designer)

**Verdict: tune, don't redo.** Real-set boosters fit the shop story, the era gate and set completion. The problem is
prices that disagree with each other: **selection packs quietly undo the band pricing of recipe packs.** All its
figures are estimates until a sim run or telemetry confirms them.

**Where DP and cards come from** (build/qa-night5's run, up to the Gauntlet clear; this was before band-priced recipe
packs):
- **DP:** earned about 302k, spent 26.3k (8.7%).
  - Of the spend: real boosters 9,900 (37%), bought Card Tokens 7,000 (27%), Make it yours 7,070 (27%), one structure
    deck 1,500 (6%).
  - Recipe and selection packs: nothing. The QA stand-in always opens 3 packs of the newest set, so those paths have
    never been tested.
- **Cards:** of 760 copies, boosters gave about 516 (68%); the structure deck 40, tokens about 33, the starter and
  rewards the rest.
- **Boosters never built a played deck:** the stand-in was still on the Mekk-Knight starter at duel 133. Its power came
  from tokens and loaners.

**Cost per useful card** (an estimate):

| Band | Recipe pack | Real booster | Selection pack |
|---|---|---|---|
| Novice | 33 DP | about 150-300 DP at best | about 30-45 DP |
| Intermediate | 67 | same | same |
| Advanced | 111 | same | same |
| Expert | 167 | same | same |
| Master | 278 | same | same |

- **Boosters** are the worst path up to Advanced and about even with recipe packs at Master. They never win for
  staples.
- **The bigger leak is selection packs** (DeckAccess.cs ~234): 200 DP flat for the same opponent deck's cards, on sale
  for every opened circuit plus the next band. At Master, about 20 selection packs (about 4k DP, 2 wins) do the job of
  22.5k in recipe packs, so the band pricing is bypassed.

**Duels to complete a recipe, against the plan's table:**
- **Novice:** about 1,630-1,830 DP, which is about 3 wins and 11% of the wallet, against a target of 6-10 duels and
  25-40%. Under target.
- **Master:** about 22.5k DP, about 10 wins. That fits, but only if selection packs didn't exist.

**Duplicates and refunds are healthy.** 4th copies returned about 11% of booster spend, a copy sells for at most half
its share of the pack, and packs can't make DP.

**Packs aren't a late sink.** One Master win buys about 15 boosters. Set completion is the only late reason to open
them: a 100-card set with about 10 Secret Rares takes roughly 200 packs, about 30k DP.

**Changes, ranked:**
1. **Price selection packs by the deck's band** (`PackPriceFor`): 200 / 200 / 400 / 650 / 1,000 / 1,600 at
   Master and the Gauntlet. That's about two-thirds of a recipe pack, since they're random rather than missing-first.
   *Risk:* players who bought next-band packs cheaply see a jump.
2. **Raise Novice recipe packs from 300 to 450**, keeping Beginner at 300. Completion becomes about 2.7k DP, about 5
   wins. *Risk:* a slower second deck for new players.
3. **Price real boosters by the set's year:** ≤2008 150, 2009-13 200, 2014-18 300, 2019-22 450, 2023+ 600. Old sets
   stay a cheap back shelf. *Risk:* New Game+ players feel taxed.
4. **Fix the shop's headline set.** Sets with no Commons (Hidden Arsenal, Yugi's Legendary Decks) top the list and
   open 9 Super Rares for 150 DP. Sort core boosters first, and charge 2× for sets with no Commons.
5. **First, make QA buy recipe and selection packs, and give `--campaign-sim` a ledger by source** (plan step 11).
   Measure before shipping 1-3.

## A combined plan (to revise when we're ready)

All three reviews meet in one shape: a **featured shelf per circuit** with **Gus's voice** and **pull
tags**, plus **one job per pack type**. Rough order:
1. **Cheap voice** (half a day): the Gus/Kit pull lines, the shop's header and footer, Selection Packs without the
   fake year, and the stale comments.
2. **Featured shelves and the back catalog** (a day): `campaign/featured.txt`, New arrivals when an era opens.
3. **Tags** (half a day): in your deck / recipe, an opponent plays this, set progress (with card unlocks' set
   completion).
4. **One job per pack type** (small): narrow selection packs to the current and next circuit.
5. **Booster boxes** (half a day) and **all-foil set badges** (small).
6. **Prices, measured first:**
   - QA buys recipe and selection packs, and the sim gets a ledger;
   - then band-priced selection packs, Novice recipe packs at 450, and boosters by the set's year;
   - core boosters sorted first, and all-foil sets at 2×.
7. **Faces on the counters and the old 2005 stock:** they need portraits and lines; later.
