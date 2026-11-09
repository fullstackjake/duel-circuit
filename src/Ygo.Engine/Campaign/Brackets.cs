using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Ygo.Engine.Campaign
{
    /// <summary>One seat in a bracket: a CPU (character id and deck) or you.</summary>
    public sealed class BracketEntrant
    {
        public const string YouId = "you";
        /// <summary>The character's id ("you" for you; "alley-regular" for an alley bracket's splash regular).</summary>
        public string Character = "";
        /// <summary>The CPU's deck (yours: the deck you entered with; you may bring another each round; a splash regular's: "alley:bracket.&lt;slot&gt;").</summary>
        public string Deck = "";
        /// <summary>Its rating (deck-ratings.txt; an unrated CPU deck counts as 1,500). Yours: your deck's, or 0 when unrated. A splash regular: its base's plus its table's splash gain.</summary>
        public double Elo;
        public bool You;
        /// <summary>1 to the bracket's size (Locals: you are 1, the cup's champion).</summary>
        public int Seed;
        /// <summary>
        /// An alley bracket's splash regular (null otherwise): its base, adds, cuts, handle and tint as built (saved,
        /// "bracketsplash"), Table "bracket", Seat its slot, From the table it was drawn from. <see cref="BackAlley.ResolveDeck"/>
        /// rebuilds its deck from them; <see cref="BackAlley.BracketOpponent"/> gives it with its skill.
        /// </summary>
        public AlleyOpponent? Splash;
    }

    /// <summary>
    /// A single-elimination bracket of 8 (BRACKETS-SPEC-2026-11-01) or 16 (the Midnight Open, docs/dev/ALLEY-SPEC-2026-11-01.md
    /// section 11). <see cref="Slots"/> are in bracket order (8: seeds 1, 8, 4, 5 | 2, 7, 3, 6; 16: <see cref="Brackets.Order16"/>),
    /// so round 0's matches pair slots (2m, 2m+1) and each later match pairs the winners of the two matches feeding it: at 8,
    /// matches 0-3 are the quarter-finals, 4-5 the semi-finals, 6 the final; at 16, 0-7 the round of 16, 8-11 the
    /// quarter-finals, 12-13 the semi-finals, 14 the final. The final may be best of three (<see cref="FinalBestOf"/>). Every
    /// CPU match's coin is fixed by <see cref="Seed"/>. Make one with <c>new Bracket(size)</c> (the arrays follow the size).
    /// </summary>
    public sealed partial class Bracket
    {
        public const int LocalsSize = 8;

        /// <summary>8 or 16 (set by the constructor; a save's "bracketsize" line resizes).</summary>
        public int Size = LocalsSize;
        public int MatchCount => Size - 1;
        /// <summary>The final's match index.</summary>
        public int Final => Size - 2;
        /// <summary>log2(Size): 3 at 8, 4 at 16.</summary>
        public int Rounds
        {
            get
            {
                int r = 0;
                for (int n = Size; n > 1; n >>= 1) r++;
                return r;
            }
        }

        /// <summary>The final's length: 1, or 3 (the Midnight Open: decided at 2 games).</summary>
        public int FinalBestOf = 1;
        /// <summary>A best-of-three final's games won by side A and side B (<see cref="Pair"/> of <see cref="Final"/>).</summary>
        public int FinalGamesA, FinalGamesB;
        /// <summary>The side (0 A, 1 B) that lost the final's last game (it chooses who goes first in the next), or -1.</summary>
        public int FinalLastLoser = -1;

        public string Kind = Brackets.LocalsKind;
        /// <summary>The event: a Library cup's id ("goat-cup"); an alley bracket's "alley".</summary>
        public string Event = "";
        /// <summary>"Goat Cup Locals", "Alley Brawl", "Midnight Open".</summary>
        public string Name = "";
        public uint Seed;
        public BracketEntrant[] Slots;
        /// <summary>Each match's winner (a slot index), or -1 while it's unplayed.</summary>
        public int[] Winners;
        /// <summary>You played a round with a loaner: placement DP is halved.</summary>
        public bool Loaned;
        /// <summary>Your match being dueled now (saved at its start), or -1. A save loaded with it set replays it.</summary>
        public int LiveMatch = -1;
        /// <summary>Duels started at your current match (each gets its own duel seed: a draw, a reload or the next game of a final replays it fresh).</summary>
        public int Attempt;

        public Bracket() : this(LocalsSize) { }

        public Bracket(int size)
        {
            Size = size;
            Slots = new BracketEntrant[size];
            Winners = Enumerable.Repeat(-1, size - 1).ToArray();
        }

        /// <summary>Changes the size, keeping the slots and results that fit (a save's "bracketsize" line).</summary>
        internal void Resize(int size)
        {
            Size = size;
            Array.Resize(ref Slots, size);
            var winners = Enumerable.Repeat(-1, size - 1).ToArray();
            Array.Copy(Winners, winners, Math.Min(Winners.Length, winners.Length));
            Winners = winners;
        }

        /// <summary>Round <paramref name="round"/>'s first match (Rounds: the match count).</summary>
        private int Start(int round) => Size - (Size >> round);

        /// <summary>0 the first round ... Rounds - 1 the final.</summary>
        public int RoundOf(int m)
        {
            for (int r = 0; r < Rounds; r++)
                if (m < Start(r + 1)) return r;
            return Rounds - 1;
        }

        public IEnumerable<int> MatchesIn(int round) =>
            round < 0 || round >= Rounds ? Enumerable.Empty<int>() : Enumerable.Range(Start(round), Start(round + 1) - Start(round));

        /// <summary>"Round of 16", "Quarter-final", "Semi-final", "Final" (counted back from the final).</summary>
        public string RoundName(int round) => (Rounds - 1 - round) switch
        {
            <= 0 => "Final",
            1 => "Semi-final",
            2 => "Quarter-final",
            _ => "Round of 16",
        };

        /// <summary>Games a side needs to take the final (1, or 2 in a best of three).</summary>
        public int GamesToWin => FinalBestOf / 2 + 1;

        /// <summary>A match's two slots (side A, side B); -1 for a side not decided yet.</summary>
        public (int a, int b) Pair(int m)
        {
            int round = RoundOf(m);
            if (round == 0) return (2 * m, 2 * m + 1);
            int feed = Start(round - 1) + 2 * (m - Start(round));
            return (Winners[feed], Winners[feed + 1]);
        }

        public int YourSlot => Array.FindIndex(Slots, s => s != null && s.You);

        /// <summary>Both sides known and no winner yet.</summary>
        public bool Pending(int m)
        {
            var (a, b) = Pair(m);
            return a >= 0 && b >= 0 && Winners[m] < 0;
        }

        public bool IsYours(int m)
        {
            var (a, b) = Pair(m);
            int you = YourSlot;
            return you >= 0 && (a == you || b == you);
        }

        /// <summary>Your match to play now, or -1 (you're out, or the bracket is over).</summary>
        public int YourMatch()
        {
            for (int m = 0; m < MatchCount; m++)
                if (IsYours(m) && Pending(m)) return m;
            return -1;
        }

        /// <summary>The slot you meet at match <paramref name="m"/> (-1 when it isn't yours or isn't known).</summary>
        public int Opponent(int m)
        {
            var (a, b) = Pair(m);
            int you = YourSlot;
            return a == you ? b : b == you ? a : -1;
        }

        public bool YouOut => Placement() > 0;

        public bool Done => Winners[Final] >= 0;

        /// <summary>The champion's slot, or -1.</summary>
        public int Champion => Winners[Final];

        /// <summary>Your placement: 0 winner, 1 finalist, 2 semi-final, 3 quarter-final, 4 round of 16; -1 while you're still in.</summary>
        public int Placement()
        {
            int you = YourSlot;
            if (you < 0) return -1;
            if (Winners[Final] == you) return 0;
            for (int m = 0; m < MatchCount; m++)
            {
                var (a, b) = Pair(m);
                if ((a == you || b == you) && Winners[m] >= 0 && Winners[m] != you) return Rounds - RoundOf(m);
            }
            return -1;
        }

        /// <summary>The round being played: the lowest with an unplayed match (<see cref="Rounds"/> once done).</summary>
        public int CurrentRound()
        {
            for (int m = 0; m < MatchCount; m++)
                if (Winners[m] < 0) return RoundOf(m);
            return Rounds;
        }
    }

    /// <summary>A Locals bracket's history at one cup (saved as "locals"), or an alley bracket's by kind ("alleybracket").</summary>
    public sealed class LocalsRecord
    {
        public int Entries, Titles;
        /// <summary>The best placement reached (0 a title ... 3 out in the quarter-finals, 4 in the round of 16), or -1 before any bracket finished.</summary>
        public int Best = -1;
    }

    /// <summary>What your bracket duel's result did (<see cref="Brackets.RecordYours"/>).</summary>
    public sealed class BracketResult
    {
        /// <summary>EXP, level-ups and the win counted (<see cref="DuelReward.WinsAdded"/>: a Locals win only). Dp: the placement paid now (a loss), else 0.</summary>
        public DuelReward Reward = new DuelReward();
        /// <summary>A draw: nothing changed, play the match again (in a best-of-three final, the game again).</summary>
        public bool Replay;
        /// <summary>A best-of-three final's game just recorded (1-3; 0 for any other match), and whether the series goes on to another game.</summary>
        public int Game;
        public bool NextGame;
        /// <summary>You lost: out, at <see cref="Placement"/> (1-4).</summary>
        public bool Out;
        public int Placement = -1;
        /// <summary>The round's other matches settled now, in order (the bracket animates them).</summary>
        public List<int> Settled = new List<int>();
        /// <summary>Story events: "level:n".</summary>
        public List<string> Events = new List<string>();
    }

    /// <summary>
    /// Whether an alley bracket can be entered (<see cref="Brackets.Opening"/>): open, "Coming soon" (its field's peak builds
    /// aren't all ready: <see cref="NotReady"/> names them), or locked (<see cref="Why"/>: "Clear No Limits to unlock").
    /// </summary>
    public sealed class BracketOpening
    {
        public bool Open;
        /// <summary>The data isn't ready (whatever you've cleared): the chalkboard shows "Coming soon", as the No Limits table does.</summary>
        public bool ComingSoon;
        /// <summary>"" when open; else what the chalkboard says ("Coming soon", "Clear No Limits to unlock", "The alley isn't open.").</summary>
        public string Why = "";
        /// <summary>The field's builds that aren't `ready: yes` (ids), when <see cref="ComingSoon"/>.</summary>
        public List<string> NotReady = new List<string>();
    }

    /// <summary>What a finished bracket paid (<see cref="Brackets.Finish"/>).</summary>
    public sealed class BracketReward
    {
        public string Name = "";
        public int Placement;
        /// <summary>DP paid now (a title's; a loss's placement was paid when you lost).</summary>
        public int Dp;
        /// <summary>Prize Packs (an alley bracket's: Contraband Packs; the Invitational's: No Limits Packs) won, and each one's pulls (revealed least rare first) when they were opened.</summary>
        public int PacksWon;
        public List<List<PrizePull>> Packs = new List<List<PrizePull>>();
        /// <summary>Card Tokens paid now (the Midnight Open's first title: <see cref="AlleyEconomy.OpenTokens"/>; the Invitational's: <see cref="AlleyEconomy.InvitationalTokens"/>).</summary>
        public int Tokens;
        /// <summary>
        /// Of <see cref="PacksWon"/>, the No Limits Packs owed instead of opened (<see cref="Brackets.SettleHeld"/>: the pool can't fill
        /// a pack): in <see cref="Profile.NoLimitsPacksOwed"/> until <see cref="Brackets.PayOwedPacks"/> opens them. 0 otherwise.
        /// </summary>
        public int PacksOwed;
        /// <summary>No Limits Packs an earlier finish owed, opened with this one (at the end of <see cref="Packs"/>).</summary>
        public int OwedPaid;
        /// <summary>"bracketwin" (the first Locals title) and "bracketwin:&lt;name&gt;" (every title; an alley title raises only this).</summary>
        public List<string> Events = new List<string>();
        public string Summary = "";
    }

    /// <summary>
    /// Brackets (POST-GAME-PLAN step 7; BRACKETS-SPEC-2026-11-01). Library Locals: once a cup is cleared you can pay 1,000 DP
    /// to enter its 8-player Locals as seed 1, against the cup's own decks and three locals (library.txt's "local:" lines).
    /// The Back Alley's three (docs/dev/ALLEY-SPEC-2026-11-01.md section 11; docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 9): the
    /// Alley Brawl (8, splash regulars), the Midnight Open (16, a best-of-three final) and the No Limits Invitational (16, Bo3,
    /// No Limits rules), entered from Baz's chalkboard. Your matches are real duels under the event's rules
    /// (<see cref="RulesFor"/>); the CPUs' are settled by a coin fixed at entry against their odds. They pay by placement, a
    /// title and a final also in packs (Prize Packs; the alley's, Contraband Packs; the Invitational's, No Limits Packs: cards,
    /// never DP): DP sinks. One bracket runs at a
    /// time, of any kind (<see cref="Profile.Bracket"/>); New Game+ keeps it and the history.
    /// </summary>
    public static partial class Brackets
    {
        public const string LocalsKind = "locals", Sink = Ledger.Brackets;
        /// <summary>The alley's kinds (and their story events: the first entry to each).</summary>
        public const string BrawlKind = "brawl", MidnightKind = "midnight";
        /// <summary>The No Limits Invitational (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 9): No Limits rules, No Limits Packs, the nolimits ledger.</summary>
        public const string InvitationalKind = "invitational";
        /// <summary>An alley bracket's <see cref="Bracket.Event"/>.</summary>
        public const string AlleyEvent = "alley";
        /// <summary>The Midnight Open's token is paid once (a <see cref="Profile.Seen"/> key, kept by New Game+).</summary>
        public const string OpenTokenKey = "midnight:token";
        /// <summary>The Invitational's tokens are paid once, on its first title (a <see cref="Profile.Seen"/> key, kept by New Game+).</summary>
        public const string InvitationalTokenKey = "invitational:token";
        /// <summary>The alley's kinds, in <see cref="AlleySeed"/>'s salt order (brawl 0, midnight 1, invitational 2: a kind's salt never moves).</summary>
        public static readonly string[] AlleyKinds = { BrawlKind, MidnightKind, InvitationalKind };
        public const int LocalsEntry = 1000;
        /// <summary>DP by placement (winner, finalist, semi-final, quarter-final) [placeholder], halved when a loaner played.</summary>
        public static readonly int[] LocalsPay = { 900, 650, 400, 150 };
        /// <summary>Prize Packs by placement.</summary>
        public static readonly int[] LocalsPacks = { 2, 1, 0, 0 };
        public const int LocalsPerCup = 3;
        /// <summary>Seeds in bracket order: 1 and 2 meet only in the final.</summary>
        public static readonly int[] Order = { 1, 8, 4, 5, 2, 7, 3, 6 };
        /// <summary>Seeds in bracket order at 16: 1 and 2 meet only in the final.</summary>
        public static int[] Order16 { get; } = { 1, 16, 8, 9, 4, 13, 5, 12, 2, 15, 7, 10, 3, 14, 6, 11 };
        public const double MinOdds = 0.03, MaxOdds = 0.97;
        /// <summary>Head-to-head results count against this many duels' worth of the Elo odds.</summary>
        public const int MatchupPrior = 20;
        /// <summary>Story events: the first entry, and the first title anywhere (its own scene; "bracketwin:&lt;name&gt;" per bracket).</summary>
        public const string EntryEvent = "locals", WinEvent = "bracketwin";

        public static string NameOf(Circuit cup) => cup.Name + " Locals";

        /// <summary>Your match's key for <see cref="Profile.NoteResult"/>: "locals:goat-cup.4", "midnight:alley.14".</summary>
        public static string Key(Bracket b, int m) => $"{b.Kind}:{b.Event}.{m}";

        /// <summary>Seeds in bracket order for a size (8 or 16).</summary>
        public static int[] OrderFor(int size) => size == 16 ? Order16 : Order;

        /// <summary>An alley bracket (the Alley Brawl, the Midnight Open or the No Limits Invitational): its own pay, ledger, packs and records.</summary>
        public static bool IsAlley(Bracket b) => IsAlleyKind(b.Kind);

        public static bool IsAlleyKind(string kind) => kind == BrawlKind || kind == MidnightKind || kind == InvitationalKind;

        /// <summary>A kind's entry fee, pay and packs by placement (the alley's from <see cref="AlleyEconomy"/>).</summary>
        public static int EntryFor(string kind) => kind switch
        {
            BrawlKind => AlleyEconomy.BrawlEntry,
            MidnightKind => AlleyEconomy.OpenEntry,
            InvitationalKind => AlleyEconomy.InvitationalEntry,
            _ => LocalsEntry,
        };

        public static int[] PayFor(string kind) => kind switch
        {
            BrawlKind => AlleyEconomy.BrawlPay,
            MidnightKind => AlleyEconomy.OpenPay,
            InvitationalKind => AlleyEconomy.InvitationalPay,
            _ => LocalsPay,
        };

        /// <summary>Packs by placement: Prize Packs (Locals), Contraband Packs (the Brawl, the Open), No Limits Packs (the Invitational).</summary>
        public static int[] PacksFor(string kind) => kind switch
        {
            BrawlKind => AlleyEconomy.BrawlPacks,
            MidnightKind => AlleyEconomy.OpenPacks,
            InvitationalKind => AlleyEconomy.InvitationalPacks,
            _ => LocalsPacks,
        };

        /// <summary>Card Tokens a kind pays on its first title (the Open's and the Invitational's; 0 for the others).</summary>
        public static int TokensFor(string kind) => kind switch
        {
            MidnightKind => AlleyEconomy.OpenTokens,
            InvitationalKind => AlleyEconomy.InvitationalTokens,
            _ => 0,
        };

        /// <summary>The <see cref="Profile.Seen"/> key that marks a kind's first-title tokens paid ("" for a kind that pays none).</summary>
        public static string TokenKeyFor(string kind) => kind switch
        {
            MidnightKind => OpenTokenKey,
            InvitationalKind => InvitationalTokenKey,
            _ => "",
        };

        /// <summary>A kind's prize pack, as the screens name it.</summary>
        public static string PackNameFor(string kind) => kind == InvitationalKind ? "No Limits Pack" : IsAlleyKind(kind) ? "Contraband Pack" : "Prize Pack";

        /// <summary>Where a kind's placement pay is earned: Locals' "locals", the alley's "alley", the Invitational's "nolimits".</summary>
        public static string PayLedger(string kind) => kind == InvitationalKind ? Ledger.NoLimits : IsAlleyKind(kind) ? Ledger.Alley : Ledger.Locals;

        /// <summary>Where a kind's entry is spent: Locals' "brackets", the alley's "alley.brackets", the Invitational's "nolimits.entry".</summary>
        public static string EntryLedger(string kind) => kind == InvitationalKind ? Ledger.NoLimitsEntry : IsAlleyKind(kind) ? Ledger.AlleyBrackets : Sink;

        /// <summary>
        /// Your rules in a bracket of this kind, every round: the No Limits rules at the Invitational (<see cref="Alley.NoLimitsList"/>:
        /// the exclusions at 0, three of anything; built from the cards when <see cref="AlleyContext.NoLimits"/> is null), the
        /// Alley's at the other alley brackets. (Locals use their cup's rules, <see cref="Library.Rules"/>.) No loaners, the turn
        /// cap: the caller's, as at the tables.
        /// </summary>
        public static FormatRules RulesFor(string kind, AlleyContext ctx)
        {
            if (kind != InvitationalKind) return Alley.Rules(ctx.List);
            ctx.NoLimits ??= Alley.NoLimitsList(ctx.Cards);
            return Alley.Rules(ctx.NoLimits);
        }

        // ---- The field ------------------------------------------------------------------------------

        /// <summary>A cup's 7 CPUs (its matches, the Champion's table included, then its locals), seeded 2-8 by Elo, highest first.</summary>
        public static List<BracketEntrant> LocalsField(Circuit cup, IReadOnlyDictionary<string, double> ratings)
        {
            var field = cup.Matches.Concat(cup.Locals)
                .Select(m => new BracketEntrant { Character = m.Character.Id, Deck = m.Deck, Elo = DeckRatings.Of(ratings, m.Deck) })
                .OrderByDescending(e => e.Elo).ThenBy(e => e.Deck, StringComparer.Ordinal).ThenBy(e => e.Character, StringComparer.Ordinal).ToList();
            for (int i = 0; i < field.Count; i++) field[i].Seed = i + 2;
            return field;
        }

        public static List<BracketEntrant> LocalsField(Library library, int cup, IReadOnlyDictionary<string, double> ratings) =>
            LocalsField(library.Cups[cup], ratings);

        /// <summary>A cup's bracket from a seed: you as seed 1 (with <paramref name="yourDeck"/>), the field as seeds 2-8, in bracket order.</summary>
        public static Bracket Create(Circuit cup, IReadOnlyDictionary<string, double> ratings, uint seed, string yourDeck = "")
        {
            var field = LocalsField(cup, ratings);
            if (field.Count != Bracket.LocalsSize - 1) throw new InvalidOperationException($"{cup.Name}: a Locals field needs 7 CPUs, not {field.Count}");
            var you = new BracketEntrant
            {
                Character = BracketEntrant.YouId, Deck = yourDeck, You = true, Seed = 1,
                Elo = ratings.TryGetValue(Loaners.RecipeOf(yourDeck), out var elo) ? elo : 0,
            };
            var bySeed = field.Prepend(you).ToDictionary(e => e.Seed);
            var b = new Bracket { Kind = LocalsKind, Event = Library.Id(cup), Name = NameOf(cup), Seed = seed };
            for (int i = 0; i < Bracket.LocalsSize; i++) b.Slots[i] = bySeed[Order[i]];
            return b;
        }

        public static Bracket Create(Library library, int cup, IReadOnlyDictionary<string, double> ratings, uint seed, string yourDeck = "") =>
            Create(library.Cups[cup], ratings, seed, yourDeck);

        /// <summary>
        /// An alley bracket from a seed (alley-events.txt's "bracket:" block of that kind): you at your field line's seed (the
        /// Brawl: 1; the Midnight Open: 2, so you meet Ilara Gale, seed 1, only in the final), the field
        /// (<see cref="BackAlley.BracketField"/>: the Brawl's splash regulars from <see cref="BackAlley.BrawlTable"/>, the
        /// Open's named entrants and Black Market regulars) seeded by Elo, in bracket order. A splash regular's slot gets its
        /// deck key "alley:bracket.&lt;slot&gt;". Throws when the data has no such bracket or its field doesn't fill it.
        /// </summary>
        public static Bracket CreateAlley(AlleyData data, string kind, Profile profile, AlleyContext ctx, uint seed, string yourDeck = "")
        {
            ctx.Data ??= data;
            var spec = data.Brackets.FirstOrDefault(x => x.Kind == kind) ?? throw new InvalidOperationException($"no alley bracket of kind {kind}");
            var (field, yourSeed) = BackAlley.BracketField(profile, data, spec, ctx, seed);
            if (field.Count != spec.Size - 1) throw new InvalidOperationException($"{spec.Name}: its field has {field.Count} CPUs, not {spec.Size - 1}");
            var you = new BracketEntrant
            {
                Character = BracketEntrant.YouId, Deck = yourDeck, You = true, Seed = yourSeed,
                Elo = ctx.Ratings.TryGetValue(Loaners.RecipeOf(yourDeck), out var elo) ? elo : 0,
            };
            var bySeed = field.Prepend(you).ToDictionary(e => e.Seed);
            var order = OrderFor(spec.Size);
            var b = new Bracket(spec.Size) { Kind = kind, Event = AlleyEvent, Name = spec.Name, Seed = seed, FinalBestOf = spec.FinalBestOf };
            for (int i = 0; i < spec.Size; i++)
            {
                var e = b.Slots[i] = bySeed[order[i]];
                if (e.Splash == null) continue;
                e.Splash.Seat = i;
                e.Deck = e.Splash.DeckKey;
            }
            return b;
        }

        // ---- Entry ----------------------------------------------------------------------------------

        /// <summary>The bracket running (null for none).</summary>
        public static Bracket? Running(Profile profile) => profile.Bracket;

        /// <summary>Whether this cup's bracket is the one running.</summary>
        public static bool RunningAt(Profile profile, Library library, int cup) =>
            profile.Bracket != null && cup >= 0 && cup < library.Cups.Count && profile.Bracket.Event == Library.Id(library.Cups[cup]);

        /// <summary>Why you can't enter a cup's Locals now (null: you can). The cup panel's button reads it.</summary>
        public static string? EnterBlocked(Profile profile, Library library, int cup)
        {
            if (cup < 0 || cup >= library.Cups.Count) return "No such cup.";
            var c = library.Cups[cup];
            if (c.Locals.Count != LocalsPerCup) return "No Locals at this cup yet.";
            if (!library.IsCleared(cup, profile)) return "Clear to unlock";
            if (profile.Bracket != null)
                return RunningAt(profile, library, cup) ? $"Your {profile.Bracket.Name} bracket is running." : $"{profile.Bracket.Name} running";
            if (profile.Dp < LocalsEntry) return $"Entry is {LocalsEntry:N0} DP; you have {profile.Dp:N0}.";
            return null;
        }

        /// <summary>
        /// Enters a cup's Locals: takes the 1,000 DP fee (a sink, never refunded) and makes the bracket from the save's salt and
        /// your entry count, so a reload gets the same bracket. Null when <see cref="EnterBlocked"/>. Save right after; raise
        /// <see cref="EntryEvent"/> (its scene shows once).
        /// </summary>
        public static Bracket? EnterLocals(Profile profile, Library library, int cup, IReadOnlyDictionary<string, double> ratings, string yourDeck = "")
        {
            if (EnterBlocked(profile, library, cup) != null || !profile.Spend(Sink, LocalsEntry)) return null;
            uint seed = Hash.Mix(profile.Collection.Salt, 0xB7ACu ^ (uint)cup, (uint)profile.BracketsEntered++);
            var b = Create(library, cup, ratings, seed, yourDeck);
            profile.Bracket = b;
            Record(profile, b.Event).Entries++;
            return b;
        }

        /// <summary>
        /// Why you can't enter an alley bracket now (null: you can): <see cref="Opening"/>'s reason (the alley shut, its field not
        /// ready: "Coming soon", its `opens:` table not cleared: the Brawl: Dumpster Row; the Midnight Open: The Black Market; the
        /// Invitational: No Limits), a bracket running, or the entry over your DP. Baz's chalkboard reads it.
        /// </summary>
        public static string? EnterBlocked(Profile profile, AlleyData data, string kind)
        {
            var opening = Opening(profile, data, kind);
            if (!opening.Open) return opening.Why;
            if (profile.Bracket != null)
                return profile.Bracket.Kind == kind ? $"Your {profile.Bracket.Name} bracket is running." : $"{profile.Bracket.Name} running";
            int entry = EntryFor(kind);
            if (profile.Dp < entry) return $"Entry is {entry:N0} DP; you have {profile.Dp:N0}.";
            return null;
        }

        /// <summary>
        /// The peak builds a bracket's field seats on their own No Limits decks (a field line naming a build's pilot and its
        /// `deck:`; a pilot's line must name a deck: <see cref="AlleyData.Problems"/>): the Invitational's eleven. The Brawl's out-of-towners play their Brawl decks, so they aren't these.
        /// </summary>
        public static List<Headliner> FieldBuilds(AlleyData data, AlleyBracketSpec spec)
        {
            var builds = new List<Headliner>();
            foreach (var line in spec.Field)
            {
                var (w, _) = AlleyBracketSpec.ParseField(line);
                if (w.Length == 0 || w[0] is "you" or "splash") continue;
                var h = data.FindBuild(w[0]);
                // Only a line that names the build's own deck: a pilot's line without a deck is a data problem (AlleyData.Problems), never a field build.
                if (h != null && w.Length > 1 && w[1] == h.Deck && !builds.Contains(h)) builds.Add(h);
            }
            return builds;
        }

        /// <summary>
        /// Whether a bracket's field can be seated: every peak build it names (<see cref="FieldBuilds"/>) is `ready: yes`. A
        /// bracket whose field isn't ready is "Coming soon" whatever you've cleared (the Invitational until wave 2 ships).
        /// </summary>
        public static bool FieldReady(AlleyData data, AlleyBracketSpec spec) => FieldBuilds(data, spec).All(h => h.Ready);

        /// <summary>
        /// Whether an alley bracket can be entered, for Baz's chalkboard and <see cref="EnterBlocked(Profile, AlleyData, string)"/>
        /// (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 9.1): open when the alley is, its field is ready (<see cref="FieldReady"/>)
        /// and its `opens:` table is cleared (the Brawl: Dumpster Row; the Open: The Black Market; the Invitational: No Limits'
        /// first clear). Otherwise <see cref="BracketOpening.ComingSoon"/> (the field isn't ready: the data's, not yours; shown
        /// before the lock), or locked, with <see cref="BracketOpening.Why"/>. Running brackets and DP are EnterBlocked's.
        /// </summary>
        public static BracketOpening Opening(Profile profile, AlleyData data, string kind)
        {
            var spec = data.Brackets.FirstOrDefault(x => x.Kind == kind);
            if (spec == null) return new BracketOpening { Why = "No such bracket." };
            if (!profile.AlleyOpen) return new BracketOpening { Why = "The alley isn't open." };
            var waiting = FieldBuilds(data, spec).Where(h => !h.Ready).Select(h => h.Id).ToList();
            if (waiting.Count > 0) return new BracketOpening { ComingSoon = true, Why = "Coming soon", NotReady = waiting };
            if (!BackAlley.IsCleared(profile, data, data.IndexOf(spec.Opens))) return new BracketOpening { Why = $"Clear {spec.Opens} to unlock" };
            return new BracketOpening { Open = true };
        }

        /// <summary>
        /// The seed your next entry to an alley bracket draws from: the save's salt, the kind (0xA11B ^ its place in
        /// <see cref="AlleyKinds"/>: brawl 0, midnight 1, invitational 2, so the older kinds' previews never move) and your entry
        /// count (shared with the Locals). <see cref="EnterAlley"/> uses it, and the entry dialog's preview, so the field it shows
        /// is the one Enter seats.
        /// </summary>
        public static uint AlleySeed(Profile profile, string kind) =>
            Hash.Mix(profile.Collection.Salt, 0xA11Bu ^ (uint)Math.Max(0, Array.IndexOf(AlleyKinds, kind)), (uint)profile.BracketsEntered);

        /// <summary>
        /// <see cref="CreateAlley"/>, or null when the field can't be drawn (no such bracket, a field that doesn't fill it, a
        /// deck or table missing from the data): the entry dialog shows no field and no Enter, and <see cref="EnterAlley"/>
        /// charges nothing.
        /// </summary>
        public static Bracket? TryCreateAlley(AlleyData data, string kind, Profile profile, AlleyContext ctx, uint seed, string yourDeck = "")
        {
            // Broad on purpose: whatever the data gets wrong, the alley says "can't be drawn" rather than taking the app down.
            try { return CreateAlley(data, kind, profile, ctx, seed, yourDeck); }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Enters an alley bracket: draws it (<see cref="CreateAlley"/>, from <see cref="AlleySeed"/>: a reload gets the same
        /// bracket), then takes the entry (<see cref="EntryLedger"/>: alley.brackets, the Invitational's nolimits.entry; never
        /// refunded). Null, with nothing charged, when <see cref="EnterBlocked(Profile, AlleyData, string)"/> or the field can't be
        /// drawn. Your deck is checked against <see cref="RulesFor"/> by the caller (no loaners). Save right after; raise the kind
        /// as its story event ("brawl", "midnight", "invitational": the scene shows once).
        /// </summary>
        public static Bracket? EnterAlley(Profile profile, AlleyData data, string kind, AlleyContext ctx, string yourDeck = "")
        {
            if (EnterBlocked(profile, data, kind) != null) return null;
            var b = TryCreateAlley(data, kind, profile, ctx, AlleySeed(profile, kind), yourDeck);
            if (b == null || !profile.Spend(EntryLedger(kind), EntryFor(kind))) return null;
            profile.BracketsEntered++;
            profile.Bracket = b;
            AlleyRecord(profile, kind).Entries++;
            return b;
        }

        public static LocalsRecord Record(Profile profile, string cupId)
        {
            if (!profile.Locals.TryGetValue(cupId, out var r)) profile.Locals[cupId] = r = new LocalsRecord();
            return r;
        }

        /// <summary>An alley bracket's history (<see cref="Profile.AlleyBrackets"/>, by kind), kept apart from the Locals'.</summary>
        public static LocalsRecord AlleyRecord(Profile profile, string kind)
        {
            if (!profile.AlleyBrackets.TryGetValue(kind, out var r)) profile.AlleyBrackets[kind] = r = new LocalsRecord();
            return r;
        }

        /// <summary>
        /// The alley titles' scenes, for a catch-up: "bracketwin:&lt;name&gt;" for each alley bracket you hold a title in. A title's
        /// scene plays after its Contraband reveal; an app closed during the reveal never got it. Raise these when Baz's
        /// chalkboard opens and at load: the story plays a scene once (seen), so it fires once.
        /// </summary>
        public static List<string> AlleyTitleEvents(Profile profile, AlleyData data) =>
            data.Brackets.Where(x => IsAlleyKind(x.Kind) && profile.AlleyBrackets.TryGetValue(x.Kind, out var r) && r.Titles > 0)
                .Select(x => WinEvent + ":" + x.Name).ToList();

        /// <summary>Locals titles (alley titles are counted apart, in <see cref="Profile.AlleyBrackets"/>).</summary>
        public static int TitlesTotal(Profile profile) => profile.Locals.Values.Sum(r => r.Titles);

        // ---- Odds and coins -------------------------------------------------------------------------

        /// <summary>
        /// A's chance against B: the Elo odds 1/(1 + 10^((Eb - Ea)/400)), blended with measured head-to-head results
        /// ((wins + 20·pElo)/(played + 20)) when there are any, clamped to [0.03, 0.97].
        /// </summary>
        public static double Odds(double eloA, double eloB, int wins = 0, int played = 0)
        {
            double p = 1 / (1 + Math.Pow(10, (eloB - eloA) / 400));
            if (played > 0) p = (wins + MatchupPrior * p) / (played + MatchupPrior);
            return Math.Max(MinOdds, Math.Min(MaxOdds, p));
        }

        /// <summary>The deck a seat's head-to-head results are looked up by: a splash regular's base, else its deck.</summary>
        public static string MatchupDeck(BracketEntrant e) => e.Splash?.Base ?? e.Deck;

        public static double Odds(BracketEntrant a, BracketEntrant b, Matchups? matchups = null)
        {
            var (wins, played) = matchups?.Get(MatchupDeck(a), MatchupDeck(b)) ?? (0, 0);
            return Odds(a.Elo, b.Elo, wins, played);
        }

        /// <summary>A side's chance to take a best-of-<paramref name="bestOf"/> series at <paramref name="p"/> a game: p²(3 − 2p) for 3.</summary>
        public static double SeriesOdds(double p, int bestOf)
        {
            int need = Math.Max(1, bestOf / 2 + 1);
            return SeriesFrom(p, need, need);
        }

        /// <summary>A's chance to win <paramref name="needA"/> games before B wins <paramref name="needB"/>.</summary>
        private static double SeriesFrom(double p, int needA, int needB)
        {
            if (needA <= 0) return 1;
            if (needB <= 0) return 0;
            return p * SeriesFrom(p, needA - 1, needB) + (1 - p) * SeriesFrom(p, needA, needB - 1);
        }

        /// <summary>Match <paramref name="m"/>'s coin in [0, 1): fixed by the bracket's seed.</summary>
        public static double Coin(uint seed, int m) => Hash.Mix(seed, 0xC014u, (uint)m) / 4294967296.0;

        /// <summary>
        /// The side (0 A, 1 B) a CPU match's coin gives it to: A when the coin is under A's odds. A best-of-three final is one
        /// coin against the series odds from the games already played (watched games count).
        /// </summary>
        public static int CoinSide(Bracket b, int m, Matchups? matchups = null)
        {
            var (a, bb) = b.Pair(m);
            double p = Odds(b.Slots[a], b.Slots[bb], matchups);
            if (m == b.Final && b.FinalBestOf > 1) p = SeriesFrom(p, b.GamesToWin - b.FinalGamesA, b.GamesToWin - b.FinalGamesB);
            return Coin(b.Seed, m) < p ? 0 : 1;
        }

        /// <summary>Decides a pending match for side 0 (A) or 1 (B). For tests and simulations; the game goes through the methods below.</summary>
        public static bool Decide(Bracket b, int m, int side)
        {
            if (m < 0 || m >= b.MatchCount || !b.Pending(m)) return false;
            var (a, bb) = b.Pair(m);
            b.Winners[m] = side == 0 ? a : bb;
            return true;
        }

        /// <summary>Settles a round's pending CPU matches by their coins (never yours). Returns the matches settled.</summary>
        public static List<int> SimulateRound(Bracket b, int round, Matchups? matchups = null)
        {
            var settled = new List<int>();
            foreach (var m in b.MatchesIn(round))
                if (b.Pending(m) && !b.IsYours(m) && Decide(b, m, CoinSide(b, m, matchups))) settled.Add(m);
            return settled;
        }

        /// <summary>
        /// Settles every CPU match left, round by round, stopping before a match of yours (so it finishes the bracket once
        /// you're out). Returns the matches settled, in order. Call <see cref="Finish"/> when the bracket is <see cref="Bracket.Done"/>.
        /// </summary>
        public static List<int> SkipToEnd(Bracket b, Matchups? matchups = null)
        {
            var settled = new List<int>();
            for (int round = b.CurrentRound(); round < b.Rounds && b.YourMatch() < 0; round++)
                settled.AddRange(SimulateRound(b, round, matchups));
            return settled;
        }

        /// <summary>
        /// A watched CPU match's real result counts (side 0 A, 1 B); a draw (2) falls back to its coin. False if it isn't a
        /// pending CPU match. A best-of-three final counts one game a call: the match is decided once a side has 2
        /// (<see cref="Bracket.Done"/>); a drawn game counts nothing and returns false (play it again).
        /// </summary>
        public static bool SettleWatched(Bracket b, int m, int side, Matchups? matchups = null)
        {
            if (m < 0 || m >= b.MatchCount || !b.Pending(m) || b.IsYours(m)) return false;
            if (m == b.Final && b.FinalBestOf > 1)
            {
                if (side != 0 && side != 1) return false;
                TakeGame(b, side == 0);
                if (Math.Max(b.FinalGamesA, b.FinalGamesB) >= b.GamesToWin) Decide(b, m, b.FinalGamesA > b.FinalGamesB ? 0 : 1);
                return true;
            }
            return Decide(b, m, side == 0 || side == 1 ? side : CoinSide(b, m, matchups));
        }

        /// <summary>A best-of-three final's game to side A or B (its loser picks who goes first next).</summary>
        private static void TakeGame(Bracket b, bool aWon)
        {
            if (aWon) b.FinalGamesA++;
            else b.FinalGamesB++;
            b.FinalLastLoser = aWon ? 1 : 0;
        }

        // ---- Your duels -----------------------------------------------------------------------------

        /// <summary>A duel's seed at match <paramref name="m"/>: Hash.Mix(seed, 0xD0E1, m*16 + attempt).</summary>
        public static uint DuelSeed(Bracket b, int m, int attempt = 0) => Hash.Mix(b.Seed, 0xD0E1u, (uint)(m * 16 + attempt));

        /// <summary>
        /// Starts your duel at your current match: marks it live (save right after: a reload replays it) and returns its duel
        /// seed (each start of the same match gets the next one; each game of a best-of-three final too). Returns 0 when you
        /// have no match to play.
        /// </summary>
        public static uint StartYours(Bracket b)
        {
            int m = b.YourMatch();
            if (m < 0) return 0;
            if (b.LiveMatch != m && b.LiveMatch >= 0) b.Attempt = 0;
            b.LiveMatch = m;
            return DuelSeed(b, m, b.Attempt++ % 16);
        }

        /// <summary>DP a placement pays in this bracket (its kind's pay; half when a loaner played a round).</summary>
        public static int PlacementDp(Bracket b, int placement)
        {
            var pay = PayFor(b.Kind);
            if (placement < 0 || placement >= pay.Length) return 0;
            int dp = pay[placement];
            return b.Loaned ? (int)Math.Round(dp * Loaners.DpShare) : dp;
        }

        public static string PlacementText(int placement) => placement switch
        {
            0 => "Champion",
            1 => "Finalist",
            2 => "Out in the semi-finals",
            3 => "Out in the quarter-finals",
            4 => "Out in the round of 16",
            _ => "Still in",
        };

        /// <summary>
        /// Records your duel at your current match (winner 0 you, 1 them, 2 draw), with a <paramref name="loaner"/> deck or not.
        /// Every result counts as a duel (<see cref="Profile.NoteResult"/> at "locals:&lt;cup&gt;.&lt;m&gt;", "brawl:alley.&lt;m&gt;"),
        /// counts the win or loss against the character, and earns EXP as a repeat Library duel; it pays no DP, and its
        /// level-ups pay tokens. A draw changes nothing else: the match (a final's game) is played again. A best-of-three
        /// final records one game a duel (<see cref="BracketResult.Game"/>; <see cref="BracketResult.NextGame"/> while
        /// neither side has 2). A decided match settles the round's other CPU matches; a loss pays your placement at once
        /// (half if a loaner played; the alley's into "alley"). An alley bracket's win is counted in <see cref="Profile.Wins"/>
        /// only, never as a card unlock's (no bond, gift or drop: <see cref="BackAlley.Record"/>'s rule), a named entrant's too;
        /// a Locals win is <see cref="Profile.AddWin"/>. Null when you have no match. Save after; call <see cref="Finish"/> once
        /// <see cref="Bracket.Done"/>.
        /// </summary>
        public static BracketResult? RecordYours(Profile profile, Library library, Bracket b, int winner, bool loaner, Matchups? matchups = null)
        {
            int m = b.YourMatch();
            if (m < 0) return null;
            var result = new BracketResult();
            var reward = result.Reward;
            var them = b.Slots[b.Opponent(m)];
            b.LiveMatch = -1;
            b.Loaned |= loaner;
            profile.NoteResult(Key(b, m), winner);
            var parts = new List<string>();
            if (winner == 0 || winner == 1)
            {
                if (winner == 1) profile.Losses[them.Character] = profile.Losses.GetValueOrDefault(them.Character) + 1;
                // Not AddWin in the alley: the card unlocks (CardUnlocks.AfterDuel) must never see an alley win.
                else if (IsAlley(b) || them.Character == BackAlley.RegularId) profile.Wins[them.Character] = profile.WinsAgainst(them.Character) + 1;
                else profile.AddWin(them.Character, reward);
                var (a, _) = b.Pair(m);
                bool youAreA = a == b.YourSlot;
                bool decided = true;
                if (m == b.Final && b.FinalBestOf > 1)
                {
                    // A best-of-three final: one game a duel, the match decided at 2 (the duel seed moves on with each game).
                    TakeGame(b, (winner == 0) == youAreA);
                    result.Game = b.FinalGamesA + b.FinalGamesB;
                    int yours = youAreA ? b.FinalGamesA : b.FinalGamesB, theirs = youAreA ? b.FinalGamesB : b.FinalGamesA;
                    decided = Math.Max(yours, theirs) >= b.GamesToWin;
                    result.NextGame = !decided;
                    if (!decided) parts.Add($"Game {result.Game} to {(winner == 0 ? "you" : "them")} · {yours}-{theirs}");
                }
                if (decided)
                {
                    Decide(b, m, (winner == 0) == youAreA ? 0 : 1);
                    b.Attempt = 0;
                    result.Settled = SimulateRound(b, b.RoundOf(m), matchups);
                    string games = m == b.Final && b.FinalBestOf > 1
                        ? $" ({(youAreA ? b.FinalGamesA : b.FinalGamesB)}-{(youAreA ? b.FinalGamesB : b.FinalGamesA)})" : "";
                    if (winner == 1)
                    {
                        result.Out = true;
                        result.Placement = b.Placement();
                        reward.Dp = PlacementDp(b, result.Placement);
                        profile.Earn(PayLedger(b.Kind), reward.Dp);
                        parts.Add($"{PlacementText(result.Placement)}{games} · +{reward.Dp} DP" + (b.Loaned ? " (half: loaner deck)" : ""));
                    }
                    else parts.Add(m == b.Final ? $"{b.Name} won!{games}" : $"Through to the {(b.RoundOf(m) + 2 == b.Rounds ? "final" : b.RoundName(b.RoundOf(m) + 1).ToLowerInvariant() + "s")}");
                }
            }
            else
            {
                result.Replay = true;
                parts.Add(m == b.Final && b.FinalBestOf > 1 ? "A draw: the game is played again" : "A draw: the match is played again");
            }
            reward.Exp = Leveling.DuelExp(library.ExpCircuit, winner, false);
            // Bracket duels' level-ups pay tokens, never DP: a bracket is a sink that pays in cards (PROOF-2026-11-01).
            reward.LevelUps = Leveling.AddExp(profile, reward.Exp, payDp: false);
            foreach (var up in reward.LevelUps) parts.Add(Leveling.Describe(up));
            result.Events.AddRange(reward.LevelUps.Select(u => "level:" + u.Level));
            reward.Summary = string.Join(" · ", parts);
            return result;
        }

        /// <summary>
        /// An alley bracket duel left before it ended (for the menu; or a save loaded with your alley match live: the app closed
        /// mid-duel): a loss, as at the tables, recorded by <see cref="RecordYours"/> (in a best-of-three final, that game is
        /// lost), its summary starting "You left the &lt;name&gt; mid-duel. That counts as a loss." Null when no alley match of
        /// yours is live. A Locals match left live is <see cref="LiveDuels.Settle"/>'s (its record says whether a loaner
        /// played); one from a save older than that record is untouched: a reload replays it. Save after.
        /// </summary>
        public static BracketResult? SettleAbandoned(Profile profile, Library library, Matchups? matchups = null)
        {
            var b = profile.Bracket;
            if (b == null || !IsAlley(b) || b.LiveMatch < 0 || b.LiveMatch != b.YourMatch()) return null;
            var result = RecordYours(profile, library, b, 1, loaner: false, matchups);
            if (result == null) return null;
            var said = result.Reward.Summary;
            result.Reward.Summary = $"You left the {b.Name} mid-duel. That counts as a loss." + (said.Length > 0 ? " " + said : "");
            return result;
        }

        /// <summary>
        /// Pays a finished bracket and moves it into its history: a title's DP (a loss was paid when it happened) and its
        /// packs, opened into your collection now. Locals: Prize Packs (<paramref name="prizes"/>; null counts them without
        /// opening), the cup's <see cref="Profile.Locals"/>, "bracketwin" (your first Locals title) and "bracketwin:&lt;name&gt;".
        /// The alley's: Contraband Packs (<paramref name="contraband"/>, <see cref="ContrabandPacks.Give"/>: Paid 0; null counts
        /// them), or at the Invitational No Limits Packs (<paramref name="noLimits"/>: a <see cref="ContrabandPacks"/> over
        /// <see cref="BackAlley.NoLimitsPackPool"/>; null counts them), <see cref="Profile.AlleyBrackets"/>, the kind's tokens on its
        /// first title (<see cref="TokensFor"/>, once per <see cref="TokenKeyFor"/>: midnight:token, invitational:token), and only
        /// "bracketwin:&lt;name&gt;". Null while the bracket runs. Save after.
        /// <para>
        /// The Invitational never counts its packs unopened: a placement that wins No Limits Packs with no
        /// <paramref name="noLimits"/>, or one that can't fill a whole pack (<see cref="ContrabandPacks.CanFill"/>), isn't
        /// finished. Finish returns null and changes nothing (the bracket stays, done, in <see cref="Profile.Bracket"/>), so a
        /// later call with the packs pays it whole; <see cref="FinishBlocked"/> says why. Its way out is <paramref name="owePacks"/>
        /// (<see cref="SettleHeld"/>, at a load): it finishes anyway, paying the DP, tokens, record and events now and owing the
        /// packs (<see cref="Profile.NoLimitsPacksOwed"/>, each with the seed it would have opened from; <see cref="BracketReward.PacksOwed"/>).
        /// An Invitational finish that can open its packs also opens any owed ones (<see cref="PayOwedPacks"/>, <see cref="BracketReward.OwedPaid"/>).
        /// </para>
        /// </summary>
        public static BracketReward? Finish(Profile profile, Library library, Bracket b, PrizePacks? prizes, ContrabandPacks? contraband = null,
            ContrabandPacks? noLimits = null, bool owePacks = false)
        {
            if (!b.Done) return null;
            bool owing = FinishBlocked(b, noLimits) != null;
            if (owing && !owePacks) return null;
            bool alley = IsAlley(b);
            int place = b.Placement();
            if (place < 0) place = b.Rounds; // you can't be "still in" a finished bracket; a corrupt save counts as an early exit
            var packs = PacksFor(b.Kind);
            var reward = new BracketReward { Name = b.Name, Placement = place, PacksWon = place < packs.Length ? packs[place] : 0 };
            bool firstTitle = TitlesTotal(profile) == 0;
            if (place == 0)
            {
                reward.Dp = PlacementDp(b, 0);
                profile.Earn(PayLedger(b.Kind), reward.Dp);
            }
            if (alley)
            {
                // Contraband Packs: each from its own seed off the bracket's (a reload of the save before Finish opens the same packs).
                var packsFrom = b.Kind == InvitationalKind ? noLimits : contraband;
                if (owing)
                {
                    // Held back and settled (SettleHeld): each pack owed with the seed it would have opened from.
                    for (int k = 0; k < reward.PacksWon; k++) profile.NoLimitsPacksOwed.Add(Hash.Mix(b.Seed, 0xC0B7u, (uint)k));
                    reward.PacksOwed = reward.PacksWon;
                }
                else
                {
                    if (packsFrom != null)
                        for (int k = 0; k < reward.PacksWon; k++) reward.Packs.Add(packsFrom.Give(profile, Hash.Mix(b.Seed, 0xC0B7u, (uint)k)));
                    if (b.Kind == InvitationalKind)
                    {
                        var owed = PayOwedPacks(profile, noLimits);
                        reward.Packs.AddRange(owed);
                        reward.OwedPaid = owed.Count;
                    }
                }
            }
            else
            {
                var cup = library.Cups.FirstOrDefault(c => Library.Id(c) == b.Event);
                if (prizes != null)
                    for (int k = 0; k < reward.PacksWon; k++) reward.Packs.Add(prizes.Give(profile, b, cup?.Pool ?? "", k));
            }
            var rec = alley ? AlleyRecord(profile, b.Kind) : Record(profile, b.Event);
            if (place == 0) rec.Titles++;
            rec.Best = rec.Best < 0 ? place : Math.Min(rec.Best, place);
            if (place == 0)
            {
                if (!alley && firstTitle) reward.Events.Add(WinEvent);
                reward.Events.Add(WinEvent + ":" + b.Name);
                var tokenKey = TokenKeyFor(b.Kind);
                if (alley && tokenKey.Length > 0 && profile.Seen.Add(tokenKey))
                {
                    reward.Tokens = TokensFor(b.Kind);
                    profile.Tokens += reward.Tokens;
                }
            }
            profile.Bracket = null;
            var parts = new List<string> { PlacementText(place) };
            if (reward.Dp > 0) parts.Add($"+{reward.Dp} DP" + (b.Loaned ? " (half: loaner deck)" : ""));
            string packName = PackNameFor(b.Kind);
            if (reward.PacksWon > 0) parts.Add($"{reward.PacksWon} {packName}{(reward.PacksWon > 1 ? "s" : "")}" + (reward.PacksOwed > 0 ? " (owed: they open once the pool can fill a pack)" : ""));
            if (reward.OwedPaid > 0) parts.Add($"{reward.OwedPaid} owed {packName}{(reward.OwedPaid > 1 ? "s" : "")}");
            if (reward.Tokens > 0) parts.Add($"+{reward.Tokens} Card Token{(reward.Tokens > 1 ? "s" : "")}");
            reward.Summary = string.Join(" · ", parts);
            return reward;
        }

        /// <summary>
        /// Why a finished bracket can't be paid yet (null: it can): the Invitational owes No Limits Packs and has no source for
        /// them, or one whose pool can't fill a pack. The other kinds always can (their null packs are counted, as built).
        /// </summary>
        public static string? FinishBlocked(Bracket b, ContrabandPacks? noLimits)
        {
            if (b.Kind != InvitationalKind) return null;
            int place = b.Placement();
            if (place < 0) place = b.Rounds;
            var packs = PacksFor(b.Kind);
            if (place >= packs.Length || packs[place] <= 0) return null;
            if (noLimits == null) return "The No Limits Packs aren't here to open.";
            if (!noLimits.CanFill) return $"The No Limits Pack's pool ({noLimits.Pool.Count} cards) can't fill a pack.";
            return null;
        }

        /// <summary>
        /// A held-back Invitational finish's way out (TODO 2026-11-06): a done Invitational whose No Limits Packs still can't be
        /// opened (<see cref="FinishBlocked"/>) is finished now, owing its packs: <see cref="Finish"/> with owePacks (its DP, tokens,
        /// record and title events paid as any finish pays them; the packs into <see cref="Profile.NoLimitsPacksOwed"/>), and
        /// <see cref="PayOwedPacks"/> opens them once the pool can fill. Nothing is lost and nothing is paid twice (the bracket leaves
        /// <see cref="Profile.Bracket"/> as it's paid, and an owed seed leaves the list as it opens). The app calls it at a load,
        /// so a Finish refused in the session is held only until the next launch, and EnterBlocked frees every bracket then.
        /// Null when nothing is held. Save after.
        /// </summary>
        public static BracketReward? SettleHeld(Profile profile, Library library, ContrabandPacks? noLimits)
        {
            var b = profile.Bracket;
            if (b == null || !b.Done || b.Kind != InvitationalKind || FinishBlocked(b, noLimits) == null) return null;
            return Finish(profile, library, b, null, null, noLimits, owePacks: true);
        }

        /// <summary>
        /// The No Limits Packs owed (<see cref="Profile.NoLimitsPacksOwed"/>), opened into your collection now (<see cref="ContrabandPacks.Give"/>:
        /// Paid 0, each from its own seed) when <paramref name="noLimits"/> can fill a pack; the list empties. Empty when nothing's
        /// owed or the pool still can't fill. Save after.
        /// </summary>
        public static List<List<PrizePull>> PayOwedPacks(Profile profile, ContrabandPacks? noLimits)
        {
            var opened = new List<List<PrizePull>>();
            if (noLimits == null || !noLimits.CanFill || profile.NoLimitsPacksOwed.Count == 0) return opened;
            foreach (var seed in profile.NoLimitsPacksOwed) opened.Add(noLimits.Give(profile, seed));
            profile.NoLimitsPacksOwed.Clear();
            return opened;
        }

        // ---- Save lines -----------------------------------------------------------------------------

        /// <summary>
        /// "bracket &lt;kind&gt; &lt;event&gt; &lt;seed&gt; &lt;loaned&gt; &lt;live&gt; &lt;attempt&gt; &lt;name&gt;", "bracketsize &lt;n&gt;" (16; absent:
        /// 8), "bracketgames &lt;a&gt; &lt;b&gt; &lt;bestOf&gt; &lt;lastLoser&gt;" (a best-of-three final), "bracketslot &lt;i&gt; &lt;char|you&gt;
        /// &lt;deck&gt; &lt;elo&gt;" × size, "bracketsplash &lt;slot&gt; &lt;table&gt; &lt;base&gt; &lt;tint&gt; &lt;added csv|-&gt; &lt;cut csv|-&gt;
        /// &lt;handle...&gt;" (a splash regular), "bracketresult &lt;m&gt; &lt;slot&gt;", "brackets &lt;entered&gt;" and "locals &lt;cupId&gt;
        /// &lt;entries&gt; &lt;titles&gt; &lt;best&gt;", and "nlowed &lt;seed&gt;" per owed No Limits Pack (<see cref="SettleHeld"/>). The alley's
        /// records are Profile's "alleybracket" lines.
        /// </summary>
        internal static void Write(Profile profile, StringBuilder sb)
        {
            if (profile.BracketsEntered > 0) sb.AppendLine($"brackets {profile.BracketsEntered}");
            // Owed No Limits Packs (SettleHeld): one optional line per pack, its seed.
            foreach (var seed in profile.NoLimitsPacksOwed) sb.AppendLine($"nlowed {seed}");
            foreach (var r in profile.Locals.OrderBy(p => p.Key, StringComparer.Ordinal))
                sb.AppendLine($"locals {r.Key} {r.Value.Entries} {r.Value.Titles} {r.Value.Best}");
            var b = profile.Bracket;
            if (b == null) return;
            sb.AppendLine($"bracket {b.Kind} {b.Event} {b.Seed} {(b.Loaned ? 1 : 0)} {b.LiveMatch} {b.Attempt} {b.Name}");
            if (b.Size != Bracket.LocalsSize) sb.AppendLine($"bracketsize {b.Size}");
            if (b.FinalBestOf > 1) sb.AppendLine($"bracketgames {b.FinalGamesA} {b.FinalGamesB} {b.FinalBestOf} {b.FinalLastLoser}");
            for (int i = 0; i < b.Size; i++)
            {
                var e = b.Slots[i];
                sb.AppendLine($"bracketslot {i} {(e.You ? BracketEntrant.YouId : e.Character)} {(e.Deck.Length > 0 ? e.Deck : "-")} {e.Elo.ToString("0", CultureInfo.InvariantCulture)}");
            }
            for (int i = 0; i < b.Size; i++)
            {
                var o = b.Slots[i].Splash;
                if (o == null) continue;
                static string Csv(List<uint> codes) => codes.Count == 0 ? "-" : string.Join(",", codes);
                sb.AppendLine($"bracketsplash {i} {(o.From.Length > 0 ? o.From : "-")} {(o.Base.Length > 0 ? o.Base : "-")} {o.Tint} {Csv(o.Added)} {Csv(o.Cut)} {o.Handle}");
            }
            for (int m = 0; m < b.MatchCount; m++)
                if (b.Winners[m] >= 0) sb.AppendLine($"bracketresult {m} {b.Winners[m]}");
        }

        /// <summary>Reads one of the lines above (false: not a bracket line).</summary>
        internal static bool Read(Profile profile, string key, string rest, string[] parts)
        {
            Bracket B() => profile.Bracket ??= new Bracket { Seed = 0 };
            switch (key)
            {
                case "brackets":
                    int.TryParse(rest, out profile.BracketsEntered);
                    return true;
                case "nlowed" when parts.Length == 1 && uint.TryParse(parts[0], out var owedSeed):
                    profile.NoLimitsPacksOwed.Add(owedSeed);
                    return true;
                case "locals" when parts.Length == 4:
                    if (int.TryParse(parts[1], out var entries) && int.TryParse(parts[2], out var titles) && int.TryParse(parts[3], out var best))
                        profile.Locals[parts[0]] = new LocalsRecord { Entries = entries, Titles = titles, Best = best };
                    return true;
                case "bracket" when parts.Length >= 5:
                {
                    var b = B();
                    b.Kind = parts[0];
                    b.Event = parts[1];
                    uint.TryParse(parts[2], out b.Seed);
                    b.Loaned = parts[3] != "0";
                    // Checked against the bracket's size after load (its "bracketsize" line comes next).
                    b.LiveMatch = int.TryParse(parts[4], out var live) && live >= 0 ? live : -1;
                    if (parts.Length > 5) int.TryParse(parts[5], out b.Attempt);
                    var fields = rest.Split(new[] { ' ' }, 7, StringSplitOptions.RemoveEmptyEntries);
                    b.Name = fields.Length == 7 ? fields[6].Trim() : b.Event;
                    return true;
                }
                case "bracketsize" when parts.Length == 1 && int.TryParse(parts[0], out var size):
                    if (size == 8 || size == 16) B().Resize(size);
                    return true;
                case "bracketgames" when parts.Length >= 2 && int.TryParse(parts[0], out var ga) && int.TryParse(parts[1], out var gb):
                {
                    var b = B();
                    b.FinalGamesA = Math.Max(0, ga);
                    b.FinalGamesB = Math.Max(0, gb);
                    b.FinalBestOf = parts.Length > 2 && int.TryParse(parts[2], out var bo) && bo >= 1 ? bo : 3;
                    b.FinalLastLoser = parts.Length > 3 && int.TryParse(parts[3], out var last) && (last == 0 || last == 1) ? last : -1;
                    return true;
                }
                case "bracketslot" when parts.Length >= 4 && int.TryParse(parts[0], out var i) && i >= 0 && i < B().Size:
                {
                    double.TryParse(parts[parts.Length - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var elo);
                    var deck = string.Join(" ", parts.Skip(2).Take(parts.Length - 3));
                    bool you = parts[1] == BracketEntrant.YouId;
                    B().Slots[i] = new BracketEntrant { Character = parts[1], Deck = deck == "-" ? "" : deck, Elo = elo, You = you };
                    return true;
                }
                case "bracketsplash" when parts.Length >= 6 && int.TryParse(parts[0], out var slot) && slot >= 0 && slot < B().Size && B().Slots[slot] != null:
                {
                    static List<uint> Codes(string csv) =>
                        csv == "-" ? new List<uint>() : csv.Split(',').Select(c => uint.TryParse(c, out var code) ? code : 0).Where(c => c != 0).ToList();
                    uint.TryParse(parts[3], out var tint);
                    B().Slots[slot].Splash = new AlleyOpponent
                    {
                        Table = BackAlley.BracketTable, Seat = slot, Kind = "regular", Character = BackAlley.RegularId,
                        From = parts[1] == "-" ? "" : parts[1], Base = parts[2] == "-" ? "" : parts[2], Tint = tint,
                        Added = Codes(parts[4]), Cut = Codes(parts[5]), Handle = string.Join(" ", parts.Skip(6)),
                    };
                    return true;
                }
                case "bracketresult" when parts.Length == 2 && int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var slot):
                    if (m >= 0 && m < B().MatchCount && slot >= 0 && slot < B().Size) B().Winners[m] = slot;
                    return true;
            }
            return false;
        }

        /// <summary>
        /// After loading: seeds back from bracket order, and a bracket missing any seat (or a splash regular's build) is
        /// dropped. A save from before 16-player brackets reads as 8, as it always was.
        /// </summary>
        internal static void AfterLoad(Profile profile)
        {
            var b = profile.Bracket;
            if (b == null) return;
            if (b.Slots.Any(s => s == null) || b.Slots.Count(s => s.You) != 1 || b.Event.Length == 0
                || b.Slots.Any(s => s.Character == BackAlley.RegularId && s.Splash == null)) { profile.Bracket = null; return; }
            var order = OrderFor(b.Size);
            for (int i = 0; i < b.Size; i++)
            {
                b.Slots[i].Seed = order[i];
                if (b.Slots[i].Splash is AlleyOpponent o) b.Slots[i].Deck = o.DeckKey;
            }
            // A result only stands where both its sides were known (a hand-edited save can't skip a round).
            for (int m = 0; m < b.MatchCount; m++)
            {
                var (x, y) = b.Pair(m);
                if (b.Winners[m] >= 0 && (x < 0 || y < 0 || (b.Winners[m] != x && b.Winners[m] != y))) b.Winners[m] = -1;
            }
            // A best-of-three final's games: never past the deciding game.
            b.FinalGamesA = Math.Min(b.FinalGamesA, b.GamesToWin);
            b.FinalGamesB = Math.Min(b.FinalGamesB, b.GamesToWin);
            if (b.LiveMatch >= b.MatchCount || (b.LiveMatch >= 0 && b.YourMatch() != b.LiveMatch)) b.LiveMatch = -1;
        }
    }
}
