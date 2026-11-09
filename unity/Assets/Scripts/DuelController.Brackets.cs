using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.DeckBuilding;
using Ygo.Engine.Presentation;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The brackets. Library Locals (BRACKETS-SPEC-2026-11-01 §4): a cleared cup's Locals button, its entry dialog and
    /// bracket screen (<see cref="BracketScreen"/>), your bracket duels (a versus under the cup's rules, loaners allowed; each
    /// round's duel seed from <see cref="Brackets.StartYours"/>), their results, a watched CPU final, and the prizes at the end.
    /// The Back Alley's (docs/dev/ALLEY-SPEC-2026-11-01.md section 11; docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 9): the Alley
    /// Brawl, the Midnight Open and the No Limits Invitational from Baz's chalkboard, in the alley's theme, mat and music; your
    /// own decks only under the kind's rules (<see cref="Brackets.RulesFor"/>: the Alley list, the Invitational's No Limits),
    /// every duel under the turn cap; splash regulars as their handles; a best-of-three final a game a duel (the last game's
    /// loser chooses who goes first), watched a game at a time once you're out; Contraband Packs (the Invitational's No Limits
    /// Packs) revealed at the finish. Baz MCs
    /// (mc:&lt;kind&gt;:&lt;round&gt;, mc:game:&lt;n&gt;, mc:out, mc:upset, mc:crowned) and Mika streams (stream:&lt;kind&gt;:&lt;round&gt;).
    /// Everything is saved as it happens: entry, the start of your duel, every result (each game), the finish.
    /// </summary>
    public sealed partial class DuelController
    {
        private BracketScreen _bracketScreen = null!;
        /// <summary>Your bracket match being dueled (a match index), or null.</summary>
        private int? _bracketMatch;
        /// <summary>The bracket's CPU final being watched (it counts).</summary>
        private bool _bracketWatch;
        /// <summary>The rules the last bracket versus was shown under (for the check).</summary>
        private FormatRules? _bracketVersusRules;
        /// <summary>
        /// An alley bracket duel's two sides as people (a splash regular as its handle at its skill, anyone else as the cast has
        /// them) and their slots (-1: you); null and -1 at a Locals, where the roster's characters sit as they always did.
        /// </summary>
        private readonly Personality?[] _bracketSides = new Personality?[2];
        private readonly int[] _bracketSeats = { -1, -1 };
        /// <summary>The alley bracket versus's lines, picked once per game (cycling decks keeps them): Baz's, Mika's, their intro.</summary>
        private string _bracketLinesFor = "";
        private (string speaker, string text)? _bracketMc, _bracketStream;
        private string _bracketQuote = "";
        /// <summary>For the checks: the last finished bracket's reward (its packs as opened).</summary>
        private BracketReward? _lastBracketReward;

        private void InitBrackets()
        {
            _bracketScreen = gameObject.AddComponent<BracketScreen>();
            _bracketScreen.Init(_campaign, _library);
            _campaignScreen.LocalsClicked += cup =>
            {
                if (_campaign.Profile is not Profile profile) return;
                if (Brackets.RunningAt(profile, _campaign.Library, cup)) ShowBracket();
                else _bracketScreen.ShowEntry(cup);
            };
            // Baz's chalkboard: the running bracket of that kind, else its entry dialog over the chalkboard.
            _campaignScreen.ChalkboardClicked += kind =>
            {
                if (_campaign.Profile is not Profile profile) return;
                if (profile.Bracket is Bracket running && running.Kind == kind) ShowBracket();
                else _bracketScreen.ShowAlleyEntry(kind);
            };
            // Entered: Hollis's scene about the format the first time, then the bracket.
            _bracketScreen.Entered += cup =>
            {
                _campaignScreen.Close();
                PlayStory(new[] { Brackets.EntryEvent }, "", () => ShowBracket());
            };
            // An alley bracket entered: its first-entry scene ("brawl", "midnight": Baz chalks the names up), then the bracket.
            _bracketScreen.AlleyEntered += kind =>
            {
                _campaignScreen.Close();
                PlayStory(new[] { kind }, "", () => ShowBracket());
            };
            _bracketScreen.DuelClicked += () => { _bracketScreen.Close(); ShowBracketVersus(); };
            _bracketScreen.BackClicked += () =>
            {
                bool alley = _campaign.Profile?.Bracket is Bracket b && Brackets.IsAlley(b);
                _bracketScreen.Close();
                if (alley) _campaignScreen.OpenChalkboard();
                else _campaignScreen.Open();
            };
            _bracketScreen.ScoutClicked += them => { _bracketScreen.Close(); _scenes.ShowScoutReport(Scouts(them.Select(t => (t.who, ScoutDeck(t.deck))).ToArray()), () => ShowBracket()); };
            _bracketScreen.WatchClicked += WatchBracketFinal;
            _bracketScreen.FinishClicked += FinishBracket;
            _bracketScreen.SettleClicked += SettleHeldBracket;
        }

        /// <summary>The running bracket's cup (an index into the Library's cups), or -1 (an alley bracket has none).</summary>
        private int BracketCup() =>
            _campaign.Profile?.Bracket is Bracket b && !Brackets.IsAlley(b) ? _campaign.Library.Cups.ToList().FindIndex(c => Library.Id(c) == b.Event) : -1;

        /// <summary>The running bracket is the alley's (the Alley Brawl, the Midnight Open or the No Limits Invitational).</summary>
        private bool AlleyBracket => _campaign.Profile?.Bracket is Bracket b && Brackets.IsAlley(b);

        /// <summary>
        /// The rules a bracket duel plays under: an alley bracket's by its kind (<see cref="CampaignState.AlleyBracketRules"/>:
        /// the No Limits rules at the Invitational, the Alley's at the others); a Locals its cup's list and pool (null when the
        /// cup is gone).
        /// </summary>
        private FormatRules? BracketRules() =>
            _campaign.Profile?.Bracket is Bracket b && Brackets.IsAlley(b) ? _campaign.AlleyBracketRules(b.Kind)
            : BracketCup() is int cup && cup >= 0 ? _campaign.CupRules(cup) : null;

        /// <summary>A bracket CPU's character (the cup's matches and locals; the roster as a fallback).</summary>
        private Personality BracketCharacter(string id)
        {
            int cup = BracketCup();
            var c = cup >= 0 ? _campaign.Library.Cups[cup] : null;
            return c?.Matches.Concat(c.Locals).Select(m => m.Character).FirstOrDefault(p => p.Id == id)
                   ?? _roster.FirstOrDefault(p => p.Id == id)
                   // A pilot with a profile but not yet a duelist (an Invitational field build before its deck ships): the cast's.
                   ?? (_campaign.Cast.TryGetValue(id, out var cast) ? cast : null) ?? new Personality { Id = id, Name = id };
        }

        /// <summary>A bracket slot's duelist: a splash regular as its handle (<see cref="BackAlley.PersonalityOf"/>), else the character.</summary>
        private Personality BracketPerson(Bracket b, int slot)
        {
            if (b.Slots[slot].Splash != null && _campaign.Profile is Profile p && _campaign.AlleyContext is AlleyContext ctx
                && BackAlley.BracketOpponent(p, slot, ctx) is AlleyOpponent o)
                return BackAlley.PersonalityOf(o, ctx);
            return BracketCharacter(b.Slots[slot].Character);
        }

        /// <summary>The deck a scouting report reads for a bracket deck: a splash regular's base (its playbook), else the deck.</summary>
        private string ScoutDeck(string deck)
        {
            if (!Alley.IsAlleyDeck(deck) || _campaign.Profile?.Bracket is not Bracket b) return deck;
            int slot = Array.FindIndex(b.Slots, s => s?.Deck == deck);
            return slot >= 0 && b.Slots[slot].Splash is AlleyOpponent o ? o.Base : deck;
        }

        /// <summary>
        /// The bracket screen (the campaign screen closed), in its theme: an alley bracket in the alley's, a Locals on main
        /// street. <paramref name="animate"/>: matches just settled, shown one by one.
        /// </summary>
        private void ShowBracket(string message = "", IReadOnlyList<int>? animate = null)
        {
            if (_campaign.Profile is not Profile profile || profile.Bracket is not Bracket b) { _campaignScreen.Open(); return; }
            _campaignScreen.Close();
            if (Brackets.IsAlley(b)) UiTheme.Current = UiTheme.BackAlley;
            else MainStreetTheme();
            // The Alley Brawl's out-of-towners (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 8): the first field that shows one plays
            // "outoftowner" before the bracket is shown.
            if (Brackets.IsAlley(b) && BackAlley.OutOfTowners(b, _campaign.AlleyData).Count > 0
                && _story.Pending(profile, new[] { BackAlley.OutOfTownerEvent }).Count > 0)
            {
                PlayStory(new[] { BackAlley.OutOfTownerEvent }, "", () => { UiTheme.Current = UiTheme.BackAlley; _bracketScreen.Open(message, animate); });
                return;
            }
            _bracketScreen.Open(message, animate);
        }

        /// <summary>
        /// An alley bracket slot's out-of-towner (a No Limits build's pilot on its Brawl deck, section 8): its id, or null for
        /// anyone else, a pilot on its own peak build (the Invitational's) included (<see cref="BackAlley.OutOfTowners"/>' test).
        /// </summary>
        private string? OutOfTownerAt(Bracket b, int slot) =>
            Brackets.IsAlley(b) && b.Slots[slot] is BracketEntrant e && !e.You && e.Splash == null
            && _campaign.AlleyData.FindBuild(e.Character) is Headliner h && e.Deck != h.Deck ? e.Character : null;

        /// <summary>An alley bracket slot's pilot on its own peak build (the Invitational's field): its id, or null.</summary>
        private string? PilotAt(Bracket b, int slot) =>
            Brackets.IsAlley(b) && b.Slots[slot] is BracketEntrant e && !e.You && e.Splash == null
            && _campaign.AlleyData.FindBuild(e.Character) is Headliner h && e.Deck == h.Deck ? e.Character : null;

        /// <summary>A bracket match's music: an alley bracket plays the alley's; a Locals final the Master theme, the rest the everyday music.</summary>
        private (string theme, int transpose) BracketMusic(int? match)
        {
            var b = _campaign.Profile?.Bracket;
            if (b != null && Brackets.IsAlley(b)) return (BossMusic.Alley, 0);
            return match == null || match == (b?.Final ?? Bracket.LocalsSize - 2) ? ("master", 0) : (BossMusic.Everyday, 0);
        }

        /// <summary>The duel's top-bar stage: "Semi-final", "Final, game 2", "The final (watching)".</summary>
        private static string BracketStage(Bracket b, int? match, bool watch)
        {
            int game = b.FinalBestOf > 1 ? b.FinalGamesA + b.FinalGamesB + 1 : 0;
            if (watch) return game > 0 ? $"The final, game {game} (watching)" : "The final (watching)";
            int m = match ?? b.Final;
            return m == b.Final && game > 0 ? $"Final, game {game}" : b.RoundName(b.RoundOf(m));
        }

        /// <summary>The mats.txt key an alley bracket's splash regular sits at: the mat of the table it was drawn from (null otherwise).</summary>
        private string? BracketMat(int seat)
        {
            if (_campaign.Profile?.Bracket is not Bracket b || !Brackets.IsAlley(b) || seat < 0 || seat > 1) return null;
            int slot = _bracketSeats[seat];
            if (slot < 0 || slot >= b.Size || b.Slots[slot].Splash is not AlleyOpponent o) return null;
            int table = _campaign.AlleyData.IndexOf(o.From);
            return table >= 0 ? _campaign.AlleyData.Tables[table].Mat : null;
        }

        /// <summary>What was measured against a bracket CPU at its cup, when they're one of the cup's own matches (the toss tip).</summary>
        private IEnumerable<MeasuredOption> BracketMeasured(BracketEntrant them)
        {
            int cup = BracketCup();
            if (cup < 0) return Enumerable.Empty<MeasuredOption>();
            int at = _campaign.Library.Cups[cup].Matches.FindIndex(m => m.Character.Id == them.Character && m.Deck == them.Deck);
            return at < 0 ? Enumerable.Empty<MeasuredOption>() : _campaign.LibraryAdvice.For(Profile.MatchKey(cup, at));
        }

        // ---- Your match --------------------------------------------------------------------------------------------------

        /// <summary>
        /// Before your bracket match: your decks and the loaners (made legal for the cup's list and pool; a loaner halves
        /// placement DP), as before a cup match. Duel! marks the match live (saved) and takes its duel seed; Back returns
        /// to the bracket. An alley bracket's versus is its own (<see cref="ShowAlleyBracketVersus"/>).
        /// </summary>
        private void ShowBracketVersus(bool cycled = false)
        {
            if (AlleyBracket) { ShowAlleyBracketVersus(cycled); return; }
            var profile = _campaign.Profile;
            var b = profile?.Bracket;
            int cup = BracketCup();
            int m = b?.YourMatch() ?? -1;
            if (profile == null || b == null || cup < 0 || m < 0) { ShowBracket(); return; }
            var c = _campaign.Library.Cups[cup];
            var rules = _campaign.CupRules(cup);
            _bracketVersusRules = rules;
            var them = b.Slots[b.Opponent(m)];
            var who = BracketCharacter(them.Character);
            var choices = PickVersusDeck(cycled);
            // Your deck isn't legal under the cup's list: start on a loaner (the arrows still reach yours).
            if (!cycled && !Loaners.IsLoaner(_versusDeck!) && OwnDeckProblems(_versusDeck!, rules).Count > 0)
            {
                var lend = _campaign.LoanerDecks().FirstOrDefault();
                if (lend != null && choices.Contains(lend)) _versusDeck = lend;
            }
            bool loaner = _campaign.IsLoanerDeck(_versusDeck!);
            var yourDeck = Loaners.IsLoaner(_versusDeck!) ? _campaign.Loaner(_versusDeck!, rules) : _library.Contains(_versusDeck!) ? _library.Load(_versusDeck!) : new Deck();
            var adjusted = Loaners.IsLoaner(_versusDeck!) ? _campaign.LoanerChanges(Loaners.RecipeOf(_versusDeck!), rules) : new List<string>();
            var theirDeck = _library.Contains(them.Deck) ? _library.Load(them.Deck) : new Deck();
            int wins = profile.WinsAgainst(who.Id), losses = profile.Losses.TryGetValue(who.Id, out var l) ? l : 0;
            var round = b.RoundName(b.RoundOf(m));
            var you = new CampaignScenes.Side
            {
                Name = "You", Deck = Loaners.IsLoaner(_versusDeck!) ? LoanerTitle(Loaners.RecipeOf(_versusDeck!), DeckTitle(them.Deck)) : VersusDeckName(_versusDeck!),
                DeckNote = loaner ? "(loaner · placement DP halved)" : "",
                Showcase = CampaignScenes.ShowcaseOf(yourDeck, _cardPack!),
                Detail = $"Seed #{b.Slots[b.YourSlot].Seed} · you have {profile.Dp:N0} DP"
                         + (rules.List != null ? $"\nBan list: {rules.List.Name}" : $"\nBan list: {c.Banlist} (not installed: no list)") + $" · cards out by {CampaignScreen.PoolMonth(c.Pool)}"
                         + (adjusted.Count > 0 ? $"\n<color=#E3A21A>Adjusted for it: {string.Join(", ", adjusted.Take(3))}{(adjusted.Count > 3 ? $" (+{adjusted.Count - 3} more)" : "")}</color>" : ""),
            };
            var themSide = new CampaignScenes.Side
            {
                Name = who.Name, Title = who.Title, Deck = DeckTitle(them.Deck),
                DeckNote = them.Elo > 0 ? $"(#{them.Seed} · Elo {them.Elo:N0})" : $"(#{them.Seed})",
                Showcase = CampaignScenes.ShowcaseOf(theirDeck, _cardPack!), Quote = who.Intro, Portraits = { who.Id },
                Detail = who.StyleSummary + (wins + losses > 0 ? $"\nYour record against them: {wins}-{losses}" : "\nFirst meeting"),
            };
            BossSting(BracketMusic(m), cycled);
            _scenes.ShowVersus(b.Name, $"{round} · your deck must be legal each round; a loaner halves placement DP", you, themSide,
                start: () =>
                {
                    var deck = _versusDeck ?? profile.Deck;
                    if (!Loaners.IsLoaner(deck) && OwnDeckProblems(deck, rules) is { Count: > 0 } problems)
                    {
                        _ownedNote = $"<color=#FF8A8A>{VersusDeckName(deck)} isn't legal in this cup:</color> {problems[0].Message} Pick a loaner with the arrows, or edit your deck.";
                        ShowBracketVersus(cycled: true);
                        return;
                    }
                    // The match is live from here, with its own duel seed (the toss and the shuffle): closing the app now loses it
                    // at the next launch (DuelController.LiveDuel), as at the alley's.
                    uint duelSeed = Brackets.StartYours(b);
                    if (duelSeed == 0) { ShowBracket(); return; }
                    BeginLiveDuel(LiveDuels.Locals, match: b.LiveMatch);
                    _campaign.Save();
                    seed = (int)(duelSeed & 0x7FFFFFFF);
                    CoinToss("You", who.Name, deck, them.Deck, StartBracketDuel, measured: MeasuredToss(BracketMeasured(them)));
                },
                back: () => ShowBracket(),
                cycleDeck: delta => { CycleVersusDeck(choices, delta); ShowBracketVersus(cycled: true); },
                // Stuck? after losses in a row at this round of this Locals (Brackets.Key), as at a cup match: what was measured
                // against them at the cup, and Make it yours under the cup's list and pool.
                scouts: Scouts((who.Name, them.Deck)), stuck: Stuck(Brackets.Key(b, m), BracketMeasured(them), false, new[] { who.Id }, them.Deck), quiet: cycled,
                offer: OwnOffer(Brackets.Key(b, m), BracketMeasured(them), false, new[] { who.Id }, rules, () => ShowBracketVersus(cycled: true)),
                note: TakeOwnedNote(), texts: _campaign.TextsLine(yourDeck, rules));
        }

        /// <summary>A round's key in alley-voice.txt's mc: and stream: pools ("round-of-16", "quarter-final", "semi-final", "final").</summary>
        private static string RoundKey(Bracket b, int round) => b.RoundName(round).ToLowerInvariant().Replace(' ', '-');

        /// <summary>The placeholders Baz's and Mika's bracket lines fill: {nick}, {handle} (who you meet), {round}, {table}.</summary>
        private Dictionary<string, string> BracketFill(Bracket b, int match, string handle)
        {
            var fill = BackAlley.FillFor(_campaign.Profile!, _campaign.AlleyData, null, null, _cardPack!);
            fill["handle"] = handle;
            fill["round"] = b.RoundName(b.RoundOf(match)).ToLowerInvariant();
            fill["table"] = b.Name;
            return fill;
        }

        /// <summary>
        /// Baz's call for a best-of-three's later game: that game's pool (mc:game:2 "Game two!", mc:game:3 "Game three!"),
        /// else plain mc:game's game-neutral lines. Picked once a game (the versus keeps it).
        /// </summary>
        private (string speaker, string text)? GameLine(int game, string handle, Dictionary<string, string> fill) =>
            _campaign.AlleyLine($"mc:game:{game}", handle, fill) ?? _campaign.AlleyLine("mc:game", handle, fill);

        /// <summary>A splash regular's line (intro, win, lose) in a bracket: its own table's pool, as at the table it came from.</summary>
        private string BracketRegularLine(string moment, AlleyOpponent o)
        {
            var table = o.Table;
            o.Table = o.From.Length > 0 ? o.From : table;
            try { return RegularLine(moment, o, null); }
            finally { o.Table = table; }
        }

        /// <summary>
        /// Before your alley bracket match: your own decks only (the arrows cycle them, no loaners), checked against the kind's
        /// rules at Duel! (the Alley list; the Invitational's, No Limits'); their side as the alley shows a regular (the handle, the hood, the contraband) or the character; Baz
        /// calls the round and Mika streams it. In the Midnight Open's final, the game and the score; game 2 and 3 have no
        /// toss: the last game's loser chooses who goes first. No stakes: the entry's paid.
        /// </summary>
        private void ShowAlleyBracketVersus(bool cycled)
        {
            var profile = _campaign.Profile;
            var b = profile?.Bracket;
            var ctx = _campaign.AlleyContext;
            int m = b?.YourMatch() ?? -1;
            if (profile == null || b == null || ctx == null || m < 0) { ShowBracket(); return; }
            UiTheme.Current = UiTheme.BackAlley;
            var T = UiTheme.Current;
            // The kind's rules (Brackets.RulesFor): the Invitational's are No Limits', the Brawl's and the Open's the Alley's.
            var rules = _campaign.AlleyBracketRules(b.Kind);
            bool noLimits = b.Kind == Brackets.InvitationalKind;
            _bracketVersusRules = rules;
            int themSlot = b.Opponent(m);
            var them = b.Slots[themSlot];
            var who = BracketPerson(b, themSlot);
            var splash = them.Splash != null ? BackAlley.BracketOpponent(profile, themSlot, ctx) : null;
            // An out-of-towner's first match: its scene ("outoftowner:<id>"), then the versus.
            var outOfTowner = OutOfTownerAt(b, themSlot);
            if (!cycled && outOfTowner != null && _story.Pending(profile, new[] { BackAlley.OutOfTownerEvent + ":" + outOfTowner }).Count > 0)
            {
                PlayStory(new[] { BackAlley.OutOfTownerEvent + ":" + outOfTowner }, "", () => ShowAlleyBracketVersus(cycled: false));
                return;
            }
            var choices = _campaign.AlleyDeckChoices();
            if (choices.Count == 0) { ShowBracket("Build a deck of your own first: the alley doesn't lend."); return; }
            if (_versusDeck == null || !choices.Contains(_versusDeck) || !cycled) _versusDeck = choices.Contains(profile.Deck) ? profile.Deck : choices[0];
            var yourDeck = _library.Load(_versusDeck);
            yourDeck.Name = _versusDeck;
            var problems = _campaign.AlleyProblems(yourDeck, rules);
            bool bo3 = m == b.Final && b.FinalBestOf > 1;
            int game = bo3 ? b.FinalGamesA + b.FinalGamesB + 1 : 0;
            var (a, _) = b.Pair(b.Final);
            int yourSide = a == b.YourSlot ? 0 : 1;
            int mine = yourSide == 0 ? b.FinalGamesA : b.FinalGamesB, theirs = yourSide == 0 ? b.FinalGamesB : b.FinalGamesA;
            var round = b.RoundName(b.RoundOf(m));

            // Baz and Mika on the round (a later game of the final: Baz's game call), and their intro: picked once a game.
            var linesKey = $"{b.Seed}.{m}.{game}";
            if (_bracketLinesFor != linesKey)
            {
                _bracketLinesFor = linesKey;
                var fill = BracketFill(b, m, who.Name);
                // Baz on an out-of-towner (mc:brawl:outoftowner), else the round; their intro from their out-of-towner pool, else their own.
                _bracketMc = game > 1 ? GameLine(game, who.Name, fill)
                    : (outOfTowner != null ? _campaign.AlleyLine($"mc:{b.Kind}:{BackAlley.OutOfTownerEvent}", who.Name, fill) : null)
                      ?? _campaign.AlleyLine($"mc:{b.Kind}:{RoundKey(b, b.RoundOf(m))}", who.Name, fill);
                _bracketStream = game > 1 ? null : _campaign.AlleyLine($"stream:{b.Kind}:{RoundKey(b, b.RoundOf(m))}", who.Name, fill);
                _bracketQuote = splash != null ? BracketRegularLine("intro", splash)
                    : outOfTowner != null ? _campaign.AlleyLine($"{BackAlley.OutOfTownerEvent}:intro:{outOfTowner}", who.Name, fill)?.text ?? who.Intro
                    // A pilot on its peak build (the Invitational): its No Limits intro (pilot:intro:<id>), else its profile's.
                    : PilotAt(b, themSlot) is string pilot ? _campaign.AlleyLine($"pilot:intro:{pilot}", who.Name, fill)?.text ?? who.Intro
                    : who.Intro;
                _campaign.Save();
            }
            int wins = profile.WinsAgainst(who.Id), losses = profile.Losses.TryGetValue(who.Id, out var l) ? l : 0;
            var calls = new List<string>();
            if (_bracketMc is { } mc) calls.Add($"{mc.speaker}: “{mc.text}”");
            if (_bracketStream is { } stream) calls.Add($"{stream.speaker}: “{stream.text}”");
            // A splash regular has no record of its own (every one is "alley-regular"); a named entrant does.
            if (splash == null) calls.Add(wins + losses > 0 ? $"Your record against them: {wins}-{losses}" : "First meeting");

            var showcase = splash != null ? BackAlley.Showcase(splash, ctx).ToList() : CampaignScenes.ShowcaseOf(_library.Contains(them.Deck) ? _library.Load(them.Deck) : new Deck(), _cardPack!);
            var added = splash == null ? new List<string>() : splash.Added.Select(c => Banlist.BaseCode(c, _cardPack!)).Distinct()
                .OrderByDescending(c => _cardPack!.Get(c) is CardInfo i ? AutoDeck.Value(i) : 0).Select(c => _cardPack!.Get(c)?.Name ?? c.ToString()).ToList();
            var you = new CampaignScenes.Side
            {
                Name = "You", Deck = DeckTitle(_versusDeck),
                Showcase = CampaignScenes.ShowcaseOf(yourDeck, _cardPack!),
                Detail = $"Seed #{b.Slots[b.YourSlot].Seed} · you have {profile.Dp:N0} DP\nNo stakes: the entry's paid, placement pays"
                         + (bo3 ? $"\n<color={T.GoldHex}>The final, game {game} of {b.FinalBestOf}: you {mine}-{theirs}</color>" : ""),
            };
            var themSide = new CampaignScenes.Side
            {
                Name = who.Name, Title = splash != null ? $"{who.Title} regular" : who.Title,
                Deck = splash != null ? $"{DeckTitle(splash.Base)}{(added.Count > 0 ? " + " + string.Join(", ", added.Take(3)) : "")}" : DeckTitle(them.Deck),
                DeckNote = them.Elo > 0 ? $"(#{them.Seed} · Elo {them.Elo:N0})" : $"(#{them.Seed})",
                Showcase = showcase,
                ShowcaseLabel = splash != null && showcase.Any(BackAlley.ShowcaseContraband(splash, ctx).Contains) ? "CONTRABAND" : "",
                Portraits = splash == null ? new List<string> { who.Id }
                    : YgoCampaign.Game.Portraits.GruntFor(who.Name) is string grunt ? new List<string> { grunt } : new List<string>(),
                Quote = _bracketQuote,
                Detail = string.Join("\n", calls),
            };
            var scouts = Scouts((who.Name, splash != null ? splash.Base : them.Deck));
            if (added.Count > 0 && scouts.Count > 0) scouts[0].Keys.Insert(0, $"<color={T.BadHex}>Contraband:</color> {string.Join(", ", added)}");
            var note = _alleyNote;
            _alleyNote = "";
            if (note.Length == 0 && problems.Count > 0) note = $"<color={T.BadHex}>{DeckTitle(_versusDeck)} isn't legal here:</color> {problems[0].Message}";
            _alleyLastNote = note;
            var stage = bo3 ? $"{round}, game {game} of {b.FinalBestOf} ({mine}-{theirs})" : round;
            // Stuck? after losses in a row at this round of this bracket (Brackets.Key): nothing was measured here and the alley
            // doesn't lend, so the advice names stronger recipes to own, and Make it yours builds the strongest under the kind's rules.
            var stuckKey = Brackets.Key(b, m);
            var theirIds = new[] { who.Id };
            var stuck = Stuck(stuckKey, Enumerable.Empty<MeasuredOption>(), false, theirIds, false, splash != null ? splash.Base : them.Deck);
            var offer = OwnOffer(stuckKey, Enumerable.Empty<MeasuredOption>(), false, theirIds, rules,
                () => { _alleyNote = TakeOwnedNote(); ShowAlleyBracketVersus(cycled: true); }, unmeasured: StrongerRecipe());
            Sounds.Prepare(BossMusic.Alley, 0);
            _scenes.ShowVersus(b.Name, $"{stage} · turn 40, higher LP wins · {(noLimits ? "no list, three of anything" : "Alley list")}, your own decks", you, themSide,
                start: () =>
                {
                    var deck = _versusDeck ?? profile.Deck;
                    if (!_library.Contains(deck) || _campaign.AlleyProblems(_library.Load(deck), rules) is { Count: > 0 })
                    {
                        var why = _library.Contains(deck) ? _campaign.AlleyProblems(_library.Load(deck), rules)[0].Message : "Build a deck first.";
                        _alleyNote = $"<color={UiTheme.Current.BadHex}>{DeckTitle(deck)} isn't legal {(noLimits ? "at the Invitational" : "in the alley")}:</color> {why} Switch decks with the arrows, or edit it.";
                        ShowAlleyBracketVersus(cycled: true);
                        return;
                    }
                    // The match (a final's game) is live from here (a reload replays it), with its own duel seed.
                    uint duelSeed = Brackets.StartYours(b);
                    if (duelSeed == 0) { ShowBracket(); return; }
                    _campaign.Save();
                    seed = (int)(duelSeed & 0x7FFFFFFF);
                    // Game 2 or 3: no toss; whoever lost the last game chooses.
                    bool? youChoose = bo3 && game > 1 && b.FinalLastLoser >= 0 ? b.FinalLastLoser == yourSide : (bool?)null;
                    CoinToss("You", who.Name, deck, splash != null ? splash.Base : them.Deck, StartBracketDuel, leftChooses: youChoose, game: game);
                },
                back: () => ShowBracket(),
                cycleDeck: delta => { CycleVersusDeck(choices, delta); ShowAlleyBracketVersus(cycled: true); },
                scouts: scouts, stuck: stuck, offer: offer, quiet: cycled, note: note, arrowsHint: "Arrows: switch between your own decks (the alley doesn't lend).",
                texts: _campaign.TextsLine(yourDeck, rules));
        }

        private void StartBracketDuel()
        {
            var b = _campaign.Profile?.Bracket;
            int m = b?.YourMatch() ?? -1;
            if (b == null || m < 0) { ShowBracket(); return; }
            int themSlot = b.Opponent(m);
            var them = b.Slots[themSlot];
            bool alley = Brackets.IsAlley(b);
            _pickCharacter[0] = -1;
            _pickDeck[0] = TakeVersusDeck();
            _pickCharacter[1] = Math.Max(0, _roster.FindIndex(p => p.Id == them.Character));
            // An alley bracket's splash regular plays its deck as rebuilt from the save ("alley:bracket.<slot>").
            _pickDeck[1] = alley && Alley.IsAlleyDeck(them.Deck) ? them.Deck : _allDecks.Contains(them.Deck) ? them.Deck : OwnDecks(_roster[_pickCharacter[1]])[0];
            _bracketSides[0] = null;
            _bracketSides[1] = alley ? BracketPerson(b, themSlot) : null;
            _bracketSeats[0] = -1;
            _bracketSeats[1] = alley ? themSlot : -1;
            ShowPicks();
            _paused = false;
            NewDuel(bracketMatch: m);
        }

        /// <summary>
        /// Leaving your alley bracket duel before it ends (for the menu): a loss, as at the tables, recorded at once
        /// (<see cref="Brackets.SettleAbandoned"/>: in the Midnight Open's final, that game) and saved; the campaign screen says
        /// so next time it opens. A Locals duel isn't left for the menu (its menu offers Back to the duel and Concede,
        /// DuelController.DevMode); one cut short by quitting is lost at the next launch (DuelController.LiveDuel).
        /// </summary>
        private void AbandonAlleyBracketDuel()
        {
            if (_bracketMatch is not int m || _bracketWatch || _runner == null || _runner.IsFinished || _watching != null) return;
            if (_campaign.Profile is not Profile profile || profile.Bracket is not Bracket b || !Brackets.IsAlley(b)) return;
            _bracketMatch = null;
            int themSlot = b.Opponent(m);
            var handle = themSlot >= 0 ? BracketPerson(b, themSlot).Name : "";
            var left = Brackets.SettleAbandoned(profile, _campaign.Library, _campaign.Matchups);
            if (left == null) return;
            _campaign.Save();
            _campaign.AlleyNotice = left.Reward.Summary;
            _campaign.QueueAlleyStory(left.Events, BracketFill(b, m, handle), left.Reward.Grants);
            _view.AppendLog(left.Reward.Summary);
        }

        /// <summary>
        /// At the end of a bracket duel (yours, or the watched final): records it, saves, and shows what's next. False when
        /// this isn't one.
        /// </summary>
        private bool FinishBracketDuel()
        {
            if (_watching != null || _campaign.Profile is not Profile profile) return false;
            if (_bracketMatch == null && !_bracketWatch) return false;
            int winner = _board.Winner > 1 ? 2 : _board.Winner;
            var b = profile.Bracket;
            if (b == null) return false;
            bool alley = Brackets.IsAlley(b);
            bool bo3 = b.FinalBestOf > 1;
            if (_bracketWatch)
            {
                // The final as watched counts (a draw falls back to its coin; in a best of three it's played again).
                int played = b.FinalGamesA + b.FinalGamesB;
                bool settled = Brackets.SettleWatched(b, b.Final, winner, _campaign.Matchups);
                _campaign.Save();
                var champion = b.Champion >= 0 ? BracketPerson(b, b.Champion) : null;
                string line;
                if (bo3)
                    line = winner > 1 ? $"A draw: game {played + 1} of the final is played again."
                        : !settled ? "The final was already settled."
                        : b.Done ? $"{champion?.Name} wins the {b.Name}, {Math.Max(b.FinalGamesA, b.FinalGamesB)}-{Math.Min(b.FinalGamesA, b.FinalGamesB)}!"
                        : $"Game {played + 1} to {_players[winner].Name}: {b.FinalGamesA}-{b.FinalGamesB}.";
                else
                    line = !settled ? "The final was already settled."
                        : winner > 1 ? $"A draw: the coin gives the final to {champion?.Name}."
                        : $"{champion?.Name} wins the {b.Name}!";
                // Baz on a crowned champion, or the series going on.
                if (alley && b.Done && _campaign.AlleyLine("mc:crowned", champion?.Name ?? "", BracketFill(b, b.Final, champion?.Name ?? "")) is { } crowned)
                    line += $"  {crowned.speaker}: “{crowned.text}”";
                _campaign.Save();
                _view.SetResult(winner > 1 ? "Draw!" : $"{_players[winner].Name} wins!", winner <= 1 ? _players[winner].WinLine : "", line);
                _view.AppendLog($"{b.Name}: {line}");
                StartCoroutine(AfterWatchedFinal(line));
                return true;
            }
            int m = _bracketMatch!.Value;
            if (b.YourMatch() != m) return false;
            int themSlot = b.Opponent(m);
            var who = alley ? BracketPerson(b, themSlot) : BracketCharacter(b.Slots[themSlot].Character);
            var splash = b.Slots[themSlot].Splash;
            int reason = _runner?.Result.Reason ?? 0;
            bool loaner = _campaign.IsLoanerDeck(_pickDeck[0]);
            var result = Brackets.RecordYours(profile, _campaign.Library, b, winner, loaner, _campaign.Matchups);
            if (result == null) return false;
            if (!alley) _campaign.EndLiveDuel(); // a Locals game's record goes with this save
            // Card unlocks as after any result (a bracket duel is a Library duel: no band).
            _campaign.AfterDuel(result.Reward, "", _tally, loaner, tag: false);
            // The turn-40 line, as the log has it (alley brackets play under the cap); its first time is a scene.
            string capLine = "";
            if (alley && reason == DuelRunner.ReasonTurnCap && _turnCap != null)
            {
                capLine = winner > 1 ? DuelNarrator.TurnCapText(_turnCap.LastTurn, null, 0, 0)
                    : DuelNarrator.TurnCapText(_turnCap.LastTurn, winner == 0 ? "You" : who.Name, _turnCap.Lp(winner), _turnCap.Lp(1 - winner));
                result.Events.Add("turncap");
            }
            // Its scenes wait in the queue until the results screen takes them (as an alley table's do): if the menu, the editor
            // or the shop opens in the 2 s before it, the next campaign screen plays them.
            var queued = result.Events.ToList();
            AddGrantEvents(queued, result.Reward);
            _campaign.Save();
            var headline = winner > 1 ? "Draw!" : winner == 0 ? "You win!" : $"{who.Name} wins!";
            // Their last word: a splash regular's from the alley's pools, an out-of-towner's from its pool (outoftowner:win|lose:<id>,
            // else its own), anyone else's own (the results screen's too).
            var oot = OutOfTownerAt(b, b.Opponent(m));
            var pilotThere = PilotAt(b, b.Opponent(m));
            var said = winner > 1 ? "" : splash != null ? BracketRegularLine(winner == 0 ? "lose" : "win", splash)
                : oot != null ? _campaign.AlleyLine($"{BackAlley.OutOfTownerEvent}:{(winner == 0 ? "lose" : "win")}:{oot}", who.Name, BracketFill(b, m, who.Name))?.text ?? (winner == 0 ? who.LoseLine : who.WinLine)
                // A pilot on its peak build (the Invitational): its No Limits win line (pilot:win:<id>). Its pilot:lose lines hand
                // over the staked card ({card}) and there's no stake in a bracket, so a loss is its profile's line.
                : pilotThere != null && winner == 1 ? _campaign.AlleyLine($"pilot:win:{pilotThere}", who.Name, BracketFill(b, m, who.Name))?.text ?? who.WinLine
                : winner == 0 ? who.LoseLine : who.WinLine;
            if (alley) _campaign.Save(); // the regular's line (heard lines are saved)
            var quote = said;
            if (_runner?.Result.Reason == DuelRunner.ReasonYataLock) quote = winner == 0 ? $"{who.Name} is Yata-locked!" : "You're Yata-locked: no cards and no draws left.";
            ShowResultsSoon(queued, result.Reward.Grants, story => ShowBracketResults(m, who, result, winner, said, capLine, story),
                fill: alley ? BracketFill(b, m, who.Name) : null);
            _view.SetResult(headline, capLine.Length > 0 ? capLine : quote, $"{result.Reward.Summary}   ·   {profile.Dp:N0} DP");
            if (capLine.Length > 0) _view.AppendLog(capLine);
            if (quote.Length > 0) _view.AppendLog($"{who.Name}: \"{quote}\"");
            _view.AppendLog($"{b.Name}: {result.Reward.Summary}.");
            return true;
        }

        /// <summary>
        /// Baz's call on your result in an alley bracket: the title (mc:crowned), going out (mc:out, the round), or an upset
        /// in the round's other results (a lower seed through: mc:upset); null at a Locals or for none.
        /// </summary>
        private (string speaker, string text)? BracketCall(Bracket b, int m, BracketResult result, string handle)
        {
            if (!Brackets.IsAlley(b) || result.Replay || result.NextGame) return null;
            var fill = BracketFill(b, m, handle);
            if (result.Out) return _campaign.AlleyLine("mc:out", handle, fill);
            if (m == b.Final) return _campaign.AlleyLine("mc:crowned", handle, fill);
            bool Upset(int x)
            {
                var (sa, sb) = b.Pair(x);
                int w = b.Winners[x];
                return w >= 0 && sa >= 0 && sb >= 0 && b.Slots[w].Seed > b.Slots[w == sa ? sb : sa].Seed + 2;
            }
            return result.Settled.Any(Upset) ? _campaign.AlleyLine("mc:upset", handle, fill) : null;
        }

        /// <summary>After your bracket duel: the board for a moment, then the results and what's next (the bracket, the final's next game, or skipping to its end).</summary>
        private void ShowBracketResults(int m, Personality who, BracketResult result, int winner, string quote, string capLine, List<CampaignState.QueuedStory> story)
        {
            var profile = _campaign.Profile!;
            var b = profile.Bracket;
            // (The queued scenes, taken off the queue, are this screen's to play: with no bracket, back in the queue.)
            if (b == null) { _campaign.Requeue(story); _campaignScreen.Open(result.Reward.Summary); return; }
            bool alley = Brackets.IsAlley(b);
            if (alley) UiTheme.Current = UiTheme.BackAlley;
            var round = b.RoundName(b.RoundOf(m));
            bool bo3 = m == b.Final && b.FinalBestOf > 1;
            var (a, _) = b.Pair(b.Final);
            int mine = a == b.YourSlot ? b.FinalGamesA : b.FinalGamesB, theirs = a == b.YourSlot ? b.FinalGamesB : b.FinalGamesA;
            var r = new CampaignScenes.Results
            {
                Won = winner == 0,
                Headline = winner > 1 ? "Draw" : winner == 0 ? "Victory!" : "Defeat",
                Speaker = who.Name,
                Quote = quote,
                Circuit = $"{b.Name} · {round}{(bo3 && result.Game > 0 ? $", game {result.Game}" : "")} against {who.DisplayName}",
                TotalDp = profile.Dp,
                // A chip per round: won once your match in it is.
                Progress = Enumerable.Range(0, b.Rounds).Select(k => b.MatchesIn(k).Any(x => b.IsYours(x) && b.Winners[x] == b.YourSlot)).ToList(),
                ProgressItem = "Round", ProgressTitle = "YOUR BRACKET",
            };
            if (result.Out) r.Earnings.Add((Brackets.PlacementText(result.Placement) + (b.Loaned ? " (half: loaner deck)" : ""), result.Reward.Dp));
            foreach (var up in result.Reward.LevelUps.Where(u => u.Dp > 0)) r.Earnings.Add(($"Level {up.Level} reward", up.Dp));
            var (into, needed) = Leveling.Progress(profile.Exp);
            r.Exp = (result.Reward.Exp, profile.Level, into, needed);
            int wins = profile.WinsAgainst(who.Id), losses = profile.Losses.TryGetValue(who.Id, out var l) ? l : 0;
            var history = alley ? Brackets.AlleyRecord(profile, b.Kind) : profile.Locals.TryGetValue(b.Event, out var rec) ? rec : new LocalsRecord();
            r.Record = (who.Id == BackAlley.RegularId ? "" : $"Against {who.Name}: <b>{wins}</b> win{(wins == 1 ? "" : "s")}, <b>{losses}</b> loss{(losses == 1 ? "" : "es")}\n")
                       + $"{b.Name}: {history.Entries} entr{(history.Entries == 1 ? "y" : "ies")} · {history.Titles} title{(history.Titles == 1 ? "" : "s")}";
            string outcome;
            if (result.Replay) outcome = bo3 ? "A draw: the game is played again" : "A draw: the match is played again";
            else if (result.NextGame) outcome = $"Game {result.Game} to {(winner == 0 ? "you" : who.Name)} · {mine}-{theirs} · game {result.Game + 1} next"
                                                + (b.FinalLastLoser >= 0 ? (b.FinalLastLoser == (a == b.YourSlot ? 0 : 1) ? " (you choose who goes first)" : $" ({who.Name} chooses who goes first)") : "");
            else if (result.Out) outcome = $"{Brackets.PlacementText(result.Placement)}{(bo3 ? $" ({mine}-{theirs})" : "")} · +{result.Reward.Dp:N0} DP";
            else if (m == b.Final) outcome = $"{b.Name} won!{(bo3 ? $" ({mine}-{theirs})" : "")} Your prizes wait on the bracket";
            else outcome = $"Through to the {(b.RoundOf(m) + 2 == b.Rounds ? "final" : b.RoundName(b.RoundOf(m) + 1).ToLowerInvariant())}";
            r.Unlocks.Add(outcome);
            if (capLine.Length > 0) r.Unlocks.Add(capLine);
            if (BracketCall(b, m, result, who.Name) is { } call) r.Unlocks.Add($"{call.speaker}: “{call.text}”");
            foreach (var up in result.Reward.LevelUps)
                r.Unlocks.Add($"Level {up.Level}!" + (up.Tokens > 0 ? $" +{up.Tokens} Card Token{(up.Tokens > 1 ? "s" : "")}" : ""));
            AddGrants(r, result.Reward);
            if (alley) _campaign.Save(); // Baz's line (heard lines are saved)
            _scenes.LastResults = r;
            var settled = result.Settled;
            void Then(Action then) => PlayResultStory(story, then);
            var buttons = new List<(string, Action)>();
            if (result.Out && !b.Done)
            {
                buttons.Add(("Skip to the end", () => Then(() =>
                {
                    var rest = Brackets.SkipToEnd(b, _campaign.Matchups);
                    _campaign.Save();
                    ShowBracket("", settled.Concat(rest).ToList());
                })));
                buttons.Add(("Follow the bracket", () => Then(() => ShowBracket("", settled))));
            }
            else if (result.NextGame)
            {
                // The final goes on: its next game's versus (the deck can change), or the bracket.
                buttons.Add(($"Game {result.Game + 1}", () => Then(() => ShowBracketVersus())));
                buttons.Add(("Back to the bracket", () => Then(() => ShowBracket())));
            }
            else buttons.Add(("Back to the bracket", () => Then(() => ShowBracket("", settled))));
            _scenes.ShowResults(r, continueAction: () => Then(() => ShowBracket("", settled)), rematch: Requeued(story, () => ShowBracket()), shop: Requeued(story, () => _shop.Open()), buttons: buttons);
        }

        // ---- The watched final ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Watch the final (you're out, it's CPU against CPU): a real duel from its fixed seed, and its result counts. A
        /// best-of-three final is watched a game at a time (each game its own seed; game 2 and 3 have no toss: the last
        /// game's loser chooses).
        /// </summary>
        private void WatchBracketFinal()
        {
            var b = _campaign.Profile?.Bracket;
            if (b == null || !b.Pending(b.Final) || b.IsYours(b.Final)) { ShowBracket(); return; }
            var (a, bb) = b.Pair(b.Final);
            BracketEntrant left = b.Slots[a], right = b.Slots[bb];
            bool alley = Brackets.IsAlley(b), bo3 = b.FinalBestOf > 1;
            int Seat(BracketEntrant e) => Math.Max(0, _roster.FindIndex(p => p.Id == e.Character));
            string Deck(BracketEntrant e, int seat) =>
                alley && Alley.IsAlleyDeck(e.Deck) ? e.Deck : _allDecks.Contains(e.Deck) ? e.Deck : OwnDecks(_roster[_pickCharacter[seat]])[0];
            _pickCharacter[0] = Seat(left);
            _pickCharacter[1] = Seat(right);
            _pickDeck[0] = Deck(left, 0);
            _pickDeck[1] = Deck(right, 1);
            _bracketSides[0] = alley ? BracketPerson(b, a) : null;
            _bracketSides[1] = alley ? BracketPerson(b, bb) : null;
            _bracketSeats[0] = alley ? a : -1;
            _bracketSeats[1] = alley ? bb : -1;
            int game = bo3 ? b.FinalGamesA + b.FinalGamesB + 1 : 0;
            // Each watched game of a best of three gets its own duel seed (a drawn one is played again, fresh).
            seed = (int)(Brackets.DuelSeed(b, b.Final, bo3 ? b.Attempt % 16 : 0) & 0x7FFFFFFF);
            if (bo3)
            {
                b.Attempt++;
                _campaign.Save();
            }
            _bracketScreen.Close();
            if (alley) UiTheme.Current = UiTheme.BackAlley;
            string Name(int slot) => alley ? BracketPerson(b, slot).Name : BracketCharacter(b.Slots[slot].Character).Name;
            string Plays(BracketEntrant e) => e.Splash?.Base ?? e.Deck;
            bool? leftChooses = bo3 && game > 1 && b.FinalLastLoser >= 0 ? b.FinalLastLoser == 0 : (bool?)null;
            CoinToss(Name(a), Name(bb), Plays(left), Plays(right), () =>
            {
                ShowPicks();
                _paused = false;
                NewDuel(bracketWatch: true);
                UpdateControls();
            }, youChoose: false, leftChooses: leftChooses, game: game);
        }

        private System.Collections.IEnumerator AfterWatchedFinal(string line)
        {
            int serial = _duelSerial;
            yield return new WaitForSecondsRealtime(3f);
            // (Another duel since, or a screen over the board: the bracket waits for its own button.)
            if (!_bracketWatch || !ResultsDue(serial)) yield break;
            ShowBracket(line);
        }

        // ---- The finish ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// "Settle now" on a held-back Invitational (its No Limits Packs can't be opened here): the launch's settle in the
        /// session (CampaignState.SettleHeldInvitational: paid now, the packs owed until they can be opened), saved, then the
        /// chalkboard with the notice and the title's scenes.
        /// </summary>
        private void SettleHeldBracket()
        {
            if (_campaign.SettleHeldInvitational() is not BracketReward reward) return;
            _lastBracketReward = reward;
            _campaign.Save();
            Sounds.Play("dp", 0.7f);
            _bracketScreen.Close();
            _campaignScreen.OpenChalkboard();
        }

        /// <summary>
        /// A finished bracket's Finish: pays it (a title's DP, its packs opened into your collection) and moves it into its
        /// history; the packs revealed on the pack-opening screen (a Locals' Prize Packs; the alley's Contraband Packs, the
        /// Invitational's No Limits Packs, Moth's word on the first); the story (a first title's scene); then the cup, or Baz's chalkboard.
        /// </summary>
        private void FinishBracket()
        {
            var profile = _campaign.Profile;
            var b = profile?.Bracket;
            if (profile == null || b == null || !b.Done) return;
            int cup = BracketCup();
            bool alley = Brackets.IsAlley(b);
            // The Invitational's packs are No Limits Packs (the ready builds' ever-Forbidden cards); the others' Contraband Packs.
            var noLimits = b.Kind == Brackets.InvitationalKind ? _campaign.NoLimitsPacks() : null;
            var reward = alley ? Brackets.Finish(profile, _campaign.Library, b, _campaign.Prizes, _campaign.Contraband, noLimits: noLimits)
                : Brackets.Finish(profile, _campaign.Library, b, _campaign.Prizes);
            if (reward == null)
            {
                // Held back (Brackets.FinishBlocked: the No Limits Packs can't be opened here): nothing paid, the bracket kept,
                // done, in the save; the bracket screen says why, and Finish (here, from the chalkboard, after a relaunch) tries again.
                if (Brackets.FinishBlocked(b, noLimits) is string why)
                {
                    Sounds.Play("error");
                    ShowBracket($"<color={UiTheme.Current.BadHex}>{why}</color> Nothing's paid yet: the bracket is kept, and Finish tries again"
                                + (b.Kind == Brackets.InvitationalKind ? $" (or {BracketScreen.SettleLabel.Split('·')[0].Trim()}: paid now, the packs owed)." : "."));
                }
                return;
            }
            _lastBracketReward = reward;
            _campaign.Save();
            _bracketScreen.Close();
            var events = reward.Events.ToList();
            void BackToCup() => PlayStory(events, "", () =>
            {
                if (alley) { _campaignScreen.OpenChalkboard($"<b>{reward.Name}</b>: {reward.Summary}."); return; }
                if (cup >= 0) _campaignScreen.SelectLibrary(cup);
                _campaignScreen.Open($"<b>{reward.Name}</b>: {reward.Summary}.");
            });
            if (reward.Packs.Count > 0)
            {
                if (alley) _shop.OpenPrizes($"{reward.Name} · {Brackets.PackNameFor(b.Kind)}", reward.Packs, BackToCup, b.Seed, contraband: true, packName: Brackets.PackNameFor(b.Kind));
                else _shop.OpenPrizes($"{reward.Name} · Prize Pack", reward.Packs, BackToCup, b.Seed);
            }
            else BackToCup();
        }

        // ---- -campaigncheck ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Library Locals, with the first cup cleared and DP to spend: the Locals button's label; the entry dialog; Enter takes
        /// the fee and opens the bracket (no slots overlap); your match's Duel opens its versus under the cup's rules; a loss
        /// pays its placement and Skip to end settles the bracket; a forced title's Finish reveals a 5-card Prize Pack. The
        /// save is put back as it was after (DP, EXP, records, the prize copies).
        /// </summary>
        private System.Collections.IEnumerator BracketCheck(Action<string, bool> report, Func<string, System.Collections.IEnumerator> shot)
        {
            var profile = _campaign.Profile!;
            var lib = _campaign.Library;
            const int cup = 0;
            var c = lib.Cups[cup];
            var cupId = Library.Id(c);
            // What the check changes, to put back.
            var dp = SnapshotDp(profile);
            int exp = profile.Exp, tokens = profile.Tokens, entered = profile.BracketsEntered, played = profile.DuelsPlayed;
            var bracketBefore = profile.Bracket;
            var copiesBefore = new HashSet<CardCopy>(profile.Collection.All);
            var locals = profile.Locals.ToDictionary(p => p.Key, p => p.Value);
            var wins = profile.Wins.ToDictionary(p => p.Key, p => p.Value);
            var lost = profile.Losses.ToDictionary(p => p.Key, p => p.Value);
            var streaks = profile.LossStreaks.ToDictionary(p => p.Key, p => p.Value);
            var seen = new HashSet<string>(profile.Seen);
            var cleared = Enumerable.Range(0, c.Matches.Count).Select(m => lib.Key(cup, m)).Where(k => !profile.LibraryWins.ContainsKey(k)).ToList();
            foreach (var k in cleared) profile.LibraryWins[k] = 1;
            profile.Bracket = null;
            // The scenes these steps raise are marked seen (the check doesn't click through stories).
            foreach (var scene in _story.Pending(profile, new[] { Brackets.EntryEvent, Brackets.WinEvent, Brackets.WinEvent + ":" + Brackets.NameOf(c) }))
                Story.MarkSeen(profile, scene);
            DebugSetDp(profile, Brackets.LocalsEntry + 500);

            _campaignScreen.DebugShowLibrary(cup);
            yield return new WaitForSecondsRealtime(0.4f);
            var label = _campaignScreen.DebugLocalsLabel(cup);
            report($"a cleared cup's Locals button reads \"{label}\"", label == $"Locals · {Brackets.LocalsEntry:N0}");
            _campaignScreen.DebugLocals(cup);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return shot("locals-entry");
            report($"it opens the entry dialog over the cup ({_bracketScreen.DebugEntryRows} seeds)", _bracketScreen.EntryOpen && _bracketScreen.DebugEntryRows == Bracket.LocalsSize);
            int before = profile.Dp;
            _bracketScreen.DebugEnter();
            for (float until = Time.realtimeSinceStartup + 3; !_bracketScreen.IsOpen && Time.realtimeSinceStartup < until;) yield return null;
            var b = profile.Bracket;
            report($"Enter takes {before - profile.Dp:N0} DP and opens the bracket screen ({b?.Name})",
                b != null && profile.Dp == before - Brackets.LocalsEntry && _bracketScreen.IsOpen && !_campaignScreen.IsOpen);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return shot("bracket");
            var (boxes, overlaps) = _bracketScreen.DebugOverlaps();
            report($"no bracket slots overlap ({boxes} boxes{(overlaps.Count > 0 ? ": " + string.Join(", ", overlaps.Take(3)) : "")})", boxes == 15 && overlaps.Count == 0);
            if (b == null) { RestoreBrackets(); yield break; }

            // Your match's Duel: the versus, under the cup's rules.
            var cupRules = _campaign.CupRules(cup);
            _bracketVersusRules = null;
            _bracketScreen.DebugDuel();
            for (float until = Time.realtimeSinceStartup + 3; !_scenes.IsOpen && Time.realtimeSinceStartup < until;) yield return null;
            yield return new WaitForSecondsRealtime(0.4f);
            yield return shot("bracket-versus");
            report($"your match's Duel opens its versus under the {c.Name}'s rules ({_bracketVersusRules?.List?.Name}, cards out by {_bracketVersusRules?.Pool})",
                _scenes.IsOpen && _scenes.Buttons.ContainsKey("Duel!") && !_bracketScreen.IsOpen
                && _bracketVersusRules != null && _bracketVersusRules.Pool == cupRules.Pool && _bracketVersusRules.List?.Name == cupRules.List?.Name);
            _scenes.Close();

            // Stuck? at a Locals: after losses in a row at this round of it (Brackets.Key), the versus opens the advice by itself,
            // as a cup match's does (what was measured against them at the cup; Make it yours under the cup's rules).
            var stuckKey = Brackets.Key(b, b.YourMatch());
            var stuckThem = b.Slots[b.Opponent(b.YourMatch())];
            profile.LossStreaks[stuckKey] = StuckAfter;
            ShowBracketVersus();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return shot("bracket-stuck");
            var stuckTips = Stuck(stuckKey, BracketMeasured(stuckThem), false, new[] { stuckThem.Character }, stuckThem.Deck) ?? new List<string>();
            report($"after {StuckAfter} losses in a row at this round, the bracket versus opens the Stuck? advice ({string.Join(" / ", stuckTips.Skip(1).Take(2))})"
                   + (_scenes.Buttons.ContainsKey("Own stuck deck") ? ", with Make it yours" : ""),
                _scenes.ScoutingOpen && stuckTips.Count >= 2 && _scenes.Buttons.ContainsKey("Duel!"));
            if (_scenes.Buttons.TryGetValue("Close scouting", out var closeStuck)) closeStuck();
            profile.LossStreaks.Remove(stuckKey);
            _scenes.Close();

            // A loss in the quarter-final: its placement paid now; Skip to end settles the rest.
            int dpOut = profile.Dp;
            var result = Brackets.RecordYours(profile, lib, b, 1, false, _campaign.Matchups);
            _campaign.Save();
            report($"a loss pays its placement at once ({result?.Reward.Summary})",
                result != null && result.Out && result.Placement == 3 && profile.Dp - dpOut == Brackets.LocalsPay[3] + (result.Reward.LevelUps.Sum(u => u.Dp)));
            ShowBracket("", result?.Settled);
            yield return new WaitForSecondsRealtime(0.3f);
            _bracketScreen.DebugSkip();
            for (float until = Time.realtimeSinceStartup + 6; _bracketScreen.Animating && Time.realtimeSinceStartup < until;) yield return null;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return shot("bracket-skipped");
            report($"Skip to end settles the bracket (champion: {(b.Champion >= 0 ? b.Slots[b.Champion].Character : "none")})", b.Done && b.Champion >= 0 && !b.Slots[b.Champion].You);
            _bracketScreen.DebugFinish();
            yield return new WaitForSecondsRealtime(0.4f);
            report("Finish closes the bracket into the cup's history (best: out in the quarter-finals) and returns to the cup",
                profile.Bracket == null && profile.Locals.TryGetValue(cupId, out var rec) && rec.Best == 3 && _campaignScreen.IsOpen);

            // A forced title: every match of yours won, then Finish reveals its Prize Packs.
            DebugAddDp(profile, Brackets.LocalsEntry);
            var title = Brackets.EnterLocals(profile, lib, cup, _campaign.Ratings, profile.Deck);
            while (title != null && title.YourMatch() >= 0) Brackets.RecordYours(profile, lib, title, 0, false, _campaign.Matchups);
            _campaign.Save();
            ShowBracket();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return shot("bracket-title");
            _bracketScreen.DebugFinish();
            yield return new WaitForSecondsRealtime(0.8f);
            yield return _shop.DebugRevealPrize();
            yield return new WaitForSecondsRealtime(1.5f);
            yield return shot("prize-pack");
            var prize = _shop.DebugPrize();
            report($"a title's Finish opens its Prize Packs: {prize.cards} cards, {prize.revealed} revealed, {prize.left} more pack(s); "
                   + $"titles at the cup: {(profile.Locals.TryGetValue(cupId, out var won) ? won.Titles : 0)}",
                title != null && prize.open && prize.cards == PrizePacks.Size && prize.revealed == PrizePacks.Size && prize.left == Brackets.LocalsPacks[0] - 1
                && profile.Bracket == null && won != null && won.Titles == 1);
            _shop.DebugFinishPrizes();
            yield return new WaitForSecondsRealtime(0.4f);
            report("the prizes' Done returns to the cup", _campaignScreen.IsOpen && _campaignScreen.LibraryCup == cup && !_shop.IsOpen);
            _campaignScreen.Close();
            RestoreBrackets();

            void RestoreBrackets()
            {
                _bracketScreen.Close();
                // The collection exactly as it was: prize copies (and any set-completion copy) out, and any copy a prize
                // replaced back in (a 4th copy swaps out your least valuable one; without it a deck can come up short).
                var now = profile.Collection.All.ToList();
                foreach (var copy in now.Where(x => !copiesBefore.Contains(x))) profile.Collection.Remove(copy);
                foreach (var copy in copiesBefore.Where(x => !now.Contains(x))) profile.Collection.Insert(copy);
                foreach (var k in cleared) profile.LibraryWins.Remove(k);
                RestoreDp(profile, dp);
                profile.Exp = exp;
                profile.Tokens = tokens;
                profile.BracketsEntered = entered;
                profile.DuelsPlayed = played;
                profile.Bracket = bracketBefore;
                profile.Locals.Clear();
                foreach (var p in locals) profile.Locals[p.Key] = p.Value;
                profile.Wins.Clear();
                foreach (var p in wins) profile.Wins[p.Key] = p.Value;
                profile.Losses.Clear();
                foreach (var p in lost) profile.Losses[p.Key] = p.Value;
                profile.LossStreaks.Clear();
                foreach (var p in streaks) profile.LossStreaks[p.Key] = p.Value;
                profile.Seen.IntersectWith(seen);
                _campaign.Save();
            }
        }
    }
}
