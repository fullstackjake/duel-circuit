using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// Ban Passes: a few slots per deck that let your own campaign deck break the circuit's ban list, one copy each (a
    /// Forbidden card at 1, or one more copy of a Limited or Semi-Limited card, never past 3). You start with 3 and earn a
    /// 4th and 5th by clearing the Advanced and Expert bands; that's all there are (<see cref="Max"/>), and DP doesn't buy them. You still have to own the card and, in an era-limited
    /// campaign, it still has to be released; loaners can't use them; and a short list of cards that win on their own
    /// (campaign/no-pass.txt) can never be passed. A deck keeps its passes as a "#passes" note (card ids, one per copy).
    /// </summary>
    public static class BanPasses
    {
        public const int Starting = 3;
        public const int Max = 5;
        /// <summary>Bands whose clearing earns one more pass each.</summary>
        public static readonly string[] EarnedBy = { "Advanced", "Expert" };

        /// <summary>How many passes a deck may use now: the starting ones and one per band named above.</summary>
        public static int Allowance(Circuits circuits, Profile profile) =>
            Starting + EarnedBy.Count(band =>
            {
                var inBand = Enumerable.Range(0, circuits.All.Count).Where(i => circuits.All[i].Band == band).ToList();
                return inBand.Count > 0 && inBand.All(i => circuits.IsCleared(i, profile));
            });

        /// <summary>The deck's passes: one card id per copy passed.</summary>
        public static List<uint> Of(Deck deck) =>
            deck.Note("passes").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => uint.TryParse(t, out var c) ? c : 0).Where(c => c != 0).ToList();

        public static void Set(Deck deck, IEnumerable<uint> passes)
        {
            var list = passes.ToList();
            if (list.Count == 0) deck.Notes.Remove("passes");
            else deck.Notes["passes"] = string.Join(",", list);
        }

        /// <summary>
        /// The ban list as it applies to this deck: each pass (up to the allowance, skipping no-pass cards) allows one
        /// more copy of its card, never past 3. A loaner gets the list as it is.
        /// </summary>
        public static Banlist For(Deck deck, Banlist list, int allowance, ISet<uint> noPass, ICardDataProvider cards)
        {
            var passes = Of(deck).Select(c => Banlist.BaseCode(c, cards)).Where(c => !noPass.Contains(c)).Take(allowance).ToList();
            if (passes.Count == 0 || Loaners.IsLoaner(deck.Name) || deck.Note("loaner").Length > 0) return list;
            var bent = new Banlist { Name = list.Name, Whitelist = list.Whitelist, Limits = new Dictionary<uint, int>(list.Limits) };
            foreach (var group in passes.GroupBy(c => c))
                bent.Limits[group.Key] = Math.Min(Collection.MaxCopies, list.Allowed(group.Key, cards) + group.Count());
            return bent;
        }

        /// <summary>Passes the deck actually uses: passed cards it runs more copies of than the plain list allows.</summary>
        public static int Used(Deck deck, Banlist list, ICardDataProvider cards, ISet<uint>? noPass = null)
        {
            var passes = Of(deck).Select(c => Banlist.BaseCode(c, cards)).Where(c => noPass?.Contains(c) != true)
                .GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
            var counts = deck.Main.Concat(deck.Extra).Concat(deck.Side).Select(c => Banlist.BaseCode(c, cards)).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
            return passes.Sum(p => Math.Min(p.Value, Math.Max(0, counts.GetValueOrDefault(p.Key) - list.Allowed(p.Key, cards))));
        }

        /// <summary>The cards that can never be passed (campaign/no-pass.txt: one card name per line, # for comments).</summary>
        public static HashSet<uint> LoadNoPass(string path, ICardDataProvider cards)
        {
            var set = new HashSet<uint>();
            if (!File.Exists(path)) return set;
            var byName = cards.All.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Code, StringComparer.OrdinalIgnoreCase);
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (byName.TryGetValue(line, out var code)) set.Add(Banlist.BaseCode(code, cards));
            }
            return set;
        }
    }
}
