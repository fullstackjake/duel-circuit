using System.Collections.Generic;
using System.Linq;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>
    /// "My recipes" (docs/dev/YDK-IMPORT-2026-11-07.md): .ydk files imported in the campaign, kept as recipes next to the
    /// unlocked ones (files in mods/save/recipes, <see cref="Ygo.Engine.Campaign.MyRecipes"/>). They're a plan: Build and the
    /// wishlist use them, nothing sells them, and they touch no DP.
    /// </summary>
    public sealed partial class CampaignState
    {
        /// <summary>Your imported recipes (files next to the save; none in an old save).</summary>
        public MyRecipes MyRecipes { get; }

        /// <summary>
        /// The list a recipe of yours is judged by: the current circuit's when ban lists apply (era-limited play), else none.
        /// Cards over it are flagged, never removed from the recipe.
        /// </summary>
        public Banlist MyRecipeList => (DebugRecipeCircuit is int c ? CircuitList(c) : ListFor(CurrentCircuit)) ?? Banlist.None;

        /// <summary>For checks: judge your recipes by this circuit's list (whatever the mode) instead of the current one.</summary>
        public int? DebugRecipeCircuit;

        /// <summary>
        /// Imports a .ydk file as one of your recipes, named from the file (a number added when you have that name). Returns
        /// its name, or null; <paramref name="status"/> is the line to show either way (what was skipped, or why nothing was
        /// imported).
        /// </summary>
        public string? ImportRecipe(string path, out string status)
        {
            var read = YdkImport.ReadFile(path, _cards);
            if (read.Deck == null) { status = read.Error; return null; }
            var name = MyRecipes.Add(read.Deck, YdkImport.NameFromFile(path));
            status = read.Summary(System.IO.Path.GetFileName(path)).Replace("Imported ", $"Added \"{name}\" to My recipes: ");
            return name;
        }

        /// <summary>Renames one of your recipes (the wishlist follows it); false with the reason in <paramref name="status"/>.</summary>
        public bool RenameRecipe(string name, string to, out string newName, out string status)
        {
            name = MyRecipes.NameOf(name);
            if (!MyRecipes.Rename(name, to, out newName, out var refused)) { status = $"Couldn't rename it: {refused}."; return false; }
            if (Profile != null && Profile.WishlistFor == MyRecipes.Prefix + name)
            {
                Profile.WishlistFor = MyRecipes.Prefix + newName;
                Save();
            }
            status = newName == name ? $"{name} keeps its name." : $"Renamed {name} to {newName}.";
            return true;
        }

        /// <summary>Deletes one of your recipes; a wishlist set from it is cleared. Your decks built from it stay.</summary>
        public bool DeleteRecipe(string name)
        {
            name = MyRecipes.NameOf(name);
            if (!MyRecipes.Delete(name)) return false;
            if (Profile != null && Profile.WishlistFor == MyRecipes.Prefix + name)
            {
                Profile.Wishlist.Clear();
                Profile.WishlistFor = "";
                Save();
            }
            return true;
        }

        /// <summary>The wishlist set from one of your recipes: the copies of each card it uses (within <see cref="MyRecipeList"/>).</summary>
        public void WishFor(Deck recipe)
        {
            if (Profile == null) return;
            Profile.Wishlist.Clear();
            foreach (var w in AutoDeck.Wants(recipe, MyRecipeList, _cards)) Profile.Wishlist[w.Key] = w.Value;
            Profile.WishlistFor = recipe.Name.StartsWith(MyRecipes.Prefix) ? recipe.Name : MyRecipes.Prefix + recipe.Name;
            Save();
        }

        /// <summary>
        /// The cards of a recipe of yours that <paramref name="list"/> doesn't allow as many of (base code, copies in it, copies
        /// allowed), in deck order.
        /// </summary>
        public List<(uint code, int copies, int allowed)> OverList(Deck recipe, Banlist list) =>
            recipe.Main.Concat(recipe.Extra).Concat(recipe.Side).GroupBy(c => Banlist.BaseCode(c, _cards))
                .Select(g => (code: g.Key, copies: g.Count(), allowed: list.Allowed(g.Key, _cards)))
                .Where(x => x.copies > x.allowed).ToList();
    }
}
