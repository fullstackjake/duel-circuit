using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.AI;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>One deck in the Recipe Book: a duelist's deck, unlocked by beating them with it (Tag Force style).</summary>
    public sealed class Recipe
    {
        public Personality Owner = Personality.Balanced;
        /// <summary>The deck's name in the deck library.</summary>
        public string Deck = "";
        /// <summary>The first circuit it appears in (its region on the map is that circuit's band).</summary>
        public int Circuit;
        /// <summary>Where you meet it: "Underdog Open, match 4" or "Novice Tag Cup".</summary>
        public string Where = "";
        /// <summary>The circuit's final opponent (a boss's deck).</summary>
        public bool Boss;
        /// <summary>
        /// A match's pairing from before a deck swap ("was:" in circuits.txt), met nowhere else now: unlocked only for a
        /// save that won it then (<see cref="RecipeBook.FormerKey"/> in <see cref="Profile.Unlocks"/>, written by
        /// <see cref="Circuits.Migrate"/>); the Recipe Book lists it only once unlocked.
        /// </summary>
        public bool Former;
        public string Key => RecipeBook.Key(Owner.Id, Deck);
    }

    /// <summary>
    /// The Recipe Book: every deck the campaign's duelists play, in campaign order. A recipe unlocks the first time you
    /// beat its duelist while they play it, in a circuit match or a Tag Cup. Unlocks are worked out from your wins, so
    /// saves from before the Recipe Book have theirs already. "Seen" recipes are marked in <see cref="Profile.Seen"/>.
    /// </summary>
    public static class RecipeBook
    {
        public static string Key(string owner, string deck) => $"{owner}|{deck}";
        public static string SeenKey(Recipe recipe) => "recipe:" + recipe.Key;

        /// <summary>The <see cref="Profile.Unlocks"/> key prefix of a recipe kept from before a deck swap.</summary>
        public const string FormerPrefix = "formerrecipe:";

        /// <summary>The <see cref="Profile.Unlocks"/> key that keeps a recipe a save won before its match's deck swap: "formerrecipe:" + its key.</summary>
        public static string FormerKey(Recipe recipe) => FormerPrefix + recipe.Key;

        /// <summary>
        /// Whether you own every card the recipe needs, by count, in every mode (unlike <see cref="RecipeShop.Missing"/>, no era rule
        /// leaves a card out: one never printed in the TCG still has to be owned).
        /// </summary>
        public static bool OwnsAll(Deck recipe, Profile profile, PrintingIndex printings, ICardDataProvider cards) =>
            AutoDeck.Wants(recipe, Banlist.None, cards).All(w => profile.Collection.Owned(w.Key) >= w.Value);

        /// <summary>Every recipe, once each (by duelist and deck), in the order you meet them.</summary>
        public static List<Recipe> All(Circuits circuits)
        {
            var list = new List<Recipe>();
            var seen = new HashSet<string>();
            void Add(CircuitMatch m, int circuit, string where, bool boss, bool former = false)
            {
                if (m.Deck.Length == 0 || !seen.Add(Key(m.Character.Id, m.Deck))) return;
                list.Add(new Recipe { Owner = m.Character, Deck = m.Deck, Circuit = circuit, Where = where, Boss = boss, Former = former });
            }
            // Circuit matches first (a deck a boss plays is listed as theirs, from that circuit), then decks only met
            // in Tag Cups; then in map order.
            for (int c = 0; c < circuits.All.Count; c++)
            {
                var circuit = circuits.All[c];
                for (int m = 0; m < circuit.Matches.Count; m++)
                    Add(circuit.Matches[m], c, $"{circuit.Name}, match {m + 1}", m == circuit.Matches.Count - 1);
            }
            for (int t = 0; t < circuits.TagCups.Count; t++)
                foreach (var (lead, partner) in circuits.TagCups[t].Matches)
                {
                    Add(lead, circuits.TagCups[t].After, circuits.TagCups[t].Name, false);
                    Add(partner, circuits.TagCups[t].After, circuits.TagCups[t].Name, false);
                }
            // Last, pairings from before a deck swap that no match or Tag Cup still plays.
            for (int c = 0; c < circuits.All.Count; c++)
                foreach (var (m, was) in circuits.All[c].Former)
                    Add(was, c, $"{circuits.All[c].Name}, match {m + 1}, in an earlier version", Circuits.IsFinal(circuits.All[c], m), former: true);
            return list.Select((r, i) => (r, i)).OrderBy(x => x.r.Circuit).ThenBy(x => x.i).Select(x => x.r).ToList();
        }

        /// <summary>Whether you've beaten the recipe's duelist while they played it (before a deck swap too: <see cref="FormerKey"/>).</summary>
        public static bool IsUnlocked(Recipe recipe, Circuits circuits, Profile profile)
        {
            if (profile.Unlocks.Contains(FormerKey(recipe))) return true;
            for (int c = 0; c < circuits.All.Count; c++)
                for (int m = 0; m < circuits.All[c].Matches.Count; m++)
                {
                    var match = circuits.All[c].Matches[m];
                    if (match.Character.Id == recipe.Owner.Id && match.Deck == recipe.Deck && profile.HasWon(c, m)) return true;
                }
            for (int t = 0; t < circuits.TagCups.Count; t++)
                for (int m = 0; m < circuits.TagCups[t].Matches.Count; m++)
                {
                    var (lead, partner) = circuits.TagCups[t].Matches[m];
                    bool plays = (lead.Character.Id == recipe.Owner.Id && lead.Deck == recipe.Deck)
                                 || (partner.Character.Id == recipe.Owner.Id && partner.Deck == recipe.Deck);
                    if (plays && profile.MatchWins.ContainsKey(Circuits.TagKey(t, m))) return true;
                }
            return false;
        }

        public static List<Recipe> Unlocked(Circuits circuits, Profile profile) =>
            All(circuits).Where(r => IsUnlocked(r, circuits, profile)).ToList();

        /// <summary>Unlocked recipes you haven't opened in the book yet.</summary>
        public static int New(Circuits circuits, Profile profile) =>
            Unlocked(circuits, profile).Count(r => !profile.Seen.Contains(SeenKey(r)));

        /// <summary>
        /// The recipe a circuit win just unlocked, if this was the first win against that duelist with that deck
        /// (call after recording the win); null otherwise.
        /// </summary>
        public static Recipe? JustUnlocked(Circuits circuits, Profile profile, int circuit, int match)
        {
            var played = circuits.All[circuit].Matches[match];
            int wins = 0;
            for (int c = 0; c < circuits.All.Count; c++)
                for (int m = 0; m < circuits.All[c].Matches.Count; m++)
                {
                    var other = circuits.All[c].Matches[m];
                    if (other.Character.Id == played.Character.Id && other.Deck == played.Deck)
                        wins += profile.MatchWins.TryGetValue(Profile.MatchKey(c, m), out var n) ? n : 0;
                }
            for (int t = 0; t < circuits.TagCups.Count; t++)
                for (int m = 0; m < circuits.TagCups[t].Matches.Count; m++)
                {
                    var (lead, partner) = circuits.TagCups[t].Matches[m];
                    if ((lead.Character.Id == played.Character.Id && lead.Deck == played.Deck) || (partner.Character.Id == played.Character.Id && partner.Deck == played.Deck))
                        wins += profile.MatchWins.TryGetValue(Circuits.TagKey(t, m), out var n) ? n : 0;
                }
            // A recipe kept from before a deck swap (FormerKey) was already yours.
            var recipe = wins == 1 ? All(circuits).FirstOrDefault(r => r.Owner.Id == played.Character.Id && r.Deck == played.Deck) : null;
            return recipe != null && !profile.Unlocks.Contains(FormerKey(recipe)) ? recipe : null;
        }

        /// <summary>The recipes a Tag Cup win just unlocked (its two opponents' decks, when this was their first loss with them).</summary>
        public static List<Recipe> JustUnlockedTag(Circuits circuits, Profile profile, int cup, int match)
        {
            var (lead, partner) = circuits.TagCups[cup].Matches[match];
            var found = new List<Recipe>();
            foreach (var played in new[] { lead, partner })
            {
                var recipe = All(circuits).FirstOrDefault(r => r.Owner.Id == played.Character.Id && r.Deck == played.Deck);
                if (recipe == null) continue;
                int wins = 0;
                for (int c = 0; c < circuits.All.Count; c++)
                    for (int m = 0; m < circuits.All[c].Matches.Count; m++)
                        if (circuits.All[c].Matches[m].Character.Id == played.Character.Id && circuits.All[c].Matches[m].Deck == played.Deck)
                            wins += profile.MatchWins.TryGetValue(Profile.MatchKey(c, m), out var n) ? n : 0;
                for (int t = 0; t < circuits.TagCups.Count; t++)
                    for (int m = 0; m < circuits.TagCups[t].Matches.Count; m++)
                    {
                        var (a, b) = circuits.TagCups[t].Matches[m];
                        if ((a.Character.Id == played.Character.Id && a.Deck == played.Deck) || (b.Character.Id == played.Character.Id && b.Deck == played.Deck))
                            wins += profile.MatchWins.TryGetValue(Circuits.TagKey(t, m), out var n) ? n : 0;
                    }
                if (wins == 1 && !profile.Unlocks.Contains(FormerKey(recipe))) found.Add(recipe);
            }
            return found;
        }
    }
}
