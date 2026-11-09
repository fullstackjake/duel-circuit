using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// What the shop's tags know for one visit (SHOP-REWORK-SPEC section 2): your wishlist, the house-pack opponents'
    /// cards and key cards, and the cards of your own decks. Built once per visit.
    /// </summary>
    public sealed class TagContext
    {
        internal HashSet<uint> Wanted = new HashSet<uint>();
        internal string WantedFor = "";
        /// <summary>Base code -> the names of the house-pack opponents who play it, in match order.</summary>
        internal Dictionary<uint, List<string>> PlayedBy = new Dictionary<uint, List<string>>();
        /// <summary>The house-pack opponents' names, in match order.</summary>
        internal List<string> Players = new List<string>();
        /// <summary>Base code -> the id of the first house-pack opponent it's a key card of.</summary>
        internal Dictionary<uint, string> KeyOf = new Dictionary<uint, string>();
        /// <summary>Base code -> the title of the first of your decks it's in (the active deck first).</summary>
        internal Dictionary<uint, string> InDeck = new Dictionary<uint, string>();

        /// <summary>
        /// Cards "plays this" and key cards leave out: <see cref="Loaners.Staples"/> (the modern hand traps and removal)
        /// plus the generic staples of every era that half the opponents run (Monster Reborn, Pot of Greed, Mirror Force...),
        /// so a tag on them would say nothing about a set or a pull. A deck's own engine is never on it.
        /// </summary>
        public static readonly IReadOnlyCollection<uint> Staples = new HashSet<uint>(Loaners.Staples.Concat(new uint[]
        {
            83764718, // Monster Reborn
            55144522, // Pot of Greed
            79571449, // Graceful Charity
            44763025, // Delinquent Duo
            19613556, // Heavy Storm
            53129443, // Dark Hole
            12580477, // Raigeki
            18144506, // Harpie's Feather Duster
            4031928,  // Change of Heart
            45986603, // Snatch Steal
            87910978, // Brain Control
            70828912, // Premature Burial
            72302403, // Swords of Revealing Light
            26202165, // Sangan
            5318639,  // Mystical Space Typhoon
            43898403, // Twin Twisters
            81439173, // Foolish Burial
            73628505, // Terraforming
            70368879, // Upstart Goblin
            98645731, // Pot of Duality
            35261759, // Pot of Desires
            84211599, // Pot of Prosperity
            25311006, // Triple Tactics Talent
            44095762, // Mirror Force
            97077563, // Call of the Haunted
            53582587, // Torrential Tribute
            94192409, // Compulsory Evacuation Device
            29401950, // Bottomless Trap Hole
            41420027, // Solemn Judgment
            84749824, // Solemn Warning
            23434538, // Maxx "C"
            42141493, // Mulcharmy Fuwalos
        }));

        /// <summary>
        /// <paramref name="yourDecks"/>: the active deck and your campaign decks (loaners are left out here);
        /// <paramref name="loadDeck"/>: a deck by name (the opponents'); <paramref name="deckTitle"/>: a deck's name as
        /// shown; <paramref name="scouting"/>: the written scouting reports (null: every report is put together from its deck).
        /// "Plays this" counts the Main and Extra Decks of every match not yet won in the current and the next circuit (the
        /// house-pack opponents; CPUs don't side), minus <see cref="Staples"/>; a key card is one of a deck's
        /// scouting keys or its character's signature, never a staple. Nothing plays in the post-game.
        /// </summary>
        public static TagContext Build(Profile profile, Circuits circuits, IEnumerable<Deck> yourDecks, Func<string, Deck?> loadDeck,
            Func<string, string> deckTitle, ICardDataProvider cards, Scouting? scouting = null)
        {
            var context = new TagContext { Wanted = profile.Wanted() };
            if (profile.WishlistFor.Length > 0) context.WantedFor = deckTitle(profile.WishlistFor);
            var staples = Staples;
            Dictionary<string, uint>? byName = null;
            uint Named(string name)
            {
                byName ??= cards.All.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => Banlist.BaseCode(g.OrderBy(c => c.Alias != 0).First().Code, cards), StringComparer.OrdinalIgnoreCase);
                return byName.TryGetValue(name, out var code) ? code : 0;
            }
            scouting ??= Scouting.Empty;
            foreach (var ci in circuits.SelectionPackCircuits(profile))
                for (int mi = 0; mi < circuits.All[ci].Matches.Count; mi++)
                {
                    if (profile.HasWon(ci, mi)) continue;
                    var m = circuits.All[ci].Matches[mi];
                    var deck = loadDeck(m.Deck);
                    if (deck == null) continue;
                    if (!context.Players.Contains(m.Character.Name)) context.Players.Add(m.Character.Name);
                    foreach (var code in deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, cards)).Distinct())
                    {
                        if (staples.Contains(code)) continue;
                        if (!context.PlayedBy.TryGetValue(code, out var who)) context.PlayedBy[code] = who = new List<string>();
                        if (!who.Contains(m.Character.Name)) who.Add(m.Character.Name);
                    }
                    var keys = scouting.For(m.Deck, deck, cards).Keys.Select(Named).ToList();
                    if (m.Character.Signature.Length > 0) keys.Add(Named(m.Character.Signature));
                    foreach (var key in keys)
                        if (key != 0 && !staples.Contains(key) && !context.KeyOf.ContainsKey(key)) context.KeyOf[key] = m.Character.Id;
                }
            foreach (var deck in yourDecks.Where(d => !Loaners.IsLoanerDeck(d)).GroupBy(d => d.Name).Select(g => g.First())
                         .OrderBy(d => d.Name == profile.Deck ? 0 : 1).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                var title = deckTitle(deck.Name);
                foreach (var code in deck.Main.Concat(deck.Extra).Concat(deck.Side).Select(c => Banlist.BaseCode(c, cards)))
                    if (!context.InDeck.ContainsKey(code)) context.InDeck[code] = title;
            }
            return context;
        }
    }

    /// <summary>A card's tags: on your wishlist, a house-pack opponent's key card, the opponents who play it, the deck of yours it's in.</summary>
    public sealed class PackCardTags
    {
        /// <summary><see cref="Key"/>: a key card of a house deck (scouting keys or a signature), for the pull:opponent line; <see cref="KeyOf"/> is that character's id.</summary>
        public bool Wanted, Key;
        public string WantedFor = "", InDeck = "", KeyOf = "";
        public List<string> PlayedBy = new List<string>();

        public bool Any => Wanted || PlayedBy.Count > 0 || InDeck.Length > 0;
    }

    /// <summary>The shop's tags on pulls and sets (SHOP-REWORK-SPEC section 2). Set progress is <see cref="CardUnlocks.Progress"/>.</summary>
    public static class PackTags
    {
        public static PackCardTags For(uint baseCode, TagContext context) => new PackCardTags
        {
            Wanted = context.Wanted.Contains(baseCode),
            WantedFor = context.WantedFor,
            Key = context.KeyOf.ContainsKey(baseCode),
            KeyOf = context.KeyOf.TryGetValue(baseCode, out var id) ? id : "",
            InDeck = context.InDeck.TryGetValue(baseCode, out var deck) ? deck : "",
            PlayedBy = context.PlayedBy.TryGetValue(baseCode, out var who) ? who.ToList() : new List<string>(),
        };

        /// <summary>
        /// A set's row counts: its distinct cards you want, its distinct cards the house-pack opponents play, and the
        /// first of them (by match order) who plays any ("Tink plays 2").
        /// </summary>
        public static (int wanted, int played, string firstPlayer) Count(PackSet set, TagContext context)
        {
            var codes = set.Pool.Values.SelectMany(c => c).Distinct().ToList();
            var players = new HashSet<string>(codes.Where(context.PlayedBy.ContainsKey).SelectMany(c => context.PlayedBy[c]));
            string first = context.Players.FirstOrDefault(players.Contains) ?? "";
            return (codes.Count(context.Wanted.Contains), codes.Count(context.PlayedBy.ContainsKey), first);
        }

        /// <summary>The one line under a revealed card, by priority: "Wanted for Tearlaments", "Tink plays this" ("Tink and 2 more play this"), "In Warriors"; "" for none.</summary>
        public static string Line(PackCardTags tags)
        {
            if (tags.Wanted) return tags.WantedFor.Length > 0 ? $"Wanted for {tags.WantedFor}" : "Wanted";
            if (tags.PlayedBy.Count == 1) return $"{tags.PlayedBy[0]} plays this";
            if (tags.PlayedBy.Count > 1) return $"{tags.PlayedBy[0]} and {tags.PlayedBy.Count - 1} more play this";
            if (tags.InDeck.Length > 0) return $"In {tags.InDeck}";
            return "";
        }
    }
}
