using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ygo.Engine.AI;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// A seat on the Champion's Ladder: a rung's holder (<see cref="Rung"/> 1 at the bottom) or a visitor (<see cref="Rung"/> 0).
    /// More than one deck makes it a sitting: every deck in order in one go (Duchess Ilse's rung).
    /// </summary>
    public sealed class LadderSeat
    {
        public Personality Character = Personality.Balanced;
        public List<string> Decks = new List<string>();
        /// <summary>The rung's number, 1 at the bottom; 0 for a visitor.</summary>
        public int Rung;
        public bool IsVisitor => Rung == 0;
        /// <summary>
        /// Its key in <see cref="LadderState.Wins"/>: "rung.&lt;holder id&gt;" or "visitor.&lt;id&gt;". By holder, not by position, so a
        /// rung reordered or left out (the tuning pass, a missing deck) keeps every save's progress with the person beaten.
        /// </summary>
        public string Key => (IsVisitor ? Ladder.VisitorPrefix : Ladder.RungPrefix) + Character.Id;
        /// <summary>Whether taking it is a sitting (more than one deck, each beaten in turn in one go).</summary>
        public bool IsSitting => Decks.Count > 1;
        public string Deck => Decks.Count > 0 ? Decks[0] : "";
    }

    /// <summary>
    /// Your standing on the Champion's Ladder, kept apart from everything else on the save (chunk 1b puts one on
    /// <see cref="Profile"/>): the wins by seat ("rung.&lt;holder id&gt;", "visitor.&lt;id&gt;", "top") and the wins inside Ilse's
    /// sitting so far. Saved as optional "ladwin &lt;key&gt; &lt;n&gt;" and "ladsit &lt;n&gt;" lines: a save without them reads as
    /// unclimbed. New Game+ leaves it alone, so a first climb never pays twice.
    /// </summary>
    public sealed class LadderState
    {
        public const string WinLine = "ladwin", SittingLine = "ladsit";

        /// <summary>Wins by seat key (a rung's first win takes it; later wins are rematches), and "top" once the prize is paid.</summary>
        public Dictionary<string, int> Wins { get; } = new Dictionary<string, int>();
        /// <summary>Wins inside the sitting in progress (0: none started). A loss or a draw sets it back to 0.</summary>
        public int Sitting;

        public bool IsEmpty => Wins.Count == 0 && Sitting == 0;

        public int WinsAt(string key) => Wins.TryGetValue(key, out var n) ? n : 0;

        /// <summary>Appends the state's save lines (none for an unclimbed ladder).</summary>
        public void Write(StringBuilder sb)
        {
            foreach (var w in Wins.Where(w => w.Value > 0).OrderBy(w => w.Key, StringComparer.Ordinal)) sb.AppendLine($"{WinLine} {w.Key} {w.Value}");
            if (Sitting > 0) sb.AppendLine($"{SittingLine} {Sitting}");
        }

        /// <summary>
        /// Reads one save line already split as Profile.Load splits it (its key, then the words after it). True when it was
        /// a ladder line; a malformed one is skipped.
        /// </summary>
        public bool ReadLine(string key, string[] parts)
        {
            switch (key)
            {
                case WinLine:
                    if (parts.Length >= 2 && parts[0].Length > 0 && int.TryParse(parts[1], out var n) && n > 0) Wins[parts[0]] = n;
                    return true;
                case SittingLine:
                    if (parts.Length >= 1 && int.TryParse(parts[0], out var s) && s > 0) Sitting = s;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The ladder lines of a whole save (every other line ignored): an old save gives an empty state.</summary>
        public static LadderState Read(IEnumerable<string> lines)
        {
            var state = new LadderState();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var space = line.IndexOf(' ');
                var key = space < 0 ? line : line.Substring(0, space);
                var rest = space < 0 ? "" : line.Substring(space + 1);
                state.ReadLine(key, rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            }
            return state;
        }
    }

    /// <summary>What a ladder duel did: its reward (DP, tokens, EXP, level-ups, wins counted) and the story events it raised.</summary>
    public sealed class LadderResult
    {
        public DuelReward Reward = new DuelReward();
        public LadderSeat Seat = new LadderSeat();
        /// <summary>The rung this win took for the first time (0: none).</summary>
        public int RungTaken;
        /// <summary>This win reached the top for the first time (the prize, the tokens, the poster).</summary>
        public bool TopReached;
        /// <summary>A visitor beaten for the first time.</summary>
        public bool VisitorBeaten;
        /// <summary>A win inside a sitting that didn't take the rung yet, and the sitting's wins after it.</summary>
        public bool SittingWin;
        public int SittingAfter;
        /// <summary>A loss or a draw that ended a sitting in progress.</summary>
        public bool SittingReset;
        /// <summary>A win against a seat already taken (a rung climbed, a visitor beaten): standing unchanged.</summary>
        public bool Rematch;
        /// <summary>
        /// The events it raised, in order: "ladderrung:&lt;n&gt;", "ladderbeat:&lt;id&gt;", "laddertop", then "level:&lt;n&gt;" for each
        /// level reached. The card unlocks' events (CardUnlocks.AfterDuel) are the caller's, as after any result.
        /// </summary>
        public List<string> Events = new List<string>();
    }

    /// <summary>
    /// The Champion's Ladder (docs/dev/CHAMPIONS-LADDER-SPEC-2026-11-06.md): ten rungs above the Format Library in
    /// campaign/ladder.txt, each holder on their own deck at skill 1.0. Open once the Every Era final is won and its
    /// <see cref="Opener"/> (Duchess Ilse) has been beaten once. You climb one rung at a time; a loss keeps your rung. A
    /// rung with more than one deck is a sitting: every deck in order, a loss or a draw starting it over. Once the
    /// visitors' bracket title is held (the No Limits Invitational), visitors arrive one at a time and take the rung above
    /// yours until you beat them. After the top, any rung or beaten visitor can be played again as a rematch.
    /// Every call takes a <see cref="LadderState"/>, so the ladder needs nothing on the save but that.
    /// </summary>
    public sealed class Ladder
    {
        public const string FileName = "ladder.txt";
        /// <summary>The ledger kind every ladder duel pays under ("ladder", "ladder.loss", "ladder.prize"): Circuits.PayDuel, so Profile.Earn.</summary>
        public const string LedgerKind = "ladder";
        public const string RungPrefix = "rung.", VisitorPrefix = "visitor.", TopKey = "top";
        /// <summary>A first climb or a visitor's first win (<see cref="Library.WinDp"/> + <see cref="Circuits.FirstWinBonus"/>), and every other win (<see cref="Library.RepeatWinDp"/>).</summary>
        public const int FirstClimbDp = Library.WinDp + Circuits.FirstWinBonus, RepeatWinDp = Library.RepeatWinDp;
        /// <summary>Card Tokens with the top's prize.</summary>
        public const int TopTokens = 3;
        /// <summary>The CPU's skill on every seat: full strength, whatever the profile says.</summary>
        public const double Skill = 1.0;

        // ---- Story ----
        /// <summary>The trigger the first campaign-screen Open with the ladder open raises (<see cref="OpenDue"/>), and its scene.</summary>
        public const string OpenEvent = "ladder", OpensScene = "ladder-opens";
        /// <summary>The top holder's introduction (ladderseat:&lt;top&gt;): before it's seen, the board names them "???".</summary>
        public const string HolderScene = "ladder-holder";
        public const string SeatPrefix = "ladderseat:", BeatPrefix = "ladderbeat:", RungEventPrefix = "ladderrung:", TopEvent = "laddertop";
        /// <summary>The scenes the spec requires (section 6); <see cref="Ladder.RequiredScenes"/> pairs each with its trigger.</summary>
        public static readonly IReadOnlyList<(string scene, string trigger)> RequiredScenes = new[]
        {
            (OpensScene, OpenEvent),
            ("ladder-ilse", SeatPrefix + "ilse"),
            ("ladder-holder", SeatPrefix + "ladder-top"),
            ("ladder-visit-junia", SeatPrefix + "junia"),
            ("ladder-ilse-beat", BeatPrefix + "ilse"),
            ("ladder-visit-junia-beat", BeatPrefix + "junia"),
            ("ladder-top", TopEvent),
            ("ladder-top-home", TopEvent),
        };

        /// <summary>The poster the top earns (chunk 1b adds <see cref="MakePoster"/>'s to <see cref="Posters.All"/>): Stairway to a Fabled Realm.</summary>
        public const string PosterId = "ladder";
        public const uint PosterCard = 22555834;

        public IReadOnlyList<LadderSeat> Rungs { get; }
        public IReadOnlyList<LadderSeat> Visitors { get; }
        /// <summary>The character whose first win (with the final won) opens the ladder (null when ladder.txt names nobody known).</summary>
        public Personality? Opener { get; private set; }
        public string OpenerId { get; private set; } = "";
        /// <summary>The top's prize, paid once ever.</summary>
        public int Prize { get; private set; }
        /// <summary>The alley bracket kind whose title brings the visitors ("" for no visitors).</summary>
        public string VisitorsKind { get; private set; } = "";
        /// <summary>A check-only switch: the visitors arrive without the title (the app's -laddercheck, spec 9 app 9).</summary>
        public bool VisitorsForced;
        /// <summary>What the data got wrong: unknown characters, missing decks, bad lines. A seat with a problem is left out.</summary>
        public List<string> Problems { get; } = new List<string>();

        public Ladder(IReadOnlyList<LadderSeat> rungs, IReadOnlyList<LadderSeat> visitors)
        {
            Rungs = rungs;
            Visitors = visitors;
        }

        public static Ladder Empty => new Ladder(new List<LadderSeat>(), new List<LadderSeat>());

        /// <summary>
        /// Reads ladder.txt: "opens:", "prize:", "rung: &lt;character&gt; &lt;deck&gt;[, &lt;deck&gt;...]" from the bottom, "visitors:" and
        /// "visitor: &lt;character&gt; &lt;deck&gt;" (the guest list's grammar). A seat whose character isn't in the roster, or one of whose
        /// decks <paramref name="deckExists"/> says is missing, is skipped and named in <see cref="Problems"/>.
        /// </summary>
        public static Ladder Parse(string text, IEnumerable<Personality> roster, Func<string, bool>? deckExists = null)
        {
            var byId = roster.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
            var rungs = new List<LadderSeat>();
            var visitors = new List<LadderSeat>();
            var problems = new List<string>();
            string opener = "", kind = "";
            int prize = 0;
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
                    case "opens": opener = value; break;
                    case "prize": if (!int.TryParse(value, out prize)) problems.Add($"prize: not a number ({value})"); break;
                    case "visitors": kind = value; break;
                    case "rung":
                    case "visitor":
                    {
                        bool visitor = key == "visitor";
                        string where = visitor ? $"visitor {visitors.Count + 1}" : $"rung {rungs.Count + 1}";
                        var t = value.Trim();
                        var gap = t.IndexOf(' ');
                        var who = gap < 0 ? t : t.Substring(0, gap);
                        var decks = (gap < 0 ? "" : t.Substring(gap + 1)).Split(',').Select(d => d.Trim()).Where(d => d.Length > 0).ToList();
                        if (!byId.TryGetValue(who, out var character)) { problems.Add($"{where}: unknown character \"{who}\" (no personalities/{who}.profile): skipped"); break; }
                        if (decks.Count == 0) { problems.Add($"{where}: {who} has no deck: skipped"); break; }
                        if (visitor && decks.Count > 1) { problems.Add($"{where}: {who} has {decks.Count} decks (a visitor plays one): skipped"); break; }
                        var missing = deckExists == null ? new List<string>() : decks.Where(d => !deckExists(d)).ToList();
                        if (missing.Count > 0) { problems.Add($"{where}: {who}'s deck{(missing.Count > 1 ? "s" : "")} {string.Join(", ", missing)} not found: skipped"); break; }
                        // Keys go by holder: a holder on two rungs (or a visitor twice) would share one.
                        if ((visitor ? visitors : rungs).Any(x => x.Character.Id == who)) { problems.Add($"{where}: {who} is already on the {(visitor ? "visitors' list" : "ladder")}: skipped"); break; }
                        var seat = new LadderSeat { Character = character, Decks = decks, Rung = visitor ? 0 : rungs.Count + 1 };
                        (visitor ? visitors : rungs).Add(seat);
                        break;
                    }
                    default: problems.Add($"unknown line \"{key}:\""); break;
                }
            }
            var ladder = new Ladder(rungs, visitors) { Prize = prize, VisitorsKind = kind, OpenerId = opener };
            ladder.Problems.AddRange(problems);
            if (opener.Length == 0) ladder.Problems.Add("no \"opens:\" line: the ladder never opens");
            else if (byId.TryGetValue(opener, out var o)) ladder.Opener = o;
            else ladder.Problems.Add($"opens: unknown character \"{opener}\": the ladder never opens");
            if (visitors.Count > 0 && kind.Length == 0) ladder.Problems.Add("visitors listed with no \"visitors:\" rule: they never arrive");
            return ladder;
        }

        /// <param name="deckExists">Whether a deck by that name is in the data (null: every deck is taken as there).</param>
        public static Ladder Load(string path, IEnumerable<Personality> roster, Func<string, bool>? deckExists = null) =>
            File.Exists(path) ? Parse(File.ReadAllText(path), roster, deckExists) : Empty;

        // ---- Opening ----------------------------------------------------------------------------------

        /// <summary>
        /// Whether the ladder is open: the Every Era final won (<see cref="Library.FinalCleared"/>) and its opener beaten at
        /// least once anywhere (<see cref="Profile.WinsAgainst"/>, which New Game+ keeps). Closed with no Library loaded.
        /// </summary>
        public bool IsOpen(Profile profile, Library? library) =>
            library != null && Rungs.Count > 0 && Opener != null && library.FinalCleared(profile) && profile.WinsAgainst(Opener.Id) > 0;

        /// <summary>Whether its plaque shows on the Library shelf at all (locked or not): the final won.</summary>
        public bool IsShown(Profile profile, Library? library) => library != null && Rungs.Count > 0 && library.FinalCleared(profile);

        /// <summary>Why it's locked, for the plaque ("" when open): the final first, then the opener's table.</summary>
        public string LockReason(Profile profile, Library? library)
        {
            if (IsOpen(profile, library)) return "";
            if (library == null || !library.FinalCleared(profile))
                return $"Win the {(library != null && library.Final.Name.Length > 0 ? library.Final.Name : StoryPlaces.FinalName)} final";
            return $"Sit across from {Opener?.Name ?? "the opener"} first (Campaign > Guests)";
        }

        /// <summary>
        /// Whether the "ladder" event is due on a campaign-screen Open: the ladder open and its opening scene not seen yet (a
        /// catch-up, as the finale's: a save that opened it long ago gets the scene once).
        /// </summary>
        public bool OpenDue(Profile profile, Library? library) => IsOpen(profile, library) && !profile.Seen.Contains(OpensScene);

        // ---- Standing ---------------------------------------------------------------------------------

        /// <summary>Rungs you hold: the run of rungs taken from the bottom.</summary>
        public int Climbed(LadderState state)
        {
            int n = 0;
            while (n < Rungs.Count && state.WinsAt(Rungs[n].Key) > 0) n++;
            return n;
        }

        public bool HoldsTop(LadderState state) => Rungs.Count > 0 && Climbed(state) == Rungs.Count;

        /// <summary>Whether the top's prize has been paid (the poster's test).</summary>
        public static bool TopReached(LadderState state) => state.WinsAt(TopKey) > 0;

        /// <summary>Whether a seat has been taken (a rung climbed) or beaten (a visitor).</summary>
        public static bool Beaten(LadderState state, LadderSeat seat) => state.WinsAt(seat.Key) > 0;

        /// <summary>
        /// Whether the visitors come: the visitors' bracket title held (Brackets' alley record for <see cref="VisitorsKind"/>,
        /// read without adding one), or <see cref="VisitorsForced"/>.
        /// </summary>
        public bool VisitorsOn(Profile profile) =>
            Visitors.Count > 0 && (VisitorsForced || (VisitorsKind.Length > 0 && profile.AlleyBrackets.TryGetValue(VisitorsKind, out var r) && r.Titles > 0));

        /// <summary>The visitor waiting above your rung: the first not beaten yet, once they come, and never during a sitting.</summary>
        public LadderSeat? ActiveVisitor(Profile profile, LadderState state) =>
            state.Sitting > 0 || !VisitorsOn(profile) ? null : Visitors.FirstOrDefault(v => !Beaten(state, v));

        /// <summary>Your next opponent: the active visitor, else the rung above yours, else nobody (you hold the top). Null when closed.</summary>
        public LadderSeat? Next(Profile profile, LadderState state, Library? library)
        {
            if (!IsOpen(profile, library)) return null;
            var visitor = ActiveVisitor(profile, state);
            if (visitor != null) return visitor;
            int climbed = Climbed(state);
            return climbed < Rungs.Count ? Rungs[climbed] : null;
        }

        /// <summary>
        /// Whether you can duel a seat now: your next opponent, or, once you hold the top, any rung or visitor you've beaten
        /// (a rematch).
        /// </summary>
        public bool CanChallenge(Profile profile, LadderState state, Library? library, LadderSeat seat)
        {
            if (!IsOpen(profile, library)) return false;
            if (ReferenceEquals(Next(profile, state, library), seat)) return true;
            return HoldsTop(state) && Beaten(state, seat);
        }

        /// <summary>
        /// Whether a seat's holder is still a stranger: the top rung before <see cref="HolderScene"/> is seen (and before you've
        /// beaten them). The board shows "???" and a ? tile for them; the versus comes after their entrance, so it names them.
        /// </summary>
        public bool Unmet(Profile profile, LadderSeat seat) =>
            !seat.IsVisitor && Rungs.Count > 0 && ReferenceEquals(seat, Rungs[Rungs.Count - 1]) && !profile.Seen.Contains(HolderScene) && !Beaten(profile.Ladder, seat);

        /// <summary>The seat by its key ("rung.ilse", "visitor.rue"), or null.</summary>
        public LadderSeat? SeatByKey(string key) => Rungs.Concat(Visitors).FirstOrDefault(s => s.Key == key);

        /// <summary>
        /// Which of a seat's decks plays next: in a sitting not won yet, the one the sitting has reached; a rematch on a
        /// sitting's rung goes round its decks by your wins there; any other seat its one deck.
        /// </summary>
        public int DeckIndex(LadderSeat seat, LadderState state)
        {
            if (!seat.IsSitting) return 0;
            int won = state.WinsAt(seat.Key);
            if (won > 0) return (won - 1) % seat.Decks.Count;
            return Math.Max(0, Math.Min(state.Sitting, seat.Decks.Count - 1));
        }

        public string DeckFor(LadderSeat seat, LadderState state) => seat.Decks.Count == 0 ? "" : seat.Decks[DeckIndex(seat, state)];

        /// <summary>A sitting's progress for the board's pips: wins so far and decks in all (0 of 1 for a plain seat).</summary>
        public (int won, int of) SittingProgress(LadderSeat seat, LadderState state) =>
            !seat.IsSitting ? (0, 1) : Beaten(state, seat) ? (seat.Decks.Count, seat.Decks.Count) : (Math.Min(state.Sitting, seat.Decks.Count - 1), seat.Decks.Count);

        /// <summary>The seat's holder as the CPU plays them: a copy at skill 1.0 (the profile itself is untouched).</summary>
        public static Personality Opponent(LadderSeat seat)
        {
            var p = seat.Character.Copy();
            p.Skill = Skill;
            return p;
        }

        /// <summary>The circuit whose rules your deck answers to on the ladder: the last one (the Tier 0 Gauntlet), in every cycle.</summary>
        public static int RulesCircuit(Circuits circuits) => Math.Max(0, circuits.All.Count - 1);

        /// <summary>The ban list name of <see cref="RulesCircuit"/> ("" with no circuits).</summary>
        public static string RulesBanlist(Circuits circuits) => circuits.All.Count == 0 ? "" : circuits.All[RulesCircuit(circuits)].Banlist;

        // ---- Results ----------------------------------------------------------------------------------

        /// <summary>
        /// Records a ladder duel (winner 0 you, 1 them, 2 draw). A win against your next opponent takes their rung
        /// (<see cref="FirstClimbDp"/>; the top's first climb adds <see cref="Prize"/> and <see cref="TopTokens"/>), beats a
        /// visitor (<see cref="FirstClimbDp"/>), or moves a sitting on one deck (<see cref="RepeatWinDp"/>; its last deck takes the
        /// rung); a rematch pays <see cref="RepeatWinDp"/>. A loss or a draw keeps your rung, ends a sitting and pays
        /// <see cref="Circuits.LossDp"/> / <see cref="Circuits.DrawDp"/>. DP through <see cref="Circuits.PayDuel"/> as
        /// <see cref="LedgerKind"/>; EXP as a Library duel, its level-ups paying DP only on a first climb or a visitor's first
        /// win (Card Tokens otherwise). Null (nothing recorded) for a win against a seat you can't challenge now.
        /// </summary>
        public LadderResult? Record(Profile profile, LadderState state, Library library, LadderSeat seat, int winner)
        {
            if (winner == 0 && !CanChallenge(profile, state, library, seat)) return null;
            var result = new LadderResult { Seat = seat };
            var reward = result.Reward;
            var key = seat.Key;
            bool wonBefore = Beaten(state, seat);
            int prize = 0;
            var parts = new List<string>();
            var name = seat.Character.Name;
            if (winner == 0)
            {
                if (wonBefore)
                {
                    result.Rematch = true;
                    reward.Dp = RepeatWinDp;
                    state.Wins[key] = state.WinsAt(key) + 1;
                    parts.Add($"Rematch +{reward.Dp} DP");
                }
                else if (seat.IsVisitor)
                {
                    reward.FirstWin = result.VisitorBeaten = true;
                    reward.Dp = FirstClimbDp;
                    state.Wins[key] = 1;
                    parts.Add($"{name} +{reward.Dp} DP (first win)");
                    result.Events.Add(BeatPrefix + seat.Character.Id);
                }
                else if (seat.IsSitting && state.Sitting + 1 < seat.Decks.Count)
                {
                    result.SittingWin = true;
                    state.Sitting++;
                    result.SittingAfter = state.Sitting;
                    reward.Dp = RepeatWinDp;
                    parts.Add($"Rung {seat.Rung}: {state.Sitting} of {seat.Decks.Count} +{reward.Dp} DP");
                }
                else
                {
                    if (seat.IsSitting) state.Sitting = 0;
                    reward.FirstWin = true;
                    reward.Dp = FirstClimbDp;
                    state.Wins[key] = 1;
                    result.RungTaken = seat.Rung;
                    parts.Add($"Rung {seat.Rung} +{reward.Dp} DP (first climb)");
                    result.Events.Add(RungEventPrefix + seat.Rung);
                    result.Events.Add(BeatPrefix + seat.Character.Id);
                    if (HoldsTop(state) && !TopReached(state))
                    {
                        state.Wins[TopKey] = 1;
                        result.TopReached = true;
                        prize = Prize;
                        reward.Dp += prize;
                        reward.Tokens += TopTokens;
                        parts.Add($"The top! +{prize:N0} DP prize, +{TopTokens} Card Tokens, the {PosterTitle} poster");
                        result.Events.Add(TopEvent);
                    }
                }
                profile.AddWin(seat.Character.Id, reward);
                profile.Tokens += reward.Tokens;
            }
            else
            {
                reward.Dp = winner == 2 ? Circuits.DrawDp : Circuits.LossDp;
                if (winner == 1) profile.Losses[seat.Character.Id] = profile.Losses.TryGetValue(seat.Character.Id, out var l) ? l + 1 : 1;
                // A loss or a draw ends a sitting in progress (the next starts again at its first deck).
                if (seat.IsSitting && !wonBefore && state.Sitting > 0) { state.Sitting = 0; result.SittingReset = true; }
                parts.Add($"+{reward.Dp} DP for the duel" + (result.SittingReset ? $" · the sitting starts over at {DeckFor(seat, state)}" : ""));
            }
            Circuits.PayDuel(profile, LedgerKind, winner, reward.Dp, prize);
            profile.NoteResult(LedgerKind + ":" + key, winner);
            reward.Exp = Leveling.DuelExp(library.ExpCircuit, winner, reward.FirstWin);
            reward.LevelUps = Leveling.AddExp(profile, reward.Exp, payDp: reward.FirstWin);
            reward.Summary = string.Join(" · ", parts);
            foreach (var up in reward.LevelUps)
                reward.Summary += " · " + Leveling.Describe(up);
            result.Events.AddRange(reward.LevelUps.Select(u => "level:" + u.Level));
            return result;
        }

        /// <summary>
        /// A ladder duel left live (the app closed mid-duel), settled as the loss it counts as: rung kept, a sitting ended,
        /// <see cref="Circuits.LossDp"/> paid as "ladder.loss". <paramref name="seatKey"/> is the seat's <see cref="LadderSeat.Key"/>
        /// (what chunk 1b keeps in the LiveDuel); null when the data no longer has that seat.
        /// </summary>
        public LadderResult? SettleLoss(Profile profile, LadderState state, Library library, string seatKey) =>
            SeatByKey(seatKey) is LadderSeat seat ? Record(profile, state, library, seat, 1) : null;

        /// <summary>
        /// A live ladder duel settled as a plain loss when there's no ladder to settle it on (none loaded, or a seat the data no
        /// longer has): the holder's id read from <paramref name="seatKey"/>, <see cref="Circuits.LossDp"/> as "ladder.loss", the
        /// loss counted, a sitting in progress ended (a sitting is the only seat you can duel while one runs), EXP with no level
        /// DP. Nothing else moves. LiveDuels.Settle uses it, so a live ladder duel is never dropped unpaid.
        /// </summary>
        public static LadderResult PlainLoss(Profile profile, LadderState state, Library library, string seatKey)
        {
            var dot = seatKey.IndexOf('.');
            var id = dot >= 0 ? seatKey.Substring(dot + 1) : seatKey;
            var who = new Personality { Id = id, Name = id };
            var result = new LadderResult { Seat = new LadderSeat { Character = who, Rung = seatKey.StartsWith(RungPrefix) ? 1 : 0 } };
            var reward = result.Reward;
            reward.Dp = Circuits.LossDp;
            if (id.Length > 0) profile.Losses[id] = profile.Losses.TryGetValue(id, out var l) ? l + 1 : 1;
            if (state.Sitting > 0) { state.Sitting = 0; result.SittingReset = true; }
            Circuits.PayDuel(profile, LedgerKind, 1, reward.Dp);
            profile.NoteResult(LedgerKind + ":" + seatKey, 1);
            reward.Exp = Leveling.DuelExp(library.ExpCircuit, 1, false);
            reward.LevelUps = Leveling.AddExp(profile, reward.Exp, payDp: false);
            reward.Summary = $"+{reward.Dp} DP for the duel" + string.Concat(reward.LevelUps.Select(up => " · " + Leveling.Describe(up)));
            result.Events.AddRange(reward.LevelUps.Select(u => "level:" + u.Level));
            return result;
        }

        /// <summary>The events a result raised (<see cref="LadderResult.Events"/>).</summary>
        public static List<string> Events(LadderResult result) => result.Events.ToList();

        /// <summary>
        /// The events Duel pressed against a seat raises before the versus ("ladderseat:&lt;id&gt;", once a seat: Seen keeps its
        /// scene once). None for a seat already taken or beaten, so a rematch never raises it.
        /// </summary>
        public static List<string> SeatEvents(LadderState state, LadderSeat seat) =>
            Beaten(state, seat) ? new List<string>() : new List<string> { SeatPrefix + seat.Character.Id };

        /// <summary>
        /// Whether a story trigger is one of the ladder's: "ladder", "laddertop", "ladderseat:"/"ladderbeat:" a seat's
        /// character, "ladderrung:" a rung's number (CampaignTest's TriggerOk calls this in chunk 1b).
        /// </summary>
        public bool IsTrigger(string trigger)
        {
            if (trigger == OpenEvent || trigger == TopEvent) return true;
            bool Seat(string id) => Rungs.Concat(Visitors).Any(s => s.Character.Id == id);
            if (trigger.StartsWith(SeatPrefix)) return Seat(trigger.Substring(SeatPrefix.Length));
            if (trigger.StartsWith(BeatPrefix)) return Seat(trigger.Substring(BeatPrefix.Length));
            if (trigger.StartsWith(RungEventPrefix)) return int.TryParse(trigger.Substring(RungEventPrefix.Length), out var n) && n >= 1 && n <= Rungs.Count;
            return false;
        }

        // ---- The poster -------------------------------------------------------------------------------

        public const string PosterTitle = "Top Rung";

        /// <summary>
        /// The top's poster for <see cref="Posters.All"/> (chunk 1b): earned once the top's prize is paid. <paramref name="stateOf"/>
        /// reads the save's ladder state (Profile.Ladder in 1b; null reads as unclimbed).
        /// </summary>
        public static Poster MakePoster(Func<Profile, LadderState?> stateOf) => new Poster
        {
            Id = PosterId, Title = PosterTitle, Tagline = "Past the binders · the next rung is the only one", Card = PosterCard, Ink = "#7F8A99",
            Unlock = "Reach the top of the Champion's Ladder", Earned = (c, p) => stateOf(p) is LadderState s && TopReached(s),
        };
    }
}
