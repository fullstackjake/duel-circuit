using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;
using Ygo.Engine.Interactive;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Build and edit decks: one-box search with quick filters, Main / Extra / Side grids, ban list checks,
    /// opening-hand odds for marked starters, a background bot test, undo/redo, test hands, and .ydk / YDKE
    /// import-export. Decks are saved to mods/decks; built-in decks are read-only (saving makes your own copy).
    /// Click a search result to add it (right-click: Side Deck); click a deck card to remove it
    /// (right-click: move between Main/Extra and Side). Or drag: results onto a deck section, deck cards between
    /// sections, or a deck card back onto the search panel to remove it. Hover anything for details.
    /// </summary>
    public sealed partial class DeckEditor : MonoBehaviour
    {
        private const int ResultsPerPage = 25; // a 5 x 5 grid of cards
        private const int ResultColumns = 5;
        private static readonly Vector2 ResultCard = new Vector2(88, 128);
        private static readonly Vector2 DeckCard = new Vector2(62, 90);
        // Effect tags (Tag Force's "special feature" icons): what a card does, not what it is.
        private static readonly string[] ChipLabels = { "Hand trap", "Searcher", "Draw", "Sp. Summon", "Revive", "Removal", "S/T removal", "Negate", "Banish", "Board wipe" };
        private static readonly string[] ChipTokens = { "handtrap", "searcher", "draw", "specialsummon", "revive", "destroymonster", "destroyst", "negate", "banish", "wipe" };
        private static readonly string[] ChipIcons = { "hand", "search", "activate", "special", "summon", "removal", "removal", "negate", "flip", "removal" };
        private static readonly string[] ChipColors = { "#3F8FB0", "#4F8A5A", "#3F7FB0", "#7B5FC0", "#5F7FC0", "#B04A3F", "#A0506A", "#8A6A2F", "#6A7F9A", "#B0603F" };

        // Pickers: what a card is. Each cycles through its options (right-click goes back).
        private static readonly (string label, string token)[] KindOptions =
            { ("Any kind", ""), ("Monster", "monster"), ("Main monster", "monster -extra"), ("Extra Deck", "extra"), ("Spell", "spell"), ("Trap", "trap") };
        private static readonly (string label, string token)[] AttributeOptions =
            new[] { ("Attribute", "") }.Concat(DuelText.Attributes.Select(a => (a.name, "attr:" + a.name.ToLowerInvariant()))).ToArray();
        private static readonly (string label, string token)[] TypeOptions =
            new[] { ("Type", "") }.Concat(DuelText.Races.Select(r => (r.name, "type:" + r.name.ToLowerInvariant().Replace(" ", "").Replace("-", "")))).ToArray();
        private static readonly (string label, string token)[] LevelOptions =
            new[] { ("Level", "") }.Concat(Enumerable.Range(1, 12).Select(n => ($"Level {n}", $"lv{n}"))).ToArray();
        private static readonly string[] SortOptions = { "Best match", "Name", "Level", "ATK", "DEF", "Most owned" };
        private readonly int[] _picks = new int[5]; // kind, attribute, type, level, sort
        private readonly Text[] _pickLabels = new Text[5];
        private bool _ownedOnly;
        /// <summary>
        /// The catalog's scope: every card, owned only (campaign), favorites, cards seen in duels, recently viewed
        /// (docs/dev/CARD-SEARCH-SPEC-2026-11-07.md section 2).
        /// </summary>
        private int _scope;
        private const int ScopeAll = 0, ScopeOwned = 1, ScopeFavorites = 2, ScopeSeen = 3, ScopeViewed = 4;
        /// <summary>Where the box's plain words look (the switch at the end of the search box); back to Name + text each time the editor opens.</summary>
        private CardSearch.SearchMode _searchMode;
        private Text _modeLabel = null!;
        /// <summary>The search panel's controls, for the search check's overlap test.</summary>
        private readonly List<(string what, RectTransform rt)> _searchLayout = new List<(string, RectTransform)>();
        /// <summary>The opponents' cards from the last duels and the cards you looked at here (mods/decks/seen.txt).</summary>
        private SeenLog _seenLog = new SeenLog("");
        /// <summary>Seen in duels: -1 every kept duel, else one duel (an index into the log's duels, newest first).</summary>
        private int _seenDuel = -1;
        /// <summary>Where the close names start in <see cref="_results"/> (they come last, sorted apart), or -1.</summary>
        private int _closeNamesFrom = -1;
        /// <summary>The empty Seen in duels / Recently viewed scope's line over the grid.</summary>
        private Text _emptyNote = null!;
        /// <summary>The last status line a search wrote ("showing close names"), cleared once it no longer holds.</summary>
        private string _searchStatus = "";
        private readonly HashSet<uint> _favorites = new HashSet<uint>();
        private Archetypes _archetypes = Archetypes.Empty;
        private List<Deck>? _references;
        private Text _favoriteLabel = null!;
        private Button _starterButton = null!, _favoriteButton = null!;

        /// <summary>Mark starter and Favorite act on the picked card: dimmed until there is one.</summary>
        private void SetCardButtons(bool on)
        {
            var T = UiTheme.Current;
            foreach (var b in new[] { _starterButton, _favoriteButton })
            {
                b.interactable = on;
                b.GetComponentInChildren<Text>().color = on ? T.Text : T.Text.WithAlpha(0.4f);
            }
        }
        private GameObject _passButtons = null!;
        private CardInfo? _related;
        /// <summary>The active filter row: a 300 px window (<see cref="_activeContent"/> scrolls in it when the chips run long).</summary>
        private RectTransform _activeRow = null!, _activeContent = null!;
        private ScrollRect _activeScroll = null!;
        private GameObject _activeLeft = null!, _activeRight = null!;
        private int _activeCount;
        private GameObject _ownedToggle = null!;
        private Text _ownedLabel = null!;
        /// <summary>Where to get a card you don't own (the campaign's shop and trader); null outside the campaign.</summary>
        public Func<uint, string>? WhereToGet;
        /// <summary>Whether the campaign is era-limited (then cards not out yet in a list's format are problems too).</summary>
        public Func<bool>? EraLimited;
        /// <summary>
        /// A Library cup's format, when opened from one: its card pool and Ban Passes apply while its list is picked, whatever
        /// <see cref="EraLimited"/> says. Null otherwise.
        /// </summary>
        private FormatRules? _format;

        // Dependencies.
        private ICardDataProvider _cards = null!;
        private IScriptProvider _scripts = null!;
        private CardSearch _search = null!;
        private DeckLibrary _library = null!;
        private List<Banlist> _banlists = new List<Banlist>();
        private List<Personality> _roster = new List<Personality>();

        // State.
        private Deck _deck = new Deck { Name = "new deck" };
        private bool _dirty;
        private readonly Stack<Deck> _undo = new Stack<Deck>(), _redo = new Stack<Deck>();
        private CardInfo? _selected;
        private List<CardInfo> _results = new List<CardInfo>();
        private int _page;
        private int _banIndex;
        private string _query = "";
        private readonly HashSet<int> _chips = new HashSet<int>();
        private int _openIndex;
        private string? _confirm; // an action waiting for a second click ("discard unsaved changes?")
        private readonly DeckTester _tester = new DeckTester();
        private int _opponentIndex, _opponentDeckIndex;
        private bool _wasTesting;
        private List<uint>? _testHand;
        private bool _testHandSecond;
        private readonly System.Random _rng = new System.Random();

        // UI.
        private RectTransform _root = null!, _cardsLayer = null!, _resultsLayer = null!, _handOverlay = null!, _handCards = null!;
        private Text _status = null!, _problems = null!, _mainHeader = null!, _extraHeader = null!, _sideHeader = null!;
        private Text _resultCount = null!, _pageLabel = null!, _banLabel = null!, _openLabel = null!, _detail = null!;
        private Text _odds = null!, _testResult = null!, _opponentLabel = null!, _opponentDeckLabel = null!, _starterLabel = null!, _handLabel = null!;
        private CardDetail _cardDetail = null!;
        private RectTransform _charts = null!;
        private Text _artLabel = null!, _textLabel = null!;
        private RectTransform _versionPanel = null!;
        private GameObject[] _freeTools = Array.Empty<GameObject>(), _campaignTools = Array.Empty<GameObject>();
        private InputField _nameField = null!, _searchField = null!, _labelField = null!;
        private readonly List<Image> _chipImages = new List<Image>();

        // Drag and drop: where a card can be dropped, and the card image that follows the pointer.
        private enum Zone { None, Main, Extra, Side, Search }
        private readonly List<(Zone zone, RectTransform rect, Image image)> _zones = new List<(Zone, RectTransform, Image)>();
        private RectTransform _ghost = null!;
        private RawImage _ghostArt = null!;
        private Image _ghostFrame = null!;
        private Text _ghostName = null!;
        private static readonly Color ZoneIdle = new Color(1, 1, 1, 0.035f), ZoneHot = new Color(0.36f, 0.6f, 1f, 0.22f),
            RemoveHot = new Color(1f, 0.35f, 0.35f, 0.18f);

        /// <summary>Raised when the editor closes, so the duel screen can refresh its deck lists.</summary>
        public event Action? Closed;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        /// <summary>
        /// Campaign deck building: search shows only cards you own (with how many), and a deck can't use more
        /// copies than you have. Null for free deck building.
        /// </summary>
        public Collection? OwnedCards { get; private set; }

        /// <summary>Decks the Open list cycles through: your campaign decks in collection mode, every deck otherwise.</summary>
        private List<string> OpenableDecks => (OwnedCards != null ? _library.CampaignNames : _library.Names).ToList();

        private string DeckLabel(string name) =>
            name + (_library.IsCampaign(name) ? " · campaign" : _library.IsOwn(name) ? "" : " · built-in");

        public void Init(ICardDataProvider cards, IScriptProvider scripts, DeckLibrary library, List<Banlist> banlists, List<Personality> roster)
        {
            _cards = cards;
            _scripts = scripts;
            _library = library;
            // Rush / Speed Duel lists don't apply to these decks.
            _banlists = new List<Banlist> { Banlist.None };
            _banlists.AddRange(banlists.Where(b => !b.Name.Contains("Rush") && !b.Name.Contains("Speed")));
            _roster = roster;
            _search = new CardSearch(cards);
            _versions = new CardVersions(cards);
            LoadFavorites();
            _seenLog = SeenLog.Read(SeenPath);
            Build();
            _root.gameObject.SetActive(false);
        }

        public string BanlistName => _banlists[_banIndex].Name;

        /// <summary>
        /// The card text timeline (campaign/texts.txt): originals by entry, not name (Mind Master's two are two texts), dups
        /// shown as their canonical text, and where each original plays (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 5.4).
        /// </summary>
        public void SetTexts(CardTexts texts) => _versions = new CardVersions(_cards, texts);

        /// <summary>Opens the editor; <paramref name="banlist"/> selects a ban list (the campaign circuit's) over the deck's own.</summary>
        /// <summary>Campaign: how many Ban Passes a deck may use, and the cards that can never be passed.</summary>
        public Func<int>? PassAllowance;
        public ISet<uint>? NoPass;

        /// <summary>The ban list as it applies to this deck: in the campaign, its Ban Passes let some cards past it.</summary>
        private Banlist EffectiveList => OwnedCards != null && PassAllowance != null
            ? BanPasses.For(_deck, _banlists[_banIndex], Passes, NoPass ?? new HashSet<uint>(), _cards)
            : _banlists[_banIndex];

        /// <summary>The Ban Passes a deck may use: the cup's when its format applies, else the campaign's.</summary>
        private int Passes => Format != null ? Format.Passes : PassAllowance?.Invoke() ?? 0;

        /// <summary>The cup's format while its list is the one picked (cycling to another list leaves it).</summary>
        private FormatRules? Format => _format != null && (_format.List == null || _format.List.Name == _banlists[_banIndex].Name) ? _format : null;

        /// <summary>A campaign circuit's texts (set by the circuit's Open): what its tables play, in either mode.</summary>
        private TextRules? _circuitTexts;

        /// <summary>
        /// The ban list was changed by hand (the list arrows) since the editor opened: the note then reads the picked list.
        /// A deck's own "#banlist" picks a list on load, which isn't the circuit's say on texts.
        /// </summary>
        private bool _listPickedByHand;

        /// <summary>The note reads a campaign circuit's own texts ("At this circuit"): opened from a circuit, list not changed by hand.</summary>
        private bool AtCircuit => Format == null && _circuitTexts != null && OwnedCards != null && !_listPickedByHand;

        /// <summary>
        /// Where originals play as printed for the note and the default version (spec 5.1): a cup's or the alley's own texts
        /// while its list is picked; a campaign circuit's own texts unless you picked another list by hand (outside
        /// era-limited play the list shown is the deck's own "#banlist", which doesn't change what the circuit plays); else
        /// the picked list's (its format end; "No limits" plays as printed).
        /// </summary>
        private TextRules TableTexts =>
            Format != null ? Format.Texts
            : AtCircuit && _circuitTexts is TextRules circuit ? circuit
            : TextRules.ForList(_banlists[_banIndex], _banlists);

        /// <summary>What a deck slot holding <paramref name="code"/> plays here (no copies yet: phase 1 plays the slot's text where allowed).</summary>
        private uint PlayedHere(uint code) => PlayTexts.PlayCode(code, null, TableTexts, _versions.TextRows, _cards);

        /// <summary>The last month of cards a list allows: the cup's pool under its format, else the list's own format.</summary>
        private string PoolEnd(Banlist list) => Format != null ? Format.Pool : DeckRules.FormatEnd(list, _banlists);

        private bool CanPass(uint key) => OwnedCards != null && PassAllowance != null && _deck.Note("loaner").Length == 0
                                          && _banlists[_banIndex].Allowed(key, _cards) < 3 && NoPass?.Contains(key) != true;

        /// <summary>One more Ban Pass on the selected card (if a pass is free and it would allow another copy).</summary>
        private void AddPass()
        {
            if (_selected == null || PassAllowance == null) return;
            var key = Banlist.BaseCode(_selected.Code, _cards);
            var passes = BanPasses.Of(_deck);
            if (!CanPass(key)) { SetStatus(NoPass?.Contains(key) == true ? $"{_selected.Name} can't be passed: it wins on its own." : $"{_selected.Name} isn't limited by this ban list."); return; }
            if (passes.Count >= Passes) { SetStatus($"All {Passes} Ban Passes are in use. Remove one first."); return; }
            if (_banlists[_banIndex].Allowed(key, _cards) + passes.Count(c => c == key) >= Collection.MaxCopies) { SetStatus($"{_selected.Name} is already allowed at 3."); return; }
            Change(d => BanPasses.Set(d, passes.Append(key)));
            SetStatus($"Ban Pass on {_selected.Name}: one more copy allowed ({BanPasses.Of(_deck).Count} of {Passes} passes used).");
        }

        private void RemovePass()
        {
            if (_selected == null) return;
            var key = Banlist.BaseCode(_selected.Code, _cards);
            var passes = BanPasses.Of(_deck);
            int at = passes.IndexOf(key);
            if (at < 0) return;
            passes.RemoveAt(at);
            Change(d => BanPasses.Set(d, passes));
        }

        public void Open(string? deckName, Collection? owned = null, string? banlist = null) => Open(deckName, owned, banlist, null);

        /// <summary>Opens the editor under a Library cup's format: its list, and its pool and Ban Passes forced (see <see cref="_format"/>).</summary>
        public void Open(string? deckName, Collection owned, FormatRules format)
        {
            // A format's own list (the Alley's, No Limits') joins the editor's lists by name when it isn't one of them yet, so the
            // editor validates under it (a list the lflists folder doesn't carry).
            if (format.List != null && _banlists.All(b => b.Name != format.List.Name)) { _banlists.Add(format.List); _formatOnly.Add(format.List.Name); }
            Open(deckName, owned, format.List?.Name, format);
        }

        /// <summary>
        /// The lists a format brought in (the Alley's, No Limits'): they belong to that opening only, so any other opening drops
        /// them, and the editor never validates a menu or circuit deck under No Limits (review fix 2).
        /// </summary>
        private readonly HashSet<string> _formatOnly = new HashSet<string>();

        private void DropFormatLists(FormatRules? format)
        {
            if (_formatOnly.Count == 0) return;
            string? keep = format?.List?.Name;
            string? current = _banIndex >= 0 && _banIndex < _banlists.Count ? _banlists[_banIndex].Name : null;
            _banlists.RemoveAll(b => _formatOnly.Contains(b.Name) && b.Name != keep);
            _formatOnly.RemoveWhere(n => n != keep);
            int at = current == null ? -1 : _banlists.FindIndex(b => b.Name == current);
            _banIndex = at >= 0 ? at : 0;
        }

        /// <summary>
        /// Opens the editor for a campaign circuit: its list (none outside era-limited play), and the texts its tables play,
        /// which hold in both modes (the text note reads them while no dated list is picked).
        /// </summary>
        public void Open(string? deckName, Collection owned, string? banlist, TextRules circuitTexts) => Open(deckName, owned, banlist, null, circuitTexts);

        private void Open(string? deckName, Collection? owned, string? banlist, FormatRules? format, TextRules? circuitTexts = null)
        {
            DropFormatLists(format);
            OwnedCards = owned;
            _format = format;
            _circuitTexts = circuitTexts;
            _listPickedByHand = false;
            _recipes = null; // the collection or ban list may have changed
            if (_browser != null) CloseBrowser(); // a deck list left open last time
            CloseMore();
            // The search box's switch holds while the editor is open; the log may have a new duel since last time.
            _searchMode = CardSearch.SearchMode.NameAndText;
            _seenLog = SeenLog.Read(SeenPath);
            _seenDuel = -1;
            if (owned == null && _scope == ScopeOwned) _scope = ScopeAll;
            // Free play has no copies: a campaign look (Foil, Slabbed...) left picked would filter on nothing there.
            if (owned == null && _look != LookAny && _look != LookAltArt) _look = LookAny;
            foreach (var go in _freeTools) go.SetActive(owned == null);
            foreach (var go in _campaignTools) go.SetActive(owned != null);
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _library.Refresh();
            if (deckName != null && _library.Contains(deckName)) LoadDeck(deckName);
            else NewDeck();
            var list = banlist == null ? -1 : _banlists.FindIndex(b => b.Name == banlist);
            if (list >= 0)
            {
                _banIndex = list;
                RefreshDeck();
            }
            RunSearch();
            if (owned != null)
                SetStatus($"Collection mode: you can use the {owned.Total} cards you own ({owned.Distinct} different). Buy packs in the Card Shop for more.");
            OfferRecovery();
#if ENABLE_INPUT_SYSTEM
            ControllerSupport.Legend = "A  add / remove  ·  X  to / from Side  ·  Y  favorite  ·  hold A  drag  ·  B  back  ·  LB / RB  page  ·  "
                                       + $"{ControllerBindings.Label(PadAction.Terms)}  more filters  ·  D-pad  jump";
#endif
        }

        /// <summary>Controller Y: star / unstar the selected card.</summary>
        public void ControllerFavorite() => ToggleFavorite();

        // ------------------------------------------------------------------ Controller

        /// <summary>Controller B: close the More filters panel or the test hand if one is open, otherwise leave the editor.</summary>
        public void ControllerBack()
        {
            if (MoreOpen) CloseMore();
            else if (_handOverlay.gameObject.activeSelf) _handOverlay.gameObject.SetActive(false);
            else Confirmed("back", Close);
        }

        /// <summary>Controller LB / RB: previous / next page of search results (the More panel covers them while it's open).</summary>
        public void ControllerPage(int delta)
        {
            if (MoreOpen) return;
            _page += delta;
            ShowResults();
        }

        /// <summary>Controller View (the hint bar's "more filters"): opens or closes the More filters panel.</summary>
        public void ControllerMore() => ToggleMore();

        // Read-outs for automated checks.
        public int CardCount(DeckSection section) => Section(_deck, section).Count;
        public string CurrentDeckName => _deck.Name;
        public bool TestHandOpen => _handOverlay.gameObject.activeSelf;
        public int ResultsPage => _page;
        public Vector2 FirstResultOnScreen()
        {
            var rt = (RectTransform)_resultsLayer.GetChild(0);
            return RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        }
        public Vector2 SideZoneOnScreen()
        {
            var rt = _zones.First(z => z.zone == Zone.Side).rect;
            return RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        }
        public void DebugShowTestHand() => ShowTestHand(true);

        /// <summary>For automated checks: fill the search box, start a bot test, or show a test hand.</summary>
        public void DebugSetup(string? query, bool runTest, bool testHand)
        {
            if (query != null) _searchField.text = query; // triggers the search
            if (runTest) RunTest();
            if (testHand) ShowTestHand(true);
            if (_results.Count > 0) ShowDetail(_results[0]);
        }

        private CardArt? _artAside;

        /// <summary>
        /// For checks: draws the deck and a search's results without card pictures (on: every card in its text look), or
        /// with them again (off); <paramref name="limited"/>: a card's name, and the newest TCG list that limits it to 1 is
        /// picked (so its limit badge shows).
        /// </summary>
        public void DebugTextLook(bool on, string? query = null, string? limited = null)
        {
            if (on) { _artAside = CardView.Art; CardView.Art = null; }
            else { CardView.Art = _artAside; _artAside = null; }
            if (limited != null && _cards.All.FirstOrDefault(c => c.Name == limited) is CardInfo card)
            {
                int at = _banlists.FindLastIndex(b => b.Name.Contains("TCG") && b.Allowed(card.Code, _cards) == 1);
                if (at >= 0) _banIndex = at;
            }
            if (query != null) _searchField.text = query; // triggers the search
            RefreshDeck();
            ShowResults();
        }

        /// <summary>
        /// For checks: the drawn cards' names that break a word mid-word or sit under a card's limit badge, how many
        /// text-look names were looked at, and how many of those wear a badge. Call a frame after drawing (the text is laid out when it's drawn).
        /// </summary>
        public (List<string> problems, int names, int badges) DebugNameProblems()
        {
            var problems = new List<string>();
            int names = 0, badges = 0;
            var corners = new Vector3[4];
            foreach (var layer in new[] { _cardsLayer, _resultsLayer })
                foreach (var view in layer.GetComponentsInChildren<CardView>())
                {
                    if (view.transform.Find("Face/Name") is not RectTransform nameRt || nameRt.GetComponent<Text>() is not Text name) continue;
                    names++;
                    foreach (var broken in CardView.BrokenWords(name)) problems.Add(broken);
                    if (view.transform.Find("Limit") is RectTransform badge)
                    {
                        badges++;
                        badge.GetWorldCorners(corners);
                        float badgeBottom = corners[0].y;
                        nameRt.GetWorldCorners(corners);
                        if (corners[1].y > badgeBottom + 0.5f) problems.Add($"\"{name.text.Replace("\n", "/")}\" starts behind its limit badge");
                    }
                }
            return (problems, names, badges);
        }

        /// <summary>For automated checks: shows a card as a click does; what's selected, the CARD TEXT switch and the text note read.</summary>
        public (uint code, string label, string note) DebugShowText(uint code)
        {
            if (_cards.Get(code) is CardInfo info) ShowDetail(info);
            return DebugTextState();
        }

        /// <summary>For automated checks: the version a search result adds (<see cref="Resolve"/>) and the ban list showing.</summary>
        public (uint code, string list) DebugResolve(uint code) => (_cards.Get(code) is CardInfo info ? Resolve(info).Code : 0, BanlistName);

        /// <summary>For automated checks: the CARD TEXT switch clicked once.</summary>
        public (uint code, string label, string note) DebugCycleText()
        {
            CycleText();
            return DebugTextState();
        }

        private (uint code, string label, string note) DebugTextState()
        {
            string Plain(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "").Replace("\n", " ");
            return (_selected?.Code ?? 0, Plain(_textLabel.text.Split('\n')[0]), _selected != null ? Plain(TextNote(_selected)) : "");
        }

        /// <summary>
        /// For automated checks: opens a historic deck and switches card versions through the real buttons' actions,
        /// logging each result and saving screenshots to <paramref name="folder"/>; then quits.
        /// </summary>
        public System.Collections.IEnumerator DebugVersionCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            void Report(string what, bool ok) => Debug.Log($"version check: {(ok ? "ok  " : "FAIL")} {what}");
            // It edits decks, and the editor autosaves a recovery copy into the deck folder: never on your own decks.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mods") < 0)
            {
                Report("needs -mods <empty folder> (the editor writes its recovery copies there)", false);
                Application.Quit();
                yield break;
            }
            System.Collections.IEnumerator Shot(string name)
            {
                yield return new WaitForEndOfFrame();
                yield return new WaitForSecondsRealtime(0.5f); // pictures load
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));
                yield return null;
            }
            uint Count(Func<CardInfo, bool> match) => (uint)_deck.Main.Concat(_deck.Extra).Count(c => _cards.Get(c) is CardInfo i && match(i));

            var deck = _library.Names.FirstOrDefault(n => n.EndsWith("chaos-yata"));
            if (deck != null) LoadDeck(deck);
            Report($"chaos-yata opens under its era list ({_banlists[_banIndex].Name})", _banlists[_banIndex].Name == "TCG April 2004");
            _searchField.text = "sangan";
            var sangan = _results.FirstOrDefault(c => c.Name.StartsWith("Sangan"));
            Report($"under April 2004 a search result adds as {(sangan != null ? Resolve(sangan).Name : "-")}", sangan != null && Resolve(sangan).Name == "Sangan (Pre-Errata)");
            if (sangan != null) ShowDetail(sangan);
            yield return Shot("versions-original");

            // Text switch: every Sangan in the deck changes version.
            uint before = Count(i => i.Name.StartsWith("Sangan"));
            CycleText();
            Report($"text switch -> {_selected?.Name}; deck now has {Count(i => i.Name == _selected?.Name)} of {before}",
                before > 0 && Count(i => i.Name == _selected?.Name) == before);
            yield return Shot("versions-text-switched");

            // Artwork switch on a card with many artworks, outside any deck.
            NewDeck();
            CycleBanlist(-_banIndex); // no limits
            _searchField.text = "dark magician";
            var dm = _results.FirstOrDefault(c => c.Name == "Dark Magician");
            if (dm != null)
            {
                ShowDetail(dm);
                CycleArt(+1);
                CycleArt(+1);
                var chosen = _selected?.Code ?? 0;
                Report($"artwork switch -> {_artLabel.text}", chosen != dm.Code && _artLabel.text.StartsWith("3 of"));
                Add(dm, side: false);
                Report("adding the card uses the chosen artwork", _deck.Main.Count == 1 && _deck.Main[0] == chosen);
            }
            else Report("Dark Magician found", false);
            yield return Shot("versions-artwork");
            yield return CampaignVersionCheck(Report, Shot);
            // The free half's autosave too (recovery.txt): the check leaves no deck "unsaved".
            OwnedCards = null;
            ClearRecovery();
            Debug.Log("version check done");
            Application.Quit();
        }


        /// <summary>
        /// For automated checks: performs drags through the real event system (begin / drag / end at screen
        /// positions), logs whether each landed, and optionally leaves one drag in mid-air for a screenshot.
        /// </summary>
        public System.Collections.IEnumerator DebugDragTest(bool holdLastDrag)
        {
            yield return null; // let layout settle
            Vector2 Center(RectTransform rt) => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            Vector2 ZoneCenter(Zone z) => Center(_zones.First(t => t.zone == z).rect);
            GameObject FirstResult() => _resultsLayer.GetChild(0).gameObject;
            GameObject FirstCardIn(float top) => _cardsLayer.Cast<Transform>()
                .Where(t => t.GetComponent<DragSource>() != null)
                .OrderBy(t => Vector2.Distance(Center((RectTransform)t), Center(Box(_root, 410, top, 62, 90))))
                .First().gameObject;
            void Drag(GameObject source, Vector2 to, bool release = true)
            {
                var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = Center((RectTransform)source.transform) };
                ExecuteEvents.Execute(source, data, ExecuteEvents.beginDragHandler);
                data.position = to;
                ExecuteEvents.Execute(source, data, ExecuteEvents.dragHandler);
                if (release) ExecuteEvents.Execute(source, data, ExecuteEvents.endDragHandler);
            }
            void Report(string what, bool ok) => Debug.Log($"drag test: {(ok ? "ok  " : "FAIL")} {what}");

            int main = _deck.Main.Count, extra = _deck.Extra.Count, side = _deck.Side.Count;
            Drag(FirstResult(), ZoneCenter(Zone.Main));
            yield return null;
            Report($"result -> Main/Extra (main {main}->{_deck.Main.Count}, extra {extra}->{_deck.Extra.Count})", _deck.Main.Count + _deck.Extra.Count == main + extra + 1);

            side = _deck.Side.Count;
            Drag(FirstResult(), ZoneCenter(Zone.Side));
            yield return null;
            Report($"result -> Side (side {side}->{_deck.Side.Count})", _deck.Side.Count == side + 1);

            side = _deck.Side.Count;
            int home = _deck.Main.Count + _deck.Extra.Count;
            Drag(FirstCardIn(688), ZoneCenter(Zone.Main));
            yield return null;
            Report($"Side card -> Main (side {side}->{_deck.Side.Count}, main+extra {home}->{_deck.Main.Count + _deck.Extra.Count})",
                _deck.Side.Count == side - 1 && _deck.Main.Count + _deck.Extra.Count == home + 1);

            main = _deck.Main.Count;
            Drag(FirstCardIn(152), ZoneCenter(Zone.Search));
            yield return null;
            Report($"Main card -> search panel removes it (main {main}->{_deck.Main.Count})", _deck.Main.Count == main - 1);

            if (holdLastDrag) Drag(FirstResult(), ZoneCenter(Zone.Side) + new Vector2(80, 0), release: false);
        }

        public void Close()
        {
            // Leaving on purpose (after the "unsaved changes" warning) drops the recovery copy; a crash or quit keeps it.
            if (_dirty) ClearRecovery();
#if ENABLE_INPUT_SYSTEM
            ControllerSupport.Legend = null;
#endif
            if (_tester.Running) _tester.Cancel();
            _root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        private void OnDestroy() => _tester.Cancel();

        private void Update()
        {
            if (!IsOpen) return;
            // Escape shuts the More filters panel (B on a controller: ControllerBack).
#if ENABLE_INPUT_SYSTEM
            if (MoreOpen && UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true) CloseMore();
#else
            if (MoreOpen && Input.GetKeyDown(KeyCode.Escape)) CloseMore();
#endif
            // The bot test runs on a background thread; show its progress here.
            if (_tester.Running || _wasTesting)
            {
                ShowTestProgress();
                _wasTesting = _tester.Running;
            }
        }

        // ------------------------------------------------------------------ Layout

        private void Build()
        {
            var T = UiTheme.Current;
            var canvasGo = new GameObject("DeckEditorCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10; // above the duel screen
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // the whole 1920x1080 layout stays visible at any aspect ratio

            _root = Ui.Stretched("DeckEditor", canvasGo.transform, Vector2.zero, Vector2.one);
            Letterbox.Attach(_root); // in a window taller than 16:9, the layout in the middle with bars above and below
            Ui.Image(_root, T.Base);
            // The side panels run the root's full height; in a window taller than 16:9 Letterbox carries them into the bars.
            Ui.Image(FullHeight(Box(_root, 0, 0, 400, 1080)), T.Panel);
            Ui.Image(FullHeight(Box(_root, 1400, 0, 520, 1080)), T.Panel);

            BuildDetail();
            BuildToolbar();
            BuildDeckArea();
            BuildSearch();
            BuildTestHand();
            BuildGhost();
        }

        /// <summary>A rect positioned from the top-left of the 1920x1080 layout.</summary>
        private static RectTransform Box(Transform parent, float x, float y, float w, float h)
        {
            var rt = Ui.Rect("Box", parent);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        private Text Label(float x, float y, float w, float h, string text, int size, TextAnchor anchor = TextAnchor.MiddleLeft, Transform? parent = null) =>
            Ui.Text(Box(parent ?? _root, x, y, w, h), text, size, UiTheme.Current.TextBody, anchor);

        private Button Button(float x, float y, float w, float h, string label, Action onClick, Transform? parent = null)
        {
            var T = UiTheme.Current;
            var rt = Box(parent ?? _root, x, y, w, h);
            var image = Ui.Image(rt, T.Button);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => { Sounds.Play("click"); onClick(); });
            Ui.Text(rt, label, 15, T.Text);
            UiHover.Attach(button);
            return button;
        }

        // Card versions: alternate artworks and original texts. A choice applies to every copy in the deck and is what
        // gets added next; with no choice made, cards come in their original text under a 2004-2005 ban list.
        private CardVersions _versions = null!;
        private readonly Dictionary<uint, uint> _preferred = new Dictionary<uint, uint>();

        private void BuildDetail()
        {
            var T = UiTheme.Current;
            // The card, Master Duel style (name bar, Attribute, picture, stats with icons, type bar, text).
            _cardDetail = new CardDetail(Box(_root, 20, 20, 360, 420)) { StylePicker = true };
            _cardDetail.Clicked += ShowReading;
            // Its genre chips and archetype chips search for more cards like it (in a duel they stay hover-only).
            _cardDetail.GenreClicked = SearchGenre;
            _cardDetail.ArchetypesOf = info => _search.ArchetypesOf(info);
            _cardDetail.ArchetypeClicked = SearchArchetype;
            // A card's art style changed (its Artwork button): its copies in the results and the deck redraw in it.
            if (CardView.Art != null)
                CardView.Art.Changed += _ =>
                {
                    if (!_root.gameObject.activeInHierarchy) return;
                    ShowResults();
                    RefreshDeck();
                };

            // Under it: the card's artwork and text version (when it has several).
            _versionPanel = Box(_root, 20, 446, 360, 50);
            Label(0, 0, 50, 22, "ART", 12, parent: _versionPanel).color = T.Caption;
            Button(0, 20, 26, 28, "<", () => CycleArt(-1), _versionPanel);
            _artLabel = Label(28, 20, 74, 28, "", 13, TextAnchor.MiddleCenter, _versionPanel);
            Button(104, 20, 26, 28, ">", () => CycleArt(+1), _versionPanel);
            Label(140, 0, 220, 22, "CARD TEXT", 12, parent: _versionPanel).color = T.Caption;
            var text = Button(140, 20, 220, 28, "", CycleText, _versionPanel);
            _textLabel = text.GetComponentInChildren<Text>();
            _textLabel.fontSize = 13;
            // ("Original text (until Sep 2026, CPUs only)" is long for the button: it shrinks.)
            _textLabel.resizeTextForBestFit = true;
            _textLabel.resizeTextMinSize = 9;
            _textLabel.resizeTextMaxSize = 13;
            _versionPanel.gameObject.SetActive(false);
            // Copies in the deck, opening-hand odds, and the ban list's limit.
            _detail = Label(20, 500, 360, 52, "", 13, TextAnchor.UpperLeft);
            _detail.supportRichText = true;
            _detail.verticalOverflow = VerticalWrapMode.Truncate;
            // (A text note adds a line or two: the block shrinks to keep them all rather than cut the last.)
            _detail.resizeTextForBestFit = true;
            _detail.resizeTextMinSize = 10;
            _detail.resizeTextMaxSize = 13;
            // The text block beside the art shows the name and stats; the box below shows card text.
            Button(20, 556, 110, 34, "Add", () => { if (_selected != null) Add(_selected, side: false); });
            Button(140, 556, 110, 34, "To Side", () => { if (_selected != null) Add(_selected, side: true); });
            Button(260, 556, 120, 34, "Remove one", () => { if (_selected != null) RemoveOne(_selected.Code); });
            // Labeled from the start (the help names them), dimmed until a card is picked.
            _starterButton = Button(20, 598, 140, 34, "Mark starter", ToggleStarter);
            Button(166, 598, 110, 34, "Related", ShowRelated);
            _favoriteButton = Button(282, 598, 98, 34, "☆ Favorite", ToggleFavorite);
            _favoriteLabel = _favoriteButton.GetComponentInChildren<Text>();
            _starterLabel = _starterButton.GetComponentInChildren<Text>();
            SetCardButtons(false);

            Label(20, 646, 360, 24, "OPENING HAND", 13).color = T.Caption;
            // Ban Passes (campaign): shown for cards the ban list limits.
            var passButtons = Ui.Box(_root, 196, 642, 184, 28);
            Button(0, 0, 88, 28, "+ Pass", AddPass, passButtons);
            Button(94, 0, 88, 28, "− Pass", RemovePass, passButtons);
            _passButtons = passButtons.gameObject;
            _passButtons.SetActive(false);
            _odds = Label(20, 672, 360, 92, "", 14, TextAnchor.UpperLeft);
            _odds.supportRichText = true;

            Label(20, 776, 360, 24, "TEST AGAINST A BOT", 13).color = T.Caption;
            Button(20, 804, 34, 30, "<", () => CycleOpponent(-1));
            _opponentLabel = Label(58, 804, 284, 30, "", 15, TextAnchor.MiddleCenter);
            Button(346, 804, 34, 30, ">", () => CycleOpponent(+1));
            Button(20, 840, 34, 30, "<", () => CycleOpponentDeck(-1));
            _opponentDeckLabel = Label(58, 840, 284, 30, "", 15, TextAnchor.MiddleCenter);
            Button(346, 840, 34, 30, ">", () => CycleOpponentDeck(+1));
            Button(20, 878, 220, 34, "Play 100 games", RunTest);
            Button(250, 878, 130, 34, "Cancel", () => { _tester.Cancel(); ShowTestProgress(); });
            _testResult = Label(20, 920, 360, 150, "Your deck plays quick games against the bot, alternating who goes first.", 14, TextAnchor.UpperLeft);
            _testResult.supportRichText = true;
        }

        private void BuildToolbar()
        {
            _nameField = Ui.Input(Box(_root, 410, 10, 250, 40), "deck name", 17, v => { _deck.Name = v; MarkDirty(); FitName(false); });
            Button(668, 10, 70, 40, "Save", Save);
            Button(744, 10, 64, 40, "New", () => Confirmed("new", NewDeck));
            Button(814, 10, 80, 40, "Delete", Delete);
            Button(904, 10, 90, 40, "Decks...", ShowBrowser);
            Button(1000, 10, 66, 40, "Copy", Duplicate);
            _labelField = Ui.Input(Box(_root, 1072, 10, 192, 40), "label / folder", 15, v => { SetLabel(v); });
            _openLabel = Label(0, 0, 0, 0, "", 1);
            _openLabel.gameObject.SetActive(false);
            Button(1270, 10, 120, 40, "Back", () => Confirmed("back", Close)); // to the duel, the campaign or the menu, wherever it was opened from

            Button(410, 58, 58, 34, "Undo", Undo);
            Button(474, 58, 58, 34, "Redo", Redo);
            Button(538, 58, 54, 34, "Sort", () => Change(d => { d.Main = DeckRules.Sorted(d.Main, _cards); d.Extra = DeckRules.Sorted(d.Extra, _cards); d.Side = DeckRules.Sorted(d.Side, _cards); }));
            Button(598, 58, 56, 34, "Clear", () => Change(d => { d.Main.Clear(); d.Extra.Clear(); d.Side.Clear(); }));
            Button(660, 58, 86, 34, "Test hand", () => ShowTestHand(true));
            // Import: a YDKE link or .ydk text from the clipboard; Import .ydk: a .ydk file, picked in macOS's open panel.
            _freeTools = new[]
            {
                Button(752, 58, 66, 34, "Import", Import).gameObject,
                Button(822, 58, 100, 34, "Import .ydk", PickYdkFile).gameObject,
                Button(926, 58, 62, 34, "Export", Export).gameObject,
            };
            // In the campaign: fix the deck for the chosen ban list from your collection, or build one (around the selected card);
            // Recipe cycles your recipes (yours first); Import .ydk keeps a .ydk file as one of your recipes and builds it.
            _campaignTools = new[]
            {
                Button(752, 58, 62, 34, "Auto-fix", AutoFix).gameObject,
                Button(818, 58, 54, 34, "Build", BuildFromCollection).gameObject,
                Button(876, 58, 62, 34, "Recipe", NextRecipe).gameObject,
                Button(942, 58, 100, 34, "Import .ydk", PickYdkFile).gameObject,
            };
            Button(1048, 58, 30, 34, "<", () => CycleBanlist(-1));
            _banLabel = Label(1080, 58, 278, 34, "", 15, TextAnchor.MiddleCenter);
            Button(1360, 58, 30, 34, ">", () => CycleBanlist(+1));
            _status = Label(410, 98, 980, 26, "", 14);
            // A longer message (Build's comparison, Save as new's how-to) shrinks to two lines rather than losing its end.
            _status.resizeTextForBestFit = true;
            _status.resizeTextMinSize = 11;
            _status.resizeTextMaxSize = 14;
        }

        private void BuildDeckArea()
        {
            var T = UiTheme.Current;
            // Drop zones sit behind the cards and never take clicks; they light up while something is dragged over them.
            AddZone(Zone.Main, 406, 148, 998, 390, ZoneIdle);
            AddZone(Zone.Extra, 406, 560, 998, 100, ZoneIdle);
            AddZone(Zone.Side, 406, 684, 998, 100, ZoneIdle);
            _mainHeader = Label(410, 126, 980, 22, "", 15);
            _extraHeader = Label(410, 538, 980, 22, "", 15);
            _sideHeader = Label(410, 662, 980, 22, "", 15);
            _cardsLayer = Box(_root, 0, 0, 1920, 1080);
            _charts = Box(_root, 410, 792, 980, 112);
            Ui.Image(_charts, T.Panel);
            _problems = Label(410, 912, 980, 160, "", 14, TextAnchor.UpperLeft);
            _problems.supportRichText = true;
        }

        private void BuildSearch()
        {
            var T = UiTheme.Current;
            _searchField = Ui.Input(Box(_root, 1410, 10, 406, 40), "search: name, text, lv8, atk>2000, @archetype...", 16,
                v => { _query = v; RunSearch(); });
            // Where the box's plain words look: Name + text, Name, Text (click: the next, right-click: the previous).
            var mode = Box(_root, 1820, 10, 90, 40);
            Ui.Image(mode, T.Inset);
            _modeLabel = Ui.Text(mode, "", 13, T.Text);
            _modeLabel.supportRichText = true;
            Ui.Clickable(mode, () => CycleMode(+1), () => CycleMode(-1), style: HoverStyle.Button);
            _searchLayout.Add(("search box", (RectTransform)_searchField.transform));
            _searchLayout.Add(("mode switch", mode));
            var modeTip = mode.gameObject.AddComponent<TipOnHover>();
            modeTip.Title = "Where words look";
            modeTip.Body = "Name + text: a card's name or its text. Name: the name only. Text: the card text only (both effects of a Pendulum monster).\n\n"
                           + "Typed prefixes ignore it: name:word or $word, text:word, pend:word (the Pendulum Effect).";
            for (int i = 0; i < ChipLabels.Length; i++)
            {
                int chip = i;
                var rt = Box(_root, 1410 + (i % 5) * 101, 56 + (i / 5) * 30, 97, 26);
                _chipImages.Add(Ui.Image(rt, ChipColor(i, false)));
                var icon = Box(rt, 4, 3, 20, 20).gameObject.AddComponent<RawImage>();
                icon.texture = Icons.For(ChipIcons[i]);
                icon.raycastTarget = false;
                var label = Ui.Text(Box(rt, 26, 0, 69, 26), ChipLabels[i], 12, T.Text, TextAnchor.MiddleLeft);
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 9;
                label.resizeTextMaxSize = 12;
                label.raycastTarget = false;
                Ui.Clickable(rt, () => ToggleChip(chip));
                _searchLayout.Add(($"chip {ChipLabels[i]}", rt));
            }
            var pickers = new[] { KindOptions.Length, AttributeOptions.Length, TypeOptions.Length, LevelOptions.Length, SortOptions.Length };
            for (int i = 0; i < pickers.Length; i++)
            {
                int picker = i, count = pickers[i];
                // Five 85 px pickers; the More button takes the row's right 60 px.
                var rt = Box(_root, 1410 + i * 88, 118, 85, 28);
                Ui.Image(rt, T.Inset);
                _pickLabels[i] = Ui.Text(rt, "", 13, T.Text);
                _pickLabels[i].resizeTextForBestFit = true;
                _pickLabels[i].resizeTextMinSize = 9;
                _pickLabels[i].resizeTextMaxSize = 13;
                Ui.Clickable(rt, () => { _picks[picker] = (_picks[picker] + 1) % count; RunSearch(); },
                    () => { _picks[picker] = (_picks[picker] - 1 + count) % count; RunSearch(); });
                _searchLayout.Add(($"picker {i + 1}", rt));
            }
            BuildMoreButton(1850, 118, 60, 28);
            _resultCount = Label(1410, 152, 90, 26, "", 13);
            _searchLayout.Add(("More button", _moreFill.rectTransform));
            _searchLayout.Add(("count", _resultCount.rectTransform));
            // The active filters: a window onto a row that scrolls sideways (the wheel, a drag, or the ‹ › ends) when it runs long.
            _activeRow = Box(_root, 1500, 152, 300, 26);
            Ui.Image(_activeRow, new Color(0, 0, 0, 0)); // takes the wheel between the chips
            _activeRow.gameObject.AddComponent<RectMask2D>();
            _activeContent = Box(_activeRow, 0, 0, 300, 26);
            _activeScroll = _activeRow.gameObject.AddComponent<ScrollRect>();
            _activeScroll.viewport = _activeRow;
            _activeScroll.content = _activeContent;
            _activeScroll.horizontal = true;
            _activeScroll.vertical = false;
            _activeScroll.movementType = ScrollRect.MovementType.Clamped;
            _activeScroll.inertia = false;
            _activeScroll.scrollSensitivity = 24;
            _activeScroll.onValueChanged.AddListener(_ => ShowActiveEnds());
            _activeLeft = ActiveEnd(0, "‹", -1).gameObject;
            _activeRight = ActiveEnd(286, "›", +1).gameObject;
            // The scope: click for the next, right-click for the previous (as the pickers do).
            var owned = Box(_root, 1806, 152, 104, 26);
            Ui.Image(owned, T.Button);
            _ownedLabel = Ui.Text(owned, "", 12, T.Text);
            _ownedLabel.resizeTextForBestFit = true;
            _ownedLabel.resizeTextMinSize = 10;
            _ownedLabel.resizeTextMaxSize = 12;
            Ui.Clickable(owned, () => CycleScope(+1), () => CycleScope(-1), style: HoverStyle.Button);
            _ownedToggle = owned.gameObject;
            _resultsLayer = Box(_root, 1410, 184, 500, 5 * 150);
            _searchLayout.Add(("active row", _activeRow));
            _searchLayout.Add(("scope", owned));
            _searchLayout.Add(("results grid", _resultsLayer));
            // An empty Seen in duels / Recently viewed says how it fills.
            _emptyNote = Label(1430, 260, 460, 80, "", 15, TextAnchor.UpperCenter);
            _emptyNote.color = T.TextSoft;
            _emptyNote.raycastTarget = false;
            var prev = Button(1410, 940, 100, 32, "< Prev", () => { _page--; ShowResults(); });
            _pageLabel = Label(1514, 940, 292, 32, "", 14, TextAnchor.MiddleCenter);
            var next = Button(1810, 940, 100, 32, "Next >", () => { _page++; ShowResults(); });
            _searchLayout.Add(("Prev", (RectTransform)prev.transform));
            _searchLayout.Add(("page", _pageLabel.rectTransform));
            _searchLayout.Add(("Next", (RectTransform)next.transform));
            var help = Label(1410, 980, 500, 90, "Click a card: add it.  Right-click: add to Side Deck.  Or drag it onto a section.\n"
                + "Click a deck card: remove it.  Right-click: move to / from Side.\nPickers: click for the next option, right-click for the previous.",
                13, TextAnchor.UpperLeft);
            help.color = T.Caption.WithAlpha(0.55f);
            _searchLayout.Add(("help", help.rectTransform));
            BuildMorePanel(); // over the grid, so it's built after it (DeckEditor.Filters)
        }

        /// <summary>One end of the active filter row: shown when the row runs on that way; a click scrolls it.</summary>
        private RectTransform ActiveEnd(float x, string glyph, int way)
        {
            var T = UiTheme.Current;
            var rt = Box(_activeRow, x, 0, 14, 26);
            Ui.Image(rt, T.Panel.WithAlpha(0.92f));
            Ui.Text(rt, glyph, 16, T.Text).raycastTarget = false;
            Ui.Clickable(rt, () =>
            {
                float room = Mathf.Max(0, _activeContent.rect.width - _activeRow.rect.width);
                if (room <= 0) return;
                _activeScroll.horizontalNormalizedPosition = Mathf.Clamp01(_activeScroll.horizontalNormalizedPosition + way * 150 / room);
            });
            return rt;
        }

        /// <summary>The ‹ › ends show only where the active row has more to scroll to.</summary>
        private void ShowActiveEnds()
        {
            if (_activeLeft == null || _activeRight == null) return; // (while the row is being built)
            float room = _activeContent.sizeDelta.x - 300;
            float x = -_activeContent.anchoredPosition.x;
            _activeLeft.SetActive(room > 0.5f && x > 0.5f);
            _activeRight.SetActive(room > 0.5f && x < room - 0.5f);
            _activeLeft.transform.SetAsLastSibling();
            _activeRight.transform.SetAsLastSibling();
        }

        // ---- Reading view: a card's full text, large, with its printings and where to get it ------------------

        private RectTransform? _reading;

        private void ShowReading(CardInfo info)
        {
            var T = UiTheme.Current;
            Viewed(info);
            if (_reading != null) Destroy(_reading.gameObject);
            var root = Ui.Stretched("Reading", _root, Vector2.zero, Vector2.one);
            _reading = root;
            var dim = Ui.Image(root, T.Scrim.WithAlpha(0.75f));
            Ui.Clickable((RectTransform)dim.transform, CloseReading);
            var panel = Box(root, 330, 140, 1260, 800);
            Ui.Image(panel, T.PanelDeep);
            panel.gameObject.AddComponent<Outline>().effectColor = T.Focus;
            var artBox = Box(panel, 30, 30, 360, 525);
            var art = CardView.Art?.GetFull(info.Code); // big enough for the whole picture
            if (art != null)
            {
                var raw = artBox.gameObject.AddComponent<RawImage>();
                raw.texture = art;
                // Your best copy, as it is.
                var best = OwnedCards?.CopiesOf(Banlist.BaseCode(info.Code, _cards)) is { Count: > 0 } mine ? mine[0] : null;
                if (best != null && CardFoil.Mode == CardFoil.Display.On) CardFoil.Apply(raw, info, best.Rarity, 1f, best.Look);
                else CardFoil.Apply(raw, info, CardFoil.RarityOf(info), 1f, best?.Look ?? default);
                CardFoil.Tilting(raw);
            }
            else Ui.Image(artBox, CardView.FrameColor(info.Type));
            var name = Label(420, 30, 800, 48, info.Name, 30, parent: panel);
            name.fontStyle = FontStyle.Bold;
            name.color = T.Text;
            Label(420, 80, 800, 28, Describe(info), 17, parent: panel).color = T.TextSoft;
            // The text, large, scrolling when long.
            var view = Box(panel, 420, 120, 800, 470);
            Ui.Image(view, T.Scrim.WithAlpha(0.25f));
            view.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Rect("Text", view);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = new Vector2(16, -10);
            content.offsetMax = new Vector2(-16, -12);
            var text = Ui.Text(content, info.Text, FxSettings.Scaled(21), T.TextBright, TextAnchor.UpperLeft);
            text.verticalOverflow = VerticalWrapMode.Overflow;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            // Printings, legality, and (in the campaign) how many you own and where to get more.
            var key = Banlist.BaseCode(info.Code, _cards);
            var limits = _banlists[_banIndex];
            int allowed = limits.Allowed(info.Code, _cards);
            var lines = new List<string>();
            if (OwnedCards != null)
            {
                int own = OwnedCards.Count(key);
                lines.Add(own == 0 ? $"<color={T.BadHex}>You don't own it.</color> Get it {WhereToGet?.Invoke(key) ?? "in the Card Shop"}." : $"You own <b>{own}</b>." + (own < 3 ? $" More {WhereToGet?.Invoke(key) ?? "in the Card Shop"}." : ""));
                foreach (var copy in OwnedCards.CopiesOf(key))
                    lines.Add($"    <color={Grades.Color(copy.Shown)}>{Grades.Glyph(copy.Shown)}</color> {CardFoil.ColoredName(copy.Rarity)} · "
                              + $"<color={Grades.Color(copy.Shown)}>{Grades.Describe(copy)}</color>  <color={T.GoldHex}>{OwnedCards.Value(copy):N0} DP</color>"
                              + $"  ·  {CardDetail.PrintingTag(copy)}"
                              + (copy.AtDesk ? $"  <color={T.TextDimHex}>(at the Grading Desk)</color>" : ""));
            }
            lines.Add(allowed >= 3 ? $"Unlimited in {limits.Name}." : allowed == 0 ? $"<color={T.BadHex}>Not allowed in {limits.Name}.</color>" : $"<color={T.WarnHex}>Limited to {allowed} in {limits.Name}.</color>");
            var printings = CardFoil.PrintingsText(info, 6);
            if (printings.Length > 0) lines.Add(printings);
            var facts = Label(30, 570, 1200, 170, string.Join("\n", lines), 15, TextAnchor.UpperLeft, panel);
            facts.supportRichText = true;
            Button(1100, 740, 130, 40, "Close", CloseReading, panel);
            Button(950, 740, 140, 40, "Related cards", () => { CloseReading(); _selected = info; ShowRelated(); }, panel);
            Button(800, 740, 140, 40, "Add to deck", () => Add(info, side: false), panel);
            root.SetAsLastSibling();
        }

        private void CloseReading()
        {
            if (_reading != null) Destroy(_reading.gameObject);
            _reading = null;
        }

        /// <summary>For automated checks: the card detail (its Artwork button), showing a card.</summary>
        public CardDetail DebugDetail(CardInfo info)
        {
            ShowDetail(info);
            return _cardDetail;
        }

        /// <summary>For automated checks: the pictures drawn in the deck grid and the results (texture names, without the letterbox tag).</summary>
        public List<string> DebugDrawnArt() => _cardsLayer.GetComponentsInChildren<RawImage>().Concat(_resultsLayer.GetComponentsInChildren<RawImage>())
            .Where(r => r.texture != null).Select(r => ArtLoader.PlainName(r.texture)).ToList();

        /// <summary>For automated checks: opens the reading view of the selected card.</summary>
        public bool DebugReading()
        {
            if (_selected == null) return false;
            ShowReading(_selected);
            return _reading != null;
        }

        /// <summary>For automated checks: the catalog's state (cards, owned only, active filter count).</summary>
        public (int results, int unowned, int active) DebugCatalog() =>
            (_results.Count, OwnedCards == null ? 0 : _results.Count(c => !OwnedCards.Owns(Banlist.BaseCode(c.Code, _cards))), _activeCount);

        /// <summary>For automated checks: sets the pickers (kind, attribute, type, level, sort) and an effect tag.</summary>
        public void DebugFilters(int kind, int attribute, int sort, int chip)
        {
            _picks[0] = kind;
            _picks[1] = attribute;
            _picks[4] = sort;
            if (chip >= 0 && !_chips.Contains(chip)) ToggleChip(chip);
            else RunSearch();
        }

        public void DebugClearFilters()
        {
            Array.Clear(_picks, 0, _picks.Length);
            foreach (var c in _chips.ToList()) _chipImages[c].color = ChipColor(c, false);
            _chips.Clear();
            _related = null;
            ClearMoreFilters();
            CloseReading();
            RunSearch();
        }

        public void DebugRelated(string name)
        {
            _selected = _search.Find($"\"{name}\"", 5).FirstOrDefault(c => c.Name == name);
            ShowRelated();
        }

        // ---- Favorites -------------------------------------------------------------------------------------

        private string FavoritesPath => System.IO.Path.Combine(_library.OwnFolder, "favorites.txt");

        private void LoadFavorites()
        {
            _favorites.Clear();
            try
            {
                if (!System.IO.File.Exists(FavoritesPath)) return;
                foreach (var line in System.IO.File.ReadAllLines(FavoritesPath))
                    if (uint.TryParse(line.Split('#')[0].Trim(), out var code)) _favorites.Add(code);
            }
            catch (System.IO.IOException) { }
        }

        private void ToggleFavorite()
        {
            if (_selected == null) return;
            var key = Banlist.BaseCode(_selected.Code, _cards);
            bool on = _favorites.Add(key);
            if (!on) _favorites.Remove(key);
            try { System.IO.File.WriteAllLines(FavoritesPath, _favorites.OrderBy(c => c).Select(c => $"{c} # {_cards.Get(c)?.Name}")); }
            catch (System.IO.IOException) { }
            SetStatus(on ? $"★ {_selected.Name} is a favorite. Show favorites with the button above the cards." : $"{_selected.Name} is no longer a favorite.");
            ShowDetail(_selected);
            ShowResults();
        }

        private void CycleScope(int delta)
        {
            // Campaign: every card, owned only, favorites, seen in duels, recently viewed. Free play: the same without owned only.
            var scopes = OwnedCards != null
                ? new[] { ScopeAll, ScopeOwned, ScopeFavorites, ScopeSeen, ScopeViewed }
                : new[] { ScopeAll, ScopeFavorites, ScopeSeen, ScopeViewed };
            int at = Math.Max(0, Array.IndexOf(scopes, _scope));
            _scope = scopes[(at + delta + scopes.Length) % scopes.Length];
            if (_scope == ScopeSeen || _scope == ScopeViewed) _seenLog = SeenLog.Read(SeenPath); // a duel may have ended since
            _seenDuel = -1;
            RunSearch();
        }

        // ---- Seen in duels and Recently viewed (seen.txt, beside favorites.txt) --------------------------------

        /// <summary>
        /// The opponents' cards from the last 20 duels and the last 40 cards looked at here: mods/decks/seen.txt, shared by
        /// free play and the campaign and kept through Start Over (a convenience, like favorites).
        /// </summary>
        public string SeenPath => System.IO.Path.Combine(_library.OwnFolder, "seen.txt");

        /// <summary>
        /// Whether a check run may write seen.txt (the search check asks; other checks leave it alone, so a run without
        /// -mods never touches the player's log).
        /// </summary>
        public static bool SeenInChecks { get; set; }

        private static bool MayWriteSeen => SeenInChecks || !DuelController.IsCheckRun;

        /// <summary>
        /// A duel ended (DuelController, when it's yours to play: never a replay, a show duel, a watched duel, the deck
        /// tester's games or a check run unless it asks): its opponent's cards go to the front of the log.
        /// </summary>
        public void AddSeenDuel(string opponent, string mode, SeenCards seen)
        {
            if (seen.Count == 0 || !MayWriteSeen) return; // nothing became known: no empty duel in the picker
            var log = SeenLog.Read(SeenPath);
            log.AddDuel(DateTime.Now, opponent, mode, seen);
            WriteSeen(log);
            _seenDuel = -1; // a new log: the picked duel's place in it moved
        }

        /// <summary>A card picked in the editor or opened in the reading view: the front of Recently viewed (hovering doesn't count).</summary>
        private void Viewed(CardInfo info)
        {
            var key = Banlist.BaseCode(info.Code, _cards);
            if (_seenLog.Viewed.Count > 0 && _seenLog.Viewed[0] == key) return;
            // Read fresh, so a duel written since the editor opened stays in the file.
            var log = SeenLog.Read(SeenPath);
            log.View(key);
            WriteSeen(log);
        }

        private void WriteSeen(SeenLog log)
        {
            _seenLog = log;
            if (!MayWriteSeen) return; // checks keep it in memory
            try { log.Write(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Debug.LogWarning($"Could not save {log.Path}: {ex.Message}"); }
        }

        /// <summary>The kept duel the Seen in duels scope shows, or null for all of them.</summary>
        private SeenLog.Duel? SeenDuel => _seenDuel >= 0 && _seenDuel < _seenLog.Duels.Count ? _seenLog.Duels[_seenDuel] : null;

        /// <summary>The duel picker's label: "All 20 duels", "Last duel", "vs Professor Mica · 7 Nov".</summary>
        private string SeenDuelLabel(int index)
        {
            int n = _seenLog.Duels.Count;
            if (index < 0) return n == 1 ? "The 1 duel" : $"All {n} duels";
            if (index == 0) return "Last duel";
            var d = _seenLog.Duels[index];
            return $"vs {d.Opponent} · {d.When.Day} {d.When.ToString("MMM", System.Globalization.CultureInfo.InvariantCulture)}";
        }

        private void CycleSeenDuel(int delta)
        {
            int options = _seenLog.Duels.Count + 1; // all, then each duel
            _seenDuel = ((_seenDuel + 1 + delta + options) % options) - 1;
            RunSearch();
        }

        // ---- Analysis: the deck's archetype and its key cards, and Extra Deck monsters it can't make ------------

        /// <summary>Archetype names (from the card scripts) and the decks that define each archetype's key cards.</summary>
        public void SetAnalysisData(Archetypes archetypes)
        {
            _archetypes = archetypes;
            _search.SetArchetypes(archetypes); // @name searches, the Archetype box's suggestions, the card detail's archetype chips
        }

        private string Analysis()
        {
            var T = UiTheme.Current;
            var lines = new List<string>();
            if (_archetypes.Main(_deck, _cards) is { } arch)
            {
                _references ??= _library.Names.Where(n => !_library.IsOwn(n)).Select(_library.Load).ToList();
                var keys = DeckAnalysis.KeyCards(arch.code, _references, _cards);
                if (keys.Count > 0)
                {
                    var used = new HashSet<uint>(_deck.Main.Concat(_deck.Extra).Select(c => Banlist.BaseCode(c, _cards)));
                    var missing = keys.Where(k => !used.Contains(k)).ToList();
                    var owned = OwnedCards == null ? "" : $", you own {keys.Count(OwnedCards.Owns)}";
                    lines.Add($"<b>{arch.name}</b>: {keys.Count - missing.Count} of its {keys.Count} key cards in the deck{owned}"
                              + (missing.Count > 0 ? $" <color={T.TextDimHex}>(not in: {string.Join(", ", missing.Take(3).Select(k => _cards.Get(k)?.Name))}{(missing.Count > 3 ? "..." : "")})</color>" : $" <color={T.GoodHex}>(all of them)</color>"));
                }
            }
            if (_deck.Extra.Count > 0)
            {
                var gaps = DeckAnalysis.ExtraDeckGaps(_deck, _cards);
                int distinct = _deck.Extra.Distinct().Count();
                lines.Add(gaps.Count == 0 ? $"Extra Deck: <color={T.GoodHex}>this Main Deck can make all {distinct}</color>."
                    : $"Extra Deck: <color={T.WarnHex}>{gaps.Count} of {distinct} look hard to make</color>: "
                      + string.Join("; ", gaps.Take(3).Select(g => $"{_cards.Get(g.code)?.Name} ({g.reason})")) + (gaps.Count > 3 ? "..." : ""));
            }
            return lines.Count == 0 ? "" : string.Join("\n", lines) + "\n";
        }

        public string DebugAnalysis() => Analysis();

        /// <summary>For automated checks: marks the selected card a favorite and shows favorites only.</summary>
        public int DebugFavorite()
        {
            if (_selected == null) return -1;
            if (!_favorites.Contains(Banlist.BaseCode(_selected.Code, _cards))) ToggleFavorite();
            _scope = ScopeFavorites;
            RunSearch();
            return _results.Count;
        }

        public void DebugUnfavoriteAll()
        {
            _favorites.Clear();
            try { System.IO.File.Delete(FavoritesPath); } catch (System.IO.IOException) { }
            _scope = ScopeAll;
            RunSearch();
        }

        private void ToggleChip(int chip)
        {
            if (!_chips.Remove(chip)) _chips.Add(chip);
            _chipImages[chip].color = ChipColor(chip, _chips.Contains(chip));
            RunSearch();
        }

        /// <summary>
        /// The filters in effect, as removable chips: the typed search, effect tags, pickers, related cards and the More
        /// panel's picks; click one to drop it. In Seen in duels the duel picker comes first. The row scrolls sideways when
        /// it runs past its 300 px.
        /// </summary>
        private void ShowActiveFilters()
        {
            var T = UiTheme.Current;
            float keptX = _activeContent.anchoredPosition.x;
            ClearChildren(_activeContent);
            var active = new List<(string label, Action remove)>();
            if (_related != null) active.Add(($"Related: {_related.Name}", () => _related = null));
            if (_query.Trim().Length > 0) active.Add(($"\"{_query.Trim()}\"", () => { _query = ""; _searchField.SetTextWithoutNotify(""); }));
            foreach (var c in _chips.OrderBy(c => c).ToList()) active.Add((ChipLabels[c], () => { _chips.Remove(c); _chipImages[c].color = ChipColor(c, false); }));
            var options = new[] { KindOptions, AttributeOptions, TypeOptions, LevelOptions };
            for (int p = 0; p < options.Length; p++)
            {
                int picker = p;
                if (_picks[p] > 0) active.Add((options[p][_picks[p]].label, () => _picks[picker] = 0));
            }
            active.AddRange(MoreChips()); // genres, archetypes, Limit, Look (DeckEditor.Filters)
            _activeCount = active.Count;
            float x = 0;
            if (_scope == ScopeSeen && _seenLog.Duels.Count > 0)
            {
                // The duel picker: all kept duels, the last one, then each one by opponent and day (click / right-click).
                var label = SeenDuelLabel(_seenDuel);
                float w = Mathf.Min(170, label.Length * 7f + 18);
                var rt = Box(_activeContent, x, 0, w - 4, 26);
                Ui.Image(rt, T.Inset);
                var t = Ui.Text(rt, label, 12, T.Text);
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = 9;
                t.resizeTextMaxSize = 12;
                Ui.Clickable(rt, () => CycleSeenDuel(+1), () => CycleSeenDuel(-1));
                var tip = rt.gameObject.AddComponent<TipOnHover>();
                tip.Title = "Which duel";
                tip.Body = SeenDuel is SeenLog.Duel d
                    ? $"vs {d.Opponent} ({d.Mode}), {d.When.ToString("d MMM yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)}: {d.Cards.Count} cards seen.\n\nClick for the next duel, right-click for the previous."
                    : $"The cards your opponents showed in your last {_seenLog.Duels.Count} duels, newest first.\n\nClick for one duel at a time, right-click goes back.";
                x += w;
            }
            foreach (var (label, remove) in active)
            {
                float w = Mathf.Min(150, label.Length * 7f + 26);
                var rt = Box(_activeContent, x, 0, w - 4, 26);
                Ui.Image(rt, T.Chip);
                var t = Ui.Text(rt, $"{label} ×", 12, T.Text);
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = 9;
                t.resizeTextMaxSize = 12;
                Ui.Clickable(rt, () => { remove(); RunSearch(); });
                x += w;
            }
            // Wider than the window: it scrolls, keeping where it was (clamped to the new width).
            _activeContent.sizeDelta = new Vector2(Mathf.Max(300, x), 26);
            _activeContent.anchoredPosition = new Vector2(Mathf.Clamp(keptX, -(Mathf.Max(300, x) - 300), 0), 0);
            ShowActiveEnds();
            for (int i = 0; i < 4; i++) _pickLabels[i].text = options[i][_picks[i]].label;
            _pickLabels[4].text = _picks[4] == 0 ? "Sort" : $"Sort: {SortOptions[_picks[4]]}";
            _ownedToggle.SetActive(true);
            _ownedLabel.text = _scope switch
            {
                ScopeFavorites => "★ Favorites",
                ScopeOwned => "✓ Owned only",
                ScopeSeen => "Seen in duels",
                ScopeViewed => "Recently viewed",
                _ => "All cards",
            };
            _modeLabel.text = $"<size=10>LOOKS IN</size>\n{ModeLabel(_searchMode)}";
            ShowMoreButton();
        }

        private static string ModeLabel(CardSearch.SearchMode mode) => mode switch
        {
            CardSearch.SearchMode.NameOnly => "Name",
            CardSearch.SearchMode.TextOnly => "Text",
            _ => "Name + text",
        };

        private void CycleMode(int delta)
        {
            _searchMode = (CardSearch.SearchMode)(((int)_searchMode + delta + 3) % 3);
            RunSearch();
        }

        private void AddZone(Zone zone, float x, float y, float w, float h, Color idle)
        {
            var rt = Box(_root, x, y, w, h);
            var image = Ui.Image(rt, idle);
            image.raycastTarget = false;
            _zones.Add((zone, rt, image));
        }

        private void BuildGhost()
        {
            var T = UiTheme.Current;
            // The search panel is a drop zone too ("remove from deck"), drawn over the results while dragging.
            AddZone(Zone.Search, 1400, 0, 520, 1080, new Color(0, 0, 0, 0));
            _ghost = Ui.Rect("DragGhost", _root);
            _ghost.anchorMin = _ghost.anchorMax = _ghost.pivot = new Vector2(0.5f, 0.5f);
            _ghost.sizeDelta = new Vector2(74, 108);
            _ghostFrame = Ui.Image(_ghost, T.Highlight);
            _ghostFrame.raycastTarget = false;
            var face = Ui.Stretched("Face", _ghost, Vector2.zero, Vector2.one);
            face.offsetMin = new Vector2(3, 3);
            face.offsetMax = new Vector2(-3, -3);
            _ghostArt = face.gameObject.AddComponent<RawImage>();
            _ghostArt.raycastTarget = false;
            _ghostName = Ui.Text(Ui.Stretched("Name", face, Vector2.zero, Vector2.one), "", 11, Color.white);
            _ghost.gameObject.SetActive(false);
        }

        private void BeginDrag(uint code, Vector2 screen)
        {
            var info = _cards.Get(code);
            var art = CardView.Art?.Get(code);
            _ghostArt.texture = art;
            _ghostArt.color = art != null ? Color.white : KindColor(info?.Type ?? 0);
            _ghostName.text = art != null ? "" : info?.Name ?? "";
            _ghost.gameObject.SetActive(true);
            _ghost.SetAsLastSibling();
            MoveDrag(screen, fromDeck: false);
        }

        private void MoveDrag(Vector2 screen, bool fromDeck)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out var local);
            _ghost.anchoredPosition = local;
            var hot = ZoneAt(screen);
            foreach (var (zone, _, image) in _zones)
            {
                if (zone == Zone.Search) image.color = fromDeck && hot == Zone.Search ? RemoveHot : new Color(0, 0, 0, 0);
                else image.color = zone == hot ? ZoneHot : ZoneIdle;
            }
        }

        private Zone EndDrag(Vector2 screen)
        {
            _ghost.gameObject.SetActive(false);
            foreach (var (zone, _, image) in _zones) image.color = zone == Zone.Search ? new Color(0, 0, 0, 0) : ZoneIdle;
            return ZoneAt(screen);
        }

        private Zone ZoneAt(Vector2 screen)
        {
            foreach (var (zone, rect, _) in _zones)
                if (RectTransformUtility.RectangleContainsScreenPoint(rect, screen, null)) return zone;
            return Zone.None;
        }

        /// <summary>A search result dropped on the deck: Main/Extra add it where it belongs, Side adds it to the Side Deck.</summary>
        private void DropResult(CardInfo info, Zone zone)
        {
            if (zone == Zone.Main || zone == Zone.Extra) Add(info, side: false);
            else if (zone == Zone.Side) Add(info, side: true);
        }

        /// <summary>A deck card dropped somewhere: move between Main/Extra and Side, or remove it on the search panel.</summary>
        private void DropDeckCard(DeckSection from, int index, Zone zone)
        {
            if (zone == Zone.Search)
            {
                var name = _cards.Get(Section(_deck, from)[index])?.Name ?? "card";
                Change(d => Section(d, from).RemoveAt(index));
                SetStatus($"Removed {name}.");
            }
            else if (zone == Zone.Side && from != DeckSection.Side) MoveSection(from, index);
            else if ((zone == Zone.Main || zone == Zone.Extra) && from == DeckSection.Side) MoveSection(from, index);
        }

        private void BuildTestHand()
        {
            var T = UiTheme.Current;
            _handOverlay = Box(_root, 410, 150, 980, 420);
            Ui.Image(_handOverlay, T.GlassSoft.WithAlpha(0.97f));
            _handLabel = Label(20, 12, 600, 30, "", 18, TextAnchor.MiddleLeft, _handOverlay);
            _handCards = Box(_handOverlay, 0, 50, 980, 280);
            Button(20, 360, 160, 40, "Draw again", () => ShowTestHand(false), _handOverlay);
            Button(190, 360, 220, 40, "Going first / second", () => { _testHandSecond = !_testHandSecond; ShowTestHand(false); }, _handOverlay);
            Button(840, 360, 120, 40, "Close", () => _handOverlay.gameObject.SetActive(false), _handOverlay);
            _handOverlay.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ Deck changes

        /// <summary>Applies an edit with undo support.</summary>
        private void Change(Action<Deck> edit)
        {
            _undo.Push(_deck.Clone());
            _redo.Clear();
            edit(_deck);
            MarkDirty();
            RefreshDeck();
        }

        private void Add(CardInfo info, bool side)
        {
            info = Resolve(info);
            Viewed(info); // picked, so it's one you looked at (Recently viewed)
            int copies = CopiesInDeck(info.Code);
            if (copies >= 3) { SetStatus($"You already have 3 copies of {info.Name}."); return; }
            if (OwnedCards != null && copies >= OwnedCards.Count(Banlist.BaseCode(info.Code, _cards)))
            {
                var own = OwnedCards.Count(Banlist.BaseCode(info.Code, _cards));
                SetStatus(own == 0 ? $"You don't own {info.Name} yet. Get it {WhereToGet?.Invoke(Banlist.BaseCode(info.Code, _cards)) ?? "in the Card Shop"}."
                    : $"You own {own} {info.Name} and they're all in the deck.");
                return;
            }
            var section = side ? DeckSection.Side : DeckRules.HomeSection(info);
            var list = Section(_deck, section);
            int max = section == DeckSection.Main ? DeckRules.MainMax : section == DeckSection.Extra ? DeckRules.ExtraMax : DeckRules.SideMax;
            if (list.Count >= max) { SetStatus($"The {section} Deck is full ({max} cards)."); return; }
            Change(d => Section(d, section).Add(info.Code));
            SetStatus($"Added {info.Name} to the {section} Deck.");
        }

        private void RemoveOne(uint code)
        {
            var key = Banlist.BaseCode(code, _cards);
            foreach (var section in new[] { DeckSection.Main, DeckSection.Extra, DeckSection.Side })
            {
                var index = Section(_deck, section).FindLastIndex(c => Banlist.BaseCode(c, _cards) == key);
                if (index < 0) continue;
                Change(d => Section(d, section).RemoveAt(index));
                return;
            }
        }

        /// <summary>Right-click on a deck card: Main/Extra -> Side, Side -> back to where the card belongs.</summary>
        private void MoveSection(DeckSection from, int index)
        {
            var code = Section(_deck, from)[index];
            var info = _cards.Get(code);
            var to = from == DeckSection.Side ? (info != null ? DeckRules.HomeSection(info) : DeckSection.Main) : DeckSection.Side;
            Change(d =>
            {
                Section(d, from).RemoveAt(index);
                Section(d, to).Add(code);
            });
        }

        private static List<uint> Section(Deck d, DeckSection s) => s == DeckSection.Main ? d.Main : s == DeckSection.Extra ? d.Extra : d.Side;

        private int CopiesInDeck(uint code)
        {
            var key = Banlist.BaseCode(code, _cards);
            return _deck.Main.Concat(_deck.Extra).Concat(_deck.Side).Count(c => Banlist.BaseCode(c, _cards) == key);
        }

        private void Undo()
        {
            if (_undo.Count == 0) { SetStatus("Nothing to undo."); return; }
            _redo.Push(_deck.Clone());
            _deck = _undo.Pop();
            MarkDirty();
            RefreshDeck();
        }

        private void Redo()
        {
            if (_redo.Count == 0) { SetStatus("Nothing to redo."); return; }
            _undo.Push(_deck.Clone());
            _deck = _redo.Pop();
            MarkDirty();
            RefreshDeck();
        }

        private void ToggleStarter()
        {
            if (_selected == null) return;
            var key = Banlist.BaseCode(_selected.Code, _cards);
            Change(d => { if (!d.Starters.Remove(key)) d.Starters.Add(key); });
            ShowDetail(_selected);
        }

        // ------------------------------------------------------------------ Files

        private void NewDeck()
        {
            _deck = new Deck { Name = OwnedCards != null ? "my campaign deck" : "new deck", Campaign = OwnedCards != null };
            RememberVersions();
            _undo.Clear();
            _redo.Clear();
            _dirty = false;
            _rebuilt = false;
            ShowName(_deck.Name);
            _labelField.SetTextWithoutNotify("");
            RefreshDeck();
            SetStatus("New deck. Search on the right and click cards to add them.");
        }

        /// <summary>Shows a deck's name in the name field, small enough to read whole ("... (new)" isn't cut off).</summary>
        private void ShowName(string name)
        {
            _nameField.SetTextWithoutNotify(name);
            FitName(true);
        }

        /// <summary>The name field's font: 17, or smaller (down to 11) when the name is wider than the field.</summary>
        private void FitName(bool redraw)
        {
            var text = _nameField.textComponent;
            float width = ((RectTransform)text.transform).rect.width;
            var generator = new TextGenerator();
            int size = 17;
            for (; size > 11 && width > 0; size--)
            {
                text.fontSize = size;
                if (generator.GetPreferredWidth(_nameField.text, text.GetGenerationSettings(Vector2.zero)) / text.pixelsPerUnit <= width) break;
            }
            text.fontSize = size;
            if (redraw) _nameField.ForceLabelUpdate();
        }

        /// <summary>The versions a deck uses become the ones added next.</summary>
        private void RememberVersions()
        {
            _preferred.Clear();
            _preferredArt.Clear();
            foreach (var code in _deck.Main.Concat(_deck.Extra).Concat(_deck.Side))
            {
                var key = Banlist.BaseCode(code, _cards);
                if (code == key) continue;
                _preferred[key] = code;
                if (_cards.Get(code) is CardInfo info && CardVersions.KindOf(info) == CardVersions.TextKind.Current) _preferredArt[key] = code;
            }
        }

        private void LoadDeck(string name)
        {
            _deck = _library.Load(name);
            _deck.Name = name;
            RememberVersions();
            _undo.Clear();
            _redo.Clear();
            _dirty = false;
            _rebuilt = false;
            ShowName(name);
            _labelField.SetTextWithoutNotify(_deck.Note("label"));
            _openIndex = Math.Max(0, OpenableDecks.IndexOf(name));
            // A historic deck opens under the ban list of its era.
            var era = _banlists.FindIndex(b => b.Name == _deck.Note("banlist"));
            if (era >= 0) _banIndex = era;
            RefreshDeck();
            SetStatus(_deck.Campaign && OwnedCards == null
                ? $"Opened campaign deck {name}. You can play it anywhere; saving here makes a free copy (edit the original in the campaign)."
                : _library.IsOwn(name) ? $"Opened {name}." : $"Opened built-in deck {name}. Saving makes your own copy in mods/decks.");
        }

        /// <summary>A deck saved under a new name with "Save new &amp; play it" (the campaign makes it your deck).</summary>
        public event Action<string>? SavedAsNew;

        private RectTransform? _savePrompt;
        /// <summary>Recipe, Build or Import made a whole new deck since it was opened or saved: Save asks how to save.</summary>
        private bool _rebuilt;

        /// <summary>
        /// Save. When it would replace a saved deck with a new one (after Recipe, Build or Import, or when most of its cards
        /// changed), it asks first: overwrite that deck, or save these cards as a new one and keep it. Early in a campaign
        /// Build reuses most of your few cards, so the share of cards replaced alone doesn't tell.
        /// </summary>
        private void Save()
        {
            CloseSavePrompt();
            var name = DeckLibrary.SafeName(string.IsNullOrWhiteSpace(_deck.Name) ? "new deck" : _deck.Name);
            var saved = _library.Contains(name) && _library.IsOwn(name) ? _library.Load(name) : null;
            // Nothing changed but at most the title (Recipe on a deck that already is that recipe, starters and notes
            // included): say so, and make no copy. Anything else Recipe, Build or Import changed (they replace your starters
            // and story notes) still asks first.
            if (saved != null && OfThisKind(name) && Contents(saved) == Contents(_deck)) { NothingToSave(name, saved); return; }
            if (saved != null && (_rebuilt || Replaced(saved, _deck) > 0.5)) AskHowToSave(name);
            else SaveNow();
        }

        /// <summary>
        /// A saved deck this editor saves over: in the campaign a campaign deck, outside it a free deck of yours (saving
        /// anything else makes a copy on purpose: a free deck in the campaign, a campaign deck outside it).
        /// </summary>
        private bool OfThisKind(string name) => _library.IsOwn(name) && _library.IsCampaign(name) == (OwnedCards != null);

        /// <summary>What makes two decks the same deck: their cards (in any order), starters and notes, not name or title.</summary>
        private static string Contents(Deck deck)
        {
            var d = deck.Clone();
            d.Name = "";
            d.Campaign = false;
            d.Notes.Remove("title");
            d.Main.Sort();
            d.Extra.Sort();
            d.Side.Sort();
            d.Starters = d.Starters.Distinct().OrderBy(c => c).ToList();
            return DeckCodes.ToYdk(d);
        }

        /// <summary>A saved deck of yours (of the kind this editor saves) that already is this deck, or null.</summary>
        private string? SavedCopyOf(Deck deck)
        {
            var contents = Contents(deck);
            return _library.Names.Where(OfThisKind).FirstOrDefault(n => Contents(_library.Load(n)) == contents);
        }

        /// <summary>Save with nothing changed: the deck stays as saved (its own title, not a Recipe's), and the status says so.</summary>
        private void NothingToSave(string name, Deck saved)
        {
            if (saved.Note("title").Length > 0) _deck.Notes["title"] = saved.Note("title");
            else _deck.Notes.Remove("title");
            _deck.Name = name;
            ShowName(name);
            _dirty = false;
            _rebuilt = false;
            ClearRecovery();
            if (_recoveryBar != null) { Destroy(_recoveryBar.gameObject); _recoveryBar = null; }
            RefreshDeck();
            SetStatus($"Nothing to save: {name} is already saved with these cards.");
        }

        /// <summary>The share of <paramref name="saved"/>'s Main and Extra Deck that <paramref name="now"/> no longer has.</summary>
        private static double Replaced(Deck saved, Deck now)
        {
            var left = saved.Main.Concat(saved.Extra).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
            int total = left.Values.Sum(), kept = 0;
            foreach (var code in now.Main.Concat(now.Extra))
                if (left.TryGetValue(code, out var n) && n > 0) { left[code] = n - 1; kept++; }
            return total == 0 ? 0 : 1 - (double)kept / total;
        }

        private void AskHowToSave(string name)
        {
            var T = UiTheme.Current;
            var suggestion = _deck.Note("title").Length > 0 && DeckLibrary.SafeName(_deck.Note("title")) != name ? _deck.Note("title") : $"{name} (new)";
            for (int n = 2; _library.Contains(DeckLibrary.SafeName(suggestion)); n++) suggestion = $"{(_deck.Note("title").Length > 0 ? _deck.Note("title") : name)} ({n})";
            var bar = Box(_root, 406, 92, 998, 40);
            _savePrompt = bar;
            Ui.Image(bar, T.NoticeBar);
            // In the campaign, saving as new asks one more thing: keep playing your deck, or play the new one.
            bool campaign = OwnedCards != null;
            Label(10, 0, 240, 40, $"This replaces most of <b>{name}</b>.", 13, parent: bar).supportRichText = true;
            Button(256, 5, 116, 30, "Overwrite it", () => { CloseSavePrompt(); SaveNow(); }, bar);
            var field = Ui.Input(Box(bar, 380, 5, campaign ? 200 : 370, 30), "new deck name", 14);
            field.text = suggestion;
            Button(campaign ? 588 : 758, 5, 130, 30, "Save as new", () => SaveAsNew(field.text, play: false), bar);
            if (campaign) Button(726, 5, 166, 30, "Save new & play it", () => SaveAsNew(field.text, play: true), bar);
            Button(900, 5, 90, 30, "Cancel", CloseSavePrompt, bar);
            SetStatus(campaign
                ? $"Overwrite {name}, or save these cards as a new deck and keep {name}: Save as new keeps your campaign deck as it is, Save new & play it switches to the new one."
                : $"Overwrite {name} with these cards, or save them as a new deck and keep {name} as it was.");
        }

        private void CloseSavePrompt()
        {
            if (_savePrompt != null) Destroy(_savePrompt.gameObject);
            _savePrompt = null;
        }

        /// <summary>
        /// The cards as a new deck; the deck they came from stays as it was saved. The new deck's title is the name you
        /// gave it, so it never reads the same as the old one. With <paramref name="play"/> (the campaign) it becomes the
        /// deck you play; otherwise your campaign deck stays as it was.
        /// </summary>
        private void SaveAsNew(string typed, bool play)
        {
            var from = _deck.Name;
            // These cards are already one of your decks: open that one (and play it) instead of saving a copy of it.
            if (SavedCopyOf(_deck) is string same)
            {
                CloseSavePrompt();
                LoadDeck(same);
                if (play && OwnedCards != null) SavedAsNew?.Invoke(same);
                SetStatus(play && OwnedCards != null ? $"These cards are already saved as {same}: no copy made. {same} is now your campaign deck; {from} is kept as it was."
                    : $"These cards are already saved as {same}: no copy made, and {from} is kept as it was.");
                return;
            }
            var title = typed.Trim().Length > 0 ? typed.Trim() : $"{_deck.Name} (new)";
            var name = DeckLibrary.SafeName(title);
            if (_library.Contains(name)) { SetStatus($"There's already a deck called {name}: pick another name."); return; }
            CloseSavePrompt();
            _deck.Name = name;
            // A copied title (a recipe's "Stall Burn") would show both decks under one name in the campaign and duels.
            _deck.Notes["title"] = title;
            ShowName(name);
            SaveNow();
            if (_deck.Name != name)
            {
                // Saving renamed it (a free deck already had the name): the title follows.
                _deck.Notes["title"] = _deck.Name;
                _library.Save(_deck);
            }
            if (play && OwnedCards != null) SavedAsNew?.Invoke(_deck.Name);
            SetStatus(play && OwnedCards != null ? $"Saved as {_deck.Name}, now your campaign deck; {from} is kept as it was."
                : OwnedCards != null ? $"Saved as {_deck.Name}; {from} is kept, and your campaign deck hasn't changed. To play {_deck.Name}, pick it with < > under your deck on the campaign screen."
                : $"Saved as {_deck.Name}; {from} is kept as it was.");
        }

        private void SaveNow()
        {
            if (string.IsNullOrWhiteSpace(_deck.Name)) _deck.Name = "new deck";
            _deck.Name = DeckLibrary.SafeName(_deck.Name);
            if (OwnedCards != null)
            {
                // Campaign decks are marked as such, and never overwrite a free deck of the same name.
                _deck.Campaign = true;
                while (_library.Contains(_deck.Name) && !_library.IsCampaign(_deck.Name)) _deck.Name += " (campaign)";
            }
            else if (_deck.Campaign)
            {
                // Outside the campaign a campaign deck is a recipe: saving changes makes a free copy.
                _deck.Campaign = false;
                while (_library.IsCampaign(_deck.Name)) _deck.Name += " (copy)";
            }
            ShowName(_deck.Name);
            _library.Save(_deck);
            _dirty = false;
            _rebuilt = false;
            ClearRecovery();
            if (_recoveryBar != null) { Destroy(_recoveryBar.gameObject); _recoveryBar = null; }
            _openIndex = Math.Max(0, OpenableDecks.IndexOf(_deck.Name));
            RefreshDeck();
            SetStatus($"Saved to {System.IO.Path.Combine(_library.OwnFolder, _deck.Name + ".ydk")}");
        }

        private void Delete()
        {
            if (!_library.IsOwn(_deck.Name)) { SetStatus("Built-in decks can't be deleted (only your own decks in mods/decks)."); return; }
            if (_library.IsCampaign(_deck.Name) && OwnedCards == null) { SetStatus("Campaign decks are deleted from the campaign (Campaign > Edit deck)."); return; }
            Confirmed("delete", () =>
            {
                var name = _deck.Name;
                _library.Delete(name);
                NewDeck();
                SetStatus($"Deleted {name}.");
            }, $"Delete {_deck.Name}? Click Delete again to confirm.");
        }

        /// <summary>Recipe picked a deck: its name and the copies of each card it uses (for the shop's wishlist).</summary>
        public event Action<string, Dictionary<uint, int>>? WishlistChanged;
        /// <summary>The decks the Recipe button may use (the campaign: recipes you've unlocked); null: every built-in deck.</summary>
        public Func<List<string>>? RecipeDecks;
        /// <summary>
        /// The campaign, after Recipe set the wishlist: pays what was held for a Recipe (Gus's owed bounties) and says what
        /// it paid ("" for nothing), shown after the Recipe's status.
        /// </summary>
        public Func<string>? PayOwed;

        private List<(Deck recipe, double owned)>? _recipes;
        /// <summary>When Recipe last said none is ready: whether none was unlocked, and your collection's size then.</summary>
        private (bool noneUnlocked, int owned)? _noRecipeSaid;
        private int _recipeIndex;

        /// <summary>
        /// Campaign: the known decks (historic lists and built-in recipes) you own most of, one per click, built from your
        /// collection with the gaps filled.
        /// </summary>
        private void NextRecipe()
        {
            if (OwnedCards == null) return;
            var list = _banlists[_banIndex];
            EnsureRecipes(list);
            if (_recipes!.Count == 0)
            {
                bool noneUnlocked = RecipeDecks != null && RecipeDecks().Count == 0;
                // Said once: pressing Recipe again with the same collection (this visit or a later one) gets a short line,
                // not the same paragraph again.
                var said = (noneUnlocked, OwnedCards.Total);
                bool again = _noRecipeSaid == said;
                _noRecipeSaid = said;
                SetStatus(noneUnlocked
                    ? again ? "Still no recipes: the Recipe Book unlocks a duelist's deck when you beat them."
                        : "No recipes yet: beat a duelist in the circuits to unlock their deck in the Recipe Book."
                    : again ? "Still none ready: you don't own enough of any unlocked recipe yet. The Recipe Book sells the missing cards."
                        : "You don't own enough of any unlocked recipe yet (a quarter of its cards). The Recipe Book sells recipe packs of the decks you've unlocked, the cards you're missing first.");
                return;
            }
            var (recipe, owned) = _recipes[_recipeIndex % _recipes.Count];
            _recipeIndex++;
            ApplyRecipe(recipe, owned, list);
        }

        /// <summary>The recipes the Recipe button cycles (yours first, then the unlocked ones you own a quarter of), made once per visit.</summary>
        private void EnsureRecipes(Banlist list)
        {
            if (OwnedCards == null) { _recipes = new List<(Deck, double)>(); return; }
            if (_recipes == null)
            {
                var names = RecipeDecks?.Invoke() ?? _library.Names.Where(n => !_library.IsCampaign(n) && !_library.IsOwn(n)).ToList();
                var known = names.Where(_library.Contains).Select(n => { var d = _library.Load(n); d.Name = n; return d; });
                // Your own recipes (imported .ydk files) come first, however little of them you own: they're your plan.
                var mine = (MyRecipeDecks?.Invoke() ?? new List<Deck>()).Where(r => r.Main.Count + r.Extra.Count > 0)
                    .Select(r => (recipe: r, owned: AutoDeck.Owned(r, OwnedCards, list, _cards, Released))).OrderByDescending(r => r.owned).ToList();
                _recipes = mine.Concat(AutoDeck.BestRecipes(known, OwnedCards, list, _cards, 0.25, Released)).ToList();
                _recipeIndex = 0;
            }
        }

        /// <summary>A recipe built from your collection into the deck (Undo reverts), its missing cards set as the wishlist.</summary>
        private void ApplyRecipe(Deck recipe, double owned, Banlist list)
        {
            if (OwnedCards == null || _recipes == null || _recipes.Count == 0) return;
            bool yours = MyRecipes.IsMine(recipe.Name);
            var result = AutoDeck.FromRecipe(recipe, OwnedCards, list, _cards, Released);
            Change(d =>
            {
                d.Main = result.Deck.Main;
                d.Extra = result.Deck.Extra;
                d.Side = result.Deck.Side;
                // The recipe's story and combo starters, not the old deck's (its Ban Passes and label stay).
                foreach (var key in StoryNotes)
                {
                    d.Notes.Remove(key);
                    if (recipe.Note(key).Length > 0) d.Notes[key] = recipe.Note(key);
                }
                // Named after the recipe ("Stall Burn"), which Save as new offers as the new deck's name.
                if (!d.Notes.ContainsKey("title")) d.Notes["title"] = DeckTitle(recipe.Name);
                d.Starters = recipe.Starters.ToList();
            });
            _rebuilt = true;
            var next = _recipes[_recipeIndex % _recipes.Count];
            int missing = AutoDeck.Missing(recipe, OwnedCards, list, _cards).Count;
            WishlistChanged?.Invoke(recipe.Name, AutoDeck.Wants(recipe, list, _cards));
            var cuts = result.Changes.Where(c => c.StartsWith("Cut ")).ToList();
            SetStatus($"{DeckTitle(recipe.Name)}{(yours ? " (your recipe)" : "")}: you own {Math.Round(owned * 100)}% of it; {result.Changes.Count - 1} changes to fill the gaps (Undo to revert)."
                      + (cuts.Count > 0 ? " " + string.Join(". ", cuts) + "." : "")
                      + (missing > 0 ? $" Its {missing} missing cards are on your wishlist in the Card Shop." : "")
                      + $" Click Recipe again for {DeckTitle(next.recipe.Name)} ({Math.Round(next.owned * 100)}%).");
            if (PayOwed?.Invoke() is { Length: > 0 } paid) SetStatus(_status.text + "  " + paid);
        }

        /// <summary>For automated checks: presses Recipe and reports the problems left.</summary>
        public int DebugRecipe(out string status)
        {
            NextRecipe();
            status = _status.text;
            return DeckRules.Validate(_deck, _banlists[_banIndex], _cards).Count
                   + (OwnedCards != null ? CollectionRules.Validate(_deck, OwnedCards, _cards).Count : 0);
        }

        /// <summary>
        /// For automated play: presses Build (a deck from your collection); the status line after. A legal deck asks
        /// first: then it presses Build again, as a player who reads the warning and builds anyway, and returns both lines.
        /// </summary>
        public string DebugBuild()
        {
            BuildFromCollection();
            if (_confirm != "build") return _status.text;
            var asked = _status.text;
            BuildFromCollection();
            return $"{asked} / {_status.text}";
        }

        /// <summary>For automated checks: presses Auto-fix and reports the problems left (ban list and collection).</summary>
        public int DebugAutoFix(out string status)
        {
            AutoFix();
            status = _status.text;
            return Problems();
        }

        /// <summary>
        /// For automated play: what a player does when a circuit's ban list stops the deck. Selects each card over its
        /// limit and presses + Pass while passes last, then Auto-fix for anything still over, then Save. The status line
        /// after each press, and the problems left (as the problems box counts them).
        /// </summary>
        public (List<string> steps, int problems) DebugPassAndSave()
        {
            var steps = new List<string>();
            var keys = _deck.Main.Concat(_deck.Extra).Concat(_deck.Side).Select(c => Banlist.BaseCode(c, _cards)).Distinct().ToList();
            foreach (var key in keys)
            {
                var info = _cards.Get(key);
                if (info == null) continue;
                while (CopiesInDeck(key) > EffectiveList.Allowed(key, _cards))
                {
                    ShowDetail(info);
                    int before = BanPasses.Of(_deck).Count;
                    AddPass();
                    steps.Add($"+ Pass on {info.Name}: {_status.text}");
                    if (BanPasses.Of(_deck).Count == before) break;
                }
            }
            if (Problems() > 0)
            {
                AutoFix();
                steps.Add($"Auto-fix: {_status.text}");
            }
            Save();
            if (_savePrompt != null)
            {
                steps.Add($"Save asked: {_status.text}");
                // The QA player saves as new and plays the new deck (Save new & play it).
                var field = _savePrompt.GetComponentInChildren<InputField>();
                SaveAsNew(field != null ? field.text : "", play: true);
            }
            steps.Add($"Save: {_status.text}");
            return (steps, Problems());
        }

        /// <summary>
        /// The deck's problems as the problems box counts them: the ban list with your passes, your collection, and (an
        /// era-limited campaign) cards not out yet in the list's format.
        /// </summary>
        private int Problems() => DeckRules.Validate(_deck, EffectiveList, _cards).Count
                                  + (OwnedCards != null ? CollectionRules.Validate(_deck, OwnedCards, _cards).Count : 0)
                                  + (Released != null ? DeckRules.Releases(_deck, _banlists[_banIndex], PoolEnd(_banlists[_banIndex]), CardFoil.Printings, _cards).Count : 0);

        /// <summary>An era-limited campaign: whether a card was out in the ban list's format (null when there's no such limit).</summary>
        private Func<uint, bool>? Released
        {
            get
            {
                if (OwnedCards == null || (EraLimited?.Invoke() == false && Format == null)) return null;
                var end = PoolEnd(_banlists[_banIndex]);
                if (end.Length == 0 || CardFoil.Printings == null) return null;
                return code => DeckRules.Released(code, end, CardFoil.Printings, _cards);
            }
        }

        /// <summary>
        /// For automated checks: empties the deck's Main and Extra Deck (most of it replaced), then Save: whether Save
        /// asked how to save, and the deck's name after Save as new with the name it offers.
        /// </summary>
        public (bool asked, string saved) DebugReplaceAndSaveAsNew()
        {
            Change(d => { d.Main.Clear(); d.Extra.Clear(); });
            Save();
            bool asked = _savePrompt != null;
            if (asked) SaveAsNew(_savePrompt!.GetComponentInChildren<InputField>()?.text ?? "", play: true);
            return (asked, _deck.Name);
        }

        /// <summary>For automated checks: Save with nothing changed. Whether it asked how to save, and the status line.</summary>
        public (bool asked, string status) DebugSaveUnchanged()
        {
            Save();
            bool asked = _savePrompt != null;
            CloseSavePrompt();
            return (asked, _status.text);
        }

        /// <summary>
        /// For automated checks: opens a saved deck, marks its first card a combo starter and saves; then a rebuild that
        /// keeps every card but drops the starter (as Build does, or a Recipe with its own starters) and Save. Whether that
        /// Save asked, and the starters the saved file still has. The deck is reopened as saved after.
        /// </summary>
        public (bool asked, int starters) DebugRebuildKeepsStarters(string name)
        {
            LoadDeck(name);
            if (_deck.Main.Count == 0) return (false, 0);
            Change(d => d.Starters = new List<uint> { d.Main[0] });
            Save();
            Change(d => d.Starters.Clear());
            _rebuilt = true;
            Save();
            bool asked = _savePrompt != null;
            CloseSavePrompt();
            LoadDeck(name);
            return (asked, _deck.Starters.Count);
        }

        /// <summary>For automated checks: opens a saved deck, then as <see cref="DebugReplaceAndSaveAsNew"/>; the status line too.</summary>
        public (bool asked, string saved, string status) DebugOpenReplaceAndSaveAsNew(string name)
        {
            LoadDeck(name);
            var (asked, saved) = DebugReplaceAndSaveAsNew();
            return (asked, saved, _status.text);
        }

        /// <summary>Campaign: drops cards the ban list or your collection doesn't allow and refills from your best spare cards.</summary>
        private void AutoFix()
        {
            if (OwnedCards == null) return;
            var result = AutoDeck.Fix(_deck, OwnedCards, EffectiveList, _cards, Released);
            if (result.Changes.Count == 0) { SetStatus($"Nothing to fix: the deck is legal under {_banlists[_banIndex].Name} with cards you own."); return; }
            Change(d => { d.Main = result.Deck.Main; d.Extra = result.Deck.Extra; d.Side = result.Deck.Side; });
            SetStatus($"Auto-fix (Undo to revert): {string.Join("; ", result.Changes.Take(6))}{(result.Changes.Count > 6 ? "; ..." : "")}");
        }

        /// <summary>A deck's own name and story (era, tier, how it worked...): what Recipe and Build replace.</summary>
        private static readonly string[] StoryNotes = { "era", "tier", "about", "why", "source", "title" };

        /// <summary>
        /// Campaign: a whole new deck from your collection, built around the selected card if you own it. A full, legal
        /// deck asks first (a second click builds), and the status line says what the new deck gives up against it.
        /// </summary>
        private void BuildFromCollection()
        {
            var T = UiTheme.Current;
            if (OwnedCards == null) return;
            bool ready = _deck.Main.Count >= DeckRules.MainMin && Problems() == 0;
            if (ready && _confirm != "build")
            {
                _confirm = "build";
                SetStatus($"{_deck.Name} is legal and ready. Build replaces it with a deck from your whole collection, often weaker. Click Build again to build (Undo brings it back).");
                return;
            }
            _confirm = null;
            var was = _deck.Clone();
            uint? core = _selected != null && OwnedCards.Owns(Banlist.BaseCode(_selected.Code, _cards)) ? Banlist.BaseCode(_selected.Code, _cards) : (uint?)null;
            var result = AutoDeck.Build(OwnedCards, _banlists[_banIndex], _cards, core, _deck.Name, Released);
            Change(d =>
            {
                d.Main = result.Deck.Main;
                d.Extra = result.Deck.Extra;
                d.Side.Clear();
                // A new deck: the old one's name, story and combo starters don't describe it (its Ban Passes and label stay).
                foreach (var key in StoryNotes) d.Notes.Remove(key);
                d.Starters.Clear();
            });
            _rebuilt = true;
            var built = core != null ? $"Built around {_cards.Get(core.Value)?.Name}" : "Built from your collection";
            var loss = GivesUp(was, _deck);
            SetStatus(loss.Length > 0
                ? $"{built}: <color={T.WarnHex}>weaker than {was.Name}? {loss}.</color> Undo brings {was.Name} back; Save keeps this one."
                : $"{built} (Undo to revert): {result.Changes[0]}. Save to keep it.");
        }

        /// <summary>
        /// What a rebuilt deck gives up against the deck it replaced, in plain words: fewer of its archetype's key cards,
        /// Main Deck slots on cards from outside its plan (neither in it before nor in its archetype), its Side Deck.
        /// "" when the old deck was nearly empty or nothing stands out.
        /// </summary>
        private string GivesUp(Deck was, Deck now)
        {
            if (was.Main.Count < DeckRules.MainMin / 2) return "";
            var parts = new List<string>();
            var arch = _archetypes.Main(was, _cards);
            if (arch is { } a)
            {
                _references ??= _library.Names.Where(n => !_library.IsOwn(n)).Select(_library.Load).ToList();
                var keys = DeckAnalysis.KeyCards(a.code, _references, _cards);
                int Keys(Deck d) { var used = new HashSet<uint>(d.Main.Concat(d.Extra).Select(c => Banlist.BaseCode(c, _cards))); return keys.Count(used.Contains); }
                int before = Keys(was), after = Keys(now);
                if (after < before) parts.Add($"{after} of {a.name}'s key cards (was {before})");
            }
            var plan = new HashSet<uint>(was.Main.Concat(was.Extra).Select(c => Banlist.BaseCode(c, _cards)));
            var off = now.Main.Where(c => !plan.Contains(Banlist.BaseCode(c, _cards))
                                          && !(arch is { } b && _cards.Get(c) is CardInfo info && Archetypes.IsIn(info, b.code))).ToList();
            if (off.Count >= 4)
                parts.Add($"{off.Count} Main Deck slots on off-plan cards ({string.Join(", ", off.Distinct().Take(3).Select(c => _cards.Get(c)?.Name))}{(off.Distinct().Count() > 3 ? "..." : "")})");
            if (was.Side.Count > 0 && now.Side.Count == 0) parts.Add($"no Side Deck (was {was.Side.Count})");
            return string.Join("; ", parts);
        }

        private void Import()
        {
            var text = GUIUtility.systemCopyBuffer ?? "";
            var deck = DeckCodes.Parse(text, _deck.Name);
            if (deck == null) { SetStatus("The clipboard doesn't hold a YDKE link or .ydk text."); return; }
            Change(d => { d.Main = deck.Main; d.Extra = deck.Extra; d.Side = deck.Side; if (deck.Starters.Count > 0) d.Starters = deck.Starters; });
            _rebuilt = true;
            SetStatus($"Imported {deck.Main.Count} / {deck.Extra.Count} / {deck.Side.Count} cards from the clipboard.");
        }

        // ------------------------------------------------------------------ .ydk files (docs/dev/YDK-IMPORT-2026-11-07.md)

        /// <summary>The campaign: keeps a .ydk file as one of your recipes; its name (null: not imported) and the line to show.</summary>
        public Func<string, (string? name, string status)>? ImportRecipe;
        /// <summary>The campaign: your recipes (imported .ydk files), for the Recipe button.</summary>
        public Func<List<Deck>>? MyRecipeDecks;

        /// <summary>Import .ydk: macOS's open panel, then the file imported (nothing when it's cancelled).</summary>
        private void PickYdkFile()
        {
            var path = YdkFilePicker.Pick(out var refused);
            if (path == null)
            {
                if (refused != null) SetStatus($"Couldn't open the file dialog: {refused}. Import still reads a YDKE link or .ydk text from the clipboard.");
                return;
            }
            ImportYdkFile(path);
        }

        /// <summary>
        /// A .ydk file imported from a path (the button's, or a check's). Free play: it becomes the deck in the editor, named
        /// from the file, and Save asks how to save (like Import). The campaign: it's kept as one of your recipes, then built
        /// from your collection as Recipe builds one. Returns whether anything was imported.
        /// </summary>
        public bool ImportYdkFile(string path)
        {
            if (OwnedCards != null)
            {
                if (ImportRecipe == null) { SetStatus("Your recipes can't be imported here."); return false; }
                var (name, status) = ImportRecipe(path);
                if (name == null) { SetStatus(status); return false; }
                _recipes = null; // the Recipe button sees it from now on
                EnsureRecipes(_banlists[_banIndex]);
                int at = _recipes!.FindIndex(r => r.recipe.Name == MyRecipes.Prefix + name);
                if (at < 0)
                {
                    // Kept, but not one the Recipe button can build (nothing in it to build): say so, and leave the deck alone.
                    SetStatus(status + $"  It's under My recipes in the Recipe Book; it has nothing the Recipe button can build.");
                    return true;
                }
                _recipeIndex = at;
                NextRecipe();
                SetStatus(status + "  " + _status.text);
                return true;
            }
            var read = YdkImport.ReadFile(path, _cards);
            if (read.Deck == null) { SetStatus(read.Error); return false; }
            var deck = read.Deck;
            // A file named like a built-in deck ("goat-control.ydk") mustn't save over it (your own decks win over built-in
            // ones by name, and CPUs, recipes and recipe packs use those): it gets a free name.
            bool BuiltIn(string n) => _library.Names.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase) && !_library.IsOwn(x));
            if (BuiltIn(deck.Name))
            {
                var baseName = deck.Name + " (imported)";
                var free = baseName;
                for (int n = 2; _library.Names.Any(x => string.Equals(x, free, StringComparison.OrdinalIgnoreCase)); n++) free = $"{deck.Name} (imported {n})";
                deck.Name = free;
            }
            Change(d =>
            {
                d.Main = deck.Main;
                d.Extra = deck.Extra;
                d.Side = deck.Side;
                d.Starters = new List<uint>();
                foreach (var key in StoryNotes) d.Notes.Remove(key); // the old deck's story isn't this one's
                d.Name = deck.Name;
            });
            ShowName(_deck.Name);
            _rebuilt = true;
            SetStatus(read.Summary(System.IO.Path.GetFileName(path)) + " Save asks how to save it.");
            return true;
        }

        /// <summary>For checks: the deck in the editor now (a copy), and whether Save would ask how to save.</summary>
        public (Deck deck, bool rebuilt, string status) DebugDeck => (_deck.Clone(), _rebuilt, _status.text);

        private void Export()
        {
            GUIUtility.systemCopyBuffer = DeckCodes.ToYdke(_deck);
            SetStatus("Copied a YDKE link to the clipboard (paste it into EDOPro, deck sites, or Import).");
        }

        /// <summary>Runs <paramref name="action"/> now, or after a second click if it would discard unsaved changes.</summary>
        private void Confirmed(string key, Action action, string? question = null)
        {
            bool risky = _dirty || key == "delete";
            if (risky && _confirm != key)
            {
                _confirm = key;
                SetStatus(question ?? "You have unsaved changes. Click again to discard them.");
                return;
            }
            _confirm = null;
            action();
        }

        private void MarkDirty()
        {
            _dirty = true;
            _confirm = null;
            Autosave();
        }

        // ---- Autosave: every change goes to a recovery file until the deck is saved ----------------------------

        private string RecoveryPath => System.IO.Path.Combine(_library.OwnFolder, OwnedCards != null ? "recovery-campaign.txt" : "recovery.txt");

        private void Autosave()
        {
            try
            {
                var text = $"#recovery {_deck.Name}\n#recovery-time {DateTime.Now:yyyy-MM-dd HH:mm}\n" + DeckCodes.ToYdk(_deck);
                System.IO.File.WriteAllText(RecoveryPath, text);
            }
            catch (System.IO.IOException) { }
        }

        private void ClearRecovery()
        {
            try { if (System.IO.File.Exists(RecoveryPath)) System.IO.File.Delete(RecoveryPath); }
            catch (System.IO.IOException) { }
        }

        private RectTransform? _recoveryBar;

        /// <summary>Unsaved work from last time (the editor was closed or the game quit without saving): offer it back.</summary>
        private void OfferRecovery()
        {
            var T = UiTheme.Current;
            if (_recoveryBar != null) Destroy(_recoveryBar.gameObject);
            _recoveryBar = null;
            if (!System.IO.File.Exists(RecoveryPath)) return;
            string text;
            try { text = System.IO.File.ReadAllText(RecoveryPath); }
            catch (System.IO.IOException) { return; }
            var lines = text.Split('\n');
            var name = lines.FirstOrDefault(l => l.StartsWith("#recovery "))?.Substring(10).Trim() ?? "a deck";
            var when = lines.FirstOrDefault(l => l.StartsWith("#recovery-time "))?.Substring(15).Trim() ?? "";
            var bar = Box(_root, 406, 92, 998, 34);
            _recoveryBar = bar;
            Ui.Image(bar, T.WarnBar);
            Label(10, 0, 640, 34, $"Unsaved changes to <b>{name}</b> from {when} were kept.", 14, parent: bar).supportRichText = true;
            Button(680, 3, 150, 28, "Restore them", () =>
            {
                var deck = Deck.ParseYdk(string.Join("\n", lines.Where(l => !l.StartsWith("#recovery"))), name);
                deck.Name = name;
                _deck = deck;
                RememberVersions();
                _undo.Clear();
                _redo.Clear();
                _dirty = true;
                ShowName(name);
                Destroy(bar.gameObject);
                RefreshDeck();
                SetStatus($"Restored your unsaved {name}. Save to keep it.");
            }, bar);
            Button(840, 3, 150, 28, "Discard", () => { ClearRecovery(); Destroy(bar.gameObject); SetStatus("Discarded the unsaved changes."); }, bar);
        }

        // ---- Copies, labels and the deck browser -------------------------------------------------------------

        private void Duplicate()
        {
            var copy = _deck.Clone();
            var name = $"{_deck.Name} (copy)";
            for (int n = 2; _library.Contains(DeckLibrary.SafeName(name)); n++) name = $"{_deck.Name} (copy {n})";
            copy.Name = name;
            copy.Campaign = OwnedCards != null;
            // A titled deck's copy gets its own title too, or both read the same everywhere.
            if (copy.Note("title").Length > 0) copy.Notes["title"] = name.Replace(_deck.Name, copy.Note("title"));
            _deck = copy;
            ShowName(name);
            SaveNow();
            SetStatus($"Copied to {_deck.Name}: change it freely, the original stays as it was.");
        }

        private void SetLabel(string label)
        {
            if (label.Trim().Length == 0) _deck.Notes.Remove("label");
            else _deck.Notes["label"] = label.Trim();
            MarkDirty();
        }

        private RectTransform? _browser;

        /// <summary>Your decks grouped by label (folders), with each one's era or ban list; click one to open it.</summary>
        private void ShowBrowser()
        {
            var T = UiTheme.Current;
            if (_browser != null) Destroy(_browser.gameObject);
            var root = Ui.Stretched("Decks", _root, Vector2.zero, Vector2.one);
            _browser = root;
            Ui.Clickable((RectTransform)Ui.Image(root, T.Scrim.WithAlpha(0.7f)).transform, CloseBrowser);
            // Sized to the list once it's built (centered, Close at the bottom), up to the screen's height.
            const float MaxHeight = 1040;
            var panel = Box(root, 460, 20, 1000, MaxHeight);
            Ui.Image(panel, T.PanelDeep);
            Label(24, 14, 600, 40, OwnedCards != null ? "YOUR CAMPAIGN DECKS" : "DECKS", 24, parent: panel).fontStyle = FontStyle.Bold;
            var viewport = Box(panel, 20, 64, 960, MaxHeight - 64 - 90);
            Ui.Image(viewport, new Color(0, 0, 0, 0.001f)); // theme: kept, an invisible raycast catcher for scrolling
            viewport.gameObject.AddComponent<RectMask2D>();
            var list = Ui.Rect("List", viewport);
            list.anchorMin = new Vector2(0, 1);
            list.anchorMax = new Vector2(1, 1);
            list.pivot = new Vector2(0.5f, 1);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = list;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;

            // Folders: your labels first (A-Z), then unlabelled decks of yours, then built-in decks (free play).
            var entries = OpenableDecks.Select(n =>
            {
                var d = _library.Load(n);
                var folder = d.Note("label");
                if (folder.Length == 0) folder = _library.IsOwn(n) ? "No label" : d.Note("era").Length > 0 ? "Historical decks" : "Built-in decks";
                var era = d.Note("era").Length > 0 ? d.Note("era") : d.Note("banlist");
                return (name: n, folder, era, main: d.Main.Count, extra: d.Extra.Count);
            }).ToList();
            int Order(string f) => f == "Built-in decks" ? 3 : f == "Historical decks" ? 2 : f == "No label" ? 1 : 0;
            float y = 0;
            foreach (var group in entries.GroupBy(e => e.folder).OrderBy(g => Order(g.Key)).ThenBy(g => g.Key))
            {
                var heading = Label(4, y + 6, 900, 26, $"{group.Key.ToUpperInvariant()}  <color={T.TextDimHex}><size=13>{group.Count()}</size></color>", 16, parent: list);
                heading.supportRichText = true;
                heading.color = T.Gold; // DebugBrowser finds headings by this role
                y += 36;
                foreach (var e in group.OrderBy(e => e.name))
                {
                    var row = Box(list, 0, y, 940, 44);
                    Ui.Image(row, e.name == _deck.Name ? T.RowSelected : T.RowB);
                    Label(14, 0, 520, 44, DeckLabel(e.name), 16, parent: row).color = T.Text;
                    Label(540, 0, 260, 44, e.era, 13, parent: row).color = T.TextMuted;
                    Label(800, 0, 130, 44, $"{e.main} main · {e.extra} extra", 13, TextAnchor.MiddleRight, row).color = T.TextMuted;
                    var name = e.name;
                    Ui.Clickable(row, () => Confirmed("open", () => { CloseBrowser(); LoadDeck(name); }));
                    y += 48;
                }
            }
            list.sizeDelta = new Vector2(0, y);
            float height = Mathf.Clamp(64 + y + 120, 400, MaxHeight);
            panel.sizeDelta = new Vector2(1000, height);
            panel.anchoredPosition = new Vector2(460, -(1080 - height) / 2);
            viewport.sizeDelta = new Vector2(960, height - 64 - 90);
            Button(445, height - 66, 110, 44, "Close", CloseBrowser, panel);
        }

        private void CloseBrowser()
        {
            if (_browser != null) Destroy(_browser.gameObject);
            _browser = null;
        }

        /// <summary>For automated checks: opens the deck browser and returns its folder names.</summary>
        public List<string> DebugBrowser()
        {
            ShowBrowser();
            return _browser!.GetComponentsInChildren<Text>().Where(t => t.fontSize == 16 && t.color == UiTheme.Current.Gold).Select(t => t.text).ToList();
        }

        public void DebugLabelAndCopy(string label)
        {
            _labelField.text = label; // sets the label like typing does
            Duplicate();
        }

        public bool DebugRecoveryOffered => _recoveryBar != null;
        public void DebugOfferRecovery() => OfferRecovery();
        public bool DebugAddAny()
        {
            // Add a card you have a spare copy of; with none spare, take one out instead (any change will do).
            var card = _results.FirstOrDefault(c => CopiesInDeck(c.Code) < Math.Min(3, OwnedCards?.Count(Banlist.BaseCode(c.Code, _cards)) ?? 3));
            if (card != null) Add(card, side: false);
            else if (_deck.Main.Count > 0) RemoveOne(_deck.Main[_deck.Main.Count - 1]);
            return _dirty && System.IO.File.Exists(RecoveryPath);
        }

        // ------------------------------------------------------------------ Display

        private void RefreshDeck()
        {
            var T = UiTheme.Current;
            for (int i = _cardsLayer.childCount - 1; i >= 0; i--) Destroy(_cardsLayer.GetChild(i).gameObject);
            Grid(_deck.Main, DeckSection.Main, 152, 4);
            Grid(_deck.Extra, DeckSection.Extra, 564, 1);
            Grid(_deck.Side, DeckSection.Side, 688, 1);

            _mainHeader.text = $"<b>Main {_deck.Main.Count}</b>   {DeckRules.Breakdown(_deck.Main, _cards)}";
            _extraHeader.text = $"<b>Extra {_deck.Extra.Count}</b>   {DeckRules.ExtraBreakdown(_deck.Extra, _cards)}";
            _sideHeader.text = $"<b>Side {_deck.Side.Count}</b>   {DeckRules.Breakdown(_deck.Side, _cards)}";
            foreach (var t in new[] { _mainHeader, _extraHeader, _sideHeader }) t.supportRichText = true;

            var banlist = _banlists[_banIndex];
            _banLabel.text = Format != null && Format.Pool.Length > 0
                ? $"Ban list: {banlist.Name}  <size=12><color={T.TextDimHex}>(this cup: cards out by {Format.Pool})</color></size>"
                : banlist.Date.Length > 0 && DeckRules.FormatEnd(banlist, _banlists) is string end && end.Length > 0
                ? $"Ban list: {banlist.Name}  <size=12><color={T.TextDimHex}>(format until {end})</color></size>" : $"Ban list: {banlist.Name}";
            _banLabel.supportRichText = true;
            var problems = DeckRules.Validate(_deck, EffectiveList, _cards);
            if (OwnedCards != null) problems.AddRange(CollectionRules.Validate(_deck, OwnedCards, _cards));
            // Cards that weren't out yet in the list's format (in the campaign only when it's era-limited).
            var formatEnd = PoolEnd(banlist);
            if (OwnedCards == null || (EraLimited?.Invoke() ?? true) || Format != null)
                problems.AddRange(DeckRules.Releases(_deck, banlist, formatEnd, CardFoil.Printings, _cards));
            var analysis = Analysis();
            _problems.text = problems.Count == 0
                ? $"<color={T.GoodHex}>This deck is legal under {banlist.Name}.</color>"
                : string.Join("\n", problems.Take(7).Select(p => $"<color={T.BadHex}>•</color> {p.Message}"))
                  + (problems.Count > 7 ? $"\n...and {problems.Count - 7} more." : "");
            // Original-text slots you don't own enough original copies of: they play current text (spec 4.4, D3: a note).
            if (OwnedCards != null && CollectionRules.TextShortfalls(_deck, OwnedCards, _versions.TextRows, _cards) is { Count: > 0 } shortfalls)
                _problems.text += "\n" + string.Join("\n", shortfalls.Take(3).Select(p => $"<color={T.WarnHex}>•</color> {p.Message}."))
                                  + (shortfalls.Count > 3 ? $"\n...and {shortfalls.Count - 3} more short of original copies." : "");
            _problems.text = analysis + _problems.text;
            if (OwnedCards != null && PassAllowance != null && _deck.Note("loaner").Length == 0)
                _problems.text = $"<color={T.GoldHex}>Ban Passes: {BanPasses.Used(_deck, banlist, _cards, NoPass)} of {Passes} used</color>  <size=12><color={T.TextDimHex}>(select a limited card, then + Pass)</color></size>\n" + _problems.text;
            // Historic recipes carry their story: when it was played, how it worked, and why it mattered.
            // (A part a deck doesn't note, like a starter's tier, is left out.)
            if (_deck.Note("era").Length > 0)
            {
                var story = $"<b>{_deck.Note("era")}{(_deck.Note("tier").Length > 0 ? " · " + _deck.Note("tier") : "")}</b>\n";
                if (_deck.Note("about").Length > 0) story += $"<b>How it worked:</b> {_deck.Note("about")}\n";
                if (_deck.Note("why").Length > 0) story += $"<b>Why it mattered:</b> {_deck.Note("why")}\n";
                if (_deck.Note("source").Length > 0) story += $"<color={T.TextDimHex}>{_deck.Note("source")}</color>\n";
                _problems.text = story + "\n" + _problems.text;
            }

            DrawCharts();
            var names = OpenableDecks;
            _openIndex = Mathf.Clamp(_openIndex, 0, Math.Max(0, names.Count - 1));
            _openLabel.text = names.Count == 0 ? "(no decks)" : DeckLabel(names[_openIndex]);
            ShowOdds();
            ShowOpponent();
            ShowResults(); // copy counts on results change with the deck
            if (_selected != null) ShowDetail(_selected);
        }

        private void Grid(List<uint> codes, DeckSection section, float top, int rows)
        {
            int perRow = 15;
            var limits = _banlists[_banIndex];
            for (int i = 0; i < codes.Count && i < perRow * rows; i++)
            {
                int index = i;
                var info = _cards.Get(codes[i]);
                var x = 410 + (i % perRow) * 66 + DeckCard.x / 2;
                var y = top + (i / perRow) * 96 + DeckCard.y / 2;
                var card = info == null ? new FieldCard { Code = codes[i] } : new FieldCard
                {
                    Code = info.Code, Type = info.Type, Level = info.Level, Rank = info.Level, Attack = info.Attack,
                    Defense = info.Defense, LinkRating = info.Level, Position = Ygo.Engine.Position.FaceUpAttack, IsPublic = true,
                };
                var view = CardView.Create(_cardsLayer, new Vector2(x - 960, 540 - y), DeckCard, card, info, 0, true,
                    ShowDetail, offField: true, onClick: () => Change(d => Section(d, section).RemoveAt(index)),
                    onRightClick: () => MoveSection(section, index),
                    highlight: _deck.Starters.Contains(Banlist.BaseCode(codes[index], _cards)));
                int allowed = limits.Allowed(codes[i], _cards);
                // A card a Ban Pass lets in shows a gold P instead of its limit.
                var baseCode = Banlist.BaseCode(codes[i], _cards);
                if (allowed < 3 && OwnedCards != null && BanPasses.Of(_deck).Contains(baseCode) && NoPass?.Contains(baseCode) != true) Badge(view.transform, "P", Ui.Hex("#C9A227"));
                else if (allowed < 3) Badge(view.transform, allowed == 0 ? "X" : allowed.ToString(), allowed == 0 ? Ui.Hex("#D9433F") : Ui.Hex("#E3A21A"));
                var code = codes[i];
                var drag = view.gameObject.AddComponent<DragSource>();
                drag.Began = p => BeginDrag(code, p);
                drag.Moved = p => MoveDrag(p, fromDeck: true);
                drag.Dropped = p => DropDeckCard(section, index, EndDrag(p));
            }
        }

        /// <summary>
        /// Two charts of the Main Deck: its monster / Spell / Trap split as one bar, and its monsters' Levels as columns
        /// (Extra Deck monsters are left out; they're counted in the Extra header).
        /// </summary>
        private void DrawCharts()
        {
            var T = UiTheme.Current;
            for (int i = _charts.childCount - 1; i >= 0; i--) Destroy(_charts.GetChild(i).gameObject);
            var infos = _deck.Main.Select(c => _cards.Get(c)).Where(c => c != null).ToList();
            int monsters = infos.Count(c => (c!.Type & CardType.Monster) != 0);
            int spells = infos.Count(c => (c!.Type & CardType.Spell) != 0);
            int traps = infos.Count(c => (c!.Type & CardType.Trap) != 0);
            int total = Math.Max(1, monsters + spells + traps);

            // The split bar.
            Label(14, 6, 400, 20, "MAIN DECK SPLIT", 12, parent: _charts).color = T.Caption;
            float x = 14, width = 440;
            foreach (var (count, color, name) in new[] { (monsters, "#D07A35", "Monsters"), (spells, "#1D9C73", "Spells"), (traps, "#B0457F", "Traps") })
            {
                if (count == 0) continue;
                float w = width * count / total;
                var seg = Box(_charts, x, 30, w - 2, 34);
                Ui.Image(seg, Ui.Hex(color)).raycastTarget = false;
                if (w > 44) Ui.Text(seg, count.ToString(), 16, Color.white).fontStyle = FontStyle.Bold;
                x += w;
            }
            var legend = Label(14, 70, 440, 36, $"<color=#F0A060>■</color> {monsters} monsters  ({100 * monsters / total}%)    "
                + $"<color=#3FD0A0>■</color> {spells} Spells  ({100 * spells / total}%)    <color=#E070B0>■</color> {traps} Traps  ({100 * traps / total}%)", 13, parent: _charts);
            legend.supportRichText = true;

            // Levels of the Main Deck's monsters.
            Label(500, 6, 400, 20, "MONSTER LEVELS", 12, parent: _charts).color = T.Caption;
            var levels = new int[13];
            foreach (var c in infos.Where(c => (c!.Type & CardType.Monster) != 0)) levels[Mathf.Clamp((int)(c!.Level & 0xff), 0, 12)]++;
            int most = Math.Max(1, levels.Max());
            for (int lv = 1; lv <= 12; lv++)
            {
                float colX = 500 + (lv - 1) * 34, h = 44f * levels[lv] / most;
                var col = Box(_charts, colX, 84 - h, 28, Math.Max(h, levels[lv] > 0 ? 3 : 1));
                Ui.Image(col, levels[lv] > 0 ? (lv >= 7 ? Ui.Hex("#FFB347") : lv >= 5 ? Ui.Hex("#FFD86B") : Ui.Hex("#9FD8FF")) : new Color(1, 1, 1, 0.12f)).raycastTarget = false;
                if (levels[lv] > 0) Label(colX, 84 - h - 15, 28, 15, levels[lv].ToString(), 11, TextAnchor.MiddleCenter, _charts);
                Label(colX, 88, 28, 18, lv.ToString(), 11, TextAnchor.MiddleCenter, _charts).color = T.Caption.WithAlpha(0.55f);
            }
            var tributes = Label(500 + 12 * 34 + 8, 28, 64, 76, $"<size=11>Lv 5-6:</size> {levels[5] + levels[6]}\n<size=11>Lv 7+:</size> {levels.Skip(7).Sum()}", 12, TextAnchor.MiddleLeft, _charts);
            tributes.supportRichText = true;
        }

        /// <summary>A top-left box stretched to its parent's full height (keeping its x and width).</summary>
        private static RectTransform FullHeight(RectTransform box)
        {
            box.anchorMin = new Vector2(box.anchorMin.x, 0);
            box.anchorMax = new Vector2(box.anchorMax.x, 1);
            box.sizeDelta = new Vector2(box.sizeDelta.x, 0);
            box.anchoredPosition = new Vector2(box.anchoredPosition.x, 0);
            return box;
        }

        private static void Badge(Transform card, string text, Color color)
        {
            var rt = Ui.Rect("Limit", card);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(2, -2);
            rt.sizeDelta = new Vector2(20, 20);
            Ui.Image(rt, color).raycastTarget = false;
            Ui.Text(rt, text, 13, Color.white).fontStyle = FontStyle.Bold;
            // A card without a picture has its name at the top: it starts under the badge (shrinking to fit), not behind it.
            if (card.Find("Face/Name") is RectTransform name) name.offsetMax = new Vector2(name.offsetMax.x, Mathf.Min(name.offsetMax.y, -20));
        }

        private void RunSearch()
        {
            // Typed ban list terms (limit:0-3, legal, -limit:N, -legal) are the editor's to apply: CardSearch knows no lists.
            var typed = CardSearch.TakeBanFilters(_query, out CardSearch.BanTerms ban);
            // The box and the rest go in apart: the switch reads only the box, and an OR typed at its end can't reach a chip.
            var filters = _chips.OrderBy(c => c).Select(c => ChipTokens[c])
                .Concat(new[] { KindOptions[_picks[0]].token, AttributeOptions[_picks[1]].token, TypeOptions[_picks[2]].token, LevelOptions[_picks[3]].token })
                .Concat(MoreTokens()); // genres, archetypes (DeckEditor.Filters)
            var found = _search.Find(typed, _searchMode, string.Join(" ", filters.Where(t => t.Length > 0)), 20000);
            // Close names (when no card is named that) stay after the real results through every filter and sort below.
            int closeAt = _search.LastCloseNameStart;
            var close = closeAt >= 0 ? found.Skip(closeAt).ToList() : new List<CardInfo>();
            if (closeAt >= 0) found = found.Take(closeAt).ToList();
            bool Owns(CardInfo c) => OwnedCards!.Owns(Banlist.BaseCode(c.Code, _cards));
            _ownedOnly = OwnedCards != null && _scope == ScopeOwned;
            // Seen in duels: newest duel first, in the order the cards appeared. Recently viewed: newest first.
            Dictionary<uint, int>? listed = null;
            if (_scope == ScopeSeen || _scope == ScopeViewed)
            {
                var codes = _scope == ScopeSeen ? _seenLog.Codes(SeenDuel) : _seenLog.Viewed;
                listed = new Dictionary<uint, int>();
                foreach (var code in codes) if (!listed.ContainsKey(code)) listed[code] = listed.Count;
            }
            List<CardInfo> Narrow(List<CardInfo> cards)
            {
                if (_related != null)
                {
                    var matching = new HashSet<uint>(cards.Select(c => c.Code));
                    cards = _search.Related(_related, 500).Where(c => matching.Contains(c.Code)).ToList();
                }
                if (_ownedOnly) cards = cards.Where(Owns).ToList();
                if (_scope == ScopeFavorites) cards = cards.Where(c => _favorites.Contains(Banlist.BaseCode(c.Code, _cards))).ToList();
                if (listed != null)
                {
                    var order = listed;
                    cards = cards.Where(c => order.ContainsKey(Banlist.BaseCode(c.Code, _cards))).OrderBy(c => order[Banlist.BaseCode(c.Code, _cards)]).ToList();
                }
                return PostFilters(cards, ban); // Limit and Look, after Find as Owned only is (DeckEditor.Filters)
            }
            IEnumerable<CardInfo> Sorted(List<CardInfo> cards)
            {
                IEnumerable<CardInfo> ordered = cards;
                switch (_picks[4])
                {
                    case 1: ordered = cards.OrderBy(c => c.Name); break;
                    case 2: ordered = cards.OrderByDescending(c => (c.Type & CardType.Monster) != 0 ? (int)(c.Level & 0xff) : -1).ThenBy(c => c.Name); break;
                    case 3: ordered = cards.OrderByDescending(c => (c.Type & CardType.Monster) != 0 ? c.Attack : -1).ThenBy(c => c.Name); break;
                    case 4: ordered = cards.OrderByDescending(c => (c.Type & CardType.Monster) != 0 && (c.Type & CardType.Link) == 0 ? c.Defense : -1).ThenBy(c => c.Name); break;
                    case 5 when OwnedCards != null: ordered = cards.OrderByDescending(c => OwnedCards.Count(Banlist.BaseCode(c.Code, _cards))).ThenBy(c => c.Name); break;
                }
                // In the campaign, cards you own come first (the rest are there to discover); a seen or viewed list keeps its order.
                if (OwnedCards != null && !_ownedOnly && _picks[4] != 5 && listed == null) ordered = ordered.OrderBy(c => Owns(c) ? 0 : 1);
                return ordered;
            }
            var real = Sorted(Narrow(found)).ToList();
            _closeNamesFrom = close.Count > 0 ? real.Count : -1;
            _results = real.Concat(close.Count > 0 ? Sorted(Narrow(close)) : Enumerable.Empty<CardInfo>()).Take(2000).ToList();
            if (_closeNamesFrom >= _results.Count) _closeNamesFrom = -1;
            ShowSearchStatus();
            _page = 0;
            _emptyNote.text = _scope == ScopeSeen && _seenLog.Duels.Count == 0 ? "Cards your opponents play show here after a duel."
                : _scope == ScopeViewed && _seenLog.Viewed.Count == 0 ? "Cards you look at in the editor show here."
                : "";
            ShowActiveFilters();
            ShowResults();
        }

        /// <summary>
        /// What the last search couldn't use, on the status line: close names, an archetype or genre that isn't one. Cleared
        /// once it no longer holds (and only if nothing else has taken the line since).
        /// </summary>
        private void ShowSearchStatus()
        {
            var notes = new List<string>();
            if (_search.LastUnknownArchetypes.Count > 0)
                notes.Add($"No archetype called {string.Join(", ", _search.LastUnknownArchetypes.Select(a => $"\"{a}\""))}.");
            if (_search.LastUnknownGenres.Count > 0)
                notes.Add($"No genre called {string.Join(", ", _search.LastUnknownGenres.Select(g => $"\"does:{g}\""))} (the More button lists them).");
            if (_closeNamesFrom >= 0) notes.Add("No card named that; showing close names."); // (some made it through the scope and Limit)
            var note = string.Join(" ", notes);
            if (note.Length > 0) SetStatus(note);
            else if (_searchStatus.Length > 0 && _status.text == _searchStatus) SetStatus("");
            _searchStatus = note;
        }

        /// <summary>Shows the cards related to one (its archetype, and cards naming it) in the catalog.</summary>
        private void ShowRelated()
        {
            if (_selected == null) return;
            _related = _cards.Get(Banlist.BaseCode(_selected.Code, _cards)) ?? _selected;
            RunSearch();
            SetStatus(_results.Count == 0 ? $"Nothing related to {_related.Name} found." : $"{_results.Count} cards related to {_related.Name}: its archetype, and cards that name it.");
        }

        private void ShowResults()
        {
            var T = UiTheme.Current;
            for (int i = _resultsLayer.childCount - 1; i >= 0; i--) Destroy(_resultsLayer.GetChild(i).gameObject);
            int pages = Math.Max(1, (_results.Count + ResultsPerPage - 1) / ResultsPerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            _resultCount.text = $"{_results.Count}{(_results.Count >= 2000 ? "+" : "")} cards";
            _pageLabel.text = $"{_page + 1} / {pages}";
            var limits = _banlists[_banIndex];
            for (int slot = 0; slot < ResultsPerPage; slot++)
            {
                int i = _page * ResultsPerPage + slot;
                if (i >= _results.Count) break;
                var info = _results[i];
                var shown = Resolve(info);
                // A grid of cards: 5 across, the cell's top-left from the panel's top-left.
                float cx = (slot % ResultColumns) * 100 + 50 - 250, cy = 375 - (slot / ResultColumns) * 150 - ResultCard.y / 2 - 2;
                int copies = CopiesInDeck(info.Code);
                int allowed = limits.Allowed(info.Code, _cards);
                var card = new FieldCard
                {
                    Code = shown.Code, Type = shown.Type, Level = shown.Level, Rank = shown.Level, Attack = shown.Attack,
                    Defense = shown.Defense, LinkRating = shown.Level, Position = Ygo.Engine.Position.FaceUpAttack, IsPublic = true,
                };
                // Your copies past the first: a plate behind the card for each, so a stack reads as "more than one".
                var ownedCopies = OwnedCards?.CopiesOf(Banlist.BaseCode(info.Code, _cards));
                for (int extra = Math.Min(2, (ownedCopies?.Count ?? 0) - 1); extra >= 1; extra--)
                {
                    var plate = Ui.Placed("Copy", _resultsLayer, new Vector2(cx + 4 * extra, cy - 2 * extra), ResultCard);
                    Ui.Image(plate, Ui.Hex("#4A5468")).raycastTarget = false; // theme: kept, copy plates are part of the card stack
                    Ui.Image(Ui.Placed("Fill", plate, Vector2.zero, ResultCard - new Vector2(2, 2)), Ui.Hex("#2A3142")).raycastTarget = false;
                }
                // Your best copy in a slab: the case's rim around the card.
                if (ownedCopies is { Count: > 0 } && ownedCopies[0].IsSlab)
                {
                    var rim = Ui.Placed("SlabRimHolder", _resultsLayer, new Vector2(cx, cy), ResultCard);
                    SlabView.Rim(rim, Vector2.zero, ResultCard);
                }
                var view = CardView.Create(_resultsLayer, new Vector2(cx, cy), ResultCard, card, shown, 0, true, ShowDetail, offField: true,
                    onClick: () => Add(info, side: false), onRightClick: () => Add(info, side: true), highlight: copies > 0);
                if (ownedCopies is { Count: > 0 } && ownedCopies[0].IsSlab) SlabView.RimGlass(_resultsLayer, new Vector2(cx, cy), ResultCard, view);
                if (allowed < 3) Badge(view.transform, allowed == 0 ? "X" : allowed.ToString(), allowed == 0 ? Ui.Hex("#D9433F") : Ui.Hex("#E3A21A"));
                int ownCount = OwnedCards?.Count(Banlist.BaseCode(info.Code, _cards)) ?? 3;
                if (_favorites.Contains(Banlist.BaseCode(info.Code, _cards)))
                {
                    var star = Ui.Placed("Favorite", view.transform, new Vector2(ResultCard.x / 2 - 12, ResultCard.y / 2 - 12), new Vector2(22, 22)).gameObject.AddComponent<RawImage>();
                    star.texture = Icons.For("star");
                    star.color = Ui.Hex("#FFD24A"); // theme: kept, a badge on the card
                    star.raycastTarget = false;
                }
                // Not yours yet (there to discover): the face is shaded, the outline stays as bright as an owned card's.
                if (ownCount == 0 && view.transform.Find("Face") is Transform face)
                    Ui.Image(Ui.Stretched("Unowned", face, Vector2.zero, Vector2.one), new Color(0, 0, 0, 0.6f)).raycastTarget = false;
                if (copies > 0)
                {
                    var tag = Ui.Placed("InDeck", view.transform, new Vector2(ResultCard.x / 2 - 14, -ResultCard.y / 2 + 38), new Vector2(26, 22)); // above the stat strip
                    Ui.Image(tag, Ui.Hex("#13203A")).raycastTarget = false; // theme: kept, a badge on the card
                    Ui.Text(tag, $"x{copies}", 13, CardView.Actionable).fontStyle = FontStyle.Bold;
                }
                // Under the card: how many you own (campaign), or its kind.
                var under = OwnedCards != null && allowed > 0
                    ? (ownCount == 0 ? $"<color={T.BadHex}>not owned</color>"
                        : $"<color={T.InfoSoftHex}>own {ownCount}</color>" + (ownedCopies is { Count: > 0 } c0
                            ? $" · <color={Grades.Color(c0[0].Shown)}>{Grades.Glyph(c0[0].Shown)} {(c0[0].IsSlab ? Grades.Label(c0[0].Slab) : Grades.Word(c0[0].Shown))}</color>" : ""))
                    : allowed == 0 ? (limits.Whitelist ? $"<color={T.BadStrongHex}>not legal</color>" : $"<color={T.BadStrongHex}>Forbidden</color>") : shown.Name;
                // Seen in duels: who played it (the latest duel it was seen in, or the duel picked).
                if (_scope == ScopeSeen && (SeenDuel ?? _seenLog.LastSeenIn(Banlist.BaseCode(info.Code, _cards))) is SeenLog.Duel seenIn)
                    under = $"<color={T.InfoSoftHex}>vs {seenIn.Opponent}</color>";
                var line = Ui.Text(Ui.Placed("Under", _resultsLayer, new Vector2(cx, cy - ResultCard.y / 2 - 10), new Vector2(98, 18)), under, 11,
                    T.TextMuted);
                line.supportRichText = true;
                line.resizeTextForBestFit = true;
                line.resizeTextMinSize = 10;
                line.resizeTextMaxSize = 11;
                line.raycastTarget = false;
                var drag = view.gameObject.AddComponent<DragSource>();
                drag.Began = p => BeginDrag(info.Code, p);
                drag.Moved = p => MoveDrag(p, fromDeck: false);
                drag.Dropped = p => DropResult(info, EndDrag(p));
            }
        }

        private static Color ChipColor(int chip, bool on)
        {
            var c = Ui.Hex(ChipColors[chip]);
            return on ? c : Color.Lerp(UiTheme.Current.ButtonOff, c, 0.35f);
        }

        /// <summary>
        /// The version of a search result to show and add: the one chosen for this card, else its original text under a
        /// 2004-2005 ban list. A specific version (a deck card) stays as it is.
        /// </summary>
        private CardInfo Resolve(CardInfo info)
        {
            var key = Banlist.BaseCode(info.Code, _cards);
            if (info.Code != key) return info;
            // In the campaign a text you'd add needs a copy of it not yet in the deck (spec 5.4); else the current text, in
            // the artwork chosen for it.
            CardInfo Current() => _preferredArt.TryGetValue(key, out var art) && _cards.Get(art) is CardInfo a ? a : info;
            if (_preferred.TryGetValue(key, out var chosen)) return HasSpare(chosen) ? _cards.Get(chosen) ?? info : Current();
            // The original by default under a 2004-2005 list, where it plays as printed (TextRules has the say).
            if (CardVersions.IsOriginalEra(_banlists[_banIndex].Name, out var preferGoat)
                && _versions.Original(key, preferGoat) is uint original && PlayedHere(original) != key && HasSpare(original))
                return _cards.Get(original) ?? info;
            return info;
        }

        private void CycleArt(int delta)
        {
            if (_selected == null) return;
            var arts = _versions.Arts(_selected.Code);
            if (arts.Count < 2) return;
            var next = arts[(arts.IndexOf(_selected.Code) + delta + arts.Count) % arts.Count];
            ChooseVersion(next, sameText: true);
        }

        private void CycleText()
        {
            if (_selected == null) return;
            var texts = SwitchTexts(_selected.Code);
            if (texts.Count < 2) return;
            // By entry, not name: Mind Master's two originals share one (a dup shows as its canonical text; an alternate
            // artwork of the current text matches the current text by name).
            var current = texts.IndexOf(_versions.Canonical(_selected.Code));
            if (current < 0) current = texts.FindIndex(c => _cards.Get(c)?.Name == _selected.Name);
            var next = texts[(current + 1) % texts.Count];
            // Back to the current text: keep the artwork chosen before, if any.
            if (next == Banlist.BaseCode(next, _cards) && _preferredArt.TryGetValue(next, out var art)) next = art;
            ChooseVersion(next, sameText: false);
        }

        /// <summary>Artwork chosen for a card's current text, kept while an original text is selected.</summary>
        private readonly Dictionary<uint, uint> _preferredArt = new Dictionary<uint, uint>();

        /// <summary>Makes <paramref name="code"/> the version of its card: every copy in the deck switches to it.</summary>
        private void ChooseVersion(uint code, bool sameText)
        {
            var info = _cards.Get(code);
            if (info == null || _selected == null) return;
            var key = Banlist.BaseCode(code, _cards);
            _preferred[key] = code;
            if (CardVersions.KindOf(info) == CardVersions.TextKind.Current) _preferredArt[key] = code;
            var name = _selected.Name;
            bool Switches(uint c) => Banlist.BaseCode(c, _cards) == key && (!sameText || _cards.Get(c)?.Name == name);
            int copies = _deck.Main.Concat(_deck.Extra).Concat(_deck.Side).Count(Switches);
            // In the campaign an original text goes on up to as many slots as you own copies of it (spec 5.4), so one deck can
            // mix versions: slots already asking for that text keep it, then the first others in deck order switch.
            var asked = AskedText(code);
            bool capped = !sameText && OwnedCards != null && asked != 0;
            int own = capped ? OwnedCards!.Count(key, asked) : int.MaxValue;
            if (copies > 0)
                Change(d =>
                {
                    int room = own - d.Main.Concat(d.Extra).Concat(d.Side).Count(c => Switches(c) && AskedText(c) == asked && capped);
                    foreach (var list in new[] { d.Main, d.Extra, d.Side })
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (!Switches(list[i])) continue;
                            if (capped && AskedText(list[i]) != asked) { if (room <= 0) continue; room--; }
                            list[i] = code;
                        }
                });
            ShowDetail(info);
            ShowResults();
            var label = _versions.TextLabel(code);
            var what = sameText ? $"artwork {_versions.Arts(code).IndexOf(code) + 1}" : label.Length > 0 ? char.ToLowerInvariant(label[0]) + label.Substring(1) : label;
            // "Switched 1 of 3": the slots that now hold this text, of the card's slots in the deck.
            int holding = _deck.Main.Concat(_deck.Extra).Concat(_deck.Side).Count(c => c == code);
            var baseName = _cards.Get(key)?.Name ?? info.Name;
            var its = CardVersions.KindOf(info) == CardVersions.TextKind.Goat ? "its 2005 ruling" : "its original text";
            string OwnCopies(int n) => $"you own {n} original cop{(n == 1 ? "y" : "ies")}";
            SetStatus(copies == 0 ? $"{info.Name}: {what} will be used when you add it."
                : capped && own < copies ? $"Switched {holding} of {copies} {baseName} to {its} ({OwnCopies(own)})."
                : capped ? $"Switched {copies} {baseName} to {its} ({OwnCopies(own)})."
                : $"Switched {copies} {info.Name} in the deck to {what}.");
        }

        private void ShowDetail(CardInfo info)
        {
            var T = UiTheme.Current;
            info = Resolve(info);
            _selected = info;
            _cardDetail.Show(info);
            var key = Banlist.BaseCode(info.Code, _cards);
            var limits = _banlists[_banIndex];
            int allowed = limits.Allowed(info.Code, _cards);
            var legality = allowed >= 3 ? "" : allowed == 0 ? $"\n<color={T.BadHex}>{(limits.Whitelist ? "Not legal" : "Forbidden")} in {limits.Name}</color>" : $"\n<color={T.WarnHex}>Limited to {allowed} in {limits.Name}</color>";
            int passed = BanPasses.Of(_deck).Count(c => c == key);
            if (allowed < 3 && OwnedCards != null && PassAllowance != null)
                legality += NoPass?.Contains(key) == true ? $"  <color={T.TextDimHex}>(can't be passed)</color>" : passed > 0 ? $"  <color={T.GoldHex}>+{passed} by Ban Pass</color>" : "";
            _passButtons.SetActive(CanPass(key) || passed > 0);
            int inMain = _deck.Main.Count(c => Banlist.BaseCode(c, _cards) == key);
            var draw = inMain > 0 && _deck.Main.Count > 0
                ? $"  ·  in your opening hand {OpeningOdds.AtLeastOne(_deck.Main.Count, inMain, 5):P0}" : "";
            int owned = OwnedCards?.Count(key) ?? -1;
            var ownCopies = owned > 0 ? OwnCopiesLine(key, owned) : "";
            // Per version (spec 5.4) on a line of its own when the card has an original text; else beside the deck count.
            var ownLine = owned < 0 ? "" : owned == 0 ? $"\n<color={T.BadHex}>Not owned.</color> Get it {WhereToGet?.Invoke(key) ?? "in the Card Shop"}"
                : ownCopies.Contains(" · ") ? $"\n{ownCopies}" : $"  ·  {ownCopies}";
            // Which text this card plays under the picked list, and why (spec 5.4): right under the CARD TEXT switch.
            var note = TextNote(info);
            // (Not owned and where to get it goes last: the longest, and the first the block shrinks for.)
            bool notOwned = owned == 0;
            _detail.text = $"In deck: <b>{CopiesInDeck(info.Code)}</b>{(notOwned ? "" : ownLine)}{draw}{(note.Length > 0 ? $"\n{note}" : "")}{legality}{(notOwned ? ownLine : "")}";
            _starterLabel.text = _deck.Starters.Contains(key) ? "Starter ✓" : "Mark starter";
            _favoriteLabel.text = _favorites.Contains(key) ? "★ Favorite" : "☆ Favorite";
            SetCardButtons(true);
            ShowVersions(info);
        }

        private void ShowVersions(CardInfo info)
        {
            var code = _versions.Canonical(info.Code);
            var arts = _versions.Arts(code);
            var texts = SwitchTexts(code);
            _versionPanel.gameObject.SetActive(arts.Count > 1 || texts.Count > 1);
            _artLabel.text = arts.Count > 1 ? $"{arts.IndexOf(code) + 1} of {arts.Count}" : "only one";
            var label = _versions.TextLabel(code);
            _textLabel.text = texts.Count > 1 ? $"{label}\n<size=11>click to switch</size>" : label;
            _textLabel.supportRichText = true;
        }

        /// <summary>
        /// The text note under the CARD TEXT switch (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 5.4), against the picked list:
        /// which text this entry plays here, and why. "" for a card with one text.
        /// </summary>
        private string TextNote(CardInfo info)
        {
            var T = UiTheme.Current;
            var code = _versions.Canonical(info.Code);
            if (_versions.PlayerTexts(code).Count < 2) return "";
            var key = Banlist.BaseCode(code, _cards);
            var kind = CardVersions.KindOf(info);
            if (kind == CardVersions.TextKind.Current)
            {
                // In the campaign the switch offers only texts you own (spec 5.4): without an original copy, where to find one.
                if (SwitchTexts(code).Count > 1 || OwnedCards == null)
                    return $"<color={T.TextDimHex}>This card had different text when it came out: CARD TEXT switches to it.</color>";
                return _versions.TextRows.Earliest(key) != null
                    ? $"<color={T.TextDimHex}>This card had different text when it came out. You don't own an original copy. Old sets in the back catalog print it.</color>"
                    : $"<color={T.TextDimHex}>This card has a 2005 Goat ruling: CARD TEXT offers it under a Goat list.</color>";
            }
            var rows = _versions.TextRows;
            var row = rows.Row(code);
            var rules = TableTexts;
            var played = PlayedHere(code);
            // "Under TCG October 2026" / "At this circuit" (its own texts, no list picked) / "Under No limits".
            var under = AtCircuit ? "At this circuit" : $"Under {_banlists[_banIndex].Name}";
            var here = AtCircuit ? "at this circuit" : "under this list";
            string Good(string s) => $"<color={T.InfoSoftHex}>{s}</color>";
            string Warn(string s) => $"<color={T.WarnHex}>{s}</color>";
            if (row != null && (row.Never || row.CpuOnly))
                return Warn(row.Never ? "Original text no TCG printing carries: your decks play its current text." : "Original text only CPUs play: your decks play its current text.");
            var until = row != null && row.Until.Length >= 7 ? DeckRules.MonthYear(row.Until) : "";
            if (played == key)
                return Warn(until.Length > 0 ? $"Original text. {under} it plays its current text (errata'd {until})."
                    : kind == CardVersions.TextKind.Goat ? $"The 2005 Goat ruling. {under} it plays its current text."
                    : $"Original text. {under} it plays its current text.");
            // Allowed here, but a slot with no copy of it plays its current text (Collection.Assign, PlayTexts.ForTable).
            if (OwnedCards != null && AskedText(code) != 0 && OwnedOfText(code) == 0)
                return Warn("You don't own an original copy. Old sets in the back catalog print it.");
            if (played != code && rows.Goat(key) == played)
                return Good(until.Length > 0 ? $"Original text, as printed until {until}. At this Goat table it plays its 2005 ruling." : "At this Goat table it plays its 2005 ruling.");
            if (kind == CardVersions.TextKind.Goat)
                return Good(played == code ? $"The 2005 Goat ruling. It plays as ruled then {here}." : "The 2005 Goat ruling. Here it plays its original printed text.");
            if (rules.Printed && Format != null) return Good("Plays as printed in the Back Alley.");
            return Good(until.Length > 0 ? $"Original text, as printed until {until}. It plays as printed {here}." : $"Original text. It plays as printed {here}.");
        }

        private void ShowOdds()
        {
            var starters = new HashSet<uint>(_deck.Starters);
            int hits = _deck.Main.Count(c => starters.Contains(Banlist.BaseCode(c, _cards)));
            if (starters.Count == 0 || _deck.Main.Count == 0)
            {
                _odds.text = "Select a card and press <b>Mark starter</b> for each card that can start your combo. Starters are outlined in gold.";
                return;
            }
            var names = starters.Select(c => _cards.Get(c)?.Name ?? c.ToString()).Take(4);
            _odds.text = $"{hits} starters in {_deck.Main.Count} cards ({string.Join(", ", names)}{(starters.Count > 4 ? "..." : "")})\n"
                         + $"At least one: <b>{OpeningOdds.AtLeastOne(_deck.Main.Count, hits, 5):P1}</b> going first (5 cards), "
                         + $"<b>{OpeningOdds.AtLeastOne(_deck.Main.Count, hits, 6):P1}</b> going second (6)";
        }

        private void ShowTestHand(bool open)
        {
            if (_deck.Main.Count == 0) { SetStatus("Add cards to the Main Deck first."); return; }
            int size = _testHandSecond ? 6 : 5;
            _testHand = _deck.Main.OrderBy(_ => _rng.Next()).Take(size).ToList();
            _handOverlay.gameObject.SetActive(true);
            _handOverlay.SetAsLastSibling();
            for (int i = _handCards.childCount - 1; i >= 0; i--) Destroy(_handCards.GetChild(i).gameObject);
            var starters = new HashSet<uint>(_deck.Starters);
            int hits = _testHand.Count(c => starters.Contains(Banlist.BaseCode(c, _cards)));
            _handLabel.text = $"Test hand, going {(_testHandSecond ? "second (6 cards)" : "first (5 cards)")}" + (starters.Count > 0 ? $"  ·  {hits} starter{(hits == 1 ? "" : "s")}" : "");
            for (int i = 0; i < _testHand.Count; i++)
            {
                var info = _cards.Get(_testHand[i]);
                var card = new FieldCard { Code = _testHand[i], Type = info?.Type ?? 0, Level = info?.Level ?? 0, Attack = info?.Attack ?? 0, Defense = info?.Defense ?? 0, Position = Ygo.Engine.Position.FaceUpAttack };
                CardView.Create(_handCards, new Vector2((i - (_testHand.Count - 1) / 2f) * 150, 0), new Vector2(136, 196), card, info, 0, true,
                    ShowDetail, offField: true, highlight: starters.Contains(Banlist.BaseCode(_testHand[i], _cards)));
            }
        }

        // ------------------------------------------------------------------ Bot test

        private List<string> OpponentDecks => _library.Names.ToList();

        private void CycleOpponent(int delta)
        {
            if (_roster.Count == 0) return;
            _opponentIndex = (_opponentIndex + delta + _roster.Count) % _roster.Count;
            var own = _roster[_opponentIndex].Decks.FirstOrDefault(OpponentDecks.Contains);
            if (own != null) _opponentDeckIndex = OpponentDecks.IndexOf(own);
            ShowOpponent();
        }

        private void CycleOpponentDeck(int delta)
        {
            var decks = OpponentDecks;
            if (decks.Count == 0) return;
            _opponentDeckIndex = (_opponentDeckIndex + delta + decks.Count) % decks.Count;
            ShowOpponent();
        }

        private void ShowOpponent()
        {
            var decks = OpponentDecks;
            _opponentDeckIndex = Mathf.Clamp(_opponentDeckIndex, 0, Math.Max(0, decks.Count - 1));
            _opponentLabel.text = _roster.Count == 0 ? "Balanced bot" : $"{_roster[_opponentIndex].Name}";
            _opponentDeckLabel.text = decks.Count == 0 ? "(no decks)" : $"with {DeckTitle(decks[_opponentDeckIndex])}";
        }

        /// <summary>A deck's name as players know it ("ABC-Dragon Buster", not the file name "abc").</summary>
        private string DeckTitle(string name)
        {
            if (MyRecipes.IsMine(name)) return MyRecipes.NameOf(name); // one of your own recipes
            var title = _library.Contains(name) ? _library.Load(name).Note("title") : "";
            if (title.Length > 0) return title;
            // A file slug ("stall-burn") reads as words; a name you gave a deck stays as it is.
            return name.All(c => char.IsLower(c) || char.IsDigit(c) || c == '-')
                ? string.Join(" ", name.Split('-').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w.Substring(1)))
                : name;
        }

        private void RunTest()
        {
            if (_deck.Main.Count < DeckRules.MainMin || _deck.Main.Concat(_deck.Extra).Any(c => _cards.Get(c) == null))
            {
                SetStatus("The deck needs at least 40 known cards in the Main Deck to test.");
                return;
            }
            var decks = OpponentDecks;
            if (decks.Count == 0) return;
            var opponent = _roster.Count == 0 ? Personality.Balanced : _roster[_opponentIndex];
            _tester.Start(_cards, _scripts, _deck, _library.Load(decks[_opponentDeckIndex]), opponent, 100, _rng.Next(1, 1000000));
            _wasTesting = true;
            ShowTestProgress();
        }

        private void ShowTestProgress()
        {
            var T = UiTheme.Current;
            int done = _tester.Completed;
            if (_tester.Error != null) { _testResult.text = $"<color={T.BadHex}>Test stopped: {_tester.Error}</color>"; return; }
            if (done == 0) { _testResult.text = _tester.Running ? "Starting..." : "Your deck plays quick games against the bot, alternating who goes first."; return; }
            double pct(int w, int n) => n == 0 ? 0 : 100.0 * w / n;
            _testResult.text = $"{(_tester.Running ? $"Playing... {done}/{_tester.Games}" : $"Finished {done} games")}\n"
                               + $"<b>You won {_tester.Wins} ({pct(_tester.Wins, done):F0}%)</b>\n"
                               + $"Going first {pct(_tester.WinsFirst, _tester.GamesFirst):F0}% · second {pct(_tester.WinsSecond, _tester.GamesSecond):F0}%\n"
                               + $"Average game: {_tester.TotalTurns / (double)done:F1} turns";
        }

        // ------------------------------------------------------------------ Small helpers

        private void CycleOpen(int delta)
        {
            var names = OpenableDecks;
            if (names.Count == 0) return;
            _openIndex = (_openIndex + delta + names.Count) % names.Count;
            _openLabel.text = DeckLabel(names[_openIndex]);
        }

        private void CycleBanlist(int delta)
        {
            _recipes = null;
            _banIndex = (_banIndex + delta + _banlists.Count) % _banlists.Count;
            _listPickedByHand = true;
            RefreshDeck();
            // The Limit filter (or a typed limit: / legal) reads the picked list: the results follow it.
            if (BanFilterOn) RunSearch();
            else if (MoreOpen) ShowMorePanel(); // the LIMIT caption names the list
            // The text note reads the picked list.
            if (_selected != null && _versions.PlayerTexts(_versions.Canonical(_selected.Code)).Count > 1) ShowDetail(_selected);
        }

        private void SetStatus(string text) => _status.text = text;

        private static string Describe(CardInfo info)
        {
            if ((info.Type & CardType.Monster) == 0)
            {
                var sub = (info.Type & 0x10000) != 0 ? "Quick-Play " : (info.Type & 0x20000) != 0 ? "Continuous " : (info.Type & 0x80000) != 0 ? "Field "
                    : (info.Type & 0x40000) != 0 ? "Equip " : (info.Type & 0x100000) != 0 ? "Counter " : (info.Type & 0x80) != 0 ? "Ritual " : "";
                return $"{sub}{((info.Type & CardType.Spell) != 0 ? "Spell" : "Trap")}";
            }
            var attribute = DuelText.Attributes.FirstOrDefault(a => (info.Attribute & a.bit) != 0).name ?? "";
            var race = DuelText.Races.FirstOrDefault(r => (info.Race & r.bit) != 0).name ?? "";
            var stars = (info.Type & CardType.Link) != 0 ? $"Link-{info.Level}" : (info.Type & CardType.Xyz) != 0 ? $"Rank {info.Level}" : $"Level {info.Level}";
            var def = (info.Type & CardType.Link) != 0 ? "" : $" / {CardDetail.Stat(info.Defense)}";
            return $"{attribute} {race} · {stars} · {CardDetail.Stat(info.Attack)}{def}";
        }

        private static Color KindColor(uint type) =>
            (type & CardType.Spell) != 0 ? Ui.Hex("#1D9C73") : (type & CardType.Trap) != 0 ? Ui.Hex("#B0457F")
            : (type & CardType.ExtraDeckTypes) != 0 ? Ui.Hex("#5B4A9A") : Ui.Hex("#D07A35");
    }
}
