using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// Measured deck strength: campaign/deck-ratings.txt, the `--rate-decks` report (bot-vs-bot Elo by deck). Its table
    /// lines read "  12. thunder-dragon   Elo  1693   won 73% ..."; everything else in the file is ignored. The one
    /// place the format is read (the harness studies, the campaign's advice and brackets all use it).
    /// </summary>
    public static class DeckRatings
    {
        public const string FileName = "deck-ratings.txt";
        /// <summary>
        /// The anchored ratings (`--rate-anchored --write`): decks rated against the frozen pool without moving it, the No
        /// Limits builds and the alley's authored decks. Merged after <see cref="FileName"/>.
        /// </summary>
        public const string PeakFileName = "peak-ratings.txt";
        /// <summary>What an unrated deck counts as where a rating is needed (bracket seeding and odds).</summary>
        public const double Unrated = 1500;

        private static readonly Regex Row = new Regex(@"^\s*\d+\.\s+(\S+)\s+Elo\s+(\d+)");

        /// <summary>Deck name -> Elo, from the report's text (a deck listed twice keeps its last rating).</summary>
        public static Dictionary<string, double> Parse(string text)
        {
            var ratings = new Dictionary<string, double>();
            foreach (var line in text.Split('\n'))
            {
                var m = Row.Match(line);
                if (m.Success) ratings[m.Groups[1].Value] = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            return ratings;
        }

        /// <summary>The ratings in the files, merged in order (a later file's rating wins; a missing file is skipped).</summary>
        public static Dictionary<string, double> Load(params string[] paths)
        {
            var ratings = new Dictionary<string, double>();
            foreach (var path in paths)
                if (File.Exists(path))
                    foreach (var (deck, elo) in Parse(File.ReadAllText(path))) ratings[deck] = elo;
            return ratings;
        }

        /// <summary>A deck's rating, or <paramref name="unrated"/>.</summary>
        public static double Of(IReadOnlyDictionary<string, double> ratings, string deck, double unrated = Unrated) =>
            ratings.TryGetValue(deck, out var elo) ? elo : unrated;
    }

    /// <summary>
    /// Measured head-to-head results between decks (campaign/matchups.txt, once a study writes it): lines of
    /// "&lt;deck a&gt; &lt;deck b&gt; &lt;a's wins&gt; &lt;played&gt;" (# comments). <see cref="Brackets.Odds"/> blends them with the Elo odds.
    /// </summary>
    public sealed class Matchups
    {
        public const string FileName = "matchups.txt";
        private readonly Dictionary<(string, string), (int wins, int played)> _results = new Dictionary<(string, string), (int, int)>();

        public static Matchups Empty => new Matchups();

        public int Count => _results.Count;

        public void Add(string a, string b, int wins, int played)
        {
            if (played <= 0 || a == b) return; // a mirror is an even match by definition
            var old = Get(a, b);
            _results[(a, b)] = (old.wins + wins, old.played + played);
            _results[(b, a)] = (old.played + played - old.wins - wins, old.played + played);
        }

        /// <summary>A's wins against B and the duels played (0, 0 when never measured).</summary>
        public (int wins, int played) Get(string a, string b) => _results.TryGetValue((a, b), out var r) ? r : (0, 0);

        public static Matchups Parse(string text)
        {
            var m = new Matchups();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                var hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash).Trim();
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4 && int.TryParse(parts[2], out var wins) && int.TryParse(parts[3], out var played) && wins >= 0 && wins <= played)
                    m.Add(parts[0], parts[1], wins, played);
            }
            return m;
        }

        public static Matchups Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
    }
}
