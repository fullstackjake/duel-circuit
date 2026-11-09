using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Ygo.Engine.AI;
using Ygo.Engine.Interactive;
using Ygo.Engine.Presentation;

namespace Ygo.Engine.DeckBuilding
{
    /// <summary>
    /// One-box card search. A query is a list of terms, all of which must match:
    /// <list type="bullet">
    /// <item>words: in the name or text ("blue eyes"); "quoted phrases"; -word excludes (any term can be excluded)</item>
    /// <item>numbers: lv8, level>=4, rank4, link2, atk>2500, def&lt;=1000, scale5</item>
    /// <item>kinds: monster spell trap normal effect tuner fusion ritual synchro xyz link pendulum flip extra
    /// quickplay continuous field equip counter</item>
    /// <item>attributes (dark, light...) and Types (dragon, spellcaster, wingedbeast...), which also match names</item>
    /// <item>roles from the bots' card classifier: handtrap searcher draw removal negate wipe revive specialsummon
    /// destroymonster destroyst</item>
    /// <item>effect tags read from the text: negateactivation negateeffect banish discard burn gainlp</item>
    /// <item>what a card does: does:search, does:banish... (<see cref="CardTags.Genres"/>)</item>
    /// <item>archetypes: @"burning abyss", @burningabyss (needs <see cref="SetArchetypes"/>)</item>
    /// <item>where a word is: name:word or $word (the name), text:word (the text), pend:word (the Pendulum Effect);
    /// quoted values work (text:"destroy all")</item>
    /// <item>a or b (also OR and |): either; binds tighter than the AND between terms</item>
    /// </list>
    /// Name matches rank above text matches, and names match with or without spaces and hyphens ("blueeyes"). When no
    /// card's name has all the plain words (not counting Attribute and Type words, which also filter), names one or two
    /// typos away follow the results (<see cref="LastUsedCloseNames"/>, <see cref="LastCloseNameStart"/>). Alternate
    /// artworks and tokens are hidden. Ban list filters (limit:N, legal and their negations) are the editor's: see
    /// <see cref="TakeBanFilters(string, out BanTerms)"/>.
    /// </summary>
    public sealed class CardSearch
    {
        /// <summary>Where plain words and phrases look (the editor's three-way switch); prefixed terms ignore it.</summary>
        public enum SearchMode
        {
            /// <summary>The name or the text (the default).</summary>
            NameAndText,
            /// <summary>The name only.</summary>
            NameOnly,
            /// <summary>The text only (both effects of a Pendulum monster).</summary>
            TextOnly,
        }

        private const uint TypeNormal = 0x10, TypeEffect = 0x20, TypeTuner = 0x1000, TypeQuickPlay = 0x10000,
            TypeContinuous = 0x20000, TypeEquip = 0x40000, TypeField = 0x80000, TypeCounter = 0x100000, TypeFlip = 0x200000;

        private static readonly Dictionary<string, uint> Kinds = new Dictionary<string, uint>
        {
            ["monster"] = CardType.Monster, ["spell"] = CardType.Spell, ["trap"] = CardType.Trap,
            ["normal"] = TypeNormal, ["effect"] = TypeEffect, ["tuner"] = TypeTuner, ["fusion"] = CardType.Fusion,
            ["ritual"] = CardType.Ritual, ["synchro"] = CardType.Synchro, ["xyz"] = CardType.Xyz, ["link"] = CardType.Link,
            ["pendulum"] = CardType.Pendulum, ["flip"] = TypeFlip, ["extra"] = CardType.ExtraDeckTypes, ["quickplay"] = TypeQuickPlay, ["quick-play"] = TypeQuickPlay,
            ["continuous"] = TypeContinuous, ["field"] = TypeField, ["equip"] = TypeEquip, ["counter"] = TypeCounter,
        };

        private static readonly Dictionary<string, Trait> Roles = new Dictionary<string, Trait>
        {
            ["handtrap"] = Trait.HandTrap, ["searcher"] = Trait.Search, ["search"] = Trait.Search, ["draw"] = Trait.Draw,
            ["removal"] = Trait.MonsterRemoval | Trait.SpellTrapRemoval, ["negate"] = Trait.Negate,
            ["wipe"] = Trait.BoardWipe, ["boardwipe"] = Trait.BoardWipe, ["revive"] = Trait.Revive,
            ["specialsummon"] = Trait.SpecialSummon, ["destroymonster"] = Trait.MonsterRemoval, ["destroyst"] = Trait.SpellTrapRemoval,
        };

        /// <summary>Effect tags read straight from the card text (more precise than the roles above).</summary>
        private static readonly Dictionary<string, Func<string, bool>> TextTags = new Dictionary<string, Func<string, bool>>
        {
            ["negateactivation"] = t => t.Contains("negate") && t.Contains("activation"),
            ["negateeffect"] = t => t.Contains("negate its effect") || t.Contains("negate that effect") || t.Contains("negate the effect")
                                    || t.Contains("effects are negated") || t.Contains("negate the effects"),
            ["banish"] = CardTags.TextBanishes,
            ["discard"] = CardTags.TextDiscards,
            ["burn"] = CardTags.TextBurns,
            ["gainlp"] = CardTags.TextGainsLp,
        };

        private static readonly Dictionary<string, ulong> Races = DuelText.Races.ToDictionary(r => Squash(r.name), r => r.bit);
        private static readonly Dictionary<string, uint> Attributes = DuelText.Attributes.ToDictionary(a => a.name.ToLowerInvariant(), a => a.bit);
        private static readonly Regex Comparison = new Regex(@"^(atk|def|lv|level|rank|link|scale)(>=|<=|>|<|=|:)?(\d+)$");
        private static readonly Regex Tokens = new Regex("-?(?:[A-Za-z]+:|[$@])?\"[^\"]*\"|\\S+");
        private static readonly Regex BanToken = new Regex(@"^(-?)(?:limit:(\d+)|legal)$", RegexOptions.IgnoreCase);

        /// <summary>One searchable card with its text worked out once (lowercase name, squashed name, text, both halves of a Pendulum's text).</summary>
        private sealed class Entry
        {
            public readonly CardInfo Card;
            public readonly int Index;
            public readonly string Name, Squashed, Text, MonsterText, PendulumText;
            /// <summary>Where each of the name's words starts in <see cref="Squashed"/>.</summary>
            public readonly bool[] WordStarts;
            private string[]? _nameWords;

            public Entry(CardInfo card, int index)
            {
                Card = card;
                Index = index;
                Name = card.Name.ToLowerInvariant();
                Squashed = SquashName(card.Name);
                WordStarts = new bool[Squashed.Length];
                bool inWord = false;
                int at = 0;
                foreach (var ch in card.Name)
                {
                    if (!char.IsLetterOrDigit(ch))
                    {
                        inWord = false;
                        continue;
                    }
                    if (!inWord && at < WordStarts.Length) WordStarts[at] = true;
                    inWord = true;
                    at++;
                }
                Text = card.Text.ToLowerInvariant();
                var (pendulum, monster) = SplitPendulum(Text);
                PendulumText = pendulum;
                MonsterText = monster;
            }

            /// <summary>The name's words, plus each run of two and three neighbouring words joined ("blueeyes"), for typos.</summary>
            public string[] NameWords => _nameWords ??= Words(Name);

            private static string[] Words(string name)
            {
                var words = Regex.Split(name, @"[^\p{L}\p{Nd}]+").Where(w => w.Length > 0).ToList();
                var all = new List<string>(words);
                for (int i = 0; i + 1 < words.Count; i++)
                {
                    all.Add(words[i] + words[i + 1]);
                    if (i + 2 < words.Count) all.Add(words[i] + words[i + 1] + words[i + 2]);
                }
                return all.Distinct().ToArray();
            }
        }

        /// <summary>A parsed term: its test, and the plain word it stands for (if any) for ranking and close names.</summary>
        private sealed class Term
        {
            public Func<Entry, bool> Match = _ => true;
            /// <summary>Counts toward name ranking (plain words, phrases, attribute and Type words, name: words).</summary>
            public string? RankWord;
            /// <summary>A plain word (not a phrase, a prefixed term or a keyword that filters): close names can stand in for it.</summary>
            public string? CloseWord;
        }

        private readonly List<CardInfo> _cards;
        private readonly List<Entry> _entries;

        private Archetypes _archetypes = Archetypes.Empty;
        private readonly List<(ushort code, string name, string squashed)> _archetypeNames = new List<(ushort, string, string)>();
        private readonly Dictionary<int, List<ushort>> _archetypesByFamily = new Dictionary<int, List<ushort>>();
        private readonly Dictionary<ushort, List<int>> _members = new Dictionary<ushort, List<int>>();

        private readonly List<string> _unknownArchetypes = new List<string>();
        private readonly List<string> _unknownGenres = new List<string>();

        public CardSearch(ICardDataProvider cards)
        {
            // Hide alternate artworks (same card, different picture) and tokens.
            _cards = cards.All.Where(c => c.Code != 0 && (c.Alias == 0 || cards.Get(c.Alias) == null)
                                          && (c.Type & CardType.Token) == 0 && c.Name.Length > 0).ToList();
            _entries = _cards.Select((c, i) => new Entry(c, i)).ToList();
        }

        public int CardCount => _cards.Count;

        /// <summary>
        /// True when no card in the whole search set has all of the last search's plain words in its name, and close names
        /// (typos, missing spaces) were added after the results: "No card named that; showing close names."
        /// </summary>
        public bool LastUsedCloseNames { get; private set; }

        /// <summary>
        /// Where the last search's close names start in its results (they're always last), or -1 when it added none. The
        /// editor sorts the cards before it and the close names apart, so its sorts never mix them.
        /// </summary>
        public int LastCloseNameStart { get; private set; } = -1;

        /// <summary>The last search's <c>@terms</c> that named no archetype, as typed (without the @): "No archetype called ...".</summary>
        public IReadOnlyList<string> LastUnknownArchetypes => _unknownArchetypes;

        /// <summary>The last search's <c>does:</c> tokens that aren't genres, as typed.</summary>
        public IReadOnlyList<string> LastUnknownGenres => _unknownGenres;

        /// <summary>
        /// The archetype names for <c>@name</c> terms, suggestions and <see cref="ArchetypesOf"/> (the editor's
        /// <c>SetAnalysisData</c> list). Until it's called, every <c>@term</c> names nothing.
        /// </summary>
        public void SetArchetypes(Archetypes archetypes)
        {
            _archetypes = archetypes ?? Archetypes.Empty;
            _archetypeNames.Clear();
            _archetypesByFamily.Clear();
            _members.Clear();
            foreach (var (code, name) in _archetypes.All.OrderBy(a => a.code))
            {
                _archetypeNames.Add((code, name, SquashName(name)));
                var family = code & 0xfff;
                if (!_archetypesByFamily.TryGetValue(family, out var list)) _archetypesByFamily[family] = list = new List<ushort>();
                list.Add(code);
            }
            foreach (var e in _entries)
                foreach (var code in ArchetypeCodes(e.Card))
                {
                    if (!_members.TryGetValue(code, out var members)) _members[code] = members = new List<int>();
                    members.Add(e.Index);
                }
        }

        /// <summary>The named archetypes a card belongs to (sub-archetypes included), as its detail's chips: code and name.</summary>
        public List<(ushort code, string name)> ArchetypesOf(CardInfo card) =>
            ArchetypeCodes(card).OrderBy(c => c & 0xfff).ThenBy(c => c).Select(c => (c, _archetypes.Name(c))).ToList();

        private IEnumerable<ushort> ArchetypeCodes(CardInfo card)
        {
            var found = new List<ushort>();
            foreach (var s in card.Setcodes)
            {
                if (s == 0 || !_archetypesByFamily.TryGetValue(s & 0xfff, out var codes)) continue;
                foreach (var a in codes)
                    if ((s & a) == a && !found.Contains(a)) found.Add(a);
            }
            return found;
        }

        /// <summary>
        /// Archetypes for the editor's suggestion box: names that start with what's typed (spaces, hyphens and case
        /// ignored), then names that contain it, most members first; only archetypes with 2+ cards in the search set.
        /// The count is the members in the search set. Pick one with <see cref="ArchetypeTerm"/>.
        /// </summary>
        public List<(ushort code, string name, int count)> ArchetypeSuggestions(string typed, int max = 8)
        {
            var key = SquashName(typed ?? "");
            var result = new List<(ushort, string, int)>();
            if (key.Length == 0) return result;
            var named = _archetypeNames
                .Select(a => (a.code, a.name, a.squashed, count: _members.TryGetValue(a.code, out var m) ? m.Count : 0))
                .Where(a => a.count >= 2)
                .ToList();
            var starts = named.Where(a => a.squashed.StartsWith(key, StringComparison.Ordinal));
            var contains = named.Where(a => !a.squashed.StartsWith(key, StringComparison.Ordinal) && a.squashed.Contains(key));
            var seen = new HashSet<string>();
            foreach (var a in starts.OrderByDescending(a => a.count).ThenBy(a => a.name)
                         .Concat(contains.OrderByDescending(a => a.count).ThenBy(a => a.name)))
            {
                if (result.Count >= max) break;
                if (seen.Add(a.squashed)) result.Add((a.code, a.name, a.count));
            }
            return result;
        }

        /// <summary>The <c>@term</c> for an archetype name ("Burning Abyss" -> "@burningabyss").</summary>
        public static string ArchetypeTerm(string name) => "@" + SquashName(name);

        public List<CardInfo> Find(string query, int limit = 500) => Find(query, SearchMode.NameAndText, limit);

        /// <summary>
        /// The cards matching every term, best first; <paramref name="mode"/> says where plain words and phrases look
        /// (kinds, roles and text tags stay filters; Attribute and Type words become plain words outside Name + text).
        /// </summary>
        public List<CardInfo> Find(string query, SearchMode mode, int limit = 500)
        {
            ClearLast();
            return Run(ParseQuery(query, mode), mode, limit, closeNames: true);
        }

        /// <summary>
        /// The editor's search: what's typed in the box, and its chips, pickers and More filters as
        /// <paramref name="filters"/>, all ANDed. In the box, <paramref name="mode"/> says where plain words look, and
        /// outside Name + text every bare keyword (kind, Attribute, Type, role, text tag) is a plain word too ("fusion gate"
        /// in Name only is Fusion Gate); prefixed terms (does:, @, name:, text:, pend:, attr:, type:) and comparisons
        /// (lv4, atk&gt;2000) keep their meaning. <paramref name="filters"/> always reads keywords as filters. An OR never
        /// joins the two: one at either end of either part is dropped. <paramref name="closeNames"/> false: never add close
        /// names (the Card Trader's search).
        /// </summary>
        public List<CardInfo> Find(string box, SearchMode mode, string filters, int limit = 500, bool closeNames = true)
        {
            ClearLast();
            var groups = ParseQuery(box, mode, plainKeywords: mode != SearchMode.NameAndText);
            groups.AddRange(ParseQuery(filters, SearchMode.NameAndText));
            return Run(groups, mode, limit, closeNames);
        }

        private void ClearLast()
        {
            _unknownArchetypes.Clear();
            _unknownGenres.Clear();
            LastUsedCloseNames = false;
            LastCloseNameStart = -1;
        }

        private List<CardInfo> Run(List<List<Term>> groups, SearchMode mode, int limit, bool closeNames)
        {
            // Ranking and close names read single terms only: the words inside an OR say nothing about the name.
            var single = groups.Where(g => g.Count == 1).Select(g => g[0]).ToList();
            var words = single.Where(t => t.RankWord != null).Select(t => t.RankWord!).ToList();
            var closeTerms = single.Where(t => t.CloseWord != null).ToList();
            var others = groups.Where(g => g.Count != 1 || g[0].CloseWord == null)
                .Select(g => g.Count == 1 ? g[0].Match : AnyOf(g)).ToArray();
            var plain = closeTerms.Select(t => t.Match).ToArray();

            var candidates = new List<Entry>();
            var matches = new List<Entry>();
            foreach (var e in _entries)
            {
                if (!All(others, e)) continue;
                candidates.Add(e);
                if (All(plain, e)) matches.Add(e);
            }

            var name = string.Join(" ", words);
            var squashedName = SquashName(name);
            var squashedWords = words.Select(SquashName).ToList();
            // With words, best name matches first; with only filters (e.g. "handtrap"), plain A-Z is easiest to browse.
            var ordered = matches.OrderBy(e => Rank(e, name, squashedName, words, squashedWords)).ThenBy(e => KindOrder(e.Card.Type));
            ordered = words.Count > 0 ? ordered.ThenByDescending(e => e.Card.Level).ThenBy(e => e.Card.Name) : ordered.ThenBy(e => e.Card.Name);
            var result = ordered.Take(limit).Select(e => e.Card).ToList();

            // Close names, only when no card in the whole set has all the plain words in its name (whatever the other terms
            // say: "raigeki monster" finds no monster named Raigeki, but Raigeki is a name, so no lookalikes). The close
            // names themselves still obey the other terms ("bleu eyes lv8").
            if (closeNames && closeTerms.Count > 0 && mode != SearchMode.TextOnly && result.Count < limit)
            {
                var closeWords = closeTerms.Select(t => t.CloseWord!).ToList();
                var closeSquashed = closeWords.Select(SquashName).ToList();
                bool NamedAll(Entry e)
                {
                    for (int i = 0; i < closeWords.Count; i++)
                        if (!NameHas(e, closeWords[i], closeSquashed[i])) return false;
                    return true;
                }
                bool named = matches.Any(NamedAll) || _entries.Any(NamedAll);
                if (!named)
                {
                    var inResults = new HashSet<int>(matches.Select(e => e.Index));
                    var parts = closeWords.Select(w => Regex.Split(w, @"[^\p{L}\p{Nd}]+").Where(p => p.Length > 0).ToArray()).ToList();
                    var close = new List<(Entry e, int distance)>();
                    foreach (var e in candidates)
                    {
                        if (inResults.Contains(e.Index)) continue;
                        int total = 0;
                        for (int i = 0; i < closeWords.Count && total >= 0; i++)
                        {
                            var d = CloseDistance(e, closeWords[i], closeSquashed[i], parts[i]);
                            total = d < 0 ? -1 : total + d;
                        }
                        if (total >= 0) close.Add((e, total));
                    }
                    if (close.Count > 0)
                    {
                        LastUsedCloseNames = true;
                        LastCloseNameStart = result.Count;
                        // Fewest edits first, then names with more of the other ranked words (the "dark" of "dark magican",
                        // which filters rather than calling up close names), then the shortest names (the fewest words the
                        // typed ones leave out).
                        var alsoNamed = words.Where(w => !closeWords.Contains(w)).Select(w => (w, sq: SquashName(w))).ToList();
                        result.AddRange(close.OrderBy(x => x.distance).ThenByDescending(x => alsoNamed.Count(a => NameHas(x.e, a.w, a.sq)))
                            .ThenBy(x => x.e.Card.Name.Length).ThenBy(x => x.e.Card.Name)
                            .Take(limit - result.Count).Select(x => x.e.Card));
                    }
                }
            }
            return result;
        }

        private static bool All(Func<Entry, bool>[] tests, Entry e)
        {
            foreach (var t in tests)
                if (!t(e)) return false;
            return true;
        }

        private static Func<Entry, bool> AnyOf(List<Term> group)
        {
            var tests = group.Select(t => t.Match).ToArray();
            return e =>
            {
                foreach (var t in tests)
                    if (t(e)) return true;
                return false;
            };
        }

        /// <summary>
        /// The query's AND groups, each a list of terms joined by OR. With <paramref name="plainKeywords"/> (the editor's box
        /// in Name only or Text only), bare keywords are plain words.
        /// </summary>
        private List<List<Term>> ParseQuery(string query, SearchMode mode, bool plainKeywords = false)
        {
            var groups = new List<List<Term>>();
            bool or = false;
            foreach (var raw in Tokenize(query))
            {
                if (IsOr(raw))
                {
                    // A leading, trailing or doubled OR joins nothing and is ignored.
                    or = groups.Count > 0;
                    continue;
                }
                var term = ParseTerm(raw, mode, plainKeywords);
                if (term == null) continue;
                if (or) groups[groups.Count - 1].Add(term);
                else groups.Add(new List<Term> { term });
                or = false;
            }
            return groups;
        }

        private static bool IsOr(string raw) => raw.Equals("or", StringComparison.OrdinalIgnoreCase) || raw.Trim('|').Length == 0;

        /// <summary>One term (null: nothing to test, e.g. a bare "name:" while typing, or limit:N, which the editor applies).</summary>
        private Term? ParseTerm(string raw, SearchMode mode, bool plainKeywords)
        {
            bool negate = raw.StartsWith("-") && raw.Length > 1;
            var term = new Term();
            var filter = Parse(negate ? raw.Substring(1) : raw, mode, term, plainKeywords);
            if (filter == null) return null;
            if (negate)
            {
                term.Match = e => !filter(e);
                term.RankWord = term.CloseWord = null;
            }
            else term.Match = filter;
            return term;
        }

        /// <summary>A bare word that is a filter in Name + text: a kind, an Attribute, a Type, a role or a text tag.</summary>
        private static bool IsKeyword(string t) =>
            Kinds.ContainsKey(t) || Attributes.ContainsKey(t) || Races.ContainsKey(Squash(t)) || Roles.ContainsKey(t) || TextTags.ContainsKey(t);

        private Func<Entry, bool>? Parse(string body, SearchMode mode, Term term, bool plainKeywords)
        {
            var t = body.ToLowerInvariant();

            // Prefixed terms: where to look, archetypes, genres. They ignore the mode.
            if (t.Length > 1 && (t[0] == '$' || t[0] == '@'))
            {
                var value = Unquote(body.Substring(1));
                if (value.Length == 0) return null;
                return t[0] == '$' ? NameTerm(value.ToLowerInvariant(), term) : ArchetypeFilter(value);
            }
            int colon = t.IndexOf(':');
            if (colon > 0 && !t.StartsWith("\""))
            {
                var value = Unquote(t.Substring(colon + 1));
                switch (t.Substring(0, colon))
                {
                    case "name": return value.Length == 0 ? null : NameTerm(value, term);
                    case "text": return value.Length == 0 ? null : e => e.Text.Contains(value);
                    case "pend": return value.Length == 0 ? null : e => e.PendulumText.Contains(value);
                    case "does": return value.Length == 0 ? null : GenreFilter(Unquote(body.Substring(colon + 1)));
                    case "limit": return null; // the editor's ban list filter (TakeBanFilters), -limit:N too
                }
            }

            if (t.Length > 1 && t.StartsWith("\"") && t.EndsWith("\""))
            {
                var phrase = t.Trim('"');
                term.RankWord = mode == SearchMode.TextOnly ? null : phrase;
                return WordFilter(phrase, mode);
            }
            var m = Comparison.Match(t);
            if (m.Success)
            {
                var field = m.Groups[1].Value;
                var op = m.Groups[2].Value;
                var n = int.Parse(m.Groups[3].Value);
                return e => Compare(Field(e.Card, field), op, n) && ((e.Card.Type & CardType.Monster) != 0 || field == "scale");
            }
            // The editor's box in Name only or Text only: a bare keyword is a word to look for ("fusion gate" is Fusion Gate).
            if (plainKeywords && mode != SearchMode.NameAndText && IsKeyword(t)) return PlainWord(t, mode, term);
            if (Kinds.TryGetValue(t, out var kind)) return e => (e.Card.Type & kind) != 0;
            // Strict forms for pickers: attr:dark and type:spellcaster match only the Attribute / Type, not names.
            if (t.StartsWith("attr:") && Attributes.TryGetValue(t.Substring(5), out var strictAttribute))
                return e => (e.Card.Attribute & strictAttribute) != 0 && (e.Card.Type & CardType.Monster) != 0;
            if (t.StartsWith("type:") && Races.TryGetValue(Squash(t.Substring(5)), out var strictRace))
                return e => (e.Card.Race & strictRace) != 0 && (e.Card.Type & CardType.Monster) != 0;
            // Attributes and Types are also common name words ("Dark Magician", "Blue-Eyes White Dragon"),
            // so they match either the attribute/Type or the name, and still count toward name ranking.
            // Name only and Text only make them plain words.
            if (Attributes.TryGetValue(t, out var attribute))
            {
                if (mode != SearchMode.NameAndText) return PlainWord(t, mode, term);
                // Ranking yes, close names no: the word is a filter too, so "earth machine" isn't a name to find.
                term.RankWord = t;
                var sq = SquashName(t);
                return e => (e.Card.Attribute & attribute) != 0 && (e.Card.Type & CardType.Monster) != 0 || NameHas(e, t, sq);
            }
            if (Races.TryGetValue(Squash(t), out var race))
            {
                if (mode != SearchMode.NameAndText) return PlainWord(t, mode, term);
                term.RankWord = t;
                var sq = SquashName(t);
                return e => (e.Card.Race & race) != 0 && (e.Card.Type & CardType.Monster) != 0 || NameHas(e, t, sq);
            }
            if (Roles.TryGetValue(t, out var role)) return e => (CardTraits.Of(e.Card) & role) != 0;
            if (TextTags.TryGetValue(t, out var tag)) return e => tag(e.Text);

            return PlainWord(t, mode, term);
        }

        private static Func<Entry, bool> PlainWord(string t, SearchMode mode, Term term)
        {
            if (mode != SearchMode.TextOnly) term.RankWord = term.CloseWord = t.Trim('"');
            return WordFilter(t, mode);
        }

        private static Func<Entry, bool> WordFilter(string w, SearchMode mode)
        {
            var sq = SquashName(w);
            switch (mode)
            {
                case SearchMode.NameOnly: return e => NameHas(e, w, sq);
                case SearchMode.TextOnly: return e => e.Text.Contains(w);
                default: return e => NameHas(e, w, sq) || e.Text.Contains(w);
            }
        }

        private static Func<Entry, bool> NameTerm(string value, Term term)
        {
            term.RankWord = value;
            var sq = SquashName(value);
            return e => NameHas(e, value, sq);
        }

        /// <summary>
        /// The name has the word, as written or with spaces, hyphens and punctuation squashed out on both sides
        /// ("blueeyes" in "Blue-Eyes"). A squashed match covers whole name words, so "hero" doesn't match "Archer of the"
        /// and "neos" doesn't match "Neo-Spacian" (those are left to close names).
        /// </summary>
        private static bool NameHas(Entry e, string word, string squashed) =>
            e.Name.Contains(word) || squashed.Length > 0 && SquashedHas(e, squashed, wholeWords: true);

        /// <summary>The squashed name has this where a name word starts (and, with <paramref name="wholeWords"/>, ends where one ends).</summary>
        private static bool SquashedHas(Entry e, string squashed, bool wholeWords = false)
        {
            for (int at = e.Squashed.IndexOf(squashed, StringComparison.Ordinal); at >= 0; at = e.Squashed.IndexOf(squashed, at + 1, StringComparison.Ordinal))
            {
                if (!e.WordStarts[at]) continue;
                int end = at + squashed.Length;
                if (!wholeWords || end == e.Squashed.Length || e.WordStarts[end]) return true;
            }
            return false;
        }

        private Func<Entry, bool> ArchetypeFilter(string typed)
        {
            var key = SquashName(typed);
            if (key.Length == 0) return _ => false;
            var codes = _archetypeNames.Where(a => a.squashed == key).Select(a => a.code).ToList();
            if (codes.Count == 0) codes = _archetypeNames.Where(a => a.squashed.StartsWith(key, StringComparison.Ordinal)).Select(a => a.code).ToList();
            if (codes.Count == 0)
            {
                _unknownArchetypes.Add(typed);
                return _ => false;
            }
            var members = new HashSet<int>(codes.SelectMany(c => _members.TryGetValue(c, out var list) ? list : Enumerable.Empty<int>()));
            return e => members.Contains(e.Index);
        }

        private Func<Entry, bool> GenreFilter(string typed)
        {
            var genre = CardTags.GenreOf(typed);
            if (genre == null)
            {
                _unknownGenres.Add(typed);
                return _ => false;
            }
            return e => genre.Matches(e.Card, e.Text);
        }

        private static string Unquote(string v)
        {
            if (v.Length >= 2 && v.StartsWith("\"") && v.EndsWith("\"")) return v.Substring(1, v.Length - 2).Trim();
            return v.TrimStart('"').Trim();
        }

        /// <summary>
        /// How far a card's name is from a plain word: 0 when the name has it, else the edits needed when each part of the
        /// word ("bleu-eyes": "bleu", "eyes") starts a name word or is within 1 edit (4-6 letters) or 2 (7+) of one
        /// (parts of 3 letters or fewer must match exactly), else -1.
        /// </summary>
        private static int CloseDistance(Entry e, string word, string squashed, string[] parts)
        {
            if (NameHas(e, word, squashed)) return 0;
            int best = -1;
            int total = 0;
            foreach (var part in parts)
            {
                var d = PartDistance(e, part);
                if (d < 0) { total = -1; break; }
                total += d;
            }
            if (parts.Length > 0 && total >= 0) best = total;
            if (parts.Length > 1)
            {
                var whole = PartDistance(e, squashed);
                if (whole >= 0 && (best < 0 || whole < best)) best = whole;
            }
            return best;
        }

        private static int PartDistance(Entry e, string part)
        {
            // A part that starts a name word ("d" in d-hero, Destiny HERO) or a longer one anywhere in it needs no edits;
            // short ones must match exactly.
            if (SquashedHas(e, part) || part.Length > 3 && e.Name.Contains(part)) return 0;
            int budget = part.Length <= 3 ? 0 : part.Length <= 6 ? 1 : 2;
            if (budget == 0) return -1;
            int best = budget + 1;
            foreach (var w in e.NameWords)
            {
                var d = Distance(part, w, budget);
                if (d < best) best = d;
                if (best == 1) break;
            }
            return best <= budget ? best : -1;
        }

        /// <summary>Edits between two words (insert, delete, change, or swap two neighbouring letters); max + 1 once it's past max.</summary>
        private static int Distance(string a, string b, int max)
        {
            if (Math.Abs(a.Length - b.Length) > max) return max + 1;
            if (b.Length > 64) return max + 1;
            Span<int> prev2 = stackalloc int[b.Length + 1];
            Span<int> prev = stackalloc int[b.Length + 1];
            Span<int> row = stackalloc int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                row[0] = i;
                int rowMin = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int v = Math.Min(Math.Min(prev[j] + 1, row[j - 1] + 1), prev[j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, prev2[j - 2] + 1);
                    row[j] = v;
                    if (v < rowMin) rowMin = v;
                }
                if (rowMin > max) return max + 1;
                var swap = prev2;
                prev2 = prev;
                prev = row;
                row = swap;
            }
            return prev[b.Length];
        }

        private static int Field(CardInfo c, string field)
        {
            switch (field)
            {
                case "atk": return c.Attack;
                case "def": return (c.Type & CardType.Link) != 0 ? -1 : c.Defense;
                case "lv":
                case "level": return (c.Type & (CardType.Xyz | CardType.Link)) != 0 ? -1 : (int)c.Level;
                case "rank": return (c.Type & CardType.Xyz) != 0 ? (int)c.Level : -1;
                case "link": return (c.Type & CardType.Link) != 0 ? (int)c.Level : -1;
                case "scale": return (c.Type & CardType.Pendulum) != 0 ? (int)c.LScale : -1;
                default: return -1;
            }
        }

        private static bool Compare(int value, string op, int n)
        {
            if (value < 0) return false;
            switch (op)
            {
                case ">": return value > n;
                case "<": return value < n;
                case ">=": return value >= n;
                case "<=": return value <= n;
                default: return value == n;
            }
        }

        /// <summary>
        /// 0 = exact name, 1 = name starts with, 2 = name contains the words, 3 = matched only in the text. Squashed names
        /// count too, so "blue eyes white" starts "Blue-Eyes White Dragon".
        /// </summary>
        private static int Rank(Entry e, string name, string squashedName, List<string> words, List<string> squashedWords)
        {
            if (words.Count == 0) return 0;
            var n = e.Name;
            if (n == name) return 0;
            if (n.StartsWith(name, StringComparison.Ordinal)) return 1;
            if (squashedName.Length > 0)
            {
                if (e.Squashed == squashedName) return 0;
                if (e.Squashed.StartsWith(squashedName, StringComparison.Ordinal)) return 1;
            }
            for (int i = 0; i < words.Count; i++)
                if (!NameHas(e, words[i], squashedWords[i])) return 3;
            return 2;
        }

        /// <summary>
        /// Cards related to <paramref name="card"/>: the same archetype (a shared set code), cards that name it in their
        /// text ("Dark Magician"), and cards it names. Itself excluded; archetype members first.
        /// </summary>
        public List<CardInfo> Related(CardInfo card, int limit = 200)
        {
            var families = new HashSet<int>(card.Setcodes.Where(c => c != 0).Select(c => c & 0xfff));
            var quoted = $"\"{card.Name.ToLowerInvariant()}\"";
            var names = new HashSet<string>(Regex.Matches(card.Text, "\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value.ToLowerInvariant()));
            int Score(Entry e)
            {
                var c = e.Card;
                if (c.Code == card.Code || c.Name == card.Name) return 0;
                int score = 0;
                if (c.Setcodes.Any(s => s != 0 && families.Contains(s & 0xfff))) score += 2;
                if (e.Text.Contains(quoted)) score += 1;
                if (names.Contains(e.Name)) score += 1;
                return score;
            }
            return _entries.Select(e => (c: e.Card, score: Score(e))).Where(x => x.score > 0)
                .OrderByDescending(x => x.score).ThenBy(x => KindOrder(x.c.Type)).ThenBy(x => x.c.Name)
                .Take(limit).Select(x => x.c).ToList();
        }

        /// <summary>Monsters (Main Deck, then Extra Deck), then Spells, then Traps.</summary>
        public static int KindOrder(uint type)
        {
            if ((type & CardType.Monster) != 0) return (type & CardType.ExtraDeckTypes) != 0 ? 1 : 0;
            return (type & CardType.Spell) != 0 ? 2 : 3;
        }

        /// <summary>
        /// A Pendulum card's text in two: the Pendulum Effect (between "[ Pendulum Effect ]" and the "----" line) and the
        /// rest (the monster's effect or flavor text, without its "[ Monster Effect ]" heading). Cards without that layout
        /// have no Pendulum Effect: ("", text).
        /// </summary>
        public static (string pendulum, string monster) SplitPendulum(string text)
        {
            const string Header = "[ Pendulum Effect ]";
            int h = text.IndexOf(Header, StringComparison.OrdinalIgnoreCase);
            if (h < 0) return ("", text);
            int start = h + Header.Length;
            int dashes = text.IndexOf("----", start, StringComparison.Ordinal);
            if (dashes < 0) return ("", text);
            var pendulum = text.Substring(start, dashes - start).Trim();
            int lineEnd = text.IndexOf('\n', dashes);
            var rest = (lineEnd < 0 ? "" : text.Substring(lineEnd + 1)).TrimStart();
            // Drop the monster half's heading ("[ Monster Effect ]", "[ Flavor Text ]").
            if (rest.StartsWith("["))
            {
                int close = rest.IndexOf(']');
                int next = rest.IndexOf('\n');
                if (close >= 0 && (next < 0 || close < next)) rest = next < 0 ? "" : rest.Substring(next + 1);
            }
            var before = text.Substring(0, h).Trim();
            return (pendulum, before.Length > 0 ? (before + "\n" + rest).Trim() : rest.Trim());
        }

        /// <summary>The ban list terms <see cref="TakeBanFilters(string, out BanTerms)"/> took out of a query, for the editor to apply.</summary>
        public sealed class BanTerms
        {
            /// <summary><c>limit:N</c> (the last one wins), or null. Only 0 to 3 name a limit.</summary>
            public int? Limit;
            /// <summary><c>legal</c>: allowed under the picked list, and already out in its format.</summary>
            public bool Legal;
            /// <summary><c>-limit:N</c>: cards at these limits are left out (each once, as typed).</summary>
            public readonly List<int> NotLimits = new List<int>();
            /// <summary><c>-legal</c>: the cards <c>legal</c> would keep are left out.</summary>
            public bool NotLegal;

            /// <summary>Any of them filters something (a limit outside 0-3 filters nothing).</summary>
            public bool Any => Limit is >= 0 and <= 3 || Legal || NotLimits.Any(n => n >= 0 && n <= 3) || NotLegal;
        }

        /// <summary>
        /// Takes the ban list terms (<c>limit:0</c> to <c>limit:3</c>, <c>legal</c>) out of a query for the editor, which
        /// applies them itself, and returns the rest. The last limit: wins; null when there's none. Negated ones
        /// (<c>-limit:N</c>, <c>-legal</c>) stay in the rest: see <see cref="TakeBanFilters(string, out BanTerms)"/>.
        /// </summary>
        public static string TakeBanFilters(string query, out int? limit, out bool legal)
        {
            var rest = TakeBanFilters(query, out var terms, negations: false);
            limit = terms.Limit;
            legal = terms.Legal;
            return rest;
        }

        /// <summary>
        /// Takes every ban list term out of a query for the editor, which applies them itself: <c>limit:N</c>, <c>legal</c>,
        /// and their negations <c>-limit:N</c> and <c>-legal</c> (which leave those cards out). Quoted forms stay. Returns the rest.
        /// </summary>
        public static string TakeBanFilters(string query, out BanTerms terms) => TakeBanFilters(query, out terms, negations: true);

        private static string TakeBanFilters(string query, out BanTerms terms, bool negations)
        {
            terms = new BanTerms();
            var rest = new List<string>();
            foreach (var token in Tokenize(query))
            {
                var m = BanToken.Match(token);
                bool negated = m.Success && m.Groups[1].Length > 0;
                if (!m.Success || negated && !negations)
                {
                    rest.Add(token);
                    continue;
                }
                bool isLimit = m.Groups[2].Success;
                int n = 0;
                if (isLimit && !int.TryParse(m.Groups[2].Value, out n)) continue; // (too many digits: no limit)
                if (negated)
                {
                    if (!isLimit) terms.NotLegal = true;
                    else if (!terms.NotLimits.Contains(n)) terms.NotLimits.Add(n);
                }
                else if (!isLimit) terms.Legal = true;
                else terms.Limit = n;
            }
            return string.Join(" ", rest);
        }

        private static IEnumerable<string> Tokenize(string query)
        {
            foreach (Match m in Tokens.Matches(query ?? ""))
            {
                var v = m.Value;
                if (v.Trim().Length == 0) continue;
                // "dragon|spellcaster": | is an OR even without spaces (not inside quotes).
                if (v.IndexOf('|') < 0 || v.IndexOf('"') >= 0 || v.Trim('|').Length == 0)
                {
                    yield return v;
                    continue;
                }
                var parts = v.Split('|');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0) yield return "|";
                    if (parts[i].Length > 0) yield return parts[i];
                }
            }
        }

        private static string Squash(string s) => s.ToLowerInvariant().Replace(" ", "").Replace("-", "");

        /// <summary>A name for loose matching: lowercase letters and digits only ("Blue-Eyes White" -> "blueeyeswhite").</summary>
        public static string SquashName(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }
    }
}
