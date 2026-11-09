# Build plan (2026-11-06): the story pass, Ilse, two No Limits bugs, small UX

The owner chose all four after the story review ([STORY-REVIEW-2026-11-06.md](STORY-REVIEW-2026-11-06.md), "The owner's
answers"). Nothing is committed until the owner says so.

## Chunks and file ownership

| # | Agent | Job | Files (only these) |
|---|---|---|---|
| S | Game Designer | Spec: Ilse as a post-final guest; the `firstloss` and `finale` triggers; the `partner:` profile field | `docs/dev/ILSE-GUEST-SPEC-2026-11-06.md` only |
| W1 | Narrative Designer | The story review's items 1-6, text only, plus the scenes for `firstloss`, `finale` and `{partnerline}` | `campaign/story.txt`, `campaign/shop.txt`, `campaign/alley-voice.txt`, text fields of `personalities/*.profile`, `campaign/alley.txt` and `campaign/alley-events.txt` |
| B1 | Senior Developer | The two No Limits bugs: the Yubel prompt stall, the get_attack_target hang; a prompt cap and turn limit for `--peak-check` | `src/Ygo.Engine/AI/**` except `Personality.cs`, the harness's endless/peak-check files, NL playbooks |
| U1 | Unity Architect | QA's small items: Save on an unchanged recipe deck piles up copies; the wallet line wraps at 1280x720; the QA harness's stale second deck | `DeckEditor*.cs`, `CampaignScreen.cs` (the header), `DuelController.Qa.cs` |
| B2 | (after S) builder | Ilse's guest entry and the three hooks | `Story.cs`, `Guests.cs`, `Personality.cs`, `campaign/guests.txt`, Ilse's decks, `CampaignState*.cs`, `DuelController.Guests.cs`, `CampaignScenes.cs`, `CampaignTest.cs`, the final/campaign app checks |
| W2 | (after S) Narrative Designer | Ilse's scenes and profile lines | `campaign/story.txt`, `personalities/ilse.profile` |

Then an independent Code Reviewer, a fix round, the harness checks and the 14 app checks on one combined build.

## Shared rules

- Owner rules (bugs if broken): every DP change through `Profile.Spend`/`Earn`; nothing DP-positive; old saves load;
  CPUs at full strength (fix misplays, never weaken); no real players' names; original characters.
- Never touch `vendor/ygopro-core` (read it, don't edit or commit it), `build/play-alley`, `build/rework-economy`,
  `build/qa-night5` (read-only use of its mods is fine), or the owner's real save.
- Each agent builds the harness into `build/bin/<agent>`. One Unity editor at a time: take
  `build/unity.lock` with `until mkdir .../build/unity.lock 2>/dev/null; do sleep 30; done`, build with
  `-buildout build/<agent>/Duel Circuit.app`, `rmdir` the lock after (also on failure). App checks run from your own
  folder with absolute `-mods`, `-logFile` and out paths.
- Tuning stays parked: no deck retuning, no difficulty changes.

## Results

Everything below is built, reviewed by an independent Code Reviewer, fixed, and checked on one combined build.
**Nothing is committed**; the owner decides.

| # | Job | Outcome |
|---|---|---|
| S | Ilse spec | [ILSE-GUEST-SPEC-2026-11-06.md](ILSE-GUEST-SPEC-2026-11-06.md); the owner took all three defaults (finale on two strands now, Mirror Force, 10,000 DP each cycle). |
| W1 | Writing pass | Items 1-6 done: Nyx's Table and Volta Kask, chair-neutral Junia, the continuity slips, 52 scenes that repeated the line just read, the rule in story.txt's header, `first-partner` with `{partnerline}` and 82 `partner:` lines, Ribbit Ruiz "the Bubblegum Loop", Mika and Moth introduced, the borrowed tics, long and muddled lines (the Kashtira one), Kit's introduction and motive, Gus's back door, `gift-ines`, Junia's last word, the Duchess plant (`first-stamp`, `graveyard-kings-clear`), regulars' pools at 8, Penny's post-loss pools, cameos, `first-loss`, `finale` (Baz chalks UPSTAIRS), two upstairs footers. Private canon in [STORY-BIBLE.md](STORY-BIBLE.md). |
| W2 | Ilse's scenes | `ilse-table` (guest:ilse), `gift-ilse` (beat:ilse, corkboard-top), her profile's duel lines and a partner line; every-era-corkboard's "not in a hurry" now points at her table. |
| B1 | No Limits bugs | One bot loop: under Nightmare Pain's forced attack the bot cancelled the core's attack-target choice and got the same menu back forever. Now it never calls off a forced attack (DuelContext `AttackForced`, SmartDuelist). Endless test 24 of 5,000 No Limits duels stalled before, 0 of 20,000 after; Lacrima's peak check finishes; `--peak-check` has a 40-turn and 8,000-prompt cap; `--forced-attack-test`. nl-branded's turn-40 games are real long games. |
| U1 | QA's small items | Save with nothing changed says so; Save as new of a deck you already have opens it; after Recipe/Build/Import Save still asks unless only the title differs (starters kept); the wallet is three scaled lines; the QA harness skips its second-deck check after other duels. |
| B2 | Ilse and the hooks | `guest:` lines take several decks and an optional partner; `unlock: final`; Ilse's deck by wins against her (mod 4); her duels on the Tier 0 Gauntlet's rules; no tag duel; Tag Cup partner once beaten; five guest rows fit; `corkboard-top`; `firstloss` (circuit and guest losses, once, the save's first loss); `finale` (caught up on Open); `partner:` and `{partnerline}`; scouting reports can be `for: <character>`; the QA autoplay reads her meeting scene. |

**Review:** one bug (Ilse's zombies and gadgets reports, keyed by deck, reached Gus, Vera, Tink and Brask in the Beginner
band: now `for: ilse`, with a check), three risks (first-loss leaned on an unheard rule; Save after a rebuild could drop
starters; the header check couldn't fail on width) and nits (AttackForced reset per turn, an unused field, a repeated
line, four long lines). All fixed.

**Checks on the combined build:**
- Harness: `--campaign-test` (all pass, 399 ok lines), `--copies-test`, `--nolimits-test`, `--headliner-test`,
  `--replay-test` identical, `--deckbuilding-test`, `--texts-check`, `--sense-test`, `--removal-timing-test`,
  `--forced-attack-test` all pass; `--arbitrage --load build/qa-night5/mods` 0 DP-positive (2,379 sequences); the
  history-deck lint 0 problems.
- App: board 22, tag 4, pile 21, controls 37, order 10, layout 25, replay 14, mat 8, menu 6, campaign 179, final 40,
  theme 19, shop 73, alley 118, all green first time; `-versioncheck` 20. Audio not heard. The guests screen (five
  rows) and the corkboard-top backdrop looked at.
- Not run: the full `--peak-check all`; a long `-qacontinue` QA run (the QA tool's fixes are compiled, not run); Ilse's
  duels played to the end (the final check records her win through the results path).

Open items are in [TODO.md](TODO.md), "From the 2026-11-06 build".
