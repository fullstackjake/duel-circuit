# The No Limits Invitational and Moth's restored slots: build plan (2026-11-05)

The owner picked NL-2's non-deck parts as the next build: the No Limits Invitational and Moth's restored-card shelf
slots ([NO-LIMITS-SPEC](NO-LIMITS-SPEC-2026-11-02.md) section 9, 13, 14.4; [NO-LIMITS-ECONOMY](NO-LIMITS-ECONOMY-2026-11-02.md)
section 3, 4, 6; [NO-LIMITS-STORY](NO-LIMITS-STORY-2026-11-02.md) the Invitational scenes and pools). Wave 2's four builds
(Remy, Margo, Lux, Inka: decks, playbooks, Mind Scan) and every measurement wait for the tuning pass (parked by the
owner, 2026-11-05). Every owner decision in the three docs is accepted.

## What ships, and what stays locked

- The Invitational is built whole (bracket kind, field, pay, packs, tokens, ledger, story, Unity, checks) and opens only
  once No Limits is cleared **and** every `field:` build is `ready: yes` (spec 9.1). Today no build is ready, so it shows
  "Coming soon", like the No Limits table. Checks force it ready in their own state, never in data.
- The No Limits Pack pool and the restored shelf slots draw from **ready** builds, so they're empty until the tuning
  pass; checks force builds ready.
- The restored slots must keep the shelf's bound: a slab sells back for at most a sixth of what it cost. The NL-1 smoke
  run found the cheapest restored slab at 18% (1.5 × V rounded down to 500 at `ShelfRestoredMinValue` 1,500). Fix it
  inside the shelf's rules (e.g. round restored prices up, or the minimum value), proven by `--arbitrage` loop 5 on the
  real code.
- Small extra (owner, 2026-11-05): a Library cup's engraving skips a partner's edit, as the Format Master stamp does
  (`Loaners.IsYours`).

## Owner rules (bugs if broken)

- Every DP change through `Profile.Spend`/`Earn` with a ledger kind (`nolimits.entry`, `nolimits`); nothing
  DP-positive; the Invitational's pay at most 300 a duel.
- Old saves load: no new save lines beyond what the spec names (`AlleyBrackets["invitational"]` uses the existing
  `alleybracket` line); existing brackets' seeds don't move (`AlleySeed` per-kind table: brawl 0, midnight 1,
  invitational 2).
- No real player's name. CPUs at full strength. No difficulty measuring.
- Commit nothing. Never touch `vendor/ygopro-core`, `build/play-alley`, `build/rework-economy`, `build/qa-night5`.

## Chunks and file ownership

| Chunk | Owner | Files |
|---|---|---|
| **C1 engine + data** (first) | Backend Architect | `Brackets.cs`, `ContrabandPacks.cs`, `BackAlley.cs`, `MothsVan.cs`, `Posters.cs`, `Library.cs` (engraving), `Profile.cs` if needed; `campaign/alley-events.txt` (the Invitational block), `campaign/story.txt` (uncomment the pending Invitational scenes), `campaign/alley-voice.txt` if a pool needs it; the smallest harness edits that keep checks green |
| **C2 Unity** (after C1) | Unity Architect | `CampaignScreen.Alley.cs` (the chalkboard's third bracket), `DuelController.Brackets.cs` (`Brackets.RulesFor`, the No Limits Pack reveal), `VanScreen.cs` (the restored slots' tag), `DuelController.AlleyCheck.cs` (a new step 23), other `unity/**` as needed |
| **C3 harness** (after C1) | Senior Developer (Sonnet) | `AlleyTest*.cs`, `Arbitrage*.cs` (loops 5-8 on real code, the bracket search's three Invitational actions), `BracketStudy.cs` (the Invitational rows, smoke-run only), `LedgerCheck.cs` (the two keys in the mix) |
| **Review** | Code Reviewer, then a fix round | |

## Reporting

Files changed; the API as implemented; checks with exact counts; deviations; open questions. Short sentences.
