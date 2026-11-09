# Code-only bug batch and Return Rook's smaller show: build plan (2026-11-05)

The owner picked the TODO's code-only bugs as the next build (tuning stays parked), and decided Return Rook ships with the
smaller show. This page splits the work. Each item quotes its TODO entry (docs/dev/TODO.md); the fix is the agent's design,
reported back.

## Owner rules (bugs if broken)

- Every DP change through `Profile.Spend`/`Earn` with a ledger kind; nothing DP-positive.
- Old saves load: optional trailing fields only, no `version` bump.
- CPUs play at full strength. Return Rook's change is which route his show calls (the payoff), not a weaker deck.
- Tuning is parked: no difficulty, CPU or deck measuring beyond Return Rook's goldfish go/no-go.
- Commit nothing. Never touch `vendor/ygopro-core`, `build/play-alley`, `build/rework-economy`, `build/qa-night5`, or the
  owner's real save under `~/Library/Application Support/YgoCampaign`.

## Agents and file ownership

| Agent | Items | Files (only these) |
|---|---|---|
| **E: engine** (Backend Architect) | 1 DP sinks through `Spend`; 2 Format Master and partner edits; 3 the bounty board's gate (engine side) | `src/Ygo.Engine/Campaign/*` except `Posters.cs` and `ReturnRook*`; `src/Ygo.Harness/LedgerCheck.cs`, `Arbitrage.cs`, a new harness test file if needed |
| **U: Unity** (Unity Architect) | 4 results over the next duel; 5 lost result scenes; 6 the level-99 EXP line; 7 replays record the mat; 8 the bracket versus's Stuck? and Make it yours; 9 the final's panel and custom loaners; 10 the round's Yata-lock line; 11 `baz:nolimits`; the bounty button wiring once E's gate lands | `unity/**`, `src/Ygo.Engine/Replays/*` |
| **R: Rook and posters** (Senior Developer) | 12 Return Rook's smaller show; 13 the Every Era poster asks the Library | `src/Ygo.Engine/AI/Playbooks/ReturnRookPlaybook.cs`, `src/Ygo.Engine/Campaign/Posters.cs`, `campaign/alley-events.txt` (Rook's block only), `campaign/replays/`, `personalities/return-rook.profile`, Rook's scenes in `campaign/story.txt`, `src/Ygo.Harness/HeadlinerTest.cs`, `ComboGoldfish.cs`, `CampaignTest.cs` (poster checks only) |

Each agent builds the harness into its own folder (`build/bin/bugs-<e|u|r>`). Only U builds Unity. The coordinator runs the
final build and all checks after everyone lands, then a code review.

## Reporting

Files changed; each item's fix and how it was verified; checks with exact counts; deviations; open questions.
