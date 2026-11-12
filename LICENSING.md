# Licensing

## The short version

You can play, copy, share and change this game for free. If you share a changed version, share its source too,
under the same license.

- **The code** is under the GNU Affero General Public License, version 3 or later (AGPL), with an extra permission
  that lets it be built with Unity.
- **The art, story and characters** are under Creative Commons Attribution-ShareAlike 4.0 (CC BY-SA 4.0). You can
  use and change them. Give credit and keep your version under the same license.
- **The name "Duel Circuit" and the logo are not licensed.** If you make your own version, give it another name and
  another logo.
- **Yu-Gi-Oh! cards belong to Konami.** None of our licenses cover card names, card text, card pictures or anything
  else of Konami's.

This page is a summary, not legal advice. The license texts are what count.

## What is under which license

| What | Where | License | License text |
|---|---|---|---|
| Our code: the game, the CPU duelists, the testing tools, the build scripts | `src/`, `unity/` (except the items below), `tools/`, `Directory.Build.props` | AGPL-3.0-or-later, plus the Unity permission | [LICENSE](LICENSE), [LICENSES/UNITY-PERMISSION.md](LICENSES/UNITY-PERMISSION.md) |
| Our changes to the rules engine | `patches/ygopro-core/` | AGPL-3.0-or-later, on ygopro-core's terms (they change ygopro-core); the Unity permission doesn't cover them | [LICENSE](LICENSE) |
| Character portraits, duel mats, map art, coin art | `art/` (except `art/logo/`) | CC BY-SA 4.0 | [LICENSES/CC-BY-SA-4.0.txt](LICENSES/CC-BY-SA-4.0.txt) |
| Characters: profiles, personalities, lines | `personalities/` | CC BY-SA 4.0 | [LICENSES/CC-BY-SA-4.0.txt](LICENSES/CC-BY-SA-4.0.txt) |
| Story, dialogue and campaign content | `campaign/` | CC BY-SA 4.0 (see "Facts and card data" below) | [LICENSES/CC-BY-SA-4.0.txt](LICENSES/CC-BY-SA-4.0.txt) |
| Our own deck lists and recipes | `decks/` (except `decks/history/`) | CC BY-SA 4.0 (see "Facts and card data" below) | [LICENSES/CC-BY-SA-4.0.txt](LICENSES/CC-BY-SA-4.0.txt) |
| Guides and documentation | `docs/`, `README.md`, `CONTRIBUTING.md`, this file | CC BY-SA 4.0 | [LICENSES/CC-BY-SA-4.0.txt](LICENSES/CC-BY-SA-4.0.txt) |
| The name "Duel Circuit" and the logo | `art/logo/`, `unity/Assets/Art/AppIcon.png` | Not licensed: all rights reserved | — |
| Historic tournament decks and historic ban lists | `decks/history/`, `lflists/` | Compiled from public sources; credited, not ours to license | [decks/history/SOURCES.md](decks/history/SOURCES.md), [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) |
| Rules engine, card scripts, card database, ban lists | `vendor/` | Their own licenses | [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) |
| Built rules engine | `unity/Assets/Plugins/` | AGPL-3.0-or-later (ygopro-core) and MIT (parts of ygopro-core, Lua) | [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) |
| Code of Conduct | `CODE_OF_CONDUCT.md` | Contributor Covenant 2.1, CC BY 4.0 | [contributor-covenant.org](https://www.contributor-covenant.org) |
| License texts | `LICENSE`, `LICENSES/CC-BY-SA-4.0.txt` | Copied word for word; their own terms | — |

A file that has its own license notice follows that notice.

Copyright (C) 2026 Jake Joseph (GitHub: fullstackjake) and the Duel Circuit authors.

## The code: AGPL with a Unity permission

The code is free software under the [GNU Affero General Public License](LICENSE), version 3 or (at your option) any
later version. In plain words:

- You can use, copy, change and share it, for free.
- If you share it, or a game built from it, share the source code too, under the AGPL.
- There is no warranty.

The game is built with Unity, which is not open source. The
[Unity permission](LICENSES/UNITY-PERMISSION.md) is an extra permission that lets our code be
combined with the Unity runtime and shipped. It only covers our own code. Our patches in `patches/ygopro-core/` are
changes to ygopro-core, so they follow ygopro-core's terms, without this permission. It can't give permission for the
rules engine (ygopro-core) or the card scripts (CardScripts), which belong to their authors.
ygopro-core's author has answered that the core may ship this way, as long as the source stays available under the AGPL ([edo9300/ygopro-core#179](https://github.com/edo9300/ygopro-core/issues/179)). Project Ignis has answered that they are fine with the game using CardScripts, BabelCDB and LFLists. Details: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Art, story and characters: CC BY-SA 4.0

The portraits, mats, map pictures, coin, characters, story and dialogue are under
[Creative Commons Attribution-ShareAlike 4.0 International](LICENSES/CC-BY-SA-4.0.txt). In plain words:

- You can share them and change them, for any use the license allows.
- Give credit, link the license, and say if you changed something.
- If you share a changed version, use the same license.

A credit line can look like this:

> Vera's portrait from Duel Circuit by the Duel Circuit authors (github.com/fullstackjake/duel-circuit),
> CC BY-SA 4.0.

The portraits, mats and map pictures were made with AI image tools for this project. In some countries AI-made
pictures have no copyright. Where we do hold rights, this license applies.

## The name and the logo

"Duel Circuit" and the logo (`art/logo/` and `unity/Assets/Art/AppIcon.png`) are not under any of these licenses.
All rights reserved. This keeps people from mixing up a changed version with this game.

- You may say your project is "based on Duel Circuit" or "a fork of Duel Circuit".
- Don't call your version "Duel Circuit" and don't use the logo for it. Pick your own name and logo.
- If you build the game yourself to share it, replace `art/logo/` and `unity/Assets/Art/AppIcon.png` first.

## Konami's rights

Duel Circuit is a fan project. It is not made, approved or endorsed by Konami.

- Yu-Gi-Oh! and everything that comes with it belongs to Konami and the other Yu-Gi-Oh! rights holders: card names,
  card text, card pictures, logos, characters from the anime and manga, and the official games. Yu-Gi-Oh! is a
  trademark of Konami.
- None of our licenses cover any of this. We can't give you rights we don't have.
- This repository and the game ship no card pictures. If you add your own, they stay on your computer: don't share
  them and don't commit them.

## Facts and card data

Some files list facts: which cards were in a tournament deck, which cards a ban list limits, when a card was printed,
which rarity it came in. They come from public sources (Format Library, YGOPRODeck, yugiohmeta.com, Yugipedia, Project Ignis), which
are credited in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and [decks/history/SOURCES.md](decks/history/SOURCES.md).

- Card names, card text and card IDs in any file are Konami's. Our licenses don't cover them.
- Historic deck lists (`decks/history/`) were built by the players credited in `decks/history/SOURCES.md`. We share
  them with credit; we don't license them.
- Where one of our files (for example in `campaign/` or `decks/`) mixes our own writing with facts like these, CC BY-SA
  4.0 covers our writing and choices, not the facts or Konami's material.

## Please don't sell it

Duel Circuit is free and non-commercial. We ask that nobody sells it, charges for it or uses it to make money.

This is a request, not a license term. The AGPL and CC BY-SA allow selling. But the game is built around Konami's
cards, so selling it could get you into trouble with Konami.

## Questions

Open an issue on GitHub. If you are a rights holder and something here is yours, open an issue and we'll act
promptly.
