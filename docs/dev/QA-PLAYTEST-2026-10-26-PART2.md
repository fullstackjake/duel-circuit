# QA playtest, part 2: the rest of the campaign (2026-10-26)

Part 1 ([QA-PLAYTEST-2026-10-25.md](QA-PLAYTEST-2026-10-25.md)) played the first three circuits. This part plays on
from a fresh Mekk-Knight Invoked save (a test folder; your own save untouched) through everything a player meets
after that:
- **the Card Shop**, with real DP: wishlist order when there's a wishlist, else the newest set, three packs after
  each circuit cleared, each revealed and checked;
- **the Card Trader**, a token for a card the deck plays when there's one;
- **the deck editor**: Recipe for a wishlist, Ban Passes and Auto-fix when a circuit's ban list refuses the deck,
  and a rebuild (Selection Packs, Recipe, Auto-fix) after three straight losses to one opponent;
- **the Tag Cups**, as they open, with the strongest partner you've beaten;
- **Gus's lessons**;
- **circuits past the Town Tournament**, through the ban-list bands.

As in part 1, a skilled stand-in made your decisions and gave them **through the prompt panel the way clicks are**
(the screens' own buttons for the shop, trader and editor). Everything ran at 16/s with animations on. The same
watchers ran: errors, hangs, stuck animations, stalls, left-over arrows and chain numbers, rejected answers, save
mismatches; plus DP, collection and token bookkeeping in the shop and trader. Afterwards the replays went through the
blunder audit, and I read the screenshots.

`-qaplay <out> -qagames N -qafull` plays it; `-qacontinue` picks up a save already in the `-mods` folder (each fix
below was rebuilt and the campaign carried on from where it was).

## Results

**80 duels (48 won, 32 lost) and 9 lesson tries**, one save carried through six runs (a fix rebuilt between each):

| Band | Circuits (ban list) | Result |
|------|---------------------|--------|
| Beginner | Beginner Circuit, Rookie Cup, Town Tournament, Regional Qualifier (house rules) | All cleared |
| Novice | Underdog Open, Trap Masters Cup, Small Engines Invitational (TCG March 2011) | All cleared; Ban Passes, then Auto-fix for era-limited releases |
| Intermediate | Synchro Dawn Classic, Xyz and Link Regionals, Link Age Open (TCG April 2015) | All cleared; two rebuilds after three straight losses (Infernity, Salamangreat) |
| Advanced | Golden Age Championship (TCG October 2019) | Cleared, then stopped |
| Tag Cups | Card Shop Tag Night, Novice Tag Cup, Intermediate Tag Cup | First two cleared; the third 2 of 3 (Sky Striker + Salamangreat won four times) |
| Lessons | Gus's three | All passed after fixes 3 and 4 (failed before) |

The end: Level 20, 452 cards from 48 packs, 13 Card Tokens traded, 57,170 DP left. Every duel decision went through
the prompt panel's buttons; none had to be answered behind it.

The watchers found **no** errors, hangs, stuck animations, stalls, left-over arrows or chain numbers, rejected
answers or save mismatches. Every pack's DP (price, refund for 4th copies) and collection count matched what the pack
screen said, and every trade took exactly one token. The bugs below came from the flow itself (the Duel button
refusing a deck the editor couldn't fix), the screenshots, and the replays.

*(Screenshot tag-duel removed 2026-11-08: it showed official card art.)*

## Bugs found and fixed

*(Screenshot card-trader removed 2026-11-08: it showed official card art.)*

### 1. Auto-fix, Build and Recipe ignored era-limited releases (high)

With **era-limited** on (the default), a circuit with a ban list only allows cards released by its format. The Duel
button checks that; the editor's problems box shows it. But **Auto-fix said "Nothing to fix: the deck is legal"**,
and Build and Recipe would happily use unreleased cards. A player at the Underdog Open (TCG March 2011) with the
Mekk-Knight starter had no tool that worked: "Aleister the Invoker: not released yet" and nothing to press.

**Fixed:** Auto-fix removes cards not out yet ("Removed 3 Aleister the Invoker (not released yet)"), and Build and
Recipe only use cards that are. The problems count the editor's checks use includes them too.

### 2. Auto-fix emptied the Extra Deck and left it empty (medium)

All 15 of the Mekk-Knight Extra Deck are post-2011: Auto-fix took them out and refilled only the Main Deck, though
you owned Cyber End Dragon and others. **Fixed:** the Extra Deck slots it empties go to the best Extra Deck monsters
you own.

*(Screenshot autofix-empty-extra removed 2026-11-08: it showed official card art.)*

### 3. The CPU (and your stand-in) never answered Ash Blossom with Called by the Grave (medium: AI)

Gus's first lesson failed twice: the stand-in let Ash Blossom negate its search with Called by the Grave in hand. The
chain logic scored a response by the card it answers (Ash: a 0 ATK monster, "not a threat"), so it never saw that
the threat was the negation of its own card. **Fixed:** when the opponent's link negates one of ours, a negate or a
GY negate (Called by the Grave, "in your opponent's GY ... negated") on it scores high. The CPU now passes lesson 1.

*(Screenshot lesson-1-failed removed 2026-11-08: it showed official card art.)*

### 4. Set Jar of Greed fired on its own turn (low: AI)

The second lesson's point: a set Jar of Greed is a bluff all turn, then a card in the opponent's End Phase. The CPU
flipped it in its own Main Phase (the planner liked the card now). **Fixed:** a set Trap whose only job is drawing
waits for the opponent's End Phase. With a planner, the CPU now passes all three lessons in the lesson test (with
Chain ON for the second, as Gus asks).

### 5. Recipe kept the old deck's story and starters (low)

Recipe turned the Mekk-Knight deck into Lightsworn, but the editor still showed the Mekk-Knight "How it worked" and
counted opening hands with the Mekk-Knight starters ("0 starters in 40 cards (Aleister the Invoker...)").
**Fixed:** the deck takes the recipe's story and starters (its Ban Passes and label stay).

*(Screenshot recipe-kept-old-story removed 2026-11-08: it showed official card art.)*

### 6. A CPU decked itself with two Pot of Desires (low: AI)

Sky Striker's Ace Raye cast Pot of Desires (banish the top 10) twice, the second at 18 cards, and lost to an empty
Deck on turn 12. The check kept only 4 cards back. **Fixed:** a big Deck cost keeps as many back as it takes.

### 7. Small things

| Where | What | Fixed |
|-------|------|-------|
| Pack opening | The top row's 5th card sat under the card info box (its NEW badge cut off; picture below) | Cards, title and buttons moved left, clear of it |
| Duel button | "Raigeki: 1 copies, Forbidden" | "1 copy" |
| Deck editor | "Back to duel" when you came from the campaign | "Back" |
| Deck editor | "Test against a bot ... with abc" | The deck's name ("ABC / Union Hangar") |
| Card Shop | "Selection Pack: Frog Ftk", "...: Abc" | The deck's own name ("Frog FTK"), read from the recipe's first line |
| Deck editor | The starter's story showed "2025 ·" and an empty "Why it mattered:" | Parts a deck doesn't have are left out |
| Recipe | "You don't own enough of any unlocked recipe yet. Open more packs." | Says what counts (a quarter of its cards) and points to Selection Packs |
| New campaign | The era-limited help only mentioned Card Trader tokens | It says decks follow it too, from the Underdog Open on |
| Recipe | "lightsworn (built-in): you own 57%..." | The deck's name ("Lightsworn") |
| Tag Cup | Your partner "(gadgets · beaten 4x)" | "(Gadgets · beaten 4x)" |
| Lessons | A lesson tried and failed still said "Not tried yet" | "Not passed yet" (only passes are saved) |

*(Screenshot pack-before-fix removed 2026-11-08: it showed official card art.)*

## Decided and built (later the same night)

| Question | What you decided | Built |
|----------|------------------|-------|
| 1. Era-limited vs the circuits' decks | Card Tokens buy any card of any era; decks answer to the ban lists and Ban Passes | Era-limited is now an opt-in **Historic challenge**, off by default |
| 2. Auto-fix saving over your deck | An overwrite or save-as-new choice | Save over most of a deck asks **Overwrite it** or **Save as new**; the new deck becomes your campaign deck |
| 3. Packs and tokens | More tokens, any era | A token per circuit and Tag Cup cleared (3 in the late bands), on top of levels; cards cost 1-3 tokens by rarity |
| 4. DP | A money pit | DP buys Card Tokens (1,000 DP, then 500 more each) and three extra Ban Pass slots (4,000 / 8,000 / 12,000) |
| 5. Tag partners | Pick partners, bonds, an editable partner, special guests | Remembered pick; bonds (3 wins: their other decks, 5: sharper play); Kit, who plays your own decks; four special guests to duel, tag against, then partner |

## Questions for you (as asked)

1. **Era-limited vs the circuits' decks.** Bands follow measured deck strength, not release year, so the Novice
   band (era 2011, TCG March 2011 list) has Burning Abyss (2014), Zoodiac (2016), Altergeist (2018), Runick Spright
   and Yummy (2022-26). With era-limited on, **you** are held to cards out by early 2012 while they aren't. The
   Mekk-Knight starter (2017-18 cards) is gutted at the Underdog Open: Auto-fix leaves a pile of 2007-2011 pack
   commons. Options: (a) era-limited only limits the shop and trader, not deck legality; (b) each circuit's format
   follows its decks' years; (c) keep it, and say so on the starter tile ("modern: plays freely until the ban-list
   circuits").
2. **Auto-fix saves over your deck.** After a ban list gutted it, Auto-fix + Save replaced "Mekk-Knight Invoked"
   with the rebuilt pile under the same name and story. Should a big Auto-fix (more than half the deck) save as a new
   deck instead?
3. **Shop sets.** With no wishlist the newest set early on was a Hidden Arsenal-style set (all Super Rares); nine
   packs of it built nothing. Is the Selection Pack (one deck's cards) the intended path? Nothing points a new player
   there except, now, Recipe's message.
4. **DP.** 57,170 DP unspent at Level 20 after 48 packs. The stand-in only buys 3 to 6 packs a visit, but even so DP
   looks plentiful from the Novice band on; packs may be too cheap for what wins pay.
5. **Tag duels.** Your partner is the beaten duelist with the best record against you, so mostly early-campaign
   characters and decks (Ryo's Classic, Tink's Gadgets) against Intermediate teams. The Intermediate Tag Cup's last
   match was lost four times out of four.

## CPU blunders (from the replays)

- **Number 30: Acid Golem of Destruction** (Burning Abyss's Dante): summoned at 7,300 LP, then kept; it burned him
  2,000 a turn until it finished him (the last LP he lost, 12 turns later). **Fixed later:** a monster that stops
  your own Special Summons is only summoned to win that turn (unless already losing), and an upkeep monster with no
  material left counts as a liability: the first tribute, and worth a Tribute Summon to get rid of.
- **Allure of Darkness banishing the only monster** (Blackwing's Kestrel), then not summoning Sirocco with an empty
  field to swing into. **Fixed later:** Allure (and Common Charity) only with a DARK (Normal) monster in hand to pay
  with, and not when that's the only monster in hand. Both are in `--sense-test` (harness), on set boards.
- Either-side wipes hitting their own cards (Heavy Storm, Torrential Tribute and Dark Hole taking their own Fiendish
  Chain or Swords): working as the cards do.
- 200 CPU duels after the AI changes: no new kind of blunder; the counts move with which games happen (own
  Spell/Trap 9 to 15, all either-side wipes like Heavy Storm and Wavering Eyes).

## Not covered

The Structure Deck shop, New Game+, the Recipe Book screen, the controller, and real mouse movement (hover-only
features). The Advanced band past its first circuit, and the Expert, Master and Gauntlet bands. Ban Passes were
spent on Raigeki and Super Polymerization; the No-Pass list never came up.
