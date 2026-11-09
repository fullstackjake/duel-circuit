using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ygo.Engine.AI;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// A special guest (campaign/guests.txt): a duelist outside the circuits, unlocked by a milestone, whom you can
    /// challenge one on one or (when they bring a partner) face in a tag duel with their partner; beaten once, they can
    /// partner you in the Tag Cups.
    /// </summary>
    public sealed class Guest
    {
        public Personality Character = Personality.Balanced;
        /// <summary>
        /// The decks they play one on one, one a duel: <see cref="GuestList.DeckFor"/> picks by your wins against them, so
        /// beating one moves them on to the next (a loss or a draw doesn't). Usually one.
        /// </summary>
        public List<string> Decks = new List<string>();
        /// <summary>Their first deck (the tag duel's, the wall study's).</summary>
        public string Deck => Decks.Count > 0 ? Decks[0] : "";
        public Personality Partner = Personality.Balanced;
        public string PartnerDeck = "";
        /// <summary>Whether they bring a partner ("+ &lt;partner&gt; &lt;deck&gt;"): without one there's no tag duel against them.</summary>
        public bool HasTag { get; internal set; }
        /// <summary>
        /// "lessons" (all of Gus's lessons passed), "band:&lt;band&gt;" (every circuit in it cleared), "tagwins:&lt;n&gt;" (tag duels won),
        /// "final" (the Format Library's Every Era final won: <see cref="Campaign.Library.FinalCleared"/>; locked with no Library loaded).
        /// </summary>
        public string Unlock = "";
        /// <summary>The Library the "final" rule asks (the one <see cref="GuestList.Parse"/> was given; each guest holds it), or null.</summary>
        internal Library? Library;
        /// <summary>
        /// When the tag duel against them and their partner opens (on top of <see cref="Unlock"/>): the same rules plus
        /// "cup:&lt;Tag Cup&gt;" (that Tag Cup cleared). Empty: the first Tag Cup cleared, so no guest's tag duel comes
        /// before the game has taught tag duels (Card Shop Tag Night) and you've picked a partner on a Tag Cup's screen.
        /// </summary>
        public string TagUnlock = "";
        public string Blurb = "";
        /// <summary>DP for the first win against them (one on one, and again for the first tag win).</summary>
        public int Prize;
        /// <summary>
        /// "prize: &lt;DP&gt; once": the one-on-one prize pays once ever, not once per New Game+ cycle (the post-final guests, Ilse
        /// and Vera: their unlock survives New Game+, which is a free click). Paid is remembered as <see cref="GuestList.PrizeKey"/>
        /// in <see cref="Profile.Unlocks"/>, which New Game+ keeps. Without it the prize comes back each cycle, as before.
        /// </summary>
        public bool PrizeOnce;
    }

    public sealed class GuestList
    {
        public IReadOnlyList<Guest> All { get; }

        public GuestList(IReadOnlyList<Guest> guests) => All = guests;

        /// <summary>
        /// Reads guests.txt: "guest: &lt;character&gt; &lt;deck&gt;[, &lt;deck&gt;...] [+ &lt;partner&gt; &lt;deck&gt;]", then "unlock:",
        /// "tagunlock:", "blurb:" and "prize:" lines. Guests with an unknown character (or partner) are skipped.
        /// </summary>
        public static GuestList Parse(string text, IEnumerable<Personality> roster, Library? library = null)
        {
            var byId = roster.ToDictionary(p => p.Id);
            var guests = new List<Guest>();
            Guest? current = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                var hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash).Trim();
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var key = line.Substring(0, colon).Trim().ToLowerInvariant();
                var value = line.Substring(colon + 1).Trim();
                switch (key)
                {
                    case "guest":
                        current = null;
                        var pair = value.Split('+');
                        if (pair.Length > 2) break;
                        var (who, deckList) = Split(pair[0]);
                        var decks = deckList.Split(',').Select(d => d.Trim()).Where(d => d.Length > 0).ToList();
                        if (!byId.TryGetValue(who, out var character) || decks.Count == 0) break;
                        current = new Guest { Character = character, Decks = decks, Library = library };
                        if (pair.Length == 2)
                        {
                            var (partner, partnerDeck) = Split(pair[1]);
                            if (!byId.TryGetValue(partner, out var partnerCharacter) || partnerDeck.Length == 0) { current = null; break; }
                            current.Partner = partnerCharacter;
                            current.PartnerDeck = partnerDeck;
                            current.HasTag = true;
                        }
                        guests.Add(current);
                        break;
                    case "unlock" when current != null: current.Unlock = value; break;
                    case "tagunlock" when current != null: current.TagUnlock = value; break;
                    case "blurb" when current != null: current.Blurb = value; break;
                    case "prize" when current != null:
                        var prize = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (prize.Length > 0) int.TryParse(prize[0], out current.Prize);
                        current.PrizeOnce = prize.Skip(1).Any(w => w.Equals("once", StringComparison.OrdinalIgnoreCase));
                        break;
                }
            }
            return new GuestList(guests);

            static (string id, string deck) Split(string text)
            {
                var t = text.Trim();
                var gap = t.IndexOf(' ');
                return gap < 0 ? (t, "") : (t.Substring(0, gap), t.Substring(gap + 1).Trim());
            }
        }

        /// <param name="library">The Format Library, for the "final" unlock (null: it stays locked).</param>
        public static GuestList Load(string path, IEnumerable<Personality> roster, Library? library = null) =>
            File.Exists(path) ? Parse(File.ReadAllText(path), roster, library) : new GuestList(new List<Guest>());

        /// <summary>
        /// Which of <paramref name="guest"/>'s decks plays the next one-on-one duel: <see cref="Guest.Decks"/> in turn, moved on by
        /// each win against them (their one-on-one key's count in <see cref="Profile.MatchWins"/>; a loss or a draw keeps it,
        /// and New Game+ starts it over). No save field: the wins are the rotation.
        /// </summary>
        public static int DeckIndex(Guest guest, Profile profile) =>
            guest.Decks.Count <= 1 ? 0 : (profile.MatchWins.TryGetValue(Key(guest, false), out var w) ? w : 0) % guest.Decks.Count;

        /// <summary>The deck <paramref name="guest"/> plays one on one next (<see cref="DeckIndex"/>).</summary>
        public static string DeckFor(Guest guest, Profile profile) => guest.Decks.Count == 0 ? "" : guest.Decks[DeckIndex(guest, profile)];

        public static string Key(Guest guest, bool tag) => (tag ? "guesttag:" : "guest:") + guest.Character.Id;

        /// <summary>
        /// The <see cref="Profile.Unlocks"/> mark of a one-on-one prize paid ("guestprize:&lt;character&gt;"): written for every guest at
        /// a first one-on-one win (<see cref="Record"/>) and by New Game+ for each guest won this cycle (<see cref="Profile.NewGamePlus"/>,
        /// so a save from before the mark keeps what it earned), but only a <see cref="Guest.PrizeOnce"/> guest reads it.
        /// </summary>
        public static string PrizeKey(string character) => "guestprize:" + character;

        /// <summary>
        /// Whether the next win pays the guest's prize: their first win this cycle (<paramref name="tag"/>: the tag duel's), and for a
        /// <see cref="Guest.PrizeOnce"/> guest one on one, no prize paid in any cycle.
        /// </summary>
        public static bool PrizeDue(Guest guest, Profile profile, bool tag = false) =>
            !profile.MatchWins.ContainsKey(Key(guest, tag)) && (tag || !guest.PrizeOnce || !profile.Unlocks.Contains(PrizeKey(guest.Character.Id)));

        /// <summary>Tag duels you've won: Tag Cup matches and tag duels against guests, every win counted.</summary>
        public static int TagWins(Profile profile) =>
            profile.MatchWins.Where(w => w.Key.StartsWith("tag") || w.Key.StartsWith("guesttag:")).Sum(w => w.Value);

        public static bool IsUnlocked(Guest guest, Circuits circuits, Profile profile) => Meets(guest.Unlock, circuits, profile, guest.Library);

        /// <summary>
        /// Whether the tag duel against the guest and their partner is open: the guest unlocked, and their tag rule met
        /// (by default the first Tag Cup cleared). Once you've won it, it stays open. Never for a guest without a partner.
        /// </summary>
        public static bool IsTagUnlocked(Guest guest, Circuits circuits, Profile profile) =>
            guest.HasTag && IsUnlocked(guest, circuits, profile)
            && (profile.MatchWins.ContainsKey(Key(guest, true)) || Meets(TagRule(guest, circuits), circuits, profile, guest.Library));

        /// <summary>The milestone in words ("Clear the Novice band"), with progress where it counts up.</summary>
        public static string UnlockText(Guest guest, Profile profile) => RuleText(guest.Unlock, null, profile, guest.Library);

        /// <summary>What opens the guest's tag duel, in words ("Clear Card Shop Tag Night (opens after Rookie Cup)").</summary>
        public static string TagUnlockText(Guest guest, Circuits circuits, Profile profile) => RuleText(TagRule(guest, circuits), circuits, profile, guest.Library);

        /// <summary>The guest's tag rule: theirs, else the first Tag Cup cleared (none when there are no Tag Cups).</summary>
        private static string TagRule(Guest guest, Circuits circuits) =>
            guest.TagUnlock.Length > 0 ? guest.TagUnlock : circuits.TagCups.Count > 0 ? "cup:" + circuits.TagCups[0].Name : "";

        private static bool Meets(string rule, Circuits circuits, Profile profile, Library? library)
        {
            var (kind, arg) = Rule(rule);
            switch (kind)
            {
                case "lessons": return ChainLessons.All.All(l => profile.Seen.Contains(l.SeenKey));
                case "final": return library != null && library.FinalCleared(profile);
                case "band":
                    var inBand = Enumerable.Range(0, circuits.All.Count).Where(i => circuits.All[i].Band.Equals(arg, StringComparison.OrdinalIgnoreCase)).ToList();
                    return inBand.Count > 0 && inBand.All(i => circuits.IsCleared(i, profile));
                case "tagwins": return int.TryParse(arg, out var n) && TagWins(profile) >= n;
                case "cup":
                    int cup = TagCupIndex(circuits, arg);
                    return cup < 0 || circuits.IsTagCupCleared(cup, profile);
                default: return true;
            }
        }

        private static int TagCupIndex(Circuits circuits, string name)
        {
            for (int i = 0; i < circuits.TagCups.Count; i++)
                if (circuits.TagCups[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private static string RuleText(string rule, Circuits? circuits, Profile profile, Library? library)
        {
            var (kind, arg) = Rule(rule);
            switch (kind)
            {
                case "lessons": return $"Pass all of Gus's lessons ({ChainLessons.All.Count(l => profile.Seen.Contains(l.SeenKey))} of {ChainLessons.All.Count})";
                case "final":
                {
                    var final = $"Win the {(library != null && library.Final.Name.Length > 0 ? library.Final.Name : StoryPlaces.FinalName)} final";
                    if (library == null || !profile.LibraryOpen || library.Cups.Count == 0) return final;
                    int cleared = library.ClearedCount(profile);
                    if (cleared < library.Cups.Count) return $"{final} (Format Library: {cleared} of {library.Cups.Count} cups cleared)";
                    return $"{final} ({Enumerable.Range(0, library.Rounds.Count).Count(r => library.FinalWon(profile, r))} of {library.Rounds.Count} rounds won)";
                }
                case "band": return $"Clear every circuit in the {arg} band";
                case "tagwins": return $"Win {arg} tag duels ({Math.Min(TagWins(profile), int.TryParse(arg, out var n) ? n : 0)} so far)";
                case "cup":
                    int cup = circuits == null ? -1 : TagCupIndex(circuits, arg);
                    if (cup < 0) return $"Clear {arg}";
                    var c = circuits!.TagCups[cup];
                    if (!circuits.IsTagCupOpen(cup, profile)) return $"Clear {c.Name}, a Tag Cup that opens after {circuits.All[c.After].Name}";
                    int won = Enumerable.Range(0, c.Matches.Count).Count(m => profile.MatchWins.ContainsKey(Circuits.TagKey(cup, m)));
                    return $"Clear the Tag Cup {c.Name} ({won} of {c.Matches.Count} tag matches won)";
                default: return "";
            }
        }

        private static (string kind, string arg) Rule(string rule)
        {
            var colon = rule.IndexOf(':');
            return colon < 0 ? (rule.Trim().ToLowerInvariant(), "") : (rule.Substring(0, colon).Trim().ToLowerInvariant(), rule.Substring(colon + 1).Trim());
        }

        /// <summary>
        /// Records a duel against a guest (<paramref name="tag"/>: them and their partner). A win pays like a match of the
        /// circuit you're on, plus the guest's prize the first time; a loss or draw pays a little. Wins count against
        /// the guest (and their partner), so they can partner you afterwards. A win with your <paramref name="loaner"/> deck
        /// pays half the duel's DP (the prize in full).
        /// </summary>
        public static DuelReward Record(Profile profile, Circuits circuits, Guest guest, bool tag, int winner, bool loaner = false)
        {
            var reward = new DuelReward();
            int circuit = circuits.Current(profile);
            var key = Key(guest, tag);
            int prize = 0;
            if (winner == 0)
            {
                // FirstWin is the prize-paying win: the first this cycle, and for a "once" guest the first ever.
                reward.FirstWin = PrizeDue(guest, profile, tag);
                if (!tag && !profile.MatchWins.ContainsKey(key)) profile.Unlocks.Add(PrizeKey(guest.Character.Id));
                int duelDp = circuits.WinDp(circuit);
                if (loaner) duelDp = (int)Math.Round(duelDp * Loaners.DpShare);
                reward.Dp = duelDp + (reward.FirstWin ? guest.Prize : 0);
                profile.MatchWins[key] = (profile.MatchWins.TryGetValue(key, out var n) ? n : 0) + 1;
                profile.AddWin(guest.Character.Id, reward);
                if (tag) profile.AddWin(guest.Partner.Id, reward);
                reward.Summary = $"+{reward.Dp} DP" + (reward.FirstWin ? $" (first win: {guest.Prize} DP prize)" : "") + (loaner ? " (half: loaner deck)" : "");
                prize = reward.FirstWin ? guest.Prize : 0;
            }
            else
            {
                reward.Dp = winner == 2 ? Circuits.DrawDp : Circuits.LossDp;
                if (winner == 1) profile.Losses[guest.Character.Id] = (profile.Losses.TryGetValue(guest.Character.Id, out var l) ? l : 0) + 1;
                reward.Summary = $"+{reward.Dp} DP for the duel";
            }
            Circuits.PayDuel(profile, Ledger.Guest, winner, reward.Dp, prize);
            profile.NoteResult(key, winner);
            reward.Exp = Leveling.DuelExp(circuit, winner, reward.FirstWin);
            reward.LevelUps = Leveling.AddExp(profile, reward.Exp);
            foreach (var up in reward.LevelUps)
                reward.Summary += $" · Level {up.Level}! +{up.Dp} DP" + (up.Tokens > 0 ? $", +{up.Tokens} Card Token{(up.Tokens > 1 ? "s" : "")}" : "");
            return reward;
        }
    }
}
