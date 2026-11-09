# Mekk-Knight Invoked Evolved (2026 TCG)

The Mekk-Knight starter, rebuilt from the "Mekk-Knight Invoked 2026 TCG Deck + Combo Guide" (Duel Circuit Deck Lab,
October 26, 2026). The deck file is `decks/mekk-knight-invoked.ydk`, built from `decks/recipes/starters.txt`; the CPU's
plan is `MekkKnightPlaybook` (src/Ygo.Engine/AI/Playbooks).

**Game plan:** going second, interrupt what matters, put the opponent's cards into favorable columns, break the field,
then choose between an Invoked / Mekk-Knight push for lethal and a Mechaba + World Legacy's Secret hold.

## The list (40 / 15 / 15)

| Main (23 monsters) | Main (13 spells, 4 traps) | Extra (15) |
|--------------------|---------------------------|------------|
| 3 Aleister the Invoker | 3 Magical Meltdown | 2 Invoked Mechaba |
| 3 Mekk-Knight Purple Nightfall | 2 Invocation | Invoked Purgatrio |
| 3 Mekk-Knight Blue Sky | 2 World Legacy's Memory | Invoked Augoeides |
| 2 Girsu, the Orcust Mekk-Knight | 2 Super Polymerization | Invoked Caliga |
| Mekk-Knight Indigo Eclipse | 2 Forbidden Droplet | Invoked Raidjin |
| Mekk-Knight Red Moon | Harpie's Feather Duster (Limited) | Garura, Wings of Resonant Life |
| Mekk-Knight Yellow Star | Terraforming (Limited) | Mudragon of the Swamp |
| 3 Ash Blossom & Joyous Spring | 3 Infinite Impermanence | Artemis, the Magistus Moon Maiden |
| 3 Mulcharmy Fuwalos | World Legacy's Secret | Salamangreat Almiraj |
| 2 Gameciel, the Sea Turtle Kaiju | | Mekk-Knight of the Morning Star |
| Nibiru, the Primal Being | | Lib the World Key Blademaster |
| | | S:P Little Knight |
| | | Accesscode Talker |
| | | Mekk-Knight Crusadia Avramax |

Side (15): 3 Evenly Matched, 3 Cosmic Cyclone, 2 Nibiru, 3 Solemn Strike, 2 Book of Eclipse, 2 Lightning Storm.

**Legality:** legal under the October 2026 TCG list. In the campaign, every circuit's list allows it except TCG April
2015 (Super Polymerization: one Ban Pass). The old list needed up to three (Red Reboot, True King of All Calamities,
Apollousa, all Forbidden now).

## How the CPU plays it

The guide's turn priorities, as playbook rules:

| Priority | What the CPU does |
|----------|-------------------|
| 1. Interrupt | Ash Blossom, Infinite Impermanence and Mulcharmy Fuwalos on the opponent's turn (the CPU's hand-trap sense) |
| 2. Break | Gameciel on their monster, Harpie's against 2+ Spells/Traps, Forbidden Droplet, Super Polymerization (Garura, Mudragon) before committing monsters |
| 3. Occupy | Purple Nightfall into a busy column, then banishes itself for Blue Sky; Blue Sky only where it searches; Red Moon facing a monster, Yellow Star facing a Spell/Trap. A Mekk-Knight lands in the zone facing the most opposing cards |
| 4. Convert | Normal Summon Aleister (Invocation), or Girsu when there's no Aleister or it's turn 1: Memory to the GY, Tokens, Lib, Set Secret |
| 5. Stabilize | Invocation into Mechaba (Artemis for the LIGHT material); Augoeides to remove a monster |
| 6. Finish | Purgatrio (Almiraj for FIRE) when they hold 4+ cards; Avramax from two Extra Deck monsters; S:P and Morning Star into Accesscode Talker |

Aleister the Invoker's hand effect (+1000 ATK/DEF to a Fusion Monster, this turn only) waits for a battle it turns:
the CPU attacks a bigger monster when the boost gets it over the top, boosts when the opponent attacks a Fusion
Monster it would otherwise lose, and boosts a direct attack when the turn's damage becomes lethal. Otherwise the
Aleisters stay in hand (it used to spend them at the first window: holding them is worth about 6 points, 65.9% to
72.1% over the same 4,680 duels).

World Legacy's Memory is held while a Fusion, Link or Token line is still possible: after it resolves only Mekk-Knights
can be Special Summoned for the rest of the turn. Blue Sky stays in hand when no column holds an opposing card.

## Corrections to the guide

The guide's rulings check out against the card text, with these exceptions:
- **Invocation's GY effect** (Combo 1, step 8) shuffles Invocation into the Deck and adds the banished Aleister to the
  hand. It doesn't return Invocation itself.
- **World Legacy's Memory** also returns the Mekk-Knight it summons to the hand during the End Phase, so it's a body for
  this turn only.
- **World Legacy's Secret** is a Trap: it can't be activated the turn Lib Sets it in any case (Combo 2, step 4).
- The `.ydk`'s `#banlist TCG-2026-10-19` isn't a list name the game knows; the deck uses "2026.10 TCG".

## Measured

Quick-bot rating (300 rounds, `--rate-decks`): Elo 1620, 60% of 296 duels, 56% going first / 65% going second. The old
list rated 1690 (59% / 68%) a few hours earlier against a slightly different field (five decks gained playbooks since).

## Tweaks tested (kept the guide's list)

`--deck-variants`: each version played 4,680 duels against every other deck in the game (60 each, half going first,
the same seeds for every version), with the quick CPU and this playbook. A difference of about 2 points is beyond
noise.

| Version | Won | First / second | vs the guide |
|---------|-----|----------------|--------------|
| The guide's list | 65.9% | 61.2% / 70.6% | |
| One World Legacy's Memory, a 3rd Forbidden Droplet | 62.9% | 58.9% / 67.0% | -3.0 |
| One World Legacy's Memory, a 3rd Invocation | 64.6% | 59.4% / 69.7% | -1.3 |
| One Girsu, a 2nd Mekk-Knight Red Moon | 63.9% | 59.4% / 68.4% | -2.1 |
| Both of the last two | 63.5% | 58.2% / 68.8% | -2.5 |
| Droplet and Red Moon swaps together | 61.7% | 57.3% / 66.0% | -4.3 |

Memory, the card that looked clunky on paper, is a free Mekk-Knight from the Deck in practice (Purple Nightfall, then
Blue Sky); the second Girsu earns its slot too.
