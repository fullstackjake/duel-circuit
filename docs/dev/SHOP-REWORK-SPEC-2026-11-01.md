# Shop and packs rework spec (2026-11-01)

A Game Designer agent's build spec. It revises "A combined plan" in [PACKS-REVIEW-2026-11-01.md](PACKS-REVIEW-2026-11-01.md)
against the code and the card data as they stand today. Read-only on code: nothing here is built yet.

- **Keep the pack engine.** Change how the shop organizes and presents packs.
- **Opponent decks are not touched.** CPUs always play their decks at full strength. This rework only *reads* their
  lists (for tags, house packs and the featured-shelf check).
- **Prices are not set here.** The Economy Designer is measuring them in parallel (docs/dev/SHOP-PRICES-2026-11-01.md,
  `src/Ygo.Engine/Campaign/ShopPrices.cs`). This spec names the knobs.
- **Lines are not written here.** The Narrative Designer wrote them in parallel (docs/dev/SHOP-VOICE-2026-11-01.md, which
  landed while this was written). This spec adopts that doc's file format, hook names and pull rules, and defines where
  each hook shows, its trigger and how it is built deterministically (section 6).

**Decided (owner, 2026-11-01)**
- **A0, the catalog fix: yes.** Ship Table B's shelves (re-validated by the harness check after A0).
- **A1, the rarity-set rule: yes.**
- **Prices: all of docs/dev/SHOP-PRICES-2026-11-01.md's recommendations ship**:
  - house packs at half a recipe pack by band: 200 / 200 / 300 / 500 / 750 / 1,250;
  - Novice recipe packs 450, Beginner 300;
  - boosters by year: up to 2008 150, 2009-13 200, 2014-18 300, 2019-22 450, 2023 on 600;
  - rarity sets by base rarity: Rare 1.5×, Super 2.5×, Ultra or better 3.5×;
  - boxes of 24 at 20% off, the Classic guarantee, refunds capped at half.
- **Kit uses they/them.**

**Changelog**
- v1 (2026-11-01): first spec.
- v1.1: the owner's decisions above.
- v1.2: chunk A as built (see "As built" under chunk A).

## What changed since the review

- **The proof pass ([PROOF-2026-11-01.md](PROOF-2026-11-01.md)):** the economy now holds and DP is spent (minimal 19.3%,
  collector 68.3%, gambler 38.7% after 10 cups). So the review's item 6 ("make DP a real limit") is done enough. The
  shelves, tags and boxes now meet a wallet that runs down.
- **Selection packs dominate spending at every band at a flat 200 DP.** Two fixes meet here: narrowing them (this spec,
  section 3) and pricing them by band (the Economy Designer's `ShopPrices.SelectionBandPricing`).
- **Card unlocks are built.** Set completion pays a framed card and a poster (`CardUnlocks.CheckSets`). The set-progress
  tag must use the same count.
- **The level-up DP fix is in.** Nothing here touches it.

## Findings in the data (read before building)

I checked every pick against the data the engine actually loads (`unity/Assets/StreamingAssets/printings.bin` through
`PackCatalog`, the opponents' `.ydk` files through `campaign/circuits.txt`). Three things in the data change the plan.

### F1. The catalog is missing about 70 real core boosters

`PackCatalog` builds 157 booster sets today. Most core boosters from 2002-2004 and 2007-2021 are not among them:
- **Sneak Peek and Premiere names (58 sets):** the printings' set table maps the prefix of a core set to its Sneak Peek
  card ("ORCS" is "Order of Chaos Sneak Peek Participation Card", "ETCO" is "Eternity Code Premiere! promotional card").
  `IsBooster` then drops the whole booster by name. The printings themselves carry the right name (111 ORCS printings
  say "Order of Chaos").
- **Substring matches:** "Tin" matches "Des**tin**y", so Rise of Destiny, Destiny Soldiers, Legendary Duelists:
  Immortal Destiny and Burst of Destiny are dropped.
- **Inflated card counts (2002-2004):** the set table counts every edition (LOB lists 355 cards for 126), so the 80% rule
  drops LOB, MRD, MRL, SRL, PSV, LON, LOD, DCR and IOC.

The lost sets are the archetype homes: Storm of Ragnarok (Six Samurai), Generation Force (Geargiano, Wind-Ups), Order of
Chaos (Inzektors), The New Challengers (Qliphorts), Duelist Alliance (Shaddolls, Satellarknights), Soul Fusion (Orcust,
Thunder Dragons), Chaos Impact (Unchained), Legacy of the Valiant (Dragon Rulers), and more.

**Recommendation (A0 below, the owner's call):** fix the catalog before the shelves ship. Section 1 gives the shelves
for both cases: Table A for today's catalog (validated), Table B if A0 lands.

### F2. Thirteen rarity sets can never give their Super Rares

In a set with no Commons whose lowest rarity is Rare (Toon Chaos, Genesis Impact, Ancient Guardians, King's Court,
The Grand Creators, Tactical Masters, Amazing Defenders, Maze of Memories, Wild Survivors, Valiant Smashers, Crossover
Breakers, Phantom Revenge, Maze of Muertos), the 7 base slots give Rares and the 2 foil slots skip Super Rare (its foil
weight is 0, "the base of the slot"). So 10-15 Super Rare-only cards per set are never pulled, and **set completion is
impossible from packs.** Measured: Wild Survivors 47/60 cards reachable in 4,000 packs; the Super Rare line never shows.

### F3. Four sets with one stray Common open 7 copies of it

Maze of Millennia, Justice Hunters, Quarter Century Stampede and Rarity Collection 5 each have exactly 1 card printed at
Common. They're treated as "commons sets": every pack is 7 copies of that one card, a rare slot and a foil slot.

**F2 and F3 are pack-engine bugs, not design.** The fix (A1 below) is narrow and keeps every printed foil odd the same.
It is also the owner's call, since "keep the pack engine" was the brief. Without it, the set-progress tag would advertise
sets nobody can finish.

## 1. The featured shelf per circuit

**Purpose:** packs become a choice tied to the opponents in front of you. **Player experience:** "Gus put the sets my
next opponents play from at the front."

### Data: `campaign/featured.txt` (new)

```
# The card shop's featured shelf (docs/dev/SHOP-REWORK-SPEC-2026-11-01.md): 3-4 real sets per circuit, chosen because
# they hold that circuit's opponents' cards. Everything else on sale goes to the back shelves.
#   shelf: <circuit name, as in circuits.txt> | postgame
#   set:  <set code> | <opponent decks, comma-separated> | <why: the cards, one short line>
#   soon: <set code> | <opponent decks> | <why>      a set the shop opens with levels before this band ends (0-2)
# Rules (the harness checks them):
#   - a set: is in the shop's catalog and released by the circuit's era, so it's on sale while the circuit is current;
#   - a soon: is released after the era and by the next band's era (it opens with levels);
#   - every deck named is a match deck of that circuit and has at least one Main/Extra card in the set;
#   - every match deck of the circuit is named by at least one set: or soon: line;
#   - 3 or 4 set: lines per shelf, at most 2 soon: lines. The postgame shelf names no decks.
```

### Which shelf shows

- **The current circuit's shelf:** `Circuits.Current(profile)`, the first circuit not cleared.
- **After the Tier 0 Gauntlet is cleared (every circuit cleared):** the `postgame` shelf.
- **New Game+:** the circuits reset, so the shelf goes back to the Beginner Circuit's. That's intended.
- **Tag Cups** don't change the shelf. They block nothing and reuse circuit decks.
- **A `set:` is always on sale** while its circuit is current: `ShopYear >= BandEra = the circuit's era >= the set's
  year`. The check proves it per line.
- **A `soon:` row** shows locked, greyed, with "Arrives at level N" (N = `BandStartLevel[era] + year - era`, the same
  sum `ShowList` uses for "to 2015 at level X"). It can be selected to preview its cards and odds; the buy buttons are
  off. Once `ShopYear` reaches its year it becomes a normal featured row. This gives the level-gated shop a face: "Dark
  Saviors (Sky Striker) arrives at level 14."

### Table A: the shelves for today's catalog (validated, 0 problems)

Every pick below passed the rules above against the data, with a scratch probe that loads `PackCatalog` and the decks
the way the harness does. "Serves" names the deck, then the cards that matter (signatures from
docs/dev/CARD-UNLOCKS-SPEC-2026-11-01.md in bold). `[R]` marks a rarity set (no Commons).

**Beginner (era 2005)**

| Shelf | Set | Serves |
|---|---|---|
| Beginner Circuit | DB2 Dark Beginning 2 | warriors: **Marauding Captain**, Command Knight, Blade Knight · classic: Stamping Destruction |
| | DB1 Dark Beginning 1 | classic: Blue-Eyes White Dragon · warriors: Gearfried the Iron Knight, Goblin Attack Force |
| | DR1 Dark Revelation Volume 1 | classic: **Luster Dragon** · warriors: D.D. Warrior Lady |
| | FET Flaming Eternity | warriors: Gearfried the Swordmaster, Release Restraint · classic: King Dragun |
| | soon: DR3 (2006), DLG1 (2008) | warriors: Gearfried the Swordmaster · classic: Lord of D., Kaibaman |
| Rookie Cup | CRV Cybernetic Revolution | gadgets: **Cyber Dragon** (Tink) |
| | DB1 | classic: **Blue-Eyes White Dragon** (Gus) · gadgets: Upstart Goblin, Mystical Space Typhoon |
| | DB2 | gadgets: Exiled Force, Book of Moon · classic: Stamping Destruction |
| | DR1 | classic: Luster Dragon · gadgets: Sakuretsu Armor |
| | soon: DPYG Duelist Pack: Yugi (2009) | gadgets: Red, Green and Yellow Gadget |
| Town Tournament | PGD Pharaonic Guardian | zombies: Pyramid Turtle, Book of Life · stall-burn: Spirit Reaper |
| | DB1 | stall-burn: Big Shield Gardna, Giant Soldier of Stone · warriors: Gearfried the Iron Knight |
| | DB2 | zombies: Vampire Lord, Call of the Mummy · warriors: Marauding Captain |
| | DR1 | stall-burn: Des Koala, Wave-Motion Cannon · zombies: Despair from the Dark |
| | soon: GLD3 Gold Series 3 (2010) | zombies: Goblin Zombie, Mezuki, Plaguespreader Zombie |
| Regional Qualifier | DR1 | stall-burn: **Wave-Motion Cannon** (Hollis), Des Koala · zombies: Spirit Reaper |
| | PGD | zombies: Pyramid Turtle, Book of Life, Call of the Mummy · gadgets: Book of Moon |
| | DB1 | stall-burn: Big Shield Gardna, Messenger of Peace · gadgets, zombies: their staples |
| | CRV | gadgets: Cyber Dragon |
| | soon: DPYG (2009) | gadgets: the Gadgets · stall-burn: Marshmallon |

**Novice (era 2011)**

| Shelf | Set | Serves |
|---|---|---|
| Underdog Open | DR04 Dark Revelation Volume 4 | frog-ftk: Poison Draw Frog, Des Frog, **Treeborn Frog** · burning-abyss: Karma Cut |
| | GLD2 Gold Series 2009 | six-samurai: Six Samurai United, Double-Edged Sword Technique · frog-ftk, spellbook, burning-abyss |
| | GLD1 Gold Series | six-samurai: **Grandmaster of the Six Samurai** |
| | DB2 | spellbook: Kycoo the Ghost Destroyer, Jowgen the Spiritualist · six-samurai: Royal Oppression |
| | soon: BP03 (2014), PGL2 `[R]` (2015) | spellbook: Spellbook of Wisdom, The Grand Spellbook Tower · burning-abyss: Night Beam, Ghostrick Alucard |
| Trap Masters Cup | DB1 | geargia: its trap line (Call of the Haunted, Torrential Tribute) · zoodiac, altergeist |
| | DR3 Dark Revelation Volume 3 | altergeist: Mind Crush |
| | DR1 | domain-monarch: Reasoning · zoodiac: My Body as a Shield |
| | DP10 Duelist Pack: Yusei 3 | geargia: Effect Veiler |
| | soon: BP01 (2012), HSRD (2015) | geargia: Fiendish Chain · zoodiac: Speedroid Terrortop |
| Small Engines Invitational | PGD | runick-spright: Lava Golem · purrely: Book of Moon · floowandereeze: Terraforming |
| | DR1 | runick-spright: Lava Golem · floowandereeze: Terraforming |
| | DB2 | yummy: Witch of the Black Forest · purrely: Book of Moon |
| | soon: BP03 (2014) | purrely: Book of Eclipse |

The Small Engines and Trap Masters decks are from 2014-2026 and the shop is at 2011, so no real set holds their
archetypes. Their shelves hold the staples and tech they play, and "Why this set" names them; the house packs
(section 3) carry the decks' own cards, and the soon rows show when the first real archetype set arrives.

**Intermediate (era 2015)**

| Shelf | Set | Serves |
|---|---|---|
| Synchro Dawn Classic | GLD3 Gold Series 3 | blackwing: the Blackwings · infernity: **Infernity Archfiend** · lightsworn: **Judgment Dragon** · teledad: Destiny HERO - Malicious |
| | DP11 Duelist Pack: Crow | blackwing: **Blackwing Armor Master**, Bora, Gale, Shura |
| | DP05 Duelist Pack: Aster Phoenix | teledad: Destiny HERO - Malicious, Destiny Draw, Diamond Dude |
| | PGLD Premium Gold `[R]` | lightsworn: Honest, Necro Gardna, Solar Recharge · teledad: Dark Armed Dragon |
| | soon: BLLR `[R]` (2017), LEHD (2018) | lightsworn: Lumina, Wulf · teledad: Malicious, Destiny Draw |
| Xyz and Link Regionals | BP01 Battle Pack: Epic Dawn | dino-rabbit: Forbidden Lance, Fiendish Chain · inzektor: Number 17: Leviathan Dragon |
| | BP03 Battle Pack 3: Monster League | qliphort: Night Beam, Diamond Dire Wolf · dino-rabbit |
| | SP14 Star Pack 2014 | inzektor: the Djinn · dino-rabbit: Number C39: Utopia Ray |
| | DLG1 Dark Legends | gouki: Destiny HERO - Malicious · dino-rabbit: Gorz |
| | soon: SP18 (2018), PEVO `[R]` (2017) | gouki: Gouki Suprex, Re-Match · qliphort: Qliphort Scout |
| Link Age Open | SP15 Star Pack ARC-V | pendulum-magician: Timegazer Magician |
| | PGLD `[R]` | sky-striker: Reinforcement of the Army · maliss: Gold Sarcophagus |
| | THSF The Secret Forces `[R]` | pendulum-magician, salamangreat: Abyss Dweller |
| | soon: DASA `[R]` (2018), PEVO `[R]` (2017) | sky-striker: Raye, Engage!, Widow Anchor · pendulum-magician: Chronograph Sorcerer |

**Advanced (era 2019)**

| Shelf | Set | Serves |
|---|---|---|
| Golden Age Championship | BP01 | goat-control: **Airknight Parshath**, Scapegoat · chaos-yata: Creature Swap · plant-synchro: Caius |
| | DB1 | goat-control: Thousand-Eyes Restrict, Magician of Faith · chaos-yata: Jinzo, Cyber Jar |
| | MYFI Mystic Fighters `[R]` | dragon-ruler: Tempest, Lightning · plant-synchro: **Lonefire Blossom** |
| | PGL2 Premium Gold: Return of the Bling `[R]` | plant-synchro: Debris Dragon, Formula Synchron, Glow-Up Bulb |
| | soon: MAMA `[R]` (2022), MAGO `[R]` (2020) | chaos-yata: Yata-Garasu · dragon-ruler: Sacred Sword of Seven Stars |
| Control Kings Cup | PRIO Primal Origin Plus | hat: Artifact Moralltach, Sanctum, Ignition · abc, true-draco |
| | BP03 | satellarknight: Forbidden Chalice, Diamond Dire Wolf, Gandiva |
| | FIGA Fists of the Gadgets `[R]` | true-draco: **Dinomight Knight**, Ignis Heat · abc: Gold Gadget |
| | BLRR Battles of Legend: Relentless Revenge `[R]` | abc: Brilliant Fusion, Union Hangar · true-draco: Dragonic Diagram |
| | soon: DLCS (2020), MAGO `[R]` (2020) | hat: Fire Hand, Ice Hand · abc: Gear Gigant X |
| Hybrid Engines Invitational | HSRD High-Speed Riders | tri-brigade: PSY-Framegear Gamma, PSY-Frame Driver |
| | THSF `[R]` | tri-brigade: Fire Formation - Tenki · orcust: Armageddon Knight |
| | DASA Dark Saviors `[R]` | orcust: Armageddon Knight, Dark Grepher |
| | PGD | exosister: Necrovalley |
| | soon: GRCR `[R]` (2022), DUNE (2023) | exosister: Elis, Stella, Pax · unchained: Sharvara, Lord of Yama |

**Expert (era 2023)**

| Shelf | Set | Serves |
|---|---|---|
| Shadows and Storms | BP01 | wind-up: Snowman Eater, Wind-Up Zenmaister |
| | PGL3 Premium Gold: Infinite Gold `[R]` | kozmo: **Kozmo Dark Destroyer**, Farmgirl, Forerunner, Kozmotown |
| | BLHR Battles of Legend: Hero's Revenge `[R]` | shaddoll: Shaddoll Beast, Hedgehog, Falco |
| | MGED Maximum Gold: El Dorado `[R]` | danger-dark-world: Danger! Bigfoot!, Danger! Nessie! |
| | soon: RA04 (2025), BLGG (2026) | shaddoll: Shaddoll Dragon, **El Shaddoll Construct** · wind-up: Wind-Up Shark |
| Rituals and Fire | DABL Darkwing Blast | yubel: its Kashtira engine (Unicorn, Fenrir, Birth) |
| | GEIM Genesis Impact `[R]` | drytron: **Drytron Alpha Thuban**, Gamma Eltanin, Zeta Aldhibah |
| | SESL Secret Slayers `[R]` | adamancipator: Analyzer, Seeker, Researcher |
| | MAGO Maximum Gold `[R]` | drytron: Herald of the Arc Light · adamancipator · tenpai-dragon |
| | soon: LEDE (2024), INFO (2024) | tenpai-dragon: Paidra, Chundra, Sangen Kaimen · yubel: Fiendsmith Engraver |
| The New Wave | AGOV Age of Overlord | fire-king-snake-eye: Snake-Eye Ash, Oak, Flamberge Dragon |
| | LD10 Legendary Duelists: Soulburning Volcano | fire-king-snake-eye: Salamangreat Raging Phoenix |
| | DIFO Dimension Force | elfnote: Psychic End Punisher |
| | BLRR `[R]` | angelechy: Foolish Burial Goods · ryzeal: Tornado Dragon · elfnote |
| | soon: CRBR `[R]` (2024), CORI (2026) | ryzeal: Ext Ryzeal, Ice Ryzeal · angelechy: Angelechy Problem |
| Fall Format | LED4 Legendary Duelists: Sisters of the Rose | lunalight: Kaleido Chick, Luna Light Perfume, Leo Dancer · artmage |
| | AGOV | mermail-atlantean: Xyz Armor Torpedo, Full Armored Dark Knight Lancer |
| | LDS2 Legendary Duelists: Season 2 | lunalight: Emerald Bird, Yellow Marten, Lunalight Fusion |
| | BLLR Battles of Legend: Light's Revenge `[R]` | mermail-atlantean: Mermail Abyssteus · dracotail: Odd-Eyes Dissolver |
| | soon: JUSH (2025), DUAD (2025) | dracotail: Faimena, Urgula · artmage: Medius the Pure, Artmage Power Patron |

**Master and the Gauntlet (era 2026; every set is on sale, so no soon lines)**

| Shelf | Set | Serves |
|---|---|---|
| Link Wars | POTE Power of the Elements | spright: Spright Blue, Jet, Starter, Elf |
| | DABL | bystial-dragon-link: The Bystial Lubellion, Bystial Saronir |
| | MZTM Maze of the Master | thunder-dragon: Thunder Dragonroar, Thunder Dragonhawk |
| | MGED `[R]` | eldlich: Eldlich the Golden Lord, Cursed Eldland · bystial-dragon-link: Rokket Tracer |
| Graveyard Kings | POTE | tearlaments: Havnis, Merrli, Reinoheart, Scheiren |
| | DABL | kashtira: Fenrir, Birth, Shangri-Ira · tearlaments: **Tearlaments Rulkallos** |
| | AGOV | snake-eye: Snake-Eye Ash, **Snake-Eyes Flamberge Dragon** · k9-vanquish-soul: Jiaolong |
| | WISU Wild Survivors `[R]` | k9-vanquish-soul: Vanquish Soul Razen, Dr. Mad Love, Stake your Soul! |
| Houston 2026 | L26D Legendary Modern Decks 2026 | mitsurugi: Mitsurugi Prayers, **Ame no Murakumo no Mitsurugi** · branded-despia |
| | ROTA Rage of the Abyss | light-and-darkness: The Hallowed Azamina, Deception of the Sinful Spoils |
| | RA04 Quarter Century Stampede | branded-despia: Fallen of Albaz, Blazing Cartesia · light-and-darkness: Dark Magician |
| | MAMO Magnificent Monsters | light-and-darkness: Dark Magician, the Pharaoh's Servant · mitsurugi |
| October 2026 | CORI Chaos Origins | blitzclique, sacred-beast, chaos-ritual, dark-magician-chaos-ritual: their cores |
| | INFO The Infinite Forbidden | sacred-beast: the Fiendsmith engine · blitzclique: Dominus Purge |
| | MAMO | dark-magician-chaos-ritual: Multiplying Kuriboh!, Master of Chaos |
| | RA04 | chaos-ritual: Fallen of Albaz, Incredible Ecclesia · sacred-beast |
| Tier 0 Gauntlet | POTE | tearlaments and ishizu-tearlaments: Havnis, Merrli, Scheiren, Perlereino |
| | JUSH Justice Hunters | k9-vanquish-soul: K9-17 Izuna, K9-17 "Ripper" |
| | MGED `[R]` | eldlich: the Golden Land core |
| | MAMA Magnificent Mavens `[R]` | ishizu-tearlaments: Kelbek, Keldo, Mudora · eldlich: Gozen Match |
| postgame | RA05, BLGG, CORI, BETB | the chase: every rarity (RA05), 146 cards with Starlights (BLGG), the newest core sets |

The ready-to-paste file is the same content in the `featured.txt` format (one `set:` line per row above, with the "Why"
column as the cards named). Build chunk A writes it from these tables.

### Table B: the shelves that change if the catalog fix (A0) lands

Validated with the same probe against an approximation of the fixed catalog (the hidden boosters pooled by prefix).
**Re-run the harness check after A0; it is the authority.** Shelves not listed keep their Table A picks.

| Shelf | Sets (soon lines after the bar) |
|---|---|
| Underdog Open | STOR Storm of Ragnarok (six-samurai: Kagemusha, Legendary Six Samurai - Kizan, Kageki), DR04, GLD2, DB2 · soon BP03, PGL2 |
| Trap Masters Cup | GENF Generation Force (geargia: **Geargiano**), ABPF Absolute Powerforce (geargia: Fiendish Chain), LODT Light of Destruction (domain-monarch: Kuraz), DR3 · soon HSRD, PGLD |
| Small Engines Invitational | TDGS The Duelist Genesis (purrely: Book of Eclipse), PGD, DR1, DB2 |
| Synchro Dawn Classic | GLD3, LODT (lightsworn: Lumina, Wulf, Honest), TSHD The Shining Darkness (infernity: Beetle, Launcher, Barrier), DP05 · soon LEHD |
| Xyz and Link Regionals | PHSW Photon Shockwave (dino-rabbit: Rescue Rabbit, Evolzars), ORCS Order of Chaos (inzektor), NECH The New Challengers (qliphort), DLG1 · soon SP18 |
| Link Age Open | CORE Clash of Rebellions (pendulum-magician: Magical Abductor), PGLD, SP15, THSF · soon DASA, PEVO |
| Golden Age Championship | BP01, DB1, LTGY Lord of the Tachyon Galaxy (dragon-ruler: the Dragon Rulers), PGL2 · soon MAMA |
| Control Kings Cup | PRIO, DUEA Duelist Alliance (satellarknight), MACR Maximum Crisis (true-draco: Dinomight Knight, True Draco Heritage), BLRR · soon DLCS |
| Hybrid Engines Invitational | SOFU Soul Fusion (orcust), CHIM Chaos Impact (unchained), HSRD, PGD · soon GRCR, MGED |
| Shadows and Storms | DUEA (shaddoll), GENF (wind-up: Wind-Up Factory, Magician), PGL3, CYHO Cybernetic Horizon (danger-dark-world) · soon BLGG |
| Fall Format | LED4, SHVI Shining Victories (lunalight), AGOV, BLLR · soon JUSH, DUAD |
| Link Wars | POTE, DABL, SOFU (thunder-dragon: Dragonroar, Dragondark, Dragonhawk), MGED |

**How to re-pick (for whoever edits the file later):** for each match deck, list the on-sale sets (year ≤ era) by how
many of the deck's copies they hold, counting only cards in 3 or fewer campaign decks and not in `Loaners.Staples`.
Take the best set per deck, merge sets that serve two decks, prefer sets with Commons, and stop at 4. Fill `soon:` with
the best set after the era for the worst-served decks. The harness check catches every mistake that matters.

### The Back shelves (the back catalog) and the list

- **Back shelves** (the Narrative doc's name; the code calls it the back catalog): every real set on sale that isn't on
  the featured shelf or in New arrivals. Nothing is deleted. Collapsed by default: one row, "Back shelves · 64 sets ▸",
  with the `back-shelves` line in the detail panel when it's selected. Expanded: year sub-headers, newest year first;
  within a year, sets with Commons first, then by name.
- **The list is one flat sequence of rows, paged 11 to a page as today.** Section headers count as rows:
  1. FOR THE <CIRCUIT> (the featured shelf; "For the champion" in the post-game): live rows, then soon rows;
  2. NEW ARRIVALS (only when any): up to 6 rows, then "+N more on the back shelves";
  3. GUS'S HOUSE PACKS · the decks you'll face (section 3);
  4. OCG IMPORTS (when the shelf is open; unchanged rule);
  5. BACK SHELVES (collapsed or expanded).
- **Each set appears once:** featured, then New arrivals, then the back shelves. A featured set that is also new gets a
  NEW chip.
- **Search** matches name, code or year across every section, opens the back shelves, and hides empty sections.
- **Wishlist order** stays as today: one flat list ranked by wishlist cards per pack, across everything on sale.
- **The default pick on opening** (the headline fix): the first live featured set that has Commons; else the first live
  featured set; else the first row. Never a rarity set, a house pack or an import by default.

### New arrivals

- **What counts as new:** a real set whose year opened since the player last looked. On each shop visit, every year in
  `(ShopYearSeen, ShopYear]` is recorded as arrived on this visit, and `ShopYearSeen` becomes `ShopYear`.
- **How long:** a set stays new for **3 shop visits** (the visit it arrived on and the next two) `[PLACEHOLDER]`. A
  visit is `ShopScreen.Open()` from the campaign: Prize Pack reveals and debug calls don't count.
- **`ShopYearSeen` only grows.** In New Game+ the shop's year drops back, but nothing is new again, because the player
  has seen those sets. Old saves and new games start with `ShopYearSeen` set on the first visit without marking any set
  new (no flood); that first visit still plays the `arrival:<year>` line, the "opening stock" line in the Narrative doc.
- **Within the section:** sets with Commons first, then newest first, at most 6 rows.

## 2. Pull and list tags

**Purpose:** a pull or a set is worth something because of *your* decks and *your* next opponents. **Player
experience:** "That's the card Tink plays. And it's in my binder now."

### The four tags and exactly which decks count

| Tag | Counts | Text |
|---|---|---|
| **Wanted** | `Profile.Wanted()`: wishlist cards you hold fewer copies of than wanted (set by the deck editor's Recipe) | "Wanted for <WishlistFor's title>" |
| **Plays this** | Main and Extra Deck cards (base codes) of the house-pack decks: every match not yet won in the current circuit and the next one (section 3), minus `Loaners.Staples` (so Ash Blossom doesn't light up every row) and the era-generic staples (Monster Reborn, Pot of Greed, Mirror Force...; `TagContext.Staples` holds both). Side Decks don't count; CPUs don't side | "<character name> plays this"; with more than one, "Tink and 2 more play this", first by match order |
| **In your deck** | Main, Extra and Side of `Profile.Deck` and every campaign deck in `DeckLibrary.CampaignNames`, minus loaners (`Loaners.IsLoanerDeck`) | "In <deck title>": the active deck first, else the first by name |
| **Set progress** | `CardUnlocks.Progress(profile, setCode)`: distinct cards of the set you own (`Collection.Owns`, desk copies included) over the set's distinct cards | "41/60", or "60/60 ✓" when done |

- **One definition for set progress.** The tag must use the same pool and test as `CheckSets` (the set's `Pool` codes,
  `Collection.Owns`). Chunk A exposes `CardUnlocks.Progress` and `CardUnlocks.FramedCard` (the card `CheckSets` would
  frame), and the shop calls only those. So "60/60" and the framed card arrive in the same pack.
- **Real sets only** get set progress. House packs and imports aren't in `CheckSets`.
- **In the post-game** there are no house decks, so there's no "plays this" tag. (A Library-cup version is later.)

### Where they show

- **List rows, second line:** "DB1 · 2004 · 41/250 · 3 wanted · Tink plays 2". Each part only when it's above 0.
  Wishlist order keeps its "≈ 1.4 wishlist cards per pack".
- **Detail panel, a "Why this set" block,** up to 3 lines, each naming up to 5 cards:
  - "Wanted for Tearlaments (3): Havnis, Merrli, Scheiren";
  - "Tink plays: Cyber Dragon";
  - "In your Warriors deck (2): Marauding Captain, Command Knight".
  - For a featured set, the shelf's voice line sits above them (section 6).
  - Under it, the set line: "You have 41 of its 60 cards. Finish it and Gus frames <FramedCard>."
- **Pack reveal, under each card's name:** one tag line, by priority Wanted, then Plays this, then In your deck. Only
  for a copy that was kept (new, 2nd/3rd copy or upgrade); never for a sold 4th copy. The hover panel lists every tag.
- **Pack summary:** adds "Set 42/60 (+1)". A completed set keeps today's gold line ("Set complete: ... Gus framed ...").
- **Colors (existing roles, no new ones):** Wanted `GoldSoft` (the wishlist role), Plays this `Info` (the scouting
  role), In your deck `InfoSoft` ("own N"), set progress `TextFaint`, done `Good`. The NEW / +DP / UPGRADE badges keep
  their fixed colors (UI-THEME rule 8).

## 3. One job per pack type

| Type | Its one job | Where | On sale |
|---|---|---|---|
| **Real sets** | The cheapest cards per DP, foils, set completion and the poster, the chase | Featured, New arrivals, back shelves | Released by `ShopYear` (unchanged) |
| **House packs** (were Selection Packs) | The decks you're about to face, before you've beaten them | GUS'S HOUSE PACKS | One per deck of a match **not yet won** in the current circuit and the next circuit |
| **Recipe packs** | Finishing a deck you've beaten and chosen | Recipe Book (unchanged) | Unlocked recipes (unchanged) |

### House packs: the exact rules

- **Decks:** `Circuits.HouseMatches(profile)` walks the matches of `Current(profile)` and `Current + 1` (main circuits
  only, in order) and keeps the first match per deck that isn't won (`HasWon`). One pack per deck.
- **Once beaten, the pack leaves.** Its recipe unlocks on that win (`RecipeBook.IsUnlocked`), so the deck moves to recipe
  packs. A player never sees both for one deck.
- **Post-game (every circuit cleared):** no house packs; the section is hidden.
- **Tag Cups** add no house packs. Tag-only decks get recipe packs once beaten, as today.
- **Was "every opened circuit plus the whole next band".** That's what let selection packs undercut band-priced recipe
  packs at every band.
- **Name:** the `house-pack:name` line, "House Pack: <deck title>". No year: `Info.Date = ""`. The code stays
  `SP-<deck>` and the ledger sink stays `packs.selection`, so saves and the Economy Designer's ledger don't change
  meaning. Player-facing "selection pack" wording goes everywhere (ShopScreen's row, detail and QA log lines).
- **List row:** "House Pack · 41 cards · you have 12" where the date was.
- **Detail line:** the `house-pack:about` line ({opponents} = every character who plays the deck in circuits.txt), or
  `house-pack:about-gus` when Old Gus is one of them, then "· 41 cards · 9 cards per pack".
- **Price:** `SelectionPacks.PriceFor(deck, circuits)` (the Economy Designer's band knob). `CampaignState` must pass it
  to `SelectionPacks.Build`; today it uses the flat default.
- **Contents and odds:** unchanged (each card at its commonest printing, plus its rarest up to Ultra). In era-limited
  play cards not out by `ShopYear` are left out, as today.
- **Keep the method name** `Circuits.SelectionPackCircuits`; change its body to return `[Current, Current + 1]` (empty
  when all are cleared). `CampaignSim.cs` (the Economy Designer's file) calls it and narrows with it, with no edit.

### Recipe packs

- Unchanged. The shop's header gets a **Recipe Book** button (a `RecipeClicked` event wired like `TraderClicked`), so a
  player who has beaten a deck finds its packs from the shop.

### Stale comments to fix on the way

- `ShopScreen.Available()` says selection packs come "from the Expert band on". They never did.
- `SelectionPacks.Bands` is really the late bands (they pay more; the OCG shelf opens with them). Move it to
  `Circuits.LateBands`; `IsLateBand` is its one user.

## 4. Booster boxes and rarity sets

### Booster boxes

- **What:** 24 packs of one set, bought at once, at a discount, with **at least one card of the set's top rarity** in
  the box. A sink for DP and the pack lover's chase.
- **Which sets:** live featured rows only (the current shelf, or the post-game shelf). Not soon rows, house packs,
  imports or the back shelves. A box follows the opponents, like the shelf.
- **The top rarity:** the rarest rarity in the set's pool (`Pool.Keys.Max()`): Starlight in a modern core set, Ultimate
  in Elemental Energy, Ultra in Dark Beginning 1, Gold Secret in Premium Gold.
- **How the guarantee works (no new odds):** open 24 packs with `PackCatalog.Open`. If none of the 216 pulls is at the
  top rarity, pick one pack (from the box's rng) and replace its best card (the last foil slot) with a random card of
  the top rarity. An upgrade, not an extra card.
- **Cards and DP:** every pull goes in with `AddPulled` and a share of the box price (`Pricing.Shares` over all 216, as a
  pack does over 9), so the Sell desk's `min(V/4, Paid/2)` still holds. 4th copies pay DP, capped at **half the box
  price** for the whole box. `PacksOpened += 24`. `CheckSets` runs once after.
- **Ledger:** a new sink `packs.box` (`Ledger.Boxes`).
- **On screen:** a third button under the set, "Buy a box · 24 packs · <price> DP", with "At least one <top rarity> in
  every box" under it. The box reveal (chunk B) shows the box's 10 best pulls face down, the top-rarity card last.
- **Knobs for the Economy Designer** (in `ShopPrices`):
  - `BoxPacks = 24` (design: fixed);
  - `BoxDiscount` `[PLACEHOLDER: 0.20-0.25]`; the price is `round5(BoxPacks × set.Price × (1 − BoxDiscount))`;
  - `BoxGuarantee`: `Top` (the set's rarest rarity, the default) or `Classic` (its rarest up to Secret Rare), in case a
    Starlight per box proves too generous;
  - `BoxRefundShare = 0.5` (the 4th-copy refund cap, as for packs).
  - **Watch:** the post-game shelf's RA05 and BLGG boxes guarantee a Starlight (Raw value 2,500). The `--arbitrage`
    box loop must stay under 100%.

### Rarity sets: the badge and the headline fix

- **One predicate everywhere:** `PackCatalog.IsRaritySet(set)` is true when the set has fewer than
  `PackCatalog.CommonFloor = 5` `[PLACEHOLDER]` distinct cards at Common or Short Print. It replaces
  `ShopPrices.NoCommons` (so Rarity Collection 5 with its one stray Common counts as a rarity set for its price too).
- **Base rarity:** the lowest rarity with at least 5 cards. Cards at a stray rarity below it are drawn with the base.
- **Badge:** list rows get a "RARITY SET" chip (`GoldSoft`). The detail panel says "A rarity set: no commons. Each pack
  is 7 <base>s and 2 foil slots."
- **The reveal builds to the last card again:** in a rarity set, only pulls above its base rarity glow, burst and play
  the rare sounds. In a set with Commons the rule stays "Super Rare or rarer". (A Hidden Arsenal pack of 9 Super Rares
  now glows only on its Secret Rares.)
- **The headline:** never the default pick (section 1), and listed after sets with Commons in every section.
- **Price:** the Economy Designer's `NoCommonsFactor` (they proposed 2×), now on this predicate.

### A1. The two engine fixes (F2, F3)

- **The foil slot's fallback in a rarity set:** when a rarity set's foil roll lands on "nothing" (the weight-36 outcome),
  it gives a card of the **lowest rarity above the base whose foil weight is 0** (Super Rare, when the base is Rare)
  instead of a base card. Every foil weight stays as it is, so the shown odds of Ultra and up don't move.
- **Stray commons:** with `IsRaritySet`, the four one-Common sets open as rarity sets (7 base, 2 foil slots).
- **Expected effect:** Wild Survivors goes from 47/60 reachable (measured in 4,000 packs) to 60/60; chunk C's check 8
  proves it. No set with Commons changes.

## 5. Odds shown (E5)

The target: odds are shown before you pay, for packs, boxes and Prize Packs. Grading already shows its odds.

### Packs (real sets, house packs, imports)

- **Engine:** `PackOdds.For(PackSet set)` returns, per rarity in the pool, the cards at it, the expected copies per pack
  and the chance of at least one per pack. **Computed exactly from the code's rules, not sampled:**
  - **Sets with Commons:** the common tier (Common ×3 per card, Short Print ×1), the rare tier (Rare, Super Rare,
    Rare Parallel, Normal Parallel ×3 per card), the foil slot `P(r) = w_r / (36 + Σw)` over the set's rarities with a
    foil weight above 0, and "nothing" falls back to the rare tier. For a rare-tier rarity with share `q`:
    `P(at least one) = 1 − (1 − q)(1 − P(nothing) · q)`.
  - **Rarity sets:** 7 base slots and 2 foil slots, `P(at least one) = 1 − (1 − p)²`, with the A1 fallback.
- **Shown in the detail panel,** under a caption "ODDS PER PACK", one row per rarity above Common (above the base in a
  rarity set), rarest last: "Ultra Rare · 15 cards · 1 in 3.0 packs". When the expected count is 1 or more: "Rare ·
  30 cards · about 1.3 a pack". "1 in N" has one decimal under 10, whole numbers from 10.
- **A box line** under it (featured sets), e.g. "A box of 24: about 8 Ultra Rares and 3 Secret Rares, and always at
  least one <top rarity>".
- **Condition when pulled,** one line: "Condition: Mint 45% · Near Mint 45% · Excellent 9% · Played 1% (Super Rare and
  up: 40 / 48 / 10 / 2)". From `Grades.PackOdds` and `FoilOdds` through a new public `Grades.OddsFor(CopySource)`,
  folded the way Raw copies show (9 and 10 both read Mint).
- The "Printings by rarity" line goes; the odds rows carry the card counts.

### Prize Packs

- **Engine:** `PrizePacks.Odds(Bracket b, string pool)`: for slots 1-3, the rarity mix over the field's candidate cards
  (each card's rarity weights by `10000 / Pricing.Unit`, averaged over the candidates: exact for slot 1, "about" for 2-3
  since they draw without replacement); slot 4's tournament set and its rarity mix; slot 5's rarity mix and the grade
  split (`SlabTenPercent`).
- **Bracket entry panel** (BracketScreen, the prize explainer), e.g. "3 cards from the field's decks: Rare 58% · Super Rare
  24% · Ultra Rare 13% · Secret Rare 5% (about) · 1 card from <Tournament Pack 8> · 1 sealed slab: Grade 9 75%, Grade 10
  25%. Prize cards sell for nothing." Shown before the entry is paid.
- **Prize Pack reveal subtitle:** "Prize Pack 1 of 2 · slab: Grade 9 75%, Grade 10 25%".

## 6. Voice hooks

**Purpose:** the shop is Gus's place, and a big pull gets a reaction. **Rule from the review:** no lines on ordinary
packs, no "as you know" patter, and lines stay rare enough to be a moment.

The Narrative Designer's doc landed while this spec was written. **This section adopts its format, file name, hook
names and pull rules as written**, and adds what the code needs: where each hook shows, the exact trigger, and a
deterministic way to pick (QA runs replay the same seeds, so no unseeded randomness).

### Data: `campaign/shop.txt` (new; the lines are in docs/dev/SHOP-VOICE-2026-11-01.md)

The format is the one proposed there:

```
# The shop's lines: pools of lines, one picked each time its hook fires (docs/dev/SHOP-VOICE-2026-11-01.md).
#   pool: <hook>           starts a pool (the hooks are listed below)
#   after: <scene id>      the pool plays only once that story scene has been seen (Profile.Seen); repeatable, all must hold
#   before: <scene id>     ...and stops once that scene has been seen
#   band: <band>[+]        only while your newest open circuit is in that band ("+": that band or any later one)
#   <speaker>: <text>      a line, as in story.txt: a character id, or "-" for a shop label; [era] / [open] work too
# Text can use {card}, {rarity}, {opponent}, {opponents}, {deck}, {year}, {packs} and {circuit}.
# A hook with several eligible pools draws from all of them. A more specific hook (pull:opponent:old-gus) wins over
# the general one (pull:opponent).
```

- "Your newest open circuit" is `Circuits.Current(profile)`; after the Gauntlet it is the post-game, band `Post-game`.
- A hook with no eligible line shows nothing. The footer then shows its old help text.

### Where each hook shows, and when

| Hook | Where | Trigger |
|---|---|---|
| `header` | The header plate (`Sign` role) | Always: "GUS'S CARDS" |
| `footer` | The status bar on opening | Every visit with no arrival |
| `arrival:<year>`, `arrival` | The status bar (it takes the footer) | A visit whose `Arrivals.Visit` recorded years, or the first visit ever: the newest year speaks, through its own pool if it has one, else `arrival` with {year} |
| `kit-hello` | Wherever Kit would first speak | The first time any Kit line is picked: this plays instead, once per save |
| `shelf:<circuit>` | Under the FOR THE <CIRCUIT> header, and above "Why this set" for that shelf's sets | While that circuit is current |
| `back-shelves` | The detail panel when the collapsed back-shelves row is selected | Selecting it |
| `house-pack:name`, `house-pack:about`, `house-pack:about-gus` | The house pack's name and description line (labels, speaker "-") | Always (section 3) |
| `odds` | Heading the ODDS PER PACK block | Selecting any pack |
| `pull:ultra` ... `pull:starlight` | Under the pack summary, once every card is face up | The pack's best pull's tier (the Narrative doc's tier table) |
| `pull:opponent`, `pull:opponent:<id>` | Under the pack summary | A kept pull that is a **key card** of a house-pack opponent: Scouting's `keys:` for the deck (written or built from its playbook), or the character's signature. Not shared staples |
| `pull:recipe` | Under the pack summary | A kept pull that was Wanted (a new copy toward the active recipe) |
| `pull:deck` | Under the pack summary | A new copy or upgrade of a card in one of your decks (the In your deck tag) |
| `box` | Under the box summary | A box is opened |
| `box-best` | Under the box summary, replacing `box` | The top-rarity card (the box's promise) is flipped |
| `prize-pull` | The Prize Pack reveal, under the summary | The slab, or a Secret Rare or rarer, is revealed; at most once per finished bracket |

### The pull rules (from the Narrative doc, made exact)

- **At most one line per pack.** None for a pack whose best card is below Ultra, unless it holds an opponent, recipe or
  deck card.
- **Priority** when a pack qualifies more than once: Starlight > Ghost > Quarter Century > opponent > recipe > deck >
  Collector's > Ultimate > Secret > Ultra.
- **Spacing, deterministic:**
  - Ghost, Quarter Century and Starlight always fire.
  - Secret, Ultimate and Collector's fire unless the previous pack of this visit had a line.
  - Ultra fires on every third Ultra-qualifying pack: a saved counter (`voice pull:ultra N`), firing when `N % 3 == 0`.
  - Opponent lines fire once per opponent per circuit (`seen shop:opp:<id>:<circuit index>`).
  - Recipe and deck lines follow the Secret rule (not right after a pack that had a line).
- **In a rarity set**, a rarity line needs the pull to be above the set's base rarity too (a Gold Rare in Premium Gold
  is the base, not a moment).
- **Never:** opponent lines on house packs (every card in one is the opponent's); any pull line on recipe packs (they
  open in the Recipe Book, not here); pull lines on Prize Packs (they get `prize-pull`); pull lines inside a box (it has
  `box` and `box-best`).
- **Kit's lines** need `after: tag-night`, as the pools say; before it the Gus pools cover.

### Picking a line from a pool (deterministic, no repeats)

- **The eligible lines** of a hook: every line of every pool for that hook whose `after:`, `before:`, `band:` and
  `[era]`/`[open]` all hold. A more specific hook's lines (`pull:opponent:vera`) replace the general ones when eligible.
- **Unheard lines first:** a heard line is remembered as `seen shopline:<hook>#<n>` (n = its place among that hook's
  lines in file order). So "a newly unlocked pool goes first" falls out: its lines are the unheard ones.
- **Order:** the eligible lines in a fixed per-save shuffle (seeded by `Collection.Salt` and the hook), first unheard
  one first. Once all are heard, step through the same order with the hook's counter (`voice <hook> N`). No repeat
  until the eligible lines have all played.
- **Placeholders** ({card}, {rarity}, {opponent}, {opponents}, {deck}, {year}, {packs}, {circuit}) fill from the
  trigger. `{rarity}` is the pulled card's own rarity name. `{packs}` is `ShopPrices.BoxPacks`.

### How a line shows

- One line: the speaker's short name in `GoldSoft`, the text in `Quote` (Gus "Truck came in. ..."). Speaker "-" lines
  are labels and show as plain text in the place they belong (the plate, the house pack name).
- **Messages win:** a status message (Not enough DP, a purchase, a completed set, the wishlist notes) replaces the
  footer line until the next visit. The standing help text moves into the empty detail panel.
- **Lengths** (the harness checks): footer and pull lines at most 110 characters, detail-panel lines at most 140.

## 7. Save, New Game+, and what's out of scope

### Save

New `Profile` fields, each one line in the save, all left alone by `NewGamePlus()`:

| Field | Save line | Default for old saves |
|---|---|---|
| `int ShopVisits` | `shopvisits N` | 0 |
| `int ShopYearSeen` | `shopseen <year>` | 0, set silently to `ShopYear` on the first visit |
| `Dictionary<int,int> Arrivals` (year → the visit it arrived on) | `arrival <year> <visit>`, dropped once expired | none |
| `Dictionary<string,int> VoiceCounts` (hook → lines played once all were heard; also `pull:ultra`'s spacing count) | `voice <hook> N` | none |

- **Seen keys** (the existing `seen` lines): `shopline:<hook>#<n>` (a heard line), `shop:kit-hello`,
  `shop:opp:<id>:<circuit index>`, `shop:prize:<bracket seed>`.
- **No new copy data.** A box is 24 pack openings in the collection and the ledger.
- **Ledger:** `packs.box` joins the sinks (`LedgerCheck` lists it).

### New Game+

- The featured shelf and house packs follow the reset circuits (back to the Beginner Circuit's).
- Nothing is a New arrival again (`ShopYearSeen` only grows).
- Heard lines stay heard (`Profile.Seen` survives New Game+, as the Narrative doc intends), so lines cycle on, and the
  New Game+ footer pools (`after: new-game-plus`) come first because they're unheard.
- Boxes, odds, tags and rarity-set rules are the same. Nothing here pays anything, so "New Game+ re-pays none of the
  post-game" still holds.

### Out of scope (later)

- **Faces on the counters:** Kit at the Card Trader. They need portraits and lines. (The Grading Desk already has a
  face, Odile Varga; the review's "Hollis at the Grading Desk" is stale, as the Narrative doc notes.)
- **The old 2005 stock** (Ines's sealed box after the Goat Cup). It needs a portrait moment and lines.
- **Prices:** every DP number here is the Economy Designer's.
- **Opponent decks, pack odds (`FoilWeights`), the pack size, the 4th-copy rule, structure decks, the Card Trader, the
  Grading Desk, the used market, the OCG import contents, Prize Pack contents:** unchanged.
- **"Plays this" for Library cups** in the post-game.

## 8. Build split

Three chunks on disjoint files. **A first**, then B and C in parallel against A's API (the signatures below are
binding, so B and C can start from this doc).

### Coordination before anything starts

- **The Economy Designer has uncommitted edits** in `Packs.cs`, `DeckAccess.cs`, `CampaignSim.cs`, `Arbitrage.cs`,
  `Program.cs` and new `ShopPrices.cs` and `ShopProbe.cs` (as of this writing). Chunk A edits `Packs.cs`,
  `DeckAccess.cs` and `ShopPrices.cs`; chunk C edits `Arbitrage.cs`. **Chunks A and C start after that work is
  committed**, or the owner merges.
- **Tell the Economy Designer:** narrowing house packs changes `CampaignSim`'s selection-pack buying (it calls
  `SelectionPackCircuits`); A0 adds about 70 sets (most priced by year at 200-450); `IsRaritySet` replaces `NoCommons`.
  Their measurements should run on the rework, or name which side they measured.
- **Owner decisions needed:** A0 (the catalog fix) and A1 (the two engine fixes). If A0 is declined, ship Table A. If
  A1 is declined, rarity sets with unreachable cards show "Can't be completed from packs" instead of a progress count.

### Chunk A: engine and data

**Files:**
- `src/Ygo.Engine/Campaign/Packs.cs`: A0, A1, `IsRaritySet`, `CommonFloor`, `BaseRarity(set)`, `TopRarity(set)`.
- `src/Ygo.Engine/Campaign/ShopPrices.cs`: the box knobs and `Box(set)`; `NoCommons` → `PackCatalog.IsRaritySet`.
- `src/Ygo.Engine/Campaign/DeckAccess.cs`: house pack name, no date, the `Bands` move.
- `src/Ygo.Engine/Campaign/Profile.cs`: `SelectionPackCircuits` body, `HouseMatches`, `LateBands`; the three fields and
  their save lines; `Ledger.Boxes`; `Grades.OddsFor`.
- `src/Ygo.Engine/Campaign/CardUnlocks.cs`: `Progress`, `FramedCard`.
- `src/Ygo.Engine/Campaign/PrizePacks.cs`: `Odds`.
- New: `Shelves.cs`, `PackTags.cs`, `PackOdds.cs`, `BoosterBox.cs`, `ShopVoice.cs` (all in `src/Ygo.Engine/Campaign/`).
- New data: `campaign/featured.txt` (Table A, or Table B after A0) and `campaign/shop.txt` (the pools exactly as in
  docs/dev/SHOP-VOICE-2026-11-01.md, header comment included).

**A0, the catalog fix:**
- A set's pool is its printings whose `SetName` is the prefix's most common name (so "Special Edition" and Sneak Peek
  printings don't mix in), and the set is named by it.
- `NotBoosters` words match whole words only ("Tin" no longer matches "Destiny").
- The 80% rule compares against the count of distinct card numbers among those printings, not the set table's count.
- Check after: Order of Chaos, Storm of Ragnarok, Duelist Alliance, Eternity Code, Rise of Destiny and Legend of
  Blue Eyes White Dragon are in the catalog under their real names; no Structure Deck, Tin or Speed Duel set is.

**API (binding for B and C):**

```csharp
// Shelves.cs
public sealed class ShelfPick { public string Code = "", Why = ""; public List<string> Decks = new List<string>(); public bool Soon; }
public sealed class FeaturedShelves
{
    public const string FileName = "featured.txt", PostGame = "postgame";
    public static FeaturedShelves Load(string path);              // a missing file is an empty shelf list
    public static FeaturedShelves Parse(string text);
    public IReadOnlyList<ShelfPick> For(string shelf);            // a circuit name or PostGame
    public List<string> Problems(Circuits circuits, PackCatalog catalog, Func<string, Deck?> loadDeck, ICardDataProvider cards);
}
public enum ShelfSection { Featured, NewArrivals, House, Imports, BackCatalog }
public sealed class ShelfRow
{
    public ShelfSection Section; public PackSet? Set;   // null for a header row
    public string Header = ""; public bool Locked; public int OpensAtLevel; public bool New; public string Why = "";
}
public sealed class ShopShelves
{
    public static ShopShelves Build(Profile profile, Circuits circuits, PackCatalog catalog, FeaturedShelves featured,
        IReadOnlyList<PackSet> housePacks, IReadOnlyList<PackSet> imports);
    public string Shelf = "";                            // the circuit's name or PostGame
    public List<ShelfRow> Featured, NewArrivals, House, Imports, Back;
    public PackSet? DefaultPick;
    public IReadOnlyList<PackSet> OnSale;                // every buyable set, each once
    public List<ShelfRow> Rows(bool backOpen, string filter);   // the flat list with headers, for paging
}
public static class Arrivals
{
    public const int Visits = 3, MaxRows = 6;            // [PLACEHOLDER]
    public static List<int> Visit(Profile profile, int shopYear);   // ShopVisits++, records and expires; the years new this visit
    public static bool IsNew(Profile profile, PackSet set);
}
// PackTags.cs
public sealed class TagContext   // built once per shop visit
{
    public static TagContext Build(Profile profile, Circuits circuits, IEnumerable<Deck> yourDecks, Func<string, Deck?> loadDeck,
        Func<string, string> deckTitle, ICardDataProvider cards);
}
public sealed class PackCardTags { public bool Wanted, Key; public string WantedFor = "", InDeck = ""; public List<string> PlayedBy = new List<string>(); }
// Key: a key card of a house deck (Scouting keys or a signature), for pull:opponent
public static class PackTags
{
    public static PackCardTags For(uint baseCode, TagContext context);
    public static (int wanted, int played, string firstPlayer) Count(PackSet set, TagContext context);
    public static string Line(PackCardTags tags);            // the one reveal line, by priority
}
// PackOdds.cs
public sealed class RarityOdds { public Rarity Rarity; public int Cards; public double PerPack, AtLeastOne; }
public static class PackOdds
{
    public static List<RarityOdds> For(PackSet set);     // exact, by the rules in section 5
    public static bool IsHit(PackSet set, Rarity rarity);// glows on reveal: >= Super Rare, and above the base in a rarity set
    public static string Text(RarityOdds odds);          // "1 in 3.0 packs" / "about 1.3 a pack"
}
// BoosterBox.cs
public sealed class BoxResult { public List<(Pull pull, bool isNew, Added added)> Pulls = new List<(Pull, bool, Added)>(); public Pull Topper; public int Refund; public bool Upgraded; }
public static class BoosterBox
{
    public static BoxResult Open(Profile profile, PackSet set, int price, Random rng);  // spends price to Ledger.Boxes; an empty result if DP is short
}
// ShopVoice.cs: campaign/shop.txt (the Narrative doc's format)
public sealed class VoiceLine { public string Speaker = "", Text = ""; }
public sealed class PackMoment   // what a revealed pack held, for the pull rules
{
    public PackSet Set = null!; public Rarity Best; public uint BestCode;
    public uint OpponentKey; public string OpponentId = "";   // a kept key card of a house deck, if any
    public uint RecipeCard, DeckCard;                         // a kept Wanted card / a new copy or upgrade of a deck card
    public bool HousePack, PreviousHadLine;                   // PreviousHadLine: this visit's previous pack had a line
}
public sealed class ShopVoice
{
    public const string FileName = "shop.txt";
    public static ShopVoice Load(string path);                // a missing file: no lines anywhere
    public List<string> Problems(IEnumerable<Personality> roster, IEnumerable<string> sceneIds, Circuits circuits);
    // unknown hooks, speakers, scene ids in after:/before:, bands, circuits in shelf:, placeholders, lengths
    public VoiceLine? Pick(string hook, Profile profile, Circuits circuits, IReadOnlyDictionary<string, string> fill);
    // eligibility, specific-over-general, unheard first, the seeded order, kit-hello, marks the line heard
    public VoiceLine? Footer(Profile profile, Circuits circuits, IReadOnlyList<int> arrivedThisVisit);  // arrival:<year> / arrival / footer
    public VoiceLine? Pull(PackMoment moment, Profile profile, Circuits circuits);  // the priority and spacing rules
    public static string Fill(string text, IReadOnlyDictionary<string, string> values);
}
```

**Acceptance:** chunk C's checks pass; `--arbitrage` passes; the harness builds; Table A (or B) is in
`campaign/featured.txt` with 0 problems.

#### As built (chunk A, 2026-11-01)

Read this before building B or C: it's where the code differs from or adds to the API above.

**The catalog (A0): 229 sets** (157 before; none dropped, 72 added; 61 rarity sets).
- A card number drops its region before the 80% rule counts it ("LOB-001", "LOB-E001" and "LOB-EN001" are one number;
  a Legendary Decks box's "ENK01" stays "K01").
- `NotBoosters` matches whole words, a plural "s" allowed. Two words changed:
  - "Starter" replaces "Starter Deck" and "Starter Kit", which also keeps out the 2-Player Starter Set and the Super
    Starters;
  - "Kaiba Set" is new: the 25th Anniversary Ultimate Kaiba Set got in once "Mat" stopped matching "Ultimate".
- Names and pools that changed for sets already on sale:
  - PRIO is now "Primal Origin", 100 cards (the "Plus" cards aren't its printings);
  - SOI, TLM and EEN lose their 4 Special Edition cards (60 cards each).
- Order of Chaos, Storm of Ragnarok, Duelist Alliance, Eternity Code, Rise of Destiny and Legend of Blue Eyes White
  Dragon are in under their real names. No Structure Deck, Tin or Speed Duel set is.

**Packs (A1):**
- `PackCatalog.Open` and `PackOdds.For` read one internal layout (`PackCatalog.LayoutOf`), so the odds shown are the odds
  opened. Sets with Commons open exactly as before, with the same rng calls.
- In a rarity set, a rarity above the base that never takes the foil slot is drawn with the base, so nothing is
  unreachable. Exact odds give **0 sets with an unreachable rarity**. Wild Survivors' Super Rares come about 1.3 a pack.
- `IsRaritySet` is for real catalog sets only: house packs and OCG imports open exactly as before the rework (a pack
  with no commons: 7 of its lowest rarity and 2 foil slots), which `--campaign-test` checks pull for pull.
- A box's upgrade (no pull at the promise) takes a pack whose best card is below the promise and replaces that card;
  it never removes a card at or above the promise (Ghost, Starlight... under Classic).
- House packs use the printings out by the shop's year for their rarities (no 2013 Shatterfoil in a 2010 shop).
- New public members: `PackCatalog.FoilNothing` (36) and `FoilWeight(rarity)`. `ShopPrices.NoCommons`, `BaseRarity` and
  `CommonFloor` forward to `PackCatalog`; they're kept for the harness probe.

**Prices:**
- `ShopPrices`' defaults are now the approved prices. `ShopPrices.UseToday()` puts back the old ones, and `--prices base`
  in `ShopProbe` calls it.
- `BoxTop` under Classic means the rarest rarity up to the Secret Rare family (Secret, Prismatic, Gold, Extra and
  Platinum Secret). So Premium Gold guarantees a Gold Secret, not its Gold Rare base, and the Rarity Collections a
  Platinum Secret. Ultimate, Collector's, Ghost, Quarter Century and Starlight are never guaranteed.

**API differences:**
- **Shelves:**
  - `ShelfRow.Kind` (`ShelfRowKind`: `Set`, `Header`, `YearHeader`, `BackToggle`, `More`) says what a row is.
  - The section lists hold set rows only:
    - `Featured` is the live rows (Commons sets first, then file order), then the locked soon rows;
    - `NewArrivals` holds at most 6, `ShopShelves.MoreNew` counts the rest, and those rest are on `Back`;
    - `Rows()` adds the headers, the "+N more on the back shelves" row and the year sub-headers.
  - Also on `ShopShelves`: `FeaturedHeader` and `BackToggle(open)`, and the constants `HouseHeader`, `NewHeader` and
    `ImportsHeader`.
- **Arrivals:** `Arrivals.Visit` returns no years on the first visit ever and records nothing. `ShopVoice.Footer` treats
  `ShopVisits <= 1` as that visit and plays `arrival:<ShopYear>`, so **call `Footer` after `Visit`**.
- **Tags:**
  - `TagContext.Build` has an optional last parameter, `Scouting? scouting = null`: the written `keys:`. Without it each
    report is built from its deck and playbook.
  - `PackCardTags.KeyOf` is the key card's character id: copy it into `PackMoment.OpponentId`. `PackCardTags.Any` is new too.
- **Voice:**
  - `ShopVoice.Parse(text)` exists.
  - Set `ShopVoice.CardName` (`Func<uint, string>`) after loading, so `{card}` gets a name.
  - `VoiceLine.Speaker` stays "-" for labels; `IsLabel` tells you which. Labels are never marked heard or counted.
  - `Fill` takes keys with or without braces.
  - A specific hook falls back by dropping its last ":segment".
  - `ShopVoice.PrizePull(profile, circuits, bracketSeed)` applies the once-per-bracket rule (`shop:prize:<seed>`).
  - `ShopVoice.Tier(rarity)` and `CurrentBand` are public.
  - The Ultra spacing count saves as `voice spacing:pull:ultra N`; `voice pull:ultra N` is that hook's line counter.
  - In `Pull`, a candidate that's gated (spacing, or seen this circuit) lets the next one down speak. `{opponent}` is the
    character's `Name`, and `{rarity}` is the best pull's rarity.
- **Boxes:** `BoxResult.Opened` is false when DP was short. A box's refunds are credited to `refund.pack`, like a pack's.
  `Topper` is the first pull at the guaranteed rarity: the upgraded one, if a pack was upgraded.
- **Engine helpers:** these are instance methods, because their classes are built once per session:
  - `CardUnlocks.Progress(profile, setCode)` returns `(owned, total)`, (0, 0) for a pack that isn't a real set;
  - `CardUnlocks.FramedCard(setCode, profile = null)` returns `SetsDone`'s card once the set is done;
  - `PrizePacks.Odds(bracket, pool)` returns a `PrizeOdds`: `Field`, `FieldCards`, `TournamentSet`, `Tournament`, `Slab`
    and `SlabTenPercent`.
- **Grades:** `Grades.OddsFor(source)` returns 10 per-mille odds, grade 10 first (all at 8 for copies bought new).
  `Grades.ConditionOdds(source)` folds them into condition words: "Mint 45% · Near Mint 45% · Excellent 9% · Played 1%",
  and 40 / 48 / 10 / 2 for foils.
- **Circuits:**
  - `HouseMatches` returns `List<(int circuit, int match, CircuitMatch entry)>`;
  - `PlayersOf(deck)` gives a house pack's `{opponents}`;
  - `AllCleared(profile)` and `LateBands` are new. `SelectionPacks.Bands` is gone.
- **House packs:**
  - `SelectionPacks.Build` takes an optional last parameter, `string? name = null`: the filled `house-pack:name` line. It
    defaults to "House Pack: <title>". Its `Date` is "".
  - `SelectionPacks.IsHousePack(set)` is new.

**Data:**
- `campaign/featured.txt` is Table B, with five fixes the check found. `Problems()` returns 0.
  - Trap Masters Cup: LODT became DR1. The domain-monarch deck has no LODT card; DR1 has Reasoning, and zoodiac's My Body
    as a Shield.
  - Control Kings Cup: the DLCS soon line names hat only. The abc deck has no DLCS card.
  - Hybrid Engines Invitational: the MGED soon line became DUNE (Table A's pick). MGED holds one generic card each for
    exosister and unchained; DUNE holds Sharvara and Lord of Yama.
  - Synchro Dawn Classic: the LEHD soon line names teledad only. The lightsworn deck has only Monster Reborn there.
  - Why lines now name only cards in their set. Geargiarmor (GENF), Ghostrick Alucard (PGL2), My Body as a Shield
    (PGLD), Orcust Knightmare (SOFU) and Unchained Soul of Sharvara (CHIM) weren't.
- `campaign/shop.txt` is the voice doc's 11 blocks in order, header comment included. `Problems()` returns 0.
- Unity loads the engine as a local package (`unity/Packages/manifest.json` points at `src/Ygo.Engine`). The new `.cs`
  files have `.meta` files, ShopPrices.cs included. The `campaign/*.txt` files reach the app through `ProjectSetup`'s copy.

### Chunk B: the Unity shop

**Files:** `unity/Assets/Scripts/ShopScreen.cs`, `CampaignState.cs`, `BracketScreen.cs`, `DuelController.cs` (wiring and
checks), `DuelController.Qa.cs` (the QA stand-in).

**Work:**
- `CampaignState`: load `featured.txt` and `shop.txt` from the campaign folder; build house packs from `HouseMatches`
  with `SelectionPacks.PriceFor` and the `house-pack:name` label; build `ShopShelves` and a `TagContext` once per visit.
- `ShopScreen`:
  - the sectioned list, the collapsed back shelves, the default pick;
  - the header plate from `header`; the shelf line under the featured header; the `odds` line; the Recipe Book button;
  - the row second line and the detail panel's "Why this set", set progress, odds, box line and condition line;
  - soon rows (locked, `Warn` "Arrives at level N", buttons off); the RARITY SET chip;
  - the box button and the box reveal (summary, then the 10 best pulls face down, the topper last);
  - reveal tags under names, `IsHit` for the glow, the pull and box lines; `Arrivals.Visit` on each `Open()`;
  - fix the stale comment.
- `BracketScreen`: the Prize Pack odds in the entry panel; `ShopScreen.OpenPrize` shows the slab odds and the
  `prize-pull` line.
- Theme: existing roles only (sections 1, 2, 6). Convert nothing else (UI-THEME rule 9).
- `DuelController.cs`:
  - wire `RecipeClicked`;
  - update `-campaigncheck`: "Selection Pack" becomes "House Pack", and "the next band's selection packs are on sale
    already" becomes "house packs cover the current and the next circuit";
  - add a `-shopcheck <absolute folder>` check (below).
- `DuelController.Qa.cs`: the QA stand-in buys from the default pick, not the newest set; once per run, when DP is at
  least a box price plus its keep-DP, it buys one box and checks the DP and collection sums.

**`-shopcheck <folder>`** (absolute paths: relative ones write nothing). Screenshots and `Report` lines:
1. A fresh save: the plate reads GUS'S CARDS; the default pick is DB2; the FOR THE BEGINNER CIRCUIT rows are DB2, DB1,
   DR1, FET, then the soon rows; the back shelves are one collapsed row; the footer is the `arrival:2005` line.
2. The back shelves expanded: every real set on sale appears exactly once across the sections.
3. A rarity set (Hidden Arsenal 2, after a few forced levels): the chip, the badge line, and the odds rows (Secret Rare
   only above the base).
4. A house pack: the name, no year, the subtitle, its odds block.
5. A seeded pack of DB1 with a wishlist set: a Wanted tag, a "plays this" tag, the summary's "Set n/N (+k)".
6. A seeded box: the DP drops by the box price minus the refund, the collection grows by the kept copies, the topper is
   at the top rarity, `PacksOpened` grew by 24.
7. A forced level-up: NEW ARRIVALS shows that year's sets and the footer shows that year's `arrival` line; after 3
   visits the section is gone and the footer is a `footer` line.
8. A bracket entry: the Prize Pack odds are on screen.
9. The save after the check has the same DP, cards and new fields as the game.

**Acceptance:** `-campaigncheck` and `-shopcheck` pass with 0 issues; a `-qaplay` run of 30 or more duels from a fresh
save logs no shop issue and buys from featured sets; MainStreet looks unchanged everywhere except the shop's new parts.

### Chunk C: harness tests

**Files:** new `src/Ygo.Harness/ShopTest.cs`, called from `CampaignTest.Run` (one line); `src/Ygo.Harness/Arbitrage.cs`
(the box loop); `src/Ygo.Harness/LedgerCheck.cs` (the new sink). Not `Program.cs` and not `CampaignSim.cs`.

**Checks (all in `--campaign-test`):**
1. **Featured file:** parses; every circuit in circuits.txt has a shelf, plus `postgame`; `Problems()` is empty (catalog,
   era, soon window, deck membership, every deck served, 3-4 sets, at most 2 soon).
2. **The shelf follows the campaign:** for each circuit made current (by setting `MatchWins`), the featured rows are its
   shelf, every live row is on sale, and the default pick is never a rarity set, a house pack or an import.
3. **Nothing deleted:** the real sets across Featured, New arrivals and the back shelves equal the old rule's list (year
   ≤ `ShopYear`), each once.
4. **New arrivals:** a level-up records that year; the sets are new for 3 visits, then not; an old save's first visit
   records nothing; after `NewGamePlus()` nothing is new.
5. **House packs:** a fresh save gets the Beginner Circuit's and Rookie Cup's decks; a won match's deck leaves; all
   cleared gives none; names start "House Pack:", dates are empty, the price is `SelectionPacks.PriceFor`.
6. **Tags:** a built profile (wishlist, a campaign deck, a loaner) gets the right Wanted, In your deck (not the loaner)
   and Plays this (character names, Side Decks ignored); set progress equals `CardUnlocks.Progress`, and `CheckSets`
   completes the set exactly when it reads n/n.
7. **Odds:** for DB1, HA01, WISU, BPRO, EEN, RA04 and one house pack, `PackOdds.For` matches 20,000 seeded packs within
   0.5 percentage points for "at least one" and 2% for "per pack".
8. **Every card can be pulled:** every card of every catalog set has a chance above 0 per pack (exact, from the odds).
   With A1 this holds for all sets; it is the check that caught F2 and F3.
9. **Boxes:** over 500 seeded boxes, every box has 216 cards and a top-rarity pull; DP spent is the price, the refund is
   at most half, the ledger sink is `packs.box`, `PacksOpened` grew by 24, and the collection reconciles.
10. **Prize odds:** each slot's mix sums to 1, and the slab split equals `SlabTenPercent`.
11. **Voice:** shop.txt parses and `Problems()` is empty (hooks, speakers, scene ids, bands, shelf circuits,
    placeholders, lengths); every circuit has a `shelf:` pool. A scripted run of visits and packs shows: one footer per
    visit; an arrival beats the footer; at most one pull line per pack, by the priority order; Ultra on every third
    qualifying pack; Secret not right after a pack with a line; an opponent line once per opponent per circuit; no
    opponent line on a house pack; `kit-hello` before any other Kit line; unheard lines before heard ones; and the same
    lines on a second run with the same save (deterministic).
12. **Save:** the three fields round-trip, old saves load with defaults.
13. **`--arbitrage`:** a new loop, "buy a box of each featured set, sell everything at the Sell desk", returns under 100%.

**Acceptance:** `--campaign-test` and `--arbitrage` pass (`build/bin/check/Ygo.Harness.dll`); the new checks fail when
fed a broken featured line, a set above its era, or a rarity set with the A1 fallback removed.

### After the build

- Update docs/dev/TODO.md (E5, line 151) and the PROOF table's E5 row when chunk B passes.
- Ask the Economy Designer to re-run `--campaign-sim` and `--postgame-sim` on the rework: the box sink and the narrowed
  house packs move the spending shares.
