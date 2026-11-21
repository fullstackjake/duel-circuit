using System;
using System.Collections.Generic;
using System.Linq;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// A deck's strength as five stars (THE-CLIMB-SPEC section 7), from its measured Elo (<see cref="DeckRatings"/>: the
    /// loaded ratings, peak builds merged). Fixed lines, so a re-rating moves a deck's stars only when it crosses one: under
    /// 1,350 one star, 1,350-1,499 two, 1,500-1,649 three, 1,650-1,799 four, 1,800 and up five. An unrated deck has none (0).
    /// </summary>
    public static class DeckStrength
    {
        public const int MaxStars = 5;

        /// <summary>The Elo each star from the second on starts at.</summary>
        public static readonly double[] Lines = { 1350, 1500, 1650, 1800 };

        /// <summary>The stars for an Elo: 1 to <see cref="MaxStars"/>.</summary>
        public static int Stars(double elo) => 1 + Lines.Count(line => elo >= line);

        /// <summary>A deck's stars, or 0 when it isn't rated.</summary>
        public static int StarsFor(IReadOnlyDictionary<string, double> ratings, string deck) =>
            ratings.TryGetValue(deck, out var elo) ? Stars(elo) : 0;

        /// <summary>The stars of the decks' mean Elo (the rated ones; a Tag Cup pair), or 0 when none is rated.</summary>
        public static int StarsFor(IReadOnlyDictionary<string, double> ratings, IEnumerable<string> decks)
        {
            double elo = Elo(ratings, decks, strongest: false);
            return elo > 0 ? Stars(elo) : 0;
        }

        /// <summary>The rated decks' Elo: the <paramref name="strongest"/> one's, or their mean; 0 when none is rated.</summary>
        public static double Elo(IReadOnlyDictionary<string, double> ratings, IEnumerable<string> decks, bool strongest)
        {
            var rated = decks.Where(d => d != null && ratings.ContainsKey(d)).Select(d => ratings[d]).ToList();
            if (rated.Count == 0) return 0;
            return strongest ? rated.Max() : rated.Average();
        }

        /// <summary>The mean Elo of every rated match deck in the band's circuits (each match once), or 0 when none is rated.</summary>
        public static double BandMean(Circuits circuits, IReadOnlyDictionary<string, double> ratings, string band) =>
            Elo(ratings, circuits.All.Where(c => c.Band.Equals(band, StringComparison.OrdinalIgnoreCase)).SelectMany(c => c.Matches).Select(m => m.Deck), strongest: false);

        /// <summary>
        /// Where a deck of this Elo is best met: the first band, in map order, whose <see cref="BandMean"/> is at least the Elo
        /// minus 25. The last band (the Gauntlet's), or none, names the campaign's last circuit ("Tier 0 Gauntlet").
        /// </summary>
        public static string BestAfterBand(Circuits circuits, IReadOnlyDictionary<string, double> ratings, double elo)
        {
            var bands = circuits.All.Select(c => c.Band).Where(b => b.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string last = circuits.All.Count > 0 ? circuits.All[circuits.All.Count - 1].Name : "";
            for (int i = 0; i < bands.Count - 1; i++)
                if (BandMean(circuits, ratings, bands[i]) >= elo - 25) return bands[i];
            return last;
        }
    }
}
