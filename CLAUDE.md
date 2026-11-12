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

## Delegating to agents

For anything bigger than a small change, the session is the architect: it plans, briefs agents, checks their work and
reports. It doesn't build the big pieces itself. A change of a few dozen lines, a lookup or a one-file fix is done
directly, with no agent.

**The pipeline** (how Import Cards was built; `docs/dev/CARD-IMPORT-SPEC-2026-11-12.md` is the worked example):

1. **Read the code, then write a spec** in `docs/dev/<NAME>-SPEC-<date>.md`: what the player gets, each part's files
   and its public interface (exact signatures), how to verify, and "Owner decisions". Fixed interfaces let the parts be
   built at the same time.
2. **Design first where it applies** (no code, the result goes in the shared notes or `docs/dev/`): Game Designer,
   Economy Designer or Narrative Designer for rules, DP and story; UI Designer for a screen's exact layout and words.
3. **Build in parallel on files that don't overlap.** Unity Architect for screens and wiring in `unity/`;
   `general-purpose` for engine and harness C# and for native interop (the installed Senior Developer agent is for web
   work, not this). Each part comes with a test that fails without it.
4. **Code Reviewer**, independent: give it the diff and the spec, never an opinion of the code. Ask for ranked
   findings with `file:line`, a concrete failure and CONFIRMED or PLAUSIBLE.
5. **One fix round**, sent back to the agents that wrote the code (they keep their context).
6. **Evidence Collector**, on the final code from a fresh build: it tests and reports, it doesn't fix. It reruns the
   harness tests and app checks, looks at every screenshot, checks that each test would fail if the feature were
   broken, and throws awkward inputs at the new code.
7. **Technical Writer** for player and developer docs, from the code and the spec.

If an agent type isn't installed on this machine (they live in `~/.claude/agents`, not in the repository), give the
same brief to `general-purpose`.

**Models.** Opus for design, building, review and the test pass; a cheaper model for docs and mechanical work. Tell
the owner which model each agent got. If the owner says usage is tight, say what you'd cut before starting.

**Every brief says:** the goal; the files the agent owns ("touch nothing else") and who is editing what beside it;
what to read first (`CLAUDE.md`, the spec, the code to copy from); the traps you already know; the exact commands to
verify with; and the report wanted (short, with real output, deviations from the spec, and what wasn't verified).
Every brief also carries: no commit, stash, reset or branch switch; LF line endings; no card pictures, ever (tests
make their own flat-colour or filler files).

**Running side by side.**

- Two agents never write the same file.
- Only one Unity can have the project open: say who may run it. A compile error in another agent's file isn't yours:
  wait and retry.
- Harness builds go in `build/bin/<name>`, check copies of the game in `build/qa-<name>`. Never build over the game
  the owner plays (`build/windows` on the PC, `build/unity` on the Mac) unless asked.
- App checks run one at a time (they fight over focus), each with a fresh `-mods` folder.

**A report isn't proof.** Before telling the owner a part is done: read the diff, rerun its test, open its
screenshots. Try the owner's real data where it's safe: read it, write only to a scratch folder.

**With the owner.** Start building without waiting on questions that have a sensible default: put those in "Owner
decisions" with what you chose, and say so. Ask only what changes what gets built. Report after each stage: what's
done, what you checked yourself, what's still running, what nobody has tested. Say plainly when you were wrong.
