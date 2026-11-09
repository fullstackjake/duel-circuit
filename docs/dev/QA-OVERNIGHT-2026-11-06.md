# QA overnight playthrough, 2026-11-06 (tonight's starting build, c80b436)

Job 5 of [OVERNIGHT-2026-11-06.md](OVERNIGHT-2026-11-06.md). A `-qaplay -qafull -qacontinue -qatools` playthrough of the
frozen app copy `build/overnight/Duel Circuit.app`, from a champion save, muted, 1280x720 window. Nothing was built,
edited or committed. All work is in `build/overnight/qa/`.

## What ran

- **Start save:** a copy of `build/qa-night5/mods` (the 2026-10-30 champion save: 354,554 DP, 1,029 cards, New Game+ cycle
  1, Format Library open). It was written by an older build, so this also checked that an old save loads.
  Result: it loaded, and its `card` lines became `copy` lines (1,149 copies after run 1, 1,994 cards at the end).
- **Run 1:** `-qagames 120`, 00:12 to 01:43 (91 min). 120 duels, 49 won, 71 lost. 0 issues.
  Log `build/overnight/qa/out/qa-log.txt`, Unity log `build/overnight/qa/log.txt`.
- **Run 2:** continued from run 1's save, `-qagames 200`, 01:44 to 04:37 (173 min). 200 duels, 43 won, 157 lost.
  1 issue (tooling noise, below). Log `build/overnight/qa/out2/qa-log.txt`, Unity log `build/overnight/qa/log2.txt`.
  The 120 duels of run 1 had used the whole budget on the Format Library, so run 2 was needed to reach the rest.
- **Total:** 320 duels, 92 won, 228 lost. Run scripts: `build/overnight/qa/run.sh`, `run2.sh`.
- **Saves kept:** `profile-start.txt`, `profile-after-run1.txt` and the live `mods/save/profile.txt` (run 2's end).
- **Not replayable by the old rule:** the memory note says fresh `-qaplay` runs replay seeds 1, 2, 3.... That holds for the
  first nine duels here. After that the Library cups and Locals brackets use their own seeds (the Unity log shows
  `duel 946605352` and similar). Nothing needed replaying, since no real bug was found.

## What the runs covered

Run 1 (all Format Library post-game): the Library's opening scenes, 10 cup first-match binder scenes, 7 cleared cups'
era mats (7 bought) and engravings (4), Locals entries (7), the Locals brackets with Prize Packs, the Stuck? advice and
"Make it yours" (two purchases: Goat Control 5,450 DP, TeleDAD 3,880 DP), 12 bond-trade unlocks, 7 bond gifts, the
second-deck editor step.

Run 2 (rest of the Library, then the campaign): the last Library cups, Locals again (7 entries), Special Guests (Rhea
solo and tag, Seraph solo and tag), the circuits Control Kings Cup, Hybrid Engines Invitational, Shadows and Storms,
Rituals and Fire, The New Wave and Fall Format, three Tag Cups, Gus's lessons 1 to 3, the Card Shop (packs, a box, a
Structure Deck, the Card Trader), the Recipe Book, bond trades, 11 rebuilds after three losses. 17 circuits and 5 Tag Cups
were cleared by the end, level 64.

## Result: 0 real bugs found

| Severity | Count |
|---|---|
| Crash, freeze, soft-lock | 0 |
| Exception or error in either Unity log | 0 |
| Harness issues (`ISSUE` lines) | 1, tooling noise |
| DP or ledger error | 0 |
| Wrong screen or flow error | 0 |
| Story scene failure | 0 |
| Low-severity observations | 3 (below) |

### Checks that held

- **The Unity logs are clean.** No `Exception`, `Error`, `Assert` or `NullReference` in `log.txt` (2,630 lines) or
  `log2.txt` (about the same). The only non-game line is "Default audio device ... failed to initialize", which is the
  muted Mac audio device and not the game.
- **The DP ledger is exact.** `sum(earned) - sum(spent) == Dp - 354,554` holds at the start, after run 1 and at the end:
  - start: 354,554 DP;
  - after run 1: 329,949 DP, earned 57,725, spent 82,330, net -24,605;
  - at the end: 405,326 DP, earned 186,512, spent 135,740, net +50,772.

  In both cases `dp - net` is 354,554, so every DP move in 320 duels, 14 shop visits, the Stuck? purchases, mats,
  engravings and Locals went through `Profile.Spend` and `Earn`. The harness's own per-duel check (the results screen's
  list against the DP paid) and its save check (the file against memory) also raised nothing in 320 results.
- **Results lines match the wallet.** I replayed every "now N DP" line against the previous total. All gaps were explained by
  the Stuck? purchases, mat and engraving spends, Locals entries (1,000), Locals finish payouts (the two Champion
  finishes paid +900 each) and the shop, whose lines are logged without a before and after.
- **No DP-positive path found beyond the designed ones.** Level-up rewards (1,225 to 1,600 DP), Locals, repeat-win
  rematches and the cup prizes pay as designed. The Locals Champion payout (+900) is below its entry (1,000).
- **Story scenes play.** 114 scenes read in all: 96 in run 1 (16 Library, 37 cup binders and 37 after-results, 3 Locals
  entry, 3 after-Locals) and 18 in run 2 (11 lesson intros, 3 partner, 4 binder). None was left open, none repeated in a
  retry, none played over a duel, and no raw tag, brace, "null" or empty line appeared in them.
- **No stalls.** No hang, animation, arrow, chain badge, long-duel, toss or rejected-answer issue. The longest duel was
  42 turns, 2.1 min. 8,353 + 5,638 prompt decisions were clicked, 0 answered directly (every one mapped to a button).
- **Old save migrated.** The older save loaded, the Library state held (`library 1`, `libwin` lines kept), and run 2's save
  has the newer `bountyowed` line.
- **The second run started cleanly from run 1's save,** which is a save-and-reload check of the whole profile.

## Issue list

### 1. `[deck] the duel uses Dinosaur's Rage, not the deck you play now (Gadgets (2))`: tooling noise, not a bug

- **Where:** run 2, g81, Control Kings Cup match 1 (Trapmaster Hattie, HAT), `out2/qa-log.txt` line 1462. Screenshot
  `out2/issue-01-g81-t0.png`.
- **What:** at run start `-qacontinue -qafull` makes a second deck ("Gadgets (2)", saved as new) and remembers it as
  the deck to play at the "next circuit duel". The 78 Library and guest duels that came first changed the campaign deck
  through the deck picker (cup deck "Dinosaur's Rage, a deck of yours legal here"). By g81 the deck in use was
  Dinosaur's Rage, so the harness's stale expectation fired.
- **Evidence:** log lines `versus: deck arrows to Dinosaur's Rage` and `playing with the second deck: Dinosaur's Rage`
  just before it. The game used the deck that the player's picker showed.
- **Reproduces:** yes, whenever `-qacontinue -qafull` has post-game duels before the first circuit duel. Harness-only.
- **Severity:** tooling. The harness could reset `secondDeck` after post-game duels, or skip the check when the deck was
  changed since.

### 2. The deck library fills with identical copies (low, UX, probably a real player path)

- **Where:** run 2's 11 "rebuilding after 3 losses" steps plus its second-deck step, 12 saves in all.
- **What:** `Recipe` on a deck that already is that recipe ("0 changes to fill the gaps") followed by `Save` still asks
  "Overwrite X, or save these cards as a new deck", and Save as new then writes the same 60 cards under a new name.
  `Gadgets (3)` to `Gadgets (13)` are byte-identical except for the `#title` line (`md5` of the files, and
  `diff` of (3) against (4)). The save ended with `deck Gadgets (13)`.
- **Why it matters:** a player who presses Recipe then Save repeatedly gets a pile of duplicates. The app could say
  nothing changed, or skip the prompt.
- **Not verified:** whether a mouse-driven player reaches the same prompt. The harness calls the same `Save()` the Save
  button calls (`DeckEditor.DebugPassAndSave`), so it is likely.
- **Evidence:** `out2/qa-log.txt` lines 1543-1546 and 2012-2015, `build/overnight/qa/mods/decks/`.

### 3. The campaign header's wallet block wraps at 1280x720 (low, cosmetic)

- **What:** the two-line wallet block at the top left breaks as "Level 43 · NG+1  4 / tokens" and "105 / packs opened"
  (small second line wraps too). It wraps with 4 tokens and with 23, so it is not data-dependent.
- **Evidence:** `out/g8-d-after.png`, `out2/g130-d-after.png`.
- **Not verified:** whether it wraps at the game's normal window size. The harness ran at 1280x720 only.

## Observations that are not bugs (for the tuning job, nothing measured or changed here)

- **Win rate was 29% over the 320 duels** (92-228). The stand-in plays the Library walls with "Dinosaur's Rage" (a deck of
  yours, legal) rather than the best loaner. It lost 6 times in a row, then moved on, at: Link Dawn match 4 (Volta Kask),
  TOSS & Eldlich match 4 (Count Aurum), Tearlaments matches 2 to 4 (Annick, Zia Sparks, Coralie Fenn), Kashtira & Snake-Eye
  matches 3 and 4 (Nell Ardent, Pilar Sendra). The Stuck? panel opened 124 times (25 in run 1, 99 in run 2). 7 of 12 cups
  were cleared, so the Every Era final never opened ("the final not open" on the last line).
- **Circuit walls in run 2:** the New Wave, Rituals and Fire, Shadows and Storms and Fall Format matches were lost
  repeatedly with Gadgets. Win rates are not bugs, and this is the weak-deck stand-in.
- **The Library cup duels paid:** loaner wins pay half (400 DP for a win with "first win"), a Locals Champion +900.
  Both as the spec says.
- **The run-1 first match** (Goat Cup match 1) took 4 tries and a 5,450 DP "Make it yours" purchase before it passed.

## What this run could not verify

- **The alley and No Limits.** `-qaplay` has no alley, Back Alley or No Limits Invitational path, so those were not played.
  The map showed the "Back Alley (new)" tab from the first cup clear on, and the story scene `alley-rumor` was seen, but
  nothing there was entered. Tonight's other jobs (scene backdrops, the scene queue saved to the profile, the held-back
  Invitational exit, shop arrows, the CPU's destruction fix) are not in this build and were not tested.
- **The Every Era final, New Game+ and Gauntlet** were not reached (the final stayed closed, the save was already past
  the Gauntlet, and New Game+ had already been done in this save's history).
- **The controller, real mouse hover, sound** (muted), and **card pictures** (no `-pics` folder; the shop and cards drew
  placeholder frames). Screenshots were checked by eye for about 15 of the roughly 4,700 files: results, brackets, shop, guests,
  Stuck? panel, tag cup, bond trade. The rest were not looked at. The harness's own checks cover them.
- **A fresh-start campaign** (starter screen, Gus's first duel, early circuits). The run was from a save.
- **Replaying seeds in the harness** was not needed: no app-versus-harness difference showed.

## Files

- `build/overnight/qa/out/qa-log.txt`, `build/overnight/qa/out2/qa-log.txt`: the runs' logs (screenshots in each folder; the
  two folders are 2.3 GB, delete them when done).
- `build/overnight/qa/log.txt`, `build/overnight/qa/log2.txt`: the Unity player logs.
- `build/overnight/qa/profile-start.txt`, `profile-after-run1.txt`, `mods/save/profile.txt`: the three save states.
- `build/overnight/qa/mods/decks/`: the decks, with the 11 identical Gadgets copies.
