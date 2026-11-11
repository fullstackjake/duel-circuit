# CLAUDE.md

Duel Circuit: a free, fan-made, offline single-player Yu-Gi-Oh! campaign game (Unity 6 + C#), built on EDOPro's rules
engine. AGPL-3.0 code, CC BY-SA 4.0 content. Full guides: [docs/DEVELOPING.md](docs/DEVELOPING.md),
[docs/BUILDING.md](docs/BUILDING.md), [docs/MODDING.md](docs/MODDING.md), [CONTRIBUTING.md](CONTRIBUTING.md).

## Layout

| Path | What it is |
|---|---|
| `src/Ygo.Engine/` | Shared C# library (netstandard2.1, C# 9): native bindings, prompt parsing, duel loop, CPU duelists (`AI/`), campaign rules (`Campaign/`), sound. Also a Unity local package (`com.ygocampaign.engine`) |
| `src/Ygo.Harness/` | Console app (net7.0): CPU-vs-CPU duels, tests, studies, data exports. Flags are documented in `Program.cs` |
| `unity/` | Unity 6 project (6000.3.27f1). Scripts in `Assets/Scripts/`, build/editor menu in `Assets/Editor/ProjectSetup.cs`, one scene `Assets/Scenes/Main.unity` |
| `vendor/` | Submodules: `ygopro-core` (C++ rules engine), `CardScripts` (Lua), `BabelCDB` (SQLite card data), `LFLists`. Run `git submodule update --init` after cloning |
| `patches/ygopro-core/` | Our 3 core patches, applied by `tools/build_core.sh` |
| `campaign/` | The campaign as data (circuits, story, shop, library, alley, ladder...). Each file explains its format at the top |
| `personalities/` | One `.profile` per character: deck pool, dialogue, play-style knobs |
| `decks/`, `lflists/` | Built-in `.ydk` decks; historic ban lists |
| `art/` | Portraits, mats, map, coin, logo. No card art, ever |
| `tools/` | Core build, release, deck/banlist fetchers and checkers |
| `docs/dev/` | Dated specs, plans, QA reports, handoffs. May be stale: the code wins. Start from the newest `HANDOFF-*.md` and `TODO.md` |
| `build/`, `data/`, `mods/`, `rules/` | Not committed |

## Architecture

```
Unity front end (unity/Assets/Scripts)     Harness (src/Ygo.Harness)
                 \                           /
            Ygo.Engine (C#) --P/Invoke (Native/OcgApi.cs)--> ygopro-core (ocgcore.dll / libocgcore.dylib)
                                                               runs CardScripts (Lua) + card data
```

- A `DuelRecipe` (card data, scripts, `DuelSettings`, decks) fully defines a duel. `Duel` creates it; `DuelRunner` steps
  the core, sends messages to listeners and hands prompts (`Protocol/`) to each seat's `IDuelist`.
- `IDuelist` implementations: `SmartDuelist`, `RandomDuelist`, `HumanDuelist` (via `Interactive/PromptPresenter`;
  returns null until the player clicks), replays.
- Recipe + `ResponseLog` reproduces a duel exactly. That powers replays and `TurnPlanner`'s scratch-copy lookahead, so
  keep duels deterministic.
- CPU stack: `SmartDuelist` (generic heuristics, card roles from `CardTraits`) + `AI/Playbooks/` (per-deck) +
  `TurnPlanner` (one-step lookahead) + rules sense (`SmartDuelist.Sense.cs`, `SmartDuelist.Taxers.cs`).
- Unity: `DuelController` (partial class over `DuelController.*.cs`) is the composition root; `BoardView.*` draws the
  duel. The editor reads data straight from the repo; built apps read `StreamingAssets`. F12 dev panel needs `-dev`,
  the editor, a dev build or a `-*check` flag.
- `Directory.Build.props` sends all `dotnet build` output to `build/`. Never leave `bin/`/`obj/` under `src/`: Unity
  would import them as duplicate assemblies.

## Build and check

An Apple-silicon Mac builds both games; a Windows PC builds the Windows game (docs/BUILDING.md#build-on-windows).
On Windows, the harness and the Unity editor use the prebuilt `unity/Assets/Plugins/Windows/x86_64/ocgcore.dll`; the
harness runs on .NET 7 or newer (`RollForward`). Text files must keep LF endings (`.gitattributes`): campaign code and
tests match on `"\n"`, so CRLF checkouts break them. Fetch submodules with `-c core.autocrlf=false`.

```bash
dotnet build src/Ygo.Harness -c Release            # -> build/bin/Ygo.Harness/Release/net7.0/
dotnet build/bin/Ygo.Harness/Release/net7.0/Ygo.Harness.dll --games 3   # smoke test
```

Before finishing a change, run the checks that cover it (none may report a failure):

- `--campaign-test`, `--deckbuilding-test`, `--texts-check`
- `--arbitrage` for anything touching DP/economy (0 DP-positive)
- `python3 tools/check_history_decks.py` for deck/ban-list changes
- Bot changes: compare before/after with `--matrix`, `--rate-decks` or the fitting study, same seeds
- Screen changes: the matching app checks (docs/DEVELOPING.md#app-checks)

Long studies take hours: run them in the background from a copy of the harness
(`cp -R build/bin/Ygo.Harness/Release/net7.0 build/bin/snap-<name>`) or build into `build/bin/<name>`, so rebuilds
can't break them.

## Owner rules (breaking one is a bug)

- **CPUs always play at full strength.** Tune difficulty by which deck a CPU plays, never by weakening it. Ban lists
  bind only the player.
- **Every DP change goes through `Profile.Spend`/`Earn`** with a ledger kind; nothing may be DP-positive.
- **Old saves must load.** Migrate, don't break.
- **Content:** no card pictures anywhere (code, docs, issues); nothing of Konami's; original characters only; no real
  people's names or faces in the game (deck provenance goes in `decks/history/SOURCES.md`); all-ages friendly.

## Working conventions

- Commit and push only when the owner says so. Never commit `vendor/ygopro-core`.
- Specs end with an "Owner decisions" section, recommendation first; record answers as "Decided (owner, date)".
- Report in short sentences; say what was verified and what wasn't (audio is never heard: checks run muted).
- Don't touch `build/play-alley`, `build/rework-economy` or `build/qa-night5` (read-only use of its `mods` is fine).
  The owner's real save and replays are read- or copy-only.
- App runs need absolute paths for `-mods`, `-logFile` and check folders.
