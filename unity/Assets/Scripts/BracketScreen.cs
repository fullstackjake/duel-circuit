using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The bracket screen and its entry dialogs, read from <see cref="Profile.Bracket"/>. Library Locals
    /// (BRACKETS-SPEC-2026-11-01 §4): a mirrored 8-player bracket, quarter-finals at the edges, semi-finals inside them, the
    /// final in the middle, its entry dialog over a cup's panel. The Back Alley's (docs/dev/ALLEY-SPEC-2026-11-01.md section 11,
    /// in the alley theme): the Alley Brawl on the same 8 layout, the Midnight Open on a mirrored 16 (240×72 slots, the round
    /// of 16 at the edges, the final in the centre) with its best-of-three final's game pips under the final's slots, and
    /// their entry dialog over Baz's chalkboard (field, entry, pay, the Contraband Pack's odds, the Alley list's rule). Each
    /// slot shows the duelist, their deck, "#seed · Elo" and a state chip: pending "—", ▶ YOUR MATCH (red border; the
    /// screen's one red button is its Duel), ♛ won (the path lit gold), OUT (dimmed). The screen only reads the bracket and
    /// settles CPU matches (Follow, Skip to end); your duels, the watched final and the prizes are the owner's (the events).
    /// </summary>
    public sealed class BracketScreen : MonoBehaviour
    {
        private const float Line = 3, SecondsPerMatch = 0.4f;

        /// <summary>
        /// A bracket size's layout (1920×1080 reference): the slot, the columns (the first round left ... the final ... the
        /// first round right), the first round's slots down one side, the final's two slots, the plaque, the captions, and
        /// what sits inside a slot (the portrait well, the four text lines).
        /// </summary>
        private sealed class Layout
        {
            public float W, H, PlaqueY, CaptionY, Well, TextX, NameY, NameH, DeckY, DeckH, EloY, EloH, ChipY, ChipH;
            public float[] Columns = Array.Empty<float>(), FirstY = Array.Empty<float>(), FinalY = Array.Empty<float>();
            public int NameSize, DeckSize, EloSize, ChipSize, InitialSize, YouSize, UndecidedSize;
            public float TextW => W - TextX - 8;
        }

        /// <summary>8 players: the Locals' layout as it always was (300×96 slots).</summary>
        private static readonly Layout Eight = new Layout
        {
            W = 300, H = 96, Columns = new float[] { 60, 435, 810, 1185, 1560 }, FirstY = new float[] { 180, 290, 500, 610 }, FinalY = new float[] { 317, 473 },
            PlaqueY = 600, CaptionY = 128, Well = 80, TextX = 96, NameY = 6, NameH = 24, DeckY = 30, DeckH = 20, EloY = 50, EloH = 18, ChipY = 70, ChipH = 20,
            NameSize = 18, DeckSize = 14, EloSize = 13, ChipSize = 13, InitialSize = 36, YouSize = 22, UndecidedSize = 15,
        };

        /// <summary>16 players: seven columns of 240×72 slots, the round of 16 at the edges (pairs 8 px apart, 26 between pairs).</summary>
        private static readonly Layout Sixteen = new Layout
        {
            W = 240, H = 72, Columns = new float[] { 30, 300, 570, 840, 1110, 1380, 1650 }, FirstY = new float[] { 150, 230, 328, 408, 506, 586, 684, 764 },
            FinalY = new float[] { 326, 480 }, PlaqueY = 606, CaptionY = 110, Well = 56, TextX = 70, NameY = 3, NameH = 20, DeckY = 23, DeckH = 15,
            EloY = 38, EloH = 14, ChipY = 54, ChipH = 15, NameSize = 16, DeckSize = 12, EloSize = 11, ChipSize = 11, InitialSize = 26, YouSize = 16, UndecidedSize = 13,
        };

        private static Layout LayoutOf(Bracket b) => b.Size == 16 ? Sixteen : Eight;

        private CampaignState _state = null!;
        private DeckLibrary _library = null!;
        private RectTransform _root = null!, _view = null!;
        private RectTransform? _entry;
        private Text? _entryNote;
        private int _entryCup = -1, _entryRows;
        /// <summary>The open entry dialog's Enter is on (off when an alley field couldn't be drawn).</summary>
        private bool _entryEnterable = true;
        /// <summary>The alley bracket's kind the entry dialog is for ("" for a cup's Locals).</summary>
        private string _entryKind = "";
        private readonly List<string> _entryNames = new List<string>();
        private string _message = "";
        /// <summary>Matches settled but not shown yet: revealed one by one (<see cref="SecondsPerMatch"/>).</summary>
        private readonly HashSet<int> _hidden = new HashSet<int>();
        /// <summary>The out-of-towners (No Limits pilots) the bracket last drew, in slot order (for the checks).</summary>
        private readonly List<string> _outOfTowners = new List<string>();
        public IReadOnlyList<string> DebugOutOfTowners => _outOfTowners;
        /// <summary>The slots and the champion's plaque as laid out (reference pixels), for the overlap check.</summary>
        private readonly List<(string what, Rect rect)> _boxes = new List<(string, Rect)>();
        /// <summary>A best-of-three final's pips as last drawn (side A, side B), for the checks.</summary>
        private (string a, string b) _pips = ("", "");
        /// <summary>The Contraband Pack's odds (worked out once: the pool doesn't change while the app runs).</summary>
        private PrizeOdds? _contrabandOdds;
        /// <summary>The No Limits Pack's odds, and the pool size they were worked out for (its pool follows the ready builds).</summary>
        private PrizeOdds? _noLimitsOdds;
        private int _noLimitsOddsPool = -1;

        /// <summary>Your match's Duel (its versus next), Back (the bracket is kept), Watch the final, Finish (the bracket is done).</summary>
        public event Action? DuelClicked, BackClicked, WatchClicked, FinishClicked;
        /// <summary>"Settle now" on a held-back Invitational: paid now, its No Limits Packs owed (CampaignState.SettleHeldInvitational).</summary>
        public event Action? SettleClicked;
        public const string SettleLabel = "Settle now · packs owed";
        /// <summary>The entry dialog's Enter went through: the cup's bracket is running (fee paid, saved).</summary>
        public event Action<int>? Entered;
        /// <summary>The alley entry dialog's Enter went through: that kind's bracket is running (entry paid, saved).</summary>
        public event Action<string>? AlleyEntered;
        /// <summary>Scout: the scouting report on a duelist in the bracket (who, and their deck).</summary>
        public event Action<List<(string who, string deck)>>? ScoutClicked;

        /// <summary>The bracket screen is showing (not just the entry dialog).</summary>
        public bool IsOpen => _root != null && _root.gameObject.activeSelf && _view.gameObject.activeSelf;
        public bool EntryOpen => _entry != null;
        public bool Animating => _hidden.Count > 0;

        public void Init(CampaignState state, DeckLibrary library)
        {
            _state = state;
            _library = library;
            // Above the campaign screen (12), under the versus and results scenes and the shop (14): the dialog sits on the cup panel.
            _root = Ui.Overlay(transform, "Bracket", 13);
            _view = Ui.Stretched("View", _root, Vector2.zero, Vector2.one);
            _view.gameObject.SetActive(false);
            _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows the running bracket. <paramref name="animate"/>: matches just settled, revealed in order at 0.4 s each.
        /// With no bracket running it goes Back.
        /// </summary>
        public void Open(string message = "", IReadOnlyList<int>? animate = null)
        {
            if (_state.Profile?.Bracket == null) { Close(); BackClicked?.Invoke(); return; }
            CloseEntry();
            StopAllCoroutines();
            _root.gameObject.SetActive(true);
            _view.gameObject.SetActive(true);
            _message = message;
            _hidden.Clear();
            if (animate != null) _hidden.UnionWith(animate);
            Refresh();
            if (_hidden.Count > 0) StartCoroutine(Reveal(animate!.Where(_hidden.Contains).ToList()));
        }

        public void Close()
        {
            StopAllCoroutines();
            _hidden.Clear();
            CloseEntry();
            _view.gameObject.SetActive(false);
            _root.gameObject.SetActive(false);
        }

        private IEnumerator Reveal(List<int> matches)
        {
            foreach (var m in matches)
            {
                yield return new WaitForSecondsRealtime(SecondsPerMatch);
                _hidden.Remove(m);
                Sounds.Play("flip", 0.6f);
                Refresh();
            }
            if (_state.Profile?.Bracket is Bracket b && b.Done) Sounds.Play("dp");
        }

        // ---- The bracket ------------------------------------------------------------------------------------------------

        private Circuit? CupOf(Bracket b) => Brackets.IsAlley(b) ? null : _state.Library.Cups.FirstOrDefault(c => Library.Id(c) == b.Event);

        private static Personality? Who(Circuit? cup, string id) =>
            cup?.Matches.Concat(cup.Locals).Select(m => m.Character).FirstOrDefault(c => c.Id == id);

        /// <summary>A duelist's name: "You"; a splash regular's handle; a cup's character as the cup has them; else the cast's.</summary>
        private string NameOf(Circuit? cup, BracketEntrant e) =>
            e.You ? "You" : e.Splash != null ? e.Splash.Handle
            : Who(cup, e.Character)?.Name ?? (_state.Cast.TryGetValue(e.Character, out var p) ? p.Name : e.Character);

        /// <summary>A match's winner as shown (-1 while it's still to be revealed).</summary>
        private int Shown(Bracket b, int m) => _hidden.Contains(m) ? -1 : b.Winners[m];

        /// <summary>Round <paramref name="round"/>'s first match (the bracket's own numbering: <see cref="Bracket.Pair"/>).</summary>
        private static int Start(Bracket b, int round) => b.Size - (b.Size >> round);

        /// <summary>The match feeding side <paramref name="k"/> of a later round's match <paramref name="m"/>.</summary>
        private static int Feeder(Bracket b, int m, int k)
        {
            int round = b.RoundOf(m);
            return Start(b, round - 1) + 2 * (m - Start(b, round)) + k;
        }

        /// <summary>A match's two sides as shown (-1 for one still to be revealed).</summary>
        private (int a, int b) ShownPair(Bracket b, int m) =>
            b.RoundOf(m) == 0 ? b.Pair(m) : (Shown(b, Feeder(b, m, 0)), Shown(b, Feeder(b, m, 1)));

        /// <summary>Whether a match before the final sits on the left half (the right half mirrors it).</summary>
        private static bool LeftHalf(Bracket b, int m)
        {
            int round = b.RoundOf(m);
            return m - Start(b, round) < (b.Size >> (round + 1)) / 2;
        }

        /// <summary>The column a match before the final is drawn in (the final's is <c>Rounds - 1</c>).</summary>
        private static int ColumnOf(Bracket b, int m)
        {
            int round = b.RoundOf(m);
            return LeftHalf(b, m) ? round : 2 * b.Rounds - 2 - round;
        }

        /// <summary>The top of side <paramref name="k"/>'s slot at match <paramref name="m"/>: the first round's as laid out, a later one level with the middle of the match feeding it.</summary>
        private static float SlotY(Bracket b, Layout L, int m, int k)
        {
            if (m == b.Final) return L.FinalY[k];
            int round = b.RoundOf(m);
            if (round == 0)
            {
                int i = m - Start(b, 0), half = (b.Size >> 1) / 2;
                return L.FirstY[2 * (i % half) + k];
            }
            int f = Feeder(b, m, k);
            return (SlotY(b, L, f, 0) + SlotY(b, L, f, 1)) / 2;
        }

        private static string Initial(string name) => name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?";

        /// <summary>Each placement's name on the prize ladder (0 the title ... 4 the round of 16).</summary>
        private static readonly string[] Places = { "Champion", "Finalist", "Semi-finals", "Quarter-finals", "Round of 16" };

        /// <summary>The pack a bracket's places pay (<see cref="Brackets.PackNameFor"/>): Prize Packs at a Locals, Contraband Packs in the alley, No Limits Packs at the Invitational.</summary>
        private static string PackName(Bracket b) => Brackets.PackNameFor(b.Kind);

        /// <summary>The Invitational's rules as its header says them (the Alley's: "Alley list").</summary>
        private const string NoLimitsRules = "No list, three of anything";

        private static string Packs(string name, int n) => n == 1 ? name : $"{n} {name}s";

        /// <summary>A placement's packs in this bracket (its kind's ladder).</summary>
        private static int PacksAt(Bracket b, int place)
        {
            var packs = Brackets.PacksFor(b.Kind);
            return place >= 0 && place < packs.Length ? packs[place] : 0;
        }

        /// <summary>
        /// The Card Tokens a first title pays here (<see cref="Brackets.TokensFor"/>, once per <see cref="Brackets.TokenKeyFor"/>:
        /// the Midnight Open's 1, the Invitational's 2); 0 once paid, and at the Brawl and the Locals.
        /// </summary>
        private int TitleTokens(Bracket b) => FirstTitleTokens(_state.Profile, b.Kind);

        /// <summary>A kind's first-title Card Tokens still to come (0 once its token key is seen, or for a kind with none).</summary>
        private static int FirstTitleTokens(Profile? profile, string kind)
        {
            int tokens = Brackets.IsAlleyKind(kind) ? Brackets.TokensFor(kind) : 0;
            var key = Brackets.TokenKeyFor(kind);
            return tokens > 0 && key.Length > 0 && profile?.Seen.Contains(key) == false ? tokens : 0;
        }

        /// <summary>"a Card Token", "2 Card Tokens".</summary>
        private static string TokensText(int n) => n == 1 ? "a Card Token" : $"{n} Card Tokens";

        /// <summary>The games each side of a best-of-three final has won ("1-0", yours first when it's yours).</summary>
        private static (int yours, int theirs) Games(Bracket b)
        {
            var (a, _) = b.Pair(b.Final);
            return a == b.YourSlot || b.YourSlot < 0 ? (b.FinalGamesA, b.FinalGamesB) : (b.FinalGamesB, b.FinalGamesA);
        }

        private void Refresh()
        {
            var T = UiTheme.Current;
            var profile = _state.Profile;
            var b = profile?.Bracket;
            for (int i = _view.childCount - 1; i >= 0; i--) Destroy(_view.GetChild(i).gameObject);
            _boxes.Clear();
            _outOfTowners.Clear();
            _pips = ("", "");
            if (profile == null || b == null) return;
            var L = LayoutOf(b);
            bool alley = Brackets.IsAlley(b);
            var cup = CupOf(b);
            int cupIndex = cup == null ? -1 : _state.Library.Cups.ToList().IndexOf(cup);
            int yours = b.YourMatch(), round = b.CurrentRound(), place = b.Placement();

            Ui.Image(Ui.Stretched("Background", _view, Vector2.zero, Vector2.one), T.Base);
            Ui.Image(Ui.Box(_view, 0, 0, 1920, 90), T.Deep);
            // The alley's brackets: Baz's chalk under a brick header band.
            if (T.IsAlley) AlleyMaterials.Header(_view, 0, 0, 1920, 90, T.Sign);
            var title = Ui.Text(Ui.Box(_view, 40, 0, 900, 90), b.Name.ToUpperInvariant(), 34, T.Sign, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            T.Title(title);
            var stage = b.Done ? "Finished" : b.YouOut ? $"{Brackets.PlacementText(place)} · {b.RoundName(round)}" : $"{b.RoundName(round)}";
            string rulesLine;
            if (alley) rulesLine = (b.Kind == Brackets.InvitationalKind ? NoLimitsRules : "Alley list") + ", turn 40, higher LP wins" + (b.FinalBestOf > 1 ? $" · final best of {b.FinalBestOf}" : "");
            else
            {
                var rules = cupIndex >= 0 ? _state.CupRules(cupIndex) : null;
                rulesLine = $"{(rules?.List?.Name ?? cup?.Banlist ?? "")} list, cards out by {CampaignScreen.PoolMonth(cup?.Pool ?? "")}";
            }
            // (The alley's header has tape across its right end: the line stops short of it.)
            var sub = Ui.Text(Ui.Box(_view, 900, 0, T.IsAlley ? 780 : 980, 90), $"{stage}  ·  {rulesLine}  ·  <color={T.GoldHex}>{profile.Dp:N0} DP</color>", 18, T.TextMuted, TextAnchor.MiddleRight);
            sub.supportRichText = true;

            // Column captions: the first round at the edges, the final in the middle.
            for (int c = 0; c < L.Columns.Length; c++)
            {
                int r = c < b.Rounds ? c : 2 * b.Rounds - 2 - c;
                bool final = r == b.Rounds - 1;
                var caption = final ? (b.FinalBestOf > 1 ? $"FINAL · BEST OF {b.FinalBestOf}" : "FINAL")
                    : b.RoundName(r) == "Round of 16" ? "ROUND OF 16" : b.RoundName(r).ToUpperInvariant() + "S";
                Ui.Text(Ui.Box(_view, L.Columns[c], L.CaptionY, L.W, 30), caption, 16, final ? T.GoldSoft : T.Caption);
            }

            // The lines first, so the slots sit on them: dim, then the paths walked lit gold.
            Connectors(b, L);

            // Every match's two slots: the first round's from the bracket, a later round's the winners feeding it.
            for (int m = 0; m < b.MatchCount; m++)
            {
                var (pa, pb) = ShownPair(b, m);
                float x = m == b.Final ? L.Columns[b.Rounds - 1] : L.Columns[ColumnOf(b, m)];
                for (int k = 0; k < 2; k++)
                {
                    var undecided = b.RoundOf(m) == 0 ? "" : $"Winner of {b.RoundName(b.RoundOf(m) - 1).ToLowerInvariant()} {Feeder(b, m, k) - Start(b, b.RoundOf(m) - 1) + 1}";
                    Slot(b, L, cup, x, SlotY(b, L, m, k), k == 0 ? pa : pb, m, undecided, yours);
                }
            }
            float vsTop = L.FinalY[0] + L.H + (b.FinalBestOf > 1 ? 24 : 0);
            var vs = Ui.Text(Ui.Box(_view, L.Columns[b.Rounds - 1], vsTop, L.W, L.FinalY[1] - vsTop), "VS", 26, T.TextDim);
            vs.fontStyle = FontStyle.Bold;
            if (b.FinalBestOf > 1) Pips(b, L);
            Plaque(b, L, cup);
            Ladder(b, L, place);

            // What's happening, then the buttons.
            var line = Ui.Text(Ui.Box(_view, 60, 868, 1800, 56), _message.Length > 0 ? _message : Situation(b, cup, yours, round, place), 20, T.TextSoft);
            line.supportRichText = true;
            line.resizeTextForBestFit = true;
            line.resizeTextMinSize = 14;
            line.resizeTextMaxSize = 20;
            Buttons(b, cup, yours, round, place);
        }

        /// <summary>A best-of-three final's games under each of its slots: "● ○" (a filled pip a game won).</summary>
        private void Pips(Bracket b, Layout L)
        {
            var T = UiTheme.Current;
            string Row(int won) => string.Join(" ", Enumerable.Range(0, b.GamesToWin).Select(g => g < won ? $"<color={T.GoldHex}>●</color>" : $"<color={T.TextDimHex}>○</color>"));
            _pips = (Row(b.FinalGamesA), Row(b.FinalGamesB));
            for (int k = 0; k < 2; k++)
            {
                var pips = Ui.Text(Ui.Box(_view, L.Columns[b.Rounds - 1], L.FinalY[k] + L.H + 2, L.W, 22), k == 0 ? _pips.a : _pips.b, 18, T.TextDim);
                pips.supportRichText = true;
                pips.raycastTarget = false;
            }
        }

        /// <summary>
        /// A finished Invitational whose No Limits Packs can't be opened here (<see cref="Brackets.FinishBlocked"/>): why, after
        /// the finish line (Finish keeps trying; nothing's paid until it can); "" otherwise.
        /// </summary>
        private string HeldBack(Bracket b) =>
            b.Kind == Brackets.InvitationalKind && Brackets.FinishBlocked(b, _state.NoLimitsPacks()) is string why
                ? $" <color={UiTheme.Current.WarnHex}>{why} Nothing's paid until they can be, or Settle now: paid now, the packs owed until they can be opened.</color>" : "";

        /// <summary>What to do now, under the bracket.</summary>
        private string Situation(Bracket b, Circuit? cup, int yours, int round, int place)
        {
            var T = UiTheme.Current;
            bool alley = Brackets.IsAlley(b);
            bool bo3 = b.FinalBestOf > 1;
            if (_hidden.Count > 0) return "The other results come in...";
            if (b.Done)
            {
                int champ = b.Champion;
                int packs = PacksAt(b, Math.Max(0, place));
                int tokens = TitleTokens(b);
                var games = bo3 ? $" ({Games(b).yours}-{Games(b).theirs})" : "";
                return champ == b.YourSlot
                    ? $"<color={T.GoldHex}>You won the {b.Name}!</color>{games} Finish to collect +{Brackets.PlacementDp(b, 0):N0} DP and {Packs(PackName(b), packs)}"
                      + (tokens > 0 ? $" and {tokens} Card Token{(tokens > 1 ? "s" : "")}." : ".") + HeldBack(b)
                    : $"{NameOf(cup, b.Slots[champ])} won the {b.Name}. You finished: {Brackets.PlacementText(place)}."
                      + (packs > 0 ? $" Finish to open your {Packs(PackName(b), packs)}." : " Finish to close the bracket.")
                    + HeldBack(b);
            }
            if (yours >= 0)
            {
                var them = b.Slots[b.Opponent(yours)];
                // A Locals duel left mid-way replays; an alley one was settled as a loss when it was left (or at load).
                var interrupted = !alley && b.LiveMatch == yours ? $"<color={T.WarnHex}>Your duel was interrupted: Duel plays it again.</color> " : "";
                var which = yours == b.Final && bo3
                    ? $"Your final, game {b.FinalGamesA + b.FinalGamesB + 1} (best of {b.FinalBestOf}, {Games(b).yours}-{Games(b).theirs})"
                    : $"Your {b.RoundName(b.RoundOf(yours)).ToLowerInvariant()}";
                return interrupted + $"{which}: #{them.Seed} {NameOf(cup, them)} ({_state.DeckTitle(them.Deck)}). "
                       + (alley ? $"Your own decks only, {(b.Kind == Brackets.InvitationalKind ? "under the No Limits rules (no list, three of anything)" : "legal under the Alley list")}; turn 40, higher LP wins. Leaving a duel counts as a loss."
                                : "Your deck must be legal each round; a loaner halves placement DP.");
            }
            if (b.YouOut)
            {
                var paid = $"{Brackets.PlacementText(place)} · +{Brackets.PlacementDp(b, place):N0} DP paid.";
                if (round == b.Rounds - 1 && b.Pending(b.Final))
                    return bo3
                        ? $"{paid} The final is best of {b.FinalBestOf}, CPU against CPU ({b.FinalGamesA}-{b.FinalGamesB}): watch it a game at a time (each game counts), or skip to the end."
                        : $"{paid} The final is CPU against CPU: watch it (it counts), or skip to the end.";
                return $"{paid} Follow the bracket a round at a time, or skip to the end.";
            }
            return "";
        }

        private void Buttons(Bracket b, Circuit? cup, int yours, int round, int place)
        {
            var T = UiTheme.Current;
            Ui.BoxButton(_view, 60, 966, 180, 56, "Back", () => BackClicked?.Invoke(), 20).targetGraphic.color = T.Button;
            if (_hidden.Count > 0) return; // the results are still coming in
            bool bo3 = b.FinalBestOf > 1;
            if (yours >= 0)
            {
                var label = yours == b.Final && bo3 ? $"Duel: Final, game {b.FinalGamesA + b.FinalGamesB + 1}" : $"Duel: {b.RoundName(b.RoundOf(yours))}";
                var duel = Ui.BoxButton(_view, 760, 950, 400, 76, label, () => DuelClicked?.Invoke(), 28);
                duel.targetGraphic.color = T.Primary;
                var them = b.Slots[b.Opponent(yours)];
                var scout = new List<(string, string)> { (NameOf(cup, them), them.Deck) };
                Ui.BoxButton(_view, 1180, 960, 200, 56, "Scout", () => ScoutClicked?.Invoke(scout), 20).targetGraphic.color = T.Button;
            }
            else if (b.Done)
            {
                int packs = PacksAt(b, Math.Max(0, place));
                var label = packs > 0 ? $"Open {(packs == 1 ? "your " + PackName(b) : Packs(PackName(b), packs))}" : "Finish";
                Ui.BoxButton(_view, 760, 950, 400, 76, label, () => FinishClicked?.Invoke(), 26).targetGraphic.color = T.Primary;
                // A held-back Invitational (its No Limits Packs can't be opened here): settle it now, as a launch would, the
                // packs owed until they can be opened.
                if (HeldBack(b).Length > 0)
                    Ui.BoxButton(_view, 1180, 960, 360, 56, SettleLabel, () => SettleClicked?.Invoke(), 20).targetGraphic.color = T.Button;
            }
            else if (b.YouOut)
            {
                bool cpuFinal = round == b.Rounds - 1 && b.Pending(b.Final);
                var watch = bo3 ? $"Watch game {b.FinalGamesA + b.FinalGamesB + 1}" : "Watch the final";
                var other = cpuFinal
                    ? Ui.BoxButton(_view, 600, 954, 340, 68, watch, () => WatchClicked?.Invoke(), 24)
                    : Ui.BoxButton(_view, 600, 954, 340, 68, "Follow the bracket", Follow, 24);
                other.targetGraphic.color = T.Button;
                Ui.BoxButton(_view, 980, 954, 340, 68, "Skip to end", Skip, 24).targetGraphic.color = T.Primary;
            }
        }

        /// <summary>Follow the bracket: the round being played settled, its results shown one by one.</summary>
        private void Follow()
        {
            var b = _state.Profile?.Bracket;
            if (b == null || !b.YouOut || b.Done) return;
            var settled = Brackets.SimulateRound(b, b.CurrentRound(), _state.Matchups);
            _state.Save();
            Open("", settled);
        }

        /// <summary>Skip to end (once you're out): every match left settled, then shown one by one.</summary>
        private void Skip()
        {
            var b = _state.Profile?.Bracket;
            if (b == null || !b.YouOut || b.Done) return;
            var settled = Brackets.SkipToEnd(b, _state.Matchups);
            _state.Save();
            Open("", settled);
        }

        /// <summary>One slot: <paramref name="slot"/> (-1 while undecided) at <paramref name="match"/>.</summary>
        private void Slot(Bracket b, Layout L, Circuit? cup, float x, float y, int slot, int match, string undecided, int yours)
        {
            var T = UiTheme.Current;
            var box = Ui.Box(_view, x, y, L.W, L.H);
            _boxes.Add(($"slot {slot} at match {match}", new Rect(x, y, L.W, L.H)));
            if (slot < 0)
            {
                Ui.Image(box, T.Well);
                Ui.Text(Ui.Box(box, 12, 0, L.W - 24, L.H - 24), undecided, L.UndecidedSize, T.TextQuiet);
                Chip(box, L, "—", T.Inset, T.TextDim);
                return;
            }
            var e = b.Slots[slot];
            int winner = Shown(b, match);
            bool won = winner == slot, lost = winner >= 0 && !won;
            var (pa, pb) = ShownPair(b, match);
            bool live = match == yours && winner < 0 && (pa == slot || pb == slot) && pa >= 0 && pb >= 0;
            // A border: red at your match, gold for a win; the fill inside it.
            Ui.Image(box, live ? T.Primary : won ? T.Highlight : T.Inset);
            var fill = Ui.Box(box, 3, 3, L.W - 6, L.H - 6);
            Ui.Image(fill, e.You ? T.RowSelected : won && match == b.Final ? T.GoldWash : T.RowA);
            if (lost) box.gameObject.AddComponent<CanvasGroup>().alpha = 0.45f;

            var well = Ui.Box(box, 8, 8, L.Well, L.Well);
            Ui.Image(well, e.You ? T.Placeholder : T.Well);
            well.gameObject.AddComponent<RectMask2D>();
            // A splash regular is hooded in its tint (the alley's stand-in until there's art); everyone else shows a face.
            if (e.Splash is AlleyOpponent o)
            {
                // A splash regular shows its grunt's face (by handle), else the hood in its tint.
                if (!(Portraits.GruntFor(o.Handle) is string grunt && Portraits.PlaceHead(well, grunt)))
                    AlleyMaterials.Silhouette(well, o.Tint == 0 ? T.TextDim : new Color(((o.Tint >> 16) & 0xFF) / 255f, ((o.Tint >> 8) & 0xFF) / 255f, (o.Tint & 0xFF) / 255f));
            }
            else if (e.You || !Portraits.PlaceHead(well, e.Character))
            {
                var initial = Ui.Text(Ui.Box(well, 0, 0, L.Well, L.Well), e.You ? "YOU" : Initial(NameOf(cup, e)), e.You ? L.YouSize : L.InitialSize, T.TextMuted);
                initial.fontStyle = FontStyle.Bold;
            }
            var name = Ui.Text(Ui.Box(box, L.TextX, L.NameY, L.TextW, L.NameH), NameOf(cup, e), L.NameSize, e.You ? T.Gold : T.Text, TextAnchor.MiddleLeft);
            name.fontStyle = FontStyle.Bold;
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 10;
            name.resizeTextMaxSize = L.NameSize;
            if (b.Size == 8) name.resizeTextMinSize = 12;
            var deck = Ui.Text(Ui.Box(box, L.TextX, L.DeckY, L.TextW, L.DeckH), e.Deck.Length > 0 ? _state.DeckTitle(e.Deck) : "Your deck", L.DeckSize, T.TextFaint, TextAnchor.MiddleLeft);
            deck.resizeTextForBestFit = true;
            deck.resizeTextMinSize = Math.Min(10, L.DeckSize);
            deck.resizeTextMaxSize = L.DeckSize;
            // An out-of-towner (a No Limits pilot at the Alley Brawl, docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 8) is marked as one;
            // at the Invitational the pilots play their own peak builds, so they aren't (BackAlley.OutOfTowners' test).
            bool outOfTown = !e.You && e.Splash == null && _state.AlleyData.FindBuild(e.Character) is Headliner oot && e.Deck != oot.Deck;
            var eloLine = Ui.Text(Ui.Box(box, L.TextX, L.EloY, L.TextW, L.EloH), (e.Elo > 0 ? $"#{e.Seed} · Elo {e.Elo:N0}" : $"#{e.Seed} · unrated") + (outOfTown ? " · out of town" : ""),
                L.EloSize, outOfTown ? T.Sign : T.TextQuiet, TextAnchor.MiddleLeft);
            eloLine.resizeTextForBestFit = true;
            eloLine.resizeTextMinSize = 8;
            eloLine.resizeTextMaxSize = L.EloSize;
            if (outOfTown) _outOfTowners.Add(e.Character);
            if (live) Chip(box, L, "▶ YOUR MATCH", T.Primary, T.Text);
            else if (won) Chip(box, L, match == b.Final ? "♛ CHAMPION" : "♛ WON", T.Highlight, T.OnAccent);
            else if (lost) Chip(box, L, "OUT", T.Inset, T.TextDim);
            else Chip(box, L, "—", T.Inset, T.TextDim);
            // Click a CPU for their scouting report.
            if (!e.You)
            {
                var them = new List<(string, string)> { (NameOf(cup, e), e.Deck) };
                Ui.Clickable(fill, () => ScoutClicked?.Invoke(them), style: HoverStyle.Row);
            }
        }

        private static void Chip(RectTransform box, Layout L, string text, Color fill, Color ink)
        {
            var chip = Ui.Box(box, L.TextX, L.ChipY, L.TextW, L.ChipH);
            Ui.Image(chip, fill).raycastTarget = false;
            var t = Ui.Text(chip, text, L.ChipSize, ink);
            t.fontStyle = FontStyle.Bold;
        }

        /// <summary>
        /// The bracket's lines: each slot's elbow to the slot its winner moves to (two slots share the last stretch). Dim,
        /// then gold where a duelist won through.
        /// </summary>
        private void Connectors(Bracket b, Layout L)
        {
            var T = UiTheme.Current;
            var dim = T.Text.WithAlpha(0.18f);
            // (source slot shown there, the match it plays, its centre y, the source edge x, the elbow x, the target edge x, the target centre y)
            var links = new List<(int slot, int match, float y, float x0, float elbow, float x1, float target)>();
            for (int m = 0; m < b.MatchCount; m++)
            {
                if (m == b.Final) continue;
                int round = b.RoundOf(m), i = m - Start(b, round), col = ColumnOf(b, m);
                bool left = LeftHalf(b, m);
                // Where the winner goes: the next round's match, on its first or second side (the final: left half above).
                int next = Start(b, round + 1) + i / 2;
                int side = next == b.Final ? (left ? 0 : 1) : i % 2;
                float target = SlotY(b, L, next, side) + L.H / 2;
                float x0 = left ? L.Columns[col] + L.W : L.Columns[col];
                float x1 = left ? L.Columns[col + 1] : L.Columns[col - 1] + L.W;
                var (pa, pb) = ShownPair(b, m);
                for (int k = 0; k < 2; k++)
                    links.Add((k == 0 ? pa : pb, m, SlotY(b, L, m, k) + L.H / 2, x0, (x0 + x1) / 2, x1, target));
            }
            foreach (bool lit in new[] { false, true })
                foreach (var l in links)
                {
                    bool through = l.slot >= 0 && Shown(b, l.match) == l.slot;
                    if (through != lit) continue;
                    var color = lit ? T.Highlight : dim;
                    HLine(l.x0, l.elbow, l.y, color);
                    VLine(l.elbow, l.y, l.target, color);
                    if (lit || Shown(b, l.match) < 0) HLine(l.elbow, l.x1, l.target, color);
                }
        }

        private void HLine(float x1, float x2, float y, Color color) =>
            Ui.Image(Ui.Box(_view, Math.Min(x1, x2), y - Line / 2, Math.Abs(x2 - x1), Line), color).raycastTarget = false;

        private void VLine(float x, float y1, float y2, Color color) =>
            Ui.Image(Ui.Box(_view, x - Line / 2, Math.Min(y1, y2) - Line / 2, Line, Math.Abs(y2 - y1) + Line), color).raycastTarget = false;

        /// <summary>The champion's plaque under the final: who won, or what winning pays.</summary>
        private void Plaque(Bracket b, Layout L, Circuit? cup)
        {
            var T = UiTheme.Current;
            float x = L.Columns[b.Rounds - 1];
            var plaque = Ui.Box(_view, x, L.PlaqueY, L.W, L.H);
            _boxes.Add(("champion's plaque", new Rect(x, L.PlaqueY, L.W, L.H)));
            int champ = Shown(b, b.Final);
            Ui.Image(plaque, champ >= 0 ? T.Highlight : T.Inset);
            Ui.Image(Ui.Box(plaque, 2, 2, L.W - 4, L.H - 4), champ >= 0 ? T.GoldWash : T.Panel);
            float head = L.H * 8 / 96, line = L.H * 24 / 96;
            Ui.Text(Ui.Box(plaque, 0, head, L.W, line), "♛ CHAMPION", L.ChipSize + 3, champ >= 0 ? T.Gold : T.Caption).fontStyle = FontStyle.Bold;
            var who = champ >= 0
                ? (b.Slots[champ].You ? "You!" : NameOf(cup, b.Slots[champ]))
                : $"+{Brackets.PlacementDp(b, 0):N0} DP · {Packs(PackName(b), PacksAt(b, 0))}";
            var t = Ui.Text(Ui.Box(plaque, 8, head + line + 4, L.W - 16, L.H - head - line - 10), who, champ >= 0 ? L.NameSize + 6 : L.NameSize - 1, champ >= 0 ? T.Text : T.Gold);
            t.fontStyle = FontStyle.Bold;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 10;
            t.resizeTextMaxSize = champ >= 0 ? L.NameSize + 6 : L.NameSize - 1;
        }

        /// <summary>What each placement pays (halved once a loaner played), yours marked once you have one.</summary>
        private void Ladder(Bracket b, Layout L, int place)
        {
            var T = UiTheme.Current;
            var rows = new List<string>();
            var pay = Brackets.PayFor(b.Kind);
            for (int p = 0; p < pay.Length; p++)
            {
                int packs = PacksAt(b, p);
                var row = $"{Places[p]}: +{Brackets.PlacementDp(b, p):N0} DP" + (packs > 0 ? $" · {Packs(PackName(b), packs)}" : "")
                          + (p == 0 && TitleTokens(b) > 0 ? " · " + TokensText(TitleTokens(b)) : "");
                rows.Add(p == place ? $"<color={T.GoldHex}>▶ {row}</color>" : row);
            }
            if (b.Loaned) rows.Add($"<color={T.WarnHex}>A loaner played: placement DP halved</color>");
            float x = L.Columns[b.Rounds - 1];
            var ladder = Ui.Text(Ui.Box(_view, x - 30, L.PlaqueY + L.H + 14, L.W + 60, 140), string.Join("\n", rows), 15, T.TextMuted, TextAnchor.UpperCenter);
            ladder.supportRichText = true;
            ladder.resizeTextForBestFit = true;
            ladder.resizeTextMinSize = 11;
            ladder.resizeTextMaxSize = 15;
        }

        // ---- The entry dialog --------------------------------------------------------------------------------------------

        /// <summary>
        /// The entry dialog over a cup's panel: the 8 seeds with their Elo, the cup's list and pool, the rule about decks,
        /// the prize ladder, and Enter (1,000 DP, not refunded) / Cancel.
        /// </summary>
        public void ShowEntry(int cup)
        {
            var T = UiTheme.Current;
            var profile = _state.Profile;
            if (profile == null || cup < 0 || cup >= _state.Library.Cups.Count) return;
            CloseEntry();
            _entryCup = cup;
            _entryKind = "";
            _entryEnterable = true;
            var c = _state.Library.Cups[cup];
            var rules = _state.CupRules(cup);
            _root.gameObject.SetActive(true);
            _entry = Ui.Stretched("Entry", _root, Vector2.zero, Vector2.one);
            Ui.Image(_entry, T.Scrim.WithAlpha(0.75f)); // blocks the screen under it
            var panel = Ui.Box(_entry, 360, 90, 1200, 900);
            Ui.Image(panel, T.Panel);
            var title = Ui.Text(Ui.Box(panel, 30, 20, 1140, 48), Brackets.NameOf(c), 32, T.LibraryGold, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            Ui.Text(Ui.Box(panel, 30, 68, 1140, 28), $"{rules.List?.Name ?? c.Banlist} list · cards out by {CampaignScreen.PoolMonth(c.Pool)}", 17, T.TextMuted, TextAnchor.MiddleLeft);
            Ui.Text(Ui.Box(panel, 30, 96, 1140, 28), "8 players, single elimination. You're seed 1 as the cup's champion: seeds 1 and 2 can only meet in the final.",
                16, T.TextSoft, TextAnchor.MiddleLeft);

            // The field: you, then the seven CPUs by seed (Elo, highest first).
            Ui.Text(Ui.Box(panel, 30, 136, 660, 26), "THE FIELD", 15, T.Caption, TextAnchor.MiddleLeft);
            var field = Brackets.LocalsField(_state.Library, cup, _state.Ratings);
            double yourElo = _state.Ratings.TryGetValue(Loaners.RecipeOf(profile.Deck), out var elo) ? elo : 0;
            var rows = new List<(string id, int seed, string name, string deck, double elo, bool you)>
                { ("", 1, "You", _state.DeckTitle(profile.Deck), yourElo, true) };
            rows.AddRange(field.Select(e => (e.Character, e.Seed, Who(c, e.Character)?.Name ?? e.Character, _state.DeckTitle(e.Deck), e.Elo, false)));
            _entryRows = rows.Count;
            _entryNames.Clear();
            _entryNames.AddRange(rows.Select(r => r.name));
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var row = Ui.Box(panel, 30, 166 + i * 50, 660, 46);
                Ui.Image(row, r.you ? T.RowSelected : i % 2 == 0 ? T.RowA : T.RowB);
                var well = Ui.Box(row, 4, 3, 40, 40);
                Ui.Image(well, r.you ? T.Placeholder : T.Well);
                well.gameObject.AddComponent<RectMask2D>();
                if (r.you || !Portraits.PlaceHead(well, r.id)) Ui.Text(Ui.Box(well, 0, 0, 40, 40), Initial(r.name), 20, T.TextMuted).fontStyle = FontStyle.Bold;
                Ui.Text(Ui.Box(row, 52, 0, 44, 46), $"#{r.seed}", 18, T.Gold, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
                var name = Ui.Text(Ui.Box(row, 98, 0, 200, 46), r.name, 18, r.you ? T.Gold : T.Text, TextAnchor.MiddleLeft);
                name.fontStyle = FontStyle.Bold;
                name.resizeTextForBestFit = true;
                name.resizeTextMinSize = 12;
                name.resizeTextMaxSize = 18;
                var deck = Ui.Text(Ui.Box(row, 300, 0, 240, 46), r.deck, 15, T.TextFaint, TextAnchor.MiddleLeft);
                deck.resizeTextForBestFit = true;
                deck.resizeTextMinSize = 11;
                deck.resizeTextMaxSize = 15;
                Ui.Text(Ui.Box(row, 540, 0, 110, 46), r.elo > 0 ? $"Elo {r.elo:N0}" : "unrated", 15, T.TextQuiet, TextAnchor.MiddleRight);
            }
            Ui.Text(Ui.Box(panel, 30, 568, 660, 26), "Quarter-finals: 1 v 8 · 4 v 5 · 2 v 7 · 3 v 6", 15, T.TextQuiet, TextAnchor.MiddleLeft);

            // The prize ladder.
            Ui.Text(Ui.Box(panel, 730, 136, 440, 26), "PRIZES", 15, T.Caption, TextAnchor.MiddleLeft);
            for (int p = 0; p < Brackets.LocalsPay.Length; p++)
            {
                int packs = Brackets.LocalsPacks[p];
                var row = Ui.Box(panel, 730, 166 + p * 50, 440, 46);
                Ui.Image(row, p == 0 ? T.GoldWash : p % 2 == 0 ? T.RowA : T.RowB);
                Ui.Text(Ui.Box(row, 14, 0, 200, 46), p == 0 ? "♛ Champion" : Places[p],
                    17, p == 0 ? T.Gold : T.Text, TextAnchor.MiddleLeft);
                Ui.Text(Ui.Box(row, 200, 0, 226, 46), $"+{Brackets.LocalsPay[p]:N0} DP" + (packs > 0 ? $" · {packs} Prize Pack{(packs > 1 ? "s" : "")}" : ""),
                    17, T.Gold, TextAnchor.MiddleRight);
            }
            // What a Prize Pack holds, with its odds, before the entry is paid (SHOP-REWORK-SPEC section 5).
            // It has the column down to the rule line (shrinking only if it must), with the entry price right under it.
            var prize = Ui.Text(Ui.Box(panel, 730, 376, 440, 186), PrizeOddsText(cup), 14, T.TextSoft, TextAnchor.UpperLeft);
            prize.supportRichText = true;
            prize.lineSpacing = 1.05f;
            float prizeHeight = Mathf.Ceil(prize.preferredHeight);
            if (prizeHeight > 186)
            {
                prize.resizeTextForBestFit = true;
                prize.resizeTextMinSize = 12;
                prize.resizeTextMaxSize = 14;
                prizeHeight = 186;
            }
            Ui.Text(Ui.Box(panel, 730, 376 + prizeHeight + 10, 440, 32), $"Entry: <color={T.GoldHex}>{Brackets.LocalsEntry:N0} DP</color> (not refunded) · you have {profile.Dp:N0} DP",
                17, T.Text, TextAnchor.UpperLeft).supportRichText = true;

            // Your deck, every round.
            var problems = _library.Contains(profile.Deck) ? _state.Problems(_library.Load(profile.Deck), rules) : new List<DeckProblem>();
            // CPUs bring their decks as built (the owner's rule everywhere): say so, or a Forbidden card across the table reads as unfair.
            var rule = $"<color={T.WarnHex}>Your deck must be legal each round; loaners halve placement DP. The others bring their decks as built.</color>"
                       + (problems.Count > 0 ? $" <color={T.BadHex}>{_state.DeckTitle(profile.Deck)} isn't legal here now: {problems[0].Message}</color> Fix it, or borrow a loaner on each round's versus screen." : "");
            var ruleText = Ui.Text(Ui.Box(panel, 30, 612, 1140, 60), rule, 17, T.TextSoft, TextAnchor.UpperLeft);
            ruleText.supportRichText = true;
            _entryNote = Ui.Text(Ui.Box(panel, 30, 680, 1140, 40), "", 17, T.Bad, TextAnchor.MiddleLeft);
            _entryNote.supportRichText = true;

            var enter = Ui.BoxButton(panel, 330, 790, 320, 72, $"Enter · {Brackets.LocalsEntry:N0} DP", () => EnterClicked(cup), 24);
            enter.targetGraphic.color = T.Primary;
            Ui.BoxButton(panel, 680, 798, 200, 56, "Cancel", CloseEntry, 20).targetGraphic.color = T.Button;
        }

        /// <summary>
        /// "A Prize Pack:", then a line each: "3 cards from the field's decks (about):" and its mix ("Rare 58% · Super Rare 24% ·
        /// Ultra Rare 13% · Secret Rare 5%"), "1 card from Tournament Pack 8", "1 sealed slab: Grade 9 75%, Grade 10 25%",
        /// "Prize cards sell for nothing: ...".
        /// (<see cref="PrizePacks.Odds"/> over the cup's field, as a bracket entered now would seed it.)
        /// </summary>
        private string PrizeOddsText(int cup)
        {
            var c = _state.Library.Cups[cup];
            var parts = new List<string>();
            PrizeOdds? odds = null;
            try
            {
                if (_state.Prizes is PrizePacks prizes) odds = prizes.Odds(Brackets.Create(_state.Library, cup, _state.Ratings, 0u, _state.Profile?.Deck ?? ""), c.Pool);
            }
            catch (InvalidOperationException) { odds = null; } // a field that isn't 7 CPUs: no bracket, no odds
            if (odds == null)
                return "A Prize Pack: three cards from the field's decks (Rare or better), one from a tournament pack of the cup's time, "
                       + "and a sealed Grade 9 or 10 slab. Prize cards sell for nothing: they're for your binder and your decks.";
            parts.Add($"{PrizePacks.FieldSlots} cards from the field's decks (about):\n{Mix(odds.Field)}");
            if (odds.TournamentSet != null) parts.Add($"1 card from {odds.TournamentSet.Name}");
            parts.Add($"1 sealed slab: Grade 9 {100 - odds.SlabTenPercent}%, Grade 10 {odds.SlabTenPercent}%");
            return "<b>A Prize Pack:</b>\n" + string.Join("\n", parts) + "\nPrize cards sell for nothing: they're for your binder and your decks.";
        }

        /// <summary>A rarity mix as one line; each "Ultra Rare 21%" holds together (no-break spaces): a line only breaks between them.</summary>
        private static string Mix(List<(Rarity rarity, double share)> mix) =>
            string.Join(" · ", mix.Where(m => m.share >= 0.005).Select(m => $"{Rarities.Name(m.rarity)} {Math.Round(m.share * 100):0}%".Replace(' ', ' ')));

        private void EnterClicked(int cup)
        {
            var T = UiTheme.Current;
            var profile = _state.Profile;
            if (profile == null) return;
            var b = Brackets.EnterLocals(profile, _state.Library, cup, _state.Ratings, profile.Deck);
            if (b == null)
            {
                Sounds.Play("error");
                if (_entryNote != null) _entryNote.text = $"<color={T.BadHex}>{Brackets.EnterBlocked(profile, _state.Library, cup) ?? "You can't enter now."}</color>";
                return;
            }
            _state.Save();
            Sounds.Play("token");
            CloseEntry();
            Entered?.Invoke(cup);
        }

        // ---- The alley's entry dialog ----------------------------------------------------------------------------------

        /// <summary>
        /// The bracket an alley entry would draw now, from <see cref="Brackets.AlleySeed"/> (the seed <see cref="Brackets.EnterAlley"/>
        /// draws from), so the dialog's field is the one Enter seats. Null when the field can't be built (Enter is then off).
        /// </summary>
        private Bracket? AlleyPreview(Profile profile, string kind)
        {
            if (_state.AlleyContext is not AlleyContext ctx) return null;
            try { return Brackets.TryCreateAlley(_state.AlleyData, kind, profile, ctx, Brackets.AlleySeed(profile, kind), profile.Deck); }
            catch (Exception) { return null; } // TryCreateAlley catches the data's faults; this is the dialog's own guard
        }

        /// <summary>
        /// An alley bracket's entry dialog over Baz's chalkboard: the field by seed (8 or 16: splash regulars by handle and the
        /// table they're from, named entrants by name), the kind's rules (the Alley list; the Invitational's, No Limits': your own
        /// decks only, every round), the prize ladder in Contraband Packs (the Invitational's No Limits Packs), what one holds and
        /// its odds, a first title's Card Tokens, and Enter (the entry, not refunded) / Cancel.
        /// </summary>
        public void ShowAlleyEntry(string kind)
        {
            var T = UiTheme.Current;
            var profile = _state.Profile;
            var spec = _state.AlleyData.Brackets.FirstOrDefault(x => x.Kind == kind);
            if (profile == null || spec == null) return;
            CloseEntry();
            _entryKind = kind;
            _entryCup = -1;
            _entryEnterable = false;
            _root.gameObject.SetActive(true);
            _entry = Ui.Stretched("Entry", _root, Vector2.zero, Vector2.one);
            Ui.Image(_entry, T.Scrim.WithAlpha(0.75f)); // blocks the screen under it
            var panel = Ui.Box(_entry, 360, 90, 1200, 900);
            Ui.Image(panel, T.Panel);
            if (T.IsAlley) AlleyMaterials.Header(panel, 0, 0, 1200, 64, T.Sign);
            var title = Ui.Text(Ui.Box(panel, 30, 10, 1140, 48), spec.Name.ToUpperInvariant(), 32, T.Sign, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            T.Title(title);
            bool noLimits = kind == Brackets.InvitationalKind;
            string packName = Brackets.PackNameFor(kind);
            Ui.Text(Ui.Box(panel, 30, 68, 1140, 28), (noLimits ? "No list · three of anything" : "Alley list") + " · no Ban Passes · your own decks only · turn 40, higher LP wins · no stakes: the entry and placement only", 16, T.TextMuted, TextAnchor.MiddleLeft);
            var preview = AlleyPreview(profile, kind);
            int yourSeed = preview?.Slots.FirstOrDefault(s => s.You)?.Seed ?? 1;
            var about = spec.Size == 16
                ? $"{spec.Size} players, single elimination; the final is best of {Math.Max(1, spec.FinalBestOf)}. {(noLimits ? "The top seat" : "The reigning champion")} is seed 1 and you're seed {yourSeed}: you can only meet in the final."
                : $"{spec.Size} players, single elimination. You're seed {yourSeed}; the field is {(BackAlley.BrawlTable(profile, _state.AlleyData) is int bt && bt >= 0 ? _state.AlleyData.Tables[bt].Name + "'s" : "the alley's")} regulars, splashed as at the table.";
            Ui.Text(Ui.Box(panel, 30, 96, 1140, 28), about, 16, T.TextSoft, TextAnchor.MiddleLeft);

            // The field by seed: you, the CPUs (Elo; a splash regular's base plus its table's splash gain).
            Ui.Text(Ui.Box(panel, 30, 136, 660, 26), "THE FIELD", 15, T.Caption, TextAnchor.MiddleLeft);
            _entryNames.Clear();
            var seeded = preview?.Slots.OrderBy(s => s.Seed).ToList() ?? new List<BracketEntrant>();
            _entryRows = seeded.Count;
            bool tall = seeded.Count <= 8;
            float step = tall ? 50 : 26, rowH = tall ? 46 : 24, well = tall ? 40 : 22;
            int big = tall ? 18 : 14, small = tall ? 15 : 12;
            for (int i = 0; i < seeded.Count; i++)
            {
                var e = seeded[i];
                var name = NameOf(null, e);
                _entryNames.Add(name);
                var row = Ui.Box(panel, 30, 166 + i * step, 660, rowH);
                Ui.Image(row, e.You ? T.RowSelected : i % 2 == 0 ? T.RowA : T.RowB);
                var face = Ui.Box(row, 4, (rowH - well) / 2, well, well);
                Ui.Image(face, e.You ? T.Placeholder : T.Well);
                face.gameObject.AddComponent<RectMask2D>();
                if (e.Splash is AlleyOpponent o)
                {
                    if (!(Portraits.GruntFor(o.Handle) is string grunt && Portraits.PlaceHead(face, grunt)))
                        AlleyMaterials.Silhouette(face, o.Tint == 0 ? T.TextDim : new Color(((o.Tint >> 16) & 0xFF) / 255f, ((o.Tint >> 8) & 0xFF) / 255f, (o.Tint & 0xFF) / 255f));
                }
                else if (e.You || !Portraits.PlaceHead(face, e.Character)) Ui.Text(Ui.Box(face, 0, 0, well, well), Initial(name), (int)(well / 2), T.TextMuted).fontStyle = FontStyle.Bold;
                float x = well + 12;
                Ui.Text(Ui.Box(row, x, 0, 44, rowH), $"#{e.Seed}", big, T.Gold, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
                var who = Ui.Text(Ui.Box(row, x + 46, 0, 200, rowH), name, big, e.You ? T.Gold : T.Text, TextAnchor.MiddleLeft);
                who.fontStyle = FontStyle.Bold;
                who.resizeTextForBestFit = true;
                who.resizeTextMinSize = 10;
                who.resizeTextMaxSize = big;
                // An out-of-towner's deck is Alley-legal for the night (one of everything; the rest stays in the toploader).
                // (At the Invitational the builds play their own No Limits decks: no "out of town" there.)
                bool outOfTown = !e.You && e.Splash == null && _state.AlleyData.FindBuild(e.Character) is Headliner oot && e.Deck != oot.Deck;
                var deckTitle = e.You ? _state.DeckTitle(profile.Deck) : e.Splash != null ? $"{_state.DeckTitle(e.Splash.Base)}, splashed"
                    : outOfTown ? $"{_state.DeckTitle(e.Deck)} · out of town" : _state.DeckTitle(e.Deck);
                var deck = Ui.Text(Ui.Box(row, x + 250, 0, 300 - x, rowH), deckTitle, small, T.TextFaint, TextAnchor.MiddleLeft);
                deck.resizeTextForBestFit = true;
                deck.resizeTextMinSize = 9;
                deck.resizeTextMaxSize = small;
                Ui.Text(Ui.Box(row, 550, 0, 100, rowH), e.Elo > 0 ? $"Elo {e.Elo:N0}" : "unrated", small, T.TextQuiet, TextAnchor.MiddleRight);
            }
            if (preview == null)
                Ui.Text(Ui.Box(panel, 30, 166, 660, 46), "The field can't be drawn here (an alley table's decks are missing).", 15, T.Bad, TextAnchor.MiddleLeft);
            // The first round's pairings by seed (slots 2m and 2m+1 in bracket order).
            var order = Brackets.OrderFor(spec.Size);
            var firstRound = string.Join(" · ", Enumerable.Range(0, spec.Size / 2).Select(m => $"{order[2 * m]} v {order[2 * m + 1]}"));
            var pairing = Ui.Text(Ui.Box(panel, 30, tall ? 568 : 586, 660, 24), $"{(spec.Size == 16 ? "Round of 16" : "Quarter-finals")}: {firstRound}", tall ? 15 : 13, T.TextQuiet, TextAnchor.MiddleLeft);
            pairing.resizeTextForBestFit = true;
            pairing.resizeTextMinSize = 10;
            pairing.resizeTextMaxSize = tall ? 15 : 13;

            // The prize ladder: DP and Contraband Packs (the Invitational's No Limits Packs); a first title's Card Tokens (the Open's, the Invitational's).
            Ui.Text(Ui.Box(panel, 730, 136, 440, 26), "PRIZES", 15, T.Caption, TextAnchor.MiddleLeft);
            var pay = Brackets.PayFor(kind);
            var packsFor = Brackets.PacksFor(kind);
            float prizeStep = pay.Length > 4 ? 40 : 50, prizeH = prizeStep - 4;
            int firstTokens = FirstTitleTokens(profile, kind);
            for (int p = 0; p < pay.Length; p++)
            {
                int packs = p < packsFor.Length ? packsFor[p] : 0;
                var row = Ui.Box(panel, 730, 166 + p * prizeStep, 440, prizeH);
                Ui.Image(row, p == 0 ? T.GoldWash : p % 2 == 0 ? T.RowA : T.RowB);
                Ui.Text(Ui.Box(row, 14, 0, 170, prizeH), p == 0 ? "♛ Champion" : Places[p], 17, p == 0 ? T.Gold : T.Text, TextAnchor.MiddleLeft);
                var paid = Ui.Text(Ui.Box(row, 170, 0, 256, prizeH), $"+{pay[p]:N0} DP" + (packs > 0 ? $" · {Packs(packName, packs)}" : ""), 17, T.Gold, TextAnchor.MiddleRight);
                paid.resizeTextForBestFit = true;
                paid.resizeTextMinSize = 11;
                paid.resizeTextMaxSize = 17;
            }
            float y = 166 + pay.Length * prizeStep + 4;
            if (firstTokens > 0)
            {
                Ui.Text(Ui.Box(panel, 730, y, 440, 22), $"Your first title here: +{firstTokens} Card Token{(firstTokens > 1 ? "s" : "")}", 14, T.GoldSoft, TextAnchor.MiddleLeft);
                y += 24;
            }
            // What a Contraband Pack holds, with its odds, before the entry is paid (as the Locals show a Prize Pack's).
            float room = 590 - y;
            var odds = Ui.Text(Ui.Box(panel, 730, y + 4, 440, room), noLimits ? NoLimitsOddsText() : ContrabandOddsText(), 14, T.TextSoft, TextAnchor.UpperLeft);
            odds.supportRichText = true;
            odds.lineSpacing = 1.05f;
            float oddsHeight = Mathf.Ceil(odds.preferredHeight);
            if (oddsHeight > room)
            {
                odds.resizeTextForBestFit = true;
                odds.resizeTextMinSize = 11;
                odds.resizeTextMaxSize = 14;
                oddsHeight = room;
            }
            int entry = Brackets.EntryFor(kind);
            Ui.Text(Ui.Box(panel, 730, y + 4 + oddsHeight + 8, 440, 32), $"Entry: <color={T.GoldHex}>{entry:N0} DP</color> (not refunded) · you have {profile.Dp:N0} DP",
                17, T.Text, TextAnchor.UpperLeft).supportRichText = true;

            // Your deck: the kind's rules bind it every round (the Alley list; the Invitational's, No Limits'), and it has to be one of your own.
            var problems = !_state.AlleyDeckChoices().Contains(profile.Deck)
                ? new List<DeckProblem> { new DeckProblem { Message = "It isn't one of your own decks: the alley doesn't lend." } }
                : _state.AlleyProblems(_library.Load(profile.Deck), _state.AlleyBracketRules(kind));
            var rule = $"<color={T.WarnHex}>Your deck must be legal under {(noLimits ? "the No Limits rules (no list, three of anything)" : "the Alley list")} each round, and your own: no loaners. The others bring their decks as built. Leaving a duel counts as a loss.</color>"
                       + (problems.Count > 0 ? $" <color={T.BadHex}>{_state.DeckTitle(profile.Deck)} isn't legal here now: {problems[0].Message}</color> Fix it, or pick another of yours on each round's versus screen." : "");
            var ruleText = Ui.Text(Ui.Box(panel, 30, 640, 1140, 60), rule, 17, T.TextSoft, TextAnchor.UpperLeft);
            ruleText.supportRichText = true;
            _entryNote = Ui.Text(Ui.Box(panel, 30, 706, 1140, 40), "", 17, T.Bad, TextAnchor.MiddleLeft);
            _entryNote.supportRichText = true;

            var enter = Ui.BoxButton(panel, 330, 790, 320, 72, $"Enter · {entry:N0} DP", () => EnterAlleyClicked(kind), 24);
            enter.targetGraphic.color = T.Primary;
            // No field drawn, nothing to enter (EnterAlley would refuse it anyway, and charge nothing).
            enter.interactable = preview != null;
            _entryEnterable = preview != null;
            Ui.BoxButton(panel, 680, 798, 200, 56, "Cancel", CloseEntry, 20).targetGraphic.color = T.Button;
        }

        /// <summary>
        /// "A Contraband Pack:", then: "4 Raw cards, each Forbidden somewhere (of N, about):" and its mix, "1 sealed slab, Super
        /// Rare and up:" and its mix, its grades, "Prize cards sell for nothing" (<see cref="ContrabandPacks.Odds"/>).
        /// </summary>
        private string ContrabandOddsText()
        {
            if (_contrabandOdds == null && _state.Contraband is ContrabandPacks packs) _contrabandOdds = packs.Odds();
            var odds = _contrabandOdds;
            if (odds == null)
                return "A Contraband Pack: four Raw cards, every one Forbidden somewhere, and a sealed Grade 9 or 10 slab. "
                       + "Prize cards sell for nothing: they're for your binder and your decks.";
            return "<b>A Contraband Pack</b> (sealed by Moth):\n"
                   + $"{ContrabandPacks.Size - 1} Raw cards, each Forbidden somewhere ({odds.FieldCards:N0} cards, about):\n{TopMix(odds.Field)}\n"
                   + $"1 sealed slab, {Rarities.Name(AlleyEconomy.PackSlabMinRarity)} and up:\n{TopMix(odds.Slab)}\n"
                   + $"Grade 9 {100 - odds.SlabTenPercent}%, Grade 10 {odds.SlabTenPercent}%\n"
                   + "Prize cards sell for nothing: they're for your binder and your decks.";
        }

        /// <summary>
        /// "A No Limits Pack:" and its mix, as the Contraband Pack's (<see cref="ContrabandOddsText"/>) over the No Limits Pack's
        /// pool: the ready builds' ever-Forbidden cards (<see cref="CampaignState.NoLimitsPacks"/>).
        /// </summary>
        private string NoLimitsOddsText()
        {
            var packs = _state.NoLimitsPacks();
            if (packs != null && packs.Pool.Count != _noLimitsOddsPool)
            {
                _noLimitsOddsPool = packs.Pool.Count;
                _noLimitsOdds = packs.Pool.Count > 0 ? packs.Odds() : null;
            }
            var odds = packs != null && packs.Pool.Count > 0 ? _noLimitsOdds : null;
            if (odds == null)
                return "A No Limits Pack: four Raw cards the peak builds run, every one Forbidden somewhere, and a sealed Grade 9 or 10 slab. "
                       + "Prize cards sell for nothing: they're for your binder and your decks.";
            return "<b>A No Limits Pack</b> (sealed by Moth):\n"
                   + $"{ContrabandPacks.Size - 1} Raw cards the peak builds run, each Forbidden somewhere ({odds.FieldCards:N0} cards, about):\n{TopMix(odds.Field)}\n"
                   + $"1 sealed slab, {Rarities.Name(AlleyEconomy.PackSlabMinRarity)} and up:\n{TopMix(odds.Slab)}\n"
                   + $"Grade 9 {100 - odds.SlabTenPercent}%, Grade 10 {odds.SlabTenPercent}%\n"
                   + "Prize cards sell for nothing: they're for your binder and your decks.";
        }

        /// <summary>A long rarity mix (the ever-Forbidden pool's dozen foils) as its four likeliest and "other foils" for the rest.</summary>
        private static string TopMix(List<(Rarity rarity, double share)> mix)
        {
            var top = mix.OrderByDescending(m => m.share).Take(4).ToList();
            double rest = mix.Sum(m => m.share) - top.Sum(m => m.share);
            var shown = Mix(top.OrderBy(m => m.rarity).ToList());
            return rest >= 0.005 ? shown + $" · other\u00A0foils\u00A0{Math.Round(rest * 100):0}%" : shown;
        }

        private void EnterAlleyClicked(string kind)
        {
            var T = UiTheme.Current;
            var profile = _state.Profile;
            if (profile == null || _state.AlleyContext is not AlleyContext ctx) return;
            var b = Brackets.EnterAlley(profile, _state.AlleyData, kind, ctx, profile.Deck);
            if (b == null)
            {
                Sounds.Play("error");
                if (_entryNote != null) _entryNote.text = $"<color={T.BadHex}>{Brackets.EnterBlocked(profile, _state.AlleyData, kind) ?? "You can't enter now."}</color>";
                return;
            }
            _state.Save();
            Sounds.Play("token");
            CloseEntry();
            AlleyEntered?.Invoke(kind);
        }

        private void CloseEntry()
        {
            if (_entry != null) Destroy(_entry.gameObject);
            _entry = null;
            _entryNote = null;
            if (_view != null && !_view.gameObject.activeSelf && _root != null) _root.gameObject.SetActive(false);
        }

        // ---- Controller ---------------------------------------------------------------------------------------------------

        /// <summary>B: closes the entry dialog, else Back (the bracket is kept).</summary>
        public void ControllerBack()
        {
            if (_entry != null) CloseEntry();
            else if (IsOpen) BackClicked?.Invoke();
        }

        /// <summary>Y: Skip to end, once you're out.</summary>
        public void ControllerSkip()
        {
            if (IsOpen && _entry == null && _hidden.Count == 0) Skip();
        }

        // ---- Automated checks ---------------------------------------------------------------------------------------------

        public int DebugEntryRows => _entry != null ? _entryRows : 0;
        /// <summary>The entry dialog's Enter can be pressed (an alley dialog whose field couldn't be drawn has it off).</summary>
        public bool DebugEntryEnterable => _entry != null && _entryEnterable;
        /// <summary>The entry dialog's field by seed, as named there (empty when it's closed).</summary>
        public IReadOnlyList<string> DebugEntryNames => _entry != null ? _entryNames : new List<string>();
        /// <summary>Every text on the entry dialog (empty when it's closed).</summary>
        public string DebugEntryText => _entry == null ? "" : string.Join("\n", _entry.GetComponentsInChildren<Text>().Select(t => t.text));
        public void DebugEnter()
        {
            if (_entry == null) return;
            if (_entryKind.Length > 0) EnterAlleyClicked(_entryKind);
            else EnterClicked(_entryCup);
        }
        public void DebugDuel() => DuelClicked?.Invoke();
        public void DebugSkip() => Skip();
        public void DebugFinish() => FinishClicked?.Invoke();

        /// <summary>For checks: presses "Settle now" when it's shown; false when it isn't.</summary>
        public bool DebugSettle()
        {
            var settle = _view.GetComponentsInChildren<Button>().FirstOrDefault(x => x.GetComponentInChildren<Text>()?.text == SettleLabel);
            if (settle == null) return false;
            settle.onClick.Invoke();
            return true;
        }
        public void DebugWatch() => WatchClicked?.Invoke();
        /// <summary>A best-of-three final's pips as drawn (side A, side B; rich text), empty for a final of one game.</summary>
        public (string a, string b) DebugPips => _pips;
        /// <summary>Every text on the bracket screen.</summary>
        public string DebugText => string.Join("\n", _view.GetComponentsInChildren<Text>().Select(t => t.text));

        /// <summary>The slots (and the plaque) laid out, and every pair that overlaps.</summary>
        public (int boxes, List<string> overlaps) DebugOverlaps()
        {
            var overlaps = new List<string>();
            for (int i = 0; i < _boxes.Count; i++)
                for (int j = i + 1; j < _boxes.Count; j++)
                    if (_boxes[i].rect.Overlaps(_boxes[j].rect)) overlaps.Add($"{_boxes[i].what} / {_boxes[j].what}");
            return (_boxes.Count, overlaps);
        }
    }
}
