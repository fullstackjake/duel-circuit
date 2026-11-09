using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Ygo.Engine.Campaign
{
    /// <summary>One measured way to play a hard match: your deck, your partner and theirs, and how often it won.</summary>
    public sealed class MeasuredOption
    {
        /// <summary>Your deck: <see cref="YourDeck"/> for your own (measured with the starter), else a recipe to borrow.</summary>
        public string Deck = YourDeck;
        /// <summary>Tag duels: the partner's character id and the deck they bring (a recipe for Kit); "" one on one.</summary>
        public string Partner = "", PartnerDeck = "";
        /// <summary>Wins in 100, overall and by who took the first turn (-1: not measured).</summary>
        public int Percent, YouFirst = -1, YouSecond = -1;
        public int Games;

        public const string YourDeck = "yours";
        public bool Borrows => Deck != YourDeck;
    }

    /// <summary>
    /// What `--wall-study` measured at the campaign's hard matches (campaign/wall-advice.txt), best first, keyed like the
    /// save keys its matches (<see cref="Circuits.TagKey"/>, <see cref="GuestList.Key"/>). The Stuck? advice picks the best
    /// option the player actually has, so it names the deck and partner that did best in that very match.
    /// </summary>
    public sealed class WallAdvice
    {
        private readonly Dictionary<string, List<MeasuredOption>> _walls;

        public WallAdvice(Dictionary<string, List<MeasuredOption>> walls) => _walls = walls;

        public static WallAdvice Empty => new WallAdvice(new Dictionary<string, List<MeasuredOption>>());

        public IReadOnlyCollection<string> Keys => _walls.Keys;

        /// <summary>The measured options at a match, best first (empty when it wasn't measured).</summary>
        public IReadOnlyList<MeasuredOption> For(string matchKey) =>
            _walls.TryGetValue(matchKey, out var list) ? list : (IReadOnlyList<MeasuredOption>)Array.Empty<MeasuredOption>();

        public void Set(string matchKey, IEnumerable<MeasuredOption> options) =>
            _walls[matchKey] = options.OrderByDescending(o => o.Percent).ToList();

        /// <summary>Adds these options to a match, replacing the same deck, partner and partner deck; keeps the rest.</summary>
        public void Merge(string matchKey, IEnumerable<MeasuredOption> options)
        {
            var list = _walls.TryGetValue(matchKey, out var old) ? old.ToList() : new List<MeasuredOption>();
            foreach (var o in options)
            {
                list.RemoveAll(x => x.Deck == o.Deck && x.Partner == o.Partner && x.PartnerDeck == o.PartnerDeck);
                list.Add(o);
            }
            Set(matchKey, list);
        }

        /// <summary>
        /// "wall: key" starts a match; each "option:" line is "percent | deck=... | partner=... | partnerdeck=... |
        /// first=... | second=... | games=...". Lines starting with # are comments.
        /// </summary>
        public static WallAdvice Parse(string text)
        {
            var walls = new Dictionary<string, List<MeasuredOption>>();
            List<MeasuredOption>? current = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("wall:")) { walls[line.Substring(5).Trim()] = current = new List<MeasuredOption>(); continue; }
                if (!line.StartsWith("option:") || current == null) continue;
                var parts = line.Substring(7).Split('|').Select(p => p.Trim()).ToArray();
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pct)) continue;
                var o = new MeasuredOption { Percent = pct };
                foreach (var part in parts.Skip(1))
                {
                    var eq = part.IndexOf('=');
                    if (eq < 0) continue;
                    var value = part.Substring(eq + 1).Trim();
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n);
                    switch (part.Substring(0, eq).Trim())
                    {
                        case "deck": o.Deck = value; break;
                        case "partner": o.Partner = value; break;
                        case "partnerdeck": o.PartnerDeck = value; break;
                        case "first": o.YouFirst = n; break;
                        case "second": o.YouSecond = n; break;
                        case "games": o.Games = n; break;
                    }
                }
                current.Add(o);
            }
            foreach (var key in walls.Keys.ToList()) walls[key] = walls[key].OrderByDescending(o => o.Percent).ToList();
            return new WallAdvice(walls);
        }

        public static WallAdvice Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;

        public void Save(string path, string header)
        {
            var sb = new StringBuilder();
            foreach (var line in header.Split('\n')) sb.AppendLine("# " + line);
            foreach (var key in _walls.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                sb.AppendLine();
                sb.AppendLine($"wall: {key}");
                foreach (var o in _walls[key])
                {
                    var partner = o.Partner.Length > 0 ? $" | partner={o.Partner} | partnerdeck={o.PartnerDeck}" : "";
                    sb.AppendLine($"option: {o.Percent} | deck={o.Deck}{partner} | first={o.YouFirst} | second={o.YouSecond} | games={o.Games}");
                }
            }
            File.WriteAllText(path, sb.ToString());
        }
    }
}
