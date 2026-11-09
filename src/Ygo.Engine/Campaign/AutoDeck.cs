using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.AI;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// Deck building from a collection, for the campaign: fixing a deck that breaks a ban list or uses cards you don't
    /// have, and building a whole deck from what you own (optionally around one card). Cards are valued by a simple
    /// heuristic: monsters by stats and how easy they are to summon, everything by its role (removal, draw, search,
    /// negation, revival), plus a bonus for sharing an archetype with the rest of the deck.
    /// </summary>
    public static class AutoDeck
    {
        public const int MainSize = 40;
        private const int MaxTributeMonsters = 5, MaxBigMonsters = 2, TargetMonsters = 18;
        private const uint TypeNormal = 0x10, TypeTuner = 0x1000;

        public sealed class Result
        {
            public Deck Deck = new Deck();
            /// <summary>What changed, in plain words ("Removed Pot of Greed (Forbidden)", "Added 2 Sangan").</summary>
            public List<string> Changes = new List<string>();
        }

        /// <summary>How good a card is on its own, roughly in "points" (a 1900 ATK beater is about 19).</summary>
        public static double Value(CardInfo c)
        {
            var traits = CardTraits.Of(c);
            double role = 0;
            if (traits.Has(Trait.BoardWipe)) role += 14;
            if (traits.Has(Trait.MonsterRemoval)) role += 10;
            if (traits.Has(Trait.SpellTrapRemoval)) role += 7;
            if (traits.Has(Trait.Draw)) role += 9;
            if (traits.Has(Trait.Search)) role += 9;
            if (traits.Has(Trait.Negate)) role += 9;
            if (traits.Has(Trait.HandTrap)) role += 8;
            if (traits.Has(Trait.Revive)) role += 8;
            if (traits.Has(Trait.SpecialSummon)) role += 4;

            if ((c.Type & CardType.Monster) == 0)
                return 8 + role; // a spell or trap with no recognised role still does something
            if ((c.Type & CardType.Ritual) != 0) return role * 0.5; // needs its Ritual Spell; rarely worth it here
            var stats = Math.Max(c.Attack, c.Defense * 0.7) / 100.0;
            if (c.IsExtraDeckMonster) return stats + role;
            if ((c.Type & CardType.Link) != 0) return stats + role;
            // Main Deck monsters: tributes make the big ones cost more.
            double cost = c.Level >= 7 ? 22 : c.Level >= 5 ? 12 : 0;
            bool vanilla = (c.Type & TypeNormal) != 0;
            return stats - cost + role + (vanilla ? 0 : 2);
        }

        private static bool IsTribute(CardInfo c) =>
            (c.Type & CardType.Monster) != 0 && !c.IsExtraDeckMonster && (c.Type & CardType.Link) == 0 && c.Level >= 5;

        /// <summary>
        /// Copies of a card you may put in a deck: what you own, within the ban list (base card code).
        /// </summary>
        private static int Allowed(uint code, Collection owned, Banlist list, ICardDataProvider cards, Func<uint, bool>? released = null) =>
            released != null && !released(code) ? 0 : Math.Min(owned.Count(code), Math.Min(Collection.MaxCopies, list.Allowed(code, cards)));

        /// <summary>
        /// Makes <paramref name="deck"/> legal for the campaign: drops copies over the ban list or over what you own, then
        /// tops the Main Deck back up to 40 with your best spare cards (favouring the deck's archetypes). With
        /// <paramref name="released"/> (an era-limited campaign), cards not out yet in the format go too.
        /// </summary>
        public static Result Fix(Deck deck, Collection owned, Banlist list, ICardDataProvider cards, Func<uint, bool>? released = null)
        {
            var result = new Result { Deck = deck.Clone() };
            var d = result.Deck;
            var used = new Dictionary<uint, int>();
            void Trim(List<uint> section)
            {
                for (int i = 0; i < section.Count; i++)
                {
                    var key = Banlist.BaseCode(section[i], cards);
                    int n = used.TryGetValue(key, out var u) ? u : 0;
                    if (n < Allowed(key, owned, list, cards, released)) { used[key] = n + 1; continue; }
                    var name = cards.Get(key)?.Name ?? key.ToString();
                    var why = released != null && !released(key) ? "not released yet" : list.Allowed(key, cards) == 0 ? (list.Whitelist ? "not legal" : "Forbidden")
                        : n >= list.Allowed(key, cards) ? $"limited to {list.Allowed(key, cards)}" : owned.Count(key) == 0 ? "not owned" : $"you own {owned.Count(key)}";
                    result.Changes.Add($"Removed {name} ({why})");
                    section.RemoveAt(i--);
                }
            }
            int extraWas = Math.Min(DeckRules.ExtraMax, d.Extra.Count);
            Trim(d.Main);
            Trim(d.Extra);
            Trim(d.Side);
            while (d.Extra.Count > DeckRules.ExtraMax) d.Extra.RemoveAt(d.Extra.Count - 1);

            var added = Fill(d, owned, list, cards, used, released);
            // Extra Deck slots it emptied go to the best Extra Deck monsters you own.
            if (d.Extra.Count < extraWas) added.AddRange(FillExtra(d, owned, list, cards, used, released, extraWas));
            foreach (var group in added.GroupBy(c => c))
                result.Changes.Add($"Added {(group.Count() > 1 ? $"{group.Count()} " : "")}{cards.Get(group.Key)?.Name}");
            Merge(result.Changes);
            return result;
        }

        /// <summary>A whole deck from your collection, legal under <paramref name="list"/>, optionally built around one card.</summary>
        public static Result Build(Collection owned, Banlist list, ICardDataProvider cards, uint? around = null, string name = "auto deck",
            Func<uint, bool>? released = null)
        {
            var deck = new Deck { Name = name, Campaign = true };
            var used = new Dictionary<uint, int>();
            if (around is uint core && cards.Get(core) is CardInfo coreInfo)
            {
                int copies = Allowed(core, owned, list, cards, released);
                for (int i = 0; i < copies; i++) (coreInfo.IsExtraDeckMonster ? deck.Extra : deck.Main).Add(core);
                if (copies > 0) used[core] = copies;
            }
            Fill(deck, owned, list, cards, used, released);
            FillExtra(deck, owned, list, cards, used, released, DeckRules.ExtraMax);
            var result = new Result { Deck = deck };
            int monsters = deck.Main.Count(c => (cards.Get(c)?.Type & CardType.Monster) != 0);
            result.Changes.Add($"Built {deck.Main.Count} cards ({monsters} monsters, {deck.Main.Count - monsters} spells and traps) and an Extra Deck of {deck.Extra.Count}");
            return result;
        }

        /// <summary>How much of a recipe you can build: owned copies (within the ban list) over the cards it uses.</summary>
        public static double Owned(Deck recipe, Collection owned, Banlist list, ICardDataProvider cards, Func<uint, bool>? released = null)
        {
            int have = 0, total = 0;
            foreach (var group in recipe.Main.Concat(recipe.Extra).GroupBy(c => Banlist.BaseCode(c, cards)))
            {
                total += group.Count();
                have += Math.Min(group.Count(), Allowed(group.Key, owned, list, cards, released));
            }
            return total == 0 ? 0 : (double)have / total;
        }

        /// <summary>The cards (base codes) a recipe uses that you don't have enough of, within the ban list: a wishlist.</summary>
        public static HashSet<uint> Missing(Deck recipe, Collection owned, Banlist list, ICardDataProvider cards) =>
            new HashSet<uint>(Wants(recipe, list, cards).Where(w => w.Value > owned.Count(w.Key)).Select(w => w.Key));

        /// <summary>Copies of each card (base code) a recipe uses, within the ban list.</summary>
        public static Dictionary<uint, int> Wants(Deck recipe, Banlist list, ICardDataProvider cards) =>
            recipe.Main.Concat(recipe.Extra).GroupBy(c => Banlist.BaseCode(c, cards))
                .Select(g => (code: g.Key, copies: Math.Min(g.Count(), list.Allowed(g.Key, cards))))
                .Where(w => w.copies > 0).ToDictionary(w => w.code, w => w.copies);

        /// <summary>
        /// A known deck (a historic list or any recipe) built from your collection: the copies you own, with the gaps
        /// filled from your best spare cards.
        /// </summary>
        public static Result FromRecipe(Deck recipe, Collection owned, Banlist list, ICardDataProvider cards, Func<uint, bool>? released = null)
        {
            var result = Fix(recipe, owned, list, cards, released);
            // A recipe over the size limits (an imported .ydk can be) builds a legal deck: the cards past 60 / 15 are cut, and said.
            void Cut(List<uint> section, int max, string what)
            {
                if (section.Count <= max) return;
                var cut = section.Skip(max).ToList();
                section.RemoveRange(max, section.Count - max);
                var names = cut.GroupBy(c => c).Select(g => $"{cards.Get(g.Key)?.Name ?? g.Key.ToString()}{(g.Count() > 1 ? $" x{g.Count()}" : "")}");
                result.Changes.Add($"Cut {cut.Count} from the {what} Deck (over {max}): {string.Join(", ", names)}");
            }
            Cut(result.Deck.Main, DeckRules.MainMax, "Main");
            Cut(result.Deck.Extra, DeckRules.ExtraMax, "Extra");
            Cut(result.Deck.Side, DeckRules.SideMax, "Side");
            result.Deck.Name = recipe.Name;
            result.Deck.Campaign = true;
            result.Deck.Notes.Clear();
            int pct = (int)Math.Round(100 * Owned(recipe, owned, list, cards, released));
            result.Changes.Insert(0, $"{recipe.Name}: you own {pct}% of it");
            return result;
        }

        /// <summary>The recipes you could build best from your collection, most owned first (at least <paramref name="minOwned"/>).</summary>
        public static List<(Deck recipe, double owned)> BestRecipes(IEnumerable<Deck> recipes, Collection owned, Banlist list, ICardDataProvider cards,
            double minOwned = 0.5, Func<uint, bool>? released = null) =>
            recipes.Where(r => r.Main.Count >= MainSize)
                .Select(r => (recipe: r, owned: Owned(r, owned, list, cards, released)))
                .Where(r => r.owned >= minOwned)
                .OrderByDescending(r => r.owned).ToList();

        /// <summary>Adds the best Extra Deck monsters you own (for the Main Deck's archetypes) up to <paramref name="size"/>; returns what it added.</summary>
        private static List<uint> FillExtra(Deck deck, Collection owned, Banlist list, ICardDataProvider cards, Dictionary<uint, int> used,
            Func<uint, bool>? released, int size)
        {
            var added = new List<uint>();
            var extra = owned.Codes.Select(cards.Get).Where(c => c != null && c.IsExtraDeckMonster).Select(c => c!)
                .OrderByDescending(c => Value(c) + Synergy(c, deck, cards)).ToList();
            foreach (var c in extra)
                for (int i = used.TryGetValue(c.Code, out var u) ? u : 0; i < Allowed(c.Code, owned, list, cards, released) && deck.Extra.Count < size; i++)
                {
                    deck.Extra.Add(c.Code);
                    added.Add(c.Code);
                    used[c.Code] = i + 1;
                }
            return added;
        }

        /// <summary>Adds your best spare cards until the Main Deck has 40; returns what it added.</summary>
        private static List<uint> Fill(Deck deck, Collection owned, Banlist list, ICardDataProvider cards, Dictionary<uint, int> used,
            Func<uint, bool>? released = null)
        {
            var added = new List<uint>();
            var pool = owned.Codes.Select(cards.Get).Where(c => c != null && !c.IsExtraDeckMonster && (c.Type & CardType.Token) == 0)
                .Select(c => c!).ToList();
            while (deck.Main.Count < MainSize)
            {
                int monsters = deck.Main.Count(c => (cards.Get(c)?.Type & CardType.Monster) != 0);
                int tributes = deck.Main.Count(c => cards.Get(c) is CardInfo i && IsTribute(i));
                int big = deck.Main.Count(c => cards.Get(c)?.Level >= 7 && IsTribute(cards.Get(c)!));
                int slotsLeft = MainSize - deck.Main.Count;
                bool needMonsters = TargetMonsters - monsters >= slotsLeft;
                CardInfo? best = null;
                double bestScore = double.MinValue;
                foreach (var c in pool)
                {
                    if ((used.TryGetValue(c.Code, out var u) ? u : 0) >= Allowed(c.Code, owned, list, cards, released)) continue;
                    bool monster = (c.Type & CardType.Monster) != 0;
                    if (needMonsters && !monster) continue;
                    if (IsTribute(c) && (tributes >= MaxTributeMonsters || (c.Level >= 7 && big >= MaxBigMonsters))) continue;
                    double score = Value(c) + Synergy(c, deck, cards);
                    // Keep a playable mix: monsters until there are enough, then the best of anything.
                    if (monster && monsters < TargetMonsters) score += 4;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }
                if (best == null) break; // you don't own enough cards
                deck.Main.Add(best.Code);
                added.Add(best.Code);
                used[best.Code] = (used.TryGetValue(best.Code, out var n) ? n : 0) + 1;
            }
            return added;
        }

        /// <summary>A bonus for sharing an archetype with cards already in the deck.</summary>
        private static double Synergy(CardInfo c, Deck deck, ICardDataProvider cards)
        {
            if (c.Setcodes.Length == 0) return 0;
            int shared = deck.Main.Concat(deck.Extra).Select(cards.Get)
                .Count(o => o != null && o.Code != c.Code && o.Setcodes.Any(s => s != 0 && c.Setcodes.Contains(s)));
            return Math.Min(12, shared * 1.5);
        }

        /// <summary>Folds repeated lines ("Removed X (Forbidden)" twice) into one with a count.</summary>
        private static void Merge(List<string> changes)
        {
            var merged = changes.GroupBy(c => c).Select(g => g.Count() > 1 && g.Key.StartsWith("Removed ")
                ? $"Removed {g.Count()} {g.Key.Substring(8)}" : g.Key).ToList();
            changes.Clear();
            changes.AddRange(merged);
        }
    }
}
