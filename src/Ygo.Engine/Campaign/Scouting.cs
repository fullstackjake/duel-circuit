using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ygo.Engine.AI.Playbooks;

namespace Ygo.Engine.Campaign
{
    /// <summary>What a scout knows about a deck: its game plan, the cards to watch, what beats it, and the turn to want.</summary>
    public sealed class ScoutReport
    {
        public string Deck = "";
        /// <summary>
        /// The character it's written for ("for: &lt;character&gt;"), or "" for anyone playing the deck: a deck file several
        /// characters share (Ilse's zombies, Old Gus's) can carry one character's late-era advice without it reaching the others.
        /// </summary>
        public string For = "";
        /// <summary>How the deck wins, in a sentence or two.</summary>
        public string Plan = "";
        /// <summary>The cards to watch for, by name.</summary>
        public List<string> Keys = new List<string>();
        /// <summary>What beats it: the cards and kinds of cards it struggles against.</summary>
        public string Counters = "";
        /// <summary>Cards to put in your deck against it, by name.</summary>
        public List<string> Build = new List<string>();
        /// <summary>Against it, would you rather go first or second ("first", "second", or "" for either), and why.</summary>
        public string Turn = "";
        public string TurnWhy = "";
        /// <summary>Written for this deck (false: put together from its cards and playbook).</summary>
        public bool Written;
    }

    /// <summary>
    /// Scouting reports (campaign/scouting.txt): written for the decks at the campaign's walls, and put together from the
    /// deck itself for every other one (its playbook's bosses, then its most-played monsters). Shown on the versus screen
    /// and after a loss, so the player knows what they're facing before choosing a deck and a turn.
    /// </summary>
    public sealed class Scouting
    {
        private readonly Dictionary<string, ScoutReport> _written;

        public Scouting(Dictionary<string, ScoutReport> written) => _written = written;

        public static Scouting Empty => new Scouting(new Dictionary<string, ScoutReport>());

        public IReadOnlyCollection<ScoutReport> Written => _written.Values;

        /// <summary>A report's key: the deck, with the character it's for after an "@" when it's for one.</summary>
        private static string KeyOf(string deck, string character) => character.Length > 0 ? $"{deck}@{character}" : deck;

        /// <summary>
        /// Reads the file: a "deck:" line starts a report, then an optional "for: &lt;character&gt;" (the report is only that
        /// character's), "plan:", "keys:" (names, separated by ;), "beats it:", "build:"
        /// (names, separated by ;) and "turn:" ("first" or "second", then why after a dash). Lines starting with # are comments.
        /// </summary>
        public static Scouting Parse(string text)
        {
            var list = new List<ScoutReport>();
            ScoutReport? current = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var colon = line.IndexOf(':');
                if (colon < 0) continue;
                var key = line.Substring(0, colon).Trim().ToLowerInvariant();
                var value = line.Substring(colon + 1).Trim();
                if (key == "deck")
                {
                    current = new ScoutReport { Deck = value, Written = true };
                    list.Add(current);
                    continue;
                }
                if (current == null) continue;
                switch (key)
                {
                    case "for": current.For = value; break;
                    case "plan": current.Plan = value; break;
                    case "keys": current.Keys = value.Split(';').Select(k => k.Trim()).Where(k => k.Length > 0).ToList(); break;
                    case "beats it": current.Counters = value; break;
                    case "build": current.Build = value.Split(';').Select(k => k.Trim()).Where(k => k.Length > 0).ToList(); break;
                    case "turn":
                        var dash = value.IndexOf(" - ", StringComparison.Ordinal);
                        current.Turn = (dash >= 0 ? value.Substring(0, dash) : value).Trim().ToLowerInvariant();
                        current.TurnWhy = dash >= 0 ? value.Substring(dash + 3).Trim() : "";
                        break;
                }
            }
            var reports = new Dictionary<string, ScoutReport>();
            foreach (var r in list) reports[KeyOf(r.Deck, r.For)] = r;
            return new Scouting(reports);
        }

        public static Scouting Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;

        /// <summary>
        /// The report for a deck as <paramref name="character"/> plays it: the one written for them, else the one written for
        /// the deck, else one put together from the deck: its playbook's bosses and its most-played monsters as the cards to
        /// watch, and its playbook's turn preference. A report written for another character never applies.
        /// </summary>
        public ScoutReport For(string deckName, Deck deck, ICardDataProvider cards, string character = "")
        {
            var slug = Playbooks.Slug(Loaners.RecipeOf(deckName));
            if (character.Length > 0 && _written.TryGetValue(KeyOf(slug, character), out var theirs)) return theirs;
            if (_written.TryGetValue(slug, out var written)) return written;
            var playbook = Playbooks.ForDeck(deck) ?? Playbooks.ForDeck(slug);
            var names = new List<string>();
            void Add(uint code)
            {
                var name = cards.Get(code)?.Name;
                if (name != null && !names.Contains(name)) names.Add(name);
            }
            if (playbook != null)
                foreach (var code in deck.Main.Concat(deck.Extra).Distinct().Where(playbook.IsBoss)) Add(code);
            foreach (var group in deck.Main.GroupBy(c => c).OrderByDescending(g => g.Count()))
            {
                if (names.Count >= 4) break;
                if (cards.Get(group.Key) is { } card && (card.Type & 0x1) != 0) Add(group.Key);
            }
            return new ScoutReport
            {
                Deck = slug,
                Keys = names.Take(4).ToList(),
                Turn = playbook != null && playbook.GoesSecond ? "first" : "",
                TurnWhy = playbook != null && playbook.GoesSecond ? "it would rather go second, working off your board" : "",
            };
        }
    }
}
