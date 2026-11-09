# QA playtest: 10 campaign games with Mekk-Knight Invoked (2026-10-25)

A fresh campaign save (in a test folder; your own save untouched) with the new **Mekk-Knight Invoked** starter, played
the way a player would: the starter screen, the opening story read line by line, then each next match through the
circuit screen, versus screen, duel, results and Continue. Your decisions were made by a skilled stand-in (the
strongest planning character's style, with the new Mekk-Knight playbook) and given **through the prompt panel the
way clicks are**: all 922 decisions matched a button; none had to be answered behind the UI's back.

Watched automatically during every duel: errors and exceptions, the duel waiting on you with nothing shown to
answer, animations that never finish, stalls, attack arrows left at a Main Phase decision, rejected answers,
overlong duels, and the save matching the game after each result. Afterwards every replay went through the blunder
audit (both players), and I looked through the screenshots (each turn, each screen; 182 of them).

**Speed.** Game 1 at the default 4 events/s (full-length animations, the speed most players use); games 2-10 at
16/s, which still plays every animation, just shorter. Animation bugs (like the frozen board earlier today) only
show with animations on, so nothing was run at 60/s, where they're switched off.

## Fix status (later the same day)

| Finding | Status |
|---------|--------|
| CPUs (and your stand-in) using removal on their own cards | **Fixed.** Removal that could hit either side isn't used when the opponent has nothing of that kind; column removal (Red Moon, Yellow Star) needs an opponent's card in its own column; "up to 2" removal stops at the opponent's cards; "Spells/Traps" wording counts as Spell/Trap removal. 200 CPU duels: 45 own Spell/Traps destroyed in 37 games, now 4 in 3 (3 are Wavering Eyes, which destroys every Pendulum Zone card by design). |
| 1. Chain-link number left on the board | **Fixed.** A badge up after its chain is over is cleared from the board's state (it cleared 7 in a 10-game rerun). One cause found: changing to 60/s during a chain. Replays at 16/s showed none; my first reproduction was the detector's own false positive (fixed too). |
| 2. Winning attack's arrow left up | **Fixed.** Hidden when the result is announced. |
| 3. Chain box over the Banished pile | **Fixed.** The box and status line moved to the free slot under the Banished pile, beside the Deck. |
| 4. "First win against them" | **Fixed.** Now "First win in this match". |
| 5. Deck file names | **Fixed.** Versus, circuit and tag cup screens show "Stall Burn", "Zombies"... (the deck's title where it has one). |
| 6. Story over a live duel | **Fixed.** A story scene holds the duel until it's read, and the board's keys (Space, P, B, E) stay off while a story, the menu or Settings is open. |
| Ryo's Burst Breath that can't reach your monster | **Fixed.** "Destroy every monster with DEF/ATK up to X" is checked against what it can reach (X: the printed number, else our strongest monster's ATK), and not used when it hits nothing of the opponent's or costs us more. |
| Opponents equipping boosts to your monster (Vera's Axe of Despair) | **Fixed.** An Equip that only helps ("the equipped monster gains") goes on the bot's own monster, and isn't played without one. 200 CPU duels: 4 times in 2 games before, 0 after. |
| Trading Black Luster Soldier (or any boss) for a weaker Link | **Fixed.** No Extra Deck summon whose cheapest possible materials already include a monster worth more than it; a deck's bosses (playbook) are valued well above their ATK, unless stuck: targeted by a lasting lock like Nightmare Wheel (read from the core's target and equip data), not Swords of Revealing Light. 200 CPU duels: "used a stronger monster as material" 78 times in 44 games before, 18 in 17 after (the rest are combo steps like Fiendsmith Requiem). |
| Mekk-Knight Invoked's boss line | **Done.** Mechaba, then Black Luster Soldier from three spare Mekk-Knights (a Level 7+ one for its protection), then True King (Monstrosity: Idlee + Mardark, Mechaba kept), then Avramax from two spare Extra Deck monsters; Morning Star only when there aren't three bodies. See [DECK-GUIDE-MEKK-KNIGHT-INVOKED.md](DECK-GUIDE-MEKK-KNIGHT-INVOKED.md). With the playbook vs without, skilled player, 100 duels each: Snake-Eye 26% to 36%, Fire King Snake-Eye 22% to 22%, Yubel 39% to 41%, Gadgets 88% to 82%. |
| Hand traps Set as monsters (Ash Blossom) | **Fixed.** A hand trap isn't Normal Summoned or Set. |
| Attacking into a bigger monster when Avramax forces the target | **Fixed.** When the target it meant isn't offered and nothing left is worth it, the bot calls the attack off (and doesn't retry it that turn). |
| QA: two "Select the card(s) to discard" answers matched no button | **Not a game bug.** The stand-in listed the same cards in another order (3, 2 against the buttons' 2, 3); the UI makes that selection fine. The QA player now compares selections regardless of order: 0 unmatched in the next run. |
| Chain badge root cause | **Found and fixed.** The only real leak: at 60/s the board skips animating, including the chain's end that removes its link numbers; they're now removed then too. Traced over 200 badges in live play at 4/s and 16/s: every one was cleared by its chain's end. The safety net now leaves badges a chain's end has claimed (it used to cut long chains' fade short). |
| Glossary tooltips with a controller | **Done.** The View button (rebindable: Card terms) explains the next blue term in the card detail's text, each press the next one; a stick can't easily land on one word. |

The 10-game rerun with the fixes: 9 won, 1 lost, 878 decisions through the prompt panel, no issues.

## Results

| # | Circuit | Opponent (deck) | Result | Turns | Time |
|---|---------|-----------------|--------|-------|------|
| 1 | Beginner Circuit 1 | Ryo (classic) | Won | 7 | 1.9 min (4/s) |
| 2 | Beginner Circuit 2 | Captain Brask (warriors) | Won | 7 | 0.4 min |
| 3 | Beginner Circuit 3 | Ryo (warriors) | Won | 9 | 0.7 min |
| 4 | Rookie Cup 1 | Captain Brask (gadgets) | Won | 9 | 0.5 min |
| 5 | Rookie Cup 2 | Old Gus (classic) | Won | 15 | 0.9 min |
| 6 | Rookie Cup 3 | Tink (gadgets) | **Lost** | 14 | 0.8 min |
| 7 | Rookie Cup 3 (retry) | Tink (gadgets) | Won | 9 | 0.8 min |
| 8 | Town Tournament 1 | Old Gus (zombies) | Won | 13 | 1.6 min |
| 9 | Town Tournament 2 | Tink (stall-burn) | Won | 15 | 1.2 min |
| 10 | Town Tournament 3 | Vera (warriors) | Won | 15 | 0.9 min |

3 circuits cleared, Level 6, 8,275 DP, 3 stories read. The automatic watchers found **no** errors, hangs, stuck
animations, stalls, left-over arrows at a decision, rejected answers or save mismatches. The bugs below came from the
screenshots, the replays and the audit.

## Bugs

### 1. A chain-link number stays on the board after its chain (medium: visual)

A gold "1" is left in the corner of an empty zone where an activated card used to be (your Raigeki's zone in game 6;
Tink's Mirror Force's zone when replaying it; Dimensional Prison's in an earlier check). It stays until the next duel.

- **Reproduce:** replay game 6 at 16/s (`-qareplay "<replay>"`): a badge is left on turn 10.
- **Cause (part found):** the badge is removed only by the animation for the chain's end. At 60/s animations are
  skipped entirely, so changing to 60/s during a chain always leaves it. At 16/s it happens too; the animation
  system swallows "object was destroyed" errors silently (`BoardFx.Run`), which can cut the clean-up short without
  anything in the log.
- **Fix:** clear chain badges whenever the chain panel is empty (from the board's state, not an animation), and
  log the swallowed errors instead of hiding them.

*(Screenshot chain-badge-left-over removed 2026-11-08: it showed official card art.)*

### 2. The winning attack's arrow stays after the duel ends (medium: visual)

The arrow and its readout ("2000 damage") stay up under the "You win!" banner and are still there behind the story
scene that follows: the duel ends during the attack, so "attack over" never comes. **Fix:** hide the arrow when the
result is announced (and when a new duel starts).

*(Screenshot arrow-after-win removed 2026-11-08: it showed official card art.)*

### 3. The Chain: AUTO box covers the Banished pile (low: layout)

On the campaign board the Chain setting box sits on top of your Banished pile, and "Opponent is playing..." runs
across the pile's ATK/DEF label (every game; also in your own screenshot from earlier today). **Fix:** move the box
(and the status line) clear of the pile.

*(Screenshot chain-box-over-banished removed 2026-11-08: it showed official card art.)*

### 4. "First win against them" is paid per match, not per opponent (low: wording)

Ryo is in Beginner Circuit matches 1 and 3; both wins paid "First win against them +200", and the same screen says
"Against Ryo: 2 wins". The bonus is for the first win of each **match**. **Fix:** call it "First win in this match"
(or pay it once per opponent, if that's the intent).

### 5. Opponent decks show their file names (low: polish)

The versus and circuit screens say "zombies", "gadgets", "classic", "stall-burn" where your deck says "Mekk-Knight
Invoked". **Fix:** show the deck's title (Undead Legion, Legendary Dragons...).

*(Screenshot versus-deck-ids removed 2026-11-08: it showed official card art.)*

### 6. A story scene can sit over a live duel (low)

In my first attempt the automation didn't dismiss the opening story, and the whole first duel played underneath it
with nothing stopping it. A player can't normally click Duel through the story, so it's unlikely in play, but the
duel doesn't pause for a story, and **Space** both advances a story and (hold) checks the field. Also, the story that
follows a results screen plays over the finished duel's board (with bug 2's arrow) rather than the campaign screen.

## CPU opponent blunders

**Destroying their own Spell/Trap cards (the "MST on their own card" kind): about 1 game in 5.** In 200 CPU-vs-CPU
duels (all characters, their own decks), a removal card that could hit either side destroyed its own player's card
45 times in 37 games: Mystical Space Typhoon on their own Fiendish Chain, Twin Twisters on their own Union Hangar,
Heavy Storm Duster on their own set Solemn Strike, Breaker the Magical Warrior on their own set Crush Card Virus,
Kozmo Sliprider on their own Bottomless Trap Hole, Dark Armed Dragon on their own Black Whirlwind, Artifact Ignition
on their own Wiretap. The bot's targeting for "a card on the field" removal doesn't keep to the opponent's cards.
(Cards that must take your own card, like Double Cyclone, aren't counted.)

**In these 10 games:**
- **Ryo, game 1, turn 4:** tributed Alexandrite Dragon (2000 ATK) for Burst Breath, which destroys monsters with DEF
  up to that ATK: your only monster, Invoked Mechaba, has 2100 DEF. It hit only his own side.
- The same "own card" targeting showed up on **your** side (the stand-in uses the same AI): see below.

Other audit categories were mostly cards doing what they're built to do (Fire King and Unchained Soul cards destroy
their own monsters on purpose; Performage Trick Clown pays LP). Three "self damage" hits on Ryo were really your
Trickstar Candina + Light Stage burning him for each Spell/Trap he activated: the audit pins the damage on the card
resolving, so those are its false positives, not his.

## Your stand-in's misplays (the same AI the CPUs use)

- **Mekk-Knight Red Moon destroyed your own Lib, the World Key Blademaster** (game 8): Red Moon destroys "1 face-up
  monster in this card's column" and the only one there was yours. Same root cause as the CPUs' own-card removal.
- **Set Ash Blossom & Joyous Spring as a monster** instead of keeping it in hand to negate.
- **Tributed Aleister to Set Mekk-Knight Green Horizon** face-down (game 1) instead of keeping it to Special Summon
  into a column.
- Often held two or three Mekk-Knights in hand when no column had room for them (not a bug; columns were full).

The playbook's big line worked in a real game: **Invocation into Invoked Mechaba, World Legacy Monstrosity calling
Knightmare Incarnation Idlee and World Legacy Guardragon Mardark, and True King of All Calamities** (game 9).

*(Screenshot true-king-line removed 2026-11-08: it showed official card art.)*

## Worked without a hitch

- Starter screen, opening story, the story after the Beginner Circuit (and its Lessons hint).
- Circuit map, versus screen, Duel!, results with DP, EXP, level-ups and unlocks, Continue back to the circuits.
- Saves: the saved DP matched the game after every duel.
- 922 decisions through the prompt panel: chains, targets, zones, attacks, positions, the phase changes.
- Losing a match and retrying it; three circuits opening in order; the Card Shop's year moving forward.
- No errors or warnings in the game's log for the whole run.

## Questions for you

- **Era-limited** was on (the default), yet the 2025 Mekk-Knight deck was accepted in circuits whose Card Shop sells
  only 2005-2010 sets: those circuits play house rules with no ban list, so the release check doesn't apply. Intended
  (the starter says "the early circuits will be easy")?
- **Difficulty:** 9 wins in 10 with this deck in the Beginner band, never shopping. As the starter promises, but the
  first circuits may be too easy for a modern starter.
- **Repeat opponents:** Ryo twice in the first circuit; Brask, Gus and Tink again in the next two. Intended?

## Not covered

The Card Shop, Card Trader, deck editor and Ban Passes (never needed so far), tag duels, Gus's lessons, the
controller, and real mouse movement (clicks went through the prompt panel, not screen positions; hover-only features
like the glossary tooltips and column guides weren't exercised). Circuits past the Town Tournament. **Part 2**
([QA-PLAYTEST-2026-10-26-PART2.md](QA-PLAYTEST-2026-10-26-PART2.md)) covers all of these except the controller and
real mouse movement.

## Running it again

- `"Duel Circuit" -mods <empty folder> -pics <your card pictures> -qaplay <out folder> -qagames 10`: the campaign
  played like this (log `qa-log.txt` and screenshots in the out folder; the log of this run is in `qa/2026-10-25/`).
- `"Duel Circuit" -qareplay <replay file> -qaout <folder>`: watches a replay and records left-over chain badges.
- `Ygo.Harness --audit-replays <folder>`: the blunder audit over every replay in a folder (both players).
- `Ygo.Harness --audit --games 200`: CPU-vs-CPU duels through the same audit (now with "destroyed own Spell/Trap").
