# QA playtest and night work, 2026-10-30

The night's list: (1) re-check the Tearlaments loaner going first and second with the deck as built, and add a toss
tip if it holds; (2) a fresh full-campaign playtest under the new ban-list rule; (3) the chain panel covering the
Banished pile; (4) Master Tag Cup match 3 with what a player has when the cup opens. Item 5 came up along the way:
the 20 s "board busy animating" note from the last run.

## 1. Tearlaments as built, and a toss tip (66827b3)

`--circuit-study` with the loaner as the game now lends it outside era-limited play (the full 2022 list), 64 duels a
match. Results: `build/late-asbuilt/new.txt`.

| Circuit | Match | Won | First | Second |
|---|---|---|---|---|
| Houston 2026 | Habakiri, Mitsurugi | 62% | 37% | 87% |
| | Archmage Noctis, Light and Darkness | 56% | 34% | 78% |
| | Albaz the Fallen, Branded Despia | 56% | 40% | 71% |
| October 2026 | Jolt, Blitzclique | 78% | 62% | 93% |
| | Warden Hask, Sacred Beast | 53% | 43% | 62% |
| | Mage Valdris, Dark Magician Chaos Ritual | 64% | 34% | 93% |
| | Sir Lucan Graye, Chaos Ritual | 43% | 18% | 68% |
| Tier 0 Gauntlet | Count Aurum, Eldlich | 71% | 62% | 81% |
| | Razen Vanquish, K9 Vanquish Soul | 82% | 81% | 84% |
| | Kitkallos, Tearlaments | 57% | 46% | 68% |
| | Seer Kelbek, Ishizu Tearlaments | 62% | 53% | 71% |

The full deck is far stronger than the cut one was (8-45% a match). Going second is still much better at most
matches, so the coin toss now quotes it: `campaign/toss-advice.txt` (`--circuit-study --write`) holds these and
Ryzeal at The New Wave, and the toss screen shows "Measured with this deck here: going second won 87%, going first
37%" where first and second differ by 20 or more (7 of the 11 Tearlaments matches). Only your side reads it; the CPUs
choose as their decks do.

## 2. The playtest

A fresh save, `-qaplay -qafull -qaloaners -qatools -qagames 230`, muted, on 66827b3. Log:
`build/qa-night3/out/qa-log.txt`. All nine app checks passed on the build first.

**229 duels, 158 won, 71 lost, 0 issues**, 3 h 28 min. The first campaign ended at duel 182 (last run: 221); the rest
was New Game+.

| Band (first campaign) | 2026-10-29 | Tonight |
|---|---|---|
| Beginner to Advanced circuits | 48-5 | 48-4 |
| Expert circuits | 16-16 | 16-20 |
| Master circuits | 15-39 | **15-15** |
| Tier 0 Gauntlet | 4-16 | **4-1** |
| Tag Cups | 18-20 | 18-22 |
| DP at the Gauntlet's end | 269k | 277k |

| Circuit | 2026-10-29 | Tonight |
|---|---|---|
| Expert Series: Shadows and Storms | 4-0 | 4-9 |
| Expert Series: Rituals and Fire | 4-4 | 4-1 |
| Expert Series: The New Wave | 4-9 | 4-7 |
| Master League: Link Wars | 4-5 | 4-12 |
| Master League: Graveyard Kings | 4-6 | 4-1 |
| Master League: Houston 2026 | 3-17 | **3-0** |
| Master League: October 2026 | 4-11 | **4-2** |
| Tier 0 Gauntlet | 4-16 | **4-1** |
| Expert / Master Tag Cup | 3-4 / 3-9 | 3-6 / 3-9 |

The stand-in followed the toss tip: it went second at every measured Houston and October match it won the toss for,
and first where no tip showed. It took turn 1 in 59 of 229 duels (last run 74).

### Two circuits got harder, and why

- **Shadows and Storms 4-9** (the Mekk-Knight starter, then the Kozmo loaner, at Nessie Grapha's Danger! Dark World:
  1-7). Measured as built, that match is 43% with the starter and 37% with Kozmo; 9 or more losses at this circuit
  happen in about 5% of runs. Bad luck, mostly.
- **Link Wars 4-12** (the Ryzeal loaner). Measured, Ryzeal wins 52-72% at each match; 12 losses there essentially
  never happens by chance. That led to a real bug:

**Loaners played without their playbooks in the app** (fixed tonight, see below). A borrowed deck is named after its
title in a duel ("Ryzeal / Mitsurugi Ryzeal (loaner)"), and the playbook lookup only knew recipe names ("ryzeal"), so
the QA stand-in played every loaner without its playbook, and so did **Kit when you lend Kit a loaner**, and any
partner bringing a copy of a deck ("Mekk-Knight Invoked Evolved (new)"). The harness studies, and so the Stuck?
advice, used recipe names and played with playbooks, which is why the app did worse than measured at Link Wars.
Tearlaments did well in spite of it.

## 3. The chain panel over the Banished pile (66827b3)

When a long callout (Lunalight Liger Dancer's text) sits above a three-link chain, even the newest callout alone pushed
the chain panel onto your Banished pile. Now that callout's text box gives way (its text shrinks to fit, down to three
lines) until the chain panel clears the piles. New pile check: a long callout over a three-link chain leaves the board
clear.

## 4. Master Tag Cup match 3 when the cup opens (66827b3)

42 teams from what a player has when the cup opens (`--wall-study --loaners 5 --partners 4 --kit 2`, 24 duels each;
`build/m3-open/run.txt`). The best: the Angelechy loaner with Ryzeal Duo (54%, 43% at 32 duels on the advice's seeds)
or with Grandmaster Celeste (50% / 40%), so about 45%. Nothing else reaches 50%. The Ryzeal Duo team is now in the
advice. The teams that do better (Tearlaments with Seer Kelbek, 75%) unlock with the Gauntlet's circuits. If 45% is too
low for a cup's last match, opening the Master Tag Cup after Graveyard Kings (one circuit later) would let a player
bring the Fire King Snake-Eye team that measured 65-71% at matches 1 and 2; that's a design choice, not changed.

## 5. The 20 s "board busy" note (66827b3)

It came during a long Lunalight combo animating batch after batch, which the QA stand-in couldn't tell from one stuck
animation. Now each animation batch is counted; QA flags only a single batch holding the board 20 s, and any batch over
8 s is written to the player log with its events. Tonight: no long batch and no long stretch.

## Fixed after the playtest: loaners play by their recipe's playbook

- A loaner loaded for a duel carries its recipe (`Deck.Notes["loaner"]`), and bots look up the playbook by it
  (`Playbooks.ForDeck(Deck)`): your partner Kit with a loaner, and the QA stand-in.
- A copy of a deck finds its playbook by its name without the copy suffix ("(new)", "(2)").
- New campaign check. CPU opponents were never affected (their decks keep their recipe names).

Not measured yet: the playtest again with this fix (Link Wars and the Expert Tag Cup, where Kit brings loaners, should
come out closer to the advice).
