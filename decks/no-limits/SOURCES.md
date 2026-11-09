# Sources of the No Limits peak lists

Provenance for `decks/no-limits/` (docs/dev/NO-LIMITS-SPEC-2026-11-02.md, P4 and N12). For the repo only: the game never
reads this file, and no deck here carries a `#source` line, because the No Limits pilots are fictional and a real
player's name must not reach the deck editor or the Unlisted page. The real lists are credited to the players who built
them here.

Each build starts from the real list of its deck's peak month, Main and Extra Deck card for card (the Side Deck is left
out: the game never sides), fetched 2026-11-02. NL-1 (2026-11-03) turned each into its peak build: the deck's own banned
cards back at 3, its contraband picks and its cuts, as each file's `#why` line records; the lists as played are in the
recipe file's history (git, NL-0). Recipes: `decks/recipes/no-limits.txt`.

## Wave 1

| File | Deck | Event, placement | Date | List in effect | Player | Source |
|---|---|---|---|---|---|---|
| `nl-snake-eye` | Fiendsmith Snake-Eye | North America WCQ 2024 (2,635 players), 1st | 19-21 Jul 2024 | TCG April 2024 | Aditya Dharap | Format Library deck 48928 (https://www.formatlibrary.com/decks/48928), event NAWCQ24 |
| `nl-mitsurugi` | Ryzeal Mitsurugi | YCS Providence 2025 (1,572 players), 1st | 3 May 2025 | TCG April 2025 | Charley Futch | YGOPRODeck (https://ygoprodeck.com/deck/ryzeal-mitsurugi-599286), tournament https://ygoprodeck.com/tournament/ycs-providence-3006 |
| `nl-ryzeal` | Fiendsmith Ryzeal | YCS Orlando 2025 (2,223 players), 1st | 1 Feb 2025 | TCG December 2024 | Tayveon Crowley | Format Library deck 50316 (https://www.formatlibrary.com/decks/50316), event YCSORL25 |
| `nl-vanquish-soul` | K9 Vanquish Soul | YCS Bologna 2025 (2,429 players), 1st | 22 Nov 2025 | TCG October 2025 | Pierre Burgals | Format Library deck 87897 (https://www.formatlibrary.com/decks/87897), event YCSBOL25 |
| `nl-tenpai` | Tenpai Dragon | European WCQ 2024 (2,333 players), 1st | 26-28 Jul 2024 | TCG April 2024 | Esteban Re | Format Library deck 49076 (https://www.formatlibrary.com/decks/49076), event EUWCQ24; the same list as YGOPRODeck `tenpai-dragon-513387` |
| `nl-branded` | Despia Branded | European WCQ 2026 (2,268 players), Top 4 | 11 Jul 2026 | TCG May 2026 | Paul-Robert Goß | YGOPRODeck (https://ygoprodeck.com/deck/despia-branded-721713), tournament https://ygoprodeck.com/tournament/european-wcq-2026-4747 |
| `nl-yubel` | Fiendsmith Kashtira Yubel | YCS Cancún 2024, 1st | 5 Oct 2024 | TCG September 2024 | Ittipat Arunnaveesiri | YGOPRODeck (https://ygoprodeck.com/deck/fiendsmith-kashtira-yubel-534552) |

## Choices against the spec's first pick

- **Snake-Eye:** the spec named YGOPRODeck `fiendsmith-snake-eye-520593` "as read by the research; confirm it's a NAWCQ
  top-cut list". It isn't: it is the YCS Sacramento winner (17 Aug 2024). The NAWCQ 2024 winner (Format Library 48928)
  is used instead, as the spec asked.
- **Mitsurugi:** the research's `ryzeal-mitsurugi-714735` is a 2026 national list (Trinidad and Tobago, 30 May 2026),
  not the 2025 peak. The spec's first choice, the YCS Providence 2025 winner, is used.
- **Vanquish Soul:** the spec's `k9-vanquish-soul-692109` is the 300th YCS Guadalajara winner (14 Feb 2026, TCG
  February 2026 list), not a late-2025 list. The spec's first choice, the YCS Bologna 2025 winner, is used (its
  3 Hollie Sue, 3 Jokul, 3 "A Case for K9", 3 Noroi, 1 Razen and 1 Stake your Soul! fit the October 2025 list).
- **Branded:** the spec's `despia-branded-719679` is the Kuwait National Championship 2026 winner. The spec's first
  choice, an EUWCQ 2026 Top 4 list without the Light and Darkness Ritual package, is used.
- **Yubel:** neither the Worlds 2024 list (its own card pool) nor a YCS Lille 2024 list is on YGOPRODeck or Format
  Library. The YCS Cancún 2024 winner (the research's `fiendsmith-kashtira-yubel-534552`, one week after Lille, same
  TCG September 2024 list) is used.

## The Alley Brawl's out-of-towners (spec section 8)

`nl-<deck>-brawl.ydk` are not tournament lists. Each is a decks/history base (its own SOURCES.md credits the player) made
legal for the Alley list with `Loaners.Adjust` (one copy of anything the list limits, topped up from the base's own cards,
its Side Deck among them), plus three Basement Game cards from campaign/alley.txt picked by the splash builder
(`--brawl-gen snake-eye,k9-vanquish-soul,yubel,tenpai-dragon --seed 1`, src/Ygo.Harness/BrawlGen.cs) and frozen into the
recipe file.

| File | Base | Basement splash (seed 1) | Cut by the builder |
|---|---|---|---|
| `nl-snake-eye-brawl` | snake-eye (YCS Indianapolis 2024) | Raigeki, Change of Heart, Pot of Greed | none (the list's cuts made room) |
| `nl-vanquish-soul-brawl` | k9-vanquish-soul (YCS Vancouver 2025) | Change of Heart, Monster Reborn, Pot of Greed | none |
| `nl-yubel-brawl` | yubel (YCS Bologna 2024) | Pot of Greed, Raigeki, Change of Heart | Escape of the Unchained, Kashtira Birth, Fabled Lurrie |
| `nl-tenpai-brawl` | tenpai-dragon (YCS Bologna 2024) | Raigeki, Heavy Storm, Change of Heart | Lightning Storm x2, Koa'ki Meiru Drago |
