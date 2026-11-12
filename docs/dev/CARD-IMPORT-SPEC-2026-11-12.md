# Card picture import: build spec (2026-11-12)

Builds on [CARD-IMPORT-PLAN-2026-11-11.md](CARD-IMPORT-PLAN-2026-11-11.md) (the owner's request and the agreed design).
This file fixes the interfaces, so the parts can be built at the same time on files that don't overlap. Branch
`feature/import-cards`, off `build/windows-dev`.

## What the player gets

A main-menu entry **Import Cards**, between Settings and About. Its screen has three sections:

| Section | Accepts | Goes to |
|---|---|---|
| Card Images | one `.zip`, or one or more `.jpg` / `.jpeg` / `.png` | `mods/cards/images/` |
| High Resolution Card Images | the same | `mods/cards/hd/` (new; read automatically) |
| Card Back | one `.jpg` / `.jpeg` / `.png` | `mods/cards/back.png` or `back.jpg` |

Each section shows how many pictures it holds and has two buttons: **Import...** (one file dialog that takes a zip
or images) and **Open folder**. After picking: a scan, then **one** question if pictures already exist ("1,204 of
these pictures already exist": Replace them / Keep mine / Cancel), a progress bar with Cancel, then a result line. Pictures
show without a restart.

## Part A: the importer (engine, pure C#)

`src/Ygo.Engine/Data/PictureImport.cs`, namespace `Ygo.Engine.Data`. No Unity types, no statics that hold state, safe
to call from a background thread. C# 9, netstandard2.1 (`System.IO.Compression` is in the standard).

```csharp
public enum PictureKind { Standard, Hd, Back }

public sealed class PictureImportPlan
{
    public PictureKind Kind { get; }
    public string TargetFolder { get; }          // images/ or hd/; for Back, the cards/ folder
    public int Total { get; }                    // pictures that would be written
    public int New { get; }                      // no picture of that card in the target yet
    public int Existing { get; }                 // a picture of that card is already there (any extension)
    public int Skipped { get; }                  // not imported; see SkippedExamples
    public IReadOnlyList<string> SkippedExamples { get; }   // up to 5, "name: why"
    public string? Problem { get; }              // the whole import can't run (unreadable zip, no pictures...): show it, stop
}

public sealed class PictureImportResult
{
    public int Added, Replaced, Kept, Failed;
    public bool Cancelled;
    public IReadOnlyList<string> FailedExamples { get; }    // up to 5, "name: why"
    public IReadOnlyList<string> WrittenFiles { get; }      // full paths written (added or replaced)
    public IReadOnlyList<string> RemovedFiles { get; }      // full paths deleted (the other extension of a replaced card)
}

public static class PictureImport
{
    // paths: one .zip, or image files, or a mix (each zip is read; loose images are taken as they are).
    public static PictureImportPlan Scan(PictureKind kind, IReadOnlyList<string> paths, string targetFolder);
    public static PictureImportResult Run(PictureImportPlan plan, bool replaceExisting,
        IProgress<(int done, int total)>? progress = null, CancellationToken cancel = default);
}
```

Rules:

1. **Names.** A card picture is `<digits>.<jpg|jpeg|png>` (1 to 10 digits, extension in any case; written with a
   lower-case extension). Anything else is skipped with a reason. In a zip, only the file name counts: folders are
   flattened (`pics/89631139.jpg` -> `89631139.jpg`). Skip directories, `__MACOSX/`, `._*`, `.DS_Store`, `Thumbs.db`
   silently (not counted as skipped).
2. **Content.** A file must start with the PNG or JPEG magic bytes; otherwise it's skipped ("not a picture").
3. **Same card twice in one import:** the first wins; the rest are skipped ("duplicate").
4. **Existing.** A card "already exists" when the target has `<digits>` under any of the three extensions. With
   `replaceExisting`, the new file is written and a file of the same card under another extension is deleted (so a
   card never has two). Without it, the card is kept as it is and counted in `Kept`.
5. **Safety.** Never write outside `TargetFolder`: the target path is always `Path.Combine(TargetFolder, <validated
   name>)`; an entry name is never used as a path. A zip entry over 64 MB uncompressed is skipped ("too big"); stop
   reading an entry at that size whatever its header says (zip bombs).
6. **Atomic.** Write each picture to `<name>.importing` in the target folder, then move it into place. A cancelled or
   failed import leaves no half-written picture and no `.importing` file (clean up in `finally`; `Scan` also ignores
   and `Run` removes stale `*.importing`).
7. **One failure doesn't stop the rest** (count it in `Failed`); `cancel` stops between pictures.
8. **Back.** `Scan(Back, ...)` takes exactly one image (a zip or several files is a `Problem`). `Existing` is 1 when
   `back.png` or `back.jpg` is there. `Run` writes `back.png` or `back.jpg` (by the source's real type) and deletes
   the other. Rule 2 applies; there's no name rule.
9. `Scan` reads zip directories and the first bytes only; it must stay fast on a 2 GB, 15,000-entry zip. Progress is
   reported at most about 20 times a second.

Harness test: `src/Ygo.Harness/ImportTest.cs`, flag `--import-test` in `Program.cs` (with the comment every flag has).
It builds its own tiny files in a temp folder (PNG/JPEG headers plus filler; **never card pictures**) and checks every
rule above, both answers to the overwrite question, cancel, and a zip-slip name (`../../evil.png`). Output in the
style of the other tests (`  ok   ...` / `  FAIL ...`, a summary line, exit code 1 on failure).

## Part B: the file dialog (Unity)

New `unity/Assets/Scripts/FilePicker.cs`; `YdkFilePicker.Pick` keeps its signature and calls it (so **Import .ydk
starts working on Windows**).

```csharp
public static class FilePicker
{
    public sealed class Request
    {
        public string Title = "", Message = "", Prompt = "Open";
        public string FilterName = "Files";               // "Card pictures and zips"
        public string[] Extensions = Array.Empty<string>(); // without dots: "jpg", "zip"
        public bool Multiple;
        public string? StartFolder;                        // null: Downloads, else home
    }
    /// The chosen files (full paths); null when cancelled or when the dialog can't open (refused says why; null for a plain cancel).
    public static string[]? Pick(Request request, out string? refused);
    public static string LastRefusal { get; }
}
```

- The same refusals as today (already open, a check run, batch mode), and "no file dialog on this platform" off
  Windows and macOS. The dialog is modal on the main thread, as now.
- **Windows:** `comdlg32.dll` `GetOpenFileNameW`, owner `GetActiveWindow()`, flags `OFN_EXPLORER | OFN_FILEMUSTEXIST
  | OFN_PATHMUSTEXIST | OFN_HIDEREADONLY | OFN_NOCHANGEDIR` (the game's working folder must not move) plus
  `OFN_ALLOWMULTISELECT` when `Multiple`. A 64K-character buffer; multi-select comes back as
  `dir\0file1\0file2\0\0` (one file: the full path alone). `CommDlgExtendedError() == FNERR_BUFFERTOOSMALL` is a
  refusal: "too many files at once: pick fewer, or zip them". The buffer parser is its own pure function, so it can
  be tested without a dialog.
- **macOS:** the existing NSOpenPanel code, with `setAllowsMultipleSelection:`, every extension as an allowed type
  (UTType when there, else the old list), and `URLs` read as an array. Can't be run on the Windows PC: write it with
  care, the owner tests it on the Mac.
- **Editor:** `EditorUtility.OpenFilePanelWithFilters` (one file; fine for development).

## Part C: CardArt (Unity)

`unity/Assets/Scripts/CardArt.cs`:

- `ImagesFolder` (`mods/cards/images`), `HdFolder` (`mods/cards/hd`), `CardsFolder` (`mods/cards`). The constructor
  creates `HdFolder` and adds it to the folders and the HD list, unless an `hd_pics:` line already names it (the
  existing `Normal()` comparison; the owner's `settings.txt` does exactly that).
- `public void Refresh()`: forgets file lookups, the installed list, art keys, loaded textures and the card back,
  rescans styles, and raises `Changed(0)`. (Corrected after review, 2026-11-12: the first draft said the disk cache is
  keyed by path only and asked for `ArtLoader.Uncache` on every changed file. `ArtLoader.ReadCache` already rejects an
  entry whose source length or write time changed, so a replaced file can't show stale; the uncache loop was removed.)
- `public int CountIn(string folder)`: card pictures in one folder (for the screen's counts).

## Part D: the screen, the menu and the check (Unity)

- `unity/Assets/Scripts/ImportScreen.cs`: built like `AboutScreen` (`Init`, `Open`, `Close`, `Back`, `Closed`,
  `ArrowNav`, `UiTheme.Current`, a `_layout` list, `Debug*` members for checks). Scan and Run go on a background
  thread (`Task.Run`); the screen polls in `Update` and never touches Unity objects off the main thread. While an
  import runs, Back and the other buttons are off; Cancel is on. Esc/B = Back (or Cancel while importing).
  For checks: `DebugImport(PictureKind kind, string[] paths, bool? replace)` runs the same flow without the dialog.
- `MenuScreens.cs`: `Choice.ImportCards` **appended to the end of the enum**; label "Import Cards" between Settings
  and About; subtitle "Add your own card pictures: a zip or image files."; shut over a campaign duel, like Deck
  Editor ("Finish or concede your duel first."). Nine options must clear the notice at the bottom: layout from the UI
  spec in the shared notes (`import-screen-ui-spec.md`).
- `DuelController.cs`: create the screen, open it from the menu, reopen the menu on `Closed`.
- `DuelController.ImportCheck.cs`: `-importcheck <folder>` (add it to the `checks` array). It makes its own test
  pictures (flat-colour `Texture2D.EncodeToPNG`, **never card pictures**), named by real passcodes from the card data,
  plus a zip built with `ZipArchive`; then: menu shows Import Cards between Settings and About; the screen opens;
  import a zip into Card Images (count rises, a card's art is no longer null); a loose image; a file with a bad name
  (skipped, reported); the same zip again with Keep mine and with Replace them (the replaced card shows the **new**
  picture); HD; a card back (`CardArt.Back` changes); Cancel; Back returns to the menu.
  Screenshots like the other checks.

## Docs

`docs/CARD-PICTURES.md` (a new first section: import from the game; the `hd` folder), `README.md` (the "only on a Mac"
known issue goes; the feature list), `docs/PLAYING.md` where it lists menus, `docs/DEVELOPING.md` (the `--import-test`
flag and the `-importcheck` row), `CLAUDE.md` only if a rule changes.

## Verification

Harness: `--import-test`, plus `--campaign-test` and `--deckbuilding-test` (nothing else should move). Unity on
Windows (`G:\Unity\hub\editor\6000.3.27f1\Editor\Unity.exe`): build the check copy (`-buildout "build/qa-win/Duel
Circuit.exe"`), then `-importcheck`, `-menucheck` (unmuted), `-layoutcheck`, `-aboutcheck`, `-ydkcheck`, `-boardcheck`,
`-campaigncheck`. Windows paths, a fresh `-mods` each, no `-batchmode`/`-nographics` (docs/BUILDING.md). Not testable
here: the macOS dialog, and the real Windows dialog (it needs a person to click).

## Owner decisions

1. **Import Cards is shut during a campaign duel.** Recommended: yes (pictures reload under a live board otherwise).
2. **One Import button per section**, whose dialog takes a zip or images. Recommended: yes (the owner asked for
   "a zip OR images"; two buttons per section would say the same thing twice).
3. **Deleting pictures from the game.** Recommended: not in this build; **Open folder** covers it.

## After the code review (2026-11-12)

Fixed in the importer: a same-name replace keeps the old picture until the new one is in place; names are parsed to
the card number (`0089631139.jpg` lands as `89631139.jpg`; numbers the game can't look up are skipped); a replace
whose old other-extension file can't be removed is rolled back; `Scan` checks free disk space and `Run` stops when the
disk fills; examples show file names only. Fixed in the screen: the focus frame on the questions; `Refresh` no longer
walks the disk cache; the HD section says when `-hdpics off` leaves its folder unread.
