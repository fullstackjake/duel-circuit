using System.Collections.Generic;
using System.Linq;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// The Library's cosmetics, its DP sinks (docs/dev/POST-GAME-PROPOSAL-2026-10-30.md, Phase 3): an era mat for your side of
    /// the board for each cleared cup, and an engraving on a cleared cup's binder naming a deck of yours that won there
    /// (a gold plate once the cup is stamped, silver before). Neither changes a duel, neither is ever sold back, and both
    /// cost one flat price whatever the era (the Economy Designer's: 10 mats and 10 plates are about 32% of a champion's
    /// savings). Mats are painted in code from campaign/mats.txt (ids "era-&lt;cup id&gt;").
    /// </summary>
    public static class Cosmetics
    {
        public const int MatPrice = 8000, EngravePrice = 2500;
        /// <summary>The <see cref="Profile.DpSpent"/> sinks.</summary>
        public const string MatSink = Ledger.Mats, EngraveSink = Ledger.Engrave;

        /// <summary>A cup's era mat id ("era-goat-cup").</summary>
        public static string MatId(Circuit cup) => "era-" + Library.Id(cup);

        /// <summary>The cup an era mat belongs to, or -1.</summary>
        public static int CupOfMat(Library library, string id) =>
            Enumerable.Range(0, library.Cups.Count).Where(c => MatId(library.Cups[c]) == id).DefaultIfEmpty(-1).First();

        /// <summary>Why a cup's era mat can't be bought now, or null when it can.</summary>
        public static string? MatBlocked(Profile profile, Library library, int cup)
        {
            var c = library.Cups[cup];
            if (profile.Mats.Contains(MatId(c))) return "you own it";
            if (!library.IsCleared(cup, profile)) return $"clear the {c.Name} first";
            if (profile.Dp < MatPrice) return $"it costs {MatPrice:N0} DP and you have {profile.Dp:N0}";
            return null;
        }

        /// <summary>Buys a cup's era mat and puts it on your side at once. False if it can't be bought.</summary>
        public static bool BuyMat(Profile profile, Library library, int cup)
        {
            if (MatBlocked(profile, library, cup) != null || !profile.Spend(MatSink, MatPrice)) return false;
            profile.Mats.Add(MatId(library.Cups[cup]));
            profile.Mat = MatId(library.Cups[cup]);
            return true;
        }

        /// <summary>Puts a mat you own on your side ("" for the free space mat). False for one you don't own.</summary>
        public static bool PickMat(Profile profile, string id)
        {
            if (id.Length > 0 && !profile.Mats.Contains(id)) return false;
            profile.Mat = id;
            return true;
        }

        /// <summary>Your decks that have won a match at a cup (no loaners): the ones its binder can be engraved with.</summary>
        public static IReadOnlyCollection<string> EngraveDecks(Profile profile, Library library, int cup) =>
            profile.LibraryDecks.TryGetValue(Library.Id(library.Cups[cup]), out var decks) ? decks : (IReadOnlyCollection<string>)new string[0];

        /// <summary>What a cup's binder is engraved with (a deck's title), or null.</summary>
        public static string? Engraving(Profile profile, Library library, int cup) =>
            profile.Engravings.TryGetValue(Library.Id(library.Cups[cup]), out var title) ? title : null;

        /// <summary>
        /// Why a cup's binder can't be engraved with <paramref name="deck"/> (titled <paramref name="title"/>) now, or null
        /// when it can. Engraving again with another deck costs the full price again.
        /// </summary>
        public static string? EngraveBlocked(Profile profile, Library library, int cup, string deck, string title)
        {
            var c = library.Cups[cup];
            if (!library.IsCleared(cup, profile)) return $"clear the {c.Name} first";
            if (!EngraveDecks(profile, library, cup).Contains(deck)) return $"win a match at the {c.Name} with {title} first (a deck of yours, not a loaner)";
            if (Engraving(profile, library, cup) == title) return $"it's engraved with {title}";
            if (profile.Dp < EngravePrice) return $"it costs {EngravePrice:N0} DP and you have {profile.Dp:N0}";
            return null;
        }

        /// <summary>Engraves a cleared cup's binder with a deck of yours that won there. False if it can't be engraved.</summary>
        public static bool Engrave(Profile profile, Library library, int cup, string deck, string title)
        {
            if (EngraveBlocked(profile, library, cup, deck, title) != null || !profile.Spend(EngraveSink, EngravePrice)) return false;
            profile.Engravings[Library.Id(library.Cups[cup])] = title;
            return true;
        }
    }
}
