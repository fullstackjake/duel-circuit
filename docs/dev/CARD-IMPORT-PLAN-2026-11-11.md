# Card picture import: plan and handoff (2026-11-11)

Status: **planned, not started.** Owner asked for it on 2026-11-11 and agreed to the design below; work stopped for the
weekly usage limit. Pick up at "Build order".

## State of the branch when this was written

- Branch `build/windows-dev` (on top of `docs/claude-md`, which holds CLAUDE.md as d0ba0f9). **Uncommitted**, owner
  hasn't said to commit yet:
  - `.gitattributes` (LF everywhere), harness `RollForward` + `ocgcore.dll` on Windows, `ocgcore.dll.meta` enabled
    for the Windows editor, `ProjectSetup.cs` (`ConfigureWindowsPlugin`, `VerifyEngineInEditor`, Mac-only lines in
    `BuildMac` under `#if UNITY_EDITOR_OSX`), `PrintingsImport.cs` (a set filed under its cards' code when the set
    table lists another: Dark Beginning 2 is "DB" in YGOPRODeck's table, "DB2" on its cards), docs/BUILDING.md
    "Build on Windows", CLAUDE.md.
  - Verified on Windows: harness `--campaign-test`, `--deckbuilding-test`, `--texts-check`, `check_history_decks.py`
    all pass; Unity build succeeds; app checks menu 6/0, board 25/0, campaign 179/0. The Mac build isn't retested.
- Windows PC: Unity 6000.3.27f1 at `G:\Unity\hub\editor\6000.3.27f1\Editor\Unity.exe`; project at
  `E:\Projects\duel-circuit`. Check build in `build/qa-win/` (see BUILDING.md for app checks on Windows).

## The feature

A main-menu entry **Import Cards** between Settings and About. Its screen has three buttons:

| Button | Accepts | Goes to |
|---|---|---|
| Card Images | a `.zip`, or one or more `.jpg`/`.png` | `mods/cards/images/` (already read) |
| High Resolution Card Images | a `.zip`, or one or more images | **new** `mods/cards/hd/`, read automatically like an `hd_pics:` folder |
| Card Back | one image | `mods/cards/back.png` / `back.jpg` (replace the other extension's file too) |

Agreed details:

1. **One overwrite question per import**, after scanning: "N of these pictures already exist: Replace them / Keep
   mine / Cancel" (not one per file; EDOPro packs are ~13,000 files).
2. HD goes to its own folder (same file names as normal pictures); the biggest picture already wins (`CardArt`).
3. Card back: single image, preview, confirm before replacing.
4. Zips: extract on a background thread with a progress bar; flatten folders inside (EDOPro's `pics/`); take only
   files named `<digits>.jpg|.png|.jpeg` (report how many skipped); reject entries whose path escapes the target
   (zip-slip); skip `__MACOSX/` and `._*`. Then refresh pictures without a restart.
5. An **Open folder** button per target.

## Code map (read before starting)

- Menu: `unity/Assets/Scripts/MenuScreens.cs` (`Init` subtitles ~line 268, `Choice` enum, `Build`, `Open`'s mid-duel
  switch). Eight items already reach the footer: tighten spacing; run `-layoutcheck` and `-menucheck` (unmuted).
  `DuelController` handles `Chosen`; `AboutScreen.cs` is a small screen to copy the pattern from.
- Pictures: `unity/Assets/Scripts/CardArt.cs` (constructor builds `_folders`: own images, EDOPro, `hd_pics`; `Back`
  caches with `_backLoaded`; `ForgetAll`, `ScanStyles`). Needs: add `mods/cards/hd` to `_folders`/`_hd`, a public
  `Refresh()` that clears `_files`, `_keys`, caches and `_backLoaded`. `ArtLoader.cs` holds the disk cache.
- File dialog: `unity/Assets/Scripts/YdkFilePicker.cs` is **macOS only** (NSOpenPanel via objc_msgSend; the editor
  uses `EditorUtility.OpenFilePanel`). Generalize into a picker taking a title, extensions and multi-select:
  - Windows: `comdlg32.dll` `GetOpenFileNameW` with `OFN_EXPLORER | OFN_ALLOWMULTISELECT | OFN_FILEMUSTEXIST`
    (multi-select returns dir\0file1\0file2\0\0; use a large buffer).
  - Mac: `setAllowsMultipleSelection:`, allowed types jpg/png/jpeg/zip; read `URLs`. **Can't be tested on the
    PC**: owner checks it on the Mac.
  - This also makes the deck editor's Import .ydk work on Windows.
- Zip: `System.IO.Compression.ZipArchive` (check the assembly resolves in Unity; else add `csc.rsp` reference).
- Docs: `docs/CARD-PICTURES.md` (new "Import from the game" section; HD folder), `docs/PLAYING.md` if it lists menus.
- Checks: add an app check (`-importcheck <folder>`) that imports a generated zip of tiny PNGs named by passcode,
  a loose image, a non-passcode file (skipped), an overwrite (both answers) and a card back, and confirms
  `CardArt` sees them. **Never commit card pictures** (content rules): generate test images in the check.

## Build order

1. `CardArt`: hd folder + `Refresh()`.
2. Importer (pure C#, no UI): scan → conflicts → copy/extract with progress → result counts. Unit-check in the app check.
3. File picker (Windows first, then Mac).
4. Import screen + menu entry + layout.
5. App check, docs, run menu/layout/board/campaign checks on Windows.

Estimate: 2–3 hours of work, plus a round of fixes after the owner tries it.
