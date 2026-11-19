# Card-data refresh (2026-11-19)

Part R of [NEXT-BUILD-2026-11-19.md](NEXT-BUILD-2026-11-19.md). Nothing is committed. Logs, old copies and helper
scripts are in `build/cardrefresh/` (logs in `build/cardrefresh/logs/`).

## Result in short

- The three submodules are on their newest commits. `vendor/ygopro-core` was not touched.
- No card id moved, vanished or changed. 81 card ids were added (47 pre-release cards, 34 alternate artworks). Every
  id in `decks/**` and in a copy of the owner's save is still in the data. No deck needed an id fix.
- Script errors: 0 everywhere, including 40 duels between two decks made of every card whose script is new or changed.
- `cards.bin` is the new export and is installed. `printings.bin` was first kept as it was (see "printings.bin"
  below); after the importer fix it is re-exported and installed (see "Importer fix" at the end).
- Every check gives the same summary before and after. The few changed lines are explained below.

## Pins

| Submodule | Old pin | New pin | Commits |
|---|---|---|---|
| `vendor/CardScripts` | `070cdbb59` (2026-10-22 18:19 -0700) | `5b613bfc3` (2026-11-17 22:46 -0800, "Update Signal Vehicle") | 78 |
| `vendor/BabelCDB` | `d4f1107` (2026-10-22 15:56 +0200) | `7be9fd1` (2026-11-18 06:42 -0800, "Update cards-unofficial.cdb") | 47 |
| `vendor/LFLists` | `1c34c46` (2026-10-22 07:42 +0000) | `336ec7e` (2026-11-16 13:48 +0000, "Rush whitelist: typo") | 18 |

The plan's "2025-04-20" is the `git describe` tag, not the commit date: the old pins were from 2026-10-22.

Moved with `git -c core.autocrlf=false submodule update --remote vendor/CardScripts vendor/BabelCDB vendor/LFLists`,
then `git submodule foreach git config core.autocrlf false`.

## What changed in the data

**BabelCDB** (the cards the engine loads: no Rush, Skill, unofficial except Pre-Errata, GOAT entries as aliases):
15,282 -> 15,363 cards. Added 81, removed 0, changed 0 (name, text, stats, alias, setcode, `ot`, category all
compared). Full list: `build/cardrefresh/logs/cdb-diff.txt`.

- 34 alternate artworks in `cards.cdb` (alias set), e.g. Super Polymerization 48130398, Red-Eyes Dark Dragoon
  37818795, the three Egyptian Gods, Odd-Eyes Arcray Dragon 6218705 (an alt art of Supreme King Z-ARC).
- 47 pre-release cards: new `prerelease-paso.cdb` (Blue-Eyes Chaos MAXIMUM Dragon, Neo Chaos Form, Endless Darkness
  and others), more RV02 (Ghostrick, Cubic, Marincess), IMPH, MVB1 and others.

**CardScripts:** 155 files changed, none deleted or renamed. Non-card files: `card_counter_constants.lua` (two new
constants), `proc_rush.lua` (Rush only). 56 changed or new scripts belong to cards the engine loads; 2 of them are in
our decks: Angelechy Endgame Problem 101402095 (`decks/history/angelechy.ydk`) and Cyber Emergency 60600126
(`decks/history/drytron.ydk`, `decks/structure/machine-re-volt.ydk`).

**LFLists:** only `Rush.lflist.conf` and `Rush-Prerelease.lflist.conf` changed. The TCG, OCG, GOAT and other lists
are the same, so `lflists/` needs nothing.

**YGOPRODeck** (re-downloaded 2026-11-19 07:04 into `data/ygoprodeck/`; old copies in `build/cardrefresh/old/`):

| | Before (2026-11-11) | After |
|---|---|---|
| cardinfo.json cards | 14,664 | 14,683 |
| cardsets.json sets | 1,038 | 1,038 (file byte-identical) |
| Set names on cards | 1,034 | 1,034 (no new set) |
| Printings | 44,903 | 44,921 |
| "New" rarity printings | 101 | 119 |

- 20 new ids, all pre-release cards with no printing. One id gone: B.L.A.C.K. Parfait 100200299 now has its real id
  65276253 at YGOPRODeck; BabelCDB still has 100200299. It's in no deck.
- The only printing change: 18 cards gained a `MAMS-EN001`-`EN018` printing with rarity "New". These are YGOPRODeck's
  placeholder next to each card's real Grand Master, Ultra and Starlight printings.

**Card files** (exported with the new data):

| File | Before | After |
|---|---|---|
| `cards.bin` | 15,282 cards, 6,709 KB | 15,363 cards, 6,776 KB (installed) |
| `printings.bin` (new export) | 14,140 cards, 14,296 ids, 839 KB | same counts, 839 KB (not installed) |
| Rarity counts | Common 18,372 | Common 18,390 (+18, the "New" rows); every other rarity the same |

Re-exporting from the old pins gave files byte-identical to the shipped ones, so the "before" baseline is exactly what
the game shipped.

## printings.bin: kept as it was (decision for the owner)

`Rarity.Parse` (src/Ygo.Engine/Data/Printings.cs) reads "New" as Common. With the 18 placeholder rows, Magnificent
Maestros stops being a rarity set (no Commons). In `--campaign-test` with the new export:

- MAMS in the shop: "Ultra Rare, Secret Rare, Starlight Rare" became "Common, Ultra Rare, Secret Rare, Starlight Rare".
- A MAMS pack: 1 Ultra in 0.1 packs became 1 in 3.4; Starlight 1 in 21.6 became 1 in 71.4; most pulls became "Common"
  copies of the Grand Master cards.
- The pack price: 2,100 became 600, a box 40,320 became 11,520.

Magnificent Monsters (MAMO) already has the same 18 "New" rows today, so the shop already sells it that way.

The new export adds nothing real (no new set, no new real printing), so I kept the shipped `printings.bin`. The new
export is at `build/cardrefresh/new/printings.bin`; installing it is one copy.

*Recommended:* fix the importer to drop a "New" printing when the same set code has a real one (a few lines in
`Rarity.Parse`'s caller or `PrintingsImport`, owned by whoever takes `src/`), then re-export. That also fixes MAMO.
*Or* install the new export as it is (MAMS goes to 600 DP with Commons, like MAMO). Arbitrage passes either way
(0 DP-positive with both files).

## Checks

The "before" run used the same harness build (`build/bin/cardrefresh`) on the old pins and the shipped files. The
"after" runs used a scratch project root (`build/cardrefresh/root`: links to the repo's folders plus its own
`StreamingAssets`), so the new files were checked before they went into `unity/Assets/StreamingAssets`. "Final" is
the shipped combination: new pins and `cards.bin`, old `printings.bin`. The links are removed now.

| Check | Before | Final (shipped) | After with new printings.bin |
|---|---|---|---|
| `python tools/check_history_decks.py` | 74 decks ok, `0 problems` | same, log identical | (doesn't read printings) |
| `--deckbuilding-test` | All deck-building checks passed. (14,371 searchable) | passed (14,415 searchable) | passed |
| `--campaign-test` | All campaign checks passed; Ladder 59 passed | passed | passed |
| `--texts-check` | All texts checks passed. | identical | identical |
| `--copies-test` | All copy checks passed. | identical | identical |
| `--nolimits-test` | All No Limits checks passed. | identical | identical |
| `--arbitrage` | No buy -> sell loop is DP-positive (0 DP-positive in every exhaustive pass) | identical (only the timing line differs) | 0 DP-positive |
| `--games 30` | classic 8, modern 22; script errors 0, crashed 0 | identical | identical |
| `--endless-test --games 300` | exit 1: 3 No Limits decks off | exit 1: the same 3 off | same |
| `--loaner-check` (extra) | 923 loaners, 0 with problems | same | |
| `--card-search-test` (extra) | 156 ok, 0 FAIL | 155 ok, 0 FAIL, 1 skipped | |

No `--fix` was needed: no deck id moved.

**What moved, and why:**

- *Search counts* (deckbuilding, card search): more cards. "blue-eyes" 58 -> 66 results, "dragon lv8 atk>=3000"
  80 -> 82, and similar. These are the new pre-release cards.
- *`--card-search-test`* skips its old-query comparison: "the card database changed since the baseline was made".
  The baseline (`src/Ygo.Harness/fixtures/card-search-baseline.txt`) isn't mine; rewrite it with
  `--card-search-test --write-baseline` after reviewing.
- *`--campaign-test`, grading lines:* "comes back a 9 (true 8)" became "a 8 (true 8)", and "buy used, grade, sell"
  -9,734 DP became -9,171 DP (still never pays). The checks pick their cards from the card list in order (`cards.All`,
  `.Take(300)`), and the new alt arts sit early in that order. Both pass.
- *`--endless-test`:* one duel (seed 701018, nl-snake-eye-brawl vs nl-vanquish-soul-brawl) answered 1419 prompts
  instead of 1418; it was decided at turn 40 either way. A second run gave the same 1419, so duels are still
  deterministic. PLAUSIBLE cause: Crossout Designator in the Snake-Eye deck declares a card name, and the list of names
  grew.
- *`--endless-test` exits 1 before and after:* nl-ryzeal, nl-branded and nl-snake-eye-brawl are "OFF" (5% of their
  duels decided at turn 40). It's not from this refresh.

**Script-error smoke:** `build/cardrefresh/decks/changed-a.ydk` and `changed-b.ydk` hold every card the engine loads
whose script is new or changed (45 Main, 11 Extra). `--games 40 --seed 11`: "script errors: 0   crashed games: 0".
No newer `ygopro-core` is needed for these scripts.

## Old saves

No id moved or vanished, so no save can hold an id the new data lacks. Shown anyway:

- A copy of the owner's save (`build/cardrefresh/saves/owner-copy.profile.txt`, copied read-only; the original is
  byte-identical afterwards): 570 distinct card codes, 0 missing from the new data. `decks/**`: 1,737 ids, 0 missing.
- `--profile-check` on that copy with the new harness: "saved and loaded back unchanged".

## No Limits wave 2: are the cards in the data?

All by name, with scripts (full list: `build/cardrefresh/logs/wave2-cards.txt`). Nothing for wave 2 arrived in this
refresh: it was all there already.

- **Kewl Tune:** all 14 (JJ "Kewl Tune", B2B, Clip, Crackle, Cue, Loudness War, Mix, Playlist, RS, Reco, Remix, Rotary,
  Synchro, Track Maker), TCG (`ot` 3), printings in PHRE and BLZD.
- **Maliss:** all 12 (<C> GWC-06, MTP-07, TB-11; <P> Chessy Cat, Dormouse, March Hare, White Rabbit; <Q> Hearts
  Crypter, Red Ransom, White Binder; Maliss in Underground; Maliss in the Mirror), TCG.
- **Light and Darkness Ritual:** Light and Darkness Ritual, Light and Darkness Dragon, Light and Darkness Dragonlord,
  Black Luster Soldier - Envoy of the Beginning, Chaos Emperor Dragon - Envoy of the End, Eclipse Wyvern, Dark
  Magician of Chaos, Djinn Releaser of Rituals, Magicians' Souls, Red-Eyes Dark Dragoon, Charmer Quartet in Bloom,
  Cross-Sheep, Zenna's Deceiving Doll Maidens. All TCG.
- **Toon (Magnificent Maestros):** the cards and their MAMS printings were already in the shipped data (YGOPRODeck had
  MAMS on 2026-11-11). Toon World the Perfect World, Funny Dark Rabbit, Comic Cat, Evil Box, Dark-Eyes Illusionist
  Faceless Mage, Mind Scan, Blue-Eyes Toon Ultimate Dragon and Perfectron Hydradrive Dragon all have MAMS printings.
  BabelCDB has **not** caught up: they're still `ot` 1 (OCG only) under their OCG names. YGOPRODeck's TCG names are
  Perfect Toon World, Animimicat (Comic Cat); Blue-Eyes Ultimate Toon Dragon is still "Blue-Eyes Toon Ultimate Dragon"
  at both. So spec P5's "rename" check (`AlleyData.Problems`) has nothing to catch yet.

## OCG import packs (not changed: not mine)

`--ocg-imports` rewrites `campaign/ocg-imports.txt` in place. Run on today's data (before or after the refresh, the
same result apart from new cards), it would:

- drop 31 cards: the Witchcrafter pack, Blue-Eyes Toon Ultimate Dragon, Toon World the Perfect World, the Unchained
  cards and a few others (they have TCG printings now, mostly through MAMS, as spec P5 expected);
- add 44 cards, all of them new pre-release cards from this refresh.

Until it's re-run, the new pre-release cards are searchable and playable in free play but no shop sells them. The
generated file is `build/cardrefresh/logs/final-ocg-imports.generated.txt`. Trap: on Windows the tool writes CRLF
(`File.WriteAllLines`), so it needs LF before committing. I ran it once by accident on the real file and put the
committed text back (`git show HEAD:campaign/ocg-imports.txt`); `git status` shows it clean.

## Files changed

| File | Why |
|---|---|
| `vendor/CardScripts`, `vendor/BabelCDB`, `vendor/LFLists` (pins) | Newest commits |
| `data/ygoprodeck/cardinfo.json`, `cardsets.json` (not in git) | Re-downloaded |
| `unity/Assets/StreamingAssets/cards.bin` (not in git) | New export, 15,363 cards |
| `docs/dev/CARD-REFRESH-2026-11-19.md` | This report |

Later, with the importer fix: `src/Ygo.Harness/PrintingsImport.cs`, `src/Ygo.Harness/CopiesTest.cs` and
`unity/Assets/StreamingAssets/printings.bin` (see "Importer fix").

Not changed: `decks/**` (no id moved), `lflists/**` (no TCG list changed),
`campaign/texts.txt` (no card text changed, `--texts-check` passes).

## Traps found

- **Line endings in the submodules.** CardScripts (and parts of BabelCDB and LFLists) are checked out with CRLF:
  `* text=auto` in their `.gitattributes` with Windows' native `core.eol`. `core.autocrlf=false` doesn't stop that.
  It was so before this refresh (files dated 2026-11-11), the new files match, Lua doesn't care, and `git status` is
  clean. For LF, set `git -C vendor/<name> config core.eol lf` and check the files out again. I didn't (it rewrites
  23,000 files).
- **YGOPRODeck "New" rarity** reprices sets (above).
- **`--ocg-imports` writes into `campaign/`** (above).

## Not verified

- The game wasn't run (no Unity, no app checks). Nobody has opened the shop, search or deck editor with the new
  `cards.bin`.
- The new pre-release cards were only played by CPUs in the smoke decks, not checked for correct rulings.
- Rush, Skill and anime cards weren't checked (the engine doesn't load them).
- The other agents' edits (`src/Ygo.Engine/Campaign/TagPartners.cs`, `personalities/rookie-ryo.profile`, Unity scripts)
  landed while I ran. My harness was built before them, so my before and after runs used the same code.

## Importer fix (2026-11-19, later the same day)

Asked by the coordinator after the first report. Rule: drop a YGOPRODeck printing whose rarity is "New" when the same
card has a printing with a real rarity under the same set name. A "New" row that is the card's only one in its set
stays, as Common, as before. The import logs the count.

**Code** (harness only; the engine's `Rarity.Parse` is unchanged):

- `src/Ygo.Harness/PrintingsImport.cs`: `DropPlaceholders(rows, out dropped)` filters each card's rows before they're
  parsed, counted or used for the set-code table; `Run(json, out, out droppedPlaceholders)` prints
  `N "New" placeholder printings dropped (the card has a real rarity in that set)`. `SelfTest()` is the test.
- `src/Ygo.Harness/CopiesTest.cs`: one line, `--copies-test` runs `PrintingsImport.SelfTest()`.

**The test** writes a fixture `cardinfo.json` to a temp folder, imports it, and loads the result back with
`PrintingIndex.Load`. With the fix:

```
  ok   a card printed Ultra Rare and "New" in one set keeps only the Ultra Rare row (FXFS-EN001 Ultra Rare)
  ok   a "New" row alone in its set stays, as Common (FXNW-EN001 Common)
  ok   a "New" row beside a real printing in another set stays (FXOL-EN001 Super Rare, FXNW-EN002 Common)
  ok   the import counts the rows it dropped (1, expected 1)
All copy checks passed.
```

Without the fix (a scratch copy of the harness with the filter turned off, built in `build/cardrefresh/nofix`):

```
  FAIL a card printed Ultra Rare and "New" in one set keeps only the Ultra Rare row (FXFS-EN001 Ultra Rare, FXFS-EN001 Common)
  FAIL the import counts the rows it dropped (0, expected 1)
2 copy checks failed.
```

**Re-export** from the new `data/ygoprodeck/cardinfo.json`: `47 "New" placeholder printings dropped`, 14,140 cards,
14,296 ids, 838 KB. The 47: MAMS 18, MAMO 18, L26D 7, LAVD 4. The other 72 "New" rows are a card's only printing in
their set (Maze of the Master 22, Battles of Legend: Monster Mayhem 22, Glorious Gallery 16, and a few) and stay.

**Compared with the shipped `printings.bin`** (both dumped to sorted text by `build/cardrefresh/dump`):

- Rows: 49,648 shipped, 49,619 fixed. 29 rows removed (MAMO 18, L26D 7, LAVD 4), all "Common", each beside a kept
  real printing of the same card in the same set. 0 rows added. Every other row is identical.
- Set table, set-name dates and reprint dates: identical.
- MAMS: still "Ultra Rare, Secret Rare, Starlight Rare"; 3,000 packs pull 25,709 Ultra, 1,152 Secret, 139 Starlight,
  exactly as shipped; the campaign test's saves still spend `packs -2,100` and `packs.box -40,320`.
- MAMO: was "Common, Ultra Rare, Secret Rare, Starlight Rare", now "Ultra Rare, Secret Rare, Starlight Rare". So it is
  a rarity set now and costs as one (by `ShopPrices`, 2,100 a pack like MAMS; inferred from the code, not printed by
  a check).
- 29 card ids lose their only Common printing, so their cheapest rarity (and the copy and recipe prices built on it)
  goes up: Dark Magician, the Pharaoh's Servant, Favorite HERO Flame Wingman and Shining Flare Wingman, Multiplying
  Kuriboh!, Winged Kuriboh Sabatiel LV10, Cyberse Code Magician, Decode Talker Integration, Odd-Eyes Pendulum Dragon
  of the Four Heavenly Dragons, Stardust Dragon - Victim Sanctuary, Sky Striker Ace - Camellia, three X-Sabers,
  Commander Souza, and others. The owner's save (the copy) holds none of them. A save that does hold a "Common" copy
  of one still loads; the copy is now valued from the card's new cheapest rarity, and the Sell desk still pays at
  most half of what was paid.

**Checks** (harness rebuilt into `build/bin/cardrefresh`; it now also compiles the Unity Architect's edit to
`src/Ygo.Engine/Campaign/TagPartners.cs`, so I ran the shipped `printings.bin` with the same build as the baseline):

| Check | Shipped printings.bin | Fixed printings.bin |
|---|---|---|
| `--campaign-test` | All campaign checks passed. | All campaign checks passed. |
| `--copies-test` | | All copy checks passed. (with the 4 new steps) |
| `--arbitrage` | No buy -> sell loop is DP-positive | No buy -> sell loop is DP-positive (every exhaustive pass 0 DP-positive) |
| `--deckbuilding-test` | | All deck-building checks passed., identical to before the fix |
| `--texts-check`, `--nolimits-test`, `--games 30` | | identical to before the fix |
| `--endless-test --games 300` | | identical to before the fix (exit 1, the same 3 decks off) |

`--campaign-test` lines that differ between the two files, all from the price changes above: MAMO's rarity list; the
"Ultra Rare at best" token example (Dual Avatar - Empowered Mitsu-Jaku became Cyberse Code Magician); Mitsurugi's
missing cards outright 30,390 became 31,230 DP; the scripted mix's `recipe.outright` 7,900 became 9,230; the 17-save
walk's `recipe.outright -9,870` became `-10,330`; boxes "40 upgraded a pack" became 39; "buy used, grade, sell"
-12,678 became -10,016 DP (still never pays).

Installed: `unity/Assets/StreamingAssets/printings.bin` is now the fixed export (`build/cardrefresh/fix/printings.bin`).
`campaign/ocg-imports.txt` was not regenerated (owner decision).

Not verified: the game wasn't run with the new file. MAMO's new pack price was read from the code, not from a check.
