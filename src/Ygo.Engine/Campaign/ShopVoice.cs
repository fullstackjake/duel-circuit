using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ygo.Engine.AI;
using Ygo.Engine.Data;

namespace Ygo.Engine.Campaign
{
    /// <summary>A shop line: who says it (a character id, or "-" for a shop label shown as plain text) and the text, filled.</summary>
    public sealed class VoiceLine
    {
        public string Speaker = "", Text = "";
        public bool IsLabel => Speaker == "-";
    }

    /// <summary>What a revealed pack held, for the pull rules (<see cref="ShopVoice.Pull"/>).</summary>
    public sealed class PackMoment
    {
        public PackSet Set = null!;
        public Rarity Best;
        public uint BestCode;
        /// <summary>A kept key card of a house deck, if any (<see cref="PackCardTags.Key"/>), and its character's id (<see cref="PackCardTags.KeyOf"/>).</summary>
        public uint OpponentKey;
        public string OpponentId = "";
        /// <summary>A kept Wanted card (a new copy toward the active recipe) / a new copy or upgrade of a card in your decks.</summary>
        public uint RecipeCard, DeckCard;
        /// <summary>A house pack (no opponent lines); this visit's previous pack had a line.</summary>
        public bool HousePack, PreviousHadLine;
    }

    /// <summary>
    /// The shop's lines (campaign/shop.txt, docs/dev/SHOP-VOICE-2026-11-01.md; SHOP-REWORK-SPEC section 6): pools of lines by
    /// hook, each gated by story scenes seen (after:/before:), the band and era play. Picking is deterministic (QA runs
    /// replay the same seeds): a hook's lines in a fixed per-save shuffle (the save's salt and the hook), the first unheard
    /// eligible line first (heard lines are "shopline:&lt;hook&gt;#&lt;n&gt;" in <see cref="Profile.Seen"/>), then, once all are
    /// heard, stepping through the same order by the hook's count in <see cref="Profile.VoiceCounts"/>. A more specific
    /// hook ("pull:opponent:vera", "arrival:2011") falls back to the general one ("pull:opponent", "arrival") when it has no
    /// eligible line. The first Kit line ever picked plays the kit-hello line instead.
    /// </summary>
    public sealed class ShopVoice
    {
        public const string FileName = "shop.txt";
        public const string KitId = "kit", KitHello = "kit-hello";
        /// <summary>The longest a footer, arrival or pull line may be, and a detail-panel line (shelf, back shelves, house pack, odds).</summary>
        public const int FooterMax = 110, DetailMax = 140;
        /// <summary><see cref="Profile.VoiceCounts"/>'s key for the Ultra spacing count (Ultra fires on every third qualifying pack).</summary>
        public const string UltraSpacing = "spacing:pull:ultra";
        /// <summary>
        /// The Seen key's prefix for an opponent line played this circuit ("shop:opp:&lt;id&gt;:&lt;circuit index&gt;"). It's a
        /// rule, not a heard line, so New Game+ clears it (<see cref="Profile.NewGamePlus"/>) and those lines can play again.
        /// </summary>
        public const string OpponentSeen = "shop:opp:";
        /// <summary>The alley pools' speaker for the CPU you just played (resolved by the caller, like a scene's "partner").</summary>
        public const string OpponentSpeaker = "opponent";

        private sealed class Pool
        {
            public string Hook = "";
            public List<string> After = new List<string>(), Before = new List<string>();
            public string Band = "";
            public bool BandPlus;
            public List<Line> Lines = new List<Line>();
        }

        private sealed class Line
        {
            public string Speaker = "", Text = "", Mode = "";
            /// <summary>Its place among its hook's lines, in file order.</summary>
            public int Index;
            public Pool Pool = null!;
        }

        private readonly List<Pool> _pools;
        private readonly List<string> _parseProblems;

        /// <summary>A card's name for {card} (set it after loading; without it, {card} reads "that card").</summary>
        public Func<uint, string>? CardName;

        private ShopVoice(List<Pool> pools, List<string> parseProblems)
        {
            _pools = pools;
            _parseProblems = parseProblems;
        }

        /// <summary>A missing file: no lines anywhere.</summary>
        public static ShopVoice Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : new ShopVoice(new List<Pool>(), new List<string>());

        public static ShopVoice Parse(string text)
        {
            var pools = new List<Pool>();
            var problems = new List<string>();
            var counts = new Dictionary<string, int>();
            Pool? current = null;
            int lineNo = 0;
            foreach (var raw in text.Split('\n'))
            {
                lineNo++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var colon = line.IndexOf(':');
                if (colon <= 0) { problems.Add($"line {lineNo}: no ':'"); continue; }
                var key = line.Substring(0, colon).Trim();
                var value = line.Substring(colon + 1).Trim();
                switch (key)
                {
                    case "pool":
                        current = new Pool { Hook = value };
                        pools.Add(current);
                        continue;
                    case "after" when current != null: current.After.Add(value); continue;
                    case "before" when current != null: current.Before.Add(value); continue;
                    case "band" when current != null:
                        current.BandPlus = value.EndsWith("+");
                        current.Band = value.TrimEnd('+').Trim();
                        continue;
                }
                if (current == null) { problems.Add($"line {lineNo}: a line before any pool"); continue; }
                var mode = "";
                var tag = Regex.Match(key, @"^(.*?)\s*\[(era|open)\]$");
                if (tag.Success) { key = tag.Groups[1].Value; mode = tag.Groups[2].Value; }
                int index = counts.TryGetValue(current.Hook, out var n) ? n : 0;
                counts[current.Hook] = index + 1;
                current.Lines.Add(new Line { Speaker = key, Text = value, Mode = mode, Index = index, Pool = current });
            }
            return new ShopVoice(pools, problems);
        }

        // ---- Checks ---------------------------------------------------------------------------------

        private static readonly string[] Hooks =
        {
            "header", "footer", "arrival", KitHello, "back-shelves", "house-pack:name", "house-pack:about", "house-pack:about-gus", "odds",
            "pull:ultra", "pull:secret", "pull:ultimate", "pull:collectors", "pull:ghost", "pull:quarter-century", "pull:starlight",
            "pull:opponent", "pull:recipe", "pull:deck", "box", "box-best", "prize-pull",
        };
        private static readonly string[] Placeholders = { "card", "rarity", "opponent", "opponents", "deck", "year", "packs", "circuit" };

        /// <summary>The bands in campaign order, then "Post-game" (after the Tier 0 Gauntlet).</summary>
        private static List<string> Bands(Circuits circuits) => circuits.All.Select(c => c.Band).Where(b => b.Length > 0).Distinct().Append(PostGameBand).ToList();
        public const string PostGameBand = "Post-game";

        /// <summary>
        /// What's wrong with the file (empty: nothing): unknown hooks, speakers (a roster id or "-"), scene ids in after: and
        /// before:, bands, circuits in shelf: hooks, placeholders, lengths (<see cref="FooterMax"/> for the footer, arrival,
        /// Kit's hello, pull, box and prize lines; <see cref="DetailMax"/> for the rest), empty pools, and circuits with no
        /// shelf: pool. Another file in this format (the alley's voice, Moth's van) passes its own <paramref name="knownHooks"/>:
        /// then its hooks are checked by it, the alley's placeholders (<see cref="BackAlley.Placeholders"/>) and the speaker
        /// "opponent" (the CPU you just played) are allowed, and no shelf: pools are expected.
        /// </summary>
        public List<string> Problems(IEnumerable<Personality> roster, IEnumerable<string> sceneIds, Circuits circuits, Func<string, bool>? knownHooks = null)
        {
            var problems = new List<string>(_parseProblems);
            var ids = new HashSet<string>(roster.Select(p => p.Id));
            var scenes = new HashSet<string>(sceneIds);
            var bands = Bands(circuits);
            var circuitNames = circuits.All.Select(c => c.Name).ToList();
            foreach (var pool in _pools)
            {
                var hook = pool.Hook;
                bool known = knownHooks != null ? knownHooks(hook) : Hooks.Contains(hook)
                    || Regex.IsMatch(hook, @"^arrival:\d{4}$")
                    || (hook.StartsWith("shelf:") && (circuitNames.Contains(hook.Substring(6)) || hook.Substring(6) == FeaturedShelves.PostGame))
                    || (hook.StartsWith("pull:opponent:") && ids.Contains(hook.Substring(14)));
                if (!known) problems.Add($"pool {hook}: unknown hook");
                if (pool.Lines.Count == 0) problems.Add($"pool {hook}: no lines");
                foreach (var s in pool.After.Concat(pool.Before).Where(s => !scenes.Contains(s))) problems.Add($"pool {hook}: unknown scene {s}");
                if (pool.Band.Length > 0 && !bands.Contains(pool.Band, StringComparer.OrdinalIgnoreCase)) problems.Add($"pool {hook}: unknown band {pool.Band}");
                int max = IsFooterHook(hook) ? FooterMax : DetailMax;
                foreach (var line in pool.Lines)
                {
                    if (line.Speaker != "-" && !ids.Contains(line.Speaker) && !(knownHooks != null && line.Speaker == OpponentSpeaker))
                        problems.Add($"pool {hook}: unknown speaker {line.Speaker}");
                    if (line.Text.Length == 0) problems.Add($"pool {hook}: an empty line");
                    if (line.Text.Length > max) problems.Add($"pool {hook}: a line of {line.Text.Length} characters (at most {max}): {line.Text}");
                    foreach (Match m in Regex.Matches(line.Text, @"\{([^}]*)\}"))
                        if (!Placeholders.Contains(m.Groups[1].Value) && !(knownHooks != null && BackAlley.Placeholders.Contains(m.Groups[1].Value)))
                            problems.Add($"pool {hook}: unknown placeholder {m.Value}");
                }
            }
            if (knownHooks == null)
                foreach (var name in circuitNames.Where(n => !_pools.Any(p => p.Hook == "shelf:" + n))) problems.Add($"circuit {name}: no shelf: pool");
            return problems;
        }

        private static bool IsFooterHook(string hook) =>
            hook == "footer" || hook == KitHello || hook == "box" || hook == "box-best" || hook == "prize-pull" || hook == "arrival"
            || hook.StartsWith("arrival:") || hook.StartsWith("pull:");

        // ---- Picking --------------------------------------------------------------------------------

        /// <summary>The band your newest open circuit is in (the current one; "Post-game" once every circuit is cleared).</summary>
        public static string CurrentBand(Profile profile, Circuits circuits) =>
            circuits.All.Count == 0 || circuits.AllCleared(profile) ? PostGameBand : circuits.All[circuits.Current(profile)].Band;

        private static bool Eligible(Line line, Profile profile, Circuits circuits)
        {
            var pool = line.Pool;
            if (pool.After.Any(s => !profile.Seen.Contains(s)) || pool.Before.Any(profile.Seen.Contains)) return false;
            if (line.Mode.Length > 0 && (line.Mode == "era") != profile.EraLimited) return false;
            if (pool.Band.Length > 0)
            {
                var bands = Bands(circuits);
                int now = bands.FindIndex(b => b.Equals(CurrentBand(profile, circuits), StringComparison.OrdinalIgnoreCase));
                int want = bands.FindIndex(b => b.Equals(pool.Band, StringComparison.OrdinalIgnoreCase));
                if (want < 0 || (pool.BandPlus ? now < want : now != want)) return false;
            }
            return true;
        }

        /// <summary>A hook's lines in this save's fixed order (a shuffle seeded by the save's salt and the hook).</summary>
        private List<Line> Order(string hook, Profile profile)
        {
            var lines = _pools.Where(p => p.Hook == hook).SelectMany(p => p.Lines).OrderBy(l => l.Index).ToList();
            uint seed = Hash.Mix(profile.Collection.Salt, CardUnlocks.StableHash(hook), 0x5C0FFEEu);
            for (int i = lines.Count - 1; i > 0; i--)
            {
                int j = (int)(Hash.Mix(seed, (uint)i) % (uint)(i + 1));
                (lines[i], lines[j]) = (lines[j], lines[i]);
            }
            return lines;
        }

        private static string General(string hook)
        {
            var colon = hook.LastIndexOf(':');
            return colon > 0 ? hook.Substring(0, colon) : "";
        }

        public static string HeardKey(string hook, int index) => $"shopline:{hook}#{index}";

        /// <summary>
        /// Like <see cref="Pick"/>, but the save keeps no trace of it (nothing marked heard, no count moved, no Kit hello): the
        /// <paramref name="turn"/>-th eligible line in the save's order, for a duel that's only a show (the exhibition, a combo
        /// clip), whose calls mustn't use up the lines a real duel would say first.
        /// </summary>
        public VoiceLine? Peek(string hook, Profile profile, Circuits circuits, IReadOnlyDictionary<string, string> fill, int turn)
        {
            for (var h = hook; h.Length > 0; h = General(h))
            {
                var order = Order(h, profile).Where(l => Eligible(l, profile, circuits)).ToList();
                if (order.Count == 0) continue;
                var label = order.FirstOrDefault(l => l.Speaker == "-");
                return Make(label ?? order[(turn % order.Count + order.Count) % order.Count], fill);
            }
            return null;
        }

        /// <summary>
        /// A line for a hook (null when no line is eligible): its eligible lines, or the general hook's when it has none;
        /// a label ("-") as it is, otherwise the first unheard line in the save's order (marked heard), or once all are
        /// heard the next by the hook's count. The first Kit line ever picked is replaced by the kit-hello line, once per
        /// save. <paramref name="fill"/> fills the placeholders ("card" or "{card}" as keys; {packs} defaults to the box size).
        /// </summary>
        public VoiceLine? Pick(string hook, Profile profile, Circuits circuits, IReadOnlyDictionary<string, string> fill)
        {
            for (var h = hook; h.Length > 0; h = General(h))
            {
                var order = Order(h, profile).Where(l => Eligible(l, profile, circuits)).ToList();
                if (order.Count == 0) continue;
                var label = order.FirstOrDefault(l => l.Speaker == "-");
                if (label != null) return Make(label, fill);
                var line = order.FirstOrDefault(l => !profile.Seen.Contains(HeardKey(h, l.Index)));
                bool unheard = line != null;
                int n = profile.VoiceCounts.TryGetValue(h, out var c) ? c : 0;
                line ??= order[n % order.Count];
                // Kit's first line in the shop is the hello (which then counts as Kit's first word; the line waits for next time).
                if (line.Speaker == KitId && h != KitHello && !profile.Seen.Contains("shop:" + KitHello)
                    && Order(KitHello, profile).FirstOrDefault(l => Eligible(l, profile, circuits)) is Line hello)
                {
                    profile.Seen.Add("shop:" + KitHello);
                    return Make(hello, fill);
                }
                if (unheard) profile.Seen.Add(HeardKey(h, line.Index));
                else profile.VoiceCounts[h] = n + 1;
                return Make(line, fill);
            }
            return null;
        }

        private static VoiceLine Make(Line line, IReadOnlyDictionary<string, string> fill) => new VoiceLine { Speaker = line.Speaker, Text = Fill(line.Text, fill) };

        /// <summary>Fills {card}, {rarity}, {opponent}, {opponents}, {deck}, {year}, {packs} and {circuit} from <paramref name="values"/> (keys with or without braces); {packs} defaults to <see cref="ShopPrices.BoxPacks"/>.</summary>
        public static string Fill(string text, IReadOnlyDictionary<string, string> values)
        {
            foreach (var pair in values)
                text = text.Replace(pair.Key.StartsWith("{") ? pair.Key : "{" + pair.Key + "}", pair.Value);
            return text.Replace("{packs}", ShopPrices.BoxPacks.ToString());
        }

        private static readonly IReadOnlyDictionary<string, string> NoFill = new Dictionary<string, string>();

        /// <summary>
        /// The status bar's line on opening a shop visit (call after <see cref="Arrivals.Visit"/>): on a visit that recorded
        /// arrivals, or the first visit ever, the newest year speaks (its own arrival:&lt;year&gt; pool, else arrival with
        /// {year}); otherwise a footer line.
        /// </summary>
        public VoiceLine? Footer(Profile profile, Circuits circuits, IReadOnlyList<int> arrivedThisVisit)
        {
            int year = arrivedThisVisit.Count > 0 ? arrivedThisVisit.Max() : profile.ShopVisits <= 1 ? circuits.ShopYear(profile) : 0;
            if (year > 0 && Pick("arrival:" + year, profile, circuits, new Dictionary<string, string> { ["year"] = year.ToString() }) is VoiceLine arrival)
                return arrival;
            return Pick("footer", profile, circuits, NoFill);
        }

        /// <summary>The pull tier of a rarity (docs/dev/SHOP-VOICE-2026-11-01.md's table), or null below Ultra.</summary>
        public static string? Tier(Rarity r) => r switch
        {
            Rarity.UltraRare or Rarity.UltraParallelRare or Rarity.PharaohsRare or Rarity.GoldRare or Rarity.PremiumGoldRare or Rarity.PlatinumRare => "ultra",
            Rarity.SecretRare or Rarity.PrismaticSecretRare or Rarity.GoldSecretRare or Rarity.ExtraSecretRare or Rarity.PlatinumSecretRare => "secret",
            Rarity.UltimateRare => "ultimate",
            Rarity.CollectorsRare => "collectors",
            Rarity.GhostRare or Rarity.GhostGoldRare => "ghost",
            Rarity.QuarterCenturySecretRare => "quarter-century",
            Rarity.StarlightRare => "starlight",
            _ => null,
        };

        /// <summary>
        /// The line under a pack's summary (at most one; null for none), by the pull rules: Starlight > Ghost > Quarter
        /// Century > opponent > recipe > deck > Collector's > Ultimate > Secret > Ultra, where a rarity is the pack's best
        /// pull's (above the base in a rarity set). Ghost, Quarter Century and Starlight always fire; Secret, Ultimate,
        /// Collector's, recipe and deck lines not right after a pack with a line; Ultra on every third Ultra pack; an
        /// opponent line once per opponent per circuit, never on a house pack. A rule that holds but has no eligible line
        /// lets the next one speak.
        /// </summary>
        public VoiceLine? Pull(PackMoment moment, Profile profile, Circuits circuits)
        {
            var tier = moment.Set != null && PackCatalog.IsRaritySet(moment.Set) && moment.Best <= PackCatalog.BaseRarity(moment.Set) ? null : Tier(moment.Best);
            bool ultraFires = false;
            if (tier == "ultra")
            {
                int n = profile.VoiceCounts.TryGetValue(UltraSpacing, out var c) ? c : 0;
                ultraFires = n % 3 == 0;
                profile.VoiceCounts[UltraSpacing] = n + 1;
            }
            string Name(uint code) => code == 0 ? "" : CardName?.Invoke(code) ?? "that card";
            var rarity = Rarities.Name(moment.Best);
            var opponent = circuits.All.SelectMany(x => x.Matches).Select(m => m.Character).FirstOrDefault(p => p.Id == moment.OpponentId);
            string oppKey = $"{OpponentSeen}{moment.OpponentId}:{circuits.Current(profile)}";
            var candidates = new List<(string hook, uint card, bool fires)>
            {
                ("pull:starlight", moment.BestCode, tier == "starlight"),
                ("pull:ghost", moment.BestCode, tier == "ghost"),
                ("pull:quarter-century", moment.BestCode, tier == "quarter-century"),
                ("pull:opponent:" + moment.OpponentId, moment.OpponentKey,
                    moment.OpponentKey != 0 && moment.OpponentId.Length > 0 && !moment.HousePack && opponent != null && !profile.Seen.Contains(oppKey)),
                ("pull:recipe", moment.RecipeCard, moment.RecipeCard != 0 && !moment.PreviousHadLine),
                ("pull:deck", moment.DeckCard, moment.DeckCard != 0 && !moment.PreviousHadLine),
                ("pull:collectors", moment.BestCode, tier == "collectors" && !moment.PreviousHadLine),
                ("pull:ultimate", moment.BestCode, tier == "ultimate" && !moment.PreviousHadLine),
                ("pull:secret", moment.BestCode, tier == "secret" && !moment.PreviousHadLine),
                ("pull:ultra", moment.BestCode, tier == "ultra" && ultraFires),
            };
            foreach (var (hook, card, fires) in candidates)
            {
                if (!fires) continue;
                var fill = new Dictionary<string, string> { ["card"] = Name(card), ["rarity"] = rarity, ["opponent"] = opponent?.Name ?? "" };
                var line = Pick(hook, profile, circuits, fill);
                if (line == null) continue;
                if (hook.StartsWith("pull:opponent")) profile.Seen.Add(oppKey);
                return line;
            }
            return null;
        }

        /// <summary>Hollis's line on a Prize Pack's slab, or a Secret Rare or rarer: at most once per bracket (by its seed).</summary>
        public VoiceLine? PrizePull(Profile profile, Circuits circuits, uint bracketSeed)
        {
            var key = $"shop:prize:{bracketSeed}";
            if (profile.Seen.Contains(key)) return null;
            var line = Pick("prize-pull", profile, circuits, NoFill);
            if (line != null) profile.Seen.Add(key);
            return line;
        }
    }
}
