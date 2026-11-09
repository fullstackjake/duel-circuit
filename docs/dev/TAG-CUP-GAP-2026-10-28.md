# Why the Tag Cups were walls in real play (2026-10-28)

## The question

In the fresh-save QA run ([QA-PLAYTEST-2026-10-28.md](QA-PLAYTEST-2026-10-28.md)) the Expert Tag Cup went 1-12 and the
Master Tag Cup 1-15. Neither was cleared. The measured Stuck? advice said the advised teams win 87% (Expert match 1)
and 56% (Expert match 3). The stand-in took the advised team every time and still lost.

## What differed between the study and real play

Three things, found by comparing `--wall-study` (src/Ygo.Harness/WallStudy.cs) with the app's QA play:

1. **The QA stand-in played tag duels without its turn planner.** A comment in the app said the planner only replays
   two-player duels, so in a tag duel the stand-in played without looking ahead. That was out of date: the tag CPUs
   already plan with the tag recipe, and so does the study's stand-in. The study measured a planning player; the run
   played a weaker one.
2. **It went second in all 29 Expert and Master Tag Cup duels.** When it won the toss it followed the toss screen's
   tip, "Your deck works best going second", which comes from the Mekk-Knight playbook (a one-on-one preference). When
   it lost, the CPUs chose to go first. Many of those duels ended on turn 3, before your partner's first turn.
3. **Some of the advice was stale.** It was measured with 16 duels per option on 2026-10-27, before the later CPU
   fixes (Branded, K9 and others). Master match 2's advised team ("50%") now wins about 17%.

Chain: Auto (the player's default chain prompts) passing chances the bot always takes turned out to be a small factor.

## Measured

`--wall-study` with the new switches (`--option`, `--you-plan off`, `--you-chain auto`), 40 duels per mode, the advised
team at each match:

| Match, advised team | As the study played | As the app played (no planner, Chain: Auto) | Fixed (planner, Chain: Auto) |
|---|---|---|---|
| Expert 1: your deck, Kit with Danger! Dark World | 70% (first 85, second 55) | 50% (70 / 30) | 65% |
| Expert 3: your deck, Kit with Danger! Dark World | 57% (65 / 50) | 50% (70 / 30) | 55% (65 / 45) |
| Master 1: Fire King Snake-Eye loaner, Garunix with Fire King Snake-Eye | 62% | 37% | 62% |
| Master 2: your deck, Kit with Eldlich | 15% | 20% | 17% |
| Master 3: Eldlich loaner, Count Aurum with Eldlich | 20% | 25% | 20% |

Expert match 1 alone, by what was changed: the planner is worth 18 points (70% to 52%), Chain: Auto 5 (70% to 65%).
Going second cost 40 points there in the app's way of playing (70% first, 30% second).

## What changed

- **The QA stand-in plans ahead in tag duels** (DuelController.cs), like the tag CPUs and the study.
- **The coin toss in tag duels** (DuelController.Toss.cs): no more "your deck works best going second" in a tag duel,
  since a deck's preference is for one-on-one duels and the measured tag matches came out even overall (34% first, 36%
  second across 102 options). Where the advice measured your exact team at that match and first and second differ by
  20 points or more, the toss screen says so ("Measured with this team here: going first won 75%, going second 62%").
  Guest duels get the same. The QA stand-in follows the toss screen's tip, as a player reading it would.
- **The Tag Cup versus screen** no longer says "You play first"; the toss decides that.
- **The advice re-measured** (`--wall-study --remeasure --games 32 --you-chain auto --write campaign/wall-advice.txt`,
  2 h 54 min): the same 90 options as before at the six Tag Cup matches, 32 duels each, with today's CPUs, the
  planner, and the game's default Chain: Auto. The file's header now says how the stand-in played. Best options now:

| Match | Before (16 duels) | Now (32 duels) |
|---|---|---|
| Expert 1 | 87%: your deck, Kit with Danger! Dark World | 68%: the same |
| Expert 2 | 87%: Danger! Dark World loaner and Nessie Grapha's Danger! Dark World | 96%: the same |
| Expert 3 | 56%: your deck, Kit with Danger! Dark World | 71%: Danger! Dark World loaner, Nessie Grapha with Danger! Dark World |
| Master 1 | 62%: Fire King Snake-Eye loaner, Garunix with Fire King Snake-Eye | 62%: the same |
| Master 2 | 50%: your deck, Kit with Eldlich | 46%: Eldlich loaner, Count Aurum with Eldlich |
| Master 3 | 68%: Branded Despia loaner, Seer Kelbek with Ishizu Tearlaments | 56%: Branded Despia loaner, Kitkallos with Tearlaments |

## In play

A fresh campaign played by the QA stand-in again (`-qaplay -qafull -qaloaners -qatools`, log:
`build/qa-tag/out/qa-log.txt`), stopped after the Master Tag Cup:

| | Before (fresh run, 2026-10-28) | After |
|---|---|---|
| Expert Tag Cup | 1-12, not cleared | **3-4, cleared** (matches 1 and 2 on the first try; match 3 on the 2nd advised try) |
| Master Tag Cup | 1-15, not cleared | 2-12, not cleared (match 1 on the 1st advised try; match 2 0-6; match 3 on the 1st advised try) |
| Duels going first | 0 of 29 (it won 14 tosses) | 12 of 21 (every toss it won) |

The stand-in went first whenever it won the toss. The Master Tag Cup is still the hardest thing in the campaign:
match 2's best measured team wins 46%, and it lost all three advised tries (about 1 chance in 6). At match 3 the best
team this save could field, when the cup opens after Master League: Link Wars, was the Eldlich loaner with Count Aurum
(21%). The stronger teams there (the Branded Despia loaner, 56%) need decks from circuits that come later.

## Master Tag Cup match 3, as the cup opens (2026-10-29)

The cup opens after Master League: Link Wars, but match 3's decks (Mitsurugi and Branded Despia) come from Houston
2026, two circuits later. The measured teams there all needed later decks, so the best team a player could take when
the cup opens was the Eldlich loaner with Count Aurum: 21%. CPUs unchanged; instead, the teams a player has by then were
measured (`--wall-study --only "Master Tag Cup 3" --loaners 6 --partners 6 --kit 4`): 77 teams at 12 duels, then the six
best at 40.

| Team (all yours once Link Wars is cleared) | Won | You first | You second |
|---|---|---|---|
| **Angelechy loaner, Grandmaster Celeste with Angelechy** | **55%** | 40% | 70% |
| Ryzeal loaner, Volta Raijin with Thunder Dragon | 35% | 50% | 20% |
| Thunder Dragon loaner, Ryzeal Duo with Ryzeal | 35% | 20% | 50% |
| Ryzeal loaner, Kit with Angelechy | 32% | 35% | 30% |
| Eldlich loaner, Drake Magnamhut with Bystial | 20% | 20% | 20% |
| Your deck, Ryzeal Duo with Ryzeal | 17% | 20% | 15% |

The Angelechy team does as well as the best team that comes later (the Branded Despia loaner with Kitkallos, 56%), so
the cup can stay where it is. It's in the advice now (`--option` with `--write` merges a team into a match's list), so
a player stuck at match 3 is told to take it, and to go second if they win the toss (70% against 40%). The campaign
check covers it.

## Still open

- **Master Tag Cup match 2** stays a near coin flip at best (46%).
- Chain: Auto costs about 5 points in tag duels against hand traps; Chain ON (or holding the ask key) gets them back.
- The guest tag duel (Seraph) wasn't re-measured.
