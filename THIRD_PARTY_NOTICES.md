# Third-party notices

Duel Circuit is built on other people's work. This page lists each part we use: where it comes from, the exact
version, its license, what we changed and how it ships. Our own licenses are in [LICENSING.md](LICENSING.md).

Thank you to everyone below.

## Summary

| Component | What it is | Version (pinned commit) | License | Changed? | Ships in the game? |
|---|---|---|---|---|---|
| [ygopro-core](#ygopro-core) | The rules engine | `baa3881` | AGPL-3.0-or-later; parts MIT | Yes, 3 patches | Yes, built as a library |
| [Lua](#lua) | The scripting language inside the rules engine | 5.4.8 (`6e22fed`) | MIT | No | Yes, inside the rules engine |
| [CardScripts](#cardscripts) | One script per card | `070cdbb` | AGPL-3.0-or-later | No | Yes, as source files |
| [BabelCDB](#babelcdb) | Card database | `d4f1107` | None stated | Converted | Yes, as `cards.bin` |
| [LFLists](#lflists) | Current ban lists | `1c34c46` | None stated | No | Yes, as `.conf` files |
| [YGOPRODeck](#ygoprodeck) | Card sets, printings, rarities | API, fetched for each data update | None stated | Converted | Yes, inside `printings.bin` |
| [Format Library](#format-library) | Historic decks and ban lists | Fetched for each data update | None stated | Reformatted | Yes, as `.ydk` and `.conf` files |
| [yugiohmeta.com](#yugiohmetacom) | Recent tournament deck lists | Fetched for each data update | None stated | Reformatted | Yes, as `.ydk` files |
| [Yugipedia](#yugipedia) | Research source (facts only) | — | — | — | No text or images copied |
| [Unity](#unity) | Game engine | Unity 6 (6000.3.27f1) | Unity's own terms | No | Yes, the runtime |
| [Build tools](#build-tools) | meson, ninja, mingw-w64, .NET | — | Various | No | Only the parts noted below |
| [Microsoft.Data.Sqlite](#microsoftdatasqlite) | Reads the card database in our tools | 7.0.20 | MIT | No | No |

## Status: the rules engine inside a Unity build

The game ships ygopro-core and CardScripts (both AGPL-3.0-or-later) inside a game built with Unity, which is not open
source. Our own code has an [extra permission](LICENSES/UNITY-PERMISSION.md) for this, but we can't give one for
other people's code.

**ygopro-core: answered (2026-11-08).** edo9300, ygopro-core's author, replied in
[edo9300/ygopro-core#179](https://github.com/edo9300/ygopro-core/issues/179):

- No separate permission is needed to use the library. When the game is distributed, its source has to be available
  under the AGPL-3.0, which it is.
- Changes to the core itself must be under the AGPL from the start. Ours are: [patches/ygopro-core/](patches/ygopro-core/),
  and every release includes the patched core's source.
- On the Unity runtime: "you can be assured I won't go after someone linking to the unity runtime."
- He's happy to be credited under the Project Ignis name.

**CardScripts, BabelCDB, LFLists: answered (2026-11-12).** We asked Project Ignis in a ticket on their Discord server
(opened 2026-11-08) about the game using their card scripts, card database and ban lists. A Project Ignis staff member
replied on 2026-11-12:

- edo9300's answer above applies to these as well.
- If you find a problem with a card script in Duel Circuit, don't report it to Project Ignis unless you've tested it
  and seen the same problem in EDOPro. Report it to us instead: most problems in this game are ours. See
  [CONTRIBUTING.md](CONTRIBUTING.md#card-effects-that-seem-wrong).

If they ever ask for anything else, we'll change it the moment they do, whether that's how they're credited, what the
game uses, or removing their work from it. The game thanks them on its About screen.

**Our promise to the projects we build on.** Duel Circuit will never be sold, and no one makes money from it: no
price, no ads, no paid extras, no donations. We will never pass their work off as our own: ygopro-core,
Project Ignis's card scripts, card database and ban lists, and every other project in this file are credited by name,
in this file and on the game's About screen.

## ygopro-core

The rules engine: it runs the duel, applies the rules and runs the card scripts.

- **Source:** https://github.com/edo9300/ygopro-core
- **Pinned commit:** `baa3881d0bae48e9994f915f5c931be53c1ba8ef` (git submodule `vendor/ygopro-core`)
- **License:** GNU Affero General Public License, version 3 or later. Copyright (C) 2019-2026 Edoardo Lolletti
  (edo9300) and contributors. The full text is in `vendor/ygopro-core/COPYING` and in our [LICENSE](LICENSE) (the same
  license).
- **Ships as:** `libocgcore.dylib` (macOS, Apple silicon) and `ocgcore.dll` (Windows, 64-bit), in
  `unity/Assets/Plugins/` and inside every build.

### What we changed

Three patches in [`patches/ygopro-core/`](patches/ygopro-core/), applied by [`tools/build_core.sh`](tools/build_core.sh)
before the build. Patches 0001 and 0003 mark their changes with a `Duel Circuit:` comment.

| Patch | Date | What it does |
|---|---|---|
| `0001-second-player-starts.patch` | 2026-10-26 | Adds a duel flag, `DUEL_SECOND_PLAYER_STARTS`, that gives the first turn to the second player (so the player who lost the coin toss, or chose to go second, can go second). |
| `0002-deterministic-effect-order.patch` | 2026-10-27 | Orders sets of effects by when each effect was created instead of by memory address, so the same duel plays out the same way every time (replays depend on this). |
| `0003-shuffle-after-deck-look.patch` | 2026-11-07 | Adds a duel flag, `DUEL_SHUFFLE_AFTER_DECK_LOOK`: a Deck that a resolving effect looked through is shuffled afterwards, whether or not a card was found, unless the effect kept the Deck's order (top or bottom cards). |

### How the Windows library is built

`ocgcore.dll` is cross-compiled on a Mac with mingw-w64 (GCC) using [`tools/mingw-w64.ini`](tools/mingw-w64.ini). The
GCC runtime libraries (libgcc, libstdc++) are linked into it statically; they are licensed under the GNU GPL with the
GCC Runtime Library Exception, which allows this. Parts of the mingw-w64 runtime may also be linked in; they are under
permissive licenses (see the mingw-w64 `COPYING` files). The library uses only Windows' own system libraries.

### Getting the source of the rules engine we ship

The AGPL gives you the right to the source code of the rules engine in any Duel Circuit build. It is:

1. This repository at the release's tag (for example `v0.1.0`), including its submodules:
   `git clone --recurse-submodules https://github.com/fullstackjake/duel-circuit.git` then
   `git checkout <tag> && git submodule update --init --recursive`.
2. `vendor/ygopro-core` at the pinned commit (with Lua in `vendor/ygopro-core/lua/src`).
3. The patches in `patches/ygopro-core/`.
4. The build scripts: `tools/build_core.sh` (macOS; `--windows` also builds the Windows library) and
   `tools/mingw-w64.ini`.

Releases should also attach a source archive of the patched engine and the card scripts, so the source stays
available even if an upstream repository changes. If a release you downloaded doesn't have one, open an issue and we
will provide the source free of charge.

### Notices from ygopro-core's LICENSE file

ygopro-core includes code under the MIT license. Its `LICENSE` file carries these notices, reproduced here as they
appear there:

```
Modified from Fluorohydride's YGOPro script engine
--------------------------------------------------
https://github.com/Fluorohydride/ygopro-core, lead maintainers DailyShana, mercury233

The MIT License (MIT)

Copyright (c) 2015  Argon Sun (Fluorohydride)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.


Lua
---------------

Lua is licensed under the terms of the MIT license reproduced below.
This means that Lua is free software and can be used for both academic
and commercial purposes at absolutely no cost.

For details and rationale, see https://www.lua.org/license.html .

Copyright © 1994–2019 Lua.org, PUC-Rio.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

## Lua

The scripting language the rules engine runs the card scripts in. It is built into the rules engine.

- **Source:** https://github.com/lua/lua (git submodule `lua/src` inside ygopro-core), https://www.lua.org
- **Version:** Lua 5.4.8, commit `6e22fedb74cf0c9b6656e9fce8b7331db847c605`
- **License:** MIT. The notice is reproduced above. The current copyright line in `lua.h` is
  "Copyright (C) 1994-2025 Lua.org, PUC-Rio".
- **Changed:** no.

## CardScripts

Project Ignis' scripts for every card: one Lua file per card, plus shared procedures.

- **Source:** https://github.com/ProjectIgnis/CardScripts
- **Pinned commit:** `070cdbb59eab9f1e4d03302cf6b6ee338e130ca3` (git submodule `vendor/CardScripts`)
- **License:** GNU Affero General Public License, version 3 or later. Copyright (C) 2020 Project Ignis contributors
  (see each file's history and author line). The full text is in `vendor/CardScripts/COPYING`.
- **Changed:** no.
- **Ships as:** the `.lua` files, copied unchanged into the game's data folder (`StreamingAssets/CardScripts`). They
  are their own source code.

## BabelCDB

Project Ignis' card database: every card's name, text, stats and IDs.

- **Source:** https://github.com/ProjectIgnis/BabelCDB
- **Pinned commit:** `d4f1107a1255d41f0ea2697dfd562df79cbee9ef` (git submodule `vendor/BabelCDB`)
- **License:** none stated in the repository. Card names and card text are Konami's.
- **Changed:** converted by our tools into `cards.bin` (not committed; made from the database for each build).
- **Ships as:** `cards.bin` in the game's data folder.
- Project Ignis has answered our request; see [the status above](#status-the-rules-engine-inside-a-unity-build).

## LFLists

Project Ignis' ban lists (Forbidden and Limited Lists). The lists themselves are Konami's.

- **Source:** https://github.com/ProjectIgnis/LFLists
- **Pinned commit:** `1c34c462f8abd5627ec58b95507357d9b066f122` (git submodule `vendor/LFLists`)
- **License:** none stated in the repository.
- **Changed:** no.
- **Ships as:** the `.conf` files, copied into the game's data folder (`StreamingAssets/lflists`).
- Project Ignis has answered our request; see [the status above](#status-the-rules-engine-inside-a-unity-build).

## YGOPRODeck

Card-set data: which sets each card was printed in, when, and at which rarities. Also six YCS Houston 2026 deck lists.

- **Source:** the YGOPRODeck API, https://db.ygoprodeck.com/api/v7/ (`cardinfo.php`, `cardsets.php`); guide at
  https://ygoprodeck.com/api-guide/. Deck pages at https://ygoprodeck.com.
- **License:** none stated for the data. Credit to YGOPRODeck.
- **Changed:** converted by our tools into `printings.bin` (not committed; made for each build). Deck lists are
  reformatted as `.ydk` files and credited in [decks/history/SOURCES.md](decks/history/SOURCES.md).
- **Ships as:** `printings.bin` in the game's data folder; deck lists in `decks/history/`.

## Format Library

Most of the historic tournament decks, and the historic TCG ban lists (2004 to 2026).

- **Source:** https://www.formatlibrary.com
- **License:** none stated for the data.
- **Changed:** reformatted as `.ydk` deck files (`decks/history/`) and one ban-list file (`lflists/history.lflist.conf`).
  A few cards missing from the fetched lists, and one recent list, were added by hand from Konami's published lists.
  Players' names were removed from the deck files; each deck's builder and event are credited in
  [decks/history/SOURCES.md](decks/history/SOURCES.md).
- **Ships as:** those files, in the game's data folder.

## yugiohmeta.com

Some recent tournament deck lists (2026).

- **Source:** https://www.yugiohmeta.com
- **License:** none stated for the data.
- **Changed:** reformatted as `.ydk` deck files in `decks/history/`, without players' names; credited in
  [decks/history/SOURCES.md](decks/history/SOURCES.md).
- **Ships as:** those files, in the game's data folder.

Thank you to Format Library, YGOPRODeck, yugiohmeta.com and every deck builder credited in
`decks/history/SOURCES.md`.

## Yugipedia

- **Source:** https://yugipedia.com
- **Used for:** research only: facts such as when a card's text changed and when a product came out
  (`campaign/texts.txt`, `campaign/printing-dates.txt`). No Yugipedia text or images are copied into this project.

## Unity

The game engine.

- **Source:** https://unity.com. Editor version 6000.3.27f1 (Unity 6).
- **License:** Unity's own terms (the Unity Terms of Service and related terms). Unity is not open source. Unity is a
  trademark of Unity Technologies.
- **Packages:** `com.unity.inputsystem` 1.20.1 and `com.unity.ugui` 2.0.0, under the
  [Unity Companion License](https://unity3d.com/legal/licenses/unity_companion_license); plus Unity's built-in modules.
- **Changed:** no.
- **Ships as:** the Unity runtime in every build. Our code may be combined with it under the
  [Unity permission](LICENSES/UNITY-PERMISSION.md).

## Build tools

Used to build the game. They are not part of the game, except where noted.

| Tool | Used for | License |
|---|---|---|
| [meson](https://mesonbuild.com) and [ninja](https://ninja-build.org) | Building the rules engine | Apache-2.0 |
| [mingw-w64](https://www.mingw-w64.org) with GCC | Cross-compiling the Windows rules engine | GCC: GPL-3.0 with the GCC Runtime Library Exception; mingw-w64 runtime: permissive licenses. libgcc and libstdc++ are linked into `ocgcore.dll` (see above). |
| [.NET 7 SDK](https://dotnet.microsoft.com) | Building and testing our code outside Unity | MIT |

## Microsoft.Data.Sqlite

- **Source:** https://github.com/dotnet/efcore (NuGet package `Microsoft.Data.Sqlite` 7.0.20, with its SQLitePCLRaw
  and SQLite dependencies)
- **License:** MIT (SQLitePCLRaw: Apache-2.0; SQLite: public domain)
- **Used for:** our command-line tools (`src/Ygo.Harness`) read the BabelCDB database with it.
- **Ships in the game:** no.

## Inspirations

Tag Force, Master Duel, Link Evolution and Duel Links inspired parts of the game's design. They are Konami's games.
No code, art, music or text from them is used.

## Trademarks

Yu-Gi-Oh! is a trademark of Konami. Card names, card text and card images belong to Konami and the other Yu-Gi-Oh!
rights holders. EDOPro and Project Ignis are names of the Project Ignis team. Unity is a trademark of Unity
Technologies. Duel Circuit is not affiliated with or endorsed by any of them.

## Something missing or wrong?

If you made something we use and it isn't credited right, or you are a rights holder with a concern, open an issue on
GitHub and we'll act promptly.
