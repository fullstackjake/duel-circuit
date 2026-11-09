# Mekk-Knight: what holds the two decks back (2026-10-29)

## The question

"Can we work on the Mekk-Knight deck? How can it be better?" Two decks carry the name:
- **Mekk-Knight Invoked Evolved** (`decks/mekk-knight-invoked.ydk`): one of the four starters, the one the wall study
  measures as "your deck". `MekkKnightPlaybook` plays it for the CPU.
- **Mekk-Knight Crusade** (`decks/structure/mekk-knight.ydk`): a 2018 structure deck in the card shop (2,500 DP). No
  circuit match uses it, and it has no playbook.

## Mekk-Knight Crusade: a broken list (fixed)

The recipe listed Mekk-Knight Spectrum Supreme (a Link Monster) under the Main Deck and Crusadia Maximus x2, Crusadia
Reclusia and Crusadia Revival under the Extra Deck. ocgcore puts a non-Extra Deck card listed there on top of the
Main Deck, so `--turn1` showed the deck opening with Revival, Reclusia and both Maximus in every hand, and Spectrum
Supreme could never be summoned.

Fixed in the recipe (44 + 10 cards, nothing removed). `tools/build_deck.py` now refuses a card in the wrong section.
No other recipe or deck in `decks/` had one.

`--deck-variants mekk-knight`, quick bot, 1,800 duels per version against every deck, the same seeds:

| Version | Won | First / second | vs now |
|---------|-----|----------------|--------|
| As it was | 40.4% | 36.5% / 44.4% | |
| Sections fixed (44 cards): **kept** | 48.6% | 43.7% / 53.5% | +8.2 |
| Fixed, 40: -2 Torrential Tribute, -Avram, -1 MST | 45.7% | 41.0% / 50.4% | +5.3 |
| Fixed, 40: -Avram, -Revival, -2 MST | 50.2% | 45.1% / 55.4% | +9.8 |
| Fixed, 40: -Avram, -2 Bottomless Trap Hole, -1 MST | 45.8% | 42.2% / 49.4% | +5.4 |

The best 40-card trim is within noise of the 44-card fix, so the fix keeps every card the recipe meant to have.
`--rate-decks --rounds 300`: Elo 1447 (50th of 90, 40%) to 1522 (42nd, 53%). The other decks moved within noise.

## Mekk-Knight Invoked Evolved: the starter

**Against the whole field it's strong:** Elo 1692, 14th of 90, 72% (71% first, 73% second). The other three starters
rate 1277 to 1418, and the Beginner circuits' decks 1277 to 1482.

**Against the late circuits it's weak.** `--playbook-ab` against the seven late decks plus Snake-Eye, Yubel and Sacred
Beast, 12 duels each, the late CPU (skill 1.0, planner) on both sides:

| | Won | First / second |
|---|-----|----------------|
| Playbook off | 19% (23/120) | 10% / 28% |
| Playbook on | 23% (28/120) | 13% / 33% |

Against the seven late decks alone it won 11 of 84 (13%); against Snake-Eye and Yubel, 12 of 24. The playbook is worth
about 4 points (noise is about ±5 at 120 duels).

**Going first** (`--turn1`, 12 hands): Invoked Mechaba in 9, with hand traps kept. Two hands opened World Legacy's
Memory without Aleister: the planner summoned Girsu with it, and Memory's lock (only Mekk-Knights for the rest of the
turn) then blocked Girsu's World Legacy Tokens, so no Lib. One hand had no play. The deck chooses to go second, so
this rarely comes up.

**Going second** (`TURN2_VS`, 8 duels each against Snake-Eye, Branded Despia, Mitsurugi): it removed 1.6 to 2.5 of
their cards per turn but dealt only 325 to 900 damage, and won on the turn 0 times in 24. Its turn breaks a board but
doesn't convert into a lethal push. One duel showed a misplay: the planner attacked with Aleister (1,000 ATK) before
fusing, then Invocation couldn't be activated in Main Phase 2.

**What a playbook fix is worth here:** the player pilots this deck, not a CPU. No circuit opponent plays it. Its
playbook only drives the wall study's "your deck" numbers and the Stuck? advice measured from them. A better
playbook makes that advice more accurate, not the player stronger.

### List swaps

`--deck-variants mekk-knight-invoked`, quick bot, 1,800 duels per version, the same seeds (cards from its own Side
Deck):

| Version | Won | First / second | vs the guide |
|---------|-----|----------------|--------------|
| The guide's list | 70.8% | 67.5% / 74.2% | |
| -2 Mulcharmy Fuwalos, +2 Lightning Storm | 70.8% | 67.5% / 74.3% | +0.1 |
| -2 Mulcharmy Fuwalos, +2 Nibiru | 70.0% | 66.8% / 73.3% | -0.8 |
| -2 Forbidden Droplet, +2 Lightning Storm | 72.9% | 67.4% / 78.5% | +2.1 |

Lightning Storm for Forbidden Droplet is the one swap that helps: +4.3 points going second, the deck's chosen turn.
Lightning Storm is at most Semi-Limited on every list, so two copies are legal in every circuit. Not applied: the
user kept the guide's list, which is strong enough for the early campaign.

## The starter's strength

The game already offers it as the strong, opt-in starter: its tile says "The early circuits will be easy", and the
recipe calls it "far stronger than the early circuits' decks". Against the late decks it wins 13% as the CPU plays it,
so a player still has to build up from packs for the late circuits.

Raw output: `build/playbook-ab-mekk-knight-invoked.txt`, `build/variants-mekk-knight-crusade.txt`,
`build/turn2-mekk-*.txt`.
