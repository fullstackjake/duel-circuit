# Production-value review: the game (2026-11-20)

The game-design part of the production-value review. Two other reviewers cover the interface
(`PRODUCTION-REVIEW-UI-2026-11-20.md`) and the audio (`PRODUCTION-REVIEW-AUDIO-2026-11-20.md`); this one stays out of
their subjects. A review only: no code, data or campaign files changed.

**Question from the owner:** as a player going through the game, where does the experience go flat, confuse or fail to
reward, and which changes or new features would improve it most for the effort?

## Verdict

As a game, Duel Circuit already does the hard things well. The rules are exact, every CPU deck is a real deck with a
character and a voice, beating a duelist hands you their deck's recipe (the Tag Force loop, done better), and the
campaign is honest with the player: odds on every pack, Scout reports, Stuck? advice that names the deck that wins,
loaners and Make it yours. The post-game is enormous: 12 Library cups and a six-round final, five alley tables with
stakes, brackets, the Champion's Ladder, grading and a used market.

It loses players in three places. **The difficulty curve is upside down for its first 40 matches:** the Novice band's
decks are the weakest in the game (mean Elo 1,255 against the Beginner band's 1,385) and play at skill 0.55, so the
same unedited starter wins about 50% in the Beginner band and about 71% in the Novice band (measured below). The
recommended first deck is the one that has it hardest at the start. **Wins stop meaning anything late:** a cleared
circuit pays its full win DP on every rematch (up to 2,588 DP a win, against the Library's 150), and New Game+ pays all
~222,000 DP of prizes and first wins again against the same decks, so a champion sits on 300,000+ DP with little left
to want. **The rival disappears:** Vera, the story's other half, never duels you between the Regional Qualifier and the
post-game, about 70 matches.

The three changes that would improve it most:
1. **Fix the early curve with data only** (reorder and re-seat the first three bands, and raise the early skills toward
   full strength). About 2 agent-days with the measuring.
2. **A rival match at the end of every band:** Vera brings a deck one notch above the band. It's the owner's own lever
   (deck choice), it puts a peak at each band's end, and it pays off the story. About 4-5 agent-days plus the owner's
   lines.
3. **Make late wins worth something:** repeat circuit wins pay a repeat rate, and New Game+ becomes a "Champion's
   Circuit" where every opponent brings a stronger deck. About 1 day for the first, 4-6 for the second.

## How it was looked at

- **Read:** `CLAUDE.md`; the model review (`DECK-EDITOR-UX-REVIEW-2026-11-19.md`); `HANDOFF-2026-11-05.md` and
  `NEXT-BUILD-2026-11-19.md`; `TODO.md`; `docs/CAMPAIGN.md` (whole) and `docs/PLAYING.md`'s section list; the earlier
  reviews (`GAME-REVIEW-2026-10-26.md`, `DESIGN-REVIEW-2026-10-30.md`, `STORY-REVIEW-2026-11-06.md`'s
  recommendations and answers, `PROOF-2026-11-01.md`); `QA-OVERNIGHT-2026-11-06.md`; `TUNING-PLAN-2026-11-06.md`.
- **Data:** `campaign/circuits.txt` (whole), `guests.txt`, `ladder.txt`, `bounties.txt`, `library.txt` (decks and the
  final), `shop.txt` (voice pools), `story.txt` (triggers counted), `deck-ratings.txt` (whole), the `skill:` and
  `decks:` lines of every profile.
- **Code, for exact numbers:** `Profile.cs` (`WinDp`, `Record`, `RecordTag`, `NewGamePlus`, `IsCleared`),
  `Leveling.cs`, `Library.cs` and `Ladder.cs` (repeat pay), `DeckAccess.cs` (`RecipeShop`), `Personality.cs` and
  `SmartDuelist.cs` (what `skill` does).
- **Screenshots** from today's fresh-campaign check (`build/qa-evidence/checks/campaign-720/out/`): starters, map,
  circuit panel, first-duel offer, versus, results with unlocks, shop, pack reveal, Recipe Book, guests, Library shelf.
  Looked at about 12 of the 123; the rest by file name only.
- **Harness runs** (`dotnet build/bin/evidence/Ygo.Harness.dll --circuit-study --only <circuit> --option <deck>
  --games 16 --seed 1`, read-only, each under a minute): classic on all 7 Beginner and Novice circuits and Synchro Dawn;
  warriors on Town Tournament, Underdog Open and Synchro Dawn; Mekk-Knight Invoked on Regional Qualifier, Underdog Open,
  Synchro Dawn and Golden Age. 15 runs, 54 match rows, 864 duels. Raw output: `build/qa-review-game/runs/cs-*.txt` (`cs-early.txt`
  was stopped part-way to keep the machine free; its finished rows are used). The numbers are in finding 1. The stand-in is the harness's skilled QA pilot (plans ahead,
  knows its deck), so read these as "a good player with this deck", not a beginner.
- **The owner's real save**, copied to `build/qa-review-game/save-copy/` and read there: 43 campaign duels, 37 won
  (86%), 10 circuits cleared (through Link Age Open, plus one match of the Golden Age Championship), level from 8,280
  EXP, 995 card copies, 119 packs, 9,563 DP in hand; 48,873 DP earned and 40,310 spent (boxes 10,560, recipes outright
  11,670, Selection Packs 6,200, packs 5,900, grading 3,400, tokens 2,500). Losses: one each to Geargia, Pendulum
  Magician, Vera and Yummy, two to Salamangreat. No lessons, Tag Cups or guests played yet.
- **Not done:** I didn't run the app (the interface reviewer has it), so pacing in minutes, feel and the first-hour
  flow rest on screenshots, docs and data. I didn't run the long studies, `--campaign-sim` or `--arbitrage` (nothing
  was changed).

## Findings, ranked by how much they hurt the experience

Severity: **High** = most players feel it over many hours; **Medium** = a stretch of the game or one kind of player;
**Low** = friction. Status: CONFIRMED (in data, code or a run) or PLAUSIBLE.

### 1. The first three bands are flat, and the Novice band is easier than the Beginner band (High, everyone; CONFIRMED)

- **Deck strength by band** (`campaign/deck-ratings.txt` Elo, opponents from `circuits.txt`):

  | Band | Circuits | Mean deck Elo | Opponent skill | Plans its turns (skill ≥ 0.9) |
  |---|---|---|---|---|
  | Beginner | 4 | 1,385 (classic 1,276 to gadgets 1,480) | 0.3-0.95 (Ryo 0.3, Brask 0.4) | Hollis only |
  | Novice | 3 | **1,255** (frog-ftk 1,054 to six-samurai 1,491) | **0.55 for all 12** | none |
  | Intermediate | 3 | 1,374 | 0.7 | none |
  | Advanced | 3 | 1,477 | 0.82 | none |
  | Expert | 4 | 1,623 | 0.92 | all |
  | Master | 4 | 1,752 | 1.0 | all |
  | Gauntlet | 1 | 1,816 | 1.0 | all |

  Novice's Small Engines Invitational is the weakest circuit in the game (mean 1,243). The header of `circuits.txt`
  says bands "follow measured deck strength"; for bands 1-3 they don't.
- **Measured with the starter as it comes** (`--circuit-study`, 16 duels a match, seed 1):

  | Starter (Elo) | Beginner band | Novice band | Intermediate (Synchro Dawn) |
  |---|---|---|---|
  | Legendary Dragons, "Best first deck" (classic, 1,276) | ~50% (Rookie Cup 48, Town Tournament 48, Vera's warriors 25) | ~71% (Underdog 65, Trap Masters 72, Small Engines 75) | 53% |
  | Warrior's Charge (warriors, 1,418) | Town Tournament 68% | Underdog Open 78% (frog-ftk 16 of 16) | 64% |
  | Mekk-Knight Invoked (1,692) | Regional Qualifier 98% | Underdog Open 100% (64 of 64) | 98%; Golden Age (Advanced) 97% |

  The same deck, untouched, does 10-20 points better a band later, and Intermediate is no harder than Beginner. With
  Mekk-Knight the first 11 circuits are a formality: 4 losses in 240 duels across the four circuits measured. A player who has also improved their deck
  (everyone does: the shop and Recipe Book are open from the start) walks through Novice.
- **Real play agrees:** the owner's save has 37 wins in 43 duels through 10 circuits; the 2026-11-01 QA run had 12 of
  22 wins at full LP by turn 4-10 (TODO "Early circuits are easy"). Vera's "the easy wins are over" line comes before a
  4-0 band.
- **Already found** in `GAME-REVIEW-2026-10-26.md` (finding 1, "reorder the early bands"), listed in TODO ("Early
  circuits are easy"), but in no plan: `TUNING-PLAN-2026-11-06.md` covers the post-game only.
- **Why skill matters here:** `skill` below 1.0 makes a CPU pass on judgement calls (`SmartDuelist.cs:140`, up to 42% of
  them at Ryo's 0.3) and below 0.9 it doesn't plan its turns (`Personality.PlansAhead`). The owner's rule says CPUs
  play at full strength; the tuning plan reads it as "no skill below what's set", and the owner's 2026-11-06 answer for
  the alley regulars was "measure at 1.0 first; if in band, set 1.0". Raising a skill never breaks the rule.

### 2. The recommended first deck has the hardest start (Medium-High, new players; CONFIRMED)

- Legendary Dragons is marked **Best first deck** (`campaign-starters.png`). It is the weakest starter (classic 1,276;
  warriors 1,418, zombies 1,384) and the weakest deck in the Beginner band.
- With it, a skilled pilot wins 25% against Vera's warriors (Town Tournament match 3) and 37% against Vera's zombies and
  Brask's gadgets. A new player, who plays worse than the stand-in, meets the first real wall here, in the first hour,
  then coasts in the Novice band.
- The other end: Mekk-Knight Invoked (1,692) is stronger than every circuit deck until Expert's New Wave. Its tile says
  "Strong from the first duel", which is honest, but nothing says the first 13 circuits will be a walk.
- The starters are also the Beginner CPUs' decks: picking Warrior's Charge means three mirror matches (Brask, Ryo, Vera)
  in 12, and the Recipe Book's first unlocks are decks you already own ("own 100%", `campaign-recipe-book.png`).

### 3. Late wins pay too much to mean anything, and the post-game has nothing left to buy with it (High, every champion; CONFIRMED)

- **Circuit rematches pay the full win DP forever.** `WinDp = (150 + 75 x circuit) x 1.5` in the late bands
  (`Profile.cs:1872`); `Record` pays it on every win (`Profile.cs:1897`) and level-ups pay DP too (`Leveling.AddExp`
  with `payDp` true). A rematch of Expert: Fall Format (mean Elo 1,576, the weakest late circuit) pays 2,025 DP; the
  Gauntlet 2,588. The Library caps a repeat at 150 (`Library.RepeatWinDp`), the Ladder too, "so a cleared cup isn't a DP
  farm" (`Library.cs:45`). The circuits were never given that rule.
- **Result:** the 2026-10-30 champion save held 354,554 DP and grew to 405,326 over the QA overnight
  (`QA-OVERNIGHT-2026-11-06.md`). Against that, the post-game's prices are pocket change: alley stakes 300-3,000, Locals
  1,000, Ladder first climbs 800, the Every Era final 10,000. The stakes in the Back Alley, its whole tension, assume
  DP is scarce.
- **Not a ledger problem:** every DP is earned through `Profile.Earn` and nothing is DP-positive (`--arbitrage`). It's a
  feel problem: "always something worth saving for" fails once a player has five good decks.
- The early economy works. The owner's save spent 82% of what it earned by circuit 10, on boxes, recipes, Selection
  Packs, grading and tokens: DP is wanted there.

### 4. New Game+ replays the same campaign with nothing at stake (Medium, players who finish; CONFIRMED in code)

- `Profile.NewGamePlus` clears `MatchWins` (`Profile.cs:911`). Every circuit prize, first-win bonus and clear token pays
  again: about 222,000 DP a cycle (my sum over `circuits.txt` with `WinDp`), plus 41 tokens.
- The opponents are the same decks at the same skills. A champion's deck (1,800-1,950) against the Novice band (1,255)
  is a formality. NG+ has its own story lines for a few clears (`ngplus-*` scenes), and that's all that's new.
- Players of this kind of game expect a second lap to be harder or different (Tag Force's later-year rosters, a
  "hard" circuit). Here it's the same lap with a fuller wallet.

### 5. The rival vanishes for most of the campaign (Medium, everyone; CONFIRMED)

- Vera is in 40 scenes, but as an opponent she plays three Beginner matches (Town Tournament, Regional Qualifier, the
  Tag Night) and then the post-final rematch (`guests.txt`), with the same warriors deck (1,418, skill 0.85). Between
  the Regional Qualifier and the Every Era final there are about 70 circuit matches, 21 tag matches and 6 guests.
- Rival fights at stage ends are what makes a climb feel like a story (and give each stretch a peak). Here the bands end
  on the circuit's last match, a stranger.
- Her post-final rematch at 10,000 DP is measured at about 94% for a champion by Elo (`TUNING-PLAN` §2); the plan has a
  new trap-warriors list for it (owner decision 6), not built.

### 6. Nine circuits end with no scene, and the middle of the climb has no peaks (Medium, everyone; CONFIRMED)

- Of 22 circuits, 13 have a `clear:` scene (`story.txt`); Town Tournament, Underdog Open, Trap Masters Cup, Synchro Dawn
  Classic, Xyz and Link Regionals, Control Kings Cup, Shadows and Storms, Rituals and Fire and Link Wars have none.
- Each circuit's last match is a regular opponent with no billing as its champion. The map marks the next node and the
  results list unlocks in gold (`campaign-results-unlock.png`), which works, but there's no "final match" moment:
  no boss intro and no title. (Corrected 2026-11-20: the music does change. `BossMusic.ForMatch` gives every circuit's
  last match its band's boss theme and a versus sting; the moment lacks words, not sound.)
- Already suggested in `GAME-REVIEW-2026-10-26.md` §6 ("a two-line intro from each circuit's champion before its last
  match"); not done.

### 7. Lessons teach chains, but nothing teaches the summoning mechanics the circuits introduce (Medium, new players; CONFIRMED)

- Four lessons: the first duel, and three on chains (`ChainLessons.cs`, CAMPAIGN.md "Gus's Lessons"). Good ones, with
  a guest as the reward.
- The player meets Xyz and Link monsters in the Novice band (Zoodiac, Runick Spright, Purrely, Yummy), Synchro and
  Pendulum in Intermediate, and nothing in the game explains Tuners, Xyz Materials, Pendulum Scales or Link Arrows.
  Professor Hollis "explains each era as you reach it", in story lines only.
- The lesson machinery (a scripted board and a goal) already exists, and Hollis is the obvious teacher.

### 8. Buying a deck outright makes early packs optional (Medium-Low, players who find it; PLAUSIBLE as a feel issue)

- "Buy what's missing" for a Beginner or Novice recipe costs only the cards (40 DP a Common, no service fee below
  Intermediate: `RecipeShop.ServiceFee`, `DeckAccess.cs:166`). Tink's Stall Burn is 1,440 DP with 4 cards owned
  (`campaign-recipe-book.png`); one Beginner circuit pays 1,650-2,925.
- The owner's save spent more on outright recipes (11,670) than on booster packs (5,900). Packs are the most exciting
  thing the shop does (foil reveal, grades, the best pull last); a cheap shortcut competes with them.
- It's a choice, not a bug: outright is also the cure for a bad-luck streak. Owner decision 4.

### 9. The post-game is mostly rematches (Medium, champions; CONFIRMED)

- The Library's 55 decks include only 4 the circuits don't (machine-re-volt, nekroz, performapal, spyral). The Ladder's
  ten rungs are circuit and guest decks; its top, Ada Quill, plays chaos-ritual, the October 2026 circuit's boss
  (`ladder.txt`). Ilse's four decks (1,384-1,527) are below the circuits' Advanced band, as a Ladder rung and as a
  10,000 DP guest.
- The era ban lists and your own-deck rule make old matchups play differently, so it isn't a pure repeat. But the
  late-game's new faces and decks are in the Back Alley (contraband builds, headliners), and its newest table, No
  Limits, shows "Coming soon" (`CAMPAIGN.md`, "Coming later"). The ship gate and the tuning pass are known and planned;
  noted here only for its effect: the post-game's most novel content is the part behind a locked door.

### 10. Tag duels and guests: walls the player can't see coming (Low-Medium; CONFIRMED in data)

- Seraph opens at the Advanced clear with Light and Darkness (1,768) and Dragon Rulers (1,522); the Advanced band's
  decks average 1,477. The wall-study's best options there are Master decks (`wall-advice.txt`: Tearlaments 62%,
  your Mekk-Knight starter 31%). Rhea (thunder-dragon 1,693 + sky-striker) opens at 12 tag wins, often in the
  Intermediate band.
- These are optional, and the Stuck? advice handles walls well. But the guests screen gives no hint of strength
  (`campaign-guests.png`: a blurb and a prize), so a player reads Seraph's 8,000 DP as the next step. The circuit rows
  show "skill 3/10" (`campaign-map-0.png`), not how strong the deck is.

### 11. Smaller things (Low)

- **Card names of the starters' recipes repeat** ("Warriors · Ryo", "Warriors · Vera", "Warriors · Captain Brask" all
  own 100%, `campaign-recipe-book.png`): the first "Recipe unlocked!" rewards are duplicates of your own deck.
- **Ilse's sitting is too easy** for what the story bills (about 73% by Elo against a 25-50% target; TUNING-PLAN §4
  "Ladder"). Known.
- **The bounty board** pays a missing recipe card, which is a good, aimed reward; with no recipe wishlisted only the
  first bounty per band pays (a token). A player who never wishlists sees bounties "owed" and may not know why
  (`bountyowed 1` in the owner's save).
- **The Back Alley's DP stakes** are tuned for a normal wallet; see finding 3.

## What already works well (keep it)

| Area | Why it works | Evidence |
|---|---|---|
| Beat a duelist, get their deck | Every win unlocks a recipe with its story, a replay of the owner's best turn, packs aimed at what you miss | `campaign-recipe-book.png`; 82 recipes |
| Honest help at walls | Scout reports, Stuck? after three losses with measured odds, loaners, Make it yours, the coin toss tip | CAMPAIGN.md "Stuck?"; wall-advice.txt and toss-advice.txt are measured, not guessed |
| The shop as a guide | "FOR THE BEGINNER CIRCUIT" shelves, "Ryo plays 10", why this set, odds per pack, wishlist order, house packs of the decks you'll face | `campaign-shop.png` |
| Every copy is a card | Rarity, grade, wear, slabs, the prized moment: a collector's layer no other fan game has | CAMPAIGN.md Part 2 |
| A clean first session | A one-screen starter pick, an optional practice duel ("three minutes, tops"), lessons with a guest as the prize, bounties after the first clear | `campaign-first-duel-offer.png`, `campaign-guests.png` |
| Rewards stacked and shown | The results screen lists DP lines and every unlock in gold | `campaign-results-unlock.png` |
| The ledger | Every DP through `Spend`/`Earn`, 0 DP-positive, checked by `--arbitrage` and QA | `QA-OVERNIGHT-2026-11-06.md` |
| Characters with jobs | 77+ duelists with styles, intro/win/lose lines, partner lines, bond gifts and trades; the shop's voice pools change with progress | `personalities/`, `shop.txt` |
| Two ways to play | The shop's way and Historic Rules with real ban lists and Ban Passes | CAMPAIGN.md "Two ways to play" |
| A huge, varied post-game | Library formats, the final, five alley tables with stakes and headliners, brackets, the Ladder | CAMPAIGN.md Part 4 |

## Proposed changes and new features

Costs are guesses in agent-days, each including the check that would fail without it. Anything touching DP ends with
`--campaign-test` and `--arbitrage` at 0 DP-positive. Anything touching difficulty is measured with `--circuit-study`
(per starter, seeds 1 and 2) before and after, and `--difficulty` for the whole map; anything touching saves gets a
`--profile-check` of an old save. **(W)** marks work that needs the owner's writing; **(A)** new art.

### Quick (a few hours each)

| # | Change | Fixes / adds | Files or data | Cost |
|---|---|---|---|---|
| Q1 | **Repeat circuit wins pay a repeat rate**: a win against a match you've already won pays the Library's 150 DP (or 25% of `WinDp`, at most 600), and its level-ups pay tokens, not DP, as the Library does | 3 | `Profile.Record`/`RecordTag` (`Profile.cs:1817-1910`), `Leveling.AddExp` call; CAMPAIGN.md "DP for each duel" | 0.5; `--campaign-test`, `--arbitrage`, a `--campaign-test` step that a rematch pays the repeat rate |
| Q2 | **Move "Best first deck" to Warrior's Charge** (or Undead Legion), and add one line to Mekk-Knight's tile: "the first bands will be easy" | 2 | the starter screen's data or code (where the tag is set) | 0.25; `-campaigncheck` shot |
| Q3 | **Deck strength on rows the player chooses from:** 1-5 stars from `deck-ratings.txt` on circuit match rows, guests, Tag Cup matches and Recipe Book entries ("Deck ★★★☆☆"), replacing or beside "skill 3/10" | 10, helps 1 and 8 | `DeckRatings` (engine has it), the circuit panel, guests, Recipe Book screens | 0.75; layout and campaign checks |
| Q4 | **A billed final match:** the last match of each circuit says "Circuit final" on its row and versus, with the circuit's champion line (one `champion:` line per circuit in `circuits.txt`) | 6 | `circuits.txt` format, `Circuits.Load`, versus and circuit panel | 0.5 + **(W)** 22 lines; `--campaign-test` parses them |
| Q5 | **Starter recipes don't count as new unlocks** when you own 100% of the deck (no "NEW" on a duplicate), or the first unlock line says "you already play this deck" | 11 | Recipe Book unlock note | 0.25 |
| Q6 | **The guests screen shows each guest's strength and "best after: <band>"** | 10 | guests screen | 0.25 (with Q3) |

### Medium (1-3 days each)

| # | Change | Fixes / adds | Files or data | Cost |
|---|---|---|---|---|
| M1 | **Re-seat the early bands by measured strength** (data only, the opponents unchanged): the Novice band's three circuits swap with the Beginner band's last two, or the Novice circuits move after Intermediate's; or the weakest Novice decks (frog-ftk, purrely, spellbook, yummy) trade places with stronger decks of nearby eras already in `decks/` (six-samurai, dragunity-legion, machine-re-volt, synchron). Then raise the Novice and Intermediate skills toward 1.0 where the measured rows stay in band (the owner's 2026-11-06 rule for the alley regulars). Targets `[PLACEHOLDER]`: with each starter unedited, 55-70% in Beginner, 50-65% in Novice, 45-60% in Intermediate | 1 | `circuits.txt`, early `personalities/*.profile` `skill:` lines; story gates that name circuits (`clear:` triggers, shop `band:` pools) | 2 (half of it measuring: `--circuit-study` per starter and band, seeds 1 and 2; `--difficulty`); old saves keep their match keys only if circuit order is unchanged, so a reorder needs a `MatchKey` migration (+1 day) or the "swap decks, keep circuits" option |
| M2 | **Hollis's lessons: Synchro, Xyz, Pendulum, Link**, one each, opened by the circuit that introduces the mechanic, on the existing lesson machinery; passing all four pays a token or a poster | 7 | `ChainLessons.cs` (or a sibling), lessons screen, `story.txt` | 3 + **(W)** Hollis's lines; `--lesson-test` steps per lesson |
| M3 | **Recipe outright buys carry a small fee in every band** (e.g. 25% over the cards), so packs stay the cheaper way and outright stays the sure way | 8 | `RecipeShop.ServiceFee` | 0.5 + an economy read; `--campaign-sim` spend shares, `--arbitrage`. Owner decision 4 |
| M4 | **Circuit scenes for the nine silent clears**, two or three lines each, from that circuit's champion or a regular | 6 | `story.txt` | 1 + **(W)**; `--campaign-test` story parse |
| M5 | **A Saturday Locals before the post-game:** after each band clear, an 8-player bracket of that band's beaten decks at the shop (the bracket engine exists), entry and prize in DP below break-even plus a Prize Pack. A repeatable thing to do between circuits that isn't a rematch | 3 (gives early DP a use), adds variety | `Brackets.cs` (a new field source), a shop-floor entry point | 2-3; `--bracket-sim` for title odds 30-45%, `--arbitrage` |

### Bigger (a week or more)

| # | Change | Fixes / adds | Files or data | Cost |
|---|---|---|---|---|
| B1 | **Vera's rival matches**: an optional fifth match at the end of each band (Novice to Master), Vera with a deck one step above the band's mean, from `decks/` where one fits (six-samurai at Novice, mekk-knight at Intermediate, then later warrior-style lists) and new lists only where none does; her line grows band by band; a win pays a poster and her bond, a loss blocks nothing. Difficulty by deck choice only, at skill 1.0 | 1, 5, 6 | `circuits.txt` (a `rival:` line per circuit), `Circuits.Load`, circuit panel and versus, `personalities/vera.profile` `decks:`, `story.txt` | 4-5 + **(W)** 6 short scenes; each band's row measured with `--circuit-study` (target `[PLACEHOLDER]` 40-55% with the band's best unlocked deck) |
| B2 | **New Game+ as the Champion's Circuit**: in a cycle ≥ 1, each circuit match plays a stronger deck from the same character's family or the next bands (a `ngplus:` deck per match in `circuits.txt`, defaulting to the band's top deck), with skill 1.0; prizes pay once per save (first cycle), first wins pay a smaller NG+ bonus, and the clear tokens stay. Makes the second lap a real climb and stops the 222k DP replay | 3, 4 | `circuits.txt`, `Profile.NewGamePlus` and `Record` (a per-cycle prize key, migrated from old saves), versus | 4-6; `--circuit-study` with a champion's deck on the NG+ roster (target 45-65%), `--campaign-test`, `--arbitrage`, old-save load |
| B3 | **Gus's puzzle book:** "win this turn" boards on the lesson machinery, unlocked a few per band, 3-5 lines of setup each, a token or a poster per page. Offline single-player games of this kind all have one; it teaches card interactions the campaign can't, and it's the most shareable thing a fan game can have | adds; helps 7 | lesson machinery, a puzzles data file, a Puzzles screen | 6-8 + **(W)** puzzle writing (an agent can draft boards; each needs a proof that its solution works in the engine: a harness test per puzzle) |
| B4 | **Late-game wants:** a collection log with set and archetype completion rewards (posters, mats, card backs drawn in code), Gem Mint chase goals, and a Champion's shelf of slabs to display; prices sized for a champion's wallet. Gives DP a destination after finding 3 is fixed | 3 | `Posters.cs`, `Cosmetics.cs`, the Locker, a collection screen | 5-7 + **(A)** optional; `--arbitrage` (cosmetics are pure sinks) |
| B5 | **Ship No Limits** (known: the gate in `TUNING-PLAN` §5, parked with the tuning pass). Listed for rank only: it's the post-game's most novel content | 9 | as planned | as planned |

### Which need the owner

- **Writing:** Q4, M2, M4, B1, B3 (and B2's few lines). **Art (optional):** B4.
- Everything else is data or code an agent can do from the spec.

## Owner decisions (recommendation first)

1. **The early curve (finding 1).**
   - Recommended: **swap decks, keep circuits.** The weakest Novice decks trade places with stronger decks of nearby
     eras that already exist; the circuits, their order and their story stay. No save migration. Then raise Novice and
     Intermediate skills toward 1.0 where the measured rows stay in band.
   - *Or* reorder the circuits (moves the story's band beats and needs a match-key migration).
   - *Or* only raise skills (cheapest, but the Novice decks stay the weakest; likely not enough).
2. **Repeat circuit wins (finding 3).**
   - Recommended: the Library's rule, 150 DP and token-only level-ups for a match you've already won.
   - *Or* 25% of the win DP, capped at 600 (keeps grinding a little more rewarding mid-campaign).
3. **New Game+ (finding 4).**
   - Recommended: B2, the Champion's Circuit, with prizes paid once per save.
   - *Or* keep NG+ as it is and only stop prizes paying twice (quick; NG+ stays easy).
4. **Recipe outright buys (finding 8).**
   - Recommended: leave them as they are for now and watch: they're the bad-luck cure, and the owner's own spending
     shows packs still sell. Revisit after Q1 lowers late DP.
   - *Or* M3's 25% fee in every band.
5. **The "Best first deck" tag (finding 2).**
   - Recommended: Warrior's Charge, after M1 is measured (the tag should go to the starter with the smoothest curve).
   - *Or* keep Legendary Dragons (the simplest deck to read) and accept the harder first band.
6. **Rival matches (finding 5).** Recommended: optional fifth matches (B1), so nobody is blocked. *Or* make them the
   band's gate (stronger story, a hard wall at every band).

**What I'd build first, as one spec:** *"The climb"*: M1 (swap decks, keep circuits, raise early skills) + Q1 (repeat
pay) + Q3 (deck stars) + Q2 (the starter tag) + Q4 (billed circuit finals) as one change, measured once with
`--circuit-study` per starter and band on seeds 1 and 2 and `--arbitrage`. About 4-5 agent-days with the owner's 22
champion lines. B1 (Vera) is the natural second spec on top of it, since it reuses Q4's billing and M1's measurements.

## Not verified

- **Nothing was played by hand.** The first hour's feel, the time per duel, how the lessons land and how the shop reads
  to a new player rest on screenshots, docs and the owner's save. I couldn't judge sound, animation pacing or how long
  the story scenes feel.
- **The win rates are a skilled stand-in's**, 16 duels a match (about ±12 points a row), one seed. A new human player
  is likely weaker in the Beginner band (finding 2 is likely worse than measured) and so is the stand-in's deck
  improvement (it plays the starter unedited; real players improve theirs, so finding 1 is likely worse too).
- **Deck Elo** is from `deck-ratings.txt` (300 rounds, written 2026-11-08); the misplay fixes since may have moved a few
  decks by a little.
- **Not run:** `--campaign-sim`, `--arbitrage`, `--difficulty`, the wall and library studies, any app check.
- **The DP sums** (222,000 per cycle; 41 tokens) are mine from `circuits.txt` and `WinDp`, without level-ups.
- **The post-game** (alley, Ladder, final) is judged from data, docs and the tuning plan; I didn't watch any of it.
