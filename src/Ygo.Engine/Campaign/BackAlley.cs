using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ygo.Engine.AI;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// A table's stakes: Street (Dumpster Row), Den (The Basement Game, The Black Market), Orvel's Table, Nyx's Table (Den
    /// stakes), No Limits (docs/dev/NO-LIMITS-SPEC-2026-11-02.md: <see cref="AlleyEconomy.NoLimitsStake"/> held, no list).
    /// </summary>
    public enum AlleyTier { Street, Den, Orvel, Nyx, NoLimits }

    /// <summary>An authored seat (Orvel's, Nyx's and No Limits' tables): `seat: &lt;n&gt; &lt;kind&gt; [character deck] | skill x | event: e | else ... | allow: cards | as-built`.</summary>
    public sealed class AlleySeatSpec
    {
        public int Seat;
        /// <summary>regular | headliner | authored | pool | champion (the last two: No Limits, a build drawn from the table's `pool:`, and its end chair).</summary>
        public string Kind = "", Character = "", Deck = "", Event = "";
        public double Skill;
        /// <summary>The <see cref="Alley.BossOnly"/> cards this seat's authored deck may run (base codes).</summary>
        public HashSet<uint> Allow = new HashSet<uint>();
        /// <summary>A headliner seat's stand-in while it isn't ready ("regular 0.97").</summary>
        public string Else = "";
        /// <summary>
        /// `as-built`: a rated roster deck seated as built (Nyx's seat 2, chaos-yata): checked by its own history, not the alley's
        /// exclusions, and its panel Elo is its own rating. Without it an authored deck is one built for the alley (decks/alley):
        /// checked, and shown at its recipe's rating plus the splash gain of the table it opens from.
        /// </summary>
        public bool AsBuilt;
    }

    /// <summary>One alley table (campaign/alley-events.txt): its event's splash knobs, base decks, headliners and seats.</summary>
    public sealed class AlleyTable
    {
        public string Id = "", Name = "", Opens = "", Mat = "", Music = "", Closer = "";
        public AlleyTier Tier;
        public AlleyEvent? Event;
        public double SplashElo;
        public List<string> Bases = new List<string>(), Headliners = new List<string>();
        /// <summary>No Limits: the `build:` ids its seats 1-3 rotate through (`pool:`); its `champions:` are <see cref="Headliners"/>.</summary>
        public List<string> Pool = new List<string>();
        /// <summary>A <see cref="Profile.Seen"/> key the table also needs to unlock (`gate:`; No Limits: <see cref="BackAlley.WallEvent"/>).</summary>
        public string Gate = "";
        /// <summary>No Limits: the window its ready builds must rate in (`elo: min-max`; <see cref="AlleyData.Problems"/> checks it).</summary>
        public double MinElo, MaxElo;
        /// <summary>Authored seats (orvel, nyx and nolimits tiers).</summary>
        public List<AlleySeatSpec> Seats = new List<AlleySeatSpec>();
        public int SeatCount => Seats.Count > 0 ? Seats.Count : BackAlley.Matches;
        /// <summary>`ready: no`: the table stays locked ("Coming soon") until its authored decks are built (Orvel's, Nyx's).</summary>
        public bool Ready = true;
    }

    /// <summary>A headliner (docs/dev/HEADLINERS-2026-11-01.md): an authored combo deck that closes a table, or sits as a wildcard.</summary>
    public sealed class Headliner
    {
        public string Id = "", Handle = "", Deck = "", Table = "", List = "";
        /// <summary>Its show-off cards, and the cards its binder leads with, in order (base codes).</summary>
        public List<uint> Pieces = new List<uint>(), Stakes = new List<uint>();
        /// <summary>Owner-approved copies over its era's list (counted at the deck's count when it's checked).</summary>
        public HashSet<uint> Over = new HashSet<uint>();
        /// <summary>Passed the Headliners go/no-go; a headliner that isn't ready never sits.</summary>
        public bool Ready;
        /// <summary>
        /// A No Limits peak build (a `build:` block, docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 10.1): its id is its pilot's
        /// (the character), its deck lives in decks/no-limits, its binder is "nl:&lt;pilot&gt;".
        /// </summary>
        public bool IsBuild;
        /// <summary>A build's decks/history base (`base:`; "" for none), the deck its Elo gain is measured from.</summary>
        public string Base = "";
        /// <summary>A build's family tags (`family:`): no two builds sharing one sit at seats 1-3 of the same night.</summary>
        public List<string> Family = new List<string>();
        /// <summary>The wave a build ships in (`wave:`).</summary>
        public int Wave;
        /// <summary>A build's own banned cards back at 3 (`restored:`, base codes): the UNBANNED row, and the "restored" story event.</summary>
        public List<uint> Restored = new List<uint>();
        /// <summary>A build's contraband picks: its `over:` cards that aren't restored (section 5.1, rules 3 and 5).</summary>
        public IEnumerable<uint> Picks => Over.Where(c => !Restored.Contains(c));
    }

    /// <summary>An alley bracket's data (Phase C plays it): kind, size, entry, pay and packs by placement, its field.</summary>
    public sealed class AlleyBracketSpec
    {
        public string Name = "", Kind = "", Opens = "";
        public int Size, Entry, FinalBestOf = 1, Tokens;
        public int[] Pay = new int[0], Packs = new int[0];
        /// <summary>
        /// The field lines as written ("you 1", "splash 7 cleared", "mitsurugi mitsurugi", "junia nl-snake-eye-brawl 8 | after: The
        /// Black Market": that entrant sits once the table is cleared; before, it's one more regular from the "splash ... cleared" source).
        /// </summary>
        public List<string> Field = new List<string>();

        /// <summary>A field line's words (before any "|") and its `after:` table ("" for none).</summary>
        public static (string[] words, string after) ParseField(string line)
        {
            var bar = line.IndexOf('|');
            var head = bar >= 0 ? line.Substring(0, bar) : line;
            var after = "";
            if (bar >= 0)
            {
                var tail = line.Substring(bar + 1).Trim();
                if (tail.StartsWith("after:", StringComparison.OrdinalIgnoreCase)) after = tail.Substring(6).Trim();
            }
            return (head.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), after);
        }
    }

    /// <summary>
    /// The Back Alley's structure (campaign/alley-events.txt; the contraband stays in alley.txt): its tables, headliners and
    /// brackets, and the chalk handles regulars are named by.
    /// </summary>
    public sealed class AlleyData
    {
        public const string FileName = "alley-events.txt";

        public IReadOnlyList<AlleyTable> Tables { get; }
        public IReadOnlyList<Headliner> Headliners { get; }
        public IReadOnlyList<AlleyBracketSpec> Brackets { get; }
        /// <summary>A regular's handle prefix when that card (base code) is its most valuable splash.</summary>
        public IReadOnlyList<(uint card, string prefix)> Handles { get; }
        /// <summary>First names for handles.</summary>
        public IReadOnlyList<string> Names { get; }

        private readonly List<string> _parseProblems;

        private AlleyData(List<AlleyTable> tables, List<Headliner> headliners, List<AlleyBracketSpec> brackets, List<(uint, string)> handles,
            List<string> names, List<string> problems)
        {
            Tables = tables;
            Headliners = headliners;
            Brackets = brackets;
            Handles = handles;
            Names = names;
            _parseProblems = problems;
        }

        public static AlleyData Empty => new AlleyData(new List<AlleyTable>(), new List<Headliner>(), new List<AlleyBracketSpec>(),
            new List<(uint, string)>(), new List<string>(), new List<string>());

        /// <summary>Reads alley-events.txt; a missing file: no tables.</summary>
        public static AlleyData Load(string path, ICardDataProvider cards) => File.Exists(path) ? Parse(File.ReadAllText(path), cards) : Empty;

        /// <summary>
        /// "table:", "headliner:", "build:" and "bracket:" start a block whose "key: value" lines follow (a headliner's or a
        /// build's own "table:" line names its table); "handle: card | prefix" and "names: a, b, ..." lines are the file's
        /// (names: lines add up). "#" starts a comment. A `build:` block is a <see cref="Headliner"/> with <see cref="Headliner.IsBuild"/>.
        /// </summary>
        public static AlleyData Parse(string text, ICardDataProvider cards)
        {
            var tables = new List<AlleyTable>();
            var headliners = new List<Headliner>();
            var brackets = new List<AlleyBracketSpec>();
            var handles = new List<(uint, string)>();
            var names = new List<string>();
            var problems = new List<string>();
            var byName = new Dictionary<string, uint>(StringComparer.Ordinal);
            foreach (var c in cards.All) if (c.Alias == 0 && !byName.ContainsKey(c.Name)) byName[c.Name] = c.Code;
            uint Card(string name, string where)
            {
                if (byName.TryGetValue(name.Trim(), out var code)) return code;
                problems.Add($"{where}: no card named \"{name.Trim()}\"");
                return 0;
            }
            // A comma-separated list of names; a name that holds a comma ("Blaster, Dragon Ruler of Infernos") is read back
            // together when its parts don't name cards on their own and the join does.
            List<uint> Cards(string value, string where)
            {
                var parts = value.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
                var list = new List<uint>();
                for (int i = 0; i < parts.Count; i++)
                {
                    var name = parts[i];
                    if (!byName.ContainsKey(name))
                        for (int k = 1; k <= 2 && i + k < parts.Count; k++)
                            if (byName.ContainsKey(string.Join(", ", parts.Skip(i).Take(k + 1)))) { name = string.Join(", ", parts.Skip(i).Take(k + 1)); i += k; break; }
                    var code = Card(name, where);
                    if (code != 0) list.Add(code);
                }
                return list;
            }
            static int[] Ints(string value) => value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(v => int.TryParse(v, out var n) ? n : 0).ToArray();
            static double Num(string value) =>
                double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;

            AlleyTable? table = null;
            Headliner? headliner = null;
            AlleyBracketSpec? bracket = null;
            int lineNo = 0;
            foreach (var raw in text.Split('\n'))
            {
                lineNo++;
                var hash = raw.IndexOf('#');
                var line = (hash >= 0 ? raw.Substring(0, hash) : raw).Trim();
                if (line.Length == 0) continue;
                var colon = line.IndexOf(':');
                if (colon <= 0) { problems.Add($"line {lineNo}: no ':'"); continue; }
                var key = line.Substring(0, colon).Trim().ToLowerInvariant();
                var value = line.Substring(colon + 1).Trim();
                switch (key)
                {
                    // In a headliner's block, its first "table:" is the headliner's table; after that one starts a table.
                    case "table" when headliner == null || headliner.Table.Length > 0:
                        tables.Add(table = new AlleyTable { Name = value });
                        headliner = null; bracket = null;
                        continue;
                    case "headliner":
                        headliners.Add(headliner = new Headliner { Id = value });
                        table = null; bracket = null;
                        continue;
                    case "build":
                        headliners.Add(headliner = new Headliner { Id = value, IsBuild = true });
                        table = null; bracket = null;
                        continue;
                    case "bracket":
                        brackets.Add(bracket = new AlleyBracketSpec { Name = value });
                        table = null; headliner = null;
                        continue;
                    case "handle" when headliner == null:
                    {
                        var parts = value.Split('|');
                        if (parts.Length != 2 || parts[1].Trim().Length == 0) { problems.Add($"line {lineNo}: handle needs \"card | prefix\""); continue; }
                        var code = Card(parts[0], $"line {lineNo}");
                        if (code != 0) handles.Add((code, parts[1].Trim()));
                        continue;
                    }
                    case "names":
                        names.AddRange(value.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0));
                        continue;
                }
                if (table != null)
                {
                    switch (key)
                    {
                        case "id": table.Id = value; break;
                        case "tier":
                            if (Enum.TryParse<AlleyTier>(value, true, out var tier)) table.Tier = tier;
                            else problems.Add($"{table.Name}: unknown tier {value}");
                            break;
                        case "event":
                            table.Event = Alley.Events.FirstOrDefault(e => e.Name == value);
                            if (table.Event == null) problems.Add($"{table.Name}: no event named {value}");
                            break;
                        case "opens": table.Opens = value; break;
                        case "bases": table.Bases = value.Split(',').Select(b => b.Trim()).Where(b => b.Length > 0).ToList(); break;
                        case "splash-elo": table.SplashElo = Num(value); break;
                        case "headliners": case "champions": table.Headliners = value.Split(',').Select(b => b.Trim()).Where(b => b.Length > 0).ToList(); break;
                        case "pool": table.Pool = value.Split(',').Select(b => b.Trim()).Where(b => b.Length > 0).ToList(); break;
                        case "gate": table.Gate = value; break;
                        case "elo":
                        {
                            var range = value.Split('-').Select(v => v.Trim()).ToArray();
                            if (range.Length == 2) { table.MinElo = Num(range[0]); table.MaxElo = Num(range[1]); }
                            if (range.Length != 2 || table.MinElo <= 0 || table.MaxElo <= table.MinElo) problems.Add($"{table.Name}: elo needs \"min-max\"");
                            break;
                        }
                        case "closer": table.Closer = value; break;
                        case "mat": table.Mat = value; break;
                        case "music": table.Music = value; break;
                        case "ready": table.Ready = value != "no"; break;
                        case "seat":
                        {
                            var parts = value.Split('|').Select(p => p.Trim()).ToList();
                            var head = parts[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (head.Length < 2 || !int.TryParse(head[0], out var n)) { problems.Add($"{table.Name}: bad seat line \"{value}\""); break; }
                            var seat = new AlleySeatSpec { Seat = n, Kind = head[1], Character = head.Length > 2 ? head[2] : "", Deck = head.Length > 3 ? head[3] : "" };
                            foreach (var p in parts.Skip(1))
                            {
                                if (p.StartsWith("skill")) seat.Skill = Num(p.Substring(5));
                                else if (p.StartsWith("event:")) seat.Event = p.Substring(6).Trim();
                                else if (p.StartsWith("else")) seat.Else = p.Substring(4).Trim();
                                else if (p.StartsWith("allow:")) foreach (var c in Cards(p.Substring(6), $"{table.Name} seat {n}")) seat.Allow.Add(c);
                                else if (p == "as-built") seat.AsBuilt = true;
                                else problems.Add($"{table.Name} seat {n}: unknown part \"{p}\"");
                            }
                            table.Seats.Add(seat);
                            break;
                        }
                        default: problems.Add($"{table.Name}: unknown key {key}"); break;
                    }
                }
                else if (headliner != null)
                {
                    switch (key)
                    {
                        case "handle": headliner.Handle = value; break;
                        case "deck": headliner.Deck = value; break;
                        case "table": headliner.Table = value; break;
                        case "list": headliner.List = value; break;
                        case "over": foreach (var c in Cards(value, headliner.Id)) headliner.Over.Add(c); break;
                        case "pieces": headliner.Pieces = Cards(value, headliner.Id); break;
                        case "stakes": headliner.Stakes = Cards(value, headliner.Id); break;
                        case "ready": headliner.Ready = value == "yes"; break;
                        case "base": headliner.Base = value; break;
                        case "family": headliner.Family = value.Split(',').Select(f => f.Trim()).Where(f => f.Length > 0).ToList(); break;
                        case "wave": int.TryParse(value, out headliner.Wave); break;
                        case "restored": headliner.Restored = Cards(value, headliner.Id); break;
                        default: problems.Add($"{headliner.Id}: unknown key {key}"); break;
                    }
                }
                else if (bracket != null)
                {
                    switch (key)
                    {
                        case "kind": bracket.Kind = value; break;
                        case "size": int.TryParse(value, out bracket.Size); break;
                        case "opens": bracket.Opens = value; break;
                        case "entry": int.TryParse(value, out bracket.Entry); break;
                        case "pay": bracket.Pay = Ints(value); break;
                        case "packs": bracket.Packs = Ints(value); break;
                        case "tokens": bracket.Tokens = Ints(value).FirstOrDefault(); break;
                        case "final": bracket.FinalBestOf = value == "bo3" ? 3 : 1; break;
                        case "field": bracket.Field.Add(value); break;
                        default: problems.Add($"{bracket.Name}: unknown key {key}"); break;
                    }
                }
                else problems.Add($"line {lineNo}: {key}: outside a table, headliner, build or bracket");
            }
            return new AlleyData(tables, headliners, brackets, handles, names, problems);
        }

        /// <summary>A table's index by name (or id); -1 if there's none.</summary>
        public int IndexOf(string tableName)
        {
            for (int i = 0; i < Tables.Count; i++)
                if (Tables[i].Name.Equals(tableName, StringComparison.OrdinalIgnoreCase) || Tables[i].Id == tableName) return i;
            return -1;
        }

        public Headliner? FindHeadliner(string id) => Headliners.FirstOrDefault(h => h.Id == id);

        /// <summary>The peak builds (the `build:` blocks among <see cref="Headliners"/>).</summary>
        public IEnumerable<Headliner> Builds => Headliners.Where(h => h.IsBuild);

        /// <summary>A build by its pilot's id; null for none (a headliner of that id isn't one).</summary>
        public Headliner? FindBuild(string id) => Headliners.FirstOrDefault(h => h.IsBuild && h.Id == id);

        /// <summary>
        /// What's wrong with the data (empty: nothing): names that don't resolve; tables that don't match an event, open from
        /// nowhere, or have base decks missing, out of their event's Elo range, not from its era or `skip:`ped; headliners
        /// missing or on another table; ready headliners whose deck (Main and Extra: a CPU never sides) isn't legal under its
        /// `list:` with its `over:` cards counted, or whose pieces and stakes aren't in it; `allow:` naming a card outside <see cref="Alley.BossOnly"/>; an
        /// authored deck on a ready table (one built for the alley: a seat marked `as-built`, a rated roster deck, is exempt) running an excluded card
        /// its seat doesn't allow; an `as-built` seat whose deck isn't a rated roster deck; the handle names; the brackets (a known kind,
        /// each once; numbers as <see cref="AlleyEconomy"/>'s for that kind, a title at most <see cref="BracketDuelCap"/> a duel; the
        /// field, CPUs counted the same before and after every `after:` table, every entrant's deck there and rated except a field
        /// build that isn't ready, which <see cref="Campaign.Brackets.Opening"/> keeps "Coming soon"; no peak build beside its as-built
        /// base; each Brawl deck legal under the Alley list with its added cards in The Basement Game's pool and no excluded card);
        /// the No Limits table (its `elo:` window, `pool:` and `champions:` naming builds, a ready table with a ready champion and
        /// <see cref="BackAlley.NoLimitsMinPool"/> ready pool builds) and its builds (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 5.1,
        /// rules 3-8: at most one <see cref="Alley.PowerCards"/> pick, no two builds sharing more than <see cref="BackAlley.NoLimitsPickShare"/>
        /// picks, at most <see cref="BackAlley.NoLimitsFiendsmith"/> builds tagged fiendsmith, no excluded card, the deck legal under its
        /// `list:` with its `over:` counted, its restored and pieces in the deck, pieces led by a restored card, stakes led by a card
        /// <see cref="Ante.CanStake"/> at No Limits; and, ready, rated inside the table's window and at most <see cref="BackAlley.NoLimitsMaxGain"/>
        /// over its base). A build that isn't ready skips its deck checks, as a headliner does; <paramref name="notReadyBuilds"/> runs them
        /// anyway on every build whose deck file exists (for the deck author), the Elo checks still only on the ready ones.
        /// </summary>
        public List<string> Problems(AlleyContext ctx, bool notReadyBuilds = false)
        {
            var problems = new List<string>(_parseProblems);
            var cards = ctx.Cards;
            var bossOnly = new HashSet<uint>(Alley.BossOnly.Select(n => Alley.CodeOf(n, cards)));
            var excluded = new HashSet<uint>(Alley.Excluded.Select(n => Alley.CodeOf(n, cards)));
            string Name(uint c) => cards.Get(c)?.Name ?? c.ToString();
            if (Tables.Count == 0) problems.Add("no tables");
            foreach (var t in Tables)
            {
                if (t.Id.Length == 0) problems.Add($"{t.Name}: no id");
                if (Tables.Count(x => x.Id == t.Id) > 1) problems.Add($"{t.Name}: id {t.Id} used twice");
                if (t.Opens != "alley" && IndexOf(t.Opens) < 0) problems.Add($"{t.Name}: opens with an unknown table {t.Opens}");
                foreach (var seat in t.Seats)
                    foreach (var c in seat.Allow.Where(c => !bossOnly.Contains(c)))
                        problems.Add($"{t.Name} seat {seat.Seat}: allow: names {Name(c)}, which isn't a boss-only card");
                if (t.Tier == AlleyTier.NoLimits)
                {
                    if (t.MinElo <= 0 || t.MaxElo <= t.MinElo) problems.Add($"{t.Name}: no elo: window");
                    if (t.Pool.Count == 0) problems.Add($"{t.Name}: no pool");
                    foreach (var id in t.Pool.Where(id => FindBuild(id) == null)) problems.Add($"{t.Name}: pool names {id}, which isn't a build");
                    foreach (var id in t.Headliners.Where(id => FindBuild(id) == null)) problems.Add($"{t.Name}: champion {id} isn't a build");
                    foreach (var seat in t.Seats.Where(s => s.Kind is not ("pool" or "champion"))) problems.Add($"{t.Name} seat {seat.Seat}: kind {seat.Kind} (pool or champion)");
                    if (t.Ready)
                    {
                        int readyPool = t.Pool.Where(id => !t.Headliners.Contains(id)).Select(FindBuild).Count(b => b != null && b.Ready);
                        if (readyPool < BackAlley.NoLimitsMinPool) problems.Add($"{t.Name}: {readyPool} ready pool builds (a ready table needs {BackAlley.NoLimitsMinPool})");
                        if (!t.Headliners.Select(FindBuild).Any(b => b != null && b.Ready)) problems.Add($"{t.Name}: no ready champion");
                    }
                }
                else
                {
                    if (t.Pool.Count > 0 || t.MinElo > 0) problems.Add($"{t.Name}: pool: and elo: are No Limits keys");
                    foreach (var id in t.Headliners.Where(id => FindBuild(id) != null)) problems.Add($"{t.Name}: headliner {id} is a build (No Limits only)");
                }
                if (t.Tier is AlleyTier.Street or AlleyTier.Den)
                {
                    var e = t.Event;
                    if (e == null) { problems.Add($"{t.Name}: no event"); continue; }
                    var curation = ctx.Curated.TryGetValue(e.Name, out var cur) ? cur : null;
                    if (t.Bases.Count == 0) problems.Add($"{t.Name}: no bases");
                    foreach (var b in t.Bases.Append(t.Closer).Where(b => b.Length > 0).Distinct())
                    {
                        var deck = ctx.LoadDeck(b);
                        if (deck == null) { problems.Add($"{t.Name}: base {b} has no deck"); continue; }
                        if (!ctx.Ratings.TryGetValue(b, out var elo)) problems.Add($"{t.Name}: base {b} has no rating");
                        else if (elo < e.MinElo || elo > e.MaxElo) problems.Add($"{t.Name}: base {b} is rated {elo:0}, outside {e.MinElo:0}-{e.MaxElo:0}");
                        if (!Alley.FromEra(deck, e, ctx.Printings, cards)) problems.Add($"{t.Name}: base {b} isn't from {e.EraCap} or before");
                        if (curation != null && curation.Skip.Contains(b)) problems.Add($"{t.Name}: base {b} sits {e.Name} out (skip:)");
                    }
                    if (t.Closer.Length == 0) problems.Add($"{t.Name}: no closer");
                }
                foreach (var id in t.Headliners)
                {
                    var h = FindHeadliner(id);
                    if (h == null) problems.Add($"{t.Name}: no headliner {id}");
                    else if (h.Table != t.Name) problems.Add($"{t.Name}: headliner {id} is for {h.Table}");
                }
                if (!t.Ready) continue;
                foreach (var seat in t.Seats.Where(s => s.Kind == "authored"))
                {
                    var deck = ctx.LoadDeck(seat.Deck);
                    if (deck == null) { problems.Add($"{t.Name} seat {seat.Seat}: no deck {seat.Deck}"); continue; }
                    // A rated roster deck played as built (Nyx's seat 2, chaos-yata: `as-built`) is checked like a headliner's: by its
                    // own history, not the alley's exclusions (CPUs play at full strength). Decks authored for the alley are checked,
                    // rated or not (--rate-decks leaves decks/alley out, but a rating must never turn the check off).
                    if (seat.AsBuilt)
                    {
                        if (!ctx.Ratings.ContainsKey(seat.Deck) || !ctx.Roster.Any(r => r.Id == seat.Character && r.Decks.Contains(seat.Deck)))
                            problems.Add($"{t.Name} seat {seat.Seat}: as-built {seat.Deck} isn't a rated deck of {seat.Character}'s on the roster");
                        continue;
                    }
                    foreach (var c in deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, cards)).Distinct().Where(excluded.Contains))
                        if (!seat.Allow.Contains(c)) problems.Add($"{t.Name} seat {seat.Seat}: {seat.Deck} runs {Name(c)}, which its allow: doesn't name");
                }
            }
            foreach (var h in Headliners)
            {
                var what = h.IsBuild ? "build" : "headliner";
                int ti = IndexOf(h.Table);
                if (ti < 0) problems.Add($"{what} {h.Id}: unknown table {h.Table}");
                if (h.Handle.Length == 0) problems.Add($"{what} {h.Id}: no handle");
                if (Headliners.Count(x => x.Id == h.Id) > 1) problems.Add($"{what} {h.Id}: id used twice");
                if (h.IsBuild) { BuildProblems(h, ti >= 0 ? Tables[ti] : null, ctx, excluded, problems, notReadyBuilds); continue; }
                if (!h.Ready) continue;
                var deck = ctx.LoadDeck(h.Deck);
                if (deck == null) { problems.Add($"headliner {h.Id}: no deck {h.Deck}"); continue; }
                var list = ctx.Lists.FirstOrDefault(l => l.Name == h.List);
                if (list == null) { problems.Add($"headliner {h.Id}: no list {h.List}"); continue; }
                var codes = ValidateUnder(h, deck, list, ctx, problems);
                foreach (var c in h.Pieces.Concat(h.Stakes).Distinct().Where(c => !codes.Contains(c)))
                    problems.Add($"headliner {h.Id}: {Name(c)} isn't in {h.Deck}");
            }
            // Across the builds: no two share more than NoLimitsPickShare picks; at most NoLimitsFiendsmith carry the fiendsmith tag.
            var builds = Builds.ToList();
            for (int a = 0; a < builds.Count; a++)
                for (int b = a + 1; b < builds.Count; b++)
                {
                    var shared = builds[a].Picks.Intersect(builds[b].Picks).ToList();
                    if (shared.Count > BackAlley.NoLimitsPickShare)
                        problems.Add($"builds {builds[a].Id} and {builds[b].Id} share {shared.Count} picks ({string.Join(", ", shared.Select(Name))}); at most {BackAlley.NoLimitsPickShare}");
                }
            var fiendsmiths = builds.Where(b => b.Family.Contains(BackAlley.FiendsmithFamily)).Select(b => b.Id).ToList();
            if (fiendsmiths.Count > BackAlley.NoLimitsFiendsmith)
                problems.Add($"{fiendsmiths.Count} builds are tagged {BackAlley.FiendsmithFamily} ({string.Join(", ", fiendsmiths)}); at most {BackAlley.NoLimitsFiendsmith}");
            BracketProblems(ctx, excluded, problems);
            return problems;
        }

        /// <summary>
        /// The deck as it plays (a CPU never sides) validated under its list with its over: cards allowed at the deck's own
        /// count; the deck's base codes.
        /// </summary>
        private static List<uint> ValidateUnder(Headliner h, Deck deck, Banlist list, AlleyContext ctx, List<string> problems)
        {
            var cards = ctx.Cards;
            var played = deck.Clone();
            played.Side.Clear();
            var codes = played.Main.Concat(played.Extra).Select(c => Banlist.BaseCode(c, cards)).ToList();
            var withOver = new Banlist { Name = list.Name, Whitelist = list.Whitelist, Limits = new Dictionary<uint, int>(list.Limits) };
            foreach (var c in h.Over) withOver.Limits[c] = Math.Max(withOver.Allowed(c, cards), codes.Count(x => x == c));
            foreach (var p in DeckRules.Validate(played, withOver, cards)) problems.Add($"{(h.IsBuild ? "build" : "headliner")} {h.Id}: {p.Message}");
            return codes;
        }

        /// <summary>One build's checks (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 5.1): see <see cref="Problems"/>.</summary>
        private void BuildProblems(Headliner h, AlleyTable? t, AlleyContext ctx, HashSet<uint> excluded, List<string> problems, bool notReady)
        {
            var cards = ctx.Cards;
            string Name(uint c) => cards.Get(c)?.Name ?? c.ToString();
            string who = $"build {h.Id}";
            if (t != null && t.Tier != AlleyTier.NoLimits) problems.Add($"{who}: {h.Table} isn't a No Limits table");
            if (t != null && !t.Pool.Contains(h.Id) && !t.Headliners.Contains(h.Id)) problems.Add($"{who}: neither in {t.Name}'s pool nor its champions");
            if (h.Deck.Length == 0) problems.Add($"{who}: no deck");
            if (h.Wave <= 0) problems.Add($"{who}: no wave");
            // The rest is checked once the build is ready (or, for the deck author, whenever its deck file exists).
            var deck = h.Deck.Length > 0 ? ctx.LoadDeck(h.Deck) : null;
            if (!h.Ready && !(notReady && deck != null)) return;
            if (h.Base.Length > 0 && ctx.LoadDeck(h.Base) == null) problems.Add($"{who}: base {h.Base} has no deck");
            // Rule 3: at most one generic power card among the picks.
            var power = new HashSet<uint>(Alley.PowerCards.Select(n => Alley.CodeOf(n, cards)));
            var powerPicks = h.Picks.Where(power.Contains).ToList();
            if (powerPicks.Count > 1) problems.Add($"{who}: {powerPicks.Count} power cards among its picks ({string.Join(", ", powerPicks.Select(Name))}); at most one");
            // Rule 4: builds have no allow:, so an excluded card in its over:, restored:, pieces: or stakes: is wrong whatever the deck says.
            foreach (var c in h.Over.Concat(h.Restored).Concat(h.Pieces).Concat(h.Stakes).Distinct().Where(excluded.Contains))
                problems.Add($"{who}: names {Name(c)}, an excluded card");
            // Rule 7's leads: pieces: leads with a restored card; stakes: leads with a card that can be staked at No Limits.
            if (h.Pieces.Count == 0 || !h.Restored.Contains(h.Pieces[0])) problems.Add($"{who}: pieces: must lead with a restored card");
            if (h.Stakes.Count == 0) problems.Add($"{who}: no stakes:");
            else if (!Ante.CanStake(h.Stakes[0], AlleyTier.NoLimits, ctx)) problems.Add($"{who}: {Name(h.Stakes[0])} fits no printing in the No Limits range, so it can't lead its stakes");
            if (deck == null) { problems.Add($"{who}: no deck {h.Deck}"); return; }
            var list = ctx.Lists.FirstOrDefault(l => l.Name == h.List);
            if (list == null) { problems.Add($"{who}: no list {h.List}"); return; }
            var codes = ValidateUnder(h, deck, list, ctx, problems);
            foreach (var c in codes.Distinct().Where(excluded.Contains)) problems.Add($"{who}: {h.Deck} runs {Name(c)}, an excluded card");
            foreach (var c in h.Restored.Concat(h.Pieces).Concat(h.Stakes).Distinct().Where(c => !codes.Contains(c)))
                problems.Add($"{who}: {Name(c)} isn't in {h.Deck}");
            if (!h.Ready) return;
            // Rule 8: rated, inside the table's window, at most NoLimitsMaxGain over its base.
            if (!ctx.Ratings.TryGetValue(h.Deck, out var elo)) { problems.Add($"{who}: {h.Deck} has no rating"); return; }
            if (t != null && t.MaxElo > t.MinElo && (elo < t.MinElo || elo > t.MaxElo))
                problems.Add($"{who}: {h.Deck} is rated {elo:0}, outside {t.Name}'s {t.MinElo:0}-{t.MaxElo:0}");
            if (h.Base.Length > 0 && ctx.Ratings.TryGetValue(h.Base, out var baseElo) && elo - baseElo > BackAlley.NoLimitsMaxGain)
                problems.Add($"{who}: {h.Deck} is rated {elo:0}, {elo - baseElo:0} over {h.Base} ({baseElo:0}); at most {BackAlley.NoLimitsMaxGain}");
        }

        /// <summary>The most DP a bracket's title may pay a duel (ALLEY-ECONOMY E7; NO-LIMITS-ECONOMY 6.2: "at most 300 a duel").</summary>
        public const int BracketDuelCap = 300;

        /// <summary>The names, then the brackets (see <see cref="Problems"/>).</summary>
        private void BracketProblems(AlleyContext ctx, HashSet<uint> excluded, List<string> problems)
        {
            var cards = ctx.Cards;
            string Name(uint c) => cards.Get(c)?.Name ?? c.ToString();
            if (Names.Count < BackAlley.Matches) problems.Add($"only {Names.Count} names (a night needs {BackAlley.Matches})");
            foreach (var n in Names.GroupBy(n => n).Where(g => g.Count() > 1)) problems.Add($"names: {n.Key} twice");
            if (Names.Contains("Fen")) problems.Add("names: Fen is retired (Firewall Fen)");
            foreach (var b in Brackets)
            {
                bool known = Campaign.Brackets.IsAlleyKind(b.Kind);
                if (!known) problems.Add($"{b.Name}: unknown kind {b.Kind}");
                if (Brackets.Count(x => x.Kind == b.Kind) > 1) problems.Add($"{b.Name}: kind {b.Kind} used twice");
                if (b.Size is not (8 or 16)) problems.Add($"{b.Name}: size {b.Size} (8 or 16)");
                int places = b.Size == 16 ? 5 : 4;
                if (b.Pay.Length != places || b.Packs.Length != places) problems.Add($"{b.Name}: pay and packs need {places} placements");
                if (IndexOf(b.Opens) < 0) problems.Add($"{b.Name}: opens with an unknown table {b.Opens}");
                // The numbers are AlleyEconomy's knobs, per kind (the code pays from them); the data says the same, so they can't drift.
                if (known && (b.Entry != Campaign.Brackets.EntryFor(b.Kind) || !b.Pay.SequenceEqual(Campaign.Brackets.PayFor(b.Kind))
                              || !b.Packs.SequenceEqual(Campaign.Brackets.PacksFor(b.Kind)) || b.Tokens != Campaign.Brackets.TokensFor(b.Kind)))
                    problems.Add($"{b.Name}: entry, pay, packs or tokens differ from AlleyEconomy's");
                // A duel pays at most 300 DP (E7): a title is at least one duel a round, and a best-of-three final one more.
                int titleDuels = (b.Size == 16 ? 4 : 3) + (b.FinalBestOf > 1 ? 1 : 0);
                if (known && b.Pay.Length > 0 && b.Pay[0] > BracketDuelCap * titleDuels)
                    problems.Add($"{b.Name}: a title pays {b.Pay[0]:N0} DP in {titleDuels} duels, over {BracketDuelCap} a duel");
                // A field build (a pilot on its own No Limits deck) that isn't ready is tolerated: Brackets.Opening keeps the
                // bracket "Coming soon" (it can't be entered) until every one is, so its deck may not exist yet. A ready one is
                // checked in full below (deck, rating); a not-ready one's deck is its build's own checks' (notReadyBuilds).
                var waiting = new HashSet<string>(Campaign.Brackets.FieldBuilds(this, b).Where(h => !h.Ready).Select(h => h.Id));
                // The field: one "you" line, seeds in range and used once, characters on the roster (or build pilots) with rated
                // decks, splash tables that have regulars, size - 1 CPUs in all, before and after every `after:` table's clear
                // (an `after:` entrant counts one CPU either way: before, it's one more regular from the "splash ... cleared" source).
                int cpus = 0, yous = 0, afterLines = 0;
                bool clearedSplash = false;
                var seeds = new List<int>();
                var named = new List<(string who, string deck, string after)>();
                foreach (var line in b.Field)
                {
                    var (w, after) = AlleyBracketSpec.ParseField(line);
                    if (w.Length == 0) continue;
                    if (after.Length > 0 && IndexOf(after) < 0) problems.Add($"{b.Name}: field: after: an unknown table {after}");
                    if (w[0] == "you") { yous++; if (w.Length > 1 && int.TryParse(w[1], out var ys)) seeds.Add(ys); continue; }
                    if (w[0] == "splash")
                    {
                        cpus += w.Length > 1 && int.TryParse(w[1], out var n) ? n : 0;
                        var which = string.Join(" ", w.Skip(2));
                        int ti = which == "cleared" ? 0 : IndexOf(which);
                        if (which == "cleared") clearedSplash = true;
                        if (ti < 0 || (which != "cleared" && Tables[ti].Tier is not (AlleyTier.Street or AlleyTier.Den))) problems.Add($"{b.Name}: field splash from {which}, not a regular table");
                        continue;
                    }
                    cpus++;
                    if (after.Length > 0) afterLines++;
                    var who = ctx.Roster.FirstOrDefault(p => p.Id == w[0]);
                    var build = FindBuild(w[0]);
                    var deck = w.Length > 1 ? w[1] : who?.Decks.FirstOrDefault() ?? "";
                    if (who == null && build == null) problems.Add($"{b.Name}: field: no character {w[0]}");
                    // A build's pilot names its deck (its build's, or a Brawl deck): without one the field would seat the profile's
                    // first deck while Brackets.FieldBuilds (the opens gate) wouldn't count the line.
                    if (build != null && w.Length < 2) problems.Add($"{b.Name}: field: {w[0]} is a build's pilot; name the deck ({build.Deck} or a Brawl deck)");
                    // Not ready: coming soon (see above); its deck is the build's own checks' (BuildProblems), not the field's.
                    if (waiting.Contains(w[0]) && w.Length > 1 && w[1] == build?.Deck) { }
                    else if (ctx.LoadDeck(deck) == null) problems.Add($"{b.Name}: field: {w[0]} has no deck {deck}");
                    else if (!ctx.Ratings.ContainsKey(deck)) problems.Add($"{b.Name}: field: {deck} has no rating");
                    if (w.Length > 2 && int.TryParse(w[2], out var fs)) seeds.Add(fs);
                    named.Add((w[0], deck, after));
                }
                if (yous != 1) problems.Add($"{b.Name}: field needs one \"you\" line");
                if (cpus != b.Size - 1) problems.Add($"{b.Name}: field has {cpus} CPUs, not {b.Size - 1}");
                if (afterLines > 0 && !clearedSplash) problems.Add($"{b.Name}: after: lines need a \"splash ... cleared\" line to stand in for them");
                if (seeds.Any(x => x < 1 || x > b.Size) || seeds.Distinct().Count() != seeds.Count) problems.Add($"{b.Name}: field seeds out of range or used twice");
                // The twin rule (section 8): no peak build and its as-built base in one bracket.
                var decks = new HashSet<string>(named.Select(n => n.deck));
                foreach (var build in Builds.Where(x => x.Base.Length > 0 && decks.Contains(x.Deck) && decks.Contains(x.Base)))
                    problems.Add($"{b.Name}: {build.Deck} and its base {build.Base} are both in the field");
                // The out-of-towners' Brawl decks (section 8): legal under the Alley list, their added cards (over the build's base)
                // in The Basement Game's pool, no excluded card.
                var basement = Alley.Events.FirstOrDefault(e => e.Name == "The Basement Game");
                foreach (var (who, deckName, after) in named.Where(n => n.after.Length > 0))
                {
                    var build = FindBuild(who);
                    var deck = ctx.LoadDeck(deckName);
                    if (build == null || deck == null) continue;
                    var played = deck.Clone();
                    played.Side.Clear();
                    foreach (var p in DeckRules.Validate(played, ctx.List, cards)) problems.Add($"{b.Name}: {deckName}: {p.Message}");
                    var codes = played.Main.Concat(played.Extra).Select(c => Banlist.BaseCode(c, cards)).Distinct().ToList();
                    foreach (var c in codes.Where(excluded.Contains)) problems.Add($"{b.Name}: {deckName} runs {Name(c)}, an excluded card");
                    var baseDeck = build.Base.Length > 0 ? ctx.LoadDeck(build.Base) : null;
                    if (baseDeck == null || basement == null) continue;
                    // "Added" is over the base made Alley-legal (Loaners.Adjust, as the deck was built), so Adjust's own swaps aren't counted.
                    var legalBase = Loaners.Adjust(baseDeck, ctx.List, cards).Deck;
                    var baseCodes = new HashSet<uint>(baseDeck.Main.Concat(baseDeck.Extra).Concat(legalBase.Main).Concat(legalBase.Extra).Concat(legalBase.Side).Select(c => Banlist.BaseCode(c, cards)));
                    var pool = new HashSet<uint>(ctx.Pool(basement.EraCap));
                    foreach (var c in codes.Where(c => !baseCodes.Contains(c) && !pool.Contains(c)))
                        problems.Add($"{b.Name}: {deckName} adds {Name(c)}, which isn't in {basement.Name}'s pool");
                }
            }
        }
    }

    /// <summary>
    /// What the alley's code needs from the game or the harness: cards, printings, every ban list and the Alley list, deck
    /// loading, deck ratings, the curated contraband (alley.txt), the roster and the Library (its EXP rate). Set
    /// <see cref="Data"/> too (every <see cref="BackAlley"/> call with the data sets it): headliner binders lead with
    /// their `stakes:` from it.
    /// </summary>
    public sealed class AlleyContext
    {
        public ICardDataProvider Cards = null!;
        public PrintingIndex Printings = null!;
        /// <summary>Every list; the Alley list.</summary>
        public IReadOnlyList<Banlist> Lists = null!;
        public Banlist List = null!;
        /// <summary>The No Limits list (<see cref="Alley.NoLimitsList"/>; built from the cards by <see cref="BackAlley.RulesFor"/> when a caller left it null).</summary>
        public Banlist NoLimits = null!;
        public Func<string, Deck?> LoadDeck = null!;
        public IReadOnlyDictionary<string, double> Ratings = null!;
        public IReadOnlyDictionary<string, Curation> Curated = null!;
        public IReadOnlyList<Personality> Roster = null!;
        public Library Library = null!;
        /// <summary>The alley's tables (for binders: a table's tier, a headliner's stakes); set by every BackAlley call that takes it.</summary>
        public AlleyData? Data;

        private readonly Dictionary<int, List<uint>> _pools = new Dictionary<int, List<uint>>();

        /// <summary>The contraband an event may splash (<see cref="Alley.Contraband"/>), by era cap; 9999 for the ever-Forbidden pool. Cached.</summary>
        public List<uint> Pool(int eraCap)
        {
            if (!_pools.TryGetValue(eraCap, out var pool)) _pools[eraCap] = pool = Alley.Contraband(Lists, List, eraCap, Printings, Cards);
            return pool;
        }

        public Rarity Commonest(uint code) => Printings.Commonest(code, Cards.Get(code)?.Alias ?? 0);
    }

    /// <summary>
    /// One seat's opponent tonight (saved as built, "alleyopp", so an update to alley.txt never changes someone you've
    /// met): a generated regular (its base and what the splash added and cut), a headliner, an authored deck, or an encore.
    /// </summary>
    public sealed class AlleyOpponent
    {
        /// <summary>The table's id. Kind: regular | headliner | wildcard | authored | encore | build (a No Limits pool seat).</summary>
        public string Table = "", Kind = "", Character = "", Handle = "", Base = "";
        /// <summary>1-4 (0: an encore). The night it was drawn for.</summary>
        public int Seat, Night;
        /// <summary>The hooded silhouette's tint, 0xRRGGBB (a hue from the seed, saturation 0.45, value 0.75).</summary>
        public uint Tint;
        public double Skill, Elo;
        public List<uint> Added = new List<uint>(), Cut = new List<uint>();
        public string DeckKey => $"{Alley.DeckPrefix}{Table}.{Seat}";
        /// <summary>The stake binder's owner key: "regular:&lt;table&gt;.&lt;seat&gt;.&lt;night&gt;", "hl:&lt;id&gt;", "nyx:&lt;seat&gt;", "nl:&lt;pilot&gt;" ("" in a bracket: no ante).</summary>
        public string Binder = "";
        /// <summary>An alley bracket's regular (<see cref="Table"/> <see cref="BackAlley.BracketTable"/>, Seat its slot): the table it was drawn from (its splash, skill and Elo).</summary>
        public string From = "";
        /// <summary>Its character is a No Limits peak build's pilot (a pool seat, or a champion as closer, wildcard or encore). Never saved: <see cref="BackAlley"/> fills it with the skill and Elo.</summary>
        public bool IsBuild;

        /// <summary>A headliner, a wildcard, an encore, or a build (the headliner paths: showcase, met and beaten counts, Unlisted).</summary>
        public bool IsHeadliner => Kind is "headliner" or "wildcard" or "encore" or BackAlley.BuildKind;
    }

    /// <summary>What an alley duel's result did (<see cref="BackAlley.Record"/>).</summary>
    public sealed class AlleyResult
    {
        /// <summary>EXP, level-ups (tokens only), the win counted, a first clear's tokens. Alley duels pay no DP.</summary>
        public DuelReward Reward = new DuelReward();
        public AnteSettlement? Ante;
        public bool SeatWon, NightOver, FirstClear, Replay;
        /// <summary>The tables that opened now, every one (The Black Market's clear opens Orvel's and Nyx's). Never saved.</summary>
        public List<string> Opened = new List<string>();
        /// <summary>Story events: alleyclear:, headlinerbeat:, pilotbeat:, restored, combobreaker:, encore, antewin, antelose, rail, railgone, turncap, firstturn, unlisted, pennywin, level:.</summary>
        public List<string> Events = new List<string>();
        /// <summary>The Unlisted binder's new page, when this win added one (<see cref="Unlisted.Add"/>).</summary>
        public UnlistedPage? Page;
        /// <summary>
        /// You won against a headliner whose route you stopped before its payoff (the duel's <see cref="AI.Playbooks.ComboWatch.Broken"/>,
        /// passed to <see cref="BackAlley.Record"/>): its Combo Breaker poster is yours. Only the first time (its event,
        /// "combobreaker:&lt;id&gt;", is raised with it); never for an encore.
        /// </summary>
        public bool ComboBreaker;
        /// <summary>Its scenes' placeholders (<see cref="BackAlley.FillFor"/>): pass them to the story with its events (<see cref="Story.Fill"/>).</summary>
        public Dictionary<string, string> Fill = new Dictionary<string, string>();
        public string Summary = "";
    }

    /// <summary>A table's progress: the night (0 the first), the seats won tonight (bit seat-1), and whether it's been cleared once.</summary>
    public sealed class AlleyTableState
    {
        public int Night, WonMask;
        public bool Cleared;
    }

    /// <summary>Your record against a headliner: duels played, wins, encores, and its Combo Breaker (Headliners phase 3).</summary>
    public sealed class HeadlinerState
    {
        public int Met, Beaten, Encores, Breaker;
    }

    /// <summary>A page of the Unlisted binder (<see cref="Unlisted"/>): a contraband deck you beat, as you met it (Kind: regular | headliner | authored).</summary>
    public sealed class UnlistedPage
    {
        public string Key = "", Table = "", Kind = "", Base = "", Handle = "";
        public List<uint> Added = new List<uint>(), Cut = new List<uint>();
    }

    /// <summary>
    /// The Back Alley (docs/dev/ALLEY-SPEC-2026-11-01.md; post-game, opened by the first Library cup clear): tables of four
    /// seats a night, played in order, against splash regulars (Alley.Splash), headliners and authored decks, each with
    /// a stake (<see cref="Ante"/>). A night's lineup is drawn from the save's salt the first time its table is shown and
    /// saved as built; losing, drawing or quitting changes nothing about a seat; the fourth win ends the night (and the
    /// first time, clears the table: its scene, the next table, <see cref="AlleyEconomy.ClearTokens"/>). Alley duels pay
    /// no DP; EXP is the Library's, and their level-ups pay tokens only.
    /// </summary>
    public static class BackAlley
    {
        public const int Matches = 4;
        public const string OpenEvent = "alley", RegularId = "alley-regular";
        /// <summary>The Back Alley tab's first showing, and the van's first visit.</summary>
        public const string VisitEvent = "alleyvisit", FenceEvent = "fence";
        /// <summary>Baz's chalk name for you, one step per first clear of Dumpster Row, The Basement Game, The Black Market and the Midnight Open title.</summary>
        public static readonly string[] NickLadder = { "Upstairs", "Curbside", "Downstairs", "Boots", "Midnight" };
        /// <summary>
        /// Ines's card (docs/dev/ALLEY-STORY-2026-11-01.md section 3): "heldcard" plays the held-card scenes once Orvel's Table is
        /// cleared and the cup-goat-clear scene is seen, whichever comes second (<see cref="HeldCardDue"/>).
        /// </summary>
        public const string HeldEvent = "heldcard", HeldScene = "held-card", HeldHomeScene = "held-card-home", GoatClearScene = "cup-goat-clear";
        /// <summary>Penny's cameo: her character id (and her binder's key), and the event of your first win against her.</summary>
        public const string PennyId = "penny", PennyEvent = "pennywin";
        /// <summary>A headliner's Combo Breaker (its id follows): its route stopped before the payoff, and you won.</summary>
        public const string ComboBreakerEvent = "combobreaker:";
        /// <summary>An alley bracket's splash regulars' table key: their deck keys are "alley:bracket.&lt;slot&gt;".</summary>
        public const string BracketTable = "bracket";

        // ---- No Limits (docs/dev/NO-LIMITS-SPEC-2026-11-02.md) ----
        /// <summary>A No Limits pool seat's kind; the pilot's build plays as written at skill 1.0.</summary>
        public const string BuildKind = "build";
        /// <summary>
        /// The wall opens (<see cref="WallDue"/>): the story event, and the <see cref="Profile.Seen"/> key a nolimits-tier table
        /// needs (its `gate:`). <see cref="ClosingScene"/> is the scene that must have played first.
        /// </summary>
        public const string WallEvent = "nolimits", ClosingScene = "alley-closing";
        /// <summary>Before your first No Limits duel against a pilot (Unity raises it, the `headliner:` pattern), and a build's first win (<see cref="Record"/>, in place of headlinerbeat:).</summary>
        public const string PilotEvent = "pilot:", PilotBeatEvent = "pilotbeat:";
        /// <summary>The first restored card you win (a `stakes:` card of a build that's also in its `restored:`); its {card}.</summary>
        public const string RestoredEvent = "restored";
        /// <summary>The first Brawl field with an out-of-towner; "outoftowner:&lt;id&gt;" before your first Brawl match against that pilot (Unity raises both).</summary>
        public const string OutOfTownerEvent = "outoftowner";
        /// <summary>The family tag rule 6 counts (at most <see cref="NoLimitsFiendsmith"/> builds carry it).</summary>
        public const string FiendsmithFamily = "fiendsmith";
        /// <summary>
        /// Design knobs (section 13; not economy): the fewest ready non-champion builds a ready table may have (a night
        /// needs 3 and a wildcard can take one), the wave-1 ship gate, the most Elo a build may gain over its base, the
        /// most picks two builds may share, and the most builds tagged fiendsmith.
        /// </summary>
        public const int NoLimitsMinPool = 4, NoLimitsShipPool = 6, NoLimitsMaxGain = 250, NoLimitsPickShare = 2, NoLimitsFiendsmith = 3;

        /// <summary>Opens the alley once a Library cup is cleared (true: it just opened; raise <see cref="OpenEvent"/>). Call after a cup result and at load.</summary>
        public static bool Open(Profile profile, Library library)
        {
            if (profile.AlleyOpen || library.ClearedCount(profile) < 1) return false;
            profile.AlleyOpen = true;
            return true;
        }

        private static AlleyTableState State(Profile profile, AlleyTable t)
        {
            if (!profile.AlleyTables.TryGetValue(t.Id, out var s)) profile.AlleyTables[t.Id] = s = new AlleyTableState();
            return s;
        }

        /// <summary>
        /// The table is open: the alley is, it's ready, the table it opens from is cleared, and (a `gate:`, or any nolimits-tier
        /// table) its gate key is in <see cref="Profile.Seen"/> (No Limits: <see cref="WallEvent"/>, raised by <see cref="WallDue"/>'s caller).
        /// </summary>
        public static bool IsUnlocked(Profile profile, AlleyData data, int table)
        {
            if (!profile.AlleyOpen || table < 0 || table >= data.Tables.Count || !data.Tables[table].Ready) return false;
            var t = data.Tables[table];
            if (t.Gate.Length > 0 && !profile.Seen.Contains(t.Gate)) return false;
            if (t.Tier == AlleyTier.NoLimits && !profile.Seen.Contains(WallEvent)) return false;
            return t.Opens == "alley" || IsCleared(profile, data, data.IndexOf(t.Opens));
        }

        /// <summary>
        /// Whether the wall opens now (section 4.1): Nyx's Table is cleared, the alley-closing scene has been seen, and
        /// <see cref="WallEvent"/> isn't in <see cref="Profile.Seen"/> yet. Check it each time the Back Alley tab is shown and
        /// at load; the caller raises <see cref="WallEvent"/> and adds it to Seen (no save field: that's the unlock).
        /// </summary>
        public static bool WallDue(Profile profile, AlleyData data)
        {
            int nyx = -1;
            for (int i = 0; i < data.Tables.Count && nyx < 0; i++) if (data.Tables[i].Tier == AlleyTier.Nyx) nyx = i;
            return IsCleared(profile, data, nyx) && profile.Seen.Contains(ClosingScene) && !profile.Seen.Contains(WallEvent);
        }

        /// <summary>
        /// Your rules at a table: the No Limits rules (<see cref="Alley.NoLimitsList"/>: no list but the exclusions, three of
        /// anything) at a nolimits-tier table, the Alley's everywhere else. Every caller that used the Alley rules for a table
        /// goes through it (the versus check, the deck editor from the panel, the -alleycheck legal-deck step).
        /// </summary>
        public static FormatRules RulesFor(AlleyData data, int table, AlleyContext ctx)
        {
            if (table < 0 || table >= data.Tables.Count || data.Tables[table].Tier != AlleyTier.NoLimits) return Alley.Rules(ctx.List);
            ctx.NoLimits ??= Alley.NoLimitsList(ctx.Cards);
            return Alley.Rules(ctx.NoLimits);
        }

        /// <summary>A build's deck's base codes (Main and Extra; a CPU never sides); empty for anyone else or a missing deck.</summary>
        private static List<uint> BuildCodes(AlleyOpponent o, AlleyContext ctx)
        {
            var h = ctx.Data?.FindBuild(o.Character);
            var deck = h != null ? ctx.LoadDeck(h.Deck) : null;
            return deck == null ? new List<uint>() : deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, ctx.Cards)).ToList();
        }

        /// <summary>The UNBANNED row (section 11): a build's restored cards that are in its deck, in `restored:` order (base codes); empty for anyone else.</summary>
        public static IReadOnlyList<uint> Unbanned(AlleyOpponent o, AlleyContext ctx)
        {
            var h = ctx.Data?.FindBuild(o.Character);
            if (h == null) return new List<uint>();
            var codes = BuildCodes(o, ctx);
            return h.Restored.Where(codes.Contains).ToList();
        }

        /// <summary>How many copies of a card (base code) a build's deck runs ("×3"); 0 for anyone else.</summary>
        public static int CopiesIn(AlleyOpponent o, uint code, AlleyContext ctx) => BuildCodes(o, ctx).Count(c => c == code);

        /// <summary>
        /// The build pilots seated in a bracket on another deck than their build (section 8's out-of-towners: the Brawl's), in slot
        /// order. The Invitational's pilots play their builds, so they aren't out-of-towners there.
        /// </summary>
        public static List<string> OutOfTowners(Bracket b, AlleyData data) =>
            b.Slots.Where(s => s != null && !s.You && data.FindBuild(s.Character) is Headliner h && s.Deck != h.Deck).Select(s => s.Character).ToList();

        /// <summary>
        /// The No Limits Pack's pool (NL-2, section 9.3): the ready builds' ever-Forbidden cards (their decks ∩ the era-9999
        /// pool) that have a printing, base codes, sorted. Computed from the authored decks, never listed.
        /// </summary>
        public static List<uint> NoLimitsPackPool(AlleyData data, AlleyContext ctx)
        {
            var forbidden = new HashSet<uint>(ctx.Pool(9999));
            var pool = new HashSet<uint>();
            foreach (var h in data.Builds.Where(h => h.Ready))
            {
                var deck = ctx.LoadDeck(h.Deck);
                if (deck == null) continue;
                foreach (var c in deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, ctx.Cards)).Where(forbidden.Contains))
                    if (ctx.Printings.Of(c, ctx.Cards.Get(c)?.Alias ?? 0).Any()) pool.Add(c);
            }
            return pool.OrderBy(c => c).ToList();
        }

        /// <summary>Its first clear is done (night 0's fourth win).</summary>
        public static bool IsCleared(Profile profile, AlleyData data, int table) =>
            table >= 0 && table < data.Tables.Count && profile.AlleyTables.TryGetValue(data.Tables[table].Id, out var s) && s.Cleared;

        public static AlleyTier TierOf(AlleyData data, int table) => data.Tables[table].Tier;

        /// <summary>
        /// Whether "heldcard" is due: Orvel's Table is cleared, the cup-goat-clear scene has been seen, and the held-card scene
        /// hasn't. Check it after an alley result, after a Library result's scenes play, and when the campaign screen opens
        /// (the catch-up), and raise <see cref="HeldEvent"/> (its scenes: held-card, then held-card-home). The card never
        /// enters your collection: it goes to Ines, with no copy, no ledger line and no save field.
        /// </summary>
        public static bool HeldCardDue(Profile profile, AlleyData data)
        {
            int orvel = -1;
            for (int i = 0; i < data.Tables.Count && orvel < 0; i++) if (data.Tables[i].Tier == AlleyTier.Orvel) orvel = i;
            return IsCleared(profile, data, orvel) && profile.Seen.Contains(GoatClearScene) && !profile.Seen.Contains(HeldScene);
        }

        /// <summary>The tables (by id) whose first clears move <see cref="NickLadder"/> on, in order (the Midnight Open's title is the last step).</summary>
        private static readonly string[] NickTables = { "dumpster-row", "basement", "black-market" };

        /// <summary>Baz's chalk name for you ({nick}): computed from your first clears (by table id), never saved.</summary>
        public static string Nick(Profile profile)
        {
            if (profile.AlleyBrackets.TryGetValue("midnight", out var open) && open.Titles > 0) return NickLadder[4];
            for (int step = NickTables.Length; step >= 1; step--)
                if (profile.AlleyTables.TryGetValue(NickTables[step - 1], out var s) && s.Cleared) return NickLadder[step];
            return NickLadder[0];
        }

        public static string Nick(Profile profile, AlleyData data) => Nick(profile);

        /// <summary>
        /// The alley's placeholders for a line or a scene about this opponent and card: {card} (the card's name, "that card"
        /// for none), {table}, {handle}, {nick} and {grade} ("Grade 9" for a slab, "Raw · Near Mint" otherwise; "" for none).
        /// For <see cref="ShopVoice.Pick"/>'s fill, and a result's scenes (<see cref="AlleyResult.Fill"/>, passed to <see cref="Story.Fill"/>).
        /// </summary>
        public static Dictionary<string, string> FillFor(Profile profile, AlleyData data, AlleyOpponent? o, Stake? stake, ICardDataProvider cards)
        {
            int ti = o != null ? data.IndexOf(o.Table) : -1;
            return new Dictionary<string, string>
            {
                ["card"] = stake != null ? cards.Get(stake.Code)?.Name ?? "that card" : "that card",
                ["table"] = ti >= 0 ? data.Tables[ti].Name : o?.Table ?? "",
                ["handle"] = o?.Handle ?? "",
                ["nick"] = Nick(profile),
                ["grade"] = stake == null ? "" : stake.Slab > 0 ? $"Grade {stake.Slab}" : Grades.RawLabel(stake.Grade),
            };
        }

        /// <summary>A copy as a stake, for <see cref="FillFor"/> ({card} and {grade} of a card of yours).</summary>
        private static Stake AsStake(CardCopy c) => new Stake { Code = c.Code, Rarity = c.Rarity, Grade = c.Grade, Slab = c.Slab, Seed = c.Seed };

        /// <summary>The next unbeaten seat tonight (1-4), in order; -1 while the table is locked.</summary>
        public static int NextSeat(Profile profile, AlleyData data, int table)
        {
            if (!IsUnlocked(profile, data, table)) return -1;
            var t = data.Tables[table];
            var s = State(profile, t);
            for (int seat = 1; seat <= t.SeatCount; seat++)
                if ((s.WonMask & (1 << (seat - 1))) == 0) return seat;
            return -1;
        }

        // ---- The night's lineup ---------------------------------------------------------------------

        /// <summary>
        /// Tonight's opponents at a table, seats 1-4 in order: the saved ones, or (none yet) the night drawn now from the
        /// save's salt. Save after. Empty while the table is locked.
        /// </summary>
        public static IReadOnlyList<AlleyOpponent> Lineup(Profile profile, AlleyData data, int table, AlleyContext ctx)
        {
            ctx.Data ??= data;
            if (!IsUnlocked(profile, data, table)) return new List<AlleyOpponent>();
            var t = data.Tables[table];
            var night = State(profile, t).Night;
            var seats = profile.AlleyOpponents.Where(o => o.Table == t.Id && o.Seat >= 1 && o.Night == night).OrderBy(o => o.Seat).ToList();
            if (seats.Count != t.SeatCount)
            {
                // Not drawn yet (or a night left over from before): draw this one.
                profile.AlleyOpponents.RemoveAll(o => o.Table == t.Id && o.Seat >= 1);
                seats = DrawNight(profile, data, table, night, ctx);
                profile.AlleyOpponents.AddRange(seats);
            }
            for (int i = 0; i < seats.Count; i++)
            {
                // A regular whose base deck is gone is drawn again (the next sub-seed); everyone gets their derived fields.
                if (seats[i].Kind == "regular" && ctx.LoadDeck(seats[i].Base) == null)
                {
                    var again = DrawSeat(profile, data, table, night, seats[i].Seat, ctx, seats.Where(o => o != seats[i]).Select(o => o.Handle), retry: 1);
                    profile.AlleyOpponents[profile.AlleyOpponents.IndexOf(seats[i])] = again;
                    seats[i] = again;
                }
                Complete(seats[i], data, ctx);
            }
            return seats;
        }

        /// <summary>A seat's seed: Hash.Mix(salt, 0xA11E ^ table, night*16 + seat); seat 0 is the night's own (bases, closer, wildcard).</summary>
        private static uint SeatSeed(Profile profile, int table, int night, int seat, int retry = 0) =>
            Hash.Mix(profile.Collection.Salt, 0xA11Eu ^ (uint)table, (uint)(night * 16 + seat + retry * 4096));

        /// <summary>The table's ready headliners you haven't beaten, in `headliners:` order.</summary>
        private static List<Headliner> Unbeaten(Profile profile, AlleyData data, AlleyTable t) =>
            t.Headliners.Select(data.FindHeadliner).OfType<Headliner>()
                .Where(h => h.Ready && !(profile.Headliners.TryGetValue(h.Id, out var st) && st.Beaten > 0)).ToList();

        private static List<AlleyOpponent> DrawNight(Profile profile, AlleyData data, int table, int night, AlleyContext ctx)
        {
            var t = data.Tables[table];
            if (t.Tier == AlleyTier.NoLimits) return DrawNoLimitsNight(profile, data, table, night, ctx);
            var rng = new Random(unchecked((int)SeatSeed(profile, table, night, 0)));
            var seats = new List<AlleyOpponent>();
            if (t.Seats.Count > 0)
            {
                // Orvel's and Nyx's tables: authored seats, named as their characters, the same every night; a headliner seat
                // while its headliner is ready (beaten or not), else its stand-in; regular seats drawn anew each night.
                foreach (var spec in t.Seats.OrderBy(s => s.Seat))
                {
                    if (spec.Kind == "authored")
                        seats.Add(new AlleyOpponent
                        {
                            Table = t.Id, Seat = spec.Seat, Night = night, Kind = "authored", Character = spec.Character, Base = spec.Deck,
                            Handle = ctx.Roster.FirstOrDefault(p => p.Id == spec.Character)?.Name ?? spec.Character,
                        });
                    else if (spec.Kind == "headliner" && data.FindHeadliner(spec.Character) is Headliner h && h.Ready)
                        seats.Add(HeadlinerSeat(t, h, spec.Seat, night, "headliner"));
                    else seats.Add(DrawSeat(profile, data, table, night, spec.Seat, ctx, seats.Select(o => o.Handle)));
                }
                return seats;
            }
            // Seat 4's closer: the first unbeaten headliner, or the other at 50/50 when both are; the other may sit as a wildcard.
            var ready = Unbeaten(profile, data, t);
            var closer = ready.Count == 0 ? null : ready.Count >= 2 && rng.NextDouble() < 0.5 ? ready[1] : ready[0];
            var wild = ready.FirstOrDefault(h => h != closer);
            int wildSeat = wild != null && rng.NextDouble() < AlleyEconomy.WildcardChance ? 1 + rng.Next(3) : 0;
            // Penny's cameo at a Street table: seat 1 with PennyChance (its own roll, so the rest of the night is drawn as before), unless the wildcard has it.
            bool penny = PennyBase(t, ctx) != null && wildSeat != 1
                         && Hash.Mix(SeatSeed(profile, table, night, 0), 0x9E77u) / 4294967296.0 < AlleyEconomy.PennyChance;
            for (int seat = 1; seat <= Matches; seat++)
            {
                if (seat == Matches && closer != null) seats.Add(HeadlinerSeat(t, closer, seat, night, "headliner"));
                else if (seat == wildSeat) seats.Add(HeadlinerSeat(t, wild!, seat, night, "wildcard"));
                else if (seat == 1 && penny) seats.Add(PennySeat(profile, data, table, night, ctx));
                else seats.Add(DrawSeat(profile, data, table, night, seat, ctx, seats.Select(o => o.Handle)));
            }
            return seats;
        }

        /// <summary>A build that can sit: ready, with its deck file present (a `ready: yes` build whose deck is missing is a data problem, and never seats).</summary>
        private static bool CanSit(Headliner? h, AlleyContext ctx) => h != null && h.IsBuild && h.Ready && h.Deck.Length > 0 && ctx.LoadDeck(h.Deck) != null;

        /// <summary>
        /// The table's order for its pool (section 4.4): the ready, non-champion pool builds shuffled once by
        /// Hash.Mix(salt, 0x4E11 ^ table), then packed so that no window of three (the positions a night takes) shares a
        /// family where the pool allows, so the family rule rarely has to skip anyone and nobody is starved by a same-family
        /// build ahead of it. Fixed for a save until the pool changes.
        /// </summary>
        private static List<Headliner> PoolOrder(Profile profile, AlleyData data, int table, AlleyTable t, AlleyContext ctx)
        {
            var pool = t.Pool.Where(id => !t.Headliners.Contains(id)).Distinct().Select(data.FindBuild).Where(h => CanSit(h, ctx)).Select(h => h!).ToList();
            var shuffle = new Random(unchecked((int)Hash.Mix(profile.Collection.Salt, 0x4E11u ^ (uint)table)));
            var shuffled = pool.Select(h => (h, k: shuffle.Next())).OrderBy(x => x.k).ThenBy(x => x.h.Id, StringComparer.Ordinal).Select(x => x.h).ToList();
            var order = new List<Headliner>();
            while (shuffled.Count > 0)
            {
                var window = order.Skip(order.Count / 3 * 3).ToList();
                var next = shuffled.FirstOrDefault(h => !window.Any(w => w.Family.Intersect(h.Family).Any())) ?? shuffled[0];
                shuffled.Remove(next);
                order.Add(next);
            }
            return order;
        }

        /// <summary>
        /// A No Limits night (section 4.4). Seat 4, the end chair: the first ready unbeaten champion in `champions:` order (no
        /// 50/50); the next ready unbeaten champion takes one of seats 1-3 with <see cref="AlleyEconomy.WildcardChance"/> (its seat
        /// by the night's seed); none unbeaten: a fourth pool build sits there (and the Encore opens). Seats 1-3 rotate through
        /// <see cref="PoolOrder"/>: night k takes builds from position 3k mod n onward, in order, skipping one already seated
        /// tonight, the wildcard, and one sharing a family with another of seats 1-3 or the wildcard (the end chair is exempt);
        /// when the skips leave a seat empty the family rule is relaxed, then repeats are allowed. Each seated build's binder is
        /// restocked (<see cref="Ante.Restock"/>): a night is drawn exactly once, so that's once a night.
        /// </summary>
        private static List<AlleyOpponent> DrawNoLimitsNight(Profile profile, AlleyData data, int table, int night, AlleyContext ctx)
        {
            var t = data.Tables[table];
            var rng = new Random(unchecked((int)SeatSeed(profile, table, night, 0)));
            var champions = t.Headliners.Select(data.FindBuild).Where(h => CanSit(h, ctx)).Select(h => h!).ToList();
            var unbeaten = champions.Where(h => !(profile.Headliners.TryGetValue(h.Id, out var st) && st.Beaten > 0)).ToList();
            var closer = unbeaten.FirstOrDefault();
            var wild = unbeaten.Skip(1).FirstOrDefault();
            int wildSeat = wild != null && rng.NextDouble() < AlleyEconomy.WildcardChance ? 1 + rng.Next(3) : 0;
            var order = PoolOrder(profile, data, table, t, ctx);
            int seatCount = t.SeatCount;
            var seats = new AlleyOpponent?[seatCount + 1];
            if (closer != null) seats[seatCount] = HeadlinerSeat(t, closer, seatCount, night, "headliner");
            if (wildSeat > 0) seats[wildSeat] = HeadlinerSeat(t, wild!, wildSeat, night, "wildcard");
            var drawn = new List<Headliner>();
            if (order.Count > 0)
            {
                int cursor = (3 * night) % order.Count;
                for (int seat = 1; seat <= seatCount; seat++)
                {
                    if (seats[seat] != null) continue;
                    Headliner? pick = null;
                    for (int pass = 0; pass < 3 && pick == null; pass++)
                        for (int step = 0; step < order.Count && pick == null; step++)
                        {
                            var h = order[(cursor + step) % order.Count];
                            if (h == wild) continue;
                            if (pass < 2 && drawn.Contains(h)) continue;
                            // The family rule: among seats 1-3 (drawn so far) and the wildcard; never for the end chair (seat 4: a
                            // champion, or the fourth pool build when none is unbeaten).
                            bool chair = seat == seatCount;
                            var others = wildSeat > 0 ? drawn.Append(wild!) : drawn; // only a wildcard that sits (review fix 3)
                            if (pass == 0 && !chair && others.Any(o => o.Family.Intersect(h.Family).Any())) continue;
                            pick = h;
                            cursor = (cursor + step + 1) % order.Count;
                        }
                    if (pick == null) break;
                    drawn.Add(pick);
                    seats[seat] = new AlleyOpponent { Table = t.Id, Seat = seat, Night = night, Kind = BuildKind, Character = pick.Id, Handle = pick.Handle, Base = pick.Deck };
                }
            }
            var lineup = new List<AlleyOpponent>();
            for (int seat = 1; seat <= seatCount; seat++)
                if (seats[seat] is AlleyOpponent o) lineup.Add(o);
            // The restock, once the night is whole (a short lineup is drawn again on the next call, so it mustn't restock).
            foreach (var o in lineup)
            {
                Complete(o, data, ctx);
                if (lineup.Count == seatCount) Ante.Restock(profile, o.Binder, ctx);
            }
            return lineup;
        }

        /// <summary>Penny's base deck (her profile's first deck, warriors) when the table is a Street table with it among its bases; else null.</summary>
        private static string? PennyBase(AlleyTable t, AlleyContext ctx)
        {
            var deck = ctx.Roster.FirstOrDefault(p => p.Id == PennyId)?.Decks.FirstOrDefault();
            return t.Tier == AlleyTier.Street && deck != null && t.Bases.Contains(deck) && ctx.LoadDeck(deck) != null ? deck : null;
        }

        /// <summary>
        /// Penny (docs/dev/ALLEY-SPEC-2026-11-01.md section 12): her warriors with a full splash (seat 1's seed), her own name and
        /// voice, skill as the table's, and her binder of one Common (<see cref="Ante"/>: nothing of yours is held).
        /// </summary>
        private static AlleyOpponent PennySeat(Profile profile, AlleyData data, int table, int night, AlleyContext ctx)
        {
            var o = DrawSeat(profile, data, table, night, 1, ctx, Enumerable.Empty<string>(), forcedBase: PennyBase(data.Tables[table], ctx));
            o.Character = PennyId;
            o.Handle = ctx.Roster.FirstOrDefault(p => p.Id == PennyId)?.Name ?? "Penny";
            return o;
        }

        private static AlleyOpponent HeadlinerSeat(AlleyTable t, Headliner h, int seat, int night, string kind) =>
            new AlleyOpponent { Table = t.Id, Seat = seat, Night = night, Kind = kind, Character = h.Id, Handle = h.Handle, Base = h.Deck };

        /// <summary>
        /// A generated regular: its base (seats 1-3 take the night's shuffled bases in turn, so three seats get three
        /// bases when the table has them; seat 4 is the closer's), then from the seat's seed the splash, a name not used
        /// tonight and the tint.
        /// </summary>
        private static AlleyOpponent DrawSeat(Profile profile, AlleyData data, int table, int night, int seat, AlleyContext ctx, IEnumerable<string> taken, int retry = 0,
            string? forcedBase = null)
        {
            var t = data.Tables[table];
            var spec = t.Seats.FirstOrDefault(s => s.Seat == seat);
            var nightTable = SourceOf(data, t, spec);
            var e = spec != null && spec.Event.Length > 0 ? Alley.Events.FirstOrDefault(x => x.Name == spec.Event) ?? nightTable.Event : nightTable.Event;
            var bases = nightTable.Bases.Where(b => ctx.LoadDeck(b) != null).ToList();
            // The night's own seed shuffles the bases, so seats 1-3 take different ones.
            var shuffle = new Random(unchecked((int)Hash.Mix(SeatSeed(profile, table, night, 0), 0xBA5Eu)));
            var order = bases.Select(b => (b, k: shuffle.Next())).OrderBy(x => x.k).Select(x => x.b).ToList();
            var rng = new Random(unchecked((int)SeatSeed(profile, table, night, seat, retry)));
            string baseName = forcedBase ?? (seat == Matches && spec == null && t.Closer.Length > 0 && ctx.LoadDeck(t.Closer) != null ? t.Closer
                : order.Count == 0 ? "" : order[(seat - 1 + retry) % order.Count]);
            var o = new AlleyOpponent { Table = t.Id, Seat = seat, Night = night, Kind = "regular", Character = RegularId, Base = baseName };
            Dress(o, e, rng, data, taken, ctx);
            return o;
        }

        /// <summary>
        /// The table a regular seat draws from: its `event:` table (Nyx's seat 1: The Black Market), else the table itself,
        /// else (a table with no event of its own: Orvel's, Nyx's) the table it opens from.
        /// </summary>
        private static AlleyTable SourceOf(AlleyData data, AlleyTable t, AlleySeatSpec? spec)
        {
            if (spec != null && spec.Event.Length > 0 && data.Tables.FirstOrDefault(x => x.Event?.Name == spec.Event) is AlleyTable byEvent) return byEvent;
            if (t.Event != null) return t;
            int from = data.IndexOf(t.Opens);
            return from >= 0 && data.Tables[from] != t ? SourceOf(data, data.Tables[from], null) : t;
        }

        /// <summary>
        /// A regular as drawn from its rng (in this order: the splash seed, the first name, the tint): its base splashed for
        /// the event (<see cref="Alley.SplashChecked"/>), the handle (the most valuable added card with a handle: line, and a
        /// first name nobody in <paramref name="taken"/> has) and the tint.
        /// </summary>
        private static void Dress(AlleyOpponent o, AlleyEvent? e, Random rng, AlleyData data, IEnumerable<string> taken, AlleyContext ctx)
        {
            var baseName = o.Base;
            int splashSeed = rng.Next();
            var baseDeck = baseName.Length > 0 ? ctx.LoadDeck(baseName) : null;
            if (baseDeck != null && e != null)
            {
                baseDeck.Name = baseName;
                var curation = ctx.Curated.TryGetValue(e.Name, out var c) ? c : null;
                var splash = Alley.SplashChecked(baseDeck, e, ctx.Pool(e.EraCap), ctx.List, splashSeed, ctx.Cards, curation, out _);
                o.Added = splash.Added.ToList();
                o.Cut = splash.Cut.ToList();
            }
            // The handle: the most valuable added card with a handle: line, and a first name nobody has tonight.
            var prefix = o.Added.Select(a => Banlist.BaseCode(a, ctx.Cards)).OrderByDescending(a => ctx.Cards.Get(a) is CardInfo i ? AutoDeck.Value(i) : 0).ThenBy(a => a)
                .Select(a => data.Handles.FirstOrDefault(h => h.card == a).prefix).FirstOrDefault(p => !string.IsNullOrEmpty(p));
            var used = new HashSet<string>(taken.Select(h => h.Split(' ').Last()));
            var free = data.Names.Where(n => !used.Contains(n)).ToList();
            var first = free.Count > 0 ? free[rng.Next(free.Count)] : data.Names.Count > 0 ? data.Names[rng.Next(data.Names.Count)] : "";
            o.Handle = prefix != null ? $"{prefix} {first}".Trim() : $"{TitleOf(baseDeck, baseName)} Regular";
            o.Tint = Tint(rng.NextDouble());
        }

        private static string TitleOf(Deck? deck, string name) =>
            deck?.Note("title") is { Length: > 0 } title ? title : string.Join(" ", name.Split('-').Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w.Substring(1) : w));

        /// <summary>A hue at saturation 0.45 and value 0.75, as 0xRRGGBB.</summary>
        private static uint Tint(double hue)
        {
            double s = 0.45, v = 0.75, h = hue * 6, c = v * s, x = c * (1 - Math.Abs(h % 2 - 1)), m = v - c;
            var (r, g, b) = (int)h switch { 0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x) };
            uint B(double u) => (uint)Math.Round((u + m) * 255);
            return (B(r) << 16) | (B(g) << 8) | B(b);
        }

        /// <summary>Fills what isn't saved: skill, Elo and the binder key.</summary>
        private static void Complete(AlleyOpponent o, AlleyData data, AlleyContext ctx)
        {
            bool bracket = o.Table == BracketTable;
            int ti = data.IndexOf(bracket ? o.From : o.Table);
            var t = ti >= 0 ? data.Tables[ti] : null;
            var spec = bracket ? null : t?.Seats.FirstOrDefault(s => s.Seat == o.Seat);
            double Rating(string deck) => ctx.Ratings.TryGetValue(deck, out var r) ? r : 0;
            switch (o.Kind)
            {
                case "regular":
                {
                    var source = t != null ? SourceOf(data, t, spec) : null;
                    var e = spec != null && spec.Event.Length > 0 ? Alley.Events.FirstOrDefault(x => x.Name == spec.Event) ?? source?.Event : source?.Event;
                    bool closer = !bracket && spec == null && o.Seat == Matches;
                    // A headliner seat's stand-in ("else regular 0.97") plays at its own skill.
                    double standIn = spec != null && spec.Kind == "headliner" ? ElseSkill(spec) : 0;
                    o.Skill = spec != null && spec.Skill > 0 ? spec.Skill : standIn > 0 ? standIn : closer ? 1.0 : e?.Skill ?? 0.7;
                    o.Elo = Rating(o.Base) + (source?.SplashElo ?? 0);
                    // Penny keeps one binder across nights (her one Common); every other regular's goes with its night. A bracket has no ante.
                    o.Binder = bracket ? "" : o.Character == PennyId ? PennyId : $"regular:{o.Table}.{o.Seat}.{o.Night}";
                    break;
                }
                case "authored":
                {
                    o.Skill = spec?.Skill > 0 ? spec.Skill : 1.0;
                    // A deck built for the alley shows its recipe's rating plus the splash gain of the table it opens from (rated
                    // or not); a roster deck seated as built (`as-built`) its own rating.
                    var deck = spec?.AsBuilt == true ? null : ctx.LoadDeck(o.Base);
                    int from = t != null ? data.IndexOf(t.Opens) : -1;
                    o.Elo = deck == null ? Rating(o.Base) : Rating(AI.Playbooks.Playbooks.RecipeOf(deck)) + (from >= 0 ? data.Tables[from].SplashElo : 0);
                    o.Binder = t?.Tier == AlleyTier.Orvel ? "orvel" : $"nyx:{o.Seat}";
                    break;
                }
                default:
                {
                    var h = data.FindHeadliner(o.Character);
                    var who = ctx.Roster.FirstOrDefault(p => p.Id == o.Character);
                    o.IsBuild = h?.IsBuild == true;
                    o.Skill = who?.Skill ?? 1.0;
                    var deck = h != null ? ctx.LoadDeck(h.Deck) : null;
                    // A headliner playing its own route ("#recipe <id>") shows the rated deck its "#rating" names; a build its
                    // own anchored rating (peak-ratings.txt, merged into the ratings).
                    o.Elo = Rating(o.Base) is var r && r > 0 ? r
                        : deck != null ? Rating(deck.Note("rating") is { Length: > 0 } rated ? rated : AI.Playbooks.Playbooks.RecipeOf(deck)) : 0;
                    o.Binder = o.IsBuild ? $"nl:{o.Character}" : $"hl:{o.Character}";
                    break;
                }
            }
        }

        /// <summary>A headliner seat's stand-in skill: the number in its `else regular 0.97`; 0 for none.</summary>
        private static double ElseSkill(AlleySeatSpec spec)
        {
            var words = spec.Else.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return words.Length >= 2 && double.TryParse(words[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : 0;
        }

        /// <summary>
        /// An encore (once both of the table's headliners are beaten... or any beaten ready headliner of the table): a
        /// one-seat night (seat 0) at the table's stake, from the headliner's own binder. Null if it can't sit. Save after.
        /// </summary>
        public static AlleyOpponent? Encore(Profile profile, AlleyData data, int table, string headliner, AlleyContext ctx)
        {
            ctx.Data ??= data;
            if (!IsUnlocked(profile, data, table)) return null;
            var t = data.Tables[table];
            var h = data.FindHeadliner(headliner);
            if (h == null || !h.Ready || !t.Headliners.Contains(h.Id) || !(profile.Headliners.TryGetValue(h.Id, out var st) && st.Beaten > 0)) return null;
            profile.AlleyOpponents.RemoveAll(o => o.Table == t.Id && o.Seat == 0);
            var encore = HeadlinerSeat(t, h, 0, State(profile, t).Night, "encore");
            Complete(encore, data, ctx);
            profile.AlleyOpponents.Add(encore);
            return encore;
        }

        /// <summary>Whether the Encore button shows: every ready headliner of the table is beaten (and it has one).</summary>
        public static bool EncoreOpen(Profile profile, AlleyData data, int table) =>
            IsUnlocked(profile, data, table) && data.Tables[table].Headliners.Select(data.FindHeadliner).OfType<Headliner>().Where(h => h.Ready).ToList() is var hs
            && hs.Count > 0 && hs.All(h => profile.Headliners.TryGetValue(h.Id, out var st) && st.Beaten > 0);

        // ---- Alley brackets' fields -------------------------------------------------------------------

        /// <summary>
        /// The table the Alley Brawl's regulars come from (decision O6: never The Black Market's, whose regulars play the
        /// Midnight Open): the last cleared of the Street and Den tables before the last one, so Dumpster Row's, then The
        /// Basement Game's once it's cleared; the first while none is. -1 with no such table.
        /// </summary>
        public static int BrawlTable(Profile profile, AlleyData data)
        {
            var regular = Enumerable.Range(0, data.Tables.Count).Where(i => data.Tables[i].Tier is AlleyTier.Street or AlleyTier.Den).ToList();
            if (regular.Count == 0) return -1;
            var allowed = regular.Count > 1 ? regular.Take(regular.Count - 1).ToList() : regular;
            var cleared = allowed.Where(i => IsCleared(profile, data, i)).ToList();
            return cleared.Count > 0 ? cleared[cleared.Count - 1] : allowed[0];
        }

        /// <summary>The Alley Brawl's 7 CPUs (<see cref="BracketField"/>): splash regulars from <see cref="BrawlTable"/>, seeded 2-8 by Elo.</summary>
        public static List<BracketEntrant> BrawlField(Profile profile, AlleyData data, AlleyContext ctx, uint seed) =>
            FieldOf(profile, data, Brackets.BrawlKind, ctx, seed);

        /// <summary>
        /// The Midnight Open's 15 CPUs (<see cref="BracketField"/>): Ilara Gale at seed 1, the circuit characters and Library
        /// bosses as written, and The Black Market's regulars, seeded 3-16 by Elo (you are seed 2).
        /// </summary>
        public static List<BracketEntrant> MidnightField(Profile profile, AlleyData data, AlleyContext ctx, uint seed) =>
            FieldOf(profile, data, Brackets.MidnightKind, ctx, seed);

        private static List<BracketEntrant> FieldOf(Profile profile, AlleyData data, string kind, AlleyContext ctx, uint seed)
        {
            var spec = data.Brackets.FirstOrDefault(b => b.Kind == kind);
            return spec == null ? new List<BracketEntrant>() : BracketField(profile, data, spec, ctx, seed).field;
        }

        /// <summary>
        /// A bracket's CPUs from its `field:` lines, and your seed: "you &lt;seed&gt;"; "&lt;character&gt; [deck] [seed]" (a roster
        /// character with its deck, rated by deck-ratings.txt; a seed as written); "splash &lt;n&gt; &lt;table&gt; | cleared" (n
        /// regulars of that table, or of <see cref="BrawlTable"/>, splashed as at the table from the bracket's seed, rated at
        /// their base plus the table's splash gain, each with a handle nobody else in the field has). CPUs without a written
        /// seed take the seeds left, by Elo, highest first. Deterministic in <paramref name="seed"/> (and, for "cleared", the
        /// tables you've cleared). A line with "| after: &lt;table&gt;" (the Brawl's out-of-towners, section 8) seats its entrant
        /// once that table is cleared (and its deck file exists); before, it's one more regular from the first "splash ...
        /// cleared" line's source, unseeded, drawn in that line's stream (so the field is the same seven regulars as before).
        /// </summary>
        public static (List<BracketEntrant> field, int yourSeed) BracketField(Profile profile, AlleyData data, AlleyBracketSpec spec, AlleyContext ctx, uint seed)
        {
            ctx.Data ??= data;
            var field = new List<BracketEntrant>();
            var written = new HashSet<int>();
            var handles = new List<string>();
            int yourSeed = 1;
            bool Active(string after) => after.Length == 0 || IsCleared(profile, data, data.IndexOf(after));
            // The inactive after: lines stand in as regulars from the first "splash ... cleared" line (or their own, without one).
            int standIns = spec.Field.Select(AlleyBracketSpec.ParseField)
                .Count(f => f.words.Length > 0 && f.words[0] != "you" && f.words[0] != "splash" && f.after.Length > 0
                            && (!Active(f.after) || ctx.LoadDeck(f.words.Length > 1 ? f.words[1] : "") == null));
            bool clearedSplash = spec.Field.Select(AlleyBracketSpec.ParseField).Any(f => f.words.Length > 0 && f.words[0] == "splash" && string.Join(" ", f.words.Skip(2)) is "" or "cleared");
            for (int line = 0; line < spec.Field.Count; line++)
            {
                var (w, after) = AlleyBracketSpec.ParseField(spec.Field[line]);
                if (w.Length == 0) continue;
                if (w[0] == "you")
                {
                    yourSeed = w.Length > 1 && int.TryParse(w[1], out var ys) ? ys : 1;
                    continue;
                }
                if (w[0] == "splash")
                {
                    int n = w.Length > 1 && int.TryParse(w[1], out var k) ? k : 0;
                    var which = string.Join(" ", w.Skip(2));
                    bool cleared = which.Length == 0 || which == "cleared";
                    if (cleared && standIns > 0) { n += standIns; standIns = 0; }
                    int ti = cleared ? BrawlTable(profile, data) : data.IndexOf(which);
                    if (ti >= 0) field.AddRange(BracketRegulars(data, ti, n, Hash.Mix(seed, 0x5B1Au, (uint)line), ctx, handles));
                    continue;
                }
                if (after.Length > 0 && (!Active(after) || ctx.LoadDeck(w.Length > 1 ? w[1] : "") == null))
                {
                    // Not yet (or its deck isn't here): a regular in its place, from this line's own stream when no cleared splash took it.
                    if (!clearedSplash && BrawlTable(profile, data) is var bt && bt >= 0) field.AddRange(BracketRegulars(data, bt, 1, Hash.Mix(seed, 0x5B1Au, (uint)line), ctx, handles));
                    continue;
                }
                var who = w[0];
                var deck = w.Length > 1 ? w[1] : ctx.Roster.FirstOrDefault(p => p.Id == who)?.Decks.FirstOrDefault() ?? "";
                var e = new BracketEntrant { Character = who, Deck = deck, Elo = DeckRatings.Of(ctx.Ratings, deck) };
                if (w.Length > 2 && int.TryParse(w[2], out var fixedSeed)) { e.Seed = fixedSeed; written.Add(fixedSeed); }
                field.Add(e);
            }
            var free = Enumerable.Range(1, spec.Size).Where(x => x != yourSeed && !written.Contains(x)).ToList();
            var unseeded = field.Where(e => e.Seed == 0).OrderByDescending(e => e.Elo).ThenBy(e => e.Splash?.Base ?? e.Deck, StringComparer.Ordinal)
                .ThenBy(e => e.Character, StringComparer.Ordinal).ThenBy(e => e.Splash?.Handle ?? "", StringComparer.Ordinal).ToList();
            for (int i = 0; i < unseeded.Count && i < free.Count; i++) unseeded[i].Seed = free[i];
            return (field, yourSeed);
        }

        /// <summary>
        /// <paramref name="n"/> splash regulars of a table for a bracket: its bases shuffled by the seed and taken in turn (as
        /// few repeats as the table allows), each splashed for the table's event as a seat is, named apart from
        /// <paramref name="taken"/> (which gains their handles).
        /// </summary>
        private static List<BracketEntrant> BracketRegulars(AlleyData data, int table, int n, uint seed, AlleyContext ctx, List<string> taken)
        {
            var list = new List<BracketEntrant>();
            var t = data.Tables[table];
            var source = SourceOf(data, t, null);
            var bases = source.Bases.Where(b => ctx.LoadDeck(b) != null).ToList();
            if (bases.Count == 0 || source.Event == null) return list;
            var shuffle = new Random(unchecked((int)Hash.Mix(seed, 0xBA5Eu)));
            var order = bases.Select(b => (b, k: shuffle.Next())).OrderBy(x => x.k).Select(x => x.b).ToList();
            for (int i = 0; i < n; i++)
            {
                var rng = new Random(unchecked((int)Hash.Mix(seed, 0x5EA7u, (uint)i)));
                var o = new AlleyOpponent { Table = BracketTable, Kind = "regular", Character = RegularId, Base = order[i % order.Count], From = source.Id };
                Dress(o, source.Event, rng, data, taken, ctx);
                taken.Add(o.Handle);
                Complete(o, data, ctx);
                list.Add(new BracketEntrant { Character = RegularId, Elo = o.Elo, Splash = o });
            }
            return list;
        }

        // ---- Decks, faces and show-off cards ------------------------------------------------------------

        /// <summary>
        /// An opponent's deck: a regular's base made legal for the Alley (Loaners.Adjust), its cuts removed and its adds
        /// added (rebuilt from the saved lists, never re-rolled), "#recipe" its base; a headliner's or an authored deck as
        /// written (a headliner keeps its own "#recipe", which routes its playbook). Name = <see cref="AlleyOpponent.DeckKey"/>.
        /// </summary>
        public static Deck DeckOf(AlleyOpponent o, AlleyContext ctx)
        {
            Deck deck;
            if (o.Kind == "regular")
            {
                var baseDeck = ctx.LoadDeck(o.Base);
                if (baseDeck == null) return new Deck { Name = o.DeckKey };
                deck = Loaners.Adjust(baseDeck, ctx.List, ctx.Cards).Deck;
                deck.Side.Clear();
                foreach (var cut in o.Cut) if (!deck.Main.Remove(cut)) deck.Extra.Remove(cut);
                foreach (var add in o.Added) (ctx.Cards.Get(add) is CardInfo i && (i.Type & CardType.ExtraDeckTypes) != 0 ? deck.Extra : deck.Main).Add(add);
                deck.Notes["recipe"] = o.Base;
            }
            else
            {
                deck = ctx.LoadDeck(o.Base)?.Clone() ?? new Deck();
                if (deck.Note("recipe").Length == 0 && deck.Note("loaner").Length == 0) deck.Notes["recipe"] = o.Base;
            }
            deck.Name = o.DeckKey;
            deck.Looks = null;
            return deck;
        }

        /// <summary>
        /// A deck key's deck: "alley:&lt;table&gt;.&lt;seat&gt;" from tonight's saved opponents, "alley:bracket.&lt;slot&gt;" from the
        /// running alley bracket's splash regular at that slot; null if unknown.
        /// </summary>
        public static Deck? ResolveDeck(Profile profile, string deckKey, AlleyContext ctx)
        {
            if (!Alley.IsAlleyDeck(deckKey)) return null;
            var key = deckKey.Substring(Alley.DeckPrefix.Length);
            var dot = key.LastIndexOf('.');
            if (dot <= 0 || !int.TryParse(key.Substring(dot + 1), out var seat)) return null;
            var table = key.Substring(0, dot);
            if (table == BracketTable)
                return profile.Bracket is Bracket b && seat >= 0 && seat < b.Size && b.Slots[seat]?.Splash is AlleyOpponent splash ? DeckOf(splash, ctx) : null;
            var o = profile.AlleyOpponents.FirstOrDefault(x => x.Table == table && x.Seat == seat);
            return o == null ? null : DeckOf(o, ctx);
        }

        /// <summary>
        /// The running alley bracket's splash regular at a slot, with its skill and Elo filled (they aren't saved): for
        /// <see cref="PersonalityOf"/>, <see cref="DeckOf"/> and <see cref="Showcase"/>. Null when that slot isn't one.
        /// </summary>
        public static AlleyOpponent? BracketOpponent(Profile profile, int slot, AlleyContext ctx)
        {
            if (!(profile.Bracket is Bracket b) || slot < 0 || slot >= b.Size || !(b.Slots[slot]?.Splash is AlleyOpponent o)) return null;
            if (ctx.Data != null) Complete(o, ctx.Data, ctx);
            return o;
        }

        /// <summary>
        /// Who they are: a regular is a copy of its base deck's owner (the roster character whose decks: lists it first;
        /// Balanced if none) as "alley-regular", named by the handle, titled by the table, at the seat's skill, with no lines
        /// of its own (the alley-voice pools speak for it); a headliner or authored seat is its own character.
        /// </summary>
        public static Personality PersonalityOf(AlleyOpponent o, AlleyContext ctx)
        {
            // A bracket regular is titled by the table it was drawn from (and gets its skill if it was loaded without one).
            var tableKey = o.Table == BracketTable && o.From.Length > 0 ? o.From : o.Table;
            if (o.Skill <= 0 && ctx.Data != null) Complete(o, ctx.Data, ctx);
            var title = ctx.Data != null && ctx.Data.IndexOf(tableKey) is var ti && ti >= 0 ? ctx.Data.Tables[ti].Name : tableKey;
            // Penny plays as herself (her profile's name and lines), at the seat's skill.
            if (o.Character == PennyId && ctx.Roster.FirstOrDefault(p => p.Id == PennyId) is Personality penny)
            {
                var her = penny.Copy();
                if (o.Skill > 0) her.Skill = o.Skill;
                her.Decks = new List<string> { o.Base };
                return her;
            }
            if (o.Kind == "regular")
            {
                var owner = ctx.Roster.FirstOrDefault(p => p.Decks.FirstOrDefault() == o.Base) ?? Personality.Balanced;
                var p = owner.Copy();
                p.Id = RegularId;
                p.Name = o.Handle;
                p.Title = title;
                p.Skill = o.Skill;
                p.Intro = p.Rematch = p.WinLine = p.LoseLine = p.Look = p.Signature = "";
                p.Lines = new Dictionary<string, List<string>>();
                p.Decks = new List<string> { o.Base };
                p.Ladder = 0;
                return p;
            }
            var who = ctx.Roster.FirstOrDefault(p => p.Id == o.Character)?.Copy() ?? new Personality { Id = o.Character, Name = o.Handle, Skill = o.Skill };
            if (o.Skill > 0) who.Skill = o.Skill;
            return who;
        }

        /// <summary>
        /// The versus screen's three show-off cards: a regular's added contraband, most valuable first, then its base's; a
        /// headliner's pieces; an authored deck's contraband (its ever-Forbidden cards, as its Unlisted page marks them), most
        /// valuable first, then its own showcase.
        /// </summary>
        public static IReadOnlyList<uint> Showcase(AlleyOpponent o, AlleyContext ctx)
        {
            var picks = new List<uint>();
            void Take(IEnumerable<uint> codes)
            {
                foreach (var c in codes)
                    if (picks.Count < 3 && ctx.Cards.Get(c) != null && !picks.Contains(c)) picks.Add(c);
            }
            double Value(uint c) => ctx.Cards.Get(c) is CardInfo i ? AutoDeck.Value(i) : 0;
            if (o.IsHeadliner && ctx.Data?.FindHeadliner(o.Character) is Headliner h) Take(h.Pieces);
            Take(o.Added.OrderByDescending(Value).ThenBy(c => c));
            var deck = o.Kind == "regular" ? ctx.LoadDeck(o.Base) : DeckOf(o, ctx);
            if (o.Kind == "authored") Take(ShowcaseContraband(o, ctx).OrderByDescending(Value).ThenBy(c => c));
            if (deck != null)
            {
                // The base's own showcase: its starters, then its strongest monsters, then anything.
                Take(deck.Starters);
                Take(deck.Extra.Concat(deck.Main).Where(c => ctx.Cards.Get(c) is CardInfo i && (i.Type & CardType.Monster) != 0)
                    .OrderByDescending(c => ctx.Cards.Get(c)!.Attack));
                Take(deck.Main);
            }
            return picks;
        }

        /// <summary>
        /// The cards the versus screen's CONTRABAND label is about (base codes): a regular's splash, a headliner's pieces, an
        /// authored deck's ever-Forbidden cards. The label shows only over a showcase holding one of them.
        /// </summary>
        public static HashSet<uint> ShowcaseContraband(AlleyOpponent o, AlleyContext ctx)
        {
            if (o.Kind == "regular") return new HashSet<uint>(o.Added.Concat(o.Added.Select(c => Banlist.BaseCode(c, ctx.Cards))));
            if (o.IsHeadliner) return new HashSet<uint>(ctx.Data?.FindHeadliner(o.Character)?.Pieces ?? new List<uint>());
            var forbidden = new HashSet<uint>(ctx.Pool(9999));
            var deck = DeckOf(o, ctx);
            return new HashSet<uint>(deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, ctx.Cards)).Where(forbidden.Contains));
        }

        // ---- Results -----------------------------------------------------------------------------

        /// <summary>
        /// Records your duel at the next seat (or at the seat held in <see cref="Profile.AnteLive"/>, an encore's too);
        /// winner 0 you, 1 them, 2 draw; turns and who went first (0 you, 1 them) for the first-turn guard, which never
        /// applies to a duel you <paramref name="conceded"/> (a quit is a loss). <paramref name="turnCap"/>: whether the turn
        /// cap decided it (<see cref="TurnCap.Decided"/>; null: judged by the turns, past <see cref="AlleyEconomy.AlleyTurns"/>),
        /// for "turncap". <see cref="AlleyResult.Fill"/> is its scenes' placeholders ({card}: the card won or lost). Settles the
        /// ante; counts the duel (<see cref="Profile.NoteResult"/> at "alley:&lt;table id&gt;.&lt;seat&gt;", AlleyDuels), the win or
        /// loss against the character, EXP at the Library's rate with level-ups in tokens. A win marks the seat; the
        /// fourth ends the night (the first time, clears the table). A loss or a draw leaves the seat as it was (a draw is
        /// played again). A win counts in <see cref="Profile.Wins"/> (the alley's own record) but not in
        /// <see cref="DuelReward.WinsAdded"/>: an alley win is never a card unlock's bond, gift or drop (the CPU's staked card
        /// is the only prize). <paramref name="comboBroken"/>: a headliner's route was stopped before its payoff and never landed
        /// this duel (<see cref="AI.Playbooks.ComboWatch.Broken"/>); with a win (not an encore) it earns that headliner's Combo
        /// Breaker poster (<see cref="Posters.Breaker"/>), the first time with "combobreaker:&lt;id&gt;" and
        /// <see cref="AlleyResult.ComboBreaker"/>. Save after.
        /// </summary>
        public static AlleyResult Record(Profile profile, AlleyData data, int table, int winner, int turns, int firstPlayer, AlleyContext ctx,
            bool conceded = false, bool? turnCap = null, bool comboBroken = false)
        {
            ctx.Data ??= data;
            var result = new AlleyResult();
            if (table < 0 || table >= data.Tables.Count) return result;
            var t = data.Tables[table];
            var live = profile.AnteLive != null && profile.AnteLive.Table == t.Id ? profile.AnteLive : null;
            int seat = live?.Seat ?? NextSeat(profile, data, table);
            if (seat < 0) return result;
            if (seat > 0 && !profile.AlleyOpponents.Any(o => o.Table == t.Id && o.Seat == seat)) Lineup(profile, data, table, ctx);
            var opp = profile.AlleyOpponents.FirstOrDefault(o => o.Table == t.Id && o.Seat == seat);
            if (opp == null) return result;
            Complete(opp, data, ctx);
            var reward = result.Reward;
            var parts = new List<string>();

            // The stakes first: a first-turn loss (they went first and won before your first turn; at No Limits, within
            // NoLimitsGuardTurns) gives yours back, unless you conceded.
            int guard = t.Tier == AlleyTier.NoLimits ? AlleyEconomy.NoLimitsGuardTurns : 1;
            bool firstTurnLoss = !conceded && winner == 1 && turns <= guard && firstPlayer == 1;
            var theirs = live?.Theirs;
            // The first restored card you win (a build's stakes: card that's also restored:) raises "restored" once: before
            // this one, no Ante copy of any build's restored lead was in your collection.
            var restoredLeads = new HashSet<uint>(data.Builds.SelectMany(b => b.Stakes.Where(b.Restored.Contains)));
            if (live != null)
            {
                result.Ante = Ante.Settle(profile, winner, firstTurnLoss, ctx);
                result.Events.AddRange(result.Ante.Events);
                if (result.Ante.Summary.Length > 0) parts.Add(result.Ante.Summary);
                // Once ever, keyed in Seen like the wall (review fix 5: a copy won at Orvel's, or a 4th copy not kept, can't move it).
                if (opp.IsBuild && result.Ante.Won is CardCopy won && restoredLeads.Contains(won.Code) && profile.Seen.Add(RestoredEvent))
                    result.Events.Add(RestoredEvent);
            }
            if (turnCap ?? turns > AlleyEconomy.AlleyTurns) result.Events.Add("turncap");

            profile.AlleyDuels++;
            profile.NoteResult($"alley:{t.Id}.{seat}", winner);
            if (opp.IsHeadliner) HeadlinerOf(profile, opp.Character).Met++;
            bool firstWin = winner == 0 && !profile.HasBeaten(opp.Character);
            if (winner == 0)
            {
                // Not AddWin: the card unlocks (CardUnlocks.AfterDuel) must never see an alley win.
                profile.Wins[opp.Character] = profile.WinsAgainst(opp.Character) + 1;
                reward.FirstWin = firstWin;
                result.SeatWon = true;
                if (opp.Character == PennyId && firstWin) result.Events.Add(PennyEvent);
                // A contraband deck beaten for the first time: a page for the Unlisted binder (the first raises "unlisted").
                if (Unlisted.Add(profile, opp))
                {
                    result.Page = profile.UnlistedPages[profile.UnlistedPages.Count - 1];
                    result.Events.Add(Unlisted.Event);
                    parts.Add($"a page for the Unlisted binder ({opp.Handle})");
                }
                if (opp.IsHeadliner)
                {
                    var hs = HeadlinerOf(profile, opp.Character);
                    if (hs.Beaten++ == 0) result.Events.Add((opp.IsBuild ? PilotBeatEvent : "headlinerbeat:") + opp.Character);
                    if (opp.Kind == "encore" && hs.Encores++ == 0) result.Events.Add("encore");
                    // Combo Breaker: the route stopped before its payoff, and you went on to win (an encore earns no poster; a build has none).
                    if (comboBroken && opp.Kind != "encore" && !opp.IsBuild && hs.Breaker++ == 0)
                    {
                        result.ComboBreaker = true;
                        result.Events.Add(ComboBreakerEvent + opp.Character);
                        parts.Add($"Combo Breaker! {opp.Handle}'s poster is yours");
                    }
                }
                if (seat == 0)
                {
                    profile.AlleyOpponents.Remove(opp);
                    parts.Add($"{opp.Handle} beaten again");
                }
                else
                {
                    var state = State(profile, t);
                    state.WonMask |= 1 << (seat - 1);
                    parts.Add($"{opp.Handle} beaten");
                    if (Enumerable.Range(1, t.SeatCount).All(s => (state.WonMask & (1 << (s - 1))) != 0))
                    {
                        result.NightOver = true;
                        if (!state.Cleared)
                        {
                            state.Cleared = result.FirstClear = true;
                            reward.Tokens = AlleyEconomy.ClearTokens;
                            profile.Tokens += reward.Tokens;
                            result.Events.Add("alleyclear:" + t.Name);
                            parts.Add($"{t.Name} cleared! +{reward.Tokens} Card Token{(reward.Tokens == 1 ? "" : "s")}");
                            var opened = data.Tables.Where(x => x.Opens == t.Name && x.Ready).Select(x => x.Name).ToList();
                            result.Opened.AddRange(opened);
                            if (opened.Count > 0) parts.Add($"{string.Join(" and ", opened)} open{(opened.Count == 1 ? "s" : "")}");
                        }
                        else parts.Add("The night's over");
                        // The next night: its regulars' binders go with them (they never sit again).
                        int ended = state.Night;
                        foreach (var key in profile.Binders.Keys.Where(k => k.StartsWith($"regular:{t.Id}.") && k.EndsWith($".{ended}")).ToList()) profile.Binders.Remove(key);
                        profile.AlleyOpponents.RemoveAll(o => o.Table == t.Id && o.Seat >= 1);
                        state.Night++;
                        state.WonMask = 0;
                    }
                }
            }
            else
            {
                if (winner == 1) profile.Losses[opp.Character] = profile.Losses.GetValueOrDefault(opp.Character) + 1;
                result.Replay = winner == 2;
                parts.Add(winner == 2 ? "A draw: the seat is played again" : $"{opp.Handle} keeps the seat");
            }
            profile.Earn(Ledger.Alley, AlleyEconomy.DuelDp);
            reward.Exp = Leveling.DuelExp(ctx.Library.ExpCircuit, winner, firstWin);
            reward.LevelUps = Leveling.AddExp(profile, reward.Exp, payDp: false);
            foreach (var up in reward.LevelUps) parts.Add(Leveling.Describe(up));
            result.Events.AddRange(reward.LevelUps.Select(u => "level:" + u.Level));
            var expired = Ante.TickRail(profile, c => ctx.Cards.Get(c)?.Name ?? c.ToString());
            foreach (var name in expired) parts.Add($"Moth sold your {name}");
            if (expired.Count > 0) result.Events.Add("railgone");
            reward.Summary = result.Summary = string.Join(" · ", parts);
            // The scenes' {card}: your card if it went on the rail, else theirs (won, or kept); {sold}: the card Moth just sold
            // (railgone's scene), whatever the stakes did.
            result.Fill = FillFor(profile, data, opp, result.Ante?.Railed is RailEntry railed ? AsStake(railed.Copy) : theirs, ctx.Cards);
            if (expired.Count > 0) result.Fill[SoldKey] = expired[0];
            return result;
        }

        /// <summary>A copy of yours as a stake (its {card} and {grade}).</summary>
        internal static Stake StakeOf(CardCopy c) => AsStake(c);

        private static HeadlinerState HeadlinerOf(Profile profile, string id)
        {
            if (!profile.Headliners.TryGetValue(id, out var s)) profile.Headliners[id] = s = new HeadlinerState();
            return s;
        }

        // ---- Hooks and triggers (for the checks) --------------------------------------------------------

        /// <summary>The van's voice hooks (campaign/van.txt).</summary>
        public static readonly string[] VanHooks =
        {
            "van:hello", "van:footer", "van:rail", "van:buyback", "van:shelf", "van:pack", "van:sell", "van:empty",
            "van:used", "van:sell-refused", "van:held", "van:held-gone", "van:held-paid", "van:sell-refused:restored",
        };

        private static readonly string[] VoiceHooks =
        {
            "baz:stake", "baz:empty", "baz:turncap", "baz:firstturn", "baz:orvel", "baz:contraband", "baz:contraband:nyx",
            "baz:antewin", "baz:antelose", "baz:rail", "orvel:pays", "baz:combo", "baz:payoff", "baz:fizzle",
            "mc:upset", "mc:out", "mc:crowned", "mc:game", "mc:game:2", "mc:game:3", "regular:intro:penny", "regular:win:penny",
            // No Limits (section 10.3).
            "baz:restored", "baz:nolimits", "baz:stake:nolimits", "mc:brawl:outoftowner",
        };

        private static readonly string[] RoundKeys = { "round-of-16", "quarter-final", "semi-final", "final" };

        /// <summary>
        /// A hook campaign/alley-voice.txt (and van.txt) may use: regular:intro|win|lose[:&lt;table id&gt;],
        /// regular:intro:card:&lt;a handle's card&gt;, tell:&lt;headliner&gt;, mc:|stream:&lt;kind&gt;:&lt;round&gt;, Baz's, Orvel's and the van's;
        /// No Limits' pilot:intro|win|lose|restored|encore:&lt;build&gt;, outoftowner:intro|win|lose:&lt;build&gt;, baz:restored:&lt;build&gt;,
        /// mc:|stream:invitational:&lt;round&gt;.
        /// </summary>
        public static bool IsVoiceHook(string hook, AlleyData data, ICardDataProvider cards)
        {
            if (VoiceHooks.Contains(hook) || VanHooks.Contains(hook)) return true;
            foreach (var moment in new[] { "intro", "win", "lose" })
            {
                var stem = "regular:" + moment;
                if (hook == stem) return true;
                if (hook.StartsWith(stem + ":") && data.Tables.Any(t => t.Id == hook.Substring(stem.Length + 1))) return true;
            }
            if (hook.StartsWith("regular:intro:card:"))
                return data.Handles.Any(h => cards.Get(h.card)?.Name == hook.Substring(19));
            if (hook.StartsWith("tell:")) return data.FindHeadliner(hook.Substring(5)) != null;
            var p = hook.Split(':');
            if (p.Length == 3 && p[0] == "pilot" && p[1] is "intro" or "win" or "lose" or "restored" or "encore") return data.FindBuild(p[2]) != null;
            if (p.Length == 3 && p[0] == OutOfTownerEvent && p[1] is "intro" or "win" or "lose") return data.FindBuild(p[2]) != null;
            if (p.Length == 3 && p[0] == "baz" && p[1] == RestoredEvent) return data.FindBuild(p[2]) != null;
            return p.Length == 3 && (p[0] == "mc" || p[0] == "stream") && (p[1] == "brawl" || p[1] == "midnight" || p[1] == "invitational") && RoundKeys.Contains(p[2]);
        }

        /// <summary>The placeholders the alley's pools and scenes fill, beside the shop's ({sold}: the card Moth just sold off the rail).</summary>
        public static readonly string[] Placeholders = { "card", "table", "handle", "nick", "grade", "price", "left", "step", "round", SoldKey };

        /// <summary>The fill key of the card Moth sold off the rail ({sold}, for railgone's scene): set wherever the rail ticks.</summary>
        public const string SoldKey = "sold";

        /// <summary>
        /// Every story trigger the alley raises (all phases), for the data: alley, alleyvisit, open:/alleyclear: per table, the
        /// headliners', the ante's, the brackets' (brawl, midnight, invitational: the first entry; bracketwin:&lt;name&gt; per bracket),
        /// and No Limits' (nolimits, restored, outoftowner, pilot:/pilotbeat: per build, ready or not, outoftowner:&lt;id&gt; per build
        /// a bracket's field seats on another deck than its build).
        /// </summary>
        public static List<string> Triggers(AlleyData data)
        {
            var triggers = new List<string> { OpenEvent, VisitEvent, FenceEvent, "encore", "antewin", "antelose", "rail", "buyback", "railgone",
                "turncap", "firstturn", "unlisted", Brackets.BrawlKind, Brackets.MidnightKind, Brackets.InvitationalKind, "pennywin", "heldcard",
                WallEvent, RestoredEvent, OutOfTownerEvent };
            foreach (var t in data.Tables) { triggers.Add("open:" + t.Name); triggers.Add("alleyclear:" + t.Name); }
            foreach (var h in data.Headliners)
            {
                if (h.IsBuild) { triggers.Add(PilotEvent + h.Id); triggers.Add(PilotBeatEvent + h.Id); }
                else { triggers.Add("headliner:" + h.Id); triggers.Add("headlinerbeat:" + h.Id); triggers.Add(ComboBreakerEvent + h.Id); }
            }
            foreach (var b in data.Brackets)
            {
                triggers.Add("bracketwin:" + b.Name);
                // Out-of-towners: build pilots on another deck than their build (the Brawl's); the Invitational's play their builds.
                foreach (var (w, _) in b.Field.Select(AlleyBracketSpec.ParseField))
                    if (w.Length > 1 && data.FindBuild(w[0]) is Headliner h && w[1] != h.Deck
                        && !triggers.Contains(OutOfTownerEvent + ":" + w[0])) triggers.Add(OutOfTownerEvent + ":" + w[0]);
            }
            return triggers;
        }

        /// <summary>Whether a trigger is one of No Limits' (for the checks: scenes pending until NO-LIMITS-STORY's block lands).</summary>
        public static bool IsNoLimitsTrigger(string trigger) =>
            trigger is WallEvent or RestoredEvent or OutOfTownerEvent or Brackets.InvitationalKind || trigger.StartsWith(PilotEvent) || trigger.StartsWith(PilotBeatEvent)
            || trigger.StartsWith(OutOfTownerEvent + ":") || trigger == "open:No Limits" || trigger == "alleyclear:No Limits" || trigger == InvitationalWinEvent;

        /// <summary>The Invitational's title scene trigger (its bracket's name as alley-events.txt writes it).</summary>
        public const string InvitationalWinEvent = Brackets.WinEvent + ":No Limits Invitational";

        /// <summary>Whether a story.txt trigger is one of the alley's (<see cref="Triggers"/>).</summary>
        public static bool IsStoryTrigger(string trigger, AlleyData data) => Triggers(data).Contains(trigger);
    }
}
