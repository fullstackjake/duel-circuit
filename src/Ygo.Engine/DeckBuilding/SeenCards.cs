using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Ygo.Engine.Presentation;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.DeckBuilding
{
    /// <summary>
    /// The opponent's cards that became known in one duel, for the deck editor's "Seen in duels" scope: cards the
    /// opponent activated or summoned, and cards that moved somewhere both players can see (face-up on the field, the
    /// GY, face-up banished, face-up in the Extra Deck, Xyz material) or were flipped face-up while on the opponent's
    /// side. Face-down Sets and cards going to the hand or Deck stay unknown (the core's messages carry their codes, so
    /// this checks where they went). Tokens are skipped. Base codes (<see cref="Banlist.BaseCode"/>), first-seen order.
    /// It listens to the core's messages as the duel runs, like <see cref="Campaign.DuelTally"/> (subscribe
    /// <see cref="Observe"/> to <c>DuelRunner.MessageReceived</c>). In a tag duel the opponent is the other team, so both
    /// opponents count.
    /// </summary>
    public sealed class SeenCards
    {
        private readonly ICardDataProvider? _cards;
        private readonly List<uint> _order = new List<uint>();
        private readonly Dictionary<uint, int> _counts = new Dictionary<uint, int>();
        private readonly HashSet<uint> _unplayed = new HashSet<uint>();

        /// <summary>Your seat (your team's in a tag duel): 0 in the game. Every other seat is the opponent.</summary>
        public readonly int Seat;

        /// <param name="cards">Card data, for base codes and to skip tokens (null: codes are kept as they come).</param>
        /// <param name="seat">Your seat (your team's in a tag duel): 0 in the game.</param>
        public SeenCards(ICardDataProvider? cards = null, int seat = 0)
        {
            _cards = cards;
            Seat = seat;
        }

        /// <summary>The opponent's known cards in the order they first showed up, each with how often it was played (activated or summoned; at least 1).</summary>
        public IReadOnlyList<(uint code, int count)> Cards => _order.Select(c => (c, _counts[c])).ToList();

        /// <summary>How many different cards were seen.</summary>
        public int Count => _order.Count;

        /// <summary>Reads one core message (subscribe it to <c>DuelRunner.MessageReceived</c>).</summary>
        public void Observe(byte[] message)
        {
            if (message.Length == 0) return;
            if (message[0] == Msg.PosChange)
            {
                // A card turned face-up where it is (attacked while Set, flipped by an effect).
                if (message.Length < 1 + 4 + 5) return;
                var r = new MessageReader(message, 1);
                var code = r.U32();
                int controller = r.U8();
                r.U8(); r.U8(); r.U8(); // location, sequence, previous position
                var position = r.U8();
                if (IsOpponent(controller) && (position & FaceUp) != 0) See(code, false);
                return;
            }
            DuelEvent? e;
            try { e = DuelEvent.Parse(message); }
            catch (Exception ex) when (ex is ArgumentException || ex is IndexOutOfRangeException) { return; }
            if (e == null) return;
            switch (e.Kind)
            {
                case DuelEventKind.Activate:
                case DuelEventKind.NormalSummon:
                case DuelEventKind.SpecialSummon:
                case DuelEventKind.FlipSummon:
                    if (IsOpponent(e.Player)) See(e.Code, true);
                    break;
                case DuelEventKind.Move:
                    // Where it ended up decides whether anyone could see it, and on whose side it is.
                    if (IsOpponent(e.To.Controller) && Visible(e.To)) See(e.Code, false);
                    break;
            }
        }

        private const uint FaceUp = Position.FaceUpAttack | Position.FaceUpDefense;

        private bool IsOpponent(int controller) => controller <= 1 && controller != Seat;

        /// <summary>Both players can see a card here: the GY, Xyz material, or face-up on the field, banished or in the Extra Deck.</summary>
        private static bool Visible(CardRef at)
        {
            if ((at.Location & Location.Overlay) != 0) return true;
            switch (at.Location & 0x7f)
            {
                case Location.Grave: return true;
                case Location.MZone:
                case Location.SZone:
                case Location.Removed:
                case Location.Extra: return (at.Position & FaceUp) != 0;
                default: return false;
            }
        }

        private void See(uint code, bool played)
        {
            if (code == 0) return;
            if (_cards != null)
            {
                var info = _cards.Get(code);
                if (info != null && (info.Type & CardType.Token) != 0) return;
                code = Banlist.BaseCode(code, _cards);
            }
            if (_counts.TryGetValue(code, out var n))
            {
                // Its first play after it was seen another way (a summon's move comes before the summon) is still its first.
                if (played) _counts[code] = _unplayed.Remove(code) ? 1 : n + 1;
                return;
            }
            _order.Add(code);
            // A card seen without being played (milled, destroyed from a Set) still counts once.
            _counts[code] = 1;
            if (!played) _unplayed.Add(code);
        }
    }

    /// <summary>
    /// The deck editor's memory of cards (mods/decks/seen.txt, beside favorites.txt): the opponent's cards from the last
    /// 20 duels, newest first, and the last 40 cards you looked at in the editor. Plain text:
    /// <code>
    /// duel 2026-11-07 21:14	Professor Mica	campaign
    /// 89631139 2
    /// 14558127 1
    /// viewed 89631139 14558127
    /// </code>
    /// A missing or broken file reads as empty; nothing here throws on a bad file.
    /// </summary>
    public sealed class SeenLog
    {
        public const int MaxDuels = 20, MaxViewed = 40;

        /// <summary>One duel: when it ended, the opponent's (in-game character's) name, the mode ("campaign", "free play"...), and the cards seen in first-seen order.</summary>
        public sealed class Duel
        {
            public DateTime When;
            public string Opponent = "";
            public string Mode = "";
            public List<(uint code, int count)> Cards = new List<(uint, int)>();

            public bool Has(uint code) => Cards.Any(c => c.code == code);
        }

        /// <summary>Where it's read from and written to.</summary>
        public readonly string Path;
        /// <summary>The kept duels, newest first (at most <see cref="MaxDuels"/>).</summary>
        public readonly List<Duel> Duels = new List<Duel>();
        /// <summary>Recently viewed cards, newest first, no repeats (at most <see cref="MaxViewed"/>).</summary>
        public readonly List<uint> Viewed = new List<uint>();

        private const string DateFormat = "yyyy-MM-dd HH:mm";

        public SeenLog(string path) { Path = path; }

        /// <summary>
        /// The log at <paramref name="path"/>; empty when the file is missing or can't be read. When only the temporary file
        /// a write leaves for a moment is there (<c>seen.txt.tmp</c>: a crash before the first move), it's read instead.
        /// </summary>
        public static SeenLog Read(string path)
        {
            var log = new SeenLog(path);
            try
            {
                var source = File.Exists(path) ? path : File.Exists(path + ".tmp") ? path + ".tmp" : null;
                if (source == null) return log;
                Duel? duel = null;
                foreach (var raw in File.ReadAllLines(source))
                {
                    var line = raw.TrimEnd('\r');
                    if (line.StartsWith("duel "))
                    {
                        duel = null;
                        var fields = line.Substring(5).Split('\t');
                        if (fields.Length < 3 || !DateTime.TryParseExact(fields[0].Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var when))
                            continue;
                        duel = new Duel { When = when, Opponent = fields[1].Trim(), Mode = fields[2].Trim() };
                        if (log.Duels.Count < MaxDuels) log.Duels.Add(duel);
                        continue;
                    }
                    if (line.StartsWith("viewed"))
                    {
                        duel = null;
                        foreach (var word in line.Substring(6).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                            if (uint.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out var code) && code != 0
                                && !log.Viewed.Contains(code) && log.Viewed.Count < MaxViewed)
                                log.Viewed.Add(code);
                        continue;
                    }
                    if (duel == null) continue;
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2 || !uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var c) || c == 0
                        || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n <= 0)
                        continue;
                    if (!duel.Has(c)) duel.Cards.Add((c, n));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                return new SeenLog(path);
            }
            return log;
        }

        /// <summary>Adds a finished duel at the front (repeated codes merge), keeping the newest <see cref="MaxDuels"/>.</summary>
        public Duel AddDuel(DateTime when, string opponent, string mode, IEnumerable<(uint code, int count)> cards)
        {
            var duel = new Duel { When = when, Opponent = Clean(opponent), Mode = Clean(mode) };
            foreach (var (code, count) in cards)
            {
                if (code == 0) continue;
                int at = duel.Cards.FindIndex(c => c.code == code);
                if (at >= 0) duel.Cards[at] = (code, duel.Cards[at].count + Math.Max(1, count));
                else duel.Cards.Add((code, Math.Max(1, count)));
            }
            Duels.Insert(0, duel);
            if (Duels.Count > MaxDuels) Duels.RemoveRange(MaxDuels, Duels.Count - MaxDuels);
            return duel;
        }

        /// <summary><see cref="AddDuel(DateTime, string, string, IEnumerable{ValueTuple{uint, int}})"/> from a duel's observer.</summary>
        public Duel AddDuel(DateTime when, string opponent, string mode, SeenCards seen) => AddDuel(when, opponent, mode, seen.Cards);

        /// <summary>A card was looked at (selected in the editor, opened in the reading view): it moves to the front of <see cref="Viewed"/>.</summary>
        public void View(uint code)
        {
            if (code == 0) return;
            Viewed.Remove(code);
            Viewed.Insert(0, code);
            if (Viewed.Count > MaxViewed) Viewed.RemoveRange(MaxViewed, Viewed.Count - MaxViewed);
        }

        /// <summary>
        /// The cards seen, newest duel first and in the order they appeared, each once (a card seen in several duels
        /// shows where it was seen last). <paramref name="duel"/> limits it to one duel.
        /// </summary>
        public List<uint> Codes(Duel? duel = null)
        {
            var seen = new HashSet<uint>();
            var result = new List<uint>();
            foreach (var d in duel != null ? new[] { duel } : (IEnumerable<Duel>)Duels)
                foreach (var (code, _) in d.Cards)
                    if (seen.Add(code)) result.Add(code);
            return result;
        }

        /// <summary>The newest duel the card was seen in ("vs Professor Mica"), or null.</summary>
        public Duel? LastSeenIn(uint code) => Duels.FirstOrDefault(d => d.Has(code));

        /// <summary>
        /// Writes the log to <see cref="Path"/> through a temporary file that then replaces it in one step, so a crash at
        /// any point leaves the old file or the new one (and <see cref="Read"/> falls back to the temporary file if that's all there is).
        /// </summary>
        public void Write()
        {
            var sb = new StringBuilder();
            foreach (var d in Duels.Take(MaxDuels))
            {
                sb.Append("duel ").Append(d.When.ToString(DateFormat, CultureInfo.InvariantCulture)).Append('\t')
                    .Append(Clean(d.Opponent)).Append('\t').Append(Clean(d.Mode)).Append('\n');
                foreach (var (code, count) in d.Cards)
                    sb.Append(code.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            if (Viewed.Count > 0)
                sb.Append("viewed ").Append(string.Join(" ", Viewed.Take(MaxViewed).Select(c => c.ToString(CultureInfo.InvariantCulture)))).Append('\n');
            var folder = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            var temp = Path + ".tmp";
            File.WriteAllText(temp, sb.ToString());
            if (File.Exists(Path)) File.Replace(temp, Path, null);
            else File.Move(temp, Path);
        }

        /// <summary>Tabs and line breaks would break the line format.</summary>
        private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
