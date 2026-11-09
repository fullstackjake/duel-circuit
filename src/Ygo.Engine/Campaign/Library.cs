using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ygo.Engine.AI;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// The rules your deck answers to at a match: a ban list (null for none), the card pool's last month ("" for any
    /// card) and how many Ban Passes it may use. A circuit's come from the campaign's mode; a Library cup forces its own.
    /// </summary>
    public sealed class FormatRules
    {
        public Banlist? List;
        public string Pool = "";
        public int Passes;
        /// <summary>A Library cup's: it applies whatever the campaign's mode, and loaners keep to its pool too.</summary>
        public bool Forced;
        /// <summary>Where your originals play as printed (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 5.1): a cup's pool month, the alley's printed texts.</summary>
        public TextRules Texts = TextRules.Today;
    }

    /// <summary>A round of the "Every Era" final: one cup's boss, under that cup's list and pool.</summary>
    public sealed class FinalRound
    {
        public int Cup;
        public CircuitMatch Match = new CircuitMatch();
    }

    /// <summary>
    /// The Format Library (docs/dev/POST-GAME-PROPOSAL-2026-10-30.md): 12 historic cups in campaign/library.txt, each under
    /// its own ban list and card pool whatever the campaign's mode, open once the Tier 0 Gauntlet is cleared. All cups
    /// are open at once. A cup is cleared by winning its matches (a "Champion's table" match is optional); its Format
    /// Master stamp needs every match won with a deck of yours (no loaner) using no Ban Passes. Wins are kept by cup
    /// name in <see cref="Profile.LibraryWins"/>, apart from the circuits, so New Game+ never re-pays a first clear.
    /// </summary>

    public sealed class Library
    {
        public const string CupsFile = "library.txt", AdviceFile = "library-advice.txt";
        /// <summary>The circuit whose clear opens the Library.</summary>
        public const string OpensAfter = "Tier 0 Gauntlet";
        /// <summary>A match's first win (plus <see cref="Circuits.FirstWinBonus"/>), a win after that (at most 300: a cleared cup isn't a DP farm).</summary>
        public const int WinDp = 600, RepeatWinDp = 150;
        public const int ClearTokens = 2, StampTokens = 1;

        public IReadOnlyList<Circuit> Cups { get; }

        /// <summary>
        /// The "Every Era" final (library.txt's `final:`, `finalprize:` and `round:` lines): open once every cup is
        /// cleared, its rounds played in order, each with a deck you built (no loaner, no Ban Passes), a lost round played
        /// again. <see cref="Final"/> holds its name, prize and the rounds' matches (for its story events and results).
        /// </summary>
        public Circuit Final { get; } = new Circuit();
        public List<FinalRound> Rounds { get; } = new List<FinalRound>();
        public const int FinalTokens = 3;
        /// <summary>The circuit Library duels earn EXP as (the last one).</summary>
        private readonly int _expCircuit;
        /// <summary>The circuit index Library duels (and bracket duels) earn EXP as.</summary>
        public int ExpCircuit => _expCircuit;

        public Library(IReadOnlyList<Circuit> cups, int expCircuit)
        {
            Cups = cups;
            _expCircuit = expCircuit;
        }

        public static Library Load(string path, IEnumerable<Personality> roster, Circuits circuits)
        {
            if (!File.Exists(path)) return Posters.Library = new Library(new List<Circuit>(), Math.Max(0, circuits.All.Count - 1));
            var library = new Library(Circuits.Load(path, roster).All, Math.Max(0, circuits.All.Count - 1));
            library.LoadFinal(File.ReadAllLines(path), roster);
            // The Every Era poster asks the loaded Library for its final (a later load replaces it).
            Posters.Library = library;
            return library;
        }

        /// <summary>Reads the final's lines; a round whose cup or character isn't known is left out.</summary>
        private void LoadFinal(IEnumerable<string> lines, IEnumerable<Personality> roster)
        {
            var byId = roster.ToDictionary(p => p.Id);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                var hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash).Trim();
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var key = line.Substring(0, colon).Trim().ToLowerInvariant();
                var value = line.Substring(colon + 1).Trim();
                if (key == "final") Final.Name = value;
                else if (key == "finalprize") int.TryParse(value, out Final.Prize);
                else if (key == "round")
                {
                    var bar = value.IndexOf('|');
                    if (bar < 0) continue;
                    int cup = IndexOf(value.Substring(0, bar).Trim());
                    var who = value.Substring(bar + 1).Trim();
                    var gap = who.IndexOf(' ');
                    if (cup < 0 || gap < 0 || !byId.TryGetValue(who.Substring(0, gap), out var character)) continue;
                    var match = new CircuitMatch { Character = character, Deck = who.Substring(gap + 1).Trim() };
                    Rounds.Add(new FinalRound { Cup = cup, Match = match });
                    Final.Matches.Add(match);
                }
            }
        }

        /// <summary>A final round's key in <see cref="Profile.LibraryWins"/> ("lib:every-era.0").</summary>
        public string FinalKey(int round) => $"lib:{Id(Final)}.{round}";

        /// <summary>Whether the final is open: it has rounds and every cup is cleared.</summary>
        public bool FinalOpen(Profile profile) => Rounds.Count > 0 && Cups.Count > 0 && ClearedCount(profile) == Cups.Count;

        public bool FinalWon(Profile profile, int round) => profile.LibraryWins.ContainsKey(FinalKey(round));

        public bool FinalCleared(Profile profile) => Rounds.Count > 0 && Enumerable.Range(0, Rounds.Count).All(r => FinalWon(profile, r));

        /// <summary>The round to play next (the first not won yet), or -1 once the final is won.</summary>
        public int FinalNext(Profile profile) => Enumerable.Range(0, Rounds.Count).Where(r => !FinalWon(profile, r)).DefaultIfEmpty(-1).First();

        /// <summary>Whether a round can be played now: the final is open and every round before it is won (won rounds can be replayed).</summary>
        public bool CanPlayRound(Profile profile, int round) =>
            FinalOpen(profile) && round >= 0 && round < Rounds.Count && Enumerable.Range(0, round).All(r => FinalWon(profile, r));

        /// <summary>A round's rules: its cup's list, pool and texts, and no Ban Passes (the final takes decks as built for the era).</summary>
        public FormatRules RoundRules(int round, IEnumerable<Banlist> lists)
        {
            var rules = Rules(Rounds[round].Cup, lists);
            rules.Passes = 0;
            return rules;
        }

        /// <summary>
        /// Records a final round (winner 0 you, 1 them, 2 draw), played with <paramref name="deck"/> of yours: a win pays like
        /// a cup match (<see cref="WinDp"/> and the first-win bonus once, <see cref="RepeatWinDp"/> after) and counts at
        /// the round's cup for its engraving; the last round's first win clears the final: its prize and
        /// <see cref="FinalTokens"/> (<see cref="DuelReward.ClearedCircuit"/> is <see cref="Final"/>). A first win
        /// that opens the next round sets <see cref="DuelReward.RoundOpened"/>.
        /// </summary>
        public DuelReward RecordFinal(Profile profile, int round, int winner, string deck)
        {
            var reward = new DuelReward();
            var key = FinalKey(round);
            // A round you'd already won: its level-ups pay tokens, not DP (the first win and retries before it still pay DP).
            bool wonBefore = profile.LibraryWins.ContainsKey(key);
            var opponent = Rounds[round].Match.Character;
            int prize = 0;
            if (winner == 0)
            {
                bool wasCleared = FinalCleared(profile);
                reward.FirstWin = !profile.LibraryWins.ContainsKey(key);
                reward.Dp = reward.FirstWin ? WinDp + Circuits.FirstWinBonus : RepeatWinDp;
                profile.LibraryWins[key] = profile.LibraryWins.GetValueOrDefault(key) + 1;
                profile.AddWin(opponent.Id, reward);
                if (deck.Length > 0)
                {
                    var cupId = Id(Cups[Rounds[round].Cup]);
                    if (!profile.LibraryDecks.TryGetValue(cupId, out var decks)) profile.LibraryDecks[cupId] = decks = new HashSet<string>();
                    decks.Add(deck);
                }
                var parts = new List<string> { $"+{reward.Dp} DP" + (reward.FirstWin ? " (first win)" : "") };
                if (!wasCleared && FinalCleared(profile))
                {
                    reward.ClearedCircuit = Final;
                    reward.Dp += Final.Prize;
                    prize = Final.Prize;
                    reward.Tokens += FinalTokens;
                    parts.Add($"{Final.Name} won! +{Final.Prize:N0} DP prize, +{FinalTokens} Card Tokens");
                }
                else if (reward.FirstWin && FinalNext(profile) is int next && next > round) reward.RoundOpened = next + 1;
                profile.Tokens += reward.Tokens;
                reward.Summary = string.Join(" · ", parts);
            }
            else
            {
                reward.Dp = winner == 2 ? Circuits.DrawDp : Circuits.LossDp;
                if (winner == 1) profile.Losses[opponent.Id] = profile.Losses.GetValueOrDefault(opponent.Id) + 1;
                reward.Summary = $"+{reward.Dp} DP for the duel";
            }
            Circuits.PayDuel(profile, Ledger.Final, winner, reward.Dp, prize);
            profile.NoteResult(key, winner);
            reward.Exp = Leveling.DuelExp(_expCircuit, winner, reward.FirstWin) + (reward.ClearedCircuit != null ? Leveling.CircuitClearExp : 0);
            reward.LevelUps = Leveling.AddExp(profile, reward.Exp, payDp: !wonBefore);
            foreach (var up in reward.LevelUps)
                reward.Summary += " · " + Leveling.Describe(up);
            return reward;
        }

        /// <summary>A cup's id in keys and saves ("Kashtira & Snake-Eye Cup" is "kashtira-snake-eye-cup").</summary>
        public static string Id(Circuit cup) => AI.Playbooks.Playbooks.Slug(cup.Name);

        public string Key(int cup, int match) => $"lib:{Id(Cups[cup])}.{match}";

        public int IndexOf(string name) => Cups.ToList().FindIndex(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Opens the Library once the Gauntlet is cleared, or for a save that saw the champion scene before New Game+.
        /// True when it opens now (the "library" story event).
        /// </summary>
        public bool Open(Profile profile, Circuits circuits)
        {
            if (profile.LibraryOpen || Cups.Count == 0) return false;
            int gauntlet = circuits.All.ToList().FindIndex(c => c.Name == OpensAfter);
            if (!(gauntlet >= 0 && circuits.IsCleared(gauntlet, profile)) && !profile.Seen.Contains("champion")) return false;
            profile.LibraryOpen = true;
            return true;
        }

        public bool HasWon(Profile profile, int cup, int match) => profile.LibraryWins.ContainsKey(Key(cup, match));

        public bool IsCleared(int cup, Profile profile) =>
            cup >= 0 && cup < Cups.Count && Enumerable.Range(0, Cups[cup].Matches.Count).All(m => Cups[cup].Matches[m].Champion || HasWon(profile, cup, m));

        public bool HasStamp(int cup, Profile profile) =>
            cup >= 0 && cup < Cups.Count && Enumerable.Range(0, Cups[cup].Matches.Count).All(m => profile.LibraryBuilt.Contains(Key(cup, m)));

        /// <summary>
        /// Whether a win with <paramref name="deck"/> counts toward a cup's Format Master stamp (<see cref="Record"/>'s
        /// built): a deck you built (<see cref="Loaners.IsYours"/>: no loaner, no partner's edit, the final's own rule)
        /// played with no Ban Passes. Null (a deck that isn't saved) never counts.
        /// </summary>
        public static bool CountsForStamp(Deck? deck, int passesUsed) => deck != null && Loaners.IsYours(deck) && passesUsed == 0;

        public int ClearedCount(Profile profile) => Enumerable.Range(0, Cups.Count).Count(c => IsCleared(c, profile));
        public int StampCount(Profile profile) => Enumerable.Range(0, Cups.Count).Count(c => HasStamp(c, profile));

        /// <summary>
        /// A cup's rules for your deck: its list (null when not installed), its pool, the Library's 5 Ban Passes, and the
        /// texts of its pool month (the Goat Cup's 2005-09 is a Goat table).
        /// </summary>
        public FormatRules Rules(int cup, IEnumerable<Banlist> lists)
        {
            var list = lists.FirstOrDefault(l => l.Name == Cups[cup].Banlist);
            return new FormatRules
            {
                List = list,
                Pool = Cups[cup].Pool,
                Passes = BanPasses.Max,
                Forced = true,
                Texts = TextRules.ForList(list, lists, Cups[cup].Pool),
            };
        }

        /// <summary>
        /// Records a Library match (winner 0 you, 1 them, 2 draw) and pays out: <see cref="WinDp"/> and the first-win bonus
        /// the first time, <see cref="RepeatWinDp"/> after; half with a <paramref name="loaner"/>. A win with a <paramref name="deck"/> of yours lets it engrave the cup's binder
        /// (<see cref="Cosmetics.EngraveDecks"/>): not a loaner, and, as the stamp's test, not a partner's edit (<paramref name="played"/>, the deck as
        /// saved: <see cref="Loaners.IsYours"/>; owner, 2026-11-05; a caller that passes none is trusted on the name alone, as before). A cup's first clear pays
        /// its prize and <see cref="ClearTokens"/>; its stamp (<paramref name="built"/>: <see cref="CountsForStamp"/>, a deck of yours, no Ban Passes)
        /// <see cref="StampTokens"/> more. <see cref="DuelReward.ClearedCircuit"/> and <see cref="DuelReward.StampedCup"/>
        /// name what this win completed.
        /// </summary>
        public DuelReward Record(Profile profile, int cup, int match, int winner, bool loaner, bool built, string deck = "", Deck? played = null)
        {
            var reward = new DuelReward();
            var c = Cups[cup];
            var key = Key(cup, match);
            // A match you'd already won: its level-ups pay tokens, not DP (the first win and retries before it still pay DP).
            bool wonBefore = profile.LibraryWins.ContainsKey(key);
            var opponent = c.Matches[match].Character;
            int prize = 0;
            if (winner == 0)
            {
                bool wasCleared = IsCleared(cup, profile), hadStamp = HasStamp(cup, profile);
                reward.FirstWin = !profile.LibraryWins.ContainsKey(key);
                reward.Dp = reward.FirstWin ? WinDp + Circuits.FirstWinBonus : RepeatWinDp;
                if (loaner) reward.Dp = (int)Math.Round(reward.Dp * Loaners.DpShare);
                profile.LibraryWins[key] = profile.LibraryWins.GetValueOrDefault(key) + 1;
                if (built && !loaner) profile.LibraryBuilt.Add(key);
                if (!loaner && deck.Length > 0 && (played == null || Loaners.IsYours(played)))
                {
                    if (!profile.LibraryDecks.TryGetValue(Id(c), out var decks)) profile.LibraryDecks[Id(c)] = decks = new HashSet<string>();
                    decks.Add(deck);
                }
                if (reward.FirstWin && c.Matches[match].Champion) reward.TableCup = c;
                profile.AddWin(opponent.Id, reward);
                var parts = new List<string> { $"+{reward.Dp} DP" + (reward.FirstWin ? " (first win)" : "") + (loaner ? " (half: loaner deck)" : "") };
                if (!wasCleared && IsCleared(cup, profile))
                {
                    reward.ClearedCircuit = c;
                    reward.Dp += c.Prize;
                    prize = c.Prize;
                    reward.Tokens += ClearTokens;
                    parts.Add($"{c.Name} cleared! +{c.Prize:N0} DP prize, +{ClearTokens} Card Tokens");
                    // The last cup cleared opens the final.
                    if (FinalOpen(profile))
                    {
                        reward.UnlockedCircuit = Final;
                        parts.Add($"The {Final.Name} final is open");
                    }
                }
                if (!hadStamp && HasStamp(cup, profile))
                {
                    reward.StampedCup = c;
                    reward.Tokens += StampTokens;
                    parts.Add($"Format Master stamp! +{StampTokens} Card Token");
                }
                profile.Tokens += reward.Tokens;
                reward.Summary = string.Join(" · ", parts);
            }
            else
            {
                reward.Dp = winner == 2 ? Circuits.DrawDp : Circuits.LossDp;
                if (winner == 1) profile.Losses[opponent.Id] = profile.Losses.GetValueOrDefault(opponent.Id) + 1;
                reward.Summary = $"+{reward.Dp} DP for the duel";
            }
            Circuits.PayDuel(profile, Ledger.Cup, winner, reward.Dp, prize);
            profile.NoteResult(key, winner);
            reward.Exp = Leveling.DuelExp(_expCircuit, winner, reward.FirstWin) + (reward.ClearedCircuit != null ? Leveling.CircuitClearExp : 0);
            reward.LevelUps = Leveling.AddExp(profile, reward.Exp, payDp: !wonBefore);
            foreach (var up in reward.LevelUps)
                reward.Summary += " · " + Leveling.Describe(up);
            return reward;
        }

        /// <summary>
        /// The story events a Library result raises: "cupclear:", "table:" (a Champion's table's first win), "stamp:" (with
        /// the cup's name; plain "stamp" too, for the first stamp's scene), "final" (the final opened), "round:n" (its next
        /// round opened) and levels reached.
        /// </summary>
        public static List<string> Events(DuelReward reward)
        {
            var events = new List<string>();
            if (reward.ClearedCircuit != null) events.Add("cupclear:" + reward.ClearedCircuit.Name);
            if (reward.TableCup != null) events.Add("table:" + reward.TableCup.Name);
            if (reward.UnlockedCircuit != null) events.Add("final");
            if (reward.RoundOpened > 0) events.Add("round:" + reward.RoundOpened);
            // "stamp" with every stamp: its scene (shown once) explains the first one, whichever cup it is.
            if (reward.StampedCup != null) { events.Add("stamp"); events.Add("stamp:" + reward.StampedCup.Name); }
            events.AddRange(reward.LevelUps.Select(u => "level:" + u.Level));
            return events;
        }
    }
}
