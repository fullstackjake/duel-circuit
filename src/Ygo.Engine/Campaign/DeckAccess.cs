using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// Loaner decks: any recipe you've unlocked in the Recipe Book can be borrowed for a campaign duel without owning
    /// its cards (Master Duel's loaner decks). It's made legal for the circuit's ban list, can't be edited, and a win
    /// with it pays half DP, so building your own copy stays worth it.
    /// </summary>
    public static class Loaners
    {
        public const string Prefix = "loaner:";

        /// <summary>The name a loaner goes by in the deck picker (and in the duel's deck slot).</summary>
        public static string NameFor(string recipeDeck) => Prefix + recipeDeck;
        public static bool IsLoaner(string deckName) => deckName.StartsWith(Prefix, StringComparison.Ordinal);
        public static string RecipeOf(string loanerName) => IsLoaner(loanerName) ? loanerName.Substring(Prefix.Length) : loanerName;

        /// <summary>
        /// Generic cards a loaner fills with when a list leaves its own cards short of a 40-card Main Deck: hand traps and
        /// removal any deck can play (each still within the list).
        /// </summary>
        public static readonly uint[] Staples =
        {
            14558127, // Ash Blossom & Joyous Spring
            97268402, // Effect Veiler
            10045474, // Infinite Impermanence
            59438930, // Ghost Ogre & Snow Rabbit
            73642296, // Ghost Belle & Haunted Mansion
            27204311, // Nibiru, the Primal Being
            94145021, // Droll & Lock Bird
            24299458, // Forbidden Droplet
            14532163, // Lightning Storm
            54693926, // Dark Ruler No More
            14087893, // Book of Moon
            8267140,  // Cosmic Cyclone
            24224830, // Called by the Grave
            65681983, // Crossout Designator
            15693423, // Evenly Matched
        };

        /// <summary>
        /// The recipe, borrowed: made legal for the ban list as if you owned three of every card in it. Copies over the
        /// list are dropped and the deck is topped back up from its own cards, then (if the list took so much that its
        /// own cards can't make 40) from <see cref="Staples"/>; <see cref="AutoDeck.Result.Changes"/> says what changed
        /// ("Removed Pot of Prosperity (Forbidden)", "Added Ash Blossom"), so the player can see it. With
        /// <paramref name="released"/> (a Library cup's pool) it also drops the cards that weren't out yet.
        /// </summary>
        public static AutoDeck.Result Adjust(Deck recipe, Banlist list, ICardDataProvider cards, Func<uint, bool>? released = null)
        {
            var result = AutoDeck.Fix(recipe, Borrowed(recipe, cards), list, cards, released);
            if (result.Deck.Main.Count < DeckRules.MainMin)
            {
                var withStaples = Borrowed(recipe, cards);
                foreach (var code in Staples.Where(c => cards.Get(c) != null && withStaples.Count(c) == 0))
                    withStaples.Add(code, Rarity.Common, Collection.MaxCopies);
                var topped = AutoDeck.Fix(result.Deck, withStaples, list, cards, released);
                result.Deck = topped.Deck;
                result.Changes.AddRange(topped.Changes);
            }
            result.Deck.Name = NameFor(recipe.Name);
            return result;
        }

        public static Deck Build(Deck recipe, Banlist list, ICardDataProvider cards, Func<uint, bool>? released = null) => Adjust(recipe, list, cards, released).Deck;

        /// <summary>
        /// Three of every card in the recipe: what a loaner (and a custom loaner) may use besides your own cards. With
        /// <paramref name="yours"/>, your real copies come first, as they are (their printing and text included, the text
        /// timeline and printings carried over), so per-text counts read as <see cref="Collection.Assign"/> plays them in the
        /// duel (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 4.3); each recipe card is then topped up to three playable copies with
        /// borrowed ones, which are current text (a borrowed card plays its current text: spec 5.2).
        /// </summary>
        public static Collection Borrowed(Deck recipe, ICardDataProvider cards, Collection? yours = null)
        {
            var borrowed = new Collection();
            if (yours != null)
            {
                borrowed.Texts = yours.Texts;
                borrowed.Printings = yours.Printings;
                borrowed.CommonestOf = yours.CommonestOf;
                foreach (var c in yours.All)
                    borrowed.Insert(new CardCopy
                    {
                        Code = c.Code, Rarity = c.Rarity, Grade = c.Grade, Seed = c.Seed, Id = c.Id, Source = c.Source, Paid = c.Paid, Slab = c.Slab,
                        Floor = c.Floor, Submissions = c.Submissions, ReadyAt = c.ReadyAt, Text = c.Text, Set = c.Set, Printed = c.Printed,
                    });
            }
            foreach (var code in recipe.Main.Concat(recipe.Extra).Concat(recipe.Side).Select(c => Banlist.BaseCode(c, cards)).Distinct())
                if (borrowed.Count(code) < Collection.MaxCopies) borrowed.Add(code, Rarity.Common, Collection.MaxCopies - borrowed.Count(code));
            return borrowed;
        }

        /// <summary>
        /// A custom loaner: the recipe as adjusted for the list, saved as a deck of your own to edit, using the recipe's
        /// cards plus your collection. It stays a loaner (half DP, no Ban Passes): the "#loaner" note names its recipe.
        /// </summary>
        public static Deck Customize(Deck recipe, string title, Banlist list, ICardDataProvider cards)
        {
            var deck = Adjust(recipe, list, cards).Deck;
            deck.Name = $"{title} (custom loaner)";
            deck.Campaign = true;
            deck.Notes["loaner"] = recipe.Name;
            return deck;
        }

        /// <summary>A loaner deck name, or a custom loaner (a deck with a "#loaner" note).</summary>
        public static bool IsLoanerDeck(Deck deck) => IsLoaner(deck.Name) || deck.Note("loaner").Length > 0;

        /// <summary>The note on your edit of a Tag Cup partner's deck ("#partner &lt;character id&gt;").</summary>
        public const string PartnerNote = "partner";

        /// <summary>Whose deck <paramref name="deck"/> is an edit of (its "#partner" note), or "" when it's simply yours.</summary>
        public static string PartnerEditOf(Deck deck) => deck.Note(PartnerNote);

        /// <summary>
        /// A deck of yours: not a loaner (by name or a custom loaner's note) and not your edit of a partner's deck (a
        /// partner's edit is theirs to bring). The "Every Era" final's rounds take only these, and a Library cup's Format
        /// Master stamp counts only these (<see cref="Library.CountsForStamp"/>).
        /// </summary>
        public static bool IsYours(Deck deck) => !IsLoanerDeck(deck) && PartnerEditOf(deck).Length == 0;

        /// <summary>Loaner wins pay this share of the duel's DP (the circuit's prize is paid in full).</summary>
        public const double DpShare = 0.5;
    }

    /// <summary>
    /// Recipe packs, for a recipe you've unlocked: a targeted pack (9 cards from the recipe's own list, the ones you're
    /// missing first) or the expensive shortcut, buying every missing card outright at a price by rarity. Both keep to
    /// the shop's era when the campaign is era-limited.
    /// <para>
    /// Packs are priced by the recipe's band (a Master deck costs Master money), so a late deck stays a goal while wins
    /// pay more. The shortcut adds a service fee of what the packs would cost above the Beginner price, so buying
    /// outright is never the cheaper way to a late deck.
    /// </para>
    /// </summary>
    public static class RecipeShop
    {
        /// <summary>A Beginner or Novice recipe pack; the shortcut's service fee is what a band charges above this.</summary>
        public const int BasePackPrice = 300;

        /// <summary>A recipe pack's price for a deck of this band.</summary>
        public static int PackPrice(string band) => band switch
        {
            "Novice" => ShopPrices.NoviceRecipePack,
            "Intermediate" => 600,
            "Advanced" => 1000,
            "Expert" => 1500,
            "Master" or "Gauntlet" => 2500,
            "Library" => 1000,
            _ => BasePackPrice,
        };

        /// <summary>The pack price for a deck: its recipe's band (where you first meet it), or the base price.</summary>
        public static int PackPriceFor(string deck, Circuits circuits)
        {
            var recipe = RecipeBook.All(circuits).FirstOrDefault(r => r.Deck == deck);
            return recipe == null || recipe.Circuit < 0 || recipe.Circuit >= circuits.All.Count ? BasePackPrice : PackPrice(circuits.All[recipe.Circuit].Band);
        }

        /// <summary>The shortcut's service fee: the packs these copies would take, at what the band charges above the base price.</summary>
        public static int ServiceFee(int copies, int packPrice) =>
            copies <= 0 ? 0 : (copies + PackCatalog.PackSize - 1) / PackCatalog.PackSize * Math.Max(0, packPrice - BasePackPrice);

        /// <summary>DP for one copy bought outright, by the card's commonest printing.</summary>
        public static int CopyPrice(Rarity commonest) => commonest switch
        {
            Rarity.Common or Rarity.ShortPrint => 40,
            Rarity.Rare or Rarity.NormalParallelRare => 80,
            Rarity.SuperRare or Rarity.RareParallelRare or Rarity.StarfoilRare or Rarity.MosaicRare or Rarity.ShatterfoilRare => 150,
            Rarity.UltraRare or Rarity.UltimateRare or Rarity.GoldRare => 250,
            _ => 400, // Secret and rarer: only ever printed that way
        };

        /// <summary>Whether the shop can sell this card now (released by the shop's year when the campaign is era-limited).</summary>
        public static bool InEra(uint baseCode, Profile profile, int shopYear, PrintingIndex printings, ICardDataProvider cards)
        {
            if (!profile.EraLimited) return true;
            int year = printings.FirstYear(baseCode, cards.Get(baseCode)?.Alias ?? 0);
            return year != 0 && year <= shopYear;
        }

        /// <summary>The copies of each card the recipe still needs (base codes), within the era.</summary>
        public static Dictionary<uint, int> Missing(Deck recipe, Profile profile, Banlist list, int shopYear, PrintingIndex printings, ICardDataProvider cards) =>
            AutoDeck.Wants(recipe, list, cards)
                .Where(w => profile.Collection.Owned(w.Key) < w.Value && InEra(w.Key, profile, shopYear, printings, cards))
                .ToDictionary(w => w.Key, w => w.Value - profile.Collection.Owned(w.Key));

        /// <summary>
        /// What buying every missing card costs now (0 when nothing is missing): the cards by rarity, plus the service fee
        /// for a recipe of a band whose packs cost <paramref name="packPrice"/>.
        /// </summary>
        public static int CompletePrice(Deck recipe, Profile profile, Banlist list, int shopYear, PrintingIndex printings, ICardDataProvider cards,
            int packPrice = BasePackPrice)
        {
            var missing = Missing(recipe, profile, list, shopYear, printings, cards);
            return missing.Sum(m => m.Value * CopyPrice(printings.Commonest(m.Key, cards.Get(m.Key)?.Alias ?? 0)))
                   + ServiceFee(missing.Values.Sum(), packPrice);
        }

        /// <summary>Buys every missing card (if you have the DP). Returns the copies added, or 0.</summary>
        public static int Complete(Deck recipe, Profile profile, Banlist list, int shopYear, PrintingIndex printings, ICardDataProvider cards,
            int packPrice = BasePackPrice, string sink = Ledger.RecipeOutright)
        {
            int price = CompletePrice(recipe, profile, list, shopYear, printings, cards, packPrice);
            if (price <= 0 || !profile.Spend(sink, price)) return 0;
            int added = 0;
            var missing = Missing(recipe, profile, list, shopYear, printings, cards);
            int copiesIn = missing.Values.Sum(), fee = ServiceFee(copiesIn, packPrice);
            foreach (var (code, copies) in missing)
                for (int i = 0; i < copies; i++)
                {
                    var commonest = printings.Commonest(code, cards.Get(code)?.Alias ?? 0);
                    // What you paid for it: its price and its share of the service fee (the first copy takes the rounding).
                    int share = fee / Math.Max(1, copiesIn) + (added == 0 ? fee % Math.Max(1, copiesIn) : 0);
                    var print = CopyPrints.Shop(code, commonest, shopYear, printings, cards);
                    profile.Collection.Add(code, commonest, 1, CopySource.Shop, CopyPrice(commonest) + share, print?.SetCode ?? "", print is Printing p ? printings.DateOf(p) : "");
                    added++;
                }
            return added;
        }

        /// <summary>
        /// Opens a targeted pack you've paid for: 9 cards from the recipe's list, missing copies first (in a random
        /// order), then any of its cards. Extra copies past 3 pay DP like a normal pack's. Returns the pulls.
        /// </summary>
        public static List<(Pull pull, bool isNew)> OpenPack(Deck recipe, Profile profile, Banlist list, int shopYear, PrintingIndex printings,
            ICardDataProvider cards, Random rng, int packPrice = BasePackPrice)
        {
            if (!profile.Spend(Ledger.RecipePack, packPrice)) return new List<(Pull, bool)>();
            var pool = AutoDeck.Wants(recipe, list, cards).Keys.Where(c => InEra(c, profile, shopYear, printings, cards)).ToList();
            var missing = Missing(recipe, profile, list, shopYear, printings, cards)
                .SelectMany(m => Enumerable.Repeat(m.Key, m.Value)).OrderBy(_ => rng.Next()).ToList();
            var pulls = new List<(Pull, bool)>();
            int sold = 0;
            var codes = new List<uint>();
            for (int i = 0; i < PackCatalog.PackSize && pool.Count > 0; i++) codes.Add(i < missing.Count ? missing[i] : pool[rng.Next(pool.Count)]);
            var shares = Pricing.Shares(packPrice, codes.Select(c => CopyPrice(printings.Commonest(c, cards.Get(c)?.Alias ?? 0))).ToList());
            for (int i = 0; i < codes.Count; i++)
            {
                var code = codes[i];
                var rarity = printings.Commonest(code, cards.Get(code)?.Alias ?? 0);
                bool isNew = !profile.Collection.Owns(code);
                var print = CopyPrints.Shop(code, rarity, shopYear, printings, cards);
                sold += profile.Collection.AddPulled(code, rarity, CopySource.Shop, shares[i], print?.SetCode ?? "", print is Printing p ? printings.DateOf(p) : "").Dp;
                pulls.Add((new Pull(code, rarity), isNew));
            }
            profile.Earn(Ledger.RecipeRefund, Math.Min(sold, packPrice / 2));
            profile.PacksOpened++;
            return pulls;
        }
    }

    /// <summary>
    /// House packs (the code's selection packs; Master Duel's selection packs): Gus's repacks of the decks you're about
    /// to face, one per deck of a match not yet won in the current circuit and the next one (<see cref="Circuits.HouseMatches"/>),
    /// built from the cards that deck plays. Once you beat the deck its pack leaves and its recipe packs take over. They
    /// open like any booster; the code (SP-&lt;deck&gt;) and the ledger sink (<see cref="Ledger.SelectionPacks"/>) keep
    /// their old names.
    /// </summary>
    public static class SelectionPacks
    {
        /// <summary>The flat price (<see cref="ShopPrices.SelectionFlat"/>); <see cref="PriceFor"/> is the price by the deck's band.</summary>
        public static int Price => ShopPrices.SelectionFlat;

        /// <summary>A deck's house pack price: by the band of its recipe (where you first meet it), like <see cref="RecipeShop.PackPriceFor"/>.</summary>
        public static int PriceFor(string deck, Circuits circuits)
        {
            var recipe = RecipeBook.All(circuits).FirstOrDefault(r => r.Deck == deck);
            return ShopPrices.Selection(recipe == null || recipe.Circuit < 0 || recipe.Circuit >= circuits.All.Count ? null : circuits.All[recipe.Circuit].Band);
        }

        /// <summary>
        /// A booster of these cards (base codes), each at the rarities it was printed at by the shop's year (all its
        /// printings for a card with none by then, or with no <paramref name="profile"/>), within the shop's era, at
        /// <paramref name="price"/> (<see cref="PriceFor"/>; the flat price when left out). It's named
        /// <paramref name="name"/> (the shop's `house-pack:name` line, filled), else "House Pack: &lt;title&gt;", and has
        /// no year.
        /// </summary>
        public static PackSet Build(string title, string code, IEnumerable<uint> cardPool, Profile? profile, int shopYear, PrintingIndex printings,
            ICardDataProvider cards, int? price = null, string? name = null)
        {
            var set = new PackSet
            {
                Info = new SetInfo { Code = code, Name = name ?? $"House Pack: {title}", Date = "" }, Price = price ?? Price, Sink = Ledger.SelectionPacks,
                StampYear = profile == null ? 9999 : shopYear,
            };
            foreach (var c in cardPool.Distinct())
            {
                var info = cards.Get(c);
                if (info == null) continue;
                if (profile != null && !RecipeShop.InEra(c, profile, shopYear, printings, cards)) continue;
                // The card's commonest printing, plus its rarest up to Ultra for the foil slot: of the printings out by the
                // shop's year, so a 2010 shop has no Shatterfoil (2013) in its house packs, as its shelves don't.
                var all = printings.Of(c, info.Alias);
                var dated = profile == null ? all : all.Where(p => YearOf(p, printings) is int y && y > 0 && y <= shopYear).ToArray();
                if (dated.Length == 0) dated = all;
                var rarity = dated.Length == 0 ? Rarity.Common : dated.Min(p => p.Rarity);
                // Rarest first, as PrintingIndex keeps them: the first up to Ultra, else the least rare (RarestUpTo's rule).
                var rarest = dated.Length == 0 ? Rarity.Common : dated.Select(p => (Rarity?)p.Rarity).FirstOrDefault(r => r <= Rarity.UltraRare) ?? dated[dated.Length - 1].Rarity;
                foreach (var r in new[] { rarity, rarest }.Distinct())
                {
                    if (!set.Pool.TryGetValue(r, out var list)) set.Pool[r] = list = new List<uint>();
                    list.Add(c);
                }
            }
            return set;
        }

        /// <summary>A printing's set's year (0 when the set or its date is unknown).</summary>
        private static int YearOf(Printing p, PrintingIndex printings) =>
            printings.Sets.TryGetValue(PrintingIndex.SetPrefix(p.SetCode), out var set) && set.Date.Length >= 4 && int.TryParse(set.Date.Substring(0, 4), out var y) ? y : 0;

        /// <summary>Whether a pack is a house pack (its code starts "SP-").</summary>
        public static bool IsHousePack(PackSet set) => set.Sink == Ledger.SelectionPacks || set.Info.Code.StartsWith("SP-", StringComparison.Ordinal);
    }
}
