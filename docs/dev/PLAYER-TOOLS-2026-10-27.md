# Player tools for the late walls (2026-10-27)

The last walls in the campaign were the Expert and Master Tag Cups (1-4 and 0-6 for the QA stand-in) and Seraph
(0-4). The rule stands: the CPUs keep playing as they do; the player gets the tools to get past them.

## What the walls were made of

- **The hardest fights had the fewest tools.** Circuit matches had a deck picker and loaners; Tag Cups and guest duels
  always used your current deck.
- **The partner decided most tag duels, and the stand-in picked a weak one.** It picked by bond, so almost always Old
  Gus with Zombies (rated 41st of 78 decks). The opponents play the strongest decks in the game (Branded Despia,
  Tearlaments, Eldlich and Mitsurugi are 1st, 2nd, 4th and 7th).

## Measuring first: the wall study

`--wall-study` plays each wall as the campaign sets it up: the CPUs exactly as the game makes them, and a skilled
stand-in for you (the QA player's style). Only the player's choices change:
- **your deck:** the Mekk-Knight Evolved starter, or the strongest loaners unlocked by then;
- **your partner:** Gus with Zombies, the two strongest partners you'd have beaten by then, or Kit with a loaner;
- **who goes first.**

16 duels per option on the same seeds, so the differences are the options' (a single figure is good to about ±12
points). Raw: `build/wall-study-before.txt`.

| Wall | Your deck + Gus (what the stand-in did) | Best with the tools |
|------|------------------------------------------|---------------------|
| Expert Tag Cup 1 (Shaddoll + Danger! Dark World) | 6% | 87%: your deck, Kit with Danger! Dark World |
| Expert Tag Cup 2 (Drytron + Tenpai) | 37% | 87%: Danger! Dark World loaner, Kit with Kozmo |
| Expert Tag Cup 3 (Fire King Snake-Eye + Angelechy) | 12% | 56%: your deck, Kit with Danger! Dark World |
| Master Tag Cup 1 (Eldlich + Bystial Dragon Link) | 25% | 62%: Fire King Snake-Eye loaner, Garunix with Fire King Snake-Eye |
| Master Tag Cup 2 (Tearlaments + Kashtira) | 25% | 50%: your deck, Kit with Eldlich |
| Master Tag Cup 3 (Mitsurugi + Branded Despia) | 0% | 31%: Fire King Snake-Eye loaner, Garunix with Fire King Snake-Eye |
| Seraph (Light and Darkness) | 31% (no partner) | 31%: your deck is already the best choice |
| Seraph in tag (+ Dragon Rulers) | 50% | 56%: your deck, Kit with HAT |

What it says:
- In a tag duel the partner matters most: the same deck goes from 6% to 87% with a better partner.
- Kit, who plays at full strength, with a strong recipe is the most reliable choice at the Expert Tag Cup.
- The turn order changes little at these walls. No deck at them has a clear first-or-second edge.
- Seraph has no tool that helps more than your own deck: about one win in three tries, a fair final boss.

(Two rows of the study's first run partnered you with a duelist you were facing in that match. The game allowed that
too; it's fixed below, and those rows are left out of the table.)

## The tools

1. **Deck picker in Tag Cups and guest duels.** The same arrows as before a circuit match: your decks, then any recipe
   you've unlocked as a loaner, made legal for that fight's ban list. If your own deck isn't legal under a guest's
   rules, the screen starts on a loaner and says why.
2. **Kit can bring a loaner.** Kit's deck choices are your other decks, then every recipe you've unlocked.
3. **Half DP with any loaner on your team.** That covers your deck or Kit's, so lending the loaner to Kit doesn't keep
   full DP. Prizes are always paid in full.
4. **Scout** on every campaign versus screen. It shows a report on each opposing deck: its plan, the cards to watch, what
   beats it, and the turn to want. The reports are written for the 16 decks at the late walls (`campaign/scouting.txt`;
   the campaign test checks that every card they name is in that deck). Any other deck gets one put together from its
   playbook's bosses and its most-played monsters.
5. **Stuck?** After three losses in a row at a match (now kept in the save), the versus screen opens the advice by
   itself. In a tag duel it suggests Kit with your strongest recipe, or the partners with the strongest decks; then a
   stronger deck to borrow, the turn to take when a report says so, and what to side in.
6. **A partner can't be on the other team.** The Tag Cup refuses a partner who is one of that match's opponents.

## In play: the QA run with the tools

The QA stand-in played on (log: `build/qa-tools/qa-log.txt`) from a late-campaign save (Expert and Master Tag Cups open, Seraph unbeaten) with `-qatools`.
It gets six tries at each Tag Cup match or guest instead of two, and once the Stuck? advice opens it does what the
advice says.

70 duels, 1 issue (the board kept animating for 20 s in a 61-turn duel). Before the tools, the same stand-in went 1-4
at the Expert Tag Cup and 0-6 at the Master Tag Cup and cleared neither.

| Match | Without the advice (3 tries) | Following it | Result |
|-------|------------------------------|--------------|--------|
| Expert Tag Cup 1 | 0-3 | 1-2 | won on the 6th try |
| Expert Tag Cup 2 | 0-3 | 1-0 | won on the 4th try |
| Expert Tag Cup 3 | 0-3 | 1-2 | won on the 6th try: **Expert Tag Cup cleared** |
| Master Tag Cup 1 | 0-3 | 1-1 | won on the 5th try |
| Master Tag Cup 2 | 0-3 | 1-1 | won on the 5th try |
| Master Tag Cup 3 | 0-3 | 0-3 | not won in six tries |
| Seraph | 0-3 | 0-3 | not won in six tries |
| Rhea, alone and in tag | | | won on the 2nd and 3rd tries |

Following the advice meant Kit with a Tearlaments loaner (the strongest recipe this save had) and an Eldlich loaner for
you. The Stuck? advice opened every time it should have.

Two walls remain:
- **Master Tag Cup 3** (Mitsurugi + Branded Despia, two of the three strongest decks). The study's best option there
  wins about one in three.
- **Seraph** alone. The advice suggested Tearlaments because it's rated strongest overall, but against Seraph nothing
  measured beats your own deck (31%).

## Advice from measured matchups

The Stuck? advice now starts with what did best when that very match was measured. `--wall-study --write` saves every
option's results to `campaign/wall-advice.txt`, and the game picks the best option you actually have:
- your own deck, or a loaner you've unlocked;
- a partner you've beaten who isn't in that match, with a deck they can bring.

It says how often that option won, the next best, and the turn to take when one turn did 25 points better. A match
that wasn't measured falls back to the strongest decks by rating.

The two remaining walls were measured again with every recipe in the campaign:
- **Master Tag Cup 3:** best is a Branded Despia loaner with Seer Kelbek bringing Ishizu Tearlaments (68%). Next is an
  Ishizu Tearlaments loaner with Kit bringing Branded Despia (62%). The best before was 31%.
- **Seraph:** best is a Mitsurugi loaner (70%), then Light and Darkness (62%). Your own deck wins 33%, and the
  Tearlaments the old advice suggested wins 45%. Going second is about 25 points better with every deck.

**Re-tested in QA** from the save where the first run stopped (log: `build/qa-tools/qa-log-measured.txt`). Following the
measured advice:
- **Master Tag Cup 3:** won on the first try, which cleared the Master Tag Cup.
- **Seraph:** won on the second try.
- The run then went on into New Game+ with 0 issues.

Every late wall has now fallen to the tools, with the CPUs unchanged.

## Found on the way

- **A CPU attack loop.** A CPU that called off an attack (no target it was offered was worth it) could have its planner
  pick the same attack again, forever, in the same Battle Phase. It showed as a replay test that never finished
  (seed 1015). The planner now leaves called-off attackers out, as the CPU's usual play already did.
