# Building Duel Circuit

This guide takes you from an empty folder to the game running in the Unity editor, then to a Mac app and a Windows game
you can share. You don't need to have built a game before: every step is a command to copy, with a line on what it
does and what you should see.

Building works on a **Mac with Apple silicon** (M1 or newer). The Windows game is built on the Mac too. Building on an
Intel Mac, on Windows or on Linux isn't supported yet.

- [What you need](#what-you-need)
- [Build from a fresh clone](#build-from-a-fresh-clone)
- [Play in the Unity editor](#play-in-the-unity-editor)
- [Build the Mac app](#build-the-mac-app)
- [Build the Windows game](#build-the-windows-game)
- [Share a build as a GitHub release](#share-a-build-as-a-github-release)
- [Update to the newest cards](#update-to-the-newest-cards)
- [Troubleshooting](#troubleshooting)

## What you need

| What | Why |
|---|---|
| A Mac with Apple silicon | The rules engine is built for it |
| Xcode's command line tools | The C++ compiler for the rules engine |
| [Homebrew](https://brew.sh), then meson and ninja | The rules engine's build tools |
| The [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0) | Builds and runs the harness (the console app the game's tools run from) |
| [Unity Hub](https://unity.com/download) with Unity **6000.3.27f1** | Runs and builds the game |
| Unity's **Windows Build Support (Mono)** module | Only if you build the Windows game |
| mingw-w64 | Only if you rebuild the Windows rules engine (rarely: see [step 3](#3-build-the-rules-engine)) |

Leave a little over 1 GB free for the project and its build files, plus room for Unity itself.

Check that your Mac has Apple silicon:

```bash
uname -m
```

You should see `arm64`.

Install Xcode's command line tools (a window opens; click **Install**):

```bash
xcode-select --install
```

If they're already installed, it says so. That's fine.

Install Homebrew from [brew.sh](https://brew.sh): copy the one-line command on its front page into Terminal and run
it. When it finishes, Homebrew prints **Next steps** with two commands. Run them, or `brew` won't be found.

Then install the rules engine's build tools:

```bash
brew install meson ninja
```

You should see both install, or a note that they're already installed.

Install the .NET 7 SDK from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/7.0) (pick the macOS
Arm64 installer), then check it:

```bash
dotnet --list-sdks
```

You should see a line starting with `7.0`. Other versions may be listed too.

Install Unity:

1. Download and install [Unity Hub](https://unity.com/download).
2. Open Unity Hub and sign in with a Unity account (it's free). When it asks for a license, add a free **Personal**
   license.
3. Click **Installs**, then **Install Editor**.
4. Choose **6000.3.27f1**. If it isn't listed, find it in Unity's
   [download archive](https://unity.com/releases/editor/archive) and click its **Install** button there.
5. When it asks for modules, tick **Windows Build Support (Mono)** if you'll build the Windows game. You can add it
   later from the same screen.

Only if you'll rebuild the Windows rules engine:

```bash
brew install mingw-w64
```

## Build from a fresh clone

Run these from the folder you want the project in. Each step finishes before the next starts.

### 1. Get the code

Downloads the project and moves you into its folder:

```bash
git clone https://github.com/fullstackjake/duel-circuit.git && cd duel-circuit
```

You should see `Cloning into 'duel-circuit'...` and a few progress lines.

### 2. Get the rules engine and the card data

Downloads the rules engine, the card scripts, the card databases and the ban lists into `vendor/`:

```bash
git submodule update --init
```

You should see four `Submodule path 'vendor/...': checked out '...'` lines. The card scripts are the biggest download,
so this can take a minute.

### 3. Build the rules engine

Applies our patches to the rules engine, builds it, and copies it into the Unity project:

```bash
tools/build_core.sh
```

You should see `applied:` (or `already applied:`) for each of the three patches, the compiler's progress, and at the
end `copied libocgcore.dylib to unity/Assets/Plugins/macOS`.

The Windows rules engine, `unity/Assets/Plugins/Windows/x86_64/ocgcore.dll`, is already in the repository. You only
rebuild it after changing the engine or its patches, with `tools/build_core.sh --windows` (needs mingw-w64).

### 4. Build the harness

Builds the harness, the console app the game's tools and tests run from:

```bash
dotnet build src/Ygo.Harness -c Release
```

You should see `Build succeeded.` with `0 Error(s)`. The harness lands in `build/bin/Ygo.Harness/Release/net7.0/`.

### 5. Export the card data

Writes `cards.bin`, the card data the game reads, from the card databases you downloaded in step 2:

```bash
dotnet build/bin/Ygo.Harness/Release/net7.0/Ygo.Harness.dll --export-cards unity/Assets/StreamingAssets/cards.bin
```

You should see `Loaded ... cards` and then `Wrote card pack to unity/Assets/StreamingAssets/cards.bin` (about 6.5 MB).

### 6. Download the card-set data

Downloads YGOPRODeck's card-set data (every printing, its rarity and its set's date) into `data/ygoprodeck/`:

```bash
mkdir -p data/ygoprodeck && curl -s --compressed -o data/ygoprodeck/cardinfo.json https://db.ygoprodeck.com/api/v7/cardinfo.php && curl -s --compressed -o data/ygoprodeck/cardsets.json https://db.ygoprodeck.com/api/v7/cardsets.php
```

It prints nothing. Afterwards `data/ygoprodeck/` holds `cardinfo.json` and `cardsets.json` (about 20 MB together).

### 7. Export the printings

Writes `printings.bin` from that data. The card shop, packs, rarities, foils and card versions need it:

```bash
dotnet build/bin/Ygo.Harness/Release/net7.0/Ygo.Harness.dll --export-printings data/ygoprodeck/cardinfo.json unity/Assets/StreamingAssets/printings.bin
```

You should see how many sets and cards were read, a line ending in `printings.bin (... KB)`, and a count per rarity.

### 8. Check that the engine works

Plays three quick duels between two CPU duelists:

```bash
dotnet build/bin/Ygo.Harness/Release/net7.0/Ygo.Harness.dll --games 3
```

You should see `3 games (smart vs smart)`, the wins for each deck, and `script errors: 0   crashed games: 0`.

### What each step made

| File or folder | Made by | In git? |
|---|---|---|
| `vendor/` (rules engine, card scripts, card databases, ban lists) | Step 2 | Pinned as submodules |
| `unity/Assets/Plugins/macOS/libocgcore.dylib` | Step 3 | Yes (rebuilt here) |
| `build/bin/Ygo.Harness/` | Step 4 | No |
| `unity/Assets/StreamingAssets/cards.bin` | Step 5 | No: export it again after new cards |
| `data/ygoprodeck/` | Step 6 | No |
| `unity/Assets/StreamingAssets/printings.bin` | Step 7 | No: without it every card is drawn plain and the shop won't open |

## Play in the Unity editor

1. In Unity Hub, click **Add → Add project from disk**, then pick the `unity` folder inside the project. Click the
   project in the list to open it. The first time, Unity imports the project, which takes a while.
2. In the **Project** panel, open `Assets/Scenes/Main.unity`.
3. Press **Play** (the triangle at the top). The game starts on its title screen.

In the editor the game uses the repository's own `mods/` folder for your save, decks and settings (it's created on the
first run and isn't committed). The editor also runs in developer mode: **F12** shows the developer panel (matchup
picker, decision list, test buttons).

If you rebuild the rules engine while the editor is open, restart the editor: Unity keeps the old library loaded.

## Build the Mac app

From the editor: the menu **YgoCampaign → Build macOS App** (the menu still uses the project's old name, YgoCampaign).

From the terminal (close the Unity editor first), in the project folder:

```bash
/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath unity -buildTarget OSXUniversal -executeMethod ProjectSetup.BuildMac -logFile build/unity/build.log
```

It prints nothing while it works (a few minutes). When it's done, `build/unity/build.log` has a line
`Built .../build/unity/Duel Circuit.app` near its end, and the app is in `build/unity/`.

Open it:

```bash
open "build/unity/Duel Circuit.app"
```

The game's title screen opens. An app you built yourself opens without macOS's "unidentified developer" warning.

The build copies the card scripts, decks, characters, campaign files, ban lists and art into the app. It stops with
`Build stopped: Assets/StreamingAssets/cards.bin is missing` (or `printings.bin`) if you skipped steps 5 or 7.

## Build the Windows game

You need Unity's **Windows Build Support (Mono)** module ([What you need](#what-you-need)).

From the editor: the menu **YgoCampaign → Build Windows Game**.

From the terminal (Unity closed):

```bash
/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath unity -buildTarget Win64 -executeMethod ProjectSetup.BuildWindows -logFile build/windows/build.log
```

When it's done, `build/windows/` holds `Duel Circuit.exe`, its `Duel Circuit_Data` folder and Unity's other files.
Copy the whole folder to a PC (zip it: see the next section).

Building for Windows switches the Unity project to Windows. The Mac build command above passes
`-buildTarget OSXUniversal`, which switches it back, so always keep that part.

## Share a build as a GitHub release

A release is made from one commit, so its source is easy to find (the license asks for that).

1. Change the version number in `unity/Assets/Editor/ProjectSetup.cs` (`PlayerSettings.bundleVersion = "0.1.1";`).
2. Commit everything. The release script refuses a working tree with changes.
3. Build the Windows game, then the Mac app (the commands above). Each build writes a `licenses` folder with a
   `SOURCE.txt` that names the commit.
4. Pack the release:

```bash
tools/make_release.sh
```

It checks that both builds come from the current commit, then writes three files to `build/release/`:

| File | What it is |
|---|---|
| `DuelCircuit-<version>-win64.zip` | The Windows game in a `Duel Circuit` folder, with a `Licenses` folder |
| `DuelCircuit-<version>-mac.zip` | The Mac app in a `Duel Circuit` folder, with a `Licenses` folder |
| `ygopro-core-<commit>-patched.tar.gz` | The rules engine's full source with our patches applied, plus the patches and build scripts |

5. Tag the commit and publish the release with the [GitHub CLI](https://cli.github.com) (replace the version, title
   and notes):

```bash
gh release create v0.1.1 build/release/* --target main --title "Duel Circuit 0.1.1" --prerelease --notes "What changed in this version."
```

It prints the release's web address. Always attach the `ygopro-core-…-patched.tar.gz` file: it's the source of the
rules engine inside the game. `--prerelease` marks a build nobody has tested on a real machine yet; leave it out for a
tested one. The builds aren't signed, so players see Gatekeeper or SmartScreen warnings: the
[README](../README.md#install-and-play) tells them how to open the game anyway, and
[Troubleshooting](#troubleshooting) has a Terminal line for the Mac.

## Update to the newest cards

New cards arrive in Project Ignis's card scripts and card databases. Move both to their newest version:

```bash
git submodule update --remote vendor/CardScripts vendor/BabelCDB
```

You should see a `Submodule path 'vendor/...': checked out '...'` line for each one that changed. `git status` then
shows them as modified: that's the new version, and only needs committing if you mean to move the project to it.

For the newest ban lists too:

```bash
git submodule update --remote vendor/LFLists
```

Then export the card data again ([step 5](#5-export-the-card-data)), download the card-set data again
([step 6](#6-download-the-card-set-data)), export the printings again ([step 7](#7-export-the-printings)), and rebuild
the app. Rebuild the rules engine only when `vendor/ygopro-core` itself changes.

After a card update, [DEVELOPING.md](DEVELOPING.md#historic-decks) has the checks for the historic decks (the newest
cards sometimes change ids).

## Troubleshooting

**`meson: command not found` or `ninja: command not found`** when building the rules engine: install them with
`brew install meson ninja`, then run `tools/build_core.sh` again.

**`DllNotFoundException: Unable to load shared library 'ocgcore'`** from the harness: the harness was built before the
rules engine, so it has no copy of it. Run `tools/build_core.sh`, then `dotnet build src/Ygo.Harness -c Release` again.

**`You must install or update .NET to run this application`** (it names version 7.0): the harness needs the .NET 7
runtime, which comes with the .NET 7 SDK. A newer .NET alone isn't enough. Install the .NET 7 SDK and try again.

**`Build stopped: Assets/StreamingAssets/cards.bin is missing`** (or `printings.bin`) in `build/unity/build.log`: run
[step 5](#5-export-the-card-data) or [step 7](#7-export-the-printings), then build again.

**The terminal build ends at once and the log says another Unity instance has the project open**: close the Unity
editor and run the command again.

**The Windows build fails with a message about the Windows build target or module**: add **Windows Build Support
(Mono)** to Unity 6000.3.27f1 in Unity Hub (**Installs**, the gear next to the version, **Add modules**).

**Every card is drawn plain and the card shop won't open**: `printings.bin` is missing. Run
[step 6](#6-download-the-card-set-data) and [step 7](#7-export-the-printings).

**A duel behaves as if the rules engine were old after you rebuilt it**: restart the Unity editor (it keeps the old
library loaded until it restarts).

**macOS won't open a Duel Circuit app you downloaded** (it says the app "can't be opened" or "is damaged"): macOS marks
every downloaded file, and the app isn't signed. This line removes the mark from the app in your Applications folder.
It prints nothing, and the app then opens:

```bash
xattr -dr com.apple.quarantine "/Applications/Duel Circuit.app"
```

Still stuck? Open an issue with the command you ran and its last 20 lines of output. [CONTRIBUTING.md](../CONTRIBUTING.md)
says how.
