# Build plan (2026-11-06, third batch): misplay hunt, tuning pass, leftovers, check prefs

The owner chose all four after 43057bb, and un-parked the tuning pass. Nothing is committed until the owner says so.

**Order:** the misplay fixes change CPU strength, so the tuning pass plans now and measures only after the misplay
hunt's fixes land.

## Owner answers (shop keys)

Left/Right turn the page in single-column lists (Card Trader, Recipe Book); Enter on a highlighted pack or card opens a
confirm ("Buy X for N DP?"; a second Enter buys, Escape cancels; one press never buys); the d-pad drives the highlight in
the shop and the van (the stick keeps the virtual cursor).

## Chunks and file ownership

| # | Agent | Job | Files |
|---|---|---|---|
| M | Senior Developer | Misplay hunt: a large audit with `--keep-replays`, `--analyze-replay` on it and the owner's replays, triage the top decisions, fix the real misplays (B.E.S./Gladiator battle triggers included) with paired A/B proof | `src/Ygo.Engine/AI/**`, AI regression tests, a findings doc |
| T | Game Designer | Tuning plan: what to measure (after M) and how to retune by deck choice; owner decisions | `docs/dev/TUNING-PLAN-2026-11-06.md` only, until measuring |
| S | Narrative Designer | Story leftovers: `[open]` variants (alley-kit, nl-first-visit), New Game+ story with gates, Rhea and Seraph mentioned, other review leftovers | `campaign/story.txt`, `campaign/shop.txt`, profile text lines |
| K | Unity Architect | Shop keys per the answers; an in-session button for a held-back Invitational; app checks keep out of the player's real preferences | `ShopScreen.cs` and sub-screens, `VanScreen.cs`, `ArrowNav.cs`, `ControllerSupport.cs`, `BracketScreen.cs`/`Brackets` UI, check launch/prefs code, their checks |

Then independent review, a fix round, the harness and the 15 app checks on one combined build.

Shared rules: as in [NEXT-BUILD-2026-11-06.md](NEXT-BUILD-2026-11-06.md). CPUs at full strength: tuning is by deck
choice, never by weakening play or skill.

## Results

Built, reviewed by independent Code Reviewers (four: the story/shop/prefs work, the hunt, the analyzer fixes' area via
the hunt, the window work), fixed, and checked on one combined build. **Nothing is committed**; the owner decides.

| # | Job | Outcome |
|---|---|---|
| M | Misplay hunt | [MISPLAY-HUNT-2026-11-06.md](MISPLAY-HUNT-2026-11-06.md). `--hunt-audit` over 118 character-deck entries; 330 replays analyzed; 155 decisions triaged: 7 real misplays, 75 low-skill design, 15 judgement calls, 52 analyzer noise. Fixed (each with a toggle and a test): Set Rotation held forever (only into an empty Field Zone); GY-to-hand recovery valued in Main Phase play (Trait.Recover, kept out of deck building and playbook gates; net-negative recovery excluded); optional Spells wait for battle against Little Knight, Commander Souza and Tellusion (removal and S/T removal still go first); the planner's missed lethal (attack trap charges kept, stopping charged when a direct attack wins); "and if you do, take N damage" as a cost; B.E.S. and Gladiator Beast battle triggers; card versions phase 3.1 (base codes match only same-name cards). Whole-roster A/B 742 vs 741 (1,200 duels) after the review's fixes, 1,075 vs 1,076 (2,400) before; no deck moved by more than two errors. Analyzer fixes from the hunt: capped index ranking, sentinel scores, watcher blame by who caused it. |
| T | Tuning plan | [TUNING-PLAN-2026-11-06.md](TUNING-PLAN-2026-11-06.md) with the owner's answers; measuring starts next (about 70-85 machine-hours). |
| S | Story leftovers | `[open]` variants (alley-kit, nl-first-visit); four New Game+ scenes and footers gated after new-game-plus; Rhea and Seraph set up and given table scenes; nocturne-beat; alley-upstairs moved to antewin; gus-lessons and novice trimmed. |
| K | Shop keys, Invitational, check prefs | Enter asks before every buy or sell (shop, trader, used market, Recipe Book, van); Left/Right page; the d-pad drives the highlight (pad A confirms only in highlight mode). "Settle now" for a held-back Invitational. Check builds use their own bundle id (`com.ygocampaign.game.qa`, via ProjectSetup's -buildout/-qa and tools/qa_copy.sh); a safe snapshot/restore remains as a fallback; the player's prefs were identical before and after every run. |
| W | Resizable window (owner, mid-batch) | The window resizes; tall windows centre the layout with bars in the theme colour (the duel board stretches proportionally); minimum 1152x648 points set on the window itself; the windowed size is remembered in points; full screen uses the display's resolution. |

**Checks on the combined build:**
- Harness: `--campaign-test`, `--ladder-test` 59, `--copies-test`, `--nolimits-test`, `--headliner-test`,
  `--replay-test`, `--deckbuilding-test`, `--texts-check`, `--sense-test`, `--removal-timing-test`,
  `--forced-attack-test`, `--battle-misplay-test` 46, `--misplay-hunt-test` 14, `--analyze-test` 54, all pass;
  `--arbitrage` 0 DP-positive; the history-deck lint 0 problems.
- App (15 checks, qa bundle id): board 22, tag 4, pile 21, controls 39, order 10, layout 31, replay 14, mat 8, menu 6,
  campaign 179, final 46, theme 19, shop 89, alley 120, ladder 42, all green first time; `-versioncheck` 20; the player's
  prefs identical before and after. Audio not heard.
- Not live-tested: dragging the window below the minimum, the launch resize, switching to full screen; real-gamepad
  timing; a Ladder or Vera duel played to a natural win.
