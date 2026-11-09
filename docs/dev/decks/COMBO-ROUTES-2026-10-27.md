# Combo routes: real turn-1 boards for Branded Despia, K9 Vanquish Soul and Mekk-Knight (2026-10-27)

## The question

Can the CPU build the big boards real players make with combo decks? `--turn1` (each deck's turn-1 end board, as the
late CPUs play it) said: only sometimes. In about one hand in four it found a real line; the rest fizzled. Three
causes:
- **The playbooks are priority lists, not combos.** They say what to play when, card by card, but not the order a combo
  needs, nor which piece to save for later.
- **The planner looks one decision ahead.** A deeper combo search exists, but with 3 seconds per decision it barely
  changed the boards (Branded and Mekk-Knight unchanged, K9 2.4 to 2.8 monsters).
- **Small mistakes end the line.** Two showed up:
  - **Branded in High Spirits in the Standby Phase:** the CPU played it there and discarded its own starter, Fallen of
    the White Dragon.
  - **A blocked Fusion:** a rules-sense check called Lubellion's Fusion "removal that could only hit our own cards" and
    declined it, so Albion never reached Mirrorjade.

## What was built

- **Combo routes in the playbooks** (`ComboRoute`): a real player's combo notes, written as code.
  - **Start:** a route begins when its starter is in hand at the first decision of the turn.
  - **Steps:** each decision then takes the route's next step the duel offers, and the CPU's usual play resumes when
    no step is left.
  - **Picks:** each step can say what it picks: the search, the Fusion Monster, the materials, the cost, and what to
    reveal.
  - **The planner stands aside** during a route, so a step plays as written (a switch, `PlannerOverridesRoutes`, lets
    the planner override instead).
- **The routes:**
  - **Branded Despia, two routes:**
    - Branded Fusion (or Aluber to search it): Albion, then Lubellion, then Mirrorjade, then Fallen of the White Dragon
      and Cartesia.
    - Fallen of the White Dragon (or Branded in High Spirits to find it): Incredible Ecclesia calls Albaz, and
      Cartesia fuses them into Albion.
  - **K9 Vanquish Soul, three routes:** Jokul and another Level 5 into K9-17 "Ripper"; Razen and Hollie Sue, with Ripper
    once two Level 5s are out; and Ripper from any two Level 5s.
  - **Mekk-Knight going first:** Aleister into Invoked Mechaba.
  - **Turn 1 only:** the Branded and K9 routes run on turn 1 only. On later turns the opponent's board changes the
    right play, and the usual play handles that better.
- **Fixes:**
  - **High Spirits:** held for your own Main Phase.
  - **Fusion effects:** a Fusion that banishes or shuffles its own materials is no longer judged as removal.
  - **The planner and route picks:** it no longer overrides a route step's picks.
- **Tools:**
  - `--turn1 decks`: turn-1 boards. `TURN1_VERBOSE` adds the opening hand and every decision; `TURN2_VS=deck` measures
    the going-second turn against that deck's board instead.
  - `--routes-ab deck opponents`: the same games with routes off, on as written, and on with the planner free to
    override.
  - `YGO_DEBUG_ROUTES` and `YGO_DEBUG_CHAINS`: trace route choices, chain options, selections and yes/no answers.

## Boards: what changed

| Deck | Before | With routes |
|------|--------|-------------|
| Branded Despia | Branded Fusion stopped at Albion or Lubellion; High Spirits wasted in the Standby Phase | Branded Fusion and Aluber end on Mirrorjade, Albion and Lubellion; Fallen of the White Dragon hands on five monsters |
| K9 Vanquish Soul | Never made K9-17 "Ripper" | Ripper in about a quarter of hands, with the hand still full for quick effects |
| Mekk-Knight (going first) | Aleister often unused | Invoked Mechaba in every hand with Aleister, Magical Meltdown or Terraforming, hand traps kept |

The averages move less than the boards do. These lists don't open with a starter in every hand (Branded's is a
Chaos hybrid with one Branded Fusion and one Aluber), and a dead hand stays dead.

## Wins: what didn't change

The same games with routes off and on, against the late decks (Mekk-Knight also against five mid decks):

| Deck | Duels per mode | Off | On, as written | On, planner may override |
|------|----------------|-----|----------------|--------------------------|
| Branded Despia | 120 | 49% | 51% (first: 36% to 40%) | 50% |
| K9 Vanquish Soul | 120 | 30% | 30% | 27% |
| Mekk-Knight (going-first route only) | 120 | 34% (first: 28%) | 33% (first: 28%) | 34% |

Within noise (about ±9 points at 120 duels), the turn-1 routes neither win nor lose games, even though the boards are
the real ones.

A first version also ran a Mekk-Knight going-second route, the guide's break, occupy, convert, finish as one fixed
line. It lost games: 28% overall against 34%, and 31% going second against 40%. It's removed: going second, the
planner's freedom to reorder the turn is worth more than a fixed line.

## Why bigger boards don't win more yet

A turn-1 board is worth what it does on the opponent's turn, and that's the next thing to look at:
- **Mirrorjade:** does it banish at the right moment?
- **K9-17 "Ripper":** does it negate the opponent's hand trap?
- **Invoked Mechaba:** is its negate saved for a real threat?

The CPU's general chain rules decide those, not the playbooks. If they're spent badly, a real board plays like a small
one. The planner's board score is the second suspect: it counts bodies and ATK, so it can't see that Ripper's negate
is worth more than two attackers.

## Part 2: using the board on the opponent's turn

`TURN1_WATCH=deck` plays our turn 1 and then their turn into our board, and logs every chain option we're offered
during it and what we do with it. Two misplays showed up:
- **Mirrorjade banished our own monster on turn 1.** The fix for Lubellion's Fusion had also exempted Mirrorjade's banish
  from the "only hits our own cards" check, because its text names Fusion Materials too. With no opposing monster it
  banished our Fallen of the White Dragon, and Mirrorjade can't use the effect on the next turn, which is when it's
  needed. The exemption is now only the exact "Fusion Summon 1 ... by banishing / shuffling Fusion Materials" wording.
- **Nibiru was never used.** It was offered 6 times against Tearlaments, a deck that summons five times a turn, and used
  0 times. The hand-trap rule only answers activations, and Nibiru's window comes after a summon. Now it's used when the
  opponent's face-up monsters are worth more than ours and the token it gives them (the Tributed monsters' combined ATK)
  can't take most of our LP. Without that second condition, the token's attack killed us.

Two more changes:
- **The planner's interruption value:** a face-up monster that can interrupt now counts if it has a quick banish,
  destroy or bounce as well as a negate. The value stays at +600 (`YGO_INTERRUPT_VALUE`). At 2,000 Branded won more
  (62%) but K9 won less (31%).
- **Playbook chain rules** (`ChainWhen`): the deck's own timing for a Trap or quick effect. Mitsurugi Prayers now waits
  Set while Ame no Murakumo is out, and on the opponent's turn Tributes Murakumo, which comes back and wipes their board.
  It works in play and doesn't change wins (Mitsurugi 55% without it, 56% with it, over 144 duels; `YGO_CHAIN_RULES=0`
  turns it off).

### Wins, with routes on, on the same seeds as before

| Deck | Before these fixes | After |
|------|--------------------|-------|
| Branded Despia | 51% | 58% |
| K9 Vanquish Soul | 30% | 38% |
| Mekk-Knight | 33% | 30% |

At about ±9 points, Branded and K9 are probably real gains; Mekk-Knight is unchanged.

### The other late decks

The same scan against Branded Despia found no misplay of that kind in Light and Darkness, Eldlich, Tearlaments, Ishizu
Tearlaments or Mitsurugi. What they hold back are timing calls that look right:
- Called by the Grave waits for an effect in the GY.
- Eldlich's Traps wait for the End Phase.
- Murakumo's negate waits for a real threat.

No routes were written for them. Across Branded, K9 and Mekk-Knight, routes made the boards real but didn't move wins;
fixing misplays did.
