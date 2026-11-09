# QA playtest: the whole campaign after the 10-28 work (2026-10-29, overnight)

A fresh save played by the QA stand-in through the real screens (`-qaplay -qafull -qaloaners -qatools -qagames 230`,
muted), on a build with everything from 2026-10-28 and the night's fixes:
- the card reader and effect popups
- the OCG import shelf
- art styles
- the Tag Cup fixes (the stand-in plans in tag duels, no one-on-one toss tip in tag, re-measured advice)
- the Master Tag Cup match 3 advice
- the fixed Mekk-Knight Crusade list
- loaners topped up to 40

Log: `build/qa-night/out/qa-log.txt`. The run to compare with is [QA-PLAYTEST-2026-10-28.md](QA-PLAYTEST-2026-10-28.md).

## Results

**229 duels, 119 won, 110 lost, 1 issue (minor)**, in 3 h 18 min: every circuit through the Tier 0 Gauntlet, then New
Game+ to its second circuit. All nine app checks passed on this build first (campaign 98, controls 32, layout 24,
board 23, pile 20, order 10, tag 4, replay 11, menu 6).

| Band (first campaign only) | 2026-10-28 | Tonight |
|---|---|---|
| Expert circuits | 16-9 | 16-16 |
| Master circuits | 15-34 | 15-39 |
| Tier 0 Gauntlet | 4-9 | 4-16 |
| **Tag Cups** | **17-32, Expert and Master not cleared** | **18-20, all cleared** |
| Guests | 10-6 | 10-13 |
| DP at the Gauntlet's end | 248k | 269k |

### The Tag Cups

| Cup | 2026-10-28 | Tonight |
|---|---|---|
| Novice / Intermediate | 3-0 / 3-0 | 3-0 / 3-0 |
| Advanced | 3-0 | 3-3 |
| Expert | 1-12, not cleared | **3-4, cleared** |
| Fall Format | 3-4 | 3-4 |
| Master | 1-15, not cleared | **3-9, cleared** |

At the Master Tag Cup, match 2 fell on the second advised try (the Eldlich loaner with Count Aurum). Match 3 fell on
the second advised try with the new advice, the Angelechy loaner with Grandmaster Celeste, on turn 2. The stand-in
went first in 74 of 229 duels; yesterday it went second in every Expert and Master Tag Cup duel.

### The late circuits were harder tonight

| Circuit | 2026-10-28 | Tonight |
|---|---|---|
| Expert Series: The New Wave | 4-5 | 4-9 |
| Master League: Houston 2026 | 3-9 | 3-17 |
| Master League: October 2026 | 4-9 | 4-11 |
| Tier 0 Gauntlet | 4-9 | 4-16 |

The stand-in borrowed the same deck (the Tearlaments loaner) in both runs. That recipe was already 40 cards, so the
night's loaner fix didn't change it. The CPUs' decks there didn't change either.

### Measured: it was luck, not a change

`--circuit-study` (new, src/Ygo.Harness/CircuitStudy.cs) plays every match of a circuit as the QA stand-in plays it, on
fixed seeds. Results: `build/late-why/new.txt` and `old.txt`.

- **The two builds play the same duels.** The 10-28 run's build (405ba0e) and tonight's (HEAD) played 48 duels at each
  of the 15 matches (Ryzeal at The New Wave, Tearlaments at the rest). All 720 came out the same, win for win.
  - The one engine change between the runs that these decks could meet is the Pendulum Scale fix, and none of the 12
    decks has a Pendulum monster.
  - "Lead with rules" and "own targets" are used only by the four new playbooks (Radiant Typhoon, Artmage, Magistus
    Fairy Tail, Power Patron), and none of them plays here.
- **Tonight was an unlucky run; 10-28 was an average one.** From the measured rates, 100,000 simulated runs of the
  retries (a coin toss each duel, as the QA run has):

| Circuit | Expected losses | 10-28 | Tonight | Runs with as many losses as tonight, or more |
|---|---|---|---|---|
| Expert Series: The New Wave | 4.8 | 5 | 9 | 14% |
| Master League: Houston 2026 | 10.0 | 9 | 17 | 16% |
| Master League: October 2026 | 22.4 | 8 | 11 | 80% (both runs were lucky here) |
| Tier 0 Gauntlet | 10.6 | 9 | 16 | 20% |

The measured rates, match by match (you first / you second):

| Circuit | Match | Won | First | Second |
|---|---|---|---|---|
| The New Wave (Ryzeal) | Garunix Azamina, Fire King Snake-Eye | 64% | 50% | 79% |
| | Ryzeal Duo, Ryzeal | 33% | 20% | 45% |
| | Grandmaster Celeste, Angelechy | 35% | 29% | 41% |
| | Medius, Elfnote | 79% | 79% | 79% |
| Houston 2026 (Tearlaments) | Habakiri, Mitsurugi | 31% | 16% | 45% |
| | Archmage Noctis, Light and Darkness | 20% | 16% | 25% |
| | Albaz the Fallen, Branded Despia | 20% | 12% | 29% |
| October 2026 (Tearlaments) | Jolt, Blitzclique | 45% | 33% | 58% |
| | Warden Hask, Sacred Beast | 12% | 4% | 20% |
| | Mage Valdris, Dark Magician Chaos Ritual | 25% | 12% | 37% |
| | Sir Lucan Graye, Chaos Ritual | 8% | 0% | 16% |
| Tier 0 Gauntlet (Tearlaments) | Count Aurum, Eldlich | 43% | 33% | 54% |
| | Razen Vanquish, K9 Vanquish Soul | 41% | 29% | 54% |
| | Kitkallos, Tearlaments | 22% | 12% | 33% |
| | Seer Kelbek, Ishizu Tearlaments | 18% | 8% | 29% |

### What the measurement turned up: Tearlaments should go second here

At every one of the 11 Tearlaments matches, the loaner wins more going second, often two or three times as much
(Houston match 1: 16% first, 45% second). The stand-in goes first whenever it wins the toss, because only Mekk-Knight and
Tenpai Dragon have a "go second" tip (`Playbooks.SecondDecks`). A player borrowing Tearlaments gets the same advice:
nothing, so going first.

Going second every time would cut the expected losses about a third:

| Circuit | Now (coin toss) | Always second |
|---|---|---|
| The New Wave (Ryzeal) | 4.8 | 3.2 |
| Houston 2026 | 10.0 | 6.7 |
| October 2026 | 22.4 | 11.7 |
| Tier 0 Gauntlet | 10.6 | 6.2 |

Not changed yet. `SecondDecks` is read by the CPUs' toss as well, so adding Tearlaments there would change the Tier 0
Gauntlet's Kitkallos and Seer Kelbek too. They also win more going second, so it would make them harder, not easier.
A tip for the player only (the measured toss hint the Tag Cups already show) leaves the CPUs as they are.

## Problems

- **Board busy animating for 20 s** (once, Fall Format Tag Cup, turn 4). It came during a long chain: Lunalight Liger
  Dancer's wipe on top of Invoked Mechaba and Lunalight Yellow Marten. The duel went on normally 45 seconds later. It
  happened once before (10-27 run, Expert Tag Cup), so it's not new. Minor: the stand-in just waits.
- **The chain panel can cover part of your Banished pile** when a long callout and a 3-link chain show together
  (same duel's screenshot, `issue-01-g134-t4.png`). The callouts move out of the way; the chain panel is placed under
  the newest callout and isn't moved further.

## Not covered by this run

- The QA stand-in never bought OCG import packs (its shop visit picks the newest set or its wishlist). The shelf is
  covered by the campaign check (opens with the Expert band, 10 packs at 1,500 DP, a pack gives 9 OCG-only cards,
  closed in era-limited play).
- Art styles need picture files in `mods/cards/styles/`; the style check covers them with generated test pictures.

## The night's other work

- **Loaners stay legal** (80753bb): 67 of 923 recipe and circuit pairs fell under 40 cards after the ban list took
  cards out (Eldlich 36-37, Kashtira 34). They now top up from generic staples, within the list. New `--loaner-check`.
  The 10-28 report's finding ("loaners skip the ban list") was wrong: they always followed it.
- **Late-deck playbooks** (bb8ccc0), measured:
  - Mitsurugi holding its turn-1 Ritual: 51% either way over 144 duels. The turn-1 Ritual sets up the Prayers wipe,
    so the current play stays.
  - Branded's Cartesia-first opener: Cartesia needs Fallen of Albaz on the field first, and with one Albaz and one
    Branded Fusion in a 60-card list, it's possible in about 1 hand in 1,000. No route was written.

## Later the same day: ban lists bind only your team, and only in era-limited play (352562d)

The CPUs play every deck as it was built (they always did). Outside era-limited play nobody answers to a list any
more: your deck, loaners and partners' recipes play as built, and Ban Pass slots aren't sold. In era-limited play your
team follows each circuit's list (your deck and a deck lent to Kit must be legal; a partner's own recipe is adjusted like
a loaner), and Ban Passes work as before.

The Stuck? advice was re-measured for the usual campaign: all 111 options, 32 duels each (seed 3), loaners and
partners' recipes as built (`--wall-study --remeasure --you-chain auto`). Before: `build/advice-asbuilt/before.txt`.

| Wall | Best before (decks cut by the list) | Best now (decks as built) |
|---|---|---|
| Seraph | 70% Mitsurugi loaner | 62% Tearlaments loaner |
| Seraph tag | 56% your deck, Kit with HAT | 40% your deck, Unchained partner |
| Expert Tag Cup 1 | 68% your deck, Kit with Danger! Dark World | 62% Danger! Dark World loaner, Kozmo partner |
| Expert Tag Cup 2 | 96% | 90% your deck, Kit with Danger! Dark World |
| Expert Tag Cup 3 | 71% | 62% Danger! Dark World loaner and partner |
| Master Tag Cup 1 | 62% | 71% Fire King Snake-Eye loaner and partner |
| Master Tag Cup 2 | 46% | **65%** Fire King Snake-Eye loaner, Garunix Azamina |
| Master Tag Cup 3 | 56% | **75%** Tearlaments loaner, Seer Kelbek with Ishizu Tearlaments |

The Master walls gain the most: their best loaners were the 2022-2023 decks the September 2026 list cut hardest. The
Expert and Seraph walls play older lists that took little from these decks, so their changes are mostly the new seeds
(32 duels move about ±9 points). Master Tag Cup match 3's team you have when the cup opens (the Angelechy loaner with
Grandmaster Celeste) now measures about 40%, down from 55%.
