# QA playtest, part 4: the late bands (2026-10-26)

The part 3 save ([part 3](QA-PLAYTEST-2026-10-26-PART3.md)), carried on by the QA stand-in through the real screens:
the Advanced, Expert and Master bands, the Tier 0 Gauntlet, the late Tag Cups, Seraph, a Structure Deck, the Recipe
Book at every band, and into New Game+. The stand-in plays Mekk-Knight Invoked and is allowed loaners
(`-qaloaners`): after repeated losses it takes the versus screen's deck arrows to a loaner, as a stuck player could.

**Important:** every duel here was played before the coin toss existed, so **you always went first** (the rules core
gave turn 1 to player 0). That was found during this run and fixed before this report; see the end.

## Results

Six runs (qa30-qa35, logs in `build/qa-late-resume/logs`), about 300 duels, **0 issues** logged (no stuck prompts, no
DP or token mismatches, no layout faults).

| Circuit | Won-lost | Notes |
|---------|----------|-------|
| Intermediate and Advanced circuits (Golden Age, Control Kings, Link Age, Hybrid Engines, Xyz and Link Regionals) | 20-0 | Mekk-Knight walks through, as in part 3 |
| Expert Series: Shadows and Storms | 4-18 | The cliff from the game review: the turn planner switches on |
| Expert Series: Rituals and Fire | 4-28 | Cleared with loaners |
| Expert Series: The New Wave | 4-4 | |
| Master League: Link Wars | 4-5 | Loaners |
| Master League: Graveyard Kings | 4-15 | Loaners; Tearlaments and Kashtira each took 8-9 tries |
| Master League: Houston 2026 | 3-46 | **The wall.** Archmage Noctis (Light and Darkness) took 33 tries, Albaz (Branded Despia) 13 |
| Tier 0 Gauntlet | 4-2 | With the Branded Despia loaner, unlocked by beating Albaz |
| Tag Cups: Intermediate, Advanced | 6-1 | |
| Tag Cups: Expert, Master | **0-22** | Never cleared; the stand-in moved on to the circuits |
| Guests: Rhea, Seraph | 1-14 | Seraph's tag duel won once; Rhea never |
| New Game+ (Beginner Circuit, Rookie Cup) | 5-4 | The 4 losses: the stand-in kept a weak loaner (below) |

The Gauntlet cleared at level 39 with 174,000 DP. The finale story played, then New Game+ started: circuits reset; 802
cards, the DP, level 39 and 8 tokens kept. The first NG+ circuit cleared normally.

| Feature | What happened | |
|---------|---------------|-|
| Seraph | Unlocked at the Advanced band; lost one on one, won in tag ("Seraph can partner you in tag duels now") | ✓ |
| Structure Deck | Bought for the shop's era; its cards added and saved as a campaign deck | ✓ |
| Recipe Book | Unlocked count matched the circuits at every band (70 of 71 at the Gauntlet) | ✓ |
| Loaners | Half DP, every time, on the results screen too (after fix 1) | ✓ |
| Card Trader and DP buys | Tokens and Ban Pass slots at the right prices; balances always matched | ✓ |
| New Game+ | Offered by the finale story and the New game button; everything earned kept | ✓ |

## Bugs found and fixed

### 1. The results screen showed a loaner win at full rates (medium)

A loaner pays half, and the save got half, but the circuit results screen listed the circuit's full rates (8,638 DP
listed, 7,694 paid). **Fixed:** it lists what was paid ("Win and first win (half: loaner deck)").

### 2. Recipe showed the deck's file name (low)

Recipe's message and Save as new offered "stall-burn", the file name. **Fixed:** Recipe names the deck after the
recipe ("Stall Burn").

### 3. You always went first (critical)

The rules core always gives turn 1 to player 0, and you are always player 0. Nothing in the game decided it. Your
setup was never interrupted on turn 1, and Mekk-Knight (built to go second) opened into an empty field every duel.
**Fixed with a coin toss** before every duel you start: the winner chooses Go first or Go second; characters go
first unless their deck's playbook prefers second. It needed a small core patch (`patches/ygopro-core`, the
`DUEL_SECOND_PLAYER_STARTS` flag) and `tools/build_core.sh` to apply it. Tag duels give turn 1 to the winning team's
lead. Checked by `--first-turn-test`, the campaign check (won toss, Go second: turn 1 is theirs; lost toss: their
choice), and the QA stand-in (it tosses before every duel and logs a mismatch as an issue). The campaign check also
caught the toss starting a duel twice (a click and the automatic Continue both fired), fixed before release.

### 4. Save overwrote the starter after Build, without asking (high)

Found by the toss spot run on a fresh save. Save only asked "Overwrite or Save as new" when more than half the cards
changed. Early in a campaign Build has few cards to choose from and reuses most of the starter's, so "Built around
V-Tiger Jet" went straight over Mekk-Knight Invoked. **Fixed:** Save always asks after Recipe, Build or Import (a
whole new deck), as well as when most cards changed.

### 5. Searches were silent in the duel log (medium)

A question from play: "When Aleister is summoned, is the CPU not grabbing Invocation?" It was: the CPU searched every
time (now a `--sense-test` case, at full and at 0.7 skill). But the log only said "activates Aleister the Invoker",
and Invocation went into a face-down hand. **Fixed:** the log says what each effect adds to a hand ("Ryo adds
Invocation from the Deck to the hand"). A search is shown to the opponent by rule, so it's named even from a hidden
hand.

## What this run says

- **Houston 2026 is the hardest wall in the game**, harder than the Gauntlet after it. Archmage Noctis's Light and
  Darkness deck beat a modern deck 32 times in a row. Noctis stays as he is (opponents aren't weakened to fix walls);
  the way through is the player's tools: scouting his deck after a loss, practising against it, a "Stuck?" panel
  pointing at the loaner that beat him, and difficulty stars that warn about it.
- **The late Tag Cups can't be won** by this pilot (0-22). This matches the review's tag findings and its fixes
  (partners borrowing your decks, matchup advice on the Tag Cup screen), with the opposing pairs unchanged.
- **The QA stand-in's loaner rule needs a reset in New Game+.** It carried a weak loaner (Classic) into the Beginner
  Circuit and lost four times to Captain Brask with it. Not a game bug: a player would pick their own deck. The QA
  tool should drop back to its own deck after a win.
- **The numbers need re-measuring with the toss.** Half the duels are now played second. Expect the early bands to
  get harder for Mekk-Knight going first, and easier where it gets to go second.
- **CPU play:** the replay audit of the late duels found no CPU blunders (a decked-out Ryo in a 68-turn duel); the
  other findings were the stand-in's own trades.
