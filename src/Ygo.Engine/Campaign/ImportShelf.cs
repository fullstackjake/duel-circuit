using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ygo.Engine.Data;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// The card shop's OCG import shelf: themed packs of cards the OCG printed and the TCG never did
    /// (campaign/ocg-imports.txt, written by the harness's `--ocg-imports`). It opens with the Expert band, packs cost
    /// <see cref="Price"/> DP (a use for the late campaign's DP), and it stays closed in era-limited play, which is about
    /// the TCG of its time. Import packs open like any booster.
    /// </summary>
    public sealed class ImportShelf
    {
        public const int Price = 1500;
        /// <summary>Import packs' set codes start with this ("OCG-UNCHAI").</summary>
        public const string Prefix = "OCG-";

        public IReadOnlyList<PackSet> Packs { get; }
        /// <summary>Every OCG-only card on the shelf (base codes).</summary>
        public IReadOnlyCollection<uint> Cards => _cards;
        private readonly HashSet<uint> _cards;

        public ImportShelf(IReadOnlyList<PackSet> packs)
        {
            Packs = packs;
            _cards = packs.SelectMany(p => p.Pool.Values.SelectMany(c => c)).ToHashSet();
        }

        public static ImportShelf Empty => new ImportShelf(Array.Empty<PackSet>());

        public bool IsOcgOnly(uint baseCode) => _cards.Contains(baseCode);

        public static bool IsImport(PackSet set) => set.Info.Code.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>Open from the Expert band on (any Expert, Master or Gauntlet circuit unlocked), never in era-limited play.</summary>
        public static bool IsOpen(Profile profile, Circuits circuits) =>
            !profile.EraLimited && Enumerable.Range(0, circuits.All.Count).Any(i => circuits.IsLateBand(i) && circuits.IsUnlocked(i, profile));

        /// <summary>Reads "pack: CODE | Name" lines, each followed by its "card: id Rarity" lines (# starts a comment).</summary>
        public static ImportShelf Parse(string text, ICardDataProvider cards)
        {
            var packs = new List<PackSet>();
            PackSet? current = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Split('#')[0].Trim();
                if (line.StartsWith("pack:"))
                {
                    var parts = line.Substring(5).Split('|').Select(p => p.Trim()).ToArray();
                    if (parts.Length < 2) continue;
                    current = new PackSet { Info = new SetInfo { Code = parts[0], Name = parts[1] }, Price = Price };
                    packs.Add(current);
                }
                else if (line.StartsWith("card:") && current != null)
                {
                    var parts = line.Substring(5).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2 || !uint.TryParse(parts[0], out var code) || cards.Get(code) == null) continue;
                    if (!Enum.TryParse<Rarity>(parts[1], out var rarity)) rarity = Rarity.Common;
                    if (!current.Pool.TryGetValue(rarity, out var list)) current.Pool[rarity] = list = new List<uint>();
                    list.Add(code);
                }
            }
            return new ImportShelf(packs.Where(p => p.CardCount > 0).ToList());
        }

        public static ImportShelf Load(string path, ICardDataProvider cards) => File.Exists(path) ? Parse(File.ReadAllText(path), cards) : Empty;
    }
}
