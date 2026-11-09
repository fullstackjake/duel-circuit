using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// "My recipes": deck lists the player imported from .ydk files in the campaign (docs/dev/YDK-IMPORT-2026-11-07.md). They
    /// sit next to the unlocked recipes (the Recipe Book, the deck editor's Recipe button) as a plan to build towards: Build
    /// fills them from your collection and their missing cards go on the wishlist, but nothing sells them (no recipe packs,
    /// no "Buy what's missing") and Gus's bounties don't pay from them.
    /// <para>
    /// Stored as one .ydk file each in a "recipes" folder next to the save (mods/save/recipes/&lt;name&gt;.ydk), holding only
    /// the three decks. A save without the folder has none, so old saves load unchanged; profile.txt doesn't change.
    /// </para>
    /// In the game a recipe is named "my:&lt;name&gt;" wherever a deck name goes (the wishlist's WishlistFor, the editor's
    /// recipe cycle), so it can't clash with a built-in deck.
    /// </summary>
    public sealed class MyRecipes
    {
        public const string Prefix = "my:";
        public const string FolderName = "recipes";

        private readonly string _folder;

        /// <param name="savePath">The save file (mods/save/profile.txt): the recipes live in a folder next to it.</param>
        public MyRecipes(string savePath)
        {
            _folder = Path.Combine(Path.GetDirectoryName(savePath) ?? "", FolderName);
        }

        public string Folder => _folder;

        /// <summary>Whether a deck name is one of yours ("my:Name").</summary>
        public static bool IsMine(string? deck) => deck != null && deck.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>A recipe's own name from its deck name ("my:Name" is "Name").</summary>
        public static string NameOf(string deck) => IsMine(deck) ? deck.Substring(Prefix.Length) : deck;

        /// <summary>Your recipes' names, A to Z (none when the folder doesn't exist).</summary>
        public List<string> Names
        {
            get
            {
                try
                {
                    if (!Directory.Exists(_folder)) return new List<string>();
                    // FormC: the disk may hand a name back decomposed ("Cafe" + a combining accent); names compare composed.
                    return Directory.GetFiles(_folder, "*.ydk").Select(Path.GetFileNameWithoutExtension)
                        .Where(n => !string.IsNullOrEmpty(n)).Select(n => Composed(n!)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return new List<string>(); }
            }
        }

        public bool Contains(string name) => Names.Any(n => SameName(n, NameOf(name)));

        private static string Composed(string name)
        {
            try { return name.Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { return name; }
        }

        /// <summary>Whether two names are one file on a case- and normalization-insensitive disk (APFS, HFS+).</summary>
        private static bool SameName(string a, string b) => string.Equals(Composed(a), Composed(b), StringComparison.OrdinalIgnoreCase);

        private string PathOf(string name) => Path.Combine(_folder, Composed(NameOf(name)) + ".ydk");

        /// <summary>
        /// A recipe as a deck: named "my:Name" with the title "Name", only its Main, Extra and Side Decks. Null when it isn't
        /// there or can't be read.
        /// </summary>
        public Deck? Load(string name)
        {
            var own = NameOf(name);
            try
            {
                if (!File.Exists(PathOf(own))) return null;
                var read = Deck.ParseYdk(File.ReadAllText(PathOf(own)), Prefix + own);
                var deck = new Deck { Name = Prefix + own, Main = read.Main, Extra = read.Extra, Side = read.Side };
                deck.Notes["title"] = own;
                return deck;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return null; }
        }

        /// <summary>Every recipe of yours as a deck (see <see cref="Load"/>).</summary>
        public List<Deck> All() => Names.Select(Load).Where(d => d != null).Select(d => d!).ToList();

        /// <summary>A free name like <paramref name="wanted"/>: "Name", else "Name (2)", "Name (3)"...</summary>
        public string FreeName(string wanted, string? except = null)
        {
            var name = YdkImport.CleanName(wanted);
            var taken = Names.Where(n => except == null || !SameName(n, except)).ToList();
            bool Free(string candidate) => !taken.Any(t => SameName(t, candidate)) && (except != null && SameName(candidate, except) || !File.Exists(PathOf(candidate)));
            if (Free(name)) return name;
            for (int n = 2; ; n++)
                if (Free($"{name} ({n})")) return $"{name} ({n})";
        }

        /// <summary>Keeps a deck's three decks as a new recipe named like <paramref name="wanted"/> (a free name); returns the name used.</summary>
        public string Add(Deck deck, string wanted)
        {
            // Never over a file that's there: a name taken between FreeName and the write gets the next one.
            for (int attempt = 0; ; attempt++)
            {
                var name = FreeName(wanted);
                try { Write(name, deck); return name; }
                catch (IOException) when (attempt < 20 && File.Exists(PathOf(name))) { }
            }
        }

        /// <summary>
        /// Renames a recipe; false with the reason when it can't (it isn't there, the new name is taken). Changing only the
        /// case of a name works.
        /// </summary>
        public bool Rename(string from, string to, out string newName, out string? refused)
        {
            from = Composed(NameOf(from));
            newName = from;
            refused = null;
            if (string.IsNullOrWhiteSpace(to)) { refused = "a recipe needs a name"; return false; }
            newName = YdkImport.CleanName(to);
            if (!File.Exists(PathOf(from))) { refused = $"there's no recipe called {from}"; return false; }
            if (newName == from) return true;
            if (!SameName(newName, from) && (Contains(newName) || File.Exists(PathOf(newName)))) { refused = $"you already have a recipe called {newName}"; return false; }
            var temp = PathOf(from) + ".renaming";
            try
            {
                File.Move(PathOf(from), temp); // two steps, so a case-only rename works on a case-insensitive disk
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { refused = ex.Message; return false; }
            try
            {
                File.Move(temp, PathOf(newName));
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Put it back under its old name, so a failed rename never loses the recipe.
                try { File.Move(temp, PathOf(from)); } catch (Exception back) when (back is IOException || back is UnauthorizedAccessException) { }
                refused = ex.Message;
                return false;
            }
        }

        /// <summary>Deletes a recipe (its file); false when it wasn't there.</summary>
        public bool Delete(string name)
        {
            try
            {
                if (!File.Exists(PathOf(name))) return false;
                File.Delete(PathOf(name));
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return false; }
        }

        private void Write(string name, Deck deck)
        {
            Directory.CreateDirectory(_folder);
            // Only the three decks: no notes, starters or campaign flag ride along.
            var plain = new Deck { Name = name, Main = deck.Main.ToList(), Extra = deck.Extra.ToList(), Side = deck.Side.ToList() };
            using var stream = new FileStream(PathOf(name), FileMode.CreateNew, FileAccess.Write); // fails rather than overwrite
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(DeckCodes.ToYdk(plain));
        }
    }
}
