using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>One line of a featured shelf: a real set, the opponent decks it serves, and why (the cards, one short line).</summary>
    public sealed class ShelfPick
    {
        public string Code = "", Why = "";
        public List<string> Decks = new List<string>();
        /// <summary>A "soon:" line: released after the circuit's era, it opens with levels before the band ends.</summary>
        public bool Soon;
    }

    /// <summary>
    /// The card shop's featured shelves (campaign/featured.txt, SHOP-REWORK-SPEC section 1): 3-4 real sets per circuit,
    /// chosen because they hold that circuit's opponents' cards, plus a post-game shelf. "shelf: &lt;circuit name&gt; |
    /// postgame" starts a shelf; "set: CODE | deck, deck | why" and "soon: CODE | decks | why" are its lines; "#" starts a
    /// comment line.
    /// </summary>
    public sealed class FeaturedShelves
    {
        public const string FileName = "featured.txt", PostGame = "postgame";

        private readonly List<(string name, List<ShelfPick> picks)> _shelves;

        private FeaturedShelves(List<(string, List<ShelfPick>)> shelves) => _shelves = shelves;

        /// <summary>The shelves' names, in file order.</summary>
        public IEnumerable<string> Names => _shelves.Select(s => s.name);

        /// <summary>A missing file is an empty shelf list.</summary>
        public static FeaturedShelves Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : new FeaturedShelves(new List<(string, List<ShelfPick>)>());

        public static FeaturedShelves Parse(string text)
        {
            var shelves = new List<(string, List<ShelfPick>)>();
            List<ShelfPick>? current = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var key = line.Substring(0, colon).Trim().ToLowerInvariant();
                var value = line.Substring(colon + 1).Trim();
                if (key == "shelf")
                {
                    current = new List<ShelfPick>();
                    shelves.Add((value, current));
                    continue;
                }
                if (current == null || (key != "set" && key != "soon")) continue;
                var parts = value.Split('|').Select(p => p.Trim()).ToArray();
                if (parts[0].Length == 0) continue;
                current.Add(new ShelfPick
                {
                    Code = parts[0],
                    Decks = parts.Length > 1 ? parts[1].Split(',').Select(d => d.Trim()).Where(d => d.Length > 0).ToList() : new List<string>(),
                    Why = parts.Length > 2 ? string.Join(" | ", parts.Skip(2)) : "",
                    Soon = key == "soon",
                });
            }
            return new FeaturedShelves(shelves);
        }

        /// <summary>A shelf's lines in file order (a circuit's name or <see cref="PostGame"/>; empty when there's none).</summary>
        public IReadOnlyList<ShelfPick> For(string shelf) =>
            _shelves.FirstOrDefault(s => s.name.Equals(shelf, StringComparison.OrdinalIgnoreCase)).picks ?? (IReadOnlyList<ShelfPick>)Array.Empty<ShelfPick>();

        /// <summary>
        /// What's wrong with the file against the campaign (empty: nothing): every circuit and the post-game have a shelf
        /// and every shelf is one of them; 3 or 4 set: lines and at most 2 soon: lines a shelf, no set twice; a set: is in
        /// the catalog and released by the circuit's era (so it's on sale while the circuit is current); a soon: is released
        /// after the era and by the next band's era; every deck named is a match deck of the circuit with a Main or Extra
        /// Deck card in the set; every match deck is named by a line; the post-game shelf names no decks.
        /// </summary>
        public List<string> Problems(Circuits circuits, PackCatalog catalog, Func<string, Deck?> loadDeck, ICardDataProvider cards)
        {
            var problems = new List<string>();
            var names = circuits.All.Select(c => c.Name).ToList();
            foreach (var group in _shelves.GroupBy(s => s.name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                problems.Add($"shelf {group.Key}: listed {group.Count()} times");
            foreach (var name in names.Append(PostGame))
                if (!_shelves.Any(s => s.name.Equals(name, StringComparison.OrdinalIgnoreCase))) problems.Add($"shelf {name}: missing");
            int lastEra = circuits.All.Select(c => c.Era).DefaultIfEmpty(0).Max();
            var deckCards = new Dictionary<string, HashSet<uint>?>();
            HashSet<uint>? CardsOf(string deck)
            {
                if (deckCards.TryGetValue(deck, out var known)) return known;
                var d = loadDeck(deck);
                return deckCards[deck] = d == null ? null : new HashSet<uint>(d.Main.Concat(d.Extra).Select(c => Banlist.BaseCode(c, cards)));
            }
            foreach (var (name, picks) in _shelves)
            {
                bool post = name.Equals(PostGame, StringComparison.OrdinalIgnoreCase);
                var circuit = circuits.All.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (!post && circuit == null) { problems.Add($"shelf {name}: not a circuit in circuits.txt"); continue; }
                int era = post ? lastEra : circuit!.Era;
                int? nextEra = circuits.All.Select(c => c.Era).Where(e => e > era).Select(e => (int?)e).DefaultIfEmpty(null).Min();
                int sets = picks.Count(p => !p.Soon), soons = picks.Count(p => p.Soon);
                if (sets < 3 || sets > 4) problems.Add($"shelf {name}: {sets} set: lines (3 or 4)");
                if (soons > 2) problems.Add($"shelf {name}: {soons} soon: lines (at most 2)");
                foreach (var dup in picks.GroupBy(p => p.Code).Where(g => g.Count() > 1)) problems.Add($"shelf {name}: {dup.Key} listed twice");
                var matchDecks = post ? new List<string>() : circuit!.Matches.Select(m => m.Deck).Distinct().ToList();
                foreach (var pick in picks)
                {
                    var where = $"shelf {name}, {(pick.Soon ? "soon" : "set")} {pick.Code}";
                    var set = catalog.Find(pick.Code);
                    if (set == null) { problems.Add($"{where}: not in the shop's catalog"); continue; }
                    int year = int.TryParse(set.Year, out var y) ? y : 0;
                    if (!pick.Soon && year > era) problems.Add($"{where}: released {year}, after the era {era}");
                    if (pick.Soon && post) problems.Add($"{where}: the post-game shelf has no soon: lines");
                    else if (pick.Soon && (year <= era || nextEra == null || year > nextEra)) problems.Add($"{where}: released {year}, not after {era} and by the next era ({nextEra?.ToString() ?? "none"})");
                    if (post && pick.Decks.Count > 0) problems.Add($"{where}: the post-game shelf names no decks");
                    var pool = new HashSet<uint>(set.Pool.Values.SelectMany(c => c));
                    foreach (var deck in pick.Decks)
                    {
                        if (!post && !matchDecks.Contains(deck)) { problems.Add($"{where}: {deck} isn't a match deck of the circuit"); continue; }
                        var codes = CardsOf(deck);
                        if (codes == null) problems.Add($"{where}: no deck {deck}");
                        else if (!codes.Any(pool.Contains)) problems.Add($"{where}: {deck} has no Main or Extra Deck card in the set");
                    }
                }
                foreach (var deck in matchDecks.Where(d => !picks.Any(p => p.Decks.Contains(d))))
                    problems.Add($"shelf {name}: {deck} isn't served by any line");
            }
            return problems;
        }
    }

    /// <summary>Which part of the shop's list a row is in (the back catalog is the player's "back shelves").</summary>
    public enum ShelfSection { Featured, NewArrivals, House, Imports, BackCatalog }

    /// <summary>What a row of the shop's list is: a set, a section header, a year sub-header of the back shelves, the back shelves' toggle, or New arrivals' "+N more" line.</summary>
    public enum ShelfRowKind { Set, Header, YearHeader, BackToggle, More }

    /// <summary>One row of the shop's list.</summary>
    public sealed class ShelfRow
    {
        public ShelfSection Section;
        /// <summary>The set (null for a header, a year sub-header, the toggle and the "+N more" line).</summary>
        public PackSet? Set;
        public ShelfRowKind Kind;
        /// <summary>A non-set row's text ("FOR THE BEGINNER CIRCUIT", "2015", "Back shelves · 64 sets ▸", "+3 more on the back shelves").</summary>
        public string Header = "";
        /// <summary>A featured set not on sale yet (a soon: line): shown greyed, its buy buttons off, opening at <see cref="OpensAtLevel"/>.</summary>
        public bool Locked;
        public int OpensAtLevel;
        /// <summary>A real set that arrived in the last <see cref="Arrivals.Visits"/> visits (the NEW chip).</summary>
        public bool New;
        /// <summary>A featured set's reason (the featured line's cards).</summary>
        public string Why = "";
    }

    /// <summary>
    /// The shop's list for one visit (SHOP-REWORK-SPEC section 1): the featured shelf for the current circuit (or the
    /// post-game), New arrivals, Gus's house packs, the OCG imports, and the back shelves (every other real set on sale,
    /// nothing deleted). Each set appears once, in that order of sections; rarity sets come after sets with Commons.
    /// </summary>
    public sealed class ShopShelves
    {
        public const string HouseHeader = "GUS'S HOUSE PACKS · the decks you'll face", NewHeader = "NEW ARRIVALS", ImportsHeader = "OCG IMPORTS";

        /// <summary>The circuit's name, or <see cref="FeaturedShelves.PostGame"/>.</summary>
        public string Shelf = "";
        /// <summary>The section's set rows (no headers): featured live rows then soon rows; up to <see cref="Arrivals.MaxRows"/> new arrivals; the back shelves newest year first.</summary>
        public List<ShelfRow> Featured = new List<ShelfRow>(), NewArrivals = new List<ShelfRow>(), House = new List<ShelfRow>(),
            Imports = new List<ShelfRow>(), Back = new List<ShelfRow>();
        /// <summary>New sets beyond <see cref="Arrivals.MaxRows"/> (they're on the back shelves).</summary>
        public int MoreNew;
        /// <summary>The set selected on opening: the first live featured set with Commons, else the first real set on sale with Commons, else the first live featured set.</summary>
        public PackSet? DefaultPick;
        /// <summary>Every buyable set, each once, in the list's order.</summary>
        public IReadOnlyList<PackSet> OnSale = Array.Empty<PackSet>();

        public static ShopShelves Build(Profile profile, Circuits circuits, PackCatalog catalog, FeaturedShelves featured,
            IReadOnlyList<PackSet> housePacks, IReadOnlyList<PackSet> imports)
        {
            var shelves = new ShopShelves();
            bool post = circuits.AllCleared(profile);
            shelves.Shelf = post || circuits.All.Count == 0 ? FeaturedShelves.PostGame : circuits.All[circuits.Current(profile)].Name;
            int shopYear = circuits.ShopYear(profile), era = circuits.BandEra(profile);
            int start = profile.BandStartLevel.TryGetValue(era, out var l) ? l : profile.Level;
            bool OnSale(PackSet s) => int.TryParse(s.Year, out var y) && y <= shopYear;
            var real = catalog.Sets.Where(OnSale).ToList();

            // The featured shelf: live rows (sets with Commons first, each in file order), then the locked soon rows.
            var live = new List<ShelfRow>();
            var locked = new List<ShelfRow>();
            var placed = new HashSet<PackSet>();
            foreach (var pick in featured.For(shelves.Shelf))
            {
                var set = catalog.Find(pick.Code);
                if (set == null || !placed.Add(set)) continue;
                var row = new ShelfRow { Section = ShelfSection.Featured, Set = set, Why = pick.Why, New = Arrivals.IsNew(profile, set) };
                if (OnSale(set)) live.Add(row);
                else
                {
                    row.Locked = true;
                    row.OpensAtLevel = start + (int.TryParse(set.Year, out var y) ? y : era) - era;
                    locked.Add(row);
                }
            }
            shelves.Featured = live.OrderBy(r => PackCatalog.IsRaritySet(r.Set!) ? 1 : 0).Concat(locked).ToList();

            // New arrivals: Commons first, then newest; the rest go to the back shelves.
            var fresh = real.Where(s => !placed.Contains(s) && Arrivals.IsNew(profile, s))
                .OrderBy(s => PackCatalog.IsRaritySet(s) ? 1 : 0).ThenByDescending(s => s.Info.Date, StringComparer.Ordinal).ThenBy(s => s.Info.Name).ToList();
            shelves.NewArrivals = fresh.Take(Arrivals.MaxRows).Select(s => new ShelfRow { Section = ShelfSection.NewArrivals, Set = s, New = true }).ToList();
            shelves.MoreNew = Math.Max(0, fresh.Count - Arrivals.MaxRows);
            foreach (var r in shelves.NewArrivals) placed.Add(r.Set!);

            shelves.House = housePacks.Select(s => new ShelfRow { Section = ShelfSection.House, Set = s }).ToList();
            shelves.Imports = imports.Select(s => new ShelfRow { Section = ShelfSection.Imports, Set = s }).ToList();
            // The back shelves: newest year first; in a year, sets with Commons first, then by name.
            shelves.Back = real.Where(s => !placed.Contains(s))
                .OrderByDescending(s => s.Year, StringComparer.Ordinal).ThenBy(s => PackCatalog.IsRaritySet(s) ? 1 : 0).ThenBy(s => s.Info.Name)
                .Select(s => new ShelfRow { Section = ShelfSection.BackCatalog, Set = s, New = Arrivals.IsNew(profile, s) }).ToList();

            shelves.OnSale = live.Concat(shelves.NewArrivals).Concat(shelves.House).Concat(shelves.Imports).Concat(shelves.Back).Select(r => r.Set!).ToList();
            var realOnSale = live.Concat(shelves.NewArrivals).Concat(shelves.Back).Select(r => r.Set!).ToList();
            shelves.DefaultPick = live.Select(r => r.Set!).FirstOrDefault(s => !PackCatalog.IsRaritySet(s))
                ?? realOnSale.FirstOrDefault(s => !PackCatalog.IsRaritySet(s))
                ?? live.Select(r => r.Set).FirstOrDefault()
                ?? realOnSale.FirstOrDefault();
            return shelves;
        }

        /// <summary>The featured header: "FOR THE BEGINNER CIRCUIT", "FOR THE CHAMPION" in the post-game.</summary>
        public string FeaturedHeader => Shelf == FeaturedShelves.PostGame ? "FOR THE CHAMPION" : "FOR THE " + Shelf.ToUpperInvariant();

        /// <summary>The back shelves' toggle row: "Back shelves · 64 sets ▸" (▾ when open).</summary>
        public string BackToggle(bool open) => $"Back shelves · {Back.Count} set{(Back.Count == 1 ? "" : "s")} {(open ? "▾" : "▸")}";

        /// <summary>
        /// The flat list for paging, with its header rows: FOR THE &lt;CIRCUIT&gt;, NEW ARRIVALS (when any, then "+N more on
        /// the back shelves"), GUS'S HOUSE PACKS, OCG IMPORTS (when any), and the back shelves (one toggle row; open, year
        /// sub-headers and their sets). A <paramref name="filter"/> matches a set's name, code or year in every section,
        /// opens the back shelves and hides the sections it leaves empty.
        /// </summary>
        public List<ShelfRow> Rows(bool backOpen, string filter)
        {
            var rows = new List<ShelfRow>();
            var f = (filter ?? "").Trim();
            bool searching = f.Length > 0;
            bool Match(ShelfRow r) => !searching || r.Set == null
                || r.Set.Info.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                || r.Set.Info.Code.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                || (r.Set.Year.Length > 0 && r.Set.Year.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
            void Section(ShelfSection section, string header, List<ShelfRow> sets, bool always)
            {
                var shown = sets.Where(Match).ToList();
                if (shown.Count == 0 && (searching || !always)) return;
                rows.Add(new ShelfRow { Section = section, Kind = ShelfRowKind.Header, Header = header });
                rows.AddRange(shown);
            }
            Section(ShelfSection.Featured, FeaturedHeader, Featured, always: true);
            Section(ShelfSection.NewArrivals, NewHeader, NewArrivals, always: false);
            if (!searching && NewArrivals.Count > 0 && MoreNew > 0)
                rows.Add(new ShelfRow { Section = ShelfSection.NewArrivals, Kind = ShelfRowKind.More, Header = $"+{MoreNew} more on the back shelves" });
            Section(ShelfSection.House, HouseHeader, House, always: false);
            Section(ShelfSection.Imports, ImportsHeader, Imports, always: false);
            var back = Back.Where(Match).ToList();
            bool open = backOpen || searching;
            if (back.Count > 0 || (!searching && Back.Count > 0))
            {
                rows.Add(new ShelfRow { Section = ShelfSection.BackCatalog, Kind = ShelfRowKind.BackToggle, Header = BackToggle(open) });
                if (open)
                    foreach (var year in back.GroupBy(r => r.Set!.Year))
                    {
                        rows.Add(new ShelfRow { Section = ShelfSection.BackCatalog, Kind = ShelfRowKind.YearHeader, Header = year.Key });
                        rows.AddRange(year);
                    }
            }
            return rows;
        }
    }

    /// <summary>
    /// New arrivals (SHOP-REWORK-SPEC section 1): a real set is new for <see cref="Visits"/> shop visits from the visit its
    /// year opened on. <see cref="Profile.ShopYearSeen"/> only grows, so New Game+ brings nothing back as new; an old save
    /// or a new game's first visit sets it without marking anything new.
    /// </summary>
    public static class Arrivals
    {
        /// <summary>Visits a set stays new (the one it arrived on and the next two), and the most New arrivals rows [placeholder].</summary>
        public const int Visits = 3, MaxRows = 6;

        /// <summary>
        /// A shop visit from the campaign (not Prize Pack reveals or debug calls): counts it, records every year in
        /// (ShopYearSeen, shopYear] as arrived on it, drops arrivals no longer new, and returns the years new this visit,
        /// oldest first. The first visit ever (ShopYearSeen 0) records nothing and returns none; <see cref="ShopVoice.Footer"/>
        /// still plays that visit's arrival line ("the opening stock").
        /// </summary>
        public static List<int> Visit(Profile profile, int shopYear)
        {
            profile.ShopVisits++;
            var arrived = new List<int>();
            if (profile.ShopYearSeen == 0) profile.ShopYearSeen = shopYear;
            else
                for (int y = profile.ShopYearSeen + 1; y <= shopYear; y++)
                {
                    profile.Arrivals[y] = profile.ShopVisits;
                    arrived.Add(y);
                }
            profile.ShopYearSeen = Math.Max(profile.ShopYearSeen, shopYear);
            foreach (var old in profile.Arrivals.Where(a => profile.ShopVisits - a.Value >= Visits).Select(a => a.Key).ToList())
                profile.Arrivals.Remove(old);
            return arrived;
        }

        /// <summary>Whether a real set's year arrived in the last <see cref="Visits"/> visits.</summary>
        public static bool IsNew(Profile profile, PackSet set) =>
            int.TryParse(set.Year, out var year) && profile.Arrivals.TryGetValue(year, out var at) && profile.ShopVisits - at < Visits;
    }
}
