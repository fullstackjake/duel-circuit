# No Limits: build spec (2026-11-02)

A Game Designer agent's build spec for the plan the owner approved in [META-DECKS-2026-11-02.md](META-DECKS-2026-11-02.md)
("Yes, plan it"). It covers the No Limits table above Nyx's Table, the 11 peak builds in two waves, the No Limits
Invitational and the Alley Brawl's out-of-towners. It's written so implementation agents can build without guessing, in
the shape of [ALLEY-SPEC-2026-11-01.md](ALLEY-SPEC-2026-11-01.md). Read-only on code: nothing here is built yet.

- **Numbers are the Economy Designer's** ([NO-LIMITS-ECONOMY-2026-11-02.md](NO-LIMITS-ECONOMY-2026-11-02.md), landed):
  - stake 2,500 DP held, no card of yours; the CPU's card 2,500-10,000;
  - one binder per pilot, kept across nights (`nl:<pilot>`), led by the build's `stakes:`, with the nightly restock;
  - the Invitational at 3,000 entry, the Midnight Open's pay, 4 / 3 / 2 / 1 / 0 No Limits Packs and 2 tokens;
  - two restored slots on Moth's shelf;
  - ledger keys `nolimits.stake`, `nolimits.entry` and `nolimits`.

  Its knob names go into an `AlleyEconomy` "No Limits" block (section 13). Every number is [P] until the built table is
  measured.
- **Names, lines and scenes are the Narrative Designer's** ([NO-LIMITS-STORY-2026-11-02.md](NO-LIMITS-STORY-2026-11-02.md),
  landed):
  - the Depot behind Nyx's wall; Nyx keeps the door, Junia Calder runs the game and Baz MCs;
  - the eleven pilots: ids, handles and pronouns;
  - the triggers `nolimits`, `pilot:<id>`, `pilotbeat:<id>`, `restored`, `invitational`, `outoftowner` and
    `outoftowner:<id>`;
  - the voice hooks.

  This spec adopts its ids and triggers as written. Where it disagrees with either doc, it says so in section 16.
- **Owner decisions in META-DECKS are accepted defaults:**
  1. no list for you at No Limits (`Never` and `BossOnly` stay out);
  2. 11 decks in two waves, cap 13;
  3. named out-of-towners in the Alley Brawl;
  4. the Maliss playbook keyed to the peak build only;
  5. Nyx's easy seats fixed with older decks (Phase D, not here);
  6. a 2025-26 Library cup later (not here);
  7. Djinn Releaser of Rituals and Archnemeses Protos into `Alley.BossOnly`.
- **CPUs are not weakened.** Peak builds play as authored, at skill 1.0. Nothing is cut after a build ships; difficulty
  is tuned before it ships, by how much contraband goes in.
- **Fictional pilots only.** No pilot, line, profile or deck note names a real player (section 5.4: no `#source` lines).

**Changelog**
- v1 (2026-11-02): first spec, aligned with NO-LIMITS-ECONOMY and NO-LIMITS-STORY (both landed while it was written).

## Contents

1. What already exists (don't re-spec it)
2. Findings in the code (read before building)
3. Prerequisites
4. The No Limits table
5. The peak builds
6. Toon
7. Playbooks and engine gaps
8. The Alley Brawl's out-of-towners
9. The No Limits Invitational
10. Cast, story hooks and data formats
11. Look and sound
12. Save, New Game+ and the ledger
13. Knobs
14. Build split and sequencing with Phase D
15. Risks
16. Owner decisions

## 1. What already exists (don't re-spec it)

Everything in ALLEY-SPEC section 1, plus Phases A-C as built (its "As built" notes are binding). What No Limits reuses:

- **Tables, nights, seats:**
  - `BackAlley.Lineup`, `DrawNight` (an authored table's seats: `seat: <n> <kind>`), `NextSeat` and `Record`;
  - `AlleyTableState` (night, won mask, first clear) and `AlleyOpponent` (saved as `alleyopp` lines, every field a free
    string);
  - the closer, wildcard and encore logic for headliners (`Unbeaten`, `WildcardChance`, `Encore`, `EncoreOpen`).
- **Ante:** `Ante.Hold`, `Settle`, `SettleAbandoned`, the first-turn guard, the held-DP ledger rule, and
  `Ante.Binder`'s headliner path:
  - an owner whose binder leads with `stakes:` in order;
  - each lead drawn only from the printings that fit the tier's range;
  - the deck's ever-Forbidden cards weighted 3×.
- **Headliner records:** `Headliner` (`handle:`, `deck:`, `table:`, `list:`, `over:`, `pieces:`, `stakes:`, `ready:`).
  `AlleyData.Problems` validates a ready headliner's Main and Extra under `list:` with the `over:` copies counted.
- **The Unlisted binder** (`Unlisted.Add`, `KeyOf`, `DeckOf`, `Contraband`) and posters (`Posters.All`).
- **Brackets:** 8 or 16 players, the Bo3 final, `Brackets.CreateAlley`, `BracketField`'s `field:` lines ("you <seed>",
  "<character> [deck] [seed]", "splash <n> <table> | cleared"), `Finish` with Contraband Packs, `AlleyRecord` and the
  scenes' catch-up.
- **The rules:** `Alley.Rules(list)` (`FormatRules { List, Pool = "", Passes = 0, Forced = true }`), the versus
  refusal, the deck editor opened with alley rules, and `TurnCap` (turn 40, higher LP wins).
- **Harness:**
  - `--alley-study` (authored tables with `--seat`), `--endless-test`, `--bracket-sim` (the `AlleySim` rows and the
    "champion's best deck" rule: the highest-rated deck legal under the table's list, as built), `--arbitrage` (the
    alley's loops), `--campaign-test` (AlleyTest), `--alley-probe`;
  - `--rate-decks`, `--playbook-ab`, `--deck-ab`, `--turn1`, `--playbook-test`, `--combo-goldfish`, `--ocg-imports`;
  - `tools/build_deck.py` (recipe text to .ydk), `tools/fetch_banlists.py` (Format Library lists to
    lflists/history.lflist.conf).
- **Unity:**
  - `CampaignScreen.Alley.cs` (map nodes, the authored-table panel Nyx's uses, the Encore button),
    `DuelController.Alley.cs` (versus, stakes, quit-as-loss), `CampaignState.Alley.cs` (`AlleyContext`, `AlleyRules`);
  - `BracketScreen.cs` and `DuelController.Brackets.cs` (16 slots, Bo3 pips, the chalkboard), `VanScreen.cs`;
  - `-alleycheck` steps 1-15 and `-themecheck`;
  - `ProjectSetup.CopyFiles` copies `decks/**/*.ydk` recursively, so a new `decks/no-limits/` folder ships with no
    setup change.
- **Decks in the game:**
  - the wave-1 bases (decks/history): snake-eye 1612, ryzeal 1867 (Mitsurugi Ryzeal, Feb 2025), k9-vanquish-soul
    1673, tenpai-dragon 1675, branded-despia 1830, yubel 1653;
  - the wave-2 bases: maliss 1392 (no playbook), light-and-darkness 1768 (the YCS Houston 2026 winner, the peak list
    itself);
  - mitsurugi 1742 (a 2026 Azamina/LADR hybrid, not the 2025 peak);
  - the game's top deck: tearlaments 1947.

## 2. Findings in the code (read before building)

- **N1. There's no tier slot, and a new tier silently plays at Den stakes.**
  - `AlleyTier` is `{ Street, Den, Orvel, Nyx }` (BackAlley.cs:12).
  - `Ante.StakeFor`, `Range` and `Conditions` end in `_ => Den` (Ante.cs:90-110), and Unity's `TierName` ends in
    `_ => "Den"` (CampaignScreen.Alley.cs:300).
  - So `AlleyTier.NoLimits` needs a case in each, or it plays at 1,000 DP with no error. `tier: nolimits` parses by
    name (`Enum.TryParse(..., true)`). The tier is never saved as a number.
- **N2. Authored seats can't be pool-drawn, and their binders are hard-wired to Nyx.**
  - `DrawNight`'s authored branch seats one fixed deck per seat (BackAlley.cs:653-664).
  - An authored seat's binder is `t.Tier == Orvel ? "orvel" : $"nyx:{seat}"` (BackAlley.cs:814), and
    `Ante.TierOf("nyx:...")` is Nyx (Ante.cs:116).
  - An authored seat's binder draws no contraband weight: `DeckFor` reads `o.Added`, which is empty for authored seats
    (Ante.cs:141-142).
  - **Decision:** a peak build is a *headliner-like record* (a `build:` block), not an authored seat. Its pilot is the
    character, its binder is `nl:<pilot>`, and its seat kinds are new (`pool`, `champion`). That reuses the headliner
    paths (stakes lead, contraband weight, closer, wildcard, encore, Unlisted) with small branches.
- **N3. An authored seat's panel Elo is its recipe's rating plus the splash gain of the table it opens from**
  (BackAlley.cs:811-813).
  - A peak build would show its base's Elo (Nyx's table has no `splash-elo`, so +0).
  - The headliner branch reads `Rating(o.Base)` first (BackAlley.cs:823), so builds rated by name show the right Elo
    once their ratings load (N4).
- **N4. `--rate-decks` would swallow the new decks.**
  - `DeckRating.DeckFiles` skips only decks/alley/ (DeckRating.cs:33-38). A full re-rate with decks/no-limits/ in it
    rates 101+ decks and shifts the 90-deck pool every Elo in the game is measured against.
  - **Needed:** an anchored rater (the pool frozen, only new decks move) and decks/no-limits/ excluded from
    `--rate-decks` (section 3, P3).
- **N5. Brackets refuse unrated named entrants.** `AlleyData.Problems` needs every `field:` character's deck rated
  (BackAlley.cs:383-384).
  - Orvel's `orvels-house` and Nyx's `gravekeepers-contraband` are unrated (decks/alley), so the Invitational's alley
    names need anchored ratings too.
  - That's safe now: C review fix 1 keys the exclusion exemption on `as-built`, not on a rating.
- **N6. Adding lists moves their neighbours' formats.**
  - `DeckRules.FormatEnd` is "the next dated TCG list" (DeckRules.cs:59-65). Inserting TCG September 2025 cuts TCG
    April 2025's format from 2026-09 to 2025-09.
  - Library cups set `pool:` explicitly (campaign/library.txt), so they don't move.
  - What can move: `DeckBuildingTest`'s history-release check (DeckBuildingTest.cs:49-55), the deck editor's pool end
    (DeckEditor.cs:184), and era-limited circuits (CampaignState.cs:434). These must be re-run, not assumed.
- **N7. Hard-coded counts in the checks.** AlleyTest.cs pins `Excluded.Length == 33` (line 219), the Contraband Pack
  pool at 231 (line 993), and the shelf at 429 items from 147 cards (line 1057).
  - The BossOnly additions take the pool to 229, and the missing lists may add cards.
  - Recount and update them in the same chunk that changes the data, or `--campaign-test` fails.
- **N8. Bracket kinds are hard-coded to two:**
  - `IsAlleyKind`, `EntryFor`, `PayFor`, `PacksFor`, `PayLedger`, `EntryLedger` (Brackets.cs:286-299);
  - `AlleySeed`, which salts `kind == MidnightKind ? 1 : 0`, so a third kind would share the Brawl's seed stream
    (Brackets.cs:425-426);
  - `Finish`'s token, which is Midnight-only (Brackets.cs:761);
  - `AlleyData.Problems` (`brawl` or `midnight`, else the Open's numbers: BackAlley.cs:351-361), `BackAlley.Triggers`
    and `IsVoiceHook`;
  - Unity: `AlleyBracket ? AlleyRules` (DuelController.Brackets.cs:97, 290).
- **N9. Mind Scan can't be played well.** `SmartDuelist.AnnounceCard` declares the most-seen card among the opponent's
  public field, GY and banished cards (SmartDuelist.cs:1148-1161). It never looks at a revealed hand, which is the
  point of Mind Scan.
- **N10. The headliner check skips the exclusions.**
  - `AlleyData.Problems` validates a headliner under `list:` and `over:` but never checks `Never` or `BossOnly` cards
    (Yata Mags runs Fiber Jar).
  - Builds need that check: k9-vanquish-soul's Extra Deck runs Number 67: Pair-a-Dice Smasher, a `Never` card.
- **N11. Shared playbooks are global.** `Playbooks.ForDeck` keys by recipe. A rule added to `SnakeEyePlaybook` also
  plays for snake-eye in its circuits, Library cups and the Brawl. Section 7 makes additions additive-only.
- **N12. `#source` reaches the player.**
  - The deck editor's story panel prints `#source` (DeckEditor.cs:1797).
  - `Unlisted.DeckOf` clones an authored deck with its notes, so "Open as a new deck" would show a real player's name
    beside a fictional pilot.
  - Peak builds carry no `#source`. Provenance goes in decks/no-limits/SOURCES.md.
- **N13. Deck names are global.** `DeckLibrary` keys built-in decks by file name across every folder, so peak-build
  files need a prefix (`nl-`) not used in decks/history.
- **N14. Some restored cards can't be staked.** `Ante.Candidates` iterates printings (Ante.cs:218-233), so a card with
  no printing, or none worth the floor, can't lead.
  - Toon World the Perfect World has no printing (OCG-only).
  - Sangen Summoning and Chundra top out at an Ultra slab 10 (1,500).
  - Branded Expulsion is Common only (240).

  NO-LIMITS-ECONOMY 1.2 lists the leads that fit; section 5 uses them.
- **N15. Moth already sells modern contraband.** His shelf, used tab and the Contraband Pack draw the era-9999 pool: 231
  cards, 43 of them first printed 2020+, including Original Sinful Spoils, Lacrima, Noroi, Rotary, White Binder,
  Albion, Habakiri, Souls and Dragoon. So the table isn't the *only* source. It's the steady, premium one (NO-LIMITS-ECONOMY 4).
- **N16. Don't add an `Alley.Events` entry.** The plan suggests one at era 2026, but `--alley-gen`, `--alley-rate` and
  AlleyTest's 1,000-seed splash check iterate `Alley.Events`, and an event with no curated list or bases fails them.
  No Limits never splashes, so the table carries its own `elo:` window instead (section 4).
- **N17. The Brawl fix as planned doesn't reach its target.** An Elo model of the Brawl (title odds of a champion's
  deck, the field as `BracketField` seeds it) matches C1's measured 70-79% for today's fields. It puts four
  out-of-towners on as-built decks at **57-61%**, still over 45%, and drops a typical (1,550) player from 24% to 9%.
  Section 8 has the fix that models at 42-46%.
- **N18. Junia's Snake-Eye is the weakest wave-1 base (1,612).** At the +250 Elo cap she rates about 1,860, against a
  reference deck of 1,947: the player wins about 62% at the end chair, not 50% (section 4.7).

## 3. Prerequisites

All in Phase NL-0 (section 14). Nothing in NL-1 builds without them.

**P1. The missing TCG lists.** lflists/history.lflist.conf has nothing between TCG April 2025 and TCG September 2026,
and no TCG January 2024. Add five lists, named in the file's convention:

| List | In effect | Needed by | Source (META-RESEARCH section 9) |
|---|---|---|---|
| `TCG January 2024` | 1 Jan 2024 | the pool's completeness (P2 of the window) | yugiohcardguide 2024-01-01 |
| `TCG September 2025` | 15 Sep 2025 | the pool; Kewl-era and Maliss limits | ygorganization.com/goodnightmoon |
| `TCG October 2025` | 27 Oct 2025 | **Augie's build (`list:`)** | yugiohcardguide 2025-10-27 |
| `TCG February 2026` | 2 Feb 2026 | the pool (White Binder Forbidden from here) | yugiohcardguide 2026-02-02 |
| `TCG May 2026` | 18 May 2026 | **Imogen's and Remy's builds (`list:`)**; Noroi Forbidden from here | ygorganization.com/prematurekewltunenerfs |

- **How:**
  - Try `tools/fetch_banlists.py` first (Format Library's formats, if it has these months).
  - Otherwise build each list as the previous list plus the cited changes, with a small `--list` mode added to the
    script (manual tables are acceptable if marked in a comment).
- **Chain check (new, in DeckBuildingTest):** each new list differs from the one before by exactly the changes its
  source names, and TCG September 2026 (already in the file) differs from the new TCG May 2026 by exactly the 21 Sep
  2026 changes on Konami's page. That catches a wrong secondary source.
- **After adding:**
  - re-run `--deckbuilding-test` (N6: history decks under their `#banlist`'s new format end);
  - recount `Alley.Contraband` at era 9999 and update AlleyTest's pinned counts (N7);
  - re-run `--alley-gen`: the curated alley.txt lists are filtered to the pool, so splash decks only change if a newly
    pooled card is curated (expected: none).

**P2. BossOnly additions.**
- Add "Djinn Releaser of Rituals" (8903700) and "Archnemeses Protos" (6728559) to `Alley.Excluded` and `Alley.BossOnly`
  (`Never ∪ BossOnly = Excluded` must stay a disjoint union).
- **Effects:** both go to 0 in the Alley list and the No Limits list. The contraband pool drops 2 (231 to 229 before
  P1). Moth's shelf and the used tab lose their printings.
- No deck, alley.txt line or authored seat runs either (checked: no .ydk under decks/ has either code).
- Update the pinned 33 / 231 / 429 / 147 counts (N7).

**P3. The anchored rater (`--rate-anchored`).**
- **New `src/Ygo.Harness/PeakRating.cs`:**
  `--rate-anchored <folder>[,<folder>...] [--rounds N] [--skilled] [--write]`.
  - Each deck in the folders plays N rounds (default 300) against pool decks drawn from campaign/deck-ratings.txt
    within ±250 of its running estimate, with the same bot as `--rate-decks` (playbook on; `--skilled` for the planner),
    first player alternating.
  - Only the new decks' Elo moves; the pool's is frozen.
  - It prints lines in `--rate-decks`' format, so `DeckRatings.Parse` reads them. `--write` writes
    campaign/peak-ratings.txt.
- **Calibration check:** re-rating 5 pool decks anchored (as if new) lands within ±40 of their pool rating.
- **Rated with it:**
  - decks/no-limits/ (every build and Brawl deck);
  - decks/alley/ (orvels-house and gravekeepers-contraband, for the Invitational's field, N5);
  - decks/history/kewl-tune.ydk until the next full `--rate-decks` (section 5).
- `DeckRating.DeckFiles` also skips decks/no-limits/ (N4).
- `DeckRatings` gains `Load(params string[] paths)` (later files win) and `PeakFileName = "peak-ratings.txt"`. The app
  (CampaignState.cs:207) and `AlleyTest.Load` merge both files.

**P4. The real lists.**
- Each build starts from the real list of its peak month (section 5's sources).
- Lists are written as readable recipes in `decks/recipes/no-limits.txt`, built into `decks/no-limits/*.ydk` with
  `tools/build_deck.py`, and credited in `decks/no-limits/SOURCES.md` (N12).
- Where a source can't be fetched, the build falls back to its decks/history base plus its peak month's engine counts,
  and SOURCES.md says so.

**P5. Magnificent Maestros (13 Nov 2026), for wave 2.**
- Today `data/ygoprodeck/cardinfo.json` lists MAMS only for Blue-Eyes Toon Dragon and Toon Dark Magician Girl.
- When the set's data lands:
  - refresh cardinfo.json and cardsets.json, then `--export-printings` (printings.bin);
  - refresh the BabelCDB names and `ot` flags (Konami's TCG name is "Blue-Eyes Ultimate Toon Dragon"; the cdb has
    "Blue-Eyes Toon Ultimate Dragon"; alley-events.txt names cards by the cdb, so `AlleyData.Problems` catches a rename);
  - re-run `--ocg-imports`: it will probably drop the RV01 cards from the import packs. The shop then needs MAMS on a
    shelf (the shop's owner, not this spec).
- Until then, Toon is an authored build of OCG-only cards (section 6). P5 doesn't block wave 2's build, only Perfect
  World as a stake and the player's shop route.

## 4. The No Limits table

### 4.1 Unlock and place

- **Where:** the map node after Nyx's Table (data order), "No Limits". Its lock line: "Behind Nyx's wall".
- **When:** the table opens with the `nolimits` event, NO-LIMITS-STORY's timing (its continuity 1), not on Nyx's clear
  itself.
  - New: `BackAlley.WallDue(profile, data)` is true when Nyx's Table is cleared, the `alley-closing` scene has been
    seen, and the `nolimits` key isn't in `profile.Seen`.
  - The caller checks it each time the Back Alley tab is shown (CampaignScreen, next to `alleyvisit`) and at load (the
    catch-up for saves already past it). It raises `nolimits` and adds `nolimits` to `profile.Seen` (constant
    `BackAlley.WallEvent`).
  - `IsUnlocked` for a `nolimits`-tier table needs its `opens:` table cleared **and** `profile.Seen` holding
    `WallEvent`. No save field.
- **Data:** `opens: Nyx's Table` plus `gate: nolimits` (a new table key: a `profile.Seen` key the table also needs).

### 4.2 The player's rules: no list

- New `Alley.NoLimitsList(cards)`: `Banlist { Name = "No Limits", Limits = the 35 Excluded at 0 }`, nothing else. Three
  of anything (DeckRules caps at 3), 40-60 Main, up to 15 Extra.
- `Alley.Rules(noLimitsList)` gives the format: no pool, no Ban Passes, forced. No loaners (O3).
- `AlleyContext.NoLimits` (new field) holds the list. CampaignState builds it beside the Alley list.
- `BackAlley.RulesFor(data, table, ctx)` returns the No Limits rules for a `nolimits` table and the Alley rules
  otherwise. Every caller that uses `AlleyRules` for a table today goes through it (the versus check, the deck editor
  from the panel, the `-alleycheck` legal-deck step).
- **No leaks:** the No Limits list never applies at another table, a cup, a circuit or a stamp. The Alley list never
  applies at No Limits. A deck legal only under No Limits (2 Pot of Greed) is refused at Dumpster Row. AlleyTest
  checks both ways.
- The rules chip: "No list · three of anything · everybody gets to play · turn 40 · higher LP wins" (the third phrase
  is Junia's rule for `Never`/`BossOnly`; NO-LIMITS-STORY's status line).

### 4.3 The CPUs' peak builds

- Authored decks in **decks/no-limits/** (`nl-<deck>.ydk`), played as written at skill 1.0. CPUs are exempt from lists,
  as everywhere.
- Each deck carries `#era`, `#tier`, `#title`, `#about`, `#why`, `#banlist <its peak list>`, and `#recipe <the
  playbook's key>` (section 7). No `#source` (N12).
- Each is described by a `build:` block in alley-events.txt (section 10). Its id is the **pilot's id**
  (NO-LIMITS-STORY's: `junia`, `remy`...): the pilot is the character, as a headliner is.
- **Era cap:** none at this table; No Limits never splashes. The "2026 cap" holds by construction:
  - builds are authored and play only at No Limits and the Invitational;
  - the Brawl decks' added cards are checked against the Basement's 2014 era (section 8);
  - the lower tables keep their `FromEra` checks.

### 4.4 Seats and nights

```
table: No Limits
id: no-limits
tier: nolimits
opens: Nyx's Table
gate: nolimits
elo: 1840-1990                  # the builds' rating window (section 4.7); AlleyData.Problems checks ready builds
pool: anselm, tova, augie, sasha, imogen, rue, lux, margo, inka
champions: remy, junia          # the end chair: the first ready unbeaten closes, the other wildcards
seat: 1 pool | skill 1.0
seat: 2 pool | skill 1.0
seat: 3 pool | skill 1.0
seat: 4 champion | skill 1.0
mat: no-limits
music: no-limits
ready: no                       # yes once the ship gate holds (section 14)
```

- **New seat kinds** (`AlleySeatSpec.Kind`): `pool` and `champion`.
- **`champions:`** parses into `AlleyTable.Headliners`, so `Unbeaten`, `Encore` and `EncoreOpen` work unchanged.
  **`pool:`** is the new `AlleyTable.Pool`. Both name `build:` ids.
- **Seat 4, the end chair** (NO-LIMITS-STORY's word; the code says champion):
  - The first ready, unbeaten champion in `champions:` order closes.
  - **Different from a regular table:** there's no 50/50 between two unbeaten champions. NO-LIMITS-STORY's owner
    question 2 wants Remy to take the chair once he's ready and Junia to wildcard. Wave 1: Remy is `ready: no`, so
    Junia closes every night until beaten.
  - **Wildcard:** the other ready, unbeaten champion takes one pool seat with `WildcardChance` (1/3; its seat by the
    night's seed).
  - **None unbeaten:** seat 4 draws a fourth build from the pool (rule below), and the Encore button opens (both
    champions beaten). An encore is the existing one-seat night at the table's stake, from the champion's own binder.
- **Seats 1-3 (the pool), drawn by rotation.** Deterministic and saved, with no new save field:
  1. The table's order: the ready, non-champion pool builds, shuffled once by `Hash.Mix(salt, 0x4E11 ^ table)`.
  2. Night k takes builds from position `3k mod n` onward, in order, skipping:
     - a build already seated tonight;
     - the wildcard;
     - a build sharing a `family:` tag with another of seats 1-3 or the wildcard (the end chair is exempt; see below).
  3. If the skips leave a seat empty, the family rule is relaxed, then repeats are allowed.

  So a fixed pool rotates: every build sits once before any sits twice. When wave 2 grows the pool, future nights
  re-rotate, and tonight's saved lineup never changes.
- **Why the end chair is exempt from families:** the wave-1 end chair (Junia) is tagged `fiendsmith`, as are Rue and
  Tova. Counting it would keep Rue and Tova out of every night until Junia is beaten. Families still keep two Fiendsmith
  builds, or Tova and Anselm (both `ryzeal`), off seats 1-3 together.
- **Night length, first clear, repeat nights:** as every table (ALLEY-SPEC section 6). Four wins in a night; a loss or
  draw leaves the seat as it was; quitting never rerolls; the first clear raises `alleyclear:No Limits`, pays
  `ClearTokens` and opens the Invitational.
- **The opponent:**
  - pool seat: `AlleyOpponent { Kind = "build", Character = <pilot>, Base = <deck>, Handle = <handle:> }`;
  - champion: `Kind = "headliner"` (or `"wildcard"`, `"encore"`), the same fields;
  - `AlleyOpponent.IsBuild` (new) is true for any of them whose character is a `build:` id;
  - `IsHeadliner` is unchanged, plus `"build"`, so `Showcase`, `ShowcaseContraband`, `Record`'s met and beaten counts
    and `Unlisted` take the headliner path.
- **Skill, Elo, binder** (`Complete`, the headliner branch):
  - skill: the profile's (1.0);
  - Elo: `Rating(o.Base)` from the merged ratings (P3);
  - binder: **`nl:<pilot>`** for builds (BackAlley.cs:824 gains the branch).

### 4.5 Stakes (NO-LIMITS-ECONOMY sections 1-2)

| | No Limits |
|---|---|
| Your stake | `NoLimitsStake` [P] 2,500 DP, held (back on a win, a draw or the guard; lost on a loss or a quit). No card |
| The CPU's card | `StakeFloor` × 2,500 to `NoLimitsCap` [P] 10,000, at `NoLimitsGrades` [P] (Raw 8 15%, Raw 9 15%, slab 9 35%, slab 10 35%) |
| The binder | `nl:<pilot>`, one for good (never dropped at a night's end): 12 cards from the build's deck, its ever-Forbidden cards 3×, led by `stakes:` in order |
| The restock | `NoLimitsRestock` [P] on: when a night is drawn, each seated pilot's missing lead cards go back into its binder, behind the leads still there (one copy of each, a new draw from the binder's seeded stream) |
| The guard | `NoLimitsGuardTurns` [P] 1: a loss with `Result.Turns <= 1` while the CPU went first gives the stake back |

- **Code:**
  - `Ante.StakeFor`, `Range` and `Conditions` gain `NoLimits` cases (N1).
  - `Ante.TierOf`: `nl:` owners are `NoLimits`.
  - `Ante.DeckFor`: `nl:` takes the headliner path: the build's deck from its `build:` block, its `stakes:` as leads,
    and contraband = its deck ∩ `ctx.Pool(9999)`.
  - **The restock** is `Ante.Restock(profile, owner, ctx)`, called from `DrawNight` for each build it seats. A night is
    drawn exactly once (its lineup is saved), so the restock runs once a night with no new save field. An encore isn't
    a night and doesn't restock.
  - `BackAlley.Record` uses `NoLimitsGuardTurns` for the first-turn guard at this tier (`turns <= guard`).
- **Leads** (NO-LIMITS-ECONOMY 1.2; a lead that fits no printing is skipped, no exemption): the `stakes:` line of each
  build in section 5. Where a build's own banned card can't fit (Tenpai, Toon, Branded Expulsion), the lead is the card
  the economy named.
- **Story:** the first restored card you win (any `stakes:` card of a build that's also in its `restored:`) raises
  `restored` once (`AlleyResult.Events`). Its `{card}` is that card.

### 4.6 Duel rewards

As every alley table:
- 0 DP (`DuelDp`), EXP at the Library's rate, level-ups in tokens (`payDp: false`);
- `NoteResult("alley:no-limits.<seat>")`;
- a win counted in `Profile.Wins` but never in `WinsAdded`;
- the stake is the prize.

New story events from `Record` for builds:
- `pilotbeat:<pilot>` replaces `headlinerbeat:` on the first win;
- `restored`, as in 4.5.

### 4.7 Targets and how they're measured

- **The targets** (META-DECKS Appendix A): with your best No Limits deck you win **55%** at seats 1-3 (over the drawn
  nights) and **50%** at the end chair. **Any single build under 45% or over 65% fails.**
- **"Your best No Limits deck" (the reference)** is defined the way `--bracket-sim` defines a champion's best deck: the
  highest-rated rated deck that's legal under the No Limits list as built. Today that's **tearlaments (1,947)**.
  - The study also prints two comparison rows: the QA save's best (`--option`, Fire King Snake-Eye, which met Orvel's
    and Nyx's targets) and the mirror (each build against the end chair's build).
  - The QA deck at 1,654 would win about 25-35% here. No Limits is the table where you need a 2026-strength deck,
    which No Limits rules let you build. Owner decision 1.
- **What that implies for ratings** (Elo as a guide; the study is the gate): with the reference at R,
  - seats 1-3 sit in **R − 110 to R + 35** (65% to 45%), mean about R − 35;
  - the end chair sits at about R ± 35.

  Today that's a window of 1,840-1,985 and an end chair near 1,947 (the table's `elo:` line, re-set if the reference
  changes).
- **Gain over base:** each build rates **+100 to +250** over its decks/history base (Appendix A's splash rule, raised for
  restored cards). Over +250 means too many cards; take a pick out.
- **What the bases allow:** Junia 1,612 + 250 = 1,862 at most (N18); Rue 1,903; Sasha 1,925; Augie 1,923; Anselm and
  Tova (base ryzeal 1,867) up to 2,117; Imogen up to 2,080. So the weak bases need the full peak list and every pick,
  and the strong ones need few.
  - **Junia's end chair can't reach 50%.** Section 16, decision 2: judge her chair against the 45-65% fail band only,
    until Remy takes it.
- **Measured by `--alley-study --only "No Limits"`** (section 14, NL-1 C3):
  - every ready build, pool and champion, played directly at 10 × `--games` duels each (alternating first, the turn
    cap), each against the 45-65% band;
  - the pool's mean over 20 drawn nights, against 55% ± 5;
  - the end chair against 50% ± 5 (Remy) or the band (Junia);
  - your deck made legal for the **No Limits** list, not the Alley list.
- **Speed gate** (Appendix A), per build against its base measured the same way:
  - average winning turn at least 3, and at most 1 turn faster than its base;
  - wins by turn 2 at most max(10%, the base's rate + 5 points). Tenpai goes second and wins on turn 2 by design.
  - If the player loses more than 10% of duels by turn 2, NO-LIMITS-ECONOMY's lever is `NoLimitsGuardTurns` 2.
- **Endless gate:** `--endless-test` with every build and Brawl deck: prompt-cap stops under 0.5%, decided at turn 40
  under 2%, draws under 1%.

## 5. The peak builds

### 5.1 Rules for every build (Appendix A 5, Appendix B; checked by `AlleyData.Problems` unless noted)

1. **The real list of its peak month,** from section 5.3's source. `list:` is that month's TCG list (it must exist:
   P1).
2. **Its own banned cards back at 3** (`restored:`). Most were legal in their peak month, so they need no `over:`. A
   card the peak list limits goes in `over:` at the deck's count. Extra Deck cards stay at the list's count (1-2).
3. **2-4 contraband picks** (`over:` = the picks plus any restored card above its list). At most one generic "power"
   card per build:
   - the set: Pot of Greed, Graceful Charity, Harpie's Feather Duster, Raigeki, Heavy Storm, Dark Hole, Monster Reborn,
     Change of Heart;
   - a const list, `Alley.PowerCards`.
4. **No `Excluded` card at all,** `Never` or `BossOnly` (N10). Builds have no `allow:`.
5. **No two builds share more than 2 picks** (`over:` minus `restored:`).
6. **Fiendsmith in at most 3 builds** (the `fiendsmith` family tag).
7. **The deck validates under `list:` with `over:` counted** (the headliner rule).
   - `restored:` and `pieces:` cards are in the deck.
   - `pieces:` leads with a restored card.
   - `stakes:` leads with a card that fits the No Limits range at some printing. The check uses `Ante.Candidates`'
     rule, so a lead that can never be staked fails here, not silently.
8. **Rated** (P3), inside the table's `elo:` window when `ready: yes`, and at most +250 over its `base:` (reported
   below +100).
9. **Gates before `ready: yes`** (the harness, section 14):
   - its playbook names every restored card and pick (`--peak-check`: each is activated, summoned or used in at least
     60% [P] of the games it's drawn);
   - `--playbook-ab <build> <base>`: the build with its playbook beats its base, and the playbook is worth more than 0;
   - `--turn1` builds its board;
   - `--endless-test` and the speed gate pass.

### 5.2 The eleven

Ids and handles are NO-LIMITS-STORY's. Families drive the night draw (4.4).

| Pilot (id) | Handle | Deck file | Wave | Base (`base:`) | Families | Seat |
|---|---|---|---|---|---|---|
| Junia Calder (`junia`) | Spoils | nl-snake-eye | 1 | snake-eye | snake-eye, fiendsmith | end chair (wave 1); wildcard once Remy is ready |
| Anselm Quist (`anselm`) | Habakiri | nl-mitsurugi | 1 | ryzeal | mitsurugi, ryzeal | pool |
| Tova Kincaid (`tova`) | Detonator | nl-ryzeal | 1 | ryzeal | ryzeal, fiendsmith | pool |
| Augie Marr (`augie`) | Noroi | nl-vanquish-soul | 1 | k9-vanquish-soul | k9 | pool |
| Sasha Penhallow (`sasha`) | Sangen | nl-tenpai | 1 | tenpai-dragon | tenpai | pool |
| Imogen Starling (`imogen`) | Albion | nl-branded | 1 | branded-despia | branded | pool |
| Rue Marchbanks (`rue`) | Lacrima | nl-yubel | 1 | yubel | yubel, fiendsmith | pool |
| Remy Okoro (`remy`) | Rotary | nl-kewl-tune | 2 | kewl-tune (new, P3) | kewl-tune | end chair |
| Margo Kessel (`margo`) | Dormouse | nl-maliss | 2 | maliss | maliss | pool |
| Lux Ferrand (`lux`) | Dragoon | nl-ladr | 2 | light-and-darkness | ladr | pool |
| Inka Moll (`inka`) | Bookmark | nl-toon | 2 | (none) | toon | pool |

- **Wave 1** ships with 6 pool builds and Junia, and the table opens.
- **Wave 2** adds Remy (who takes the end chair), Margo, Lux and Inka, and ships the Invitational.
- The cap stays 13. Reserves (Dracotail, Yummy, Elfnote) are unchanged from META-DECKS.

### 5.3 Build by build

Picks are Appendix B's, narrowed to the final set; "dropped" says why the rest went. Cuts come from the peak list's
non-engine slots (its hand-trap and flex block), never its engine, and the deck designer names them in the `#why`
note. "Playbook" is section 7's work.

**Junia (`junia`), nl-snake-eye: Fiendsmith Snake-Eye, summer 2024.**
- **Source:** the NAWCQ 2024 Fiendsmith Snake-Eye list (ygoprodeck `fiendsmith-snake-eye-520593`, as read by the
  research; confirm it's a NAWCQ top-cut list). `list: TCG April 2024`.
- **Restored:** Original Sinful Spoils - Snake-Eye ×3, Snake-Eye Ash ×3, Snake-Eyes Poplar ×3, Fiendsmith's Lacrima
  (Extra, at the list's count). All legal in April 2024, so no `over:`.
- **Picks:** Union Carrier (Extra), Card of Safe Return, Soul Charge, Blaster, Dragon Ruler of Infernos. The fullest
  build, for the end chair; the most it can be (N18).
  - Dropped: Dark Hole (the Fire King build's only).
- **Cuts:** 3 Main from the hand-trap block, 1 Extra (Link Spider).
- `pieces: Original Sinful Spoils - Snake-Eye, Fiendsmith's Lacrima, Union Carrier`
- `stakes: Original Sinful Spoils - Snake-Eye, Fiendsmith's Lacrima`
- **Playbook:** `#recipe snake-eye`, plus the Fiendsmith engine and the four picks (7.2).

**Anselm (`anselm`), nl-mitsurugi: Ryzeal Mitsurugi, summer 2025** (NO-LIMITS-STORY's deck).
- **Source:** a P6 (7 Apr - 14 Sep 2025) premier Mitsurugi top-cut list. YCS Providence (3 May 2025) winner first,
  else UK Nationals (14 Jun 2025). `list: TCG April 2025`.
- **Restored:** Ame no Habakiri no Mitsurugi ×3. Legal in April 2025, so no `over:`.
- **Picks:** Mass Driver, Pharaonic Advent, Sinister Serpent, King of the Feral Imps (Extra).
  - Dropped: Metamorphosis (the bot can't match a Level to Dragoon); Djinn (BossOnly, P2).
- **Cuts:** 3 Main flex, 1 Extra.
- `pieces: Ame no Habakiri no Mitsurugi, Mass Driver, Pharaonic Advent`
- `stakes: Ame no Habakiri no Mitsurugi`
- **Playbook:** `#recipe ryzeal`. The base ryzeal.ydk is itself Mitsurugi Ryzeal, and RyzealPlaybook knows the Mitsurugi
  side (`Murakumo`). Add Habakiri and the picks.

**Tova (`tova`), nl-ryzeal: Fiendsmith Ryzeal, winter 2024-25** (NO-LIMITS-STORY's deck).
- **Source:** the YCS Orlando (1 Feb 2025) winning Fiendsmith Ryzeal list (Konami counted 24 Fiendsmith Ryzeal in its
  Top 64). `list: TCG December 2024`.
- **Restored:** Ryzeal Detonator, Ext Ryzeal, Ice Ryzeal and Sword Ryzeal ×3 (all legal in December 2024).
- **Picks:** Fairy Tail - Snow, Lavalval Chain (Extra), Daigusto Emeral (Extra).
  - Held back: Pot of Greed. Ryzeal is the strongest base, and Appendix B's reason for it (the Alley list's Ext Ryzeal
    cut) doesn't apply here. It's the first card added if Tova measures under her band.
  - Dropped: Soul Charge (Junia's; rule 5's sharing budget).
- **Cuts:** 1 Main hand trap, 2 Extra (Gagaga Cowboy, Tornado Dragon if present).
- `pieces: Ryzeal Detonator, Ext Ryzeal, Lavalval Chain`
- `stakes: Ryzeal Detonator` (NO-LIMITS-ECONOMY: Ryzeal has no ever-Forbidden card of its own; Detonator, Limited, is
  restored to 3).
- **Playbook:** `#recipe ryzeal`, plus the Fiendsmith engine (shared helper, 7.2) and the picks.
- **Overlap with Anselm:** both run the Ryzeal engine. They're told apart by Fiendsmith against the Mitsurugi rituals,
  their restored cards and their picks, and they never share a night (family `ryzeal`). Section 16, decision 4 offers
  the alternative.

**Augie (`augie`), nl-vanquish-soul: K9 Vanquish Soul, late 2025.**
- **Source:** the YCS Bologna (22 Nov 2025) or NA Remote YCS (6 Dec 2025) winning list (ygoprodeck
  `k9-vanquish-soul-692109`: confirm the date). `list: TCG October 2025` (**P1**).
- **Restored:** K9-04 Noroi ×3, Vanquish Soul Razen ×3, Stake your Soul! ×3, Vanquish Soul Hollie Sue ×3, K9-66a Jokul
  ×3, "A Case for K9" ×3. The October 2025 list limits Razen and Stake your Soul! (Limited 15 Sep 2025), so they go in
  `over:`.
- **Picks:** Confiscation, Trap Dustshoot, Outer Entity Azathot (Extra).
  - Out: Number 67: Pair-a-Dice Smasher (`Never`; Azathot takes its slot).
  - Dropped: Archnemeses Protos (BossOnly, P2).
  - Two hand-strip cards: the cap.
- **Cuts:** 2 Main flex, Pair-a-Dice.
- `pieces: K9-04 Noroi, Confiscation, Outer Entity Azathot`
- `stakes: K9-04 Noroi`
- **Playbook:** `#recipe k9-vanquish-soul`, plus Noroi and the picks. Confiscation and Dustshoot use the known-hand view
  (7.3).

**Sasha (`sasha`), nl-tenpai: Tenpai Dragon, summer 2024.**
- **Source:** the EUWCQ 2024 (26 Jul 2024) winning list (ygoprodeck `tenpai-dragon-513387`). `list: TCG April 2024`.
- **Restored:** Sangen Summoning, Tenpai Dragon Chundra and Sangen Kaimen ×3 (legal then).
- **Picks:** Giant Trunade, Raigeki (the power card), Blaster, Dragon Ruler of Infernos, Burner, Dragon Ruler of Sparks.
  - Dropped: Cold Wave (its start-of-Main-Phase-1 window is untested for the bot, and with Trunade and Raigeki it's
    Appendix B's harshest line; cap 2 of the 3); Heavy Storm (symmetric; Trunade covers it).
- **Cuts:** 4 Main (hand traps, Dimension Shifter if run).
- `pieces: Sangen Summoning, Tenpai Dragon Chundra, Giant Trunade`
- `stakes: Giant Trunade` (NO-LIMITS-ECONOMY: no Tenpai card fits the 2,500 floor. Sangen Summoning and Chundra top out
  at 1,500, and by this spec's arithmetic Kaimen's Secret slab 10 is 2,400. Cold Wave, the economy's other choice, isn't
  in this build).
- **Playbook:** `#recipe tenpai-dragon` (goes second through `TenpaiDragonPlaybook.Deck`), plus Trunade before the
  Battle Phase, Raigeki and the Dragon Rulers.

**Imogen (`imogen`), nl-branded: Despia Branded, summer 2026.**
- **Source:** a P10 (18 May - 20 Sep 2026) Despia Branded list without the Light and Darkness Ritual package (EUWCQ
  2026 Top 4, or a 2026 National winner; ygoprodeck `despia-branded-719679`). `list: TCG May 2026` (**P1**).
- **Restored:** Albion the Branded Dragon (Extra, at the list's count), Branded Fusion ×3, Branded in High Spirits ×3,
  Branded Opening ×3; Branded Expulsion ×1-2 (NO-LIMITS-STORY lists it as Imogen's "never came back"; ever-Forbidden, so
  in `over:`). The May 2026 list limits Branded Fusion, so it goes in `over:` at 3.
- **Picks:** Eclipse Wyvern, Chaos Sorcerer.
  - Chaos Sorcerer needs 3 LIGHT and 3 DARK monsters. Eclipse Wyvern needs a Level 7+ LIGHT/DARK Dragon in the Main
    Deck. If the list fails either, swap it for Monster Reborn (the one power card).
  - Dropped: Black Luster Soldier - Envoy of the Beginning (Lux's; keeps Imogen and Lux apart, rule 5).
- **Cuts:** 3 Main flex.
- `pieces: Albion the Branded Dragon, Branded Expulsion, Eclipse Wyvern`
- `stakes: Albion the Branded Dragon` (Expulsion is Common only and never fits).
- **Playbook:** `#recipe branded-despia`, plus Expulsion and the picks.

**Rue (`rue`), nl-yubel: Fiendsmith Yubel, autumn 2024.**
- **Source:** the Worlds 2024 or YCS Lille (28 Sep 2024) Fiendsmith Yubel list (ygoprodeck
  `fiendsmith-kashtira-yubel-534552`). `list: TCG September 2024`.
- **Restored:** Fiendsmith's Lacrima (Extra; Forbidden in September 2024, so in `over:`), Phantom of Yubel at the
  list's count, Opening of the Spirit Gates ×3 (Limited then, so in `over:`).
- **Picks:** Sangan, Witch of the Black Forest.
  - Dropped: Grinder Golem (Inka's hook; rule 5).
- **Cuts:** 2 Main flex; Lacrima takes an Extra slot.
- `pieces: Fiendsmith's Lacrima, Phantom of Yubel, Sangan`
- `stakes: Fiendsmith's Lacrima`
- **Playbook:** `#recipe yubel`, plus the Fiendsmith's Lacrima (46640168) Fusion and Sangan and Witch's search targets.
  The playbook's `Lacrima` is the Crimson Tears (28803166), not this card.

**Remy (`remy`), nl-kewl-tune: Kewl Tune, summer 2026 (wave 2, the end chair).**
- **Source:** the Worlds 2026 winning list (ygoprodeck tournament `world-championship-2026-4894`; the research read
  `kewl-tune-733722`). Worlds used its own card pool, so any card that isn't TCG-legal or TCG-printed in 2026 takes the
  EUWCQ 2026 (11 Jul 2026) winner's count. `list: TCG May 2026` (**P1**).
- **Base:** the same list as built, at `decks/history/kewl-tune.ydk` (`#banlist TCG May 2026`), rated anchored (P3). It
  joins the 90-deck pool at the next full `--rate-decks`, and the 2025-26 Library cup can use it later.
- **Restored:** Kewl Tune Rotary ×3; Synchro Overtake ×3 (Limited in May 2026, so in `over:`).
- **Picks:** Naturia Rosewhip, Glow-Up Bulb, Jet Synchron, Destrudo the Lost Dragon's Frisson (all Tuners, so they pass
  the Tuner-only lock). The fullest build, for the end chair.
  - Crossout Designator, Triple Tactics Talent and Called by the Grave stay at the list's count: they aren't picks.
- **Cuts:** 4 hand traps (the Veiler and Ghost Ogre class).
- `pieces: Kewl Tune Rotary, Naturia Rosewhip, Glow-Up Bulb`
- `stakes: Kewl Tune Rotary`
- **Playbook:** new `KewlTunePlaybook` keyed `kewl-tune` (both decks). Engine spike first (7.4).

**Margo (`margo`), nl-maliss: @Ignister / Bystial Maliss, summer 2025 (wave 2).**
- **Source:** the South America WCQ (7 Jun 2025) or France Nationals (31 May 2025) winning list. `list: TCG April 2025`.
- **Restored:** Maliss <P> Dormouse ×3, Maliss <P> White Rabbit ×3, Maliss in Underground ×3 (Semi-Limited in April
  2025, so in `over:`), Maliss <Q> White Binder at the list's count.
- **Picks:** Return from the Different Dimension, Link Decoder (Extra), Protectcode Talker (Extra).
  - Dropped: Dimension Fusion (the same idea twice, and symmetric); Appointer of the Red Lotus (generic); Topologic
    Gumblar Dragon (random discards).
- **Cuts:** 1 Main flex, 2 Extra (Linguriboh, Link Disciple, Cyberse Wicckid as needed).
- `pieces: Maliss <Q> White Binder, Maliss <P> Dormouse, Return from the Different Dimension`
- `stakes: Maliss <Q> White Binder`
- **Playbook:** new `MalissPlaybook` keyed **`nl-maliss` only** (owner decision 4). The deck has no `#recipe` line, so
  it plays by its own name. The Link Age Open's and the Ryzeal cup's `maliss` stay without a playbook.

**Lux (`lux`), nl-ladr: Light and Darkness Ritual, October 2026 (wave 2).**
- **Source:** the YCS Houston 2026 winner, which is decks/history/light-and-darkness.ydk itself (the peak list).
  `list: TCG September 2026`.
- **Restored:** Magicians' Souls ×3, Azamina Ilia Silvia ×3, Red-Eyes Dark Dragoon and Zenna's Deceiving Doll Maidens
  (Extra, at the list's count), Griffoh at the list's count (all legal in September 2026).
- **Picks:** Black Luster Soldier - Envoy of the Beginning, Dark Magician of Chaos, Eclipse Wyvern.
  - Dropped: Chaos Emperor Dragon - Envoy of the End (a reset the bot fires at random; only with a playbook gate, a
    later option); Djinn (BossOnly).
- **Cuts:** Mind Shuffle, Spell Shattering Sword, 1 flex.
- `pieces: Magicians' Souls, Red-Eyes Dark Dragoon, Black Luster Soldier - Envoy of the Beginning`
- `stakes: Magicians' Souls, Red-Eyes Dark Dragoon`
- **Playbook:** `#recipe light-and-darkness`, plus the picks.
- Imogen and Lux share one pick (Eclipse Wyvern), within rule 5.

**Inka (`inka`), nl-toon: Toon, OCG summer 2026 (wave 2).** See section 6.
- **Source:** the Japan Championship 2026 winning list (ygoprodeck `toon-722468`). `list: TCG October 2026` (the list
  the deck arrives under in the TCG).
- **Restored:** Toon Table of Contents ×3 and Toon Bookmark ×3 (Limited in TCG October 2026, so in `over:`), Toon World
  the Perfect World ×3 (not on the TCG list).
- **Picks:** Grinder Golem, Witch of the Black Forest, Change of Heart, Harpie's Feather Duster (the power card).
  - Dropped: Sangan (Rue's; rule 5); Crush Card Virus; Confiscation (Mind Scan already opens the hand; at most one
    hand-strip, and none needed).
- **Cuts:** 3-4 hand traps.
- `pieces: Toon World the Perfect World, Toon Bookmark, Grinder Golem`
- `stakes: Toon Bookmark, Change of Heart` (NO-LIMITS-ECONOMY 1.5; Perfect World joins the front once P5's printings
  land).
- **Playbook:** new `ToonPlaybook` keyed `nl-toon`, plus Mind Scan's declaration (7.3).

### 5.4 Deck files

```
#created by Duel Circuit - No Limits: Fiendsmith Snake-Eye, summer 2024 (Junia "Spoils")
#title Fiendsmith Snake-Eye (2024, uncut)
#era 2024
#tier No Limits peak build: the end chair (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 5)
#about <what the deck did, never who played it (NO-LIMITS-STORY's rule)>
#why Peak list: <month>; restored: ...; picks: ...; cuts: .... Measured <date>: rated N (+g over snake-eye); the reference won x% of n.
#banlist TCG April 2024
#recipe snake-eye
#main ...
```

- decks/no-limits/SOURCES.md holds each list's provenance (site, deck id, event, placement). It's for the repo, never
  read by the game.
- The Brawl decks (section 8) live here too, as `nl-<deck>-brawl.ydk`.

## 6. Toon

- **The cards are OCG-only in the game today.** Eight engine cards have `ot` 1 and no TCG printing: Perfect World, Funny
  Dark Rabbit, Comic Cat, Evil Box, Faceless Mage, Mind Scan, Blue-Eyes Toon Ultimate Dragon and Perfectron.
  `PrintingIndex.FirstYear` counts TCG printings only, so it's 0 for all of them.
- **An authored build avoids every era check.**
  - Builds never splash, so `Alley.FromEra` and `Alley.Contraband` never see them.
  - `AlleyData.Problems` must not era-check a `build:` (it doesn't check headliners; keep it that way).
  - `DeckRules.Releases` doesn't judge cards with no TCG printing, so your own Toon deck isn't refused either.
- **What FirstYear 0 still touches:**
  1. **Stakes:** no printing means no candidate, so Toon leads with Toon Bookmark and Change of Heart (5.3) until P5.
  2. **Packs and the shelf:** the No Limits Pack pool (9.3) and the restored shelf slots (NO-LIMITS-ECONOMY 4) need
     printings, so Perfect World can't appear in them until P5.
  3. **Your copies:** the OCG import packs (OCG-ARCHET, OCG-MONSTE, OCG-SPELLS) until MAMS; after P5's `--ocg-imports`
     re-run, the shop's MAMS shelf.
  4. **"Open as a new deck"** from Inka's Unlisted page works the same way; the editor shows what you're missing.
- **Magnificent Maestros (13 Nov 2026):** P5's data refresh. Then:
  - re-check `AlleyData.Problems` (the Blue-Eyes Ultimate Toon Dragon rename);
  - add Perfect World to the front of Inka's `stakes:`;
  - re-measure Inka's binder (`--alley-probe`'s No Limits section).
- **The authored seat:** Inka is a pool build like the others. Appendix B's "authored seat" means she can't be generated,
  and no build is.
- **The OTK risk:** real but never first-turn (there's no Battle Phase on turn 1). The speed gate (4.7) measures it.
  Inka is fragile to losing Perfect World, and the player's Field Spell removal is the intended out: no Cold Wave or
  floodgate goes in to protect it.
- **Mind Scan's name declaration:** new logic (7.3).

## 7. Playbooks and engine gaps

### 7.1 The rule for shared playbooks (N11)

- **Wave-1 builds and Lux use their base's playbook through `#recipe <base>`.** Additions are **additive only**: new
  rules on cards the base doesn't run, never an edit to an existing rule.
  - A rule on a card the base doesn't hold never fires there, so circuits, Library cups, the Brawl and the Midnight Open
    are unchanged by construction.
  - **Check:** `--turn1 <base>` boards and a 200-duel `--playbook-ab <base> <its usual opponents>` match before and
    after, within noise.
  - An addition that has to change an existing rule goes in a derived class instead: unseal the base class, add
    `Nl<Base>Playbook : <Base>Playbook` keyed `nl-<deck>`, and drop the deck's `#recipe`.
- **Remy, Margo and Inka have no base playbook:**
  - `KewlTunePlaybook` (`kewl-tune`), shared with decks/history/kewl-tune.ydk;
  - `MalissPlaybook` (`nl-maliss` only);
  - `ToonPlaybook` (`nl-toon`).
- `Playbooks.Lookup` gains the three keys. Tenpai's going-second stays through `TenpaiDragonPlaybook.Deck`.

### 7.2 Additions, build by build

| Build | Playbook | What it must learn |
|---|---|---|
| Junia | SnakeEyePlaybook (+ the Fiendsmith helper) | **The Fiendsmith engine:** Engraver discards for Tract; Tract adds Lacrima the Crimson Tears or Lurrie; a LIGHT Fiend into Requiem; Requiem summons from the Deck; Sequence; Fiendsmith's Lacrima Fusion (46640168) and its revive and burn. **Union Carrier:** two FIRE monsters, equip Ash or Poplar from the Deck, only with Original Sinful Spoils in hand or set (a face-up card for it). **Card of Safe Return:** set it early. **Soul Charge:** revive at least 2 FIRE bodies for a Link climb, only at 4,000 LP or more. **Blaster:** discard with a FIRE to destroy; revive by banishing |
| Anselm | RyzealPlaybook | Habakiri (Mitsurugi side). **Mass Driver:** Tribute only monsters with a "when Tributed" trigger (Saji, Aramasa, Habakiri), never Murakumo or Dragoon (`Prefer`/`Avoid(Tributes, MassDriver, ...)`). **Pharaonic Advent:** Tribute a Mitsurugi Reptile to summon a Level 8 from the hand. **Sinister Serpent:** Tribute fodder. **King of the Feral Imps:** two Level 4 Reptiles, search a Reptile |
| Tova | RyzealPlaybook (+ the Fiendsmith helper) | Fiendsmith as Junia's. **Fairy Tail - Snow:** revive from the GY banishing 7, as Xyz material. **Lavalval Chain:** send a Ryzeal or Snow. **Daigusto Emeral:** recycle three and draw |
| Augie | K9VanquishSoulPlaybook | **Noroi:** Normal Summon, call a non-Machine K9 (Izuna, Lupis, Jokul). **Confiscation, Dustshoot:** pick by the known-hand view (7.3), hand traps first. **Azathot:** Rank 5 from three Level 5 K9s, before the opponent's main play |
| Sasha | TenpaiDragonPlaybook | **Giant Trunade** before the Battle Phase when the opponent has set Spells/Traps. **Raigeki** with 2+ opposing monsters or a negater up. **Blaster and Burner:** discard lines; Burner calls Blaster |
| Imogen | BrandedDespiaPlaybook | **Branded Expulsion:** Tribute an Albaz Fusion (Mirrorjade, Albion) to revive 2 non-Fusions, in response to removal or at the end of the opponent's turn. **Eclipse Wyvern:** the banish target. **Chaos Sorcerer:** banish a LIGHT and a DARK |
| Rue | YubelPlaybook | Fiendsmith's Lacrima from Tract and Sequence; its GY revive and burn. **Sangan, Witch:** search Spirit of Yubel, Samsara D Lotus or Engraver |
| Lux | LightAndDarknessPlaybook | **Black Luster Soldier:** banish a LIGHT and a DARK. **Eclipse Wyvern:** banished, add the target. **Dark Magician of Chaos:** recover the Ritual Spell |
| Remy | KewlTunePlaybook (new) | Synchro on both turns; Tuners from the hand as material; Rotary's extra Normal Summon and its material effect; Fydraulis Harmonia; Synchro Overtake; the four picks. `ChainWhen` for the opponent-turn Synchro |
| Margo | MalissPlaybook (new) | The Link combo (Dormouse, White Rabbit, Underground into White Binder, Accesscode and Firewall); banish-for-value ordering. **Return from the Different Dimension** only when at least 3 Maliss <P> are banished and the LP pays: their "if banished" effects fire again at the End Phase. **Link Decoder** recurs; **Protectcode Talker** protects the Link-4s |
| Inka | ToonPlaybook (new) | Appendix B 6.2's nine rules: Perfect World first, then its 3 searches; Rabbit's extra Normal Summon on a Toon World monster; Comic Cat as a quick effect (pull Toon Black Luster Soldier first); Evil Box with Toon Terror; Faceless Mage for Mind Scan; Mind Scan's name (7.3); Perfect World's banish dodge; Blue-Eyes Toon Ultimate Dragon's contact Fusion; **Grinder Golem only on a turn Comic Cat can Tribute it**, or with Blue-Eyes Toon Dragon in hand. Plus Change of Heart and Harpie's Feather Duster before the direct attacks |

- **The Fiendsmith helper:** `protected void FiendsmithEngine(Func<bool>? when = null)` on `Playbook`, adding the rules
  above, used by Junia and Tova.
  - YubelPlaybook keeps its own rules unchanged. It only adds the Fiendsmith's Lacrima lines, so it doesn't move.
  - The helper's rules name cards snake-eye's and ryzeal's as-built lists don't run, so 7.1 holds.

### 7.3 Mind Scan and the known hand (engine)

- `PlaybookView.OpponentKnownHand` (new): the codes of the opponent's hand cards that are public (revealed, or kept
  revealed by an effect), as `BoardState` tracks them.
  - **Spike first:** confirm `BoardState` keeps the codes of a public hand. If not, track `MSG_CONFIRM_CARDS` and the
    public-hand flag in `BoardState`.
- `Playbook.Declare(uint source, Func<PlaybookView, IEnumerable<uint>> rank)` (new): when an `AnnounceCardPrompt` comes
  from `source`, declare the first declarable ranked card.
- `SmartDuelist.AnnounceCard` asks the playbook first. Then a better fallback: the opponent's known hand ranked by
  `ThreatValue`, hand traps first; then today's "most seen".
- Toon's rule: Mind Scan names the most dangerous card in the revealed hand (a hand trap, then removal, then their
  boss).
- The same view drives Augie's Confiscation and Dustshoot picks: `Prefer` over known cards.

### 7.4 Engine spikes (before the playbooks)

- **Kewl Tune** (NL-2, step 1):
  - goldfish the base list with `--turn1 kewl-tune` and a duel log;
  - confirm the core offers Synchro Summons that use **Tuners from the hand**, and that the bot's material selection can
    pick them;
  - confirm a Kewl Tune quick effect can Synchro Summon on the opponent's turn through `ChainWhen`.

  If the material selection never offers hand cards, that's a `SmartDuelist` material-selection fix (an engine chunk).
- **Maliss:** run `--endless-test` on the base plus Return from the Different Dimension, to check the End Phase banish
  loop for prompt-cap stops before the playbook is written.
- **Grinder Golem and Comic Cat:** a scripted duel where Comic Cat Tributes the opponent's Grinder Golem (an
  opponent-controlled monster as cost), so the playbook's rule has something to fire.

## 8. The Alley Brawl's out-of-towners

- **Who:** Junia, Rue, Sasha and Augie (NO-LIMITS-STORY), the same characters as their No Limits pilots.
- **When they sit: once The Black Market is cleared.** NO-LIMITS-STORY wants the cast met before Nyx; The Black Market's
  clear opens Nyx's Table, so that still holds.
  - Before that, the Brawl is unchanged for a player still climbing. N17's model: out-of-towners from Dumpster Row's
    clear would take a typical player from 24% to 9%, under NO-LIMITS-ECONOMY's 20% "toll" line.
- **Their decks: legal, plus a Basement splash.** As built they don't fix the Brawl (N17: a champion's deck at 57-61%).
  Each out-of-towner's Brawl deck is **authored**, `decks/no-limits/nl-<deck>-brawl.ydk`:
  - their 2024-25 base (snake-eye, yubel, tenpai-dragon, k9-vanquish-soul) made legal for the Alley list with
    `Loaners.Adjust` (one copy of anything: NO-LIMITS-STORY's toploader fiction, its conflict 4);
  - 3 cards from alley.txt's Basement Game list (era 2014), picked as the splash builder would (`Alley.Pick` with a
    fixed seed, then frozen), Main Deck only;
  - rated anchored (P3).
  - `AlleyData.Problems` checks each: legal under the Alley list, its added cards in the Basement's pool, no `Excluded`
    card.
- **Seeding: one per round.** You're seed 1. The out-of-towners sit at seeds 8, 5, 2 and 3, so you meet one in the
  quarter-final, likely one in the semi-final and likely one in the final. The three regulars take seeds 4, 6 and 7 by
  Elo.
- **The model** (Elo odds, 20,000 brackets a row; `--bracket-sim` is the gate):

| Field after The Black Market's clear | 1,700 | 1,800 | 1,879 (late-Library best) | 1,896 (best in the Alley) |
|---|---|---|---|---|
| Today (7 Basement regulars) | 41% | 58% | 70% | 72% |
| 4 as built, seeded by Elo, + 3 Basement regulars | 27% | 44% | 57% | 60% |
| 4 with a Basement splash, seeded by Elo | 20% | 36% | 49% | 52% |
| **4 with a Basement splash at seeds 8 / 5 / 2 / 3 (recommended)** | about 15% | 29% | **42-43%** | **46%** |

- The last row sits at the top of the 30-45% band. If `--bracket-sim` reads over 45%, add a fourth Basement card to each
  Brawl deck (the next lever); never change the payouts (NO-LIMITS-ECONOMY 5).
- **Data:** a field line gains `| after: <table>`, meaning the line applies once that table is cleared.

```
bracket: Alley Brawl
...
field: you 1
field: junia nl-snake-eye-brawl 8 | after: The Black Market
field: augie nl-vanquish-soul-brawl 5 | after: The Black Market
field: rue nl-yubel-brawl 2 | after: The Black Market
field: sasha nl-tenpai-brawl 3 | after: The Black Market
field: splash 3 cleared
```

  - Until the table is cleared, each `after:` line becomes one more regular from the `splash ... cleared` source,
    unseeded (so the field is 7 regulars as today).
  - `AlleyData.Problems` counts CPUs the same in both states.
  - `BracketField` builds the named entrants as the Midnight Open's (character, deck, Elo from the merged ratings). Save
    lines are unchanged.
- **Story:**
  - `outoftowner` (the first Brawl field that shows one; raised when the bracket screen first shows it);
  - `outoftowner:<id>` (before your first Brawl match against that pilot);
  - voice hooks `outoftowner:intro|win|lose:<id>` and `mc:brawl:outoftowner`.
  - `BackAlley.OutOfTowners(bracket, data)` lists the named build pilots in a field.
- **Twin rule:** no peak build and its as-built base in one bracket. The Brawl has the base-derived Brawl decks only, and
  the Midnight Open has none of the four bases. AlleyData.Problems checks it for every alley bracket.

## 9. The No Limits Invitational

### 9.1 The bracket

```
bracket: No Limits Invitational
kind: invitational
size: 16
opens: No Limits
entry: 3000
final: bo3
pay: 1500 1000 600 300 100
packs: 4 3 2 1 0
tokens: 2 first
field: remy nl-kewl-tune 1
field: you 2
field: junia nl-snake-eye
field: anselm nl-mitsurugi
field: tova nl-ryzeal
field: augie nl-vanquish-soul
field: sasha nl-tenpai
field: imogen nl-branded
field: rue nl-yubel
field: lux nl-ladr
field: margo nl-maliss
field: inka nl-toon
field: radiant-typhoon radiant-typhoon
field: power-patron orvels-house
field: nyx gravekeepers-contraband
field: nocturne hat
```

- **Field:** the 11 peak builds and 4 alley names (Ilara, Orvel, Nyx, Nocturne), as NO-LIMITS-STORY writes it (its
  conflict 3: no headliner seat). Remy is seed 1, you're seed 2 (the Midnight Open's O5 pattern), the rest by Elo.
- **Opens:** with No Limits' first clear, and only once every `field:` build is `ready: yes` (wave 2). Before that, the
  chalkboard shows it as "Coming soon".
- **Your deck:** the No Limits rules each round (`Brackets.RulesFor(kind, ctx)`), the turn cap, no loaners.
- **No ante;** entry and placement only. Leaving a duel is a loss (C review).
- **Pay and tokens:** NO-LIMITS-ECONOMY 3.1, from the knobs; the data must match them (as the other brackets).
  - 2 Card Tokens on the first title only, keyed `invitational:token`.
  - Ledger: `nolimits.entry` and `nolimits` (N8's `PayLedger` and `EntryLedger` gain the kind).
- **Story:** `invitational` (the first entry), `bracketwin:No Limits Invitational`, and the round pools
  `mc:invitational:<round>` and `stream:invitational:<round>`.

### 9.2 The title target

- **10-20%** for the reference deck (4.7), by `--bracket-sim`.
- N17's model, with the builds at their 4.7 window and the alley names at their ratings (Ilara 1,674, Orvel about 1,596,
  Nyx about 1,620 measured, Nocturne 1,478):
  - the reference (1,947): about 12.5%;
  - the late-Library best (1,879): about 6%;
  - with the builds at base + 150 instead: 17% and 9%.

  The reference is inside the band. A typical player is well under it, which is what a capstone is for.
  NO-LIMITS-ECONOMY's "−2,400 to −2,600 DP an entry" holds either way.

### 9.3 The No Limits Pack

- The Contraband Pack's format (4 Raw, a sealed slab 9-10 at Super Rare and up; Paid 0; odds shown). Its pool is
  **every ever-Forbidden card the shipped builds run** (NO-LIMITS-ECONOMY 3.2): the `ready: yes` builds' decks ∩
  `ctx.Pool(9999)`, cards with a printing only.
- About 34 cards in wave 1 and 52 with wave 2; it grows with each build, so re-measure per wave.
- **Code:** `ContrabandPacks` gains a constructor that takes an explicit pool. `BackAlley.NoLimitsPackPool(data, ctx)`
  computes it. `Brackets.Finish` takes the No Limits pack for the `invitational` kind.

### 9.4 Engine changes (N8)

- `Brackets.InvitationalKind = "invitational"`. `IsAlleyKind`, `EntryFor`, `PayFor`, `PacksFor`, `PayLedger` and
  `EntryLedger` gain it.
- `AlleySeed` salts by a per-kind table: brawl 0, midnight 1, invitational 2. The existing kinds keep their values, so
  saved previews don't move.
- `Finish` pays the token per kind (`OpenTokens` and `midnight:token`; `InvitationalTokens` and `invitational:token`).
- `Brackets.RulesFor(kind, ctx)`: the Invitational gets the No Limits rules, the others the Alley rules.
- `AlleyData.Problems`:
  - the kind set grows;
  - the numbers are checked per kind against `AlleyEconomy`;
  - a field build must be ready, and its `base:` must not be in the same field.
- `BackAlley.Triggers` gains `invitational`, `nolimits`, `restored`, `outoftowner`, and `pilot:`/`pilotbeat:` per build.
  `IsVoiceHook` gains section 10's hooks.
- Profile: `AlleyBrackets["invitational"]` records it. `Nick` is unchanged (NO-LIMITS-STORY: "Midnight" is for good).

## 10. Cast, story hooks and data formats

### 10.1 The `build:` block (alley-events.txt)

```
# ---- No Limits (docs/dev/NO-LIMITS-SPEC-2026-11-02.md): peak builds. The id is the pilot's (personalities/<id>.profile). ----
build: junia
handle: Spoils
deck: nl-snake-eye
table: No Limits
base: snake-eye
family: snake-eye, fiendsmith
wave: 1
list: TCG April 2024
over: Union Carrier, Card of Safe Return, Soul Charge, Blaster, Dragon Ruler of Infernos
restored: Original Sinful Spoils - Snake-Eye, Snake-Eye Ash, Snake-Eyes Poplar, Fiendsmith's Lacrima
pieces: Original Sinful Spoils - Snake-Eye, Fiendsmith's Lacrima, Union Carrier
stakes: Original Sinful Spoils - Snake-Eye, Fiendsmith's Lacrima
ready: no
```

- Parsed as a `Headliner` with new fields: `IsBuild`, `Base`, `Family` (a list), `Wave`, `Restored` (a list of uint).
- A `build:` block reads `table:` the way a headliner's block does: its first `table:` line is the build's table.
- `AlleyTable` gains `Pool`, `Gate`, `MinElo` and `MaxElo` (`pool:`, `champions:` into `Headliners`, `gate:`, `elo:`).

### 10.2 Profiles

- `personalities/<id>.profile` for the 11 pilots, NO-LIMITS-STORY section 6, as written:
  - `decks:` lists the build, then the Brawl deck if any;
  - skill 1.0, no `ladder:` line;
  - `intro`, `win`, `lose` and `look`;
  - `combo`/`fizzle` only if the show layer is reused (11).
- None of the eleven ids collides with an existing profile (NO-LIMITS-STORY checked).
- `--campaign-test`'s roster checks must accept them: no portrait required (the hooded silhouette stands in).

### 10.3 Story triggers (NO-LIMITS-STORY section 2, adopted as written)

| Trigger | Raised by | When |
|---|---|---|
| `outoftowner` | Unity (bracket screen) | The first Brawl field with an out-of-towner |
| `outoftowner:<id>` | Unity (bracket versus) | Before your first Brawl match against that pilot |
| `nolimits` | Unity (alley tab show, load catch-up) via `BackAlley.WallDue` | The wall opens |
| `open:No Limits`, `alleyclear:No Limits` | existing paths | The panel's first open; the first clear |
| `pilot:<id>` | Unity (No Limits versus, the `headliner:` pattern) | Before your first No Limits duel against that pilot |
| `pilotbeat:<id>` | `BackAlley.Record` (a build's first win; replaces `headlinerbeat:` for builds) | Your first win against that pilot |
| `restored` | `BackAlley.Record` | The first restored card you win |
| `invitational`, `bracketwin:No Limits Invitational` | existing bracket paths | The first entry; the first title |

- `encore`, `turncap`, `firstturn` and `unlisted` keep working (their scenes are table-neutral).
- No new placeholders. "Local" is literal text.
- Speaker tokens: the eleven ids, and `opponent` in `nl-restored`.
- **Voice hooks** (alley-voice.txt; `IsVoiceHook` gains them):
  - `pilot:intro|win|lose:<id>`, `pilot:restored:<id>`, `pilot:encore:<id>`;
  - `outoftowner:intro|win|lose:<id>`;
  - `baz:restored` and `baz:restored:<id>` (the versus's UNBANNED row), `baz:nolimits`, `baz:stake:nolimits`;
  - `mc:brawl:outoftowner`, `mc:invitational:<round>`, `stream:invitational:<round>`;
  - van.txt: `van:sell-refused:restored` (a won restored card sells for 0).
- **Pilot pools speak for builds:** a build's versus intro picks `pilot:intro:<id>`, falling back to the profile's
  `intro:`, as regular pools do.
- **NO-LIMITS-STORY's `after: nl-<id>-beat` gates** use the existing scene `after:` mechanism. No code.

### 10.4 The rest of the data

- **campaign/story.txt:** NO-LIMITS-STORY section 4's block, after `alley-closing`. Until it's pasted in, scenes are
  marked `# PENDING NO-LIMITS-STORY` and the check lists them.
- **campaign/mats.txt:** `no-limits: <base> <panel> <accent> <motif>` (section 11).
- **campaign/peak-ratings.txt:** P3's output.
- **decks/recipes/no-limits.txt:** P4's recipes. **decks/no-limits/SOURCES.md:** provenance.

## 11. Look and sound

- **Theme:** BackAlley (it's still the alley). The Depot's panel uses the table panel with:
  - a "THE END CHAIR" tag on seat 4 (NO-LIMITS-STORY: never "champion" in-world; the code may say champion);
  - an **UNBANNED** row under each seat's deck title: "UNBANNED: Original Sinful Spoils - Snake-Eye ×3" (the build's
    first restored card and its count in the deck; `BackAlley.Unbanned(o, ctx)` gives the codes);
  - the status line from NO-LIMITS-STORY section 1.
- **Versus:**
  - the CPU's three showcase cards are its `pieces:` (restored first);
  - an **UNBANNED** label over restored cards and **CONTRABAND** over picks;
  - the deck line: "<#title> + <picks>";
  - Baz's `baz:restored` line under the UNBANNED row;
  - the stake chip as at every table;
  - the rules chip from 4.2.
- **The mat:** `no-limits` in mats.txt. Suggested: a cold strip-light grey (`#0B0C0E #1C2026 #F2C14E`, the warm bulb
  over the end chair as the accent) on the existing `chains` motif. Narrative or owner may change it.
- **Music:** a `no-limits` theme in `Sfx.Themes` (procedural, at the end chair through `music: no-limits` and
  `BossMusic.ForAlley`'s host-seat rule). Seats 1-3 play `alley`. NO-LIMITS-STORY's "Baz's bell rings twice as long in
  here" is a reverb tail on the bell sound at this table, optional.
- **Show layer (optional, low priority):** the end chair's playbook can mark one `ComboRoute` (Junia's Original Sinful
  Spoils line; Remy's opponent-turn Synchro) and reuse the headliners' banner and meter once Phase D's show layer
  exists.
- **Posters** (titles are Narrative's; card art from cards a stock install has pictures of; check modern cards'
  pictures exist):
  - `the-wall`: Original Sinful Spoils - Snake-Eye, earned when No Limits is first cleared;
  - `invitational-bill`: Kewl Tune Rotary, earned with the Invitational's title.
  - `-campaigncheck` uses `Posters.All[^1]` as its locked poster: keep that test true (B1's note).
- **Assets the owner may supply:** optional portraits for the eleven (`art/portraits/<id>.png`). The hooded silhouette
  stands in.

## 12. Save, New Game+ and the ledger

- **No new save lines:**
  - seats are `alleyopp` lines (kind `build` or `headliner`, character = pilot, base = deck);
  - builds' met and beaten counts are `headliner <id> ...` lines (`profile.Headliners`, keyed by pilot id);
  - binders are `binder nl:<pilot> ...` lines;
  - the wall is `profile.Seen` (`nolimits`); the table is an `alleytable no-limits ...` line;
  - the Invitational is a 16-slot Bo3 bracket (existing lines) and an `alleybracket invitational ...` record.
- **Unlisted:** one page per build. `Unlisted.KeyOf` returns `nl:<pilot>` for a build (new branch). The page kind is
  "headliner" (the existing layout). `Unlisted.Contraband` marks its ever-Forbidden cards. "Open as a new deck" has no
  `#source` (N12). `Unlisted.Backfill` iterates `data.Headliners`, which now includes builds, harmlessly.
- **New Game+:** the alley is untouched (ALLEY-SPEC 14). Binders, the restock state (none saved), the wall,
  `invitational:token` and posters carry over; nothing re-pays.
- **Ledger** (NO-LIMITS-ECONOMY 6.2): `Ledger.NoLimitsStake` (`nolimits.stake`), `Ledger.NoLimitsEntry`
  (`nolimits.entry`), `Ledger.NoLimits` (`nolimits`).
  - `Ante.Settle` writes `nolimits.stake` for a No Limits loss or quit; other tables keep `alley.stake`.
  - `LedgerCheck` lists the three keys. `Ledger.Net` reconciles with held DP as built.

## 13. Knobs

NO-LIMITS-ECONOMY section 7's "No Limits" block in **`AlleyEconomy`** (the one file of alley numbers; the harness swaps
values before measuring). All [P], per NO-LIMITS-ECONOMY:

| Knob | Default | Notes |
|---|---|---|
| `NoLimitsStake` | 2,500 | Held. `Ante.StakeFor(NoLimits)` |
| `NoLimitsCap` | 10,000 | The floor is `StakeFloor` (1.0) × the stake |
| `NoLimitsGrades` | (8 Raw 0.15), (9 Raw 0.15), (9 slab 0.35), (10 slab 0.35) | `Ante.Conditions(NoLimits)` |
| `NoLimitsRestock` | true | `Ante.Restock` on a drawn night |
| `NoLimitsGuardTurns` | 1 | The first-turn guard at this tier |
| `InvitationalEntry` | 3,000 | |
| `InvitationalPay` | 1,500, 1,000, 600, 300, 100 | At most 300 a duel |
| `InvitationalPacks` | 4, 3, 2, 1, 0 | No Limits Packs |
| `InvitationalTokens` | 2 | First title, `invitational:token` |
| `ShelfRestoredSlots`, `ShelfRestoredMinRarity`, `ShelfRestoredMinValue` | 2 of 6, Ultra Rare, 1,500 | `MothsVan.Shelf` once the wall is open: 2 slots drawn from the ready builds' `stakes:` cards |

**Reused as they are:** `StakeFloor`, `BinderSize` (12), `RefillEvery` (3), `ContrabandWeight` (3×), `DuelDp` (0),
`ClearTokens` (1), `WildcardChance` (1/3), `PackSize`, `PackSlabNine`, `PackSlabMinRarity`, `AlleyTurns` (40).

**Design knobs** (constants in BackAlley, not economy):

| Knob | Default | What |
|---|---|---|
| `NoLimitsMinPool` | 4 | The fewest ready non-champion builds a `ready: yes` table may have (a night needs 3, and a wildcard can take one) |
| `NoLimitsShipPool` | 6 | The wave-1 ship gate (section 14) |
| `NoLimitsMaxGain` | 250 | Elo over `base:` (AlleyData.Problems fails above it) |
| `NoLimitsPickShare` | 2 | The most picks two builds may share |
| `NoLimitsFiendsmith` | 3 | The most builds tagged `fiendsmith` |

**Harness targets** (AlleyStudy and BracketStudy constants):

| Target | Value |
|---|---|
| Seats 1-3 | 55% ± 5 (pool mean) |
| End chair | 50% ± 5 (Junia: band only, decision 2) |
| Any build | 45-65% |
| Invitational title (reference) | 10-20% |
| Brawl title after The Black Market | 30-45% |
| `--peak-check` use | 60% |
| Speed | as 4.7 |

**Hard rules no knob may break** (ALLEY-SPEC 15, NO-LIMITS-ECONOMY 7): unchanged.

## 14. Build split and sequencing with Phase D

### 14.1 The order

ALLEY-SPEC's Phase D (the headliners' show, the difficulty pass and the proof) isn't built. No Limits interlocks with
it in two places: the Brawl fix lives here, and the proof should run once over both.

| Step | What | Size | Runs beside |
|---|---|---|---|
| **NL-0** | Prerequisites P1-P4 (lists, BossOnly, the anchored rater, the real wave-1 lists), the No Limits list | about 0.5 session | **Phase D-show** (HEADLINERS phases 2-3: Unity board, banner, meter, the four headliners). Files are disjoint |
| **NL-1** | Wave 1: the table, 7 builds, stakes, the Brawl's out-of-towners. **Phase D's difficulty pass rides along** (the Brawl's measurement is NL-1's; Nyx's seats 1-3 with older decks, owner decision 5; Dumpster Row's retune), as chunk 4 after C1 lands, since both edit alley-events.txt | about 1.5 sessions | |
| **NL-2** | Wave 2 (Remy, Margo, Lux, Inka), the Invitational, the restored shelf slots, P5 when the MAMS data lands | about 1.5-2 sessions | |
| **Proof** | Phase D's proof extended: `--alley-study` over every table; `--bracket-sim` over all four brackets; `--postgame-sim` with an alley and No Limits pilot; `--arbitrage` with NO-LIMITS-ECONOMY's 10 loops and searches; a 60-duel `-qaplay` that spends a session behind the wall | about 0.5 session | |

Total about 4-4.5 sessions, plus Phase D-show's 0.5.

Within a phase, chunk 1 (engine and data) lands its API first (binding below), then chunks 2 (Unity) and 3 (harness)
run in parallel on disjoint files. New `.cs` files under src/Ygo.Engine need `.meta` files.

### 14.2 NL-0: prerequisites

- **Chunk 0a, data:** lflists/history.lflist.conf (P1), tools/fetch_banlists.py (`--list` mode),
  decks/recipes/no-limits.txt and decks/no-limits/ for wave 1 (P4: lists only, unrated), decks/no-limits/SOURCES.md.
- **Chunk 0b, engine:**
  - `Alley.cs`: P2, `NoLimitsList`, `PowerCards`;
  - `DeckRatings.cs`: `Load(params)`, `PeakFileName`.
- **Chunk 0c, harness:**
  - new `PeakRating.cs` (`--rate-anchored`); `DeckRating.cs` (skip decks/no-limits); `Program.cs` (the flag);
  - `DeckBuildingTest.cs` (the chain check);
  - `AlleyTest.cs` (the pinned counts, the Excluded 35, the No Limits list legality and no-leak checks).
- **API:**
  ```csharp
  public static readonly string[] Excluded, BossOnly;        // + "Djinn Releaser of Rituals", "Archnemeses Protos"
  public static readonly string[] PowerCards;                 // rule 3's set (section 5.1)
  public static Banlist NoLimitsList(ICardDataProvider cards); // Name "No Limits": the Excluded cards at 0, nothing else
  // DeckRatings.cs
  public const string PeakFileName = "peak-ratings.txt";
  public static Dictionary<string, double> Load(params string[] paths);   // later files win; missing files skipped
  ```
- **Acceptance:**
  - `--deckbuilding-test` passes with the new lists and the chain check.
  - `--campaign-test` passes with the recounted pool and shelf numbers (recorded in the As-built note).
  - `--alley-gen` reports 0 decks short or illegal and no splash changed.
  - `--rate-anchored` calibration is within ±40, and it rates every wave-1 build, Brawl deck and the two decks/alley
    decks into campaign/peak-ratings.txt.

**As built (NL-0, 2026-11-02).** The API above holds as written (`Excluded`, `BossOnly`, `PowerCards`, `NoLimitsList`,
`PeakFileName`, `Load(params)`). NL-1 builds from these additions and choices:
- **The lists (P1).** All five came from Format Library's API (`tools/fetch_banlists.py --list "<Month Year>"`, new: it
  writes one list into the file in date order and leaves the rest alone), then were checked card for card:
  - `TCG January 2024`: yugiohcardguide.com's 2024-01-01 table (all 194 entries and its 25 changes);
  - `TCG September 2025`: ygorganization.com/goodnightmoon (its 26 changes; yugiohcardguide has no page for it);
  - `TCG October 2025`: yugiohcardguide.com's 2025-10-27 table (214 entries, 7 changes);
  - `TCG February 2026`: yugiohcardguide.com's 2026-02-02 table (216 entries, 21 changes);
  - `TCG May 2026`: ygorganization.com/prematurekewltunenerfs (its 15 changes); and TCG September 2026 = May 2026 plus
    exactly the 21 changes on Konami's list_2026-09-21 page.
  - **Format Library leaves out two cards** Konami's lists carry: Skill Drain (Limited from TCG September 2024) and
    Barrier Statue of the Torrent (Forbidden from TCG February 2026). The script adds them (`FL_MISSING`), and the
    repo's existing TCG September 2024, December 2024, April 2025 and September 2026 lists were corrected the same way
    (Skill Drain 1; Torrent 0 in September 2026). No other card in any list moved; no deck was affected.
  - **The chain check covers ten steps, not five:** every list from TCG September 2023 to TCG September 2026 is the one
    before plus its source's changes (`DeckBuildingTest.ListChain`; the Sep 2024, Dec 2024 and Apr 2025 steps are
    yugiohcardguide's tables), and no dated TCG list in that span is skipped.
  - **N6, re-run:** TCG September 2023's format now ends 2024-01 and TCG April 2025's 2025-09 (was 2026-09). No
    circuit or cup uses either list. One history deck changes: k9-vanquish-soul (`#banlist TCG April 2025`, the Aug 2025
    YCS Vancouver list) now shows Vanquish Soul, Start! as "not out yet" (first TCG printing on record Feb 2026); the
    history-release check allows it (1 of 74 decks). The deck editor will flag it the same way when that deck is opened.
- **The peak lists (P4)** are `decks/no-limits/nl-*.ydk` from `decks/recipes/no-limits.txt`: the real list of each
  deck's peak month, Main and Extra card for card (no Side Deck: the game never sides), `#banlist` its month's list,
  `#recipe` the base (its playbook), no `#source`. Provenance and every place the research's deck id was wrong are in
  decks/no-limits/SOURCES.md:
  - nl-snake-eye: NAWCQ 2024 winner (Jul 2024, TCG April 2024). The research's `fiendsmith-snake-eye-520593` is the YCS
    Sacramento winner, not a NAWCQ list.
  - nl-mitsurugi: YCS Providence 2025 winner (May 2025, TCG April 2025). The research's `ryzeal-mitsurugi-714735` is a
    2026 national list.
  - nl-ryzeal: YCS Orlando 2025 winner (Feb 2025, TCG December 2024).
  - nl-vanquish-soul: YCS Bologna 2025 winner (Nov 2025, TCG October 2025). The research's `k9-vanquish-soul-692109` is
    the Feb 2026 300th YCS Guadalajara list (TCG February 2026).
  - nl-tenpai: EUWCQ 2024 winner (Jul 2024, TCG April 2024), the research's list.
  - nl-branded: EUWCQ 2026 Top 4 (Jul 2026, TCG May 2026), no Light and Darkness package. The research's
    `despia-branded-719679` is the Kuwait national winner.
  - nl-yubel: YCS Cancún 2024 winner (Oct 2024, TCG September 2024), the research's list. No Worlds or Lille 2024 list is
    on either site.
  - `tools/build_deck.py` now keeps a recipe's own `#created by Duel Circuit - ...` line.
  - **The four Brawl decks aren't here:** 14.3 builds them (chunk 1, "the seven builds and four Brawl decks, tuned and
    rated"), so NL-1 rates them with `--rate-anchored`.
- **P2:** Djinn Releaser of Rituals and Archnemeses Protos close `Excluded` and `BossOnly` (35 = 10 Never + 25
  BossOnly). They left The Basement Game's and The Black Market's contraband pools (Djinn; Protos is post-2019); no
  splash deck changed (`--alley-gen` before and after differ only in those pool lines; 0 short or illegal).
- **The recounted pins (N7):** the Contraband Pack pool is **229** (231 less the two; the new lists add no
  ever-Forbidden card that wasn't already in it), Moth's shelf **425 possible items from 146 cards** (was 429 from
  147). They're `AlleyTest.ContrabandPool`, `ShelfItems` and `ShelfCards` now. Three new AlleyTest checks (the No Limits
  list, its legality, no leaks): 263 campaign checks.
- **The anchored rater (P3), `PeakRating.cs`:**
  - **`DefaultRounds` = 600, not 300.** At 300 the first run's calibration missed by 2 (ishizu-tearlaments +42; the
    other four +10 to +40): the estimate's own spread (about ±20 at 300) adds to the pool rating's. At 600 all five
    land inside (-5 to +38). `--rounds N` still overrides.
  - A deck's rating is the maximum-likelihood Elo of its results against the pool's fixed ratings, re-estimated every
    10 duels (the estimate picks the next opponents, ±250, widened until 6 pool decks qualify). It starts at its
    `#recipe` base's rating (else the pool median).
  - **The calibration runs in every call**, on the 5 pool decks nearest evenly spaced points over the new decks'
    range; a miss fails the run (exit 3) and `--write` writes nothing. Calibration lines don't parse as ratings.
  - `--write` merges into campaign/peak-ratings.txt (a re-rated deck is replaced, the rest kept). `--write` is still
    `<path>` for the studies; it's a bare flag only beside `--rate-anchored`.
  - `AlleyTest.Load` merges both files. **CampaignState.cs:207 (Unity) doesn't yet:** it's in the files the parallel
    show-layer work owns, so NL-1 chunk 2 makes the one-line change (`DeckRatings.Load(deck, peak)`).
- **Ratings (campaign/peak-ratings.txt, 600 duels a deck, seed 1; calibration -5 to +38).** The peak lists as played,
  before NL-1's restored cards and picks, against their `base:`:

  | Deck | Elo | Base (`#recipe`) | Over base |
  |---|---|---|---|
  | nl-branded | 1,817 | branded-despia 1,830 | -13 |
  | nl-ryzeal | 1,788 | ryzeal 1,867 | -79 |
  | nl-vanquish-soul | 1,725 | k9-vanquish-soul 1,673 | +52 |
  | nl-mitsurugi | 1,689 | ryzeal 1,867 | -178 |
  | nl-snake-eye | 1,681 | snake-eye 1,612 | +69 |
  | nl-tenpai | 1,671 | tenpai-dragon 1,675 | -4 |
  | nl-yubel | 1,606 | yubel 1,653 | -47 |
  | gravekeepers-contraband (Nyx) | 1,826 | eldlich 1,764 | +62 |
  | orvels-house (Orvel) | 1,539 | power-patron 1,546 | -7 |

  Four peak lists rate under their base. The bot plays them with the base's playbook, written for the base's list:
  nl-mitsurugi (3 Habakiri, 3 Seventh Tachyon, 1 Mitsurugi Ritual) is furthest from ryzeal.ydk (no Habakiri or
  Tachyon, 3 Rituals) and falls furthest. That's NL-1's playbook work (7.2); the +250 cap (rule 8) is measured from
  the bases, so the builds have room.

### 14.3 NL-1: wave 1, the table and the out-of-towners

**Chunk 1, engine and data.** Files:
- **`src/Ygo.Engine/Campaign/BackAlley.cs`:**
  - the tier, the `build:` block and table keys, seat kinds `pool` and `champion`, and `DrawNight`'s No Limits branch
    (rotation, families, the end chair, the wildcard, the restock call);
  - `Complete` (the `nl:` binder), `IsBuild`, `RulesFor`, `Unbanned`, `WallDue`;
  - `Record` (`pilotbeat:`, `restored`, the guard turns, `nolimits.stake` through `Ante`);
  - the `after:` field lines, `OutOfTowners`, `Triggers` and `IsVoiceHook`;
  - `AlleyData.Problems` (5.1's rules, the Brawl decks, the twin rule).
- **`src/Ygo.Engine/Campaign/Ante.cs`:** the `NoLimits` cases, `nl:` owners, `Restock`, the ledger key.
- **`src/Ygo.Engine/Campaign/AlleyEconomy.cs`:** the No Limits block (all of section 13's knobs now, so NL-2 adds none).
- **`src/Ygo.Engine/Campaign/Unlisted.cs`** (`KeyOf`) and **`Profile.cs`** (the `Ledger` constants).
- **`src/Ygo.Engine/AI/Playbooks/`:** `Playbook.cs` (`FiendsmithEngine`), and the additions to SnakeEye, Ryzeal,
  K9VanquishSoul, TenpaiDragon, BrandedDespia and Yubel playbooks (7.2).
- **`src/Ygo.Engine/Audio/Sfx.Themes.cs`** (`no-limits`) and **`Posters.cs`** (`the-wall`).
- **Data:**
  - alley-events.txt: the table (`ready: no` until the gate), the seven wave-1 `build:` blocks, the wave-2 four with
    `ready: no`, the Brawl's `after:` lines;
  - decks/no-limits/ (the seven builds and four Brawl decks, tuned and rated);
  - personalities/ (eleven profiles); story.txt (NO-LIMITS-STORY's scenes); alley-voice.txt and van.txt (its pools);
  - mats.txt; campaign/peak-ratings.txt.

**API (binding for C2 and C3):**

```csharp
// BackAlley.cs
public enum AlleyTier { Street, Den, Orvel, Nyx, NoLimits }
public sealed class Headliner { /* existing */ public bool IsBuild; public string Base = ""; public List<string> Family = new List<string>();
                                public int Wave; public List<uint> Restored = new List<uint>(); }
public sealed class AlleyTable { /* existing */ public List<string> Pool = new List<string>(); public string Gate = ""; public double MinElo, MaxElo; }
public sealed class AlleyContext { /* existing */ public Banlist NoLimits = null!; }
public sealed class AlleyOpponent { /* existing */ public bool IsBuild; }                 // set by Complete; IsHeadliner also includes Kind "build"
public static class BackAlley
{
    public const string BuildKind = "build", WallEvent = "nolimits", PilotEvent = "pilot:", PilotBeatEvent = "pilotbeat:",
                        RestoredEvent = "restored", OutOfTownerEvent = "outoftowner";
    public static bool WallDue(Profile profile, AlleyData data);                      // Nyx cleared, alley-closing seen, nolimits not seen
    public static FormatRules RulesFor(AlleyData data, int table, AlleyContext ctx);   // No Limits rules at a nolimits table, else the Alley's
    public static IReadOnlyList<uint> Unbanned(AlleyOpponent o, AlleyContext ctx);   // the build's restored cards in its deck, in order
    public static int CopiesIn(AlleyOpponent o, uint code, AlleyContext ctx);        // "×3"
    public static List<string> OutOfTowners(Bracket b, AlleyData data);              // build pilots seated in a bracket
    public static List<uint> NoLimitsPackPool(AlleyData data, AlleyContext ctx);     // NL-2: the ready builds' ever-Forbidden cards with printings
}
// Ante.cs
public static StakeBinder Restock(Profile profile, string owner, AlleyContext ctx);  // nl: owners: missing leads back, behind those still there
// AlleyEconomy.cs: section 13's knobs. Profile.cs: Ledger.NoLimitsStake, NoLimitsEntry, NoLimits.
// Playbook.cs
protected void FiendsmithEngine(Func<bool>? when = null);
```

**Chunk 2, Unity.** Files:
- `CampaignState.Alley.cs`: the No Limits list in `AlleyContext`, the merged ratings, `WallDue` at load.
- `CampaignScreen.Alley.cs`: the node, the lock line, the panel (THE END CHAIR, UNBANNED rows, the Encore button for
  champions), `TierName`, `InNoLimits`, `WallDue` on tab show.
- `DuelController.Alley.cs`: `RulesFor` at the versus, `pilot:<id>` before the first duel, the UNBANNED and CONTRABAND
  labels.
- `CampaignScenes.cs` (the labels), `DuelController.cs` (the deck editor gets `RulesFor`), `BracketScreen.cs` and
  `DuelController.Brackets.cs` (`outoftowner` events).
- `DuelController.AlleyCheck.cs` (steps 16-19) and `DuelController.ThemeCheck.cs` (the No Limits panel).

**Chunk 3, harness.** Files:
- `AlleyTest.cs` (NL checks, below).
- `AlleyStudy.cs`: `--alley-study` for the No Limits tier per 4.7 (the reference deck under the No Limits list, every
  build directly, the pool's night mean, the end chair, speed figures); `--endless-test` with the builds and Brawl
  decks.
- `BracketStudy.cs`: the Brawl rows "after The Black Market (out-of-towners)", with the as-built comparison row.
- `Arbitrage.cs`: NO-LIMITS-ECONOMY 6.1 loops 1-5 and 9-10, and the 12-action No Limits search.
- New `PeakCheck.cs` (`--peak-check <pilot|all> [--games N]`): use rate per restored card and pick, gain over base,
  winning-turn speed against base, and wins by turn 2.
- `LedgerCheck.cs` (the keys), `Program.cs` (the flags).
- `AlleyProbe.cs`: NO-LIMITS-ECONOMY's No Limits section (binder fill and leads per build, from the authored decks).

**Chunk 4, Phase D's difficulty pass (data and measurement)**, after C1 merges: alley-events.txt (Nyx's seats 1-3 with
older decks, decision 5) and alley.txt (Dumpster Row), each measured with `--alley-study`. Recorded in ALLEY-SPEC's
Phase D As-built.

**The ship gate** (the table's `ready: yes`):
- Junia and at least `NoLimitsShipPool` (6) pool builds pass 5.1's gates;
- `--alley-study` meets 4.7: every build 45-65%, the pool mean 55% ± 5, Junia inside the band;
- `--endless-test` passes.

A build that misses stays `ready: no` and is retuned by its picks, never weakened below its peak list.

**AlleyTest checks (C3):**
1. **The list:**
   - Excluded is 35, `Never ∪ BossOnly` disjoint;
   - the No Limits list has exactly the Excluded cards at 0;
   - 3 Pot of Greed are legal at No Limits and illegal at Dumpster Row;
   - Djinn and Protos are illegal at both;
   - no leaks (cups, circuits and rounds never see the No Limits list).
2. **Data:** `Problems()` empty, with every 5.1 rule exercised by a bite (`ALLEY_BITE=nl-pick`, `nl-power`,
   `nl-excluded`, `nl-lead`).
3. **Unlock:**
   - locked until Nyx's Table is cleared and `alley-closing` is seen;
   - `WallDue` is true exactly once, then the table opens;
   - an old save past both opens it at load.
4. **Draws** (1,000 seeded nights):
   - three different builds at seats 1-3, with no shared family among seats 1-3 when the pool allows;
   - every ready pool build sits within ⌈n/3⌉ + 1 nights;
   - Junia closes until beaten, and the encore opens after every ready champion is beaten;
   - with Remy ready: Remy closes, and Junia wildcards in 33% ± 2;
   - deterministic, a loss changes nothing, save and load byte-identical.
5. **Binders:**
   - every ready build's binder leads with its first `stakes:` card;
   - every stake is inside 2,500-10,000 at `NoLimitsGrades`;
   - its ever-Forbidden cards are drawn at about 3× their share;
   - the restock returns a won lead on the next night it sits, only then, once.
6. **Ante:**
   - `Hold` moves 2,500;
   - a loss writes `nolimits.stake`;
   - the guard at turn 1 gives it back with no line;
   - a won lead is Paid 0, and the Sell desk refuses it;
   - `restored` fires once.
7. **Unlisted:** one page per pilot, key `nl:<pilot>`, no `#source` in its deck.
8. **Story:**
   - the triggers raised are in `Triggers(data)` and have scenes (or are listed pending);
   - every pilot has a profile;
   - `pilotbeat:` and not `headlinerbeat:` for builds.
9. **Brawl:**
   - before The Black Market's clear the field is 7 regulars;
   - after it, the four at seeds 8, 5, 2 and 3;
   - every Brawl deck is legal under the Alley list, with its added cards in the Basement's pool.

**`-alleycheck` steps (C2):**
- 16. No Limits is locked until the wall opens. The `nolimits` scene plays on the next tab show after a forced Nyx clear
  and `alley-closing`, never on the same screen.
- 17. The panel: four seats, THE END CHAIR on seat 4, UNBANNED rows, a stake chip of 2,500.
- 18. The versus:
  - UNBANNED and CONTRABAND labels over `pieces:`;
  - a deck with 3 Pot of Greed is accepted here and refused at Dumpster Row;
  - a deck with Djinn is refused at both.
- 19. A forced win: the restored card is in the collection, Paid 0; `restored` and `pilotbeat:<id>` play; the deck
  editor opened from this panel uses the No Limits rules.

**Acceptance:**
- `--campaign-test`, `--arbitrage` and `--deckbuilding-test` pass;
- `-alleycheck` 1-19, `-campaigncheck` and `-themecheck` pass;
- the ship gate holds;
- `--bracket-sim` puts the Brawl after The Black Market at 30-45% for the late-Library best and the best-in-the-Alley
  deck, or the Brawl decks are retuned and the numbers recorded.

**As built (NL-1, 2026-11-05).** Everything above is built except the measuring, which the owner parked with a later
difficulty and CPU pass (2026-11-05). So the table and all eleven builds ship `ready: no`: the map shows No Limits as
"Coming soon" and nothing in it can be played yet. Build plan and file ownership: docs/dev/NL1-BUILD-2026-11-03.md.
- **The API holds as written,** plus: `AlleyData.Builds`, `FindBuild`, `Problems(ctx, notReadyBuilds)` (a not-ready build
  skips its deck checks unless asked); `Headliner.Picks` (`over:` minus `restored:`); `BackAlley.ClosingScene`,
  `FiendsmithFamily`, `IsNoLimitsTrigger`; `Ante.NoLimitsOwner`, `Ante.CanStake` (rule 7's lead check);
  `AlleyBracketSpec.ParseField`. `NoLimitsPackPool` returns the pool now (ready builds' decks with a printing).
- **Choices the spec left open:**
  - The pool order is the salt shuffle, then packed so no window of three shares a family where the pool allows (else a
    same-family build is starved, failing the "within ⌈n/3⌉+1 nights" check).
  - The restock keeps a binder at `BinderSize` by dropping its last non-lead card.
  - A build or `after:` entrant whose deck file is missing never sits (a regular stands in at the Brawl).
  - Rule 3's "2-4 picks" isn't counted (Junia has 5 by 5.3).
  - The Brawl's `after:` lines fold into the `splash 3 cleared` stream, so its field before The Black Market's clear is
    the same seven regulars byte for byte.
  - `restored` is once ever, keyed in `Profile.Seen` (review fix).
- **Builds:** all seven validate under `list:` with `over:` (`Problems` is empty with `notReadyBuilds`). The shared
  playbooks are additive: `--turn1` boards of snake-eye, ryzeal, k9-vanquish-soul, tenpai-dragon, branded-despia and yubel
  are identical before and after (6 hands each, against a build with none of the new code). Imogen keeps Chaos Sorcerer
  (the list has 7 LIGHT and enough DARK).
- **Ratings (campaign/peak-ratings.txt, 600 a deck, a single run, not a tuning result):**

  | Build | Elo | Over base |
  |---|---|---|
  | nl-branded (Imogen) | 1,865 | +35 |
  | nl-vanquish-soul (Augie) | 1,763 | +90 |
  | nl-snake-eye (Junia) | 1,728 | +116 |
  | nl-yubel (Rue) | 1,696 | +43 |
  | nl-mitsurugi (Anselm) | 1,683 | -184 |
  | nl-tenpai (Sasha) | 1,675 | 0 |
  | nl-ryzeal (Tova) | 1,673 | -194 |
  | Brawl decks: vanquish-soul / snake-eye / yubel / tenpai | 1,715 / 1,695 / 1,679 / 1,586 | |

  Every build is under the table's 1,840-1,990 window and most under +100; the ship gate (section 14.3) is the parked
  tuning pass's.
- **`-alleycheck` steps are 19-22,** not 16-19 (the show layer owns 16-18). Step 12 now expects the out-of-towners, since
  its save has The Black Market cleared by then. Step 19 plays the real Continue path (review fix 1).
- **Harness:** the NL checks live in `AlleyTest.NoLimits.cs` (26 checks, about 3 s, no duels; `--nolimits-test` runs only
  them). The study, probe and arbitrage additions are partial classes beside their files. New flags: `--peak-check`,
  `--nolimits-test`; `--only "No Limits"` also works for `--arbitrage` and `--alley-probe`. The measuring flags were only
  smoke-tested. Nyx's pinned checks (harness and Unity) read the expected seats from the data now.
- **Review fixes (Code Reviewer, 2026-11-05):**
  1. The wall opened on the screen the Continue reopens after alley-closing, the same night. `CampaignState.OpenWall`
     now holds it when alley-closing was seen since the last campaign screen.
  2. The No Limits and Alley lists stayed in the deck editor after a panel opening, so a menu deck could validate under
     No Limits. A format's list now belongs to that opening only.
  3. The family rule counted a champion who didn't sit (wave 2 only).
  4. The pilots joined Free Duel and attract mode, and moved the QA stand-in's style (by id order) to Anselm. Pilots are
     out of free play; `QaStyle` picks from ladder characters only.
  5. `restored` relied on the collection to fire once.
  6. `van:sell-refused:restored` and `pilot:encore:<id>` are now spoken.
  7. A `champions:` id that names nothing is a data problem.
- **Left for later (not bugs today):**
  - `baz:nolimits` has no caller.
  - A saved build seat whose deck file later goes missing isn't redrawn.
  - With no unbeaten champion, a night seats four pool builds but the cursor steps three, so night k's seat 4 is night
    k+1's seat 1. It matches "3k mod n" literally.
  - The 2024-26 pieces have no card art in a stock install; the versus shows their names.
- **Hints for the tuning pass (smoke runs, unrepeated):** the Brawl after The Black Market read 46.4% / 49.4% (owner:
  accepted, ALLEY-DIFFICULTY); nl-yubel stopped at the prompt cap in 3.3% of an endless run (target under 0.5%);
  Fiendsmith's Lacrima and Union Carrier read low on use in Junia's build; at `ShelfRestoredMinValue` 1,500 the cheapest
  restored slab sells back 18% of its price (limit a sixth), an NL-2 knob.

### 14.4 NL-2: wave 2 and the Invitational

**Chunk 1, engine and data.** Files:
- `Brackets.cs` (9.4) and `ContrabandPacks.cs` (an explicit-pool constructor).
- `BackAlley.cs`: `NoLimitsPackPool`, the Invitational in `Problems`, `Triggers` and `IsVoiceHook`.
- `MothsVan.cs`: the restored shelf slots.
- `AI/SmartDuelist.cs` (`AnnounceCard`), `AI/Playbooks/Playbook.cs` (`Declare`, `PlaybookView.OpponentKnownHand`),
  `AI/BoardState.cs` (only if the spike says so).
- New `KewlTunePlaybook.cs`, `MalissPlaybook.cs`, `ToonPlaybook.cs` with `.meta` files; `Playbooks.cs` (`Lookup`);
  `LightAndDarknessPlaybook.cs` (additions).
- `Posters.cs` (`invitational-bill`).
- **Data:**
  - decks/history/kewl-tune.ydk and SOURCES.md;
  - decks/no-limits/ (remy, margo, lux, inka);
  - alley-events.txt (wave 2 `ready:`, the Invitational block);
  - peak-ratings.txt; story.txt and alley-voice.txt (the Invitational and wave-2 scenes).

**Order inside C1:**
1. the spikes (7.4);
2. Lux (cheap: an existing playbook);
3. Margo;
4. Remy;
5. Inka.

Each flips `ready: yes` only through 5.1's gates. The Invitational's block stays "Coming soon" until all four are ready.

**API:**

```csharp
// Brackets.cs
public const string InvitationalKind = "invitational";
public const string InvitationalTokenKey = "invitational:token";
public static FormatRules RulesFor(string kind, AlleyContext ctx);
public static BracketReward? Finish(Profile profile, Library library, Bracket b, PrizePacks? prizes, ContrabandPacks? contraband = null,
    ContrabandPacks? noLimits = null);                                                 // the Invitational's packs from noLimits
// ContrabandPacks.cs
public ContrabandPacks(ICardDataProvider cards, PrintingIndex printings, IReadOnlyList<uint> pool);
// Playbook.cs
protected void Declare(uint source, Func<PlaybookView, IEnumerable<uint>> rank);
public sealed class PlaybookView { /* existing */ public IReadOnlyList<uint> OpponentKnownHand { get; } }
// MothsVan.cs: Shelf gives AlleyEconomy.ShelfRestoredSlots to the ready builds' stakes: cards once BackAlley's wall is open.
```

**Chunk 2, Unity:**
- `CampaignScreen.Alley.cs` (the chalkboard's third bracket);
- `DuelController.Brackets.cs` (`Brackets.RulesFor`, the Invitational's prize reveal through `ShopScreen.OpenPrizes`);
- `VanScreen.cs` (the restored slots' tag);
- `DuelController.AlleyCheck.cs`, step 20: the Invitational's entry, 16 slots, Bo3 pips, the No Limits rules chip, a No
  Limits Pack reveal.

**Chunk 3, harness:**
- `BracketStudy.cs` (the Invitational rows: 10-20% for the reference);
- `AlleyTest.cs` (the Invitational's numbers, seeding, twin rule, pack pool; Toon's leads);
- `Arbitrage.cs` (NO-LIMITS-ECONOMY loops 6-8 and the bracket search's 3 actions);
- `PeakCheck.cs` (Mind Scan: a named card that's in the revealed hand in at least 80% [P] of declarations);
- `AlleyStudy.cs` (Remy's end chair at 50% ± 5).

**Acceptance:**
- every wave-2 build passes 5.1's gates;
- Remy's end chair is at 50% ± 5 and Junia, wildcarding, is inside the band;
- the Invitational is at 10-20% for the reference;
- `--arbitrage` passes;
- `-alleycheck` 20 passes;
- `--turn1 kewl-tune,nl-toon,nl-maliss` boards are recorded.
- **P5:** when MAMS lands, Inka's leads gain Perfect World and the checks re-run.

**As built (NL-2's non-deck parts, 2026-11-05).** The owner picked the Invitational and Moth's restored slots ahead of
wave 2's builds; plan docs/dev/NL2-INVITATIONAL-BUILD-2026-11-05.md. Built: section 9 whole and the MothsVan line of 14.4.
Not built: wave 2 (Remy, Margo, Lux, Inka: decks, playbooks, Mind Scan, the spikes) and every measurement, parked with
the tuning pass.
- **It ships locked.** `Brackets.Opening(profile, data, kind)` says "Coming soon" while any field build isn't ready
  (today none is), then "Clear No Limits to unlock", then open. The chalkboard hides the Invitational until the wall has
  opened, and the Locker hides `the-wall` and `invitational-bill` until then too (`Poster.HiddenUntil`).
- **API as built:** section 14.4's, plus `Brackets.AlleyKinds` (the salt order: brawl 0, midnight 1, invitational 2),
  `TokensFor`, `TokenKeyFor`, `PackNameFor`, `FieldBuilds`, `FieldReady`, `Opening`/`BracketOpening`, `FinishBlocked`;
  `ContrabandPacks.NoLimits(data, ctx)` and `CanFill`; `MothsVan.RestoredOpen`, `RestoredItems`; `Listing.Restored`;
  `BackAlley.InvitationalWinEvent`; `AlleyData.BracketDuelCap` (300).
- **Finish can't lose the packs:** at the Invitational, a placement owed No Limits Packs with no pack source (or a pool
  that can't fill a pack) returns null and changes nothing; the bracket stays done and the next Finish pays it whole.
- **The shelf:** `AlleyEconomy.ShelfPrice` rounds up to 500 for every item, so no slab sells back more than a sixth. This
  moves one regular price (Quarter Century Secret slab 10, V 5,400: 8,000 to 8,500), which broke the bound before. The
  restored slots are slots 4-5 once the wall is open and a build is ready; old slot-number sold marks apply to regular
  slots only, and `Buy` refuses a listing no longer on the current shelf.
- **Field rules:** a build counts as an Invitational field build only on its own deck; `AlleyData.Problems` flags a build
  pilot's field line without a deck. The Brawl's out-of-towners play Brawl decks, so they don't gate the Brawl.
- **Pilots at the Invitational** use their `pilot:intro|win:<id>` pools, never the Brawl's out-of-towner lines.
- **Engraving** (owner, 2026-11-05) skips a partner's edit, like the Format Master stamp.
- **The poster** uses Glow-Up Bulb (Kewl Tune Rotary has no picture in a stock install).
- **Checks:** `--campaign-test` 374 (20 Invitational and shelf checks, 1 ledger); `--arbitrage` loops 5-8 and the
  bracket search's three Invitational actions, 0 DP-positive; `-alleycheck` steps 23-24 (alley 117).
- **Smoke hints (unconfirmed, for the tuning pass):** with stand-in ratings for wave 2, the reference deck takes the title
  15.5% (the spec's model) to 23.0% (wave 1 at its measured ratings), the late-Library best 7.5-12.1%. The No Limits Pack
  pool is 44 cards with wave 1 ready.

## 15. Risks

| Risk | What could happen | Mitigation | Gate |
|---|---|---|---|
| **AI on the three new playbooks** | Remy's opponent-turn Synchros, Margo's banish loop and Inka's Mind Scan play badly or stall; a build without its playbook plays like Maliss today (1,392) | Spikes first (7.4), engine fixes in C1, and `ready: no` until it passes | `--peak-check`, `--playbook-ab`, `--turn1`, `--endless-test` |
| **The reference deck** | Targets judged against tearlaments (1,947) assume a player with a 2026-strength deck; a typical post-game deck wins 25-35% | Owner decision 1; the study prints the QA deck's row so the owner sees both | `--alley-study` rows |
| **Weak and strong bases** | Junia can't reach 50% (N18); Tova and Anselm (base 1,867) may pass 65% the other way even with few picks | Junia judged by the band (decision 2); Tova holds Pot of Greed back; picks are the only lever, never a weaker list | 4.7 window and band |
| **Elo is 85 points off head-to-head** (Phase 0) | Ratings place a build in the window, and duels disagree | The study is the gate; ratings are only a guide | `--alley-study` |
| **Secondary-source lists** (P1) | A wrong change in TCG October 2025 or May 2026 mislabels Augie's or Imogen's `over:` | The chain check against TCG September 2026 | `--deckbuilding-test` |
| **Shared playbooks** (N11) | An addition changes snake-eye or ryzeal in circuits and cups | Additive-only, a before/after check, derived classes when needed | 7.1's check |
| **The Brawl** | Still over 45% (Elo model) or too hard for the player who just cleared The Black Market (15% at 1,700) | Out-of-towners gated to The Black Market's clear; a 4th Basement card is the lever; payouts unchanged | `--bracket-sim` |
| **Repetition** | Anselm and Tova share the Ryzeal engine; Imogen and Lux are the same 2026 family by origin | Families keep them off one night; rule 5; decision 4 | Night-draw check |
| **Stakes that can't lead** | Tenpai, Toon and Expulsion can't lead; a future build's card may not either | 5.1 rule 7 fails the data, not the game | AlleyTest |
| **MAMS data churn** | Names, `ot` flags, the import packs and the shop all move on 13 Nov 2026 | P5 as its own step; Inka is authored, so only her leads and the player's route move | `AlleyData.Problems`, `--ocg-imports` |
| **Strong players' haul** | About 15,800 of premium slabs a night, Paid 0 | NO-LIMITS-ECONOMY: bounded by 4 seats and one restock per lead | `--arbitrage` |

## 16. Owner decisions

**Decided (owner, 2026-11-02): every recommendation below is accepted**, together with NO-LIMITS-ECONOMY's and
NO-LIMITS-STORY's. Where this spec differs from those docs (the out-of-towners at The Black Market's clear on
authored splash decks), this spec's recommendation is the one accepted.

The recommendation comes first; build it unless told otherwise. Items already decided in META-DECKS and the two
parallel docs' recommendations are adopted and not repeated, except where this spec differs.

1. **The reference deck for No Limits' targets:** the highest-rated deck legal under the No Limits list as built
   (tearlaments, 1,947 today), as `--bracket-sim` defines "a champion's best deck". The QA deck's row is printed beside
   it. *Or the QA save's best deck (Fire King Snake-Eye): every build would need to sit near 1,620, below every wave-1
   base.*
2. **The wave-1 end chair:** Junia (NO-LIMITS-STORY's host and META-DECKS' fame pick), judged against the 45-65% band
   rather than 50%. Her base can't reach 50% within the +250 cap (N18). Remy, the real end chair, must meet 50% in wave
   2. *Or seat the strongest measured wave-1 build (likely Tova or Imogen) in the chair until Remy is ready, which
   contradicts the story's host.*
3. **The out-of-towners join the Brawl at The Black Market's clear,** on authored Basement-splash decks (Alley-legal),
   seeded one per round. **This differs from both parallel docs:**
   - NO-LIMITS-STORY seats them on their legal decks as built;
   - NO-LIMITS-ECONOMY suggests The Basement Game's clear if a typical player drops under 20%.

   N17's model puts as-built decks at 57-61% for a champion (over the band), and puts any gate before The Black Market
   at 9-15% for a typical player. *Or as built from The Basement Game's clear (champion about 57%, OVER).*
4. **Tova and Anselm as NO-LIMITS-STORY casts them** (Fiendsmith Ryzeal and Ryzeal Mitsurugi: the real peaks), kept
   apart by the family rule. *Or a pure Ryzeal for Tova and a Mitsurugi with no Ryzeal engine for Anselm: less
   overlap, but not the decks that dominated.*
5. **The end chair is exempt from the family rule** (4.4), so Rue and Tova can sit while Junia closes. *Or count it:
   wave 1 then has 4 eligible pool builds for most nights.*
6. **Champion order:** Remy closes once ready and Junia wildcards (NO-LIMITS-STORY's question 2). That means no 50/50
   between two unbeaten champions at this table, unlike the headliner tables. *Or Junia keeps closing as host.*
7. **Pool seats rotate** (every build sits once before any twice) instead of drawing independently each night. *Or
   independent seeded draws (more repeats; a 6-build pool repeats a given build about 50% of nights).*
8. **Shared playbooks, additive-only through `#recipe <base>`**, with derived classes only when an existing rule must
   change; Maliss, Kewl Tune and Toon get their own keys. *Or derived classes for every build (safer for the bases,
   more code).*
9. **No `Alley.Events` entry for No Limits** (N16); the table's `elo:` window replaces it. *Or add one and exempt it
   from every splash study.*
10. **Tenpai without Cold Wave**, so it leads with Giant Trunade (NO-LIMITS-ECONOMY named Trunade or Cold Wave). *Or Cold
    Wave in, at most 2 of Cold Wave, Trunade and Raigeki, if the bot handles its window.*
11. **Kewl Tune's real list goes into decks/history** (rated anchored now; it joins the pool at the next full re-rate,
    and the 2025-26 cup can use it later). *Or only the peak build exists.*
