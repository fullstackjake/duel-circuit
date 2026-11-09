# Overnight work (2026-11-06): plan

The owner went to sleep and asked for six things, built, reviewed and checked, left uncommitted for the morning:

1. **Shop arrows** (owner): move between highlighted cards in the shop with the keyboard arrows.
2. **The CPU's wasted destruction** (owner): early on, the CPU destroys cards that were activated and are going to the
   field, for nothing in return. Find the cause (likely destroying a Normal Spell, Quick-Play Spell or Normal Trap in
   chain to its own activation, which doesn't stop it), fix the bot's choice, and prove it. A misplay fix, so CPUs get
   stronger, never weaker; the owner's full-strength rule holds.
3. **Save the scene queue and small fixes:** the queued story scenes survive quitting the app (an optional save field);
   the Yata-lock line on guest and Tag Cup results; a way out for a held-back Invitational finish.
4. **Story scene backgrounds:** a code-drawn backdrop per place, picked by each scene.
5. **An overnight QA playthrough** of tonight's starting build (c80b436), from a champion save: a bug report.
6. **The tuning measurements** (un-parked by the owner for measuring only): numbers written up, nothing retuned.

## Shared rules

- Owner rules (bugs if broken): every DP change through `Profile.Spend`/`Earn`; nothing DP-positive; old saves load;
  CPUs at full strength; no real player's names. Commit nothing. Never touch `vendor/ygopro-core`, `build/play-alley`,
  `build/rework-economy`, `build/qa-night5` (read-only use of its mods is fine), or the owner's real save under
  `~/Library/Application Support/YgoCampaign`.
- **Frozen copies for the long runs:** `build/overnight/Duel Circuit.app` (the QA run) and `build/bin/overnight` (the
  measurements) are tonight's starting point, c80b436. Builders never touch them.
- **One Unity editor at a time.** Unity can't open the project twice. Builders take a lock before any Unity build:
  `until mkdir "$PWD/build/unity.lock" 2>/dev/null; do sleep 30; done`, build with
  `-buildout build/<agent>/Duel Circuit.app` (ProjectSetup supports it), then `rmdir` the lock (also on failure). Run app
  checks from your own folder with absolute `-mods`, `-logFile` and out paths, never `build/qa-quick` (the coordinator's).
- Each builder builds the harness into its own folder (`build/bin/<agent>`); a build failing on another agent's
  half-written file is retried after a short wait.

## File ownership

| # | Agent | Files |
|---|---|---|
| 1 | Unity Architect | `ShopScreen.cs` and the shop's sub-screens' input; `ControllerSupport` only if needed |
| 2 | Senior Developer | `src/Ygo.Engine/AI/**` (SmartDuelist, TurnPlanner, BlunderAudit's checks), a harness regression test |
| 3 | Unity Architect | `Profile.cs` (the queue's save field), `Brackets.cs` (the held-back exit), `CampaignState.Alley.cs`, `DuelController.Results.cs`, `DuelController.Guests.cs`, tag results |
| 4 | Unity Architect | `StoryScreen.cs`, new backdrop painter `.cs` (+ `.meta`), `Story.cs` and `campaign/story.txt` for a place key |
| 5 | Evidence Collector | the frozen app copy only; a report |
| 6 | Senior Developer (Sonnet) | the frozen harness copy only; a report |

The coordinator reviews (Code Reviewer), runs one final Unity build and all checks, and writes the morning summary.

## Results (morning summary)

Everything below is built, reviewed by an independent Code Reviewer, fixed, and checked on one combined build. **Nothing
is committed**; the owner decides in the morning.

| # | Job | Outcome |
|---|---|---|
| 1 | Shop arrows | Arrows move the highlight on the shop shelves, the pack reveal (Enter turns a card over), the Card Trader, the Grading Desk and the Recipe Book; Enter never buys. Also fixed: the Recipe Book opened *behind* the shop; a clicked Buy button stayed selected, so Enter bought another pack with no confirm; an Enter that closes the pause menu no longer also acts on the shop under it. |
| 2 | The CPU's wasted destruction | Cause: the CPU chained Mystical Space Typhoon or Dust Tornado onto a Normal or Quick-Play Spell or Normal Trap as it activated (it resolves anyway). The first fix stopped that but blocked four good plays (the review caught it: Artifact Ignition on Heavy Storm, a lingering Altergeist Manifestation, Shade Brigandine, Filia Regis on Monster Reborn), so it was reworked into a narrow safelist (MST, Cosmic Cyclone, Typhoon, Twister, Twin Twisters, Dust Tornado; only when the target really waits on the chain). Beginner band: 42 to 0 wasted. A paired 1,200-duel A/B on the six risky decks: new 469, old 465, no drop. Turn-one boards identical. Swords of Revealing Light is now valued as a real target for destroy and banish. The re-review said ship. |
| 3 | Story queue saved, small fixes | Queued story scenes survive quitting (optional save lines; each plays exactly once, even after a quit mid-batch); Start Over no longer carries them into a new game; the save's .bak again holds the save before the duel. The Yata-lock line shows on guest and Tag Cup results. A held-back Invitational finish settles at the next launch (DP and record now, packs owed). |
| 4 | Story backdrops | 11 code-drawn places (shop floor, shop at night, corkboard, back room, Library, Every Era table, hall, alley, basement, Moth's van, the Depot), a `place:` key per scene with sensible defaults, crossfades, painted in the background so a first visit doesn't stutter. Owner art in `art/backdrops/<place>.png` replaces a painting. |
| 5 | QA playthrough | 320 duels over about 4.5 hours on tonight's starting build: no real bugs (no crashes, freezes, errors, stuck scenes; the DP ledger exact). Two small items to the TODO. Report: [QA-OVERNIGHT-2026-11-06.md](QA-OVERNIGHT-2026-11-06.md). |
| 6 | Tuning measurements | [TUNING-MEASURE-2026-11-06.md](TUNING-MEASURE-2026-11-06.md), measurements only, nothing retuned. No Limits' ship gate fails: the reference deck wins 71% of the pool (target 55% ± 5), only Junia, Noroi and Albion sit inside the 45-65% band, and three builds play worse than their bases. The Black Market 84% (target 60%). The Brawl 46-78% by stage (target 30-45%); the Midnight Open 26% (inside); the Invitational 12-23% on stand-in ratings. Five Locals still run easy (46-74%). Dumpster Row 99% / 36%, The Basement Game 96% / 35%, Nyx's Table 69%, Orvel's 53% (inside its error). Two possible bugs, below. |
| + | Story review (asked mid-night) | [STORY-REVIEW-2026-11-06.md](STORY-REVIEW-2026-11-06.md): merged recommendations and 12 owner decisions. |

**Checks on the combined build:**
- Harness: `--campaign-test` 385, `--copies-test`, `--nolimits-test`, `--headliner-test`, `--replay-test`,
  `--deckbuilding-test`, `--texts-check`, `--sense-test`, `--removal-timing-test` (16) all pass; `--arbitrage` 0
  DP-positive; the history-deck lint 0 problems.
- App: board 22, tag 4, pile 21, controls 37, order 10, layout 25, replay 14, mat 8, menu 6, campaign 173, final 29,
  theme 19, shop 73, alley 117, all green first time; `-versioncheck` 20. Audio not heard.
- One flake seen once in an agent's run (the card-text check cycling Mind Master to the wrong entry); it passed on every
  later run including the final one.

**Possible bugs from the measurements** (No Limits decks only, which players can't reach yet; in TODO): the Yubel decks
hit the prompt cap on turns 2-3 in the endless test (a stalled prompt, not a tuning number), and one peak-check duel never
ended, stuck in the engine's attack-target choice (the peak check has no prompt cap or turn limit).

**Open questions** are in TODO ("From the overnight work").
