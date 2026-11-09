using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.AI;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>One Back Alley event: its base decks (an Elo range), the contraband its era allows, and how much is splashed.</summary>
    public sealed class AlleyEvent
    {
        public string Name = "";
        public double MinElo, MaxElo;
        /// <summary>Contraband first printed by this year.</summary>
        public int EraCap;
        /// <summary>Contraband cards added to each base deck.</summary>
        public int Splash;
        /// <summary>Extra Deck contraband allowed (-1: any).</summary>
        public int MaxExtra = -1;
        public double Skill;
    }

    /// <summary>A curated contraband card for an event: its role in a deck and the support it needs.</summary>
    public sealed class Contraband
    {
        public uint Code;
        public string Name = "";
        /// <summary>draw, wipe, removal, backrow, steal, revive, search, negate, handtrap, beater, extender.</summary>
        public string Role = "";
        /// <summary>"any", or a support rule: dark-low-atk:N, light-dark:N, dark:N, synchro, xyz, link, fusion, monsters:N, spells:N.</summary>
        public string Needs = "any";
        /// <summary>The base decks it's offered to (`for:`), ahead of the shared list; empty: every base.</summary>
        public HashSet<string> For = new HashSet<string>();
        /// <summary>A base's card that keeps its place in the list (`late`) instead of going ahead of the shared cards.</summary>
        public bool Late;
    }

    /// <summary>An event's curation: its contraband (best first), bases that sit it out, and per-base role caps.</summary>
    public sealed class Curation
    {
        public List<Contraband> Cards = new List<Contraband>();
        /// <summary>Base decks that don't play this event (`skip:`).</summary>
        public HashSet<string> Skip = new HashSet<string>();
        /// <summary>A base's cap on a role (`cap: base role n`), e.g. kozmo takes one draw card.</summary>
        public Dictionary<(string deck, string role), int> Caps = new Dictionary<(string, string), int>();
        /// <summary>A base's own splash size (`size: base n`): tightly tuned decks take fewer cards.</summary>
        public Dictionary<string, int> Sizes = new Dictionary<string, int>();
    }

    /// <summary>A generated alley deck: the base it came from, what was splashed in, and what was cut for it.</summary>
    public sealed class SplashDeck
    {
        public Deck Deck = new Deck();
        public string Base = "";
        public List<uint> Added = new List<uint>();
        public List<uint> Cut = new List<uint>();
        public List<string> Skipped = new List<string>();
    }

    /// <summary>
    /// The Back Alley's rules and its splash decks (POST-GAME-PLAN "The Back Alley"; measured in Phase 0 before it's built).
    /// The Alley list is the Traditional list (Forbidden cards at one) with the exclusions at 0. A splash deck is a base
    /// deck plus N contraband cards from the event's era, with support checked for "fitted" cards, one board wipe, one
    /// steal and one Maxx "C" at most, and the lowest-value non-archetype cards cut to make room. The same seed always
    /// builds the same deck, in the game and the harness.
    /// </summary>
    public static class Alley
    {
        /// <summary>
        /// Each event's curated contraband (campaign/alley.txt), best first: `event: name`, then `card: name | role | needs`
        /// lines. Cards not in the event's pool (not out yet, excluded) are dropped when read.
        /// </summary>
        public static Dictionary<string, Curation> LoadCurated(string path, ICardDataProvider cards)
        {
            var lists = new Dictionary<string, Curation>();
            Curation? curation = null;
            foreach (var raw in System.IO.File.ReadAllLines(path))
            {
                var line = raw.Split('#')[0].Trim();
                if (line.StartsWith("event:")) lists[line.Substring(6).Trim()] = curation = new Curation();
                else if (line.StartsWith("skip:") && curation != null)
                    foreach (var d in line.Substring(5).Split(',')) { if (d.Trim().Length > 0) curation.Skip.Add(d.Trim()); }
                else if (line.StartsWith("size:") && curation != null && line.Substring(5).Trim().Split(' ') is var size && size.Length == 2 && int.TryParse(size[1], out var count))
                    curation.Sizes[size[0]] = count;
                else if (line.StartsWith("cap:") && curation != null && line.Substring(4).Trim().Split(' ') is var cap && cap.Length == 3 && int.TryParse(cap[2], out var n))
                    curation.Caps[(cap[0], cap[1])] = n;
                else if (line.StartsWith("card:") && curation != null)
                {
                    var current = curation.Cards;
                    var parts = line.Substring(5).Split('|').Select(p => p.Trim()).ToArray();
                    var code = Code(parts[0], cards);
                    if (code == 0) continue;
                    var entry = new Contraband { Code = code, Name = parts[0], Role = parts.Length > 1 ? parts[1] : "", Needs = parts.Length > 2 ? parts[2] : "any" };
                    foreach (var part in parts.Skip(3).Where(p => p.StartsWith("for:")))
                        foreach (var d in part.Substring(4).Split(',')) if (d.Trim().Length > 0) entry.For.Add(d.Trim());
                    entry.Late = parts.Skip(3).Any(p => p == "late");
                    current.Add(entry);
                }
            }
            return lists;
        }

        /// <summary>The roles a deck already fills (by its cards' traits), for picking contraband it lacks.</summary>
        public static HashSet<string> Roles(Deck deck, ICardDataProvider cards)
        {
            var roles = new HashSet<string>();
            var removal = 0;
            foreach (var info in deck.Main.Select(cards.Get).OfType<CardInfo>())
            {
                var t = CardTraits.Of(info);
                if (t.HasFlag(Trait.Draw)) roles.Add("draw");
                if (t.HasFlag(Trait.Search)) roles.Add("search");
                if (t.HasFlag(Trait.BoardWipe)) roles.Add("wipe");
                if (t.HasFlag(Trait.MonsterRemoval)) removal++;
                if (t.HasFlag(Trait.SpellTrapRemoval)) roles.Add("backrow");
                if (t.HasFlag(Trait.Revive)) roles.Add("revive");
                if (t.HasFlag(Trait.Negate)) roles.Add("negate");
                if (t.HasFlag(Trait.HandTrap)) roles.Add("handtrap");
                if ((info.Type & CardType.Monster) != 0 && info.Attack >= 2400) roles.Add("beater");
            }
            if (removal >= 3) roles.Add("removal");
            return roles;
        }

        /// <summary>Whether a curated card's support rule holds for this deck.</summary>
        private static bool Meets(string needs, Deck deck, ICardDataProvider cards)
        {
            if (needs.Length == 0 || needs == "any") return true;
            var monsters = deck.Main.Select(cards.Get).OfType<CardInfo>().Where(c => (c.Type & CardType.Monster) != 0).ToList();
            var extra = deck.Extra.Select(cards.Get).OfType<CardInfo>().ToList();
            const uint Light = 0x10, Dark = 0x20;
            var rule = needs.Split(':');
            int n = rule.Length > 1 && int.TryParse(rule[1], out var k) ? k : 1;
            return rule[0] switch
            {
                "dark-low-atk" => monsters.Count(m => (m.Attribute & Dark) != 0 && m.Attack >= 0 && m.Attack <= 1000) >= n,
                "light-dark" => monsters.Count(m => (m.Attribute & Light) != 0) >= n && monsters.Count(m => (m.Attribute & Dark) != 0) >= n,
                "dark" => monsters.Count(m => (m.Attribute & Dark) != 0) >= n,
                "synchro" => extra.Any(c => (c.Type & CardType.Synchro) != 0),
                "xyz" => extra.Any(c => (c.Type & CardType.Xyz) != 0),
                "link" => extra.Any(c => (c.Type & CardType.Link) != 0),
                "fusion" => extra.Any(c => (c.Type & CardType.Fusion) != 0),
                "monsters" => monsters.Count >= n,
                "spells" => deck.Main.Select(cards.Get).Count(c => c != null && (c.Type & CardType.Spell) != 0) >= n,
                _ => true,
            };
        }

        /// <summary>The order roles a deck lacks are filled in.</summary>
        private static readonly string[] RolePriority = { "draw", "wipe", "backrow", "steal", "revive", "removal", "handtrap", "negate", "beater", "search", "extender" };

        /// <summary>
        /// The roles a deck lacks (Game Designer's rules): no draw spell (Upstart Goblin and Pot of Duality count half);
        /// two or fewer backrow removal; two or fewer monster removal spells and traps (wipe and removal); no steal;
        /// one or fewer revival; no hand trap; no negation; three or fewer monsters with 1900 ATK or more.
        /// </summary>
        public static HashSet<string> Lacks(Deck deck, ICardDataProvider cards)
        {
            var main = deck.Main.Select(cards.Get).OfType<CardInfo>().ToList();
            double draw = main.Where(c => (c.Type & CardType.Spell) != 0 && CardTraits.Of(c).HasFlag(Trait.Draw))
                .Sum(c => c.Name is "Upstart Goblin" or "Pot of Duality" ? 0.5 : 1);
            int Count(Trait t, bool spellTrapOnly = false) =>
                main.Count(c => CardTraits.Of(c).HasFlag(t) && (!spellTrapOnly || (c.Type & CardType.Monster) == 0));
            var lacks = new HashSet<string>();
            if (draw < 1) lacks.Add("draw");
            if (Count(Trait.SpellTrapRemoval) <= 2) lacks.Add("backrow");
            if (Count(Trait.MonsterRemoval, spellTrapOnly: true) <= 2) { lacks.Add("wipe"); lacks.Add("removal"); }
            if (!main.Any(c => Steals.Contains(c.Name))) lacks.Add("steal");
            if (Count(Trait.Revive) <= 1) lacks.Add("revive");
            if (Count(Trait.HandTrap) == 0) lacks.Add("handtrap");
            if (Count(Trait.Negate) == 0) lacks.Add("negate");
            if (main.Count(c => (c.Type & CardType.Monster) != 0 && c.Attack >= 1900) <= 3) lacks.Add("beater");
            return lacks;
        }

        /// <summary>
        /// Picks an event's curated contraband for one deck: first the best card for each role it lacks (in priority
        /// order), then the rest best-first; one card a role (two for draw), only cards whose support it has, none it
        /// already runs. A little seeded shuffle among neighbours keeps two decks from one base apart.
        /// </summary>
        public static List<Contraband> Pick(IReadOnlyList<Contraband> curated, Deck deck, string baseName, int count, Random rng, ICardDataProvider cards,
            IReadOnlyDictionary<(string, string), int>? caps = null)
        {
            var owned = new HashSet<uint>(deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, cards)));
            // Cards meant for this base (`for:`) come ahead of the shared list; cards meant for other bases don't come.
            var list = curated.Where(c => c.For.Count == 0 || c.For.Contains(baseName))
                .Select((c, i) => (c, rank: (c.For.Count > 0 && !c.Late ? -100 : 0) + i + rng.NextDouble() * (c.For.Count > 0 ? 1 : 5))).OrderBy(x => x.rank).Select(x => x.c)
                .Where(c => !owned.Contains(c.Code) && Meets(c.Needs, deck, cards)).ToList();
            var picked = new List<Contraband>();
            bool Room(string role) => picked.Count(p => p.Role == role)
                < (caps != null && caps.TryGetValue((baseName, role), out var cap) ? cap : role == "draw" ? 2 : 1);
            int extras = 0;
            bool Fits(Contraband c) => (cards.Get(c.Code)!.Type & CardType.ExtraDeckTypes) == 0 || extras == 0;
            void Take(Contraband c) { picked.Add(c); if ((cards.Get(c.Code)!.Type & CardType.ExtraDeckTypes) != 0) extras++; }
            // The base's own era contraband first (up to half the splash), then what it lacks, then the rest.
            foreach (var c in list.Where(c => c.For.Count > 0 && !c.Late).Take(Math.Max(1, count / 2)))
                if (Room(c.Role) && Fits(c)) Take(c);
            var lacks = Lacks(deck, cards);
            // The lacking roles, roughly in priority order (a seeded nudge, so decks from one base differ).
            foreach (var role in RolePriority.Where(lacks.Contains).Select((r, i) => (r, k: i + rng.NextDouble() * 2.5)).OrderBy(x => x.k).Select(x => x.r))
            {
                if (picked.Count >= count) break;
                var best = list.FirstOrDefault(c => c.Role == role && !picked.Contains(c) && Fits(c));
                if (best != null && Room(role)) Take(best);
            }
            foreach (var c in list)
            {
                if (picked.Count >= count) break;
                if (!picked.Contains(c) && Room(c.Role) && Fits(c)) Take(c);
            }
            return picked;
        }

        /// <summary>What a role looks like in a deck's own cards, for cutting the weakest card of the splash card's role.</summary>
        private static bool HasRole(CardInfo c, string role) => role switch
        {
            "draw" => CardTraits.Of(c).HasFlag(Trait.Draw),
            "wipe" or "removal" or "steal" => CardTraits.Of(c).HasFlag(Trait.MonsterRemoval) || CardTraits.Of(c).HasFlag(Trait.BoardWipe),
            "backrow" => CardTraits.Of(c).HasFlag(Trait.SpellTrapRemoval),
            "revive" => CardTraits.Of(c).HasFlag(Trait.Revive),
            "negate" => CardTraits.Of(c).HasFlag(Trait.Negate),
            "handtrap" => CardTraits.Of(c).HasFlag(Trait.HandTrap),
            "beater" => (c.Type & CardType.Monster) != 0 && c.Attack >= 1900,
            _ => false,
        };

        /// <summary>
        /// The card a splash card replaces: never the archetype, a draw or search card, a card the Alley limits below 3
        /// (the deck's limited staples), a card on the event's own list (Raigeki, Monster Reborn: contraband-tier already),
        /// a big monster (unless a beater replaces it) or contraband just added; first the weakest card of the same role, then one copy of a two-of, then a one-of.
        /// 0 when nothing may go.
        /// </summary>
        private static uint CutFor(Contraband splash, Deck deck, HashSet<ushort> archetype, Banlist alley, List<uint> added, HashSet<uint> listed,
            ICardDataProvider cards)
        {
            var copies = deck.Main.GroupBy(c => Banlist.BaseCode(c, cards)).ToDictionary(g => g.Key, g => g.Count());
            var candidates = deck.Main.Distinct().Where(c => !added.Contains(c) && cards.Get(c) is CardInfo ci
                && !ci.Setcodes.Any(archetype.Contains) && !CardTraits.Of(ci).HasFlag(Trait.Draw) && !CardTraits.Of(ci).HasFlag(Trait.Search)
                && alley.Allowed(c, cards) >= 3 && !listed.Contains(Banlist.BaseCode(c, cards))
                // The deck's big monsters (2400 ATK and up: Dark Armed Dragon, its bosses) stay, unless a beater replaces them.
                && (splash.Role == "beater" || (ci.Type & CardType.Monster) == 0 || ci.Attack < 2400)).ToList();
            double Value(uint c) => AutoDeck.Value(cards.Get(c)!);
            // Same role, same kind of card: a removal spell replaces a removal spell, never the deck's boss monster.
            bool splashIsMonster = (cards.Get(splash.Code)!.Type & CardType.Monster) != 0;
            var sameRole = candidates.Where(c => HasRole(cards.Get(c)!, splash.Role) && ((cards.Get(c)!.Type & CardType.Monster) != 0) == splashIsMonster)
                .OrderBy(Value).ThenBy(c => c).FirstOrDefault();
            if (sameRole != 0) return sameRole;
            // Then a copy of a two-of, then a one-of: of the same kind of card first (a spell doesn't replace the boss).
            foreach (var sameKind in new[] { true, false })
            {
                var pool = candidates.Where(c => !sameKind || ((cards.Get(c)!.Type & CardType.Monster) != 0) == splashIsMonster).ToList();
                var twoOf = pool.Where(c => copies[Banlist.BaseCode(c, cards)] == 2).OrderBy(Value).ThenBy(c => c).FirstOrDefault();
                if (twoOf != 0) return twoOf;
                var oneOf = pool.Where(c => copies[Banlist.BaseCode(c, cards)] == 1).OrderBy(Value).ThenBy(c => c).FirstOrDefault();
                if (oneOf != 0) return oneOf;
            }
            return 0;
        }

        public static readonly IReadOnlyList<AlleyEvent> Events = new[]
        {
            new AlleyEvent { Name = "Dumpster Row", MinElo = 1276, MaxElo = 1480, EraCap = 2006, Splash = 3, Skill = 0.7 },
            new AlleyEvent { Name = "The Basement Game", MinElo = 1416, MaxElo = 1592, EraCap = 2014, Splash = 4, Skill = 0.85 },
            new AlleyEvent { Name = "The Black Market", MinElo = 1585, MaxElo = 1693, EraCap = 2019, Splash = 4, MaxExtra = 0, Skill = 0.95 },
        };

        /// <summary>Set to 0 in the Alley: loops, coin-flip and deck-out endings, resets, turn-1 FTKs and floodgates.</summary>
        public static readonly string[] Excluded =
        {
            "Butterfly Dagger - Elma", "Ultimate Offering", "Wind-Up Hunter", "Self-Destruct Button", "Last Turn", "Sixth Sense",
            "Exchange of the Spirit", "Fiber Jar", "Magical Scientist", "Cyber-Stein", "Dark Strike Fighter",
            "Imperial Order", "Royal Oppression", "Vanity's Emptiness", "Kaiser Colosseum", "Number 16: Shock Master",
            "Anti-Spell Fragrance", "Rivalry of Warlords", "Gozen Match", "Dimensional Barrier", "Mystic Mine",
            // More locks two bots stall on, and dice (Game Designer, 2026-11-01).
            "Barrier Statue of the Torrent", "Barrier Statue of the Drought", "Barrier Statue of the Inferno", "Barrier Statue of the Stormwinds",
            "Archlord Kristya", "There Can Be Only One", "Summon Limit", "Spell Canceller", "Secret Village of the Spellcasters",
            "Number 41: Bagooska the Terribly Tired Tapir", "Number 67: Pair-a-Dice Smasher", "Chicken Game",
            // A Ritual lock and a banish-all floodgate (No Limits owner decision 7, docs/dev/NO-LIMITS-SPEC-2026-11-02.md P2).
            "Djinn Releaser of Rituals", "Archnemeses Protos",
        };

        /// <summary>
        /// Exclusions no deck in the alley ever runs (a subset of <see cref="Excluded"/>): loops, a draw, coin and dice
        /// endings, a deck-out win, a reset, an LP stall the bot can't judge.
        /// </summary>
        public static readonly string[] Never =
        {
            "Butterfly Dagger - Elma", "Ultimate Offering", "Wind-Up Hunter", "Self-Destruct Button", "Last Turn", "Sixth Sense",
            "Exchange of the Spirit", "Fiber Jar", "Number 67: Pair-a-Dice Smasher", "Chicken Game",
        };

        /// <summary>
        /// The rest of <see cref="Excluded"/>: turn-1 FTKs and floodgates. Allowed only in an authored boss deck that names
        /// them in its seat's `allow:` and passes the endless-duel check.
        /// </summary>
        public static readonly string[] BossOnly =
        {
            "Magical Scientist", "Cyber-Stein", "Dark Strike Fighter",
            "Imperial Order", "Royal Oppression", "Vanity's Emptiness", "Kaiser Colosseum", "Number 16: Shock Master",
            "Anti-Spell Fragrance", "Rivalry of Warlords", "Gozen Match", "Dimensional Barrier", "Mystic Mine",
            "Barrier Statue of the Torrent", "Barrier Statue of the Drought", "Barrier Statue of the Inferno", "Barrier Statue of the Stormwinds",
            "Archlord Kristya", "There Can Be Only One", "Summon Limit", "Spell Canceller", "Secret Village of the Spellcasters",
            "Number 41: Bagooska the Terribly Tired Tapir",
            "Djinn Releaser of Rituals", "Archnemeses Protos",
        };

        /// <summary>
        /// The generic power cards (No Limits rule 3, docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 5.1): a peak build's
        /// contraband picks hold at most one.
        /// </summary>
        public static readonly string[] PowerCards =
        {
            "Pot of Greed", "Graceful Charity", "Harpie's Feather Duster", "Raigeki", "Heavy Storm", "Dark Hole", "Monster Reborn",
            "Change of Heart",
        };

        /// <summary>The deck key prefix of an alley opponent's deck ("alley:dumpster-row.1", "alley:bracket.3").</summary>
        public const string DeckPrefix = "alley:";

        public static bool IsAlleyDeck(string deckName) => deckName.StartsWith(DeckPrefix, StringComparison.Ordinal);

        /// <summary>
        /// Your decks' rules in the alley (every alley duel and the deck editor opened from it, nowhere else): the Alley
        /// list, any card released by now (no pool), no Ban Passes, forced like a cup's, and originals as printed
        /// (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md D2: the alley's exclusions still apply to them).
        /// </summary>
        public static FormatRules Rules(Banlist alley) =>
            new FormatRules { List = alley, Pool = "", Passes = 0, Forced = true, Texts = TextRules.PrintedText };

        private static readonly string[] Steals = { "Change of Heart", "Snatch Steal", "Brain Control", "Mind Control", "Creature Swap", "Owner's Seal" };
        private const string MaxxC = "Maxx \"C\"";

        private static uint Code(string name, ICardDataProvider cards) =>
            cards.All.FirstOrDefault(c => c.Alias == 0 && c.Name == name)?.Code ?? 0;

        /// <summary>A card's code by its exact name (the modern card, alias 0); 0 if there's none.</summary>
        public static uint CodeOf(string name, ICardDataProvider cards) => Code(name, cards);

        /// <summary>The Alley list: Traditional with the exclusions at 0.</summary>
        public static Banlist List(Banlist traditional, ICardDataProvider cards)
        {
            var list = new Banlist { Name = "The Alley", Limits = new Dictionary<uint, int>(traditional.Limits) };
            foreach (var name in Excluded) if (Code(name, cards) is var code && code != 0) list.Limits[code] = 0;
            return list;
        }

        /// <summary>
        /// The No Limits list (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 4.2): the exclusions at 0 and nothing else, so every
        /// Forbidden card is back at 3. Never and BossOnly cards stay out even here.
        /// </summary>
        public static Banlist NoLimitsList(ICardDataProvider cards)
        {
            var list = new Banlist { Name = "No Limits" };
            foreach (var name in Excluded) if (Code(name, cards) is var code && code != 0) list.Limits[code] = 0;
            return list;
        }

        /// <summary>
        /// The contraband an event may splash: every card Forbidden in some TCG list, out by the event's year, allowed in
        /// the Alley (not excluded).
        /// </summary>
        public static List<uint> Contraband(IEnumerable<Banlist> lists, Banlist alley, int eraCap, PrintingIndex printings, ICardDataProvider cards)
        {
            var ever = new HashSet<uint>();
            foreach (var list in lists.Where(l => l.Name.Contains("TCG") && !l.Whitelist))
                foreach (var (code, limit) in list.Limits)
                    if (limit == 0) ever.Add(Banlist.BaseCode(code, cards));
            return ever.Where(c => cards.Get(c) != null && alley.Allowed(c, cards) > 0)
                .Where(c => printings.FirstYear(c, cards.Get(c)!.Alias) is var year && year > 0 && year <= eraCap)
                .OrderBy(c => c).ToList();
        }

        /// <summary>Whether a "fitted" card has the support it needs in this deck (other cards need none).</summary>
        private static bool Supported(CardInfo card, Deck deck, ICardDataProvider cards)
        {
            var monsters = deck.Main.Select(cards.Get).OfType<CardInfo>().Where(c => (c.Type & CardType.Monster) != 0).ToList();
            int Of(uint attribute) => monsters.Count(m => (m.Attribute & attribute) != 0);
            const uint Light = 0x10, Dark = 0x20;
            return card.Name switch
            {
                "Crush Card Virus" => monsters.Count(m => (m.Attribute & Dark) != 0 && m.Attack <= 1000 && m.Attack >= 0) >= 3,
                "Chaos Emperor Dragon - Envoy of the End" or "Black Luster Soldier - Envoy of the Beginning" => Of(Light) >= 4 && Of(Dark) >= 4,
                "Dark Armed Dragon" => Of(Dark) >= 10,
                "Chaos Sorcerer" => Of(Light) >= 3 && Of(Dark) >= 3,
                _ => true,
            };
        }

        /// <summary>The setcodes most of the deck shares: its archetype, whose cards are never cut.</summary>
        private static HashSet<ushort> Archetype(Deck deck, ICardDataProvider cards) =>
            new HashSet<ushort>(deck.Main.Concat(deck.Extra).Select(cards.Get).OfType<CardInfo>().SelectMany(c => c.Setcodes)
                .GroupBy(s => s).Where(g => g.Count() >= 6).Select(g => g.Key));

        /// <summary>
        /// Whether an Extra Deck card is one this deck can summon: a kind its Extra Deck already makes (Synchro, Xyz, Link),
        /// or a Fusion of its own archetype. Main Deck cards always can.
        /// </summary>
        private static bool Summonable(CardInfo card, Deck deck, HashSet<ushort> archetype, ICardDataProvider cards)
        {
            if ((card.Type & CardType.ExtraDeckTypes) == 0) return true;
            var extra = deck.Extra.Select(cards.Get).OfType<CardInfo>().ToList();
            foreach (var kind in new[] { CardType.Synchro, CardType.Xyz, CardType.Link })
                if ((card.Type & kind) != 0) return extra.Any(c => (c.Type & kind) != 0);
            return (card.Type & CardType.Fusion) != 0 && card.Setcodes.Any(archetype.Contains);
        }

        /// <summary>Whether a deck is from an event's era: nine in ten of its cards out by its year.</summary>
        public static bool FromEra(Deck deck, AlleyEvent e, PrintingIndex printings, ICardDataProvider cards)
        {
            var all = deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, cards)).ToList();
            return all.Count > 0 && all.Count(c => printings.FirstYear(c, cards.Get(c)?.Alias ?? 0) is var y && y > 0 && y <= e.EraCap) >= 0.9 * all.Count;
        }

        /// <summary>
        /// Builds an event's splash deck from a base deck: first made legal for the Alley (its own cards topped up, as a
        /// loaner is), then the contraband added. The same seed, the same deck.
        /// </summary>
        public static SplashDeck Splash(Deck baseDeck, AlleyEvent e, IReadOnlyList<uint> pool, Banlist alley, int seed, ICardDataProvider cards,
            Curation? curation = null)
        {
            var curated = curation?.Cards;
            var rng = new Random(seed);
            var deck = Loaners.Adjust(baseDeck, alley, cards).Deck;
            deck.Side.Clear();
            var result = new SplashDeck { Deck = deck, Base = baseDeck.Name };
            var archetype = Archetype(baseDeck, cards);
            if (curated != null && curated.Count > 0)
            {
                // The event's curated list: what the deck lacks first, cut by role.
                var poolSet = new HashSet<uint>(pool);
                var listed = new HashSet<uint>(curated.Select(c => c.Code));
                int splash = curation!.Sizes.TryGetValue(baseDeck.Name, out var own) ? own : e.Splash;
                foreach (var pick in Pick(curated.Where(c => poolSet.Contains(c.Code)).ToList(), deck, baseDeck.Name, splash, rng, cards, curation.Caps))
                {
                    var info = cards.Get(pick.Code)!;
                    if ((info.Type & CardType.ExtraDeckTypes) != 0)
                    {
                        // An Extra Deck payoff (only from a base's own list, its support checked there): it replaces the base's least
                        // valuable Extra card outside its archetype.
                        if (pick.For.Count == 0 && !Summonable(info, deck, archetype, cards)) continue;
                        var extraCut = deck.Extra.Where(c => cards.Get(c) is CardInfo ci && !ci.Setcodes.Any(archetype.Contains) && !result.Added.Contains(c))
                            .OrderBy(c => AutoDeck.Value(cards.Get(c)!)).ThenBy(c => c).FirstOrDefault();
                        if (extraCut == 0 && deck.Extra.Count >= DeckRules.ExtraMax) continue;
                        if (extraCut != 0) { deck.Extra.Remove(extraCut); result.Cut.Add(extraCut); }
                        deck.Extra.Add(pick.Code);
                        result.Added.Add(pick.Code);
                        continue;
                    }
                    var cut = CutFor(pick, deck, archetype, alley, result.Added, listed, cards);
                    if (cut == 0 && deck.Main.Count >= DeckRules.MainMax) continue;
                    if (cut != 0) { deck.Main.Remove(cut); result.Cut.Add(cut); }
                    deck.Main.Add(pick.Code);
                    result.Added.Add(pick.Code);
                }
                deck.Name = $"{baseDeck.Name}+{string.Join("+", result.Added.Select(c => c % 100000))}";
                deck.Notes["recipe"] = baseDeck.Name;
                return result;
            }
            var wipes = deck.Main.Concat(deck.Extra).Select(cards.Get).Count(c => c != null && CardTraits.Of(c).HasFlag(Trait.BoardWipe));
            var steals = new HashSet<uint>(Steals.Select(n => Code(n, cards)));
            uint maxxC = Code(MaxxC, cards);
            bool HasSteal() => deck.Main.Any(steals.Contains);
            int extraAdded = 0;
            // No curated list: stronger cards of the whole pool first, with some chance (seeded) so decks differ.
            var order = pool.Select(c => (code: c, info: cards.Get(c)!))
                .OrderByDescending(x => AutoDeck.Value(x.info) * (0.6 + 0.8 * rng.NextDouble())).ToList();
            foreach (var (code, info) in order)
            {
                if (result.Added.Count >= e.Splash) break;
                if (deck.Main.Concat(deck.Extra).Any(c => Banlist.BaseCode(c, cards) == code)) continue;
                bool extra = (info.Type & CardType.ExtraDeckTypes) != 0;
                if (extra && e.MaxExtra >= 0 && extraAdded >= e.MaxExtra) continue;
                if (CardTraits.Of(info).HasFlag(Trait.BoardWipe) && wipes >= 1) { result.Skipped.Add($"{info.Name} (a board wipe already)"); continue; }
                if (steals.Contains(code) && HasSteal()) { result.Skipped.Add($"{info.Name} (a steal already)"); continue; }
                if (code == maxxC && deck.Main.Contains(maxxC)) continue;
                if (!Supported(info, deck, cards) || !Summonable(info, deck, archetype, cards)) { result.Skipped.Add($"{info.Name} (no support)"); continue; }
                // Make room: the lowest-value card of that section, never the archetype, a card the deck runs three of, or
                // contraband just added; one-ofs go before two-ofs.
                var section = extra ? deck.Extra : deck.Main;
                var copies = section.GroupBy(c => Banlist.BaseCode(c, cards)).ToDictionary(g => g.Key, g => g.Count());
                var cut = section.Where(c => !result.Added.Contains(c) && copies[Banlist.BaseCode(c, cards)] < 3
                                             && cards.Get(c) is CardInfo ci && !ci.Setcodes.Any(archetype.Contains))
                    .OrderBy(c => copies[Banlist.BaseCode(c, cards)]).ThenBy(c => AutoDeck.Value(cards.Get(c)!)).ThenBy(c => c).FirstOrDefault();
                if (cut == 0 && (extra ? section.Count >= DeckRules.ExtraMax : section.Count >= DeckRules.MainMax)) continue;
                if (cut != 0) { section.Remove(cut); result.Cut.Add(cut); }
                section.Add(code);
                result.Added.Add(code);
                if (extra) extraAdded++;
                if (CardTraits.Of(info).HasFlag(Trait.BoardWipe)) wipes++;
            }
            deck.Name = $"{baseDeck.Name}+{string.Join("+", result.Added.Select(c => c % 100000))}";
            // The base's playbook plays it (Playbooks.ForDeck reads "#recipe").
            deck.Notes["recipe"] = baseDeck.Name;
            return result;
        }

        /// <summary>Sub-seeds <see cref="SplashChecked"/> tries before it plays the base unsplashed.</summary>
        public const int SubSeeds = 8;

        /// <summary>The splash an event's base takes: the base's own `size:`, else the event's.</summary>
        public static int SplashSize(AlleyEvent e, string baseName, Curation? curation) =>
            curation != null && curation.Sizes.TryGetValue(baseName, out var own) ? own : e.Splash;

        /// <summary>
        /// <see cref="Splash"/>, checked: the deck must be legal under the Alley list and carry its full splash. If not, the
        /// next sub-seed is tried (up to <see cref="SubSeeds"/>: the first is <paramref name="seed"/> itself, so a good deck
        /// is the same as Splash's), then the base plays made legal and unsplashed (<paramref name="subSeed"/> -1; the
        /// harness fails any seed that needs it). <paramref name="subSeed"/> is the attempt that was kept.
        /// </summary>
        public static SplashDeck SplashChecked(Deck baseDeck, AlleyEvent e, IReadOnlyList<uint> pool, Banlist alley, int seed, ICardDataProvider cards,
            Curation? curation, out int subSeed)
        {
            int want = SplashSize(e, baseDeck.Name, curation);
            for (int k = 0; k < SubSeeds; k++)
            {
                int s = k == 0 ? seed : unchecked((int)Hash.Mix((uint)seed, 0x5B5Eu, (uint)k));
                var splash = Splash(baseDeck, e, pool, alley, s, cards, curation);
                if (splash.Added.Count >= want && DeckRules.Validate(splash.Deck, alley, cards).Count == 0) { subSeed = k; return splash; }
            }
            subSeed = -1;
            var plain = Loaners.Adjust(baseDeck, alley, cards).Deck;
            plain.Side.Clear();
            plain.Name = baseDeck.Name;
            plain.Notes["recipe"] = baseDeck.Name;
            return new SplashDeck { Deck = plain, Base = baseDeck.Name, Skipped = { "unsplashed: no sub-seed gave a legal, full splash" } };
        }
    }
}
