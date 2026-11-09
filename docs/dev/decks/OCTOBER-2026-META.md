# The October 2026 meta in the campaign (2026-10-28)

## What the top decks are

yugiohmeta.com's TCG tier list (the last month of tops, after the October 19 Forbidden & Limited list), every deck
with at least 1.2% of tops:

| # | Deck | Share | In the game |
|---|------|-------|-------------|
| 1 | Elfnote | 24.7% | already (Expert Series: The New Wave) |
| 2 | Angelechy | 13.3% | already (The New Wave, Expert Tag Cup) |
| 3 | Dark Magician Chaos Ritual | 8.0% | **new** |
| 4 | Branded | 5.3% | already (Houston 2026) |
| 5 | Chaos Ritual | 4.3% | **new** |
| 6 | Blitzclique | 3.7% | **new** |
| 7 | Artmage | 2.8% | **new** |
| 8 | Radiant Typhoon | 2.2% | **new** |
| 9 | Sky Striker | 2.2% | already |
| 10 | Sacred Beast | 2.2% | **new** |
| 11 | Maliss | 1.9% | already |
| 12 | Invoked | 1.9% | already (Mekk-Knight Invoked) |
| 13 | Yummy (and Mitsurugi Yummy) | 1.9% | already |
| 15 | Mitsurugi (and its hybrids) | 1.5% | already (Houston 2026) |
| 16 | Lunalight | 1.5% | **new** |
| 17 | Mermail Atlantean | 1.2% | **new** |
| 18 | Power Patron | 1.2% | **new** |
| 19 | Magistus Fairy Tail | 1.2% | **new** |
| 21 | Dracotail | 1.2% | **new** |

Below 1.2% (DoomZ, Blue-Eyes, Destiny HERO, Synchron and the rest) weren't added.

## The decks

Each new deck is a recent topping TCG list from yugiohmeta.com (`decks/history/<deck>.ydk`, its `#source` line), made legal
under the October 2026 TCG list, which is now in `lflists/history.lflist.conf` as "TCG October 2026" (from
vendor/LFLists). Lists that topped before October 19 had their now-forbidden cards swapped, noted in each deck's
`#swapped` line:
- Called by the Grave became Infinite Impermanence, and Triple Tactics Talent became Pot of Prosperity.
- Droll & Lock Bird, Griffoh and Terminus copies over their new limit of 1 became hand traps.
- Forbidden Extra Deck cards (Albion, Dugares, Bagooska, Cross-Sheep, Trishula, Varudras and others) became a legal
  card of the same kind.

Some cards use the database's OCG-style names (Mediclius is "Medicurius the Power Patron of Illusions", the Ars Magna
Spells are "Ars Magna of ..."); they're mapped through YGOPRODeck's card data, and every card has a script.

## Measured strength

`--rate-decks --rounds 300`, all 90 decks (`campaign/deck-ratings.txt`), no engine errors. The last four have
playbooks now ([PLAYBOOKS-OCTOBER-2026.md](PLAYBOOKS-OCTOBER-2026.md)); before them they rated 1382 to 1512:

| Deck | Elo | Rank |
|------|-----|------|
| Chaos Ritual | 1895 | 2 |
| Dark Magician Chaos Ritual | 1838 | 5 |
| Sacred Beast | 1669 | 18 |
| Blitzclique | 1620 | 23 |
| Mermail Atlantean | 1575 | 32 |
| Lunalight | 1563 | 34 |
| Dracotail | 1543 | 35 |
| Radiant Typhoon | 1683 (was 1382) | 16 (was 69) |
| Magistus Fairy Tail | 1630 (was 1403) | 22 (was 61) |
| Artmage | 1617 (was 1512) | 25 (was 38) |
| Power Patron | 1538 (was 1403) | 36 (was 62) |

## Where they play

Placed by strength, as the bands are:
- **Expert Series: Fall Format** (new, after The New Wave): Artmage, Dracotail, Mermail Atlantean, Lunalight. Prize
  7,500.
- **Fall Format Tag Cup** (new, opens with it): Power Patron with Artmage, Magistus Fairy Tail with Dracotail, Radiant
  Typhoon with Lunalight. Prize 5,500.
- **Master League: October 2026** (new, after Houston 2026, before the Gauntlet): Blitzclique, Sacred Beast, Dark
  Magician Chaos Ritual, Chaos Ritual, under the October 2026 list. Prize 9,500.

Eleven new duelists (`personalities/`, `docs/dev/CHARACTERS.md`), all fictional: Maestra Vellum (Artmage), Ember Rahu
(Dracotail), Captain Maris Deepwell (Mermail Atlantean), Selune (Lunalight), Patron Orvel (Power Patron), Aurelie
(Magistus Fairy Tail), Ilara Gale (Radiant Typhoon), Jolt (Blitzclique), Warden Hask (Sacred Beast), Mage Valdris (Dark
Magician Chaos Ritual) and Sir Lucan Graye (Chaos Ritual). Selune and Sir Lucan, each circuit's final opponent, have
signature mats. New story scenes introduce the Fall Format circuit, its Tag Cup, and the October list after Houston.

## Saves

Saves count circuits by position ("12.3", "tag5.0"), so new circuits in between would have moved wins onto other
matches. Saves now record the circuit and Tag Cup names they count by, and `Circuits.Migrate` moves older saves' keys
by name when the campaign loads. Saves from before this change are read as the layout without the three new entries.
The Stuck? advice's Master Tag Cup entries moved from tag5 to tag6 to match.
