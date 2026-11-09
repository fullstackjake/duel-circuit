using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>Self-checks for the deck editor's engine pieces: search, ban lists, validation, odds, codes, tester.</summary>
public static class DeckBuildingTest
{
    /// <summary>
    /// lflists/history.lflist.conf's lists from TCG September 2023 to TCG September 2026, each as the list before it plus
    /// the changes its source names ("&lt;card&gt;: &lt;copies now&gt;", 3 = off the list), checked card for card against
    /// the source's page when the lists went in (docs/dev/NO-LIMITS-SPEC-2026-11-02.md P1). The last step is Konami's 21 Sep
    /// 2026 page, so a wrong secondary source anywhere before it shows up as a miss.
    /// </summary>
    private static readonly (string name, string before, string source, string[] changes)[] ListChain =
    {
        ("TCG January 2024", "TCG September 2023", "yugiohcardguide.com 2024-01-01", new[]
        {
            "Agido the Ancient Sentinel: 0", "Dinowrestler Pankratops: 2", "Gozen Match: 1",
            "Ib the World Chalice Justiciar: 1", "Infernity Archfiend: 3", "Isolde, Two Tales of the Noble Knights: 0",
            "Kashtira Unicorn: 3", "Kelbek the Ancient Vanguard: 0", "Mathmech Circular: 0", "Mind Control: 3",
            "Orcust Harp Horror: 1", "Pot of Desires: 3", "Purrely Sleepy Memory: 2",
            "Redox, Dragon Ruler of Boulders: 1", "Rescue-ACE Air Lifter: 1", "Rivalry of Warlords: 1",
            "Snatch Steal: 1", "Speedroid Terrortop: 2", "Spellbook of Judgment: 3", "Spright Starter: 3",
            "Sunavalon Dryas: 1", "Sunvine Healer: 1", "There Can Be Only One: 1", "Unchained Soul of Sharvara: 1",
            "Upstart Goblin: 3"
        }),
        ("TCG April 2024", "TCG January 2024", "yugiohcardguide.com 2024-04-15", new[]
        {
            "Anti-Spell Fragrance: 1", "Archnemeses Protos: 1", "Armageddon Knight: 2", "Baronne de Fleur: 0",
            "Borreload Savage Dragon: 0", "Chicken Game: 1", "Destiny HERO - Malicious: 3", "Linkuriboh: 0",
            "Majespecter Unicorn - Kirin: 1", "Orcust Harp Horror: 3", "Purrely Delicious Memory: 2",
            "Sky Striker Mobilize - Engage!: 3", "Speedroid Terrortop: 3", "Summon Limit: 0",
            "Thunder Dragon Colossus: 1", "Tidal, Dragon Ruler of Waterfalls: 1"
        }),
        ("TCG September 2024", "TCG April 2024", "yugiohcardguide.com 2024-09-02", new[]
        {
            "Ancient Fairy Dragon: 3", "Apollousa, Bow of the Goddess: 0", "Armageddon Knight: 3",
            "Beatrice, Lady of the Eternal: 0", "Blaster, Dragon Ruler of Infernos: 2", "Branded Fusion: 1",
            "Denglong, First of the Yang Zing: 3", "Eva: 1", "Fiendsmith's Lacrima: 0",
            "Hot Red Dragon Archfiend King Calamity: 0", "Ib the World Chalice Justiciar: 2", "Lunalight Tiger: 2",
            "Majespecter Unicorn - Kirin: 3", "Number 40: Gimmick Puppet of Strings: 1",
            "Number C40: Gimmick Puppet of Dark Strings: 1", "Opening of the Spirit Gates: 1", "Performage Plushfire: 3",
            "Pot of Prosperity: 1", "Red Rose Dragon: 3", "Redox, Dragon Ruler of Boulders: 2", "Sangen Summoning: 1",
            "Skill Drain: 1", "Snake-Eye Ash: 1", "Snake-Eyes Poplar: 1", "Tempest, Dragon Ruler of Storms: 2",
            "That Grass Looks Greener: 1", "Thunder Dragon Colossus: 2", "Tidal, Dragon Ruler of Waterfalls: 2",
            "Time Seal: 3"
        }),
        ("TCG December 2024", "TCG September 2024", "yugiohcardguide.com 2024-12-09", new[]
        {
            "Blaster, Dragon Ruler of Infernos: 3", "Cyber Jar: 2", "Danger!? Jackalope?: 2", "Danger!? Tsuchinoko?: 2",
            "Dinowrestler Pankratops: 3", "Eva: 2", "Gimmick Puppet Nightmare: 0", "Ib the World Chalice Justiciar: 3",
            "Jowgen the Spiritualist: 0", "Lunalight Tiger: 3", "Opening of the Spirit Gates: 3",
            "Original Sinful Spoils - Snake-Eye: 0", "Performapal Monkeyboard: 2", "Phantom of Yubel: 1",
            "Redox, Dragon Ruler of Boulders: 3", "Rescue-ACE Air Lifter: 3", "Sangen Kaimen: 1",
            "Tempest, Dragon Ruler of Storms: 3", "Tenpai Dragon Chundra: 1", "Thunder Dragon Colossus: 3",
            "Tidal, Dragon Ruler of Waterfalls: 3", "Zoodiac Barrage: 1"
        }),
        ("TCG April 2025", "TCG December 2024", "yugiohcardguide.com 2025-04-07", new[]
        {
            "Abyss Dweller: 0", "Bahamut Shark: 0", "Black Dragon Collapserpent: 2", "Bonfire: 1", "Brilliant Fusion: 1",
            "Bystial Druiswurm: 1", "Crossout Designator: 1", "Cyber Jar: 3", "Danger!? Jackalope?: 3",
            "Danger!? Tsuchinoko?: 3", "Dimension Shifter: 1", "Eva: 3", "Ext Ryzeal: 2", "Ice Ryzeal: 2",
            "Knightmare Gryphon: 0", "Link Decoder: 0", "Maliss <P> Dormouse: 2", "Maliss <P> White Rabbit: 2",
            "Maliss in Underground: 2", "Master Peace, the True Dracoslaying King: 1", "Mathmech Circular: 1",
            "Morphing Jar: 2", "Performapal Monkeyboard: 3", "Ryzeal Detonator: 1", "Snake-Eye Ash: 2",
            "Snake-Eyes Poplar: 2", "Sword Ryzeal: 2", "Triple Tactics Talent: 1", "Unchained Soul of Sharvara: 2",
            "White Dragon Wyverburster: 2"
        }),
        ("TCG September 2025", "TCG April 2025", "ygorganization.com/goodnightmoon", new[]
        {
            "Branded Fusion: 2", "Crystron Inclusion: 1", "Dragonic Diagram: 3", "Ext Ryzeal: 1",
            "Fiendsmith's Lacrima: 1", "Glow-Up Bulb: 3", "Herald of the Arc Light: 1", "King of the Feral Imps: 0",
            "Lightning Storm: 3", "Linkuriboh: 1", "Maliss <P> Dormouse: 1",
            "Master Peace, the True Dracoslaying King: 3", "Moon of the Closed Heaven: 0", "Morphing Jar: 3",
            "Number 67: Pair-a-Dice Smasher: 0", "Obedience Schooled: 1", "Phantom Skyblaster: 3", "Snake-Eye Ash: 3",
            "Snake-Eyes Poplar: 3", "Solemn Judgment: 1", "Splash Mage: 0", "Stake your Soul!: 1", "Summon Sorceress: 1",
            "Vanquish Soul Razen: 1", "Zoodiac Barrage: 3", "Zoodiac Drident: 1"
        }),
        ("TCG October 2025", "TCG September 2025", "yugiohcardguide.com 2025-10-27", new[]
        {
            "Archnemeses Protos: 0", "Artifact Mjollnir: 0", "Barrier Statue of the Drought: 0",
            "Barrier Statue of the Inferno: 0", "Dimensional Barrier: 0", "Evilswarm Ouroboros: 0",
            "Fossil Dyna Pachycephalo: 0"
        }),
        ("TCG February 2026", "TCG October 2025", "yugiohcardguide.com 2026-02-02", new[]
        {
            "\"A Case for K9\": 1", "Ame no Habakiri no Mitsurugi: 1", "Barrier Statue of the Torrent: 0",
            "Black Dragon Collapserpent: 3", "Change of Heart: 3", "Crystron Inclusion: 3", "Dark Grepher: 3",
            "Dracotail Mululu: 1", "Droll & Lock Bird: 2", "Harpie's Feather Storm: 0", "Herald of the Arc Light: 0",
            "K9-66a Jokul: 1", "Ketu Dracotail: 1", "Maliss <Q> White Binder: 0", "Snatch Steal: 3",
            "Stake your Soul!: 3", "Vanquish Soul Hollie Sue: 1", "Vanquish Soul Razen: 3",
            "White Dragon Wyverburster: 3", "Yummy★Snatchy: 1", "Zoodiac Drident: 3"
        }),
        ("TCG May 2026", "TCG February 2026", "ygorganization.com/prematurekewltunenerfs", new[]
        {
            "Branded Fusion: 1", "CXyz Gimmick Puppet Fanatix Machinix: 0", "Cupsy★Yummy Way: 1", "Cupsy☆Yummy: 1",
            "Dracotail Arthalion: 2", "Dracotail Lukias: 2", "Fairy Tail - Snow: 1", "K9-04 Noroi: 0",
            "Maliss <P> White Rabbit: 1", "Metamorphosis: 1", "Naturia Rosewhip: 0", "Premature Burial: 1",
            "Radiant Typhoon Chant: 1", "Rahu Dracotail: 1", "Synchro Overtake: 1"
        }),
        ("TCG September 2026", "TCG May 2026", "yugioh-card.com/en/limited/list_2026-09-21", new[]
        {
            "Archlord Kristya: 0", "Branded in High Spirits: 2", "Dimension Shifter: 0", "Elder Entity Norden: 3",
            "Elfnote Tinia: 1", "Kewl Tune Rotary: 0", "M-X-Saber Invoker: 3", "Metamorphosis: 3", "Mind Master: 3",
            "Mirage of Nightmare: 3", "PSY-Framelord Omega: 0", "Phantasmal Lord Ultimitl Bishbaalkin: 0",
            "Premature Burial: 3", "Prohibited Power Patron Portal - Terminus: 1", "Protectcode Talker: 0",
            "Purrely Delicious Memory: 3", "Reprodocus: 0", "Runick Fountain: 3", "Unchained Soul of Sharvara: 3",
            "Wind-Up Carrier Zenmaity: 3", "Wind-Up Hunter: 0"
        }),
    };

    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }

        Console.WriteLine("\nSearch");
        var search = new CardSearch(cards);
        Console.WriteLine($"  {search.CardCount} searchable cards");
        void Query(string q, Func<List<CardInfo>, bool> expect, string what)
        {
            var r = search.Find(q);
            Check(expect(r), $"\"{q}\" -> {r.Count} results, top: {string.Join(", ", r.Take(3).Select(c => c.Name))}  ({what})");
        }
        Query("dark magician", r => r.Count > 0 && r[0].Name == "Dark Magician", "exact name ranks first");
        Query("blue-eyes", r => r.Count > 5 && r.Take(5).All(c => c.Name.Contains("Blue-Eyes")), "name matches before text matches");
        Query("dragon lv8 atk>=3000", r => r.Count > 0 && r.All(c => c.Level == 8 && c.Attack >= 3000), "Type + level + ATK");
        Query("handtrap", r => r.Any(c => c.Name == "Ash Blossom & Joyous Spring") && r.Any(c => c.Name.StartsWith("Maxx")), "role filter");
        Query("synchro tuner", r => r.Count > 0 && r.All(c => (c.Type & 0x1000) != 0 && (c.Type & 0x2000) != 0), "two kinds");
        Query("trap \"destroy all\"", r => r.Any(c => c.Name == "Mirror Force"), "quoted phrase");
        Query("link2 cyberse", r => r.Count > 0 && r.All(c => (c.Type & CardType.Link) != 0 && c.Level == 2), "link rating + Type");
        Query("dragon -effect lv8", r => r.Any(c => c.Name == "Blue-Eyes White Dragon") && r.All(c => (c.Type & 0x20) == 0), "exclusion");

        Query("attr:light", r => r.Count > 0 && r.All(c => (c.Attribute & 0x10) != 0) && !r.Any(c => c.Name == "Dark Magician"), "strict Attribute (no name matches)");
        Query("type:spellcaster lv7", r => r.Any(c => c.Name == "Dark Magician"), "strict Type");
        Query("negateactivation trap", r => r.Any(c => c.Name == "Solemn Judgment"), "effect tag from the text");
        var dm = search.Find("\"dark magician\"").First(c => c.Name == "Dark Magician");
        var related = search.Related(dm);
        Check(related.Any(c => c.Name == "Dark Magician Girl") && related.Any(c => c.Name == "Dark Magic Attack") && related.All(c => c.Name != "Dark Magician"),
            $"related to Dark Magician: {related.Count} cards ({string.Join(", ", related.Take(4).Select(c => c.Name))}...)");

        Console.WriteLine("\nReleases (not out yet in an era)");
        var printings = Ygo.Engine.Data.PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        var datedLists = Banlist.LoadFolders(new[] { Path.Combine(root, "lflists"), Path.Combine(root, "vendor", "LFLists") });
        Check(datedLists.First(l => l.Name == "TCG March 2011").Date == "2011-03" && datedLists.First(l => l.Name.Contains("GOAT")).Date == "2005-04", "ban list dates read from their names");
        int flagged = 0, checkedDecks = 0;
        foreach (var file in Directory.GetFiles(Path.Combine(root, "decks", "history"), "*.ydk"))
        {
            var deck = Deck.LoadYdk(file);
            var list = datedLists.FirstOrDefault(l => l.Name == deck.Note("banlist"));
            if (list == null) continue;
            checkedDecks++;
            var early = DeckRules.Releases(deck, list, DeckRules.FormatEnd(list, datedLists), printings, cards);
            if (early.Count == 0) continue;
            flagged++;
            Console.WriteLine($"    {Path.GetFileNameWithoutExtension(file)} ({list.Name}): {string.Join("; ", early.Take(3).Select(p => p.Message))}");
        }
        Check(checkedDecks > 40 && flagged <= checkedDecks / 10, $"historical decks under their own era's list: {flagged} of {checkedDecks} have cards not out yet");
        var modern = new Deck { Main = search.Find("\"ash blossom\"").Take(1).Select(c => c.Code).ToList() };
        var march2011 = datedLists.First(l => l.Name == "TCG March 2011");
        Check(DeckRules.FormatEnd(march2011, datedLists) == "2012-03", $"the March 2011 format runs until {DeckRules.FormatEnd(march2011, datedLists)}");
        Check(DeckRules.Releases(modern, march2011, DeckRules.FormatEnd(march2011, datedLists), printings, cards).Count == 1, "Ash Blossom (2017) isn't out yet in March 2011");

        Console.WriteLine("\nDeck analysis");
        var archetypes = Archetypes.Load(Path.Combine(root, "vendor", "CardScripts", "archetype_setcode_constants.lua"));
        Check(archetypes.Count > 500, $"{archetypes.Count} archetype names");
        var references = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories).Select(f => Deck.LoadYdk(f)).ToList();
        foreach (var (name, expect) in new[] { ("burning-abyss", "Burning Abyss"), ("spright", "Spright"), ("six-samurai", "Six Samurai"), ("zombies", "") })
        {
            var file = Directory.GetFiles(Path.Combine(root, "decks"), name + ".ydk", SearchOption.AllDirectories).FirstOrDefault();
            if (file == null) { Check(false, $"{name} deck found"); continue; }
            var deck = Deck.LoadYdk(file);
            var main = archetypes.Main(deck, cards);
            var keys = main is { } m ? DeckAnalysis.KeyCards(m.code, references, cards) : new List<uint>();
            var used = new HashSet<uint>(deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, cards)));
            Check(expect.Length == 0 ? true : main?.name == expect,
                $"{name}: archetype {(main?.name ?? "none")} ({main?.count ?? 0} cards), uses {keys.Count(used.Contains)} of {keys.Count} key cards");
            var gaps = DeckAnalysis.ExtraDeckGaps(deck, cards);
            Check(gaps.Count <= 2, $"{name}: Extra Deck gaps {gaps.Count}{(gaps.Count > 0 ? ": " + string.Join("; ", gaps.Take(3).Select(g => $"{cards.Get(g.code)?.Name} ({g.reason})")) : "")}");
        }
        var noTuners = new Deck { Main = Enumerable.Repeat(search.Find("\"dark magician\"").First(c => c.Name == "Dark Magician").Code, 40).ToList(),
            Extra = search.Find("\"stardust dragon\"").Where(c => c.Name == "Stardust Dragon").Select(c => c.Code).Take(1).ToList() };
        Check(DeckAnalysis.ExtraDeckGaps(noTuners, cards).Any(g => g.reason.Contains("Tuner")), "a Synchro with no Tuner in the deck is flagged");
        int allGaps = 0, decksChecked = 0;
        foreach (var d in references.Where(d => d.Extra.Count > 0)) { decksChecked++; allGaps += DeckAnalysis.ExtraDeckGaps(d, cards).Count; }
        Check(allGaps <= decksChecked / 2, $"built-in decks: {allGaps} Extra Deck gaps across {decksChecked} decks (few false alarms)");

        Console.WriteLine("\nBan lists");
        var lists = Banlist.LoadFolders(new[] { Path.Combine(root, "vendor", "LFLists") });
        Console.WriteLine($"  loaded: {string.Join(", ", lists.Select(l => $"{l.Name}{(l.Whitelist ? " (whitelist)" : "")}"))}");
        var tcg = lists.FirstOrDefault(l => l.Name.EndsWith("TCG") && !l.Whitelist) ?? Banlist.None;
        var goat = lists.FirstOrDefault(l => l.Name.Contains("GOAT")) ?? Banlist.None;
        Check(tcg.Limits.Count > 50, $"current TCG list \"{tcg.Name}\" has {tcg.Limits.Count} entries");
        Check(goat.Whitelist, "GOAT is a whitelist");

        Console.WriteLine("\nValidation (our decks)");
        foreach (var file in Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk").OrderBy(f => f))
        {
            var deck = Deck.LoadYdk(file);
            var none = DeckRules.Validate(deck, Banlist.None, cards);
            var underTcg = DeckRules.Validate(deck, tcg, cards);
            var underGoat = DeckRules.Validate(deck, goat, cards);
            Check(none.Count == 0, $"{deck.Name}: legal with no limits ({none.Count} problems{(none.Count > 0 ? ": " + none[0].Message : "")})");
            Console.WriteLine($"         under {tcg.Name}: {underTcg.Count} problems{(underTcg.Count > 0 ? " e.g. " + underTcg[0].Message : "")}; under GOAT: {underGoat.Count}");
        }
        Console.WriteLine("\nValidation (historic decks, under the ban list of their era)");
        var eras = Banlist.LoadFolders(new[] { Path.Combine(root, "lflists") });
        int legal = 0, tagged = 0;
        foreach (var file in Directory.GetFiles(Path.Combine(root, "decks", "history"), "*.ydk").OrderBy(f => f))
        {
            var deck = Deck.LoadYdk(file);
            var era = eras.FirstOrDefault(l => l.Name == deck.Note("banlist"));
            if (era == null) { Check(false, $"{deck.Name}: ban list \"{deck.Note("banlist")}\" not found"); continue; }
            tagged++;
            var problems = DeckRules.Validate(deck, era, cards);
            if (problems.Count == 0) legal++;
            else Check(false, $"{deck.Name}: under {era.Name}: {problems[0].Message}");
        }
        Check(tagged > 50 && legal == tagged, $"{legal} of {tagged} historic decks legal under their era's list ({eras.Count} lists)");
        // The builds carry picks (docs/dev/NO-LIMITS-SPEC-2026-11-02.md 5.1): each validates under its list with its over: counted, the way
        // AlleyData.Problems does it (the build: block of alley-events.txt). The Brawl decks (nl-*-brawl) are the Alley list's: Problems checks them.
        var nlData = AlleyData.Load(Path.Combine(root, "campaign", AlleyData.FileName), cards);
        int peakLegal = 0, peakDecks = 0, brawlDecks = 0;
        foreach (var file in Directory.GetFiles(Path.Combine(root, "decks", "no-limits"), "*.ydk").OrderBy(f => f))
        {
            var deck = Deck.LoadYdk(file);
            if (deck.Name.EndsWith("-brawl", StringComparison.Ordinal)) { brawlDecks++; continue; }
            peakDecks++;
            var build = nlData.Builds.FirstOrDefault(b => b.Deck == deck.Name);
            var era = eras.FirstOrDefault(l => l.Name == deck.Note("banlist"));
            if (build == null) { Check(false, $"{deck.Name}: no build: block in {AlleyData.FileName} names it"); continue; }
            if (era == null) { Check(false, $"{deck.Name}: ban list \"{deck.Note("banlist")}\" not found"); continue; }
            if (build.List != era.Name) { Check(false, $"{deck.Name}: #banlist {era.Name} but build {build.Id}'s list: is {build.List}"); continue; }
            var played = deck.Clone();
            played.Side.Clear();
            var codes = played.Main.Concat(played.Extra).Select(c => Banlist.BaseCode(c, cards)).ToList();
            var withOver = new Banlist { Name = era.Name, Whitelist = era.Whitelist, Limits = new Dictionary<uint, int>(era.Limits) };
            foreach (var c in build.Over) withOver.Limits[c] = Math.Max(withOver.Allowed(c, cards), codes.Count(x => x == c));
            var problems = DeckRules.Validate(played, withOver, cards);
            if (problems.Count == 0) peakLegal++;
            else Check(false, $"{deck.Name}: under {era.Name} with {build.Over.Count} over: cards: {problems[0].Message}");
        }
        Check(peakDecks >= 7 && peakLegal == peakDecks, $"{peakLegal} of {peakDecks} No Limits peak lists legal under the list of their peak month with their over: counted "
              + $"(decks/no-limits; {brawlDecks} nl-*-brawl decks left to AlleyData.Problems, which checks them under the Alley list)");

        Console.WriteLine("\nThe list chain (No Limits P1: each added list is the one before it plus its source's changes)");
        int Limit(Dictionary<uint, int> limits, uint code) => limits.TryGetValue(code, out var n) ? n : 3;
        Dictionary<uint, int> ByBase(Banlist l)
        {
            var map = new Dictionary<uint, int>();
            foreach (var (code, n) in l.Limits) map[Banlist.BaseCode(code, cards)] = n;
            return map;
        }
        var tcgLists = eras.Where(l => l.Name.StartsWith("TCG ") && l.Date.Length > 0).OrderBy(l => l.Date, StringComparer.Ordinal).ToList();
        foreach (var (name, before, source, changes) in ListChain)
        {
            var b = eras.FirstOrDefault(l => l.Name == name);
            var a = eras.FirstOrDefault(l => l.Name == before);
            if (a == null || b == null) { Check(false, $"{name} and {before} both in lflists"); continue; }
            var expected = new Dictionary<uint, int>();
            var unknown = new List<string>();
            foreach (var change in changes)
            {
                int colon = change.LastIndexOf(": ", StringComparison.Ordinal);
                var card = cards.All.FirstOrDefault(c => c.Alias == 0 && c.Name == change.Substring(0, colon));
                if (card == null) unknown.Add(change.Substring(0, colon));
                else expected[card.Code] = int.Parse(change.Substring(colon + 2));
            }
            var (am, bm) = (ByBase(a), ByBase(b));
            var actual = am.Keys.Union(bm.Keys).Where(c => Limit(am, c) != Limit(bm, c)).ToDictionary(c => c, c => Limit(bm, c));
            var missing = expected.Where(e => !actual.TryGetValue(e.Key, out var n) || n != e.Value).Select(e => cards.Get(e.Key)?.Name ?? e.Key.ToString()).ToList();
            var extra = actual.Where(e => !expected.ContainsKey(e.Key)).Select(e => cards.Get(e.Key)?.Name ?? e.Key.ToString()).ToList();
            int at = tcgLists.IndexOf(b);
            bool adjacent = at > 0 && tcgLists[at - 1] == a;
            Check(unknown.Count + missing.Count + extra.Count == 0 && adjacent,
                $"{name} = {before} + {changes.Length} changes ({source}){(adjacent ? "" : $"; {before} isn't the list before it")}"
                + (unknown.Count > 0 ? $"; unknown: {string.Join(", ", unknown)}" : "") + (missing.Count > 0 ? $"; not applied: {string.Join(", ", missing.Take(5))}" : "")
                + (extra.Count > 0 ? $"; not in the source: {string.Join(", ", extra.Take(5))}" : ""));
        }
        var sep2026 = tcgLists.FindIndex(l => l.Name == "TCG September 2026");
        var chained = ListChain.Select(c => c.name).Concat(ListChain.Select(c => c.before)).ToHashSet();
        var gap = tcgLists.Where((l, i) => i <= sep2026 && string.CompareOrdinal(l.Date, "2023-09") >= 0 && !chained.Contains(l.Name)).Select(l => l.Name).ToList();
        Check(sep2026 >= 0 && ListChain[^1].name == "TCG September 2026" && gap.Count == 0,
            $"the chain runs from TCG September 2023 to the repo's TCG September 2026 with no list skipped{(gap.Count > 0 ? $" (unchained: {string.Join(", ", gap)})" : "")}");

        var original = Deck.LoadYdk(Path.Combine(root, "decks", "history", "chaos-yata.ydk"));
        var apr04 = eras.First(l => l.Name == "TCG April 2004");
        var ced = original.Main.First(c => cards.Get(c)?.Name.StartsWith("Chaos Emperor Dragon") == true);
        Check(cards.Get(ced)!.Name.Contains("Pre-Errata") && apr04.Allowed(ced, cards) == 1,
              $"{cards.Get(ced)!.Name} counts as the original under April 2004 (limited to {apr04.Allowed(ced, cards)})");

        Console.WriteLine("\nCard versions (artworks and original texts)");
        var versions = new CardVersions(cards);
        var darkMagician = versions.Arts(46986414);
        Check(darkMagician.Count > 5 && darkMagician[0] == 46986414 && versions.Texts(46986414).Count == 1,
              $"Dark Magician: {darkMagician.Count} artworks, one text");
        var sangan = versions.Texts(26202165).Select(c => cards.Get(c)!.Name).ToList();
        Check(sangan.Count == 3 && sangan[0] == "Sangan", $"Sangan texts: {string.Join(", ", sangan)}");
        Check(cards.Get(versions.Original(26202165, preferGoat: true)!.Value)!.Name == "Sangan (GOAT)"
              && cards.Get(versions.Original(26202165, preferGoat: false)!.Value)!.Name == "Sangan (Pre-Errata)"
              && versions.Original(46986414, preferGoat: true) == null, "original version: Goat for 2005 lists, pre-errata before");
        Check(versions.Arts(versions.Original(26202165, true)!.Value).Count == 1, "an original text has its own single artwork");
        Check(CardVersions.IsOriginalEra("TCG April 2004", out var g1) && !g1 && CardVersions.IsOriginalEra("2005.4 GOAT", out var g2) && g2
              && !CardVersions.IsOriginalEra("TCG September 2008", out _) && !CardVersions.IsOriginalEra("2026.10 TCG", out _),
              "original-text era: lists through 2005 and Goat");

        // Card versions phase 1 (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md): texts by entry (S1), dups (S2), the texts date
        // of a table (5.1) and what a slot plays there (5.2). The matrix runs on the spec's rows; S1/S2 on the real file too.
        Console.WriteLine("\nCard texts (texts.txt) and what a slot plays");
        var fixture = CardTexts.Parse(TextsCheck.Fixture, cards);
        var realPath = Path.Combine(root, "campaign", CardTexts.File);
        foreach (var (label, rowsOf) in new[] { ("spec rows", fixture) }.Concat(File.Exists(realPath) ? new[] { ("texts.txt", CardTexts.Load(realPath, cards)) } : Array.Empty<(string, CardTexts)>()))
        {
            var tv = new CardVersions(cards, rowsOf);
            var mindMaster = tv.Texts(96782886);
            Check(mindMaster.Count == 3 && mindMaster.Contains(96782896u) && mindMaster.Contains(511003019u) && tv.Arts(96782896).Count == 1,
                  $"{label}: Mind Master has 2 original texts, told apart by entry ({string.Join(", ", mindMaster)})");
            var ring = tv.Texts(83555666);
            var virus = tv.Texts(57728570);
            Check(ring.Count == 2 && ring.Contains(511000825u) && !ring.Contains(511000824u) && tv.Arts(511000825).Count == 1 && tv.Canonical(511000824) == 511000825
                  && virus.Count == 2 && virus.Contains(511000822u) && !virus.Contains(511000823u) && tv.Arts(511000822).Count == 1 && tv.Canonical(511000823) == 511000822
                  && tv.Arts(83555666).Count == 2,
                  $"{label}: Ring of Destruction and Crush Card Virus have one original text, one artwork; their dups show as 511000825 and 511000822");
            var sanganTexts = tv.Texts(26202165);
            Check(sanganTexts.SequenceEqual(new uint[] { 26202165, 504700178, 511002631 }) && rowsOf.Goat(26202165) == 504700178
                  && rowsOf.PrintedText(26202165, "2002-06") == 511002631 && rowsOf.PrintedText(26202165, "2016-08") == 511002631
                  && rowsOf.PrintedText(26202165, "2016-09") == 0 && rowsOf.PrintedText(26202165, "") == 0
                  && tv.TextLabel(511002631) == "Original text (until Sep 2016)" && tv.TextLabel(26202165) == "Current text",
                  $"{label}: Sangan's texts (current, GOAT, Pre-Errata until {rowsOf.Row(511002631)?.Until}); a 2002 printing carries the original, a September 2016 one the current");
            Check(tv.Original(96782886, false) == 96782896 && tv.Original(15259703, false) == null,
                  $"{label}: the editor's default original skips texts players can't own (Mind Master's cpu row, Toon World's OCG text)");
            Check(!tv.PlayerTexts(96782886).Contains(511003019u) && tv.PlayerTexts(96782886).Contains(96782896u)
                  && tv.PlayerTexts(15259703).SequenceEqual(new uint[] { 15259703 }) && tv.Texts(15259703).Contains(511004007u)
                  && tv.PlayerTexts(511004007).Contains(511004007u) && tv.PlayerTexts(511003019).Contains(511003019u),
                  $"{label}: CARD TEXT offers no cpu or never row (Mind Master's OCG text, Toon World's) unless the slot holds it");
        }

        var historyLists = Banlist.LoadFolders(new[] { Path.Combine(root, "vendor", "LFLists"), Path.Combine(root, "lflists") }).ToList();
        Banlist ListNamed(string name) => historyLists.First(l => l.Name == name);
        var octRules = TextRules.ForList(ListNamed("TCG October 2026"), historyLists);
        var aprRules = TextRules.ForList(ListNamed("TCG April 2015"), historyLists);
        var chaosRules = TextRules.ForList(ListNamed("TCG April 2004"), historyLists);
        var goatList = TextRules.ForList(ListNamed("2005.4 GOAT"), historyLists);
        var goatCup = TextRules.ForList(ListNamed("TCG April 2005"), historyLists, "2005-09");
        var beginner = TextRules.ForList(null, historyLists, "", 2005);
        var noLimits = TextRules.ForList(Banlist.None, historyLists);
        Check(octRules.AsOf == TextRules.Latest && !octRules.Goat && !octRules.Printed && aprRules.AsOf == "2015-07" && !aprRules.Goat
              && chaosRules.AsOf == "2005-04" && !chaosRules.Goat && goatList.Goat && goatList.AsOf == TextRules.GoatUntil
              && goatCup.Goat && goatCup.AsOf == "2005-09" && beginner.AsOf == "2005-12" && !beginner.Goat && noLimits.Printed,
              $"texts dates: October 2026 {octRules.AsOf}, April 2015 {aprRules.AsOf}, April 2004 {chaosRules.AsOf}, the GOAT list {goatList.Describe()}, "
              + $"a 2005-09 pool {goatCup.Describe()}, the Beginner band {beginner.AsOf}, No limits {noLimits.Describe()}");
        // A gap in the list history: the TCG list after April 2005 on record is September 2008. A Goat table by its list's
        // month plays 2005 texts, not 2008's, so Ultimate Offering's original (errata'd 2006) plays as printed there.
        const uint OfferingBase = 80604091, OfferingPre = 511003023;
        var april2005 = TextRules.ForList(ListNamed("TCG April 2005"), historyLists);
        var offeringRows = File.Exists(realPath) ? CardTexts.Load(realPath, cards) : CardTexts.Parse("511003023 until 2006-05", cards);
        Check(april2005.Goat && april2005.AsOf == TextRules.GoatUntil && DeckRules.FormatEnd(ListNamed("TCG April 2005"), historyLists) == "2008-09"
              && PlayTexts.PlayCode(OfferingPre, null, april2005, offeringRows, cards) == OfferingPre
              && PlayTexts.PlayCode(OfferingPre, null, octRules, offeringRows, cards) == OfferingBase,
              $"TCG April 2005 (format end {DeckRules.FormatEnd(ListNamed("TCG April 2005"), historyLists)}) plays {april2005.Describe()} as of {april2005.AsOf}: "
              + $"Ultimate Offering's original (until {offeringRows.Row(OfferingPre)?.Until}) plays as printed there");
        Check(new TextRules("2016-07", false, false).Describe() == "texts of July 2016" && TextRules.PrintedText.Describe() == "as printed"
              && TextRules.Today.Describe() == "today's texts" && default(TextRules).Allows(fixture.Row(511002631)!) == false,
              "texts dates describe themselves; a default rules value plays today's texts");

        // The PlayCode matrix (section 12): {base, Pre-Errata, GOAT, dup, ruling-only GOAT, cpu, never} x {no copy (phase 1),
        // a current copy, an original copy} x {today, the Goat Cup (2005-09), July 2016, printed}.
        const uint SanganBase = 26202165, SanganPre = 511002631, SanganGoat = 504700178, RingBase = 83555666, RingDup = 511000824, RingPre = 511000825;
        const uint GoatBase = 73915051, GoatScapegoat = 504700123, MindBase = 96782886, MindCpu = 511003019, ToonBase = 15259703, ToonOcg = 511004007;
        var tables = new[] { ("today", TextRules.Today), ("Goat Cup", new TextRules("2005-09", false, true)), ("July 2016", new TextRules("2016-07", false, false)), ("printed", TextRules.PrintedText) };
        CardCopy? CopyOf(uint text) => new CardCopy { Code = 0, Text = text };
        var matrix = new (string slotName, uint slot, uint originalText, uint[] noCopy, uint[] current, uint[] original)[]
        {
            ("Sangan", SanganBase, SanganPre, new[] { SanganBase, SanganBase, SanganBase, SanganBase }, new[] { SanganBase, SanganBase, SanganBase, SanganBase }, new[] { SanganBase, SanganBase, SanganBase, SanganBase }),
            ("Sangan (Pre-Errata)", SanganPre, SanganPre, new[] { SanganBase, SanganGoat, SanganPre, SanganPre }, new[] { SanganBase, SanganBase, SanganBase, SanganBase }, new[] { SanganBase, SanganGoat, SanganPre, SanganPre }),
            ("Sangan (GOAT)", SanganGoat, SanganPre, new[] { SanganBase, SanganGoat, SanganPre, SanganPre }, new[] { SanganBase, SanganBase, SanganBase, SanganBase }, new[] { SanganBase, SanganGoat, SanganPre, SanganPre }),
            ("Ring of Destruction dup", RingDup, RingPre, new[] { RingBase, RingDup, RingBase, RingDup }, new[] { RingBase, RingBase, RingBase, RingBase }, new[] { RingBase, RingDup, RingBase, RingDup }),
            ("Scapegoat (GOAT), ruling only", GoatScapegoat, 0, new[] { GoatBase, GoatScapegoat, GoatBase, GoatBase }, new[] { GoatBase, GoatScapegoat, GoatBase, GoatBase }, new[] { GoatBase, GoatScapegoat, GoatBase, GoatBase }),
            ("Mind Master cpu row", MindCpu, 96782896, new[] { MindBase, MindBase, MindBase, MindBase }, new[] { MindBase, MindBase, MindBase, MindBase }, new[] { MindBase, MindBase, MindBase, MindBase }),
            ("Toon World never row", ToonOcg, 0, new[] { ToonBase, ToonBase, ToonBase, ToonBase }, new[] { ToonBase, ToonBase, ToonBase, ToonBase }, new[] { ToonBase, ToonBase, ToonBase, ToonBase }),
        };
        int cells = 0;
        var wrong = new List<string>();
        foreach (var row in matrix)
            foreach (var (copyName, copy, expect) in new[] { ("no copy", (CardCopy?)null, row.noCopy), ("current copy", CopyOf(0), row.current), ("original copy", CopyOf(row.originalText), row.original) })
                for (int t = 0; t < tables.Length; t++)
                {
                    cells++;
                    var played = PlayTexts.PlayCode(row.slot, copy, tables[t].Item2, fixture, cards);
                    if (played != expect[t]) wrong.Add($"{row.slotName}, {copyName}, {tables[t].Item1}: {played} (expected {expect[t]})");
                }
        Check(wrong.Count == 0, $"PlayCode: {cells - wrong.Count} of {cells} cells as section 5.2 says{(wrong.Count > 0 ? ": " + string.Join("; ", wrong.Take(4)) : "")}");
        Check(PlayTexts.PlayCode(SanganPre, null, TextRules.Today, CardTexts.Empty, cards) == SanganBase
              && PlayTexts.PlayCode(SanganPre, null, TextRules.PrintedText, CardTexts.Empty, cards) == SanganPre
              && PlayTexts.PlayCode(SanganPre, CopyOf(511003012), TextRules.PrintedText, fixture, cards) == SanganBase,
              "a Pre-Errata entry with no row plays current but where printed texts play; a copy of another card's text doesn't count");
        var mixed = new Deck { Main = { SanganPre, SanganPre, SanganBase, GoatScapegoat, RingDup }, Side = { SanganPre } };
        var atToday = PlayTexts.ForTable(mixed, null, TextRules.Today, fixture, cards, out var downToday);
        var at2011 = PlayTexts.ForTable(mixed, new CardCopy?[] { CopyOf(SanganPre), CopyOf(0), CopyOf(0), null, null }, new TextRules("2012-03", false, false), fixture, cards, out var down2011);
        Check(atToday.Main.SequenceEqual(new[] { SanganBase, SanganBase, SanganBase, GoatBase, RingBase }) && atToday.Side.SequenceEqual(new[] { SanganBase })
              && downToday.SequenceEqual(new[] { SanganBase, GoatBase, RingBase }) && mixed.Main[0] == SanganPre
              && at2011.Main.SequenceEqual(new[] { SanganPre, SanganBase, SanganBase, GoatBase, RingBase }) && down2011.SequenceEqual(new[] { SanganBase, GoatBase, RingBase }),
              $"ForTable maps a copy of the deck slot by slot (copies line up with Main then Extra; a slot without one plays current) and names each downgraded card once: today {downToday.Count}, March 2011 {down2011.Count}");

        var bad = new Deck { Main = Enumerable.Repeat(89631139u, 4).ToList(), Extra = { 63977008 } };
        var badProblems = DeckRules.Validate(bad, Banlist.None, cards);
        Check(badProblems.Any(p => p.Message.Contains("at least 40")) && badProblems.Any(p => p.Message.Contains("4 copies"))
              && badProblems.Any(p => p.Message.Contains("can't go in the Extra Deck")), "catches size, copies, and wrong section");

        Console.WriteLine("\nOpening odds");
        Check(Math.Abs(OpeningOdds.AtLeastOne(40, 3, 5) - 0.3376) < 0.001, $"3 copies in 40, 5-card hand: {OpeningOdds.AtLeastOne(40, 3, 5):P1} (expected 33.8%)");
        Check(Math.Abs(OpeningOdds.AtLeastOne(40, 9, 5) - 0.7418) < 0.001, $"9 starters in 40, 5 cards: {OpeningOdds.AtLeastOne(40, 9, 5):P1}; 6 cards: {OpeningOdds.AtLeastOne(40, 9, 6):P1}");

        Console.WriteLine("\nDeck codes");
        foreach (var file in Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk").OrderBy(f => f))
        {
            var deck = Deck.LoadYdk(file);
            var viaYdke = DeckCodes.Parse(DeckCodes.ToYdke(deck));
            var viaYdk = DeckCodes.Parse(DeckCodes.ToYdk(deck));
            Check(viaYdke != null && viaYdke.Main.SequenceEqual(deck.Main) && viaYdke.Extra.SequenceEqual(deck.Extra) && viaYdk != null
                  && viaYdk.Main.SequenceEqual(deck.Main), $"{deck.Name}: YDKE and .ydk round trip");
        }

        Console.WriteLine("\nBot test (background thread)");
        var tester = new DeckTester();
        var mine = Deck.LoadYdk(Path.Combine(root, "decks", "warriors.ydk"));
        var theirs = Deck.LoadYdk(Path.Combine(root, "decks", "classic.ydk"));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        tester.Start(cards, scripts, mine, theirs, Personality.Balanced, 40, 1234);
        while (tester.Running) Thread.Sleep(50);
        Check(tester.Error == null && tester.Completed == 40,
            $"40 games in {timer.Elapsed.TotalSeconds:F1}s: won {tester.Wins} ({tester.WinsFirst}/{tester.GamesFirst} first, {tester.WinsSecond}/{tester.GamesSecond} second), avg {tester.TotalTurns / Math.Max(1.0, tester.Completed):F1} turns{(tester.Error != null ? ", error: " + tester.Error : "")}");

        Console.WriteLine(failures == 0 ? "\nAll deck-building checks passed." : $"\n{failures} check(s) FAILED.");
        return failures == 0 ? 0 : 3;
    }
}
