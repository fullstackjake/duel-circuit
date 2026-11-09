# .ydk file import (2026-11-07)

The owner's decisions: an "Import .ydk" button opens macOS's open panel (NSOpenPanel, ~/Downloads, .ydk only); in free
play the file becomes the editor's deck; in the campaign it becomes a recipe the player keeps under "My recipes". The
clipboard Import (YDKE links, pasted .ydk text) stays as it is. No URL import: the game stays offline.

## Files

| File | What |
|---|---|
| `src/Ygo.Engine/DeckBuilding/YdkImport.cs` | The reader: `ReadFile(path, cards)` / `Read(text, name, cards)`, a result with the deck, the unknown codes and an error line. Never throws. |
| `src/Ygo.Engine/Campaign/MyRecipes.cs` | The store: one .ydk per recipe in `mods/save/recipes/`; list, load, add (free name), rename, delete. |
| `src/Ygo.Engine/Campaign/CardUnlocks.cs` | Gus's bounties pay nothing from a wishlist set by one of your recipes. |
| `unity/Assets/Scripts/YdkFilePicker.cs` | NSOpenPanel through the Objective-C runtime (as WindowSize.cs and ClickSupport.cs call AppKit). |
| `unity/Assets/Scripts/DeckEditor.cs` | The Import .ydk button (free play and campaign), `ImportYdkFile(path)`, your recipes first in the Recipe button's cycle. |
| `unity/Assets/Scripts/RecipeBookScreen.Mine.cs` | "My recipes" at the top of the Recipe Book: Import .ydk, owned/missing, flags, Build, wishlist, Rename, Delete. |
| `unity/Assets/Scripts/CampaignState.MyRecipes.cs` | The campaign side: import, rename/delete (the wishlist follows), the list recipes are judged by. |
| `unity/Assets/Scripts/DuelController.YdkCheck.cs` | `-ydkcheck`: the app check. |
| `src/Ygo.Harness/YdkImportTest.cs`, `src/Ygo.Harness/fixtures/small-goat.ydk` | `--ydk-test`: the parser and the store. |

## The save format: files next to the save

Each recipe is `mods/save/recipes/<name>.ydk`, written by `DeckCodes.ToYdk` with only the three decks (no notes, starters
or `#campaign`). The file name is the recipe's name. profile.txt doesn't change, so **old saves load unchanged** (no folder:
no recipes) and the folder is easy to inspect, back up or drop a file into. In the game a recipe's deck name is
`my:<name>` (the wishlist's `wishfor` line, the editor's recipe cycle), which can't clash with a built-in deck.
Recipes are plans, not progress: like your decks in mods/decks they stay through Start Over and New Game+.

## Behaviour

- **Free play:** the file replaces the editor's Main/Extra/Side, the deck is named from the file name, and Save asks how
  to save (`_rebuilt`), as the clipboard Import does. The old deck's story notes go; the file's never come.
- **Campaign:** the file is kept as a recipe (Recipe Book, or the campaign editor's Import .ydk, which then builds it).
  It lists with owned/missing counts; Build fills it from the collection with `AutoDeck.FromRecipe`, as built-in recipes
  build; "Add missing to wishlist" and the editor's Recipe button set the shop's wishlist. Cards over the current circuit's
  list (era-limited play; none applies in open play) are tagged BANNED / LIMIT n and listed, never removed; counts follow
  the list as built-in recipes' do. It offers no recipe pack, no "Buy what's missing", no loaner, no lesson.
- **Gus's bounties** pay cards from the wishlist. Imported recipes would let them pay any card in the shop's era, so a
  wishlist from one of your recipes pays as if no Recipe were set (a Card Token, then owed). That keeps the economy as it was.
- **The dialog** runs modally on the main thread: the game waits until Cancel/Escape/Import. It never opens in a check
  run (`DuelController.IsCheckRun`), in batch mode, off macOS, or twice; checks import from a path. In the Unity editor it
  uses `EditorUtility.OpenFilePanel`.

## Edge cases

- `#main`, `#extra`, `!side` in any case; `#created by` and every other `#`/`!` line is dropped (a name in it is never
  shown or stored). CRLF, CR, a BOM, blank lines, tabs/spaces around a code, words after a code all read.
- Codes not in the card database are left out and named by code with their copies ("Skipped 2 copies of 2 cards the game
  doesn't have: ..."). Alt-art ids that are in the database keep their id and resolve with `Banlist.BaseCode`, as decks
  from DeckCodes already do.
- Too many copies, Main under 40 or over 60, Extra or Side over 15: imported as they are; the editor's legality line and
  the Recipe Book's size line say what's wrong.
- Empty, words-only, binary (a NUL byte), over 256 KB, missing, sections without codes, only unknown codes: a clear line,
  nothing imported, the current deck untouched.
- Duplicate names get " (2)", " (3)"; renaming onto a name you have is refused (a case-only rename works). Names are
  cleaned (no control characters, slashes or colons, no leading dots, at most 60 characters).
- Deleting the recipe the wishlist is for clears the wishlist; renaming it moves the wishlist along.
- DP: none is spent or earned anywhere in this feature.

## Checks

`--ydk-test` (harness): the owner's list (copied to build/ydk-test, not committed) and its CRLF+BOM and CR copies, the
fixture, garbage, sizes, the store. `-ydkcheck <folder> -ydkfile <.ydk> -ydkfixture <.ydk>` (app): free play, the
campaign's My recipes, Build, wishlist, save/reload, rename, delete, no DP. Plus campaign, shop, layout, --campaign-test,
--deckbuilding-test and `--arbitrage --load build/qa-night5/mods`.

## As built (2026-11-07)

All green first time after two check-expectation fixes: `--ydk-test` 42 ok (the owner's list reads 40 / 14 / 15, two
codes the database lacks skipped: 82833574, 60344723), `-ydkcheck` 28 ok, campaign 179, shop 89, layout 51,
`--campaign-test` and `--deckbuilding-test` pass, `--arbitrage --load build/qa-night5/mods` 0 DP-positive,
`defaults read com.ygocampaign.game` unchanged. Run them with `build/ydk/checks.sh`. The open panel itself can't run in a
check (it refuses there by design): it needs one click by hand.

## The owner's answers (2026-11-07)

- Bounties: with an imported recipe as the goal, bounties pay a Card Token (as built), so an outside list can't aim free
  cards anywhere.
- Used market: imported recipes' missing cards are listed for sale at the usual DP price (as built).

## Review fixes (2026-11-07, second round)

The owner tried the open panel by hand (it works) and kept both defaults: imported recipes' wishlists pay bounties a
Token (then owed), and the used market's wishlist slots include their cards.

- Names are composed (Unicode FormC) when cleaned and when listed, so "Café" typed two ways can't be two names for one
  APFS file; FreeName also skips any name whose file exists; recipes are written with FileMode.CreateNew (never over a
  file) and Add takes the next name if one appears in between; a rename whose second step fails moves the file back.
- CleanName: leading dots and spaces trimmed however they mix, no half surrogate pair at the 60-character cut, no < or >
  (names show in rich-text labels). Rename refuses a blank name.
- Free play: a file named like a built-in deck ("goat-control.ydk") is named "goat-control (imported)", so Save can't
  shadow the built-in deck (CPU decks, recipes and recipe packs read those).
- A file with no Main or Extra Deck cards (Side only) is refused. The campaign editor's import stops with a line if its
  recipe isn't in the Recipe cycle instead of building another one.
- AutoDeck.FromRecipe cuts a deck past 60 / 15 / 15 and lists the cuts ("Cut 10 from the Main Deck (over 60): ..."); the
  Recipe Book's Build and the editor's Recipe show them.
- The size message gives the limit; UTF-16 files with a byte-order mark read; other files with NUL bytes say "save it as UTF-8".
- `-editor`, `-p0`, `-p1` count as check runs (DuelController.IsCheckRun): the panel never opens there and prefs stay isolated.
