using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Ygo.Engine.DeckBuilding
{
    /// <summary>
    /// Archetype names, read from the card scripts' archetype_setcode_constants.lua ("SET_BURNING_ABYSS = 0xb1" ->
    /// 0xb1 "Burning Abyss"). A card belongs to an archetype when one of its set codes has the archetype's low 12 bits
    /// and all of its sub-archetype bits.
    /// </summary>
    public sealed class Archetypes
    {
        private readonly Dictionary<ushort, string> _names = new Dictionary<ushort, string>();

        public static Archetypes Empty => new Archetypes();

        public static Archetypes Load(string luaFile)
        {
            var a = new Archetypes();
            if (!File.Exists(luaFile)) return a;
            foreach (Match m in Regex.Matches(File.ReadAllText(luaFile), @"^SET_([A-Z0-9_]+)\s*=\s*0x([0-9a-fA-F]+)", RegexOptions.Multiline))
            {
                var code = Convert.ToUInt16(m.Groups[2].Value, 16);
                if (a._names.ContainsKey(code)) continue;
                a._names[code] = string.Join(" ", m.Groups[1].Value.Split('_').Select(w => w.Length == 0 ? w : w[0] + w.Substring(1).ToLowerInvariant()));
            }
            return a;
        }

        public int Count => _names.Count;

        public string Name(ushort code) => _names.TryGetValue(code, out var n) ? n : $"0x{code:x}";

        /// <summary>Every named archetype (sub-archetypes included): its set code and name.</summary>
        public IEnumerable<(ushort code, string name)> All => _names.Select(p => (p.Key, p.Value));

        public static bool IsIn(CardInfo card, ushort archetype) =>
            card.Setcodes.Any(s => s != 0 && (s & 0xfff) == (archetype & 0xfff) && (s & archetype) == archetype);

        /// <summary>
        /// The archetype most of a deck's Main and Extra Deck cards belong to (at least <paramref name="minimum"/> of
        /// them), counting only archetypes this list names; null for decks without one.
        /// </summary>
        public (ushort code, string name, int count)? Main(Deck deck, ICardDataProvider cards, int minimum = 4)
        {
            var counts = new Dictionary<ushort, int>();
            foreach (var code in deck.Main.Concat(deck.Extra))
            {
                var info = cards.Get(code);
                if (info == null) continue;
                foreach (var s in info.Setcodes.Where(s => s != 0).Distinct())
                {
                    // Count toward the archetype itself (its base code) when it has a name; sub-archetypes roll up.
                    var family = (ushort)(s & 0xfff);
                    if (!_names.ContainsKey(family)) continue;
                    counts[family] = (counts.TryGetValue(family, out var n) ? n : 0) + 1;
                }
            }
            if (counts.Count == 0) return null;
            var best = counts.OrderByDescending(p => p.Value).First();
            return best.Value >= minimum ? (best.Key, Name(best.Key), best.Value) : ((ushort, string, int)?)null;
        }
    }

    /// <summary>Deck checks beyond legality: archetype completeness and whether the Extra Deck can be summoned.</summary>
    public static class DeckAnalysis
    {
        /// <summary>
        /// An archetype's key cards: its members that the given reference decks (built-in and historical decks) play,
        /// most-played first. Base codes.
        /// </summary>
        public static List<uint> KeyCards(ushort archetype, IEnumerable<Deck> references, ICardDataProvider cards)
        {
            var plays = new Dictionary<uint, int>();
            foreach (var deck in references)
                foreach (var code in deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, cards)).Distinct())
                    if (cards.Get(code) is CardInfo info && Archetypes.IsIn(info, archetype))
                        plays[code] = (plays.TryGetValue(code, out var n) ? n : 0) + 1;
            return plays.OrderByDescending(p => p.Value).ThenBy(p => cards.Get(p.Key)?.Name).Select(p => p.Key).ToList();
        }

        /// <summary>
        /// Extra Deck monsters the Main Deck can't make, with why: a Synchro with no Tuner in the Main Deck, an Xyz with
        /// fewer than two Main Deck monsters of its Rank's Level, a Fusion or Link whose named materials aren't in the
        /// deck. A rough check (it doesn't follow combos), so it only flags clear cases.
        /// </summary>
        public static List<(uint code, string reason)> ExtraDeckGaps(Deck deck, ICardDataProvider cards)
        {
            var main = deck.Main.Select(cards.Get).Where(c => c != null).Select(c => c!).ToList();
            var monsters = main.Where(c => (c.Type & CardType.Monster) != 0).ToList();
            var all = main.Concat(deck.Extra.Select(cards.Get).Where(c => c != null).Select(c => c!)).ToList();
            bool tuner = monsters.Any(c => (c.Type & 0x1000) != 0) || all.Any(c => (c.Type & 0x1000) != 0 && (c.Type & CardType.Synchro) != 0);
            // Cards that let you treat a monster as a Tuner or summon one are common; text mentioning "tuner" counts too.
            bool tunerHelp = all.Any(c => (c.Type & CardType.Synchro) == 0 && c.Text.IndexOf("Tuner", StringComparison.Ordinal) >= 0);
            // Cards that Special Summon a Fusion straight from the Extra Deck (Metamorphosis, Magical Scientist) skip its materials.
            bool fusionDirect = main.Any(c => c.Text.Contains("Fusion Monster from your Extra Deck") || c.Text.Contains("Fusion Monster from your Fusion Deck")
                                              || c.Text.Contains("Special Summon 1 Fusion Monster"));
            // Rank-Up cards (and Xyz Summons made by Spells and Traps) reach Ranks the Main Deck's Levels can't.
            bool rankUp = main.Any(c => (c.Type & CardType.Monster) == 0 && (c.Text.Contains("Rank higher") || c.Text.Contains("Xyz Summon")));
            var gaps = new List<(uint, string)>();
            foreach (var code in deck.Extra.Distinct())
            {
                var info = cards.Get(code);
                if (info == null) continue;
                var materials = info.Text.Split('\n')[0];
                if ((info.Type & CardType.Synchro) != 0)
                {
                    if (!tuner && !tunerHelp) gaps.Add((code, "no Tuner in the Main Deck"));
                    continue;
                }
                if ((info.Type & CardType.Xyz) != 0)
                {
                    var m = Regex.Match(materials, @"(\d+)\+? Level (\d+)");
                    if (!m.Success) continue;
                    // Its own way in ("You can also Xyz Summon this card by using..."), or a Rank-Up, needs no Levels.
                    if (rankUp || info.Text.Contains("also Xyz Summon") || info.Text.Contains("Xyz Monster you control")) continue;
                    int need = int.Parse(m.Groups[1].Value), level = int.Parse(m.Groups[2].Value);
                    // Monsters whose Level can change ("its Level becomes 4", "Level 4 monster") count too.
                    int have = monsters.Count(c => (c.Type & CardType.ExtraDeckTypes) == 0
                                                   && ((c.Level & 0xff) == level || c.Text.Contains($"Level becomes {level}") || c.Text.Contains($"Level {level}")));
                    have += main.Count(c => (c.Type & CardType.Monster) == 0 && c.Text.Contains($"Level {level}"));
                    if (have < Math.Min(need, 2)) gaps.Add((code, $"needs Level {level} monsters ({have} in the Main Deck)"));
                    continue;
                }
                if ((info.Type & (CardType.Fusion | CardType.Link)) != 0)
                {
                    var named = Regex.Matches(materials, "\"([^\"]+)\"").Cast<Match>().Select(x => x.Groups[1].Value).Distinct().ToList();
                    if (named.Count == 0) continue;
                    // Made from the opponent's monsters, or sent out by Level (Metamorphosis): the materials don't matter.
                    if (info.Text.Contains("opponent controls") || info.Text.Contains("either field") || ((info.Type & CardType.Fusion) != 0 && fusionDirect)) continue;
                    bool Found(string n) => all.Any(c => c.Code != info.Code && (c.Name == n || c.Name.Contains(n)));
                    // "A" + "B": all needed; "A" or "B" / an archetype name: one is enough.
                    bool ok = materials.Contains(" + ") && !materials.Contains(" or ") ? named.All(Found) : named.Any(Found);
                    if (!ok) gaps.Add((code, $"needs {string.Join(" / ", named.Take(2).Select(n => $"\"{n}\""))}"));
                }
            }
            return gaps;
        }
    }
}
