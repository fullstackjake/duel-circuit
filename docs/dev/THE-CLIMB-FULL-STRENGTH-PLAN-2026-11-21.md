# The climb at full strength: the deck plan (2026-11-21)

Decided (owner, 2026-11-21): the Novice and Intermediate bands play at **skill 1.0**, and difficulty comes back into band
by **which deck sits where**. This plan picks those decks. A plan only: no code or data changed. It follows
[THE-CLIMB-SPEC-2026-11-20.md](THE-CLIMB-SPEC-2026-11-20.md) ("As measured", round 2) and its raw runs in
`build/climb/runs/` (the round 1 rows at 1.0: `D3-n100-*`, `D4-i100-*`) and `build/climb/runs2/`.

## What the numbers say

- **Elo doesn't predict these matchups.** Domain Monarch is rated 1,202, one of the weakest decks in the pool, and the
  starters win 16-20% against it (its floodgates stop their plays). Qliphort (1,225) and Gouki (1,259) hold them to 20-33%.
  Warriors (1,418) beats nearly everything from the Beginner band. What the starters can beat is **old-era beatdown and
  wall decks** (the Beginner band's 73-82%, Altergeist 75-79%, Zoodiac 54-66%, Machine Re-Volt 37-62%, Infernity
  50-58%). What walls them is **modern engines and floodgates**, whatever their rating. So every swap below picks a
  deck from the first kind, and every one is measured before it's kept.
- **The step at 0.9** (the CPU starts planning its turns) took about 30 points off every row at once. Decks have to
  give those points back where they hurt most: the walls.

## 1. Targets

The old bands (Novice 50-65, Intermediate 45-60) were per band. At full strength they need two changes. Both stay
`[PLACEHOLDER]` working targets the measurements may move.

- **Per circuit, falling step by step**, so circuit 5 isn't a cliff after the Beginner band's 73-82%:

  | Circuit | Target (gate mean) | Now (w / z) | Change needed |
  |---|---|---|---|
  | Underdog Open | 58-70 | 47.1 / 51.6 | +10 |
  | Trap Masters Cup | 55-65 | 52.1 / 55.2 | +2 to +5 |
  | Small Engines Invitational | 50-62 | 39.6 / 42.7 | +10 |
  | Synchro Dawn Classic | 47-58 | 41.7 / 54.7 | in band (48.2) |
  | Xyz and Link Regionals | 44-55 | 34.9 / 38.5 | +8 |
  | Link Age Open | 40-52 | 32.8 / 29.2 | +10 |

- **No wall:** every match's gate mean (warriors and zombies, seeds 1 and 2) is at least **35%**. A circuit's final may
  go to **30%**: it's the boss, and it has the boss music.
- **Why lower at the Intermediate end than before:** the gate starters are unedited, and the stand-in plays them well.
  By Synchro Dawn a real player has had about 25 matches of DP (roughly 25,000-30,000) and owns Recipe Book decks and
  packs, so a row of 40-50% with the unedited starter is about 50-60% for them.
- **What a player would feel:** the Beginner band is easy (it stays as it is: its skills aren't part of this); the
  Novice band costs a loss every two or three matches; by Link Age Open about every other match goes to a rematch
  unless the deck was improved. No match should need more than about three tries.
- **Gate:** the mean of Warrior's Charge and Undead Legion, unedited, seeds 1 and 2, 24 duels a match (192 duels a row,
  about ±3.6 points). Legendary Dragons (`classic`) is measured once at the end and reported: it stays the hardest start,
  which is why the ribbon goes to Warrior's Charge.

## 2. The change set, seat by seat

Six swaps, plus one more that is only applied if Link Age Open is still short. Every swap keeps the match's position and
key, and adds a `was:` line. None needs a new character, a new deck list or art. The owner allowed decks outside a
character's own archetype; each pick below still fits who the character is.

| # | Circuit, match | Sits there now (Elo; gate w / z, s1 / s2) | First choice (Elo) | Why it still fits | Fallback |
|---|---|---|---|---|---|
| 1 | Underdog Open, 2 (`4.1`) | Isamu Tokiwa, of the Gateway: six-samurai (1,491; 29/25, 25/20) | warriors (1,418) | A disciplined swordsman with a Warrior deck: Gearfried, Marauding Captain, Command Knight. He keeps Six Samurai in the Novice Tag Cup, the Library and the final | dragunity-legion (1,433): dragon-riding knights, still a warrior's deck |
| 2 | Trap Masters Cup, 2 (`5.1`) | Emperor Ostrand, of the True Domain: domain-monarch (1,202; 20/16, 20/20) | stall-burn (1,367) | An emperor behind his walls; a trap wall is what the "Trap Masters Cup" promises. Its starter rows were 62-75% (Town Tournament) | classic (1,276): Lord of D. commands the dragons, a royal deck |
| 3 | Small Engines Invitational, 1 (`6.0`) | Juniper Pell, of the Wandering Flock: floowandereeze (1,387; 29/12, 41/45) | dragunity-legion (1,433) | A birdwatcher with a flock of winged beasts and the dragons they ride | lost-sanctuary (1,234): the sky's angels and Sanctuary in the Sky |
| 4 | Small Engines Invitational, 2 (`6.1`) | Huginn, the Runic Raven: runick-spright (1,206; 29/16, 25/29) | blackwing (1,430) | Huginn is Odin's raven; Blackwings are a flock of crows and ravens. Measured 37-62% against the starters in Synchro Dawn | spellcasters-command (1,399): rune magic as spellcasting |
| 5 | Xyz and Link Regionals, 3 (`8.2`) | Unit QL-1, the Scale Engine: qliphort (1,225; 29/20, 25/20) | gadgets (1,480) | An android with a Machine deck that runs like clockwork | machine-re-volt (1,435): Cyber Dragon machines (measured 37-62% in Trap Masters) |
| 6 | Link Age Open, 2 (`9.1`) | Ace Raye, the Sky Striker: sky-striker (1,397; 25/29, 20/25) | mekk-knight (1,522), `decks/structure/` | A pilot in a mech: Mekk-Knights are giant machine knights, and the deck is from 2018, Link Age Open's own years | machine-re-volt (1,435) |
| 7 (only if Link Age < 40 after 6) | Link Age Open, 1 (`9.0`) | Ozzie Quintero, the Clockmaker's Apprentice: pendulum-magician (1,371; 25/41, 20/37) | spellcasters-command (1,399) | His deck is magicians; Spellcaster's Command is Dark Magician and friends | performapal (1,412): Pendulum showmen, Ringmaster Pepe's deck |

**Not changed, on purpose:**
- **The finals** (Nerio's Burning Abyss, Ione's Altergeist, Chef Praline's Yummy, Ash Hollow's Infernity, Coach Gouki,
  White Rabbit Maliss) stay: their `finalline:` lines are written for them, and a final may be the hardest match.
  Gouki (25-33) and Maliss (8-33) sit near or under the final's 30% floor: if the circuit lands in band without touching
  them, they stay; see the protocol.
- **Synchro Dawn Classic** is in band (48.2).
- **Re-ordering** inside a circuit: none. It moves match keys (old saves) and changes which character is the final.
- **Dino Rabbit** (33-41): with #5 the circuit should land in band; a Dinosaur's Rage swap would break Bunny Rex's lines
  (they name Laggia), as round 2 found.

**What it should give** (a rough guess from the measured rows of the new decks where they exist): Underdog Open about
58-62, Trap Masters Cup about 62, Small Engines about 50-54, Xyz and Link about 48, Link Age about 40-44 (with #7 about
45). Elo means barely move (the swaps aren't chosen by Elo), so the stars on these rows stay about where they are.

## 3. What each change costs elsewhere

### Lines that need the owner's approval

Each character's lines show wherever they play, including the places they keep their old deck, so each rewrite is
deck-neutral. Every proposed line is under 100 characters and all-ages.

| Character (profile) | Line | Now | Proposed |
|---|---|---|---|
| Isamu Tokiwa (`six-samurai`) | `intro:` | "Six blades, one will. Draw yours." | "Steel, discipline, one will. Draw yours." |
| | `win:` | "The Gateway stays open, and you fall." | "My clan stands, and you fall." |
| | `partner:` | "Six blades, and now one more. Walk beside me." | keep (a partner line; it reads as his clan) |
| Emperor Ostrand (`domain-monarch`) | `intro:` | "My domain. No Extra Deck allowed." | "My domain, my rules. Nothing crosses it." |
| | `win:` | "Bow before the Monarchs." | "Bow before the throne." |
| | `lose:` | "A Monarch knows when to yield. Rarely." | "An emperor knows when to yield. Rarely." |
| Juniper Pell (`floowandereeze`) | `intro:` | "Normal Summon, Normal Summon, Normal Summon... and no Graveyard for you." | "Every flock has a leader. Mine has wings and a lance." |
| | `win:` | "The birds fly home, and your Graveyard stays empty." | "The flock flies home. You stay grounded." |
| Huginn (`runick-spright`) | `intro:` | "Runes in my hand, banishing in my heart." | "Ravens see everything. Including your next move." |
| | `win:` | "Your Deck ran out before my runes did." | "The ravens return. Your turn is over." |
| Unit QL-1 (`qliphort`) | `intro:` | "Initializing Pendulum Scales. Resistance is inefficient." | "Initializing. Resistance is inefficient." |
| | `win:` | "Apoqliphort Towers online. Duel terminated." | "Duel terminated. Efficiency: optimal." |
| Ace Raye (`sky-striker`) | `intro:` | "One girl, three spells, a lot of firepower." | "Pilot's seat, full throttle. Try to keep up." |
| | `win:` | "Engage, Widow Anchor, mission complete." | "Target down. Mission complete." |
| Ozzie Quintero (`pendulum-magician`), only with #7 | `intro:` | "Wind the scales and let the magicians swing." | "Wind the clock and let the magicians work." |

The data agent also checks each changed character's duel lines (`summon:`, `strike:`, `hit:`, `danger:`) and their
`story.txt` scenes (grep the id) for an archetype name; Isamu's scenes (lines 127-129, 612-620) belong to the Library
and his gift, where he still plays Six Samurai, and stay.

### Circuit blurbs (data, no approval needed beyond this plan)

- Underdog Open: `Frog FTK, Warriors, Spellbooks and Burning Abyss: underdogs from 2005 to 2014.`
- Trap Masters Cup: `Machine Re-Volt, a stall wall, Zoodiac and Altergeist: machines and trap walls from 2005 to 2018.`
- Small Engines Invitational: `Dragunity, Blackwings, Purrely and Yummy: small monsters and the birds that carry them.`
- Xyz and Link Regionals: `Dino Rabbit, Inzektor, Gadgets and Gouki: decks from 2005 to 2018.`
- Link Age Open: `Pendulum Magicians, Mekk-Knights, Salamangreat and Maliss: decks from 2016 to 2025.` (with #7:
  `Spellcasters, Mekk-Knights, Salamangreat and Maliss: decks from 2005 to 2025.`)

The circuit names all still hold ("Small Engines": small monsters; "Xyz and Link": Dino Rabbit and Gouki).

### `featured.txt`

Each changed circuit's shelf must still pass the harness (every match deck named by a `set:` or `soon:` line; `set:`
released by the circuit's era: Novice 2011, Intermediate 2015). Dragunity and Blackwing cards are printed by 2011;
Mekk-Knight cards are 2017-2018, after the Intermediate era of 2015, so Ace Raye's deck can only be named on `soon:` lines
(sets released by the Advanced era, 2019). `--campaign-test` checks it.

### Recipe Book and old saves

- Each swap adds `was: <N> <character> <old deck>` under its circuit, as Trap Masters Cup already has.
- **Trap: the migration gate.** `Circuits.Migrate` records the former recipes once per save and then sets
  `climb:2026-11-20`. A save that already ran the committed build (4e1b492) has that key and would skip the new `was:`
  lines, losing those old recipes. **Part E must change the gate to one mark per `was:` line** (for example
  `formerseen:<circuit>.<match>:<owner>|<deck>`), so a later swap migrates too, and add the old-save test for a save that
  already holds `climb:2026-11-20`. Half a day.
- **Old recipes after the swaps, and where they can still be won:**

  | Old recipe | Still met | For a new player |
  |---|---|---|
  | Isamu's Six Samurai | Novice Tag Cup, the Library, the final | yes, in the Novice Tag Cup |
  | Ostrand's Domain Monarch | the Library (Nekroz & PePe) | post-game only |
  | Juniper's Floowandereeze | the Library (Tearlaments) | post-game only |
  | Huginn's Runick Spright | **nowhere** | **lost**: only old saves keep it (`formerrecipe:`) |
  | Unit QL-1's Qliphort | the Library (HAT & Shaddoll) | post-game only |
  | Ace Raye's Sky Striker | the Intermediate Tag Cup, Rhea's partner, the Library (Link Dawn) | yes |
  | Ozzie's Pendulum Magician (#7) | **nowhere** | **lost** |

  The Recipe Book goes from 82 recipes to 82 + 6 new − 2 lost for a new save (Runick Spright and, with #7, Pendulum
  Magician). Owner decision 2.
- An old save that beat a swapped match keeps its old recipe and counts the new one as unlocked, as the spec decided.

### Shop prices and `--arbitrage`

A recipe pack is priced by the band where its deck is first met (`RecipeShop.PackPriceFor`, the first recipe with that
deck). Blackwing moves from Intermediate (600) to Novice; Dragunity Legion and Mekk-Knight (structure decks today) gain
recipe packs. Cheaper recipe packs can't make DP (the best buy-and-sell return is 50%, `PROOF-2026-11-01.md`), but the
prices move, so **`--arbitrage` must show 0 DP-positive** after the data lands, with `--campaign-test` and
`--deckbuilding-test`.

### Stars

The stars read Elo, and Elo doesn't predict these matchups (above). Stall Burn (2 stars) replaces Domain Monarch (1 star)
while the match gets much easier; Gadgets (2) replaces Qliphort (1). The stars will tell a player little in these two
bands. Owner decision 3.

### Where the same characters play at 1.0 and haven't been re-measured

- **Before this ships:** the **Novice Tag Cup** (all six duelists now at 1.0; it opens right after Underdog Open, on the
  main path, and tag was already the weakest mode) and **Nocturne's tag duel** (altergeist; opens after the Novice band).
  Try `--wall-study --only "Novice Tag Cup"` and `--only "nocturne"` from the same root; if the harness doesn't treat
  them as walls, report that and leave them to the tuning pass.
- **Can wait for the tuning plan's Library step** (TUNING-PLAN-2026-11-06.md step 4): the Intermediate Tag Cup, Rhea's
  tag (sky-striker), the 11 Library cups with these characters and the final's Synchro Peak round. Their advice reads
  easier than the game until then.

## 4. Measuring protocol (for the data agent)

Same harness and root as round 2 (`build/bin/snap-climb2`, `build/climb/root2/`), `DOTNET_PROCESSOR_COUNT=6`, raw output
in `build/climb/runs3/`. A **circuit run** is warriors and zombies, seeds 1 and 2, `--games 24`: 4 runs, about 6 minutes.
Text changes (lines, blurbs, `featured.txt`) don't change a duel: make them after the measuring.

1. **Apply #1-#6** (the `match:` lines and `was:` lines; the profiles' `decks:` lines gain the new deck after their own).
2. **Measure the five changed circuits** (Underdog, Trap Masters, Small Engines, Xyz and Link, Link Age): 20 runs,
   about 30 min. Synchro Dawn and the Beginner band are reused as measured.
3. **Per seat:** a swapped seat whose gate mean is under 35% (30% for a final) gets its fallback; re-run that circuit
   (6 min). If the fallback is also under, the seat goes back to its original deck and the circuit is reported short.
4. **Per circuit:** in band, or at most 5 points over the ceiling, is accepted (easier is the safe side in the first
   bands). More than 5 over: try the seat's fallback where it should be harder (Ostrand → classic is not; there, accept
   and report). Under the floor with every seat over 35%: report; don't add swaps beyond this plan.
5. **Link Age Open:** if under 40 after steps 2-4, apply #7 and re-run (6 min); its fallback if #7's seat is under 35%.
6. **Order check:** the circuit means should fall from Underdog Open to Link Age Open. One step up of up to 5 points is
   accepted (within the error); more is reported.
7. **Classic once** (seed 1, the five changed circuits): 5 runs, about 7 min. Reported, not gated.
8. **Then the text and the checks:** lines (after the owner's approval), blurbs, `featured.txt`; `--campaign-test`,
   `--deckbuilding-test`, `--texts-check`, `--arbitrage` (0 DP-positive), and the two tag measurements in section 3.
9. **Stop** when all six circuits are accepted or reported. Write the table (circuit, seat, deck, w / z per seed, mean,
   verdict) into the spec's "As measured", round 3.

**Machine time:** about 30 min for step 2, up to 40 min of fallbacks (at most six circuit re-runs), 7 min for classic,
about 15 min of checks, and the tag measurements (time the first; drop them if they would take the total past 2.5 h).
About 1.5-2 hours in all.

## Owner decisions (recommendation first)

1. **Characters playing decks outside their archetype, and the 13 rewritten lines** (section 3).
   - Recommended: the six first choices and the deck-neutral lines as proposed. Each character keeps their look, name
     and story, and keeps their old deck wherever else they play.
   - *Or* keep any character you want on their own deck and accept that circuit out of band (the walls stay).
2. **Two recipes a new player can no longer win: Huginn's Runick Spright, and Ozzie's Pendulum Magician if #7 is needed.**
   - Recommended: accept for now; they come back if a later build gives them a seat (a Library local or a Tag Cup pair).
   - *Or* keep Huginn on Runick Spright and swap Small Engines' Purrely seat instead (Pip's Purrely is 33-70%, so the
     circuit may stay short).
3. **Stars in the early bands.** Recommended: keep the Elo stars for this build and note that they don't predict
   starter matchups; a later build could base each match's stars on its measured row. *Or* hide stars on Novice and
   Intermediate rows until then.
