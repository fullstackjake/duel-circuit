using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The campaign: pick a starter deck (first time), then the circuit map, a scrolling climb through the difficulty
    /// bands. Only the first circuit is open at the start; clearing every match in one opens the next. The right side shows the chosen circuit's matches, and the
    /// campaign deck you take into them (built from cards you own). The card shop lives here too.
    /// </summary>
    public sealed partial class CampaignScreen : MonoBehaviour
    {
        private CampaignState _state = null!;
        private DeckLibrary _library = null!;
        private ICardDataProvider _cards = null!;
        private RectTransform _root = null!, _main = null!, _starters = null!, _circuitList = null!, _detail = null!;
        private ScrollRect _scroll = null!;
        private Text _dp = null!, _status = null!;
        private int _selected;
        private List<string> _decks = new List<string>();

        /// <summary>Play a circuit match: (circuit, match). The screen has closed itself.</summary>
        public event Action<int, int>? Duel;
        public event Action? ShopClicked, MenuClicked, RecipesClicked, LessonsClicked, GuestsClicked;
        private Text _lessonsLabel = null!;
        private Text _recipesLabel = null!;
        private GameObject _recipesButton = null!;
        /// <summary>Edit a campaign deck (null: a new one) in collection mode.</summary>
        public event Action<string?>? EditDeck;
        /// <summary>Edit deck under a partner's deck: your edit of it (saved first), and the circuit whose list it answers to.</summary>
        public event Action<string, int>? EditPartnerDeck;
        /// <summary>Scout: the scouting report on a match's decks (who plays each, and the deck), to prepare a deck for it.</summary>
        public event Action<List<(string who, string deck)>>? ScoutClicked;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        public void Init(CampaignState state, DeckLibrary library, ICardDataProvider cards)
        {
            _state = state;
            _library = library;
            _cards = cards;
            Build();
            _root.gameObject.SetActive(false);
        }

        /// <summary>-themecheck only: draws the screen in this theme whatever tab is shown (null: the tab's theme).</summary>
        public UiTheme? ThemeOverride;

        /// <summary>Raised as the screen opens, before anything on it (DuelController drops a results screen still waiting).</summary>
        public event Action? Opened;

        public void Open(string? message = null)
        {
            Opened?.Invoke();
            // The Back Alley shown: its theme, the whole screen rebuilt in it (UI-THEME rule 9).
            if (_state.Profile is not Profile p0 || !p0.AlleyOpen || !_state.AlleyReady) _alleyView = false;
            // The wall (No Limits): checked each time the Back Alley tab is shown, this open included; the one time it opens, its
            // scene plays with the tab's (AlleyOnOpen) and the map draws the table open.
            // Asked on every open, so a closing scene seen on main street still counts as that night's.
            if (_state.OpenWall(show: _alleyView)) { _wallJustOpened = true; _state.Save(); }
            // Main street everywhere else (the starter picker after "Start over" from the alley tab too).
            UiTheme.Current = ThemeOverride ?? (_alleyView ? UiTheme.BackAlley : UiTheme.MainStreet);
            if (_builtTheme != UiTheme.Current) Rebuild();
            var T = UiTheme.Current;
            _root.gameObject.SetActive(true);
            _library.Refresh();
            var profile = _state.Profile;
            // Moth sells what's past its time on the rail (any duel counts): the notice shows below (AlleyOnOpen).
            if (profile != null) _state.TickAlleyRail();
            _starters.gameObject.SetActive(profile == null);
            _newGameButton.SetActive(profile != null);
            _recipesButton.SetActive(profile != null);
            _lockerButton.SetActive(profile != null);
            // Gus's board is up once he's introduced it (the Beginner Circuit's clear and its scene, or New Game+).
            _bountiesButton.SetActive(profile != null && _state.Unlocks.Bounties.All.Count > 0 && _state.Unlocks.BountiesOpen(profile));
            if (profile != null)
            {
                int posters = Posters.New(_state.Circuits, profile).Count;
                _lockerLabel.text = posters > 0 ? $"Locker <color={T.GoodHex}>({posters})</color>" : "Locker";
                int fresh = RecipeBook.New(_state.Circuits, profile);
                _recipesLabel.text = fresh > 0 ? $"Recipe Book <color={T.GoodHex}>({fresh})</color>" : "Recipe Book";
                // Lessons not passed yet, counted like the Recipe Book's new recipes (the persona walkthrough: players missed them).
                int lessonsLeft = ChainLessons.All.Count(l => !profile.Seen.Contains(l.SeenKey));
                _lessonsLabel.text = lessonsLeft > 0 ? $"Lessons <color={T.GoodHex}>({lessonsLeft})</color>" : "Lessons";
                BountiesLabel(profile);
            }
            _main.gameObject.SetActive(profile != null);
            PackNav();
            if (profile == null)
            {
                _wallet.Clear();
                _dp.text = "";
                _status.text = "Pick the deck you start with. Its cards become your collection; open packs in the Card Shop to grow it.";
                return;
            }
            _decks = _library.CampaignNames.Where(d => _state.PartnerEditOf(d).Length == 0).ToList(); // a partner's edit is theirs to bring
            if (!_decks.Contains(profile.Deck) && _decks.Count > 0) profile.Deck = _decks[0];
            _selected = _state.Circuits.Current(profile);
            // A cup picked before New Game+ may be locked again: the panel follows the map's lock.
            if (_selectedCup >= 0 && (_selectedCup >= _state.Circuits.TagCups.Count || !_state.Circuits.IsTagCupOpen(_selectedCup, profile))) _selectedCup = -1;
            // The Library stays where you left it (a duel, a story, the shop), and is gone after Start over.
            if (!profile.LibraryOpen || _state.Library.Cups.Count == 0) _libraryView = false;
            // (Cups.Count is the "Every Era" final's plaque.)
            if (_selectedLibrary > _state.Library.Cups.Count && !(_selectedLibrary == UnlistedShelf && profile.UnlistedPages.Count > 0)
                && !(_selectedLibrary == LadderShelf && LadderOnShelf(profile))) _selectedLibrary = -1;
            if (_libraryView && _selectedLibrary < 0) _selectedLibrary = FirstCupToPlay();
            _districts.SetActive(profile.LibraryOpen && _state.Library.Cups.Count > 0 || AlleyShown(profile));
            _status.text = message ?? (_alleyView ? AlleyStatus : _libraryView ? LibraryHelp : CircuitsHelp);
            // Once, after loading a save from when Ban Pass slots were sold.
            if (message == null && profile.PassRefund > 0)
            {
                _status.text = $"<color={T.GoodHex}>Ban Passes are earned now, not bought:</color> your bought slots came back as {profile.PassRefund:N0} DP. "
                               + $"You earn up to {BanPasses.Max} by clearing bands.";
                profile.PassRefund = 0;
            }
            else if (message == null && !_alleyView && Posters.New(_state.Circuits, profile).FirstOrDefault() is Poster pinned)
                _status.text = $"<color={T.GoodHex}>New poster:</color> <b>{pinned.Title}</b> is in your Locker. Hang it on your plate for your next duel.";
            // Card unlocks held for you: an old save's catch-up (once), and gifts or bounties that can be paid now.
            var unlockNews = string.Join("  ·  ", new[] { _state.CatchUpNote, _state.PayOwed() }.Where(n => n.Length > 0));
            _state.CatchUpNote = "";
            if (unlockNews.Length > 0)
            {
                _status.text = $"<color={T.GoldSoftHex}>{unlockNews}</color>" + (message != null ? "  ·  " + message : "");
                BountiesLabel(profile);
                int newPosters = Posters.New(_state.Circuits, profile).Count;
                _lockerLabel.text = newPosters > 0 ? $"Locker <color={T.GoodHex}>({newPosters})</color>" : "Locker";
            }
            Refresh();
            ScrollToSelected();
            AlleyOnOpen(message);
        }

        private const string CircuitsHelp = "Clear every match in a circuit to open the next. Wins pay DP; spend it on packs in the Card Shop.";

        /// <summary>Shows the current circuit in the list (the Library's shelf from the top).</summary>
        private void ScrollToSelected()
        {
            Canvas.ForceUpdateCanvases();
            // The Unlisted binder sits at the shelf's end, under the cups.
            if (UnlistedShown) { _scroll.verticalNormalizedPosition = 0; return; }
            if (_libraryView || _alleyView) { _scroll.verticalNormalizedPosition = 1; return; }
            var max = Mathf.Max(1, _circuitList.rect.height - _scroll.viewport.rect.height);
            var top = _selected < _rowTops.Count ? _rowTops[_selected] : 0;
            _scroll.verticalNormalizedPosition = 1 - Mathf.Clamp01((top - _scroll.viewport.rect.height / 2) / max);
        }

        public void Close() => _root.gameObject.SetActive(false);

        /// <summary>The header's buttons left of Menu, in order; hidden ones (before a campaign starts) leave no gap.</summary>
        private RectTransform[] _nav = Array.Empty<RectTransform>();

        /// <summary>Packs the visible header buttons toward Menu, right to left.</summary>
        private void PackNav()
        {
            float right = 1720 - 14;
            for (int i = _nav.Length - 1; i >= 0; i--)
            {
                var rt = _nav[i];
                if (!rt.gameObject.activeSelf) continue;
                rt.anchoredPosition = new Vector2(right - rt.sizeDelta.x, rt.anchoredPosition.y);
                right -= rt.sizeDelta.x + 14;
            }
            // The wallet line ends where the buttons begin (it shrinks to fit rather than running under them).
            var dp = _dp.rectTransform;
            dp.sizeDelta = new Vector2(right - dp.anchoredPosition.x, dp.sizeDelta.y);
            FitWallet();
        }

        /// <summary>
        /// For automated checks: the wallet with long values (level 99, NG+12, 9,999,999 DP, a held stake) at its narrowest,
        /// every header button shown. Its base font size, its widest line as drawn (the text's own measure, &lt;size&gt; runs
        /// included) and the room it has: its box, which ends 14 px before the first header button. Open() puts the real one back.
        /// </summary>
        public (int size, float widest, float room) DebugLongWallet()
        {
            var T = UiTheme.Current;
            var dp = _dp.rectTransform;
            dp.sizeDelta = new Vector2(1720 - 14 - _nav.Sum(n => n.sizeDelta.x + 14) - dp.anchoredPosition.x, dp.sizeDelta.y);
            _wallet = new List<(string, int, string?)[]>
            {
                new (string, int, string?)[] { ("9,999,999 DP", 24, null), (" (12,500 held)", 18, T.TextDimHex), ("   9,999 tokens", 20, T.GoldSoftHex) },
                new (string, int, string?)[] { ("Level 99 · NG+12", 20, T.GoodHex), ("   44/44 circuits cleared", 15, T.TextDimHex) },
                new (string, int, string?)[] { ("99,999 cards (12,345 different) · 9,999 packs opened", 15, T.TextDimHex) },
            };
            FitWallet();
            Canvas.ForceUpdateCanvases();
            return (_dp.fontSize, _dp.preferredWidth, dp.sizeDelta.x);
        }

        /// <summary>The wallet block's lines, each a run of (text, size, color or null for the block's gold).</summary>
        private List<(string text, int size, string? color)[]> _wallet = new List<(string, int, string?)[]>();

        /// <summary>
        /// Writes the wallet block at the largest scale (up to its design sizes) at which every line fits its width and the
        /// header's height: Unity's best fit can't shrink the &lt;size&gt; runs, so long values or a full row of buttons
        /// used to wrap it ("4 / tokens" at 1280x720). Lines never wrap; they shrink.
        /// </summary>
        private void FitWallet()
        {
            if (_wallet.Count == 0) return;
            var settings = _dp.GetGenerationSettings(Vector2.zero);
            settings.richText = false;
            settings.resizeTextForBestFit = false;
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            var generator = new TextGenerator();
            float Width((string text, int size, string? color) run)
            {
                settings.fontSize = run.size;
                return generator.GetPreferredWidth(run.text, settings) / _dp.pixelsPerUnit;
            }
            float widest = _wallet.Max(line => line.Sum(Width));
            float tall = _wallet.Sum(line => line.Max(r => r.size)) * 1.2f;
            float room = _dp.rectTransform.sizeDelta.x * 0.97f;
            float scale = Mathf.Min(1f, room / Mathf.Max(1f, widest), (_dp.rectTransform.sizeDelta.y - 4) / tall);
            int Size(int size) => Mathf.Max(8, Mathf.FloorToInt(size * scale));
            int baseSize = Size(24);
            _dp.fontSize = baseSize;
            _dp.text = string.Join("\n", _wallet.Select(line => string.Concat(line.Where(r => r.text.Length > 0).Select(r =>
                r.color == null && Size(r.size) == baseSize ? r.text
                : r.color == null ? $"<size={Size(r.size)}>{r.text}</size>"
                : $"<color={r.color}><size={Size(r.size)}>{r.text}</size></color>"))));
        }

        // ---- Layout ----------------------------------------------------------------------------------

        private void Build()
        {
            var T = UiTheme.Current;
            _builtTheme = T;
            _root = Ui.Overlay(transform, "Campaign", 12);
            Ui.Image(_root, T.Base);
            Ui.Image(Ui.Box(_root, 0, 0, 1920, 90), T.Deep);
            Ui.Text(Ui.Box(_root, 40, 0, 400, 90), "CAMPAIGN", 34, T.Sign, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            _dp = Ui.Text(Ui.Box(_root, 280, 0, 484, 90), "", 24, T.Gold, TextAnchor.MiddleLeft);
            _dp.supportRichText = true;
            // FitWallet sizes it: it never wraps (a wrapped line broke as "Level 43 · NG+1  4 / tokens").
            _dp.horizontalOverflow = HorizontalWrapMode.Overflow;
            _dp.verticalOverflow = VerticalWrapMode.Overflow;
            var guests = Ui.BoxButton(_root, 772, 22, 112, 46, "Guests", () => { if (_state.Profile != null) GuestsClicked?.Invoke(); }, 18);
            var lessons = Ui.BoxButton(_root, 916, 22, 142, 46, "Lessons", () => { if (_state.Profile != null) LessonsClicked?.Invoke(); }, 18);
            _lessonsLabel = lessons.GetComponentInChildren<Text>();
            _lessonsLabel.supportRichText = true;
            // Gus's bounty board (the header packs the buttons, so its place is only its order).
            var bounties = Ui.BoxButton(_root, 916, 22, 176, 46, "Bounties", ShowBounties, 18);
            _bountiesLabel = bounties.GetComponentInChildren<Text>();
            _bountiesLabel.supportRichText = true;
            _bountiesButton = bounties.gameObject;
            var recipes = Ui.BoxButton(_root, 1090, 22, 178, 46, "Recipe Book", () => { if (_state.Profile != null) RecipesClicked?.Invoke(); }, 18);
            _recipesLabel = recipes.GetComponentInChildren<Text>();
            _recipesLabel.supportRichText = true;
            _recipesButton = recipes.gameObject;
            var locker = Ui.BoxButton(_root, 1150, 22, 120, 46, "Locker", ShowLocker, 18);
            _lockerLabel = locker.GetComponentInChildren<Text>();
            _lockerLabel.supportRichText = true;
            _lockerButton = locker.gameObject;
            var shop = Ui.BoxButton(_root, 1290, 22, 150, 46, "Card Shop", () => { if (_state.Profile != null) ShopClicked?.Invoke(); }, 18);
            // New game resets the campaign: after the navigation, and tinted so it doesn't read as one more screen.
            var newGame = Ui.BoxButton(_root, 1506, 22, 146, 46, "New game", ShowNewGame, 18);
            newGame.GetComponentInChildren<Text>().color = T.Bad;
            _newGameButton = newGame.gameObject;
            _nav = new[] { guests, lessons, bounties, recipes, locker, shop, newGame }.Select(b => (RectTransform)b.transform).ToArray();
            Ui.BoxButton(_root, 1720, 22, 160, 46, "Menu", () => { Close(); MenuClicked?.Invoke(); }, 18);
            _status = Ui.Text(Ui.Box(_root, 40, 1018, 1840, 50), "", 16, T.TextSoft, TextAnchor.MiddleLeft);
            _status.supportRichText = true;

            _main = Ui.Stretched("Main", _root, Vector2.zero, Vector2.one);
            _mapTitle = Ui.Text(Ui.Box(_main, 40, 104, 800, 30), CircuitsTitle, 15, T.Caption, TextAnchor.MiddleLeft);
            _mapTitle.supportRichText = true;
            BuildDistricts();

            // A scrolling list: more circuits than fit on screen.
            var viewport = Ui.Box(_main, 40, 138, 800, 868);
            Ui.Image(viewport, T.Scrim.WithAlpha(0.001f));
            viewport.gameObject.AddComponent<RectMask2D>();
            _circuitList = Ui.Rect("Circuits", viewport);
            _circuitList.anchorMin = new Vector2(0, 1);
            _circuitList.anchorMax = new Vector2(1, 1);
            _circuitList.pivot = new Vector2(0.5f, 1);
            _circuitList.sizeDelta = Vector2.zero;
            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = _circuitList;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 40;

            _detail = Ui.Box(_main, 880, 104, 1000, 902);
            Ui.Image(_detail, T.Panel);

            BuildStarters();
        }

        private void BuildStarters()
        {
            var T = UiTheme.Current;
            _starters = Ui.Stretched("Starters", _root, Vector2.zero, Vector2.one);
            Ui.Text(Ui.Box(_starters, 0, 130, 1920, 60), "Choose your starter deck", 38, T.Text).fontStyle = FontStyle.Bold;
            Ui.Text(Ui.Box(_starters, 0, 192, 1920, 34), "You can build new decks from your collection at any time.", 18, T.TextMuted);
            BuildOptions();
            var starters = CampaignState.Starters;
            // The tiles share the width: three at 540 wide, four at 420.
            const float gap = 30;
            float width = Math.Min(540, (1920 - 150 - gap * (starters.Length - 1)) / starters.Length);
            float left = (1920 - starters.Length * width - (starters.Length - 1) * gap) / 2;
            float card = width >= 540 ? 150 : 128, spread = width >= 540 ? 170 : 128;
            for (int i = 0; i < starters.Length; i++)
            {
                var starter = starters[i];
                var x = left + i * (width + gap);
                var tile = Ui.Box(_starters, x, 260, width, 700);
                Ui.Image(tile, T.RowB);
                for (int c = 0; c < starter.Showcase.Length; c++)
                {
                    var info = _cards.Get(starter.Showcase[c]);
                    var shown = new FieldCard
                    {
                        Code = starter.Showcase[c], Type = info?.Type ?? 0, Level = info?.Level ?? 0, Attack = info?.Attack ?? 0,
                        Defense = info?.Defense ?? 0, Position = 0x1,
                    };
                    var view = CardView.Create(tile, new Vector2(-spread + c * spread, 150), new Vector2(card, card * 1.46f), shown, info, 0, true, null, offField: true);
                    view.transform.localRotation = Quaternion.Euler(0, 0, (c - 1) * -6);
                }
                if (starter.Badge.Length > 0)
                {
                    var ribbon = Ui.Box(tile, 0, 0, width, 40);
                    Ui.Image(ribbon, T.Highlight);
                    Ui.Text(ribbon, starter.Badge.ToUpperInvariant(), 18, T.OnAccent).fontStyle = FontStyle.Bold;
                }
                if (starter.Tag.Length > 0)
                    Ui.Text(Ui.Box(tile, 20, 356, width - 40, 34), starter.Tag, 16, T.TextQuiet).fontStyle = FontStyle.Italic;
                Ui.Text(Ui.Box(tile, 20, 400, width - 40, 50), starter.Title, 30, T.Text).fontStyle = FontStyle.Bold;
                Ui.Text(Ui.Box(tile, 30, 460, width - 60, 130), starter.Blurb, 18, T.TextSoft, TextAnchor.UpperCenter);
                var pick = starter;
                Ui.BoxButton(tile, (width - 360) / 2, 600, 360, 64, "Start with this deck", () => StartWith(pick), 20);
            }
        }

        private void StartWith(StarterDeck pick)
        {
            _state.Start(pick, _eraLimited);
            Open($"Welcome to the circuit! Your {pick.Title} deck is ready, and you have {Profile.StartingDp:N0} DP for packs.");
            CampaignStarted?.Invoke();
        }

        /// <summary>For automated play: the starter tile's "Start with this deck", with the era option as it stands.</summary>
        public void DebugStartWith(StarterDeck pick) => StartWith(pick);

        /// <summary>For automated play: a match's Duel button. Null when the duel starts, else what the screen said.</summary>
        public string? DebugTryDuel(int circuit, int match)
        {
            _status.text = "";
            TryDuel(circuit, match);
            return IsOpen ? _status.text : null;
        }

        // ---- New game: start over, or New Game+ ------------------------------------------------------------

        private GameObject _newGameButton = null!;
        private RectTransform? _newGame;
        private RectTransform? _locker;
        // The Locker's tab (posters, or the Library's era mats) and its note line (what a click just did).
        private bool _lockerMats;
        private Text? _lockerNote;
        private Text _lockerLabel = null!;
        private GameObject _lockerButton = null!;
        private string? _armed; // the choice clicked once, waiting for the confirming second click

        private const string StartOverHelp =
            "<b>Start over.</b> A brand-new campaign: pick a starter deck again, with a fresh collection, 1,000 DP, level 1 and no "
            + "tokens. Your current save is kept as a backup file in mods/save, and your campaign decks stay in mods/decks.";
        private const string NewGamePlusHelp =
            "<b>New Game+.</b> The circuits start over from the first one (and the shop's era with them), but you keep everything "
            + "you've earned: every card, your decks, DP, level and Card Tokens. Each cycle is counted (NG+1, NG+2...).";

        private void ShowNewGame()
        {
            var T = UiTheme.Current;
            if (_state.Profile == null) return;
            _armed = null;
            if (_newGame != null) Destroy(_newGame.gameObject);
            _newGame = Ui.Stretched("NewGame", _root, Vector2.zero, Vector2.one);
            Ui.Image(_newGame, T.Scrim.WithAlpha(0.75f));
            var panel = Ui.Box(_newGame, 560, 300, 800, 480);
            Ui.Image(panel, T.Panel);
            Ui.Text(Ui.Box(panel, 0, 20, 800, 50), "New game", 32, T.Text).fontStyle = FontStyle.Bold;
            var help = Ui.Text(Ui.Box(panel, 40, 300, 720, 110), "Highlight an option to see what it does.", 16, T.TextSoft, TextAnchor.UpperLeft);
            help.supportRichText = true;
            GameObject? first = null;
            void Choice(float y, string label, string text, Action act)
            {
                var rt = Ui.Box(panel, 100, y, 600, 70);
                Ui.Image(rt, T.Button);
                var t = Ui.Text(rt, label, 24, T.Text);
                // Its help shows on hover and on the keyboard's focus too (Ui.Focusable: arrows reach it, Enter clicks it).
                Ui.Clickable(rt, () =>
                {
                    if (_armed != label) { _armed = label; t.text = $"{label}: click again to confirm"; help.text = text; return; }
                    Destroy(_newGame!.gameObject);
                    _newGame = null;
                    act();
                }, hover: () => help.text = text, style: HoverStyle.Button);
                Ui.Focusable(rt);
                // Opened from the keyboard: the focus starts on the first choice, so its help shows without a mouse.
                if (first == null) first = rt.gameObject;
            }
            Choice(90, "New Game+", NewGamePlusHelp, () =>
            {
                _state.NewGamePlus();
                Open($"New Game+ {_state.Profile!.Cycle}: the circuits start over, and you keep your cards, DP, level and tokens.");
                NewGamePlusStarted?.Invoke();
            });
            Choice(180, "Start over", StartOverHelp, () =>
            {
                _state.StartOver();
                Open();
            });
            Ui.BoxButton(panel, 300, 420, 200, 44, "Cancel", () => { Destroy(_newGame!.gameObject); _newGame = null; }, 18);
            if (!InputMode.Pointer && first != null) UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(first);
        }

        // ---- The Locker: the poster on your plate -----------------------------------------------------------------

        /// <summary>
        /// The Locker: the emblem and every poster, earned ones to pick and locked ones with how to earn them. Opening it
        /// marks the posters you've earned as seen (the header's count).
        /// </summary>
        private void ShowLocker()
        {
            var T = UiTheme.Current;
            var profile = _state.Profile;
            if (profile == null) return;
            if (_locker != null) Destroy(_locker.gameObject);
            _locker = Ui.Stretched("Locker", _root, Vector2.zero, Vector2.one);
            Ui.Image(_locker, T.Scrim.WithAlpha(0.88f));
            var panel = Ui.Box(_locker, 300, 130, 1320, 820);
            Ui.Image(panel, T.Panel);
            Ui.Text(Ui.Box(panel, 40, 18, 600, 50), "Locker", 32, T.Text, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            bool mats = _lockerMats && profile.LibraryOpen;
            _lockerNote = Ui.Text(Ui.Box(panel, 40, 66, 1240, 30), mats
                    ? $"The mat on your half of the duel board; your opponent brings their own. A cleared Library cup sells its era's mat: {Cosmetics.MatPrice:N0} DP."
                    : "Your plate shows this in a duel, where your opponent has a portrait. Clearing a band pins a new poster.", 16,
                T.TextSoft, TextAnchor.MiddleLeft);
            _lockerNote.supportRichText = true;
            Ui.BoxButton(panel, 1140, 24, 140, 44, "Close", CloseLocker, 18);
            if (profile.LibraryOpen)
            {
                // Two tabs once the Library is open: the poster on your plate, the mat on your side.
                Ui.BoxButton(panel, 760, 24, 170, 44, "Posters", () => { _lockerMats = false; ShowLocker(); }, 18).targetGraphic.color = mats ? T.Button : T.TabOn;
                Ui.BoxButton(panel, 940, 24, 170, 44, "Mats", () => { _lockerMats = true; ShowLocker(); }, 18).targetGraphic.color = mats ? T.TabOn : T.Button;
            }
            if (mats)
            {
                MatGrid(panel, profile);
                return;
            }

            var current = Posters.Current(_state.Circuits, profile);
            var options = new List<Poster?> { null };
            // The posters (No Limits' only once the wall has opened: Posters.Shown), then a poster for each shop set you've completed.
            options.AddRange(Posters.AllFor(profile).Where(p => Posters.Shown(p, profile)));
            _lockerIds.Clear();
            _lockerIds.AddRange(options.Select(o => o?.Id ?? Posters.Emblem));
            // Two rows fit the panel (the emblem and nine posters); more scroll.
            RectTransform grid = panel;
            float ox = 40, oy = 110;
            if (options.Count > 10)
            {
                var viewport = Ui.Box(panel, 30, 100, 1270, 712);
                Ui.Image(viewport, T.Scrim.WithAlpha(0.001f));
                viewport.gameObject.AddComponent<RectMask2D>();
                grid = Ui.Rect("Posters", viewport);
                grid.anchorMin = new Vector2(0, 1);
                grid.anchorMax = new Vector2(1, 1);
                grid.pivot = new Vector2(0.5f, 1);
                grid.sizeDelta = new Vector2(0, 20 + (options.Count + 4) / 5 * 352);
                var scroll = viewport.gameObject.AddComponent<ScrollRect>();
                scroll.viewport = viewport;
                scroll.content = grid;
                scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 40;
                ox = 10;
                oy = 10;
            }
            for (int i = 0; i < options.Count; i++)
            {
                var poster = options[i];
                bool unlocked = poster == null || Posters.IsUnlocked(poster, _state.Circuits, profile);
                bool chosen = poster == current || (poster != null && current != null && poster.Id == current.Id);
                bool fresh = poster != null && unlocked && poster != Posters.All[0] && !profile.Seen.Contains(Posters.SeenKey(poster));
                // Five to a row.
                float tx = ox + (i % 5) * 250, ty = oy + (i / 5) * 352;
                if (chosen) Ui.Image(Ui.Box(grid, tx - 4, ty - 4, 244, 344), T.Highlight).raycastTarget = false;
                var tile = Ui.Box(grid, tx, ty, 236, 336);
                Ui.Image(tile, chosen ? T.GoldWash : T.Inset);
                var art = Ui.Placed("Art", tile, new Vector2(0, 30), new Vector2(240, 320));
                art.localScale = Vector3.one * 0.68f;
                if (poster == null) EmblemTile(art);
                else PosterView.Create(art, poster, Vector2.zero);
                if (!unlocked) art.gameObject.AddComponent<CanvasGroup>().alpha = 0.22f;
                string name = poster == null ? "The emblem" : poster.Title;
                string line = chosen ? $"<color={T.GoldHex}>On your plate</color>"
                    : !unlocked ? $"<color={T.TextDimHex}>{poster!.Unlock}</color>"
                    : fresh ? $"<color={T.GoodHex}>New · click to hang it</color>"
                    : $"<color={T.TextDimHex}>Click to hang it</color>";
                var label = Ui.Text(Ui.Box(tile, 8, 262, 220, 30), unlocked ? name : $"{name} (locked)", 18, T.Text);
                label.fontStyle = FontStyle.Bold;
                Ui.Text(Ui.Box(tile, 8, 292, 220, 36), line, 14, T.Text).supportRichText = true;
                if (unlocked)
                {
                    var id = poster?.Id ?? Posters.Emblem;
                    Ui.Clickable(tile, () => PickPoster(id), style: HoverStyle.Tile);
                }
            }
            bool marked = false;
            foreach (var p in Posters.Unlocked(_state.Circuits, profile)) marked |= profile.Seen.Add(Posters.SeenKey(p));
            if (marked) _state.Save();
            _lockerLabel.text = "Locker";
        }

        /// <summary>The emblem as the plate draws it: the card back's sparkle in a gold ring, on a soft glow.</summary>
        private static void EmblemTile(RectTransform at)
        {
            void Layer(Texture texture, float size, Color color)
            {
                var image = Ui.Placed("Emblem", at, Vector2.zero, new Vector2(size, size)).gameObject.AddComponent<RawImage>();
                image.texture = texture;
                image.color = color;
                image.raycastTarget = false;
            }
            // theme: kept, the plate's emblem art (as the duel board draws it)
            Layer(BoardFx.Textures.Glow, 300, new Color(1f, 0.79f, 0.25f, 0.18f));
            Layer(BoardFx.Textures.Ring, 200, Ui.Hex("#C8901E"));
            Layer(CardView.Sparkle, 112, Ui.Hex("#FFC940"));
        }

        private void PickPoster(string id)
        {
            var profile = _state.Profile!;
            profile.Poster = id;
            _state.Save();
            ShowLocker();
            var poster = Posters.Find(id, profile);
            _status.text = poster == null ? "Your plate shows the emblem." : $"Your plate shows <b>{poster.Title}</b> in your next duel.";
        }

        /// <summary>
        /// The Locker's Mats tab: the free space mat and the Library's 12 era mats, each a preview of your side. Yours to
        /// pick, a cleared cup's to buy, the rest locked with the cup that sells them.
        /// </summary>
        private void MatGrid(RectTransform panel, Profile profile)
        {
            var T = UiTheme.Current;
            var library = _state.Library;
            for (int i = 0; i <= library.Cups.Count; i++)
            {
                int cup = i - 1;
                var id = cup < 0 ? "" : Cosmetics.MatId(library.Cups[cup]);
                bool owned = cup < 0 || profile.Mats.Contains(id), chosen = profile.Mat == id || (cup < 0 && !profile.Mats.Contains(profile.Mat));
                bool forSale = !owned && library.IsCleared(cup, profile);
                float tx = 40 + (i % 5) * 250, ty = 110 + (i / 5) * 232;
                if (chosen) Ui.Image(Ui.Box(panel, tx - 4, ty - 4, 244, 228), T.Highlight).raycastTarget = false;
                var tile = Ui.Box(panel, tx, ty, 236, 220);
                Ui.Image(tile, chosen ? T.GoldWash : T.Inset);
                var preview = Ui.Box(tile, 8, 8, 220, 104);
                var image = preview.gameObject.AddComponent<RawImage>();
                image.texture = MatPreview(id);
                image.raycastTarget = false;
                // Locked mats are tinted, not faded, so you can still see what you're working toward.
                if (!owned) image.color = forSale ? new Color(0.85f, 0.85f, 0.85f, 1) : new Color(0.55f, 0.55f, 0.6f, 1);
                if (!owned && !forSale)
                {
                    var chip = Ui.Box(preview, 6, 6, 64, 20);
                    Ui.Image(chip, T.Scrim.WithAlpha(0.6f)).raycastTarget = false;
                    var locked = Ui.Text(chip, "LOCKED", 11, Ui.Hex("#C9CED6")); // theme: kept, no role matches #C9CED6 (LOCKED on a mat preview)
                    locked.fontStyle = FontStyle.Bold;
                    locked.raycastTarget = false;
                }
                var c = cup < 0 ? null : library.Cups[cup];
                var label = Ui.Text(Ui.Box(tile, 8, 118, 220, 50), c == null ? "Space\n<size=13>Default · free</size>" : $"{c.Era}\n<size=13>{c.Name}</size>", 18, T.Text);
                label.fontStyle = FontStyle.Bold;
                label.supportRichText = true;
                string line = chosen ? $"<color={T.GoldHex}>On your side</color>"
                    : owned ? $"<color={T.GoodHex}>Owned</color> · click to use"
                    : forSale ? $"<color={(profile.Dp >= Cosmetics.MatPrice ? T.GoldHex : T.BadHex)}>{Cosmetics.MatPrice:N0} DP</color> · click to buy"
                    : $"<color={T.TextDimHex}>Clear the {c!.Name}</color>";
                Ui.Text(Ui.Box(tile, 8, 168, 220, 46), line, 14, T.Text).supportRichText = true;
                int pick = cup;
                if (owned || forSale) Ui.Clickable(tile, () => { var note = MatClicked(pick); ShowLocker(); if (_lockerNote != null) _lockerNote.text = note; }, style: HoverStyle.Tile);
            }
        }

        /// <summary>An era mat's tile or the cup panel's Era mat button: buys a cleared cup's mat, or puts an owned one on (cup -1: the space mat). What happened, for the screen.</summary>
        private string MatClicked(int cup)
        {
            var T = UiTheme.Current;
            var profile = _state.Profile!;
            var library = _state.Library;
            if (cup < 0) { Cosmetics.PickMat(profile, ""); _state.Save(); return "Your side shows the <b>space mat</b> in your next duel."; }
            var c = library.Cups[cup];
            var id = Cosmetics.MatId(c);
            if (profile.Mats.Contains(id))
            {
                Cosmetics.PickMat(profile, id);
                _state.Save();
                return $"Your side shows the <b>{c.Era} mat</b> in your next duel. Switch mats in the Locker.";
            }
            var why = Cosmetics.MatBlocked(profile, library, cup);
            if (why != null || !Cosmetics.BuyMat(profile, library, cup)) { Sounds.Play("error"); return $"<color={T.BadHex}>Can't buy the {c.Era} mat: {why}.</color>"; }
            Sounds.Play("token");
            _state.Save();
            Refresh();
            return $"<color={T.GoodHex}>Bought the {c.Era} mat for {Cosmetics.MatPrice:N0} DP.</color> It's on your side from your next duel; switch mats in the Locker.";
        }

        private static readonly Dictionary<string, Texture2D?> MatPictures = new Dictionary<string, Texture2D?>();

        /// <summary>A mat's look for a preview: its picture, else its painted signature; "" (or one not painted yet) the space mat.</summary>
        public static Texture MatPreview(string id)
        {
            // A picture is loaded once (the Locker redraws on every click).
            if (id.Length > 0 && !MatPictures.TryGetValue(id, out var picture)) MatPictures[id] = picture = Playmat.LoadCustom(id);
            if (id.Length > 0 && MatPictures[id] is Texture2D loaded) return loaded;
            return id.Length > 0 && Playmat.Signatures.TryGetValue(id, out var era)
                ? Playmat.Painted(era.Theme, 0, era.Motif) : Playmat.Painted(Playmat.SpaceYou, 0, "space");
        }

        private void CloseLocker()
        {
            if (_locker != null) Destroy(_locker.gameObject);
            _locker = null;
        }

        /// <summary>For automated checks: opens the Locker; returns how many posters it shows earned.</summary>
        public int DebugOpenLocker()
        {
            ShowLocker();
            return Posters.Unlocked(_state.Circuits, _state.Profile!).Count;
        }

        /// <summary>For automated checks: the Locker on its Mats tab (false: its Posters tab).</summary>
        public void DebugLockerMats(bool on)
        {
            _lockerMats = on;
            ShowLocker();
        }

        /// <summary>For automated checks: the Locker's tile for a cup's era mat (-1 the space mat), as clicked; what the Locker said.</summary>
        public string DebugMatTile(int cup)
        {
            var note = MatClicked(cup);
            ShowLocker();
            return note;
        }

        /// <summary>For automated checks: hangs a poster (or "emblem") as a click on its tile would, if it's earned.</summary>
        public bool DebugPickPoster(string id)
        {
            var poster = Posters.Find(id, _state.Profile!);
            if (poster != null && !Posters.IsUnlocked(poster, _state.Circuits, _state.Profile!)) return false;
            PickPoster(id);
            return true;
        }

        public void DebugCloseLocker() => CloseLocker();

        /// <summary>For automated checks: whether the Locker (as last opened) shows a poster (by id).</summary>
        public bool DebugLockerShows(string id) => _locker != null && _lockerIds.Contains(id);

        /// <summary>The ids the Locker shows, emblem first (for checks).</summary>
        private readonly List<string> _lockerIds = new List<string>();

        // ---- Gus's bounty board ------------------------------------------------------------------------------

        private Text _bountiesLabel = null!;
        private GameObject _bountiesButton = null!;
        private RectTransform? _bounties;

        /// <summary>"Bounties (2 open)": the header button, counting the open bounties on the band's board.</summary>
        private void BountiesLabel(Profile profile)
        {
            var T = UiTheme.Current;
            int open = _state.Unlocks.OpenBounties(profile);
            _bountiesLabel.text = open > 0 ? $"Bounties <color={T.GoodHex}>({open} open)</color>" : "Bounties";
        }

        /// <summary>
        /// Gus's bounty board: the band's three bounties, each open or claimed, and what the next one pays (a card your
        /// Recipe needs, or a Card Token before you've set one).
        /// </summary>
        private void ShowBounties()
        {
            var T = UiTheme.Current;
            var profile = _state.Profile;
            // Not before Gus puts the board up (BountyBoard.IsOpen): there's no board to show yet.
            if (profile == null || !_state.Unlocks.BountiesOpen(profile)) return;
            CloseBounties();
            _bounties = Ui.Stretched("Bounties", _root, Vector2.zero, Vector2.one);
            Ui.Image(_bounties, T.Scrim.WithAlpha(0.88f));
            var panel = Ui.Box(_bounties, 360, 210, 1200, 640);
            Ui.Image(panel, T.Panel);
            Ui.Text(Ui.Box(panel, 40, 18, 900, 50), "Gus's bounty board", 32, T.Text, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            Ui.BoxButton(panel, 1040, 24, 120, 44, "Close", CloseBounties, 18);
            var band = _state.Unlocks.BoardBand(profile);
            var rows = _state.Unlocks.Board(profile);
            Ui.Text(Ui.Box(panel, 40, 66, 1120, 30), rows.Count == 0
                    ? "No bounties are posted for this band."
                    : $"The {band} band's bounties. Win a circuit duel in this band the way one asks, and it's yours: each pays once.",
                16, T.TextSoft, TextAnchor.MiddleLeft);
            var pays = _state.Unlocks.BountyPays(profile);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var row = Ui.Box(panel, 40, 116 + i * 130, 1120, 116);
                Ui.Image(row, r.Claimed ? T.RowWon : i % 2 == 0 ? T.RowA : T.RowB);
                Ui.Image(Ui.Box(row, 0, 0, 6, 116), r.Claimed ? T.GoodStripe : T.Brass);
                Ui.Text(Ui.Box(row, 28, 12, 880, 44), r.Bounty.Text, 22, T.Text, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
                Ui.Text(Ui.Box(row, 28, 60, 880, 40), r.Claimed ? "Claimed: Gus paid this one." : pays, 16, r.Claimed ? T.TextFaint : T.GoldSoft, TextAnchor.MiddleLeft);
                var state = Ui.Text(Ui.Box(row, 920, 0, 180, 116), r.Claimed ? "CLAIMED" : "OPEN", 22, r.Claimed ? T.Good : T.Gold);
                state.fontStyle = FontStyle.Bold;
            }
            Ui.Text(Ui.Box(panel, 40, 520, 1120, 90),
                "Bounties count in one-on-one circuit duels you win, not in Tag Cups, guests or the Library. A bounty pays a card your Recipe still needs "
                + "(Recipe in the deck editor, or Wishlist in the Recipe Book); with none set, the first pays a Card Token and the rest wait for one.",
                15, T.TextQuiet, TextAnchor.UpperLeft);
        }

        private void CloseBounties()
        {
            if (_bounties != null) Destroy(_bounties.gameObject);
            _bounties = null;
        }

        /// <summary>For automated checks: opens the bounty board from its button; the rows it shows.</summary>
        public int DebugOpenBounties()
        {
            if (_state.Profile == null) return 0;
            ShowBounties();
            return _state.Unlocks.Board(_state.Profile).Count;
        }

        public bool DebugBountiesOpen => _bounties != null;
        /// <summary>For automated checks: whether the Bounties button shows.</summary>
        public bool DebugBountiesButton => _bountiesButton.activeSelf;
        public void DebugCloseBounties() => CloseBounties();

        // ---- Bond 3: one trade -------------------------------------------------------------------------------

        private RectTransform? _trade;
        private Text? _tradeNote;
        private string? _tradeArmed;
        private float _tradeArmedAt;
        private Ygo.Engine.AI.Personality? _tradeWith;
        private uint _tradeTake;

        private int TokenCost(uint code) => CardTrader.Cost(code, _state.Printings, _cards);

        /// <summary>A vertical list that scrolls inside <paramref name="parent"/>: its content, <paramref name="height"/> tall.</summary>
        private static RectTransform ScrollList(RectTransform parent, float x, float y, float w, float h, float height)
        {
            var viewport = Ui.Box(parent, x, y, w, h);
            Ui.Image(viewport, UiTheme.Current.Well);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Rect("Rows", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = new Vector2(0, height);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            return content;
        }

        /// <summary>
        /// The bond 3 trade with a partner: on the left the cards they'd give (from their decks), on the right your spare
        /// copies worth at least as many Card Tokens as the one picked. Trade arms; a second click trades.
        /// </summary>
        private void ShowTrade(Ygo.Engine.AI.Personality partner, uint take = 0, string? note = null)
        {
            var T = UiTheme.Current;
            var profile = _state.Profile;
            if (profile == null) return;
            if (_trade != null) Destroy(_trade.gameObject);
            _tradeWith = partner;
            _trade = Ui.Stretched("BondTrade", _root, Vector2.zero, Vector2.one);
            Ui.Image(_trade, T.Scrim.WithAlpha(0.88f));
            var panel = Ui.Box(_trade, 300, 130, 1320, 820);
            Ui.Image(panel, T.Panel);
            Ui.Text(Ui.Box(panel, 40, 18, 1000, 50), $"A trade with {partner.Name}", 32, T.Text, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            Ui.BoxButton(panel, 1160, 24, 120, 44, "Close", CloseTrade, 18);
            Ui.Text(Ui.Box(panel, 40, 66, 1240, 30),
                $"Bond {TagPartners.DeckBond}: one trade. Pick a card from {partner.Name}'s decks, then give a spare copy worth at least as many Card Tokens.",
                16, T.TextSoft, TextAnchor.MiddleLeft);
            var offers = _state.Unlocks.TradeOffers(profile, partner.Id);
            if (!offers.Contains(take)) take = offers.FirstOrDefault();
            _tradeTake = take;

            Ui.Text(Ui.Box(panel, 40, 106, 600, 30), $"{partner.Name.ToUpperInvariant()} GIVES", 15, T.Caption, TextAnchor.MiddleLeft);
            Ui.Text(Ui.Box(panel, 680, 106, 600, 30), "YOU GIVE", 15, T.Caption, TextAnchor.MiddleLeft);
            const float rowH = 44;
            var left = ScrollList(panel, 40, 140, 600, 560, Math.Max(1, offers.Count) * rowH);
            if (offers.Count == 0)
                Ui.Text(Ui.Box(left, 16, 0, 570, rowH), "Nothing to trade: you have three of every card they'd give.", 16, T.TextFaint, TextAnchor.MiddleLeft);
            for (int i = 0; i < offers.Count; i++)
            {
                uint code = offers[i];
                bool picked = code == take;
                var row = Ui.Box(left, 0, i * rowH, 600, rowH - 4);
                Ui.Image(row, picked ? T.RowSelected : i % 2 == 0 ? T.RowA : T.RowB);
                int cost = TokenCost(code);
                Ui.Text(Ui.Box(row, 16, 0, 420, rowH - 4), _cards.Get(code)?.Name ?? code.ToString(), 16, T.Text, TextAnchor.MiddleLeft);
                Ui.Text(Ui.Box(row, 440, 0, 144, rowH - 4), $"{cost} token{(cost == 1 ? "" : "s")} · own {profile.Collection.Count(code)}", 14, T.TextQuiet, TextAnchor.MiddleRight);
                Ui.Clickable(row, () => { _tradeArmed = null; ShowTrade(partner, code); }, style: HoverStyle.Row);
            }

            var gives = take == 0 ? new List<CardCopy>() : _state.Unlocks.TradeGives(profile, take, _state.NeededByDecks);
            var right = ScrollList(panel, 680, 140, 600, 560, Math.Max(1, gives.Count) * rowH);
            if (take != 0 && gives.Count == 0)
                Ui.Text(Ui.Box(right, 16, 0, 570, rowH * 2), $"No spare copy worth {TokenCost(take)} Card Token{(TokenCost(take) == 1 ? "" : "s")} or more: your saved decks need the ones you have.",
                    16, T.TextFaint, TextAnchor.MiddleLeft);
            for (int i = 0; i < gives.Count; i++)
            {
                var copy = gives[i];
                var row = Ui.Box(right, 0, i * rowH, 600, rowH - 4);
                Ui.Image(row, i % 2 == 0 ? T.RowA : T.RowB);
                var look = $"{Ygo.Engine.Data.Rarities.Name(copy.Rarity)} · {Grades.Describe(copy)}";
                var label = Ui.Text(Ui.Box(row, 16, 0, 440, rowH - 4), $"{_cards.Get(copy.Code)?.Name ?? copy.Code.ToString()}  <color={T.TextDimHex}>{look}</color>", 15, T.Text, TextAnchor.MiddleLeft);
                label.supportRichText = true;
                var key = $"{take}:{copy.Id}";
                bool armed = _tradeArmed == key && Time.unscaledTime - _tradeArmedAt < 5;
                var b = Ui.BoxButton(row, 470, 4, 120, rowH - 12, armed ? "Confirm" : "Trade", () => TradeClicked(partner, take, copy, key), 15);
                if (armed) b.targetGraphic.color = T.Primary;
            }
            _tradeNote = Ui.Text(Ui.Box(panel, 40, 712, 1240, 90), note ?? (take == 0 ? "" : $"You'd get {_cards.Get(take)?.Name} as a Raw Near Mint copy at its commonest printing."),
                16, T.TextSoft, TextAnchor.UpperLeft);
            _tradeNote.supportRichText = true;
        }

        private void TradeClicked(Ygo.Engine.AI.Personality partner, uint take, CardCopy give, string key)
        {
            var T = UiTheme.Current;
            if (!(_tradeArmed == key && Time.unscaledTime - _tradeArmedAt < 5))
            {
                _tradeArmed = key;
                _tradeArmedAt = Time.unscaledTime;
                ShowTrade(partner, take, $"Give your {_cards.Get(give.Code)?.Name} for {_cards.Get(take)?.Name}? Click Confirm to trade.");
                return;
            }
            _tradeArmed = null;
            var grants = _state.BondTrade(partner.Id, take, give, out var refused);
            if (grants == null)
            {
                Sounds.Play("error");
                ShowTrade(partner, take, $"<color={T.BadHex}>No trade: {refused}.</color>");
                return;
            }
            Sounds.Play("token");
            CloseTrade();
            _status.text = $"<color={T.GoldSoftHex}>{CampaignState.GrantsLine(grants)}</color>";
            if (_selectedCup >= 0) ShowTagCup(_selectedCup);
        }

        private void CloseTrade()
        {
            if (_trade != null) Destroy(_trade.gameObject);
            _trade = null;
            _tradeNote = null;
            _tradeArmed = null;
        }

        /// <summary>For automated checks: the Trade button for the partner shown; whether it opened, the offers, and the spares for the first.</summary>
        public (bool opened, int offers, int gives) DebugOpenTrade()
        {
            var profile = _state.Profile;
            var partners = _state.TagPartnerChoices();
            if (profile == null || partners.Count == 0) return (false, 0, 0);
            var partner = partners[Mathf.Clamp(_partnerIndex, 0, partners.Count - 1)];
            if (TagPartners.IsShopRegular(partner) || !_state.Unlocks.CanTrade(profile, partner.Id)) return (false, 0, 0);
            ShowTrade(partner);
            int gives = _tradeTake == 0 ? 0 : _state.Unlocks.TradeGives(profile, _tradeTake, _state.NeededByDecks).Count;
            return (_trade != null, _state.Unlocks.TradeOffers(profile, partner.Id).Count, gives);
        }

        public void DebugCloseTrade() => CloseTrade();

        /// <summary>For automated checks: the Tag Cup shown on the right (-1: a circuit), to put back after a check.</summary>
        public int DebugSelectedCup { get => _selectedCup; set => _selectedCup = value; }

        /// <summary>
        /// For automated play: in the open trade dialog, the first card they give for your first spare, Trade then Confirm.
        /// What the screen said: the trade's line, or why not.
        /// </summary>
        public string DebugTakeTrade()
        {
            var profile = _state.Profile;
            if (profile == null || _trade == null || _tradeWith == null) return "no trade dialog";
            var partner = _tradeWith;
            if (_tradeTake == 0) { CloseTrade(); return "nothing they give that you have fewer than three of"; }
            var give = _state.Unlocks.TradeGives(profile, _tradeTake, _state.NeededByDecks).FirstOrDefault();
            if (give == null) { CloseTrade(); return $"no spare worth {TokenCost(_tradeTake)} tokens for {_cards.Get(_tradeTake)?.Name}"; }
            var key = $"{_tradeTake}:{give.Id}";
            TradeClicked(partner, _tradeTake, give, key);
            TradeClicked(partner, _tradeTake, give, key);
            if (_trade != null) { var said = _tradeNote?.text ?? ""; CloseTrade(); return said; }
            return _status.text;
        }

        /// <summary>For automated checks: runs New Game+ as the dialog would.</summary>
        public void DebugNewGamePlus() { _state.NewGamePlus(); Open(); }

        /// <summary>For automated play: New game, then New Game+ clicked twice (it confirms), story and all.</summary>
        public void DebugChooseNewGamePlus()
        {
            _state.NewGamePlus();
            Open($"New Game+ {_state.Profile!.Cycle}: the circuits start over, and you keep your cards, DP, level and tokens.");
            NewGamePlusStarted?.Invoke();
        }

        // ---- Campaign options (chosen before starting) --------------------------------------------------

        private bool _eraLimited;
        private readonly Image[] _ruleFills = new Image[2];
        private readonly Text[] _ruleLabels = new Text[2];

        private const string SameEitherWay = " The champions you face play the same either way.";
        private const string ShopWayHelp =
            "<b>The shop's way.</b> Build any deck from what you own: nobody checks a ban list, and Card Tokens trade for any "
            + "card of any era at the Card Trader. Recommended for a first run." + SameEitherWay;
        private const string EraLimitedHelp =
            "<b>The history way: Historic Rules (era-limited cards).</b> Your team plays by each circuit's real ban list (Ban "
            + "Passes let a few cards past it), tokens only buy cards released by the Card Shop's current era, and a circuit "
            + "only allows cards released by its format: from the Underdog Open on, nothing printed after 2011. The classic "
            + "starters come in their 2011 editions; Mekk-Knight Invoked Evolved, a 2025 deck, can only play the Beginner band "
            + "under it. Build from packs as you go." + SameEitherWay;

        // Two choices side by side, the picked one lit (the persona walkthrough: a single toggle hid what "off" meant).
        private void BuildOptions()
        {
            var T = UiTheme.Current;
            var labels = new[] { "The shop's way", "The history way: Historic Rules" };
            for (int i = 0; i < 2; i++)
            {
                bool history = i == 1;
                var box = Ui.Box(_starters, 560 + i * 404, 972, 396, 42);
                _ruleFills[i] = Ui.Image(box, T.Button);
                _ruleLabels[i] = Ui.Text(box, labels[i], 18, T.Text);
                _ruleLabels[i].supportRichText = true;
                // Its help shows on hover and on the keyboard's focus too (Ui.Focusable); the picked one (TabOn) doesn't light up.
                Ui.Clickable(box, () => { _eraLimited = history; ShowOptions(); _status.text = history ? EraLimitedHelp : ShopWayHelp; },
                    hover: () => _status.text = history ? EraLimitedHelp : ShopWayHelp, style: HoverStyle.Button);
                Ui.Focusable(box);
            }
            ShowOptions();
        }

        private void ShowOptions()
        {
            var T = UiTheme.Current;
            for (int i = 0; i < 2; i++)
            {
                bool picked = (i == 1) == _eraLimited;
                _ruleFills[i].color = picked ? T.TabOn : T.ButtonOff;
                _ruleLabels[i].color = picked ? T.Gold : T.TextDim;
                _ruleLabels[i].fontStyle = picked ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        /// <summary>For automated checks: the option's explanation, as hovering shows it.</summary>
        public string DebugHoverEraOption()
        {
            _status.text = EraLimitedHelp;
            return _status.text;
        }

        // ---- Circuits --------------------------------------------------------------------------------

        private void Refresh()
        {
            var T = UiTheme.Current;
            var profile = _state.Profile!;
            var circuits = _state.Circuits;
            // Three lines, never wrapped: the wallet (DP, a held stake, tokens), level and circuits, the collection.
            var held = profile.AnteLive is { Held: > 0 } live ? $" ({live.Held:N0} held)" : "";
            _wallet = new List<(string, int, string?)[]>
            {
                new (string, int, string?)[] { ($"{profile.Dp:N0} DP", 24, null), (held, 18, T.TextDimHex), ($"   {profile.Tokens:N0} token{(profile.Tokens == 1 ? "" : "s")}", 20, T.GoldSoftHex) },
                new (string, int, string?)[] { ($"Level {profile.Level}{(profile.Cycle > 0 ? $" · NG+{profile.Cycle}" : "")}", 20, T.GoodHex),
                    ($"   {circuits.ClearedCount(profile)}/{circuits.All.Count} circuits cleared", 15, T.TextDimHex) },
                new (string, int, string?)[] { ($"{profile.Collection.Total:N0} cards ({profile.Collection.Distinct:N0} different) · {profile.PacksOpened:N0} pack{(profile.PacksOpened == 1 ? "" : "s")} opened", 15, T.TextDimHex) },
            };
            FitWallet();

            for (int i = _circuitList.childCount - 1; i >= 0; i--) Destroy(_circuitList.GetChild(i).gameObject);
            ShowDistricts(profile);
            float y = _alleyView ? BuildAlleyMap(profile) : _libraryView ? BuildShelf(profile) : BuildMap(profile, circuits);
            _circuitList.sizeDelta = new Vector2(0, y);
            ShowSelected();
        }

        private readonly List<float> _rowTops = new List<float>();

        // ---- The map ---------------------------------------------------------------------------------

        private const float MapWidth = 800, NodeStep = 150, RegionPad = 160, Node = 96;

        /// <summary>Each difficulty band's region: its colors and a painted motif, like the duel mats.</summary>
        private static readonly Dictionary<string, (string deep, string mid, string accent, string motif)> Regions = new Dictionary<string, (string, string, string, string)>
        {
            ["Beginner"] = ("#1B1710", "#4A3B22", "#C9A15A", "sparkles"),
            ["Novice"] = ("#0B1A14", "#1F5A3C", "#7BE0A8", "runes"),
            ["Intermediate"] = ("#081726", "#14466E", "#5FC8FF", "waves"),
            ["Advanced"] = ("#1E0A08", "#6B1E12", "#FF8A3D", "flames"),
            ["Expert"] = ("#110A1C", "#3E1E66", "#B98CFF", "mist"),
            ["Master"] = ("#1C1A12", "#5E5326", "#FFE38A", "sigil"),
            ["Gauntlet"] = ("#080608", "#2A0F14", "#FF4040", "void"),
        };

        /// <summary>
        /// The campaign as a map you climb: the bands are regions from Beginner at the bottom to the Gauntlet at the top,
        /// each circuit a node on a winding path with its boss's face, a lock until it opens and a crown once cleared;
        /// Tag Cups branch off the circuit that opens them. Returns the map's height.
        /// </summary>
        private float BuildMap(Profile profile, Circuits circuits)
        {
            var T = UiTheme.Current;
            int n = circuits.All.Count;
            // Bands in order, and the height of each region.
            var bands = new List<(string band, int first, int count)>();
            for (int i = 0; i < n; i++)
            {
                var band = circuits.All[i].Band;
                if (bands.Count == 0 || bands[bands.Count - 1].band != band) bands.Add((band, i, 1));
                else { var last = bands[bands.Count - 1]; bands[bands.Count - 1] = (last.band, last.first, last.count + 1); }
            }
            float height = bands.Sum(b => b.count * NodeStep + RegionPad) + 40;
            var centers = new Vector2[n];
            var labels = new List<Action>();
            // Regions, bottom to top (y measured from the top of the map).
            float bottom = height;
            foreach (var (band, first, count) in bands)
            {
                float regionH = count * NodeStep + RegionPad;
                float top = bottom - regionH;
                var look = Regions.TryGetValue(band, out var r) ? r : ("#0E1320", "#22324F", "#7FA6E0", "runes");
                var theme = new Playmat.Theme(look.Item1, look.Item2, look.Item3);
                var region = Ui.Box(_circuitList, 0, top, MapWidth, regionH);
                var paint = region.gameObject.AddComponent<RawImage>();
                // A picture of the region (map/<band>.png, see docs/dev/MAP.md) or the painted mat for its colors.
                var picture = MapArt(band);
                paint.texture = picture ?? Playmat.Painted(theme, 1, look.Item4);
                // Fill the region without stretching: crop the picture's sides or top and bottom.
                float aspect = MapWidth / regionH, texAspect = (float)paint.texture.width / paint.texture.height;
                paint.uvRect = aspect < texAspect ? new Rect(0.5f - aspect / texAspect / 2, 0, aspect / texAspect, 1)
                    : new Rect(0, 0.5f - texAspect / aspect / 2, 1, texAspect / aspect);
                bool bandOpen = circuits.IsUnlocked(first, profile);
                paint.color = bandOpen ? Color.white : new Color(0.45f, 0.45f, 0.5f);
                var edge = Ui.Box(region, 0, 0, MapWidth, 3);
                Ui.Image(edge, new Color(theme.Accent.r, theme.Accent.g, theme.Accent.b, 0.5f));
                var era = circuits.All[first].Era;
                // The band's name where you enter it, at the bottom of its region, on a dark strip (drawn after the path).
                float stripY = top + regionH - 48;
                // The shop's rule for this band (its era on arrival, a year a level up to the next band's era); for the
                // band you're in, today's year and the level that opens the rest, worded as the shop's own header.
                int bandNext = circuits.All.Select(c => c.Era).Where(e => e > era).DefaultIfEmpty(era).Min();
                int shopNow = circuits.ShopYear(profile);
                string shopRule = !bandOpen || circuits.BandEra(profile) != era ? (era < bandNext ? $"Shop: sets up to {era} on arrival, a year a level to {bandNext}" : $"Shop: sets up to {era}")
                    : shopNow < bandNext ? $"Shop now: sets up to {shopNow}, to {bandNext} at level {profile.Level + bandNext - shopNow}"
                    : bandNext > era ? $"Shop now: sets up to {shopNow}; newer ones with the next band" : $"Shop now: sets up to {shopNow}";
                // theme: kept, no role matches #C8CEDA (the band strip's shop rule)
                string bandText = band.ToUpperInvariant() + (era > 0 && band != "Gauntlet" ? $"   <size=14><color=#C8CEDA>{shopRule}</color></size>" : "");
                var bandColor = bandOpen ? theme.Accent : T.Text.WithAlpha(0.4f);
                labels.Add(() =>
                {
                    Ui.Image(Ui.Box(_circuitList, 0, stripY, MapWidth, 42), T.Glass.WithAlpha(0.92f)).raycastTarget = false;
                    var title = Ui.Text(Ui.Box(_circuitList, 20, stripY + 4, 760, 34), bandText, 22, bandColor, TextAnchor.MiddleLeft);
                    title.fontStyle = FontStyle.Bold;
                    title.supportRichText = true;
                    title.raycastTarget = false;
                });
                for (int k = 0; k < count; k++)
                {
                    int i = first + k;
                    // A winding path: nodes swing left and right as they climb.
                    float x = MapWidth / 2 + 230 * Mathf.Sin(i * 1.25f + 0.4f);
                    float yc = bottom - 140 - k * NodeStep; // clear of the band's name at the bottom
                    centers[i] = new Vector2(x, yc);
                }
                bottom = top;
            }

            // The path between circuits: gold where you've been, grey ahead.
            for (int i = 0; i + 1 < n; i++)
                Segment(centers[i], centers[i + 1], circuits.IsUnlocked(i + 1, profile) ? T.Highlight : T.Text.WithAlpha(0.18f), 6);

            // Tag Cups: a smaller node branching off the circuit that opens them.
            _rowTops.Clear();
            for (int t = 0; t < circuits.TagCups.Count; t++)
            {
                var cup = circuits.TagCups[t];
                if (cup.After < 0 || cup.After >= n) continue;
                var from = centers[cup.After];
                // Level with its circuit, on the open side (away from the path's swing).
                var at = new Vector2(from.x > MapWidth / 2 ? from.x - 210 : from.x + 210, from.y + 6);
                bool cupOpen = circuits.IsTagCupOpen(t, profile), cupCleared = circuits.IsTagCupCleared(t, profile);
                Segment(from, at, cupOpen ? T.GoldSoft : T.Text.WithAlpha(0.15f), 3);
                int cupIndex = t;
                var lead = cup.Matches.Count > 0 ? cup.Matches[cup.Matches.Count - 1].lead.Character.Id : "";
                MapNode(at, 70, lead, cupOpen, cupCleared, t == _selectedCup, false, "TAG", cup.Name,
                    !cupOpen ? "" : $"{Enumerable.Range(0, cup.Matches.Count).Count(m => profile.MatchWins.ContainsKey(Circuits.TagKey(t, m)))} / {cup.Matches.Count}",
                    T.GoldSoft, cupOpen ? () => { _selectedCup = cupIndex; Refresh(); } : null);
            }

            // Band names, drawn over the path lines.
            foreach (var label in labels) label();
            int current = circuits.Current(profile);
            for (int i = 0; i < n; i++)
            {
                var circuit = circuits.All[i];
                bool open = circuits.IsUnlocked(i, profile), cleared = circuits.IsCleared(i, profile);
                int won = Enumerable.Range(0, circuit.Matches.Count).Count(m => profile.HasWon(i, m));
                var boss = circuit.Matches.Count > 0 ? circuit.Matches[circuit.Matches.Count - 1].Character.Id : "";
                int index = i;
                MapNode(centers[i], Node, boss, open, cleared, i == _selected && _selectedCup < 0, i == current && !cleared, (i + 1).ToString(), circuit.Name,
                    !open ? "LOCKED" : cleared ? "CLEARED" : $"{won} / {circuit.Matches.Count}", T.Highlight,
                    open ? () => { _selected = index; _selectedCup = -1; Refresh(); } : null);
                _rowTops.Add(centers[i].y);
            }
            return height;
        }

        private static readonly Dictionary<string, Texture2D?> MapArtCache = new Dictionary<string, Texture2D?>();

        /// <summary>A region's picture: map/&lt;band&gt;.png or .jpg in mods (yours first), StreamingAssets or the repo's art.</summary>
        private Texture2D? MapArt(string band)
        {
            var key = band.ToLowerInvariant();
            if (MapArtCache.TryGetValue(key, out var cached)) return cached;
            Texture2D? found = null;
            foreach (var folder in new[]
                     {
                         System.IO.Path.Combine(_state.ModsRoot, "map"),
                         System.IO.Path.Combine(Application.streamingAssetsPath, "map"),
                         System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "art", "map")),
                     })
                foreach (var ext in new[] { ".png", ".jpg" })
                {
                    var path = System.IO.Path.Combine(folder, key + ext);
                    if (found != null || !System.IO.File.Exists(path)) continue;
                    var tex = new Texture2D(2, 2) { wrapMode = TextureWrapMode.Clamp };
                    if (tex.LoadImage(System.IO.File.ReadAllBytes(path))) found = tex;
                }
            return MapArtCache[key] = found;
        }

        /// <summary>A straight line between two points on the map.</summary>
        private void Segment(Vector2 a, Vector2 b, Color color, float width)
        {
            var mid = (a + b) / 2;
            var rt = Ui.Box(_circuitList, mid.x, mid.y, Vector2.Distance(a, b), width);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(mid.x, -mid.y);
            var d = b - a;
            rt.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            Ui.Image(rt, color).raycastTarget = false;
        }

        /// <summary>
        /// One place on the map: a round window with the boss's face (dimmed and locked until it opens), a crown once
        /// cleared, a pulsing ring on the next one to play, and its name and progress underneath.
        /// </summary>
        private void MapNode(Vector2 at, float size, string face, bool open, bool cleared, bool selected, bool next, string number,
            string name, string progress, Color accent, Action? onClick)
        {
            var T = UiTheme.Current;
            var node = Ui.Box(_circuitList, 0, 0, size, size);
            node.pivot = new Vector2(0.5f, 0.5f);
            node.anchoredPosition = new Vector2(at.x, -at.y);
            if (next)
            {
                var halo = Ui.Placed("Halo", node, Vector2.zero, new Vector2(size * 2.4f, size * 2.4f)).gameObject.AddComponent<RawImage>();
                halo.texture = BoardFx.Textures.Glow;
                halo.color = new Color(accent.r, accent.g, accent.b, 0.7f);
                halo.raycastTarget = false;
                halo.gameObject.AddComponent<MapPulse>();
            }
            var rim = Ui.Placed("Rim", node, Vector2.zero, new Vector2(size + 8, size + 8)).gameObject.AddComponent<RawImage>();
            rim.texture = Icons.Round;
            rim.color = selected ? Color.white : cleared ? accent : open ? Ui.Hex("#6FB6FF") : new Color(0.3f, 0.32f, 0.38f);
            // The face, cut to a circle.
            var window = Ui.Placed("Window", node, Vector2.zero, new Vector2(size, size));
            var mask = window.gameObject.AddComponent<RawImage>();
            mask.texture = Icons.Round;
            mask.color = Ui.Hex("#1A2033");
            window.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            if (face.Length > 0 && Portraits.PlaceHead(window, face) && window.GetChild(window.childCount - 1).GetComponent<RawImage>() is RawImage portrait)
            {
                portrait.color = open ? Color.white : new Color(0.25f, 0.25f, 0.3f);
                portrait.raycastTarget = false;
            }
            if (!open) Badge(node, "lock", new Vector2(0, 0), size * 0.42f, new Color(0.75f, 0.78f, 0.85f));
            if (cleared) Badge(node, "crown", new Vector2(0, size / 2 + 6), size * 0.42f, accent);
            // The circuit's number on a small tab.
            var tab = Ui.Placed("Number", node, new Vector2(-size / 2 + 6, size / 2 - 6), new Vector2(number.Length > 2 ? 44 : 30, 26));
            Ui.Image(tab, open ? accent : new Color(0.3f, 0.32f, 0.38f)).raycastTarget = false;
            var num = Ui.Text(tab, number, 15, Ui.Hex("#121620"));
            num.fontStyle = FontStyle.Bold;
            // A dark tag behind the name and progress, so they read on any map picture.
            float tagWidth = Mathf.Min(330, name.Length * (size > 80 ? 9.5f : 8f) + 24);
            var backing = Ui.Placed("NameTag", node, new Vector2(0, -size / 2 - (progress.Length > 0 ? 25 : 16)), new Vector2(tagWidth, progress.Length > 0 ? 44 : 26));
            Ui.Image(backing, T.Glass.WithAlpha(0.72f)).raycastTarget = false;
            var label = Ui.Text(Ui.Placed("Name", node, new Vector2(0, -size / 2 - 16), new Vector2(320, 24)), name, size > 80 ? 17 : 14,
                open ? T.Text : T.Text.WithAlpha(0.6f));
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;
            label.gameObject.AddComponent<Outline>().effectColor = T.Scrim.WithAlpha(0.8f);
            if (progress.Length > 0)
            {
                var p = Ui.Text(Ui.Placed("Progress", node, new Vector2(0, -size / 2 - 36), new Vector2(200, 20)), progress, 15,
                    cleared ? accent : T.TextSoft.WithAlpha(open ? 1 : 0.75f));
                p.raycastTarget = false;
            }
            if (onClick != null) Ui.Clickable(window, () => { Sounds.Play("click"); onClick(); });
            // Hover: the ring brightens and the name strip lightens, drawn at 1.06 (the hit area stays the window); a locked
            // node only brightens its ring.
            var hover = UiHover.Add(window, HoverStyle.Node, fill: rim, visual: node);
            hover.Strip = backing.GetComponent<Graphic>();
            hover.Moves = open;
        }

        private static void Badge(RectTransform node, string icon, Vector2 at, float size, Color color)
        {
            var img = Ui.Placed(icon, node, at, new Vector2(size, size)).gameObject.AddComponent<RawImage>();
            img.texture = Icons.For(icon);
            img.color = color;
            img.raycastTarget = false;
            img.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2, -2);
        }

        /// <summary>The next circuit's halo breathes.</summary>
        private sealed class MapPulse : MonoBehaviour
        {
            private RawImage? _image;
            private void Start() => _image = GetComponent<RawImage>();
            private void Update()
            {
                if (_image == null) return;
                var c = _image.color;
                c.a = 0.6f + 0.35f * Mathf.Sin(Time.unscaledTime * 3);
                _image.color = c;
                transform.localScale = Vector3.one * (1 + 0.06f * Mathf.Sin(Time.unscaledTime * 3));
            }
        }

        private void ShowCircuit(int index)
        {
            var T = UiTheme.Current;
            for (int i = _detail.childCount - 1; i >= 0; i--) Destroy(_detail.GetChild(i).gameObject);
            var profile = _state.Profile!;
            var circuits = _state.Circuits;
            if (circuits.All.Count == 0) return;
            var circuit = circuits.All[index];
            bool cleared = circuits.IsCleared(index, profile);

            Ui.Text(Ui.Box(_detail, 30, 20, 940, 46), circuit.Name, 30, T.Text, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            Ui.Text(Ui.Box(_detail, 30, 66, 940, 28), circuit.Blurb, 16, T.TextMuted, TextAnchor.MiddleLeft);
            var prize = Ui.Text(Ui.Box(_detail, 30, 96, 940, 28),
                cleared ? $"<color={T.GoldHex}>Cleared.</color> Replay any match for DP." : $"Clear all {circuit.Matches.Count} matches: <color={T.GoldHex}>+{circuit.Prize:N0} DP prize</color>"
                + (index + 1 < circuits.All.Count ? $" and {circuits.All[index + 1].Name} opens." : "."), 16, T.Text, TextAnchor.MiddleLeft);
            prize.supportRichText = true;
            if (_state.ListFor(index) is Banlist list)
                Ui.Text(Ui.Box(_detail, 30, 118, 940, 24), $"Ban list: {list.Name}. Your deck must be legal under it.", 14,
                    T.Warn, TextAnchor.MiddleLeft);

            // One primary: the first match not won yet gets the red Duel.
            int nextUp = -1;
            for (int k = 0; k < circuit.Matches.Count && nextUp < 0; k++) if (!profile.HasWon(index, k)) nextUp = k;
            for (int m = 0; m < circuit.Matches.Count; m++)
            {
                var match = circuit.Matches[m];
                var c = match.Character;
                bool won = profile.HasWon(index, m);
                var row = Ui.Box(_detail, 30, 142 + m * 128, 940, 116);
                Ui.Image(row, won ? T.RowWon : T.RowA);
                // Their portrait on the left of the row (text starts after it).
                float tx = Portraits.Place(Ui.Box(row, 6, 4, 110, 108), c.Id) ? 124 : 20;
                Ui.Text(Ui.Box(row, tx, 10, 600 - tx + 20, 34), $"Match {m + 1}: {c.DisplayName}", 21, T.Text, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
                Ui.Text(Ui.Box(row, tx, 44, 600 - tx + 20, 26), $"Deck: {_state.DeckTitle(match.Deck)} · {c.StyleSummary}", 14, T.TextFaint, TextAnchor.MiddleLeft);
                var intro = c.IntroFor(circuit.Matches.Take(m).Any(earlier => earlier.Character.Id == c.Id));
                if (intro.Length > 0)
                    Ui.Text(Ui.Box(row, tx, 72, 600 - tx + 20, 36), $"“{intro}”", 14, T.Intro, TextAnchor.UpperLeft).fontStyle = FontStyle.Italic;
                // Same numbers as the results screen: the first win carries the bonus, rematches pay the base.
                var reward = won ? $"Won ×{profile.MatchWins[Profile.MatchKey(index, m)]}\nRematch: +{circuits.WinDp(index)} DP"
                                 : $"First win: +{circuits.WinDp(index) + Circuits.FirstWinBonus} DP";
                var r = Ui.Text(Ui.Box(row, 610, 0, 160, 116), reward, 16, won ? T.Good : T.Gold);
                int circuitIndex = index, matchIndex = m;
                Ui.BoxButton(row, 780, 10, 140, 54, "Duel", () => TryDuel(circuitIndex, matchIndex), 20).targetGraphic.color = m == nextUp ? T.Primary : T.Button;
                var scouted = new List<(string, string)> { (c.Name, match.Deck) };
                Ui.BoxButton(row, 780, 70, 140, 38, "Scout", () => ScoutClicked?.Invoke(scouted), 16).targetGraphic.color = T.Button;
            }

            DrawYourDeck(142 + circuit.Matches.Count * 128 + 20, index);
        }

        /// <summary>Your campaign deck: pick, edit, new, and whether it can duel here (deck rules, the ban list of
        /// <paramref name="rulesCircuit"/>, cards you own).</summary>
        private void DrawYourDeck(float top, int rulesCircuit) => DrawYourDeck(top, DeckProblems(rulesCircuit));

        /// <summary>For checks: the last YOUR DECK panel's verdict ("Ready to duel.", or its first problems).</summary>
        public string YourDeckLine { get; private set; } = "";

        /// <summary>The same with the deck's problems found (under a Library cup's rules).</summary>
        private void DrawYourDeck(float top, List<DeckProblem> problems)
        {
            var T = UiTheme.Current;
            var profile = _state.Profile!;
            Ui.Text(Ui.Box(_detail, 30, top, 400, 26), "YOUR DECK", 15, T.Caption, TextAnchor.MiddleLeft);
            Ui.BoxButton(_detail, 30, top + 32, 44, 44, "<", () => CycleDeck(-1), 20);
            var label = Ui.Text(Ui.Box(_detail, 80, top + 32, 380, 44), profile.Deck.Length > 0 ? CampaignState.SlugTitle(profile.Deck) : "(no deck)", 22, T.Text);
            label.fontStyle = FontStyle.Bold;
            Ui.BoxButton(_detail, 466, top + 32, 44, 44, ">", () => CycleDeck(+1), 20);
            Ui.BoxButton(_detail, 540, top + 32, 200, 44, "Edit deck", () => EditDeck?.Invoke(profile.Deck), 18);
            Ui.BoxButton(_detail, 760, top + 32, 200, 44, "New deck", () => EditDeck?.Invoke(null), 18);
            YourDeckLine = problems.Count == 0 ? "Ready to duel." : string.Join("  ·  ", problems.Take(3).Select(p => p.Message));
            var status = Ui.Text(Ui.Box(_detail, 30, top + 86, 940, 90),
                problems.Count == 0
                    ? $"<color={T.GoodHex}>Ready to duel.</color> Every card in it is one you own."
                    : $"<color={T.BadHex}>Not ready ({problems.Count} problem{(problems.Count == 1 ? "" : "s")}):</color> "
                      + string.Join("  ·  ", problems.Take(3).Select(p => p.Message)) + (problems.Count > 3 ? "  ..." : ""),
                15, T.Text, TextAnchor.UpperLeft);
            status.supportRichText = true;
        }

        /// <summary>For automated play: ‹ › under YOUR DECK until it's <paramref name="deck"/>; the deck shown after.</summary>
        public string DebugPickDeck(string deck)
        {
            Open();
            for (int n = 0; n < _decks.Count && _state.Profile!.Deck != deck; n++) CycleDeck(1);
            return _state.Profile!.Deck;
        }

        private void CycleDeck(int delta)
        {
            var profile = _state.Profile!;
            if (_decks.Count == 0) return;
            var i = Math.Max(0, _decks.IndexOf(profile.Deck));
            profile.Deck = _decks[(i + delta + _decks.Count) % _decks.Count];
            _state.Save();
            ShowSelected();
        }

        private void ShowSelected()
        {
            if (_alleyView) ShowAlleyTable(_selectedAlley);
            else if (_libraryView && _selectedLibrary == UnlistedShelf) ShowUnlisted();
            else if (_libraryView && _selectedLibrary == LadderShelf) ShowLadder();
            else if (_libraryView) ShowCup(_selectedLibrary);
            else if (_selectedCup >= 0) ShowTagCup(_selectedCup);
            else ShowCircuit(_selected);
        }

        // ---- Tag Cups --------------------------------------------------------------------------------

        private int _selectedCup = -1, _partnerIndex;
        /// <summary>A new campaign has just started (after the starter deck), and New Game+ began: story moments.</summary>
        public event Action? CampaignStarted, NewGamePlusStarted;

        /// <summary>A tag match: the cup, the match, and your partner's character id.</summary>
        public event Action<int, int, string>? TagDuel;

        private void ShowTagCup(int index)
        {
            var T = UiTheme.Current;
            for (int i = _detail.childCount - 1; i >= 0; i--) Destroy(_detail.GetChild(i).gameObject);
            var profile = _state.Profile!;
            var circuits = _state.Circuits;
            var cup = circuits.TagCups[index];
            bool cleared = circuits.IsTagCupCleared(index, profile);
            Ui.Text(Ui.Box(_detail, 30, 20, 940, 46), cup.Name, 30, T.GoldSoft, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            Ui.Text(Ui.Box(_detail, 30, 66, 940, 28), cup.Blurb, 16, T.TextMuted, TextAnchor.MiddleLeft);
            var prize = Ui.Text(Ui.Box(_detail, 30, 94, 940, 28),
                cleared ? $"<color={T.GoldHex}>Cleared.</color> Replay any match for DP." : $"You and a partner against two. Clear all {cup.Matches.Count}: <color={T.GoldHex}>+{cup.Prize:N0} DP prize</color>",
                16, T.Text, TextAnchor.MiddleLeft);
            prize.supportRichText = true;

            // Your partner (‹ › to pick; remembered): anyone you've beaten, or Kit with one of your decks. The deck they
            // bring is a choice too once the bond allows it.
            var partners = _state.TagPartnerChoices();
            Ui.Text(Ui.Box(_detail, 30, 128, 200, 44), "YOUR PARTNER", 15, T.Caption, TextAnchor.MiddleLeft);
            if (partners.Count == 0)
                Ui.Text(Ui.Box(_detail, 230, 128, 740, 44), "Beat a duelist in the circuits first: they can partner you here.", 16, T.Text, TextAnchor.MiddleLeft);
            else
            {
                int remembered = partners.FindIndex(p => p.Id == profile.TagPartner);
                _partnerIndex = remembered >= 0 ? remembered : Mathf.Clamp(_partnerIndex, 0, partners.Count - 1);
                var partner = partners[_partnerIndex];
                void PickPartner(int delta)
                {
                    _partnerIndex = (_partnerIndex + delta + partners.Count) % partners.Count;
                    profile.TagPartner = partners[_partnerIndex].Id;
                    _state.Save();
                    ShowTagCup(index);
                }
                Ui.BoxButton(_detail, 230, 128, 44, 44, "<", () => PickPartner(-1), 20);
                Portraits.Place(Ui.Box(_detail, 900, 100, 70, 80), partner.Id);
                Ui.Text(Ui.Box(_detail, 280, 128, 560, 44), $"<b>{partner.DisplayName}</b>  <color={T.TextDimHex}>({_partnerIndex + 1} of {partners.Count})</color>", 18, T.Text)
                    .supportRichText = true;
                Ui.BoxButton(_detail, 846, 128, 44, 44, ">", () => PickPartner(1), 20);

                var decks = _state.PartnerDeckChoices(partner);
                var deck = _state.PartnerDeck(partner);
                Ui.Text(Ui.Box(_detail, 30, 176, 200, 40), "THEIR DECK", 15, T.Caption, TextAnchor.MiddleLeft);
                if (decks.Count > 1)
                {
                    void PickDeck(int delta)
                    {
                        int at = Math.Max(0, decks.IndexOf(deck));
                        profile.PartnerDecks[partner.Id] = decks[(at + delta + decks.Count) % decks.Count];
                        _state.Save();
                        ShowTagCup(index);
                    }
                    Ui.BoxButton(_detail, 230, 176, 40, 40, "<", () => PickDeck(-1), 18);
                    Ui.Text(Ui.Box(_detail, 274, 176, 300, 40), _state.DeckTitle(deck), 17, T.Text);
                    Ui.BoxButton(_detail, 578, 176, 40, 40, ">", () => PickDeck(1), 18);
                }
                else Ui.Text(Ui.Box(_detail, 230, 176, 388, 40), deck.Length > 0 ? _state.DeckTitle(deck) : "(no deck)", 17, T.Text, TextAnchor.MiddleLeft);
                // Bond 3's one trade, while it's on offer: a Trade button beside the bond.
                bool trade = !TagPartners.IsShopRegular(partner) && _state.Unlocks.CanTrade(profile, partner.Id);
                Ui.Text(Ui.Box(_detail, 630, 176, trade ? 230 : 340, 40), BondText(TagPartners.Bond(partner, profile)), 13, T.TextQuiet, TextAnchor.MiddleLeft);
                if (trade) Ui.BoxButton(_detail, 866, 178, 104, 36, "Trade", () => ShowTrade(partner), 15);
                // Their own deck is their exhibition deck: it plays as built under any list. Your edit of it answers to the
                // list like your decks do, using your cards plus theirs.
                if (!TagPartners.IsShopRegular(partner) && deck.Length > 0)
                {
                    bool edited = _state.PartnerEditOf(deck) == partner.Id;
                    Ui.Text(Ui.Box(_detail, 30, 222, 560, 36), edited
                        ? $"<color={T.WarnHex}>Your edit:</color> follows the ban list, like your decks."
                        : $"<color={T.GoodHex}>Their own deck:</color> plays as built, whatever the list.", 15, T.Text, TextAnchor.MiddleLeft).supportRichText = true;
                    int after = cup.After;
                    Ui.BoxButton(_detail, 600, 222, 170, 36, "Edit deck", () =>
                    {
                        var name = _state.EditPartnerDeck(partner);
                        if (name == null) { _status.text = $"<color={T.BadHex}>{partner.Name}'s deck can't be edited.</color>"; return; }
                        EditPartnerDeck?.Invoke(name, after);
                    }, 15);
                    if (edited)
                        Ui.BoxButton(_detail, 780, 222, 190, 36, "Their own deck", () => { _state.UsePartnersOwnDeck(partner); ShowTagCup(index); }, 15);
                }
            }

            // One primary: the first match not won yet gets the red Duel.
            int nextUp = -1;
            for (int k = 0; k < cup.Matches.Count && nextUp < 0; k++) if (!profile.MatchWins.ContainsKey(Circuits.TagKey(index, k))) nextUp = k;
            for (int m = 0; m < cup.Matches.Count; m++)
            {
                var (lead, second) = cup.Matches[m];
                bool won = profile.MatchWins.ContainsKey(Circuits.TagKey(index, m));
                var row = Ui.Box(_detail, 30, 270 + m * 120, 940, 108);
                Ui.Image(row, won ? T.RowWon : T.RowA);
                bool faces = Portraits.Place(Ui.Box(row, 4, 4, 92, 100), lead.Character.Id) | Portraits.Place(Ui.Box(row, 70, 4, 92, 100), second.Character.Id);
                float tx = faces ? 170 : 20;
                Ui.Text(Ui.Box(row, tx, 10, 620 - tx, 34), $"Match {m + 1}: {lead.Character.Name} & {second.Character.Name}", 21, T.Text, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
                Ui.Text(Ui.Box(row, tx, 44, 620 - tx, 26), $"Decks: {_state.DeckTitle(lead.Deck)} + {_state.DeckTitle(second.Deck)}", 14, T.TextFaint, TextAnchor.MiddleLeft);
                if (lead.Character.Intro.Length > 0)
                    Ui.Text(Ui.Box(row, tx, 70, 620 - tx, 30), $"“{lead.Character.Intro}”", 14, T.Intro, TextAnchor.UpperLeft).fontStyle = FontStyle.Italic;
                var reward = won ? $"Won ×{profile.MatchWins[Circuits.TagKey(index, m)]}\nRematch: +{circuits.WinDp(cup.After)} DP" : $"First win: +{circuits.WinDp(cup.After) + Circuits.FirstWinBonus} DP";
                Ui.Text(Ui.Box(row, 610, 0, 160, 108), reward, 16, won ? T.Good : T.Gold);
                int cupIndex = index, matchIndex = m;
                Ui.BoxButton(row, 780, 8, 140, 52, "Duel", () => TryTagDuel(cupIndex, matchIndex), 20).targetGraphic.color = m == nextUp ? T.Primary : T.Button;
                var scoutedPair = new List<(string, string)> { (lead.Character.Name, lead.Deck), (second.Character.Name, second.Deck) };
                Ui.BoxButton(row, 780, 64, 140, 38, "Scout", () => ScoutClicked?.Invoke(scoutedPair), 16).targetGraphic.color = T.Button;
            }
            DrawYourDeck(270 + cup.Matches.Count * 120 + 14, cup.After);
        }

        private void TryTagDuel(int cup, int match)
        {
            var T = UiTheme.Current;
            var partners = _state.TagPartnerChoices();
            if (partners.Count == 0) { _status.text = $"<color={T.BadHex}>You need a partner:</color> beat a duelist in the circuits first."; return; }
            var picked = partners[Mathf.Clamp(_partnerIndex, 0, partners.Count - 1)];
            if (_state.PartnerDeck(picked).Length == 0) { _status.text = $"<color={T.BadHex}>{picked.Name} has no deck to bring:</color> build a campaign deck for them first."; return; }
            // Nobody partners you against themselves.
            var (lead, second) = _state.Circuits.TagCups[cup].Matches[match];
            if (picked.Id == lead.Character.Id || picked.Id == second.Character.Id)
            {
                _status.text = $"<color={T.BadHex}>{picked.Name} is on the other team in this match:</color> pick another partner.";
                return;
            }
            var problems = DeckProblems(_state.Circuits.TagCups[cup].After);
            if (problems.Count > 0)
            {
                _status.text = $"<color={T.BadHex}>Your deck isn't ready:</color> {problems[0].Message} Use Edit deck to fix it.";
                return;
            }
            // A deck of yours lent to your partner answers to the list as yours does (era-limited play).
            var lent = _state.PartnerDeck(picked);
            if (_state.ListFor(_state.Circuits.TagCups[cup].After) != null && _library.IsCampaign(lent)
                && _state.Problems(_library.Load(lent), _state.Circuits.TagCups[cup].After) is { Count: > 0 } lentProblems)
            {
                _status.text = $"<color={T.BadHex}>{picked.Name}'s deck ({_state.DeckTitle(lent)}) isn't ready:</color> {lentProblems[0].Message} Edit that deck to fix it.";
                return;
            }
            Close();
            TagDuel?.Invoke(cup, match, partners[Mathf.Clamp(_partnerIndex, 0, partners.Count - 1)].Id);
        }

        // ---- The Format Library -----------------------------------------------------------------------

        /// <summary>The Library's shelf on the left (instead of the map), and the cup shown on the right.</summary>
        private bool _libraryView;
        private int _selectedLibrary = -1;
        private GameObject _districts = null!;
        private Button _circuitsTab = null!, _libraryTab = null!;
        private Text _mapTitle = null!;

        /// <summary>Play a Library cup match: (cup, match). The screen has closed itself.</summary>
        public event Action<int, int>? CupDuel;
        /// <summary>A Library cup's Card Trader button: the trader under that cup's pool.</summary>
        public event Action<int>? CupTrader;
        /// <summary>An "Every Era" round's Duel button (the round's index).</summary>
        public event Action<int>? RoundDuel;
        /// <summary>Story events the screen raises (the Library shown, a cup's panel opened); scenes already seen don't play.</summary>
        public event Action<string[]>? StoryEvents;

        /// <summary>The Library cup shown on the right, or -1 (the editor follows its rules then).</summary>
        public int LibraryCup => _libraryView ? _selectedLibrary : -1;

        private const string CircuitsTitle = "THE CIRCUIT MAP  ·  climb from the local shop to the Gauntlet";
        /// <summary>Remembers the Library was shown (its tab's "new" mark).</summary>
        private const string LibrarySeen = "district:library";
        private const string LibraryHelp =
            "Each cup plays by its year's list and card pool, whatever your campaign's rules. Win one with a deck you built, no loaner and no Ban Passes, for its stamp.";

        /// <summary>The "Circuits | Library" switch over the map (the header has no room left); shown once the Library opens.</summary>
        private void BuildDistricts()
        {
            var box = Ui.Box(_main, 600, 100, 240, 36);
            _districts = box.gameObject;
            _circuitsTab = Ui.BoxButton(box, 0, 0, 104, 36, "Circuits", () => ShowDistrict(false), 16);
            _libraryTab = Ui.BoxButton(box, 110, 0, 130, 36, "Library", () => ShowDistrict(true), 16);
            _libraryTab.GetComponentInChildren<Text>().supportRichText = true;
            _alleyTab = Ui.BoxButton(box, 250, 0, 140, 36, "Back Alley", () => ShowDistrictView(District.Alley), 16);
            _alleyTab.GetComponentInChildren<Text>().supportRichText = true;
            _alleyTab.gameObject.SetActive(false);
            _districts.SetActive(false);
        }

        /// <summary>The switch lit for the district shown, and the map's title line.</summary>
        private void ShowDistricts(Profile profile)
        {
            var T = UiTheme.Current;
            LayOutDistricts(profile);
            foreach (var (tab, on) in new[] { (_circuitsTab, !_libraryView && !_alleyView), (_libraryTab, _libraryView), (_alleyTab, _alleyView) })
            {
                tab.targetGraphic.color = on ? T.TabOn : T.ButtonOff;
                var label = tab.GetComponentInChildren<Text>();
                label.color = on ? T.Gold : T.TextDim;
                label.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
            }
            _libraryTab.GetComponentInChildren<Text>().text = profile.Seen.Contains(LibrarySeen) ? "Library" : $"Library <color={T.GoodHex}>(new)</color>";
            var library = _state.Library;
            int stamps = library.StampCount(profile);
            _mapTitle.text = _alleyView ? AlleyTitle(profile) : _libraryView
                ? $"THE FORMAT LIBRARY  ·  {library.ClearedCount(profile)}/{library.Cups.Count} cups cleared · {stamps} stamp{(stamps == 1 ? "" : "s")}"
                : CircuitsTitle;
        }

        private void ShowDistrict(bool library) => ShowDistrictView(library ? District.Library : District.Circuits);

        /// <summary>Circuits or the Library (main street), after the alley's district has been left.</summary>
        private void ShowMainStreetDistrict(bool library)
        {
            var profile = _state.Profile;
            if (profile == null || (library && (!profile.LibraryOpen || _state.Library.Cups.Count == 0)) || library == _libraryView) return;
            _libraryView = library;
            if (library && _selectedLibrary < 0) _selectedLibrary = FirstCupToPlay();
            if (library && profile.Seen.Add(LibrarySeen)) _state.Save();
            _status.text = library ? LibraryHelp : CircuitsHelp;
            Refresh();
            ScrollToSelected();
            // The Library's scene the first time, then the cup it opens on.
            // (Scenes show once: a save that cleared every cup before the final existed meets Ilse here.)
            if (library) StoryEvents?.Invoke(new[] { "library" }
                .Concat(_state.Library.FinalOpen(_state.Profile!) ? new[] { "final" } : new string[0])
                .Concat(_selectedLibrary < _state.Library.Cups.Count ? new[] { "open:" + _state.Library.Cups[_selectedLibrary].Name }
                    : _state.Library.FinalOpen(_state.Profile!) ? new[] { "open:" + _state.Library.Final.Name } : new string[0]).ToArray());
        }

        /// <summary>The first cup not cleared yet, in shelf order (the first one when all are).</summary>
        private int FirstCupToPlay()
        {
            // Once the final is open, it's what's next until it's won.
            if (_state.Library.FinalOpen(_state.Profile!) && !_state.Library.FinalCleared(_state.Profile!)) return _state.Library.Cups.Count;
            var order = ShelfOrder();
            var open = order.Where(c => !_state.Library.IsCleared(c, _state.Profile!)).ToList();
            return open.Count > 0 ? open[0] : order.FirstOrDefault();
        }

        /// <summary>The cups by year, oldest first.</summary>
        private List<int> ShelfOrder() =>
            Enumerable.Range(0, _state.Library.Cups.Count).OrderBy(c => _state.Library.Cups[c].Era).ThenBy(c => c).ToList();

        /// <summary>A cup's boss: its last match that isn't the Champion's table.</summary>
        private static CircuitMatch? Boss(Circuit cup) => cup.Matches.LastOrDefault(m => !m.Champion) ?? cup.Matches.LastOrDefault();

        // Sized so the final's plaque and four rows of binders fit the shelf without scrolling.
        private const float BinderWidth = 220, BinderHeight = 160, ShelfStep = 186, PlaqueStep = 116;

        /// <summary>
        /// The Library as a bookcase: one binder per cup, shelved by year three to a shelf, each with its year, its boss, how
        /// many matches you've won, ✓ once cleared and ★ once stamped. Returns the shelf's height.
        /// </summary>
        private float BuildShelf(Profile profile)
        {
            var T = UiTheme.Current;
            var library = _state.Library;
            var order = ShelfOrder();
            // The Unlisted binder after the cups, once it has a page (CampaignScreen.Alley).
            bool unlisted = profile.UnlistedPages.Count > 0;
            int rows = (order.Count + (unlisted ? 1 : 0) + 2) / 3;
            // The Champion's Ladder's plaque under the final's, once the final is won (CampaignScreen.Ladder): the binders move down.
            float shelfTop = ShelfTop(profile);
            float height = Math.Max(_scroll.viewport.rect.height, shelfTop - 12 + rows * ShelfStep + 24);
            Ui.Image(Ui.Box(_circuitList, 0, 0, MapWidth, height), Ui.Hex("#1A140E")).raycastTarget = false;
            for (int r = 0; r < rows; r++)
            {
                // The plank under each shelf.
                float plank = shelfTop + r * ShelfStep + BinderHeight + 2;
                Ui.Image(Ui.Box(_circuitList, 14, plank, MapWidth - 28, 14), Ui.Hex("#5A3F22")).raycastTarget = false;
                Ui.Image(Ui.Box(_circuitList, 14, plank + 14, MapWidth - 28, 4), Ui.Hex("#2E2011")).raycastTarget = false;
            }
            FinalPlaque(profile);
            LadderPlaque(profile);
            for (int k = 0; k < order.Count; k++)
            {
                int index = order[k];
                var cup = library.Cups[index];
                float x = 30 + (k % 3) * 250, y = shelfTop + (k / 3) * ShelfStep;
                bool cleared = library.IsCleared(index, profile), stamped = library.HasStamp(index, profile), selected = index == _selectedLibrary;
                // Each year its own cover color, around the wheel from 2004.
                var cover = Color.HSVToRGB(Mathf.Repeat((cup.Era - 2004) * 0.083f, 1), 0.45f, 0.42f);
                var accent = Color.HSVToRGB(Mathf.Repeat((cup.Era - 2004) * 0.083f, 1), 0.5f, 0.95f);
                // theme: kept, the white selection frame (no frame role is white)
                if (selected) Ui.Image(Ui.Box(_circuitList, x - 4, y - 4, BinderWidth + 8, BinderHeight + 8), Color.white).raycastTarget = false;
                var binder = Ui.Box(_circuitList, x, y, BinderWidth, BinderHeight);
                Ui.Image(binder, cover);
                Ui.Image(Ui.Box(binder, 0, 0, 14, BinderHeight), accent).raycastTarget = false;
                var year = Ui.Text(Ui.Box(binder, 24, 8, 120, 40), cup.Era.ToString(), 32, accent, TextAnchor.MiddleLeft);
                year.fontStyle = FontStyle.Bold;
                year.raycastTarget = false;
                // The marks to earn, dim until earned.
                var tick = Ui.Text(Ui.Box(binder, 148, 8, 32, 36), "✓", 26, cleared ? T.Good : T.Text.WithAlpha(0.18f));
                tick.raycastTarget = false;
                var star = Ui.Text(Ui.Box(binder, 182, 8, 32, 36), "★", 26, stamped ? T.Gold : T.Text.WithAlpha(0.18f));
                star.raycastTarget = false;
                var name = Ui.Text(Ui.Box(binder, 24, 50, 190, 46), cup.Name, 16, T.Text, TextAnchor.UpperLeft);
                name.fontStyle = FontStyle.Bold;
                name.raycastTarget = false;
                if (Boss(cup) is CircuitMatch boss) Portraits.Place(Ui.Box(binder, 142, 84, 72, 72), boss.Character.Id);
                int needed = cup.Matches.Count(m => !m.Champion);
                int won = Enumerable.Range(0, cup.Matches.Count).Count(m => !cup.Matches[m].Champion && library.HasWon(profile, index, m));
                // An engraved binder carries a plate with the deck that won there: gold once stamped, silver before.
                if (Cosmetics.Engraving(profile, library, index) is string plate)
                {
                    // A metal plate: a darker 2px rim, a light top edge, the deck's name stamped in.
                    var rim = Ui.Box(binder, 18, 116, 124, 40);
                    Ui.Image(rim, stamped ? Ui.Hex("#8A6A1E") : Ui.Hex("#7E8691")).raycastTarget = false;
                    var plateBox = Ui.Box(rim, 2, 2, 120, 36);
                    Ui.Image(plateBox, stamped ? Ui.Hex("#D9B24C") : Ui.Hex("#B9C0CB")).raycastTarget = false;
                    Ui.Image(Ui.Box(plateBox, 0, 0, 120, 2), stamped ? Ui.Hex("#F3DC8C") : Ui.Hex("#E6EAF0")).raycastTarget = false;
                    var engraved = Ui.Text(Ui.Box(plateBox, 4, 2, 112, 32), plate, 13, stamped ? Ui.Hex("#20180A") : Ui.Hex("#1E232B"));
                    engraved.fontStyle = FontStyle.Bold;
                    engraved.horizontalOverflow = HorizontalWrapMode.Wrap;
                    engraved.resizeTextForBestFit = true;
                    engraved.resizeTextMinSize = 11;
                    engraved.resizeTextMaxSize = 13;
                    engraved.raycastTarget = false;
                }
                else
                {
                    var progress = Ui.Text(Ui.Box(binder, 24, 124, 120, 28), cleared ? "CLEARED" : $"{won} / {needed} won", 15,
                        cleared ? T.Good : T.TextBody, TextAnchor.MiddleLeft);
                    progress.raycastTarget = false;
                }
                Ui.Clickable(binder, () => BinderClicked(index), style: HoverStyle.Tile);
            }
            if (unlisted) UnlistedBinder(profile, 30 + (order.Count % 3) * 250, shelfTop + (order.Count / 3) * ShelfStep);
            return height;
        }

        /// <summary>A binder on the shelf, clicked: its cup on the right, and its scene the first time.</summary>
        private void BinderClicked(int index)
        {
            var cup = _state.Library.Cups[index];
            Sounds.Play("click");
            _selectedLibrary = index;
            _status.text = $"<b>{cup.Name}</b>: your deck answers to {cup.Banlist} and cards out by {PoolMonth(cup.Pool)}, whatever your campaign's rules.";
            Refresh();
            StoryEvents?.Invoke(new[] { "open:" + cup.Name });
        }

        /// <summary>A pool's last month in words ("2005-09" is "September 2005").</summary>
        public static string PoolMonth(string pool) =>
            DateTime.TryParseExact(pool, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var month)
                ? month.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture) : pool;

        /// <summary>A cup's panel: its format, its matches (the Champion's table optional), its rewards and your deck under its rules.</summary>
        private void ShowCup(int index)
        {
            var T = UiTheme.Current;
            for (int i = _detail.childCount - 1; i >= 0; i--) Destroy(_detail.GetChild(i).gameObject);
            var profile = _state.Profile!;
            var library = _state.Library;
            if (index == library.Cups.Count && library.Rounds.Count > 0) { ShowFinal(); return; }
            if (index < 0 || index >= library.Cups.Count) return;
            var cup = library.Cups[index];
            var rules = _state.CupRules(index);
            bool cleared = library.IsCleared(index, profile), stamped = library.HasStamp(index, profile);
            bool champion = cup.Matches.Any(m => m.Champion);

            var cupTitle = Ui.Text(Ui.Box(_detail, 30, 20, 300, 46), cup.Name, 30, T.LibraryGold, TextAnchor.MiddleLeft);
            cupTitle.fontStyle = FontStyle.Bold;
            cupTitle.resizeTextForBestFit = true;
            cupTitle.resizeTextMinSize = 16;
            cupTitle.resizeTextMaxSize = 30;
            // The cup's Locals (BRACKETS-SPEC): its 8-player bracket once cleared, gold while yours runs here.
            int traderCup = index;
            var (localsLabel, localsOpen, localsRunning) = LocalsButtonState(index);
            var localsButton = Ui.BoxButton(_detail, 336, 20, 144, 44, localsLabel, () => LocalsButtonClicked(traderCup), 14);
            localsButton.interactable = localsOpen;
            var localsText = localsButton.GetComponentInChildren<Text>();
            localsText.resizeTextForBestFit = true;
            localsText.resizeTextMinSize = 10;
            localsText.resizeTextMaxSize = 14;
            if (localsRunning)
            {
                localsText.color = T.Gold;
                localsText.fontStyle = FontStyle.Bold;
            }
            // The cup's shop: its era mat and its binder's engraving once cleared, and the Library Trader (tokens for cards
            // out by the cup's pool month).
            var matId = Cosmetics.MatId(cup);
            bool matOn = profile.Mat == matId && profile.Mats.Contains(matId);
            string matLabel = !cleared ? "Clear to unlock" : matOn ? "Era mat ✓" : profile.Mats.Contains(matId) ? "Use era mat" : $"Era mat · {Cosmetics.MatPrice:N0} DP";
            var matButton = Ui.BoxButton(_detail, 486, 20, 150, 44, matLabel, () => { _status.text = MatClicked(traderCup); Refresh(); }, 14);
            matButton.interactable = cleared && !matOn;
            DoneOrAction(matButton, matOn);
            var deckTitle = profile.Deck.Length > 0 ? _state.DeckTitle(profile.Deck) : "";
            var engraving = Cosmetics.Engraving(profile, library, index);
            bool engravedHere = engraving != null && engraving == deckTitle;
            // Only a deck of yours that won here can be engraved: say so before the click, not after.
            bool canEngraveThis = Cosmetics.EngraveDecks(profile, library, index).Contains(profile.Deck);
            string engraveLabel = !cleared ? "Clear to unlock" : engravedHere ? "Engraved ✓"
                : !canEngraveThis ? "Win here to engrave"
                : engraving != null ? $"Re-engrave · {Cosmetics.EngravePrice:N0} DP" : $"Engrave · {Cosmetics.EngravePrice:N0} DP";
            var engraveButton = Ui.BoxButton(_detail, 642, 20, 170, 44, engraveLabel, () => { _status.text = EngraveClicked(traderCup); Refresh(); }, 14);
            engraveButton.interactable = cleared && !engravedHere;
            DoneOrAction(engraveButton, engravedHere);
            Ui.BoxButton(_detail, 818, 20, 142, 44, $"Card Trader ({profile.Tokens})", () => CupTrader?.Invoke(traderCup), 14).targetGraphic.color = T.Button;
            Ui.Text(Ui.Box(_detail, 30, 66, 940, 28), $"{cup.Banlist} list · cards out by {PoolMonth(cup.Pool)}"
                + (rules.List == null ? $"  <color={T.BadHex}>(that list isn't installed: no list applies)</color>" : ""), 16, T.TextMuted, TextAnchor.MiddleLeft)
                .supportRichText = true;
            Ui.Text(Ui.Box(_detail, 30, 94, 940, 26),
                cleared ? $"<color={T.GoodHex}>✓ Cleared.</color> Replay any match: +{Library.RepeatWinDp} DP a win."
                    : $"Win every match{(champion ? " (the Champion's table is optional)" : "")}: <color={T.GoldHex}>+{cup.Prize:N0} DP prize and +{Library.ClearTokens} Card Tokens</color>",
                16, T.Text, TextAnchor.MiddleLeft).supportRichText = true;
            Ui.Text(Ui.Box(_detail, 30, 120, 940, 24),
                stamped ? $"<color={T.GoldHex}>★ Format Master stamp earned.</color>"
                    : $"★ Format Master stamp: win every match{(champion ? ", the Champion's table too," : "")} with a deck you built, no loaner, no Ban Passes: +{Library.StampTokens} Card Token.",
                14, T.Warn, TextAnchor.MiddleLeft).supportRichText = true;

            // One primary: the first match not won yet gets the red Duel (the Champion's table once the rest are won).
            int nextUp = -1;
            for (int k = 0; k < cup.Matches.Count && nextUp < 0; k++) if (!cup.Matches[k].Champion && !library.HasWon(profile, index, k)) nextUp = k;
            for (int k = 0; k < cup.Matches.Count && nextUp < 0; k++) if (!library.HasWon(profile, index, k)) nextUp = k;
            // Five matches (a Champion's table) take shorter rows to leave room for your deck.
            float step = Math.Min(128, 560f / Math.Max(1, cup.Matches.Count)), rowHeight = step - 12;
            for (int m = 0; m < cup.Matches.Count; m++)
            {
                var match = cup.Matches[m];
                var c = match.Character;
                var key = library.Key(index, m);
                bool won = library.HasWon(profile, index, m);
                var row = Ui.Box(_detail, 30, 150 + m * step, 940, rowHeight);
                Ui.Image(row, won ? T.RowWon : match.Champion ? T.RowChampion : T.RowA);
                float tx = Portraits.Place(Ui.Box(row, 6, 4, 110, rowHeight - 8), c.Id) ? 124 : 20;
                // The panel's header says the table is optional; a long name and title shrink to fit rather than cut off.
                var title = match.Champion ? $"<color={T.VioletHex}>Champion's table:</color> {c.DisplayName}" : $"Match {m + 1}: {c.DisplayName}";
                var head = Ui.Text(Ui.Box(row, tx, 8, 600 - tx + 20, 34), title, 21, T.Text, TextAnchor.MiddleLeft);
                head.fontStyle = FontStyle.Bold;
                head.supportRichText = true;
                head.resizeTextForBestFit = true;
                head.resizeTextMinSize = 15;
                head.resizeTextMaxSize = 21;
                Ui.Text(Ui.Box(row, tx, 42, 600 - tx + 20, 26), $"Deck: {_state.DeckTitle(match.Deck)} · {c.StyleSummary}", 14, T.TextFaint, TextAnchor.MiddleLeft);
                var intro = c.IntroFor(cup.Matches.Take(m).Any(earlier => earlier.Character.Id == c.Id));
                if (intro.Length > 0 && rowHeight >= 104)
                    Ui.Text(Ui.Box(row, tx, 70, 600 - tx + 20, rowHeight - 74), $"“{intro}”", 14, T.Intro, TextAnchor.UpperLeft).fontStyle = FontStyle.Italic;
                // Same numbers as the results screen; ★ marks a win that counts toward the stamp.
                var reward = won ? $"Won ×{profile.LibraryWins[key]}{(profile.LibraryBuilt.Contains(key) ? $" <color={T.GoldHex}>★</color>" : "")}\nRematch: +{Library.RepeatWinDp} DP"
                                 : $"First win: +{Library.WinDp + Circuits.FirstWinBonus} DP";
                Ui.Text(Ui.Box(row, 610, 0, 160, rowHeight), reward, 16, won ? T.Good : T.Gold).supportRichText = true;
                int cupIndex = index, matchIndex = m;
                Ui.BoxButton(row, 780, 8, 140, rowHeight / 2 - 4, "Duel", () => TryCupDuel(cupIndex, matchIndex), 20).targetGraphic.color = m == nextUp ? T.Primary : T.Button;
                var scouted = new List<(string, string)> { (c.Name, match.Deck) };
                Ui.BoxButton(row, 780, rowHeight / 2 + 8, 140, rowHeight / 2 - 14, "Scout", () => ScoutClicked?.Invoke(scouted), 16).targetGraphic.color = T.Button;
            }

            DrawYourDeck(150 + cup.Matches.Count * step + 14, DeckProblems(rules));
        }

        /// <summary>
        /// A cup match's Duel: your deck has to be legal under the cup's rules, unless there's a loaner to borrow (the versus
        /// screen offers one then, and won't start with a deck of yours that isn't legal).
        /// </summary>
        private void TryCupDuel(int cup, int match)
        {
            var T = UiTheme.Current;
            var problems = DeckProblems(_state.CupRules(cup));
            if (problems.Count > 0 && _state.LoanerDecks().Count == 0)
            {
                _status.text = $"<color={T.BadHex}>Your deck isn't ready for this cup:</color> {problems[0].Message} Use Edit deck to fix it.";
                return;
            }
            Close();
            CupDuel?.Invoke(cup, match);
        }

        // ---- Library Locals (brackets) ---------------------------------------------------------------------------------

        /// <summary>
        /// A cup's Locals button: the entry dialog (or, while this cup's bracket runs, the bracket screen). The screen stays
        /// open under the dialog.
        /// </summary>
        public event Action<int>? LocalsClicked;

        /// <summary>
        /// The Locals button's label, whether it can be pressed, and whether this cup's bracket runs: "Clear to unlock",
        /// "Locals · 1,000" (pressed short of DP it says why), "Locals: Semi-final ▶" while yours runs here, or why not
        /// ("Goat Cup Locals running").
        /// </summary>
        private (string label, bool open, bool running) LocalsButtonState(int cup)
        {
            var profile = _state.Profile!;
            var library = _state.Library;
            if (Brackets.RunningAt(profile, library, cup))
            {
                var b = profile.Bracket!;
                return (b.Done ? "Locals: results ▶" : $"Locals: {b.RoundName(b.CurrentRound())} ▶", true, true);
            }
            var blocked = Brackets.EnterBlocked(profile, library, cup);
            bool shortOfDp = blocked != null && profile.Bracket == null && library.IsCleared(cup, profile)
                             && library.Cups[cup].Locals.Count == Brackets.LocalsPerCup && profile.Dp < Brackets.LocalsEntry;
            return blocked == null || shortOfDp ? ($"Locals · {Brackets.LocalsEntry:N0}", true, false) : (blocked!, false, false);
        }

        private void LocalsButtonClicked(int cup)
        {
            var T = UiTheme.Current;
            var profile = _state.Profile!;
            var blocked = Brackets.EnterBlocked(profile, _state.Library, cup);
            if (!Brackets.RunningAt(profile, _state.Library, cup) && blocked != null)
            {
                Sounds.Play("error");
                _status.text = $"<color={T.BadHex}>{blocked}</color> Locals: 8 players, single elimination; you're seed 1 as the cup's champion.";
                return;
            }
            LocalsClicked?.Invoke(cup);
        }

        /// <summary>For automated checks: a cup's Locals button label.</summary>
        public string DebugLocalsLabel(int cup) => LocalsButtonState(cup).label;

        /// <summary>For automated checks: a cup's Locals button, as clicked.</summary>
        public void DebugLocals(int cup) => LocalsButtonClicked(cup);

        /// <summary>For automated checks: opens the campaign on the Library, showing a cup.</summary>
        public void DebugShowLibrary(int cup)
        {
            Open();
            if (_state.Profile?.LibraryOpen != true) return;
            _libraryView = true;
            _selectedLibrary = cup;
            Refresh();
        }

        /// <summary>For automated play: the "Circuits | Library" switch, as clicked (the Library's scenes the first time).</summary>
        public void DebugShowDistrict(bool library)
        {
            Open();
            ShowDistrict(library);
        }

        /// <summary>For automated play: a cup's binder on the shelf, as clicked (its scene the first time).</summary>
        public void DebugClickBinder(int cup)
        {
            if (cup >= 0 && cup < _state.Library.Cups.Count) BinderClicked(cup);
        }

        /// <summary>For automated play: a cleared cup's Era mat button, as clicked; the status line after.</summary>
        public string DebugCupMat(int cup)
        {
            var note = MatClicked(cup);
            _status.text = note;
            Refresh();
            return note;
        }

        /// <summary>A cup panel button that's done (green with its ✓, not clickable) or an action (the panel's plain button).</summary>
        private static void DoneOrAction(Button button, bool done)
        {
            var T = UiTheme.Current;
            button.targetGraphic.color = done ? T.ButtonDone : T.Button;
            if (done) button.GetComponentInChildren<Text>().color = T.Good;
        }

        /// <summary>
        /// A cleared cup's Engrave button: engraves its binder with your deck (the one shown under the cup), if it has won a
        /// match there; again with another deck at the full price. What happened, for the status line.
        /// </summary>
        private string EngraveClicked(int cup)
        {
            var T = UiTheme.Current;
            var profile = _state.Profile!;
            var library = _state.Library;
            var title = profile.Deck.Length > 0 ? _state.DeckTitle(profile.Deck) : "";
            var why = Cosmetics.EngraveBlocked(profile, library, cup, profile.Deck, title);
            if (why != null || !Cosmetics.Engrave(profile, library, cup, profile.Deck, title))
            {
                Sounds.Play("error");
                return $"<color={T.BadHex}>Can't engrave the {library.Cups[cup].Name} binder: {why}.</color>";
            }
            Sounds.Play("token");
            _state.Save();
            Refresh();
            bool gold = library.HasStamp(cup, profile);
            return $"<color={T.GoodHex}>Engraved the {library.Cups[cup].Name} binder with {title} for {Cosmetics.EngravePrice:N0} DP.</color> "
                   + (gold ? "A gold plate: the cup is stamped." : "A silver plate; it turns gold with the cup's Format Master stamp.");
        }

        /// <summary>For automated checks: a cleared cup's Engrave button, as clicked; the status line after.</summary>
        public string DebugEngrave(int cup) => EngraveClicked(cup);

        // ---- The "Every Era" final ---------------------------------------------------------------------------------------

        /// <summary>
        /// The final's plaque over the shelf: locked with the cups still to clear, then its rounds as six pips, then won.
        /// Clicking it shows the final's panel.
        /// </summary>
        private void FinalPlaque(Profile profile)
        {
            var T = UiTheme.Current;
            var library = _state.Library;
            if (library.Rounds.Count == 0) return;
            bool open = library.FinalOpen(profile), won = library.FinalCleared(profile), selected = _selectedLibrary == library.Cups.Count;
            const float x = 30, y = 12, w = 720, h = 104;
            // theme: kept, the white selection frame (no frame role is white)
            if (selected) Ui.Image(Ui.Box(_circuitList, x - 4, y - 4, w + 8, h + 8), Color.white).raycastTarget = false;
            // A gold frame (dull while locked) around the plaque: the shelf's centerpiece.
            var frame = Ui.Box(_circuitList, x, y, w, h);
            Ui.Image(frame, open ? Ui.Hex("#D9B24C") : Ui.Hex("#5A5040"));
            var plaque = Ui.Box(frame, 2, 2, w - 4, h - 4);
            Ui.Image(plaque, open ? Ui.Hex("#3A2E14") : Ui.Hex("#2A2620"));
            var title = Ui.Text(Ui.Box(plaque, 20, 6, 320, 50), library.Final.Name.ToUpperInvariant(), 36, open ? T.GoldSoft : new Color(0.7f, 0.66f, 0.58f), TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            title.raycastTarget = false;
            if (!open)
            {
                var locked = Ui.Text(Ui.Box(plaque, 250, 18, 90, 24), "LOCKED", 14, T.TextDim, TextAnchor.MiddleLeft);
                locked.fontStyle = FontStyle.Bold;
                locked.raycastTarget = false;
            }
            int cleared = library.ClearedCount(profile), roundsWon = Enumerable.Range(0, library.Rounds.Count).Count(r => library.FinalWon(profile, r));
            var line = !open ? $"The final · clear every cup to open it ({cleared} of {library.Cups.Count})"
                : won ? $"<color={T.GoodHex}>✓ Won.</color> Every era, on its own terms"
                : $"The final · six eras, decks you built · {roundsWon} of {library.Rounds.Count} rounds won";
            // theme: kept, the plaque's warm line (Library material, no role)
            var sub = Ui.Text(Ui.Box(plaque, 20, 60, 580, 30), line, 15, new Color(0.88f, 0.86f, 0.8f), TextAnchor.MiddleLeft);
            sub.supportRichText = true;
            sub.raycastTarget = false;
            // One pip per round, labelled with its year: won (gold), next (gold outline), to come (dim).
            int next = library.FinalNext(profile);
            for (int r = 0; r < library.Rounds.Count; r++)
            {
                bool roundWon = library.FinalWon(profile, r), isNext = open && r == next;
                var pip = Ui.Box(plaque, 360 + r * 40, 16, 34, 32);
                Ui.Image(pip, roundWon || isNext ? Ui.Hex(roundWon ? "#D9B24C" : "#FFC940") : Ui.Hex("#3A3428")).raycastTarget = false;
                if (isNext) Ui.Image(Ui.Box(pip, 2, 2, 30, 28), Ui.Hex("#3A2E14")).raycastTarget = false;
                var era = library.Cups[library.Rounds[r].Cup].Era;
                var year = Ui.Text(pip, era >= 1000 ? "'" + (era % 100).ToString("00") : "?", 13,
                    roundWon ? Ui.Hex("#20180A") : isNext ? Ui.Hex("#FFC940") : new Color(1, 1, 1, open ? 0.6f : 0.35f));
                year.fontStyle = FontStyle.Bold;
                year.raycastTarget = false;
            }
            Portraits.Place(Ui.Box(plaque, w - 112, 0, 100, 100), "ilse");
            Ui.Clickable(plaque, () =>
            {
                Sounds.Play("click");
                _selectedLibrary = library.Cups.Count;
                Refresh();
                if (open) StoryEvents?.Invoke(new[] { "open:" + library.Final.Name });
            }, style: HoverStyle.Tile);
        }

        /// <summary>
        /// The final's panel: its rules, its prize, and its six rounds in order (won, next with the red Duel, or waiting for
        /// the one before), each a cup's boss under that cup's list and pool; your deck checked against the next round.
        /// </summary>
        private void ShowFinal()
        {
            var T = UiTheme.Current;
            var profile = _state.Profile!;
            var library = _state.Library;
            bool open = library.FinalOpen(profile), won = library.FinalCleared(profile);
            int next = library.FinalNext(profile);
            Ui.Text(Ui.Box(_detail, 30, 14, 700, 54), library.Final.Name, 40, T.GoldSoft, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            Portraits.Place(Ui.Box(_detail, 820, 4, 140, 140), "ilse");
            Ui.Text(Ui.Box(_detail, 30, 66, 940, 28), "Duchess Ilse's final: six eras in order, each under its own list and card pool.", 16,
                T.TextMuted, TextAnchor.MiddleLeft);
            Ui.Text(Ui.Box(_detail, 30, 94, 940, 26),
                won ? $"<color={T.GoodHex}>✓ Won.</color> Replay any round: +{Library.RepeatWinDp} DP a win."
                    : !open ? $"<color={T.TextDimHex}>Opens when every cup is cleared ({library.ClearedCount(profile)} of {library.Cups.Count}).</color>"
                    : $"Win all six: <color={T.GoldHex}>+{library.Final.Prize:N0} DP prize and +{Library.FinalTokens} Card Tokens</color>",
                16, T.Text, TextAnchor.MiddleLeft).supportRichText = true;
            Ui.Text(Ui.Box(_detail, 30, 120, 940, 24), "Your own decks only: no loaners, no Ban Passes. A lost round is played again; won rounds stay won.",
                14, T.Warn, TextAnchor.MiddleLeft).supportRichText = true;
            const float step = 96, rowHeight = 86;
            for (int r = 0; r < library.Rounds.Count; r++)
            {
                var round = library.Rounds[r];
                var c = round.Match.Character;
                var cup = library.Cups[round.Cup];
                bool roundWon = library.FinalWon(profile, r), playable = library.CanPlayRound(profile, r);
                var row = Ui.Box(_detail, 30, 150 + r * step, 940, rowHeight);
                Ui.Image(row, roundWon ? T.RowWon : playable ? T.RowPlayable : T.RowB);
                float tx = Portraits.Place(Ui.Box(row, 6, 4, 78, rowHeight - 8), c.Id) ? 96 : 20;
                var head = Ui.Text(Ui.Box(row, tx, 6, 600 - tx + 20, 32), $"Round {r + 1} · {cup.Era}: {c.DisplayName}", 20, T.Text, TextAnchor.MiddleLeft);
                head.fontStyle = FontStyle.Bold;
                head.resizeTextForBestFit = true;
                head.resizeTextMinSize = 14;
                head.resizeTextMaxSize = 20;
                var about = Ui.Text(Ui.Box(row, tx, 40, 600 - tx + 20, 24), $"{_state.DeckTitle(round.Match.Deck)} · {cup.Banlist} list · cards out by {PoolMonth(cup.Pool)}", 14,
                    T.TextFaint, TextAnchor.MiddleLeft);
                about.resizeTextForBestFit = true;
                about.resizeTextMinSize = 11;
                about.resizeTextMaxSize = 14;
                var key = library.FinalKey(r);
                // While the final is locked, the rows are its lineup only: no states, no Duel buttons.
                int roundIndex = r;
                if (open)
                {
                    var state = roundWon ? $"Won ×{profile.LibraryWins[key]}" : playable ? "Next" : $"Win round {r} first";
                    Ui.Text(Ui.Box(row, 610, 0, 160, rowHeight), state, 16, roundWon ? T.Good : playable ? T.Gold : T.TextDim).supportRichText = true;
                    var duel = Ui.BoxButton(row, 780, 6, 140, rowHeight / 2 - 4, "Duel", () => { Close(); RoundDuel?.Invoke(roundIndex); }, 18);
                    duel.targetGraphic.color = r == next && playable ? T.Primary : T.Button;
                    duel.interactable = playable;
                    if (!playable) duel.GetComponentInChildren<Text>().color = T.Text.WithAlpha(0.35f);
                }
                var scouted = new List<(string, string)> { (c.Name, round.Match.Deck) };
                Ui.BoxButton(row, 780, rowHeight / 2 + 6, 140, rowHeight / 2 - 10, "Scout", () => ScoutClicked?.Invoke(scouted), 15).targetGraphic.color = T.Button;
            }
            // Your deck against the next round (the first, once it's won), by the round's versus check: a deck you built
            // (no loaner, custom or not), legal there with no Ban Passes.
            int against = next >= 0 ? next : 0;
            DrawYourDeck(150 + library.Rounds.Count * step + 8, _state.RoundProblems(profile.Deck, against));
        }

        /// <summary>Shows the Library on a cup (or the final, at Cups.Count) the next time the screen opens.</summary>
        public void SelectLibrary(int index)
        {
            _libraryView = true;
            _selectedLibrary = index;
        }

        /// <summary>For automated checks: the final's plaque, as clicked (its panel, and its story when open).</summary>
        public void DebugClickFinalPlaque()
        {
            var library = _state.Library;
            _libraryView = true;
            _selectedLibrary = library.Cups.Count;
            Refresh();
            if (library.FinalOpen(_state.Profile!)) StoryEvents?.Invoke(new[] { "open:" + library.Final.Name });
        }

        /// <summary>For automated checks: an "Every Era" round's Duel button, as clicked (if it can be played).</summary>
        public void DebugRoundDuel(int round)
        {
            if (!_state.Library.CanPlayRound(_state.Profile!, round)) return;
            Close();
            RoundDuel?.Invoke(round);
        }

        /// <summary>For automated checks: a Library cup's Card Trader button, as clicked.</summary>
        public void DebugCupTrader(int cup) => CupTrader?.Invoke(cup);

        /// <summary>For automated play: a Library cup match's Duel button. Null when the duel starts, else what the screen said.</summary>
        public string? DebugTryCupDuel(int cup, int match)
        {
            _status.text = "";
            TryCupDuel(cup, match);
            return IsOpen ? _status.text : null;
        }

        /// <summary>For automated checks: scrolls the map (1 = top, 0 = bottom).</summary>
        public void DebugScrollMap(float position) => _scroll.verticalNormalizedPosition = position;

        /// <summary>For automated checks: shows a Tag Cup.</summary>
        public void DebugShowTagCup(int cup)
        {
            Open();
            _selectedCup = cup;
            Refresh();
        }

        /// <summary>For automated play: a Tag Cup match's Duel button. Null when the duel starts, else what the screen said.</summary>
        public string? DebugTryTagDuel(int cup, int match)
        {
            _status.text = "";
            TryTagDuel(cup, match);
            return IsOpen ? _status.text : null;
        }

        /// <summary>
        /// For automated play: on the shown Tag Cup, ‹ › until <paramref name="partnerId"/> is the partner, then ‹ › under
        /// THEIR DECK until it's <paramref name="deck"/> (when given and offered). The partner and deck shown after.
        /// </summary>
        public (string partner, string deck) DebugPickPartner(string partnerId, string? deck = null)
        {
            var partners = _state.TagPartnerChoices();
            int at = partners.FindIndex(p => p.Id == partnerId);
            if (at >= 0 && _state.Profile != null)
            {
                _partnerIndex = at;
                _state.Profile.TagPartner = partnerId;
                if (deck != null && _state.PartnerDeckChoices(partners[at]).Contains(deck)) _state.Profile.PartnerDecks[partnerId] = deck;
                _state.Save();
                if (_selectedCup >= 0) ShowTagCup(_selectedCup);
            }
            var shown = partners.ElementAtOrDefault(Mathf.Clamp(_partnerIndex, 0, Math.Max(0, partners.Count - 1)));
            return (shown?.Id ?? "", shown == null ? "" : _state.PartnerDeck(shown));
        }

        /// <summary>For automated play: the status line at the bottom.</summary>
        public string StatusText => _status.text;

        /// <summary>For automated play: the Edit deck button (the editor opens with the shown circuit's ban list).</summary>
        public void DebugEditDeck() => EditDeck?.Invoke(_state.Profile!.Deck);

        /// <summary>The circuit shown on the right (for a Tag Cup, the circuit whose rules it follows).</summary>
        public int Selected => _selectedCup >= 0 ? _state.Circuits.TagCups[_selectedCup].After : _selected;

        /// <summary>For automated checks: opens the campaign showing a circuit, even a locked one.</summary>
        public void DebugShowCircuit(int index)
        {
            Open();
            _selected = index;
            _selectedCup = -1; // as clicking the circuit on the map does
            ShowCircuit(index);
        }

        private List<DeckProblem> DeckProblems(int circuit) => DeckProblems(_state.Rules(circuit));

        private List<DeckProblem> DeckProblems(FormatRules rules) =>
            _library.Contains(_state.Profile!.Deck)
                ? _state.Problems(_library.Load(_state.Profile.Deck), rules)
                : new List<DeckProblem> { new DeckProblem { Message = "Build a deck first (New deck)." } };

        private void TryDuel(int circuit, int match)
        {
            var T = UiTheme.Current;
            var problems = DeckProblems(circuit);
            if (problems.Count > 0)
            {
                _status.text = $"<color={T.BadHex}>Your deck isn't ready:</color> {problems[0].Message} Use Edit deck to fix it.";
                return;
            }
            Close();
            Duel?.Invoke(circuit, match);
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>The partner bond line in plainer words: "beaten 1x · 2 more wins: pick their deck" reads
        /// "beaten once · 2 more wins to pick their deck".</summary>
        private static string BondText(string bond)
        {
            bond = System.Text.RegularExpressions.Regex.Replace(bond, @"^beaten (\d+)x",
                m => m.Groups[1].Value == "1" ? "beaten once" : $"beaten {m.Groups[1].Value} times");
            return bond.Replace(": pick their deck", " to pick their deck").Replace(": sharper play", " for sharper play");
        }
    }
}
