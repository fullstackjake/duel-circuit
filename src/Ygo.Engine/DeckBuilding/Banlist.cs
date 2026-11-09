using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ygo.Engine.DeckBuilding
{
    /// <summary>
    /// A Forbidden/Limited list in EDOPro's .lflist.conf format:
    /// "!Name" starts a list, "&lt;card id&gt; &lt;copies&gt; --comment" sets a limit, "$whitelist" means only
    /// listed cards are legal (e.g. GOAT format), and "#" lines are comments.
    /// </summary>
    public sealed class Banlist
    {
        public string Name = "";
        public Dictionary<uint, int> Limits = new Dictionary<uint, int>();
        /// <summary>Only listed cards are legal; everything else counts as forbidden.</summary>
        public bool Whitelist;

        /// <summary>No restrictions beyond the normal 3 copies.</summary>
        public static Banlist None => new Banlist { Name = "No limits" };

        public bool IsUnrestricted => Limits.Count == 0 && !Whitelist;

        /// <summary>Copies allowed of a card (aliases count as their base card).</summary>
        public int Allowed(uint code, ICardDataProvider cards)
        {
            var key = BaseCode(code, cards);
            if (Limits.TryGetValue(key, out var n) || Limits.TryGetValue(code, out n)) return n;
            return Whitelist ? 0 : 3;
        }

        public static uint BaseCode(uint code, ICardDataProvider cards)
        {
            var info = cards.Get(code);
            return info != null && info.Alias != 0 && cards.Get(info.Alias) != null ? info.Alias : code;
        }

        private static readonly string[] Months = { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };

        /// <summary>
        /// The month a list took effect, "yyyy-mm", read from its name ("TCG March 2011", "2005.4 GOAT", "2026.10 TCG");
        /// "" when the name has no date.
        /// </summary>
        public string Date
        {
            get
            {
                var lower = Name.ToLowerInvariant();
                var dotted = System.Text.RegularExpressions.Regex.Match(lower, @"\b(\d{4})\.(\d{1,2})\b");
                if (dotted.Success) return $"{dotted.Groups[1].Value}-{int.Parse(dotted.Groups[2].Value):00}";
                var year = System.Text.RegularExpressions.Regex.Match(lower, @"\b(19|20)\d{2}\b");
                if (!year.Success) return "";
                int month = System.Array.FindIndex(Months, m => lower.Contains(m)) + 1;
                return $"{year.Value}-{(month > 0 ? month : 12):00}";
            }
        }

        public static List<Banlist> Parse(string text)
        {
            var lists = new List<Banlist>();
            Banlist? current = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("!"))
                {
                    current = new Banlist { Name = line.Substring(1).Trim() };
                    lists.Add(current);
                    continue;
                }
                if (current == null) continue;
                if (line.StartsWith("$whitelist")) { current.Whitelist = true; continue; }
                var parts = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && uint.TryParse(parts[0], out var code) && int.TryParse(parts[1], out var limit))
                    current.Limits[code] = limit;
            }
            return lists;
        }

        /// <summary>All lists from *.conf files in the given folders (missing folders are skipped), newest names first.</summary>
        public static List<Banlist> LoadFolders(IEnumerable<string> folders)
        {
            var all = new List<Banlist>();
            foreach (var folder in folders.Where(Directory.Exists))
                foreach (var file in Directory.GetFiles(folder, "*.conf").OrderBy(f => f))
                    all.AddRange(Parse(File.ReadAllText(file)));
            return all;
        }
    }
}
