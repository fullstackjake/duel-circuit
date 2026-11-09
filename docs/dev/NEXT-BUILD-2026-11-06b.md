# Build plan (2026-11-06, second batch): the Ladder spec, story gates and Vera, small UX, two CPU misplays

The owner chose all four after 1274f39. Nothing is committed until the owner says so.

## Chunks and file ownership

| # | Agent | Job | Files (only these) |
|---|---|---|---|
| L | Game Designer | Spec the Champion's Ladder (spec only; the owner approves it before anything is built) | `docs/dev/CHAMPIONS-LADDER-SPEC-2026-11-06.md` only |
| G | Senior Developer | `after:`/`before:` gates for story scenes; Vera's rematch as a post-final guest | `Story.cs`, `CampaignState*.cs` (scene firing), `campaign/guests.txt`, `personalities/vera.profile` data lines, `CampaignTest.cs`, the campaign/final app checks, `DuelController.Guests.cs` if needed |
| U | Unity Architect | QA leftovers: chain panel clipping, the summon hint, card names wrapping mid-word, the editor message shown twice and its badge, Moth's van arrow keys | `BoardView.*.cs`, `CardView.cs`, `DeckEditor*.cs`, `VanScreen.cs`, `ArrowNav.cs`, their app checks (board, controls, layout, shop) |
| C | Senior Developer | Two CPU misplays: idle attackers in a reused zone; attacking into "Cannot be destroyed by battle" | `src/Ygo.Engine/AI/**` except `Personality.cs`, a harness regression test |
| W | Narrative Designer (after G) | Vera's rematch scenes; order-aware variants the gates allow (D6's cases) | `campaign/story.txt`, text lines of `personalities/vera.profile` |

Then an independent Code Reviewer, a fix round, the harness and the 14 app checks on one combined build.

Shared rules: as in [NEXT-BUILD-2026-11-06.md](NEXT-BUILD-2026-11-06.md) (owner rules; never touch vendor/ygopro-core,
build/play-alley, build/rework-economy, build/qa-night5 except read-only mods, the owner's save; own harness folder;
the Unity lock; absolute paths for app checks; tuning parked: no deck retuning).

## Results

Everything below is built, reviewed by independent Code Reviewers (three: this batch, the AI fix, the analyzer), fixed,
and checked on one combined build. **Nothing is committed**; the owner decides.

| # | Job | Outcome |
|---|---|---|
| L | Champion's Ladder | Spec [CHAMPIONS-LADDER-SPEC-2026-11-06.md](CHAMPIONS-LADDER-SPEC-2026-11-06.md) with the owner's answers. Built: ten rungs past the binders, opening after your first win over Ilse; no falling; Ilse's rung is her four decks in one sitting; a new top, **Ada Quill, "This Week"**, on chaos-ritual (the highest-rated deck legal under the Gauntlet's list); the visitors (Rue, Sasha, Augie, then Junia) after the Invitational title; always the TCG September 2026 list, in open play too; no loaners; 800 DP a first climb, 150 a rematch, 5,000 + 3 tokens + the "Top Rung" poster at the top, once ever (27,150 DP max, arbitrage-checked); save keys by holder; 13 scenes; `--ladder-test`, `-laddercheck` (the 15th app check). |
| G | Story gates, Vera | `after:`/`before:` gates for story scenes (skipped scenes stay unseen; two variants play exactly one); two gated pairs (advanced-tag-clear after champion; every-era-round-2 with Ines's Scapegoat after held-card-home). Vera as a post-final guest on her circuit warriors deck (the trap-waiting build waits for tuning), `guest:vera` / `guestbeat:vera` scenes, a `for: vera` scouting report, six guest rows. Ilse's and Vera's first-win prizes pay once ever (`prize: 10000 once`). |
| U | Small UX | The chain panel (rows grow, "+N more", opaque), "summon here" only in your Main Phase, card names hyphenate instead of breaking mid-word and clear the limit badge, the repeated Recipe message, arrow keys in Moth's van (Enter never buys or sells). |
| C | Two CPU misplays | Battle memory keyed by card identity; "Cannot be destroyed by battle" in any case with conditions rejected; no attacks into battle-proof monsters whose controller takes no battle damage or reflects it (Utopic Future, the Timelords, the Mikanko). Paired A/B 1,240 duels: fixed 548, old 544, no deck dropped; vs Performapal 59 vs 53. `--battle-misplay-test` (30). |
| R | Replay analyzer (owner, mid-batch) | Spec [REPLAY-ANALYZER-SPEC-2026-11-06.md](REPLAY-ANALYZER-SPEC-2026-11-06.md). `--analyze-replay <file|folder>`: an HTML page per replay with every decision, the full board (both hands), the options, the play, a full-strength second opinion with planner scores and regret, and flags; an index ranking the most suspicious decisions. `--keep-replays` on audit and study runs (flagged only, cap 200, with a `.replay.bot` sidecar). Read-only hooks in SmartDuelist/TurnPlanner; play byte-identical with them on or off and against the pre-change build. All 31 of the owner's replays analyze in about 7 s; the top suspect: Juniper Pell normal-summoned Robina where Set Rotation rated 5,297 better. |

**Reviews:** batch review (no blockers; fixed: the ladder editor's list, save keys by holder, a real-win check, the
row-fit check, Ada hidden until her introduction, results labels, a live ladder duel never dropped, the window restore,
the chain panel's comma names; owner calls: prizes once, the Gauntlet's list always, Vera's deck for tuning). AI review
(no blockers; fixed: wider no-damage and reflector matches, an identity test, a harness crash). Analyzer review (fixed:
the output guard against case and symlinks, the fresh-memory cap, flagged keeping of missed lethal, retries, flags on
the right player, the sidecar for wrapped seats and switched-off playbooks, diverged and drifted rows, bad files, and
ten nits).

**Checks on the combined build:**
- Harness: `--campaign-test`, `--ladder-test` 59, `--copies-test`, `--nolimits-test`, `--headliner-test`,
  `--replay-test`, `--deckbuilding-test`, `--texts-check`, `--sense-test`, `--removal-timing-test`,
  `--forced-attack-test`, `--battle-misplay-test`, `--analyze-test` 53, all pass; `--arbitrage --load
  build/qa-night5/mods` 0 DP-positive; the history-deck lint 0 problems.
- App (15 checks now): board 22, tag 4, pile 21, controls 39, order 10, layout 29, replay 14, mat 8, menu 6, campaign
  179, final 46, theme 19, shop 80, alley 118, ladder 42, all green first time; `-versioncheck` 20. Audio not heard.
- Not run: a ladder or Vera duel played to a natural win (checks concede or record); the analyzer's pages in a real
  browser (checked in a node DOM only); the full `--peak-check all`.
