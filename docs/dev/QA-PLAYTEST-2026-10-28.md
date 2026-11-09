# QA playtest: the difficulty curve with the October 2026 meta (2026-10-28)

The whole campaign from a fresh save on the build with everything from 2026-10-27 and 10-28: the coin toss, the player
tools (loaners, measured Stuck? advice, six tries per Tag Cup match or guest), stronger late CPUs (Branded, K9), Gus's
guided first duel, and the October 2026 meta (two new circuits, a new Tag Cup, eleven new decks and duelists; see
[decks/OCTOBER-2026-META.md](decks/OCTOBER-2026-META.md)). Played by the QA stand-in through the real screens with
`-qaloaners -qatools`, sound muted. Log: `build/qa-curve/out/qa-log.txt`.

## Results

**229 duels, 134 won, 95 lost, 0 issues**, in 3 hours 20 minutes: Gus's first duel (passed on turn 3), every circuit
through the Tier 0 Gauntlet, then New Game+ to its fourth circuit.

| Band | This run | 2026-10-27 run (no tools, no new content) |
|------|----------|----------------------------------------------|
| Beginner | 12-1 | 12-1 |
| Novice | 12-0 | 12-0 |
| Intermediate | 12-0 | 12-0 |
| Advanced | 12-2 | 12-0 |
| Expert (4 circuits now) | 16-9 | 12-8 (3 circuits) |
| Master (4 circuits now) | 15-34 | 11-20 (3 circuits) |
| Tier 0 Gauntlet | 4-9 | 4-3 |
| Tag Cups | 17-31 | 13-13 |
| Guests | 8-6, all beaten (Seraph both ways) | 6-8, Seraph not beaten |
| DP at the Gauntlet's end | 248k | 178k |

## The new content

| Where | Result | Hardest match |
|-------|--------|---------------|
| Expert Series: Fall Format | 4-3 | even: Artmage, Dracotail and Mermail Atlantean 1-1 each |
| Fall Format Tag Cup | 3-4 | Patron Orvel & Maestra Vellum (Power Patron + Artmage) 1-4 |
| Master League: October 2026 | 4-9 | Jolt's Blitzclique 1-5, Warden Hask's Sacred Beast 1-3 |

The new circuits sit inside their bands. Fall Format is a step up from the first two Expert circuits and level with The
New Wave (4-5); October 2026 is level with the other Master circuits (Link Wars 4-9, Graveyard Kings 4-7, Houston 3-9).
Blitzclique plays far stronger as a late CPU (skill 1.0 with the planner) than its balanced-bot Elo of 1624 says: its
five wins came on turns 2 to 5, three of them without taking damage.

## The curve

- **No Novice dip:** Beginner to Advanced went 60-3.
- **The jump is at Expert**, as intended, and the Master band is the hard part: 15-34, about one win in three, with every
  Master circuit and the Gauntlet needing several tries. The player tools carried the stand-in through all of them.
- **Tag Cups are the walls now.** The Expert Tag Cup went 1-12 and the Master Tag Cup 1-15, both left uncleared after
  six tries per match. In each, two of the three matches went 0-6, while the measured Stuck? advice says 87% (Expert
  match 1: Kit with the Danger! Dark World loaner, you with your own deck) and 56% (Expert match 3). The stand-in took the
  advised option every time and still lost. Last week's late-save run with the same tools cleared both cups. Something
  differs between the wall study and real play, perhaps the player's Chain Auto missing hand-trap windows that the
  study's bot always takes. That's worth measuring before more tuning.
  **Measured and fixed** ([TAG-CUP-GAP-2026-10-28.md](TAG-CUP-GAP-2026-10-28.md)): the stand-in played tag duels
  without its planner, went second in every one (the toss screen's one-on-one deck tip), and some advice was stale. A
  new run cleared the Expert Tag Cup (3-4); the Master Tag Cup went 2-12.
- **DP inflation is up:** 248k by the end of the Gauntlet, from 178k, from the extra circuits' prizes and the extra
  tries the tools allow.

## Other findings

- **Loaner decks skip the circuit's ban list:** in Master League: October 2026 the stand-in borrowed the Tearlaments
  loaner, a September 2026 list that breaks the October 2026 list. The player's own deck is checked; loaners aren't.
  **Checked 2026-10-29:** loaners do follow each circuit's list (`--loaner-check` builds all 923 recipe and circuit
  pairs). The real problem was different: 67 of them fell under 40 cards once the list took cards out (Eldlich 36-37,
  Kashtira 34), because a loaner could only refill from its own recipe. Now they top up from generic staples (hand
  traps and removal, within the list): all 923 are legal.
- **Four new decks rate below their tournament results** (Artmage, Power Patron, Magistus Fairy Tail, Radiant Typhoon).
  Each needs a playbook for its plan. Done: with playbooks they rate 1538 to 1683
  ([decks/PLAYBOOKS-OCTOBER-2026.md](decks/PLAYBOOKS-OCTOBER-2026.md)), so the Fall Format Tag Cup is harder than
  this run found it.
