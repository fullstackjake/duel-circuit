# Build plan (2026-11-19): card-data refresh, player-visible bugs, deck editor review

Three parts, run at the same time on files that don't overlap. Nothing is committed until the owner says so.

| Part | Agent (model) | Owns (touch nothing else) | Ends with |
|---|---|---|---|
| R: card-data refresh | general-purpose (Opus) | `vendor/CardScripts`, `vendor/BabelCDB`, `vendor/LFLists` pins; `data/ygoprodeck/`; `unity/Assets/StreamingAssets/cards.bin` and `printings.bin`; `decks/` (id fixes only); `lflists/`; `campaign/texts.txt` (only if a new errata date needs it); `build/bin/cardrefresh`, `build/cardrefresh/` | `docs/dev/CARD-REFRESH-2026-11-19.md`: old and new pins, new cards and sets, id changes, check output |
| B: player-visible bugs | Unity Architect (Opus) | `unity/Assets/Scripts/**` (except the read-only use below); `personalities/kit.profile`, `personalities/rookie-ryo.profile`; `src/Ygo.Engine/**` only where a fix needs it; `build/bin/bugs`, `build/qa-win` | Its section in this file ("As built"), app-check output |
| U: deck editor review | UX Researcher (Opus) | `docs/dev/DECK-EDITOR-UX-REVIEW-2026-11-19.md`; `build/qa-ux/` (its own copy of the check build) | The review: findings ranked by how much they hurt deck building, with screenshots and a proposed change list. No code |

Then: a Code Reviewer (Opus) on B's and R's diffs against this plan, one fix round back to the authors, and an Evidence
Collector (Opus) on a fresh build. Docs (README, PLAYING, CAMPAIGN known issues) by a Technical Writer (Sonnet) at the
end.

## Part R: card-data refresh

The pins date from 2025-04-20 (BabelCDB, CardScripts) and the LFLists commit 1c34c46. The game's card data (and so the
pool, search and printings) is that old. Toon's card data (for No Limits wave 2) was due after Magnificent Maestros on
2026-11-13.

1. Move the three submodules to their newest commits (BUILDING.md "Update to the newest cards").
2. Re-export `cards.bin`, download YGOPRODeck's card-set data again, re-export `printings.bin`.
3. `tools/check_history_decks.py` (and `--fix` for moved ids), `--deckbuilding-test`, `--campaign-test`,
   `--texts-check`, `--copies-test`, `--nolimits-test`, `--arbitrage` (0 DP-positive), `--games 30` and
   `--endless-test` smoke (script errors 0).
4. Old saves must load: a save whose collection holds ids that moved still loads and keeps its cards.

Trap: new scripts can need a newer `ygopro-core` (new effect codes or Lua functions). If script errors appear, report
them with the cards; don't move or rebuild the core.

## Part B: player-visible bugs (from TODO.md)

Check each first: some may already be fixed in code. For each, a failing check step before the fix.

1. **Warrior's Charge is refused at the Back Alley** (3 Reinforcement of the Army; the Alley list allows 1).
2. **Chain panel text clips:** a link-2 panel's text shows through link 1; a two-target line ends in a comma with the
   second target cut off.
3. **"summon here" shows when you can't summon** (their turn, your End Phase). The code may already handle it:
   confirm with `-controlscheck`/the board check, then tick it.
4. **Card names wrap mid-word on cards without a picture** ("Polymerizatio n"); the editor's count badge covers part
   of the name.
5. **The deck editor's "don't own enough of any recipe" message shows twice.**
6. **Partner intro lines don't fit the moment:** Ryo's "My first real duel!" as a partner; Kit's "I brought my
   Synchrons" on a loaner; the versus deck line runs into the < arrow.

## Part U: deck editor review

Is the deck editor good for building a deck, finding cards and getting around? Read `DeckEditor*.cs`, the search spec
([CARD-SEARCH-SPEC-2026-11-07.md](CARD-SEARCH-SPEC-2026-11-07.md)), PLAYING.md's editor section; run the editor
(`-editor <deck>`, `-searchcheck`) in its own check copy and look at every screenshot. Cover mouse, keyboard and
controller, free play and the campaign catalog, at 1920x1080 and 1280x720. Tasks to walk: build a deck from empty, fix
an illegal deck, find a card by what it does, find a card you saw in a duel, swap a card between Main and Side, check a
deck against a ban list, test-draw.

## Owner decisions

1. **Warrior's Charge at the Back Alley.** *Recommended:* when the Alley refuses a deck only for copy limits, the versus
   offers **Make it Alley-legal**: a copy named "<deck> (Alley)" with the extra copies swapped for cards you own (the
   loaner rule `Loaners.Adjust` already uses), saved as a new deck. The starter list and the CPU decks stay as they are
   (changing `decks/warriors.ydk` would change CPU strength while tuning is parked). *Or* change the starter for new
   saves only. Building the recommendation.
2. **Move the card data to the newest commits.** *Recommended:* yes, all three submodules, committed only when you say.
   *Or* stay on the April 2025 data.
3. **Deck editor changes.** *Recommended:* the review only now; you pick from its change list, then it gets a spec.

## As built (Part B)

Unity Architect (Opus), 2026-11-19, then one review fix round the same day. Nothing committed. Check build:
`build/qa-win/` (from this tree, with whatever card data Part R had in place at build time). Check runs:
`build/qa-win/checks-bugs/<check>/` (final), earlier runs in `run1-*` to `run4-*`. Harness: `build/bin/bugs/`.

| Bug | What was wrong | Fix | Check step (fails without the fix) |
|---|---|---|---|
| 1. Warrior's Charge refused at the Alley | Starter runs 3 Reinforcement of the Army; the Alley allows 1, and the versus only said so (`DuelController.Alley.cs` ShowAlleyVersus) | Owner decision 1 as recommended. When the table refuses a deck **only** over copies (`CampaignState.AlleyRefusesOnlyCopies`, decided by counts: every problem is a card whose copies exceed the list's allowance, nothing else), the versus shows **Make it Alley-legal** (at No Limits **Make it No Limits-legal**), between Back and Duel!. `CampaignState.MakeAlleyLegal` builds the copy with `AutoDeck.Fix` on your own collection: the extra copies out, the Main Deck topped back to 40 from cards you own, saved as "<deck> (Alley)" ("(No Limits)" there). Pressed again for the same deck, it picks the copy you already have with the same cards instead of saving another. If your cards can't fill it, nothing is saved and it says how many cards short you are. A fresh visit opens on your deck if it's legal at that table, else its legal "(Alley)" copy, else your first legal deck (as the Ladder does), else your deck with the fix offered. `decks/warriors.ydk`, the starter and every CPU deck are untouched | `-alleycheck` step 4b (8 lines): a fresh visit with the starter refused opens on a legal deck; the arrows back to the starter offer the fix; the "can't fill" message; a deck refused only for cards not out yet (a pooled Alley list, whose words name the list) isn't offered it; the saved deck legal, 1 Reinforcement, owned, picked; starter, built-in `warriors`, collection unchanged, 0 DP; leaving and coming back opens on the (Alley) copy with no button; pressing the fix again picks it (1 new deck in all). Step 21: at No Limits the button reads "Make it No Limits-legal" and saves "<deck> (No Limits)". Steps 4 and 21 now pick their refused decks with the arrows (a fresh visit no longer opens on them) |
| 2. Chain panel clips | Already fixed in code (`BoardView.cs` ShowChain/FitChainLine: opaque rows, a two-target line grows to two lines) | None | Existing `-layoutcheck` step "a two-link chain reads whole", and the four-target one: ok. Screenshot `layout/out/layout-chain-panel.png` |
| 3. "summon here" when you can't summon | Already fixed (`BoardView.Columns.cs` CanSummonNow: your turn and Main 1/2 only) | Only a check helper (`DebugColumnLabelsAt`) | New `-controlscheck` step: your End Phase and Battle Phase show "2 ✓", not "summon here" (the existing "their turn" step also passes). It would have passed before too |
| 4. Names wrap mid-word; badge over the name | Whole-word wrap was already in (`CardView.FitWords`), but its hyphenation split digraphs: "Knig-/htmare" (`CardView.SyllableCut` took the first consonant pair after a vowel). The limit badge was already handled; a search result's in-deck count ("x2") sat over the bottom of the name's box (`DeckEditor.cs` ShowResults) | `SyllableCut` now cuts inside a run of consonants between two vowels where the next syllable takes the longest start a word can have (`CardView.Onsets`: tr, str, chr, gl, kn...; not sm, sn, gn), never inside ch/gh/ph/sh/th/wh/ck/qu: "Des-truction", "Con-struction", "Thunder-strike", "Syn-chron", "After-glow", "Air-knight", "Knight-mare", "Black-wing", "Arch-fiend", "Trans-migration". Else after a vowel before one consonant, as before. The result's name box ends above its in-deck count. Over every card-name word of 6+ letters in cards.cdb (7,559 words, 24,289 word-and-room cases; `build/bin/bugs-hy/`), against the Nov 12 code: 3,538 cuts change, 2,959 better and 0 worse by a syllable-start judge (the first Part B change: 1,759 better, 800 worse). By the reviewer's own measure (a changed cut splitting a start cluster the old cut kept, counting sm/sn): 78 cases, 37 distinct ("Abyss-mander", "Assess-ment", "Blacks-mith", "Cock-roach"), down from 1,081 | `-layoutcheck`: a step with real names at the room that broke them (Destruction, Synchron, Electromagnetic, Construction, Thunderstrike, Airknight, Afterglow, Knightmare, Blackwing, Archfiend, Transmigration, Polymerization; fails on the first change: "Dest-ruction", "Synch-ron"...); the editor steps flag a hyphen inside a digraph and a name box under an in-deck count, and search "crusadia" (9 results with a count) |
| 5. Recipe message twice | Already fixed (`DeckEditor.cs` NextRecipe: the paragraph once, then "Still none ready...") | Only a check helper (`DebugNoRecipeTwice`) | New `-campaigncheck` step: the paragraph once, then the short line. It would have passed before too |
| 6. Partner lines | Every partner quote used the character's opponent intro: the Tag Cup versus (`DuelController.cs` PartnerQuote, non-Kit), the guest tag versus (`DuelController.Guests.cs:176`), the duel log's opening line (`DuelController.cs` ~1923) and free play's tag setup (`TagSetupScreen.cs`). Kit with a loaner got "<deck>? Good list. Your deck, my hands." on the Tag Cup versus and her Synchrons intro on the guest versus. The versus deck line ran to 10 px of the "<" (580 wide), the Free Duel's (720 wide) under it, and the 52-tall arrows ran over the detail's first line ("last picked on a" under the "<") | One rule, `TagPartners.Greeting` (engine), reached through one function, `DuelController.PartnerQuote` (the tag setup is handed it, so both use `SpokenTitle`): a partner says their `partner:` line (intro only if they have none); Kit: her intro with her own deck, the piloting line only with a campaign deck of yours, her partner line with anything else (a loaner, a built-in deck in free play). Ryo's partner line now reads right every time: "We're a team! Okay. I'm writing this one down, every turn of it!" (it said "Team duel number one!"). The deck line beside arrows is 520 wide (70 px short of the "<"), Free Duel included; the arrows are 44 tall from 644, ending above the detail | `-campaigncheck`: Ryo's partner line on the Tag Cup versus; in the duel log; Kit with a loaner; Kit with her own deck, a deck of yours, and a built-in deck; the guest tag versus (Ines); the deck line 70 px short and whole (twice); arrows end at 688, the detail starts at 690. `-tagcheck`: the partner seat quotes Ryo's partner line; Kit with a built-in deck her partner line, with her own her intro. `-layoutcheck`: the Free Duel deck line and arrows |

Also: the Duel! refusal says "Make it Alley-legal (or No Limits-legal), switch decks with the arrows, or edit it." when
the fix applies. The other starters under the Alley list are logged by the alley check (`alley check: note the starters...`).

**Files changed:** `src/Ygo.Engine/Campaign/TagPartners.cs`; `personalities/rookie-ryo.profile`;
`unity/Assets/Scripts/`: `BoardView.cs` (log opening lines for checks), `BoardView.Columns.cs`, `CampaignScenes.cs`,
`CampaignState.Alley.cs`, `CardView.cs`, `DeckEditor.cs`, `DuelController.cs`, `DuelController.Alley.cs`,
`DuelController.AlleyCheck.cs`, `DuelController.Guests.cs`, `TagSetupScreen.cs`. `kit.profile` unchanged (her
partner line already fits a loaner).

**Owner decisions taken by default:** Forbidden cards count as copy limits (a limit of 0), so a deck refused only for a
Forbidden card is offered the fix too. The fix is one click (it spends nothing and keeps the old deck). "sm" and "sn"
don't start a syllable when hyphenating ("Trans-migration" over "Tran-smigration"; the cost is "Blacks-mith").

**Checks** (fix round, one at a time, fresh `-mods` each): alley 128 ok + 1 FAIL, campaign 189 ok, tag 6 ok, layout 54
ok (layout rerun after its hyphenation step's two room values were corrected; only check code changed in that build).
Before the fix round: controls 40 ok, search 61 ok, board 25 ok, order 10 ok. `alley`'s one FAIL is step 13 (the
Midnight Open final's game 1 read back): the same FAIL on the Nov 12 build (`build/qa-final`, run in
`checks-bugs/baseline-qa-final-alley`: 119 ok, 1 FAIL), so it isn't from this part. Harness `--campaign-test`,
`--deckbuilding-test` (fix round) and `--texts-check` (first round): all passed. `--arbitrage` not run: nothing here
touches DP (the fix spends and earns nothing; the check asserts 0 DP).

**Not verified:** the app on a Mac; 1920x1080 (the checks ran in a 1280x720 window); the alley fix with a deck whose
spare cards are blocked only by AutoDeck's tribute caps (the message then still says "can't fill"); controls, search,
board and order weren't rerun after the fix round (none of their code paths changed); sound.
