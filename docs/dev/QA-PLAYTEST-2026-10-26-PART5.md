# QA playtest, part 5: the whole campaign again, with the coin toss (2026-10-26)

Parts 1 to 4 were all played before the coin toss existed: you always went first, and every CPU always went second.
This run replays the campaign from a fresh save with the toss, so the player gets to go second and the CPUs get to go
first. The QA stand-in plays the Mekk-Knight Invoked starter (the pre-2026 list: the build was made before the starter
became Mekk-Knight Invoked Evolved), borrows the best loaner after a wall (`-qaloaners`), and chooses on the toss as a
player would: second with Mekk-Knight, first with a go-first loaner. The CPUs choose by `Playbooks.SecondDecks`.

## Results

**143 duels, 96 won, 47 lost, 0 issues.** Every circuit, Tag Cup up to Advanced, every guest, the Tier 0 Gauntlet, and
into New Game+ (its first circuit cleared, everything earned kept). 2 hours 10 minutes at full speed.

| The toss | |
|----------|-|
| You won it | 70 times: going second 58 (Mekk-Knight), going first 12 (a go-first loaner) |
| You lost it | 73 times: the CPU went first 72 times; Tenpai Dragon's Chundra Sangen let you go first, as Tenpai should |
| Turn 1 as chosen | Every duel (the stand-in logs an issue if the engine's first turn doesn't match the toss) |

## The walls were mostly going first with a going-second deck

| Circuit | Part 4 (always first) | Part 5 (the toss) |
|---------|----------------------|-------------------|
| Expert Series: Shadows and Storms | 4-18 | 4-0 |
| Expert Series: Rituals and Fire | 4-28 | 4-3 |
| Expert Series: The New Wave | 4-4 | 4-1 |
| Master League: Link Wars | 4-5 | 4-6 |
| Master League: Graveyard Kings | 4-15 | 4-8 |
| Master League: Houston 2026 | 3-46 | 3-7 |
| Tier 0 Gauntlet | 4-2 | 4-0 |
| Rhea, one on one | 0-24 (parts 3-4) | 1-0 |
| The whole campaign | about 250 duels to the Gauntlet | 139 |

The game review's "Expert cliff" and part 4's "Houston is the hardest wall in the game" were mostly the stand-in going
first with Mekk-Knight, a deck that needs the opponent's cards on the field. Going second, it clears the Expert band
almost without losing; the Master League is still a real step (4-6, 4-8, 3-7), as the top band should be.

Still hard: the **Expert and Master Tag Cups** (1-4 and 0-6, not cleared) and **Seraph** (0-2 alone, 0-2 in tag). The
tag findings in the review stand.

## Everything else

- **Replay audit** (the last 30 duels, Master League to New Game+): no CPU blunders. Two CPU findings, both ordinary
  trades (Eldlich into Knightmare Phoenix for its removal; Jokul into S:P Little Knight); the rest were the stand-in's.
- **Bugs:** none new in the campaign. Found today on the way here and fixed: the CPU stuck in a forced Battle Phase
  facing Sangenpai Transcendent Dragion; Save not asking after Build on a new save; searches missing from the duel log;
  the toss's Continue starting a duel twice.
- **In-app checks** on the new build (the Evolved starter, today's playbooks): all nine pass. The board check failed
  once on its controller Back-button step (the cursor wasn't back on the card yet) and passed 23 of 23 on both reruns:
  a timing flake to watch, not a regression.
- **New Game+:** cards, DP and level kept; the circuits reset. Tokens were 0 because the stand-in had traded them all.

## What changes

- The difficulty curve in the game review (its first finding) needs re-measuring with the toss: with a deck that goes
  second, the cliff it described isn't there. The review now points here.
- The starter is now **Mekk-Knight Invoked Evolved** ([docs/dev/decks](decks/MEKK-KNIGHT-INVOKED-EVOLVED.md)). This run used
  the older list; the in-app checks ran with the new one.
