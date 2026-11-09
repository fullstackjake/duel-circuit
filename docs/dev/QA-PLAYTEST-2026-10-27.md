# QA playtest: the new duel screen and duelist panels (2026-10-27)

The whole campaign again from a fresh save, on the build with everything from 2026-10-26 and 10-27: the coin toss, the
Mekk-Knight Invoked Evolved starter, the new playbooks, the redesigned duel screen (space mats, glass zones, HUD panels,
corner icons) and the new duelist panels (1,000-LP segments, change chips, danger steps, turn tags, partner chips).
Played by the QA stand-in through the real screens with loaners after a wall (`-qaloaners`), sound muted.

## Results

**150 duels, 97 won, 53 lost, 0 issues**, from the first table to the Tier 0 Gauntlet and New Game+ (its first circuit
cleared), in 2 hours 23 minutes. Every circuit, every Tag Cup up to Advanced, and every guest was played.

| | |
|-|-|
| Coin toss | Won 75 (going second 57 times with Mekk-Knight), lost 75; turn 1 as chosen in every duel |
| Second deck | Build, then Save asked Overwrite or Save as new; saved as "Mekk-Knight Invoked Evolved (new)" |
| Recipe Book | Unlocked count matched the circuits at every band |
| New Game+ | Started after the Gauntlet: 704 cards, 177,944 DP and level 33 kept; the first circuit cleared |

| Band | Won-lost |
|------|----------|
| Beginner and Novice circuits | 33-1 |
| Intermediate and Advanced circuits | 24-0 |
| Expert Series | 12-8 |
| Master League | 11-20 (Houston 2026: 3-11) |
| Tier 0 Gauntlet | 4-3 |
| Tag Cups | 13-13 (Expert 1-4, Master 0-6, not cleared) |
| Guests | Ines and Nocturne beaten both ways; Rhea 1-3; Seraph 0-4 |

## The new panels in real duels

Checked on the run's screenshots:
- **Change chips with the right reason:** a sword on battle damage, a coin when Solenne paid 800 LP for Brain Control,
  the amount beside the number.
- **Segments:** 5,500 LP showed five and a half lit segments; 300 LP one sliver.
- **Danger:** at 300 LP the tag read "! Low", the bar was red with stripes and the frame glowed red.
- **Turns:** "Their turn" on the opponent's panel during their turn, and yours dimmed.
- **Tag duels:** the waiting partner in the round chip (Ribbit Ruiz, Old Gus, Solenne), swapping each turn.
- **Opponent's healthy bar:** light coral, so red on their panel only means danger.

## CPU play

- **Replay audit** (the last 30 duels): one CPU finding, Kitkallos summoned from Rulkallos, a normal Tearlaments line.
- **Blunder audit, 200 duels**, against the same audit at the commit before 2026-10-27's CPU changes: 0 engine errors in
  both, the same nine categories and similar counts (133 findings against 119, 58 games each). The extra self-damage
  and own-card findings come from decks that destroy their own cards or pay LP on purpose (Fire King Island, the
  Unchained Souls, Dark Contract with the Gate), several of which got playbooks that day. No new kind of blunder.
  Raw: `build/audit-2026-10-27.txt` and `build/audit-2026-10-27-before.txt`.

## Still open

- **The late Tag Cups and Seraph** remain unbeaten by the stand-in, as in parts 4 and 5 (see the saved next-up list:
  player tools, not weaker CPUs).
- **The stray blue box** at the left edge of the duel screen, above the seam, is still there in campaign duels.
  *(Fixed later on 2026-10-27: it was the turn banner's first frame. QA's per-turn screenshots are taken the frame a
  turn starts, when the "YOUR TURN" banner sat at full strength just off the field's left edge. The banner now fades in
  as it slides in, and a 4-duel QA run had none in 28 turn screenshots.)*
- **Your portrait:** the "YOU" placeholder stays until there's a portrait for you; in tag duels your partner chip shows
  a "Y".
