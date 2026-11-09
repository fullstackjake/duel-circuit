# Playbooks for four October 2026 decks (2026-10-28)

## The question

Four of the eleven October 2026 meta decks rated far below their tournament results (Elo 1382 to 1512, ranks 38 to 69
of 90): Radiant Typhoon, Magistus Fairy Tail, Power Patron and Artmage. Watching their turn-1 boards (`--turn1`)
showed why. The generic bot doesn't follow their plans:
- **Radiant Typhoon** wants Mystical Space Typhoon on its own Typhoon Spells. The rules-sense check refused that as
  "removal that only hits our own cards". The draw logic also played Vision before the free summons, and the planner
  liked a Tribute Summon of Bandit better than the free Meghala.
- **Magistus Fairy Tail** needs Crowley's hand effect on its own turn. The chain scorer gives it 0. Weaver's text
  ("banishment") and Genia read as removal too.
- **Artmage and Power Patron** run Pendulum Treasure and Quick Effect Fusion lines. Treasure picked Vidolium instead of
  the starter, and placing a Pendulum monster as a Scale read as "destroying our own card".

## What was built

- **Four playbooks** (`src/Ygo.Engine/AI/Playbooks/`): `RadiantTyphoonPlaybook`, `MagistusFairyTailPlaybook`,
  `ArtmagePlaybook`, `PowerPatronPlaybook`. Each one gives its deck's starter order, searches, discards, boss picks, and
  holds on cards the bot wasted.
- **`LeadWithRules()`:** when one of the deck's rules applies, the turn planner doesn't override it. Radiant Typhoon,
  Magistus Fairy Tail and Artmage use it.
- **`OwnTargets(...)`:** cards whose removal on our own cards is the plan (Radiant Typhoon's MST), or whose text the
  check misreads (Weaver, Genia, Theorealize Medius, the Ars Magna Spells).
- **A general fix:** a Pendulum monster activated from the hand is being placed as a Scale, so the rules-sense check
  no longer calls it removal. This applies to every Pendulum deck.
- **New `PlaybookView` helpers:** `ChainTopIsMine`, `ChainHasMine`, `ChainTopIsTheirs`, `GraveyardCount`.
- **`Playbooks.Off`** and **`--playbook-ab <deck> <opponents> [--games N]`:** the deck played by the late CPU (skill
  1.0 with the planner) with its playbook off, then on, on the same seeds, first turn alternating.

## Results

Turn-1 boards (average monsters at the end of turn 1):

| Deck | Before | With the playbook |
|------|--------|-------------------|
| Radiant Typhoon | 1.2 | 1.3 to 1.5 (real engine lines; some hands still brick) |
| Magistus Fairy Tail | 2.2 | 3.2 (Spells and Traps 0.8 to 3.1) |
| Artmage | 2.0 | 3.8 (Diactorus, Nerva or Non-Finito in most hands) |
| Power Patron | 3.7 | 3.1 |

`--playbook-ab`, the late CPU against Dracotail, Mermail Atlantean, Lunalight, Elfnote, Angelechy and Sky Striker, 20
duels each (120 per mode):

| Deck | Playbook off | Playbook on |
|------|--------------|-------------|
| Radiant Typhoon | 30% | **57%** (going first 21% to 51%) |
| Magistus Fairy Tail | 30% | **59%** (going first 21% to 53%) |
| Artmage | 57% | 55% |
| Power Patron | 60% | 58% |

With the planner on, Artmage and Power Patron already played well, and their playbooks don't change that (the changes
are within noise). The deck ratings use the balanced bot, which has no planner, like the Expert band's CPUs. There all
four playbooks help a lot. `--rate-decks --rounds 300`, before and after (`build/deck-ratings-before-playbooks.txt`,
`campaign/deck-ratings.txt`):

| Deck | Elo before | Elo after | Rank before | Rank after |
|------|-----------|-----------|-------------|------------|
| Radiant Typhoon | 1382 | 1683 | 69 | 16 |
| Magistus Fairy Tail | 1403 | 1630 | 61 | 22 |
| Artmage | 1512 | 1617 | 38 | 25 |
| Power Patron | 1403 | 1538 | 62 | 36 |

The other 86 decks moved by 9 points (median) and at most 52, so the four gains are real.

In the campaign, `--playbook-test` (each late boss against the skilled player, 16 games with its playbook and 16
without): Maestra Vellum's Artmage, the Fall Format boss, went from 31% to 56%. The late bosses overall went from 54%
to 60%.

## Placement

No changes are needed. Expert Series: Fall Format now averages 1574 Elo (Shadows and Storms 1585, Rituals and Fire
1612). The Fall Format Tag Cup averages 1596, up from 1473, and stays below the Expert Tag Cup (1654). Its
hardest pair is now Radiant Typhoon with Lunalight. The Fall Format Tag Cup has no measured Stuck? advice entries, so
none went stale.

## Still open

- Radiant Typhoon still bricks on some opening hands: hands with Bandit or Hraesvelgr and no Eldam, Swen or Meghala.
- Power Patron's turn-1 board got smaller (Jupiter is rarely made) even though its rating rose. Its rules help later
  turns more than turn 1.
- Expert Series: Fall Format (1574) comes after The New Wave (1726) and is weaker than it. It plays as a breather
  before the Master band.
