using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-ydkcheck &lt;folder&gt; [-ydkfile &lt;a .ydk&gt;] [-ydkfixture &lt;a 40-card .ydk&gt;]` (absolute; use with -mods &lt;empty
    /// folder&gt;; without the two files it writes its own into the folder): .ydk import (docs/dev/YDK-IMPORT-2026-11-07.md), from paths, never through the file dialog.
    /// <list type="number">
    /// <item>The dialog refuses to open in a check run.</item>
    /// <item>Free play: -ydkfile becomes the editor's deck, named from the file, with the reader's counts, and Save asks how
    /// to save; an empty and a garbage file say why and leave the deck alone.</item>
    /// <item>The campaign: -ydkfixture goes under My recipes in the Recipe Book with owned/missing counts, the cards over
    /// a circuit's list flagged (none removed), no pack or buy button; a second import gets a number; its wishlist goes to
    /// the shop and never to Gus's bounties; the editor's Recipe button builds it; the editor's Import .ydk keeps
    /// -ydkfile as a recipe; Build fills a deck from the collection; it all survives a save and reload; rename (a taken
    /// name refused) and delete (asked first; the wishlist goes with it). No DP changes hands.</item>
    /// </list>
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>The check's 40 / 3 / 2 list (src/Ygo.Harness/fixtures/small-goat.ydk): unknown codes, an alt-art id, spaces, a comment, notes to drop.</summary>
        private const string SmallGoatYdk = "#created by A Deck Site User\n#main\n26202165\n78010363\n71413901\n33184167\n77585513\n72989439\n9596126\n31560081\n33508719\n74131780\n7572887\n47507260\n18036057\n88240808\n23205979\n46986414\n36996508\n55144522\n55144522\n55144522\n79571449\n19613556\n12580477\n45986603\n70828912\n5318639\n\n71044499\n73915051\n14087893\n72302403\n44763025\n17375316\n   44095762   \n53582587 # Torrential Tribute\n97077563\n83555666\n56120475\n29401950\n62279055\n99999999\n99999999\n31786629\n#extra\n63519819\n87751584\n80071763\n#title Not a real title\n#passes 55144522\n!side\n60082869\n51452091\n1000000001\n";

        private IEnumerator YdkCheck(string folder, string? ydkFile, string? ydkFixture)
        {
            Directory.CreateDirectory(folder);
            int bad = 0;
            void Report(string what, bool ok) { if (!ok) bad++; Debug.Log($"ydk check: {(ok ? "ok  " : "FAIL")} {what}"); }
            IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"ydk-{name}.png"));
                yield return null;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1.5f);

            // Its own .ydk files (no outside file needed): a 40-card list (the harness's fixtures/small-goat.ydk) as written,
            // and a CRLF + byte-order-mark copy under another name. -ydkfile / -ydkfixture use other files instead.
            var fixture = ydkFixture ?? Path.Combine(folder, "small-goat.ydk");
            var file = ydkFile ?? Path.Combine(folder, "ydk-decklist.ydk");
            if (ydkFixture == null) File.WriteAllText(fixture, SmallGoatYdk);
            if (ydkFile == null)
                File.WriteAllBytes(file, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes(SmallGoatYdk.Replace("\n", "\r\n"))).ToArray());
            Report($"the files are there ({file}, {fixture})", Path.IsPathRooted(file) && File.Exists(file) && Path.IsPathRooted(fixture) && File.Exists(fixture));

            // ---- The dialog never opens in a check run ----
            var picked = YdkFilePicker.Pick(out var refused);
            Report($"the file dialog refuses to open in a check run (\"{refused}\")", picked == null && refused != null && refused.Contains("automated checks"));

            // ---- Free play: the editor's deck ----
            _editor.Open(null);
            yield return new WaitForSecondsRealtime(0.5f);
            var expected = YdkImport.ReadFile(file, _cardPack!);
            bool imported = _editor.ImportYdkFile(file);
            var (deck, rebuilt, status) = _editor.DebugDeck;
            Debug.Log($"ydk check: free play status: {status}");
            Report($"free play: the file is the editor's deck ({deck.Main.Count} / {deck.Extra.Count} / {deck.Side.Count}, as read: "
                   + $"{expected.Deck?.Main.Count} / {expected.Deck?.Extra.Count} / {expected.Deck?.Side.Count})",
                imported && expected.Deck != null && deck.Main.SequenceEqual(expected.Deck.Main) && deck.Extra.SequenceEqual(expected.Deck.Extra) && deck.Side.SequenceEqual(expected.Deck.Side));
            Report($"named from the file ({deck.Name}), no notes or starters from it, and Save asks how to save", deck.Name == YdkImport.NameFromFile(file) && rebuilt && deck.Starters.Count == 0 && deck.Note("title").Length == 0);
            Report("the status says what was imported and what was skipped",
                status.StartsWith($"Imported {deck.Main.Count} / {deck.Extra.Count} / {deck.Side.Count}") && (expected.Skipped == 0 || status.Contains($"Skipped {expected.Skipped}")));
            Report("no name from its \"#created by\" line is shown", !File.ReadAllLines(file).Where(l => l.StartsWith("#created by ")).Select(l => l.Substring(12).Trim()).Any(n => n.Length > 0 && status.Contains(n)));
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("free-import");
            var empty = Path.Combine(folder, "empty.ydk");
            var garbage = Path.Combine(folder, "garbage.ydk");
            File.WriteAllText(empty, "");
            File.WriteAllText(garbage, "<html>not a deck</html>\n12345\n");
            bool emptyIn = _editor.ImportYdkFile(empty);
            var emptyStatus = _editor.DebugDeck.status;
            bool garbageIn = _editor.ImportYdkFile(garbage);
            var after = _editor.DebugDeck;
            Report($"an empty file and a garbage one say why (\"{emptyStatus}\" / \"{after.status}\") and leave the deck as it was",
                !emptyIn && !garbageIn && emptyStatus.Contains("empty") && after.status.Contains("isn't a .ydk") && after.deck.Main.SequenceEqual(deck.Main));
            // A file named like a built-in deck gets a free name, so Save can't shadow the built-in one.
            var builtIn = _library.Names.FirstOrDefault(n => !_library.IsOwn(n) && n.All(ch => char.IsLetterOrDigit(ch) || ch == '-')) ?? "goat-control";
            var clash = Path.Combine(folder, builtIn + ".ydk");
            File.Copy(fixture, clash, overwrite: true);
            _editor.ImportYdkFile(clash);
            var clashName = _editor.DebugDeck.deck.Name;
            Report($"a file named like the built-in deck {builtIn} is named {clashName}, so saving it can't replace the built-in", clashName == builtIn + " (imported)" && !_library.Contains(clashName));
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("free-garbage");
            _editor.Close();
            yield return new WaitForSecondsRealtime(0.3f);

            // ---- The campaign: My recipes ----
            _campaign.Start(CampaignState.Starters[0]);
            var profile = _campaign.Profile!;
            int dp = profile.Dp, cards = profile.Collection.Total;
            // Judge recipes by the first circuit with a list (the Beginner ones play none), so the flags show.
            int listed = Enumerable.Range(0, _campaign.Circuits.All.Count).FirstOrDefault(i => _campaign.CircuitList(i) != null);
            _campaign.DebugRecipeCircuit = listed;
            var list = _campaign.MyRecipeList;
            Report($"an old save has no recipes folder ({_campaign.MyRecipes.Folder})", !Directory.Exists(_campaign.MyRecipes.Folder));
            _recipeBook.Open();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("book-empty");
            var name = _recipeBook.ImportYdk(fixture);
            yield return new WaitForSecondsRealtime(0.5f);
            Debug.Log($"ydk check: book import status: {_recipeBook.DebugStatus}");
            yield return Shot("book-import");
            Report($"the fixture is under My recipes as {name}", name == YdkImport.NameFromFile(fixture) && _recipeBook.DebugMine.Contains(name!));
            Report("the book's status says what was added and skipped", _recipeBook.DebugStatus.Contains($"Added \"{name}\" to My recipes") && _recipeBook.DebugStatus.Contains("Skipped"));
            var recipe = _campaign.MyRecipes.Load(name!)!;
            var wants = AutoDeck.Wants(recipe, list, _cardPack!);
            int wanted = wants.Sum(w => w.Value), owned = wants.Sum(w => Mathf.Min(w.Value, profile.Collection.Count(w.Key))), missing = wants.Count(w => profile.Collection.Count(w.Key) < w.Value);
            var detail = _recipeBook.DebugMineDetail();
            Report($"it shows owned / wanted ({detail.owned} of {detail.wanted}, {detail.missing} cards missing) under {list.Name}",
                detail.owned == owned && detail.wanted == wanted && detail.missing == missing && wanted > 0);
            var over = _campaign.OverList(recipe, list);
            Report($"cards over {list.Name} are flagged, not removed ({string.Join(", ", over.Select(o => $"{_cardPack!.Get(o.code)?.Name} x{o.copies}/{o.allowed}"))}; the recipe keeps {recipe.Main.Count(c => c == 55144522)} Pot of Greed)",
                detail.flagged > 0 && detail.flagged == over.Count && recipe.Main.Count(c => c == 55144522) == 3 && detail.texts.Any(t => t.Contains("Over it:")) && detail.texts.Any(t => t == "BANNED" || t.StartsWith("LIMIT")));
            var selling = detail.buttons.Where(t => t.Contains("Recipe pack") || t.Contains("Buy") || t.Contains("DP") || t.Contains("loaner") || t.Contains("Watch")).ToList();
            Report($"no recipe pack, buy, loaner or lesson button on it (its buttons: {string.Join(", ", detail.buttons)})",
                selling.Count == 0 && detail.buttons.Contains("Build this deck") && detail.buttons.Contains("Rename") && detail.buttons.Contains("Delete"));
            var second = _recipeBook.ImportYdk(fixture);
            Report($"importing it again gets a numbered name ({second})", second == $"{name} (2)");
            _recipeBook.DebugSelectMine(name!);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("book-mine");

            // Its wishlist: the shop's, never Gus's bounties.
            _recipeBook.DebugWishMine();
            Report($"Add missing to wishlist: {profile.Wanted().Count} cards wanted for {profile.WishlistFor} (the recipe misses {missing})",
                profile.WishlistFor == MyRecipes.Prefix + name && profile.Wanted().Count == missing && missing > 0);
            Report($"Gus's bounties don't pay from it (\"{_campaign.Unlocks.BountyPays(profile)}\")", _campaign.Unlocks.BountyCard(profile) == 0 && _campaign.Unlocks.BountyPays(profile).Contains("your own recipes"));
            Report($"the shop names it by its own name ({_campaign.DeckTitle(profile.WishlistFor)})", _campaign.DeckTitle(profile.WishlistFor) == name);
            _recipeBook.Close();
            _campaignScreen.Close(); // the book's Close opens the campaign screen, which would cover the editor
            yield return new WaitForSecondsRealtime(0.3f);

            // The editor's Recipe button: your recipes first.
            _editor.Open(profile.Deck, EditorCollection(profile.Deck), _campaign.ListFor(listed)?.Name ?? _campaign.CircuitList(listed)?.Name);
            yield return new WaitForSecondsRealtime(0.5f);
            int problems = _editor.DebugRecipe(out var recipeStatus);
            Debug.Log($"ydk check: Recipe: {recipeStatus}");
            Report($"the editor's Recipe button builds your recipe first ({problems} problems left: \"{recipeStatus}\")",
                recipeStatus.Contains("(your recipe)") && recipeStatus.Contains("% of it"));
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("editor-recipe");
            // The campaign editor's Import .ydk keeps the file as a recipe and builds it.
            bool kept = _editor.ImportYdkFile(file);
            var keptStatus = _editor.DebugDeck.status;
            Debug.Log($"ydk check: campaign editor import: {keptStatus}");
            var fileName = YdkImport.NameFromFile(file);
            Report($"the campaign editor's Import .ydk keeps {fileName} under My recipes and builds it (\"{keptStatus}\")",
                kept && _campaign.MyRecipes.Names.Contains(fileName) && keptStatus.Contains($"Added \"{fileName}\"") && keptStatus.Contains("(your recipe)"));
            Report("and the deck it built is yours to play (every card owned)", CollectionRules.Validate(_editor.DebugDeck.deck, profile.Collection, _cardPack!).Count == 0);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("editor-import");
            _editor.Close();
            yield return new WaitForSecondsRealtime(0.3f);

            // Build from the book: a campaign deck from the collection.
            _recipeBook.Open();
            _recipeBook.DebugSelectMine(name!);
            yield return new WaitForSecondsRealtime(0.3f);
            _recipeBook.DebugBuildMine();
            yield return new WaitForSecondsRealtime(0.6f);
            var built = _library.Contains(profile.Deck) ? _library.Load(profile.Deck) : null;
            Report($"Build makes {profile.Deck} from your collection ({built?.Main.Count} / {built?.Extra.Count}, {(built == null ? -1 : CollectionRules.Validate(built, profile.Collection, _cardPack!).Count)} cards you don't own)",
                built != null && profile.Deck == $"{name} (recipe)" && built.Campaign && built.Main.Count > 0 && CollectionRules.Validate(built, profile.Collection, _cardPack!).Count == 0
                && built.Note("recipe").Length == 0);
            yield return Shot("built");
            _editor.Close();
            yield return new WaitForSecondsRealtime(0.3f);
            // (The editor's Import .ydk set the wishlist to the file's recipe, as Recipe does: set it back to the fixture's.)
            Report($"the editor's import set the wishlist to its recipe ({profile.WishlistFor})", profile.WishlistFor == MyRecipes.Prefix + fileName);
            _recipeBook.Open();
            _recipeBook.DebugSelectMine(name!);
            _recipeBook.DebugWishMine();
            _recipeBook.Close();
            _campaignScreen.Close();

            // A save and reload.
            _campaign.Save();
            var before = _campaign.MyRecipes.Names;
            _campaign.DebugReload();
            profile = _campaign.Profile!;
            Report($"the recipes survive a save and reload ({string.Join(", ", _campaign.MyRecipes.Names)}), the wishlist too ({profile.WishlistFor})",
                _campaign.MyRecipes.Names.SequenceEqual(before) && before.Count == 3 && profile.WishlistFor == MyRecipes.Prefix + name
                && _campaign.MyRecipes.Load(name!) is Deck again && again.Main.SequenceEqual(recipe.Main));
            Report("profile.txt holds no recipe lines (they're files next to it)", !File.ReadAllText(_campaign.SavePath).Contains("#main"));

            // Rename and delete.
            _recipeBook.Open();
            _recipeBook.DebugSelectMine(name!);
            _recipeBook.DebugRenameMine(fileName);
            Report($"renaming onto a name you have is refused (\"{_recipeBook.DebugStatus}\")", _recipeBook.DebugStatus.Contains("already") && _campaign.MyRecipes.Names.Contains(name!));
            _recipeBook.DebugRenameMine("Goat Plan");
            Report($"rename: {name} is Goat Plan, and the wishlist follows ({profile.WishlistFor})",
                _campaign.MyRecipes.Names.Contains("Goat Plan") && !_campaign.MyRecipes.Names.Contains(name!) && profile.WishlistFor == MyRecipes.Prefix + "Goat Plan");
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("book-renamed");
            bool asked = _recipeBook.DebugDeleteMine();
            Report($"delete asks first, then deletes it; the wishlist goes with it ({_recipeBook.DebugStatus})",
                asked && !_campaign.MyRecipes.Names.Contains("Goat Plan") && profile.WishlistFor == "" && profile.Wishlist.Count == 0);
            Report($"no DP changed hands ({dp} -> {profile.Dp}) and no cards were added ({cards} -> {profile.Collection.Total})", profile.Dp == dp && profile.Collection.Total == cards);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("book-final");
            _recipeBook.Close();

            Debug.Log($"ydk check: done, {bad} failed");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }
    }
}
