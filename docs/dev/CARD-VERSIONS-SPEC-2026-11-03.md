# Card versions: build spec (2026-11-03)

This is a Game Designer agent's build spec for the owner's question: "Can you get original and errata cards both in the
game? I want to be able to get both or more versions of the [card] and play the card as their original text
intended." It is written against the code as it stands on `main` (3d973d0), in the shape of
[ALLEY-SPEC-2026-11-01.md](ALLEY-SPEC-2026-11-01.md). Nothing in it is built yet, and no code was changed while
writing it.

- **Source inventory:** the version inventory written for this spec (session scratchpad `versions/inventory.md`).
  Section 1 below gives the facts it established, so this doc stands on its own.
- **Numbers are hypotheses.** Every number marked [P] waits for the Economy Designer, or for a measurement.
- **Owner rules kept:**
  - CPUs play their decks at full strength. No CPU deck is downgraded, and an era switch only lands if the deck
    measures as strong.
  - Every DP change goes through `Profile.Spend` or `Profile.Earn`, and nothing new is DP-positive.
  - Old saves load.
  - Real cards and real sets: a copy's text comes from a real printing's date.

**Changelog**
- v1 (2026-11-03): first spec.
- v1.1 (2026-11-03): phase 1's engine and harness built; their "As built" note is in section 11.
- v1.2 (2026-11-03): phase 1 review fixes (section 11's "As built": the Goat texts date cap, list-history gaps, partner
  decks, the editor at a circuit, `never` rows hidden; Norden and Mind Master dated 2026-09; risk 3's phase 1 note).

## Contents

1. What already exists (don't re-spec it)
2. Findings in the code (read before building)
3. The design on one page
4. Versions as collectibles
5. Where an original text plays
6. Old rules: per-format duel modes
7. Economy
8. Small fixes
9. Data formats
10. Binding API
11. Build split
12. Checks
13. Knobs
14. Risks
15. Owner decisions
16. Originals worth scripting later

---

## 1. What already exists (don't re-spec it)

- **251 cards have a second text, and all of them are scripted.**
  - 191 `(GOAT)` entries in `vendor/BabelCDB/goat-entries.cdb` play the card as ruled in April 2005. 73 of them
    differ mainly by the 2005 "failed search reveals your deck" rule (`Duel.GoatConfirm`). The other 118 differ by a
    2005 ruling or text.
  - 69 `(Pre-Errata)` rows in `cards-unofficial.cdb` cover 66 cards. They include all 31 cards on Konami's
    major-errata list.
  - Each version is its own card entry whose `alias` is the modern card. So `Banlist.BaseCode`
    (`src/Ygo.Engine/DeckBuilding/Banlist.cs:32-36`) counts all versions as one card for limits, the collection,
    unlocks, packs, the alley and ante.
- **Inside a duel,** `card::get_code()` returns the alias, so other scripts see a version as the modern card.
  `QUERY_CODE` reports the version's own id, so the client and the AI see the exact version.
- **`CardVersions`** (`src/Ygo.Engine/DeckBuilding/CardVersions.cs`) groups each card's versions:
  - `KindOf` tells the kind by the name's suffix.
  - `Arts` and `Texts` group entries by name.
  - `Original(code, preferGoat)` picks the Goat entry for 2005 lists and the Pre-Errata entry for earlier ones.
  - `IsOriginalEra` is true for a list from 2005 or earlier.
- **The deck editor** (`unity/Assets/Scripts/DeckEditor.cs`):
  - The CARD TEXT button cycles a card's texts (`CycleText`, 2049-2058).
  - `ChooseVersion` (2066-2087) switches every copy of the card in the deck to the chosen version.
  - `Resolve` (2029-2039) adds originals by default under a 2004-2005 list.
  - `ShowVersions` (2115-2128) shows the labels.
- **Copies** (`CardCopy`, `src/Ygo.Engine/Campaign/Profile.cs:60-88`) carry the base code, rarity, grade, seed, id,
  source, paid, slab, floor, submissions and readyAt. They have no printing and no text. `Collection` is keyed by base
  code with 3 copies at most, and `Assign` (425-438) gives each deck slot the best copy of its base card.
- **Duels** all run Master Rule 5. `DuelSettings.Flags = DuelFlags.ModeMR5` (`src/Ygo.Engine/Duel.cs:11`), and
  nothing sets anything else. ygopro-core already supports `DUEL_MODE_MR1`, `MR2` and `GOAT`
  (`vendor/ygopro-core/ocgapi_constants.h`). Replays store the flags and the exact codes of both decks
  (`Replay.cs:162-164, 212`).
- **Authored decks hold version ids.**
  - yata-mags, chaos-yata, goat-control and scientist-turtle use them.
  - Their playbooks key on those ids: `YataLockPlaybook.cs:16-18` and `ScientistTurtlePlaybook.cs:13`.
  - The Goat Cup's other decks (stall-burn, gadgets) play current texts.

## 2. Findings in the code (read before building)

- **F1. The exploit.**
  - Nothing ties a version to a format. `DeckRules.Validate` (`DeckRules.cs:39-50`) and `Banlist.Allowed` count by
    base code.
  - Under `TCG October 2026` (`lflists/history.lflist.conf`), CED, Sangan, Witch, Catapult Turtle, Sinister Serpent,
    DMoC, Crush Card Virus, Ring of Destruction, Brain Control, Firewall Dragon, Norden and Future Fusion are
    unlisted.
  - So 3 pre-errata CED (or Turtles, or Serpents) are legal in a Master League deck today.
- **F2. Era-limited mode decides whether a circuit's list applies at all.** `CampaignState.ListFor`
  (`CampaignState.cs:412`) returns null outside era-limited play, so a circuit's deck answers to no list.
  - Texts are rules, not restrictions, so this spec keys them to the circuit's list date in both modes (section 5).
  - The Beginner circuits have no `banlist:` line, only `era: 2005`.
- **F3. Set dates are per prefix, so the earliest date wins.**
  - `PrintingIndex.Sets` is keyed by prefix, and `PrintingsImport.cs:53-55` keeps the earliest date.
  - The 25th-anniversary "Spell Ruler (25th Anniversary Edition)" (2023) therefore reads as 2002. It reprints
    Darkness Approaches, one of our Pre-Errata cards.
  - The JUMP prefix spans 2006-2018, so CED's JUMP-EN086 errata printing (November 2018) reads as 2006.
  - `PrizePacks.cs:140` dates printings the same way.
  - So **a printing must be dated by its set name.** `cardsets.json` has a date for each name. Boosters are safe:
    `PackCatalog` builds a pack from the name most printings carry under the prefix, so the MRD booster is the 2002
    set.
- **F4. Every copy-creation site knows, or could know, a printing, but none keeps it.**
  - Packs: `Profile.OpenPack` (744) and `BoosterBox.Open` (72).
  - Structure decks (766), the starter (794) and recipe or trader buys: `DeckAccess.cs:192, 219`.
  - Special orders: `GradeEconomy.cs:290`. Used market: 232/261.
  - Prize Packs (`PrizePacks.cs:262`) and Contraband Packs (`ContrabandPacks.cs:97`). Both already carry `SetCode`
    in `PrizePull`.
  - Unlocks: `CardUnlocks.cs:383` and `unity/.../DuelController.Unlocks.cs:84`. Level rewards: `Leveling.cs:132`.
  - The van: `MothsVan.cs:81, 147, 194`. Ante: `Ante.cs:267`.
- **F5. Copy fields always end their save lines.** `copy`, `rail`, `anteyours` (`CopyFields`, `Profile.cs:1017`),
  `stake` and `antetheirs` (`StakeFields`, 1037) all end with the copy's or stake's fields. Optional trailing fields
  are therefore safe both ways: an old build ignores them, and a new build defaults them.
- **F6. `CardVersions` tells texts apart by name.**
  - The two "Mind Master (Pre-Errata)" entries carry different texts:
    - 96782896 says "except Mind Master";
    - 511003019 doesn't.

    They collapse into one text with "2 artworks". Mind Master has one TCG printing (2008), so only one of them is
    a TCG printed text.
  - Crush Card Virus 511000822/823 differ slightly in script. 822 checks `IsAttackAbove(1500)`; 823 checks
    `GetAttack()>=1500 and IsDestructable()`.
  - Ring of Destruction 511000824/825 have identical scripts. Three decks use 511000825.
  - Both pairs show as a fake second artwork.
- **F7. `#originals` is never read.** `Deck.NoteKeys` doesn't include it, so the three decks that carry it lose it on
  parse.
- **F8. Playbooks match exact codes.**
  - The `Code ==` checks in `AI/Playbooks/*.cs` and `TurnPlanner.cs:360-362, 537, 553` compare exact ids.
  - Switching a CPU deck's card to a version id makes its playbook stop seeing that card. This is the main cost of
    giving era decks their era texts (section 5.6).
- **F9. Two Pre-Errata bases have different ids in YGOPRODeck's main entry.** Metalmorph is 68540058 here and 68540059
  there; Ultimate Offering is 80604091 here and 80604092 there. Printing lookups must resolve through the import's
  image-id mapping. The `--texts-check` asserts that every row has printings.
- **F10. Toon World (Pre-Errata) 511004007 is the OCG's pre-TCG text.** No TCG printing carries it, so it can't be a
  collectible.
- **F11. The alley has era caps.** Dumpster Row is 2006, the Basement 2014 and the Black Market 2019
  (`Alley.cs:263-265`). The alley's own deck rules have no era ("any card released by now").
- **F12. Performapal Pendulum Sorcerer's TCG errata came with BOSH (2016-01-14).** The only deck that plays it
  (`decks/history/performapal.ydk`) is in the Nekroz & PePe Cup, whose pool runs to 2016-07. So its original text
  would be legal at no table (section 16).

## 3. The design on one page

1. **A copy carries a printing and the text it was printed with.** The printing is a set code and its date, for
   example "MRD-EN075, 2002-06". The text is either the current text or one dated original from the card's text
   timeline. For example, "Sangan (Pre-Errata)" was printed until 2016-09.
   - A printing dated before the card's errata carries the original text, so the text follows from the printing.
   - You can own a 2002 Sangan and a 2023 Sangan side by side as different copies. They count as one card for the
     3-copy cap and ban lists.
2. **Goat is a format, not a printing.** A 2002 Sangan and a 2004 Sangan have the same text. "Goat Sangan" is how an
   original copy is ruled at a Goat table, so GOAT entries are a ruling layer applied by the table:
   - an original copy plays its GOAT entry at a Goat table, and its Pre-Errata entry at other tables that allow
     originals;
   - for the 185 cards whose GOAT entry is a ruling only, the editor still offers the 2005 ruling as a choice, and it
     plays only at Goat tables.

   (Decision D1. The alternative is three collectible kinds.)
3. **An original plays as printed only where its text was in force.** Each table has a texts date, `AsOf`. An
   original plays as printed when its errata came after `AsOf`. This is the same "released by" rule the card pool
   already uses. At a later format the copy plays its **current text**, as Konami rules old printings, and the deck
   is not refused.
   - That closes F1: 3 pre-errata CED in a Master League deck play as 3 current CED.
   - Decision D3 is whether to refuse the deck instead.
4. **The Back Alley plays what's printed** (`Printed`). This keeps the POST-GAME-PLAN promise "old contraband plays
   with its original text". The alley's exclusions (`Never`, `BossOnly`) apply to originals too. (Decision D2.)
5. **CPU decks are exempt, as they are from ban lists.**
   - Authored version ids stay as written and are never downgraded.
   - Era texts come to CPU decks only by an opt-in per deck, once the deck has been re-measured (Phase 3).
6. **Price stays "one price":** rarity and grade, nothing for being original (D5).
   - Originals are found, not bought at a premium. They come from old sets' packs, era prize packs, the used market,
     the van and ante.
   - The arbitrage rules (Sell desk ≤ half of Paid, refund caps) are untouched.
7. **Old saves:** every existing copy loads as "current text, printing unknown". There is no grant (D6).
8. **Duel modes:** DUEL_MODE_GOAT for the Goat Cup is a later, gated step (Phase 4, decision D7). It is not part of
   this build.

## 4. Versions as collectibles

### 4.1 What a copy records

`CardCopy` gains three fields. All are optional on load (section 9.2).

| Field | Type | Meaning |
|---|---|---|
| `Set` | string | The printing's set code, e.g. "MRD-EN075". "" when unknown (old saves) |
| `Printed` | string | The printing's release month "yyyy-mm", by **set name** (F3). "" when unknown |
| `Text` | uint | The version entry the printing carries: a timeline row's id (section 9.1), or 0 for the current text |

- `Text` is derived: `CardTexts.PrintedText(base, Printed)`. Copies with an unknown printing are 0.
- `Text` is stored, though, so a later correction to `texts.txt` never silently changes a copy you own. The check
  reports any copy whose stored text disagrees with its printing; the owner decides whether to rewrite it.
- A copy of a card with no timeline row always has `Text` 0. Most cards have no row, so the field costs nothing for
  them.

### 4.2 Where each version comes from

The rule everywhere is to pick a printing, date it by its set name, and derive the text from the date. Printings
picked from a seed use `Hash.Mix` on that seed, never the shared `Random`. QA runs are deterministic, and listings
must not reroll on update.

| Source | Code | Printing it gets | So, originals? |
|---|---|---|---|
| Booster, booster box | `Profile.OpenPack`, `BoosterBox.Open` | The pack's own set: the card's printing in that set at the pulled rarity (the first by set code), dated as the set (`PackSet.Info.Date`) | **Yes, from old sets.** The back catalog's LOB, MRD, PSV, IOC, DR1-3, DB1-2 etc. give original Sangan, Witch, CED, DMoC, Turtle, Imperial Order... OCG imports (`ImportShelf.IsImport`) give current text |
| Structure deck | `Profile.BuyStructureDeck` | The structure deck's set when its deck file names one (new optional `#set <prefix>`), else as a shop copy | By date (e.g. SDKS 2016: CCV current) |
| Shop copy, Card Trader, "Make it yours" | `DeckAccess` | The newest printing at that rarity dated ≤ the shop year (`Circuits.ShopYear`) | In era-limited play, early circuits sell originals; post-game buys are current |
| Special order | `UsedMarket.Order` | As a shop copy | Current in the post-game. Originals are not orderable (D5, knob `OrdersGiveOriginals`) |
| Starter | `Profile.New` | The newest printing at that rarity | Current |
| Level-up and story rewards | `Leveling` | As the starter | Current |
| Rival and bond gifts | `CardUnlocks`, `DuelController.Unlocks` | The newest printing ≤ the band's era year (`Circuits.BandEra`) | A Beginner-band gift is a 2005-or-earlier printing, which fits "first printed by the era where you meet them" |
| Locals Prize Packs | `PrizePacks.Give` | `PrizePull.SetCode`, already picked in the pool's era; date it by set name | **Yes.** Goat Cup Locals prizes are 2005 printings |
| Contraband Packs | `ContrabandPacks.Give` | `PrizePull.SetCode`, already picked (any date) | Yes, whenever the picked printing is old |
| Used market | `UsedMarket.Listings` | Among the card's in-era printings at the listing's rarity, by `Hash.Mix(seed, 0x5E7)` [P] | Sometimes. The listing shows "ORIGINAL TEXT · 2004" |
| Moth's van, used and shelf | `MothsVan.Used`, `Shelf` | As the used market, any date. `ShelfKey` is unchanged (code.rarity.grade), so sold marks stay valid | Sometimes |
| Ante stakes | `Ante.Draw`, `PennyBinder` | At draw, among the card's printings at that rarity: those dated ≤ the CPU deck's `#era` when any, else any, by hash of the stake's seed. Penny's Kuriboh is its earliest Common printing | **Yes, often**: era decks stake era printings. "Chaos Emperor Dragon · IOC 2004 · original text" |
| Library cup clear | `Library.Record` | No card today. Unchanged | none |
| Ines's held Scapegoat | `MothsVan.HeldCode` | Display only: its earliest printing ≤ 2005-04 | A 2005 printing on the rail (Scapegoat has no errata; its GOAT ruling is the table's) |
| Old saves | none | Unknown | Current |

### 4.3 Owning several versions

- **The cap is unchanged** (D4). There are 3 copies per card across all its versions, so you can own 2 originals and 1
  current. Since an original plays current text wherever the original isn't allowed, 3 originals work everywhere.
  The collection goal is therefore "3 originals" for the cards whose originals matter.
- **Keep the lone version** (knob `KeepLoneText`). A 4th copy still keeps your best three, by `Collection.Take`. One
  change: a copy that is your only copy of its text never goes, and nor does a new copy whose text you don't own yet.
  - The copy that goes is the lowest-valued Raw copy among those whose text you own at least 2 of, plus the new one
    if its text is already owned.
  - If no such copy exists, today's rule applies.
  - Why: without this, a pulled 2002 Sangan worth less than your three 2023 ones would be sold on the spot.
- **`Collection.Assign` per text** (section 10):
  - A slot asking for an original gets a copy of that text, best first.
  - Current slots then take the remaining copies, current-text copies first (so originals stay free), then
    originals.
  - A GOAT entry of a card with a timeline needs the copy of that card's earliest original. A GOAT entry of a
    ruling-only card takes any copy.
- **`CollectionRules.Validate` per text.** It keeps today's per-card count and adds: "Sangan: the deck plays 2 with
  the original text; you own 1 original copy."

### 4.4 Existing saves

- Copies load with `Set`, `Printed` = "" and `Text` = 0: current text, printing unknown. The binder shows "printing
  not recorded".
- **Recommendation (D6): no grant.**
  - Any grant would invent printings, which breaks "real sets".
  - The paths to originals open the moment the build lands: the back catalog, Locals prizes and ante.
  - A save's decks keep their version ids (section 5.5). A deck that asked for originals now needs original copies,
    and until it has them those slots play current text. The editor says so per card.
- **The other option:** treat `CopySource.Old` copies (saves from before copies were recorded) as originals of their
  card's earliest printing. It's cheap, but it's a guess, and it hands originals to the oldest saves only.

## 5. Where an original text plays

### 5.1 The texts date of a table

A new `TextRules` value (section 10) goes on every `FormatRules`:

| Context | `AsOf` | `Printed` | `Goat` |
|---|---|---|---|
| Library cup, its Locals, an Every Era round | the cup's `pool:` month | no | `AsOf` in [`GoatFrom`, `GoatUntil`] = 2005-04..2005-09 [P]: the Goat Cup only |
| Circuit with a `banlist:` (either mode, F2) | `DeckRules.FormatEnd(list)`, or today ("9999-12") for the newest list | no | same test |
| Circuit without a list (the Beginner band) | `<era>-12` (2005-12) | no | no |
| Tag Cups, guests | their circuit's | | |
| Back Alley: tables, Orvel's, Nyx's, Brawl, Midnight Open, No Limits | (unused) | **yes** | no |
| Free duel and the editor | the chosen list's `FormatEnd` (today for the newest) | yes for "No limits" | same test |

An original plays as printed when `Printed || row.Until > AsOf` (string compare of "yyyy-mm"). This is the same
inclusive rule as `DeckRules.Released`: a card released in the pool's month is in the pool, so an errata printed in
that month is in force.

### 5.2 What a slot plays (the one function)

`PlayTexts.PlayCode(slot, copy, rules)` is called for the player's deck at duel start. CPU decks are never passed
through it (5.6).

1. **Base card or alternate art:**
   - At a Goat table, a ruling-only card with a GOAT entry plays the GOAT entry if the slot asked for it, which the
     editor does by default under a Goat list.
   - Otherwise the slot plays as written.
2. **A version entry (Pre-Errata or GOAT):**
   - The assigned copy must carry an original text. Without one (only in a loaner, or with `copy` null in Phase 1),
     the slot plays current in Phase 2 and as written in Phase 1.
   - If the table doesn't allow the copy's text (`!rules.Allows(row)`), the slot plays the **current** code, keeping
     the copy's alternate art if the deck chose one.
   - Else at a Goat table, it plays the card's GOAT entry if it has one; else the copy's own text row.
   - Else it plays the copy's own text row. This is the Pre-Errata entry, or for Mind Master the row matching the
     printing.
3. A GOAT slot at a table that isn't Goat falls back to step 2 without the Goat branch: the Pre-Errata entry if the
   copy is original and allowed, else current.

`PlayTexts.ForTable(deck, copies, rules)` maps every slot and returns the mapped deck plus a note list. It runs in
`DuelController` right before `Collection.Assign` (`DuelController.cs:1501-1503`). So the replay (`_recording.Decks`)
records the codes actually played, and old replays are untouched.

### 5.3 Where the famous originals play (worked from the rule)

| Original (errata, TCG) | Plays as printed at |
|---|---|
| Catapult Turtle (2014-06, DL18) | Goat (2005), TeleDAD (2009), Synchro Peak (2012), Rabbits & Rulers (2013) cups; Beginner band; the 2011 circuits; the alley |
| Sinister Serpent, CCV, RoD, Exchange, Temple (2015-03, PGL2) | The cups through HAT & Shaddoll (2014-12); 2005 and 2011 circuits; the alley |
| DMoC (2015-11, YGLD) | As above, plus the 2015 circuits (AsOf 2015-07) |
| Sangan (2016-09, DPRP) | Cups through Nekroz & PePe (2016-07); circuits through the 2015 band; the alley. Goat Cup: as its GOAT entry |
| Witch, Rescue Cat, Brain Control, Imperial Order, Future Fusion (2017) | Cups through Nekroz & PePe; circuits through 2015; the alley |
| CED (2018-11, JUMP-EN086) | Cups through ABC to SPYRAL (2017-10); circuits through 2015 (Forbidden in most of those lists anyway); the alley |
| Firewall Dragon (2021-04, GFTP) | Link Dawn Cup (2019-09); the Golden Age band (AsOf 2020-06); the alley |
| Elder Entity Norden, Mind Master (2026-09, the TCG database change) | Every cup; every circuit but the September and October 2026 Master League lists; the alley |
| Any original | Never under TCG October 2026 (F1 closed) |

### 5.4 The deck editor

- **Detail line, own-copies view per version:** "own 3 · 1 original (MRD-EN075, 2002) · 2 current". The binder and
  the card detail list each copy with its set and year, and an "ORIGINAL TEXT" tag where it applies.
- **CARD TEXT toggle:**
  - In the campaign (`OwnedCards != null`), it cycles only the texts you own a copy of, plus the 2005 ruling of a
    ruling-only card when the editor's list is a Goat list.
  - In free duel (no collection), it cycles every text, as today.
- **Choosing a version** (`ChooseVersion`) switches up to as many of the deck's slots as you own copies of that text,
  so one deck can mix versions. The status line reads "Switched 1 of 3 Sangan to its original text (you own 1
  original copy)."
- **Adding from search** (`Resolve`) adds the preferred version if you have a copy of it not yet in the deck, else the
  current text.
- **The legality note** (`_versionNote`), always one of:
  - "Original text, as printed until September 2016. It plays as printed under this list."
  - "Original text. Under TCG October 2026 it plays its current text (errata'd September 2016)."
  - "Plays as printed in the Back Alley."
  - "You don't own an original copy. Old sets in the back catalog print it."
- `IsOriginalEra` and `Original()` keep working for the default under old lists. `TextRules` replaces their legality
  role.

### 5.5 The versus screen

- When any slot is downgraded, the versus screen shows one line: "2 cards play their current text here: Sangan,
  Witch of the Black Forest." This is a note, not a refusal (D3).
- `CampaignState.Problems` adds the per-text collection problem from 4.3, which does refuse. As today, you can't
  play copies you don't own.

### 5.6 CPU decks

- **As authored.** CPUs are exempt from lists, and from text dates in the same way. chaos-yata in the TeleDAD Locals
  plays its 2004 texts.
- **Era texts for era decks (Phase 3, opt-in):**
  - A deck file may say `#texts era`. Its slots are then mapped once at load, by the same `PlayCode` with
    `Printed = false`, the deck's `#banlist` date as `AsOf`, and the CPU owning every version.
  - Before a deck gets the line:
    1. its playbook must match by base code (F8; section 10, chunk 3.1);
    2. it is re-measured, and it lands only if its rating holds within the measuring error or rises.

    This is the owner's "full strength" rule. GoatConfirm texts are weaker, so some decks won't qualify.
- **Generated alley decks (Phase 3).** `Alley.Splash` gives each splashed contraband card its original text when the
  card has a timeline row and a TCG printing dated ≤ the event's `EraCap`. Dumpster Row's CED, Witch and Sangan, and
  the Basement's CCV, play as printed. This builds POST-GAME-PLAN line 209. The base decks are unchanged.
- **`#originals`** becomes a read note and a lint (section 8).

## 6. Old rules: per-format duel modes

**What the core offers.** `DUEL_MODE_GOAT` is MR1 plus:
- TCG fast-effect ignition and Traps in a new chain;
- the 6-step battle step with attack replays;
- triggers on private knowledge;
- equips staying with a missing target;
- 0 ATK destroyed;
- a single chain in the damage substep;
- TCG SEGOC.

MR1 and MR2 add a turn-1 draw and one face-up Field Spell. All of these are whole-duel flags; none can apply per card.

**What using it would take:**
1. **Data and engine.** An optional `rules: goat` line on a cup in `campaign/library.txt` sets `FormatRules.Mode`.
   `DuelFlags.ModeGoat` and `ModeMR2` constants go in `Constants.cs`, and `DuelSettings.Flags` is set from the mode
   for both seats. Replays already store the flags.
2. **Client.** Handle the prompts GOAT mode can raise that MR5 never does. The attack-replay yes/no and battle-step
   re-targeting need checking. The freeze-prone skip-to-my-turn path (commit 6330165) needs a GOAT run too.
3. **AI.**
   - `SmartDuelist` treats `Board.Turn <= 1` as "no attack, no draw" (`SmartDuelist.Sense.cs:144`,
     `Taxers.cs:99`).
   - Playbooks use `V.IsFirstTurn` for MR5 first-turn combos.
   - The turn-1 draw changes go-first or second (`campaign/toss-advice.txt`, `docs/dev/GO-FIRST-OR-SECOND-2026-10-26.md`).
   - The planner runs `DuelRunner` with the recipe's settings, so it inherits the flags. That needs checking.
4. **Checks.**
   - 1,000 harness Goat Cup duels with 0 freezes.
   - `--endless-test` at or below today's rate.
   - A 72-duel re-measure of the Goat Cup field and Locals, and Every Era round 1.
   - `-replaycheck` on a GOAT replay.
5. **The player is told** on the cup's panel: "Goat rules: the first player draws, and battles and chains work as in
   2005."

**Risks.**
- Unknown prompt shapes could freeze a duel.
- Ratings shift, and the cup's difficulty with them.
- The GOAT scripts were written for GOAT mode and run under MR5 today. They may behave better or differently once the
  mode is on.

**Recommendation (D7): a later step, the Goat Cup only.** That is Phase 4: the Goat Cup, its Locals and Every Era
round 1, behind `rules: goat`, landing only when the checks above pass.
- MR1/MR2: no table needs them, because there's no 2002-2004 cup.
- MR3/MR4 for the Pendulum and Link-era cups: not recommended. MR4's Extra Monster Zone would gut decks measured
  under MR5 and needs zone-aware AI.

## 7. Economy

The Economy Designer should confirm everything here.

- **Pricing: no premium for originals** (D5, knob `OriginalPremium` = 0).
  - `Pricing.V` stays rarity × grade, so nothing about selling, grading, stake ranges, shelf prices or pack shares
    moves. This matches NO-LIMITS-ECONOMY's "one price stays".
  - An original's worth is where it plays, and the binder tag.
  - With a premium, old-set packs would gain EV over new ones, and the used market and van would list originals
    above their pack share. Both would need new caps.
- **Sell desk: unchanged.** It pays min(V/4, Paid/2), so an original bought cheap in a pack still sells for less than
  its share.
- **Packs: old sets' pulls carry the original text.** Pack contents, prices (`ShopPrices.Booster`) and odds are
  unchanged.
- **Special orders: current printings only** (knob `OrdersGiveOriginals` = false [P]).
  - If the Economy Designer wants an order path, the suggestion is the basis × 2.0 [P], not × 1.5.
  - An order is a pure sink, so it can't be DP-positive. It does make originals trivially available, though.
- **The keep-lone-text rule** (4.3) changes which copy a 4th copy sells. A refund still pays at most a pack's half
  price (`OpenPack`, `BoosterBox`), so the rule is DP-neutral or DP-negative. `--arbitrage` and `--postgame-sim`
  re-run to confirm.
- **Ante:** stake values are V, unchanged. More originals reach the player through ante wins at Paid 0, which never
  sell for DP.
- **Loops to keep negative:**
  1. Buy an old pack, sell the original. Capped by Paid/2.
  2. Buy used, re-grade, sell. Unchanged.
  3. Pull an original as a 4th copy and refund the displaced copy. Capped per pack.
  4. Order, then sell. Paid/2.

  None of these involves a text-specific number.

## 8. Small fixes

- **S1. Texts by identity, not name** (F6).
  - A text is a version entry. `CardVersions.Texts` returns the base plus one entry per **timeline row or GOAT
    entry**, by code.
  - `Arts` returns only same-kind current-text entries, so originals have exactly one artwork.
  - Labels come from the row: "Original text (until Sep 2016)".
  - Mind Master then shows two original texts; the row marked `cpu` is hidden from the player's toggle (9.1).
- **S2. Duplicate rows.** `texts.txt` marks each duplicate `dup <canonical>`: 511000823 → 511000822 and 511000824 →
  511000825 (the id the decks use).
  - `CardVersions` hides a dup from `Texts` and `Arts`.
  - The editor shows a deck slot holding a dup as its canonical text.
  - The dup stays loadable, and is never rewritten in decks or replays (replays need stable ids).
- **S3. `#originals`** joins `Deck.NoteKeys`, so it survives parse and save, and becomes a lint.
  - In `tools/check_history_decks.py` and the `--texts-check`: a deck with `#originals` and `#banlist` must use an
    original version id for every card whose timeline allows it at that list's `AsOf`.
  - A deliberate exception is written `#current <id> ...`.
  - The lint finds today, for example, any 2005 card in goat-control left on its current id.
- **S4. Set dates by name** (F3). `PrintingIndex` gains `DateOf(Printing)` from a set-name table. `printings.bin`
  goes to version 4, because the reader throws on a version mismatch; regenerate with `--export-printings`.
  `PrizePacks.cs:140` switches to it.
- **S5.** The Red-Eyes Darkness Metal Dragon script folders are backwards (inventory: the modern script in
  `pre-errata/`, the Pre-Errata entry in `official/`). This is harmless with the search order, so leave it and note
  it in `texts.txt`'s header.

## 9. Data formats

### 9.1 `campaign/texts.txt` (new)

```
# Card text timeline (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md). One line per original printed TCG text:
#   <version id> until <yyyy-mm> [set <prefix>] [cpu] [dup <id>]   # card name
#   until: the month the TCG text changed (the first printing with the new text, or the database change).
#          A printing dated before it carries this text; a table whose AsOf is before it plays it.
#   never: no TCG printing carries it (an OCG or anime text): CPUs may play it, players can't own it.
#   cpu:   playable by CPUs, not collectible (the second of two texts no TCG printing carries).
#   dup:   an identical entry; the canonical id is collectible, this one only loads.
# Rows for one card are read oldest first. GOAT entries need no row (they are the 2005 ruling, section 5).
511000228 until 2014-06 set DL18     # Catapult Turtle
511000229 until 2014-10 set LC5D     # Dark Strike Fighter
511000868 until 2015-10 set AP08     # Twin-Headed Behemoth
511000822 until 2015-03 set PGL2     # Crush Card Virus
511000823 dup 511000822              # Crush Card Virus
511000825 until 2015-03 set PGL2     # Ring of Destruction
511000824 dup 511000825              # Ring of Destruction
511000820 until 2015-03 set PGL2     # Exchange of the Spirit
511000821 until 2015-03 set PGL2     # Temple of the Kings
511000818 until 2015-03 set PGL2     # Sinister Serpent
511000819 until 2018-11              # Chaos Emperor Dragon (JUMP-EN086, Nov 2018) [verify]
511001039 until 2015-11 set YGLD     # Dark Magician of Chaos
511002631 until 2016-09 set DPRP     # Sangan
511002997 until 2017-03 set DUSA     # Future Fusion
511002996 until 2017-03 set DUSA     # Imperial Order
511002995 until 2017-03 set DUSA     # Brain Control
511002994 until 2017-03 set DUSA     # Goyo Guardian
511002993 until 2017-03 set DUSA     # Brionac
511002992 until 2017-03 set DUSA     # Rescue Cat
511003012 until 2017-07 set BLLR     # Witch of the Black Forest
511003062 until 2018-05 set FLOD     # Grandsoil
511003116 until 2018-10 set LEHD     # Disk Commander
88264988  until 2020-07 set LDS1     # Red-Eyes Darkness Metal Dragon
21593987  until 2020-09 set LED7     # Makyura
70583996  until 2021-02 set SDFC     # Dewloren
5043020   until 2021-04 set GFTP     # Firewall Dragon
16226796  until 2022-06 set OP19     # Night Assailant
25862691  until 2023-03 set MAZE     # Ancient Fairy Dragon
71525242  until 2024-??              # Gandora-X (TCG database) [research]
61665255  until 2025-08 set BLMM     # Summon Sorceress
7563589   until 2024-08              # Performage Plushfire (TCG database) [verify]
17412731  until 2026-07              # Elder Entity Norden
511003019 until ????-?? / cpu        # Mind Master: which entry is the 2008 TCG print [research]
96782896  until 2026-07 / cpu        # Mind Master
511004007 never                      # Toon World: OCG text
# ... the other 35 Pre-Errata cards (wording fixes, Unions, the 2017 Main Monster Zone wave), each researched
```

- **Research chunk.** The 35 wording-fix rows, plus Gandora-X, Plushfire, CED and Mind Master, need dates from
  Yugipedia's "Card Errata:<name>" pages, which list each printing's text.
  - The Unions changed with MR3, and the Main-Monster-Zone wave with MR4 (2017-03).
  - The check refuses a missing row or a `?`, because a wrong date mis-assigns copies forever (4.1).
- **Months from sets** come from `cardsets.json` by set name: DL18 2014-06-27, LC5D 2014-10-23, AP08 2015-10-23, PGL2
  2015-03-19, YGLD 2015-11-12, DPRP 2016-09-15, DUSA 2017-03-30, BLLR 2017-07-06, FLOD 2018-05-03, LEHD 2018-10-04,
  LDS1 2020-07-02, LED7 2020-09-24, SDFC 2021-02-18, GFTP 2021-04-15, OP19 2022-06-15, MAZE 2023-03-09, BLMM
  2025-08-28.

### 9.2 Save lines (optional trailing fields, F5)

- `copy <11 fields> <text> <set|-> <yyyy-mm|->`: `CopyFields` gains the three. `copy`, `rail` and `anteyours` get
  them for free.
  - Parse: `parts.Length > 11`. A missing field gives 0 / "" / "".
  - `ReadCopy` reads `at+11..at+13` when present.
- `stake ... <flag> <set|-> <yyyy-mm|->` and `antetheirs ...`: `StakeFields` gains `Set` and `Printed`. A stake's text
  is derived when it is won (`Ante.ToCopy`).
- There is no `version` bump and no new line kinds. An old build reading a new save ignores the trailing fields. The
  copy keeps its fields until the old build re-saves; then they are lost, and the copy reads as "printing unknown",
  which is harmless.

### 9.3 Deck and campaign files

- `.ydk`:
  - `#originals` is now a read note (S3).
  - `#texts era` is the CPU opt-in (5.6, Phase 3).
  - `#set <prefix>` gives a structure deck's printing (4.2).
  - `#current <id> ...` is a lint exception.
- `campaign/library.txt`: an optional `rules: goat` line on a cup (Phase 4).

## 10. Binding API

These signatures are binding. Chunk 1 of each phase lands them first.

```csharp
// New: src/Ygo.Engine/DeckBuilding/CardTexts.cs
public sealed class TextRow { public uint Code, Base; public string Until = ""; public string Set = "";
                              public bool Never, CpuOnly; public uint DupOf; }
public sealed class CardTexts
{
    public const string File = "texts.txt";
    public static CardTexts Parse(string text, ICardDataProvider cards);
    public static CardTexts Load(string path, ICardDataProvider cards);
    public static readonly CardTexts Empty;
    public IReadOnlyList<TextRow> Timeline(uint baseCode);      // collectible + cpu rows, oldest first; no dups
    public TextRow? Row(uint versionCode);                      // a dup resolves to its canonical row
    public uint Goat(uint baseCode);                            // its GOAT entry, or 0
    public uint PrintedText(uint baseCode, string printedYm);   // the row in force at that date, 0 = current
    public string Label(uint versionCode);                      // "Original text (until Sep 2016)"
    public List<string> Problems(ICardDataProvider cards, PrintingIndex printings); // --texts-check
}

// New: src/Ygo.Engine/DeckBuilding/TextRules.cs
public readonly struct TextRules
{
    public readonly string AsOf; public readonly bool Printed, Goat;
    public TextRules(string asOf, bool printed, bool goat);
    public static TextRules Today { get; }      // "9999-12"
    public static TextRules PrintedText { get; }
    public static TextRules ForList(Banlist? list, IEnumerable<Banlist> all, string pool = "", int eraYear = 0);
    public bool Allows(TextRow row);            // Printed || string.CompareOrdinal(row.Until, AsOf) > 0
    public string Describe();                   // "texts of September 2016" / "as printed" / "Goat rulings"
}
public static class PlayTexts
{
    public static uint PlayCode(uint slot, CardCopy? copy, TextRules rules, CardTexts texts, ICardDataProvider cards);
    public static Deck ForTable(Deck deck, IReadOnlyList<CardCopy?>? copies, TextRules rules, CardTexts texts,
                                ICardDataProvider cards, out List<uint> downgraded);
}

// src/Ygo.Engine/Campaign/Library.cs
public sealed class FormatRules { /* existing */ public TextRules Texts = TextRules.Today; public ulong Mode; } // Mode: Phase 4

// src/Ygo.Engine/Campaign/Profile.cs
public sealed class CardCopy { /* existing */ public uint Text; public string Set = "", Printed = ""; }
public sealed class Collection
{
    public CardTexts Texts = CardTexts.Empty;                   // set where CommonestOf is set
    public int Count(uint baseCode, uint text);                 // playable copies of one text
    public void Stamp(CardCopy copy, string setCode, string printedYm); // sets Set, Printed, Text
    // Assign and Take: behaviour of 4.3; signatures unchanged.
}
// Ante.cs: Stake gains Set, Printed.   Data/Printings.cs: PrintingIndex.DateOf(Printing) -> "yyyy-mm-dd"; Version = 4.
// Phase 2 helper, src/Ygo.Engine/Campaign/CopyPrints.cs (new):
public static class CopyPrints
{
    public static Printing? InSet(uint code, Rarity r, string setPrefix, PrintingIndex p, ICardDataProvider c);
    public static Printing? Newest(uint code, Rarity r, int maxYear, PrintingIndex p, ICardDataProvider c);
    public static Printing? BySeed(uint code, Rarity r, int maxYear, uint seed, PrintingIndex p, ICardDataProvider c);
}
```

## 11. Build split

Within a phase, chunk 1 (engine and data) lands its API first. Then the Unity chunk and the harness chunk run in
parallel, on disjoint files. New `src/Ygo.Engine/*.cs` files need `.meta` files. New `campaign/*.txt` files reach the
app through `ProjectSetup`'s copy.

### Phase 1: close the exploit, fix the small things (about 0.5 session). Value: high; effort: small

- **1.0 Data research** (data agent, web): `campaign/texts.txt` with all 69 Pre-Errata rows (9.1). It blocks 1.1's
  check, not its code.
- **1.1 Engine:**
  - new `CardTexts.cs` and `TextRules.cs` (incl. `PlayTexts`);
  - `CardVersions.cs` (S1, S2);
  - `Library.cs` (`FormatRules.Texts`; `Rules` and `RoundRules` fill it from `pool:`);
  - `Alley.cs` (`Rules` sets `Printed`);
  - `Deck.cs` (`NoteKeys` += originals).
- **1.2 Unity:**
  - `CampaignState.cs`: `Rules(circuit)` fills `Texts` from the circuit list in both modes, or its era. Tag Cups and
    guests use the same.
  - `DuelController.cs` (`ForTable` before `Assign`, copies null);
  - `DeckEditor.cs` (the note from 5.4 against the selected list; `Resolve` via `TextRules`);
  - the versus line.
- **1.3 Harness:**
  - `--texts-check` (new, in `src/Ygo.Harness/TextsCheck.cs`);
  - `DeckBuildingTest.cs`: the `PlayCode` matrix, S1/S2, Sangan's texts;
  - `CampaignTest.cs`: the exploit case;
  - `tools/check_history_decks.py` (S3 lint).

**As built (phase 1, 1.1 and 1.3).** Section 10's signatures hold; these are the additions and the places the build
differs:
- **The Goat test** (5.1) reads the pool month, or else the **list's own month**, not `AsOf`. With `AsOf` it would make
  TCG April 2004 a Goat table (its format ends 2005-04, inside the window). The `2005.4 GOAT` list itself is
  `AsOf` 2005-09 and Goat; its format end would be TCG September 2008, the next TCG list on record. TCG April 2005's
  format end is also 2008-09 for that reason, so a Goat table by its list's month takes `AsOf = min(FormatEnd,
  GoatUntil)`: TCG April 2005 plays 2005-09 texts (Ultimate Offering's original, errata'd 2006, plays as printed
  there). `tools/check_history_decks.py`'s `texts_date` does the same. No circuit uses that list; the Goat Cup goes by
  its pool.
- **Gaps in the list history (a data job, not code):** `lflists/history.lflist.conf` has no TCG list between September
  2018 and October 2019, nor between October 2019 and June 2020 (the December 2018 to July 2019 lists and January 2020
  are missing). So TCG September 2018's texts date reads 2019-10 (really 2018-12) and October 2019's reads 2020-06
  (really 2020-01). No texts.txt row's `until` falls between the real and the recorded date (CED's 2018-11 is in force
  either way), so nothing plays differently today; adding the missing lists fixes it for future rows.
- **Your decks in a partner's hands** play the table's texts as yours do: a campaign deck of yours lent to Kit, or
  your `#partner` edit of a partner's deck (`CampaignState.PlaysYourTexts`). A partner's own deck and a loaner play as
  authored (5.6). The Tag Cup and guest-tag versus lines name both decks' downgraded cards in one list.
- **The editor at a circuit** reads the circuit's texts ("At this circuit ...") in both modes, even when the list
  showing is the deck's own `#banlist` (outside era-limited play a circuit opens the editor with no list), until you
  change the list by hand; the default version (`Resolve`) follows the same rule.
- **CARD TEXT hides `never` rows** as it hides `cpu` rows (a slot already holding one still shows it).
- **Amazoness Fighter's ATK 1300 original stays collectible** although BEUD's, Cipher Soldier's and Metalmorph's
  misprints are `never`: 1300 was the card's original printing (MFC-060, its only print until DR1), not a one-off error
  in a reprint. **Ultimate Offering** is `until 2006-04 set TP8`: the TP8-EN012 card image (2006-04-28) already
  carries "You can only activate this effect during your Main Phase or your opponent's Battle Phase", although
  Yugipedia's errata table cites the later SD7-EN027 (texts.txt notes it).
- **`PlayCode` keeps rows off the player that no copy can carry**: a `never` or `cpu` row plays current text, and a
  Pre-Errata entry with no row plays current except at a printed table, where it plays as written. So a missing
  texts.txt can't reopen F1. A ruling-only GOAT slot plays current text in the alley (it's a 2005 ruling, not a
  printed text). A slot whose text is the one played keeps its id, so a dup is never rewritten. Phase 3's CPU
  mapping needs a variant in which the CPU owns every row, `cpu` rows included.
- **`CardCopy` already has `Text`, `Set` and `Printed`**, in memory only: phase 1 doesn't stamp or save them (no save
  change), so `PlayCode` and its tests can read a copy's text. Phase 2 stamps and saves them (9.2).
- **`CardTexts`** also has `Rows`, `Count`, `IsDup(code)`, `Earliest(base)` (the row a GOAT entry needs) and
  `ShortMonth`. `Timeline` includes `never` rows too. `Parse` leaves out a row whose id isn't a Pre-Errata entry and
  reports it. `Problems(cards, printings, dateOf = null)` takes the printing-date function. `--texts-check` passes
  dates by set name from `cardsets.json` until S4's `PrintingIndex.DateOf` lands in phase 2.
- **`CardVersions(cards, texts = null)`** gains `TextRows`, `Canonical(code)` (a dup's canonical entry) and
  `TextLabel(code)`. `Original` skips `never` and `cpu` rows, so it doesn't default to a text the player can't own.
- **`Deck.NoteKeys`** gains `current` as well as `originals`, so the lint's exceptions survive a save.
- Not built in phase 1: `FormatRules.Mode` (phase 4), and the `Downgrade` and `AlleyPrinted` knobs as code (D2 and D3
  are decided).

### Phase 2: versions as collectibles (about 1.5 sessions). Value: high; effort: medium-large

**Found in phase 1's research (must land before any copy is stamped):** ygoprodeck lists the 25th Anniversary
Edition reprints (2023-04-20) of Sangan, Catapult Turtle, Witch of the Black Forest (MRD), Imperial Order and
Inspection (PSV) and Toon World (SRL) only under the 2002 set name with the same code, so dating by set name (S4)
would read them as originals that never existed. All of them carry the current text. S4 needs another source for
those printings (e.g. Yugipedia's 25th Anniversary set lists) or an override list. Stronghold the Moving Fortress's
2013 DPYG Special Edition reprint has the same problem (it shares the 2009 code).

- **2.1 Engine:**
  - `Profile.cs` (`CardCopy` fields, save lines 9.2, `Count(base, text)`, `Stamp`, `Assign` and `Take` per 4.3,
    `CollectionRules.Validate` per text);
  - `Data/Printings.cs` and `Ygo.Harness/PrintingsImport.cs` (S4, version 4);
  - new `CopyPrints.cs`;
  - every creation site in F4: `Packs.cs`/`Profile.OpenPack`, `BoosterBox.cs`, `DeckAccess.cs`, `GradeEconomy.cs`,
    `PrizePacks.cs`, `ContrabandPacks.cs`, `CardUnlocks.cs`, `Leveling.cs`, `MothsVan.cs`, `Ante.cs`.
- **2.2 Unity:**
  - `DeckEditor.cs` (own-copies line, toggle limited to owned, `ChooseVersion` up to owned, `Resolve`);
  - `CardDetail.cs` (copy rows with set, year and tag);
  - `ShopScreen.cs` (reveal tag);
  - `GradingDeskScreen.cs` and `VanScreen.cs` (listing tags; the HELD row's year);
  - `DuelController.Unlocks.cs` (stamp);
  - `DuelController.cs` (pass `Assign`'s copies to `ForTable`).
- **2.3 Harness:**
  - `CopiesTest.cs`: save round trip, an old line loads, composite lines, determinism of stamps, lone-text keep;
  - `--arbitrage` and `--postgame-sim` re-runs, plus a pack-EV comparison before and after;
  - `CampaignTest.cs`: per-text collection problems.

**As built (phase 2, 2026-11-05).** Section 10's signatures hold. Build plan: docs/dev/CV2-BUILD-2026-11-05.md. Where the
build differs or adds:
- **Decision (coordinator, following 4.4 and D3): a per-text shortfall never refuses a deck.** 5.5's "which does refuse"
  is dropped: a deck whose original-text slots outnumber your original copies plays the rest as current text. The new
  `CollectionRules.TextShortfalls(deck, collection, texts, cards)` lists them as notes ("Sangan: 2 slots ask for the
  original text; you own 1 original copy, so 1 plays its current text"), shown in amber in the editor and as a second
  sentence on the versus line. `CollectionRules.Validate` keeps only today's per-card count, which still refuses. Why:
  no old save owns an original, so refusing would lock goat-control, yata-mags and the other recipe decks out.
- **The reprints (risk 2):** campaign/printing-dates.txt (14 lines, cited in its header) dates the 25th Anniversary
  Edition reprints of LOB, MRD, SRL, PSV, DCR and IOC (their `-EN` codes, 2023-04-20) and the DPYG/DPKB rows reprinted
  under the same codes with current text (2013-10 and 2017-09). `--export-printings` bakes it into printings.bin
  version 4. A booster still gives its own set's printing (packs date pulls by the pack). LON and LOD weren't reprinted;
  their `-EN` rows are European printings of the time. `--texts-check` fails a future data refresh that adds an
  undated reprint, by set name and by the code pattern.
- **Stamping:** a copy is stamped only when `Collection.Texts` is loaded (a bare collection stays "printing not
  recorded", so a missing texts.txt can't freeze copies as current). `Collection.Printings` is new and set with Texts
  (`CampaignState.WireCollection`). Ante stakes from old saves are stamped the first time their binder is read.
  `PrizePacks.PoolByName` (dating the prize pool by set name) is off, because turning it on changes 88 of 480 sampled
  Prize Packs; each pull is still dated by its set name.
- **KeepLoneText** lets same-text copies swap: among your Raw copies (not at the desk) and the new one, a copy may go
  only if its text appears at least twice among the four; the lowest-valued such copy goes; else today's rule.
  `--pack-ev`: DP-neutral from an empty collection, -56 DP over 229 sets × 60 packs with sets finished by paid current
  copies; no pack paid more than half its price.
- **Contents unchanged:** `--cv2-dump` (every booster, boxes, structure decks, shop and trader buys, 600 used listings,
  orders, 480 Prize Packs, 150 Contraband Packs, the van, 193 binder stakes) is byte-identical before and after; only
  the "what was kept" file moves, where KeepLoneText chose differently (2 of 1,100 lines).
- **Not built:** no structure deck carries `#set` yet (they stamp as shop copies). Side Deck slots get no copy in
  `ForTable` (the game never sides). An old build re-saving a new save drops the three fields (9.2, inherent).
- **Review fixes (two Code Reviewer passes, 2026-11-05):** KeepLoneText refused a better copy of a text you owned (now
  the swap above); DCR and IOC reprints were undated; a collection without texts stamped copies as current forever;
  ante binder reads rebuilt decks every time; a copy whose row later turns non-collectible counted as original; GOAT
  slots counted only the earliest original; the editor checked loaner and partner decks against stand-in copies (now
  your real copies, `Loaners.Borrowed`); `-versioncheck` could leave recovery files (now needs `-mods`); tags read the
  raw text id (now play's test). The pack reveal's side panel shows an original pull's own text.
- **Checks:** `--campaign-test` 341, `--copies-test` 44 (41 new), `--deckbuilding-test` 90, `--texts-check` 10,
  `--arbitrage` 0 DP-positive; app checks all green first time (campaign 169, shop 42, alley 96; the rest unchanged),
  `-versioncheck` 20.
- **Decided (owner, 2026-11-05):** `PoolByName` stays off (pack contents unchanged); one original copy may play in both
  decks of a tag duel when your partner brings a deck of yours, as card counts already work.

### Phase 3: era texts for CPUs and the alley (about 1 session plus measurement runs). Value: medium; effort: medium

- **3.1 Engine:**
  - `AI/Playbooks/Playbook.cs` and `AI/TurnPlanner.cs`: code matches compare base codes (`Banlist.BaseCode` on both
    sides), mechanical across the `Code ==` sites (F8). This also fixes phase 1's mapped decks losing playbook
    matches (the `-qaplay` stand-in, a partner with your deck: risk 3);
  - `Deck.cs` (`#texts era`, mapping at load);
  - `Alley.cs` (`Splash` gives era originals, 5.6).
- **3.2 Harness:** `--remeasure` for each deck proposed for `#texts era` (start with the Goat Cup field:
  stall-burn, gadgets); `--alley-gen`, `--alley-rate` and `--endless-test` with originals on; the playbook tests
  (`--playbook-test`).
- **3.3 Data:** `#texts era` lines only for decks that pass. `Alley.Never`/`BossOnly` additions only if the endless
  test flags an original (knob `AlleyCurrentOnly`).

### Phase 4: Goat rules for the Goat Cup (about 0.5 session code plus 1 measurement session). Value: medium; effort: small code, large verification

- Section 6 as written:
  - `Constants.cs`, `Library.cs` (`rules:`), `CampaignState.cs` and `DuelController.cs` (flags);
  - the AI first-turn branches;
  - harness freeze, endless and Elo runs;
  - `-replaycheck` on a GOAT replay.

### Phase 5: not recommended now

Section 16.

## 12. Checks

| Check | Phase | Proves |
|---|---|---|
| `--texts-check` (new) | 1 | Every Pre-Errata entry has a row or `never`, `dup` or `cpu`; `until` parses, with no `?`; a `set` prefix's name-date month equals `until`; every collectible row's card has at least one TCG printing dated before `until`; no row for a GOAT entry |
| `--deckbuilding-test` | 1 | `PlayCode` over {base, Pre-Errata, GOAT, dup} × {null, current, original copy} × {Today, 2005-09 Goat, 2016-07, Printed}; Mind Master has 2 texts; RoD and CCV have 1 art |
| `--campaign-test` | 1-2 | 3 pre-errata CED in a Master League deck play 3 current CED (the replay's codes); the Goat Cup plays GOAT Sangan for an original copy and current Sangan for a current copy; the alley plays it as printed |
| `--copies-test` | 2 | A save with the new fields round-trips; a v1 copy line loads as text 0; `rail`/`anteyours`/`stake` lines with and without trailing fields; stamps are the same for the same seed; lone-text keep |
| `--arbitrage`, `--postgame-sim` | 2 | No loop nets DP > 0; ledger totals within ±1% of before, except `refund.*` [P] |
| `tools/check_history_decks.py` | 1 | The `#originals` lint |
| `--remeasure`, `--alley-rate`, `--endless-test` | 3-4 | Era-text decks hold their rating; no new endless duels |
| Unity `-versioncheck` (extend `DeckEditor.DebugVersionCheck`) | 1-2 | The note text per list; the toggle shows owned texts only; switching 1 of 3; the versus line |
| Unity `-replaycheck` | 1, 4 | A replay keeps the mapped codes and the GOAT flags |

## 13. Knobs

| Knob | Default | Where | Note |
|---|---|---|---|
| `GoatFrom`, `GoatUntil` | 2005-04, 2005-09 | `TextRules` | A table is "Goat" when its AsOf falls in this window [P] |
| `Downgrade` | true | `PlayTexts` | false refuses the deck instead (D3) |
| `AlleyPrinted` | true | `Alley.Rules` | false uses each event's `EraCap` as AsOf (D2) |
| `AlleyCurrentOnly` | empty | `Alley.cs` | Originals the alley plays as current, if the endless test flags one |
| `KeepLoneText` | true | `Collection.Take` | 4.3 |
| `OriginalPremium` | 0 | `Pricing` | Per mille added to V for an original copy [P, Economy Designer] |
| `OrdersGiveOriginals` | false | `UsedMarket.Order` | If true, the price is basis × 2.0 [P] |
| `UsedPrintingSalt` | 0x5E7 | `CopyPrints.BySeed` | The hash salt (changing it rerolls printings, not listings) |

## 14. Risks

1. **Wrong errata dates assign the wrong text to copies forever.** The research chunk and `--texts-check` gate this.
   Copies store `Text`, so a later fix never silently changes owned copies.
2. **Prefix dates (F3).** Phase 2 must not ship before S4. Otherwise JUMP-EN086 and the 25th-anniversary reprints
   produce originals that never existed.
3. **Playbooks keyed on exact ids (F8).** Phase 3's base-code matching touches every playbook, so the playbook tests
   and a re-measure must run. Phases 1-2 don't touch CPU decks.
   - Phase 1 already feeds a mapped deck to two playbook users: the `-qaplay` stand-in's advisor
     (`DuelController.cs`, built from `decks[0]` after `ForTable`) and Kit or a partner bringing your deck. A slot
     mapped to another id (an original to current, Sangan to its GOAT entry) no longer matches the deck's playbook
     entries, so those plays weaken a little at tables that change texts. Not fixed in code now: phase 3's base-code
     matching (3.1) fixes it with nothing else to do. Until then, read a QA win-rate drop at a modern table with
     originals in the QA deck with this in mind.
4. **Originals in the alley** (pre-errata Firewall, Summon Sorceress, Norden, Ancient Fairy Dragon at 3).
   - These are loops the bot may not end, and they make easier wins against CPUs.
   - Mitigations: the turn-40 cap, `Never`/`BossOnly`, `--endless-test` and `AlleyCurrentOnly`.
   - Splash Elo moves once CPUs splash originals (Phase 3), so re-measure the splash gain.
5. **Determinism.** A printing pick that touches a shared `Random` would reroll packs, listings and QA seeds. Use
   `Hash.Mix` on existing seeds only.
6. **Player confusion:** "my original plays current here". Mitigated by the editor note, the versus line and one
   tooltip; the replay shows the codes actually played.
7. **Replays.** Version ids and their scripts must never change. Dups stay loadable; nothing is rewritten.
8. **GOAT mode (Phase 4):** unhandled prompts or freezes, the turn-1 draw's balance, and ratings. It stays out until
   the checks in section 6 pass.

## 15. Owner decisions

**Decided (owner, 2026-11-03): every recommendation below is accepted (D1-D9).**

The recommendation comes first; build it unless told otherwise.

1. **D1. What "a version" is.**
   - *Recommended:* two printed texts per card (original, current), with Goat as the table's 2005 ruling of an
     original copy. A 2002 Sangan is "the original", and plays as Goat Sangan at the Goat Cup.
   - *Or:* three collectible kinds, with "Goat" stamped on printings dated 2004-10 to 2005-09. That's more to
     collect, but those printings carry the same text as a 2002 copy.
2. **D2. Where originals play outside dated formats.**
   - *Recommended:* the whole Back Alley, No Limits included, plays as printed, with the alley's exclusions.
   - *Or:* alley tables use their era cap (Dumpster 2006, Basement 2014, Black Market 2019), and the uncapped tables
     (Orvel's, Nyx's, brackets, No Limits) play current texts.
3. **D3. An original at a format after its errata.**
   - *Recommended:* it plays its current text, with a versus note, as Konami rules old printings.
   - *Or:* the deck is refused until you switch the card.
4. **D4. The copy cap.**
   - *Recommended:* 3 per card across versions.
   - *Or:* 3 per text. That doubles the cap for 66 cards and touches every 4th-copy refund.
5. **D5. Price.**
   - *Recommended:* no premium for originals; one price stays.
   - *Or:* `OriginalPremium` [P], with the Economy Designer re-capping pack shares and the van.
6. **D6. Existing saves.**
   - *Recommended:* all copies load as current text, printing unknown, with no grant.
   - *Or:* `CopySource.Old` copies become originals of their earliest printing.
7. **D7. Duel modes.**
   - *Recommended:* GOAT mode later (Phase 4), the Goat Cup only, and gated.
   - *Or:* now with Phase 1, which risks freezes and unmeasured difficulty.
   - *Or:* never.
8. **D8. CPU era texts.**
   - *Recommended:* opt-in per authored deck (`#texts era`) after a re-measure, plus era originals in generated alley
     splashes.
   - *Or:* auto-map every era deck, which needs every playbook converted and every deck re-measured first.
9. **D9. Mixed versions in one deck.**
   - *Recommended:* yes. `ChooseVersion` switches as many slots as you own copies of that text.
   - *Or:* one version per card per deck, as today. A deck then needs 3 originals before it can use any.

## 16. Originals worth scripting later

None now. Each candidate is checked against section 5's rule (where would it play?):

- **Performapal Pendulum Sorcerer (TCG text):** the errata was BOSH, 2016-01. The only deck that plays it is in the
  Nekroz & PePe Cup (pool 2016-07), so the original would play nowhere in the campaign except the alley. Skip.
- **The other Union monsters** (beyond Y/Z/W, Heavy Mech and the GOAT XY/XYZ/XZ/YZ): no deck plays them. Skip unless
  a Union deck is added.
- **The rest of the 2017 Main-Monster-Zone wave** (Ground Collapse, Shifting Shadows, Needle Wall...): the wording
  only matters with an Extra Monster Zone, so it changes nothing under MR5. Skip.
- **A TCG Toon World:** the repo's is the OCG text (`never`). Worth one script only if a Toon deck joins a pre-2005
  table.
- **Tyrant Dragon (GOAT):** its own header says it is not fully implemented. Fix it if Phase 4 lands and a Goat deck
  plays it.
