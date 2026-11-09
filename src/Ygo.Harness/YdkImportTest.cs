using System.Text;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--ydk-test [folder]`: the .ydk import's reader and the campaign's "My recipes" store (docs/dev/YDK-IMPORT-2026-11-07.md).
/// Reads the owner's example list from <c>folder/ydk-decklist.ydk</c> (default build/ydk-test; copied there by hand, never
/// committed: skipped with a note when it's missing) and src/Ygo.Harness/fixtures/small-goat.ydk, each as written and as
/// CRLF, CR-only and BOM variants; unknown codes, extra spaces and comments; garbage (empty, words, binary, too big,
/// missing, no codes, only unknown codes); and the recipes folder (add, duplicate names, rename, delete, an old save
/// without the folder). Writes only under the folder given.
/// </summary>
public static class YdkImportTest
{
    public static int Run(ICardDataProvider cards, string root, string? folder)
    {
        folder ??= Path.Combine(root, "build", "ydk-test");
        Directory.CreateDirectory(folder);
        int fails = 0, oks = 0;
        void Check(string what, bool ok)
        {
            if (ok) oks++; else fails++;
            Console.WriteLine($"ydk test: {(ok ? "ok" : "FAIL")}: {what}");
        }
        static string Counts(Deck? d) => d == null ? "none" : $"{d.Main.Count}/{d.Extra.Count}/{d.Side.Count}";
        static bool Same(Deck? a, Deck? b) => a != null && b != null && a.Main.SequenceEqual(b.Main) && a.Extra.SequenceEqual(b.Extra) && a.Side.SequenceEqual(b.Side);
        static string Crlf(string t) => t.Replace("\r\n", "\n").Replace("\n", "\r\n");

        // ---- The owner's example (a public tournament list) ----
        var owner = Path.Combine(folder, "ydk-decklist.ydk");
        if (!File.Exists(owner)) Console.WriteLine($"ydk test: note: {owner} isn't there, so the owner's list is skipped (copy it from ~/Downloads)");
        else
        {
            var text = File.ReadAllText(owner);
            int listed = 0; string? section = null;
            foreach (var l in text.Split('\n').Select(x => x.Trim()))
            {
                if (l == "#main" || l == "#extra" || l == "!side") { section = l; continue; }
                if (section != null && uint.TryParse(l, out _)) listed++;
            }
            var read = YdkImport.ReadFile(owner, cards);
            Console.WriteLine($"ydk test: the owner's list: {read.Summary("ydk-decklist.ydk")}");
            Check($"the owner's list imports ({Counts(read.Deck)}, {read.Skipped} skipped of {listed} codes)", read.Ok && read.Deck!.Main.Count + read.Deck.Extra.Count + read.Deck.Side.Count + read.Skipped == listed);
            Check("it's named from its file (ydk-decklist)", read.Deck?.Name == "ydk-decklist");
            Check("its \"#created by\" line leaves no note, starter or campaign flag", read.Deck is { } d0 && d0.Notes.Count == 0 && d0.Starters.Count == 0 && !d0.Campaign);
            Check("every code it kept is in the card database", read.Deck is { } d1 && d1.Main.Concat(d1.Extra).Concat(d1.Side).All(c => cards.Get(c) != null));
            var bom = Path.Combine(folder, "ydk-decklist-crlf-bom.ydk");
            File.WriteAllBytes(bom, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Crlf(text))).ToArray());
            var bomRead = YdkImport.ReadFile(bom, cards);
            Check($"a CRLF + BOM copy reads the same ({Counts(bomRead.Deck)})", Same(read.Deck, bomRead.Deck) && bomRead.Skipped == read.Skipped);
            var cr = YdkImport.Read(text.Replace("\r\n", "\n").Replace('\n', '\r'), "cr", cards);
            Check("a CR-only copy reads the same", Same(read.Deck, cr.Deck));
        }

        // ---- The fixture: unknown codes, an alt-art id, spaces, a comment after a code, notes it must drop ----
        var fixture = Path.Combine(root, "src", "Ygo.Harness", "fixtures", "small-goat.ydk");
        var fx = YdkImport.ReadFile(fixture, cards);
        Console.WriteLine($"ydk test: the fixture: {fx.Summary("small-goat.ydk")}");
        Check($"the fixture reads 40 / 3 / 2 ({Counts(fx.Deck)})", fx.Deck is { Main.Count: 40, Extra.Count: 3, Side.Count: 2 });
        Check($"its unknown codes are listed by code with their copies ({string.Join(", ", fx.Unknown.Select(u => $"{u.code}x{u.copies}"))})",
            fx.Unknown.Count == 2 && fx.Unknown[0] == (99999999u, 2) && fx.Unknown[1] == (1000000001u, 1) && fx.Skipped == 3);
        Check("the summary says how many were skipped and names them", fx.Summary("x.ydk").Contains("Skipped 3 copies of 2 cards") && fx.Summary("x.ydk").Contains("99999999 x2"));
        Check("\"   44095762   \" and \"53582587 # Torrential Tribute\" both read", fx.Deck is { } f1 && f1.Main.Contains(44095762) && f1.Main.Contains(53582587));
        Check("the alt-art Dark Magician keeps its id and resolves to the original (as DeckCodes and the ban lists do)",
            fx.Deck is { } f2 && f2.Main.Contains(36996508) && Banlist.BaseCode(36996508, cards) == 46986414);
        Check("its #title, #passes and #created by lines are dropped", fx.Deck is { } f3 && f3.Notes.Count == 0 && !f3.Campaign && f3.Starters.Count == 0);
        Check("three Pot of Greed stay three (the editor's legality line says what's wrong)", fx.Deck is { } f4 && f4.Main.Count(c => c == 55144522) == 3);
        var fxText = File.ReadAllText(fixture);
        Check("its CRLF + BOM copy reads the same", Same(fx.Deck, YdkImport.Read("\uFEFF" + Crlf(fxText), "x", cards).Deck));
        foreach (var (label, bom, enc) in new[] { ("UTF-16 LE", new byte[] { 0xFF, 0xFE }, Encoding.Unicode), ("UTF-16 BE", new byte[] { 0xFE, 0xFF }, Encoding.BigEndianUnicode) })
        {
            var path16 = Path.Combine(folder, $"fixture-{label.Replace(' ', '-')}.ydk");
            File.WriteAllBytes(path16, bom.Concat(enc.GetBytes(Crlf(fxText))).ToArray());
            Check($"a {label} copy with its byte-order mark reads the same", Same(fx.Deck, YdkImport.ReadFile(path16, cards).Deck));
        }
        Check("tabs around codes and upper-case sections read the same",
            Same(fx.Deck, YdkImport.Read(fxText.Replace("#main", "#MAIN").Replace("!side", "!SIDE").Replace("\n2", "\n\t2"), "x", cards).Deck));
        // DeckCodes.Parse (the game's own reader) skips a line with words after its code; the import reads the code.
        Check("DeckCodes.Parse reads the fixture's codes the same way (unknown ones and the commented line aside)",
            DeckCodes.Parse(fxText.Replace(" # Torrential Tribute", "")) is { } parsed && parsed.Main.Where(c => cards.Get(c) != null).SequenceEqual(fx.Deck!.Main));

        // ---- Sizes: imported as they are ----
        var big = new StringBuilder("#main\n");
        for (int i = 0; i < 61; i++) big.Append("55144522\n");
        big.Append("#extra\n");
        for (int i = 0; i < 16; i++) big.Append("63519819\n");
        big.Append("!side\n");
        for (int i = 0; i < 16; i++) big.Append("12580477\n");
        var bigRead = YdkImport.Read(big.ToString(), "big", cards);
        Check($"61 / 16 / 16 (61 copies of one card) imports as it is ({Counts(bigRead.Deck)})", bigRead.Deck is { Main.Count: 61, Extra.Count: 16, Side.Count: 16 });
        Check("and the deck rules then say what's wrong", bigRead.Deck != null && DeckRules.Validate(bigRead.Deck, Banlist.None, cards).Count > 0);
        var tiny = YdkImport.Read("#main\n55144522\n", "tiny", cards);
        Check($"a 1-card deck imports ({Counts(tiny.Deck)})", tiny.Deck is { Main.Count: 1 });

        // ---- Garbage: a clear message, no exception ----
        void Garbage(string what, YdkImport r, string says)
        {
            Console.WriteLine($"ydk test:   {what}: \"{r.Error}\"");
            Check($"{what} is refused with a message ({says})", !r.Ok && r.Error.Contains(says));
        }
        Garbage("an empty file", YdkImport.Read("", "e", cards, "e.ydk"), "empty");
        Garbage("only a BOM and blank lines", YdkImport.Read("\uFEFF\r\n\r\n  \n", "e", cards, "e.ydk"), "empty");
        Garbage("words", YdkImport.Read("hello world\nthis is not a deck\n", "w", cards, "w.ydk"), "no #main");
        Garbage("sections with no codes", YdkImport.Read("#created by x\n#main\n#extra\n!side\n", "n", cards, "n.ydk"), "no card codes");
        Garbage("sections with only words", YdkImport.Read("#main\nAsh Blossom\nMaxx C\n", "n", cards, "n.ydk"), "no card codes");
        Garbage("only unknown codes", YdkImport.Read("#main\n99999999\n99999998\n", "u", cards, "u.ydk"), "None of the 2 card codes");
        var binary = Path.Combine(folder, "binary.ydk");
        File.WriteAllBytes(binary, new byte[] { 0x23, 0x6D, 0x61, 0x69, 0x6E, 0x0A, 0x00, 0xFF, 0x13, 0x37 });
        Garbage("a binary file", YdkImport.ReadFile(binary, cards), "isn't a text file");
        var huge = Path.Combine(folder, "huge.ydk");
        File.WriteAllText(huge, "#main\n" + string.Concat(Enumerable.Repeat("55144522\n", YdkImport.MaxBytes / 9 + 10)));
        Garbage("a huge file", YdkImport.ReadFile(huge, cards), "the limit is 256 KB");
        Garbage("a Side-only file", YdkImport.Read("#main\n#extra\n!side\n55144522\n", "s", cards, "s.ydk"), "only a Side Deck");
        var utf16bare = Path.Combine(folder, "utf16-nobom.ydk");
        File.WriteAllBytes(utf16bare, Encoding.Unicode.GetBytes(File.ReadAllText(fixture)));
        Garbage("UTF-16 without a byte-order mark", YdkImport.ReadFile(utf16bare, cards), "save it as UTF-8");
        Garbage("a missing file", YdkImport.ReadFile(Path.Combine(folder, "nope.ydk"), cards), "Couldn't find");
        Check("a name from a file: \"ydk-decklist.ydk\" is \"ydk-decklist\"", YdkImport.NameFromFile("/x/ydk-decklist.ydk") == "ydk-decklist");
        Check("a name with a slash, a colon and leading dots is cleaned", YdkImport.CleanName("..a/b:c") == "a b c" && YdkImport.CleanName("   ") == "imported deck");
        Check("leading dots and spaces go however they mix (\". . .x\" is \"x\")", YdkImport.CleanName(". . .x") == "x" && YdkImport.CleanName(" . ") == "imported deck");
        Check($"< and > are taken out, since names show in rich-text labels (\"{YdkImport.CleanName("<b>Big</b>")}\")", YdkImport.CleanName("<b>Big</b>") == "b Big b");
        var cutName = YdkImport.CleanName(new string('a', 59) + "\U0001F409" + "tail");
        Check($"a 60-character cut doesn't split a surrogate pair ({cutName.Length} characters)", cutName.Length == 59 && !char.IsHighSurrogate(cutName[cutName.Length - 1]));
        Check("names are composed (FormC): \"Cafe\" + a combining accent is \"Café\"", YdkImport.CleanName("Cafe\u0301") == "Caf\u00E9");

        // ---- My recipes (files next to the save) ----
        var save = Path.Combine(folder, "save", "profile.txt");
        if (Directory.Exists(Path.GetDirectoryName(save)!)) Directory.Delete(Path.GetDirectoryName(save)!, true);
        var mine = new MyRecipes(save);
        Check("an old save without the recipes folder has none (and nothing is created)", mine.Names.Count == 0 && !Directory.Exists(mine.Folder));
        var n1 = mine.Add(fx.Deck!, "small-goat");
        var n2 = mine.Add(fx.Deck!, "small-goat");
        var n3 = mine.Add(fx.Deck!, "SMALL-GOAT");
        Check($"duplicate names get a number ({n1}, {n2}, {n3})", n1 == "small-goat" && n2 == "small-goat (2)" && n3 == "SMALL-GOAT (3)");
        var loaded = mine.Load(n1);
        Check($"a recipe loads as my:{n1} with its title and the same cards", loaded is { Name: "my:small-goat" } && loaded.Note("title") == "small-goat" && Same(loaded, fx.Deck));
        Check("its file holds no notes or name but the game's own header", !File.ReadAllText(Path.Combine(mine.Folder, "small-goat.ydk")).Contains("Deck Site User"));
        Check("Load takes \"my:name\" too", mine.Load("my:" + n2) != null && MyRecipes.IsMine("my:x") && !MyRecipes.IsMine("zombies") && MyRecipes.NameOf("my:x") == "x");
        Check("renaming onto a name you have is refused", !mine.Rename(n2, "small-goat", out _, out var why) && why != null && why.Contains("already"));
        Check("renaming works and keeps the cards", mine.Rename(n2, "Goat Control", out var renamed, out _) && renamed == "Goat Control" && Same(mine.Load("Goat Control"), fx.Deck) && !mine.Contains(n2));
        Check("a case-only rename works", mine.Rename("Goat Control", "goat control", out _, out _) && mine.Names.Contains("goat control") && !mine.Names.Contains("Goat Control"));
        Check("renaming one that isn't there is refused", !mine.Rename("nope", "x", out _, out _));
        Check("delete removes it", mine.Delete("goat control") && !mine.Contains("goat control") && mine.Names.Count == 2);
        Check("deleting one that isn't there says so", !mine.Delete("goat control"));
        Check("FreeName skips the names in use", mine.FreeName("small-goat") == "small-goat (2)");
        Check("a blank rename is refused", !mine.Rename("small-goat", "   ", out _, out var blankWhy) && blankWhy != null && mine.Contains("small-goat"));
        // Accents: APFS sees "Café" composed and decomposed as one file. Adding both must keep both recipes.
        var smallDeck = new Deck { Main = { 55144522 } };
        var c1 = mine.Add(fx.Deck!, "Caf\u00E9");
        var c2 = mine.Add(smallDeck, "Cafe\u0301");
        Check($"\"Café\" added twice, spelled two ways, keeps both ({c1}, {c2})", c1 == "Caf\u00E9" && c2 == "Caf\u00E9 (2)" && Same(mine.Load(c1), fx.Deck) && Same(mine.Load(c2), smallDeck));
        Check("renaming onto the other spelling of a name you have is refused, and nothing is stranded",
            !mine.Rename(c2, "Cafe\u0301", out _, out _) && Same(mine.Load(c2), smallDeck) && Directory.GetFiles(mine.Folder, "*.renaming").Length == 0);
        // A rename whose second step fails (a folder in the way) puts the recipe back.
        Directory.CreateDirectory(Path.Combine(mine.Folder, "Blocked.ydk"));
        Check("a rename that fails half way rolls back (the recipe keeps its name, no .renaming file)",
            !mine.Rename(c2, "Blocked", out _, out _) && Same(mine.Load(c2), smallDeck) && Directory.GetFiles(mine.Folder, "*.renaming").Length == 0);
        Check("the folder in the way isn't listed as a recipe", !mine.Names.Contains("Blocked"));

        // Build trims a recipe over the size limits (AutoDeck.FromRecipe) and says what it cut.
        var pool = cards.All.Where(c => c.Alias == 0 && (c.Type & CardType.Monster) != 0 && (c.Type & (CardType.ExtraDeckTypes | CardType.Token)) == 0)
            .Select(c => c.Code).OrderBy(c => c).Take(90).ToList();
        var owned = new Collection();
        foreach (var code in pool) owned.Add(code, Ygo.Engine.Data.Rarity.Common);
        var oversize = new Deck { Name = "my:oversize", Main = pool.Take(70).ToList(), Side = pool.Skip(70).Take(17).ToList() };
        var builtOver = AutoDeck.FromRecipe(oversize, owned, Banlist.None, cards);
        var cutLines = builtOver.Changes.Where(c => c.StartsWith("Cut ")).ToList();
        Check($"Build cuts a 70 / 0 / 17 recipe to {builtOver.Deck.Main.Count} / {builtOver.Deck.Extra.Count} / {builtOver.Deck.Side.Count} and lists the cuts ({string.Join("; ", cutLines.Select(c => c.Substring(0, Math.Min(44, c.Length))))})",
            builtOver.Deck.Main.Count == DeckRules.MainMax && builtOver.Deck.Side.Count == DeckRules.SideMax
            && cutLines.Any(c => c.StartsWith("Cut 10 from the Main Deck")) && cutLines.Any(c => c.StartsWith("Cut 2 from the Side Deck"))
            && DeckRules.Validate(builtOver.Deck, Banlist.None, cards).Count == 0);

        Console.WriteLine($"ydk test: {oks} ok, {fails} FAIL");
        return fails == 0 ? 0 : 1;
    }
}
